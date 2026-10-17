using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>A wall cross-section: what material sits at depth d0 (0 = outer face), height py, position al along the wall.</summary>
public sealed class WallProfile
{
    public readonly string Key, Name, Info;
    public readonly int W, H;
    readonly Func<int, int, int, byte> _fn;

    public WallProfile(string key, string name, string info, int w, int h, Func<int, int, int, byte> fn)
    {
        Key = key; Name = name; Info = info; W = w; H = h; _fn = fn;
    }

    public byte At(int d0, int py, int al) => _fn(d0, py, al);

    /// <summary>Price of one metre of this wall.</summary>
    public Cost CostPerMetre()
    {
        int st = 0, wd = 0, ea = 0;
        for (int al = 0; al < 16; al++)
            for (int d0 = 0; d0 < W; d0++)
                for (int py = 0; py < H; py++)
                {
                    byte m = At(d0, py, al);
                    if (m == Mat.Stone || m == Mat.Mortar) st++; else if (m == Mat.Wood) wd++; else if (m == Mat.Rammed) ea++;
                }
        return new Cost(st, wd, ea);
    }
}

/// <summary>
/// The player's brushes. Each one only plans: it returns the voxels it would write,
/// and <see cref="Builder"/> decides whether the treasury can pay for them.
/// </summary>
public static class Brushes
{
    /// <summary>JavaScript's Math.round (halves go up), so plans match the prototype voxel for voxel.</summary>
    public static int JsRound(double v) => (int)Math.Floor(v + 0.5);

    /// <summary>Brick courses 4 voxels high, bricks 8 long; on an outer face the mortar joints are recessed a voxel.</summary>
    public static byte Brick(int al, int py, bool face)
    {
        int course = (int)Math.Floor(py / 4.0), row = ((py % 4) + 4) % 4, off = (course & 1) * 4;
        bool joint = ((al + off) & 7) == 0;
        if (row == 3 || joint) return face ? Mat.Air : Mat.Mortar;
        return Mat.Stone;
    }

    public static readonly WallProfile Palisade = new("palisade", "Частокол", "брёвна в ряд, 2,5 м", 10, 44, (d0, py, al) =>
    {
        int post = (int)Math.Floor(al / 7.0);
        double dt = al - (post * 7 + 3), ds = d0 - 4.5;
        int ph = 36 + (int)Math.Floor(Hash.H3(post, 1, 7) * 7);
        if (py >= ph) return Mat.Air;
        double rad = py > ph - 7 ? 3.7 * (ph - py) / 7 : 3.7;
        return Math.Sqrt(dt * dt + ds * ds) < rad ? Mat.Wood : Mat.Air;
    });

    public static readonly WallProfile StoneWall = new("stone", "Каменная стена", "камень, 2,5 м и зубцы", 20, 52, (d0, py, al) =>
    {
        if (py < 40) return Brick(al, py + 64, d0 == 0 || d0 == 19);
        if (d0 < 6) return ((int)Math.Floor(al / 10.0) & 1) == 0 ? Mat.Stone : Mat.Air;
        return Mat.Air;
    });

    public static readonly WallProfile Rampart = new("rampart", "Вал с облицовкой", "камень снаружи, земля внутри, деревянные зубцы", 24, 46, (d0, py, al) =>
    {
        if (py < 32)
        {
            if (d0 < 4) return Brick(al, py + 64, d0 == 0);
            if (d0 >= 20) return Brick(al, py + 64, d0 == 23);
            return py == 31 ? Mat.Wood : Mat.Rammed;
        }
        if (d0 < 4) return ((int)Math.Floor(al / 10.0) & 1) == 0 ? Mat.Wood : Mat.Air;
        return Mat.Air;
    });

    public static readonly WallProfile EarthBank = new("earth", "Земляной вал", "одна земля, крутые склоны", 36, 40,
        (d0, py, al) => Math.Abs(d0 - 17.5) < 18 - py * .3 ? Mat.Rammed : Mat.Air);

