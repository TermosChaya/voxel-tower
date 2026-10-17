using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// One MeshInstance3D per 32³ chunk, and one more for the water in it. Vertices are in voxel units; this node scales them
/// to metres and centres the field on the origin, so the shaders see whole-voxel coordinates.
/// </summary>
public partial class WorldView : Node3D
{
    VoxelWorld _w;
    ShaderMaterial _mat, _waterMat;
    MeshInstance3D[] _inst, _water;
    readonly GreedyMesher _mesher = new();
    readonly byte[] _pad = new byte[34 * 34 * 34], _wet = new byte[34 * 34 * 34];
    readonly List<int> _batch = new();

    public double LastFullMeshMs { get; private set; }

    public void Init(VoxelWorld w)
    {
        _w = w;
        Position = new Vector3(w.OriginX, 0, w.OriginZ);
        Scale = Vector3.One * VoxelWorld.Vox;
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
        _waterMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
        _inst = new MeshInstance3D[w.ChunkCount];
        _water = new MeshInstance3D[w.ChunkCount];

        var sw = Stopwatch.StartNew();
        var results = new (MeshBuffers solid, MeshBuffers water)[w.ChunkCount];
        Parallel.For(0, w.ChunkCount, () => (new GreedyMesher(), new byte[34 * 34 * 34], new byte[34 * 34 * 34]), (ci, _, loc) =>
        {
            results[ci].solid = loc.Item1.MeshChunk(w, ci, loc.Item2, loc.Item3, out results[ci].water);
            return loc;
        }, _ => { });
        for (int ci = 0; ci < w.ChunkCount; ci++) Apply(ci, results[ci]);
        _batch.Clear();
        w.TakeDirty(_batch);
        _batch.Clear();
        LastFullMeshMs = sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>Re-meshes edited chunks; big edits are spread over all cores.</summary>
    public int RemeshDirty(int maxChunks = 64)
    {
        if (_w.DirtyCount == 0) return 0;
        _batch.Clear();
        _w.TakeDirty(_batch, maxChunks);
        var results = new (MeshBuffers solid, MeshBuffers water)[_batch.Count];
        if (_batch.Count <= 2)
            for (int i = 0; i < _batch.Count; i++) results[i].solid = _mesher.MeshChunk(_w, _batch[i], _pad, _wet, out results[i].water);
        else
            Parallel.For(0, _batch.Count, () => (new GreedyMesher(), new byte[34 * 34 * 34], new byte[34 * 34 * 34]), (i, _, loc) =>
            {
                results[i].solid = loc.Item1.MeshChunk(_w, _batch[i], loc.Item2, loc.Item3, out results[i].water);
                return loc;
            }, _ => { });
        for (int i = 0; i < _batch.Count; i++) Apply(_batch[i], results[i]);
        return _batch.Count;
    }

    void Apply(int ci, (MeshBuffers solid, MeshBuffers water) mb)
    {
        Put(_inst, ci, mb.solid, _mat, true);
        Put(_water, ci, mb.water, _waterMat, false);
    }

    void Put(MeshInstance3D[] slots, int ci, MeshBuffers mb, Material mat, bool shadows)
    {
        var mi = slots[ci];
        var mesh = GodotMesh.From(mb);
        if (mesh == null)
        {
            if (mi != null) { mi.QueueFree(); slots[ci] = null; }
            return;
        }
        if (mi == null)
        {
            mi = new MeshInstance3D { MaterialOverride = mat, CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(mi);
            slots[ci] = mi;
        }
        mi.Mesh = mesh;
    }

    // ---------------------------------------------------------------- the current

    /// <summary>Strongest current the flow map holds, voxels a second.</summary>
    const float FlowMax = 6;
    float[] _riverFlow;
    Image _flowImg;
    ImageTexture _flowTex;
    byte[] _flowBytes;
    double _flowAt = -99;
    bool _flowStill = true;

    /// <summary>
    /// The current over the valley for the water shader, one texel to a 4×4 patch: the river runs along its bed, quicker
    /// where it is narrow and slack by the banks; water let out runs on the way it spread (see <see cref="UpdateFlow"/>).
    /// </summary>
    public void InitFlow(Site site)
    {
        int W = _w.SX / Sim.FlowPatch, H = _w.SZ / Sim.FlowPatch;
        _riverFlow = new float[W * H * 2];
        if (site.RiverMid != null && site.WaterTop != null)
            for (int j = 0; j < H; j++)
                for (int i = 0; i < W; i++)
                {
                    int x = i * Sim.FlowPatch + 2, z = j * Sim.FlowPatch + 2;
                    if (site.WaterTop[z * _w.SX + x] == 0) continue;
                    float half = site.RiverHalfWidth[z], off = Math.Abs(x - site.RiverMid[z]) / (half + 6);
                    if (off > 1.3f) continue;   // a pond: still
                    float d = (site.RiverMid[Math.Min(_w.SZ - 1, z + 3)] - site.RiverMid[Math.Max(0, z - 3)]) / 6f;
                    float n = MathF.Sqrt(1 + d * d), speed = 64f / half * (1 - .6f * Math.Min(1, off * off));
                    _riverFlow[(j * W + i) * 2] = d / n * speed;
                    _riverFlow[(j * W + i) * 2 + 1] = speed / n;
                }
        _flowBytes = new byte[W * H * 2];
        FillFlow(null, 0);
        _flowImg = Image.CreateFromData(W, H, false, Image.Format.Rg8, _flowBytes);
        _flowTex = ImageTexture.CreateFromImage(_flowImg);
        _waterMat.SetShaderParameter("flow_tex", _flowTex);
        _waterMat.SetShaderParameter("map_size", new Vector2(_w.SX, _w.SZ));
        _waterMat.SetShaderParameter("flow_max", FlowMax);
    }

    /// <summary>The river's current, and where water has run lately the way it went: toward where it got to later.</summary>
    void FillFlow(float[] arrived, double now)
    {
        int W = _w.SX / Sim.FlowPatch, H = _w.SZ / Sim.FlowPatch;
        for (int j = 0; j < H; j++)
            for (int i = 0; i < W; i++)
            {
                int c = j * W + i;
                float fx = _riverFlow[c * 2], fz = _riverFlow[c * 2 + 1];
                double age = arrived == null ? 1e9 : now - arrived[c];
                if (age < 25)
                {
                    float At(int ii, int jj)
                    {
                        if (ii < 0 || jj < 0 || ii >= W || jj >= H) return arrived[c];
                        float a = arrived[jj * W + ii];
                        return now - a < 60 ? a : arrived[c];
                    }
                    float gx = At(i + 1, j) - At(i - 1, j), gz = At(i, j + 1) - At(i, j - 1), len = MathF.Sqrt(gx * gx + gz * gz);
                    if (len > 1e-4f)
                    {
                        float run = 4.5f * MathF.Exp(-(float)age / 10);
                        fx += gx / len * run; fz += gz / len * run;
                    }
                }
                _flowBytes[c * 2] = (byte)Math.Clamp(128 + fx / FlowMax * 127, 0, 255);
                _flowBytes[c * 2 + 1] = (byte)Math.Clamp(128 + fz / FlowMax * 127, 0, 255);
            }
    }

    /// <summary>Brings the current up to date a few times a second while water runs and for a while after it settles.</summary>
    public void UpdateFlow(Sim sim)
    {
        if (_flowTex == null || sim.WaterArrived == null || sim.Time - _flowAt < .25) return;
        bool running = sim.Time - sim.LastPour < 30;
        if (!running && _flowStill) return;
        _flowAt = sim.Time;
        _flowStill = !running;
        FillFlow(running ? sim.WaterArrived : null, sim.Time);
        _flowImg.SetData(_w.SX / Sim.FlowPatch, _w.SZ / Sim.FlowPatch, false, Image.Format.Rg8, _flowBytes);
        _flowTex.Update(_flowImg);
    }

    /// <summary>Chunks with water drawn in them.</summary>
    public int WaterChunks()
    {
        int n = 0;
        foreach (var mi in _water) if (mi != null) n++;
        return n;
    }

    public long Triangles()
    {
        long t = 0;
        foreach (var mi in _inst)
            if (mi?.Mesh is ArrayMesh am && am.GetSurfaceCount() > 0)
                t += am.SurfaceGetArrayIndexLen(0) / 3;
        return t;
    }
}
