using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using VoxelTower.Core;

/// <summary>
/// The bench's nature's dressing of the open ground (--wilds, MapGen.Wilds.cs; the author: «Больше декораций богу
/// декораций»): every kind of valley on two seeds has what its kind gives it — the mountains their boulder fields,
/// ledges, heather and snow in the hollows, the ore hills boulders, ledges and broom, the forest bracket fungi on its dead
/// wood, the river's valley logs and pebbles on its banks, a beavers' lodge and loosestrife, the bog cranberries, heather
/// and fungi, the holy valley hawthorns in blossom, the first a little of everything; none of it in the citadel's ground,
/// on a road, the plateau or a ramp, in water, on a named place or in the horde's strip, and what stands in a walker's way
/// further off the roads and the citadel than the plants; made with it off, a valley is the same voxel for voxel but where
/// it lies; a save tells which way it was made, and one of before (no bit) is made again without it; it makes a valley
/// little slower to make and little dearer to draw. WILDS_HASH=1 prints the hashes of a few valleys of each kind made
/// with it off instead, to tell them from the valleys of before (MountainsBench's MOUNTAINS_HASH); WILDS_DEBUG=1 how much
/// rock each boulder and ledge of the mountains' valley on seed 3 lays.
/// </summary>
static class WildsBench
{
    /// <summary>A valley made, and the work its making took the bench's own thread (the dressing is laid on it, not in parallel).</summary>
    static (VoxelWorld w, Site s, double ms) Make(uint seed, ValleyKind kind, bool wilds = true, bool massifs = true)
    {
        var p = new MapParams { Seed = seed, Kind = kind, Hexagon = true, EntrySide = Hex.West, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize, Massifs = massifs, Wilds = wilds };
        var w = new VoxelWorld(p.SizeX, p.SizeY, p.SizeZ);
        double t0 = BenchClock.CpuMs();
        var s = MapGen.Generate(w, p);
        return (w, s, BenchClock.CpuMs() - t0);
    }

    /// <summary>What stands in a walker's way of it, against what does not (plants, laid in the turf or over it).</summary>
    static bool Big(Wild t) => t is Wild.Erratic or Wild.Outcrop or Wild.Driftwood or Wild.Lodge;

