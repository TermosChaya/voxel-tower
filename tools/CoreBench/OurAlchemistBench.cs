using System;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's alchemists of ours (--ouralchemist): the lore takes the fire pots' place among the shot (the lead shot
/// throws the pots now), the citadel takes one on once it is learnt; on our wall it throws at the rungs under the
/// raiders climbing the horde's ladder, which comes apart and drops them, and our stone is not eaten; an engine comes
/// before a lone raider; nothing is thrown where one of ours stands by the raider; with the bombardiers it throws fumes
/// into a knot of raiders, who cough and the weaker run; in the open its acid lies as acid does.
/// </summary>
static class OurAlchemistBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
        int kx = site.KeepX, kz = site.KeepZ;

        // since 25.10.2026 hired at the alchemists' laboratory, which the shot's branch opens (Sim.Town.cs), not at the citadel
        var lab = Sim.TownKindOf("lab");
        bool before = sim.Unlocked(Kinds.OurAlchemist);
        sim.Learnt.Add("alchemists");
        bool after = sim.Unlocked(Kinds.OurAlchemist);
        sim.Stock.Gold = 1000;
        string atCitadel = sim.CantEnlist(Kinds.OurAlchemist);
        check("our alchemists are hired at the alchemists' laboratory, which the shot's branch opens, and not at the citadel; its first level gives them, its second the bombardiers and the smoke",
            lab != null && Sim.LoreOf("b_lab")?.Branch == "Стрелки" && lab.Grants1.Contains("alchemists") && lab.Grants2.Contains("bombards")
            && lab.Hires.Any(h => h.kind == Kinds.OurAlchemist) && Sim.LoreOf("firepots") == null && !before && after && atCitadel != null && atCitadel.Contains("Лаборатория"),
            $"unlocked {before} → {after}; at the citadel «{atCitadel}»");
        foreach (var u in sim.Units) if (u.Team == Team.Player) Gone(u);

        // a level field west of the citadel, a stone wall four metres high on it, and the horde's ladder up its outer face
        // (the wall's face on the line between two cells of the walkable world: the ladder has a cell of its own)
        int wx = (kx - 300) & ~7, wz = kz, fg = w.SurfAt(wx, wz);
        for (int z = wz - 120; z <= wz + 120; z++)
            for (int x = wx - 220; x <= wx + 60; x++)
                for (int y = 2; y < w.SY - 1; y++)
                {
                    byte want = y < fg ? Mat.Soil : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
        for (int z = wz - 30; z < wz + 30; z++)
            for (int x = wx; x < wx + 6; x++)
                for (int y = fg; y < fg + 32; y++) w.Set(x, y, z, Mat.Stone);
        for (int z = wz - 1; z < wz + 1; z++)
            for (int y = fg; y < fg + 32; y++) w.Set(wx - 1, y, z, Mat.SiegeLadder);
        Run(.3);
        int Stone() { int n = 0; for (int z = wz - 30; z < wz + 30; z++) for (int x = wx; x < wx + 6; x++) for (int y = fg; y < fg + 32; y++) if (w.Get(x, y, z) == Mat.Stone) n++; return n; }
        int Ladder() { int n = 0; for (int z = wz - 3; z < wz + 3; z++) for (int y = fg; y < fg + 40; y++) for (int x = wx - 3; x < wx; x++) if (w.Get(x, y, z) == Mat.SiegeLadder) n++; return n; }
        // an alchemist of ours on the wall, along it from the ladder; two goblins up the ladder, held there
        var al = sim.Spawn(Kinds.OurAlchemist, sim.NearestStand(wx + 3, wz + 24, 2));
        al.Hold = true; al.Dest = al.Post = al.Cell;
        var climbers = new[] { 26, 14 }.Select(h =>
        {
            int c = sim.Nav.CellOf(wx - 1, fg + h, wz);
            var g = sim.Spawn(Kinds.Goblin, c);
            g.Cell = c; (g.X, g.Y, g.Z) = (wx - .5f, fg + h, wz + .5f);
            g.ChainedUntil = 1e9; g.Hp = g.MaxHp = 5000;
            return g;
        }).ToArray();
        bool onIt = climbers.All(sim.Climbing);
        int stone0 = Stone(), ladder0 = Ladder();
        float y0 = climbers[0].Y;
        Run(12);
        int stone1 = Stone(), ladder1 = Ladder();
        bool fell = climbers.All(g => g.Dead || !sim.Climbing(g)) && climbers[0].Y < y0 - 8;
        check("an alchemist of ours on the wall throws at the rungs under the raiders on the horde's ladder: it is eaten through and they come down, and our stone is not eaten",
            onIt && al.Y > fg + 24 && sim.OurFlasks >= 1 && sim.OurAte > 0 && ladder1 < ladder0 / 2 && fell && stone1 == stone0,
            $"on the ladder {onIt}; the alchemist at {al.Y - fg:0} up; {sim.OurFlasks} flasks, {sim.OurAte} rungs eaten; ladder {ladder0} → {ladder1}; " +
            $"the climbers {string.Join(", ", climbers.Select(g => g.Dead ? "dead" : $"{g.Y - fg:0} up, on it {sim.Climbing(g)}"))}; our stone {stone0} → {stone1}");
        foreach (var g in climbers) Gone(g);
        Run(1);

        // an engine of the horde before a raider nearer by, and nothing thrown where one of ours stands by the raider
        var ram = sim.Spawn(Kinds.Ram, sim.NearestStand(wx - 110, wz + 30, Kinds.Ram.Move.Height));
        ram.ChainedUntil = 1e9;
        var lone = sim.Spawn(Kinds.Goblin, sim.NearestStand(wx - 60, wz + 20, 2));
        lone.ChainedUntil = 1e9; lone.Hp = lone.MaxHp = 5000;
        int flasks0 = sim.OurFlasks;
        float ram0 = ram.Hp;
        Arrow first = null;
        for (int i = 0; i < 160 && first == null; i++) { Run(.05); first = sim.Arrows.FirstOrDefault(a => a.Kind == Shot.Flask && a.From == al); }
        Run(3);
        check("an engine of the horde in a throw comes before a raider nearer by: the flask goes at the ram and the acid eats into it",
            first != null && first.Target == ram && ram.Hp < ram0,
            $"first flask at {(first?.Target == null ? "nothing" : first.Target.T.Key)}; the ram {ram0:0} → {ram.Hp:0}");
        Gone(ram);
        var guard = sim.Spawn(Kinds.Spearman, sim.NearestStand((int)lone.X + 4, (int)lone.Z, 2));
        guard.Hold = true; guard.Dest = guard.Post = guard.Cell; guard.Hp = guard.MaxHp = 5000;
        Run(1);
        int flasks1 = sim.OurFlasks;
        Run(8);
        int heldBy = sim.OurFlasks - flasks1;
        Gone(guard);
        Run(8);
        check("an alchemist of ours throws nothing at a raider one of ours stands by, and throws once he is gone",
            heldBy == 0 && sim.OurFlasks > flasks1 && lone.Hp < lone.MaxHp, $"flasks with ours by it {heldBy}, after {sim.OurFlasks - flasks1}; the goblin {lone.Hp:0} of {lone.MaxHp:0}");
        Gone(lone);
        Run(1);

        // a knot of raiders out in the open: acid laid there; with the bombardiers' lore the fumes, coughing, the weaker run
        int acid0 = sim.Acids.Count;
        var knot = Enumerable.Range(0, 5).Select(i =>
        {
            var g = sim.Spawn(Kinds.Goblin, sim.NearestStand(wx - 90 + i % 3 * 6, wz - 30 + i / 3 * 6, 2));
            g.ChainedUntil = 1e9; g.Hp = g.MaxHp = 5000;
            return g;
        }).ToArray();
        for (int i = 0; i < 300 && sim.Acids.Count <= acid0; i++) Run(.05);
        Run(2);
        int laid = sim.Acids.Count - acid0;
        sim.Learnt.Add("bombards");
        int fumes0 = sim.OurFumes, burst = 0;
        sim.Fumed += (_, _, _) => burst++;
        bool coughed = false, ran = false;
        double since = -1;
        // (two in five throws into a knot are fumes: a minute of throwing, so that a run of plain ones does not fail it)
        for (int i = 0; i < 1200 && (since < 0 || sim.Time - since < 1); i++)
        {
            int was = burst;
            Run(.05);
            if (burst > was)
            {
                if (since < 0) since = sim.Time;
                foreach (var g in knot) coughed |= sim.Time - g.CoughAt < .1;
            }
        }
        foreach (var g in knot) ran |= g.Routed;
        check("into a knot of raiders on open ground the acid lies as acid does; with the bombardiers some flasks are fumes, and the raiders in them cough and the weaker run",
            laid > 0 && burst > 0 && coughed && ran,
            $"acid laid {laid}; fumes {sim.OurFumes - fumes0} thrown, {burst} broken, of {sim.OurFlasks} flasks; coughed {coughed}, ran {ran}");
        foreach (var g in knot) Gone(g);
        Gone(al);
        sim.Learnt.Remove("bombards");
    }
}
