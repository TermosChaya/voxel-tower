using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The blows as they are drawn: a fighter with a blade, an axe, a club, a spear, a shield or its jaws stands at its
/// guard; as its arm comes round again it draws back for the stroke it means next; the blow goes fast along the very
/// sweep the simulation laid out to where it meets the body, on through it (or back off armour that turned it), and
/// the arms come back to the guard — every move flowing on from wherever the arms were, nothing snapping. The arms turn
/// every way at the shoulders, the trunk twists and leans at the hips, the feet step into the blow. Where the blade
/// meets the body, at the moment it is seen to: sparks off steel and mail, splinters off a shield, blood sprayed on
/// the way the blade went; and near the eye, the smear of the blade.
/// </summary>
public partial class UnitView
{
    /// <summary>The arms, the trunk and the step of one figure this frame, as turns at the joints.</summary>
    struct Turns { public Quaternion Body, Main, Off; public Vector3 Step; }

    /// <summary>
    /// What the view keeps of a fighter between frames: the pose last drawn and the stroke it was of, the blow it saw
    /// begin and the pose it began from, how far the fight has taken over the arms.
    /// </summary>
    sealed class Stance { public StrokePose Drawn, From; public StrokeKind Kind; public double Seen = double.NaN, Was; public float In, LegR, LegL; public bool Has; }

    readonly Dictionary<int, Stance> _stances = new();
    readonly List<(Smiting h, double at)> _smitings = new();
    BladeTrail _trail;
    double _stancesSwept;

    /// <summary>The blow from drawn back to where it meets the body; on through it; back to the guard (seconds).</summary>
    public const float StrikeTime = .085f, FollowTime = .2f, RecoverTime = .4f;

    /// <summary>Figures drawn, frame by frame, with an arm turned out of the plane of a plain swing (the self-test asks).</summary>
    public int Twisted { get; private set; }

    /// <summary>The smears of the blades drawn this frame and in all.</summary>
    public int Smears => _trail?.Ever ?? 0;

