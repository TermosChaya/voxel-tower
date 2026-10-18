using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>How a body moves: its height, how high it steps, how far it drops, how fast it goes, hacks and builds.</summary>
public sealed class Mover
{
    /// <summary>Cells (metres) of headroom: 2 for a man or a goblin, 3 for a troll; climb and drop in cells too.</summary>
    public int Height = 4, Climb = 1, Drop = 4;
    /// <summary>Cells (metres) per second on the flat.</summary>
    public float Speed = 3;
    /// <summary>Voxels per second hacked out of earth, wood, stone and rock; null if it cannot hack at all.</summary>
    public float[] Carve;
    /// <summary>How fast it raises siege works (ladders, bridges, fills) next to a sapper's 1; 0 if it never builds them.</summary>
    public float Build;

    /// <summary>Seconds to hack a blocked cell open, or infinity.</summary>
    public float CarveTime(NavGrid g, int c)
    {
        int fill = g.Fill[c];
        if (fill < NavGrid.SolidT) return 0;
        if (g.Hard[c] != 0 || Carve == null) return float.PositiveInfinity;
        float t = 0; int n = 0;
        if (g.Earth[c] > 0) { if (Carve[0] <= 0) return float.PositiveInfinity; t += g.Earth[c] / Carve[0]; n += g.Earth[c]; }
        if (g.Wood[c] > 0) { if (Carve[1] <= 0) return float.PositiveInfinity; t += g.Wood[c] / Carve[1]; n += g.Wood[c]; }
        if (g.Stone[c] > 0) { if (Carve[2] <= 0) return float.PositiveInfinity; t += g.Stone[c] / Carve[2]; n += g.Stone[c]; }
        if (g.Rock[c] > 0) { if (Carve[3] <= 0) return float.PositiveInfinity; t += g.Rock[c] / Carve[3]; n += g.Rock[c]; }
        // hack until the cell no longer blocks, not to the last voxel
        float share = (fill - (NavGrid.SolidT - 24)) / (float)Math.Max(1, n);
        return t * Math.Clamp(share, 0, 1) + .3f;
    }
}

/// <summary>Siege works the horde raises on its way: what each costs in labour and in material.</summary>
public enum Works : byte { None, Ladder, Bridge, Fill }

public static class Siege
{
    /// <summary>Work units for a cell of ladder, a cell of bridge deck, one cell of fill; a sapper does <see cref="Rate"/> a second.</summary>
    public const float LadderWork = 240, BridgeWork = 320, FillWork = 540;
    /// <summary>Voxels of timber or earth each of those takes.</summary>
    public const int LadderWood = 24, BridgeWood = 128, FillEarth = 512;
    /// <summary>A sapper's work a second, kept apart from the pawns' own so their pace can change without the horde's.</summary>
    public const float Rate = 180;

    public static float Seconds(float work, Mover m) => m.Build > 0 ? work / (Rate * m.Build) : float.PositiveInfinity;
}

/// <summary>A* over the cells pawns can be in, for those who walk around obstacles instead of through them.</summary>
public sealed class PathFinder
{
    readonly NavGrid _g;
    readonly float[] _cost;
    readonly int[] _from, _seen, _closed;
    int _gen;
    readonly PriorityQueue<int, float> _open = new();

