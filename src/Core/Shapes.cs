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

    /// <summary>
    /// A house's door: the outer face of its wall in the middle of a side, a metre over its floor (<see cref="Y"/> − 8 is
    /// the threshold, the top of the floor), the way it faces, how thick the wall is, and the top of the walls (where the
    /// roof begins, and a storey over it would).
    /// </summary>
    public struct HouseDoor
    {
        public int X, Y, Z, NX, NZ, Thick, Top;
        /// <summary>The side of its house it looks out of (<see cref="SideOf(int, int)"/>).</summary>
        public int Side => SideOf(NX, NZ);
    }

    /// <summary>
    /// The four sides of a rectangle a door may look out of: 0 its low z (north), 1 its high z (south), 2 its low x
    /// (west), 3 its high x (east); and the way each faces.
    /// </summary>
    public static (int nx, int nz) SideNormal(int side) => side switch { 0 => (0, -1), 1 => (0, 1), 2 => (-1, 0), _ => (1, 0) };
    public static int SideOf(int nx, int nz) => nz < 0 ? 0 : nz > 0 ? 1 : nx < 0 ? 2 : 3;
    /// <summary>Where a door looks, for the player: «на север»…</summary>
    public static readonly string[] SideWords = { "на север", "на юг", "на запад", "на восток" };

    /// <summary>The door in the middle of a side of a house's rectangle, its floor's top at <paramref name="ground"/> + 1.</summary>
    public static HouseDoor DoorOn(int side, int x0, int z0, int x1, int z1, int ground, int top, int along = int.MinValue)
    {
        int cx = along != int.MinValue ? along : (x0 + x1) / 2, cz = along != int.MinValue ? along : (z0 + z1) / 2;
        var d = side switch
        {
            0 => new HouseDoor { X = cx, Z = z0, NZ = -1 },
            1 => new HouseDoor { X = cx, Z = z1, NZ = 1 },
            2 => new HouseDoor { X = x0, Z = cz, NX = -1 },
            _ => new HouseDoor { X = x1, Z = cz, NX = 1 },
        };
        d.Y = ground + 1 + 8;
        d.Thick = HouseWall;
        d.Top = top;
        return d;
    }

    /// <summary>
    /// Whether a door can open out of a wall here: before it, a metre out from the wall and a little wider than its
    /// gateway (wherever that snaps to), ground in the valley with nothing over the threshold to head height — no wall,
    /// no trunk, no water, no other house (<paramref name="taken"/>: what another building holds or is marked to be built,
    /// by voxel) — and, just outside, ground to step down onto no more than three quarters of a metre down.
    /// </summary>
    public static bool DoorFree(VoxelWorld w, Site site, HouseDoor d, Func<int, int, int, bool> taken)
    {
        int threshold = d.Y - 8, across = d.NX != 0 ? d.Z : d.X;
        static bool Solid(byte m) => m != Mat.Air && !Mat.Passable[m] && !Mat.IsLeafy(m) || Mat.IsTrunk(m);
        for (int u = -Gateway.Half - 4; u <= Gateway.Half + 4; u++)
            for (int k = 1; k <= 8; k++)
            {
                int x = d.NX != 0 ? d.X + d.NX * k : across + u, z = d.NX != 0 ? across + u : d.Z + d.NZ * k;
                if (!w.InBounds(x, threshold, z) || !w.OnLand(x, z) || site.Forbidden(x, z)) return false;
                // what stands there over a step's height: a wall, a trunk, a house
                for (int y = threshold + 3; y < threshold + 20 && y < w.SY; y++)
                    if (Solid(w.Get(x, y, z))) return false;
                for (int y = threshold; y < threshold + 20 && y < w.SY; y++)
                    if (taken != null && taken(x, y, z)) return false;
                // the ground there: no higher than a step over the threshold, no water on it; just outside, no more
                // than three quarters of a metre down
                int top = threshold + 2;
                while (top > 1 && threshold - top <= 8 && !Solid(w.Get(x, top, z)) && w.Get(x, top, z) != Mat.Water) top--;
                if (w.Get(x, top, z) == Mat.Water || w.Get(x, top + 1, z) == Mat.Water) return false;
                if (k <= 2 && threshold - (top + 1) > 6) return false;
            }
        return true;
    }

    /// <summary>
    /// The sides a door of a house might go in, the likeliest first: the one asked for alone; else the long walls (the one
    /// facing the citadel first), then the ends. Not a side the house shares with a house beside it.
    /// </summary>
    static IEnumerable<int> DoorSides(Site site, int x0, int z0, int x1, int z1, int asked, int shared)
    {
        if (asked >= 0) { yield return asked; yield break; }
        int cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
        bool alongX = x1 - x0 >= z1 - z0;
        int towardZ = site.KeepZ > cz ? 1 : 0, towardX = site.KeepX > cx ? 3 : 2;
        int[] order = alongX ? new[] { towardZ, 1 - towardZ, towardX, 5 - towardX } : new[] { towardX, 5 - towardX, towardZ, 1 - towardZ };
        foreach (int s in order) if ((shared & 1 << s) == 0) yield return s;
    }

    /// <summary>
    /// Where the ground is under a column for a house to stand on: the top of it, but under whatever only overhangs it
    /// (the eaves of a house beside it, a gate's leaf, a thin run of anything with air under it).
    /// </summary>
    static int Underfoot(VoxelWorld w, int x, int z)
    {
        static bool Holds(byte m) => !Brushes.Free(m) && !Mat.IsLeaf(m);
        int s = w.SurfAt(x, z);
        for (int pass = 0; pass < 4; pass++)
        {
            int y = s - 1;
            while (y > 0 && s - y <= 6 && Holds(w.Get(x, y, z))) y--;
            if (y <= 0 || s - y > 6) return s;
            while (y > 0 && !Holds(w.Get(x, y, z))) y--;
            s = y + 1;
        }
        return s;
    }

    /// <summary>How thick a house's walls are, how high they stand over its floor, and how far its eaves reach out.</summary>
    public const int HouseWall = 4, HouseHeight = 28, Eaves = 2;

    /// <summary>
    /// A house drawn as a rectangle: walls of the stroke's material round it, half a metre thick whatever the brush's
    /// width, three and a half metres over the highest ground under them; a floor of logs laid over the ground
    /// inside up to that height; and a roof of logs in two slopes from eaves a quarter of a metre out over the long
    /// walls up to a ridge along the middle, the end walls going on up under it in gables (<see cref="Roof"/>). The room
    /// is under four metres across, so that a storey can be floored over it, and the door goes in the middle of a long
    /// wall, the one facing the citadel, under a lintel of logs (stone bears little held out over a doorway; the gables
    /// stand over the end walls). The door is not left out of the wall: once the wall stands all round it and over it,
    /// the gateway is cut and the gate hung (Sim.PlanHouse). <paramref name="arched"/> is what of it stands as an arch,
    /// held as long as it is joined to the rest: the roof, and the lintel. Null, and why, when the room is too small to
    /// sleep in or too wide to floor over.
    /// </summary>
    public static VoxelList House(VoxelWorld w, Site site, int ax, int az, int bx, int bz, in Stroke st, int levelTop, out HouseDoor door, out string why)
        => House(w, site, ax, az, bx, bz, st, levelTop, out door, out why, out _);

    public static VoxelList House(VoxelWorld w, Site site, int ax, int az, int bx, int bz, in Stroke st, int levelTop, out HouseDoor door, out string why, out VoxelList arched)
        => House(w, site, ax, az, bx, bz, st, levelTop, -1, null, 0, out door, out why, out arched);

    /// <summary>
    /// A house as above, its door on <paramref name="doorSide"/> (−1: the first side that opens onto free ground, see
    /// <see cref="DoorSides"/>), with <paramref name="taken"/> telling what other buildings hold, and no eaves out over
    /// the <paramref name="shared"/> sides (a bit for each side): those of its walls it shares with a house beside it.
    /// Null and why when the door has nowhere to open.
    /// </summary>
    public static VoxelList House(VoxelWorld w, Site site, int ax, int az, int bx, int bz, in Stroke st, int levelTop, int doorSide, Func<int, int, int, bool> taken, int shared,
        out HouseDoor door, out string why, out VoxelList arched)
    {
        door = default; why = null; arched = null;
        const int t = HouseWall;
        int x0 = Math.Min(ax, bx), x1 = Math.Max(ax, bx), z0 = Math.Min(az, bz), z1 = Math.Max(az, bz);
        if (!HouseSize(x1 - x0 + 1, z1 - z0 + 1, out why)) return null;
        // the highest ground under it: not over the wall it shares with a house beside it, nor over the leaves of that
        // one's door hung outside it (walled up and moved when this one is put up)
        int ground = 0;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (x < 0 || z < 0 || x >= w.SX || z >= w.SZ) continue;
                if ((shared & 1) != 0 && z < z0 + t || (shared & 2) != 0 && z > z1 - t || (shared & 4) != 0 && x < x0 + t || (shared & 8) != 0 && x > x1 - t) continue;
                ground = Math.Max(ground, Underfoot(w, x, z));
            }
        int top = ground + 1 + HouseHeight;
        var wall = st; wall.Width = t; wall.Level = true; wall.Hollow = true; wall.Dig = false;
        var L = new VoxelList();
        arched = new VoxelList();
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                bool room = x - x0 >= t && x1 - x >= t && z - z0 >= t && z1 - z >= t;
                if (x < 0 || z < 0 || x >= w.SX || z >= w.SZ || site.Forbidden(x, z)) continue;
                // the walls from the ground up, under the eaves of a house beside it too
                if (!room)
                {
                    int last = Underfoot(w, x, z);
                    for (int y = Math.Max(1, last); y < top && y < w.SY; y++)
                        if (Brushes.Free(w.Get(x, y, z))) { L.Add(x, y, z, Lay(wall.Mat, x, y, z)); last = y + 1; }
                    Brushes.ClearVegAbove(w, L, x, z, last);
                    continue;
                }
                // the floor: logs over the ground inside, up to the highest ground under the house
                for (int y = Math.Max(1, Underfoot(w, x, z)); y <= ground; y++) if (Brushes.Free(w.Get(x, y, z))) L.Add(x, y, z, Mat.Wood);
            }
        if (L.Count == 0) { why = "здесь не построить"; return null; }
        // the door: in the middle of a side that opens onto free ground — a long wall (the ridge runs along them), the
        // citadel's side first — never into a wall or another house standing before it
        bool found = false;
        foreach (int side in DoorSides(site, x0, z0, x1, z1, doorSide, shared))
        {
            var d = DoorOn(side, x0, z0, x1, z1, ground, top);
            if (!DoorFree(w, site, d, taken)) continue;
            door = d;
            found = true;
            break;
        }
        if (!found)
        {
            why = doorSide >= 0 ? $"дверь {SideWords[doorSide]} упрётся в стену, дом или обрыв: {{Rotate}} — другая сторона" : "двери некуда открыться: со всех сторон стена, дом, вода или обрыв";
            return null;
        }
        Roof(w, site, L, arched, x0, z0, x1, z1, top, st.Mat, shared);
        // the lintel: three courses of logs over where the doorway will be, a metre beyond it either side (the gateway
        // snaps to the cells, half a metre either way), standing as an arch
        var lintel = new HashSet<long>();
        for (int k = 0; k < t; k++)
            for (int u = -Gateway.Half - 8; u <= Gateway.Half + 8; u++)
                for (int y = door.Y - 8 + Gateway.Height; y < Math.Min(top, door.Y - 8 + Gateway.Height + 3); y++)
                {
                    int x = door.NX != 0 ? door.X - door.NX * k : door.X + u, z = door.NX != 0 ? door.Z + u : door.Z - door.NZ * k;
                    lintel.Add(Key(w, x, y, z));
                }
        var o = new VoxelList();
        for (int i = 0; i < L.Count; i++)
        {
            int x = L.X(i), y = L.Y(i), z = L.Z(i);
            byte m = lintel.Contains(Key(w, x, y, z)) ? Mat.Wood : L.M(i);
            // windows in the outer face of the long walls, over head height, clear of the door and the corners
            if (m != Mat.Wood || !lintel.Contains(Key(w, x, y, z))) m = Windowed(x, y, z, m, x0, z0, x1, z1, ground, door);
            o.Add(x, y, z, m);
            if (m == Mat.Wood && lintel.Contains(Key(w, x, y, z))) arched.Add(x, y, z, m);
        }
        return o;
    }

    /// <summary>
    /// What a voxel of a house's wall is laid as: a window where it is in the outer face of a long wall, three voxels
    /// wide and five high from a metre and a third over the floor (the dark of the room between two shutters, a board
    /// over them), one every two metres along the wall, none within a metre of the door or of the corners; else what
    /// it was.
    /// </summary>
    static byte Windowed(int x, int y, int z, byte m, int x0, int z0, int x1, int z1, int ground, HouseDoor door)
    {
        if (m == Mat.Wood && y <= ground) return m;
        bool alongX = x1 - x0 >= z1 - z0;
        int c = alongX ? z : x, c0 = alongX ? z0 : x0, c1 = alongX ? z1 : x1, l = alongX ? x : z, l0 = alongX ? x0 : z0, l1 = alongX ? x1 : z1;
        if (c != c0 && c != c1) return m;
        int above = y - (ground + 1);
        if (above < 11 || above > 15) return m;
        int dl = door.NX != 0 ? door.Z : door.X;
        bool doorSide = door.NX != 0 ? c == (door.NX > 0 ? c1 : c0) : c == (door.NZ > 0 ? c1 : c0);
        if (doorSide && Math.Abs(l - dl) <= Gateway.Half + 8) return m;
        int from = l - (l0 + HouseWall + 6);
        if (from < 0 || l > l1 - HouseWall - 6 || from % 16 > 2) return m;
        // the dark of the room between two shutters, a board over them
        bool glass = from % 16 == 1 && above < 15, stone = Mat.IsMasonry(m) || m == Mat.Rammed;
        if (!stone && m != Mat.Wood) return m;
        return glass ? (stone ? Mat.Window : Mat.WindowLog) : stone ? Mat.Shutter : Mat.ShutterLog;
    }

    /// <summary>Whether a house of this outer size can be built: room enough inside to sleep in, and not too wide to floor over.</summary>
    public static bool HouseSize(int sx, int sz, out string why)
    {
        int inX = sx - 2 * HouseWall, inZ = sz - 2 * HouseWall;
        why = null;
        if (Math.Min(inX, inZ) < 16 || Math.Max(inX, inZ) < 24) why = "дом мал: внутри нужно хотя бы 2 × 3 м";
        else if (Math.Min(inX, inZ) > 30) why = "дом широк: внутри не шире 3,75 м, рисуйте поуже";
        else if (Math.Max(sx, sz) > 151) why = "дом велик: не длиннее 18 м";
        return why == null;
    }

    /// <summary>
    /// The roof of a house over the rectangle of its walls, standing on them at <paramref name="top"/>: two slopes of
    /// logs, a voxel thick, from eaves a quarter of a metre out over the long walls up to a ridge along the middle, a
    /// voxel up for every two in (as the members of an arch, they hold at their corners); the end walls go on up under it
    /// as gables of their own material. Into the plan, and the slopes into <paramref name="arched"/>.
    /// </summary>
    public static void Roof(VoxelWorld w, Site site, VoxelList L, VoxelList arched, int x0, int z0, int x1, int z1, int top, byte wallMat, int shared = 0)
    {
        const int t = HouseWall, e = Eaves;
        bool alongX = x1 - x0 >= z1 - z0;
        int c0 = alongX ? z0 : x0, c1 = alongX ? z1 : x1, l0 = alongX ? x0 : z0, l1 = alongX ? x1 : z1;
        // over a wall shared with a house beside it, no eaves: they would reach out over the other's room
        bool shareC0 = (shared & (alongX ? 1 : 4)) != 0, shareC1 = (shared & (alongX ? 2 : 8)) != 0;
        bool shareL0 = (shared & (alongX ? 4 : 1)) != 0, shareL1 = (shared & (alongX ? 8 : 2)) != 0;
        int cmin = c0 - e, cmax = c1 + e, dmax = (cmax - cmin) / 2;
        for (int k = 0; 2 * k <= dmax; k++)
        {
            int y = top + k;
            if (y >= w.SY - 1) break;
            for (int c = cmin; c <= cmax; c++)
            {
                int d = Math.Min(c - cmin, cmax - c);
                // the slope: two voxels in at each course (the eaves and the wall's outer half at the first), all of
                // what is left at the ridge; the gable: under the slopes, over the end walls
                bool slope = k == 0 ? d <= e + 1 : d >= 2 * k && (d <= 2 * k + 1 || 2 * k + 1 >= dmax);
                bool gable = !slope && d > 2 * k + 1 && c >= c0 && c <= c1;
                if (!slope && !gable) continue;
                if (slope && (c < c0 && shareC0 || c > c1 && shareC1)) continue;
                for (int l = slope ? (shareL0 ? l0 : l0 - e) : l0; l <= (slope ? (shareL1 ? l1 : l1 + e) : l1); l++)
                {
                    if (gable && l - l0 >= t && l1 - l >= t) continue;
                    int x = alongX ? l : c, z = alongX ? c : l;
                    if (!w.InBounds(x, y, z) || site.Forbidden(x, z) || !Brushes.Free(w.Get(x, y, z))) continue;
                    byte m = slope ? Mat.Wood : Lay(wallMat, x, y, z);
                    L.Add(x, y, z, m);
                    if (slope) arched.Add(x, y, z, m);
                }
            }
        }
    }

    /// <summary>
    /// Where a house's smoke goes out: under the ridge over its far gable, where a smoke hole is left in old houses with
    /// no chimney (the ridge's voxel there; its smoke leaves from over it).
    /// </summary>
    public static (int x, int z, int top) SmokeHole(int x0, int z0, int x1, int z1, int top)
    {
        bool alongX = x1 - x0 >= z1 - z0;
        int c0 = alongX ? z0 : x0, c1 = alongX ? z1 : x1, l1 = alongX ? x1 : z1;
        int c = (c0 + c1) / 2, l = l1 - 2, ridge = top + (c1 - c0 + 2 * Eaves) / 2 / 2;
        return alongX ? (l, c, ridge) : (c, l, ridge);
    }

    /// <summary>
    /// What of a house stands over its walls' top: its roof and gables, and whatever else was laid up there, to be taken
    /// down before a storey goes up on it.
    /// </summary>
    public static VoxelList OverHouse(VoxelWorld w, int x0, int z0, int x1, int z1, int top)
    {
        var L = new VoxelList();
        for (int z = z0 - Eaves; z <= z1 + Eaves; z++)
            for (int x = x0 - Eaves; x <= x1 + Eaves; x++)
                for (int y = top; y < Math.Min(w.SY - 1, top + HouseHeight + 4); y++)
                {
                    // the eaves outside the walls are left, a ledge between the storeys
                    if (y == top && (x < x0 || x > x1 || z < z0 || z > z1)) continue;
                    if (!w.InBounds(x, y, z)) continue;
                    byte m = w.Get(x, y, z);
                    if (m != Mat.Air && Mat.IsPlayer[m] && Labour.Diggable(m)) L.Add(x, y, z, Mat.Air);
                }
        return L;
    }

    /// <summary>
    /// A storey over a house whose walls stand to <paramref name="lowerTop"/>, its roof taken down: a floor of logs in the
    /// top two courses of the room below, held as an arch, but for a hatch over a ladder set against one long wall from
    /// the floor below (<paramref name="ladder"/>, laid first); the walls going on up as high again, and a roof of their
    /// own over them (<see cref="Roof"/>). The ladder stands against the long wall away from the citadel, the door being
    /// in the other. The walls' top is <paramref name="newTop"/>.
    /// </summary>
    public static VoxelList Storey(VoxelWorld w, Site site, int x0, int z0, int x1, int z1, int lowerTop, byte wallMat, out VoxelList arched, out VoxelList ladder, out int newTop)
        => Storey(w, site, x0, z0, x1, z1, lowerTop, wallMat, 0, 0, out arched, out ladder, out newTop);

    /// <summary>A storey as above, its ladder against a long wall with no door in it (<paramref name="doors"/>, a bit for each side), and no eaves over the <paramref name="shared"/> sides.</summary>
    public static VoxelList Storey(VoxelWorld w, Site site, int x0, int z0, int x1, int z1, int lowerTop, byte wallMat, int doors, int shared, out VoxelList arched, out VoxelList ladder, out int newTop)
    {
        const int t = HouseWall;
        var L = new VoxelList();
        arched = new VoxelList();
        newTop = lowerTop + HouseHeight;
        bool alongX = x1 - x0 >= z1 - z0;
        // the ladder: against the inner face of the long wall away from the citadel (the door is in the other), a metre
        // and a half in from an end wall
        bool far0 = alongX ? site.KeepZ > (z0 + z1) / 2 : site.KeepX > (x0 + x1) / 2;
        // (unless the door is in that one and not the other)
        int farSide = alongX ? (far0 ? 0 : 1) : (far0 ? 2 : 3), nearSide = alongX ? 1 - farSide : 5 - farSide;
        if ((doors & 1 << farSide) != 0 && (doors & 1 << nearSide) == 0) far0 = !far0;
        var hit = alongX
            ? new VoxelHit { X = x0 + t + 12, Y = lowerTop - 20, Z = far0 ? z0 + t - 1 : z1 - t + 1, NZ = far0 ? 1 : -1 }
            : new VoxelHit { X = far0 ? x0 + t - 1 : x1 - t + 1, Y = lowerTop - 20, Z = z0 + t + 12, NX = far0 ? 1 : -1 };
        ladder = LadderAt(w, site, hit);
        var hatch = new HashSet<int>();
        for (int i = 0; i < ladder.Count; i++) hatch.Add((ladder.Z(i) >> 3) * 4096 + (ladder.X(i) >> 3));
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (x < 0 || z < 0 || x >= w.SX || z >= w.SZ || site.Forbidden(x, z)) continue;
                bool room = x - x0 >= t && x1 - x >= t && z - z0 >= t && z1 - z >= t;
                if (!room)
                {
                    for (int y = lowerTop; y < newTop && y < w.SY - 1; y++) if (Brushes.Free(w.Get(x, y, z))) L.Add(x, y, z, Lay(wallMat, x, y, z));
                    continue;
                }
                if (hatch.Contains((z >> 3) * 4096 + (x >> 3))) continue;
                for (int y = lowerTop - 2; y < lowerTop; y++)
                    if (Brushes.Free(w.Get(x, y, z))) { L.Add(x, y, z, Mat.Wood); arched.Add(x, y, z, Mat.Wood); }
            }
        Roof(w, site, L, arched, x0, z0, x1, z1, newTop, wallMat, shared);
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
