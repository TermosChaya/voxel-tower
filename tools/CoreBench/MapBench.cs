using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VoxelTower.Core;

/// <summary>
/// The bench's ground for the map's own life (--map, Campaign.Turn.cs, MapAI.cs, QuickBattle.cs, MapPath.cs,
/// ValleySummary.cs), all of it without a valley open but one: the ways found are the cheapest; thirty days of a
/// campaign in seconds — the lair musters and sends, its parties walk, take cells nobody holds and come home; a party of
/// the horde walking into the valley we play in is a raid by the side it came across, in the valley itself too; quick
/// battles go to the stronger as often as they should; the valleys we hold and are not in pay what the formula says
/// and their workers' metal waits for their stores; the same seed plays the same, and a campaign written halfway and
/// read back goes on the same; the first valley lost, the campaign is lost on the third dawn after unless another
/// valley is held; a day of the map takes a few milliseconds.
/// </summary>
static class MapBench
{
    /// <summary>A campaign whose first valley we have left: a household with soldiers, half a wall, a town paying 5 a minute.</summary>
    static Campaign Away(uint seed, bool weak = false)
    {
        var c = Campaign.New(seed);
        var s = c.Home.Sum;
        var kinds = weak ? new[] { Kinds.Worker, Kinds.Worker }
            : new[] { Kinds.Captain, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Archer, Kinds.Archer,
                Kinds.Archer, Kinds.Archer, Kinds.Swordsman, Kinds.Swordsman, Kinds.Swordsman, Kinds.Swordsman, Kinds.Spearman, Kinds.Spearman };
        foreach (var k in kinds) s.Keep.Add(new Traveller { Kind = k, Hp = k.Hp, MaxHp = k.Hp });
        s.Walls = weak ? 0 : .6f; s.TownGold = 5; s.Known = true;
        return c;
    }

    static void Day(Campaign c, List<string> said = null)
    {
        c.Advance(DayClock.Cycle);
        if (said != null) said.AddRange(c.News.Select(n => n.Text));
        c.News.Clear();
    }

    /// <summary>All of a campaign's state that plays on, as a line: the same line, the same campaign.</summary>
    static string Digest(Campaign c)
    {
        var b = new StringBuilder();
        b.Append($"{c.Clock.Day}/{c.Clock.At:0.###} gold {c.Gold:0.###} away {c.EarnedAway:0.###} fell {c.HomeFell} won {c.Won} tier {c.Tier:0.##};");
        foreach (var x in c.Cells)
            b.Append($" {x.Q},{x.R}:{x.Owner}/{x.Sum.Keep.Count}/{string.Join("", x.Sum.Keep.Select(m => m.Kind.Key[0]))}/{x.Sum.Keep.Sum(m => m.Hp):0.#}/{x.Sum.Raised:0.###}/{x.Sum.Metal:0.###}");
        foreach (var p in c.Parties)
            b.Append($" [{p.Id} {p.Owner} {p.Q},{p.R} {p.Aim} {p.Party.Members.Count} {p.Path?.Count ?? 0} {p.Progress:0.###} {p.Lord?.Name}{p.Lord?.Level}]");
        b.Append($" lords {string.Join(",", c.Realm.Lords.Select(l => l.Name + l.Level))} mustered {c.Realm.Mustered} sent {c.Realm.Sent}");
        return b.ToString();
    }

