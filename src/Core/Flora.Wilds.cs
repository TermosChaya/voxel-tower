using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The things of a valley's open ground set out by MapGen.Wilds.cs: a boulder left lying in a meadow, a ledge of the rock
/// under the turf, thickets of dog rose, hawthorn and broom in flower, a clump of tall flowers, a bracket fungus on dead
/// wood, a log the water left on its bank, the beavers' lodge. They are of the world's own stuff, as the rest of nature
/// is: a boulder is rock the workers break up for stone, a thicket is leaves that burn, a lodge is mud and sticks. Each
/// column of what stands on the ground stands from that column's own ground, so nothing of it hangs.
/// </summary>
public static partial class Flora
{
    /// <summary>
    /// A boulder lying in the open, r across its middle: two lumps of rock run together, longer than wide and lower than
    /// long, turned as it lies, sunk a little into the ground; patches of moss on its top. <paramref name="ground"/> holds
    /// each column's ground (index z·SX + x).
    /// </summary>
    public static void Erratic(VoxelWorld w, int[] ground, int cx, int cz, double r, double turn, double high)
    {
        double ca = Math.Cos(turn), sa = Math.Sin(turn), ra = r * 1.25, rb = r * .85, h = r * high;
        // the second lump, smaller, off along the length and a little across
        double ou = r * .55, ov = r * .3 * (Hash.H3(cx, cz, 81) < .5 ? 1 : -1);
        int n = (int)Math.Ceiling(ra + ou) + 1, g0 = ground[cz * w.SX + cx];
        for (int z = cz - n; z <= cz + n; z++)
            for (int x = cx - n; x <= cx + n; x++)
            {
                if (!w.InBounds(x, 1, z)) continue;
                double u = (x - cx) * ca + (z - cz) * sa, v = -(x - cx) * sa + (z - cz) * ca;
                // the rim broken a little by the grain of the stone, in grains of two
                double rag = (Hash.H3(x >> 1, 83, z >> 1) - .5) * .28;
                double d1 = Sq(u / ra) + Sq(v / rb) + rag, d2 = Sq((u - ou) / (ra * .7)) + Sq((v - ov) / (rb * .8)) + rag;
                double t = Math.Max(d1 < 1 ? h * Math.Pow(1 - d1, .42) : -1, d2 < 1 ? h * .78 * Math.Pow(1 - d2, .42) : -1);
                if (t < 0) continue;
                int g = ground[z * w.SX + x], top = g0 - 1 + (int)Math.Round(t);
                if (top < g) continue;
                for (int y = g - 2; y <= top; y++)
                {
                    byte m = y == top && Hash.H3(x >> 1, y, z >> 1) < .4 ? Mat.Moss : Mat.Rock;
                    if (y < g) { if (w.Get(x, y, z) != Mat.Air) w.SetRaw(x, y, z, Mat.Rock); continue; }
                    if (w.Get(x, y, z) == Mat.Air || Mat.IsSprig(w.Get(x, y, z))) w.SetRaw(x, y, z, m);
                }
            }
    }

    /// <summary>
    /// A ledge of the rock under the turf come out on a slope: a long low back along the slope's contour, half as wide as
    /// long, standing highest on its downhill side, its rim ragged, moss in the cracks of its top; a few stones broken off
    /// it lying in the turf below. (ux, uz) is the way along the contour, downhill to its right.
    /// </summary>
    public static void Outcrop(VoxelWorld w, int[] ground, int cx, int cz, double ux, double uz, double half, double wide, double rise)
    {
        int n = (int)Math.Ceiling(half + wide) + 1;
        double core = Math.Max(0, half - wide);
        for (int z = cz - n; z <= cz + n; z++)
            for (int x = cx - n; x <= cx + n; x++)
            {
                if (!w.InBounds(x, 1, z)) continue;
                double u = (x - cx) * ux + (z - cz) * uz, v = -(x - cx) * uz + (z - cz) * ux;
                // its rim ragged by the grain of the rock, its back smooth
                double du = Math.Max(0, Math.Abs(u) - core), d0 = Math.Sqrt(du * du + v * v) / wide, d = d0 + (Hash.H3(x >> 1, 85, z >> 1) - .5) * .3;
                int g = ground[z * w.SX + x];
                if (d >= 1)
                {
                    // stones broken off it, in the turf below its downhill face
                    if (d < 1.5 && v > 0 && Hash.H3(x, 86, z) < .14 && w.Get(x, g - 1, z) != Mat.Air && !Mat.IsVeg[w.Get(x, g - 1, z)]) w.SetRaw(x, g - 1, z, Mat.Rock);
                    continue;
                }
                double downhill = Math.Clamp(.5 + v / (2 * wide), 0, 1);
                int top = g + (int)Math.Round(rise * Math.Sqrt(Math.Max(0, 1 - d0 * d0)) * (.45 + .55 * downhill));
                for (int y = g - 2; y <= top; y++)
                {
                    bool crack = y == top && Hash.H3(x, y, z >> 2) < .32;
                    if (y < g) { if (w.Get(x, y, z) != Mat.Air) w.SetRaw(x, y, z, Mat.Rock); continue; }
                    if (w.Get(x, y, z) == Mat.Air || Mat.IsSprig(w.Get(x, y, z))) w.SetRaw(x, y, z, crack ? Mat.Moss : Mat.Rock);
                }
            }
    }

