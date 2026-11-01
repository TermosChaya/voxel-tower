using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The mountains of a mountain valley are mountains (--mountains, MapGen.Mountains.cs). On eight seeds: no ground stands
/// against the top of the world (the knolls of before stood cut flat against it, hundreds of columns of them in every
/// valley); every summit comes to a point; a summit is no cone: going down it the rock breaks in ledges with cliffs
/// between them (a cone's flank runs down smooth, the knolls' did on every way down); several summits to a valley, of
/// different heights; the valley made in good time. The silver mine stands at the foot of the mountains, got to on foot
/// from the citadel's gate, and so does every side of five more seeds (the reach bench walks three). A valley made with
/// the massifs off is made with the knolls of before, and a save carries which it was made with. MOUNTAINS_PROBE=1
/// prints the measures of valleys of both kinds of mountains instead (the spurs round each summit too), MOUNTAINS_DEBUG=1
/// the summits that are flat or smooth, MOUNTAINS_HASH=1 the hashes of the ground of a few valleys of each kind besides
/// (to tell a change of the generator from none).
/// </summary>
static class MountainsBench
{
    /// <summary>A summit: where it is, how high, how many columns stand within a voxel of its top about it, how far it
    /// stands over the lowest ground about it, its spurs round it and the share of the ways down it that break in a ledge.</summary>
    public readonly record struct Summit(int X, int Z, int H, int Flat, int Rise, int Spurs, double Ledges);

    /// <summary>What a valley's mountains measure.</summary>
    public sealed class Measures
    {
        public int Top, AtCeiling, Land;
        public readonly List<Summit> Summits = new();
        /// <summary>The summits of the mountains: over the highest of the lowland (135), standing 50 voxels and more over the ground about them.</summary>
        public List<Summit> Great => Summits.FindAll(s => s.Rise >= 50 && s.H >= 135);
        public int Flattest { get { int f = 0; foreach (var t in Great) f = Math.Max(f, t.Flat); return f; } }
        public int Spread
        {
            get
            {
                var g = Great;
                if (g.Count == 0) return 0;
                int a = int.MinValue, b = int.MaxValue;
                foreach (var t in g) { a = Math.Max(a, t.H); b = Math.Min(b, t.H); }
                return a - b;
            }
        }
        static double Median(List<double> l) { if (l.Count == 0) return 0; l.Sort(); return l[l.Count / 2]; }
        public double SpursMedian => Median(Great.ConvertAll(s => (double)s.Spurs));
        public double LedgesMedian => Median(Great.ConvertAll(s => s.Ledges));
    }

