using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>Settings of one stroke of a laying brush.</summary>
public struct Stroke
{
    public byte Mat;
    /// <summary>Thickness of lines and walls, diameter of brush dabs, in voxels.</summary>
    public int Width;
    /// <summary>Height of what is laid, in voxels.</summary>
    public int Height;
    /// <summary>Flat top at the start surface plus Height; otherwise Height above each column's own surface.</summary>
    public bool Level;
    /// <summary>Rectangles and circles: walls only, Width thick.</summary>
    public bool Hollow;
    /// <summary>Dig instead of lay: Height becomes the depth, and a levelled stroke digs to one flat floor.</summary>
    public bool Dig;
}

/// <summary>
/// The player's drawing brushes. There are no ready-made walls or towers: everything is drawn from these
/// primitives, and anything worth repeating is saved as a blueprint. Each brush only plans; Builder pays and lays.
/// </summary>
public static class Shapes
{
    /// <summary>Stone goes down as rough masonry in mortar; other materials as they are.</summary>
    public static byte Lay(byte mat, int x, int y, int z) => mat == Mat.Stone ? Masonry(x, y, z) : mat;

    /// <summary>Stone of any shade and mortar count as one material when looking for a flat face.</summary>
    public static byte Family(byte m) => Mat.IsMasonry(m) ? Mat.Stone : m;

    /// <summary>
    /// Coursed stone, not brick: rows three to five voxels high, and in each row stones of five to nine voxels with their
    /// joints falling anywhere against the row below; a joint a voxel wide between them, the stones in five near shades of
    /// grey. Some stones are set back a little in the face (see <see cref="Recess"/>), so the wall stands rough.
    /// </summary>
    public static byte Masonry(int x, int y, int z) => Masonry(x, y, z, out _);

    /// <summary>The same, and which stone (a number unique to it) the voxel belongs to; −1 for mortar.</summary>
    public static byte Masonry(int x, int y, int z, out int stone)
    {
        int course = CourseOf(y, out int y0);
        int ix = StoneAlong(x, course, 0, out int bx), iz = StoneAlong(z, course, 1, out int bz);
        stone = -1;
        // the bed of the course, and the head joints across it
        if (y == y0 || x == bx || z == bz) return Mat.Mortar;
        stone = (int)(Hash.H3(ix * 7 + course, iz * 13 - course, course * 3 + 1) * int.MaxValue);
        double shade = Hash.H3(ix * 11 + course * 5, iz * 3 + 7, course * 17);
        return shade < .2 ? Mat.StoneDark : shade < .4 ? Mat.StoneCool : shade < .62 ? Mat.Stone : shade < .82 ? Mat.StoneWarm : Mat.StoneLight;
    }

    /// <summary>Whether a stone is set back a voxel in the face of its wall.</summary>
    public static bool SetBack(int stone) => stone >= 0 && Hash.H3(stone, 5, 91) < .28;

    static readonly int[] CourseStart = MakeCourses();

    static int[] MakeCourses()
    {
        // rows three to five voxels high up to the top of the world (and below it, for anything dug under)
        var starts = new int[512];
        int y = -64;
        for (int c = 0; c < starts.Length; c++) { starts[c] = y; y += 3 + (int)(Hash.H3(c, 77, 3) * 2.99); }
        return starts;
    }

    static int CourseOf(int y, out int y0)
    {
        int lo = 0, hi = CourseStart.Length - 1;
        while (lo < hi) { int mid = (lo + hi + 1) / 2; if (CourseStart[mid] <= y) lo = mid; else hi = mid - 1; }
        y0 = CourseStart[lo];
        return lo;
    }

    /// <summary>The stone of a row a coordinate falls in, along x (axis 0) or z (axis 1), and where that stone begins.</summary>
    static int StoneAlong(int p, int course, int axis, out int start)
    {
        // stones seven voxels apart on average, each joint shifted by up to two either way, the rows offset from each other
        int off = (int)(Hash.H3(course, axis, 61) * 7);
        int i = FloorDiv(p + off, 7);
        for (int k = i + 1; k >= i - 1; k--)
        {
            int b = k * 7 - off + (int)(Hash.H3(k, course, 63 + axis) * 4.99) - 2;
            if (b <= p) { start = b; return k; }
        }
        start = (i - 1) * 7 - off;
        return i - 1;
    }

