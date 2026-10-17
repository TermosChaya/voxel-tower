using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Trees, bushes and boulders: the wood and the stone the valley gives. The valley's trees are grown rather than stamped:
/// trunks thin as they rise and flare into roots at the foot, boughs crook and fork, leaves gather in clumps that are
/// lighter where the sun is on them. (The prototype's map keeps its small cypresses and dead trees as they were.)
/// </summary>
public static class Flora
{
    static void Put(VoxelWorld w, int x, int y, int z, byte m)
    {
        if (!w.InBounds(x, y, z)) return;
        byte cur = w.Get(x, y, z);
        if (cur == Mat.Air || cur is Mat.Tuft or Mat.Flower or Mat.Bloom) w.SetRaw(x, y, z, m);
    }

    static void Trunk(VoxelWorld w, int x0, int z0, int y0, int y1, int thick, byte bark = Mat.Trunk)
    {
        for (int y = y0; y < y1; y++)
            for (int dz = 0; dz < thick; dz++)
                for (int dx = 0; dx < thick; dx++) Put(w, x0 + dx, y, z0 + dz, bark);
    }

    // ------------------------------------------------------------------ growing

    static double Sq(double v) => v * v;

    /// <summary>A ball of one material; a single voxel when it is thinner than one.</summary>
    static void Ball(VoxelWorld w, double cx, double cy, double cz, double r, byte m)
    {
        if (r < .8) { Put(w, (int)Math.Floor(cx), (int)Math.Floor(cy), (int)Math.Floor(cz), m); return; }
        double r2 = r * r;
        for (int y = (int)Math.Floor(cy - r); y <= (int)Math.Floor(cy + r); y++)
            for (int z = (int)Math.Floor(cz - r); z <= (int)Math.Floor(cz + r); z++)
                for (int x = (int)Math.Floor(cx - r); x <= (int)Math.Floor(cx + r); x++)
                    if (Sq(x + .5 - cx) + Sq(y + .5 - cy) + Sq(z + .5 - cz) <= r2) Put(w, x, y, z, m);
    }

    /// <summary>
    /// A trunk from below the ground (so it stands on a slope as well) up to <paramref name="top"/>, thinning from r0 to r1.
    /// Its axis drifts by (lx, lz) by the top, more the higher it goes, and the foot flares into a few roots.
    /// </summary>
    static void Stem(VoxelWorld w, double ax, double az, int b, int top, double r0, double r1, double lx, double lz, double flare,
        Func<int, int, int, byte> bark)
    {
        double phase = ax * 1.7 + az;
        for (int y = b - 3; y < top; y++)
        {
            double t = Math.Clamp((y - b) / (double)(top - b), 0, 1);
            double cx = ax + lx * t * t, cz = az + lz * t * t, r = r0 + (r1 - r0) * t;
            double foot = y < b + 6 ? flare * Sq(1 - Math.Max(0, y - b) / 6.0) : 0, rr = r + foot;
            for (int z = (int)Math.Floor(cz - rr); z <= (int)Math.Floor(cz + rr); z++)
                for (int x = (int)Math.Floor(cx - rr); x <= (int)Math.Floor(cx + rr); x++)
                {
                    double dx = x + .5 - cx, dz = z + .5 - cz, reach = r;
                    if (foot > 0) reach += foot * (.35 + .65 * Math.Max(0, Math.Cos(5 * Math.Atan2(dz, dx) + phase)));
                    if (dx * dx + dz * dz < reach * reach) Put(w, x, y, z, bark(x, y, z));
                }
            int ix = (int)Math.Floor(cx), iz = (int)Math.Floor(cz);
            if (r < .75) Put(w, ix, y, iz, bark(ix, y, iz));
        }
    }

