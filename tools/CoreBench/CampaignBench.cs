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
            // (they stand together where they were sent: 30 m in along the side's middle, or short of a river or a crag
            // across the way)
            float cx = p.Count == 0 ? 0 : p.Average(u => u.X), cz = p.Count == 0 ? 0 : p.Average(u => u.Z);
            float least = p.Count == 0 ? 0 : p.Min(u => hex.FromSide(s, u.X, u.Z).d), most = p.Count == 0 ? 0 : p.Max(u => MathF.Sqrt((u.X - cx) * (u.X - cx) + (u.Z - cz) * (u.Z - cz)));
            var worker = p.Find(u => u.T == Kinds.Worker);
            bool ok = p.Count == 6 && p.All(u => !u.Dead && u.State != UnitState.Walk) && p.Count(u => u.T == Kinds.Captain) == 1 && least >= 2 * VoxelWorld.PerMetre
                && hex.FromSide(s, cx, cz).d <= Sim.ArriveIn + 4 * VoxelWorld.PerMetre && most <= 8 * VoxelWorld.PerMetre && worker != null && Math.Abs(cameHurt[s] - .5f) < .001f;
            if (ok) sidesOk++;
            lines.Add($"side {s}: {p.Count(u => !u.Dead)} of {p.Count} standing, {least / VoxelWorld.PerMetre:0} m in at the least, {hex.FromSide(s, cx, cz).d / VoxelWorld.PerMetre:0} m in on the whole, {most / VoxelWorld.PerMetre:0} m apart at the most, worker came {cameHurt[s]:0.00}");
        }
        check("a party comes in over each of the six sides, the commander in it and the hurt worker hurt still, and stands together inside, some way in from its side",
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

        Realm(check, Step);
    }

    /// <summary>
    /// The realm's places, commanders, roads and rivers (closing what phase 1 left): the places of a valley are its own —
    /// ours gone out on the map keep theirs, ours come from another valley are its guests and hold their own valley's, and
    /// at dawn guests settle where there is room; two commanders, and one more for each level of a working town hall, this
    /// valley's or another of ours; the lair's garrison on the board is the one met in it; every cell has its river and its
    /// road, two cells side by side agree on them, no river runs in a ring, and a way along roads is quicker.
    /// </summary>
    static void Realm(Action<string, bool, string> check, Action<Sim, double> Step)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vt-campaign-realm-{Environment.ProcessId}");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        var realm = Campaign.New(31);
        realm.Advance(1);
        var home = realm.Home;
        var ore = realm.Cells.First(c => c.Kind == ValleyKind.Ore && Campaign.Distance(0, 0, c.Q, c.R) == 1);
        var sim = realm.OpenValley(dir, home, out _);
        sim.WildTarget = 0; sim.Clock.Left = 1e9;
        foreach (var k in new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Archer, Kinds.Swordsman, Kinds.Captain, Kinds.Captain }) sim.Spawn(k, sim.Gate);
        int own0 = sim.Household();
        // four of them out over the side toward the ore valley, onto the map
        int side = Campaign.SideToward(home, ore.Q, ore.R);
        var four = new List<Unit>
        {
            sim.Units.First(u => !u.Dead && u.T == Kinds.Captain), sim.Units.First(u => !u.Dead && u.T == Kinds.Swordsman),
            sim.Units.First(u => !u.Dead && u.T == Kinds.Archer), sim.Units.First(u => !u.Dead && u.T == Kinds.Worker),
        };
        sim.Depart(four, side);
        for (int i = 0; i < 300 && sim.Units.Exists(u => !u.Dead && u.LeaveBy >= 0); i++) Step(sim, 1);
        var gone = sim.TakeDeparted();
        var party = gone.Count == 1 ? realm.Out(gone[0], ore) : null;
        realm.Reckon(sim, home);
        int away = sim.AwayPlaces, own1 = sim.Household(), key = Campaign.KeyOf(home);
        string full = sim.CantEnlist(Kinds.Archer);
        sim.Stock.Gold = 5000;
        int hired = 0;
        while (sim.CantEnlist(Kinds.Archer) == null && hired < 10) { sim.Enlist(Kinds.Archer); hired++; }
        check("ours gone out of the valley onto the map keep their places in it: the household counts them, hiring fills only what is left, and on the map they hold the valley's places",
            party != null && party.Party.Members.Count == 4 && party.Party.Members.All(m => m.Billet == key) && away == 4 && own1 == own0 && hired == sim.Places - own0,
            $"on the map {party?.Party.Members.Count}, holding {party?.Party.Members.Count(m => m.Billet == key)}; away {away}, household {own0} → {own1} of {sim.Places}; hired {hired}");
        // they come into the ore valley as its guests
        realm.Parties.Remove(party);
        var osim = realm.OpenValley(dir, ore, out _);
        osim.WildTarget = 0; osim.Clock.Left = 1e9;
        var come = osim.Arrive(party.Party, Campaign.SideToward(ore, 0, 0));
        bool settledThere = osim.SettleGuests();
        check("come into another valley they are its guests: they hold their own valley's places, not its; with no room there (no citadel, no dwellings) they do not settle at dawn",
            come.Count == 4 && osim.Guests() == 4 && osim.Household() == 0 && osim.Places == 0 && come.All(u => u.Billet == key) && !settledThere,
            $"come {come.Count}, guests {osim.Guests()}, household {osim.Household()} of {osim.Places}, holding home's {come.Count(u => u.Billet == key)}; settled {settledThere}");
        // the ore valley ours and left with them in it: the first valley still keeps their places
        ore.Owner = Owner.Ours; ore.Outpost = true;
        realm.Leave(osim, ore);
        realm.Reckon(sim, home);
        int awayKept = sim.AwayPlaces;
        // guests of the ore valley come to the first one, and settle there at dawn while it has room
        var visit = new Party();
        foreach (var k in new[] { Kinds.Spearman, Kinds.Spearman })
            visit.Members.Add(new Traveller { Kind = k, Look = 5, Hp = k.Hp, MaxHp = k.Hp, Billet = Campaign.KeyOf(ore) });
        foreach (var u in sim.Units.Where(u => !u.Dead && u.T == Kinds.Archer).Take(2).ToList()) sim.Hurt(u, null, 1e6f);
        Step(sim, 1);
        var visitors = sim.Arrive(visit, side);
        int guests = sim.Guests(), before = sim.Household();
        string room = sim.CantEnlist(Kinds.Archer);
        bool settled = sim.SettleGuests();
        check("kept in another valley of ours, they still hold the first valley's places; guests come to it hold theirs, and settle in it at dawn while it has room",
            awayKept == 4 && visitors.Count == 2 && guests == 2 && settled && sim.Guests() == 0 && sim.Household() == before + 2 && visitors.All(u => u.Billet < 0),
            $"kept away {awayKept}; guests {guests}, household {before} → {sim.Household()} of {sim.Places}, hiring before «{room}»; settled {settled}");

        // two commanders, and one more for each level of a working town hall, here or in another valley of ours
        sim.Knowledge = 20;
        sim.Learn("b_hall");
        int cap0 = sim.CaptainCap, have0 = sim.Captains;
        string refused = sim.CantEnlist(Kinds.Captain);
        ore.Sum.Hall = 1;
        realm.Reckon(sim, home);
        int capAway = sim.CaptainCap;
        ore.Sum.Hall = 0;
        realm.Reckon(sim, home);
        // a town hall of the first valley's own, put up at once on level ground near the citadel
        var (hx, hz) = Level(sim);
        sim.InstantLabour = true;
        string put = hx < 0 ? "no level ground" : sim.PutUp(Sim.TownKindOf("hall"), hx, hz, false, out var hall);
        for (int i = 0; i < 120 && sim.HallLevel() == 0; i++) { sim.CompleteAll(); Step(sim, 1); }
        sim.InstantLabour = false;
        int cap1 = sim.CaptainCap;
        // (a place made for him: the household is full)
        sim.Hurt(sim.Units.First(u => !u.Dead && u.T == Kinds.Worker), null, 1e6f);
        Step(sim, .1);
        string third = sim.CantEnlist(Kinds.Captain);
        bool took = third == null && sim.Enlist(Kinds.Captain);
        check("the realm has two commanders, one away on the map counted; a town hall's level gives one more, another valley's or this one's: a third is hired",
            cap0 == Sim.MaxCaptains && have0 == 2 && refused != null && refused.Contains("2 из 2") && capAway == 3 && put == null && sim.HallLevel() == 1 && cap1 == 3 && took && sim.Captains == 3,
            $"cap {cap0}, have {have0} (away {sim.CaptainsAway}): «{refused}»; with a hall elsewhere {capAway}; the hall «{put}», level {sim.HallLevel()}, cap {cap1}: «{third}», hired {took}, now {sim.Captains}");

        // the lair's garrison as the board counts it is the one met in it
        for (int d = 0; d < 4; d++) realm.Advance(DayClock.Cycle);
        var lair = realm.Cells.Single(c => c.Holding == Holding.HordeTown);
        int counted = realm.Foes(lair), all = lair.Sum.Keep.Count;
        var lsim = realm.OpenValley(dir, lair, out _);
        int met = lsim.Units.Count(u => !u.Dead && u.Team == Team.Horde);
        check("the lair's garrison on the board is the one met in its valley; those who only come raiding wait outside it",
            counted > 5 && counted == met && counted <= all, $"counted {counted} of {all} kept, met {met}");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);

        // roads and rivers: every cell has both, neighbours agree, no ring, the way along roads quicker
        int cells = 0, rivers = 0, roads = 0, agree = 0, sides = 0, rings = 0, apart = 0;
        foreach (uint seed in new uint[] { 3, 11, 23, 31, 47, 59, 71, 97 })
        {
            var m = Campaign.New(seed);
            foreach (var c in m.Cells)
            {
                cells++;
                var rs = Enumerable.Range(0, 6).Where(s => c.Edges[s].River >= 0).ToList();
                // two sides, not next to each other, one on each side of the road's line (the road fords it)
                int e0 = c.EntrySide;
                bool OnArc(int x) => x == (e0 + 1) % 6 || x == (e0 + 2) % 6;
                if (rs.Count == 2 && OnArc(rs[0]) != OnArc(rs[1]) && !rs.Contains(e0) && !rs.Contains((e0 + 3) % 6)) rivers++;
                if (rs.Count == 2 && (rs[1] - rs[0]) is 1 or 5) apart++;
                if (c.Edges[c.EntrySide].Road >= 0 && c.Edges[(c.EntrySide + 3) % 6].Road >= 0 && Enumerable.Range(0, 6).Count(s => c.Edges[s].Road >= 0) == 2) roads++;
                for (int s = 0; s < 6; s++)
                    if (m.Neighbour(c, s) is { } n)
                    {
                        sides++;
                        // both or neither have a river over it, at one spot (each reads it from its own first corner),
                        // and not where a road crosses
                        float pa = Campaign.RiverAt(c, s), pb = Campaign.RiverAt(n, (s + 3) % 6);
                        bool road = c.Edges[s].Road >= 0 || n.Edges[(s + 3) % 6].Road >= 0;
                        if (pa < 0 && pb < 0 || pa >= 0 && pb >= 0 && Math.Abs(pa + pb - 1) < 1e-4 && (!road || Math.Abs(pa - .5f) > .1f)) agree++;
                    }
            }
            // a river followed from a cell comes back to it only in a ring
            foreach (var c in m.Cells)
            {
                var at = c; int s0 = Enumerable.Range(0, 6).FirstOrDefault(s => c.Edges[s].River >= 0, -1), steps = 0;
                while (s0 >= 0 && m.Neighbour(at, s0) is { } n && steps++ < 40)
                {
                    int back = (s0 + 3) % 6;
                    s0 = Enumerable.Range(0, 6).FirstOrDefault(s => s != back && n.Edges[s].River >= 0, -1);
                    at = n;
                    if (at == c) { rings++; break; }
                }
            }
        }
        var rm = Campaign.New(31);
        var lr = rm.Cells.Single(c => c.Holding == Holding.HordeTown);
        var way = MapPath.Find(rm, rm.Home, lr, Owner.Ours);
        double along = MapPath.WayHours(rm.Home, way);
        int onRoads = 0, prev = 0;
        var a = rm.Home;
        foreach (var b in way) { int s = MapPath.Side(a, b); if (a.Edges[s].Road >= 0 || b.Edges[(s + 3) % 6].Road >= 0) onRoads++; a = b; prev++; }
        check("every cell has its river and its road as its valley lays them, the river between the road's two sides (the road fords it), two cells side by side agree on a river between them and where it crosses, no river runs in a ring; a way to the lair takes the roads",
            rivers == cells && roads == cells && agree == sides && rings == 0 && apart == 0 && way != null && onRoads >= 1,
            $"{cells} cells: rivers {rivers}, roads {roads}, sides agreeing {agree} of {sides}, rings {rings}, rivers turning at once {apart}; to the lair {way?.Count} cells, {along:0.0} h, {onRoads} steps on a road");
    }

    /// <summary>Level ground near the citadel for a building: the first spot round it with its height even and no trees.</summary>
    static (int x, int z) Level(Sim sim)
    {
        var w = sim.World; var site = sim.Site;
        for (int r = 120; r < 420; r += 8)
            for (int k = 0; k < 24; k++)
            {
                int x = site.KeepX + (int)(Math.Cos(k * Math.Tau / 24) * r), z = site.KeepZ + (int)(Math.Sin(k * Math.Tau / 24) * r);
                if (!w.OnLand(x, z) || site.Forbidden(x, z)) continue;
                int lo = int.MaxValue, hi = 0;
                for (int dx = -44; dx <= 44; dx += 4)
                    for (int dz = -44; dz <= 44; dz += 4)
                    {
                        if (!w.OnLand(x + dx, z + dz)) { hi = 999; continue; }
                        int h = w.SurfAt(x + dx, z + dz); lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                        if (Mat.IsTrunk(w.Get(x + dx, h + 2, z + dz)) || w.Get(x + dx, h - 1, z + dz) == Mat.Water) hi = 999;
                    }
                if (hi - lo <= 2) return (x, z);
            }
        return (-1, -1);
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
