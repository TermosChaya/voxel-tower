using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The architect at work (--pose=architect, best with --holding=outpost): the ground about the point the valley holds
    /// levelled, six workers, the stores full, a stone-thrower with its crew, and the architect, chosen, his card open.
    /// --look=0 his plan just made, a ghost on the ground and the first stretch marked; 1 a minute and a half on, the
    /// ring rising; 2 five minutes on, the gate and the posts; 3 all of it finished, from above; 4 the same from by the
    /// gate, low.
    /// </summary>
    void ArchitectPose()
    {
        var w = World; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.WildTarget = 0;
        foreach (var u in sim.Units) if (u.Team == Team.Wild || u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        int cx = Site.KeepX, cz = Site.KeepZ, g = w.SurfAt(cx + 90, cz + 90), R = 320;
        ClearTrees(cx - R - 120, cz - R - 120, cx + R + 120, cz + R + 120);
        int keep = Site.HasKeep ? Site.KeepR + 48 : Holdings.Square + 2;
        for (int z = cz - R; z <= cz + R; z++)
            for (int x = cx - R; x <= cx + R; x++)
            {
                if (!w.OnLand(x, z) || Math.Abs(x - cx) <= keep && Math.Abs(z - cz) <= keep) continue;
                for (int y = Math.Max(1, g - 8); y < Math.Min(w.SY, g + 80); y++)
                {
                    byte want = y < g ? (y == g - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
                Site.HTop[z * w.SX + x] = g;
            }
        sim.Stock[Stockpile.Stone] = 600 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] = 300 * VoxelWorld.PerM3;
        for (int i = 0; i < 6; i++) sim.Spawn(Kinds.Worker, sim.NearestStand(cx + 24 + i * 6, cz + 30, 2));
        var engine = sim.Spawn(Kinds.Mangonel, sim.NearestStand(cx - 20, cz - 30, Kinds.Mangonel.Move.Height));
        for (int i = 0; i < Kinds.Mangonel.Crew; i++) sim.Spawn(Kinds.Crewman, sim.NearestStand(cx - 24 - i * 4, cz - 36, 2)).Escort = engine;
        var arch = sim.Spawn(Kinds.Architect, sim.NearestStand(cx + 12, cz + 16, 2));
        Advance(1);
        double run = _look switch { 0 => 6, 1 => 90, 2 => 300, _ => 1200 };
        for (double t = 0; t < run; t += 5)
        {
            Advance(5);
            if (_look >= 3 && sim.ArchPlan is { } p && sim.ArchDoing == "всё построено") break;
        }
        Selected.Clear(); Selected.Add(arch);
        Hud.OnSelectionChanged();
        int h = sim.ArchPlan?.H ?? 100;
        if (_look == 4)
        {
            // low by the gate, looking in through it
            var p = sim.ArchPlan;
            int gs = p?.GateSide ?? 1;
            var (ox, oz) = gs switch { 0 => (0, -1), 1 => (1, 0), 2 => (0, 1), _ => (-1, 0) };
            Rig.SetAt(w.WX(cx + ox * (h + 40)), w.WY(g), w.WZ(cz + oz * (h + 40)), MathF.Atan2(-oz, -ox) - MathF.PI / 2 + .5f, .42f, .8f);
        }
        else Rig.SetAt(w.WX(cx), w.WY(g), w.WZ(cz), -.7f, .9f, 104f / (h + 90));
    }
}
