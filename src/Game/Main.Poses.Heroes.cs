using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// --pose=heroes: a commander of level 4 west of the citadel with his men, Строй II and Стрелки I learnt, just risen to
    /// level 5 with a skill to choose. --look=0 his card open with the choice; 1 the field as it is then, his face in the
    /// row with its level and star, the medallion up and the advice; 2 a band of goblins come on his line, the fight a
    /// moment in.
    /// </summary>
    void HeroesPose()
    {
        var w = World; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = Site.KeepX, kz = Site.KeepZ, x0 = kx - Site.KeepR - 140;
        ClearTrees(x0 - 160, kz - 90, x0 + 80, kz + 90);
        foreach (var u in sim.Units) if (u.Team == Team.Player && u.T.Work < 1) { u.Dead = true; u.DiedAt = -999; }
        Unit Stand(UnitType t, int x, int z)
        {
            int c = sim.NearestStand(x, z, t.Move.Height);
            if (c < 0) return null;
            var u = sim.Spawn(t, c);
            sim.Command(new[] { u }, c);
            u.Facing = MathF.PI;
            return u;
        }
        var cap = Stand(Kinds.Captain, x0 + 24, kz);
        foreach (var (t, dz) in new[] { (Kinds.Shieldbearer, -24), (Kinds.Swordsman, -12), (Kinds.Shieldbearer, 0), (Kinds.Spearman, 12), (Kinds.Shieldbearer, 24) }) Stand(t, x0, kz + dz);
        foreach (var dz in new[] { -16, 0, 16 }) Stand(Kinds.Archer, x0 + 40, kz + dz);
        var h = cap.Hero;
        h.Level = 4; h.Xp = Heroes.XpFor(4) + 180; h.Kills = 7;
        h.Grade[(int)Skill.Line] = 2; h.Grade[(int)Skill.Archers] = 1;
        Advance(1);
        Rig.SetAt(w.WX(x0 + 10), w.WY(w.SurfAt(x0 + 10, kz)), w.WZ(kz), -.6f, .55f, 1.3f);
        if (_look == 2)
        {
            var roster = new List<UnitType>();
            for (int i = 0; i < 9; i++) roster.Add(i % 4 == 3 ? Kinds.Orc : Kinds.Goblin);
            sim.Raid(roster, Sim.WestEdge, .6);
            foreach (var b in sim.Bands) b.Entry = sim.NearestStand(x0 - 140, kz, 4);
            sim.ComputeFields();
            for (int t = 0; t < 600 && !sim.Units.Exists(u => !u.Dead && u.Team == Team.Horde && Sim.Dist(u, cap) < 14 * VoxelWorld.PerMetre); t++) Advance(.1);
            Advance(3);
            sim.Glory(cap, Heroes.XpFor(5) - h.Xp);
            Advance(.5);
            Rig.SetAt(w.WX(x0 - 20), w.WY(w.SurfAt(x0 - 20, kz)), w.WZ(kz), -.6f, .55f, 1.3f);
            return;
        }
        sim.Glory(cap, Heroes.XpFor(5) - h.Xp);
        if (_look == 0) Hud.HeroPane.Show(cap);
    }
}
