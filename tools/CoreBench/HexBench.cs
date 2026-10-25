using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The valley a hexagon, made by itself, and the kinds of valley (--hex): nothing made, walkable or buildable off the
/// land; the citadel in the middle on level ground; the river in over one side and out over another, the roads in from
/// the horde's side and out at the far one; the horde in over its side, whichever side that is; one thrown off the edge
/// gone; the water at the edge standing; the beasts keeping off the edge; two neighbours meeting along the side they
/// share; no building of the town over the edge; each kind with its places and its ground; the same seed the same
/// valley; the kind, the size and the horde's side kept in a save.
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
        // a voxel's place packed into one number keeps it apart from every other across the game's valley and wider
        // (it was 10 bits of x and z, and the valley of 1536 folded its far thirds onto the rest)
        var seen = new HashSet<int>();
        bool round = true;
        int[] xs = { 0, 1, 1023, 1024, 1535, 2047 }, ys = { 0, 1, 191, 511 };
        foreach (int x in xs) foreach (int z in xs) foreach (int y in ys)
        {
            int p = WaterPlanner.Pack(x, y, z);
            round &= WaterPlanner.Unpack(p) == (x, y, z) && p >= 0;
            seen.Add(p);
        }
        check("a voxel's place packed into a number comes back whole and apart from every other up to 2048 across and 512 high",
            round && seen.Count == xs.Length * xs.Length * ys.Length, $"{seen.Count} of {xs.Length * xs.Length * ys.Length} apart, round trip {round}");
        Console.WriteLine();
        // the game's valley, in the game's box
        var mp = new MapParams { Hexagon = true, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize };
        (VoxelWorld w, Site s) Make(ValleyKind kind = ValleyKind.First, bool classic = true, int entry = Hex.West, uint seed = 0, HexEdge[] edges = null)
        {
            var wk = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
            var sk = MapGen.Generate(wk, new MapParams
            {
                Seed = seed == 0 ? mp.Seed : seed, Kind = kind, Classic = classic, Hexagon = true, EntrySide = entry, Edges = edges,
                SizeX = mp.SizeX, SizeZ = mp.SizeZ,
            });
            return (wk, sk);
        }
        var sw = Stopwatch.StartNew();
        var (w, site) = Make();
        long made = sw.ElapsedMilliseconds;
        w.Compact();
        var hex = w.Land;
        Console.WriteLine($"the game's valley {w.SX}×{w.SZ} made in {made} ms");

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

        // the citadel in the middle, on level ground to build on all round it
        (int lo, int hi, int wet) Round(VoxelWorld wk, Site sk, int r0, int r1)
        {
            int lo = int.MaxValue, hi = int.MinValue, wet = 0;
            for (int z = sk.KeepZ - r1; z <= sk.KeepZ + r1; z += 2)
                for (int x = sk.KeepX - r1; x <= sk.KeepX + r1; x += 2)
                {
                    double d = Math.Sqrt((x - sk.KeepX) * (x - sk.KeepX) + (z - sk.KeepZ) * (z - sk.KeepZ));
                    if (d < r0 || d > r1) continue;
                    int h = sk.HTop[z * wk.SX + x];
                    lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                    if (sk.WaterTop[z * wk.SX + x] > 0) wet++;
                }
            return (lo, hi, wet);
        }
        var level = Round(w, site, site.KeepR + 40, 18 * VoxelWorld.PerMetre);
        check("the citadel stands in the middle of the hexagon, on level dry ground 18 m round it",
            Math.Abs(site.KeepX - hex.CX) <= 1 && Math.Abs(site.KeepZ - hex.CZ) <= 1 && level.hi - level.lo <= 6 && level.wet == 0,
            $"citadel at {site.KeepX},{site.KeepZ}, the middle {hex.CX:0},{hex.CZ:0}; the ground round it {level.lo}…{level.hi}, {level.wet} columns of water");

        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        void Run(Sim sm, double seconds) { for (double t = 0; t < seconds; t += .05) sm.Step(.05f); }

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

        // a building of the town is not put up over the edge, half of it on nothing
        var barracks = Sim.TownKindOf("barracks");
        int ex = (int)(hex.CX + hex.HalfW), ez = (int)hex.CZ + 120;
        var overEdge = sim.BuildingPlan(barracks, ex - 6, ez, false, out _, out string whyEdge, out _);
        sim.BuildingPlan(barracks, ex - 90, ez, false, out _, out string whyIn, out _);
        check("a building of the town is not put up over the valley's edge, and is a little way in", overEdge == null && whyEdge == "здесь край долины" && whyIn != "здесь край долины",
            $"at the edge «{whyEdge}», 90 voxels in «{whyIn ?? "fits"}»");

        // the river comes in over one side and goes out over another; the roads come in over the horde's side and go
        // out over the far one
        HashSet<int> SidesWhere(VoxelWorld wk, Site sk, Func<int, int, bool> at)
        {
            var sides = new HashSet<int>();
            for (int z = 0; z < wk.SZ; z += 2)
                for (int x = 0; x < wk.SX; x += 2)
                    if (wk.OnLand(x, z) && sk.Hex.Edge(x + .5f, z + .5f) < 4 && at(x, z)) sides.Add(sk.Hex.NearestSide(x + .5f, z + .5f));
            return sides;
        }
        bool River(Site sk, int x, int z) => sk.WaterTop[z * sk.SX + x] > 0 && sk.RiverAt(x + .5f, z + .5f, out float o, out float h, out _, out _) && o < h + 2;
        bool Road(Site sk, int x, int z) => sk.RoadDist(x + .5f, z + .5f) < 4;
        var riverSides = SidesWhere(w, site, (x, z) => River(site, x, z));
        var roadSides = SidesWhere(w, site, (x, z) => Road(site, x, z));
        check("the river comes in over one side of the hexagon and goes out over another; the roads come in over the horde's side and go out over the far one",
            riverSides.Count >= 2 && roadSides.Contains(site.EntrySide) && roadSides.Contains((site.EntrySide + 3) % 6),
            $"river at the sides {string.Join(",", riverSides.OrderBy(i => i))}; roads at {string.Join(",", roadSides.OrderBy(i => i))}; the horde's side {site.EntrySide}");

        // the water at the edge stands: a cut from the river out to the land's side, let run a while, and the water at
        // the edge is still there
        int Water()
        {
            int n = 0;
            for (int z = 0; z < w.SZ; z += 2)
                for (int x = 0; x < w.SX; x += 2)
                    if (w.OnLand(x, z) && hex.Edge(x + .5f, z + .5f) < 6)
                        for (int y = 1; y < 48; y++) if (w.Get(x, y, z) == Mat.Water) n++;
            return n;
        }
        int waterBefore = Water();
        (int x, int z) near = (-1, -1); float nearIn = 1e9f;
        for (int z = 0; z < w.SZ; z += 2)
            for (int x = 0; x < w.SX; x += 2)
            {
                float e = hex.Edge(x + .5f, z + .5f);
                if (e >= 24 && e < nearIn && River(site, x, z)) { nearIn = e; near = (x, z); }
            }
        if (near.x >= 0)
        {
            var (ix, iz) = hex.Inward(hex.NearestSide(near.x, near.z));
            for (float s = 0; s < nearIn + 4; s += .5f)
                for (int a = -3; a <= 3; a++)
                {
                    int x = (int)(near.x - ix * s - iz * a), z = (int)(near.z - iz * s + ix * a);
                    if (!w.OnLand(x, z)) continue;
                    for (int y = MapGen.RiverLevel - 4; y < MapGen.RiverLevel + 8; y++) w.Set(x, y, z, Mat.Air);
                }
        }
        Run(sim, 20);
        int waterAfter = Water();
        check("the water at the land's edge stays in the valley: none runs out over the side", near.x >= 0 && waterAfter >= waterBefore * .9,
            $"cut from the river at {near.x},{near.z}; water at the edge {waterBefore} → {waterAfter}");

        // the horde comes in over its side, all along it, and a raider called up stands there
        bool OnSide(Site sk, float x, float z, int side) { var (d, t) = sk.Hex.FromSide(side, x, z); return d < 48 && t > -.02f && t < 1.02f; }
        List<(int x, int z)> Entries(Sim sm, Site sk)
        {
            var list = new List<(int x, int z)>();
            for (int i = 0; i <= 6; i++)
            {
                var (x, z) = sk.Entry(i / 6.0);
                int cell = sm.NearestStand(x, z, 3);
                if (cell >= 0) { var (fx, _, fz) = sm.Nav.Feet(cell); list.Add(((int)fx, (int)fz)); }
            }
            return list;
        }
        var entries = Entries(sim, site);
        var raider = sim.SpawnRaider(Kinds.Goblin);
        check("the horde comes in over its side of the hexagon (the west), all along it, and a raider called up stands there",
            entries.Count == 7 && entries.All(e => OnSide(site, e.x, e.z, site.EntrySide)) && raider != null && OnSide(site, raider.X, raider.Z, site.EntrySide) && hex.Edge(raider.X, raider.Z) > -6,
            string.Join(" ", entries.Select(e => $"{e.x},{e.z}")) + (raider == null ? "; no raider" : $"; the raider at {raider.X:0},{raider.Z:0}"));

        // one thrown off the edge goes down past the valley's side, and is gone
        var thrown = sim.Spawn(Kinds.Orc, sim.NearestStand((int)hex.WestX + 20, (int)hex.CZ, 2));
        sim.Fling(thrown, -12 * VoxelWorld.PerMetre, 4 * VoxelWorld.PerMetre, 0, "взрыв");
        Run(sim, 6);
        check("one thrown off the land's edge is gone over it", thrown.Dead && thrown.KilledBy.Length > 0, $"dead {thrown.Dead}, {thrown.KilledBy}; at {thrown.X:0},{thrown.Z:0}");

        // the beasts keep off the edge: their herds are set well in from it, and one bolting from a goblin toward the
        // edge turns along it and stays clear of it
        {
            var (wb, sb) = Make();
            wb.Compact();
            var sim2 = new Sim(wb, sb);
            sim2.Waves.Countdown = 1e9;
            sim2.SeedWildlife(16);
            var beasts = sim2.Units.Where(u => u.Team == Team.Wild && !u.Dead).ToList();
            float homeMin = beasts.Count == 0 ? 0 : beasts.Min(u => { var (hx, _, hz) = sim2.Nav.Feet(u.Home); return hex.Edge(hx, hz); });
            // a deer 6 m in from the north-western side, and a goblin come up behind it from inland
            var (dx0, dz0) = sb.Hex.SidePoint(Hex.NorthWest, .5f, 48);
            var (nx, nz) = sb.Hex.Inward(Hex.NorthWest);
            var deer = sim2.Spawn(Kinds.Deer, sim2.NearestStand((int)dx0, (int)dz0, Kinds.Deer.Move.Height));
            deer.Home = deer.Cell;
            var goblin = sim2.Spawn(Kinds.Goblin, sim2.NearestStand((int)(dx0 + nx * 40), (int)(dz0 + nz * 40), 2));
            goblin.Guard = true; goblin.Home = goblin.Cell;
            float closest = 1e9f, closestHerd = 1e9f;
            for (int sec = 0; sec < 90; sec++)
            {
                Run(sim2, 1);
                if (!deer.Dead) closest = Math.Min(closest, hex.Edge(deer.X, deer.Z));
                foreach (var u in beasts) if (!u.Dead) closestHerd = Math.Min(closestHerd, hex.Edge(u.X, u.Z));
                if (sec == 20) { goblin.Dead = true; goblin.DiedAt = -999; }
            }
            check("the beasts keep off the land's edge: herds are set well in from it, they graze clear of it, and a deer bolting toward it turns along it",
                beasts.Count >= 20 && homeMin >= 40 && closestHerd >= 12 && closest >= 12 && !deer.Dead,
                $"{beasts.Count} beasts, the nearest home {homeMin / 8:0.0} m from the edge, the nearest a herd came {closestHerd / 8:0.0} m; the deer came {closest / 8:0.0} m, last {hex.Edge(deer.X, deer.Z) / 8:0.0} m");
        }

        // the horde's side another: from the east the horde comes in, up its own road, under the plateau's long cliff,
        // and the raids drawn up for the siege come in by it
        {
            var (we, se) = Make(entry: Hex.East);
            we.Compact();
            var sim3 = new Sim(we, se);
            sim3.Waves.Countdown = 1e9; sim3.WildTarget = 0;
            var east = Entries(sim3, se);
            var rE = sim3.SpawnRaider(Kinds.Goblin);
            sim3.PlanRaid(3, 1);
            var planned = sim3.Bands.Where(b => b.State == BandState.Coming && b.Entry >= 0).ToList();
            bool bandsEast = planned.Count > 0 && planned.All(b => { var (fx, _, fz) = sim3.Nav.Feet(b.Entry); return OnSide(se, fx, fz, Hex.East); });
            var roadsE = SidesWhere(we, se, (x, z) => Road(se, x, z));
            // the lowland under the cliff on the horde's side, the plateau on the other
            var (ix, iz) = se.Hex.Inward(Hex.East);
            int under = se.HTop[(int)(se.KeepZ - iz * 290) * we.SX + (int)(se.KeepX - ix * 290)], top = se.HTop[se.KeepZ * we.SX + se.KeepX + 150];
            check("the horde's side may be any: from the east it comes in all along that side, by its road, under the plateau's cliff, and the siege's raids are drawn up to come by it",
                Math.Abs(se.KeepX - se.Hex.CX) <= 1 && east.Count == 7 && east.All(e => OnSide(se, e.x, e.z, Hex.East)) && rE != null && OnSide(se, rE.X, rE.Z, Hex.East)
                    && bandsEast && roadsE.Contains(Hex.East) && roadsE.Contains(Hex.West) && under < top - 16,
                $"entries {string.Join(" ", east.Select(e => $"{e.x},{e.z}"))}; raider {(rE == null ? "none" : $"{rE.X:0},{rE.Z:0}")}; {planned.Count} bands drawn, all by the east {bandsEast}; roads at {string.Join(",", roadsE.OrderBy(i => i))}; under the cliff {under}, the plateau {top}");
        }

        // two neighbours meet along the side they share: the north-eastern side of one is the south-western of the
        // other, from the same seed, and its heights are the same both sides of it
        {
            HexEdge[] Sides(uint salt, int shared, uint sharedSeed, int r0, int r1)
            {
                var e = new HexEdge[6];
                for (int i = 0; i < 6; i++) e[i] = new HexEdge { Seed = salt * 1000 + (uint)i };
                e[shared].Seed = sharedSeed;
                e[r0].River = .5f; e[r1].River = .5f;
                return e;
            }
            var (wa, sa) = Make(seed: 11, edges: Sides(11, Hex.NorthEast, 4242, Hex.NorthWest, Hex.SouthWest));
            var (wb2, sb2) = Make(ValleyKind.Mountain, seed: 12, edges: Sides(12, Hex.SouthWest, 4242, Hex.East, Hex.NorthWest));
            int same = 0, all = 0; double worst = 0;
            for (int i = 1; i < 100; i++)
            {
                float t = i / 100f;
                var (ax, az) = sa.Hex.SidePoint(Hex.NorthEast, t, 1);
                var (bx, bz) = sb2.Hex.SidePoint(Hex.SouthWest, 1 - t, 1);
                int ha = sa.HTop[(int)az * wa.SX + (int)ax], hb = sb2.HTop[(int)bz * wb2.SX + (int)bx];
                all++;
                if (Math.Abs(ha - hb) <= 2) same++;
                worst = Math.Max(worst, Math.Abs(ha - hb));
            }
            check("two neighbouring valleys, of different seeds and kinds, meet along the side they share without a step",
                same >= all * .95, $"{same} of {all} points along it within 2 voxels of each other, the worst {worst:0}");
        }

        // the kinds: each its places
        var named = site.Landmarks.Select(l => l.Name).ToHashSet();
        check("the first valley keeps every place it always had while the old set is kept", OldPlaces.All(named.Contains),
            $"missing: {string.Join(", ", OldPlaces.Where(n => !named.Contains(n)))}");
        HashSet<string> PlacesOf(ValleyKind k, bool classic) => Make(k, classic).s.Landmarks.Select(l => l.Name).ToHashSet();
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

        // the kinds' ground: the forest thicker, the mountains rockier and higher, the marsh wetter and lower
        (int trees, int rock, int wet, double high) Ground(ValleyKind k)
        {
            var (wk, sk) = Make(k);
            int trees = 0, rock = 0, wet = 0, n = 0; double sum = 0;
            for (int z = 0; z < wk.SZ; z += 4)
                for (int x = 0; x < wk.SX; x += 4)
                {
                    if (!wk.OnLand(x, z)) continue;
                    int top = wk.ColTop(x, z);
                    if (top <= 0) continue;
                    byte m = wk.Get(x, top - 1, z);
                    if (Mat.IsLeafy(m)) trees++;
                    if (m == Mat.Rock) rock++;
                    if (sk.WaterTop[z * wk.SX + x] > 0) wet++;
                    sum += sk.HTop[z * wk.SX + x]; n++;
                }
            return (trees, rock, wet, sum / Math.Max(1, n));
        }
        var g0 = Ground(ValleyKind.First); var gf = Ground(ValleyKind.Forest); var gm = Ground(ValleyKind.Mountain); var gw = Ground(ValleyKind.Marsh);
        check("a forest valley has more woods than the first, a mountain one more bare rock and higher ground, a marsh more water and lower",
            gf.trees > g0.trees * 1.25 && gm.rock > g0.rock * 1.25 && gm.high > g0.high + 2 && gw.wet > g0.wet * 1.25 && gw.high < g0.high - 2,
            $"woods {g0.trees} → {gf.trees} in the forest; rock {g0.rock} → {gm.rock}, ground {g0.high:0.0} → {gm.high:0.0} in the mountains; water {g0.wet} → {gw.wet}, ground {gw.high:0.0} in the marsh");

        // the same seed makes the same valley, another seed another
        long Print(Site sk)
        {
            long h = 17;
            for (int i = 0; i < sk.HTop.Length; i += 97) h = h * 31 + sk.HTop[i] + sk.WaterTop[i] * 7;
            return h;
        }
        long p0 = Print(site), p1 = Print(Make().s), p2 = Print(Make(seed: 8).s);
        check("the same seed makes the same valley, another seed another", p0 == p1 && p0 != p2, $"{p0:x} {p1:x} {p2:x}");

        // the kind, the size and the horde's side are kept in a save, and the valley made again from them
        var (wo, so) = Make(ValleyKind.Ore, entry: Hex.SouthEast);
        wo.Compact();
        var simO = new Sim(wo, so);
        var ms = new System.IO.MemoryStream();
        simO.Save(ms, mp.Seed, "test", "");
        ms.Position = 0;
        using (var reader = new SaveReader(ms))
            check("a save keeps the valley's kind, shape, size and the horde's side",
                reader.Kind == ValleyKind.Ore && reader.Classic && reader.Hexagon && reader.SizeX == MapParams.GameSize && reader.SizeZ == MapParams.GameSize && reader.EntrySide == Hex.SouthEast,
                $"{reader.Kind}, classic {reader.Classic}, hexagon {reader.Hexagon}, {reader.SizeX}×{reader.SizeZ}, the horde's side {reader.EntrySide}");
    }

    /// <summary>
    /// Valleys of a few seeds and kinds, each from another side (--seeds): whole — the citadel high on its plateau, no
    /// water standing open against the air, the ponds, the river across, every outcrop of ore its kind has.
    /// </summary>
    public static void Seeds(Action<string, bool, string> check)
    {
        var kinds = new[] { ValleyKind.First, ValleyKind.Marsh, ValleyKind.Mountain, ValleyKind.Forest };
        for (uint seed = 1; seed <= 4; seed++)
        {
            var kind = kinds[seed - 1];
            int entry = (int)(seed * 2 + 2) % 6;
            var mps = new MapParams { Seed = seed, Kind = kind, Hexagon = true, EntrySide = entry, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize };
            var sw = Stopwatch.StartNew();
            var ws = new VoxelWorld(mps.SizeX, mps.SizeY, mps.SizeZ);
            var ss = MapGen.Generate(ws, mps);
            long ms = sw.ElapsedMilliseconds;
            int open = 0, river = 0;
            for (int z = 1; z < ws.SZ - 1; z++)
                for (int x = 1; x < ws.SX - 1; x++)
                {
                    int i = z * ws.SX + x;
                    for (int y = ss.HTop[i]; y < ss.WaterTop[i]; y++)
                        foreach (var (sx, sz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                            if (ws.Get(x + sx, y, z + sz) == Mat.Air && ws.OnLand(x + sx, z + sz)) open++;
                    if (ss.WaterTop[i] > 0 && ws.OnLand(x, z) && ss.Hex.Edge(x + .5f, z + .5f) < 3) river++;
                }
            int outcrops = Valleys.OreOf(kind).outcrops;
            check($"seed {seed}, {kind}, the horde from side {entry}: a whole valley, its outcrops of ore all found room", ss.KeepBase > MapGen.RiverLevel + 24 && open == 0 && ss.Ponds.Count == 2 && river > 0 && ss.Outcrops.Count == outcrops,
                $"made in {ms} ms; keep base {ss.KeepBase}, {open} open water faces, {ss.Ponds.Count} ponds, {ss.Lakes.Count} lakes, {river} columns of water at the edge, {ss.Outcrops.Count} of {outcrops} outcrops");
        }
    }
}