    public static (VoxelWorld w, Site s, double ms) Make(uint seed, ValleyKind kind, bool massifs = true)
    {
        var p = new MapParams { Seed = seed, Kind = kind, Hexagon = true, EntrySide = Hex.West, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize, Massifs = massifs };
        var w = new VoxelWorld(p.SizeX, p.SizeY, p.SizeZ);
        var sw = Stopwatch.StartNew();
        var s = MapGen.Generate(w, p);
        return (w, s, sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>Measures a valley's mountains from the heights of its ground as the world holds them (none over its top).</summary>
    public static Measures Measure(VoxelWorld w, Site s)
    {
        var m = new Measures();
        int SX = w.SX, SZ = w.SZ, SY = w.SY;
        var h = new int[SX * SZ];
        for (int i = 0; i < h.Length; i++) h[i] = Math.Min(s.HTop[i], SY);
        bool Land(int x, int z) => x >= 0 && z >= 0 && x < SX && z < SZ && w.OnLand(x, z) && (s.Hex == null || s.Hex.Edge(x + .5f, z + .5f) >= 4);
        int H(int x, int z) => h[z * SX + x];
        for (int z = 0; z < SZ; z++)
            for (int x = 0; x < SX; x++)
            {
                if (!Land(x, z)) continue;
                m.Land++;
                m.Top = Math.Max(m.Top, s.HTop[z * SX + x]);
                if (s.HTop[z * SX + x] >= SY - 2) m.AtCeiling++;
            }
        // the heights in blocks of 4 × 4, the highest and the lowest of each
        const int B = 4;
        int bx = SX / B, bz = SZ / B;
        var hi = new int[bx * bz]; var lo = new int[bx * bz];
        for (int k = 0; k < bz; k++)
            for (int i = 0; i < bx; i++)
            {
                int a = int.MinValue, b = int.MaxValue;
                for (int z = k * B; z < k * B + B; z++)
                    for (int x = i * B; x < i * B + B; x++)
                        if (Land(x, z)) { a = Math.Max(a, H(x, z)); b = Math.Min(b, H(x, z)); }
                hi[k * bx + i] = a; lo[k * bx + i] = b;
            }
        // the summits: blocks higher than every other within 20 voxels, falling away 4 voxels and more every way 16 out
        // (not the brink of the plateau), standing 35 and more over the lowest within 60
        for (int k = 0; k < bz; k++)
            for (int i = 0; i < bx; i++)
            {
                int v = hi[k * bx + i];
                if (v == int.MinValue) continue;
                bool top = true;
                for (int dk = -5; dk <= 5 && top; dk++)
                    for (int di = -5; di <= 5 && top; di++)
                    {
                        if (di == 0 && dk == 0) continue;
                        int ii = i + di, kk = k + dk;
                        if (ii < 0 || kk < 0 || ii >= bx || kk >= bz) continue;
                        int o = hi[kk * bx + ii];
                        if (o > v || o == v && (dk < 0 || dk == 0 && di < 0)) top = false;
                    }
                if (!top) continue;
                int sx = i * B, sz = k * B;
                for (int z = k * B; z < k * B + B; z++)
                    for (int x = i * B; x < i * B + B; x++)
                        if (Land(x, z) && H(x, z) == v) { sx = x; sz = z; }
                bool falls = true;
                for (int a = 0; a < 8 && falls; a++)
                {
                    int x = sx + (int)Math.Round(Math.Cos(a * Math.PI / 4) * 16), z = sz + (int)Math.Round(Math.Sin(a * Math.PI / 4) * 16);
                    if (Land(x, z) && v - H(x, z) < 4) falls = false;
                }
                int least = int.MaxValue;
                for (int dk = -15; dk <= 15; dk += 3)
                    for (int di = -15; di <= 15; di += 3)
                    {
                        int ii = i + di, kk = k + dk;
                        if (ii < 0 || kk < 0 || ii >= bx || kk >= bz || lo[kk * bx + ii] == int.MaxValue) continue;
                        least = Math.Min(least, lo[kk * bx + ii]);
                    }
                // (a summit cut flat against the top falls away nowhere near it: it is counted, its flat top with it)
                if (!falls && v < SY - 2 || v - least < 35) continue;
                int flat = 0;
                for (int dz = -8; dz <= 8; dz++)
                    for (int dx = -8; dx <= 8; dx++)
                        if (Land(sx + dx, sz + dz) && H(sx + dx, sz + dz) >= v - 1) flat++;
                // how far down a third of its height it reaches each way, and round it there its spurs: crests standing 5
                // voxels and more over the hollows either side
                int level = v - (v - least) / 3;
                var reach = new double[36];
                for (int a = 0; a < 36; a++)
                {
                    double ang = a * Math.PI / 18, dx = Math.Cos(ang), dz = Math.Sin(ang);
                    int r = 1;
                    for (; r < 220; r++)
                    {
                        int x = sx + (int)Math.Round(dx * r), z = sz + (int)Math.Round(dz * r);
                        if (!Land(x, z) || H(x, z) < level) break;
                    }
                    reach[a] = r;
                }
                Array.Sort(reach);
                double ring = reach[18] * .8;
                var around = new double[72];
                for (int a = 0; a < 72; a++)
                {
                    double ang = a * Math.PI / 36;
                    int x = sx + (int)Math.Round(Math.Cos(ang) * ring), z = sz + (int)Math.Round(Math.Sin(ang) * ring);
                    around[a] = Land(x, z) ? H(x, z) : 0;
                }
                int spurs = 0;
                for (int a = 0; a < 72; a++)
                {
                    double c = (around[(a + 71) % 72] + around[a] + around[(a + 1) % 72]) / 3;
                    bool crest = true;
                    double low = double.MaxValue;
                    for (int d = -9; d <= 9; d++)
                    {
                        if (d == 0) continue;
                        int j = (a + d + 72) % 72;
                        double o = (around[(j + 71) % 72] + around[j] + around[(j + 1) % 72]) / 3;
                        if (Math.Abs(d) <= 3 && o > c) crest = false;
                        low = Math.Min(low, o);
                    }
                    if (crest && c - low >= 5) spurs++;
                }
                // going down it each of 24 ways two thirds of the way to the ground about it: where the ground lies nearly level for a few voxels between
                // stretches as steep as a roof, a ledge
                int broken = 0, ways = 0;
                for (int a = 0; a < 24; a++)
                {
                    double ang = a * Math.PI / 12, dx = Math.Cos(ang), dz = Math.Sin(ang);
                    var down = new List<int>();
                    for (int r = 0; r < 220; r++)
                    {
                        int x = sx + (int)Math.Round(dx * r), z = sz + (int)Math.Round(dz * r);
                        if (!Land(x, z)) break;
                        down.Add(H(x, z));
                        if (H(x, z) < v - (v - least) * 2 / 3) break;
                    }
                    if (down.Count < 24) continue;
                    ways++;
                    double Slope(int r) => r < 0 || r + 4 >= down.Count ? double.NaN : (down[r] - down[r + 4]) / 4.0;
                    for (int r = 6; r + 10 < down.Count; r++)
                    {
                        double here = Slope(r), above = Slope(r - 6), below = Slope(r + 6);
                        if (here <= .3 && above >= .8 && below >= .8) { broken++; break; }
                    }
                }
                m.Summits.Add(new Summit(sx, sz, v, flat, v - least, spurs, ways > 0 ? broken / (double)ways : 0));
            }
        return m;
    }

    /// <summary>A hash of the ground's heights, and one of every voxel of the world.</summary>
    public static (ulong ground, ulong world) Hashes(VoxelWorld w, Site s)
    {
        ulong a = 1469598103934665603UL, b = a;
        foreach (int v in s.HTop) { a ^= (uint)v; a *= 1099511628211UL; }
        for (int ci = 0; ci < w.ChunkCount; ci++)
        {
            var d = w.ChunkData(ci);
            if (d == null) { b ^= (ulong)(ci * 31 + w.ChunkUniform(ci)); b *= 1099511628211UL; continue; }
            foreach (byte v in d) { b ^= v; b *= 1099511628211UL; }
        }
        return (a, b);
    }

    static string Tell(Measures m) =>
        $"top {m.Top}, {m.AtCeiling} columns at the top of the world; {m.Great.Count} great summits of {m.Summits.Count}, " +
        $"heights {string.Join(" ", m.Great.ConvertAll(t => t.H))}; flattest top {m.Flattest}; spurs {string.Join(" ", m.Great.ConvertAll(t => t.Spurs))} " +
        $"(median {m.SpursMedian:0}); ledges {string.Join(" ", m.Great.ConvertAll(t => t.Ledges.ToString("0.00")))} (median {m.LedgesMedian:0.00})";

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        if (Environment.GetEnvironmentVariable("MOUNTAINS_PROBE") == "1") { Probe(); return; }
        var sw = Stopwatch.StartNew();
        // eight valleys of the mountains
        var ceiling = new List<string>(); var blunt = new List<string>(); var smooth = new List<string>(); var few = new List<string>();
        var times = new List<double>();
        var mine = new List<string>();
        int mines = 0;
        for (uint seed = 1; seed <= 8; seed++)
        {
            var (w, s, ms) = Make(seed, ValleyKind.Mountain);
            times.Add(ms);
            var m = Measure(w, s);
            if (Environment.GetEnvironmentVariable("MOUNTAINS_SAY") == "1") Console.WriteLine($"       seed {seed}: {Tell(m)}; made in {ms:0} ms");
            if (m.AtCeiling > 0 || m.Top > w.SY - 8) ceiling.Add($"seed {seed}: top {m.Top}, {m.AtCeiling} columns at the top");
            // (a plateau: most of the ground within 8 voxels of the top within a voxel of it; a crest running level along a
            // ridge is a good deal less, the knolls cut flat against the top all of it)
            if (m.Flattest > 170) blunt.Add($"seed {seed}: {m.Flattest} columns of 289 within 8 voxels of a summit within a voxel of its top");
            if (m.LedgesMedian < .25) smooth.Add($"seed {seed}: ledges on {m.LedgesMedian * 100:0} % of the ways down");
            var tops = m.Summits.FindAll(t => t.Rise >= 50);
            int spread = tops.Count == 0 ? 0 : tops.ConvertAll(t => t.H).Max() - tops.ConvertAll(t => t.H).Min();
            if (tops.Count < 4 || spread < 25) few.Add($"seed {seed}: {tops.Count} summits, heights {spread} apart");
            // the silver mine at the foot of the mountains, and got to from the gate
            var silver = s.Landmarks.Find(l => l.Name == "Серебряная жила");
            if (silver != null)
            {
                mines++;
                var sim = new Sim(w, s);
                sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
                int hgt = Kinds.Worker.Move.Height, gate = sim.NearestStand(s.KeepX - s.KeepR - 20, s.KeepZ, hgt), at = sim.NearestStand(silver.StandX, silver.StandZ, hgt);
                var u = at >= 0 ? sim.Spawn(Kinds.Worker, gate) : null;
                if (u == null || !sim.GoTo(u, at, adjacent: true, budget: 600000)) mine.Add($"seed {seed}: the mine at {silver.StandX},{silver.StandZ} not got to");
            }
        }
        check("the mountains of eight mountain valleys stand under the top of the world, none of their ground against it", ceiling.Count == 0,
            ceiling.Count == 0 ? "every one a few metres under it" : string.Join("; ", ceiling));
        check("every summit of them comes to a point or a crest, none of them flat", blunt.Count == 0, blunt.Count == 0 ? $"no top flat (the knolls of before: whole tops flat against the top of the world)" : string.Join("; ", blunt));
        check("a summit is no cone: going down it, the rock breaks in ledges with cliffs between them", smooth.Count == 0,
            smooth.Count == 0 ? "a ledge on a quarter and more of the ways down the middle summit of every valley (on none of the knolls of before)" : string.Join("; ", smooth));
        check("several summits to a valley, of different heights", few.Count == 0, few.Count == 0 ? "four and more to each, 25 voxels and more apart" : string.Join("; ", few));
        times.Sort();
        check("a mountain valley is made in good time", times[times.Count / 2] < 4000, $"{times[times.Count / 2]:0} ms the middle of eight (the knolls of before some 750 ms by themselves, 1400 under load)");
        check("the silver mine stands at the foot of the mountains, got to on foot from the gate", mine.Count == 0 && mines >= 6,
            mine.Count == 0 ? $"{mines} mines of 8 valleys got to" : string.Join("; ", mine));
        // every side of five more seeds reaches the citadel
        var cut = new List<string>();
        for (uint seed = 4; seed <= 8; seed++)
        {
            var c = ReachBench.Cut(ValleyKind.Mountain, seed, false);
            if (c.Count > 0) cut.Add($"seed {seed}: {string.Join(", ", c)}");
        }
        check("every side of five more mountain valleys reaches the citadel on foot", cut.Count == 0, cut.Count == 0 ? "all 90 places along their sides reach the gate" : string.Join("; ", cut));
        // the knolls of before, for a valley saved before the massifs; and a save tells which it was made with
        var (w0, s0, _) = Make(9, ValleyKind.Mountain, massifs: false);
        var (w1, s1, _) = Make(9, ValleyKind.Mountain);
        var m0 = Measure(w0, s0);
        bool told = true;
        foreach (var (w, s) in new[] { (w0, s0), (w1, s1) })
        {
            var sim = new Sim(w, s);
            using var ms = new MemoryStream();
            sim.Save(ms, 9, "test", "");
            ms.Position = 0;
            using var r = new SaveReader(ms);
            told &= r.Massifs == s.Massifs && r.Hexagon && r.Kind == ValleyKind.Mountain;
        }
        check("a valley made with the massifs off has the knolls of before, and a save tells which it was made with", m0.AtCeiling > 0 && !s0.Massifs && s1.Massifs && told,
            $"seed 9 with them off: {m0.AtCeiling} columns at the top of the world as before; the saves told {(told ? "right" : "wrong")}");
        Console.WriteLine($"     ({sw.ElapsedMilliseconds / 1000.0:0.0} s)");
    }

    static void Probe()
    {
        bool debug = Environment.GetEnvironmentVariable("MOUNTAINS_DEBUG") == "1";
        foreach (bool massifs in new[] { false, true })
            foreach (uint seed in new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 36 })
            {
                if (debug && !massifs) continue;
                var (w, s, ms) = Make(seed, ValleyKind.Mountain, massifs);
                var m = Measure(w, s);
                Console.WriteLine($"{(massifs ? "massifs" : "knolls")} seed {seed}: made in {ms:0} ms; {Tell(m)}");
                if (debug)
                    foreach (var t in m.Summits)
                        if (t.Flat > 40 || t.Ledges < .2 && t.Rise >= 50)
                            Console.WriteLine($"       summit at {t.X},{t.Z}: height {t.H}, over the ground about {t.Rise}, flat {t.Flat}, spurs {t.Spurs}, ledges {t.Ledges:0.00}; " +
                                $"{Math.Sqrt((t.X - s.KeepX) * (t.X - s.KeepX) + (t.Z - s.KeepZ) * (t.Z - s.KeepZ)):0} from the keep; landmark {s.Landmarks.Find(l => Math.Abs(l.X - t.X) < l.R + 10 && Math.Abs(l.Z - t.Z) < l.R + 10)?.Name ?? "-"}");
            }
        if (Environment.GetEnvironmentVariable("MOUNTAINS_HASH") != "1") return;
        foreach (ValleyKind kind in Enum.GetValues(typeof(ValleyKind)))
            foreach (uint seed in new uint[] { 1, 9 })
                foreach (bool massifs in new[] { false, true })
                {
                    if (kind != ValleyKind.Mountain && !massifs) continue;
                    var (w, s, ms) = Make(seed, kind, massifs);
                    var (g, v) = Hashes(w, s);
                    Console.WriteLine($"hash {Valleys.Key(kind)} seed {seed}{(kind == ValleyKind.Mountain ? massifs ? " massifs" : " knolls" : "")}: ground {g:x16} world {v:x16} ({ms:0} ms)");
                }
    }
}
