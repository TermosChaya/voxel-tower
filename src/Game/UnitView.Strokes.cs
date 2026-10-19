using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The blows as they are drawn: a fighter with a blade, an axe, a club, a spear, a shield or its jaws stands at its
/// guard; as its arm comes round again it draws back for the stroke it means next; the blow goes fast along the very
/// sweep the simulation lays out, so as to get to the body in the moment the simulation strikes it — the wound opens,
/// a head or a hand comes off and one struck dead starts to fall as the blade gets there — then on through it (or back
/// off armour that turned it), and the arms come back to the guard, every move flowing on from wherever the arms were,
/// nothing snapping. A blow the eye could not see coming (one who came into reach with its arm already round) is
/// brought down at once, a few hundredths of a second, and what it does shows as it lands. The arms turn every way at
/// the shoulders, the trunk twists and leans at the hips, the feet step into the blow — unless the fighter is walking,
/// and then the legs walk. Where the blade meets the body, as it is seen to: sparks off steel and mail, splinters off a
/// shield, blood sprayed on the way the blade went; and near the eye, the smear of the blade.
/// </summary>
public partial class UnitView
{
    /// <summary>The arms, the trunk and the step of one figure this frame, as turns at the joints.</summary>
    struct Turns { public Quaternion Body, Main, Off; public Vector3 Step; }

    /// <summary>
    /// What the view keeps of a fighter between frames: the pose last drawn and the stroke it was of, the blow it saw
    /// begin and the pose it began from, how far the fight has taken over the arms; the blow it sees coming and how it
    /// will go; whether it is bringing that blow down now, and when it last did.
    /// </summary>
    sealed class Stance
    {
        public StrokePose Drawn, From;
        public StrokeKind Kind;
        public double Seen = double.NaN, Was;
        public float In, LegR, LegL;
        public bool Has;
        /// <summary>How long after the simulation struck the blade was drawn to get there (nought for one seen coming), for the blow struck at LagAt.</summary>
        public float Lag;
        public double LagAt = double.NaN;
        /// <summary>The blow foreseen: its stroke, laid out with the draws at their middle (aim, yaw, step, where it meets), at whom, and when worked out.</summary>
        public bool Fore;
        public StrokeKind FK;
        public float FAim, FYaw, FLunge, FAt;
        public int FFor = -1, FCount = -1;
        public double FWhen = double.NegativeInfinity;
        /// <summary>Bringing the foreseen blow down now, and when last (simulation seconds).</summary>
        public bool Pre;
        public double PreSeen = double.NegativeInfinity;
        /// <summary>How the first moments after the blow go on from the strike drawn before it: 0 they do not, 1 along the foreseen stroke, 2 from the pose last drawn.</summary>
        public byte Carry;
    }

    readonly Dictionary<int, Stance> _stances = new();
    readonly List<(Smiting h, double at)> _smitings = new();
    BladeTrail _trail;
    double _stancesSwept;

    /// <summary>
    /// The blow from drawn back to where it meets the body; on through it; back to the guard (seconds). One not seen
    /// coming is brought down in the quick of it; the stroke foreseen gives way to the one struck over the blend.
    /// </summary>
    public const float StrikeTime = .085f, FollowTime = .2f, RecoverTime = .4f, QuickTime = .045f, BlendTime = .06f;

    /// <summary>How long past its time a blow drawn to the body is held there before the arm goes back (the one meant slipped out of reach).</summary>
    const float Overdue = .04f;

    /// <summary>Figures drawn, frame by frame, with an arm turned out of the plane of a plain swing (the self-test asks).</summary>
    public int Twisted { get; private set; }

    /// <summary>The smears of the blades drawn this frame and in all.</summary>
    public int Smears => _trail?.Ever ?? 0;

    /// <summary>Blows struck, and of them those whose blade the eye had brought down to the body by the moment they were struck (the self-test asks).</summary>
    public int Blows { get; private set; }
    public int BlowsSeenComing { get; private set; }

    /// <summary>A blade starts down at someone: the swish of it.</summary>
    public event Action<Unit> Swung;

    /// <summary>A blow is seen to land, where the blade gets to the body: the ring, the knock or the thud of it.</summary>
    public event Action<Smiting> Landed;