    /// <summary>A dog rose: a tangle of arching clumps waist high, pink flowers open on the outside of it and red hips among them.</summary>
    public static void Rose(VoxelWorld w, int x0, int z0, int r)
    {
        int b = w.SurfAt(x0, z0);
        var R = new Mulberry32((uint)(x0 * 71 + z0 * 19 + 23));
        var laid = new List<(int x, int y, int z)>();
        for (int k = 0, n = 3 + (int)(R.Next() * 3); k < n; k++)
        {
            double a = R.Next() * 6.28, off = k == 0 ? 0 : r * (.4 + R.Next() * .5), rr = r * (k == 0 ? .9 : .55 + R.Next() * .3);
            Clump(w, x0 + .5 + Math.Cos(a) * off, b + rr * .75, z0 + .5 + Math.Sin(a) * off, rr, rr * .9, Mat.Foliage, Mat.FoliageLight, .9, 0, laid);
        }
        Berries(w, laid, Mat.BlossomPink, .15);
        Berries(w, laid, Mat.BerryRed, .05);
    }

    /// <summary>A hawthorn: a dense round bush taller than a man on a few stems, its outside white with blossom.</summary>
    public static void Hawthorn(VoxelWorld w, int x0, int z0, int h)
    {
        int b = w.SurfAt(x0, z0);
        var R = new Mulberry32((uint)(x0 * 43 + z0 * 59 + 29));
        var laid = new List<(int x, int y, int z)>();
        double wide = h * .45;
        Clump(w, x0 + .5, b + h * .3, z0 + .5, wide * .65, h * .3, Mat.Foliage, Mat.FoliageLight, .7, 0, laid);
        for (int k = 0, n = 4 + (int)(R.Next() * 3); k < n; k++)
        {
            double a = R.Next() * 6.28, t = .45 + R.Next() * .35, off = wide * (.15 + t * .4);
            Clump(w, x0 + .5 + Math.Cos(a) * off, b + h * t, z0 + .5 + Math.Sin(a) * off, wide * (.5 + R.Next() * .2), h * (.24 + R.Next() * .08),
                Mat.Foliage, Mat.FoliageLight, .7, .1, laid);
        }
        Berries(w, laid, Mat.BlossomWhite, .24);
    }

    /// <summary>Broom on a dry slope: a low spreading mound of thin green twigs gone yellow all over with flowers.</summary>
    public static void BroomBush(VoxelWorld w, int x0, int z0, int r)
    {
        int b = w.SurfAt(x0, z0);
        var R = new Mulberry32((uint)(x0 * 31 + z0 * 97 + 37));
        var laid = new List<(int x, int y, int z)>();
        for (int k = 0, n = 3 + (int)(R.Next() * 3); k < n; k++)
        {
            double a = R.Next() * 6.28, off = k == 0 ? 0 : r * (.35 + R.Next() * .5), rr = r * (k == 0 ? .85 : .5 + R.Next() * .3);
            Clump(w, x0 + .5 + Math.Cos(a) * off, b + rr * .55, z0 + .5 + Math.Sin(a) * off, rr, rr * .7, Mat.FoliageLight, Mat.Foliage, .8, .15, laid);
        }
        Berries(w, laid, Mat.Broom, .55);
    }

    /// <summary>
    /// A tall flower on (x, g, z): a green stem, then its flowers up the top of it, with bells hanging off one side of them
    /// when asked (a foxglove's), and a few leaves at its foot.
    /// </summary>
    public static void Spire(VoxelWorld w, int x, int g, int z, int stem, int bloom, byte flower, bool bells)
    {
        for (int y = g; y < g + stem + bloom; y++)
            if (!Put(w, x, y, z, y < g + stem ? Mat.Foliage : flower)) return;
        int side = (int)(Hash.H3(x, g, z) * 4);
        int sx = side switch { 0 => 1, 1 => -1, _ => 0 }, sz = side switch { 2 => 1, 3 => -1, _ => 0 };
        if (bells)
            for (int y = g + stem; y < g + stem + bloom - 1; y += 2) Put(w, x + sx, y, z + sz, flower);
        // the leaves at its foot, on the other sides where the ground is as high
        if (Hash.H3(z, g, x) < .7 && w.SurfAt(x - sx, z - sz) == g) Put(w, x - sx, g, z - sz, Mat.Foliage);
        if (Hash.H3(x, g + 1, z) < .5 && w.SurfAt(x + sz, z + sx) == g) Put(w, x + sz, g, z + sx, Mat.Foliage);
    }

