using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The horde's faith (--hordefaith): an orc at a fresh body of the horde eats it, mended and the fiercer for it, and the
/// faith fed; one of ours eaten feeds it thrice; an orc with one of ours at hand fights rather than eats; a shaman throws
/// the swamp's poison over a knot of ours, and not near a sanctuary; a goblin with a shaman by puts its ladder up as ivy,
/// quick and with no timber, where alone it has nothing to build with; fire takes ivy as it takes the horde's ladders;
/// the faith full in a raid calls the Great Toad and is spent; the third chapter's champion is a shaman of the swamps.
/// </summary>
static class HordeFaithBench
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
        Unit Put(UnitType t, float x, float z, bool hold = true)
        {
            int c = sim.NearestStand(Math.Clamp((int)x, 8, w.SX - 9), Math.Clamp((int)z, 8, w.SZ - 9), t.Move.Height);
            var u = sim.Spawn(t, c);
            (u.X, u.Y, u.Z) = sim.Nav.Feet(c);
            if (!hold) return u;
            if (t.Team == Team.Horde) { u.Guard = true; u.Home = u.Cell; }
            else { u.Hold = true; u.Dest = u.Cell; u.Post = u.Cell; }
            return u;
        }
        void Gone(params Unit[] us) { foreach (var u in us) if (!u.Dead) { u.Dead = true; u.DiedAt = -999; } else u.DiedAt = -999; }
        float cx = fx0 + 300, cz = fz0 + 300;

        // ---- an orc at a fresh body of the horde eats it
        var body = Put(Kinds.Goblin, cx, cz);
        sim.Hurt(body, null, 9999, "test");
        var orc = Put(Kinds.Orc, cx + 3 * M, cz);
        orc.Hp = orc.MaxHp * .5f;
        double f0 = sim.HordeFaith;
        bool ate = false;
        for (int i = 0; i < 200 && !ate; i++) { Run(.05); ate = orc.State == UnitState.Work && orc.Meal == body; }
        Run(5);
        check("an orc with a fresh body of the horde by it goes to it and eats it: nothing left of it, the orc mended and the fiercer a minute, the horde's faith fed by one",
            ate && body.DiedAt == -999 && orc.Hp >= orc.MaxHp * .85f && orc.GorgedUntil > sim.Time && Math.Abs(sim.HordeFaith - f0 - 1) < .01,
            $"at it {ate}; the body {(body.DiedAt == -999 ? "gone" : "there")}; the orc {orc.Hp:0} of {orc.MaxHp:0}, gorged {orc.GorgedUntil > sim.Time}; faith {f0} → {sim.HordeFaith}");

        // ---- one of ours eaten feeds it thrice
        var ours = Put(Kinds.Swordsman, cx + 20 * M, cz);
        sim.Hurt(ours, null, 9999, "test");
        var troll = Put(Kinds.Troll, cx + 23 * M, cz);
        troll.Hp = troll.MaxHp * .6f;
        double f1 = sim.HordeFaith;
        Run(10);
        check("a troll eats one of ours fallen, and that feeds the horde's faith by three",
            ours.DiedAt == -999 && Math.Abs(sim.HordeFaith - f1 - 3) < .01, $"the body {(ours.DiedAt == -999 ? "gone" : "there")}; faith {f1} → {sim.HordeFaith}");
        Gone(orc, troll);

        // ---- with one of ours at hand, it fights rather than eats
        var body2 = Put(Kinds.Goblin, cx, cz + 20 * M);
        sim.Hurt(body2, null, 9999, "test");
        var orc2 = Put(Kinds.Orc, cx + 2 * M, cz + 20 * M);
        orc2.Hp = orc2.MaxHp * .5f;
        var foe = Put(Kinds.Swordsman, cx + 3.2f * M, cz + 20 * M);
        foe.Hp = foe.MaxHp = 5000;
        bool fought = false, fed = false;
        for (int i = 0; i < 80; i++) { Run(.05); fought |= orc2.State == UnitState.Fight; fed |= orc2.Meal != null && orc2.State == UnitState.Work; }
        check("an orc with one of ours at hand fights him, and does not stop to eat", fought && !fed && body2.DiedAt != -999, $"fought {fought}, ate {fed}");
        Gone(orc2, foe, body2);

        // ---- the swamp's poison over a knot of ours, not near a sanctuary
        var knot = new List<Unit>();
        for (int i = 0; i < 4; i++) knot.Add(Put(Kinds.Swordsman, cx - 30 * M + i % 2 * 1.5f * M, cz + i / 2 * 1.5f * M));
        var shaman = Put(Kinds.Shaman, cx - 30 * M - 10 * M, cz);
        // (kept on its feet a while: the swordsmen go for it)
        shaman.Hp = shaman.MaxHp = 5000;
        double poisoned = 0;
        sim.Hurting += (u, d, c) => { if (c == Sim.PoisonCause) poisoned += d; };
        for (int i = 0; i < 300 && sim.Miasmas.Count == 0; i++) Run(.05);
        Run(4);
        int clouds = sim.Miasmas.Count;
        check("a shaman throws the swamp's poison over a knot of ours, and it eats at them",
            clouds >= 1 && poisoned > 10, $"clouds {clouds}, harm {poisoned:0}; the shaman {(shaman.Dead ? "dead" : shaman.Routed ? "running" : shaman.State.ToString())}, next poison at {shaman.NextPoison:0} (now {sim.Time:0}), {Sim.Dist(shaman, knot[0]) / M:0.0} m off");
        Gone(shaman); Gone(knot.ToArray());
        Run(13);
        var knot2 = new List<Unit>();
        for (int i = 0; i < 4; i++) knot2.Add(Put(Kinds.Swordsman, cx - 30 * M + i % 2 * 1.5f * M, cz + 40 * M + i / 2 * 1.5f * M));
        sim.AddZone((int)(cx - 34 * M), (int)(cz + 36 * M), (int)(cx - 26 * M), (int)(cz + 44 * M), Zone.Sanctum, 1);
        var shaman2 = Put(Kinds.Shaman, cx - 40 * M, cz + 40 * M);
        Run(25);
        check("near a sanctuary of ours the poison does not take", sim.Miasmas.Count == 0, $"clouds {sim.Miasmas.Count}");
        Gone(shaman2); Gone(knot2.ToArray());

        // ---- ivy: against a ring of stone about the citadel, a goblin with no timber and a shaman by
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        sim.Step(.05f);
        sim.InstantLabour = true;
        sim.DesignateBuild(Shapes.Circle(w, site, site.KeepX, site.KeepZ, site.KeepR + 44, new Stroke { Mat = Mat.Stone, Width = 8, Height = 48, Hollow = true }, 0));
        sim.InstantLabour = false;
        sim.HordeStock[Stockpile.Wood] = 4096; sim.HordeStock[Stockpile.Earth] = 0;
        sim.ComputeFields();
        int ivy = 0, timber = 0;
        (int x, int y, int z) ivyAt = (-1, 0, 0);
        sim.Raised = (x, y, z, m) => { if (m == Mat.Ivy) { ivy++; ivyAt = (x, y, z); } else if (m == Mat.SiegeLadder) timber++; };
        var gob = Put(Kinds.Sapper, site.KeepX - site.KeepR - 80, site.KeepZ, hold: false);
        var witchDoctor = Put(Kinds.Shaman, site.KeepX - site.KeepR - 84, site.KeepZ + 4, hold: false);
        witchDoctor.ChainedUntil = 1e9;
        double wood0 = sim.HordeStock[Stockpile.Wood];
        for (int i = 0; i < 1200 && ivy < 40; i++)
        {
            Run(.05);
            // (the shaman kept by the goblin)
            if (i % 20 == 0 && !gob.Dead) { (witchDoctor.X, witchDoctor.Z) = (gob.X - 2 * M, gob.Z); }
        }
        check("a sapper with a shaman by puts its ladder up our wall as ivy, of itself: none of the horde's timber goes on it",
            ivy >= 30 && timber == 0 && sim.HordeStock[Stockpile.Wood] == wood0, $"ivy {ivy} voxels, timber ladder {timber}, timber {wood0:0} → {sim.HordeStock[Stockpile.Wood]:0}; the sapper {gob.State}");
        // fire takes it as it takes the horde's ladders
        Gone(gob, witchDoctor);
        bool there = ivyAt.x >= 0 && w.Get(ivyAt.x, ivyAt.y, ivyAt.z) == Mat.Ivy, burnt = false;
        if (there)
        {
            sim.Ignite(ivyAt.x, ivyAt.y, ivyAt.z);
            for (int i = 0; i < 600 && !burnt; i++) { Run(.1); burnt = w.Get(ivyAt.x, ivyAt.y, ivyAt.z) != Mat.Ivy; }
        }
        check("fire takes the ivy as it takes the horde's ladders", there && burnt, $"there {there}, burnt {burnt}");

        // ---- the faith full, in a raid: the Great Toad called, the faith spent
        sim.HordeFaith = Sim.HordeFaithFull;
        sim.Waves.StartNow(sim);
        Run(1);
        var toad = sim.Gods.Find(g => !g.Gone && g.T == Deities.Toad);
        check("the horde's faith full, in a raid the shamans call the Great Toad on the horde's side of the valley, and the faith is spent",
            toad != null && sim.HordeFaith < 1 && toad.X < site.KeepX - site.KeepR, $"the toad {(toad == null ? "not called" : $"at {(site.KeepX - toad.X) / M:0} m west of the citadel")}; faith {sim.HordeFaith}");
        sim.Waves.CallOff(sim);

        check("the third chapter's champion is a shaman of the swamps, not a necromancer", Sim.Champion(3).kind == Kinds.Shaman, $"{Sim.Champion(3).title}: {Sim.Champion(3).kind.Name}");
    }
}
