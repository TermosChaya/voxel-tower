using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>A step of a way on the big map: the hex, and the day it is reached (0 today, 1 tomorrow…), for the arrows drawn.</summary>
public readonly struct HexStep
{
    public readonly int Col, Row, Day;
    public HexStep(int col, int row, int day) { Col = col; Row = row; Day = day; }
}

public sealed partial class MapParty
{
    /// <summary>Its moves on the big map as «Heroes» counts them: what is left of today's, and a whole day's.</summary>
    public int MovesLeft, MovesMax;
    /// <summary>The week it last went by stables (their moves its till the week is out), −1 never.</summary>
    public int StablesWeek = -1;
    /// <summary>The hexes of its way still to walk, with the day each is reached (MapWalk.cs).</summary>
    public List<HexStep> Steps = new();
    /// <summary>Waiting for the dawn: today's moves spent short of the next step.</summary>
    public bool Tired;
    /// <summary>The moves the step it is taking cost, paid as it set out on it.</summary>
    public int StepCost;

    public int Col => Q + (R - (R & 1)) / 2;
    public int Row => R;
}

/// <summary>
/// Walking the big map as in «Heroes of Might and Magic III»: a hero has so many moves a day (1500, by the slowest of his
/// army; riders alone more, engines and giants fewer; Поход and the stables add), each hex costs by its ground — 100
/// over grass, dirt and burnt land, 125 rough, 150 sand and snow, 175 swamp and shallows; a wood 50 more, hills 25 —
/// and 50 along a road, a ford 50 more where a river crosses with no road. Mountains and deep water are not walked. A
/// step is paid as it is set out on; with too little left for the next, he waits for the dawn, when his moves are whole
/// again. A hundred moves take 30 seconds of the map's clock to walk, so a day's are spent well within the day.
/// </summary>
public static class MapWalk
{
    public const int Moves = 1500, RiderMoves = 2000, SlowMoves = 1300, ScoutMoves = 1800;
    public const int RoadCost = 50, FordCost = 50;
    /// <summary>What the search guesses a hex still to go costs.</summary>
    const int Guess = 90;
    /// <summary>Seconds of the map's clock a hundred moves take.</summary>
    public const double SecondsPer100 = 30;

    /// <summary>The moves a hex costs to walk into, by its ground and what stands on it.</summary>
    public static int Ground(MapCell b)
    {
        int cost = b.Ground switch
        {
            HexGround.Rough => 125, HexGround.Sand or HexGround.Snow => 150, HexGround.Swamp or HexGround.Water => 175, _ => 100,
        };
        if (b.Relief == HexRelief.Forest) cost += 50; else if (b.Relief == HexRelief.Hills) cost += 25;
        return cost;
    }

    /// <summary>The moves a step costs from a hex into its neighbour across a side: along a road both ends share 50, else by the ground, a ford more.</summary>
    public static int Cost(MapCell a, MapCell b, int side)
    {
        if (b == null || !b.Passable) return int.MaxValue;
        if (side >= 0 && (a.Roads >> side & 1) != 0 && (b.Roads >> (side + 3) % 6 & 1) != 0) return RoadCost;
        return Ground(b) + (b.Rivers != 0 ? FordCost : 0);
    }

    /// <summary>A day's moves of a party: by the slowest of it, a hero's Поход the more, the stables' this week.</summary>
    public static int MaxMoves(MapParty p, int week)
    {
        bool riders = p.Party.Members.Count > 0, slow = false;
        foreach (var m in p.Party.Members)
        {
            if (!m.Kind.Mounted) riders = false;
            if (Kinds.IsEngine(m.Kind) || m.Kind == Kinds.Giant || m.Kind == Kinds.HillGiant || m.Kind == Kinds.TameTroll) slow = true;
        }
        int n = slow ? SlowMoves : p.Scouting ? ScoutMoves : riders ? RiderMoves : Moves;
        n = (int)Math.Round(n / Heroes.March(p.Party.Commander?.Hero));
        if (p.StablesWeek == week) n += Campaign.StablesMoves;
        return n;
    }

    /// <summary>Seconds of the map's clock a step of so many moves takes to walk.</summary>
    public static double Seconds(int cost) => cost / 100.0 * SecondsPer100;