    static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 }, DK = { 0, 0, 1, -1, 1, -1, 1, -1 };

    public PathFinder(NavGrid g)
    {
        _g = g;
        _cost = new float[g.Count]; _from = new int[g.Count]; _seen = new int[g.Count]; _closed = new int[g.Count];
    }

    /// <summary>How much more a step into a cell counts than on dry open ground: wading, or picking a way among stakes.</summary>
    public const float AmongStakes = 6;
    static float Going(NavGrid g, int c) => (g.Water[c] > 0 ? FlowField.Wade : 1) * (g.Spikes[c] > 0 ? AmongStakes : 1);

    /// <summary>
    /// Cells from start (exclusive) to the first cell that satisfies goal, or null.
    /// The heuristic should not overestimate; cost is in cells walked. Ladders are climbed, slowly.
    /// A lane adds a little to every cell entered (off the line one keeps to in a group), so of ways that cost
    /// the same each walker takes its own.
    /// </summary>
    public List<int> Find(int start, Func<int, bool> goal, Func<int, float> heuristic, Mover m, int maxExpand = 40000, bool gates = false,
        Func<int, float> lane = null)
    {
        _gates = gates;
        _lane = lane;
        var g = _g;
        _gen++;
        _open.Clear();
        _cost[start] = 0; _from[start] = -1; _seen[start] = _gen;
        _open.Enqueue(start, heuristic(start));
        int expanded = 0;
        while (_open.TryDequeue(out int c, out _))
        {
            if (_closed[c] == _gen) continue;
            _closed[c] = _gen;
            if (goal(c)) return Trace(c, start);
            if (++expanded > maxExpand) return null;
            int i = g.I(c), y = g.Y(c), k = g.K(c);
            bool standing = g.Solid(i, y - 1, k, gates);
            for (int dir = 0; dir < 8; dir++)
            {
                int ni = i + DX[dir], nk = k + DK[dir];
                bool diag = dir >= 4;
                if (diag)
                {
                    // no cutting corners: both sides must be walkable at this level, and only on the ground
                    if (!standing || !g.Stand(ni, y, nk, m.Height, gates) || !g.Stand(i + DX[dir], y, k, m.Height, gates) || !g.Stand(i, y, k + DK[dir], m.Height, gates)) continue;
                    int dn = g.Id(ni, y, nk);
                    Relax(c, dn, 1.414f * Going(g, dn), heuristic);
                    continue;
                }
                // level first, then a step up, then the shortest drop; onto ground or onto a ladder,
                // and never a step higher than the legs manage, whatever the cells say
                for (int s = 0; s <= m.Climb + m.Drop; s++)
                {
                    int dy = s <= m.Climb ? s : m.Climb - s;
                    int ny = y + dy;
                    if (!g.CanBe(ni, ny, nk, m.Height, gates)) continue;
                    if (!FlowField.StepsOk(m, g.Floor[c], g.Floor[g.Id(ni, ny, nk)])) continue;
                    if (dy > 0 && !OpenColumn(i, k, y + m.Height, y + m.Height + dy - 1)) continue;
                    if (dy < 0 && !OpenColumn(ni, nk, ny + m.Height, y + m.Height - 1)) continue;
                    int sn = g.Id(ni, ny, nk);
                    Relax(c, sn, (1 + (dy > 0 ? .6f * dy : -.3f * dy)) * Going(g, sn), heuristic);
                    break;
                }
            }
            // up and down a ladder
            if (y + 1 < g.NY && (g.Ladder[c] != 0 || g.Ladder[c + g.Layer] != 0) && g.CanBe(i, y + 1, k, m.Height, gates))
                Relax(c, c + g.Layer, 1.8f, heuristic);
            if (y >= 2 && (g.Ladder[c] != 0 || g.Ladder[c - g.Layer] != 0) && g.CanBe(i, y - 1, k, m.Height, gates))
                Relax(c, c - g.Layer, 1.4f, heuristic);
        }
        return null;
    }

    bool _gates;
    Func<int, float> _lane;

    bool OpenColumn(int i, int k, int y0, int y1)
    {
        for (int y = y0; y <= y1; y++) if (!_g.Open(i, y, k, _gates)) return false;
        return true;
    }

    void Relax(int from, int to, float step, Func<int, float> heuristic)
    {
        if (_closed[to] == _gen) return;
        float c = _cost[from] + step + (_lane != null ? _lane(to) : 0);
        if (_seen[to] == _gen && c >= _cost[to]) return;
        _seen[to] = _gen; _cost[to] = c; _from[to] = from;
        _open.Enqueue(to, c + heuristic(to));
    }

    List<int> Trace(int end, int start)
    {
        var path = new List<int>();
        for (int c = end; c != start && c >= 0; c = _from[c]) path.Add(c);
        path.Reverse();
        return path;
    }

    /// <summary>Straight-line distance in cells, for heuristics toward a target cell column.</summary>
    public static float Octile(int di, int dk)
    {
        di = Math.Abs(di); dk = Math.Abs(dk);
        return Math.Max(di, dk) + .414f * Math.Min(di, dk);
    }
}

/// <summary>
/// Seconds from every cell to the citadel for one kind of attacker, hacking through what is in the way and building
/// what it lacks: a ladder up a wall face or a cliff, a bridge over a moat or a river, a fill in a shallow ditch.
/// Built by a reverse Dijkstra from the cells beside the citadel; attackers walk downhill in it.
/// </summary>
public sealed class FlowField
{
    public readonly float[] Dist;
    public readonly Mover M;
    public readonly int Version;
    /// <summary>Whether the horde had timber (for ladders and bridges) and earth (for fills) when this was built.</summary>
    public readonly bool Wood, Earth;

