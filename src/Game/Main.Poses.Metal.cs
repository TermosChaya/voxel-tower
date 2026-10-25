using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// Iron (--pose=metal, Sim.Metal.cs), the stores with metal in them. --look=0 the outcrop of ore nearest the citadel
    /// seen from afar over the valley; 1 workers at a mine on an outcrop, the pit down to the ore; 2 the «Рудокопня»
    /// standing on an outcrop, the plan of another in hand and the outcrops it may go on marked; 3 a wall of iron plate
    /// beside one of stone, an iron gate hung in it; 4 the gatehouse from the library, its iron lintel and gate.
    /// </summary>
    void MetalPose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX, kz = s.KeepZ, look = Math.Max(0, _look);
        sim.Stock[Stockpile.Metal] = 12.4 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] += 600.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 600.0 * VoxelWorld.PerM3;
        sim.Stock.Gold = Math.Max(sim.Stock.Gold, 3000);
        // the outcrop nearest the citadel
        (int X, int Z, int Y, int R) crop = (kx - 200, kz, w.SurfAt(kx - 200, kz), 10);
        long best = long.MaxValue;
        foreach (var o in s.Outcrops)
        {
            long d = (long)(o.X - kx) * (o.X - kx) + (long)(o.Z - kz) * (o.Z - kz);
            if (d < best) { best = d; crop = o; }
        }
        float yaw = MathF.Atan2(crop.Z - kz, crop.X - kx) + MathF.PI;
        if (look == 0)
        {
            // the one most in the open: fewest trees about it
            int fewest = int.MaxValue;
            foreach (var o in s.Outcrops)
            {
                int trees = 0;
                for (int z = o.Z - 64; z <= o.Z + 64; z += 4)
                    for (int x = o.X - 64; x <= o.X + 64; x += 4)
                        if (w.InBounds(x, 1, z) && Mat.IsTrunk(w.Get(x, w.SurfAt(x, z) + 2, z))) trees++;
                if (trees < fewest) { fewest = trees; crop = o; }
            }
            yaw = MathF.Atan2(crop.Z - kz, crop.X - kx) + MathF.PI;
            Rig.Set(w.WX(crop.X), w.WZ(crop.Z), yaw, .62f, .85f, snap: true);
            GD.Print($"[pose] metal: the outcrop at {crop.X},{crop.Z}, {s.Outcrops.Count} in the valley");
        }
        else if (look == 1)
        {
            // a mine round the outcrop, eight workers at it for a while
            for (int i = 0; i < 8; i++) sim.Spawn(Kinds.Worker, sim.NearestStand(crop.X + 30, crop.Z + (i - 4) * 6, 4));
            // wide enough to follow the vein a way down into the hill from it
            sim.AddZone(crop.X - 44, crop.Z - 44, crop.X + 44, crop.Z + 44, Stockpile.Metal, 32);
            Advance(_poseSeconds > 0 ? _poseSeconds : 70);
            Rig.Set(w.WX(crop.X), w.WZ(crop.Z), yaw + .5f, .78f, 2.4f, snap: true);
            GD.Print($"[pose] metal: the mine at {crop.X},{crop.Z}, metal {sim.Stock[Stockpile.Metal] / VoxelWorld.PerM3:0.0} m³");
        }
        else if (look == 2)
        {
            // the miners' building on it, built as by many hands and furnished by the household; another's plan in hand
            var kind = Sim.TownKindOf("mine");
            sim.Learnt.Add("b_smithy"); sim.Learnt.Add(kind.Lore);
            for (int i = 0; i < 6; i++) sim.Spawn(Kinds.Worker, sim.NearestStand(crop.X + 40, crop.Z + (i - 3) * 6, 4));
            ClearTrees(crop.X - 50, crop.Z - 50, crop.X + 50, crop.Z + 50);
            sim.InstantLabour = true;
            string no = sim.PutUp(kind, crop.X, crop.Z, false, out var b);
            sim.InstantLabour = false;
            if (no != null) GD.Print($"[pose] mine: {no}");
            for (int t = 0; t < 240 && b is { Ready: false }; t++) Advance(1);
            Advance(10);
            Tools.StartBuilding(kind);
            // the camera between it and the nearest other outcrop, marked
            (int X, int Z, int Y, int R) other = crop;
            long near = long.MaxValue;
            foreach (var o in s.Outcrops)
            {
                long d = (long)(o.X - crop.X) * (o.X - crop.X) + (long)(o.Z - crop.Z) * (o.Z - crop.Z);
                if (o != crop && d < near) { near = d; other = o; }
            }
            float far = MathF.Sqrt(near) / 900f;
            Rig.Set(w.WX((crop.X + other.X) / 2), w.WZ((crop.Z + other.Z) / 2), yaw + .3f, .62f, Math.Clamp(1.3f - far, .55f, 1.3f), snap: true);
            GD.Print($"[pose] metal: the miners' building {(b == null ? "not put up" : $"ready {b.Ready}")}, {Tools.OutcropMarks} outcrops marked");
        }
        else
        {
            // level ground a walk from the citadel
            int bx = kx - s.KeepR - 160, bz = kz + 90, low = int.MaxValue;
            for (int r = s.KeepR + 280; r < 560; r += 16)
                for (int a = 0; a < 32; a++)
                {
                    int cx = kx + (int)(r * Math.Cos(a * Math.Tau / 32)), cz = kz + (int)(r * Math.Sin(a * Math.Tau / 32));
                    int lo = int.MaxValue, hi = int.MinValue; bool bad = false;
                    for (int z = cz - 50; z <= cz + 50 && !bad; z += 6)
                        for (int x = cx - 60; x <= cx + 60 && !bad; x += 6)
                        {
                            if (x < 8 || z < 8 || x >= w.SX - 8 || z >= w.SZ - 8 || s.Forbidden(x, z)) { bad = true; break; }
                            int g = w.SurfAt(x, z); lo = Math.Min(lo, g); hi = Math.Max(hi, g);
                            if (w.Get(x, g - 1, z) == Mat.Water || Mat.IsPlayer[w.Get(x, g - 1, z)]) bad = true;
                        }
                    int score = bad ? int.MaxValue : (hi - lo) * 1000 + r;
                    if (score < low) { low = score; bx = cx; bz = cz; }
                }
            ClearTrees(bx - 70, bz - 60, bx + 70, bz + 60);
            // the ground under them level (a gate's leaves on ground lower at one end stand on too little and come down)
            int lv = w.SurfAt(bx, bz);
            for (int z = bz - 40; z <= bz + 40; z++)
                for (int x = bx - 64; x <= bx + 64; x++)
                    for (int y = lv - 4; y < lv + 48; y++)
                    {
                        byte want = y < lv ? (y == lv - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                        if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                    }
            sim.InstantLabour = true;
            if (look == 3)
            {
                // a wall of iron beside a wall of stone, an iron gate in the iron one
                int top = Shapes.LevelTop(w, bx, bz, 32);
                sim.DesignateBuild(Shapes.Line(w, s, bx - 56, bz, bx - 4, bz, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Level = true }, top));
                sim.DesignateBuild(Shapes.Line(w, s, bx + 4, bz, bx + 56, bz, new Stroke { Mat = Mat.Plate, Width = 8, Height = 32, Level = true }, top));
                sim.InstantLabour = false;
                Advance(1);
                int fz = bz - 12;
                while (fz < bz && w.Get(bx + 30, top - 20, fz) == Mat.Air) fz++;
                var gp = GatePlan.At(w, s, new VoxelHit { X = bx + 30, Y = top - 20, Z = fz, NZ = -1, M = w.Get(bx + 30, top - 20, fz) }, iron: true);
                if (gp != null) sim.AddGate(gp, built: true);
                Advance(2);
                // from beyond them, looking back toward the citadel (it stands behind, not in the way)
                Rig.Set(w.WX(bx + 10), w.WZ(bz), MathF.Atan2(bz - kz, bx - kx), .5f, 2.2f, snap: true);
                GD.Print($"[pose] metal: walls at {bx},{bz}, gate {(gp == null ? "not hung" : "hung")}");
            }
            else
            {
                // the gatehouse, put up at once and its gate hung by the household
                var t = Sim.TemplateOf("gatehouse");
                sim.Standing = Math.Max(sim.Standing, t.Opens);
                string no = sim.PutTemplate(t, t.Plan(w, s, bx, bz, 0, 0, 0, t.Size));
                sim.InstantLabour = false;
                if (no != null) GD.Print($"[pose] gatehouse: {no}");
                for (int i = 0; i < 6; i++) sim.Spawn(Kinds.Worker, sim.NearestStand(bx + (i - 3) * 8, bz - 60, 4));
                for (int i = 0; i < 60 && sim.HouseDoors.Count > 0; i++) Advance(1);
                Advance(4);
                Rig.Set(w.WX(bx), w.WZ(bz), MathF.Atan2(bz - kz, bx - kx), .5f, 1.8f, snap: true);
                GD.Print($"[pose] metal: the gatehouse at {bx},{bz}, doors waiting {sim.HouseDoors.Count}, gates {sim.Gates.Count}, iron {sim.Gates.FindAll(g => g.Iron).Count}");
            }
        }
        Hud.Refresh();
    }
}
