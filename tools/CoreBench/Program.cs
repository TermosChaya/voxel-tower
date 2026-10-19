using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using VoxelTower.Core;

// Checks the engine-free core against numbers measured in the browser prototype, and times the heavy parts.
int failures = 0;
// what stands where a gate's lost leaf voxels should be, for a gate check that fails
string GoneLeaves(Sim sim, Gateway g)
{
    var parts = new System.Collections.Generic.List<string>();
    for (int i = 0; i < Gateway.Voxels && parts.Count < 4; i++)
    {
        if (!g.Gone[i]) continue;
        var (x, y, z) = g.Place(i, g.Step < 0 ? 0 : g.Step);
        var who = sim.Units.Find(u => !u.Dead && u.Cell == sim.Nav.CellOf(x, y, z));
        int fires = 0; foreach (int b in sim.Burning.Keys) { var (bx, _, bz) = WaterPlanner.Unpack(b); if (Math.Abs(bx - x) < 30 && Math.Abs(bz - z) < 30) fires++; }
        parts.Add($"{x},{y},{z}: {Mat.NameOf(sim.World.Get(x, y, z))}{(who != null ? " under " + who.T.Key : "")}, {fires} burning near");
    }
    return parts.Count == 0 ? "" : "; gone: " + string.Join("; ", parts);
}
string Retinue0(Unit u) => u.Why != "" ? u.Why : u.State.ToString();
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
        int mines = ss.Landmarks.Count(l => l.Kind == LandmarkKind.Mine), ruins = ss.Landmarks.Count(l => l.Kind == LandmarkKind.Ruin), powers = ss.Landmarks.Count(l => l.Kind == LandmarkKind.Power);
        Check($"seed {seed}: a whole valley", ss.KeepBase > 70 && open == 0 && mines == 3 && ruins == 6 && powers == 3 && ss.Ponds.Count == 2,
            $"keep base {ss.KeepBase}, {open} open water faces, {mines} mines, {ruins} ruins, {powers} places of power, {ss.Ponds.Count} ponds");
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
        // it hacks its way out: no torch, or the gate burns and that is another test
        brute.Torch = false;
        sim.ComputeFields();
        float opened = 0;
        bool Out(Unit u) => Math.Abs(u.X - yx) > 36 || Math.Abs(u.Z - yz) > 36;
        for (int t = 0; t < 120 * 20 && !Out(brute); t++) { sim.Step(.05f); opened = Math.Max(opened, gate.Open); }
        Check("barred, it lets nobody through; it never opens for the horde, which hacks through it", barred == 0 && opened == 0 && gate.Broken && Out(brute),
            $"way out while barred: {barred}, opened for the orc to {opened:0.00}, leaf voxels hacked {gate.Hacked} of {Gateway.Voxels}, broken {gate.Broken}, orc out of the yard {Out(brute)} at ({brute.X:0}, {brute.Z:0})");
        brute.Dead = true; brute.DiedAt = -999;
        int mended = sim.MendGate(gate);
        // nobody standing in the gateway while it is hung at once (laid by hand, a leaf waits for whoever is there to pass)
        var (gmx, _, gmz) = gate.Middle;
        int clear = sim.NearestStand((int)gmx, (int)gmz + 120, 4);
        foreach (var o in sim.Units)
            if (!o.Dead && Math.Abs(o.X - gmx) < 40 && Math.Abs(o.Z - gmz) < 40) { o.Cell = clear; (o.X, o.Y, o.Z) = sim.Nav.Feet(clear); o.Path = null; }
        sim.CompleteAll();
        Run(2);
        Check("a broken gate is mended and works again", mended > 1000 && gate.Ready && !gate.Broken && gate.Hacked == 0,
            $"{mended} leaf voxels to lay anew, ready {gate.Ready}, broken {gate.Broken}, hacked {gate.Hacked}" + GoneLeaves(sim, gate));
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
        Check("workers sent to a mine take it, and it pays while it is ours", sim.Mines.Count(m => !m.Power) == 3 && ruins == 6 && taken && paid > copper.Income * .9,
            $"{sim.Mines.Count(m => !m.Power)} mines, {ruins} ruins; {copper.Name} {(taken ? "taken" : "not taken")}, +{paid:0.0} gold in a minute (pays {copper.Income})");

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
            // those in the citadel are out of the world: nothing is built into them
            if (!u.Dead && !u.Inside && u.Team == Team.Player) { there++; if (sim.Nav.Fill[u.Cell] >= NavGrid.SolidT) walled++; }
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
    sim8.DesignateBuild(plan8, arched: true);
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
    // tough enough to last till help comes, whatever the knife finds
    victim.Hp = victim.MaxHp = 600;
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
    // on its face, where the air gets at it
    bool litF = simF.Ignite(fx0, baseF + 1, fz0 + 20);
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

// ---- what holds up what is built: a tower cut at its foot falls in a heap, a wall over too wide a hole comes down over
// it, a bridge stands while it touches the banks, a miner brings a wall down by digging under it, mending raises it again
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--collapse")
{
    Console.WriteLine();
    var mpC = new MapParams();
    var wC = new VoxelWorld(mpC.SizeX, mpC.SizeY, mpC.SizeZ);
    var siteC = MapGen.Generate(wC, mpC);
    wC.Compact();
    var simC = new Sim(wC, siteC);
    simC.Waves.Countdown = 1e9; simC.WildTarget = 0;
    void RunC(double seconds) { for (double t = 0; t < seconds; t += .05) simC.Step(.05f); }
    void GoneC(Unit u) { u.Dead = true; u.DiedAt = -999; }
    int kxC = siteC.KeepX, kzC = siteC.KeepZ;
    int CountC(Func<byte, bool> what, int x0, int z0, int x1, int z1, int y0 = 0, int y1 = 191)
    {
        int n = 0;
        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) if (what(wC.Get(x, y, z))) n++;
        return n;
    }
    // a level patch of ground: the highest of it, with everything over it cleared
    int Flatten(int x0, int z0, int x1, int z1)
    {
        int g = 0;
        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) g = Math.Max(g, wC.SurfAt(x, z));
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                for (int y = g; y < g + 80; y++) wC.Set(x, y, z, Mat.Air);
                for (int y = g - 1; y >= 0 && !Mat.IsAnchor[wC.Get(x, y, z)]; y--) wC.Set(x, y, z, Mat.Soil);
                wC.Set(x, g - 1, z, Mat.Soil);
            }
        return g;
    }
    int collapses = 0;
    simC.Collapsed = (n, x, y, z) => collapses++;

    // a stone tower two metres square and six high, cut through at its foot: it all comes down in a heap, and the goblin
    // standing by it is crushed
    int tx0 = kxC - 150, tz0 = kzC + 100, tg = Flatten(tx0 - 40, tz0 - 40, tx0 + 56, tz0 + 56);
    for (int z = tz0; z < tz0 + 16; z++) for (int x = tx0; x < tx0 + 16; x++) for (int y = tg; y < tg + 48; y++) wC.Set(x, y, z, Mat.Stone);
    RunC(.5);
    int tower0 = CountC(m => m == Mat.Stone, tx0 - 40, tz0 - 40, tx0 + 56, tz0 + 56);
    var under = simC.Spawn(Kinds.Goblin, simC.NearestStand(tx0 + 8, tz0 + 20, 2));
    under.Raid = null;
    for (int z = tz0; z < tz0 + 16; z++) for (int x = tx0; x < tx0 + 16; x++) for (int y = tg; y < tg + 2; y++) wC.Set(x, y, z, Mat.Air);
    RunC(1);
    static bool Stony(byte m) => m is Mat.Stone or Mat.Rubble;
    int high = CountC(Stony, tx0, tz0, tx0 + 15, tz0 + 15, tg + 40, tg + 50), heap = CountC(m => m == Mat.Rubble, tx0 - 40, tz0 - 40, tx0 + 56, tz0 + 56);
    int spread = heap - CountC(m => m == Mat.Rubble, tx0, tz0, tx0 + 15, tz0 + 15);
    Check("a tower cut through at its foot comes down whole in a heap, and crushes the goblin by it",
        high == 0 && heap > tower0 * 8 / 10 && spread > heap / 3 && collapses > 0 && (under.Dead || under.Hp < under.MaxHp),
        $"{tower0 - 512} voxels of tower over the cut, {high} still at its top, {heap} lying in the heap ({spread} run out past its foot); the goblin {(under.Dead ? "dead, " + under.KilledBy : $"{under.Hp:0} hp")}");
    GoneC(under);

    // a stone wall a metre thick and three high: a hole a metre and a half wide under it holds, one of four metres
    // brings the wall down over its middle, to the top
    int wx0 = kxC - 200, wz0 = kzC - 160, wg = Flatten(wx0 - 8, wz0 - 4, wx0 + 72, wz0 + 12);
    for (int z = wz0; z < wz0 + 8; z++) for (int x = wx0; x < wx0 + 64; x++) for (int y = wg; y < wg + 24; y++) wC.Set(x, y, z, Mat.Stone);
    RunC(.5);
    void Undercut(int x0, int x1) { for (int z = wz0 - 2; z < wz0 + 10; z++) for (int x = x0; x <= x1; x++) for (int y = wg - 6; y < wg; y++) wC.Set(x, y, z, Mat.Air); }
    int wall0 = CountC(m => m == Mat.Stone, wx0, wz0, wx0 + 63, wz0 + 7, wg, wg + 30);
    Undercut(wx0 + 4, wx0 + 15);
    RunC(1);
    int wall1 = CountC(m => m == Mat.Stone, wx0, wz0, wx0 + 63, wz0 + 7, wg, wg + 30);
    Undercut(wx0 + 26, wx0 + 57);
    RunC(1);
    int middle = CountC(m => m == Mat.Stone, wx0 + 40, wz0, wx0 + 43, wz0 + 7, wg + 16, wg + 30), sides = CountC(m => m == Mat.Stone, wx0 + 26, wz0, wx0 + 30, wz0 + 7, wg + 16, wg + 30);
    Check("a wall stands over a hole a metre and a half wide, and over one of four metres comes down to the top in the middle",
        wall1 == wall0 && middle == 0 && sides >= 4 * 8 * 8,
        $"{wall0} voxels of wall; over the narrow hole {wall1}; over the wide one, {middle} left of its middle and {sides} by its edges");

    // a bridge of ours over a trench stands, an arch; cut away at both banks it falls
    int bx0 = kxC - 110, bz0 = kzC - 260, bg = Flatten(bx0 - 30, bz0 - 12, bx0 + 90, bz0 + 12);
    simC.InstantLabour = true;
    simC.DesignateRemove(Shapes.Box(wC, siteC, bx0 + 16, bz0 - 12, bx0 + 44, bz0 + 12, new Stroke { Width = 1, Height = 16, Dig = true }, 0));
    var span = Bridges.Plan(wC, siteC, bx0 + 10, bz0, bx0 + 50, bz0, 10, Mat.Stone);
    simC.DesignateBuild(span, arched: true);
    simC.InstantLabour = false;
    RunC(1);
    int Over() => CountC(m => Mat.IsMasonry(m), bx0 + 22, bz0 - 8, bx0 + 38, bz0 + 8, bg, bg + 40);
    int deck0 = Over();
    for (int z = bz0 - 12; z <= bz0 + 12; z++)
        foreach (int x0 in new[] { bx0 - 4, bx0 + 40 })
            for (int x = x0; x < x0 + 24; x++) for (int y = bg - 20; y < bg + 30; y++) if (Mat.IsMasonry(wC.Get(x, y, z))) wC.Set(x, y, z, Mat.Air);
    RunC(1);
    int deck1 = Over();
    Check("a bridge of ours stands over a trench as an arch, and falls once it is cut away at both banks", span.Count > 500 && deck0 > 300 && deck1 < deck0 / 10,
        $"{span.Count} voxels planned, {deck0} over the trench; {deck1} once both ends were cut away");

    // a miner comes up against a ring of stone round the citadel: it digs out the ground under the wall, and the
    // wall comes down; mending puts it back as it was
    simC.InstantLabour = true;
    simC.DesignateBuild(Shapes.Circle(wC, siteC, kxC, kzC, siteC.KeepR + 44, new Stroke { Mat = Mat.Stone, Width = 10, Height = 40, Hollow = true }, 0));
    simC.InstantLabour = false;
    RunC(1);
    simC.HordeStock[Stockpile.Wood] = simC.HordeStock[Stockpile.Earth] = 0;
    simC.ComputeFields();
    collapses = 0;
    var miner = simC.Spawn(Kinds.Miner, simC.NearestStand(kxC - siteC.KeepR - 100, kzC, 2));
    int fallen = 0;
    simC.Fell = (x, y, z, m) => { if (Mat.IsMasonry(m)) fallen++; };
    bool sapped = false;
    for (int t = 0; t < 120 && fallen < 2000; t++)
    {
        RunC(1);
        sapped |= miner.Sapped >= 0;
        if (Environment.GetEnvironmentVariable("COLLAPSE_DEBUG") != null && (miner.State == UnitState.Carve || t % 10 == 0))
        {
            int ci = simC.Nav.I(Math.Max(0, miner.CarveCell)), ck = simC.Nav.K(Math.Max(0, miner.CarveCell));
            string list = miner.CarveList.Count > 0 ? $"first {miner.CarveList[0]}, last {miner.CarveList[^1]}" : "";
            Console.WriteLine($"  t {t}: miner at {miner.X:0},{miner.Y:0},{miner.Z:0} {miner.State}, carve cell {ci},{ck} ({miner.CarveI}/{miner.CarveList.Count}; {list}), sapped {miner.Sapped}, fallen {fallen}, collapses {collapses}");
        }
    }
    Check("a miner digs out the ground under a wall of ours, and the wall comes down", sapped && collapses > 0 && fallen > 2000,
        $"sapped {sapped}, {collapses} falls, {fallen} voxels of masonry came down, the miner {(miner.Dead ? "dead, " + miner.KilledBy : miner.State.ToString())}");
    GoneC(miner);
    simC.Fell = null;
    var meant = simC.MeantIn(kxC - 120, kzC - 120, kxC + 120, kzC + 120);
    var lay = new VoxelList(); var clear = new VoxelList();
    simC.MendPlan(meant, lay, clear);
    simC.InstantLabour = true;
    simC.Mend(lay, clear);
    simC.InstantLabour = false;
    RunC(1);
    var (meantN, standing) = simC.Soundness(kxC - 120, kzC - 120, kxC + 120, kzC + 120);
    Check("mending raises the fallen wall again as it was", lay.Count > 1000 && standing >= meantN * 98 / 100,
        $"{lay.Count} to lay, {clear.Count} of rubble to clear; {standing} of {meantN} meant voxels stand");
}

