using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A worker shut in by the work — at the bottom of a pit it dug, inside a tower it walled round, in a quarry cut too
/// deep — sees that by itself and marks a way out to cut: steps a metre high up the side of a pit, or a way through a
/// wall at its own level, whichever is the least to cut (its own masonry is cut less gladly than the ground). So a pit
/// is left with a way up out of it, and nobody stands walled in waiting for the player. Shut in is when the cells it
/// can walk to are few and the citadel's door is not among them, twenty seconds on end: a gate being hung or a ladder
/// being set shuts a worker in for a moment only.
/// </summary>
public sealed partial class Sim
{
    /// <summary>Cells: one who can walk to so many is not shut in.</summary>
    const int RoomEnough = 240;
    readonly List<int> _room = new();
    readonly HashSet<int> _roomSet = new();

    /// <summary>Now and then for a worker: shut in, it marks a way out (again after a while, should the marks be taken away).</summary>
    void LookForWayOut(Unit u)
    {
        if (Time < u.NextWayOut) return;
        u.NextWayOut = Time + 4 + (u.Id % 10) * .2;
        if (u.Hold || u.Inside || u.Airborne != null || u.Down || u.Captor != null || Time < u.WayOutMarked + 40) { u.ShutSince = -1; return; }
        _room.Clear();
        if (Paths.Reach(u.Cell, u.T.Move, RoomEnough, true, _room) >= RoomEnough || _room.Contains(Gate)) { u.ShutSince = -1; return; }
        // shut in for a moment (a gate being hung across the only way): wait and see; at the bottom of a pit it dug, it
        // is shut in for good, and the sooner it cuts its way up the better (Sim.Undermine.cs)
        bool pit = InPit(u);
        if (u.ShutSince < 0 && !pit) { u.ShutSince = Time; return; }
        if (!pit && Time - u.ShutSince < 20) return;
        _roomSet.Clear();
        foreach (int c in _room) _roomSet.Add(c);
        // one way out for all who are shut in there together
        foreach (var o in Units)
            if (o != u && !o.Dead && o.Team == Team.Player && Time < o.WayOutMarked + 40 && _roomSet.Contains(o.Cell)) { u.ShutSince = -1; return; }
        // scaffolding out of the stores' logs, if there are any: nothing of what was built is cut (Sim.Scaffold.cs); out
        // of a pit, steps cut up its side first, for they stay, a way out for all who work in it after
        var cut = pit ? WayOut(u) : null;
        if (cut == null && Stock[Stockpile.Wood] >= 400 && PlanEscape(u) is { } climb)
        {
            Scaffolds.Add(climb);
            u.Raising = climb;
            u.WayOutMarked = Time;
            u.ShutSince = -1;
            Say($"{u.T.Name} оказался взаперти и ставит леса, чтобы выбраться.", 2, u);
            return;
        }
        cut ??= pit ? null : WayOut(u);
        if (cut == null) return;
        u.WayOutMarked = Time;
        u.ShutSince = -1;
        if (DesignateRemove(cut) > 0) Say($"{u.T.Name} оказался взаперти и прорубает себе выход.", 2, u);
    }

    /// <summary>
    /// The voxels to cut for the cheapest way out of where a worker stands, along one of the eight ways: up out of a
    /// pit by steps of a metre, or level through a wall. Null when no way out within fourteen metres can be cut.
    /// </summary>
    VoxelList WayOut(Unit u)
    {
        int i0 = Nav.I(u.Cell), k0 = Nav.K(u.Cell), f0 = Nav.Floor[u.Cell];
        // headroom kept open over each step: the body's height and a metre more to climb by
        int head = (u.T.Move.Height + 1) * NavGrid.CS;
        VoxelList best = null;
        float bestCost = float.MaxValue;
        for (int dir = 0; dir < 8; dir++)
            foreach (bool rising in new[] { true, false })
            {
                var cut = new VoxelList();
                float cost = PlanWayOut(u.T.Move, i0, k0, f0, dir, rising, head, cut);
                if (cost < bestCost) { bestCost = cost; best = cut; }
            }
        return best != null && best.Count > 0 ? best : null;
    }

