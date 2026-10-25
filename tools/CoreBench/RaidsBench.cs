using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's raids (--raids, Sim.Raids.cs): what brings the raids is apart from the raid, the night, the threat and the
/// rewards. The siege still brings twelve raids at nightfall and ends; the night holds while a raid is in the valley and
/// ends with it; the threat grows night by night and with the settlement's rank; a raid called by Sim.Raid comes by day,
/// by any side, of whatever it is told, and goes out by where it came in; knowledge comes for deeds (the fallen leader,
/// the place taken, the building put up); the sandbox's horde comes only when called while the sun stands at noon and
/// the days are counted all the same; the map's way lets nights go by with no raid; all of it read back from a save.
/// </summary>
static class RaidsBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        Sim Fresh(out MapParams mp)
        {
            mp = new MapParams();
            var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
            var site = MapGen.Generate(w, mp);
            w.Compact();
            var s = new Sim(w, site) { WildTarget = 0 };
            return s;
        }
        void Run(Sim s, double seconds) { for (double t = 0; t < seconds; t += .05) s.Step(.05f); }
        // the raid in the valley ended by hand: no more to come, those come vanish
        void Clear(Sim s)
        {
            s.Raids._queue.Clear();
            foreach (var u in s.Units) if (u.Team == Team.Horde && !u.Dead) { u.Dead = true; u.DiedAt = -999; }
        }

        // ---- the siege: twelve raids at nightfall, each a night stronger, then the end
        var sim = Fresh(out var mpS);
        var nights = new List<bool>(); var threats = new List<float>(); var days = new List<bool>();
        for (int i = 0; i < 12 && !sim.Over; i++)
        {
            // the day runs out by itself: the raid comes at nightfall, the night with it
            sim.Clock.Left = 3;
            Run(sim, 3.5);
            nights.Add(sim.Raids.Active && sim.Clock.Night && sim.Raids.AtNight);
            threats.Add(sim.Raids.Threat);
            Run(sim, 1);
            Clear(sim);
            Run(sim, .5);
            days.Add(!sim.Clock.Night && !sim.Raids.Active);
        }
        check("the siege brings twelve raids, each at nightfall, the night ending with it; each a night stronger; the twelfth beaten back ends the siege",
            nights.Count == 12 && nights.All(x => x) && days.Take(11).All(x => x) && threats.SequenceEqual(Enumerable.Range(1, 12).Select(n => (float)n))
            && sim.Over && sim.Won && sim.Raids.Repelled == 12,
            $"raids {nights.Count}, at night {nights.Count(x => x)}, day after {days.Count(x => x)}; threats {string.Join(" ", threats)}; over {sim.Over}, won {sim.Won}, repelled {sim.Raids.Repelled}");

        // ---- the threat: the night's number, and a quarter more a rank of the settlement
        var simT = Fresh(out _);
        float t0 = simT.Threat;
        simT.Standing = 2;
        float t2 = simT.Threat;
        simT.PlanRaid();
        int many0 = Sim.Roster(1f, 1).Count, many2 = simT.Bands.Where(b => b.State == BandState.Coming).Sum(b => b.Roster.Count);
        check("the threat is the night's number and grows with the settlement's rank (a quarter a rank): the horde comes the more for it",
            t0 == 1 && Math.Abs(t2 - 1.5f) < 1e-4 && simT.PlannedThreat == t2 && many2 > many0,
            $"threat {t0} → {t2} at rank 2, planned {simT.PlannedThreat}; raiders {many0} → {many2}");

        // ---- a raid called by Sim.Raid: by day, by the eastern side, of what it is told; out again by where it came in
        var simR = Fresh(out _);
        simR.SetSource(new MapRaids());
        simR.Clock.Left = 1e9;
        var roster = new List<UnitType> { Kinds.Orc, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Troll };
        simR.Raid(roster, 1, intel: .3);
        Run(simR, 10);
        var come = simR.Units.Where(u => u.Team == Team.Horde && !u.Dead).ToList();
        var hex = simR.Site.Hex ?? Hex.In(simR.World.SX, simR.World.SZ);
        float east = come.Count == 0 ? 0 : come.Average(u => u.X);
        bool byDay = simR.Raids.Active && !simR.Clock.Night && !simR.Raids.AtNight;
        var kinds = come.Select(u => u.T).GroupBy(t => t.Key).ToDictionary(g => g.Key, g => g.Count());
        var band = simR.Bands.FirstOrDefault(b => b.Edge == 1);
        check("a raid called by Sim.Raid comes at once by day, by the side it is told (the east), of the kinds it is told",
            byDay && come.Count == 5 && east > hex.CX + hex.HalfW * .5f && kinds.GetValueOrDefault("troll") == 1 && kinds.GetValueOrDefault("goblin") == 3 && band != null
            && Math.Abs(simR.Raids.Intel - .3) < 1e-6,
            $"by day {byDay}; {come.Count} come, their mean x {east:0} (the middle {hex.CX:0}, the east side {hex.CX + hex.HalfW:0}); {string.Join(", ", kinds.Select(k => $"{k.Key} {k.Value}"))}; intel {simR.Raids.Intel}");
        // routed, they go out east, where they came in, not west
        foreach (var u in come) u.Routed = true;
        Run(simR, 90);
        int gone = come.Count(u => u.Dead), westward = come.Count(u => !u.Dead && u.X < hex.CX);
        check("routed, they go out by where they came in", gone == come.Count && westward == 0 && !simR.Raids.Active,
            $"gone {gone} of {come.Count}, still in the valley west of the middle {westward}; the raid over {!simR.Raids.Active}"
            + string.Join("", come.Where(u => !u.Dead).Select(u => $"; {u.T.Key} {u.State} at {u.X:0},{u.Z:0} routed {u.Routed} band {u.Band?.Edge} entry {(u.Band != null && u.Band.Entry >= 0 ? simR.Nav.Feet(u.Band.Entry).ToString() : "-")} path {(u.Path == null ? -1 : u.Path.Count - u.PathI)} why {u.Why}")));

        // ---- knowledge by deeds: a leader and a troll felled in a raid, a place taken, a building put up
        var simK = Fresh(out _);
        simK.Clock.Left = 1e9;
        simK.Raids.Count = 5;
        simK.PlanWave(6);
        simK.CallRaid();
        Run(simK, 60);
        double k0 = simK.Knowledge;
        var leader = simK.Units.FirstOrDefault(u => u.Team == Team.Horde && !u.Dead && u.Leader);
        var troll = simK.Units.FirstOrDefault(u => u.Team == Team.Horde && !u.Dead && u.T == Kinds.Troll);
        if (leader != null) simK.Hurt(leader, null, 1e6f, "test");
        if (troll != null) simK.Hurt(troll, null, 1e6f, "test");
        double deeds = simK.DeedKnowledge;
        Clear(simK);
        Run(simK, .5);
        double got = simK.Knowledge - k0;
        // (2 and the raid's threat 6, and what the fallen were worth, a whole knowledge paid and the rest kept)
        check("knowledge for a raid beaten back is two and its threat, with what its fallen leader and troll were worth on top",
            leader != null && troll != null && deeds >= .5 - 1e-6 && got == Math.Floor(2 + 6 + deeds) && Math.Abs(simK.DeedKnowledge - (2 + 6 + deeds - got)) < 1e-6,
            $"leader {(leader != null)}, troll {(troll != null)}; deeds {deeds:0.00}; knowledge +{got}, kept over {simK.DeedKnowledge:0.00}");
        double k1 = simK.Knowledge;
        simK.PlaceDeed("place:test", "рудник", 1);
        simK.PlaceDeed("place:test", "рудник", 1);
        check("a place taken pays its knowledge once", simK.Knowledge == k1 + 1, $"knowledge {k1} → {simK.Knowledge}");

        // ---- the sandbox's horde on call: no raid of itself, the sun at noon, the days counted; called, it comes at night
        var simC = Fresh(out _);
        simC.SetSource(new OnCallRaids());
        int dayC0 = simC.Clock.Day;
        Run(simC, 1300);
        bool stood = !simC.Raids.Active && !simC.Clock.Night && simC.Clock.Left >= DayClock.DayLength / 2 - .01 && simC.Clock.Darkness == 0;
        int dayC1 = simC.Clock.Day;
        bool called = simC.CallRaid();
        bool atNight = simC.Raids.Active && simC.Clock.Night && simC.Raids.Intel == 1;
        Run(simC, 1);
        Clear(simC);
        Run(simC, .5);
        check("the sandbox's horde comes only when called, the sun standing at noon and the days counted all the same; called, it comes knowing all, the night with it, and goes",
            stood && dayC1 >= dayC0 + 1 && called && atNight && !simC.Raids.Active && !simC.Clock.Night && simC.Raids.Repelled == 1,
            $"stood at noon {stood}; day {dayC0} → {dayC1}; called {called}, at night {atNight}; repelled {simC.Raids.Repelled}, night now {simC.Clock.Night}");

        // ---- the map's way: the nights go by with no raid, the threat by the day
        var simM = Fresh(out var mpM);
        simM.SetSource(new MapRaids());
        simM.Clock.Left = 2;
        Run(simM, 3);
        bool night = simM.Clock.Night && !simM.Raids.Active;
        Run(simM, DayClock.NightLength + 1);
        bool morning = !simM.Clock.Night && simM.Clock.Day == 2 && simM.Raids.Count == 0 && simM.Threat == 2;
        check("by the map's way the night falls and goes by itself with no raid, and the threat grows by the day",
            night && morning && !simM.CallRaid(), $"night with no raid {night}; morning of day {simM.Clock.Day}, raids {simM.Raids.Count}, threat {simM.Threat}");

        // ---- read back: the source, the clock, the raids, a band's side
        simM.Raid(new List<UnitType> { Kinds.Goblin, Kinds.Goblin }, 2);
        simM.DeedKnowledge = .75;
        var ms = new MemoryStream();
        simM.Save(ms, mpM.Seed, "test", "");
        ms.Position = 0;
        var w2 = new VoxelWorld(mpM.SizeX, mpM.SizeY, mpM.SizeZ);
        Sim sim2;
        using (var reader = new SaveReader(ms))
        {
            var site2 = MapGen.Generate(w2, new MapParams { Seed = reader.Seed });
            w2.Compact();
            reader.Valley(w2, site2);
            sim2 = new Sim(w2, site2);
            sim2.Load(reader);
        }
        check("the source of raids, the day, the raid in the valley, the deeds and a band's side are read back from a save",
            sim2.Source is MapRaids && sim2.Clock.Day == simM.Clock.Day && sim2.Raids.Active && sim2.Raids.Count == simM.Raids.Count && Math.Abs(sim2.DeedKnowledge - .75) < 1e-9
            && sim2.Bands.Any(b => b.Edge == 2),
            $"source {sim2.Source.Key}, day {sim2.Clock.Day}, active {sim2.Raids.Active}, raids {sim2.Raids.Count}, deeds {sim2.DeedKnowledge}, sides {string.Join(",", sim2.Bands.Select(b => b.Edge))}");
    }
}
