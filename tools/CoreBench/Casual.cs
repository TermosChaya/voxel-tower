using System;
using System.Collections.Generic;
using VoxelTower.Core;

/// <summary>
/// The bench's easier building (--casual): how long four workers take over the usual works — a stone wall twenty
/// metres long, a round tower, a house of stone, a moat — each in a valley of its own from the citadel's gate on level
/// ground; every ready template put up and built whole; the settlement's rank.
/// </summary>
static class CasualBench
{
    /// <summary>A valley with the old fort, four workers at the gate and stores enough (earth too, for the towers' hearts), and level spots a walk from the citadel.</summary>
    static (Sim sim, VoxelWorld w, Site site, List<(int x, int z)> spots) Valley()
    {
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        StartingFort.Raise(sim);
        sim.CancelArea(0, 0, w.SX, w.SZ);
        for (int i = 0; i < 4; i++) sim.Spawn(Kinds.Worker, sim.Gate);
        sim.Stock[Stockpile.Stone] += 1200.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] += 600.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Earth] += 300.0 * VoxelWorld.PerM3;
        int kx = site.KeepX, kz = site.KeepZ;
        var spots = new List<(int x, int z)>();
        for (int r = 110; r < 460 && spots.Count < 6; r += 8)
            for (int a = 0; a < 24 && spots.Count < 6; a++)
            {
                int x = kx + (int)(Math.Cos(a * Math.Tau / 24) * r), z = kz + (int)(Math.Sin(a * Math.Tau / 24) * r);
                if (x < 100 || z < 100 || x > w.SX - 100 || z > w.SZ - 100 || spots.Exists(s => Math.Abs(s.x - x) < 120 && Math.Abs(s.z - z) < 120)) continue;
                int lo = int.MaxValue, hi = 0;
                for (int dx = -48; dx <= 48; dx += 4) for (int dz = -48; dz <= 48; dz += 4) { int h = w.SurfAt(x + dx, z + dz); lo = Math.Min(lo, h); hi = Math.Max(hi, h); if (Mat.IsTrunk(w.Get(x + dx, h + 2, z + dz))) hi = 999; }
                if (hi - lo <= 2 && !site.Forbidden(x, z)) spots.Add((x, z));
            }
        return (sim, w, site, spots);
    }

    /// <summary>How long the work marked takes the workers to the last voxel, seconds; -1 if not done in the time given.</summary>
    static double Until(Sim sim, int limit = 900)
    {
        double t0 = sim.Time;
        for (int t = 0; t < limit * 20 && sim.Orders.Total > 0; t++) sim.Step(.05f);
        return sim.Orders.Total > 0 ? -1 : sim.Time - t0;
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var (simA, wA, siteA, spotsA) = Valley();
        if (spotsA.Count < 1) { check("level ground for the works", false, "none"); return; }
        int fell = 0;
        simA.Collapsed += (n, _, _, _) => fell += n;
        var (ax, az) = spotsA[0];
        simA.DesignateBuild(Shapes.Line(wA, siteA, ax - 80, az, ax + 79, az, new Stroke { Mat = Mat.Stone, Width = 8, Height = 16, Level = true }, Shapes.LevelTop(wA, ax, az, 16)));
        double wall = Until(simA);

        var (simB, wB, siteB, spotsB) = Valley();
        simB.Collapsed += (n, _, _, _) => fell += n;
        var (bx, bz) = spotsB[0];
        simB.DesignateBuild(Shapes.Circle(wB, siteB, bx, bz, 24, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Hollow = false, Level = true }, Shapes.LevelTop(wB, bx, bz, 32)));
        double tower = Until(simB);

        var (simC, wC, siteC, spotsC) = Valley();
        simC.Collapsed += (n, _, _, _) => fell += n;
        var (cx, cz) = spotsC[0];
        int x0 = cx - 18, z0 = cz - 28, x1 = cx + 18, z1 = cz + 27;
        var plan = Shapes.House(wC, siteC, x0, z0, x1, z1, new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true }, Shapes.LevelTop(wC, cx, cz, 32), out var door, out _, out var arched);
        double house = -1;
        if (plan != null)
        {
            simC.DesignateBuild(plan);
            simC.Arch(arched);
            simC.PlanHouse(door, x0, z0, x1, z1);
            house = Until(simC);
        }

        var (simD, wD, siteD, spotsD) = Valley();
        var (dx0, dz0) = spotsD[0];
        simD.DesignateRemove(Shapes.Line(wD, siteD, dx0 - 80, dz0, dx0 + 79, dz0, new Stroke { Width = 8, Height = 8, Dig = true }, 0));
        double moat = Until(simD);

        check("four workers build the usual works briskly: a stone wall 20 m by 2 m in under 35 s, a solid round tower 6 m across and 4 m high in under 60 s, a stone house in under 65 s, a moat 20 m long in under 28 s, and nothing falls",
            wall > 0 && wall < 35 && tower > 0 && tower < 60 && house > 0 && house < 65 && moat > 0 && moat < 28 && fell == 0,
            $"the wall {wall:0} s, the tower {tower:0} s, the house {house:0} s, the moat {moat:0} s; fell {fell}");

        Templates(check);
        Standing(check);
    }

    /// <summary>
    /// Every ready template set on level ground, each in a valley of its own, put up and built whole by four workers
    /// with nothing falling: the gatehouse's gate cut and hung once its wall stands, the watchtower's deck reached up its
    /// ladder, the wall's merlons up on its outer side; the stair down from the wall built, and the floor laid in a room;
    /// over the valley's edge every one refused.
    /// </summary>
    static void Templates(Action<string, bool, string> check)
    {
        var said = new List<string>();
        bool all = true;
        (Sim sim, VoxelWorld w, Site site, int x, int z) wallAt = default;
        foreach (var t in Sim.Templates)
        {
            if (t.Key is "stairs" or "floor") continue;
            var (sim, w, site, spots) = Valley();
            sim.Standing = Sim.StandingNames.Length - 1;
            int fell = 0, gates0 = sim.Gates.Count;
            sim.Collapsed += (n, _, _, _) => fell += n;
            var (x, z) = spots[0];
            var plan = t.Line ? t.Plan(w, site, x - 64, z, x + 63, z, 0, t.Size) : t.Plan(w, site, x, z, 0, 0, 1, t.Size);
            // the watchtower's deck, as its plan sets it on the ground before anything stands there
            int deck = 0, bx = ((x >> 3) - 1) << 3, bz = ((z >> 3) - 1) << 3;
            for (int xx = bx; xx < bx + 24; xx += 4) for (int zz = bz; zz < bz + 24; zz += 4) deck = Math.Max(deck, w.SurfAt(xx, zz));
            deck = ((deck + 31) >> 3) << 3;
            string why = sim.PutTemplate(t, plan);
            double sec = why == null ? Until(sim) : -1;
            string more = "";
            bool ok = sec > 0 && fell == 0;
            if (t.Key == "gatehouse" && ok)
            {
                // the doorway cut and the leaves hung once the wall stands round it
                for (int i = 0; i < 20 * 20 && (sim.HouseDoors.Count > 0 || sim.Orders.Total > 0); i++) sim.Step(.05f);
                double cut = Until(sim);
                ok &= sim.HouseDoors.Count == 0 && sim.Gates.Count > gates0 && cut >= 0;
                more = $", the gate {(sim.Gates.Count > gates0 ? "hung" : "not hung")}";
            }
            if (t.Key == "watchtower" && ok)
            {
                // the deck stood on, and got up to from the gate by the ladder
                int ci = x >> 3, ck = z >> 3;
                sim.Nav.Flush();
                var cells = new List<int>();
                sim.Paths.Reach(sim.Gate, Kinds.Worker.Move, 400000, true, cells);
                bool stands = sim.Nav.Stand(ci, deck >> 3, ck, 2), reached = cells.Contains(sim.Nav.Id(ci, deck >> 3, ck));
                ok &= stands && reached;
                more = $", the deck stood on {stands}, reached {reached}";
            }
            if (t.Key == "wall" && ok)
            {
                // the merlons up on the side away from the citadel, over the walk
                int walk = w.SurfAt(x, z), outer = Math.Max(w.SurfAt(x, z - 3), w.SurfAt(x, z + 3)), mx = 0;
                for (int dx = -12; dx <= 12; dx++) mx = Math.Max(mx, Math.Max(w.SurfAt(x + dx, z - 3), w.SurfAt(x + dx, z + 3)));
                ok &= mx >= walk + 4;
                more = $", merlons {mx - walk} over the walk";
                wallAt = (sim, w, site, x, z);
            }
            all &= ok;
            said.Add($"{t.Name} {(sec < 0 ? why ?? "not done" : $"{sec:0} s")}{(fell > 0 ? $", {fell} fell" : "")}{more}");
        }

        // the stair down from the wall's top, built
        double stairs = -1;
        if (wallAt.sim != null)
        {
            var (sim, w, site, x, z) = wallAt;
            var t = Sim.TemplateOf("stairs");
            int sz = w.SurfAt(x + 30, z + 2) > w.SurfAt(x + 30, z - 2) ? z + 2 : z - 2;
            var p = t.Plan(w, site, x + 30, sz, 0, 0, sz > z ? 0 : 2, 0);
            if (sim.PutTemplate(t, p) == null) stairs = Until(sim);
        }
        said.Add($"the stair {(stairs < 0 ? "not done" : $"{stairs:0} s")}");

        // the floor laid inside a room of stone walls with a doorway in one, open to the sky, three metres over the ground
        double floor = -1;
        string floorWhy = "";
        {
            var (sim, w, site, spots) = Valley();
            var (cx, cz) = spots[0];
            int g = w.SurfAt(cx, cz);
            var room = Shapes.Box(w, site, cx - 18, cz - 18, cx + 17, cz + 17, new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true, Hollow = true }, g + 32);
            var plan = new VoxelList();
            for (int i = 0; i < room.Count; i++)
                if (!(room.Z(i) < cz - 13 && Math.Abs(room.X(i) - cx) < 8 && room.Y(i) < g + 20)) plan.Add(room.X(i), room.Y(i), room.Z(i), room.M(i));
            sim.DesignateBuild(plan);
            if (Until(sim) > 0)
            {
                var t = Sim.TemplateOf("floor");
                var p = t.Plan(w, site, cx, cz, 0, 0, 0, t.Size);
                floorWhy = sim.PutTemplate(t, p) ?? "";
                if (floorWhy.Length == 0) floor = Until(sim);
            }
        }
        said.Add($"the floor {(floor < 0 ? $"not done «{floorWhy}»" : $"{floor:0} s")}");

        // over the hexagon's edge: refused, as the town's buildings are
        var (simE, wE, siteE, _) = Valley();
        int ex = 4, ez = wE.SZ / 2;
        while (ex < wE.SX && !wE.OnLand(ex + 40, ez)) ex += 8;
        int edge = 0;
        foreach (var t in Sim.Templates)
        {
            var p = t.Line ? t.Plan(wE, siteE, ex, ez, ex + 40, ez, 0, t.Size) : t.Plan(wE, siteE, ex, ez, 0, 0, 0, t.Size);
            if (p.Voxels == null && p.Why == "здесь край долины") edge++;
        }
        check("every ready template is put up and built whole by four workers and nothing falls: the gatehouse's gate hung, the watchtower's deck reached by its ladder, the wall's merlons up; the stair down from the wall and a floor in a room built; over the valley's edge each is refused",
            all && stairs > 0 && floor > 0 && edge == Sim.Templates.Length,
            string.Join("; ", said) + $"; refused over the edge {edge} of {Sim.Templates.Length}");
    }

    /// <summary>
    /// The settlement's rank: it rises with what is built, pays its gold once, says so in the chronicle with the
    /// templates it opens, opens them (and not those of the ranks to come), and is kept in a save.
    /// </summary>
    static void Standing(Action<string, bool, string> check)
    {
        var (sim, w, site, _) = Valley();
        var mp = new MapParams();
        var lines = new List<string>();
        sim.Said += (text, _, _, _) => lines.Add(text);
        bool shut0 = !sim.TemplateOpen(Sim.TemplateOf("watchtower"));
        double gold0 = sim.Stock.Gold;
        sim.BuiltVoxels = 2000L * Sim.StandingPoints[1];
        for (int i = 0; i < 20 * 6; i++) sim.Step(.05f);
        double paid = sim.Stock.Gold - gold0;
        string line = lines.Find(l => l.StartsWith("Поселение выросло")) ?? "";
        bool open1 = sim.TemplateOpen(Sim.TemplateOf("watchtower")) && !sim.TemplateOpen(Sim.TemplateOf("gatehouse"));
        string refused = sim.PutTemplate(Sim.TemplateOf("gatehouse"), null) ?? "";
        // the gold paid once: a while on, nothing more
        for (int i = 0; i < 20 * 6; i++) sim.Step(.05f);
        double again = sim.Stock.Gold - gold0 - paid;
        var buf = new System.IO.MemoryStream();
        sim.Save(buf, mp.Seed, "casual", "");
        buf.Position = 0;
        var w2 = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        Sim sim2;
        using (var save = new SaveReader(buf))
        {
            var site2 = MapGen.Generate(w2, new MapParams { Seed = save.Seed });
            w2.Compact();
            save.Valley(w2, site2);
            sim2 = new Sim(w2, site2);
            sim2.Load(save);
        }
        check("the settlement's rank rises with what is built: «Деревня» pays its gold once, says so in the chronicle with the templates it opens, opens them and not the gatehouse yet, and is kept in a save",
            shut0 && sim.Standing == 1 && paid >= 40 && Math.Abs(again) < 1 && line.Contains("деревня") && line.Contains("«Квадратная башня»") && open1
            && refused.Contains("«Село»") && sim2.Standing == 1 && sim2.BuiltVoxels == sim.BuiltVoxels && sim.StandingLine().StartsWith("Деревня"),
            $"shut at first {shut0}; rank {sim.Standing}, paid {paid:0}, then {again:0}; «{line}»; open {open1}; the gatehouse «{refused}»; read back {sim2.Standing}, {sim2.BuiltVoxels} laid; «{sim.StandingLine()}»");
    }
}