    /// <summary>
    /// The way for a party of a side over the big map to a hex: the hexes after the one it is in, the last where it goes;
    /// empty if it is there, null if no way. It goes round what is kept (beasts, the dead, the other side's towns) and the
    /// other side's parties standing, but into them where it goes.
    /// </summary>
    public static List<MapCell> Find(Campaign c, MapCell from, MapCell to, Owner side)
    {
        if (from == null || to == null || !c.Big) return null;
        var way = new List<MapCell>();
        if (from == to) return way;
        if (!to.Passable) return null;
        int n = c.Width * c.Height, goal = c.IndexOf(to), start = c.IndexOf(from);
        // (the buffers kept from search to search, a search's own marked by its stamp)
        var buf = c.WalkBuffers(n);
        int stamp = ++buf.Stamp;
        var cost = buf.Cost; var back = buf.Back; var mark = buf.Mark;
        int Got(int i) => mark[i] >= stamp ? cost[i] : int.MaxValue;
        mark[start] = stamp; cost[start] = 0;
        var open = buf.Open;
        open.Clear();
        open.Enqueue(start, 0);
        var blocked = c.KeptFor(side);
        while (open.TryDequeue(out int ai, out _))
        {
            if (ai == goal) break;
            if (buf.Done[ai] == stamp) continue;
            buf.Done[ai] = stamp;
            var a = c.Cell(ai % c.Width, ai / c.Width);
            for (int s = 0; s < 6; s++)
            {
                var b = c.Neighbour(a, s);
                if (b == null) continue;
                int bi = c.IndexOf(b);
                if (buf.Done[bi] == stamp || bi != goal && blocked.Contains(bi)) continue;
                int step = Cost(a, b, s);
                if (step == int.MaxValue) continue;
                // (the horde fights its way through the beasts and the dead that keep a hex nobody holds, as a day's walk)
                if (side == Owner.Horde && b.Owner == Owner.None && b.Sum.Keep.Count > 0 && bi != goal) step += Moves;
                int g = cost[ai] + step;
                if (g >= Got(bi)) continue;
                mark[bi] = stamp; cost[bi] = g; back[bi] = ai;
                // (a little over a road's cost a hex: the way found quickly, near enough the cheapest)
                int h = Campaign.Distance(b.Q, b.R, to.Q, to.R) * Guess;
                open.Enqueue(bi, g + h);
            }
        }
        if (Got(goal) == int.MaxValue) return null;
        for (int x = goal; x != start; x = back[x]) way.Add(c.Cell(x % c.Width, x / c.Width));
        way.Reverse();
        return way;
    }

    /// <summary>
    /// The ways out of one hex to everywhere within so many moves (one search for all of them, as the horde weighs its
    /// targets): a hex kept against the side is reached but not walked through. Read with <see cref="SpreadWay"/> before
    /// the next search.
    /// </summary>
    public static int Spread(Campaign c, MapCell from, Owner side, int most)
    {
        int n = c.Width * c.Height, start = c.IndexOf(from);
        var buf = c.WalkBuffers(n);
        int stamp = ++buf.Stamp;
        var cost = buf.Cost; var back = buf.Back; var mark = buf.Mark;
        mark[start] = stamp; cost[start] = 0; back[start] = -1;
        var open = buf.Open;
        open.Clear();
        open.Enqueue(start, 0);
        var blocked = c.KeptFor(side);
        while (open.TryDequeue(out int ai, out int ag))
        {
            if (buf.Done[ai] == stamp || ag > cost[ai]) continue;
            buf.Done[ai] = stamp;
            if (ai != start && blocked.Contains(ai)) continue;
            var a = c.Cell(ai % c.Width, ai / c.Width);
            for (int s = 0; s < 6; s++)
            {
                var b = c.Neighbour(a, s);
                if (b == null) continue;
                int bi = c.IndexOf(b);
                if (buf.Done[bi] == stamp) continue;
                int step = Cost(a, b, s);
                if (step == int.MaxValue) continue;
                if (side == Owner.Horde && b.Owner == Owner.None && b.Sum.Keep.Count > 0) step += Moves;
                int g = cost[ai] + step;
                if (g > most || mark[bi] >= stamp && g >= cost[bi]) continue;
                mark[bi] = stamp; cost[bi] = g; back[bi] = ai;
                open.Enqueue(bi, g);
            }
        }
        return stamp;
    }

