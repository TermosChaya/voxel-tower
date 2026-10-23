using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// What one gone into holds before its eyes, at its true size, as a man sees his own hands: a sword low on the right in
/// the fist, its blade slanting up and across toward the upper left, the forearm going off under the right edge of the
/// sight; the upper right of the shield at the lower left, or the empty left hand there; a polearm, a maul or a pick in
/// both hands; a bow held out on the left with the string drawn back to the jaw. The hands breathe and sway, lag behind
/// a turn of the head and bob in step with the feet. A blow is drawn as the simulation struck it, the stroke it chose:
/// cocked, brought fast across the sight, carried on through and back to the guard; a bowman lets the string go, takes
/// another arrow, nocks it and draws again as he is ready to shoot; a digger swings his pick in both hands as fast as he
/// works. All of it is drawn as if almost at the eye (the voxel shader with VIEW_MODEL), so it never sinks into a wall.
/// Positions here are in metres before the eye: x right, y up, −z ahead.
/// </summary>
public partial class ViewModel : Node3D
{
    /// <summary>World units to a metre.</summary>
    const float M = VoxelWorld.PerMetre * VoxelWorld.Vox;

    /// <summary>A hold: where the fist is, and how what it holds is turned (its axis from grip to point along x, its edge along y).</summary>
    struct Pose
    {
        public Vector3 P;
        public Quaternion Q;
        public Vector3 D => new Basis(Q).X;
    }

    Figure _fig;
    BodyPlan _plan;
    Grip _g;
    int _wi = -1, _si = -1;
    bool _bow, _two, _spear, _aimed;
    MeshInstance3D _armR, _armL, _held, _shield, _strA, _strB, _shaft, _head;
    BodyState _woundsSeen;
    int _rev = -1;
    bool _dirty = true;
    // the hold as it is drawn now, the stroke being drawn and where it began
    Pose _now;
    double _strokeT = -99, _simT;
    StrokeKind _k;
    Pose _from;
    bool _glanced, _stroking, _snap = true;
    // the lean toward the next stroke, the swing of the eyes that chooses it; the pick's round; walking
    float _ready, _swR, _swD, _work, _pick;
    // the head turned faster than the hands follow, and the breath
    float _lagY, _lagP;
    // the bow: the cooldown at the last shot and now, how far the string is drawn, how high the bow is held, the hand when the shot went
    float _cool0, _cool, _draw, _up = 1;
    Vector3 _loosed, _hand;

    public void Init()
    {
        MeshInstance3D Mi()
        {
            var mi = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            AddChild(mi);
            return mi;
        }
        _armR = Mi(); _armL = Mi(); _held = Mi(); _shield = Mi();
        var box = new BoxMesh { Size = Vector3.One };
        MeshInstance3D Rod(Color c) { var mi = Mi(); mi.Mesh = box; mi.MaterialOverride = Plain(c); return mi; }
        _strA = Rod(new Color(.82f, .78f, .64f)); _strB = Rod(new Color(.82f, .78f, .64f));
        _shaft = Rod(new Color(.58f, .42f, .26f)); _head = Rod(new Color(.62f, .64f, .66f));
    }

    /// <summary>Starts afresh on one just gone into: nothing it struck before is drawn again, and the hands are where they rest.</summary>
    public void Reset(Unit u, Sim sim)
    {
        _strokeT = u.StrokeTime; _simT = sim.Time;
        _snap = true; _stroking = false; _ready = _work = _pick = _lagY = _lagP = _swR = _swD = 0;
        _cool = u.Cooldown; _cool0 = 0; _draw = 0; _up = 1;
        _dirty = true;
    }

