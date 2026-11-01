using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The town's own ways. Workers with nothing marked to do, the town's work allowed them in the work table (WorkKind.Town),
/// lay the town's ways by themselves: from the citadel's gate to every door of the town — of its buildings, of its houses,
/// of the gates in its walls — and to its wells and campfires, each the shortest way that keeps to the ways planned
/// already, a metre and a half wide, with a pad two metres across before each door. Rammed earth to begin with; gravel
/// once the stores have stone to spare; cobbles once a masons' yard works and there is stone enough, the earth taken up
/// for them. What the player laid of flags or boards is left as it is. They lay it a cell at a time, by hand: the turf
/// cut and the way set in its place, a few columns a second. The plan is found again when the doors change, so a new
/// house gets its way; it is not kept in the save (only the ways themselves, laid in the ground).
/// </summary>
public sealed partial class Sim
{
    /// <summary>The town's ways as planned: for each cell column (k·NX + i), the columns of ground in it to lay, packed z·SX + x.</summary>
    readonly Dictionary<int, List<int>> _townWays = new();
    /// <summary>Cells of the plan found laid to the grade they were looked over for: not looked over again till the grade rises.</summary>
    readonly Dictionary<int, WayKind> _townDone = new();
    /// <summary>Who lays which cell of the plan now; the cells none could get to lately, till when.</summary>
    readonly Dictionary<int, Unit> _paving = new();
    readonly Dictionary<int, double> _townNoWay = new();
    double _nextTownWays, _townPlanned = -1, _ordersMoved = -999;
    int _ordersSeen;
    long _townDoorsKey;
    bool _townSaid;
    /// <summary>The columns of the town's ways planned now, and those laid by the workers since the valley was entered.</summary>
    public int TownWayColumns, TownWaysLaid;

    /// <summary>How far round the citadel the town's ways go (90 m); how wide they are and the pads before the doors, voxels.</summary>
    const int TownReach = 90 * VoxelWorld.PerMetre, TownWayWidth = 12, TownPad = 16;
    /// <summary>Columns of a way a worker lays a second: the turf cut and the way rammed or set.</summary>
    const float PaveRate = 6;
    /// <summary>
    /// Seconds a worker has had no marked work before it goes to the town's ways: what is marked and waits a moment (for
    /// the course under it, for stone) is not left for the streets.
    /// </summary>
    const double TownIdle = 20;
    /// <summary>
    /// Seconds since the marked work in the valley last went down before anyone goes to the streets: while a building
    /// goes up, its workers wait out a course out of their reach or a scaffold going up rather than go off paving.
    /// </summary>
    const double TownBusy = 90;

    /// <summary>
    /// The grade the town's ways are laid to now: cobbles with a masons' yard at work and forty cubic metres of stone in
    /// the stores, gravel with fifteen, else rammed earth.
    /// </summary>
    public WayKind TownGrade() =>
        Stock[Stockpile.Stone] >= 40 * VoxelWorld.PerM3 && Buildings.Exists(b => b.Kind == "masons" && b.Ready) ? WayKind.Cobble
        : Stock[Stockpile.Stone] >= 15 * VoxelWorld.PerM3 ? WayKind.Gravel : WayKind.Dirt;

    /// <summary>How good a way the top of a column is: none, trodden, earth, gravel, cobbles (flags and boards as good).</summary>
    static int WayRank(byte m) => m switch { Mat.Trodden => 0, Mat.DirtPath or Mat.Path => 1, Mat.Gravel => 2, Mat.Cobble or Mat.Flag or Mat.Boardwalk => 3, _ => -1 };
    static int WayRank(WayKind k) => k switch { WayKind.Dirt => 1, WayKind.Gravel => 2, _ => 3 };

    /// <summary>How many columns of the town's ways are planned now, and how many of them are laid (a way of any kind on them).</summary>
    public (int planned, int laid) TownWaysDone()
    {
        int planned = 0, laid = 0;
        foreach (var cols in _townWays.Values)
            foreach (int p in cols)
            {
                int x = p % World.SX, z = p / World.SX, top = World.SurfAt(x, z) - 1;
                planned++;
                if (top >= 1 && WayRank(World.Get(x, top, z)) >= 1) laid++;
            }
        return (planned, laid);
    }

