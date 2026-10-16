using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoxelTower.Core;

// Checks the engine-free core against numbers measured in the browser prototype, and times the heavy parts.
int failures = 0;
void Check(string what, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}{(detail.Length > 0 ? "  — " + detail : "")}");
    if (!ok) failures++;
}

var sw = Stopwatch.StartNew();
var w = new VoxelWorld(WorldGen.SizeX, WorldGen.SizeY, WorldGen.SizeZ);
var site = WorldGen.Generate(w);
long genMs = sw.ElapsedMilliseconds;

// --dump <file> [chunk ids...]: per-chunk voxel counts and hashes (and raw chunks) to compare with the prototype
if (args.Length >= 2 && args[0] == "--dump")
{
    int n = w.ChunkCount; var cnt = new int[n]; var hs = new uint[n];
    for (int y = 0; y < w.SY; y++)
        for (int z = 0; z < w.SZ; z++)
            for (int x = 0; x < w.SX; x++)
            {
                byte m = w.Get(x, y, z); if (m == 0) continue;
                int ci = w.ChunkIndex(x >> 5, y >> 5, z >> 5);
                cnt[ci]++;
                uint li = (uint)(((y & 31) << 10) | ((z & 31) << 5) | (x & 31));
                unchecked { hs[ci] = hs[ci] * 31 + (li * 64 + m); }
            }
    var sb = new System.Text.StringBuilder();
    sb.Append("{\"cnt\":[").Append(string.Join(",", cnt)).Append("],\"hs\":[").Append(string.Join(",", hs)).Append("],\"raw\":{");
    for (int a = 2; a < args.Length; a++)
    {
        int ci = int.Parse(args[a]); w.ChunkCoords(ci, out int cx, out int cy, out int cz);
        var bytes = new byte[VoxelWorld.CS3];
        for (int y = 0; y < 32; y++) for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++)
            bytes[(y << 10) | (z << 5) | x] = w.Get(cx * 32 + x, cy * 32 + y, cz * 32 + z);
        sb.Append(a > 2 ? "," : "").Append('"').Append(ci).Append("\":\"").Append(Convert.ToBase64String(bytes)).Append('"');
    }
    sb.Append("},\"quads\":[");
    var qm = new GreedyMesher(); var qpad = new byte[34 * 34 * 34];
    for (int ci = 0; ci < n; ci++) { var mb = qm.MeshChunk(w, ci, qpad); sb.Append(ci > 0 ? "," : "").Append(mb?.Quads ?? 0); }
    sb.Append("]}");
    System.IO.File.WriteAllText(args[1], sb.ToString());
    Console.WriteLine($"dumped to {args[1]}");
    return 0;
}
int freed = w.Compact();
Console.WriteLine($"world {w.SX}×{w.SY}×{w.SZ} = {(long)w.SX * w.SY * w.SZ / 1e6:0.0} M voxels, generated in {genMs} ms; {w.AllocatedChunks}/{w.ChunkCount} chunks hold data ({freed} collapsed)");

// the same numbers the prototype showed after the tower fix
int walk = w.SurfAt(site.WallX + 8, 70);
Check("wall walk height", walk == 104, $"{walk}, prototype 104");
int t1 = w.SurfAt(site.WallX + 6, 170), t2 = w.SurfAt(site.WallX + 6, 18);
Check("tower platforms", t1 == 120 && t2 == 115, $"{t1} and {t2}, prototype 120 and 115");
Check("keep base", site.KeepBase >= 70 && site.KeepBase <= 76, site.KeepBase.ToString());
Check("torches", site.Torches.Count == 3, site.Torches.Count.ToString());

// meshing, one thread
var mesher = new GreedyMesher();
var pad = new byte[34 * 34 * 34];
sw.Restart();
long quads = 0; int meshed = 0;
for (int ci = 0; ci < w.ChunkCount; ci++) { var mb = mesher.MeshChunk(w, ci, pad); if (mb != null) { quads += mb.Quads; meshed++; } }
long oneMs = sw.ElapsedMilliseconds;
Check("triangles match the prototype", quads * 2 == 232642, $"{quads * 2} triangles in {meshed} chunks, prototype 232642");
Console.WriteLine($"     mesh, 1 thread: {oneMs} ms");