    /// <summary>
    /// The hands this frame: <paramref name="turnYaw"/> and <paramref name="turnPitch"/> are how far the head turned since
    /// the last (right and up), <paramref name="walk"/> how hard it walks (1 a walk, more a run) and <paramref name="steps"/>
    /// the steps it has taken; <paramref name="foe"/> whether one of the horde is before it in reach.
    /// </summary>
    public void Show(Unit u, Figure fig, Sim sim, float dt, float turnYaw, float turnPitch, float walk, float steps, bool foe)
    {
        // a beast's "arms" are its forelegs, and a rider's are drawn on the horse's frame: neither is held before the eyes
        Visible = !fig.Beast && !fig.Mounted;
        if (!Visible) return;
        if (fig != _fig) Take(fig, u);
        if (_dirty || !ReferenceEquals(u.Wounds, _woundsSeen) || u.Wounds != null && u.Wounds.Rev != _rev) Build(u);
        float sdt = (float)Math.Max(0, sim.Time - _simT);
        _simT = sim.Time;
        bool snap = _snap || dt <= 0;
        _snap = false;

        // the rig: the breath, an idle sway, the hands left behind a turn of the head, the bob of the steps
        float t = Time.GetTicksMsec() / 1000f;
        _lagY = Math.Clamp(_lagY + turnYaw, -.1f, .1f) * MathF.Exp(-dt * 12);
        _lagP = Math.Clamp(_lagP + turnPitch, -.1f, .1f) * MathF.Exp(-dt * 12);
        if (snap) _lagY = _lagP = 0;
        float breath = MathF.Sin(MathF.Tau * t / 4), w = Math.Min(walk, 1), run = Math.Max(0, walk - 1) / .6f;
        var sway = new Vector3(.004f * MathF.Sin(.7f * t), .006f * breath + .003f * MathF.Sin(1.3f * t + 1) + .012f * w * MathF.Sin(MathF.Tau * steps + .6f) - .06f * run, 0);
        float roll = Mathf.DegToRad(.8f) * MathF.Sin(.5f * t + .3f) + Mathf.DegToRad(1.2f) * w * MathF.Sin(MathF.PI * steps);
        var rig = new Basis(Vector3.Up, _lagY) * new Basis(Vector3.Right, -_lagP + Mathf.DegToRad(.6f) * breath) * new Basis(Vector3.Back, roll);
        Transform = new Transform3D(rig, sway * M);

        // the bow comes up and is drawn for one of the horde before it, and for a shot asked at anything else
        if (_bow) { Bow(u, sdt, dt, foe || sim.ShotAsked || sim.WishStrike, snap); return; }

        // the eyes' swing of the last fifth of a second: the stroke the next blow is leant toward
        float fade = MathF.Exp(-dt / .2f);
        _swR = _swR * fade + turnYaw; _swD = _swD * fade - turnPitch;
        var next = _spear ? StrokeKind.Thrust : Sim.StrokeOfSwing(_swR, _swD, sim.LookPitch);
        if (next == StrokeKind.Bash && _si < 0 || next == StrokeKind.Bite) next = StrokeKind.DiagR;
        var guard = Guard();
        if (snap) _now = guard;
        if (!_aimed && u.StrokeTime > _strokeT) { _strokeT = u.StrokeTime; _k = u.Stroke; _from = _now; _glanced = u.Glanced; _stroking = true; }
        // the blow lands as the blade comes to the body, and only then is it known whether it was turned
        else if (_stroking && u.StrokeTime == _strokeT) _glanced = u.Glanced;
        float since = (float)(sim.Time - _strokeT);
        _ready += ((foe && u.Cooldown <= 0 && !_aimed ? .3f : 0) - _ready) * (1 - MathF.Exp(-dt * 6));
        _work += ((u.State == UnitState.Work && u.T.Work > 0 ? 1 : 0) - _work) * (1 - MathF.Exp(-dt * 8));
        if (u.State == UnitState.Work) _pick = (_pick + sdt / Math.Max(.3f, fig.Chop)) % 1;
        var rest = Mix(guard, Keys(next)[0], _ready);
        if (_work > .01f) rest = Mix(rest, Pick(_pick), _work);
        if (_aimed)
        {
            // the kick of a crossbow or a sling loosed, settling as it is wound or loaded again
            float kick = u.Swing * u.Swing;
            rest.P += new Vector3(0, .02f, .07f) * kick;
            rest.Q = new Quaternion(Vector3.Right, .25f * kick) * rest.Q;
        }
        if (_stroking && since < Total) _now = Stroke(since, rest);
        else _now = snap ? rest : Mix(_now, rest, 1 - MathF.Exp(-dt * 14));

        // the fist on the grip, the forearm going off under the right edge
        var D = _now.D;
        Held(_held, _now);
        Arm(_armR, false, Fist(), _now.P, D, new Vector3(.40f, -.42f, 0));
        if (_si >= 0) Shield(since);
        else if (_two || _aimed)
            // the other hand further up the haft, or under the stock
            Arm(_armL, true, V(_g.OffFist), _now.P + D * (_aimed ? .2f : .28f) + (_aimed ? new Vector3(0, -.04f, 0) : Vector3.Zero), D, new Vector3(-.40f, -.42f, 0));
        else Arm(_armL, true, V(_g.OffFist), new Vector3(-.25f, -.31f, -.36f), new Vector3(.25f, .55f, -.8f), new Vector3(-.40f, -.50f, 0));
        _strA.Visible = _strB.Visible = _shaft.Visible = _head.Visible = false;
    }

