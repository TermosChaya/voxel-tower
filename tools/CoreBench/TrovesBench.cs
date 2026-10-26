using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's ground for the map's content (--troves, Campaign.Troves.cs, Troves.cs, MapAI.cs): the big map of 37 cells
/// with its two or three lairs apart on the outer ring and its treasures in the cells nobody holds, each kind among them;
/// their keepers, more the further out; the horde leaving them be for thirty days while every lair musters and sends;
/// a quick battle against a treasure's keepers paying its reward once, a shrine's trial fought at once blessing the realm;
/// scouts walking faster and seeing two cells round, a town hall of the second level seeing two; each treasure laid in its
/// valley, its chest reached by a way from the middle; in the valley a chest taken once its keepers are down, a shrine's
/// trial held to the end or lost; fierce keepers going for ours and ours turning on them; the map's file keeping it all.
/// </summary>
static class TrovesBench
{
    static void Step(Sim s, double seconds) { for (double t = 0; t < seconds; t += .05) s.Step(.05f); }

    static Traveller Man(UnitType k) => new() { Kind = k, Hp = k.Hp, MaxHp = k.Hp };

    /// <summary>A party of ours standing in a cell: a commander and so many swordsmen and archers.</summary>
    static MapParty Party(Campaign c, MapCell at, int swords, int bows)
    {
        var p = new MapParty { Owner = Owner.Ours, Q = at.Q, R = at.R, CameQ = at.Q, CameR = at.R, Party = new Party() };
        p.Party.Members.Add(Man(Kinds.Captain));
        for (int i = 0; i < swords; i++) p.Party.Members.Add(Man(Kinds.Swordsman));
        for (int i = 0; i < bows; i++) p.Party.Members.Add(Man(Kinds.Archer));
        c.Parties.Add(p);
        return p;
    }

    /// <summary>The keepers of a valley's treasure set at their spots, as the game sets them when we first come.</summary>
    static List<Unit> Keep(Sim sim, MapCell cell)
    {
        var list = new List<Unit>();
        int k = 0;
        foreach (var m in cell.Sum.Keep)
        {
            var (px, py, pz) = sim.Site.TrovePosts[k++ % sim.Site.TrovePosts.Count];
            int at = py >= 0 ? sim.StandNear(px, py, pz, m.Kind.Move.Height) : sim.NearestStand(px, pz, m.Kind.Move.Height);
            if (at < 0) continue;
            var u = sim.Spawn(m.Kind, at);
            u.Guard = true; u.Home = at;
            list.Add(u);
        }
        return list;
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        string dir = Path.Combine(Path.GetTempPath(), $"vt-troves-bench-{Environment.ProcessId}");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);

        // ---- the big map: 37 cells, its lairs apart on the outer ring, its treasures
        int seeds = 0, good = 0, lairs2 = 0, lairs3 = 0, kindsAll = 0, troveMin = int.MaxValue, troveMax = 0, apartLeast = int.MaxValue;
        string bad = "";
        for (uint seed = 1; seed <= 24; seed++)
        {
            var c = Campaign.New(seed, 3);
            seeds++;
            var dens = c.Lairs;
            var troves = c.Cells.Where(x => x.Trove != TroveKind.None).ToList();
            int apart = int.MaxValue;
            for (int i = 0; i < dens.Count; i++)
                for (int j = i + 1; j < dens.Count; j++) apart = Math.Min(apart, Campaign.Distance(dens[i].Q, dens[i].R, dens[j].Q, dens[j].R));
            bool ok = c.Cells.Count == 37 && dens.Count is 2 or 3 && dens.All(d => Campaign.Distance(0, 0, d.Q, d.R) == 3) && apart >= 3
                && troves.All(t => t.Owner == Owner.None && !t.Home && Campaign.Distance(0, 0, t.Q, t.R) >= 2)
                && troves.Where(t => t.Trove == TroveKind.Shrine).All(t => t.Blessing != Blessing.None)
                && troves.Count is >= 8 and <= 10;
            bool all = new[] { TroveKind.DeadMine, TroveKind.Ruins, TroveKind.Den, TroveKind.Shrine }.All(k => troves.Exists(t => t.Trove == k));
            if (all) kindsAll++;
            if (ok && all) good++; else if (bad.Length == 0) bad = $" (seed {seed}: {dens.Count} lairs {apart} apart, {troves.Count} treasures, all kinds {all})";
            if (dens.Count == 2) lairs2++; else if (dens.Count == 3) lairs3++;
            troveMin = Math.Min(troveMin, troves.Count); troveMax = Math.Max(troveMax, troves.Count);
            apartLeast = Math.Min(apartLeast, apart);
        }
        var small = Campaign.New(11);
        check("the big map: 37 cells, two or three lairs of the horde on the outer ring three cells apart and more, treasures in a third of the cells nobody holds past the first ring, each kind among them, every shrine with its blessing; the small map 19 cells with one lair",
            good == seeds && lairs2 > 0 && lairs3 > 0 && small.Cells.Count == 19 && small.Lairs.Count == 1 && small.Cells.Count(x => x.Trove != TroveKind.None) is >= 2 and <= 4,
            $"{good} of {seeds} seeds right{bad}; lairs 2 on {lairs2}, 3 on {lairs3}, {apartLeast} apart at the least; treasures {troveMin}…{troveMax}, all kinds on {kindsAll}; the small map {small.Cells.Count} cells, {small.Lairs.Count} lair, {small.Cells.Count(x => x.Trove != TroveKind.None)} treasures");

