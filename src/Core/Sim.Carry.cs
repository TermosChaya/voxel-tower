using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The wounded looked after by their own. One of ours who cannot get up — both legs broken, or down with its hurts —
/// is lifted onto a comrade's shoulder and carried to mend: into the citadel, or onto the floor of a dwelling nearer
/// (they mend there as well as by the citadel). The carrier goes at three fifths of its pace and takes no ladder. One
/// limping to the citadel on a broken leg is helped along by a comrade near with nothing more pressing, its arm over
/// the comrade's shoulder: it goes twice as fast. Whoever carries or helps puts the wounded down when it is struck,
/// when the alarm sounds, when the horde comes near, or when it is hurt itself, and sees to that first; soldiers carry
/// and help only while the horde is not in the valley. The player may send the chosen to one who cannot get up (a
/// right click on it): the nearest carries it.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The pace of one carrying another, and of one limping with a comrade under its arm.</summary>
    const float CarryPace = .6f, PropGait = .7f;

    /// <summary>How far, in voxels, one of ours sees a comrade who cannot get up or limps, and goes to it.</summary>
    const float SeeFallen = 35 * VoxelWorld.PerMetre;

    /// <summary>Up on someone's shoulder, carried.</summary>
    public static bool OnShoulder(Unit u) => u.Aloft && u.CarriedBy != null;

    /// <summary>Down and not getting up of itself (both legs broken, or its hurts keeping it down), and not pinned: to be carried.</summary>
    static bool CannotRise(Unit o) => o.Down && double.IsPositiveInfinity(o.DownUntil) && o.PinKg <= 0 && o.Airborne == null && !o.Inside
        && !o.Dead && !Kinds.IsEngine(o.T) && !o.T.Mounted && BodyKg(o.T) <= 150;

    /// <summary>Limping on a broken leg to mend: a comrade's shoulder would help it along.</summary>
    static bool Hobbling(Unit o) => Lame(o) && !o.Down && o.Mending && !o.Inside && !o.Dead && o.Airborne == null && !o.T.Mounted && !(o.Wounds?.Plan.Beast ?? false);

    /// <summary>Helped along, its helper beside it: it walks the better for it (<see cref="Gait"/>).</summary>
    public static bool Propped(Unit u) => u.PropBy is { Dead: false } h && h.Propping == u && MathF.Abs(h.X - u.X) + MathF.Abs(h.Z - u.Z) < 8;

    /// <summary>Where a wounded one is safe to mend: in the citadel, or in a dwelling (closed, with its door).</summary>
    public bool InDwelling(Unit u)
    {
        foreach (var z in Zones)
            if (z.Kind == Zone.Dwelling && z.Closed && z.Contains((int)u.X, (int)u.Z)) return true;
        return false;
    }

    /// <summary>
    /// One of ours carrying or helping: going on with it, or putting the wounded down when it is struck, the alarm
    /// sounds, the horde comes near, or it is hurt itself (then false, and it sees to that).
    /// </summary>
    bool Aid(Unit u, float dt)
    {
        if (u.Carrying != null && !Carrying(u, dt)) return false;
        if (u.Propping != null && !Propping(u, dt)) return false;
        return u.Carrying != null || u.Propping != null;
    }

    /// <summary>Whether it must put down what it carries, or let go of whom it helps, to see to itself.</summary>
    bool Pressed(Unit u) => Time - u.LastHurt < 1 || Alarm || u.Mending || u.Down || u.Airborne != null || u == Possessed
        || Nearest(u, Team.Horde, 6 * VoxelWorld.PerMetre) != null;

    /// <summary>A free one of ours looks about it for a comrade who cannot get up, or one limping alone, and goes to it.</summary>
    bool LookForFallen(Unit u)
    {
        if (Time < u.NextLookForFallen || Alarm || u.T.Mounted || u.T.NoHands) return false;
        // (spread by who it is, not by the valley's dice, so that looking changes no other chance)
        u.NextLookForFallen = Time + 1 + u.Id % 7 * .07;
        if (Nearest(u, Team.Horde, 12 * VoxelWorld.PerMetre) != null) return false;
        Unit carry = null, prop = null;
        float bestC = SeeFallen, bestP = SeeFallen * .7f;
        foreach (var o in Units)
        {
            if (o == u || o.Team != Team.Player || o.Dead || o.Inside) continue;
            float d = Dist(u, o);
            if (d < bestC && CannotRise(o) && o.CarriedBy is not { Dead: false } && !AtMending(o) && BodyKg(o.T) <= BodyKg(u.T) * 2.2f
                && o.Bandager is not { Dead: false } && Nearest(o, Team.Horde, 12 * VoxelWorld.PerMetre) == null) { bestC = d; carry = o; }
            else if (d < bestP && Hobbling(o) && o.PropBy is not { Dead: false } && Nearest(o, Team.Horde, 12 * VoxelWorld.PerMetre) == null) { bestP = d; prop = o; }
        }
        if (carry != null) { Shoulder(u, carry); return true; }
        if (prop != null)
        {
            u.Propping = prop; prop.PropBy = u;
            ReleaseSite(u); StopCarve(u); StopBuild(u);
            u.Path = null;
            return true;
        }
        return false;
    }

    /// <summary>It goes to lift a comrade who cannot get up.</summary>
    void Shoulder(Unit u, Unit o)
    {
        if (o.CarriedBy is { } was && was != u) was.Carrying = null;
        u.Carrying = o; o.CarriedBy = u; o.Aloft = false;
        u.LiftEffort = 0; u.CarryTo = -1;
        ReleaseSite(u); StopCarve(u); StopBuild(u);
        u.Path = null; u.Prey = null; u.Hold = false;
    }

    /// <summary>Already where it mends: in a dwelling, or by the citadel's door.</summary>
    bool AtMending(Unit o) => CellsApart(o.Cell, Gate) <= 3 || InDwelling(o);

    /// <summary>Carrying (or going to lift) one who cannot get up; false when it has put it down to see to itself.</summary>
    bool Carrying(Unit u, float dt)
    {
        var o = u.Carrying;
        if (o.Dead || o.CarriedBy != u || o.Inside || o.PinKg > 0 || !o.Down)
        {
            if (o.CarriedBy == u) { o.CarriedBy = null; o.Aloft = false; }
            u.Carrying = null;
            return true;
        }
        if (Pressed(u)) { PutDown(u); return false; }
        if (!o.Aloft)
        {
            // to its side, and up onto the shoulder: it takes a moment
            u.Why = $"идёт поднять {Whom(o.T)}";
            // beside it: in a cell next to its own (as GoTo has it), or within arm's reach
            bool beside = CellsApart(u.Cell, o.Cell) <= 1 && Math.Abs(Nav.Y(u.Cell) - Nav.Y(o.Cell)) <= 1;
            if (!beside && Dist(u, o) > u.T.BodyR + o.T.BodyR + 9)
            {
                if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
                {
                    u.NextSearch = Time + .8;
                    if (!GoTo(u, o.Cell, adjacent: true, budget: 40000)) { o.CarriedBy = null; u.Carrying = null; u.NextLookForFallen = Time + 5; return true; }
                }
                if (u.Path != null && u.PathI < u.Path.Count) Walk(u, dt);
                return true;
            }
            u.Path = null;
            u.State = UnitState.Work;
            u.Facing = MathF.Atan2(o.Z - u.Z, o.X - u.X);
            u.LiftEffort += dt;
            if (u.LiftEffort < 1.2f) return true;
            o.Aloft = true;
            u.CarryTo = MendingPlace(u, out bool dwelling);
            if (u.CarryTo < 0) { PutDown(u); u.NextLookForFallen = Time + 10; return true; }
            if (Time - _carrySaid > 6)
            {
                _carrySaid = Time;
                Say($"{u.T.Name} несёт {Whom(o.T)} {(dwelling ? "в жилой дом" : "к цитадели")}.", 1, o);
            }
        }
        bool there = u.CarryTo == Gate ? CellsApart(u.Cell, Gate) <= 2 : CellsApart(u.Cell, u.CarryTo) <= 1;
        if (!there)
        {
            u.Why = u.CarryTo == Gate ? $"несёт {Whom(o.T)} к цитадели" : $"несёт {Whom(o.T)} в жилой дом";
            if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
            {
                u.NextSearch = Time + 2;
                if (!GoTo(u, u.CarryTo, adjacent: true, budget: 30000)) { PutDown(u); u.NextLookForFallen = Time + 10; return true; }
            }
            if (u.Path != null && u.PathI < u.Path.Count) Walk(u, dt, CarryPace);
            return true;
        }
        // there: into the citadel to mend, or laid down on the dwelling's floor
        PutDown(u);
        if (u.CarryTo == Gate) { o.Mending = true; Enter(o); }
        u.CarryTo = -1;
        u.NextLookForFallen = Time + 2;
        return true;
    }

    double _carrySaid = -99;

    /// <summary>Where to take a wounded one: the citadel's door, or a dwelling with a bunk whole in it if that is nearer.</summary>
    int MendingPlace(Unit u, out bool dwelling)
    {
        dwelling = false;
        int best = Gate;
        float far = CellsApart(u.Cell, Gate);
        foreach (var z in Zones)
        {
            if (z.Kind != Zone.Dwelling || !z.Closed || z.BunksWhole.Count == 0) continue;
            foreach (int b in z.BunksWhole)
            {
                var (bx, by, bz) = WaterPlanner.Unpack(b);
                int c = StandNear(bx, by, bz, u.T.Move.Height);
                if (c < 0 || CellsApart(c, u.Cell) >= far) continue;
                var (fx, _, fz) = Nav.Feet(c);
                if (!z.Contains((int)fx, (int)fz)) continue;
                best = c; far = CellsApart(c, u.Cell); dwelling = true;
                break;
            }
        }
        return best;
    }

    /// <summary>What it carries put down at its feet (or, not yet lifted, left where it lies).</summary>
    void PutDown(Unit u)
    {
        var o = u.Carrying;
        u.Carrying = null;
        u.Path = null;
        if (o == null || o.CarriedBy != u) return;
        o.CarriedBy = null;
        if (!o.Aloft) return;
        o.Aloft = false;
        o.Cell = u.Cell;
        var (fx, fy, fz) = Nav.Feet(u.Cell);
        // beside its feet, the way it faces
        o.X = fx + MathF.Cos(u.Facing) * 3; o.Y = fy; o.Z = fz + MathF.Sin(u.Facing) * 3;
        o.Facing = u.Facing + MathF.PI / 2;
        o.DownSince = Time - 5;
    }

    /// <summary>Up on a comrade's shoulder: it goes where it is carried, and is put down if its carrier can carry it no more.</summary>
    void Shouldered(Unit o, float dt)
    {
        var u = o.CarriedBy;
        if (u.Dead || u.Inside || u.Down || u.Airborne != null || u == Possessed || u.Carrying != o) { if (u.Carrying == o) PutDown(u); else { o.CarriedBy = null; o.Aloft = false; } return; }
        o.Cell = u.Cell;
        o.X = u.X; o.Y = u.Y; o.Z = u.Z;
        o.Facing = u.Facing + MathF.PI;
        o.Path = null; o.State = UnitState.Idle;
        o.Why = $"его несут: {(u.CarryTo == Gate ? "к цитадели" : "в жилой дом")}";
        TickPain(o, dt);
        // eased enough to stand on its own legs: put down to walk
        if (!o.Down) PutDown(u);
    }

    /// <summary>Helping a comrade along on a broken leg, at its side; false when it has let go to see to itself.</summary>
    bool Propping(Unit u, float dt)
    {
        var p = u.Propping;
        if (p.Dead || p.PropBy != u || !Hobbling(p))
        {
            if (p.PropBy == u) p.PropBy = null;
            u.Propping = null;
            return true;
        }
        if (Pressed(u)) { Unprop(u); return false; }
        float d = Dist(u, p);
        if (d > 2.5f * VoxelWorld.PerMetre)
        {
            u.Why = $"идёт помочь {Whom(p.T)}";
            if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
            {
                u.NextSearch = Time + .8;
                if (!GoTo(u, p.Cell, adjacent: true, budget: 8000)) { Unprop(u); u.NextLookForFallen = Time + 5; return true; }
            }
            if (u.Path != null && u.PathI < u.Path.Count) Walk(u, dt, 1.2f);
            return true;
        }
        // shoulder to shoulder at its right, the way it goes (its right arm over the shoulder); where there is no room for that, close behind it
        u.Path = null;
        float side = p.Facing + MathF.PI / 2, gap = u.T.BodyR + p.T.BodyR + .5f;
        float x = p.X + MathF.Cos(side) * gap, z = p.Z + MathF.Sin(side) * gap;
        int c = Nav.CellOf((int)x, (int)p.Y + 1, (int)z);
        if (c < 0 || !CanStand(u, c)) { x = p.X - MathF.Cos(p.Facing) * gap; z = p.Z - MathF.Sin(p.Facing) * gap; c = p.Cell; }
        u.X = x; u.Z = z; u.Y = p.Y; u.Cell = c;
        u.Facing = p.Facing;
        u.State = UnitState.Idle;
        u.Why = $"ведёт {Whom(p.T)} к цитадели";
        return true;
    }

    /// <summary>Whether one could stand in a cell (for walking beside a comrade).</summary>
    bool CanStand(Unit u, int c) => c >= 0 && Nav.Fill[c] < NavGrid.SolidT && (Nav.Stand(Nav.I(c), Nav.Y(c), Nav.K(c), u.T.Move.Height) || Nav.Y(c) >= 1 && Nav.Ground(Nav.I(c), Nav.Y(c) - 1, Nav.K(c)));

    void Unprop(Unit u)
    {
        var p = u.Propping;
        u.Propping = null;
        if (p != null && p.PropBy == u) p.PropBy = null;
    }

    /// <summary>Every tie of carrying and helping it has, let go (thrown by a blast, gone into the citadel, dead).</summary>
    void LetGoOf(Unit u)
    {
        if (u.Carrying != null) PutDown(u);
        if (u.CarriedBy is { } c && c.Carrying == u) PutDown(c);
        u.CarriedBy = null; u.Aloft = false;
        if (u.Propping != null) Unprop(u);
        if (u.PropBy is { } h && h.Propping == u) Unprop(h);
        u.PropBy = null;
    }

    /// <summary>
    /// The chosen sent to one of ours who cannot help itself: to one pinned, all of them dig it out; to one down who
    /// cannot get up, the nearest of them carries it to mend. How many went.
    /// </summary>
    public int OrderAid(IEnumerable<Unit> chosen, Unit p)
    {
        if (p == null || p.Dead || p.Team != Team.Player) return 0;
        int n = 0;
        if (p.PinKg > 0)
        {
            foreach (var u in chosen)
            {
                if (u == p || u.Dead || u.Inside || u.Team != Team.Player || u.T.NoHands || u.T.Mounted || Kinds.IsEngine(u.T) || Hands(u) < .2f) continue;
                LetGoOf(u);
                ReleaseSite(u); StopCarve(u); StopBuild(u);
                u.Hold = false; u.Prey = null; u.Path = null; u.NextSearch = 0;
                if (u.Freeing is { } was && was != p && was.Digger == u) was.Digger = null;
                u.Freeing = p; u.FreeOrdered = true; u.FreeEffort = 0;
                if (p.Digger is not { Dead: false }) p.Digger = u;
                p.DugAt = Time;
                n++;
            }
            return n;
        }
        if (!CannotRise(p)) return 0;
        Unit best = null;
        float near = float.MaxValue;
        foreach (var u in chosen)
        {
            if (u == p || u.Dead || u.Inside || u.Team != Team.Player || u.T.NoHands || u.T.Mounted || Kinds.IsEngine(u.T) || BodyKg(p.T) > BodyKg(u.T) * 2.2f) continue;
            float d = Dist(u, p);
            if (d < near) { near = d; best = u; }
        }
        if (best == null) return 0;
        LetGoOf(best);
        Shoulder(best, p);
        return 1;
    }

    /// <summary>What the player may do for one of ours with a right click: «откопать», «вынести», or nothing.</summary>
    public static string AidFor(Unit p) => p == null || p.Dead || p.Team != Team.Player ? null : p.PinKg > 0 ? "откопать" : CannotRise(p) && p.CarriedBy is not { Dead: false } ? "вынести к цитадели" : null;
}