    /// <summary>
    /// Whether a column of the plan still wants its way at a grade: its top what a way is laid over, below the grade,
    /// nothing marked or meant there and no water over it; the top's height.
    /// </summary>
    bool WantsWay(int packed, WayKind grade, out int top)
    {
        int x = packed % World.SX, z = packed / World.SX;
        top = World.SurfAt(x, z) - 1;
        if (top < 1) return false;
        byte m = World.Get(x, top, z);
        if (!Paveable(m) || World.Get(x, top + 1, z) == Mat.Water || Orders.At(x, top, z) != 0 || MeantAt(x, top, z) != 0) return false;
        return WayRank(m) < WayRank(grade);
    }

    /// <summary>Every half minute: the ways planned again if the town's doors have changed; with the alarm up the streets are left.</summary>
    void TickTownWays()
    {
        // when the marked work last went down: work going on in the valley is done before its streets
        if (Orders.Total < _ordersSeen) _ordersMoved = Time;
        _ordersSeen = Orders.Total;
        if (Time < _nextTownWays) return;
        _nextTownWays = Time + 30;
        if (Alarm || Raids.Active)
        {
            foreach (var (_, u) in _paving) if (u.Paving >= 0) { u.Paving = -1; u.PaveEffort = 0; }
            _paving.Clear();
        }
        var doors = TownDoors();
        long key = doors.Count;
        foreach (var (x, z) in doors) key = key * 1000003 + x * 4099 + z;
        if (key == _townDoorsKey && _townPlanned >= 0 && Time - _townPlanned < 600) return;
        _townDoorsKey = key;
        _townPlanned = Time;
        PlanTownWays(doors);
    }

    /// <summary>
    /// The places the town's ways go to: before each door that is hung (the street side of a house's or a building's,
    /// both sides of a gate in a wall), each well and campfire; none without a town (a building or a house of ours), and
    /// none further from the citadel than the town goes.
    /// </summary>
    List<(int x, int z)> TownDoors()
    {
        var list = new List<(int x, int z)>();
        if (!Site.HasKeep || Gate < 0) return list;
        bool town = false;
        foreach (var b in Buildings) if (b.Ready) town = true;
        foreach (var zone in Zones) if (zone.Kind == Zone.Dwelling) town = true;
        if (!town) return list;
        bool Near(int x, int z) => (x - Site.KeepX) * (x - Site.KeepX) + (z - Site.KeepZ) * (z - Site.KeepZ) <= TownReach * TownReach && World.OnLand(x, z);
        foreach (var g in Gates)
        {
            if (!g.Ready || g.Broken) continue;
            var (mx, _, mz) = g.Middle;
            for (int s = -1; s <= 1; s += 2)
            {
                int x = (int)mx + (g.AlongX ? 0 : s * 14), z = (int)mz + (g.AlongX ? s * 14 : 0);
                if (Indoors(x, z) || !Near(x, z)) continue;
                list.Add((x, z));
            }
        }
        foreach (var y in Yards)
            if (y.Ready && y.Kind is DecorKind.Well or DecorKind.Campfire && Near(y.X, y.Z)) list.Add((y.X + 14, y.Z));
        return list;
    }

    /// <summary>Whether a column is inside a building of the town or a house: a room, not the street.</summary>
    bool Indoors(int x, int z)
    {
        foreach (var b in Buildings) if (x > b.X0 && x < b.X1 && z > b.Z0 && z < b.Z1) return true;
        foreach (var zone in Zones) if (zone.Kind == Zone.Dwelling && x > zone.X0 && x < zone.X1 && z > zone.Z0 && z < zone.Z1) return true;
        return false;
    }

