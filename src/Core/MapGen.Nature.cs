using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoxelTower.Core;

/// <summary>The kinds of small things a valley grows and leaves lying about by itself (<see cref="MapGen"/>, its last pass).</summary>
public enum Detail : byte { FallenTree, Stump, Hazel, BerryBush, Bilberry, Juniper, Anthill, Pit, Burrow, Hollow, Nest, MossyBoulder, Scree, OldOak, Drowned, FairyRing }

public sealed partial class Site
{
    /// <summary>
    /// The small things the valley was made with, where they are and how far they reach (voxels): what the bench counts,
    /// and the shots look for. A fallen tree's (X, Z) is its foot and (DX, DZ) the way it lies, R long; a hollow's (X, Z)
    /// is its trunk's middle, R the height of it and (DX, DZ) the way it faces; a nest's R is its height.
    /// </summary>
    public readonly List<(Detail Kind, int X, int Z, int R, float DX, float DZ)> Details = new();
    /// <summary>What kind of ground each column was made as (<see cref="Relief"/>, index z·SX + x): the plateau, a road, the
    /// river's bed and the rest, as the small things were set out by it; null for the old valley of the box
    /// and for one made without them.</summary>
    public byte[] GroundKind;
}

public static partial class MapGen
{
    /// <summary>
    /// The last of a valley made by itself, once the woods stand: the small things that make it look lived in by nature
    /// and not drawn. Along the woods' edges hazel and berry bushes, under the pines bilberry and anthills, ferns over the
    /// forest floor and mushrooms in it, here and there a tree that came down long ago with its root plate on end and the
    /// pit it tore out, stumps, a hollow in an old oak, a nest on a dead tree; juniper on the dry slopes and scree under
    /// the crags; the meadows in drifts of poppies, daisies, cornflowers, buttercups and bellflowers, an old oak standing
    /// alone in one, a ring of mushrooms in another; hollows in the ground, some holding a puddle, and burrows in the
    /// banks; reeds along the still water and lilies on the lakes. The kind of valley sets how much of each: the forest
    /// more of the woods' things, the marsh of the water's, the mountains of juniper and stone. All by the valley's seed;
    /// nothing on the citadel's plateau, the roads, the river's bed, the named places or the ore, nor in the horde's strip.
    /// </summary>
    static void Nature(VoxelWorld w, Site site, MapParams p, Relief rel)
    {
        int SX = w.SX, SZ = w.SZ;
        var hex = site.Hex;
        var kind = rel.Kind; var moist = rel.Moist; var water = rel.Water;
        site.GroundKind = kind;
        uint seed = p.Seed;
        int salt = (int)(seed * 2654435761u >> 9);
        double H(int a, int b, int c) => Hash.H3(a + salt, b - salt * 3, c);
        var look = Looks(p.Kind);
        var nDrift = new ValueNoise(seed * 71 + 201); var nHue = new ValueNoise(seed * 73 + 202); var nFern = new ValueNoise(seed * 79 + 203);
        var nReed = new ValueNoise(seed * 83 + 204); var nLily = new ValueNoise(seed * 89 + 205);

        // ---- how wooded it is round each spot: the share of the sky the crowns cover within 4 m, in cells of a metre
        const int C = 8;
        int NX = SX / C, NZ = SZ / C;
        var sum = new int[(NX + 1) * (NZ + 1)];
        for (int cz = 0; cz < NZ; cz++)
            for (int cx = 0, row = 0; cx < NX; cx++)
            {
                int x = cx * C + 4, z = cz * C + 4, i = z * SX + x;
                bool crown = false;
                if (w.OnLand(x, z))
                {
                    int top = w.ColTop(x, z);
                    crown = top > site.HTop[i] + 8 && Mat.IsVeg[w.Get(x, top - 1, z)];
                }
                row += crown ? 1 : 0;
                sum[(cz + 1) * (NX + 1) + cx + 1] = sum[cz * (NX + 1) + cx + 1] + row;
            }
        double Wooded(int x, int z)
        {
            int cx = Math.Clamp(x / C, 0, NX - 1), cz = Math.Clamp(z / C, 0, NZ - 1);
            int x0 = Math.Max(0, cx - 4), x1 = Math.Min(NX, cx + 5), z0 = Math.Max(0, cz - 4), z1 = Math.Min(NZ, cz + 5);
            int n = sum[z1 * (NX + 1) + x1] - sum[z0 * (NX + 1) + x1] - sum[z1 * (NX + 1) + x0] + sum[z0 * (NX + 1) + x0];
            return n / (double)((x1 - x0) * (z1 - z0));
        }

        // ---- where a thing may go: the lowland and the river's banks, clear of the edge, the horde's strip, the citadel,
        // the named places and the ore; for what stands, on open turf with nothing over it, kept apart from what is there
        var taken = new bool[NX * NZ];
        bool Ground(int x, int z, int margin)
        {
            if (!w.OnLand(x, z) || hex.Edge(x, z) < margin + 10 || site.InStrip(x, z)) return false;
            int i = z * SX + x;
            if (kind[i] is not (Relief.Low or Relief.Bank) || water[i] > 0 || site.HTop[i] > w.SY - 32) return false;
            return Brushes.Dist(x - site.KeepX, z - site.KeepZ) > site.KeepR + 70 && !Landmarks.Taken(site, x, z, margin + 4);
        }
        bool Turf(int x, int z)
        {
            int h = site.HTop[z * SX + x];
            return Mat.IsGrass(w.Get(x, h - 1, z)) && (w.Get(x, h, z) == Mat.Air || Mat.IsSprig(w.Get(x, h, z)));
        }
        bool Free(int x, int z, int r)
        {
            for (int cz = Math.Max(0, (z - r) / C); cz <= Math.Min(NZ - 1, (z + r) / C); cz++)
                for (int cx = Math.Max(0, (x - r) / C); cx <= Math.Min(NX - 1, (x + r) / C); cx++)
                    if (taken[cz * NX + cx]) return false;
            return true;
        }
        void Take(int x, int z, int r)
        {
            for (int cz = Math.Max(0, (z - r) / C); cz <= Math.Min(NZ - 1, (z + r) / C); cz++)
                for (int cx = Math.Max(0, (x - r) / C); cx <= Math.Min(NX - 1, (x + r) / C); cx++)
                    taken[cz * NX + cx] = true;
        }
        // nothing taken there yet, and all the ground it will spread over ground a thing may go on
        bool Room(int x, int z, int r)
        {
            if (!Free(x, z, r)) return false;
            for (int k = 0; k < 16; k++)
            {
                double a = k * Math.Tau / 16;
                if (!Ground(x + (int)Math.Round(Math.Cos(a) * r), z + (int)Math.Round(Math.Sin(a) * r), 0)) return false;
                if ((k & 1) == 0 && !Ground(x + (int)Math.Round(Math.Cos(a) * r / 2), z + (int)Math.Round(Math.Sin(a) * r / 2), 0)) return false;
            }
            return true;
        }
        // no tree's wood in a box round a spot up to a height: room for a bush or a stump without growing into a trunk
        bool NoWood(int x, int z, int r, int h)
        {
            int g = site.HTop[z * SX + x];
            for (int zz = z - r; zz <= z + r; zz += 2)
                for (int xx = x - r; xx <= x + r; xx += 2)
                    for (int y = g - 2; y <= g + h; y += 2)
                        if (Mat.IsTrunk(w.Get(xx, y, zz))) return false;
            return true;
        }
        // a spot on a jittered grid of so many voxels, the same for the same seed
        IEnumerable<(int x, int z, int qx, int qz)> Grid(int step, int saltG)
        {
            for (int qz = 0; qz < SZ; qz += step)
                for (int qx = 0; qx < SX; qx += step)
                {
                    int x = qx + (int)(H(qx, qz, saltG) * step), z = qz + (int)(H(qx, qz, saltG + 1) * step);
                    if (x < SX && z < SZ) yield return (x, z, qx, qz);
                }
        }
        void Note(Detail k, int x, int z, int r, double dx = 0, double dz = 0) => site.Details.Add((k, x, z, r, (float)dx, (float)dz));

        // ---- hollows in the ground, a pace or two across and a few hands deep; in a wet one a puddle stands
        foreach (var (x, z, qx, qz) in Grid(72, 301))
        {
            if (H(qx, qz, 302) > .32 * look.Pits || !Ground(x, z, 14) || !Free(x, z, 10) || !Turf(x, z)) continue;
            int r = 4 + (int)(H(qx, qz, 303) * 5), depth = 2 + (int)(H(qx, qz, 304) * 2.6);
            if (!NoWood(x, z, r + 2, 6)) continue;
            bool puddle = moist[z * SX + x] > .5 && H(qx, qz, 305) < .65;
            if (Dip(w, site, rel, x, z, r, depth, puddle, grass: true)) { Take(x, z, r); Note(Detail.Pit, x, z, r); }
        }

        // ---- trees that came down long ago, in the woods: along the ground, root plate on end, the pit behind it
        foreach (var (x, z, qx, qz) in Grid(44, 311))
        {
            double f = Wooded(x, z);
            if (f < .12 || H(qx, qz, 312) > (f > .3 ? .55 : .25) * look.Fallen || !Ground(x, z, 20) || !Turf(x, z)) continue;
            double a = H(qx, qz, 313) * Math.Tau, dx = Math.Cos(a), dz = Math.Sin(a);
            int len = 36 + (int)(H(qx, qz, 314) * 44);
            double r = 2 + H(qx, qz, 315) * 1.5;
            // the whole of it on the ground it may lie on, and nothing else lying there
            bool ok = Room(x, z, 12) && Room(x + (int)(dx * len), z + (int)(dz * len), 8) && Room(x + (int)(dx * len / 2), z + (int)(dz * len / 2), 8);
            for (int s = 0; s <= len && ok; s += 6)
                for (int q = -8; q <= 8 && ok; q += 8)
                {
                    int lx = x + (int)(dx * s - dz * q), lz = z + (int)(dz * s + dx * q);
                    ok = Ground(lx, lz, 6) && kind[lz * SX + lx] != Relief.Bank;
                }
            if (!ok) continue;
            bool roots = H(qx, qz, 316) < .7;
            int pr = (int)(r * 2.3 + 2);
            // the pit where it stood, behind its foot
            int pxi = x - (int)Math.Round(dx * (pr * .55 + 1.5)), pzi = z - (int)Math.Round(dz * (pr * .55 + 1.5));
            if (roots && (!Ground(pxi, pzi, 6) || !NoWood(pxi, pzi, pr, 8))) roots = false;
            if (!Flora.RoomToLie(w, x, z, dx, dz, len, r, out double yFoot, out double yTop)) continue;
            if (roots) Dip(w, site, rel, pxi, pzi, pr - 1, 2 + (int)(r * .8), puddle: moist[pzi * SX + pxi] > .55, grass: false);
            // what wood it is: grey and long dead mostly, else the bark of the trees about it
            byte bark = H(qx, qz, 317) < .45 ? Mat.Snag : NearBirch(w, site, x, z) ? Mat.Birch : Mat.Trunk;
            Flora.FallenTree(w, x, z, dx, dz, len, r, yFoot, yTop, bark, roots);
            for (int s = 0; s <= len; s += 8) Take(x + (int)(dx * s), z + (int)(dz * s), 6);
            Take(pxi, pzi, pr);
            Note(Detail.FallenTree, x, z, len, dx, dz);
        }

        // ---- stumps of trees broken off, in the woods and at their edges
        foreach (var (x, z, qx, qz) in Grid(40, 321))
        {
            double f = Wooded(x, z);
            if (f < .08 || H(qx, qz, 322) > .3 * look.Stumps || !Ground(x, z, 10) || !Room(x, z, 7) || !Turf(x, z)) continue;
            double r0 = 1.7 + H(qx, qz, 323) * 1.3;
            byte bark = H(qx, qz, 324) < .5 ? Mat.Snag : NearBirch(w, site, x, z) ? Mat.Birch : Mat.Trunk;
            if (Flora.Stump(w, x, z, r0, 3 + (int)(H(qx, qz, 325) * 5), bark)) { Take(x, z, 6); Note(Detail.Stump, x, z, (int)r0 + 2); }
        }

        // ---- the bushes: hazel and berries along the woods' edges, bilberry under the pines, juniper on the dry slopes
        foreach (var (x, z, qx, qz) in Grid(26, 331))
        {
            if (!Ground(x, z, 10) || !Turf(x, z)) continue;
            int i = z * SX + x;
            double f = Wooded(x, z), m = moist[i], edge = f > .06 && f < .5 ? 1 - Math.Abs(f - .25) / .25 : 0, roll = H(qx, qz, 332);
            int slope = Slope(site, x, z);
            if (roll < .3 * edge * look.Bushes)
            {
                int h = 12 + (int)(H(qx, qz, 333) * 10);
                if (!Room(x, z, h / 2 + 1) || !NoWood(x, z, h / 2 + 1, h)) continue;
                if (H(qx, qz, 334) < .5) { Flora.Hazel(w, x, z, h); Note(Detail.Hazel, x, z, h / 2); }
                else { Flora.BerryBush(w, x, z, 3 + (int)(H(qx, qz, 335) * 2.5), m > .5 ? Mat.BerryBlue : Mat.BerryRed); Note(Detail.BerryBush, x, z, 5); }
                Take(x, z, h / 2);
            }
            else if (f > .35 && roll < .42 * look.Bilberry && m < .7)
            {
                int r = 4 + (int)(H(qx, qz, 336) * 4);
                if (!Room(x, z, r + 3) || !NoWood(x, z, r, 4)) continue;
                Flora.Bilberry(w, x, z, r);
                Take(x, z, r); Note(Detail.Bilberry, x, z, r);
            }
            else if (f < .3 && (m < .3 || slope > 3) && roll < .09 * look.Juniper)
            {
                int h = 9 + (int)(H(qx, qz, 337) * 9);
                bool spread = slope > 4 || H(qx, qz, 338) < .35;
                if (!Room(x, z, spread ? (int)(h * .8) + 1 : 5) || !NoWood(x, z, 5, h)) continue;
                Flora.Juniper(w, x, z, h, spread);
                Take(x, z, spread ? 8 : 4); Note(Detail.Juniper, x, z, spread ? 8 : 4);
            }
        }

        // ---- anthills under the pines; boulders gone over with moss in the woods
        foreach (var (x, z, qx, qz) in Grid(60, 341))
        {
            double f = Wooded(x, z);
            if (f < .3 || !Ground(x, z, 10) || !Turf(x, z)) continue;
            double roll = H(qx, qz, 342);
            if (roll < .28 * look.Anthills && moist[z * SX + x] < .62)
            {
                double r = 3.5 + H(qx, qz, 343) * 2.5;
                if (!Room(x, z, 7) || !NoWood(x, z, 6, 8)) continue;
                Flora.AnthillAt(w, x, z, r, 4 + (int)(H(qx, qz, 344) * 4));
                Take(x, z, 6); Note(Detail.Anthill, x, z, (int)r);
            }
            else if (roll > 1 - .2 * look.Stones)
            {
                int r = 4 + (int)(H(qx, qz, 345) * 4);
                if (!Room(x, z, r + 3) || !NoWood(x, z, r + 2, r + 2)) continue;
                Flora.Boulder(w, x, z, r, site.HTop[z * SX + x]);
                Moss(w, site, x, z, r + 2);
                Take(x, z, r + 2); Note(Detail.MossyBoulder, x, z, r);
            }
        }

        // ---- burrows in the banks and the slopes: a hole into the higher side, the dug earth heaped before it
        foreach (var (x, z, qx, qz) in Grid(64, 351))
        {
            if (H(qx, qz, 352) > .35 * look.Burrows || !Ground(x, z, 12) || !Room(x, z, 10)) continue;
            if (Burrow(w, site, x, z)) { Take(x, z, 8); Note(Detail.Burrow, x, z, 6); }
        }

        // ---- an old oak standing alone in a meadow, its crown spread wide
        for (int k = 0, found = 0; k < 400 && found < look.OldOaks; k++)
        {
            int x = (int)(H(k, 1, 361) * SX), z = (int)(H(k, 2, 361) * SZ);
            if (!Ground(x, z, 70) || !Turf(x, z) || Wooded(x, z) > .02 || site.RoadDist(x, z) < 50 || !Room(x, z, 34) || Slope(site, x, z) > 2) continue;
            if (Brushes.Dist(x - site.KeepX, z - site.KeepZ) < 260 || !Flora.Oak(w, x, z, 88 + (int)(H(k, 3, 361) * 12), 5)) continue;
            Take(x, z, 30); Note(Detail.OldOak, x, z, 30);
            found++;
        }

        // ---- hollows in old trunks; a nest on top of a dead one snapped off
        for (int qz = 0; qz < SZ; qz += 20)
            for (int qx = 0; qx < SX; qx += 20)
            {
                // the trunk standing in this patch of the woods, if any: its wood at two and a half metres
                int x = -1, z = -1, g = 0;
                for (int zz = qz + 1; zz < qz + 20 && x < 0; zz += 2)
                    for (int xx = qx + 1; xx < qx + 20; xx += 2)
                    {
                        if (!Ground(xx, zz, 10)) continue;
                        int gg = site.HTop[zz * SX + xx];
                        if (Mat.IsTrunk(w.Get(xx, gg + 20, zz))) { (x, z, g) = (xx, zz, gg); break; }
                    }
                if (x < 0) continue;
                byte m = w.Get(x, g + 20, z);
                if (m == Mat.Trunk && H(qx, qz, 372) < .06 * look.Hollows)
                {
                    var (ax, az, n) = Axis(w, x, g + 20, z);
                    if (n < 14) continue;
                    int y = g + 14 + (int)(H(qx, qz, 373) * 12);
                    double way = H(qx, qz, 374) * Math.Tau;
                    if (Flora.HollowIn(w, ax, az, y, way)) Note(Detail.Hollow, (int)ax, (int)az, y, Math.Cos(way), Math.Sin(way));
                    continue;
                }
                if (m != Mat.Snag || H(qx, qz, 375) > .5) continue;
                // up the trunk as it leans, to where it broke off
                int ix = x, iz = z, top = g + 6;
                for (int y = g + 6; y < w.SY - 4; y++)
                {
                    var (ax, az, n) = Axis(w, ix, y, iz);
                    if (n == 0) break;
                    (ix, iz, top) = ((int)ax, (int)az, y);
                }
                if (top < g + 22 || w.Get(ix, top + 1, iz) != Mat.Air || !Ground(ix, iz, 2)) continue;
                Flora.NestAt(w, ix, top + 1, iz);
                Note(Detail.Nest, ix, iz, top + 1);
            }

        // ---- dead trees standing grey on the marshes' wet shores, killed by the water
        if (look.Drowned > 0)
            foreach (var (x, z, qx, qz) in Grid(36, 381))
            {
                if (H(qx, qz, 382) > .6 * look.Drowned || !Ground(x, z, 20) || !Turf(x, z) || Shore(water, kind, SX, x, z) != 1 || !Room(x, z, 10)) continue;
                if (Flora.DeadTree(w, x, z, grown: true, scale: 1.1 + H(qx, qz, 383) * .4)) { Take(x, z, 10); Note(Detail.Drowned, x, z, 6); }
            }

        // ---- scree under the crags: stones fallen from the rock lying at its foot, a few big ones
        for (int z = 2; z < SZ - 2; z += 2)
            for (int x = 2 + (z >> 1 & 1); x < SX - 2; x += 2)
            {
                int i = z * SX + x;
                if (kind[i] is not (Relief.Low or Relief.Bank) || water[i] > 0 || !w.OnLand(x, z)) continue;
                // a crag that stands over this spot within 3 voxels
                int h = site.HTop[i], over = 0;
                for (int d = 1; d <= 3; d++)
                    foreach (var (ox, oz) in Four)
                    {
                        int j = (z + oz * d) * SX + x + ox * d;
                        if (j >= 0 && j < kind.Length && kind[j] == Relief.Crag) over = Math.Max(over, site.HTop[j] - h);
                    }
                if (over < 6 || h > w.SY - 4 || H(x, z, 391) > .2 * look.Scree || !Ground(x, z, 2) || w.Get(x, h, z) != Mat.Air && !Mat.IsSprig(w.Get(x, h, z))) continue;
                if (H(x, z, 392) < .04 && Room(x, z, 4)) { Flora.Boulder(w, x, z, 2 + (int)(H(x, z, 393) * 2), h); Take(x, z, 4); Note(Detail.Scree, x, z, 3); }
                else
                {
                    w.SetRaw(x, h, z, Mat.Rock);
                    if (H(x, z, 394) < .35) w.SetRaw(x + 1, h, z, Mat.Rock);
                    if (H(x, z, 395) < .2) w.SetRaw(x, h + 1, z, Mat.Rock);
                }
            }

        // ---- a ring of mushrooms in a meadow, the grass lusher along it
        for (int k = 0, found = 0; k < 300 && found < look.Rings; k++)
        {
            int x = (int)(H(k, 4, 401) * SX), z = (int)(H(k, 5, 401) * SZ), r = 9 + (int)(H(k, 6, 401) * 8);
            if (!Ground(x, z, r + 20) || Wooded(x, z) > .05 || !Free(x, z, r + 2) || Slope(site, x, z) > 2) continue;
            for (double a = 0; a < Math.Tau; a += 1.3 / r)
            {
                int mx = x + (int)Math.Round(Math.Cos(a) * r), mz = z + (int)Math.Round(Math.Sin(a) * r);
                if (!Ground(mx, mz, 4)) continue;
                int g = site.HTop[mz * SX + mx];
                if (!Mat.IsGrass(w.Get(mx, g - 1, mz))) continue;
                w.SetRaw(mx, g - 1, mz, Mat.GrassLush);
                if (w.Get(mx, g, mz) == Mat.Air || Mat.IsSprig(w.Get(mx, g, mz))) w.SetRaw(mx, g, mz, H(mx, mz, 402) < .7 ? Mat.Mushroom : Mat.Air);
            }
            Take(x, z, r + 2); Note(Detail.FairyRing, x, z, r);
            found++;
        }

        // ---- what grows column by column: the meadows' flowers in drifts, ferns and mushrooms over the forest floor,
        // reeds along the still water and lilies on the lakes (every chunk's row of columns by itself, all at once);
        // the named places and the ore kept clear, looked for only in the metres they are near
        var named = new bool[NX * NZ];
        for (int cz = 0; cz < NZ; cz++)
            for (int cx = 0; cx < NX; cx++) named[cz * NX + cx] = Landmarks.Taken(site, cx * C + 4, cz * C + 4, 8);
        Parallel.For(0, w.CZ, cz =>
        {
            for (int z = cz * VoxelWorld.CS; z < (cz + 1) * VoxelWorld.CS && z < SZ; z++)
                for (int x = 0; x < SX; x++)
                {
                    if (!w.OnLand(x, z) || hex.Edge(x, z) < 6) continue;
                    if (named[Math.Min(NZ - 1, z / C) * NX + Math.Min(NX - 1, x / C)] && Landmarks.Taken(site, x, z, 2)) continue;
                    int i = z * SX + x;
                    byte k = kind[i];
                    double m = moist[i];
                    // lilies by the bank of a broad river where it is slack, in the marshes
                    if (k == Relief.River && water[i] - site.HTop[i] >= 2 && look.Lilies > 1.5 && w.Get(x, water[i], z) == Mat.Air
                        && NearDry(water, SX, x, z, 4) && nLily.Sample(x * .045, z * .045) > .55 && H(x, z, 410) < .25)
                    {
                        w.SetRaw(x, water[i], z, Mat.Lily);
                        continue;
                    }
                    // lilies on a lake, out from its shore in patches
                    if (k == Relief.Lake && water[i] > 0)
                    {
                        int top = water[i];
                        if (top > w.SY - 4) continue;
                        if (water[i] - site.HTop[i] < 2 || w.Get(x, top, z) != Mat.Air) continue;
                        if (nLily.Sample(x * .045, z * .045) > .58 && H(x, z, 411) < .3 * look.Lilies) w.SetRaw(x, top, z, Mat.Lily);
                        else if (water[i] - site.HTop[i] <= 3 && Shore(water, kind, SX, x, z) == 1 && nReed.Sample(x * .05, z * .05) > .5 && H(x, z, 412) < .45 * look.Reeds)
                            Reeds(w, x, top, z, H(x, z, 413));
                        continue;
                    }
                    if (k is not (Relief.Low or Relief.Bank) || water[i] > 0 || site.InStrip(x, z)) continue;
                    int h = site.HTop[i];
                    if (h > w.SY - 4) continue;
                    byte under = w.Get(x, h - 1, z), at = w.Get(x, h, z);
                    if (!Mat.IsGrass(under) || at != Mat.Air) continue;
                    // reeds on the shore of still water, fewer along the river where it is slack by the bank
                    int shore = Shore(water, kind, SX, x, z);
                    if (shore != 0)
                    {
                        double reeds = shore == 1 ? .5 : .18;
                        if (nReed.Sample(x * .05, z * .05) > .42 && H(x, z, 415) < reeds * look.Reeds) Reeds(w, x, h, z, H(x, z, 416));
                        continue;
                    }
                    // a column of the woods or of the open by how many crowns are over it near by
                    int cx = Math.Min(NX - 1, x / C), czc = Math.Min(NZ - 1, z / C);
                    int x0 = Math.Max(0, cx - 3), x1 = Math.Min(NX, cx + 4), z0 = Math.Max(0, czc - 3), z1 = Math.Min(NZ, czc + 4);
                    double f = (sum[z1 * (NX + 1) + x1] - sum[z0 * (NX + 1) + x1] - sum[z1 * (NX + 1) + x0] + sum[z0 * (NX + 1) + x0]) / (double)((x1 - x0) * (z1 - z0));
                    double roll = H(x, z, 417);
                    if (f > .3)
                    {
                        // ferns where it is damp and shaded, in patches; mushrooms here and there in the litter
                        double fern = Site.Smooth01((nFern.Sample(x * .035, z * .035) - .42) / .2) * Math.Clamp((m - .25) * 2.5, 0, 1);
                        if (roll < .09 * fern * look.Ferns) { w.SetRaw(x, h, z, Mat.Fern); if (H(x, z, 418) < .3) w.SetRaw(x, h + 1, z, Mat.Fern); }
                        else if (roll > 1 - .0022 * look.Mushrooms) w.SetRaw(x, h, z, Mat.Mushroom);
                        continue;
                    }
                    if (f > .12) continue;
                    // the open: drifts of flowers, each drift mostly of one colour and the colour by how wet the ground is
                    double drift = Site.Smooth01((nDrift.Sample(x * .014, z * .014) - .6) / .16);
                    if (roll >= .13 * drift * look.Flowers + .003) continue;
                    double hue = nHue.Sample(x * .009 + 3.7, z * .009 + 1.1);
                    byte flower = m < .3 ? (hue < .5 ? Mat.FlowerRed : hue < .78 ? Mat.Flower : Mat.FlowerWhite)
                        : m > .62 ? (hue < .45 ? Mat.FlowerBlue : hue < .78 ? Mat.Flower : Mat.Bloom)
                        : hue < .32 ? Mat.FlowerWhite : hue < .52 ? Mat.FlowerBlue : hue < .72 ? Mat.Flower : hue < .86 ? Mat.Bloom : Mat.FlowerRed;
                    w.SetRaw(x, h, z, flower);
                    if (H(x, z, 419) < .15) w.SetRaw(x, h + 1, z, flower);
                }
        });
    }

