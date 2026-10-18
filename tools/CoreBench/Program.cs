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
// the prototype had 232642; its citadel of neat brick is rough masonry now
Check("triangles as counted", quads * 2 == 234006, $"{quads * 2} triangles in {meshed} chunks, 234006 expected (the prototype's 232642 with a brick citadel)");
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
    int dark = 0, light = 0; for (int i = 0; i < L.Count; i++) { if (L.M(i) == Mat.StoneDark) dark++; if (L.M(i) == Mat.StoneLight) light++; }
    Check("stone goes down as rough masonry", mortar > L.Count / 10 && mortar < L.Count / 2 && dark > L.Count / 20 && light > L.Count / 20,
        $"{mortar} mortar, {dark} dark and {light} light stones of {L.Count}");
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
    // no walls cut into the lowland: hills come down to the river, the road and the ramps as slopes
    int walls = 0, wx0 = 0, wz0 = 0;
    for (int z = 1; z < w3.SZ - 1; z++)
        for (int x = 1; x < w3.SX - 1; x++)
        {
            int h0 = site3.HTop[z * w3.SX + x];
            foreach (var (dx, dz) in new[] { (1, 0), (0, 1) })
            {
                int h1 = site3.HTop[(z + dz) * w3.SX + x + dx];
                if (Math.Abs(h1 - h0) <= 10 || Math.Max(h0, h1) > 74 || Landmarks.Taken(site3, x, z, 10)) continue;
                if (walls++ == 0) { wx0 = x; wz0 = z; }
            }
        }
    Check($"valley {size}: no walls cut into the lowland", walls < 10, $"{walls} steps over 1.25 m between neighbouring columns below the plateau{(walls > 0 ? $", the first at {wx0},{wz0}" : "")}");
}

