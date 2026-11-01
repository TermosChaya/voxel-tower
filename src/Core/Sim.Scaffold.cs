using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>
/// Scaffolding: poles and boards lashed up a metre at a time, climbed like a ladder. Workers raise it for themselves,
/// out of the logs in the stores, and take it down again when it has served, the logs back in the stores:
/// <list type="bullet">
/// <item>shut in up on what they built — on a wall's top, an upper floor, a roof, inside a ring of wall gone up round
/// them — they lash a column of it down to the ground beside, or up the wall and down its far side, and climb out by it;
/// only with no logs to be had do they cut their way out (Sim.Escape.cs);</item>
/// <item>work marked higher than anyone can reach from where they can stand — the top of a tower, a high course of a
/// wall — they raise a column beside it from the ground and work from it.</item>
/// </list>
/// It is nobody's building: it costs nothing to the plans, is never mended or copied into a blueprint; a save keeps it
/// as it stands, and who was at it goes on with it. The horde climbs it as any ladder, so it is taken down as soon as a
/// raider comes near; it burns.
/// </summary>
public sealed class Scaffold
{
    /// <summary>The columns of it in the order they go up: the cell column, its lowest and highest cells, and whether it is lashed from the top down.</summary>
    public readonly List<(int i, int k, int y0, int y1, bool down)> Columns = new();
    /// <summary>The voxels of it standing, in the order they were laid.</summary>
    public readonly List<int> Voxels = new();
    /// <summary>Who raises it, and the cell to stand in to raise each column.</summary>
    public Unit By;
    public readonly List<int> From = new();
    public int Column, Next;
    /// <summary>To get its raiser out (else to reach work high up, the work's column).</summary>
    public bool Escape;
    public int Site = -1;
    /// <summary>Where one climbing out by it gets down to (for one put up to get out).</summary>
    public int Exit = -1;
    /// <summary>When it was finished; when it was last of use (somebody on it, or work in reach of it); being taken down.</summary>
    public double Done = -1, Used;
    public bool Lowering;
    public float Budget;
    /// <summary>The player marked it to be taken out: it comes down from the top as soon as nobody is on it.</summary>
    public bool Ordered;
    /// <summary>How many of its voxels stood when it was last looked over for what has lost its footing (−1: not yet).</summary>
    internal int Standing = -1;
}

public sealed partial class Sim
{
    public readonly List<Scaffold> Scaffolds = new();

    /// <summary>Voxels of scaffolding a worker lashes up a second, and takes down.</summary>
    const float ScaffoldPace = 40, UnscaffoldPace = 60;
    /// <summary>
    /// Seconds a scaffold stands with nobody on it and no work high up about it before it is taken down: long enough for
    /// the workers to fetch what the next course is laid of and come back to it.
    /// </summary>
    const double ScaffoldIdle = 45;
    /// <summary>How far about a scaffold work high up keeps it standing, cells: the next stretch of the same wall or tower is worked from it.</summary>
    const int ScaffoldServes = 4;

    double _scaffoldAt;

    /// <summary>The voxels of one column cell of scaffolding, bottom to top: a pole at each corner and a ring of boards at its middle.</summary>
    static IEnumerable<(int x, int y, int z)> Lattice(int i, int y, int k)
    {
        int x0 = i << NavGrid.Shift, y0 = y << NavGrid.Shift, z0 = k << NavGrid.Shift, e = NavGrid.CS - 1;
        for (int dy = 0; dy < NavGrid.CS; dy++)
        {
            yield return (x0, y0 + dy, z0); yield return (x0 + e, y0 + dy, z0);
            yield return (x0, y0 + dy, z0 + e); yield return (x0 + e, y0 + dy, z0 + e);
            if (dy != 3) continue;
            for (int d = 1; d < e; d++)
            {
                yield return (x0 + d, y0 + dy, z0); yield return (x0 + d, y0 + dy, z0 + e);
                yield return (x0, y0 + dy, z0 + d); yield return (x0 + e, y0 + dy, z0 + d);
            }
        }
    }

    /// <summary>The logs a scaffold takes, voxels: not begun without them all, lest it be lashed half up and taken down again.</summary>
    static int ScaffoldLogs(Scaffold s)
    {
        int n = 0;
        foreach (var c in s.Columns) n += ColumnVoxels(c).Count;
        return n;
    }

    /// <summary>The logs in the stores not owed yet to scaffolding going up, voxels: two begun at once on the same logs,
    /// one ran out of them halfway and came down again.</summary>
    double ScaffoldLogsFree()
    {
        double free = Stock[Stockpile.Wood];
        foreach (var s in Scaffolds) if (s.Done < 0 && !s.Lowering) free -= Math.Max(0, ScaffoldLogs(s) - s.Voxels.Count);
        return free;
    }

