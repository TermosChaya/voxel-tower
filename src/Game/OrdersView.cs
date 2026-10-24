using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The work the player has marked out, drawn over the valley: planned masonry as pale ghosts in its own colours,
/// earth, rock and trees to be dug or felled washed red. Re-meshed a few times a second as the pawns get through it,
/// in play on the worker threads, Godot's meshes made of what they meshed here on the main thread when it is ready.
/// </summary>
public partial class OrdersView : Node3D
{
    VoxelWorld _layer, _w;
    MeshInstance3D[] _build, _dig;
    ShaderMaterial _buildMat, _digMat;
    readonly List<int> _batch = new();
    double _t, _next;
    /// <summary>Meshed on the workers and put in a frame or so on (in play), or at once (pictures, the self-test).</summary>
    public bool Async;
    Task<(MeshBuffers build, MeshBuffers dig)[]> _making;
    int[] _makingOf;
    static readonly ThreadLocal<Mesher> Meshers = new(() => new Mesher());

    public void Init(Sim sim)
    {
        _w = sim.World;
        _layer = sim.Orders.Layer;
        Position = new Vector3(_w.OriginX, 0, _w.OriginZ);
        Scale = Vector3.One * VoxelWorld.Vox;
        var shader = GD.Load<Shader>("res://shaders/orders.gdshader");
        _buildMat = new ShaderMaterial { Shader = shader };
        _buildMat.SetShaderParameter("tint", new Vector3(1f, .78f, .5f));
        _buildMat.SetShaderParameter("tint_mix", .22f);
        _buildMat.SetShaderParameter("alpha", .4f);
        _digMat = new ShaderMaterial { Shader = shader };
        _digMat.SetShaderParameter("tint", new Vector3(.82f, .1f, .07f));
        _digMat.SetShaderParameter("tint_mix", .7f);
        _digMat.SetShaderParameter("alpha", .55f);
        _digMat.SetShaderParameter("inflate", .07f);
        _digMat.SetShaderParameter("hatch", 1f);
        _build = new MeshInstance3D[_layer.ChunkCount];
        _dig = new MeshInstance3D[_layer.ChunkCount];
    }

    /// <summary>Re-meshes now whatever changed, without waiting for the next beat (screenshots).</summary>
    public void Flush()
    {
        if (_making != null) { Put(_makingOf, _making.Result); _making = null; }
        while (_layer != null && _layer.DirtyCount > 0) Remesh(false);
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("OrdersView");
        _t += delta;
        if (_making != null)
        {
            if (!_making.IsCompleted) return;
            Put(_makingOf, _making.Result);
            _making = null;
        }
        if (_layer == null || _layer.DirtyCount == 0 || _t < _next) return;
        _next = _t + .2;
        Remesh(Async);
    }

    public override void _ExitTree()
    {
        // what the workers were still meshing when the scene went; and the meshes hung up let go here, not by the
        // garbage collector on a thread of its own
        _making?.Wait();
        _making = null;
        foreach (var slots in new[] { _build, _dig })
            if (slots != null)
                foreach (var mi in slots)
                    if (mi != null && IsInstanceValid(mi)) mi.Mesh?.Dispose();
    }

    void Remesh(bool async)
    {
        _batch.Clear();
        _layer.TakeDirty(_batch, 96);
        var chunks = _batch.ToArray();
        if (async)
        {
            _makingOf = chunks;
            _making = Task.Run(() => Make(chunks));
        }
        else Put(chunks, Make(chunks));
    }

    (MeshBuffers build, MeshBuffers dig)[] Make(int[] chunks)
    {
        var made = new (MeshBuffers, MeshBuffers)[chunks.Length];
        Parallel.For(0, chunks.Length, i => made[i] = Meshers.Value.Mesh(_layer, _w, chunks[i]));
        return made;
    }

    void Put(int[] chunks, (MeshBuffers build, MeshBuffers dig)[] made)
    {
        for (int i = 0; i < chunks.Length; i++)
        {
            Apply(_build, chunks[i], made[i].build, _buildMat);
            Apply(_dig, chunks[i], made[i].dig, _digMat);
        }
    }

    /// <summary>Godot's mesh of what was meshed hung up (here, on the main thread), the borrowed buffers given back.</summary>
    void Apply(MeshInstance3D[] slots, int ci, MeshBuffers b, Material mat)
    {
        var mesh = GodotMesh.From(b);
        b?.Return();
        var mi = slots[ci];
        if (mesh == null)
        {
            if (mi != null) { var gone = mi.Mesh; mi.QueueFree(); slots[ci] = null; gone?.Dispose(); }
            return;
        }
        if (mi == null)
        {
            mi = new MeshInstance3D { MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(mi);
            slots[ci] = mi;
        }
        var old = mi.Mesh;
        mi.Mesh = mesh;
        old?.Dispose();
    }

    /// <summary>Splits a chunk of orders into what is to be laid and what is to be taken out, and meshes both.</summary>
    sealed class Mesher
    {
        const int P = VoxelWorld.CS + 2;
        readonly GreedyMesher _mesher = new() { Pooled = true };
        readonly byte[] _pad = new byte[P * P * P], _b = new byte[P * P * P], _d = new byte[P * P * P];

        public (MeshBuffers, MeshBuffers) Mesh(VoxelWorld layer, VoxelWorld w, int ci)
        {
            if (layer.ChunkData(ci) == null) return (null, null);
            layer.CopyPadded(ci, _pad);
            layer.ChunkCoords(ci, out int cx, out int cy, out int cz);
            int ox = cx * VoxelWorld.CS, oy = cy * VoxelWorld.CS, oz = cz * VoxelWorld.CS;
            bool anyB = false, anyD = false;
            for (int i = 0; i < _pad.Length; i++)
            {
                byte o = _pad[i];
                _b[i] = 0; _d[i] = 0;
                if (o == 0) continue;
                if (!Orders.IsRemove(o)) { _b[i] = o; anyB = true; continue; }
                // the red wash keeps a hint of what is under it
                int x = i % P - 1, z = i / P % P - 1, y = i / (P * P) - 1;
                byte m = w.Get(ox + x, oy + y, oz + z);
                // grass and flowers are drawn in blades: the wash shows the turf they stand in, not cubes of them
                if (Mat.IsSprig(m)) continue;
                _d[i] = m != 0 && m < Mat.Count ? m : Mat.Soil;
                anyD = true;
            }
            var build = anyB ? _mesher.Mesh(_b, VoxelWorld.CS, VoxelWorld.CS, VoxelWorld.CS, ox, oy, oz) : null;
            var dig = anyD ? _mesher.Mesh(_d, VoxelWorld.CS, VoxelWorld.CS, VoxelWorld.CS, ox, oy, oz) : null;
            return (build, dig);
        }
    }
}