    FlowField(int n, Mover m, int version, bool wood, bool earth) { Dist = new float[n]; M = m; Version = version; Wood = wood; Earth = earth; }

    static readonly int[] DX = { 1, -1, 0, 0 }, DK = { 0, 0, 1, -1 };

    // what a cell is to the mover; several may hold at once
    const int Ground = 1, OnLadder = 2, CouldLadder = 4, CouldDeck = 8;

    /// <summary>Cells beside the citadel where attackers stand to batter it.</summary>
    public static List<int> Goals(NavGrid g)
    {
        var goals = new List<int>();
        for (int c = 0; c < g.Count; c++)
        {
            if (g.Keep[c] == 0) continue;
            int i = g.I(c), y = g.Y(c), k = g.K(c);
            for (int d = 0; d < 4; d++)
            {
                int ni = i + DX[d], nk = k + DK[d];
                if (!g.In(ni, y, nk)) continue;
                int n = g.Id(ni, y, nk);
                if (g.Keep[n] == 0 && g.Fill[n] < NavGrid.SolidT && g.Solid(ni, y - 1, nk) && !goals.Contains(n)) goals.Add(n);
            }
        }
        return goals;
    }

    public static FlowField Compute(NavGrid g, Mover m, List<int> goals, float maxCost = 600, bool wood = false, bool earth = false)
    {
        var f = new FlowField(g.Count, m, g.Version, wood, earth);
        var dist = f.Dist;
        Array.Fill(dist, float.PositiveInfinity);
        var open = new PriorityQueue<int, float>();
        foreach (int c in goals) { dist[c] = 0; open.Enqueue(c, 0); }
        void Relax(int p, float nd)
        {
            if (nd < dist[p]) { dist[p] = nd; open.Enqueue(p, nd); }
        }
        while (open.TryDequeue(out int q, out float dq))
        {
            if (dq > dist[q] || dq > maxCost) continue;
            int qi = g.I(q), qy = g.Y(q), qk = g.K(q);
            int tq = f.Kind(g, qi, qy, qk);
            if (tq == 0) continue;
            // p steps into q from beside it: from up to Climb cells lower, or drops from up to Drop cells higher
            for (int d = 0; d < 4; d++)
            {
                int pi = qi - DX[d], pk = qk - DK[d];
                for (int py = qy - m.Climb; py <= qy + m.Drop; py++)
                {
                    if (!g.In(pi, py, pk) || py < 1) continue;
                    int p = g.Id(pi, py, pk);
                    if (g.Hard[p] != 0) continue;
                    int tp = f.Kind(g, pi, py, pk);
                    if (tp == 0) continue;
                    float cost = f.Move(g, pi, py, pk, qi, qy, qk, tq, out _);
                    if (!float.IsPositiveInfinity(cost)) Relax(p, dq + cost);
                }
            }
            // p climbs up into q
            if ((tq & (OnLadder | CouldLadder)) != 0 && qy >= 2)
            {
                int tp = f.Kind(g, qi, qy - 1, qk);
                if ((tp & (Ground | OnLadder | CouldLadder)) != 0) Relax(q - g.Layer, dq + f.ClimbUp(tq, out _));
            }
            // p comes down a ladder into q
            if ((tq & OnLadder) != 0 && qy + 1 < g.NY && (f.Kind(g, qi, qy + 1, qk) & OnLadder) != 0)
                Relax(q + g.Layer, dq + 1.4f / m.Speed);
        }
        return f;
    }

    /// <summary>What a cell is to this mover: ground to stand on, a ladder to hang on, a place to raise a ladder or lay a deck.</summary>
    int Kind(NavGrid g, int i, int y, int k)
    {
        if (!g.In(i, y, k) || y < 1) return 0;
        int c = g.Id(i, y, k);
        if (g.Water[c] >= NavGrid.DeepT) return 0;
        if (g.Solid(i, y - 1, k)) return Ground;
        for (int d = 0; d < M.Height; d++) if (!g.Open(i, y + d, k)) return 0;
        int kind = g.Ladder[c] != 0 ? OnLadder : 0;
        if (M.Build <= 0) return kind;
        if (Wood && kind == 0 && Leans(g, i, y, k)) kind |= CouldLadder;
        int gap = GapBelow(g, i, y, k);
        if (gap > 0 && (Wood || (Earth && gap <= MaxFill))) kind |= CouldDeck;
        return kind;
    }

