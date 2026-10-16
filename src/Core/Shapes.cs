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
    /// <summary>Stone goes down as coursed brick with mortar joints; other materials as they are.</summary>
    public static byte Lay(byte mat, int x, int y, int z) => mat == Mat.Stone ? Brushes.Brick(x + z, y + 64, false) : mat;

    /// <summary>Stone and mortar count as one material when looking for a flat face.</summary>
    public static byte Family(byte m) => m == Mat.Mortar ? Mat.Stone : m;

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
        return L;
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
        return L;
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
    /// A whole tree from one of its voxels: trunk, boughs and crown, kept within reach of where it was clicked
    /// so a forest doesn't come down with it.
    /// </summary>
    public static VoxelList Tree(VoxelWorld w, int x0, int y0, int z0, int limit = 40000)
    {
        var L = new VoxelList();
        static bool Wood(byte m) => m == Mat.Trunk || m == Mat.Leaf || m == Mat.Foliage;
        if (!Wood(w.Get(x0, y0, z0))) return L;
        var seen = new HashSet<long> { Key(w, x0, y0, z0) };
        var queue = new Queue<(int x, int y, int z)>();
        queue.Enqueue((x0, y0, z0));
        while (queue.Count > 0 && L.Count < limit)
        {
            var (x, y, z) = queue.Dequeue();
            L.Add(x, y, z, Mat.Air);
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy, nz = z + dz;
                        if (!w.InBounds(nx, ny, nz) || Math.Abs(nx - x0) > 22 || Math.Abs(nz - z0) > 22) continue;
                        if (!Wood(w.Get(nx, ny, nz)) || !seen.Add(Key(w, nx, ny, nz))) continue;
                        queue.Enqueue((nx, ny, nz));
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