    /// <summary>
    /// A bough from a point: it runs <paramref name="len"/> voxels along (dx, dy, dz), thinning from r0 to r1, turning up by
    /// <paramref name="rise"/> each half voxel (down when negative: it droops) and crooking a little at random.
    /// Returns where it ends; the points along it go into <paramref name="path"/> when one is given.
    /// </summary>
    static (double x, double y, double z) Limb(VoxelWorld w, Mulberry32 R, double x, double y, double z, double dx, double dy, double dz,
        double len, double r0, double r1, byte bark, double rise = 0, double crook = .08, List<(double x, double y, double z)> path = null)
    {
        for (double s = 0; s < len; s += .5)
        {
            Ball(w, x, y, z, r0 + (r1 - r0) * (s / len), bark);
            path?.Add((x, y, z));
            dx += (R.Next() - .5) * crook; dz += (R.Next() - .5) * crook; dy += rise + (R.Next() - .5) * crook * .5;
            double n = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            dx /= n; dy /= n; dz /= n;
            x += dx * .5; y += dy * .5; z += dz * .5;
        }
        return (x, y, z);
    }

    /// <summary>
    /// A clump of leaves: an ellipsoid rx across and ry high with a ragged rim (the noise in 2³ grains keeps the faces few),
    /// its upper part mostly in the sunlit shade; <paramref name="sun"/> gives more of it to the light, or less.
    /// </summary>
    static void Clump(VoxelWorld w, double cx, double cy, double cz, double rx, double ry, byte leaf, byte lit, double rag = .6, double sun = 0)
    {
        for (int y = (int)Math.Floor(cy - ry - 1); y <= (int)Math.Floor(cy + ry + 1); y++)
            for (int z = (int)Math.Floor(cz - rx - 1); z <= (int)Math.Floor(cz + rx + 1); z++)
                for (int x = (int)Math.Floor(cx - rx - 1); x <= (int)Math.Floor(cx + rx + 1); x++)
                {
                    double ey = (y + .5 - cy) / ry;
                    double e = Sq((x + .5 - cx) / rx) + ey * ey + Sq((z + .5 - cz) / rx) + (Hash.H3(x >> 1, y >> 1, z >> 1) - .5) * rag;
                    if (e >= 1) continue;
                    bool sunlit = ey > -.15 && Hash.H3((x >> 1) + 9, y >> 1, (z >> 1) + 5) < .3 + ey * .6 + sun;
                    Put(w, x, y, z, sunlit ? lit : leaf);
                }
    }

    // ------------------------------------------------------------------ the trees

    /// <summary>
    /// A cypress, the prototype's tree: tall and narrow. Grain 1 clumps the needles in 2³ tufts (fewer faces).
    /// Grown ones are the valley's: a dark flame fluted down its length on a short trunk (the prototype keeps the small ones).
    /// </summary>
    public static void Cypress(VoxelWorld w, int x0, int z0, int h, int grain = 0, bool grown = false)
    {
        if (grown) { Flame(w, x0, z0, h); return; }
        int bse = w.SurfAt(x0, z0), trunk = 9;
        Trunk(w, x0, z0, bse, bse + trunk, 2);
        double cy = bse + 7 + h / 2.0, ry = h / 2.0, rx = 4.4 + Hash.H3(x0, z0, 3) * 1.2;
        for (int y = bse + 5; y < bse + 8 + h; y++)
        {
            double t = (y - cy) / ry;
            if (Math.Abs(t) > 1) continue;
            double r = rx * Math.Sqrt(1 - t * t);
            if (t > 0) r *= 1 - t * .5;
            for (int z = (int)Math.Floor(z0 - r - 1); z <= z0 + r + 2; z++)
                for (int x = (int)Math.Floor(x0 - r - 1); x <= x0 + r + 2; x++)
                {
                    double d = Brushes.Dist(x - x0 - .5, z - z0 - .5) + (Hash.H3(x >> grain, y >> grain, z >> grain) - .5) * 1.7;
                    if (d < r) Put(w, x, y, z, Mat.Leaf);
                }
        }
    }