// ---- a game saved and read back: the valley as play changed it, and the siege, the same; and it plays on
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--night")
{
    Console.WriteLine();
    var mpN = new MapParams();
    var wN = new VoxelWorld(mpN.SizeX, mpN.SizeY, mpN.SizeZ);
    var siteN = MapGen.Generate(wN, mpN);
    wN.Compact();
    var simN = new Sim(wN, siteN);
    simN.WildTarget = 0;
    var wvN = simN.Waves;

    // the day the siege keeps: light while the walls go up, dusk the last minute, night while the wave is in, a dawn after it
    float dark0 = wvN.Darkness, day0 = wvN.Day;
    wvN.Countdown = 30; float duskN = wvN.Darkness, dayDusk = wvN.Day;
    wvN.Active = true; float nightN = wvN.Darkness; wvN.Active = false;
    wvN.Countdown = Waves.Prep; wvN.DawnLeft = Waves.Dawn; float dawnN = wvN.Darkness;
    wvN.Tick(simN, 15); float dawnHalf = wvN.Darkness;
    wvN.Tick(simN, 20); float dayAgain = wvN.Darkness;
    Check("the day: light while the walls go up, dusk the last minute before the horn, night through the wave, a dawn after it",
        dark0 == 0 && day0 == 0 && Math.Abs(duskN - .5f) < .01f && dayDusk > .9f && nightN == 1 && dawnN == 1 && Math.Abs(dawnHalf - .5f) < .01f && dayAgain == 0,
        $"start dark {dark0}, day {day0}; 30 s before the horn {duskN:0.00}, {dayDusk:0.00}; the wave {nightN}; dawn {dawnN} → {dawnHalf:0.00} → {dayAgain}");

    // an archer out in the open, far from the citadel's light, and a goblin keeping its post 18 m off
    wvN.Countdown = 1e9; wvN.DawnLeft = 0;
    void RunN(double seconds, bool night) { for (double t = 0; t < seconds; t += .05) { if (night) wvN.DawnLeft = Waves.Dawn; simN.Step(.05f); } }
    // on the old road through the lowland, where nothing grows to stand in the way
    Unit bowN = null, gobN = null;
    for (int rx = 200; rx < 600 && gobN == null; rx += 24)
    {
        bowN = simN.Spawn(Kinds.Archer, simN.NearestStand(rx, (int)Site.PathZ(rx), 4));
        simN.Command(new[] { bowN }, bowN.Cell);
        int gx = (int)bowN.X - 144;
        var g = simN.Spawn(Kinds.Goblin, simN.NearestStand(gx, (int)Site.PathZ(gx), 2));
        g.Raid = null; g.Guard = true; g.Home = g.Cell;
        float d = Sim.Dist(bowN, g) / VoxelWorld.PerMetre;
        if (simN.Sight(bowN, g) && d > 16 && d < 20 && !simN.Lit(g.X, g.Z)) { gobN = g; break; }
        g.Dead = true; g.DiedAt = -999; bowN.Dead = true; bowN.DiedAt = -999;
    }
    int ShotsIn(double seconds, bool night)
    {
        int s0 = simN.Shots;
        RunN(seconds, night);
        return simN.Shots - s0;
    }
    if (gobN != null)
    {
        float gm = Sim.Dist(bowN, gobN) / VoxelWorld.PerMetre;
        gobN.Hp = gobN.MaxHp = 1e6f;
        int byDay = ShotsIn(6, false);
        RunN(1.5, true);
        int byNight = ShotsIn(6, true);
        bool darkThere = !simN.Lit(gobN.X, gobN.Z);
        gobN.Torch = true;
        RunN(1.5, true);
        int byTorch = ShotsIn(6, true);
        Check("at night an archer can only make out a raider far off by its light: none in the dark at 18 m, one with a torch",
            byDay > 0 && byNight == 0 && darkThere && byTorch > 0,
            $"{gm:0.0} m: {byDay} arrows in 6 s by day, {byNight} at night in the dark (lit {!darkThere}), {byTorch} at the goblin with a torch; " +
            $"reach in the dark {simN.RangeAt(bowN, gobN.X + 400, gobN.Z) / VoxelWorld.PerMetre:0.0} m");
    }
    else Check("at night an archer can only make out a raider far off by its light", false, "no spot for the goblin in the archer's sight");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--shelter")
{
    Console.WriteLine();
    var mpS = new MapParams();
    var wS = new VoxelWorld(mpS.SizeX, mpS.SizeY, mpS.SizeZ);
    var siteS = MapGen.Generate(wS, mpS);
    wS.Compact();
    var simS = new Sim(wS, siteS);
    simS.Waves.Countdown = 1e9; simS.WildTarget = 0;
    void RunS(double seconds) { for (double t = 0; t < seconds; t += .05) simS.Step(.05f); }
    void GoneS(Unit u) { u.Dead = true; u.DiedAt = -999; }
    var (gxS, _, gzS) = simS.Nav.Feet(simS.Gate);

    // three workers 20 m out from the citadel's door, three more 60 m out with a shelter set aside round them, an archer on a post
    var nearCrew = new System.Collections.Generic.List<Unit>();
    var farCrew = new System.Collections.Generic.List<Unit>();
    for (int i = 0; i < 3; i++) nearCrew.Add(simS.Spawn(Kinds.Worker, simS.NearestStand((int)gxS - 160, (int)gzS - 30 + i * 30, 4)));
    for (int i = 0; i < 3; i++) farCrew.Add(simS.Spawn(Kinds.Worker, simS.NearestStand((int)gxS - 480, (int)gzS - 30 + i * 30, 4)));
    var hut = simS.AddZone((int)gxS - 520, (int)gzS - 40, (int)gxS - 440, (int)gzS + 40, Zone.Shelter, 0);
    var bowS = simS.Spawn(Kinds.Archer, simS.NearestStand((int)gxS - 60, (int)gzS + 260, 4));
    simS.Command(new[] { bowS }, bowS.Cell);
    simS.ReleaseHold(new[] { bowS });
    RunS(2);
    int room = simS.ShelterRoom(hut.X0, hut.Z0, hut.X1, hut.Z1);
    simS.RaiseAlarm();
    double inAt = -1;
    bool InHut(Unit u) => !u.Inside && hut.Contains((int)u.X, (int)u.Z) && (u.Path == null || u.PathI >= u.Path.Count);
    for (int t = 0; t < 120 && inAt < 0; t++) { RunS(.5); if (nearCrew.TrueForAll(u => u.Inside) && farCrew.TrueForAll(InHut)) inAt = (t + 1) * .5; }
    var cells = new System.Collections.Generic.HashSet<int>();
    foreach (var u in farCrew) cells.Add(u.Cell);
    Check("with the alarm up the workers go to the nearest shelter: those near the citadel into it, those out by a shelter area to a place each in it",
        inAt > 0 && cells.Count == farCrew.Count && !bowS.Inside && room > 50,
        $"all sheltered after {inAt} s; {nearCrew.FindAll(u => u.Inside).Count} of 3 in the citadel, {farCrew.FindAll(InHut).Count} of 3 in the shelter " +
        $"({cells.Count} places, {room} in all); the archer {(bowS.Inside ? "inside" : "on his post")}");

    // a goblin at the citadel's door finds nobody to strike; stood down with it still there, they wait inside, and come out once it is gone
    var gobS = simS.Spawn(Kinds.Goblin, simS.NearestStand((int)gxS - 8, (int)gzS, 2));
    gobS.Raid = null; gobS.Guard = true; gobS.Home = gobS.Cell; gobS.Hp = gobS.MaxHp = 1e6f;
    RunS(3);
    float hurtS = 0; foreach (var u in nearCrew) hurtS += u.MaxHp - u.Hp;
    var seenS = simS.Nearest(gobS, Team.Player, 30 * VoxelWorld.PerMetre);
    simS.StandDown();
    RunS(3);
    bool waited = nearCrew.TrueForAll(u => u.Inside), hutOut = farCrew.TrueForAll(u => !u.Inside);
    GoneS(gobS);
    double outAt = -1;
    for (int t = 0; t < 40 && outAt < 0; t++) { RunS(.5); if (nearCrew.TrueForAll(u => !u.Inside)) outAt = (t + 1) * .5; }
    Check("in the citadel nobody can get at them; stood down with a raider at the door they wait, and come out once it is gone",
        hurtS == 0 && (seenS == null || !nearCrew.Contains(seenS)) && waited && hutOut && outAt > 0 && !simS.Alarm,
        $"hurt {hurtS:0}, the goblin sees {seenS?.T.Name ?? "nobody"}; stood down: still inside {waited}; out {outAt} s after the goblin went");

    // a worker run down by a goblin on its way to the citadel gets in, and the goblin besieging the citadel can't get at it
    foreach (var u in farCrew) GoneS(u);
    var runner = simS.Spawn(Kinds.Worker, simS.NearestStand((int)gxS - 110, (int)gzS + 20, 4));
    var chaser = simS.Spawn(Kinds.Goblin, simS.NearestStand((int)gxS - 190, (int)gzS + 20, 2));
    chaser.Hp = chaser.MaxHp = 1e6f;
    simS.ComputeFields();
    bool fled = false; double gotIn = -1;
    for (int t = 0; t < 80 && gotIn < 0; t++) { RunS(.5); fled |= runner.State == UnitState.Flee; if (runner.Inside) gotIn = (t + 1) * .5; }
    RunS(10);
    Check("a worker running from a goblin gets into the citadel, and the goblin that comes after it can't get at it there",
        fled && gotIn > 0 && runner.Inside && runner.Hp >= runner.MaxHp - .5f,
        $"fled {fled}, in after {gotIn} s; 10 s on: inside {runner.Inside}, {runner.Hp:0} of {runner.MaxHp:0} hp; the goblin {chaser.State} {Sim.Dist(chaser, runner) / VoxelWorld.PerMetre:0.0} m off");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--traps")
{
    Console.WriteLine();
    var mpT = new MapParams();
    var wT = new VoxelWorld(mpT.SizeX, mpT.SizeY, mpT.SizeZ);
    var siteT = MapGen.Generate(wT, mpT);
    wT.Compact();
    var simT = new Sim(wT, siteT);
    simT.Waves.Countdown = 1e9; simT.WildTarget = 0;
    void RunT(double seconds) { for (double t = 0; t < seconds; t += .05) simT.Step(.05f); }
    void GoneT(Unit u) { u.Dead = true; u.DiedAt = -999; }
    int kxT = siteT.KeepX, kzT = siteT.KeepZ;
    int Count(byte m, int x0, int z0, int x1, int z1)
    {
        int n = 0;
        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) for (int y = 0; y < wT.SY; y++) if (wT.Get(x, y, z) == m) n++;
        return n;
    }

    // a field of stakes a metre high: a goblin kept among them is torn and treads some down
    int sx0 = kxT - 150, sz0 = kzT - 160;
    simT.InstantLabour = true;
    simT.DesignateBuild(Shapes.Box(wT, siteT, sx0, sz0, sx0 + 40, sz0 + 40, new Stroke { Mat = Mat.Stakes, Height = 8 }, 0));
    simT.InstantLabour = false;
    simT.Nav.Flush();
    int stakes0 = Count(Mat.Stakes, sx0, sz0, sx0 + 40, sz0 + 40);
    var gobT = simT.Spawn(Kinds.Goblin, simT.NearestStand(sx0 + 20, sz0 + 20, 2));
    gobT.Raid = null; gobT.Guard = true; gobT.Home = gobT.Cell;
    float hp0T = gobT.Hp;
    bool spiked = simT.Nav.Spikes[gobT.Cell] > 0;
    RunT(4);
    int stakes1 = Count(Mat.Stakes, sx0, sz0, sx0 + 40, sz0 + 40);
    Check("stakes tear whoever stands among them, and are trodden down a little",
        stakes0 > 300 && spiked && hp0T - gobT.Hp > 12 && stakes1 < stakes0,
        $"{stakes0} voxels of stakes, the goblin's cell spiked {spiked}: {hp0T:0} → {gobT.Hp:0} hp in 4 s; stakes {stakes0} → {stakes1}");
    GoneT(gobT);

    // a worker sent past the field goes round it, not through
    var walker = simT.Spawn(Kinds.Worker, simT.NearestStand(sx0 - 20, sz0 + 20, 4));
    int beyond = simT.NearestStand(sx0 + 60, sz0 + 20, 4);
    simT.Command(new[] { walker }, beyond);
    int through = 0;
    if (walker.Path != null) foreach (int c in walker.Path) if (simT.Nav.Spikes[c] > 0) through++;
    int pathLen = walker.Path?.Count ?? 0;
    RunT(30);
    Check("a pawn of ours sent past a field of stakes goes round it", pathLen > 0 && through == 0 && walker.Cell == beyond && walker.Hp >= walker.MaxHp - .5f,
        $"path of {pathLen} cells, {through} of them among stakes; there {walker.Cell == beyond}, {walker.Hp:0} hp");
    GoneT(walker);

    // a pit two metres deep with stakes at the bottom, under a trap floor flush with the ground: it holds a worker of
    // ours walking over it, and gives way under a goblin, which goes down onto the stakes
    int px0 = kxT - 150, pz0 = kzT + 120, ground = 0;
    for (int z = pz0; z < pz0 + 24; z++) for (int x = px0; x < px0 + 24; x++) ground = Math.Max(ground, wT.SurfAt(x, z));
    for (int z = pz0 - 16; z < pz0 + 40; z++)
        for (int x = px0 - 16; x < px0 + 40; x++)
        {
            // flat ground round it, and the pit dug out of it
            for (int y = ground; y < ground + 30; y++) wT.Set(x, y, z, Mat.Air);
            for (int y = ground - 20; y < ground; y++) wT.Set(x, y, z, Mat.Soil);
            wT.Set(x, ground - 1, z, Mat.Grass);
        }
    for (int z = pz0; z < pz0 + 24; z++)
        for (int x = px0; x < px0 + 24; x++)
        {
            for (int y = ground - 18; y < ground; y++) wT.Set(x, y, z, Mat.Air);
            if (x % 3 == 0 && z % 3 == 0) for (int y = ground - 18; y < ground - 12; y++) wT.Set(x, y, z, Mat.Stakes);
        }
    simT.InstantLabour = true;
    simT.DesignateBuild(Shapes.Box(wT, siteT, px0, pz0, px0 + 23, pz0 + 23, new Stroke { Mat = Mat.TrapDeck, Height = 0, Level = true }, ground));
    simT.InstantLabour = false;
    simT.Nav.Flush();
    int deck0 = Count(Mat.TrapDeck, px0, pz0, px0 + 23, pz0 + 23);
    var over = simT.Spawn(Kinds.Worker, simT.NearestStand(px0 - 10, pz0 + 12, 4));
    int farSide = simT.NearestStand(px0 + 34, pz0 + 12, 4);
    simT.Command(new[] { over }, farSide);
    float lowest = over.Y;
    for (int t = 0; t < 60 && over.Cell != farSide; t++) { RunT(.5); lowest = Math.Min(lowest, over.Y); }
    bool crossed = over.Cell == farSide;
    int deck1 = Count(Mat.TrapDeck, px0, pz0, px0 + 23, pz0 + 23);
    var prey = simT.Spawn(Kinds.Goblin, simT.NearestStand(px0 + 12, pz0 + 12, 2));
    prey.Raid = null; prey.Guard = true; prey.Home = prey.Cell;
    float py0 = prey.Y, php0 = prey.Hp;
    RunT(4);
    int deck2 = Count(Mat.TrapDeck, px0, pz0, px0 + 23, pz0 + 23);
    Check("a trap floor over a pit holds a worker of ours and gives way under a goblin, which goes down onto the stakes",
        deck0 > 1800 && deck1 == deck0 && lowest > ground - 2 && crossed && deck2 < deck0 / 4 && prey.Y < ground - 12 && php0 - prey.Hp > 25,
        $"{deck0} voxels of floor; the worker crossed it {crossed}, lowest at {lowest - ground:0} voxels, floor {deck1} after; " +
        $"the goblin {py0 - ground:0} → {prey.Y - ground:0} voxels, {php0:0} → {prey.Hp:0} hp, floor {deck2} left");
    GoneT(over); GoneT(prey);

    // a stone wall four metres high with stones laid along its outer edge, an archer of ours on it by them, a goblin at
    // its foot under them: the archer tips them over, and they come down on the goblin
    int wx0 = kxT - 220, wz0 = kzT - 40;
    simT.InstantLabour = true;
    int wallTop = Shapes.LevelTop(wT, wx0 + 4, wz0 + 40, 32);
    simT.DesignateBuild(Shapes.Line(wT, siteT, wx0 + 4, wz0, wx0 + 4, wz0 + 80, new Stroke { Mat = Mat.Stone, Width = 9, Height = 32, Level = true }, wallTop));
    simT.InstantLabour = false;
    int edge = wx0;
    while (edge < wx0 + 10 && wT.Get(edge, wallTop - 1, wz0 + 40) == Mat.Air) edge++;
    for (int z = wz0 + 30; z < wz0 + 50; z++)
        for (int y = wallTop; y < wallTop + 3; y++) { wT.Set(edge, y, z, Mat.DropStone); wT.Set(edge + 1, y, z, Mat.DropStone); }
    simT.Nav.Flush();
    int dropped0 = Count(Mat.DropStone, wx0 - 4, wz0 + 28, wx0 + 12, wz0 + 52);
    var guardT = simT.Spawn(Kinds.Archer, simT.NearestStand(edge + 5, wz0 + 40, 4));
    simT.Command(new[] { guardT }, guardT.Cell);
    var footGob = simT.Spawn(Kinds.Goblin, simT.NearestStand(edge - 5, wz0 + 40, 2));
    footGob.Raid = null; footGob.Guard = true; footGob.Home = footGob.Cell;
    float fhp0 = footGob.Hp;
    float onWall = guardT.Y - wallTop;
    RunT(3);
    int dropped1 = Count(Mat.DropStone, wx0 - 4, wz0 + 28, wx0 + 12, wz0 + 52);
    Check("stones on a wall are tipped onto a goblin at its foot by an archer of ours beside them",
        dropped0 >= 100 && Math.Abs(onWall) < 4 && dropped1 < dropped0 && (footGob.Dead || fhp0 - footGob.Hp >= 25),
        $"{dropped0} stones on the wall, the archer {onWall:0} voxels from its top; {dropped1} left; the goblin {fhp0:0} → {(footGob.Dead ? "dead" : $"{footGob.Hp:0} hp")}");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--mechanisms")
{
    Console.WriteLine();
    var mpM = new MapParams();
    var wM = new VoxelWorld(mpM.SizeX, mpM.SizeY, mpM.SizeZ);
    var siteM = MapGen.Generate(wM, mpM);
    wM.Compact();
    var simM = new Sim(wM, siteM);
    simM.Waves.Countdown = 1e9; simM.WildTarget = 0;
    simM.HordeStock[Stockpile.Wood] = simM.HordeStock[Stockpile.Earth] = 0;
    void RunM(double seconds) { for (double t = 0; t < seconds; t += .05) simM.Step(.05f); }
    int kxM = siteM.KeepX, kzM = siteM.KeepZ;
    // flat ground, and a ditch two and a half metres deep and four wide across it, north to south
    int cx = kxM - 260, cz = kzM + 60, gM = 0;
    for (int z = cz - 40; z < cz + 40; z++) for (int x = cx - 60; x < cx + 60; x++) gM = Math.Max(gM, wM.SurfAt(x, z));
    for (int z = cz - 40; z < cz + 40; z++)
        for (int x = cx - 60; x < cx + 60; x++)
        {
            for (int y = gM; y < gM + 40; y++) wM.Set(x, y, z, Mat.Air);
            for (int y = gM - 30; y < gM; y++) wM.Set(x, y, z, Mat.Soil);
            wM.Set(x, gM - 1, z, Mat.Grass);
            if (Math.Abs(x - cx) <= 16) for (int y = gM - 20; y < gM; y++) wM.Set(x, y, z, Mat.Air);
        }
    simM.Nav.Flush();
    var drawM = Drawbridge.Plan(wM, siteM, cx + 24, cz, cx - 24, cz, 12, out string whyM);
    if (drawM != null) simM.AddDrawbridge(drawM, built: true);
    RunM(1);
    // down, it carries a worker of ours across
    var crosser = simM.Spawn(Kinds.Worker, simM.NearestStand(cx - 40, cz, 4));
    int eastBank = simM.NearestStand(cx + 40, cz, 4), westBank = crosser.Cell;
    simM.Command(new[] { crosser }, eastBank);
    float lowM = crosser.Y;
    for (int t = 0; t < 60 && crosser.Cell != eastBank; t++) { RunM(.5); lowM = Math.Min(lowM, crosser.Y); }
    bool across1 = crosser.Cell == eastBank;
    // a goblin on it: it goes up, and the goblin goes down into the ditch
    var rider = simM.Spawn(Kinds.Goblin, simM.NearestStand(cx, cz, 2));
    rider.Raid = null; rider.Guard = true; rider.Home = rider.Cell;
    float ry0 = rider.Y, rhp0 = rider.Hp;
    RunM(4);
    float raised = drawM?.Raise ?? 0;
    int mi = cx >> 3, mk = cz >> 3, my = gM >> 3;
    bool cut = !simM.Nav.Stand(mi, my, mk, 2);
    Check("a drawbridge down carries a worker of ours across a ditch; a goblin on it goes up with it and down into the ditch",
        drawM != null && drawM.Ready && across1 && lowM > gM - 4 && raised >= 1 && rider.Y < gM - 12 && rider.Hp < rhp0 && cut,
        drawM == null ? whyM : $"{drawM.Length / 8.0:0.0} m over the ditch, {drawM.Voxels - drawM.Missing} planks; the worker across {across1} (lowest {lowM - gM:0}); " +
        $"up to {raised:0.00}, the goblin {ry0 - gM:0} → {rider.Y - gM:0} voxels, {rhp0:0} → {rider.Hp:0} hp; the way over cut {cut}");
    rider.Dead = true; rider.DiedAt = -999;

    // held down, it comes down again and the worker goes back over it
    if (drawM != null) simM.SetDrawMode(drawM, DrawMode.Down);
    RunM(4);
    simM.Command(new[] { crosser }, westBank);
    for (int t = 0; t < 60 && crosser.Cell != westBank; t++) RunM(.5);
    Check("held down, it comes down again and the worker goes back over it", drawM != null && drawM.Raise == 0 && crosser.Cell == westBank,
        $"down to {drawM?.Raise:0.00}; the worker back {crosser.Cell == westBank}");

    // a pond behind a stone dam with a gate in it: shut, the water stays; opened, it runs out through the gateway
    int rx = kxM - 330, rz = kzM - 120, rg = 0;
    for (int z = rz - 40; z < rz + 40; z++) for (int x = rx - 40; x < rx + 120; x++) rg = Math.Max(rg, wM.SurfAt(x, z));
    for (int z = rz - 40; z < rz + 40; z++)
        for (int x = rx - 40; x < rx + 120; x++)
        {
            for (int y = rg; y < rg + 50; y++) wM.Set(x, y, z, Mat.Air);
            for (int y = rg - 30; y < rg; y++) wM.Set(x, y, z, Mat.Soil);
        }
    simM.InstantLabour = true;
    int rtop = Shapes.LevelTop(wM, rx, rz, 32);
    simM.DesignateBuild(Shapes.Box(wM, siteM, rx - 24, rz - 24, rx + 24, rz + 24, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Level = true, Hollow = true }, rtop));
    simM.InstantLabour = false;
    for (int z = rz - 16; z < rz + 17; z++) for (int x = rx - 16; x < rx + 17; x++) for (int y = rg; y < rg + 24; y++) if (wM.Get(x, y, z) == Mat.Air) wM.Set(x, y, z, Mat.Water);
    RunM(2);
    int ex = rx + 16;
    while (wM.Get(ex + 1, rg + 6, rz) != Mat.Air) ex++;
    var faceM = new VoxelHit { X = ex, Y = rg + 6, Z = rz, NX = 1, M = wM.Get(ex, rg + 6, rz) };
    var planM = GatePlan.At(wM, siteM, faceM);
    var sluice = planM == null ? null : simM.AddGate(planM, built: true);
    long Wet(int x0, int x1)
    {
        long n = 0;
        for (int z = rz - 40; z < rz + 40; z++) for (int x = x0; x < x1; x++) for (int y = rg - 5; y < rg + 30; y++) if (wM.Get(x, y, z) == Mat.Water) n++;
        return n;
    }
    if (sluice != null) simM.SetGateMode(sluice, GateMode.Shut);
    RunM(6);
    long out0 = Wet(ex + 4, rx + 120), in0 = Wet(rx - 16, rx + 17);
    int ran0 = simM.Flooded;
    if (sluice != null) simM.SetGateMode(sluice, GateMode.Open);
    RunM(20);
    long in1 = Wet(rx - 16, rx + 17);
    Check("a gate in a dam holds the water shut, and opened lets it out",
        sluice != null && in0 > 20000 && out0 == 0 && in1 < in0 / 2 && simM.Flooded - ran0 > 10000,
        sluice == null ? "no gate fits the dam" : $"shut: {in0} voxels of water behind it, {out0} outside; opened ({sluice.Open:0.00}): {in1} behind, {simM.Flooded - ran0} voxels ran out");

    // pitch is a liquid too, a slow one: poured on the bank it runs down into a ditch, and comes to rest on the water
    // in it, not mixing with it
    int qx = kxM - 120, qz = kzM - 260, qg = 0;
    for (int z = qz - 30; z < qz + 30; z++) for (int x = qx - 30; x < qx + 30; x++) qg = Math.Max(qg, wM.SurfAt(x, z));
    for (int z = qz - 30; z < qz + 30; z++)
        for (int x = qx - 30; x < qx + 30; x++)
        {
            for (int y = qg; y < qg + 40; y++) wM.Set(x, y, z, Mat.Air);
            for (int y = qg - 30; y < qg; y++) wM.Set(x, y, z, Mat.Soil);
            if (Math.Abs(x - qx) <= 8 && Math.Abs(z - qz) <= 24)
            {
                for (int y = qg - 16; y < qg; y++) wM.Set(x, y, z, Mat.Air);
                for (int y = qg - 16; y < qg - 12; y++) wM.Set(x, y, z, Mat.Water);
            }
        }
    RunM(2);
    long WaterQ() { long n = 0; for (int z = qz - 30; z < qz + 30; z++) for (int x = qx - 30; x < qx + 30; x++) for (int y = qg - 20; y < qg + 30; y++) if (wM.Get(x, y, z) == Mat.Water) n++; return n; }
    long water0Q = WaterQ();
    // a heap of pitch on the bank beside the ditch
    int heap = 0;
    for (int z = qz - 4; z < qz + 4; z++) for (int x = qx + 12; x < qx + 18; x++) for (int y = qg; y < qg + 12; y++) { wM.Set(x, y, z, Mat.Pitch); heap++; }
    for (int t = 0; t < 120 && simM.PitchBusy; t++) RunM(.5);
    int inDitch = 0, onBank = 0, onWater = 0, pitchAll = 0;
    for (int z = qz - 30; z < qz + 30; z++)
        for (int x = qx - 30; x < qx + 30; x++)
            for (int y = qg - 20; y < qg + 30; y++)
            {
                if (wM.Get(x, y, z) != Mat.Pitch) continue;
                pitchAll++;
                if (Math.Abs(x - qx) <= 8 && y < qg) inDitch++; else onBank++;
                if (wM.Get(x, y - 1, z) == Mat.Water) onWater++;
            }
    long water1Q = WaterQ();
    Check("pitch poured on a bank runs down into the ditch and comes to rest on the water in it, not mixing with it",
        pitchAll == heap && inDitch > heap * .7 && onWater > 50 && water1Q == water0Q && !simM.PitchBusy,
        $"{heap} voxels poured: {inDitch} in the ditch ({onWater} lying on the water), {onBank} left on the bank; water {water0Q} → {water1Q}");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--thieves")
{
    Console.WriteLine();
    var mpH = new MapParams();
    var wH = new VoxelWorld(mpH.SizeX, mpH.SizeY, mpH.SizeZ);
    var siteH = MapGen.Generate(wH, mpH);
    wH.Compact();
    var simH = new Sim(wH, siteH);
    simH.Waves.Countdown = 1e9; simH.WildTarget = 0;
    foreach (var u in simH.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
    void RunH(double seconds) { for (double t = 0; t < seconds; t += .05) simH.Step(.05f); }
    simH.ComputeFields();
    var (gxH, _, gzH) = simH.Nav.Feet(simH.Gate);
    simH.Stock[Stockpile.Wood] = 20 * VoxelWorld.PerM3;
    double wood0 = simH.Stock[Stockpile.Wood];

    // a thief comes to the citadel's door and takes what it can carry of the timber there; cut down, it drops it
    var thief = simH.Spawn(Kinds.Thief, simH.NearestStand((int)gxH - 160, (int)gzH, 2));
    double tookAt = -1;
    for (int t = 0; t < 80 && tookAt < 0; t++) { RunH(.5); if (thief.Loot > 0) tookAt = (t + 1) * .5; }
    double woodTaken = wood0 - simH.Stock[Stockpile.Wood], carried = thief.Loot;
    var doing = thief.State;
    simH.Hurt(thief, null, 1000, "test");
    double woodBack = simH.Stock[Stockpile.Wood];
    Check("a thief takes three cubic metres of timber at the citadel's door, and cut down on its way drops it",
        tookAt > 0 && Math.Abs(woodTaken - 3 * VoxelWorld.PerM3) < 1 && carried == woodTaken && Math.Abs(woodBack - wood0) < 1,
        $"took {Fmt.M3(woodTaken)} m³ after {tookAt} s ({doing}); killed: timber {Fmt.M3(wood0)} → {Fmt.M3(woodBack)} m³");

    // another gets away west with it: it is gone, and the thief comes back with the next wave
    var thief2 = simH.Spawn(Kinds.Thief, simH.NearestStand((int)gxH - 160, (int)gzH + 30, 2));
    for (int t = 0; t < 80 && thief2.Loot <= 0; t++) RunH(.5);
    bool took2 = thief2.Loot > 0;
    int west = simH.NearestStand(60, (int)thief2.Z, 2);
    if (west >= 0) { thief2.Cell = west; (thief2.X, thief2.Y, thief2.Z) = simH.Nav.Feet(west); thief2.Path = null; }
    int escaped0 = simH.Escaped.Count;
    for (int t = 0; t < 60 && !thief2.Dead; t++) RunH(.5);
    Check("a thief that gets out of the valley takes it with it, and comes back with the next wave",
        took2 && thief2.Dead && thief2.DiedAt < 0 && simH.Escaped.Count == escaped0 + 1 && simH.Stock[Stockpile.Wood] < wood0 - 1,
        $"took {took2}, gone {thief2.Dead}; timber {Fmt.M3(wood0)} → {Fmt.M3(simH.Stock[Stockpile.Wood])} m³; {simH.Escaped.Count - escaped0} to come back");

    // a worker of ours alone in the field: a thief beats it down, binds it and leads it off; cut down, it lets it go
    var lone = simH.Spawn(Kinds.Worker, simH.NearestStand((int)gxH - 420, (int)gzH - 120, 4));
    var snatcher = simH.Spawn(Kinds.Thief, simH.NearestStand((int)lone.X - 40, (int)lone.Z, 2));
    snatcher.Band = null;
    double boundAt = -1;
    for (int t = 0; t < 60 && boundAt < 0; t++) { RunH(.5); if (lone.Captor == snatcher) boundAt = (t + 1) * .5; }
    float led0 = lone.X;
    RunH(4);
    float ledTo = lone.X;
    float toThief = Sim.Dist(lone, snatcher);
    simH.Hurt(snatcher, null, 1000, "test");
    RunH(2);
    Check("a thief beats down a worker caught alone, binds him and leads him off; cut down, it lets him go",
        boundAt > 0 && ledTo < led0 && toThief < 4 * VoxelWorld.PerMetre && lone.Captor == null && !lone.Dead,
        $"bound after {boundAt} s ({lone.Hp:0} hp), led {(led0 - ledTo) / VoxelWorld.PerMetre:0.0} m west at {toThief / VoxelWorld.PerMetre:0.0} m behind; freed {lone.Captor == null}, {lone.State}");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--engines")
{
    Console.WriteLine();
    var mpE = new MapParams();
    var wE = new VoxelWorld(mpE.SizeX, mpE.SizeY, mpE.SizeZ);
    var siteE = MapGen.Generate(wE, mpE);
    wE.Compact();
    var simE = new Sim(wE, siteE);
    simE.Waves.Countdown = 1e9; simE.WildTarget = 0;
    simE.HordeStock[Stockpile.Wood] = simE.HordeStock[Stockpile.Earth] = 0;
    foreach (var u in simE.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
    void RunE(double seconds) { for (double t = 0; t < seconds; t += .05) simE.Step(.05f); }
    int kxE = siteE.KeepX, kzE = siteE.KeepZ;
    int Masonry(int x0, int z0, int x1, int z1, int y0, int y1)
    {
        int n = 0;
        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) for (int y = y0; y < y1; y++) if (Mat.IsMasonry(wE.Get(x, y, z))) n++;
        return n;
    }
    // a stone wall four metres high across the way west of the citadel
    int wxE = kxE - siteE.KeepR - 60, wz0E = kzE - 60, wz1E = kzE + 60;
    simE.InstantLabour = true;
    int topE = Shapes.LevelTop(wE, wxE, kzE, 32);
    simE.DesignateBuild(Shapes.Line(wE, siteE, wxE, wz0E, wxE, wz1E, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Level = true }, topE));
    simE.InstantLabour = false;
    int wall0 = Masonry(wxE - 6, wz0E, wxE + 6, wz1E, 0, topE + 2);

    // a catapult sixty metres out: it comes to where it throws from, out of bowshot, and breaks the wall up; its guard keeps by it
    var cat = simE.Spawn(Kinds.Catapult, simE.NearestStand(wxE - 480, kzE, 3));
    var guardsE = new System.Collections.Generic.List<Unit>();
    foreach (var t in new[] { Kinds.Orc, Kinds.Goblin })
    {
        var g = simE.Spawn(t, simE.NearestStand((int)cat.X + 10, (int)cat.Z + 10, 2));
        g.Escort = cat; g.Guard = true; g.Home = cat.Cell; guardsE.Add(g);
    }
    double firstThrow = -1; float far = 0;
    for (int t = 0; t < 240 && simE.Time < 200; t++)
    {
        RunE(.5);
        if (firstThrow < 0 && cat.State == UnitState.Siege) { firstThrow = simE.Time; far = MathF.Abs(cat.X - wxE) / VoxelWorld.PerMetre; }
        if (firstThrow > 0 && simE.Time - firstThrow > 70) break;
    }
    int wall1 = Masonry(wxE - 6, wz0E, wxE + 6, wz1E, 0, topE + 2);
    float guardFar = 0; foreach (var g in guardsE) guardFar = Math.Max(guardFar, Sim.Dist(g, cat) / VoxelWorld.PerMetre);
    Check("a catapult comes to where it throws from, out of bowshot of the wall, and breaks the wall up; its guard keeps by it",
        firstThrow > 0 && far > 26 && far <= 41 && wall1 < wall0 - 300 && guardFar < 8,
        $"throwing from {far:0.0} m after {firstThrow:0} s; the wall {wall0} → {wall1} voxels of masonry in 70 s; guard at most {guardFar:0.0} m off");

    // plain arrows hardly hurt it; a fire arrow burns it down
    Unit bowE = null;
    foreach (var (bx, bz) in new[] { (150, 0), (120, 40), (120, -40), (0, 150), (0, -150), (-150, 0), (90, 90), (90, -90) })
    {
        bowE = simE.Spawn(Kinds.Archer, simE.NearestStand((int)cat.X + bx, (int)cat.Z + bz, 4));
        if (simE.Sight(bowE, cat) && Sim.Dist(bowE, cat) < 22 * VoxelWorld.PerMetre) break;
        bowE.Dead = true; bowE.DiedAt = -999;
    }
    bowE.Dead = false; bowE.DiedAt = 0;
    simE.Command(new[] { bowE }, bowE.Cell);
    foreach (var g in guardsE) { g.Dead = true; g.DiedAt = -999; }
    float chp0 = cat.Hp;
    for (int i = 0; i < 10; i++) simE.Arrows.Add(new Arrow { X0 = bowE.X, Y0 = bowE.Y + 12, Z0 = bowE.Z, X1 = cat.X, Y1 = cat.Y + 10, Z1 = cat.Z, Dur = .5f, Target = cat, From = bowE, Hits = true, Damage = 18 });
    RunE(1);
    float plain = chp0 - cat.Hp;
    simE.SetFireArrows(new[] { bowE }, true);
    double burntAt = -1;
    for (int t = 0; t < 120 && burntAt < 0; t++) { RunE(.5); if (cat.Dead) burntAt = (t + 1) * .5; }
    Check("plain arrows hardly hurt an engine, a fire arrow burns it down", plain < 50 && burntAt > 0,
        $"ten arrows: {plain:0} of {chp0:0}; with fire arrows it burnt {(burntAt > 0 ? $"down in {burntAt} s" : $"to {cat.Hp:0}")}");
    bowE.Dead = true; bowE.DiedAt = -999;

    // a ram breaks through a wall a metre thick, a long one it would take long to go round, in well under a minute
    int rwx = wxE + 40;
    simE.InstantLabour = true;
    int rtopE = Shapes.LevelTop(wE, rwx, kzE, 32);
    simE.DesignateBuild(Shapes.Line(wE, siteE, rwx, kzE - 300, rwx, kzE + 300, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Level = true }, rtopE));
    simE.InstantLabour = false;
    simE.ComputeFields();
    var ram = simE.Spawn(Kinds.Ram, simE.NearestStand(rwx - 30, kzE + 40, 3));
    int before = Masonry(rwx - 6, kzE - 20, rwx + 6, kzE + 100, 0, rtopE + 2);
    double through = -1;
    for (int t = 0; t < 120 && through < 0; t++) { RunE(.5); if (ram.X > rwx + 8) through = (t + 1) * .5; }
    int after = Masonry(rwx - 6, kzE - 20, rwx + 6, kzE + 100, 0, rtopE + 2);
    Check("a ram breaks through a stone wall a metre thick in well under a minute", through > 0 && through < 50 && after < before,
        $"through after {through} s, {before - after} voxels of masonry broken; it is now {ram.State}, {MathF.Sqrt((ram.X - kxE) * (ram.X - kxE) + (ram.Z - kzE) * (ram.Z - kzE)) / VoxelWorld.PerMetre:0} m from the citadel");
    ram.Dead = true; ram.DiedAt = -999;

    // a mantlet between an archer of ours and a goblin takes the arrows loosed at the goblin
    Unit bowM = null, gobM = null;
    foreach (var (bx, bz) in new[] { (-300, -200), (-300, 200), (-340, 0), (-260, -120) })
    {
        bowM = simE.Spawn(Kinds.Archer, simE.NearestStand(rwx + bx, kzE + bz, 4));
        gobM = simE.Spawn(Kinds.Goblin, simE.NearestStand((int)bowM.X - 120, (int)bowM.Z, 2));
        if (simE.Sight(bowM, gobM) && Sim.Dist(bowM, gobM) > 12 * VoxelWorld.PerMetre) break;
        bowM.Dead = gobM.Dead = true; bowM.DiedAt = gobM.DiedAt = -999;
    }
    simE.Command(new[] { bowM }, bowM.Cell);
    gobM.Raid = null; gobM.Guard = true; gobM.Home = gobM.Cell; gobM.Hp = gobM.MaxHp = 500;
    var shield = simE.Spawn(Kinds.Mantlet, simE.NearestStand((int)(gobM.X + (bowM.X - gobM.X) * .12f), (int)(gobM.Z + (bowM.Z - gobM.Z) * .12f), 2));
    float ghp0 = gobM.Hp, shp0 = shield.Hp;
    int shotsM = simE.Shots;
    RunE(8);
    Check("a mantlet before a goblin takes the arrows loosed at it", simE.Shots - shotsM >= 3 && gobM.Hp >= ghp0 - 1 && shield.Hp < shp0,
        $"{simE.Shots - shotsM} arrows: the goblin {ghp0:0} → {gobM.Hp:0}, the mantlet {shp0:0} → {shield.Hp:0}, {Sim.Dist(shield, gobM) / VoxelWorld.PerMetre:0.0} m before it");
    foreach (var x in new[] { bowM, gobM, shield }) { x.Dead = true; x.DiedAt = -999; }

    // a siege tower rolls up to a wall three metres high and drops its ladder against it; broken, the ladder comes down with it
    int twx = rwx - 150;
    simE.InstantLabour = true;
    int ttop = Shapes.LevelTop(wE, twx, kzE - 150, 24);
    simE.DesignateBuild(Shapes.Line(wE, siteE, twx, kzE - 240, twx, kzE - 60, new Stroke { Mat = Mat.Stone, Width = 8, Height = 24, Level = true }, ttop));
    simE.InstantLabour = false;
    var tower = simE.Spawn(Kinds.SiegeTower, simE.NearestStand(twx - 90, kzE - 150, 3));
    double dockedAt = -1;
    for (int t = 0; t < 120 && dockedAt < 0; t++) { RunE(.5); if (tower.Docked.Count > 0) dockedAt = (t + 1) * .5; }
    int ladder = 0; foreach (int p in tower.Docked) { var (x, y, z) = WaterPlanner.Unpack(p); if (wE.Get(x, y, z) == Mat.TowerLadder) ladder++; }
    simE.Hurt(tower, null, 5000, "test");
    int left = 0;
    for (int z = kzE - 240; z < kzE - 60; z++) for (int x = twx - 24; x < twx; x++) for (int y = ttop - 30; y < ttop + 6; y++) if (wE.Get(x, y, z) == Mat.TowerLadder) left++;
    Check("a siege tower rolls up to a wall and drops its ladder against it; broken, the ladder comes down with it",
        dockedAt > 0 && ladder > 20 && left == 0,
        $"docked after {dockedAt} s with {ladder} voxels of ladder; broken: {left} left");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--events")
{
    Console.WriteLine();
    var mpV = new MapParams();
    var wV = new VoxelWorld(mpV.SizeX, mpV.SizeY, mpV.SizeZ);
    var siteV = MapGen.Generate(wV, mpV);
    wV.Compact();
    var simV = new Sim(wV, siteV);
    simV.Waves.Countdown = 1e9; simV.WildTarget = 0;
    foreach (var u in simV.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
    void RunV(double seconds) { for (double t = 0; t < seconds; t += .05) simV.Step(.05f); }
    int kxV = siteV.KeepX, kzV = siteV.KeepZ;
    int LogsLeft(int x0, int z0)
    {
        int n = 0;
        for (int z = z0; z < z0 + 40; z++) for (int x = x0; x < x0 + 4; x++) for (int y = 0; y < wV.SY; y++) if (wV.Get(x, y, z) == Mat.Wood) n++;
        return n;
    }
    void LogWall(int x0, int z0)
    {
        for (int z = z0; z < z0 + 40; z++) for (int x = x0; x < x0 + 4; x++) { int g = wV.SurfAt(x, z); for (int y = g; y < g + 14; y++) wV.Set(x, y, z, Mat.Wood); }
    }
    // a wall of logs set alight burns far less in the rain
    int ax = kxV - 140, az = kzV + 100, bx = kxV - 140, bz = kzV - 160;
    int ag = wV.SurfAt(ax, az + 20), bg = wV.SurfAt(bx, bz + 20);
    LogWall(ax, az); LogWall(bx, bz);
    int a0 = LogsLeft(ax, az), b0 = LogsLeft(bx, bz);
    // on its face, where the air gets at it
    simV.Ignite(ax, ag + 1, az + 20);
    RunV(150);
    int aBurnt = a0 - LogsLeft(ax, az);
    simV.Burning.Clear();
    simV.StartWeather(Weather.Rain, 600);
    simV.Ignite(bx, bg + 1, bz + 20);
    RunV(150);
    int bBurnt = b0 - LogsLeft(bx, bz);
    Check("a wall of logs set alight burns far less in the rain", aBurnt > 40 && bBurnt * 3 < aBurnt,
        $"in 150 s: {aBurnt} voxels of logs gone in clear weather, {bBurnt} in the rain");
    simV.Burning.Clear();

    // fog: an archer's reach falls to three fifths
    var bowV = simV.Spawn(Kinds.Archer, simV.NearestStand(kxV - 200, kzV, 4));
    simV.StartWeather(Weather.Clear, 0); simV.Weather = Weather.Clear;
    float clearReach = simV.RangeAt(bowV, bowV.X + 100, bowV.Z);
    simV.StartWeather(Weather.Fog, 600);
    float fogReach = simV.RangeAt(bowV, bowV.X + 100, bowV.Z);
    simV.Weather = Weather.Clear; simV.WeatherLeft = 0;
    Check("in fog an archer reaches three fifths as far", Math.Abs(fogReach - clearReach * .6f) < 1, $"{clearReach / VoxelWorld.PerMetre:0.0} m clear, {fogReach / VoxelWorld.PerMetre:0.0} m in fog");
    bowV.Dead = true; bowV.DiedAt = -999;

    // a cave bear goes for a goblin that comes near it
    var bear = simV.Spawn(Kinds.Bear, simV.NearestStand(kxV - 300, kzV + 200, 2));
    bear.Home = bear.Cell;
    // a goblin on the same ground, five metres off
    Unit gobV = null;
    foreach (var (gx, gz) in new[] { (40, 0), (-40, 0), (0, 40), (0, -40), (28, 28), (-28, -28), (28, -28), (-28, 28) })
    {
        gobV = simV.Spawn(Kinds.Goblin, simV.NearestStand((int)bear.X + gx, (int)bear.Z + gz, 2));
        if (MathF.Abs(gobV.Y - bear.Y) < 4) break;
        gobV.Dead = true; gobV.DiedAt = -999;
    }
    gobV.Dead = false; gobV.DiedAt = 0;
    gobV.Raid = null; gobV.Guard = true; gobV.Home = gobV.Cell;
    float ghV = gobV.Hp;
    RunV(10);
    Check("a cave bear goes for a goblin that comes near it", gobV.Dead || gobV.Hp < ghV - 15,
        $"the goblin {ghV:0} → {(gobV.Dead ? "dead" : $"{gobV.Hp:0} hp")}; the bear {bear.State}, {bear.Hp:0} hp");
    bear.Dead = true; bear.DiedAt = -999; gobV.Dead = true; gobV.DiedAt = -999;

    // an envoy of the horde comes to the walls and makes an offer; a bargain struck holds; an envoy cut down angers the horde
    simV.Waves.Number = 2; simV.Waves.Countdown = 500;
    simV.PlanWave(3);
    simV.Stock.Gold = 500;
    simV.SendEnvoy();
    var envoy = simV.Offer?.Envoy;
    double offerAt = -1;
    for (int t = 0; t < 240 && offerAt < 0 && envoy != null; t++) { RunV(.5); if (simV.Offer is { Kind: not DealKind.None }) offerAt = (t + 1) * .5; }
    var kind = simV.Offer?.Kind ?? DealKind.None;
    double cd0 = simV.Waves.Countdown, gold0V = simV.Stock.Gold;
    int roster0 = 0; foreach (var b in simV.Bands) if (b.State == BandState.Coming) roster0 += b.Roster.Count;
    bool struck = simV.Answer(true);
    int roster1 = 0; foreach (var b in simV.Bands) if (b.State == BandState.Coming) roster1 += b.Roster.Count;
    bool held = kind switch
    {
        DealKind.Delay => simV.Waves.Countdown > cd0 + 170,
        DealKind.Tribute => roster1 < roster0,
        DealKind.NoEngines => roster1 < roster0,
        _ => false,
    };
    RunV(2);
    simV.SendEnvoy();
    var envoy2 = simV.Offer?.Envoy;
    RunV(3);
    int rosterA = 0; foreach (var b in simV.Bands) if (b.State == BandState.Coming) rosterA += b.Roster.Count;
    if (envoy2 != null) simV.Hurt(envoy2, null, 1000, "test");
    RunV(1);
    int rosterB = 0; foreach (var b in simV.Bands) if (b.State == BandState.Coming) rosterB += b.Roster.Count;
    Check("an envoy comes to the walls and makes an offer; the bargain struck holds; an envoy cut down angers the horde",
        envoy != null && offerAt > 0 && struck && held && simV.Angered && rosterB > rosterA,
        $"offer after {offerAt} s: {kind}; struck {struck}, held {held} (countdown {cd0:0} → {simV.Waves.Countdown:0}, raiders {roster0} → {roster1}, gold {gold0V:0} → {simV.Stock.Gold:0}); " +
        $"the second cut down: angered {simV.Angered}, raiders {rosterA} → {rosterB}");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--faith")
{
    Console.WriteLine();
    var mpF = new MapParams();
    var wF = new VoxelWorld(mpF.SizeX, mpF.SizeY, mpF.SizeZ);
    var siteF = MapGen.Generate(wF, mpF);
    wF.Compact();
    var simF = new Sim(wF, siteF);
    simF.Waves.Countdown = 1e9; simF.WildTarget = 0;
    void RunF(double seconds) { for (double t = 0; t < seconds; t += .05) simF.Step(.05f); }
    int kxF = siteF.KeepX, kzF = siteF.KeepZ;
    // the valley's three places of power, each what it should be
    var powers = simF.Mines.FindAll(m => m.Power);
    var barrowF = powers.Find(m => m.Name == "Курган");
    var blackF = powers.Find(m => m.Name == "Чёрный камень");
    var springF = powers.Find(m => m.Name == "Мёртвый родник");
    int menhir = 0, pool = 0, passage = 0;
    if (blackF != null) for (int y = 0; y < wF.SY; y++) if (wF.Get(blackF.Place.X, y, blackF.Place.Z) == Mat.Menhir) menhir++;
    if (springF != null) for (int y = 0; y < wF.SY; y++) if (wF.Get(springF.Place.X, y, springF.Place.Z) == Mat.Water) pool++;
    if (barrowF != null)
    {
        // the passage into the mound: open under its crown
        int bx = barrowF.Place.X, bz = barrowF.Place.Z, top = wF.SurfAt(bx, bz);
        for (int y = top - 1; y > top - 30; y--) if (wF.Get(bx, y, bz) == Mat.Air) passage++;
    }
    Check("the valley has its three places of power: a barrow with a passage into it, the black stone, a dead spring",
        powers.Count == 3 && barrowF != null && passage > 4 && menhir >= 40 && pool >= 5,
        $"{powers.Count} places; the barrow's passage {passage} voxels high under the crown, the black stone {menhir} voxels tall, the spring {pool} deep");

    // a priest goes to a sanctuary on plain ground and prays: intervention comes in, a point a minute
    var priestF = simF.Spawn(Kinds.Priest, simF.Gate);
    var plainF = simF.AddZone(kxF - 200, kzF + 60, kxF - 160, kzF + 100, Zone.Sanctum, 1);
    double f0 = simF.Favour;
    RunF(90);
    double plainGain = simF.Favour - f0;
    bool prayed = priestF.Pastime == Pastime.Pray && priestF.Shrine >= 0 && simF.Nav.Feet(priestF.Shrine) is var (fxF, _, fzF) && plainF.Contains((int)fxF, (int)fzF);
    Check("a priest goes to the sanctuary and prays there, and the prayer brings in intervention", prayed && plainGain > .8 && plainGain < 1.6,
        $"praying {prayed} ({Retinue0(priestF)}), +{plainGain:0.00} in 90 s");

    // a sanctuary on the barrow: the priest takes the place, and there it is heard thrice as well
    simF.RemoveZones(plainF.X0, plainF.Z0, plainF.X1, plainF.Z1);
    var bp = barrowF.Place;
    var onBarrow = simF.AddZone(bp.X - 32, bp.Z - 32, bp.X + 32, bp.Z + 32, Zone.Sanctum, 1);
    RunF(60);
    bool heldF = barrowF.Holder == Holder.Ours;
    double f1 = simF.Favour;
    RunF(60);
    double powerGain = simF.Favour - f1;
    // defiled, it is not heard at all
    barrowF.Holder = Holder.Horde; barrowF.Grip = -1;
    double deaf = simF.Hearing(onBarrow);
    barrowF.Holder = Holder.Ours; barrowF.Grip = 1;
    Check("a priest takes the place of power it prays at; there prayer is heard thrice as well, and not at all once the horde defiles it",
        heldF && powerGain > 2.4 && powerGain < 3.3 && deaf == 0,
        $"{bp.Name} {(heldF ? "ours" : barrowF.Holder.ToString())}, +{powerGain:0.00} a minute ({Retinue0(priestF)}); defiled, heard ×{deaf}");

    // at night the priest goes into the citadel and keeps in till the morning
    simF.Waves.StartNow(simF);
    for (int t = 0; t < 120 && !priestF.Inside; t++) RunF(.5);
    bool inF = priestF.Inside;
    RunF(10);
    Check("at night the priest shelters in the citadel and stays there", inF && priestF.Inside, $"inside {inF}, still {priestF.Inside}");
    simF.Waves.CallOff(simF);
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--captives")
{
    Console.WriteLine();
    var mpC = new MapParams();
    var wC = new VoxelWorld(mpC.SizeX, mpC.SizeY, mpC.SizeZ);
    var siteC = MapGen.Generate(wC, mpC);
    wC.Compact();
    var simC = new Sim(wC, siteC);
    simC.Waves.Countdown = 1e9; simC.WildTarget = 0;
    void RunC(double seconds) { for (double t = 0; t < seconds; t += .05) simC.Step(.05f); }
    int kxC = siteC.KeepX, kzC = siteC.KeepZ;

    // a swordsman told to take them alive fells a goblin and binds it instead of killing it
    var swordC = simC.Spawn(Kinds.Swordsman, simC.NearestStand(kxC - 200, kzC + 40, 2));
    swordC.TakeAlive = true;
    var gobC = simC.Spawn(Kinds.Goblin, simC.NearestStand(kxC - 188, kzC + 40, 2));
    gobC.Guard = true; gobC.Home = gobC.Cell;
    for (int t = 0; t < 240 && !gobC.Bound && !gobC.Dead; t++) RunC(.25);
    bool boundC = gobC.Bound && !gobC.Dead;
    RunC(3);
    Check("a swordsman told to take them alive fells a goblin and binds it instead of killing it", boundC && !gobC.Dead && gobC.Bound,
        $"the goblin {(gobC.Dead ? "dead: " + gobC.KilledBy : gobC.Bound ? $"bound, {gobC.Hp:0} hp left" : $"free, {gobC.Hp:0} hp")}; the swordsman {swordC.State}");

    // a free worker goes out for it and leads it into the citadel
    var porterC = simC.Spawn(Kinds.Worker, simC.Gate);
    for (int t = 0; t < 240 && !gobC.Inside && !gobC.Dead; t++) RunC(.5);
    Check("a free worker goes out for the bound goblin and leads it into the citadel, a prisoner", gobC.Inside && gobC.Bound && simC.Prisoners() == 1,
        $"inside {gobC.Inside}, {simC.Prisoners()} prisoners; the worker {Retinue0(porterC)}");

    // a priest leads the prisoner to the altar and gives it to the old powers
    var priestC = simC.Spawn(Kinds.Priest, simC.Gate);
    simC.AddZone(kxC - 200, kzC + 60, kxC - 160, kzC + 100, Zone.Sanctum, 1);
    RunC(5);
    double favC = simC.Favour;
    string askC = simC.Sacrifice(RiteKind.Prisoner);
    for (int t = 0; t < 240 && !gobC.Dead; t++) RunC(.5);
    Check("a priest leads the prisoner bound to the altar and gives it to the old powers: intervention and a revelation",
        askC == null && gobC.Dead && gobC.KilledBy == Sim.GivenToPowers && simC.Revelations == 1 && simC.Favour - favC >= 15 && simC.RiteNow == null,
        $"asked: {askC ?? "taken in hand"}; the goblin {(gobC.Dead ? gobC.KilledBy : "alive")}; revelations {simC.Revelations}, intervention +{simC.Favour - favC:0.0}");

    // a carcass from the hunt: a little intervention, no revelation
    simC.Carcasses.Add(Kinds.Deer);
    favC = simC.Favour;
    askC = simC.Sacrifice(RiteKind.Carcass);
    for (int t = 0; t < 120 && simC.RiteNow != null; t++) RunC(.5);
    Check("a carcass from the hunt is offered up: a little intervention, no revelation",
        askC == null && simC.RiteNow == null && simC.Carcasses.Count == 0 && simC.Favour - favC >= 4 && simC.Revelations == 1,
        $"asked: {askC ?? "taken in hand"}; intervention +{simC.Favour - favC:0.0}, revelations {simC.Revelations}");

    // one of ours: much intervention, two revelations, and the household mourns and works slower
    var lambC = simC.Spawn(Kinds.Worker, simC.Gate);
    favC = simC.Favour;
    askC = simC.Sacrifice(RiteKind.Own, lambC);
    for (int t = 0; t < 240 && !lambC.Dead; t++) RunC(.5);
    Check("one of ours given up: much intervention, two revelations, and the household mourns",
        askC == null && lambC.Dead && simC.Revelations == 3 && simC.Favour - favC >= 50 && simC.MournUntil > simC.Time,
        $"asked: {askC ?? "taken in hand"}; the worker {(lambC.Dead ? lambC.KilledBy : "alive")}; revelations {simC.Revelations}, intervention +{simC.Favour - favC:0.0}, mourning {simC.MournUntil - simC.Time:0} s");

    // by night there are no rites, and a prisoner left unguarded slips out of the citadel and runs
    var gob2C = simC.Spawn(Kinds.Goblin, simC.Gate);
    gob2C.Bound = true; gob2C.Inside = true;
    simC.Waves.StartNow(simC);
    RunC(1);
    simC.Carcasses.Add(Kinds.Boar);
    string nightC = simC.Sacrifice(RiteKind.Carcass);
    for (int t = 0; t < 300 && gob2C.Inside; t++) RunC(.5);
    Check("by night there are no rites; a prisoner left unguarded slips out of the citadel and runs", nightC != null && !gob2C.Inside && !gob2C.Bound && gob2C.Routed,
        $"a rite by night: {nightC ?? "asked"}; the prisoner {(gob2C.Inside ? "still inside" : gob2C.Routed ? "out and running" : "out")}");
    simC.Waves.CallOff(simC);

    // the horde's envoy buys its people back
    var orcC = simC.Spawn(Kinds.Orc, simC.Gate);
    orcC.Bound = true; orcC.Inside = true;
    var envC = simC.Spawn(Kinds.Goblin, simC.NearestStand(kxC - 300, kzC, 2));
    envC.Envoy = true;
    simC.Offer = new Deal { Kind = DealKind.Ransom, Cost = 1, Envoy = envC, Until = simC.Time + 90 };
    double goldC = simC.Stock.Gold;
    bool tookC = simC.Answer(true);
    Check("the horde's envoy buys its people back: the prisoners go home, and gold comes in", tookC && orcC.Dead && simC.Prisoners() == 0 && simC.Stock.Gold - goldC >= 30,
        $"struck {tookC}, the orc {(orcC.Dead ? orcC.KilledBy : "still held")}, gold +{simC.Stock.Gold - goldC:0}");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--spells")
{
    Console.WriteLine();
    var mpP = new MapParams();
    var wP = new VoxelWorld(mpP.SizeX, mpP.SizeY, mpP.SizeZ);
    var siteP = MapGen.Generate(wP, mpP);
    wP.Compact();
    var simP = new Sim(wP, siteP);
    simP.Waves.Countdown = 1e9; simP.WildTarget = 0;
    void RunP(double seconds) { for (double t = 0; t < seconds; t += .05) simP.Step(.05f); }
    int kxP = siteP.KeepX, kzP = siteP.KeepZ;
    Unit Raider(UnitType t, int x, int z) { var u = simP.Spawn(t, simP.NearestStand(x, z, t.Move.Height)); u.Guard = true; u.Home = u.Cell; return u; }
    void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }

    // learning: the first of each circle for intervention, the rest for revelations
    simP.Favour = 25;
    bool mireFirst = simP.LearnSpell(SpellKind.Mire), handP = simP.LearnSpell(SpellKind.Hand);
    simP.Revelations = 2;
    bool mireP = simP.LearnSpell(SpellKind.Mire);
    Check("spells are learnt: the first of a circle for intervention, the rest for revelations", !mireFirst && handP && mireP && simP.Favour == 5 && simP.Revelations == 0,
        $"the Mire before any revelation {mireFirst}, the Hand {handP}, the Mire then {mireP}; {simP.Favour} intervention, {simP.Revelations} revelations left");
    foreach (var s in Sim.Spells) simP.Known.Add(s.Kind);
    simP.Favour = 5000;

    // the Hand: a bank of earth rises along the stroke; cast again at once, it is not ready
    int hx = kxP - 220, hz0 = kzP - 60, hz1 = kzP + 20, hmid = (hz0 + hz1) / 2;
    int hg0 = wP.SurfAt(hx, hmid);
    string castHand = simP.Cast(SpellKind.Hand, hx, hg0, hz0, hx, hz1), againHand = simP.Cast(SpellKind.Hand, hx, hg0, hz0, hx, hz1);
    RunP(4);
    int hg1 = wP.SurfAt(hx, hmid);
    Check("the Hand of the earth raises a bank along the stroke, and the spell waits before it can be cast again", castHand == null && againHand != null && hg1 - hg0 >= 16,
        $"cast: {castHand ?? "yes"}, again at once: {againHand ?? "yes"}; the ground {hg0} → {hg1}");

    // the Mire: one walking through it goes at a third of the pace
    int mx0 = kxP - 150, mz = kzP + 150, mx1 = kxP + 90;
    var walkerA = simP.Spawn(Kinds.Worker, simP.NearestStand(mx0, mz, 2));
    simP.Command(new[] { walkerA }, simP.NearestStand(mx1, mz, 2));
    float ax0 = walkerA.X;
    RunP(8);
    float freeWay = walkerA.X - ax0;
    Gone(walkerA);
    var walkerB = simP.Spawn(Kinds.Worker, simP.NearestStand(mx0, mz, 2));
    simP.Cast(SpellKind.Mire, mx0 + 50, wP.SurfAt(mx0 + 50, mz), mz);
    simP.Command(new[] { walkerB }, simP.NearestStand(mx1, mz, 2));
    float bx0 = walkerB.X;
    RunP(8);
    float boggedWay = walkerB.X - bx0;
    Gone(walkerB);
    Check("in the Mire one goes at a third of the pace", freeWay > 80 && boggedWay < freeWay * .55f, $"8 s: {freeWay / 8:0} voxels on dry ground, {boggedWay / 8:0} in the bog");

    // the Fall: a ball knocked out of a stone wall comes down, and the goblin at its foot is hurt
    int fx = kxP - 200, fz0 = kzP - 200, fz1 = fz0 + 64;
    int ftop = Shapes.LevelTop(wP, fx, fz0 + 32, 40);
    simP.InstantLabour = true;
    simP.DesignateBuild(Shapes.Line(wP, siteP, fx, fz0, fx, fz1, new Stroke { Mat = Mat.Stone, Width = 8, Height = 40, Level = true }, ftop));
    simP.InstantLabour = false;
    int StoneIn() { int n = 0; for (int z = fz0 - 4; z <= fz1 + 4; z++) for (int x = fx - 8; x <= fx + 8; x++) for (int y = ftop - 48; y < ftop + 4; y++) if (Mat.IsMasonry(wP.Get(x, y, z))) n++; return n; }
    int stone0 = StoneIn();
    var gobF = Raider(Kinds.Goblin, fx - 14, fz0 + 32);
    float fh0 = gobF.Hp;
    string castFall = simP.Cast(SpellKind.Fall, fx, ftop - 20, fz0 + 32);
    RunP(3);
    int stone1 = StoneIn();
    Check("the Fall knocks a ball out of a wall: it comes down, and the goblin at its foot is hurt", castFall == null && stone0 - stone1 > 1000 && (gobF.Dead || gobF.Hp < fh0 - 20),
        $"cast: {castFall ?? "yes"}; masonry {stone0} → {stone1}; the goblin {(gobF.Dead ? "dead: " + gobF.KilledBy : $"{gobF.Hp:0} of {fh0:0}")}");
    Gone(gobF);

    // the Fire Rain: a wall of logs catches, and a goblin by it burns
    int rx = kxP - 300, rz = kzP - 120;
    for (int z = rz; z < rz + 40; z++) for (int x = rx; x < rx + 4; x++) { int g = wP.SurfAt(x, z); for (int y = g; y < g + 14; y++) wP.Set(x, y, z, Mat.Wood); }
    var gobR = Raider(Kinds.Goblin, rx + 9, rz + 20);
    float rh0 = gobR.Hp;
    int caughtR = 0;
    Action<int, int, int, byte> catchR = (_, _, _, _) => caughtR++;
    simP.Caught += catchR;
    string castRain = simP.Cast(SpellKind.FireRain, rx + 4, wP.SurfAt(rx + 4, rz + 20), rz + 20);
    RunP(5);
    simP.Caught -= catchR;
    Check("the Fire Rain sets the logs alight and burns the goblin under it", castRain == null && caughtR > 0 && (gobR.Dead || gobR.Hp < rh0 - 10),
        $"cast: {castRain ?? "yes"}; {caughtR} voxels caught; the goblin {(gobR.Dead ? "dead: " + gobR.KilledBy : $"{gobR.Hp:0} of {rh0:0}")}");
    simP.Burning.Clear(); Gone(gobR);

    // the Finger of thunder: the raider by the cursor takes 150 and burns
    var trollT = Raider(Kinds.Troll, kxP - 260, kzP + 240);
    float th0 = trollT.Hp;
    string castBolt = simP.Cast(SpellKind.Thunder, trollT.X + 10, trollT.Y, trollT.Z);
    float th1 = trollT.Hp;
    Check("the Finger of thunder strikes the raider by the cursor: 150, and it burns", castBolt == null && th0 - th1 >= 145 && trollT.Burn > 0,
        $"cast: {castBolt ?? "yes"}; the troll {th0:0} → {th1:0}, burning {trollT.Burn:0.0} s");
    Gone(trollT);

    // Dread: the goblins in the circle run
    var scared = new System.Collections.Generic.List<Unit>();
    for (int i = 0; i < 4; i++) scared.Add(Raider(Kinds.Goblin, kxP - 340 + i * 12, kzP + 60));
    string castDread = simP.Cast(SpellKind.Dread, kxP - 322, wP.SurfAt(kxP - 322, kzP + 60), kzP + 60);
    Check("Dread puts the goblins in the circle to flight", castDread == null && scared.TrueForAll(u => u.Routed), $"cast: {castDread ?? "yes"}; running {scared.FindAll(u => u.Routed).Count} of 4");
    foreach (var u in scared) Gone(u);

    // Chains: a goblin held fast does not move, and a swordsman told to take them alive fells it at three fifths
    var heldC = Raider(Kinds.Goblin, kxP - 240, kzP - 280);
    heldC.Guard = false; heldC.Hp = 30;
    var (cx0, cz0) = (heldC.X, heldC.Z);
    string castChains = simP.Cast(SpellKind.Chains, heldC.X, heldC.Y, heldC.Z);
    RunP(2);
    float moved = MathF.Abs(heldC.X - cx0) + MathF.Abs(heldC.Z - cz0);
    var swordP = simP.Spawn(Kinds.Swordsman, simP.NearestStand((int)heldC.X + 10, (int)heldC.Z, 2));
    swordP.TakeAlive = true;
    for (int t = 0; t < 40 && !heldC.Bound && !heldC.Dead; t++) RunP(.2);
    Check("Chains hold a goblin fast, and a swordsman told to take them alive fells it at three fifths of itself", castChains == null && moved < 2 && heldC.Bound && !heldC.Dead,
        $"cast: {castChains ?? "yes"}; moved {moved:0.0} voxels in chains; the goblin {(heldC.Dead ? "dead" : heldC.Bound ? $"bound at {heldC.Hp:0}" : $"free, {heldC.Hp:0}")}");
    Gone(heldC); Gone(swordP);

    // the dead raised: goblins cut down get up as dead men, go for a goblin, and after their minute fall apart
    int dx = kxP - 380, dz = kzP - 40;
    for (int i = 0; i < 3; i++) { var d = Raider(Kinds.Goblin, dx + i * 10, dz); simP.Hurt(d, null, 1000, "test"); }
    RunP(.5);
    var foeD = Raider(Kinds.Goblin, dx + 40, dz + 20);
    string castRaise = simP.Cast(SpellKind.Raise, dx + 10, wP.SurfAt(dx + 10, dz), dz);
    int risenN = simP.Count(Kinds.Risen);
    for (int t = 0; t < 60 && !foeD.Dead; t++) RunP(.5);
    bool slew = foeD.Dead;
    RunP(62);
    Check("the dead raised get up and go for the horde, and after their minute they fall apart", castRaise == null && risenN == 3 && slew && simP.Count(Kinds.Risen) == 0,
        $"cast: {castRaise ?? "yes"}; {risenN} risen; the goblin {(slew ? "cut down" : "alive")}; {simP.Count(Kinds.Risen)} still standing after a minute");

    // the Eye: in the fog an archer sees as far as on a clear day
    var bowP = simP.Spawn(Kinds.Archer, simP.NearestStand(kxP - 160, kzP - 100, 2));
    simP.StartWeather(Weather.Fog, 600);
    float fogged = simP.RangeAt(bowP, bowP.X + 100, bowP.Z);
    string castEye = simP.Cast(SpellKind.Eye, kxP, 0, kzP);
    float seen = simP.RangeAt(bowP, bowP.X + 100, bowP.Z);
    simP.Weather = Weather.Clear; simP.WeatherLeft = 0;
    Check("the Eye: in the fog the archers see as far as on a clear day", castEye == null && seen > fogged * 1.6f, $"cast: {castEye ?? "yes"}; reach {fogged / VoxelWorld.PerMetre:0.0} → {seen / VoxelWorld.PerMetre:0.0} m");

    // the Blood Moon: by day it will not rise; at night it lights the dark for the archers
    string dayMoon = simP.Cast(SpellKind.BloodMoon, kxP, 0, kzP);
    simP.Waves.StartNow(simP);
    RunP(.5);
    float dark = simP.RangeAt(bowP, bowP.X + 100, bowP.Z);
    string nightMoon = simP.Cast(SpellKind.BloodMoon, kxP, 0, kzP);
    float lit = simP.RangeAt(bowP, bowP.X + 100, bowP.Z);
    simP.Waves.CallOff(simP);
    Check("the Blood Moon will not rise by day; at night it lights the dark for the archers", dayMoon != null && nightMoon == null && lit > dark * 1.5f,
        $"by day: {dayMoon ?? "cast"}; at night: {nightMoon ?? "cast"}; reach into the dark {dark / VoxelWorld.PerMetre:0.0} → {lit / VoxelWorld.PerMetre:0.0} m");
    Gone(bowP);

    // a stone skin on a wall of ours: the horde hacks it three times as long there, and nowhere else; on bare ground it will not take
    int sx = kxP - 120, sz0 = kzP + 260;
    int stop = Shapes.LevelTop(wP, sx, sz0 + 24, 24);
    simP.InstantLabour = true;
    simP.DesignateBuild(Shapes.Line(wP, siteP, sx, sz0, sx, sz0 + 48, new Stroke { Mat = Mat.Stone, Width = 8, Height = 24, Level = true }, stop));
    simP.InstantLabour = false;
    string skinBare = simP.Cast(SpellKind.Skin, sx - 60, wP.SurfAt(sx - 60, sz0), sz0);
    string skinWall = simP.Cast(SpellKind.Skin, sx, stop - 10, sz0 + 24);
    float hardIn = simP.SkinHack(sx, stop - 10, sz0 + 24), hardOut = simP.SkinHack(sx, stop - 10, sz0 + 200);
    Check("a stone skin makes our wall three times as long to hack, and will not take on bare ground", skinBare != null && skinWall == null && MathF.Abs(hardIn - 1 / 3f) < .01f && hardOut == 1,
        $"on bare ground: {skinBare ?? "cast"}; on the wall: {skinWall ?? "cast"}; hacking there ×{1 / hardIn:0.#} as long, elsewhere ×{1 / hardOut:0.#}");

    // the weather called down: rain for three minutes
    simP.WeatherLeft = 0; simP.Weather = Weather.Clear;
    string castWeather = simP.Cast(SpellKind.Weather, kxP, 0, kzP, variant: 0);
    Check("the weather is called down: rain, for three minutes", castWeather == null && simP.Weather == Weather.Rain && simP.WeatherLeft >= 179,
        $"cast: {castWeather ?? "yes"}; now {Sim.WeatherName(simP.Weather)}, {simP.WeatherLeft:0} s more");
    simP.Weather = Weather.Clear; simP.WeatherLeft = 0;

    // the fog of the dead: a goblin in it wastes away, one of ours in it does not
    int ox = kxP - 300, oz = kzP + 330;
    var gobO = Raider(Kinds.Goblin, ox, oz);
    var ourO = simP.Spawn(Kinds.Worker, simP.NearestStand(ox + 12, oz, 2));
    ourO.Hold = true;
    float go0 = gobO.Hp, wo0 = ourO.Hp;
    string castFog = simP.Cast(SpellKind.DeadFog, ox + 6, wP.SurfAt(ox + 6, oz), oz);
    RunP(4);
    Check("the fog of the dead wastes the raiders in it, and leaves ours be", castFog == null && go0 - gobO.Hp >= 10 && ourO.Hp >= wo0,
        $"cast: {castFog ?? "yes"}; in 4 s the goblin {go0:0} → {gobO.Hp:0}, the worker {wo0:0} → {ourO.Hp:0}");
    Gone(gobO); Gone(ourO);
    simP.DeadFogs.Clear();

    // the plague: the goblin it falls on sickens, and it passes to those standing by it, outside the circle
    int px = kxP - 380, pz = kzP + 120;
    var sickA = Raider(Kinds.Goblin, px, pz);
    var sickB = Raider(Kinds.Goblin, px + 12, pz);
    var sickC = Raider(Kinds.Goblin, px + 24, pz);
    string castPlague = simP.Cast(SpellKind.Plague, px - 34, wP.SurfAt(px - 34, pz), pz);
    bool onlyA = sickA.PlagueUntil > simP.Time && sickB.PlagueUntil < simP.Time && sickC.PlagueUntil < simP.Time;
    float pa0 = sickA.Hp;
    RunP(8);
    Check("the plague sickens the raider it falls on, and passes on to those standing by it", castPlague == null && onlyA && pa0 - sickA.Hp >= 12 && sickB.PlagueUntil > simP.Time - 1 && sickC.PlagueUntil > simP.Time - 1,
        $"cast: {castPlague ?? "yes"}; at first only the nearest: {onlyA}; it {pa0:0} → {sickA.Hp:0} in 8 s; the next {(sickB.PlagueUntil > 0 ? "sick" : "well")}, the one past it {(sickC.PlagueUntil > 0 ? "sick" : "well")}");
    Gone(sickA); Gone(sickB); Gone(sickC);

    // the blood oath: ours in the circle mend two fifths and stop bleeding
    var oathW = simP.Spawn(Kinds.Worker, simP.NearestStand(kxP - 140, kzP - 40, 2));
    oathW.Hold = true; oathW.Hp = 15;
    string castOath = simP.Cast(SpellKind.Oath, oathW.X, oathW.Y, oathW.Z);
    Check("the blood oath mends ours in the circle by two fifths, and holds a minute", castOath == null && oathW.Hp >= 15 + oathW.MaxHp * .39f && oathW.OathUntil > simP.Time + 59,
        $"cast: {castOath ?? "yes"}; the worker 15 → {oathW.Hp:0} of {oathW.MaxHp:0}, the oath for {oathW.OathUntil - simP.Time:0} s");
    Gone(oathW);

    // the call: one of ours led off by a thief is in the citadel that instant, and the thief goes empty-handed
    var ledW = simP.Spawn(Kinds.Worker, simP.NearestStand(kxP - 260, kzP - 140, 2));
    var thiefW = simP.Spawn(Kinds.Thief, simP.NearestStand(kxP - 268, kzP - 140, 2));
    thiefW.Captive = ledW; ledW.Captor = thiefW;
    string castCall = simP.Cast(SpellKind.Recall, ledW.X + 4, ledW.Y, ledW.Z);
    Check("the call takes one of ours out of a thief's hands into the citadel", castCall == null && ledW.Inside && ledW.Captor == null && thiefW.Captive == null,
        $"cast: {castCall ?? "yes"}; the worker {(ledW.Inside ? "in the citadel" : "outside")}, led by {(ledW.Captor == null ? "nobody" : "the thief")}");
    Gone(thiefW);
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--physics")
{
    Console.WriteLine();
    var mpY = new MapParams();
    var wY = new VoxelWorld(mpY.SizeX, mpY.SizeY, mpY.SizeZ);
    var siteY = MapGen.Generate(wY, mpY);
    wY.Compact();
    var simY = new Sim(wY, siteY);
    simY.Waves.Countdown = 1e9; simY.WildTarget = 0;
    void RunY(double seconds) { for (double t = 0; t < seconds; t += .05) simY.Step(.05f); }
    int kxY = siteY.KeepX, kzY = siteY.KeepZ;
    Unit Stand(UnitType t, int x, int z) { var u = simY.Spawn(t, simY.NearestStand(x, z, t.Move.Height)); u.Guard = true; u.Home = u.Cell; return u; }
    void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
    float Far(Unit u, float x, float z) => MathF.Sqrt((u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z)) / VoxelWorld.PerMetre;
    foreach (var s in Sim.Spells) simY.Known.Add(s.Kind);
    simY.Favour = 5000;
    // no trees in the way of what flies
    for (int z = kzY - 400; z < kzY + 400; z++)
        for (int x = kxY - 480; x < kxY - 40; x++)
            for (int y = wY.SurfAt(x, z), top = wY.ColTop(x, z); y < top; y++)
                if (Mat.IsVeg[wY.Get(x, y, z)]) wY.Set(x, y, z, Mat.Air);
    int CountY(int x0, int z0, int x1, int z1, Func<byte, bool> what)
    {
        int n = 0;
        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) for (int y = 4; y < wY.SY - 1; y++) if (what(wY.Get(x, y, z))) n++;
        return n;
    }

    // a blast in a stone wall: the heart of it goes to dust, the rest flies in pieces that come down as rubble, some of it
    // far off; what stood on it falls; the goblin by it is hurt and thrown
    int ex = kxY - 220, ez0 = kzY + 160, ez1 = ez0 + 80, emid = (ez0 + ez1) / 2;
    int etop = Shapes.LevelTop(wY, ex, emid, 48);
    simY.InstantLabour = true;
    simY.DesignateBuild(Shapes.Line(wY, siteY, ex, ez0, ex, ez1, new Stroke { Mat = Mat.Stone, Width = 8, Height = 48, Level = true }, etop));
    simY.InstantLabour = false;
    int stoneE0 = CountY(ex - 8, ez0 - 4, ex + 8, ez1 + 4, Mat.IsMasonry);
    var gobE = Stand(Kinds.Goblin, ex - 20, emid);
    float gobE0 = gobE.Hp;
    float gx0 = gobE.X, gz0 = gobE.Z;
    string castBlast = simY.Cast(SpellKind.Explosion, ex, etop - 30, emid);
    int flying = simY.Fliers.Count, aloft = simY.Aloft;
    bool thrownE = gobE.Airborne != null;
    RunY(8);
    int stoneE1 = CountY(ex - 8, ez0 - 4, ex + 8, ez1 + 4, Mat.IsMasonry);
    int rubble = 0; float farthest = 0;
    for (int z = emid - 200; z <= emid + 200; z++)
        for (int x = ex - 200; x <= ex + 200; x++)
        {
            int top = wY.ColTop(x, z);
            for (int y = Math.Max(4, top - 6); y < top; y++)
                if (wY.Get(x, y, z) == Mat.Rubble) { rubble++; farthest = MathF.Max(farthest, MathF.Sqrt((x - ex) * (x - ex) + (z - emid) * (z - emid)) / VoxelWorld.PerMetre); }
        }
    Check("a blast in a wall: it goes up in pieces that fly and come down as rubble far about, and the wall over it falls",
        castBlast == null && flying >= 10 && aloft > 3000 && simY.Fliers.Count == 0 && stoneE0 - stoneE1 > 6000 && rubble > 2000 && farthest > 8,
        $"cast: {castBlast ?? "yes"}; {flying} pieces, {aloft} voxels in the air, {simY.Fliers.Count} still flying after 8 s; masonry {stoneE0} → {stoneE1}; rubble {rubble}, the farthest {farthest:0.0} m off");
    Check("the goblin by the blast is hurt and thrown", thrownE && (gobE.Dead || gobE.Hp < gobE0 - 20) && gobE.Airborne == null && Far(gobE, gx0, gz0) > 2,
        $"thrown {thrownE}; the goblin {(gobE.Dead ? "dead: " + gobE.KilledBy : $"{gobE.Hp:0} of {gobE0:0}")}, {Far(gobE, gx0, gz0):0.0} m from where it stood, {(gobE.Airborne == null ? "down" : "still in the air")}");
    Gone(gobE);

    // telekinesis takes a goblin up, and swung hard and let go it flies off and is dashed down
    var gobT = Stand(Kinds.Goblin, kxY - 300, kzY - 200);
    float tx0 = gobT.X, tz0 = gobT.Z, ty0 = gobT.Y;
    string castGrip = simY.Cast(SpellKind.Telekinesis, gobT.X, gobT.Y + 5, gobT.Z);
    bool heldT = simY.HeldNow?.Unit == gobT;
    simY.HoldAt(gobT.X, ty0 + 32, gobT.Z);
    RunY(1);
    float lifted = (gobT.Y - ty0) / VoxelWorld.PerMetre;
    simY.HoldAt(gobT.X - 400, ty0 + 60, gobT.Z);
    RunY(.4);
    simY.LetGo();
    for (int t = 0; t < 40 && gobT.Airborne != null; t++) RunY(.25);
    Check("telekinesis takes a goblin up, and swung and let go it flies off and is dashed down", castGrip == null && heldT && lifted > 2.5f && gobT.Airborne == null && Far(gobT, tx0, tz0) > 15 && (gobT.Dead || gobT.Hp < gobT.MaxHp - 20),
        $"cast: {castGrip ?? "yes"}; held {heldT}; lifted {lifted:0.0} m; landed {Far(gobT, tx0, tz0):0.0} m off, {(gobT.Dead ? "dead: " + gobT.KilledBy : $"{gobT.Hp:0} of {gobT.MaxHp:0}")}");
    Gone(gobT);

    // a ball of earth torn out of the ground and let fall on a goblin: it is struck, and the earth lies in a heap where it fell
    int bx = kxY - 260, bz = kzY - 60, bg = wY.SurfAt(bx, bz);
    var gobB = Stand(Kinds.Goblin, bx + 120, bz);
    float gb0 = gobB.Hp;
    int SoilBy() { int n = 0; for (int z = (int)gobB.Z - 20; z <= gobB.Z + 20; z++) for (int x = (int)gobB.X - 20; x <= gobB.X + 20; x++) for (int y = (int)gobB.Y - 2; y < gobB.Y + 24; y++) if (wY.Get(x, y, z) == Mat.Soil) n++; return n; }
    int soil0 = SoilBy();
    simY.ReadyNow(SpellKind.Telekinesis);
    string castBall = simY.Cast(SpellKind.Telekinesis, bx + .5f, bg - 3, bz + .5f);
    var ball = simY.HeldNow;
    int ballN = ball?.Voxels.Count ?? 0;
    bool hole = wY.Get(bx, bg - 3, bz) == Mat.Air;
    simY.HoldAt(gobB.X, gobB.Y + 40, gobB.Z);
    RunY(3);
    simY.LetGo();
    RunY(3);
    RunY(3);
    int heap = SoilBy() - soil0;
    Check("a ball of earth torn out and let fall on a goblin strikes it, and lies in a heap where it fell", castBall == null && ballN > 600 && hole && (gobB.Dead || gobB.Hp < gb0 - 15) && heap > 300,
        $"cast: {castBall ?? "yes"}; {ballN} voxels lifted, a hole {hole}; the goblin {(gobB.Dead ? "dead: " + gobB.KilledBy : $"{gobB.Hp:0} of {gb0:0}")}; {heap} voxels of earth heaped by it");
    Gone(gobB);

    // what telekinesis holds does not go through a wall: pulled at one from behind it, it stays on its side
    int qx = kxY - 330, qz0 = kzY + 40;
    int qtop = Shapes.LevelTop(wY, qx, qz0 + 24, 48);
    simY.InstantLabour = true;
    simY.DesignateBuild(Shapes.Line(wY, siteY, qx, qz0, qx, qz0 + 48, new Stroke { Mat = Mat.Stone, Width = 8, Height = 48, Level = true }, qtop));
    simY.InstantLabour = false;
    var gobQ = Stand(Kinds.Goblin, qx - 30, qz0 + 24);
    simY.ReadyNow(SpellKind.Telekinesis);
    string castQ = simY.Cast(SpellKind.Telekinesis, gobQ.X, gobQ.Y + 5, gobQ.Z);
    simY.HoldAt(qx + 40, gobQ.Y + 16, gobQ.Z);
    RunY(2);
    float side = gobQ.X;
    simY.LetGo();
    RunY(2);
    Check("what telekinesis holds does not go through a wall", castQ == null && side < qx - 3, $"cast: {castQ ?? "yes"}; the goblin at x {side:0}, the wall at {qx - 4}..{qx + 4}");
    Gone(gobQ);

    // the black wind: the ladders against a wall are torn away, and the raiders there thrown off, away from the citadel
    int lx = kxY - 200, lz0 = kzY - 300;
    int ltop = Shapes.LevelTop(wY, lx, lz0 + 24, 40);
    simY.InstantLabour = true;
    simY.DesignateBuild(Shapes.Line(wY, siteY, lx, lz0, lx, lz0 + 48, new Stroke { Mat = Mat.Stone, Width = 8, Height = 40, Level = true }, ltop));
    simY.InstantLabour = false;
    for (int z = lz0 + 20; z < lz0 + 28; z++) { int g = wY.SurfAt(lx - 6, z); for (int y = g; y < ltop + 40; y++) if (wY.Get(lx - 5, y, z) == Mat.Air) wY.Set(lx - 5, y, z, Mat.SiegeLadder); }
    int ladders0 = CountY(lx - 8, lz0, lx, lz0 + 48, m => m == Mat.SiegeLadder);
    var gobW = Stand(Kinds.Goblin, lx - 24, lz0 + 12);
    var orcW = Stand(Kinds.Orc, lx - 24, lz0 + 36);
    float gw0 = MathF.Sqrt((gobW.X - kxY) * (gobW.X - kxY) + (gobW.Z - kzY) * (gobW.Z - kzY)), ow0 = MathF.Sqrt((orcW.X - kxY) * (orcW.X - kxY) + (orcW.Z - kzY) * (orcW.Z - kzY));
    string castWind = simY.Cast(SpellKind.BlackWind, lx - 10, wY.SurfAt(lx - 10, lz0 + 24), lz0 + 24);
    RunY(5);
    int ladders1 = CountY(lx - 8, lz0, lx, lz0 + 48, m => m == Mat.SiegeLadder);
    float gw1 = MathF.Sqrt((gobW.X - kxY) * (gobW.X - kxY) + (gobW.Z - kzY) * (gobW.Z - kzY)), ow1 = MathF.Sqrt((orcW.X - kxY) * (orcW.X - kxY) + (orcW.Z - kzY) * (orcW.Z - kzY));
    Check("the black wind tears the ladders from a wall and throws the raiders off, away from the citadel",
        castWind == null && ladders0 > 100 && ladders1 == 0 && (gw1 - gw0) / VoxelWorld.PerMetre > 6 && (ow1 - ow0) / VoxelWorld.PerMetre > 4 && gobW.Hp < gobW.MaxHp,
        $"cast: {castWind ?? "yes"}; ladder voxels {ladders0} → {ladders1}; the goblin {(gw1 - gw0) / VoxelWorld.PerMetre:0.0} m farther off ({(gobW.Dead ? "dead" : $"{gobW.Hp:0} hp")}), the orc {(ow1 - ow0) / VoxelWorld.PerMetre:0.0} m");
    Gone(gobW); Gone(orcW);

    // the rift: the ground sinks in a bowl, the goblin on it falls in hurt, and a wall standing on it comes down into it
    int fx2 = kxY - 360, fz2 = kzY + 220;
    int ftop2 = Shapes.LevelTop(wY, fx2, fz2, 32);
    simY.InstantLabour = true;
    simY.DesignateBuild(Shapes.Line(wY, siteY, fx2, fz2 - 24, fx2, fz2 + 24, new Stroke { Mat = Mat.Wood, Width = 8, Height = 32, Level = true }, ftop2));
    simY.InstantLabour = false;
    int woodR0 = CountY(fx2 - 8, fz2 - 28, fx2 + 8, fz2 + 28, m => m == Mat.Wood);
    var gobR = Stand(Kinds.Goblin, fx2 - 12, fz2);
    int gr0 = wY.GroundTop(fx2 - 6, fz2);
    string castRift = simY.Cast(SpellKind.Rift, fx2 - 6, gr0, fz2);
    int gr1 = wY.GroundTop(fx2 - 6, fz2);
    RunY(3);
    int woodR1 = CountY(fx2 - 8, fz2 - 28, fx2 + 8, fz2 + 28, m => m == Mat.Wood);
    Check("the rift: the ground sinks, the goblin on it falls in hurt, and the wall standing there comes down", castRift == null && gr0 - gr1 >= 16 && gobR.Hp <= gobR.MaxHp - 15 && woodR0 - woodR1 > 500,
        $"cast: {castRift ?? "yes"}; the ground {gr0} → {gr1}; the goblin {(gobR.Dead ? "dead: " + gobR.KilledBy : $"{gobR.Hp:0} of {gobR.MaxHp:0}")}; logs standing {woodR0} → {woodR1}");
    Gone(gobR);
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--powder")
{
    Console.WriteLine();
    var mpX = new MapParams();
    var wX = new VoxelWorld(mpX.SizeX, mpX.SizeY, mpX.SizeZ);
    var siteX = MapGen.Generate(wX, mpX);
    wX.Compact();
    var simX = new Sim(wX, siteX);
    simX.Waves.Countdown = 1e9; simX.WildTarget = 0;
    simX.HordeStock[Stockpile.Wood] = simX.HordeStock[Stockpile.Earth] = 0;
    foreach (var u in simX.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
    void RunX(double seconds) { for (double t = 0; t < seconds; t += .05) simX.Step(.05f); }
    int kxX = siteX.KeepX, kzX = siteX.KeepZ;
    Unit Camp(UnitType t, int x, int z) { var u = simX.Spawn(t, simX.NearestStand(x, z, t.Move.Height)); u.Guard = true; u.Home = u.Cell; return u; }
    void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
    float Apart(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz)) / VoxelWorld.PerMetre;
    // no trees in the way of what flies
    for (int z = kzX - 400; z < kzX + 400; z++)
        for (int x = kxX - 480; x < kxX - 40; x++)
            for (int y = wX.SurfAt(x, z), top = wX.ColTop(x, z); y < top; y++)
                if (Mat.IsVeg[wX.Get(x, y, z)]) wX.Set(x, y, z, Mat.Air);
    int CountX(int x0, int z0, int x1, int z1, Func<byte, bool> what)
    {
        int n = 0;
        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) for (int y = 4; y < wX.SY - 1; y++) if (what(wX.Get(x, y, z))) n++;
        return n;
    }
    // kegs stacked on the ground: a cube this many voxels a side
    System.Collections.Generic.List<(int x, int y, int z)> Kegs(int x0, int z0, int size, int sx = -1, int sz = -1)
    {
        var laid = new System.Collections.Generic.List<(int, int, int)>();
        for (int z = z0; z < z0 + (sz < 0 ? size : sz); z++)
            for (int x = x0; x < x0 + (sx < 0 ? size : sx); x++)
            {
                int g = wX.SurfAt(x, z);
                for (int y = g; y < g + size; y++) if (wX.Get(x, y, z) == Mat.Air) { wX.Set(x, y, z, Mat.Powder); laid.Add((x, y, z)); }
            }
        return laid;
    }
    var blasts = new System.Collections.Generic.List<(double t, float x, float z, float r)>();
    simX.Exploded += (x, y, z, r) => blasts.Add((simX.Time, x, z, r));

    // kegs set alight go up a moment after: the log wall beside them is breached, the goblin by them hurt and thrown
    int lx = kxX - 220, lz = kzX + 160;
    int ltop = Shapes.LevelTop(wX, lx, lz, 32);
    simX.InstantLabour = true;
    simX.DesignateBuild(Shapes.Line(wX, siteX, lx, lz - 32, lx, lz + 32, new Stroke { Mat = Mat.Wood, Width = 8, Height = 32, Level = true }, ltop));
    simX.InstantLabour = false;
    int logs0 = CountX(lx - 6, lz - 36, lx + 6, lz + 36, m => m == Mat.Wood);
    var k1 = Kegs(lx - 10, lz - 2, 4);
    var gobK = Camp(Kinds.Goblin, lx - 26, lz);
    float gk0 = gobK.Hp, gkx = gobK.X, gkz = gobK.Z;
    blasts.Clear();
    double litAt = simX.Time;
    var (ix, iy, iz) = k1[^1];
    bool caught = simX.Ignite(ix, iy, iz);
    RunX(3);
    int logs1 = CountX(lx - 6, lz - 36, lx + 6, lz + 36, m => m == Mat.Wood);
    Check("kegs of powder set alight go up a moment after: the log wall by them is breached, the goblin by them hurt and thrown",
        caught && blasts.Count >= 1 && blasts[0].t - litAt < 1 && logs0 - logs1 > 150 && (gobK.Dead || gobK.Hp < gk0 - 10) && Apart(gobK.X, gobK.Z, gkx, gkz) > 1,
        $"caught {caught}; {blasts.Count} blasts, the first {(blasts.Count > 0 ? $"{blasts[0].t - litAt:0.00} s after, {blasts[0].r / VoxelWorld.PerMetre:0.0} m" : "none")}; logs {logs0} → {logs1}; the goblin {(gobK.Dead ? "dead: " + gobK.KilledBy : $"{gobK.Hp:0} of {gk0:0}")}, {Apart(gobK.X, gobK.Z, gkx, gkz):0.0} m from where it stood");
    Gone(gobK);

    // three stacks of kegs a few paces apart: the first set alight sets off the next, and that the last
    int cx = kxX - 320, cz = kzX - 220;
    var stacks = new[] { Kegs(cx, cz, 3), Kegs(cx, cz + 20, 3), Kegs(cx, cz + 40, 3) };
    blasts.Clear();
    var (sx0, sy0, sz0) = stacks[0][^1];
    simX.Ignite(sx0, sy0, sz0);
    RunX(3);
    int leftC = CountX(cx - 4, cz - 4, cx + 8, cz + 48, m => m == Mat.Powder);
    bool inTurn = blasts.Count == 3 && blasts[0].z < blasts[1].z && blasts[1].z < blasts[2].z && blasts[0].t < blasts[2].t;
    Check("stacks of powder set each other off, one after another", inTurn && leftC == 0,
        $"{blasts.Count} blasts{(blasts.Count > 0 ? ": " + string.Join(", ", blasts.ConvertAll(b => $"{b.t - blasts[0].t:0.00} s at z {b.z - cz:0}")) : "")}; {leftC} voxels of powder left");

    // a trail of powder: the fire runs along it, and it goes up as it comes, the far end last
    int tx = kxX - 420, tz = kzX + 280;
    Kegs(tx, tz, 1, sx: 96, sz: 1);
    blasts.Clear();
    simX.Ignite(tx, wX.SurfAt(tx, tz) - 0, tz);
    if (blasts.Count == 0 && wX.Get(tx, wX.SurfAt(tx, tz) - 1, tz) == Mat.Powder) simX.Ignite(tx, wX.SurfAt(tx, tz) - 1, tz);
    RunX(4);
    // the far end goes up a good while after the near one
    var farEnd = blasts.Count > 0 ? blasts.MaxBy(bl => bl.x) : default;
    bool runs = blasts.Count >= 8 && farEnd.x > blasts[0].x + 60 && farEnd.t - blasts[0].t >= 1;
    Check("a trail of powder burns along, going up as the fire comes to it, the far end last", runs,
        $"{blasts.Count} blasts; the near end at x {(blasts.Count > 0 ? blasts[0].x - tx : 0):0}, the far end at x {farEnd.x - tx:0} {farEnd.t - (blasts.Count > 0 ? blasts[0].t : 0):0.00} s later");

    // a ring of stone round the citadel: the horde has to go through it somewhere
    simX.InstantLabour = true;
    simX.DesignateBuild(Shapes.Circle(wX, siteX, kxX, kzX, siteX.KeepR + 44, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Hollow = true }, 0));
    simX.InstantLabour = false;
    simX.ComputeFields();
    int ringR = siteX.KeepR + 44;
    int RingStone() => CountX(kxX - ringR - 8, kzX - ringR - 8, kxX + ringR + 8, kzX + ringR + 8, Mat.IsMasonry);
    Charge LaidBy(Unit s, int seconds)
    {
        for (int t = 0; t < seconds * 2; t++) { RunX(.5); var c = simX.Charges.Find(c => c.Planted); if (c != null) return c; }
        return null;
    }

    // one of ours at a sapper's burning fuse cuts it: no blast, and the powder lies there for us
    var sap2 = simX.Spawn(Kinds.Sapper, simX.NearestStand(kxX - ringR - 60, kzX + 60, 2));
    sap2.Charge = true;
    var laid2 = LaidBy(sap2, 90);
    Unit cutter = null;
    if (laid2 != null) { cutter = simX.Spawn(Kinds.Swordsman, simX.NearestStand((int)laid2.X, (int)laid2.Z, 2)); cutter.Hold = true; }
    blasts.Clear();
    RunX(8);
    int keptPowder = laid2 == null ? 0 : CountX((int)laid2.X - 8, (int)laid2.Z - 8, (int)laid2.X + 8, (int)laid2.Z + 8, m => m == Mat.Powder);
    Check("one of ours at a sapper's fuse cuts it: no blast, and the powder lies there for us",
        laid2 != null && cutter != null && !simX.Charges.Contains(laid2) && blasts.Count == 0 && keptPowder >= 12,
        $"{(laid2 != null ? "laid" : "not laid")}, {(cutter != null ? $"a swordsman {Apart(cutter.X, cutter.Z, laid2.X, laid2.Z):0.0} m from it" : "nobody")}; {blasts.Count} blasts; {keptPowder} voxels of powder lie there");
    Gone(sap2); if (cutter != null) Gone(cutter);

    // the nearest free fighter of ours runs to a sapper's burning fuse by itself, and cuts it in time
    var sap3 = simX.Spawn(Kinds.Sapper, simX.NearestStand(kxX - ringR - 60, kzX + 200, 2));
    sap3.Charge = true;
    var laid3 = LaidBy(sap3, 90);
    Gone(sap3);
    Unit runner = null; float runFrom = 0;
    if (laid3 != null)
    {
        runner = simX.Spawn(Kinds.Swordsman, simX.NearestStand((int)laid3.X - 44, (int)laid3.Z + 24, 2));
        runFrom = Apart(runner.X, runner.Z, laid3.X, laid3.Z);
    }
    blasts.Clear();
    bool ranFor = false;
    for (int t = 0; t < 20; t++) { RunX(.5); ranFor |= runner != null && runner.Defusing == laid3; }
    int kept3 = laid3 == null ? 0 : CountX((int)laid3.X - 8, (int)laid3.Z - 8, (int)laid3.X + 8, (int)laid3.Z + 8, m => m == Mat.Powder);
    Check("the nearest free fighter runs to a sapper's burning fuse by itself and cuts it in time",
        laid3 != null && ranFor && !simX.Charges.Contains(laid3) && blasts.Count == 0 && kept3 >= 12,
        $"{(laid3 != null ? $"laid; a swordsman {runFrom:0.0} m off ran for it: {ranFor}" : "not laid")}; {blasts.Count} blasts; {kept3} voxels of powder left");
    if (runner != null) Gone(runner);

    // a sapper with a charge before the ring lays it against the wall instead of hacking through, lights it and runs; the wall goes;
    // nobody of ours can get to the fuse in time, and the workers by the wall walk off before it goes
    int ring0 = RingStone();
    var sap = simX.Spawn(Kinds.Sapper, simX.NearestStand(kxX - ringR - 60, kzX - 60, 2));
    sap.Charge = true;
    var laidC = LaidBy(sap, 90);
    bool fled = sap.Routed && !sap.Charge;
    // the sapper is off and away: it strikes nobody on its way out
    Gone(sap);
    var byWall = new System.Collections.Generic.List<Unit>();
    if (laidC != null)
        foreach (var (dx, dz) in new[] { (-20, 0), (-16, 18), (-14, -18) })
        {
            var wk = simX.Spawn(Kinds.Worker, simX.NearestStand((int)laidC.X + dx, (int)laidC.Z + dz, 2));
            wk.Hold = true; byWall.Add(wk);
        }
    float toRing = laidC == null ? 0 : MathF.Abs(MathF.Sqrt((laidC.X - kxX) * (laidC.X - kxX) + (laidC.Z - kzX) * (laidC.Z - kzX)) - ringR) / VoxelWorld.PerMetre;
    blasts.Clear();
    // the nearest of them as it goes up
    float nearest = float.MaxValue;
    for (int t = 0; t < 160 && blasts.Count == 0; t++) RunX(.05);
    foreach (var wk in byWall) nearest = MathF.Min(nearest, Apart(wk.X, wk.Z, laidC?.X ?? 0, laidC?.Z ?? 0));
    RunX(2);
    int ring1 = RingStone();
    Check("a sapper with a charge lays it against our wall instead of hacking through, lights it and runs; the wall is breached",
        laidC != null && toRing < 2 && fled && blasts.Count >= 1 && ring0 - ring1 > 300,
        $"{(laidC != null ? $"laid {toRing:0.0} m from the ring's line" : "not laid")}, the sapper {(fled ? "running" : sap.State.ToString())}; {blasts.Count} blasts; masonry {ring0} → {ring1}");
    Check("with nobody able to get to the fuse in time, ours by the wall walk off and the blast does not touch them",
        byWall.Count == 3 && byWall.TrueForAll(wk => !wk.Dead && wk.Hp >= wk.MaxHp) && nearest >= 12,
        $"{byWall.Count} workers 2–3 m from the charge; when it went the nearest was {nearest:0.0} m off; {byWall.FindAll(wk => !wk.Dead && wk.Hp >= wk.MaxHp).Count} unhurt after it" + string.Join("", byWall.ConvertAll(wk => wk.Hp < wk.MaxHp ? $"; one {wk.Hp:0} hp at {Apart(wk.X, wk.Z, laidC?.X ?? 0, laidC?.Z ?? 0):0.0} m, hurt by {wk.HurtBy?.T.Name ?? "nobody"}, wounds: {string.Join(",", wk.Wounds?.Wounds ?? new System.Collections.Generic.List<string>())}" : "")));
    foreach (var wk in byWall) Gone(wk);
    Gone(sap);

    // a sapper cut down while burning goes up with its charge where it falls; one cut down cold leaves its kegs lying
    var hot = Camp(Kinds.Sapper, kxX - 380, kzX - 60);
    var cold = Camp(Kinds.Sapper, kxX - 380, kzX + 60);
    hot.Charge = cold.Charge = true;
    hot.Burn = 5; hot.BurnHeat = 1;
    blasts.Clear();
    simX.Hurt(hot, null, 1000, "огонь");
    simX.Hurt(cold, null, 1000, "стрела");
    RunX(1.5);
    int coldKegs = CountX((int)cold.X - 6, (int)cold.Z - 6, (int)cold.X + 6, (int)cold.Z + 6, m => m == Mat.Powder);
    bool hotWent = blasts.Exists(b => Apart(b.x, b.z, hot.X, hot.Z) < 3);
    Check("a sapper cut down burning goes up with its charge where it falls; one cut down cold leaves its kegs lying",
        hotWent && blasts.TrueForAll(b => Apart(b.x, b.z, cold.X, cold.Z) > 6) && coldKegs >= 9,
        $"the burning one {(hotWent ? "went up" : "did not")}; the cold one left {coldKegs} voxels of powder");

    // a catapult's stone is a ball of rock in flight, high on its arc; it breaks the masonry it strikes and lies there broken
    // clear of the long wall's end, so nothing else of ours stands on the stone's way
    int qx = kxX - siteX.KeepR - 40, qz = kzX - 360;
    int qtop = Shapes.LevelTop(wX, qx, qz, 32);
    simX.InstantLabour = true;
    simX.DesignateBuild(Shapes.Line(wX, siteX, qx, qz - 40, qx, qz + 40, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Level = true }, qtop));
    simX.InstantLabour = false;
    int q0 = CountX(qx - 6, qz - 44, qx + 6, qz + 44, Mat.IsMasonry);
    // thirty metres out, throwing at the wall's face before it
    var cat = simX.Spawn(Kinds.Catapult, simX.NearestStand(qx - 240, qz, 3));
    int face = qx - 8; while (face < qx + 8 && !Mat.IsMasonry(wX.Get(face, qtop - 6, qz))) face++;
    cat.AimAt = WaterPlanner.Pack(face, qtop - 6, qz); cat.NextLook = 1e9;
    Flier stone = null; float apex = 0, from = 0;
    for (int t = 0; t < 600 && stone == null; t++) { RunX(.1); stone = simX.Fliers.Find(f => f.Smash > 0); }
    if (stone != null) from = stone.Y;
    for (int t = 0; t < 100 && stone != null && simX.Fliers.Contains(stone); t++) { RunX(.05); apex = MathF.Max(apex, stone.Y); }
    int q1 = CountX(qx - 6, qz - 44, qx + 6, qz + 44, Mat.IsMasonry);
    float landX = stone?.X ?? 0;
    Check("a catapult's stone flies as a ball of rock high on its arc, breaks the masonry it strikes and lies there",
        stone != null && stone.Voxels.Count > 40 && apex - from > 24 && q0 - q1 > 40 && MathF.Abs(landX - qx) < 4 * VoxelWorld.PerMetre,
        $"{(stone != null ? $"{stone.Voxels.Count} voxels of rock, {(apex - from) / VoxelWorld.PerMetre:0.0} m over where it was let go, down {MathF.Abs(landX - qx) / VoxelWorld.PerMetre:0.0} m from the wall" : "no stone")}; masonry {q0} → {q1}");
    Gone(cat);
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--shaman")
{
    Console.WriteLine();
    var mpH = new MapParams();
    var wH = new VoxelWorld(mpH.SizeX, mpH.SizeY, mpH.SizeZ);
    var siteH = MapGen.Generate(wH, mpH);
    wH.Compact();
    var simH = new Sim(wH, siteH);
    simH.Waves.Countdown = 1e9; simH.WildTarget = 0;
    void RunH(double seconds) { for (double t = 0; t < seconds; t += .05) simH.Step(.05f); }
    int kxH = siteH.KeepX, kzH = siteH.KeepZ;
    Unit Camp(UnitType t, int x, int z) { var u = simH.Spawn(t, simH.NearestStand(x, z, t.Move.Height)); u.Guard = true; u.Home = u.Cell; return u; }
    void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }

    // a shaman mends those of its band hurt about it
    var shamanH = Camp(Kinds.Shaman, kxH - 300, kzH + 200);
    var hurtH = Camp(Kinds.Goblin, kxH - 290, kzH + 200);
    hurtH.Hp = 12;
    RunH(10);
    Check("a shaman mends the hurt of the horde about it", hurtH.Hp >= 12 + 24, $"the goblin 12 → {hurtH.Hp:0} hp in 10 s");
    Gone(shamanH); Gone(hurtH);

    // a shaman lays a curse on the nearest piece of our walls: the horde hacks it twice as fast; by a sanctuary it does not take
    Stroke wallH = new() { Mat = Mat.Stone, Width = 8, Height = 24, Level = true };
    int w1x = kxH - 240, w1z = kzH - 260, w2x = kxH - 240, w2z = kzH + 120;
    simH.InstantLabour = true;
    simH.DesignateBuild(Shapes.Line(wH, siteH, w1x, w1z, w1x, w1z + 48, wallH, Shapes.LevelTop(wH, w1x, w1z + 24, 24)));
    simH.DesignateBuild(Shapes.Line(wH, siteH, w2x, w2z, w2x, w2z + 48, wallH, Shapes.LevelTop(wH, w2x, w2z + 24, 24)));
    simH.InstantLabour = false;
    simH.AddZone(w2x + 30, w2z, w2x + 70, w2z + 48, Zone.Sanctum, 1);
    var hex1 = Camp(Kinds.Shaman, w1x - 80, w1z + 24);
    var hex2 = Camp(Kinds.Shaman, w2x - 80, w2z + 24);
    RunH(30);
    var curseH = simH.Curses.Find(c => MathF.Abs(c.X - w1x) < 16 && c.Z > w1z - 8 && c.Z < w1z + 56);
    bool wardedH = !simH.Curses.Exists(c => MathF.Abs(c.X - w2x) < 16 && c.Z > w2z - 8 && c.Z < w2z + 56);
    float hackIn = curseH == null ? 0 : simH.CurseHack((int)curseH.X, (int)curseH.Y, (int)curseH.Z), hackOut = simH.CurseHack(w1x, (int)(curseH?.Y ?? 0), w1z + 200);
    Check("a shaman curses the nearest piece of our walls, and the horde hacks it twice as fast; by a sanctuary the curse does not take",
        curseH != null && hackIn == 2 && hackOut == 1 && wardedH,
        $"curses {simH.Curses.Count}, on the open wall {(curseH != null ? $"at {curseH.X:0},{curseH.Z:0}" : "none")}, hacking there ×{hackIn}, elsewhere ×{hackOut}; the wall by the sanctuary {(wardedH ? "untouched" : "cursed")}");
    Gone(hex1); Gone(hex2);

    // our archers shoot a shaman before a goblin nearer to them
    // in the clearing round the citadel, nothing between them
    var bowH = simH.Spawn(Kinds.Archer, simH.NearestStand(kxH - 60, kzH + 100, 2));
    // both standing about, neither at one of ours (one cutting down one of ours would come first)
    var nearGob = simH.Spawn(Kinds.Goblin, simH.NearestStand((int)bowH.X + 56, (int)bowH.Z, 2));
    var farSham = simH.Spawn(Kinds.Shaman, simH.NearestStand((int)bowH.X + 120, (int)bowH.Z + 10, 2));
    RunH(1);
    Check("our archers shoot a shaman before a goblin nearer to them", bowH.Target == farSham,
        $"the archer aims at {bowH.Target?.T.Name ?? "nobody"}; the goblin {Sim.Dist(bowH, nearGob) / VoxelWorld.PerMetre:0.0} m off, the shaman {Sim.Dist(bowH, farSham) / VoxelWorld.PerMetre:0.0} m");
    Gone(bowH); Gone(nearGob); Gone(farSham);

    // a shaman comes with the fourth wave, not before
    simH.PlanWave(3);
    bool third = simH.Bands.Exists(b => b.Roster.Contains(Kinds.Shaman));
    simH.PlanWave(4);
    bool fourth = simH.Bands.Exists(b => b.Roster.Contains(Kinds.Shaman));
    Check("shamans come from the fourth wave", !third && fourth, $"in the third {third}, in the fourth {fourth}");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--sorcerers")
{
    Console.WriteLine();
    var mpV = new MapParams();
    var wV = new VoxelWorld(mpV.SizeX, mpV.SizeY, mpV.SizeZ);
    var siteV = MapGen.Generate(wV, mpV);
    wV.Compact();
    var simV = new Sim(wV, siteV);
    simV.Waves.Countdown = 1e9; simV.WildTarget = 0;
    void RunV(double seconds) { for (double t = 0; t < seconds; t += .05) simV.Step(.05f); }
    int kxV = siteV.KeepX, kzV = siteV.KeepZ;
    Unit Camp(UnitType t, int x, int z) { var u = simV.Spawn(t, simV.NearestStand(x, z, t.Move.Height)); u.Guard = true; u.Home = u.Cell; return u; }
    void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
    Stroke wallV = new() { Mat = Mat.Stone, Width = 8, Height = 32, Level = true };

    // a witch puts out the torches on our wall about her; by a sanctuary she cannot, and a minute on they are lit again
    int vx1 = kxV - 150, vz1 = kzV - 120, vx2 = kxV - 150, vz2 = kzV + 60;
    simV.InstantLabour = true;
    simV.DesignateBuild(Shapes.Line(wV, siteV, vx1, vz1, vx1, vz1 + 64, wallV, Shapes.LevelTop(wV, vx1, vz1 + 32, 32)));
    simV.DesignateBuild(Shapes.Line(wV, siteV, vx2, vz2, vx2, vz2 + 64, wallV, Shapes.LevelTop(wV, vx2, vz2 + 32, 32)));
    simV.InstantLabour = false;
    simV.Decorate();
    var torches1 = simV.Fixtures.FindAll(f => f.Kind == FixtureKind.Torch && MathF.Abs(f.LX - vx1) < 12 && f.LZ > vz1 - 4 && f.LZ < vz1 + 68);
    var torches2 = simV.Fixtures.FindAll(f => f.Kind == FixtureKind.Torch && MathF.Abs(f.LX - vx2) < 12 && f.LZ > vz2 - 4 && f.LZ < vz2 + 68);
    simV.AddZone(vx2 + 24, vz2, vx2 + 60, vz2 + 64, Zone.Sanctum, 1);
    var witch1 = Camp(Kinds.Witch, vx1 - 60, vz1 + 32);
    var witch2 = Camp(Kinds.Witch, vx2 - 60, vz2 + 32);
    RunV(12);
    bool flameGone(Fixture f) { foreach (var (x, y, z, m) in f.Voxels) if (m == Mat.Flame) return wV.Get(x, y, z) == Mat.Sconce; return false; }
    int dark1 = torches1.FindAll(f => f.DarkUntil > simV.Time && flameGone(f) && simV.Fixtures.Contains(f)).Count;
    int dark2 = torches2.FindAll(f => f.DarkUntil > simV.Time).Count;
    Gone(witch1); Gone(witch2);
    RunV(64);
    int relit = torches1.FindAll(f => f.DarkUntil <= simV.Time && !flameGone(f) && simV.Fixtures.Contains(f)).Count;
    Check("a witch puts out the torches on our wall about her, by a sanctuary she cannot, and a minute on they burn again",
        torches1.Count > 0 && torches2.Count > 0 && dark1 == torches1.Count && dark2 == 0 && relit == torches1.Count,
        $"torches on the open wall {torches1.Count}, put out {dark1}; by the sanctuary {torches2.Count}, put out {dark2}; lit again after a minute {relit}");

    // a witch puts out the fires about her, and the raiders burning there
    int bxV = kxV - 300, bzV = kzV + 200;
    for (int z = bzV; z < bzV + 16; z++) for (int x = bxV; x < bxV + 3; x++) { int g = wV.SurfAt(x, z); for (int y = g; y < g + 10; y++) wV.Set(x, y, z, Mat.Wood); }
    var witch3 = Camp(Kinds.Witch, bxV - 30, bzV + 8);
    var burnt = Camp(Kinds.Goblin, bxV - 22, bzV + 8);
    int litV = 0;
    for (int z = bzV; z < bzV + 16; z += 2) { int g = wV.SurfAt(bxV, z); if (simV.Ignite(bxV, g - 1, z)) litV++; }
    burnt.Burn = 8; burnt.BurnHeat = 1;
    RunV(4);
    int stillV = 0;
    foreach (int p in simV.Burning.Keys) { var (x, _, z) = WaterPlanner.Unpack(p); if (x >= bxV - 2 && x < bxV + 6 && z >= bzV - 2 && z < bzV + 18) stillV++; }
    Check("a witch puts out the fires about her and the raiders burning there", litV > 0 && stillV == 0 && burnt.Burn <= 0,
        $"{litV} voxels set alight, {stillV} still burning after 4 s; the goblin {(burnt.Burn > 0 ? $"burning {burnt.Burn:0.0} s more" : "put out")}");
    Gone(witch3); Gone(burnt);
    simV.Burning.Clear();

    // a necromancer gets the fallen about it up as bone thralls, three at most, and after their minute they fall apart
    int nxV = kxV - 330, nzV = kzV - 260;
    var necro = Camp(Kinds.Necromancer, nxV, nzV);
    for (int i = 0; i < 4; i++) { var d = Camp(Kinds.Goblin, nxV + 12 + i * 8, nzV + 6); simV.Hurt(d, null, 1000, "test"); }
    for (int t = 0; t < 30 && simV.Count(Kinds.Thrall) == 0; t++) RunV(.5);
    int thralls = simV.Count(Kinds.Thrall);
    var oneThrall = simV.Units.Find(u => !u.Dead && u.T == Kinds.Thrall);
    Gone(necro);
    RunV(62);
    Check("a necromancer gets the fallen about it up as bone thralls, three at most, and they fall apart after a minute",
        thralls == 3 && oneThrall?.Team == Team.Horde && simV.Count(Kinds.Thrall) == 0,
        $"{thralls} thralls got up of 4 fallen, {(oneThrall?.Team == Team.Horde ? "the horde's" : "not the horde's")}; {simV.Count(Kinds.Thrall)} still standing after a minute");

    // near a sanctuary of ours the intervention drains away while a necromancer stands there
    simV.AddZone(kxV - 200, kzV + 300, kxV - 160, kzV + 340, Zone.Sanctum, 1);
    var necro2 = Camp(Kinds.Necromancer, kxV - 240, kzV + 320);
    simV.Favour = 100;
    RunV(10);
    double drained = 100 - simV.Favour;
    Gone(necro2);
    Check("near a sanctuary of ours the intervention drains away while a necromancer stands there", drained >= 9 && drained <= 11, $"{drained:0.0} intervention drained in 10 s");

    // our archers shoot a witch or a necromancer before a goblin nearer to them
    var bowV = simV.Spawn(Kinds.Archer, simV.NearestStand(kxV - 60, kzV + 100, 2));
    var gobV = simV.Spawn(Kinds.Goblin, simV.NearestStand((int)bowV.X + 56, (int)bowV.Z, 2));
    var witchV = simV.Spawn(Kinds.Witch, simV.NearestStand((int)bowV.X + 120, (int)bowV.Z + 10, 2));
    RunV(1);
    Check("our archers shoot a witch before a goblin nearer to them", bowV.Target == witchV, $"the archer aims at {bowV.Target?.T.Name ?? "nobody"}");
    Gone(bowV); Gone(gobV); Gone(witchV);

    // witches come from the sixth wave, necromancers from the eighth
    bool W(int n, UnitType t) { simV.PlanWave(n); return simV.Bands.Exists(b => b.Roster.Contains(t)); }
    bool w5 = W(5, Kinds.Witch), w6 = W(6, Kinds.Witch), n7 = W(7, Kinds.Necromancer), n8 = W(8, Kinds.Necromancer);
    Check("witches come from the sixth wave, necromancers from the eighth", !w5 && w6 && !n7 && n8, $"a witch in the fifth {w5}, the sixth {w6}; a necromancer in the seventh {n7}, the eighth {n8}");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--lore")
{
    Console.WriteLine();
    var mpK = new MapParams();
    var wK = new VoxelWorld(mpK.SizeX, mpK.SizeY, mpK.SizeZ);
    var siteK = MapGen.Generate(wK, mpK);
    wK.Compact();
    var simK = new Sim(wK, siteK);
    simK.WildTarget = 0;
    void RunK(double seconds) { for (double t = 0; t < seconds; t += .05) simK.Step(.05f); }
    // a wave beaten back pays in gold and in knowledge
    simK.Waves.StartNow(simK);
    RunK(.5);
    simK.Waves._queue.Clear();
    foreach (var u in simK.Units) if (u.Team == Team.Horde && !u.Dead) { u.Dead = true; u.DiedAt = -999; }
    double goldK0 = simK.Stock.Gold;
    RunK(.5);
    Check("a wave beaten back brings gold and knowledge", !simK.Waves.Active && simK.Stock.Gold >= goldK0 + 40 && simK.Knowledge == 2,
        $"gold {goldK0:0} → {simK.Stock.Gold:0}, knowledge {simK.Knowledge}");

    // lore is learnt in the order of its branch, and only for the knowledge it costs
    bool barbsFirst = simK.Learn("barbs");
    var bowK = simK.Spawn(Kinds.Archer, simK.Gate);
    float reachK0 = simK.RangeAt(bowK, bowK.X + 50, bowK.Z);
    int costK0 = simK.HireCost(Kinds.Swordsman);
    bool yewK = simK.Learn("yew");
    float reachK1 = simK.RangeAt(bowK, bowK.X + 50, bowK.Z);
    bool barbsPoor = simK.Learn("barbs");
    simK.Knowledge = 6;
    bool drillFirst = simK.Learn("drill");
    bool coats = simK.Learn("gambesons"), drillK = simK.Learn("drill");
    Check("lore is learnt in the order of its branch, for the knowledge it costs",
        !barbsFirst && yewK && !barbsPoor && !drillFirst && coats && drillK && simK.Knowledge == 1 && simK.Learnt.Count == 3,
        $"barbs before yew {barbsFirst}, yew {yewK}, barbs with nothing left {barbsPoor}, drill before coats {drillFirst}, coats {coats}, drill {drillK}; {simK.Knowledge} left");

    // and does what it says: yew carries further, the drill yard makes hiring cheaper, quilted coats keep a soldier longer
    int costK1 = simK.HireCost(Kinds.Swordsman);
    simK.Stock.Gold = 1000;
    var swordK = simK.Hire(Kinds.Swordsman);
    Check("yew bows carry 15 % further, the drill yard takes a fifth off hiring, quilted coats give a fifth more life",
        MathF.Abs(reachK1 / reachK0 - 1.15f) < .01f && costK1 == (int)Math.Round(costK0 * .8) && swordK != null && 1000 - simK.Stock.Gold == costK1
        && MathF.Abs(swordK.MaxHp - Kinds.Swordsman.Hp * 1.2f) < .01f && swordK.Hp == swordK.MaxHp,
        $"reach {reachK0 / VoxelWorld.PerMetre:0.0} → {reachK1 / VoxelWorld.PerMetre:0.0} m; a swordsman {costK0} → {costK1} gold, paid {1000 - simK.Stock.Gold:0}; " +
        $"his life {swordK?.MaxHp:0} of {Kinds.Swordsman.Hp:0}");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--save")
{
    Console.WriteLine();
    const uint SeedS = 7;
    var mpS = new MapParams { Seed = SeedS };
    var wS = new VoxelWorld(mpS.SizeX, mpS.SizeY, mpS.SizeZ);
    var siteS = MapGen.Generate(wS, mpS);
    wS.Compact();
    var simS = new Sim(wS, siteS);
    simS.WildTarget = 0;
    void RunS(double seconds) { for (double t = 0; t < seconds; t += .05) simS.Step(.05f); }
    StartingFort.Raise(simS);
    foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Archer, Kinds.Swordsman }) simS.Spawn(t, simS.Gate);
    simS.SeedWildlife(3);
    simS.SeedMines();
    int kxS = siteS.KeepX, kzS = siteS.KeepZ;
    // a wall marked, a plot set aside, something alight, one of ours cut, the horde in the valley
    simS.DesignateBuild(Shapes.Line(wS, siteS, kxS - 140, kzS - 40, kxS - 140, kzS + 40, new Stroke { Mat = Mat.Stone, Width = 8, Height = 16, Level = true }, Shapes.LevelTop(wS, kxS - 140, kzS, 16)));
    simS.AddZone(kxS - 260, kzS + 120, kxS - 200, kzS + 180, Stockpile.Wood, 0);
    var sore = simS.Units.Find(u => u.T == Kinds.Swordsman);
    var (cut, depth, reach) = Sim.WeaponOf(Kinds.Orc);
    float share = simS.Wound(sore, cut, depth, reach, sore.X + 10, sore.Y + 10, sore.Z, -1, 0, 0);
    simS.Hurt(sore, null, 30 * share);
    RunS(20);
    simS.Waves.StartNow(simS);
    RunS(25);
    // a post of logs set alight
    int px = kxS - 170, pz = kzS + 60, pg = wS.SurfAt(px, pz);
    for (int y = pg; y < pg + 12; y++) for (int z = pz; z < pz + 2; z++) for (int x = px; x < px + 2; x++) wS.Set(x, y, z, Mat.Wood);
    simS.Ignite(px, pg + 4, pz);
    RunS(1);
    // and something learnt, and given
    simS.Knowledge = 4;
    simS.Learn("mortar");
    simS.Revelations = 2; simS.Carcasses.Add(Kinds.Boar); simS.Favour = 17;
    simS.Known.Add(SpellKind.Hand); simS.Known.Add(SpellKind.Eye);
    var ms = new System.IO.MemoryStream();
    var watch = System.Diagnostics.Stopwatch.StartNew();
    simS.Save(ms, SeedS, "Волна 1", "the screen's own");
    long saveMs = watch.ElapsedMilliseconds;
    long bytes = ms.Length;
    ms.Position = 0;
    watch.Restart();
    var wL = new VoxelWorld(mpS.SizeX, mpS.SizeY, mpS.SizeZ);
    Sim simL;
    string extra;
    using (var reader = new SaveReader(ms))
    {
        var siteL = MapGen.Generate(wL, new MapParams { Seed = reader.Seed });
        wL.Compact();
        reader.Valley(wL, siteL);
        simL = new Sim(wL, siteL);
        extra = simL.Load(reader);
    }
    long loadMs = watch.ElapsedMilliseconds;
    int chunksOff = 0;
    for (int ci = 0; ci < wS.ChunkCount; ci++)
    {
        var a = wS.ChunkData(ci); var b = wL.ChunkData(ci);
        bool same = a == null ? b == null && wS.ChunkUniform(ci) == wL.ChunkUniform(ci) : b != null && a.AsSpan().SequenceEqual(b);
        if (!same) chunksOff++;
    }
    int designOff = 0;
    for (int ci = 0; ci < wS.ChunkCount; ci++)
    {
        var a = simS.Design.ChunkData(ci); var b = simL.Design.ChunkData(ci);
        if (!(a == null ? b == null || Array.TrueForAll(b, v => v == 0) : b != null && a.AsSpan().SequenceEqual(b))) designOff++;
    }
    var livingS = simS.Units.FindAll(u => !u.Dead);
    int unitsOff = 0;
    foreach (var u in livingS)
    {
        var v = simL.Units.Find(o => o.Id == u.Id);
        if (v == null || v.T != u.T || v.Look != u.Look || MathF.Abs(v.X - u.X) > .01f || MathF.Abs(v.Hp - u.Hp) > .01f || (u.Wounds == null) != (v.Wounds == null)
            || u.Wounds != null && (u.Wounds.Wounds.Count != v.Wounds.Wounds.Count || MathF.Abs(u.Wounds.Bleed - v.Wounds.Bleed) > .001f || u.Wounds.Grid[0]?.Count != v.Wounds.Grid[0]?.Count)
            || (u.Band == null) != (v.Band == null) || u.Leader != v.Leader) unitsOff++;
    }
    bool sameSiege = simL.Time == simS.Time && simL.Orders.Total == simS.Orders.Total && simL.Orders.BuildTotal == simS.Orders.BuildTotal
        && simL.Stock.Gold == simS.Stock.Gold && simL.Stock[Stockpile.Stone] == simS.Stock[Stockpile.Stone] && simL.Zones.Count == simS.Zones.Count
        && simL.Gates.Count == simS.Gates.Count && simL.Fixtures.Count == simS.Fixtures.Count && simL.Burning.Count == simS.Burning.Count
        && simL.Bands.Count == simS.Bands.Count && simL.Waves.Number == simS.Waves.Number && simL.Waves.Active == simS.Waves.Active
        && simL.Waves.Coming == simS.Waves.Coming && simL.Mines.TrueForAll(m => m.Holder == simS.Mines[simL.Mines.IndexOf(m)].Holder) && extra == "the screen's own"
        && simL.Knowledge == 2 && simL.Knows("mortar") && simL.Learnt.Count == 1
        && simL.Revelations == 2 && simL.Carcasses.Count == 1 && simL.Carcasses[0] == Kinds.Boar && Math.Abs(simL.Favour - simS.Favour) < 1e-9
        && simL.Known.SetEquals(simS.Known);
    Check("a game saved and read back is the same: the valley as play changed it, the household and the horde, the siege",
        chunksOff == 0 && designOff == 0 && unitsOff == 0 && livingS.Count == simL.Units.Count && sameSiege && simS.Burning.Count > 0 && simS.Bands.Count > 0,
        $"{bytes / 1024} KB, written in {saveMs} ms, read in {loadMs} ms (the valley made again); {chunksOff} chunks and {designOff} of the design differ; " +
        $"{livingS.Count} living, {unitsOff} differ; {simS.Bands.Count} bands, {simS.Burning.Count} burning, wave {simS.Waves.Number} ({simS.Waves.Coming} still to come); same siege {sameSiege}");
    // and it plays on: the horde still comes on, the fire still burns
    simL.ComputeFields();
    int burning0 = simL.Burning.Count;
    float far0 = simL.Units.FindAll(u => !u.Dead && u.Team == Team.Horde && !u.Guard).ConvertAll(u => MathF.Abs(u.X - kxS) + MathF.Abs(u.Z - kzS)).DefaultIfEmpty(0).Average();
    for (double t = 0; t < 30; t += .05) simL.Step(.05f);
    float far1 = simL.Units.FindAll(u => !u.Dead && u.Team == Team.Horde && !u.Guard).ConvertAll(u => MathF.Abs(u.X - kxS) + MathF.Abs(u.Z - kzS)).DefaultIfEmpty(0).Average();
    Check("…and the game read back plays on: the horde comes on, the fire burns on", burning0 > 0 && simL.Burning.Count != burning0 && (far1 < far0 || simL.Kills > simS.Kills),
        $"burning {burning0} → {simL.Burning.Count}; the horde on average {far0 / VoxelWorld.PerMetre:0} → {far1 / VoxelWorld.PerMetre:0} m from the citadel, {simL.Kills - simS.Kills} more killed");
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
