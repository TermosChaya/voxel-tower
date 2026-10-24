using System;

namespace VoxelTower.Core;

/// <summary>How the one a blow fell on got out of its way, or took it on something hard.</summary>
public enum Guarded : byte { None, StepBack, Dodge, Duck, Block, Parry }

/// <summary>
/// What a wound does to the one who takes it, beyond the voxels it cuts: it hurts. Pain gathers from every wound — a
/// blow to the head most of all, bleeding all the while — and ebbs slowly; it slows the arm and the legs, and when it is
/// too much a raider breaks and runs, and one in agony goes down and lies there, getting up only as it eases, if it
/// does. A hard blow knocks one back a step and leaves it reeling a moment, no blow of its own the while; a blow to the
/// head dazes; the hand goes to the wound. A goblin feels much, an orc less, a troll little, the dead nothing — so the
/// horde comes many, not as a few that cannot be stopped.
/// A blow seen coming may be got out of the way of — a step back, a lean aside, ducking under a sweep — or taken on a
/// shield or turned by a blade: the more as the one it falls on is quick, sees it and is not hurt, stunned or down.
/// Between blows fighters do not stand still: they circle one another, and step back after a blow of their own.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The share of the blows it sees coming a kind gets out of the way of or turns, at its best.</summary>
    public static float Agility(UnitType t) => t.Key switch
    {
        "sword" => .32f, "knight" => .34f, "halberd" => .22f, "spear" => .24f, "shield" => .2f, "worker" => .08f, "archer" => .13f,
        "sling" => .14f, "crossbow" => .1f, "bombard" => .08f, "priest" => .06f, "goblin" => .2f, "sapper" => .14f, "miner" => .1f,
        "orc" => .14f, "thief" => .3f, "troll" => .03f, "shaman" => .1f, "witch" => .14f, "necro" => .1f, "alchemist" => .12f, "hound" => .18f,
        "giant" => .02f, "tametroll" => .03f, "boar" => .05f, "bear" => .06f, _ => 0f,
    };

    /// <summary>How much a wound hurts a kind, for the share of it its health it cost: the dead and the engines feel nothing.</summary>
    public static float Sense(UnitType t) => Kinds.IsEngine(t) ? 0 : t.Key switch
    {
        "risen" or "thrall" => 0, "troll" or "tametroll" or "giant" => .55f, "orc" => 1f, "goblin" or "thief" or "alchemist" => 1.45f, "bear" or "boar" => .7f, _ => 1.25f,
    };

    /// <summary>Pain past which a raider breaks and runs (a leader holds on longer); past which one goes down.</summary>
    public const float BreakPain = .62f, LeaderBreak = .95f, DownPain = 1.1f;
    /// <summary>Pain past which one of ours who is no fighter, caught, hits back no more.</summary>
    public const float Cowed = .12f;

    /// <summary>A draw of its own for the guard and the footwork, so that the valley's other chances run as they did.</summary>
    float Draw(Unit a, Unit b, int salt) => (float)Hash.H3(a.Id * 31 + salt, b?.Id ?? 0, (int)(Time * 20));

    /// <summary>Hurt it: the pain of it, and what a hard blow does — knocked back a step, reeling, dazed, the hand to the wound, down.</summary>
    void Pained(Unit b, Unit by, float dmg)
    {
        float sense = Sense(b.T);
        if (sense <= 0 || b.Dead) { b.HitHead = false; return; }
        float share = dmg / Math.Max(1, b.MaxHp);
        bool head = b.HitHead;
        b.HitHead = false;
        b.Pain = Math.Min(1.6f, b.Pain + share * sense * (head ? 1.8f : 1));
        // a blow to the head dazes: the arm drops a moment, and it sees nothing coming the while
        if (head && share > .05f) { b.DazedUntil = Math.Max(b.DazedUntil, Time + 1.5 + share * 8); b.Cooldown = Math.Max(b.Cooldown, .5f + share * 5); }
        if (b.HitPart != BodyPart.Body || head || share > .08f) { b.Clutch = head ? (byte)9 : (byte)b.HitPart; b.ClutchUntil = Time + 2.5 + share * 6; }
        // a hard blow (of one who struck it, not fire or a fall): back a step, reeling a moment, nothing struck back the while
        if (by != null && share >= .1f && b.T.BodyH <= by.T.BodyH * 1.4f)
        {
            float reel = .25f + Math.Min(1, share * 2.2f) * .9f;
            b.StaggerUntil = Math.Max(b.StaggerUntil, Time + reel);
            b.Cooldown = Math.Max(b.Cooldown, reel);
            float dx = b.X - by.X, dz = b.Z - by.Z, d = MathF.Max(.01f, MathF.Sqrt(dx * dx + dz * dz));
            float push = (2 + share * 10) / reel;
            b.StagX = dx / d * push; b.StagZ = dz / d * push;
            // a blow that would fell an ox: off its feet a while
            if (share >= .35f && !b.Down) GoDown(b, 1.2 + share * 2);
        }
        if (!b.Down && b.Pain >= DownPain && !b.Leader) GoDown(b, double.PositiveInfinity);
    }

    /// <summary>Off its feet: for a while, or (forever) till the pain eases enough to get up.</summary>
    void GoDown(Unit u, double seconds)
    {
        if (u.Dead || u.Airborne != null || Kinds.IsEngine(u.T) || u.T.Key is "risen" or "thrall") return;
        u.Down = true;
        u.DownSince = Time;
        u.DownUntil = double.IsInfinity(seconds) ? double.PositiveInfinity : Time + seconds;
        u.Path = null;
        u.State = UnitState.Idle;
        StopCarve(u); StopBuild(u); ReleaseSite(u);
        if (u == Possessed) Unpossess($"{u.Name} сбит с ног");
        // (one pinned, or knocked senseless, is told of as that: Sim.Trauma.cs)
        if (u.Team == Team.Player && u.PinKg <= 0 && Time >= u.DazedUntil && seconds > 6 && Time - _downSaid > 6) { _downSaid = Time; Say($"{u.Name} упал и не может подняться.", 3, u); }
    }

    double _downSaid = -99;

    /// <summary>The pain ebbing, bleeding keeping it up; one knocked back sliding back; one down getting up when it can.</summary>
    void TickPain(Unit u, float dt)
    {
        if (u.Pain > 0)
        {
            float bleed = u.Wounds?.Bleed ?? 0;
            u.Pain = Math.Max(0, u.Pain - dt * (.018f - Math.Min(.016f, bleed * .0025f)));
        }
        if (Time < u.StaggerUntil && (u.StagX != 0 || u.StagZ != 0))
        {
            // knocked back within its cell, the push dying away
            var (fx, _, fz) = Nav.Feet(u.Cell);
            u.X = Math.Clamp(u.X + u.StagX * dt, fx - 3.5f, fx + 3.5f);
            u.Z = Math.Clamp(u.Z + u.StagZ * dt, fz - 3.5f, fz + 3.5f);
            u.StagX *= MathF.Exp(-dt * 6); u.StagZ *= MathF.Exp(-dt * 6);
        }
        if (u.Down && !Grounded(u) && (Time >= u.DownUntil || double.IsInfinity(u.DownUntil) && u.Pain < BreakPain * .8f))
        {
            u.Down = false;
            u.NextSearch = 0;
        }
    }

    /// <summary>Lying where it fell: breathing, nothing more; a raider broken by it runs when it gets up (not a leader, nor one guarding its camp).</summary>
    void DownTick(Unit u, float dt)
    {
        u.State = UnitState.Idle;
        u.Path = null;
        u.Cooldown = Math.Max(u.Cooldown, .6f);
        if (u.Team == Team.Horde && !u.Leader && !u.Guard) u.Routed = true;
    }

    /// <summary>
    /// A blow coming at <paramref name="b"/>: whether it sees it and gets out of its way — a step back from a thrust or a
    /// blow from overhead, a lean aside from a slanting one, ducking under a sweep — or takes it on its shield, or turns
    /// it with its blade. Never one hurt past bearing, reeling, dazed, down, bound, or set on by one it does not face.
    /// </summary>
    Guarded Defend(Unit a, Unit b, StrokeKind k)
    {
        if (b.Dead || b.Down || b.Bound || b.Airborne != null || b == Possessed || Kinds.IsEngine(b.T) || Time < b.StaggerUntil || Time < b.DazedUntil) return Guarded.None;
        float quick = Agility(b.T);
        if (quick <= 0) return Guarded.None;
        // it has to see it coming: facing the striker, fighting
        float to = MathF.Atan2(a.Z - b.Z, a.X - b.X), off = MathF.Abs(Wrap(to - b.Facing));
        if (off > 1.9f || b.State != UnitState.Fight && b.Target != a) return Guarded.None;
        // hurt, it is slower; a quick striker is harder to see coming; just after its own blow it is open
        float chance = quick * (1 - Math.Min(1, b.Pain)) * (1 - Agility(a.T) * .8f) * (b.Cooldown > b.T.AttackTime * .75f ? .5f : 1);
        bool shield = Bodies.Plan(b.T, b.Look).Shield && HasOffHand(b);
        if (shield) chance += .25f * (1 - Math.Min(1, b.Pain));
        float roll = Draw(a, b, 1);
        if (roll >= chance) return Guarded.None;
        if (shield && roll < chance * .7f) return Guarded.Block;
        bool blade = WeaponOf(b.T).cut is Cut.Slash or Cut.Chop && HasMainHand(b) && Agility(b.T) >= .2f;
        if (blade && Draw(a, b, 2) < .35f) return Guarded.Parry;
        return k switch
        {
            StrokeKind.Sweep or StrokeKind.Smash => Guarded.Duck,
            StrokeKind.DiagL or StrokeKind.DiagR => Guarded.Dodge,
            _ => Guarded.StepBack,
        };
    }

    static float Wrap(float a) { while (a > MathF.PI) a -= MathF.Tau; while (a < -MathF.PI) a += MathF.Tau; return a; }

    /// <summary>The blow went where the body was no more: the blade through the air, the one it meant stepping, leaning or ducking out of its way.</summary>
    internal void Missed(Unit a, Unit b, StrokeKind k, Guarded how)
    {
        a.Swing = 1;
        LayStroke(a, b, k, .5f, .5f, out var at, out var dir, out _, out _);
        b.React = how; b.ReactAt = Time; b.ReactSide = (sbyte)(Draw(a, b, 3) < .5f ? -1 : 1);
        if (how == Guarded.StepBack)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z, d = MathF.Max(.01f, MathF.Sqrt(dx * dx + dz * dz));
            b.StagX = dx / d * 7; b.StagZ = dz / d * 7;
            b.StaggerUntil = Math.Max(b.StaggerUntil, Time + .3);
        }
        a.Glanced = false;
        Smote?.Invoke(new Smiting(a, b, at, dir, Mat.Air, 0, k, false));
        a.Strokes++;
        a.NextStroke = Choose(a, b);
        a.StrokeFor = b;
    }

    /// <summary>The blow taken on a shield or turned by a blade: a jar, the striker's arm thrown back a moment, no wound.</summary>
    internal void Turned(Unit a, Unit b, StrokeKind k, Guarded how)
    {
        a.Swing = 1;
        LayStroke(a, b, k, .5f, .5f, out var at, out var dir, out _, out _);
        b.React = how; b.ReactAt = Time; b.ReactSide = 1;
        a.Glanced = true;
        a.StaggerUntil = Math.Max(a.StaggerUntil, Time + .2);
        a.Cooldown = Math.Max(a.Cooldown, .3f);
        Smote?.Invoke(new Smiting(a, b, at, dir, how == Guarded.Block ? Mat.Board : Mat.Steel, 0, k, true));
        a.Strokes++;
        a.NextStroke = Choose(a, b);
        a.StrokeFor = b;
    }

    /// <summary>
    /// Fighting in reach: no standing still between blows. Each circles the other, now one way, now the other; after
    /// its own blow it gives back a step. All within the ground it stands on (its cell), so the ways it walks keep.
    /// </summary>
    void Footwork(Unit u, Unit foe, float dt)
    {
        if (u.Down || Time < u.StaggerUntil || u.Bound || Kinds.IsEngine(u.T) || u.T.Move.Height > 3 && u.T.Key is "troll" or "giant" or "tametroll") return;
        if (Time >= u.CircleNext)
        {
            u.CircleNext = Time + 1.2 + Draw(u, foe, 4) * 2.2;
            float r = Draw(u, foe, 5);
            u.Circle = r < .38f ? -1 : r < .76f ? 1 : 0;
        }
        float dx = foe.X - u.X, dz = foe.Z - u.Z, d = MathF.Max(.01f, MathF.Sqrt(dx * dx + dz * dz));
        float nx = dx / d, nz = dz / d;
        // round, and back off a little just after a blow of its own (its arm still coming round)
        float round = u.Circle * .9f * VoxelWorld.PerMetre * .3f, back = u.Cooldown > u.T.AttackTime * .7f ? -.5f * VoxelWorld.PerMetre * .4f : 0;
        // too close: give ground; a little far: close in — and never backing out of its own reach
        float reach = ReachOf(u) + foe.T.BodyR;
        if (d < reach * .55f) back -= .3f * VoxelWorld.PerMetre * .4f;
        if (back < 0 && d > reach * .75f) back = 0;
        if (d > reach * .85f) back = .5f * VoxelWorld.PerMetre * .4f;
        var (fx, _, fz) = Nav.Feet(u.Cell);
        u.X = Math.Clamp(u.X + (-nz * round + nx * back) * dt, fx - 3.5f, fx + 3.5f);
        u.Z = Math.Clamp(u.Z + (nx * round + nz * back) * dt, fz - 3.5f, fz + 3.5f);
    }

    /// <summary>How much the pain slows it: its arm between blows, its legs (1 not at all).</summary>
    public static float PainPace(Unit u) => 1 / (1 + .9f * Math.Min(1.2f, u.Pain));
}
