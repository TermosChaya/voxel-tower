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
    /// <summary>Those who got away from the last wave alive and come back with this band, after its roster.</summary>
    public readonly List<Survivor> Veterans = new();
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
    /// <summary>Which way it is going (a unit vector in the valley), from its leader's way a few steps ahead: for the
    /// places its members keep beside and behind the leader.</summary>
    public float HX = 1, HZ;
    internal double HeadingAt = -1;
    /// <summary>Its first leader has fallen; and it has broken and runs.</summary>
    public bool LostLeader, Broken;
    /// <summary>
    /// The side of the citadel the chief sent it against (0 east, then round by south: 2 south, 4 west, 6 north; −1 for
    /// none), where it gathers on that side before the storm, and why that side.
    /// </summary>
    public int Side = -1, RallyCell = -1;
    public string Why = "";
    /// <summary>The chief's guess at each side where he knows nothing, from 0 (weak) to 1.</summary>
    internal float[] Hunch;
    /// <summary>The leader's way to the gathering place, cell by cell, and how far along it is; when to look for it again.</summary>
    internal List<int> RallyPath;
    internal int RallyStep;
    internal double RallySearch;
    /// <summary>How long its way to the citadel takes, in seconds, as its trail was last worked out (hacking and building counted).</summary>
    public float TrailSeconds;
    /// <summary>The way to the gathering place as last worked out for the trail, from where and when.</summary>
    internal List<int> WayCache;
    internal int WayFrom = -1, WayTo = -1;
    internal double WayAt = -99;
    /// <summary>Every state it has been in, in order.</summary>
    public readonly List<BandState> History = new() { BandState.Coming };

    public int Alive { get { int n = 0; foreach (var u in Members) if (!u.Dead) n++; return n; } }
    /// <summary>How many it comes with, the returning among them.</summary>
    public int Size => Roster.Count + Veterans.Count;
    /// <summary>Raiders still to come into the valley, and living in it.</summary>
    public int Strength => Size - Spawned + Alive;
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
        // those who got away from the last wave alive come back with this one, shared among the bands
        for (int i = 0; i < Escaped.Count; i++) planned[i % count].Veterans.Add(Escaped[i]);
        Bands.AddRange(planned);
        ChooseSides(Waves.IntelNow);
        _nextTrails = 0;
    }

    /// <summary>The sides of the citadel the chief weighs: eight, round by south from the east.</summary>
    public const int Sides = 8;
    public static readonly string[] SideNames = { "восточную", "юго-восточную", "южную", "юго-западную", "западную", "северо-западную", "северную", "северо-восточную" };
    /// <summary>How many of ours keep each side, as the chief last counted them (archers count more), for the panel and the tests.</summary>
    public readonly float[] SideKeep = new float[Sides];
    /// <summary>
    /// The sides as the chief sees them, for the ring round the citadel: 1 the weakest to his eye, 0 the strongest, −1
    /// where he finds nowhere to gather; how many bands he sends at each; and a count that goes up when he weighs again.
    /// </summary>
    public readonly float[] SideSeen = new float[Sides];
    public readonly int[] SideBands = new int[Sides];
    public int SidesRev;
    double _nextChoice;

    /// <summary>
    /// The chief weighs the sides of the citadel for every band still to come: how many of ours keep each (archers
    /// count more), how long the way in is from a gathering place outside whatever is built or dug on that side (what
    /// has to be hacked through or built over counts), and how far round it is from where the band comes in. What the
    /// horde has scouted he knows; the rest he guesses. Each band goes for the weakest side, a little less keen on one
    /// that another band takes already.
    /// </summary>
    public void ChooseSides(double intel)
    {
        var coming = Bands.FindAll(b => b.Target == null && b.State is BandState.Coming or BandState.Forming);
        if (coming.Count == 0) return;
        Array.Clear(SideKeep);
        foreach (var u in Units)
        {
            if (u.Dead || u.Team != Team.Player || u.T.Work >= 1 && !u.Militia) continue;
            var (x, _, z) = u.Post >= 0 ? Nav.Feet(u.Post) : (u.X, u.Y, u.Z);
            float dx = x - Site.KeepX, dz = z - Site.KeepZ;
            if (dx * dx + dz * dz > 60 * 60 * VoxelWorld.PerMetre * VoxelWorld.PerMetre) continue;
            int side = ((int)MathF.Round(MathF.Atan2(dz, dx) / (MathF.PI / 4)) % Sides + Sides) % Sides;
            float w = (u.T.Ranged ? 1.5f : u.T.Work < 1 ? 1 : .6f) * Math.Clamp(u.Hp / u.MaxHp, .2f, 1);
            SideKeep[side] += w;
            // an archer's reach covers the sides beside too
            SideKeep[(side + 1) % Sides] += w * .4f;
            SideKeep[(side + Sides - 1) % Sides] += w * .4f;
        }
        var rally = new int[Sides];
        for (int s = 0; s < Sides; s++) rally[s] = RallyPoint(s);
        var taken = new int[Sides];
        var seenBest = new float[Sides];
        Array.Fill(seenBest, float.PositiveInfinity);
        foreach (var b in coming)
        {
            var lead = b.Roster.Count > 0 ? b.Roster[0] : Kinds.Goblin;
            Fields.TryGetValue(lead, out var f);
            var (ex, _, ez) = b.Entry >= 0 ? Nav.Feet(b.Entry) : (0f, 0f, Site.KeepZ);
            var score = new float[Sides];
            var round = new float[Sides];
            float lo = float.MaxValue, hi = float.MinValue, nearest = float.MaxValue;
            for (int s = 0; s < Sides; s++)
            {
                round[s] = float.PositiveInfinity;
                if (rally[s] < 0) continue;
                var (rx, _, rz) = Nav.Feet(rally[s]);
                round[s] = MathF.Sqrt((rx - ex) * (rx - ex) + (rz - ez) * (rz - ez)) / (lead.Move.Speed * NavGrid.CS);
                nearest = Math.Min(nearest, round[s]);
            }
            for (int s = 0; s < Sides; s++)
            {
                float way = rally[s] < 0 ? float.PositiveInfinity : f == null ? 0 : f.Dist[rally[s]];
                if (float.IsPositiveInfinity(way)) { score[s] = float.PositiveInfinity; continue; }
                // the way round counts, and a long way round counts double: he will not march the band half the day
                score[s] = way + round[s] + Math.Max(0, round[s] - nearest - 30) * 1.5f + SideKeep[s] * 18;
                lo = Math.Min(lo, score[s]); hi = Math.Max(hi, score[s]);
            }
            if (lo == float.MaxValue) { b.Side = b.RallyCell = -1; b.Why = ""; continue; }
            if (b.Hunch == null) { b.Hunch = new float[Sides]; for (int s = 0; s < Sides; s++) b.Hunch[s] = (float)Rng.NextDouble(); }
            float spread = Math.Max(30, hi - lo), bestSeen = float.MaxValue;
            int best = -1;
            for (int s = 0; s < Sides; s++)
            {
                if (float.IsPositiveInfinity(score[s])) continue;
                float guess = (float)(intel * score[s] + (1 - intel) * (lo + spread * b.Hunch[s])), seen = guess + taken[s] * spread * .3f;
                seenBest[s] = Math.Min(seenBest[s], guess);
                if (seen < bestSeen) { bestSeen = seen; best = s; }
            }
            taken[best]++;
            if (b.Side != best || b.RallyCell != rally[best]) { b.Side = best; b.RallyCell = rally[best]; b.RallyPath = null; }
            float fewest = float.MaxValue;
            for (int s = 0; s < Sides; s++) if (!float.IsPositiveInfinity(score[s])) fewest = Math.Min(fewest, SideKeep[s]);
            b.Why = intel < .3 ? "наугад, разведать вас не успели"
                : SideKeep[best] < .3f ? "там никто не стережёт"
                : SideKeep[best] <= fewest + .3f ? "там стражи меньше всего"
                : "там короче путь";
        }
        // the ring round the citadel: how weak each side looks to him
        float min = float.MaxValue, max = float.MinValue;
        foreach (float v in seenBest) if (!float.IsPositiveInfinity(v)) { min = Math.Min(min, v); max = Math.Max(max, v); }
        for (int s = 0; s < Sides; s++)
        {
            SideSeen[s] = float.IsPositiveInfinity(seenBest[s]) ? -1 : max - min < 1e-3f ? .5f : (max - seenBest[s]) / (max - min);
            SideBands[s] = taken[s];
        }
        SidesRev++;
    }

    /// <summary>
    /// Where a band gathers to storm one side: outside whatever is built or dug on that side (6 m clear of the last
    /// of it), never nearer the citadel than 26 m; −1 if there is nowhere to stand there.
    /// </summary>
    int RallyPoint(int side)
    {
        float a = side * MathF.PI / 4, cx = MathF.Cos(a), cz = MathF.Sin(a), last = 0;
        for (float r = 10 * VoxelWorld.PerMetre; r < 70 * VoxelWorld.PerMetre; r += 4)
        {
            int x = (int)(Site.KeepX + cx * r), z = (int)(Site.KeepZ + cz * r);
            if (!World.InBounds(x, 1, z)) break;
            int ground = World.GroundTop(x, z);
            bool built = World.SurfAt(x, z) < Site.HTop[z * World.SX + x] - 6;
            for (int y = ground; y < Math.Min(World.SY, ground + 48) && !built; y += 2) built = Mat.IsPlayer[World.Get(x, y, z)];
            if (built) last = r;
        }
        float at = Math.Max(26 * VoxelWorld.PerMetre, last + 6 * VoxelWorld.PerMetre);
        int px = (int)(Site.KeepX + cx * at), pz = (int)(Site.KeepZ + cz * at);
        if (!World.InBounds(px, 1, pz)) return -1;
        int c = NearestStand(px, pz, 3);
        if (c < 0) return -1;
        var (fx, _, fz) = Nav.Feet(c);
        return MathF.Abs(fx - px) + MathF.Abs(fz - pz) < 6 * VoxelWorld.PerMetre ? c : -1;
    }

    /// <summary>
    /// The leader on its way to where the band gathers on the side the chief chose: along its own way there on foot, a
    /// cell at a time; false when there is no such way (then it goes straight for the walls as the field leads).
    /// </summary>
    bool MarchToRally(Unit l, Warband b, float dt)
    {
        if (b.RallyCell < 0) return false;
        if (b.RallyPath == null || b.RallyStep >= b.RallyPath.Count || CellsApart(l.Cell, b.RallyPath[b.RallyStep]) > 1
            || Math.Abs(Nav.Y(l.Cell) - Nav.Y(b.RallyPath[b.RallyStep])) > l.T.Move.Drop)
        {
            if (Time < b.RallySearch) return false;
            b.RallySearch = Time + 3;
            int ri = Nav.I(b.RallyCell), rk = Nav.K(b.RallyCell);
            var path = Paths.Find(l.Cell, c => Math.Abs(Nav.I(c) - ri) <= 1 && Math.Abs(Nav.K(c) - rk) <= 1, c => H(c, ri, rk), l.T.Move, 90000);
            if (path == null || path.Count == 0) { b.RallyCell = -1; b.RallyPath = null; return false; }
            b.RallyPath = path; b.RallyStep = 0;
        }
        l.Path ??= new List<int>();
        l.Path.Clear(); l.Path.Add(b.RallyPath[b.RallyStep++]); l.PathI = 0;
        Walk(l, dt);
        return true;
    }

    /// <summary>The wave comes: if a mine is ours, a few goblins split off from the last band to take it.</summary>
    internal List<Warband> Muster()
    {
        // what the horde scouted is all the chief will know: he chooses the sides once more by it
        ChooseSides(Waves.Intel);
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

    /// <summary>
    /// One raider of a band comes into the valley where the band comes in; the first of it leads. One that got away
    /// last time comes back as it went — its look, its wounds — mended halfway in the camp.
    /// </summary>
    public Unit SpawnRaider(UnitType t, Warband b, Survivor back = null)
    {
        int h = t.Move.Height, at = b.Entry >= 0 ? Spread(b.Entry, b.Spawned, h) : -1;
        if (at < 0) return SpawnRaider(t);
        var u = Spawn(t, at);
        if (back != null)
        {
            u.Look = back.Look;
            u.Wounds = back.Wounds;
            u.Hp = Math.Min(u.MaxHp, back.Hp + (u.MaxHp - back.Hp) * .5f);
            u.Returned = true;
        }
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
        // while bands are still to come the chief keeps weighing the sides as the defences change
        if (Time >= _nextChoice && Bands.Exists(b => b.State == BandState.Coming)) { _nextChoice = Time + 10; ChooseSides(Waves.Active ? Waves.Intel : Waves.IntelNow); }
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
                    if (b.Spawned >= b.Size) Bands.RemoveAt(i);
                    continue;
                }
                bool fell = b.Leader != null;
                b.LostLeader |= fell;
                MakeLeader(b, next);
                if (fell && b.Alive > 1) Say($"{b.Name}: вожак пал, ватагу ведёт {next.T.Name.ToLowerInvariant()}.", 1, next);
            }
            // its leader fallen and two in three of it gone: the rest have had enough
            if (!b.Broken && b.LostLeader && b.Spawned >= b.Size && b.Alive * 3 <= b.Size && b.Target == null)
            {
                b.Broken = true;
                foreach (var m in b.Members) if (!m.Dead) m.Routed = true;
                Say($"{b.Name} дрогнула и бежит: {(b.Alive == 1 ? "уцелел один" : $"уцелело {b.Alive}")}.", 1, b.Leader ?? b.Members.Find(m => !m.Dead));
            }
            var l = b.Leader;
            if (b.State is BandState.March or BandState.Rally && Time >= b.HeadingAt) { b.HeadingAt = Time + .5; Heading(b); }
            switch (b.State)
            {
                case BandState.Forming:
                {
                    bool all = b.Spawned >= b.Size, near = true;
                    foreach (var u in b.Members) if (!u.Dead && CellsApart(u.Cell, l.Cell) > 5) near = false;
                    if (all && (near || Time - b.Since > 8) || Time - b.Since > 20) Enter(b, BandState.March);
                    break;
                }
                case BandState.March:
                {
                    // where the chief sent it, or short of the walls: stop and gather; a band after a mine just goes
                    float dx = l.X - Site.KeepX, dz = l.Z - Site.KeepZ;
                    bool there = b.RallyCell >= 0 ? CellsApart(l.Cell, b.RallyCell) <= 3 : dx * dx + dz * dz < RallyR * RallyR;
                    if (b.Target == null && there) Enter(b, BandState.Rally);
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
                        Say($"{b.Name} идёт на штурм: {alive} {Raiders(alive)}.", 2, l);
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
                    // while the leader walks (or waits) each keeps its own place beside or behind it; while it hacks or
                    // builds the rest go on by their own ways and set to as well
                    if (l.State is UnitState.Walk or UnitState.Idle && KeepPlace(u, b, l, dt, still: l.State == UnitState.Idle)) return true;
                    return false;
                }
                // the leader waits for stragglers, but not for ever; it leads to where the chief sent the band
                bool lagging = false;
                foreach (var m in b.Members) if (!m.Dead && m != l && Apart(m, l) > Lag * NavGrid.CS) { lagging = true; break; }
                if (!lagging) { b.WaitedFrom = -1; return MarchToRally(u, b, dt); }
                if (b.WaitedFrom < 0) b.WaitedFrom = Time;
                if (Time - b.WaitedFrom > 8) { if (Time - b.WaitedFrom > 20) b.WaitedFrom = -1; return MarchToRally(u, b, dt); }
                u.State = UnitState.Idle;
                return true;
            }
            case BandState.Rally:
            {
                // all close round the leader, each in its place, then all at once
                if (u != l && Apart(u, l) > CaughtUp * NavGrid.CS) return CatchUp(u, l, dt);
                if (u != l && KeepPlace(u, b, l, dt, still: true)) return true;
                u.State = UnitState.Idle;
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Where a band's members keep round its leader, in cells across (minus to the left) and back: two at its shoulders,
    /// the rest in ranks behind, so they go side by side rather than in each other's steps.
    /// </summary>
    static readonly (float side, float back)[] Slots =
    {
        (-1.6f, .5f), (1.6f, .5f), (-.8f, 2.1f), (.8f, 2.1f), (-2.4f, 2.1f), (2.4f, 2.1f),
        (0, 3.7f), (-1.6f, 3.7f), (1.6f, 3.7f), (-3.2f, 3.7f), (3.2f, 3.7f),
    };

    /// <summary>The place a member keeps in its band, in valley voxels, each a little out of line.</summary>
    bool SlotOf(Warband b, Unit u, out float x, out float z)
    {
        x = z = 0;
        var l = b.Leader;
        int k = 0;
        foreach (var m in b.Members)
        {
            if (m == u) break;
            if (!m.Dead && m != l) k++;
        }
        if (u == l || k >= b.Members.Count) return false;
        var (side, back) = k < Slots.Length ? Slots[k] : (((k - Slots.Length) % 5 - 2) * 1.6f, 5.3f + (k - Slots.Length) / 5 * 1.6f);
        side += (float)(Hash.H3(u.Id, 3, 1) - .5) * .5f;
        back += (float)(Hash.H3(u.Id, 5, 2) - .5) * .5f;
        // the band's right hand is (−HZ, HX) turned the other way: across is minus to the left of the way it goes
        x = l.X + (-b.HZ * side - b.HX * back) * NavGrid.CS;
        z = l.Z + (b.HX * side - b.HZ * back) * NavGrid.CS;
        return true;
    }

    /// <summary>Which way a band goes: from its leader toward where its way leads six steps on, turning gently.</summary>
    void Heading(Warband b)
    {
        var l = b.Leader;
        if (l == null || l.Dead || !Fields.TryGetValue(l.T, out var f)) return;
        int c = l.Cell;
        for (int n = 0; n < 6; n++)
        {
            int next = f.Next(Nav, c, out _, out _);
            if (next < 0 || next == c) break;
            c = next;
        }
        var (x, _, z) = Nav.Feet(c);
        float dx = x - l.X, dz = z - l.Z, d = MathF.Sqrt(dx * dx + dz * dz);
        if (d < NavGrid.CS * .5f) return;
        float hx = b.HX + dx / d, hz = b.HZ + dz / d, h = MathF.Sqrt(hx * hx + hz * hz);
        if (h > 1e-3f) { b.HX = hx / h; b.HZ = hz / h; }
    }

    /// <summary>
    /// A member steps toward its place in the band (a little ahead of it while the leader walks, so it keeps pace),
    /// by a cell beside it it can walk straight into; true when it does so or stands in its place. False when no such
    /// cell brings it nearer: then for a while it goes on by its own way, which leads round whatever is in between.
    /// </summary>
    bool KeepPlace(Unit u, Warband b, Unit l, float dt, bool still)
    {
        if (Time < u.OwnWayUntil || !SlotOf(b, u, out float px, out float pz)) return false;
        if (!still) { px += b.HX * 1.5f * NavGrid.CS; pz += b.HZ * 1.5f * NavGrid.CS; }
        float Off(float x, float z) => (x - px) * (x - px) + (z - pz) * (z - pz);
        float here = Off(u.X, u.Z);
        if (still && here < NavGrid.CS * NavGrid.CS * .4f) { u.State = UnitState.Idle; return true; }
        int i = Nav.I(u.Cell), y = Nav.Y(u.Cell), k = Nav.K(u.Cell), h = u.T.Move.Height, best = -1;
        float bestOff = here - NavGrid.CS * 2;
        for (int d = 0; d < 8; d++)
        {
            int ni = i + DX8[d], nk = k + DK8[d];
            // no cutting corners round a wall
            if (d >= 4 && (!Nav.Stand(ni, y, k, h) || !Nav.Stand(i, y, nk, h))) continue;
            for (int dy = 0; dy >= -1; dy = dy == 0 ? 1 : dy > 0 ? -1 : -2)
            {
                if (d >= 4 && dy != 0) break;
                if (!Nav.Stand(ni, y + dy, nk, h)) continue;
                if (dy > 0 && !Nav.Open(i, y + h, k)) continue;
                if (dy < 0 && !Nav.Open(ni, y + h - 1, nk)) continue;
                int c = Nav.Id(ni, y + dy, nk);
                if (!FlowField.StepsOk(u.T.Move, Nav.Floor[u.Cell], Nav.Floor[c])) continue;
                var (fx, _, fz) = Nav.Feet(c);
                // wading only where the leader wades
                float off = Off(fx + u.OffX, fz + u.OffZ) + (Nav.Water[c] > 0 && Nav.Water[l.Cell] == 0 ? 400 : 0);
                if (off < bestOff) { bestOff = off; best = c; }
                break;
            }
        }
        if (best < 0)
        {
            // in its place, or there ahead of the leader: it waits for the place to move on
            if (still || here < NavGrid.CS * NavGrid.CS * 1.5f) { u.State = UnitState.Idle; return true; }
            u.OwnWayUntil = Time + 2;
            return false;
        }
        u.Path ??= new List<int>();
        u.Path.Clear(); u.Path.Add(best); u.PathI = 0;
        Walk(u, dt);
        return true;
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

    /// <summary>The way on foot from a cell to a band's gathering place, for its trail (worked out again at most every six seconds).</summary>
    List<int> WayToRally(Warband b, int from, UnitType kind)
    {
        if (b.WayFrom == from && b.WayTo == b.RallyCell && Time - b.WayAt < 6) return b.WayCache;
        int ri = Nav.I(b.RallyCell), rk = Nav.K(b.RallyCell);
        b.WayCache = Paths.Find(from, c => Math.Abs(Nav.I(c) - ri) <= 1 && Math.Abs(Nav.K(c) - rk) <= 1, c => H(c, ri, rk), kind.Move, 60000);
        b.WayFrom = from; b.WayTo = b.RallyCell; b.WayAt = Time;
        return b.WayCache;
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
        // first to where the chief sent it, on foot, then on from there as the field leads
        float walk = 0;
        if (b.RallyCell >= 0 && b.State is BandState.Coming or BandState.Forming or BandState.March)
        {
            var way = b.State == BandState.March && b.RallyPath != null && b.RallyStep < b.RallyPath.Count
                ? b.RallyPath.GetRange(b.RallyStep, b.RallyPath.Count - b.RallyStep) : WayToRally(b, c, kind);
            if (way is { Count: > 0 }) { b.Trail.AddRange(way); c = way[^1]; walk = way.Count * 1.15f / kind.Move.Speed; }
        }
        b.TrailSeconds = walk + (float.IsPositiveInfinity(f.Dist[c]) ? 0 : f.Dist[c]);
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
