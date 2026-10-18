using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The areas set aside for gathering, drawn as a coloured ribbon along their edges, laid over the ground,
/// with a little post at each corner: green for a grove, grey for a quarry, brown for an earth cut, pale blue for a shelter.
/// Redrawn when an area is set or used up, and now and then as the ground under the ribbon is dug away.
/// </summary>
public partial class ZoneView : Node3D
{
    Sim _sim;
    VoxelWorld _w;
    int _version = -1, _rev = -1;
    double _t, _next;
    readonly Dictionary<int, MeshInstance3D> _meshes = new();
    readonly Dictionary<int, StandardMaterial3D> _mats = new();

    public static Color Tint(int kind, float alpha) => kind switch
    {
        Stockpile.Wood => new Color(.5f, .8f, .32f, alpha),
        Stockpile.Earth => new Color(.85f, .58f, .32f, alpha),
        Zone.Shelter => new Color(.5f, .68f, .98f, alpha),
        Zone.Sanctum => new Color(.7f, .42f, .92f, alpha),
        _ => new Color(.78f, .78f, .84f, alpha),
    };

    public void Init(Sim sim)
    {
        _sim = sim;
        _w = sim.World;
        Position = new Vector3(_w.OriginX, 0, _w.OriginZ);
        Scale = Vector3.One * VoxelWorld.Vox;
        foreach (int kind in new[] { Stockpile.Wood, Stockpile.Stone, Stockpile.Earth, Zone.Shelter, Zone.Sanctum })
            _mats[kind] = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoColor = Tint(kind, .9f) * new Color(1.25f, 1.25f, 1.25f, 1),
            };
    }

    public override void _Process(double delta)
    {
        if (_sim == null) return;
        _t += delta;
        bool changed = _sim.ZonesVersion != _version;
        if (!changed && (_w.Revision == _rev || _t < _next)) return;
        _version = _sim.ZonesVersion; _rev = _w.Revision; _next = _t + 1;
        var alive = new HashSet<int>();
        foreach (var z in _sim.Zones)
        {
            alive.Add(z.Id);
            if (!_meshes.TryGetValue(z.Id, out var mi))
            {
                mi = new MeshInstance3D { MaterialOverride = _mats[z.Kind], CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(mi);
                _meshes[z.Id] = mi;
            }
            mi.Mesh = Ribbon(z);
        }
        var gone = new List<int>();
        foreach (var id in _meshes.Keys) if (!alive.Contains(id)) gone.Add(id);
        foreach (var id in gone) { _meshes[id].QueueFree(); _meshes.Remove(id); }
    }

    /// <summary>The edge of an area as a band two and a half voxels wide lying on the ground, and a post at each corner.</summary>
    ArrayMesh Ribbon(Zone z)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const float w = 2.5f, lift = .4f;
        void Edge(int ax, int az, int bx, int bz)
        {
            int n = Math.Max(Math.Abs(bx - ax), Math.Abs(bz - az));
            float nx = -(bz - az) / (float)Math.Max(1, n), nz = (bx - ax) / (float)Math.Max(1, n);
            for (int i = 0; i < n; i += 2)
            {
                int x0 = ax + (bx - ax) * i / n, z0 = az + (bz - az) * i / n;
                int x1 = ax + (bx - ax) * Math.Min(n, i + 2) / n, z1 = az + (bz - az) * Math.Min(n, i + 2) / n;
                float y0 = _w.SurfAt(x0, z0) + lift, y1 = _w.SurfAt(x1, z1) + lift;
                var p0 = new Vector3(x0 + .5f, y0, z0 + .5f); var p1 = new Vector3(x1 + .5f, y1, z1 + .5f);
                var o = new Vector3(nx, 0, nz) * w;
                Quad(st, p0, p1, p1 + o, p0 + o);
            }
        }
        Edge(z.X0, z.Z0, z.X1, z.Z0);
        Edge(z.X1, z.Z0, z.X1, z.Z1);
        Edge(z.X1, z.Z1, z.X0, z.Z1);
        Edge(z.X0, z.Z1, z.X0, z.Z0);
        foreach (var (x, zz) in new[] { (z.X0, z.Z0), (z.X1, z.Z0), (z.X0, z.Z1), (z.X1, z.Z1) })
        {
            float y = _w.SurfAt(x, zz);
            Post(st, new Vector3(x + .5f, y, zz + .5f), 1.2f, 10f);
        }
        return st.Commit();
    }

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
    }

    static void Post(SurfaceTool st, Vector3 foot, float r, float h)
    {
        var top = foot + new Vector3(0, h, 0);
        Vector3[] c = { new(-r, 0, -r), new(r, 0, -r), new(r, 0, r), new(-r, 0, r) };
        for (int i = 0; i < 4; i++)
        {
            var a = c[i]; var b = c[(i + 1) % 4];
            Quad(st, foot + a, foot + b, top + b, top + a);
        }
        Quad(st, top + c[0], top + c[1], top + c[2], top + c[3]);
    }
}
