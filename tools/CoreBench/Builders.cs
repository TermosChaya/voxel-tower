using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The builders at work (--builders), by real hands on a level field: a house of logs, a second house drawn on top of
/// it, a stone tower eight metres high, a plank deck over a pit — what falls while they build, whether anyone is left
/// shut in up there, whether a thin deck is walked on.
/// </summary>
static class BuildersBench
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
        // a field of level ground west of the citadel, bare of trees
        int fx0 = (site.KeepX - 460) & ~7, fz0 = (site.KeepZ - 140) & ~7, fg = w.SurfAt(fx0 + 160, fz0 + 140);
        for (int z = fz0; z < fz0 + 280; z++)
            for (int x = fx0; x < fx0 + 320; x++)
                for (int y = fg - 8; y < fg + 120; y++) w.Set(x, y, z, y < fg ? (y < fg - 4 ? Mat.Rock : Mat.Soil) : Mat.Air);
        Run(.5);
        int fell = 0;
        var fellAt = new List<string>();
        sim.Collapsed += (n, x, y, z) => { if (x >= fx0 && x < fx0 + 320 && z >= fz0 && z < fz0 + 280) { fell += n; if (fellAt.Count < 6) fellAt.Add($"{n} at {x:0},{y - fg:0},{z:0} ({sim.Time:0} s)"); } };
        sim.Said += (t, k, x, z) => { if (Environment.GetEnvironmentVariable("BUILDERS_SAY") == "1") Console.WriteLine($"SAID {sim.Time:0}: {t}"); };
        var fellOf = new Dictionary<string, int>();
        sim.Fell += (x, y, z, m) => { if (x >= fx0 && x < fx0 + 320 && z >= fz0 && z < fz0 + 280) { string k = Mat.Def[m].Name ?? "?"; fellOf[k] = fellOf.GetValueOrDefault(k) + 1; } };
        string FellWhat()
        {
            string t = fellAt.Count > 0 ? $" ({string.Join("; ", fellAt)}; of {string.Join(", ", fellOf.Select(k => $"{k.Key} {k.Value}"))})" : "";
            fellAt.Clear(); fellOf.Clear();
            return t;
        }
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        var crew = new List<Unit>();
        for (int i = 0; i < 4; i++)
        {
            var u = sim.Spawn(Kinds.Worker, sim.NearestStand(fx0 + 40 + i * 10, fz0 + 40, 2));
            crew.Add(u);
        }
        void Stock() { sim.Stock[Stockpile.Wood] = 4000.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Stone] = 4000.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Earth] = 4000.0 * VoxelWorld.PerM3; sim.Stock.Gold = 1e6; }
        Stock();
        // whether a worker can still walk away to the open field (or is shut in up there)
        bool Free(Unit u) => u.Dead || sim.Paths.Reach(u.Cell, u.T.Move, 240, true) >= 240;
        string Where(Unit u) => $"{u.Name} at {u.X:0},{u.Y - fg:0},{u.Z:0} «{u.Why}»";
        // where what is left of a work lies, a few of it, and what the crew says
        string Left(VoxelList L)
        {
            var at = new List<string>();
            for (int i = 0; i < L.Count && at.Count < 4; i++)
                if (sim.Orders.At(L.X(i), L.Y(i), L.Z(i)) != 0) at.Add($"{L.X(i)},{L.Y(i) - fg},{L.Z(i)} {Mat.Def[L.M(i)].Name}");
            return at.Count == 0 ? "" : $" (left: {string.Join("; ", at)}; the crew: {string.Join("; ", crew.Select(u => $"«{u.Why}»"))})";
        }
        (double took, int left) Build(VoxelList L, double most)
        {
            sim.DesignateBuild(L);
            double t0 = sim.Time;
            int Open() { int n = 0; for (int i = 0; i < L.Count; i++) if (sim.Orders.At(L.X(i), L.Y(i), L.Z(i)) != 0) n++; return n; }
            while (sim.Time - t0 < most && Open() > 0) { Stock(); Run(1); }
            return (sim.Time - t0, Open());
        }

        // what is still marked, a few of it, and what the crew is about
        string Marked()
        {
            var at = new List<string>();
            foreach (int col in sim.Orders.Active)
            {
                if (!sim.Orders.Span(col, out int y0, out int y1)) continue;
                at.Add($"{(col % sim.Nav.NX) << 3},{y0 - fg}..{y1 - fg},{(col / sim.Nav.NX) << 3}");
                if (at.Count >= 4) break;
            }
            return $" (marked: {string.Join("; ", at)}; the crew: {string.Join("; ", crew.Select(Where))})";
        }
        // (BUILDERS_ONLY=stranded runs that alone, for working on it)
        if (Environment.GetEnvironmentVariable("BUILDERS_ONLY") == "stranded") goto stranded;
        if (Environment.GetEnvironmentVariable("BUILDERS_ONLY") == "tower") goto tower;
        // a house of stone roofed with logs, the room 3 × 4 m, as the rectangle's house form draws it: its roof
        // standing as an arch
        int hx0 = fx0 + 120, hz0 = fz0 + 100, hx1 = hx0 + 36, hz1 = hz0 + 44;
        var st = new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true };
        var house = Shapes.House(w, site, hx0, hz0, hx1, hz1, st, Shapes.LevelTop(w, hx0, hz0, 32), out var door, out string why, out var arched);
        fell = 0; fellAt.Clear();
        double took = 0;
        int left = -1;
        string houseLeft = "";
        if (house != null)
        {
            sim.DesignateBuild(house);
            sim.Arch(arched);
            sim.PlanHouse(door, hx0, hz0, hx1, hz1);
            double t0 = sim.Time;
            int Open() { int n = 0; for (int i = 0; i < house.Count; i++) if (sim.Orders.At(house.X(i), house.Y(i), house.Z(i)) != 0) n++; return n; }
            // and its door after it, hung once the wall stands all round the doorway
            while (sim.Time - t0 < 900 && (Open() > 0 || sim.Orders.Total > 0 || sim.HouseDoors.Count > 0)) { Stock(); Run(1); }
            took = sim.Time - t0; left = Open(); houseLeft = Left(house) + (sim.Orders.Total > 0 ? Marked() : "");
        }
        Run(30);
        var shut = crew.Where(u => !Free(u)).ToList();
        // its door: a gate in the wall of the house, its leaves hung whole
        string Door(int x0, int z0, int x1, int z1, out bool hung)
        {
            var g = sim.Gates.Find(g => { var (gx, _, gz) = g.Middle; return gx >= x0 - 8 && gx <= x1 + 8 && gz >= z0 - 8 && gz <= z1 + 8; });
            var zone = sim.Zones.Find(z => z.Kind == Zone.Dwelling && z.X0 == x0 && z.Z0 == z0);
            hung = g is { Ready: true, Broken: false } && g.Hacked == 0 && zone is { Closed: true };
            return g == null ? "no door" : $"door ready {g.Ready}, broken {g.Broken}, {g.Hacked} of its leaves gone, the dwelling closed {zone?.Closed} «{zone?.Why}»";
        }
        string door1 = Door(hx0, hz0, hx1, hz1, out bool hung1);
        check("four workers raise a house of stone roofed with logs: nothing they lay falls, nobody is left shut in up on it, its door is hung and nothing is left waiting",
            house != null && left == 0 && fell == 0 && shut.Count == 0 && hung1 && sim.Orders.Total == 0,
            $"{house?.Count} voxels{(why != null ? $" ({why})" : "")} in {took:0} s, {left} left{houseLeft}; {door1}; {sim.Orders.Total} voxels still marked{(sim.Orders.Total > 0 ? Marked() : "")}; {fell} fell{FellWhat()}; shut in {shut.Count}: {string.Join("; ", shut.Select(Where))}");

        // one of ours standing on the doorstep as the door is shut on it: it is put out of the leaves' way beside them,
        // not up on the wall over the doorway
        {
            var g = sim.Gates.Find(g => { var (gx, _, gz) = g.Middle; return gx >= hx0 - 8 && gx <= hx1 + 8 && gz >= hz0 - 8 && gz <= hz1 + 8; });
            string said = "no door";
            bool aside = false;
            if (g is { Ready: true })
            {
                sim.SetGateMode(g, GateMode.Open);
                Run(3);
                int sill = door.Y - 8, at = sim.StandNear(door.X + door.NX * 2, sill, door.Z + door.NZ * 2, 2);
                var by = sim.Spawn(Kinds.Worker, at);
                sim.Command(new[] { by }, at);
                Run(.5);
                float y0 = by.Y;
                sim.SetGateMode(g, GateMode.Shut);
                Run(3);
                aside = !by.Dead && g.Open == 0 && Math.Abs(by.Y - sill) <= 8;
                said = $"stood at {y0 - fg:0} over the field, the threshold {sill - fg}; after the door shut: {Where(by)}, the door open {g.Open:0.0}";
                by.Dead = true; by.DiedAt = -999;
                sim.SetGateMode(g, GateMode.Auto);
            }
            check("one standing on the doorstep as the door shuts on it is put aside on the ground, not up on the wall over the doorway", aside, said);
        }

        // a second house drawn on the first: its roof taken down, a floor and a ladder, a storey of walls and a roof
        var under = sim.HouseUnder(hx0, hz0, hx1, hz1);
        string no = under == null ? "no house under it" : sim.PlanStorey(under, hx0, hz0, hx1, hz1, Mat.Stone);
        fell = 0; fellAt.Clear();
        double s0 = sim.Time, still = s0;
        int depth0 = under?.Depth ?? 0, marked = int.MaxValue, roofDown = 0;
        var hp0 = crew.ToDictionary(u => u, u => u.Hp);
        // till nothing is marked, or it stops going down for a minute; what of the old roof comes down as it is taken
        // apart is told apart
        while (no == null && sim.Time - s0 < 1200 && (sim.Storeys.Count > 0 || sim.Orders.Total > 0 && sim.Time - still < 60))
        {
            Stock();
            int f0 = fell;
            bool takingDown = sim.Storeys.Count > 0;
            Run(1);
            // (and what is marked is counted from when the storey itself is marked out)
            if (takingDown) { roofDown += fell - f0; fell = f0; marked = int.MaxValue; }
            if (sim.Orders.Total < marked) { marked = sim.Orders.Total; still = sim.Time; }
        }
        took = still - s0;
        int storeyLeft = sim.Storeys.Count + (under != null && under.Depth > depth0 ? 0 : 1);
        Run(30);
        shut = crew.Where(u => !Free(u)).ToList();
        var hurt = crew.Where(u => u.Dead || u.Hp < hp0[u] - .5f).ToList();
        check("a second storey raised on the house goes up too, all of it: the old roof taken off from the ridge down with nothing of it coming down, nothing else falling, nobody hurt, and nobody left shut in on top",
            no == null && storeyLeft == 0 && sim.Orders.Total == 0 && roofDown == 0 && fell == 0 && hurt.Count == 0 && shut.Count == 0,
            $"{no ?? $"done in {took:0} s"}, the house {depth0} → {under?.Depth} high, {sim.Orders.Total} voxels still marked{(sim.Orders.Total > 0 ? Marked() : "")}; {roofDown} of the old roof came down; {fell} fell besides{(fell > 0 ? FellWhat() : "")}; hurt {hurt.Count}: {string.Join("; ", hurt.Select(Where))}; shut in {shut.Count}: {string.Join("; ", shut.Select(Where))}");

        // a house all of logs elsewhere on the field: its door hung too, on a doorstep of logs, no leaf of it laid in the
        // air to fall
        {
            int tx0 = fx0 + 76, tz0 = fz0 + 176, tx1 = tx0 + 36, tz1 = tz0 + 44;
            var logs = Shapes.House(w, site, tx0, tz0, tx1, tz1, new Stroke { Mat = Mat.Wood, Width = 4, Height = 32, Level = true }, Shapes.LevelTop(w, tx0, tz0, 32), out var door3, out string why3, out var arched3);
            fell = 0; fellAt.Clear();
            double l0 = sim.Time, lstill = l0;
            int lmarked = int.MaxValue;
            if (logs != null)
            {
                sim.DesignateBuild(logs); sim.Arch(arched3); sim.PlanHouse(door3, tx0, tz0, tx1, tz1);
                while (sim.Time - l0 < 900 && (sim.Orders.Total > 0 || sim.HouseDoors.Count > 0 || sim.Time - lstill < 20))
                {
                    Stock(); Run(1);
                    if (sim.Orders.Total < lmarked) { lmarked = sim.Orders.Total; lstill = sim.Time; }
                }
            }
            string door4 = Door(tx0, tz0, tx1, tz1, out bool hung2);
            int died = crew.Count(u => u.Dead);
            check("a house all of logs ends with its door hung on its doorstep, nothing fallen and nothing left waiting",
                logs != null && hung2 && fell == 0 && sim.Orders.Total == 0 && died == 0,
                $"{logs?.Count} voxels{(why3 != null ? $" ({why3})" : "")} in {lstill - l0:0} s; {door4}; {sim.Orders.Total} voxels still marked{(sim.Orders.Total > 0 ? Marked() : "")}; {fell} fell{FellWhat()}; {died} of the crew dead");
        }

        tower:
        // a round stone tower eight metres high, three across
        var tst = new Stroke { Mat = Mat.Stone, Width = 6, Height = 64, Level = true, Hollow = true };
        var tower = Shapes.Circle(w, site, fx0 + 240, fz0 + 200, 24, tst, Shapes.LevelTop(w, fx0 + 240, fz0 + 200, 64));
        fell = 0; fellAt.Clear();
        (took, left) = Build(tower, 1500);
        string towerLeft = Left(tower);
        if (Environment.GetEnvironmentVariable("BUILDERS_ONLY") == "tower")
        {
            for (int i = 0; i < tower.Count; i++)
            {
                if (sim.Orders.At(tower.X(i), tower.Y(i), tower.Z(i)) == 0) continue;
                int col = (tower.Z(i) >> 3) * sim.Nav.NX + (tower.X(i) >> 3);
                Console.WriteLine($"DBG left col {col % sim.Nav.NX},{col / sim.Nav.NX} at y {tower.Y(i) - fg}");
                break;
            }
            foreach (var sc in sim.Scaffolds) Console.WriteLine($"DBG scaffold site {(sc.Site >= 0 ? $"{sc.Site % sim.Nav.NX},{sc.Site / sim.Nav.NX}" : "-")} done {sc.Done:0} lowering {sc.Lowering} cols {string.Join(",", sc.Columns.Select(c => $"{c.i}/{c.k}:{c.y0}-{c.y1}"))} by {sc.By?.Name}");
            Console.WriteLine($"DBG fg cell {fg >> 3}");
        }
        // whoever is left up on it has a while to get down
        Run(90);
        shut = crew.Where(u => !Free(u)).ToList();
        check("a round tower of stone eight metres high goes up with nothing falling, and nobody is left on top or inside",
            left == 0 && fell == 0 && shut.Count == 0,
            $"{tower.Count} voxels in {took:0} s, {left} left{towerLeft}; {fell} fell{FellWhat()}; shut in {shut.Count}: {string.Join("; ", shut.Select(Where))}; the crew: {string.Join("; ", crew.Select(Where))}");

        stranded:
        // one of ours set on top of a block of stone eight metres high, three across, with no way down: it lashes a
        // column of scaffolding down its side, climbs down, and takes the scaffolding down after
        int bx0 = fx0 + 200, bz0 = fz0 + 40;
        for (int z = bz0; z < bz0 + 24; z++) for (int x = bx0; x < bx0 + 24; x++) for (int y = fg; y < fg + 64; y++) w.Set(x, y, z, Mat.Stone);
        Run(.5);
        var stranded = sim.Spawn(Kinds.Worker, sim.NearestStand(bx0 + 12, bz0 + 12, 2));
        (stranded.X, stranded.Y, stranded.Z) = sim.Nav.Feet(stranded.Cell);
        bool up = stranded.Y >= fg + 63, shutUp = !Free(stranded);
        int raised = 0;
        double downAt = -1;
        string kept = null;
        // scaffolding half raised is kept in a save, and its raiser goes on with it
        void Kept()
        {
            var ms = new System.IO.MemoryStream();
            sim.Save(ms, mp.Seed, "builders", "");
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
            var s1 = sim.Scaffolds[0];
            var s2 = sim2.Scaffolds.Count == sim.Scaffolds.Count ? sim2.Scaffolds[0] : null;
            int standing1 = s1.Voxels.Count(p => { var (x, y, z) = WaterPlanner.Unpack(p); return w.Get(x, y, z) == Mat.Scaffold; });
            int standing2 = s2 == null ? -1 : s2.Voxels.Count(p => { var (x, y, z) = WaterPlanner.Unpack(p); return w2.Get(x, y, z) == Mat.Scaffold; });
            bool raiser = s2?.By != null && s2.By.Id == stranded.Id && s2.By.Raising == s2;
            kept = $"{sim.Scaffolds.Count} → {sim2.Scaffolds.Count} scaffolds, {standing1} → {standing2} voxels standing, its raiser at it {raiser}";
            check("scaffolding half raised is kept in a save as it stands, and its raiser goes on with it",
                s2 != null && standing1 > 0 && standing2 == standing1 && s2.Next == s1.Next && s2.Column == s1.Column && raiser, kept);
        }
        for (int t = 0; t < 240 && (downAt < 0 || sim.Scaffolds.Count > 0); t++)
        {
            Run(.5);
            foreach (var sc in sim.Scaffolds) raised = Math.Max(raised, sc.Voxels.Count);
            if (kept == null && sim.Scaffolds.Count == 1 && sim.Scaffolds[0].Done < 0 && sim.Scaffolds[0].Voxels.Count > 100) Kept();
            if (downAt < 0 && stranded.Y < fg + 4 && Free(stranded)) downAt = sim.Time;
            if (Environment.GetEnvironmentVariable("BUILDERS_ONLY") == "stranded" && t % 4 == 0)
                Console.WriteLine($"DBG t {sim.Time:0} {Where(stranded)} cell {sim.Nav.I(stranded.Cell)},{sim.Nav.Y(stranded.Cell)},{sim.Nav.K(stranded.Cell)} path {stranded.PathI}/{stranded.Path?.Count} raising {stranded.Raising != null} scaffolds {sim.Scaffolds.Count} {string.Join(" ", sim.Scaffolds.Select(sc => $"[{sc.Voxels.Count} done {sc.Done:0} esc {sc.Escape} exit {(sc.Exit >= 0 ? $"{sim.Nav.I(sc.Exit)},{sim.Nav.Y(sc.Exit)},{sim.Nav.K(sc.Exit)}" : "-")} cols {string.Join(",", sc.Columns.Select(c => $"{c.i}/{c.k}:{c.y0}-{c.y1}"))}]"))}");
        }
        int standing = 0;
        for (int z = bz0 - 16; z < bz0 + 40; z++) for (int x = bx0 - 16; x < bx0 + 40; x++) for (int y = fg; y < fg + 72; y++) if (w.Get(x, y, z) == Mat.Scaffold) standing++;
        check("one of ours left on top of a block eight metres high with no way down lashes scaffolding down its side, climbs down, and takes it down after",
            up && shutUp && raised > 300 && downAt > 0 && standing == 0 && sim.Scaffolds.Count == 0,
            $"up {up}, shut in {shutUp}; scaffolding of {raised} voxels; down {(downAt > 0 ? "yes" : "no")} ({Where(stranded)}); left standing {standing} voxels, {sim.Scaffolds.Count} scaffolds");
        stranded.Dead = true; stranded.DiedAt = -999;

        // scaffolding standing comes down as soon as a raider comes near it, lest the horde climb it
        var second = sim.Spawn(Kinds.Worker, sim.NearestStand(bx0 + 12, bz0 + 12, 2));
        (second.X, second.Y, second.Z) = sim.Nav.Feet(second.Cell);
        for (int t = 0; t < 200 && !(sim.Scaffolds.Count > 0 && sim.Scaffolds[0].Done >= 0); t++) Run(.5);
        bool stood = sim.Scaffolds.Count > 0 && sim.Scaffolds[0].Done >= 0;
        int foot = stood ? sim.Scaffolds[0].Columns[0].i << 3 : bx0;
        var raider = sim.Spawn(Kinds.Goblin, sim.NearestStand(foot - 40, bz0 + 12, 2));
        raider.Guard = true; raider.Home = raider.Cell;
        Run(2);
        int afterRaid = 0;
        for (int z = bz0 - 16; z < bz0 + 40; z++) for (int x = bx0 - 16; x < bx0 + 40; x++) for (int y = fg; y < fg + 72; y++) if (w.Get(x, y, z) == Mat.Scaffold) afterRaid++;
        check("scaffolding standing comes down as soon as a raider comes near, lest the horde climb it",
            stood && afterRaid == 0 && sim.Scaffolds.Count == 0, $"stood {stood}; {afterRaid} voxels of it left, {sim.Scaffolds.Count} scaffolds");
        raider.Dead = second.Dead = true; raider.DiedAt = second.DiedAt = -999;

        // the scaffolding the crew put up for the works is all taken down when they are done
        for (int t = 0; t < 240 && sim.Scaffolds.Count > 0; t++) Run(.5);
        int anywhere = 0;
        for (int z = fz0; z < fz0 + 280; z++) for (int x = fx0; x < fx0 + 320; x++) for (int y = fg; y < fg + 120; y++) if (w.Get(x, y, z) == Mat.Scaffold) anywhere++;
        check("the scaffolding raised for the works is taken down again when they are done, its logs back in the stores",
            sim.Scaffolds.Count == 0 && anywhere == 0, $"{sim.Scaffolds.Count} scaffolds, {anywhere} voxels of it left");

        // a ditch three metres across and two deep right across the field, and a deck of planks a voxel thick laid over
        // it: walked across, there being no way round
        int px0 = fx0 + 40, pz0 = fz0, px1 = px0 + 24, pz1 = fz0 + 280;
        for (int z = pz0; z < pz1; z++) for (int x = px0; x < px1; x++) for (int y = fg - 16; y < fg; y++) w.Set(x, y, z, Mat.Air);
        for (int z = pz0 + 120; z < pz0 + 152; z++) for (int x = px0 - 8; x < px1 + 8; x++) w.Set(x, fg - 1, z, Mat.Wood);
        Run(.5);
        int mid = sim.Nav.CellOf(px0 + 12, fg + 1, pz0 + 136);
        bool ground = sim.Nav.Ground(sim.Nav.I(mid), sim.Nav.Y(mid) - 1, sim.Nav.K(mid));
        int from = sim.NearestStand(px0 - 30, pz0 + 136, 2), to = sim.NearestStand(px1 + 30, pz0 + 136, 2);
        var walker = sim.Spawn(Kinds.Worker, from);
        walker.Hold = true;
        bool sent = sim.GoTo(walker, to);
        float lowest = 1e9f;
        for (int i = 0; i < 400 && walker.Cell != to; i++) { Run(.05); if (walker.X > px0 && walker.X < px1) lowest = Math.Min(lowest, walker.Y); }
        check("a deck of planks a voxel thick over a pit is ground: a worker walks across it and does not fall in",
            ground && sent && walker.Cell == to && lowest >= fg - 1,
            $"ground {ground}, a way {sent}, across {walker.Cell == to}; lowest over the pit {lowest - fg:0.0} voxels (its bottom {-16})");
    }
}
