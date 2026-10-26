using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Draws everyone on the field from the simulation: every part of every kind of figure, and every piece of its dress, is
/// one MultiMesh, so a hundred pawns cost a few dozen draw calls. Near they are drawn in cells of 3 cm with their
/// weapons and shields finer still, casting their shadows from the half-voxels; at the middle distance in half-voxels;
/// far in whole voxels. Legs swing with the stride, arms with the blows; the fallen lie down and sink. A hurt body draws
/// the parts its wounds have changed on its own. Also draws arrows in flight and rings under the selected.
/// </summary>
public partial class UnitView : Node3D
{
    sealed class Kind
    {
        public Figure Fig;
        /// <summary>Each part as near, middle or far wants it; and in half-voxels again, casting the shadows when near.</summary>
        public MultiMeshInstance3D[] Parts, Shades;
        /// <summary>Each piece of its dress, and its shadow.</summary>
        public MultiMeshInstance3D[] Dress, DressShades;
        /// <summary>One buffer per part and per piece of dress: a transform and a colour for every figure of this kind.</summary>
        public readonly float[][] Bufs = new float[Figure.Bones][];
        public float[][] DressBufs;
        public int Count, Cap;
    }

    /// <summary>
    /// What the view remembers of a unit between frames: smoothed facing, how fast it is moving (and how much of that is
    /// a run), how far through its stride, how far it went this frame; its breath and how hard it is breathing; where
    /// its head turns and what it looks at next.
    /// </summary>
    sealed class Look
    {
        public float Face, Move, LastX, LastZ, Speed, Run, Dist, Phase;
        public float Breath, Exert, HeadYaw, HeadPitch, IdleYaw, IdlePitch;
        /// <summary>How far down it is (0 on its feet, 1 lying), eased so it goes down and gets up.</summary>
        public float Down;
        /// <summary>A beast's gait now and the one it is changing from, how far through the change, its strides walked, how far down to the grass its head is (UnitView.Gait.cs).</summary>
        public int Gait, GaitWas;
        public float GaitMix = 1, GaitPhase, Graze;
        public double GazeNext;
        public Unit GazeAt;
        public bool Seen;
        public Transform3D Body;
    }

    /// <summary>
    /// A hurt body: the parts its wounds have changed (and what they hold) drawn on their own, meshed again after a wound
    /// changes them and kept at each distance they have been drawn at; the rest are drawn with their kind.
    /// </summary>
    sealed class Own
    {
        /// <summary>Each bone of it (the parts are cut into bones as its kind's are).</summary>
        public readonly MeshInstance3D[] Parts = new MeshInstance3D[Figure.Bones];
        public MeshInstance3D[] Dress = Array.Empty<MeshInstance3D>();
        /// <summary>Which parts a wound has changed, and a sum of each.</summary>
        public readonly bool[] Hurt = new bool[Figure.Parts];
        public readonly int[] Sums = new int[Figure.Parts];
        /// <summary>The distance each bone is drawn at now (-1: to be drawn again at once, a wound has changed it).</summary>
        public readonly int[] At = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        /// <summary>The meshes made of each bone as it is now, at each distance (null: not made yet).</summary>
        public readonly Made[,] Kept = new Made[Figure.Bones, 3];
        /// <summary>The wounds last drawn, and how far they had gone.</summary>
        public BodyState Of;
        public int Rev = -1;
        /// <summary>Put away while the body is someone's eyes (first person).</summary>
        public bool Hidden;
        /// <summary>The tint its kind's figures are drawn in this frame (grey when dead, red when struck).</summary>
        public Color Tint = Colors.White;
    }

    /// <summary>A hurt part's mesh at one distance, and of what hangs on it (by the index of the dress).</summary>
    sealed class Made { public Mesh Body; public Mesh[] Dress; }

    /// <summary>A piece cut off a body, falling and then lying where it fell.</summary>
    sealed class Fallen { public MeshInstance3D Node; public Transform3D From, To; public double At; }

    Main _m;
    VoxelWorld _w;
    /// <summary>How near the figures are drawn: 0 in cells of 3 cm, 1 in half-voxels, 2 in whole voxels and without shadows.</summary>
    int _lod = 1;
    /// <summary>A distance the figures are kept at whatever the zoom (--figures=near|mid|far, for pictures), or -1.</summary>
    int _pinned = -1;
    /// <summary>Time spent this frame making meshes of hurt bodies, in ticks of <see cref="Stopwatch"/>.</summary>
    long _meshing;
    readonly Dictionary<BodyPlan, Kind> _kinds = new();
    readonly Dictionary<int, Look> _looks = new();
    readonly HashSet<int> _alive = new();
    readonly List<int> _gone = new();
    MultiMeshInstance3D _arrows, _rings, _standards, _stuck, _kegs, _bolts, _stones, _pots, _firePots, _flasks, _spears, _pitchKegs;
    float[] _kegBuf = Array.Empty<float>();
    float[] _ringBuf = Array.Empty<float>(), _standardBuf = Array.Empty<float>(), _stuckBuf = Array.Empty<float>();
    /// <summary>A buffer for each of what flies, by <see cref="Shot"/>.</summary>
    readonly float[][] _shotBufs = { Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>() };
    readonly Dictionary<int, Own> _own = new();
    readonly List<Fallen> _fallen = new();
    readonly Transform3D[] _xf = new Transform3D[Figure.Bones];
    float _dt;
    readonly Random _rnd = new(5);
    double _t;

    static readonly Aabb Everywhere = new(new Vector3(-100, -10, -100), new Vector3(200, 60, 200));