    // ---------------------------------------------------------------- the figure's pieces

    void Take(Figure fig, Unit u)
    {
        _fig = fig; _plan = fig.Plan;
        _g = Strokes.GripOf(_plan, u.T);
        var wpn = _plan.Weapon;
        var sh = _plan.ShieldDress;
        _wi = wpn != null ? _plan.Dress.IndexOf(wpn) : -1;
        _si = sh != null ? _plan.Dress.IndexOf(sh) : -1;
        _bow = u.T.Tool == "bow" && wpn != null && wpn.On == BodyPart.Off;
        _two = _g.TwoHands;
        _spear = u.T.Tool is "spear" or "lance";
        _aimed = u.T.Ranged && !_bow;
        _dirty = true;
    }

    /// <summary>The arms in their finest cells, what they hold in its own: as much as is left of them after their wounds.</summary>
    void Build(Unit u)
    {
        _dirty = false;
        _woundsSeen = u.Wounds; _rev = u.Wounds?.Rev ?? -1;
        var plan = u.Wounds?.Plan ?? _plan;
        PartGrid Now(BodyPart p) => u.Wounds != null ? u.Wounds.Grid[(int)p] : plan.Grid[(int)p];
        _armR.Mesh = Made(Figure.NearOf(plan, (int)Part.Main, Now(BodyPart.Main), out _), BodyPlan.FineSub);
        _armL.Mesh = Made(Figure.NearOf(plan, (int)Part.Off, Now(BodyPart.Off), out _), BodyPlan.FineSub);
        _held.Mesh = _wi < 0 ? null : Made(Figure.DressOf(plan, _wi, Now(plan.Dress[_wi].On), out _, _bow ? Mat.Linen : (byte)0), plan.Dress[_wi].Sub);
        _shield.Mesh = _si < 0 ? null : Made(Figure.DressOf(plan, _si, Now(plan.Dress[_si].On), out _), plan.Dress[_si].Sub);
        _armR.Visible = _armR.Mesh != null; _armL.Visible = _armL.Mesh != null;
        _held.Visible = _held.Mesh != null; _shield.Visible = _shield.Mesh != null;
    }

    static ArrayMesh Made(MeshBuffers b, int grid)
    {
        var mesh = GodotMesh.From(b);
        mesh?.SurfaceSetMaterial(0, Drawn(grid));
        return mesh;
    }

    static Shader _voxel;
    static readonly Dictionary<int, ShaderMaterial> Mats = new();

    /// <summary>The figures' material, drawn before the world: the voxel shader with VIEW_MODEL defined.</summary>
    static ShaderMaterial Drawn(int grid)
    {
        if (Mats.TryGetValue(grid, out var m)) return m;
        _voxel ??= new Shader { Code = GD.Load<Shader>("res://shaders/voxel.gdshader").Code.Replace("shader_type spatial;", "shader_type spatial;\n#define VIEW_MODEL") };
        m = new ShaderMaterial { Shader = _voxel };
        m.SetShaderParameter("grid", (float)grid);
        return Mats[grid] = m;
    }

    static Shader _plain;

    /// <summary>A plain colour for a bowstring or an arrow, pulled before the world as the figures are.</summary>
    static ShaderMaterial Plain(Color c)
    {
        _plain ??= new Shader
        {
            Code = "shader_type spatial;\nrender_mode cull_disabled;\nuniform vec4 albedo : source_color;\n"
                + "void vertex() {\n\tPOSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);\n\tPOSITION.z = mix(POSITION.z, POSITION.w, 0.85);\n}\n"
                + "void fragment() {\n\tALBEDO = albedo.rgb;\n\tROUGHNESS = 0.75;\n}\n",
        };
        var m = new ShaderMaterial { Shader = _plain };
        m.SetShaderParameter("albedo", c);
        return m;
    }

