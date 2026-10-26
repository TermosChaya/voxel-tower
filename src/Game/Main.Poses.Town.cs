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
    /// down beside them); 1 the smithy close with half its roof lifted off, the anvil, the forge and the racks inside;
    /// 2 their fronts by day from the citadel's side, the signs, the chimneys and the things of each trade before them
    /// (Sim.Frontage.cs); 3 the same at night; 4 the smithy's front close, 5 the market's.
    /// </summary>
    void TownPose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX, kz = s.KeepZ;
        // a stretch of dry ground of a hundred voxels by three hundred, as level as can be found near the citadel
        int bx = kx + 200, bz = kz, best = int.MaxValue;
        for (int r = 160; r < 520; r += 16)
            for (int a = 0; a < 32; a++)
            {
                int cx = kx + (int)(r * Math.Cos(a * Math.Tau / 32)), cz = kz + (int)(r * Math.Sin(a * Math.Tau / 32));
                int lo = int.MaxValue, hi = int.MinValue; bool bad = false;
                for (int z = cz - 144; z <= cz + 144 && !bad; z += 6)
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
        ClearTrees(bx - 84, bz - 150, bx + 84, bz + 150);
        foreach (var k in new[] { "b_hall", "b_market", "b_barracks", "b_smithy" }) sim.Learnt.Add(k);
        sim.Stock.Gold = Math.Max(sim.Stock.Gold, 3000);
        sim.Stock[Stockpile.Wood] += 600.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 600.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Metal] += 10.0 * VoxelWorld.PerM3;
        for (int i = 0; i < 8; i++) sim.Spawn(Kinds.Worker, sim.NearestStand(bx - 30 + i * 8, bz + 76, 4));
        // in a row, the long sides along z, the doors to the citadel and room between the gable ends for a chimney
        var plan = new (string key, int dx, int dz)[] { ("hall", 0, -105), ("market", 0, -35), ("barracks", 0, 35), ("smithy", 0, 105) };
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
        // the workers gone from the doors, round the backs of the buildings, a little stock grown in each and what
        // each trade sets out before its door set out
        Advance(20);
        for (int i = 0; i < sim.Units.Count; i++)
        {
            var u = sim.Units[i];
            if (u.Dead || u.Team != Team.Player || Math.Abs(u.X - bx) > 70 || Math.Abs(u.Z - bz) > 160) continue;
            u.Cell = sim.NearestStand(kx < bx ? bx + 44 : bx - 44, bz - 60 + i % 10 * 12, u.T.Move.Height);
            (u.X, u.Y, u.Z) = sim.Nav.Feet(u.Cell); u.Path = null;
            u.Hold = true; u.Dest = u.Post = u.Cell;
        }
        sim.DressTown();
        Advance(2);
        GD.Print("[pose] town at " + bx + "," + bz + ": " + string.Join(", ", Array.ConvertAll(put, b => b == null ? "—" : $"{b.K.Name} {(b.Ready ? "ready" : "not ready")} level {b.Level}")) +
            $"; {sim.TownIncome():0.#} gold a minute, {sim.Orders.Total} orders left");
        int look = Math.Max(0, _look);
        var smithy = put[3];
        if (look == 3)
        {
            // dusk falls, and the night holds while the town is looked at
            sim.Waves.Countdown = Waves.Dusk + 1;
            Advance(2);
            for (int t = 0; t < 6; t++) { sim.Waves.Countdown = 8; Advance(1); }
        }
        // the fronts, from the side the doors look to
        float toFront = put[0] != null && sim.FrontOf(put[0], out _, out _, out _, out int fnx, out int fnz) ? MathF.Atan2(fnx, fnz) : MathF.Atan2(kx - bx, kz - bz);
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
        else if (look is 2 or 3) Rig.SetAt(World.WX(bx), World.WY(w.SurfAt(bx, bz) + 10), World.WZ(bz), toFront + .45f, .6f, .85f);
        else if (look is 4 or 5 && put[look == 4 ? 3 : 1] is { } close && sim.FrontOf(close, out int fx, out int fy, out int fz, out int nx, out int nz))
            Rig.SetAt(World.WX(fx + nx * 8), World.WY(fy + 10), World.WZ(fz + nz * 8), MathF.Atan2(nx, nz) + .35f, .5f, 2.2f);
        else Rig.Set(World.WX(bx), World.WZ(bz) + .4f, .75f, .72f, 1.05f, snap: true);
        Hud.Refresh();
    }
}
