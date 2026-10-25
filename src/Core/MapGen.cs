using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoxelTower.Core;

public sealed class MapParams
{
    /// <summary>Voxels; 8 make a metre. The default valley is 128 × 24 × 128 m.</summary>
    public int SizeX = 1024, SizeY = 192, SizeZ = 1024;
    /// <summary>The game's valley's box across: the hexagon in it 166 m between its straight sides, 191 m point to point.</summary>
    public const int GameSize = 1536;
    public uint Seed = 7;
    /// <summary>Knobs for the look against the triangle count.</summary>
    public double TreeDensity = .7, TuftChance = .015, Roughness = 2.5;
    /// <summary>What kind of valley it is: its ground and its places (<see cref="Valleys"/>).</summary>
    public ValleyKind Kind = ValleyKind.First;
    /// <summary>
    /// The first valley with every place it has always had — the mines, the barrow — as long as there is no map of valleys
    /// to find them in; off, it has only the ruins and the bridges, and the rest lies in valleys of other kinds.
    /// </summary>
    public bool Classic = true;
    /// <summary>
    /// The land a hexagon (the cell of the map to come); off, the whole box as before. The game makes its valleys
    /// hexagons; the core's default is the old box, on which the bench's checks of the mechanics were set out.
    /// </summary>
    public bool Hexagon;
    /// <summary>
    /// The six sides as the map of valleys gives them (seeds, river and road crossings), so the valley meets its
    /// neighbours; null for a valley made by itself, whose sides are made from its own seed.
    /// </summary>
    public HexEdge[] Edges;
    /// <summary>The side of the hexagon the horde comes in by (<see cref="Hex.West"/> and the others).</summary>
    public int EntrySide = Hex.West;
}

/// <summary>
/// The valley the game is played in: rolling lowland with ridges, forests, rocky knolls and boulders; a river winding
/// through it with a few fords; an old road from the west; and a cliff-edged plateau with two ramps, ponds and a clearing
/// round the citadel. Everything in it can be dug, chopped or quarried, down to the bedrock, and water runs into what is dug
/// beside it. The land is a hexagon pointed north and south (<see cref="Hex"/>): the river comes in over its north-western
/// side and goes out over its south-western one, the road and the horde come in over its straight western side, and the
/// citadel stands on the plateau towards its eastern side. The kind of valley (<see cref="Valleys"/>) sets how thick the
/// woods are, how rocky and how wet the ground, and which named places it has.
/// </summary>
public static partial class MapGen
{
    // terrain kinds per column
    const byte Low = 0, Plateau = 1, Rim = 2, Ramp = 3, Road = 4, Knoll = 5, River = 6, Pond = 7, Bank = 8;

    /// <summary>The river's surface: water stands below this height.</summary>
    public const int RiverLevel = 31;

