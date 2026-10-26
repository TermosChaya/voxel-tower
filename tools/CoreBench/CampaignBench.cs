using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's ground for the map of valleys (--campaign, Campaign.cs, Sim.Campaign.cs, Holdings.cs): a new campaign's
/// cells and their shared sides; the one clock set on a valley from outside and read back; the citadel's fall the end of
/// a siege but only of the valley in a campaign; a party coming in over each of the six sides and standing inside; ours
/// going out over a side, taken off and handed to the map as they were; the outpost's place made level and holding, a raid
/// making for its stone and taking the valley; the horde's town with its garrison keeping at home; a valley written to the
/// campaign's folder and read back the same, with the map's file beside it.
/// </summary>
static class CampaignBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        void Step(Sim s, double seconds) { for (double t = 0; t < seconds; t += .05) s.Step(.05f); }
        Sim Fresh()
        {
            var mp = new MapParams();
            var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
            var site = MapGen.Generate(w, mp);
            w.Compact();
            return new Sim(w, site) { WildTarget = 0 };
        }
        // a party as it would leave a valley: a commander, two swordsmen, two archers, a worker hurt to half
        Party Band()
        {
            var p = new Party();
            int look = 1;
            foreach (var k in new[] { Kinds.Captain, Kinds.Swordsman, Kinds.Swordsman, Kinds.Archer, Kinds.Archer, Kinds.Worker })
                p.Members.Add(new Traveller { Kind = k, Look = look += 7, Hp = k == Kinds.Worker ? k.Hp * .5f : k.Hp, MaxHp = k.Hp });
            return p;
        }
        string dir = Path.Combine(Path.GetTempPath(), $"vt-campaign-bench-{Environment.ProcessId}");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);

        // ---- a new campaign's map
        var camp = Campaign.New(11);
        var home = camp.Home;
        var lair = camp.Cells.Single(c => c.Holding == Holding.HordeTown);
        int pairs = 0, shared = 0;
        foreach (var c in camp.Cells)
            for (int s = 0; s < 6; s++)
                if (camp.Neighbour(c, s) is { } n) { pairs++; if (c.Edges[s].Seed == n.Edges[(s + 3) % 6].Seed) shared++; }
        var near = camp.Cells.Where(c => Campaign.Distance(0, 0, c.Q, c.R) == 1).Select(c => c.Kind).ToList();
        var toward = camp.Neighbour(home, home.EntrySide);
        check("a new campaign: 19 cells, the first valley in the middle ours with its citadel, an ore valley and a holy one beside it, the horde's lair on the outer ring; neighbours share their sides; the first valley's enemy comes by its side toward the lair",
            camp.Cells.Count == 19 && home is { Q: 0, R: 0, Kind: ValleyKind.First, Holding: Holding.Citadel, Owner: Owner.Ours } && near.Contains(ValleyKind.Ore) && near.Contains(ValleyKind.Sacred)
            && Campaign.Distance(0, 0, lair.Q, lair.R) == 2 && lair.Owner == Owner.Horde && pairs == 84 && shared == pairs
            && toward != null && Campaign.Distance(toward.Q, toward.R, lair.Q, lair.R) < Campaign.Distance(0, 0, lair.Q, lair.R),
            $"{camp.Cells.Count} cells; home {home.Kind} {home.Holding} {home.Owner}; beside it {string.Join(", ", near)}; lair at {lair.Q},{lair.R}; {shared} of {pairs} sides shared; the enemy by side {home.EntrySide}");

        // ---- the one clock: set from outside, read back, and on from there
        var clk = Fresh();
        clk.SetSource(new MapRaids());
        clk.SetClock(new ClockTime(5, 200));
        var a = clk.Clock.Now;
        clk.SetClock(new ClockTime(6, 680));
        var b = clk.Clock.Now;
        bool night = clk.Clock.Night;
        Step(clk, 10);
        var c10 = clk.Clock.Now;
        Step(clk, 70);
        var dawn = clk.Clock.Now;
        check("the valley runs by the map's clock: set from outside it reads back the same, by day and by night, and goes on from there through the night to the next dawn",
            a == new ClockTime(5, 200) && b == new ClockTime(6, 680) && night && c10.Day == 6 && Math.Abs(c10.At - 690) < .2 && dawn.Day == 7 && Math.Abs(dawn.At - 10) < .3 && !clk.Raids.Active,
            $"set 5/200 → {a.Day}/{a.At:0.0}; set 6/680 → {b.Day}/{b.At:0.0}, night {night}; 10 s on {c10.Day}/{c10.At:0.0}; 70 s more {dawn.Day}/{dawn.At:0.0}");

        // ---- the citadel down: the siege's end, but in a valley of the map the valley's
        var siege = Fresh();
        siege.KeepHp = 0;
        Step(siege, .1);
        var held = Fresh();
        held.Campaign = true;
        held.SetSource(new MapRaids());
        held.Clock.Left = 1e9;
        held.Raid(new List<UnitType> { Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Orc, Kinds.Orc }, Sim.WestEdge);
        Step(held, 12);
        bool told = false;
        held.ValleyFell += () => told = true;
        held.KeepHp = 0;
        Step(held, 1);
        int holders = held.Units.Count(u => u.Team == Team.Horde && !u.Dead && u.Guard);
        double was = held.Time;
        Step(held, 5);
        check("the citadel down ends a siege; in a valley of the map the valley falls instead: the map is told, the raid is over, its raiders stay to hold the valley, and the valley runs on",
            siege.Over && !siege.Lost && held.Lost && told && !held.Over && !held.Raids.Active && holders >= 4 && held.Time > was + 4.9,
            $"siege over {siege.Over}; in the map's valley: lost {held.Lost}, told {told}, over {held.Over}, raid {held.Raids.Active}, holding it {holders}, ran on {held.Time - was:0.0} s");

        // ---- the first valley, made by the campaign: its citadel, without the old places
        var sw = Stopwatch.StartNew();
        var hsim = camp.OpenValley(dir, home, out _);
        long made = sw.ElapsedMilliseconds;
        hsim.WildTarget = 0;
        hsim.Clock.Left = 1e9;
        bool oldPlaces = hsim.Site.Landmarks.Exists(l => l.Kind is LandmarkKind.Mine or LandmarkKind.Power);
        check("the first valley of a campaign: a hexagon with the citadel, without the old set of places (the mines and the barrow lie in other valleys), run by the map's clock and the realm's gold, its raids the map's",
            hsim.Site.Hex != null && hsim.Site.HasKeep && hsim.Goals.Count > 0 && !oldPlaces && hsim.Campaign && hsim.Source is MapRaids && hsim.Stock.Gold == camp.Gold && hsim.Clock.Day == camp.Clock.Day,
            $"made in {made} ms; hexagon {hsim.Site.Hex != null}, citadel {hsim.Site.HasKeep}, {hsim.Goals.Count} cells at its walls; old places {oldPlaces}; campaign {hsim.Campaign}, raids {hsim.Source.Key}, gold {hsim.Stock.Gold}, day {hsim.Clock.Day}");

        // ---- a party in over each of the six sides
        var hex = hsim.Site.Hex;
        var parties = new List<Unit>[6];
        for (int s = 0; s < 6; s++) parties[s] = hsim.Arrive(Band(), s);
        // as they left the last valley: the worker hurt to half as he came (he mends after, as anyone does)
        var cameHurt = parties.Select(p => p.Find(u => u.T == Kinds.Worker) is { } wk ? wk.Hp / wk.MaxHp : 0).ToArray();
        Step(hsim, 50);
        var lines = new List<string>();
        int sidesOk = 0;
        for (int s = 0; s < 6; s++)
        {
            var p = parties[s];
            var (sx, sz) = hex.SidePoint(s, .5f, Sim.ArriveIn);
            float least = p.Count == 0 ? 0 : p.Min(u => hex.FromSide(s, u.X, u.Z).d), most = p.Count == 0 ? 0 : p.Max(u => MathF.Sqrt((u.X - sx) * (u.X - sx) + (u.Z - sz) * (u.Z - sz)));
            var worker = p.Find(u => u.T == Kinds.Worker);
            bool ok = p.Count == 6 && p.All(u => !u.Dead) && p.Count(u => u.T == Kinds.Captain) == 1 && least >= 15 * VoxelWorld.PerMetre && most <= 20 * VoxelWorld.PerMetre
                && worker != null && Math.Abs(cameHurt[s] - .5f) < .001f;
            if (ok) sidesOk++;
            lines.Add($"side {s}: {p.Count(u => !u.Dead)} of {p.Count} standing, {least / VoxelWorld.PerMetre:0} m in at the least, {most / VoxelWorld.PerMetre:0} m from the spot at the most, worker came {cameHurt[s]:0.00}");
        }
        check("a party comes in over each of the six sides, the commander in it and the hurt worker hurt still, and stands inside, some way in from its side",
            sidesOk == 6, string.Join("; ", lines));

        // ---- going out over a side: taken off, and handed to the map as they were
        int leaving = hsim.Depart(parties[1], Hex.East) + hsim.Depart(parties[0], Hex.East);
        Step(hsim, 150);
        // (looked at here; the map takes them as the valley is left, below)
        var out1 = hsim.Departed;
        var went = out1.Count == 1 ? out1[0] : null;
        bool stayed = Enumerable.Range(2, 4).All(s => parties[s].All(u => !u.Dead));
        bool goneOff = parties[0].Concat(parties[1]).All(u => u.Dead && !hsim.Units.Contains(u));
        int hurt = went?.Members.Count(m => m.Kind == Kinds.Worker && m.Hp < m.MaxHp * .9f && m.Hp > m.MaxHp * .4f) ?? 0;
        check("ours sent out over a side go to its edge and over it: off the valley, one party of that side for the map with all of them as they were (two commanders, the hurt workers hurt), the rest staying",
            leaving == 12 && went is { Side: Hex.East } && went.Members.Count == 12 && went.Members.Count(m => m.Kind == Kinds.Captain) == 2 && hurt == 2 && goneOff && stayed,
            $"{leaving} sent; {out1.Count} parties out{(went == null ? "" : $", by side {went.Side} with {went.Members.Count}, commanders {went.Members.Count(m => m.Kind == Kinds.Captain)}, hurt workers {hurt}")}; all gone off {goneOff}; the rest stayed {stayed}");

        // ---- the valley into the campaign's folder and back: the same world, the same people, the map's file beside it
        int sx0 = hsim.Site.KeepX + 120, sz0 = hsim.Site.KeepZ + 40, sy0 = hsim.World.SurfAt(sx0, sz0);
        for (int y = sy0; y < sy0 + 6; y++) for (int x = sx0; x < sx0 + 4; x++) hsim.World.Set(x, y, sz0, Mat.Stone);
        camp.Leave(hsim, home);
        camp.SaveValley(dir, home, hsim, "Первая долина");
        camp.Write(dir);
        long sum0 = WorldSum(hsim.World);
        var people0 = hsim.Units.Where(u => !u.Dead).OrderBy(u => u.Id).Select(u => (u.Id, u.T.Key, X: (int)u.X, Z: (int)u.Z, Hp: (int)u.Hp)).ToList();
        var back = Campaign.Read(dir);
        var hsim2 = back.OpenValley(dir, back.Home, out _);
        long sum1 = WorldSum(hsim2.World);
        var people1 = hsim2.Units.Where(u => !u.Dead).OrderBy(u => u.Id).Select(u => (u.Id, u.T.Key, X: (int)u.X, Z: (int)u.Z, Hp: (int)u.Hp)).ToList();
        var party1 = back.Parties.FirstOrDefault();
        check("a valley written to the campaign's folder reads back the same, voxel for voxel and man for man, and the map's file with it: its cells, the clock, the gold, the party on the map",
            sum0 == sum1 && people0.SequenceEqual(people1) && hsim2.World.Get(sx0, sy0 + 2, sz0) == Mat.Stone && back.Cells.Count == 19 && back.Clock == camp.Clock && back.Gold == camp.Gold
            && back.Home.Visited && party1 != null && party1.Party.Members.Count == 12 && party1.Party.Side == Hex.East && hsim.Departed.Count == 0 && hsim2.Campaign && hsim2.Clock.Now == back.Clock,
            $"world {sum0:x} → {sum1:x}; people {people0.Count} → {people1.Count}, same {people0.SequenceEqual(people1)}; cells {back.Cells.Count}, clock {back.Clock}, gold {back.Gold}; party on the map {party1?.Party.Members.Count} by side {party1?.Party.Side}; read clock {hsim2.Clock.Now}");
        hsim2 = null;

        // ---- the outpost's place: a valley of ore taken or to be taken
        var ore = camp.Cells.First(c => c.Kind == ValleyKind.Ore && Campaign.Distance(0, 0, c.Q, c.R) == 1);
        var osim = camp.OpenValley(dir, ore, out _);
        osim.WildTarget = 0;
        osim.Clock.Left = 1e9;
        var os = osim.Site;
        int g = os.KeepBase, uneven = 0, cluttered = 0;
        for (int z = os.KeepZ - 80; z <= os.KeepZ + 80; z += 2)
            for (int x = os.KeepX - 80; x <= os.KeepX + 80; x += 2)
            {
                if (Brushes.Dist(x - os.KeepX, z - os.KeepZ) > 80 || Math.Abs(x - os.KeepX) < 8 && Math.Abs(z - os.KeepZ) < 8) continue;
                int top = osim.World.ColTop(x, z);
                if (Math.Abs(os.HTop[z * osim.World.SX + x] - g) > 1) uneven++;
                if (top > g && osim.World.Get(x, top - 1, z) is not (Mat.Wood or Mat.Keep or Mat.KeepShade)) cluttered++;
            }
        int falls = 0;
        osim.Collapsed += (n, _, _, _) => falls += n;
        Step(osim, 40);
        bool standing = !osim.Over && !osim.Lost && osim.KeepHp == osim.KeepMax && falls == 0 && osim.Fliers.Count == 0;
        check("a valley without a citadel: the outpost's place in the middle is level and clear, its stone the spot the horde makes for, nobody goes into a citadel or hires at one there, and it holds (nothing falls, nothing ends)",
            os.Holding == Holding.Outpost && !os.HasKeep && osim.Gate >= 0 && uneven == 0 && cluttered == 0 && osim.Goals.Count > 0 && osim.CantEnlist(Kinds.Archer) != null && standing,
            $"{os.Holding}, gate {osim.Gate}; {uneven} columns off its height, {cluttered} cluttered; {osim.Goals.Count} cells at the stone; hiring: «{osim.CantEnlist(Kinds.Archer)}»; over {osim.Over}, lost {osim.Lost}, keep {osim.KeepHp}, fallen {falls}");

        // a raid in the outpost's valley makes for the stone and takes the valley
        bool oTold = false;
        osim.ValleyFell += () => oTold = true;
        osim.KeepHp = 40;
        osim.Raid(new List<UnitType> { Kinds.Orc, Kinds.Orc, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin }, os.EntrySide);
        double took = 0;
        for (double t = 0; t < 300 && !osim.Lost; t += 1) { Step(osim, 1); took = t + 1; }
        int oHold = osim.Units.Count(u => u.Team == Team.Horde && !u.Dead && u.Guard);
        check("a raid in the outpost's valley makes for its stone, and the stone down the valley is the horde's: told, not the game's end, the raid over and its raiders holding it",
            osim.Lost && oTold && !osim.Over && !osim.Raids.Active && oHold > 0,
            $"lost {osim.Lost} after {took:0} s, told {oTold}, over {osim.Over}, raid {osim.Raids.Active}, holding it {oHold}");
        camp.SaveValley(dir, ore, osim, "Рудная долина");
        osim = null;

        // ---- the horde's town, and its garrison keeping at home
        var lsim = camp.OpenValley(dir, lair, out _);
        lsim.WildTarget = 0;
        lsim.Clock.Left = 1e9;
        var ls = lsim.Site;
        int logs = 0, thatch = 0, embers = 0;
        for (int z = ls.KeepZ - Holdings.PalisadeR - 4; z <= ls.KeepZ + Holdings.PalisadeR + 4; z++)
            for (int x = ls.KeepX - Holdings.PalisadeR - 4; x <= ls.KeepX + Holdings.PalisadeR + 4; x++)
                for (int y = ls.KeepBase - 2; y < ls.KeepBase + 60; y++)
                {
                    byte m = lsim.World.Get(x, y, z);
                    if (m == Mat.Wood) logs++; else if (m == Mat.Hay) thatch++; else if (m == Mat.Ember) embers++;
                }
        var guards = lsim.Units.Where(u => u.Team == Team.Horde && !u.Dead).ToList();
        int expected = Sim.Roster(Campaign.LairThreat, 1).Count(t => !Kinds.IsEngine(t) && t != Kinds.Miner && t != Kinds.Sapper && t != Kinds.Thief);
        double within = guards.Count == 0 ? 0 : guards.Max(u => Brushes.Dist(u.X - ls.KeepX, u.Z - ls.KeepZ));
        int lFalls = 0;
        lsim.Collapsed += (n, _, _, _) => lFalls += n;
        Step(lsim, 40);
        float strayed = guards.Count == 0 ? 0 : guards.Max(u => { var (hx, _, hz) = lsim.Nav.Feet(u.Home); return MathF.Sqrt((u.X - hx) * (u.X - hx) + (u.Z - hz) * (u.Z - hz)); });
        check("the horde's town: a palisade of logs, thatched huts, a fire, the lair named; its garrison at its posts, all of them, keeping at home; nothing falls, no raid",
            ls.Holding == Holding.HordeTown && ls.Landmarks.Exists(l => l.Kind == LandmarkKind.Lair) && logs > 20000 && thatch > 5000 && embers > 20 && ls.HordePosts.Count >= 9
            && guards.Count == expected && guards.All(u => u.Guard) && within < Holdings.PalisadeR + 2 * VoxelWorld.PerMetre && guards.All(u => !u.Dead) && strayed < 4 * VoxelWorld.PerMetre
            && lFalls == 0 && !lsim.Raids.Active && !lsim.Lost && lsim.Goals.Count == 0,
            $"lair {ls.Landmarks.Exists(l => l.Kind == LandmarkKind.Lair)}; {logs} voxels of logs, {thatch} of thatch, {embers} embers, {ls.HordePosts.Count} posts; garrison {guards.Count} of {expected}, {within / VoxelWorld.PerMetre:0.0} m out at the most, {guards.Count(u => !u.Dead)} alive, strayed {strayed / VoxelWorld.PerMetre:0.0} m; fallen {lFalls}");

        // ours come into the enemy's valley: their camp where they came in
        var ours = lsim.Arrive(Band(), lair.EntrySide);
        Step(lsim, 30);
        var (gx, _, gz) = lsim.Nav.Feet(lsim.Gate);
        float camp0 = ls.Hex.FromSide(lair.EntrySide, gx, gz).d;
        check("ours come into the horde's valley and stand; where they came in is their camp (the wounded are carried back there); the garrison stays at home",
            ours.Count == 6 && ours.All(u => !u.Dead) && camp0 < Site.StripDepth + 8 && lsim.Units.Count(u => u.Team == Team.Horde && !u.Dead) == expected,
            $"{ours.Count(u => !u.Dead)} of {ours.Count} standing; the camp {camp0 / VoxelWorld.PerMetre:0.0} m in from their side; garrison {lsim.Units.Count(u => u.Team == Team.Horde && !u.Dead)}");
        // what the map remembers of a valley left: the horde standing in it; and the outpost of a valley taken, marked and built
        camp.Leave(lsim, lair);
        int remembered = lair.Sum.Keep.Count;
        // (the garrison beaten first, as it must be before an outpost goes up in its yard: nobody stands in the workers' way)
        foreach (var u in lsim.Units.ToArray()) if (!u.Dead && u.Team == Team.Horde) lsim.Hurt(u, null, 1e6f);
        Step(lsim, 2);
        int marked0 = lsim.Orders.BuildTotal;
        bool standsBefore = lsim.OutpostStands();
        string why = lsim.RaiseOutpost();
        int marked = lsim.Orders.BuildTotal - marked0;
        var (ox, oz) = lsim.OutpostSpot();
        lsim.InstantLabour = true;
        for (int i = 0; i < 4; i++) { lsim.CompleteAll(); Step(lsim, 1); }
        lsim.InstantLabour = false;
        bool standsAfter = lsim.OutpostStands();
        string inFirst = hsim.RaiseOutpost();
        check("a valley left, the map remembers the horde standing in it; an outpost is a stone tower marked for the workers in the yard, standing once they have laid it, and none in the first valley",
            remembered == expected && why == null && marked > 2000 && Brushes.Dist(ox - ls.KeepX, oz - ls.KeepZ) < Holdings.PalisadeR && !standsBefore && standsAfter && inFirst != null,
            $"remembered {remembered} of {expected}; outpost «{why}», {marked} voxels marked at {Brushes.Dist(ox - ls.KeepX, oz - ls.KeepZ) / VoxelWorld.PerMetre:0.0} m from the middle, standing before {standsBefore}, after {standsAfter} ({lsim.OutpostLaid():P0} laid); in the first valley «{inFirst}»");
        camp.SaveValley(dir, lair, lsim, "Логово");
        var files = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(f => f).ToList();
        check("the campaign's folder holds the map's file and a file for each valley visited",
            files.Contains(Campaign.FileName) && files.Contains(home.File) && files.Contains(ore.File) && files.Contains(lair.File) && files.Count == 4,
            string.Join(", ", files));
        Directory.Delete(dir, true);

        // ---- the map as the player plays it: ours gone out put on the map and sent on, seen as they go, come in by the side before
        var play = Campaign.New(23);
        var plair = play.Cells.Single(c => c.Holding == Holding.HordeTown);
        var nextTo = play.Neighbour(play.Home, Campaign.SideToward(play.Home, plair.Q, plair.R));
        int seen0 = play.Cells.Count(play.Seen);
        bool lairHidden = !play.Seen(plair);
        var mp = play.Out(Band(), nextTo);
        double left0 = play.WalkLeft(mp), expectLeft = MapPath.WayHours(play.Home, mp.Path) * MapPath.Pace(mp) * MapPath.HourSeconds;
        bool seenHalf = false;
        for (int i = 0; i < 4000 && mp.Moving; i++) { play.Advance(2); if (play.StepShare(mp) > .55) seenHalf |= play.Cells.Count(play.Seen) > seen0; }
        var come = play.News.Find(n => n.Kind == NewsKind.Arrived && n.Party == mp);
        check("ours gone out of the valley are put on the map and sent on: they walk as long as the way takes, are told come, come into the valley by the side facing the cell before; the fog lifts round them from halfway (seven cells at first, the lair hidden)",
            play.CellOf(mp) == nextTo && Math.Abs(left0 - expectLeft) < 1 && come != null && play.Neighbour(nextTo, play.EntrySideOf(mp)) == play.Home && seen0 == 7 && lairHidden && seenHalf,
            $"at {play.CellOf(mp).Kind}; time {left0:0} s of {expectLeft:0}; told {come != null}; comes in by side {play.EntrySideOf(mp)}; seen at first {seen0}, the lair hidden {lairHidden}, more seen from halfway {seenHalf}");

        // ---- an outpost raised in the valley we play in makes it ours; the horde's armies on their way to us are known
        var raise = Campaign.New(29);
        var spot = raise.Cells.First(c => c.Owner == Owner.None && Campaign.Distance(0, 0, c.Q, c.R) == 1);
        spot.Outpost = true;
        raise.OutpostStands(spot);
        var foe = new MapParty { Q = plair.Q, R = plair.R, CameQ = plair.Q, CameR = plair.R, Owner = Owner.Horde, Aim = PartyAim.Strike, Party = new Party() };
        foreach (var t in new[] { Kinds.Goblin, Kinds.Goblin, Kinds.Orc }) foe.Party.Members.Add(new Traveller { Kind = t, Hp = t.Hp, MaxHp = t.Hp });
        var flair = raise.Cells.Single(c => c.Holding == Holding.HordeTown);
        foe.Q = flair.Q; foe.R = flair.R; foe.CameQ = flair.Q; foe.CameR = flair.R;
        raise.Parties.Add(foe);
        bool sent = raise.Send(foe, raise.Home);
        var coming = raise.Coming();
        string dir2 = Path.Combine(Path.GetTempPath(), $"vt-campaign-play-{Environment.ProcessId}");
        raise.Write(dir2);
        var rb = Campaign.Read(dir2);
        Directory.Delete(dir2, true);
        check("an outpost standing in a valley nobody holds makes it ours, said; an army of the horde sent at the first valley is known to be coming; the map's file keeps the outpost",
            spot.Owner == Owner.Ours && spot.Sum.Raised >= 1 && raise.News.Exists(n => n.Kind == NewsKind.Taken && n.Cell == spot) && sent && coming.Count == 1 && coming[0] == foe
            && rb.At(spot.Q, spot.R) is { Owner: Owner.Ours, Outpost: true },
            $"owner {spot.Owner}, raised {spot.Sum.Raised}; sent {sent}, coming {coming.Count}; read back {rb.At(spot.Q, spot.R).Owner} outpost {rb.At(spot.Q, spot.R).Outpost}");
    }

    /// <summary>Every voxel of a world summed up into one number.</summary>
    static long WorldSum(VoxelWorld w)
    {
        ulong h = 1469598103934665603;
        for (int ci = 0; ci < w.ChunkCount; ci++)
        {
            var d = w.ChunkData(ci);
            if (d == null) { h = (h ^ (ulong)(w.ChunkUniform(ci) + 1)) * 1099511628211; continue; }
            for (int i = 0; i < d.Length; i += 8) h = (h ^ BitConverter.ToUInt64(d, i)) * 1099511628211;
        }
        return (long)h;
    }
}
