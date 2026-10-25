using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// What the hands of one gone into do, beyond the blow, the shot and the pick (Sim.Possess.cs). A soldier guards with the
/// right button held — a shield takes nearly every blow it faces, a blade turns some — and, the left button held after a
/// blow, draws back for a heavy one that lands at the release, harder and knocking the foe back; a kick shoves one of the
/// horde back staggering, or tears a siege ladder off the wall before it. A worker lays and digs what the player marked
/// out where it looks, the marked voxels nearest first, and puts a ladder up against the wall it looks at (marked, then
/// laid by its own hands). Running, guarding, the heavy blow and the kick take its wind, which comes back at a walk and
/// faster standing; winded, it cannot run.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The right button held to guard (a soldier); a kick asked for.</summary>
    public bool WishGuard, WishKick;
    /// <summary>Its wind, 0 to 1; whether it is guarding now; how far a heavy blow is drawn back, 0 to 1.</summary>
    public float Stamina = 1, Charge;
    public bool Guarding { get; private set; }
    /// <summary>When it last kicked, and why the hands last could not do what was asked (for the words over the eyes).</summary>
    public double KickedAt = -99, RefusedAt = -99;
    public string Refused = "";
    /// <summary>Blows the one gone into took on its guard (the bench counts them).</summary>
    public int GuardedBlows;
    /// <summary>Laying or digging what was marked out, this tick (for the mark in the sight).</summary>
    public bool OnMarked { get; private set; }
    /// <summary>How long a heavy blow takes to draw back, seconds.</summary>
    public const float HeavyDraw = .8f;

    bool _struckHeld, _winded, _heavyNext, _handHeavy, _layingMarked;
    double _kickReady;
    /// <summary>How hard the blow of the one gone into lands: more for a blow held back (<see cref="Heartened"/>).</summary>
    float PossessedArm = 1;

    void Refuse(string why)
    {
        if (Refused == why && Time - RefusedAt < 1) return;
        Refused = why; RefusedAt = Time;
    }

    /// <summary>A soldier who strikes hand to hand (rather than shoot or work): it guards with the right button and draws back for a heavy blow.</summary>
    public static bool Fights(Unit u) => !u.T.Ranged && u.T.Work < 1 && !Kinds.IsEngine(u.T);

    /// <summary>Whether one gone into may run now: not winded.</summary>
    bool CanRun => !_winded;

    /// <summary>The hands of one gone into, each tick, and its wind.</summary>
    void Hands(Unit u, float dt, bool moving, bool run)
    {
        OnMarked = false;
        bool guard = WishGuard && Fights(u) && Armed(u);
        Guarding = guard;
        float use = (run && moving ? .14f : 0) + (guard ? .04f : 0);
        Stamina = use > 0 ? Math.Max(0, Stamina - use * dt) : Math.Min(1, Stamina + (moving ? .12f : .22f) * dt);
        if (Stamina <= 0) _winded = true; else if (Stamina > .3f) _winded = false;
        if (WishKick) { WishKick = false; Kick(u); }
        if (Bow(u.T)) { Draw(u, dt); return; }
        if (Fights(u))
        {
            if (guard) { Charge = 0; _struckHeld = WishStrike; return; }
            // the click strikes at once; held, the arm goes back for a heavy blow, which lands when it is let go
            if (WishStrike && !_struckHeld) { _struckHeld = true; Charge = 0; if (u.Cooldown <= 0) Blow(u); }
            else if (WishStrike) Charge = Math.Min(1, Charge + dt / HeavyDraw);
            else
            {
                if (_struckHeld && Charge >= 1)
                {
                    if (Stamina >= .15f)
                    {
                        _heavyNext = true; u.Cooldown = 0;
                        Blow(u);
                        u.Cooldown *= 1.4f;
                        Stamina = Math.Max(0, Stamina - .2f);
                    }
                    else Refuse("нет сил на тяжёлый удар");
                }
                _struckHeld = false; Charge = 0;
            }
            return;
        }
        if (WishStrike && u.Cooldown <= 0) Blow(u);
        else if (WishStrike && u.T.Work > 0 && !u.T.Ranged) Pick(u, dt);
        if (WishLay && u.T.Work > 0) Trowel(u, dt);
        // a stroke on the marking lays only what is marked, till the button is let go
        else _layingMarked = false;
    }

    /// <summary>
    /// How the one gone into takes a blow while the player guards it: facing it, a shield takes nearly every one, a blade
    /// turns some; from the side or behind, none. Each blow taken costs a little wind; winded, it cannot guard.
    /// </summary>
    Guarded PossessedGuard(Unit a, Unit b)
    {
        if (!Guarding || Stamina <= 0) return Guarded.None;
        float to = MathF.Atan2(a.Z - b.Z, a.X - b.X), off = MathF.Abs(Wrap(to - b.Facing));
        if (off > 1.25f) return Guarded.None;
        Stamina = Math.Max(0, Stamina - .08f);
        bool shield = Bodies.Plan(b.T, b.Look).Shield && HasOffHand(b);
        var g = shield ? Draw(a, b, 1) < .92f ? Guarded.Block : Guarded.None
            : Draw(a, b, 1) < (WeaponOf(b.T).cut is Cut.Slash or Cut.Chop && HasMainHand(b) ? .6f : .35f) ? Guarded.Parry : Guarded.None;
        if (g != Guarded.None) GuardedBlows++;
        return g;
    }

    /// <summary>
    /// A kick: a siege ladder against the wall before it is torn off; else the one of the horde before it, in reach, is
    /// hurt a little and shoved back staggering (one of a man's size or less; a troll or a giant does not give).
    /// </summary>
    void Kick(Unit u)
    {
        if (Time < _kickReady) return;
        if (Stamina < .1f) { Refuse("нет сил"); return; }
        _kickReady = Time + 1.4;
        Stamina -= .1f;
        u.Swing = 1;
        KickedAt = Time;
        float fx = MathF.Cos(LookYaw), fz = MathF.Sin(LookYaw);
        int ci = Nav.I(u.Cell), cy = Nav.Y(u.Cell), ck = Nav.K(u.Cell);
        for (int dy = -3; dy <= 1; dy++)
            for (int dk = -1; dk <= 1; dk++)
                for (int di = -1; di <= 1; di++)
                {
                    if (!Nav.In(ci + di, cy + dy, ck + dk)) continue;
                    int c = Nav.Id(ci + di, cy + dy, ck + dk);
                    if (Nav.Ladder[c] == 0 || !SiegeVoxel(c, out var v)) continue;
                    float dx = v.x + .5f - u.X, dz = v.z + .5f - u.Z, d = MathF.Sqrt(dx * dx + dz * dz);
                    if (d > 3 * VoxelWorld.PerMetre || d > 1 && (dx * fx + dz * fz) / d < .3f) continue;
                    if (ThrowDown(v.x, v.y, v.z) > 0) { Say($"{u.T.Name} сбросил осадную лестницу.", 1, u); return; }
                }
        Unit best = null; float bd = ReachOf(u) + 8;
        foreach (var t in Units)
        {
            if (t.Dead || t.Inside || t.Team == Team.Player || t.Bound || t.Envoy) continue;
            float dx = t.X - u.X, dz = t.Z - u.Z, d = MathF.Sqrt(dx * dx + dz * dz);
            if (d > bd || MathF.Abs(t.Y - u.Y) > u.T.BodyH || d > 1 && (dx * fx + dz * fz) / d < .55f) continue;
            bd = d; best = t;
        }
        if (best == null) return;
        float ex = best.X - u.X, ez = best.Z - u.Z, el = MathF.Max(.5f, MathF.Sqrt(ex * ex + ez * ez));
        Hurt(best, u, 4, "пинок");
        if (best.Dead || best.T.BodyH > 18 || Kinds.IsEngine(best.T)) return;
        best.StagX = ex / el * 12; best.StagZ = ez / el * 12;
        best.StaggerUntil = Math.Max(best.StaggerUntil, Time + .6);
        best.Cooldown = Math.Max(best.Cooldown, .6f);
    }

    /// <summary>
    /// Laying what the player marked out, where the one gone into looks: the marked voxels about the face it looks at,
    /// within its reach, the lowest and nearest first, each as a worker lays it (from the stores, on something that holds
    /// it). False when nothing is marked there to lay: then it lays freely what it holds.
    /// </summary>
    bool LayMarked(Unit u, float dt)
    {
        if (!LookedAt(u, HandReach + 4, out var hit)) return _layingMarked;
        var (fx, fy, fz) = hit.Front;
        var near = new List<(int x, int y, int z, byte m)>();
        for (int dy = -3; dy <= 3; dy++)
            for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    if (dx * dx + dy * dy + dz * dz > 10) continue;
                    int x = fx + dx, y = fy + dy, z = fz + dz;
                    if (!World.InBounds(x, y, z)) continue;
                    byte o = Orders.At(x, y, z);
                    if (o == 0 || Orders.IsRemove(o)) continue;
                    near.Add((x, y, z, o));
                }
        if (near.Count == 0) return _layingMarked;
        OnMarked = true;
        _layingMarked = true;
        float ey = u.Y + u.T.BodyH * .88f;
        near.RemoveAll(v => (v.x + .5f - u.X) * (v.x + .5f - u.X) + (v.y + .5f - ey) * (v.y + .5f - ey) + (v.z + .5f - u.Z) * (v.z + .5f - u.Z) > (HandReach + 4) * (HandReach + 4));
        if (near.Count == 0) { Refuse("разметка далеко: подойдите ближе"); return true; }
        near.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : ((a.x - fx) * (a.x - fx) + (a.z - fz) * (a.z - fz)).CompareTo((b.x - fx) * (b.x - fx) + (b.z - fz) * (b.z - fz)));
        u.State = UnitState.Work;
        if (u.Swing <= 0) u.Swing = 1;
        float cost0 = Labour.Lay(near[0].m) / LayPace;
        u.Effort = Math.Min(u.Effort + (float)(Labour.WorkPerSecond * Math.Max(.4f, u.T.Work) * u.Pace * dt), cost0 * 4);
        int laid = 0;
        foreach (var v in near)
        {
            float cost = Labour.Lay(v.m) / LayPace;
            if (u.Effort < cost) break;
            if (!Do(u, v)) continue;
            u.Effort -= cost;
            laid++;
            Worked?.Invoke(v.x, v.y, v.z, v.m, false);
        }
        if (laid == 0 && u.Effort >= cost0)
        {
            int ck = Labour.CostKind(near[0].m);
            Refuse(ck != Stockpile.None && Stock[ck] < 1 ? "запас пуст: " + Stockpile.Name(ck) : "сначала то, на чём это держится");
        }
        return true;
    }

    /// <summary>
    /// Digging out what the player marked to come out, where the one gone into looks: the marked voxels about the one it
    /// strikes, within the bite of a pick, and only those. False when what it looks at is not marked.
    /// </summary>
    bool DigMarked(Unit u, float dt, VoxelHit hit)
    {
        if (!Orders.IsRemove(Orders.At(hit.X, hit.Y, hit.Z))) return false;
        OnMarked = true;
        var bite = new List<(int x, int y, int z, byte m)>();
        float cost = 0;
        int r = (int)MathF.Ceiling(Bite);
        for (int dy = -r; dy <= r; dy++)
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy + dz * dz > Bite * Bite) continue;
                    int x = hit.X + dx, y = hit.Y + dy, z = hit.Z + dz;
                    if (!World.InBounds(x, y, z)) continue;
                    byte o = Orders.At(x, y, z);
                    if (!Orders.IsRemove(o) || !Labour.Diggable(World.Get(x, y, z))) continue;
                    bite.Add((x, y, z, o));
                    cost += Labour.Dig(World.Get(x, y, z)) / DigPace;
                }
        if (bite.Count == 0) return true;
        u.State = UnitState.Work;
        if (u.Swing <= 0) u.Swing = 1;
        u.Effort += (float)(Labour.WorkPerSecond * Math.Max(.4f, u.T.Work) * u.Pace * dt);
        if (u.Effort < cost) return true;
        u.Effort = 0;
        foreach (var v in bite) Do(u, v);
        return true;
    }

    /// <summary>
    /// A ladder put up by the one gone into against the wall or the cliff it looks at: marked as the ladder tool marks it,
    /// to be laid by its own hands (the right button on the marking) or by the workers. False with nothing to set it on.
    /// </summary>
    public bool PossessedLadder()
    {
        var u = Possessed;
        if (u == null || u.T.Work <= 0) return false;
        if (!LookedAt(u, HandReach + 16, out var hit) || hit.NY != 0) { Refuse("лестницу ставят к стене: смотрите на её бок"); return false; }
        var plan = Shapes.LadderAt(World, Site, hit);
        if (plan == null || plan.Count == 0) { Refuse("сюда лестницу не поставить"); return false; }
        int n = DesignateBuild(plan);
        if (n == 0) { Refuse("лестница здесь уже размечена"); return false; }
        Say($"Лестница размечена: {n} вокселей. Правая кнопка по разметке — класть.", 1, u);
        return true;
    }
}