// ---- other seeds: the valley is made from its seed alone, so any seed gives a whole one
if (args.Length == 0 || args[0] == "--seeds")
{
    foreach (uint seed in new uint[] { 1, 2, 3 })
    {
        var mps = new MapParams { Seed = seed };
        var ws = new VoxelWorld(mps.SizeX, mps.SizeY, mps.SizeZ);
        var ss = MapGen.Generate(ws, mps);
        int open = 0;
        for (int z = 1; z < ws.SZ - 1; z++)
            for (int x = 1; x < ws.SX - 1; x++)
            {
                int i = z * ws.SX + x;
                for (int y = ss.HTop[i]; y < ss.WaterTop[i]; y++)
                    foreach (var (sx, sz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        if (ws.Get(x + sx, y, z + sz) == Mat.Air) open++;
            }
        int mines = ss.Landmarks.Count(l => l.Kind == LandmarkKind.Mine), ruins = ss.Landmarks.Count(l => l.Kind == LandmarkKind.Ruin);
        Check($"seed {seed}: a whole valley", ss.KeepBase > 70 && open == 0 && mines == 3 && ruins == 6 && ss.Ponds.Count == 2,
            $"keep base {ss.KeepBase}, {open} open water faces, {mines} mines, {ruins} ruins, {ss.Ponds.Count} ponds");
    }
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
        int shoreR = MapGen.Shore(w4, site4, qx, qz, site4.KeepX - qx, site4.KeepZ - qz), farR = MapGen.Shore(w4, site4, qx, qz, qx - site4.KeepX, qz - site4.KeepZ);
        var edge = At(shoreR);
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
        var shore = At(shoreR - 3);
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
        var pit = (x: qx - (int)Math.Round((site4.KeepX - qx) / kd * (farR + 6)), z: qz - (int)Math.Round((site4.KeepZ - qz) / kd * (farR + 6)));
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
        Check("workers sent to a mine take it, and it pays while it is ours", sim.Mines.Count == 3 && ruins == 6 && taken && paid > copper.Income * .9,
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

        // everyone off to the citadel, the ore left be: the mine stands alone
        sim.CancelArea(copper.Place.X - 40, copper.Place.Z - 40, copper.Place.X + 40, copper.Place.Z + 40);
        var ours = sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player);
        sim.Command(ours, sim.Gate);
        Run(30);
        var raider = sim.Spawn(Kinds.Goblin, sim.NearestStand(copper.Place.StandX - 70, copper.Place.StandZ, 2));
        raider.Raid = copper;
        for (int t = 0; t < 90 && copper.Holder != Holder.Horde; t++) Run(1);
        Check("a raider left alone at a mine of ours takes it back", copper.Holder == Holder.Horde,
            $"{copper.Name} now {copper.Holder}, the goblin {(raider.Dead ? "dead" : "alive")}");
        raider.Dead = true; raider.DiedAt = -999;
        sim.ReleaseHold(ours);
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
        sim.DesignateRemove(Shapes.Line(w4, site4, qx - MapGen.Shore(w4, site4, qx, qz, -1, 0) + 6, qz, cliff, qz, new Stroke { Width = 6, Height = 16, Dig = true }, 0));
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

// ---- pawns left to themselves, on a fresh valley: no trudging to work that waits for stone, duties, militia, herds
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--life")
{
    Console.WriteLine();
    var mp6 = new MapParams();
    var w4 = new VoxelWorld(mp6.SizeX, mp6.SizeY, mp6.SizeZ);
    var site4 = MapGen.Generate(w4, mp6);
    w4.Compact();
    var sim = new Sim(w4, site4);
    sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
    void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
    // with no stone in the stores, workers don't trudge to a stone wall marked out; told to bring in stone, timber or
    // earth they find it for themselves; a herd doesn't stand in one heap; a worker called up meets a goblin
    {
        double stoneKept = sim.Stock[Stockpile.Stone];
        sim.Stock[Stockpile.Stone] = 0;
        sim.CancelArea(0, 0, w4.SX - 1, w4.SZ - 1);
        int wx = site4.KeepX - 120, wz = site4.KeepZ + 100;
        sim.InstantLabour = false;
        int marked = sim.DesignateBuild(Shapes.Line(w4, site4, wx, wz - 30, wx, wz + 30, new Stroke { Mat = Mat.Stone, Width = 8, Height = 24, Level = true }, Shapes.LevelTop(w4, wx, wz, 24)));
        var idlers = new System.Collections.Generic.List<Unit>();
        for (int i = 0; i < 3; i++) idlers.Add(sim.Spawn(Kinds.Worker, sim.Gate));
        Run(30);
        float nearest = float.MaxValue;
        foreach (var u in idlers) nearest = Math.Min(nearest, MathF.Abs(u.X - wx) + MathF.Abs(u.Z - wz) * 0f + Math.Max(0, MathF.Abs(u.Z - wz) - 30));
        string why = idlers[0].Why;
        Check("with no stone in the stores, workers don't trudge to a stone wall", marked > 1000 && nearest > 40 && why.Contains("камень"),
            $"{marked} voxels of stone wall marked, the nearest worker {nearest / VoxelWorld.PerMetre:0.0} m from it, saying «{why}»");
        sim.CancelArea(wx - 20, wz - 40, wx + 20, wz + 40);
        sim.Stock[Stockpile.Stone] = stoneKept;

        double s0 = sim.Stock[Stockpile.Stone], wd0 = sim.Stock[Stockpile.Wood], e0 = sim.Stock[Stockpile.Earth];
        sim.SetDuty(new[] { idlers[0] }, Stockpile.Stone);
        sim.SetDuty(new[] { idlers[1] }, Stockpile.Wood);
        sim.SetDuty(new[] { idlers[2] }, Stockpile.Earth);
        for (int t = 0; t < 150; t++) Run(1);
        float pitFar = idlers[2].DutyZone == null ? 0 : MathF.Sqrt(MathF.Pow((idlers[2].DutyZone.X0 + idlers[2].DutyZone.X1) / 2f - site4.KeepX, 2) + MathF.Pow((idlers[2].DutyZone.Z0 + idlers[2].DutyZone.Z1) / 2f - site4.KeepZ, 2));
        Check("told to bring in stone, timber and earth, workers find it for themselves", sim.Stock[Stockpile.Stone] > s0 + 300 && sim.Stock[Stockpile.Wood] > wd0 + 300 && sim.Stock[Stockpile.Earth] > e0 + 300 && pitFar > 140,
            $"stone +{Fmt.M3(sim.Stock[Stockpile.Stone] - s0)}, wood +{Fmt.M3(sim.Stock[Stockpile.Wood] - wd0)}, earth +{Fmt.M3(sim.Stock[Stockpile.Earth] - e0)} m³; the pit {pitFar / VoxelWorld.PerMetre:0} m from the citadel");
        sim.SetDuty(idlers, Stockpile.None);
        foreach (var z in sim.Zones.FindAll(z => z.Auto)) sim.RemoveZones(z.X0, z.Z0, z.X1, z.Z1);
        sim.CancelArea(0, 0, w4.SX - 1, w4.SZ - 1);

        var guardsman = idlers[0];
        sim.SetMilitia(new[] { guardsman }, true);
        var gob = sim.Spawn(Kinds.Goblin, sim.NearestStand((int)guardsman.X + 40, (int)guardsman.Z, 2));
        gob.Raid = null;
        bool fought = false;
        for (int t = 0; t < 20 * 20 && !gob.Dead; t++) { sim.Step(.05f); fought |= guardsman.State == UnitState.Fight; }
        Check("a worker called up meets a goblin instead of running", fought && guardsman.State != UnitState.Flee,
            $"fought {fought}, the goblin {(gob.Dead ? "dead" : "alive")}, the worker {Math.Ceiling(guardsman.Hp)} hp");
        gob.Dead = true; gob.DiedAt = -999;
        foreach (var u in idlers) { u.Dead = true; u.DiedAt = -999; }

        int herd = sim.SeedWildlife(4);
        Run(40);
        var cells = new System.Collections.Generic.Dictionary<int, int>();
        int stacked = 0;
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.Team != Team.Wild || u.State != UnitState.Idle) continue;
            if (cells.TryGetValue(u.Cell, out int n)) { stacked++; cells[u.Cell] = n + 1; } else cells[u.Cell] = 1;
        }
        Check("a herd doesn't stand in one heap", herd > 6 && stacked == 0, $"{herd} beasts, {stacked} standing in a cell with another");
        foreach (var u in sim.Units) if (u.Team == Team.Wild) { u.Dead = true; u.DiedAt = -999; }

        // the work table: a worker set never to dig leaves a pit marked at its feet alone, and digs it once allowed;
        // one set to fell first walks past a pit at its feet to a tree further off
        // the tree on the plateau nearest the citadel, at least 40 m off it
        int tx = -1, tz = -1;
        float treeFar = float.MaxValue;
        for (int z = 16; z < w4.SZ - 16; z += 2)
            for (int x = 16; x < w4.SX - 16; x += 2)
            {
                float d = MathF.Sqrt((x - site4.KeepX) * (x - site4.KeepX) + (z - site4.KeepZ) * (z - site4.KeepZ));
                if (d < 320 || d > treeFar || !Mat.IsTrunk(w4.Get(x, w4.SurfAt(x, z) + 2, z))) continue;
                if (w4.SurfAt(x, z) < site4.KeepBase - 16 || site4.WaterTop[z * w4.SX + x] > 0) continue;
                tx = x; tz = z; treeFar = d;
            }
        // orders of one kind of work left in a rectangle
        int Left(int x0, int z0, int x1, int z1, WorkKind kind)
        {
            int n = 0;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int y = Math.Max(0, w4.SurfAt(x, z) - 10); y < Math.Min(w4.SY, w4.SurfAt(x, z) + 100); y++)
                        if (sim.Orders.At(x, y, z) is var o && o != 0 && Work.Of(o) == kind) n++;
            return n;
        }
        var digger = sim.Spawn(Kinds.Worker, sim.NearestStand(tx + 100, tz, 4));
        digger.Priority[(int)WorkKind.Dig] = 0;
        int dx0 = (int)digger.X + 10, dz0 = (int)digger.Z - 6;
        sim.DesignateRemove(Shapes.Box(w4, site4, dx0, dz0, dx0 + 12, dz0 + 12, new Stroke { Height = 8, Dig = true }, 0));
        int pit0 = Left(dx0, dz0, dx0 + 12, dz0 + 12, WorkKind.Dig);
        Run(25);
        int pitBarred = Left(dx0, dz0, dx0 + 12, dz0 + 12, WorkKind.Dig);
        string barredWhy = digger.Why;
        digger.Priority[(int)WorkKind.Dig] = Work.Usual;
        Run(25);
        int pitAllowed = Left(dx0, dz0, dx0 + 12, dz0 + 12, WorkKind.Dig);
        Check("a worker set never to dig leaves the pit alone, and digs it once allowed", pit0 > 500 && pitBarred == pit0 && pitAllowed < pit0 * 3 / 4 && barredWhy.Contains("копать"),
            $"{pit0} voxels marked, {pitBarred} left after 25 s set never to dig («{barredWhy}»), {pitAllowed} once allowed");
        sim.CancelArea(dx0 - 4, dz0 - 4, dx0 + 16, dz0 + 16);
        digger.Dead = true; digger.DiedAt = -999;

        var feller = sim.Spawn(Kinds.Worker, sim.NearestStand(tx + 90, tz + 70, 4));
        feller.Priority[(int)WorkKind.Fell] = 1;
        int fx0 = (int)feller.X + 8, fz0 = (int)feller.Z - 6;
        sim.DesignateRemove(Shapes.Box(w4, site4, fx0, fz0, fx0 + 12, fz0 + 12, new Stroke { Height = 8, Dig = true }, 0));
        sim.DesignateRemove(Shapes.Tree(w4, tx, w4.SurfAt(tx, tz) + 2, tz));
        int pitF0 = Left(fx0, fz0, fx0 + 12, fz0 + 12, WorkKind.Dig), tree0 = Left(tx - 40, tz - 40, tx + 40, tz + 40, WorkKind.Fell);
        double treeAt = -1, pitAt = -1;
        float startFar = MathF.Sqrt((feller.X - tx) * (feller.X - tx) + (feller.Z - tz) * (feller.Z - tz));
        for (int t = 0; t < 40 && (treeAt < 0 || pitAt < 0); t++)
        {
            Run(1);
            if (treeAt < 0 && Left(tx - 40, tz - 40, tx + 40, tz + 40, WorkKind.Fell) < tree0) treeAt = t + 1;
            if (pitAt < 0 && Left(fx0, fz0, fx0 + 12, fz0 + 12, WorkKind.Dig) < pitF0) pitAt = t + 1;
        }
        Check("a worker set to fell first walks past the pit at its feet to a tree further off", tx >= 0 && pitF0 > 300 && treeAt > 0 && (pitAt < 0 || pitAt > treeAt),
            $"the tree {startFar / VoxelWorld.PerMetre:0} m off ({tree0} voxels to fell), the pit at its feet ({pitF0} to dig): felling from {treeAt} s, digging from {(pitAt < 0 ? "never" : pitAt + " s")}");
        sim.CancelArea(0, 0, w4.SX - 1, w4.SZ - 1);
        feller.Dead = true; feller.DiedAt = -999;

        // Ctrl+Z: the player's markings come off one by one, the last first, each as it was before it
        int ux = site4.KeepX - 120, uz = site4.KeepZ + 100;
        var earthWall = new Stroke { Mat = Mat.Rammed, Width = 8, Height = 12, Level = true };
        int t0 = sim.Orders.Total;
        sim.BeginMarking();
        sim.DesignateBuild(Shapes.Line(w4, site4, ux, uz - 30, ux, uz + 30, earthWall, Shapes.LevelTop(w4, ux, uz, 12)));
        sim.EndMarking("Стена");
        int tWall = sim.Orders.Total;
        sim.BeginMarking();
        sim.DesignateRemove(Shapes.Box(w4, site4, ux + 20, uz - 10, ux + 36, uz + 6, new Stroke { Width = 1, Height = 6, Dig = true }, 0));
        sim.EndMarking("Копать");
        int tPit = sim.Orders.Total;
        sim.BeginMarking();
        int cancelled = sim.CancelArea(ux - 8, uz - 30, ux + 8, uz);
        sim.EndMarking("Снятие разметки");
        int tCut = sim.Orders.Total;
        var un1 = sim.UndoMarking(out int back1);
        int tBack1 = sim.Orders.Total;
        var un2 = sim.UndoMarking(out _);
        int tBack2 = sim.Orders.Total;
        var un3 = sim.UndoMarking(out _);
        int tBack3 = sim.Orders.Total;
        var un4 = sim.UndoMarking(out _);
        Check("Ctrl+Z takes the markings back one by one, the last first, each as it was",
            tWall > t0 + 1000 && tPit > tWall + 300 && cancelled > 300 && tCut == tPit - cancelled && back1 == cancelled && tBack1 == tPit && tBack2 == tWall && tBack3 == t0
            && un4 == null && un1?.What == "Снятие разметки" && un2?.What == "Копать" && un3?.What == "Стена",
            $"wall +{tWall - t0}, pit +{tPit - tWall}, {cancelled} taken off; back: {un1?.What} {tBack1 - tCut:+0;-0}, {un2?.What} {tBack2 - tBack1:+0;-0}, {un3?.What} {tBack3 - tBack2:+0;-0}, then {un4?.What ?? "nothing"}");

        // what the workers have laid of a marking stays when it is taken back: only what is left comes off
        sim.Stock[Stockpile.Earth] = Math.Max(sim.Stock[Stockpile.Earth], 40 * VoxelWorld.PerM3);
        var mason = sim.Spawn(Kinds.Worker, sim.NearestStand(ux - 24, uz, 4));
        int Rammed()
        {
            int n = 0;
            for (int z = uz - 34; z <= uz + 34; z++)
                for (int x = ux - 6; x <= ux + 6; x++)
                    for (int y = 0; y < w4.SY; y++) if (w4.Get(x, y, z) == Mat.Rammed) n++;
            return n;
        }
        int r0 = Rammed(), b0 = sim.Orders.BuildTotal;
        sim.BeginMarking();
        int laid = sim.DesignateBuild(Shapes.Line(w4, site4, ux, uz - 30, ux, uz + 30, earthWall, Shapes.LevelTop(w4, ux, uz, 12)));
        Run(30);
        sim.EndMarking("Насыпь");
        int r1 = Rammed(), leftOf = sim.Orders.BuildTotal - b0;
        var un5 = sim.UndoMarking(out int back5);
        int r2 = Rammed();
        Check("what the workers have laid stays when its marking is taken back; only what is left comes off",
            r1 > r0 + 100 && r2 == r1 && back5 == leftOf && sim.Orders.BuildTotal == b0 && un5?.What == "Насыпь",
            $"{laid} voxels marked, {r1 - r0} laid in 30 s, {back5} of the {leftOf} left taken back, {r2 - r0} still standing");
        mason.Dead = true; mason.DiedAt = -999;

        // a plot taken back goes with the felling it had begun to mark; what it marks while the hand is on
        // something else is not the hand's
        (int x, int z) tree = (-1, -1);
        for (int r = 60; r < 500 && tree.x < 0; r += 8)
            for (int a = 0; a < 360 && tree.x < 0; a += 4)
            {
                int x = site4.KeepX + (int)(Math.Cos(a * Math.PI / 180) * r), z = site4.KeepZ + (int)(Math.Sin(a * Math.PI / 180) * r);
                if (x < 40 || z < 40 || x >= w4.SX - 40 || z >= w4.SZ - 40) continue;
                if (Mat.IsTrunk(w4.Get(x, w4.SurfAt(x, z) + 2, z))) tree = (x, z);
            }
        var woodman = sim.Spawn(Kinds.Worker, sim.NearestStand(tree.x + 30, tree.z, 4));
        sim.BeginMarking();
        var plot = sim.AddZone(tree.x - 20, tree.z - 20, tree.x + 20, tree.z + 20, Stockpile.Wood, 0);
        sim.EndMarking("Делянка");
        Run(10);
        sim.BeginMarking();
        int heap = sim.DesignateBuild(Shapes.Line(w4, site4, ux, uz - 6, ux, uz + 6, earthWall, Shapes.LevelTop(w4, ux, uz, 12)));
        Run(10);
        sim.EndMarking("Насыпь");
        int fed = plot.Marked;
        var un6 = sim.UndoMarking(out _);
        int felling = Left(tree.x - 20, tree.z - 20, tree.x + 20, tree.z + 20, WorkKind.Fell) + Left(tree.x - 20, tree.z - 20, tree.x + 20, tree.z + 20, WorkKind.Dig);
        var un7 = sim.UndoMarking(out _);
        int fellingAfter = Left(tree.x - 20, tree.z - 20, tree.x + 20, tree.z + 20, WorkKind.Fell) + Left(tree.x - 20, tree.z - 20, tree.x + 20, tree.z + 20, WorkKind.Dig);
        Check("a plot taken back goes with the felling it had marked; what it marks meanwhile is not the hand's",
            tree.x >= 0 && fed > 0 && un6 != null && un6.Voxels == heap && felling > 0 && un7?.What == "Делянка" && !sim.Zones.Contains(plot) && fellingAfter == 0,
            $"the plot round the tree at {tree} marked {fed} voxels; the heap marked meanwhile took back {un6?.Voxels} of its {heap}; {felling} orders of felling left, {fellingAfter} once the plot was taken back");
        woodman.Dead = true; woodman.DiedAt = -999;
        sim.CancelArea(0, 0, w4.SX - 1, w4.SZ - 1);
    }

}

