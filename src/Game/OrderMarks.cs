using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Rings on the ground where the player sent its people: one at the place of each sent there, opening out and fading in
/// a second and a half, of the colour of the order — ember to go, red to strike, green to hunt, white to dig out or carry
/// a comrade, brass for the spot a commander is to hold; on the one struck at or hunted the ring goes with it. While some
/// are chosen, a faint line from each to where it was sent and a small ring there, till it gets there; about a chosen
/// commander, the ring of how far its word reaches, and its spot. Drawn over everything, a few at a time, in one mesh made
/// again each frame; in first person as well.
/// </summary>
public partial class OrderMarks : Node3D
{
    public enum Kind { Go, Strike, Hunt, Aid, Hold }

    struct Mark { public Vector3 At; public Unit On; public Kind Kind; public double Since; }

    Main _m;
    ImmediateMesh _mesh;
    MeshInstance3D _mi;
    readonly List<Mark> _marks = new();
    const double Life = 1.5;
    /// <summary>How many rings are on the ground now (the self-test asks).</summary>
    public int Count => _marks.Count;

    public static Color ColorOf(Kind k) => k switch
    {
        Kind.Strike => new Color(.92f, .16f, .1f),
        Kind.Hunt => new Color(.45f, .82f, .3f),
        Kind.Aid => new Color(.92f, .94f, 1f),
        Kind.Hold => new Color(.95f, .78f, .35f),
        _ => new Color(1f, .5f, .08f),
    };

