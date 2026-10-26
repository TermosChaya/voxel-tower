using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// What stands on the big world map, drawn over its land (MapDress.cs, MapModels.cs): for every piece of the land the
/// eye wants, its trees and things made on a worker thread into one mesh of fine models for near, one of coarse models
/// for further off and their shadow, put in here a few at a time, let go with the piece. The sails of the windmills and
/// the wheels of the water mills turn; chimneys and the horde's fires smoke, fires and torches throw their light about,
/// the alchemist's chimney breathes green, the sulphur steams, the dead places glow; a few birds wheel over the land.
/// From far off, where no piece is drawn, each thing seen stands as its coarse model, larger the further the eye is,
/// in place of the land's blocks of colour.
/// </summary>
public partial class HexMapView
{
    ShaderMaterial _things, _turning;
    IHexSource _dressSrc;
    bool _dressHooked;

    sealed class DressPiece
    {
        /// <summary>Its things and what grows on it, each fine and coarse, made into Godot's meshes when first shown; its shadow.</summary>
        public readonly MeshInstance3D[] Mesh = new MeshInstance3D[4];
        public readonly MeshBuffers[] Buffers = new MeshBuffers[4];
        public MeshInstance3D Shadow;
        /// <summary>Whether its things and what grows on it show their fine models; its middle (scene units).</summary>
        public bool FineThings, FineGrowth;
        public Vector3 Mid;
        /// <summary>A mesh it wants is yet to be made (the frame's budget was spent): it is tried again next frame.</summary>
        public bool Pending;
        public MeshBuffers ShadowB;
        public readonly List<(Node3D Spin, MapDress.Turner T)> Turners = new();
        public readonly List<(GpuParticles3D Fx, MapDress.Plume P)> Plumes = new();
        public bool Stale;
    }

    readonly Dictionary<int, DressPiece> _dressed = new();
    readonly Dictionary<int, Task<MapDress.Dressed>> _dressing = new();
    readonly Queue<(int Key, MapDress.Dressed D)> _dressReady = new();
    bool _iconsDirty = true;
    float _spinAngle, _iconMag;
    double _lightsAt;
    readonly List<OmniLight3D> _lights = new();
    readonly Dictionary<(MapModel, byte), MultiMeshInstance3D> _icons = new();
    readonly Dictionary<(MapModel, byte), ArrayMesh> _iconMeshes = new();
    MultiMeshInstance3D _birds;
    ArrayMesh _sailsMesh, _wheelMesh;
    /// <summary>The drawing's voxels to the map's (the things' meshes are in the drawing's).</summary>
    static float G => MapDress.K / MapDress.Scale;
    /// <summary>
    /// How far from the eye (scene units) a piece shows its things' fine models, and what grows on it — the trees, many
    /// and alike, nearer; beyond it the coarse ones (a little nearer again to come back), so the far part of the screen
    /// costs little.
    /// </summary>
    const float FineTill = 41, FineFrom = 37, GrowTill = 33, GrowFrom = 30;
    /// <summary>Vertices of the things put into Godot's meshes a frame at most (the first piece waiting always goes in).</summary>
    const int DressVerticesAFrame = 25_000;
    /// <summary>The vertices put in this frame so far (pieces come and pieces turn fine or coarse out of one budget).</summary>
    int _dressSpent;
    /// <summary>Nothing of the things drawn (--perf-without=things, to weigh what they cost).</summary>
    bool _noThings;
    /// <summary>Pieces dressed, being dressed (the self-test and the pictures read them).</summary>
    public int DressedPieces => _dressed.Count;
    /// <summary>How many meshes of things and what grows are shown now.</summary>
    public int ThingsShown
    {
        get
        {
            int n = 0;
            foreach (var p in _dressed.Values) foreach (var m in p.Mesh) if (m != null && m.Visible) n++;
            return n;
        }
    }
    public bool DressSettled => _dressing.Count == 0 && _dressReady.Count == 0 && AllDressed();

    bool AllDressed()
    {
        foreach (int k in _want) if (!_dressed.TryGetValue(k, out var p) || p.Stale || p.Pending) return false;
        return true;
    }