// ---- a new game: the old fort round the citadel, gone half to ruin, and mending what was built
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
    int kx = site5.KeepX, kz = site5.KeepZ, kb = site5.KeepBase;

    // what the workers lay is remembered; what is knocked out stays remembered; what the player takes down is forgotten
    sim5.InstantLabour = true;
    var wallStroke = new Stroke { Mat = Mat.Stone, Width = 6, Height = 24, Level = true };
    int wx = kx + 150, wz0 = kz - 160, wz1 = kz - 120;
    int top5 = Shapes.LevelTop(w5, wx, wz0, 24);
    sim5.DesignateBuild(Shapes.Line(w5, site5, wx, wz0, wx, wz1, wallStroke, top5));
    bool remembered = true;
    for (int y = top5 - 20; y < top5; y++) remembered &= sim5.MeantAt(wx, y, kz - 140) == w5.Get(wx, y, kz - 140) && Mat.IsMasonry(w5.Get(wx, y, kz - 140));
    int knocked = 0;
    for (int y = top5 - 12; y < top5 - 4; y++) for (int z = kz - 142; z < kz - 136; z++) for (int x = wx - 3; x <= wx + 3; x++) if (w5.Set(x, y, z, Mat.Air)) knocked++;
    var lay5 = new VoxelList(); var clear5 = new VoxelList();
    sim5.MendPlan(sim5.BuildingFrom(sim5.MeantNear(wx, top5 - 2, kz - 150)), lay5, clear5);
    int planned = lay5.Count;
    sim5.Mend(lay5, clear5);
    bool asWas = true;
    for (int i = 0; i < lay5.Count; i++) asWas &= w5.Get(lay5.X(i), lay5.Y(i), lay5.Z(i)) == lay5.M(i) && Mat.IsMasonry(lay5.M(i));
    var down = new VoxelList();
    for (int y = 0; y < top5 + 2; y++) for (int z = wz0 - 4; z <= wz0 + 10; z++) for (int x = wx - 4; x <= wx + 4; x++) if (Mat.IsMasonry(w5.Get(x, y, z))) down.Add(x, y, z, 0);
    sim5.DesignateRemove(down);
    int still = 0;
    for (int y = 0; y < top5 + 2; y++) for (int z = wz0 - 4; z <= wz0 + 10; z++) for (int x = wx - 4; x <= wx + 4; x++) if (sim5.MeantAt(x, y, z) != 0) still++;
    sim5.InstantLabour = false;
    Check("what is built is remembered, a hole in it is mended stone for stone, what is taken down is forgotten",
        remembered && knocked > 200 && planned == knocked && asWas && down.Count > 1000 && still == 0,
        $"remembered {remembered}, {knocked} knocked out, {planned} planned back, as they were {asWas}, {down.Count} taken down, {still} still remembered there");

    var raise = System.Diagnostics.Stopwatch.StartNew();
    var gate5 = StartingFort.Raise(sim5);
    long raiseMs = raise.ElapsedMilliseconds;
    var (fx0, fz0, fx1, fz1) = sim5.FortArea;
    var (meant5, standing5) = sim5.Soundness(fx0, fz0, fx1, fz1);
    double whole0 = standing5 / (double)Math.Max(1, meant5);
    // the way in from the west is open (a breach, the empty gateway), and nothing is marked yet
    int outside = sim5.NearestStand(kx - 140, kz, 4);
    var probe = sim5.Spawn(Kinds.Worker, outside);
    bool way = sim5.GoTo(probe, sim5.Gate);
    probe.Path = null; probe.Dead = true; probe.DiedAt = -999;
    Check("a new game finds the old fort round the citadel half in ruins", gate5 != null && gate5.Broken && whole0 > .55 && whole0 < .9 && sim5.Orders.Total == 0 && way,
        $"{Fmt.M3(meant5)} m³ as it was built, {whole0:P0} of it standing, gate off its hinges {gate5?.Broken}, a way in {way}, raised in {raiseMs} ms");

    // what is left standing is dressed: torches inside, a banner on the tower, barrels and crates along the walls in the yard
    int torches5 = sim5.Fixtures.Count(f => f.Kind == FixtureKind.Torch), banners5 = sim5.Fixtures.Count(f => f.Kind == FixtureKind.Banner);
    int props5 = sim5.Fixtures.Count(f => f.Kind >= FixtureKind.Barrel);
    var torch5 = sim5.Fixtures.Find(f => f.Kind == FixtureKind.Torch);
    var (tax, tay, taz) = WaterPlanner.Unpack(torch5?.Anchor ?? 0);
    if (torch5 != null) w5.Set(tax, tay, taz, Mat.Air);
    Run5(2.5);
    bool fell5 = torch5 != null && !sim5.Fixtures.Contains(torch5) && torch5.Voxels.TrueForAll(v => w5.Get(v.x, v.y, v.z) == Mat.Air);
    Check("the fort is dressed by itself, and a torch goes with the stone it hung on", torches5 >= 4 && banners5 >= 1 && props5 >= 2 && fell5,
        $"{torches5} torches, {banners5} banners, {props5} barrels, crates and sacks; the torch taken down with its stone {fell5}");

    // a click on the west wall: the whole fort is marked to be mended, rubble on it to be cleared first, the gate to be hung
    var fort = sim5.BuildingFrom(sim5.MeantNear(kx - 84, kb + 3, kz - 22));
    var lay6 = new VoxelList(); var clear6 = new VoxelList();
    sim5.MendPlan(fort, lay6, clear6);
    int marked6 = sim5.Mend(lay6, clear6) + sim5.MendGate(gate5);
    Check("a click mends the whole of it: what fell to lay again, the rubble on it to clear, the gate to hang", fort.Count >= meant5 * 95 / 100 && lay6.Count > 5000 && marked6 >= lay6.Count + clear6.Count,
        $"{fort.Count} of {meant5} voxels in one building, {lay6.Count} to lay, {clear6.Count} to clear, {marked6} marked");
    double stone6 = sim5.Stock[Stockpile.Stone];
    for (int i = 0; i < 6; i++) sim5.Spawn(Kinds.Worker, sim5.Gate);
    Run5(150);
    var (_, standing6) = sim5.Soundness(fx0, fz0, fx1, fz1);
    Check("…and the workers set about it from the stores", standing6 > standing5 + 2000 && sim5.Stock[Stockpile.Stone] < stone6 - 2 * VoxelWorld.PerM3,
        $"{standing6 - standing5} voxels back in 2½ min, stone {Fmt.M3(stone6)} → {Fmt.M3(sim5.Stock[Stockpile.Stone])} m³");
    // the rest at once, as if by many hands; the workers out of the way first (an idle one may stand in a breach)
    foreach (var u in sim5.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
    sim5.InstantLabour = true;
    sim5.CompleteAll();
    sim5.InstantLabour = false;
    Run5(2);
    var (meant7, standing7) = sim5.Soundness(fx0, fz0, fx1, fz1);
    int exact = 0;
    foreach (int p in fort) { var (x, y, z) = WaterPlanner.Unpack(p); if (w5.Get(x, y, z) == sim5.MeantAt(x, y, z)) exact++; }
    Check("…to what it was, the gate swinging again", standing7 >= meant7 - meant7 / 200 && exact >= fort.Count * 97 / 100 && gate5.Ready && !gate5.Broken,
        $"{standing7} of {meant7} standing, {exact} of {fort.Count} exactly as built, gate ready {gate5.Ready}");
}