    /// <summary>The valley's cypress: a column of needles swelling low and drawn to a point, fluted, its tip leaning off true.</summary>
    static void Flame(VoxelWorld w, int x0, int z0, int h)
    {
        int b = w.SurfAt(x0, z0), top = b + h + 8, y0 = b + 5;
        var R = new Mulberry32((uint)(x0 * 29 + z0 * 47 + 13));
        double ax = x0 + .5, az = z0 + .5, rmax = 4.8 + R.Next() * 1.8, ph = R.Next() * 6.28, ph2 = R.Next() * 6.28;
        double lean = R.Next() * 6.28, ll = 1 + R.Next() * 2;
        Stem(w, ax, az, b, b + (int)(h * .6), 1.3, .6, 0, 0, .8, (_, _, _) => Mat.Trunk);
        for (int y = y0; y < top; y++)
        {
            double t = (y - y0) / (double)(top - y0);
            double r = rmax * (t < .3 ? .7 + t : Math.Pow((1 - t) / .7, .8)) + .5;
            double cx = ax + Math.Cos(lean) * ll * t * t * t, cz = az + Math.Sin(lean) * ll * t * t * t;
            int n = (int)Math.Ceiling(r * 1.25) + 1;
            for (int z = (int)cz - n; z <= (int)cz + n; z++)
                for (int x = (int)cx - n; x <= (int)cx + n; x++)
                {
                    double dx = x + .5 - cx, dz = z + .5 - cz, a = Math.Atan2(dz, dx);
                    double lim = r * (1 + .14 * Math.Sin(5 * a + ph + y * .07) + .07 * Math.Sin(9 * a + ph2)) + (Hash.H3(x >> 1, y >> 1, z >> 1) - .5) * 1.5;
                    if (dx * dx + dz * dz >= lim * lim) continue;
                    bool lit = Hash.H3((x >> 1) + 3, y >> 1, (z >> 1) + 17) < .22 + t * .3;
                    Put(w, x, y, z, lit ? Mat.NeedleLight : Mat.Leaf);
                }
        }
    }

    /// <summary>
    /// A dead tree. The prototype's is a bare post with a few crooked sticks; grown ones are the valley's: a gaunt grey trunk
    /// leaning off true, its boughs crooked and forking, broken short.
    /// </summary>
    public static void DeadTree(VoxelWorld w, int x0, int z0, bool grown = false, double scale = 1)
    {
        int bse = w.SurfAt(x0, z0), H = grown ? 26 + (int)Math.Floor(Hash.H3(x0, z0, 5) * 10) : 20 + (int)Math.Floor(Hash.H3(x0, z0, 5) * 8);
        H = (int)(H * scale);
        var R = new Mulberry32((uint)(x0 * 31 + z0));
        if (grown)
        {
            double ax = x0 + .5, az = z0 + .5, lean = R.Next() * 6.28, ll = 1 + R.Next() * 3, lx = Math.Cos(lean) * ll, lz = Math.Sin(lean) * ll;
            Stem(w, ax, az, bse, bse + H, 1.1 * scale, .55, lx, lz, 1.3 * scale, (_, _, _) => Mat.Snag);
            for (int k = 0; k < 5; k++)
            {
                double y = bse + H * (.4 + R.Next() * .5), t = (y - bse) / H, a = R.Next() * 6.28, up = .2 + R.Next() * .9, n = Math.Sqrt(1 + up * up);
                double len = (5 + R.Next() * 7) * scale;
                var e = Limb(w, R, ax + lx * t * t, y, az + lz * t * t, Math.Cos(a) / n, up / n, Math.Sin(a) / n, len, .95, .5, Mat.Snag, 0, .22);
                if (R.Next() < .6)
                {
                    double sa = a + (R.Next() - .5) * 1.6;
                    Limb(w, R, e.x, e.y, e.z, Math.Cos(sa) * .7, .7, Math.Sin(sa) * .7, len * .45, .5, .5, Mat.Snag, 0, .25);
                }
            }
            return;
        }
        Trunk(w, x0, z0, bse, bse + H, scale > 1.2 ? 3 : 2);
        for (int b = 0; b < 4; b++)
        {
            double x = x0 + .5, z = z0 + .5, y = bse + H * (.45 + R.Next() * .45);
            double a = R.Next() * 6.28, len = (6 + R.Next() * 6) * scale;
            for (int s = 0; s < len; s++)
            {
                x += Math.Cos(a) * .9; z += Math.Sin(a) * .9; y += .45;
                Put(w, Brushes.JsRound(x), Brushes.JsRound(y), Brushes.JsRound(z), Mat.Trunk);
            }
        }
    }

