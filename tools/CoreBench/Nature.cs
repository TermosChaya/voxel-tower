using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The small things a valley grows by itself (--nature, MapGen.Nature.cs): how long they take to make and how much they
/// add; each kind of them in each kind of valley in a sensible number and where it belongs (fallen trees in the woods,
/// reeds by the water, lilies on the lakes), none on the citadel's plateau, a road, the river's bed, a named place, the
/// ore or the horde's strip; the same seed the same valley; no ground left hanging over a pit, and a puddle that stays;
/// a fallen tree sawn up for logs as a felled one is, a bush that burns.
/// </summary>
static class NatureBench
{
    static (VoxelWorld w, Site s, long ms) Make(ValleyKind kind, uint seed, bool details)
    {
        var mp = new MapParams { Seed = seed, Kind = kind, Hexagon = true, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize, Details = details };
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var sw = Stopwatch.StartNew();
        var s = MapGen.Generate(w, mp);
        return (w, s, sw.ElapsedMilliseconds);
    }

    /// <summary>Every voxel of a world, a chunk at a time: what is in each, by material.</summary>
    static long[] Tally(VoxelWorld w)
    {
        var n = new long[256];
        for (int ci = 0; ci < w.ChunkCount; ci++)
        {
            var d = w.ChunkData(ci);
            if (d == null) { n[w.ChunkUniform(ci)] += VoxelWorld.CS * VoxelWorld.CS * VoxelWorld.CS; continue; }
            foreach (byte m in d) n[m]++;
        }
        return n;
    }

    /// <summary>The voxels where two worlds differ: (x, y, z, what the first has, what the second has).</summary>
    static List<(int x, int y, int z, byte a, byte b)> Diff(VoxelWorld a, VoxelWorld b)
    {
        var l = new List<(int, int, int, byte, byte)>();
        for (int ci = 0; ci < a.ChunkCount; ci++)
        {
            var da = a.ChunkData(ci); var db = b.ChunkData(ci);
            if (da == null && db == null && a.ChunkUniform(ci) == b.ChunkUniform(ci)) continue;
            if (da != null && db != null && da.AsSpan().SequenceEqual(db)) continue;
            a.ChunkCoords(ci, out int cx, out int cy, out int cz);
            for (int y = 0; y < VoxelWorld.CS; y++)
                for (int z = 0; z < VoxelWorld.CS; z++)
                    for (int x = 0; x < VoxelWorld.CS; x++)
                    {
                        int wx = cx * VoxelWorld.CS + x, wy = cy * VoxelWorld.CS + y, wz = cz * VoxelWorld.CS + z;
                        byte ma = a.Get(wx, wy, wz), mb = b.Get(wx, wy, wz);
                        if (ma != mb) l.Add((wx, wy, wz, ma, mb));
                    }
        }
        return l;
    }

    static readonly byte[] Flowers = { Mat.Flower, Mat.Bloom, Mat.FlowerRed, Mat.FlowerWhite, Mat.FlowerBlue };

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        // ---- how long: the first valley made without its small things and with them, the best of three each
        long bare = long.MaxValue, full = long.MaxValue;
        for (int k = 0; k < 3; k++)
        {
            bare = Math.Min(bare, Make(ValleyKind.First, 7, false).ms);
            full = Math.Min(full, Make(ValleyKind.First, 7, true).ms);
        }
        var (w0, s0, _) = Make(ValleyKind.First, 7, false);
        var (w, s, _) = Make(ValleyKind.First, 7, true);
        var diff = Diff(w0, w);
        int added = diff.Count(d => d.a == Mat.Air && d.b != Mat.Air), sprigs = diff.Count(d => Mat.IsSprig(d.b) && !Mat.IsSprig(d.a));
        var t0 = Tally(w0); var t1 = Tally(w);
        long solid0 = 0, solid1 = 0;
        for (int m = 1; m < 256; m++) if (m != Mat.Water && !Mat.IsSprig((byte)m)) { solid0 += t0[m]; solid1 += t1[m]; }
        Console.WriteLine($"the first valley made in {bare} ms without its small things, {full} ms with them; {diff.Count} voxels differ, {added} laid where there was air, " +
            $"{sprigs} of them drawn in blades; solid voxels {solid0} → {solid1} (+{(solid1 - solid0) * 100.0 / solid0:0.00} %)");
        check("the valley's small things take little time to make and add little to draw: under 0,8 s more, under 1 % more solid voxels",
            full - bare < 800 && (solid1 - solid0) < solid0 / 100,
            $"{bare} → {full} ms; solid +{solid1 - solid0} of {solid0}");