// ---- bridges: the old ones over the river, one whole and one fallen in; mending the fallen one; one of our own
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--bridges")
{
    Console.WriteLine();
    var mp8 = new MapParams();
    var w8 = new VoxelWorld(mp8.SizeX, mp8.SizeY, mp8.SizeZ);
    var site8 = MapGen.Generate(w8, mp8);
    w8.Compact();
    var sim8 = new Sim(w8, site8);
    sim8.Waves.Countdown = 1e9; sim8.WildTarget = 0;
    void Run8(double seconds) { for (double t = 0; t < seconds; t += .05) sim8.Step(.05f); }
    // the way from one end of a bridge to the other: over the deck (how high it goes over the water), or round, or none
    (bool way, int cells, int highest) Across(Landmark l, int sx, int sz, int ex, int ez)
    {
        int a = sim8.NearestStand(sx, sz, 4), b = sim8.NearestStand(ex, ez, 4);
        var walker = sim8.Spawn(Kinds.Worker, a);
        bool ok = sim8.GoTo(walker, b, budget: 200000);
        int n = ok ? walker.Path?.Count ?? 0 : 0, top = 0;
        if (ok && walker.Path != null) foreach (int c in walker.Path) top = Math.Max(top, sim8.Nav.Floor[c]);
        walker.Path = null; walker.Dead = true; walker.DiedAt = -999;
        return (ok, n, top);
    }
    (int ax, int az, int bx, int bz) Ends(Landmark l)
    {
        // the bridge runs from bank to bank across the river: its ends are where its stone meets dry ground, west and east
        int x0 = int.MaxValue, x1 = int.MinValue, za = l.Z, zb = l.Z;
        for (int z = l.Z - l.R; z <= l.Z + l.R; z++)
            for (int x = l.X - l.R; x <= l.X + l.R; x++)
            {
                if (!w8.InBounds(x, 0, z) || sim8.MeantAt(x, w8.SurfAt(x, z) - 1, z) == 0 && sim8.MeantAt(x, w8.SurfAt(x, z), z) == 0) continue;
                if (x < x0) { x0 = x; za = z; }
                if (x > x1) { x1 = x; zb = z; }
            }
        return (x0 + 4, za, x1 - 4, zb);
    }
    var whole = site8.Landmarks.Find(l => l.Name == "Старый мост");
    var fallen = site8.Landmarks.Find(l => l.Name == "Обрушенный мост");
    var (wa, wza, wb, wzb) = Ends(whole);
    var over = Across(whole, wa, wza, wb, wzb);
    Check("the old bridge carries the road over the river, arched", whole != null && over.way && over.highest > MapGen.RiverLevel + 6 && over.cells < (wb - wa) / 8 + 12,
        $"from x {wa} to {wb}: way {over.way}, {over.cells} cells, deck up to {over.highest} over the river at {MapGen.RiverLevel}");
    var (fa, fza, fb, fzb) = Ends(fallen);
    var broken = Across(fallen, fa, fza, fb, fzb);
    var span = sim8.BuildingFrom(sim8.MeantNear((fa + fb) / 2, MapGen.RiverLevel + 8, (fza + fzb) / 2));
    if (span.Count == 0) span = sim8.MeantIn(fa - 8, Math.Min(fza, fzb) - 12, fb + 8, Math.Max(fza, fzb) + 12);
    var lay8 = new VoxelList(); var clear8 = new VoxelList();
    sim8.MendPlan(span, lay8, clear8);
    sim8.InstantLabour = true;
    sim8.Mend(lay8, clear8);
    sim8.InstantLabour = false;
    Run8(1);
    var mended = Across(fallen, fa, fza, fb, fzb);
    Check("the fallen bridge cannot be crossed; mended, it carries across again", fallen != null && (!broken.way || broken.cells > mended.cells + 20) && lay8.Count > 500 && mended.way && mended.highest > MapGen.RiverLevel + 6,
        $"before: way {broken.way} in {broken.cells} cells; {lay8.Count} voxels laid again; after: {mended.cells} cells, deck up to {mended.highest}");

    // a timber bridge of our own over the river, further south, laid by the workers from the banks
    int bz8 = (int)(site8.KeepZ * 1.6);
    int rx8 = 0;
    for (int x = 40; x < 700; x++) if (site8.WaterTop[bz8 * w8.SX + x] > 0) { rx8 = x; break; }
    int ex8 = rx8;
    while (ex8 < w8.SX - 2 && site8.WaterTop[bz8 * w8.SX + ex8] > 0) ex8++;
    int sa = rx8 - 10, sb = ex8 + 10;
    var plan8 = Bridges.Plan(w8, site8, sa, bz8, sb, bz8, 10, Mat.Wood);
    var before = Across(null, sa - 6, bz8, sb + 6, bz8);
    sim8.DesignateBuild(plan8);
    sim8.Stock[Stockpile.Wood] = 60 * VoxelWorld.PerM3;
    for (int i = 0; i < 6; i++) sim8.Spawn(Kinds.Worker, sim8.NearestStand(sa - 12, bz8 + 4 * (i % 3), 4));
    Run8(240);
    var after = Across(null, sa - 6, bz8, sb + 6, bz8);
    Check("workers lay a timber bridge of their own from the banks outward, and it carries across", plan8.Count > 1000 && sim8.Orders.BuildTotal < plan8.Count / 10 && after.way && after.highest > MapGen.RiverLevel + 4 && after.cells <= before.cells,
        $"{plan8.Count} voxels over {sb - sa} of span at z {bz8}, {sim8.Orders.BuildTotal} left after 4 min; way across {before.cells} → {after.cells} cells, deck up to {after.highest}");
}

