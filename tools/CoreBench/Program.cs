using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
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
    Console.WriteLine($"valley {mp.SizeX / VoxelWorld.PerMetre}×{mp.SizeY / VoxelWorld.PerMetre}×{mp.SizeZ / VoxelWorld.PerMetre} m: generated in {gen3} ms, {w3.AllocatedChunks}/{w3.ChunkCount} chunks hold data ({mem / 1048576} MB), mesh {mesh3} ms on {Environment.ProcessorCount} threads, {q3 * 2 / 1000} k triangles in {meshed3} chunks");
    Check($"valley {size}: the citadel stands on the plateau", w3.Get(site3.KeepX, site3.KeepBase + 10, site3.KeepZ) != 0 && site3.KeepBase > 70, $"keep base {site3.KeepBase}");
    Check($"valley {size}: forests and rock to quarry", trunk > 50 && rock > 1000, $"{trunk} trunk and {rock} rock samples");
}

// ---- the living valley: navigation, labour, the horde
if (args.Length == 0 || args[0] == "--sim")
{
    Console.WriteLine();
    var mp4 = new MapParams();
    var w4 = new VoxelWorld(mp4.SizeX, mp4.SizeY, mp4.SizeZ);
    var site4 = MapGen.Generate(w4, mp4);
    w4.Compact();
    sw.Restart();
    var sim = new Sim(w4, site4);
    Console.WriteLine($"navigation {sim.Nav.NX}×{sim.Nav.NY}×{sim.Nav.NZ} cells built in {sw.ElapsedMilliseconds} ms");
    sim.Waves.Countdown = 1e9;
    sim.WildTarget = 0;
    void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
    Check("a gate before the citadel", sim.Gate >= 0, sim.Gate.ToString());
    {
        int open = 0;
        for (int z = 1; z < w4.SZ - 1; z++)
            for (int x = 1; x < w4.SX - 1; x++)
            {
                int i = z * w4.SX + x;
                for (int y = site4.HTop[i]; y < site4.WaterTop[i]; y++)
                    foreach (var (sx, sz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        if (w4.Get(x + sx, y, z + sz) == Mat.Air) open++;
            }
        Check("ponds and the river lie level inside their banks", open == 0, $"{open} faces of water open to the air");
    }
    Check("cells beside the citadel to batter", sim.Goals.Count > 20, sim.Goals.Count.ToString());

    sim.Stock.Gold = 2000;
    var crew = new System.Collections.Generic.List<Unit>();
    for (int i = 0; i < 4; i++) crew.Add(sim.Hire(Kinds.Worker));
    Check("workers hired at the gate", crew.TrueForAll(u => u != null));

    // with nothing to do they pass the time: stroll, sit, talk, look round, warm their hands, and stay close
    {
        var seen = new System.Collections.Generic.HashSet<Pastime>();
        var start = crew.ConvertAll(u => (u.X, u.Z));
        int talks = 0; float moved = 0, strayed = 0;
        for (int t = 0; t < 60; t++)
        {
            Run(1);
            foreach (var u in crew)
            {
                seen.Add(u.Pastime);
                if (u.Pastime == Pastime.Talk && u.Mate != null) talks++;
            }
        }
        for (int i = 0; i < crew.Count; i++)
        {
            float d = MathF.Sqrt((crew[i].X - start[i].X) * (crew[i].X - start[i].X) + (crew[i].Z - start[i].Z) * (crew[i].Z - start[i].Z));
            moved = Math.Max(moved, d); strayed = Math.Max(strayed, d);
        }
        Check("idle workers pass the time and stay close", seen.Count >= 4 && talks > 0 && moved > 8 && strayed < 12 * VoxelWorld.PerMetre,
            $"{string.Join(", ", seen)}; talking {talks} of 240 looks, the farthest {strayed / VoxelWorld.PerMetre:0.0} m from where it stood");
    }

    int gx = site4.KeepX - site4.KeepR - 12, gz = site4.KeepZ;
    int far = sim.NearestStand(gx - 160, gz + 60, 4);
    sw.Restart();
    bool walked = sim.GoTo(crew[0], far);
    Check("a path across the plateau", walked && crew[0].Path != null && crew[0].Path.Count > 15, $"{crew[0].Path?.Count} cells, {sw.ElapsedMilliseconds} ms");
    crew[0].Path = null;

    // a pit two metres square and half a metre deep
    int px = gx - 70, pz = gz + 44;
    double earth0 = sim.Stock[Stockpile.Earth];
    var digStroke = new Stroke { Width = 1, Height = 8, Dig = true };
    int nDig = sim.DesignateRemove(Shapes.Box(w4, site4, px, pz, px + 31, pz + 31, digStroke, 0));
    sw.Restart();
    Run(120);
    long simMs = sw.ElapsedMilliseconds;
    Check("workers dig the pit", nDig > 3000 && sim.Orders.Total < nDig / 10, $"{nDig} voxels marked, {sim.Orders.Total} left, +{Fmt.M3((int)(sim.Stock[Stockpile.Earth] - earth0))} m³ of earth; 120 s simulated in {simMs} ms");

    // a stone wall three metres long, half a metre thick and a metre high, from the stores
    double stone0 = sim.Stock[Stockpile.Stone];
    var wallStroke = new Stroke { Mat = Mat.Stone, Width = 8, Height = 16, Level = true };
    int wx4 = px + 60;
    int nWall = sim.DesignateBuild(Shapes.Line(w4, site4, wx4, pz, wx4, pz + 48, wallStroke, Shapes.LevelTop(w4, wx4, pz, 16)));
    Run(150);
    Check("workers lay the wall from the stores", nWall > 2000 && sim.Orders.BuildTotal < nWall / 10 && sim.Stock[Stockpile.Stone] < stone0,
        $"{nWall} voxels planned, {sim.Orders.BuildTotal} left, stone {Fmt.M3((int)stone0)} → {Fmt.M3((int)sim.Stock[Stockpile.Stone])} m³");
    foreach (var u in crew) if (u.Why != "") Console.WriteLine($"     {u.T.Name} #{u.Id}: {u.Why}");

    // fell the nearest tree
    (int x, int y, int z) trunk = (-1, -1, -1);
    for (int r = 8; r < 400 && trunk.x < 0; r += 8)
        for (int dz = -r; dz <= r && trunk.x < 0; dz += 4)
            for (int dx = -r; dx <= r && trunk.x < 0; dx += 4)
            {
                int x = gx + dx, z = gz + dz;
                if (x < 0 || z < 0 || x >= w4.SX || z >= w4.SZ) continue;
                int g = w4.SurfAt(x, z);
                if (w4.Get(x, g + 2, z) == Mat.Trunk) trunk = (x, g + 2, z);
            }
    double wood0 = sim.Stock[Stockpile.Wood];
    var treeList = trunk.x >= 0 ? Shapes.Tree(w4, trunk.x, trunk.y, trunk.z) : null;
    int nTree = treeList == null ? 0 : sim.DesignateRemove(treeList);
    Run(200);
    Check("workers fell a tree for wood", nTree > 300 && sim.Stock[Stockpile.Wood] > wood0 + 200 && w4.Get(trunk.x, trunk.y, trunk.z) != Mat.Trunk,
        $"{nTree} voxels of tree at {trunk}, wood +{Fmt.M3((int)(sim.Stock[Stockpile.Wood] - wood0))} m³, {sim.Orders.Total} orders left");

    // a block of logs marked right where a soldier stands: the rest of it gets laid, nobody stalls over his spot
    {
        int fx = px - 60, fz = pz - 60;
        var guard = sim.Spawn(Kinds.Archer, sim.NearestStand(fx + 12, fz + 12, 4));
        sim.Command(new[] { guard }, guard.Cell);
        int block = sim.DesignateBuild(Shapes.Box(w4, site4, fx, fz, fx + 24, fz + 24, new Stroke { Mat = Mat.Wood, Width = 1, Height = 12, Level = true }, Shapes.LevelTop(w4, fx, fz, 12)));
        Run(90);
        string why = "";
        foreach (var u in crew) if (u.Why != "") why += $" #{u.Id}: {u.Why};";
        Check("masonry where a soldier stands waits, the rest gets laid", block > 6000 && sim.Orders.BuildTotal > 0 && sim.Orders.BuildTotal <= 8 * 8 * 12,
            $"{block} voxels planned, {sim.Orders.BuildTotal} left in his place;{why}");
        sim.CancelArea(fx, fz, fx + 24, fz + 24);
        guard.Dead = true; guard.DiedAt = -999;
    }

    // pawns sent to one spot don't stand inside each other
    {
        int spot = sim.NearestStand(px - 30, pz - 30, 2);
        sim.Command(new[] { crew[0], crew[1] }, spot);
        sim.Command(new[] { crew[2] }, spot);
        Run(6);
        float gap = MathF.Sqrt((crew[0].X - crew[2].X) * (crew[0].X - crew[2].X) + (crew[0].Z - crew[2].Z) * (crew[0].Z - crew[2].Z));
        Check("pawns sent to one spot keep apart", gap >= crew[0].T.BodyR * 2 - .2f, $"{gap:0.0} voxels between two of them");
        sim.ReleaseHold(crew);
    }

    // areas set aside for a resource: the workers fell, quarry and dig them by themselves when nothing else is marked
    {
        (int x, int z) Find(Func<int, int, bool> test)
        {
            for (int r = 40; r < 500; r += 6)
                for (int a = 0; a < 360; a += 3)
                {
                    int x = site4.KeepX + (int)(Math.Cos(a * Math.PI / 180) * r), z = site4.KeepZ + (int)(Math.Sin(a * Math.PI / 180) * r);
                    if (x > 40 && z > 40 && x < w4.SX - 40 && z < w4.SZ - 40 && test(x, z)) return (x, z);
                }
            return (-1, -1);
        }
        const int G = 40;
        // the plateau's thickest grove within a walk of the citadel
        (int x, int z) wood = (-1, -1); int most = 0;
        for (int cz = 80; cz < w4.SZ - 80; cz += 40)
            for (int cx = 80; cx < w4.SX - 80; cx += 40)
            {
                float dk = MathF.Sqrt((cx - site4.KeepX) * (cx - site4.KeepX) + (cz - site4.KeepZ) * (cz - site4.KeepZ));
                if (dk < 150 || dk > 460 || w4.SurfAt(cx, cz) < 70) continue;
                int t = sim.Survey(cx - G, cz - G, cx + G, cz + G, Stockpile.Wood, 0).trees;
                if (t > most) { most = t; wood = (cx, cz); }
            }
        var (trees0, _) = sim.Survey(wood.x - G, wood.z - G, wood.x + G, wood.z + G, Stockpile.Wood, 0);
        double w0 = sim.Stock[Stockpile.Wood];
        var grove = sim.AddZone(wood.x - G, wood.z - G, wood.x + G, wood.z + G, Stockpile.Wood, 0);
        Run(150);
        var (trees1, _) = sim.Survey(wood.x - G, wood.z - G, wood.x + G, wood.z + G, Stockpile.Wood, 0);
        Check("workers fell a set-aside grove by themselves", trees0 >= 3 && trees1 <= trees0 - 2 && sim.Stock[Stockpile.Wood] > w0 + 2000,
            $"{trees0} → {trees1} trees round {wood}, wood +{Fmt.M3(sim.Stock[Stockpile.Wood] - w0)} m³, {grove.Marked} voxels marked so far");
        sim.RemoveZones(wood.x - G, wood.z - G, wood.x + G, wood.z + G);
        sim.CancelArea(wood.x - 70, wood.z - 70, wood.x + 70, wood.z + 70);

        var rock = Find((x, z) => w4.Get(x, w4.SurfAt(x, z) - 1, z) == Mat.Rock && w4.SurfAt(x, z) > site4.HTop[z * w4.SX + x] + 4 && w4.SurfAt(x, z) > 70);
        double s0 = sim.Stock[Stockpile.Stone];
        var (_, rockVox) = sim.Survey(rock.x - 16, rock.z - 16, rock.x + 16, rock.z + 16, Stockpile.Stone, 16);
        sim.AddZone(rock.x - 16, rock.z - 16, rock.x + 16, rock.z + 16, Stockpile.Stone, 16);
        Run(120);
        Check("…quarry a boulder in a set-aside pit", rockVox > 200 && sim.Stock[Stockpile.Stone] > s0 + 300,
            $"boulder at {rock}, ≈{Fmt.M3(rockVox)} m³ of rock in reach, stone +{Fmt.M3(sim.Stock[Stockpile.Stone] - s0)} m³");
        sim.RemoveZones(rock.x - 16, rock.z - 16, rock.x + 16, rock.z + 16);
        sim.CancelArea(rock.x - 40, rock.z - 40, rock.x + 40, rock.z + 40);

        int ex = px - 60, ez = pz + 40;
        double e0 = sim.Stock[Stockpile.Earth];
        var cut = sim.AddZone(ex, ez, ex + 15, ez + 15, Stockpile.Earth, 8);
        Run(60);
        Check("…and dig a set-aside cut until it is done, then let it go", !sim.Zones.Contains(cut) && sim.Stock[Stockpile.Earth] > e0 + 1000,
            $"{cut.Marked} voxels dug, earth +{Fmt.M3(sim.Stock[Stockpile.Earth] - e0)} m³, zones left {sim.Zones.Count}");
    }

    // a ladder against a wall: a worker climbs up onto it
    {
        int lx = px - 40, lz = pz + 80;
        sim.InstantLabour = true;
        int ltop = Shapes.LevelTop(w4, lx, lz, 40);
        sim.DesignateBuild(Shapes.Line(w4, site4, lx, lz - 24, lx, lz + 24, new Stroke { Mat = Mat.Stone, Width = 12, Height = 40, Level = true }, ltop));
        int fy = w4.SurfAt(lx + 20, lz) + 6, fx = lx + 20;
        while (fx > lx && w4.Get(fx - 1, fy, lz) == Mat.Air) fx--;
        var face = new VoxelHit { X = fx - 1, Y = fy, Z = lz, NX = 1, M = w4.Get(fx - 1, fy, lz) };
        int rungs = sim.DesignateBuild(Shapes.LadderAt(w4, site4, face));
        sim.InstantLabour = false;
        var climber = crew[0];
        int wallTop = sim.NearestStand(lx, lz, 4);
        bool path = sim.GoTo(climber, wallTop);
        climber.Hold = true;
        Run(30);
        Check("a worker climbs a ladder onto a wall", rungs > 50 && path && climber.Cell == wallTop, $"{rungs} voxels of ladder, path {path}, at {sim.Nav.Floor[climber.Cell]} (wall top {ltop})");
        sim.ReleaseHold(new[] { climber });
        Run(10);
    }

    // crews cutting earth beside a pond leave its shore alone; a trench cut from the pond into rising ground fills,
    // and the pond gives up just as much
    {
        var (qx, qz, pr, _) = site4.Ponds[0];
        double kd = Math.Sqrt((double)(site4.KeepX - qx) * (site4.KeepX - qx) + (double)(site4.KeepZ - qz) * (site4.KeepZ - qz));
        (int x, int z) At(double r) => (qx + (int)Math.Round((site4.KeepX - qx) / kd * r), qz + (int)Math.Round((site4.KeepZ - qz) / kd * r));
        var edge = At(pr);
        int before = sim.Flooded;
        double e0 = sim.Stock[Stockpile.Earth];
        var (_, cutVox) = sim.Survey(edge.x - 16, edge.z - 16, edge.x + 16, edge.z + 16, Stockpile.Earth, 16);
        sim.AddZone(edge.x - 16, edge.z - 16, edge.x + 16, edge.z + 16, Stockpile.Earth, 16);
        Run(90);
        Check("crews cutting earth beside a pond leave its shore alone", sim.Stock[Stockpile.Earth] > e0 + 300 && sim.Flooded == before,
            $"pond at ({qx}, {qz}), ≈{Fmt.M3(cutVox)} m³ of earth in reach, earth +{Fmt.M3(sim.Stock[Stockpile.Earth] - e0)} m³, {sim.Flooded - before} voxels of water let in");
        sim.RemoveZones(edge.x - 16, edge.z - 16, edge.x + 16, edge.z + 16);
        sim.CancelArea(edge.x - 40, edge.z - 40, edge.x + 40, edge.z + 40);

        // the trench runs from just inside the water to the highest ground a few metres beyond the shore
        var shore = At(pr - 3);
        (int x, int z) high = shore; int highY = int.MinValue;
        for (int z = shore.z - 40; z <= shore.z + 40; z += 2)
            for (int x = shore.x - 40; x <= shore.x + 40; x += 2)
            {
                int g = w4.SurfAt(x, z), d2 = (x - qx) * (x - qx) + (z - qz) * (z - qz);
                if (w4.Get(x, g, z) == Mat.Water || site4.Forbidden(x, z) || d2 < (pr + 12) * (pr + 12) || d2 > (pr + 30) * (pr + 30)) continue;
                if (g > highY) { highY = g; high = (x, z); }
            }
        long water0 = WaterIn(w4);
        int ran0 = sim.Flooded, over0 = OverLand(w4, site4);
        sim.InstantLabour = true;
        sim.DesignateRemove(Shapes.Line(w4, site4, shore.x, shore.z, high.x, high.z, new Stroke { Width = 6, Height = 16, Dig = true }, 0));
        sim.InstantLabour = false;
        for (int i = 0; i < 120 && sim.WaterBusy; i++) Run(1);
        long water1 = WaterIn(w4);
        Check("a trench cut from a pond fills, and the pond gives up just as much", sim.Flooded - ran0 > 300 && water1 == water0 && !sim.WaterBusy && Floating(w4) == 0,
            $"pond at ({qx}, {qz}), trench to {high} at {highY}: {sim.Flooded - ran0} voxels of water moved, {water0} → {water1} in the valley, {OverLand(w4, site4) - over0} over the land");

        // the same worked out on a worker thread, as in the game: a pit dug into the pit shore
        var pit = (x: qx - (int)Math.Round((site4.KeepX - qx) / kd * (pr + 6)), z: qz - (int)Math.Round((site4.KeepZ - qz) / kd * (pr + 6)));
        sim.WaterInBackground = true;
        ran0 = sim.Flooded;
        sim.InstantLabour = true;
        sim.DesignateRemove(Shapes.Box(w4, site4, pit.x - 6, pit.z - 6, pit.x + 6, pit.z + 6, new Stroke { Height = 12, Dig = true }, 0));
        sim.InstantLabour = false;
        var waited = Stopwatch.StartNew();
        while (sim.WaterBusy && waited.ElapsedMilliseconds < 20000) { Run(.05); if (sim.WaterBusy) Thread.Sleep(1); }
        sim.WaterInBackground = false;
        long water2 = WaterIn(w4);
        Check("…and on a worker thread, as in the game", sim.Flooded - ran0 > 100 && water2 == water0 && !sim.WaterBusy && Floating(w4) == 0,
            $"pit at {pit}: {sim.Flooded - ran0} voxels of water moved in {waited.ElapsedMilliseconds} ms, {water0} → {water2} in the valley");
    }

    // a gate in a walled yard: our worker knocks, it opens and shuts behind him; barred, there is no way through;
    // an brute shut in the yard gets no help from it and hacks his way out
    {
        int yx = site4.KeepX + 120, yz = site4.KeepZ - 150;
        sim.InstantLabour = true;
        int ytop = Shapes.LevelTop(w4, yx, yz, 32);
        sim.DesignateBuild(Shapes.Box(w4, site4, yx - 32, yz - 32, yx + 32, yz + 32, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Level = true, Hollow = true }, ytop));
        sim.InstantLabour = false;
        // the south wall, toward the citadel, clicked from outside in its middle: the leaves hang on its outer face
        int fy = w4.SurfAt(yx, yz) + 6, fz = yz;
        while (w4.Get(yx, fy, fz) == Mat.Air) fz++;
        while (w4.Get(yx, fy, fz) != Mat.Air) fz++;
        var face = new VoxelHit { X = yx, Y = fy, Z = fz - 1, NZ = 1, M = w4.Get(yx, fy, fz - 1) };
        var plan = GatePlan.At(w4, site4, face);
        var gate = plan == null ? null : sim.AddGate(plan, built: true);
        Run(1);
        int outside = sim.NearestStand(yx, fz + 40, 4), inside = sim.NearestStand(yx, yz, 4);
        var porter = sim.Spawn(Kinds.Worker, outside);
        bool shutAtFirst = gate != null && gate.Open == 0 && !sim.Nav.Open(sim.Nav.I(gate.Cells.First()), sim.Nav.Y(gate.Cells.First()), sim.Nav.K(gate.Cells.First()));
        sim.Command(new[] { porter }, inside);
        float widest = 0;
        for (int t = 0; t < 40 * 20 && porter.Cell != inside; t++) { sim.Step(.05f); widest = Math.Max(widest, gate?.Open ?? 0); }
        bool got = porter.Cell == inside;
        Run(6);
        Check("a gate opens for our worker and shuts behind him", gate != null && shutAtFirst && got && widest >= .99f && gate.Open == 0,
            gate == null ? "no gate fits the wall" : $"leaves on the {(gate.In > 0 ? "south (outer)" : "north")} side, opened to {widest:0.00}, worker in: {got}, now {gate.Open:0.00}");

        sim.SetGateMode(gate, GateMode.Shut);
        Run(1);
        int barred = sim.Command(new[] { porter }, outside);
        sim.SetGateMode(gate, GateMode.Auto);
        porter.Dead = true; porter.DiedAt = -999;

        var brute = sim.Spawn(Kinds.Orc, sim.NearestStand(yx, yz + 10, 2));
        sim.ComputeFields();
        float opened = 0;
        bool Out(Unit u) => Math.Abs(u.X - yx) > 36 || Math.Abs(u.Z - yz) > 36;
        for (int t = 0; t < 120 * 20 && !Out(brute); t++) { sim.Step(.05f); opened = Math.Max(opened, gate.Open); }
        Check("barred, it lets nobody through; it never opens for the horde, which hacks through it", barred == 0 && opened == 0 && gate.Broken && Out(brute),
            $"way out while barred: {barred}, opened for the orc to {opened:0.00}, leaf voxels hacked {gate.Hacked} of {Gateway.Voxels}, broken {gate.Broken}, orc out of the yard {Out(brute)} at ({brute.X:0}, {brute.Z:0})");
        brute.Dead = true; brute.DiedAt = -999;
        int mended = sim.MendGate(gate);
        sim.CompleteAll();
        Run(2);
        Check("a broken gate is mended and works again", mended > 1000 && gate.Ready && !gate.Broken && gate.Hacked == 0,
            $"{mended} leaf voxels to lay anew, ready {gate.Ready}");
        sim.InstantLabour = true;
        sim.DesignateRemove(Shapes.Box(w4, site4, yx - 36, yz - 36, yx + 36, yz + 36, new Stroke { Width = 14, Height = 40, Dig = true, Hollow = true }, 0));
        sim.InstantLabour = false;
    }

    // the mines: workers sent to one take it and it pays; ore broken out of its vein pays more; the horde camped at
    // another keeps it till our soldiers clear the camp; a raider left alone at a mine of ours takes it back
    {
        int ruins = site4.Landmarks.Count(l => l.Kind == LandmarkKind.Ruin);
        var copper = sim.Mines.Find(m => m.Name == "Медный рудник");
        int stand = sim.NearestStand(copper.Place.StandX, copper.Place.StandZ, 4);
        var miners = new System.Collections.Generic.List<Unit> { sim.Spawn(Kinds.Worker, sim.Gate), sim.Spawn(Kinds.Worker, sim.Gate) };
        sim.Command(miners, stand);
        for (int t = 0; t < 120 && copper.Holder != Holder.Ours; t++) Run(1);
        bool taken = copper.Holder == Holder.Ours;
        double g1 = sim.Stock.Gold;
        Run(60);
        double paid = sim.Stock.Gold - g1;
        Check("workers sent to a mine take it, and it pays while it is ours", sim.Mines.Count == 3 && ruins == 4 && taken && paid > copper.Income * .9,
            $"{sim.Mines.Count} mines, {ruins} ruins; {copper.Name} {(taken ? "taken" : "not taken")}, +{paid:0.0} gold in a minute (pays {copper.Income})");

        // the vein: the ore showing in the adit, marked for the pick
        var ore = new VoxelList();
        for (int y = 40; y < 140 && ore.Count < 240; y++)
            for (int z = copper.Place.Z - 20; z <= copper.Place.Z + 20 && ore.Count < 240; z++)
                for (int x = copper.Place.X - 20; x <= copper.Place.X + 20 && ore.Count < 240; x++)
                    if (w4.Get(x, y, z) == Mat.Ore && (w4.Get(x + 1, y, z) == 0 || w4.Get(x - 1, y, z) == 0 || w4.Get(x, y, z + 1) == 0 || w4.Get(x, y, z - 1) == 0))
                        ore.Add(x, y, z, Mat.Air);
        sim.DesignateRemove(ore);
        sim.ReleaseHold(miners);
        double g2 = sim.Stock.Gold;
        Run(90);
        double fromOre = sim.Stock.Gold - g2 - copper.Income * 1.5;
        Check("ore broken out of the vein pays gold besides", ore.Count > 50 && fromOre > ore.Count * Labour.OreGold * .5,
            $"{ore.Count} voxels of ore showing in the adit, +{fromOre:0.0} gold for what was broken out");

        sim.SeedMines();
        var silver = sim.Mines.Find(m => m.Place.Guarded);
        var guards = sim.Units.FindAll(u => u.Guard && !u.Dead);
        bool held = silver.Holder == Holder.Horde && sim.RaidersAlive() == 0;
        var squad = new System.Collections.Generic.List<Unit>();
        foreach (var t in new[] { Kinds.Archer, Kinds.Archer, Kinds.Archer, Kinds.Swordsman, Kinds.Swordsman }) squad.Add(sim.Spawn(t, sim.Gate));
        sim.Command(squad, sim.NearestStand(silver.Place.StandX, silver.Place.StandZ, 4));
        for (int t = 0; t < 240 && silver.Holder != Holder.Ours; t++) Run(1);
        Check("the horde camped at a mine keeps it until our soldiers clear the camp", guards.Count == 4 && held && guards.TrueForAll(u => u.Dead) && silver.Holder == Holder.Ours,
            $"{guards.Count} guards, the horde's at first {held}; dead {guards.Count(u => u.Dead)}, {silver.Name} now {silver.Holder}; soldiers alive {squad.Count(u => !u.Dead)} of {squad.Count}");
        foreach (var u in squad) { u.Dead = true; u.DiedAt = -999; }

        sim.Command(miners, sim.Gate);
        Run(20);
        var raider = sim.Spawn(Kinds.Goblin, sim.NearestStand(copper.Place.StandX - 70, copper.Place.StandZ, 2));
        raider.Raid = copper;
        for (int t = 0; t < 90 && copper.Holder != Holder.Horde; t++) Run(1);
        Check("a raider left alone at a mine of ours takes it back", copper.Holder == Holder.Horde,
            $"{copper.Name} now {copper.Holder}, the goblin {(raider.Dead ? "dead" : "alive")}");
        raider.Dead = true; raider.DiedAt = -999;
        foreach (var u in miners) { u.Dead = true; u.DiedAt = -999; }
        foreach (var m in sim.Mines) { m.Holder = Holder.None; m.Grip = 0; }
    }

    // the beasts: a deer bolts from a worker, archers hunt a boar for gold
    {
        sim.WildTarget = 0;
        int meadow = sim.NearestStand(site4.KeepX - 230, site4.KeepZ - 120, 3);
        var deer = sim.Spawn(Kinds.Deer, meadow);
        var (mx, _, mz) = sim.Nav.Feet(meadow);
        var walker = sim.Spawn(Kinds.Worker, sim.NearestStand((int)mx + 40, (int)mz, 4));
        sim.Command(new[] { walker }, walker.Cell);
        float dd0 = Sim.Dist(deer, walker);
        Run(4);
        float dd1 = Sim.Dist(deer, walker);
        Check("a deer bolts from a worker", dd1 > dd0 + 48, $"{dd0 / VoxelWorld.PerMetre:0.0} → {dd1 / VoxelWorld.PerMetre:0.0} m, {deer.State}");
        walker.Dead = true; walker.DiedAt = -999;
        var boar = sim.Spawn(Kinds.Boar, sim.NearestStand((int)mx + 120, (int)mz + 160, 2));
        var hunters = new[] { sim.Spawn(Kinds.Archer, sim.NearestStand((int)boar.X - 150, (int)boar.Z, 4)), sim.Spawn(Kinds.Archer, sim.NearestStand((int)boar.X - 150, (int)boar.Z + 20, 4)) };
        double g0 = sim.Stock.Gold;
        int sent = sim.Hunt(hunters, boar);
        Run(40);
        Check("archers hunt a boar down for gold", sent == 2 && boar.Dead && sim.Stock.Gold >= g0 + Kinds.Boar.Bounty, $"boar {(boar.Dead ? "taken" : $"{boar.Hp:0} hp")}, gold +{sim.Stock.Gold - g0:0}, archers {hunters[0].Hp:0}/{hunters[1].Hp:0} hp");
        foreach (var u in hunters) { u.Dead = true; u.DiedAt = -999; }
        deer.Dead = true; deer.DiedAt = -999;
    }

    // the horde finds its way
    sw.Restart();
    sim.HordeStock[Stockpile.Wood] = sim.HordeStock[Stockpile.Earth] = 0;
    sim.ComputeFields();
    Console.WriteLine($"     raider fields for {Kinds.HordeKinds.Length} kinds in {sw.ElapsedMilliseconds} ms");
    int spawn = sim.NearestStand(site4.SpawnStrip / 2, w4.SZ / 2, 4);
    Check("every raider kind finds the citadel from the west", Array.TrueForAll(Kinds.HordeKinds, t => sim.Fields.TryGetValue(t, out var f) && !float.IsInfinity(f.Dist[spawn])),
        string.Join(", ", Array.ConvertAll(Kinds.HordeKinds, t => $"{t.Key} {sim.Fields[t].Dist[spawn]:0} s")));

    // a whole wave against two archers and a swordsman by the gate
    foreach (var u in crew) sim.Command(new[] { u }, sim.Gate);
    sim.Hire(Kinds.Archer); sim.Hire(Kinds.Archer); sim.Hire(Kinds.Swordsman);
    float keep0 = sim.KeepHp;
    sim.Waves.Countdown = 0;
    sw.Restart();
    Run(180);
    int alive = 0, dead = 0;
    foreach (var u in sim.Units) if (u.Team == Team.Horde) { if (u.Dead) dead++; else alive++; }
    Check("the first wave comes and fights", sim.Waves.Number == 1 && sim.Kills > 0 && sim.Shots > 0,
        $"{sim.Kills} raiders killed, {alive} alive, {sim.Shots} arrows loosed, {sim.ShotHits} hit, citadel {keep0:0} → {sim.KeepHp:0}; 180 s in {sw.ElapsedMilliseconds} ms, fields {sim.LastFieldMs:0} ms");
    foreach (var u in sim.Units)
        if (!u.Dead && u.Team != Team.Wild) Console.WriteLine($"     {u.T.Name} #{u.Id}: {u.State}, {u.Hp:0} hp, at {u.X / VoxelWorld.PerMetre:0.0},{u.Y / VoxelWorld.PerMetre:0.0},{u.Z / VoxelWorld.PerMetre:0.0} m{(u.Why != "" ? ", " + u.Why : "")}");

    // calling the horde early pays nothing: it only comes knowing less of the defences
    {
        sim.Waves.CallOff(sim);
        double gold = sim.Stock.Gold;
        sim.Waves.Countdown = Waves.Prep * .75;
        sim.Waves.StartNow(sim);
        Check("an early call pays nothing and the horde comes knowing less", sim.Waves.Active && Math.Abs(sim.Stock.Gold - gold) < .01 && Math.Abs(sim.Waves.Intel - .25) < .01,
            $"intel {sim.Waves.Intel * 100:0} %, gold {gold:0} → {sim.Stock.Gold:0}");
        sim.Waves.CallOff(sim);
        sim.Waves.Countdown = 1e9;
    }

    sim.InstantLabour = true;
    {
        // a block of stone dropped right on the gate where the garrison stands
        var (gfx, gfy, gfz) = sim.Nav.Feet(sim.Gate);
        sim.DesignateBuild(Shapes.Box(w4, site4, (int)gfx - 20, (int)gfz - 20, (int)gfx + 20, (int)gfz + 20, new Stroke { Mat = Mat.Stone, Width = 8, Height = 40 }, 0));
        sim.Step(.05f);
        int walled = 0, there = 0;
        foreach (var u in sim.Units)
            if (!u.Dead && u.Team == Team.Player) { there++; if (sim.Nav.Fill[u.Cell] >= NavGrid.SolidT) walled++; }
        Check("instant building leaves room for the garrison", there > 0 && walled == 0, $"{there} pawns at the gate, {walled} walled in");
    }
    // the horde alone against a stone ring: the garrison leaves the field
    foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
    sim.Step(.05f);
    float DistKeep(Unit u) => MathF.Sqrt((u.X - site4.KeepX) * (u.X - site4.KeepX) + (u.Z - site4.KeepZ) * (u.Z - site4.KeepZ));
    int Ring(int r, Stroke st) => st.Dig ? sim.DesignateRemove(Shapes.Circle(w4, site4, site4.KeepX, site4.KeepZ, r, st, 0)) : sim.DesignateBuild(Shapes.Circle(w4, site4, site4.KeepX, site4.KeepZ, r, st, 0));
    var wallRing = new Stroke { Mat = Mat.Stone, Width = 8, Height = 48, Hollow = true };
    Ring(site4.KeepR + 44, wallRing);
    sim.InstantLabour = false;
    int hacked = 0, ladder = 0, decked = 0, filled = 0;
    (int x, int y, int z) rung = (-1, -1, 0);
    sim.Hacked = (x, y, z, m) => hacked++;
    sim.Raised = (x, y, z, m) =>
    {
        if (m == Mat.SiegeLadder) { ladder++; if (y > rung.y) rung = (x, y, z); }
        else if (m == Mat.Deck) decked++;
        else if (m == Mat.Soil) filled++;
    };

    // with nothing to build from, an orc has to hack its way through
    sim.HordeStock[Stockpile.Wood] = sim.HordeStock[Stockpile.Earth] = 0;
    sim.ComputeFields();
    var orc = sim.Spawn(Kinds.Orc, sim.NearestStand(site4.KeepX - site4.KeepR - 100, site4.KeepZ, 4));
    float d0 = DistKeep(orc);
    Run(120);
    float d1 = DistKeep(orc);
    Check("with nothing to build from, an orc hacks through the stone ring", hacked > 200 && ladder == 0 && d1 < d0 - 40, $"{hacked} voxels hacked, {d0 / VoxelWorld.PerMetre:0.0} → {d1 / VoxelWorld.PerMetre:0.0} m from the citadel, {orc.State}");
    orc.Dead = true; orc.DiedAt = -999;

    // with timber a sapper raises a ladder against a fresh ring and climbs over it
    sim.InstantLabour = true;
    Ring(site4.KeepR + 76, wallRing);
    sim.InstantLabour = false;
    sim.HordeStock[Stockpile.Wood] = 4096;
    sim.ComputeFields();
    hacked = 0;
    var sapper = sim.Spawn(Kinds.Sapper, sim.NearestStand(site4.KeepX - site4.KeepR - 120, site4.KeepZ, 3));
    d0 = DistKeep(sapper);
    Run(60);
    d1 = DistKeep(sapper);
    Check("a sapper raises a ladder against the wall and climbs over", ladder > 30 && d1 < site4.KeepR + 70 && hacked < 100, $"{ladder} voxels of ladder, {hacked} hacked, {d0 / VoxelWorld.PerMetre:0.0} → {d1 / VoxelWorld.PerMetre:0.0} m, {sapper.State}");
    sapper.Dead = true; sapper.DiedAt = -999;

    // a swordsman on the wall throws that ladder down
    {
        int onWall = sim.NearestStand(rung.x, rung.z, 4);
        var guard = sim.Spawn(Kinds.Swordsman, onWall);
        sim.Command(new[] { guard }, guard.Cell);
        Run(4);
        Check("a swordsman on the wall throws the siege ladder down", rung.x >= 0 && w4.Get(rung.x, rung.y, rung.z) != Mat.SiegeLadder, $"ladder top at {rung}, swordsman at {sim.Nav.Floor[guard.Cell]}");
        guard.Dead = true; guard.DiedAt = -999;
    }

    // a moat round the citadel: the sapper bridges it
    sim.InstantLabour = true;
    Ring(site4.KeepR + 130, new Stroke { Width = 24, Height = 24, Dig = true, Hollow = true });
    sim.InstantLabour = false;
    sim.HordeStock[Stockpile.Wood] = 4096; sim.HordeStock[Stockpile.Earth] = 0;
    sim.ComputeFields();
    var bridger = sim.Spawn(Kinds.Sapper, sim.NearestStand(site4.KeepX - site4.KeepR - 160, site4.KeepZ, 3));
    Run(60);
    Check("a sapper lays a bridge over the moat", decked > 200 && DistKeep(bridger) < site4.KeepR + 104, $"{decked} voxels of deck, now {DistKeep(bridger) / VoxelWorld.PerMetre:0.0} m from the citadel, {bridger.State}");
    bridger.Dead = true; bridger.DiedAt = -999;

    // a ditch too deep to climb out of and no timber left: earth is heaped in (the ring keeps clear of the ponds,
    // or the ditch would fill from them and be a moat to bridge)
    sim.InstantLabour = true;
    Ring(site4.KeepR + 176, new Stroke { Width = 16, Height = 16, Dig = true, Hollow = true });
    sim.InstantLabour = false;
    sim.HordeStock[Stockpile.Wood] = 0; sim.HordeStock[Stockpile.Earth] = 8192;
    sim.ComputeFields();
    var filler = sim.Spawn(Kinds.Sapper, sim.NearestStand(site4.KeepX - site4.KeepR - 206, site4.KeepZ, 3));
    Run(60);
    Check("without timber a sapper fills the ditch with earth", filled > 400 && DistKeep(filler) < site4.KeepR + 160, $"{filled} voxels of fill, now {DistKeep(filler) / VoxelWorld.PerMetre:0.0} m from the citadel, {filler.State}");

    // the other pond breached by a trench run out to the cliff: the water pours out over the land and down into the
    // lowland as far as it goes, the pond drops by as much as left it, and nothing is made or lost
    {
        var (qx, qz, pr, plevel) = site4.Ponds[1];
        int cliff = qx - pr;
        while (cliff > 0 && w4.SurfAt(cliff - 1, qz) > w4.SurfAt(cliff, qz) - 12) cliff--;
        long water0 = WaterIn(w4);
        int ran0 = sim.Flooded, over0 = OverLand(w4, site4);
        var sw2 = Stopwatch.StartNew();
        sim.InstantLabour = true;
        sim.DesignateRemove(Shapes.Line(w4, site4, qx - pr + 6, qz, cliff, qz, new Stroke { Width = 6, Height = 16, Dig = true }, 0));
        sim.InstantLabour = false;
        double t0 = sim.Time;
        for (int i = 0; i < 600 && sim.WaterBusy; i++) Run(1);
        int surface = w4.SurfAt(qx, qz);
        while (w4.Get(qx, surface, qz) == Mat.Water) surface++;
        long water1 = WaterIn(w4);
        int over = OverLand(w4, site4) - over0;
        Check("a pond breached out to the cliff pours over the land and settles; no water is made or lost",
            water1 == water0 && over > 1000 && surface < plevel && !sim.WaterBusy && Floating(w4) == 0,
            $"pond at ({qx}, {qz}) standing to {plevel}, now to {surface}; trench to the cliff at x {cliff}: {sim.Flooded - ran0} voxels moved in {sim.Time - t0:0} s ({sw2.ElapsedMilliseconds} ms), " +
            $"{over} out over the land, {water0} → {water1} in the valley");
    }
}

// ---- a new game: the half-built fort round the citadel
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--fort")
{
    Console.WriteLine();
    var mp5 = new MapParams();
    var w5 = new VoxelWorld(mp5.SizeX, mp5.SizeY, mp5.SizeZ);
    var site5 = MapGen.Generate(w5, mp5);
    w5.Compact();
    var sim5 = new Sim(w5, site5);
    sim5.Waves.Countdown = 1e9; sim5.WildTarget = 0;
    void Run5(double seconds) { for (double t = 0; t < seconds; t += .05) sim5.Step(.05f); }
    var gate5 = StartingFort.Raise(sim5);
    Run5(.5);
    int marked = sim5.Orders.Total;
    double stone0 = sim5.Stock[Stockpile.Stone], wood0 = sim5.Stock[Stockpile.Wood];
    var crew5 = new System.Collections.Generic.List<Unit>();
    for (int i = 0; i < 4; i++) crew5.Add(sim5.Spawn(Kinds.Worker, sim5.Gate));
    // the west curtain stands, and nothing but its gate leads through it
    int kx = site5.KeepX, kz = site5.KeepZ;
    int outside = sim5.NearestStand(kx - 140, kz, 4);
    var probe = sim5.Spawn(Kinds.Worker, outside);
    bool way = sim5.GoTo(probe, sim5.Gate);
    bool throughGate = way && probe.Path.Exists(c => gate5.Cells.Contains(c));
    probe.Path = null; probe.Dead = true; probe.DiedAt = -999;
    Check("a new game finds a fort half raised round the citadel", gate5 != null && gate5.Ready && marked > 10000 && throughGate,
        $"gate ready {gate5?.Ready}, {marked} voxels marked to finish it, the way in from the west through the gate {throughGate}");
    Run5(120);
    Check("…and the workers set about finishing it from the stores", sim5.Orders.Total < marked && sim5.Stock[Stockpile.Wood] < wood0 - 2 * VoxelWorld.PerM3,
        $"{marked - sim5.Orders.Total} voxels laid in 2 min, wood {Fmt.M3(wood0)} → {Fmt.M3(sim5.Stock[Stockpile.Wood])} m³, stone {Fmt.M3(stone0)} → {Fmt.M3(sim5.Stock[Stockpile.Stone])} m³");
}

// voxels of water in the whole valley
static long WaterIn(VoxelWorld w)
{
    long n = 0;
    for (int ci = 0; ci < w.ChunkCount; ci++)
    {
        var d = w.ChunkData(ci);
        if (d == null) { if (w.ChunkUniform(ci) == Mat.Water) n += VoxelWorld.CS3; continue; }
        foreach (byte b in d) if (b == Mat.Water) n++;
    }
    return n;
}

// water hanging over air: there should be none once it has settled
static int Floating(VoxelWorld w)
{
    int n = 0;
    for (int ci = 0; ci < w.ChunkCount; ci++)
    {
        var d = w.ChunkData(ci);
        if (d == null) continue;
        w.ChunkCoords(ci, out int cx, out int cy, out int cz);
        for (int i = 0; i < d.Length; i++)
            if (d[i] == Mat.Water && w.Get(cx * 32 + (i & 31), cy * 32 + (i >> 10) - 1, cz * 32 + (i >> 5 & 31)) == Mat.Air) n++;
    }
    return n;
}

// water standing above the bare ground and the water as the valley was made: what has run out over the land
static int OverLand(VoxelWorld w, Site site)
{
    int n = 0;
    for (int ci = 0; ci < w.ChunkCount; ci++)
    {
        var d = w.ChunkData(ci);
        if (d == null) continue;
        w.ChunkCoords(ci, out int cx, out int cy, out int cz);
        for (int i = 0; i < d.Length; i++)
        {
            if (d[i] != Mat.Water) continue;
            int x = cx * 32 + (i & 31), y = cy * 32 + (i >> 10), z = cz * 32 + (i >> 5 & 31), c = z * w.SX + x;
            if (y >= site.HTop[c] && y >= site.WaterTop[c]) n++;
        }
    }
    return n;
}

Console.WriteLine(failures == 0 ? "\nall checks passed" : $"\n{failures} check(s) failed");
return failures == 0 ? 0 : 1;