    /// <summary>The voxels of a column of scaffolding, in the order they go up.</summary>
    static List<(int x, int y, int z)> ColumnVoxels((int i, int k, int y0, int y1, bool down) c)
    {
        var all = new List<(int x, int y, int z)>();
        if (c.down) for (int y = c.y1; y >= c.y0; y--) all.AddRange(Lattice(c.i, y, c.k));
        else for (int y = c.y0; y <= c.y1; y++) all.AddRange(Lattice(c.i, y, c.k));
        return all;
    }

    /// <summary>Whether a scaffold stands in a cell column, or (<paramref name="rising"/>) is still going up about it.</summary>
    bool ScaffoldNear(int i, int k, int within, bool rising = false)
    {
        foreach (var s in Scaffolds)
        {
            if (rising && s.Done >= 0) continue;
            foreach (var c in s.Columns)
                if (Math.Abs(c.i - i) <= within && Math.Abs(c.k - k) <= within) return true;
        }
        return false;
    }

    /// <summary>A column can take scaffolding from its lowest cell to its highest: open all the way, nothing to be laid there, nobody in it.</summary>
    bool Scaffoldable(int i, int k, int y0, int y1)
    {
        if (i < 0 || k < 0 || i >= Nav.NX || k >= Nav.NZ || y0 < 1 || y1 >= Nav.NY - 2) return false;
        for (int y = y0; y <= y1; y++)
        {
            if (!Nav.Open(i, y, k) || Nav.Water[Nav.Id(i, y, k)] > 0) return false;
            if (ToLay(Nav.Id(i, y, k), 1)) return false;
        }
        return !Site.Forbidden((i << NavGrid.Shift) + 4, (k << NavGrid.Shift) + 4);
    }

    /// <summary>How far down from a cell its column falls to a cell to stand in (1 or more), or 0 if not within twenty metres.</summary>
    int DropTo(int i, int y, int k, int h)
    {
        for (int d = 1; d <= 20 && y - d >= 1; d++)
        {
            if (!Nav.Open(i, y - d, k)) return 0;
            if (Nav.Stand(i, y - d, k, h)) return d;
        }
        return 0;
    }

    /// <summary>
    /// The cheapest column of scaffolding down from the edge of some cells (a place shut in, or a wall's top) to the
    /// ground beside them, not into cells to keep out of: the cell to stand in to lash it down, the column, and where it
    /// comes to the ground. Its height in cells, or int.MaxValue if none can be lashed down.
    /// </summary>
    int DownFrom(List<int> cells, HashSet<int> keepOut, int h, out int from, out (int i, int k, int y0, int y1) column, out int exit)
    {
        from = exit = -1; column = default;
        int best = int.MaxValue;
        var set = _downSet;
        set.Clear();
        foreach (int c in cells) set.Add(c);
        foreach (int r in cells)
        {
            int ri = Nav.I(r), ry = Nav.Y(r), rk = Nav.K(r);
            for (int dir = 0; dir < 4; dir++)
            {
                int ni = ri + WayDX[dir], nk = rk + WayDK[dir];
                if (!Nav.In(ni, ry, nk)) continue;
                int n = Nav.Id(ni, ry, nk);
                if (set.Contains(n) || keepOut.Contains(n) || !Nav.Open(ni, ry, nk) || Nav.Stand(ni, ry, nk, h)) continue;
                int d = DropTo(ni, ry, nk, h);
                if (d == 0 || d + 1 >= best || !Scaffoldable(ni, nk, ry - d, ry) || ScaffoldNear(ni, nk, 0)) continue;
                int foot = Nav.Id(ni, ry - d, nk);
                if (set.Contains(foot) || keepOut.Contains(foot)) continue;
                best = d + 1; from = r; column = (ni, nk, ry - d, ry); exit = foot;
            }
        }
        return best;
    }

    readonly List<int> _escapeTop = new();
    readonly HashSet<int> _escapeTried = new(), _downSet = new();
    /// <summary>Walls' tops a shut-in worker tries for a way out at most: each is a search of its own.</summary>
    const int EscapeTries = 16;

