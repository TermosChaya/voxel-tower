using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// One MeshInstance3D per 32³ chunk, one more for the water in it, and one for its shadow: the chunk's solid voxels as
/// the fewest faces that hold their shape (GreedyMesher.MeshChunk), drawn into the sun's shadow only, so the shadow
/// pass draws a few large faces where the picture draws many. From far off (a voxel under 1,7 pixels on the screen) the
/// valley is drawn a level coarser instead: a mesh and a shadow for every block of 2×2×2 chunks, every 2×2×2 voxels one
/// cell (GreedyMesher.MeshCoarse), a quarter of the faces in an eighth of the draws; a block whose chunks changed is made
/// again while the coarse level is drawn, or when it comes on. Vertices are in voxel units; this node scales them
/// to metres and centres the field on the origin, so the shaders see whole-voxel coordinates.
/// </summary>
public partial class WorldView : Node3D
{
    VoxelWorld _w;
    ShaderMaterial _mat, _waterMat;
    MeshInstance3D[] _inst, _water, _shade;
    // the coarse level: a block of 2×2×2 chunks each, its picture and its shadow; the blocks to make again
    MeshInstance3D[] _far, _farShade;
    int _rx, _ry, _rz;
    readonly HashSet<int> _farDirty = new();
    // the chunks whose shadow is to be made again, a little after the chunk itself
    readonly HashSet<int> _shadeDirty = new();
    readonly List<int> _shadeBatch = new();
    readonly List<int> _farBatch = new();
    bool _coarse, _showSolid = true;
    /// <summary>Whether the valley is drawn at the coarse level now.</summary>
    public bool Coarse => _coarse;
    readonly GreedyMesher _mesher = new();
    readonly byte[] _pad = new byte[34 * 34 * 34], _wet = new byte[34 * 34 * 34];
    readonly List<int> _batch = new();

    public double LastFullMeshMs { get; private set; }
    /// <summary>The tufts, the flowers and the sward, drawn in blades over the chunks.</summary>
    public GrassView Grass { get; private set; }

    public void Init(VoxelWorld w)
    {
        _w = w;
        Position = new Vector3(w.OriginX, 0, w.OriginZ);
        Scale = Vector3.One * VoxelWorld.Vox;
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
        _waterMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
        _inst = new MeshInstance3D[w.ChunkCount];
        _water = new MeshInstance3D[w.ChunkCount];
        _shade = new MeshInstance3D[w.ChunkCount];
        Grass = new GrassView();
        AddChild(Grass);
        Grass.Init(w);

        var sw = Stopwatch.StartNew();
        var results = new (MeshBuffers solid, MeshBuffers water, Sward sward, MeshBuffers shadow)[w.ChunkCount];
        Parallel.For(0, w.ChunkCount, () => (new GreedyMesher(), new byte[34 * 34 * 34], new byte[34 * 34 * 34]), (ci, _, loc) =>
        {
            results[ci].solid = loc.Item1.MeshChunk(w, ci, loc.Item2, loc.Item3, out results[ci].water, out results[ci].sward, out results[ci].shadow);
            return loc;
        }, _ => { });
        for (int ci = 0; ci < w.ChunkCount; ci++) Apply(ci, results[ci]);
        _batch.Clear();
        w.TakeDirty(_batch);
        _batch.Clear();
        _rx = (w.CX + 1) / 2; _ry = (w.CY + 1) / 2; _rz = (w.CZ + 1) / 2;
        _far = new MeshInstance3D[_rx * _ry * _rz];
        _farShade = new MeshInstance3D[_far.Length];
        // the coarse level is wanted only from far off: made off the main thread, put in a few blocks a frame after
        _farMade = Task.Run(() =>
        {
            var made = new (MeshBuffers solid, MeshBuffers shadow)[_far.Length];
            Parallel.For(0, made.Length, () => (new GreedyMesher(), new byte[34 * 34 * 34]), (ri, _, loc) =>
            {
                made[ri].solid = loc.Item1.MeshCoarse(w, ri % _rx, ri / (_rx * _rz), ri / _rx % _rz, loc.Item2, out made[ri].shadow);
                return loc;
            }, _ => { });
            return made;
        });
        LastFullMeshMs = sw.Elapsed.TotalMilliseconds;
    }