    public void Init(Main m)
    {
        _m = m;
        _w = m.World;

        // an arrow is 70 cm long, a little under five voxels
        var shaft = new BoxMesh { Size = new Vector3(.022f, .022f, .35f), Material = new StandardMaterial3D { AlbedoColor = new Color("4a3424"), Roughness = .9f } };
        _arrows = Multi(shaft, null, false);
        _stuck = Multi(shaft, null, false);
        // a crossbow's bolt shorter and thicker, with a steel head; a sling's stone; a pot of powder, or of burning pitch
        var bolt = new BoxMesh { Size = new Vector3(.032f, .032f, .22f), Material = new StandardMaterial3D { AlbedoColor = new Color("3b2e24"), Roughness = .8f, Metallic = .3f } };
        _bolts = Multi(bolt, null, false);
        var stone = new SphereMesh { Radius = .045f, Height = .09f, RadialSegments = 6, Rings = 3, Material = new StandardMaterial3D { AlbedoColor = new Color("5f5b58"), Roughness = 1 } };
        _stones = Multi(stone, null, false);
        var pot = new SphereMesh { Radius = .1f, Height = .18f, RadialSegments = 8, Rings = 4, Material = new StandardMaterial3D { AlbedoColor = new Color("8e5634"), Roughness = .9f } };
        _pots = Multi(pot, null, false);
        var firePot = new SphereMesh { Radius = .1f, Height = .18f, RadialSegments = 8, Rings = 4,
            Material = new StandardMaterial3D { AlbedoColor = new Color("8e3a1c"), EmissionEnabled = true, Emission = new Color(1f, .45f, .12f), EmissionEnergyMultiplier = 2.5f } };
        _firePots = Multi(firePot, null, false);
        InitSplints();
        // an alchemist's flask: green glass with the acid glowing in it
        var flask = new SphereMesh { Radius = .08f, Height = .15f, RadialSegments = 8, Rings = 4,
            Material = new StandardMaterial3D { AlbedoColor = new Color("5f9a3a"), Roughness = .15f, EmissionEnabled = true, Emission = new Color(.45f, .9f, .25f), EmissionEnergyMultiplier = 1.2f } };
        _flasks = Multi(flask, null, false);
        // a ballista's bolt, near two metres of ash with an iron head; an engine's keg of burning pitch, half a metre
        var spear = new BoxMesh { Size = new Vector3(.05f, .05f, .9f), Material = new StandardMaterial3D { AlbedoColor = new Color("4b3826"), Roughness = .8f, Metallic = .2f } };
        _spears = Multi(spear, null, false);
        var pitchKeg = new CylinderMesh { TopRadius = .11f, BottomRadius = .11f, Height = .26f, RadialSegments = 8, Rings = 1,
            Material = new StandardMaterial3D { AlbedoColor = new Color("6e3a1e"), EmissionEnabled = true, Emission = new Color(1f, .42f, .1f), EmissionEnergyMultiplier = 2f } };
        _pitchKegs = Multi(pitchKeg, null, false);
        // a sapper's charge: a keg of powder, a little under half a metre
        var keg = new BoxMesh { Size = new Vector3(.36f, .42f, .36f), Material = new StandardMaterial3D { AlbedoColor = new Color("6a3322"), Roughness = .85f } };
        _kegs = Multi(keg, null, false);
        m.Sim.Severed += Fall;
        StrokesInit(m.Sim);
        EnginesInit(m.Sim);
        var ring = new TorusMesh { InnerRadius = .82f, OuterRadius = 1f, Rings = 32, RingSegments = 4 };
        var ringMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, AlbedoColor = Colors.White };
        _rings = Multi(ring, ringMat, true);
        _rings.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        // a warband's leader carries its standard on its back: a pole and a rag of red cloth
        var cube = new BoxMesh { Size = Vector3.One };
        _standards = Multi(cube, new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled }, true);
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--figures=")) _pinned = a[10..] switch { "near" => 0, "mid" => 1, "far" => 2, _ => -1 };
    }

    /// <summary>The figures of one kind in one look, added the first time someone of that look turns up.</summary>
    Kind KindOf(Unit u)
    {
        var plan = Bodies.Plan(u.T, u.Look);
        if (_kinds.TryGetValue(plan, out var k)) return k;
        var fig = Figure.For(plan);
        int dn = fig.DressOn.Length;
        k = new Kind
        {
            Fig = fig, Parts = new MultiMeshInstance3D[Figure.Bones], Shades = new MultiMeshInstance3D[Figure.Bones],
            Dress = new MultiMeshInstance3D[dn], DressShades = new MultiMeshInstance3D[dn], DressBufs = new float[dn][],
        };
        for (int b = 0; b < Figure.Bones; b++)
        {
            if (fig.BoneNear[b] == null && fig.BoneMid[b] == null && fig.BoneFar[b] == null) continue;
            k.Parts[b] = Multi(fig.BoneMid[b] ?? fig.BoneNear[b] ?? fig.BoneFar[b], Figure.Material(BodyPlan.Sub), true);
            if (fig.BoneMid[b] != null) k.Shades[b] = Multi(fig.BoneMid[b], Figure.Material(BodyPlan.Sub), true);
        }
        for (int d = 0; d < dn; d++)
        {
            if (fig.DressNear[d] == null && fig.DressMid[d] == null) continue;
            k.Dress[d] = Multi(fig.DressMid[d] ?? fig.DressNear[d], Figure.Material(BodyPlan.Sub), true);
            if (fig.DressMid[d] != null) k.DressShades[d] = Multi(fig.DressMid[d], Figure.Material(BodyPlan.Sub), true);
        }
        _kinds[plan] = k;
        Fit(k);
        return k;
    }

    /// <summary>
    /// Every kind of figure in the meshes and the materials for how near it is drawn: near the cells of 3 cm and the
    /// dress, the shadows cast by the half-voxels; in the middle the half-voxels, casting their own; far whole voxels, no
    /// dress of its own and no shadows.
    /// </summary>
    void Fit(Kind k)
    {
        var fig = k.Fig;
        var on = GeometryInstance3D.ShadowCastingSetting.On;
        var off = GeometryInstance3D.ShadowCastingSetting.Off;
        for (int p = 0; p < Figure.Bones; p++)
        {
            var mi = k.Parts[p];
            if (mi == null) continue;
            var mesh = _lod == 0 ? fig.BoneNear[p] : _lod == 1 ? fig.BoneMid[p] : fig.BoneFar[p];
            mi.Visible = mesh != null;
            if (mesh != null)
            {
                mi.Multimesh.Mesh = mesh;
                mi.MaterialOverride = Figure.Material(_lod == 0 ? BodyPlan.FineSub : _lod == 1 ? BodyPlan.Sub : 1);
                mi.CastShadow = _lod == 1 ? on : off;
            }
            if (k.Shades[p] is { } sh) { sh.Visible = _lod == 0; sh.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly; }
        }
        for (int d = 0; d < k.Dress.Length; d++)
        {
            var mi = k.Dress[d];
            if (mi == null) continue;
            var mesh = _lod == 0 ? fig.DressNear[d] : _lod == 1 ? fig.DressMid[d] : null;
            mi.Visible = mesh != null;
            if (mesh != null)
            {
                mi.Multimesh.Mesh = mesh;
                mi.MaterialOverride = Figure.Material(_lod == 0 ? fig.Plan.Dress[d].Sub : BodyPlan.Sub);
                mi.CastShadow = _lod == 1 ? on : off;
            }
            if (k.DressShades[d] is { } sh) { sh.Visible = _lod == 0; sh.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly; }
        }
    }

    /// <summary>The one whose eyes the view is (first person): not drawn, but posed all the same; and the figures kept fine.</summary>
    public int HiddenId = -1;
    public bool ForceNear;

    /// <summary>The body's place and turn this frame of one drawn (or hidden), for the eyes of first person.</summary>
    public bool TryBody(int id, out Transform3D body)
    {
        if (_looks.TryGetValue(id, out var look) && look.Seen) { body = look.Body; return true; }
        body = Transform3D.Identity;
        return false;
    }

    /// <summary>
    /// How many pixels of the window a voxel takes: from seven up the figures are drawn in their cells of 3 cm, under
    /// three in whole voxels, between in half-voxels.
    /// </summary>
    void Distance()
    {
        int lod = 0;
        if (_pinned >= 0) lod = _pinned;
        else if (!ForceNear)
        {
            // (the pixels the world is drawn in, which is fewer than the window's at a lower detail)
            float px = GetTree().Root.Size.Y * Settings.Details[Settings.Detail] / Math.Max(.01f, _m.Rig.Cam.Size) * VoxelWorld.Vox;
            lod = px >= (_lod == 0 ? NearPx * .9f : NearPx) ? 0 : px < (_lod == 2 ? 3.6f : 3.2f) ? 2 : 1;
        }
        if (lod == _lod) return;
        // the kinds' meshes are all made: they change over at once; the hurt bodies each in turn (DrawOwn)
        _lod = lod;
        foreach (var k in _kinds.Values) Fit(k);
    }

    /// <summary>Pixels of the window to a voxel from which the figures are drawn in their finest cells.</summary>
    const float NearPx = 7;

    /// <summary>
    /// Time in a frame for making the meshes of hurt bodies only to draw them at another distance: when the zoom crosses
    /// a threshold they go over a few at a time, each drawn as it was until its turn, not all at once in one long frame.
    /// </summary>
    static readonly long Spare = Stopwatch.Frequency * 2 / 1000;

    MultiMeshInstance3D Multi(Mesh mesh, Material mat, bool colors)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colors, Mesh = mesh };
        var mi = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.On, CustomAabb = Everywhere };
        AddChild(mi);
        return mi;
    }

    /// <summary>Whether every part of one's body a wound has changed is drawn, on its own (the self-test, after first person).</summary>
    public bool HurtShown(int id)
    {
        if (!_own.TryGetValue(id, out var own)) return false;
        // every hurt part with something left of it is drawn by at least one of its bones
        for (int p = 0; p < Figure.Parts; p++)
        {
            if (!own.Hurt[p] || own.Of.Grid[p] is not { Count: > 0 }) continue;
            var (upper, lower) = Figure.BonesOf((Part)p);
            bool drawn = false;
            foreach (var b in new[] { upper, lower }) drawn |= own.Parts[(int)b] is { Visible: true, Mesh: not null };
            if (!drawn) return false;
        }
        return Array.IndexOf(own.Hurt, true) >= 0;
    }

    /// <summary>Arrows drawn in the last frame.</summary>
    public int ArrowsDrawn => _arrows.Multimesh.VisibleInstanceCount;

    /// <summary>Figures drawn in the last frame.</summary>
    public int Drawn()
    {
        int n = 0;
        foreach (var k in _kinds.Values) n += k.Count;
        return n;
    }

    // ---------------------------------------------------------------- every frame

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("UnitView");
        if (_m?.Sim == null) return;
        float dt = (float)Math.Min(delta, .1);
        _t += dt;
        _dt = _m.Paused ? 0 : dt;
        var sim = _m.Sim;
        Distance();
        _meshing = 0;
        look_xf.Clear();
        foreach (var k in _kinds.Values) k.Count = 0;
        // those in the citadel are not to be seen
        foreach (var u in sim.Units) if (!u.Inside && u.Id != HiddenId) KindOf(u).Count++;
        foreach (var k in _kinds.Values) { if (k.Count > k.Cap) Grow(k, k.Count); k.Count = 0; }
        _alive.Clear();
        StrokesBegin(sim);
        foreach (var u in sim.Units)
        {
            if (u.Inside) continue;
            var k = KindOf(u);
            _alive.Add(u.Id);
            if (!_looks.TryGetValue(u.Id, out var look)) _looks[u.Id] = look = new Look();
            Track(u, look, dt);
            var tint = Pose(u, k.Fig, look);
            // the dead go limp: their bones as the body falls and lies (UnitView.Corpse.cs)
            if (u.Dead) Limp(u, k.Fig, look);
            if (u.Id == HiddenId)
            {
                // its own wounded body put away, and shown again (and drawn as its wounds are by then) when it is drawn again
                if (_own.TryGetValue(u.Id, out var hid) && !hid.Hidden) { Show(hid, false); hid.Hidden = true; }
                continue;
            }
            // with its kind: all of it, or what the wounds have not changed (the rest, drawn on its own, is folded away here)
            if (u.Wounds == null && _own.Count > 0 && _own.Remove(u.Id, out var whole)) Free(whole);
            var hurt = u.Wounds != null ? DrawOwn(u, k.Fig, tint) : null;
            int o = k.Count++ * 16;
            for (int b = 0; b < Figure.Bones; b++) Write(k.Bufs[b], o, hurt != null && hurt[(int)Figure.PartOf[b]] ? Folded : _xf[b], tint);
            bool stowed = Stowed(u);
            for (int d = 0; d < k.DressBufs.Length; d++)
            {
                int on = (int)k.Fig.DressOn[d];
                Write(k.DressBufs[d], o, stowed || hurt != null && hurt[on] ? Folded : _xf[(int)k.Fig.DressBone[d]] * Wrist(u, k.Fig, d), tint);
            }
            // splints on what is splinted (UnitView.Carry.cs)
            Splints(u, k.Fig);
        }
        StrokesEnd();
        foreach (var k in _kinds.Values) Upload(k);
        _gone.Clear();
        foreach (int id in _looks.Keys) if (!_alive.Contains(id)) _gone.Add(id);
        foreach (int id in _gone)
        {
            _looks.Remove(id);
            _rags.Remove(id);
            _felling.Remove(id);
            if (_own.Remove(id, out var own)) Free(own);
        }
        DrawArrows(sim);
        DrawTrails(sim, _dt);
        DrawStuck(sim);
        DrawFallen(dt);
        DrawRings();
        DrawStandards(sim);
        DrawSplints();
    }

    void Track(Unit u, Look l, float dt)
    {
        if (!l.Seen) { l.Seen = true; l.Face = u.Facing; l.LastX = u.X; l.LastZ = u.Z; l.Phase = u.Id * 1.7f; l.Breath = u.Id * 2.3f; }
        float dx = u.X - l.LastX, dz = u.Z - l.LastZ;
        float moved = MathF.Sqrt(dx * dx + dz * dz);
        // a jump of more than a couple of voxels in a frame is no step (put somewhere, or a game read back)
        l.Dist = _m.Paused || moved > 3 ? 0 : moved;
        float speed = moved / Math.Max(dt, 1e-4f);
        float target = Math.Clamp(speed / (u.T.Move.Speed * NavGrid.CS * .6f), 0, 1);
        if (_m.Paused || u.Dead) target = l.Move * .9f;
        l.Move += (target - l.Move) * Math.Min(1, dt * 10);
        if (!_m.Paused && moved <= 3) l.Speed += (speed - l.Speed) * Math.Min(1, dt * 6);
        // a run: faster than its walk by a good deal (fleeing, charging, called to its post)
        float walk = u.T.Move.Speed * NavGrid.CS;
        l.Run += (Math.Clamp((l.Speed / Math.Max(walk, .1f) - 1.1f) / .45f, 0, 1) - l.Run) * Math.Min(1, dt * 4);
        l.LastX = u.X; l.LastZ = u.Z;
        float d = Mathf.Wrap(u.Facing - l.Face, -MathF.PI, MathF.PI);
        l.Face += d * Math.Min(1, dt * 12);
    }

    static void Grow(Kind k, int need)
    {
        while (k.Cap < need) k.Cap = Math.Max(8, k.Cap * 2);
        void Room(MultiMeshInstance3D mi)
        {
            if (mi == null) return;
            mi.Multimesh.InstanceCount = k.Cap;
            mi.Multimesh.VisibleInstanceCount = 0;
        }
        for (int p = 0; p < Figure.Bones; p++)
        {
            k.Bufs[p] = new float[k.Cap * 16];
            Room(k.Parts[p]); Room(k.Shades[p]);
        }
        for (int d = 0; d < k.Dress.Length; d++)
        {
            k.DressBufs[d] = new float[k.Cap * 16];
            Room(k.Dress[d]); Room(k.DressShades[d]);
        }
    }

    /// <summary>
    /// How the hand turns what it holds about its grip, on top of the arm's swing (identity: as it was sculpted in the
    /// fist). The place for a wrist's own turn — a blade laid flat, a thrust — when the blows want one.
    /// </summary>
    static Transform3D Wrist(Unit u, Figure f, int dress) => Transform3D.Identity;

    /// <summary>Folds a part of a figure to nothing: it is drawn elsewhere (a hurt part, on its own).</summary>
    static readonly Transform3D Folded = new(new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), new Vector3(0, -1000, 0));

    /// <summary>Works out the joints for this frame and writes the part transforms of one figure.</summary>
    /// <summary>Works out the joints for this frame: the part transforms go to _xf, the tint is returned.</summary>
    Color Pose(Unit u, Figure f, Look look)
    {
        float legR = 0, legL = 0, main = f.Carry, off = 0, lean = 0, bob = 0, fall = 0, sink = 0;
        // the joints a man moves at besides the shoulders and the hips (NaN: as the hip and the shoulder have them)
        float kneeR = float.NaN, kneeL = float.NaN, elbowR = float.NaN, elbowL = float.NaN, hipYaw = 0, chestYaw = 0;
        float seed = u.Id * .618f;
        if (Kinds.IsEngine(u.T))
        {
            // an engine does not walk: the ram's log swings as it batters, the catapult's arm flies up as it throws
            if (u.T == Kinds.Ram && u.State is UnitState.Carve or UnitState.Siege) main = MathF.Sin((float)_t * MathF.Tau / f.Chop + seed) * .28f;
            // thrown, the arm stands up against the beam a while, then is winched back down for the next (ours have
            // their pieces moved on their own: UnitView.Engines.cs)
            else if (u.T == Kinds.Catapult) main = u.Dead ? 0 : 1.9f * Math.Clamp(u.Cooldown / u.T.AttackTime * 1.5f, 0, 1);
            if (u.Dead) sink = Math.Max(0, (float)(_m.Sim.Time - u.DiedAt) - 18) * .8f;
        }
        else if (u.Dead)
        {
            float since = (float)(_m.Sim.Time - u.DiedAt);
            // (a man or a beast of four legs on the ground falls as a body, from the pose it stood in: UnitView.Corpse.cs)
            fall = (f.Jointed || f.Legged) && u.Airborne == null ? 0 : Math.Clamp(since / .45f, 0, 1);
            fall = 1 - (1 - fall) * (1 - fall);
            sink = Math.Max(0, since - 18) * 1.4f;
            main = f.Beast ? .5f : -.4f; off = f.Beast ? .4f : f.Shield ? -.2f : .3f;
            if (f.Beast) { legR = -.4f; legL = -.3f; }
            if (f.Legged) Stiff(u, f, 0);
            // a horse down on its side, its legs stretched out stiff
            if (f.Mounted) { legR = -.5f; legL = .45f; main = -.9f; off = -.6f; }
        }
        else if (u.Airborne != null)
        {
            // in the air: arms and legs flung about
            float flail = MathF.Sin((float)_t * 11 + seed);
            main = f.Beast ? .6f * flail : -2.4f + flail * .5f; off = f.Beast ? -.6f * flail : -2.2f - flail * .5f;
            legR = .5f + flail * .4f; legL = -.5f - flail * .4f;
            if (f.Legged) Stiff(u, f, flail);
        }
        // a beast with knees and a neck: its gait worked out step by step (UnitView.Gait.cs)
        else if (f.Legged) bob = Strides(u, f, look);
        else if (f.Beast)
        {
            // diagonal pairs of legs together; a gallop when bolting or charging
            float phase = u.Stride / f.Stride * MathF.Tau, a = look.Move;
            bool run = u.State is UnitState.Flee or UnitState.Fight;
            float sw = MathF.Sin(phase) * (run ? .85f : .45f) * a;
            main = sw; legL = sw; off = -sw; legR = -sw;
            bob = MathF.Abs(MathF.Sin(phase)) * a * (run ? .8f : .2f);
            if (a < .05f)
            {
                // grazing: now and then the head goes down to the grass
                float graze = MathF.Max(0, MathF.Sin((float)_t * .35f + seed * 3));
                lean = graze * graze * .38f;
            }
            if (u.State == UnitState.Fight) lean = -.08f + u.Swing * .3f;
        }
        else if (f.Mounted)
        {
            // a horse: at a walk its legs swing a little; at the gallop the forelegs reach out together and the hind legs
            // drive under, and the body rocks and rises with every bound. The rider has the reins in his left hand
            float phase = u.Stride / f.Stride * MathF.Tau, a = look.Move, gallop = Math.Clamp((a - .45f) / .35f, 0, 1);
            float sw = MathF.Sin(phase) * (.3f + .42f * gallop) * a;
            legR = sw; legL = -sw;
            bob = MathF.Abs(MathF.Sin(phase)) * a * (.15f + .75f * gallop);
            lean = MathF.Cos(phase) * .05f * gallop * a;
            off = f.Shield ? -.45f : -.6f;
            if (a < .05f)
            {
                // standing: it breathes, and now and then shifts its weight
                float breath = MathF.Sin((float)_t * 1.3f + seed);
                bob = breath * .08f;
                legL = MathF.Max(0, MathF.Sin((float)_t * .4f + seed * 2)) * .12f;
            }
            if (u.State == UnitState.Fight) { float l = 0; FightArms(u, f, seed, ref main, ref off, ref l); lean += l * .3f; }
            else if (u.State is UnitState.Work) { Chop(f.Chop / u.Pace, (float)_t + seed * f.Chop, out main, out _); }
        }
        else if (_m.Sim.Climbing(u))
        {
            // hand over hand up the ladder
            float phase = u.Y / 3f * MathF.Tau;
            main = -2.6f + MathF.Sin(phase) * .35f;
            off = -2.6f - MathF.Sin(phase) * .35f;
            legR = -.5f + MathF.Sin(phase) * .45f;
            legL = -.5f - MathF.Sin(phase) * .45f;
            // pulling up on the rungs, stepping up on them
            elbowR = -.5f - .4f * MathF.Max(0, MathF.Sin(phase)); elbowL = -.5f - .4f * MathF.Max(0, -MathF.Sin(phase));
            kneeR = .5f - legR * .9f; kneeL = .5f - legL * .9f;
        }
        else if (f.Jointed)
        {
            // the stride: a full step of each leg every so many voxels walked, longer at a run
            float a = look.Move, run = look.Run;
            look.Phase += look.Dist / (f.Stride * (1 + .35f * run)) * MathF.Tau;
            float phase = look.Phase, swing = (.42f + .22f * run) * a;
            legR = -MathF.Sin(phase) * swing;
            legL = -legR;
            // the knee bends as the leg swings through, most as it passes under the body, and gives a little under the
            // weight as the foot comes down; running, the heel comes up high behind
            float lift = .7f + .65f * run;
            kneeR = a * (.1f + lift * MathF.Pow(MathF.Max(0, MathF.Cos(phase)), 1.5f) + .12f * run);
            kneeL = a * (.1f + lift * MathF.Pow(MathF.Max(0, -MathF.Cos(phase)), 1.5f) + .12f * run);
            // the arms swing against the legs, the elbows bent the more the faster; a priest walks with its staff
            float arm = u.T.Prays ? .2f : .62f + .25f * run;
            main += -legR * arm;
            off = f.Shield ? -.3f - legL * .2f : -legL * arm;
            elbowR = -(.18f + 1.05f * run * a + .3f * MathF.Max(0, -(main - f.Carry)));
            elbowL = f.Shield ? -1.0f : -(.18f + 1.05f * run * a + .3f * MathF.Max(0, -off));
            // the hips turn with the leg going forward, the chest against them; the body rises over the leg it stands on
            hipYaw = .11f * MathF.Sin(phase) * a;
            chestYaw = -.08f * MathF.Sin(phase) * a;
            bob = (MathF.Abs(MathF.Cos(phase)) - .64f) * a * (.5f + .35f * run);
            // leaning into the walk, more into a run
            lean = .04f * a + .17f * run;
            if (u.Bound && a >= .05f) { main = .55f; off = .55f; lean = .08f; elbowR = elbowL = -.4f; }
            switch (u.State)
            {
                case UnitState.Work when u.T.Prays:
                {
                    float k = u.Rite != null ? Math.Clamp(u.Rite.Done / Sim.RiteTime, 0, 1) : 0, t = (float)_t;
                    main = -.15f;
                    off = k < .85f ? -2.9f + MathF.Sin(t * 2.3f + seed) * .12f : Mathf.Lerp(-2.9f, -.5f, (k - .85f) / .15f);
                    lean = k < .85f ? -.1f : Mathf.Lerp(-.1f, .22f, (k - .85f) / .15f);
                    elbowR = -.35f; elbowL = -.1f;
                    break;
                }
                case UnitState.Work when u.JobI < u.Jobs.Count:
                    Labour(u, f, ref main, ref off, ref lean, ref elbowR, ref elbowL, ref kneeR, ref kneeL);
                    break;
                // one of an engine's crew at the windlass, the shot or the pins (UnitView.Engines.cs)
                case UnitState.Work when u.Escort is { } engine && Kinds.IsOurEngine(engine.T):
                    Crewing(u, engine, f, seed, ref main, ref off, ref lean, ref elbowR, ref elbowL, ref kneeR, ref kneeL);
                    break;
                // an alchemist standing to throw: the flask drawn back over the shoulder and lobbed
                case UnitState.Siege when u.T.Tool == "flask":
                    FightArms(u, f, seed, ref main, ref off, ref lean);
                    break;
                case UnitState.Work:
                case UnitState.Carve:
                case UnitState.Siege:
                    Chop(f.Chop / u.Pace, (float)_t + seed * f.Chop, out main, out lean);
                    off = -.35f;
                    // the hands together on the haft, the knees giving a little with the blow
                    elbowR = -.12f; elbowL = -.9f;
                    kneeR = kneeL = .18f + .12f * MathF.Max(0, lean);
                    break;
                case UnitState.Fight:
                    FightArms(u, f, seed, ref main, ref off, ref lean);
                    // a fighter's stance: the knees bent, the guard arm's elbow up
                    kneeR = MathF.Max(kneeR, .22f); kneeL = MathF.Max(kneeL, .22f);
                    break;
                default:
                    if (a < .05f)
                    {
                        if (u.State == UnitState.Idle) Leisure(u, f, seed, ref main, ref off, ref legR, ref legL, ref lean, ref bob);
                        // standing at ease: the weight on one leg, the other knee easy, shifted now and then
                        if (u.State != UnitState.Idle || u.Pastime is Pastime.None or Pastime.Talk or Pastime.Watch or Pastime.Warm or Pastime.Pray)
                        {
                            float shift = MathF.Sin((float)_t * .23f + seed * 4);
                            kneeR = .06f + .16f * MathF.Max(0, shift);
                            kneeL = .06f + .16f * MathF.Max(0, -shift);
                            hipYaw = .05f * shift;
                        }
                        if (u.Pastime == Pastime.Bound)
                        {
                            // on its knees, sitting back on its heels, the hands tied behind, the head bowed
                            bob = -f.Shin * .95f; legR = legL = .08f; kneeR = kneeL = 1.55f; elbowR = elbowL = -.3f;
                        }
                        else if (u.Pastime == Pastime.Sit && u.SeatH > 0)
                        {
                            // on a bench (Sim.Yard.cs): the thighs along the seat, the shins down, the hands on the knees
                            bob = u.SeatH + .25f - (f.Thigh + f.Shin); legR = -1.5f; legL = -1.42f; kneeR = 1.5f; kneeL = 1.38f; elbowR = elbowL = -.6f;
                            main = off = -.55f;
                        }
                        else if (u.Pastime == Pastime.Sit)
                        {
                            // on the ground, the knees drawn up
                            bob = -(f.Thigh + f.Shin) * .78f; legR = -1.25f; legL = -1.15f; kneeR = .9f; kneeL = 1.05f; elbowR = elbowL = -.5f;
                        }
                        else if (u.Pastime == Pastime.Warm) { elbowR = elbowL = -.9f; }
                        else if (u.Pastime == Pastime.Talk) { elbowR = -.9f; }
                    }
                    break;
            }
            // a great one at a throw: stooping to the stone, up with it, over the head and away (UnitView.Hurl.cs)
            bool hurl = HurlPose(u, f, ref main, ref off, ref lean, ref bob, ref elbowR, ref elbowL, ref kneeR, ref kneeL, ref legR, ref legL);
            // the feet on the ground under them, as the stride sets them down (UnitView.Gait.cs): walking, and standing
            // where the ground is not level; not at work, in a fight, sitting or bound
            if (!hurl && u.State is not (UnitState.Work or UnitState.Carve or UnitState.Siege or UnitState.Fight) && !u.Bound
                && u.Pastime is not (Pastime.Sit or Pastime.Bound))
                bob = Footing(u, f, look, bob, ref legR, ref legL, ref kneeR, ref kneeL);
        }
        else
        {
            float phase = u.Stride / f.Stride * MathF.Tau;
            float a = look.Move;
            legR = -MathF.Sin(phase) * .6f * a;
            legL = -legR;
            // a priest walks with its staff, not swinging it
            main += -legR * (u.T.Prays ? .2f : .7f);
            // the shield is carried before the body, not swung into it
            off = f.Shield ? -.3f - legL * .2f : -legL * .7f;
            bob = (MathF.Abs(MathF.Cos(phase)) - .64f) * a * .55f;
            // bound, it walks where it is led with its hands tied behind it
            if (u.Bound && a >= .05f) { main = .55f; off = .55f; lean = .08f; }
            switch (u.State)
            {
                case UnitState.Work when u.T.Prays:
                {
                    // the rite: the staff planted, the other hand raised over the one given, and brought down at the end
                    float k = u.Rite != null ? Math.Clamp(u.Rite.Done / Sim.RiteTime, 0, 1) : 0, t = (float)_t;
                    main = -.15f;
                    off = k < .85f ? -2.9f + MathF.Sin(t * 2.3f + seed) * .12f : Mathf.Lerp(-2.9f, -.5f, (k - .85f) / .15f);
                    lean = k < .85f ? -.1f : Mathf.Lerp(-.1f, .22f, (k - .85f) / .15f);
                    break;
                }
                case UnitState.Siege when u.T.Tool == "flask":
                    FightArms(u, f, seed, ref main, ref off, ref lean);
                    break;
                case UnitState.Work:
                case UnitState.Carve:
                case UnitState.Siege:
                    Chop(f.Chop / u.Pace, (float)_t + seed * f.Chop, out main, out lean);
                    off = -.35f;
                    break;
                case UnitState.Fight:
                    FightArms(u, f, seed, ref main, ref off, ref lean);
                    break;
                default:
                    if (a < .05f)
                    {
                        float breath = MathF.Sin((float)_t * 1.7f + seed);
                        bob = breath * .12f;
                        main += breath * .04f;
                        off = -breath * .04f;
                        if (u.State == UnitState.Idle) Leisure(u, f, seed, ref main, ref off, ref legR, ref legL, ref lean, ref bob);
                    }
                    break;
            }
        }

        // off its feet with its wounds: down it goes, and up again by and by, as the dead go down
        if (f.Jointed && !u.Dead)
        {
            // (going down by the time since it fell, so that one down lies down in a game stopped or just loaded too)
            if (u.Down) look.Down = Math.Max(look.Down, 1 - MathF.Exp(-(float)(_m.Sim.Time - u.DownSince) * 3.5f));
            else look.Down -= look.Down * Math.Min(1, _dt * 1.4f);
            if (look.Down > .01f) fall = Math.Max(fall, Smooth(look.Down));
        }
        // up on a comrade's shoulder: laid across it, not lying down (UnitView.Carry.cs)
        bool borne = f.Jointed && Sim.OnShoulder(u);
        if (borne) fall = 0;
        // lying in wait: low in the grass, the top of the head over it (Sim.Ambush.cs)
        if (u.Lurking && !u.Dead) sink += u.T.BodyH * .62f;
        var at = new Vector3(_w.WX(u.X), _w.WY(u.Y + bob - sink), _w.WZ(u.Z));
        // a leader stands a little taller than its kind
        var root = new Transform3D(new Basis(Vector3.Up, MathF.PI / 2 - look.Face).Scaled(Vector3.One * VoxelWorld.Vox * (u.Leader ? 1.15f : 1)), at);
        if (borne) root = Shouldered(u, f);
        // thrown through the air, it tumbles about its middle as it flies
        if (u.Airborne is { } fl && new Vector3(fl.AX, fl.AY, fl.AZ) is var axis && axis.LengthSquared() > 1e-4f)
        {
            var mid = at + Vector3.Up * u.T.BodyH * .5f * VoxelWorld.Vox;
            var spin = new Basis(axis.Normalized(), fl.Turn);
            root = new Transform3D(spin, mid - spin * mid) * root;
        }
        // the fallen lie down: on the back, or a beast (and a horse with its rider) on its side
        if (fall > 0)
            root *= f.Beast || f.Mounted
                ? new Transform3D(new Basis(Vector3.Back, MathF.PI / 2 * fall), new Vector3(0, fall * (u.T.BodyR * .7f), 0))
                : new Transform3D(new Basis(Vector3.Right, -MathF.PI / 2 * fall), new Vector3(0, fall * (u.T.BodyR + .5f), 0));
        // a blow: the arms turned every way at the shoulders, the trunk twisted, the step taken (UnitView.Strokes.cs)
        var turns = StrokeTurns(u, f, look, main, off, lean, ref legR, ref legL, root);
        var hips = turns.Step == Vector3.Zero ? root : root * new Transform3D(Basis.Identity, turns.Step);
        var tint = Tint(u);
        if (f.Legged)
        {
            PoseLegs(f, hips, u.Dead ? 0 : MathF.Sin((float)_t * (look.Run > .3f ? 4.5f : 1.6f) + seed));
            look.Body = _xf[(int)Bone.Torso];
            return tint;
        }
        if (!f.Jointed)
        {
            var body0 = hips * TurnAt(f, Part.Body, turns.Body);
            look.Body = body0;
            // a horse's legs go with its body as it rocks
            var arms0 = f.Beast ? hips : body0;
            var legs0 = f.Mounted ? body0 : hips;
            _xf[(int)Bone.Torso] = body0;
            _xf[(int)Bone.UpperR] = arms0 * TurnAt(f, Part.Main, turns.Main);
            _xf[(int)Bone.UpperL] = arms0 * TurnAt(f, Part.Off, turns.Off);
            _xf[(int)Bone.ThighR] = legs0 * Swing(f, Part.LegR, legR);
            _xf[(int)Bone.ThighL] = legs0 * Swing(f, Part.LegL, legL);
            if (Kinds.IsOurEngine(u.T)) OurEngine(u, f, body0);
            return tint;
        }
        // the knees and the elbows, where the pose did not set them: a thigh raised forward lets its shin hang, an arm
        // hangs easy; in a blow (or just after one) the striking arm goes straight, along the stroke the simulation laid out
        if (float.IsNaN(kneeR)) kneeR = MathF.Max(0, -legR) * .9f + .05f;
        if (float.IsNaN(kneeL)) kneeL = MathF.Max(0, -legL) * .9f + .05f;
        if (float.IsNaN(elbowR)) elbowR = -.2f;
        if (float.IsNaN(elbowL)) elbowL = f.Shield ? -.9f : -.2f;
        elbowR *= 1 - turns.Fight; if (!f.Shield) elbowL *= 1 - turns.Fight * .7f;
        if (u.Dead || fall > 0) { kneeR = kneeL = .25f * fall; elbowR = elbowL = -.3f * fall; }
        // what the wounds and the blows do to the pose: getting out of the way, reeling, hurt, dazed
        Reacting(u, f, look, ref turns, ref kneeR, ref kneeL, ref elbowR, ref elbowL, ref legR, ref legL, out float leanAdd, out float rollAdd, out var shove, out float headAdd);
        // carried, carrying, helped along, helping (UnitView.Carry.cs)
        Aiding(u, f, ref turns, ref kneeR, ref kneeL, ref elbowR, ref elbowL, ref legR, ref legL, ref leanAdd);
        if (shove != Vector3.Zero) hips *= new Transform3D(Basis.Identity, shove);
        // the breath: slow at rest, quick and deep after a run or in a fight, and it takes a while to settle
        float exert = u.Dead ? 0 : look.Run > .3f ? 1 : u.State == UnitState.Fight ? .8f : u.State is UnitState.Work or UnitState.Carve or UnitState.Siege ? .45f : look.Move > .2f ? .25f : 0;
        look.Exert += (exert - look.Exert) * Math.Min(1, _dt * (exert > look.Exert ? 1.2f : .12f));
        look.Breath += _dt * (1.5f + 3.2f * look.Exert);
        float inhale = u.Dead ? 0 : MathF.Sin(look.Breath) * (.55f + .9f * look.Exert);
        // where the head turns: at whom it fights, what it works, what it hunts, about it when idle
        Gaze(u, f, look);
        var waist = f.Pivot[(int)Bone.Torso];
        var legs = hips * RotAt(waist, Vector3.Up, hipYaw);
        var body = legs * TurnAt(f, Part.Body, turns.Body) * RotAt(waist, Vector3.Right, leanAdd) * RotAt(waist, Vector3.Back, rollAdd)
            * RotAt(waist, Vector3.Up, chestYaw + look.HeadYaw * .25f);
        look.Body = body;
        // the chest swells with the breath and the shoulders and the head rise with it
        var chest = body * new Transform3D(Basis.Identity, waist) * new Transform3D(Basis.FromScale(new Vector3(1 + .018f * inhale, 1 + .008f * inhale, 1 + .03f * inhale)), Vector3.Zero)
            * new Transform3D(Basis.Identity, -waist);
        var top = body * new Transform3D(Basis.Identity, new Vector3(0, .008f * inhale * f.NeckUp, .01f * inhale));
        _xf[(int)Bone.Torso] = chest;
        // the head: turned and tilted to what it looks at, holding the eyes level against the trunk's lean
        var neck = f.Pivot[(int)Bone.Head];
        float tilt = look.HeadPitch - (turns.Lean + lean + leanAdd) * .5f + headAdd;
        _xf[(int)Bone.Head] = top * new Transform3D(new Basis(Vector3.Up, look.HeadYaw * .75f) * new Basis(Vector3.Right, tilt), neck - new Basis(Vector3.Up, look.HeadYaw * .75f) * new Basis(Vector3.Right, tilt) * neck);
        var upperR = top * TurnAt(f, Part.Main, turns.Main);
        var upperL = top * TurnAt(f, Part.Off, turns.Off);
        _xf[(int)Bone.UpperR] = upperR;
        _xf[(int)Bone.LowerR] = upperR * RotAt(f.Pivot[(int)Bone.LowerR], Vector3.Right, elbowR);
        _xf[(int)Bone.UpperL] = upperL;
        _xf[(int)Bone.LowerL] = upperL * RotAt(f.Pivot[(int)Bone.LowerL], Vector3.Right, elbowL);
        var thighR = legs * Swing(f, Part.LegR, legR);
        var thighL = legs * Swing(f, Part.LegL, legL);
        _xf[(int)Bone.ThighR] = thighR;
        _xf[(int)Bone.ShinR] = thighR * RotAt(f.Pivot[(int)Bone.ShinR], Vector3.Right, kneeR);
        _xf[(int)Bone.ThighL] = thighL;
        _xf[(int)Bone.ShinL] = thighL * RotAt(f.Pivot[(int)Bone.ShinL], Vector3.Right, kneeL);
        return tint;
    }

    /// <summary>
    /// A pawn at its work, by the blow it is at (<see cref="Unit.Beat"/>, the tool comes down at 1, when the simulation
    /// takes the voxel out or sets it in): digging, the tool drawn up over the shoulder and brought down hard on the very
    /// voxel worked, the other hand on the haft, bending low to what is low; laying, a stone taken up from beside the
    /// feet, carried to its place and pressed down into it.
    /// </summary>
    void Labour(Unit u, Figure f, ref float main, ref float off, ref float lean, ref float elbowR, ref float elbowL, ref float kneeR, ref float kneeL)
    {
        var j = u.Jobs[u.JobI];
        bool dig = Orders.IsRemove(j.m);
        // the voxel from the shoulder, in the figure's own frame: how far ahead and how far up
        float dx = j.x + .5f - u.X, dz = j.z + .5f - u.Z, ahead = MathF.Max(.5f, MathF.Sqrt(dx * dx + dz * dz));
        float up = j.y + .5f - (u.Y + f.Joint[(int)Part.Main].Y);
        // what is low is reached by bending: the trunk leans in and the knees give, and the arm need come down less far
        float low = Math.Clamp(-up / MathF.Max(1, f.Joint[(int)Part.Main].Y) - .45f, 0, 1);
        float bend = low * .55f;
        // the arm's angle at which the tool's head (or the hand) is on the voxel: 0 straight down, more negative higher
        float at = -MathF.Atan2(ahead + bend * 4, -(up + bend * 5));
        float c = u.Beat;
        if (dig)
        {
            // drawn up slowly over the shoulder; brought down fast, the knees giving with it; a jar and back
            float over = -2.85f;
            if (c < .62f)
            {
                float s = Smooth(c / .62f);
                main = Mathf.Lerp(at, over, s);
                lean = Mathf.Lerp(bend + .1f, -.08f, s);
                elbowR = Mathf.Lerp(-.1f, -.75f, s);
            }
            else
            {
                float s = (c - .62f) / .38f; s *= s;
                main = Mathf.Lerp(over, at, s);
                lean = Mathf.Lerp(-.08f, bend + .14f, s);
                elbowR = Mathf.Lerp(-.75f, -.05f, s);
            }
            // the other hand lower on the same haft
            off = main * .92f + .1f;
            elbowL = elbowR - .35f;
            kneeR = kneeL = .15f + low * .5f + .12f * MathF.Max(0, lean);
        }
        else
        {
            // a stone up from beside the feet, carried to its place, pressed down into its bed
            if (c < .4f)
            {
                float s = Smooth(c / .4f);
                main = Mathf.Lerp(at * .3f, .25f, s);
                lean = Mathf.Lerp(.15f, .38f, s);
                elbowR = elbowL = Mathf.Lerp(-.6f, -.25f, s);
                kneeR = kneeL = Mathf.Lerp(.25f, .65f, s);
            }
            else if (c < .85f)
            {
                float s = Smooth((c - .4f) / .45f);
                main = Mathf.Lerp(.25f, at - .25f, s);
                lean = Mathf.Lerp(.38f, bend + .1f, s);
                elbowR = elbowL = Mathf.Lerp(-.25f, -1.05f, s);
                kneeR = kneeL = Mathf.Lerp(.65f, .15f + low * .5f, s);
            }
            else
            {
                float s = Smooth((c - .85f) / .15f);
                main = Mathf.Lerp(at - .25f, at, s);
                lean = bend + .1f + .05f * s;
                elbowR = elbowL = Mathf.Lerp(-1.05f, -.55f, s);
                kneeR = kneeL = .15f + low * .5f;
            }
            // both hands on the stone
            off = main;
        }
    }

    /// <summary>
    /// The pose over what it is doing, from what the fight does to it: out of the way of a blow (a step back, a lean
    /// aside, ducking under it, the shield up into it, its own blade up across it); reeling back from a hard one; the
    /// free hand to the wound and the hurt leg kept stiff; bent with the pain; swaying, dazed. All of it eased in and out.
    /// </summary>
    void Reacting(Unit u, Figure f, Look look, ref Turns turns, ref float kneeR, ref float kneeL, ref float elbowR, ref float elbowL,
        ref float legR, ref float legL, out float lean, out float roll, out Vector3 shift, out float head)
    {
        lean = 0; roll = 0; shift = Vector3.Zero; head = 0;
        if (u.Dead || look.Down > .5f) return;
        double now = _m.Sim.Time;
        // getting out of the way: quick in, held while the blade goes by, eased out
        float t = (float)(now - u.ReactAt), w = t < 0 ? 0 : t < .07f ? t / .07f : t < .3f ? 1 : Math.Max(0, 1 - (t - .3f) / .22f);
        w = Smooth(w);
        if (w > 0)
            switch (u.React)
            {
                case Guarded.StepBack:
                    lean -= .28f * w; shift += new Vector3(0, 0, -1.6f * w); legR += .35f * w; kneeR += .3f * w; kneeL += .15f * w; head -= .15f * w;
                    break;
                case Guarded.Dodge:
                    roll += .38f * w * u.ReactSide; shift += new Vector3(1.3f * w * u.ReactSide, -.3f * w, 0); kneeR += .25f * w; kneeL += .25f * w; lean -= .05f * w;
                    break;
                case Guarded.Duck:
                    kneeR += 1.05f * w; kneeL += 1.05f * w; legR -= .55f * w; legL -= .55f * w; lean += .5f * w; shift += new Vector3(0, -f.Thigh * .42f * w, 0); head += .2f * w;
                    break;
                case Guarded.Block:
                    turns.Off = turns.Off.Slerp(new Quaternion(Vector3.Right, -1.65f), w); elbowL = Mathf.Lerp(elbowL, -1.3f, w); lean += .1f * w; kneeR += .15f * w; kneeL += .2f * w;
                    break;
                case Guarded.Parry:
                    turns.Main = turns.Main.Slerp(new Quaternion(Vector3.Right, -1.95f) * new Quaternion(Vector3.Up, .5f), w); elbowR = Mathf.Lerp(elbowR, -.45f, w); lean -= .06f * w;
                    break;
            }
        // reeling back from a hard blow: the trunk thrown back, the head snapped, the arms flung, a foot back to catch it
        float reel = (float)Math.Clamp((u.StaggerUntil - now) / .55, 0, 1);
        if (reel > 0)
        {
            float k = Smooth(reel);
            lean -= .32f * k; head -= .35f * k;
            turns.Main = turns.Main.Slerp(new Quaternion(Vector3.Right, .35f) * new Quaternion(Vector3.Back, -.35f), k * .6f);
            turns.Off = turns.Off.Slerp(new Quaternion(Vector3.Right, .3f) * new Quaternion(Vector3.Back, .35f), k * .6f);
            legL += .3f * k; kneeL += .3f * k; kneeR += .15f * k;
        }
        // hurt: bent with it, the free hand to the wound (unless the fight wants it), the hurt leg kept stiff
        float pain = Math.Min(1, u.Pain);
        lean += .16f * pain; head += .22f * pain;
        float clutch = (float)Math.Clamp((u.ClutchUntil - now) / 1.2, 0, 1) * (1 - turns.Fight * .8f);
        if (clutch > 0 && !f.Shield)
        {
            var (to, bend) = u.Clutch switch
            {
                9 => (-2.55f, -1.95f), (byte)BodyPart.Main => (-.75f, -1.45f), (byte)BodyPart.LegR or (byte)BodyPart.LegL => (.15f, -.25f), _ => (-.95f, -1.75f),
            };
            turns.Off = turns.Off.Slerp(new Quaternion(Vector3.Right, to), Smooth(clutch));
            elbowL = Mathf.Lerp(elbowL, bend, Smooth(clutch));
        }
        if (u.Clutch is (byte)BodyPart.LegR && now < u.ClutchUntil + 20) kneeR *= .35f;
        if (u.Clutch is (byte)BodyPart.LegL && now < u.ClutchUntil + 20) kneeL *= .35f;
        // dazed: the head lolling, the body swaying on its feet
        if (now < u.DazedUntil)
        {
            float d = (float)Math.Min(1, (u.DazedUntil - now) / 1.5), tt = (float)_t * 2.3f + u.Id;
            roll += .12f * d * MathF.Sin(tt); head += .2f * d * MathF.Sin(tt * 1.7f); lean += .06f * d * MathF.Sin(tt * .8f);
        }
        // broken bones: the arm hanging, the limp, bent over the ribs (UnitView.Trauma.cs)
        Injured(u, f, look, ref turns, ref kneeR, ref kneeL, ref elbowR, ref elbowL, ref lean, ref roll, ref shift, ref head);
    }

    /// <summary>A turn about an axis through a point (in figure voxels).</summary>
    static Transform3D RotAt(Vector3 at, Vector3 axis, float angle)
    {
        if (angle == 0) return Transform3D.Identity;
        var b = new Basis(axis, angle);
        return new Transform3D(b, at - b * at);
    }

    /// <summary>
    /// Where the head turns this frame, eased towards what it looks at: the foe it fights, the voxel it works, the beast
    /// it hunts; walking, ahead, with a glance aside now and then; idle, about it, and now and then at someone near.
    /// The angles are the head's own, yaw about the neck and pitch down; the trunk turns a little with a wide look.
    /// </summary>
    void Gaze(Unit u, Figure f, Look look)
    {
        float yaw = 0, pitch = 0;
        var sim = _m.Sim;
        float ex = u.X, ey = u.Y + u.T.BodyH * .88f, ez = u.Z;
        bool at = false;
        float tx = 0, ty = 0, tz = 0;
        if (u.Dead || u.Airborne != null) { }
        else if (u.Target is { Dead: false } foe && (u.State == UnitState.Fight || Dist2(u, foe) < 30 * 30)) { tx = foe.X; ty = foe.Y + foe.T.BodyH * .8f; tz = foe.Z; at = true; }
        else if (u.Prey is { Dead: false } prey) { tx = prey.X; ty = prey.Y + prey.T.BodyH * .6f; tz = prey.Z; at = true; }
        else if (u.State == UnitState.Work && u.JobI < u.Jobs.Count) { var j = u.Jobs[u.JobI]; tx = j.x + .5f; ty = j.y + .5f; tz = j.z + .5f; at = true; }
        else
        {
            // now and then something else to look at: about, or someone near
            if (_m.Sim.Time >= look.GazeNext)
            {
                double rnd = Hash01(u.Id, (int)(sim.Time * 3));
                look.GazeNext = sim.Time + 1.6 + rnd * 3.4;
                bool walking = look.Move > .2f;
                look.GazeAt = null;
                if (!walking && rnd < .4)
                {
                    float best = 10 * 10 * VoxelWorld.PerMetre;
                    foreach (var o in sim.Units)
                        if (o != u && !o.Dead && Dist2(u, o) is var d2 && d2 < best && d2 > 4) { best = d2; look.GazeAt = o; }
                }
                look.IdleYaw = walking ? (float)(Hash01(u.Id, (int)(sim.Time * 7)) - .5) * (rnd < .7 ? .3f : 1.3f) : (float)(Hash01(u.Id, (int)(sim.Time * 5)) - .5) * 2.1f;
                look.IdlePitch = (float)(Hash01(u.Id + 7, (int)(sim.Time * 5)) - .45) * .35f;
            }
            if (look.GazeAt is { Dead: false } g) { tx = g.X; ty = g.Y + g.T.BodyH * .85f; tz = g.Z; at = true; }
            else { yaw = look.IdleYaw; pitch = look.IdlePitch; }
        }
        if (at)
        {
            float dx = tx - ex, dz = tz - ez, dy = ty - ey;
            yaw = -Mathf.Wrap(MathF.Atan2(dz, dx) - look.Face, -MathF.PI, MathF.PI);
            pitch = -MathF.Atan2(dy, MathF.Max(1, MathF.Sqrt(dx * dx + dz * dz)));
        }
        yaw = Math.Clamp(yaw, -1.2f, 1.2f);
        pitch = Math.Clamp(pitch, -.6f, .75f);
        float k = Math.Min(1, _dt * (at ? 7 : 3.5f));
        look.HeadYaw += (yaw - look.HeadYaw) * k;
        look.HeadPitch += (pitch - look.HeadPitch) * k;
    }

    static float Dist2(Unit a, Unit b) => (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z);

    static double Hash01(int a, int b) { unchecked { uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xFFFFFF) / (double)0x1000000; } }

    /// <summary>
    /// The arms in a fight, by what is in them: a bow drawn; a crossbow spanned with its nose to the ground, then
    /// levelled at the shoulder and kicking as it looses; a sling whirled over the head and let fly; a pot drawn back
    /// over the shoulder and lobbed; a spear or a lance drawn back and thrust home; anything else raised and brought down.
    /// </summary>
    void FightArms(Unit u, Figure f, float seed, ref float main, ref float off, ref float lean)
    {
        string tool = u.T.Tool;
        if (u.T.Ranged && tool == "crossbow")
        {
            float k = u.Cooldown / Math.Max(.1f, u.T.AttackTime), span = Math.Clamp((k - .35f) / .2f, 0, 1);
            main = Mathf.Lerp(-1.42f + u.Swing * .25f, -.2f, span);
            off = Mathf.Lerp(-1.3f, -.45f, span);
            lean = span * .18f;
        }
        else if (u.T.Ranged && tool == "sling")
        {
            float whirl = MathF.Sin((float)_t * 14 + seed);
            main = u.Swing > .05f ? Mathf.Lerp(-2.6f, -1.1f, u.Swing) : -2.75f + whirl * .35f;
            off = -.9f;
            lean = u.Swing * .12f;
        }
        else if (u.T.Ranged && tool == "bombard" || tool == "flask")
        {
            float k = 1 - Math.Clamp(u.Cooldown / 1.2f, 0, 1);
            main = Mathf.Lerp(-.5f, -2.8f, k * k);
            main = Mathf.Lerp(main, -1.3f, u.Swing);
            off = -.6f;
            lean = u.Swing * .12f - k * .05f;
        }
        else if (u.T.Ranged)
        {
            off = -1.5f;
            main = -1.42f + u.Swing * .3f;
        }
        else if (tool is "spear" or "lance")
        {
            float draw = u.Cooldown < .45f ? Math.Clamp(1 - u.Cooldown / .45f, 0, 1) : 0;
            main = Mathf.Lerp(f.Carry, f.Carry + .3f, draw);
            main = Mathf.Lerp(main, f.Carry - .2f, u.Swing);
            if (!f.Mounted) off = -.75f - u.Swing * .15f;
            lean = u.Swing * .22f - draw * .08f;
        }
        else
        {
            float raise = u.Cooldown < .45f ? Math.Clamp(1 - u.Cooldown / .45f, 0, 1) : 0;
            main = Mathf.Lerp(-.65f, -2.7f, raise);
            main = Mathf.Lerp(main, .15f, u.Swing);
            lean = u.Swing * .14f - raise * .05f;
            off = f.Shield ? f.Guard : -.45f;
        }
    }

    /// <summary>
    /// A hurt body: the parts its wounds have changed, and what hangs on them, drawn on their own and posed like any
    /// other, in the kind's tint. A part a wound has changed is meshed again at once (not the others); one only to be
    /// drawn at another distance takes the meshes kept from when it was last drawn there, or waits its turn for new
    /// ones while there is time in the frame (<see cref="Spare"/>), drawn as it was meanwhile. Returns which parts are
    /// drawn here.
    /// </summary>
    bool[] DrawOwn(Unit u, Figure fig, Color tint)
    {
        var w = u.Wounds;
        var plan = w.Plan;
        // (one come back from a game read in, of another look, is drawn anew)
        if (_own.TryGetValue(u.Id, out var own) && own.Of != null && own.Of.Plan != plan) { Free(own); own = null; }
        if (own == null) _own[u.Id] = own = new Own { Dress = new MeshInstance3D[plan.Dress.Count], Tint = tint };
        if (own.Of != w || own.Rev != w.Rev)
        {
            own.Of = w; own.Rev = w.Rev;
            for (int p = 0; p < Figure.Parts; p++)
            {
                var g = w.Grid[p]; var g0 = plan.Grid[p];
                bool hurt = g != g0 && (g == null || g0 == null || g.Count != g0.Count || !g.M.AsSpan().SequenceEqual(g0.M));
                int sum = Sum(g);
                var (upper, lower) = Figure.BonesOf((Part)p);
                if (own.At[(int)upper] >= 0 && hurt == own.Hurt[p] && sum == own.Sums[p]) continue;
                own.Hurt[p] = hurt; own.Sums[p] = sum;
                foreach (var b in new[] { (int)upper, (int)lower })
                {
                    own.At[b] = -1;
                    for (int l = 0; l < 3; l++) own.Kept[b, l] = null;
                }
            }
        }
        for (int b = 0; b < Figure.Bones; b++)
        {
            if (own.At[b] == _lod) continue;
            if (own.At[b] >= 0 && own.Kept[b, _lod] == null && _meshing >= Spare) continue;
            Draw(own, plan, fig, b, w.Grid[(int)Figure.PartOf[b]]);
        }
        if (tint != own.Tint)
        {
            own.Tint = tint;
            foreach (var n in own.Parts) n?.SetInstanceShaderParameter(Tinted, tint);
            foreach (var n in own.Dress) n?.SetInstanceShaderParameter(Tinted, tint);
        }
        if (own.Hidden) { own.Hidden = false; Show(own, true); }
        for (int b = 0; b < Figure.Bones; b++) if (own.Parts[b] != null) own.Parts[b].Transform = _xf[b];
        bool stowed = Stowed(u);
        for (int d = 0; d < own.Dress.Length; d++) if (own.Dress[d] != null) own.Dress[d].Transform = stowed ? Folded : _xf[(int)fig.DressBone[d]] * Wrist(u, fig, d);
        // for the arrows stuck in it
        for (int b = 0; b < Figure.Bones; b++) look_xf[u.Id * Figure.Bones + b] = _xf[b];
        return own.Hurt;
    }

    /// <summary>
    /// One part of a hurt body drawn at the distance the figures are drawn at now, and what hangs on it: from the meshes
    /// kept for it there, or new ones made (nothing, for a part no wound has changed: its kind draws it).
    /// </summary>
    void Draw(Own own, BodyPlan plan, Figure fig, int b, PartGrid g)
    {
        int lod = _lod, p = (int)Figure.PartOf[b];
        var made = own.Kept[b, lod];
        if (made == null)
        {
            long t0 = Stopwatch.GetTimestamp();
            made = new Made { Dress = new Mesh[plan.Dress.Count] };
            if (own.Hurt[p])
            {
                bool has = fig.BoneNear[b] != null || fig.BoneMid[b] != null || fig.BoneFar[b] != null;
                made.Body = !has ? null : lod == 0 ? GodotMesh.From(fig.NearOf(plan, b, g)) : lod == 1 ? GodotMesh.From(fig.MidOf(plan, b, g))
                    : Figure.CoarseOf(fig.Keep(plan, (Bone)b, plan.Seen(p, g)));
                for (int d = 0; d < plan.Dress.Count && lod < 2; d++)
                    if ((int)fig.DressBone[d] == b)
                        made.Dress[d] = GodotMesh.From(lod == 0 ? Figure.DressOf(plan, d, g, out _) : Figure.MidOf(plan, p, g, d + 1, out _));
            }
            own.Kept[b, lod] = made;
            _meshing += Stopwatch.GetTimestamp() - t0;
        }
        Put(ref own.Parts[b], made.Body, lod == 0 ? BodyPlan.FineSub : lod == 1 ? BodyPlan.Sub : 1, lod, own);
        for (int d = 0; d < plan.Dress.Count; d++)
            if ((int)fig.DressBone[d] == b) Put(ref own.Dress[d], made.Dress[d], lod == 0 ? plan.Dress[d].Sub : BodyPlan.Sub, lod, own);
        own.At[b] = lod;
    }

    /// <summary>The figures' shader's tint for one drawn on its own (its kind's figures have theirs from the MultiMesh).</summary>
    static readonly StringName Tinted = "tint";

    /// <summary>
    /// A node of a hurt body given its mesh (made the first time one is wanted), or hidden when there is none: its
    /// material for the cells it is made of, its shadow unless it is drawn far.
    /// </summary>
    void Put(ref MeshInstance3D node, Mesh mesh, int grid, int lod, Own own)
    {
        if (node == null && mesh == null) return;
        if (node == null)
        {
            node = new MeshInstance3D();
            AddChild(node);
            node.SetInstanceShaderParameter(Tinted, own.Tint);
        }
        node.Mesh = mesh;
        node.Visible = mesh != null && !own.Hidden;
        node.MaterialOverride = Figure.Material(grid);
        node.CastShadow = lod == 2 ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On;
    }

    static void Show(Own own, bool on)
    {
        foreach (var n in own.Parts) if (n != null) n.Visible = on && n.Mesh != null;
        foreach (var n in own.Dress) if (n != null) n.Visible = on && n.Mesh != null;
    }

    static void Free(Own own)
    {
        foreach (var n in own.Parts) n?.QueueFree();
        foreach (var n in own.Dress) n?.QueueFree();
    }

    /// <summary>A sum of a part's half-voxels that changes when any of them does (so only a part a wound changed is meshed again).</summary>
    static int Sum(PartGrid g)
    {
        if (g == null) return 0;
        unchecked
        {
            int h = g.Count * 31 + g.M.Length;
            for (int i = 0; i < g.M.Length; i++) h = h * 16777619 ^ g.M[i];
            return h;
        }
    }

    // where each part of a hurt body was drawn this frame, for the arrows in it: by unit and part
    readonly Dictionary<int, Transform3D> look_xf = new();

    /// <summary>Arrows stuck where they went in, most of the shaft out.</summary>
    void DrawStuck(Sim sim)
    {
        var mm = _stuck.Multimesh;
        int n = 0;
        foreach (var u in sim.Units) if (u.Wounds != null) n += u.Wounds.Arrows.Count;
        if (n == 0) { mm.VisibleInstanceCount = 0; return; }
        if (mm.InstanceCount < n) { mm.InstanceCount = Math.Max(16, n * 2); _stuckBuf = new float[mm.InstanceCount * 12]; }
        int i = 0;
        foreach (var u in sim.Units)
        {
            if (u.Wounds == null) continue;
            var fig = Figure.For(u.Wounds.Plan);
            foreach (var (part, x, y, z, dx, dy, dz) in u.Wounds.Arrows)
            {
                if (!look_xf.TryGetValue(u.Id * Figure.Bones + (int)fig.BoneAt((Part)part, y, z), out var px)) continue;
                var d = new Vector3(dx, dy, dz).Normalized();
                var side = Vector3.Up.Cross(d);
                side = side.LengthSquared() < 1e-6f ? Vector3.Right : side.Normalized();
                var up = d.Cross(side);
                // the shaft is drawn in world units: undo the figure's voxel scale
                float k = 1 / VoxelWorld.Vox;
                var t = px * new Transform3D(new Basis(side * k, up * k, d * k), new Vector3(x, y, z) - d * 2.2f);
                int o = i++ * 12;
                _stuckBuf[o] = t.Basis.X.X; _stuckBuf[o + 1] = t.Basis.Y.X; _stuckBuf[o + 2] = t.Basis.Z.X; _stuckBuf[o + 3] = t.Origin.X;
                _stuckBuf[o + 4] = t.Basis.X.Y; _stuckBuf[o + 5] = t.Basis.Y.Y; _stuckBuf[o + 6] = t.Basis.Z.Y; _stuckBuf[o + 7] = t.Origin.Y;
                _stuckBuf[o + 8] = t.Basis.X.Z; _stuckBuf[o + 9] = t.Basis.Y.Z; _stuckBuf[o + 10] = t.Basis.Z.Z; _stuckBuf[o + 11] = t.Origin.Z;
            }
        }
        RenderingServer.MultimeshSetBuffer(mm.GetRid(), _stuckBuf);
        mm.VisibleInstanceCount = i;
    }

    /// <summary>A piece comes off a body: it drops from where it was and lies down on the ground beside it.</summary>
    void Fall(Remnant r)
    {
        // drawn near, with what it held: its cells where its half-voxels came off, the flesh showing where it was cut
        var plan = r.Of?.Wounds?.Plan ?? (r.Of != null ? Bodies.Plan(r.Of.T, r.Of.Look) : null);
        var mesh = plan != null ? Figure.Whole(plan, (int)r.From, r.Grid) : Figure.MeshOf(r.Grid);
        if (mesh == null) return;
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = plan != null ? null : Figure.Material(BodyPlan.Sub), CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        AddChild(node);
        var at = new Vector3(_w.WX(r.X), _w.WY(r.Y), _w.WZ(r.Z));
        var basis = new Basis(Vector3.Up, MathF.PI / 2 - r.Facing).Scaled(Vector3.One * VoxelWorld.Vox * r.Scale);
        var from = new Transform3D(basis, at);
        // where it comes down: tipped over, a little off, its lowest voxels on the ground
        float y0 = r.Grid.Y0 / (float)BodyPlan.Sub, y1 = (r.Grid.Y0 + r.Grid.SY) / (float)BodyPlan.Sub;
        float mid = (y0 + y1) / 2, side = (float)(_rnd.NextDouble() - .5) * 8, turn = (float)(_rnd.NextDouble() * Math.PI * 2);
        var spin = new Basis(Vector3.Up, turn) * new Basis(Vector3.Right, MathF.PI / 2 * (_rnd.NextDouble() < .5 ? 1 : -1));
        int gx = (int)(r.X + MathF.Cos(turn) * side), gz = (int)(r.Z + MathF.Sin(turn) * side);
        float ground = _w.SurfAt(Math.Clamp(gx, 0, _w.SX - 1), Math.Clamp(gz, 0, _w.SZ - 1));
        var to = new Transform3D(spin.Scaled(Vector3.One * VoxelWorld.Vox * r.Scale), new Vector3(_w.WX(gx), _w.WY(ground + 1), _w.WZ(gz)))
            * new Transform3D(Basis.Identity, new Vector3(0, -mid, 0));
        _fallen.Add(new Fallen { Node = node, From = from, To = to, At = _m.Sim.Time });
        node.Transform = from;
    }

    void DrawFallen(float dt)
    {
        double now = _m.Sim.Time;
        for (int i = _fallen.Count - 1; i >= 0; i--)
        {
            var f = _fallen[i];
            float t = (float)(now - f.At);
            if (t > 75) { f.Node.QueueFree(); _fallen.RemoveAt(i); continue; }
            // half a second down, a bounce of a tumble; long after, it sinks into the ground
            float k = Math.Clamp(t / .5f, 0, 1);
            var o = f.From.Origin.Lerp(f.To.Origin, k) + Vector3.Up * MathF.Sin(k * MathF.PI) * .12f;
            float scale = f.From.Basis.X.Length();
            var q = f.From.Basis.Orthonormalized().GetRotationQuaternion().Slerp(f.To.Basis.Orthonormalized().GetRotationQuaternion(), k);
            var b = new Basis(q).Scaled(Vector3.One * scale);
            if (t > 60) o += Vector3.Down * (t - 60) * .02f;
            f.Node.Transform = new Transform3D(b, o);
        }
    }

    /// <summary>How a pawn holds itself while passing the time: sitting, talking with its hands, stretching, warming its hands, shading its eyes.</summary>
    void Leisure(Unit u, Figure f, float seed, ref float main, ref float off, ref float legR, ref float legL, ref float lean, ref float bob)
    {
        float t = (float)_t;
        switch (u.Pastime)
        {
            case Pastime.Sit:
                // on a bench, or on the ground
                bob = u.SeatH > 0 ? u.SeatH + .25f - f.Joint[(int)Part.LegR].Y : -f.Joint[(int)Part.LegR].Y * .8f;
                legR = -1.5f; legL = -1.38f;
                main = -.62f; off = -.5f;
                lean = .12f;
                break;
            case Pastime.Talk:
                float g = .5f + .5f * MathF.Sin(t * 3.1f + seed * 5);
                main = -.3f - .8f * g * g;
                off = -.15f - .25f * (.5f + .5f * MathF.Sin(t * 1.7f + seed));
                lean = .03f;
                break;
            case Pastime.Stretch:
                float p = Math.Clamp(1 - (float)((u.PastimeUntil - _m.Sim.Time) / 2.2), 0, 1);
                float up = p < .35f ? p / .35f : p > .75f ? (1 - p) / .25f : 1;
                up = up * up * (3 - 2 * up);
                main = Mathf.Lerp(main, -3f, up);
                off = Mathf.Lerp(off, -3f, up);
                lean = -.1f * up;
                bob += up * .25f;
                break;
            case Pastime.Warm:
                main = -1.2f + MathF.Sin(t * 1.3f + seed) * .08f;
                off = -1.25f - MathF.Sin(t * 1.3f + seed) * .08f;
                lean = .1f;
                break;
            case Pastime.Watch:
                off = -2.45f;
                lean = .05f;
                break;
            case Pastime.Bound:
                // on its knees, sitting back on its heels, the hands tied behind, the head bowed
                bob = -f.Joint[(int)Part.LegR].Y * .78f;
                legR = 1.32f; legL = 1.38f;
                main = .62f; off = .62f;
                lean = .28f;
                break;
            case Pastime.Pray:
                // the staff planted, the other hand raised to the sky, swaying a little as it chants
                float sway = MathF.Sin(t * 1.1f + seed);
                main = -.12f + sway * .03f;
                off = -2.75f + sway * .12f;
                lean = -.07f + sway * .02f;
                bob += MathF.Sin(t * 2.2f + seed) * .05f;
                break;
        }
    }

    /// <summary>A blow every so many seconds: a slow lift over the shoulder, then a quick swing down.</summary>
    static void Chop(float period, float t, out float arm, out float lean)
    {
        float c = t / period; c -= MathF.Floor(c);
        if (c < .62f)
        {
            float s = c / .62f; s = s * s * (3 - 2 * s);
            arm = Mathf.Lerp(.3f, -2.85f, s);
            lean = Mathf.Lerp(.14f, -.06f, s);
        }
        else
        {
            float s = (c - .62f) / .38f; s *= s;
            arm = Mathf.Lerp(-2.85f, .3f, s);
            lean = Mathf.Lerp(-.06f, .14f, s);
        }
    }

    static Transform3D Swing(Figure f, Part p, float angle)
    {
        if (angle == 0) return Transform3D.Identity;
        var j = f.Joint[(int)p];
        return new Transform3D(Basis.Identity, j) * new Transform3D(new Basis(Vector3.Right, angle), Vector3.Zero) * new Transform3D(Basis.Identity, -j);
    }

    Color Tint(Unit u)
    {
        double hurt = _m.Sim.Time - u.LastHurt;
        if (!u.Dead && hurt < .1) { float k = 1 - (float)(hurt / .1); return new Color(1 + .7f * k, 1 - .3f * k, 1 - .35f * k); }
        if (u.Dead) return new Color(.62f, .58f, .58f);
        return Colors.White;
    }

    static void Write(float[] b, int o, Transform3D t, Color c)
    {
        if (b == null) return;
        int i = o;
        b[i] = t.Basis.X.X; b[i + 1] = t.Basis.Y.X; b[i + 2] = t.Basis.Z.X; b[i + 3] = t.Origin.X;
        b[i + 4] = t.Basis.X.Y; b[i + 5] = t.Basis.Y.Y; b[i + 6] = t.Basis.Z.Y; b[i + 7] = t.Origin.Y;
        b[i + 8] = t.Basis.X.Z; b[i + 9] = t.Basis.Y.Z; b[i + 10] = t.Basis.Z.Z; b[i + 11] = t.Origin.Z;
        b[i + 12] = c.R; b[i + 13] = c.G; b[i + 14] = c.B; b[i + 15] = 1;
    }

    static void Upload(Kind k)
    {
        void One(MultiMeshInstance3D mi, float[] buf)
        {
            if (mi == null || !mi.Visible) return;
            var mm = mi.Multimesh;
            if (k.Count == 0) { if (mm.VisibleInstanceCount != 0) mm.VisibleInstanceCount = 0; return; }
            RenderingServer.MultimeshSetBuffer(mm.GetRid(), buf);
            mm.VisibleInstanceCount = k.Count;
        }
        for (int p = 0; p < Figure.Bones; p++) { One(k.Parts[p], k.Bufs[p]); One(k.Shades[p], k.Bufs[p]); }
        for (int d = 0; d < k.Dress.Length; d++) { One(k.Dress[d], k.DressBufs[d]); One(k.DressShades[d], k.DressBufs[d]); }
    }

    // ---------------------------------------------------------------- arrows and rings

    /// <summary>
    /// What flies at the horde, each on its arc: arrows and bolts point along their flight (and a miss lies where it
    /// came down), a stone and a pot tumble; a stone or a pot that has come down is gone — the pot burst, the stone among
    /// the others on the ground.
    /// </summary>
    void DrawArrows(Sim sim)
    {
        DrawKegs(sim);
        // a shot that missed throws up bits of what it came down in: earth, grass, chips of stone, water
        foreach (var a in sim.Arrows) if (a.Done && !a.Hits && a.Kind is not (Shot.Pot or Shot.FirePot or Shot.Flask or Shot.Keg) && _grounded.Add(a)) Thud(a);
        if (_grounded.Count > 600) _grounded.RemoveWhere(a => !sim.Arrows.Contains(a));
        var meshes = new[] { _arrows, _bolts, _stones, _pots, _firePots, _flasks, _spears, _pitchKegs };
        Span<int> counts = stackalloc int[meshes.Length];
        foreach (var a in sim.Arrows) if (Flying(a)) counts[(int)a.Kind]++;
        for (int k = 0; k < meshes.Length; k++)
        {
            var mm = meshes[k].Multimesh;
            if (counts[k] == 0) { mm.VisibleInstanceCount = 0; continue; }
            if (mm.InstanceCount < counts[k]) { mm.InstanceCount = Math.Max(32, counts[k] * 2); _shotBufs[k] = new float[mm.InstanceCount * 12]; }
        }
        counts.Clear();
        foreach (var a in sim.Arrows)
        {
            if (!Flying(a)) continue;
            int k = (int)a.Kind;
            float s = Math.Clamp(a.T / Math.Max(a.Dur, 1e-3f), 0, 1);
            var p0 = new Vector3(a.X0, a.Y0, a.Z0); var p1 = new Vector3(a.X1, a.Y1, a.Z1);
            float h = p0.DistanceTo(p1) * a.Arc;
            var p = p0.Lerp(p1, s) + Vector3.Up * 4 * h * s * (1 - s);
            var dir = (p1 - p0) + Vector3.Up * 4 * h * (1 - 2 * s);
            if (a.Done) { p = p1 + dir.Normalized() * 3; }
            var at = new Vector3(_w.WX(p.X), _w.WY(p.Y), _w.WZ(p.Z));
            var z = dir.Normalized();
            // a stone or a pot turns over and over as it goes
            if (a.Kind is Shot.Stone or Shot.Pot or Shot.FirePot or Shot.Flask or Shot.Keg) z = new Basis(Vector3.Up, a.T * 9) * (new Basis(Vector3.Right, a.T * 13) * Vector3.Back);
            var x = Vector3.Up.Cross(z);
            x = x.LengthSquared() < 1e-6f ? Vector3.Right : x.Normalized();
            var y = z.Cross(x);
            var buf = _shotBufs[k];
            int o = counts[k]++ * 12;
            buf[o] = x.X; buf[o + 1] = y.X; buf[o + 2] = z.X; buf[o + 3] = at.X;
            buf[o + 4] = x.Y; buf[o + 5] = y.Y; buf[o + 6] = z.Y; buf[o + 7] = at.Y;
            buf[o + 8] = x.Z; buf[o + 9] = y.Z; buf[o + 10] = z.Z; buf[o + 11] = at.Z;
        }
        for (int k = 0; k < meshes.Length; k++)
        {
            if (counts[k] == 0) continue;
            var mm = meshes[k].Multimesh;
            RenderingServer.MultimeshSetBuffer(mm.GetRid(), _shotBufs[k]);
            mm.VisibleInstanceCount = counts[k];
        }
    }

    readonly HashSet<Arrow> _grounded = new();

    void Thud(Arrow a)
    {
        int x = (int)MathF.Floor(a.X1), z = (int)MathF.Floor(a.Z1);
        bool stone = a.Kind == Shot.Stone;
        for (int y = (int)a.Y1 + 1; y >= 0 && y >= (int)a.Y1 - 4; y--)
        {
            byte m = _w.Get(x, y, z);
            if (m == 0) continue;
            _m.Debris.Spawn(m, x, y, z, stone ? 4 : 2, stone ? 1.3f : .8f, m == Mat.Water ? .35f : .45f);
            return;
        }
    }

    /// <summary>Whether a shot is drawn flying (or lying, an arrow or a bolt that missed).</summary>
    static bool Flying(Arrow a) => !a.Done || !a.Hits && a.Kind is Shot.Arrow or Shot.Bolt or Shot.Spear;

    /// <summary>The charges the horde's sappers carry: a keg of powder on each one's back.</summary>
    void DrawKegs(Sim sim)
    {
        var mm = _kegs.Multimesh;
        int n = 0;
        foreach (var u in sim.Units) if (u.Charge && !u.Dead && !u.Inside) n++;
        if (n == 0) { mm.VisibleInstanceCount = 0; return; }
        if (mm.InstanceCount < n) { mm.InstanceCount = Math.Max(8, n * 2); _kegBuf = new float[mm.InstanceCount * 12]; }
        int i = 0;
        foreach (var u in sim.Units)
        {
            if (!u.Charge || u.Dead || u.Inside) continue;
            float fx = MathF.Cos(u.Facing), fz = MathF.Sin(u.Facing);
            var at = new Vector3(_w.WX(u.X - fx * (u.T.BodyR + 1.2f)), _w.WY(u.Y + u.T.BodyH * .55f), _w.WZ(u.Z - fz * (u.T.BodyR + 1.2f)));
            var b = new Basis(Vector3.Up, MathF.PI / 2 - u.Facing);
            int o = i++ * 12;
            _kegBuf[o] = b.X.X; _kegBuf[o + 1] = b.Y.X; _kegBuf[o + 2] = b.Z.X; _kegBuf[o + 3] = at.X;
            _kegBuf[o + 4] = b.X.Y; _kegBuf[o + 5] = b.Y.Y; _kegBuf[o + 6] = b.Z.Y; _kegBuf[o + 7] = at.Y;
            _kegBuf[o + 8] = b.X.Z; _kegBuf[o + 9] = b.Y.Z; _kegBuf[o + 10] = b.Z.Z; _kegBuf[o + 11] = at.Z;
        }
        RenderingServer.MultimeshSetBuffer(mm.GetRid(), _kegBuf);
        mm.VisibleInstanceCount = n;
    }

    static readonly Color Pole = new(.24f, .16f, .1f), Rag = new(.55f, .07f, .06f), RagDark = new(.3f, .04f, .04f),
        WhiteRag = new(.82f, .8f, .74f), WhiteRagDark = new(.55f, .53f, .48f),
        GoldRag = new(.78f, .55f, .12f), GoldRagDark = new(.45f, .3f, .06f),
        LordRag = new(.08f, .07f, .07f), LordRagDark = new(.62f, .09f, .05f);

    /// <summary>The standards of the warbands' leaders: a pole up the back, a red rag stirring from its top.</summary>
    void DrawStandards(Sim sim)
    {
        var mm = _standards.Multimesh;
        int n = 0;
        foreach (var u in sim.Units) if ((u.Leader || u.Envoy) && !u.Dead && !u.Lurking) n += 3;
        if (n == 0) { mm.VisibleInstanceCount = 0; return; }
        if (mm.InstanceCount < n) { mm.InstanceCount = Math.Max(24, n * 2); _standardBuf = new float[mm.InstanceCount * 16]; }
        int i = 0;
        foreach (var u in sim.Units)
        {
            if (!(u.Leader || u.Envoy) || u.Dead || u.Lurking || !_looks.TryGetValue(u.Id, out var look)) continue;
            float h = u.T.BodyH, back = -(u.T.BodyR + .6f), sway = MathF.Sin((float)_t * 2.1f + u.Id) * .25f;
            var b = look.Body;
            Box(ref i, b, new Vector3(0, h * .78f, back), new Vector3(.9f, h * 1.05f, .9f), 0, Pole);
            // the rag: hangs back from the top of the pole and stirs
            var top = new Vector3(0, h * 1.18f, back);
            var cloth = b * new Transform3D(new Basis(Vector3.Up, sway), top);
            // an envoy's rag is white: it comes to talk
            // a chapter's champion and the horde's chief fly gold, the chief's the larger; a warlord black over red
            bool lord = u.Lord is { Champion: false };
            float big = u.Champion == 4 ? 1.5f : u.Champion > 0 ? 1.25f : lord ? 1.4f : 1;
            Box(ref i, cloth, new Vector3(0, -2.4f * big, -2.8f * big), new Vector3(.35f, 5f * big, 5.4f * big), 0, u.Envoy ? WhiteRag : u.Champion > 0 ? GoldRag : lord ? LordRag : Rag);
            Box(ref i, cloth, new Vector3(0, -5.4f * big, -1.6f * big), new Vector3(.35f, 1.2f * big, 3f * big), 0, u.Envoy ? WhiteRagDark : u.Champion > 0 ? GoldRagDark : lord ? LordRagDark : RagDark);
        }
        RenderingServer.MultimeshSetBuffer(mm.GetRid(), _standardBuf);
        mm.VisibleInstanceCount = i;
    }

    void Box(ref int i, Transform3D parent, Vector3 at, Vector3 size, float turn, Color c)
    {
        var t = parent * new Transform3D(new Basis(Vector3.Up, turn).Scaled(size), at);
        int o = i++ * 16;
        var buf = _standardBuf;
        buf[o] = t.Basis.X.X; buf[o + 1] = t.Basis.Y.X; buf[o + 2] = t.Basis.Z.X; buf[o + 3] = t.Origin.X;
        buf[o + 4] = t.Basis.X.Y; buf[o + 5] = t.Basis.Y.Y; buf[o + 6] = t.Basis.Z.Y; buf[o + 7] = t.Origin.Y;
        buf[o + 8] = t.Basis.X.Z; buf[o + 9] = t.Basis.Y.Z; buf[o + 10] = t.Basis.Z.Z; buf[o + 11] = t.Origin.Z;
        buf[o + 12] = c.R; buf[o + 13] = c.G; buf[o + 14] = c.B; buf[o + 15] = 1;
    }

    void DrawRings()
    {
        var mm = _rings.Multimesh;
        var sel = _m.Selected;
        // ours chosen, and the raider or beast looked at, ringed in blood
        var seen = _m.Inspected is { Dead: false } l ? l : null;
        int n = seen != null ? 1 : 0;
        foreach (var u in sel) if (!u.Dead && !u.Inside) n++;
        if (n == 0) { mm.VisibleInstanceCount = 0; return; }
        if (mm.InstanceCount < n) { mm.InstanceCount = Math.Max(16, n * 2); _ringBuf = new float[mm.InstanceCount * 16]; }
        int i = 0;
        float pulse = 1 + MathF.Sin((float)_t * 4) * .04f;
        // ours glow (the light runs over white), the one looked at stays plain blood red
        void Ring(Unit u, Color c, float glow)
        {
            float r = (u.T.BodyR + 2f) * VoxelWorld.Vox * pulse;
            int o = i++ * 16;
            _ringBuf[o] = r; _ringBuf[o + 1] = 0; _ringBuf[o + 2] = 0; _ringBuf[o + 3] = _w.WX(u.X);
            _ringBuf[o + 4] = 0; _ringBuf[o + 5] = r * .5f; _ringBuf[o + 6] = 0; _ringBuf[o + 7] = _w.WY(u.Y + .6f);
            _ringBuf[o + 8] = 0; _ringBuf[o + 9] = 0; _ringBuf[o + 10] = r; _ringBuf[o + 11] = _w.WZ(u.Z);
            _ringBuf[o + 12] = c.R * glow; _ringBuf[o + 13] = c.G * glow; _ringBuf[o + 14] = c.B * glow; _ringBuf[o + 15] = 1;
        }
        foreach (var u in sel) if (!u.Dead && !u.Inside) Ring(u, u.Hold ? Ornament.BronzeLight : Ornament.EmberGlow, 1.6f);
        if (seen != null) Ring(seen, new Color("c8281c"), .9f);
        RenderingServer.MultimeshSetBuffer(mm.GetRid(), _ringBuf);
        mm.VisibleInstanceCount = n;
    }
}
