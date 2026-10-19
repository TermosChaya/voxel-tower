using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Trees, bushes and boulders: the wood and the stone the valley gives. The valley's trees are grown rather than stamped:
/// trunks swell at the butt and stand on buttress roots, thin as they rise, boughs crook and fork, leaves gather in clumps
/// that are lighter where the sun is on them. Each keeps clear of the others, so that it is felled by itself. (The
/// prototype's map keeps its small cypresses and dead trees as they were.)
/// </summary>
public static class Flora
{
    static bool Put(VoxelWorld w, int x, int y, int z, byte m)
    {
        if (!w.InBounds(x, y, z)) return false;
        byte cur = w.Get(x, y, z);
        if (cur != Mat.Air && cur is not (Mat.Tuft or Mat.Flower or Mat.Bloom)) return false;
        w.SetRaw(x, y, z, m);
        return true;
    }

    static void Trunk(VoxelWorld w, int x0, int z0, int y0, int y1, int thick, byte bark = Mat.Trunk)
    {
        for (int y = y0; y < y1; y++)
            for (int dz = 0; dz < thick; dz++)
                for (int dx = 0; dx < thick; dx++) Put(w, x0 + dx, y, z0 + dz, bark);
    }

    // ------------------------------------------------------------------ growing

    static double Sq(double v) => v * v;

    /// <summary>The wood laid so far of the tree being grown, so that its boughs know another tree's wood from its own.</summary>
    [ThreadStatic] static HashSet<long> _own;
    /// <summary>Which blocks of 4³ about the growing tree hold another tree's wood: most hold none, and none comes to.</summary>
    [ThreadStatic] static Dictionary<long, bool> _near;

    static long Key(int x, int y, int z) => ((long)x << 40) | ((long)y << 20) | (uint)z;

    /// <summary>A tree begins to grow: none of the wood about it is its own yet.</summary>
    static void Sprout()
    {
        (_own ??= new HashSet<long>()).Clear();
        (_near ??= new Dictionary<long, bool>()).Clear();
    }

    /// <summary>A voxel of the growing tree's wood.</summary>
    static void Wood(VoxelWorld w, int x, int y, int z, byte m)
    {
        if (Put(w, x, y, z, m)) _own.Add(Key(x, y, z));
    }

    static bool Theirs(VoxelWorld w, int x, int y, int z) => Mat.IsTrunk(w.Get(x, y, z)) && !_own.Contains(Key(x, y, z));

    /// <summary>Wood of another tree within r of a point: a bough grown there would make the two trees one.</summary>
    static bool Foreign(VoxelWorld w, double cx, double cy, double cz, double r)
    {
        int x0 = (int)Math.Floor(cx - r), x1 = (int)Math.Floor(cx + r), y0 = (int)Math.Floor(cy - r), y1 = (int)Math.Floor(cy + r);
        int z0 = (int)Math.Floor(cz - r), z1 = (int)Math.Floor(cz + r);
        for (int by = y0 >> 2; by <= y1 >> 2; by++)
            for (int bz = z0 >> 2; bz <= z1 >> 2; bz++)
                for (int bx = x0 >> 2; bx <= x1 >> 2; bx++)
                {
                    long k = Key(bx, by, bz);
                    if (!_near.TryGetValue(k, out bool any))
                    {
                        for (int i = 0; i < 64 && !any; i++) any = Theirs(w, (bx << 2) + (i & 3), (by << 2) + (i >> 4), (bz << 2) + (i >> 2 & 3));
                        _near[k] = any;
                    }
                    if (!any) continue;
                    for (int y = Math.Max(y0, by << 2); y <= Math.Min(y1, (by << 2) + 3); y++)
                        for (int z = Math.Max(z0, bz << 2); z <= Math.Min(z1, (bz << 2) + 3); z++)
                            for (int x = Math.Max(x0, bx << 2); x <= Math.Min(x1, (bx << 2) + 3); x++)
                                if (Sq(x + .5 - cx) + Sq(y + .5 - cy) + Sq(z + .5 - cz) <= r * r && Theirs(w, x, y, z)) return true;
                }
        return false;
    }

