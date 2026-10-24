using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Building so that nothing comes down while it goes up. A voxel is held out over the air by its neighbours in the
/// course only as far as its material bears (<see cref="SpanOf"/>, counted as the search for overhangs counts it: up for
/// nothing, aside for one), and a voxel laid further out than that falls as soon as it is laid. So the builders lay
/// nothing that would be out further than it bears from what holds it: a roof of logs waits for the wall across the
/// room, a deck for its posts, and the worker says it waits for its footing.
/// </summary>
public sealed partial class Sim
{
    readonly Dictionary<int, int> _heldAt = new();
    readonly LinkedList<int> _heldWay = new();

    /// <summary>
    /// How far out over the air a built voxel is held, as the search for overhangs reckons it (0 on the ground, the same
    /// over what holds it, one more aside of it); int.MaxValue when not within <paramref name="most"/>, or not held at all.
    /// Straight down a column first, which is how a wall is held.
    /// </summary>
    int HeldReach(int x, int y, int z, int most)
    {
        // down the column while it is built: a wall rests on its footing
        int yy = y;
        while (yy > 0 && Holds(World.Get(x, yy, z)))
        {
            if (Rests(x, yy, z)) return 0;
            yy--;
        }
        // something under it is not built and does not hold it: the way to what holds it goes aside somewhere
        _heldAt.Clear(); _heldWay.Clear();
        int start = WaterPlanner.Pack(x, y, z);
        _heldAt[start] = 0; _heldWay.AddFirst(start);
        while (_heldWay.Count > 0)
        {
            int p = _heldWay.First.Value; _heldWay.RemoveFirst();
            int d = _heldAt[p];
            var (px, py, pz) = WaterPlanner.Unpack(p);
            if (Rests(px, py, pz)) return d;
            void Step(int nx, int ny, int nz, int cost)
            {
                if (ny < 1 || !World.InBounds(nx, ny, nz) || !Holds(World.Get(nx, ny, nz))) return;
                int nd = d + cost, n = WaterPlanner.Pack(nx, ny, nz);
                if (nd > most || _heldAt.TryGetValue(n, out int had) && had <= nd) return;
                _heldAt[n] = nd;
                if (cost == 0) _heldWay.AddFirst(n); else _heldWay.AddLast(n);
                // the search is bounded: a few thousand voxels of a fort are enough to tell
            }
            if (_heldAt.Count > 6000) break;
            Step(px, py - 1, pz, 0);
            Step(px + 1, py, pz, 1); Step(px - 1, py, pz, 1); Step(px, py, pz + 1, 1); Step(px, py, pz - 1, 1);
        }
        return int.MaxValue;
    }

    /// <summary>Held from the start, as the search for overhangs has it: on the ground, set into rock aside, floating, or of old masonry or an arch.</summary>
    bool Rests(int x, int y, int z)
    {
        byte m = World.Get(x, y, z), below = World.Get(x, y - 1, z);
        return Ground(below) || Debris(m) && Mat.IsTrunk(below) || below == Mat.Water && Floats(m)
            || HoldsAside(m, World.Get(x + 1, y, z)) || HoldsAside(m, World.Get(x - 1, y, z))
            || HoldsAside(m, World.Get(x, y, z + 1)) || HoldsAside(m, World.Get(x, y, z - 1))
            || SpanOf(m) == Free || Arched.Get(x, y, z) != 0;
    }

    /// <summary>
    /// How far out a voxel of this laid here now would be held (as <see cref="HeldReach"/>), from what it would stand
    /// on or beside; int.MaxValue if nothing would hold it within what it bears.
    /// </summary>
    int ReachIfLaid(int x, int y, int z, byte m)
    {
        int span = SpanOf(m);
        if (span == 0 || span == Free) return 0;
        byte below = World.Get(x, y - 1, z);
        if (Ground(below) || Arched.Get(x, y, z) != 0) return 0;
        int best = int.MaxValue;
        if (Holds(below)) best = HeldReach(x, y - 1, z, span);
        if (best == 0) return 0;
        foreach (var (dx, dz) in Beside)
        {
            if (!Holds(World.Get(x + dx, y, z + dz))) continue;
            int d = HeldReach(x + dx, y, z + dz, Math.Min(best, span) - 1);
            if (d != int.MaxValue) best = Math.Min(best, d + 1);
        }
        return best;
    }

    static readonly (int dx, int dz)[] Beside = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>
    /// Whether what is under a voxel is marked to be laid and not laid yet: then the voxel waits for it, rather than be
    /// laid held out from its neighbours in the course (a wall goes up course by course, each on the one under it).
    /// </summary>
    bool UnderMarked(int x, int y, int z)
    {
        byte o = Orders.At(x, y - 1, z);
        return o != 0 && !Orders.IsRemove(o) && World.Get(x, y - 1, z) != o;
    }

    /// <summary>Whether a voxel of this laid here now would be out further than it bears, and come straight down.</summary>
    bool Overreaches(int x, int y, int z, byte m)
    {
        int span = SpanOf(m);
        return span != 0 && span != Free && ReachIfLaid(x, y, z, m) > span;
    }

    /// <summary>
    /// An arched voxel to come out with another to come out over it, a course up and up to two voxels aside (a roof's
    /// slope steps two voxels in at each course): it waits for that one. So a roof is taken off from the ridge down;
    /// cut out from under what is still over it, that is left joined to nothing and comes down on those below.
    /// </summary>
    bool ArchOver(int x, int y, int z)
    {
        for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
                if (Arched.Get(x + dx, y + 1, z + dz) != 0 && Orders.IsRemove(Orders.At(x + dx, y + 1, z + dz))
                    && World.Get(x + dx, y + 1, z + dz) != Mat.Air) return true;
        return false;
    }
}