    /// <summary>
    /// The cheapest scaffolding out of the cells a shut-in worker can walk to (<see cref="_room"/>): a column down to the
    /// ground from the edge of where it stands; or one up a wall beside it to the wall's top, and then, unless it can get
    /// away from there by itself, another down from the edge of what it can walk to up there. Null if none can be raised.
    /// </summary>
    Scaffold PlanEscape(Unit u)
    {
        int h = u.T.Move.Height;
        Scaffold best = null;
        int bestCells = DownFrom(_room, _roomSet, h, out int from, out var col, out int exit);
        if (bestCells != int.MaxValue)
        {
            best = new Scaffold { Escape = true, By = u, Exit = exit };
            best.Columns.Add((col.i, col.k, col.y0, col.y1, true)); best.From.Add(from);
        }
        var top = _escapeTop;
        var tried = _escapeTried;
        tried.Clear();
        foreach (int r in _room)
        {
            // the walls nearest first (the room is walked out from where it stands): sixteen tried are enough
            if (tried.Count >= EscapeTries) break;
            int ri = Nav.I(r), ry = Nav.Y(r), rk = Nav.K(r);
            for (int dir = 0; dir < 4; dir++)
            {
                int ni = ri + WayDX[dir], nk = rk + WayDK[dir];
                if (!Nav.In(ni, ry, nk) || Nav.Open(ni, ry, nk)) continue;
                // a wall beside: up it in the cell where it stands, onto its top
                int ty = -1;
                for (int y = ry + 1; y <= ry + 12 && y < Nav.NY - 2; y++)
                    if (Nav.Stand(ni, y, nk, h)) { ty = y; break; }
                if (ty < 0 || ty - ry + 1 >= bestCells || !Scaffoldable(ri, rk, ry, ty) || ScaffoldNear(ri, rk, 0)) continue;
                int t = Nav.Id(ni, ty, nk);
                if (!tried.Add(t)) continue;
                top.Clear();
                if (Paths.Reach(t, u.T.Move, RoomEnough, true, top) >= RoomEnough && !top.Contains(r))
                {
                    best = new Scaffold { Escape = true, By = u, Exit = t };
                    best.Columns.Add((ri, rk, ry, ty, false)); best.From.Add(r);
                    bestCells = ty - ry + 1;
                    continue;
                }
                // and down from the edge of the wall's top, not back into where it was shut
                int down = DownFrom(top, _roomSet, h, out int edge, out var col2, out int exit2);
                if (down == int.MaxValue || ty - ry + 1 + down >= bestCells || col2.i == ri && col2.k == rk) continue;
                best = new Scaffold { Escape = true, By = u, Exit = exit2 };
                best.Columns.Add((ri, rk, ry, ty, false)); best.From.Add(r);
                best.Columns.Add((col2.i, col2.k, col2.y0, col2.y1, true)); best.From.Add(edge);
                bestCells = ty - ry + 1 + down;
            }
        }
        return best;
    }