    // the coarse level as the thread made it, and how much of it is put in so far
    Task<(MeshBuffers solid, MeshBuffers shadow)[]> _farMade;
    int _farPut;
    bool FarReady => _farMade == null;

    int RegionOf(int ci)
    {
        _w.ChunkCoords(ci, out int cx, out int cy, out int cz);
        return ((cy >> 1) * _rz + (cz >> 1)) * _rx + (cx >> 1);
    }

    /// <summary>Makes these blocks of the coarse level again, over all the cores when there are more than two.</summary>
    void MeshFar(List<int> blocks)
    {
        Prof.Count("far blocks", blocks.Count);
        var made = new (MeshBuffers solid, MeshBuffers shadow)[blocks.Count];
        void One(GreedyMesher m, byte[] grid, int i)
        {
            int ri = blocks[i];
            made[i].solid = m.MeshCoarse(_w, ri % _rx, ri / (_rx * _rz), ri / _rx % _rz, grid, out made[i].shadow);
        }
        if (blocks.Count <= 2) for (int i = 0; i < blocks.Count; i++) One(_mesher, _pad, i);
        else Parallel.For(0, blocks.Count, () => (new GreedyMesher(), new byte[34 * 34 * 34]), (i, _, loc) => { One(loc.Item1, loc.Item2, i); return loc; }, _ => { });
        for (int i = 0; i < blocks.Count; i++)
        {
            Put(_far, blocks[i], made[i].solid, _mat, GeometryInstance3D.ShadowCastingSetting.Off, _showSolid && _coarse);
            Put(_farShade, blocks[i], made[i].shadow, null, GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, _showSolid && _coarse);
        }
    }

    /// <summary>
    /// The level the valley is drawn at, by how many pixels a voxel has on the screen (the coarse one under 1,7, back
    /// over 1,9); always the fine one through someone's eyes. Every block changed is made again before the coarse comes on.
    /// </summary>
    public void ChooseLevel(float pxPerVoxel, bool close)
    {
        bool want = FarReady && !close && (_coarse ? pxPerVoxel < 1.9f : pxPerVoxel < 1.7f);
        if (want == _coarse) return;
        _coarse = want;
        if (_coarse && _farDirty.Count > 0)
        {
            _farBatch.Clear();
            _farBatch.AddRange(_farDirty);
            _farDirty.Clear();
            MeshFar(_farBatch);
        }
        ShowLevels();
    }

    void ShowLevels()
    {
        foreach (var mi in _inst) if (mi != null) mi.Visible = _showSolid && !_coarse;
        foreach (var mi in _shade) if (mi != null) mi.Visible = _showSolid && !_coarse;
        foreach (var mi in _far) if (mi != null) mi.Visible = _showSolid && _coarse;
        foreach (var mi in _farShade) if (mi != null) mi.Visible = _showSolid && _coarse;
    }

    /// <summary>The ground and the water shown or not (--perf-without measures what each costs).</summary>
    public void ShowParts(bool solid, bool water)
    {
        _showSolid = solid;
        ShowLevels();
        foreach (var mi in _water) if (mi != null) mi.Visible = water;
    }

