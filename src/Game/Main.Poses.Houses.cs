using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// A house built by the household as the player draws one (--pose=house): a stone house of four and a half metres by
    /// seven east of the citadel, drawn with the rectangle's house form, built, its door cut and hung and its bunks set by
    /// six workers. --look=0 (by itself) it stands done, seen from outside; 1 half its roof lifted off to show the floor
    /// and the bunks; 2 it is drawn over again and a second storey put up on it at once; 3 that one with half its roof
    /// lifted, the upper floor, its bunks and the hatch over the ladder; 4 being built (--at= seconds, 25 by itself).
    /// </summary>
    void HousePose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX, kz = s.KeepZ;
        // the nearest level dry ground east of the citadel, clear of the fort
        int hx = kx + 150, hz = kz + 160;
        bool found = false;
        for (int r = 110; r < 400 && !found; r += 12)
            for (int a = 0; a < 24 && !found; a++)
            {
                int cx = kx + (int)(r * Math.Cos(a * Math.PI / 12)), cz = kz + (int)(r * Math.Sin(a * Math.PI / 12));
                int lo = int.MaxValue, hi = int.MinValue; bool bad = false;
                for (int z = cz - 40; z <= cz + 40 && !bad; z += 4)
                    for (int x = cx - 30; x <= cx + 30 && !bad; x += 4)
                    {
                        if (x < 0 || z < 0 || x >= w.SX || z >= w.SZ || s.Forbidden(x, z)) { bad = true; break; }
                        int g = w.SurfAt(x, z); lo = Math.Min(lo, g); hi = Math.Max(hi, g);
                        if (w.Get(x, g, z) == Mat.Water || w.Get(x, g - 1, z) == Mat.Water || Mat.IsPlayer[w.Get(x, g - 1, z)]) bad = true;
                    }
                if (!bad && hi - lo <= 2) { hx = cx; hz = cz; found = true; }
            }
        ClearTrees(hx - 50, hz - 60, hx + 50, hz + 60);
        sim.Stock[Stockpile.Wood] += 300.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 300.0 * VoxelWorld.PerM3;
        for (int i = 0; i < 6; i++) sim.Spawn(Kinds.Worker, sim.NearestStand(hx - 40 + i * 6, hz - 40, 4));
        int x0 = hx - 18, z0 = hz - 27, x1 = hx + 18, z1 = hz + 27;
        var st = new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true };
        var plan = Shapes.House(w, s, x0, z0, x1, z1, st, Shapes.LevelTop(w, hx, hz, 32), out var door, out _, out var arched);
        if (plan == null) return;
        sim.DesignateBuild(plan);
        sim.Arch(arched);
        sim.PlanHouse(door, x0, z0, x1, z1);
        var home = sim.Zones.Find(z => z.Kind == Zone.Dwelling && z.X0 == x0 && z.Z0 == z0);
        int look = Math.Max(0, _look);
        void Done(double most)
        {
            for (double t = 0; t < most && (sim.Orders.Total > 0 || sim.Storeys.Count > 0 || home is { Closed: false }); t += 1) Advance(1);
            Advance(6);
            sim.LookOver(home);
        }
        if (look == 4) Advance(_poseSeconds == 14 ? 25 : _poseSeconds);
        else Done(400);
        int top = door.Top;
        if (look is 2 or 3)
        {
            // the storey put up at once, as by many hands (the bench and the self-test have the household do it)
            sim.PlanStorey(home, x0, z0, x1, z1, Mat.Stone);
            sim.InstantLabour = true;
            sim.CompleteAll();
            Advance(3);
            sim.LookOver(home);
            sim.InstantLabour = false;
            Done(120);
            top = home.Depth;
        }
        GD.Print($"[pose] house at {hx},{hz}: {(home.Closed ? "closed" : home.Why)}, {home.Beds} of {home.Planned} bunks, walls' top {top}, {sim.Orders.Total} orders left");
        // half the roof lifted off to look in: the half nearer the camera's side, eaves and all
        if (look is 1 or 3)
            for (int z = z0 - Shapes.Eaves; z <= z1 + Shapes.Eaves; z++)
                for (int x = (x0 + x1) / 2; x <= x1 + Shapes.Eaves; x++)
                    for (int y = top; y < top + Shapes.HouseHeight; y++)
                        if (w.Get(x, y, z) != Mat.Air) w.Set(x, y, z, Mat.Air);
        float cx2 = World.WX(hx), cz2 = World.WZ(hz);
        if (look is 1 or 3) Rig.Set(cx2 + .3f, cz2, 1.35f, 1.1f, look == 3 ? 2.0f : 2.4f, snap: true);
        else Rig.Set(cx2 + (look == 2 ? .6f : 0), cz2 + .2f, .75f, .6f, look == 2 ? 1.25f : 1.7f, snap: true);
        Hud.Refresh();
    }
}