    static Vector3 V(Vec v) => new(v.X, v.Y, v.Z);
    static Vector3 V((float X, float Y, float Z) v) => new(v.X, v.Y, v.Z);

    /// <summary>Where the right fist closes, in figure voxels: on the weapon's grip, or the fist at rest.</summary>
    Vector3 Fist() => _wi >= 0 && _plan.Dress[_wi].On == BodyPart.Main ? V(_plan.Dress[_wi].Grip) : V(_g.Fist);

    /// <summary>A frame with its x along <paramref name="a"/> and its y along as much of <paramref name="e"/> as is square to it.</summary>
    static Basis Frame(Vector3 a, Vector3 e)
    {
        a = a.Normalized();
        var p = e - a * a.Dot(e);
        if (p.LengthSquared() < 1e-4f) { p = Vector3.Up - a * a.Y; if (p.LengthSquared() < 1e-4f) p = Vector3.Back - a * a.Z; }
        p = p.Normalized();
        return new Basis(a, p, a.Cross(p));
    }

    /// <summary>What is held pointing along <paramref name="d"/>, its edge toward <paramref name="e"/>, turned about its axis by <paramref name="twist"/>.</summary>
    static Quaternion Aim(Vector3 d, Vector3 e, float twist = 0)
    {
        var b = Frame(d, e);
        if (twist != 0) b = new Basis(b.X, twist) * b;
        return new Quaternion(b.Orthonormalized());
    }

    /// <summary>
    /// The arms a little smaller than the figure's: its fists are sculpted big, to read from above, and so near the eye
    /// one would fill a quarter of the sight. What they hold keeps its true size.
    /// </summary>
    const float ArmSize = .65f;

    /// <summary>A piece in figure voxels set before the eye: turned by <paramref name="r"/> about its point <paramref name="g"/>, which comes to <paramref name="p"/> (metres).</summary>
    static void Place(MeshInstance3D mi, Basis r, Vector3 g, Vector3 p, float size = 1)
    {
        float s = VoxelWorld.Vox * size;
        var b = r * Basis.FromScale(new Vector3(s, s, s));
        mi.Transform = new Transform3D(b, p * M - b * g);
    }

    /// <summary>The weapon in the fist as the hold has it.</summary>
    void Held(MeshInstance3D mi, in Pose pose)
    {
        if (_wi < 0 || mi.Mesh == null) return;
        var w = _plan.Dress[_wi];
        var a = V(w.Tip) - V(w.Grip);
        var e = V(w.Edge);
        Place(mi, new Basis(pose.Q) * Frame(a.LengthSquared() > 1e-4f ? a : Vector3.Back, e.LengthSquared() > 1e-4f ? e : Vector3.Down).Transposed(), V(w.Grip), pose.P);
    }

    /// <summary>
    /// An arm with its fist at <paramref name="p"/>, coming to it from <paramref name="elbow"/> (both in metres), what the
    /// fist holds going through it along <paramref name="d"/> as near as the wrist allows. At rest the arm hangs from the
    /// shoulder and what it holds goes through the fist along +z.
    /// </summary>
    void Arm(MeshInstance3D mi, bool left, Vector3 fist, Vector3 p, Vector3 d, Vector3 elbow)
    {
        if (mi.Mesh == null) return;
        var up = (left ? V(_g.OffShoulder) : V(_g.Shoulder)) - fist;
        Place(mi, Frame(elbow - p, d) * Frame(up, Vector3.Back).Transposed(), fist, p, ArmSize);
        mi.Visible = true;
    }

    // ---------------------------------------------------------------- holds and strokes

    static Pose At(float x, float y, float z, Vector3 d, Vector3 e, float twist = 0) => new() { P = new Vector3(x, y, z), Q = Aim(d, e, twist) };

    static Pose Mix(in Pose a, in Pose b, float k) => new() { P = a.P.Lerp(b.P, k), Q = a.Q.Slerp(b.Q, k).Normalized() };

