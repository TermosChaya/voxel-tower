using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VoxelTower.Core;

/// <summary>
/// The bench's ground for the big map (--world, Campaign.World.cs, MapThing.cs, MapWalk.cs, MapAI.World.cs): each size
/// made in good time and the same for the same seed; its zones walled off from each other but for guarded passes, every
/// one of them reached from ours through them; its things sparse; mines flagged and paying each dawn; piles, campfires
/// and chests had once, a chest's choice, a windmill once a week; moves spent by the ground and whole again at dawn, too
/// few for the next step waited out; «До рассвета» landing on the dawn; sixty days of the horde on an XL map quickly;
/// a mid-game XL map written small and read back the same.
/// </summary>
static class WorldBench
{
    /// <summary>All of a big map's look and state as a line: the same line, the same map.</summary>
    static string Print(Campaign c)
    {
        var sb = new StringBuilder();
        sb.Append($"{c.Width}x{c.Height} {c.Zones.Count} d{c.Clock.Day} g{c.Gold:0.00} r{string.Join(",", c.Rare.Select(x => x.ToString("0.00")))}|");
        unchecked
        {
            long h = 17;
            foreach (var x in c.Cells)
            {
                h = h * 31 + (int)x.Ground; h = h * 31 + (int)x.Relief; h = h * 31 + x.Roads; h = h * 31 + x.Rivers; h = h * 31 + x.Zone;
                h = h * 31 + (int)x.Owner; h = h * 31 + x.Sum.Keep.Count; h = h * 31 + (x.Visited ? 1 : 0);
                if (x.Thing is { } t) { h = h * 31 + (int)t.Kind; h = h * 31 + (int)t.Res; h = h * 31 + (int)t.Owner; h = h * 31 + (t.Taken ? 1 : 0); h = h * 31 + t.Stock; h = h * 31 + t.Week; }
                if (c.Explored(x)) h = h * 31 + 7;
            }
            sb.Append(h);
        }
        foreach (var p in c.Parties) sb.Append($"|{p.Owner}{p.Q},{p.R} {p.Party.Members.Count} m{p.MovesLeft}/{p.MovesMax} w{p.Path?.Count ?? 0}");
        return sb.ToString();
    }

    /// <summary>The hexes reached from one over passable hexes, guarded ones let through or not.</summary>
    static HashSet<MapCell> Reach(Campaign c, MapCell from, bool throughGuards)
    {
        var seen = new HashSet<MapCell> { from };
        var q = new Queue<MapCell>();
        q.Enqueue(from);
        while (q.Count > 0)
        {
            var a = q.Dequeue();
            for (int s = 0; s < 6; s++)
            {
                var b = c.Neighbour(a, s);
                if (b == null || !b.Passable || seen.Contains(b)) continue;
                if (!throughGuards && b.Pass && b.Sum.Keep.Count > 0) continue;
                seen.Add(b);
                q.Enqueue(b);
            }
        }
        return seen;
    }

    /// <summary>A hero of ours stood on a hex at once, his moves whole.</summary>
    static MapParty Put(Campaign c, MapParty p, MapCell at)
    {
        p.Q = at.Q; p.R = at.R; p.CameQ = at.Q; p.CameR = at.R; p.Path = null; p.Progress = 0; p.StepCost = 0;
        p.MovesMax = p.MovesLeft = MapWalk.MaxMoves(p, c.Week);
        return p;
    }

    /// <summary>The clock run on till a party stands still (or a day is out).</summary>
    static void WalkOut(Campaign c, MapParty p, double most = DayClock.Cycle)
    {
        for (double t = 0; t < most && p.Moving && !p.Tired; t += 1) c.Advance(1);
    }