    /// <summary>How much of each small thing a kind of valley has, against the first valley's.</summary>
    readonly struct Look
    {
        public readonly double Fallen, Stumps, Bushes, Bilberry, Juniper, Anthills, Stones, Pits, Burrows, Hollows, Drowned, Scree, Ferns,
            Mushrooms, Flowers, Reeds, Lilies;
        public readonly int OldOaks, Rings;
        public Look(double fallen = 1, double stumps = 1, double bushes = 1, double bilberry = 1, double juniper = 1, double anthills = 1,
            double stones = 1, double pits = 1, double burrows = 1, double hollows = 1, double drowned = 0, double scree = 1, double ferns = 1,
            double mushrooms = 1, double flowers = 1, double reeds = 1, double lilies = 1, int oldOaks = 1, int rings = 1)
        {
            Fallen = fallen; Stumps = stumps; Bushes = bushes; Bilberry = bilberry; Juniper = juniper; Anthills = anthills; Stones = stones;
            Pits = pits; Burrows = burrows; Hollows = hollows; Drowned = drowned; Scree = scree; Ferns = ferns; Mushrooms = mushrooms;
            Flowers = flowers; Reeds = reeds; Lilies = lilies; OldOaks = oldOaks; Rings = rings;
        }
    }

    static Look Looks(ValleyKind k) => k switch
    {
        ValleyKind.Forest => new Look(fallen: 1.8, stumps: 1.5, bushes: 1.3, bilberry: 1.5, anthills: 1.8, hollows: 1.6, ferns: 1.5, mushrooms: 2, flowers: .7, juniper: .6, oldOaks: 1),
        ValleyKind.Marsh => new Look(fallen: .8, reeds: 2.2, lilies: 2, drowned: 1, flowers: 1.1, juniper: .4, pits: 1.5, anthills: .5, ferns: 1.2),
        ValleyKind.Mountain => new Look(fallen: .7, juniper: 2.8, scree: 2, stones: 1.8, flowers: .7, ferns: .7, reeds: .6, burrows: 1.5, oldOaks: 0),
        ValleyKind.Ore => new Look(juniper: 1.6, scree: 1.6, stones: 1.4, flowers: .8, burrows: 1.3),
        ValleyKind.Sacred => new Look(flowers: 1.4, rings: 2, oldOaks: 2, mushrooms: 1.4),
        _ => new Look(oldOaks: 1),
    };