// ---- the horde in warbands: drawn up and shown before the wave, marching together, regrouping, storming, new leaders
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--bands")
{
    Console.WriteLine();
    var mp9 = new MapParams();
    var w9 = new VoxelWorld(mp9.SizeX, mp9.SizeY, mp9.SizeZ);
    var site9 = MapGen.Generate(w9, mp9);
    w9.Compact();
    var sim9 = new Sim(w9, site9);
    sim9.WildTarget = 0;
    sim9.WantTrails = true;
    void Run9(double seconds) { for (double t = 0; t < seconds; t += .05) sim9.Step(.05f); }
    sim9.Waves.Number = 3;
    sim9.PlanWave(4);
    sim9.ComputeFields(coming: true);
    Run9(2);
    var planned9 = sim9.Bands.FindAll(b => b.State == BandState.Coming);
    var goals9 = new System.Collections.Generic.HashSet<int>(sim9.Goals);
    bool led = planned9.TrueForAll(b => b.Roster.TrueForAll(t => Rank9(t) <= Rank9(b.Roster[0])));
    bool trails = planned9.TrueForAll(b => b.Trail.Count > 30 && goals9.Contains(b.Trail[^1]));
    Check("the next wave is drawn up in warbands, each with its strongest at its head, and their ways are shown", planned9.Count >= 2 && led && trails,
        string.Join("; ", planned9.ConvertAll(b => $"{b.Name}: {string.Join(", ", b.Roster.ConvertAll(t => t.Key))}, way {b.Trail.Count} cells, {b.Marks.Count} to break or build")));

    // five sent together from the gate walk side by side, each to a cell of its own
    {
        var five = new System.Collections.Generic.List<Unit>();
        for (int i = 0; i < 5; i++) five.Add(sim9.Spawn(Kinds.Worker, sim9.Gate));
        int to = sim9.NearestStand(site9.KeepX - 180, site9.KeepZ + 40, 4);
        var (gx9, _, gz9) = sim9.Nav.Feet(sim9.Gate);
        var (tx9, _, tz9) = sim9.Nav.Feet(to);
        float fx9 = tx9 - gx9, fz9 = tz9 - gz9, fl9 = MathF.Sqrt(fx9 * fx9 + fz9 * fz9);
        fx9 /= fl9; fz9 /= fl9;
        sim9.Command(five, to);
        double across = 0, cells = 0; int samples = 0;
        for (double t = 0; t < 30; t += .25)
        {
            Run9(.25);
            var walking = five.FindAll(u => u.Path != null && u.PathI < u.Path.Count);
            if (t < 4 || walking.Count < 4) continue;
            float lo = float.MaxValue, hi = float.MinValue;
            var occupied = new System.Collections.Generic.HashSet<int>();
            foreach (var u in walking) { float lat = (u.X - gx9) * -fz9 + (u.Z - gz9) * fx9; lo = Math.Min(lo, lat); hi = Math.Max(hi, lat); occupied.Add(u.Cell); }
            across += hi - lo; cells += occupied.Count / (double)walking.Count; samples++;
        }
        var stood = new System.Collections.Generic.HashSet<int>();
        int there = 0;
        foreach (var u in five)
        {
            stood.Add(u.Cell);
            if (Math.Max(Math.Abs(sim9.Nav.I(u.Cell) - sim9.Nav.I(u.Dest)), Math.Abs(sim9.Nav.K(u.Cell) - sim9.Nav.K(u.Dest))) <= 1) there++;
        }
        Check("five sent together walk side by side, not in one file, and stand each in a cell of its own", samples > 10 && across / samples > 2.5 * VoxelWorld.PerMetre
            && cells / samples > .8 && stood.Count == 5 && there == 5,
            $"{samples} looks on the way: {across / samples / VoxelWorld.PerMetre:0.0} m abreast, {cells / samples * 100:0} % in cells of their own; at the end {stood.Count} cells, {there} of 5 where sent");
        foreach (var u in five) { u.Dead = true; u.DiedAt = -999; }
    }

    sim9.Waves.Countdown = 0;
    float spread = 0;
    double abreast = 0, ownCells = 0; int marchLooks = 0;
    var seen = new System.Collections.Generic.HashSet<BandState>();
    Warband watched = null; Unit fallen = null; Unit successor = null;
    for (int t = 0; t < 360 && (successor == null || sim9.Bands.Exists(b => b.State == BandState.Rally)); t++)
    {
        Run9(1);
        foreach (var b in sim9.Bands)
        {
            foreach (var st in b.History) seen.Add(st);
            if (b.State == BandState.March && sim9.Time - b.Since > 6 && b.Leader != null)
            {
                foreach (var u in b.Members)
                    if (!u.Dead) spread = Math.Max(spread, MathF.Sqrt((u.X - b.Leader.X) * (u.X - b.Leader.X) + (u.Z - b.Leader.Z) * (u.Z - b.Leader.Z)));
                // how wide it goes across its way, and how many walk in a cell of their own
                if (b.Leader.State == UnitState.Walk && b.Alive >= 4)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    var occupied = new System.Collections.Generic.HashSet<int>();
                    foreach (var u in b.Members)
                    {
                        if (u.Dead) continue;
                        float lat = (u.X - b.Leader.X) * -b.HZ + (u.Z - b.Leader.Z) * b.HX;
                        lo = Math.Min(lo, lat); hi = Math.Max(hi, lat); occupied.Add(u.Cell);
                    }
                    abreast += hi - lo; ownCells += occupied.Count / (double)b.Alive; marchLooks++;
                }
            }
            // the leader of the first band to storm is struck down
            if (b.State == BandState.Storm && watched == null && b.Alive > 2) { watched = b; fallen = b.Leader; fallen.Dead = true; fallen.DiedAt = sim9.Time; }
        }
        if (watched != null && successor == null && watched.Leader != fallen && watched.Leader is { Dead: false } nl) successor = nl;
    }
    Check("a warband marches abreast, each in a cell of its own, not in one file", marchLooks > 10 && abreast / marchLooks > 2 * VoxelWorld.PerMetre && ownCells / marchLooks > .85,
        $"{marchLooks} looks on the march: {abreast / Math.Max(1, marchLooks) / VoxelWorld.PerMetre:0.0} m abreast, {ownCells / Math.Max(1, marchLooks) * 100:0} % in cells of their own");
    Check("warbands gather, march together, regroup before the walls and storm; a fallen leader is replaced", spread < 22 * VoxelWorld.PerMetre
        && seen.Contains(BandState.Forming) && seen.Contains(BandState.March) && seen.Contains(BandState.Rally) && seen.Contains(BandState.Storm) && successor is { Leader: true },
        $"widest spread on the march {spread / VoxelWorld.PerMetre:0.0} m; states {string.Join(" → ", seen)}; leader {fallen?.T.Key} replaced by {successor?.T.Key}");
    static int Rank9(UnitType t) => t == Kinds.Troll ? 5 : t == Kinds.Orc ? 4 : t == Kinds.Miner ? 3 : t == Kinds.Goblin ? 2 : 1;
}

