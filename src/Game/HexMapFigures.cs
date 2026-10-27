using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Someone who walks the big world map, as the drawing knows him (until the campaign's own parties walk it, the made-up
/// world's): the hex he is in, his way on (the hexes and the day each is reached), how far into its first step he is,
/// whose he is (1 ours, 2 the horde's), the body he is drawn with and the number by him.
/// </summary>
public sealed class MapWalker
{
    public int Col, Row;
    public List<(int Col, int Row, int Day)> Steps = new();
    /// <summary>How far into the first step of his way he is, 0 to 1.</summary>
    public double Progress;
    public byte Owner = 1;
    public BodyPlan Body;
    public int Count;
    public bool Walking => Steps.Count > 0 && Progress > 0;
}

/// <summary>
/// Those who stand and walk on the big world map, as figures on it (HexMapView.cs): a hero of ours in the saddle under a
/// blue banner, his army not shown; an army of the horde its warlord under a red banner, seen only where ours see; the
/// monsters that keep a hex, one of their kind with how many they are. Each walks from the middle of its hex to the
/// next's as far as it has got, its legs going, turned the way it goes; the one chosen stands in a gold ring.
/// </summary>
public partial class HexMapFigures : Node3D
{
    HexMapView _view;
    public readonly List<MapWalker> Walkers = new();
    /// <summary>Called first every frame: whoever feeds the figures (the campaign) brings <see cref="Walkers"/> up to date.</summary>
    public Action Sync;
    /// <summary>The kind and the number of the stack keeping a hex, when the source knows them (the campaign does).</summary>
    public Func<int, int, UnitType> KindOf;
    public Func<int, int, int> CountOf;
    public MapWalker Chosen;
    readonly Dictionary<MapWalker, Token> _tokens = new();
    readonly Dictionary<(int, int), Token> _keepers = new();
    /// <summary>What each keeper's figure was made for (a stack, ours holding it, and its works): made again when it changes.</summary>
    readonly Dictionary<(int, int), int> _keeperState = new();
    MeshInstance3D _ring;
    double _keepersAt;
    /// <summary>Figures drawn on the last frame: ours, the horde's, the monsters (the self-test reads them).</summary>
    public int OursDrawn, TheirsDrawn, MonstersDrawn;
    /// <summary>How tall a hero in the saddle stands, in the map's voxels: as in «Heroes», plain to the eye beside a castle.</summary>
    const float HeroTall = 24;
    static readonly UnitType[] Keepers = { Kinds.Wolf, Kinds.Skeleton, Kinds.Bear, Kinds.Orc, Kinds.Troll, Kinds.Goblin, Kinds.Boar, Kinds.Shaman };
    static readonly Color Ours = new("3f68b0"), Theirs = new("a8321f"), Gold = new("d9b25a");
    float _scale = .05f;

    public void Init(HexMapView view)
    {
        _view = view;
        _ring = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(2, 2), Orientation = PlaneMesh.OrientationEnum.Y }, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/map_ring.gdshader") },
        };
        AddChild(_ring);
        view.MiniDots = Dots;
    }

    IEnumerable<(float X, float Z, Color C)> Dots()
    {
        foreach (var w in Walkers)
        {
            if (w.Owner != 1 && !_view.Seen(w.Col, w.Row)) continue;
            var p = Spot(w);
            yield return (p.X / VoxelWorld.Vox, p.Z / VoxelWorld.Vox, w.Owner == 1 ? new Color("6f9ff0") : new Color("ff6a50"));
        }
    }

    /// <summary>Where a walker is in the scene and the way he faces: between the middles of his hex and the next as far as he has got.</summary>
    public Vector3 Spot(MapWalker w) => Spot(w, out _);

    Vector3 Spot(MapWalker w, out float facing)
    {
        var a = Stand(w.Col, w.Row);
        facing = 0;
        if (w.Steps.Count == 0) return a;
        var b = Stand(w.Steps[0].Col, w.Steps[0].Row);
        facing = MathF.Atan2(b.Z - a.Z, b.X - a.X);
        return a.Lerp(b, (float)Math.Clamp(w.Progress, 0, 1));
    }

    /// <summary>Where one stands on a hex: its middle, or before its thing on the south side, the thing whole to the eye (as a hero at a castle's gate).</summary>
    Vector3 Stand(int col, int row)
    {
        var l = _view.Source.Look(col, row);
        float off = _view.Source.Seen(col, row) && MapDress.Shows(l) ? MapDress.StandOff(l.Icon) : 0;
        if (off <= 0) return _view.HexToScene(col, row);
        var (x, z) = _view.Layout.Center(col, row);
        return _view.ToScene(x, z + off);
    }

    /// <summary>The walker of ours whose figure is nearest a point of the map (its voxels), within reach of a click; null if none.</summary>
    public MapWalker At(Vector2 vox)
    {
        MapWalker best = null; float bd = HexMapLayout.R * .8f;
        foreach (var w in Walkers)
        {
            if (w.Owner != 1) continue;
            var p = Spot(w) / VoxelWorld.Vox;
            float d = new Vector2(p.X - vox.X, p.Z - vox.Y).Length();
            if (d < bd) { bd = d; best = w; }
        }
        return best;
    }

    public override void _Process(double delta)
    {
        if (_view == null || !_view.IsOpen || _view.Layout == null) return;
        Sync?.Invoke();
        OursDrawn = TheirsDrawn = 0;
        bool near = _view.Distance < HexMapView.NearMost * 1.2f;
        foreach (var t in _tokens.Values) t.Seen = false;
        foreach (var w in Walkers)
        {
            bool seen = w.Owner == 1 || _view.Seen(w.Col, w.Row) || w.Steps.Count > 0 && w.Progress > .5 && _view.Seen(w.Steps[0].Col, w.Steps[0].Row);
            if (!seen) continue;
            if (!_tokens.TryGetValue(w, out var t)) { t = Make(w.Body ?? (w.Owner == 1 ? Bodies.HeroRider(0) : Bodies.Plan(Kinds.Orc, 0)), w.Owner, w.Owner != 0); _tokens[w] = t; AddChild(t); }
            t.Seen = true;
            var at = Spot(w, out float facing);
            t.Position = t.Position.DistanceTo(at) > 3 ? at : t.Position.Lerp(at, (float)Math.Min(1, delta * 12));
            t.Face = LerpAngle(t.Face, facing, (float)Math.Min(1, delta * 8));
            float s = _scale * (near ? 1 : 2.2f);
            t.Basis = new Basis(Vector3.Up, MathF.PI / 2 - t.Face).Scaled(Vector3.One * s);
            t.Pose(w.Walking, delta);
            t.Count.Text = w.Count > 1 ? w.Count.ToString() : "";
            // (the number keeps its size on the screen whatever the figure's scale)
            t.Count.Scale = Vector3.One / s;
            t.Plate.Visible = t.Count.Text != "";
            t.Plate.Scale = Vector3.One / s;
            if (w.Owner == 1) OursDrawn++; else TheirsDrawn++;
        }
        foreach (var (w, t) in new List<KeyValuePair<MapWalker, Token>>(_tokens))
            if (!t.Seen || !Walkers.Contains(w)) { t.QueueFree(); _tokens.Remove(w); }
        if (Chosen != null && !Walkers.Contains(Chosen)) Chosen = null;
        _ring.Visible = Chosen != null && _tokens.ContainsKey(Chosen);
        if (_ring.Visible)
        {
            _ring.Position = _tokens[Chosen].Position + Vector3.Up * .03f;
            float r = HexMapLayout.R * .62f * VoxelWorld.Vox;
            _ring.Scale = new Vector3(r, 1, r);
        }
        if ((_keepersAt -= delta) <= 0) { _keepersAt = .4; KeepersUp(near); }
    }

    /// <summary>The monsters that keep the hexes near the eye and seen: one of their kind on each, how many they are by it.</summary>
    void KeepersUp(bool near)
    {
        var lay = _view.Layout;
        var src = _view.Source;
        var keep = new HashSet<(int, int)>();
        if (near)
        {
            var shown = _view.GroundShown();
            var hexes = new List<(int Col, int Row)>();
            lay.HexesIn(shown.Position.X - 40, shown.Position.Y - 40, shown.End.X + 40, shown.End.Y + 40, hexes);
            foreach (var (c, r) in hexes)
            {
                var l = src.Look(c, r);
                // a stack on its hex, or one keeping the thing of a hex beside it
                // ours holding a hex stand by its thing under our banner, a fort's mark over them if it has works
                bool held = (l.Variant & 4) != 0, works = (l.Variant & 8) != 0;
                bool stack = l.Icon == HexIcon.Monster, keeping = !stack && (HexTiles.Kept(l) || held) && l.Icon != HexIcon.None;
                if (!stack && !keeping || !src.Seen(c, r)) continue;
                keep.Add((c, r));
                int state = (held ? 1 : 0) | (works ? 2 : 0);
                if (_keepers.ContainsKey((c, r)))
                {
                    if (_keeperState[(c, r)] == state) continue;
                    _keepers[(c, r)].QueueFree(); _keepers.Remove((c, r));
                }
                int vr = HexTiles.Vary(c, r);
                var kind = KindOf?.Invoke(c, r) ?? Keepers[vr % Keepers.Length];
                var t = Make(Bodies.Plan(kind, vr), held ? (byte)1 : (byte)0, banner: held);
                int n = CountOf?.Invoke(c, r) ?? 4 + vr % 23;
                t.Count.Text = n > 1 || held ? n.ToString() : "";
                t.Plate.Visible = n > 1 || held;
                if (held && works)
                {
                    var fort = new Sprite3D
                    {
                        Texture = GD.Load<Texture2D>("res://icons/hill-fort.svg"), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FixedSize = true,
                        PixelSize = .0006f * .45f, NoDepthTest = true, Position = t.Plate.Position + new Vector3(0, 0, 0), Offset = new Vector2(-70, 0),
                        RenderPriority = 3, Shaded = false, Modulate = new Color("cfe0ff"),
                    };
                    t.AddChild(fort);
                    t.Fort = fort;
                }
                _keeperState[(c, r)] = state;
                var at = _view.HexToScene(c, r);
                // (one keeping a thing stands by it, toward the south-east, the thing whole to the eye)
                if (keeping) { var (hx, hz) = lay.Center(c, r); at = _view.ToScene(hx + 9, hz + 8); }
                t.Position = at;
                float s = _scale * 1.15f;
                t.Basis = new Basis(Vector3.Up, MathF.PI / 2 + (float)(vr % 6) * .6f + 2.1f).Scaled(Vector3.One * s);
                t.Count.Scale = Vector3.One / s;
                t.Plate.Scale = Vector3.One / s;
                if (t.Fort != null) t.Fort.Scale = Vector3.One / s;
                t.Pose(false, 0);
                AddChild(t);
                _keepers[(c, r)] = t;
            }
        }
        foreach (var k in new List<(int, int)>(_keepers.Keys))
            if (!keep.Contains(k)) { _keepers[k].QueueFree(); _keepers.Remove(k); _keeperState.Remove(k); }
        MonstersDrawn = _keepers.Count;
    }

    static float LerpAngle(float a, float b, float t)
    {
        float d = (b - a) % MathF.Tau;
        if (d > MathF.PI) d -= MathF.Tau; else if (d < -MathF.PI) d += MathF.Tau;
        return a + d * t;
    }

    /// <summary>How tall a figure stands, in its own voxels.</summary>
    static float Height(Figure f)
    {
        float top = 0;
        for (int i = 0; i < Figure.Parts; i++) if ((f.Mid[i] ?? f.Mesh[i]) is { } m) top = MathF.Max(top, m.GetAabb().End.Y);
        return top;
    }

    float _heroH;

    Token Make(BodyPlan plan, byte owner, bool banner)
    {
        if (_heroH <= 0)
        {
            _heroH = Height(Figure.For(Bodies.HeroRider(0)));
            _scale = HeroTall * VoxelWorld.Vox / Math.Max(1, _heroH);
        }
        var t = new Token { Fig = Figure.For(plan) };
        var mat = Figure.Material(BodyPlan.Sub);
        for (int i = 0; i < Figure.Parts; i++)
        {
            var mesh = t.Fig.Mid[i] ?? t.Fig.Mesh[i];
            if (mesh == null) continue;
            t.Parts[i] = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat };
            t.AddChild(t.Parts[i]);
        }
        float h = Height(t.Fig);
        if (banner)
        {
            var b = Banner(owner == 1 ? Ours : Theirs, 1);
            b.Position = new Vector3(owner == 1 ? -5 : -4, h * .45f, -4);
            b.Scale = Vector3.One * h * .85f;
            t.AddChild(b);
        }
        t.Count = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 26, OutlineSize = 8, PixelSize = .0006f, FixedSize = true,
            Modulate = owner == 1 ? new Color("e9eefc") : owner == 2 ? new Color("ffd2c8") : new Color("f3e6c4"), OutlineModulate = new Color(0, 0, 0, .85f),
            NoDepthTest = true, Font = Ornament.SerifBold, Position = new Vector3(0, h + 4, 0), RenderPriority = 2, OutlineRenderPriority = 1,
        };
        t.AddChild(t.Count);
        // the number on a plate of dark iron with a bronze rim, as the game's own plates are
        t.Plate = new Sprite3D
        {
            Texture = Plate(owner), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FixedSize = true, PixelSize = .0006f * 1.25f,
            NoDepthTest = true, Position = new Vector3(0, h + 4, 0), RenderPriority = 0, Visible = false, Shaded = false,
        };
        t.AddChild(t.Plate);
        return t;
    }

    static readonly Dictionary<byte, Texture2D> _plates = new();

    /// <summary>A small plate to set a number on: dark iron, its corners cut, a rim of bronze (of red for the horde's).</summary>
    static Texture2D Plate(byte owner)
    {
        if (_plates.TryGetValue(owner, out var have)) return have;
        const int W = 64, H = 34;
        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        var rim = owner == 2 ? new Color("a8432c") : owner == 1 ? new Color("6f92d8") : Ornament.Bronze;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int dx = Math.Min(x, W - 1 - x), dy = Math.Min(y, H - 1 - y);
                if (dx + dy < 4) continue;
                bool edge = dx < 2 || dy < 2 || dx + dy < 6;
                img.SetPixel(x, y, edge ? rim : Ornament.Fade(Ornament.Iron, .92f));
            }
        var tex = ImageTexture.CreateFromImage(img);
        _plates[owner] = tex;
        return tex;
    }

    /// <summary>A banner on its pole: the pole a unit tall (scaled), the cloth at its top stirring in the wind, its hem and its round gilt, a gilt knob atop.</summary>
    static Node3D Banner(Color cloth, float height)
    {
        var n = new Node3D();
        var wood = new StandardMaterial3D { AlbedoColor = new Color("4a3424"), Roughness = .9f };
        n.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.05f, height, .05f), Material = wood }, Position = new Vector3(0, height / 2, 0) });
        var flag = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/map_banner.gdshader") };
        flag.SetShaderParameter("cloth", cloth);
        flag.SetShaderParameter("hem", Gold);
        flag.SetShaderParameter("half_w", .23f);
        flag.SetShaderParameter("half_h", height * .18f);
        flag.SetShaderParameter("phase", (float)GD.RandRange(0, 6.3));
        var cloth3 = new PlaneMesh { Size = new Vector2(.46f, height * .36f), Orientation = PlaneMesh.OrientationEnum.X, SubdivideWidth = 10, SubdivideDepth = 3, Material = flag };
        n.AddChild(new MeshInstance3D { Mesh = cloth3, Position = new Vector3(0, height * .8f, -.25f) });
        var hem = new StandardMaterial3D { AlbedoColor = Gold, Roughness = .5f, Metallic = .4f };
        n.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.03f, .03f, .5f), Material = wood }, Position = new Vector3(0, height * .98f, -.24f) });
        n.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .045f, Height = .09f, Material = hem }, Position = new Vector3(0, height + .03f, 0) });
        return n;
    }

    /// <summary>A figure on the map: its parts, how it moves.</summary>
    sealed partial class Token : Node3D
    {
        public Figure Fig;
        public readonly MeshInstance3D[] Parts = new MeshInstance3D[Figure.Parts];
        public Label3D Count;
        public Sprite3D Plate;
        /// <summary>The fort's mark over a garrison of ours behind works.</summary>
        public Sprite3D Fort;
        public bool Seen;
        public float Face, Phase, Walk;

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