    static readonly (int x, int z)[] Four = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>
    /// Whether a column is on a shore: water within two voxels of it when it is dry, dry ground when it is wet. 1 by still
    /// water (a lake, a puddle), 2 by the river, which runs; 0 when it is no shore.
    /// </summary>
    static int Shore(short[] water, byte[] kind, int SX, int x, int z)
    {
        int i = z * SX + x;
        if (kind[i] == Relief.River) return 0;
        bool wet = water[i] > 0;
        int by = 0;
        for (int d = 1; d <= 2; d++)
            foreach (var (ox, oz) in Four)
            {
                int j = (z + oz * d) * SX + x + ox * d;
                if (j < 0 || j >= water.Length || (water[j] > 0) == wet) continue;
                if (kind[j] != Relief.River) return 1;
                by = 2;
            }
        return by;
    }

    /// <summary>Dry ground within so many voxels of a wet column, straight across either way.</summary>
    static bool NearDry(short[] water, int SX, int x, int z, int d)
    {
        for (int k = 1; k <= d; k++)
            foreach (var (ox, oz) in Four)
            {
                int j = (z + oz * k) * SX + x + ox * k;
                if (j >= 0 && j < water.Length && water[j] == 0) return true;
            }
        return false;
    }

    /// <summary>A stand of reeds a metre tall: two or three voxels of them on the ground or on the water.</summary>
    static void Reeds(VoxelWorld w, int x, int y, int z, double roll)
    {
        int tall = roll < .4 ? 2 : 3;
        for (int k = 0; k < tall; k++) if (w.Get(x, y + k, z) is Mat.Air or Mat.Tuft) w.SetRaw(x, y + k, z, Mat.Reed);
    }

