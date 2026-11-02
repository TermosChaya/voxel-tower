using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's --walkers: how our people walk and what the workers put first. On level ground made for it: one man
/// walked thirty metres over each kind of ground in turn (turf, a dirt way, cobbles, tall grass, moss, a bog, rubble,
/// shallow water) for the pace each keeps; a man across a field of tall grass with a road round two sides of it; a yard
/// walled three and a half metres high with a ladder up the inside of its far wall and its gate round the other side; a
/// ring of wall two metres high with a gap in it; a river waded or crossed by a bridge thirty metres off the straight
/// way. WALKERS_DEBUG=1 tells every way taken.
/// </summary>
static class WalkersBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        bool debug = Environment.GetEnvironmentVariable("WALKERS_DEBUG") == "1";
        // WALKERS_ONLY=ground, scaffolds or town: that part alone
        string only = Environment.GetEnvironmentVariable("WALKERS_ONLY");
        if (only == "scaffolds") { Scaffolds(check, debug); return; }
        if (only == "town") { Town(check, debug); return; }
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        // (the old quiet way with nothing to do, as the other staged scenes have it: Sim.Life.cs)
        var sim = new Sim(w, site) { QuietLife = true };
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        // the household at the gate kept out of it: these tests bring their own
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        Run(.1);
        var nav = sim.Nav;

        // ---- level ground to try it on, west of the citadel: grass on soil, nothing standing on it
        int y0 = Math.Clamp(w.SurfAt(site.KeepX - 300, site.KeepZ), 24, w.SY - 80);
        void Level(int x0, int z0, int x1, int z1, int y)
        {
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                {
                    if (!w.InBounds(x, 1, z)) continue;
                    // whole trees that stand on it go, so that nothing is left hanging
                    for (int yy = w.SurfAt(x, z), top = w.ColTop(x, z); yy < top; yy++)
                        if (Mat.IsTrunk(w.Get(x, yy, z))) { var tree = Shapes.Tree(w, x, yy, z); for (int i = 0; i < tree.Count; i++) w.Set(tree.X(i), tree.Y(i), tree.Z(i), Mat.Air); }
                    int high = Math.Max(w.ColTop(x, z), y) + 1;
                    for (int yy = Math.Max(1, y - 16); yy < high && yy < w.SY; yy++)
                    {
                        byte want = yy < y - 1 ? Mat.Soil : yy == y - 1 ? Mat.Grass : Mat.Air;
                        if (w.Get(x, yy, z) != want) w.Set(x, yy, z, want);
                    }
                    if (site.HTop != null) site.HTop[z * w.SX + x] = y;
                    if (site.WaterTop != null) site.WaterTop[z * w.SX + x] = 0;
                }
        }
        // the top of a column laid with something (a way's stuff, peat, rubble), or what grows over it
        void Top(int x0, int z0, int x1, int z1, Func<int, int, byte> mat)
        {
            for (int z = z0; z < z1; z++) for (int x = x0; x < x1; x++) w.Set(x, y0 - 1, z, mat(x, z));
        }
        void Grow(int x0, int z0, int x1, int z1, Func<int, int, byte> plant, int tall)
        {
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                {
                    byte p = plant(x, z);
                    if (p == 0) continue;
                    for (int yy = y0; yy < y0 + tall; yy++) w.Set(x, yy, z, p);
                }
        }
        int fx0 = site.KeepX - 560, fz0 = site.KeepZ - 240;
        Level(fx0 - 24, fz0 - 24, fx0 + 424, fz0 + 504, y0);
        Run(1);

        // a man of ours sent somewhere: the way planned for it, and how it went, step by step
        Unit Man(int x, int z)
        {
            int c = sim.NearestStand(x, z, 2);
            if (c < 0) throw new InvalidOperationException($"nowhere to stand by {x},{z}");
            return sim.Spawn(Kinds.Worker, c);
        }
        void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
        // walks it there by the player's word; the cells it went through, and the seconds it took (NaN: never got there)
        (List<int> cells, double secs) Walk(Unit u, int x, int z, double most = 240, Action<Unit> watch = null)
        {
            int target = sim.NearestStand(x, z, 2);
            var went = new List<int> { u.Cell };
            sim.Command(new[] { u }, target, stay: true);
            double t0 = sim.Time;
            int goal = u.Dest >= 0 ? u.Dest : target;
            while (sim.Time - t0 < most)
            {
                Run(.1);
                watch?.Invoke(u);
                if (went[^1] != u.Cell) went.Add(u.Cell);
                if (u.Cell == goal && (u.Path == null || u.PathI >= u.Path.Count)) return (went, sim.Time - t0);
            }
            return (went, double.NaN);
        }
        string Cells(List<int> cells) => string.Join(" ", cells.Select(c => $"{nav.I(c)},{nav.Y(c)},{nav.K(c)}:{nav.Floor[c]}{"OTtDGBPFMRSsnbW"[nav.Footing[c]]}{(nav.Ladder[c] != 0 ? "L" : "")}"));

        // ---- the pace on each kind of ground: thirty metres of it walked straight, the strip laid again for each, by the
        // same man each time (each of ours keeps a pace of his own)
        int sx0 = fx0, sz0 = fz0, len = 240;
        var paces = new Dictionary<string, double>();
        Unit pacer = null;
        void Strip(string name, Action lay)
        {
            Level(sx0 - 16, sz0 - 24, sx0 + len + 16, sz0 + 40, y0);
            lay();
            // walled in along both sides, so that the man keeps to it
            var side = new Stroke { Mat = Mat.Stone, Width = 4, Height = 24, Level = true };
            Lay(Shapes.Line(w, site, sx0 - 16, sz0 - 18, sx0 + len + 16, sz0 - 18, side, y0 + 24));
            Lay(Shapes.Line(w, site, sx0 - 16, sz0 + 34, sx0 + len + 16, sz0 + 34, side, y0 + 24));
            Lay(Shapes.Line(w, site, sx0 - 14, sz0 - 18, sx0 - 14, sz0 + 34, side, y0 + 24));
            Lay(Shapes.Line(w, site, sx0 + len + 14, sz0 - 18, sx0 + len + 14, sz0 + 34, side, y0 + 24));
            Run(.5);
            if (pacer == null) pacer = Man(sx0, sz0 + 8);
            else
            {
                int at = sim.NearestStand(sx0, sz0 + 8, 2);
                pacer.Cell = at; (pacer.X, pacer.Y, pacer.Z) = nav.Feet(at); pacer.Path = null;
            }
            // the time between two marks twenty metres apart along it, clear of the start and the stop
            double at40 = double.NaN, at200 = double.NaN;
            var (cells, secs) = Walk(pacer, sx0 + len, sz0 + 8, watch: u =>
            {
                if (double.IsNaN(at40) && u.X >= sx0 + 40) at40 = sim.Time;
                if (double.IsNaN(at200) && u.X >= sx0 + 200) at200 = sim.Time;
            });
            paces[name] = at200 - at40;
            if (debug) Console.WriteLine($"       {name}: {secs:0.0} s ({cells.Count} cells, going {string.Join(" ", cells.Select(c => (Footing)nav.Footing[c]).GroupBy(f => f).Select(g => $"{g.Key}×{g.Count()}"))}, pace {pacer.Pace:0.00})");
        }
        Strip("turf", () => { });
        Strip("dirt", () => Top(sx0 - 16, sz0 - 8, sx0 + len + 16, sz0 + 24, (x, z) => Mat.DirtPath));
        Strip("cobbles", () => Top(sx0 - 16, sz0 - 8, sx0 + len + 16, sz0 + 24, (x, z) => Sim.WayMat(WayKind.Cobble, x, z)));
        Strip("tall grass", () => Grow(sx0 - 16, sz0 - 16, sx0 + len + 16, sz0 + 32, (x, z) => (x * 7 + z * 3) % 5 < 3 ? Mat.Tuft : (x + z) % 7 == 0 ? Mat.Fern : (byte)0, 3));
        Strip("moss", () => Top(sx0 - 16, sz0 - 16, sx0 + len + 16, sz0 + 32, (x, z) => Mat.Moss));
        Strip("bog", () => Top(sx0 - 16, sz0 - 16, sx0 + len + 16, sz0 + 32, (x, z) => Mat.Peat));
        Strip("rubble", () => Top(sx0 - 16, sz0 - 16, sx0 + len + 16, sz0 + 32, (x, z) => Mat.Rubble));
        Strip("shallow water", () =>
        {
            for (int z = sz0 - 16; z < sz0 + 32; z++)
                for (int x = sx0 + 16; x < sx0 + len - 16; x++) { w.Set(x, y0 - 1, z, Mat.Water); w.Set(x, y0 - 2, z, Mat.Water); }
        });
        double turf = paces["turf"];
        string Rel(string k) => $"{k} {turf / paces[k]:0.00}";
        Console.WriteLine($"     pace against turf (20 m in {turf:0.0} s): {string.Join(", ", paces.Keys.Where(k => k != "turf").Select(Rel))}");
        check("a way is plainly faster than the turf beside it: a dirt way by a fifth and more, cobbles by a third",
            paces["dirt"] < turf / 1.2 && paces["cobbles"] < turf / 1.3,
            $"20 m of turf {turf:0.0} s, of a dirt way {paces["dirt"]:0.0} s, of cobbles {paces["cobbles"]:0.0} s");
        check("rough ground is slow going: tall grass and rubble a quarter slower than turf, a bog and shallow water a third and more",
            paces["tall grass"] > turf * 1.25 && paces["rubble"] > turf * 1.25 && paces["bog"] > turf * 1.5 && paces["shallow water"] > turf * 1.6 && paces["moss"] > turf * 1.1,
            string.Join(", ", paces.Select(p => $"{p.Key} {p.Value:0.0} s")));
        Gone(pacer);

        // ---- a field of tall grass, a road round two sides of it: across it is shorter, along the road quicker
        int ax = fx0 + 16, az = fz0 + 64, bx = fx0 + 256, bz = fz0 + 304;
        Level(fx0 - 16, fz0 + 40, fx0 + 288, fz0 + 336, y0);
        Grow(fx0 - 16, fz0 + 40, fx0 + 288, fz0 + 336, (x, z) => (x * 7 + z * 3) % 5 < 3 ? Mat.Tuft : (x + z) % 7 == 0 ? Mat.Fern : (byte)0, 3);
        // the road: from A east, then south to B (an L, 30 m and 30 m; the straight way across is 42 m)
        foreach (var (x0, z0, x1, z1) in new[] { (ax - 16, az - 16, bx + 16, az + 16), (bx - 16, az - 16, bx + 16, bz + 16) })
        {
            for (int z = z0; z < z1; z++) for (int x = x0; x < x1; x++) for (int yy = y0; yy < y0 + 3; yy++) w.Set(x, yy, z, Mat.Air);
            Top(x0, z0, x1, z1, (x, z) => Sim.WayMat(WayKind.Cobble, x, z));
        }
        Run(.5);
        var walker = Man(ax, az);
        var (road, roadSecs) = Walk(walker, bx, bz);
        int onRoad = road.Count(c => PathFinder.OnWay(nav, c));
        if (debug) Console.WriteLine($"       across the field: {roadSecs:0.0} s, {onRoad} of {road.Count} cells on the road: {Cells(road)}");
        check("a man going across a field of tall grass keeps to the road round two sides of it, the quicker way",
            onRoad >= road.Count * 7 / 10 && !double.IsNaN(roadSecs), $"{onRoad} of {road.Count} cells on the road, {roadSecs:0.0} s");
        Gone(walker);

        // ---- a yard walled three and a half metres high, its gate in the east wall, a ladder up the inside of the west
        // wall: a man by the ladder going out west goes round by the gate, not up the ladder and down off the wall
        int yx0 = fx0 + 32, yz0 = fz0 + 360, yx1 = yx0 + 192, yz1 = yz0 + 128;
        Level(yx0 - 120, yz0 - 40, yx1 + 64, yz1 + 40, y0);
        Run(.5);
        var stone = new Stroke { Mat = Mat.Stone, Width = 6, Height = 28, Level = true };
        void Lay(VoxelList L) { for (int i = 0; i < L.Count; i++) { w.Set(L.X(i), L.Y(i), L.Z(i), L.M(i)); sim.Remember(L.X(i), L.Y(i), L.Z(i), L.M(i)); } }
        Lay(Shapes.Line(w, site, yx0, yz0, yx1, yz0, stone, y0 + 28));
        Lay(Shapes.Line(w, site, yx1, yz0, yx1, yz1, stone, y0 + 28));
        Lay(Shapes.Line(w, site, yx1, yz1, yx0, yz1, stone, y0 + 28));
        Lay(Shapes.Line(w, site, yx0, yz1, yx0, yz0, stone, y0 + 28));
        Run(.5);
        int ym = (yz0 + yz1) / 2;
        int east = yx1 + 3;
        while (east > yx1 - 8 && !Mat.IsPlayer[w.Get(east, y0 + 6, ym)]) east--;
        var gp = GatePlan.At(w, site, new VoxelHit { X = east, Y = y0 + 6, Z = ym, NX = 1, M = w.Get(east, y0 + 6, ym) });
        if (gp != null) sim.AddGate(gp, built: true);
        int inner = yx0 + 3;
        while (inner > yx0 - 8 && !Mat.IsPlayer[w.Get(inner, y0 + 6, ym)]) inner--;
        Lay(Shapes.LadderAt(w, site, new VoxelHit { X = inner, Y = y0 + 4, Z = ym, NX = 1, M = w.Get(inner, y0 + 4, ym) }));
        Run(1);
        var yardMan = Man(yx0 + 24, ym);
        var (yard, yardSecs) = Walk(yardMan, yx0 - 64, ym);
        int laddered = yard.Count(c => nav.Ladder[c] != 0), aloft = yard.Count(c => nav.Floor[c] > y0 + 12);
        bool byGate = gp != null && yard.Any(c => Math.Abs((nav.I(c) << NavGrid.Shift) + 4 - yx1) < 12 && Math.Abs((nav.K(c) << NavGrid.Shift) + 4 - ym) < 16);
        if (debug) Console.WriteLine($"       out of the yard: {yardSecs:0.0} s, gate {(gp != null ? "hung" : "none")}, {laddered} cells on a ladder, {aloft} up on the wall: {Cells(yard)}");
        check("a man in a walled yard going out the far side goes round by its gate, not up the ladder and down off the wall",
            gp != null && laddered == 0 && aloft == 0 && byGate && !double.IsNaN(yardSecs),
            $"gate {(gp != null ? "hung" : "not hung")}, {laddered} cells on the ladder, {aloft} up on the wall, {(byGate ? "by the gate" : "not by the gate")}, {yardSecs:0.0} s");
        Gone(yardMan);

        // ---- a ring of wall two metres high with a gap in its east side: a man inside going out west goes round by the gap
        int rx0 = fx0 + 64, rz0 = fz0 + 360 + 168, rx1 = rx0 + 160, rz1 = rz0 + 128;
        Level(rx0 - 120, rz0 - 40, rx1 + 64, rz1 + 40, y0);
        Run(.5);
        var low = new Stroke { Mat = Mat.Stone, Width = 6, Height = 16, Level = true };
        int rm = (rz0 + rz1) / 2;
        Lay(Shapes.Line(w, site, rx0, rz0, rx1, rz0, low, y0 + 16));
        Lay(Shapes.Line(w, site, rx1, rz0, rx1, rm - 16, low, y0 + 16));
        Lay(Shapes.Line(w, site, rx1, rm + 16, rx1, rz1, low, y0 + 16));
        Lay(Shapes.Line(w, site, rx1, rz1, rx0, rz1, low, y0 + 16));
        Lay(Shapes.Line(w, site, rx0, rz1, rx0, rz0, low, y0 + 16));
        Run(1);
        var ringMan = Man(rx0 + 24, rm);
        var (ring, ringSecs) = Walk(ringMan, rx0 - 64, rm);
        int over = ring.Count(c => nav.Floor[c] > y0 + 8);
        if (debug) Console.WriteLine($"       out of the low ring: {ringSecs:0.0} s, {over} cells up on the wall: {Cells(ring)}");
        check("a man inside a ring of wall two metres high goes out round by the gap in it, not over the wall",
            over == 0 && !double.IsNaN(ringSecs), $"{over} cells up on the wall, {ringSecs:0.0} s");
        Gone(ringMan);

        // ---- a river ten metres wide, knee-deep, running east and west south of the citadel; a man goes over it south,
        // and a bridge crosses it thirty metres east of his straight way
        int mx = site.KeepX - 140, wz0 = site.KeepZ + 248, wz1 = wz0 + 80;
        Level(mx - 80, wz0 - 200, Math.Min(w.SX - 8, mx + 320), Math.Min(w.SZ - 8, wz1 + 200), y0);
        for (int z = wz0; z < wz1; z++)
            for (int x = mx - 80; x < Math.Min(w.SX - 8, mx + 320); x++)
            {
                // knee-deep: two voxels of water
                int deep = 2;
                for (int yy = y0 - deep - 1; yy < y0; yy++) w.Set(x, yy, z, yy < y0 - deep ? Mat.Soil : yy < y0 - 1 ? Mat.Water : Mat.Air);
                if (site.WaterTop != null) site.WaterTop[z * w.SX + x] = (short)(y0 - 1);
            }
        Run(1);
        // knee-deep: a man stands in it all the way across
        int wadeAt = sim.NearestStand(mx, (wz0 + wz1) / 2, 2);
        bool wadeable = wadeAt >= 0 && Math.Abs((nav.K(wadeAt) << NavGrid.Shift) + 4 - (wz0 + wz1) / 2) < 12 && nav.Footing[wadeAt] == (byte)Footing.Water;
        var bridge = Bridges.Plan(w, site, mx + 240, wz0 - 12, mx + 240, wz1 + 12, 14, Mat.Stone);
        Lay(bridge);
        sim.Arch(bridge);
        Run(1);
        var crosser = Man(mx, wz0 - 160);
        var (cross, crossSecs) = Walk(crosser, mx, wz1 + 160);
        // in the river anywhere but on the bridge (the water its piers put out of the way stands a while on its ends)
        int wet = cross.Count(c => (nav.Water[c] > 0 || nav.Footing[c] == (byte)Footing.Water) && Math.Abs((nav.I(c) << NavGrid.Shift) + 4 - (mx + 240)) > 16);
        if (debug) Console.WriteLine($"       over the river: {crossSecs:0.0} s, {wet} cells in the water (wadeable {wadeable}): {Cells(cross)}");
        check("a man going over a knee-deep river ten metres wide takes the bridge thirty metres along from the straight way, and keeps dry",
            wadeable && wet == 0 && !double.IsNaN(crossSecs), $"{(wadeable ? "the river wadeable" : "the river NOT wadeable")}, {wet} cells in the water off the bridge, {crossSecs:0.0} s");
        Gone(crosser);

        if (only == "ground") return;
        Scaffolds(check, debug);
        Town(check, debug);
    }

    /// <summary>
    /// The town's ways (Sim.TownWays.cs): three houses about the citadel and a ring of wall round them with a gate west
    /// and east, four workers with nothing marked to do. In ten minutes they lay the ways from the citadel's gate to every
    /// door and gate themselves, and a man going from a house's door to the citadel walks on them; with the town's work
    /// struck off in the work table they lay none.
    /// </summary>
    static void Town(Action<string, bool, string> check, bool debug)
    {
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        // (the old quiet way with nothing to do, as the other staged scenes have it: Sim.Life.cs)
        var sim = new Sim(w, site) { QuietLife = true };
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        int kx = site.KeepX, kz = site.KeepZ;
        void Stock() { sim.Stock[Stockpile.Wood] = 600.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Stone] = 600.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Earth] = 600.0 * VoxelWorld.PerM3; }
        Stock();
        sim.InstantLabour = true;
        // three houses about the citadel, twenty-five metres off to the west, north-west and south-west, each on ground
        // levelled for it
        var spots = new List<(int x, int z)>();
        foreach (double deg in new[] { 180.0, 140.0, 220.0 })
        {
            int x = kx + (int)(Math.Cos(deg * Math.PI / 180) * 200), z = kz + (int)(Math.Sin(deg * Math.PI / 180) * 200), g = w.SurfAt(x, z);
            for (int zz = z - 40; zz <= z + 40; zz++)
                for (int xx = x - 40; xx <= x + 40; xx++)
                {
                    for (int y = w.SurfAt(xx, zz), top = w.ColTop(xx, zz); y < top; y++)
                        if (Mat.IsTrunk(w.Get(xx, y, zz))) { var tree = Shapes.Tree(w, xx, y, zz); for (int t = 0; t < tree.Count; t++) w.Set(tree.X(t), tree.Y(t), tree.Z(t), Mat.Air); }
                    for (int y = g - 12; y < Math.Max(g, w.ColTop(xx, zz)) + 2; y++)
                    {
                        byte want = y < g - 1 ? Mat.Soil : y == g - 1 ? Mat.Grass : Mat.Air;
                        if (w.Get(xx, y, zz) != want) w.Set(xx, y, zz, want);
                    }
                }
            spots.Add((x, z));
        }
        Run(2);
        foreach (var (x, z) in spots)
        {
            var house = Shapes.House(w, site, x - 18, z - 24, x + 18, z + 23, new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true },
                Shapes.LevelTop(w, x, z, 32), out var door, out _, out var arched);
            if (house == null) continue;
            sim.DesignateBuild(house);
            sim.Arch(arched);
            sim.PlanHouse(door, x - 18, z - 24, x + 18, z + 23);
        }
        Run(10);
        // a ring of wall three metres high round it all (the houses stand west of the citadel), a gate in its west side and its east
        int xw = kx - 340, xe = Math.Min(kx + 120, w.SX - 40), zn = kz - 340, zs = kz + 340, R = 340;
        var wall = new Stroke { Mat = Mat.Stone, Width = 6, Height = 24, Level = false };
        foreach (var (ax, az, bx, bz) in new[] { (xw, zn, xe, zn), (xe, zn, xe, zs), (xe, zs, xw, zs), (xw, zs, xw, zn) })
            sim.DesignateBuild(Shapes.Line(w, site, ax, az, bx, bz, wall, 0));
        Run(1);
        int gates = 0;
        foreach (int side in new[] { -1, 1 })
        {
            int line = side < 0 ? xw : xe, x = line + side * 8, g = w.SurfAt(x, kz);
            while (Math.Abs(x - line) < 16 && !Mat.IsPlayer[w.Get(x, g + 6, kz)]) x -= side;
            var gp = GatePlan.At(w, site, new VoxelHit { X = x, Y = g + 6, Z = kz, NX = side, M = w.Get(x, g + 6, kz) });
            if (gp != null) { sim.AddGate(gp, built: true); gates++; }
        }
        Run(5);
        sim.InstantLabour = false;
        int doors = sim.Gates.Count;
        var crew = new List<Unit>();
        for (int i = 0; i < 4; i++) crew.Add(sim.Spawn(Kinds.Worker, sim.Gate));
        // the town's work struck off: nothing is laid
        foreach (var u in crew) u.Priority[(int)WorkKind.Town] = 0;
        Run(60);
        int laidOff = sim.TownWaysLaid;
        foreach (var u in crew) u.Priority[(int)WorkKind.Town] = Work.Usual;
        double t0 = sim.Time;
        for (int s = 0; s < 600; s++) { Stock(); Run(1); }
        var (planned, laid) = sim.TownWaysDone();
        // from each house's door to the citadel's gate: the share of the way on a way
        var shares = new List<string>();
        int poor = 0;
        foreach (var g in sim.Gates)
        {
            var (mx, _, mz) = g.Middle;
            if ((Math.Abs(mx - xw) < 24 || Math.Abs(mx - xe) < 24) && Math.Abs(mz - kz) < 40) continue;
            var walker = sim.Spawn(Kinds.Worker, sim.NearestStand((int)mx + (g.AlongX ? 0 : 14), (int)mz + (g.AlongX ? 14 : 0), 2));
            if (sim.GoTo(walker, sim.Gate, adjacent: true, budget: 100000) && walker.Path != null)
            {
                int on = walker.Path.Count(c => PathFinder.OnWay(sim.Nav, c));
                shares.Add($"{on}/{walker.Path.Count}");
                if (on < walker.Path.Count * 7 / 10) poor++;
            }
            walker.Dead = true; walker.DiedAt = -999;
        }
        if (debug) Console.WriteLine($"       town: {spots.Count} houses, {gates} gates in the ring, {doors} doors in all; planned {planned} columns, laid {laid}; door to citadel on ways {string.Join(" ", shares)}; crew «{string.Join("», «", crew.Select(u => u.Why))}»");
        check("workers with nothing else to do lay the town's ways themselves: from the citadel's gate to every door and gate in ten minutes, and a man from a house's door walks on them",
            spots.Count == 3 && planned > 400 && laid >= planned * 8 / 10 && shares.Count >= 3 && poor == 0,
            $"{spots.Count} houses, {gates} gates; {planned} columns of way planned, {laid} laid in {sim.Time - t0:0} s; from the doors to the citadel on a way: {string.Join(", ", shares)}");
        check("with the town's work struck off in the work table, the workers lay none of it", laidOff == 0, $"{laidOff} columns laid in a minute with it struck off");
    }

    /// <summary>
    /// Scaffolding over a tall build: a round stone tower eight metres high and a stone wall six metres high and twenty
    /// long on a level field, four workers at them with all the stone and logs they want. How many times scaffolding goes
    /// up and comes down, how long each stands, and how often one goes up where another was taken down within the minute:
    /// none of that to and fro, and none left standing at the end.
    /// </summary>
    static void Scaffolds(Action<string, bool, string> check, bool debug)
    {
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        // (the old quiet way with nothing to do, as the other staged scenes have it: Sim.Life.cs)
        var sim = new Sim(w, site) { QuietLife = true };
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        int fx0 = (site.KeepX - 460) & ~7, fz0 = (site.KeepZ - 140) & ~7, fg = w.SurfAt(fx0 + 160, fz0 + 140);
        for (int z = fz0; z < fz0 + 280; z++)
            for (int x = fx0; x < fx0 + 320; x++)
                for (int y = fg - 8; y < fg + 120; y++) w.Set(x, y, z, y < fg ? (y < fg - 4 ? Mat.Rock : Mat.Soil) : Mat.Air);
        Run(.5);
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        var crew = new List<Unit>();
        for (int i = 0; i < 4; i++) crew.Add(sim.Spawn(Kinds.Worker, sim.NearestStand(fx0 + 40 + i * 10, fz0 + 40, 2)));
        void Stock() { sim.Stock[Stockpile.Wood] = 4000.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Stone] = 4000.0 * VoxelWorld.PerM3; sim.Stock.Gold = 1e6; }
        Stock();
        var tower = Shapes.Circle(w, site, fx0 + 240, fz0 + 200, 24, new Stroke { Mat = Mat.Stone, Width = 6, Height = 64, Level = true, Hollow = true },
            Shapes.LevelTop(w, fx0 + 240, fz0 + 200, 64));
        var wall = Shapes.Line(w, site, fx0 + 40, fz0 + 140, fx0 + 200, fz0 + 140, new Stroke { Mat = Mat.Stone, Width = 8, Height = 48, Level = true },
            Shapes.LevelTop(w, fx0 + 120, fz0 + 140, 48));
        sim.DesignateBuild(tower);
        sim.DesignateBuild(wall);
        // each scaffold as it goes up and comes down, while the work goes on (logs given as told); one put up to get out
        // of somewhere comes down as soon as its raiser is out, as it should, and is not counted as taken down by turns
        var log = new List<string>();
        (int raised, int down, int quick, int again, double took, int left) Watch(string name, Action stock)
        {
            var up = new Dictionary<Scaffold, double>();
            // (those standing from before are not this work's)
            foreach (var sc in sim.Scaffolds) up[sc] = double.NaN;
            var downs = new List<(double up, double down, int i, int k)>();
            int raised = 0, quick = 0, again = 0, marked = int.MaxValue;
            double t0 = sim.Time, still = t0;
            while (sim.Time - t0 < 1800 && sim.Orders.Total > 0 && sim.Time - still < 120)
            {
                stock();
                Run(1);
                if (sim.Orders.Total < marked) { marked = sim.Orders.Total; still = sim.Time; }
                foreach (var sc in sim.Scaffolds)
                {
                    if (up.ContainsKey(sc) || sc.Columns.Count == 0) continue;
                    up[sc] = sim.Time;
                    raised++;
                    var c = sc.Columns[0];
                    if (!sc.Escape && downs.Any(d => Math.Abs(d.i - c.i) <= 3 && Math.Abs(d.k - c.k) <= 3 && sim.Time - d.down < 60)) again++;
                    log.Add($"{name} {sim.Time - t0:0} up {(sc.Escape ? "escape" : "reach")} at {c.i},{c.k} {c.y0}-{c.y1}");
                }
                foreach (var sc in up.Keys.ToList())
                {
                    if (sim.Scaffolds.Contains(sc)) continue;
                    var c = sc.Columns.Count > 0 ? sc.Columns[0] : default;
                    if (double.IsNaN(up[sc])) { up.Remove(sc); continue; }
                    downs.Add((up[sc], sim.Time, c.i, c.k));
                    if (sim.Time - up[sc] < 30 && !sc.Escape) quick++;
                    log.Add($"{name} {sim.Time - t0:0} down at {c.i},{c.k} after {sim.Time - up[sc]:0} s");
                    up.Remove(sc);
                }
            }
            return (raised, downs.Count, quick, again, sim.Time - t0, sim.Orders.Total);
        }
        var plenty = Watch("plenty", Stock);
        // (long enough for one left up on what it built to wait for a gate to open, lash its own way down and climb down by it)
        Run(480);
        int standing = sim.Scaffolds.Count(sc => !sc.Escape);
        if (debug)
        {
            foreach (var sc in sim.Scaffolds)
                Console.WriteLine($"       standing: at {sc.Columns[0].i},{sc.Columns[0].k} {sc.Columns[0].y0}-{sc.Columns[^1].y1} escape {sc.Escape} done {sc.Done:0} used {sc.Used:0} lowering {sc.Lowering} by {sc.By?.Id}");
            foreach (var u in crew) Console.WriteLine($"       crew {u.Id} at {sim.Nav.I(u.Cell)},{sim.Nav.Y(u.Cell)},{sim.Nav.K(u.Cell)} y {u.Y - fg:0} «{u.Why}» {u.State} raising {u.Raising != null}");
        }
        // and a wall as high with the logs for one column of scaffolding and not two: none lashed up only to come down
        // again for want of logs
        sim.CancelArea(0, 0, w.SX, w.SZ);
        var wall2 = Shapes.Line(w, site, fx0 + 40, fz0 + 60, fx0 + 200, fz0 + 60, new Stroke { Mat = Mat.Stone, Width = 8, Height = 48, Level = true },
            Shapes.LevelTop(w, fx0 + 120, fz0 + 60, 48));
        sim.DesignateBuild(wall2);
        sim.Stock[Stockpile.Wood] = 800;
        var scarce = Watch("scarce", () => { sim.Stock[Stockpile.Stone] = 4000.0 * VoxelWorld.PerM3; });
        // and a ring of wall six metres high closed round the crew as they build it: they get out over it and go on from
        // outside and on top, and the scaffolding they got out by is not lashed up and taken down again each time
        sim.CancelArea(0, 0, w.SX, w.SZ);
        Run(60);
        int rx = fx0 + 80, rz = fz0 + 200;
        var ring = new VoxelList();
        foreach (var (ax, az, bx, bz) in new[] { (rx, rz, rx + 96, rz), (rx + 96, rz, rx + 96, rz + 96), (rx + 96, rz + 96, rx, rz + 96), (rx, rz + 96, rx, rz) })
        {
            var side = Shapes.Line(w, site, ax, az, bx, bz, new Stroke { Mat = Mat.Stone, Width = 8, Height = 48, Level = true }, Shapes.LevelTop(w, rx + 48, rz + 48, 48));
            for (int i = 0; i < side.Count; i++) ring.Add(side.X(i), side.Y(i), side.Z(i), side.M(i));
        }
        foreach (var u in crew)
        {
            int c = sim.NearestStand(rx + 48, rz + 48, 2);
            u.Cell = c; (u.X, u.Y, u.Z) = sim.Nav.Feet(c); u.Path = null; u.Site = -1; u.Jobs.Clear(); u.Raising = null;
        }
        sim.DesignateBuild(ring);
        var closed = Watch("ring", Stock);
        Run(240);
        int ringStanding = sim.Scaffolds.Count(sc => !sc.Escape);
        if (debug)
        {
            Console.WriteLine($"       scaffolds: {string.Join("; ", log)}");
            foreach (var L in new[] { tower, wall, wall2 })
                for (int i = 0, n = 0; i < L.Count && n < 4; i++)
                    if (sim.Orders.At(L.X(i), L.Y(i), L.Z(i)) != 0) { n++; Console.WriteLine($"       left: {L.X(i)},{L.Y(i) - fg},{L.Z(i)} {Mat.Def[L.M(i)].Name}, crew «{string.Join("», «", crew.Select(u => u.Why))}»"); }
        }
        check("scaffolding over a tall build goes up where the work needs it and stands till that work is done, not put up and taken down by turns; none is left standing",
            plenty.left == 0 && plenty.again == 0 && plenty.quick == 0 && standing == 0,
            $"tower and wall {(plenty.left == 0 ? "built" : $"{plenty.left} voxels left")} in {plenty.took:0} s; {plenty.raised} raised, {plenty.down} taken down, {plenty.quick} stood under 30 s, " +
            $"{plenty.again} raised where another came down within the minute; {standing} left standing");
        check("a ring of wall six metres high closed round the crew as they build it: they get out over it and finish it, with no scaffolding lashed up and taken down by turns, none left standing",
            closed.left == 0 && closed.quick == 0 && closed.again == 0 && ringStanding == 0,
            $"{(closed.left == 0 ? "built" : $"{closed.left} voxels left")} in {closed.took:0} s; {closed.raised} raised, {closed.down} taken down, {closed.quick} stood under 30 s, {closed.again} raised where another came down within the minute; {ringStanding} left standing");
        check("with logs for one column of scaffolding and not two, none is lashed half up and taken down again for want of them",
            scarce.quick == 0 && scarce.again == 0,
            $"{scarce.raised} raised, {scarce.down} taken down, {scarce.quick} stood under 30 s, {scarce.again} raised where another came down within the minute; {scarce.left} voxels left in {scarce.took:0} s");
    }
}