        // ---- the keepers, more the further out
        var big = Campaign.New(7, 3);
        big.Advance(0);
        string keeps = "";
        bool kept = true;
        foreach (var t in big.Cells.Where(x => x.Trove != TroveKind.None))
        {
            int ring = Campaign.Distance(0, 0, t.Q, t.R);
            var want = Campaign.KeepersOf(t.Trove, ring);
            var have = t.Sum.Keep.Select(m => m.Kind).ToList();
            if (have.Count != want.Count || want.Except(have).Any()) kept = false;
            keeps += $" {t.Trove}@{ring}:{have.Count}";
        }
        var mine2 = Campaign.KeepersOf(TroveKind.DeadMine, 2).Count; var mine3 = Campaign.KeepersOf(TroveKind.DeadMine, 3).Count;
        check("the keepers of the treasures: the dead of an abandoned mine, the wolves and a dead or two of the ruins, the bears and wolves of a den, the dead that come for a shrine; more the further out; the cells with no treasure past the first ring keep their beasts",
            kept && mine3 > mine2 && Campaign.KeepersOf(TroveKind.Den, 2).Contains(Kinds.Bear) && Campaign.KeepersOf(TroveKind.Ruins, 2).Contains(Kinds.Wolf)
            && big.Cells.Where(x => x.Owner == Owner.None && x.Trove == TroveKind.None && Campaign.Distance(0, 0, x.Q, x.R) >= 2).All(x => x.Sum.Keep.Count == 4),
            $"{keeps.Trim()}; a mine's dead 2nd ring {mine2}, 3rd {mine3}");

        // ---- thirty days: every lair musters and sends; the horde leaves the treasures be
        double spent = 0;
        int days = 0, untouched = 0, troveCells = 0, sendingLairs = 0, lairsAll = 0;
        double worst = 0;
        foreach (uint seed in new uint[] { 3, 5, 9 })
        {
            var c = Campaign.New(seed, 3);
            var hs = c.Home.Sum;
            foreach (var k in new[] { Kinds.Captain, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Archer, Kinds.Archer, Kinds.Archer, Kinds.Swordsman, Kinds.Swordsman, Kinds.Swordsman })
                hs.Keep.Add(Man(k));
            hs.Walls = .6f; hs.TownGold = 5; hs.Known = true;
            var keys = c.Lairs.Select(Campaign.KeyOf).ToList();
            lairsAll += keys.Count;
            var sentFrom = new HashSet<int>();
            var keepAt = c.Cells.Where(x => x.Trove != TroveKind.None).ToDictionary(x => x, x => -1);
            for (int d = 0; d < 30; d++)
            {
                var t0 = Stopwatch.GetTimestamp();
                c.Advance(DayClock.Cycle);
                double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                spent += ms; worst = Math.Max(worst, ms);
                c.News.Clear();
                days++;
                if (d == 0) foreach (var x in keepAt.Keys.ToList()) keepAt[x] = x.Sum.Keep.Count;
                foreach (var p in c.Parties) if (p.Owner == Owner.Horde && p.Den >= 0) sentFrom.Add(p.Den);
            }
            sendingLairs += keys.Count(k => sentFrom.Contains(k));
            foreach (var (x, n) in keepAt)
            {
                troveCells++;
                if (x.Owner == Owner.None && !x.TroveTaken && x.Sum.Keep.Count == n) untouched++;
            }
        }
        double perDay = spent / Math.Max(1, days);
        check("thirty days of the big map: every lair musters and sends its parties out; the horde takes no cell with a treasure, loots none and does not fight their keepers; a day of the map takes a few milliseconds",
            sendingLairs == lairsAll && untouched == troveCells && perDay < 5 && worst < 40,
            $"lairs sending {sendingLairs} of {lairsAll}; treasures left be {untouched} of {troveCells}; {perDay:0.00} ms a day on average, {worst:0.0} the worst");

