using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Where the horde's warbands will go: for every band a dashed line on the ground in its own colour, from where it comes
/// in (or from its leader, once it is in the valley) to the citadel, with arrowheads along it; red rings where it will
/// hack through something, amber ones where it will raise a ladder, lay a bridge or fill a ditch; and a tag over its
/// head with its name and strength. One line a band, not a line a raider.
/// </summary>
public partial class TrailView : Node3D
{
    sealed class Drawn
    {
        public MeshInstance3D Line;
        public Label3D Tag;
        public readonly List<MeshInstance3D> Rings = new();
        public int Rev = -1;
    }

    /// <summary>The bands' colours: ember, blood, rust, amber, plum.</summary>
    public static readonly Color[] Palette =
    {
        new(.92f, .3f, .08f), new(.8f, .08f, .06f), new(.9f, .5f, .1f), new(.66f, .1f, .3f), new(.9f, .7f, .16f),
    };

    public static Color ColorOf(Warband b) => Palette[(b.Id - 1) % Palette.Length];

    Main _m;
    readonly Dictionary<Warband, Drawn> _drawn = new();
    readonly List<Warband> _gone = new();
    StandardMaterial3D _lineMat, _breach, _works;
    TorusMesh _ring;

    public void Init(Main m)
    {
        _m = m;
        _lineMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = 2,
        };
        _ring = new TorusMesh { InnerRadius = .8f, OuterRadius = 1f, Rings = 24, RingSegments = 4 };
        StandardMaterial3D Ring(Color c) => new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = c, NoDepthTest = true, RenderPriority = 5 };
        _breach = Ring(new Color(1f, .16f, .1f));
        _works = Ring(new Color(1f, .7f, .2f));
    }

    public override void _Process(double delta)
    {
        if (_m?.Sim == null) return;
        var sim = _m.Sim;
        Visible = sim.WantTrails;
        if (!Visible) return;
        foreach (var b in sim.Bands)
        {
            if (!_drawn.TryGetValue(b, out var d)) _drawn[b] = d = Make(b);
            if (d.Rev != b.TrailRev) { d.Rev = b.TrailRev; Rebuild(b, d); }
            Place(b, d);
        }
        _gone.Clear();
        foreach (var b in _drawn.Keys) if (!sim.Bands.Contains(b)) _gone.Add(b);
        foreach (var b in _gone)
        {
            var d = _drawn[b];
            d.Line.QueueFree(); d.Tag.QueueFree();
            foreach (var r in d.Rings) r.QueueFree();
            _drawn.Remove(b);
        }
    }

    Drawn Make(Warband b)
    {
        var d = new Drawn
        {
            Line = new MeshInstance3D { MaterialOverride = _lineMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off },
            Tag = new Label3D
            {
                Font = Ornament.SerifBold, FontSize = 30, PixelSize = .0011f, FixedSize = true, OutlineSize = 10, OutlineModulate = new Color(0, 0, 0, .9f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, Modulate = ColorOf(b).Lightened(.35f), RenderPriority = 6,
            },
        };
        AddChild(d.Line);
        AddChild(d.Tag);
        return d;
    }

    /// <summary>The tag hangs over the band's leader, or over where it will come in.</summary>
    void Place(Warband b, Drawn d)
    {
        var w = _m.World;
        var nav = _m.Sim.Nav;
        Vector3 at;
        if (b.Leader is { Dead: false } l) at = new Vector3(w.WX(l.X), w.WY(l.Y + l.T.BodyH + 9), w.WZ(l.Z));
        else if (b.Entry >= 0) { var (x, y, z) = nav.Feet(b.Entry); at = new Vector3(w.WX(x), w.WY(y + 22), w.WZ(z)); }
        else { d.Tag.Visible = false; return; }
        d.Tag.Visible = true;
        d.Tag.Position = at;
        string what = b.State switch
        {
            BandState.Coming => "идёт",
            BandState.Forming => "собирается",
            BandState.Rally => "собирается у стен",
            BandState.Storm => "на штурме",
            _ => b.Target != null ? "на " + b.Target.Name.ToLowerInvariant() : "в походе",
        };
        int n = b.State == BandState.Coming ? b.Size : b.Strength;
        d.Tag.Text = $"{b.Name} · {n} · {what}";
    }

    void Rebuild(Warband b, Drawn d)
    {
        var w = _m.World;
        var nav = _m.Sim.Nav;
        var color = ColorOf(b);
        // the line: through the middles of the cells, a little over the ground, smoothed into curves
        var pts = new List<Vector3>();
        foreach (int c in b.Trail)
        {
            var (x, y, z) = nav.Feet(c);
            pts.Add(new Vector3(x, y + 1.2f, z));
        }
        for (int pass = 0; pass < 2 && pts.Count > 2; pass++) pts = Chaikin(pts);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const float Half = 1.1f, Dash = 7, Gap = 4, ArrowEvery = 44;
        float run = 0, nextArrow = ArrowEvery * .5f;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i]; var e = pts[i + 1];
            var dir = e - a;
            float len = dir.Length();
            if (len < 1e-3f) continue;
            dir /= len;
            var side = new Vector3(-dir.Z, 0, dir.X) * Half;
            // fades in from the band's end of the line
            float alpha = Math.Clamp(.35f + run / 80f, .35f, .9f);
            var col = new Color(color, alpha);
            for (float t = 0; t < len;)
            {
                float phase = (run + t) % (Dash + Gap), step = Math.Min(len - t, phase < Dash ? Dash - phase : Dash + Gap - phase);
                if (phase < Dash) Quad(st, w, a + dir * t - side, a + dir * t + side, a + dir * (t + step) + side, a + dir * (t + step) - side, col);
                t += Math.Max(step, .01f);
            }
            run += len;
            if (run >= nextArrow)
            {
                nextArrow += ArrowEvery;
                var tip = e + dir * 2.5f + Vector3.Up * .1f;
                Tri(st, w, e - side * 2.4f, tip, e + side * 2.4f, new Color(color, .95f));
            }
        }
        d.Line.Mesh = run > 0 ? st.Commit() : null;
        // rings where it hacks through or builds
        foreach (var r in d.Rings) r.QueueFree();
        d.Rings.Clear();
        int shown = 0;
        foreach (var (cell, works, carve) in b.Marks)
        {
            if (shown++ >= 10) break;
            var (x, y, z) = nav.Feet(cell);
            var ring = new MeshInstance3D
            {
                Mesh = _ring,
                MaterialOverride = carve ? _breach : _works,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(w.WX(x), w.WY(y + 1.5f), w.WZ(z)),
                Scale = new Vector3(4.5f, 2f, 4.5f) * VoxelWorld.Vox,
            };
            AddChild(ring);
            d.Rings.Add(ring);
        }
    }

    static List<Vector3> Chaikin(List<Vector3> p)
    {
        var o = new List<Vector3>(p.Count * 2) { p[0] };
        for (int i = 0; i + 1 < p.Count; i++)
        {
            o.Add(p[i].Lerp(p[i + 1], .25f));
            o.Add(p[i].Lerp(p[i + 1], .75f));
        }
        o.Add(p[^1]);
        return o;
    }

    static Vector3 W(VoxelWorld w, Vector3 v) => new(w.WX(v.X), w.WY(v.Y), w.WZ(v.Z));

    static void Quad(SurfaceTool st, VoxelWorld w, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
    {
        Tri(st, w, a, b, c, col);
        Tri(st, w, a, c, d, col);
    }

    static void Tri(SurfaceTool st, VoxelWorld w, Vector3 a, Vector3 b, Vector3 c, Color col)
    {
        st.SetColor(col); st.AddVertex(W(w, a));
        st.SetColor(col); st.AddVertex(W(w, b));
        st.SetColor(col); st.AddVertex(W(w, c));
    }
}