    /// <summary>A ball of the growing tree's wood; a single voxel when it is thinner than one.</summary>
    static void Ball(VoxelWorld w, double cx, double cy, double cz, double r, byte m)
    {
        if (r < .8) { Wood(w, (int)Math.Floor(cx), (int)Math.Floor(cy), (int)Math.Floor(cz), m); return; }
        double r2 = r * r;
        for (int y = (int)Math.Floor(cy - r); y <= (int)Math.Floor(cy + r); y++)
            for (int z = (int)Math.Floor(cz - r); z <= (int)Math.Floor(cz + r); z++)
                for (int x = (int)Math.Floor(cx - r); x <= (int)Math.Floor(cx + r); x++)
                    if (Sq(x + .5 - cx) + Sq(y + .5 - cy) + Sq(z + .5 - cz) <= r2) Wood(w, x, y, z, m);
    }

    /// <summary>How high the butt of a trunk swells (3 m) and how high its roots stand out of it (1 m), in voxels.</summary>
    const int Butt = 24, Foot = 8;

    /// <summary>
    /// How far from its axis the roots of a trunk <paramref name="r0"/> thick above its butt reach at the ground: the butt
    /// swollen by <paramref name="swell"/> and fluted, and the longest buttress <paramref name="flare"/> further out where
    /// it runs into the ground (see <see cref="Stem"/>).
    /// </summary>
    static double Roots(double r0, double swell, double flare) => r0 * (1 + swell) * 1.15 + flare * 1.4;

    /// <summary>
    /// Room for a tree <paramref name="h"/> tall whose roots reach so far and whose trunk is <paramref name="r0"/> thick: no
    /// other tree's trunk or roots stand within twice that of its foot, nor any bough of one where its own trunk will rise,
    /// so that each tree of the valley stands, is counted and comes down by itself.
    /// </summary>
    static bool Room(VoxelWorld w, int x0, int z0, double roots, double r0, int h)
    {
        int n = (int)Math.Ceiling(2 * roots);
        double clear = r0 * 1.2 + 2;
        for (int z = z0 - n; z <= z0 + n; z++)
            for (int x = x0 - n; x <= x0 + n; x++)
            {
                double d2 = Sq(x - x0) + Sq(z - z0);
                if (d2 > 4 * roots * roots || !w.InBounds(x, 0, z)) continue;
                int g = w.SurfAt(x, z), top = d2 <= clear * clear ? g + h : g + 2;
                for (int y = g - 1; y <= top; y++) if (Mat.IsTrunk(w.Get(x, y, z))) return false;
            }
        return true;
    }

    /// <summary>
    /// Where across its voxel a stem stands roundest as it thins from <paramref name="hi"/> to <paramref name="lo"/> along
    /// its bare length: on the voxel's middle, which gives it odd widths, or on its corner, even ones. Small rounds in
    /// voxels come out as crosses and squares; of the two this takes the one that fills its width most like a disc does.
    /// </summary>
    static double Seat(double lo, double hi)
    {
        double best = 0, worst = double.MaxValue;
        foreach (double o in new[] { 0, .5 })
        {
            // over the rounds the bare trunk passes through: a cross or a pair of voxels is the worst of all
            double off = 0;
            for (int k = 0; k <= 8; k++)
            {
                double r = lo + (hi - lo) * k / 8;
                int n = 0, wide = 1;
                for (int z = -6; z <= 6; z++)
                    for (int x = -6; x <= 6; x++)
                        if (Sq(x + o) + Sq(z + o) < r * r) { n++; wide = Math.Max(wide, (int)Math.Round(2 * Math.Abs(x + o) + 1)); }
                off += Math.Abs(n / (double)(wide * wide) - Math.PI / 4) + (n <= 5 ? 1 : 0);
            }
            if (off < worst - 1e-9) { worst = off; best = o; }
        }
        return best;
    }

    /// <summary>The radius of a trunk <paramref name="up"/> voxels above its foot, roots aside (see <see cref="Stem"/>).</summary>
    static double Girth(double r0, double r1, int height, double swell, int up)
    {
        up = Math.Max(0, up);
        double r = r1 + (r0 - r1) * (1 - Math.Pow(Math.Min(1, up / (double)height), 1.6));
        return up < Butt ? r * (1 + swell * Sq(1 - up / (double)Butt)) : r;
    }