        // ---- a quick battle against a treasure's keepers: the reward once
        var qc = Campaign.New(7, 3);
        qc.Advance(0);
        var mine = qc.Cells.First(x => x.Trove == TroveKind.DeadMine);
        int ringM = Campaign.Distance(0, 0, mine.Q, mine.R);
        var (gold, metal) = Campaign.RewardOf(TroveKind.DeadMine, ringM);
        double g0 = qc.Gold, m0 = qc.Home.Sum.Metal;
        var strong = Party(qc, mine, 8, 6);
        float odds = QuickBattle.Chance(strong.Party.Members, null, mine.Sum.Holders(), null, 0, mine.Kind);
        var r1 = qc.FightHere(strong);
        double g1 = qc.Gold, m1 = qc.Home.Sum.Metal;
        var r2 = qc.FightHere(strong);
        int wins = 0;
        for (int i = 0; i < 200; i++)
        {
            var keepers = Campaign.KeepersOf(TroveKind.DeadMine, ringM).Select(Man).ToList();
            var ours = new List<Traveller> { Man(Kinds.Captain) };
            for (int k = 0; k < 8; k++) ours.Add(Man(Kinds.Swordsman));
            for (int k = 0; k < 6; k++) ours.Add(Man(Kinds.Archer));
            if (QuickBattle.Fight(ours, null, keepers, null, 0, mine.Kind, qc.Roll).AttackerWon) wins++;
        }
        check("a quick battle against an abandoned mine's dead: a party twice as strong and more takes it nearly always; won, its chest pays once — gold to the realm, metal to the first valley — and a second fight finds nobody",
            odds > .85f && wins >= 170 && r1 is { AttackerWon: true } && mine.TroveTaken && Math.Abs(g1 - g0 - gold) < .01 && Math.Abs(m1 - m0 - metal) < .01 && r2 == null && qc.Gold == g1,
            $"odds {odds:0.00}, won {wins} of 200; first {(r1 == null ? "none" : r1.AttackerWon ? "won" : "lost")}, taken {mine.TroveTaken}, +{g1 - g0:0} gold of {gold}, +{m1 - m0:0.#} m³ metal of {metal}; second {(r2 == null ? "nobody" : "a fight")}");

        // ---- a shrine's trial fought at once: the blessing; plenty pays a minute
        var shrine = qc.Cells.FirstOrDefault(x => x.Trove == TroveKind.Shrine);
        bool blessed = false; double plenty = 0; string what = "no shrine";
        if (shrine != null)
        {
            shrine.Blessing = Blessing.Plenty;
            var party = Party(qc, shrine, 12, 8);
            qc.FightHere(party);
            blessed = qc.Blessed(Blessing.Plenty) && shrine.TroveTaken;
            double before = qc.Gold;
            qc.Parties.Clear();
            qc.Advance(60);
            plenty = qc.Gold - before;
            what = $"{shrine.Q},{shrine.R}";
        }
        check("a shrine's trial fought at once by a strong party blesses the realm for good: with plenty the realm gets 4 gold a minute more",
            blessed && plenty >= Campaign.PlentyGold - .01,
            $"shrine {what}: blessed {blessed}, a minute after +{plenty:0.00} gold");

