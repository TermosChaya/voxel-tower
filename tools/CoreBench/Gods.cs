using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The gods (--gods): the appearance learnt at the heart of the intervention's page and cast; the Archangel coming down
/// out of the sky, the horde about the spot running from its light; set loose on a horde with ours and our wall by, what
/// it does to each; the player in it gliding and cutting, the pillar of heaven called down far off, a flight and where it
/// comes down, the wings and the trumpet; its time over, up into heaven. The Great Toad up out of its bog, the pit full
/// of water and the ground about it mire; set on a file of ours, its hops, its tongue swallowing, its venom; its croak.
/// The two gods against each other, the one struck down gone; the colossus kept, still coming up out of the earth; how
/// long a tick takes.
/// </summary>
static class GodsBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        const float M = VoxelWorld.PerMetre;
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        // (the horde's god left with nobody of ours about goes for the citadel: what it takes off it counted, and the
        // citadel kept whole meanwhile, so the valley is not lost under the bench)
        float keepStruck = 0;
        void Run(double seconds)
        {
            for (double t = 0; t < seconds; t += .05)
            {
                sim.Step(.05f);
                if (sim.KeepHp < sim.KeepMax) { keepStruck += sim.KeepMax - sim.KeepHp; sim.KeepHp = sim.KeepMax; }
            }
        }
        // a broad field west of the citadel, level and bare
        int fx0 = (site.KeepX - 760) & ~7, fz0 = (site.KeepZ - 300) & ~7, fg = w.SurfAt(fx0 + 300, fz0 + 300);
        for (int z = fz0; z < fz0 + 600; z++)
            for (int x = fx0; x < fx0 + 600; x++)
            {
                if (x < 4 || z < 4 || x >= w.SX - 4 || z >= w.SZ - 4) continue;
                for (int y = fg - 12; y < fg + 60; y++) w.Set(x, y, z, y < fg ? (y < fg - 5 ? Mat.Rock : Mat.Soil) : Mat.Air);
                site.HTop[z * w.SX + x] = fg;
            }
        Run(.5);
        Unit Put(UnitType t, float x, float z)
        {
            int c = sim.NearestStand(Math.Clamp((int)x, 8, w.SX - 9), Math.Clamp((int)z, 8, w.SZ - 9), t.Move.Height);
            var u = sim.Spawn(t, c);
            (u.X, u.Y, u.Z) = sim.Nav.Feet(c);
            if (t.Team == Team.Horde) { u.Guard = true; u.Home = u.Cell; }
            else { u.Hold = true; u.Dest = u.Cell; u.Post = u.Cell; }
            return u;
        }
        void Gone(IEnumerable<Unit> us) { foreach (var u in us) if (!u.Dead) { u.Dead = true; u.DiedAt = -999; } }

        // ---- the learning and the casting
        var wake = Sim.LoreOf(Sim.SpellKey(SpellKind.Awaken));
        bool shut = !sim.CanLearnSpell(Sim.SpellOf(SpellKind.Awaken));
        foreach (var k in new[] { SpellKind.Quake, SpellKind.Meteor, SpellKind.Host, SpellKind.Whirlwind }) sim.Known.Add(k);
        sim.Revelations = 20;
        bool stillShut = !sim.CanLearnSpell(Sim.SpellOf(SpellKind.Awaken));
        sim.Grant(Sim.LoreOf("might"));
        bool learnt = sim.LearnSpell(SpellKind.Awaken);
        check("the appearance stands at the heart of the intervention's page, on the greatest of each circle and the strength of faith, and is learnt for revelations",
            wake != null && wake.Tier == Sim.Heart && wake.Page == 2 && wake.Needs.Length == 5 && shut && stillShut && learnt && sim.Known.Contains(SpellKind.Awaken) && sim.Revelations == 12,
            $"tier {wake?.Tier}, needs {wake?.Needs.Length}; shut at first {shut}, without might {stillShut}, learnt {learnt}, revelations left {sim.Revelations}");
        float cx = fx0 + 300, cz = fz0 + 300;
        // a few of the horde near where it is to come down
        var near = new List<Unit>();
        for (int i = 0; i < 6; i++) near.Add(Put(Kinds.Goblin, cx - 18 * M + i % 3 * 2 * M, cz + 10 * M + i / 3 * 2 * M));
        sim.Favour = 100;
        string poor = sim.Cast(SpellKind.Awaken, cx, fg, cz);
        sim.Favour = 150;
        string nearKeep = sim.Cast(SpellKind.Awaken, site.KeepX + 40, w.SurfAt(site.KeepX + 40, site.KeepZ), site.KeepZ);
        int ground0 = w.SurfAt((int)cx, (int)cz);
        string cast = sim.Cast(SpellKind.Awaken, cx, fg, cz);
        var god = sim.OurGod;
        check("cast with the store full, away from the citadel, the Archangel comes; it takes all of it and is not ready again for half an hour; not with less, nor at the citadel",
            poor != null && nearKeep != null && cast == null && god != null && god.T.Kind == DeityKind.Archangel && sim.Favour < 1 && sim.ReadyIn(SpellKind.Awaken) > 1700 && god.Act == DeityAct.Rising,
            $"with 100: «{poor}»; at the citadel: «{nearKeep}»; cast «{cast}» ({god?.T.Name}), left {sim.Favour:0}, ready in {sim.ReadyIn(SpellKind.Awaken):0} s");
        if (god == null) return;

        // ---- coming down: the horde about it runs from the light; touching down the ground shakes, no pit
        Run(1.5);
        int ran = near.Count(u => u.Routed);
        Run(Sim.GodRiseFor - 1);
        int ground1 = w.SurfAt((int)cx, (int)cz);
        check("the Archangel comes down out of the sky in ten seconds: the horde about the spot runs from the light, and as it touches down the ground is broken a little but no pit is left",
            ran >= 4 && god.Act != DeityAct.Rising && god.Up >= 1 && ground0 - ground1 < 4 * M,
            $"ran {ran} of {near.Count}; act {god.Act}, up {god.Up:0.00}; the ground under it {ground0} → {ground1}");
        Gone(near);

        // ---- set loose on the horde, ours and our wall by
        float hx = cx - 34 * M, hz = cz;
        var horde = new List<Unit>();
        for (int i = 0; i < 36; i++) horde.Add(Put(i % 3 == 0 ? Kinds.Orc : Kinds.Goblin, hx + (i % 6 - 2.5f) * 1.5f * M, hz + (i / 6 - 2.5f) * 1.5f * M));
        var ram = Put(Kinds.Ram, hx - 6 * M, hz + 6 * M);
        var ours = new List<Unit>();
        for (int i = 0; i < 6; i++) ours.Add(Put(Kinds.Swordsman, cx + 20 * M + i * 1.5f * M, cz + 12 * M));
        int wallX0 = (int)(cx - 14 * M), wallZ0 = (int)(cz - 30 * M);
        for (int z = wallZ0; z < wallZ0 + 64; z++) for (int x = wallX0; x < wallX0 + 6; x++) for (int y = fg; y < fg + 24; y++) w.Set(x, y, z, Mat.Stone);
        int Wall() { int n = 0; for (int z = wallZ0; z < wallZ0 + 64; z++) for (int x = wallX0; x < wallX0 + 6; x++) for (int y = fg; y < fg + 24; y++) if (w.Get(x, y, z) == Mat.Stone) n++; return n; }
        int wall0 = Wall();
        Run(.2);
        int broke0 = god.Broke;
        var acts = new HashSet<DeityAct>();
        double flungFast = 0;
        for (int i = 0; i < 1200; i++)
        {
            Run(.05);
            acts.Add(god.Act);
            foreach (var f in sim.Fliers) if (f.Unit is { Team: Team.Horde }) flungFast = Math.Max(flungFast, MathF.Sqrt(f.VX * f.VX + f.VZ * f.VZ) / M);
        }
        int beaten = horde.Count(u => u.Dead || u.Routed);
        check("set loose on a knot of the horde it goes at it with the sword, the pillar and the wings: most of it dead or running, the ram broken, those it strikes thrown hard",
            beaten >= horde.Count * 3 / 4 && ram.Dead && acts.Overlaps(new[] { DeityAct.Sweep, DeityAct.Slam, DeityAct.Gust }) && god.Kills >= 10 && flungFast > 12,
            $"beaten {beaten} of {horde.Count}, the ram dead {ram.Dead}, acts {string.Join(",", acts)}, kills {god.Kills}, thrown up to {flungFast:0} m/s");
        int oursUp = ours.Count(u => !u.Dead), wall1 = Wall();
        check("ours by it come to no harm, and our wall stands as it was: the household's god spares what is the household's",
            oursUp == ours.Count && wall1 == wall0, $"ours alive {oursUp} of {ours.Count}; the wall {wall0} → {wall1}");
        check("what it broke of the world flies: the ground where its blows came down went up in pieces",
            god.Broke - broke0 > 1000, $"broke {god.Broke - broke0} voxels since the horde came");
        Gone(horde); Gone(ours);

        // ---- the player in it: it glides the way asked, cuts when asked, calls the pillar down far off, flies
        Run(1);
        while (god.Act != DeityAct.Standing && god.Act != DeityAct.Walking) Run(.1);
        bool rode = sim.Ride(god);
        float x0 = god.X, z0 = god.Z;
        sim.GodLookYaw = 0; sim.GodWishX = 1; sim.GodWishZ = 0;
        Run(6);
        sim.GodWishX = 0;
        Run(1.5);
        float glided = (god.X - x0) / M, sideways = MathF.Abs(god.Z - z0) / M;
        var target = Put(Kinds.Orc, god.X + MathF.Cos(god.Facing + .6f) * god.T.Reach, god.Z + MathF.Sin(god.Facing + .6f) * god.T.Reach);
        sim.GodWishSweep = -1;
        bool swept = false; float tSpeed = 0;
        for (int i = 0; i < 60; i++)
        {
            Run(.05);
            if (target.Airborne is { } fl) { swept = true; tSpeed = MathF.Max(tSpeed, MathF.Sqrt(fl.VX * fl.VX + fl.VZ * fl.VZ) / M); }
        }
        check("the player in it glides it the way asked, and a cut asked throws one of the horde in reach of the sword far",
            rode && glided > 12 && sideways < 6 && swept && tSpeed > 15, $"in it {rode}; glided {glided:0.0} m on, {sideways:0.0} m aside; cut {swept} at {tSpeed:0} m/s");
        Run(1.5);
        // the pillar of heaven, 35 m before it
        float px = god.X + MathF.Cos(god.Facing) * 35 * M, pz = god.Z + MathF.Sin(god.Facing) * 35 * M;
        int pg0 = w.SurfAt((int)px, (int)pz), pillars = 0;
        sim.PillarFell += (dd, xx, yy, zz) => pillars++;
        sim.GodAimX = px; sim.GodAimZ = pz; sim.GodAimY = pg0; sim.GodWishSlam = true;
        Run(Sim.SlamDone + .3);
        int pg1 = w.SurfAt((int)px, (int)pz);
        check("the pillar of heaven called down 35 m before it comes down there as a pillar of light and breaks a crater",
            pillars == 1 && pg0 - pg1 >= 2 * M, $"pillars {pillars}; the ground there {pg0} → {pg1}");
        // a flight to a spot 50 m off, the horde thrown where it comes down
        float lx = god.X - 50 * M, lz = god.Z;
        var under = new List<Unit>();
        for (int i = 0; i < 6; i++) under.Add(Put(Kinds.Goblin, lx + 5 * M + i % 3 * 2 * M, lz + (i / 3 * 2 - 1) * M));
        sim.GodAimX = lx; sim.GodAimZ = lz; sim.GodAimY = w.SurfAt((int)lx, (int)lz); sim.GodWishLeap = true;
        Run(Sim.LeapCrouch + Sim.FlightFor + 1);
        float off = MathF.Sqrt((god.X - lx) * (god.X - lx) + (god.Z - lz) * (god.Z - lz)) / M;
        int thrown = under.Count(u => u.Dead || u.Airborne != null || u.Down);
        check("a flight asked takes it on its wings to the spot looked at, and where it comes down the horde is thrown",
            off < 3 && thrown >= 4, $"{off:0.0} m from the spot; thrown {thrown} of {under.Count}");
        Gone(under);
        Run(1);
        // the wings: a wedge of the horde before it blown away; the trumpet: the horde about it runs
        var wedge = new List<Unit>();
        for (int i = 0; i < 6; i++) wedge.Add(Put(Kinds.Goblin, god.X + MathF.Cos(god.Facing) * (18 + i * 3) * M, god.Z + MathF.Sin(god.Facing) * (18 + i * 3) * M + (i % 2) * 2 * M));
        sim.GodWishLift = true;
        bool blown = false;
        for (int i = 0; i < 60 && !blown; i++) { Run(.05); blown = wedge.Count(u => u.Airborne != null || u.Dead) >= 4; }
        Run(1.5);
        Gone(wedge);
        var about = new List<Unit>();
        for (int i = 0; i < 8; i++) about.Add(Put(Kinds.Orc, god.X + MathF.Cos(i) * 40 * M, god.Z + MathF.Sin(i) * 40 * M));
        sim.GodWishRoar = true;
        Run(Sim.RoarDone + .2);
        int routed = about.Count(u => u.Routed);
        check("a beat of the wings blows away the horde in the wedge before it, and the trumpet sets the horde about it running",
            blown && routed >= 5, $"blown {blown}; ran from the trumpet {routed} of {about.Count}");
        Gone(about);
        sim.Unride();

        // ---- its time over: up into heaven
        Run(1);
        god.Until = sim.Time;
        int said = 0;
        sim.Said += (s, k, x, z) => { if (s.Contains("возносится на небо")) said++; };
        // (it finishes the blow it is at first)
        Run(Sim.GodSinkFor + Sim.SweepDone + 1);
        check("its time over, it goes up into heaven and is gone, nothing broken under it, and the chronicle tells what it did",
            god.Gone && sim.OurGod == null && said == 1, $"gone {god.Gone}, told {said}");

        // ---- the Great Toad, out of its bog, set on a file of ours
        float tx = cx + 40 * M, tz = cz + 30 * M;
        int tg0 = w.SurfAt((int)tx, (int)tz);
        string toadCame = sim.Awaken(DeityKind.Toad, tx, tz, lasts: 600);
        var toad = sim.Gods.Find(g => !g.Gone && g.T.Kind == DeityKind.Toad);
        Run(Sim.GodRiseFor + .5);
        int water = 0;
        for (int dz = -64; dz <= 64; dz++) for (int dx = -64; dx <= 64; dx++) for (int y = tg0 - 48; y < tg0 + 4; y++) if (w.InBounds((int)tx + dx, y, (int)tz + dz) && w.Get((int)tx + dx, y, (int)tz + dz) == Mat.Water) water++;
        check("the Great Toad comes up out of a bog: its pit fills with water and the ground about it goes to mire",
            toadCame == null && toad != null && toad.Act != DeityAct.Rising && sim.Mires.Count >= 1 && water > 2000,
            $"came «{toadCame}», act {toad?.Act}; mires {sim.Mires.Count}, water in its pit {water}");
        if (toad == null) return;
        var file = new List<Unit>();
        for (int i = 0; i < 14; i++) file.Add(Put(i % 2 == 0 ? Kinds.Swordsman : Kinds.Archer, tx - (70 + i % 7 * 2) * M, tz + (i / 7 * 3 - 1) * M));
        float t0x = toad.X, t0z = toad.Z;
        int swallowed = 0, downs = 0; bool venom = false;
        var tacts = new HashSet<DeityAct>();
        sim.Said += (s, k, x, z) => { if (s.StartsWith("Великая жаба проглотила")) swallowed++; };
        for (int i = 0; i < 1200 && file.Any(u => !u.Dead); i++)
        {
            Run(.05);
            tacts.Add(toad.Act);
            venom |= sim.Acids.Count > 0;
            downs = Math.Max(downs, file.Count(u => !u.Dead && u.Down));
        }
        float hopped = MathF.Sqrt((toad.X - t0x) * (toad.X - t0x) + (toad.Z - t0z) * (toad.Z - t0z)) / M;
        int dead = file.Count(u => u.Dead), gone = file.Count(u => u.Dead && u.KilledBy == Sim.SwallowCause);
        check("set on a file of ours 70 m off the toad hops to it, catches them on its tongue and swallows them",
            tacts.Contains(DeityAct.Leap) && hopped > 15 && tacts.Contains(DeityAct.Tongue) && swallowed >= 1 && gone >= 1 && dead >= file.Count / 2,
            $"acts {string.Join(",", tacts)}; hopped {hopped:0} m; dead {dead} of {file.Count}, swallowed {gone} ({swallowed} times); venom lying {venom}; down at once {downs}");
        Gone(file);
        // a knot of ours just beyond its tongue, off the venom it spat before: venom at it
        while (toad.Act != DeityAct.Standing) Run(.05);
        float back = toad.Facing + MathF.PI / 2;
        toad.LastSpit = -100;
        var spatAt = new List<Unit>();
        for (int i = 0; i < 6; i++) spatAt.Add(Put(Kinds.Swordsman, toad.X + MathF.Cos(back) * 52 * M + (i % 3 - 1) * 1.5f * M, toad.Z + MathF.Sin(back) * 52 * M + (i / 3) * 1.5f * M));
        float hp0s = spatAt.Sum(u => u.Hp), sx = spatAt.Average(u => u.X), sz = spatAt.Average(u => u.Z), splashOff = -1;
        sim.VenomSplashed += (x, y, z) => splashOff = MathF.Sqrt((x - sx) * (x - sx) + (z - sz) * (z - sz)) / M;
        bool spat = false;
        for (int i = 0; i < 100 && !spat; i++) { Run(.05); spat = toad.Act == DeityAct.Spit; }
        Run(Sim.SpitDone + 2);
        float burnt = hp0s - spatAt.Sum(u => u.Dead ? 0 : u.Hp);
        check("a knot of ours just beyond its tongue it spits venom at: those there burnt, and the venom lies there eating in",
            spat && burnt > 100 && sim.Acids.Count > 0, $"spat {spat}, came down {splashOff:0.0} m off them; burnt {burnt:0} hp of {hp0s:0}; acid lying in {sim.Acids.Count} voxels; died of {string.Join(",", spatAt.Where(u => u.Dead).Select(u => u.KilledBy).Distinct())}");
        Gone(spatAt);
        // a knot of ours close by it, off the venom: it croaks at it
        Run(2);
        float aside = toad.Facing + MathF.PI / 2;
        var croaked = new List<Unit>();
        for (int i = 0; i < 10; i++) croaked.Add(Put(Kinds.Swordsman, toad.X + MathF.Cos(aside) * 22 * M + (i % 4 - 1.5f) * 1.5f * M, toad.Z + MathF.Sin(aside) * 22 * M + (i / 4 - 1) * 1.5f * M));
        toad.LastRoar = -100;
        int croaks = 0, knocked = 0;
        sim.Said += (s, k, x, z) => { if (s.StartsWith("Великая жаба квакнула")) croaks++; };
        for (int i = 0; i < 600 && croaks == 0; i++) Run(.05);
        Run(.3);
        knocked = croaked.Count(u => !u.Dead && u.Down);
        check("a knot of ours close by, it croaks at it, and the croak knocks them off their feet",
            croaks >= 1 && knocked >= 5, $"croaked {croaks} times; down {knocked} of {croaked.Count} ({croaked.Count(u => u.Dead)} dead)");
        Gone(croaked);

        // ---- the two gods against each other
        string angelCame = sim.Awaken(DeityKind.Archangel, cx, cz - 25 * M, lasts: 600);
        var angel = sim.OurGod;
        Run(Sim.GodRiseFor + .5);
        double hpA = angel?.Hp ?? 0, hpT = toad.Hp, fight = 0;
        int byAngel = 0, byToad = 0;
        sim.Said += (s, k, x, z) => { if (s.StartsWith("Архангел бьёт")) byAngel++; else if (s.StartsWith("Великая жаба бьёт")) byToad++; };
        for (int i = 0; i < 3600 && angel != null && !toad.Gone && !angel.Gone; i++)
        {
            Run(.05); fight += .05;
            if (toad.Act == DeityAct.Sinking || angel.Act == DeityAct.Sinking) { Run(Sim.GodSinkFor + .5); break; }
        }
        bool fell = toad.Gone && toad.Hp <= 0 || angel != null && angel.Gone && angel.Hp <= 0;
        check("the horde's god against ours: they go at each other, both are struck, and the one struck down goes from the world",
            angelCame == null && angel != null && fell && angel.Hp < hpA && toad.Hp < hpT,
            $"came «{angelCame}»; blows {byAngel} by the Archangel, {byToad} by the Toad; after {fight:0} s the Toad {hpT:0} → {toad.Hp:0} hp gone {toad.Gone}, the Archangel {hpA:0} → {angel?.Hp:0} gone {angel?.Gone}");
        foreach (var g in sim.Gods) if (!g.Gone) g.Until = sim.Time;
        Run(Sim.GodSinkFor + Sim.SweepDone + 1);

        // ---- the colossus, kept for a side to come: it still breaks up out of the earth
        float kx = cx + 10 * M, kz = cz - 50 * M;
        int cg0 = w.SurfAt((int)kx, (int)kz);
        string colossus = sim.Awaken(DeityKind.Colossus, kx, kz, lasts: 30);
        Run(Sim.GodRiseFor + .5);
        int cg1 = w.SurfAt((int)kx, (int)kz);
        var col = sim.Gods.Find(g => !g.Gone && g.T.Kind == DeityKind.Colossus);
        check("the colossus, no side's now, still breaks up out of the earth and leaves its pit",
            colossus == null && col != null && col.Up >= 1 && cg0 - cg1 >= 4 * M, $"came «{colossus}», up {col?.Up:0.00}, the ground {cg0} → {cg1}");
        if (col != null) { col.Until = sim.Time; Run(Sim.GodSinkFor + Sim.SweepDone + 1); }

        // ---- a tick with a god at its work
        sim.Favour = 150; sim.ReadyNow(SpellKind.Awaken);
        sim.Cast(SpellKind.Awaken, cx + 30 * M, fg, cz - 30 * M);
        var g2 = sim.OurGod;
        Run(Sim.GodRiseFor + .5);
        for (int i = 0; i < 30; i++) Put(Kinds.Goblin, cx - 10 * M + i % 6 * 2 * M, cz - 50 * M + i / 6 * 2 * M);
        double worst = 0, sum = 0; int n = 0;
        for (int i = 0; i < 300; i++)
        {
            double a = BenchClock.CpuMs();
            sim.Step(.05f);
            double ms = BenchClock.CpuMs() - a;
            worst = Math.Max(worst, ms); sum += ms; n++;
        }
        check("a tick with the god at its work among the horde takes little: under 12 ms on the average",
            g2 != null && sum / n < 12, $"average {sum / n:0.0} ms, worst {worst:0.0} ms");
    }
}
