using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Weight, cracks, and what goes over whole. What is built carries what stands on it: each voxel its own weight and all
/// that is over it, passed down to what is under it, or, held out over the air, aside to what holds it (<see
/// cref="Weigh"/>). Stone bears a great deal standing on what is under it and little held out aside, timber the other
/// way about, rammed earth little either way; a course under a deep wall bears more aside than a lone one. What carries
/// more than it bears cracks — slowly when it is a little over, fast when far over, and the faster the more it has
/// cracked, for a crack takes from what it bears — and at the end gives way: it crumbles out, and what it held comes down
/// after. Blows crack what is built about where they land (<see cref="Jar"/>): a blast, a catapult's stone, something
/// heavy coming down hard; a wall struck often enough cracks through. Mending puts cracked masonry right.
/// A building standing free — a tower, a stretch of wall, a house — is also weighed as one body on what it stands on
/// (<see cref="Balance"/>): with its weight past the edge of that it goes over whole, turning on the edge, faster and
/// faster, breaking what it swings into and striking whoever is under it, and shatters where it lands (<see
/// cref="GoOver"/>); standing on too little, the edge nearest its weight carries the most and cracks first. A tower
/// whose foot is dug out on one side cracks there, crumbles toward its weight, and goes over that way.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The cracked voxels of what is built (packed), and how far each has cracked, 0 to 1: at 1 it gives way.</summary>
    public readonly Dictionary<int, float> Cracks = new();
    /// <summary>Counts every change to the cracks, for the view to draw them again.</summary>
    public int CracksVersion;
    /// <summary>The voxels whose cracks grew in the last step, for the dust that trickles from them and their creaking.</summary>
    public readonly List<int> Cracking = new();

    /// <summary>A voxel gave way under what it carried, or was cracked through by a blow: where, and what it was.</summary>
    public Action<int, int, int, byte> Crumbled;
    /// <summary>Something built begins to go over whole: the piece of it, turning on its edge.</summary>
    public Action<Flier> GoingOver;
    /// <summary>A piece came down into water: where, how many voxels, how fast (for the splash).</summary>
    public Action<float, float, float, int, float> Plunged;

    /// <summary>
    /// The voxels carrying more than they would bear cracked through, with what they carry and what they bear whole, and
    /// whether that was found by the tower they belong to standing on too little (the rest by the courses about them):
    /// while the load is over what they bear as cracked as they are, their cracks grow.
    /// </summary>
    readonly Dictionary<int, (float load, float bears, bool body)> _strained = new();
    /// <summary>Patches of the valley to be weighed again (their cracks changed, and with them what they bear).</summary>
    readonly HashSet<int> _weighs = new();
    /// <summary>Places where something that held was taken away since they were last looked at: what stands over them is weighed as one body.</summary>
    readonly HashSet<int> _undercut = new();
    double _crackSaid = -99, _overSaid = -99;
    /// <summary>How many of the strained were found so by a building standing on too little.</summary>
    int _leaning;

    /// <summary>The most voxels a building weighed as one body may have; a greater one is held together by its courses only.</summary>
    const int BodyMost = 30000;
    /// <summary>The fewest voxels a building weighed as one body has: a few stones stacked are held by their courses.</summary>
    const int BodyLeast = 200;
    /// <summary>How fast a crack grows, a second, at a load a sixth over what the voxel bears as cracked as it is.</summary>
    const float CrackRate = .2f;

    /// <summary>How heavy a voxel of this is, as voxels of stone.</summary>
    static float WeightOf(byte m) =>
        Mat.IsMasonry(m) || m is Mat.Ruin or Mat.DropStone ? 1
        : m is Mat.Rammed or Mat.Soil ? .8f : m == Mat.Rubble ? .9f
        : Mat.LikeLogs(m) || m is Mat.Deck or Mat.TrapDeck or Mat.Lumber or Mat.DropLog or Mat.Stakes or Mat.Char ? .3f
        : Mat.IsTrunk(m) || Mat.IsLeaf(m) ? .35f
        // iron is near thrice as heavy as stone
        : Mat.IsMetalWork(m) ? 2.8f : .5f;

    /// <summary>How much a voxel of this carries standing on what is under it before it cracks, as voxels of stone: stone a
    /// great deal (a column a good deal higher than the valley is deep), logs less, rammed earth least; 0 for what does not
    /// crack — rubble, old ruins, trees, what is not built.</summary>
    static float BearsDown(byte m) => Mat.IsMasonry(m) ? 250 : m == Mat.Plate ? 900 : Mat.LikeLogs(m) ? 160 : m == Mat.Rammed ? 130 : m == Mat.Deck ? 60 : 0;

    /// <summary>How much a voxel of this held out over the air passes on aside before it cracks, a course with nothing on
    /// it (one under a deep wall bears the more): timber better than stone, rammed earth hardly at all.</summary>
    static float BearsAside(byte m) => Mat.IsMasonry(m) ? 40 : m == Mat.Plate ? 400 : Mat.LikeLogs(m) ? 150 : m == Mat.Rammed ? 12 : m == Mat.Deck ? 30 : 0;

    /// <summary>What a crack leaves of what a voxel bears: cracked through, two fifths.</summary>
    static float Unbroken(float crack) => 1 - .6f * crack;

    /// <summary>What a built voxel is for holding a building together as one body: anything built that holds, but not what
    /// lies about loose, trees, or the old ruins, which stand as they lie; a gate's leaves go with their wall.</summary>
    static bool Rigid(byte m) => Holds(m) && !Debris(m) && !Mat.IsTrunk(m) && SpanOf(m) != Free || Mat.IsLeaf(m);

    static int PatchOf(int x, int z) => (x >> 5) | (z >> 5) << 8;

    // ------------------------------------------------------------ cracks

    /// <summary>Sets how far a voxel has cracked; a crack widening under a load runs up through what stands on it.</summary>
    void SetCrack(int p, float c, bool runs = false)
    {
        if (Cracks.Count > 40000 && !Cracks.ContainsKey(p)) return;
        Cracks[p] = c;
        CracksVersion++;
        if (!runs || c < .35f) return;
        var (x, y, z) = WaterPlanner.Unpack(p);
        float up = c * .7f;
        for (int k = 1; k <= 6 && up > .12f; k++, up *= .8f)
        {
            if (!World.InBounds(x, y + k, z) || BearsDown(World.Get(x, y + k, z)) <= 0) break;
            int q = WaterPlanner.Pack(x, y + k, z);
            if (Cracks.GetValueOrDefault(q) < up) Cracks[q] = up;
        }
    }

    /// <summary>A voxel cracked through gives way: it crumbles out, and what it held is looked at again.</summary>
    void GiveWay(int p)
    {
        var (x, y, z) = WaterPlanner.Unpack(p);
        byte m = World.Get(x, y, z);
        _strained.Remove(p);
        if (BearsDown(m) <= 0) { if (Cracks.Remove(p)) CracksVersion++; return; }
        World.Set(x, y, z, Mat.Air);
        if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
        Burning.Remove(p);
        Fell?.Invoke(x, y, z, m);
        Crumbled?.Invoke(x, y, z, m);
    }

    /// <summary>
    /// A blow or a blast jars what is built about a spot (voxels): what is within so far of it cracks, the more the
    /// nearer, by as much as this at the heart (1 cracks it through there), each voxel a little more or less as the stone
    /// lies. What is cracked through gives way; the rest is weighed again with its cracks.
    /// </summary>
    public void Jar(float fx, float fy, float fz, float r, float k)
    {
        if (r < 1 || k <= 0) return;
        int x0 = (int)fx, y0 = (int)fy, z0 = (int)fz, ri = (int)MathF.Ceiling(r);
        List<int> through = null;
        for (int dy = -ri; dy <= ri; dy++)
            for (int dz = -ri; dz <= ri; dz++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    float d = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d > r) continue;
                    int x = x0 + dx, y = y0 + dy, z = z0 + dz;
                    if (!World.InBounds(x, y, z) || BearsDown(World.Get(x, y, z)) <= 0) continue;
                    uint h = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791);
                    float add = k * (1 - d / r) * (.6f + (h >> 8 & 255) / 255f * .8f);
                    // stone under a warlord's curse cracks the more
                    if (Curses.Count > 0) add *= CurseBlow(x, y, z);
                    if (add < .04f) continue;
                    int p = WaterPlanner.Pack(x, y, z);
                    float c = Math.Min(1, Cracks.GetValueOrDefault(p) + add);
                    SetCrack(p, c);
                    if (c >= 1) (through ??= new()).Add(p);
                }
        for (int pz = (z0 - ri) >> 5; pz <= (z0 + ri) >> 5; pz++)
            for (int px = (x0 - ri) >> 5; px <= (x0 + ri) >> 5; px++)
                if (px >= 0 && pz >= 0) _weighs.Add(px | pz << 8);
        if (through != null) foreach (int p in through) GiveWay(p);
        // a mine under it is shaken too (Sim.Tunnels.cs)
        JarTunnels(fx, fy, fz, r);
    }

    /// <summary>
    /// Acid eating into a voxel of masonry or timber, so far (0 to 1): it is pitted and cracked half as far, and bears
    /// the less for it; what it is in is weighed again each fifth further it goes.
    /// </summary>
    void Pit(int p, float eaten)
    {
        float c = eaten * .5f, was = Cracks.GetValueOrDefault(p);
        if (c <= was + .05f) return;
        SetCrack(p, c);
        if ((int)(c * 5) != (int)(was * 5)) { var (x, _, z) = WaterPlanner.Unpack(p); _weighs.Add(PatchOf(x, z)); }
    }

    /// <summary>Puts a cracked voxel right (a mason has pointed it): the crack is gone, and what it bears with it.</summary>
    public void Point(int x, int y, int z)
    {
        int p = WaterPlanner.Pack(x, y, z);
        if (!Cracks.Remove(p)) return;
        CracksVersion++;
        _weighs.Add(PatchOf(x, z));
        _undercut.Add(p);
    }

    /// <summary>
    /// Each step: the cracks of what carries more than it bears grow, and what they crack through gives way. The first
    /// cracks in what is ours are told of.
    /// </summary>
    void TickStrain(float dt)
    {
        Cracking.Clear();
        if (_strained.Count == 0) return;
        List<int> stale = null, through = null;
        foreach (var (p, (load, bears, _)) in _strained)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            byte m = World.Get(x, y, z);
            if (BearsDown(m) <= 0) { (stale ??= new()).Add(p); continue; }
            float c = Cracks.GetValueOrDefault(p), over = load / (bears * Unbroken(c));
            if (over <= 1) continue;
            Cracking.Add(p);
            if (c == 0 && Mat.IsPlayer[m] && Time - _crackSaid > 30)
            {
                _crackSaid = Time;
                Say("Кладка трескается под тяжестью. Подоприте её или почините, пока не рухнула.", 3, x, z);
            }
        }
        if (stale != null) foreach (int p in stale) _strained.Remove(p);
        _leaning = 0;
        foreach (var (_, (_, _, body)) in _strained) if (body) _leaning++;
        foreach (int p in Cracking)
        {
            var (load, bears, _) = _strained[p];
            float c = Cracks.GetValueOrDefault(p), over = load / (bears * Unbroken(c));
            // the further cracked, the faster it goes on: what is all but through does not hang on for long
            c = Math.Min(1, c + dt * CrackRate * (over - 1 + .15f) * (1 + 2 * c));
            SetCrack(p, c, runs: true);
            if (c >= 1) (through ??= new()).Add(p);
        }
        if (through != null) foreach (int p in through) GiveWay(p);
    }

    /// <summary>Forgets what was found strained by the courses in a patch, to be found again.</summary>
    void ForgetStrain(int x0, int z0, int size)
    {
        List<int> gone = null;
        foreach (var (p, (_, _, body)) in _strained)
        {
            if (body) continue;
            var (x, _, z) = WaterPlanner.Unpack(p);
            if (x >= x0 && x < x0 + size && z >= z0 && z < z0 + size) (gone ??= new()).Add(p);
        }
        if (gone != null) foreach (int p in gone) _strained.Remove(p);
    }

    /// <summary>Keeps a voxel strained, by whichever finding makes it the more so.</summary>
    void Strain(int p, float load, float bears, bool body)
    {
        if (_strained.TryGetValue(p, out var was) && was.body != body && was.load / was.bears > load / bears) return;
        _strained[p] = (load, bears, body);
    }

    // ------------------------------------------------------------ the load through the courses

    float[] _load = Array.Empty<float>();
    long[] _order = Array.Empty<long>();

    /// <summary>
    /// What each built voxel of a patch carries (its margin taken as held, as in <see cref="Overhangs"/>, whose reach this
    /// reads): its own weight and all that stands on it, passed down to what is under it, or, held out over the air, aside
    /// to the neighbours in its course nearer to where it rests. The highest go first, and in a course the farthest out
    /// first, so that what each passes on is all in it when its turn comes. Those carrying more than they would bear
    /// cracked through are kept, to crack while they do.
    /// </summary>
    void Weigh(int bx0, int bz0, int bx1, int bz1, int x0, int z0, int size)
    {
        int wx = bx1 - bx0 + 1, wz = bz1 - bz0 + 1, layer = wx * wz;
        ForgetStrain(x0, z0, size);
        if (_load.Length < _reach.Length) _load = new float[_reach.Length];
        if (_order.Length < _built.Count) _order = new long[_built.Count * 2];
        int n = 0;
        foreach (int p in _built)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            int i = (y * wz + (z - bz0)) * wx + (x - bx0), r = _reach[i];
            if (r == int.MaxValue) continue;
            _load[i] = WeightOf(World.Get(x, y, z));
            _order[n++] = (long)(y << 8 | Math.Min(255, r)) << 32 | (uint)i;
        }
        Array.Sort(_order, 0, n);
        for (int k = n - 1; k >= 0; k--)
        {
            int i = (int)(_order[k] & 0xffffffff);
            int x = i % wx + bx0, z = i / wx % wz + bz0, y = i / layer, r = _reach[i];
            byte m = World.Get(x, y, z), under = World.Get(x, y - 1, z);
            float load = _load[i], aside = 0;
            if (y > 1 && Holds(under) && _reach[i - layer] != int.MaxValue) _load[i - layer] += load;
            else if (Holds(under) || under == Mat.Air || Mat.Passable[under] && under != Mat.Water || under == Mat.Water && !Floats(m))
            {
                // held out over the air: aside, to those of its course nearer to where it rests
                int a = x > bx0 && _reach[i - 1] < r ? i - 1 : -1, b = x < bx1 && _reach[i + 1] < r ? i + 1 : -1;
                int c = z > bz0 && _reach[i - wx] < r ? i - wx : -1, d = z < bz1 && _reach[i + wx] < r ? i + wx : -1;
                int to = (a >= 0 ? 1 : 0) + (b >= 0 ? 1 : 0) + (c >= 0 ? 1 : 0) + (d >= 0 ? 1 : 0);
                if (to > 0)
                {
                    float share = load / to;
                    if (a >= 0) _load[a] += share;
                    if (b >= 0) _load[b] += share;
                    if (c >= 0) _load[c] += share;
                    if (d >= 0) _load[d] += share;
                    aside = load;
                }
            }
            if (x < x0 || x >= x0 + size || z < z0 || z >= z0 + size) continue;
            // what comes down a column spreads out through the courses about it as it goes (half as far again down as
            // aside), as passing it straight down here does not: a column bears twice what it would standing alone
            float down = BearsDown(m) * 2;
            if (down <= 0) continue;
            float worst = load / down, bears = down, carried = load;
            if (aside > 0)
            {
                // a course with a deep wall over it bears the more
                int deep = 0;
                for (int yy = y + 1; yy <= y + 40 && yy < World.SY && BearsDown(World.Get(x, yy, z)) > 0; yy++) deep++;
                float side = BearsAside(m) * (1 + deep / 2f);
                if (aside / side > worst) { worst = aside / side; bears = side; carried = aside; }
            }
            if (worst > .4f) Strain(WaterPlanner.Pack(x, y, z), carried, bears, body: false);
        }
    }

    // ------------------------------------------------------------ a building as one body

    readonly List<int> _body = new();
    readonly List<int> _stands = new();
    readonly Stack<int> _bodyStack = new();
    /// <summary>A body is looked for within so many voxels of where it is first found, each way; one reaching further
    /// is held together by its courses only.</summary>
    const int Box = 64;
    /// <summary>What each search for a body has seen, by its number (a box about where it began), and the last search
    /// that found one too great to weigh: what it saw is not searched again.</summary>
    int[] _stamp = Array.Empty<int>();
    int _gen, _bigGen = -1, _bigX, _bigZ;

    int StampAt(int ox, int oz, int x, int y, int z)
    {
        int lx = x - ox, lz = z - oz;
        if ((uint)lx > 2 * Box || (uint)lz > 2 * Box || (uint)y >= World.SY) return -1;
        return (y * (2 * Box + 1) + lz) * (2 * Box + 1) + lx;
    }

    /// <summary>Whether a voxel was seen by the last search that found too great a body.</summary>
    bool InBig(int x, int y, int z)
    {
        if (_bigGen < 0) return false;
        int i = StampAt(_bigX, _bigZ, x, y, z);
        return i >= 0 && _stamp[i] == _bigGen;
    }

    /// <summary>A building found as one body: its weight and where its weight is, the bounds of it and of what it stands on.</summary>
    struct Bulk
    {
        public float M, GX, GY, GZ;
        public int X0, X1, Y0, Y1, Z0, Z1;
        public int FX0, FX1, FZ0, FZ1, FY;
        public bool Free;
    }

    /// <summary>
    /// The building a built voxel belongs to, as one body: all that is built and joined to it face to face, and the voxels
    /// of it that stand on something — the ground, rubble, a tree, water under timber — or are set into rock aside. Null
    /// when there is more of it than a body is weighed of, or it reaches further (then its courses hold it together), or
    /// when the voxel is in <paramref name="taken"/>, which a body found is added to.
    /// </summary>
    Bulk? Gather(int sx, int sy, int sz, HashSet<int> taken)
    {
        _body.Clear(); _stands.Clear(); _bodyStack.Clear();
        int s = WaterPlanner.Pack(sx, sy, sz);
        if (taken.Contains(s) || !Rigid(World.Get(sx, sy, sz)) || InBig(sx, sy, sz)) return null;
        int n = (2 * Box + 1) * (2 * Box + 1) * World.SY;
        if (_stamp.Length < n) _stamp = new int[n];
        int gen = ++_gen, ox = sx - Box, oz = sz - Box;
        _bodyStack.Push(s); _stamp[StampAt(ox, oz, sx, sy, sz)] = gen;
        var b = Empty();
        bool tooBig = false;
        _byJoint = false;
        while (_bodyStack.Count > 0)
        {
            int p = _bodyStack.Pop();
            if (_body.Count >= BodyMost) { tooBig = true; break; }
            var (x, y, z) = WaterPlanner.Unpack(p);
            Take(ref b, p, x, y, z);
            for (int d = 0; d < 6 && !tooBig; d++)
            {
                var (nx, ny, nz) = Step6(x, y, z, d);
                if (!World.InBounds(nx, ny, nz) || !Rigid(World.Get(nx, ny, nz))) continue;
                int i = StampAt(ox, oz, nx, ny, nz);
                if (i < 0) { tooBig = true; break; }
                if (_stamp[i] == gen) continue;
                _stamp[i] = gen;
                _bodyStack.Push(WaterPlanner.Pack(nx, ny, nz));
            }
            if (tooBig) break;
        }
        if (tooBig)
        {
            // too great: a tower in a long wall may yet be weighed by itself, cut from the wall at its joint (Sim.Joint.cs)
            var tower = Joint(sx, sy, sz, taken, out int seen);
            if (tower != null) return tower;
            _bigGen = seen; _bigX = ox; _bigZ = oz;
            return null;
        }
        foreach (int q in _body) taken.Add(q);
        return Finish(ref b);
    }

    /// <summary>A new body's bounds, before any voxel is taken into it.</summary>
    static Bulk Empty() => new Bulk { X0 = int.MaxValue, Y0 = int.MaxValue, Z0 = int.MaxValue, X1 = int.MinValue, Y1 = int.MinValue, Z1 = int.MinValue,
        FX0 = int.MaxValue, FZ0 = int.MaxValue, FX1 = int.MinValue, FZ1 = int.MinValue, FY = int.MaxValue };

    /// <summary>
    /// A voxel taken into a body: its weight and its bounds, and, if it stands on something — the ground, rubble, a tree,
    /// water under timber — or is set into rock aside, it is one of what the body stands on.
    /// </summary>
    void Take(ref Bulk b, int p, int x, int y, int z)
    {
        _body.Add(p);
        byte m = World.Get(x, y, z);
        float w = WeightOf(m);
        b.M += w; b.GX += w * (x + .5f); b.GY += w * (y + .5f); b.GZ += w * (z + .5f);
        b.X0 = Math.Min(b.X0, x); b.X1 = Math.Max(b.X1, x); b.Y0 = Math.Min(b.Y0, y); b.Y1 = Math.Max(b.Y1, y); b.Z0 = Math.Min(b.Z0, z); b.Z1 = Math.Max(b.Z1, z);
        byte under = World.Get(x, y - 1, z);
        bool stands = y <= 1 || !Rigid(under) && (under == Mat.Water ? Floats(m) : under != Mat.Air && !Mat.Passable[under])
            || HoldsAside(m, World.Get(x + 1, y, z)) || HoldsAside(m, World.Get(x - 1, y, z))
            || HoldsAside(m, World.Get(x, y, z + 1)) || HoldsAside(m, World.Get(x, y, z - 1)) || Arched.Get(x, y, z) != 0;
        if (!stands) return;
        _stands.Add(p);
        b.FX0 = Math.Min(b.FX0, x); b.FX1 = Math.Max(b.FX1, x); b.FZ0 = Math.Min(b.FZ0, z); b.FZ1 = Math.Max(b.FZ1, z); b.FY = Math.Min(b.FY, y);
    }

    /// <summary>A body's weight put where it is, and whether it stands free as a tower or a house does.</summary>
    static Bulk? Finish(ref Bulk b)
    {
        if (b.M <= 0) return null;
        b.GX /= b.M; b.GY /= b.M; b.GZ /= b.M;
        // a tower or a house: about as deep as it is wide, and standing up
        int dx = b.X1 - b.X0 + 1, dz = b.Z1 - b.Z0 + 1, h = b.Y1 - b.Y0 + 1;
        b.Free = Math.Max(dx, dz) <= 2.5f * Math.Min(dx, dz) && h >= 16 && h >= .6f * Math.Min(dx, dz);
        return b;
    }

    /// <summary>Whether a body stands high enough to go over: a metre and more, its weight half a metre over its foot. A
    /// slab or a floor lying out past its footing only sags, and its courses hold it or let it down.</summary>
    static bool Tall(in Bulk b) => b.Y1 - b.Y0 >= 8 && b.GY - b.FY >= 4;

    /// <summary>How far a body's weight is out past the edge of what it stands on (voxels), and which way (0 +x, 1 −x, 2 +z, 3 −z).</summary>
    static float Past(in Bulk b, out int dir)
    {
        float ox = b.GX < b.FX0 ? b.GX - b.FX0 : b.GX > b.FX1 + 1 ? b.GX - (b.FX1 + 1) : 0;
        float oz = b.GZ < b.FZ0 ? b.GZ - b.FZ0 : b.GZ > b.FZ1 + 1 ? b.GZ - (b.FZ1 + 1) : 0;
        if (MathF.Abs(ox) >= MathF.Abs(oz)) { dir = ox > 0 ? 0 : 1; return MathF.Abs(ox); }
        dir = oz > 0 ? 2 : 3;
        return MathF.Abs(oz);
    }

    /// <summary>
    /// The buildings over the places undercut since they were last looked at, each weighed as one body on what is left to
    /// stand on: with its weight past the edge of that by more than half a voxel it goes over whole; else the weight on
    /// each voxel it stands on is as a stiff body's on its footing — even when its weight is over the middle of it, more on
    /// the edge it leans toward when not — and those carrying more than they bear crack.
    /// </summary>
    void Balance(HashSet<int> falling)
    {
        var taken = new HashSet<int>(falling);
        var under = new List<int>(_undercut);
        _undercut.Clear();
        foreach (int p in under)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            for (int d = 0; d <= 6; d++)
            {
                var (nx, ny, nz) = d < 6 ? Step6(x, y, z, d) : (x, y, z);
                if (!World.InBounds(nx, ny, nz)) continue;
                var bulk = Gather(nx, ny, nz, taken);
                if (bulk is not { } b || _stands.Count == 0 || _body.Count < BodyLeast) continue;
                if (Past(b, out int dir) > .5f) { if (Tall(b)) GoOverWhole(b, dir); continue; }
                Bear(b);
            }
        }
    }

    /// <summary>
    /// The weight of a body standing as a stiff one on what it stands on: spread over it evenly, and more toward the edge
    /// its weight leans to, as a plank's on two trestles; the voxels it stands on that carry more than they bear crack.
    /// </summary>
    void Bear(in Bulk b)
    {
        int n = _stands.Count;
        float cx = 0, cz = 0;
        foreach (int p in _stands) { var (x, _, z) = WaterPlanner.Unpack(p); cx += x + .5f; cz += z + .5f; }
        cx /= n; cz /= n;
        float jxx = .1f * n, jxz = 0, jzz = .1f * n;
        foreach (int p in _stands)
        {
            var (x, _, z) = WaterPlanner.Unpack(p);
            float dx = x + .5f - cx, dz = z + .5f - cz;
            jxx += dx * dx; jxz += dx * dz; jzz += dz * dz;
        }
        float det = jxx * jzz - jxz * jxz, ex = b.GX - cx, ez = b.GZ - cz;
        float bx = det > 1e-3f ? b.M * (jzz * ex - jxz * ez) / det : 0, bz = det > 1e-3f ? b.M * (jxx * ez - jxz * ex) / det : 0;
        float even = b.M / n, worst = 0;
        foreach (int p in _stands)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            byte m = World.Get(x, y, z);
            float down = BearsDown(m);
            if (down <= 0) continue;
            float load = even + bx * (x + .5f - cx) + bz * (z + .5f - cz);
            worst = Math.Max(worst, load / down);
            if (load / down > .4f) Strain(p, load, down, body: true);
            else if (_strained.TryGetValue(p, out var was) && was.body) _strained.Remove(p);
        }
        // a tower in a wall: its joint with the wall cracks as its foot does (Sim.Joint.cs)
        StrainJoint(worst);
    }

    /// <summary>
    /// Whether what <see cref="Overhangs"/> found held out too far is part of a tower or a house standing as one body on
    /// enough: then a wall of it over a hole in its foot is held by the rest of it, stiff, rather than coming down by
    /// itself — or, its weight past the edge of what it stands on, all of it goes over. True if the voxel is taken care of.
    /// </summary>
    bool HeldByBody(int x, int y, int z, HashSet<int> taken, HashSet<int> held)
    {
        int p = WaterPlanner.Pack(x, y, z);
        if (held.Contains(p)) return true;
        if (taken.Contains(p)) return false;
        var bulk = Gather(x, y, z, taken);
        if (bulk is not { } b || _stands.Count == 0 || _body.Count < BodyLeast) return false;
        if (Past(b, out int dir) > .5f) { if (!Tall(b)) return false; GoOverWhole(b, dir); return true; }
        if (!b.Free) return false;
        foreach (int q in _body) held.Add(q);
        Bear(b);
        return true;
    }

    // ------------------------------------------------------------ going over

    /// <summary>
    /// A building goes over whole: it is taken out of the valley as one piece and turns on the edge of what it stood on,
    /// toward its weight (<see cref="GoOver"/>). Too great to be in the air with all else that is, it comes down in a heap
    /// where it stands instead.
    /// </summary>
    void GoOverWhole(in Bulk b, int dir)
    {
        var cells = new List<(int x, int y, int z)>(_body.Count);
        foreach (int p in _body) cells.Add(WaterPlanner.Unpack(p));
        float edge = dir switch { 0 => b.FX1 + 1, 1 => b.FX0, 2 => b.FZ1 + 1, _ => b.FZ0 };
        bool torn = _byJoint;
        var f = Loose(cells, 0, 0, 0, "обвал");
        // a tower going over out of the wall it is built into tears its joint (Sim.Joint.cs)
        TearJoint();
        if (f == null)
        {
            var heap = new HashSet<int>(_body);
            Fall(heap, quiet: false);
            return;
        }
        f.Tipping = f.Toppling = true;
        f.TipFrom = Time; f.TipRate = 0; f.Turn = 0; f.Spin = 0;
        f.CX0 = f.X; f.CY0 = f.Y; f.CZ0 = f.Z;
        (f.AX, f.AZ) = dir switch { 0 => (0f, -1f), 1 => (0f, 1f), 2 => (1f, 0f), _ => (-1f, 0f) };
        f.AY = 0;
        if (dir < 2) { f.PX = edge; f.PZ = f.Z; } else { f.PX = f.X; f.PZ = edge; }
        f.PY = b.FY;
        f.Foot = b.FY + Math.Max(3, (b.Y1 - b.Y0) / 4) - (f.Y - .5f);
        // how far over it leans already: its weight out past the edge, against how high it is over it
        float out_ = dir switch { 0 => b.GX - edge, 1 => edge - b.GX, 2 => b.GZ - edge, _ => edge - b.GZ };
        f.TipLean = MathF.Max(.08f, MathF.Atan2(out_, MathF.Max(1, b.GY - b.FY)));
        // what it stood on cracked through at the edge it goes over
        foreach (int p in _stands) if (Rng.NextDouble() < .3) { var (x, y, z) = WaterPlanner.Unpack(p); Fell?.Invoke(x, y, z, Mat.Stone); }
        GoingOver?.Invoke(f);
        if (cells.Count > 400 && Time - _overSaid > 8)
        {
            _overSaid = Time;
            Say(torn ? "Башня отрывается от стены и рушится." : b.Y1 - b.Y0 > Math.Max(b.X1 - b.X0, b.Z1 - b.Z0) ? "Башня рушится." : "Стена заваливается.", 2, b.GX, b.GZ);
        }
    }

    /// <summary>
    /// A building going over: it turns on the edge it stood on as a stiff body does, slowly from upright and faster and
    /// faster as its weight goes out over the edge. What is built in its way is broken out and thrown on the way it swings,
    /// taking from its swing; whoever it swings into is struck by it. When it lies over, or comes against the ground or
    /// more than it can break, it flies on from there as anything falls, turning as it turned, and shatters where it
    /// comes down (<see cref="Shatter"/>). Its foot grinds into the ground it stood on as it turns, and is let be.
    /// </summary>
    void GoOver(Flier f, float dt)
    {
        float rx = f.CX0 - f.PX, ry = f.CY0 - f.PY, rz = f.CZ0 - f.PZ, arm = MathF.Max(4, MathF.Sqrt(rx * rx + ry * ry + rz * rz));
        // a stiff body turning on an edge: as a rod on its end, its weight at its middle
        f.TipRate += 3 * Gravity / (4 * arm) * MathF.Sin(f.TipLean + f.Turn) * dt;
        float th = MathF.Min(f.Turn + f.TipRate * dt, MathF.PI / 2 + .1f);
        var (px, py, pz) = Turned(rx, ry, rz, f.AX, 0, f.AZ, MathF.Cos(th), MathF.Sin(th));
        f.X = f.PX + px; f.Y = f.PY + py; f.Z = f.PZ + pz;
        f.Turn = th;
        // what it swings into
        var (ax, ay, az) = AxisOf(f);
        float c = MathF.Cos(th), s = MathF.Sin(th);
        var hit = new HashSet<int>();
        bool stopped = false;
        int stride = Math.Max(1, f.Voxels.Count / 6000);
        for (int i = 0; i < f.Voxels.Count && !stopped; i += stride)
        {
            var (dx, dy, dz, _) = f.Voxels[i];
            var (vx, vy, vz) = Turned(dx, dy, dz, ax, ay, az, c, s);
            float wx = f.X + vx, wy = f.Y + vy, wz = f.Z + vz;
            bool foot = dy < f.Foot;
            // its foot, by the edge it turns on, grinds into what it stood on
            float ex = wx - f.PX, ey = wy - f.PY, ez = wz - f.PZ, along = ex * f.AX + ez * f.AZ;
            ex -= along * f.AX; ez -= along * f.AZ;
            if (ex * ex + ey * ey + ez * ez < 3.5f * 3.5f) continue;
            int ix = (int)MathF.Floor(wx), iy = (int)MathF.Floor(wy), iz = (int)MathF.Floor(wz);
            if (!World.InBounds(ix, iy, iz)) { stopped |= iy < 1; continue; }
            byte m = World.Get(ix, iy, iz);
            if (m == Mat.Air || Mat.Passable[m] && !Mat.IsLadder[m] || m == Mat.Water) continue;
            if (Breakable(m) && (Holds(m) || Mat.IsPlayer[m] || Mat.IsLadder[m] || Mat.IsLeafy(m) || Mat.IsDecor[m])) hit.Add(WaterPlanner.Pack(ix, iy, iz));
            // its foot ploughs through the ground; the rest of it is stopped there
            else if (!foot) stopped = true;
        }
        float swing = f.TipRate;
        if (hit.Count > 0)
        {
            // what it breaks takes from its swing; more than a third of its own weight stops it
            if (hit.Count * stride * 3 > f.Voxels.Count) stopped = true;
            else
            {
                Smash(f, hit);
                f.TipRate *= MathF.Max(.35f, 1 - 2.5f * hit.Count * stride / f.Voxels.Count);
            }
        }
        // whoever it swings into
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null || u.LastHurt >= f.TipFrom || u == f.By) continue;
            if (MathF.Abs(u.X - f.X) > f.R + u.T.BodyR + 2 || MathF.Abs(u.Z - f.Z) > f.R + u.T.BodyR + 2 || u.Y + u.T.BodyH < f.PY + 2) continue;
            if (!Touches(f, u, .5f)) continue;
            // how fast the building goes where it meets them: across its arm, the right hand's way about its axis
            float ux = u.X - f.PX, uy = u.Y + u.T.BodyH * .5f - f.PY, uz = u.Z - f.PZ;
            float vx = -az * uy, vy = az * ux - ax * uz, vz = ax * uy;
            float speed = swing * MathF.Sqrt(vx * vx + vy * vy + vz * vz);
            Crush(u, f, vx, vy, vz, speed, Blow(f, speed) * 1.4f);
        }
        if (!stopped && f.Turn < MathF.PI / 2) return;
        // come against what it cannot break, or lying over on the ground: a great one breaks up there and then
        float e = .02f;
        if (f.Voxels.Count >= 300 && f.TipRate * arm > 2 * VoxelWorld.PerMetre)
        {
            var (sx, sy, sz) = Turned(rx, ry, rz, f.AX, 0, f.AZ, MathF.Cos(th + e), MathF.Sin(th + e));
            f.VX = (sx - px) / e * f.TipRate; f.VY = (sy - py) / e * f.TipRate; f.VZ = (sz - pz) / e * f.TipRate;
            f.Tipping = f.Toppling = false;
            if (Shatter(f, f.TipRate * arm * 1.5f, force: true)) return;
        }
        // over: on from here as anything falls, its middle going the way the turn takes it, and turning as it turned
        var (qx, qy, qz) = Turned(rx, ry, rz, f.AX, 0, f.AZ, MathF.Cos(th + e), MathF.Sin(th + e));
        f.VX = (qx - px) / e * f.TipRate; f.VY = (qy - py) / e * f.TipRate; f.VZ = (qz - pz) / e * f.TipRate;
        f.Spin = f.TipRate;
        f.Tipping = f.Toppling = false;
        f.Since = Time - .2;
    }

    /// <summary>What a building going over swings into is broken out: thrown on in pieces the way it swings, and what is
    /// about it jarred.</summary>
    void Smash(Flier f, HashSet<int> hit)
    {
        var cells = new List<(int x, int y, int z)>(hit.Count);
        float cx = 0, cy = 0, cz = 0;
        foreach (int p in hit) { var v = WaterPlanner.Unpack(p); cells.Add(v); cx += v.Item1; cy += v.Item2; cz += v.Item3; }
        cx /= cells.Count; cy /= cells.Count; cz /= cells.Count;
        // how fast the building goes where it struck: across its arm from the edge it turns on, the right hand's way
        // about its axis
        float ux = cx - f.PX, uy = cy - f.PY, uz = cz - f.PZ;
        var (ax, _, az) = AxisOf(f);
        float vx = -az * uy * f.TipRate, vy = (az * ux - ax * uz) * f.TipRate, vz = ax * uy * f.TipRate;
        int room = MaxAloft - Aloft;
        foreach (var piece in Split(cells, 10))
        {
            float k = .7f + (float)Rng.NextDouble() * .5f;
            var p = piece.Count <= room ? Loose(piece, vx * k, vy * k + 2 * VoxelWorld.PerMetre, vz * k, f.Cause, room) : null;
            if (p != null) { p.Heap = true; p.By = f.By; room -= p.Voxels.Count; continue; }
            foreach (var (x, y, z) in piece) { byte m = World.Get(x, y, z); World.Set(x, y, z, Mat.Air); Fell?.Invoke(x, y, z, m); }
        }
        Jar(cx, cy, cz, 6, .45f);
    }

    /// <summary>
    /// A great piece come down hard breaks up where it lands: into pieces of about so many voxels each, thrown on a little
    /// the way it came and out from where it struck, to lie in heaps about it; what it lands on is jarred, and a great
    /// cloud of dust goes up. True if it broke so.
    /// </summary>
    bool Shatter(Flier f, float speed, bool force = false)
    {
        // the greater, the less it takes: a tower breaks up coming down a man's height, a block of a wall from higher
        float takes = MathF.Max(3, 6 - f.Voxels.Count / 2000f) * VoxelWorld.PerMetre;
        if (f.Intact || f.Unit != null || f.Voxels.Count < (force ? 60 : 300) || !force && speed < takes) return false;
        speed = MathF.Max(speed, 1);
        var (ax, ay, az) = AxisOf(f);
        float c = MathF.Cos(f.Turn), s = MathF.Sin(f.Turn);
        int n = Math.Clamp(f.Voxels.Count / 160, 2, 120);
        var seeds = new int[n];
        for (int i = 0; i < n; i++) seeds[i] = Rng.Next(f.Voxels.Count);
        var parts = new List<(sbyte dx, sbyte dy, sbyte dz, byte m)>[n];
        for (int i = 0; i < n; i++) parts[i] = new();
        foreach (var v in f.Voxels)
        {
            int best = 0, bd = int.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var o = f.Voxels[seeds[i]];
                int ex = v.dx - o.dx, ey = v.dy - o.dy, ez = v.dz - o.dz, d = ex * ex + ey * ey + ez * ez;
                if (d < bd) { bd = d; best = i; }
            }
            parts[best].Add(v);
        }
        Fliers.Remove(f);
        float hx = f.VX / speed, hz = f.VZ / speed;
        // where it struck: its leading edge, low
        float sx = f.X + hx * f.R * .5f, sz = f.Z + hz * f.R * .5f, sy = f.Y - f.R * .3f;
        int laid = 0;
        foreach (var part in parts)
        {
            if (part.Count == 0) continue;
            float mx = 0, my = 0, mz = 0;
            foreach (var (dx, dy, dz, _) in part) { mx += dx; my += dy; mz += dz; }
            mx /= part.Count; my /= part.Count; mz /= part.Count;
            int ox = (int)MathF.Round(mx), oy = (int)MathF.Round(my), oz = (int)MathF.Round(mz);
            var own = new List<(sbyte, sbyte, sbyte, byte)>(part.Count);
            foreach (var (dx, dy, dz, m) in part) own.Add(((sbyte)(dx - ox), (sbyte)(dy - oy), (sbyte)(dz - oz), m));
            var (px, py, pz) = Turned(ox, oy, oz, ax, ay, az, c, s);
            float wx = f.X + px, wy = f.Y + py + 1, wz = f.Z + pz;
            float outX = wx - sx, outZ = wz - sz, ol = MathF.Max(1, MathF.Sqrt(outX * outX + outZ * outZ));
            float kick = Math.Clamp(speed / (14 * VoxelWorld.PerMetre), .3f, 1) * (1.5f + (float)Rng.NextDouble() * 2.5f) * VoxelWorld.PerMetre;
            var piece = Hurl(own, wx, wy, wz, f.VX * .25f + outX / ol * kick, MathF.Abs(f.VY) * .1f + (1.5f + (float)Rng.NextDouble() * 2) * VoxelWorld.PerMetre, f.VZ * .25f + outZ / ol * kick, f.Cause);
            if (piece == null) continue;
            piece.AX = ax; piece.AY = ay; piece.AZ = az; piece.Turn = f.Turn; piece.Spin = (float)(Rng.NextDouble() - .5) * 3;
            piece.Marked = f.Marked; piece.Heap = true;
            // what one broke out spares it still, in pieces
            piece.By = f.By;
            piece.Since = Time - .1;
            laid += part.Count;
        }
        Collapsed?.Invoke(f.Voxels.Count, f.X, f.Y, f.Z);
        Jar(sx, sy, sz, 5 + MathF.Sqrt(f.Voxels.Count) / 8, Math.Min(1, speed / (12 * VoxelWorld.PerMetre)));
        return true;
    }

    /// <summary>
    /// A piece of something broken up lies down as a heap: each voxel of it, as it lay when it struck, comes down its column
    /// onto whatever is under it and runs off the heap to a lower neighbour as loose stone does, the lowest first; what
    /// was marked to be taken out is marked again where it lies.
    /// </summary>
    void LayHeap(Flier f, List<(int ox, int oy, int oz, byte m)> cells, int ix, int iy, int iz)
    {
        cells.Sort((a, b) => a.oy.CompareTo(b.oy));
        int wood = 0;
        foreach (var c in cells) if (c.m is Mat.Lumber or Mat.Wood or Mat.Char or Mat.Shingle or Mat.Thatch || Mat.IsTrunk(c.m)) wood++;
        bool floats = wood * 2 > cells.Count;
        var marked = f.Marked ? new VoxelList() : null;
        _settling = true;
        foreach (var (ox, oy, oz, b) in cells)
        {
            int x = Math.Clamp(ix + ox, 1, World.SX - 2), z = Math.Clamp(iz + oz, 1, World.SZ - 2), y = Math.Clamp(iy + oy, 2, World.SY - 2);
            // out of whatever it struck into first, a little way; deep in it, it is ground to dust
            int up = 0;
            for (; up <= 6 && y < World.SY - 2 && World.Get(x, y, z) is var at && at != Mat.Air && at != Mat.Water && !Mat.Passable[at]; up++) y++;
            if (up > 6) continue;
            var (lx, ly, lz) = Landing(x, y, z, floats);
            if (ly < 2) continue;
            World.Set(lx, ly, lz, b);
            if (Orders.At(lx, ly, lz) != 0) Orders.Clear(lx, ly, lz);
            marked?.Add(lx, ly, lz, Mat.Air);
        }
        _settling = false;
        if (marked is { Count: > 0 }) Orders.PlanRemove(marked);
    }

    /// <summary>
    /// Someone struck by a building going over: by all its weight coming so fast (voxels a second) along (vx, vy, vz), as
    /// any blunt thing strikes (<see cref="Strike"/>): a weight many times a body's meets all of it at once, breaks bones
    /// and crushes; it costs at least the health given.
    /// </summary>
    void Crush(Unit u, Flier f, float vx, float vy, float vz, float speed, float harm) =>
        Strike(u, MassOf(f), speed / VoxelWorld.PerMetre, vx, vy, vz, float.NaN, f.Cause, harm);
}
