using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>How far a warband has got: still to come, gathering where it came in, on the march, regrouping before the walls, storming.</summary>
public enum BandState : byte { Coming, Forming, March, Rally, Storm }

/// <summary>
/// A warband of the horde: a leader and those who follow it. It comes into the valley at one place and gathers there,
/// marches on together — the leader waits for stragglers, nobody runs on ahead of it — stops short of the walls to
/// regroup, and storms them all at once. When the leader falls the strongest of the rest takes its place.
/// </summary>
public sealed class Warband
{
    public int Id;
    /// <summary>"Ватага Гаррука": named after the one who leads it.</summary>
    public string Name = "";
    public BandState State;
    /// <summary>When the present state began, in game seconds.</summary>
    public double Since;
    /// <summary>The cell on the western edge where it comes in.</summary>
    public int Entry = -1;
    /// <summary>What it is made of, the leader first.</summary>
    public readonly List<UnitType> Roster = new();
    public readonly List<Unit> Members = new();
    public Unit Leader;
    public int Spawned;
    /// <summary>Sent to take a mine of ours rather than for the citadel.</summary>
    public Mine Target;
    /// <summary>The way it will go from where its leader is (or from where it comes in), cell by cell, and where on it
    /// it will hack through something or raise a ladder or a bridge.</summary>
    public readonly List<int> Trail = new();
    public readonly List<(int cell, Works works, bool carve)> Marks = new();
    /// <summary>Goes up whenever the trail is worked out anew.</summary>
    public int TrailRev;
    internal double WaitedFrom = -1;
    /// <summary>Every state it has been in, in order.</summary>
    public readonly List<BandState> History = new() { BandState.Coming };

    public int Alive { get { int n = 0; foreach (var u in Members) if (!u.Dead) n++; return n; } }
    /// <summary>Raiders still to come into the valley, and living in it.</summary>
    public int Strength => Roster.Count - Spawned + Alive;
}

public sealed partial class Sim
{
    /// <summary>The warbands of the wave in the valley, and those of the next one while it is still to come.</summary>
    public readonly List<Warband> Bands = new();
    int _nextBand = 1;
    double _nextTrails;
    /// <summary>The game shows where the bands will go: keep their trails worked out (and the horde's ways up to date between waves).</summary>
    public bool WantTrails;

    /// <summary>How close to the citadel, in voxels, a band stops to regroup before it storms.</summary>
    const float RallyR = 36 * VoxelWorld.PerMetre;
    /// <summary>In cells: how far on ahead of its leader a follower may get, how far off it may stray before it turns back
    /// to it, how far off one has to be for the leader to wait, and how close they gather before the storm.</summary>
    const float Ahead = 3, Stray = 7, Lag = 10, CaughtUp = 4;

    static readonly string[] LeaderNames =
    {
        "Гаррука", "Мрога", "Ухтара", "Грызя", "Шкуродёра", "Буржука", "Карга", "Зуборыла", "Ржавого", "Хряща", "Одноглазого", "Гнилозуба",
    };

    static int Rank(UnitType t) => t == Kinds.Troll ? 5 : t == Kinds.Orc ? 4 : t == Kinds.Miner ? 3 : t == Kinds.Goblin ? 2 : 1;

