using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The ways over the map of valleys. A party crosses a cell in a third of a day on open ground, slower through the woods,
/// the river's wet meadows and the mountains; along a road it goes faster, over a river without one it fords. Riders
/// alone go faster, a party dragging engines or a giant slower. The way is found cell by cell (A*), the cells of the
/// other side kept clear of unless they are where the party is going.
/// </summary>
public static class MapPath
{
    /// <summary>An hour of the map's day: a day and its night (<see cref="DayClock.Cycle"/>) are 24 of them.</summary>
    public const double HourSeconds = DayClock.Cycle / 24;
    /// <summary>A road along the way: so much of the time it takes. A river crossed without one: hours more, to ford it.</summary>
    public const double RoadShare = .6, FordHours = 2;
    /// <summary>What a cell of the other side on the way costs, hours: a party goes round it if it can.</summary>
    const double HostileHours = 24;

    /// <summary>The hours a party on foot takes to cross a cell, by its ground.</summary>
    public static double Hours(MapCell c) => c.Kind switch
    {
        ValleyKind.Marsh or ValleyKind.Mountain => 12,
        ValleyKind.Forest => 10,
        _ => 8,
    };

    /// <summary>
    /// The hours from one cell into its neighbour across a side: half of each cell's crossing, the less in a cell whose
    /// road runs to that side; and a ford's more over a river crossing the side where no road does.
    /// </summary>
    public static double StepHours(MapCell a, MapCell b, int side)
    {
        var ea = a.Edges[side]; var eb = b.Edges[(side + 3) % 6];
        bool ra = ea != null && ea.Road >= 0, rb = eb != null && eb.Road >= 0;
        double h = (Hours(a) * (ra ? RoadShare : 1) + Hours(b) * (rb ? RoadShare : 1)) / 2;
        if (!ra && !rb && (ea != null && ea.River >= 0 || eb != null && eb.River >= 0)) h += FordHours;
        return h;
    }

    /// <summary>How much slower (above 1) or faster a party goes than men on foot: riders alone faster, with engines or a giant slower.</summary>
    public static double Pace(MapParty p)
    {
        bool riders = p.Party.Members.Count > 0, slow = false;
        foreach (var m in p.Party.Members)
        {
            if (!m.Kind.Mounted) riders = false;
            if (Kinds.IsEngine(m.Kind) || m.Kind == Kinds.Giant || m.Kind == Kinds.HillGiant || m.Kind == Kinds.TameTroll) slow = true;
        }
        // and a hero at its head with Поход quicker
        return (slow ? 1.25 : p.Scouting ? Campaign.ScoutPace : riders ? .75 : 1) * Heroes.March(p.Party.Commander?.Hero);
    }

    /// <summary>Seconds of the map's time the party takes from one cell into the next.</summary>
    public static double StepSeconds(MapParty p, MapCell a, MapCell b)
    {
        int side = Side(a, b);
        return (side < 0 ? Hours(b) : StepHours(a, b, side)) * Pace(p) * HourSeconds;
    }

    /// <summary>The side of a cell its neighbour lies across; −1 if they are not neighbours.</summary>
    public static int Side(MapCell a, MapCell b)
    {
        for (int s = 0; s < 6; s++)
            if (a.Q + Campaign.Step[s].dq == b.Q && a.R + Campaign.Step[s].dr == b.R) return s;
        return -1;
    }

    /// <summary>
    /// The way for a party of a side from one cell to another: the cells it walks through after the first, the last of
    /// them where it is going; empty if it is there already, null if there is no way. Cells held by the other side cost
    /// a day more to cross unless they are where it goes.
    /// </summary>
    public static List<MapCell> Find(Campaign c, MapCell from, MapCell to, Owner side)
    {
        if (from == null || to == null) return null;
        var way = new List<MapCell>();
        if (from == to) return way;
        // the least a step can cost: two cells of open ground along a road
        const double least = 8 * RoadShare;
        var cost = new Dictionary<MapCell, double> { [from] = 0 };
        var back = new Dictionary<MapCell, MapCell>();
        var open = new PriorityQueue<MapCell, double>();
        var done = new HashSet<MapCell>();
        open.Enqueue(from, Campaign.Distance(from.Q, from.R, to.Q, to.R) * least);
        while (open.TryDequeue(out var a, out _))
        {
            if (a == to) break;
            if (!done.Add(a)) continue;
            for (int s = 0; s < 6; s++)
            {
                var b = c.Neighbour(a, s);
                if (b == null || done.Contains(b)) continue;
                double g = cost[a] + StepHours(a, b, s);
                if (b != to && Hostile(c, b, side)) g += HostileHours;
                if (cost.TryGetValue(b, out double had) && had <= g) continue;
                cost[b] = g; back[b] = a;
                open.Enqueue(b, g + Campaign.Distance(b.Q, b.R, to.Q, to.R) * least);
            }
        }
        if (!back.ContainsKey(to)) return null;
        for (var x = to; x != from; x = back[x]) way.Add(x);
        way.Reverse();
        return way;
    }

    /// <summary>The hours a way takes on foot, from a cell through the cells after it.</summary>
    public static double WayHours(MapCell from, List<MapCell> way)
    {
        double h = 0;
        var a = from;
        foreach (var b in way) { int s = Side(a, b); h += s < 0 ? Hours(b) : StepHours(a, b, s); a = b; }
        return h;
    }

    /// <summary>
    /// A cell the other side holds, for a party of this side; the valley we play in is the horde's to keep clear of on its
    /// way elsewhere, and so are the keepers of the map's treasures (Campaign.Troves.cs).
    /// </summary>
    static bool Hostile(Campaign c, MapCell b, Owner side) =>
        side == Owner.Ours ? b.Owner == Owner.Horde
            : side == Owner.Horde && (b.Owner == Owner.Ours || c.InValley && b == c.Here || Campaign.Untaken(b) && b.Sum.Keep.Count > 0);
}
