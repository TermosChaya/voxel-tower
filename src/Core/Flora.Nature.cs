using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The small things that grow and lie about a valley by themselves, set out by MapGen.Nature.cs: hazel, berry bushes,
/// bilberry and juniper; trees that came down long ago, root plate and all, and the stumps of others; an anthill, a
/// bird's nest, the hollow in an old trunk. They are made of the world's own stuff: a fallen trunk is timber the workers
/// saw up for logs as they do a felled tree, a bush is leaves that burn, an anthill is earth.
/// </summary>
public static partial class Flora
{
    /// <summary>A hazel: a vase of leafy clumps on many stems, taller than it is wide, a man's height or more.</summary>
    public static void Hazel(VoxelWorld w, int x0, int z0, int h)
    {
        int b = w.SurfAt(x0, z0);
        var R = new Mulberry32((uint)(x0 * 29 + z0 * 83 + 11));
        double wide = h * .42;
        // the lowest clump sits on the ground; the others stand out and up from it, as the stems spread
        Clump(w, x0 + .5, b + h * .24, z0 + .5, wide * .7, h * .3, Mat.Foliage, Mat.FoliageLight, .8);
        for (int k = 0, n = 4 + (int)(R.Next() * 3); k < n; k++)
        {
            double a = R.Next() * 6.28, t = .38 + R.Next() * .45, off = wide * (.2 + t * .5);
            Clump(w, x0 + .5 + Math.Cos(a) * off, b + h * t, z0 + .5 + Math.Sin(a) * off, wide * (.45 + R.Next() * .2), h * (.22 + R.Next() * .08),
                Mat.Foliage, Mat.FoliageLight, .8, .1);
        }
    }

    /// <summary>
    /// A berry bush knee to waist high, raspberry or blackcurrant: a few lumpy clumps half sunk into the ground, berries
    /// hanging among its outer leaves.
    /// </summary>
    public static void BerryBush(VoxelWorld w, int x0, int z0, int r, byte berry)
    {
        int b = w.SurfAt(x0, z0);
        var R = new Mulberry32((uint)(x0 * 47 + z0 * 13 + 3));
        var laid = new List<(int x, int y, int z)>();
        for (int k = 0, n = 3 + (int)(R.Next() * 3); k < n; k++)
        {
            double a = R.Next() * 6.28, off = k == 0 ? 0 : r * (.45 + R.Next() * .45), rr = r * (k == 0 ? 1 : .6 + R.Next() * .3);
            Clump(w, x0 + .5 + Math.Cos(a) * off, b + rr * .5, z0 + .5 + Math.Sin(a) * off, rr, rr * .8, Mat.Foliage, Mat.FoliageLight, .8, 0, laid);
        }
        Berries(w, laid, berry, .16);
    }

    /// <summary>Bilberry under the pines: a low carpet of small leaves a hand or two high, dark blue berries in it.</summary>
    public static void Bilberry(VoxelWorld w, int x0, int z0, int r)
    {
        int b = w.SurfAt(x0, z0);
        var R = new Mulberry32((uint)(x0 * 61 + z0 * 7 + 5));
        var laid = new List<(int x, int y, int z)>();
        for (int k = 0, n = 3 + (int)(R.Next() * 4); k < n; k++)
        {
            double a = R.Next() * 6.28, off = r * Math.Sqrt(R.Next()) * .8, rr = 2.2 + R.Next() * 1.6;
            int g = w.SurfAt(x0 + (int)(Math.Cos(a) * off), z0 + (int)(Math.Sin(a) * off));
            if (Math.Abs(g - b) > 3) continue;
            Clump(w, x0 + .5 + Math.Cos(a) * off, g + .4, z0 + .5 + Math.Sin(a) * off, rr, 1.5 + R.Next() * .6, Mat.Foliage, Mat.FoliageLight, .5, -.1, laid);
        }
        Berries(w, laid, Mat.BerryBlue, .12);
    }

    /// <summary>Berries on the outside of what a bush laid: on the leaves with open air beside or above them.</summary>
    static void Berries(VoxelWorld w, List<(int x, int y, int z)> laid, byte berry, double share)
    {
        foreach (var (x, y, z) in laid)
        {
            if (Hash.H3(x * 3 + 1, y * 7, z * 5) >= share || !Mat.IsLeafy(w.Get(x, y, z))) continue;
            bool open = w.Get(x, y + 1, z) == Mat.Air || w.Get(x + 1, y, z) == Mat.Air || w.Get(x - 1, y, z) == Mat.Air
                || w.Get(x, y, z + 1) == Mat.Air || w.Get(x, y, z - 1) == Mat.Air;
            if (open) w.SetRaw(x, y, z, berry);
        }
    }