        // ---- scouts and the sight of a town hall
        var sc = Campaign.New(5, 3);
        var home = sc.Home;
        var scouts = new MapParty { Owner = Owner.Ours, Q = home.Q, R = home.R, CameQ = 0, CameR = 0, Party = new Party() };
        scouts.Party.Members.Add(Man(Kinds.Archer)); scouts.Party.Members.Add(Man(Kinds.Archer));
        var foot = new MapParty { Owner = Owner.Ours, Q = home.Q, R = home.R, CameQ = 0, CameR = 0, Party = new Party() };
        foot.Party.Members.Add(Man(Kinds.Captain)); foot.Party.Members.Add(Man(Kinds.Archer));
        var east = sc.At(2, 0);
        bool far0 = sc.Seen(sc.At(-2, 0)); // nothing of ours two cells west yet
        // a party standing two cells west: the cells two from it and past the first ring are seen by its sight alone
        bool TwoRound() => sc.Cells.Any(x => Campaign.Distance(x.Q, x.R, -2, 0) == 2 && Campaign.Distance(0, 0, x.Q, x.R) > 1 && sc.Seen(x));
        scouts.Q = -2; scouts.R = 0; sc.Parties.Add(scouts);
        bool seen4 = TwoRound();
        sc.Parties.Remove(scouts);
        foot.Q = -2; sc.Parties.Add(foot);
        bool footFar = TwoRound();
        sc.Parties.Remove(foot);
        bool hall1 = sc.Seen(east);
        home.Sum.Hall = 2;
        bool hall2 = sc.Seen(east);
        double paceScout = MapPath.Pace(scouts), paceFoot = MapPath.Pace(foot);
        var three = new MapParty { Owner = Owner.Ours, Party = new Party() };
        foreach (var k in new[] { Kinds.Archer, Kinds.Archer, Kinds.Archer, Kinds.Archer }) three.Party.Members.Add(Man(k));
        check("scouts: up to three light men without a commander walk the map faster and see two cells round them; a party with its commander one; ours with a town hall of the second level see two cells round",
            scouts.Scouting && !foot.Scouting && !three.Scouting && paceScout < paceFoot && seen4 && !footFar && !far0 && !hall1 && hall2,
            $"scouts {scouts.Scouting}, four light {three.Scouting}; pace {paceScout} against {paceFoot}; scouts see two round {seen4}, the party on foot {footFar}; hall 1 sees two east {hall1}, hall 2 {hall2}");

        // ---- each treasure laid in its valley, its chest reached from the middle
        var lay = Campaign.New(7, 3);
        lay.Advance(0);
        string laid = "";
        bool allLaid = true;
        var sims = new Dictionary<TroveKind, (Sim sim, MapCell cell)>();
        foreach (var kind in new[] { TroveKind.DeadMine, TroveKind.Ruins, TroveKind.Den, TroveKind.Shrine })
        {
            var cell = lay.Cells.First(x => x.Trove == kind);
            lay.HereQ = cell.Q; lay.HereR = cell.R;
            var sw2 = Stopwatch.StartNew();
            var sim = lay.OpenValley(dir, cell, out _);
            var s = sim.Site;
            int chest = 0;
            for (int y = s.TroveY; y < s.TroveY + 5; y++)
                for (int z = s.TroveZ - 4; z <= s.TroveZ + 4; z++)
                    for (int x = s.TroveX - 4; x <= s.TroveX + 4; x++)
                        if (sim.World.Get(x, y, z) is Mat.Crate or Mat.SoulFire) chest++;
            float off = MathF.Sqrt((s.TroveX - s.KeepX) * (s.TroveX - s.KeepX) + (s.TroveZ - s.KeepZ) * (s.TroveZ - s.KeepZ));
            int to = sim.StandNear(s.TroveX, s.TroveY, s.TroveZ, 2), from = sim.NearestStand(s.KeepX + 40, s.KeepZ, 2);
            var way = to >= 0 && from >= 0 ? sim.Paths.Find(from, c => c == to, _ => 0, Kinds.Worker.Move, 400000) : null;
            float near = to < 0 ? 999 : MathF.Sqrt(MathF.Pow(sim.Nav.Feet(to).x - s.TroveX - .5f, 2) + MathF.Pow(sim.Nav.Feet(to).z - s.TroveZ - .5f, 2) + MathF.Pow(sim.Nav.Feet(to).y - s.TroveY, 2));
            bool ok = s.Trove == kind && chest > (kind == TroveKind.Shrine ? 2 : 40) && s.TrovePosts.Count >= 6 && off > 120 && way != null && near < 4 * VoxelWorld.PerMetre
                && s.Landmarks.Exists(l => l.Kind == LandmarkKind.Trove);
            if (!ok) allLaid = false;
            laid += $" {kind}: chest {chest}, {s.TrovePosts.Count} posts, {off / VoxelWorld.PerMetre:0} m off the middle, way {(way == null ? "none" : way.Count + " cells")}, nearest stand {near / VoxelWorld.PerMetre:0.0} m, made in {sw2.ElapsedMilliseconds} ms;";
            sims[kind] = (sim, cell);
        }
        check("each treasure laid in its valley away from the middle — the abandoned mine's knoll, adit and chamber, the ruins' hall, the den's cave, the shrine's ring and altar — its chest (or altar) there and reached on foot from the middle, its keepers' spots about it, named among the valley's places",
            allLaid, laid.Trim());