// meshing, all cores
sw.Restart();
long pq = 0;
Parallel.For(0, w.ChunkCount, () => (new GreedyMesher(), new byte[34 * 34 * 34]), (ci, _, loc) =>
{
    var mb = loc.Item1.MeshChunk(w, ci, loc.Item2);
    if (mb != null) Interlocked.Add(ref pq, mb.Quads);
    return loc;
}, _ => { });
Console.WriteLine($"     mesh, {Environment.ProcessorCount} threads: {sw.ElapsedMilliseconds} ms");
Check("parallel mesh agrees", pq == quads);

// a ray straight down onto the plateau, and one that misses
{
    int x = 270, z = 40;
    bool hit = Raycast.Cast(w, x + .5, w.SY + 20, z + .5, 0, -1, 0, out var h);
    Check("ray down hits the surface", hit && h.Y + 1 == w.ColTop(x, z) && h.NY == 1, hit ? $"y {h.Y}, {Mat.NameOf(h.M)}" : "miss");
    bool miss = Raycast.Cast(w, -50, 300, -50, 0, 1, 0, out _);
    Check("ray into the sky misses", !miss);
}

// brushes and the treasury
var wal = new Wallet { Gold = 5000, Earth = 500 };
{
    var L = Brushes.Wall(w, site, 140, 40, 140, 88, Brushes.StoneWall);
    var c = Builder.CostOf(L);
    var r = Builder.TryBuild(w, wal, L, out _);
    Check("stone wall builds", r == BuildResult.Ok && c.Stone > 0, $"{L.Count} voxels, {c.Gold} gold");
    var again = Builder.TryBuild(w, wal, Brushes.Wall(w, site, 140, 40, 140, 88, Brushes.StoneWall), out _);
    Check("a second wall on top stacks higher", again == BuildResult.Ok);
}
{
    var L = Brushes.Tower(w, site, 120, 120, 16, 56);
    var r = Builder.TryBuild(w, wal, L, out var c);
    int top = w.SurfAt(126, 120);
    Check("tower stands on the ground", r == BuildResult.Ok && top == L.MetaPlat, $"platform {L.MetaPlat}, top {top}, {c.Gold} gold, {Fmt.M3(c.Earth)} m³ earth");
}
{
    int top = w.SurfAt(site.WallX + 8, 120);
    var L = Brushes.Stairs(w, site, site.WallX + 8, 120, top, site.WallX + 40, 120, Mat.Wood);
    Check("stairs plan", L != null && L.Count > 0, L?.Count.ToString() ?? "null");
}
{
    double earth0 = wal.Earth;
    var r = Builder.Dig(w, site, wal, 100, 60, 18, 7, out int ev);
    Check("digging fills the earth store", r == Builder.DigResult.Ok && wal.Earth > earth0, $"+{Fmt.M3(ev)} m³");
    int dug = w.SurfAt(100, 60);
    Check("the moat is 1 m deep or stops at rock", dug <= 26, dug.ToString());
}
{
    var L = Brushes.Fill(w, site, 60, 100, w.SurfAt(60, 100) + 8, 7);
    var r = Builder.TryBuild(w, wal, L, out var c);
    Check("fill uses earth", r == BuildResult.Ok && c.Earth > 0, Fmt.M3(c.Earth));
}
{
    int y = w.SurfAt(140, 60);
    int n = Builder.RemoveSphere(w, wal, 140, y - 4, 60, 7);
    Check("remove takes masonry apart", n > 0, n.ToString());
    int n2 = Builder.RemoveSphere(w, wal, 270, w.SurfAt(270, 40) - 2, 40, 7);
    Check("remove leaves nature alone", n2 == 0);
}
{
    var bp = Blueprint.Capture(w, site.WallX - 12, 100, site.WallX + 11, 147, 1, "Пролёт вала, 3 м");
    Check("blueprint of the wall span", bp != null && bp.Stone > 0 && bp.Earth > 0, bp == null ? "null" : $"{bp.W}×{bp.D}×{bp.H}, {bp.Describe()}, {bp.Cost.Gold} gold");
    var plan = bp.PlanPaste(w, site, 60, 20, 1, w.SurfAt(70, 30));
    var r = Builder.TryBuild(w, wal, plan, out var c);
    Check("blueprint pastes rotated", r == BuildResult.Ok && c.Voxels > 0, $"{c.Voxels} voxels");
}
{
    sw.Restart(); int remeshed = 0;
    var list = new System.Collections.Generic.List<int>();
    w.TakeDirty(list);
    foreach (int ci in list) if (mesher.MeshChunk(w, ci, pad) != null) remeshed++;
    Console.WriteLine($"     re-mesh after the edits: {list.Count} chunks in {sw.ElapsedMilliseconds} ms");
}