    /// <summary>
    /// The town's ways planned: from the doors nearest the citadel's gate outward, each the way a worker would walk from
    /// it to the gate or to a way planned already (a step on one counts for less, so they run together), laid a metre
    /// and a half wide along it, and a pad before the door.
    /// </summary>
    void PlanTownWays(List<(int x, int z)> doors)
    {
        _townWays.Clear(); _townDone.Clear(); _townNoWay.Clear();
        TownWayColumns = 0;
        if (doors.Count == 0) return;
        int gi = Nav.I(Gate), gk = Nav.K(Gate);
        var routed = new HashSet<int> { Gate };
        var seen = new HashSet<int>();
        var (gx, _, gz) = Nav.Feet(Gate);
        doors.Sort((a, b) => ((a.x - gx) * (a.x - gx) + (a.z - gz) * (a.z - gz)).CompareTo((b.x - gx) * (b.x - gx) + (b.z - gz) * (b.z - gz)));
        var mover = Kinds.Worker.Move;
        foreach (var (x, z) in doors)
        {
            AddTownWay(WayPlan(World, Site, x - TownPad / 2, z, x + TownPad / 2, z, TownPad, WayKind.Dirt), seen);
            int from = NearestStand(x, z, mover.Height);
            if (from < 0 || routed.Contains(from)) continue;
            var path = Paths.Find(from, c => routed.Contains(c), c => H(c, gi, gk), mover, 40000, gates: true, lane: c => routed.Contains(c) ? 0 : .4f);
            if (path == null) continue;
            var (px, _, pz) = Nav.Feet(from);
            routed.Add(from);
            foreach (int c in path)
            {
                var (cx, _, cz) = Nav.Feet(c);
                AddTownWay(WayPlan(World, Site, (int)px, (int)pz, (int)cx, (int)cz, TownWayWidth, WayKind.Dirt), seen);
                (px, pz) = (cx, cz);
                routed.Add(c);
            }
        }
    }

    void AddTownWay(VoxelList L, HashSet<int> seen)
    {
        for (int i = 0; i < L.Count; i++)
        {
            int x = L.X(i), z = L.Z(i), packed = z * World.SX + x;
            if (!seen.Add(packed)) continue;
            int col = (z >> NavGrid.Shift) * Nav.NX + (x >> NavGrid.Shift);
            if (!_townWays.TryGetValue(col, out var cols)) _townWays[col] = cols = new List<int>();
            cols.Add(packed);
            TownWayColumns++;
        }
    }

    /// <summary>What one at the town's ways is doing, for its card.</summary>
    string PavingWhy => TownGrade() == WayKind.Dirt ? "прокладывает тропу по городу" : "мостит улицу";

    /// <summary>
    /// A worker with nothing marked to do lays the nearest cell of the town's ways still wanting it, that nobody else is
    /// at; true if there is one to go to.
    /// </summary>
    bool TownWork(Unit u)
    {
        if (_townWays.Count == 0 || u.T.Work < 1 || !Work.Does(u, WorkKind.Town) || !HasMainHand(u) || Alarm || Raids.Active) return false;
        if (Time - u.MarkedWorkAt < TownIdle || Orders.Total > 0 && Time - _ordersMoved < TownBusy) return false;
        var grade = TownGrade();
        int ui = Nav.I(u.Cell), uk = Nav.K(u.Cell), best = -1;
        float bd = float.MaxValue;
        foreach (var (col, cols) in _townWays)
        {
            if (_townDone.TryGetValue(col, out var done) && WayRank(done) >= WayRank(grade)) continue;
            if (_paving.TryGetValue(col, out var by) && by != u && !by.Dead && by.Paving == col) continue;
            if (_townNoWay.TryGetValue(col, out double until) && Time < until) continue;
            float d = PathFinder.Octile(col % Nav.NX - ui, col / Nav.NX - uk);
            if (d >= bd) continue;
            bool wants = false;
            foreach (int p in cols) if (WantsWay(p, grade, out _)) { wants = true; break; }
            if (!wants) { _townDone[col] = grade; continue; }
            bd = d; best = col;
        }
        if (best < 0) return false;
        StopPaving(u);
        u.Paving = best; u.PaveEffort = 0;
        _paving[best] = u;
        u.Why = PavingWhy;
        if (!_townSaid)
        {
            _townSaid = true;
            Say("Рабочие без дела прокладывают тропы по городу: от цитадели к дверям, воротам и колодцам.", 1, (best % Nav.NX << NavGrid.Shift) + 4, (best / Nav.NX << NavGrid.Shift) + 4);
        }
        return true;
    }

    void StopPaving(Unit u)
    {
        if (u.Paving >= 0 && _paving.TryGetValue(u.Paving, out var by) && by == u) _paving.Remove(u.Paving);
        u.Paving = -1; u.PaveEffort = 0;
    }