    /// <summary>A free hex next to a hex of a thing, from where one step walks onto it.</summary>
    static MapCell Beside(Campaign c, MapCell at)
    {
        for (int s = 0; s < 6; s++)
            if (c.Neighbour(at, s) is { } b && b.Passable && b.Thing == null && b.Sum.Keep.Count == 0 && !b.Pass) return b;
        return null;
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();

        // ---- each size, in good time, the same each time
        foreach (int side in Campaign.Sides)
        {
            double t0 = BenchClock.CpuMs();
            var a = Campaign.NewWorld(21, side);
            double made = BenchClock.CpuMs() - t0;
            var b = Campaign.NewWorld(21, side);
            var other = Campaign.NewWorld(22, side);
            int things = a.Cells.Count(x => x.Thing != null), open = a.Cells.Count(x => x.Passable);
            check($"a big map {Campaign.SizeName(side)} ({side}×{side}) is made in good time and the same for the same seed",
                made < 2000 && Print(a) == Print(b) && Print(a) != Print(other) && a.Cells.Count == side * side,
                $"{made:0} ms, {a.Zones.Count} zones, {things} things on {open} open hexes ({100.0 * things / open:0.0} %)");
        }

        foreach (int side in new[] { 36, 108 })
        {
            var c = Campaign.NewWorld(5, side);
            var home = c.Home;
            // walled off: from ours, without the passes' keepers beaten, only our zone is reached
            var shut = Reach(c, home, false);
            var zonesShut = shut.Select(x => x.Zone).Distinct().Count();
            var all = Reach(c, home, true);
            int reached = c.Zones.Count(z => all.Contains(c.Cell(z.Col, z.Row)));
            int passes = c.Cells.Count(x => x.Pass), kept = c.Cells.Count(x => x.Pass && x.Sum.Keep.Count > 0);
            check($"the zones of the {Campaign.SizeName(side)} map are walled off but for guarded passes, and all are reached from ours through them",
                zonesShut == 1 && reached == c.Zones.Count && kept > 0,
                $"{c.Zones.Count} zones, {passes} pass hexes, {kept} kept; without fighting {zonesShut} zone(s), through the passes {reached}");
            // sparse, and something in every zone
            int things = c.Cells.Count(x => x.Thing != null), open = c.Cells.Count(x => x.Passable);
            var kinds = c.Cells.Where(x => x.Thing is { Kind: ThingKind.Mine }).Select(x => x.Thing.Res).Distinct().Count();
            bool everyZone = c.Zones.All(z => c.Cells.Any(x => x.Zone == z.Id && x.Thing != null && x.Thing.Kind != ThingKind.Monster));
            int realms = c.Zones.Count(z => z.Kind == ZoneKind.Horde);
            check($"the things of the {Campaign.SizeName(side)} map lie sparse, in every zone, the horde's realms with their towns",
                things > open * .02 && things < open * .12 && everyZone && realms == Math.Clamp(side / 36, 1, 4) && kinds >= (side >= 72 ? 6 : 4)
                && c.Lairs.Count >= realms,
                $"{things} things on {open} open hexes, {kinds} kinds of mines, {realms} realms, {c.Lairs.Count} towns and lairs of the horde");
        }

        // ---- walled off on many seeds: no zone gone into from another but by its pass, every zone reached through them
        {
            int bad = 0, maps = 0;
            var said = new List<string>();
            foreach (int side in new[] { 36, 72 })
                for (uint seed = 101; seed < 109; seed++)
                {
                    var c = Campaign.NewWorld(seed, side);
                    maps++;
                    int leaks = 0;
                    foreach (var x in c.Cells)
                        for (int s = 0; s < 6; s++)
                            if (c.Neighbour(x, s) is { } y && x.Passable && y.Passable && x.Zone != y.Zone && !(x.Pass && y.Pass)) leaks++;
                    var all = Reach(c, c.Home, true);
                    int reached = c.Zones.Count(z => all.Contains(c.Cell(z.Col, z.Row)));
                    if (leaks > 0 || reached < c.Zones.Count) { bad++; said.Add($"{side}/{seed}: {leaks} leaks, {reached} of {c.Zones.Count}"); }
                }
            check("on sixteen maps of two sizes every zone is walled off but by its passes and reached from ours", bad == 0,
                bad == 0 ? $"{maps} maps" : string.Join("; ", said));
        }

        // ---- a hero's things: mines, piles, campfires, chests, windmills
        {
            var c = Campaign.NewWorld(9, 36);
            var hero = c.AddHero();
            var home = c.Home;
            MapCell Find(Func<MapCell, bool> f) => c.Cells.Where(f).OrderBy(x => Campaign.Distance(x.Q, x.R, home.Q, home.R)).FirstOrDefault();
            // a mine of ours: walked onto, flagged, paying at dawn
            var mine = Find(x => x.Thing is { Kind: ThingKind.Mine, Owner: Owner.None } && x.Sum.Keep.Count == 0 && Beside(c, x) != null);
            Put(c, hero, Beside(c, mine));
            c.Send(hero, mine);
            WalkOut(c, hero);
            bool flagged = mine.Thing.Owner == Owner.Ours;
            double before = c.StockOf(mine.Thing.Res);
            c.SkipToDawn();
            double after = c.StockOf(mine.Thing.Res);
            check("a mine walked onto is ours and pays its day at dawn",
                flagged && Math.Abs(after - before - mine.Thing.Amount) < .01,
                $"{mine.Thing.Name}: flagged {flagged}, {before:0.##} → {after:0.##} (a day {mine.Thing.Amount})");
            // a pile: once
            var pile = Find(x => x.Thing is { Kind: ThingKind.Pile, Taken: false } && x.Sum.Keep.Count == 0 && Beside(c, x) != null);
            var near = Beside(c, pile);
            Put(c, hero, near);
            double p0 = c.StockOf(pile.Thing.Res);
            c.Send(hero, pile); WalkOut(c, hero);
            double p1 = c.StockOf(pile.Thing.Res);
            c.Send(hero, near); WalkOut(c, hero);
            Put(c, hero, near);
            c.Send(hero, pile); WalkOut(c, hero);
            double p2 = c.StockOf(pile.Thing.Res);
            check("a pile is had once", Math.Abs(p1 - p0 - pile.Thing.Amount) < .01 && Math.Abs(p2 - p1) < .01,
                $"{MapThing.NameOf(pile.Thing.Res)} {p0:0.##} → {p1:0.##} → {p2:0.##}");
            // a chest: its choice waits, gold or experience; once
            var chest = Find(x => x.Thing is { Kind: ThingKind.Chest, Taken: false } && x.Sum.Keep.Count == 0 && Beside(c, x) != null);
            string chestSaid = "no chest";
            bool chestOk = false;
            if (chest != null)
            {
                Put(c, hero, Beside(c, chest));
                c.Send(hero, chest); WalkOut(c, hero);
                bool waits = c.Chests.Exists(x => x.cell == chest);
                int xp0 = hero.Party.Commander.Hero.Xp;
                c.OpenChest(chest, gold: false);
                int xp1 = hero.Party.Commander.Hero.Xp;
                Put(c, hero, Beside(c, chest));
                c.Send(hero, chest); WalkOut(c, hero);
                chestOk = waits && xp1 - xp0 == Campaign.ChestOf(chest.Thing).xp && !c.Chests.Exists(x => x.cell == chest);
                chestSaid = $"waits {waits}, experience {xp0} → {xp1}, after: waits again {c.Chests.Exists(x => x.cell == chest)}";
            }
            check("a chest's choice waits for the player, gives once", chestOk, chestSaid);
            // a windmill (or a water wheel): once a week
            var mill = Find(x => x.Thing is { Kind: ThingKind.Windmill or ThingKind.WaterWheel } && x.Sum.Keep.Count == 0 && Beside(c, x) != null);
            string millSaid = "no windmill";
            bool millOk = false;
            if (mill != null)
            {
                var res = mill.Thing.Kind == ThingKind.WaterWheel ? Res.Gold : mill.Thing.Res;
                var by = Beside(c, mill);
                double m0 = c.StockOf(res);
                Put(c, hero, by); c.Send(hero, mill); WalkOut(c, hero);
                double m1 = c.StockOf(res);
                Put(c, hero, by); c.Send(hero, mill); WalkOut(c, hero);
                double m2 = c.StockOf(res);
                while (c.Week == 0) c.SkipToDawn();
                double m3 = c.StockOf(res);
                Put(c, hero, by); c.Send(hero, mill); WalkOut(c, hero);
                double m4 = c.StockOf(res);
                millOk = m1 > m0 && Math.Abs(m2 - m1) < .01 && m4 > m3;
                millSaid = $"{mill.Thing.Name}: {m0:0} → {m1:0} → again {m2:0}; next week {m3:0} → {m4:0}";
            }
            check("a windmill or a water wheel gives once a week", millOk, millSaid);
        }

        // ---- moves: spent by the ground, waited out, whole at dawn; the days of a way
        {
            var c = Campaign.NewWorld(13, 72);
            var hero = c.AddHero();
            var home = c.Home;
            // a stretch of open ground, no road, no river: a step costs its ground
            var at = c.Cells.Where(x => x.Passable && x.Roads == 0 && x.Thing == null && x.Sum.Keep.Count == 0 && !x.Pass)
                .Where(x => Enumerable.Range(0, 6).Any(s => c.Neighbour(x, s) is { } n && n.Passable && n.Roads == 0 && n.Rivers == 0 && n.Thing == null && n.Sum.Keep.Count == 0 && !n.Pass))
                .OrderBy(x => Campaign.Distance(x.Q, x.R, home.Q, home.R)).First();
            var to = Enumerable.Range(0, 6).Select(s => c.Neighbour(at, s)).First(n => n != null && n.Passable && n.Roads == 0 && n.Rivers == 0 && n.Thing == null && n.Sum.Keep.Count == 0 && !n.Pass);
            Put(c, hero, at);
            int m0 = hero.MovesLeft, cost = MapWalk.Ground(to);
            c.Send(hero, to);
            double t = 0;
            while (hero.Moving && t < 400) { c.Advance(1); t++; }
            int spent = m0 - hero.MovesLeft;
            check("a step of the big map costs its ground's moves and takes their time",
                spent == cost && Math.Abs(t - MapWalk.Seconds(cost)) <= 1.5,
                $"{to.Ground}/{to.Relief}: {spent} moves of {m0} (should {cost}), {t:0} s (should {MapWalk.Seconds(cost):0})");
            // too few moves for the next step: he waits for the dawn, then goes on
            Put(c, hero, at);
            hero.MovesLeft = cost - 1;
            c.Send(hero, to);
            c.Advance(5);
            bool waited = hero.Tired && hero.Q == at.Q && hero.R == at.R;
            int day = c.Clock.Day;
            c.SkipToDawn();
            // (whole at the dawn, and he set out on the step at once: paid for it already)
            bool whole = c.Clock.Day == day + 1 && c.Clock.At < 1 && hero.MovesLeft + hero.StepCost == hero.MovesMax;
            WalkOut(c, hero);
            check("with too few moves for the next step a hero waits for the dawn; «До рассвета» lands on it, his moves whole, and he goes on",
                waited && whole && hero.Q == to.Q && hero.R == to.R,
                $"waited {waited}, day {day} → {c.Clock.Day} at {c.Clock.At:0.0} s, moves whole {whole}, came {hero.Q == to.Q && hero.R == to.R}");
            // a long way: its days as its moves give them
            var far = c.Cells.Where(x => x.Passable && x.Zone == home.Zone && x.Sum.Keep.Count == 0 && x.Thing == null)
                .OrderByDescending(x => Campaign.Distance(x.Q, x.R, home.Q, home.R)).First();
            Put(c, hero, home);
            var plan = MapWalk.Plan(c, hero, far.Col, far.Row);
            int moves = 0, days = 0;
            bool daysOk = plan != null && plan.Count > 0;
            var a0 = home;
            int left = hero.MovesLeft;
            if (plan != null)
                foreach (var s in plan)
                {
                    var b = c.Cell(s.Col, s.Row);
                    int k = MapWalk.Cost(a0, b, MapPath.Side(a0, b));
                    if (k > left) { days++; left = hero.MovesMax; }
                    left -= k; moves += k;
                    daysOk &= s.Day == days;
                    a0 = b;
                }
            check("a way planned on the big map has its days as the moves give them",
                daysOk, $"{plan?.Count ?? -1} hexes, {moves} moves, last day {(plan?.Count > 0 ? plan[^1].Day : -1)} (counted {days}), a day's moves {hero.MovesMax}");
        }

        // ---- sixty days of the horde on an XL map, quickly; the same seed the same
        {
            // (timed the second time over: the first warms the code up, as a game played a while has it)
            Campaign Sixty(out double sum, out double worst)
            {
                var c = Campaign.NewWorld(17, 144);
                c.AddHero();
                sum = 0; worst = 0;
                for (int d = 0; d < 60; d++)
                {
                    double t0 = BenchClock.CpuMs();
                    c.Advance(DayClock.Cycle);
                    double t = BenchClock.CpuMs() - t0;
                    sum += t; worst = Math.Max(worst, t);
                    c.News.Clear();
                }
                return c;
            }
            var first = Sixty(out double cold, out _);
            var c = Sixty(out double sum, out double worst);
            int out_ = c.Parties.Count(p => p.Owner == Owner.Horde), mines = c.Cells.Count(x => x.Thing is { Kind: ThingKind.Mine, Owner: Owner.Horde });
            check("sixty days of the horde on an XL map: it musters, sends parties along the hexes and flags mines, a day in a few milliseconds; the same seed the same",
                sum / 60 < 8 && c.Realm.Sent > 0 && mines > 0 && Print(first) == Print(c),
                $"{sum / 60:0.0} ms a day on average ({cold / 60:0.0} the first time over), {worst:0} ms the worst; sent {c.Realm.Sent}, out {out_}, mines of the horde {mines}; the same {Print(first) == Print(c)}");

            // written small mid-game, read back the same, and going on the same
            string dir = Path.Combine(Path.GetTempPath(), "vt-world-bench");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            c.Write(dir);
            long size = new FileInfo(Path.Combine(dir, Campaign.FileName)).Length;
            var back = Campaign.Read(dir);
            string p0 = Print(c), p1 = Print(back);
            c.Advance(DayClock.Cycle * 3); back.Advance(DayClock.Cycle * 3);
            string q0 = Print(c), q1 = Print(back);
            check("an XL map written mid-game is small, read back the same, and goes on the same",
                size < 400_000 && p0 == p1 && q0 == q1, $"{size / 1024.0:0} KB; the same {p0 == p1}, after three days {q0 == q1}");
            Directory.Delete(dir, true);
        }
    }
}

