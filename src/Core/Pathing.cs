using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>How a body moves: its height, how high it steps, how far it drops, how fast it goes and hacks.</summary>
public sealed class Mover
{
    /// <summary>Cells: 4 is a man (2 m of headroom), 3 a goblin.</summary>
    public int Height = 4, Climb = 1, Drop = 4;
    /// <summary>Cells (half metres) per second on the flat.</summary>
    public float Speed = 3;
    /// <summary>Voxels per second hacked out of earth, wood, stone and rock; null if it cannot hack at all.</summary>
    public float[] Carve;

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

/// <summary>A* over standable cells for those who walk around obstacles instead of through them.</summary>
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

    /// <summary>
    /// Cells from start (exclusive) to the first cell that satisfies goal, or null.
    /// The heuristic should not overestimate; cost is in cells walked.
    /// </summary>
    public List<int> Find(int start, Func<int, bool> goal, Func<int, float> heuristic, Mover m, int maxExpand = 40000)
    {
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
            for (int dir = 0; dir < 8; dir++)
            {
                int ni = i + DX[dir], nk = k + DK[dir];
                bool diag = dir >= 4;
                if (diag)
                {
                    // no cutting corners: both sides must be walkable at this level
                    if (!g.Stand(ni, y, nk, m.Height) || !g.Stand(i + DX[dir], y, k, m.Height) || !g.Stand(i, y, k + DK[dir], m.Height)) continue;
                    Relax(c, g.Id(ni, y, nk), 1.414f, heuristic);
                    continue;
                }
                // level first, then a step up, then the shortest drop
                for (int s = 0; s <= m.Climb + m.Drop; s++)
                {
                    int dy = s <= m.Climb ? s : m.Climb - s;
                    int ny = y + dy;
                    if (!g.Stand(ni, ny, nk, m.Height)) continue;
                    if (dy > 0 && !OpenColumn(i, k, y + m.Height, y + m.Height + dy - 1)) continue;
                    if (dy < 0 && !OpenColumn(ni, nk, ny + m.Height, y + m.Height - 1)) continue;
                    Relax(c, g.Id(ni, ny, nk), 1 + (dy > 0 ? .6f * dy : -.3f * dy), heuristic);
                    break;
                }
            }
        }
        return null;
    }

    bool OpenColumn(int i, int k, int y0, int y1)
    {
        for (int y = y0; y <= y1; y++) if (!_g.Open(i, y, k)) return false;
        return true;
    }

    void Relax(int from, int to, float step, Func<int, float> heuristic)
    {
        if (_closed[to] == _gen) return;
        float c = _cost[from] + step;
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
/// Seconds from every cell to the citadel for one kind of attacker, hacking through what is in the way.
/// Built by a reverse Dijkstra from the cells beside the citadel; attackers walk downhill in it.
/// </summary>
public sealed class FlowField
{
    public readonly float[] Dist;
    public readonly Mover M;
    public readonly int Version;

    FlowField(int n, Mover m, int version) { Dist = new float[n]; M = m; Version = version; }

    static readonly int[] DX = { 1, -1, 0, 0 }, DK = { 0, 0, 1, -1 };

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

    public static FlowField Compute(NavGrid g, Mover m, List<int> goals, float maxCost = 600)
    {
        var f = new FlowField(g.Count, m, g.Version);
        var dist = f.Dist;
        Array.Fill(dist, float.PositiveInfinity);
        var open = new PriorityQueue<int, float>();
        foreach (int c in goals) { dist[c] = 0; open.Enqueue(c, 0); }
        while (open.TryDequeue(out int q, out float dq))
        {
            if (dq > dist[q] || dq > maxCost) continue;
            int qi = g.I(q), qy = g.Y(q), qk = g.K(q);
            for (int d = 0; d < 4; d++)
            {
                int pi = qi - DX[d], pk = qk - DK[d];
                // p steps into q: from up to Climb cells lower, or drops from up to Drop cells higher
                for (int py = qy - m.Climb; py <= qy + m.Drop; py++)
                {
                    if (!g.In(pi, py, pk) || py < 1 || !g.Solid(pi, py - 1, pk)) continue;
                    int p = g.Id(pi, py, pk);
                    if (g.Hard[p] != 0) continue;
                    float cost = Step(g, m, pi, py, pk, qi, qy, qk);
                    if (float.IsPositiveInfinity(cost)) continue;
                    float nd = dq + cost;
                    if (nd < dist[p]) { dist[p] = nd; open.Enqueue(p, nd); }
                }
            }
        }
        return f;
    }

    /// <summary>Seconds to go from p to its neighbour q, hacking q's column (and headroom when climbing).</summary>
    public static float Step(NavGrid g, Mover m, int pi, int py, int pk, int qi, int qy, int qk)
    {
        int dy = qy - py;
        if (dy > m.Climb || -dy > m.Drop || !g.Solid(qi, qy - 1, qk)) return float.PositiveInfinity;
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
        // falling a long way hurts and the horde avoids it a little
        float walk = (1 + (dy > 0 ? .6f * dy : -.3f * dy)) / m.Speed + (dy <= -4 ? 2 : 0);
        return t + walk;
    }

    /// <summary>The neighbour to head for from c, and whether getting there means hacking.</summary>
    public int Next(NavGrid g, int c, out float carve)
    {
        carve = 0;
        // the cheapest neighbour: the one this cell's distance came through costs exactly as much,
        // and when the valley has changed since the field was built the best available is still the way to go
        int i = g.I(c), y = g.Y(c), k = g.K(c), best = -1;
        float bestD = float.PositiveInfinity;
        for (int d = 0; d < 4; d++)
        {
            int qi = i + DX[d], qk = k + DK[d];
            for (int qy = y - M.Drop; qy <= y + M.Climb; qy++)
            {
                if (!g.In(qi, qy, qk) || qy < 1) continue;
                int q = g.Id(qi, qy, qk);
                float s = Step(g, M, i, y, k, qi, qy, qk);
                if (float.IsPositiveInfinity(s)) continue;
                float total = s + Dist[q];
                if (total < bestD) { bestD = total; best = q; }
            }
        }
        if (best >= 0)
        {
            int qy = g.Y(best);
            for (int yy = qy; yy < qy + M.Height && yy < g.NY; yy++) carve += M.CarveTime(g, g.Id(g.I(best), yy, g.K(best)));
        }
        return best;
    }
}