    public void Init(Main m)
    {
        _m = m;
        _mesh = new ImmediateMesh();
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled, NoDepthTest = true, RenderPriority = 6,
        };
        _mi = new MeshInstance3D { Mesh = _mesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_mi);
    }

    /// <summary>A ring at a cell of the walking world (where one was sent).</summary>
    public void At(int cell, Kind k)
    {
        if (cell < 0) return;
        var (x, y, z) = _m.Sim.Nav.Feet(cell);
        _marks.Add(new Mark { At = new Vector3(x, y, z), Kind = k, Since = Time.GetTicksMsec() / 1000.0 });
    }

    /// <summary>A ring on one (struck at, hunted, dug out), going with it.</summary>
    public void On(Unit u, Kind k)
    {
        if (u == null) return;
        _marks.Add(new Mark { On = u, At = new Vector3(u.X, u.Y, u.Z), Kind = k, Since = Time.GetTicksMsec() / 1000.0 });
    }

    /// <summary>Rings at where each of these was sent (their own places in the ranks).</summary>
    public void Sent(IEnumerable<Unit> units, Kind k)
    {
        var seen = new HashSet<int>();
        foreach (var u in units) if (!u.Dead && u.Dest >= 0 && seen.Add(u.Dest)) At(u.Dest, k);
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("OrderMarks");
        _mesh.ClearSurfaces();
        // in first person too, where a commander sends its soldiers by looking; not in the bare eyes of the trailer
        if (_m?.Sim == null || _m.FP.On && _m.FP.Bare) return;
        double now = Time.GetTicksMsec() / 1000.0;
        _marks.RemoveAll(k => now - k.Since > Life || k.On is { Dead: true });
        var w = _m.World;
        bool any = false;
        void Begin() { if (!any) { _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles); any = true; } }
        foreach (var k in _marks)
        {
            float age = (float)((now - k.Since) / Life);
            var at = k.On != null ? new Vector3(k.On.X, k.On.Y, k.On.Z) : k.At;
            float r = (.45f + .45f * age) * VoxelWorld.PerMetre, a = (1 - age) * (1 - age) * .9f;
            Begin();
            Ring(w, at, r, r * .16f, new Color(ColorOf(k.Kind), a));
            if (age < .35f) Ring(w, at, r * .45f, r * .12f, new Color(ColorOf(k.Kind), a * (1 - age / .35f)));
        }
        // the chosen: a faint line to where each was sent, and a small ring there, till it is there
        int shown = 0;
        foreach (var u in _m.Selected)
        {
            if (u.Dead || u.Inside || shown++ > 40) continue;
            if (u.T == Kinds.Captain)
            {
                Begin();
                Ring(w, new Vector3(u.X, u.Y, u.Z), Sim.CaptainReach, .16f * VoxelWorld.PerMetre, new Color(ColorOf(Kind.Hold), .42f));
                if (u.Goal >= 0) { var (gx, gy, gz) = _m.Sim.Nav.Feet(u.Goal); Ring(w, new Vector3(gx, gy, gz), .9f * VoxelWorld.PerMetre, .2f * VoxelWorld.PerMetre, new Color(ColorOf(Kind.Hold), .7f)); }
            }
            if (!u.Hold || u.Dest < 0 || Apart(u.Cell, u.Dest) <= 1) continue;
            var (dx, dy, dz) = _m.Sim.Nav.Feet(u.Dest);
            var to = new Vector3(dx, dy, dz);
            Begin();
            Line(w, new Vector3(u.X, u.Y, u.Z), to, .07f * VoxelWorld.PerMetre, new Color(ColorOf(Kind.Go), .28f));
            Ring(w, to, .35f * VoxelWorld.PerMetre, .07f * VoxelWorld.PerMetre, new Color(ColorOf(Kind.Go), .45f));
        }
        if (any) _mesh.SurfaceEnd();
    }

    int Apart(int a, int b) { var nav = _m.Sim.Nav; return Math.Max(Math.Abs(nav.I(a) - nav.I(b)), Math.Abs(nav.K(a) - nav.K(b))); }

    /// <summary>A flat ring on the ground about a point (voxels), so wide, its band so thick.</summary>
    void Ring(VoxelWorld w, Vector3 at, float r, float band, Color c)
    {
        int n = Math.Clamp((int)(r * .9f), 20, 72);
        float y = w.WY(at.Y + .4f);
        for (int i = 0; i < n; i++)
        {
            float a0 = MathF.Tau * i / n, a1 = MathF.Tau * (i + 1) / n;
            var o0 = P(w, at, r + band / 2, a0, y); var o1 = P(w, at, r + band / 2, a1, y);
            var i0 = P(w, at, r - band / 2, a0, y); var i1 = P(w, at, r - band / 2, a1, y);
            Tri(o0, i0, o1, c); Tri(o1, i0, i1, c);
        }
    }

    static Vector3 P(VoxelWorld w, Vector3 at, float r, float a, float y) => new(w.WX(at.X + MathF.Cos(a) * r), y, w.WZ(at.Z + MathF.Sin(a) * r));

    /// <summary>A flat band on the ground from one point to another (voxels).</summary>
    void Line(VoxelWorld w, Vector3 a, Vector3 b, float half, Color c)
    {
        float dx = b.X - a.X, dz = b.Z - a.Z, d = MathF.Max(.01f, MathF.Sqrt(dx * dx + dz * dz));
        float sx = -dz / d * half, sz = dx / d * half;
        var p0 = new Vector3(w.WX(a.X + sx), w.WY(a.Y + .4f), w.WZ(a.Z + sz));
        var p1 = new Vector3(w.WX(a.X - sx), w.WY(a.Y + .4f), w.WZ(a.Z - sz));
        var p2 = new Vector3(w.WX(b.X + sx), w.WY(b.Y + .4f), w.WZ(b.Z + sz));
        var p3 = new Vector3(w.WX(b.X - sx), w.WY(b.Y + .4f), w.WZ(b.Z - sz));
        Tri(p0, p1, p2, new Color(c, c.A * .4f)); Tri(p2, p1, p3, c);
    }

    void Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
    {
        _mesh.SurfaceSetColor(col); _mesh.SurfaceAddVertex(a);
        _mesh.SurfaceSetColor(col); _mesh.SurfaceAddVertex(b);
        _mesh.SurfaceSetColor(col); _mesh.SurfaceAddVertex(c);
    }
}
