using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>The things of nature's own a valley's open ground, banks and heights are dressed with (MapGen.Wilds.cs).</summary>
public enum Wild : byte { Erratic, Outcrop, Rose, Hawthorn, Broom, Heather, Spires, Bracket, Driftwood, Pebbles, Lodge, Snow, Cranberry }

public sealed partial class Site
{
    /// <summary>
    /// Nature's dressing of the open laid when the valley was made: what, where its middle is and how far it reaches,
    /// voxels; what was laid column by column (snow, cranberries) once for each block of 32 voxels it is in. For the bench
    /// and the shots.
    /// </summary>
    public readonly List<(Wild Kind, int X, int Z, int R)> Wilds = new();
    /// <summary>Whether the valley was made with it (<see cref="MapParams.Wilds"/>), for a save to make it again so.</summary>
    public bool Wilded;
}

public static partial class MapGen
{
    /// <summary>
    /// How far out of the citadel's ground nature's dressing keeps, voxels over the citadel's own reach: what stands in a
    /// walker's way (boulders, ledges, logs, a lodge) further out than what does not (plants).
    /// </summary>
    public const int WildsClearBig = 230, WildsClearSmall = 90;

    /// <summary>
    /// The very last of a valley made by itself (the author: «Больше декораций богу декораций»): nature's own dressing of
    /// its open ground, where the woods' small things (MapGen.Nature.cs) and the old works of people (MapGen.Traces.cs)
    /// leave it bare. Boulders left lying in the meadows and on the slopes, moss on their tops, fields of them in the
    /// mountains and the ore hills; ledges of the rock come out along the slopes; thickets of dog rose in pink flower with
    /// red hips, of hawthorn white with blossom (most in the holy valley), of broom yellow on the dry slopes; heather on the
    /// heights and the dry ground, most in the mountains and the bog; clumps of tall flowers in the open — foxgloves at the
    /// woods' edges, mullein on the dry ground, loosestrife by the water; bracket fungi on the dead wood and toadstools at
    /// its foot; grey logs the water left on the banks, and pebbles; the beavers' lodge by a lake; in the mountains, snow
    /// still lying under the snow line in the hollows that face north; cranberries and cotton grass on the wet ground of
    /// the bog and the marshes. How much of each the kind of valley says. All by the valley's seed and only where nothing
    /// else is; none of it in the citadel's ground, on a road or the plateau, in water, on a named place or in the horde's
    /// strip; nothing else of the valley is moved by it.
    /// </summary>
    static void Wilds(VoxelWorld w, Site site, MapParams p, Relief rel)
    {
        var hex = site.Hex;
        if (hex == null || rel.Moist == null) return;
        int SX = w.SX, SZ = w.SZ;
        var kind = rel.Kind; var moist = rel.Moist; var water = rel.Water; var top = site.HTop;
        uint seed = p.Seed;
        int salt = (int)(seed * 3266489917u >> 10);
        double H(int a, int b, int c) => Hash.H3(a - salt * 7, b + salt, c + 4021);
        var look = WildLooks(p.Kind);
        var nPatch = new ValueNoise(seed * 97 + 301); var nSnow = new ValueNoise(seed * 101 + 302);

        // ---- how wooded it is round each spot, as the woods' small things read it: the share of the sky the crowns cover within 4 m
        const int C = 8;
        int NX = SX / C, NZ = SZ / C;
        var sum = new int[(NX + 1) * (NZ + 1)];
        for (int cz = 0; cz < NZ; cz++)
            for (int cx = 0, row = 0; cx < NX; cx++)
            {
                int x = cx * C + 4, z = cz * C + 4;
                bool crown = false;
                if (w.OnLand(x, z))
                {
                    int t = w.ColTop(x, z);
                    crown = t > top[z * SX + x] + 8 && Mat.IsVeg[w.Get(x, t - 1, z)];
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

        // ---- what is there already, by metre cells: the woods' small things, the old works, and what is laid here
        var taken = new bool[NX * NZ];
        void Take(int x, int z, int r)
        {
            for (int cz = Math.Max(0, (z - r) / C); cz <= Math.Min(NZ - 1, (z + r) / C); cz++)
                for (int cx = Math.Max(0, (x - r) / C); cx <= Math.Min(NX - 1, (x + r) / C); cx++)
                    taken[cz * NX + cx] = true;
        }
        bool Free(int x, int z, int r)
        {
            for (int cz = Math.Max(0, (z - r) / C); cz <= Math.Min(NZ - 1, (z + r) / C); cz++)
                for (int cx = Math.Max(0, (x - r) / C); cx <= Math.Min(NX - 1, (x + r) / C); cx++)
                    if (taken[cz * NX + cx]) return false;
            return true;
        }
        foreach (var d in site.Details)
        {
            if (d.Kind == Detail.FallenTree) for (int s = 0; s <= d.R; s += 8) Take(d.X + (int)(d.DX * s), d.Z + (int)(d.DZ * s), 6);
            else Take(d.X, d.Z, d.Kind is Detail.Hollow or Detail.Nest ? 4 : d.R + 2);
        }
        foreach (var (_, tx, tz) in site.Traces) Take(tx, tz, 16);

        // ---- where something may go: wild ground, dry, clear of the edge, the horde's strip, the citadel's ground and the
        // named places (and, asked, of the roads); on the heights too for what lies or grows on them
        int Height(int x, int z) => top[Math.Clamp(z, 0, SZ - 1) * SX + Math.Clamp(x, 0, SX - 1)];
        bool Ground(int x, int z, int margin, bool heights, int keep, int road)
        {
            if (!w.OnLand(x, z) || hex.Edge(x, z) < margin + 12 || site.Forbidden(x, z)) return false;
            int i = z * SX + x;
            byte k = kind[i];
            if (!(k is Relief.Low or Relief.Bank || heights && k is Relief.Knoll or Relief.Crag)) return false;
            if (water[i] > 0 || top[i] > w.SY - 20 || Brushes.Dist(x - site.KeepX, z - site.KeepZ) < site.KeepR + keep) return false;
            return (road <= 0 || site.RoadDist(x, z) >= road) && !Landmarks.Taken(site, x, z, margin + 4);
        }
        // natural ground on top of a column, and nothing over it but what is drawn in blades for so many voxels
        bool Bare(int x, int z, int clear)
        {
            int g = top[z * SX + x];
            byte m = w.Get(x, g - 1, z);
            if (!(Mat.IsGrass(m) || m is Mat.Soil or Mat.Moss or Mat.Peat or Mat.Rock or Mat.Scree or Mat.RockPale or Mat.Sand or Mat.Snow)) return false;
            for (int y = g; y < g + clear && y < w.SY; y++)
            {
                byte a = w.Get(x, y, z);
                if (a != Mat.Air && !Mat.IsSprig(a)) return false;
            }
            return true;
        }
        // a box of such ground off the roads (its corners and middle so far from a road), its lowest and highest ground no
        // more apart than so much
        bool Open(int x0, int z0, int x1, int z1, int level, bool heights, int keep, int road, int clear = 14)
        {
            if (road > 0)
                foreach (var (cx, cz) in new[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1), ((x0 + x1) / 2, (z0 + z1) / 2) })
                    if (site.RoadDist(cx, cz) < road) return false;
            int lo = int.MaxValue, hi = int.MinValue;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!Ground(x, z, 0, heights, keep, 0) || !Bare(x, z, clear)) return false;
                    int g = top[z * SX + x];
                    lo = Math.Min(lo, g); hi = Math.Max(hi, g);
                }
            return hi - lo <= level;
        }
        // the nearest water within so far, looked for along the eight ways: the way to it and how far it is (−1: none)
        (double x, double z, int d) ToWater(int x, int z, int most)
        {
            for (int d = 2; d <= most; d += 2)
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.Tau / 8, ux = Math.Cos(a), uz = Math.Sin(a);
                    int px = x + (int)Math.Round(ux * d), pz = z + (int)Math.Round(uz * d);
                    if (w.OnLand(px, pz) && water[pz * SX + px] > 0) return (ux, uz, d);
                }
            return (0, 0, -1);
        }
        // a spot on a jittered grid of so many voxels, the same for the same seed
        IEnumerable<(int x, int z, int qx, int qz)> Grid(int step, int s)
        {
            for (int qz = 0; qz < SZ; qz += step)
                for (int qx = 0; qx < SX; qx += step)
                {
                    int x = qx + (int)(H(qx, qz, s) * step), z = qz + (int)(H(qx, qz, s + 1) * step);
                    if (x < SX && z < SZ) yield return (x, z, qx, qz);
                }
        }

        // ---- boulders left lying in the open, on the slopes and the heights; fields of smaller ones about some of them
        foreach (var (x, z, qx, qz) in Grid(84, 501))
        {
            double f = Wooded(x, z);
            if (f > .12 || H(qx, qz, 502) > .42 * look.Erratic * (f < .04 ? 1 : .5)) continue;
            double r = 4 + H(qx, qz, 503) * (look.Fields > 0 ? 5 : 3.5);
            int n = (int)Math.Ceiling(r * 1.8) + 1;
            if (!Free(x, z, n + 4) || !Open(x - n, z - n, x + n, z + n, (int)r + 3, true, WildsClearBig, 22)) continue;
            Flora.Erratic(w, top, x, z, r, H(qx, qz, 504) * Math.PI, .85 + H(qx, qz, 505) * .4);
            Take(x, z, n + 2);
            site.Wilds.Add((Wild.Erratic, x, z, n));
            if (H(qx, qz, 506) >= look.Fields) continue;
            for (int k = 0, m = 2 + (int)(H(qx, qz, 507) * 4); k < m; k++)
            {
                double a = H(qx, k, 508) * Math.Tau, d = r * 2 + H(k, qz, 509) * r * 2.5;
                int bx = x + (int)(Math.Cos(a) * d), bz = z + (int)(Math.Sin(a) * d);
                double br = 2.5 + H(bx, bz, 510) * r * .45;
                int bn = (int)Math.Ceiling(br * 1.8) + 1;
                if (!Free(bx, bz, bn + 1) || !Open(bx - bn, bz - bn, bx + bn, bz + bn, (int)br + 3, true, WildsClearBig, 22)) continue;
                Flora.Erratic(w, top, bx, bz, br, H(bx, bz, 511) * Math.PI, .7 + H(bz, bx, 512) * .3);
                Take(bx, bz, bn + 1);
                site.Wilds.Add((Wild.Erratic, bx, bz, bn));
            }
        }

        // ---- ledges of the rock come out on the slopes, lying along their contours
        if (look.Outcrop > 0)
            foreach (var (x, z, qx, qz) in Grid(100, 521))
            {
                if (H(qx, qz, 522) > .5 * look.Outcrop || !w.OnLand(x, z) || Wooded(x, z) > .15) continue;
                if (Slope(site, x, z) < 2 && kind[z * SX + x] is not (Relief.Knoll or Relief.Crag)) continue;
                // along the contour, across the fall of the ground, downhill to its right
                int gx = Height(x + 6, z) - Height(x - 6, z), gz = Height(x, z + 6) - Height(x, z - 6);
                double gl = Math.Sqrt(gx * gx + gz * gz), ux, uz;
                if (gl < 1) { double a = H(qx, qz, 523) * Math.PI; ux = Math.Cos(a); uz = Math.Sin(a); }
                else { ux = -gz / gl; uz = gx / gl; }
                double half = 6 + H(qx, qz, 524) * 8, wide = 3 + H(qx, qz, 525) * 2.5, rise = 3 + H(qx, qz, 526) * (look.Outcrop > 1.5 ? 5 : 3);
                int n = (int)Math.Ceiling(half + wide) + 1;
                if (!Free(x, z, n + 2) || !Open(x - n, z - n, x + n, z + n, 26, true, WildsClearBig, 22)) continue;
                Flora.Outcrop(w, top, x, z, ux, uz, half, wide, rise);
                Take(x, z, n + 1);
                site.Wilds.Add((Wild.Outcrop, x, z, n));
            }

        // ---- thickets in the open and along the woods' edges, two or three bushes together: dog rose, hawthorn, and broom
        // on the dry slopes
        foreach (var (x0, z0, qx, qz) in Grid(44, 531))
        {
            double f = Wooded(x0, z0);
            if (f > .4 || H(qx, qz, 532) > .3 * look.Thicket * (f > .03 ? 1 : .45) || !w.OnLand(x0, z0)) continue;
            bool dry = moist[z0 * SX + x0] < .33 || Slope(site, x0, z0) > 3;
            double pick = H(qx, qz, 533) * (look.Rose + look.Hawthorn + (dry ? look.Broom * 2 : 0));
            var what = pick >= look.Rose + look.Hawthorn ? Wild.Broom : pick < look.Rose ? Wild.Rose : Wild.Hawthorn;
            for (int k = 0, bushes = 1 + (int)(H(qx, qz, 535) * 2.6); k < bushes; k++)
            {
                double a = H(qx, k, 536) * Math.Tau, off = k == 0 ? 0 : 9 + H(k, qz, 537) * 7;
                int x = x0 + (int)(Math.Cos(a) * off), z = z0 + (int)(Math.Sin(a) * off);
                int size = what switch { Wild.Rose => 4 + (int)(H(x, z, 534) * 2.5), Wild.Broom => 4 + (int)(H(x, z, 534) * 2.5), _ => 12 + (int)(H(x, z, 534) * 7) };
                int rr = what == Wild.Hawthorn ? size / 2 + 1 : size + 2;
                if (!Free(x, z, rr + 1) || !Open(x - rr, z - rr, x + rr, z + rr, 4, what == Wild.Broom, WildsClearSmall, 12, what == Wild.Hawthorn ? size + 2 : 10)) continue;
                if (what == Wild.Rose) Flora.Rose(w, x, z, size);
                else if (what == Wild.Broom) Flora.BroomBush(w, x, z, size);
                else Flora.Hawthorn(w, x, z, size);
                Take(x, z, rr);
                site.Wilds.Add((what, x, z, rr));
            }
        }

        // ---- heather on the heights and the dry ground: patches of it a few metres across, broken, its flowering tufts among
        // dark green ones, a little higher in their middles
        if (look.Heather > 0)
            foreach (var (x, z, qx, qz) in Grid(32, 541))
            {
                if (H(qx, qz, 542) > .3 * look.Heather || !w.OnLand(x, z)) continue;
                int i = z * SX + x;
                bool high = kind[i] is Relief.Knoll or Relief.Crag || top[i] > w.SY * .55;
                if (!(moist[i] < look.HeatherWet || high) || Wooded(x, z) > .25 || !Ground(x, z, 4, true, WildsClearSmall, 10) || !Free(x, z, 3)) continue;
                int r = 4 + (int)(H(qx, qz, 543) * 7), laid = 0;
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int hx = x + dx, hz = z + dz;
                        double d = Math.Sqrt(dx * dx + dz * dz) / r + (nPatch.Sample(hx * .2, hz * .2) - .5) * .7;
                        if (d >= 1 || H(hx, hz, 545) < .2 + d * .25 || !Ground(hx, hz, 2, true, WildsClearSmall, 0)) continue;
                        int g = top[hz * SX + hx];
                        byte under = w.Get(hx, g - 1, hz), at = w.Get(hx, g, hz);
                        if (!(Mat.IsGrass(under) || under is Mat.Soil or Mat.Peat or Mat.Moss) || at != Mat.Air && !Mat.IsSprig(at)) continue;
                        // in tufts of two: flowering, or the dark green of its leaves
                        byte tuft = nPatch.Sample(hx * .45 + 31, hz * .45 + 7) < .42 ? Mat.Juniper : Mat.Heather;
                        w.SetRaw(hx, g, hz, tuft);
                        if (d < .5 && H(hx >> 1, hz >> 1, 544) < .45 && w.Get(hx, g + 1, hz) == Mat.Air) w.SetRaw(hx, g + 1, hz, tuft);
                        laid++;
                    }
                if (laid > 0) site.Wilds.Add((Wild.Heather, x, z, r));
            }

        // ---- clumps of tall flowers in the open: loosestrife by the water, foxgloves at the woods' edges, mullein on the
        // dry ground, and either of the others elsewhere
        foreach (var (x, z, qx, qz) in Grid(36, 551))
        {
            double f = Wooded(x, z);
            if (f > .35 || H(qx, qz, 552) > .26 * look.Spires || !Ground(x, z, 6, false, WildsClearSmall, 10) || !Free(x, z, 4)) continue;
            int i = z * SX + x;
            bool wet = ToWater(x, z, 12).d >= 0;
            byte flower = wet ? Mat.Heather : f > .04 ? Mat.BlossomPink : moist[i] < .4 ? Mat.Broom : H(qx, qz, 553) < .5 ? Mat.Heather : Mat.BlossomPink;
            bool bells = flower == Mat.BlossomPink;
            int n = 5 + (int)(H(qx, qz, 554) * 8), laid = 0;
            for (int k = 0; k < n * 2 && laid < n; k++)
            {
                int sx = x + (int)((H(qx, k, 555) - .5) * 11), sz = z + (int)((H(k, qz, 556) - .5) * 11);
                if (!Ground(sx, sz, 2, false, WildsClearSmall, 0) || !Bare(sx, sz, 14) || !Mat.IsGrass(w.Get(sx, top[sz * SX + sx] - 1, sz))) continue;
                // (its bells and the leaves at its foot go into the columns about it: those wild ground too)
                if (!Dry(sx + 1, sz) || !Dry(sx - 1, sz) || !Dry(sx, sz + 1) || !Dry(sx, sz - 1)) continue;
                Flora.Spire(w, sx, top[sz * SX + sx], sz, 3 + (int)(H(sx, sz, 557) * 3), 4 + (int)(H(sz, sx, 558) * 4), flower, bells);
                laid++;
            }
            if (laid == 0) continue;
            Take(x, z, 5);
            site.Wilds.Add((Wild.Spires, x, z, 6));
        }

        // ---- bracket fungi on the dead wood — the stumps, the logs, the drowned trees, the old hollow trunks — and toadstools
        // in the turf at its foot; now and then one on a living trunk deep in the woods
        // (none out over the water or any ground but the wild's)
        bool Dry(int x, int z) => w.OnLand(x, z) && water[z * SX + x] == 0 && kind[z * SX + x] is Relief.Low or Relief.Bank or Relief.Knoll or Relief.Crag;
        bool Shelves(int cx, int cz, int r, int y0, int y1, int most, int s)
        {
            var faces = new List<(int x, int y, int z, int nx, int nz, double o)>();
            for (int y = y0; y <= y1; y++)
                for (int z = cz - r; z <= cz + r; z++)
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        if (!w.InBounds(x, y, z) || !Mat.IsTrunk(w.Get(x, y, z))) continue;
                        foreach (var (ox, oz) in Four)
                            if (Dry(x + ox, z + oz) && Dry(x + ox * 2, z + oz * 2) && w.Get(x + ox, y, z + oz) == Mat.Air && w.Get(x + ox, y + 1, z + oz) == Mat.Air && w.Get(x + ox, y - 1, z + oz) == Mat.Air)
                                faces.Add((x, y, z, ox, oz, H(x + ox * 3, y, z + oz * 5 + s)));
                    }
            faces.Sort((a, b) => a.o.CompareTo(b.o));
            int laid = 0;
            var used = new List<(int x, int y, int z)>();
            foreach (var (x, y, z, ox, oz, _) in faces)
            {
                if (laid >= most) break;
                bool near = false;
                foreach (var u in used) if (Math.Abs(u.x - x) + Math.Abs(u.y - y) + Math.Abs(u.z - z) < 4) { near = true; break; }
                if (near || !Flora.BracketOn(w, x, y, z, ox, oz)) continue;
                used.Add((x, y, z));
                laid++;
            }
            return laid > 0;
        }
        void Toadstools(int cx, int cz, int r, int s)
        {
            for (int k = 0, n = 2 + (int)(H(cx, cz, s) * 4); k < n; k++)
            {
                double a = H(cx, k, s + 1) * Math.Tau, d = r * (.7 + H(k, cz, s + 2) * .6);
                int x = cx + (int)Math.Round(Math.Cos(a) * d), z = cz + (int)Math.Round(Math.Sin(a) * d);
                if (!Dry(x, z) || Brushes.Dist(x - site.KeepX, z - site.KeepZ) < site.KeepR + WildsClearSmall) continue;
                int g = top[z * SX + x];
                if (Mat.IsGrass(w.Get(x, g - 1, z)) && w.Get(x, g, z) == Mat.Air) w.SetRaw(x, g, z, Mat.Mushroom);
            }
        }
        foreach (var d in site.Details)
        {
            if (d.Kind is not (Detail.Stump or Detail.FallenTree or Detail.Drowned or Detail.Hollow) || H(d.X, d.Z, 561) > .55 * look.Bracket) continue;
            if (Brushes.Dist(d.X - site.KeepX, d.Z - site.KeepZ) < site.KeepR + WildsClearSmall || !Dry(d.X, d.Z)) continue;
            int g = top[d.Z * SX + d.X], most = 1 + (int)(H(d.X, d.Z, 562) * 3);
            bool laid = d.Kind switch
            {
                Detail.Stump => Shelves(d.X, d.Z, d.R + 2, g, g + 8, most, 563),
                Detail.Drowned => Shelves(d.X, d.Z, 3, g + 3, g + 22, most, 564),
                Detail.Hollow => Shelves(d.X, d.Z, 5, d.R - 12, d.R - 3, most, 565),
                _ => Shelves(d.X + (int)(d.DX * d.R * .5), d.Z + (int)(d.DZ * d.R * .5), Math.Max(4, d.R / 2), g - 2, g + 10, most, 566),
            };
            if (d.Kind is Detail.Stump or Detail.FallenTree) Toadstools(d.X, d.Z, d.Kind == Detail.Stump ? d.R + 2 : 6, 567);
            if (laid) site.Wilds.Add((Wild.Bracket, d.X, d.Z, d.Kind == Detail.FallenTree ? d.R : 6));
        }
        foreach (var (x, z, qx, qz) in Grid(28, 571))
        {
            if (H(qx, qz, 572) > .05 * look.Bracket || Wooded(x, z) < .45 || !Ground(x, z, 8, false, WildsClearSmall, 0)) continue;
            int g = top[z * SX + x];
            // a trunk standing about here at a man's height
            int tx = -1, tz = -1;
            for (int zz = z - 6; zz <= z + 6 && tx < 0; zz += 2)
                for (int xx = x - 6; xx <= x + 6; xx += 2)
                    if (Dry(xx, zz) && w.Get(xx, g + 8, zz) == Mat.Trunk) { tx = xx; tz = zz; break; }
            if (tx >= 0 && Shelves(tx, tz, 4, g + 3, g + 14, 2, 573)) site.Wilds.Add((Wild.Bracket, tx, tz, 6));
        }

        // ---- on the banks of the river and the lakes: grey logs the water left, and pebbles in the bank
        foreach (var (x, z, qx, qz) in Grid(48, 581))
        {
            if (H(qx, qz, 582) > .4 * look.Drift || !Ground(x, z, 6, false, WildsClearBig, 16)) continue;
            var (wx, wz, wd) = ToWater(x, z, 14);
            if (wd < 0) continue;
            double ax = -wz, az = wx;
            if (H(qx, qz, 583) < .55)
            {
                // a log along the shore, all of it on the dry bank with nothing on it
                int len = 8 + (int)(H(qx, qz, 584) * 12);
                bool ok = Free(x, z, 6) && Free(x + (int)(ax * len), z + (int)(az * len), 6);
                // (every column it will lie on, as Flora.Driftwood lays it: two wide)
                for (double s = 0; s <= len && ok; s += .5)
                    for (double q = 0; q <= 1 && ok; q += .5)
                    {
                        int lx = (int)Math.Floor(x + .5 + ax * s - az * q), lz = (int)Math.Floor(z + .5 + az * s + ax * q);
                        ok = Ground(lx, lz, 2, false, WildsClearBig, 0) && Bare(lx, lz, 6) && Math.Abs(Height(lx, lz) - top[z * SX + x]) < 5;
                    }
                if (!ok) continue;
                Flora.Driftwood(w, top, x, z, ax, az, len);
                for (int s = 0; s <= len; s += 8) Take(x + (int)(ax * s), z + (int)(az * s), 4);
                site.Wilds.Add((Wild.Driftwood, x + (int)(ax * len / 2), z + (int)(az * len / 2), len / 2 + 3));
                continue;
            }
            // pebbles: a patch of the bank by the water gone to stones
            int r = 3 + (int)(H(qx, qz, 585) * 4), stones = 0;
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int px = x + dx, pz = z + dz;
                    if (dx * dx + dz * dz > r * r || H(px, pz, 586) > .42 || !Ground(px, pz, 2, false, WildsClearBig, 0)) continue;
                    int g = top[pz * SX + px];
                    byte m = w.Get(px, g - 1, pz);
                    if (!(Mat.IsGrass(m) || m is Mat.Soil or Mat.Sand) || w.Get(px, g, pz) != Mat.Air && !Mat.IsSprig(w.Get(px, g, pz))) continue;
                    w.SetRaw(px, g - 1, pz, Mat.Rock);
                    if (Mat.IsSprig(w.Get(px, g, pz))) w.SetRaw(px, g, pz, Mat.Air);
                    stones++;
                }
            if (stones > 0) site.Wilds.Add((Wild.Pebbles, x, z, r));
        }

        // ---- the beavers' lodge on the dry shore of a lake or a pond, deep water by it (the shores looked along in an order
        // the seed gives)
        var shores = new List<(int x, int z)>();
        if (look.Lodges > 0)
            for (int z = 12; z < SZ - 12; z += 3)
                for (int x = 12; x < SX - 12; x += 3)
                    if (w.OnLand(x, z) && water[z * SX + x] == 0 && kind[z * SX + x] is Relief.Low or Relief.Bank && Shore(water, kind, SX, x, z) == 1) shores.Add((x, z));
        shores.Sort((a, b) => H(a.x, a.z, 591).CompareTo(H(b.x, b.z, 591)));
        int found = 0;
        foreach (var (x, z) in shores)
        {
            if (found >= look.Lodges) break;
            int k = x * 7 + z;
            if (!Ground(x, z, 10, false, WildsClearBig, 24)) continue;
            var (wx, wz, wd) = ToWater(x, z, 6);
            if (wd < 0) continue;
            int dx = x + (int)Math.Round(wx * (wd + 3)), dz = z + (int)Math.Round(wz * (wd + 3));
            if (!w.OnLand(dx, dz) || water[dz * SX + dx] - top[dz * SX + dx] < 2) continue;
            double r = 8 + H(k, 3, 591) * 3, h = 6 + H(k, 4, 591) * 3;
            int cx = x - (int)Math.Round(wx * 2), cz = z - (int)Math.Round(wz * 2), n = (int)r + 2;
            // (in the open, not under the crowns of the trees on the shore: no trunk within a metre of it)
            if (!Free(cx, cz, n + 2) || Wooded(cx, cz) > .08) continue;
            bool trees = false;
            for (int zz = cz - n - 8; zz <= cz + n + 8 && !trees; zz += 2)
                for (int xx = cx - n - 8; xx <= cx + n + 8 && !trees; xx += 2)
                    if (w.OnLand(xx, zz)) for (int y = Height(xx, zz); y < Height(xx, zz) + 24 && !trees; y += 3) trees = Mat.IsTrunk(w.Get(xx, y, zz));
            if (trees) continue;
            bool ok = true;
            int lo = int.MaxValue, hi = int.MinValue;
            for (int zz = cz - n; zz <= cz + n && ok; zz++)
                for (int xx = cx - n; xx <= cx + n && ok; xx++)
                {
                    if (!w.OnLand(xx, zz) || water[zz * SX + xx] > 0) continue;
                    ok = Ground(xx, zz, 0, false, WildsClearBig, 0) && Bare(xx, zz, 24);
                    lo = Math.Min(lo, top[zz * SX + xx]); hi = Math.Max(hi, top[zz * SX + xx]);
                }
            if (!ok || hi - lo > 4) continue;
            Flora.Lodge(w, top, water, cx, cz, r, h);
            Take(cx, cz, n + 1);
            site.Wilds.Add((Wild.Lodge, cx, cz, n));
            found++;
        }

        // ---- in the mountains, snow still lying under the snow line in the hollows that face north (the ground falling
        // toward −z, lower than either side across it)
        var blocks = new HashSet<int>();
        int BX = (SX + 31) >> 5;
        if (look.Snow && p.Massifs && p.Kind == ValleyKind.Mountain)
        {
            int line = new Massifs(seed, w.SY).SnowLine;
            for (int z = 8; z < SZ - 8; z++)
                for (int x = 8; x < SX - 8; x++)
                {
                    int i = z * SX + x, h = top[i];
                    if (h < line - 34 || h >= line - 6 || Height(x, z + 6) - Height(x, z - 6) < 3 || h > (Height(x - 6, z) + Height(x + 6, z)) / 2 - 1) continue;
                    if (nSnow.Sample(x * .05, z * .05) < .45 || !Ground(x, z, 2, true, WildsClearSmall, 0)) continue;
                    byte m = w.Get(x, h - 1, z), at = w.Get(x, h, z);
                    if (!(Mat.IsGrass(m) || m is Mat.Moss or Mat.Rock or Mat.Scree or Mat.Soil) || at != Mat.Air && !Mat.IsSprig(at)) continue;
                    w.SetRaw(x, h - 1, z, Mat.Snow);
                    if (at != Mat.Air) w.SetRaw(x, h, z, Mat.Air);
                    blocks.Add((z >> 5) * BX + (x >> 5));
                }
            foreach (int b in blocks) site.Wilds.Add((Wild.Snow, (b % BX) * 32 + 16, (b / BX) * 32 + 16, 23));
            blocks.Clear();
        }

        // ---- on the wet ground of the bog and the marshes: cranberries on the moss and the peat, and cotton grass
        if (look.Cranberry > 0)
        {
            for (int z = 0; z < SZ; z++)
                for (int x = 0; x < SX; x++)
                {
                    int i = z * SX + x;
                    if (moist[i] < .62 || water[i] > 0 || kind[i] is not (Relief.Low or Relief.Bank)) continue;
                    double patch = nPatch.Sample(x * .03 + 17, z * .03 + 5), roll = H(x, z, 592);
                    bool berry = patch > .7 && roll < .1 * look.Cranberry, cotton = !berry && patch < .26 && roll < .035 * look.Cranberry;
                    if (!berry && !cotton || !Ground(x, z, 2, false, WildsClearSmall, 0)) continue;
                    int g = top[i];
                    byte under = w.Get(x, g - 1, z);
                    // (the cranberries on the moss and the peat only: a low mat of their small leaves, a berry here and there
                    // in it; a red voxel alone on the ground is too like blood)
                    if (!(under is Mat.Peat or Mat.Moss || cotton && Mat.IsGrass(under)) || w.Get(x, g, z) != Mat.Air) continue;
                    w.SetRaw(x, g, z, berry ? H(z, x, 593) < .16 ? Mat.BerryRed : Mat.Foliage : Mat.FlowerWhite);
                    blocks.Add((z >> 5) * BX + (x >> 5));
                }
            foreach (int b in blocks) site.Wilds.Add((Wild.Cranberry, (b % BX) * 32 + 16, (b / BX) * 32 + 16, 23));
        }
    }

    /// <summary>How much of nature's dressing of the open a kind of valley has, against the first valley's.</summary>
    readonly struct WildLook
    {
        public readonly double Erratic, Fields, Outcrop, Thicket, Rose, Hawthorn, Broom, Heather, HeatherWet, Spires, Bracket, Drift, Cranberry;
        public readonly int Lodges;
        public readonly bool Snow;
        public WildLook(double erratic = 1, double fields = 0, double outcrop = .7, double thicket = 1, double rose = 1, double hawthorn = .6, double broom = .6,
            double heather = .5, double heatherWet = .4, double spires = 1, double bracket = 1, double drift = 1, double cranberry = 0, int lodges = 1, bool snow = false)
        {
            Erratic = erratic; Fields = fields; Outcrop = outcrop; Thicket = thicket; Rose = rose; Hawthorn = hawthorn; Broom = broom; Heather = heather;
            HeatherWet = heatherWet; Spires = spires; Bracket = bracket; Drift = drift; Cranberry = cranberry; Lodges = lodges; Snow = snow;
        }
    }

    static WildLook WildLooks(ValleyKind k) => k switch
    {
        // the forest: the dead wood's fungi most, foxgloves at its edges, roses and hawthorns about its clearings
        ValleyKind.Forest => new WildLook(erratic: .6, outcrop: .3, thicket: .8, hawthorn: .8, broom: .3, heather: .3, spires: .9, bracket: 2.4),
        // the river's valley: logs and pebbles on its banks, lodges on its lakes, loosestrife by the water, wet meadows
        ValleyKind.Marsh => new WildLook(erratic: .3, outcrop: 0, thicket: .7, hawthorn: .5, broom: .2, heather: .4, spires: 1.6, bracket: 1.2, drift: 1.8, lodges: 2, cranberry: 1),
        // the mountains: fields of boulders, ledges on every slope, heather and broom, snow in the hollows
        ValleyKind.Mountain => new WildLook(erratic: 2.2, fields: .45, outcrop: 2.2, thicket: .5, rose: .4, hawthorn: .3, broom: 1.2, heather: 2.4, heatherWet: .55,
            spires: .6, bracket: .5, drift: .5, lodges: 0, snow: true),
        // the ore hills: boulders and ledges, broom and heather on their dry ground
        ValleyKind.Ore => new WildLook(erratic: 1.8, fields: .35, outcrop: 1.8, thicket: .6, rose: .5, hawthorn: .3, broom: 1.4, heather: 1.2, spires: .6, bracket: .7, drift: .6, lodges: 0),
        // the holy valley: hawthorn white with blossom, tall flowers in its meadows
        ValleyKind.Sacred => new WildLook(erratic: .8, outcrop: .6, thicket: 1.2, rose: .6, hawthorn: 2, broom: .4, heather: .6, spires: 1.4, drift: .8),
        // the horde's bog: cranberries and cotton grass, heather on what is drier, fungi on its dead wood, hardly a stone
        ValleyKind.Bog => new WildLook(erratic: .15, outcrop: 0, thicket: .35, rose: .6, hawthorn: .3, broom: 0, heather: 1.2, heatherWet: .66, spires: .6,
            bracket: 2, drift: .5, cranberry: 2.5),
        // the first valley: a little of everything (an argument given, or the struct's own empty constructor is taken)
        _ => new WildLook(erratic: 1),
    };
}
