using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The architect at work (--pose=architect): six workers, the stores full, a stone-thrower with its crew, and the
    /// architect, chosen, his card open, on one of four grounds. --look=0…4 the ground about the point levelled bare (best
    /// with --holding=outpost): 0 his plan just made, a ghost on the ground and the first stretch marked; 1 a minute and a
    /// half on, the ring rising; 2 five minutes on, the gate and the posts; 3 all of it finished, from above; 4 the same
    /// from by the gate, low. --look=5…7 the old fort a new game begins with, half in ruin; --look=8…10 a ring of stone
    /// walls the household built round the citadel by hand, its gate on the road; --look=11…13 two of its sides only, the
    /// north and the west: each his plan just made, five minutes on, and all of it finished, from above.
    /// </summary>
    void ArchitectPose()
    {
        var w = World; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.WildTarget = 0;
        foreach (var u in sim.Units) if (u.Team == Team.Wild || u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        int scene = _look < 5 ? 0 : 1 + (_look - 5) / 3, stage = _look < 5 ? _look : (_look - 5) % 3;
        int cx = Site.KeepX, cz = Site.KeepZ, g = scene == 1 ? Site.KeepBase : w.SurfAt(cx + 90, cz + 90), R = 320;
        ClearTrees(cx - R - 120, cz - R - 120, cx + R + 120, cz + R + 120);
        if (scene != 1)
        {
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
        }
        const int Hand = 120;
        if (scene == 1) StartingFort.Raise(sim);
        else if (scene == 2) sim.RaiseWalls(new[] { (cx - Hand, cz - Hand), (cx + Hand, cz - Hand), (cx + Hand, cz + Hand), (cx - Hand, cz + Hand) }, closed: true, 24, 8, gates: true);
        else if (scene == 3) sim.RaiseWalls(new[] { (cx + Hand, cz - Hand), (cx - Hand, cz - Hand), (cx - Hand, cz + Hand) }, closed: false, 24, 8);
        sim.Stock[Stockpile.Stone] = 600 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] = 300 * VoxelWorld.PerM3;
        for (int i = 0; i < 6; i++) sim.Spawn(Kinds.Worker, sim.NearestStand(cx + 24 + i * 6, cz + 30, 2));
        var engine = sim.Spawn(Kinds.Mangonel, sim.NearestStand(cx - 20, cz - 30, Kinds.Mangonel.Move.Height));
        for (int i = 0; i < Kinds.Mangonel.Crew; i++) sim.Spawn(Kinds.Crewman, sim.NearestStand(cx - 24 - i * 4, cz - 36, 2)).Escort = engine;
        var arch = sim.Spawn(Kinds.Architect, sim.NearestStand(cx + 12, cz + 16, 2));
        Advance(1);
        double run = scene == 0 ? _look switch { 0 => 6, 1 => 90, 2 => 300, _ => 1500 } : stage switch { 0 => 6, 1 => 300, _ => 1500 };
        for (double t = 0; t < run; t += 5)
        {
            Advance(5);
            if ((scene == 0 ? _look >= 3 : stage == 2) && sim.ArchPlan != null && sim.ArchDoing == "всё построено") break;
        }
        Selected.Clear(); Selected.Add(arch);
        Hud.OnSelectionChanged();
        int h = Math.Max(sim.ArchPlan?.H ?? 100, scene >= 2 ? Hand + 8 : 0);
        if (_look == 4)
        {
            // low by the gate, looking in through it
            var gate = sim.ArchGate();
            int gs = gate?.side ?? sim.ArchPlan?.GateSide ?? 1;
            var (ox, oz) = gs switch { 0 => (0, -1), 1 => (1, 0), 2 => (0, 1), _ => (-1, 0) };
            int gx = gate?.x ?? cx + ox * h, gz = gate?.z ?? cz + oz * h;
            Rig.SetAt(w.WX(gx + ox * 40), w.WY(g), w.WZ(gz + oz * 40), MathF.Atan2(-oz, -ox) - MathF.PI / 2 + .5f, .42f, .8f);
        }
        else Rig.SetAt(w.WX(cx), w.WY(g), w.WZ(cz), -.7f, .9f, 104f / (h + 90));
    }
}