    /// <summary>The way to a hex out of the last <see cref="Spread"/> (its stamp), or null if it was not reached.</summary>
    public static List<MapCell> SpreadWay(Campaign c, int stamp, MapCell to)
    {
        var buf = c.WalkBuffers(c.Width * c.Height);
        int i = c.IndexOf(to);
        if (buf.Stamp != stamp || buf.Mark[i] < stamp) return null;
        var way = new List<MapCell>();
        for (int x = i; buf.Back[x] >= 0; x = buf.Back[x]) way.Add(c.Cell(x % c.Width, x / c.Width));
        way.Reverse();
        return way;
    }

    /// <summary>The hexes a party of a side does not walk through on its way elsewhere: those kept against it, and the other side's parties.</summary>
    internal static HashSet<int> Kept(Campaign c, Owner side)
    {
        var set = new HashSet<int>();
        foreach (var cell in c.Cells)
        {
            if (cell.Sum.Keep.Count == 0 || cell.Owner == side || side == Owner.Horde && cell.Owner == Owner.None) continue;
            set.Add(c.IndexOf(cell));
        }
        foreach (var p in c.Parties)
            if (p.Owner != side && !p.Moving && c.At(p.Q, p.R) is { } at) set.Add(c.IndexOf(at));
        return set;
    }

    /// <summary>
    /// The way a party of ours would walk to a hex, with the day each hex of it is reached: today while its moves last,
    /// then a whole day's each day after. Null if there is no way.
    /// </summary>
    public static List<HexStep> Plan(Campaign c, MapParty p, int col, int row)
    {
        var to = c.Cell(col, row);
        var way = Find(c, c.At(p.Q, p.R), to, p.Owner);
        return way == null ? null : Days(c, p, way);
    }

    /// <summary>The steps of a way with their days, from where a party stands and its moves now.</summary>
    public static List<HexStep> Days(Campaign c, MapParty p, List<MapCell> way)
    {
        var steps = new List<HexStep>(way.Count);
        int left = p.MovesLeft, day = 0, max = Math.Max(1, p.MovesMax > 0 ? p.MovesMax : MaxMoves(p, c.Week));
        bool fresh = left >= max;
        var a = c.At(p.Q, p.R);
        // (the step it is on is paid already)
        int i0 = p.Progress > 0 && p.StepCost > 0 ? 1 : 0;
        for (int i = 0; i < way.Count; i++)
        {
            var b = way[i];
            if (i >= i0)
            {
                int cost = Cost(a, b, MapPath.Side(a, b));
                if (cost > left && !fresh) { day++; left = max; }
                left = Math.Max(0, left - cost);
                fresh = false;
            }
            steps.Add(new HexStep(b.Col, b.Row, day));
            a = b;
        }
        return steps;
    }

    /// <summary>A party's steps as it walks now.</summary>
    public static List<HexStep> StepsOf(Campaign c, MapParty p) => p.Moving ? Days(c, p, p.Path) : new List<HexStep>();
}

/// <summary>The big map's ways' buffers, kept by the campaign from search to search.</summary>
sealed class WalkBuffers
{
    public int[] Cost, Back, Mark, Done;
    public int Stamp;
    public readonly PriorityQueue<int, int> Open = new();
}

public sealed partial class Campaign
{
    WalkBuffers _walk;
    HashSet<int> _keptOurs, _keptHorde;
    /// <summary>The horde deciding at a dawn: the hexes kept gathered once for all its searches.</summary>
    bool _cacheKept;

    internal WalkBuffers WalkBuffers(int n)
    {
        if (_walk == null || _walk.Cost.Length != n) _walk = new WalkBuffers { Cost = new int[n], Back = new int[n], Mark = new int[n], Done = new int[n] };
        return _walk;
    }

    /// <summary>The hexes kept against a side: gathered anew for each search but while the horde decides.</summary>
    internal HashSet<int> KeptFor(Owner side)
    {
        if (!_cacheKept) return MapWalk.Kept(this, side);
        return side == Owner.Horde ? _keptHorde ??= MapWalk.Kept(this, Owner.Horde) : _keptOurs ??= MapWalk.Kept(this, side);
    }

    /// <summary>The horde's dawn with the kept hexes gathered once.</summary>
    void HordeDawnCached()
    {
        _cacheKept = true;
        try { HordeDawn(); }
        finally { _cacheKept = false; _keptOurs = _keptHorde = null; _wide = null; }
    }
}