// ---- drawing brushes, on the map the game starts with: no ready-made buildings
Console.WriteLine();
var w2 = new VoxelWorld(WorldGen.SizeX, WorldGen.SizeY, WorldGen.SizeZ);
var site2 = WorldGen.Generate(w2, starterFort: false);
w2.Compact();
var wal2 = new Wallet { Gold = 9000, Earth = 900 };
Check("the map starts bare", w2.SurfAt(site2.WallX + 8, 70) < 90 && site2.Torches.Count == 3, $"ground {w2.SurfAt(site2.WallX + 8, 70)}, {site2.Torches.Count} lights");
{
    var st = new Stroke { Mat = Mat.Stone, Width = 16, Height = 32, Level = true };
    int lt = Shapes.LevelTop(w2, 250, 40, st.Height);
    var L = Shapes.Line(w2, site2, 250, 40, 250, 100, st, lt);
    int mortar = 0; for (int i = 0; i < L.Count; i++) if (L.M(i) == Mat.Mortar) mortar++;
    var r = Builder.TryBuild(w2, wal2, L, out var c);
    Check("line lays a level wall", r == BuildResult.Ok && w2.SurfAt(250, 70) == lt && w2.SurfAt(250, 98) == lt, $"top {lt}, {L.Count} voxels");
    Check("stone goes down coursed", mortar > L.Count / 5 && mortar < L.Count / 2, $"{mortar} mortar of {L.Count}");
    Check("price is counted per voxel", c.Gold == (int)Math.Ceiling(c.Stone * Prices.Stone / VoxelWorld.PerM3), $"{c.Stone} stone voxels = {c.Gold} gold");

    var wood = st; wood.Mat = Mat.Wood; wood.Height = 8;
    int lt2 = Shapes.LevelTop(w2, 250, 70, wood.Height);
    Builder.TryBuild(w2, wal2, Shapes.Line(w2, site2, 250, 40, 250, 100, wood, lt2), out _);
    Check("a wooden course on top of the stone", w2.Get(250, lt2 - 1, 70) == Mat.Wood && w2.Get(250, lt - 1, 70) != Mat.Wood, $"top {lt2}");
}
{
    var st = new Stroke { Mat = Mat.Stone, Width = 4, Height = 40, Level = true, Hollow = true };
    int g = w2.SurfAt(285, 155);
    var r = Builder.TryBuild(w2, wal2, Shapes.Box(w2, site2, 270, 140, 300, 170, st, Shapes.LevelTop(w2, 270, 140, 40)), out _);
    Check("hollow rectangle leaves the inside open", r == BuildResult.Ok && w2.SurfAt(285, 155) == g && w2.SurfAt(271, 155) > g + 30);
}
{
    var st = new Stroke { Mat = Mat.Stone, Width = 4, Height = 48, Level = true, Hollow = true };
    int lt = Shapes.LevelTop(w2, 280, 60, 48);
    var r = Builder.TryBuild(w2, wal2, Shapes.Circle(w2, site2, 280, 60, 14, st, lt), out _);
    Check("ring of a round tower", r == BuildResult.Ok && w2.SurfAt(280 + 12, 60) == lt && w2.SurfAt(280, 60) < lt - 30);
    var hit = new VoxelHit { X = 292, Y = lt - 1, Z = 60, NY = 1, M = w2.Get(292, lt - 1, 60) };
    var L = Shapes.Extrude(w2, site2, hit, 8, Mat.Wood, 6000, out bool tooBig);
    Builder.TryBuild(w2, wal2, L, out _);
    Check("extrude raises the whole ring top", !tooBig && w2.SurfAt(292, 60) == lt + 8 && w2.SurfAt(268, 60) == lt + 8, $"{L?.Count} voxels");
}
{
    int y = w2.SurfAt(250, 60) - 8 - 4;   // inside the stone, under the wooden course
    var L = Shapes.Repaint(w2, 250, y, 60, 3, Mat.Wood);
    var r = Builder.TryRepaint(w2, wal2, L, out var c, out double refund);
    Check("repaint turns stone to wood", r == BuildResult.Ok && w2.Get(250, y, 60) == Mat.Wood && refund > 0, $"{L.Count} voxels, paid {c.Gold}, back {refund:0.00}");
    int n = Builder.RemoveSphere(w2, wal2, 250, y, 60, 2.0);
    Check("eraser takes own voxels out", n > 0 && w2.Get(250, y, 60) == 0, n.ToString());
    var ball = Shapes.Ball(w2, site2, 200, w2.SurfAt(200, 40) + 2, 40, 2, Mat.Rammed);
    Check("ball nib", ball.Count == 33, ball.Count.ToString());
    Check("one-voxel nib", Shapes.Ball(w2, site2, 200, 120, 40, Shapes.NibRadius(1), Mat.Stone).Count == 1);
}
var cols = new System.Collections.Generic.List<int>();
Check("edited columns are reported for the map", w2.TakeDirtyColumns(cols) > 0, $"{cols.Count} columns");

