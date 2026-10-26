using System;
using System.Collections.Generic;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// A warlord's army at the walls (--pose=warlord, --at=seconds into the fight, 14 by itself): a ring of stone wall
    /// round the citadel, a commander with a line of spearmen and swordsmen before it and archers on the wall, and an
    /// army of the horde's realm come in from the west by day, a warlord of the third level at its head with his guard.
    /// --look=1 close on the warlord; --look=2 the warlord killed, his army run and gone, the raid's report up;
    /// --look=3 a raid two hundred strong on the walls, for how fast it is drawn (--perf).
    /// </summary>
    void WarlordPose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX, kz = s.KeepZ, line = kx - s.KeepR - 70;
        sim.InstantLabour = true;
        int top = Shapes.LevelTop(w, kx - 64, kz, 40);
        sim.DesignateBuild(Shapes.Circle(w, s, kx, kz, s.KeepR + 44, new Stroke { Mat = Mat.Stone, Width = 10, Height = 40, Level = true, Hollow = true }, top));
        sim.InstantLabour = false;
        Unit Stand(UnitType t, int x, int z)
        {
            int c = sim.NearestStand(x, z, t.Move.Height);
            if (c < 0) return null;
            var u = sim.Spawn(t, c);
            sim.Command(new[] { u }, c);
            u.Facing = MathF.PI;
            return u;
        }
        var foot = new[] { Kinds.Spearman, Kinds.Swordsman, Kinds.Shieldbearer, Kinds.Spearman, Kinds.Knight, Kinds.Swordsman, Kinds.Spearman };
        for (int i = 0; i < foot.Length; i++) Stand(foot[i], line, kz - 30 + i * 10);
        // the archers on the wall's western arc
        foreach (var (dx, dz) in new[] { (-62, -14), (-60, 18), (-52, -36), (-48, 40), (-63, 2) }) Stand(Kinds.Archer, kx + dx, kz + dz);
        var cap = Stand(Kinds.Captain, line + 26, kz);
        if (cap != null) sim.Command(new[] { cap }, cap.Cell);
        Advance(1);
        // the army: a warlord with his guard and two bands, come in a little way off the line rather than at the
        // valley's far edge, so the fight is soon
        var lord = sim.NewWarlord(3);
        bool host = _look == 3;
        var roster = new List<UnitType>();
        void Add(UnitType t, int n) { for (int i = 0; i < n; i++) roster.Add(t); }
        if (host) { Add(Kinds.Goblin, 110); Add(Kinds.Orc, 50); Add(Kinds.Sapper, 16); Add(Kinds.Troll, 10); Add(Kinds.Ram, 4); }
        else { Add(Kinds.Orc, 5); Add(Kinds.Goblin, 8); Add(Kinds.Sapper, 2); Add(Kinds.Troll, 1); }
        sim.HordeStock[Stockpile.Wood] = 24 * VoxelWorld.PerM3;
        sim.Raid(roster, Sim.WestEdge, .6, lord);
        int k = 0;
        foreach (var b in sim.Bands)
        {
            if (b.Lord != lord) continue;
            int dz = b.LordsOwn ? 0 : (k % 2 == 0 ? -1 : 1) * (36 + k / 2 * 30);
            b.Entry = sim.NearestStand(line - (host ? 150 : 110) - (b.LordsOwn ? 20 : 0), kz + dz, 4);
            if (!b.LordsOwn) k++;
        }
        sim.ComputeFields();
        Advance(_poseSeconds);
        Unit at = null;
        foreach (var u in sim.Units) if (u.Lord == lord && !u.Dead) at = u;
        if (_look == 2)
        {
            if (at != null) sim.Hurt(at, null, 1e6f, "пробит насквозь");
            for (int i = 0; i < 300 && sim.Raids.Active; i++) Advance(1);
        }
        if (_look == 1 && at != null) Rig.Set(World.WX(at.X + 6), World.WZ(at.Z + 4), -.75f, .5f, 3.4f, snap: true);
        else if (at != null && _look != 2) Rig.Set(World.WX((at.X + line) / 2), World.WZ((at.Z + kz) / 2), -.75f, .55f, host ? 1.2f : 1.7f, snap: true);
        else Rig.Set(World.WX(line - 34), World.WZ(kz + 6), -.75f, .55f, host ? 1.2f : 1.6f, snap: true);
        Hud.Refresh();
        Marks.Flush();
    }
}