    /// <summary>
    /// A bracket fungus on dead wood: a shelf a voxel thick standing out of the face of the wood at (x, y, z) the way
    /// (nx, nz), two or three voxels along that face, each of them on the wood, its middle standing out a voxel more.
    /// False when there is no room for it there.
    /// </summary>
    public static bool BracketOn(VoxelWorld w, int x, int y, int z, int nx, int nz)
    {
        int tx = nz, tz = nx, laid = 0;
        for (int s = -1; s <= 1; s++)
        {
            int px = x + nx + tx * s, pz = z + nz + tz * s;
            if (!Mat.IsTrunk(w.Get(x + tx * s, y, z + tz * s))) continue;
            if (Put(w, px, y, pz, Mat.Bracket)) laid++;
        }
        if (laid > 1) Put(w, x + nx * 2, y, z + nz * 2, Mat.Bracket);
        return laid > 0;
    }

    /// <summary>
    /// A log the water left on its bank, long bare of its bark and gone grey: two voxels thick and len long the way
    /// (dx, dz), lying along the ground column by column, thinner at its ends, a broken branch sticking up off it.
    /// </summary>
    public static void Driftwood(VoxelWorld w, int[] ground, int x0, int z0, double dx, double dz, int len)
    {
        double px = -dz, pz = dx;
        for (double s = 0; s <= len; s += .5)
            for (double q = 0; q <= 1; q += .5)
            {
                int x = (int)Math.Floor(x0 + .5 + dx * s + px * q), z = (int)Math.Floor(z0 + .5 + dz * s + pz * q);
                if (!w.InBounds(x, 1, z)) continue;
                int g = ground[z * w.SX + x];
                bool end = s < 1.5 || s > len - 1.5;
                Put(w, x, g, z, Mat.Snag);
                if (!end) Put(w, x, g + 1, z, Mat.Snag);
            }
        // a branch broken off short, up and out from the log a third of the way along
        double at = len * (.3 + Hash.H3(x0, z0, 87) * .4);
        int bx = (int)Math.Floor(x0 + .5 + dx * at), bz = (int)Math.Floor(z0 + .5 + dz * at);
        if (w.InBounds(bx, 1, bz))
        {
            int g = ground[bz * w.SX + bx];
            Put(w, bx, g + 2, bz, Mat.Snag);
            Put(w, bx + (int)Math.Round(px), g + 3, bz + (int)Math.Round(pz), Mat.Snag);
        }
    }

    /// <summary>
    /// The beavers' lodge by a lake or a pond: a dome of mud and sticks r across and h high, the sticks lying across one
    /// another over its outside and a few of them standing out of it. It stands on the dry shore only; where its foot
    /// would be in the water it is not laid, so it comes up out of the water's edge.
    /// </summary>
    public static void Lodge(VoxelWorld w, int[] ground, short[] water, int cx, int cz, double r, double h)
    {
        int n = (int)Math.Ceiling(r) + 1, g0 = ground[cz * w.SX + cx];
        for (int z = cz - n; z <= cz + n; z++)
            for (int x = cx - n; x <= cx + n; x++)
            {
                if (!w.InBounds(x, 1, z) || water[z * w.SX + x] > 0) continue;
                double d2 = (Sq(x - cx) + Sq(z - cz)) / (r * r) + (Hash.H3(x, 88, z) - .5) * .2;
                if (d2 >= 1) continue;
                int g = ground[z * w.SX + x], top = g0 - 1 + (int)Math.Round(h * Math.Sqrt(1 - d2));
                for (int y = g; y <= top; y++)
                {
                    // the outside of sticks lying every which way, the inside mud
                    bool outside = y >= top - 1;
                    byte m = outside && Hash.H3(x + y, z, 89) < .62 ? Mat.Snag : Mat.Soil;
                    Put(w, x, y, z, m);
                }
            }
        // sticks standing out of it
        for (int k = 0; k < 6; k++)
        {
            double a = Hash.H3(cx, k, 90) * Math.Tau, dd = r * (.3 + Hash.H3(cz, k, 91) * .5);
            int x = cx + (int)Math.Round(Math.Cos(a) * dd), z = cz + (int)Math.Round(Math.Sin(a) * dd);
            int ox = (int)Math.Round(Math.Cos(a)), oz = (int)Math.Round(Math.Sin(a));
            if (!w.InBounds(x + ox, 1, z + oz) || water[z * w.SX + x] > 0 || water[(z + oz) * w.SX + x + ox] > 0) continue;
            int y = w.ColTop(x, z);
            Put(w, x, y, z, Mat.Snag);
            Put(w, x + ox, y + 1, z + oz, Mat.Snag);
        }
    }
}
