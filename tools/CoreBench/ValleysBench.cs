using System;
using System.Collections.Generic;
using System.Diagnostics;
using VoxelTower.Core;

/// <summary>
/// Every valley is made (--valleys): each kind by itself on many seeds, and the cells of two big maps as the campaign
/// makes them, none of them failing on the way. The mountain's valleys most: their ground stands up to the top of the
/// world, and a mine's knoll, a watchtower or an outcrop of ore raised on a peak went past it, and the valley was not
/// made at all (2 seeds in 40, about one mountain cell in thirteen of a big map). A share of the maps' cells by default
/// (every mountain cell of the first map's every eighth, and as many of the rest); VALLEYS_ALL=1 makes every passable
/// cell of both (some minutes); VALLEYS_ONLY=kind[:seed] or map:seed:q:r makes only those, with the whole trace.
/// </summary>
static class ValleysBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        bool all = Environment.GetEnvironmentVariable("VALLEYS_ALL") == "1";
        string only = Environment.GetEnvironmentVariable("VALLEYS_ONLY");
        var sw = Stopwatch.StartNew();
        if (only != null && only.StartsWith("map:"))
        {
            var a = only.Split(':');
            var c = Campaign.NewWorld(uint.Parse(a[1]), 72);
            var cell = c.Cells.Find(x => x.Q == int.Parse(a[2]) && x.R == int.Parse(a[3]));
            Console.WriteLine($"       {Make(c.ParamsOf(cell), true) ?? "made"}");
            return;
        }
        // each kind by itself: the mountain's on forty seeds, the others on eight
        foreach (ValleyKind kind in Enum.GetValues(typeof(ValleyKind)))
        {
            uint seeds = kind == ValleyKind.Mountain ? 40u : 8u;
            var bad = new List<string>();
            int n = 0;
            for (uint seed = 1; seed <= seeds; seed++)
            {
                if (only != null)
                {
                    var parts = only.Split(':');
                    if (Valleys.Parse(parts[0]) != kind || parts.Length > 1 && uint.Parse(parts[1]) != seed) continue;
                }
                n++;
                var p = new MapParams { Seed = seed, Kind = kind, Hexagon = true, EntrySide = Hex.West, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize };
                string fail = Make(p, only != null);
                if (fail != null) bad.Add($"seed {seed}: {fail}");
            }
            if (n > 0)
                check($"a {Valleys.Name(kind)} is made by itself on each of {n} seeds", bad.Count == 0,
                    bad.Count == 0 ? $"all {n} made" : $"{bad.Count} of {n} fail — {string.Join("; ", bad.GetRange(0, Math.Min(4, bad.Count)))}");
        }
        if (only != null) { Console.WriteLine($"     ({sw.ElapsedMilliseconds / 1000.0:0.0} s)"); return; }
        // the cells of two big maps as the campaign makes them: their sides, their places, their holdings
        foreach (uint mapSeed in new uint[] { 1, 2 })
        {
            var c = Campaign.NewWorld(mapSeed, 72);
            var bad = new List<string>();
            int n = 0, mountains = 0, others = 0, part = 0;
            // (every one of them told in parts of four hundred, so the bench's watch for a stuck run is not woken)
            void Told(bool last)
            {
                if (n == 0 || !last && n % 400 != 0) return;
                check($"the cells of big map {mapSeed} are made as the campaign makes them{(all ? $", every passable one, part {++part}" : ", a share of them")}", bad.Count == 0,
                    bad.Count == 0 ? $"all {n} made" : $"{bad.Count} of {n} fail — {string.Join("; ", bad.GetRange(0, Math.Min(4, bad.Count)))}");
                if (all) { n = 0; bad.Clear(); }
            }
            foreach (var cell in c.Cells)
            {
                if (!cell.Passable) continue;
                bool mountain = cell.Kind == ValleyKind.Mountain;
                if (!all)
                {
                    // the first map's mountains one in eight, the rest of both maps one in sixty, the second map's mountains
                    // one in thirty (enough to meet the faults this mode was made for, and the bench not hours long)
                    int every = mountain ? (mapSeed == 1 ? 8 : 30) : 60;
                    if ((mountain ? mountains++ : others++) % every != 0) continue;
                }
                n++;
                string fail = Make(c.ParamsOf(cell), false);
                if (fail != null) bad.Add($"{cell.Q},{cell.R} ({Valleys.Name(cell.Kind)}): {fail}");
                if (all) Told(false);
            }
            Told(true);
        }
        Console.WriteLine($"     ({sw.ElapsedMilliseconds / 1000.0:0.0} s)");
    }

    /// <summary>Makes a valley; null if it is made, else what failed and where (the whole trace when asked).</summary>
    static string Make(MapParams p, bool trace)
    {
        try
        {
            var w = new VoxelWorld(p.SizeX, p.SizeY, p.SizeZ);
            MapGen.Generate(w, p);
            return null;
        }
        catch (Exception e)
        {
            if (trace) Console.WriteLine(e);
            var frame = e.StackTrace?.Split('\n')[0].Trim() ?? "";
            return $"{e.GetType().Name} {frame}";
        }
    }
}
