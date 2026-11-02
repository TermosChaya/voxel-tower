using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The town's streets (--pose=townways, Sim.TownWays.cs): three houses west of the citadel, a well, six workers with
    /// nothing marked to do. --look=0 as it stands, the houses just built; 1 the same after ten minutes of the workers
    /// laying the streets themselves; 2 after twenty, with stone in the stores, the streets gravelled; 3 from above.
    /// </summary>
    void TownWaysPose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        if (sim.Alarm) sim.StandDown();
        int kx = s.KeepX, kz = s.KeepZ, look = _look;
        sim.Stock[Stockpile.Wood] += 600.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Earth] += 600.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] = look >= 2 ? 300.0 * VoxelWorld.PerM3 : 0;
        int g0 = 0;
        sim.InstantLabour = true;
        // three houses on ground evened out for them, twenty-five metres off to the west, north-west and south-west
        foreach (double deg in new[] { 180.0, 140.0, 220.0 })
        {
            int x = kx + (int)(Math.Cos(deg * Math.PI / 180) * 200), z = kz + (int)(Math.Sin(deg * Math.PI / 180) * 200), g = w.SurfAt(x, z);
            g0 = Math.Max(g0, g);
            for (int zz = z - 40; zz <= z + 40; zz++)
                for (int xx = x - 40; xx <= x + 40; xx++)
                {
                    for (int y = w.SurfAt(xx, zz), top = w.ColTop(xx, zz); y < top; y++)
                        if (Mat.IsTrunk(w.Get(xx, y, zz))) { var tree = Shapes.Tree(w, xx, y, zz); for (int t = 0; t < tree.Count; t++) w.Set(tree.X(t), tree.Y(t), tree.Z(t), Mat.Air); }
                    for (int y = g - 12; y < Math.Max(g, w.ColTop(xx, zz)) + 2; y++)
                    {
                        byte want = y < g - 1 ? Mat.Soil : y == g - 1 ? Mat.Grass : Mat.Air;
                        if (w.Get(xx, y, zz) != want) w.Set(xx, y, zz, want);
                    }
                }
            var house = Shapes.House(w, s, x - 18, z - 24, x + 18, z + 23, new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true },
                Shapes.LevelTop(w, x, z, 32), out var door, out _, out var arched);
            if (house == null) continue;
            sim.DesignateBuild(house);
            sim.Arch(arched);
            sim.PlanHouse(door, x - 18, z - 24, x + 18, z + 23);
        }
        for (int t = 0; t < 10; t++) { sim.CompleteAll(); Advance(1); }
        sim.InstantLabour = false;
        for (int i = 0; i < 6; i++) sim.Spawn(Kinds.Worker, sim.Gate);
        double minutes = look == 0 ? 0 : look == 1 ? 10 : 20;
        for (int t = 0; t < minutes * 60; t += 10) Advance(10);
        var (planned, laid) = sim.TownWaysDone();
        GD.Print($"[pose] townways: {laid} of {planned} columns of street laid, grade {sim.TownGrade()}");
        if (look == 3) Rig.SetAt(w.WX(kx - 160), w.WY(g0), w.WZ(kz), .6f, 1.25f, .35f);
        else Rig.SetAt(w.WX(kx - 120), w.WY(g0 + 8), w.WZ(kz + 40), MathF.PI * .75f, .62f, .55f);
        Hud.Refresh();
        if (_bare) Hud.Visible = false;
    }
}