    /// <summary>A new source to dress: what was dressed let go, the materials told where the hexes are, its changes listened to.</summary>
    void DressAnew()
    {
        if (_dressSrc != null) _dressSrc.Changed -= DressChanged;
        foreach (var t in _dressing.Values) { try { t.Wait(); } catch { } }
        _dressing.Clear();
        _dressReady.Clear();
        foreach (var p in _dressed.Values) Undress(p);
        _dressed.Clear();
        foreach (var mm in _icons.Values) mm.QueueFree();
        _icons.Clear();
        _dressSrc = Source;
        Source.Changed += DressChanged;
        if (!_dressHooked) { _dressHooked = true; TreeExiting += () => { if (_dressSrc != null) _dressSrc.Changed -= DressChanged; }; }
        _things ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
        _turning ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
        var lay = Layout;
        float g = G;
        _things.SetShaderParameter("atlas", new Vector4(lay.OX * g, lay.OZ * g, HexMapLayout.R * g, 1));
        _things.SetShaderParameter("atlas_size", new Vector2(lay.FogW * HexMapLayout.HalfW * g, lay.FogH * HexMapLayout.RowStep * g));
        _things.SetShaderParameter("atlas_fog", _fogTex);
        _things.SetShaderParameter("atlas_through", .04f);
        _things.SetShaderParameter("atlas_dark", new Vector3(.012f, .014f, .02f));
        _iconsDirty = true;
        if (_birds == null) Birds();
        // every kind of smoke and fire shown once at the eye, under the ground, so their shaders are made as the map
        // opens and not the first time one comes into sight
        foreach (MapDress.PlumeKind k in Enum.GetValues<MapDress.PlumeKind>())
        {
            var (x, z) = (_look.X, _look.Y);
            var warm = Plume(new MapDress.Plume { Kind = k, X = x, Y = HexTiles.Base - 4, Z = z, Size = 1 });
            GetTree().CreateTimer(1.5).Timeout += () => { if (IsInstanceValid(warm)) warm.QueueFree(); };
        }
    }

    /// <summary>A hex's look or fog changed: the pieces it and those beside it are dressed in made again (a wood's edge, a farm look across).</summary>
    void DressChanged(int col, int row)
    {
        if (Layout == null || !Layout.OnField(col, row)) return;
        var (cx, cz) = Layout.Center(col, row);
        float r = HexMapLayout.W + HexMapLayout.R;
        var (tx0, tz0, tx1, tz1) = Layout.TilesIn(cx - r, cz - r, cx + r, cz + r);
        for (int tz = tz0; tz <= tz1; tz++)
            for (int tx = tx0; tx <= tx1; tx++)
                if (_dressed.TryGetValue(PieceKey(tx, tz), out var p)) p.Stale = true;
        _iconsDirty = true;
    }