    /// <summary>
    /// Juniper of the dry slopes, dark and blue-green: a narrow column a man's height or more, or one spreading low over
    /// the ground.
    /// </summary>
    public static void Juniper(VoxelWorld w, int x0, int z0, int h, bool spreading)
    {
        int b = w.SurfAt(x0, z0);
        var R = new Mulberry32((uint)(x0 * 37 + z0 * 53 + 7));
        if (spreading)
        {
            for (int k = 0, n = 3 + (int)(R.Next() * 2); k < n; k++)
            {
                double a = R.Next() * 6.28, off = k == 0 ? 0 : h * (.2 + R.Next() * .2), rr = h * (.3 + R.Next() * .12);
                Clump(w, x0 + .5 + Math.Cos(a) * off, b + .6, z0 + .5 + Math.Sin(a) * off, rr, 2 + R.Next(), Mat.Juniper, Mat.NeedleLight, .6, -.2);
            }
            return;
        }
        // a column of clumps, widest a little above the ground and drawn in to a blunt point
        double lean = R.Next() * 6.28, ll = R.Next() * 1.5;
        for (int k = 0, n = Math.Max(4, h / 2); k < n; k++)
        {
            double t = k / (double)(n - 1), r = (2.4 + h * .1) * (1 - t * .7) * (t < .15 ? .85 + t : 1);
            Clump(w, x0 + .5 + Math.Cos(lean) * ll * t, b + 1 + h * .88 * t, z0 + .5 + Math.Sin(lean) * ll * t, r, Math.Max(2.5, h * .2), Mat.Juniper, Mat.NeedleLight, .7, -.2);
        }
    }