    /// <summary>Re-meshes edited chunks; big edits are spread over all cores.</summary>
    public int RemeshDirty(int maxChunks = 64)
    {
        if (_w.DirtyCount == 0) return 0;
        _batch.Clear();
        _w.TakeDirty(_batch, maxChunks);
        Prof.Count("chunks", _batch.Count);
        // the picture at once; its shadow a little after (RemeshShadows), so a great blast is not made twice in one frame
        var results = new (MeshBuffers solid, MeshBuffers water, Sward sward)[_batch.Count];
        if (_batch.Count <= 2)
            for (int i = 0; i < _batch.Count; i++) results[i].solid = _mesher.MeshChunk(_w, _batch[i], _pad, _wet, out results[i].water, out results[i].sward);
        else
            Parallel.For(0, _batch.Count, () => (new GreedyMesher(), new byte[34 * 34 * 34], new byte[34 * 34 * 34]), (i, _, loc) =>
            {
                results[i].solid = loc.Item1.MeshChunk(_w, _batch[i], loc.Item2, loc.Item3, out results[i].water, out results[i].sward);
                return loc;
            }, _ => { });
        for (int i = 0; i < _batch.Count; i++)
        {
            int ci = _batch[i];
            Put(_inst, ci, results[i].solid, _mat, GeometryInstance3D.ShadowCastingSetting.Off, _showSolid && !_coarse);
            Put(_water, ci, results[i].water, _waterMat, GeometryInstance3D.ShadowCastingSetting.Off, true);
            Grass.Take(ci, results[i].sward);
            _shadeDirty.Add(ci);
            _farDirty.Add(RegionOf(ci));
        }
        return _batch.Count;
    }

    /// <summary>The shadows of the chunks made again lately, eight a frame at most.</summary>
    public void RemeshShadows()
    {
        if (_shadeDirty.Count == 0) return;
        _shadeBatch.Clear();
        foreach (int ci in _shadeDirty) { _shadeBatch.Add(ci); if (_shadeBatch.Count >= 8) break; }
        foreach (int ci in _shadeBatch) _shadeDirty.Remove(ci);
        Prof.Count("shadows", _shadeBatch.Count);
        var made = new MeshBuffers[_shadeBatch.Count];
        if (made.Length <= 2) for (int i = 0; i < made.Length; i++) made[i] = _mesher.MeshShadow(_w, _shadeBatch[i], _pad);
        else Parallel.For(0, made.Length, () => (new GreedyMesher(), new byte[34 * 34 * 34]), (i, _, loc) => { made[i] = loc.Item1.MeshShadow(_w, _shadeBatch[i], loc.Item2); return loc; }, _ => { });
        for (int i = 0; i < made.Length; i++)
            Put(_shade, _shadeBatch[i], made[i], null, GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, _showSolid && !_coarse);
    }

    /// <summary>
    /// A few of the changed blocks of the coarse level made again while it is drawn; while it is not they wait, all made
    /// at once when it comes on (<see cref="ChooseLevel"/>), so the work of the near view is never slowed by the far one.
    /// </summary>
    public void RemeshFar()
    {
        // the first making of the coarse level, done off the main thread, put in a few dozen blocks a frame
        if (_farMade != null)
        {
            if (!_farMade.IsCompleted) return;
            var made = _farMade.Result;
            for (int n = 0; n < 48 && _farPut < made.Length; n++, _farPut++)
            {
                Put(_far, _farPut, made[_farPut].solid, _mat, GeometryInstance3D.ShadowCastingSetting.Off, _showSolid && _coarse);
                Put(_farShade, _farPut, made[_farPut].shadow, null, GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, _showSolid && _coarse);
            }
            if (_farPut >= made.Length) _farMade = null;
            return;
        }
        if (!_coarse || _farDirty.Count == 0) return;
        _farBatch.Clear();
        const int most = 4;
        foreach (int ri in _farDirty) { _farBatch.Add(ri); if (_farBatch.Count >= most) break; }
        foreach (int ri in _farBatch) _farDirty.Remove(ri);
        MeshFar(_farBatch);
    }

    void Apply(int ci, (MeshBuffers solid, MeshBuffers water, Sward sward, MeshBuffers shadow) mb)
    {
        Put(_inst, ci, mb.solid, _mat, GeometryInstance3D.ShadowCastingSetting.Off, _showSolid && !_coarse);
        Put(_water, ci, mb.water, _waterMat, GeometryInstance3D.ShadowCastingSetting.Off, true);
        Put(_shade, ci, mb.shadow, null, GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, _showSolid && !_coarse);
        Grass.Take(ci, mb.sward);
    }

    void Put(MeshInstance3D[] slots, int ci, MeshBuffers mb, Material mat, GeometryInstance3D.ShadowCastingSetting shadows, bool visible)
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
            mi = new MeshInstance3D { MaterialOverride = mat, CastShadow = shadows, Visible = visible };
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
