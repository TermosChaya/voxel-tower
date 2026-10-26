using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

/// <summary>
/// Those who walk the map's land, as figures on it (AtlasView.cs): a hero of ours in the saddle under a blue banner, his
/// army not shown (it is seen once he goes into a valley); a scouting party a rider without a banner; an army of the
/// horde its warlord, or an orc, under a red banner, and only where ours see it. Each walks where its party is, its legs
/// going while it walks, turned the way it goes; a number by it says how many there are. The hero chosen stands in a gold
/// ring; the way he would walk is laid out before him in marks, green as far as he gets by nightfall, ochre beyond,
/// with the hours to get there at its end.
/// </summary>
public partial class AtlasFigures : Node3D
{
    AtlasView _view;
    static Campaign C => Main.Atlas;
    readonly Dictionary<MapParty, Token> _tokens = new();
    MeshInstance3D _ring;
    MultiMeshInstance3D _marks;
    Node3D _flag;
    Label3D _flagText;
    float _scale = .05f, _spacing = 80, _refH;
    /// <summary>The party of ours chosen on the map; the way shown and where it ends.</summary>
    public MapParty Chosen;
    public List<(float X, float Z)> Shown { get; private set; }
    public (float X, float Z) ShownTo { get; private set; }
    /// <summary>The figures drawn on the last frame, ours and the horde's (for the self-test).</summary>
    public int OursDrawn, TheirsDrawn;

    static readonly Color Ours = new("3f68b0"), Theirs = new("a8321f"), Gold = new("d9b25a"), Today = new("7fc25a"), Later = new("c9933f");

