using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The town's life as it is seen (Sim.Life.cs, Sim.Voices.cs): one asleep lies on its back in its bunk or by the fire,
/// breathing slow; one eating sits with a bowl in its left hand and the spoon going to its mouth; dice are thrown from a
/// squat; a whetstone goes along a blade on the knees; a rod is held out over the water, its line down to it; a broom
/// goes to and fro before a door; the flower beds are worked on the knees; in the sanctuary one kneels upright with its
/// hands together and its head bowed. In a ring the one with the word talks with its hands and the rest listen, arms
/// folded or hanging. Two passing greet each other, a raised hand and a nod, their heads turned to each other; heads
/// turn to whoever calls out near; a commander's praise is answered with weapons up, and a band of the horde gathering
/// pumps its weapons up on the beat of its chant and roars as it goes in. Standing at ease one now and then scratches its
/// head, folds its arms, rubs its neck, puts its hands on its hips or looks its weapon over. What is held for it — a
/// bowl, a rod and its line, a broom, a whetstone, the dice — is drawn in the hands.
/// </summary>
public partial class UnitView
{
    MultiMeshInstance3D _props;
    float[] _propBuf = Array.Empty<float>();
    /// <summary>The sim whose voices the view hears (a game begun or read back is another).</summary>
    Sim _heardSim;
    /// <summary>Who said something aloud lately, and when (heads turn to them).</summary>
    readonly List<(Unit who, double at)> _heard = new();
    /// <summary>Who said what kind of thing last, and when (the hands and the body go with it).</summary>
    readonly Dictionary<int, (double at, Voice v)> _said = new();

    static readonly Color BowlWood = new(.34f, .22f, .13f), Pottage = new(.74f, .6f, .36f), RodWood = new(.46f, .34f, .2f),
        Line = new(.86f, .86f, .8f), Bristle = new(.7f, .6f, .33f), Whet = new(.42f, .43f, .45f), DiceBone = new(.93f, .91f, .85f);

    /// <summary>Whether one is drawn lying down asleep (for the self-test).</summary>
    public bool Lying(int id) => _looks.TryGetValue(id, out var l) && l.Lie > .9f;
    /// <summary>The things drawn in hands in the last frame (for the self-test).</summary>
    public int PropsDrawn => _props?.Multimesh.VisibleInstanceCount ?? 0;