    /// <summary>How far from a point in a pond its water reaches along a direction, in voxels: the first dry column.</summary>
    public static int Shore(VoxelWorld w, Site s, int px, int pz, double dx, double dz)
    {
        double len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 1e-9 || s.WaterTop == null) return 0;
        dx /= len; dz /= len;
        for (int r = 0; r < 400; r++)
        {
            int x = px + (int)Math.Round(dx * r), z = pz + (int)Math.Round(dz * r);
            if (x < 0 || z < 0 || x >= w.SX || z >= w.SZ || s.WaterTop[z * w.SX + x] == 0) return r;
        }
        return 400;
    }

    public static Site Generate(VoxelWorld w, MapParams p)
    {
        // a hexagon is made by itself, procedurally (MapGen.Relief.cs); the box keeps the valley the bench was set out on
        if (p.Hexagon) return GenerateHex(w, p);
        int SX = w.SX, SZ = w.SZ;
        uint seed = p.Seed;
        var site = new Site { HTop = new int[SX * SZ], SpawnStrip = 24, KeepR = 24, KeepX = (int)(SX * .8), KeepZ = SZ / 2, Design = new VoxelWorld(SX, w.SY, SZ), SX = SX, SZ = SZ };
        site.Kind = p.Kind; site.Classic = p.Classic;
        var ground = Valleys.GroundOf(p.Kind);
        var places = Valleys.PlacesOf(p.Kind, p.Classic);
        // the land a hexagon: nothing is made outside it; the horde's strip runs along its straight western side
        var hex = p.Hexagon ? Hex.In(SX, SZ) : null;
        w.SetLand(hex);
        site.Hex = hex;
        if (hex != null)
        {
            site.SpawnStrip = (int)hex.WestX + 24;
            site.WestZ0 = (int)hex.WestZ0 + 24;
            site.WestZ1 = (int)hex.WestZ1 - 24;
            site.KeepX = Math.Min(site.KeepX, (int)(hex.CX + hex.HalfW) - 128);
        }
        else { site.WestZ0 = 40; site.WestZ1 = SZ - 40; }
        // how far in from the land's edge a spot is: the box's edge, or the hexagon's
        double In(double x, double z) => hex != null ? hex.Edge((float)x, (float)z) : Math.Min(Math.Min(x, SX - x), Math.Min(z, SZ - z));
        var nHill = new ValueNoise(seed * 11 + 1); var nHill2 = new ValueNoise(seed * 13 + 2); var nFine = new ValueNoise(seed * 17 + 3);
        var nEdge = new ValueNoise(seed * 19 + 4); var nKnoll = new ValueNoise(seed * 23 + 5); var nSoil = new ValueNoise(seed * 29 + 6);
        var nForest = new ValueNoise(seed * 31 + 7); var nRidge = new ValueNoise(seed * 37 + 8); var nRiver = new ValueNoise(seed * 41 + 9);
        var nMoist = new ValueNoise(seed * 43 + 10); var nBiome = new ValueNoise(seed * 47 + 11); var nBloom = new ValueNoise(seed * 53 + 12);
        var nPond = new ValueNoise(seed * 59 + 13);

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
        double RiverHalf(double z) => (15 + nRiver.Sample(z * .02, 9.4) * 12) * ground.River;
        // a ford to the north; where the road crosses the river is deep, and the old bridge carries it over
        double roadCross = 0;
        for (int x = 0; x < SX; x++) if (Math.Abs(x - RiverX(RoadZ(x))) < 2) { roadCross = RoadZ(x); break; }
        double[] fords = { SZ * .22 };

        // the plateau rolls gently away from the citadel and lies flat round it
        double Plat(int x, int z)
        {
            double dk = Brushes.Dist(x - site.KeepX, z - site.KeepZ);
            double roll = Site.Smooth01((dk - 190) / 140) * ((nHill.Sample(x * .013 + 30, z * .013) - .5) * 16);
            return baseH + rise + (nHill2.Sample(x * .02 + 40, z * .02 + 9) - .5) * 3 + roll;
        }

        // two big ponds on the plateau, north and south of the citadel a good way off (40 m), 15–20 m across and up to
        // 2.5 m deep, with a ragged shore. Each lies level, its surface flush with the lowest ground round its shore, so
        // it holds still until someone digs into it.
        const int shore = 5;
        var ponds = new (int x, int z, int r, int level)[2];
        // how far the water reaches from a pond's middle, in a direction: the shore goes in and out
        double Reach(int n, int pr, double dx, double dz)
        {
            double a = Math.Atan2(dz, dx);
            return pr * (.78 + .44 * nPond.Sample(Math.Cos(a) * 1.4 + n * 9 + 4, Math.Sin(a) * 1.4 + n * 5 + 4));
        }
        for (int n = 0; n < ponds.Length; n++)
        {
            double a = (n == 0 ? 1.62 : 4.66) + (Hash.H3((int)seed, n, 3) - .5) * .4, dist = 300 + Hash.H3((int)seed, n, 4) * 30;
            int pr = 60 + (int)(Hash.H3((int)seed, n, 5) * 24), level = int.MaxValue;
            int px = site.KeepX + (int)(Math.Cos(a) * dist);
            int pz = Math.Clamp(site.KeepZ + (int)(Math.Sin(a) * dist), pr + shore + 16, SZ - pr - shore - 16);
            int span = 0;
            // the whole shore on the plateau, well back from the cliff wherever the cliff wanders, and on the land: nearer
            // the citadel, or smaller, where the hexagon narrows
            for (int tries = 0; tries < 60; tries++)
            {
                span = (int)(pr * 1.22) + shore;
                double edge = EdgeX(pz) + 70;
                for (int z = pz - span; z <= pz + span; z++) edge = Math.Max(edge, EdgeX(z) + span + 8);
                px = Math.Clamp(px, (int)Math.Ceiling(edge), SX - span - 16);
                pz = Math.Clamp(pz, span + 16, SZ - span - 16);
                if (In(px, pz) >= span + 16) break;
                if (Math.Abs(pz - site.KeepZ) > 170) pz += pz > site.KeepZ ? -8 : 8;
                else if (pr > 40) pr -= 4;
                else px -= 8;
            }
            for (int z = pz - span; z <= pz + span; z++)
                for (int x = px - span; x <= px + span; x++)
                {
                    double d = Brushes.Dist(x - px, z - pz), r = Reach(n, pr, x - px, z - pz);
                    if (d >= r && d < r + shore) level = Math.Min(level, (int)Math.Round(Plat(x, z)) - 1);
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
                    + Math.Pow(ridge, 3) * 24 * ground.Ridges + (nFine.Sample(x * .11, z * .11) - .5) * p.Roughness;
                low = Math.Min(low, baseH + 30 * ground.Ridges);
                double plat = Plat(x, z);
                double h = low; byte k = Low;
                // how far into a ramp's band, and how far from the road: hills and ramps fade out over a slope, never a cut
                double rampD = Math.Min(Math.Abs(z - rampN), Math.Abs(z - rampS)) - rampHalf;
                double roadD = x < roadEnd + 20 ? Math.Abs(z - RoadZ(x)) - 6 : 1e9;
                if (x >= edge) { h = plat; k = x - edge < 5 && !ramp ? Rim : Plateau; }
                else
                {
                    double kn = nKnoll.Sample(x * .018, z * .018);
                    if (kn > ground.Knoll && x > site.SpawnStrip + 16)
                    {
                        double fade = Site.Smooth01(roadD / 26) * Site.Smooth01(rampD / 30) * Site.Smooth01((x - site.SpawnStrip - 16) / 40.0);
                        double hill = (kn - ground.Knoll) * 190 * ground.KnollHeight * fade;
                        if (hill > 1.5) { h = low + hill; k = Knoll; }
                    }
                    if (x >= edge - rampLen && rampD < 34)
                    {
                        // the ramp up to the plateau, its sides sloping down to the lowland
                        double r = low + (plat - low) * Site.Smooth01((x - (edge - rampLen)) / rampLen);
                        double side = rampD <= 0 ? 1 : 1 - Site.Smooth01(rampD / 34);
                        double hr = h + (r - h) * side;
                        if (r > h && hr > h) { h = hr; k = rampD <= 0 ? Ramp : k; }
                    }
                    if (roadD < 0 && k != Ramp) k = Road;
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
                        // the ford shelves off into the deep on either side, no step in the riverbed
                        double deep = 3 + (1 - t * t) * 13;
                        double depth = 2 + (deep - 2) * Site.Smooth01((ford - 8) / 16);
                        h = RiverLevel - depth;
                        k = River;
                        wl = RiverLevel;
                    }
                    else if (d < rh + 120)
                    {
                        // the banks slope down to the water; further off the slope steepens, so a hill near the river
                        // comes down to it as a hillside, not cut off by a wall
                        double bank = RiverLevel + 2 + (d - rh) * .42 + Math.Max(0, d - rh - 30) * .95;
                        if (h > bank) { h = bank; if (k == Low || k == Knoll) k = Bank; }
                        // and never lower than the river near it: a low meadow beside the water would stand under it
                        h = Math.Max(h, RiverLevel + 1 - Math.Max(0, d - rh - 40) * .25);
                    }
                }
                for (int n = 0; n < ponds.Length; n++)
                {
                    var (px, pz, pr, level) = ponds[n];
                    double d = Brushes.Dist(x - px, z - pz);
                    if (d >= pr * 1.22 + shore || k is not (Plateau or Rim)) continue;
                    double r = Reach(n, pr, x - px, z - pz);
                    if (d >= r + shore) continue;
                    if (d < r)
                    {
                        double t = d / r;
                        h = level - 1 - (1 - t * t) * 19;
                        k = Pond; wl = (short)level;
                    }
                    // the shore climbs from the water's edge to the plateau
                    else h = level + (h - level) * Site.Smooth01((d - r) / shore);
                }
                kind[i] = k;
                // no water stands off the land
                water[i] = w.OnLand(x, z) ? wl : (short)0;
                site.HTop[i] = (int)Math.Round(h);
                soil[i] = (byte)(k switch { Knoll or Rim => 0, Plateau or Pond => 7, River => 4, _ => (int)((9 + nSoil.Sample(x * .02, z * .02) * 6) * ground.Soil) });
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
                    // the turf goes dry on the heights and lush in the hollows and by the water
                    double moist = nMoist.Sample(x * .011, z * .011) + (k == Bank ? .22 : 0) - (h - baseH) / 110.0 + ground.Wet;
                    byte grass = moist < .22 ? Mat.GrassDry : moist > .64 ? Mat.GrassLush : Mat.Grass;
                    byte top = k switch
                    {
                        Rim => Mat.Moss,
                        Ramp or Road => Mat.Path,
                        Knoll => Hash.H3(x, z, 41) < .35 ? Mat.Moss : Mat.Rock,
                        River or Pond => Mat.Soil,
                        _ => grass,
                    };
                    w.FillColumnRaw(x, z, h - 1, h, top);
                    if (water[i] > h) w.FillColumnRaw(x, z, h, water[i], Mat.Water);
                    else if (Mat.IsGrass(top))
                    {
                        double bloom = nBloom.Sample(x * .03, z * .03);
                        if (top != Mat.GrassDry && bloom > .74 && Hash.H3(x, z, 79) < .035)
                            w.FillColumnRaw(x, z, h, h + 1, Hash.H3(x, z, 80) < .6 ? Mat.Flower : Mat.Bloom);
                        else if (Hash.H3(x, z, 77) < p.TuftChance * (top == Mat.GrassDry ? 2.2 : 1))
                        {
                            w.FillColumnRaw(x, z, h, h + 1, Mat.Tuft);
                            if (Hash.H3(x, z, 78) < .35) w.FillColumnRaw(x, z, h + 1, h + 2, Mat.Tuft);
                        }
                        // stones lying about where the rock is near the surface
                        else if (k is Low or Bank && nKnoll.Sample(x * .018, z * .018) > .6 && Hash.H3(x, z, 81) < .01)
                            w.FillColumnRaw(x, z, h, h + 1 + (Hash.H3(x, z, 82) < .3 ? 1 : 0), Mat.Rock);
                    }
                }
        });

        bool NearKeep(int x, int z, int margin) => Brushes.Dist(x - site.KeepX, z - site.KeepZ) < site.KeepR + margin;
        // where water stands, for whatever lays a pool of its own
        site.WaterTop = water;
        AfterRelief(w, site, p, new Relief { SX = SX, SZ = SZ, Kind = kind, Soil = soil, Water = water });

        // the named places, before the rocks and trees: mines worth holding, and ruins of what stood here before
        (int x, int z) Free(int x, int z, int r)
        {
            bool Ok(int cx, int cz)
            {
                if (cx < r + 16 || cz < r + 16 || cx >= SX - r - 16 || cz >= SZ - r - 16 || In(cx, cz) < r + 16 || NearKeep(cx, cz, 100 + r)) return false;
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
        // the mines, as many as the kind of valley has: copper on the plateau east of the citadel (in the hexagon's
        // narrowing, south of it), silver in the lowland under the cliff, gold out west by the horde's side
        var copper = (x: 0, z: 0);
        if ((places & Places.Mines) != 0)
        {
            copper = Free(site.KeepX + (hex != null ? 40 : 150), site.KeepZ + (hex != null ? 170 : 64), 30);
            Landmarks.Mine(w, site, copper.x, copper.z, "Медный рудник", 3, false, seed + 1);
        }
        if ((places & Places.MinersHouses) != 0 && (places & Places.Mines) != 0)
        {
            var houses = Free(copper.x - 52, copper.z + 46, 34);
            Landmarks.Houses(w, site, houses.x, houses.z, "Брошенные дома рудокопов", seed + 2);
        }
        if ((places & (Places.Mines | Places.Silver)) != 0)
        {
            // the silver lies in the lowland between the river and the cliff, on the horde's way
            int sz = (int)(SZ * .36), sx = (int)((RiverX(sz) + RiverHalf(sz) + EdgeX(sz)) / 2);
            var silver = Free(sx, sz, 30);
            Landmarks.Mine(w, site, silver.x, silver.z, "Серебряная жила", 6, true, seed + 3);
        }
        var gold = (x: (int)(hex != null ? hex.WestX + 96 : SX * .12), z: (int)(SZ * (hex != null ? .3 : .17)));
        if ((places & Places.Mines) != 0)
        {
            gold = Free(gold.x, gold.z, 30);
            Landmarks.Mine(w, site, gold.x, gold.z, "Золотой прииск", 10, false, seed + 4);
        }
        if ((places & Places.Outpost) != 0)
        {
            var outpost = Free(gold.x + 64, gold.z + 56, 32);
            Landmarks.Outpost(w, site, outpost.x, outpost.z, "Старая застава", seed + 5);
        }
        if ((places & Places.Watchtowers) != 0)
        {
            double f = fords[0];
            var tower = Free((int)(RiverX(f) + RiverHalf(f) + 36), (int)f + 24, 20);
            Landmarks.Watchtower(w, site, tower.x, tower.z, "Руины сторожевой башни", seed + 6);
            var fallen = Free((int)EdgeX(rampN) + 34, rampN + 64, 20);
            Landmarks.Watchtower(w, site, fallen.x, fallen.z, "Обвалившаяся башня", seed + 7, .45);
        }
        // old stone bridges over the river: one carries the road, whole; one between the road and the north ford fell in
        foreach (var (z0, whole, name) in (places & Places.Bridges) != 0
            ? new[] { ((int)roadCross, true, "Старый мост"), ((int)((roadCross + fords[0]) / 2), false, "Обрушенный мост") }
            : Array.Empty<(int, bool, string)>())
        {
            int bz = z0;
            // the road's bridge stands where the road crosses; the fallen one keeps clear of the other places
            for (int tries = 0; tries < 8 && !whole; tries++)
            {
                bool clear = true;
                foreach (var l in site.Landmarks) if (Brushes.Dist(RiverX(bz) - l.X, bz - l.Z) < l.R + 50) clear = false;
                if (clear) break;
                bz += (tries % 2 == 0 ? 1 : -1) * (tries / 2 + 1) * 24;
            }
            if (bz < 60 || bz > SZ - 60) continue;
            // from dry ground to dry ground, along the road where it is the road's
            int xa = (int)RiverX(bz), xb = xa;
            while (xa > 2 && water[bz * SX + xa] > 0) xa--;
            while (xb < SX - 3 && water[bz * SX + xb] > 0) xb++;
            xa -= 10; xb += 10;
            int za = whole ? (int)Math.Round(RoadZ(xa)) : bz, zb = whole ? (int)Math.Round(RoadZ(xb)) : bz;
            Landmarks.OldBridge(w, site, xa, za, xb, zb, name, whole, seed + (whole ? 8u : 9u));
        }
        // the places of power: a barrow on the plateau north of the citadel, the black stone in the lowland under the
        // cliff to the south, a dead spring beyond the river, in the horde's country
        if ((places & Places.Power) != 0)
        {
            var barrow = Free(site.KeepX - 110, site.KeepZ - 200, 56);
            Landmarks.Barrow(w, site, barrow.x, barrow.z, "Курган", seed + 10);
            var black = Free((int)EdgeX(SZ * (hex != null ? .78 : .86)) - 110, (int)(SZ * (hex != null ? .78 : .86)), 38);
            Landmarks.BlackStone(w, site, black.x, black.z, "Чёрный камень", seed + 11);
            var spring = Free((int)(hex != null ? hex.WestX + 110 : SX * .16), (int)(SZ * .64), 50);
            Landmarks.DeadSpring(w, site, spring.x, spring.z, "Мёртвый родник", seed + 12);
        }

        bool Clear(int x, int z)
        {
            // (a tree's crown kept off the land's edge)
            if (x < site.SpawnStrip + 8 || x >= SX - 8 || z < 8 || z >= SZ - 8 || hex != null && In(x, z) < 20) return false;
            byte k = kind[z * SX + x];
            return k is Low or Plateau or Bank && !NearKeep(x, z, 48) && !Landmarks.Taken(site, x, z, 6);
        }

        // iron: veins through the rock, outcrops where they come up (MapGen.Ore.cs), before what grows over the ground
        OreVeins.Lay(w, site, seed);

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

        // forests in patches, a few lone trees between them: firs and pines in some stretches, oaks and birches in
        // others, cypresses and birches on the plateau; the trees stand tall, 6 to 13 m. A tree keeps its roots clear of
        // the others' (Flora), and one with no room where it would come up tries once more elsewhere in its square.
        for (int gz = 0; gz < SZ; gz += 20)
            for (int gx = 0; gx < SX; gx += 20)
                if (Grow(gx, gz, 0) == false) Grow(gx, gz, 1);

        // the tree of a square: null when none is to grow at the spot, false when it had no room there
        bool? Grow(int gx, int gz, int again)
        {
            int x = gx + (int)(Hash.H3(gx, gz, 201 + again * 10) * 16), z = gz + (int)(Hash.H3(gx, gz, 202 + again * 10) * 16);
            if (x >= SX || z >= SZ || !Clear(x, z) || !Mat.IsGrass(w.Get(x, w.SurfAt(x, z) - 1, z))) return null;
            double f = nForest.Sample(x * .006, z * .006);
            double density = f > .58 ? .72 : f > .48 ? .28 : .04;
            bool plateau = kind[z * SX + x] == Plateau;
            // the plateau is where the player builds: a clearing round the citadel, thin woods beyond
            if (plateau) density = NearKeep(x, z, 160) ? 0 : density * .3;
            if (Hash.H3(gx, gz, 203) > density * p.TreeDensity * ground.Trees) return null;
            double r = Hash.H3(gx, gz, 204), size = Hash.H3(gx, gz, 205), biome = nBiome.Sample(x * .004, z * .004);
            if (r > .94) return Flora.DeadTree(w, x, z, grown: true, scale: 1.45);
            if (r > .86) { Flora.Bush(w, x, z, 3 + (int)(size * 4)); return true; }
            if (plateau)
                return r < .45 ? Flora.Cypress(w, x, z, 42 + (int)(size * 26), grain: 1, grown: true)
                    : r < .7 ? Flora.Birch(w, x, z, 48 + (int)(size * 26))
                    : Flora.Pine(w, x, z, 66 + (int)(size * 34));
            if (biome < .45)
                return r < .55 ? Flora.Spruce(w, x, z, 56 + (int)(size * 40), size > .5 ? 3 : 2) : Flora.Pine(w, x, z, 68 + (int)(size * 36));
            if (biome > .58)
                return r < .5 ? Flora.Oak(w, x, z, 60 + (int)(size * 28), 4) : Flora.Birch(w, x, z, 46 + (int)(size * 26));
            return r < .3 ? Flora.Spruce(w, x, z, 56 + (int)(size * 40), size > .5 ? 3 : 2)
                : r < .55 ? Flora.Oak(w, x, z, 60 + (int)(size * 28), 4)
                : r < .72 ? Flora.Cypress(w, x, z, 42 + (int)(size * 26), grain: 1, grown: true)
                : Flora.Birch(w, x, z, 46 + (int)(size * 26));
        }

        site.RiverMid = new float[SZ]; site.RiverHalfWidth = new float[SZ];
        for (int z = 0; z < SZ; z++) { site.RiverMid[z] = (float)RiverX(z); site.RiverHalfWidth[z] = (float)RiverHalf(z); }
        // the river and the old road as lines, carried on out of the box the way the far land always laid them: the river
        // north and south the way it was going and then winding, the road on west
        site.River = new List<(float X, float Z, float Half)>();
        for (int z = -6400; z <= SZ + 6400; z += 32)
        {
            bool north = z < 0;
            int edge = north ? 0 : SZ - 1, inner = north ? 24 : SZ - 25;
            float x, half;
            if (z >= 0 && z < SZ) { x = site.RiverMid[z]; half = site.RiverHalfWidth[z]; }
            else
            {
                float slope = Math.Clamp((site.RiverMid[edge] - site.RiverMid[inner]) / 24f, -.6f, .6f), t = north ? -z : z - SZ;
                half = site.RiverHalfWidth[edge] + t * .004f;
                x = site.RiverMid[edge] + slope * 300 * (1 - MathF.Exp(-t / 300)) + 80 * MathF.Sin(t * .0032f) * (float)Site.Smooth01(t / 700);
            }
            site.River.Add((x, z, half));
        }
        var oldRoad = new List<(float X, float Z)>();
        for (double x = -6400; x <= roadEnd; x += 16) oldRoad.Add(((float)x, (float)(x < 0 ? SZ * .56 + Math.Sin(x * .018) * 18 : RoadZ(x))));
        site.Roads.Add(oldRoad);
        site.KeepBase = WorldGen.Keep(w, site);
        int gx2 = site.KeepX - site.KeepR - 7;
        foreach (int dz in new[] { -10, 10 }) WorldGen.Brazier(w, site, gx2, w.SurfAt(gx2, site.KeepZ + dz), site.KeepZ + dz, post: 4);
        site.Torches.Add((site.KeepX - site.KeepR - 3, site.KeepBase + 22, site.KeepZ));
        return site;
    }
}