    /// <summary>How steep the ground is at a spot: the most it rises or falls over a metre either way.</summary>
    static int Slope(Site site, int x, int z)
    {
        int SX = site.SX, h = site.HTop[z * SX + x], most = 0;
        foreach (var (ox, oz) in Four)
        {
            int xx = Math.Clamp(x + ox * 8, 0, SX - 1), zz = Math.Clamp(z + oz * 8, 0, site.SZ - 1);
            most = Math.Max(most, Math.Abs(site.HTop[zz * SX + xx] - h));
        }
        return most;
    }

    /// <summary>Birches about a spot, so a tree fallen there is a birch too.</summary>
    static bool NearBirch(VoxelWorld w, Site site, int x, int z)
    {
        int birch = 0, other = 0;
        for (int dz = -40; dz <= 40; dz += 4)
            for (int dx = -40; dx <= 40; dx += 4)
            {
                int xx = x + dx, zz = z + dz;
                if (!w.OnLand(xx, zz)) continue;
                byte m = w.Get(xx, site.HTop[zz * site.SX + xx] + 12, zz);
                if (m == Mat.Birch) birch++; else if (Mat.IsTrunk(m)) other++;
            }
        return birch > other;
    }

    /// <summary>The middle of the trunk a voxel of wood belongs to, at its height, and how many voxels across it is there.</summary>
    static (double x, double z, int n) Axis(VoxelWorld w, int x, int y, int z)
    {
        double sx = 0, sz = 0;
        int n = 0;
        for (int dz = -6; dz <= 6; dz++)
            for (int dx = -6; dx <= 6; dx++)
                if (Mat.IsTrunk(w.Get(x + dx, y, z + dz))) { sx += x + dx + .5; sz += z + dz + .5; n++; }
        return n == 0 ? (x + .5, z + .5, 0) : (sx / n, sz / n, n);
    }

