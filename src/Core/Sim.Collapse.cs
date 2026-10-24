using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>A tree going over: its voxels as they stood, the foot it turns on, the way it goes, and from when to when.</summary>
public sealed class Toppling
{
    public readonly List<(int x, int y, int z, byte m)> Voxels = new();
    public float PX, PY, PZ, DX, DZ;
    public double From, To;
}

/// <summary>
/// What holds up what is built. A voxel of masonry, timber or earth stands on whatever is under it; out over empty air
/// it is held by its neighbours in the course only so far
/// (<see cref="SpanOf"/>): stone a metre and a quarter from where it rests, timber two metres, rammed earth half a metre.
/// Past that it falls, and all that stands on it. Whatever no longer touches the ground at all — a tower cut at its
/// foot, a bridge cut off at both banks, the trunk over a butt burnt through — falls whole. The arches of bridges and old
/// ruined masonry need only touch the ground. What falls comes down in a heap of rubble that runs out to the sides as
/// loose stone does, crushing whoever is under it; mending puts the building back as it was meant. The horde's miners
/// bring walls down so: they dig out the ground under one, and it falls into the hole.
/// </summary>
public sealed partial class Sim
{
    /// <summary>Voxels laid as part of an arch (a bridge): they stand as long as they touch the ground.</summary>
    public readonly VoxelWorld Arched;

    /// <summary>Places where something solid went, came or changed since they were last looked at.</summary>
    readonly HashSet<int> _shaken = new();
    bool _settling;
    float _collapseAt;
    double _collapseSaid = -99;

    /// <summary>Something fell: the voxel, where it stood and what it was (for chips and dust).</summary>
    public Action<int, int, int, byte> Fell;
    /// <summary>A fall came down: how many voxels, and where the heap lies (for dust and the sound of it).</summary>
    public Action<int, float, float, float> Collapsed;

    const int Free = 255;

    /// <summary>
    /// How far out over the air a voxel of this material is held by its neighbours, in voxels: 0 for what is not built
    /// of anything that falls (air, ground, leaves, ladders, water, gates, dressing), 255 for what only needs to touch
    /// the ground (old masonry).
    /// </summary>
    public static int SpanOf(byte m) => Spans[m];

    // the spans and what holds what, by material, looked up rather than worked out: the search for overhangs asks them
    // of every voxel of a fort, a few hundred thousand times over
    static readonly byte[] Spans = ByMaterial(m => (byte)SpanOfMaterial(m));
    static readonly byte[] Grounds = ByMaterial(m => m != Mat.Air && (Mat.IsAnchor[m] || m == Mat.Ore) ? (byte)1 : (byte)0);
    static readonly byte[] Hards = ByMaterial(m => m is Mat.Rock or Mat.Bed or Mat.Moss or Mat.Ore or Mat.Keep or Mat.KeepShade or Mat.KeepDark or Mat.Roof or Mat.Iron or Mat.Plank or Mat.Ember ? (byte)1 : (byte)0);

    /// <summary>A table of something by material, every byte a voxel can hold (nothing for those no material has).</summary>
    static byte[] ByMaterial(Func<byte, byte> of)
    {
        var t = new byte[256];
        for (int m = 0; m < Mat.IsAnchor.Length && m < Mat.Passable.Length && m < 256; m++) t[m] = of((byte)m);
        return t;
    }

    static int SpanOfMaterial(byte m)
    {
        if (m == Mat.Air || Mat.Passable[m] || Mat.IsAnchor[m] || m == Mat.Water || Mat.IsDecor[m] || Mat.Swings(m) || m == Mat.Ore) return 0;
        if (Mat.IsMasonry(m) || m == Mat.Keep) return 10;
        if (Mat.IsTrunk(m)) return 30;
        return m switch { Mat.Wood or Mat.Deck or Mat.TrapDeck => 16, Mat.Rammed => 4, Mat.Char or Mat.Rubble or Mat.DropStone or Mat.DropLog => 2, Mat.Lumber => 4, Mat.Pitch => 1, Mat.Ruin => Free, _ => 8 };
    }

