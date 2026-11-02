using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// A town's day (--living): on a level field a dwelling with its bunks, a campfire with benches round it, a well, a
/// flower bed, a pond and a sanctuary; six workers and four soldiers with nothing to do; the valley's clock as the map
/// keeps it, no raid at nightfall. Through a day and a night: pastimes taken up and kept changing, nobody standing still
/// for long; rings of two and three, dice, fishing, sweeping, the flower bed, the sanctuary; a meal at noon; greetings
/// and words said; to bed at night, in the bunks and by the fire, every other soldier on watch; work marked in the night
/// taken up at once; up at dawn. LIVING_DEBUG=1 prints who does what every ten seconds.
/// </summary>
static class LivingBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.SetSource(new MapRaids());
        sim.WildTarget = 0;
        bool debug = Environment.GetEnvironmentVariable("LIVING_DEBUG") == "1";
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        // a field of level turf west of the citadel, bare of trees
        int fx0 = (site.KeepX - 460) & ~7, fz0 = (site.KeepZ - 140) & ~7, fg = w.SurfAt(fx0 + 160, fz0 + 140);
        for (int z = fz0; z < fz0 + 280; z++)
            for (int x = fx0; x < fx0 + 320; x++)
                for (int y = fg - 8; y < fg + 120; y++) w.Set(x, y, z, y < fg ? (y < fg - 4 ? Mat.Rock : y == fg - 1 ? Mat.Grass : Mat.Soil) : Mat.Air);
        Run(.5);
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        sim.Stock[Stockpile.Wood] = 4000.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Stone] = 4000.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Earth] = 4000.0 * VoxelWorld.PerM3;
        sim.InstantLabour = true;
        // the dwelling, its door to the east
        int hx0 = fx0 + 30, hz0 = fz0 + 40, hx1 = hx0 + 36, hz1 = hz0 + 55;
        var house = Shapes.House(w, site, hx0, hz0, hx1, hz1, new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true }, Shapes.LevelTop(w, (hx0 + hx1) / 2, (hz0 + hz1) / 2, 32), out var door, out string why, out var arched);
        if (house == null) { check("a town's day: the dwelling is planned", false, why); return; }
        sim.DesignateBuild(house);
        sim.Arch(arched);
        sim.PlanHouse(door, hx0, hz0, hx1, hz1);
        // a campfire with a bench on each side, a well, a flower bed
        int cx = fx0 + 170, cz = fz0 + 100;
        string Put(DecorKind k, int x, int z, int rot) => sim.PlaceDecor(k, x, z, rot);
        var put = new[]
        {
            Put(DecorKind.Campfire, cx, cz, 0), Put(DecorKind.Bench, cx, cz - 15, 0), Put(DecorKind.Bench, cx, cz + 15, 2),
            Put(DecorKind.Bench, cx - 16, cz, 3), Put(DecorKind.Bench, cx + 16, cz, 1), Put(DecorKind.Well, cx + 60, cz + 40, 0),
            Put(DecorKind.FlowerBed, cx - 50, cz + 60, 0),
        };
        for (int i = 0; i < 4; i++) { sim.CompleteAll(); Run(2); }
        var dwelling = sim.Zones.Find(z => z.Kind == Zone.Dwelling);
        // a pond of four metres by six and a metre deep by the field's south edge; a sanctuary in the field's corner
        int px0 = fx0 + 220, pz0 = fz0 + 200;
        for (int z = pz0; z < pz0 + 48; z++)
            for (int x = px0; x < px0 + 32; x++)
                for (int y = fg - 8; y < fg; y++) w.Set(x, y, z, Mat.Water);
        sim.AddZone(fx0 + 250, fz0 + 20, fx0 + 300, fz0 + 70, Zone.Sanctum, 0);
        sim.InstantLabour = false;
        Run(2);
        Console.WriteLine($"     the town: dwelling {(dwelling == null ? "none" : $"closed {dwelling.Closed}, {dwelling.Beds} bunks")}, things placed: {string.Join(", ", put.Select(p => p ?? "ok"))}, {sim.Yards.Count(y => y.Ready)} standing");

        // the household about the campfire
        var folk = new List<Unit>();
        var kinds = new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Swordsman, Kinds.Spearman, Kinds.Archer, Kinds.Swordsman };
        for (int i = 0; i < kinds.Length; i++)
            folk.Add(sim.Spawn(kinds[i], sim.NearestStand(cx - 40 + i % 5 * 20, cz - 30 + i / 5 * 60, kinds[i].Move.Height)));
        var workers = folk.Where(u => u.T == Kinds.Worker).ToList();
        var soldiers = folk.Where(u => u.T != Kinds.Worker).ToList();
        foreach (var s in soldiers) s.Post = s.Cell;

        // the morning to the evening, looked at every half second
        var seen = new Dictionary<Pastime, int>();
        var still = new Dictionary<int, (int cell, double since)>();
        double stillMost = 0; Unit stillOne = null;
        int threes = 0, eatingAtNoon = 0;
        void Look(bool day)
        {
            foreach (var u in folk)
            {
                if (u.Dead) continue;
                var p = u.Pastime == Pastime.Stroll ? u.After == Pastime.None ? Pastime.Stroll : u.After : u.Pastime;
                seen[p] = seen.GetValueOrDefault(p) + 1;
                if (u.Group is { Members.Count: 3 }) threes++;
                // stood at nothing in one cell: how long
                bool idle = u.Pastime is Pastime.None or Pastime.Look && u.State == UnitState.Idle;
                if (!idle || !day || !still.TryGetValue(u.Id, out var s) || s.cell != u.Cell) still[u.Id] = (u.Cell, sim.Time);
                else if (sim.Time - s.since > stillMost) { stillMost = sim.Time - s.since; stillOne = u; }
            }
        }
        void Print(string when)
        {
            if (!debug) return;
            Console.WriteLine($"     {when} {sim.Time:0} s, share {sim.Clock.Share:0.00}, night {sim.Clock.Night}: "
                + string.Join("; ", folk.Select(u => $"{u.T.Key}#{u.Id} {u.State}/{u.Pastime}{(u.Pastime == Pastime.Stroll ? ">" + u.After : "")}{(u.Group != null ? $"[{u.Group.Members.Count}]" : "")}")));
        }
        sim.Clock.Set(new ClockTime(1, 120));
        int greet0 = sim.Greetings, said0 = sim.WordsSaid;
        int said = 0;
        sim.Spoke += (_, _, _) => said++;
        for (int t = 0; t < 2 * 420; t++)
        {
            Run(.5);
            Look(day: !sim.Clock.Night && sim.Clock.Darkness < .3f);
            if (t % 20 == 0) Print("day");
            if (sim.Noon) foreach (var u in folk) if (u.Pastime == Pastime.Eat) eatingAtNoon++;
        }
        int ate = folk.Count(u => u.AteDay == 1);
        var kindsSeen = seen.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToHashSet();
        var town = new[] { Pastime.Talk, Pastime.Dice, Pastime.Sharpen, Pastime.Fish, Pastime.Sweep, Pastime.Tend, Pastime.Kneel, Pastime.Eat, Pastime.Fetch, Pastime.Drink, Pastime.Warm, Pastime.Sit };
        int variety = town.Count(kindsSeen.Contains);
        Console.WriteLine($"     the day: {string.Join(", ", seen.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))}");
        check("a town's day: those with nothing to do take up many pastimes, in rings of two and three, at dice, fishing, sweeping, at the flowers and in the sanctuary",
            variety >= 8 && kindsSeen.Contains(Pastime.Talk) && threes > 0 && sim.Rings >= 3 && (kindsSeen.Contains(Pastime.Fish) || kindsSeen.Contains(Pastime.Sweep) || kindsSeen.Contains(Pastime.Tend)) && kindsSeen.Contains(Pastime.Dice),
            $"{variety} of {town.Length} of the town's pastimes seen ({string.Join(", ", town.Where(kindsSeen.Contains))}); {sim.Rings} rings, of three seen {threes} times; fished {sim.Fished}, swept {sim.Swept}, tended {sim.Tended}, knelt {sim.Knelt}, sharpened {sim.Sharpened}, dice {sim.Diced}");
        check("a town's day: nobody off duty stands at nothing in one place for long", stillMost < 45,
            $"the longest {stillMost:0} s ({stillOne?.T.Key}#{stillOne?.Id})");
        check("a town's day: at noon those with nothing to do eat, once a day", ate >= 8 && eatingAtNoon > 0 && sim.Meals == ate,
            $"{ate} of {folk.Count} ate on day 1 ({sim.Meals} meals), {eatingAtNoon} looks at someone eating at noon");
        check("a town's day: ours greet each other in passing and talk", sim.Greetings - greet0 >= 5 && said >= 10,
            $"{sim.Greetings - greet0} greetings, {said} words said");

        // the night: to bed, workers in the bunks (or by the fire), every other soldier on watch
        sim.Clock.Set(new ClockTime(1, DayClock.DayLength + 1));
        Run(40);
        Print("night");
        int asleep = workers.Count(u => u.Pastime == Pastime.Sleep || u.Pastime == Pastime.Stroll && u.After == Pastime.Sleep);
        // (in a bunk or on the way to the one it took, as the asleep are counted: slower over the turf now than along the
        // ways, two of the five were still crossing the yard to theirs at forty seconds)
        int inBunks = folk.Count(u => (u.Pastime == Pastime.Sleep || u.Pastime == Pastime.Stroll && u.After == Pastime.Sleep) && u.Berth is { Key: >= 0 });
        int soldiersAsleep = soldiers.Count(u => u.Pastime == Pastime.Sleep), watching = soldiers.Count(u => u.Pastime is Pastime.Watch or Pastime.Look);
        int bunks = dwelling?.Beds ?? 0;
        check("a town's night: the workers go to bed, in the bunks of the dwelling while there are bunks, every other soldier on watch",
            asleep >= 5 && inBunks >= Math.Min(bunks, 4) && soldiersAsleep >= 1 && watching >= 1,
            $"{asleep} of {workers.Count} workers asleep or on the way, {inBunks} in the {bunks} bunks; soldiers {soldiersAsleep} asleep, {watching} on watch");
        // lying in its bunk: on the bedding, its feet at the foot end
        var lying = folk.FirstOrDefault(u => u.Pastime == Pastime.Sleep && u.Berth is { Key: >= 0 });
        bool onBedding = lying != null && w.Get((int)(lying.X - MathF.Cos(lying.Facing) * 5), (int)(lying.Y + lying.SeatH) - 1, (int)(lying.Z - MathF.Sin(lying.Facing) * 5)) == Mat.Bedding;
        check("a town's night: one asleep in a bunk lies on its bedding", onBedding,
            lying == null ? "nobody in a bunk" : $"#{lying.Id} at {lying.X:0},{lying.Y + lying.SeatH:0},{lying.Z:0} facing {lying.Facing:0.00}");

        // work marked in the night: a worker gets up for it at once, and back to bed when it is done
        int wx = cx + 40, wz = cz - 70;
        int dug0 = sim.DesignateRemove(Shapes.Box(w, site, wx, wz, wx + 8, wz + 8, new Stroke { Height = 4, Dig = true }, 0));
        double upAt = -1;
        for (int t = 0; t < 40 && upAt < 0; t++) { Run(.5); if (workers.Any(u => u.State == UnitState.Work || u.Site >= 0)) upAt = t * .5 + .5; }
        sim.CancelArea(wx - 4, wz - 4, wx + 12, wz + 12);
        Run(20);
        int backAsleep = workers.Count(u => u.Pastime == Pastime.Sleep || u.Pastime == Pastime.Stroll && u.After == Pastime.Sleep);
        check("a town's night: work marked at night is taken up at once, and the worker goes back to bed after",
            dug0 > 0 && upAt >= 0 && upAt < 15 && backAsleep >= 5,
            $"{dug0} voxels marked; a worker at it after {(upAt < 0 ? "never" : upAt + " s")}; {backAsleep} asleep 20 s after it was taken off");

        // dawn: everyone up
        sim.Clock.Break(sim);
        Run(15);
        Print("dawn");
        int stillAsleep = folk.Count(u => u.Pastime == Pastime.Sleep);
        check("a town's dawn: everyone gets up, with a stretch", stillAsleep == 0 && seen.ContainsKey(Pastime.Stretch),
            $"{stillAsleep} still asleep 15 s after daybreak");

        // the alarm in the night wakes everyone at once
        sim.Clock.Set(new ClockTime(2, DayClock.DayLength + 1));
        Run(40);
        int before = folk.Count(u => u.Pastime == Pastime.Sleep);
        sim.RaiseAlarm();
        Run(1);
        int after = folk.Count(u => u.Pastime == Pastime.Sleep);
        sim.StandDown();
        check("a town's night: the alarm wakes those asleep at once", before >= 4 && after == 0, $"{before} asleep, {after} after a second of the alarm");
        foreach (var u in folk) { u.Dead = true; u.DiedAt = -999; }
        BuildsWithLife(check);
    }

    /// <summary>
    /// The town's life does not keep work from being done: on three valleys four workers build a house of stone, two
    /// soldiers idle about, a campfire, a bench and a well by it to draw them off, the clock as the map keeps it from the
    /// morning on through noon and the night; most of the houses (two of three) stand finished in the time a house
    /// takes, and nobody is left shut in by it.
    /// </summary>
    static void BuildsWithLife(Action<string, bool, string> check)
    {
        int done = 0;
        var said = new List<string>();
        // each begun at another hour: just before noon, at dusk with the night coming on, in the morning
        foreach (var (seed, hour) in new (uint, double)[] { (7, 240), (23, 570), (41, 120) })
        {
            var mp = new MapParams { Seed = seed };
            var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
            var site = MapGen.Generate(w, mp);
            w.Compact();
            var sim = new Sim(w, site);
            sim.SetSource(new MapRaids());
            sim.WildTarget = 0;
            void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
            int fx0 = (site.KeepX - 460) & ~7, fz0 = (site.KeepZ - 140) & ~7, fg = w.SurfAt(fx0 + 160, fz0 + 140);
            for (int z = fz0; z < fz0 + 280; z++)
                for (int x = fx0; x < fx0 + 320; x++)
                    for (int y = fg - 8; y < fg + 120; y++) w.Set(x, y, z, y < fg ? (y < fg - 4 ? Mat.Rock : y == fg - 1 ? Mat.Grass : Mat.Soil) : Mat.Air);
            Run(.5);
            foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
            sim.Stock[Stockpile.Wood] = 4000.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Stone] = 4000.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Earth] = 4000.0 * VoxelWorld.PerM3;
            sim.InstantLabour = true;
            int cx = fx0 + 170, cz = fz0 + 100;
            sim.PlaceDecor(DecorKind.Campfire, cx, cz, 0); sim.PlaceDecor(DecorKind.Bench, cx, cz - 15, 0); sim.PlaceDecor(DecorKind.Well, cx + 60, cz + 40, 0);
            sim.CompleteAll(); Run(2);
            sim.InstantLabour = false;
            var crew = new List<Unit>();
            for (int i = 0; i < 4; i++) crew.Add(sim.Spawn(Kinds.Worker, sim.NearestStand(cx - 30 + i * 12, cz + 30, 2)));
            for (int i = 0; i < 2; i++) crew.Add(sim.Spawn(Kinds.Swordsman, sim.NearestStand(cx + 30, cz - 30 + i * 12, 2)));
            sim.Clock.Set(new ClockTime(1, hour));
            int hx0 = fx0 + 30, hz0 = fz0 + 40, hx1 = hx0 + 36, hz1 = hz0 + 55;
            var house = Shapes.House(w, site, hx0, hz0, hx1, hz1, new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true }, Shapes.LevelTop(w, (hx0 + hx1) / 2, (hz0 + hz1) / 2, 32), out var door, out _, out var arched);
            if (house == null) { said.Add($"seed {seed}: no house"); continue; }
            sim.DesignateBuild(house);
            sim.Arch(arched);
            sim.PlanHouse(door, hx0, hz0, hx1, hz1);
            double t0 = sim.Time, built = -1;
            for (int s = 0; s < 1500 && built < 0; s++)
            {
                Run(1);
                var zone = sim.Zones.Find(z => z.Kind == Zone.Dwelling);
                if (sim.Orders.Total == 0 && sim.Storeys.Count == 0 && zone is { Closed: true }) built = sim.Time - t0;
            }
            Run(30);
            int shut = crew.Count(u => !u.Dead && sim.Paths.Reach(u.Cell, u.T.Move, 240, true) < 240);
            if (built >= 0 && shut == 0) done++;
            said.Add($"seed {seed}: {(built < 0 ? "not done in 1500 s" : $"done in {built:0} s")}, {sim.Meals} meals, {sim.SleptInBunks + sim.SleptOut} to sleep, {sim.Rings} rings, shut in {shut}");
        }
        check("the town's life keeps no work from being done: a house of stone built by four workers with their noon and their night, on three valleys, most of them finished and nobody shut in",
            done >= 2, string.Join("; ", said));
    }
}
