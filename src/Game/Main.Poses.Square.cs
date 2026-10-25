using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// A little square between two houses (--pose=square), all of it drawn with the «Обустройство» tool and put up at
    /// once: a square of flagstones with a campfire in its middle and benches round it, a cobbled way off it, a path of
    /// earth and one of gravel to the houses' doors, a boardwalk; standing torches at its corners and a brazier, a well, a
    /// woodpile, a cart, barrels, a fence, a signpost and flower beds; the household about it, some on the benches and
    /// some at the fire. --look=0 (by itself) by day, seen as the game shows the valley; 1 the same at night; 2 the fire
    /// and the benches close by night; 3 the benches and the ways close by day; 4 the torches and the brazier by night.
    /// </summary>
    /// <summary>Where the square of --pose=square is.</summary>
    (int x, int z) _squareAt;

    /// <summary>
    /// The ground for --pose=square, made ready before the valley is drawn: the nearest stretch of level dry ground about
    /// the citadel wide enough for the square and a house either side, cleared of trees and evened out, easing back into
    /// the valley's ground at the edges.
    /// </summary>
    void SquareGround()
    {
        var w = World; var s = Site;
        int kx = s.KeepX, kz = s.KeepZ, hx = kx + 180, hz = kz + 160;
        bool found = false;
        for (int r = 130; r < 900 && !found; r += 12)
            for (int a = 0; a < 36 && !found; a++)
            {
                int cx = kx + (int)(r * Math.Cos(a * Math.PI / 18)), cz = kz + (int)(r * Math.Sin(a * Math.PI / 18));
                int lo = int.MaxValue, hi = int.MinValue; bool bad = false;
                for (int z = cz - 64; z <= cz + 64 && !bad; z += 4)
                    for (int x = cx - 104; x <= cx + 104 && !bad; x += 4)
                    {
                        if (x < 0 || z < 0 || x >= w.SX || z >= w.SZ || s.Forbidden(x, z)) { bad = true; break; }
                        int g = w.SurfAt(x, z); lo = Math.Min(lo, g); hi = Math.Max(hi, g);
                        if (w.Get(x, g, z) == Mat.Water || w.Get(x, g - 1, z) == Mat.Water) bad = true;
                    }
                if (!bad && hi - lo <= 6) { hx = cx; hz = cz; found = true; }
            }
        _squareAt = (hx, hz);
        if (!found) GD.Print("[pose] no level ground for the square");
        ClearTrees(hx - 150, hz - 110, hx + 150, hz + 110);
        int g0 = w.SurfAt(hx, hz);
        for (int z = hz - 72; z <= hz + 72; z++)
            for (int x = hx - 112; x <= hx + 112; x++)
            {
                float d = Math.Max(Math.Abs(x - hx) / 112f, Math.Abs(z - hz) / 72f), t = Math.Clamp((d - .8f) / .2f, 0, 1);
                int was = w.SurfAt(x, z), to = (int)MathF.Round(g0 + (was - g0) * t);
                if (to == was) continue;
                for (int y = Math.Min(was, to) - 1; y < Math.Max(was, to) + 4; y++)
                    w.SetRaw(x, y, z, y >= to ? Mat.Air : y == to - 1 ? Mat.Grass : Mat.Soil);
            }
    }

    void SquarePose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        var (hx, hz) = _squareAt;
        sim.Stock[Stockpile.Wood] += 600.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 600.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Earth] += 200.0 * VoxelWorld.PerM3;
        sim.InstantLabour = true;

        // a house either side of the square, their long sides to it
        var st = new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true };
        foreach (int side in new[] { -1, 1 })
        {
            int cx = hx + side * 78, x0 = cx - 18, x1 = cx + 18, z0 = hz - 27, z1 = hz + 27;
            var plan = Shapes.House(w, s, x0, z0, x1, z1, st, Shapes.LevelTop(w, cx, hz, 32), out var door, out _, out var arched);
            if (plan == null) continue;
            sim.DesignateBuild(plan);
            sim.Arch(arched);
            sim.PlanHouse(door, x0, z0, x1, z1);
        }
        sim.CompleteAll();
        Advance(2);
        sim.CompleteAll();

        // the ways: flags over the square, cobbles off it to the south, earth to the west house, gravel to the east
        // one, a boardwalk to the north
        void Way(int x0, int z0, int x1, int z1, int width, WayKind kind) { sim.Pave(Sim.WayPlan(w, s, x0, z0, x1, z1, width, kind)); sim.CompleteAll(); }
        Way(hx - 14, hz, hx + 14, hz, 44, WayKind.Flags);
        Way(hx, hz + 22, hx + 6, hz + 90, 14, WayKind.Cobble);
        Way(hx - 36, hz - 6, hx - 58, hz - 6, 10, WayKind.Dirt);
        Way(hx + 36, hz + 6, hx + 58, hz + 6, 10, WayKind.Gravel);
        Way(hx - 4, hz - 22, hx - 10, hz - 80, 12, WayKind.Boards);
        Advance(1);

        // the things, each where a householder would want it
        void Put(DecorKind k, int dx, int dz, int rot)
        {
            string why = sim.PlaceDecor(k, hx + dx, hz + dz, rot);
            if (why != null) GD.Print($"[pose] {Yard.Name(k)} at {dx},{dz}: {why}");
            sim.CompleteAll();
        }
        Put(DecorKind.Campfire, 0, 0, 0);
        Put(DecorKind.Bench, 0, -15, 0); Put(DecorKind.Bench, 0, 15, 2);
        Put(DecorKind.Bench, -16, 0, 3); Put(DecorKind.Bench, 16, 0, 1);
        foreach (var (dx, dz) in new[] { (-34, -20), (34, -20), (-34, 20), (34, 20) }) Put(DecorKind.Torch, dx, dz, 0);
        Put(DecorKind.Brazier, 18, 30, 0);
        Put(DecorKind.Well, -24, 34, 0);
        Put(DecorKind.Woodpile, -50, 30, 1);
        Put(DecorKind.Cart, 30, -40, 1);
        Put(DecorKind.Barrels, 52, -32, 0);
        Put(DecorKind.Fence, 24, 40, 1); Put(DecorKind.Fence, 33, 49, 0);
        Put(DecorKind.Signpost, 12, 26, 0);
        Put(DecorKind.FlowerBed, -48, -36, 0); Put(DecorKind.FlowerBed, 48, 38, 2);
        sim.InstantLabour = false;

        // the household out on the square
        var folk = new System.Collections.Generic.List<Unit>();
        foreach (var (t, dx, dz) in new[] { (Kinds.Worker, -8, -26), (Kinds.Worker, 8, -26), (Kinds.Worker, -26, 8), (Kinds.Worker, 26, -8),
                     (Kinds.Spearman, -10, 26), (Kinds.Archer, 10, 26), (Kinds.Worker, -24, -12), (Kinds.Swordsman, 24, 12), (Kinds.Worker, 40, 0), (Kinds.Worker, -40, 4) })
            folk.Add(sim.Spawn(t, sim.NearestStand(hx + dx, hz + dz, t.Move.Height)));
        bool night = _look is 1 or 2 or 4;
        if (night)
        {
            // dusk falls (the fires lit as it does), and the night holds while the scene is looked at
            sim.Waves.Countdown = Waves.Dusk + 1;
            Advance(2);
            sim.Waves.Countdown = 8;
        }
        // half of them to the benches, the rest about the fire
        for (int i = 0; i < folk.Count; i++) sim.Rest(folk[i], warm: night && i % 2 == 1);
        for (int t = 0; t < 30; t++)
        {
            Advance(1);
            if (night) sim.Waves.Countdown = 8;
            foreach (var u in folk) if (u.Pastime is not (Pastime.Sit or Pastime.Warm or Pastime.Stroll)) sim.Rest(u, warm: night && folk.IndexOf(u) % 2 == 1);
        }
        int sitting = 0, warming = 0;
        foreach (var u in folk) { if (u.Pastime == Pastime.Sit && u.SeatH > 0) sitting++; if (u.Pastime == Pastime.Warm) warming++; }
        int lit = 0, ready = 0;
        foreach (var y in sim.Yards) { if (y.Ready) ready++; if (y.Light != null) lit++; }
        GD.Print($"[pose] square at {hx},{hz}: {sim.Yards.Count} things ({ready} standing, {lit} lit), {sitting} on the benches, {warming} at the fire, darkness {sim.Waves.Darkness:0.00}");

        float cx2 = w.WX(hx), cz2 = w.WZ(hz), gy = w.WY(w.SurfAt(hx, hz));
        switch (_look)
        {
            case 2: Rig.SetAt(w.WX(hx + 6), gy, w.WZ(hz - 26), -.66f + MathF.PI, .55f, 2.4f); break;
            case 3: Rig.SetAt(w.WX(hx + 4), gy, w.WZ(hz + 24), -.66f + MathF.PI, .55f, 2.6f); break;
            case 4: Rig.SetAt(w.WX(hx + 6), gy + .4f, w.WZ(hz + 52), -.66f, .5f, 2.4f); break;
            default: Rig.Set(cx2, cz2, -.66f, .62f, 1.1f, snap: true); break;
        }
        Hud.Refresh();
    }
}
