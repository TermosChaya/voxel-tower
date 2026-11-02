using System;
using System.Collections.Generic;
using System.Diagnostics;
using VoxelTower.Core;

/// <summary>
/// The bench's street of houses (--village, Sim.HouseLooks.cs and Sim.HouseDress.cs; the author: «делаем дома разными»,
/// «Больше декораций богу декораций»): two rows of four houses drawn with the rectangle facing each other across a
/// street laid with cobbles, of logs, stone and earth by turns. They are not all alike — several looks, washes and
/// paints among them — and each is a dwelling closed and furnished as a plain one is; the same plot drawn again is the
/// same house, and a storey goes up in its look. Once they stand they dress themselves: boxes of flowers, lamps,
/// benches, pots, barrels, chimneys, woodpiles, gardens, washing; nothing of it on the street, before a door or where a
/// door swings, every house still walked into by its door; dressing them is quick and meshes them little dearer.
/// </summary>
static class VillageBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        int kx = site.KeepX, kz = site.KeepZ;
        // a flat stretch a walk from the citadel, wide enough for the street
        (int x, int z) spot = (-1, -1);
        int best = int.MaxValue;
        for (int r = 160; r < 480; r += 8)
            for (int a = 0; a < 36; a++)
            {
                int x = kx + (int)(Math.Cos(a * Math.Tau / 36) * r), z = kz + (int)(Math.Sin(a * Math.Tau / 36) * r);
                if (x < 200 || z < 160 || x > w.SX - 200 || z > w.SZ - 160) continue;
                int lo = int.MaxValue, hi = 0;
                for (int dx = -170; dx <= 170; dx += 8)
                    for (int dz = -110; dz <= 110; dz += 8)
                    {
                        int h = w.SurfAt(x + dx, z + dz);
                        lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                        if (!w.OnLand(x + dx, z + dz) || site.Forbidden(x + dx, z + dz) || w.Get(x + dx, h - 1, z + dz) == Mat.Water
                            || Landmarks.Taken(site, x + dx, z + dz, 12) || site.RoadDist(x + dx, z + dz) < 12) hi = 999;
                    }
                if (hi - lo < best) { best = hi - lo; spot = (x, z); }
            }
        if (spot.x < 0 || best > 64) { check("a flat stretch for a street of houses", false, $"the flattest {best} voxels"); return; }
        // evened out to turf, as the player would have it cleared
        int g = w.SurfAt(spot.x, spot.z);
        for (int z = spot.z - 120; z <= spot.z + 120; z++)
            for (int x = spot.x - 170; x <= spot.x + 170; x++)
                for (int y = Math.Max(1, Math.Min(w.SurfAt(x, z), g) - 4); y < Math.Min(w.SY, Math.Max(w.SurfAt(x, z), g) + 120); y++)
                {
                    byte want = y < g ? (y == g - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.SetRaw(x, y, z, want);
                }
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        sim.Stock[Stockpile.Wood] += 3000.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 3000.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Earth] += 3000.0 * VoxelWorld.PerM3;
        sim.InstantLabour = true;
        // the street first, cobbled, so that nothing of the houses' may stand on it
        sim.Pave(Sim.WayPlan(w, site, spot.x - 160, spot.z, spot.x + 160, spot.z, 40, WayKind.Cobble));
        sim.CompleteAll();
        byte[] north = { Mat.Wood, Mat.Stone, Mat.Wood, Mat.Rammed }, south = { Mat.Stone, Mat.Wood, Mat.Rammed, Mat.Wood };
        var houses = new List<(Zone z, Look look, byte mat)>();
        var notes = new List<string>();
        for (int i = 0; i < 4; i++)
            for (int row = 0; row < 2; row++)
            {
                int x0 = spot.x - 140 + i * 72, x1 = x0 + 47;
                int z0 = row == 0 ? spot.z - 32 - 36 : spot.z + 32, z1 = z0 + 35;
                byte mat = row == 0 ? north[i] : south[i];
                var st = new Stroke { Mat = mat, Width = 4, Height = 32, Level = true };
                var plan = sim.PlanHouseAt(x0, z0, x1, z1, st, row == 0 ? 1 : 0, out var door, out string why, out var arched, out var rect, out _, out _, out var moves, varied: true);
                if (plan == null) { notes.Add($"{x0},{z0}: {why}"); continue; }
                sim.PutHouse(plan, arched, door, rect, moves);
                var look = sim.HouseLook(mat, rect.x0, rect.z0, rect.x1, rect.z1);
                houses.Add((null, look, mat));
                houses[^1] = (sim.Zones.Find(zz => zz.Kind == Zone.Dwelling && zz.X0 == rect.x0 && zz.Z0 == rect.z0), look, mat);
            }
        for (int i = 0; i < 6; i++) { sim.CompleteAll(); for (int t = 0; t < 50; t++) sim.Step(.05f); }
        foreach (var z in sim.Zones) if (z.Kind == Zone.Dwelling) sim.LookOver(z);
        // the middle of each house's floor, to walk into
        var inners = new List<int>();
        foreach (var (z, _, _) in houses) inners.Add(z == null ? -1 : sim.Nav.CellOf((z.X0 + z.X1) / 2, z.Depth - Shapes.HouseHeight + 1, (z.Z0 + z.Z1) / 2));
        // not all alike
        var looks = new HashSet<string>(); var washes = new HashSet<byte>(); var paints = new HashSet<byte>();
        foreach (var (_, look, _) in houses) { looks.Add(look?.Name ?? "—"); washes.Add(look?.Daub ?? 0); paints.Add(look?.Shutter ?? 0); }
        check("eight houses drawn in a street are not all alike: several looks, washes and shutters' paints among them",
            houses.Count == 8 && looks.Count >= 4 && washes.Count >= 2 && paints.Count >= 2,
            $"{houses.Count} houses{(notes.Count > 0 ? $" ({string.Join("; ", notes)})" : "")}; looks: {string.Join(", ", looks)}; {washes.Count} washes, {paints.Count} paints");
        int closed = 0, beds = 0;
        foreach (var (z, _, _) in houses) if (z != null && z.Closed) { closed++; beds += z.Beds; }
        check("each is a dwelling closed and furnished as a plain house is", closed == houses.Count && beds >= 3 * houses.Count, $"{closed} of {houses.Count} closed, {beds} beds");
        // the same plot, the same house; a storey in its look
        var (fz, flook, fmat) = houses.Find(h => h.look != null && h.look.Walls is WallStyle.Frame or WallStyle.Plaster or WallStyle.StoneBase);
        bool same = fz != null && ReferenceEquals(sim.HouseLook(fmat, fz.X0, fz.Z0, fz.X1, fz.Z1), flook);
        int daubUp = 0;
        if (fz != null && sim.PlanStorey(fz, fz.X0, fz.Z0, fz.X1, fz.Z1, fmat) == null)
        {
            int top0 = fz.Depth;
            for (int i = 0; i < 8; i++) { sim.CompleteAll(); for (int t = 0; t < 50; t++) sim.Step(.05f); }
            for (int z = fz.Z0; z <= fz.Z1; z++)
                for (int x = fz.X0; x <= fz.X1; x++)
                    for (int y = top0 + 2; y < Math.Min(w.SY, fz.Depth); y++) if (Mat.IsDaub(w.Get(x, y, z))) daubUp++;
        }
        check("the same plot drawn again is the same house, and a storey goes up on it in its look", same && daubUp > 40,
            $"«{flook?.Name}»: {(same ? "the same" : "another")}; daub in the storey {daubUp}");
        // undressed, as meshed
        int MeshQuads(out double ms)
        {
            var mesher = new GreedyMesher();
            var pad = new byte[34 * 34 * 34];
            int quads = 0;
            var sw = Stopwatch.StartNew();
            for (int cz = (spot.z - 120) >> VoxelWorld.Shift; cz <= (spot.z + 120) >> VoxelWorld.Shift; cz++)
                for (int cx = (spot.x - 170) >> VoxelWorld.Shift; cx <= (spot.x + 170) >> VoxelWorld.Shift; cx++)
                    for (int cy = (g - 16) >> VoxelWorld.Shift; cy <= Math.Min(w.CY - 1, (g + 110) >> VoxelWorld.Shift); cy++)
                    {
                        var b = mesher.MeshChunk(w, w.ChunkIndex(cx, cy, cz), pad);
                        if (b == null) continue;
                        quads += b.Quads;
                        b.Return();
                    }
            ms = sw.Elapsed.TotalMilliseconds;
            return quads;
        }
        // (the dressing set out in the steps above is taken down for the count, then set out at once again)
        for (int i = sim.Fixtures.Count - 1; i >= 0; i--)
            if (sim.Fixtures[i].Kind is FixtureKind.HouseProp or FixtureKind.WindowBox or FixtureKind.Garden || DressedHouse(sim, sim.Fixtures[i]))
                foreach (var (x, y, z, m) in sim.Fixtures[i].Voxels) if (w.Get(x, y, z) == m) w.Set(x, y, z, Mat.Air);
        sim.Fixtures.RemoveAll(f => f.Kind is FixtureKind.HouseProp or FixtureKind.WindowBox or FixtureKind.Garden || DressedHouse(sim, f));
        int plainQuads = MeshQuads(out double plainMs);
        // (the thread's own time, as the other checks of speed: the machine's load is no part of it)
        double cpu0 = BenchClock.CpuMs();
        sim.DressHouses();
        double dressMs = BenchClock.CpuMs() - cpu0;
        int dressedQuads = MeshQuads(out double dressedMs);
        var kinds = new Dictionary<FixtureKind, int>();
        int pieces = 0, onWay = 0, beforeDoor = 0;
        var doors = new List<Gateway>();
        foreach (var (z, _, _) in houses) if (z != null) doors.AddRange(sim.DoorsOf(z));
        foreach (var (z, _, _) in houses)
        {
            if (z == null) continue;
            foreach (var f in sim.DressOf(z))
            {
                pieces++;
                kinds[f.Kind] = kinds.GetValueOrDefault(f.Kind) + 1;
                foreach (var (x, y, zz, _) in f.Voxels)
                {
                    int ground = w.GroundTop(x, zz);
                    if (y - ground > 20) continue;
                    if (Mat.IsPavement[w.Get(x, ground, zz)] || Mat.IsPavement[w.Get(x, ground - 1, zz)]) onWay++;
                    foreach (var d in doors)
                    {
                        var (mx, _, mz) = d.Middle;
                        float along = d.AlongX ? MathF.Abs(x + .5f - mx) : MathF.Abs(zz + .5f - mz), off = d.AlongX ? MathF.Abs(zz + .5f - mz) : MathF.Abs(x + .5f - mx);
                        if (along < 11 && off < 10) beforeDoor++;
                    }
                }
            }
        }
        string KindList() { var l = new List<string>(); foreach (var (k, n) in kinds) l.Add($"{k} {n}"); return string.Join(", ", l); }
        check("once they stand they dress themselves: flower boxes, lamps, things by the doors, chimneys, woodpiles, gardens or washing",
            pieces >= 2 * houses.Count && kinds.ContainsKey(FixtureKind.WindowBox) && kinds.ContainsKey(FixtureKind.Chimney) && kinds.ContainsKey(FixtureKind.HouseProp),
            $"{pieces} pieces about {houses.Count} houses: {KindList()}");
        check("nothing of it on the street or before a door", onWay == 0 && beforeDoor == 0, $"{onWay} voxels on the way, {beforeDoor} before a door");
        // every house walked into by its door, dressed
        int walked = 0;
        for (int i = 0; i < houses.Count; i++)
        {
            var z = houses[i].z;
            int inner = inners[i];
            if (z == null) continue;
            int outer = sim.NearestStand((z.X0 + z.X1) / 2, spot.z, 2);
            if (inner < 0 || outer < 0) continue;
            var u = sim.Spawn(Kinds.Worker, outer);
            u.Hold = true;
            if (sim.GoTo(u, inner, adjacent: true, budget: 80000)) walked++;
            u.Dead = true; u.DiedAt = -999;
        }
        check("every house dressed is still walked into from the street", walked == houses.Count, $"{walked} of {houses.Count}");
        check("dressing the street is quick and makes it little dearer to draw", dressMs < 60 && dressedQuads <= plainQuads * 1.6,
            $"dressed in {dressMs:0.0} ms; {plainQuads} quads bare ({plainMs:0} ms), {dressedQuads} dressed ({dressedMs:0} ms)");

        // ---- the town furnishes itself as it grows: one thing at a time, built by the workers
        int set0 = sim.TownDecorSet;
        var spotMs = Stopwatch.StartNew();
        for (int i = 0; i < 16; i++) { sim.SetOutTownThing(); sim.CompleteAll(); for (int t = 0; t < 30; t++) sim.Step(.05f); }
        double perThing = spotMs.Elapsed.TotalMilliseconds / 16;
        var things = new Dictionary<DecorKind, int>();
        int ready = 0, yardOnWay = 0, yardBeforeDoor = 0, lamps = 0, lit = 0;
        var notReady = new List<string>();
        foreach (var y in sim.Yards)
        {
            things[y.Kind] = things.GetValueOrDefault(y.Kind) + 1;
            if (y.Ready) ready++;
            else
            {
                int st = 0;
                for (int i = 0; i < y.Voxels.Count; i++) if (w.Get(y.Voxels.X(i), y.Voxels.Y(i), y.Voxels.Z(i)) == y.Voxels.M(i)) st++;
                notReady.Add($"{Yard.Name(y.Kind)} {st}/{y.Voxels.Count}");
                if (Environment.GetEnvironmentVariable("DBG") == "1")
                    for (int i = 0; i < y.Voxels.Count; i++)
                        if (w.Get(y.Voxels.X(i), y.Voxels.Y(i), y.Voxels.Z(i)) != y.Voxels.M(i))
                            Console.WriteLine($"  missing {y.Kind} rot {y.Rot}: {y.Voxels.X(i) - y.X},{y.Voxels.Y(i) - y.Y},{y.Voxels.Z(i) - y.Z} want {y.Voxels.M(i)} is {w.Get(y.Voxels.X(i), y.Voxels.Y(i), y.Voxels.Z(i))} order {sim.Orders.At(y.Voxels.X(i), y.Voxels.Y(i), y.Voxels.Z(i))}");
            }
            if (y.Kind == DecorKind.LampPost) { lamps++; if (y.Light != null && sim.Fixtures.Contains(y.Light)) lit++; }
            for (int i = 0; i < y.Voxels.Count; i++)
            {
                int x = y.Voxels.X(i), zz = y.Voxels.Z(i), ground = w.GroundTop(x, zz);
                if (Mat.IsPavement[w.Get(x, ground, zz)] || Mat.IsPavement[w.Get(x, ground - 1, zz)]) yardOnWay++;
                foreach (var d in doors)
                {
                    var (mx, _, mz) = d.Middle;
                    float along = d.AlongX ? MathF.Abs(x + .5f - mx) : MathF.Abs(zz + .5f - mz), off = d.AlongX ? MathF.Abs(zz + .5f - mz) : MathF.Abs(x + .5f - mx);
                    if (along < 11 && off < 16) yardBeforeDoor++;
                }
            }
        }
        string ThingList() { var l = new List<string>(); foreach (var (k, n) in things) l.Add($"{Yard.Name(k)} {n}"); return string.Join(", ", l); }
        check("the town sets out benches, lamps, flower beds and more between its houses by itself, and the workers build them",
            sim.TownDecorSet - set0 >= 6 && things.Count >= 4 && ready == sim.Yards.Count && lamps > 0 && lit == lamps,
            $"{sim.TownDecorSet - set0} set out, {ready} standing{(notReady.Count > 0 ? $" (not yet: {string.Join(", ", notReady)})" : "")}: {ThingList()}; lamps lit {lit} of {lamps}; {perThing:0.0} ms a thing");
        check("nothing the town sets out stands on the street or before a door", yardOnWay == 0 && yardBeforeDoor == 0,
            $"{yardOnWay} voxels on the way, {yardBeforeDoor} before a door");
        int walkedAfter = 0;
        for (int i = 0; i < houses.Count; i++)
        {
            var z = houses[i].z;
            if (z == null || inners[i] < 0) continue;
            int outer = sim.NearestStand((z.X0 + z.X1) / 2, spot.z, 2);
            if (outer < 0) continue;
            var u = sim.Spawn(Kinds.Worker, outer);
            u.Hold = true;
            if (sim.GoTo(u, inners[i], adjacent: true, budget: 80000)) walkedAfter++;
            u.Dead = true; u.DiedAt = -999;
        }
        check("every house is still walked into with the town furnished", walkedAfter == houses.Count, $"{walkedAfter} of {houses.Count}");
    }

    /// <summary>A house's lamp or chimney (a torch or a chimney anchored in a house drawn with the rectangle).</summary>
    static bool DressedHouse(Sim sim, Fixture f)
    {
        if (f.Kind is not (FixtureKind.Torch or FixtureKind.Chimney)) return false;
        var (ax, _, az) = WaterPlanner.Unpack(f.Anchor);
        return sim.ZoneAt(ax, az) is { Kind: Zone.Dwelling } z && sim.BuildingOn(z) == null;
    }
}