    /// <summary>
    /// Draws up the next wave: who comes, split into warbands of six or so, each with its strongest at its head, and
    /// where along the western edge each comes in. Shown on the map as soon as it is known, so its way can be read.
    /// </summary>
    public void PlanWave(int n)
    {
        Bands.RemoveAll(b => b.State == BandState.Coming);
        var list = new List<UnitType>();
        for (int i = 0; i < 2 + n; i++) list.Add(Kinds.Goblin);
        for (int i = 0; i < n / 2 + 1; i++) list.Add(Kinds.Orc);
        for (int i = 0; i < (n + 1) / 3; i++) list.Add(Kinds.Miner);
        for (int i = 0; i < 1 + n / 3; i++) list.Add(Kinds.Sapper);
        for (int i = 0; i < (n >= 5 ? (n - 3) / 3 : 0); i++) list.Add(Kinds.Troll);
        int count = Math.Clamp((list.Count + 5) / 7, 1, 5);
        // the strongest are dealt out first, one to a band, and lead it
        for (int i = list.Count - 1; i > 0; i--) { int j = Rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        list.Sort((a, b) => Rank(b).CompareTo(Rank(a)));
        var planned = new List<Warband>();
        for (int i = 0; i < count; i++) planned.Add(new Warband { Id = _nextBand++, State = BandState.Coming });
        for (int i = 0; i < list.Count; i++) planned[i % count].Roster.Add(list[i]);
        // where they come in: spread along the western edge, a little off even spacing
        for (int i = 0; i < count; i++)
        {
            var b = planned[i];
            int z = (int)(World.SZ * (i + 1.0) / (count + 1) + (Rng.NextDouble() - .5) * World.SZ / (count + 1) * .6);
            b.Entry = NearestStand(Site.SpawnStrip / 2, Math.Clamp(z, 40, World.SZ - 40), 3);
            b.Name = "Ватага " + LeaderNames[(b.Id * 7 + n) % LeaderNames.Length];
            // the tail of the shuffle beyond the leader: kinds come mixed
            for (int k = b.Roster.Count - 1; k > 1; k--) { int j = 1 + Rng.Next(k); (b.Roster[k], b.Roster[j]) = (b.Roster[j], b.Roster[k]); }
        }
        Bands.AddRange(planned);
        _nextTrails = 0;
    }

    /// <summary>The wave comes: if a mine is ours, a few goblins split off from the last band to take it.</summary>
    internal List<Warband> Muster()
    {
        var coming = Bands.FindAll(b => b.State == BandState.Coming);
        if (coming.Count == 0) return coming;
        Mine held = null;
        foreach (var m in Mines) if (m.Holder == Holder.Ours) { held = m; break; }
        if (held != null)
        {
            var from = coming[^1];
            var raid = new Warband { Id = _nextBand++, State = BandState.Coming, Target = held, Name = "Налётчики на " + held.Name.ToLowerInvariant() };
            for (int i = from.Roster.Count - 1; i >= 1 && raid.Roster.Count < 3; i--)
                if (from.Roster[i] is var t && (t == Kinds.Goblin || t == Kinds.Orc)) { raid.Roster.Add(t); from.Roster.RemoveAt(i); }
            if (raid.Roster.Count > 0)
            {
                // they come in where the mine is nearest the edge
                raid.Entry = NearestStand(Site.SpawnStrip / 2, Math.Clamp(held.Place.StandZ, 40, World.SZ - 40), 3);
                raid.Roster.Sort((a, b) => Rank(b).CompareTo(Rank(a)));
                Bands.Add(raid);
                coming.Add(raid);
            }
        }
        return coming;
    }

    /// <summary>One raider of a band comes into the valley where the band comes in; the first of it leads.</summary>
    public Unit SpawnRaider(UnitType t, Warband b)
    {
        int h = t.Move.Height, at = b.Entry >= 0 ? Spread(b.Entry, b.Spawned, h) : -1;
        if (at < 0) return SpawnRaider(t);
        var u = Spawn(t, at);
        u.Band = b;
        u.Raid = b.Target;
        b.Members.Add(u);
        if (b.Spawned++ == 0)
        {
            MakeLeader(b, u);
            Enter(b, BandState.Forming);
        }
        return u;
    }

    void MakeLeader(Warband b, Unit u)
    {
        b.Leader = u;
        if (u.Leader) return;
        u.Leader = true;
        // a leader is a head taller in spirit: half again as hard to kill
        float share = u.Hp / u.MaxHp;
        u.MaxHp *= 1.5f;
        u.Hp = u.MaxHp * share;
    }

    /// <summary>Keeps the bands together: forming up, marching, regrouping before the walls, storming; new leaders for fallen ones.</summary>
    void TickBands()
    {
        for (int i = Bands.Count - 1; i >= 0; i--)
        {
            var b = Bands[i];
            if (b.State == BandState.Coming) continue;
            if (b.Leader == null || b.Leader.Dead || !Armed(b.Leader))
            {
                Unit next = null;
                foreach (var u in b.Members)
                    if (!u.Dead && Armed(u) && (next == null || Rank(u.T) > Rank(next.T) || Rank(u.T) == Rank(next.T) && u.Hp > next.Hp)) next = u;
                if (next == null)
                {
                    if (b.Spawned >= b.Roster.Count) Bands.RemoveAt(i);
                    continue;
                }
                bool fell = b.Leader != null;
                MakeLeader(b, next);
                if (fell && b.Alive > 1) Say($"{b.Name}: вожак пал, ватагу ведёт {next.T.Name.ToLowerInvariant()}.", 1);
            }
            var l = b.Leader;
            switch (b.State)
            {
                case BandState.Forming:
                {
                    bool all = b.Spawned >= b.Roster.Count, near = true;
                    foreach (var u in b.Members) if (!u.Dead && CellsApart(u.Cell, l.Cell) > 5) near = false;
                    if (all && (near || Time - b.Since > 8) || Time - b.Since > 20) Enter(b, BandState.March);
                    break;
                }
                case BandState.March:
                {
                    // short of the walls: stop and gather; a band after a mine just goes
                    float dx = l.X - Site.KeepX, dz = l.Z - Site.KeepZ;
                    if (b.Target == null && dx * dx + dz * dz < RallyR * RallyR) Enter(b, BandState.Rally);
                    break;
                }
                case BandState.Rally:
                {
                    int alive = 0, near = 0;
                    foreach (var u in b.Members)
                    {
                        if (u.Dead) continue;
                        alive++;
                        if (u == l || Apart(u, l) <= (CaughtUp + 1) * NavGrid.CS) near++;
                    }
                    // a breath before the charge, however close they already are
                    if (Time - b.Since > 3 && near >= alive * .75f || Time - b.Since > 14)
                    {
                        Enter(b, BandState.Storm);
                        Say($"{b.Name} идёт на штурм: {alive} {Raiders(alive)}.", 2);
                    }
                    break;
                }
            }
        }
    }

    static string Raiders(int n) => n % 10 == 1 && n % 100 != 11 ? "налётчик" : n % 10 is >= 2 and <= 4 && (n % 100 < 10 || n % 100 >= 20) ? "налётчика" : "налётчиков";

    void Enter(Warband b, BandState s) { b.State = s; b.Since = Time; b.WaitedFrom = -1; b.History.Add(s); }

    /// <summary>How far a raider still has to go to the citadel, straight, in voxels: the same yardstick for every kind.</summary>
    float ToKeep(Unit u) => MathF.Sqrt((u.X - Site.KeepX) * (u.X - Site.KeepX) + (u.Z - Site.KeepZ) * (u.Z - Site.KeepZ));

    static float Apart(Unit a, Unit b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>
    /// What a raider of a band does this moment so the band keeps together: true when that is decided here (it holds,
    /// or it is on its way back to its leader), false when it goes on its own way to the citadel.
    /// </summary>
    bool BandStep(Unit u, float dt)
    {
        var b = u.Band;
        if (b == null || b.Target != null && b.State != BandState.Forming) return false;
        var l = b.Leader;
        if (l == null || l.Dead) return false;
        switch (b.State)
        {
            case BandState.Forming:
                u.State = UnitState.Idle;
                return true;
            case BandState.March:
            {
                if (u != l)
                {
                    // strayed off on a way of its own: back to the leader; close by, never on ahead of it
                    float apart = Apart(u, l);
                    if (apart > Stray * NavGrid.CS) return CatchUp(u, l, dt);
                    if (ToKeep(u) < ToKeep(l) - Ahead * NavGrid.CS) { u.State = UnitState.Idle; return true; }
                    return false;
                }
                // the leader waits for stragglers, but not for ever
                bool lagging = false;
                foreach (var m in b.Members) if (!m.Dead && m != l && Apart(m, l) > Lag * NavGrid.CS) { lagging = true; break; }
                if (!lagging) { b.WaitedFrom = -1; return false; }
                if (b.WaitedFrom < 0) b.WaitedFrom = Time;
                if (Time - b.WaitedFrom > 8) { if (Time - b.WaitedFrom > 20) b.WaitedFrom = -1; return false; }
                u.State = UnitState.Idle;
                return true;
            }
            case BandState.Rally:
            {
                // all close round the leader, then all at once
                if (u != l && Apart(u, l) > CaughtUp * NavGrid.CS) return CatchUp(u, l, dt);
                u.State = UnitState.Idle;
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>Walks back to the leader by its own way; false if there is none (it goes on by itself then).</summary>
    bool CatchUp(Unit u, Unit l, float dt)
    {
        if (u.Path == null || u.PathI >= u.Path.Count)
        {
            if (Time < u.NextSearch) return false;
            u.NextSearch = Time + 1.5;
            if (!GoTo(u, l.Cell, adjacent: true, budget: 8000)) return false;
        }
        Walk(u, dt);
        return true;
    }

    /// <summary>Works out every band's way: from its leader, or from where it will come in, along its leader's field.</summary>
    void TickTrails()
    {
        if (!WantTrails || Time < _nextTrails) return;
        _nextTrails = Time + 1.5;
        foreach (var b in Bands) WorkOutTrail(b);
    }

    public void WorkOutTrail(Warband b)
    {
        b.Trail.Clear(); b.Marks.Clear();
        b.TrailRev++;
        var kind = b.Leader != null && !b.Leader.Dead ? b.Leader.T : b.Roster.Count > 0 ? b.Roster[0] : null;
        int c = b.Leader != null && !b.Leader.Dead ? b.Leader.Cell : b.Entry;
        if (kind == null || c < 0) return;
        b.Trail.Add(c);
        if (b.Target != null)
        {
            // a band after a mine: the way there on foot
            int goal = NearestStand(b.Target.Place.StandX, b.Target.Place.StandZ, kind.Move.Height);
            if (goal < 0) return;
            int gi = Nav.I(goal), gk = Nav.K(goal);
            var path = Paths.Find(c, x => x == goal, x => H(x, gi, gk), kind.Move, 60000);
            if (path != null) b.Trail.AddRange(path);
            return;
        }
        if (!Fields.TryGetValue(kind, out var f)) return;
        _goalSet ??= new HashSet<int>(Goals);
        for (int step = 0; step < 1500 && !_goalSet.Contains(c); step++)
        {
            int next = f.Next(Nav, c, out float carve, out var works);
            if (next < 0 || next == c) break;
            if (carve > 0 || works != Works.None) b.Marks.Add((next, works, carve > 0));
            b.Trail.Add(next);
            c = next;
        }
    }
}
