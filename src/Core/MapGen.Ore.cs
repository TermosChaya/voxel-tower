using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Iron in the valley, laid after its relief and its places and before its trees, from nothing but the finished ground
/// (so it outlives any change to how the relief is made). Veins of ore wind down through the rock, a metre or so across;
/// where one comes up to the surface it stands out of the ground as an outcrop, a knob of rusty rock a couple of metres
/// across that is seen from afar, and the vein goes on down into the hill from it. Other veins lie hidden under the
/// ground, to be struck by whoever digs there. How many of each a valley has goes by its kind (<see cref="Valleys.OreOf"/>):
/// the valleys of ore are full of them, every valley has a little. The outcrops are kept in <see cref="Site.Outcrops"/>:
/// the miners' building (Sim.Metal.cs) stands only on one.
/// </summary>
public static class OreVeins
{
    public static void Lay(VoxelWorld w, Site site, uint seed)
    {
        var (outcrops, hidden) = Valleys.OreOf(site.Kind);
        int SX = w.SX, SZ = w.SZ, s = (int)(seed % 1000003);
        // how far in from the land's edge a column is
        double In(int x, int z) => site.Hex != null ? site.Hex.Edge(x, z) : Math.Min(Math.Min(x, SX - x), Math.Min(z, SZ - z));
        // water on the column or within three metres of it (the river, a pond, a marsh)
        bool Wet(int x, int z)
        {
            for (int dz = -24; dz <= 24; dz += 12)
                for (int dx = -24; dx <= 24; dx += 12)
                {
                    int nx = Math.Clamp(x + dx, 0, SX - 1), nz = Math.Clamp(z + dz, 0, SZ - 1);
                    if (w.Get(nx, w.ColTop(nx, nz) - 1, nz) == Mat.Water) return true;
                }
            foreach (var p in site.Ponds) if (Brushes.Dist(x - p.X, z - p.Z) < p.R + 24) return true;
            return false;
        }
        // where an outcrop may come up: on the land well in from its edge, off the citadel's ground, clear of the places
        // and the water, on ground level enough for the miners' building to stand on; better on bare rock
        var spots = new List<(double score, int x, int z)>();
        for (int gz = 24; gz < SZ - 24; gz += 12)
            for (int gx = 24; gx < SX - 24; gx += 12)
            {
                int x = gx + (int)(Hash.H3(gx, gz, s + 1) * 10), z = gz + (int)(Hash.H3(gx, gz, s + 2) * 10);
                if (!w.OnLand(x, z) || In(x, z) < 56 || x < site.SpawnStrip + 40) continue;
                if (Brushes.Dist(x - site.KeepX, z - site.KeepZ) < site.KeepR + 120 || Landmarks.Taken(site, x, z, 24) || Wet(x, z)) continue;
                int top = w.SurfAt(x, z);
                byte m = w.Get(x, top - 1, z);
                if (!Mat.IsNatural[m] || m == Mat.Water) continue;
                int slope = 0;
                foreach (var (dx, dz) in new[] { (8, 0), (-8, 0), (0, 8), (0, -8) }) slope = Math.Max(slope, Math.Abs(w.SurfAt(x + dx, z + dz) - top));
                // not in a hollow, nor under the brow of a steep side: there the knob would not stand out to be seen; and
                // the ground about it near enough level for the miners' building, a house of five metres by four
                double around = 0;
                int lo = int.MaxValue, hi = int.MinValue;
                for (int a = 0; a < 8; a++)
                {
                    int h = w.SurfAt(x + (int)(Math.Cos(a * Math.PI / 4) * 22), z + (int)(Math.Sin(a * Math.PI / 4) * 22));
                    around += h / 8.0; lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                }
                if (top < around - 1 || slope > 4 || hi - lo > 8) continue;
                double weight = (m is Mat.Rock or Mat.Moss ? 3 : 1) + (slope <= 3 ? 1 : 0);
                spots.Add((Hash.H3(x, z, s + 3) * weight, x, z));
            }
        spots.Sort((a, b) => b.score.CompareTo(a.score));
        int made = 0;
        foreach (var (_, x, z) in spots)
        {
            if (made >= outcrops) break;
            bool near = false;
            foreach (var o in site.Outcrops) if (Brushes.Dist(x - o.X, z - o.Z) < 110) { near = true; break; }
            if (near) continue;
            int r = 9 + (int)(Hash.H3(x, z, s + 4) * 6);
            int y = Outcrop(w, x, z, r, s + made * 31);
            site.Outcrops.Add((x, z, y, r));
            // the vein goes on from it down into the hill, away from the open side
            double a = Hash.H3(x, z, s + 5) * Math.PI * 2, dip = .35 + Hash.H3(x, z, s + 6) * .5;
            Vein(w, x, y - 2, z, a, dip, 110 + (int)(Hash.H3(x, z, s + 7) * 110), 3.2 + Hash.H3(x, z, s + 8) * 1.4, s + made * 53, deepOnly: false);
            made++;
        }
        // and the hidden ones, under the ground anywhere on the land but the citadel's
        for (int n = 0, tries = 0; n < hidden && tries < hidden * 40; tries++)
        {
            int x = 40 + (int)(Hash.H3(n, tries, s + 9) * (SX - 80)), z = 40 + (int)(Hash.H3(tries, n, s + 10) * (SZ - 80));
            if (!w.OnLand(x, z) || In(x, z) < 48 || Brushes.Dist(x - site.KeepX, z - site.KeepZ) < site.KeepR + 90 || Wet(x, z)) continue;
            int top = w.SurfAt(x, z), y = top - 14 - (int)(Hash.H3(x, z, s + 11) * 18);
            if (y < 8) continue;
            double a = Hash.H3(x, z, s + 12) * Math.PI * 2, dip = (Hash.H3(x, z, s + 13) - .5) * .5;
            Vein(w, x, y, z, a, dip, 70 + (int)(Hash.H3(x, z, s + 14) * 90), 2.4 + Hash.H3(x, z, s + 15) * 1.2, s + 700 + n * 37, deepOnly: true);
            n++;
        }
    }

