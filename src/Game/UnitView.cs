using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Draws everyone on the field from the simulation: every part of every kind of figure is one MultiMesh, so a hundred
/// pawns cost a few dozen draw calls. Legs swing with the stride, arms with the blows; the fallen lie down and sink.
/// Also draws arrows in flight and rings under the selected.
/// </summary>
public partial class UnitView : Node3D
{
    sealed class Kind
    {
        public Figure Fig;
        public MultiMeshInstance3D[] Parts;
        /// <summary>One buffer per part: a transform and a colour for every figure of this kind.</summary>
        public readonly float[][] Bufs = new float[Figure.Parts][];
        public int Count, Cap;
    }

    /// <summary>What the view remembers of a unit between frames: smoothed facing and how fast it is moving.</summary>
    sealed class Look { public float Face, Move, LastX, LastZ; public bool Seen; }

    Main _m;
    VoxelWorld _w;
    ShaderMaterial _mat;
    readonly Dictionary<UnitType, Kind> _kinds = new();
    readonly Dictionary<int, Look> _looks = new();
    readonly HashSet<int> _alive = new();
    readonly List<int> _gone = new();
    MultiMeshInstance3D _arrows, _rings;
    float[] _arrowBuf = Array.Empty<float>(), _ringBuf = Array.Empty<float>();
    double _t;

    static readonly Aabb Everywhere = new(new Vector3(-100, -10, -100), new Vector3(200, 60, 200));

    public void Init(Main m)
    {
        _m = m;
        _w = m.World;
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
        foreach (var t in Kinds.PlayerKinds) Add(t);
        foreach (var t in Kinds.HordeKinds) Add(t);

        var shaft = new BoxMesh { Size = new Vector3(.035f, .035f, .7f), Material = new StandardMaterial3D { AlbedoColor = new Color("4a3424"), Roughness = .9f } };
        _arrows = Multi(shaft, null, false);
        var ring = new TorusMesh { InnerRadius = .82f, OuterRadius = 1f, Rings = 32, RingSegments = 4 };
        var ringMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, AlbedoColor = Colors.White };
        _rings = Multi(ring, ringMat, true);
        _rings.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    void Add(UnitType t)
    {
        var k = new Kind { Fig = Figure.For(t), Parts = new MultiMeshInstance3D[Figure.Parts] };
        for (int p = 0; p < Figure.Parts; p++)
            if (k.Fig.Mesh[p] != null) k.Parts[p] = Multi(k.Fig.Mesh[p], _mat, true);
        _kinds[t] = k;
    }