// ---- wits: the horde looks after its own skin and its own kind, our people after each other
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--wits")
{
    Console.WriteLine();
    var mpW = new MapParams();
    var wW = new VoxelWorld(mpW.SizeX, mpW.SizeY, mpW.SizeZ);
    var siteW = MapGen.Generate(wW, mpW);
    wW.Compact();
    var simW = new Sim(wW, siteW);
    simW.Waves.Countdown = 1e9; simW.WildTarget = 0;
    simW.ComputeFields();
    void RunW(double seconds) { for (double t = 0; t < seconds; t += .05) simW.Step(.05f); }
    void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
    int kxW = siteW.KeepX, kzW = siteW.KeepZ;

    // a raider hurt to a fifth of itself turns back west
    var sore = simW.Spawn(Kinds.Orc, simW.NearestStand(kxW - 250, kzW, 2));
    simW.Hurt(sore, null, sore.MaxHp * .8f);
    float sx0 = sore.X;
    RunW(8);
    Check("a raider hurt to a fifth of itself turns back west", sore.Routed && sore.X < sx0 - 40, $"routed {sore.Routed}, x {sx0:0} → {sore.X:0}, {sore.State}");
    Gone(sore);

    // raiders see one of their own fighting and come to help it
    var guard = simW.Spawn(Kinds.Swordsman, simW.NearestStand(kxW - 230, kzW - 140, 4));
    simW.Command(new[] { guard }, guard.Cell);
    RunW(1);
    var biter = simW.Spawn(Kinds.Goblin, simW.NearestStand((int)guard.X + 9, (int)guard.Z, 2));
    var helper = simW.Spawn(Kinds.Goblin, simW.NearestStand((int)guard.X, (int)guard.Z + 120, 2));
    float hx0 = helper.X, hz0 = helper.Z, toGuardX = guard.X - hx0, toGuardZ = guard.Z - hz0, toGuard = MathF.Sqrt(toGuardX * toGuardX + toGuardZ * toGuardZ);
    RunW(2.5);
    float came = ((helper.X - hx0) * toGuardX + (helper.Z - hz0) * toGuardZ) / toGuard;
    Check("raiders see one of their own fighting and come to help it", biter.State == UnitState.Fight || biter.Dead ? came > 32 : false,
        $"{toGuard / VoxelWorld.PerMetre:0.0} m off, came {came / VoxelWorld.PerMetre:0.0} m toward the fight, {helper.State}; the first {biter.State}");
    Gone(biter); Gone(helper); Gone(guard);

    // a band that has lost its leader and two in three of its own breaks and runs
    var band = new Warband { Id = 900, State = BandState.Coming, Name = "Ватага Проверки", Entry = simW.NearestStand(kxW - 320, kzW + 90, 2) };
    for (int i = 0; i < 6; i++) band.Roster.Add(Kinds.Goblin);
    simW.Bands.Add(band);
    for (int i = 0; i < 6; i++) simW.SpawnRaider(Kinds.Goblin, band);
    RunW(1);
    band.Leader.Dead = true; band.Leader.DiedAt = simW.Time;
    RunW(.3);
    int felled = 0;
    foreach (var m in band.Members) if (!m.Dead && !m.Leader && felled < 3) { m.Dead = true; m.DiedAt = simW.Time; felled++; }
    var left = band.Members.FindAll(m => !m.Dead);
    float lx0 = 0; foreach (var m in left) lx0 += m.X / left.Count;
    RunW(6);
    float lx1 = 0; foreach (var m in left) lx1 += m.X / left.Count;
    Check("a band that has lost its leader and two in three of its own breaks and runs", band.Broken && left.TrueForAll(m => m.Routed) && lx1 < lx0 - 40,
        $"broken {band.Broken}, {left.Count} left, all running {left.TrueForAll(m => m.Routed)}, x {lx0:0} → {lx1:0}");
    foreach (var m in band.Members) Gone(m);
    simW.Bands.Remove(band);

    // one that gets away alive comes back with the next wave, as hurt as it went, mended halfway
    var runner = simW.Spawn(Kinds.Orc, simW.NearestStand(siteW.SpawnStrip + 40, kzW, 2));
    simW.Hurt(runner, null, runner.MaxHp * .8f);
    for (int t = 0; t < 30 && !runner.Dead; t++) RunW(.5);
    bool away = simW.Escaped.Count == 1 && simW.Escaped[0].Kind == Kinds.Orc;
    simW.PlanWave(2);
    var withIt = simW.Bands.Find(b => b.State == BandState.Coming && b.Veterans.Count > 0);
    simW.Waves.Countdown = 0;
    Unit back = null;
    for (int t = 0; t < 80 && back == null; t++) { RunW(.5); back = simW.Units.Find(u => u.Returned && !u.Dead); }
    Check("a raider that gets away alive comes back with the next wave, as hurt as it went, mended halfway", away && withIt != null && back is { T: var bt } && bt == Kinds.Orc && back.Hp < back.MaxHp * .7f && back.Band == withIt,
        $"got away {away}, the next wave's {withIt?.Name} brings {withIt?.Veterans.Count} back; back: {back?.T.Name} with {back?.Hp:0} of {back?.MaxHp:0}");
    simW.Waves.CallOff(simW);
    RunW(.1);

    // out of the fight our people mend, faster by the citadel
    var athome = simW.Spawn(Kinds.Worker, simW.Gate);
    var afield = simW.Spawn(Kinds.Worker, simW.NearestStand(kxW - 420, kzW + 200, 4));
    foreach (var u in new[] { athome, afield }) { simW.Command(new[] { u }, u.Cell); u.Hp = u.MaxHp * .5f; u.LastHurt = simW.Time; }
    RunW(40);
    float homeGain = athome.Hp / athome.MaxHp - .5f, fieldGain = afield.Hp / afield.MaxHp - .5f;
    Check("out of the fight our people mend, faster resting by the citadel", homeGain > .07f && fieldGain > .02f && homeGain > fieldGain * 2,
        $"in 40 s: by the citadel +{homeGain * 100:0} %, out in the valley +{fieldGain * 100:0} %");
    Gone(athome); Gone(afield);

    // a swordsman cut down to a third of himself leaves his post to mend by the citadel, and goes back to it mended
    int post = simW.NearestStand(kxW - 300, kzW - 60, 4);
    var cut = simW.Spawn(Kinds.Swordsman, post);
    simW.Command(new[] { cut }, post);
    simW.ReleaseHold(new[] { cut });
    cut.Post = post;
    RunW(1);
    cut.Hp = cut.MaxHp * .3f; cut.LastHurt = simW.Time;
    var (gxW, _, gzW) = simW.Nav.Feet(simW.Gate);
    float far0 = MathF.Sqrt((cut.X - gxW) * (cut.X - gxW) + (cut.Z - gzW) * (cut.Z - gzW));
    for (int t = 0; t < 60 && MathF.Sqrt((cut.X - gxW) * (cut.X - gxW) + (cut.Z - gzW) * (cut.Z - gzW)) > 24; t++) RunW(.5);
    float far1 = MathF.Sqrt((cut.X - gxW) * (cut.X - gxW) + (cut.Z - gzW) * (cut.Z - gzW));
    bool fell = cut.Mending;
    cut.Hp = cut.MaxHp * .8f;
    for (int t = 0; t < 80 && simW.Nav.I(cut.Cell) != simW.Nav.I(post) | simW.Nav.K(cut.Cell) != simW.Nav.K(post); t++) RunW(.5);
    var (pxW, _, pzW) = simW.Nav.Feet(post);
    float atPost = MathF.Sqrt((cut.X - pxW) * (cut.X - pxW) + (cut.Z - pzW) * (cut.Z - pzW));
    Check("a swordsman cut down to a third of himself falls back to mend by the citadel, and mended goes back to his post", fell && far1 < 32 && !cut.Mending && atPost < 16,
        $"{far0 / VoxelWorld.PerMetre:0} m from the citadel → {far1 / VoxelWorld.PerMetre:0.0} m, mending {fell}; mended, {atPost / VoxelWorld.PerMetre:0.0} m from his post");
    Gone(cut);

    // one bleeding is seen by a worker near, who binds the wound
    var bleeder = simW.Spawn(Kinds.Archer, simW.NearestStand(kxW - 120, kzW + 40, 4));
    simW.Command(new[] { bleeder }, bleeder.Cell);
    var binder = simW.Spawn(Kinds.Worker, simW.NearestStand(kxW - 120, kzW + 120, 4));
    bleeder.Wounds = new BodyState(Bodies.Plan(bleeder.T, bleeder.Look)) { Bleed = 1 };
    bleeder.LastHurt = simW.Time;
    bool bound = false;
    double bindFrom = simW.Time;
    for (int t = 0; t < 40 && !bound; t++) { RunW(.5); bound = bleeder.Wounds.Bleed < .2f; }
    Check("a worker sees one of ours bleeding and binds the wound", bound && binder.Patient == null,
        $"bleeding 1 → {bleeder.Wounds.Bleed:0.00} in {simW.Time - bindFrom:0} s, the worker {binder.State}");
    Gone(bleeder); Gone(binder);

    // a swordsman comes to the help of a worker set upon beyond his post
    int post2 = simW.NearestStand(kxW - 200, kzW + 160, 4);
    var keeper = simW.Spawn(Kinds.Swordsman, post2);
    keeper.Post = post2;
    var victim = simW.Spawn(Kinds.Worker, simW.NearestStand(kxW - 200, kzW + 300, 4));
    simW.Command(new[] { victim }, victim.Cell);
    var attacker = simW.Spawn(Kinds.Goblin, simW.NearestStand((int)victim.X + 9, (int)victim.Z, 2));
    attacker.Hp = attacker.MaxHp * 3; attacker.MaxHp *= 3;
    float apart0 = Sim.Dist(keeper, attacker), nearest = apart0;
    for (int t = 0; t < 32 && nearest > 40; t++) { RunW(.5); nearest = Math.Min(nearest, Sim.Dist(keeper, attacker)); }
    Check("a swordsman comes to the help of a worker set upon beyond his post", apart0 > 120 && nearest <= 40,
        $"the goblin {apart0 / VoxelWorld.PerMetre:0} m from the swordsman at first, {nearest / VoxelWorld.PerMetre:0.0} m at the nearest; the swordsman {keeper.State}");
    Gone(keeper); Gone(victim); Gone(attacker);

    // the chief, knowing the defences, sends the bands at a side none of ours keep, and they gather there to storm
    var watch = new System.Collections.Generic.List<Unit>();
    for (int i = 0; i < 6; i++)
    {
        int at = simW.NearestStand(kxW - 200 + i % 3 * 16, kzW - 24 + i / 3 * 48, 4);
        var a = simW.Spawn(Kinds.Archer, at);
        simW.Command(new[] { a }, at);
        watch.Add(a);
    }
    simW.ComputeFields(coming: true);
    simW.PlanWave(3);
    simW.ChooseSides(1);
    var sent = simW.Bands.FindAll(b => b.State == BandState.Coming && b.Target == null);
    bool weak = sent.Count > 0 && sent.TrueForAll(b => b.Side >= 0 && simW.SideKeep[b.Side] < .5f);
    string where = string.Join("; ", sent.ConvertAll(b => b.Side >= 0 ? $"{b.Name} on the {b.Side * 45}° side ({simW.SideKeep[b.Side]:0.0} of ours there): {b.Why}" : $"{b.Name}: none"));
    simW.Waves.Countdown = 0;
    Warband gathered = null;
    for (int t = 0; t < 400 && gathered == null; t++) { RunW(.5); gathered = simW.Bands.Find(b => b.State == BandState.Rally && b.RallyCell >= 0); }
    int off = gathered?.Leader is { Dead: false } gl ? Math.Max(Math.Abs(simW.Nav.I(gl.Cell) - simW.Nav.I(gathered.RallyCell)), Math.Abs(simW.Nav.K(gl.Cell) - simW.Nav.K(gathered.RallyCell))) : 99;
    Check("knowing the defences, the chief sends the bands at a side none of ours keep, and they gather there to storm", weak && gathered != null && off <= 4,
        $"west kept by {watch.Count} archers; {where}; {gathered?.Name ?? "nobody"} gathered {off} cells from its place, {simW.Time:0} s");
    simW.Waves.CallOff(simW);
    foreach (var a in watch) Gone(a);
}