    static float Smooth(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    static Quaternion Q(Turn t) => new(t.X, t.Y, t.Z, t.W);
    static Vector3 V(Vec v) => new(v.X, v.Y, v.Z);

    static Transform3D TurnAt(Figure f, Part p, Quaternion q)
    {
        if (q.IsEqualApprox(Quaternion.Identity)) return Transform3D.Identity;
        var j = f.Joint[(int)p];
        return new Transform3D(new Basis(q), j - new Basis(q) * j);
    }

    /// <summary>The smears of the blades, and an ear for the blows as they land.</summary>
    void StrokesInit(Sim sim)
    {
        _trail = new BladeTrail();
        AddChild(_trail);
        _trail.Init();
        sim.Smote += h => _smitings.Add((h, h.By.StrokeTime + StrikeTime));
    }

    /// <summary>Before the figures are posed: last frame's smears are gone, and what a blow struck a moment ago shows now.</summary>
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
    /// stroke, the one going over into the other in a moment.
    /// </summary>
    Turns StrokeTurns(Unit u, Figure f, Look look, float main, float off, float lean, ref float legR, ref float legL, in Transform3D root)
    {
        var plain = new Turns { Body = new Quaternion(Vector3.Right, lean), Main = new Quaternion(Vector3.Right, main), Off = new Quaternion(Vector3.Right, off) };
        if (Kinds.IsEngine(u.T) || u.T.Ranged) return plain;
        var sim = _m.Sim;
        bool able = !u.Dead && u.Airborne == null && !u.Bound && !sim.Climbing(u);
        float since = (float)(sim.Time - u.StrokeTime);
        bool fighting = able && (u.State == UnitState.Fight || since >= 0 && since < StrikeTime + FollowTime + RecoverTime);
        if (!_stances.TryGetValue(u.Id, out var st))
        {
            if (!fighting) return plain;
            _stances[u.Id] = st = new Stance { Was = _t };
        }
        float dt = (float)Math.Clamp(_t - st.Was, 0, .1);
        st.Was = _t;
        st.In = fighting ? Math.Min(1, st.In + dt * 7) : Math.Max(0, st.In - dt * 5);
        if (!fighting && (!able || st.In <= 0)) { st.In = 0; st.Has = false; return plain; }
        var pose = fighting ? FightPose(u, f, st, since, dt) : st.Drawn;
        float k = Smooth(st.In);
        var t = new Turns
        {
            Body = plain.Body.Slerp(new Quaternion(Vector3.Up, pose.Twist) * new Quaternion(Vector3.Right, pose.Lean), k),
            Main = f.Beast ? plain.Main : plain.Main.Slerp(Q(pose.Main), k),
            Off = f.Beast ? plain.Off : plain.Off.Slerp(Q(pose.Off), k),
            Step = new Vector3(0, -pose.Drop, pose.Lunge) * k,
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
            legR += (st.LegR - legR) * k; legL += (st.LegL - legL) * k;
        }
        else if (f.Mounted)
        {
            // the horse takes the step: its legs reach out and drive under
            float th = Math.Clamp(pose.Lunge * .07f, -.5f, .5f);
            legR += (-th - legR * .5f) * k; legL += (th - legL * .5f) * k;
        }
        if (k > .5f && !f.Beast && MathF.Abs(t.Main.Y) + MathF.Abs(t.Main.Z) > .08f) Twisted++;
        Smear(u, f, root, since, k);
        return t;
    }

    /// <summary>
    /// A fighter's pose now: the stroke from drawn back fast through the moment it meets the body, then on through it
    /// (or back off armour that turned it), then back to the guard — and drawn back for the next as its arm comes round.
    /// The first moments of a blow flow on from wherever the arms were.
    /// </summary>
    StrokePose FightPose(Unit u, Figure f, Stance st, float since, float dt)
    {
        var sim = _m.Sim;
        var g = Strokes.GripOf(f.Plan, u.T);
        StrokePose pose;
        if (since >= 0 && since < StrikeTime + FollowTime)
        {
            float s = Along(u, since);
            st.Kind = u.Stroke;
            pose = Strokes.Pose(g, u.T, u.Stroke, s, u.StrokeAim, u.StrokeYaw, u.StrokeLunge);
            if (st.Seen != u.StrokeTime) { st.Seen = u.StrokeTime; st.From = st.Has ? st.Drawn : Ready(u, g, out _); }
            float flow = since / (StrikeTime * .8f);
            if (flow < 1) pose = Strokes.Mix(st.From, pose, Smooth(flow));
        }
        else
        {
            var rest = Ready(u, g, out var next);
            float back = (since - StrikeTime - FollowTime) / RecoverTime;
            // the feet go with the stroke being struck, then with the one drawn back for
            st.Kind = since >= 0 && back < .5f || next == null ? u.Stroke : next.Value;
            if (since >= 0 && back < 1)
            {
                var spent = Strokes.Pose(g, u.T, u.Stroke, Along(u, StrikeTime + FollowTime), u.StrokeAim, u.StrokeYaw, u.StrokeLunge);
                pose = Strokes.Mix(spent, rest, Smooth(back));
            }
            else pose = rest;
            // out of the blow itself the arms go after the pose rather than jump to it: one set on another, or meaning
            // another stroke, comes round to it in a moment
            if (st.Has) pose = Strokes.Mix(st.Drawn, pose, 1 - MathF.Exp(-dt / .045f));
        }
        st.Drawn = pose; st.Has = true;
        return pose;
    }

    /// <summary>
    /// How far along its stroke (0 drawn back … 1 spent) the blade is so long after the blow: gathering speed to where
    /// it meets the body, then slowing as it goes on through; one turned by armour comes back off it a little way.
    /// </summary>
    static float Along(Unit u, float since)
    {
        float sc = Math.Clamp(u.StrokeAt, .05f, .95f);
        if (since < StrikeTime) { float x = Math.Max(0, since / StrikeTime); return sc * x * x; }
        float y = Math.Clamp((since - StrikeTime) / FollowTime, 0, 1), out_ = 1 - (1 - y) * (1 - y);
        return u.Glanced ? sc - sc * .3f * out_ : sc + (1 - sc) * out_;
    }

    /// <summary>Its guard, breathing; and as its arm comes round again, drawn back for the stroke it means next at whoever it is set on.</summary>
    StrokePose Ready(Unit u, Grip g, out StrokeKind? next)
    {
        var sim = _m.Sim;
        float breath = MathF.Sin((float)_t * 1.7f + u.Id * .618f) * .02f;
        float span = Math.Min(.5f, .45f * u.T.AttackTime), wind = u.State == UnitState.Fight ? Math.Clamp(1 - u.Cooldown / span, 0, 1) : 0;
        StrokePose p;
        next = null;
        if (wind <= 0) p = Strokes.Guard(g, u.T);
        else
        {
            var k = sim.Upcoming(u);
            var at = u.Target ?? u.StrokeFor;
            float aim = 0, yaw = 0, lunge = 0;
            if (at is { Dead: false }) sim.Aim(u, at, k, out aim, out yaw, out lunge);
            p = Strokes.Windup(g, u.T, k, wind, aim, yaw, lunge);
            next = k;
        }
        p.Lean += breath;
        return p;
    }

    /// <summary>
    /// The smear of the blade through the fast of a blow, for one near enough the eye: where the blade was over the last
    /// few hundredths of a second, worked out along the stroke.
    /// </summary>
    void Smear(Unit u, Figure f, in Transform3D root, float since, float k)
    {
        if (since < 0 || since > StrikeTime + FollowTime * .7f || k < .5f || u.Stroke is StrokeKind.Bash or StrokeKind.Bite || _trail == null) return;
        if (_trail.Drawn > 40) return;
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
        const int N = 9;
        Span<Vector3> roots = stackalloc Vector3[N], tips = stackalloc Vector3[N];
        int n = 0;
        for (int i = 0; i < N; i++)
        {
            float t = since - .075f * i / (N - 1);
            if (t < 0) { if (n > 0) break; t = 0; }
            var p = Strokes.Pose(g, u.T, u.Stroke, Along(u, t), u.StrokeAim, u.StrokeYaw, u.StrokeLunge);
            roots[n] = root * V(Strokes.OnMain(g, p, Strokes.Blade(g, .05f)));
            tips[n] = root * V(Strokes.OnMain(g, p, Strokes.Blade(g, 1)));
            n++;
        }
        bool club = u.Stroke == StrokeKind.Smash || u.T.Tool is "club" or "maul" or "mace" or "mallet" or "staff";
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