    /// <summary>
    /// Rakes out the mortar on the faces of a planned wall: where a joint meets the open air at the side it is left
    /// empty, a groove between the stones, so the masonry stands in relief. The top is left whole to walk on.
    /// </summary>
    public static VoxelList Recess(VoxelWorld w, VoxelList L)
    {
        if (L == null || L.Count == 0) return L;
        var planned = new HashSet<long>();
        for (int i = 0; i < L.Count; i++) if (L.M(i) != 0) planned.Add(Key(w, L.X(i), L.Y(i), L.Z(i)));
        bool Open(int x, int y, int z) => !planned.Contains(Key(w, x, y, z)) && Brushes.Free(w.Get(x, y, z));
        // the wall stands two voxels more behind each open side: a joint is raked into a thick wall, never through a thin one
        bool Side(int x, int y, int z, int dx, int dz) => !Open(x + dx, y, z + dz) || !Open(x - dx, y, z - dz) && !Open(x - 2 * dx, y, z - 2 * dz);
        bool Deep(int x, int y, int z) => Side(x, y, z, 1, 0) && Side(x, y, z, -1, 0) && Side(x, y, z, 0, 1) && Side(x, y, z, 0, -1);
        var o = new VoxelList();
        for (int i = 0; i < L.Count; i++)
        {
            int x = L.X(i), y = L.Y(i), z = L.Z(i);
            byte m = L.M(i);
            bool face = Open(x, y + 1, z) == false && (Open(x + 1, y, z) || Open(x - 1, y, z) || Open(x, y, z + 1) || Open(x, y, z - 1));
            if (face && Mat.IsMasonry(m) && Deep(x, y, z))
            {
                Masonry(x, y, z, out int stone);
                // a raked joint, or a stone set back in the face
                if (m == Mat.Mortar || SetBack(stone)) continue;
            }
            o.Add(x, y, z, m);
        }
        o.MetaBase = L.MetaBase; o.MetaPlat = L.MetaPlat;
        return o;
    }

    static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    /// <summary>Fills one column from its surface up to the top (exclusive), clearing plants above.
    /// Digging strokes instead mark everything diggable from the surface down to the floor.</summary>
    static void Column(VoxelWorld w, Site site, VoxelList L, int x, int z, in Stroke st, int levelTop)
    {
        if (x < 0 || x >= w.SX || z < 0 || z >= w.SZ || site.Forbidden(x, z)) return;
        if (st.Dig)
        {
            int surf = w.SurfAt(x, z), floor = Math.Max(3, st.Level ? levelTop : surf - st.Height);
            for (int y = surf - 1; y >= floor; y--)
                if (Labour.Diggable(w.Get(x, y, z))) L.Add(x, y, z, Mat.Air);
            if (floor < surf) Brushes.ClearVegAbove(w, L, x, z, surf);
            return;
        }
        int bottom = w.SurfAt(x, z);
        int top = st.Level ? levelTop : bottom + st.Height;
        // a trap floor lies flush with the ground the stroke began on, half a metre of poles and turf, over whatever is lower
        if (st.Mat == Mat.TrapDeck) { top = levelTop - st.Height; bottom = Math.Max(bottom, top - 4); }
        // stakes stand a hand apart, each a little taller or shorter than the next
        if (st.Mat == Mat.Stakes)
        {
            if (x % 3 != 0 || z % 3 != 0) return;
            top += (int)(Hash.H3(x, 7, z) * 3) - 1;
        }
        if (top <= bottom) return;
        int last = bottom;
        for (int y = Math.Max(1, bottom); y < top && y < w.SY; y++)
            if (Brushes.Free(w.Get(x, y, z))) { L.Add(x, y, z, Lay(st.Mat, x, y, z)); last = y + 1; }
        Brushes.ClearVegAbove(w, L, x, z, last);
    }