    public void Init(AtlasView view)
    {
        _view = view;
        _ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = .8f, OuterRadius = 1f, Rings = 24, RingSegments = 6 }, Layers = AtlasView.Layer, Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Gold, EmissionEnabled = true, Emission = Gold, EmissionEnergyMultiplier = .6f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
        AddChild(_ring);
        var mark = new CylinderMesh { TopRadius = .5f, BottomRadius = .5f, Height = .12f, RadialSegments = 10, Rings = 1 };
        _marks = new MultiMeshInstance3D { Layers = AtlasView.Layer, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mark },
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
        AddChild(_marks);
        _flag = Banner(Gold, 1.6f);
        _flag.Visible = false;
        AddChild(_flag);
        _flagText = new Label3D { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 30, OutlineSize = 9, Modulate = new Color("f3e6c4"),
            OutlineModulate = new Color(0, 0, 0, .8f), Layers = AtlasView.Layer, NoDepthTest = true, PixelSize = .0006f, FixedSize = true, Font = Ornament.SerifBold };
        _flag.AddChild(_flagText);
    }

    /// <summary>The scale of the figures to the land: a hero about a quarter as tall as two cells' middles are apart.</summary>
    void Measure()
    {
        if (_view.Atlas == null) return;
        _spacing = AtlasWalk.Spacing(_view.Atlas);
        var a = _view.ToScene(0, 0); var b = _view.ToScene(_spacing, 0);
        float across = new Vector2(b.X - a.X, b.Z - a.Z).Length();
        // a hero in the saddle a fifth as tall as two cells' middles are apart; the rest by him
        if (_refH <= 0) _refH = Height(Figure.For(Bodies.HeroRider(0)));
        _scale = across * .2f / Math.Max(1, _refH);
    }

    // ------------------------------------------------------------ where each is

    /// <summary>Where a party is on the land, and the way it faces: walking the land, where its feet are; walking from cell to cell, between their middles.</summary>
    public (float x, float z, float facing) Spot(MapParty p)
    {
        var land = _view.Atlas;
        if (p.Trail == null && p.Moving && land != null)
        {
            var (ax, az) = land.Center(C.CellOf(p));
            var (bx, bz) = land.Center(p.Path[0]);
            float t = (float)C.StepShare(p);
            return (ax + (bx - ax) * t, az + (bz - az) * t, MathF.Atan2(bz - az, bx - ax));
        }
        if (p.Placed) return (p.AtX, p.AtZ, p.Facing);
        if (land == null) return (0, 0, 0);
        var (cx, cz) = land.Center(C.CellOf(p));
        return (cx, cz, 0);
    }

    /// <summary>Whether ours see a party of the horde: in a cell seen, or stepping into one.</summary>
    bool Visible_(MapParty p) => p.Owner == Holder.Ours || _view.Seen(C.CellOf(p)) || p.Moving && _view.Seen(p.Path[0]);

    /// <summary>The party of ours whose figure is nearest a spot of the land, within reach of a click; null if none.</summary>
    public MapParty At(Vector2 spot)
    {
        MapParty best = null; float bd = _spacing * .16f;
        foreach (var p in C.Parties)
        {
            if (p.Owner != Holder.Ours) continue;
            var (x, z, _) = Spot(p);
            float d = new Vector2(x - spot.X, z - spot.Y).Length();
            if (d < bd) { bd = d; best = p; }
        }
        return best;
    }

    // ------------------------------------------------------------ the way shown

    /// <summary>The way a party would walk laid out on the land: marks, green as far as it gets by nightfall; the time at its end.</summary>
    public void Show(MapParty p, List<(float X, float Z)> way, double seconds)
    {
        Measure();
        Shown = way;
        ShownTo = way[^1];
        double light = Math.Max(0, DayClock.DayLength - C.Clock.At);
        var spots = new List<(Vector3 at, Color col)>();
        float step = _spacing / 12, left = step * .6f;
        double spent = 0;
        for (int i = 1; i < way.Count; i++)
        {
            var (ax, az) = way[i - 1]; var (bx, bz) = way[i];
            float len = new Vector2(bx - ax, bz - az).Length();
            double per = len > 0 ? C.WaySeconds(p, new List<(float X, float Z)> { way[i - 1], way[i] }) / len : 0;
            float d = left;
            for (; d < len; d += step)
            {
                double at = spent + per * d;
                float x = ax + (bx - ax) * d / len, z = az + (bz - az) * d / len;
                spots.Add((_view.ToScene(x, z) - _view.Position, at <= light ? Today : Later));
            }
            left = d - len;
            spent += per * len;
        }
        var mm = _marks.Multimesh;
        mm.InstanceCount = spots.Count;
        float r = _scale * _refH * .09f;
        for (int i = 0; i < spots.Count; i++)
        {
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(r, 1, r)), spots[i].at + Vector3.Up * _scale * .5f));
            mm.SetInstanceColor(i, spots[i].col);
        }
        _marks.Visible = true;
        _flag.Visible = true;
        _flag.Position = _view.ToScene(ShownTo.X, ShownTo.Z) - _view.Position;
        _flag.Scale = Vector3.One * _scale * _refH * .7f;
        _flagText.Text = Hours(seconds);
        _flagText.Position = new Vector3(0, 2.4f, 0);
    }

    public void Hide()
    {
        Shown = null;
        _marks.Visible = false;
        _flag.Visible = false;
    }

    /// <summary>«5 ч», «1 д 4 ч»: the map's hours of a walk.</summary>
    public static string Hours(double seconds)
    {
        int h = (int)Math.Ceiling(seconds / MapPath.HourSeconds);
        return h >= 24 ? $"{h / 24} д {h % 24} ч" : $"{Math.Max(1, h)} ч";
    }

    // ------------------------------------------------------------ the figures

    public override void _Process(double delta)
    {
        if (C == null || _view.Atlas == null || !_view.IsOpen) return;
        Measure();
        foreach (var t in _tokens.Values) t.Seen = false;
        OursDrawn = TheirsDrawn = 0;
        foreach (var p in C.Parties)
        {
            if (!Visible_(p)) continue;
            if (!_tokens.TryGetValue(p, out var t)) { t = Make(p); _tokens[p] = t; AddChild(t); }
            t.Seen = true;
            var (x, z, facing) = Spot(p);
            var at = _view.ToScene(x, z) - _view.Position;
            bool walking = p.Walking || p.Moving;
            // smoothly to where it is, turning toward the way it goes
            t.Position = t.Position.DistanceTo(at) > _scale * 60 ? at : t.Position.Lerp(at, (float)Math.Min(1, delta * 12));
            t.Face = LerpAngle(t.Face, facing, (float)Math.Min(1, delta * 8));
            t.Basis = new Basis(Vector3.Up, MathF.PI / 2 - t.Face).Scaled(Vector3.One * _scale * (p.Scouting ? .85f : 1));
            t.Pose(walking, delta);
            t.Count.Text = p.Party.Members.Count + (p.Lord != null ? 1 : 0) > 1 ? (p.Party.Members.Count + (p.Lord != null ? 1 : 0)).ToString() : "";
            if (p.Owner == Holder.Ours) OursDrawn++; else TheirsDrawn++;
        }
        foreach (var (p, t) in new List<KeyValuePair<MapParty, Token>>(_tokens))
            if (!t.Seen || !C.Parties.Contains(p)) { t.QueueFree(); _tokens.Remove(p); }
        // the ring round the chosen
        if (Chosen != null && !C.Parties.Contains(Chosen)) Chosen = null;
        _ring.Visible = Chosen != null && _tokens.ContainsKey(Chosen);
        if (_ring.Visible)
        {
            _ring.Position = _tokens[Chosen].Position + Vector3.Up * _scale;
            _ring.Scale = new Vector3(_scale * _refH * .5f, _scale * _refH * .2f, _scale * _refH * .5f);
        }
        // the way shown wears off as it is walked
        if (Shown != null && Chosen != null && Chosen.Walking && Chosen.Trail.Count > 0 && Chosen.Trail[^1] == ShownTo)
        {
            var way = new List<(float X, float Z)> { (Chosen.AtX, Chosen.AtZ) };
            way.AddRange(Chosen.Trail);
            if (((int)(Engine.GetFramesDrawn()) & 7) == 0) Show(Chosen, way, C.TrailLeft(Chosen));
        }
        else if (Shown != null && Chosen != null && !Chosen.Walking && new Vector2(Chosen.AtX - ShownTo.X, Chosen.AtZ - ShownTo.Z).Length() < 2) Hide();
    }

    /// <summary>How tall a figure stands, in its own voxels.</summary>
    static float Height(Figure f)
    {
        float top = 0;
        for (int i = 0; i < Figure.Parts; i++) if ((f.Mid[i] ?? f.Mesh[i]) is { } m) top = MathF.Max(top, m.GetAabb().End.Y);
        return top;
    }

    static float LerpAngle(float a, float b, float t)
    {
        float d = (b - a) % MathF.Tau;
        if (d > MathF.PI) d -= MathF.Tau; else if (d < -MathF.PI) d += MathF.Tau;
        return a + d * t;
    }

    Token Make(MapParty p)
    {
        bool ours = p.Owner == Holder.Ours;
        BodyPlan plan;
        if (ours && p.Party.Commander is { } cm) plan = Bodies.HeroRider(cm.Look);
        else if (ours) plan = Bodies.Plan(p.Party.Members.Exists(m => m.Kind.Mounted) ? p.Party.Members.Find(m => m.Kind.Mounted).Kind : Kinds.Rider, 0);
        else plan = Bodies.Plan(p.Lord?.Kind ?? Kinds.Orc, 0);
        var t = new Token { Fig = Figure.For(plan) };
        var mat = Figure.Material(BodyPlan.Sub);
        for (int i = 0; i < Figure.Parts; i++)
        {
            var mesh = t.Fig.Mid[i] ?? t.Fig.Mesh[i];
            if (mesh == null) continue;
            t.Parts[i] = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Layers = AtlasView.Layer };
            t.AddChild(t.Parts[i]);
        }
        // the banner over his shoulder: blue for ours, red for the horde's; none over scouts
        if (!p.Scouting)
        {
            float h0 = Height(t.Fig);
            var banner = Banner(ours ? Ours : Theirs, 1);
            banner.Position = new Vector3(ours ? -5 : -4, h0 * .45f, -4);
            banner.Scale = Vector3.One * h0 * .85f;
            t.AddChild(banner);
        }
        float h = Height(t.Fig);
        t.Count = new Label3D { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 26, OutlineSize = 8, PixelSize = .0006f, FixedSize = true, Layers = AtlasView.Layer,
            Modulate = ours ? new Color("e9eefc") : new Color("ffd2c8"), OutlineModulate = new Color(0, 0, 0, .85f), NoDepthTest = true, Font = Ornament.SerifBold,
            Position = new Vector3(0, h + 4, 0) };
        t.AddChild(t.Count);
        return t;
    }

    /// <summary>A banner on its pole: the pole a unit tall (scaled), the cloth at its top, a gold stripe along its hem.</summary>
    static Node3D Banner(Color cloth, float height)
    {
        var n = new Node3D();
        var wood = new StandardMaterial3D { AlbedoColor = new Color("4a3424"), Roughness = .9f };
        n.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.05f, height, .05f), Material = wood }, Position = new Vector3(0, height / 2, 0), Layers = AtlasView.Layer });
        var flag = new StandardMaterial3D { AlbedoColor = cloth, Roughness = .85f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        n.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.02f, height * .32f, .42f), Material = flag }, Position = new Vector3(0, height * .8f, -.23f), Layers = AtlasView.Layer });
        var hem = new StandardMaterial3D { AlbedoColor = Gold, Roughness = .5f, Metallic = .4f };
        n.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.025f, height * .05f, .42f), Material = hem }, Position = new Vector3(0, height * .63f, -.23f), Layers = AtlasView.Layer });
        n.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .045f, Height = .09f, Material = hem }, Position = new Vector3(0, height + .03f, 0), Layers = AtlasView.Layer });
        return n;
    }

    /// <summary>A figure on the map: its parts, how it moves.</summary>
    sealed partial class Token : Node3D
    {
        public Figure Fig;
        public readonly MeshInstance3D[] Parts = new MeshInstance3D[Figure.Parts];
        public Label3D Count;
        public bool Seen;
        public float Face, Phase, Walk;

        /// <summary>Its legs going as it walks (a horse's forelegs and hind legs, a man's two), at rest standing; its body rising and falling with them.</summary>
        public void Pose(bool walking, double delta)
        {
            Walk = Mathf.MoveToward(Walk, walking ? 1 : 0, (float)delta * 3);
            Phase += (float)delta * 7 * Walk;
            float swing = MathF.Sin(Phase) * .45f * Walk, bob = MathF.Abs(MathF.Sin(Phase)) * .6f * Walk;
            for (int i = 0; i < Figure.Parts; i++)
            {
                if (Parts[i] == null) continue;
                float a = i == (int)Part.LegR ? swing : i == (int)Part.LegL ? -swing : i is (int)Part.Main or (int)Part.Off && !Fig.Mounted ? -swing * .5f * (i == (int)Part.Main ? 1 : -1) : 0;
                var j = Fig.Joint[i];
                var turn = new Basis(Vector3.Right, a);
                Parts[i].Transform = new Transform3D(turn, j - turn * j + Vector3.Up * (i is (int)Part.LegR or (int)Part.LegL ? 0 : bob));
            }
        }
    }
}