    /// <summary>Every frame, after the land: the pieces' dressing kept up, near or far, what turns turned, the smokes and the lights, the far icons, the birds.</summary>
    void Dressed(double delta)
    {
        if (Source == null || Layout == null || _noThings) return;
        if (_dressSrc != Source) DressAnew();
        float g = G;
        var hv = Layout.OnField(Hovered.Col, Hovered.Row) ? Layout.Center(Hovered.Col, Hovered.Row) : (-1e5f, -1e5f);
        var ch = Layout.OnField(Chosen.Col, Chosen.Row) ? Layout.Center(Chosen.Col, Chosen.Row) : (-1e5f, -1e5f);
        _things.SetShaderParameter("atlas_hover", new Vector4(hv.Item1 * g, hv.Item2 * g, ch.Item1 * g, ch.Item2 * g));
        // what is made comes in, so many vertices a frame
        foreach (var (key, task) in new List<KeyValuePair<int, Task<MapDress.Dressed>>>(_dressing))
        {
            if (!task.IsCompleted) continue;
            _dressing.Remove(key);
            if (task.IsFaulted) { GD.PrintErr($"[world map] a piece's dressing failed: {task.Exception?.GetBaseException().Message}"); continue; }
            _dressReady.Enqueue((key, task.Result));
        }
        _dressSpent = 0;
        while (_dressReady.Count > 0 && (_dressSpent == 0 || _dressSpent < DressVerticesAFrame))
        {
            var (key, d) = _dressReady.Dequeue();
            Dress(key, d);
            if (_dressSpent == 0) _dressSpent = 1;
        }
        // what the land wants is dressed too, nearest first, two at a time beside the land's own work (one while the land
        // still makes its pieces: the ground comes first)
        var order = new List<int>();
        foreach (int k in _want)
            if ((!_dressed.TryGetValue(k, out var p) || p.Stale) && !_dressing.ContainsKey(k)) order.Add(k);
        order.Sort((a, b) => Far(a).CompareTo(Far(b)));
        foreach (int k in order)
        {
            if (_dressing.Count >= (_making.Count > 0 ? 1 : 2)) break;
            int tx = k % Layout.TilesX, tz = k / Layout.TilesX;
            var src = Source; var lay = Layout;
            _dressing[k] = Task.Run(() => MapDress.Dress(src, lay, tx, tz));
            if (_dressed.TryGetValue(k, out var stale)) stale.Stale = false;
        }
        // let go what the land lets go
        if (_dressed.Count > _want.Count + 8 || _dist >= NearMost)
        {
            var keep = new HashSet<int>();
            if (_dist < NearMost)
                foreach (int k in _want)
                {
                    int tx = k % Layout.TilesX, tz = k / Layout.TilesX;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++) keep.Add(PieceKey(Math.Clamp(tx + dx, 0, Layout.TilesX - 1), Math.Clamp(tz + dz, 0, Layout.TilesZ - 1)));
                }
            foreach (int k in new List<int>(_dressed.Keys))
                if (!keep.Contains(k)) { Undress(_dressed[k]); _dressed.Remove(k); }
        }
        // near the fine models, further the coarse: piece by piece, by how far its middle is from the eye
        var eye = Cam.GlobalPosition;
        foreach (var p in _dressed.Values)
        {
            float far = eye.DistanceTo(p.Mid);
            bool things = far < (p.FineThings ? FineTill : FineFrom), growth = far < (p.FineGrowth ? GrowTill : GrowFrom);
            if (things == p.FineThings && growth == p.FineGrowth && !p.Pending) continue;
            p.FineThings = things; p.FineGrowth = growth;
            Show(p);
        }
        // what turns and smokes
        _spinAngle += (float)delta * .9f;
        bool close = _dist < 52;
        foreach (var p in _dressed.Values)
        {
            foreach (var (spin, t) in p.Turners)
            {
                spin.GetParent<Node3D>().Visible = close && Seen(t.Col, t.Row);
                spin.Rotation = new Vector3(0, 0, t.Model == MapModel.Wheel ? -_spinAngle * 1.6f : _spinAngle);
            }
            foreach (var (fx, pl) in p.Plumes) fx.Emitting = fx.Visible = close && Seen(pl.Col, pl.Row);
        }
        if ((_lightsAt -= delta) <= 0) { _lightsAt = .35; Lights(); }
        foreach (var l in _lights)
            if (l.Visible) l.LightEnergy = (float)(l.GetMeta("base").AsDouble() * (.8 + .2 * Math.Sin(Time.GetTicksMsec() * .013 + l.GetInstanceId() % 7) + .08 * Math.Sin(Time.GetTicksMsec() * .041)));
        Icons();
        BirdsFly(delta);
    }

    void Dress(int key, MapDress.Dressed d)
    {
        // a piece no longer wanted is not put in
        if (!_want.Contains(key) && !_dressed.ContainsKey(key)) return;
        if (_dressed.TryGetValue(key, out var old)) Undress(old);
        int tx = key % Layout.TilesX, tz = key / Layout.TilesX;
        var p = new DressPiece { Mid = new Vector3((tx + .5f) * HexMapLayout.Tile * V, HexTiles.Base * V, (tz + .5f) * HexMapLayout.Tile * V) };
        p.Buffers[0] = d.Things; p.Buffers[1] = d.ThingsFar; p.Buffers[2] = d.Growth; p.Buffers[3] = d.GrowthFar;
        float far = Cam.GlobalPosition.DistanceTo(p.Mid);
        p.FineThings = far < FineFrom; p.FineGrowth = far < GrowFrom;
        p.ShadowB = d.Shadow;
        foreach (var t in d.Turners) p.Turners.Add((Turner(t), t));
        foreach (var pl in d.Plumes) p.Plumes.Add((Plume(pl), pl));
        _dressed[key] = p;
        Show(p);
    }

    static bool Needs(DressPiece p, int i) => p.Mesh[i] == null && p.Buffers[i] != null;

    /// <summary>Whether so many vertices more may be put in this frame (the first lot always may).</summary>
    bool Room(int vertices) => _dressSpent == 0 || _dressSpent + vertices <= DressVerticesAFrame;

    /// <summary>
    /// A piece's things and what grows on it shown fine or coarse, each mesh made into Godot's on first need as the
    /// frame's budget allows; till the one wanted is made the other stays shown. Its shadow likewise, near the eye only.
    /// </summary>
    void Show(DressPiece p)
    {
        float scale = MapDress.Scale / MapDress.K;
        p.Pending = false;
        for (int pair = 0; pair < 2; pair++)
        {
            int fine = pair * 2, coarse = fine + 1, want = (pair == 0 ? p.FineThings : p.FineGrowth) ? fine : coarse, other = want == fine ? coarse : fine;
            if (Needs(p, want))
            {
                if (Room(p.Buffers[want].Vertices))
                {
                    _dressSpent += p.Buffers[want].Vertices;
                    p.Mesh[want] = new MeshInstance3D { Mesh = GodotMesh.From(p.Buffers[want]), MaterialOverride = _things, Scale = Vector3.One * scale, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                    _land.AddChild(p.Mesh[want]);
                    p.Buffers[want] = null;
                }
                else p.Pending = true;
            }
            bool made = p.Mesh[want] != null;
            if (made) p.Mesh[want].Visible = true;
            if (p.Mesh[other] != null) p.Mesh[other].Visible = !made;
        }
        // only the pieces near the eye cast their things' shadows: further off they are not missed
        if (p.Shadow == null && p.ShadowB != null && p.FineThings)
        {
            if (Room(p.ShadowB.Vertices))
            {
                _dressSpent += p.ShadowB.Vertices;
                p.Shadow = new MeshInstance3D { Mesh = GodotMesh.From(p.ShadowB), Scale = Vector3.One * scale, CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly };
                _land.AddChild(p.Shadow);
                p.ShadowB = null;
            }
            else p.Pending = true;
        }
        if (p.Shadow != null) p.Shadow.Visible = p.FineThings;
    }

    static void Undress(DressPiece p)
    {
        foreach (var mi in new[] { p.Mesh[0], p.Mesh[1], p.Mesh[2], p.Mesh[3], p.Shadow })
            if (mi != null && IsInstanceValid(mi)) { var m = mi.Mesh; mi.QueueFree(); m?.Dispose(); }
        foreach (var (spin, _) in p.Turners) if (IsInstanceValid(spin)) spin.GetParent().QueueFree();
        foreach (var (fx, _) in p.Plumes) if (IsInstanceValid(fx)) fx.QueueFree();
    }

    // ------------------------------------------------------------ what turns

    /// <summary>A windmill's sails or a water wheel on its hub, to be turned: a pivot at the hub (turned west to east for a wheel), the model on it.</summary>
    Node3D Turner(MapDress.Turner t)
    {
        bool sails = t.Model == MapModel.Sails;
        var mesh = sails ? (_sailsMesh ??= GodotMesh.From(MapModels.Get(MapModel.Sails, 0, 0, false, MapModels.ThingK).Solid))
                         : (_wheelMesh ??= GodotMesh.From(MapModels.Get(MapModel.Wheel, 0, 0, false, MapModels.ThingK).Solid));
        float s = MapDress.Scale * V * MapModels.Size(t.Model), hub = sails ? 8 : 4;
        var pivot = new Node3D { Position = new Vector3(t.X * V, t.Y * V, t.Z * V), Rotation = new Vector3(0, t.AxleWestEast ? MathF.PI / 2 : 0, 0) };
        var spin = new Node3D();
        pivot.AddChild(spin);
        spin.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = _turning, Scale = Vector3.One * (s / MapModels.ThingK), Position = new Vector3(0, -hub * s, 0) });
        _world.AddChild(pivot);
        return spin;
    }

    // ------------------------------------------------------------ smoke, fire, the dead glow

    static Texture2D _puff;

    /// <summary>A soft round puff, white, fading to nothing at its rim.</summary>
    static Texture2D Puff()
    {
        if (_puff != null) return _puff;
        var grad = new Gradient();
        grad.SetColor(0, new Color(1, 1, 1, 1));
        grad.SetColor(1, new Color(1, 1, 1, 0));
        grad.AddPoint(.5f, new Color(1, 1, 1, .55f));
        _puff = new GradientTexture2D { Gradient = grad, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(.5f, .5f), FillTo = new Vector2(1, .5f), Width = 32, Height = 32 };
        return _puff;
    }

    /// <summary>What a kind of smoke or fire is drawn with, made once and shared, so no new shader is made as the eye moves.</summary>
    readonly Dictionary<MapDress.PlumeKind, (ParticleProcessMaterial Pm, StandardMaterial3D Mat, float Size, int Amount, float Life)> _plumeKinds = new();

    GpuParticles3D Plume(MapDress.Plume p)
    {
        if (!_plumeKinds.TryGetValue(p.Kind, out var kind)) _plumeKinds[p.Kind] = kind = PlumeKindMade(p.Kind);
        var fx = new GpuParticles3D
        {
            Amount = kind.Amount, Lifetime = kind.Life, ProcessMaterial = kind.Pm, DrawPass1 = new QuadMesh { Size = Vector2.One * kind.Size * p.Size, Material = kind.Mat },
            Position = new Vector3(p.X * V, p.Y * V, p.Z * V), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-2, -1, -2), new Vector3(4, 6, 4)), Preprocess = kind.Life, SpeedScale = .8f + p.Size * .2f,
        };
        _world.AddChild(fx);
        return fx;
    }

    (ParticleProcessMaterial, StandardMaterial3D, float, int, float) PlumeKindMade(MapDress.PlumeKind k)
    {
        const float s = 1;
        Color c0, c1;
        float rise, life, size;
        int amount;
        switch (k)
        {
            case MapDress.PlumeKind.Fire: c0 = new Color(1, .55f, .16f, .85f); c1 = new Color(.3f, .27f, .26f, 0); rise = .16f; life = 1.1f; size = .07f; amount = 12; break;
            case MapDress.PlumeKind.Torch: c0 = new Color(1, .66f, .22f, .85f); c1 = new Color(1, .3f, .1f, 0); rise = .07f; life = .5f; size = .03f; amount = 5; break;
            case MapDress.PlumeKind.GreenSmoke: c0 = new Color(.45f, .9f, .5f, .32f); c1 = new Color(.3f, .6f, .35f, 0); rise = .08f; life = 3.4f; size = .1f; amount = 10; break;
            case MapDress.PlumeKind.Steam: c0 = new Color(.95f, .9f, .55f, .26f); c1 = new Color(.9f, .9f, .8f, 0); rise = .06f; life = 3; size = .13f; amount = 8; break;
            case MapDress.PlumeKind.Ghost: c0 = new Color(.6f, 1, .82f, .6f); c1 = new Color(.4f, .9f, .7f, 0); rise = .05f; life = 1.6f; size = .035f; amount = 6; break;
            default: c0 = new Color(.6f, .6f, .62f, .3f); c1 = new Color(.75f, .76f, .8f, 0); rise = .07f; life = 3.8f; size = .1f; amount = 9; break;
        }
        var ramp = new Gradient();
        ramp.SetColor(0, c0); ramp.SetColor(1, c1);
        var curve = new Curve();
        curve.AddPoint(new Vector2(0, .35f)); curve.AddPoint(new Vector2(1, 1));
        var pm = new ParticleProcessMaterial
        {
            Direction = new Vector3(0, 1, 0), Spread = 12, InitialVelocityMin = rise * .7f * s, InitialVelocityMax = rise * 1.2f * s,
            Gravity = new Vector3(.03f, k == MapDress.PlumeKind.Fire ? .12f : .02f, 0), ScaleMin = .8f, ScaleMax = 1.2f,
            ColorRamp = new GradientTexture1D { Gradient = ramp }, ScaleCurve = new CurveTexture { Curve = curve },
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = .06f * s,
        };
        var mat = new StandardMaterial3D
        {
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, AlbedoTexture = Puff(),
        };
        return (pm, mat, size, amount, life);
    }

    /// <summary>The fires and torches nearest the eye given a light each, so many lights at most.</summary>
    void Lights()
    {
        const int Most = 8;
        while (_lights.Count < Most)
        {
            var l = new OmniLight3D { ShadowEnabled = false, OmniRange = 1.6f, LightColor = new Color(1, .62f, .3f), Visible = false };
            l.SetMeta("base", 1.0);
            _world.AddChild(l);
            _lights.Add(l);
        }
        var fires = new List<(float d, MapDress.Plume p)>();
        if (_dist < 52)
            foreach (var piece in _dressed.Values)
                foreach (var (_, pl) in piece.Plumes)
                {
                    if (pl.Kind is not (MapDress.PlumeKind.Fire or MapDress.PlumeKind.Torch or MapDress.PlumeKind.Ghost) || !Seen(pl.Col, pl.Row)) continue;
                    float dx = pl.X - _look.X, dz = pl.Z - _look.Y;
                    fires.Add((dx * dx + dz * dz, pl));
                }
        fires.Sort((a, b) => a.d.CompareTo(b.d));
        for (int i = 0; i < _lights.Count; i++)
        {
            var l = _lights[i];
            if (i >= fires.Count) { l.Visible = false; continue; }
            var pl = fires[i].p;
            l.Visible = true;
            l.Position = new Vector3(pl.X * V, pl.Y * V + .15f, pl.Z * V);
            bool ghost = pl.Kind == MapDress.PlumeKind.Ghost;
            l.LightColor = ghost ? new Color(.45f, 1, .7f) : new Color(1, .6f, .28f);
            l.OmniRange = (pl.Kind == MapDress.PlumeKind.Fire ? 1.3f : .7f) * pl.Size;
            l.SetMeta("base", pl.Kind == MapDress.PlumeKind.Fire ? .5 : ghost ? .35 : .25);
        }
    }

    // ------------------------------------------------------------ from far off

    /// <summary>
    /// From far off, where no piece is drawn: every thing seen as its coarse model on its hex, larger the further the eye
    /// (the land's own blocks of colour hidden); made again when a look changes or the eye has gone much nearer or further.
    /// </summary>
    void Icons()
    {
        if (_farMarks != null) _farMarks.Visible = false;
        bool far = _dist > NearMost * .85f;
        float mag = MathF.Round(Math.Clamp(_dist / 52f, 1, 2f) * 4) / 4;
        foreach (var mm in _icons.Values) mm.Visible = far;
        if (!far || !_iconsDirty && mag == _iconMag) return;
        _iconsDirty = false;
        _iconMag = mag;
        var lists = new Dictionary<(MapModel, byte), List<Transform3D>>();
        float s0 = MapDress.Scale * V * mag;
        for (int r = 0; r < Layout.Rows; r++)
            for (int c = 0; c < Layout.Cols; c++)
            {
                var l = Source.Look(c, r);
                if (!MapDress.Shows(l) || !Source.Seen(c, r)) continue;
                // (from far off only what one steers by: towns, mines, places; the small things that lie about are not seen)
                if (l.Icon is HexIcon.Pile or HexIcon.Chest or HexIcon.Campfire or HexIcon.Signpost or HexIcon.Learning) continue;
                var key = (MapDress.ModelOf(l.Icon).Value, l.Owner);
                if (!lists.TryGetValue(key, out var list)) lists[key] = list = new List<Transform3D>();
                var (x, z) = Layout.Center(c, r);
                list.Add(new Transform3D(Basis.Identity.Scaled(Vector3.One * s0 * MapModels.Size(key.Item1)), new Vector3(x * V, HexTiles.LevelOf(l) * V, z * V)));
            }
        foreach (var (key, mm) in _icons) if (!lists.ContainsKey(key)) mm.Multimesh.InstanceCount = 0;
        foreach (var (key, list) in lists)
        {
            if (!_icons.TryGetValue(key, out var mmi))
            {
                var mesh = IconMesh(key.Item1, key.Item2);
                if (mesh == null) continue;
                mmi = new MultiMeshInstance3D
                {
                    Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh },
                    MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = false, Roughness = .85f },
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                _world.AddChild(mmi);
                _icons[key] = mmi;
            }
            var mmesh = mmi.Multimesh;
            mmesh.InstanceCount = list.Count;
            for (int i = 0; i < list.Count; i++) mmesh.SetInstanceTransform(i, list[i]);
            mmi.Visible = true;
        }
    }

    /// <summary>A thing's coarse model as a mesh for far off: its corners' shade put into its colours (the plain material has none of its own).</summary>
    ArrayMesh IconMesh(MapModel m, byte owner)
    {
        if (_iconMeshes.TryGetValue((m, owner), out var have)) return have;
        var b = MapModels.Get(m, 0, owner, false, 1).Solid;
        if (b == null) return null;
        var shaded = new MeshBuffers { Pos = b.Pos, Nrm = b.Nrm, Idx = b.Idx, Quads = b.Quads, Col = (float[])b.Col.Clone() };
        for (int v = 0; v < b.Vertices; v++)
            for (int k = 0; k < 3; k++) shaded.Col[v * 4 + k] *= b.Uv[v * 2];
        var mesh = GodotMesh.From(shaded);
        _iconMeshes[(m, owner)] = mesh;
        return mesh;
    }

    // ------------------------------------------------------------ birds

    /// <summary>A few birds: a dark V each, wheeling in loose rings over the land near the eye, their wings beating now and then.</summary>
    void Birds()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Tri(Vector3 a, Vector3 b, Vector3 c) { st.SetNormal(Vector3.Up); st.AddVertex(a); st.AddVertex(b); st.AddVertex(c); }
        // the body and the wings swept back, a bird a third of a unit across
        Tri(new Vector3(0, 0, -.06f), new Vector3(-.17f, .05f, .04f), new Vector3(0, 0, .03f));
        Tri(new Vector3(0, 0, -.06f), new Vector3(0, 0, .03f), new Vector3(.17f, .05f, .04f));
        Tri(new Vector3(0, 0, -.06f), new Vector3(0, 0, .03f), new Vector3(-.17f, .05f, .04f));
        Tri(new Vector3(0, 0, -.06f), new Vector3(.17f, .05f, .04f), new Vector3(0, 0, .03f));
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = st.Commit(), InstanceCount = 10 };
        _birds = new MultiMeshInstance3D
        {
            Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("2a2622"), Roughness = 1, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
        };
        _world.AddChild(_birds);
    }

    void BirdsFly(double delta)
    {
        if (_birds == null) return;
        _birds.Visible = _dist < 50;
        if (!_birds.Visible) return;
        float t = Time.GetTicksMsec() / 1000f;
        var mm = _birds.Multimesh;
        for (int i = 0; i < mm.InstanceCount; i++)
        {
            // two flocks, each about a point that drifts over the land near the eye
            int flock = i % 2;
            float cx = _look.X + (flock == 0 ? -60 : 70) + MathF.Sin(t * .05f + flock) * 40, cz = _look.Y + (flock == 0 ? -30 : 20) + MathF.Cos(t * .04f + flock * 2) * 30;
            float a = t * (.32f + flock * .07f) + i * .55f, r = 22 + (i * 7 % 13);
            float x = cx + MathF.Cos(a) * r, z = cz + MathF.Sin(a) * r, y = HexTiles.Base + 36 + MathF.Sin(t * .7f + i) * 3;
            float beat = .35f + .65f * MathF.Abs(MathF.Sin(t * (7 + i % 3) + i));
            var basis = new Basis(Vector3.Up, -a).Scaled(new Vector3(1, beat, 1) * 6.5f * V * MapDress.Scale);
            mm.SetInstanceTransform(i, new Transform3D(basis, new Vector3(x * V, y * V, z * V)));
        }
    }
}