static class WorldDebug
{
    public static void Run(uint seed, int side)
    {
        var c = Campaign.NewWorld(seed, side);
        var seen = new HashSet<MapCell> { c.Home };
        var q = new Queue<MapCell>(); q.Enqueue(c.Home);
        while (q.Count > 0) { var a = q.Dequeue(); for (int s = 0; s < 6; s++) if (c.Neighbour(a, s) is { } b && b.Passable && seen.Add(b)) q.Enqueue(b); }
        foreach (var z in c.Zones)
        {
            var mid = c.Cell(z.Col, z.Row);
            var passes = c.Cells.Where(x => x.Zone == z.Id && x.Pass).Select(x => $"{x.Col},{x.Row}{(seen.Contains(x) ? "+" : "-")}");
            Console.WriteLine($"zone {z.Id} {z.Kind} t{z.Tier} links [{string.Join(",", z.Links)}] mid {z.Col},{z.Row} reached {seen.Contains(mid)} passable {mid.Passable} passes {string.Join(" ", passes)}");
        }
    }
}

/// <summary>A big map drawn flat for the eye (--world-dump seed side file.ppm): a hex a block, its ground and relief, its roads and rivers, its things.</summary>
static class WorldDump
{
    public static void Run(uint seed, int side, string file)
    {
        var c = Campaign.NewWorld(seed, side);
        const int S = 10;
        int w = side * S + S / 2, h = side * (S - 2) + 2;
        var px = new byte[w * h * 3];
        void Put(int x, int y, (int r, int g, int b) col)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int i = (y * w + x) * 3; px[i] = (byte)col.r; px[i + 1] = (byte)col.g; px[i + 2] = (byte)col.b;
        }
        (int, int, int) Ground(MapCell x) => x.Ground switch
        {
            HexGround.Grass => (96, 150, 64), HexGround.Dirt => (140, 120, 80), HexGround.Sand => (214, 196, 130), HexGround.Snow => (232, 236, 240),
            HexGround.Swamp => (70, 96, 70), HexGround.Rough => (128, 112, 96), HexGround.Lava => (190, 70, 30), HexGround.Scorched => (60, 44, 40),
            HexGround.Water => (90, 140, 200), _ => (30, 60, 120),
        };
        foreach (var x in c.Cells)
        {
            int ox = x.Col * S + (x.Row & 1) * S / 2, oy = x.Row * (S - 2);
            var g = Ground(x);
            if (x.Relief == HexRelief.Mountain) g = ((int)(g.Item1 * .45 + 70), (int)(g.Item2 * .45 + 66), (int)(g.Item3 * .45 + 70));
            else if (x.Relief == HexRelief.Forest) g = ((int)(g.Item1 * .55), (int)(g.Item2 * .7), (int)(g.Item3 * .5));
            else if (x.Relief == HexRelief.Hills) g = ((int)(g.Item1 * .85 + 20), (int)(g.Item2 * .85 + 14), (int)(g.Item3 * .8));
            for (int yy = 0; yy < S; yy++)
                for (int xx = 0; xx < S; xx++)
                {
                    // a hexagon-ish block: its corners cut
                    int dy = Math.Min(yy, S - 1 - yy);
                    if (Math.Abs(xx - S / 2.0 + .5) > S / 2.0 - Math.Max(0, 2 - dy)) continue;
                    Put(ox + xx, oy + yy, g);
                }
            int cx = ox + S / 2, cy = oy + S / 2;
            for (int s = 0; s < 6; s++)
            {
                double a = Math.PI / 180 * (-60 + 60 * s);
                if ((x.Roads >> s & 1) != 0) for (int k = 0; k <= S / 2; k++) Put(cx + (int)Math.Round(Math.Cos(a) * k), cy + (int)Math.Round(Math.Sin(a) * k), (110, 74, 40));
                if ((x.Rivers >> s & 1) != 0) for (int k = 0; k <= S / 2; k++) Put(cx + (int)Math.Round(Math.Cos(a) * k) + 1, cy + (int)Math.Round(Math.Sin(a) * k), (60, 120, 230));
            }
            if (x.Thing is { } t)
            {
                var col = t.Kind switch
                {
                    ThingKind.Citadel => (40, 90, 255), ThingKind.HordeTown or ThingKind.Lair => (255, 30, 30), ThingKind.Mine => (255, 210, 0),
                    ThingKind.Monster => (230, 0, 230), ThingKind.Trove => (255, 140, 0), ThingKind.Chest or ThingKind.Pile or ThingKind.Campfire => (255, 255, 255),
                    _ => (0, 230, 230),
                };
                int r = t.Kind is ThingKind.Citadel or ThingKind.HordeTown ? 3 : 1;
                for (int yy = -r; yy <= r; yy++) for (int xx = -r; xx <= r; xx++) Put(cx + xx, cy + yy, col);
            }
        }
        using var f = System.IO.File.Create(file);
        var head = System.Text.Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n");
        f.Write(head); f.Write(px);
        Console.WriteLine($"{file}: {w}×{h}, {c.Zones.Count} zones");
    }
}
