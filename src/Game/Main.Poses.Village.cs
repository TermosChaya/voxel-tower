using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// A street of houses drawn with the rectangle (--pose=village, Sim.HouseLooks.cs and Sim.HouseDress.cs): on level
    /// ground west of the citadel, two rows of four facing each other across a street, of logs, stone and earth by turns,
    /// each in the look its plot gives it and dressed as it stands. --look=0 the street from over its east end, 1 the
    /// northern row's fronts close, 2 the southern row from behind (its gardens, washing and chimneys), 3 the street at
    /// dusk; 4 and 5 the same as 0 and 3 with the town furnished by itself (Sim.TownDecor.cs: benches, lamps, beds, a
    /// well…); 10–13 as 0–3 with the houses as they were before they differed (plain, undressed).
    /// </summary>
    void VillagePose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.WantTrails = false;
        int look = Math.Max(0, _look), view = look % 10;
        bool plain = look >= 10;
        sim.HouseDressing = !plain;
        if (_bare) Hud.Visible = false;
        int kx = s.KeepX, kz = s.KeepZ;
        // a stretch of ground as level as can be found west of the citadel, wide enough for the street
        int fx = kx - 300, fz = kz, best = int.MaxValue;
        for (int dx = -220; dx <= 0; dx += 16)
            for (int dz = -200; dz <= 200; dz += 16)
            {
                int cx = kx - 240 + dx, cz = kz + dz, lo = int.MaxValue, hi = int.MinValue;
                bool bad = false;
                for (int z = cz - 110; z <= cz + 110 && !bad; z += 8)
                    for (int x = cx - 150; x <= cx + 150 && !bad; x += 8)
                    {
                        if (!w.OnLand(x, z) || s.Forbidden(x, z)) { bad = true; break; }
                        int g = w.SurfAt(x, z); lo = Math.Min(lo, g); hi = Math.Max(hi, g);
                        if (w.Get(x, g - 1, z) == Mat.Water || Mat.IsPlayer[w.Get(x, g - 1, z)]) bad = true;
                    }
                int score = bad ? int.MaxValue : (hi - lo) * 1000 - dx;
                if (score < best) { best = score; fx = cx; fz = cz; }
            }
        // evened out to turf, bare of trees
        int g0 = w.SurfAt(fx, fz);
        for (int z = fz - 120; z <= fz + 120; z++)
            for (int x = fx - 160; x <= fx + 160; x++)
            {
                int top = w.SurfAt(x, z);
                for (int y = Math.Max(1, Math.Min(top, g0) - 2); y < Math.Max(top, g0) + 90 && y < w.SY; y++)
                {
                    byte want = y < g0 ? (y == g0 - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
            }
        sim.Stock.Gold = Math.Max(sim.Stock.Gold, 400);
        sim.Stock[Stockpile.Wood] += 2000.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 2000.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Earth] += 2000.0 * VoxelWorld.PerM3;
        sim.InstantLabour = true;
        void Settle() { for (int i = 0; i < 6; i++) { Advance(2.5); sim.CompleteAll(); } }
        byte[] north = { Mat.Wood, Mat.Stone, Mat.Wood, Mat.Rammed }, south = { Mat.Stone, Mat.Wood, Mat.Rammed, Mat.Wood };
        int houses = 0;
        for (int i = 0; i < 4; i++)
            for (int row = 0; row < 2; row++)
            {
                // each house 6 m along the street and 4.5 m deep, 3 m between them; the street 8 m wide
                int x0 = fx - 140 + i * 72, x1 = x0 + 47;
                int z0 = row == 0 ? fz - 32 - 36 : fz + 32, z1 = z0 + 35;
                var st = new Stroke { Mat = row == 0 ? north[i] : south[i], Width = 4, Height = 32, Level = true };
                var plan = sim.PlanHouseAt(x0, z0, x1, z1, st, row == 0 ? 1 : 0, out var door, out string why, out var arched, out var rect, out _, out _, out var moves, varied: !plain);
                if (plan == null) { GD.Print($"[pose] house at {x0},{z0}: {why}"); continue; }
                sim.PutHouse(plan, arched, door, rect, moves);
                houses++;
            }
        Settle();
        sim.InstantLabour = false;
        foreach (var z in sim.Zones) if (z.Kind == Zone.Dwelling) sim.LookOver(z);
        if (!plain) sim.DressHouses();
        if (view >= 4)
        {
            // the town furnishes itself, as over an hour or two of play
            sim.InstantLabour = true;
            for (int i = 0; i < 14; i++) { sim.SetOutTownThing(); Advance(1.5); }
            sim.InstantLabour = false;
        }
        int dressed = 0;
        foreach (var z in sim.Zones) if (z.Kind == Zone.Dwelling) dressed += sim.DressOf(z).Count;
        if (view is 3 or 5)
        {
            sim.Waves.Countdown = Waves.Dusk + 1;
            Advance(2);
            for (int t = 0; t < 6; t++) { sim.Waves.Countdown = 8; Advance(1); }
        }
        Advance(.5);
        Paused = true;
        GD.Print($"[pose] village at {fx},{fz}: {houses} houses, {sim.Zones.FindAll(z => z.Kind == Zone.Dwelling && z.Closed).Count} closed, {dressed} pieces of dressing");
        // (the camera stands off the way of its yaw: sin east, cos south)
        int ground = g0;
        switch (view)
        {
            case 1: Rig.SetAt(w.WX(fx - 20), w.WY(ground), w.WZ(fz - 30), .35f, .32f, 1.05f); break;
            case 2: Rig.SetAt(w.WX(fx - 30), w.WY(ground), w.WZ(fz + 60), .5f, .5f, 1.35f); break;
            // (the town's own things close, along the street from its east end)
            case 4 or 5: Rig.SetAt(w.WX(fx + 40), w.WY(ground), w.WZ(fz), 1.35f, .42f, 2.3f); break;
            default: Rig.SetAt(w.WX(fx), w.WY(ground), w.WZ(fz), 1.25f, .72f, .95f); break;
        }
    }
}