        // ---- in the valley: the chest taken once its keepers are down
        {
            var (sim, cell) = sims[TroveKind.DeadMine];
            lay.HereQ = cell.Q; lay.HereR = cell.R;
            lay.Enter(sim, cell);
            var keepers = Keep(sim, cell);
            var s = sim.Site;
            var taker = sim.Spawn(Kinds.Swordsman, sim.StandNear(s.TroveX, s.TroveY, s.TroveZ, 2));
            taker.Hold = true;
            bool before = lay.TroveTick(sim, cell, .1);
            foreach (var u in keepers) if (!u.Dead) sim.Hurt(u, null, 1e6f);
            double gold0 = sim.Stock.Gold, metal0 = lay.Home.Sum.Metal;
            bool took = lay.TroveTick(sim, cell, .1);
            int left = 0;
            for (int y = s.TroveY; y < s.TroveY + 5; y++)
                for (int z = s.TroveZ - 4; z <= s.TroveZ + 4; z++)
                    for (int x = s.TroveX - 4; x <= s.TroveX + 4; x++)
                        if (sim.World.Get(x, y, z) == Mat.Crate) left++;
            double paid = sim.Stock.Gold - gold0;
            bool again = lay.TroveTick(sim, cell, .1);
            var (rg, rm) = Campaign.RewardOf(TroveKind.DeadMine, Campaign.Distance(0, 0, cell.Q, cell.R));
            check("in the abandoned mine: with its dead standing the chest is not taken; its dead down, the first of ours at the chest takes it, its boards carried off, the gold into the valley's stores and the metal to the first valley's, once",
                !before && took && left == 0 && Math.Abs(paid - rg) < .01 && Math.Abs(lay.Home.Sum.Metal - metal0 - rm) < .01 && !again && cell.TroveTaken,
                $"{keepers.Count} keepers; taken while they stood {before}; after {took}, crates left {left}, paid {paid:0} of {rg}, metal +{lay.Home.Sum.Metal - metal0:0.#} of {rm}; again {again}");
        }

        // ---- fierce keepers go for ours who come near, and ours turn on them
        {
            var (sim, cell) = sims[TroveKind.Ruins];
            var keepers = Keep(sim, cell);
            var wolf = keepers.First(u => u.T == Kinds.Wolf);
            var (wx, _, wz) = sim.Nav.Feet(wolf.Home);
            var man = sim.Spawn(Kinds.Swordsman, sim.NearestStand((int)wx + 70, (int)wz, 2));
            man.Hold = false;
            Step(sim, 6);
            bool wolfOn = keepers.Any(u => u.Target == man || u.Dead) , manOn = man.Prey != null || man.Dead || keepers.Any(u => u.Dead || u.Hp < u.MaxHp);
            check("the wolves of the ruins go for one of ours who comes near, and he turns on them",
                wolfOn && manOn,
                $"a wolf set on him {keepers.Any(u => u.Target == man)}, keepers hurt or down {keepers.Count(u => u.Dead || u.Hp < u.MaxHp)}; he hunts {man.Prey?.T.Name ?? "nobody"}, dead {man.Dead}");
        }