    /// <summary>
    /// A spruce: tiers of drooping boughs round a straight trunk, widest near the ground and narrowing to a spire. Each tier
    /// is a skirt, solid by the trunk and thin at its ragged rim, which hangs lower; its top and its tips are lighter.
    /// </summary>
    public static void Spruce(VoxelWorld w, int x0, int z0, int h, int thick = 2)
    {
        int b = w.SurfAt(x0, z0), top = b + h;
        var R = new Mulberry32((uint)(x0 * 37 + z0 * 53 + 11));
        double ax = x0 + .5, az = z0 + .5;
        Stem(w, ax, az, b, top - 3, .9 + thick * .45, .5, 0, 0, 1, (_, _, _) => Mat.Trunk);
        double maxR = 3 + h * .17, sp = 4.6 + R.Next() * 1.6, droop = .3 + R.Next() * .12;
        int start = b + (int)(h * (.07 + R.Next() * .08));
        int tiers = Math.Max(3, (int)((top - 6 - start) / sp));
        for (int k = 0; k < tiers; k++)
        {
            double yk = start + k * sp, t = (yk - start) / (top - start);
            double reach = maxR * Math.Pow(1 - t, 1.05) * (.9 + R.Next() * .2) + 1;
            double ph1 = R.Next() * 6.28, ph2 = R.Next() * 6.28, th0 = sp * 1.15 + 1, th1 = 2;
            int n = (int)Math.Ceiling(reach * 1.15) + 1;
            for (int z = (int)az - n; z <= (int)az + n; z++)
                for (int x = (int)ax - n; x <= (int)ax + n; x++)
                {
                    double dx = x + .5 - ax, dz = z + .5 - az, rho = Math.Sqrt(dx * dx + dz * dz), a = Math.Atan2(dz, dx);
                    double lim = reach * (.84 + .16 * Math.Sin(3 * a + ph1) + .1 * Math.Sin(7 * a + ph2)) + (Hash.H3(x >> 1, k, z >> 1) - .5) * 2.2;
                    if (rho >= lim) continue;
                    double f = rho / Math.Max(1, lim), low = yk - droop * rho, th = th0 + (th1 - th0) * f;
                    for (int y = (int)Math.Floor(low); y < low + th; y++)
                    {
                        bool lit = (y + 2 >= low + th || f > .8) && Hash.H3(x >> 1, y >> 1, (z >> 1) + 31) < .55;
                        Put(w, x, y, z, lit ? Mat.NeedleLight : Mat.Leaf);
                    }
                }
        }
        // the spire: the leader above the last whorl
        double s0 = start + tiers * sp - 2;
        for (int y = (int)s0; y < top; y++)
        {
            double r = 1.7 * (1 - (y - s0) / (top - s0)) + .45;
            for (int z = (int)az - 2; z <= (int)az + 2; z++)
                for (int x = (int)ax - 2; x <= (int)ax + 2; x++)
                    if (Sq(x + .5 - ax) + Sq(z + .5 - az) < r * r) Put(w, x, y, z, (y & 2) == 0 ? Mat.NeedleLight : Mat.Leaf);
            Put(w, (int)ax, y, (int)az, Mat.Leaf);
        }
    }

