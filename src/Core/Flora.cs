using System;

namespace VoxelTower.Core;

/// <summary>Trees, bushes and boulders: the wood and the stone the valley gives.</summary>
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

    /// <summary>
    /// A tall narrow cypress, the prototype's tree. Grain 1 clumps the needles in 2³ tufts (fewer faces);
    /// grown ones stand on a longer trunk with a broader crown (the valley's; the prototype's map keeps the small ones).
    /// </summary>
    public static void Cypress(VoxelWorld w, int x0, int z0, int h, int grain = 0, bool grown = false)
    {
        int bse = w.SurfAt(x0, z0), trunk = grown ? 12 : 9, lift = grown ? 2 : 0;
        Trunk(w, x0, z0, bse, bse + trunk, 2);
        double cy = bse + 7 + lift + h / 2.0, ry = h / 2.0, rx = (grown ? 5.2 : 4.4) + Hash.H3(x0, z0, 3) * (grown ? 1.6 : 1.2);
        for (int y = bse + 5 + lift; y < bse + 8 + lift + h; y++)
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

    /// <summary>A bare dead tree with a few crooked branches.</summary>
    public static void DeadTree(VoxelWorld w, int x0, int z0, bool grown = false, double scale = 1)
    {
        int bse = w.SurfAt(x0, z0), H = grown ? 26 + (int)Math.Floor(Hash.H3(x0, z0, 5) * 10) : 20 + (int)Math.Floor(Hash.H3(x0, z0, 5) * 8);
        H = (int)(H * scale);
        Trunk(w, x0, z0, bse, bse + H, scale > 1.2 ? 3 : 2);
        var R = new Mulberry32((uint)(x0 * 31 + z0));
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

    /// <summary>A spruce: needles in layered skirts narrowing to the tip.</summary>
    public static void Spruce(VoxelWorld w, int x0, int z0, int h, int thick = 2)
    {
        int b = w.SurfAt(x0, z0);
        Trunk(w, x0, z0, b, b + (int)(h * .85), thick);
        double maxR = 3.2 + h * .17;
        int start = b + (int)(h * .2), top = b + h;
        for (int y = start; y < top; y++)
        {
            double t = (y - start) / (double)(top - start);
            double skirt = 1 - ((y - start) % 7) / 7.0;
            double r = maxR * Math.Pow(1 - t, .95) * (.7 + .3 * skirt) + .6;
            for (int z = (int)Math.Floor(z0 - r - 1); z <= z0 + r + 2; z++)
                for (int x = (int)Math.Floor(x0 - r - 1); x <= x0 + r + 2; x++)
                {
                    double d = Brushes.Dist(x - x0 - .5, z - z0 - .5) + (Hash.H3(x >> 1, y >> 1, z >> 1) - .5) * 1.4;
                    if (d < r) Put(w, x, y, z, Mat.Leaf);
                }
        }
    }

    /// <summary>A broad oak: a thick trunk, crooked boughs and a lumpy crown of leaves.</summary>
    public static void Oak(VoxelWorld w, int x0, int z0, int h, int thick = 3)
    {
        int b = w.SurfAt(x0, z0), trunkTop = b + (int)(h * .55);
        Trunk(w, x0 - thick / 2, z0 - thick / 2, b, trunkTop, thick);
        var R = new Mulberry32((uint)(x0 * 73 + z0 * 19 + 5));
        for (int k = 0; k < 4; k++)
        {
            double a = R.Next() * 6.28, len = h * (.16 + R.Next() * .12);
            double x = x0 + .5, z = z0 + .5, y = trunkTop - 3 - R.Next() * 6;
            for (int s = 0; s < len; s++)
            {
                x += Math.Cos(a) * .8; z += Math.Sin(a) * .8; y += .55;
                Put(w, (int)x, (int)y, (int)z, Mat.Trunk);
                Put(w, (int)x + 1, (int)y, (int)z, Mat.Trunk);
            }
        }
        double cy = trunkTop + h * .1, rad = h * .3;
        Span<double> bx = stackalloc double[5], by = stackalloc double[5], bz = stackalloc double[5], br = stackalloc double[5];
        for (int k = 0; k < 5; k++)
        {
            double a = R.Next() * 6.28, off = k == 0 ? 0 : rad * (.45 + R.Next() * .3);
            bx[k] = x0 + .5 + Math.Cos(a) * off; bz[k] = z0 + .5 + Math.Sin(a) * off;
            by[k] = cy + (k == 0 ? rad * .15 : (R.Next() - .4) * rad * .5);
            br[k] = rad * (k == 0 ? .85 : .55 + R.Next() * .2);
        }
        int n = (int)Math.Ceiling(rad * 1.7);
        for (int y = (int)(cy - rad); y <= cy + rad * 1.2; y++)
            for (int z = z0 - n; z <= z0 + n; z++)
                for (int x = x0 - n; x <= x0 + n; x++)
                {
                    double best = 9;
                    for (int k = 0; k < 5; k++)
                    {
                        double dx = (x + .5 - bx[k]) / br[k], dy = (y + .5 - by[k]) / (br[k] * .78), dz = (z + .5 - bz[k]) / br[k];
                        best = Math.Min(best, dx * dx + dy * dy + dz * dz);
                    }
                    if (best + (Hash.H3(x >> 1, y >> 1, z >> 1) - .5) * .3 < 1) Put(w, x, y, z, Mat.Foliage);
                }
    }

    /// <summary>A birch: a slender white trunk marked with black, a light open crown in loose clumps.</summary>
    public static void Birch(VoxelWorld w, int x0, int z0, int h)
    {
        int b = w.SurfAt(x0, z0), top = b + h;
        var R = new Mulberry32((uint)(x0 * 41 + z0 * 23 + 9));
        for (int y = b; y < b + (int)(h * .8); y++)
            for (int dz = 0; dz < 2; dz++)
                for (int dx = 0; dx < 2; dx++)
                    Put(w, x0 + dx, y, z0 + dz, Hash.H3(x0 + dx, y, z0 + dz) < .14 ? Mat.Trunk : Mat.Birch);
        for (int k = 0; k < 7; k++)
        {
            double a = R.Next() * 6.28, y = b + h * (.4 + R.Next() * .45), x = x0 + 1, z = z0 + 1;
            double reach = 4 + R.Next() * 5, rad = 3.4 + R.Next() * 2.4;
            for (int s = 0; s < reach; s++) { x += Math.Cos(a) * .8; z += Math.Sin(a) * .8; y += .5; Put(w, (int)x, (int)y, (int)z, Mat.Birch); }
            for (int dy = -(int)rad; dy <= rad; dy++)
                for (int dz = -(int)rad - 1; dz <= rad + 1; dz++)
                    for (int dx = -(int)rad - 1; dx <= rad + 1; dx++)
                    {
                        double d = Brushes.Dist(dx, dy * 1.3, dz) + (Hash.H3((int)x + dx >> 1, (int)y + dy >> 1, (int)z + dz >> 1) - .5) * 1.8;
                        if (d < rad) Put(w, (int)x + dx, (int)y + dy, (int)z + dz, Mat.Foliage);
                    }
        }
        for (int y = top - 8; y < top; y++)
            for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++)
                    if (Brushes.Dist(dx, (y - top + 4) * 1.4, dz) < 3.3) Put(w, x0 + dx, y, z0 + dz, Mat.Foliage);
    }

    /// <summary>A pine: a tall straight trunk bare for most of its height, and a flat crown of needles in a few clumps up top.</summary>
    public static void Pine(VoxelWorld w, int x0, int z0, int h)
    {
        int b = w.SurfAt(x0, z0), top = b + h;
        Trunk(w, x0 - 1, z0 - 1, b, top - 4, 3);
        var R = new Mulberry32((uint)(x0 * 59 + z0 * 31 + 3));
        for (int k = 0; k < 6; k++)
        {
            double a = R.Next() * 6.28, off = k == 0 ? 0 : 3 + R.Next() * 5;
            double cx = x0 + .5 + Math.Cos(a) * off, cz = z0 + .5 + Math.Sin(a) * off, cy = top - 6 - R.Next() * (h * .22);
            double rx = 4.5 + R.Next() * 3, ry = 2.2 + R.Next() * 1.4;
            if (off > 0) for (double s = 0; s < off; s += .8) Put(w, (int)(x0 + .5 + Math.Cos(a) * s), (int)(cy - 1), (int)(z0 + .5 + Math.Sin(a) * s), Mat.Trunk);
            for (int y = (int)(cy - ry); y <= cy + ry; y++)
                for (int z = (int)(cz - rx - 1); z <= cz + rx + 1; z++)
                    for (int x = (int)(cx - rx - 1); x <= cx + rx + 1; x++)
                    {
                        double ex = (x + .5 - cx) / rx, ey = (y + .5 - cy) / ry, ez = (z + .5 - cz) / rx;
                        if (ex * ex + ey * ey + ez * ez + (Hash.H3(x >> 1, y >> 1, z >> 1) - .5) * .5 < 1) Put(w, x, y, z, Mat.Leaf);
                    }
        }
    }

    /// <summary>A low lumpy bush.</summary>
    public static void Bush(VoxelWorld w, int x0, int z0, int r)
    {
        int b = w.SurfAt(x0, z0);
        double cy = b + r * .55;
        for (int y = b; y <= b + r * 1.3; y++)
            for (int z = z0 - r - 1; z <= z0 + r + 1; z++)
                for (int x = x0 - r - 1; x <= x0 + r + 1; x++)
                {
                    double d = Brushes.Dist(x - x0, (y - cy) * 1.25, z - z0) + (Hash.H3(x >> 1, y >> 1, z >> 1) - .5) * 1.6;
                    if (d < r) Put(w, x, y, z, Mat.Foliage);
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
