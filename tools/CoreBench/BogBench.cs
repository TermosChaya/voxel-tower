using System;
using System.Collections.Generic;
using System.Diagnostics;
using VoxelTower.Core;

/// <summary>
/// The horde's homeland a bog (--bog; the author's word: «их родиной - болота, а не выжженные земли»): on the big map its
/// zones are bog and no land anywhere is burnt; a map made with the burnt land of before is the same in all else, so a
/// campaign begun before reads back with its things where they were; a bog valley with the horde's town on its island is
/// low and wet, pools and mires about it, peat between the tussocks, no water open against the air, and what stands in it
/// can be walked to from the side a party comes in by; the sim lays its mires for good, and they slow whoever wades them.
/// </summary>
static class BogBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        // ---- the big map: the horde's zones bog, its valleys bog valleys, nothing burnt
        int hordeHexes = 0, bog = 0, burnt = 0, bogValleys = 0, hordeValleys = 0;
        for (uint seed = 1; seed <= 3; seed++)
        {
            var c = Campaign.NewWorld(seed, 72);
            foreach (var x in c.Cells)
            {
                if (x.Ground is HexGround.Scorched or HexGround.Lava) burnt++;
                if (x.Zone < 0 || c.Zones[x.Zone].Kind != ZoneKind.Horde || !x.Passable || x.Ground is HexGround.Water or HexGround.DeepWater) continue;
                hordeHexes++;
                if (x.Ground == HexGround.Bog) bog++;
                if (x.Thing is { Kind: ThingKind.Mine or ThingKind.Shrine } || x.Trove is TroveKind.DeadMine or TroveKind.Shrine) continue;
                hordeValleys++;
                if (x.Kind == ValleyKind.Bog) bogValleys++;
            }
        }
        check("the horde's realm on the big map is its homeland's bog, its valleys bog valleys, and no land is burnt",
            hordeHexes > 100 && bog == hordeHexes && bogValleys == hordeValleys && burnt == 0,
            $"{bog} of {hordeHexes} open hexes of the horde's zones bog on three M maps, {bogValleys} of {hordeValleys} of its valleys bog valleys, {burnt} hexes burnt");

        // ---- a map made with the burnt land of before: the same in all else (the map is made again from its seed when a
        // campaign is read back, and only what the game touched is read over it)
        int cells = 0, same = 0, groundsOff = 0;
        foreach (uint seed in new uint[] { 2, 7 })
        {
            Campaign old;
            WorldMaker.HordeGround = HexGround.Scorched;
            try { old = Campaign.NewWorld(seed, 72); }
            finally { WorldMaker.HordeGround = HexGround.Bog; }
            var now = Campaign.NewWorld(seed, 72);
            for (int i = 0; i < now.Cells.Count; i++)
            {
                var a = old.Cells[i]; var b = now.Cells[i];
                cells++;
                bool alike = a.Relief == b.Relief && a.Roads == b.Roads && a.Rivers == b.Rivers && a.Pass == b.Pass && a.Owner == b.Owner
                    && a.Holding == b.Holding && a.Zone == b.Zone && a.Trove == b.Trove && a.Height == b.Height && a.EntrySide == b.EntrySide
                    && a.Thing?.Kind == b.Thing?.Kind && a.Thing?.Res == b.Thing?.Res && a.Sum.Keep.Count > 0 == b.Sum.Keep.Count > 0;
                if (alike) same++;
                if (a.Ground != b.Ground && !(a.Ground is HexGround.Scorched or HexGround.Lava or HexGround.Grass && b.Ground == HexGround.Bog)) groundsOff++;
            }
        }
        check("a big map made with the horde's burnt land of before is the same in all but the horde's ground: a campaign begun before reads back with its things where they were",
            same == cells && groundsOff == 0, $"{same} of {cells} hexes alike on two M maps, {groundsOff} grounds changed but the horde's");

        // ---- a bog valley with the horde's town on its island
        const float M = VoxelWorld.PerMetre;
        var p = new MapParams { Seed = 5, Kind = ValleyKind.Bog, Hexagon = true, Holding = Holding.HordeTown, HordeWorks = uint.MaxValue, EntrySide = Hex.West, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize };
        var w = new VoxelWorld(p.SizeX, p.SizeY, p.SizeZ);
        var sw = Stopwatch.StartNew();
        var s = MapGen.Generate(w, p);
        long ms = sw.ElapsedMilliseconds;
        int open = 0, low = 0, peat = 0;
        for (int z = 1; z < w.SZ - 1; z++)
            for (int x = 1; x < w.SX - 1; x++)
            {
                int i = z * w.SX + x;
                for (int y = s.HTop[i]; y < s.WaterTop[i]; y++)
                    foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        if (w.Get(x + dx, y, z + dz) == Mat.Air && w.OnLand(x + dx, z + dz)) open++;
                if (!w.OnLand(x, z) || s.WaterTop[i] > 0 || s.Hex.Edge(x + .5f, z + .5f) < 40 || Brushes.Dist(x - s.KeepX, z - s.KeepZ) < 400) continue;
                low++;
                if (w.Get(x, s.HTop[i] - 1, z) == Mat.Peat) peat++;
            }
        check("a bog valley: pools by the dozen and mires in its lowland, peat between the tussocks, the island low, no water open against the air",
            s.Pools.Count >= 40 && s.Mires.Count >= 4 && peat > low / 20 && open == 0 && s.KeepBase < 72,
            $"made in {ms} ms; {s.Pools.Count} pools, {s.Lakes.Count} lakes, {s.Mires.Count} mires, peat on {peat * 100.0 / Math.Max(1, low):0} % of the lowland's dry ground, the island's ground at {s.KeepBase}, {open} open water faces");

        var sim = new Sim(w, s);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        bool forGood = sim.Mires.Count == s.Mires.Count && sim.Mires.TrueForAll(m => m.Until > 1e12);
        // from where a party comes in, on the road over the west side, on foot: to each mire's middle and onto the island
        // the horde's town stands on, toward the way in
        var road = s.Roads[0];
        var start = road.Find(q => s.Hex.Edge(q.X, q.Z) > 40);
        var u = sim.Spawn(Kinds.Swordsman, sim.NearestStand((int)start.X, (int)start.Z, Kinds.Swordsman.Move.Height));
        int reached = 0, targets = 0;
        var goals = new List<(float x, float z)>();
        foreach (var (mx, mz, _) in s.Mires) goals.Add((mx, mz));
        goals.Add((s.KeepX + (start.X - s.KeepX) * .2f, s.KeepZ + (start.Z - s.KeepZ) * .2f));
        foreach (var (gx, gz) in goals)
        {
            targets++;
            int cell = sim.NearestStand((int)gx, (int)gz, Kinds.Swordsman.Move.Height);
            if (cell >= 0 && sim.GoTo(u, cell, adjacent: true, budget: 400000)) reached++;
            u.Path = null;
        }
        // a man in a mire goes at a third of his pace: a second's walk from a mire's middle against one on the island
        float Walked(float x, float z, float tx, float tz)
        {
            var t = sim.Spawn(Kinds.Swordsman, sim.NearestStand((int)x, (int)z, Kinds.Swordsman.Move.Height));
            (t.X, t.Y, t.Z) = sim.Nav.Feet(t.Cell);
            float x0 = t.X, z0 = t.Z;
            sim.GoTo(t, sim.NearestStand((int)tx, (int)tz, Kinds.Swordsman.Move.Height));
            for (int i = 0; i < 20; i++) sim.Step(.05f);
            float d = MathF.Sqrt((t.X - x0) * (t.X - x0) + (t.Z - z0) * (t.Z - z0));
            t.Dead = true; t.DiedAt = -999;
            return d;
        }
        var (m0x, m0z, m0r) = s.Mires[0];
        float slowed = Walked(m0x, m0z, m0x + m0r * .9f, m0z);
        float free = Walked(s.KeepX + 60, s.KeepZ - 40, s.KeepX + 140, s.KeepZ - 40);
        check("in a bog valley the sim lays its mires for good, they slow whoever wades them, and every mire and the horde's town can be walked to from the side a party comes in by",
            forGood && reached == targets && slowed > 0 && slowed < free * .5f,
            $"{sim.Mires.Count} mires in the sim, for good {forGood}; reached {reached} of {targets} from the road in; in a second {slowed / M:0.0} m through the mire, {free / M:0.0} m on the island");
    }
}