    /// <summary>The flat top a levelled stroke gets when it starts on this column.</summary>
    public static int LevelTop(VoxelWorld w, int x, int z, int height) =>
        w.SurfAt(Math.Clamp(x, 0, w.SX - 1), Math.Clamp(z, 0, w.SZ - 1)) + height;

    /// <summary>The flat floor a levelled digging stroke goes down to when it starts on this column.</summary>
    public static int LevelFloor(VoxelWorld w, int x, int z, int depth) =>
        w.SurfAt(Math.Clamp(x, 0, w.SX - 1), Math.Clamp(z, 0, w.SZ - 1)) - depth;

    /// <summary>The level a stroke works to: a top when laying, a floor when digging.</summary>
    public static int Level(VoxelWorld w, int x, int z, in Stroke st) => st.Dig ? LevelFloor(w, x, z, st.Height) : LevelTop(w, x, z, st.Height);

    /// <summary>A straight band from A to B, Width thick, with rounded ends so lines join cleanly at corners.</summary>
    public static VoxelList Line(VoxelWorld w, Site site, int ax, int az, int bx, int bz, in Stroke st, int levelTop)
    {
        var L = new VoxelList();
        double r = Math.Max(1, st.Width) / 2.0 + .25;
        double px = ax + .5, pz = az + .5, dx = bx - ax, dz = bz - az, len2 = dx * dx + dz * dz;
        int x0 = (int)Math.Floor(Math.Min(ax, bx) - r), x1 = (int)Math.Ceiling(Math.Max(ax, bx) + r);
        int z0 = (int)Math.Floor(Math.Min(az, bz) - r), z1 = (int)Math.Ceiling(Math.Max(az, bz) + r);
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                double cx = x + .5, cz = z + .5;
                double t = len2 > 0 ? Math.Clamp(((cx - px) * dx + (cz - pz) * dz) / len2, 0, 1) : 0;
                double ex = px + dx * t - cx, ez = pz + dz * t - cz;
                if (ex * ex + ez * ez > r * r) continue;
                Column(w, site, L, x, z, st, levelTop);
            }
        return !st.Dig && st.Mat == Mat.Stone ? Recess(w, L) : L;
    }

    /// <summary>A rectangle between two corners, solid or as walls Width thick.</summary>
    public static VoxelList Box(VoxelWorld w, Site site, int ax, int az, int bx, int bz, in Stroke st, int levelTop)
    {
        var L = new VoxelList();
        int x0 = Math.Min(ax, bx), x1 = Math.Max(ax, bx), z0 = Math.Min(az, bz), z1 = Math.Max(az, bz), t = Math.Max(1, st.Width);
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (st.Hollow && x - x0 >= t && x1 - x >= t && z - z0 >= t && z1 - z >= t) continue;
                Column(w, site, L, x, z, st, levelTop);
            }
        return !st.Dig && st.Mat == Mat.Stone ? Recess(w, L) : L;
    }

    /// <summary>A house's door: the outer face of its wall in the middle of the side facing the citadel, a metre over the ground, the way it faces, and how thick the wall is.</summary>
    public struct HouseDoor { public int X, Y, Z, NX, NZ, Thick; }

    /// <summary>
    /// A house drawn as a rectangle: walls of the stroke's material round it, half a metre thick whatever the brush's
    /// width, up to a flat top
    /// at least four metres over the highest ground under them (a door three metres high and a lintel over it), and a
    /// roof of logs over the room flush with the walls' top. Logs span two metres from a wall, so the room is under four
    /// metres across, and the door goes in an end wall (the one nearer the citadel): the long walls carry all the roof,
    /// and the lintel over the door none of it. The door is not left out of the wall: once the wall stands there, the
    /// gateway is cut and the gate hung (Sim.PlanHouse). Null, and why, when the room is too small to sleep in or too
    /// wide to roof.
    /// </summary>
    public static VoxelList House(VoxelWorld w, Site site, int ax, int az, int bx, int bz, in Stroke st, int levelTop, out HouseDoor door, out string why)
    {
        door = default; why = null;
        const int t = 4;
        int x0 = Math.Min(ax, bx), x1 = Math.Max(ax, bx), z0 = Math.Min(az, bz), z1 = Math.Max(az, bz);
        int inX = x1 - x0 + 1 - 2 * t, inZ = z1 - z0 + 1 - 2 * t;
        if (Math.Min(inX, inZ) < 16 || Math.Max(inX, inZ) < 24) { why = "дом мал: внутри нужно хотя бы 2 × 3 м"; return null; }
        if (Math.Min(inX, inZ) > 30) { why = "дом широк: брёвна крыши перекрывают меньше 4 м, рисуйте поуже"; return null; }
        if (Math.Max(x1 - x0, z1 - z0) > 150) { why = "дом велик: не длиннее 18 м"; return null; }
        int ground = 0;
        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) if (x >= 0 && z >= 0 && x < w.SX && z < w.SZ) ground = Math.Max(ground, w.SurfAt(x, z));
        int top = Math.Max(levelTop, ground + 32);
        var wall = st; wall.Width = t; wall.Level = true; wall.Hollow = true; wall.Dig = false;
        var L = new VoxelList();
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                bool room = x - x0 >= t && x1 - x >= t && z - z0 >= t && z1 - z >= t;
                if (!room) { Column(w, site, L, x, z, wall, top); continue; }
                if (x < 0 || z < 0 || x >= w.SX || z >= w.SZ || site.Forbidden(x, z)) continue;
                if (Brushes.Free(w.Get(x, top - 1, z))) L.Add(x, top - 1, z, Mat.Wood);
            }
        if (L.Count == 0) { why = "здесь не построить"; return null; }
        // the door: in the middle of the end wall nearer the citadel (of a square house, the side facing it)
        int cx = (x0 + x1) / 2, cz = (z0 + z1) / 2, dx = site.KeepX - cx, dz = site.KeepZ - cz;
        bool endsAlongX = inX > inZ || inX == inZ && Math.Abs(dx) > Math.Abs(dz);
        if (endsAlongX) door = new HouseDoor { X = dx > 0 ? x1 : x0, Z = cz, NX = dx > 0 ? 1 : -1 };
        else door = new HouseDoor { X = cx, Z = dz > 0 ? z1 : z0, NZ = dz > 0 ? 1 : -1 };
        door.Y = w.SurfAt(door.X, door.Z) + 8;
        door.Thick = t;
        return L;
    }

    /// <summary>A disc around a centre, solid or as a ring Width thick: round towers, wells, bastions.</summary>
    public static VoxelList Circle(VoxelWorld w, Site site, int cx, int cz, double radius, in Stroke st, int levelTop)
    {
        var L = new VoxelList();
        double R = Math.Max(.5, radius), inner = st.Hollow ? R - Math.Max(1, st.Width) : -1;
        int n = (int)Math.Ceiling(R);
        for (int z = cz - n; z <= cz + n; z++)
            for (int x = cx - n; x <= cx + n; x++)
            {
                double d = Brushes.Dist(x - cx, z - cz);
                if (d > R || d <= inner) continue;
                Column(w, site, L, x, z, st, levelTop);
            }
        return !st.Dig && st.Mat == Mat.Stone ? Recess(w, L) : L;
    }

    /// <summary>A ball of material, the freehand brush's round nib.</summary>
    public static VoxelList Ball(VoxelWorld w, Site site, int cx, int cy, int cz, double radius, byte mat)
    {
        var L = new VoxelList();
        int n = (int)Math.Ceiling(radius);
        for (int y = Math.Max(1, cy - n); y <= Math.Min(w.SY - 1, cy + n); y++)
            for (int z = Math.Max(0, cz - n); z <= Math.Min(w.SZ - 1, cz + n); z++)
                for (int x = Math.Max(0, cx - n); x <= Math.Min(w.SX - 1, cx + n); x++)
                {
                    if (Brushes.Dist(x - cx, y - cy, z - cz) > radius + .01 || site.Forbidden(x, z)) continue;
                    if (Brushes.Free(w.Get(x, y, z))) L.Add(x, y, z, Lay(mat, x, y, z));
                }
        return L;
    }

    /// <summary>Everything diggable inside a ball: the freehand pick.</summary>
    public static VoxelList BallDig(VoxelWorld w, Site site, int cx, int cy, int cz, double radius)
    {
        var L = new VoxelList();
        int n = (int)Math.Ceiling(radius);
        for (int y = Math.Max(3, cy - n); y <= Math.Min(w.SY - 1, cy + n); y++)
            for (int z = Math.Max(0, cz - n); z <= Math.Min(w.SZ - 1, cz + n); z++)
                for (int x = Math.Max(0, cx - n); x <= Math.Min(w.SX - 1, cx + n); x++)
                {
                    if (Brushes.Dist(x - cx, y - cy, z - cz) > radius + .01 || site.Forbidden(x, z)) continue;
                    if (Labour.Diggable(w.Get(x, y, z))) L.Add(x, y, z, Mat.Air);
                }
        return L;
    }

    /// <summary>
    /// A whole tree from one of its voxels: its wood first (the trunk and every bough and twig grown out of it), then the
    /// leaves hanging on that wood, each leaf going with the tree whose wood is nearer to it through the crown, so where
    /// crowns grow into each other the next tree keeps its own. Clicked on a crown, the tree is the one it hangs on.
    /// </summary>
    public static VoxelList Tree(VoxelWorld w, int x0, int y0, int z0, int limit = 80000)
    {
        var L = new VoxelList();
        byte m0 = w.Get(x0, y0, z0);
        if (!Mat.IsTrunk(m0) && !Mat.IsLeafy(m0)) return L;
        var seen = new HashSet<long>();
        var queue = new Queue<(int x, int y, int z, int d)>();
        void Around(int x, int y, int z, Action<int, int, int> f)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if ((dx | dy | dz) != 0 && w.InBounds(x + dx, y + dy, z + dz)) f(x + dx, y + dy, z + dz);
        }
        if (!Mat.IsTrunk(m0))
        {
            // the nearest wood through the leaves; a crown with none left comes down by itself
            var leaves = new List<(int x, int y, int z)>();
            bool found = false;
            seen.Add(Key(w, x0, y0, z0));
            queue.Enqueue((x0, y0, z0, 0));
            while (queue.Count > 0 && !found)
            {
                var (x, y, z, d) = queue.Dequeue();
                leaves.Add((x, y, z));
                Around(x, y, z, (nx, ny, nz) =>
                {
                    byte m = w.Get(nx, ny, nz);
                    if (found) return;
                    if (Mat.IsTrunk(m)) { found = true; x0 = nx; y0 = ny; z0 = nz; }
                    else if (Mat.IsLeafy(m) && d < 24 && seen.Add(Key(w, nx, ny, nz))) queue.Enqueue((nx, ny, nz, d + 1));
                });
            }
            if (!found)
            {
                foreach (var (x, y, z) in leaves) L.Add(x, y, z, Mat.Air);
                return L;
            }
            seen.Clear();
            queue.Clear();
        }
        // the wood
        seen.Add(Key(w, x0, y0, z0));
        queue.Enqueue((x0, y0, z0, 0));
        while (queue.Count > 0 && L.Count < limit)
        {
            var (x, y, z, _) = queue.Dequeue();
            L.Add(x, y, z, Mat.Air);
            Around(x, y, z, (nx, ny, nz) =>
            {
                if (Math.Abs(nx - x0) <= 40 && Math.Abs(nz - z0) <= 40 && Mat.IsTrunk(w.Get(nx, ny, nz)) && seen.Add(Key(w, nx, ny, nz))) queue.Enqueue((nx, ny, nz, 0));
            });
        }
        // the leaves within reach of it, nearer to it than to any other tree's wood met among them
        var wood = new List<(int x, int y, int z)>(L.Count);
        for (int i = 0; i < L.Count; i++) wood.Add((L.X(i), L.Y(i), L.Z(i)));
        foreach (var (x, y, z) in Crown(w, wood))
        {
            if (L.Count >= limit) break;
            L.Add(x, y, z, Mat.Air);
        }
        return L;
    }

    /// <summary>
    /// The leaves that go with this wood when it is felled or comes down: those within reach of it through the leaves
    /// (twenty-two steps), no nearer to the wood of another tree met among them than to it. Another tree's wood is any
    /// trunk not of this wood that the test (if given) says still stands; a leaf the other test says is this tree's
    /// anyway (marked to be felled with it) goes with it however near the other tree is. Each leaf counted once, nearest
    /// first.
    /// </summary>
    public static List<(int x, int y, int z)> Crown(VoxelWorld w, IReadOnlyList<(int x, int y, int z)> wood, Func<int, int, int, bool> stands = null, Func<int, int, int, bool> ours = null)
    {
        const int Reach = 22;
        var mine = new HashSet<long>(wood.Count);
        foreach (var (x, y, z) in wood) mine.Add(Key(w, x, y, z));
        var queue = new Queue<(int x, int y, int z, int d)>();
        var near = new Dictionary<long, int>();
        var order = new List<(int x, int y, int z, long k)>();
        var other = new List<(int x, int y, int z)>();
        var otherSeen = new HashSet<long>();
        foreach (var (x, y, z) in wood) queue.Enqueue((x, y, z, 0));
        while (queue.Count > 0)
        {
            var (x, y, z, d) = queue.Dequeue();
            if (d >= Reach) continue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy, nz = z + dz;
                        if ((dx | dy | dz) == 0 || !w.InBounds(nx, ny, nz)) continue;
                        byte m = w.Get(nx, ny, nz);
                        long k = Key(w, nx, ny, nz);
                        if (Mat.IsLeafy(m)) { if (near.TryAdd(k, d + 1)) { order.Add((nx, ny, nz, k)); queue.Enqueue((nx, ny, nz, d + 1)); } }
                        else if (Mat.IsTrunk(m) && !mine.Contains(k) && (stands == null || stands(nx, ny, nz)) && otherSeen.Add(k)) other.Add((nx, ny, nz));
                    }
        }
        // how near the other trees' wood is to the same leaves
        var theirs = new Dictionary<long, int>();
        foreach (var (x, y, z) in other) queue.Enqueue((x, y, z, 0));
        while (queue.Count > 0)
        {
            var (x, y, z, d) = queue.Dequeue();
            if (d >= Reach) continue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy, nz = z + dz;
                        if ((dx | dy | dz) == 0 || !w.InBounds(nx, ny, nz)) continue;
                        long k = Key(w, nx, ny, nz);
                        if (near.ContainsKey(k) && theirs.TryAdd(k, d + 1)) queue.Enqueue((nx, ny, nz, d + 1));
                    }
        }
        var crown = new List<(int x, int y, int z)>(order.Count);
        foreach (var (x, y, z, k) in order)
            if (!theirs.TryGetValue(k, out int dt) || near[k] <= dt || ours != null && ours(x, y, z)) crown.Add((x, y, z));
        return crown;
    }

    /// <summary>Trees a dig cuts into come down whole, not as stumps under floating crowns: adds the rest of each to the plan.</summary>
    public static VoxelList WithTrees(VoxelWorld w, VoxelList L)
    {
        if (L == null) return null;
        int n0 = L.Count;
        bool any = false;
        for (int i = 0; i < n0 && !any; i++) any = Mat.IsTrunk(w.Get(L.X(i), L.Y(i), L.Z(i)));
        if (!any) return L;
        var seen = new HashSet<long>();
        for (int i = 0; i < n0; i++) seen.Add(Key(w, L.X(i), L.Y(i), L.Z(i)));
        var felled = new HashSet<long>();
        for (int i = 0; i < n0; i++)
        {
            int x = L.X(i), y = L.Y(i), z = L.Z(i);
            if (!Mat.IsTrunk(w.Get(x, y, z)) || felled.Contains(Key(w, x, y, z))) continue;
            var tree = Tree(w, x, y, z);
            for (int t = 0; t < tree.Count; t++)
            {
                long k = Key(w, tree.X(t), tree.Y(t), tree.Z(t));
                felled.Add(k);
                if (seen.Add(k)) L.Add(tree.X(t), tree.Y(t), tree.Z(t), Mat.Air);
            }
        }
        return L;
    }

    /// <summary>Nib radius for a brush of the given width: one voxel at width 1, then half the width.</summary>
    public static double NibRadius(int width) => width <= 1 ? 0 : width / 2.0;

    /// <summary>
    /// The flat patch of one material around the face that was clicked, pushed out by `steps` voxels in the
    /// selected material. Raises a wall by a course, thickens it, or puts a wooden top on a stone one.
    /// </summary>
    public static VoxelList Extrude(VoxelWorld w, Site site, VoxelHit hit, int steps, byte mat, int maxFaces, out bool tooBig, bool dig = false)
    {
        tooBig = false;
        var L = new VoxelList();
        byte fam = Family(hit.M);
        int nx = hit.NX, ny = hit.NY, nz = hit.NZ;
        int ux = ny != 0 ? 1 : 0, uy = ny != 0 ? 0 : 1, uz = 0;
        int vx = nx != 0 ? 0 : (ny != 0 ? 0 : 1), vy = 0, vz = nx != 0 || ny != 0 ? 1 : 0;
        bool Exposed(int x, int y, int z)
        {
            if (!w.InBounds(x, y, z)) return false;
            byte m = w.Get(x, y, z);
            return m != 0 && Family(m) == fam && Brushes.Free(w.Get(x + nx, y + ny, z + nz));
        }
        if (!Exposed(hit.X, hit.Y, hit.Z)) return L;
        var seen = new HashSet<long> { Key(w, hit.X, hit.Y, hit.Z) };
        var face = new List<(int x, int y, int z)>();
        var queue = new Queue<(int x, int y, int z)>();
        queue.Enqueue((hit.X, hit.Y, hit.Z));
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            face.Add(p);
            if (face.Count > maxFaces) { tooBig = true; return null; }
            foreach (var (dx, dy, dz) in new[] { (ux, uy, uz), (-ux, -uy, -uz), (vx, vy, vz), (-vx, -vy, -vz) })
            {
                int x = p.x + dx, y = p.y + dy, z = p.z + dz;
                if (!Exposed(x, y, z) || !seen.Add(Key(w, x, y, z))) continue;
                queue.Enqueue((x, y, z));
            }
        }
        foreach (var (fx, fy, fz) in face)
            for (int k = 1; k <= steps; k++)
            {
                if (dig)
                {
                    // pushing the face in: a tunnel mouth, a step cut into a bank
                    int x = fx - nx * (k - 1), y = fy - ny * (k - 1), z = fz - nz * (k - 1);
                    if (!w.InBounds(x, y, z) || y < 3 || site.Forbidden(x, z) || !Labour.Diggable(w.Get(x, y, z))) break;
                    L.Add(x, y, z, Mat.Air);
                }
                else
                {
                    int x = fx + nx * k, y = fy + ny * k, z = fz + nz * k;
                    if (!w.InBounds(x, y, z) || site.Forbidden(x, z) || !Brushes.Free(w.Get(x, y, z))) break;
                    L.Add(x, y, z, Lay(mat, x, y, z));
                }
            }
        return L;
    }

    static long Key(VoxelWorld w, int x, int y, int z) => ((long)y * w.SZ + z) * w.SX + x;

    /// <summary>
    /// A ladder against the side face that was clicked: two rails and rungs every third voxel, from the ground in front of the
    /// face to a step above its top. It stands in the first cell clear of the wall, so a body can hang on it. With
    /// <paramref name="crush"/> it goes through leaves and grass in its way, as a siege tower's does, breaking them.
    /// </summary>
    public static VoxelList LadderAt(VoxelWorld w, Site site, VoxelHit hit, byte mat = Mat.Ladder, bool crush = false)
    {
        var L = new VoxelList();
        if (hit.NY != 0 || (hit.NX == 0 && hit.NZ == 0)) return L;
        int fx = hit.X + hit.NX, fz = hit.Z + hit.NZ;
        int tx = hit.NZ != 0 ? 1 : 0, tz = hit.NX != 0 ? 1 : 0;
        // clear of the face where it stands out further (a stone set back was clicked, or a raked joint), and out of
        // the wall's own cell if the wall fills most of it
        bool Face(int ax, int az)
        {
            for (int y = hit.Y - 4; y <= hit.Y + 8; y++)
                for (int t = -2; t <= 3; t++)
                    if (Blocking(w.Get(ax + tx * t, y, az + tz * t)) && Mat.IsPlayer[w.Get(ax + tx * t, y, az + tz * t)]) return true;
            return false;
        }
        for (int s = 0; s < 2 && Face(fx, fz); s++) { fx += hit.NX; fz += hit.NZ; }
        for (int s = 0; s < 8 && CellBlocked(w, fx >> 3, hit.Y >> 3, fz >> 3); s++) { fx += hit.NX; fz += hit.NZ; }
        if (!w.InBounds(fx, hit.Y, fz) || site.Forbidden(fx, fz)) return L;
        // up the wall to its top: its face is rough, so the stone behind a raked joint counts as the wall going on
        int top = hit.Y;
        while (top + 1 < w.SY && (Blocking(w.Get(hit.X, top + 1, hit.Z)) || Blocking(w.Get(hit.X - hit.NX, top + 1, hit.Z - hit.NZ)))) top++;
        int bottom = hit.Y;
        while (bottom > 1 && !Blocking(w.Get(fx, bottom - 1, fz))) bottom--;
        for (int y = bottom; y <= top + 2 && y < w.SY; y++)
            for (int t = -2; t <= 3; t++)
            {
                bool rail = t == -2 || t == 3, rung = y % 3 == 0 && y <= top;
                if (!rail && !rung) continue;
                int x = fx + tx * t, z = fz + tz * t;
                byte at = w.Get(x, y, z);
                bool room = at == Mat.Air || crush && (Mat.IsLeafy(at) || at is Mat.Tuft or Mat.Flower or Mat.Bloom);
                if (w.InBounds(x, y, z) && room && !site.Forbidden(x, z)) L.Add(x, y, z, mat);
            }
        return L;
    }

    static bool Blocking(byte m) => m != 0 && !Mat.Passable[m];

    /// <summary>A cell a quarter full of what blocks: a body cannot be in it.</summary>
    static bool CellBlocked(VoxelWorld w, int ci, int cy, int ck)
    {
        int n = 0;
        for (int y = cy << 3; y < (cy + 1) << 3; y++)
            for (int z = ck << 3; z < (ck + 1) << 3; z++)
                for (int x = ci << 3; x < (ci + 1) << 3; x++)
                    if (Blocking(w.Get(x, y, z)) && ++n >= NavGrid.SolidT) return true;
        return false;
    }

    /// <summary>The player's own voxels inside a ball, planned over in another material.</summary>
    public static VoxelList Repaint(VoxelWorld w, int cx, int cy, int cz, double radius, byte mat)
    {
        var L = new VoxelList();
        int n = (int)Math.Ceiling(radius);
        for (int y = Math.Max(1, cy - n); y <= Math.Min(w.SY - 1, cy + n); y++)
            for (int z = Math.Max(0, cz - n); z <= Math.Min(w.SZ - 1, cz + n); z++)
                for (int x = Math.Max(0, cx - n); x <= Math.Min(w.SX - 1, cx + n); x++)
                {
                    if (Brushes.Dist(x - cx, y - cy, z - cz) > radius + .01) continue;
                    byte m = w.Get(x, y, z);
                    if (m == 0 || !Mat.IsPlayer[m] || Family(m) == Family(mat)) continue;
                    L.Add(x, y, z, Lay(mat, x, y, z));
                }
        return L;
    }
}
