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

    /// <summary>The most of a loose bit the one taking a building down takes away with the voxel that held it.</summary>
    const int LooseMost = 96;
    readonly HashSet<int> _looseSeen = new(), _looseHeld = new();
    readonly List<int> _loose = new();
    readonly Stack<int> _looseWay = new();

    /// <summary>
    /// A voxel of ours just taken out: what of an arch is marked to come out and was held by it alone — a pair of
    /// shingles in the course under the ridge whose neighbour lower down the slope went first, the stub of a crown on the
    /// ridge — is taken away with it, as whoever takes a roof apart takes the loose bits down by hand, and does not drop
    /// on those working under it. Only a small bit, all of it ours and marked to come out: what else it held comes down.
    /// </summary>
    void TakeLooseWith(int x, int y, int z, float yield)
    {
        _looseHeld.Clear();
        bool arched = Arched.Get(x, y, z) != 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy, nz = z + dz, k = Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
                    if (k == 0 || !World.InBounds(nx, ny, nz)) continue;
                    // an arch's members hold at their corners as well
                    if (k > 1 && !arched && Arched.Get(nx, ny, nz) == 0) continue;
                    if (!Loosens(nx, ny, nz) || _looseHeld.Contains(WaterPlanner.Pack(nx, ny, nz))) continue;
                    if (!LooseFrom(nx, ny, nz)) { foreach (int p in _looseSeen) _looseHeld.Add(p); continue; }
                    foreach (int p in _loose)
                    {
                        var (px, py, pz) = WaterPlanner.Unpack(p);
                        DigAway(px, py, pz, World.Get(px, py, pz), yield);
                    }
                }
    }

    /// <summary>
    /// Whether a voxel is of an arch of ours — a roof, a lintel, what stands on a ridge — and marked to come out: only
    /// such a bit hangs on its neighbours in the course and is taken down so. A wall's stones cut out of a gateway go as
    /// they are dug, and those left loose come down to be cleared, as they always did.
    /// </summary>
    bool Loosens(int x, int y, int z)
    {
        byte m = World.Get(x, y, z);
        return Arched.Get(x, y, z) != 0 && Bound(m) && Mat.IsPlayer[m] && Orders.IsRemove(Orders.At(x, y, z));
    }

    /// <summary>
    /// Whether the bit of what is marked to come out joined to a voxel is held by nothing now (into <see cref="_loose"/>):
    /// searched as <see cref="Adrift"/> searches, and held by the ground, by what is set in rock, by anything built that
    /// is not to come out, or by being more than a hand can take.
    /// </summary>
    bool LooseFrom(int sx, int sy, int sz)
    {
        _loose.Clear(); _looseWay.Clear(); _looseSeen.Clear();
        int s = WaterPlanner.Pack(sx, sy, sz);
        _looseWay.Push(s); _looseSeen.Add(s);
        while (_looseWay.Count > 0)
        {
            int p = _looseWay.Pop();
            _loose.Add(p);
            if (_loose.Count > LooseMost) return false;
            var (px, py, pz) = WaterPlanner.Unpack(p);
            byte here = World.Get(px, py, pz);
            bool arched = Arched.Get(px, py, pz) != 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int k = Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
                        if (k == 0 || k > 1 && !arched) continue;
                        int nx = px + dx, ny = py + dy, nz = pz + dz;
                        if (!World.InBounds(nx, ny, nz)) { if (ny < 0) return false; continue; }
                        byte m = World.Get(nx, ny, nz);
                        // resting on the ground (timber on water too), set into rock aside, an arch's corner on the ground
                        if (dy < 0 && dx == 0 && dz == 0 ? Ground(m) || m == Mat.Water && Floats(here)
                            : k == 1 ? dy == 0 && HoldsAside(here, m) : dy < 0 && Ground(m)) return false;
                        if (!Bound(m)) continue;
                        int q = WaterPlanner.Pack(nx, ny, nz);
                        if (_looseSeen.Contains(q)) continue;
                        // anything built that stays holds it, so does what is no arch, and what was found held a moment ago
                        if (!Loosens(nx, ny, nz) || _looseHeld.Contains(q)) return false;
                        _looseSeen.Add(q);
                        _looseWay.Push(q);
                    }
        }
        return true;
    }
}
