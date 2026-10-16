using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

public sealed partial class Sim
{
    // ------------------------------------------------------------ what the player marks out

    /// <summary>Marks a planned building; pawns lay it voxel by voxel from the stores.</summary>
    public int DesignateBuild(VoxelList L)
    {
        int n = Orders.PlanBuild(L);
        if (InstantLabour) CompleteAll();
        return n;
    }

    /// <summary>Marks earth to dig, rock to quarry, trees to fell or own masonry to take down.</summary>
    public int DesignateRemove(VoxelList L)
    {
        int n = Orders.PlanRemove(L);
        if (InstantLabour) CompleteAll();
        return n;
    }

    public int CancelOrders(int x, int y, int z, double radius) => Orders.Cancel(x, y, z, radius);

    /// <summary>Takes back every order on the columns of a rectangle.</summary>
    public int CancelArea(int x0, int z0, int x1, int z1) => Orders.CancelArea(x0, z0, x1, z1);

    /// <summary>Tests: every order done at once, as if by an army of workers with full stores.</summary>
    public void CompleteAll()
    {
        CountBodies();
        var L = Orders.Layer;
        for (int ci = 0; ci < L.ChunkCount; ci++)
        {
            var d = L.ChunkData(ci);
            if (d == null) continue;
            L.ChunkCoords(ci, out int cx, out int cy, out int cz);
            for (int i = 0; i < d.Length; i++)
            {
                byte o = d[i];
                if (o == 0) continue;
                int x = cx * 32 + (i & 31), z = cz * 32 + (i >> 5 & 31), y = cy * 32 + (i >> 10);
                if (o == Orders.Remove) World.Set(x, y, z, 0);
                else if (_bodies[Nav.CellOf(x, y, z)] > 0) continue;
                else if (Brushes.Free(World.Get(x, y, z))) World.Set(x, y, z, o);
                Orders.Clear(x, y, z);
            }
        }
        Nav.Flush();
    }

    // ------------------------------------------------------------ pawns

    void PawnTick(Unit u, float dt)
    {
        bool soldier = u.T.Work < 1;
        if (soldier && Fight(u, dt)) return;
        if (!soldier && Flee(u, dt)) return;
        if (u.Path != null && u.PathI < u.Path.Count && (u.Hold || u.Site < 0)) { Walk(u, dt); return; }
        if (u.Hold) { u.State = UnitState.Idle; u.Why = ""; return; }
        if (soldier && Waves.Active)
        {
            // while the horde is in the valley soldiers keep to their posts
            ReleaseSite(u);
            if (u.Post >= 0 && u.Cell != u.Post && Time >= u.NextSearch) { u.NextSearch = Time + 2; GoTo(u, u.Post); }
            if (u.Path != null) Walk(u, dt); else u.State = UnitState.Idle;
            return;
        }
        WorkTick(u, dt);
    }