// ---- fire: timber burns and stone does not, water stops it, fire arrows and torches set things alight, pitch burns
// hot under the horde, and our people beat fires out
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--fire")
{
    Console.WriteLine();
    var mpF = new MapParams();
    var wF = new VoxelWorld(mpF.SizeX, mpF.SizeY, mpF.SizeZ);
    var siteF = MapGen.Generate(wF, mpF);
    wF.Compact();
    var simF = new Sim(wF, siteF);
    simF.Waves.Countdown = 1e9; simF.WildTarget = 0;
    void RunF(double seconds) { for (double t = 0; t < seconds; t += .05) simF.Step(.05f); }
    void GoneF(Unit u) { u.Dead = true; u.DiedAt = -999; }
    int kxF = siteF.KeepX, kzF = siteF.KeepZ;
    int CountF(byte m, int x0, int z0, int x1, int z1)
    {
        int n = 0;
        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) for (int y = 0; y < wF.SY; y++) if (wF.Get(x, y, z) == m) n++;
        return n;
    }
    int BurningIn(int x0, int z0, int x1, int z1)
    {
        int n = 0;
        foreach (int p in simF.Burning.Keys) { var (x, _, z) = WaterPlanner.Unpack(p); if (x >= x0 && x <= x1 && z >= z0 && z <= z1) n++; }
        return n;
    }
    // a wall of logs or stone a man high, following the ground
    void WallF(int x0, int z0, int x1, int z1, int high, Func<int, int, byte> mat)
    {
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int g = wF.SurfAt(x, z);
                for (int y = g; y < g + high; y++) wF.Set(x, y, z, mat(x, z));
            }
    }

    // a wall half of logs and half of stone, set alight in the middle of the logs: the logs burn down, the stone stands
    int fx0 = kxF - 130, fz0 = kzF + 90, baseF = wF.SurfAt(fx0, fz0 + 20);
    WallF(fx0, fz0, fx0 + 3, fz0 + 79, 16, (x, z) => z < fz0 + 40 ? Mat.Wood : Mat.Stone);
    int wood0F = CountF(Mat.Wood, fx0, fz0, fx0 + 3, fz0 + 79), stone0F = CountF(Mat.Stone, fx0, fz0, fx0 + 3, fz0 + 79);
    bool litF = simF.Ignite(fx0 + 1, baseF + 1, fz0 + 20);
    int mostF = 0;
    for (int t = 0; t < 420; t++) { RunF(1); mostF = Math.Max(mostF, BurningIn(fx0, fz0, fx0 + 3, fz0 + 79)); }
    int wood1F = CountF(Mat.Wood, fx0, fz0, fx0 + 3, fz0 + 79), stone1F = CountF(Mat.Stone, fx0, fz0, fx0 + 3, fz0 + 79), charF = CountF(Mat.Char, fx0, fz0, fx0 + 3, fz0 + 79);
    int leftF = BurningIn(fx0, fz0, fx0 + 3, fz0 + 79);
    Check("a wall of logs set alight burns down slowly, and the stone wall beside it does not catch",
        litF && wood1F < wood0F * .5 && mostF < wood0F / 3 && stone1F == stone0F && charF > 0,
        $"{wood0F} voxels of logs → {wood1F} ({charF} charred) in 7 min, up to {mostF} burning at once; stone {stone0F} → {stone1F}; {leftF} still burning");

    // a floor of logs over a slab of stone with a pool sunk in it: what lies on the water never catches, the rest burns
    int sx0 = kxF - 200, sz0 = kzF - 40, sg = 0;
    for (int z = sz0; z < sz0 + 10; z++) for (int x = sx0; x < sx0 + 30; x++) sg = Math.Max(sg, wF.SurfAt(x, z));
    for (int z = sz0; z < sz0 + 10; z++)
        for (int x = sx0; x < sx0 + 30; x++)
        {
            for (int y = sg; y < sg + 24; y++) wF.Set(x, y, z, Mat.Air);
            wF.Set(x, sg, z, Mat.Stone); wF.Set(x, sg + 1, z, Mat.Stone);
        }
    for (int z = sz0 + 2; z < sz0 + 8; z++) for (int x = sx0 + 18; x < sx0 + 28; x++) wF.Set(x, sg + 1, z, Mat.Water);
    var deck = new System.Collections.Generic.List<(int x, int z, bool wet)>();
    for (int z = sz0; z < sz0 + 10; z++)
        for (int x = sx0; x < sx0 + 30; x++)
        {
            wF.Set(x, sg + 2, z, Mat.Wood);
            deck.Add((x, z, wF.Get(x, sg + 1, z) == Mat.Water));
        }
    RunF(.5);
    int wetDeck = deck.FindAll(d => d.wet).Count, dryDeck = deck.Count - wetDeck;
    bool farLit = simF.Ignite(sx0 + 1, sg + 2, sz0 + 5), wetLit = simF.Ignite(sx0 + 22, sg + 2, sz0 + 5);
    RunF(240);
    int wetLeft = deck.FindAll(d => d.wet && wF.Get(d.x, sg + 2, d.z) == Mat.Wood).Count, dryLeft = deck.FindAll(d => !d.wet && wF.Get(d.x, sg + 2, d.z) == Mat.Wood).Count;
    Check("a floor of logs over stone and a pool: what lies on the water never catches, the rest burns",
        farLit && !wetLit && wetLeft == wetDeck && dryLeft < dryDeck / 2,
        $"{wetDeck} voxels on the water, {wetLeft} left; {dryDeck} on the stone, {dryLeft} left; {BurningIn(sx0, sz0, sx0 + 29, sz0 + 9)} burning");

    // an archer with fire arrows told to set a post of logs alight does; a goblin struck by one burns
    int px0F = kxF - 110, pz0F = kzF - 120;
    var bowman = simF.Spawn(Kinds.Archer, simF.NearestStand(px0F, pz0F, 4));
    simF.Command(new[] { bowman }, bowman.Cell);
    simF.SetFireArrows(new[] { bowman }, true);
    int postX = (int)bowman.X + 100, postZ = (int)bowman.Z, postY = wF.SurfAt(postX, postZ);
    WallF(postX, postZ, postX + 1, postZ + 1, 14, (x, z) => Mat.Wood);
    int post0 = CountF(Mat.Wood, postX, postZ, postX + 1, postZ + 1);
    int told = simF.KindleOrder(new[] { bowman }, postX, postY + 8, postZ);
    double caughtAt = -1;
    for (int t = 0; t < 30 && caughtAt < 0; t++) { RunF(1); if (BurningIn(postX, postZ, postX + 1, postZ + 1) > 0 || CountF(Mat.Wood, postX, postZ, postX + 1, postZ + 1) < post0) caughtAt = t + 1; }
    // a goblin where the archer can see it
    Unit gobF = null;
    foreach (var (gx, gz) in new[] { (-70, 20), (-60, -30), (40, 60), (50, -60), (-90, 0), (0, 80), (0, -80) })
    {
        gobF = simF.Spawn(Kinds.Goblin, simF.NearestStand((int)bowman.X + gx, (int)bowman.Z + gz, 2));
        gobF.Raid = null;
        if (simF.Sight(bowman, gobF)) break;
        GoneF(gobF);
    }
    float burnt = 0;
    for (int t = 0; t < 12 * 20 && !gobF.Dead; t++) { simF.Step(.05f); burnt = Math.Max(burnt, gobF.Burn); }
    Check("an archer with fire arrows sets a post of logs alight as told, and a goblin struck by one burns",
        told == 1 && caughtAt > 0 && burnt > 0, $"the post caught after {caughtAt} s; the goblin burnt {burnt:0.0} s at most, {(gobF.Dead ? "dead" : $"{gobF.Hp:0} hp")}; " +
        $"the archer {bowman.State}, target {bowman.Target?.T.Name ?? "none"}, {simF.Shots} shots, {Sim.Dist(bowman, gobF) / VoxelWorld.PerMetre:0.0} m, sight {simF.Sight(bowman, gobF)}");
    GoneF(gobF); GoneF(bowman);
    simF.Burning.Clear();

    // a goblin with a torch comes to a ring of logs round the citadel: it sets the timber alight and goes through
    simF.InstantLabour = true;
    simF.DesignateBuild(Shapes.Circle(wF, siteF, kxF, kzF, siteF.KeepR + 44, new Stroke { Mat = Mat.Wood, Width = 8, Height = 48, Hollow = true }, 0));
    simF.InstantLabour = false;
    simF.HordeStock[Stockpile.Wood] = simF.HordeStock[Stockpile.Earth] = 0;
    simF.ComputeFields();
    float DistKeepF(Unit u) => MathF.Sqrt((u.X - kxF) * (u.X - kxF) + (u.Z - kzF) * (u.Z - kzF));
    int caught = 0;
    simF.Caught = (x, y, z, m) => caught++;
    var torchGob = simF.Spawn(Kinds.Goblin, simF.NearestStand(kxF - siteF.KeepR - 100, kzF, 2));
    torchGob.Torch = true;
    float dg0 = DistKeepF(torchGob);
    string sawKindle = "";
    for (int t = 0; t < 150 && DistKeepF(torchGob) > siteF.KeepR + 30; t++) { RunF(1); if (torchGob.Kindling > 0) sawKindle = "kindled"; }
    float dg1 = DistKeepF(torchGob);
    Check("a goblin with a torch sets a ring of logs alight and goes through it", caught >= 5 && dg1 < siteF.KeepR + 40,
        $"{caught} voxels caught ({sawKindle}), {dg0 / VoxelWorld.PerMetre:0.0} → {dg1 / VoxelWorld.PerMetre:0.0} m from the citadel, {torchGob.State}");
    GoneF(torchGob);
    if (Environment.GetEnvironmentVariable("FIRE_DEBUG") != null)
        for (int t = 0; t < 12; t++) { RunF(5); Console.WriteLine($"  +{(t + 1) * 5} s: {simF.Burning.Count} burning, {caught} caught"); }
    simF.Caught = null;
    simF.Burning.Clear();

    // workers put a fire out in a wall of ours before it eats much of it
    int bx0F = kxF - 100, bz0F = kzF - 210;
    WallF(bx0F, bz0F, bx0F + 3, bz0F + 47, 16, (x, z) => Mat.Wood);
    int bw0 = CountF(Mat.Wood, bx0F, bz0F, bx0F + 3, bz0F + 47);
    var crewF = new System.Collections.Generic.List<Unit>();
    for (int i = 0; i < 3; i++) crewF.Add(simF.Spawn(Kinds.Worker, simF.NearestStand(bx0F - 30, bz0F + 10 + i * 12, 2)));
    int bbase = wF.SurfAt(bx0F - 1, bz0F + 24);
    foreach (int dz in new[] { 8, 20, 32, 44 }) simF.Ignite(bx0F, wF.SurfAt(bx0F - 2, bz0F + dz) + 3, bz0F + dz);
    int fightersSeen = 0;
    for (int t = 0; t < 60 && BurningIn(bx0F, bz0F, bx0F + 3, bz0F + 47) > 0; t++)
    {
        RunF(1);
        fightersSeen = Math.Max(fightersSeen, crewF.FindAll(u => u.FireCell >= 0).Count);
        if (Environment.GetEnvironmentVariable("FIRE_DEBUG") != null)
            Console.WriteLine($"  t {t}: {BurningIn(bx0F, bz0F, bx0F + 3, bz0F + 47)} burning; " + string.Join("; ", crewF.ConvertAll(u => $"#{u.Id} {u.State} fire {u.FireCell} at {u.X - bx0F:0},{u.Y - bbase:0},{u.Z - bz0F:0} path {(u.Path == null ? -1 : u.Path.Count - u.PathI)} burn {u.Burn:0.0} hp {u.Hp:0} why {u.Why}")));
    }
    int bw1 = CountF(Mat.Wood, bx0F, bz0F, bx0F + 3, bz0F + 47), stillF = BurningIn(bx0F, bz0F, bx0F + 3, bz0F + 47);
    Check("workers put out a fire in a wall of ours before it eats much of it", stillF == 0 && bw1 > bw0 * .85 && fightersSeen >= 2,
        $"{fightersSeen} went to it; {stillF} still burning after {simF.Time:0} s, {bw1} of {bw0} voxels of logs left");
    foreach (var u in crewF) GoneF(u);

    // goblins standing on pitch set alight burn, twice as hot as in timber
    int qx0 = kxF - 60, qz0 = kzF + 200;
    for (int z = qz0; z < qz0 + 48; z++) for (int x = qx0; x < qx0 + 48; x++) wF.Set(x, wF.SurfAt(x, z) - 1, z, Mat.Pitch);
    var onPitch = new System.Collections.Generic.List<Unit>();
    foreach (var (dx, dz) in new[] { (16, 16), (24, 30), (32, 20) })
    {
        var g = simF.Spawn(Kinds.Goblin, simF.NearestStand(qx0 + dx, qz0 + dz, 2));
        g.Raid = null; g.Band = null;
        onPitch.Add(g);
    }
    // a fire arrow at the feet of each
    int litPitch = 0;
    foreach (var g in onPitch)
        for (int y = (int)g.Y + 1; y > g.Y - 4; y--)
            if (wF.Get((int)g.X, y, (int)g.Z) == Mat.Pitch) { if (simF.Ignite((int)g.X, y, (int)g.Z)) litPitch++; break; }
    RunF(6);
    int scorched = onPitch.FindAll(g => g.Dead || g.Hp < g.MaxHp * .4f).Count;
    Check("goblins on pitch set alight burn, and the pitch clings as they run off it", scorched >= 2,
        string.Join(", ", onPitch.ConvertAll(g => (g.Dead ? "dead" : $"{g.Hp:0} of {g.MaxHp:0} hp") + $" at {g.X - qx0:0},{g.Z - qz0:0}")) + $"; {BurningIn(qx0, qz0, qx0 + 47, qz0 + 47)} burning, {litPitch} lit");
    foreach (var g in onPitch) GoneF(g);
    simF.Burning.Clear();

    // a worker pours a line of pitch from the timber in the stores
    simF.Stock[Stockpile.Wood] = 10 * VoxelWorld.PerM3;
    double woodF0 = simF.Stock[Stockpile.Wood];
    int lx0 = kxF - 90, lz0 = kzF + 130;
    var pourer = simF.Spawn(Kinds.Worker, simF.NearestStand(lx0 - 16, lz0, 2));
    int poured0 = CountF(Mat.Pitch, lx0 - 4, lz0 - 4, lx0 + 4, lz0 + 40);
    simF.DesignateBuild(Shapes.Line(wF, siteF, lx0, lz0, lx0, lz0 + 32, new Stroke { Mat = Mat.Pitch, Width = 4, Height = 2 }, 0));
    RunF(40);
    int poured = CountF(Mat.Pitch, lx0 - 4, lz0 - 4, lx0 + 4, lz0 + 40) - poured0;
    Check("a worker pours pitch from the timber in the stores", poured > 100 && simF.Stock[Stockpile.Wood] <= woodF0 - poured + 1,
        $"{poured} voxels poured, timber {Fmt.M3(woodF0)} → {Fmt.M3(simF.Stock[Stockpile.Wood])} m³");
    GoneF(pourer);
}

