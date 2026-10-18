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
    sealed class Look { public float Face, Move, LastX, LastZ; public bool Seen; public Transform3D Body; }

    /// <summary>A hurt body drawn on its own: a mesh of what is left of each part, made again after every wound.</summary>
    sealed class Own { public readonly MeshInstance3D[] Parts = new MeshInstance3D[Figure.Parts]; public int Rev = -1; }

    /// <summary>A piece cut off a body, falling and then lying where it fell.</summary>
    sealed class Fallen { public MeshInstance3D Node; public Transform3D From, To; public double At; }

    Main _m;
    VoxelWorld _w;
    ShaderMaterial _mat, _matFar;
    /// <summary>The camera is far: figures are drawn in whole voxels and cast no shadows (a half-voxel is under a pixel).</summary>
    bool _far;
    readonly Dictionary<BodyPlan, Kind> _kinds = new();
    readonly Dictionary<int, Look> _looks = new();
    readonly HashSet<int> _alive = new();
    readonly List<int> _gone = new();
    MultiMeshInstance3D _arrows, _rings, _standards, _stuck, _stones;
    float[] _stoneBuf = Array.Empty<float>();
    float[] _arrowBuf = Array.Empty<float>(), _ringBuf = Array.Empty<float>(), _standardBuf = Array.Empty<float>(), _stuckBuf = Array.Empty<float>();
    readonly Dictionary<int, Own> _own = new();
    readonly List<Fallen> _fallen = new();
    readonly Transform3D[] _xf = new Transform3D[Figure.Parts];
    readonly Random _rnd = new(5);
    double _t;

    static readonly Aabb Everywhere = new(new Vector3(-100, -10, -100), new Vector3(200, 60, 200));

    public void Init(Main m)
    {
        _m = m;
        _w = m.World;
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
        // the figures are sculpted in half-voxels: their grain and bevels follow those
        _mat.SetShaderParameter("grid", (float)BodyPlan.Sub);
        _matFar = (ShaderMaterial)_mat.Duplicate();
        _matFar.SetShaderParameter("grid", 1f);

        // an arrow is 70 cm long, a little under five voxels
        var shaft = new BoxMesh { Size = new Vector3(.022f, .022f, .35f), Material = new StandardMaterial3D { AlbedoColor = new Color("4a3424"), Roughness = .9f } };
        _arrows = Multi(shaft, null, false);
        _stuck = Multi(shaft, null, false);
        // a catapult's stone, half a metre of it
        var stone = new BoxMesh { Size = Vector3.One * .55f, Material = new StandardMaterial3D { AlbedoColor = new Color("7d776f"), Roughness = .95f } };
        _stones = Multi(stone, null, false);
        m.Sim.Severed += Fall;
        var ring = new TorusMesh { InnerRadius = .82f, OuterRadius = 1f, Rings = 32, RingSegments = 4 };
        var ringMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, AlbedoColor = Colors.White };
        _rings = Multi(ring, ringMat, true);
        _rings.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        // a warband's leader carries its standard on its back: a pole and a rag of red cloth
        var cube = new BoxMesh { Size = Vector3.One };
        _standards = Multi(cube, new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled }, true);
    }

    /// <summary>The figures of one kind in one look, added the first time someone of that look turns up.</summary>
    Kind KindOf(Unit u)
    {
        var plan = Bodies.Plan(u.T, u.Look);
        if (_kinds.TryGetValue(plan, out var k)) return k;
        k = new Kind { Fig = Figure.For(plan), Parts = new MultiMeshInstance3D[Figure.Parts] };
        for (int p = 0; p < Figure.Parts; p++)
            if (k.Fig.Mesh[p] != null) k.Parts[p] = Multi(k.Fig.Mesh[p], _mat, true);
        _kinds[plan] = k;
        if (_far) Dress(k);
        return k;
    }

    /// <summary>Near or far, every kind of figure in the meshes and the material for it.</summary>
    void Dress(Kind k)
    {
        for (int p = 0; p < Figure.Parts; p++)
        {
            var mi = k.Parts[p];
            if (mi == null) continue;
            var mesh = _far ? k.Fig.Far[p] : k.Fig.Mesh[p];
            mi.Visible = mesh != null;
            if (mesh == null) continue;
            mi.Multimesh.Mesh = mesh;
            mi.MaterialOverride = _far ? _matFar : _mat;
            mi.CastShadow = _far ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On;
        }
    }

    /// <summary>How many pixels of the window a voxel takes: under three, the figures go over to whole voxels.</summary>
    void Distance()
    {
        float px = GetTree().Root.Size.Y / Math.Max(.01f, _m.Rig.Cam.Size) * VoxelWorld.Vox;
        bool far = _far ? px < 3.6f : px < 3.2f;
        if (far == _far) return;
        _far = far;
        foreach (var k in _kinds.Values) Dress(k);
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
        Distance();
        look_xf.Clear();
        foreach (var k in _kinds.Values) k.Count = 0;
        // those in the citadel are not to be seen
        foreach (var u in sim.Units) if (u.Wounds == null && !u.Inside) KindOf(u).Count++;
        foreach (var k in _kinds.Values) { if (k.Count > k.Cap) Grow(k, k.Count); k.Count = 0; }
        _alive.Clear();
        foreach (var u in sim.Units)
        {
            if (u.Inside) continue;
            var k = KindOf(u);
            _alive.Add(u.Id);
            if (!_looks.TryGetValue(u.Id, out var look)) _looks[u.Id] = look = new Look();
            Track(u, look, dt);
            var tint = Pose(u, k.Fig, look);
            if (u.Wounds == null)
            {
                int o = k.Count++ * 16;
                for (int p = 0; p < Figure.Parts; p++) Write(k, (Part)p, o, _xf[p], tint);
            }
            else DrawOwn(u);
        }
        foreach (var k in _kinds.Values) Upload(k);
        _gone.Clear();
        foreach (int id in _looks.Keys) if (!_alive.Contains(id)) _gone.Add(id);
        foreach (int id in _gone)
        {
            _looks.Remove(id);
            if (_own.Remove(id, out var own)) foreach (var n in own.Parts) n?.QueueFree();
        }
        DrawArrows(sim);
        DrawStuck(sim);
        DrawFallen(dt);
        DrawRings();
        DrawStandards(sim);
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
    /// <summary>Works out the joints for this frame: the part transforms go to _xf, the tint is returned.</summary>
    Color Pose(Unit u, Figure f, Look look)
    {
        float legR = 0, legL = 0, main = f.Carry, off = 0, lean = 0, bob = 0, fall = 0, sink = 0;
        float seed = u.Id * .618f;
        if (Kinds.IsEngine(u.T))
        {
            // an engine does not walk: the ram's log swings as it batters, the catapult's arm flies up as it throws
            if (u.T == Kinds.Ram && u.State is UnitState.Carve or UnitState.Siege) main = MathF.Sin((float)_t * MathF.Tau / f.Chop + seed) * .28f;
            // thrown, the arm stands up against the beam a while, then is winched back down for the next
            else if (u.T == Kinds.Catapult) main = u.Dead ? 0 : 1.9f * Math.Clamp(u.Cooldown / u.T.AttackTime * 1.5f, 0, 1);
            if (u.Dead) sink = Math.Max(0, (float)(_m.Sim.Time - u.DiedAt) - 18) * .8f;
        }
        else if (u.Dead)
        {
            float since = (float)(_m.Sim.Time - u.DiedAt);
            fall = Math.Clamp(since / .45f, 0, 1);
            fall = 1 - (1 - fall) * (1 - fall);
            sink = Math.Max(0, since - 18) * 1.4f;
            main = f.Beast ? .5f : -.4f; off = f.Beast ? .4f : f.Shield ? -.2f : .3f;
            if (f.Beast) { legR = -.4f; legL = -.3f; }
        }
        else if (u.Airborne != null)
        {
            // in the air: arms and legs flung about
            float flail = MathF.Sin((float)_t * 11 + seed);
            main = f.Beast ? .6f * flail : -2.4f + flail * .5f; off = f.Beast ? -.6f * flail : -2.2f - flail * .5f;
            legR = .5f + flail * .4f; legL = -.5f - flail * .4f;
        }
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
        else if (_m.Sim.Climbing(u))
        {
            // hand over hand up the ladder
            float phase = u.Y / 3f * MathF.Tau;
            main = -2.6f + MathF.Sin(phase) * .35f;
            off = -2.6f - MathF.Sin(phase) * .35f;
            legR = -.5f + MathF.Sin(phase) * .45f;
            legL = -.5f - MathF.Sin(phase) * .45f;
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
                case UnitState.Work:
                case UnitState.Carve:
                case UnitState.Siege:
                    Chop(f.Chop / u.Pace, (float)_t + seed * f.Chop, out main, out lean);
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
                        bob = breath * .12f;
                        main += breath * .04f;
                        off = -breath * .04f;
                        if (u.State == UnitState.Idle) Leisure(u, f, seed, ref main, ref off, ref legR, ref legL, ref lean, ref bob);
                    }
                    break;
            }
        }

        var at = new Vector3(_w.WX(u.X), _w.WY(u.Y + bob - sink), _w.WZ(u.Z));
        // a leader stands a little taller than its kind
        var root = new Transform3D(new Basis(Vector3.Up, MathF.PI / 2 - look.Face).Scaled(Vector3.One * VoxelWorld.Vox * (u.Leader ? 1.15f : 1)), at);
        // thrown through the air, it tumbles about its middle as it flies
        if (u.Airborne is { } fl && new Vector3(fl.AX, 0, fl.AZ) is var axis && axis.LengthSquared() > 1e-4f)
        {
            var mid = at + Vector3.Up * u.T.BodyH * .5f * VoxelWorld.Vox;
            var spin = new Basis(axis.Normalized(), fl.Turn);
            root = new Transform3D(spin, mid - spin * mid) * root;
        }
        // the fallen lie down: on the back, or a beast on its side
        if (fall > 0)
            root *= f.Beast
                ? new Transform3D(new Basis(Vector3.Back, MathF.PI / 2 * fall), new Vector3(0, fall * (u.T.BodyR * .7f), 0))
                : new Transform3D(new Basis(Vector3.Right, -MathF.PI / 2 * fall), new Vector3(0, fall * (u.T.BodyR + .5f), 0));
        var body = root * Swing(f, Part.Body, lean);
        look.Body = body;
        var tint = Tint(u);
        var arms = f.Beast ? root : body;
        _xf[(int)Part.Body] = body;
        _xf[(int)Part.Main] = arms * Swing(f, Part.Main, main);
        _xf[(int)Part.Off] = arms * Swing(f, Part.Off, off);
        _xf[(int)Part.LegR] = root * Swing(f, Part.LegR, legR);
        _xf[(int)Part.LegL] = root * Swing(f, Part.LegL, legL);
        return tint;
    }

    /// <summary>A hurt body: what is left of each part, meshed again whenever it is wounded, posed like any other.</summary>
    void DrawOwn(Unit u)
    {
        var w = u.Wounds;
        if (!_own.TryGetValue(u.Id, out var own)) _own[u.Id] = own = new Own();
        if (own.Rev != w.Rev)
        {
            own.Rev = w.Rev;
            for (int p = 0; p < Figure.Parts; p++)
            {
                var mesh = Figure.MeshOf(w.Grid[p]);
                if (own.Parts[p] == null && mesh != null)
                {
                    own.Parts[p] = new MeshInstance3D { MaterialOverride = _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
                    AddChild(own.Parts[p]);
                }
                if (own.Parts[p] != null) { own.Parts[p].Mesh = mesh; own.Parts[p].Visible = mesh != null; }
            }
        }
        for (int p = 0; p < Figure.Parts; p++) if (own.Parts[p] != null) own.Parts[p].Transform = _xf[p];
        // for the arrows stuck in it
        for (int p = 0; p < Figure.Parts; p++) look_xf[u.Id * Figure.Parts + p] = _xf[p];
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
            foreach (var (part, x, y, z, dx, dy, dz) in u.Wounds.Arrows)
            {
                if (!look_xf.TryGetValue(u.Id * Figure.Parts + (int)part, out var px)) continue;
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
        var mesh = Figure.MeshOf(r.Grid);
        if (mesh == null) return;
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
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
                bob = -f.Joint[(int)Part.LegR].Y * .8f;
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
        DrawStones(sim);
        foreach (var a in sim.Arrows) if (!(a.Done && a.Hits) && !a.Stone) n++;
        if (n == 0) { mm.VisibleInstanceCount = 0; return; }
        if (mm.InstanceCount < n) { mm.InstanceCount = Math.Max(32, n * 2); _arrowBuf = new float[mm.InstanceCount * 12]; }
        int i = 0;
        foreach (var a in sim.Arrows)
        {
            if (a.Done && a.Hits || a.Stone) continue;
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

    /// <summary>Stones in flight from the catapults: high over, turning as they go.</summary>
    void DrawStones(Sim sim)
    {
        var mm = _stones.Multimesh;
        int n = 0;
        foreach (var a in sim.Arrows) if (a.Stone && !a.Done) n++;
        if (n == 0) { mm.VisibleInstanceCount = 0; return; }
        if (mm.InstanceCount < n) { mm.InstanceCount = Math.Max(8, n * 2); _stoneBuf = new float[mm.InstanceCount * 12]; }
        int i = 0;
        foreach (var a in sim.Arrows)
        {
            if (!a.Stone || a.Done) continue;
            float s = Math.Clamp(a.T / Math.Max(a.Dur, 1e-3f), 0, 1);
            var p0 = new Vector3(a.X0, a.Y0, a.Z0); var p1 = new Vector3(a.X1, a.Y1, a.Z1);
            var p = p0.Lerp(p1, s) + Vector3.Up * 4 * (p0.DistanceTo(p1) * .35f) * s * (1 - s);
            var at = new Vector3(_w.WX(p.X), _w.WY(p.Y), _w.WZ(p.Z));
            var b = new Basis(new Vector3(1, .6f, .3f).Normalized(), a.T * 5);
            int o = i++ * 12;
            _stoneBuf[o] = b.X.X; _stoneBuf[o + 1] = b.Y.X; _stoneBuf[o + 2] = b.Z.X; _stoneBuf[o + 3] = at.X;
            _stoneBuf[o + 4] = b.X.Y; _stoneBuf[o + 5] = b.Y.Y; _stoneBuf[o + 6] = b.Z.Y; _stoneBuf[o + 7] = at.Y;
            _stoneBuf[o + 8] = b.X.Z; _stoneBuf[o + 9] = b.Y.Z; _stoneBuf[o + 10] = b.Z.Z; _stoneBuf[o + 11] = at.Z;
        }
        RenderingServer.MultimeshSetBuffer(mm.GetRid(), _stoneBuf);
        mm.VisibleInstanceCount = n;
    }

    static readonly Color Pole = new(.24f, .16f, .1f), Rag = new(.55f, .07f, .06f), RagDark = new(.3f, .04f, .04f),
        WhiteRag = new(.82f, .8f, .74f), WhiteRagDark = new(.55f, .53f, .48f);

    /// <summary>The standards of the warbands' leaders: a pole up the back, a red rag stirring from its top.</summary>
    void DrawStandards(Sim sim)
    {
        var mm = _standards.Multimesh;
        int n = 0;
        foreach (var u in sim.Units) if ((u.Leader || u.Envoy) && !u.Dead) n += 3;
        if (n == 0) { mm.VisibleInstanceCount = 0; return; }
        if (mm.InstanceCount < n) { mm.InstanceCount = Math.Max(24, n * 2); _standardBuf = new float[mm.InstanceCount * 16]; }
        int i = 0;
        foreach (var u in sim.Units)
        {
            if (!(u.Leader || u.Envoy) || u.Dead || !_looks.TryGetValue(u.Id, out var look)) continue;
            float h = u.T.BodyH, back = -(u.T.BodyR + .6f), sway = MathF.Sin((float)_t * 2.1f + u.Id) * .25f;
            var b = look.Body;
            Box(ref i, b, new Vector3(0, h * .78f, back), new Vector3(.9f, h * 1.05f, .9f), 0, Pole);
            // the rag: hangs back from the top of the pole and stirs
            var top = new Vector3(0, h * 1.18f, back);
            var cloth = b * new Transform3D(new Basis(Vector3.Up, sway), top);
            // an envoy's rag is white: it comes to talk
            Box(ref i, cloth, new Vector3(0, -2.4f, -2.8f), new Vector3(.35f, 5f, 5.4f), 0, u.Envoy ? WhiteRag : Rag);
            Box(ref i, cloth, new Vector3(0, -5.4f, -1.6f), new Vector3(.35f, 1.2f, 3f), 0, u.Envoy ? WhiteRagDark : RagDark);
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
