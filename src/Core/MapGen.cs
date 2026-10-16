using System;
using System.Threading.Tasks;

namespace VoxelTower.Core;

public sealed class MapParams
{
    /// <summary>Voxels; 16 make a metre. The default valley is 64 × 12 × 64 m.</summary>
    public int SizeX = 1024, SizeY = 192, SizeZ = 1024;
    public uint Seed = 7;
    /// <summary>Knobs for the look against the triangle count.</summary>
    public double TreeDensity = .8, TuftChance = .015, Roughness = 2.5;
}

/// <summary>
/// The valley the game is played in: rolling lowland with forests, rocky knolls and boulders, an old road from the
/// west, and a cliff-edged plateau with two ramps where the citadel stands. Everything in it can be dug, chopped
/// or quarried, down to the bedrock.
/// </summary>
public static class MapGen
{
    // terrain kinds per column
    const byte Low = 0, Plateau = 1, Rim = 2, Ramp = 3, Road = 4, Knoll = 5;

    public static Site Generate(VoxelWorld w, MapParams p)
    {
        int SX = w.SX, SZ = w.SZ;
        uint seed = p.Seed;
        var site = new Site { HTop = new int[SX * SZ], SpawnStrip = 24, KeepR = 24, KeepX = (int)(SX * .8), KeepZ = SZ / 2 };
        var nHill = new ValueNoise(seed * 11 + 1); var nHill2 = new ValueNoise(seed * 13 + 2); var nFine = new ValueNoise(seed * 17 + 3);
        var nEdge = new ValueNoise(seed * 19 + 4); var nKnoll = new ValueNoise(seed * 23 + 5); var nSoil = new ValueNoise(seed * 29 + 6);
        var nForest = new ValueNoise(seed * 31 + 7);

        const int baseH = 40, rise = 44, rampHalf = 26, rampLen = 150;
        double EdgeX(double z) => SX * .58 + (nEdge.Sample(z * .008, 7.7) - .5) * 140 + (nEdge.Sample(z * .03, 3.1) - .5) * 26;
        int rampN = (int)(SZ * .27), rampS = (int)(SZ * .74);
        double roadEnd = EdgeX(rampS) - rampLen;
        double RoadZ(double x)
        {
            double t = Site.Smooth01(x / roadEnd);
            return SZ * .56 + (rampS - SZ * .56) * t + Math.Sin(x * .018) * 18 * (1 - t);
        }

        var kind = new byte[SX * SZ];
        var soil = new byte[SX * SZ];
        Parallel.For(0, SZ, z =>
        {
            double edge = EdgeX(z);
            bool ramp = Math.Abs(z - rampN) < rampHalf || Math.Abs(z - rampS) < rampHalf;
            for (int x = 0; x < SX; x++)
            {
                double low = baseH + (nHill.Sample(x * .010, z * .010) - .5) * 26 + (nHill2.Sample(x * .033, z * .033) - .5) * 9 + (nFine.Sample(x * .11, z * .11) - .5) * p.Roughness;
                double plat = baseH + rise + (nHill2.Sample(x * .02 + 40, z * .02 + 9) - .5) * 3;
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
                    if (kn > .7 && x > site.SpawnStrip + 16) { h = low + (kn - .7) * 140; k = Knoll; }
                }
                int i = z * SX + x;
                kind[i] = k;
                site.HTop[i] = (int)Math.Round(h);
                soil[i] = (byte)(k switch { Knoll or Rim => 0, Plateau => 7, _ => 9 + (int)(nSoil.Sample(x * .02, z * .02) * 6) });
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
                        _ => Mat.Grass,
                    };
                    w.FillColumnRaw(x, z, h - 1, h, top);
                    if (top == Mat.Grass && Hash.H3(x, z, 77) < p.TuftChance)
                    {
                        w.FillColumnRaw(x, z, h, h + 1, Mat.Tuft);
                        if (Hash.H3(x, z, 78) < .35) w.FillColumnRaw(x, z, h + 1, h + 2, Mat.Tuft);
                    }
                }
        });

        bool NearKeep(int x, int z, int margin) => Brushes.Dist(x - site.KeepX, z - site.KeepZ) < site.KeepR + margin;
        bool Clear(int x, int z)
        {
            if (x < site.SpawnStrip + 8 || x >= SX - 8 || z < 8 || z >= SZ - 8) return false;
            byte k = kind[z * SX + x];
            return k is Low or Plateau && !NearKeep(x, z, 48);
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
                if (plateau ? r < .5 : r >= .42 && r < .66) Flora.Cypress(w, x, z, 22 + (int)(size * 14), grain: 1);
                else if (r < .42) Flora.Spruce(w, x, z, 30 + (int)(size * 22));
                else if (r < .86) Flora.Oak(w, x, z, 34 + (int)(size * 14));
                else if (r < .94) Flora.Bush(w, x, z, 3 + (int)(size * 3));
                else Flora.DeadTree(w, x, z);
            }

        site.KeepBase = WorldGen.Keep(w, site);
        int gx2 = site.KeepX - site.KeepR - 7;
        foreach (int dz in new[] { -10, 10 }) WorldGen.Brazier(w, site, gx2, w.SurfAt(gx2, site.KeepZ + dz), site.KeepZ + dz);
        site.Torches.Add((site.KeepX - site.KeepR - 3, site.KeepBase + 22, site.KeepZ));
        return site;
    }
}