    static readonly int[] WayDX = { 1, -1, 0, 0, 1, 1, -1, -1 }, WayDK = { 0, 0, 1, -1, 1, -1, 1, -1 };

    /// <summary>
    /// Plans one way out step by step, the floor of each step following the ground where it can be walked (a metre up
    /// at most, four down), and cut where it cannot: a metre higher up the side of a pit (rising), or at the same level
    /// through a wall. It ends where the walk comes out beyond the shut-in cells. What it costs (masonry and wood count
    /// double), or infinity when it cannot be cut (forbidden ground, nothing under a step, more than fourteen metres).
    /// </summary>
    float PlanWayOut(Mover m, int i0, int k0, int f0, int dir, bool rising, int head, VoxelList cut)
    {
        float cost = 0;
        int floor = f0;
        for (int s = 1; s <= 14; s++)
        {
            int ci = i0 + WayDX[dir] * s, ck = k0 + WayDK[dir] * s;
            if (ci < 0 || ck < 0 || ci >= Nav.NX || ck >= Nav.NZ) return float.MaxValue;
            int x0 = ci << NavGrid.Shift, z0 = ck << NavGrid.Shift;
            // walkable as it is (a metre up at most, four down): still within where it is shut in, it walks on;
            // beyond that, it is out
            int at = Walkable(m, ci, ck, floor);
            if (at >= 0)
            {
                floor = Nav.Floor[at];
                if (_roomSet.Contains(at)) continue;
                return cost > 0 ? cost : float.MaxValue;
            }
            // to be cut: a step a metre up a pit's side, or level through a wall
            if (rising) floor += NavGrid.CS;
            if (!Under(x0, z0, floor)) return float.MaxValue;
            for (int x = x0; x < x0 + NavGrid.CS; x++)
                for (int z = z0; z < z0 + NavGrid.CS; z++)
                {
                    if (Site.Forbidden(x, z)) return float.MaxValue;
                    for (int y = floor; y < floor + head && y < World.SY; y++)
                    {
                        byte v = World.Get(x, y, z);
                        if (!Solid(v)) continue;
                        if (!Labour.Diggable(v)) return float.MaxValue;
                        cut.Add(x, y, z, Mat.Air);
                        cost += Mat.IsMasonry(v) || Mat.Burns(v) ? 2 : 1;
                    }
                }
        }
        return float.MaxValue;
    }

    static bool Solid(byte m) => m != Mat.Air && !Mat.IsLeafy(m) && !Mat.IsGrass(m);

    /// <summary>A cell of a column one can stand in, as the navigation has it, with its floor a metre above a floor at most and four below at most; -1 if none.</summary>
    int Walkable(Mover m, int i, int k, int floor)
    {
        int y0 = floor >> NavGrid.Shift;
        for (int y = Math.Min(Nav.NY - 1, y0 + 2); y >= Math.Max(0, y0 - 5); y--)
        {
            if (!Nav.CanBe(i, y, k, m.Height, true, m.Ladders)) continue;
            int c = Nav.Id(i, y, k);
            if (Nav.Floor[c] <= floor + NavGrid.CS && Nav.Floor[c] >= floor - 4 * NavGrid.CS) return c;
        }
        return -1;
    }

    /// <summary>Whether there is ground under most of a cell at a floor: a step to stand on.</summary>
    bool Under(int x0, int z0, int floor)
    {
        if (floor <= 0) return true;
        int solid = 0;
        for (int x = x0; x < x0 + NavGrid.CS; x++)
            for (int z = z0; z < z0 + NavGrid.CS; z++)
                if (Solid(World.Get(x, floor - 1, z))) solid++;
        return solid >= NavGrid.CS * NavGrid.CS / 4;
    }
}