        // ---- what there is of each, in the first valley and in each kind
        string Count(Site site) => string.Join(", ", site.Details.GroupBy(d => d.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"));
        string Sprigs(long[] t) => $"flowers {Flowers.Sum(f => t[f])} (red {t[Mat.FlowerRed]}, white {t[Mat.FlowerWhite]}, blue {t[Mat.FlowerBlue]}, yellow {t[Mat.Flower]}, violet {t[Mat.Bloom]}), " +
            $"ferns {t[Mat.Fern]}, reeds {t[Mat.Reed]}, mushrooms {t[Mat.Mushroom]}, lilies {t[Mat.Lily]}; juniper {t[Mat.Juniper]}, berries {t[Mat.BerryRed] + t[Mat.BerryBlue]}, " +
            $"nests {t[Mat.Nest]}, anthills {t[Mat.Anthill]}, hollows {t[Mat.Hollow]}";
        Console.WriteLine($"     first: {Count(s)}");
        Console.WriteLine($"     first: {Sprigs(t1)}");
        int Of(Site site, Detail k) => site.Details.Count(d => d.Kind == k);
        check("the first valley has every kind of small thing: fallen trees, stumps, bushes of three kinds, anthills, pits, a hollow tree, flowers of five colours, ferns, reeds, mushrooms",
            Of(s, Detail.FallenTree) >= 6 && Of(s, Detail.Stump) >= 8 && Of(s, Detail.Hazel) >= 10 && Of(s, Detail.BerryBush) >= 10 && Of(s, Detail.Juniper) >= 5
            && Of(s, Detail.Anthill) >= 2 && Of(s, Detail.Pit) >= 5 && Of(s, Detail.Hollow) >= 2 && Flowers.All(f => t1[f] >= 300) && t1[Mat.Fern] >= 1000 && t1[Mat.Reed] >= 200 && t1[Mat.Mushroom] >= 50,
            $"{Count(s)}; {Sprigs(t1)}");

        var kinds = new[] { ValleyKind.Forest, ValleyKind.Marsh, ValleyKind.Mountain };
        var byKind = new Dictionary<ValleyKind, (Site s, long[] t)>();
        foreach (var k in kinds)
        {
            var (wk, sk, ms) = Make(k, 11, true);
            byKind[k] = (sk, Tally(wk));
            Console.WriteLine($"     {k} ({ms} ms): {Count(sk)}");
            Console.WriteLine($"     {k}: {Sprigs(byKind[k].t)}");
        }
        var (fs, ft) = byKind[ValleyKind.Forest]; var (ms2, mt) = byKind[ValleyKind.Marsh]; var (ns, nt) = byKind[ValleyKind.Mountain];
        check("the kind of valley shows in them: more fallen trees and ferns in the forest, more reeds and lilies in the marsh, more juniper in the mountains",
            Of(fs, Detail.FallenTree) > Of(ns, Detail.FallenTree) && ft[Mat.Fern] > nt[Mat.Fern] && mt[Mat.Reed] > ft[Mat.Reed] && mt[Mat.Lily] > 0
            && Of(ns, Detail.Juniper) > Of(fs, Detail.Juniper),
            $"fallen: forest {Of(fs, Detail.FallenTree)}, mountains {Of(ns, Detail.FallenTree)}; ferns {ft[Mat.Fern]} / {nt[Mat.Fern]}; reeds: marsh {mt[Mat.Reed]}, forest {ft[Mat.Reed]}; " +
            $"lilies {mt[Mat.Lily]}; juniper: mountains {Of(ns, Detail.Juniper)}, forest {Of(fs, Detail.Juniper)}");

        // ---- where: nothing of them on the plateau, the roads, the river's bed, a named place, the ore or the strip
        int bad = 0;
        var where = new List<string>();
        foreach (var (x, y, z, a, b) in diff)
        {
            int i = z * w.SX + x;
            byte g = s.GroundKind[i];
            bool wrong = g is Relief.Plateau or Relief.Rim or Relief.Ramp or Relief.Road or Relief.River or Relief.Pond
                || Landmarks.Taken(s, x, z, 0) || s.InStrip(x, z) || Brushes.Dist(x - s.KeepX, z - s.KeepZ) < s.KeepR + 40;
            if (!wrong) continue;
            bad++;
            if (where.Count < 6) where.Add($"{Mat.NameOf(a)} → {Mat.NameOf(b)} at {x},{y},{z} (ground {g})");
        }
        check("none of it on the citadel's plateau, a road, the river's bed, a named place, the ore or the horde's strip", bad == 0,
            bad == 0 ? $"{diff.Count} voxels looked at" : $"{bad} voxels: {string.Join("; ", where)}");

        // reeds by water and lilies on it; fallen trees in the woods
        int reedsFar = 0, reeds = 0, liliesOff = 0, lilies = 0;
        bool Wet(int x, int z) => w.OnLand(x, z) && s.WaterTop[z * w.SX + x] > 0;
        foreach (var (x, y, z, a, b) in diff)
        {
            if (b == Mat.Reed && !Mat.IsSprig(w.Get(x, y - 1, z)))
            {
                reeds++;
                bool near = false;
                for (int dz = -3; dz <= 3 && !near; dz++) for (int dx = -3; dx <= 3 && !near; dx++) near = Wet(x + dx, z + dz);
                if (!near) reedsFar++;
            }
            if (b == Mat.Lily) { lilies++; if (w.Get(x, y - 1, z) != Mat.Water) liliesOff++; }
        }
        // (in the woods: crowns over a tenth of the ground within 6 m of its foot)
        int fallenOpen = 0;
        foreach (var d in s.Details.Where(d => d.Kind == Detail.FallenTree))
        {
            int crowns = 0, all = 0;
            for (int dz = -48; dz <= 48; dz += 4)
                for (int dx = -48; dx <= 48; dx += 4)
                {
                    int x = d.X + dx, z = d.Z + dz;
                    if (!w.OnLand(x, z)) continue;
                    all++;
                    int top = w.ColTop(x, z);
                    if (top > s.HTop[z * w.SX + x] + 16 && Mat.IsLeafy(w.Get(x, top - 1, z))) crowns++;
                }
            if (crowns * 10 < all) fallenOpen++;
        }
        check("reeds stand by the water, lilies lie on it, and the fallen trees lie in the woods",
            reeds > 0 && reedsFar == 0 && liliesOff == 0 && fallenOpen == 0,
            $"{reeds} stands of reeds, {reedsFar} away from water; {lilies} lilies, {liliesOff} not on water; {fallenOpen} fallen trees out in the open");

        // ---- the same seed, the same valley
        var (w2, s2, _) = Make(ValleyKind.First, 7, true);
        int same = Diff(w, w2).Count;
        check("the same seed makes the same small things", same == 0 && s2.Details.Count == s.Details.Count, $"{same} voxels differ, {s.Details.Count} / {s2.Details.Count} things");

        // ---- no ground left hanging by what was dug for them; the puddles held in
        int hanging = 0, leaks = 0, puddles = 0;
        var hangingAt = new List<string>();
        foreach (var d in s.Details.Where(d => d.Kind is Detail.Pit or Detail.Burrow or Detail.FallenTree))
        {
            int r = d.Kind == Detail.FallenTree ? 14 : d.R + 4;
            int cx = d.X - (int)(d.DX * 6), cz = d.Z - (int)(d.DZ * 6);
            for (int z = cz - r; z <= cz + r; z++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (!w.OnLand(x, z)) continue;
                    // earth, turf or rock in the air: nothing under it and no ground beside it to hold to
                    int top = w.ColTop(x, z);
                    for (int y = top - 1; y > s.HTop[z * w.SX + x] - 12 && y > 4; y--)
                    {
                        byte m = w.Get(x, y, z);
                        if (!(Mat.IsGrass(m) || m is Mat.Soil or Mat.Rock or Mat.Moss) || w.Get(x, y - 1, z) != Mat.Air) continue;
                        bool held = false;
                        foreach (var (ox, oz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                            if (w.Get(x + ox, y, z + oz) is var b && b != Mat.Air && b != Mat.Water && !Mat.IsSprig(b) && !Mat.IsLeafy(b)) held = true;
                        // (an overhang that was there before, as a boulder's, is none of theirs)
                        if (held || w0.Get(x, y, z) == m && w0.Get(x, y - 1, z) == Mat.Air) continue;
                        hanging++;
                        if (hangingAt.Count < 5) hangingAt.Add($"{Mat.NameOf(m)} at {x},{y},{z} ({d.Kind} at {d.X},{d.Z}, ground {s.HTop[z * w.SX + x]})");
                    }
                    int i = z * w.SX + x;
                    if (s.WaterTop[i] > 0 && s0.WaterTop[i] == 0)
                    {
                        puddles++;
                        foreach (var (ox, oz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int j = i + oz * w.SX + ox;
                            if (s.WaterTop[j] == 0 && s.HTop[j] < s.WaterTop[i]) leaks++;
                        }
                    }
                }
        }
        check("no turf or earth left hanging over a pit or a burrow, and every puddle held in on all sides", hanging == 0 && leaks == 0,
            $"{hanging} hanging{(hangingAt.Count > 0 ? " (" + string.Join("; ", hangingAt) + ")" : "")}, {puddles} columns of puddles, {leaks} sides open");

        // ---- a fallen tree is timber, a bush burns
        var sim = new Sim(w, s);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        var fall = s.Details.Where(d => d.Kind == Detail.FallenTree).OrderBy(d => Brushes.Dist(d.X - s.KeepX, d.Z - s.KeepZ)).FirstOrDefault();
        VoxelList plan = null;
        if (fall.R > 0)
        {
            for (int t = 6; t < fall.R && plan == null; t += 2)
            {
                int x = fall.X + (int)Math.Round(fall.DX * t), z = fall.Z + (int)Math.Round(fall.DZ * t);
                for (int y = s.HTop[z * w.SX + x] - 2; y < s.HTop[z * w.SX + x] + 10 && plan == null; y++)
                    if (Mat.IsTrunk(w.Get(x, y, z))) plan = Shapes.Tree(w, x, y, z);
            }
        }
        int logWood = 0;
        if (plan != null) for (int i = 0; i < plan.Count; i++) if (Mat.IsTrunk(w.Get(plan.X(i), plan.Y(i), plan.Z(i)))) logWood++;
        double wood0 = sim.Stock[Stockpile.Wood];
        int left = -1;
        if (plan != null)
        {
            for (int i = 0; i < 4; i++) sim.Spawn(Kinds.Worker, sim.NearestStand(fall.X + (int)(fall.DZ * 16), fall.Z - (int)(fall.DX * 16), Kinds.Worker.Move.Height));
            sim.DesignateRemove(plan);
            for (double t = 0; t < 400 && sim.Orders.Total > 0; t += .05) sim.Step(.05f);
            left = 0;
            for (int i = 0; i < plan.Count; i++) if (Mat.IsTrunk(w.Get(plan.X(i), plan.Y(i), plan.Z(i)))) left++;
        }
        double got = sim.Stock[Stockpile.Wood] - wood0;
        check("a fallen tree is sawn up for logs as a felled one is", plan != null && logWood > 300 && left == 0 && got >= logWood * 3,
            plan == null ? "no fallen tree found" : $"a log of {logWood} voxels of wood at {fall.X},{fall.Z}, {left} left after the workers; logs +{got:0}");

        var bush = s.Details.FirstOrDefault(d => d.Kind == Detail.Hazel);
        int leaves0 = 0, leaves1 = 0;
        if (bush.R > 0)
        {
            int g = s.HTop[bush.Z * w.SX + bush.X];
            int Leaves()
            {
                int n = 0;
                for (int y = g - 2; y < g + 30; y++) for (int z = bush.Z - 12; z <= bush.Z + 12; z++) for (int x = bush.X - 12; x <= bush.X + 12; x++)
                    if (Mat.IsLeafy(w.Get(x, y, z))) n++;
                return n;
            }
            leaves0 = Leaves();
            for (int y = g; y < g + 4; y++) if (Mat.IsLeafy(w.Get(bush.X, y, bush.Z))) { sim.Ignite(bush.X, y, bush.Z); break; }
            for (double t = 0; t < 40; t += .05) sim.Step(.05f);
            leaves1 = Leaves();
        }
        check("a hazel bush set alight burns down", bush.R > 0 && leaves0 > 100 && leaves1 < leaves0 / 4, $"{leaves0} leaves before, {leaves1} after 40 s");

        // ---- a lily goes with the water under it: one laid on a puddle, the puddle let out by a trench cut under it and away
        int lx = -1, lz = -1, level = 0;
        for (int i = 0; i < s.WaterTop.Length && lx < 0; i++)
            if (s.WaterTop[i] > 0 && s0.WaterTop[i] == 0 && w.Get(i % w.SX, s.WaterTop[i], i / w.SX) == Mat.Air
                && w.Get(i % w.SX, s.WaterTop[i] - 1, i / w.SX) == Mat.Water) (lx, lz, level) = (i % w.SX, i / w.SX, s.WaterTop[i]);
        bool laid = false, gone = false, drained = false;
        if (lx >= 0)
        {
            laid = w.Set(lx, level, lz, Mat.Lily);
            // the trench: from under the lily out one way, deeper than the puddle and wider than it holds
            for (int t = 0; t <= 20; t++)
                for (int q = -2; q <= 2; q++)
                    for (int y = level - 6; y < level + 2; y++)
                        if (w.Get(lx + t, y, lz + q) is var m && m is not (Mat.Air or Mat.Water or Mat.Bed or Mat.Lily)) w.Set(lx + t, y, lz + q, Mat.Air);
            for (double t = 0; t < 30 && !gone; t += .05) { sim.Step(.05f); gone = w.Get(lx, level, lz) != Mat.Lily; }
            drained = w.Get(lx, level - 1, lz) != Mat.Water;
        }
        check("a lily on a puddle goes with the water when the puddle is let out", laid && drained && gone,
            lx < 0 ? "no puddle found" : $"lily at {lx},{level},{lz}: laid {laid}, water under it gone {drained}, lily gone {gone}");
    }

}