    /// <summary>
    /// Scaffolding to get at work marked out of reach of anywhere a worker can walk to (its column, and the lowest and
    /// highest voxels marked there): a column raised from the ground the worker can walk to, beside a place to stand from
    /// which the work is in reach — the ground of a column beside it, the top of a wall by it, the laid part of a roof
    /// over it — up to that place. Null if there is no such place, or a scaffold stands near already.
    /// </summary>
    Scaffold PlanReach(Unit u, int col, int y0, int y1)
    {
        int ci = col % Nav.NX, ck = col / Nav.NX, h = u.T.Move.Height;
        // one going up for this work already: it waits for that one
        if (ScaffoldNear(ci, ck, 3, rising: true)) return null;
        Scaffold best = null;
        int bestCells = int.MaxValue;
        int yLo = Math.Max(1, (y0 - Labour.ReachUp >> NavGrid.Shift) - 1), yHi = Math.Min(Nav.NY - 3, (y1 + Labour.ReachDown >> NavGrid.Shift) + 1);
        // places to stand within three metres of the work, high enough for it
        for (int dk = -3; dk <= 3; dk++)
            for (int di = -3; di <= 3; di++)
            {
                int si = ci + di, sk = ck + dk;
                if (si < 0 || sk < 0 || si >= Nav.NX || sk >= Nav.NZ) continue;
                for (int sy = yHi; sy >= yLo; sy--)
                {
                    if (!Nav.Stand(si, sy, sk, h)) continue;
                    int floor = Nav.Floor[Nav.Id(si, sy, sk)];
                    if (floor + Labour.ReachUp <= y0 || floor - Labour.ReachDown > y1) continue;
                    // a column beside it to raise the scaffold in, from ground the worker can walk to
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int ni = si + WayDX[dir], nk = sk + WayDK[dir];
                        if (ni < 0 || nk < 0 || ni >= Nav.NX || nk >= Nav.NZ || !Nav.Open(ni, sy, nk) || ScaffoldNear(ni, nk, 0)) continue;
                        // not in a dwelling's room: its furnishing is laid out where nothing stands
                        if (ZoneAt((ni << NavGrid.Shift) + 4, (nk << NavGrid.Shift) + 4) is { Kind: Zone.Dwelling }) continue;
                        int down = DropTo(ni, sy, nk, h);
                        if (down == 0) continue;
                        int baseY = sy - down, cells = down + 1;
                        if (cells >= bestCells || cells > 16 || !Scaffoldable(ni, nk, baseY, sy)) continue;
                        int start = Nav.Id(ni, baseY, nk);
                        if (u.Cell != start && Paths.Find(u.Cell, c => c == start, c => H(c, ni, nk), MoverOf(u), 20000, gates: true) == null) continue;
                        best = new Scaffold { By = u, Site = col };
                        best.Columns.Add((ni, nk, baseY, sy, false)); best.From.Add(start);
                        bestCells = cells;
                    }
                    break;
                }
            }
        return best;
    }

    /// <summary>
    /// A worker at its scaffolding: walking to where it raises the next column from, lashing it up there a few voxels at
    /// a time out of the stores' logs; or taking a scaffold down, from the top. True while at that.
    /// </summary>
    bool ScaffoldTick(Unit u, float dt)
    {
        var s = u.Raising;
        if (s == null) return false;
        if (s.Lowering) return LowerTick(u, s, dt);
        if (s.Column >= s.Columns.Count) { FinishScaffold(u, s); return false; }
        u.Why = s.Escape ? "ставит леса, чтобы выбраться" : "ставит леса к работе наверху";
        // to where it raises this column from
        int from = s.From[s.Column];
        if (u.Cell != from)
        {
            if (u.Path == null || u.PathI >= u.Path.Count)
            {
                if (!GoTo(u, from, budget: 20000)) { GiveUpScaffold(u, s); return false; }
                if (u.Path == null) return true;
            }
            Walk(u, dt);
            return true;
        }
        u.Path = null;
        u.State = UnitState.Work;
        var c = s.Columns[s.Column];
        u.Facing = MathF.Atan2(((c.k << NavGrid.Shift) + 4) - u.Z, ((c.i << NavGrid.Shift) + 4) - u.X);
        s.Budget += dt * ScaffoldPace * Math.Max(.3f, u.T.Work) * Hands(u);
        var voxels = ColumnVoxels(c);
        while (s.Budget >= 1 && s.Next < voxels.Count)
        {
            var (x, y, z) = voxels[s.Next];
            if (World.Get(x, y, z) == Mat.Air)
            {
                if (!Stock.Take(Stockpile.Wood, 1))
                {
                    u.Why = "нет брёвен на леса";
                    GiveUpScaffold(u, s);
                    return false;
                }
                World.Set(x, y, z, Mat.Scaffold);
                s.Voxels.Add(WaterPlanner.Pack(x, y, z));
                u.Swing = 1;
                if ((s.Next & 15) == 0) Worked?.Invoke(x, y, z, Mat.Scaffold, false);
            }
            s.Next++;
            s.Budget -= 1;
        }
        if (s.Next < voxels.Count) return true;
        s.Column++; s.Next = 0;
        Nav.Flush();
        if (s.Column < s.Columns.Count) return true;
        FinishScaffold(u, s);
        return false;
    }

    void FinishScaffold(Unit u, Scaffold s)
    {
        s.Done = s.Used = Time;
        u.Raising = null;
        u.Why = "";
        u.Path = null; u.NextSearch = 0;
        // a way out is there now: the shut-in time starts again, and the one who raised it climbs down by it
        u.ShutSince = -1;
        Nav.Flush();
        InvalidateFields();
        if (s.Escape && s.Exit >= 0 && GoTo(u, s.Exit, budget: 6000)) u.Why = "спускается по лесам";
    }

    /// <summary>Nothing to raise it of, or nowhere to raise it from after all: what was put up comes down at once.</summary>
    void GiveUpScaffold(Unit u, Scaffold s)
    {
        u.Raising = null;
        u.Path = null;
        TakeDown(s, quick: true);
    }

    bool LowerTick(Unit u, Scaffold s, float dt)
    {
        u.Why = "разбирает леса";
        u.Path = null;
        u.State = UnitState.Work;
        if (s.Columns.Count > 0) { var c = s.Columns[0]; u.Facing = MathF.Atan2(((c.k << NavGrid.Shift) + 4) - u.Z, ((c.i << NavGrid.Shift) + 4) - u.X); }
        s.Budget += dt * UnscaffoldPace * Math.Max(.3f, u.T.Work) * Hands(u);
        while (s.Budget >= 1 && s.Voxels.Count > 0)
        {
            // from the top down (the voxels sorted so when it began to come down)
            var (x, y, z) = WaterPlanner.Unpack(s.Voxels[^1]);
            s.Voxels.RemoveAt(s.Voxels.Count - 1);
            if (World.Get(x, y, z) == Mat.Scaffold) { World.Set(x, y, z, Mat.Air); Stock[Stockpile.Wood] += 1; u.Swing = 1; }
            s.Budget -= 1;
        }
        if (s.Voxels.Count > 0) return true;
        Scaffolds.Remove(s);
        u.Raising = null;
        u.Why = "";
        u.NextSearch = 0;
        Nav.Flush();
        InvalidateFields();
        return false;
    }

    /// <summary>A scaffold taken down: by one of ours standing by, or at once when the horde is on it or nobody is there to.</summary>
    void TakeDown(Scaffold s, bool quick, Unit by = null)
    {
        if (!quick && by != null && by.Raising == null)
        {
            s.Lowering = true; s.Budget = 0;
            // from the top down, and in each course the boards before the poles they are lashed to, the middle of a span
            // first, so that none is ever left hanging by itself (taken off the end)
            static int Key(int p)
            {
                var (x, y, z) = WaterPlanner.Unpack(p);
                int lx = x & NavGrid.CS - 1, lz = z & NavGrid.CS - 1, e = NavGrid.CS - 1;
                bool onX = lz is 0 || lz == e, onZ = lx is 0 || lx == e;
                int along = onX && onZ ? 0 : 1 + (onX ? Math.Min(lx, e - lx) : Math.Min(lz, e - lz));
                return y * NavGrid.CS + along;
            }
            s.Voxels.Sort((a, b) => Key(a).CompareTo(Key(b)));
            by.Raising = s;
            return;
        }
        foreach (int p in s.Voxels)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            if (World.Get(x, y, z) == Mat.Scaffold) { World.Set(x, y, z, Mat.Air); Stock[Stockpile.Wood] += 1; }
        }
        s.Voxels.Clear();
        if (s.By?.Raising == s) s.By.Raising = null;
        Scaffolds.Remove(s);
        Nav.Flush();
        InvalidateFields();
    }

    /// <summary>
    /// Whether anyone is on a scaffold, or up on what it leads to: in its cells, or one of ours standing higher than its
    /// foot within a few metres of it, who would have no other way down.
    /// </summary>
    bool OnScaffold(Scaffold s, Unit but = null)
    {
        var c0 = s.Columns[0];
        int foot = Nav.Floor[Nav.Id(c0.i, c0.y0, c0.k)];
        foreach (var u in Units)
        {
            if (u == but || u.Dead || u.Inside || u.Airborne != null) continue;
            int ui = Nav.I(u.Cell), uy = Nav.Y(u.Cell), uk = Nav.K(u.Cell);
            foreach (var c in s.Columns)
            {
                // in its cells (anyone: a raider on it is on it too), but for one of ours idling on the ground at its
                // foot (the work it was put up for done from beside it, where the workers reach the lowest course)
                if (ui == c.i && uk == c.k && uy >= c.y0 && uy <= c.y1 + 1 && !(u.Team == Team.Player && uy == c.y0 && u.State == UnitState.Idle)) return true;
                // a worker of ours up on what it leads to, near it (not a soldier at its post on the wall: it has the
                // wall's own way up and down, and a wall kept manned kept the scaffolding by it standing for good)
                // (up there: about as high as its top, not merely higher than its foot, as one on the ground by a scaffold
                // stood in a ditch is)
                if (u.Team == Team.Player && u.T.Work >= 1 && u.Y >= foot + 2 * NavGrid.CS && Math.Abs(uy - (c.y1 + 1)) <= 2 && Math.Abs(ui - c.i) <= 6 && Math.Abs(uk - c.k) <= 6) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Once a second: scaffolds that have served are taken down — one put up to get out of somewhere once its raiser is
    /// out, one for work once nothing marked is left in reach of it for a while, one the player marked to be taken out as
    /// soon as nobody is on it — and any with a raider near comes down at once. One being taken down by a worker who is no
    /// more (fallen, gone out of the valley) is given to another; what of one has lost its footing comes down.
    /// </summary>
    void TickScaffolds()
    {
        if (Scaffolds.Count == 0 || Time < _scaffoldAt) return;
        _scaffoldAt = Time + 1;
        for (int n = Scaffolds.Count - 1; n >= 0; n--)
        {
            var s = Scaffolds[n];
            if (s.Columns.Count == 0) continue;
            var c0 = s.Columns[0];
            float bx = (c0.i << NavGrid.Shift) + 4, bz = (c0.k << NavGrid.Shift) + 4;
            if (s.Lowering)
            {
                // nobody at it any more: another takes it on, or it comes down at once
                bool at = false;
                foreach (var u in Units) if (!u.Dead && u.Raising == s) { at = true; break; }
                if (!at) { s.Lowering = false; TakeDown(s, quick: false, FreeWorkerNear(bx, bz)); }
                continue;
            }
            // what of it stands on nothing any more (burnt through, blown out at its foot) comes down
            if (s.Done >= 0) ScaffoldFalls(s);
            if (!Scaffolds.Contains(s)) continue;
            // the horde near: down with it, before it is climbed
            bool raider = false;
            foreach (var r in Units)
                if (!r.Dead && !r.Inside && r.Team == Team.Horde && MathF.Abs(r.X - bx) < 15 * VoxelWorld.PerMetre && MathF.Abs(r.Z - bz) < 15 * VoxelWorld.PerMetre) { raider = true; break; }
            if (raider)
            {
                if (s.By?.Raising == s) { s.By.Raising = null; s.By.Path = null; }
                TakeDown(s, quick: true);
                Say("Леса разобраны: орда близко.", 2, bx, bz);
                continue;
            }
            // still going up: its raiser gone (fallen, gone out of the valley), it comes down
            if (s.Done < 0)
            {
                if (s.By == null || s.By.Dead || s.By.Raising != s || !Units.Contains(s.By)) TakeDown(s, quick: true);
                continue;
            }
            // marked by the player to be taken out: from the top, once nobody is on it
            if (s.Ordered)
            {
                if (OnScaffold(s)) continue;
                TakeDown(s, quick: InstantLabour, FreeWorkerNear(bx, bz));
                continue;
            }
            // one put up out of a pit stays there, its way out (Sim.Undermine.cs)
            if (s.Escape && OutOfPit(s)) { s.Used = Time; continue; }
            // one put up to get out of somewhere: down as soon as its raiser has climbed down by it
            if (s.Escape && s.By is { Dead: false } by && by.Raising == null && s.Exit >= 0 && CellsApart(by.Cell, s.Exit) <= 1
                && Math.Abs(Nav.Y(by.Cell) - Nav.Y(s.Exit)) <= 1 && !OnScaffold(s, by))
            {
                TakeDown(s, quick: false, by);
                continue;
            }
            if (OnScaffold(s)) { s.Used = Time; continue; }
            if (s.Escape)
            {
                // its raiser out and away from it
                var u = s.By;
                if (u != null && !u.Dead && Paths.Reach(u.Cell, u.T.Move, RoomEnough, true) < RoomEnough) { s.Used = Time; continue; }
                if (Time - s.Used < 3) continue;
                TakeDown(s, quick: u == null || u.Dead || u.Raising != null || Dist2(u, bx, bz) > 30 * VoxelWorld.PerMetre, u);
                continue;
            }
            // work still marked in reach of it, round its column
            if (WorkNear(s)) { s.Used = Time; continue; }
            if (Time - s.Used < ScaffoldIdle) continue;
            TakeDown(s, quick: false, FreeWorkerNear(bx, bz));
        }
    }

    static float Dist2(Unit u, float x, float z) => MathF.Sqrt((u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z));

    /// <summary>
    /// Scaffolding is poles lashed one on another up from the ground: what of a scaffold no longer stands on anything
    /// (its foot burnt through, blown out, dug away) comes down, each piece of it that hangs together falling as one body
    /// and lying broken where it lands. Looked over only when some of it has gone since the last look.
    /// </summary>
    void ScaffoldFalls(Scaffold s)
    {
        int standing = 0;
        foreach (int p in s.Voxels) { var (x, y, z) = WaterPlanner.Unpack(p); if (World.Get(x, y, z) == Mat.Scaffold) standing++; }
        if (standing == s.Standing) return;
        bool first = s.Standing < 0;
        s.Standing = standing;
        if (standing == 0) { Scaffolds.Remove(s); if (s.By?.Raising == s) s.By.Raising = null; return; }
        // (one never looked over before is taken to stand: a scaffold read back from a save stood as it was laid)
        if (first) return;
        var left = new HashSet<int>();
        foreach (int p in s.Voxels) { var (x, y, z) = WaterPlanner.Unpack(p); if (World.Get(x, y, z) == Mat.Scaffold) left.Add(p); }
        // what rests on something that is not scaffolding, and what is lashed to that, up and along
        var queue = new Queue<int>();
        foreach (int p in left)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            byte under = World.Get(x, y - 1, z);
            if (under != Mat.Air && under != Mat.Water && under != Mat.Scaffold) queue.Enqueue(p);
        }
        foreach (int p in queue) left.Remove(p);
        while (queue.Count > 0)
        {
            var (x, y, z) = WaterPlanner.Unpack(queue.Dequeue());
            for (int d = 0; d < 6; d++)
            {
                var (nx, ny, nz) = Step6(x, y, z, d);
                int n = WaterPlanner.Pack(nx, ny, nz);
                if (left.Remove(n)) queue.Enqueue(n);
            }
        }
        if (left.Count == 0) return;
        // what is left hangs in the air: down it comes, piece by piece
        var block = new List<(int x, int y, int z)>();
        while (left.Count > 0)
        {
            int seed = 0;
            foreach (int p in left) { seed = p; break; }
            left.Remove(seed);
            block.Clear();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var (x, y, z) = WaterPlanner.Unpack(queue.Dequeue());
                block.Add((x, y, z));
                for (int d = 0; d < 6; d++)
                {
                    var (nx, ny, nz) = Step6(x, y, z, d);
                    int n = WaterPlanner.Pack(nx, ny, nz);
                    if (left.Remove(n)) queue.Enqueue(n);
                }
            }
            // (lying where it lands as a heap of broken poles, not standing up again in its shape; with too much in the air
            // already, it is taken, its logs back in the stores)
            var piece = Loose(new List<(int x, int y, int z)>(block), 0, -4, 0, "упавшие леса");
            if (piece != null) piece.Heap = true;
            else foreach (var (x, y, z) in block) { World.Set(x, y, z, Mat.Air); Stock[Stockpile.Wood] += 1; }
        }
        s.Standing = 0;
        foreach (int p in s.Voxels) { var (x, y, z) = WaterPlanner.Unpack(p); if (World.Get(x, y, z) == Mat.Scaffold) s.Standing++; }
        if (s.Standing == 0) { Scaffolds.Remove(s); if (s.By?.Raising == s) s.By.Raising = null; }
        Say("Леса лишились опоры и рухнули.", 2, s.Columns[0].i << NavGrid.Shift, s.Columns[0].k << NavGrid.Shift);
        Nav.Flush();
        InvalidateFields();
    }

    /// <summary>
    /// The player's marking to take out scaffolding: the whole scaffold it is part of comes down from the top as the
    /// workers take scaffolding down, once nobody is on it — not dug out from the foot up as earth is, which left what is
    /// over the dug part hanging in the air. Scaffolding not of a scaffold the sim keeps (a game saved before) is gathered
    /// into one as it stands. What was marked of it is dropped.
    /// </summary>
    void ScaffoldsMarked(VoxelList L)
    {
        if (L == null) return;
        HashSet<Scaffold> marked = null;
        for (int i = 0; i < L.Count; i++)
        {
            int x = L.X(i), y = L.Y(i), z = L.Z(i);
            if (World.Get(x, y, z) != Mat.Scaffold || !Orders.IsRemove(Orders.At(x, y, z))) continue;
            int p = WaterPlanner.Pack(x, y, z);
            bool had = false;
            if (marked != null) foreach (var s in marked) if (s.Voxels.Contains(p)) { had = true; break; }
            if (had) continue;
            var sc = Scaffolds.Find(s => s.Voxels.Contains(p)) ?? GatherScaffold(p);
            if (sc != null) (marked ??= new()).Add(sc);
        }
        if (marked == null) return;
        foreach (var s in marked)
        {
            foreach (int p in s.Voxels)
            {
                var (x, y, z) = WaterPlanner.Unpack(p);
                if (Orders.IsRemove(Orders.At(x, y, z))) Orders.Clear(x, y, z);
            }
            // (one still going up comes down when it has served)
            if (s.Done >= 0) s.Ordered = true;
        }
    }

    /// <summary>The scaffolding joined to a voxel of it, gathered into a scaffold standing done, nobody's; null past fifty thousand voxels.</summary>
    Scaffold GatherScaffold(int seed)
    {
        var seen = new HashSet<int> { seed };
        var queue = new Queue<int>();
        queue.Enqueue(seed);
        var s = new Scaffold { Done = Time, Used = Time };
        var cols = new Dictionary<int, (int y0, int y1)>();
        while (queue.Count > 0)
        {
            int p = queue.Dequeue();
            s.Voxels.Add(p);
            if (s.Voxels.Count > 50_000) return null;
            var (x, y, z) = WaterPlanner.Unpack(p);
            int col = (z >> NavGrid.Shift) * Nav.NX + (x >> NavGrid.Shift), cy = y >> NavGrid.Shift;
            cols[col] = cols.TryGetValue(col, out var c) ? (Math.Min(c.y0, cy), Math.Max(c.y1, cy)) : (cy, cy);
            for (int d = 0; d < 6; d++)
            {
                var (nx, ny, nz) = Step6(x, y, z, d);
                if (World.Get(nx, ny, nz) != Mat.Scaffold) continue;
                int n = WaterPlanner.Pack(nx, ny, nz);
                if (seen.Add(n)) queue.Enqueue(n);
            }
        }
        foreach (var (col, (y0, y1)) in cols)
        {
            int i = col % Nav.NX, k = col / Nav.NX;
            s.Columns.Add((i, k, y0, y1, false));
            s.From.Add(Nav.Id(i, y0, k));
        }
        Scaffolds.Add(s);
        return s;
    }

    /// <summary>
    /// Whether work is still marked high up about a scaffold: in any column within <see cref="ScaffoldServes"/> cells of
    /// the work it was raised for (of its foot, for one put up to get out), higher than one reaches from the ground at
    /// its foot. The next stretch and the next courses of the same wall or tower are worked from it, so it stands for them.
    /// </summary>
    bool WorkNear(Scaffold s)
    {
        if (s.Columns.Count == 0) return false;
        var c0 = s.Columns[0];
        int si = s.Site >= 0 ? s.Site % Nav.NX : c0.i, sk = s.Site >= 0 ? s.Site / Nav.NX : c0.k;
        int foot = Nav.Floor[Nav.Id(c0.i, c0.y0, c0.k)];
        for (int dk = -ScaffoldServes; dk <= ScaffoldServes; dk++)
            for (int di = -ScaffoldServes; di <= ScaffoldServes; di++)
            {
                int i = si + di, k = sk + dk;
                if (i < 0 || k < 0 || i >= Nav.NX || k >= Nav.NZ) continue;
                if (Orders.Span(k * Nav.NX + i, out _, out int y1) && y1 >= foot + Labour.ReachUp - 4) return true;
            }
        return false;
    }

    /// <summary>
    /// One with nothing to do, up on what was built with scaffolding beside it: it climbs down by the scaffolding, so
    /// that it is not left up there when the scaffolding comes down. True when it is on its way.
    /// </summary>
    bool GetDown(Unit u)
    {
        if (Scaffolds.Count == 0 || u.T.Work < 1 || Time < u.NextGetDown) return false;
        u.NextGetDown = Time + 5;
        int ui = Nav.I(u.Cell), uk = Nav.K(u.Cell);
        foreach (var s in Scaffolds)
        {
            if (s.Lowering || s.Done < 0 || s.Columns.Count == 0) continue;
            var c0 = s.Columns[0];
            if (Math.Abs(ui - c0.i) > 8 || Math.Abs(uk - c0.k) > 8) continue;
            int foot = s.Escape && s.Exit >= 0 ? s.Exit : Nav.Id(c0.i, c0.y0, c0.k);
            if (u.Y < Nav.Floor[foot] + 2 * NavGrid.CS) continue;
            if (!GoTo(u, foot, budget: 20000)) continue;
            u.Why = "спускается по лесам";
            return true;
        }
        return false;
    }

    /// <summary>A worker of ours near a place with nothing pressing in hand, to take a scaffold down; null if none.</summary>
    Unit FreeWorkerNear(float x, float z)
    {
        Unit best = null;
        float near = 30 * VoxelWorld.PerMetre;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Team != Team.Player || u.T.Work < 1 || u.Raising != null || u.Down || u.Hold || u.Airborne != null) continue;
            float d = Dist2(u, x, z);
            if (d < near) { near = d; best = u; }
        }
        return best;
    }

    /// <summary>The scaffolds standing or going up, kept in a save: who is at each, its columns, how far it is, its voxels.</summary>
    void WriteScaffolds(BinaryWriter w)
    {
        w.Write(Scaffolds.Count);
        foreach (var s in Scaffolds)
        {
            w.Write(s.By?.Id ?? -1); w.Write(s.Escape); w.Write(s.Site); w.Write(s.Exit);
            w.Write(s.Done); w.Write(s.Used); w.Write(s.Lowering); w.Write(s.Column); w.Write(s.Next);
            w.Write(s.Columns.Count);
            for (int n = 0; n < s.Columns.Count; n++)
            {
                var c = s.Columns[n];
                w.Write(c.i); w.Write(c.k); w.Write(c.y0); w.Write(c.y1); w.Write(c.down); w.Write(s.From[n]);
            }
            w.Write(s.Voxels.Count); foreach (int p in s.Voxels) w.Write(p);
        }
    }

    void ReadScaffolds(BinaryReader r)
    {
        Scaffolds.Clear();
        for (int i = r.ReadInt32(); i > 0; i--)
        {
            int by = r.ReadInt32();
            var s = new Scaffold { Escape = r.ReadBoolean(), Site = r.ReadInt32(), Exit = r.ReadInt32(), Done = r.ReadDouble(), Used = r.ReadDouble() };
            s.Lowering = r.ReadBoolean(); s.Column = r.ReadInt32(); s.Next = r.ReadInt32();
            for (int n = r.ReadInt32(); n > 0; n--)
            {
                s.Columns.Add((r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadBoolean()));
                s.From.Add(r.ReadInt32());
            }
            for (int n = r.ReadInt32(); n > 0; n--) s.Voxels.Add(r.ReadInt32());
            s.By = by >= 0 ? Units.Find(u => u.Id == by) : null;
            // one raising it or taking it down goes on with that
            if (s.By != null && !s.By.Dead && (s.Done < 0 || s.Lowering)) s.By.Raising = s;
            Scaffolds.Add(s);
        }
    }
}
