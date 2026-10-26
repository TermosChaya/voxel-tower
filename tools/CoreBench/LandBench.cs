using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The map of valleys as a land (--atlas, Atlas.cs; the heroes' walking on it is AtlasBench.cs): how long it takes to make, the big map and the small; every kind of
/// cell shows as its kind (woods in a forest, peaks with snow in the mountains, water in a river's valley, rock in an ore
/// one); the rivers and the roads run on over the sides they cross; one may walk from the castle to every lair and to the
/// middle of every cell; the same campaign makes the same land; a cell put on again as its holder changed is the land a
/// new map with that holder makes, and only its chunks and those about it change. --atlas-dump seed rings file: the map
/// seen from straight above, a picture.
/// </summary>
static class LandBench
{
    static int Count(Atlas a, MapCell c, Func<byte, bool> what, out int top)
    {
        var hex = a.HexOf(c);
        var (cx, cz) = a.Center(c);
        int n = 0;
        top = 0;
        for (int z = (int)(cz - Atlas.CellR); z <= (int)(cz + Atlas.CellR); z++)
            for (int x = (int)(cx - hex.HalfW); x <= (int)(cx + hex.HalfW); x++)
            {
                if (a.CellOfColumn(x, z) != c) continue;
                for (int y = 32; y < Atlas.Height; y++)
                {
                    byte m = a.World.Get(x, y, z);
                    if (m == Mat.Air) continue;
                    if (what(m)) n++;
                    if (m != Mat.Air && !Mat.IsVeg[m]) top = Math.Max(top, y + 1);
                }
            }
        return n;
    }

    static bool Same(VoxelWorld a, VoxelWorld b, out int differ, out (int x, int y, int z) first)
    {
        differ = 0; first = (-1, -1, -1);
        for (int ci = 0; ci < a.ChunkCount; ci++)
        {
            var da = a.ChunkData(ci); var db = b.ChunkData(ci);
            if (da == null && db == null && a.ChunkUniform(ci) == b.ChunkUniform(ci)) continue;
            a.ChunkCoords(ci, out int cx, out int cy, out int cz);
            for (int y = 0; y < VoxelWorld.CS; y++)
                for (int z = 0; z < VoxelWorld.CS; z++)
                    for (int x = 0; x < VoxelWorld.CS; x++)
                    {
                        int wx = cx * 32 + x, wy = cy * 32 + y, wz = cz * 32 + z;
                        if (a.Get(wx, wy, wz) == b.Get(wx, wy, wz)) continue;
                        if (differ++ == 0) first = (wx, wy, wz);
                    }
        }
        return differ == 0;
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        // ---- how long, the big map and the small
        long big = long.MaxValue, small = long.MaxValue;
        Atlas a = null;
        for (int k = 0; k < 3; k++)
        {
            var c3 = Campaign.New(11, 3);
            var sw = Stopwatch.StartNew();
            a = Atlas.Build(c3);
            big = Math.Min(big, sw.ElapsedMilliseconds);
            var c2 = Campaign.New(11, 2);
            sw.Restart();
            Atlas.Build(c2);
            small = Math.Min(small, sw.ElapsedMilliseconds);
        }
        int chunks = a.World.AllocatedChunks;
        check("the map of valleys is made as a land in good time: the big map under 2 s", big < 2000,
            $"37 cells {a.SX}×{a.SZ}×{Atlas.Height} voxels in {big} ms, 19 cells in {small} ms; {chunks} chunks hold voxels");

        // ---- every kind of cell shows as its kind
        foreach (uint seed in new uint[] { 11, 23, 5 })
        {
            var c = Campaign.New(seed, 3);
            var at = Atlas.Build(c);
            var bad = new List<string>();
            int snowAll = 0;
            foreach (var cell in c.Cells)
            {
                int trees = Count(at, cell, m => Mat.IsTrunk(m), out int top);
                int snow = Count(at, cell, m => m == Mat.Snow, out _);
                int water = Count(at, cell, m => m == Mat.Water, out _);
                int rock = Count(at, cell, m => m is Mat.Rock or Mat.StoneDark, out _);
                snowAll += snow;
                bool ok = cell.Kind switch
                {
                    ValleyKind.Forest => trees > 120,
                    ValleyKind.Mountain => top > Atlas.Ground + 22 && rock > 20000,
                    ValleyKind.Marsh => water > 400,
                    ValleyKind.Ore => rock > 400,
                    _ => true,
                };
                if (!ok) bad.Add($"{Valleys.Key(cell.Kind)} {cell.Q},{cell.R}: trunks {trees}, top {top}, snow {snow}, water {water}, rock {rock}");
            }
            check($"seed {seed}: every cell shows as its kind — woods in a forest, peaks of rock in the mountains, water in a river's valley, rock in an ore one; snow on the highest",
                bad.Count == 0 && snowAll > 30, (bad.Count == 0 ? $"{c.Cells.Count} cells" : string.Join("; ", bad.Take(4))) + $"; snow on the field {snowAll} voxels");

            // ---- the rivers and the roads run on over the sides
            int rivers = 0, roads = 0;
            var broken = new List<string>();
            foreach (var cell in c.Cells)
                for (int s = 0; s < 6; s++)
                {
                    var n = c.Neighbour(cell, s);
                    if (n == null || c.Cells.IndexOf(n) < c.Cells.IndexOf(cell)) continue;
                    var hex = at.HexOf(cell);
                    var (ix, iz) = hex.Inward(s);
                    bool Has((float x, float z) p, float off, Func<int, int, bool> what)
                    {
                        for (int dz = -2; dz <= 2; dz++)
                            for (int dx = -2; dx <= 2; dx++)
                                if (what((int)(p.x + ix * off) + dx, (int)(p.z + iz * off) + dz)) return true;
                        return false;
                    }
                    bool Wet(int x, int z) => at.World.Get(x, Atlas.WaterLevel - 1, z) == Mat.Water;
                    bool Dusty(int x, int z) => at.World.Get(x, at.World.SurfAt(x, z) - 1, z) is Mat.Dust or Mat.Gravel || Wet(x, z);
                    if (cell.Edges[s] is { River: >= 0 })
                    {
                        rivers++;
                        var p = hex.SidePoint(s, Campaign.RiverAt(cell, s));
                        if (!Has(p, 3, Wet) || !Has(p, -3, Wet)) broken.Add($"river {cell.Q},{cell.R} side {s}");
                    }
                    if (cell.Edges[s] is { Road: >= 0 } || n.Edges[(s + 3) % 6] is { Road: >= 0 })
                    {
                        roads++;
                        var p = hex.SidePoint(s, .5f);
                        if (!Has(p, 3, Dusty) || !Has(p, -3, Dusty)) broken.Add($"road {cell.Q},{cell.R} side {s}");
                    }
                }
            check($"seed {seed}: the rivers and the roads run on from cell to cell over the sides they cross", broken.Count == 0 && rivers > 0 && roads > 0,
                $"{rivers} sides with a river, {roads} with a road{(broken.Count > 0 ? "; broken: " + string.Join(", ", broken.Take(5)) : "")}");

            // ---- one may walk from the castle to every lair and every cell's middle
            var home = at.ThingIn(c.Home, AtlasThingKind.Citadel);
            var missing = new List<string>();
            int onRoad = 0, steps = 0;
            foreach (var cell in c.Cells)
            {
                var (cx, cz) = at.Center(cell);
                if (cell.Home) continue;
                var goal = at.ThingIn(cell, AtlasThingKind.Lair, AtlasThingKind.Outpost, AtlasThingKind.OutpostPlace, AtlasThingKind.HordeCamp);
                int gx = goal?.X ?? (int)cx, gz = goal?.Z ?? (int)cz;
                if (!at.Walkable(gx, gz)) { missing.Add($"{cell.Q},{cell.R} its front {gx},{gz} not to be walked"); continue; }
                var path = at.Walk(home.X, home.Z, gx, gz);
                if (path == null) { missing.Add($"{cell.Q},{cell.R}"); continue; }
                if (cell.Holding == Holding.HordeTown) { steps += path.Count; foreach (var (px, pz) in path) if (at.Cost(px, pz) < .7f) onRoad++; }
            }
            check($"seed {seed}: from the castle's gate one walks to every lair and to the middle of every cell, the lairs much of the way by road",
                home != null && missing.Count == 0 && onRoad * 3 > steps,
                $"{c.Cells.Count} cells; to the lairs {steps} steps, {onRoad} of them on a road{(missing.Count > 0 ? "; no way to " + string.Join(", ", missing.Take(5)) : "")}");
        }

        // ---- the same campaign the same land
        {
            var x1 = Atlas.Build(Campaign.New(23, 3));
            var x2 = Atlas.Build(Campaign.New(23, 3));
            check("the same campaign makes the same land", Same(x1.World, x2.World, out int differ, out var first), differ == 0 ? "" : $"{differ} voxels differ, the first at {first}");
        }

        // ---- a cell put on again as its holder changed: the land a new map with that holder makes; only its chunks change
        {
            var c = Campaign.New(5, 3);
            var at = Atlas.Build(c);
            var ore = c.Cells.Find(x => x.Kind == ValleyKind.Ore && Campaign.Distance(0, 0, x.Q, x.R) == 1)
                ?? c.Cells.Find(x => !x.Home && x.Owner == Owner.None);
            var lair = c.Cells.Find(x => x.Holding == Holding.HordeTown);
            var trove = c.Cells.Find(x => x.Trove != TroveKind.None);
            at.World.TakeDirty(new List<int>());
            ore.Owner = Owner.Ours;
            var sw = Stopwatch.StartNew();
            at.Refresh(ore);
            long ms = sw.ElapsedMilliseconds;
            var dirty = new List<int>();
            at.World.TakeDirty(dirty);
            var fresh = Atlas.Build(c);
            bool same1 = Same(at.World, fresh.World, out int d1, out var f1);
            bool tower = at.ThingIn(ore, AtlasThingKind.Outpost) != null;
            // the horde takes a cell next to the first valley: the ash comes over into it
            var near = c.Cells.Find(x => !x.Home && x != ore && Campaign.Distance(0, 0, x.Q, x.R) == 1);
            int ash0 = Count(at, near, m => m is Mat.Ash or Mat.Lava, out _);
            near.Owner = Owner.Horde;
            at.Refresh(near);
            int ash1 = Count(at, near, m => m is Mat.Ash or Mat.Lava, out _), homeAsh = Count(at, c.Home, m => m == Mat.Ash, out _);
            trove.TroveTaken = true;
            at.Refresh(trove);
            fresh = Atlas.Build(c);
            bool same2 = Same(at.World, fresh.World, out int d2, out var f2);
            check("a cell put on again as its holder changed is the land a new map makes: an outpost on a cell taken, ash over one the horde took, a treasure taken; only its chunks and those about it made again",
                same1 && same2 && tower && ash1 > ash0 + 500 && homeAsh > 0 && dirty.Count < a.World.ChunkCount / 4 && ms < 200,
                $"taken in {ms} ms, {dirty.Count} chunks of {at.World.ChunkCount}; outpost {tower}; ash in the cell the horde took {ash0} → {ash1}, in the first valley {homeAsh}; " +
                $"differ from a new map: {d1} {(d1 > 0 ? f1.ToString() : "")}, {d2} {(d2 > 0 ? $"{f2}: {Mat.NameOf(at.World.Get(f2.x, f2.y, f2.z))} against {Mat.NameOf(fresh.World.Get(f2.x, f2.y, f2.z))}, in {at.CellOfColumn(f2.x, f2.z)?.Q},{at.CellOfColumn(f2.x, f2.z)?.R}; the horde took {near.Q},{near.R}, the treasure {trove.Q},{trove.R}" : "")}");
        }
    }

    /// <summary>How many of the big maps of the first seeds have their rivers laid (--atlas-rivers).</summary>
    public static void Rivers()
    {
        int laid = 0, n = 0;
        var sw = Stopwatch.StartNew();
        for (uint seed = 1; seed <= 60; seed++)
        {
            var c = Campaign.New(seed, 3);
            n++;
            if (c.Cells.Exists(x => Array.Exists(x.Edges, e => e is { River: >= 0 }))) laid++;
            else Console.Write($"{seed} ");
        }
        Console.WriteLine($"\nrivers laid on {laid} of {n} big maps ({sw.ElapsedMilliseconds} ms)");
    }

    /// <summary>Where one may walk, a picture (--atlas-walkdump seed rings file): black no, yellow a road, cyan a ford, green by the cell's hours.</summary>
    public static void WalkDump(uint seed, int rings, string file)
    {
        var a = Atlas.Build(Campaign.New(seed, rings));
        using var f = File.Create(file);
        f.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{a.SX} {a.SZ}\n255\n"));
        var px = new byte[a.SX * a.SZ * 3];
        for (int z = 0; z < a.SZ; z++)
            for (int x = 0; x < a.SX; x++)
            {
                float c = a.Cost(x, z);
                (byte r, byte g, byte b) col = float.IsInfinity(c) ? ((byte)0, (byte)0, (byte)0) : c < .7f ? ((byte)230, (byte)200, (byte)90) : c > 3 ? ((byte)60, (byte)220, (byte)230) : ((byte)40, (byte)(200 - (c - 1) * 160), (byte)60);
                int i = (z * a.SX + x) * 3;
                px[i] = col.r; px[i + 1] = col.g; px[i + 2] = col.b;
            }
        f.Write(px);
    }

    /// <summary>The map seen from straight above, each column the colour of what is highest on it, lit from the north-west; a PPM picture.</summary>
    public static void Dump(uint seed, int rings, string file)
    {
        var c = Campaign.New(seed, rings);
        var sw = Stopwatch.StartNew();
        var a = Atlas.Build(c);
        Console.WriteLine($"atlas {a.SX}×{a.SZ} made in {sw.ElapsedMilliseconds} ms; {a.Things.Count} things");
        var w = a.World;
        var top = new int[a.SX * a.SZ];
        var mat = new byte[a.SX * a.SZ];
        for (int z = 0; z < a.SZ; z++)
            for (int x = 0; x < a.SX; x++)
            {
                int y = w.ColTop(x, z) - 1;
                top[z * a.SX + x] = y;
                mat[z * a.SX + x] = y >= 0 ? w.Get(x, y, z) : Mat.Air;
            }
        using var f = File.Create(file);
        var head = System.Text.Encoding.ASCII.GetBytes($"P6\n{a.SX} {a.SZ}\n255\n");
        f.Write(head);
        var px = new byte[a.SX * a.SZ * 3];
        for (int z = 0; z < a.SZ; z++)
            for (int x = 0; x < a.SX; x++)
            {
                int i = z * a.SX + x;
                byte m = mat[i];
                uint rgb = m == Mat.Water ? 0x2f6fa8u : Mat.Def[m].Srgb;
                float light = 1;
                if (x > 0 && z > 0) light += (top[i] - top[i - a.SX - 1]) * .08f;
                light *= .75f + .25f * Math.Clamp(top[i] / 80f, 0, 1.4f);
                if (a.OnField(x, z) && a.CellOfColumn(x, z) is { } cell && a.HexOf(cell).Edge(x + .5f, z + .5f) < .7f) light *= .82f;
                byte R(int sh) => (byte)Math.Clamp(((rgb >> sh) & 255) * light, 0, 255);
                px[i * 3] = R(16); px[i * 3 + 1] = R(8); px[i * 3 + 2] = R(0);
            }
        f.Write(px);
    }
}