// ---- bodies of voxels: a skeleton inside, blows carving out what they strike, limbs cut off, vital wounds, bleeding
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--bodies")
{
    Console.WriteLine();
    // every kind in every look: whole on the outside, the body's insides hidden under the skin
    int looks = 0, showing = 0;
    foreach (var t in Kinds.PlayerKinds.Concat(Kinds.HordeKinds).Concat(Kinds.WildKinds))
        for (int l = 0; l < Bodies.Looks(t); l++)
        {
            var plan = Bodies.Plan(t, l);
            looks++;
            foreach (var g in plan.Grid)
            {
                if (g == null) continue;
                for (int i = 0; i < g.M.Length; i++)
                {
                    if (g.T[i] is not ((byte)Tissue.Flesh or (byte)Tissue.Bone or (byte)Tissue.Brain or (byte)Tissue.Heart)) continue;
                    int x = i % g.SX + g.X0, z = i / g.SX % g.SZ + g.Z0, y = i / (g.SX * g.SZ) + g.Y0;
                    if (g.Mat(x + 1, y, z) == 0 || g.Mat(x - 1, y, z) == 0 || g.Mat(x, y + 1, z) == 0 || g.Mat(x, y - 1, z) == 0 || g.Mat(x, y, z + 1) == 0 || g.Mat(x, y, z - 1) == 0) showing++;
                }
            }
        }
    var man = Bodies.Plan(Kinds.Worker, 0);
    Check("bodies in every look have a skull with a brain, a heart behind ribs, and none of it shows", looks > 30 && showing == 0 && man.Brain > 20 && man.Heart > 4,
        $"{looks} looks, {showing} inner voxels on the surface; a man: {man.Brain} of brain, {man.Heart} of heart");

    var mpB = new MapParams();
    var wB = new VoxelWorld(mpB.SizeX, mpB.SizeY, mpB.SizeZ);
    var siteB = MapGen.Generate(wB, mpB);
    wB.Compact();
    var simB = new Sim(wB, siteB);
    simB.Waves.Countdown = 1e9; simB.WildTarget = 0;
    void RunB(double seconds) { for (double t = 0; t < seconds; t += .05) simB.Step(.05f); }
    Unit Stand(UnitType t, int dx, int dz)
    {
        var u = simB.Spawn(t, simB.NearestStand(siteB.KeepX + dx, siteB.KeepZ + dz, 4));
        u.Facing = 0;
        simB.Command(new[] { u }, u.Cell);
        return u;
    }
    int Voxels(Unit u) { int n = 0; var parts = u.Wounds?.Grid ?? Bodies.Plan(u.T, u.Look).Grid; foreach (var g in parts) if (g != null) n += g.Count; return n; }
    // facing +x, its right hand side is +z: a sword comes at the right arm from that side, at the elbow
    var hewn = Stand(Kinds.Worker, 120, 40);
    int remnants0 = simB.Remnants.Count, blows = 0;
    var (sCut, sDepth, sReach) = Sim.WeaponOf(Kinds.Swordsman);
    while (Sim.HasMainHand(hewn) && blows < 5) { blows++; simB.Wound(hewn, sCut, sDepth, sReach, hewn.X, hewn.Y + 7.5f, hewn.Z + 14, 0, 0, -1); }
    var piece = simB.Remnants.Count > remnants0 ? simB.Remnants[^1] : null;
    float hp0 = hewn.Hp;
    simB.ReleaseHold(new[] { hewn });
    RunB(6);
    Check("a sword cuts a man's right arm off: the arm falls, he cannot work or fight with it, and he bleeds", !Sim.HasMainHand(hewn) && piece != null && piece.From == BodyPart.Main
        && !Sim.Armed(hewn) && hewn.Hp < hp0 - 2 && hewn.Why.StartsWith("без руки"),
        $"{blows} blows, a piece of {piece?.Grid.Count} half-voxels from the {piece?.From}, hp {hp0:0} → {hewn.Hp:0} in 6 s, \"{hewn.Why}\"; {string.Join(", ", hewn.Wounds?.Wounds ?? new())}");

    // an axe at the face splits the head; an arrow in the chest finds the heart
    var split = Stand(Kinds.Worker, 120, 60);
    var (aCut, aDepth, aReach) = Sim.WeaponOf(Kinds.Orc);
    for (int i = 0; i < 3 && !split.Dead; i++) simB.Wound(split, aCut, aDepth, aReach, split.X + 16, split.Y + 12, split.Z, -1, 0, 0);
    var shot = Stand(Kinds.Worker, 120, 80);
    var (bCut, bDepth, _) = Sim.WeaponOf(Kinds.Archer);
    for (int i = 0; i < 3 && !shot.Dead; i++) simB.Wound(shot, bCut, bDepth, 0, shot.X + 20, shot.Y + 8.75f, shot.Z - .75f, -1, 0, 0);
    RunB(12);
    Check("an axe at the face splits the head; an arrow in the chest pierces the heart, and he bleeds out", split.Dead && shot.Dead,
        $"head: {string.Join(", ", split.Wounds?.Wounds ?? new())}, dead {split.Dead}; chest: {string.Join(", ", shot.Wounds?.Wounds ?? new())}, dead {shot.Dead}");

    // mail turns much of a blade that bites deep into cloth and flesh: the same blow across the back of fresh men
    int pc = 0, mc = 0;
    for (int i = 0; i < 4; i++)
    {
        var plain = Stand(Kinds.Worker, 140 + i * 12, 40);
        var mailed = Stand(Kinds.Swordsman, 140 + i * 12, 60);
        simB.Wound(plain, sCut, sDepth, sReach, plain.X - 14, plain.Y + 8.5f, plain.Z, 1, 0, 0); pc += simB.LastCut;
        simB.Wound(mailed, sCut, sDepth, sReach, mailed.X - 14, mailed.Y + 8.5f, mailed.Z, 1, 0, 0); mc += simB.LastCut;
    }
    Check("mail turns much of a blade that bites deep into a smock", pc > mc * 1.5f, $"{pc} half-voxels cut out of four workers, {mc} out of four swordsmen in mail");

    // a goblin with its knife hand cut off gives up and drags itself back west
    var gob = simB.Spawn(Kinds.Goblin, simB.NearestStand(siteB.KeepX - 300, siteB.KeepZ, 2));
    gob.Facing = 0;
    for (int i = 0; i < 5 && Sim.HasMainHand(gob); i++) simB.Wound(gob, sCut, sDepth, sReach, gob.X, gob.Y + 4, gob.Z + 12, 0, 0, -1);
    float gx0 = gob.X;
    RunB(12);
    Check("a goblin with its knife hand cut off drags itself back west", !Sim.HasMainHand(gob) && (gob.X < gx0 - 16 || gob.Dead), $"hand {Sim.HasMainHand(gob)}, x {gx0:0} → {gob.X:0}, {gob.State}");
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