    static float Smooth(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    static Quaternion Q(Turn t) => new(t.X, t.Y, t.Z, t.W);
    static Vector3 V(Vec v) => new(v.X, v.Y, v.Z);

    static Transform3D TurnAt(Figure f, Part p, Quaternion q)
    {
        if (q.IsEqualApprox(Quaternion.Identity)) return Transform3D.Identity;
        var j = f.Joint[(int)p];
        return new Transform3D(new Basis(q), j - new Basis(q) * j);
    }

    /// <summary>The smears of the blades, and an eye for the blows as they are struck.</summary>
    void StrokesInit(Sim sim)
    {
        _trail = new BladeTrail();
        AddChild(_trail);
        _trail.Init();
        sim.Smote += h =>
        {
            var u = h.By;
            if (!_stances.TryGetValue(u.Id, out var st)) _stances[u.Id] = st = new Stance { Was = _t };
            // a blow seen coming was drawn to the body by now; one that was not is brought down at once and lands as
            // it gets there; one struck with the game stopped, or by the one the player is in, lands at once
            bool seen = _m.Paused || u.Id == HiddenId || sim.Time - st.PreSeen < StrikeTime + .1;
            st.Lag = seen ? 0 : QuickTime;
            st.LagAt = u.StrokeTime;
            Blows++;
            if (sim.Time - st.PreSeen < StrikeTime + .1) BlowsSeenComing++;
            _smitings.Add((h, u.StrokeTime + st.Lag));
        };
    }

    /// <summary>Before the figures are posed: last frame's smears are gone, and what a blow struck shows as the blade gets there.</summary>
    void StrokesBegin(Sim sim)
    {
        _trail.Clear();
        for (int i = _smitings.Count - 1; i >= 0; i--)
        {
            var (h, at) = _smitings[i];
            // a blow struck with the game stopped shows at once
            if (sim.Time < at && !_m.Paused) continue;
            _smitings.RemoveAt(i);
            Struck(h);
            Landed?.Invoke(h);
        }
        if (_t - _stancesSwept > 5)
        {
            _stancesSwept = _t;
            var gone = new List<int>();
            foreach (var (id, st) in _stances) if (_t - st.Was > 3) gone.Add(id);
            foreach (int id in gone) _stances.Remove(id);
        }
    }

    /// <summary>After the figures are posed.</summary>
    void StrokesEnd() => _trail?.Done();

    /// <summary>
    /// The turns of a figure's joints this frame: from the angles of its walking, working or idling (main, off, lean, all
    /// about the shoulders' and the hips' cross axis), or — for one fighting hand to hand, or just after a blow — from the
    /// stroke, the one going over into the other in a moment. The step into a blow and the stance of the legs give way
    /// to the walk as the fighter walks.
    /// </summary>
    Turns StrokeTurns(Unit u, Figure f, Look look, float main, float off, float lean, ref float legR, ref float legL, in Transform3D root)
    {
        var plain = new Turns { Body = new Quaternion(Vector3.Right, lean), Main = new Quaternion(Vector3.Right, main), Off = new Quaternion(Vector3.Right, off) };
        if (Kinds.IsEngine(u.T) || u.T.Ranged) return plain;
        var sim = _m.Sim;
        bool able = !u.Dead && u.Airborne == null && !u.Bound && !sim.Climbing(u);
        _stances.TryGetValue(u.Id, out var st);
        float since = (float)(sim.Time - u.StrokeTime), lag = st != null && st.LagAt == u.StrokeTime ? st.Lag : 0;
        bool fighting = able && (u.State == UnitState.Fight || since >= 0 && since < lag + FollowTime + RecoverTime);
        if (st == null)
        {
            if (!fighting) return plain;
            _stances[u.Id] = st = new Stance { Was = _t };
        }
        float dt = (float)Math.Clamp(_t - st.Was, 0, .1);
        st.Was = _t;
        st.In = fighting ? Math.Min(1, st.In + dt * 7) : Math.Max(0, st.In - dt * 5);
        if (!fighting && (!able || st.In <= 0)) { st.In = 0; st.Has = false; st.Pre = false; return plain; }
        var pose = fighting ? FightPose(u, f, st, since, lag, dt) : st.Drawn;
        float k = Smooth(st.In);
        // walking, the legs stride as they walk and no step is taken into the blow: the arms and the trunk fight on
        float stand = k * (1 - Math.Clamp(look.Move, 0, 1));
        var t = new Turns
        {
            Body = plain.Body.Slerp(new Quaternion(Vector3.Up, pose.Twist) * new Quaternion(Vector3.Right, pose.Lean), k),
            Main = f.Beast ? plain.Main : plain.Main.Slerp(Q(pose.Main), k),
            Off = f.Beast ? plain.Off : plain.Off.Slerp(Q(pose.Off), k),
            Step = new Vector3(0, -pose.Drop, pose.Lunge) * stand,
        };
        if (!f.Beast && !f.Mounted)
        {
            // the step: the foot that stays planted where it stood, the other out to where the step takes it
            float leg = Math.Max(1, f.Joint[(int)Part.LegR].Y), th = MathF.Asin(Math.Clamp(pose.Lunge / leg, -.8f, .8f));
            // the right foot steps out for a blow from the right, the left for one from the left and for the shield
            bool rightOut = st.Kind is not (StrokeKind.DiagL or StrokeKind.Rising or StrokeKind.Bash);
            float stepR = rightOut ? -th * .7f : th, stepL = rightOut ? th : -th * .7f;
            // the feet a step behind the arms, so that changing the foot to step out with is a step and not a jump
            float follow = 1 - MathF.Exp(-dt / .05f);
            st.LegR += (stepR - st.LegR) * follow; st.LegL += (stepL - st.LegL) * follow;
            legR += (st.LegR - legR) * stand; legL += (st.LegL - legL) * stand;
        }
        else if (f.Mounted)
        {
            // the horse takes the step: its legs reach out and drive under
            float th = Math.Clamp(pose.Lunge * .07f, -.5f, .5f);
            legR += (-th - legR * .5f) * stand; legL += (th - legL * .5f) * stand;
        }
        if (k > .5f && !f.Beast && MathF.Abs(t.Main.Y) + MathF.Abs(t.Main.Z) > .08f) Twisted++;
        Smear(u, f, st, root, since, lag, k);
        return t;
    }

    /// <summary>
    /// A fighter's pose now: drawn back for the blow it means as its arm comes round; brought down fast along the stroke
    /// it foresees to meet the body in the very moment the blow is struck; then on through it along the stroke as it was
    /// struck (or back off armour that turned it), and back to the guard. The first moments of a stroke flow on from
    /// wherever the arms were.
    /// </summary>
    StrokePose FightPose(Unit u, Figure f, Stance st, float since, float lag, float dt)
    {
        var sim = _m.Sim;
        var g = Strokes.GripOf(f.Plan, u.T);
        StrokePose pose;
        // the blow about to fall: its arm round, the one it means in reach
        var foe = u.Cooldown < StrikeTime && u.Cooldown > -Overdue ? sim.Poised(u) : null;
        if (foe != null)
        {
            if (!st.Pre)
            {
                // the stroke worked out afresh as the blade starts down; it goes on from wherever the arms are
                st.Pre = true;
                Foresee(u, st, foe, fresh: true);
                st.From = st.Has ? st.Drawn : Ready(u, g, st, out _);
                Swung?.Invoke(u);
            }
            st.PreSeen = sim.Time;
            st.Kind = st.FK;
            float t = Math.Min(0, -u.Cooldown);
            pose = Strokes.Pose(g, u.T, st.FK, Along(st.FAt, false, t, StrikeTime), st.FAim, st.FYaw, st.FLunge);
            float flow = (t + StrikeTime) / (StrikeTime * .8f);
            if (flow < 1) pose = Strokes.Mix(st.From, pose, Smooth(flow));
        }
        else if (since >= 0 && since < lag + FollowTime + RecoverTime)
        {
            if (st.Seen != u.StrokeTime)
            {
                // a blow struck: drawn on from the strike brought down before it, if there was one
                st.Seen = u.StrokeTime;
                st.Carry = (byte)(lag > 0 ? 0 : st.Pre && st.Fore && st.FK == u.Stroke ? 1 : st.Has && !_m.Paused && u.Id != HiddenId ? 2 : 0);
                st.From = st.Has ? st.Drawn : Ready(u, g, st, out _);
                // one not seen coming: its swish as it comes down now
                if (lag > 0) Swung?.Invoke(u);
            }
            st.Pre = false;
            float t = since - lag;
            if (t < FollowTime)
            {
                st.Kind = u.Stroke;
                pose = Strokes.Pose(g, u.T, u.Stroke, Along(u.StrokeAt, u.Glanced, t, lag), u.StrokeAim, u.StrokeYaw, u.StrokeLunge);
                if (t < 0)
                {
                    float flow = (t + lag) / (lag * .8f);
                    if (flow < 1) pose = Strokes.Mix(st.From, pose, Smooth(flow));
                }
                else if (t < BlendTime && st.Carry > 0)
                {
                    // the blade as it was foreseen to go, giving way to the blade as it went
                    var was = st.Carry == 1 ? Strokes.Pose(g, u.T, st.FK, Along(st.FAt, u.Glanced, t, StrikeTime), st.FAim, st.FYaw, st.FLunge) : st.From;
                    pose = Strokes.Mix(was, pose, Smooth(t / BlendTime));
                }
            }
            else
            {
                var rest = Ready(u, g, st, out var next);
                float back = (t - FollowTime) / RecoverTime;
                // the feet go with the stroke struck, then with the one drawn back for
                st.Kind = back < .5f || next == null ? u.Stroke : next.Value;
                var spent = Strokes.Pose(g, u.T, u.Stroke, Along(u.StrokeAt, u.Glanced, FollowTime, lag), u.StrokeAim, u.StrokeYaw, u.StrokeLunge);
                pose = Strokes.Mix(spent, rest, Smooth(back));
                if (st.Has) pose = Strokes.Mix(st.Drawn, pose, 1 - MathF.Exp(-dt / .045f));
            }
        }
        else
        {
            st.Pre = false;
            pose = Ready(u, g, st, out var next);
            st.Kind = next ?? u.Stroke;
            // out of the blow itself the arms go after the pose rather than jump to it: one set on another, or meaning
            // another stroke, or whose blow did not come, comes round to it in a moment
            if (st.Has) pose = Strokes.Mix(st.Drawn, pose, 1 - MathF.Exp(-dt / .045f));
        }
        st.Drawn = pose; st.Has = true;
        return pose;
    }

    /// <summary>
    /// How far along its stroke (0 drawn back … 1 spent) the blade is, t seconds after it meets the body: before it (t
    /// under nought), coming down over the last lead seconds and gathering speed; after it, slowing as it goes on
    /// through; one turned by armour comes back off it a little way.
    /// </summary>
    static float Along(float sc, bool glanced, float t, float lead)
    {
        sc = Math.Clamp(sc, .05f, .95f);
        if (t < 0) { float x = Math.Clamp(1 + t / Math.Max(lead, 1e-3f), 0, 1); return sc * x * x; }
        float y = Math.Clamp(t / FollowTime, 0, 1), out_ = 1 - (1 - y) * (1 - y);
        return glanced ? sc - sc * .3f * out_ : sc + (1 - sc) * out_;
    }

    /// <summary>
    /// The blow a fighter means next at one it is set on, as it will be laid out: worked out again when the one it means,
    /// the blow or the stroke changes, now and then as they move, and fresh as the blade starts down.
    /// </summary>
    void Foresee(Unit u, Stance st, Unit b, bool fresh)
    {
        var sim = _m.Sim;
        if (b is not { Dead: false })
        {
            if (st.FCount != u.Strokes || st.FFor != -1) { st.FK = sim.Upcoming(u); st.FAim = st.FYaw = st.FLunge = 0; st.FAt = Strokes.Contact(st.FK); st.Fore = false; st.FFor = -1; st.FCount = u.Strokes; }
            return;
        }
        var k = sim.Upcoming(u, b);
        if (!fresh && st.Fore && st.FFor == b.Id && st.FCount == u.Strokes && st.FK == k && sim.Time - st.FWhen < .25) return;
        sim.Foresee(u, b, k, out st.FAim, out st.FYaw, out st.FLunge, out st.FAt);
        st.FK = k; st.Fore = true; st.FFor = b.Id; st.FCount = u.Strokes; st.FWhen = sim.Time;
    }

    /// <summary>
    /// Its guard, breathing; and as its arm comes round again, drawn back for the blow it means next at whoever it is set
    /// on, all the way back by the time the blade is to start down.
    /// </summary>
    StrokePose Ready(Unit u, Grip g, Stance st, out StrokeKind? next)
    {
        float breath = MathF.Sin((float)_t * 1.7f + u.Id * .618f) * .02f;
        float span = Math.Min(.5f, .45f * u.T.AttackTime), wind = u.State == UnitState.Fight ? Math.Clamp((span - u.Cooldown) / Math.Max(.01f, span - StrikeTime), 0, 1) : 0;
        StrokePose p;
        next = null;
        if (wind <= 0) p = Strokes.Guard(g, u.T);
        else
        {
            Foresee(u, st, u.Target ?? u.Prey ?? u.StrokeFor, fresh: false);
            p = Strokes.Windup(g, u.T, st.FK, wind, st.FAim, st.FYaw, st.FLunge);
            next = st.FK;
        }
        p.Lean += breath;
        return p;
    }

    /// <summary>
    /// The smear of the blade through the fast of a blow, for one near enough the eye: where the blade was over the last
    /// few hundredths of a second, worked out along the stroke — the one foreseen while it comes down, the one struck after.
    /// </summary>
    void Smear(Unit u, Figure f, Stance st, in Transform3D root, float since, float lag, float k)
    {
        if (_trail == null || k < .5f || _trail.Now >= 40) return;
        bool pre = st.Pre;
        // how long since (under nought: before) the blade meets the body, and which stroke it is
        float t = pre ? Math.Min(0, -u.Cooldown) : since - lag;
        var kind = pre ? st.FK : u.Stroke;
        if (!pre && since < 0 || t > FollowTime * .7f || kind is StrokeKind.Bash or StrokeKind.Bite) return;
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        var at = root.Origin;
        if (!cam.IsPositionInFrustum(at)) return;
        // near enough to see it: a voxel six pixels and more, or within a dozen metres of the eye; fainter the further
        float near = cam.Projection == Camera3D.ProjectionType.Orthogonal
            ? (GetViewport().GetVisibleRect().Size.Y / Math.Max(.01f, cam.Size) * VoxelWorld.Vox - 6) / 14
            : (12 - cam.GlobalPosition.DistanceTo(at)) / 6;
        if (near <= 0) return;
        var g = Strokes.GripOf(f.Plan, u.T);
        // the strike before the blade met: foreseen, brought down at once, or (with the game stopped) as it would have come
        bool foreseen = pre || st.Carry == 1;
        float lead = !pre && lag > 0 ? lag : StrikeTime;
        const int N = 9;
        Span<Vector3> roots = stackalloc Vector3[N], tips = stackalloc Vector3[N];
        int n = 0;
        for (int i = 0; i < N; i++)
        {
            float ti = t - .075f * i / (N - 1);
            if (ti < -lead) { if (n > 0) break; ti = -lead; }
            var p = ti < 0 && foreseen
                ? Strokes.Pose(g, u.T, st.FK, Along(st.FAt, false, ti, StrikeTime), st.FAim, st.FYaw, st.FLunge)
                : Strokes.Pose(g, u.T, u.Stroke, Along(u.StrokeAt, u.Glanced, ti, lead), u.StrokeAim, u.StrokeYaw, u.StrokeLunge);
            roots[n] = root * V(Strokes.OnMain(g, p, Strokes.Blade(g, .05f)));
            tips[n] = root * V(Strokes.OnMain(g, p, Strokes.Blade(g, 1)));
            n++;
        }
        bool club = kind == StrokeKind.Smash || u.T.Tool is "club" or "maul" or "mace" or "mallet" or "staff";
        _trail.Ribbon(roots, tips, n, club ? new Color(.55f, .48f, .4f) : new Color(.78f, .84f, .95f), (club ? .25f : .4f) * k * Math.Min(1, .25f + near));
    }

    /// <summary>What a blow throws off where it is seen to land: sparks off metal, splinters off boards, blood along the blade's way.</summary>
    void Struck(in Smiting h)
    {
        var d = _m.Debris;
        if (d == null) return;
        var at = new Vector3(_w.WX(h.X), _w.WY(h.Y), _w.WZ(h.Z));
        var way = new Vector3(h.DX, h.DY, h.DZ);
        way = way.LengthSquared() > 1e-6f ? way.Normalized() : Vector3.Down;
        byte m = h.Mat;
        bool metal = m != Mat.Air && (Mat.Metal[m] && m != Mat.Pitch || m == Mat.Iron);
        bool wood = m is Mat.Board or Mat.BoardDark or Mat.Handle || m != Mat.Air && Mat.Def[m].Group == MatGroup.Wood;
        bool crush = h.Stroke is StrokeKind.Smash or StrokeKind.Bash || Sim.WeaponOf(h.By.T).cut == Sim.Cut.Crush;
        // a blow seen with the game stopped: what it threw is already a little way out
        float ahead = _m.Paused ? .06f : 0;
        if (metal) d.Sparks(at, h.Glanced ? -way * .5f + Vector3.Up * .3f : way, h.Glanced ? 16 : crush ? 6 : 10, crush ? 3 : 4.5f, ahead);
        if (wood) d.Spray(m, at, (way + Vector3.Up * .4f).Normalized(), crush ? 7 : 5, 2.6f, .7f, .32f, 3.2f, .8f, ahead);
        if (h.Flesh > 0)
        {
            // the blood goes on the way the blade went, a jet and a scatter of drops, more for a deeper cut
            int n = Math.Clamp(3 + h.Flesh / 2, 4, 18);
            d.Spray(Mat.Blood, at, way, n, crush ? 1.8f : 3.2f, crush ? .9f : .4f, .28f, 1.4f, .9f, ahead);
            d.Spray(Mat.Blood, at, (way * .3f + Vector3.Up).Normalized(), n / 3, 1.4f, .8f, .22f, 1, .7f, ahead);
        }
        else if (!metal && !wood && m != Mat.Air && !h.Glanced) d.Spray(m, at, way, 2, 1.6f, .8f, .25f, 1, .6f, ahead);
    }
}
