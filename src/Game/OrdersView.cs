using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The work the player has marked out, drawn over the valley: planned masonry as pale ghosts in its own colours,
/// earth, rock and trees to be dug or felled washed red. Re-meshed a few times a second as the pawns get through it.
/// </summary>
public partial class OrdersView : Node3D
{
    VoxelWorld _layer, _w;
    MeshInstance3D[] _build, _dig;
    ShaderMaterial _buildMat, _digMat;
    readonly List<int> _batch = new();
    double _t, _next;

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
    public void Flush() { _next = 0; _Process(0); }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("OrdersView");
        _t += delta;
        if (_layer == null || _layer.DirtyCount == 0 || _t < _next) return;
        _next = _t + .2;
        _batch.Clear();
        _layer.TakeDirty(_batch, 96);
        var results = new (MeshBuffers build, MeshBuffers dig)[_batch.Count];
        Parallel.For(0, _batch.Count, () => new Mesher(), (i, _, m) => { results[i] = m.Mesh(_layer, _w, _batch[i]); return m; }, _ => { });
        for (int i = 0; i < _batch.Count; i++)
        {
            Apply(_build, _batch[i], results[i].build, _buildMat);
            Apply(_dig, _batch[i], results[i].dig, _digMat);
        }
    }

    void Apply(MeshInstance3D[] slots, int ci, MeshBuffers mb, Material mat)
    {
        var mesh = GodotMesh.From(mb);
        var mi = slots[ci];
        if (mesh == null)
        {
            if (mi != null) { mi.QueueFree(); slots[ci] = null; }
            return;
        }
        if (mi == null)
        {
            mi = new MeshInstance3D { MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(mi);
            slots[ci] = mi;
        }
        mi.Mesh = mesh;
    }

    /// <summary>Splits a chunk of orders into what is to be laid and what is to be taken out, and meshes both.</summary>
    sealed class Mesher
    {
        const int P = VoxelWorld.CS + 2;
        readonly GreedyMesher _mesher = new();
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