    /// <summary>Moss over the tops of what a boulder left bare: most of its upper faces green, the rock under it as it was.</summary>
    static void Moss(VoxelWorld w, Site site, int cx, int cz, int r)
    {
        for (int z = cz - r; z <= cz + r; z++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (!w.OnLand(x, z)) continue;
                int top = w.SurfAt(x, z) - 1;
                if (top >= site.HTop[z * w.SX + x] && w.Get(x, top, z) == Mat.Rock && Hash.H3(x, top, z * 3) < .9) w.SetRaw(x, top, z, Mat.Moss);
            }
    }

    /// <summary>
    /// A hollow in the ground r across and so deep in its middle, its bottom bare earth or turf; a puddle standing in it
    /// when asked and when its rim holds water all round. The valley's heights and water are kept to it. False when the
    /// ground there will not take one (rock, water or wood in the way).
    /// </summary>
    static bool Dip(VoxelWorld w, Site site, Relief rel, int x0, int z0, int r, int depth, bool puddle, bool grass)
    {
        int SX = w.SX;
        int rim = int.MaxValue;
        for (int z = z0 - r - 1; z <= z0 + r + 1; z++)
            for (int x = x0 - r - 1; x <= x0 + r + 1; x++)
            {
                if (!w.OnLand(x, z)) return false;
                int i = z * SX + x;
                double d2 = ((x - x0) * (x - x0) + (z - z0) * (z - z0)) / (double)(r * r);
                if (d2 > 1.3) continue;
                if (rel.Water[i] > 0 || rel.Soil[i] < depth + 2) return false;
                // nothing standing where the ground is to sink: a boulder, a root, a bush
                if (d2 <= 1 && w.Get(x, site.HTop[i], z) is var on && on != Mat.Air && !Mat.IsSprig(on)) return false;
                if (d2 > 1) rim = Math.Min(rim, site.HTop[i]);
            }
        int bottom = int.MaxValue;
        for (int z = z0 - r; z <= z0 + r; z++)
            for (int x = x0 - r; x <= x0 + r; x++)
            {
                double d2 = ((x - x0) * (x - x0) + (z - z0) * (z - z0) + (Hash.H3(x, z, 431) - .5) * r * .8) / (double)(r * r);
                if (d2 >= 1) continue;
                int i = z * SX + x, h = site.HTop[i], low = h - (int)Math.Round(depth * (1 - d2));
                if (low >= h) continue;
                byte top = grass ? w.Get(x, h - 1, z) : Mat.Soil;
                if (!grass || !Mat.IsGrass(top)) top = Mat.Soil;
                // what stood on the turf goes with it; the hollow's floor takes the turf's place
                for (int y = low - 1; y < h + 3; y++)
                {
                    byte m = w.Get(x, y, z);
                    if (y >= h && m != Mat.Air && !Mat.IsSprig(m)) break;
                    w.SetRaw(x, y, z, y == low - 1 ? top : Mat.Air);
                }
                site.HTop[i] = low;
                bottom = Math.Min(bottom, low);
            }
        if (puddle && rim != int.MaxValue && bottom < rim - 1)
        {
            // a hand of water over the lowest of it, and only where everything round it holds it in
            int level = Math.Min(rim - 1, bottom + 1);
            var wet = new HashSet<int>();
            for (int z = z0 - r; z <= z0 + r; z++)
                for (int x = x0 - r; x <= x0 + r; x++)
                    if (site.HTop[z * SX + x] < level && w.Get(x, site.HTop[z * SX + x], z) == Mat.Air) wet.Add(z * SX + x);
            foreach (int i in wet)
                foreach (var (ox, oz) in Four)
                {
                    int j = i + oz * SX + ox;
                    if (!wet.Contains(j) && site.HTop[j] < level) return true;
                }
            foreach (int i in wet)
            {
                int x = i % SX, z = i / SX;
                for (int y = site.HTop[i]; y < level; y++) w.SetRaw(x, y, z, Mat.Water);
                w.SetRaw(x, site.HTop[i] - 1, z, Mat.Soil);
                rel.Water[i] = (short)level;
            }
        }
        return bottom != int.MaxValue;
    }