    MultiMeshInstance3D Multi(Mesh mesh, Material mat, bool colors)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colors, Mesh = mesh };
        var mi = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.On, CustomAabb = Everywhere };
        AddChild(mi);
        return mi;
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
        if (_m?.Sim == null) return;
        float dt = (float)Math.Min(delta, .1);
        _t += dt;
        var sim = _m.Sim;
        foreach (var k in _kinds.Values) k.Count = 0;
        foreach (var u in sim.Units) if (_kinds.TryGetValue(u.T, out var k)) k.Count++;
        foreach (var k in _kinds.Values) { if (k.Count > k.Cap) Grow(k, k.Count); k.Count = 0; }
        _alive.Clear();
        foreach (var u in sim.Units)
        {
            if (!_kinds.TryGetValue(u.T, out var k)) continue;
            _alive.Add(u.Id);
            if (!_looks.TryGetValue(u.Id, out var look)) _looks[u.Id] = look = new Look();
            Track(u, look, dt);
            Pose(u, k, look, k.Count++);
        }
        foreach (var k in _kinds.Values) Upload(k);
        _gone.Clear();
        foreach (int id in _looks.Keys) if (!_alive.Contains(id)) _gone.Add(id);
        foreach (int id in _gone) _looks.Remove(id);
        DrawArrows(sim);
        DrawRings();
    }

    void Track(Unit u, Look l, float dt)
    {
        if (!l.Seen) { l.Seen = true; l.Face = u.Facing; l.LastX = u.X; l.LastZ = u.Z; }
        float dx = u.X - l.LastX, dz = u.Z - l.LastZ;
        float speed = MathF.Sqrt(dx * dx + dz * dz) / Math.Max(dt, 1e-4f);
        float target = Math.Clamp(speed / (u.T.Move.Speed * NavGrid.CS * .6f), 0, 1);
        if (_m.Paused || u.Dead) target = l.Move * .9f;
        l.Move += (target - l.Move) * Math.Min(1, dt * 10);
        l.LastX = u.X; l.LastZ = u.Z;
        float d = Mathf.Wrap(u.Facing - l.Face, -MathF.PI, MathF.PI);
        l.Face += d * Math.Min(1, dt * 12);
    }

    static void Grow(Kind k, int need)
    {
        while (k.Cap < need) k.Cap = Math.Max(8, k.Cap * 2);
        for (int p = 0; p < Figure.Parts; p++)
        {
            var mi = k.Parts[p];
            if (mi == null) continue;
            k.Bufs[p] = new float[k.Cap * 16];
            mi.Multimesh.InstanceCount = k.Cap;
            mi.Multimesh.VisibleInstanceCount = 0;
        }
    }

    /// <summary>Works out the joints for this frame and writes the part transforms of one figure.</summary>
    void Pose(Unit u, Kind k, Look look, int slot)
    {
        var f = k.Fig;
        float legR = 0, legL = 0, main = f.Carry, off = 0, lean = 0, bob = 0, fall = 0, sink = 0;
        float seed = u.Id * .618f;
        if (u.Dead)
        {
            float since = (float)(_m.Sim.Time - u.DiedAt);
            fall = Math.Clamp(since / .45f, 0, 1);
            fall = 1 - (1 - fall) * (1 - fall);
            sink = Math.Max(0, since - 18) * 1.4f;
            main = -.4f; off = .3f;
        }
        else
        {
            float phase = u.Stride / f.Stride * MathF.Tau;
            float a = look.Move;
            legR = -MathF.Sin(phase) * .6f * a;
            legL = -legR;
            main += -legR * .7f;
            off = -legL * .7f;
            bob = (MathF.Abs(MathF.Cos(phase)) - .64f) * a * 1.1f;
            switch (u.State)
            {
                case UnitState.Work:
                case UnitState.Carve:
                case UnitState.Siege:
                    Chop(f, (float)_t + seed * f.Chop, out main, out lean);
                    off = -.35f;
                    break;
                case UnitState.Fight when u.T.Ranged:
                    off = -1.5f;
                    main = -1.42f + u.Swing * .3f;
                    break;
                case UnitState.Fight:
                    float raise = u.Cooldown < .45f ? Math.Clamp(1 - u.Cooldown / .45f, 0, 1) : 0;
                    main = Mathf.Lerp(-.65f, -2.7f, raise);
                    main = Mathf.Lerp(main, .15f, u.Swing);
                    lean = u.Swing * .14f - raise * .05f;
                    off = f.Shield ? -1.05f : -.45f;
                    break;
                default:
                    if (a < .05f)
                    {
                        float breath = MathF.Sin((float)_t * 1.7f + seed);
                        bob = breath * .25f;
                        main += breath * .04f;
                        off = -breath * .04f;
                    }
                    break;
            }
        }

        var at = new Vector3(_w.WX(u.X), _w.WY(u.Y + bob - sink), _w.WZ(u.Z));
        var root = new Transform3D(new Basis(Vector3.Up, MathF.PI / 2 - look.Face).Scaled(Vector3.One * VoxelWorld.Vox), at);
        if (fall > 0)
            root *= new Transform3D(new Basis(Vector3.Right, -MathF.PI / 2 * fall), new Vector3(0, fall * (u.T.BodyR + .5f), 0));
        var body = root * Swing(f, Part.Body, lean);
        var tint = Tint(u);
        int o = slot * 16;
        Write(k, Part.Body, o, body, tint);
        Write(k, Part.Main, o, body * Swing(f, Part.Main, main), tint);
        Write(k, Part.Off, o, body * Swing(f, Part.Off, off), tint);
        Write(k, Part.LegR, o, root * Swing(f, Part.LegR, legR), tint);
        Write(k, Part.LegL, o, root * Swing(f, Part.LegL, legL), tint);
    }

    /// <summary>A blow every f.Chop seconds: a slow lift over the shoulder, then a quick swing down.</summary>
    static void Chop(Figure f, float t, out float arm, out float lean)
    {
        float c = t / f.Chop; c -= MathF.Floor(c);
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

    static void Write(Kind k, Part p, int o, Transform3D t, Color c)
    {
        var b = k.Bufs[(int)p];
        if (b == null) return;
        int i = o;
        b[i] = t.Basis.X.X; b[i + 1] = t.Basis.Y.X; b[i + 2] = t.Basis.Z.X; b[i + 3] = t.Origin.X;
        b[i + 4] = t.Basis.X.Y; b[i + 5] = t.Basis.Y.Y; b[i + 6] = t.Basis.Z.Y; b[i + 7] = t.Origin.Y;
        b[i + 8] = t.Basis.X.Z; b[i + 9] = t.Basis.Y.Z; b[i + 10] = t.Basis.Z.Z; b[i + 11] = t.Origin.Z;
        b[i + 12] = c.R; b[i + 13] = c.G; b[i + 14] = c.B; b[i + 15] = 1;
    }

    static void Upload(Kind k)
    {
        for (int p = 0; p < Figure.Parts; p++)
        {
            var mi = k.Parts[p];
            if (mi == null) continue;
            var mm = mi.Multimesh;
            if (k.Count == 0) { if (mm.VisibleInstanceCount != 0) mm.VisibleInstanceCount = 0; continue; }
            RenderingServer.MultimeshSetBuffer(mm.GetRid(), k.Bufs[p]);
            mm.VisibleInstanceCount = k.Count;
        }
    }

    // ---------------------------------------------------------------- arrows and rings

    void DrawArrows(Sim sim)
    {
        var mm = _arrows.Multimesh;
        int n = 0;
        foreach (var a in sim.Arrows) if (!(a.Done && a.Hits)) n++;
        if (n == 0) { mm.VisibleInstanceCount = 0; return; }
        if (mm.InstanceCount < n) { mm.InstanceCount = Math.Max(32, n * 2); _arrowBuf = new float[mm.InstanceCount * 12]; }
        int i = 0;
        foreach (var a in sim.Arrows)
        {
            if (a.Done && a.Hits) continue;
            float s = Math.Clamp(a.T / Math.Max(a.Dur, 1e-3f), 0, 1);
            var p0 = new Vector3(a.X0, a.Y0, a.Z0); var p1 = new Vector3(a.X1, a.Y1, a.Z1);
            float h = p0.DistanceTo(p1) * .1f;
            var p = p0.Lerp(p1, s) + Vector3.Up * 4 * h * s * (1 - s);
            var dir = (p1 - p0) + Vector3.Up * 4 * h * (1 - 2 * s);
            if (a.Done) { p = p1 + dir.Normalized() * 3; }
            var at = new Vector3(_w.WX(p.X), _w.WY(p.Y), _w.WZ(p.Z));
            var z = dir.Normalized();
            var x = Vector3.Up.Cross(z);
            x = x.LengthSquared() < 1e-6f ? Vector3.Right : x.Normalized();
            var y = z.Cross(x);
            int o = i++ * 12;
            _arrowBuf[o] = x.X; _arrowBuf[o + 1] = y.X; _arrowBuf[o + 2] = z.X; _arrowBuf[o + 3] = at.X;
            _arrowBuf[o + 4] = x.Y; _arrowBuf[o + 5] = y.Y; _arrowBuf[o + 6] = z.Y; _arrowBuf[o + 7] = at.Y;
            _arrowBuf[o + 8] = x.Z; _arrowBuf[o + 9] = y.Z; _arrowBuf[o + 10] = z.Z; _arrowBuf[o + 11] = at.Z;
        }
        RenderingServer.MultimeshSetBuffer(mm.GetRid(), _arrowBuf);
        mm.VisibleInstanceCount = n;
    }

    void DrawRings()
    {
        var mm = _rings.Multimesh;
        var sel = _m.Selected;
        int n = 0;
        foreach (var u in sel) if (!u.Dead) n++;
        if (n == 0) { mm.VisibleInstanceCount = 0; return; }
        if (mm.InstanceCount < n) { mm.InstanceCount = Math.Max(16, n * 2); _ringBuf = new float[mm.InstanceCount * 16]; }
        int i = 0;
        float pulse = 1 + MathF.Sin((float)_t * 4) * .04f;
        foreach (var u in sel)
        {
            if (u.Dead) continue;
            float r = (u.T.BodyR + 3.5f) * VoxelWorld.Vox * pulse;
            int o = i++ * 16;
            _ringBuf[o] = r; _ringBuf[o + 1] = 0; _ringBuf[o + 2] = 0; _ringBuf[o + 3] = _w.WX(u.X);
            _ringBuf[o + 4] = 0; _ringBuf[o + 5] = r * .5f; _ringBuf[o + 6] = 0; _ringBuf[o + 7] = _w.WY(u.Y + .6f);
            _ringBuf[o + 8] = 0; _ringBuf[o + 9] = 0; _ringBuf[o + 10] = r; _ringBuf[o + 11] = _w.WZ(u.Z);
            var c = u.Hold ? Ornament.BronzeLight : Ornament.EmberGlow;
            _ringBuf[o + 12] = c.R * 1.6f; _ringBuf[o + 13] = c.G * 1.6f; _ringBuf[o + 14] = c.B * 1.6f; _ringBuf[o + 15] = 1;
        }
        RenderingServer.MultimeshSetBuffer(mm.GetRid(), _ringBuf);
        mm.VisibleInstanceCount = n;
    }
}
