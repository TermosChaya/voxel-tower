using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// One MeshInstance3D per 32³ chunk. Vertices are in voxel units; this node scales them to metres
/// and centres the field on the origin, so the shader sees whole-voxel coordinates.
/// </summary>
public partial class WorldView : Node3D
{
    VoxelWorld _w;
    ShaderMaterial _mat;
    MeshInstance3D[] _inst;
    readonly GreedyMesher _mesher = new();
    readonly byte[] _pad = new byte[34 * 34 * 34];
    readonly List<int> _batch = new();

    public double LastFullMeshMs { get; private set; }

    public void Init(VoxelWorld w)
    {
        _w = w;
        Position = new Vector3(w.OriginX, 0, w.OriginZ);
        Scale = Vector3.One * VoxelWorld.Vox;
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
        _inst = new MeshInstance3D[w.ChunkCount];

        var sw = Stopwatch.StartNew();
        var results = new MeshBuffers[w.ChunkCount];
        Parallel.For(0, w.ChunkCount, () => (new GreedyMesher(), new byte[34 * 34 * 34]), (ci, _, loc) =>
        {
            results[ci] = loc.Item1.MeshChunk(w, ci, loc.Item2);
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
        var results = new MeshBuffers[_batch.Count];
        if (_batch.Count <= 2)
            for (int i = 0; i < _batch.Count; i++) results[i] = _mesher.MeshChunk(_w, _batch[i], _pad);
        else
            Parallel.For(0, _batch.Count, () => (new GreedyMesher(), new byte[34 * 34 * 34]), (i, _, loc) =>
            {
                results[i] = loc.Item1.MeshChunk(_w, _batch[i], loc.Item2);
                return loc;
            }, _ => { });
        for (int i = 0; i < _batch.Count; i++) Apply(_batch[i], results[i]);
        return _batch.Count;
    }

    void Apply(int ci, MeshBuffers mb)
    {
        var mi = _inst[ci];
        var mesh = GodotMesh.From(mb);
        if (mesh == null)
        {
            if (mi != null) { mi.QueueFree(); _inst[ci] = null; }
            return;
        }
        if (mi == null)
        {
            mi = new MeshInstance3D { MaterialOverride = _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
            AddChild(mi);
            _inst[ci] = mi;
        }
        mi.Mesh = mesh;
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