    /// <summary>
    /// A burrow where the ground at (x, z) falls away steeply one way: a round hole into the higher side, a voxel or two
    /// below its lip, running in and down, and the earth dug out of it heaped before it. False when it is not steep there.
    /// </summary>
    static bool Burrow(VoxelWorld w, Site site, int x, int z)
    {
        int SX = w.SX, h = site.HTop[z * SX + x];
        // the way the ground rises most steeply from here, within a metre
        int best = 0, bx = 0, bz = 0;
        foreach (var (ox, oz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1) })
        {
            int xx = x + ox * 6, zz = z + oz * 6;
            if (!w.OnLand(xx, zz)) continue;
            int rise = site.HTop[zz * SX + xx] - h;
            if (rise > best) (best, bx, bz) = (rise, ox, oz);
        }
        if (best < 6 || !Mat.IsGrass(w.Get(x, h - 1, z))) return false;
        double len = Math.Sqrt(bx * bx + bz * bz), ux = bx / len, uz = bz / len;
        // the hole: in from where the slope begins, a voxel down every three
        int dug = 0;
        for (double s = 0; s < 10; s += .5)
        {
            double cx = x + .5 + ux * s, cz = z + .5 + uz * s, cy = h + 1.5 - s / 3;
            for (int y = (int)cy - 2; y <= cy + 2; y++)
                for (int zz = (int)cz - 2; zz <= cz + 2; zz++)
                    for (int xx = (int)cx - 2; xx <= cx + 2; xx++)
                    {
                        double ox = xx + .5 - cx, oy = y + .5 - cy, oz = zz + .5 - cz, along = ox * ux + oz * uz, across = -ox * uz + oz * ux;
                        if (across * across + oy * oy * 1.2 >= 2.6 || Math.Abs(along) > .6) continue;
                        byte m = w.Get(xx, y, zz);
                        if (m is Mat.Soil or Mat.Grass or Mat.GrassDry or Mat.GrassLush) { w.SetRaw(xx, y, zz, Mat.Air); dug++; }
                    }
        }
        if (dug < 12) return false;
        // the turf over the hole's mouth gone to earth where it hangs; the earth heaped before it
        for (int zz = z - 3; zz <= z + 3; zz++)
            for (int xx = x - 3; xx <= x + 3; xx++)
            {
                double ox = xx - x + ux * 2.5, oz = zz - z + uz * 2.5, d2 = ox * ox + oz * oz;
                if (d2 > 7 || !w.OnLand(xx, zz)) continue;
                int g = w.SurfAt(xx, zz);
                if (Math.Abs(g - h) > 2) continue;
                if (Mat.IsGrass(w.Get(xx, g - 1, zz))) w.SetRaw(xx, g - 1, zz, Mat.Soil);
                if (d2 < 3.5 && w.Get(xx, g, zz) == Mat.Air) w.SetRaw(xx, g, zz, Mat.Soil);
            }
        return true;
    }
}