    /// <summary>
    /// A trunk from below the ground (so it stands on a slope as well) up to <paramref name="top"/>: <paramref name="r0"/>
    /// thick above its butt, it thins slowly at first and faster up in the crown, to <paramref name="r1"/>. The butt swells
    /// by <paramref name="swell"/> over the lowest three metres, and in the lowest metre four to six buttress roots stand
    /// out of it, the longest <paramref name="flare"/> further, and run on along the ground into it. The axis drifts by
    /// (lx, lz) by the top, more the higher it goes.
    /// </summary>
    static void Stem(VoxelWorld w, double ax, double az, int b, int top, double r0, double r1, double lx, double lz, double swell, double flare,
        Func<int, int, int, byte> bark)
    {
        var R = new Mulberry32((uint)(ax * 97 + az * 61 + 29));
        int lobes = 4 + (int)(R.Next() * 3);
        double a0 = R.Next() * 6.28;
        var at = new double[lobes];
        var amp = new double[lobes];
        for (int j = 0; j < lobes; j++) { at[j] = a0 + (j + (R.Next() - .5) * .45) * 2 * Math.PI / lobes; amp[j] = .6 + R.Next() * .4; }
        // how far the roots stand out along a direction: a ridge for each, falling off to the hollows between them
        double Lobe(double a)
        {
            double most = 0;
            for (int j = 0; j < lobes; j++)
            {
                double d = Math.IEEERemainder(a - at[j], 2 * Math.PI) * lobes / 2;
                if (Math.Abs(d) < Math.PI / 2) most = Math.Max(most, amp[j] * Math.Pow(Math.Cos(d), 3));
            }
            return most;
        }
        // on a slope the roots go down to the ground on the lower side, a metre at most
        int low = b - 3, spread = (int)Math.Ceiling(Girth(r0, r1, top - b, swell, 0) + flare * 1.4);
        for (int z = (int)Math.Floor(az) - spread; z <= (int)Math.Floor(az) + spread; z++)
            for (int x = (int)Math.Floor(ax) - spread; x <= (int)Math.Floor(ax) + spread; x++)
                if (Sq(x + .5 - ax) + Sq(z + .5 - az) < spread * spread) low = Math.Min(low, w.SurfAt(x, z));
        for (int y = Math.Max(b - 8, low); y < top; y++)
        {
            int up = Math.Max(0, y - b);
            double t = Math.Clamp((y - b) / (double)(top - b), 0, 1);
            double cx = ax + lx * t * t, cz = az + lz * t * t, r = Girth(r0, r1, top - b, swell, up);
            // the roots: tall fins at the foot, longer where they meet the ground and go into it; above them their ridges
            // run on up the butt as flutes, fading out where it stops swelling
            double foot = up < Foot ? flare * Math.Pow(1 - up / (double)Foot, 1.6) * (y <= b ? 1.4 : 1) : 0, rr = r * 1.15 + foot;
            double flute = up < Butt ? .25 * Sq(1 - up / (double)Butt) : 0;
            for (int z = (int)Math.Floor(cz - rr); z <= (int)Math.Floor(cz + rr); z++)
                for (int x = (int)Math.Floor(cx - rr); x <= (int)Math.Floor(cx + rr); x++)
                {
                    double dx = x + .5 - cx, dz = z + .5 - cz, reach = r;
                    if (foot > 0 || flute > 0)
                    {
                        double lobe = Lobe(Math.Atan2(dz, dx));
                        reach = r * (1 + flute * (lobe - .4)) + foot * (.15 + .85 * lobe);
                    }
                    if (dx * dx + dz * dz < reach * reach) Wood(w, x, y, z, bark(x, y, z));
                }
            int ix = (int)Math.Floor(cx), iz = (int)Math.Floor(cz);
            if (r < .75) Wood(w, ix, y, iz, bark(ix, y, iz));
        }
    }