    // the guards: a blade low on the right slanting up and across, the flat half turned to the eye; a polearm, a maul or
    // a pick across the body in both hands; a spear levelled; a crossbow or a sling held out before the eye
    static readonly Pose GuardOne = At(.20f, -.24f, -.42f, new Vector3(-.30f, .84f, -.45f), Vector3.Forward, 1.05f);
    static readonly Pose GuardTwo = At(.20f, -.30f, -.36f, new Vector3(-.38f, .72f, -.58f), Vector3.Forward, .5f);
    static readonly Pose GuardSpear = At(.18f, -.24f, -.26f, new Vector3(-.10f, .16f, -.98f), Vector3.Up);
    static readonly Pose GuardAimed = At(.13f, -.17f, -.38f, new Vector3(-.04f, .03f, -1f), Vector3.Down);

    Pose Guard() => _aimed ? GuardAimed : _spear ? GuardSpear : _two ? GuardTwo : GuardOne;

    /// <summary>A stroke's three holds: cocked, at the blow, spent — the edge leading the way the blade goes.</summary>
    static Pose[] Arc(Vector3 pw, Vector3 dw, Vector3 pc, Vector3 dc, Vector3 pe, Vector3 de) => new[]
    {
        new Pose { P = pw, Q = Aim(dw, dc - dw) }, new Pose { P = pc, Q = Aim(dc, de - dw) }, new Pose { P = pe, Q = Aim(de, de - dc) },
    };

    static Vector3 v(float x, float y, float z) => new(x, y, z);

    static readonly Pose[] Overhead = Arc(v(.18f, .14f, -.34f), v(-.1f, .5f, .86f), v(.05f, -.05f, -.55f), v(0, .2f, -.98f), v(-.02f, -.40f, -.45f), v(-.1f, -.85f, -.5f));
    static readonly Pose[][] Shapes =
    {
        // over the head: cocked down the back, brought down the middle, spent low before the feet
        Overhead,
        // the forehand from over the right shoulder down across to the low left
        Arc(v(.34f, .12f, -.34f), v(-.3f, .6f, .75f), v(.06f, -.08f, -.55f), v(-.70f, .15f, -.70f), v(-.22f, -.40f, -.35f), v(-.60f, -.65f, -.45f)),
        // the backhand from over the left shoulder down to the low right
        Arc(v(-.22f, .10f, -.38f), v(.4f, .6f, .7f), v(.02f, -.08f, -.55f), v(.75f, .10f, -.65f), v(.32f, -.36f, -.32f), v(.65f, -.60f, -.45f)),
        // flat across from the right, round through the front to the left
        Arc(v(.42f, -.06f, -.30f), v(.85f, .2f, -.1f), v(.05f, -.12f, -.55f), v(-.90f, .05f, -.42f), v(-.30f, -.16f, -.30f), v(-.55f, 0, .83f)),
        // the point driven home from the hip, the flat level
        new[] { At(.16f, -.18f, -.20f, v(-.05f, .05f, -1), Vector3.Right), At(.04f, -.10f, -.75f, v(-.03f, .04f, -1), Vector3.Right), At(.04f, -.10f, -.75f, v(-.03f, .04f, -1), Vector3.Right) },
        // up from low on the right to high on the left
        Arc(v(.28f, -.40f, -.34f), v(.55f, -.55f, -.6f), v(.04f, -.12f, -.55f), v(-.6f, .5f, -.62f), v(-.20f, .12f, -.40f), v(-.4f, .85f, .35f)),
        // the shield driven in: the blade drawn back out of its way
        new[] { At(.26f, -.30f, -.28f, v(-.25f, .85f, -.3f), Vector3.Forward, 1.05f), At(.26f, -.32f, -.26f, v(-.25f, .85f, -.3f), Vector3.Forward, 1.05f), At(.26f, -.32f, -.26f, v(-.25f, .85f, -.3f), Vector3.Forward, 1.05f) },
        // a polearm's head brought down from high
        Overhead,
        // a club or a maul up over the right shoulder and smashed down
        Arc(v(.26f, .14f, -.32f), v(-.2f, .55f, .8f), v(.06f, -.06f, -.55f), v(-.15f, .15f, -.97f), v(-.06f, -.42f, -.42f), v(-.2f, -.85f, -.45f)),
        Overhead,
    };

    static Pose[] Keys(StrokeKind k) => Shapes[Math.Clamp((int)k, 0, Shapes.Length - 1)];