    void WorkTick(Unit u, float dt)
    {
        if (u.Site < 0)
        {
            u.State = UnitState.Idle;
            if (Time >= u.NextSearch) FindWork(u);
            return;
        }
        if (u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt); return; }
        int si = u.Site % Nav.NX, sk = u.Site / Nav.NX;
        if (Math.Abs(Nav.I(u.Cell) - si) > 1 || Math.Abs(Nav.K(u.Cell) - sk) > 1) { ReleaseSite(u); return; }
        if (u.JobI >= u.Jobs.Count)
        {
            Gather(u);
            if (u.Jobs.Count == 0)
            {
                // nothing to do from here after all (stores empty, or someone stands where the masonry goes):
                // leave this spot to others for a while and look elsewhere
                u.Unreachable[u.Site] = Time + (u.Why.StartsWith("нет") ? 8 : 4);
                ReleaseSite(u);
                u.NextSearch = Time + .2;
                return;
            }
        }
        u.State = UnitState.Work;
        u.Effort += (float)(Labour.WorkPerSecond * u.T.Work * dt);
        int guard = 0;
        while (u.JobI < u.Jobs.Count && guard++ < 256)
        {
            var j = u.Jobs[u.JobI];
            float cost = j.m == Orders.Remove ? Labour.Dig(World.Get(j.x, j.y, j.z)) : Labour.Lay(j.m);
            if (cost <= 0) cost = .25f;
            if (u.Effort < cost) break;
            u.JobI++;
            if (Do(u, j))
            {
                u.Effort -= cost;
                u.Swing = 1;
                u.Facing = MathF.Atan2(j.z + .5f - u.Z, j.x + .5f - u.X);
            }
        }
        u.Effort = Math.Min(u.Effort, 60);
    }

    /// <summary>Does one voxel of work; false if there was nothing to do there after all.</summary>
    bool Do(Unit u, (int x, int y, int z, byte m) j)
    {
        byte cur = World.Get(j.x, j.y, j.z);
        if (Orders.At(j.x, j.y, j.z) != j.m) return false;
        if (j.m == Orders.Remove)
        {
            if (!Labour.Diggable(cur)) { Orders.Clear(j.x, j.y, j.z); return false; }
            World.Set(j.x, j.y, j.z, Mat.Air);
            Orders.Clear(j.x, j.y, j.z);
            int kind = Labour.YieldKind(cur);
            if (kind != Stockpile.None) Stock[kind] += Labour.YieldShare(cur) * u.T.Yield;
            Mined?.Invoke(j.x, j.y, j.z, cur);
            return true;
        }
        if (!Brushes.Free(cur)) { Orders.Clear(j.x, j.y, j.z); return false; }
        if (_bodies[Nav.CellOf(j.x, j.y, j.z)] > 0) return false;
        int ck = Labour.CostKind(j.m);
        if (ck != Stockpile.None && !Stock.Take(ck, 1)) { u.Why = "нет: " + Stockpile.Name(ck); return false; }
        World.Set(j.x, j.y, j.z, j.m);
        Orders.Clear(j.x, j.y, j.z);
        return true;
    }

    /// <summary>Called for every voxel a pawn digs out, for chips and dust.</summary>
    public Action<int, int, int, byte> Mined;

    /// <summary>The orders within reach of where the pawn stands: digging top-down first, then laying bottom-up.</summary>
    void Gather(Unit u)
    {
        u.Jobs.Clear(); u.JobI = 0; u.Why = "";
        int i = Nav.I(u.Cell), k = Nav.K(u.Cell), feet = (int)MathF.Round(u.Y);
        int x0 = Math.Max(0, (i - 1) * NavGrid.CS), x1 = Math.Min(World.SX, (i + 2) * NavGrid.CS);
        int z0 = Math.Max(0, (k - 1) * NavGrid.CS), z1 = Math.Min(World.SZ, (k + 2) * NavGrid.CS);
        int y0 = Math.Max(0, feet - Labour.ReachDown), y1 = Math.Min(World.SY, feet + Labour.ReachUp);
        var builds = new List<(int, int, int, byte)>();
        string missing = "";
        bool crowded = false;
        for (int y = y1 - 1; y >= y0 && u.Jobs.Count < 2048; y--)
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                {
                    byte o = Orders.At(x, y, z);
                    if (o == 0) continue;
                    if (o == Orders.Remove) { u.Jobs.Add((x, y, z, o)); continue; }
                    int ck = Labour.CostKind(o);
                    if (ck != Stockpile.None && Stock[ck] < 1) { missing = Stockpile.Name(ck); continue; }
                    if (_bodies[Nav.CellOf(x, y, z)] > 0) { crowded = true; continue; }
                    builds.Add((x, y, z, o));
                }
        builds.Reverse();
        u.Jobs.AddRange(builds);
        if (u.Jobs.Count == 0) u.Why = missing != "" ? "нет: " + missing : crowded ? "на месте кладки стоят" : "";
    }

    void FindWork(Unit u)
    {
        u.NextSearch = Time + .5 + Rng.NextDouble() * .5;
        if (Orders.Active.Count == 0) { u.Why = ""; return; }
        int ui = Nav.I(u.Cell), uk = Nav.K(u.Cell);
        var near = new List<(float d, int col)>();
        foreach (int col in Orders.Active)
        {
            if (_claims[col] >= 2) continue;
            if (u.Unreachable.TryGetValue(col, out double until) && Time < until) continue;
            float d = PathFinder.Octile(col % Nav.NX - ui, col / Nav.NX - uk);
            if (near.Count < 6) { near.Add((d, col)); continue; }
            int worst = 0;
            for (int n = 1; n < near.Count; n++) if (near[n].d > near[worst].d) worst = n;
            if (d < near[worst].d) near[worst] = (d, col);
        }
        near.Sort((a, b) => a.d.CompareTo(b.d));
        int h = u.T.Move.Height;
        foreach (var (_, col) in near)
        {
            if (!Orders.Span(col, out int y0, out int y1)) continue;
            int ci = col % Nav.NX, ck = col / Nav.NX;
            // stand beside the work, or in its column if nothing is to be laid where the body would be
            bool Goal(int c) => Math.Abs(Nav.I(c) - ci) <= 1 && Math.Abs(Nav.K(c) - ck) <= 1
                && Nav.Floor[c] - Labour.ReachDown <= y1 && Nav.Floor[c] + Labour.ReachUp > y0
                && !(Nav.I(c) == ci && Nav.K(c) == ck && BuildsInBody(c, h));
            List<int> path = Goal(u.Cell) ? new List<int>() : Paths.Find(u.Cell, Goal, c => H(c, ci, ck), u.T.Move, 30000);
            if (path == null) { u.Unreachable[col] = Time + 12; continue; }
            u.Site = col; _claims[col]++;
            u.Path = path.Count > 0 ? path : null; u.PathI = 0;
            u.Jobs.Clear(); u.JobI = 0; u.Why = "";
            return;
        }
        if (near.Count > 0) u.Why = "до работы не дойти";
    }

    /// <summary>Planned masonry where a pawn would stand: don't let it wall itself in.</summary>
    bool BuildsInBody(int c, int h)
    {
        int x0 = Nav.I(c) << NavGrid.Shift, z0 = Nav.K(c) << NavGrid.Shift, y0 = Nav.Y(c) << NavGrid.Shift;
        for (int y = y0; y < Math.Min(World.SY, y0 + h * NavGrid.CS); y++)
            for (int z = z0; z < z0 + NavGrid.CS; z++)
                for (int x = x0; x < x0 + NavGrid.CS; x++)
                {
                    byte o = Orders.At(x, y, z);
                    if (o != 0 && o != Orders.Remove) return true;
                }
        return false;
    }

    void ReleaseSite(Unit u)
    {
        if (u.Site >= 0) { _claims[u.Site] = Math.Max(0, _claims[u.Site] - 1); u.Site = -1; }
        u.Jobs.Clear(); u.JobI = 0;
    }

    /// <summary>Workers run for the citadel when a raider comes close; true while running.</summary>
    bool Flee(Unit u, float dt)
    {
        var foe = Nearest(u, Team.Horde, 72);
        if (foe == null)
        {
            if (u.State != UnitState.Flee) return false;
            if (Nearest(u, Team.Horde, 140) != null && u.Path != null) { Walk(u, dt); return true; }
            u.State = UnitState.Idle; u.Path = null;
            return false;
        }
        if (InReach(u, foe) && (u.Path == null || u.Cell == Gate))
        {
            // cornered: hit back
            u.State = UnitState.Fight;
            u.Facing = MathF.Atan2(foe.Z - u.Z, foe.X - u.X);
            if (u.Cooldown <= 0) { Strike(u, foe); u.Cooldown = u.T.AttackTime; }
            return true;
        }
        if (u.State != UnitState.Flee || u.Path == null)
        {
            ReleaseSite(u);
            u.State = UnitState.Flee;
            if (!GoTo(u, Gate, adjacent: true, budget: 20000)) u.Path = null;
        }
        if (u.Path != null) Walk(u, dt);
        return true;
    }
}
