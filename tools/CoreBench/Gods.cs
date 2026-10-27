using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The gods (--gods): the waking learnt at the heart of the intervention's page and cast; the Colossus breaking up out
/// of the ground — the ring heaving, the earth going up in slabs, the chains, the pit left; set loose on a horde with
/// ours and our wall by, what it does to each; its prints; the player in it walking and sweeping; the horde's god
/// against it and the one struck down falling to pieces; its time over and back under; how long a tick takes.
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
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        float Metres(float a, float b, float c, float d) => MathF.Sqrt((a - c) * (a - c) + (b - d) * (b - d)) / M;
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

        // ---- the learning and the casting
        var wake = Sim.LoreOf(Sim.SpellKey(SpellKind.Awaken));
        bool shut = !sim.CanLearnSpell(Sim.SpellOf(SpellKind.Awaken));
        foreach (var k in new[] { SpellKind.Quake, SpellKind.Meteor, SpellKind.Whirlwind }) sim.Known.Add(k);
        sim.Revelations = 20;
        bool stillShut = !sim.CanLearnSpell(Sim.SpellOf(SpellKind.Awaken));
        sim.Grant(Sim.LoreOf("reap")); sim.Grant(Sim.LoreOf("might"));
        bool learnt = sim.LearnSpell(SpellKind.Awaken);
        check("the waking stands at the heart of the intervention's page, on the greatest of each circle and the strength of faith, and is learnt for revelations",
            wake != null && wake.Tier == Sim.Heart && wake.Page == 2 && wake.Needs.Length == 5 && shut && stillShut && learnt && sim.Known.Contains(SpellKind.Awaken) && sim.Revelations == 12,
            $"tier {wake?.Tier}, needs {wake?.Needs.Length}; shut at first {shut}, without reap and might {stillShut}, learnt {learnt}, revelations left {sim.Revelations}");
        float cx = fx0 + 300, cz = fz0 + 300;
        sim.Favour = 100;
        string poor = sim.Cast(SpellKind.Awaken, cx, fg, cz);
        sim.Favour = 150;
        string nearKeep = sim.Cast(SpellKind.Awaken, site.KeepX + 40, w.SurfAt(site.KeepX + 40, site.KeepZ), site.KeepZ);
        int pitTop0 = w.SurfAt((int)cx, (int)cz);
        string cast = sim.Cast(SpellKind.Awaken, cx, fg, cz);
        var god = sim.OurGod;
        check("cast with the store full, away from the citadel, it takes all of it and is not ready again for half an hour; not with less, nor at the citadel",
            poor != null && nearKeep != null && cast == null && god != null && sim.Favour < 1 && sim.ReadyIn(SpellKind.Awaken) > 1700 && god.Act == DeityAct.Rising,
            $"with 100: «{poor}»; at the citadel: «{nearKeep}»; cast «{cast}», left {sim.Favour:0}, ready in {sim.ReadyIn(SpellKind.Awaken):0} s");
        if (god == null) return;

        // ---- the rising: the ground goes up in slabs, the chains after, a pit left
        Run(.6);
        int aloft = sim.Aloft, pieces = sim.Fliers.Count(f => f.Unit == null);
        Run(7.2);
        int chains = sim.Fliers.Count(f => f.Unit == null && f.Voxels.Count > 40 && f.Voxels.All(v => v.m == Mat.Iron));
        Run(2.6);
        int pitTop1 = w.SurfAt((int)cx, (int)cz);
        check("the Colossus breaks up out of the ground in ten seconds: the earth over it goes up in slabs, the chains that held it come up after, and a pit is left",
            aloft > 3000 && pieces >= 10 && chains >= 4 && god.Act == DeityAct.Standing && god.Up >= 1 && pitTop0 - pitTop1 >= 4 * M,
            $"aloft {aloft} in {pieces} pieces at 0,6 s; chains {chains}; act {god.Act}, up {god.Up:0.00}; the ground at its middle {pitTop0} → {pitTop1}");

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
        check("set loose on a knot of the horde it walks to it and sweeps it and brings its fist down on it: most of it dead or running, the ram broken, those it sweeps thrown hard",
            beaten >= horde.Count * 3 / 4 && ram.Dead && acts.Contains(DeityAct.Walking) && (acts.Contains(DeityAct.Sweep) || acts.Contains(DeityAct.Slam)) && god.Kills >= 10 && flungFast > 12,
            $"beaten {beaten} of {horde.Count}, the ram dead {ram.Dead}, acts {string.Join(",", acts)}, kills {god.Kills}, thrown up to {flungFast:0} m/s");
        int oursUp = ours.Count(u => !u.Dead);
        int wall1 = Wall();
        check("ours by it come to no harm, and our wall stands as it was: the household's god spares what is the household's",
            oursUp == ours.Count && wall1 == wall0, $"ours alive {oursUp} of {ours.Count}; the wall {wall0} → {wall1}");
        check("what it broke of the world flies: the ground of the pit and the fist's craters went up in pieces",
            god.Broke - broke0 > 2000, $"broke {god.Broke - broke0} voxels since the horde came");

        // ---- the player in it: it walks where it is told and sweeps when asked
        foreach (var u in horde) if (!u.Dead) { u.Dead = true; u.DiedAt = -999; }
        Run(1);
        while (god.Act != DeityAct.Standing && god.Act != DeityAct.Walking) Run(.1);
        bool rode = sim.Ride(god);
        float x0 = god.X, z0 = god.Z;
        sim.GodLookYaw = 0; sim.GodWishX = 1; sim.GodWishZ = 0;
        Run(8);
        sim.GodWishX = 0;
        Run(2.5);
        float walked = (god.X - x0) / M, sideways = MathF.Abs(god.Z - z0) / M;
        var target = Put(Kinds.Orc, god.X + MathF.Cos(god.Facing + .6f) * god.T.Reach, god.Z + MathF.Sin(god.Facing + .6f) * god.T.Reach);
        sim.GodWishSweep = -1;
        bool swept = false; float tSpeed = 0;
        for (int i = 0; i < 60; i++)
        {
            Run(.05);
            if (target.Airborne is { } fl) { swept = true; tSpeed = MathF.Max(tSpeed, MathF.Sqrt(fl.VX * fl.VX + fl.VZ * fl.VZ) / M); }
        }
        check("the player in it walks it the way asked, a stride at a time, and a sweep asked throws one of the horde in its reach far",
            rode && walked > 12 && sideways < 6 && swept && tSpeed > 15, $"in it {rode}; walked {walked:0.0} m on, {sideways:0.0} m aside; swept {swept} at {tSpeed:0} m/s");
        // its prints
        int printed = 0;
        foreach (var fx in new[] { god.FootX[0], god.FootX[1] })
            foreach (var fz in new[] { god.FootZ[0], god.FootZ[1] })
                if (w.SurfAt((int)fx, (int)fz) < fg) printed++;
        check("where its feet came down the ground is pressed down", printed >= 2, $"prints under its feet {printed} of 4");
        sim.Unride();

        // ---- the horde's god against it: struck, it falls to pieces
        string came = sim.Awaken(DeityKind.Devourer, god.X - 40 * M, god.Z, lasts: 600);
        var dev = sim.Gods.Find(g => g.T.Kind == DeityKind.Devourer);
        double hp0 = god.Hp;
        god.Until = sim.Time + 600;
        double fight = 0;
        for (int i = 0; i < 3600 && dev != null && !dev.Gone && !god.Gone; i++) { Run(.05); fight += .05; }
        bool devFell = dev != null && dev.Gone && dev.Hp <= 0;
        // (whichever of the two fell: its pieces of rock in the air, or come down already)
        bool rocks = sim.Fliers.Count(f => f.Cause is Sim.DevourerStoneCause or Sim.ColossusStoneCause) >= 6 || devFell || god.Gone && god.Hp <= 0;
        check("the horde's god against ours: they go at each other, ours is struck too, and the one struck down falls to pieces of rock",
            came == null && dev != null && god.Hp < hp0 && (devFell || god.Gone) && rocks,
            $"came «{came}»; after {fight:0} s the Devourer {dev?.Hp:0} hp gone {dev?.Gone}, the Colossus {hp0:0} → {god.Hp:0} gone {god.Gone}");

        // ---- its time over: back under
        if (!god.Gone)
        {
            god.Until = sim.Time;
            int said = 0;
            sim.Said += (s, k, x, z) => { if (s.Contains("уходит под землю")) said++; };
            // (it finishes the blow it is at first)
            Run(Sim.GodSinkFor + Sim.SweepDone + 1);
            check("its time over, it goes back under the ground and is gone, and the chronicle tells what it did",
                god.Gone && sim.OurGod == null && said == 1, $"gone {god.Gone}, told {said}");
        }

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
        check("a tick with the god walking and striking among the horde takes little: under 12 ms on the average",
            g2 != null && sum / n < 12, $"average {sum / n:0.0} ms, worst {worst:0.0} ms");
    }
}
