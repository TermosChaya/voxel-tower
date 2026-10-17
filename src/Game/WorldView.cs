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