    /// <summary>
    /// Something a voxel laid here would rest on or be set against: what is under it, or a neighbour in its course. The
    /// builders lay nothing that has neither, so a floor or a parapet waits for the walls under it.
    /// </summary>
    public bool Footed(int x, int y, int z)
    {
        static bool Solid(byte m) => m != Mat.Air && m != Mat.Water && (!Mat.Passable[m] || Mat.IsLadder[m] || m == Mat.Stakes || Mat.IsFurniture[m]);
        if (Solid(World.Get(x, y - 1, z)) || Solid(World.Get(x + 1, y, z)) || Solid(World.Get(x - 1, y, z))
            || Solid(World.Get(x, y, z + 1)) || Solid(World.Get(x, y, z - 1))) return true;
        // the curved members of an arch touch at their corners
        if (Arched.Get(x, y, z) == 0) return false;
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0 || dz != 0) && Solid(World.Get(x + dx, y + dy, z + dz))) return true;
        return false;
    }

    /// <summary>It falls if it is not held: masonry, timber, earth laid by hand, trunks.</summary>
    static bool Holds(byte m) => Spans[m] != 0;

    /// <summary>What lies where it came down: broken stone and splinters.</summary>
    static bool Debris(byte m) => m is Mat.Rubble or Mat.Lumber;

    /// <summary>
    /// Timber floats: what is of wood and lies on water is held by it as by the ground (it came to rest there so, see
    /// <see cref="Landing"/>); else splinters come down on a moat, are found held by nothing, come down on it again, for ever.
    /// </summary>
    static bool Floats(byte m) => Mat.IsTrunk(m) || m is Mat.Wood or Mat.Deck or Mat.Char or Mat.Lumber or Mat.DropLog;

    /// <summary>What holds things up from below: the ground itself, the citadel, a solid floor.</summary>
    static bool Ground(byte m) => Grounds[m] != 0;

    /// <summary>What holds a thing set into it from the side: rock and the citadel's masonry, not loose earth.</summary>
    static bool Hard(byte m) => Hards[m] != 0;

    /// <summary>What holds a voxel of this from the side: what is hard, and for the poles of a trap floor, whose ends are dug into the pit's sides, the ground too.</summary>
    static bool HoldsAside(byte m, byte side) => Hard(side) || m == Mat.TrapDeck && Ground(side);

    /// <summary>A gate's leaf goes when the wall round it does, and it is the gate's own business otherwise.</summary>
    static bool Bound(byte m) => Holds(m) || Mat.IsLeaf(m);

    void OnSupportEdit(int x, int y, int z, byte old)
    {
        byte now = World.Get(x, y, z);
        // a cracked voxel taken out or changed: its crack goes with it
        if (Cracks.Count > 0 && old != now && Cracks.Remove(WaterPlanner.Pack(x, y, z))) CracksVersion++;
        if (_settling) return;
        // the leaves of a gate turning, water running, grass and the dressing come and go without shaking anything
        if (Mat.Swings(old) || Mat.Swings(now)) return;
        if (!Bound(old) && !Ground(old) && !Holds(now)) return;
        // turf burnt black, or grown green again, holds up just what it held
        if ((Mat.IsGrass(old) || old == Mat.Scorch) && (Mat.IsGrass(now) || now == Mat.Scorch)) return;
        _shaken.Add(WaterPlanner.Pack(x, y, z));
        // something that held taken away: what stands over it is weighed as one body; and while a building is weighed
        // so as standing on too little, what is set under it or against it too (it may be propped up)
        if ((Rigid(old) || Ground(old)) && !Holds(now) && !Ground(now) || _leaning > 0 && (Holds(now) || Ground(now))) _undercut.Add(WaterPlanner.Pack(x, y, z));
    }

    void TickCollapse(float dt)
    {
        if (_toppling.Count > 0) LandToppling();
        TickStrain(dt);
        _collapseAt += dt;
        if (_collapseAt < .25f || _shaken.Count == 0 && _weighs.Count == 0 && _undercut.Count == 0)
        {
            if (_due.Count > 0) OverhangsDue(null);
            return;
        }
        _collapseAt = 0;
        // what was found too great to weigh as one body is looked for afresh each pass: the building has changed since
        // (it may be a tower standing by itself now, or one to be cut from its wall, Sim.Joint.cs)
        _bigGen = -1;
        var shaken = new List<int>(_shaken);
        _shaken.Clear();
        var falling = new HashSet<int>();
        _held.Clear();
        // what no longer touches the ground at all
        foreach (int p in shaken)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            for (int d = 0; d <= 6; d++)
            {
                var (nx, ny, nz) = d < 6 ? Step6(x, y, z, d) : (x, y, z);
                if (!World.InBounds(nx, ny, nz) || !Bound(World.Get(nx, ny, nz))) continue;
                int q = WaterPlanner.Pack(nx, ny, nz);
                if (!falling.Contains(q) && !_held.Contains(q)) Adrift(nx, ny, nz, falling);
            }
        }
        // the buildings over where something under them went, each weighed as one body
        if (_undercut.Count > 0) Balance(falling);
        // what is held out too far over the air, and what carries more than it bears, in each patch of the valley that
        // was shaken or jarred (those left over from the last look first)
        var patches = new HashSet<int>(_weighs);
        _weighs.Clear();
        foreach (int p in shaken) { var (x, _, z) = WaterPlanner.Unpack(p); patches.Add(PatchOf(x, z)); }
        foreach (int key in patches) if (_dueSet.Add(key)) _due.Add(key);
        OverhangsDue(falling);
    }

    // the patches of the valley still to be looked over for overhangs, in the order they were shaken: a fort's worth of
    // them a step, so that a stretch of digging along its walls, or a great blast, does not stop the game while all of
    // it is gone through at once
    readonly List<int> _due = new();
    readonly HashSet<int> _dueSet = new();
    const int BuiltAStep = 60_000;

    /// <summary>
    /// Looks over the patches due for overhangs, in turn, till those looked over hold so many built voxels between them
    /// (one patch at least: by count, not by the clock, so a run goes the same on any machine); the rest wait for the
    /// next step. Then what is found held by nothing falls, with whatever else was found so.
    /// </summary>
    void OverhangsDue(HashSet<int> falling)
    {
        falling ??= new HashSet<int>();
        int built = 0, n = 0;
        while (n < _due.Count && built < BuiltAStep)
        {
            int key = _due[n++];
            _dueSet.Remove(key);
            Overhangs((key & 255) << 5, (key >> 8) << 5, 32, falling);
            built += _built.Count;
        }
        _due.RemoveRange(0, n);
        if (falling.Count > 0) Fall(falling, quiet: false);
    }

    static (int, int, int) Step6(int x, int y, int z, int d) => d switch
    {
        0 => (x + 1, y, z), 1 => (x - 1, y, z), 2 => (x, y + 1, z), 3 => (x, y - 1, z), 4 => (x, y, z + 1), _ => (x, y, z - 1),
    };

    readonly Stack<int> _flood = new();
    readonly HashSet<int> _seen = new(), _held = new();
    // the way down tried first, so a search from a wall's top runs straight to its foot
    static readonly int[] Order = { 2, 0, 1, 4, 5, 3 };

    /// <summary>
    /// Searches what is built from a voxel through what is built, downward first; if it never comes to the ground (within
    /// a few tens of thousands of voxels, past which it is surely held), all of it falls.
    /// </summary>
    void Adrift(int x, int y, int z, HashSet<int> falling)
    {
        _flood.Clear(); _seen.Clear();
        int start = WaterPlanner.Pack(x, y, z);
        _flood.Push(start); _seen.Add(start);
        bool held = false;
        while (_flood.Count > 0 && !held)
        {
            if (_seen.Count > 40000) { held = true; break; }
            var (px, py, pz) = WaterPlanner.Unpack(_flood.Pop());
            // an arch holds together at its members' corners as well
            if (Arched.Get(px, py, pz) != 0 && Corners(px, py, pz)) { held = true; break; }
            byte here = World.Get(px, py, pz);
            foreach (int d in Order)
            {
                var (nx, ny, nz) = Step6(px, py, pz, d);
                if (ny < 0) { held = true; break; }
                if (!World.InBounds(nx, ny, nz)) continue;
                byte m = World.Get(nx, ny, nz);
                // resting on the ground (timber on water as well), or set into rock sideways (not hanging under it)
                if (d == 3 ? Ground(m) || m == Mat.Water && Floats(here) : d != 2 && HoldsAside(here, m)) { held = true; break; }
                if (!Bound(m)) continue;
                int q = WaterPlanner.Pack(nx, ny, nz);
                if (_held.Contains(q)) { held = true; break; }
                if (_seen.Add(q)) _flood.Push(q);
            }
        }
        if (held) { foreach (int q in _seen) _held.Add(q); return; }
        foreach (int q in _seen) falling.Add(q);

        // the corners of an arch's voxel: what is built there joins the search, what is ground there holds it
        bool Corners(int px, int py, int pz)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) < 2) continue;
                        int nx = px + dx, ny = py + dy, nz = pz + dz;
                        if (!World.InBounds(nx, ny, nz)) continue;
                        byte m = World.Get(nx, ny, nz);
                        if (dy < 0 && Ground(m)) return true;
                        if (!Bound(m)) continue;
                        int q = WaterPlanner.Pack(nx, ny, nz);
                        if (_held.Contains(q)) return true;
                        if (_seen.Add(q)) _flood.Push(q);
                    }
            return false;
        }
    }

    // a patch of the valley being looked over for overhangs: how far out over the air each built voxel in it is held,
    // and a double-ended queue for the search
    int[] _reach = Array.Empty<int>(), _deque = new int[1 << 16];
    int _head, _tail;
    readonly List<int> _built = new();

    void PushFront(int v) { _head = (_head - 1) & (_deque.Length - 1); _deque[_head] = v; if (_head == _tail) GrowDeque(); }
    void PushBack(int v) { _deque[_tail] = v; _tail = (_tail + 1) & (_deque.Length - 1); if (_head == _tail) GrowDeque(); }
    int PopFront() { int v = _deque[_head]; _head = (_head + 1) & (_deque.Length - 1); return v; }

    void GrowDeque()
    {
        // full: unrolled into one twice as long
        var bigger = new int[_deque.Length * 2];
        int n = _deque.Length;
        for (int i = 0; i < n; i++) bigger[i] = _deque[(_head + i) & (n - 1)];
        _deque = bigger; _head = 0; _tail = n;
    }

    /// <summary>
    /// Looks over a square patch (and a margin round it, taken as held) for built voxels held out over the air further
    /// than their material bears: a 0-1 search outward from all that rests on something, up for nothing, aside for one.
    /// (Courses of mortared stone do not step out as corbels unless built so: what is over a hole wider than masonry
    /// bears comes down to the top of the wall.)
    /// </summary>
    void Overhangs(int x0, int z0, int size, HashSet<int> falling, bool weigh = true)
    {
        const int Margin = 16;
        int bx0 = Math.Max(0, x0 - Margin), bz0 = Math.Max(0, z0 - Margin);
        int bx1 = Math.Min(World.SX - 1, x0 + size + Margin), bz1 = Math.Min(World.SZ - 1, z0 + size + Margin);
        int wx = bx1 - bx0 + 1, wz = bz1 - bz0 + 1, sy = World.SY;
        int n = wx * wz * sy;
        if (_reach.Length < n) _reach = new int[n];
        int I(int x, int y, int z) => ((y * wz) + (z - bz0)) * wx + (x - bx0);
        Array.Fill(_reach, int.MaxValue, 0, n);
        _head = _tail = 0;
        _built.Clear();
        // only the chunks that hold more than one material are looked into: what is built never fills a chunk
        for (int cy = 0; cy < World.CY; cy++)
            for (int cz = bz0 >> VoxelWorld.Shift; cz <= bz1 >> VoxelWorld.Shift; cz++)
                for (int cx = bx0 >> VoxelWorld.Shift; cx <= bx1 >> VoxelWorld.Shift; cx++)
                {
                    var data = World.ChunkData(World.ChunkIndex(cx, cy, cz));
                    if (data == null) continue;
                    for (int i = 0; i < data.Length; i++)
                    {
                        byte m = data[i];
                        if (!Holds(m)) continue;
                        int x = cx * VoxelWorld.CS + (i & 31), z = cz * VoxelWorld.CS + (i >> 5 & 31), y = cy * VoxelWorld.CS + (i >> 10);
                        if (x < bx0 || x > bx1 || z < bz0 || z > bz1 || y < 1) continue;
                        _built.Add(WaterPlanner.Pack(x, y, z));
                        // held from the start: on the ground, set into rock aside, at the edge of the patch, or an arch; and
                        // what came down on a tree's wood, caught in a fork or lying on a bough or a log, as on the ground
                        byte below = World.Get(x, y - 1, z);
                        bool held = Ground(below) || Debris(m) && Mat.IsTrunk(below) || below == Mat.Water && Floats(m)
                            || HoldsAside(m, World.Get(x + 1, y, z)) || HoldsAside(m, World.Get(x - 1, y, z))
                            || HoldsAside(m, World.Get(x, y, z + 1)) || HoldsAside(m, World.Get(x, y, z - 1))
                            || x == bx0 || x == bx1 || z == bz0 || z == bz1 || SpanOf(m) == Free || Arched.Get(x, y, z) != 0;
                        if (!held) continue;
                        _reach[I(x, y, z)] = 0;
                        PushBack(WaterPlanner.Pack(x, y, z));
                    }
                }
        if (_built.Count == 0) return;
        while (_head != _tail)
        {
            int p = PopFront();
            var (x, y, z) = WaterPlanner.Unpack(p);
            int d = _reach[I(x, y, z)];
            byte here = World.Get(x, y, z);
            void Try(int nx, int ny, int nz, int cost)
            {
                if (nx < bx0 || nx > bx1 || nz < bz0 || nz > bz1 || ny < 1 || ny >= sy) return;
                byte m = World.Get(nx, ny, nz);
                if (!Holds(m)) return;
                // what has come down and lies on something built that holds lies on it as on the ground, however far out
                // that is held: else, never out far enough to slip off it, it would lift off it and come down on it again
                // for ever
                int i = I(nx, ny, nz), nd = cost == 0 && Debris(m) && !Debris(here) ? 0 : d + cost;
                if (nd >= _reach[i] || nd > SpanOf(m)) return;
                _reach[i] = nd;
                if (cost == 0) PushFront(WaterPlanner.Pack(nx, ny, nz)); else PushBack(WaterPlanner.Pack(nx, ny, nz));
            }
            // what stands on it is held as well as it is; what juts out beside it, a step less
            Try(x, y + 1, z, 0);
            Try(x + 1, y, z, 1); Try(x - 1, y, z, 1); Try(x, y, z + 1, 1); Try(x, y, z - 1, 1);
        }
        if (weigh) Weigh(bx0, bz0, bx1, bz1, x0, z0, size);
        // in the patch itself (not its margin): whatever built was not reached is held by nothing — unless it stands in a
        // tower or a house that holds it up as one stiff body (or goes over with it)
        HashSet<int> taken = null, stiff = null;
        foreach (int p in _built)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            if (x < x0 || x >= x0 + size || z < z0 || z >= z0 + size || _reach[I(x, y, z)] != int.MaxValue) continue;
            if (weigh && Rigid(World.Get(x, y, z)) && HeldByBody(x, y, z, taken ??= new HashSet<int>(falling), stiff ??= new HashSet<int>())) continue;
            falling.Add(p);
        }
    }

    double _sapSaid = -99;

    /// <summary>
    /// A miner come up against a wall of ours digs out the ground under it rather than hack through: the ground right
    /// under the wall's footing, forty centimetres deep, along five metres of it from the middle outward, so that what
    /// stands over the hole is soon held out over the air further than masonry bears and comes down into it. Once a
    /// wall: then it hacks.
    /// </summary>
    void Sap(Unit u)
    {
        if (u.Sapped == u.CarveCell) return;
        bool ours = false;
        foreach (var (x, y, z) in u.CarveList) { byte m = World.Get(x, y, z); if (Holds(m) && (Mat.IsPlayer[m] || MeantAt(x, y, z) != 0)) { ours = true; break; } }
        if (!ours) return;
        int ui = Nav.I(u.Cell), uk = Nav.K(u.Cell), ti = Nav.I(u.CarveCell), tk = Nav.K(u.CarveCell);
        int di = Math.Sign(ti - ui), dk = Math.Sign(tk - uk);
        if (di != 0 && dk != 0) dk = 0;
        if (di == 0 && dk == 0) return;
        int pi = -dk, pk = di, feet = (int)MathF.Round(u.Y);
        var chamber = new List<(int x, int y, int z, float along)>();
        float mx = (ti << NavGrid.Shift) + 4, mz = (tk << NavGrid.Shift) + 4;
        for (int b = 0; b < 2; b++)
            for (int a = -2; a <= 2; a++)
            {
                int ci = ti + di * b + pi * a, ck = tk + dk * b + pk * a;
                if (ci < 0 || ck < 0 || ci >= Nav.NX || ck >= Nav.NZ) continue;
                for (int z = ck << NavGrid.Shift; z < (ck + 1) << NavGrid.Shift; z++)
                    for (int x = ci << NavGrid.Shift; x < (ci + 1) << NavGrid.Shift; x++)
                    {
                        // the top of the ground in this column, under whatever stands on it: only where the wall stands
                        int top = feet + 16;
                        while (top > feet - 16 && !Ground(World.Get(x, top, z))) top--;
                        if (!Holds(World.Get(x, top + 1, z))) continue;
                        float along = MathF.Abs((x + .5f - mx) * pi + (z + .5f - mz) * pk);
                        for (int y = top; y > top - 3 && y > 1; y--)
                        {
                            byte m = World.Get(x, y, z);
                            if (Ground(m) && Labour.Diggable(m)) chamber.Add((x, y, z, along));
                        }
                    }
            }
        u.Sapped = u.CarveCell;
        // on rock or bedrock, or not under a wall after all: hack through
        if (chamber.Count < 400) return;
        // from the middle of the stretch outward along the wall
        chamber.Sort((a, b) => a.along.CompareTo(b.along));
        u.CarveList.Clear();
        u.SapCost = 0;
        foreach (var (x, y, z, _) in chamber)
        {
            u.CarveList.Add((x, y, z));
            int g = Group(World.Get(x, y, z));
            u.SapCost += g >= 0 && u.T.Move.Carve[g] > 0 ? 1f / u.T.Move.Carve[g] : 1;
        }
        u.CarveI = 0; u.CarveEffort = 0;
        if (Time - _sapSaid > 30) { _sapSaid = Time; Say($"{u.T.Name} подкапывает стену.", 2, u); }
    }

    /// <summary>
    /// A miner at work under a wall: the ground is dug out and held up the while, and when all of it is out it goes at
    /// once, and the wall with it (as the props were burnt in a mine of old). True while it is at it.
    /// </summary>
    bool Sapping(Unit u, float dt)
    {
        if (u.SapCost <= 0) return false;
        u.State = UnitState.Carve;
        u.CarveEffort += dt;
        u.Swing = 1;
        // chips and dust come up out of the hole as it goes
        if (Rng.NextDouble() < dt * 6 && u.CarveList.Count > 0)
        {
            var (cx, cy, cz) = u.CarveList[Rng.Next(u.CarveList.Count)];
            Hacked?.Invoke(cx, cy, cz, World.Get(cx, cy, cz));
        }
        if (u.CarveEffort < u.SapCost) return true;
        foreach (var (x, y, z) in u.CarveList) if (Ground(World.Get(x, y, z))) World.Set(x, y, z, Mat.Air);
        u.SapCost = 0;
        StopCarve(u);
        return true;
    }

    /// <summary>Lets everything unsound in a rectangle come down at once, without a word: a new game's ruins settle.</summary>
    public int Subside(int x0, int z0, int x1, int z1)
    {
        var falling = new HashSet<int>();
        for (int z = Math.Max(0, z0) & ~31; z <= Math.Min(World.SZ - 1, z1); z += 32)
            for (int x = Math.Max(0, x0) & ~31; x <= Math.Min(World.SX - 1, x1); x += 32)
                Overhangs(x, z, 32, falling, weigh: false);
        if (falling.Count > 0) Fall(falling, quiet: true);
        _shaken.Clear();
        return falling.Count;
    }

    /// <summary>
    /// A felled trunk lying across a slope steep enough to start a log rolling goes rolling down it, still marked to be
    /// cut up if it was; one lying along the slope, or on the level, stays where it lies.
    /// </summary>
    void RollFelled(List<(int x, int y, int z)> cells, int fx, int fz)
    {
        int nx = -fz, nz = fx;
        int lo = int.MaxValue, hi = int.MinValue;
        var along = new HashSet<int>();
        float mid = 0;
        foreach (var (x, _, z) in cells)
        {
            int across = x * nx + z * nz;
            lo = Math.Min(lo, across); hi = Math.Max(hi, across); mid += across;
            along.Add(x * fx + z * fz);
        }
        mid /= cells.Count;
        // the ground on either side of it, along its length
        float off = (hi - lo + 1) * .5f + 2, rise = 0;
        int n = 0;
        foreach (int a in along)
        {
            int px = (int)MathF.Round(a * fx + (mid + off) * nx), pz = (int)MathF.Round(a * fz + (mid + off) * nz);
            int qx = (int)MathF.Round(a * fx + (mid - off) * nx), qz = (int)MathF.Round(a * fz + (mid - off) * nz);
            if (!World.InBounds(px, 2, pz) || !World.InBounds(qx, 2, qz)) continue;
            rise += World.SurfAt(px, pz) - World.SurfAt(qx, qz); n++;
        }
        if (n == 0) return;
        float slope = rise / n / (2 * off), push = 2f / 3 * MathF.Abs(slope) / (1 + slope * slope);
        if (push < .14f) return;
        var f = Loose(cells, 0, 0, 0, "бревно");
        if (f != null) f.Intact = true;
        if (f != null && BakeLog(f, out float r, out float half, out float ax, out float az)) StartRoll(f, r, 0, half, ax, az);
    }

    /// <summary>
    /// After a tree came down: any tuft of its leaves still marked to be felled that touches no wood — cut off from the
    /// rest of its crown by what went before — comes down as brushwood too, so no one is left looking up at work out of
    /// reach.
    /// </summary>
    void DropStrays(List<(int x, int y, int z)> timber, List<(int x, int y, int z)> twigs)
    {
        const int Margin = 24;
        int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
        foreach (var (x, y, z) in timber) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z); }
        foreach (var (x, y, z) in twigs) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z); }
        x0 = Math.Max(0, x0 - Margin); z0 = Math.Max(0, z0 - Margin); y0 = Math.Max(1, y0 - Margin);
        x1 = Math.Min(World.SX - 1, x1 + Margin); z1 = Math.Min(World.SZ - 1, z1 + Margin); y1 = Math.Min(World.SY - 1, y1 + Margin);
        var seen = new HashSet<int>();
        for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    DropTuft(x, y, z, seen);
    }

    float _straysAt;

    /// <summary>Every few seconds, where felling is marked: leaves marked with a tree that no longer touch any wood come down.</summary>
    void TickStrays(float dt)
    {
        _straysAt += dt;
        if (_straysAt < 3) return;
        _straysAt = 0;
        DropStrayLeaves(fellingOnly: true);
    }

    /// <summary>
    /// Every tuft of leaves still marked to be felled that touches no wood comes down: what a felling leaves hanging —
    /// a tuft held by a neighbour's trunk that went next, or a game read back from before the crowns went with the
    /// trunks. Looks only where felling is marked, unless told to look at all the marked work. Returns how many leaves
    /// came down.
    /// </summary>
    public int DropStrayLeaves(bool fellingOnly = false)
    {
        var seen = new HashSet<int>();
        int n = 0;
        var fell = Orders.ColWork[(int)WorkKind.Fell];
        foreach (int col in new List<int>(Orders.Active))
        {
            if (fellingOnly && fell[col] == 0) continue;
            if (!Orders.Span(col, out int y0, out int y1)) continue;
            int cx = col % Nav.NX << NavGrid.Shift, cz = col / Nav.NX << NavGrid.Shift;
            for (int y = y0; y <= y1; y++)
                for (int z = cz; z < cz + NavGrid.CS && z < World.SZ; z++)
                    for (int x = cx; x < cx + NavGrid.CS && x < World.SX; x++)
                        n += DropTuft(x, y, z, seen);
        }
        return n;
    }

    /// <summary>
    /// A leaf comes down as brushwood: gone, its order and its fire with it; whatever lay on it (a log, splinters that
    /// came down on the crown before) is looked at again for what holds it.
    /// </summary>
    void Shed(int x, int y, int z)
    {
        World.Set(x, y, z, Mat.Air);
        if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
        Burning.Remove(WaterPlanner.Pack(x, y, z));
        if (World.InBounds(x, y + 1, z) && Bound(World.Get(x, y + 1, z))) _shaken.Add(WaterPlanner.Pack(x, y + 1, z));
    }

    readonly List<(int x, int y, int z)> _tuft = new();
    readonly Queue<(int x, int y, int z)> _tuftQueue = new();

    /// <summary>
    /// The leaves marked to be felled that hang together with this one, down — unless any of them touches wood (a trunk
    /// or a bough, standing or a stump still to be cut). Another tree's leaves they lean on do not hold them up.
    /// </summary>
    int DropTuft(int x, int y, int z, HashSet<int> held)
    {
        int p0 = WaterPlanner.Pack(x, y, z);
        if (!Mat.IsLeafy(World.Get(x, y, z)) || !Orders.IsRemove(Orders.At(x, y, z)) || held.Contains(p0)) return 0;
        // (what is looked at for this tuft is kept apart from the tufts already found held: a tuft half looked at is
        // not to be taken for one cut off, whatever order the columns are gone through in)
        _tuft.Clear(); _tuftQueue.Clear(); _tuftSeen.Clear();
        _tuftSeen.Add(p0);
        _tuftQueue.Enqueue((x, y, z));
        bool holds = false;
        while (_tuftQueue.Count > 0 && !holds)
        {
            var (px, py, pz) = _tuftQueue.Dequeue();
            _tuft.Add((px, py, pz));
            if (_tuft.Count > 40000) { holds = true; break; }
            for (int dy = -1; dy <= 1 && !holds; dy++)
                for (int dz = -1; dz <= 1 && !holds; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = px + dx, ny = py + dy, nz = pz + dz;
                        if ((dx | dy | dz) == 0 || !World.InBounds(nx, ny, nz)) continue;
                        byte m = World.Get(nx, ny, nz);
                        if (Mat.IsTrunk(m)) { holds = true; break; }
                        if (!Mat.IsLeafy(m) || !Orders.IsRemove(Orders.At(nx, ny, nz))) continue;
                        int np = WaterPlanner.Pack(nx, ny, nz);
                        // hanging together with a tuft already found held: held too
                        if (held.Contains(np)) { holds = true; break; }
                        if (_tuftSeen.Add(np)) _tuftQueue.Enqueue((nx, ny, nz));
                    }
        }
        if (holds) { foreach (int q in _tuftSeen) held.Add(q); return 0; }
        foreach (var (px, py, pz) in _tuft) Shed(px, py, pz);
        return _tuft.Count;
    }

    readonly HashSet<int> _tuftSeen = new();

    /// <summary>
    /// What is no longer held comes down: taken out of where it stood, and laid again lower, column by column, each
    /// voxel running off its heap to a lower neighbour as loose stone does. A tree falls over instead, away from whoever
    /// is beside it, and lies as a log — and rolls on, if it lies across a slope. Leaves and twigs that fall are lost.
    /// Whoever it comes down on is crushed; what was marked to be taken out is marked again where it lies.
    /// </summary>
    void Fall(HashSet<int> falling, bool quiet)
    {
        if (!quiet && FallWhole(falling)) return;
        _settling = true;
        var pieces = new List<(int x, int y, int z, byte m, byte order)>(falling.Count);
        int trunks = 0, baseY = int.MaxValue;
        float tx = 0, tz = 0;
        foreach (int p in falling)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            byte m = World.Get(x, y, z);
            if (m == Mat.Air) continue;
            pieces.Add((x, y, z, m, Orders.At(x, y, z)));
            if (Mat.IsTrunk(m)) { trunks++; tx += x; tz += z; baseY = Math.Min(baseY, y); }
        }
        // which way a tree falls: away from the nearest of us, or as it leans
        int fx = 1, fz = 0;
        if (trunks > 0)
        {
            tx /= trunks; tz /= trunks;
            Unit near = null;
            float nd = 12 * VoxelWorld.PerMetre;
            foreach (var u in Units) { if (u.Dead || u.Inside) continue; float d = MathF.Abs(u.X - tx) + MathF.Abs(u.Z - tz); if (d < nd) { nd = d; near = u; } }
            int dir = near == null ? Rng.Next(4) : MathF.Abs(near.X - tx) > MathF.Abs(near.Z - tz) ? (near.X > tx ? 1 : 0) : (near.Z > tz ? 3 : 2);
            (fx, fz) = dir switch { 0 => (1, 0), 1 => (-1, 0), 2 => (0, 1), _ => (0, -1) };
        }
        // the crowns of trees go with their trunks: the leaves nearer to the wood that falls than to any tree left
        // standing (a stump marked to be cut stands no more), all of them however big the crown
        var timber = new List<(int x, int y, int z)>();
        foreach (var (x, y, z, m, _) in pieces) if (Mat.IsTrunk(m)) timber.Add((x, y, z));
        var twigs = timber.Count == 0 ? new List<(int x, int y, int z)>()
            : Shapes.Crown(World, timber, (x, y, z) => !falling.Contains(WaterPlanner.Pack(x, y, z)) && !Orders.IsRemove(Orders.At(x, y, z)),
                (x, y, z) => Orders.IsRemove(Orders.At(x, y, z)));
        // a tree cut through goes over whole, crown and all, turning on its stump, and lies down only when it gets to the
        // ground: what it is made of is taken out of the valley now and drawn falling (Toppled), and laid in a moment
        Toppling topple = null;
        int height = 0;
        foreach (var pc in pieces) height = Math.Max(height, pc.y - baseY);
        if (!quiet && trunks >= 60 && trunks == pieces.Count && height >= 2 * VoxelWorld.PerMetre)
        {
            int top = baseY;
            topple = new Toppling { PX = tx + .5f, PY = baseY, PZ = tz + .5f, DX = fx, DZ = fz, From = Time };
            foreach (var (x, y, z, m, _) in pieces) { topple.Voxels.Add((x, y, z, m)); top = Math.Max(top, y); }
            foreach (var (x, y, z) in twigs) { byte m = World.Get(x, y, z); if (Mat.IsLeafy(m)) topple.Voxels.Add((x, y, z, m)); }
            // a tall tree is longer about it: about a second and a half for ten metres
            topple.To = Time + Math.Clamp(.85 + .07 * (top - baseY) / VoxelWorld.PerMetre, 1, 2.4);
        }
        foreach (var (x, y, z, m, _) in pieces)
        {
            World.Set(x, y, z, Mat.Air);
            if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
            Burning.Remove(WaterPlanner.Pack(x, y, z));
            if (!quiet && topple == null) Fell?.Invoke(x, y, z, m);
        }
        // a crown with its trunk gone comes down as brushwood, which does not keep
        foreach (var (x, y, z) in twigs) if (Mat.IsLeafy(World.Get(x, y, z))) Shed(x, y, z);
        if (timber.Count > 0) DropStrays(timber, twigs);
        if (topple != null)
        {
            _settling = false;
            _toppling.Add((topple, pieces, fx, fz, baseY));
            Toppled?.Invoke(topple);
            return;
        }
        Lay(pieces, fx, fz, baseY, trunks, quiet);
    }

    /// <summary>Trees going over now, to be laid down when they get to the ground.</summary>
    readonly List<(Toppling t, List<(int x, int y, int z, byte m, byte order)> pieces, int fx, int fz, int baseY)> _toppling = new();

    /// <summary>A tree begins to go over: what it is made of, the foot of it, which way it goes and how long it takes.</summary>
    public Action<Toppling> Toppled;

    /// <summary>The trees going over that have got to the ground are laid down there (all of them, before a game is saved).</summary>
    void LandToppling(bool all = false)
    {
        for (int i = _toppling.Count - 1; i >= 0; i--)
        {
            var (t, pieces, fx, fz, baseY) = _toppling[i];
            if (!all && Time < t.To) continue;
            _toppling.RemoveAt(i);
            _settling = true;
            foreach (var (x, y, z, m, _) in pieces) Fell?.Invoke(x, y, z, m);
            Lay(pieces, fx, fz, baseY, pieces.Count, quiet: false);
        }
    }

    /// <summary>What fell, laid down where it comes to rest: a trunk along the ground as a log, the rest in a heap.</summary>
    void Lay(List<(int x, int y, int z, byte m, byte order)> pieces, int fx, int fz, int baseY, int trunks, bool quiet)
    {
        pieces.Sort((a, b) => a.y.CompareTo(b.y));
        float cx = 0, cy = 0, cz = 0;
        int laid = 0, stone = 0, wood = 0;
        var crushed = new Dictionary<Unit, int>();
        // what each came down on brought to its legs, trunk, arms and head, joules (Sim.Trauma.cs)
        var blows = new Dictionary<Unit, float[]>();
        // those it may come down on
        int ax0 = int.MaxValue, az0 = int.MaxValue, ax1 = int.MinValue, az1 = int.MinValue;
        foreach (var (x, _, z, _, _) in pieces) { ax0 = Math.Min(ax0, x); az0 = Math.Min(az0, z); ax1 = Math.Max(ax1, x); az1 = Math.Max(az1, z); }
        var marked = new VoxelList();
        var logCells = new List<(int x, int y, int z)>();
        var logLaid = new HashSet<int>();
        var under = quiet ? new List<Unit>() : Units.FindAll(u => !u.Dead && !u.Inside && u.X > ax0 - 16 && u.X < ax1 + 16 && u.Z > az0 - 16 && u.Z < az1 + 16);
        foreach (var (x, y, z, m, order) in pieces)
        {
            // a trunk falls over and lies along the ground; a gate's leaf is only so much timber once it has fallen
            bool log = Mat.IsTrunk(m);
            int sx = log ? x + fx * (y - baseY) : x, sz = log ? z + fz * (y - baseY) : z, sy = log ? baseY + 6 : y;
            if (!World.InBounds(sx, sy, sz)) continue;
            var (lx, ly, lz) = Landing(sx, sy, sz, log || m is Mat.Wood or Mat.Deck or Mat.Char || Mat.IsLeaf(m), runs: !log, log: log ? logLaid : null);
            if (ly < 0) continue;
            World.Set(lx, ly, lz, Broken(m));
            if (Orders.At(lx, ly, lz) != 0) Orders.Clear(lx, ly, lz);
            if (Orders.IsRemove(order)) marked.Add(lx, ly, lz, Mat.Air);
            if (log) { logCells.Add((lx, ly, lz)); logLaid.Add(WaterPlanner.Pack(lx, ly, lz)); }
            cx += lx; cy += ly; cz += lz; laid++;
            if (Mat.IsMasonry(m) || m == Mat.Ruin) stone++; else if (m is Mat.Wood or Mat.Deck || Mat.IsTrunk(m)) wood++;
            // a falling tree comes down away from those beside it
            if (quiet || log) continue;
            foreach (var u in under)
            {
                if (u.Dead || MathF.Abs(u.X - lx - .5f) > u.T.BodyR + 1 || MathF.Abs(u.Z - lz - .5f) > u.T.BodyR + 1 || ly < u.Y - 1 || ly > u.Y + u.T.BodyH) continue;
                crushed[u] = crushed.GetValueOrDefault(u) + 1;
                // it comes down through the body from the top (or from where it was, if that was lower): the head, a
                // shoulder at the edge, as hard as the fall to there made it
                // (one lying down is as high as it is broad, and what comes down on it lands along it)
                float top = u.Y + (u.Down ? u.T.BodyR * 2 : u.T.BodyH), from = Math.Min(y, top), drop = (y - Math.Max(ly, from - 1)) / VoxelWorld.PerMetre;
                // (what comes down beside the body, outside its round, goes by it; the head is the one column over its middle)
                float off = MathF.Max(MathF.Abs(u.X - lx - .5f), MathF.Abs(u.Z - lz - .5f)) / MathF.Max(1, u.T.BodyR);
                if (drop <= .05f || off > 1) continue;
                if (!blows.TryGetValue(u, out var j)) blows[u] = j = new float[4];
                float at = (from - u.Y) / u.T.BodyH;
                int part = u.Down ? (int)Lying(u, lx + .5f, lz + .5f)
                    : at >= .86f ? (off < .35f ? (int)OnBody.Head : off < .7f ? (int)OnBody.Trunk : (int)OnBody.Arm) : at >= .47f ? (off < .6f ? (int)OnBody.Trunk : (int)OnBody.Arm) : (int)OnBody.Legs;
                j[part] += Kg(m) * 9.8f * drop;
            }
        }
        _settling = false;
        if (marked.Count > 0) Orders.PlanRemove(marked);
        foreach (var (u, n) in crushed) StrikeHeap(u, blows.TryGetValue(u, out var j) ? j : new float[4], "обвал", 30 + Math.Min(90, n * 2));
        // a felled trunk lying across a slope steep enough to start it goes rolling down it
        if (!quiet && logCells.Count >= 24) RollFelled(logCells, fx, fz);
        if (quiet || laid == 0) return;
        cx /= laid; cy /= laid; cz /= laid;
        Collapsed?.Invoke(laid, cx, cy, cz);
        if (laid > 120 && Time - _collapseSaid > 8)
        {
            _collapseSaid = Time;
            if (trunks < laid / 2)
            {
                string what = stone >= wood ? "кладка" : "деревянное";
                Say($"Обвал: рухнула {what}, {Fmt.M3(laid)} м³.", 2, cx, cz);
            }
        }
    }

    /// <summary>
    /// What is no longer held comes down whole: each block of it that hangs together falls as one body, through the air
    /// as anything falls, and lies where it lands in its shape — or, come down on too narrow a footing, goes over on its
    /// side, a wall falling as a wall falls. Trees fall over their own way, and what there is no room for in the air
    /// comes down as before (false then).
    /// </summary>
    bool FallWhole(HashSet<int> falling)
    {
        var list = new List<(int x, int y, int z)>(falling.Count);
        foreach (int p in falling)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            byte m = World.Get(x, y, z);
            if (m == Mat.Air) continue;
            if (Mat.IsTrunk(m) || !Breakable(m)) return false;
            list.Add((x, y, z));
        }
        if (list.Count == 0) return true;
        if (list.Count > MaxAloft - Aloft) return false;
        // the blocks that hang together, face to face
        var left = new HashSet<int>(); foreach (var (x, y, z) in list) left.Add(WaterPlanner.Pack(x, y, z));
        var blocks = new List<List<(int x, int y, int z)>>();
        var queue = new Queue<int>();
        foreach (var (sx, sy, sz) in list)
        {
            int s = WaterPlanner.Pack(sx, sy, sz);
            if (!left.Remove(s)) continue;
            var block = new List<(int x, int y, int z)>();
            queue.Enqueue(s);
            while (queue.Count > 0)
            {
                var (x, y, z) = WaterPlanner.Unpack(queue.Dequeue());
                block.Add((x, y, z));
                for (int d = 0; d < 6; d++)
                {
                    var (nx, ny, nz) = Step6(x, y, z, d);
                    if (left.Remove(WaterPlanner.Pack(nx, ny, nz))) queue.Enqueue(WaterPlanner.Pack(nx, ny, nz));
                }
            }
            blocks.Add(block);
        }
        int stone = 0, wood = 0, laid = 0;
        float cx = 0, cy = 0, cz = 0;
        foreach (var (x, y, z) in list)
        {
            byte m = World.Get(x, y, z);
            if (Mat.IsMasonry(m) || m == Mat.Ruin) stone++; else if (m is Mat.Wood or Mat.Deck) wood++;
            cx += x; cy += y; cz += z; laid++;
            // for the chips and dust at its broken edges
            Fell?.Invoke(x, y, z, m);
        }
        // each block beginning to fall as one, turning a little as it goes
        foreach (var block in blocks)
        {
            var f = Loose(block, 0, -8, 0, "обвал");
            if (f != null) f.Spin = .3f + f.Id % 5 * .15f;
        }
        cx /= laid; cy /= laid; cz /= laid;
        Collapsed?.Invoke(laid, cx, cy, cz);
        if (laid > 120 && Time - _collapseSaid > 8)
        {
            _collapseSaid = Time;
            if (stone + wood > laid / 3)
            {
                string what = stone >= wood ? "кладка" : "деревянное";
                Say($"Обвал: рухнула {what}, {Fmt.M3(laid)} м³.", 2, cx, cz);
            }
        }
        return true;
    }

    /// <summary>What a voxel is once it has fallen: masonry broken stone, timber broken timber, rammed earth loose earth;
    /// trees lie as logs, and the rest as it was.</summary>
    static byte Broken(byte m) =>
        Mat.IsMasonry(m) || m is Mat.Ruin or Mat.Keep or Mat.DropStone ? Mat.Rubble
        : m is Mat.Wood or Mat.Deck or Mat.Plank or Mat.TrapDeck or Mat.Stakes or Mat.DropLog || Mat.Swings(m) ? Mat.Lumber
        : m == Mat.Rammed ? Mat.Soil : m;

    /// <summary>Where a falling voxel comes to rest: down its column onto whatever is solid, then off its heap down to a
    /// lower neighbour while there is one two voxels lower (or one lower, half the time), as loose stone runs; (−1, −1, −1)
    /// if it drops out of the world.</summary>
    (int x, int y, int z) Landing(int x, int y, int z, bool floats, bool runs = true, HashSet<int> log = null)
    {
        // timber floats: it comes to rest on water as on ground; stone sinks through it; and all of it comes down through
        // the leaves of a crown, which hold nothing up (on them it would hang in the air once they went)
        int Rest(int cx, int cz, int from)
        {
            int yy = from;
            while (yy > 0)
            {
                byte b = World.Get(cx, yy - 1, cz);
                if (b != Mat.Air && (b != Mat.Water || floats) && (!Mat.Passable[b] || b == Mat.Water) && !Mat.IsLeafy(b)
                    // a felled trunk is not caught up in a neighbour high over the ground, out of every reach: it comes
                    // down past its boughs and lies low, on what of itself came down first or on the ground
                    && !(log != null && Mat.IsTrunk(b) && yy - 1 > World.GroundTop(cx, cz) + 16 && !log.Contains(WaterPlanner.Pack(cx, yy - 1, cz)))) break;
                yy--;
            }
            return yy;
        }
        int ly = Rest(x, z, y);
        // what is rigid, as a log, comes down its column and stays there
        for (int step = 0; runs && step < 48; step++)
        {
            int bestX = x, bestZ = z, bestY = ly;
            int start = Rng.Next(4);
            for (int k = 0; k < 4; k++)
            {
                int d = (start + k) & 3;
                int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (!World.InBounds(nx, ly, nz)) continue;
                byte at = World.Get(nx, ly, nz);
                if (at != Mat.Air && at != Mat.Water && !Mat.Passable[at]) continue;
                int ny = Rest(nx, nz, ly);
                if ((ny <= ly - 2 || ny == ly - 1 && Rng.NextDouble() < .5) && ny < bestY) { bestX = nx; bestZ = nz; bestY = ny; }
            }
            if (bestY == ly) break;
            x = bestX; z = bestZ; ly = bestY;
        }
        return ly <= 0 ? (-1, -1, -1) : (x, ly, z);
    }
}
