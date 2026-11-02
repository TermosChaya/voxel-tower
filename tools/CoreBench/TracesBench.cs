using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using VoxelTower.Core;

/// <summary>
/// The bench's old works of people in the wild (--traces, MapGen.Traces.cs; the author: «Больше декораций богу
/// декораций»): every kind of valley on a few seeds has its wayside crosses and milestones by the roads, cairns, standing
/// stones and rings, old walls and footings, piles of logs and fence posts as the kind gives them; none in the town's
/// ground about the citadel, on a road, in water or on a named place; made with them off, a valley is the same voxel for
/// voxel but where they stand, so the rest of it is not moved; a save tells which way it was made, and one of before
/// (no bit) is made again without them; and they make a valley little slower to make.
/// </summary>
static class TracesBench
{
    static (VoxelWorld w, Site s, double ms) Make(uint seed, ValleyKind kind, bool traces = true)
    {
        var p = new MapParams { Seed = seed, Kind = kind, Hexagon = true, EntrySide = Hex.West, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize, Traces = traces };
        var w = new VoxelWorld(p.SizeX, p.SizeY, p.SizeZ);
        var sw = Stopwatch.StartNew();
        var s = MapGen.Generate(w, p);
        return (w, s, sw.Elapsed.TotalMilliseconds);
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var sw = Stopwatch.StartNew();
        var kinds = (ValleyKind[])Enum.GetValues(typeof(ValleyKind));
        var count = new Dictionary<OldWork, int>();
        var perKind = new List<string>();
        int bad = 0;
        var badWhere = new List<string>();
        bool sacredRings = true, mountainCairns = true, forestLogs = true;
        foreach (var kind in kinds)
            foreach (uint seed in new uint[] { 3 })
            {
                var (w, s, _) = Make(seed, kind);
                var here = new Dictionary<OldWork, int>();
                foreach (var (t, x, z) in s.Traces)
                {
                    here[t] = here.GetValueOrDefault(t) + 1;
                    count[t] = count.GetValueOrDefault(t) + 1;
                    // none in the town's ground, on a road, in water or on a named place
                    string why = Brushes.Dist(x - s.KeepX, z - s.KeepZ) < s.KeepR + 280 ? "by the citadel"
                        : s.RoadDist(x, z) < (t is OldWork.Shrine or OldWork.Milestone ? 12 : 16) ? "on a road"
                        : s.WaterTop != null && s.WaterTop[z * w.SX + x] > 0 ? "in water"
                        : Landmarks.Taken(s, x, z, 2) ? "on a named place" : null;
                    if (why == null) continue;
                    bad++;
                    if (badWhere.Count < 4) badWhere.Add($"{kind} {seed} {t} at {x},{z} {why}");
                }
                if (kind == ValleyKind.Sacred && here.GetValueOrDefault(OldWork.Ring) == 0) sacredRings = false;
                if (kind == ValleyKind.Mountain && here.GetValueOrDefault(OldWork.Cairn) < 3) mountainCairns = false;
                if (kind == ValleyKind.Forest && here.GetValueOrDefault(OldWork.Logs) < 2) forestLogs = false;
                if (seed == 3) perKind.Add($"{kind} {s.Traces.Count}");
            }
        string Counts() { var l = new List<string>(); foreach (var (t, n) in count) l.Add($"{t} {n}"); return string.Join(", ", l); }
        check("every kind of valley has old works of people in its wild, as the kind gives them",
            count.Count >= 8 && sacredRings && mountainCairns && forestLogs && !perKind.Exists(p => p.EndsWith(" 0")),
            $"{Counts()}; seed 3: {string.Join(", ", perKind)}; the holy valley's rings {(sacredRings ? "there" : "missing")}, the mountains' cairns {(mountainCairns ? "there" : "few")}, the forest's logs {(forestLogs ? "there" : "few")}");
        check("none of them by the citadel, on a road, in water or on a named place", bad == 0, bad == 0 ? "none" : string.Join("; ", badWhere));

        // made with them off: the same valley but where they stand
        int moved = 0, theirs = 0;
        double withMs = 0, withoutMs = 0;
        foreach (var kind in new[] { ValleyKind.First, ValleyKind.Sacred, ValleyKind.Mountain })
        {
            var (w0, s0, ms0) = Make(5, kind, traces: false);
            var (w1, s1, ms1) = Make(5, kind);
            withoutMs += ms0; withMs += ms1;
            for (int z = 0; z < w0.SZ; z += 1)
                for (int x = 0; x < w0.SX; x += 1)
                {
                    int top = Math.Min(w0.SY - 1, Math.Max(w0.ColTop(x, z), w1.ColTop(x, z)) + 1);
                    bool differs = false;
                    for (int y = 1; y <= top && !differs; y++) differs = w0.Get(x, y, z) != w1.Get(x, y, z);
                    if (!differs) continue;
                    bool near = false;
                    foreach (var (_, tx, tz) in s1.Traces) if (Math.Abs(tx - x) <= 48 && Math.Abs(tz - z) <= 48) { near = true; break; }
                    if (near) theirs++; else moved++;
                }
        }
        check("made with them off, a valley is the same voxel for voxel but where they stand", moved == 0 && theirs > 0,
            $"{theirs} columns changed by them, {moved} elsewhere");

        // a save tells which way it was made; one of before (no bit in its byte) is made again without them
        bool told = true;
        foreach (bool traces in new[] { false, true })
        {
            var (w, s, _) = Make(4, ValleyKind.Forest, traces);
            var sim = new Sim(w, s);
            using var ms = new MemoryStream();
            sim.Save(ms, 4, "test", "");
            ms.Position = 0;
            using var r = new SaveReader(ms);
            told &= r.Traced == traces && r.Hexagon && r.Massifs && r.Kind == ValleyKind.Forest;
        }
        check("a save tells whether its valley was made with them, and one of before is made again without", told, told ? "right" : "wrong");
        check("they make a valley little slower to make", withMs < withoutMs * 1.15 + 150, $"{withMs:0} ms with them, {withoutMs:0} ms without, three valleys");
        Console.WriteLine($"     ({sw.ElapsedMilliseconds / 1000.0:0.0} s)");
    }
}