    /// <summary>The days of a campaign told, for tuning (--map-trace [seed] [days]).</summary>
    public static void Trace(uint seed, int days, bool weak)
    {
        var c = Away(seed, weak);
        Console.WriteLine($"lair at {c.Lair.Q},{c.Lair.R}; cells: {string.Join(" ", c.Cells.Select(x => $"{x.Q},{x.R}={x.Kind}"))}");
        for (int d = 0; d < days; d++)
        {
            var said = new List<string>();
            Day(c, said);
            int horde = c.Parties.Where(p => p.Owner == Owner.Horde).Sum(p => p.Party.Members.Count);
            Console.WriteLine($"day {c.Clock.Day}: tier {c.Tier:0.0}, lair {c.Lair?.Sum.Keep.Count}, abroad {horde} in {c.Parties.Count(p => p.Owner == Owner.Horde)}, horde cells {c.Cells.Count(x => x.Owner == Owner.Horde)}, ours {c.Cells.Count(x => x.Owner == Owner.Ours)}, home keep {c.Home.Sum.Keep.Count}, gold {c.Gold:0}");
            foreach (var s in said) Console.WriteLine("    " + s);
            foreach (var p in c.Parties) Console.WriteLine($"    party {p.Id} {p.Owner} at {p.Q},{p.R} {p.Aim} → {p.GoalQ},{p.GoalR}, {p.Party.Members.Count} men, {p.Lord?.Name}");
        }
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();

        // ---- the ways: the cheapest there is, cell to neighbouring cell, the slow ground slower
        {
            var c = Campaign.New(11);
            int pairs = 0, cheapest = 0, joined = 0;
            foreach (var a in c.Cells)
                foreach (var z in c.Cells)
                {
                    if (a == z) continue;
                    pairs++;
                    var way = MapPath.Find(c, a, z, Owner.Ours);
                    if (way == null || way.Count == 0 || way[^1] != z) continue;
                    bool ok = true;
                    var prev = a;
                    foreach (var x in way) { if (MapPath.Side(prev, x) < 0) ok = false; prev = x; }
                    if (ok) joined++;
                    if (Math.Abs(Cost(c, a, way, z) - Dijkstra(c, a, z)) < 1e-6) cheapest++;
                }
            var marsh = new MapCell { Kind = ValleyKind.Marsh }; var ore = new MapCell { Kind = ValleyKind.Ore };
            check("the map's ways: from every cell to every other a way of neighbouring cells, the cheapest there is (a cell of the other side costing a day more unless it is the goal); the marsh and the mountains slower than open ground",
                joined == pairs && cheapest == pairs && MapPath.Hours(marsh) > MapPath.Hours(ore),
                $"{joined} of {pairs} ways joined, {cheapest} the cheapest; a marsh {MapPath.Hours(marsh)} h, ore valley {MapPath.Hours(ore)} h");
        }

        // ---- thirty days with nobody of ours in the field, over a few seeds
        {
            var lines = new List<string>();
            bool grows = true, walks = true, takes = true, home = true;
            double worstMs = 0, totalMs = 0; int daysTimed = 0;
            // (the code warmed first: the first days run through the compiler)
            var warm = Away(99);
            for (int d = 0; d < 20; d++) Day(warm);
            foreach (uint seed in new uint[] { 11, 23, 37, 52, 68 })
            {
                var c = Away(seed);
                var lair = c.Lair;
                int farthest = 0, horde2 = 0;
                var said = new List<string>();
                for (int d = 0; d < 30; d++)
                {
                    double t0 = BenchClock.CpuMs();
                    Day(c, said);
                    double ms = BenchClock.CpuMs() - t0;
                    worstMs = Math.Max(worstMs, ms); totalMs += ms; daysTimed++;
                    foreach (var p in c.Parties.Where(p => p.Owner == Owner.Horde)) farthest = Math.Max(farthest, Campaign.Distance(p.Q, p.R, lair.Q, lair.R));
                    if (d == 1) horde2 = HordeMen(c);
                }
                int cells = c.Cells.Count(x => x.Owner == Owner.Horde), men = HordeMen(c);
                bool struck = said.Any(s => s.Contains("первую долину"));
                grows &= men > horde2 && c.Realm.Mustered >= 30 * Campaign.MusterBase;
                walks &= farthest >= 2;
                takes &= cells >= 3;
                home &= struck || c.Home.Owner == Owner.Ours;
                lines.Add($"seed {seed}: horde {horde2} men on day 2, {men} on day 31 ({c.Realm.Mustered} mustered, {c.Realm.Sent} parties sent), walked {farthest} cells from the lair, holds {cells} cells, first valley {c.Home.Owner}{(struck ? ", struck at" : "")}");
            }
            check("thirty days of a campaign without ours in the field: the lair musters and grows, its parties walk two cells and more from it, and the horde takes cells nobody holds (three or more with the lair)",
                grows && walks && takes && home, string.Join("; ", lines));
            check("a day of the map takes a few milliseconds", worstMs < 5, $"{totalMs / daysTimed:0.00} ms a day on average, {worstMs:0.00} the worst of {daysTimed}");
        }

        // ---- a party of the horde walks into the valley we play in: a raid by the side it came across
        MapParty raid = null; int raidSide = -1; Campaign rc = null; int raidDay = 0;
        {
            rc = Away(11);
            rc.InValley = true;
            for (int d = 0; d < 40 && raid == null; d++)
            {
                for (int k = 0; k < 150 && raid == null; k++)
                {
                    rc.Advance(5);
                    if (rc.TakeRaid(out var e, out int s)) { raid = e; raidSide = s; raidDay = rc.Clock.Day; }
                }
                rc.News.Clear();
            }
            var came = raid == null ? null : rc.At(raid.CameQ, raid.CameR);
            bool horde = raid != null && raid.Party.Members.Count > 0 && raid.Party.Members.All(m => m.Kind.Team == Team.Horde);
            check("a party of the horde walking into the cell of the valley we play in is a raid: by the side the cell it came from lies across, of the horde's kinds",
                raid != null && came != null && rc.Neighbour(rc.Home, raidSide) == came && horde && !rc.Parties.Contains(raid),
                raid == null ? "no raid in 40 days" : $"day {raidDay}: {raid.Party.Members.Count} raiders{(raid.Lord != null ? $" led by {raid.Lord.Name}" : "")}, from {came?.Q},{came?.R} across side {raidSide}");
            if (raid != null)
            {
                var half = raid.Roster().Take(raid.Party.Members.Count / 2).ToList();
                rc.RaidOver(raid, half, true);
                var back = rc.Parties.Find(p => p.Owner == Owner.Horde && p.Aim == PartyAim.Return && p.Q == raid.CameQ && p.R == raid.CameR);
                check("what is left of a raid goes back the way it came, home to the lair, its warlord with it",
                    back != null && back.Party.Members.Count == half.Count && back.Lord == raid.Lord && back.Path is { Count: > 0 } && back.Path[^1] == rc.Lair,
                    back == null ? "nobody going home" : $"{back.Party.Members.Count} going home by {back.Path.Count} cells");
            }
        }

        // ---- the raid in the valley itself: its bands come in by that side
        string dir = Path.Combine(Path.GetTempPath(), $"vt-map-bench-{Environment.ProcessId}");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Sim valley = null;
        {
            var camp = Away(11);
            valley = camp.OpenValley(dir, camp.Home, out _);
            int side = raid != null ? raidSide : 2;
            var roster = raid?.Roster() ?? Sim.Roster(3, 1);
            var before = valley.Bands.ToList();
            valley.Raid(roster, side, .5, raid?.Lord);
            var come = valley.Bands.Except(before).ToList();
            var edges = come.Select(b => b.Edge).Distinct().ToList();
            check("the raid brought into the valley comes in by that side: every band of it",
                valley.Raids.Active && come.Count > 0 && edges.Count == 1 && edges[0] == side,
                $"{come.Count} bands by sides {string.Join(",", edges)}, the side {side}");
        }

        // ---- what the map keeps of a valley as we leave it, and what it gives back as we come
        {
            var camp = Away(11);
            var home = camp.Home;
            double t0 = BenchClock.CpuMs();
            camp.Summarise(home, valley);
            double ms = BenchClock.CpuMs() - t0;
            int ours = valley.Units.Count(u => !u.Dead && u.Team == Team.Player);
            var kept = home.Sum;
            bool taken = kept.Known && kept.Keep.Count == ours && kept.Walls > 0 && kept.Walls <= 1.5f && home.Owner == Owner.Ours;
            // two of them fall in a battle counted while we are away; we come back
            var gone = kept.Keep.Where(m => m.Kind != Kinds.Captain).Take(2).ToList();
            foreach (var m in gone) kept.Keep.Remove(m);
            home.Visited = true;
            camp.Settle(home, valley);
            int after = valley.Units.Count(u => !u.Dead && u.Team == Team.Player);
            check("the map keeps of a valley left its keepers, its walls by the masonry about its stronghold and its town's pay; come back after a battle counted away, those who fell in it are gone",
                taken && after == ours - 2 && ms < 300,
                $"{kept.Keep.Count + 2} kept of {ours} of ours, walls {kept.Walls:0.00}, town {kept.TownGold:0.#} a minute, counted in {ms:0} ms; 2 fell away: {after} left");
        }

        // ---- quick battles: twice the strength wins 9 in 10, the same half the time
        {
            var ours = new[] { Kinds.Swordsman, Kinds.Swordsman, Kinds.Swordsman, Kinds.Swordsman, Kinds.Swordsman, Kinds.Swordsman, Kinds.Archer, Kinds.Archer };
            float so = QuickBattle.Strength(Side(ours), null);
            int gob = 1;
            while (gob < 200 && QuickBattle.Strength(Side(Enumerable.Repeat(Kinds.Goblin, gob + 1)), null) <= so / 2) gob++;
            var weak = Enumerable.Repeat(Kinds.Goblin, gob).ToArray();
            float sw = QuickBattle.Strength(Side(weak), null);
            int strong = 0, even = 0, n = 1000;
            double strongLost = 0, weakLost = 0;
            for (int i = 0; i < n; i++)
            {
                var rnd = new Mulberry32((uint)(i * 7919 + 13));
                var a = Side(ours); var d = Side(weak);
                var r = QuickBattle.Fight(a, null, d, null, 0, ValleyKind.First, rnd.Next);
                if (r.AttackerWon) { strong++; strongLost += r.AttackerFell.Count / (double)ours.Length; weakLost += r.DefenderFell.Count / (double)weak.Length; }
                var r2 = QuickBattle.Fight(Side(ours), null, Side(ours), null, 0, ValleyKind.First, rnd.Next);
                if (r2.AttackerWon) even++;
            }
            check("a quick battle: twice the strength wins 9 times in 10, the same strength half the time; the strong winning lose few, the beaten most",
                strong is >= 850 and <= 950 && even is >= 440 and <= 560 && strongLost / strong < .3 && weakLost / strong > .5,
                $"{ours.Length} of ours ({so:0}) against {gob} goblins ({sw:0}, {so / sw:0.00} times weaker): ours won {strong} of {n}, losing {strongLost / strong:P0}, the goblins {weakLost / strong:P0}; even sides: the attacker won {even}");
            // walls and the ground
            var keep = Side(ours);
            float open = QuickBattle.Chance(Side(Enumerable.Repeat(Kinds.Orc, 14)), null, keep, null, 0, ValleyKind.First);
            float walled = QuickBattle.Chance(Side(Enumerable.Repeat(Kinds.Orc, 14)), null, keep, null, 1, ValleyKind.First);
            float rammed = QuickBattle.Chance(Side(Enumerable.Repeat(Kinds.Orc, 14).Append(Kinds.Ram).Append(Kinds.Ram)), null, keep, null, 1, ValleyKind.First);
            var lord = new Warlord { Kind = Kinds.Orc, Level = 3 };
            float led = QuickBattle.Chance(Side(Enumerable.Repeat(Kinds.Orc, 14)), lord, keep, null, 0, ValleyKind.First);
            check("walls hold a valley's keepers better, the horde's rams batter them down, a warlord makes his party stronger",
                walled < open * .7f && rammed > walled && led > open,
                $"14 orcs against {ours.Length} of ours: in the open {open:P0}, behind walls {walled:P0}, with two rams {rammed:P0}, led by a warlord of level 3 {led:P0}");
        }

        // ---- the valleys we hold and are not in pay what the formula says
        {
            var c = Away(5);
            var den = c.Lair; den.Owner = Owner.None; den.Sum.Keep.Clear();
            c.Home.Sum.TownGold = 6;
            var ore = c.Cells.First(x => x.Kind == ValleyKind.Ore && Campaign.Distance(0, 0, x.Q, x.R) == 1);
            ore.Owner = Owner.Ours;
            for (int i = 0; i < 4; i++) ore.Sum.Keep.Add(new Traveller { Kind = Kinds.Worker, Hp = Kinds.Worker.Hp, MaxHp = Kinds.Worker.Hp });
            double gold0 = c.Gold;
            Day(c); Day(c);
            double want = (6 + ValleySummary.MineGold(ValleyKind.Ore) * ValleySummary.MineShare) / 60 * 2 * DayClock.Cycle;
            double metal = ValleySummary.Yield(ValleyKind.Ore).metal * 4 * 2;
            double stock0 = valley.Stock[Stockpile.Metal];
            c.Settle(ore, valley);
            double into = (valley.Stock[Stockpile.Metal] - stock0) / VoxelWorld.PerM3;
            check("the valleys we hold and are not in pay into the realm's gold a minute what their town and mines pay (the mines six tenths), and their workers' metal waits for their stores till we come",
                Math.Abs(c.EarnedAway - want) < .01 && Math.Abs(c.Gold - gold0 - want) < .01 && Math.Abs(into - metal) < 1e-6 && ore.Sum.Metal == 0,
                $"2 days: {c.EarnedAway:0.00} gold of {want:0.00} by the formula; {into:0.00} m³ of metal into the ore valley's stores of {metal:0.00}");
        }

        // ---- the same seed plays the same; written halfway and read back, the same goes on
        {
            var a = Away(21); var b = Away(21);
            var sa = new List<string>(); var sb = new List<string>();
            for (int d = 0; d < 30; d++) { Day(a, sa); Day(b, sb); }
            check("the same seed plays the same thirty days", Digest(a) == Digest(b) && sa.SequenceEqual(sb), $"{sa.Count} lines said; {Digest(a).Length} characters of state alike");

            var x = Away(21);
            for (int d = 0; d < 15; d++) Day(x);
            x.Write(dir);
            var y = Campaign.Read(dir);
            bool same = Digest(x) == Digest(y);
            var sx = new List<string>(); var sy = new List<string>();
            for (int d = 0; d < 15; d++) { Day(x, sx); Day(y, sy); }
            check("a campaign written halfway and read back goes on the same",
                same && Digest(x) == Digest(y) && sx.SequenceEqual(sy) && Digest(x) == Digest(a),
                $"read back the same {same}; after 15 days more the same {Digest(x) == Digest(y)}, as played straight through {Digest(x) == Digest(a)}");
        }

        // ---- the first valley lost: the campaign is lost on the third dawn after, unless another valley is held
        {
            var c = Away(31);
            var den = c.Lair; den.Owner = Owner.None; den.Sum.Keep.Clear();
            while (c.Clock.Day < 5) Day(c);
            c.Fall(c.Home);
            int fell = c.HomeFell;
            while (c.Clock.Day < 7) Day(c);
            c.Advance(DayClock.Cycle - c.Clock.At - 1);
            bool before = c.Defeated;
            c.Advance(2);
            bool after = c.Defeated, said = c.News.Any(n => n.Text.StartsWith("Кампания проиграна"));

            var k = Away(31);
            var den2 = k.Lair; den2.Owner = Owner.None; den2.Sum.Keep.Clear();
            // a party of ours with workers raises an outpost in a cell nobody holds beside the first valley
            var spot = k.Cells.First(x => Campaign.Distance(0, 0, x.Q, x.R) == 1);
            var party = new MapParty { Id = 900, Q = 0, R = 0, CameQ = 0, CameR = 0, Party = new Party() };
            foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Swordsman, Kinds.Swordsman }) party.Party.Members.Add(new Traveller { Kind = t, Hp = t.Hp, MaxHp = t.Hp });
            k.Parties.Add(party);
            bool sent = k.Send(party, spot);
            while (k.Clock.Day < 5) Day(k);
            bool raised = spot.Owner == Owner.Ours && spot.Sum.Workers == 3 && party.Party.Members.Count == 2;
            k.Fall(k.Home);
            while (k.Clock.Day < 9) Day(k);
            check("the first valley lost, the campaign is lost on the third dawn after it and said so; with an outpost raised elsewhere, it is not",
                fell == 5 && !before && after && said && sent && raised && !k.Defeated,
                $"fell on day {fell}; lost on the night before day 8 {before}, at its dawn {after}, said {said}; a party of 3 workers and 2 swordsmen raised an outpost {(raised ? "and left its workers there" : "— no")}; with it, lost on day 9 {k.Defeated}");
        }

        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    static int HordeMen(Campaign c) =>
        c.Cells.Where(x => x.Owner == Owner.Horde).Sum(x => x.Sum.Keep.Count) + c.Parties.Where(p => p.Owner == Owner.Horde).Sum(p => p.Party.Members.Count);

    static List<Traveller> Side(IEnumerable<UnitType> kinds) => kinds.Select(k => new Traveller { Kind = k, Hp = k.Hp, MaxHp = k.Hp }).ToList();

    static double Cost(Campaign c, MapCell from, List<MapCell> way, MapCell to)
    {
        double h = 0; var a = from;
        foreach (var b in way) { h += MapPath.StepHours(a, b, MapPath.Side(a, b)) + (b != to && b.Owner == Owner.Horde ? 24 : 0); a = b; }
        return h;
    }

    static double Dijkstra(Campaign c, MapCell from, MapCell to)
    {
        var dist = c.Cells.ToDictionary(x => x, x => double.MaxValue);
        dist[from] = 0;
        var left = new HashSet<MapCell>(c.Cells);
        while (left.Count > 0)
        {
            var a = left.OrderBy(x => dist[x]).First();
            left.Remove(a);
            for (int s = 0; s < 6; s++)
            {
                var b = c.Neighbour(a, s);
                if (b == null || !left.Contains(b)) continue;
                double g = dist[a] + MapPath.StepHours(a, b, s) + (b != to && b.Owner == Owner.Horde ? 24 : 0);
                if (g < dist[b]) dist[b] = g;
            }
        }
        return dist[to];
    }
}
