using System;
using System.Collections.Generic;
using VoxelTower.Core;

/// <summary>
/// The bench's houses (--houses): a house drawn with the rectangle and built by the workers themselves, not at once —
/// nothing of it falls or cracks while they build, its roof stands whole and pitched when they are done, it has a floor
/// of logs, and once it is closed it is furnished with bunks along its walls and no barrel or crate stands in it; a house
/// drawn over it makes a second storey: the roof taken down, a ladder up through a hatch in a new floor, the walls going
/// on up and a roof of their own, and the two floors one dwelling furnished on both; a house drawn half over it is
/// refused, and so is a third storey.
/// </summary>
static class HousesBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        StartingFort.Raise(sim);
        int kx = site.KeepX, kz = site.KeepZ;
        // a flat spot of the plateau a walk from the citadel
        (int x, int z) spot = (-1, -1);
        for (int r = 120; r < 400 && spot.x < 0; r += 8)
            for (int a = 0; a < 24 && spot.x < 0; a++)
            {
                int x = kx + (int)(Math.Cos(a * Math.Tau / 24) * r), z = kz + (int)(Math.Sin(a * Math.Tau / 24) * r);
                if (x < 80 || z < 80 || x > w.SX - 80 || z > w.SZ - 80) continue;
                int lo = int.MaxValue, hi = 0;
                for (int dx = -40; dx <= 40; dx += 4) for (int dz = -40; dz <= 40; dz += 4) { int h = w.SurfAt(x + dx, z + dz); lo = Math.Min(lo, h); hi = Math.Max(hi, h); if (Mat.IsTrunk(w.Get(x + dx, h + 2, z + dz))) hi = 999; }
                if (hi - lo <= 2 && !site.Forbidden(x, z)) spot = (x, z);
            }
        for (int i = 0; i < 6; i++) sim.Spawn(Kinds.Worker, sim.Gate);
        sim.Stock[Stockpile.Wood] += 600.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 600.0 * VoxelWorld.PerM3;
        int fell = 0, cracked = 0;
        // what falls while a storey's roof is taken apart is the roof coming down; what falls after is the building
        int fellTakingDown = 0;
        sim.Collapsed += (n, _, _, _) => { if (sim.Storeys.Count > 0) fellTakingDown += n; else fell += n; };
        // the house: four and a half metres by seven, of stone
        const int t = Shapes.HouseWall;
        int x0 = spot.x - 18, z0 = spot.z - 28, x1 = spot.x + 18, z1 = spot.z + 27;
        var st = new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true };
        var house = Shapes.House(w, site, x0, z0, x1, z1, st, Shapes.LevelTop(w, spot.x, spot.z, 32), out var door, out string why, out var arched);
        if (house == null) { check("a house drawn with the rectangle is planned", false, why); return; }
        sim.DesignateBuild(house);
        sim.Arch(arched);
        sim.PlanHouse(door, x0, z0, x1, z1);
        int top = door.Top, floorTop = door.Y - 8;
        // the doorway, cut once the wall stands: not missing from the house
        bool Doorway(int x, int y, int z)
        {
            var g = sim.Gates.Find(g => g.Y == floorTop);
            if (g == null || y < g.Y || y >= g.Y + Gateway.Height) return false;
            int u = g.AlongX ? x : z, a = g.AlongX ? z : x, at = door.NX != 0 ? door.X : door.Z, n = door.NX != 0 ? door.NX : door.NZ;
            return u >= g.U0 && u < g.U0 + Gateway.Width && (a - at) * n <= 0 && (a - at) * n > -t;
        }
        double Build(double limit)
        {
            // done when nothing is ordered any more; then on until the work has been quiet half a minute, as the
            // furnishing is ordered a moment after the walls stand and wants its time too
            double done = -1, quiet = -1, end = sim.Time + limit;
            while (sim.Time < end)
            {
                sim.Step(.05f);
                foreach (var (_, c) in sim.Cracks) if (c > .5f) { cracked++; break; }
                bool idle = sim.Orders.Total == 0 && sim.Storeys.Count == 0;
                if (done < 0 && idle) done = sim.Time;
                quiet = !idle ? -1 : quiet < 0 ? sim.Time : quiet;
                if (quiet >= 0 && sim.Time - quiet > 30) break;
            }
            return done;
        }
        double built = Build(1500);
        int missing = 0, roofMissing = 0, roofAbove = 0;
        for (int i = 0; i < house.Count; i++)
        {
            int x = house.X(i), y = house.Y(i), z = house.Z(i);
            if (y > top + 3) roofAbove++;
            if (w.Get(x, y, z) == Mat.Air && !Doorway(x, y, z)) { missing++; if (y >= top) roofMissing++; }
        }
        int floorLogs = 0, room = 0;
        for (int z = z0 + t; z <= z1 - t; z++)
            for (int x = x0 + t; x <= x1 - t; x++) { room++; if (w.Get(x, floorTop - 1, z) == Mat.Wood) floorLogs++; }
        var zone = sim.Zones.Find(z => z.Kind == Zone.Dwelling);
        sim.LookOver(zone);
        // barrels, crates or sacks standing in the room (not along the house's walls outside)
        int Props()
        {
            int n = 0;
            foreach (var f in sim.Fixtures)
                if (f.Kind is FixtureKind.Barrel or FixtureKind.Crate or FixtureKind.Sacks
                    && f.Voxels.Exists(v => v.x >= x0 + t && v.x <= x1 - t && v.z >= z0 + t && v.z <= z1 - t)) n++;
            return n;
        }
        int props = Props();
        Console.WriteLine($"house at {spot}: {house.Count} voxels, built at {built:0} s, missing {missing} (roof {roofMissing}), fell {fell}, cracked {cracked}, dwelling {zone?.Closed} «{zone?.Why}» bunks {zone?.Beds}/{zone?.Planned}, props inside {props}, places {sim.Places}");
        check("a house the workers build stands whole when they are done: nothing of it fell or cracked while they built, its roof pitched and with no hole",
            built >= 0 && missing == 0 && fell == 0 && cracked == 0 && roofAbove > 400,
            $"built at {built:0} s, {missing} voxels of it missing ({roofMissing} of the roof), {fell} fell, {cracked} steps with a crack; {roofAbove} voxels of roof over the eaves' course");
        check("a house has a floor of logs inside its walls", floorLogs == room, $"{floorLogs} of {room} columns floored");
        check("a house built is furnished at once: bunks along its walls, and no barrel, crate or sack stands in it",
            zone is { Closed: true } && zone.Beds >= 2 && zone.Beds == zone.Planned && props == 0,
            $"closed {zone?.Closed} «{zone?.Why}», bunks {zone?.Beds} of {zone?.Planned}, props inside {props}");
        if (zone == null) return;

        // drawn half over it: refused; over it: a second storey
        string half = sim.PlanStorey(zone, x0 + 12, z0, x1 + 12, z1, Mat.Stone);
        int beds1 = zone.Beds;
        string no = sim.PlanStorey(zone, x0 + 1, z0 - 1, x1, z1 + 2, Mat.Stone);
        fell = 0; cracked = 0;
        double up = Build(1500);
        int newTop = zone.Depth;
        int upperFloor = 0, ladders = 0, upperWall = 0;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                bool inside = x - x0 >= t && x1 - x >= t && z - z0 >= t && z1 - z >= t;
                if (inside && w.Get(x, top - 1, z) == Mat.Wood) upperFloor++;
                if (!inside && w.Get(x, top + 12, z) != Mat.Air) upperWall++;
                for (int y = floorTop; y < top + 2; y++) if (Mat.IsLadder[w.Get(x, y, z)]) { ladders++; break; }
            }
        int debrisLeft = 0;
        for (int z = z0 + t; z <= z1 - t; z++) for (int x = x0 + t; x <= x1 - t; x++) for (int y = floorTop; y < newTop; y++) if (w.Get(x, y, z) is Mat.Lumber or Mat.Rubble) debrisLeft++;
        sim.LookOver(zone);
        Console.WriteLine($"storey: refused half over «{half}», {(no == null ? "planned" : no)}; built at {up:0} s, top {top} → {newTop}, upper floor {upperFloor} of {room}, ladder columns {ladders}, upper wall {upperWall}, fell {fell} (taking the roof down {fellTakingDown}, debris left in the house {debrisLeft}), cracked {cracked}, dwelling {zone.Closed} «{zone.Why}» bunks {zone.Beds}/{zone.Planned} (was {beds1}), places {sim.Places}");
        check("a house drawn half over a house is refused with why", half != null && half.Contains("ровно"), $"«{half}»");
        check("a house drawn over a house raises a second storey: the roof taken down, a floor laid with a hatch over a ladder, the walls going on up and a roof of their own",
            no == null && up >= 0 && newTop == top + Shapes.HouseHeight && upperFloor > room * 8 / 10 && upperFloor < room && ladders >= 4 && upperWall > 200 && fell == 0 && cracked == 0 && debrisLeft == 0,
            $"{(no ?? "planned")}; done at {up:0} s, top {top} → {newTop}, upper floor {upperFloor} of {room}, ladder in {ladders} columns, {upperWall} of wall a metre and a half over the old top, {fell} fell ({fellTakingDown} of the old roof as it came down, {debrisLeft} of it left lying in the house), {cracked} cracked");
        check("a house of two storeys is one dwelling, furnished on both floors", zone.Closed && zone.Beds > beds1 && zone.Beds == zone.Planned,
            $"closed {zone.Closed} «{zone.Why}», bunks {zone.Beds} of {zone.Planned} (one floor had {beds1})");
        string third = sim.PlanStorey(zone, x0, z0, x1, z1, Mat.Stone);
        check("a third storey is refused", third != null && third.Contains("двух"), $"«{third}»");
    }
}
