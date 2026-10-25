using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The valley a hexagon, and the kinds of valley (--hex): nothing made, walkable or buildable off the land; the river in
/// over one side and out over another; the horde in over the straight western side; one thrown off the edge gone; the
/// water at the edge standing; each kind with its places, the first valley with every old place while the old set is
/// kept and only ruins and bridges without it; the kind kept in a save.
/// </summary>
static class HexBench
{
    static readonly string[] OldPlaces =
    {
        "Медный рудник", "Брошенные дома рудокопов", "Серебряная жила", "Золотой прииск", "Старая застава", "Руины сторожевой башни",
        "Обвалившаяся башня", "Старый мост", "Обрушенный мост", "Курган", "Чёрный камень", "Мёртвый родник",
    };

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var mp = new MapParams { Hexagon = true };
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var hex = w.Land;

        // the land: every column on it has its bedrock, none off it has anything; nothing can be put off it
        int on = 0, off = 0, bareOn = 0, filledOff = 0;
        for (int z = 0; z < w.SZ; z += 3)
            for (int x = 0; x < w.SX; x += 3)
            {
                if (w.OnLand(x, z)) { on++; if (w.Get(x, 0, z) == Mat.Air) bareOn++; }
                else { off++; if (w.ColTop(x, z) > 0) filledOff++; }
            }
        var c = hex.Corners();
        bool setOff = w.Set((int)c[0].x - 40, 60, 3, Mat.Rock);
        check("the valley is a hexagon pointed north and south: every column on it has ground, none off it anything, and nothing can be laid off it",
            hex != null && on > 0 && off > 0 && bareOn == 0 && filledOff == 0 && !setOff && Math.Abs(hex.R * 2 - (w.SZ - 4)) < 1,
            $"radius {hex?.R:0}, half-width {hex?.HalfW:0}; {on} columns on it ({bareOn} bare), {off} off it ({filledOff} filled); laid off it {setOff}; {(double)on / (on + off):P0} of the box");

        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }

        // nobody stands off the land: no cell to stand in there
        int standOff = 0;
        for (int k = 0; k < sim.Nav.NZ; k++)
            for (int i = 0; i < sim.Nav.NX; i++)
            {
                int x = i * 8 + 4, z = k * 8 + 4;
                if (hex.Edge(x, z) > -8) continue;
                for (int y = 1; y < sim.Nav.NY; y++) if (sim.Nav.Stand(i, y, k, 2)) { standOff++; break; }
            }
        check("there is nowhere to stand off the land", standOff == 0, $"{standOff} cells off it to stand in");

        // the river comes in over one side and goes out over another: its water at the edge north and south
        int northEdge = 0, southEdge = 0;
        for (int z = 0; z < w.SZ; z++)
            for (int x = 0; x < w.SX; x++)
            {
                if (!w.OnLand(x, z) || hex.Edge(x + .5f, z + .5f) > 3 || site.WaterTop == null || site.WaterTop[z * w.SX + x] == 0) continue;
                if (site.RiverMid == null || Math.Abs(x - site.RiverMid[z]) > site.RiverHalfWidth[z] + 2) continue;
                if (z < hex.CZ) northEdge++; else southEdge++;
            }
        check("the river comes in over the north-western side and goes out over the south-western one", northEdge > 0 && southEdge > 0,
            $"{northEdge} columns of river at the edge to the north, {southEdge} to the south");

        // the water at the edge stands: let it run a while, and the river at the edge is still there
        int waterBefore = 0, waterAfter = 0;
        int Water()
        {
            int n = 0;
            for (int z = 0; z < w.SZ; z += 2)
                for (int x = 0; x < w.SX; x += 2)
                    if (w.OnLand(x, z) && hex.Edge(x + .5f, z + .5f) < 6)
                        for (int y = 1; y < 48; y++) if (w.Get(x, y, z) == Mat.Water) n++;
            return n;
        }
        waterBefore = Water();
        // a cut at the river's edge in the south: the water comes to the land's side, and stays in the valley
        int sz = (int)(hex.CZ + hex.R * .8f);
        if (site.RiverMid != null)
        {
            int rx = (int)site.RiverMid[sz];
            for (int x = rx; x > rx - 40; x--)
                for (int y = MapGen.RiverLevel - 4; y < MapGen.RiverLevel + 8; y++)
                    if (w.OnLand(x, sz)) w.Set(x, y, sz, Mat.Air);
        }
        Run(20);
        waterAfter = Water();
        check("the water at the land's edge stays in the valley: none runs out over the side", waterAfter >= waterBefore * .9,
            $"water at the edge {waterBefore} → {waterAfter}");

        // the horde comes in over the straight western side
        var entries = new List<(int x, int z)>();
        for (int z = site.WestZ0; z <= site.WestZ1; z += (site.WestZ1 - site.WestZ0) / 6)
        {
            int cell = sim.NearestStand(site.EntryX, site.EntryZ(z), 3);
            if (cell >= 0) { var (fx, _, fz) = sim.Nav.Feet(cell); entries.Add(((int)fx, (int)fz)); }
        }
        check("the horde comes in over the straight western side, all along it", entries.Count >= 6 && entries.All(e => e.x < hex.WestX + 48),
            string.Join(" ", entries.Select(e => $"{e.x},{e.z}")));
        var raider = sim.SpawnRaider(Kinds.Goblin);
        // (a cell of the walkable grid, a metre, may lie half over the edge: one stands in it by the edge)
        check("a raider called up stands by the western side", raider != null && raider.X < hex.WestX + 48 && hex.Edge(raider.X, raider.Z) > -6,
            raider == null ? "none" : $"at {raider.X:0},{raider.Z:0}");

        // one thrown off the edge goes down past the valley's side, and is gone
        var thrown = sim.Spawn(Kinds.Orc, sim.NearestStand((int)hex.WestX + 20, (int)hex.CZ, 2));
        float out0 = -12 * VoxelWorld.PerMetre;
        sim.Fling(thrown, out0, 4 * VoxelWorld.PerMetre, 0, "взрыв");
        Run(6);
        check("one thrown off the land's edge is gone over it", thrown.Dead && thrown.KilledBy.Length > 0, $"dead {thrown.Dead}, {thrown.KilledBy}; at {thrown.X:0},{thrown.Z:0}");

        // the kinds: each its places
        var named = site.Landmarks.Select(l => l.Name).ToHashSet();
        check("the first valley keeps every place it always had while the old set is kept", OldPlaces.All(named.Contains),
            $"missing: {string.Join(", ", OldPlaces.Where(n => !named.Contains(n)))}");
        HashSet<string> PlacesOf(ValleyKind k, bool classic)
        {
            var wk = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
            var sk = MapGen.Generate(wk, new MapParams { Seed = mp.Seed, Kind = k, Classic = classic, Hexagon = true });
            return sk.Landmarks.Select(l => l.Name).ToHashSet();
        }
        bool Mines(HashSet<string> s) => s.Contains("Медный рудник") || s.Contains("Золотой прииск");
        bool Power(HashSet<string> s) => s.Contains("Курган") || s.Contains("Чёрный камень") || s.Contains("Мёртвый родник");
        var first = PlacesOf(ValleyKind.First, false);
        var ore = PlacesOf(ValleyKind.Ore, true);
        var sacred = PlacesOf(ValleyKind.Sacred, true);
        var mountain = PlacesOf(ValleyKind.Mountain, true);
        check("without the old set the first valley has no mine and no place of power, only ruins and bridges",
            !Mines(first) && !Power(first) && !first.Contains("Серебряная жила") && first.Count > 0, string.Join(", ", first));
        check("a valley of ore has the three mines and no place of power; a holy one the places of power and no mine; a mountain one the silver",
            ore.Contains("Медный рудник") && ore.Contains("Серебряная жила") && ore.Contains("Золотой прииск") && !Power(ore)
                && sacred.Contains("Курган") && sacred.Contains("Чёрный камень") && !Mines(sacred)
                && mountain.Contains("Серебряная жила") && !mountain.Contains("Золотой прииск"),
            $"ore: {string.Join(", ", ore)}; holy: {string.Join(", ", sacred)}; mountain: {string.Join(", ", mountain)}");

        // the kinds' ground: the forest thicker, the mountains rockier
        (int trees, int rock) Ground(ValleyKind k)
        {
            var wk = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
            MapGen.Generate(wk, new MapParams { Seed = mp.Seed, Kind = k, Hexagon = true });
            int trees = 0, rock = 0;
            for (int z = 0; z < wk.SZ; z += 4)
                for (int x = 0; x < wk.SX; x += 4)
                {
                    if (!wk.OnLand(x, z)) continue;
                    int top = wk.ColTop(x, z);
                    if (top <= 0) continue;
                    byte m = wk.Get(x, top - 1, z);
                    if (Mat.IsLeafy(m)) trees++;
                    if (m == Mat.Rock) rock++;
                }
            return (trees, rock);
        }
        var g0 = Ground(ValleyKind.First); var gf = Ground(ValleyKind.Forest); var gm = Ground(ValleyKind.Mountain);
        check("a forest valley has more woods than the first, a mountain one more bare rock", gf.trees > g0.trees * 1.25 && gm.rock > g0.rock * 1.25,
            $"woods {g0.trees} → {gf.trees} in the forest; rock {g0.rock} → {gm.rock} in the mountains");

        // the kind is kept in a save, and the valley made again from it
        var wo = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var so = MapGen.Generate(wo, new MapParams { Seed = mp.Seed, Kind = ValleyKind.Ore, Classic = true, Hexagon = true });
        wo.Compact();
        var simO = new Sim(wo, so);
        var ms = new System.IO.MemoryStream();
        simO.Save(ms, mp.Seed, "test", "");
        ms.Position = 0;
        using (var reader = new SaveReader(ms))
            check("a save keeps the valley's kind and shape", reader.Kind == ValleyKind.Ore && reader.Classic && reader.Hexagon,
                $"{reader.Kind}, classic {reader.Classic}, hexagon {reader.Hexagon}");
    }
}