    /// <summary>
    /// Whether a trunk <paramref name="len"/> long and <paramref name="r"/> thick can lie on the ground from (x0, z0) the way
    /// (dx, dz): no other tree's wood within its reach, no water under it, nothing built, and the ground along it neither
    /// rising through it nor falling away far below it. Gives the heights of its axis at the foot and the top.
    /// </summary>
    public static bool RoomToLie(VoxelWorld w, int x0, int z0, double dx, double dz, int len, double r, out double yFoot, out double yTop)
    {
        int g0 = w.SurfAt(x0, z0), g1 = w.SurfAt(x0 + (int)Math.Round(dx * len), z0 + (int)Math.Round(dz * len));
        yFoot = g0 + r * .8; yTop = g1 + r * .45;
        int reach = (int)Math.Ceiling(r) + 2;
        for (int s = -3; s <= len + 2; s++)
        {
            double t = Math.Clamp(s / (double)len, 0, 1), ay = yFoot + (yTop - yFoot) * t;
            for (int q = -reach; q <= reach; q++)
            {
                int x = (int)Math.Floor(x0 + .5 + dx * s - dz * q), z = (int)Math.Floor(z0 + .5 + dz * s + dx * q);
                if (!w.OnLand(x, z)) return false;
                int g = w.SurfAt(x, z);
                if (Math.Abs(q) <= r)
                {
                    // the ground under the trunk: a hump it rests over, a dip it spans, no more
                    if (g > ay + r * .3 || g < ay - r - 5) return false;
                    if (w.Get(x, g, z) == Mat.Water || w.Get(x, g - 1, z) == Mat.Water) return false;
                }
                for (int y = (int)(ay - r - 2); y <= ay + r + 3; y++)
                {
                    byte m = w.Get(x, y, z);
                    if (Mat.IsTrunk(m) || m != Mat.Air && (Mat.IsPlayer[m] || Mat.IsDecor[m])) return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// A tree that came down long ago (see <see cref="RoomToLie"/> for where it can): its trunk along the ground the way it
    /// fell, thinning from its butt to the top where it broke, splinters standing out of the break; a few boughs broken short
    /// stand up out of it or lie along the ground. Torn up by the roots, its root plate stands on end at the foot, earth
    /// in it and the roots' ends sticking out all round; the pit it came out of is the caller's to dig behind it.
    /// </summary>
    public static void FallenTree(VoxelWorld w, int x0, int z0, double dx, double dz, int len, double r, double yFoot, double yTop, byte bark, bool roots)
    {
        Sprout();
        var R = new Mulberry32((uint)(x0 * 89 + z0 * 37 + 17));
        double ax = x0 + .5, az = z0 + .5;
        (double x, double y, double z) At(double s)
        {
            double t = Math.Clamp(s / len, 0, 1);
            return (ax + dx * s, yFoot + (yTop - yFoot) * t, az + dz * s);
        }
        double Thick(double s) => s < 4 ? r * (1.18 - .045 * s) : r * (1 - .48 * s / len);
        for (double s = 0; s <= len; s += .5)
        {
            var (x, y, z) = At(s);
            Ball(w, x, y, z, Thick(s), bark);
        }
        // the break at its top: splinters along the way it lay, the longest on one side
        var (tx, ty, tz) = At(len);
        for (int k = 0; k < 4; k++)
        {
            double a = R.Next() * 6.28, off = Thick(len) * .6, sl = 2 + R.Next() * 4;
            double sx = tx + Math.Cos(a) * off * -dz, sz = tz + Math.Cos(a) * off * dx, sy = ty + Math.Sin(a) * off;
            Limb(w, R, sx, sy, sz, dx, (R.Next() - .5) * .3, dz, sl, .6, .5, bark, 0, .3);
        }
        // the boughs it kept, broken short: most stand up out of it, a few lie along the ground
        for (int k = 0, n = 3 + (int)(R.Next() * 4); k < n; k++)
        {
            double s = len * (.3 + R.Next() * .62), roll = (R.Next() - .5) * (R.Next() < .75 ? 2.2 : 4.2);
            var (bx, by, bz) = At(s);
            // up and to the side of the trunk, across it
            double ux = -dz * Math.Sin(roll), uy = Math.Cos(roll), uz = dx * Math.Sin(roll);
            double fx = ux + dx * .35, fy = uy, fz = uz + dz * .35, n2 = Math.Sqrt(fx * fx + fy * fy + fz * fz);
            double rr = Thick(s);
            Limb(w, R, bx + ux * rr * .7, by + uy * rr * .7, bz + uz * rr * .7, fx / n2, fy / n2, fz / n2, 4 + R.Next() * 7, Math.Min(1.3, rr * .45), .5, bark, -.01, .2);
        }
        if (!roots) return;
        // the root plate on end at its foot, across the way it fell: a disc of earth held in the roots, a little raised
        double pr = r * 2.3 + 2, px = ax - dx * 1.2, pz = az - dz * 1.2, py = yFoot + r * .5;
        int box = (int)Math.Ceiling(pr) + 3;
        for (int y = (int)(py - pr - 1); y <= py + pr + 1; y++)
            for (int z = (int)(pz - box); z <= pz + box; z++)
                for (int x = (int)(px - box); x <= px + box; x++)
                {
                    double ox = x + .5 - px, oy = y + .5 - py, oz = z + .5 - pz;
                    double along = ox * dx + oz * dz, across = -ox * dz + oz * dx;
                    if (along < -2.6 || along > .6) continue;
                    double rad = Math.Sqrt(across * across + oy * oy), edge = pr * (.86 + .28 * Hash.H3(x >> 1, y >> 1, z >> 1));
                    if (rad >= edge) continue;
                    // the face it shows the pit is earth and the cut ends of roots; its rim is roots
                    bool rim = rad > edge - 1.6;
                    byte m = rim || Hash.H3(x * 5, y * 3, z * 7) < .22 ? bark : Mat.Soil;
                    if (m == bark) Wood(w, x, y, z, m); else Put(w, x, y, z, m);
                }
        // and the longer roots reaching out of it, every way across the way it fell
        for (int k = 0, n = 6 + (int)(R.Next() * 4); k < n; k++)
        {
            double a = k * 6.28 / n + R.Next() * .5, cx = Math.Cos(a), cy = Math.Sin(a);
            double ox = -dz * cx, oz = dx * cx;
            Limb(w, R, px + ox * pr * .7, py + cy * pr * .7, pz + oz * pr * .7, ox - dx * .3, cy, oz - dz * .3, pr * (.25 + R.Next() * .35), .9, .5, bark, -.02, .3);
        }
    }

    /// <summary>
    /// The stump of a tree that broke off long ago, knee high or less: a short trunk swollen at the butt on its roots, its
    /// top splintered, the longest splinters on the side it broke away from. False when there is no room for it.
    /// </summary>
    public static bool Stump(VoxelWorld w, int x0, int z0, double r0, int h, byte bark)
    {
        double swell = .3, flare = .6 * r0;
        if (!Room(w, x0, z0, Roots(r0, swell, flare), r0, h + 6)) return false;
        Sprout();
        int b = w.SurfAt(x0, z0);
        double ax = x0 + .5, az = z0 + .5, snap = Hash.H3(x0, z0, 61) * 6.28;
        Stem(w, ax, az, b, b + h, r0, r0 * .92, 0, 0, swell, flare, (_, _, _) => bark);
        double rt = Girth(r0, r0 * .92, h, swell, h);
        for (int z = (int)Math.Floor(az - rt); z <= (int)Math.Floor(az + rt); z++)
            for (int x = (int)Math.Floor(ax - rt); x <= (int)Math.Floor(ax + rt); x++)
            {
                double dx = x + .5 - ax, dz = z + .5 - az;
                if (dx * dx + dz * dz >= rt * rt) continue;
                double side = (1 + Math.Cos(Math.Atan2(dz, dx) - snap)) / 2;
                int tall = (int)Math.Round(4 * Hash.H3(x, z, 62) * (.2 + .8 * side));
                for (int y = b + h; y < b + h + tall; y++) Wood(w, x, y, z, bark);
            }
        return true;
    }

    /// <summary>
    /// A hollow in an old trunk standing about (ax, az): an opening a little taller than wide at height y, facing the way
    /// a, dark inside. False when the trunk there is too thin to hold one.
    /// </summary>
    public static bool HollowIn(VoxelWorld w, double ax, double az, int y, double a)
    {
        double ux = Math.Cos(a), uz = Math.Sin(a), s = 0;
        while (s < 14 && Mat.IsTrunk(w.Get((int)Math.Floor(ax + ux * s), y, (int)Math.Floor(az + uz * s)))) s += .5;
        if (s < 2.5 || s >= 14) return false;
        // a pocket reaching in from the bark: an upright egg a voxel and a half to either side
        double cx = ax + ux * (s - 1.1), cz = az + uz * (s - 1.1), cy = y + 1.6;
        var carved = new List<(int x, int y, int z)>();
        for (int yy = y - 2; yy <= y + 5; yy++)
            for (int z = (int)(cz - 3); z <= cz + 3; z++)
                for (int x = (int)(cx - 3); x <= cx + 3; x++)
                {
                    double ox = x + .5 - cx, oy = yy + .5 - cy, oz = z + .5 - cz;
                    double along = ox * ux + oz * uz, across = -ox * uz + oz * ux;
                    if (across * across / 1.7 + oy * oy / 4.4 + along * along / 2.6 >= 1 || !Mat.IsTrunk(w.Get(x, yy, z))) continue;
                    w.SetRaw(x, yy, z, Mat.Air);
                    carved.Add((x, yy, z));
                }
        // the wood round the pocket, inside the bark, dark; the lip the bark has grown round it, grey and bare
        foreach (var (x, yy, z) in carved)
            foreach (var (nx, ny, nz) in new[] { (x + 1, yy, z), (x - 1, yy, z), (x, yy + 1, z), (x, yy - 1, z), (x, yy, z + 1), (x, yy, z - 1) })
            {
                double out1 = (nx + .5 - ax) * ux + (nz + .5 - az) * uz;
                if (!Mat.IsTrunk(w.Get(nx, ny, nz)) || w.Get(nx, ny, nz) == Mat.Hollow) continue;
                w.SetRaw(nx, ny, nz, out1 < s - 1.6 ? Mat.Hollow : Mat.Snag);
            }
        return carved.Count > 0;
    }

    /// <summary>A bird's nest of twigs on (x, y, z): a cup three voxels across, open in the middle.</summary>
    public static void NestAt(VoxelWorld w, int x, int y, int z)
    {
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                Put(w, x + dx, y, z + dz, Mat.Nest);
                if (dx != 0 || dz != 0) Put(w, x + dx, y + 1, z + dz, Mat.Nest);
            }
        // and a few twigs sticking out of its rim
        for (int k = 0; k < 4; k++)
        {
            int ox = k switch { 0 => 2, 1 => -2, _ => 0 }, oz = k switch { 2 => 2, 3 => -2, _ => 0 };
            if (Hash.H3(x + k, y, z) < .6) Put(w, x + ox, y + 1, z + oz, Mat.Nest);
        }
    }

    /// <summary>An anthill of needles and earth: a dome r across and h high, a little lopsided.</summary>
    public static void AnthillAt(VoxelWorld w, int x0, int z0, double r, int h)
    {
        int b = w.SurfAt(x0, z0);
        double lx = (Hash.H3(x0, z0, 71) - .5) * r * .3, lz = (Hash.H3(z0, x0, 72) - .5) * r * .3;
        for (int z = (int)(z0 - r - 1); z <= z0 + r + 1; z++)
            for (int x = (int)(x0 - r - 1); x <= x0 + r + 1; x++)
            {
                double ddx = (x + .5 - x0 - .5 - lx) / r, ddz = (z + .5 - z0 - .5 - lz) / r, d2 = ddx * ddx + ddz * ddz;
                if (d2 >= 1) continue;
                int top = b + (int)Math.Round(h * Math.Pow(1 - d2, .75) + (Hash.H3(x, z, 73) - .5) * 1.2);
                for (int y = w.SurfAt(x, z); y < top; y++) Put(w, x, y, z, Mat.Anthill);
            }
    }
}