    public static readonly WallProfile[] Profiles = { Palisade, StoneWall, Rampart, EarthBank };

    /// <summary>Masonry may go here: air, plants, or water it pushes aside.</summary>
    internal static bool Free(byte cur) => cur == Mat.Air || Mat.IsVeg[cur] || cur == Mat.Water || Mat.IsDecor[cur];

    /// <summary>Clears grass and branches sitting on top of a new column.</summary>
    internal static void ClearVegAbove(VoxelWorld w, VoxelList L, int x, int z, int from)
    {
        for (int y = from; y < Math.Min(w.SY, from + 48); y++)
        {
            byte m = w.Get(x, y, z);
            if (Mat.IsVeg[m]) L.Add(x, y, z, Mat.Air);
            else if (m != 0) break;
        }
    }

    /// <summary>A wall from A to B with the given cross-section; the face toward the citadel is the inner one.</summary>
    public static VoxelList Wall(VoxelWorld w, Site site, int ax, int az, int bx, int bz, WallProfile P)
    {
        double dx = bx - ax, dz = bz - az, len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 6) return null;
        double ux = dx / len, uz = dz / len, nx = -uz, nz = ux;
        double mx = (ax + bx) / 2.0, mz = (az + bz) / 2.0;
        bool innerPlus = nx * (site.KeepX - mx) + nz * (site.KeepZ - mz) > 0;
        var cols = new Dictionary<int, (double t, double s)>();
        var order = new List<int>();
        for (double t = 0; t <= len; t += .5)
            for (double s = 0; s < P.W; s += .5)
            {
                int x = JsRound(ax + ux * t + nx * (s - P.W / 2.0)), z = JsRound(az + uz * t + nz * (s - P.W / 2.0));
                if (x < 0 || x >= w.SX || z < 0 || z >= w.SZ) continue;
                int key = z * w.SX + x;
                if (cols.TryAdd(key, (t, s))) order.Add(key);
            }
        var L = new VoxelList();
        foreach (int key in order)
        {
            var (t, s) = cols[key];
            int x = key % w.SX, z = key / w.SX;
            if (site.Forbidden(x, z)) continue;
            int bse = w.SurfAt(x, z), sI = Math.Min(P.W - 1, (int)Math.Floor(s));
            int d0 = innerPlus ? sI : P.W - 1 - sI, al = JsRound(t), top = bse;
            for (int py = 0; py < P.H; py++)
            {
                int y = bse + py; if (y >= w.SY) break;
                byte m = P.At(d0, py, al); if (m == 0) continue;
                if (Free(w.Get(x, y, z))) { L.Add(x, y, z, m); top = y + 1; }
            }
            ClearVegAbove(w, L, x, z, top);
        }
        return L;
    }

    /// <summary>A round tower: brick shell, rammed-earth core, plank platform with merlons. Stands on the natural ground.</summary>
    public static VoxelList Tower(VoxelWorld w, Site site, int cx, int cz, int R, int H)
    {
        var L = new VoxelList();
        int bse = 0;
        for (int z = cz - R; z <= cz + R; z++)
            for (int x = cx - R; x <= cx + R; x++)
            {
                if (x < 0 || x >= w.SX || z < 0 || z >= w.SZ || Dist(x - cx + .5, z - cz + .5) > R) continue;
                bse = Math.Max(bse, w.GroundTop(x, z));
            }
        int plat = H - 14;
        for (int z = cz - R; z <= cz + R; z++)
            for (int x = cx - R; x <= cx + R; x++)
            {
                if (x < 0 || x >= w.SX || z < 0 || z >= w.SZ) continue;
                double d = Dist(x - cx + .5, z - cz + .5);
                if (d > R || site.Forbidden(x, z)) continue;
                int g = w.SurfAt(x, z), al = JsRound((Math.Atan2(z - cz, x - cx) + Math.PI) * R), top = g;
                for (int y = g; y < bse + H && y < w.SY; y++)
                {
                    int py = y - bse; byte m = 0;
                    if (d > R - 4) m = py < plat ? Brick(al, py + 64, d > R - 1) : (((int)Math.Floor(al / 9.0) & 1) == 0 ? Mat.Stone : Mat.Air);
                    else if (py < plat - 1) m = Mat.Rammed;
                    else if (py == plat - 1) m = Mat.Wood;
                    if (m != 0 && Free(w.Get(x, y, z))) { L.Add(x, y, z, m); top = y + 1; }
                }
                ClearVegAbove(w, L, x, z, top);
            }
        L.MetaBase = bse; L.MetaPlat = bse + plat;
        return L;
    }

    /// <summary>Stairs that reach topY at the start and step down a cell (8 voxels) for every 8 toward the end point.</summary>
    public static VoxelList Stairs(VoxelWorld w, Site site, int sx, int sz, int topY, int ex, int ez, byte mat)
    {
        double dx = ex - sx, dz = ez - sz, len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 3) return null;
        double ux = dx / len, uz = dz / len, nx = -uz, nz = ux;
        var cols = new Dictionary<int, double>();
        var order = new List<int>();
        for (double t = 0; t < 128; t += .5)
            for (double s = -6; s < 6; s += .5)
            {
                int x = JsRound(sx + ux * (t + 1) + nx * s), z = JsRound(sz + uz * (t + 1) + nz * s);
                if (x < 0 || x >= w.SX || z < 0 || z >= w.SZ) continue;
                int key = z * w.SX + x;
                if (cols.TryAdd(key, t)) order.Add(key);
            }
        var L = new VoxelList();
        foreach (int key in order)
        {
            double t = cols[key];
            int x = key % w.SX, z = key / w.SX;
            if (site.Forbidden(x, z)) continue;
            int g = w.SurfAt(x, z), h = topY - (int)Math.Floor(t / 8) * 8;
            if (h <= g) continue;
            for (int y = g; y < h && y < w.SY; y++)
            {
                byte m = mat == Mat.Stone ? Brick(JsRound(t), y + 64, false) : mat;
                if (Free(w.Get(x, y, z))) L.Add(x, y, z, m);
            }
        }
        return L.Count > 0 ? L : null;
    }

    /// <summary>A ball of material; stone gets laid as brick.</summary>
    public static VoxelList Sphere(VoxelWorld w, Site site, int cx, int cy, int cz, int r, byte m)
    {
        var L = new VoxelList();
        for (int y = Math.Max(1, cy - r); y <= Math.Min(w.SY - 1, cy + r); y++)
            for (int z = Math.Max(0, cz - r); z <= Math.Min(w.SZ - 1, cz + r); z++)
                for (int x = Math.Max(0, cx - r); x <= Math.Min(w.SX - 1, cx + r); x++)
                {
                    if (Dist(x - cx, y - cy, z - cz) > r + .3 || site.Forbidden(x, z)) continue;
                    if (Free(w.Get(x, y, z))) L.Add(x, y, z, m == Mat.Stone ? Brick(x + z, y + 64, false) : m);
                }
        return L;
    }

    /// <summary>Raises the ground in a disc up to targetY with rammed earth.</summary>
    public static VoxelList Fill(VoxelWorld w, Site site, int cx, int cz, int targetY, int r)
    {
        var L = new VoxelList();
        for (int z = cz - r; z <= cz + r; z++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= w.SX || z < 0 || z >= w.SZ || Dist(x - cx, z - cz) > r || site.Forbidden(x, z)) continue;
                for (int y = w.SurfAt(x, z); y < targetY && y < w.SY; y++) L.Add(x, y, z, Mat.Rammed);
            }
        return L;
    }

    public static double Dist(double a, double b) => Math.Sqrt(a * a + b * b);
    public static double Dist(double a, double b, double c) => Math.Sqrt(a * a + b * b + c * c);
}