    /// <summary>
    /// An outcrop: a knob of ore standing up out of the ground, ragged at its edge and highest at its middle, rusty in
    /// patches on its face and flecked with rust inside; the ground under it ore too. The top of its middle.
    /// </summary>
    static int Outcrop(VoxelWorld w, int cx, int cz, int r, int s)
    {
        int peak = 0;
        double height = 6 + Hash.H3(cx, cz, s + 1) * 6;
        for (int z = cz - r - 2; z <= cz + r + 2; z++)
            for (int x = cx - r - 2; x <= cx + r + 2; x++)
            {
                if (!w.OnLand(x, z)) continue;
                double d = Brushes.Dist(x - cx, z - cz) + (Hash.H3(x, z, s + 2) - .5) * 4;
                if (d > r) continue;
                int top = w.GroundTop(x, z);
                double k = 1 - d / r;
                // lumpy in blocks of a few voxels, as a rock is, not grain by grain
                int h = (int)Math.Round(Math.Pow(k, .7) * height + (Hash.H3(x / 3, z / 3, s + 3) - .5) * 2.4);
                // what grew there is gone under it
                for (int y = top; y < top + h + 3 && y < w.SY; y++) if (w.Get(x, y, z) is var m && (Mat.IsVeg[m] || Mat.IsSprig(m))) w.SetRaw(x, y, z, Mat.Air);
                for (int y = Math.Max(4, top - 3); y < top + Math.Max(0, h); y++)
                {
                    // its face rusty in patches over the dark ore, and flecked with rust inside
                    bool face = y >= top + h - 1;
                    double rust = face ? Hash.H3(x / 4, z / 4, s + 4) * .85 + Hash.H3(x, z, s + 5) * .15 : Hash.H3(x * 3 + y, z, s + 4);
                    w.SetRaw(x, y, z, rust < (face ? .55 : .2) ? Mat.OreRust : Mat.IronOre);
                }
                if (Brushes.Dist(x - cx, z - cz) < 2) peak = Math.Max(peak, top + h);
            }
        return peak > 0 ? peak : w.GroundTop(cx, cz);
    }

    /// <summary>
    /// A vein: a tube of ore wound through the ground from a point, its way turning a little as it goes and its girth
    /// swelling and thinning. It takes the place of rock and earth only, never of what is built or of the water, and
    /// keeps below the surface (a hidden one well below it).
    /// </summary>
    static void Vein(VoxelWorld w, int x0, int y0, int z0, double angle, double dip, int length, double girth, int s, bool deepOnly)
    {
        double x = x0, y = y0, z = z0;
        for (int i = 0; i < length; i++)
        {
            angle += (Hash.H3(i, s, 1) - .5) * .18;
            dip = Math.Clamp(dip + (Hash.H3(i, s, 2) - .5) * .08, -.35, .9);
            x += Math.Cos(angle) * Math.Cos(dip);
            z += Math.Sin(angle) * Math.Cos(dip);
            y -= Math.Sin(dip);
            if (y < 6 || !w.InBounds((int)x, (int)y, (int)z)) break;
            double r = girth * (.75 + .5 * Hash.H3(i / 6, s, 3));
            int ri = (int)Math.Ceiling(r);
            for (int dy = -ri; dy <= ri; dy++)
                for (int dz = -ri; dz <= ri; dz++)
                    for (int dx = -ri; dx <= ri; dx++)
                    {
                        if (dx * dx + dy * dy + dz * dz > r * r) continue;
                        int vx = (int)x + dx, vy = (int)y + dy, vz = (int)z + dz;
                        if (!w.InBounds(vx, vy, vz) || !w.OnLand(vx, vz) || vy < 4) continue;
                        byte m = w.Get(vx, vy, vz);
                        if (m is not (Mat.Rock or Mat.Moss or Mat.Soil) && !Mat.IsGrass(m)) continue;
                        // below the surface: the outcrop's own vein may come up to it, a hidden one keeps well under
                        int top = w.GroundTop(vx, vz);
                        if (vy >= top - (deepOnly ? 6 : 1)) continue;
                        w.SetRaw(vx, vy, vz, Hash.H3(vx, vy * 7, vz) < .14 ? Mat.OreRust : Mat.IronOre);
                    }
        }
    }
}
