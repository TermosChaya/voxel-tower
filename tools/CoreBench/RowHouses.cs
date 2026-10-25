using System;
using System.Collections.Generic;
using VoxelTower.Core;

/// <summary>
/// The bench's houses side by side (--rowhouses), the player's case first (25.10.2026: «поставила два домика рядом —
/// двери оказались недоступны, установила новую дверь — её не используют, старую не сносят»): a house built, and a second
/// drawn right up against the wall its door is in — the two share that wall, the first one's door is walled up and goes to
/// a free side, and both are entered from outside once the workers are done; the shared wall is one wall, and neither
/// roof reaches out over the other's room. A door the player puts in a house's wall with the gate tool is cut and used,
/// the old one walled up is not; a house drawn just short of another's door, not sharing its wall, is refused; a house
/// with nowhere free for its door is refused with why; and a door asked for on a side opens there.
/// </summary>
static class RowHousesBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        int kx = site.KeepX, kz = site.KeepZ;
        // a flat stretch of the plateau a walk from the citadel, wide enough for three houses
        (int x, int z) spot = (-1, -1);
        int best = int.MaxValue;
        for (int r = 140; r < 460; r += 8)
            for (int a = 0; a < 36; a++)
            {
                int x = kx + (int)(Math.Cos(a * Math.Tau / 36) * r), z = kz + (int)(Math.Sin(a * Math.Tau / 36) * r);
                if (x < 120 || z < 120 || x > w.SX - 120 || z > w.SZ - 120) continue;
                int lo = int.MaxValue, hi = 0;
                for (int dx = -90; dx <= 100; dx += 6)
                    for (int dz = -70; dz <= 90; dz += 6)
                    {
                        int h = w.SurfAt(x + dx, z + dz);
                        lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                        if (!w.OnLand(x + dx, z + dz) || site.Forbidden(x + dx, z + dz) || w.Get(x + dx, h, z + dz) == Mat.Water || w.Get(x + dx, h - 1, z + dz) == Mat.Water
                            || Landmarks.Taken(site, x + dx, z + dz, 12)) hi = 999;
                    }
                if (hi - lo < best) { best = hi - lo; spot = (x, z); }
            }
        if (spot.x < 0 || best > 6) { check("a flat stretch for houses side by side", false, $"the flattest {best} voxels"); return; }
        // evened out to turf, its trees, tufts and any ruin there taken away, as the player would have it cleared
        int g = w.SurfAt(spot.x, spot.z);
        for (int z = spot.z - 100; z <= spot.z + 110; z++)
            for (int x = spot.x - 110; x <= spot.x + 120; x++)
                for (int y = g - 10; y < Math.Min(w.SY, g + 120); y++)
                {
                    byte want = y < g ? (y == g - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.SetRaw(x, y, z, want);
                }
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        StartingFort.Raise(sim);
        for (int i = 0; i < 8; i++) sim.Spawn(Kinds.Worker, sim.Gate);
        sim.Stock[Stockpile.Wood] += 900.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 900.0 * VoxelWorld.PerM3;
        var st = new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true };
        const int t = Shapes.HouseWall;

        double Build(double limit)
        {
            double done = -1, quiet = -1, end = sim.Time + limit;
            while (sim.Time < end)
            {
                sim.Step(.05f);
                // (and the workers' scaffolding taken down: whoever is to go in, goes in by a door)
                bool idle = sim.Orders.Total == 0 && sim.HouseDoors.Count == 0 && sim.Scaffolds.Count == 0;
                if (done < 0 && idle) done = sim.Time;
                quiet = !idle ? -1 : quiet < 0 ? sim.Time : quiet;
                if (quiet >= 0 && sim.Time - quiet > 25) break;
            }
            return done;
        }
        Zone ZoneOf((int x0, int z0, int x1, int z1) r) => sim.Zones.Find(z => z.Kind == Zone.Dwelling && z.X0 == r.x0 && z.Z0 == r.z0 && z.X1 == r.x1 && z.Z1 == r.z1);
        // whether one of ours outside the house gets into its room: a way through a gate from a cell a few metres off
        (bool inside, Gateway through) Enter(Zone z, int floorTop)
        {
            // (its doors at rest first: a leaf still swinging after whoever went through last is in everyone's way)
            for (double end = sim.Time + 30; sim.Time < end && sim.DoorsOf(z).Exists(g => g.Step != 0);) sim.Step(.05f);
            int cx = (z.X0 + z.X1) / 2, cz = (z.Z0 + z.Z1) / 2;
            int inner = sim.Nav.CellOf(cx, floorTop + 1, cz);
            // from the ground five metres off, not in a house, on the field away from the old fort
            int outer = -1;
            var froms = new List<(int x, int z)> { (cx, z.Z0 - 40), (cx, z.Z1 + 40), (z.X0 - 40, cz), (z.X1 + 40, cz) };
            froms.Sort((a, b) => ((b.x - kx) * (b.x - kx) + (b.z - kz) * (b.z - kz)).CompareTo((a.x - kx) * (a.x - kx) + (a.z - kz) * (a.z - kz)));
            foreach (var (ox, oz) in froms)
            {
                if (!w.OnLand(ox, oz) || sim.ZoneAt(ox, oz) is { Kind: Zone.Dwelling }) continue;
                int c = sim.NearestStand(ox, oz, 2);
                if (c < 0) continue;
                var (fx, _, fz) = sim.Nav.Feet(c);
                if (sim.ZoneAt((int)fx, (int)fz) is { Kind: Zone.Dwelling }) continue;
                outer = c;
                break;
            }
            if (outer < 0) return (false, null);
            var u = sim.Spawn(Kinds.Worker, outer);
            u.Hold = true;
            bool ok = sim.GoTo(u, inner, adjacent: true, budget: 60000);
            Gateway via = null;
            if (ok && u.Path != null)
                foreach (int c in u.Path)
                    foreach (var g in sim.Gates) if (g.Cells.Contains(c)) via = g;
            u.Dead = true; u.DiedAt = -999;
            return (ok, via);
        }

        // how much of a gate's doorway is laid up again, through the thickness of the house's wall on that side
        int stone = 0;
        (int filled, int of) Laid(Gateway g, Zone house, int side)
        {
            int filled = 0, of = 0;
            stone = 0;
            int a0 = side switch { 0 => house.Z0, 1 => house.Z1 - t + 1, 2 => house.X0, _ => house.X1 - t + 1 };
            for (int a = a0; a < a0 + t; a++)
                for (int u = g.U0; u < g.U0 + Gateway.Width; u++)
                    for (int y = g.Y; y < g.Y + Gateway.Height; y++)
                    {
                        int x = g.AlongX ? u : a, z = g.AlongX ? a : u;
                        of++;
                        if (!Brushes.Free(w.Get(x, y, z)) && !Mat.IsLeaf(w.Get(x, y, z))) filled++;
                        if (Mat.IsMasonry(w.Get(x, y, z))) stone++;
                    }
            return (filled, of);
        }

        // ---- the player's case: a house, then a second right up against the wall its door is in
        int ax0 = spot.x - 50, az0 = spot.z - 18, ax1 = ax0 + 55, az1 = spot.z + 17;
        // (its door asked for on the side away from the citadel, so that the second goes up clear of the old fort: it
        // opens there)
        int away = kz > spot.z ? 0 : 1;
        var planA = sim.PlanHouseAt(ax0, az0, ax1, az1, st, away, out var doorA, out string whyA, out var archA, out var rectA, out _, out _, out var movesA);
        var (pocket, asked, askedWhy) = (planA, doorA, whyA);
        if (planA == null) { check("the first house is planned", false, whyA); return; }
        sim.PutHouse(planA, archA, doorA, rectA, movesA);
        double builtA = Build(900);
        var zoneA = ZoneOf(rectA);
        sim.LookOver(zoneA);
        var gatesA0 = sim.DoorsOf(zoneA);
        // the second drawn against the side A's door is in, a voxel off it — as the player would
        int sideA = doorA.Side;
        var (nx, nz) = Shapes.SideNormal(sideA);
        int bx0, bz0, bx1, bz1;
        if (nz != 0)
        {
            bx0 = ax0; bx1 = ax1;
            if (nz < 0) { bz1 = az0 - 1; bz0 = bz1 - 35; } else { bz0 = az1 + 1; bz1 = bz0 + 35; }
        }
        else
        {
            bz0 = az0; bz1 = az1;
            if (nx < 0) { bx1 = ax0 - 1; bx0 = bx1 - 55; } else { bx0 = ax1 + 1; bx1 = bx0 + 55; }
        }
        // as the houses stood before: the second as drawn, a voxel off the first, stands right before the first one's door
        int along = sideA < 2 ? doorA.X : doorA.Z, face = sideA switch { 0 => zoneA.Z0, 1 => zoneA.Z1, 2 => zoneA.X0, _ => zoneA.X1 };
        bool oldBlocked = sideA < 2
            ? bx0 <= along - Gateway.Half && bx1 >= along + Gateway.Half && (nz < 0 ? bz1 >= face - 8 : bz0 <= face + 8)
            : bz0 <= along - Gateway.Half && bz1 >= along + Gateway.Half && (nx < 0 ? bx1 >= face - 8 : bx0 <= face + 8);
        var planB = sim.PlanHouseAt(bx0, bz0, bx1, bz1, st, -1, out var doorB, out string whyB, out var archB, out var rectB, out int sharedB, out _, out var movesB);
        if (planB == null) { check("a house drawn up against another is planned", false, whyB); return; }
        int movedA = movesB?.Count ?? 0;
        sim.PutHouse(planB, archB, doorB, rectB, movesB);
        double builtB = Build(1200);
        var zoneB = ZoneOf(rectB);
        sim.LookOver(zoneA); sim.LookOver(zoneB);
        var (inA, viaA) = Enter(zoneA, doorA.Y - 8);
        var (inB, viaB) = Enter(zoneB, doorB.Y - 8);
        // the shared wall: one wall, four voxels thick, between the rooms
        int sharedCols = 0, doubled = 0;
        var (rxA0, rzA0, rxA1, rzA1) = (zoneA.X0 + t, zoneA.Z0 + t, zoneA.X1 - t, zoneA.Z1 - t);
        var (rxB0, rzB0, rxB1, rzB1) = (zoneB.X0 + t, zoneB.Z0 + t, zoneB.X1 - t, zoneB.Z1 - t);
        int gap = nz != 0 ? (nz < 0 ? rzA0 - rzB1 - 1 : rzB0 - rzA1 - 1) : (nx < 0 ? rxA0 - rxB1 - 1 : rxB0 - rxA1 - 1);
        // roof over the other's room: none of B's eaves over A's room, nor A's over B's
        int overA = 0, overB = 0;
        int topA = zoneA.Depth, topB = zoneB.Depth;
        for (int i = 0; i < planB.Count; i++)
        {
            int x = planB.X(i), y = planB.Y(i), z = planB.Z(i);
            if (x >= rxA0 && x <= rxA1 && z >= rzA0 && z <= rzA1) overA++;
            if (x >= zoneA.X0 && x <= zoneA.X1 && z >= zoneA.Z0 && z <= zoneA.Z1 && y < topA) doubled++;
        }
        for (int z = rzB0; z <= rzB1; z++)
            for (int x = rxB0; x <= rxB1; x++)
                for (int y = topB - 3; y <= topB + 1; y++)
                    if (sim.Arched.Get(x, y, z) != 0 && Mat.IsPlayer[w.Get(x, y, z)] && (x - rxB0 <= 2 || rxB1 - x <= 2 || z - rzB0 <= 2 || rzB1 - z <= 2) && y < topB) overB++;
        sharedCols = gap;
        var gatesA = sim.DoorsOf(zoneA);
        bool oldGateGone = gatesA0.TrueForAll(g => !sim.Gates.Contains(g));
        var (filledA, openingA) = gatesA0.Count > 0 ? Laid(gatesA0[0], zoneA, sideA) : (0, 1);
        Console.WriteLine($"row houses at {spot}: A built {builtA:0} s, door {Shapes.SideWords[doorA.Side]}; B drawn {(nz != 0 ? "across z" : "across x")}, as drawn its door blocked {oldBlocked}; " +
            $"shared {sharedB}, A's doors moved {movedA}, B built {builtB:0} s, door {Shapes.SideWords[doorB.Side]}; A closed {zoneA.Closed} «{zoneA.Why}» bunks {zoneA.Beds}, " +
            $"B closed {zoneB.Closed} «{zoneB.Why}» bunks {zoneB.Beds}; enter A {inA} via {viaA?.Id}, enter B {inB} via {viaB?.Id}; gap between rooms {gap}, B's plan over A's room {overA}, inside A's walls {doubled}, eaves left over B's room {overB}");
        check("the player's case: a house drawn right up against the wall another's door is in stood before that door", oldBlocked, $"the first one's door {Shapes.SideWords[sideA]}, the second drawn there");
        check("two houses side by side share the wall between them: one wall four voxels thick, no eaves over the other's room",
            sharedB != 0 && gap == t && overA == 0 && overB == 0, $"shared sides {sharedB}, wall between the rooms {gap} voxels, B over A's room {overA}, eaves over B's room {overB}");
        check("the door in the wall shared is walled up and the first house gets one on a free side; both are entered from outside and furnished",
            movedA >= 1 && oldGateGone && filledA >= openingA * 9 / 10 && gatesA.Count >= 1 && zoneA.Closed && zoneB.Closed && inA && inB && viaA != null && gatesA.Contains(viaA) && viaB != null && sim.DoorsOf(zoneB).Contains(viaB) && zoneA.Beds > 0 && zoneB.Beds > 0,
            $"moved {movedA}, old gate gone {oldGateGone}, its doorway {filledA} of {openingA} laid, A's doors now {gatesA.Count} (sides {sim.DoorSidesOf(zoneA)}); A closed {zoneA.Closed} «{zoneA.Why}», B closed {zoneB.Closed} «{zoneB.Why}»; in A {inA} (gate {viaA?.Id}), in B {inB} (gate {viaB?.Id}); bunks {zoneA.Beds}/{zoneB.Beds}");

        // ---- a door through a long wall, its windows and shutters cut out with the wall (they used to stop it)
        {
            int far = sideA ^ 1;
            var d = Shapes.DoorOn(far, zoneA.X0, zoneA.Z0, zoneA.X1, zoneA.Z1, doorA.Y - 9, zoneA.Depth);
            var gp = GatePlan.At(w, site, new VoxelHit { X = d.X, Y = d.Y, Z = d.Z, NX = d.NX, NZ = d.NZ }, d.Y - 8, outward: true);
            int panes = 0;
            if (gp != null)
                for (int i = 0; i < gp.Cut.Count; i++)
                {
                    byte m = w.Get(gp.Cut.X(i), gp.Cut.Y(i), gp.Cut.Z(i));
                    if (m is Mat.Window or Mat.WindowLog or Mat.Shutter or Mat.ShutterLog) panes++;
                }
            check("a door can be cut through a house's long wall where its windows are, the window going with the wall",
                gp != null && panes > 0, $"{Shapes.SideWords[far]}: {(gp == null ? "no gateway" : $"{gp.Cut.Count} voxels to cut, {panes} of window and shutters")}");
        }

        // ---- a door the player puts in, and the old one walled up
        var gB = sim.DoorsOf(zoneB);
        int newSide = -1, alongB = 0;
        for (int s = 0; s < 4 && newSide < 0; s++)
        {
            if (s == doorB.Side || (sim.SharedSidesOf(zoneB) & 1 << s) != 0) continue;
            int a = s < 2 ? (zoneB.X0 + zoneB.X1) / 2 : (zoneB.Z0 + zoneB.Z1) / 2;
            if (sim.HouseDoorAt(zoneB, s, a, out _) == null) { newSide = s; alongB = a; }
        }
        string added = newSide >= 0 ? sim.AddHouseDoor(zoneB, newSide, alongB) : "no free side";
        string walled = gB.Count > 0 ? sim.WallUp(gB[0]) : "no door";
        double redone = Build(600);
        sim.LookOver(zoneB);
        var gB2 = sim.DoorsOf(zoneB);
        var (inB2, viaB2) = Enter(zoneB, doorB.Y - 8);
        bool oldShut = gB.Count > 0 && !sim.Gates.Contains(gB[0]);
        var (filled, opening) = gB.Count > 0 ? Laid(gB[0], zoneB, doorB.Side) : (0, 1);
        int stoneB = stone;
        Console.WriteLine($"a door added {Shapes.SideWords[Math.Max(0, newSide)]}: «{added ?? "ok"}», the old walled «{walled ?? "ok"}»; done {redone:0} s; doors now {gB2.Count}, old gone {oldShut}, opening filled {filled}/{opening}; enter {inB2} via {viaB2?.Id}; closed {zoneB.Closed} «{zoneB.Why}»");
        check("a door put in a house's wall with the gate tool is cut and used; the old one walled up of the wall's stone is shut and not gone through",
            added == null && walled == null && oldShut && filled >= opening * 9 / 10 && stoneB >= filled * 9 / 10 && gB2.Count == 1 && inB2 && viaB2 != null && viaB2 == gB2[0] && zoneB.Closed,
            $"added «{added ?? "ok"}», walled «{walled ?? "ok"}»; old gate gone {oldShut}, its opening {filled} of {opening} laid, {stoneB} of stone as the wall; doors {gB2.Count}; in through {viaB2?.Id} (the new {(gB2.Count > 0 ? gB2[0].Id : -1)}); closed {zoneB.Closed} «{zoneB.Why}»");

        // ---- a house drawn half a metre short of a door, not sharing its wall: refused
        int door2Side = gB2.Count > 0 ? sim.DoorSidesOf(zoneB) : 0;
        int ds = -1;
        for (int s = 0; s < 4; s++) if ((door2Side & 1 << s) != 0) ds = s;
        string shortWhy = null;
        if (ds >= 0)
        {
            var (dnx, dnz) = Shapes.SideNormal(ds);
            int off = 5;
            int cx0, cz0, cx1, cz1;
            if (dnz != 0) { cx0 = zoneB.X0 + 4; cx1 = zoneB.X1 + 4; if (dnz < 0) { cz1 = zoneB.Z0 - off; cz0 = cz1 - 35; } else { cz0 = zoneB.Z1 + off; cz1 = cz0 + 35; } }
            else { cz0 = zoneB.Z0 + 4; cz1 = zoneB.Z1 + 4; if (dnx < 0) { cx1 = zoneB.X0 - off; cx0 = cx1 - 55; } else { cx0 = zoneB.X1 + off; cx1 = cx0 + 55; } }
            sim.PlanHouseAt(cx0, cz0, cx1, cz1, st, -1, out _, out shortWhy, out _, out _, out _, out _, out _);
        }
        check("a house drawn just short of a door of another, not sharing its wall, is refused with why", shortWhy != null && shortWhy.Contains("перед дверью"), $"«{shortWhy}»");

        // ---- nowhere free for a door: refused
        string boxedWhy = null;
        {
            // a small house walled in on all four sides by a ring of stone a quarter metre off it
            int qx0 = spot.x + 40, qz0 = spot.z + 40, qx1 = qx0 + 47, qz1 = qz0 + 35;
            var ring = Shapes.Box(w, site, qx0 - 3, qz0 - 3, qx1 + 3, qz1 + 3, new Stroke { Mat = Mat.Stone, Width = 2, Height = 32, Level = true, Hollow = true }, w.SurfAt(qx0, qz0) + 32);
            sim.DesignateBuild(ring);
            sim.CompleteAll();
            sim.PlanHouseAt(qx0, qz0, qx1, qz1, st, -1, out _, out boxedWhy, out _, out _, out _, out _, out _);
        }
        check("a house with nowhere free before any wall is refused with why; a door asked for on a side opens there",
            boxedWhy != null && boxedWhy.Contains("некуда") && pocket != null && asked.Side == away,
            $"walled round: «{boxedWhy}»; asked {Shapes.SideWords[away]}: {(pocket != null ? Shapes.SideWords[asked.Side] : askedWhy)}");
    }
}
