using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// Houses side by side (--pose=rowhouses, Sim.Houses.cs) on level ground west of the citadel: three in a row, each
    /// drawn up against the last, sharing their walls, the middle one's door gone to its end. --look=0 (by itself) the
    /// row built, the household going in by the doors; 1 the southern house with a door put in its end and the old one
    /// walled up, the household going in by the new one; 2 the template library open at «Декор», a campfire's ghost by the
    /// houses.
    /// </summary>
    void RowHousesPose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.WantTrails = false;
        int kx = s.KeepX, kz = s.KeepZ;
        // a stretch of ground as level as can be found west of the citadel
        int fx = kx - 260, fz = kz, best = int.MaxValue;
        for (int dx = -200; dx <= 0; dx += 16)
            for (int dz = -160; dz <= 160; dz += 16)
            {
                int cx = kx - 200 + dx, cz = kz + dz, lo = int.MaxValue, hi = int.MinValue;
                bool bad = false;
                for (int z = cz - 90; z <= cz + 90 && !bad; z += 8)
                    for (int x = cx - 80; x <= cx + 80 && !bad; x += 8)
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
        for (int z = fz - 100; z <= fz + 100; z++)
            for (int x = fx - 90; x <= fx + 90; x++)
            {
                int top = w.SurfAt(x, z);
                for (int y = Math.Max(1, Math.Min(top, g0) - 2); y < Math.Max(top, g0) + 80 && y < w.SY; y++)
                {
                    byte want = y < g0 ? (y == g0 - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
            }
        sim.Stock.Gold = Math.Max(sim.Stock.Gold, 400);
        sim.Stock[Stockpile.Wood] += 800.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 1600.0 * VoxelWorld.PerM3;
        int look = Math.Max(0, _look);
        var st = new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true };
        Zone House(int x0, int z0, int x1, int z1, int side)
        {
            var plan = sim.PlanHouseAt(x0, z0, x1, z1, st, side, out var door, out string why, out var arched, out var rect, out _, out _, out var moves);
            if (plan == null) { GD.Print($"[pose] house at {x0},{z0}: {why}"); return null; }
            sim.PutHouse(plan, arched, door, rect, moves);
            return sim.Zones.Find(z => z.Kind == Zone.Dwelling && z.X0 == rect.x0 && z.Z0 == rect.z0 && z.X1 == rect.x1 && z.Z1 == rect.z1);
        }
        void Settle() { for (int i = 0; i < 8; i++) { Advance(2.5); sim.CompleteAll(); } }
        sim.InstantLabour = true;
        // the middle one first, its door south; then one drawn up against that wall a voxel off it, then one on its north
        int x0 = fx - 28, x1 = fx + 27;
        var mid = House(x0, fz - 18, x1, fz + 17, 1);
        Settle();
        var south = mid == null ? null : House(x0, mid.Z1 + 1, x1, mid.Z1 + 36, -1);
        Settle();
        var north = mid == null ? null : House(x0, mid.Z0 - 36, x1, mid.Z0 - 1, -1);
        Settle();
        if (look == 1 && south != null)
        {
            // a door put in the southern one's east end, and its old door walled up
            var old = sim.DoorsOf(south);
            string no = sim.AddHouseDoor(south, 3, (south.Z0 + south.Z1) / 2);
            if (no != null) GD.Print($"[pose] door: {no}");
            foreach (var g in old) if (sim.WallUp(g) is { } why) GD.Print($"[pose] wall up: {why}");
            Settle();
            foreach (var g in sim.DoorsOf(south)) GD.Print($"[pose] the southern house's door: {(g.AlongX ? $"z {g.P}" : $"x {g.P}")}, ready {g.Ready}, was there {old.Contains(g)}");
        }
        sim.InstantLabour = false;
        foreach (var z in sim.Zones) if (z.Kind == Zone.Dwelling) sim.LookOver(z);
        // the household going in: from a few metres out before each door
        var doors = new List<(Gateway g, Zone house)>();
        foreach (var h in new[] { mid, south, north })
            if (h != null && (look != 1 || h == south)) foreach (var g in sim.DoorsOf(h)) doors.Add((g, h));
        foreach (var (g, h) in doors)
        {
            var (mx, my, mz) = g.Middle;
            int ox = (int)mx + (g.AlongX ? 0 : g.In * 44), oz = (int)mz + (g.AlongX ? g.In * 44 : 0);
            int inner = sim.Nav.CellOf((h.X0 + h.X1) / 2, h.Depth - Shapes.HouseHeight + 1, (h.Z0 + h.Z1) / 2);
            for (int i = 0; i < 3; i++)
            {
                var u = sim.Spawn(Kinds.Worker, sim.NearestStand(ox + (g.AlongX ? i * 6 - 6 : i * 5), oz + (g.AlongX ? i * 5 : i * 6 - 6), 2));
                u.Hold = true;
                sim.GoTo(u, inner, adjacent: true, budget: 60000);
            }
        }
        Advance(1.6);
        // (held there: those going in caught on the way to the doors)
        if (look < 2) Paused = true;
        GD.Print($"[pose] row houses at {fx},{fz}: {sim.Zones.FindAll(z => z.Kind == Zone.Dwelling && z.Closed).Count} closed, gates {sim.Gates.Count}, orders {sim.Orders.Total}");
        int ground = g0;
        // (the camera stands off the way of its yaw: sin east, cos south) from the east, before the southern one's end
        // and its new door, its south wall aside; the library from the south-east; else from the north-east, the doors
        // of the northern one and of the middle one's end in sight
        if (look == 1) Rig.SetAt(w.WX(x1 + 6), w.WY(ground), w.WZ(south?.Z1 - 14 ?? fz), 1.12f, .5f, 1.5f);
        else if (look == 2) Rig.SetAt(w.WX(x1 + 30), w.WY(ground), w.WZ(fz + 20), .69f, .62f, 1.4f);
        else Rig.SetAt(w.WX(fx + 20), w.WY(ground), w.WZ(fz - 20), 2.45f, .6f, 1.25f);
        if (look == 2)
        {
            Tools.SetTool(Tool.Template);
            Tools.PickTemplateTab(2);
            Tools.PickTemplateDecor(Array.IndexOf(Yard.All, DecorKind.Campfire));
        }
        // the cursor on the open ground right of the row, in sight whatever the screen
        if (look == 2) _poseLater = () => { Tools._UnhandledInput(new InputEventMouseMotion { Position = GetViewport().GetVisibleRect().Size * new Vector2(.8f, .6f) }); Hud.Refresh(); };
        Hud.Refresh();
    }
}