    /// <summary>Something solid beside the cell to lean a ladder on (not the citadel: that is battered, not climbed).</summary>
    static bool Leans(NavGrid g, int i, int y, int k)
    {
        for (int d = 0; d < 4; d++)
        {
            int ni = i + DX[d], nk = k + DK[d];
            if (!g.In(ni, y, nk)) continue;
            int n = g.Id(ni, y, nk);
            if (g.Fill[n] >= NavGrid.SolidT && g.Keep[n] == 0) return true;
        }
        return false;
    }

    /// <summary>A ditch up to this many cells deep can be filled with earth; deeper ones are bridged.</summary>
    public const int MaxFill = 3;

    /// <summary>
    /// How many open cells lie between this one and a bottom close below (a moat, a ditch), or 9 over water;
    /// 0 when there is no bottom within six metres.
    /// </summary>
    public static int GapBelow(NavGrid g, int i, int y, int k)
    {
        for (int d = 1; d <= 6; d++)
        {
            int yy = y - d;
            if (yy < 0) return d - 1;
            int c = g.Id(i, yy, k);
            if (g.Fill[c] >= NavGrid.SolidT) return d - 1;
            if (g.Water[c] > 0) return 9;
        }
        return 0;
    }

    /// <summary>Seconds to step from p into q beside it, hacking what is in the way or laying a deck under q.</summary>
    float Move(NavGrid g, int pi, int py, int pk, int qi, int qy, int qk, int tq, out Works works)
    {
        works = Works.None;
        int dy = qy - py;
        if (dy > M.Climb || -dy > M.Drop) return float.PositiveInfinity;
        if ((tq & Ground) != 0) return Step(g, M, pi, py, pk, qi, qy, qk);
        float walk = (1 + (dy > 0 ? .6f * dy : -.3f * dy)) / M.Speed;
        int fp = Feet(g, pi, py, pk);
        if ((tq & OnLadder) != 0) return StepsOk(M, fp, g.Floor[g.Id(qi, qy, qk)]) ? walk : float.PositiveInfinity;
        if ((tq & CouldDeck) != 0 && dy == 0 && StepsOk(M, fp, qy << NavGrid.Shift)) return walk + DeckSeconds(g, qi, qy, qk, out works);
        return float.PositiveInfinity;
    }

    /// <summary>Where the feet are in a cell: on its floor, or at its bottom once whatever fills it is hacked away.</summary>
    static int Feet(NavGrid g, int i, int y, int k)
    {
        int c = g.Id(i, y, k);
        return g.Fill[c] >= NavGrid.SolidT ? y << NavGrid.Shift : g.Floor[c];
    }

    /// <summary>How much longer a step through standing or running water counts for, against one on dry land.</summary>
    public const float Wade = 3f;
    /// <summary>How much longer a step among stakes counts for the horde: it goes round if the way round is short.</summary>
    public const float Spiked = 2.5f;

    /// <summary>A step the legs manage, in voxels: up a little over a cell per Climb, down as far as Drop allows.</summary>
    public static bool StepsOk(Mover m, int from, int to) =>
        to - from <= m.Climb * NavGrid.CS + 2 && from - to <= m.Drop * NavGrid.CS + 6;

    float ClimbUp(int tq, out Works works)
    {
        works = (tq & OnLadder) != 0 ? Works.None : Works.Ladder;
        return 1.8f / M.Speed + (works == Works.Ladder ? Siege.Seconds(Siege.LadderWork, M) : 0);
    }

    /// <summary>The cheaper way to put a floor under q: planks across, or earth heaped up from a shallow bottom.</summary>
    float DeckSeconds(NavGrid g, int i, int y, int k, out Works works)
    {
        int gap = GapBelow(g, i, y, k);
        float bridge = Wood ? Siege.Seconds(Siege.BridgeWork, M) : float.PositiveInfinity;
        float fill = Earth && gap <= MaxFill ? Siege.Seconds(Siege.FillWork, M) * gap : float.PositiveInfinity;
        works = fill <= bridge ? Works.Fill : Works.Bridge;
        return Math.Min(bridge, fill);
    }