    /// <summary>
    /// A broad oak: a thick trunk flaring into roots that forks low into crooked boughs, and each of those forks again; the
    /// crown is big clumps of leaves at the ends of the limbs, smaller ones where they part, one over the leader.
    /// </summary>
    public static void Oak(VoxelWorld w, int x0, int z0, int h, int thick = 3)
    {
        int b = w.SurfAt(x0, z0);
        var R = new Mulberry32((uint)(x0 * 73 + z0 * 19 + 5));
        double ax = x0 + .5, az = z0 + .5, r0 = .6 + thick * .45;
        double lean = R.Next() * 6.28, ll = R.Next() * 2.5, lx = Math.Cos(lean) * ll, lz = Math.Sin(lean) * ll;
        int fork = b + (int)(h * (.3 + R.Next() * .1)), lead = b + (int)(h * .64);
        Stem(w, ax, az, b, lead, r0, r0 * .4, lx, lz, 2.4, (_, _, _) => Mat.Trunk);
        var tips = new List<(double x, double y, double z)>();
        var parts = new List<(double x, double y, double z)>();
        int boughs = 3 + (int)(R.Next() * 2);
        double a0 = R.Next() * 6.28;
        for (int k = 0; k < boughs; k++)
        {
            double a = a0 + k * 6.28 / boughs + (R.Next() - .5) * .8, up = .5 + R.Next() * .55, len = h * (.15 + R.Next() * .05);
            double y = fork - 3 + R.Next() * 6, n = Math.Sqrt(1 + up * up), t = Math.Clamp((y - b) / (lead - b), 0, 1);
            var p = Limb(w, R, ax + lx * t * t, y, az + lz * t * t, Math.Cos(a) / n, up / n, Math.Sin(a) / n, len, r0 * .62, r0 * .4, Mat.Trunk, -.004, .14);
            parts.Add(p);
            for (int s = -1; s <= 1; s += 2)
            {
                double sa = a + s * (.3 + R.Next() * .35), sup = up * (.55 + R.Next() * .5), sn = Math.Sqrt(1 + sup * sup);
                tips.Add(Limb(w, R, p.x, p.y, p.z, Math.Cos(sa) / sn, sup / sn, Math.Sin(sa) / sn, h * (.13 + R.Next() * .06), r0 * .38, .5, Mat.Trunk, -.006, .16));
            }
        }
        foreach (var (x, y, z) in tips) { double rx = h * (.12 + R.Next() * .04); Clump(w, x, y + 1, z, rx, rx * .72, Mat.Foliage, Mat.FoliageLight, .7); }
        foreach (var (x, y, z) in parts) { double rx = h * (.1 + R.Next() * .03); Clump(w, x, y + 2, z, rx, rx * .7, Mat.Foliage, Mat.FoliageLight, .7); }
        double cr = h * (.15 + R.Next() * .04);
        Clump(w, ax + lx, lead + cr * .2, az + lz, cr, cr * .75, Mat.Foliage, Mat.FoliageLight, .7);
    }

    /// <summary>Birch bark: white with black dashes across it, mostly black and rough at the foot.</summary>
    static byte BirchBark(int x, int y, int z, int b, int h)
    {
        double foot = 1 - (y - b) / (h * .14);
        if (foot > 0 && Hash.H3(x, y >> 1, z) < .3 + foot * .55) return Mat.Trunk;
        return Hash.H3(x >> 1, y, z >> 1) < .15 ? Mat.Trunk : Mat.Birch;
    }