    /// <summary>
    /// A worker at the town's ways: walking to its cell, then laying it column by column — the turf taken up and the way set
    /// in its place, the stone of gravel and cobbles from the stores — till the cell is done. True while at it.
    /// </summary>
    bool PaveTick(Unit u, float dt)
    {
        int col = u.Paving;
        if (!_townWays.TryGetValue(col, out var cols) || !Work.Does(u, WorkKind.Town) || Alarm || Raids.Active) { StopPaving(u); return false; }
        int ci = col % Nav.NX, ck = col / Nav.NX;
        if (Math.Abs(Nav.I(u.Cell) - ci) > 1 || Math.Abs(Nav.K(u.Cell) - ck) > 1)
        {
            if (u.Path == null || u.PathI >= u.Path.Count)
            {
                int at = TownStand(ci, ck, cols, u.T.Move.Height);
                if (at < 0 || !GoTo(u, at, adjacent: true, budget: 20000)) { _townNoWay[col] = Time + 60; StopPaving(u); return false; }
            }
            u.Why = PavingWhy;
            Walk(u, dt);
            return true;
        }
        u.Path = null;
        // marked work it can do come up meanwhile: that first
        if (Time >= u.NextSearch)
        {
            u.NextSearch = Time + 3;
            if (MarkedWorkFor(u)) { StopPaving(u); u.NextSearch = 0; return false; }
        }
        u.State = UnitState.Work;
        u.Facing = MathF.Atan2((ck << NavGrid.Shift) + 4 - u.Z, (ci << NavGrid.Shift) + 4 - u.X);
        u.Why = PavingWhy;
        var grade = TownGrade();
        u.PaveEffort += dt * PaveRate * Math.Max(.3f, u.T.Work) * u.Pace * Hands(u);
        while (u.PaveEffort >= 1)
        {
            int next = -1, top = 0;
            foreach (int p in cols) if (WantsWay(p, grade, out top)) { next = p; break; }
            if (next < 0) { _townDone[col] = grade; StopPaving(u); u.NextSearch = 0; return false; }
            int x = next % World.SX, z = next / World.SX;
            var kind = grade;
            // a voxel of stone a column for gravel and cobbles; with none left, rammed earth for now
            if (kind != WayKind.Dirt && !Stock.Take(Stockpile.Stone, 1)) kind = WayKind.Dirt;
            if (WayRank(World.Get(x, top, z)) >= WayRank(kind)) { _townDone.Remove(col); u.PaveEffort = 0; break; }
            byte m = WayMat(kind, x, z);
            World.Set(x, top, z, m);
            for (int yy = top + 1; yy < top + 4 && World.InBounds(x, yy, z); yy++)
            {
                if (!Mat.IsSprig(World.Get(x, yy, z))) break;
                World.Set(x, yy, z, Mat.Air);
            }
            u.PaveEffort -= 1;
            u.Swing = 1;
            if ((TownWaysLaid++ & 7) == 0) Worked?.Invoke(x, top, z, m, false);
        }
        return true;
    }

    /// <summary>Whether marked work stands that this worker does and could go to now: something there it is allowed, the stores holding what it is laid of, not found out of reach lately.</summary>
    bool MarkedWorkFor(Unit u)
    {
        int mask = Work.Allowed(u) & Work.Marked;
        foreach (int col in Orders.Active)
        {
            if (!HasWork(col, mask) || !Workable(col, mask) || _claims[col] >= 2) continue;
            if (u.Unreachable.TryGetValue(col, out double until) && Time < until) continue;
            return true;
        }
        return false;
    }

    /// <summary>A cell to stand in by a cell of the town's ways: in it, at the height of its ground.</summary>
    int TownStand(int ci, int ck, List<int> cols, int h)
    {
        int p = cols[cols.Count / 2], x = p % World.SX, z = p / World.SX;
        int cy = World.SurfAt(x, z) >> NavGrid.Shift;
        for (int dy = 0; dy <= 1; dy++)
            foreach (int y in new[] { cy + dy, cy - dy })
                if (Nav.Stand(ci, y, ck, h)) return Nav.Id(ci, y, ck);
        return NearestStand(x, z, h);
    }
}