    void LifeInit()
    {
        var cube = new BoxMesh { Size = Vector3.One };
        _props = Multi(cube, new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .85f }, true);
    }

    /// <summary>Hears the sim's voices (again, if the sim is a new one).</summary>
    void HearLife(Sim sim)
    {
        if (sim == _heardSim) return;
        _heardSim = sim;
        _heard.Clear(); _said.Clear();
        sim.Spoke += (u, _, v) => { _heard.Add((u, sim.Time)); _said[u.Id] = (sim.Time, v); };
        sim.Shouted += (u, _) => _heard.Add((u, sim.Time));
    }

    /// <summary>How far down one asleep is (0 standing, 1 lying), eased so it lies down and gets up.</summary>
    float Lie(Unit u, Look look)
    {
        float want = u.Pastime == Pastime.Sleep && !u.Dead && u.State == UnitState.Idle ? 1 : 0;
        look.Lie += (want - look.Lie) * Math.Min(1, _dt * (want > look.Lie ? 1.6f : 2.5f));
        return look.Lie;
    }

    /// <summary>Whether a pastime puts what one holds aside (sleeping, eating, at the water, the broom, the beds, the sanctuary, the dice).</summary>
    static bool PutAside(Unit u) => u.State == UnitState.Idle
        && u.Pastime is Pastime.Sleep or Pastime.Eat or Pastime.Drink or Pastime.Fish or Pastime.Sweep or Pastime.Tend or Pastime.Kneel or Pastime.Dice;

    /// <summary>Whether a pastime sets the legs itself (seated, squatting, kneeling, lying): the feet are not put on the ground for it.</summary>
    static bool OwnLegs(Unit u) => u.State == UnitState.Idle
        && u.Pastime is Pastime.Sleep or Pastime.Dice or Pastime.Tend or Pastime.Kneel or Pastime.Eat or Pastime.Sharpen;

    /// <summary>
    /// The pose of the town's own pastimes, over what the standing pose has set: the seat or the squat or the knees, the
    /// arms at the bowl, the dice, the whetstone, the rod, the broom, the earth, the hands together; in a ring, the speaker's
    /// hands and the listeners'.
    /// </summary>
    void LifePose(Unit u, Figure f, Look look, float seed, ref float main, ref float off, ref float legR, ref float legL, ref float lean, ref float bob,
        ref float elbowR, ref float elbowL, ref float kneeR, ref float kneeL, ref float hipYaw)
    {
        float t = (float)_t;
        bool seated = u.SeatH > 0 && u.Pastime != Pastime.Sleep;
        switch (u.Pastime)
        {
            case Pastime.Sleep:
                // on its back, arms at its sides, the legs out straight
                bob = u.SeatH * Smooth(look.Lie);
                main = off = .12f; legR = legL = 0; kneeR = kneeL = .05f; elbowR = elbowL = -.15f; lean = 0; hipYaw = 0;
                break;
            case Pastime.Eat:
            case Pastime.Sharpen:
                if (seated) { bob = u.SeatH + .25f - (f.Thigh + f.Shin); legR = -1.5f; legL = -1.42f; kneeR = 1.5f; kneeL = 1.38f; }
                else { bob = -(f.Thigh + f.Shin) * .78f; legR = -1.25f; legL = -1.15f; kneeR = .9f; kneeL = 1.05f; }
                lean = .12f;
                if (u.Pastime == Pastime.Eat)
                {
                    // the bowl held before the chest; the spoon to the mouth and back
                    off = -.85f; elbowL = -1.15f;
                    float k = (t * .45f + seed) % 1;
                    float up = k < .25f ? Smooth(k / .25f) : k < .45f ? 1 : k < .65f ? 1 - Smooth((k - .45f) / .2f) : 0;
                    main = Mathf.Lerp(-.75f, -.35f, up); elbowR = Mathf.Lerp(-1.2f, -2.35f, up);
                }
                else
                {
                    // the blade across the knees in the right hand, the whetstone going along it in the left
                    main = -.62f; elbowR = -.95f;
                    off = -.75f + .22f * MathF.Sin(t * 4.2f + seed); elbowL = -.85f;
                    lean = .22f;
                }
                break;
            case Pastime.Drink:
                // the water in both hands, up to the mouth
                main = off = -.45f; elbowR = elbowL = -2.15f; lean = -.04f;
                break;
            case Pastime.Dice:
            {
                // squatting round the middle, a hand on a knee; the thrower shakes the dice in its fist and throws them in
                bob = -(f.Thigh + f.Shin) * .5f; legR = legL = -1.15f; kneeR = kneeL = 2.1f; lean = .32f; hipYaw = 0;
                off = -.62f; elbowL = -.45f;
                var g = u.Group;
                float since = g != null ? (float)(_m.Sim.Time - g.TurnAt) : 9;
                if (g?.Speaker == u && since < 1.1f)
                {
                    float k = since / 1.1f;
                    main = k < .7f ? -.95f + .22f * MathF.Sin(k * 40) : Mathf.Lerp(-1.45f, -.8f, (k - .7f) / .3f);
                    elbowR = k < .7f ? -1.3f : Mathf.Lerp(-.3f, -.6f, (k - .7f) / .3f);
                }
                else { main = -.55f; elbowR = -.5f; }
                break;
            }
            case Pastime.Fish:
            {
                // both hands on the rod out before it; now and then a tug at the line
                float tug = Hash01(u.Id, (int)(t / 4)) < .3 ? MathF.Max(0, MathF.Sin((t % 4) * 3)) * .3f : 0;
                main = -.95f - tug; elbowR = -.45f; off = -.85f - tug * .7f; elbowL = -.65f; lean = .04f;
                break;
            }
            case Pastime.Sweep:
            {
                // the broom low before it in both hands, to and fro
                float sw = MathF.Sin(t * 2.6f + seed);
                main = -.42f + .3f * sw; off = main + .12f; elbowR = -.5f; elbowL = -.95f; lean = .2f; hipYaw = .12f * sw;
                kneeR = kneeL = .15f;
                break;
            }
            case Pastime.Tend:
            {
                // on its knees at the bed's edge, leaning over it, the hands in the earth
                float w = MathF.Sin(t * 1.8f + seed);
                bob = -f.Shin * .92f; legR = legL = -.25f; kneeR = kneeL = 1.7f; lean = .55f;
                main = -.95f + .15f * w; off = -.9f - .15f * w; elbowR = elbowL = -.3f;
                break;
            }
            case Pastime.Kneel:
                // upright on both knees, the hands together before the chest
                bob = -f.Shin; legR = legL = 0; kneeR = kneeL = 1.57f; lean = .05f;
                main = off = -.5f; elbowR = elbowL = -1.75f;
                break;
            case Pastime.Talk when u.Group is { } ring && ring.Speaker != u:
                // listening: the arms hanging, or folded (one in two)
                if (Hash01(u.Id, 3) < .5) { main = off = -.62f; elbowR = elbowL = -1.85f; }
                else { main = -.08f; off = -.05f; elbowR = elbowL = -.25f; }
                break;
        }
    }

    /// <summary>
    /// Standing at ease with nothing to do, now and then for a couple of seconds: a scratch of the head, the arms folded,
    /// the neck rubbed, the hands on the hips, the weapon looked over, the brow wiped.
    /// </summary>
    void Fidget(Unit u, Figure f, Look look, float seed, ref float main, ref float off, ref float elbowR, ref float elbowL, ref float lean)
    {
        if (u.State != UnitState.Idle || u.Pastime is not (Pastime.None or Pastime.Look) || look.Move > .05f) return;
        double now = _m.Sim.Time;
        const double Every = 9;
        int slot = (int)(now / Every);
        double r = Hash01(u.Id * 7 + 1, slot);
        // one slot in two passes with nothing
        if (r > .55) return;
        double from = slot * Every + Hash01(u.Id, slot * 3) * (Every - 3);
        float k = (float)((now - from) / 2.6);
        if (k < 0 || k > 1) return;
        float w = Smooth(Math.Clamp(k / .15f, 0, 1)) * Smooth(Math.Clamp((1 - k) / .15f, 0, 1));
        if (w <= 0) return;
        Settle(f, ref elbowR, ref elbowL);
        int kind = (int)(Hash01(u.Id + 11, slot) * 6);
        float t = (float)_t;
        switch (kind)
        {
            case 0: main = Mathf.Lerp(main, -2.45f + .08f * MathF.Sin(t * 16), w); elbowR = Mathf.Lerp(elbowR, -2.2f, w); break;
            case 1: main = Mathf.Lerp(main, -.62f, w); off = Mathf.Lerp(off, -.62f, w); elbowR = Mathf.Lerp(elbowR, -1.85f, w); elbowL = Mathf.Lerp(elbowL, -1.85f, w); break;
            case 2: off = Mathf.Lerp(off, -2.55f, w); elbowL = Mathf.Lerp(elbowL, -2.4f, w); break;
            case 3: main = Mathf.Lerp(main, .32f, w); off = Mathf.Lerp(off, .32f, w); elbowR = Mathf.Lerp(elbowR, -1.25f, w); elbowL = Mathf.Lerp(elbowL, -1.25f, w); break;
            case 4 when u.T.Work < 1: main = Mathf.Lerp(main, -1.15f, w); elbowR = Mathf.Lerp(elbowR, -1.35f, w); lean = Mathf.Lerp(lean, .08f, w); break;
            default: main = Mathf.Lerp(main, -2.1f + .25f * MathF.Sin(k * 6), w); elbowR = Mathf.Lerp(elbowR, -2.3f, w); break;
        }
    }

    /// <summary>
    /// The hands and the body with what was said or done just now: a hand raised in greeting (a worker waves), the hands
    /// going as one talks, the weapon up for a cheer, arms out for a roar, a laugh shaking the shoulders; and a band of the
    /// horde gathering pumps its weapons up on the beat of its chant.
    /// </summary>
    void Gesture(Unit u, Figure f, Look look, float seed, ref float main, ref float off, ref float elbowR, ref float elbowL, ref float lean, ref float bob)
    {
        if (!f.Jointed) return;
        if (u.Dead || u.State is not (UnitState.Idle or UnitState.Walk) || u.Pastime is Pastime.Sleep or Pastime.Sit or Pastime.Dice or Pastime.Kneel or Pastime.Tend) return;
        double now = _m.Sim.Time;
        float t = (float)_t;
        // the horde gathering: the chant's beat
        if (u.Team == Team.Horde && u.Band is { State: BandState.Forming } && look.Move < .2f)
        {
            float beat = (float)(now % 1.5 / 1.5), pump = beat < .25f ? Smooth(beat / .25f) : 1 - Smooth(Math.Clamp((beat - .25f) / .4f, 0, 1));
            Settle(f, ref elbowR, ref elbowL);
            main = Mathf.Lerp(main, -2.7f, pump); elbowR = Mathf.Lerp(elbowR, -.4f, pump);
            bob -= .25f * (beat < .12f ? 1 - beat / .12f : 0);
            return;
        }
        if (_said.TryGetValue(u.Id, out var s) && now - s.at < 1.4)
        {
            float k = (float)((now - s.at) / 1.4), w = Smooth(Math.Clamp(k / .12f, 0, 1)) * Smooth(Math.Clamp((1 - k) / .2f, 0, 1));
            Settle(f, ref elbowR, ref elbowL);
            switch (s.v)
            {
                case Voice.Cheer:
                case Voice.Praise:
                    main = Mathf.Lerp(main, -3f + .15f * MathF.Sin(t * 12), w); elbowR = Mathf.Lerp(elbowR, -.3f, w); lean = Mathf.Lerp(lean, -.06f, w);
                    return;
                case Voice.Roar:
                    main = Mathf.Lerp(main, -1.6f, w); off = Mathf.Lerp(off, -1.5f, w); elbowR = Mathf.Lerp(elbowR, -.4f, w); elbowL = Mathf.Lerp(elbowL, -.4f, w); lean = Mathf.Lerp(lean, .22f, w);
                    return;
                case Voice.Laugh:
                    lean = Mathf.Lerp(lean, -.1f, w); bob += .25f * MathF.Abs(MathF.Sin(t * 17)) * w;
                    return;
                case Voice.Chant:
                    main = Mathf.Lerp(main, -2.7f, w); elbowR = Mathf.Lerp(elbowR, -.4f, w);
                    return;
                default:
                    if (u.State == UnitState.Idle && u.Pastime != Pastime.Talk)
                    {
                        float g = .5f + .5f * MathF.Sin(t * 6 + seed);
                        main = Mathf.Lerp(main, -.5f - .7f * g, w); elbowR = Mathf.Lerp(elbowR, -1.1f, w);
                    }
                    break;
            }
        }
        // a greeting: a worker waves, a soldier raises its hand (the nod is the head's: Gaze)
        if (now - u.GreetAt < 1.3)
        {
            float k = (float)((now - u.GreetAt) / 1.3), w = Smooth(Math.Clamp(k / .15f, 0, 1)) * Smooth(Math.Clamp((1 - k) / .25f, 0, 1));
            Settle(f, ref elbowR, ref elbowL);
            if (u.T.Work >= 1) { main = Mathf.Lerp(main, -2.55f + .3f * MathF.Sin(t * 13), w); elbowR = Mathf.Lerp(elbowR, -.75f, w); }
            else { main = Mathf.Lerp(main, -2.1f, w); elbowR = Mathf.Lerp(elbowR, -1.05f, w); }
        }
    }

    /// <summary>The elbows as the pose leaves them when it sets none (hanging easy; a shield's arm bent to it), so a gesture can blend from them.</summary>
    static void Settle(Figure f, ref float elbowR, ref float elbowL)
    {
        if (float.IsNaN(elbowR)) elbowR = -.2f;
        if (float.IsNaN(elbowL)) elbowL = f.Shield ? -.9f : -.2f;
    }

    /// <summary>
    /// Where the head turns for the town's life, over the idle looking about: to the one it greets (with a nod), to whoever
    /// called out close by, down at the hands at the bowl, the whetstone, the earth, in prayer; up with a drink. False if
    /// none of it.
    /// </summary>
    bool LifeGaze(Unit u, Look look, out float tx, out float ty, out float tz, out float pitch)
    {
        tx = ty = tz = 0; pitch = float.NaN;
        double now = _m.Sim.Time;
        if (u.State == UnitState.Idle)
            switch (u.Pastime)
            {
                case Pastime.Kneel: pitch = .5f; return false;
                case Pastime.Eat or Pastime.Sharpen or Pastime.Tend: pitch = .42f; return false;
                case Pastime.Drink: pitch = -.35f; return false;
                case Pastime.Fish: pitch = .25f; return false;
                case Pastime.Sleep: pitch = 0; return false;
            }
        if (u.GreetTo is { Dead: false } g && now - u.GreetAt < 1.6)
        {
            tx = g.X; ty = g.Y + g.T.BodyH * .85f; tz = g.Z;
            // the nod, as the hand goes up
            if (now - u.GreetAt is > .2 and < .7) pitch = .3f;
            return true;
        }
        // whoever called out within 12 m, the last of them
        for (int i = _heard.Count - 1; i >= 0; i--)
        {
            var (who, at) = _heard[i];
            if (now - at > 2.5) break;
            if (who == u || who.Dead) continue;
            float dx = who.X - u.X, dz = who.Z - u.Z;
            if (dx * dx + dz * dz > 96 * 96) continue;
            tx = who.X; ty = who.Y + who.T.BodyH * .85f; tz = who.Z;
            return true;
        }
        return false;
    }

    /// <summary>The words heard more than a few seconds ago forgotten.</summary>
    void ForgetHeard()
    {
        double now = _m.Sim.Time;
        int old = 0;
        while (old < _heard.Count && now - _heard[old].at > 3) old++;
        if (old > 0) _heard.RemoveRange(0, old);
    }

    /// <summary>Where a figure's hands are this frame (from the bones posed just now), for what is drawn in them.</summary>
    void Hands(Figure f, Look look)
    {
        if (!f.Jointed) return;
        look.HandR = _xf[(int)Bone.LowerR] * new Transform3D(Basis.Identity, f.Pivot[(int)Bone.LowerR] - new Vector3(0, f.Forearm, 0));
        look.HandL = _xf[(int)Bone.LowerL] * new Transform3D(Basis.Identity, f.Pivot[(int)Bone.LowerL] - new Vector3(0, f.Forearm, 0));
        look.HandsAt = _t;
    }

    /// <summary>What the town's pastimes hold, drawn in the hands: a bowl, a rod and its line, a broom, a whetstone; and the dice in a ring's middle.</summary>
    void DrawProps(Sim sim)
    {
        var mm = _props.Multimesh;
        int n = 0;
        foreach (var u in sim.Units)
            if (!u.Dead && !u.Inside && u.State == UnitState.Idle && u.Pastime is Pastime.Eat or Pastime.Fish or Pastime.Sweep or Pastime.Sharpen or Pastime.Dice) n += 3;
        if (n == 0) { mm.VisibleInstanceCount = 0; return; }
        if (mm.InstanceCount < n) { mm.InstanceCount = Math.Max(24, n * 2); _propBuf = new float[mm.InstanceCount * 16]; }
        int i = 0;
        var thrown = new HashSet<Huddle>();
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.Inside || u.State != UnitState.Idle || !_looks.TryGetValue(u.Id, out var look) || look.HandsAt != _t) continue;
            var r = look.HandR; var l = look.HandL;
            switch (u.Pastime)
            {
                case Pastime.Eat:
                    // a wooden bowl in the left hand, held level, the pottage in it
                    var bowl = new Transform3D(new Basis(Vector3.Up, MathF.PI / 2 - look.Face).Scaled(Vector3.One * VoxelWorld.Vox), l.Origin);
                    Prop(ref i, bowl, new Vector3(0, .2f, .6f), new Vector3(2.2f, .9f, 2.2f), BowlWood);
                    Prop(ref i, bowl, new Vector3(0, .55f, .6f), new Vector3(1.8f, .3f, 1.8f), Pottage);
                    break;
                case Pastime.Fish:
                {
                    // the rod out from the hands over the water, rising; the line straight down from its tip
                    var face = new Basis(Vector3.Up, MathF.PI / 2 - look.Face);
                    var dir = (face * new Vector3(0, .55f, 1)).Normalized();
                    var root = r.Origin;
                    float len = 20 * VoxelWorld.Vox;
                    var tip = root + dir * len;
                    Rod(ref i, root, tip, .28f * VoxelWorld.Vox, RodWood);
                    float drop = MathF.Max(.3f, tip.Y - _w.WY(u.Y - 3));
                    Rod(ref i, tip, tip - new Vector3(0, drop, 0), .07f * VoxelWorld.Vox, Line);
                    break;
                }
                case Pastime.Sweep:
                {
                    // the broomstick down from the hands to the ground ahead, the bristles at its foot
                    var face = new Basis(Vector3.Up, MathF.PI / 2 - look.Face);
                    var foot = new Vector3(r.Origin.X, _w.WY(u.Y + .6f), r.Origin.Z) + face * new Vector3(0, 0, 5 * VoxelWorld.Vox);
                    var top = (r.Origin + l.Origin) / 2 + Vector3.Up * 2 * VoxelWorld.Vox;
                    Rod(ref i, top, foot, .3f * VoxelWorld.Vox, RodWood);
                    Prop(ref i, new Transform3D(new Basis(Vector3.Up, MathF.PI / 2 - look.Face).Scaled(Vector3.One * VoxelWorld.Vox), foot), Vector3.Zero, new Vector3(3f, .9f, 1.2f), Bristle);
                    break;
                }
                case Pastime.Sharpen:
                    // the whetstone in the left hand
                    Prop(ref i, new Transform3D(new Basis(Vector3.Up, MathF.PI / 2 - look.Face).Scaled(Vector3.One * VoxelWorld.Vox), l.Origin), new Vector3(0, -.3f, .2f), new Vector3(.6f, .5f, 1.7f), Whet);
                    break;
                case Pastime.Dice when u.Group is { } g && thrown.Add(g):
                {
                    // two dice in the middle of the ring, where they fell
                    float gy = _w.WY(u.Y + .25f);
                    double seed = Hash01(g.Members.Count > 0 ? g.Members[0].Id : 0, (int)(g.TurnAt * 10));
                    for (int d = 0; d < 2; d++)
                    {
                        var at = new Vector3(_w.WX(g.X + (d == 0 ? -.6f : .7f)), gy, _w.WZ(g.Z + (d == 0 ? .3f : -.4f)));
                        var turn = new Basis(Vector3.Up, (float)(seed * 6 + d * 1.3)).Scaled(Vector3.One * VoxelWorld.Vox);
                        Prop(ref i, new Transform3D(turn, at), Vector3.Zero, new Vector3(.55f, .55f, .55f), DiceBone);
                    }
                    break;
                }
            }
        }
        RenderingServer.MultimeshSetBuffer(mm.GetRid(), _propBuf);
        mm.VisibleInstanceCount = i;
    }

    void Prop(ref int i, Transform3D parent, Vector3 at, Vector3 size, Color c)
    {
        if ((i + 1) * 16 > _propBuf.Length) return;
        var t = parent * new Transform3D(Basis.Identity.Scaled(size), at);
        int o = i++ * 16;
        var buf = _propBuf;
        buf[o] = t.Basis.X.X; buf[o + 1] = t.Basis.Y.X; buf[o + 2] = t.Basis.Z.X; buf[o + 3] = t.Origin.X;
        buf[o + 4] = t.Basis.X.Y; buf[o + 5] = t.Basis.Y.Y; buf[o + 6] = t.Basis.Z.Y; buf[o + 7] = t.Origin.Y;
        buf[o + 8] = t.Basis.X.Z; buf[o + 9] = t.Basis.Y.Z; buf[o + 10] = t.Basis.Z.Z; buf[o + 11] = t.Origin.Z;
        buf[o + 12] = c.R; buf[o + 13] = c.G; buf[o + 14] = c.B; buf[o + 15] = 1;
    }

    /// <summary>A thin rod from one point to another (world units), as thick as given.</summary>
    void Rod(ref int i, Vector3 a, Vector3 b, float thick, Color c)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return;
        var z = d / len;
        var x = MathF.Abs(z.Y) < .95f ? Vector3.Up.Cross(z).Normalized() : Vector3.Right;
        var y = z.Cross(x);
        var basis = new Basis(x * thick, y * thick, z * len);
        Prop(ref i, new Transform3D(basis, (a + b) / 2), Vector3.Zero, Vector3.One, c);
    }
}