    /// <summary>
    /// A birch: a slender white trunk marked with black, bending a little as it climbs; thin boughs rise steeply and hang
    /// over at their ends, light leaves in small clumps along them. One in five is turning yellow.
    /// </summary>
    public static void Birch(VoxelWorld w, int x0, int z0, int h)
    {
        int b = w.SurfAt(x0, z0), stemTop = b + h - 5;
        var R = new Mulberry32((uint)(x0 * 41 + z0 * 23 + 9));
        double ax = x0 + .5, az = z0 + .5, r0 = 1.05 + h * .004;
        double lean = R.Next() * 6.28, ll = 1.5 + R.Next() * 2.5, lx = Math.Cos(lean) * ll, lz = Math.Sin(lean) * ll;
        Stem(w, ax, az, b, stemTop, r0, .5, lx, lz, .9, (x, y, z) => BirchBark(x, y, z, b, h));
        bool turning = R.Next() < .2;
        int boughs = 13 + (int)(R.Next() * 6);
        double a0 = R.Next() * 6.28;
        var path = new List<(double x, double y, double z)>();
        for (int k = 0; k < boughs; k++)
        {
            double u = (k + R.Next()) / boughs, y = b + h * (.3 + u * .55), a = a0 + k * 2.4 + R.Next() * .5;
            double t = Math.Clamp((y - b) / (stemTop - b), 0, 1);
            double up = 1.3 - u * .4 + R.Next() * .5, len = h * (.15 + R.Next() * .08) * (1 - u * .45), n = Math.Sqrt(1 + up * up);
            path.Clear();
            Limb(w, R, ax + lx * t * t, y, az + lz * t * t, Math.Cos(a) / n, up / n, Math.Sin(a) / n, len, .75, .5, Mat.Birch, -.05, .1, path);
            // the twigs hang from it in small loose clumps, longer than they are wide
            for (int c = 0; c < 4; c++)
            {
                var (cx, cy, cz) = path[Math.Min(path.Count - 1, (int)(path.Count * (.3 + c * .23)))];
                double rx = 1.9 + R.Next() * 1.3;
                Clump(w, cx, cy - 1.2, cz, rx, rx * 1.35, Mat.Foliage, turning && R.Next() < .55 ? Mat.FoliageGold : Mat.FoliageLight, .9, .25);
            }
        }
        Clump(w, ax + lx, stemTop + 1, az + lz, 3.2, 4.4, Mat.Foliage, turning ? Mat.FoliageGold : Mat.FoliageLight, .9, .25);
    }

    /// <summary>
    /// A pine: a tall trunk, a little crooked, grey below and rusty where the crown begins, a few dead stubs on its bare
    /// length. The crown is boughs reaching out and curving up, each with a flat pad of needles at its end and another
    /// partway, the low ones longest; a dome over the top.
    /// </summary>
    public static void Pine(VoxelWorld w, int x0, int z0, int h)
    {
        int b = w.SurfAt(x0, z0), stemTop = b + h - 3;
        var R = new Mulberry32((uint)(x0 * 59 + z0 * 31 + 3));
        double ax = x0 + .5, az = z0 + .5, r0 = 1.5 + h * .005;
        double lean = R.Next() * 6.28, ll = 1 + R.Next() * 3, lx = Math.Cos(lean) * ll, lz = Math.Sin(lean) * ll;
        int crown = b + (int)(h * (.4 + R.Next() * .12)), rust = crown - (int)(h * .14);
        Stem(w, ax, az, b, stemTop, r0, .55, lx, lz, 1.3, (x, y, z) => y > rust + (int)(Hash.H3(x, y >> 2, z) * 8) - 4 ? Mat.PineBark : Mat.Trunk);
        (double x, double z) Axis(double y) { double t = Math.Clamp((y - b) / (stemTop - b), 0, 1); return (ax + lx * t * t, az + lz * t * t); }
        // stubs of the dead lower boughs on the bare length
        for (int k = 0, stubs = 2 + (int)(R.Next() * 3); k < stubs; k++)
        {
            double y = b + h * .2 + R.Next() * Math.Max(2, crown - 6 - b - h * .2), a = R.Next() * 6.28;
            var (sx, sz) = Axis(y);
            Limb(w, R, sx, y, sz, Math.Cos(a), -.2, Math.Sin(a), r0 + 1.5 + R.Next() * 2.5, .5, .5, Mat.Trunk);
        }
        // the boughs, round the trunk by the golden angle, reaching well out before they turn up
        int boughs = 11 + (int)(R.Next() * 5);
        double a0 = R.Next() * 6.28, span = stemTop - crown;
        var path = new List<(double x, double y, double z)>();
        for (int k = 0; k < boughs; k++)
        {
            double u = (k + R.Next() * .8) / boughs, y = crown + span * u * .88, a = a0 + k * 2.4 + R.Next() * .5;
            var (sx, sz) = Axis(y);
            double reach = r0 + h * .23 * (1 - u * u * .7) * (.7 + R.Next() * .5), up = .05 + R.Next() * .25 + u * .2, n = Math.Sqrt(1 + up * up);
            path.Clear();
            Limb(w, R, sx, y, sz, Math.Cos(a) / n, up / n, Math.Sin(a) / n, reach, .95, .5, Mat.PineBark, .005, .08, path);
            // needles in flat pads along its outer part, the biggest at the end
            double rx = 4.4 + R.Next() * 2.6 + reach * .12, ry = 2.3 + R.Next() * 1.2;
            for (int c = 0, pads = reach > 14 ? 3 : reach > 8 ? 2 : 1; c < pads; c++)
            {
                var (cx, cy, cz) = path[Math.Max(0, path.Count - 1 - c * path.Count / 3)];
                double f = 1 - c * .22;
                Clump(w, cx, cy + .9, cz, rx * f, ry * (.8 + .2 * f), Mat.Leaf, Mat.NeedleLight, .6, .08);
            }
        }
        var (tx, tz) = Axis(stemTop);
        Clump(w, tx, stemTop - 1, tz, 5 + R.Next() * 2, 2.8 + R.Next() * .8, Mat.Leaf, Mat.NeedleLight, .6, .08);
    }