// ---- the big valley the game is played in
Console.WriteLine();
var variants = args.Length > 0 && args[0] == "--sizes"
    ? new[] { (768, "as is", new MapParams()), (1024, "as is", new MapParams()), (1280, "as is", new MapParams()) }
    : args.Length > 0 && args[0] == "--costs"
    ? new[] { (1024, "as is", new MapParams()), (1024, "no trees", new MapParams { TreeDensity = 0 }), (1024, "no tufts", new MapParams { TuftChance = 0 }), (1024, "smooth", new MapParams { Roughness = 0 }), (1024, "bare", new MapParams { TreeDensity = 0, TuftChance = 0, Roughness = 0 }) }
    : new[] { (1024, "game", new MapParams()) };
foreach (var (size, label, mp) in variants)
{
    mp.SizeX = mp.SizeZ = size;
    Console.Write(label + ": ");
    long mem0 = GC.GetTotalMemory(true);
    sw.Restart();
    var w3 = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
    var site3 = MapGen.Generate(w3, mp);
    long gen3 = sw.ElapsedMilliseconds;
    int freed3 = w3.Compact();
    long mem = GC.GetTotalMemory(true) - mem0;
    sw.Restart();
    long q3 = 0; int meshed3 = 0;
    Parallel.For(0, w3.ChunkCount, () => (new GreedyMesher(), new byte[34 * 34 * 34]), (ci, _, loc) =>
    {
        var mb = loc.Item1.MeshChunk(w3, ci, loc.Item2);
        if (mb != null) { Interlocked.Add(ref q3, mb.Quads); Interlocked.Increment(ref meshed3); }
        return loc;
    }, _ => { });
    long mesh3 = sw.ElapsedMilliseconds;
    int trunk = 0, rock = 0;
    for (int z = 0; z < w3.SZ; z += 4)
        for (int x = 0; x < w3.SX; x += 4)
            for (int y = 0; y < w3.SY; y += 2)
            {
                byte m = w3.Get(x, y, z);
                if (m == Mat.Trunk) trunk++; else if (m == Mat.Rock || m == Mat.Moss) rock++;
            }
    Console.WriteLine($"valley {mp.SizeX * VoxelWorld.Vox:0}×{mp.SizeY * VoxelWorld.Vox:0}×{mp.SizeZ * VoxelWorld.Vox:0} m: generated in {gen3} ms, {w3.AllocatedChunks}/{w3.ChunkCount} chunks hold data ({mem / 1048576} MB), mesh {mesh3} ms on {Environment.ProcessorCount} threads, {q3 * 2 / 1000} k triangles in {meshed3} chunks");
    Check($"valley {size}: the citadel stands on the plateau", w3.Get(site3.KeepX, site3.KeepBase + 10, site3.KeepZ) != 0 && site3.KeepBase > 70, $"keep base {site3.KeepBase}");
    Check($"valley {size}: forests and rock to quarry", trunk > 50 && rock > 1000, $"{trunk} trunk and {rock} rock samples");
}

Console.WriteLine(failures == 0 ? "\nall checks passed" : $"\n{failures} check(s) failed");
return failures == 0 ? 0 : 1;
