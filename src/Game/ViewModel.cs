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
    // the hands cut off the arms at the wrist, so that a fist can turn onto what it holds as a wrist bends; how high the
    // wrists are in the figure's voxels
    MeshInstance3D _handR, _handL;
    float _wristR, _wristL;
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
    // how far the hands are up at the guard (1) from carried low at the side (0), and how long there has been no call to raise them
    float _raise, _calm;
    string _tool = "";
    bool _big;
    /// <summary>How near one of the horde (or a beast, nearer) brings the hands up to the guard, voxels (10 m).</summary>
    const float Threat = 10 * VoxelWorld.PerMetre;
    /// <summary>How long the hands stay up after the last call to raise them, seconds.</summary>
    const float Settle = .9f;

    public void Init()
    {
        MeshInstance3D Mi()
        {
            var mi = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            AddChild(mi);
            return mi;
        }
        _armR = Mi(); _armL = Mi(); _held = Mi(); _shield = Mi(); _handR = Mi(); _handL = Mi();
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
        _raise = 0; _calm = Settle;
        _dirty = true;
    }

    /// <summary>
    /// Whether there is a call for the hands to be up: one of the horde in reach before the eyes or near, a beast nearer,
    /// a blow struck or asked for, the guard, a heavy blow drawn back, a shot asked, the pick at work or something laid.
    /// </summary>
    static bool Called(Unit u, Sim sim, bool foe, bool striking) =>
        foe || striking || sim.WishStrike || sim.WishGuard || sim.Guarding || sim.Charge > 0 || sim.ShotAsked || sim.WishLay || u.State == UnitState.Work
        || Threatened(u, sim);

    /// <summary>One of the horde near, or a beast that falls on people nearer (a deer or a hare going by is no call).</summary>
    static bool Threatened(Unit u, Sim sim)
    {
        foreach (var o in sim.Units)
        {
            if (o.Dead || o.Inside || o.Team == Team.Player || o.Team == Team.Wild && o.T.Aggro <= 0) continue;
            float dx = o.X - u.X, dz = o.Z - u.Z, r = o.Team == Team.Wild ? Threat * .6f : Threat;
            if (dx * dx + dz * dz < r * r) return true;
        }
        return false;
    }

    /// <summary>How far the hands are up at the guard (1) from carried low at the side (0).</summary>
    public float Raised => _raise;

    /// <summary>
    /// How much of the sight the hands and what they hold cover, as a share of it (counted on a grid of 96 by 54 over
    /// the viewport, a cell covered when its middle is under a face of theirs); whether any of it is in
    /// <paramref name="middle"/>, and the middle of what is covered (for the tests); of what the fist holds alone, if asked.
    /// </summary>
    public float Covered(Camera3D cam, Vector2 size, Rect2 middle, out bool inMiddle, out Vector2 centre, bool heldOnly = false)
    {
        const int GX = 96, GY = 54;
        var on = new bool[GX * GY];
        foreach (var mi in heldOnly ? new[] { _held } : new[] { _armR, _armL, _handR, _handL, _held, _shield })
        {
            if (mi.Mesh == null || !mi.Visible || !Visible) continue;
            var t = mi.GlobalTransform;
            for (int sfc = 0; sfc < mi.Mesh.GetSurfaceCount(); sfc++)
            {
                var arrays = mi.Mesh.SurfaceGetArrays(sfc);
                var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                int n = idx.Length > 0 ? idx.Length : verts.Length;
                for (int i = 0; i + 2 < n; i += 3)
                {
                    Vector3 a = t * verts[idx.Length > 0 ? idx[i] : i], b = t * verts[idx.Length > 0 ? idx[i + 1] : i + 1], c = t * verts[idx.Length > 0 ? idx[i + 2] : i + 2];
                    if (cam.IsPositionBehind(a) || cam.IsPositionBehind(b) || cam.IsPositionBehind(c)) continue;
                    Fill(cam.UnprojectPosition(a), cam.UnprojectPosition(b), cam.UnprojectPosition(c));
                }
            }
        }
        void Fill(Vector2 a, Vector2 b, Vector2 c)
        {
            float cw = size.X / GX, ch = size.Y / GY;
            int x0 = Math.Max(0, (int)(Math.Min(a.X, Math.Min(b.X, c.X)) / cw)), x1 = Math.Min(GX - 1, (int)(Math.Max(a.X, Math.Max(b.X, c.X)) / cw));
            int y0 = Math.Max(0, (int)(Math.Min(a.Y, Math.Min(b.Y, c.Y)) / ch)), y1 = Math.Min(GY - 1, (int)(Math.Max(a.Y, Math.Max(b.Y, c.Y)) / ch));
            float area = (b - a).Cross(c - a);
            if (MathF.Abs(area) < 1e-6f) return;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2((x + .5f) * cw, (y + .5f) * ch);
                    float w0 = (b - a).Cross(p - a) / area, w1 = (c - b).Cross(p - b) / area, w2 = (a - c).Cross(p - c) / area;
                    if (w0 >= 0 && w1 >= 0 && w2 >= 0) on[y * GX + x] = true;
                }
        }
        int count = 0;
        inMiddle = false;
        centre = Vector2.Zero;
        for (int y = 0; y < GY; y++)
            for (int x = 0; x < GX; x++)
            {
                if (!on[y * GX + x]) continue;
                var p = new Vector2((x + .5f) * size.X / GX, (y + .5f) * size.Y / GY);
                count++;
                centre += p;
                if (middle.HasPoint(p)) inMiddle = true;
            }
        if (count > 0) centre /= count;
        return count / (float)(GX * GY);
    }

    /// <summary>The hands coming up at a call and going down a moment after it is over: up quickly, down slowly.</summary>
    void Raise(bool called, float dt, bool snap)
    {
        _calm = called ? 0 : _calm + dt;
        float up = _calm < Settle ? 1 : 0;
        _raise = snap ? up : _raise + (up - _raise) * (1 - MathF.Exp(-dt * (up > _raise ? 11 : 3.2f)));
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
        if (_bow)
        {
            Raise(Called(u, sim, foe, false), dt, snap);
            Bow(u, sdt, dt, foe || sim.ShotAsked || sim.WishStrike, snap);
            return;
        }

        // the eyes' swing of the last fifth of a second: the stroke the next blow is leant toward
        float fade = MathF.Exp(-dt / .2f);
        _swR = _swR * fade + turnYaw; _swD = _swD * fade - turnPitch;
        var next = _spear ? StrokeKind.Thrust : Sim.StrokeOfSwing(_swR, _swD, sim.LookPitch);
        if (next == StrokeKind.Bash && _si < 0 || next == StrokeKind.Bite) next = StrokeKind.DiagR;
        // up at the guard while there is a call for it, else carried low at the side
        bool striking = !_aimed && (u.StrokeTime > _strokeT || _stroking && sim.Time - _strokeT < Total);
        Raise(Called(u, sim, foe, striking), dt, snap);
        var guard = Mix(Carried(w, steps), Guard(), Smooth(_raise));
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
        float up = Smooth(_raise);
        // (carried low, the fist turned onto the grip; at the guard and in a stroke, the forearm the elbow's way)
        float carried = _stroking && since < Total ? 0 : 1 - up;
        Arm(_armR, false, Fist(), _now.P, D, new Vector3(.40f, -.42f, 0), carried);
        // the other hand: on the shield; once the hands are up, further up the haft or under the stock, or empty at the
        // lower left; else down at the side, out of sight
        if (_si >= 0) Shield(since);
        else if (_two || _aimed)
            Arm(_armL, true, V(_g.OffFist), Away.Lerp(_now.P + D * (_aimed ? .2f : .28f) + (_aimed ? new Vector3(0, -.04f, 0) : Vector3.Zero), up), D,
                AwayElbow.Lerp(new Vector3(-.40f, -.42f, 0), up), carried);
        else Arm(_armL, true, V(_g.OffFist), Away.Lerp(new Vector3(-.25f, -.31f, -.36f), up), new Vector3(.25f, .55f, -.8f), AwayElbow.Lerp(new Vector3(-.40f, -.50f, 0), up));
        if (_si < 0 && up < .02f) _armL.Visible = _handL.Visible = false;
        _strA.Visible = _strB.Visible = _shaft.Visible = _head.Visible = false;
    }

    /// <summary>The other hand down at the side, out of sight, and its elbow.</summary>
    static readonly Vector3 Away = new(-.32f, -.70f, -.24f), AwayElbow = new(-.42f, -.95f, .05f);

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
        _tool = u.T.Tool;
        _big = u.T.BodyH >= 20;
        _dirty = true;
    }

    /// <summary>The arms in their finest cells, what they hold in its own: as much as is left of them after their wounds.</summary>
    void Build(Unit u)
    {
        _dirty = false;
        _woundsSeen = u.Wounds; _rev = u.Wounds?.Rev ?? -1;
        var plan = u.Wounds?.Plan ?? _plan;
        PartGrid Now(BodyPart p) => u.Wounds != null ? u.Wounds.Grid[(int)p] : plan.Grid[(int)p];
        // (cut at the wrist: as far above the fist's middle as its middle is above the arm's end, a half-voxel more)
        int Wrist(Part part, Vector3 fist)
        {
            var f = plan.Fine[(int)part];
            int bottom = f == null ? 0 : (f.Y0 >= 0 ? f.Y0 / BodyPlan.K : (f.Y0 - BodyPlan.K + 1) / BodyPlan.K);
            int row = (int)MathF.Floor(fist.Y * BodyPlan.Sub);
            return row + Math.Max(1, row - bottom) + 1;
        }
        int wr = Wrist(Part.Main, Fist()), wl = Wrist(Part.Off, V(_g.OffFist));
        _wristR = wr / (float)BodyPlan.Sub; _wristL = wl / (float)BodyPlan.Sub;
        _armR.Mesh = Made(Figure.NearOf(plan, (int)Part.Main, Now(BodyPart.Main), out _, wr), BodyPlan.FineSub);
        _handR.Mesh = Made(Figure.NearOf(plan, (int)Part.Main, Now(BodyPart.Main), out _, int.MinValue, wr), BodyPlan.FineSub);
        _armL.Mesh = Made(Figure.NearOf(plan, (int)Part.Off, Now(BodyPart.Off), out _, wl), BodyPlan.FineSub);
        _handL.Mesh = Made(Figure.NearOf(plan, (int)Part.Off, Now(BodyPart.Off), out _, int.MinValue, wl), BodyPlan.FineSub);
        _held.Mesh = _wi < 0 ? null : Made(Figure.DressOf(plan, _wi, Now(plan.Dress[_wi].On), out _, _bow ? Mat.Linen : (byte)0), plan.Dress[_wi].Sub);
        _shield.Mesh = _si < 0 ? null : Made(Figure.DressOf(plan, _si, Now(plan.Dress[_si].On), out _), plan.Dress[_si].Sub);
        _armR.Visible = _armR.Mesh != null; _armL.Visible = _armL.Mesh != null;
        _handR.Visible = _handR.Mesh != null; _handL.Visible = _handL.Mesh != null;
        _held.Visible = _held.Mesh != null; _shield.Visible = _shield.Mesh != null;
        // how much further than a round shield's this one reaches from the fist, metres: carried, it goes that much lower
        _shieldTall = 0;
        if (_shield.Mesh != null)
        {
            var box = _shield.Mesh.GetAabb();
            var g = V(plan.Dress[_si].Grip);
            float far = 0;
            for (int i = 0; i < 8; i++) far = Math.Max(far, (box.GetEndpoint(i) - g).Length());
            _shieldTall = Math.Max(0, far / VoxelWorld.PerMetre - .5f);
        }
    }

    float _shieldTall;

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
    /// fist holds going through it along <paramref name="d"/>. At rest the arm hangs from the shoulder and what it holds
    /// goes through the fist along +z. At the guard (<paramref name="carry"/> 0) the forearm is turned the elbow's way and
    /// what is held goes through the fist as near <paramref name="d"/> as the wrist allows; carried low
    /// (<paramref name="carry"/> 1), the fist's way through is turned onto <paramref name="d"/> itself and the forearm
    /// comes to it as near the elbow's way as is square to that: a blade carried pointing ahead along the arm, turned the
    /// forearm's way, stood out of the top of the fist, as if from between the fingers.
    /// </summary>
    void Arm(MeshInstance3D mi, bool left, Vector3 fist, Vector3 p, Vector3 d, Vector3 elbow, float carry = 0)
    {
        var hand = left ? _handL : _handR;
        if (mi.Mesh == null && hand.Mesh == null) return;
        var up = (left ? V(_g.OffShoulder) : V(_g.Shoulder)) - fist;
        var r = Frame(elbow - p, d) * Frame(up, Vector3.Back).Transposed();
        // the forearm the elbow's way; the fist about its grip, turned onto what it holds as far as it is carried, and the
        // forearm hung from the wrist where the fist ends (turned whole, the arm stood across the chest as if held to it)
        var fistTurn = r;
        if (carry > .001f)
        {
            var held = Frame(d, elbow - p) * Frame(Vector3.Back, up).Transposed();
            fistTurn = new Basis(new Quaternion(r.Orthonormalized()).Slerp(new Quaternion(held.Orthonormalized()), Math.Clamp(carry, 0, 1)));
        }
        if (hand.Mesh != null) { Place(hand, fistTurn, fist, p, ArmSize); hand.Visible = true; }
        if (mi.Mesh != null)
        {
            var wrist = new Vector3(fist.X, left ? _wristL : _wristR, fist.Z);
            var at = p + fistTurn * (wrist - fist) * (VoxelWorld.Vox * ArmSize / M);
            Place(mi, r, wrist, at, ArmSize);
            mi.Visible = true;
        }
        if (!left) _gripAngle = Mathf.RadToDeg((fistTurn * Vector3.Back).AngleTo(d));
    }

    /// <summary>The left arm and its hand placed as one, unbent at the wrist (on the shield, at the bow).</summary>
    void Whole(bool left, Basis r, Vector3 g, Vector3 p)
    {
        foreach (var mi in left ? new[] { _armL, _handL } : new[] { _armR, _handR })
            if (mi.Mesh != null) { Place(mi, r, g, p, ArmSize); mi.Visible = true; }
    }

    float _gripAngle;
    /// <summary>How far the right fist's way through is from what it holds, degrees, as last drawn (for the tests): 0 when the grip runs straight through it.</summary>
    public float GripAngle => _gripAngle;

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

    // carried at rest, low at the right and out of the middle of the sight: a blade, a mace or an axe with its point
    // ahead, up and out to the right; a spear, a halberd or a staff leant forward up the right side, its head at the
    // top; a pick, a club or a maul by its haft at the right side, its head up and out; a crossbow, a hand gun or a
    // flask at the right hip, pointing ahead and down; a sling hanging from the fist
    static readonly Pose CarryOne = At(.29f, -.31f, -.42f, new Vector3(.34f, .38f, -.86f), Vector3.Forward, 1.05f);
    static readonly Pose CarryUpright = At(.38f, -.31f, -.44f, new Vector3(.24f, .72f, -.65f), Vector3.Forward);
    static readonly Pose CarrySide = At(.31f, -.35f, -.42f, new Vector3(.40f, .62f, -.68f), Vector3.Forward);
    static readonly Pose CarryHeavy = At(.36f, -.33f, -.40f, new Vector3(.55f, .50f, -.67f), Vector3.Forward);
    // (a troll's club or a giant's maul, as long as a man and thick as his body: further out and lower still, its head at the right edge)
    static readonly Pose CarryHuge = At(.42f, -.40f, -.40f, new Vector3(.72f, .32f, -.62f), Vector3.Forward);
    static readonly Pose CarryAimed = At(.28f, -.31f, -.42f, new Vector3(.10f, -.30f, -.95f), Vector3.Down);
    static readonly Pose CarrySling = At(.29f, -.30f, -.42f, new Vector3(.04f, -.92f, -.38f), Vector3.Forward);

    Pose Carry() => _tool switch
    {
        "spear" or "lance" or "halberd" or "staff" => CarryUpright,
        "pick" or "mallet" => CarrySide,
        "maul" or "club" => _big ? CarryHuge : CarryHeavy,
        "sling" => CarrySling,
        _ => _aimed ? CarryAimed : CarryOne,
    };

    /// <summary>What is held, carried low at the side: walking, it goes lower and swings a little with the steps.</summary>
    Pose Carried(float walk, float steps)
    {
        var p = Carry();
        if (walk <= .01f) return p;
        float s = MathF.Sin(MathF.PI * steps), c = MathF.Abs(MathF.Cos(MathF.PI * steps));
        p.P += new Vector3(.010f * s, -.03f * walk - .012f * walk * c, .015f * s);
        p.Q = (new Quaternion(Vector3.Right, .06f * walk * s) * p.Q).Normalized();
        return p;
    }

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

    /// <summary>
    /// The shield on the left forearm: up at the guard, its upper right at the lower left of the sight, its face to the
    /// foe; carried at rest, lowered at the side with only its rim showing in the corner (a tall one lower still).
    /// </summary>
    void Shield(float since)
    {
        var w = _plan.Dress[_si];
        float b = Bash(since), up = Smooth(_raise);
        var c = new Vector3(-.47f, -.45f - 2.8f * _shieldTall, -.34f).Lerp(new Vector3(-.42f, -.36f, -.38f), up);
        var n = new Vector3(.20f, -.42f, -.88f).Lerp(new Vector3(.15f, -.15f, -.98f), up);
        if (b > 0) { c = c.Lerp(new Vector3(-.16f, -.20f, -.50f), b); n = n.Lerp(new Vector3(0, -.05f, -1), b); }
        else c += new Vector3(-.05f, -.06f, .04f) * -b;
        var r = Frame(n, new Vector3(-.35f, -.85f, .4f)) * Frame(V(w.Edge), V(_g.OffShoulder) - V(w.Grip)).Transposed();
        if (_shield.Mesh != null) Place(_shield, r, V(w.Grip), c);
        Whole(true, r, V(w.Grip), c);
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
        // up and drawn for a foe in sight or a shot asked; half up with the horde about; else carried low at the left
        float want = foe ? 1 : .3f * Smooth(_raise);
        _up = snap ? want : _up + (want - _up) * k;
        float carried = Smooth(Math.Clamp(1 - _up / .3f, 0, 1));
        float target = f < .45f ? 0 : Smooth((f - .45f) / .45f) * (foe ? 1 : .2f * (1 - carried));
        _draw = f < .45f || snap ? target : _draw + (target - _draw) * (1 - MathF.Exp(-dt * 8));
        // the bow hand, the bow canted with its top to the right; jerked forward and rolled as the string goes; carried,
        // the bow hangs low at the left with its top canted out to the left
        float jerk = f < .1f ? 1 - f / .1f : 0, held = Math.Max(_up, .3f);
        var b = new Vector3(-.18f, -.24f, -.52f).Lerp(new Vector3(-.10f, -.10f, -.62f), held).Lerp(BowLow, carried) + new Vector3(0, 0, -.02f) * jerk;
        float tilt = Mathf.Lerp(Mathf.Lerp(.6f, .26f, held), -.42f, carried) + .07f * jerk;
        var axis = new Vector3(MathF.Sin(tilt), MathF.Cos(tilt), 0);
        var w = _plan.Dress[_wi];
        var grip = V(w.Grip);
        var r = Frame(new Vector3(-.22f, -.30f, .05f) - b, axis) * Frame(V(_g.OffShoulder) - grip, Vector3.Back).Transposed();
        if (_held.Mesh != null) Place(_held, r, grip, b);
        Whole(true, r, grip, b);
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
        // carried, the drawing hand is down at the side with no arrow in it
        _hand = _hand.Lerp(HandDown, carried);
        bool nocked = f >= .45f && carried < .5f;
        // the string: straight and shivering after the shot, else to the fingers
        var nock = nocked ? _hand : rest + (f < .1f ? new Vector3(.006f * MathF.Sin(f * 140), 0, 0) * jerk : Vector3.Zero);
        Rod(_strA, top, nock, .003f);
        Rod(_strB, nock, low, .003f);
        // the arrow from the fingers out past the bow's grip on its right
        if (f >= .28f && carried < .5f)
        {
            var shelf = b + new Vector3(.022f, .02f, 0);
            var dir = (shelf - _hand).Normalized();
            var point = _hand + dir * .74f;
            Rod(_shaft, _hand, point - dir * .05f, .008f);
            Rod(_head, point - dir * .05f, point, .013f);
        }
        else _shaft.Visible = _head.Visible = false;
        Arm(_armR, false, V(_g.Fist), _hand, (b - _hand).Normalized(), new Vector3(.34f, -.16f, .10f).Lerp(new Vector3(.40f, -.95f, .05f), carried));
        // drawn to the cheek the hand is beside the eye, out of sight: only the string and the arrow show
        if (nocked && _draw > .5f || carried > .95f) _armR.Visible = _handR.Visible = false;
    }

    /// <summary>The bow carried low at the left, and the drawing hand down at the right side.</summary>
    static readonly Vector3 BowLow = new(-.30f, -.45f, -.42f), HandDown = new(.30f, -.70f, -.20f);
}
