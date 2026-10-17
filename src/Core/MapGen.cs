using System;
using System.Threading.Tasks;

namespace VoxelTower.Core;

public sealed class MapParams
{
    /// <summary>Voxels; 8 make a metre. The default valley is 128 × 24 × 128 m.</summary>
    public int SizeX = 1024, SizeY = 192, SizeZ = 1024;
    public uint Seed = 7;
    /// <summary>Knobs for the look against the triangle count.</summary>
    public double TreeDensity = .7, TuftChance = .015, Roughness = 2.5;
}

/// <summary>
/// The valley the game is played in: rolling lowland with ridges, forests, rocky knolls and boulders; a river winding
/// through it with a few fords; an old road from the west; and a cliff-edged plateau with two ramps, ponds and a clearing
/// round the citadel. Everything in it can be dug, chopped or quarried, down to the bedrock, and water runs into what is dug
/// beside it.
/// </summary>
public static class MapGen
{
    // terrain kinds per column
    const byte Low = 0, Plateau = 1, Rim = 2, Ramp = 3, Road = 4, Knoll = 5, River = 6, Pond = 7, Bank = 8;

    /// <summary>The river's surface: water stands below this height.</summary>
    public const int RiverLevel = 31;

    public static Site Generate(VoxelWorld w, MapParams p)
    {
        int SX = w.SX, SZ = w.SZ;
        uint seed = p.Seed;
        var site = new Site { HTop = new int[SX * SZ], SpawnStrip = 24, KeepR = 24, KeepX = (int)(SX * .8), KeepZ = SZ / 2 };
        var nHill = new ValueNoise(seed * 11 + 1); var nHill2 = new ValueNoise(seed * 13 + 2); var nFine = new ValueNoise(seed * 17 + 3);
        var nEdge = new ValueNoise(seed * 19 + 4); var nKnoll = new ValueNoise(seed * 23 + 5); var nSoil = new ValueNoise(seed * 29 + 6);
        var nForest = new ValueNoise(seed * 31 + 7); var nRidge = new ValueNoise(seed * 37 + 8); var nRiver = new ValueNoise(seed * 41 + 9);

        const int baseH = 40, rise = 44, rampHalf = 26, rampLen = 150;
        double EdgeX(double z) => SX * .58 + (nEdge.Sample(z * .008, 7.7) - .5) * 140 + (nEdge.Sample(z * .03, 3.1) - .5) * 26;
        int rampN = (int)(SZ * .27), rampS = (int)(SZ * .74);
        double roadEnd = EdgeX(rampS) - rampLen;
        double RoadZ(double x)
        {
            double t = Site.Smooth01(x / roadEnd);
            return SZ * .56 + (rampS - SZ * .56) * t + Math.Sin(x * .018) * 18 * (1 - t);
        }

        // the river runs north to south across the lowland, between the horde's edge and the plateau
        double RiverX(double z) => SX * .3 + Math.Sin(z * .0062 + 1.3) * 64 + (nRiver.Sample(z * .009, 2.2) - .5) * 70;
        double RiverHalf(double z) => 15 + nRiver.Sample(z * .02, 9.4) * 12;
        // fords: where the road crosses, and one more to the north
        double roadCross = 0;
        for (int x = 0; x < SX; x++) if (Math.Abs(x - RiverX(RoadZ(x))) < 2) { roadCross = RoadZ(x); break; }
        double[] fords = { roadCross, SZ * .22 };

        // the plateau rolls gently away from the citadel and lies flat round it
        double Plat(int x, int z)
        {
            double dk = Brushes.Dist(x - site.KeepX, z - site.KeepZ);
            double roll = Site.Smooth01((dk - 190) / 140) * ((nHill.Sample(x * .013 + 30, z * .013) - .5) * 16);
            return baseH + rise + (nHill2.Sample(x * .02 + 40, z * .02 + 9) - .5) * 3 + roll;
        }

        // two big ponds on the plateau, north and south of the citadel a good way off (40 m), 15–20 m across and up to
        // 2.5 m deep. Each lies level, its surface flush with the lowest ground round its shore, so it holds still
        // until someone digs into it.
        const int shore = 5;
        var ponds = new (int x, int z, int r, int level)[2];
        for (int n = 0; n < ponds.Length; n++)
        {
            double a = (n == 0 ? 1.62 : 4.66) + (Hash.H3((int)seed, n, 3) - .5) * .4, dist = 300 + Hash.H3((int)seed, n, 4) * 30;
            int pr = 60 + (int)(Hash.H3((int)seed, n, 5) * 24), level = int.MaxValue;
            int px = site.KeepX + (int)(Math.Cos(a) * dist);
            int pz = Math.Clamp(site.KeepZ + (int)(Math.Sin(a) * dist), pr + shore + 16, SZ - pr - shore - 16);
            // the whole shore on the plateau, well back from the cliff wherever the cliff wanders
            double edge = EdgeX(pz) + 70;
            for (int z = pz - pr - shore; z <= pz + pr + shore; z++) edge = Math.Max(edge, EdgeX(z) + pr + shore + 8);
            px = Math.Clamp(px, (int)Math.Ceiling(edge), SX - pr - shore - 16);
            for (int z = pz - pr - shore; z <= pz + pr + shore; z++)
                for (int x = px - pr - shore; x <= px + pr + shore; x++)
                {
                    double d = Brushes.Dist(x - px, z - pz);
                    if (d >= pr && d < pr + shore) level = Math.Min(level, (int)Math.Round(Plat(x, z)) - 1);
                }
            ponds[n] = (px, pz, pr, level);
            site.Ponds.Add((px, pz, pr, level));
        }

        var kind = new byte[SX * SZ];
        var soil = new byte[SX * SZ];
        var water = new short[SX * SZ];
        Parallel.For(0, SZ, z =>
        {
            double edge = EdgeX(z), rx = RiverX(z), rh = RiverHalf(z);
            bool ramp = Math.Abs(z - rampN) < rampHalf || Math.Abs(z - rampS) < rampHalf;
            double ford = 1e9;
            foreach (double f in fords) ford = Math.Min(ford, Math.Abs(z - f));
            for (int x = 0; x < SX; x++)
            {
                // rolling lowland with long ridges
                double ridge = 1 - Math.Abs(nRidge.Sample(x * .0065, z * .0065) * 2 - 1);
                double low = baseH + (nHill.Sample(x * .010, z * .010) - .5) * 34 + (nHill2.Sample(x * .033, z * .033) - .5) * 11
                    + Math.Pow(ridge, 3) * 24 + (nFine.Sample(x * .11, z * .11) - .5) * p.Roughness;
                low = Math.Min(low, baseH + 30);
                double plat = Plat(x, z);
                double h = low; byte k = Low;
                if (x >= edge) { h = plat; k = x - edge < 5 && !ramp ? Rim : Plateau; }
                else if (ramp && x >= edge - rampLen)
                {
                    double r = low + (plat - low) * Site.Smooth01((x - (edge - rampLen)) / rampLen);
                    if (r > h) { h = r; k = Ramp; }
                }
                else if (x < roadEnd + 20 && Math.Abs(z - RoadZ(x)) < 6) k = Road;
                else
                {
                    double kn = nKnoll.Sample(x * .018, z * .018);
                    if (kn > .7 && x > site.SpawnStrip + 16) { h = low + (kn - .7) * 190; k = Knoll; }
                }
                int i = z * SX + x;
                short wl = 0;
                if (x < edge - 8)
                {
                    // the river valley: banks that slope down to the water, a bed that drops away from them
                    double d = Math.Abs(x - rx);
                    if (d < rh)
                    {
                        double t = d / rh;
                        double depth = ford < 14 ? 2 : 3 + (1 - t * t) * 13;
                        h = RiverLevel - depth;
                        k = River;
                        wl = RiverLevel;
                    }
                    else if (d < rh + 70)
                    {
                        double bank = RiverLevel + 2 + (d - rh) * .42;
                        if (h > bank) { h = bank; if (k == Low || k == Knoll) k = Bank; }
                        // and never lower than the river near it: a low meadow beside the water would stand under it
                        h = Math.Max(h, RiverLevel + 1 - Math.Max(0, d - rh - 40) * .25);
                    }
                }
                foreach (var (px, pz, pr, level) in ponds)
                {
                    double d = Brushes.Dist(x - px, z - pz);
                    if (d >= pr + shore || k is not (Plateau or Rim)) continue;
                    if (d < pr)
                    {
                        double t = d / pr;
                        h = level - 1 - (1 - t * t) * 19;
                        k = Pond; wl = (short)level;
                    }
                    // the shore climbs from the water's edge to the plateau
                    else h = level + (h - level) * Site.Smooth01((d - pr) / shore);
                }
                kind[i] = k;
                water[i] = wl;
                site.HTop[i] = (int)Math.Round(h);
                soil[i] = (byte)(k switch { Knoll or Rim => 0, Plateau or Pond => 7, River => 4, _ => 9 + (int)(nSoil.Sample(x * .02, z * .02) * 6) });
            }
        });

        Parallel.For(0, w.CZ, cz =>
        {
            for (int z = cz * VoxelWorld.CS; z < (cz + 1) * VoxelWorld.CS && z < SZ; z++)
                for (int x = 0; x < SX; x++)
                {
                    int i = z * SX + x, h = site.HTop[i], s = soil[i];
                    byte k = kind[i];
                    w.FillColumnRaw(x, z, 0, 3, Mat.Bed);
                    w.FillColumnRaw(x, z, 3, h - s, Mat.Rock);
                    w.FillColumnRaw(x, z, h - s, h - 1, Mat.Soil);
                    byte top = k switch
                    {
                        Rim => Mat.Moss,
                        Ramp or Road => Mat.Path,
                        Knoll => Hash.H3(x, z, 41) < .35 ? Mat.Moss : Mat.Rock,
                        River or Pond => Mat.Soil,
                        _ => Mat.Grass,
                    };
                    w.FillColumnRaw(x, z, h - 1, h, top);
                    if (water[i] > h) w.FillColumnRaw(x, z, h, water[i], Mat.Water);
                    else if (top == Mat.Grass && Hash.H3(x, z, 77) < p.TuftChance)
                    {
                        w.FillColumnRaw(x, z, h, h + 1, Mat.Tuft);
                        if (Hash.H3(x, z, 78) < .35) w.FillColumnRaw(x, z, h + 1, h + 2, Mat.Tuft);
                    }
                }
        });

        bool NearKeep(int x, int z, int margin) => Brushes.Dist(x - site.KeepX, z - site.KeepZ) < site.KeepR + margin;

        // the named places, before the rocks and trees: mines worth holding, and ruins of what stood here before
        (int x, int z) Free(int x, int z, int r)
        {
            bool Ok(int cx, int cz)
            {
                if (cx < r + 16 || cz < r + 16 || cx >= SX - r - 16 || cz >= SZ - r - 16 || NearKeep(cx, cz, 100 + r)) return false;
                foreach (var (px, pz, pr, _) in ponds) if (Brushes.Dist(cx - px, cz - pz) < pr + r + 16) return false;
                foreach (var l in site.Landmarks) if (Brushes.Dist(cx - l.X, cz - l.Z) < l.R + r + 12) return false;
                for (int dz = -r; dz <= r; dz += 4)
                    for (int dx = -r; dx <= r; dx += 4)
                        if (dx * dx + dz * dz <= r * r && kind[(cz + dz) * SX + cx + dx] is River or Pond or Road or Ramp or Rim) return false;
                return true;
            }
            for (int ring = 0; ring <= 200; ring += 12)
                for (int a = 0; a < 360; a += ring == 0 ? 360 : 30)
                {
                    int cx = x + (int)(Math.Cos(a * Math.PI / 180) * ring), cz = z + (int)(Math.Sin(a * Math.PI / 180) * ring);
                    if (Ok(cx, cz)) return (cx, cz);
                }
            return (x, z);
        }
        var copper = Free(site.KeepX + 150, site.KeepZ + 64, 30);
        Landmarks.Mine(w, site, copper.x, copper.z, "Медный рудник", 3, false, seed + 1);
        var houses = Free(copper.x - 52, copper.z + 46, 34);
        Landmarks.Houses(w, site, houses.x, houses.z, "Брошенные дома рудокопов", seed + 2);
        {
            // the silver lies in the lowland between the river and the cliff, on the horde's way
            int sz = (int)(SZ * .36), sx = (int)((RiverX(sz) + RiverHalf(sz) + EdgeX(sz)) / 2);
            var silver = Free(sx, sz, 30);
            Landmarks.Mine(w, site, silver.x, silver.z, "Серебряная жила", 6, true, seed + 3);
        }
        var gold = Free((int)(SX * .12), (int)(SZ * .17), 30);
        Landmarks.Mine(w, site, gold.x, gold.z, "Золотой прииск", 10, false, seed + 4);
        var outpost = Free(gold.x + 64, gold.z + 56, 32);
        Landmarks.Outpost(w, site, outpost.x, outpost.z, "Старая застава", seed + 5);
        {
            double f = fords[1];
            var tower = Free((int)(RiverX(f) + RiverHalf(f) + 36), (int)f + 24, 20);
            Landmarks.Watchtower(w, site, tower.x, tower.z, "Руины сторожевой башни", seed + 6);
        }
        var fallen = Free((int)EdgeX(rampN) + 34, rampN + 64, 20);
        Landmarks.Watchtower(w, site, fallen.x, fallen.z, "Обвалившаяся башня", seed + 7, .45);

        bool Clear(int x, int z)
        {
            if (x < site.SpawnStrip + 8 || x >= SX - 8 || z < 8 || z >= SZ - 8) return false;
            byte k = kind[z * SX + x];
            return k is Low or Plateau or Bank && !NearKeep(x, z, 48) && !Landmarks.Taken(site, x, z, 6);
        }

        // boulders, more of them on the rocky ground
        for (int gz = 0; gz < SZ; gz += 56)
            for (int gx = 0; gx < SX; gx += 56)
            {
                int x = gx + (int)(Hash.H3(gx, gz, 101) * 56), z = gz + (int)(Hash.H3(gx, gz, 102) * 56);
                if (x >= SX || z >= SZ || !Clear(x, z)) continue;
                double chance = nKnoll.Sample(x * .018, z * .018) > .6 ? .55 : .2;
                if (Hash.H3(gx, gz, 103) > chance) continue;
                Flora.Boulder(w, x, z, 5 + (int)(Hash.H3(gx, gz, 104) * 9), site.HTop[z * SX + x]);
            }

        // forests in patches, a few lone trees between them
        for (int gz = 0; gz < SZ; gz += 18)
            for (int gx = 0; gx < SX; gx += 18)
            {
                int x = gx + (int)(Hash.H3(gx, gz, 201) * 14), z = gz + (int)(Hash.H3(gx, gz, 202) * 14);
                if (x >= SX || z >= SZ || !Clear(x, z) || w.Get(x, w.SurfAt(x, z) - 1, z) != Mat.Grass) continue;
                double f = nForest.Sample(x * .006, z * .006);
                double density = f > .58 ? .7 : f > .48 ? .26 : .035;
                bool plateau = kind[z * SX + x] == Plateau;
                // the plateau is where the player builds: a clearing round the citadel, thin woods beyond
                if (plateau) density = NearKeep(x, z, 160) ? 0 : density * .3;
                if (Hash.H3(gx, gz, 203) > density * p.TreeDensity) continue;
                double r = Hash.H3(gx, gz, 204), size = Hash.H3(gx, gz, 205);
                if (plateau ? r < .5 : r >= .42 && r < .66) Flora.Cypress(w, x, z, 30 + (int)(size * 18), grain: 1, grown: true);
                else if (r < .42) Flora.Spruce(w, x, z, 40 + (int)(size * 28));
                else if (r < .86) Flora.Oak(w, x, z, 44 + (int)(size * 18));
                else if (r < .94) Flora.Bush(w, x, z, 3 + (int)(size * 3));
                else Flora.DeadTree(w, x, z, grown: true);
            }

        site.WaterTop = water;
        site.KeepBase = WorldGen.Keep(w, site);
        int gx2 = site.KeepX - site.KeepR - 7;
        foreach (int dz in new[] { -10, 10 }) WorldGen.Brazier(w, site, gx2, w.SurfAt(gx2, site.KeepZ + dz), site.KeepZ + dz, post: 4);
        site.Torches.Add((site.KeepX - site.KeepR - 3, site.KeepBase + 22, site.KeepZ));
        return site;
    }
}