    // a stroke's beats (seconds): cocked, struck across the sight, carried through, held, and back to the guard; the
    // blade comes to the body at the end of the second, when the simulation lands the blow (Sim.HandBlowLag)
    const float Tw = .07f, Ts = Sim.HandBlowLag - Tw, Tf = .12f, Th = .04f, Tb = .32f, Total = Tw + Ts + Tf + Th + Tb;

    static float Smooth(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    static float In(float x) { x = Math.Clamp(x, 0, 1); return x * x; }
    static float Out(float x) { x = Math.Clamp(x, 0, 1); return 1 - (1 - x) * (1 - x); }

    /// <summary>The hold <paramref name="t"/> seconds into the stroke: speeding into the blow, slowing after it, and home.</summary>
    Pose Stroke(float t, in Pose home)
    {
        var k = Keys(_k);
        if (t < Tw) return Mix(_from, k[0], Out(t / Tw));
        t -= Tw;
        if (t < Ts) return Mix(k[0], k[1], In(t / Ts));
        t -= Ts;
        // turned by mail or a shield, the blade comes back off it instead of going on through
        var end = _glanced ? Mix(k[1], k[0], .3f) : k[2];
        if (t < Tf) return Mix(k[1], end, Out(t / Tf));
        t -= Tf + Th;
        return t < 0 ? end : Mix(end, home, Smooth(t / Tb));
    }

    /// <summary>Of a stroke, how far the shield is driven out (the bash), or drawn in out of the blade's way (the rest).</summary>
    float Bash(float t)
    {
        if (t >= Total || !_stroking) return 0;
        bool bash = _k == StrokeKind.Bash;
        if (t < Tw) return (bash ? -.3f : -.4f) * Out(t / Tw);
        t -= Tw;
        if (t < Ts) return bash ? Mathf.Lerp(-.3f, 1, In(t / Ts)) : -.4f;
        t -= Ts + Tf + Th;
        return (bash ? 1 : -.4f) * (1 - Smooth(t / Tb));
    }

    /// <summary>The pick's round, 0…1: lifted over the right shoulder, brought down before the eyes, and the jar of the stone.</summary>
    static Pose Pick(float p)
    {
        var low = At(.04f, -.16f, -.48f, v(-.1f, -.35f, -.93f), Vector3.Down);
        var high = At(.22f, .10f, -.18f, v(-.25f, .55f, .8f), Vector3.Forward);
        if (p < .45f) return Mix(low, high, Smooth(p / .45f));
        if (p < .62f) return Mix(high, low, In((p - .45f) / .17f));
        var jar = low;
        jar.P += new Vector3(0, .015f, .01f) * MathF.Sin((p - .62f) / .38f * MathF.PI) * (1 - (p - .62f) / .38f);
        return jar;
    }

    /// <summary>The shield on the left forearm: its upper right at the lower left of the sight, its face to the foe.</summary>
    void Shield(float since)
    {
        var w = _plan.Dress[_si];
        float b = Bash(since);
        var c = new Vector3(-.42f, -.36f, -.38f);
        var n = new Vector3(.15f, -.15f, -.98f);
        if (b > 0) { c = c.Lerp(new Vector3(-.16f, -.20f, -.50f), b); n = n.Lerp(new Vector3(0, -.05f, -1), b); }
        else c += new Vector3(-.05f, -.06f, .04f) * -b;
        var r = Frame(n, new Vector3(-.35f, -.85f, .4f)) * Frame(V(w.Edge), V(_g.OffShoulder) - V(w.Grip)).Transposed();
        if (_shield.Mesh != null) Place(_shield, r, V(w.Grip), c);
        if (_armL.Mesh != null) { Place(_armL, r, V(w.Grip), c, ArmSize); _armL.Visible = true; }
    }

    // ---------------------------------------------------------------- the bow

    static void Rod(MeshInstance3D mi, Vector3 a, Vector3 b, float thick)
    {
        a *= M; b *= M; thick *= M;
        var d = b - a;
        float l = d.Length();
        if (l < 1e-5f) { mi.Visible = false; return; }
        var y = d / l;
        var x = (MathF.Abs(y.Y) < .9f ? Vector3.Up : Vector3.Right).Cross(y).Normalized();
        mi.Transform = new Transform3D(new Basis(x * thick, d, x.Cross(y) * thick), (a + b) / 2);
        mi.Visible = true;
    }

    /// <summary>
    /// A bowman: the bow out on the left, the string drawn to the jaw while one of the horde is before it or a shot is
    /// asked for, and the shot is ready; loosed, the string snaps straight and the hand flies back, goes down for another
    /// arrow, nocks it and draws.
    /// </summary>
    void Bow(Unit u, float sdt, float dt, bool foe, bool snap)
    {
        if (u.Cooldown > _cool + .05f) { _cool0 = u.Cooldown; _loosed = _hand; }
        _cool = u.Cooldown;
        float f = _cool0 > 0 ? Math.Clamp(1 - u.Cooldown / _cool0, 0, 1) : 1;
        float k = 1 - MathF.Exp(-dt * 6);
        _up = snap ? (foe ? 1 : .3f) : _up + ((foe ? 1 : .3f) - _up) * k;
        float target = f < .45f ? 0 : Smooth((f - .45f) / .45f) * (foe ? 1 : .2f);
        _draw = f < .45f || snap ? target : _draw + (target - _draw) * (1 - MathF.Exp(-dt * 8));
        // the bow hand, the bow canted with its top to the right; jerked forward and rolled as the string goes
        float jerk = f < .1f ? 1 - f / .1f : 0;
        var b = new Vector3(-.18f, -.24f, -.52f).Lerp(new Vector3(-.10f, -.10f, -.62f), _up) + new Vector3(0, 0, -.02f) * jerk;
        float tilt = Mathf.Lerp(.6f, .26f, _up) + .07f * jerk;
        var axis = new Vector3(MathF.Sin(tilt), MathF.Cos(tilt), 0);
        var w = _plan.Dress[_wi];
        var grip = V(w.Grip);
        var r = Frame(new Vector3(-.22f, -.30f, .05f) - b, axis) * Frame(V(_g.OffShoulder) - grip, Vector3.Back).Transposed();
        if (_held.Mesh != null) Place(_held, r, grip, b);
        if (_armL.Mesh != null) { Place(_armL, r, grip, b, ArmSize); _armL.Visible = true; }
        // the tips of its limbs, where the string is tied
        var tip = V(w.Tip);
        float s = VoxelWorld.Vox / M;
        Vector3 Seen(Vector3 local) => b + r * (local - grip) * s;
        Vector3 top = Seen(tip), low = Seen(new Vector3(tip.X, tip.Y, -tip.Z)), rest = (top + low) / 2;
        // the drawing hand: flown back, down to the quiver and back with an arrow, on the string and drawn to the jaw
        // drawn to the cheek beside the eye: the hand all but out of sight at the lower right, the arrow running from it
        var anchor = new Vector3(.08f, -.11f, -.03f);
        var quiver = new Vector3(.32f, -.55f, -.05f);
        var flown = _loosed + new Vector3(.03f, -.01f, .06f);
        _hand = f < .1f ? _loosed.Lerp(flown, Out(f / .1f)) : f < .28f ? flown.Lerp(quiver, Smooth((f - .1f) / .18f))
            : f < .45f ? quiver.Lerp(rest, Smooth((f - .28f) / .17f)) : rest.Lerp(anchor, _draw);
        bool nocked = f >= .45f;
        // the string: straight and shivering after the shot, else to the fingers
        var nock = nocked ? _hand : rest + (f < .1f ? new Vector3(.006f * MathF.Sin(f * 140), 0, 0) * jerk : Vector3.Zero);
        Rod(_strA, top, nock, .003f);
        Rod(_strB, nock, low, .003f);
        // the arrow from the fingers out past the bow's grip on its right
        if (f >= .28f)
        {
            var shelf = b + new Vector3(.022f, .02f, 0);
            var dir = (shelf - _hand).Normalized();
            var point = _hand + dir * .74f;
            Rod(_shaft, _hand, point - dir * .05f, .008f);
            Rod(_head, point - dir * .05f, point, .013f);
        }
        else _shaft.Visible = _head.Visible = false;
        Arm(_armR, false, V(_g.Fist), _hand, (b - _hand).Normalized(), new Vector3(.34f, -.16f, .10f));
        // drawn to the cheek the hand is beside the eye, out of sight: only the string and the arrow show
        if (nocked && _draw > .5f) _armR.Visible = false;
    }
}
