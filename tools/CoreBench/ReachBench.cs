using System;
using System.Collections.Generic;
using System.Diagnostics;
using VoxelTower.Core;

/// <summary>
/// Every side of a valley reaches the citadel (--reach): a valley made by itself on a hexagon, of every kind and many
/// seeds, the horde from the west, and the river's valleys of two big maps as the campaign makes them; from three places
/// along each of its six sides, on dry ground a few metres in, a man on foot finds a way up to the gate. The river's
/// valley first and most: its river is broad, and on seed 5 the west side was cut off from everything over it.
/// REACH_DEBUG=1 tells each side's way (REACH_MAP=1 draws the whole valley too); REACH_ONLY=kind[:seed] makes only
/// those, REACH_ONLY=map:seed:q:r one valley of a big map.
/// </summary>
static class ReachBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        bool debug = Environment.GetEnvironmentVariable("REACH_DEBUG") == "1";
        string only = Environment.GetEnvironmentVariable("REACH_ONLY");
        // the river's valley on a dozen seeds; every other kind on three
        var runs = new List<(ValleyKind kind, uint seed)>();
        for (uint s = 1; s <= 12; s++) runs.Add((ValleyKind.Marsh, s));
        foreach (var k in new[] { ValleyKind.First, ValleyKind.Ore, ValleyKind.Sacred, ValleyKind.Forest, ValleyKind.Mountain, ValleyKind.Bog })
            for (uint s = 1; s <= 3; s++) runs.Add((k, s));
        var failed = new Dictionary<ValleyKind, List<string>>();
        var made = new Dictionary<ValleyKind, int>();
        var sw = Stopwatch.StartNew();
        if (only != null && only.StartsWith("map:"))
        {
            // REACH_ONLY=map:seed:q:r — one valley of a big map
            var a = only.Split(':');
            var c = Campaign.NewWorld(uint.Parse(a[1]), 72);
            var cell = c.Cells.Find(x => x.Q == int.Parse(a[2]) && x.R == int.Parse(a[3]));
            Console.WriteLine($"       cut: {string.Join(", ", Cut(c.ParamsOf(cell), only, debug))}; entry side {cell.EntrySide}, edges {string.Join(" ", Array.ConvertAll(cell.Edges ?? Array.Empty<HexEdge>(), e => $"r{e?.River:0.00}/d{e?.Road:0.00}"))}");
            return;
        }
        foreach (var (kind, seed) in runs)
        {
            if (only != null)
            {
                var parts = only.Split(':');
                if (Valleys.Parse(parts[0]) != kind || parts.Length > 1 && uint.Parse(parts[1]) != seed) continue;
            }
            made[kind] = made.GetValueOrDefault(kind) + 1;
            var cut = Cut(kind, seed, debug);
            if (cut.Count > 0) (failed.TryGetValue(kind, out var l) ? l : failed[kind] = new List<string>()).Add($"seed {seed}: {string.Join(", ", cut)}");
        }
        foreach (var (kind, n) in made)
        {
            var bad = failed.GetValueOrDefault(kind);
            check($"every side of a {Valleys.Name(kind)} reaches the citadel on foot, on {n} seeds", bad == null,
                bad == null ? $"all {n * 18} places along the sides reach the gate" : string.Join("; ", bad));
        }
        // the river's valleys of two big maps as the campaign makes them, their sides' rivers and roads the map's (fords
        // where its roads cross the river), the horde from the side the map has; but a town of the horde's, walled
        if (only == null)
        {
            var mapBad = new List<string>();
            int mapMade = 0;
            foreach (uint mapSeed in new uint[] { 1, 2 })
            {
                var c = Campaign.NewWorld(mapSeed, 72);
                int taken = 0;
                foreach (var cell in c.Cells)
                {
                    if (taken >= 4 || cell.Kind != ValleyKind.Marsh || cell.Holding == Holding.HordeTown || cell.Trove != TroveKind.None || !cell.Passable) continue;
                    taken++; mapMade++;
                    var cut = Cut(c.ParamsOf(cell), $"map {mapSeed} cell {cell.Q},{cell.R}", debug);
                    if (cut.Count > 0) mapBad.Add($"map {mapSeed}, cell {cell.Q},{cell.R}: {string.Join(", ", cut)}");
                }
            }
            check($"every side of the river's valleys of two big maps, as the campaign makes them, reaches the citadel on foot", mapMade >= 4 && mapBad.Count == 0,
                mapBad.Count == 0 ? $"{mapMade} valleys, all {mapMade * 18} places along their sides reach the gate" : string.Join("; ", mapBad));
        }
        Console.WriteLine($"     ({sw.ElapsedMilliseconds / 1000.0:0.0} s)");
    }

    /// <summary>The places along a valley's sides from which nobody on foot gets to the citadel's gate: "side·part".</summary>
    public static List<string> Cut(ValleyKind kind, uint seed, bool debug) =>
        Cut(new MapParams { Seed = seed, Kind = kind, Hexagon = true, EntrySide = Hex.West, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize },
            $"{kind} {seed}", debug);

    public static List<string> Cut(MapParams p, string name, bool debug)
    {
        var w = new VoxelWorld(p.SizeX, p.SizeY, p.SizeZ);
        var s = MapGen.Generate(w, p);
        var sim = new Sim(w, s);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        int h = Kinds.Swordsman.Move.Height;
        int gate = sim.NearestStand(s.KeepX - s.KeepR - 20, s.KeepZ, h);
        var cut = new List<string>();
        for (int side = 0; side < 6; side++)
            foreach (float t in new[] { .3f, .5f, .7f })
            {
                // on dry ground a few metres in (not in a lake or the river that lies there: a party comes in onto the land)
                var (x, z) = s.Hex.SidePoint(side, t, 40);
                foreach (int inset in new[] { 40, 56, 72, 88 })
                {
                    bool dry = false;
                    foreach (float dt in new[] { 0f, .03f, -.03f, .06f, -.06f, .09f, -.09f })
                    {
                        var (dx, dz) = s.Hex.SidePoint(side, t + dt, inset);
                        if (s.WaterTop[(int)dz * w.SX + (int)dx] == 0) { (x, z) = (dx, dz); dry = true; break; }
                    }
                    if (dry) break;
                }
                int c = sim.NearestStand((int)x, (int)z, h);
                bool way = false;
                if (c >= 0 && gate >= 0)
                {
                    var u = sim.Spawn(Kinds.Swordsman, c);
                    way = sim.GoTo(u, gate, adjacent: true, budget: 600000);
                    u.Dead = true; u.DiedAt = -999;
                }
                if (!way) cut.Add($"{side}·{t:0.0}");
                if (debug) Console.WriteLine($"       {name} side {side} at {t:0.0} ({x:0},{z:0}): {(way ? "way" : "CUT")}");
                if (debug && !way)
                {
                    // the ground about it, a character every 8 voxels: water ~, else its height over the river's in metres
                    var (fx, fy, fz) = c >= 0 ? sim.Nav.Feet(c) : (x, 0, z);
                    Console.WriteLine($"         stands at {fx:0},{fy:0},{fz:0} (river at {MapGen.RiverLevel}, water in the cell {(c >= 0 ? sim.Nav.Water[c] : -1)}); north up, 8 voxels a character:");
                    foreach (int inset in new[] { 64, 96, 140 })
                    {
                        var (ix, iz) = s.Hex.SidePoint(side, t, inset);
                        int ic = sim.NearestStand((int)ix, (int)iz, h);
                        bool iw = false;
                        if (ic >= 0)
                        {
                            var v = sim.Spawn(Kinds.Swordsman, ic);
                            iw = sim.GoTo(v, gate, adjacent: true, budget: 600000);
                            v.Dead = true; v.DiedAt = -999;
                        }
                        Console.WriteLine($"         {inset} in ({ix:0},{iz:0}): {(iw ? "way" : "cut")}");
                    }
                    for (int zz = (int)fz - 200; zz <= (int)fz + 200; zz += 8)
                    {
                        var row = new System.Text.StringBuilder("         ");
                        for (int xx = (int)fx - 320; xx <= (int)fx + 320; xx += 8)
                        {
                            if (xx < 0 || zz < 0 || xx >= w.SX || zz >= w.SZ || !w.OnLand(xx, zz)) { row.Append(' '); continue; }
                            int i = zz * w.SX + xx;
                            if (Math.Abs(xx - fx) < 4 && Math.Abs(zz - fz) < 4) { row.Append('@'); continue; }
                            if (s.WaterTop[i] > 0) { row.Append('~'); continue; }
                            int m = (s.HTop[i] - MapGen.RiverLevel) / 8;
                            row.Append(m < 0 ? '_' : m < 10 ? (char)('0' + m) : (char)('a' + Math.Min(25, m - 10)));
                        }
                        Console.WriteLine(row);
                    }
                }
            }
        if (debug && Environment.GetEnvironmentVariable("REACH_MAP") == "1")
        {
            // the whole valley, 16 voxels a character: water ~, the height over the river's in metres, the gate G, the
            // bridges B, the places that reach the gate +, those that do not X
            var marks = new Dictionary<(int, int), char>();
            void Mark(float mx, float mz, char ch) => marks[((int)mx / 16, (int)mz / 16)] = ch;
            Mark(s.KeepX - s.KeepR - 20, s.KeepZ, 'G');
            foreach (var l in s.Landmarks) if (l.Name.Contains("мост")) Mark(l.X, l.Z, 'B');
            for (int side = 0; side < 6; side++)
                foreach (float t in new[] { .3f, .5f, .7f })
                {
                    var (x, z) = s.Hex.SidePoint(side, t, 40);
                    Mark(x, z, cut.Contains($"{side}·{t:0.0}") ? 'X' : '+');
                }
            for (int zz = 0; zz < w.SZ; zz += 16)
            {
                var row = new System.Text.StringBuilder("     ");
                for (int xx = 0; xx < w.SX; xx += 16)
                {
                    if (marks.TryGetValue((xx / 16, zz / 16), out char mc)) { row.Append(mc); continue; }
                    if (!w.OnLand(xx, zz)) { row.Append(' '); continue; }
                    int i = zz * w.SX + xx;
                    if (s.WaterTop[i] > 0) { row.Append('~'); continue; }
                    int m = (s.HTop[i] - MapGen.RiverLevel) / 8;
                    row.Append(m < 0 ? '_' : m < 10 ? (char)('0' + m) : (char)('a' + Math.Min(25, m - 10)));
                }
                Console.WriteLine(row);
            }
        }
        if (debug)
        {
            foreach (var l in s.Landmarks) if (l.Name.Contains("мост")) Console.WriteLine($"       {name}: {l.Name} at {l.X:0},{l.Z:0} r {l.R:0}");
            Console.WriteLine($"       {name}: keep {s.KeepX},{s.KeepZ}, gate cell {gate}, {s.River.Count} points of river, {s.Roads.Count} roads");
        }
        return cut;
    }
}
