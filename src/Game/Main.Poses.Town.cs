using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The town (--pose=town, Sim.Town.cs): on level ground by the citadel a town hall, a barracks, a smithy and a
    /// market, put up from their plans as by many hands and furnished by the household, the barracks raised a storey;
    /// their plates over them. --look=0 (by itself) the four seen from above at a slant (with --tab=town the tab let
    /// down beside them); 1 the smithy close with half its roof lifted off, the anvil, the forge and the racks inside.
    /// </summary>
    void TownPose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX, kz = s.KeepZ;
        // a stretch of dry ground of ninety voxels by a hundred and forty, as level as can be found near the citadel
        int bx = kx + 200, bz = kz, best = int.MaxValue;
        for (int r = 160; r < 520; r += 16)
            for (int a = 0; a < 32; a++)
            {
                int cx = kx + (int)(r * Math.Cos(a * Math.Tau / 32)), cz = kz + (int)(r * Math.Sin(a * Math.Tau / 32));
                int lo = int.MaxValue, hi = int.MinValue; bool bad = false;
                for (int z = cz - 72; z <= cz + 72 && !bad; z += 6)
                    for (int x = cx - 48; x <= cx + 48 && !bad; x += 6)
                    {
                        if (x < 8 || z < 8 || x >= w.SX - 8 || z >= w.SZ - 8 || s.Forbidden(x, z)) { bad = true; break; }
                        int g = w.SurfAt(x, z); lo = Math.Min(lo, g); hi = Math.Max(hi, g);
                        if (w.Get(x, g, z) == Mat.Water || w.Get(x, g - 1, z) == Mat.Water || Mat.IsPlayer[w.Get(x, g - 1, z)]) bad = true;
                    }
                // the nearer the better among the level ones
                int score = bad ? int.MaxValue : (hi - lo) * 1000 + r;
                if (score < best) { best = score; bx = cx; bz = cz; }
            }
        ClearTrees(bx - 60, bz - 84, bx + 60, bz + 84);
        foreach (var k in new[] { "b_hall", "b_market", "b_barracks", "b_smithy" }) sim.Learnt.Add(k);
        sim.Stock.Gold = Math.Max(sim.Stock.Gold, 3000);
        sim.Stock[Stockpile.Wood] += 600.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 600.0 * VoxelWorld.PerM3;
        for (int i = 0; i < 8; i++) sim.Spawn(Kinds.Worker, sim.NearestStand(bx - 30 + i * 8, bz + 76, 4));
        // two by two, the long sides along z, a lane between them
        var plan = new (string key, int dx, int dz)[] { ("hall", -24, -35), ("market", 24, -35), ("barracks", -24, 35), ("smithy", 24, 35) };
        var put = new Building[plan.Length];
        for (int i = 0; i < plan.Length; i++)
        {
            sim.InstantLabour = true;
            string no = sim.PutUp(Sim.TownKindOf(plan[i].key), bx + plan[i].dx, bz + plan[i].dz, false, out put[i]);
            sim.InstantLabour = false;
            if (no != null) GD.Print($"[pose] {plan[i].key}: {no}");
            // the door hung and the furnishing laid by the household before the next goes up (a market needs a hall)
            for (int t = 0; t < 240 && put[i] is { Ready: false }; t++) Advance(1);
        }
        var barracks = put[2];
        if (barracks is { Ready: true } && sim.Raise(barracks) == null)
        {
            // the storey put up at once, as by many hands, then furnished
            sim.InstantLabour = true;
            sim.CompleteAll();
            Advance(3);
            if (sim.ZoneOf(barracks) is { } z) sim.LookOver(z);
            sim.InstantLabour = false;
            for (int t = 0; t < 240 && barracks.Level < 2; t++) Advance(1);
        }
        // the workers gone from the doors, a little stock grown in each
        Advance(20);
        GD.Print("[pose] town at " + bx + "," + bz + ": " + string.Join(", ", Array.ConvertAll(put, b => b == null ? "—" : $"{b.K.Name} {(b.Ready ? "ready" : "not ready")} level {b.Level}")) +
            $"; {sim.TownIncome():0.#} gold a minute, {sim.Orders.Total} orders left");
        int look = Math.Max(0, _look);
        var smithy = put[3];
        if (look == 1 && smithy != null)
        {
            // half the smithy's roof lifted off to look in: the half nearer the camera's side, eaves and all
            int top = sim.ZoneOf(smithy)?.Depth ?? smithy.Top0;
            for (int z = smithy.Z0 - Shapes.Eaves; z <= smithy.Z1 + Shapes.Eaves; z++)
                for (int x = (smithy.X0 + smithy.X1) / 2; x <= smithy.X1 + Shapes.Eaves; x++)
                    for (int y = top; y < top + Shapes.HouseHeight; y++)
                        if (w.Get(x, y, z) != Mat.Air) w.Set(x, y, z, Mat.Air);
            Rig.Set(World.WX(smithy.CX) + .3f, World.WZ(smithy.CZ), 1.35f, 1.1f, 2.3f, snap: true);
        }
        else Rig.Set(World.WX(bx), World.WZ(bz) + .4f, .75f, .72f, 1.05f, snap: true);
        Hud.Refresh();
    }
}