    /// <summary>
    /// A bough from a point: it runs <paramref name="len"/> voxels along (dx, dy, dz), thinning from r0 to r1, turning up by
    /// <paramref name="rise"/> each half voxel (down when negative: it droops) and crooking a little at random; it stops
    /// short of another tree's wood, so that the two are not felled as one. Returns where it ends; the points along it go
    /// into <paramref name="path"/> when one is given.
    /// </summary>
    static (double x, double y, double z) Limb(VoxelWorld w, Mulberry32 R, double x, double y, double z, double dx, double dy, double dz,
        double len, double r0, double r1, byte bark, double rise = 0, double crook = .08, List<(double x, double y, double z)> path = null)
    {
        for (double s = 0; s < len; s += .5)
        {
            double r = r0 + (r1 - r0) * (s / len);
            if (s > 0 && Foreign(w, x, y, z, r + 2)) break;
            Ball(w, x, y, z, r, bark);
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
    /// Grown ones are the valley's: a dark flame fluted down its length on a short trunk (the prototype keeps the small ones);
    /// false when there is no room for one (see <see cref="Room"/>).
    /// </summary>
    public static bool Cypress(VoxelWorld w, int x0, int z0, int h, int grain = 0, bool grown = false)
    {
        if (grown) return Flame(w, x0, z0, h);
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
        return true;
    }

    /// <summary>The valley's cypress: a column of needles swelling low and drawn to a point, fluted, its tip leaning off true.</summary>
    static bool Flame(VoxelWorld w, int x0, int z0, int h)
    {
        double r0 = 1.6 + h * .002, swell = .3, flare = .6 * r0;
        if (!Room(w, x0, z0, Roots(r0, swell, flare), r0, h + 8)) return false;
        Sprout();
        int b = w.SurfAt(x0, z0), top = b + h + 8, y0 = b + 5, stem = (int)(h * .6);
        var R = new Mulberry32((uint)(x0 * 29 + z0 * 47 + 13));
        double o = Seat(Girth(r0, .6, stem, swell, 10), Girth(r0, .6, stem, swell, 4));
        double ax = x0 + .5 + o, az = z0 + .5 + o, rmax = 4.8 + R.Next() * 1.8, ph = R.Next() * 6.28, ph2 = R.Next() * 6.28;
        double lean = R.Next() * 6.28, ll = 1 + R.Next() * 2;
        Stem(w, ax, az, b, b + stem, r0, .6, 0, 0, swell, flare, (_, _, _) => Mat.Trunk);
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
        return true;
    }

    /// <summary>
    /// A dead tree. The prototype's is a bare post with a few crooked sticks; grown ones are the valley's: a gaunt grey trunk
    /// leaning off true, its boughs crooked and forking, broken short. False when a grown one has no room (see
    /// <see cref="Room"/>).
    /// </summary>
    public static bool DeadTree(VoxelWorld w, int x0, int z0, bool grown = false, double scale = 1)
    {
        double r0 = 1.25 * scale, swell = .3, flare = .6 * r0;
        int bse = w.SurfAt(x0, z0), H = grown ? 26 + (int)Math.Floor(Hash.H3(x0, z0, 5) * 10) : 20 + (int)Math.Floor(Hash.H3(x0, z0, 5) * 8);
        H = (int)(H * scale);
        if (grown && !Room(w, x0, z0, Roots(r0, swell, flare), r0, H)) return false;
        if (grown) Sprout();
        var R = new Mulberry32((uint)(x0 * 31 + z0));
        if (grown)
        {
            double o = Seat(Girth(r0, .55, H, swell, (int)(H * .4)), Girth(r0, .55, H, swell, 10));
            double ax = x0 + .5 + o, az = z0 + .5 + o, lean = R.Next() * 6.28, ll = 1 + R.Next() * 3, lx = Math.Cos(lean) * ll, lz = Math.Sin(lean) * ll;
            Stem(w, ax, az, bse, bse + H, r0, .55, lx, lz, swell, flare, (_, _, _) => Mat.Snag);
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
            return true;
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
        return true;
    }

    /// <summary>
    /// A spruce: tiers of drooping boughs round a straight trunk, widest near the ground and narrowing to a spire. Each tier
    /// is a skirt, solid by the trunk and thin at its ragged rim, which hangs lower; its top and its tips are lighter. False
    /// when there is no room for it (see <see cref="Room"/>).
    /// </summary>
    public static bool Spruce(VoxelWorld w, int x0, int z0, int h, int thick = 2)
    {
        double r0 = 1.45 + thick * .25 + h * .002, swell = .35, flare = .6 * r0;
        if (!Room(w, x0, z0, Roots(r0, swell, flare), r0, h)) return false;
        Sprout();
        int b = w.SurfAt(x0, z0), top = b + h;
        var R = new Mulberry32((uint)(x0 * 37 + z0 * 53 + 11));
        double o = Seat(Girth(r0, .5, h - 3, swell, 12), Girth(r0, .5, h - 3, swell, 4)), ax = x0 + .5 + o, az = z0 + .5 + o;
        Stem(w, ax, az, b, top - 3, r0, .5, 0, 0, swell, flare, (_, _, _) => Mat.Trunk);
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
        return true;
    }

    /// <summary>
    /// A broad oak: a thick trunk flaring into roots that forks low into crooked boughs, and each of those forks again; the
    /// crown is big clumps of leaves at the ends of the limbs, smaller ones where they part, one over the leader. False when
    /// there is no room for it (see <see cref="Room"/>).
    /// </summary>
    public static bool Oak(VoxelWorld w, int x0, int z0, int h, int thick = 3)
    {
        // the boughs keep the measure of thick: three or four of them take up about as much wood as the trunk under them
        double limb = .6 + thick * .45, r0 = limb + .25 + h * .005, swell = .35, flare = .6 * r0;
        if (!Room(w, x0, z0, Roots(r0, swell, flare), r0, h)) return false;
        Sprout();
        int b = w.SurfAt(x0, z0);
        var R = new Mulberry32((uint)(x0 * 73 + z0 * 19 + 5));
        double lean = R.Next() * 6.28, ll = R.Next() * 2.5, lx = Math.Cos(lean) * ll, lz = Math.Sin(lean) * ll;
        int fork = b + (int)(h * (.3 + R.Next() * .1)), lead = b + (int)(h * .64);
        double o = Seat(Girth(r0, r0 * .4, lead - b, swell, fork - b), Girth(r0, r0 * .4, lead - b, swell, 10)), ax = x0 + .5 + o, az = z0 + .5 + o;
        Stem(w, ax, az, b, lead, r0, r0 * .4, lx, lz, swell, flare, (_, _, _) => Mat.Trunk);
        var tips = new List<(double x, double y, double z)>();
        var parts = new List<(double x, double y, double z)>();
        int boughs = 3 + (int)(R.Next() * 2);
        double a0 = R.Next() * 6.28;
        for (int k = 0; k < boughs; k++)
        {
            double a = a0 + k * 6.28 / boughs + (R.Next() - .5) * .8, up = .5 + R.Next() * .55, len = h * (.15 + R.Next() * .05);
            double y = fork - 3 + R.Next() * 6, n = Math.Sqrt(1 + up * up), t = Math.Clamp((y - b) / (lead - b), 0, 1);
            var p = Limb(w, R, ax + lx * t * t, y, az + lz * t * t, Math.Cos(a) / n, up / n, Math.Sin(a) / n, len, limb * .62, limb * .4, Mat.Trunk, -.004, .14);
            parts.Add(p);
            for (int s = -1; s <= 1; s += 2)
            {
                double sa = a + s * (.3 + R.Next() * .35), sup = up * (.55 + R.Next() * .5), sn = Math.Sqrt(1 + sup * sup);
                tips.Add(Limb(w, R, p.x, p.y, p.z, Math.Cos(sa) / sn, sup / sn, Math.Sin(sa) / sn, h * (.13 + R.Next() * .06), limb * .38, .5, Mat.Trunk, -.006, .16));
            }
        }
        foreach (var (x, y, z) in tips) { double rx = h * (.12 + R.Next() * .04); Clump(w, x, y + 1, z, rx, rx * .72, Mat.Foliage, Mat.FoliageLight, .7); }
        foreach (var (x, y, z) in parts) { double rx = h * (.1 + R.Next() * .03); Clump(w, x, y + 2, z, rx, rx * .7, Mat.Foliage, Mat.FoliageLight, .7); }
        double cr = h * (.15 + R.Next() * .04);
        Clump(w, ax + lx, lead + cr * .2, az + lz, cr, cr * .75, Mat.Foliage, Mat.FoliageLight, .7);
        return true;
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
    /// over at their ends, light leaves in small clumps along them. One in five is turning yellow. False when there is no
    /// room for it (see <see cref="Room"/>).
    /// </summary>
    public static bool Birch(VoxelWorld w, int x0, int z0, int h)
    {
        double r0 = 1.6 + h * .001, swell = .3, flare = .6 * r0;
        if (!Room(w, x0, z0, Roots(r0, swell, flare), r0, h)) return false;
        Sprout();
        int b = w.SurfAt(x0, z0), stemTop = b + h - 5;
        var R = new Mulberry32((uint)(x0 * 41 + z0 * 23 + 9));
        double o = Seat(Girth(r0, .5, h - 5, swell, (int)(h * .3)), Girth(r0, .5, h - 5, swell, 10)), ax = x0 + .5 + o, az = z0 + .5 + o;
        double lean = R.Next() * 6.28, ll = 1.5 + R.Next() * 2.5, lx = Math.Cos(lean) * ll, lz = Math.Sin(lean) * ll;
        Stem(w, ax, az, b, stemTop, r0, .5, lx, lz, swell, flare, (x, y, z) => BirchBark(x, y, z, b, h));
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
        return true;
    }

    /// <summary>
    /// A pine: a tall trunk, a little crooked, grey below and rusty where the crown begins, a few dead stubs on its bare
    /// length. The crown is boughs reaching out and curving up, each with a flat pad of needles at its end and another
    /// partway, the low ones longest; a dome over the top. False when there is no room for it (see <see cref="Room"/>).
    /// </summary>
    public static bool Pine(VoxelWorld w, int x0, int z0, int h)
    {
        double r0 = 1.7 + h * .0055, swell = .35, flare = .6 * r0;
        if (!Room(w, x0, z0, Roots(r0, swell, flare), r0, h)) return false;
        Sprout();
        int b = w.SurfAt(x0, z0), stemTop = b + h - 3;
        var R = new Mulberry32((uint)(x0 * 59 + z0 * 31 + 3));
        double lean = R.Next() * 6.28, ll = 1 + R.Next() * 3, lx = Math.Cos(lean) * ll, lz = Math.Sin(lean) * ll;
        int crown = b + (int)(h * (.4 + R.Next() * .12)), rust = crown - (int)(h * .14);
        double Thick(double y) => Girth(r0, .55, stemTop - b, swell, (int)(y - b));
        double o = Seat(Thick(crown), Thick(b + 10)), ax = x0 + .5 + o, az = z0 + .5 + o;
        Stem(w, ax, az, b, stemTop, r0, .55, lx, lz, swell, flare, (x, y, z) => y > rust + (int)(Hash.H3(x, y >> 2, z) * 8) - 4 ? Mat.PineBark : Mat.Trunk);
        (double x, double z) Axis(double y) { double t = Math.Clamp((y - b) / (stemTop - b), 0, 1); return (ax + lx * t * t, az + lz * t * t); }
        // stubs of the dead lower boughs on the bare length
        for (int k = 0, stubs = 2 + (int)(R.Next() * 3); k < stubs; k++)
        {
            double y = b + h * .2 + R.Next() * Math.Max(2, crown - 6 - b - h * .2), a = R.Next() * 6.28;
            var (sx, sz) = Axis(y);
            Limb(w, R, sx, y, sz, Math.Cos(a), -.2, Math.Sin(a), Thick(y) + 1.5 + R.Next() * 2.5, .5, .5, Mat.Trunk);
        }
        // the boughs, round the trunk by the golden angle, reaching well out before they turn up
        int boughs = 11 + (int)(R.Next() * 5);
        double a0 = R.Next() * 6.28, span = stemTop - crown;
        var path = new List<(double x, double y, double z)>();
        for (int k = 0; k < boughs; k++)
        {
            double u = (k + R.Next() * .8) / boughs, y = crown + span * u * .88, a = a0 + k * 2.4 + R.Next() * .5;
            var (sx, sz) = Axis(y);
            double reach = Thick(y) + h * .23 * (1 - u * u * .7) * (.7 + R.Next() * .5), up = .05 + R.Next() * .25 + u * .2, n = Math.Sqrt(1 + up * up);
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
        return true;
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
