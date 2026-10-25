using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The settlement's ways and furnishing (--decor), by real hands on a level field: a cobbled way laid flush over the turf
/// and walked faster; a route walked again and again trodden bare and grown over when left; every thing of the
/// «Обустройство» tool put together from the stores, its fires lit at night, its bench sat on, taken down again;
/// kept in a save.
/// </summary>
static class DecorBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        // a field of level turf west of the citadel, bare of trees
        int fx0 = (site.KeepX - 460) & ~7, fz0 = (site.KeepZ - 140) & ~7, fg = w.SurfAt(fx0 + 160, fz0 + 140);
        for (int z = fz0; z < fz0 + 280; z++)
            for (int x = fx0; x < fx0 + 320; x++)
                for (int y = fg - 8; y < fg + 120; y++) w.Set(x, y, z, y < fg ? (y < fg - 4 ? Mat.Rock : y == fg - 1 ? Mat.Grass : Mat.Soil) : Mat.Air);
        Run(.5);
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        var crew = new List<Unit>();
        for (int i = 0; i < 4; i++) crew.Add(sim.Spawn(Kinds.Worker, sim.NearestStand(fx0 + 40 + i * 10, fz0 + 30, 2)));
        void Stock() { sim.Stock[Stockpile.Wood] = 4000.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Stone] = 4000.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Earth] = 4000.0 * VoxelWorld.PerM3; sim.Stock.Gold = 1e6; }
        Stock();
        int Open(VoxelList L) { int n = 0; for (int i = 0; i < L.Count; i++) if (sim.Orders.At(L.X(i), L.Y(i), L.Z(i)) != 0) n++; return n; }

        // a cobbled way two metres wide, twenty long, laid by the crew: flush with the turf either side, of stones, the
        // turf's tufts gone off it
        int wz = fz0 + 80, wx0 = fx0 + 40, wx1 = fx0 + 200;
        var way = Sim.WayPlan(w, site, wx0, wz, wx1, wz, 16, WayKind.Cobble);
        sim.Pave(way);
        double t0 = sim.Time;
        while (sim.Time - t0 < 400 && (Open(way) > 0 || sim.Relays.Count > 0)) { Stock(); Run(1); }
        int laid = 0, flush = 0;
        for (int i = 0; i < way.Count; i++)
        {
            int x = way.X(i), y = way.Y(i), z = way.Z(i);
            if (Mat.IsPavement[w.Get(x, y, z)]) laid++;
            if (w.SurfAt(x, z) == fg) flush++;
        }
        check("a way laid by the workers: the turf under it taken up and cobbles set in its place, flush with the ground",
            way.Count > 2000 && laid == way.Count && flush == way.Count,
            $"{way.Count} columns, {laid} laid, {flush} flush, in {sim.Time - t0:0} s");

        // walked faster: one along the way, one along the turf beside it, the same distance
        foreach (var u in crew) { u.Hold = false; }
        var onWay = crew[0]; var onTurf = crew[1];
        onWay.X = wx0 + 4; onWay.Z = wz; onWay.Cell = sim.NearestStand(wx0 + 4, wz, 2); (onWay.X, onWay.Y, onWay.Z) = sim.Nav.Feet(onWay.Cell);
        onTurf.Cell = sim.NearestStand(wx0 + 4, wz + 60, 2); (onTurf.X, onTurf.Y, onTurf.Z) = sim.Nav.Feet(onTurf.Cell);
        int endWay = sim.NearestStand(wx1 - 4, wz, 2), endTurf = sim.NearestStand(wx1 - 4, wz + 60, 2);
        sim.Command(new[] { onWay }, endWay); sim.Command(new[] { onTurf }, endTurf);
        double tWay = -1, tTurf = -1; t0 = sim.Time;
        for (int i = 0; i < 1200 && (tWay < 0 || tTurf < 0); i++)
        {
            Run(.05);
            if (tWay < 0 && onWay.Cell == endWay) tWay = sim.Time - t0;
            if (tTurf < 0 && onTurf.Cell == endTurf) tTurf = sim.Time - t0;
        }
        check("along a cobbled way one walks faster than over the turf", tWay > 0 && tTurf > 0 && tWay < tTurf * .88,
            $"{tWay:0.0} s along the way, {tTurf:0.0} s over the turf");

        // a route walked again and again is trodden bare; left, it grows over again
        int ax = fx0 + 60, az = fz0 + 200, bx = fx0 + 200;
        int ca = sim.NearestStand(ax, az, 2), cb = sim.NearestStand(bx, az, 2);
        var walkers = new[] { crew[2], crew[3] };
        foreach (var u in walkers) { u.Cell = ca; (u.X, u.Y, u.Z) = sim.Nav.Feet(ca); }
        bool toB = true;
        int trips = 0;
        for (int i = 0; i < 12000 && trips < 30; i++)
        {
            Run(.1);
            int goal = toB ? cb : ca;
            if (walkers.All(u => (u.Path == null || u.PathI >= u.Path.Count) && Math.Abs(sim.Nav.I(u.Cell) - sim.Nav.I(goal)) <= 2 && Math.Abs(sim.Nav.K(u.Cell) - sim.Nav.K(goal)) <= 2)) { toB = !toB; trips++; sim.Command(walkers, toB ? cb : ca); }
            else if (i % 50 == 0) sim.Command(walkers.Where(u => u.Path == null), toB ? cb : ca);
        }
        int trodden = 0;
        for (int x = ax + 20; x < bx - 20; x++)
            for (int z = az - 3; z <= az + 3; z++)
                if (w.Get(x, w.SurfAt(x, z) - 1, z) == Mat.Trodden) { trodden++; break; }
        check("a route walked again and again between two places is trodden bare along it", trodden >= (bx - ax - 40) * .8,
            $"{trodden} of {bx - ax - 40} columns along it trodden after {trips} trips; {sim.TroddenCount} trodden in all");
        foreach (var u in walkers) { sim.Command(new[] { u }, sim.NearestStand(fx0 + 300, fz0 + 260, 2)); }
        sim.WearFades = 1;
        Run(90);
        sim.WearFades = .02f;
        check("a trodden way left alone grows over again", sim.TroddenCount == 0, $"{sim.TroddenCount} columns still trodden");

        // every thing of the furnishing put together by the crew from the stores
        foreach (var u in crew) { u.Hold = false; }
        var placed = new List<(DecorKind kind, DecorSite site)>();
        int col = 0;
        foreach (var k in Yard.All)
        {
            int px = fx0 + 230 + col % 2 * 40, pz = fz0 + 20 + col / 2 * 40;
            string why = sim.PlaceDecor(k, px, pz, col % 4);
            if (why == null) placed.Add((k, sim.Yards[^1]));
            else Console.WriteLine($"  {Yard.Name(k)}: {why}");
            col++;
        }
        t0 = sim.Time;
        while (sim.Time - t0 < 400 && placed.Any(p => !p.site.Ready)) { Stock(); Run(1); }
        foreach (var (k, st) in placed.Where(p => !p.site.Ready))
        {
            var miss = new Dictionary<string, int>();
            for (int i = 0; i < st.Voxels.Count; i++)
            {
                byte now = w.Get(st.Voxels.X(i), st.Voxels.Y(i), st.Voxels.Z(i)), want = st.Voxels.M(i);
                if (now != want) { string key = $"{Mat.Def[want].Name}→{Mat.Def[now].Name ?? now.ToString()} order {sim.Orders.At(st.Voxels.X(i), st.Voxels.Y(i), st.Voxels.Z(i))}"; miss[key] = miss.GetValueOrDefault(key) + 1; }
            }
            Console.WriteLine($"  {Yard.Name(k)} missing: {string.Join(", ", miss.Select(m => $"{m.Key} ×{m.Value}"))}");
        }
        int lights = sim.Fixtures.Count(f => f.Kind is FixtureKind.Campfire or FixtureKind.Brazier);
        check("each thing of the furnishing is put together by the workers; the fires are lit and the bench has its seats",
            placed.Count == Yard.All.Length && placed.All(p => p.site.Ready) && lights == 3 && sim.Seats.Count == 2,
            $"{placed.Count(p => p.site.Ready)} of {Yard.All.Length} standing in {sim.Time - t0:0} s{string.Join("", placed.Where(p => !p.site.Ready).Select(p => $"; {Yard.Name(p.kind)} not yet"))}; {lights} fires, {sim.Seats.Count} seats");

        // the idle crew sits on the bench or warms itself at a fire
        var bench = placed.First(p => p.kind == DecorKind.Bench).site;
        foreach (var u in crew) { u.Cell = sim.NearestStand(bench.X + 16, bench.Z, 2); (u.X, u.Y, u.Z) = sim.Nav.Feet(u.Cell); u.Hold = false; u.Path = null; }
        bool sat = false, warmed = false;
        for (int i = 0; i < 2400 && !(sat && warmed); i++)
        {
            Run(.1);
            foreach (var u in crew)
            {
                if (u.Pastime == Pastime.Sit && u.SeatH > 0) sat = true;
                if (u.Pastime == Pastime.Warm && sim.Yards.Any(s => s.Light != null && Yard.Warms(s.Kind) && Math.Abs(s.Light.LX - u.X) < 24 && Math.Abs(s.Light.LZ - u.Z) < 24)) warmed = true;
            }
        }
        check("people with nothing to do sit on the bench and warm themselves at the campfire", sat && warmed, $"sat {sat}, warmed {warmed}");

        // at dusk the fires light the ground round them, and not the field far off
        sim.Waves.Countdown = 20;
        Run(1.5);
        var fire = placed.First(p => p.kind == DecorKind.Campfire).site.Light;
        bool lit = fire != null && sim.Lit(fire.LX + 20, fire.LZ), dark = !sim.Lit(fx0 + 20, fz0 + 260);
        sim.Waves.Countdown = 1e9;
        check("at dusk a campfire lights the ground round it", lit && dark, $"by the fire {lit}, far off dark {dark}");

        // kept in a save: the things set out and the ways laid
        var ms = new System.IO.MemoryStream();
        sim.Save(ms, mp.Seed, "test", "");
        ms.Position = 0;
        var w2 = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        Sim sim2;
        using (var reader = new SaveReader(ms))
        {
            var site2 = MapGen.Generate(w2, new MapParams { Seed = reader.Seed });
            w2.Compact();
            reader.Valley(w2, site2);
            sim2 = new Sim(w2, site2);
            sim2.Load(reader);
        }
        for (int i = 0; i < 40; i++) sim2.Step(.05f);
        int lights2 = sim2.Fixtures.Count(f => f.Kind is FixtureKind.Campfire or FixtureKind.Brazier);
        check("the things set out are kept in a save, standing and lit again after it is read back, and the ways with them",
            sim2.Yards.Count == sim.Yards.Count && sim2.Yards.All(s => s.Ready) && lights2 == 3 && sim2.Seats.Count == 2 && Mat.IsPavement[w2.Get(way.X(0), way.Y(0), way.Z(0))],
            $"{sim2.Yards.Count} of {sim.Yards.Count} things, {sim2.Yards.Count(s => s.Ready)} standing, {lights2} fires, {sim2.Seats.Count} seats");

        // taken down: the woodpile marked to be taken apart is gone, and forgotten
        var pile = placed.First(p => p.kind == DecorKind.Woodpile).site;
        var take = new VoxelList();
        for (int i = 0; i < pile.Voxels.Count; i++) take.Add(pile.Voxels.X(i), pile.Voxels.Y(i), pile.Voxels.Z(i), 0);
        sim.DesignateRemove(take);
        t0 = sim.Time;
        while (sim.Time - t0 < 200 && sim.Yards.Contains(pile)) { Stock(); Run(1); }
        check("a thing of the furnishing taken apart by the workers is forgotten", !sim.Yards.Contains(pile), $"in {sim.Time - t0:0} s");
    }
}