    /// <summary>Seconds to go from p to its neighbour q on the ground, hacking q's column (and headroom when climbing).</summary>
    public static float Step(NavGrid g, Mover m, int pi, int py, int pk, int qi, int qy, int qk)
    {
        int dy = qy - py;
        if (dy > m.Climb || -dy > m.Drop || !g.Solid(qi, qy - 1, qk)) return float.PositiveInfinity;
        if (!StepsOk(m, Feet(g, pi, py, pk), Feet(g, qi, qy, qk))) return float.PositiveInfinity;
        float t = 0;
        for (int y = qy; y < qy + m.Height; y++)
        {
            if (y >= g.NY) break;
            int c = g.Id(qi, y, qk);
            if (g.Keep[c] != 0) return float.PositiveInfinity;
            t += m.CarveTime(g, c);
            if (float.IsPositiveInfinity(t)) return t;
        }
        if (dy > 0)
            for (int y = py + m.Height; y < py + m.Height + dy && y < g.NY; y++) t += m.CarveTime(g, g.Id(pi, y, pk));
        // falling a long way hurts and the horde avoids it a little; wading is slow and nobody does it if a bridge is near
        float walk = (1 + (dy > 0 ? .6f * dy : -.3f * dy)) / m.Speed + (dy <= -4 ? 2 : 0);
        if (g.Water[g.Id(qi, qy, qk)] > 0) walk *= Wade;
        if (g.Spikes[g.Id(qi, qy, qk)] > 0) walk *= Spiked;
        return t + walk;
    }

    /// <summary>
    /// The cell to head for from c, whether getting there means hacking (and how long), and what has to be built first.
    /// </summary>
    public int Next(NavGrid g, int c, out float carve, out Works works)
    {
        carve = 0; works = Works.None;
        // the cheapest move: the one this cell's distance came through costs exactly as much,
        // and when the valley has changed since the field was built the best available is still the way to go,
        // so long as it leads downhill in the field: a step back up it, in a field gone stale where a wall face was
        // hacked half open, would only be stepped back down again, to and fro
        int i = g.I(c), y = g.Y(c), k = g.K(c), best = -1, up = -1;
        int tc = Kind(g, i, y, k);
        if (tc == 0) tc = Ground;   // standing in its own tunnel face, or on what it just built
        float bestD = float.PositiveInfinity, upD = float.PositiveInfinity, here = Dist[c];
        Works bestWorks = Works.None, upWorks = Works.None;
        void Offer(int q, float total, Works w)
        {
            if (Dist[q] < here) { if (total < bestD) { bestD = total; best = q; bestWorks = w; } }
            else if (total < upD) { upD = total; up = q; upWorks = w; }
        }
        for (int d = 0; d < 4; d++)
        {
            int qi = i + DX[d], qk = k + DK[d];
            for (int qy = y - M.Drop; qy <= y + M.Climb; qy++)
            {
                if (!g.In(qi, qy, qk) || qy < 1) continue;
                int q = g.Id(qi, qy, qk);
                if (float.IsPositiveInfinity(Dist[q])) continue;
                int tq = Kind(g, qi, qy, qk);
                if (tq == 0) continue;
                float s = Move(g, i, y, k, qi, qy, qk, tq, out var w);
                if (!float.IsPositiveInfinity(s)) Offer(q, s + Dist[q], w);
            }
        }
        if (y + 1 < g.NY && (tc & (Ground | OnLadder | CouldLadder)) != 0)
        {
            int q = c + g.Layer, tq = Kind(g, i, y + 1, k);
            if ((tq & (OnLadder | CouldLadder)) != 0 && !float.IsPositiveInfinity(Dist[q]))
            {
                float s = ClimbUp(tq, out var w);
                Offer(q, s + Dist[q], w);
            }
        }
        if (y >= 2 && (tc & OnLadder) != 0 && (Kind(g, i, y - 1, k) & OnLadder) != 0 && !float.IsPositiveInfinity(Dist[c - g.Layer]))
            Offer(c - g.Layer, 1.4f / M.Speed + Dist[c - g.Layer], Works.None);
        // nothing downhill at all (a hollow the stale field never knew): the best way there is
        if (best < 0) { best = up; bestWorks = upWorks; }
        works = bestWorks;
        if (best >= 0 && works == Works.None && g.Solid(g.I(best), g.Y(best) - 1, g.K(best)))
        {
            int qy = g.Y(best);
            for (int yy = qy; yy < qy + M.Height && yy < g.NY; yy++) carve += M.CarveTime(g, g.Id(g.I(best), yy, g.K(best)));
        }
        return best;
    }
}