    public static void Run(Action<string, bool, string> check)
    {
        if (Environment.GetEnvironmentVariable("WILDS_HASH") == "1") { Hashes(); return; }
        if (Environment.GetEnvironmentVariable("WILDS_DEBUG") == "1")
        {
            var (dw, ds, _) = Make(3, ValleyKind.Mountain);
            foreach (var (t, x, z, r) in ds.Wilds)
            {
                if (t != Wild.Outcrop && t != Wild.Erratic) continue;
                int over = 0, most = 0;
                for (int zz = z - r; zz <= z + r; zz++)
                    for (int xx = x - r; xx <= x + r; xx++)
                    {
                        int g = ds.HTop[zz * dw.SX + xx], n = 0;
                        for (int y = g; y < g + 16; y++) if (dw.Get(xx, y, zz) is Mat.Rock or Mat.Moss) n++;
                        over += n; most = Math.Max(most, n);
                    }
                Console.WriteLine($"{t} at {x},{z} r {r}: {over} voxels over the ground, {most} the highest; kind {ds.GroundKind[z * dw.SX + x]}");
            }
            return;
        }
        Console.WriteLine();
        var sw = Stopwatch.StartNew();
        var kinds = (ValleyKind[])Enum.GetValues(typeof(ValleyKind));
        var count = new Dictionary<Wild, int>();
        var byKind = new Dictionary<ValleyKind, Dictionary<Wild, int>>();
        int bad = 0;
        var badWhere = new List<string>();
        foreach (var kind in kinds)
        {
            var here = byKind[kind] = new Dictionary<Wild, int>();
            foreach (uint seed in new uint[] { 3, 7 })
            {
                var (w, s, _) = Make(seed, kind);
                foreach (var (t, x, z, _) in s.Wilds)
                {
                    here[t] = here.GetValueOrDefault(t) + 1;
                    count[t] = count.GetValueOrDefault(t) + 1;
                    if (t is Wild.Snow or Wild.Cranberry) continue;
                    // its middle: off the citadel's ground, the roads, the water, the named places and the horde's strip
                    bool big = Big(t);
                    string why = Brushes.Dist(x - s.KeepX, z - s.KeepZ) < s.KeepR + (big ? MapGen.WildsClearBig : MapGen.WildsClearSmall) - 8 ? "by the citadel"
                        : s.RoadDist(x, z) < (big ? 14 : 6) ? "on a road"
                        : t != Wild.Driftwood && s.WaterTop[z * w.SX + x] > 0 ? "in water"
                        : Landmarks.Taken(s, x, z, 2) ? "on a named place"
                        : s.InStrip(x, z) ? "in the horde's strip" : null;
                    if (why == null) continue;
                    bad++;
                    if (badWhere.Count < 4) badWhere.Add($"{kind} {seed} {t} at {x},{z} {why}");
                }
            }
        }
        int N(ValleyKind k, Wild t) => byKind[k].GetValueOrDefault(t);
        var lacks = new List<string>();
        void Want(ValleyKind k, Wild t, int least) { if (N(k, t) < least) lacks.Add($"{k} {t} {N(k, t)} < {least}"); }
        Want(ValleyKind.Mountain, Wild.Erratic, 8); Want(ValleyKind.Mountain, Wild.Outcrop, 3); Want(ValleyKind.Mountain, Wild.Heather, 6); Want(ValleyKind.Mountain, Wild.Snow, 1);
        Want(ValleyKind.Ore, Wild.Erratic, 6); Want(ValleyKind.Ore, Wild.Outcrop, 3); Want(ValleyKind.Ore, Wild.Broom, 1);
        Want(ValleyKind.Forest, Wild.Bracket, 10);
        Want(ValleyKind.Marsh, Wild.Spires, 3); Want(ValleyKind.Marsh, Wild.Cranberry, 1);
        if (N(ValleyKind.Marsh, Wild.Driftwood) + N(ValleyKind.Marsh, Wild.Pebbles) < 3) lacks.Add($"Marsh banks {N(ValleyKind.Marsh, Wild.Driftwood) + N(ValleyKind.Marsh, Wild.Pebbles)} < 3");
        Want(ValleyKind.Bog, Wild.Cranberry, 2); Want(ValleyKind.Bog, Wild.Heather, 2); Want(ValleyKind.Bog, Wild.Bracket, 3);
        Want(ValleyKind.Sacred, Wild.Hawthorn, 2); Want(ValleyKind.Sacred, Wild.Spires, 3);
        Want(ValleyKind.First, Wild.Rose, 2); Want(ValleyKind.First, Wild.Spires, 2); Want(ValleyKind.First, Wild.Erratic, 2);
        int lodges = 0;
        foreach (var k in kinds) lodges += N(k, Wild.Lodge);
        if (lodges == 0) lacks.Add("no lodge in any valley");
        string Counts() { var l = new List<string>(); foreach (var (t, n) in count) l.Add($"{t} {n}"); return string.Join(", ", l); }
        string Kinds() { var l = new List<string>(); foreach (var (k, d) in byKind) { int n = 0; foreach (var (_, c) in d) n += c; l.Add($"{k} {n}"); } return string.Join(", ", l); }
        check("every kind of valley has nature's dressing of its open ground as the kind gives it", count.Count >= 12 && lacks.Count == 0,
            $"{Counts()}; by kind (seeds 3 and 7): {Kinds()}{(lacks.Count > 0 ? "; lacking: " + string.Join("; ", lacks) : "")}");
        check("none of it by the citadel, on a road, in water, on a named place or in the horde's strip", bad == 0, bad == 0 ? "none" : string.Join("; ", badWhere));

        // made with it off: the same valley but where it lies; and where it lies, only on wild ground, off the roads; and
        // the faces of the whole of the mountains' valley, the most dressed, as meshed with it and without
        int Quads(VoxelWorld w)
        {
            var mesher = new GreedyMesher();
            var pad = new byte[34 * 34 * 34];
            int quads = 0;
            for (int ci = 0; ci < w.ChunkCount; ci++)
            {
                var b = mesher.MeshChunk(w, ci, pad);
                if (b == null) continue;
                quads += b.Quads;
                b.Return();
            }
            return quads;
        }
        int moved = 0, theirs = 0, wrong = 0, blockingOnRoads = 0, qa = 0, qb = 0;
        var wrongWhere = new List<string>();
        double withMs = 0, withoutMs = 0;
        foreach (var kind in new[] { ValleyKind.First, ValleyKind.Mountain, ValleyKind.Marsh, ValleyKind.Bog })
        {
            var (w0, s0, ms0) = Make(5, kind, wilds: false);
            var (w1, s1, ms1) = Make(5, kind);
            withoutMs += ms0; withMs += ms1;
            if (kind == ValleyKind.Mountain) { qa = Quads(w0); qb = Quads(w1); }
            for (int z = 0; z < w0.SZ; z++)
                for (int x = 0; x < w0.SX; x++)
                {
                    int colTop = Math.Min(w0.SY - 1, Math.Max(w0.ColTop(x, z), w1.ColTop(x, z)) + 1);
                    bool differs = false, blocks = false;
                    for (int y = 1; y <= colTop; y++)
                    {
                        byte a = w0.Get(x, y, z), b = w1.Get(x, y, z);
                        if (a == b) continue;
                        differs = true;
                        if (b != Mat.Air && !Mat.Passable[b] && y >= s1.HTop[z * w1.SX + x]) blocks = true;
                    }
                    if (!differs) continue;
                    bool near = false;
                    Wild by = Wild.Erratic;
                    foreach (var (tk, tx, tz, tr) in s1.Wilds) if (Math.Abs(tx - x) <= tr + 6 && Math.Abs(tz - z) <= tr + 6) { near = true; by = tk; break; }
                    if (near) theirs++; else moved++;
                    byte k = s1.GroundKind[z * w1.SX + x];
                    string why = k is not (Relief.Low or Relief.Bank or Relief.Knoll or Relief.Crag) ? $"on ground of kind {k}"
                        : s1.WaterTop[z * w1.SX + x] > 0 ? "in water"
                        : Brushes.Dist(x - s1.KeepX, z - s1.KeepZ) < s1.KeepR + MapGen.WildsClearSmall ? "by the citadel"
                        : s1.InStrip(x, z) ? "in the horde's strip" : null;
                    if (why != null) { wrong++; if (wrongWhere.Count < 4) wrongWhere.Add($"{kind} {x},{z} {why} ({(near ? by.ToString() : "no thing of it near")})"); }
                    if (blocks && s1.RoadDist(x, z) < 8) blockingOnRoads++;
                }
        }
        check("made with it off, a valley is the same voxel for voxel but where it lies", moved == 0 && theirs > 0,
            $"{theirs} columns changed by it, {moved} elsewhere");
        check("what it lays lies on wild ground only, and nothing of it stands in the way on a road", wrong == 0 && blockingOnRoads == 0,
            $"{wrong} columns on other ground{(wrongWhere.Count > 0 ? " (" + string.Join("; ", wrongWhere) + ")" : "")}, {blockingOnRoads} in the way by a road");

        // a save tells which way it was made; one of before (no bit in its byte) is made again without it
        bool told = true;
        foreach (bool wilds in new[] { false, true })
        {
            var (w, s, _) = Make(4, ValleyKind.Forest, wilds);
            var sim = new Sim(w, s);
            using var ms = new MemoryStream();
            sim.Save(ms, 4, "test", "");
            ms.Position = 0;
            using var r = new SaveReader(ms);
            told &= r.Wilded == wilds && r.Traced && r.Hexagon && r.Massifs && r.Kind == ValleyKind.Forest;
        }
        check("a save tells whether its valley was made with it, and one of before is made again without", told, told ? "right" : "wrong");
        check("it makes a valley little slower to make", withMs < withoutMs * 1.12 + 200, $"{withMs:0} ms of work with it, {withoutMs:0} ms without, four valleys");
        check("it makes a valley little dearer to draw", qb < qa * 1.06, $"{qb} faces with it, {qa} without (+{(qb - qa) * 100.0 / qa:0.0} %), the mountains' valley on seed 5");
        Console.WriteLine($"     ({sw.ElapsedMilliseconds / 1000.0:0.0} s)");
    }

    /// <summary>The hashes of the ground and of every voxel of a few valleys of each kind made with it off (WILDS_HASH=1).</summary>
    static void Hashes()
    {
        foreach (ValleyKind kind in Enum.GetValues(typeof(ValleyKind)))
            foreach (uint seed in new uint[] { 1, 9 })
                foreach (bool massifs in new[] { false, true })
                {
                    if (kind != ValleyKind.Mountain && !massifs) continue;
                    var (w, s, ms) = Make(seed, kind, wilds: false, massifs: massifs);
                    var (g, v) = MountainsBench.Hashes(w, s);
                    Console.WriteLine($"hash {Valleys.Key(kind)} seed {seed}{(kind == ValleyKind.Mountain ? massifs ? " massifs" : " knolls" : "")}: ground {g:x16} world {v:x16} ({ms:0} ms)");
                }
    }
}