    /// <summary>A low bush of a few lumpy clumps, half sunk into the ground.</summary>
    public static void Bush(VoxelWorld w, int x0, int z0, int r)
    {
        int b = w.SurfAt(x0, z0);
        var R = new Mulberry32((uint)(x0 * 17 + z0 * 71 + 1));
        for (int k = 0, n = 2 + (int)(R.Next() * 3); k < n; k++)
        {
            double a = R.Next() * 6.28, off = k == 0 ? 0 : r * (.4 + R.Next() * .5), rr = r * (k == 0 ? 1 : .6 + R.Next() * .3);
            Clump(w, x0 + .5 + Math.Cos(a) * off, b + rr * .45, z0 + .5 + Math.Sin(a) * off, rr, rr * .75, Mat.Foliage, Mat.FoliageLight, .8);
        }
    }

    /// <summary>A boulder half sunk into the ground, moss on its crown.</summary>
    public static void Boulder(VoxelWorld w, int cx, int cz, int r, int bse)
    {
        for (int y = bse - 4; y < bse + r; y++)
            for (int z = cz - r - 1; z <= cz + r + 1; z++)
                for (int x = cx - r - 1; x <= cx + r + 1; x++)
                {
                    if (!w.InBounds(x, y, z)) continue;
                    double d = Brushes.Dist(x - cx, (y - bse + 1) * 1.25, z - cz) + (Hash.H3(x, y, z) - .5) * 2.4;
                    if (d < r) w.SetRaw(x, y, z, Mat.Rock);
                }
        for (int z = cz - r - 1; z <= cz + r + 1; z++)
            for (int x = cx - r - 1; x <= cx + r + 1; x++)
            {
                if (x < 0 || x >= w.SX || z < 0 || z >= w.SZ) continue;
                for (int y = Math.Min(w.SY - 1, bse + r); y >= bse; y--)
                {
                    byte m = w.Get(x, y, z);
                    if (m == Mat.Rock) { if (Hash.H3(x, y, z) < .7) w.SetRaw(x, y, z, Mat.Moss); break; }
                    if (m != 0) break;
                }
            }
    }
}