        // ---- a shrine's trial in the valley
        {
            var (sim, cell) = sims[TroveKind.Shrine];
            lay.HereQ = cell.Q; lay.HereR = cell.R;
            lay.Enter(sim, cell);
            var s = sim.Site;
            cell.Blessing = Blessing.Hardy;
            int roster = cell.Sum.Keep.Count;
            var holders = new List<Unit>();
            for (int i = 0; i < 4; i++)
            {
                var u = sim.Spawn(Kinds.Swordsman, sim.NearestStand(s.TroveX + 20 + i * 4, s.TroveZ, 2));
                u.Hold = true; holders.Add(u);
            }
            // begun, then left: lost after twenty seconds, its dead crumbling
            lay.TroveTick(sim, cell, .1);
            bool begun = cell.TrialOn;
            lay.TroveTick(sim, cell, 1);
            int came1 = sim.Units.Count(Campaign.Spirit);
            foreach (var u in holders) { var far = sim.NearestStand(s.TroveX + 300, s.TroveZ, 2); (u.X, u.Y, u.Z) = sim.Nav.Feet(far); u.Cell = far; }
            for (int i = 0; i < 22; i++) lay.TroveTick(sim, cell, 1);
            bool lost = !cell.TrialOn && sim.Units.Count(Campaign.Spirit) == 0;
            // held to the end: the dead of each wave struck down as they come
            foreach (var u in holders) { var at = sim.NearestStand(s.TroveX + 20, s.TroveZ, 2); (u.X, u.Y, u.Z) = sim.Nav.Feet(at); u.Cell = at; }
            int waves = 0, came = 0;
            float hp0 = holders[0].MaxHp;
            for (double t = 0; t < Campaign.TrialLength + 20 && !cell.TroveTaken; t += .5)
            {
                lay.TroveTick(sim, cell, .5);
                foreach (var u in sim.Units.ToArray()) if (Campaign.Spirit(u)) { came++; sim.Hurt(u, null, 1e6f); }
                waves = cell.TrialWave;
            }
            float hp1 = holders[0].MaxHp;
            var later = sim.Spawn(Kinds.Archer, sim.NearestStand(s.TroveX + 30, s.TroveZ, 2));
            check("a shrine's trial in its valley: begun when one of ours stands at the altar, the dead come in three waves; left for twenty seconds it is lost and its dead crumble; held to the end with the dead down it blesses the realm — hardiness: ours in the valley and those hired after a sixth hardier",
                begun && came1 > 0 && lost && waves == Campaign.TrialWaves && came >= roster && cell.TroveTaken && lay.Blessed(Blessing.Hardy)
                && hp1 > hp0 * 1.1f && later.MaxHp > Kinds.Archer.Hp * 1.1f,
                $"begun {begun}, first wave {came1}; lost when left {lost}; then {waves} waves, {came} dead of {roster} struck down; taken {cell.TroveTaken}, hardy {lay.Blessed(Blessing.Hardy)}; a swordsman {hp0:0} → {hp1:0}, an archer hired after {later.MaxHp:0}");
        }

        // ---- the map's file keeps it all
        {
            var c = Campaign.New(9, 3);
            c.Advance(DayClock.Cycle * 3);
            c.News.Clear();
            var t = c.Cells.First(x => x.Trove == TroveKind.Ruins);
            c.Parties.Add(new MapParty { Owner = Owner.Ours, Q = t.Q, R = t.R, CameQ = t.Q, CameR = t.R, Party = new Party() });
            c.Parties[^1].Party.Members.AddRange(Enumerable.Range(0, 14).Select(_ => Man(Kinds.Knight)));
            c.FightHere(c.Parties[^1]);
            c.Blessings.Add(Blessing.Mending);
            string d1 = Path.Combine(dir, "camp");
            c.Write(d1);
            var back = Campaign.Read(d1);
            bool same = back.Rings == 3 && back.Cells.Count == 37 && back.Blessings.SequenceEqual(c.Blessings)
                && back.Cells.Zip(c.Cells).All(p => p.First.Trove == p.Second.Trove && p.First.TroveTaken == p.Second.TroveTaken && p.First.Blessing == p.Second.Blessing)
                && back.Parties.Where(p => p.Owner == Owner.Horde).Select(p => p.Den).SequenceEqual(c.Parties.Where(p => p.Owner == Owner.Horde).Select(p => p.Den));
            int horde = c.Parties.Count(p => p.Owner == Owner.Horde);
            for (int d = 0; d < 5; d++) { c.Advance(DayClock.Cycle); back.Advance(DayClock.Cycle); }
            bool on = c.Gold == back.Gold && c.Parties.Count == back.Parties.Count && c.Cells.Zip(back.Cells).All(p => p.First.Owner == p.Second.Owner && p.First.Sum.Keep.Count == p.Second.Sum.Keep.Count);
            check("the map's file keeps the big map: its rings, the treasures taken or not, the shrines' blessings and the realm's, the lair each party of the horde came out of; read back it goes on the same",
                same && on && t.TroveTaken,
                $"read back the same {same} ({horde} parties of the horde), five days on the same {on}; the ruins taken {t.TroveTaken}");
        }
        try { Directory.Delete(dir, true); } catch { }
    }
}
