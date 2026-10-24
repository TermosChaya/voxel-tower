using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
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
// the prototype had 232642; its citadel of neat brick is rough masonry now (234006), its tufts of grass are drawn in
// blades by the game instead of meshed as cubes (145454), and the earth in its banks under turf is grassed over (145640)
Check("triangles as counted", quads * 2 == 145640, $"{quads * 2} triangles in {meshed} chunks, 145640 expected (the prototype's 232642 with a brick citadel and its tufts as cubes)");
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
    // nobody stands up a tree: no cell to stand in rests on a trunk or a bough more than a metre over the ground beneath it,
    // and an order to a tree's foot (the ground under its trunk, where a click through the trunk lands) keeps to the ground
    {
        var nav = sim.Nav;
        // how high over the ground beneath it the wood is that the feet in a cell rest on (0: not on wood)
        int OnWood(int c)
        {
            int i = nav.I(c), k = nav.K(c), f = nav.Floor[c], most = 0;
            for (int q = 0; q < 4; q++)
            {
                int x = (i << NavGrid.Shift) + 3 + (q & 1), z = (k << NavGrid.Shift) + 3 + (q >> 1), g = f - 1;
                if (g < 0 || !Mat.IsTrunk(w4.Get(x, g, z))) continue;
                while (g >= 0 && (w4.Get(x, g, z) == Mat.Air || Mat.Passable[w4.Get(x, g, z)] || Mat.IsTrunk(w4.Get(x, g, z)))) g--;
                most = Math.Max(most, f - g - 1);
            }
            return most;
        }
        int stands = 0, up = 0, feet = 0, sent = 0, away = 0;
        string first = "", firstSent = "", firstAway = "";
        for (int c = 0; c < nav.Count; c++)
        {
            if (!nav.Stand(nav.I(c), nav.Y(c), nav.K(c), 2)) continue;
            stands++;
            if (OnWood(c) > NavGrid.CS && up++ == 0) first = $", the first at cell {nav.I(c)},{nav.Y(c)},{nav.K(c)} (feet {nav.Floor[c]})";
        }
        var clicked = new System.Collections.Generic.HashSet<int>();
        for (int z = 1; z < w4.SZ - 1; z += 2)
            for (int x = 1; x < w4.SX - 1; x += 2)
            {
                // a trunk standing on the ground here, once a cell
                int g = w4.SurfAt(x, z), i = x >> NavGrid.Shift, k = z >> NavGrid.Shift;
                if (!Mat.IsTrunk(w4.Get(x, g, z)) || !Mat.IsTrunk(w4.Get(x, g + 16, z)) || !clicked.Add(k * nav.NX + i)) continue;
                feet++;
                // as the commander's hand picks a cell to stand in there, from a click that went through the trunk to the
                // ground: on the ground beside the trunk, a few metres off at most and about as high
                int cell = sim.StandNear(x, g, z, 2);
                if (cell >= 0 && OnWood(cell) > NavGrid.CS && sent++ == 0) firstSent = $", the first from {x},{z} to feet {nav.Floor[cell]} over ground {g}";
                var (fx, fy, fz) = cell >= 0 ? nav.Feet(cell) : (0f, 0f, 0f);
                if ((cell < 0 || Math.Abs(fy - g) > 12 || Math.Max(Math.Abs(fx - x), Math.Abs(fz - z)) > 28) && away++ == 0)
                    firstAway = $", the first from {x},{z} (ground {g}) to {(cell < 0 ? "nowhere" : $"{fx:0},{fy:0},{fz:0}")}";
            }
        Check("nobody stands up a tree, nor is sent up one", up == 0 && sent == 0 && away == 0 && feet > 150,
            $"{stands} cells to stand in, {up} of them on wood over a metre up{first}; {feet} trees' feet clicked, {sent} sent up the tree{firstSent}, {away} not beside it{firstAway}");
    }

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
        sim.Command(new[] { crew[0], crew[1] }, spot, stay: true);
        sim.Command(new[] { crew[2] }, spot, stay: true);
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

        // a worker sent with a right-click goes and then works by itself: into a grove, the grove's work first (another
        // grove than the one felled above)
        (int x, int z) wood2 = (-1, -1); int most2 = 0;
        for (int cz = 80; cz < w4.SZ - 80; cz += 40)
            for (int cx = 80; cx < w4.SX - 80; cx += 40)
            {
                float dk = MathF.Sqrt((cx - site4.KeepX) * (cx - site4.KeepX) + (cz - site4.KeepZ) * (cz - site4.KeepZ));
                if (dk < 150 || dk > 460 || w4.SurfAt(cx, cz) < 70 || Math.Abs(cx - wood.x) < 2 * G + 30 && Math.Abs(cz - wood.z) < 2 * G + 30) continue;
                int t = sim.Survey(cx - G, cz - G, cx + G, cz + G, Stockpile.Wood, 0).trees;
                if (t > most2) { most2 = t; wood2 = (cx, cz); }
            }
        var grove2 = sim.AddZone(wood2.x - G, wood2.z - G, wood2.x + G, wood2.z + G, Stockpile.Wood, 0);
        var sent = crew[0];
        sim.Command(new[] { sent }, sim.NearestStand(wood2.x, wood2.z, sent.T.Move.Height), stay: false);
        bool heldOnTheWay = sent.Hold, inGrove = false;
        // (the work is quick: the grove may be all felled before the minute is out)
        for (int t = 0; t < 60; t++)
        {
            Run(1);
            int sc = sent.Site;
            if (!sent.Hold && (sent.Focus == grove2 || sc >= 0 && Math.Abs((sc % sim.Nav.NX << NavGrid.Shift) + 4 - wood2.x) <= G + 8 && Math.Abs((sc / sim.Nav.NX << NavGrid.Shift) + 4 - wood2.z) <= G + 8)) inGrove = true;
        }
        Check("a worker sent into a grove goes there and fells it by itself, no «За работу» needed",
            heldOnTheWay && !sent.Hold && inGrove,
            $"held on the way {heldOnTheWay}, held now {sent.Hold}, at work in the grove {inGrove}, focus {(sent.Focus == grove2 ? "the grove" : "none")}; {sent.Why}");
        sim.RemoveZones(wood2.x - G, wood2.z - G, wood2.x + G, wood2.z + G);
        sim.CancelArea(wood2.x - 70, wood2.z - 70, wood2.x + 70, wood2.z + 70);
        sim.ReleaseHold(crew);


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
        if (Environment.GetEnvironmentVariable("CUT_WHY") == "1")
        {
            // what was marked before the cut, and where
            foreach (int col in sim.Orders.Active)
            {
                if (!sim.Orders.Span(col, out int sy0, out int sy1)) continue;
                int cx = col % sim.Nav.NX << NavGrid.Shift, cz = col / sim.Nav.NX << NavGrid.Shift;
                for (int y = sy0; y <= sy1; y++) for (int z = cz; z < cz + NavGrid.CS; z++) for (int x = cx; x < cx + NavGrid.CS; x++)
                {
                    byte o = sim.Orders.At(x, y, z);
                    if (o == 0) continue;
                    int up = 0, down = 0;
                    while (up < 200 && Mat.IsTrunk(w4.Get(x, y + up + 1, z))) up++;
                    while (down < 200 && Mat.IsTrunk(w4.Get(x, y - down - 1, z))) down++;
                    Console.WriteLine($"     marked {o} at {x},{y},{z} ({y - w4.SurfAt(x, z)} from the top), is {Mat.NameOf(w4.Get(x, y, z))}, under it {Mat.NameOf(w4.Get(x, y - 1, z))}; wood above {up}, below {down}, ground at {w4.GroundTop(x, z)}");
                }
            }
        }
        if (Environment.GetEnvironmentVariable("CUT_WHY") == "1")
            for (int t = 0; t < 6; t++)
            {
                for (int q = 0; q < 10; q++)
                {
                    foreach (var u in crew) Console.WriteLine($"     {t * 10 + q} s: {u.T.Name} #{u.Id} {u.State} at {u.X:0},{u.Z:0} ({MathF.Sqrt((u.X - ex - 8) * (u.X - ex - 8) + (u.Z - ez - 8) * (u.Z - ez - 8)) / 8:0} m off) {Retinue0(u)}, site {(u.Site >= 0 ? $"{u.Site % sim.Nav.NX * 8},{u.Site / sim.Nav.NX * 8}" : "-")}, orders {sim.Orders.Total}");
                    Run(1);
                }
            }
        else Run(60);
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
        sim.Command(new[] { porter }, inside, stay: true);
        float widest = 0;
        for (int t = 0; t < 40 * 20 && porter.Cell != inside; t++) { sim.Step(.05f); widest = Math.Max(widest, gate?.Open ?? 0); }
        bool got = porter.Cell == inside;
        Run(6);
        Check("a gate opens for our worker and shuts behind him", gate != null && shutAtFirst && got && widest >= .99f && gate.Open == 0,
            gate == null ? "no gate fits the wall" : $"leaves on the {(gate.In > 0 ? "south (outer)" : "north")} side, opened to {widest:0.00}, worker in: {got}, now {gate.Open:0.00}");

        sim.SetGateMode(gate, GateMode.Shut);
        Run(1);
        int barred = sim.Command(new[] { porter }, outside, stay: true);
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
        // a deer runs from whoever hunts it: an archer sent after one keeps it in range as it runs, and brings it down
        var stag = sim.Spawn(Kinds.Deer, sim.NearestStand((int)mx - 60, (int)mz + 140, 2));
        var bowman = sim.Spawn(Kinds.Archer, sim.NearestStand((int)stag.X - 220, (int)stag.Z, 4));
        int shots0 = sim.Shots;
        sim.Hunt(new[] { bowman }, stag);
        double hunted = 0;
        // (a minute and a half: a deer that runs well can take the tenth arrow)
        for (; hunted < 90 && !stag.Dead; hunted += .5) Run(.5);
        Check("an archer sent after a running deer keeps it in range and brings it down", stag.Dead,
            $"the deer {(stag.Dead ? $"taken in {hunted:0} s" : $"{stag.Hp:0} hp after a minute and a half")}, {sim.Shots - shots0} shots");
        bowman.Dead = true; bowman.DiedAt = -999; stag.Dead = true; stag.DiedAt = -999;
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
        // out of whatever pit the duties left him in, onto open ground before the citadel, and called up there
        sim.Command(new[] { guardsman }, sim.NearestStand(site4.KeepX - site4.KeepR - 60, site4.KeepZ + 30, 2));
        Run(40);
        sim.SetMilitia(new[] { guardsman }, true);
        var gob = sim.Spawn(Kinds.Goblin, sim.NearestStand((int)guardsman.X + 40, (int)guardsman.Z, 2));
        gob.Raid = null;
        bool fought = false;
        for (int t = 0; t < 20 * 20 && !gob.Dead; t++)
        {
            sim.Step(.05f); fought |= guardsman.State == UnitState.Fight;
            if (t % 40 == 0 && Environment.GetEnvironmentVariable("LIFE_WHY") == "1")
                Console.WriteLine($"     {t / 20} s: the worker {guardsman.State} at {guardsman.X:0},{guardsman.Y:0},{guardsman.Z:0} «{Retinue0(guardsman)}»; the goblin {gob.State} at {gob.X:0},{gob.Y:0},{gob.Z:0} «{Retinue0(gob)}»");
        }
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
        // something else is not the hand's (a big tree, still being felled eight seconds on)
        (int x, int z) tree = (-1, -1);
        for (int r = 60; r < 500 && tree.x < 0; r += 8)
            for (int a = 0; a < 360 && tree.x < 0; a += 4)
            {
                int x = site4.KeepX + (int)(Math.Cos(a * Math.PI / 180) * r), z = site4.KeepZ + (int)(Math.Sin(a * Math.PI / 180) * r);
                if (x < 40 || z < 40 || x >= w4.SX - 40 || z >= w4.SZ - 40) continue;
                int y = w4.SurfAt(x, z) + 2;
                if (Mat.IsTrunk(w4.Get(x, y, z)) && Shapes.Tree(w4, x, y, z).Count > 6000) tree = (x, z);
            }
        var woodman = sim.Spawn(Kinds.Worker, sim.NearestStand(tree.x + 30, tree.z, 4));
        sim.BeginMarking();
        var plot = sim.AddZone(tree.x - 20, tree.z - 20, tree.x + 20, tree.z + 20, Stockpile.Wood, 0);
        sim.EndMarking("Делянка");
        Run(4);
        sim.BeginMarking();
        int heap = sim.DesignateBuild(Shapes.Line(w4, site4, ux, uz - 6, ux, uz + 6, earthWall, Shapes.LevelTop(w4, ux, uz, 12)));
        Run(4);
        sim.EndMarking("Насыпь");
        int fed = plot.Marked;
        var un6 = sim.UndoMarking(out _);
        // (the tree is cut at its foot and may be down by now, its log lying out of the plot along the ground: what is
        // felled already and lying is still marked to be cut up after the plot goes; what stands in the plot is not)
        int felling = Left(tree.x - 100, tree.z - 100, tree.x + 100, tree.z + 100, WorkKind.Fell) + Left(tree.x - 100, tree.z - 100, tree.x + 100, tree.z + 100, WorkKind.Dig);
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
    // (well within the sixteen metres it sees a fight from: the fighters step about as they fight)
    var helper = simW.Spawn(Kinds.Goblin, simW.NearestStand((int)guard.X, (int)guard.Z + 100, 2));
    float hx0 = helper.X, hz0 = helper.Z, toGuardX = guard.X - hx0, toGuardZ = guard.Z - hz0, toGuard = MathF.Sqrt(toGuardX * toGuardX + toGuardZ * toGuardZ);
    RunW(2.5);
    float came = ((helper.X - hx0) * toGuardX + (helper.Z - hz0) * toGuardZ) / toGuard;
    // (the first may well be knocked off its feet at the first blow, and get up only to run)
    Check("raiders see one of their own fighting and come to help it", biter.State == UnitState.Fight || biter.Dead || biter.Routed ? came > 32 : false,
        $"{toGuard / VoxelWorld.PerMetre:0.0} m off, came {came / VoxelWorld.PerMetre:0.0} m toward the fight, {helper.State}; the first {biter.State}");
    Gone(biter); Gone(helper); Gone(guard);

    // a hard blow sends a goblin reeling, no blow of its own the while; hurt past bearing, whole as it is, it breaks
    var hitter = simW.Spawn(Kinds.Swordsman, simW.NearestStand(kxW - 300, kzW - 200, 4));
    simW.Command(new[] { hitter }, hitter.Cell);
    var reeler = simW.Spawn(Kinds.Goblin, simW.NearestStand(kxW - 300, kzW - 60, 2));
    simW.Hurt(reeler, hitter, reeler.MaxHp * .3f, "test");
    bool reeled = reeler.StaggerUntil > simW.Time && reeler.Cooldown > .25f && !reeler.Routed && !reeler.Down;
    float painOne = reeler.Pain;
    simW.Hurt(reeler, hitter, reeler.MaxHp * .15f, "test");
    RunW(.1);
    Check("a hard blow sends a goblin reeling; hurt past bearing, whole as it is, it breaks and runs", reeled && painOne > .3f && reeler.Routed && reeler.Hp > reeler.MaxHp * .25f,
        $"reeling {reeled}, pain {painOne:0.00} after the first; after the second {reeler.Pain:0.00}, running {reeler.Routed} with {reeler.Hp:0} of {reeler.MaxHp:0} hp");
    Gone(reeler);

    // a blow that would fell an ox knocks one off its feet a while; it gets up, and runs
    var felledW = simW.Spawn(Kinds.Goblin, simW.NearestStand(kxW - 300, kzW - 60, 2));
    simW.Hurt(felledW, hitter, felledW.MaxHp * .4f, "test");
    bool downW = felledW.Down;
    RunW(3);
    Check("a blow that would fell an ox knocks a goblin off its feet a while; it gets up and runs", downW && !felledW.Down && felledW.Routed,
        $"down at once {downW}; three seconds on down {felledW.Down}, running {felledW.Routed}");
    Gone(felledW);

    // a blow seen coming is now and then stepped out of the way of, taken on the shield or turned; from behind, never
    var hewer = simW.Spawn(Kinds.Orc, simW.NearestStand((int)hitter.X + 10, (int)hitter.Z, 2));
    hewer.Guard = true; hewer.Home = hewer.Cell;
    hewer.Hp = hewer.MaxHp = hitter.Hp = hitter.MaxHp = 100000;
    int Seen(bool behind)
    {
        int n = 0;
        for (int i = 0; i < 60; i++)
        {
            RunW(.05);
            hitter.State = UnitState.Fight; hitter.Target = hewer; hitter.Cooldown = 0; hitter.Pain = 0; hitter.StaggerUntil = hitter.DazedUntil = 0;
            float to = MathF.Atan2(hewer.Z - hitter.Z, hewer.X - hitter.X);
            hitter.Facing = behind ? to + MathF.PI : to;
            double at = hitter.ReactAt;
            simW.Strike(hewer, hitter);
            if (hitter.ReactAt != at) n++;
        }
        return n;
    }
    int seenW = Seen(false), behindW = Seen(true);
    Check("a blow seen coming is now and then got out of the way of or turned; one from behind never", seenW >= 6 && seenW <= 50 && behindW == 0,
        $"of 60 blows of an orc at a swordsman facing it {seenW} not taken; from behind {behindW}");
    Gone(hewer); Gone(hitter);

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
    // the dry part set alight in two places (a fire creeping along one course of logs from a single spark can all but die
    // down for minutes, as the dice fall), the pool in one
    bool farLit = simF.Ignite(sx0 + 1, sg + 2, sz0 + 5) && simF.Ignite(sx0 + 10, sg + 2, sz0 + 5), wetLit = simF.Ignite(sx0 + 22, sg + 2, sz0 + 5);
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

    // a stone tower two metres square and six high, cut through at its foot: it comes down whole and stands as it was,
    // only lower, and the goblin standing beside it is not touched
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
    int stood = CountC(Stony, tx0, tz0, tx0 + 15, tz0 + 15, tg, tg + 50), top = CountC(Stony, tx0, tz0, tx0 + 15, tz0 + 15, tg + 46, tg + 50);
    Check("a tower cut through at its foot comes down whole and stands in its shape, lower; the goblin beside it is not touched",
        stood >= (tower0 - 512) * 95 / 100 && top == 0 && spread < stood / 20 && collapses > 0 && !under.Dead && under.Hp >= under.MaxHp,
        $"{tower0 - 512} voxels of tower over the cut, {stood} standing in its footprint ({top} over its new top), {spread} spilled past its foot; the goblin {(under.Dead ? "dead, " + under.KilledBy : $"{under.Hp:0} hp")}");
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
    // what fell lies down before the mending: blocks still in the air would come down on the new masonry
    RunC(4);
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

// ---- weight and cracks, and buildings going over whole
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--collapse" || args[0] == "--strain") StrainBench.Run(Check);

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
    // just where the stones come down, not wherever in its cell the dice put it
    footGob.X = edge - 4.5f; footGob.OffX = footGob.X - simT.Nav.Feet(footGob.Cell).x;
    float fhp0 = footGob.Hp;
    float onWall = guardT.Y - wallTop;
    RunT(3);
    int dropped1 = Count(Mat.DropStone, wx0 - 4, wz0 + 28, wx0 + 12, wz0 + 52);
    Check("stones on a wall are tipped onto a goblin at its foot by an archer of ours beside them",
        dropped0 >= 100 && Math.Abs(onWall) < 4 && dropped1 < dropped0 && (footGob.Dead || fhp0 - footGob.Hp >= 25),
        $"{dropped0} stones on the wall, the archer {onWall:0} voxels from its top; {dropped1} left; the goblin {fhp0:0} → {(footGob.Dead ? "dead" : $"{footGob.Hp:0} hp")}");

    // logs laid along the same wall's edge a little farther on: the archer rolls them down onto a goblin at its foot, and
    // they roll on across themselves
    for (int z = wz0 + 58; z < wz0 + 74; z++)
        for (int y = wallTop; y < wallTop + 2; y++) { wT.Set(edge, y, z, Mat.DropLog); wT.Set(edge + 1, y, z, Mat.DropLog); }
    simT.Nav.Flush();
    int logsUp0 = Count(Mat.DropLog, wx0 - 4, wz0 + 56, wx0 + 12, wz0 + 76);
    var logGob = simT.Spawn(Kinds.Goblin, simT.NearestStand(edge - 5, wz0 + 66, 2));
    logGob.Raid = null; logGob.Guard = true; logGob.Home = logGob.Cell;
    float lhp0 = logGob.Hp;
    bool logsRolled = false;
    for (int t = 0; t < 60; t++) { RunT(.05); logsRolled |= simT.Fliers.Exists(f => f.Cause == "брёвна со стены" && f.Rolling && f.RollH > 0); }
    RunT(3);
    int logsUp1 = Count(Mat.DropLog, wx0 - 4, wz0 + 56, wx0 + 12, wz0 + 76);
    Check("logs on a wall are rolled down onto a goblin at its foot by the archer beside them, and roll on across themselves",
        logsUp0 >= 60 && logsUp1 == 0 && logsRolled && (logGob.Dead || lhp0 - logGob.Hp >= 15),
        $"{logsUp0} voxels of logs on the wall, {logsUp1} left; rolled {logsRolled}; the goblin {lhp0:0} → {(logGob.Dead ? "dead" : $"{logGob.Hp:0} hp")}");
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
    if (Environment.GetEnvironmentVariable("NIGHT_WHY") != null)
        foreach (var o in simC.Units) if (!o.Dead && o.Team == Team.Player) Console.WriteLine($"  NIGHT_WHY {o.T.Name} #{o.Id} {o.X:0},{o.Z:0} inside {o.Inside} {o.State} militia {o.Militia}; waves {simC.Waves.Active}, unguarded {gob2C.Unguarded:0.0}, bound {gob2C.Bound}");
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
    float pa8 = sickA.Hp;
    // (it passes on by chance, a quarter of the time each second it is near: given a little longer, it reaches the third)
    for (int i = 0; i < 12 && sickC.PlagueUntil < 0; i++) RunP(1);
    Check("the plague sickens the raider it falls on, and passes on to those standing by it", castPlague == null && onlyA && pa0 - pa8 >= 12 && sickB.PlagueUntil > simP.Time - 1 && sickC.PlagueUntil > simP.Time - 1,
        $"cast: {castPlague ?? "yes"}; at first only the nearest: {onlyA}; it {pa0:0} → {pa8:0} in 8 s; the next {(sickB.PlagueUntil > 0 ? "sick" : "well")}, the one past it {(sickC.PlagueUntil > 0 ? "sick" : "well")}");
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

// what blunt things do to bodies (Trauma.cs)
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--trauma") TraumaBench.Run(Check);

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
    // (what the blast cracked about the hole goes on crumbling a while, and what it held comes down after)
    RunY(8);
    double settledE = 8;
    for (int t = 0; t < 40 && simY.Fliers.Count > 0; t++) { RunY(.5); settledE += .5; }
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
        castBlast == null && flying >= 10 && aloft > 3000 && simY.Fliers.Count == 0 && stoneE0 - stoneE1 > 6000 && rubble > 2000 && farthest > 6,
        $"cast: {castBlast ?? "yes"}; {flying} pieces, {aloft} voxels in the air, {simY.Fliers.Count} still flying after {settledE:0.0} s; masonry {stoneE0} → {stoneE1}; rubble {rubble}, the farthest {farthest:0.0} m off");
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
    // the earth there before, to tell the ball from it after
    var earthBefore = new System.Collections.Generic.HashSet<(int, int, int)>();
    for (int z = (int)gobB.Z - 24; z <= gobB.Z + 24; z++) for (int x = (int)gobB.X - 24; x <= gobB.X + 24; x++) for (int y = (int)gobB.Y - 2; y < gobB.Y + 30; y++) if (wY.Get(x, y, z) == Mat.Soil) earthBefore.Add((x, y, z));
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
    // and it lies as the ball it was, not spilled out flat: as high as it is wide, no wider than the ball
    var ballNow = new System.Collections.Generic.List<(int x, int y, int z)>();
    for (int z = (int)gobB.Z - 24; z <= gobB.Z + 24; z++) for (int x = (int)gobB.X - 24; x <= gobB.X + 24; x++) for (int y = (int)gobB.Y - 2; y < gobB.Y + 30; y++) if (wY.Get(x, y, z) == Mat.Soil && !earthBefore.Contains((x, y, z))) ballNow.Add((x, y, z));
    int bx0 = int.MaxValue, bx1 = int.MinValue, by0 = int.MaxValue, by1 = int.MinValue, bz0 = int.MaxValue, bz1 = int.MinValue;
    foreach (var (x, y, z) in ballNow) { bx0 = Math.Min(bx0, x); bx1 = Math.Max(bx1, x); by0 = Math.Min(by0, y); by1 = Math.Max(by1, y); bz0 = Math.Min(bz0, z); bz1 = Math.Max(bz1, z); }
    int wide = Math.Max(bx1 - bx0, bz1 - bz0) + 1, tall = by1 - by0 + 1;
    Check("a ball of earth that fell lies in its shape: as the ball it was, not spilled out flat", ballNow.Count > 600 && wide <= 18 && tall >= 8,
        $"{ballNow.Count} voxels of it lying {wide} wide and {tall} high (the ball was 17 across)");
    Gone(gobB);

    // a block that comes down on a narrow post with its weight off the post tips over the post's edge, and stands on its end beside it
    int tpx = kxY - 420, tpz = kzY - 300, tpg = wY.SurfAt(tpx, tpz);
    for (int z = tpz - 12; z < tpz + 12; z++) for (int x = tpx - 12; x < tpx + 30; x++) { for (int y = tpg; y < tpg + 40; y++) wY.Set(x, y, z, Mat.Air); for (int y = tpg - 3; y < tpg; y++) wY.Set(x, y, z, Mat.Soil); }
    for (int z = tpz; z < tpz + 2; z++) for (int x = tpx; x < tpx + 2; x++) for (int y = tpg; y < tpg + 12; y++) wY.Set(x, y, z, Mat.Stone);
    var slab = new System.Collections.Generic.List<(sbyte, sbyte, sbyte, byte)>();
    for (int dz = -3; dz < 3; dz++) for (int dy = -3; dy < 3; dy++) for (int dx = -8; dx < 8; dx++) slab.Add(((sbyte)dx, (sbyte)dy, (sbyte)dz, Mat.Rock));
    var block = simY.Hurl(slab, tpx + 7.5f, tpg + 22.5f, tpz + 1, 0, 0, 0, "test");
    bool tipped = false;
    for (int t = 0; t < 80; t++) { RunY(.05); tipped |= block.Tipping; }
    RunY(3);
    int beside = 0, stoodUp = int.MinValue, onPost = 0;
    for (int z = tpz - 4; z < tpz + 6; z++)
        for (int x = tpx + 2; x < tpx + 12; x++)
            for (int y = tpg; y < tpg + 24; y++) if (wY.Get(x, y, z) == Mat.Rubble) { beside++; stoodUp = Math.Max(stoodUp, y - tpg + 1); }
    for (int z = tpz - 4; z < tpz + 6; z++) for (int x = tpx - 4; x < tpx + 2; x++) for (int y = tpg + 12; y < tpg + 24; y++) if (wY.Get(x, y, z) == Mat.Rubble) onPost++;
    Check("a block that comes down on a narrow post with its weight off it tips over the post's edge and stands on its end beside it",
        tipped && simY.Fliers.Count == 0 && beside >= 500 && stoodUp >= 14 && onPost == 0,
        $"tipped {tipped}; {beside} voxels of it beside the post, {stoodUp} high (it was 6 high lying, 16 long); {onPost} left on the post");

    // a catapult's stone come down on the edge of a crag rolls off it and lies at its foot; it strikes as a shot only
    // where it first struck, and after is a stone like any: the goblin standing by where it comes down again is not hurt
    int spx = kxY - 420, spz = kzY - 250, spg = wY.SurfAt(spx, spz);
    for (int z = spz - 18; z < spz + 12; z++) for (int x = spx - 12; x < spx + 30; x++) { for (int y = spg; y < spg + 50; y++) wY.Set(x, y, z, Mat.Air); for (int y = spg - 3; y < spg; y++) wY.Set(x, y, z, Mat.Soil); }
    for (int z = spz; z < spz + 2; z++) for (int x = spx; x < spx + 2; x++) for (int y = spg; y < spg + 20; y++) wY.Set(x, y, z, Mat.Rock);
    var gobS = Stand(Kinds.Goblin, spx + 12, spz - 10);
    float gs0 = gobS.Hp;
    var stone = simY.Hurl(Sim.Ball(2.6f, Mat.Rock), spx + 3.5f, spg + 30.5f, spz + 1.5f, 0, 0, 0, "камень из камнемёта");
    stone.Smash = 5;
    bool rolled = false;
    for (int t = 0; t < 80; t++) { RunY(.05); rolled |= stone.Tipping || stone.Rolling; }
    RunY(3);
    int stoneOn = 0, stoneOff = 0, stoneX = 0;
    for (int z = spz - 8; z < spz + 10; z++)
        for (int x = spx - 6; x < spx + 26; x++)
            for (int y = spg; y < spg + 30; y++)
                if (wY.Get(x, y, z) == Mat.Rubble) { if (y >= spg + 20) stoneOn++; else { stoneOff++; stoneX = Math.Max(stoneX, x - spx); } }
    Check("a catapult's stone come down on the edge of a crag rolls off it and lies at its foot, and strikes as a shot only where it first struck: the goblin by where it comes down again is not hurt",
        rolled && simY.Fliers.Count == 0 && stoneOn == 0 && stoneOff >= 70 && !gobS.Dead && gobS.Hp >= gs0,
        $"rolled {rolled}; {stoneOff} voxels of it at the foot (out to {stoneX} from the crag), {stoneOn} left on top; the goblin {gobS.Hp:0} of {gs0:0}, {Far(gobS, spx + 10, spz + 1) :0.0} m from where it fell");
    Gone(gobS);

    // a ball of earth the hand tears out of the face of the ground is a whole ball: let go over a slope, it rolls down it,
    // runs over the goblin standing on it, is turned back off the wall at the foot and lies whole before it
    int rx0 = kxY - 450, rz = kzY - 190, rg = wY.SurfAt(rx0, rz);
    int SlopeTop(int x) => x < rx0 + 40 ? rg + 24 : x < rx0 + 88 ? rg + 24 - (x - rx0 - 40) / 2 : rg;
    for (int z = rz - 24; z < rz + 24; z++)
        for (int x = rx0 - 4; x < rx0 + 130; x++)
        {
            int top = SlopeTop(x);
            for (int y = rg - 4; y < rg + 70; y++) wY.Set(x, y, z, y < top - 1 ? Mat.Soil : y == top - 1 ? Mat.Grass : Mat.Air);
            if (x >= rx0 + 110 && x < rx0 + 114) for (int y = rg; y < rg + 16; y++) wY.Set(x, y, z, Mat.Stone);
        }
    int WallR() { int n = 0; for (int z = rz - 24; z < rz + 24; z++) for (int x = rx0 + 110; x < rx0 + 114; x++) for (int y = rg; y < rg + 16; y++) if (wY.Get(x, y, z) == Mat.Stone) n++; return n; }
    int wall0 = WallR();
    var gobL = Stand(Kinds.Goblin, rx0 + 70, rz);
    float gl0 = gobL.Hp;
    simY.ReadyNow(SpellKind.Telekinesis);
    string castR = simY.Cast(SpellKind.Telekinesis, rx0 + 20.5f, rg + 23.5f, rz + .5f);
    var ballR = simY.HeldNow;
    int ballRN = ballR?.Voxels.Count ?? 0;
    simY.HoldAt(rx0 + 46, rg + 44, rz + .5f);
    RunY(2.5);
    simY.LetGo();
    bool rollR = false; float rollFast = 0;
    for (int t = 0; t < 400 && ballR != null && simY.Fliers.Contains(ballR); t++)
    {
        RunY(.05);
        rollR |= ballR.Rolling;
        if (ballR.Rolling) rollFast = MathF.Max(rollFast, MathF.Sqrt(ballR.VX * ballR.VX + ballR.VZ * ballR.VZ) / VoxelWorld.PerMetre);
    }
    int footN = 0, pastN = 0, fx0 = int.MaxValue, fx1 = int.MinValue, fy1 = int.MinValue, fz0 = int.MaxValue, fz1 = int.MinValue;
    for (int z = rz - 24; z < rz + 24; z++)
        for (int x = rx0 + 88; x < rx0 + 130; x++)
            for (int y = rg; y < rg + 24; y++)
            {
                byte m = wY.Get(x, y, z);
                if (m != Mat.Soil && m != Mat.Grass) continue;
                if (x >= rx0 + 114) { pastN++; continue; }
                footN++; fx0 = Math.Min(fx0, x); fx1 = Math.Max(fx1, x); fy1 = Math.Max(fy1, y); fz0 = Math.Min(fz0, z); fz1 = Math.Max(fz1, z);
            }
    int wideR = Math.Max(fx1 - fx0, fz1 - fz0) + 1, tallR = fy1 - rg + 1;
    Check("a ball of earth torn from the face of the ground is a whole ball: let go over a slope it rolls down, runs over the goblin on it, is turned back by the wall at the foot and lies whole before it",
        castR == null && ballRN > 1500 && rollR && (gobL.Dead || gobL.Hp < gl0 - 15) && footN > ballRN * .8f && pastN == 0 && WallR() == wall0 && wideR <= 18 && tallR >= 13,
        $"cast: {castR ?? "yes"}; {ballRN} voxels lifted; rolled {rollR}, at most {rollFast:0.0} m/s; the goblin {(gobL.Dead ? "dead: " + gobL.KilledBy : $"{gobL.Hp:0} of {gl0:0}")}; {footN} voxels of it at the foot, {wideR} wide and {tallR} high, {pastN} past the wall; the wall {wall0} → {WallR()}");
    Gone(gobL);

    // a log lying across a slope rolls down it across itself, runs over the goblin on it and lies whole at the foot
    int hx0 = kxY - 450, hz0 = kzY + 140, hg = wY.SurfAt(hx0, hz0);
    int HillTop(int x) => x < hx0 + 40 ? hg + 24 : x < hx0 + 88 ? hg + 24 - (x - hx0 - 40) / 2 : hg;
    void Hill(int half = 24)
    {
        for (int z = hz0 - half; z < hz0 + half; z++)
            for (int x = hx0 - 4; x < hx0 + 130; x++)
            {
                int top = HillTop(x);
                for (int y = hg - 4; y < hg + 80; y++) wY.Set(x, y, z, y < top - 1 ? Mat.Soil : y == top - 1 ? Mat.Grass : Mat.Air);
                if (x >= hx0 + 110 && x < hx0 + 114) for (int y = hg; y < hg + 16; y++) wY.Set(x, y, z, Mat.Stone);
            }
    }
    Hill();
    RunY(.5);
    var logL = new System.Collections.Generic.List<(sbyte, sbyte, sbyte, byte)>();
    for (int dz = -12; dz < 12; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) logL.Add(((sbyte)dx, (sbyte)dy, (sbyte)dz, Mat.Trunk));
    var gobG = Stand(Kinds.Goblin, hx0 + 70, hz0);
    float gg0 = gobG.Hp;
    var logF = simY.Hurl(logL, hx0 + 46.5f, HillTop(hx0 + 46) + 3.5f, hz0 + .5f, 0, 0, 0, "test");
    bool logRolled = false;
    for (int t = 0; t < 400 && logF != null && simY.Fliers.Contains(logF); t++) { RunY(.05); logRolled |= logF.Rolling && logF.RollH > 0; }
    int logFoot = 0, lgx0 = int.MaxValue, lgx1 = int.MinValue, lgz0 = int.MaxValue, lgz1 = int.MinValue;
    for (int z = hz0 - 24; z < hz0 + 24; z++)
        for (int x = hx0 + 80; x < hx0 + 110; x++)
            for (int y = hg; y < hg + 12; y++)
                if (wY.Get(x, y, z) == Mat.Lumber) { logFoot++; lgx0 = Math.Min(lgx0, x); lgx1 = Math.Max(lgx1, x); lgz0 = Math.Min(lgz0, z); lgz1 = Math.Max(lgz1, z); }
    Check("a log lying across a slope rolls down it across itself, runs over the goblin on it and lies whole at the foot, along the way it lay",
        logRolled && (gobG.Dead || gobG.Hp < gg0 - 10) && logFoot >= 180 && lgz1 - lgz0 >= 20 && lgx1 - lgx0 <= 5,
        $"rolled {logRolled}; the goblin {(gobG.Dead ? "dead: " + gobG.KilledBy : $"{gobG.Hp:0} of {gg0:0}")}; {logFoot} of 216 voxels of it at the foot, {lgx1 - lgx0 + 1} across and {lgz1 - lgz0 + 1} along");
    Gone(gobG);

    // a tree marked to be felled, cut through at the foot on the same slope, falls across the slope and rolls down to
    // the foot, still marked to be cut up
    Hill(48);
    int tx2 = hx0 + 52, tbase = HillTop(tx2 + 1);
    var trunkMark = new VoxelList();
    for (int y = tbase; y < tbase + 40; y++) for (int z = hz0; z < hz0 + 2; z++) for (int x = tx2; x < tx2 + 2; x++) { wY.Set(x, y, z, Mat.Trunk); trunkMark.Add(x, y, z, Mat.Air); }
    for (int z = hz0; z < hz0 + 2; z++) for (int x = tx2; x < tx2 + 2; x++) for (int y = HillTop(x); y < tbase; y++) wY.Set(x, y, z, Mat.Trunk);
    simY.Orders.PlanRemove(trunkMark);
    var gobH = Stand(Kinds.Goblin, tx2, hz0 + 40);
    RunY(.5);
    for (int z = hz0; z < hz0 + 2; z++) for (int x = tx2; x < tx2 + 2; x++) for (int y = HillTop(x); y < tbase + 2; y++) wY.Set(x, y, z, Mat.Air);
    bool felledRolled = false;
    for (int t = 0; t < 400; t++) { RunY(.05); felledRolled |= simY.Fliers.Exists(f => f.Rolling && f.RollH > 0 && f.Cause == "бревно"); if (t > 40 && !simY.Fliers.Exists(f => f.Cause == "бревно")) break; }
    int trunkFoot = 0, trunkMarked = 0, trunkUp = 0;
    for (int z = hz0 - 48; z < hz0 + 48; z++)
        for (int x = hx0; x < hx0 + 110; x++)
            for (int y = hg; y < hg + 40; y++)
            {
                if (!Mat.IsTrunk(wY.Get(x, y, z))) continue;
                if (x >= hx0 + 80) { trunkFoot++; if (Orders.IsRemove(simY.Orders.At(x, y, z))) trunkMarked++; } else trunkUp++;
            }
    Check("a tree marked to be felled, cut through at the foot on a slope, falls across the slope, rolls down to the foot and is still marked to be cut up there",
        felledRolled && trunkFoot >= 120 && trunkMarked >= trunkFoot * .9f,
        $"rolled {felledRolled}; {trunkFoot} voxels of the trunk at the foot ({trunkMarked} marked), {trunkUp} left up the slope");
    Gone(gobH);

    // a stone falling six metres onto the horde's deck over a ditch breaks through it and lies at the bottom; a block of
    // a hand's breadth let down on the same deck from a metre lies on it; the goblin standing on the deck a little way
    // off stays standing
    int dx0 = kxY - 460, dz0 = kzY - 130, dg = wY.SurfAt(dx0, dz0);
    for (int z = dz0 - 20; z < dz0 + 20; z++)
        for (int x = dx0 - 10; x < dx0 + 50; x++)
            for (int y = dg - 30; y < dg + 60; y++)
                wY.Set(x, y, z, y >= dg ? Mat.Air : x >= dx0 + 8 && x < dx0 + 32 && y >= dg - 24 ? (y >= dg - 2 && x >= dx0 + 4 && x < dx0 + 36 && Math.Abs(z - dz0) < 12 ? Mat.Deck : Mat.Air)
                    : y >= dg - 2 && x >= dx0 + 4 && x < dx0 + 36 && Math.Abs(z - dz0) < 12 ? Mat.Deck : Mat.Soil);
    int DeckN() { int n = 0; for (int z = dz0 - 12; z < dz0 + 12; z++) for (int x = dx0 + 4; x < dx0 + 36; x++) for (int y = dg - 2; y < dg; y++) if (wY.Get(x, y, z) == Mat.Deck) n++; return n; }
    int deck0 = DeckN();
    RunY(.5);
    var gobD = Stand(Kinds.Goblin, dx0 + 20, dz0 + 18);
    float gd0 = gobD.Hp, gdy = gobD.Y;
    simY.Hurl(Sim.Ball(2.6f, Mat.Rock), dx0 + 20.5f, dg + 48.5f, dz0 + .5f, 0, 0, 0, "test");
    var block3 = new System.Collections.Generic.List<(sbyte, sbyte, sbyte, byte)>();
    for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) block3.Add(((sbyte)dx, (sbyte)dy, (sbyte)dz, Mat.Rock));
    simY.Hurl(block3, dx0 + 13.5f, dg + 9.5f, dz0 - 5.5f, 0, 0, 0, "test");
    RunY(5);
    int bottomN = 0, onDeckN = 0, holeN = 0;
    for (int z = dz0 - 20; z < dz0 + 20; z++)
        for (int x = dx0 + 8; x < dx0 + 32; x++)
            for (int y = dg - 24; y < dg + 8; y++)
            {
                byte m = wY.Get(x, y, z);
                if (m == Mat.Rubble) { if (y < dg - 2) bottomN++; else if (y >= dg && Math.Abs(x - dx0 - 13) <= 3 && Math.Abs(z - dz0 + 6) <= 3) onDeckN++; }
            }
    for (int z = dz0 - 1; z <= dz0 + 1; z++) for (int x = dx0 + 19; x <= dx0 + 21; x++) for (int y = dg - 2; y < dg; y++) if (wY.Get(x, y, z) == Mat.Deck) holeN++;
    int deck1 = DeckN();
    Check("a stone falling six metres onto the horde's deck over a ditch breaks through it and lies at the bottom; a little block let down on it from a metre lies on it; the goblin on the deck a little way off stands",
        bottomN >= 70 && holeN == 0 && deck1 > deck0 * .8f && deck1 < deck0 && onDeckN >= 20 && !gobD.Dead && gobD.Y >= gdy - 1,
        $"{bottomN} voxels of the stone at the bottom, {holeN} of the deck left where it struck; the deck {deck0} → {deck1}; {onDeckN} of the block lying on it; the goblin {(gobD.Dead ? "dead" : $"{gobD.Hp:0} of {gd0:0}")}, {(gobD.Y - gdy) / VoxelWorld.PerMetre:0.0} m from where it stood");
    Gone(gobD);

    // a floor of logs over a room holds a stone lying on it; one falling onto it from eight metres goes through it
    int wx0 = kxY - 460, wz0 = kzY - 80, wg = wY.SurfAt(wx0, wz0);
    for (int z = wz0 - 16; z < wz0 + 16; z++)
        for (int x = wx0 - 10; x < wx0 + 40; x++)
            for (int y = wg - 24; y < wg + 80; y++)
            {
                bool floor = y >= wg - 2 && y < wg && x >= wx0 + 2 && x < wx0 + 30 && Math.Abs(z - wz0) < 14;
                bool room = y >= wg - 18 && y < wg && x >= wx0 + 6 && x < wx0 + 26 && Math.Abs(z - wz0) < 10;
                wY.Set(x, y, z, floor ? Mat.Wood : room || y >= wg ? Mat.Air : Mat.Soil);
            }
    int FloorN() { int n = 0; for (int z = wz0 - 14; z < wz0 + 14; z++) for (int x = wx0 + 2; x < wx0 + 30; x++) for (int y = wg - 2; y < wg; y++) if (wY.Get(x, y, z) == Mat.Wood) n++; return n; }
    int floor0 = FloorN();
    RunY(.5);
    simY.Hurl(Sim.Ball(2.6f, Mat.Rock), wx0 + 11.5f, wg + 3.5f, wz0 + .5f, 0, 0, 0, "test");
    simY.Hurl(Sim.Ball(2.6f, Mat.Rock), wx0 + 20.5f, wg + 66.5f, wz0 + .5f, 0, 0, 0, "test");
    RunY(5);
    int lyingN = 0, belowN = 0;
    for (int z = wz0 - 12; z < wz0 + 12; z++)
        for (int x = wx0 + 2; x < wx0 + 30; x++)
            for (int y = wg - 18; y < wg + 8; y++)
                if (wY.Get(x, y, z) == Mat.Rubble) { if (y >= wg) lyingN++; else if (y < wg - 2) belowN++; }
    int floor1 = FloorN();
    Check("a floor of logs over a room holds a stone lying on it, and one falling onto it from eight metres goes through it into the room",
        lyingN >= 70 && belowN >= 70 && floor1 < floor0 && floor1 > floor0 * .9f,
        $"{lyingN} voxels of stone lying on the floor, {belowN} gone through it into the room; the floor {floor0} → {floor1}");

    // a stone thrown hard at a siege ladder against a wall tears it off where it strikes it; the wall stands
    int ldx = kxY - 380, ldz = kzY - 125, ldg = wY.SurfAt(ldx, ldz);
    for (int z = ldz - 10; z < ldz + 10; z++)
        for (int x = ldx - 34; x < ldx + 12; x++)
            for (int y = ldg - 4; y < ldg + 48; y++)
                wY.Set(x, y, z, y < ldg ? Mat.Soil : x >= ldx && x < ldx + 4 && y < ldg + 32 ? Mat.Stone : x == ldx - 1 && Math.Abs(z - ldz + .5f) < 1 && y < ldg + 32 ? Mat.SiegeLadder : Mat.Air);
    int RungsL(int y0, int y1) { int n = 0; for (int z = ldz - 2; z < ldz + 2; z++) for (int y = y0; y < y1; y++) if (wY.Get(ldx - 1, y, z) == Mat.SiegeLadder) n++; return n; }
    int WallL() { int n = 0; for (int z = ldz - 10; z < ldz + 10; z++) for (int x = ldx; x < ldx + 4; x++) for (int y = ldg; y < ldg + 32; y++) if (wY.Get(x, y, z) == Mat.Stone) n++; return n; }
    int rungsL0 = RungsL(ldg, ldg + 32), midL0 = RungsL(ldg + 13, ldg + 19), wallL0 = WallL();
    RunY(.5);
    simY.Hurl(Sim.Ball(2.6f, Mat.Rock), ldx - 24.5f, ldg + 16.5f, ldz + .5f, 12 * VoxelWorld.PerMetre, VoxelWorld.PerMetre, 0, "test");
    RunY(3);
    int rungsL1 = RungsL(ldg, ldg + 32), midL1 = RungsL(ldg + 13, ldg + 19);
    Check("a stone thrown hard at a siege ladder against a wall tears the ladder off where it strikes it; the wall stands",
        midL0 > 10 && midL1 == 0 && rungsL1 < rungsL0 && WallL() == wallL0,
        $"the ladder {rungsL0} → {rungsL1} rungs, where it was struck {midL0} → {midL1}; the wall {wallL0} → {WallL()}");

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

    // broken stone come down on a bough, far out from its trunk, lies there: shaken over and over (as by a tree burning
    // beside it, or the ground dug), it does not lift off the bough and come down on it again for ever
    int bpx = kxY - 120, bpz = kzY + 320, bgB = wY.SurfAt(bpx, bpz);
    for (int y = bgB; y < bgB + 48; y++) for (int dz = 0; dz < 2; dz++) for (int dx = 0; dx < 2; dx++) wY.Set(bpx + dx, y, bpz + dz, Mat.PineBark);
    for (int x = bpx + 2; x < bpx + 14; x++) wY.Set(x, bgB + 40, bpz, Mat.PineBark);
    RunY(1);
    simY.Hurl(new System.Collections.Generic.List<(sbyte, sbyte, sbyte, byte)> { (0, 0, 0, Mat.Rubble), (1, 0, 0, Mat.Rubble) }, bpx + 10.5f, bgB + 48, bpz + .5f, 0, -4, 0, "обвал");
    RunY(1.5);
    int landedB = 0, shakeY = wY.SurfAt(bpx + 10, bpz + 4);
    Action<float, float, float, int, float> countB = (x, y, z, _, _) => { if (MathF.Abs(x - bpx - 10) < 24 && MathF.Abs(z - bpz) < 24 && MathF.Abs(y - bgB - 41) < 24) landedB++; };
    simY.Landed += countB;
    for (int k = 0; k < 16; k++) { wY.Set(bpx + 10, shakeY, bpz + 4, k % 2 == 0 ? Mat.Stone : Mat.Air); RunY(.25); }
    simY.Landed -= countB;
    int lyingB = 0;
    for (int x = bpx + 2; x < bpx + 14; x++) if (wY.Get(x, bgB + 41, bpz) == Mat.Rubble) lyingB++;
    Check("broken stone come down on a bough lies there, and shaken does not lift off it and come down again", lyingB == 2 && landedB == 0,
        $"{lyingB} of 2 voxels on the bough; came down there again {landedB} times in 4 s of shaking");
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
    Check("a wave beaten back brings gold and knowledge", !simK.Waves.Active && simK.Stock.Gold >= goldK0 + 40 && simK.Knowledge == 4,
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

    // the tree goes deeper: the last of a branch rests on both before it, one rests on another branch besides its own,
    // and there is more of it than a siege gives knowledge for
    simK.Knowledge = 100;
    bool longFirst = simK.Learn("barbs") && simK.Learn("volley") && simK.Learn("longbows");
    bool longK = simK.Learn("braziers") && simK.Learn("owleye") && simK.Learn("longbows");
    float reachK2 = simK.RangeAt(bowK, bowK.X + 50, bowK.Z);
    bool guildFirst = simK.Learn("picks") && simK.Learn("barrows") && simK.Learn("carts") && simK.Learn("guild");
    bool guildK = simK.Learn("mortar") && simK.Learn("masons") && simK.Learn("guild");
    int treeCost = 0, siege = 0, skills = 0, troops = 0, rites = 0;
    foreach (var l in Sim.Lores)
    {
        if (l.Coin == Coin.Knowledge) treeCost += l.Cost;
        if (l.Page == 0) skills++; else if (l.Page == 1) troops++; else rites++;
    }
    for (int wave = 1; wave <= Waves.Total; wave++) siege += 3 + wave;
    Check("the tree is four deep: the last of a branch wants both before it, the masters' guild wants the masons' too, and the skills and the troops together cost more than a siege gives",
        skills == 30 && troops == 24 && rites == 30 && !longFirst && longK && MathF.Abs(reachK2 / reachK0 - 1.15f * 1.15f) < .01f && !guildFirst && guildK && treeCost > siege * 1.5f,
        $"{skills} skills, {troops} of the troops, {rites} of the intervention; longbows on one {longFirst}, on both {longK}, reach ×{reachK2 / reachK0:0.000}; the guild without masons {guildFirst}, with {guildK}; skills and troops {treeCost}, a siege's knowledge {siege}");
    // every page's branches are its own, each branch a tier 0 to a tier 3, every root on its own page
    bool shaped = true;
    for (int pg = 0; pg < 3; pg++)
        foreach (string br in Sim.PageBranches[pg])
        {
            var tiers = new System.Collections.Generic.List<int>();
            foreach (var l in Sim.LoresOn(pg)) if (l.Branch == br) tiers.Add(l.Tier);
            if (tiers.Count != 6 || !tiers.Contains(0) || !tiers.Contains(3)) shaped = false;
        }
    foreach (var l in Sim.Lores) foreach (string nd in l.Needs) if (Sim.LoreOf(nd) is not { } r || r.Page != l.Page) shaped = false;
    Check("every branch of every page is six lores from a first to a last, every lore resting on its own page; the circles are the spells' own",
        shaped && Sim.Circles[0] == Sim.PageBranches[2][0] && Sim.Circles[3] == Sim.PageBranches[2][3] && Sim.Spells.Length == 23,
        $"shaped {shaped}, {Sim.Spells.Length} spells");

    // mail takes a fifth off a blow to a soldier of ours; tracks and barrows have workers walk a fifth faster
    float hpK0 = swordK.Hp;
    simK.Hurt(swordK, null, 10);
    float mailOff = hpK0 - swordK.Hp;
    simK.Learn("drill"); simK.Learn("mail");
    hpK0 = swordK.Hp;
    simK.Hurt(swordK, null, 10);
    float mailOn = hpK0 - swordK.Hp;
    var walkerK = simK.Spawn(Kinds.Worker, simK.NearestStand(siteK.KeepX - 120, siteK.KeepZ, 2));
    int farK = simK.NearestStand(siteK.KeepX - 220, siteK.KeepZ, 2);
    float WalkFor(double seconds)
    {
        float x0 = walkerK.X, z0 = walkerK.Z;
        walkerK.Path = null;
        simK.Command(new[] { walkerK }, farK);
        RunK(seconds);
        float d = MathF.Sqrt((walkerK.X - x0) * (walkerK.X - x0) + (walkerK.Z - z0) * (walkerK.Z - z0));
        simK.Command(new[] { walkerK }, simK.NearestStand(siteK.KeepX - 120, siteK.KeepZ, 2));
        RunK(30);
        return d;
    }
    simK.Learnt.Remove("carts");
    float plainK = WalkFor(2);
    simK.Learnt.Add("carts");
    float cartsK = WalkFor(2);
    Check("mail takes a fifth off the blows a soldier of ours takes; tracks and stretchers have workers walk a fifth faster",
        MathF.Abs(mailOff - 10) < .01f && MathF.Abs(mailOn - 8) < .01f && cartsK > plainK * 1.12f,
        $"a blow of 10 takes {mailOff:0.0}, in mail {mailOn:0.0}; a worker walks {plainK / VoxelWorld.PerMetre:0.0} m in two seconds, with tracks {cartsK / VoxelWorld.PerMetre:0.0} m");
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
    // a dead tree's trunk set alight, out in the field (our workers put out what burns of ours, and are quick about it)
    int px = kxS - 400, pz = kzS + 60, pg = wS.SurfAt(px, pz);
    for (int y = pg; y < pg + 12; y++) for (int z = pz; z < pz + 2; z++) for (int x = px; x < px + 2; x++) wS.Set(x, y, z, Mat.Trunk);
    simS.Ignite(px, pg + 4, pz);
    RunS(1);
    // and something learnt, and given
    simS.Knowledge = 4;
    simS.Learn("mortar");
    simS.Revelations = 2; simS.Carcasses.Add(Kinds.Boar); simS.Favour = 17;
    simS.Known.Add(SpellKind.Hand); simS.Known.Add(SpellKind.Eye);
    // a ball of rock rolling, and a stone high in the air
    int rxS = kxS - 200, rzS = kzS - 100;
    simS.Hurl(Sim.Ball(3.6f, Mat.Rock), rxS + .5f, wS.SurfAt(rxS, rzS) + 4.5f, rzS + .5f, 3 * VoxelWorld.PerMetre, 0, 0, "test");
    simS.Hurl(Sim.Ball(2f, Mat.Stone), rxS - 20.5f, wS.SurfAt(rxS - 20, rzS) + 80, rzS + .5f, 0, 0, 0, "test");
    RunS(.5);
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
        if (!same)
        {
            chunksOff++;
            if (Environment.GetEnvironmentVariable("SAVE_WHY") != null)
            {
                wS.ChunkCoords(ci, out int ccx, out int ccy, out int ccz);
                int shown = 0;
                for (int y = ccy * 32; y < ccy * 32 + 32 && shown < 6; y++)
                    for (int z = ccz * 32; z < ccz * 32 + 32 && shown < 6; z++)
                        for (int x = ccx * 32; x < ccx * 32 + 32 && shown < 6; x++)
                            if (wS.Get(x, y, z) != wL.Get(x, y, z)) { shown++; Console.WriteLine($"  SAVE_WHY {x},{y},{z}: saved {Mat.NameOf(wS.Get(x, y, z))}, read {Mat.NameOf(wL.Get(x, y, z))}; order {simS.Orders.At(x, y, z)} / {simL.Orders.At(x, y, z)}; chunk changed {wS.Changed(ci)}; below {Mat.NameOf(wS.Get(x, y - 1, z))} / {Mat.NameOf(wL.Get(x, y - 1, z))}");
                                if (shown == 1)
                                {
                                    // the tuft it hangs in, and any trunk it touches, in both
                                    foreach (var (wv, nm) in new[] { (wS, "saved"), (wL, "read") })
                                    {
                                        var seenT = new HashSet<long>(); var q = new Queue<(int, int, int)>(); q.Enqueue((x, y, z)); seenT.Add(((long)y * 1024 + z) * 1024 + x);
                                        int leaves = 0; string trunk = "none";
                                        while (q.Count > 0 && leaves < 50000)
                                        {
                                            var (qx, qy, qz) = q.Dequeue(); leaves++;
                                            for (int ddy = -1; ddy <= 1; ddy++) for (int ddz = -1; ddz <= 1; ddz++) for (int ddx = -1; ddx <= 1; ddx++)
                                            {
                                                int nx = qx + ddx, ny = qy + ddy, nz = qz + ddz; byte nm2 = wv.Get(nx, ny, nz);
                                                if (Mat.IsTrunk(nm2) && trunk == "none") trunk = $"{nx},{ny},{nz} {Mat.NameOf(nm2)} (other {Mat.NameOf((wv == wS ? wL : wS).Get(nx, ny, nz))})";
                                                if (Mat.IsLeafy(nm2) && seenT.Add(((long)ny * 1024 + nz) * 1024 + nx)) q.Enqueue((nx, ny, nz));
                                            }
                                        }
                                        Console.WriteLine($"    {nm}: tuft {leaves} leaves, trunk {trunk}");
                                    }
                                }
                            }
            }
        }
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
    // and what was in the air, or rolling, goes on its way as it was
    var piecesS = simS.Fliers.FindAll(f => f.Unit == null);
    var piecesL = simL.Fliers.FindAll(f => f.Unit == null);
    bool fliersSame = piecesS.Count >= 2 && piecesS.Exists(f => f.Rolling) && piecesS.Exists(f => !f.Rolling) && piecesL.Count == piecesS.Count;
    for (int i = 0; fliersSame && i < piecesS.Count; i++)
    {
        var (a, b) = (piecesS[i], piecesL[i]);
        fliersSame = a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.VX == b.VX && a.VY == b.VY && a.Turn == b.Turn && a.AY == b.AY && a.Rolling == b.Rolling
            && a.RollR == b.RollR && a.Voxels.Count == b.Voxels.Count && a.Voxels.SequenceEqual(b.Voxels);
    }
    sameSiege &= fliersSame;
    Check("a game saved and read back is the same: the valley as play changed it, the household and the horde, the siege",
        chunksOff == 0 && designOff == 0 && unitsOff == 0 && livingS.Count == simL.Units.Count && sameSiege && simS.Burning.Count > 0 && simS.Bands.Count > 0,
        $"{bytes / 1024} KB, written in {saveMs} ms, read in {loadMs} ms (the valley made again); {chunksOff} chunks and {designOff} of the design differ; " +
        $"{livingS.Count} living, {unitsOff} differ; {simS.Bands.Count} bands, {simS.Burning.Count} burning, wave {simS.Waves.Number} ({simS.Waves.Coming} still to come); {piecesS.Count} pieces in the air or rolling, the same {fliersSame}; same siege {sameSiege}");
    // and it plays on: the horde still comes on, the fire still burns
    simL.ComputeFields();
    int burning0 = simL.Burning.Count;
    float far0 = simL.Units.FindAll(u => !u.Dead && u.Team == Team.Horde && !u.Guard).ConvertAll(u => MathF.Abs(u.X - kxS) + MathF.Abs(u.Z - kzS)).DefaultIfEmpty(0).Average();
    for (double t = 0; t < 30; t += .05) simL.Step(.05f);
    float far1 = simL.Units.FindAll(u => !u.Dead && u.Team == Team.Horde && !u.Guard).ConvertAll(u => MathF.Abs(u.X - kxS) + MathF.Abs(u.Z - kzS)).DefaultIfEmpty(0).Average();
    Check("…and the game read back plays on: the horde comes on, the fire burns on", burning0 > 0 && simL.Burning.Count != burning0 && (far1 < far0 || simL.Kills > simS.Kills),
        $"burning {burning0} → {simL.Burning.Count}; the horde on average {far0 / VoxelWorld.PerMetre:0} → {far1 / VoxelWorld.PerMetre:0} m from the citadel, {simL.Kills - simS.Kills} more killed");
}

// ---- grace and the greatest spells: what the intervention's page does
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--grace")
{
    Console.WriteLine();
    var mpG = new MapParams();
    var wG = new VoxelWorld(mpG.SizeX, mpG.SizeY, mpG.SizeZ);
    var siteG = MapGen.Generate(wG, mpG);
    wG.Compact();
    var simG = new Sim(wG, siteG);
    simG.Waves.Countdown = 1e9; simG.WildTarget = 0;
    void RunG(double seconds) { for (double t = 0; t < seconds; t += .05) simG.Step(.05f); }
    int kxG = siteG.KeepX, kzG = siteG.KeepZ;
    Unit RaiderG(UnitType t, int x, int z) { var u = simG.Spawn(t, simG.NearestStand(x, z, t.Move.Height)); u.Guard = true; u.Home = u.Cell; return u; }
    void GoneG(Unit u) { u.Dead = true; u.DiedAt = -999; }

    // the intervention held has a ceiling, raised by the vessel; prayer with the rosary brings more
    simG.Favour = 140;
    simG.GainFavour(30);
    double capped = simG.Favour;
    simG.Learnt.Add("vessel");
    simG.GainFavour(30);
    Check("intervention is held up to 150, up to 300 with the vessel", capped == 150 && simG.Favour == 180 && simG.FavourCap == 300, $"140 + 30 → {capped}; with the vessel +30 → {simG.Favour}");
    // the circles are learnt in order up to their greatest; litanies make casting cheaper, the vigil readier
    simG.Favour = 200; simG.Revelations = 30;
    bool meteorFirst = simG.Learn(Sim.SpellKey(SpellKind.Meteor));
    bool meteorK = simG.Learn(Sim.SpellKey(SpellKind.FireRain)) && simG.Learn(Sim.SpellKey(SpellKind.Thunder)) && simG.Learn(Sim.SpellKey(SpellKind.Weather))
        && simG.Learn(Sim.SpellKey(SpellKind.BlackWind)) && simG.Learn(Sim.SpellKey(SpellKind.BloodMoon)) && simG.Learn(Sim.SpellKey(SpellKind.Meteor));
    var thunder = Sim.SpellOf(SpellKind.Thunder);
    double cost0 = simG.CostOf(thunder), cd0 = simG.CooldownOf(thunder);
    simG.Learnt.Add("litany"); simG.Learnt.Add("vigil");
    Check("a circle is learnt in order to its greatest spell at its end; litanies take a fifth off a casting, the vigil a third off the wait",
        !meteorFirst && meteorK && simG.Known.Contains(SpellKind.Meteor) && Math.Abs(simG.CostOf(thunder) - cost0 * .8) < 1e-9 && Math.Abs(simG.CooldownOf(thunder) - cd0 * .7) < 1e-9,
        $"the falling star first {meteorFirst}, after its circle {meteorK}; the bolt {cost0} → {simG.CostOf(thunder)} intervention, {cd0} → {simG.CooldownOf(thunder)} s");
    foreach (var sp in Sim.Spells) simG.Known.Add(sp.Kind);
    simG.Favour = 5000;

    // the strength of faith: a bolt strikes a troll a third harder
    var trollG = RaiderG(Kinds.Troll, kxG - 300, kzG + 200);
    simG.Cast(SpellKind.Thunder, trollG.X, trollG.Y, trollG.Z);
    float plainBolt = trollG.MaxHp - trollG.Hp;
    trollG.Hp = trollG.MaxHp;
    simG.ReadyNow(SpellKind.Thunder);
    simG.Learnt.Add("might");
    simG.Cast(SpellKind.Thunder, trollG.X, trollG.Y, trollG.Z);
    float mightyBolt = trollG.MaxHp - trollG.Hp;
    GoneG(trollG);
    // the reaping: a raider the spells kill gives intervention back
    simG.Learnt.Add("reap");
    var gobReap = RaiderG(Kinds.Goblin, kxG - 300, kzG + 260);
    simG.ReadyNow(SpellKind.Thunder);
    simG.Favour = 100;
    double fav0 = simG.Favour;
    simG.Cast(SpellKind.Thunder, gobReap.X, gobReap.Y, gobReap.Z);
    double back = simG.Favour - (fav0 - simG.CostOf(thunder));
    Check("with the strength of faith spells strike a third harder; with the reaping a raider they kill gives two intervention back",
        MathF.Abs(mightyBolt / plainBolt - 1.3f) < .02f && gobReap.Dead && Math.Abs(back - 2) < 1e-6,
        $"a bolt into a troll {plainBolt:0} → {mightyBolt:0}; the goblin {(gobReap.Dead ? "killed" : "alive")}, intervention back {back:0.##}");
    GoneG(gobReap);
    simG.Favour = 5000;

    // the earthquake: the raiders in the circle are thrown up, the horde's ladder is shaken apart, our wall stands
    int qx = kxG - 260, qz = kzG - 160;
    var shaken = new System.Collections.Generic.List<Unit>();
    for (int i = 0; i < 5; i++) shaken.Add(RaiderG(Kinds.Goblin, qx - 16 + i * 8, qz + (i % 2) * 10));
    int lg = wG.SurfAt(qx + 30, qz);
    for (int y = lg; y < lg + 24; y++) for (int x = qx + 30; x < qx + 32; x++) wG.Set(x, y, qz, Mat.SiegeLadder);
    int ourWall = 0, wg = wG.SurfAt(qx, qz + 30);
    for (int y = wg; y < wg + 16; y++) for (int x = qx - 8; x < qx + 8; x++) { wG.Set(x, y, qz + 30, Mat.Stone); ourWall++; }
    RunG(.2);
    string quake = simG.Cast(SpellKind.Quake, qx, wG.SurfAt(qx, qz), qz);
    int thrownQ = shaken.FindAll(u => u.Airborne != null).Count;
    RunG(3);
    int ladderLeft = 0, wallLeft = 0;
    for (int y = lg; y < lg + 24; y++) for (int x = qx + 30; x < qx + 32; x++) if (wG.Get(x, y, qz) == Mat.SiegeLadder) ladderLeft++;
    for (int y = wg; y < wg + 16; y++) for (int x = qx - 8; x < qx + 8; x++) if (Mat.IsMasonry(wG.Get(x, y, qz + 30))) wallLeft++;
    Check("the earthquake throws the raiders in it up off their feet and shakes the horde's ladder apart, and leaves our wall standing",
        quake == null && thrownQ == 5 && ladderLeft == 0 && wallLeft == ourWall, $"cast: {quake ?? "yes"}; thrown {thrownQ} of 5; ladder voxels left {ladderLeft} of 48; our wall {wallLeft} of {ourWall}");
    foreach (var u in shaken) GoneG(u);

    // the falling star: it comes down out of the sky on the spot, blows a crater and the goblins about it away, sets the
    // grass alight, and its rock rolls on
    int sx = kxG - 330, sz = kzG + 60;
    int sg0 = wG.SurfAt(sx, sz);
    var struck = new System.Collections.Generic.List<Unit>();
    for (int i = 0; i < 3; i++) struck.Add(RaiderG(Kinds.Goblin, sx - 12 + i * 12, sz + 8));
    int blasts = 0;
    float bxS = 0, bzS = 0;
    Action<float, float, float, float> countBlast = (bx, _, bz, _) => { if (blasts++ == 0) { bxS = bx; bzS = bz; } };
    simG.Exploded += countBlast;
    string star = simG.Cast(SpellKind.Meteor, sx, sg0, sz);
    bool flying = simG.Fliers.Exists(f => f.Star);
    RunG(3);
    simG.Exploded -= countBlast;
    int sg1 = wG.SurfAt(sx, sz);
    // the crater: columns about where it struck hollowed out under the ground as it was
    int crater = 0;
    for (int dz = -16; dz <= 16; dz += 2) for (int dx = -16; dx <= 16; dx += 2) if (wG.Get((int)bxS + dx, sg0 - 3, (int)bzS + dz) == Mat.Air) crater++;
    float offS = MathF.Sqrt((bxS - sx) * (bxS - sx) + (bzS - sz) * (bzS - sz)) / VoxelWorld.PerMetre;
    int hurtS = struck.FindAll(u => u.Dead || u.Hp < u.MaxHp).Count;
    bool rockLies = false;
    for (int z = sz - 160; z <= sz + 160 && !rockLies; z += 2) for (int x = sx - 40; x <= sx + 220 && !rockLies; x += 2) for (int y = sg1 - 30; y < sg1 + 30 && !rockLies; y++) if (wG.Get(x, y, z) == Mat.Rock && Math.Abs(wG.SurfAt(x, z) - 1 - y) < 2 && Mat.IsNatural[wG.Get(x, y - 1, z)] == false) rockLies = true;
    bool rocks = rockLies || simG.Fliers.Exists(f => f.Unit == null && f.Rolling);
    Check("the falling star comes down on the spot: a blast, a crater, the goblins about it struck, and its rock still there after",
        star == null && flying && blasts >= 1 && offS < 2 && crater > 20 && hurtS == 3 && simG.Fliers.TrueForAll(f => !f.Star),
        $"cast: {star ?? "yes"}; in the air {flying}; blasts {blasts}, {offS:0.0} m from the spot; a crater of {crater} columns; struck {hurtS} of 3; the rock {(rocks ? "lies or rolls" : "not found")}");
    foreach (var u in struck) GoneG(u);

    // the whirlwind: let loose by a band, it goes to them and throws them about; ours it goes round
    int wx = kxG - 240, wz = kzG + 220;
    var band = new System.Collections.Generic.List<Unit>();
    for (int i = 0; i < 4; i++) band.Add(RaiderG(Kinds.Goblin, wx + 48 + i * 6, wz + (i % 2) * 8));
    var ours = simG.Spawn(Kinds.Worker, simG.NearestStand(wx, wz + 24, 2));
    ours.Hold = true;
    string whirl = simG.Cast(SpellKind.Whirlwind, wx, wG.SurfAt(wx, wz), wz);
    var bx0 = band.ConvertAll(u => (u.X, u.Z));
    int flungW = 0;
    for (int t = 0; t < 80; t++) { RunG(.1); foreach (var u in band) if (u.Airborne != null) { } flungW = band.FindAll(u => u.Airborne != null || u.Dead).Count; if (flungW >= 3) break; }
    RunG(14);
    float movedW = 0;
    for (int i = 0; i < band.Count; i++) movedW = MathF.Max(movedW, MathF.Sqrt((band[i].X - bx0[i].X) * (band[i].X - bx0[i].X) + (band[i].Z - bx0[i].Z) * (band[i].Z - bx0[i].Z)) / VoxelWorld.PerMetre);
    Check("the whirlwind goes to the band and throws them about, and leaves ours alone; and it blows itself out",
        whirl == null && flungW >= 3 && movedW > 3 && ours.Airborne == null && !ours.Dead && simG.Whirls.Count == 0,
        $"cast: {whirl ?? "yes"}; taken up {flungW} of 4; the farthest thrown {movedW:0.0} m; ours {(ours.Airborne != null ? "in the air" : "on the ground")}; whirls left {simG.Whirls.Count}");
}

// ---- going into one of ours: it goes where it is told, strikes and shoots where it looks, digs and lays
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--possess")
{
    Console.WriteLine();
    var mpQ = new MapParams();
    var wQ = new VoxelWorld(mpQ.SizeX, mpQ.SizeY, mpQ.SizeZ);
    var siteQ = MapGen.Generate(wQ, mpQ);
    wQ.Compact();
    var simQ = new Sim(wQ, siteQ);
    simQ.Waves.Countdown = 1e9; simQ.WildTarget = 0;
    void RunQ(double seconds) { for (double t = 0; t < seconds; t += .05) simQ.Step(.05f); }
    var (gxQ, _, gzQ) = simQ.Nav.Feet(simQ.Gate);
    // flat open ground a little west of the citadel's door
    int ox = (int)gxQ - 120, oz = (int)gzQ + 80;
    var sword = simQ.Spawn(Kinds.Swordsman, simQ.NearestStand(ox, oz, 2));
    bool took = simQ.Possess(sword);
    float x0 = sword.X, z0 = sword.Z;
    simQ.LookYaw = 0; simQ.WishX = 1; simQ.WishZ = 0;
    RunQ(2);
    simQ.WishX = 0;
    float walked = (sword.X - x0) / VoxelWorld.PerMetre, sideways = MathF.Abs(sword.Z - z0) / VoxelWorld.PerMetre;
    Check("one gone into goes where it is told and nowhere else: two seconds forward", took && simQ.Possessed == sword && walked > 3 && sideways < .5f && sword.Path == null,
        $"possessed {took}; went {walked:0.0} m forward, {sideways:0.0} m aside");
    // a wall across the way stops it; a ledge a metre high it steps up onto
    int wx = (int)sword.X + 24, wz = (int)sword.Z;
    int wtop = Shapes.LevelTop(wQ, wx, wz, 24);
    simQ.InstantLabour = true;
    simQ.DesignateBuild(Shapes.Line(wQ, siteQ, wx, wz - 40, wx, wz + 40, new Stroke { Mat = Mat.Stone, Width = 8, Height = 24, Level = true }, wtop));
    simQ.InstantLabour = false;
    RunQ(.5);
    simQ.WishX = 1;
    RunQ(4);
    simQ.WishX = 0;
    bool stopped = sword.X < wx - 2;
    Check("a wall across its way stops one gone into", stopped, $"at x {sword.X:0}, the wall's face at {wx - 4}");
    // a goblin before it: the blow finds it
    var gob = simQ.Spawn(Kinds.Goblin, simQ.NearestStand((int)sword.X - 10, (int)sword.Z, 2));
    gob.Guard = true; gob.Home = gob.Cell;
    // looking at it, as one would: the blade goes where the eyes do, and a goblin stands a head lower than a man
    float sdx = gob.X - sword.X, sdz = gob.Z - sword.Z, sdy = gob.Y + gob.T.BodyH * .55f - (sword.Y + sword.T.BodyH * .88f);
    simQ.LookYaw = MathF.Atan2(sdz, sdx); simQ.LookPitch = MathF.Atan2(sdy, MathF.Sqrt(sdx * sdx + sdz * sdz));
    simQ.WishStrike = true;
    RunQ(1.5);
    simQ.WishStrike = false;
    Check("one gone into strikes whoever of the horde stands before it", gob.Dead || gob.Hp < gob.MaxHp, $"the goblin {(gob.Dead ? "dead" : $"{gob.Hp:0} of {gob.MaxHp:0}")}");
    gob.Dead = true; gob.DiedAt = -999;
    // an archer: the arrow goes along its eyes to the goblin it looks at
    var bow = simQ.Spawn(Kinds.Archer, simQ.NearestStand(ox, oz + 60, 2));
    simQ.Possess(bow);
    bool letGo = simQ.Possessed == bow && sword.Why == "";
    var mark = simQ.Spawn(Kinds.Goblin, simQ.NearestStand((int)bow.X - 80, (int)bow.Z + 6, 2));
    mark.Guard = true; mark.Home = mark.Cell;
    RunQ(.2);
    float tdx = mark.X - bow.X, tdz = mark.Z - bow.Z, tdy = mark.Y + mark.T.BodyH * .55f - (bow.Y + bow.T.BodyH * .88f);
    simQ.LookYaw = MathF.Atan2(tdz, tdx); simQ.LookPitch = MathF.Atan2(tdy, MathF.Sqrt(tdx * tdx + tdz * tdz));
    int shots0 = simQ.Shots;
    simQ.WishStrike = true;
    for (int t = 0; t < 60 && !(mark.Dead || mark.Hp < mark.MaxHp); t++) RunQ(.1);
    simQ.WishStrike = false;
    var sighted = simQ.Sighted(bow, 200, out float along);
    Check("an archer gone into looses along its eyes, and the arrow strikes the goblin it looks at; letting go of the swordsman before",
        letGo && simQ.Shots > shots0 && (mark.Dead || mark.Hp < mark.MaxHp), $"let go {letGo}; {simQ.Shots - shots0} arrows, the goblin {(mark.Dead ? "dead" : $"{mark.Hp:0} of {mark.MaxHp:0}")}; sighted {(sighted == mark ? "the goblin" : sighted?.T.Key ?? "nothing")} {along / VoxelWorld.PerMetre:0.0} m");
    mark.Dead = true; mark.DiedAt = -999;
    // at nothing of the horde the bow is drawn first: a click brings the string back to the jaw, and the arrow goes up
    // into the air where it looks a third of a second later
    simQ.LookYaw += MathF.PI; simQ.LookPitch = .3f;
    RunQ(2);
    int shots1 = simQ.Shots;
    simQ.WishStrike = true;
    simQ.Step(.05f);
    simQ.WishStrike = false;
    bool notYet = simQ.Shots == shots1;
    RunQ(.5);
    Check("…and at nothing it draws first: a click looses the arrow when the string is at the jaw, a third of a second on",
        notYet && simQ.Shots == shots1 + 1, $"at the click {(notYet ? "nothing yet" : "loosed")}, half a second on {simQ.Shots - shots1} arrows");
    // a worker: the pick takes out the earth it looks at, the trowel lays stone where it looks
    var dig = simQ.Spawn(Kinds.Worker, simQ.NearestStand(ox, oz - 60, 2));
    simQ.Possess(dig);
    simQ.LookYaw = 0; simQ.LookPitch = -.75f;
    double earth0 = simQ.Stock[Stockpile.Earth], stone0 = simQ.Stock[Stockpile.Stone];
    int below0 = wQ.SurfAt((int)dig.X + 10, (int)dig.Z);
    simQ.WishStrike = true;
    RunQ(3);
    simQ.WishStrike = false;
    double dug = simQ.Stock[Stockpile.Earth] - earth0;
    simQ.LookPitch = -.6f;
    simQ.WishLay = true;
    RunQ(3);
    simQ.WishLay = false;
    double laid = stone0 - simQ.Stock[Stockpile.Stone];
    Check("a worker gone into digs out the earth it looks at with the pick, and lays stone where it looks",
        dug > 100 && laid > 50, $"earth +{dug:0} voxels, stone laid {laid:0} voxels");
    // and when it falls, the player is out of it
    string why = null;
    simQ.Unpossessed += w => why = w;
    simQ.Hurt(dig, null, 1000, "test");
    RunQ(.1);
    Check("when the one gone into falls, the player is out of it", simQ.Possessed == null && why != null && why.Contains("пал"), $"«{why}»");
}

// ---- the citadel's muster: taken on for gold, made ready in turn, as many as there are places for
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--muster")
{
    Console.WriteLine();
    var mpM = new MapParams();
    var wM = new VoxelWorld(mpM.SizeX, mpM.SizeY, mpM.SizeZ);
    var siteM = MapGen.Generate(wM, mpM);
    wM.Compact();
    var simM = new Sim(wM, siteM);
    simM.Waves.Countdown = 1e9; simM.WildTarget = 0;
    void RunM(double seconds) { for (double t = 0; t < seconds; t += .05) simM.Step(.05f); }
    int Ours() => simM.Units.FindAll(u => !u.Dead && u.Team == Team.Player).Count;
    simM.Stock.Gold = 1000;
    // a worker enlisted: paid, and out of the citadel's door at once
    int before = Ours();
    bool took = simM.Enlist(Kinds.Worker);
    double paid = 1000 - simM.Stock.Gold;
    int at = Ours();
    RunM(4);
    var fresh = simM.Units.Find(u => !u.Dead && u.T == Kinds.Worker);
    Check("one enlisted at the citadel is paid for and comes out of its door at once",
        took && paid == Kinds.Worker.Cost && before == 0 && at == 1 && fresh != null && !fresh.Inside,
        $"paid {paid}; ours {before} → {at} at the word, then {(fresh == null ? "none" : fresh.Inside ? "inside" : "out of the door")}");
    // the places: the citadel's ten, and no more taken on past them
    for (int i = Ours(); i < Sim.CitadelPlaces - 1; i++) simM.Spawn(Kinds.Worker, simM.Gate);
    bool lastOne = simM.Enlist(Kinds.Worker);
    string full = simM.CantEnlist(Kinds.Worker);
    var giant = new UnitType { Key = "testgiant", Name = "Великан", Team = Team.Player, Move = Kinds.Worker.Move, Hp = 600, Cost = 10, Room = 4 };
    var locked = new UnitType { Key = "testknight", Name = "Рыцарь", Team = Team.Player, Move = Kinds.Worker.Move, Hp = 200, Cost = 10, Lore = "knights" };
    string shut = simM.CantEnlist(locked);
    simM.Learnt.Add("knights");
    string open = simM.CantEnlist(locked);
    simM.Learnt.Add("barracks");
    string roomy = simM.CantEnlist(giant);
    Check("the citadel takes on no more than it has places for; a kind waits for its lore; barracks make room, a giant takes four",
        lastOne && full != null && full.Contains("мест") && shut != null && shut.Contains("изучите") && open != null && open.Contains("мест") && roomy == null && simM.Places == Sim.CitadelPlaces + 4,
        $"household {simM.Household()} of {Sim.CitadelPlaces}: «{full}»; locked: «{shut}», then «{open}»; with barracks {simM.Places} places, the giant: «{roomy ?? "taken"}»");
    // the drill yard: soldiers on their feet a seventh faster, workers as they were
    float Walked(UnitType kind, bool drilled)
    {
        if (drilled) simM.Learnt.Add("drillyard"); else simM.Learnt.Remove("drillyard");
        var u = simM.Spawn(kind, simM.NearestStand(siteM.KeepX + siteM.KeepR + 60, siteM.KeepZ + 40, 2));
        u.Pace = 1;
        float x0 = u.X, z0 = u.Z;
        simM.GoTo(u, simM.NearestStand(siteM.KeepX + siteM.KeepR + 220, siteM.KeepZ + 40, 2));
        u.Hold = true;
        RunM(4);
        float d = MathF.Sqrt((u.X - x0) * (u.X - x0) + (u.Z - z0) * (u.Z - z0));
        u.Dead = true; u.DiedAt = -999;
        return d;
    }
    float plainSword = Walked(Kinds.Swordsman, false), drilledSword = Walked(Kinds.Swordsman, true), drilledWorker = Walked(Kinds.Worker, true), plainWorker = Walked(Kinds.Worker, false);
    Check("a drill yard puts soldiers on their feet a seventh faster, and the workers as they were",
        drilledSword > plainSword * 1.1f && Math.Abs(drilledWorker - plainWorker) < plainWorker * .03f,
        $"a swordsman in 4 s {plainSword:0} → {drilledSword:0} voxels, a worker {plainWorker:0} → {drilledWorker:0}");
}

// ---- dwellings: a room closed all round, roofed, with a door, furnished by the workers, and a place for each bunk
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--dwelling")
{
    Console.WriteLine();
    var mpD = new MapParams();
    var wD = new VoxelWorld(mpD.SizeX, mpD.SizeY, mpD.SizeZ);
    var siteD = MapGen.Generate(wD, mpD);
    wD.Compact();
    var simD = new Sim(wD, siteD);
    simD.Waves.Countdown = 1e9; simD.WildTarget = 0;
    void RunD(double seconds) { for (double t = 0; t < seconds; t += .05) simD.Step(.05f); }
    var (gxD, _, gzD) = simD.Nav.Feet(simD.Gate);
    // a house of stone 6 by 9 m outside, walls a metre thick and four high, roofed with timber (which spans the
    // 3.75 m between its walls; stone would fall in), on the nearest flat dry ground west of the citadel
    int hx = -1, hz = -1;
    for (int r = 120; r < 600 && hx < 0; r += 16)
        for (int a = 0; a < 24 && hx < 0; a++)
        {
            int cx = (int)gxD - (int)(r * Math.Cos(a * Math.PI / 12)), cz = (int)gzD + (int)(r * Math.Sin(a * Math.PI / 12));
            int lo = int.MaxValue, hi = int.MinValue; bool wet = false;
            for (int z = cz - 48; z <= cz + 48; z += 4)
                for (int x = cx - 56; x <= cx + 56; x += 4)
                {
                    if (x < 0 || z < 0 || x >= wD.SX || z >= wD.SZ || siteD.Forbidden(x, z)) { wet = true; continue; }
                    int g = wD.SurfAt(x, z); lo = Math.Min(lo, g); hi = Math.Max(hi, g);
                    if (wD.Get(x, g, z) == Mat.Water || wD.Get(x, g - 1, z) == Mat.Water) wet = true;
                }
            if (!wet && hi - lo <= 2) (hx, hz) = (cx, cz);
        }
    int ytop = Shapes.LevelTop(wD, hx, hz, 32);
    simD.InstantLabour = true;
    simD.DesignateBuild(Shapes.Box(wD, siteD, hx - 23, hz - 36, hx + 22, hz + 35, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Level = true, Hollow = true }, ytop));
    simD.InstantLabour = false;
    RunD(1);
    var noRoof = simD.CheckDwelling(hx - 23, hz - 36, hx + 22, hz + 35);
    for (int z = hz - 36; z <= hz + 35; z++) for (int x = hx - 23; x <= hx + 22; x++) for (int y = ytop; y < ytop + 4; y++) wD.Set(x, y, z, Mat.Wood);
    RunD(1);
    var noDoor = simD.CheckDwelling(hx - 23, hz - 36, hx + 22, hz + 35);
    // a gate in its south wall, clicked from outside
    int fy = ytop - 26, fz = hz;
    while (wD.Get(hx, fy, fz) == Mat.Air) fz++;
    while (wD.Get(hx, fy, fz) != Mat.Air) fz++;
    var face = new VoxelHit { X = hx, Y = fy, Z = fz - 1, NZ = 1, M = wD.Get(hx, fy, fz - 1) };
    var plan = GatePlan.At(wD, siteD, face);
    var door = plan == null ? null : simD.AddGate(plan, built: true);
    RunD(1);
    var ok = simD.CheckDwelling(hx - 23, hz - 36, hx + 22, hz + 35);
    Check("a room is a dwelling only once it is roofed and has its door; then it has floor and bunks enough",
        noRoof is { Ok: false } && noRoof.Why.Contains("крыш") && noDoor is { Ok: false } && noDoor.Why.Contains("двер") && door != null && ok.Ok && ok.Area > 18 && ok.Bunks >= 4,
        $"without a roof: «{noRoof.Why}»; without a door: «{noDoor.Why}»; with both: {(ok.Ok ? "a dwelling" : "«" + ok.Why + "»")}, {ok.Area:0.#} m², {ok.Bunks} bunks; " +
        $"the gate {(plan == null ? "does not fit" : door == null ? "not hung" : $"hung, ready {door.Ready}, leaves {(door.In > 0 ? "outside" : "inside")}")}");
    // set aside as a dwelling with a crew inside: they furnish it, and its bunks are places in the household
    var home = simD.AddZone(hx - 26, hz - 40, hx + 25, hz + 39, Zone.Dwelling, 0);
    int inside = simD.NearestStand(hx, hz - 10, 2);
    for (int i = 0; i < 4; i++) simD.Spawn(Kinds.Worker, inside);
    double wood0 = simD.Stock[Stockpile.Wood];
    int places0 = simD.Places;
    for (int t = 0; t < 300 && home.Beds < ok.Bunks; t++) RunD(1);
    int bunk = 0, bedding = 0, table = 0;
    for (int z = hz - 36; z <= hz + 35; z++) for (int x = hx - 23; x <= hx + 22; x++) for (int y = ytop - 34; y < ytop; y++)
    {
        byte m = wD.Get(x, y, z);
        if (m == Mat.Bunk) bunk++; else if (m == Mat.Bedding) bedding++; else if (m is Mat.Table or Mat.Bench) table++;
    }
    Check("the workers furnish a dwelling of timber, bunks along the walls (and a table in the middle if there is room), and every whole bunk is a place",
        home.Closed && home.Beds == ok.Bunks && simD.Places == places0 + home.Beds && bunk > 0 && bedding > 0 && simD.Stock[Stockpile.Wood] < wood0,
        $"{home.Beds} of {ok.Bunks} bunks whole ({bunk} voxels of frame, {bedding} of bedding, {table} of table and benches), timber {Fmt.M3(wood0 - simD.Stock[Stockpile.Wood])} m³; places {places0} → {simD.Places}");
    // the same house framed again: still one house, its bunks counted once
    int placesOnce = simD.Places;
    simD.AddZone(hx - 30, hz - 44, hx + 29, hz + 43, Zone.Dwelling, 0);
    RunD(6);
    Check("a house framed twice over is one house: its bunks are places once", simD.Places == placesOnce, $"places {placesOnce} → {simD.Places}");
    // a hole hacked through its wall: it is open, and its places are lost till it is closed again
    for (int y = ytop - 32; y < ytop - 8; y++) for (int z = hz - 8; z < hz + 8; z++) for (int x = hx - 23; x < hx - 15; x++) wD.Set(x, y, z, Mat.Air);
    RunD(6);
    // (the timber roof over the breach comes down in part too, having lost the wall under it)
    Check("a dwelling broken open gives no places till it is closed again", !home.Closed && simD.Places == Sim.CitadelPlaces && (home.Why.Contains("стен") || home.Why.Contains("крыш")),
        $"closed {home.Closed}: «{home.Why}», places {simD.Places}");
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

    // the figures are sculpted in cells of 3 cm, the blows cut half-voxels made of them: every look's half-voxels, sculpted
    // two cells to one without the finer work, are the half-voxels of the same look sculpted in half-voxels — the same
    // tissue, the same brain, heart and joints — so the wounds cut what they always cut
    int parityLooks = 0, parityCells = 0;
    var parityOff = new System.Collections.Generic.List<string>();
    var everyKind = Kinds.PlayerKinds.Concat(Kinds.HordeKinds).Concat(Kinds.WildKinds)
        .Concat(new[] { Kinds.Catapult, Kinds.Mantlet, Kinds.SiegeTower, Kinds.Risen, Kinds.Thrall, Kinds.Shaman, Kinds.Witch, Kinds.Necromancer, Kinds.Alchemist }).Distinct().ToArray();
    foreach (var t in everyKind)
        for (int l = 0; l < Bodies.Looks(t); l++)
        {
            var one = Bodies.Sculpted(t, l, 1, false);
            var two = Bodies.Sculpted(t, l, 2, false);
            parityLooks++;
            bool same = one.Brain == two.Brain && one.Heart == two.Heart && one.Top == two.Top && one.HeadY0 == two.HeadY0 && one.HeadY1 == two.HeadY1;
            for (int p = 0; p < BodyPlan.Parts && same; p++)
            {
                var a = one.Grid[p]; var b = two.Grid[p];
                same = a == null ? b == null : b != null && a.X0 == b.X0 && a.Y0 == b.Y0 && a.Z0 == b.Z0 && a.SX == b.SX && a.SY == b.SY && a.SZ == b.SZ
                    && a.Count == b.Count && a.M.AsSpan().SequenceEqual(b.M) && a.T.AsSpan().SequenceEqual(b.T)
                    && one.JX[p] == two.JX[p] && one.JY[p] == two.JY[p] && one.JZ[p] == two.JZ[p];
                // and every cell of the finer body lies in a half-voxel of it
                if (same && two.Fine[p] is { } fine)
                    for (int i = 0; i < fine.M.Length; i++)
                    {
                        if (fine.M[i] == 0) continue;
                        parityCells++;
                        int fx = i % fine.SX + fine.X0, fz = i / fine.SX % fine.SZ + fine.Z0, fy = i / (fine.SX * fine.SZ) + fine.Y0;
                        if (b.Mat(fx >> 1, fy >> 1, fz >> 1) == 0) { same = false; break; }
                    }
            }
            if (!same) parityOff.Add($"{t.Key}{l}");
        }
    Check("a body sculpted in cells of 3 cm is, half-voxel for half-voxel, the body sculpted in half-voxels: the wounds cut what they always cut",
        parityLooks > 90 && parityOff.Count == 0, $"{parityLooks} looks, {parityCells} cells; differing: {string.Join(", ", parityOff.Take(8))}");

    // and sculpted finer, every piece of every body of every kind hangs together with its part by a face (else the
    // first wound would knock it off), what the hand holds is a piece of dress with its grip in the fist and its point
    // out beyond it, and a figure near is no more than a few thousand faces
    int looseAll = 0, armed = 0, unarmed = 0, heavy = 0, maxQuads = 0;
    var looseWhere = new System.Collections.Generic.List<string>();
    string heaviest = "";
    var mesherB = new GreedyMesher();
    foreach (var t in everyKind)
        for (int l = 0; l < Bodies.Looks(t); l++)
        {
            var plan = Bodies.Plan(t, l);
            // (an engine's timbers are not cut by blows)
            bool engine = t.Key is "ram" or "catapult" or "mantlet" or "tower";
            for (int p = 0; p < BodyPlan.Parts && !engine; p++)
            {
                var g = plan.Grid[p];
                if (g == null) continue;
                float jx = plan.JX[p] * BodyPlan.Sub, jy = plan.JY[p] * BodyPlan.Sub, jz = plan.JZ[p] * BodyPlan.Sub;
                int seed = -1; float best = float.MaxValue;
                for (int i = 0; i < g.M.Length; i++)
                {
                    if (g.M[i] == 0 || g.T[i] == (byte)Tissue.Gear) continue;
                    float x = i % g.SX + g.X0 + .5f - jx, z = i / g.SX % g.SZ + g.Z0 + .5f - jz, y = i / (g.SX * g.SZ) + g.Y0 + .5f - jy;
                    if (x * x + y * y + z * z < best) { best = x * x + y * y + z * z; seed = i; }
                }
                var kept = new bool[g.M.Length];
                var q = new System.Collections.Generic.Queue<int>();
                if (seed >= 0) { q.Enqueue(seed); kept[seed] = true; }
                while (q.Count > 0)
                {
                    int i = q.Dequeue(), x = i % g.SX, z = i / g.SX % g.SZ, y = i / (g.SX * g.SZ);
                    foreach (var (ax, ay, az) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                    {
                        int nx = x + ax, ny = y + ay, nz = z + az;
                        if (nx < 0 || ny < 0 || nz < 0 || nx >= g.SX || ny >= g.SY || nz >= g.SZ) continue;
                        int n = (ny * g.SZ + nz) * g.SX + nx;
                        if (g.M[n] == 0 || kept[n]) continue;
                        kept[n] = true; q.Enqueue(n);
                    }
                }
                for (int i = 0; i < g.M.Length; i++)
                    if (g.M[i] != 0 && !kept[i] && looseAll++ < 6) looseWhere.Add($"{t.Key}{l} {(BodyPart)p} at {i % g.SX + g.X0},{i / (g.SX * g.SZ) + g.Y0},{i / g.SX % g.SZ + g.Z0} ({Mat.NameOf(g.M[i])})");
            }
            if (plan.Weapon is { } wpn)
            {
                var hand = plan.Grid[(int)wpn.On];
                bool inFist = hand.Mat((int)MathF.Floor(wpn.Grip.X * BodyPlan.Sub), (int)MathF.Floor(wpn.Grip.Y * BodyPlan.Sub), (int)MathF.Floor(wpn.Grip.Z * BodyPlan.Sub)) != 0;
                float reach = MathF.Sqrt((wpn.Tip.X - wpn.Grip.X) * (wpn.Tip.X - wpn.Grip.X) + (wpn.Tip.Y - wpn.Grip.Y) * (wpn.Tip.Y - wpn.Grip.Y) + (wpn.Tip.Z - wpn.Grip.Z) * (wpn.Tip.Z - wpn.Grip.Z));
                if (inFist && reach > 1) armed++; else looseWhere.Add($"{t.Key}{l}: grip in fist {inFist}, reach {reach:0.0}");
            }
            else if (!plan.Beast && !engine) unarmed++;
            int nearQuads = 0;
            foreach (var g in plan.Fine) if (g != null) nearQuads += mesherB.Mesh(g.Padded(), g.SX, g.SY, g.SZ, g.X0, g.Y0, g.Z0)?.Quads ?? 0;
            foreach (var d in plan.Dress) nearQuads += mesherB.Mesh(d.Grid.Padded(), d.Grid.SX, d.Grid.SY, d.Grid.SZ, d.Grid.X0, d.Grid.Y0, d.Grid.Z0)?.Quads ?? 0;
            if (nearQuads > maxQuads) { maxQuads = nearQuads; heaviest = $"{t.Key}{l}"; }
            if (nearQuads > 6000) heavy++;
        }
    Check("every piece of every finer body hangs together with its part, the hand holds its weapon by the grip, and none is too heavy to draw near",
        looseAll == 0 && unarmed == 0 && armed > 80 && heavy == 0,
        $"{looseAll} loose half-voxels, {armed} armed, {unarmed} unarmed with a hand; the heaviest near {heaviest}, {maxQuads} quads; {string.Join("; ", looseWhere.Take(6))}");

    // drawn in half-voxels (the middle distance), every half-voxel a weapon or a shield fills enough of is drawn — none
    // left out at the fist, where a knuckle has it and too little of it — and a painted shield shows its paint there,
    // not the planks under it
    int holes = 0, shieldHalves = 0, bare = 0;
    var holeWhere = new System.Collections.Generic.List<string>();
    foreach (var t in everyKind)
        for (int l = 0; l < Bodies.Looks(t); l++)
        {
            var plan = Bodies.Plan(t, l);
            for (int d = 0; d < plan.Dress.Count; d++)
            {
                var dress = plan.Dress[d];
                int p = (int)dress.On, r = dress.Sub / BodyPlan.Sub;
                var g = plan.Grid[p]; var dg = dress.Grid;
                var solid = plan.Solid[p]; var owner = plan.Owner[p]; var shown = plan.Shown[p];
                for (int i = 0; i < g.M.Length; i++)
                {
                    if (g.M[i] == 0) continue;
                    int x = i % g.SX + g.X0, z = i / g.SX % g.SZ + g.Z0, y = i / (g.SX * g.SZ) + g.Y0, n = 0;
                    for (int dy = 0; dy < r; dy++) for (int dz = 0; dz < r; dz++) for (int dx = 0; dx < r; dx++)
                        if (dg.Mat(x * r + dx, y * r + dy, z * r + dz) != 0) n++;
                    if (n >= Math.Max(1, r * r * r / 16) && !solid[i] && holes++ < 6) holeWhere.Add($"{t.Key}{l} {(BodyPart)p} {x},{y},{z}");
                    if (dress.Kind != DressKind.Shield || owner[i] != d + 1 || !solid[i]) continue;
                    shieldHalves++;
                    byte m = shown != null && shown[i] != 0 ? shown[i] : g.M[i];
                    if (m is Mat.Board or Mat.BoardDark) bare++;
                }
            }
        }
    Check("drawn in half-voxels, every half-voxel a weapon or a shield fills is drawn, the haft on into the fist, and a shield shows its paint and not its planks",
        holes == 0 && shieldHalves > 300 && bare * 8 < shieldHalves,
        $"{holes} left out ({string.Join("; ", holeWhere)}); of {shieldHalves} half-voxels of shields {bare} bare planks");

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

// ---- the strokes: a blow comes one of many ways, chosen ahead by what the striker holds and whom it strikes and never
// the same twice running; the wound is cut where the blade's own sweep meets the body and along that sweep; one gone
// into strikes the way the player swung the mouse
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--strokes")
{
    Console.WriteLine();
    var mpK = new MapParams();
    var wK = new VoxelWorld(mpK.SizeX, mpK.SizeY, mpK.SizeZ);
    var siteK = MapGen.Generate(wK, mpK);
    wK.Compact();
    // a field of level ground west of the citadel, bare of trees
    int kx0 = (siteK.KeepX - 460) & ~7, kz0 = (siteK.KeepZ - 140) & ~7, kg = wK.SurfAt(kx0 + 160, kz0 + 140);
    for (int z = kz0; z < kz0 + 280; z++)
        for (int x = kx0; x < kx0 + 320; x++)
            for (int y = kg - 4; y < kg + 50; y++) wK.Set(x, y, z, y < kg ? Mat.Soil : Mat.Air);
    Sim Field()
    {
        var s = new Sim(wK, siteK);
        s.Waves.Countdown = 1e9; s.WildTarget = 0;
        for (double t = 0; t < .5; t += .05) s.Step(.05f);
        return s;
    }
    var simK = Field();
    void RunK(double seconds) { for (double t = 0; t < seconds; t += .05) simK.Step(.05f); }
    var smitten = new List<Smiting>();
    simK.Smote += h => smitten.Add(h);
    float row = kz0 + 20;
    // two set face to face on the field, the struck one held where it stands and hard to kill
    (Unit a, Unit b) Pair(Sim s, UnitType ta, UnitType tb, float gap = -1)
    {
        float x = kx0 + 60, z = row;
        row += 7; if (row > kz0 + 260) row = kz0 + 20;
        var a = s.Spawn(ta, s.NearestStand((int)x, (int)z, ta.Move.Height));
        float d = gap > 0 ? gap : ta.Reach + tb.BodyR - 1;
        var b = s.Spawn(tb, s.NearestStand((int)(x + d), (int)z, tb.Move.Height));
        a.X = x; a.Z = z; a.Y = kg; b.X = x + d; b.Z = z; b.Y = kg;
        a.Look = b.Look = 0;
        a.Facing = 0; b.Facing = MathF.PI;
        b.Hp = b.MaxHp = 1e6f;
        return (a, b);
    }
    void Done(params Unit[] us) { foreach (var u in us) { u.Dead = true; u.DiedAt = -999; } }
    // struck over and over, the body made whole again before each
    List<Smiting> Rain(Sim s, Unit a, Unit b, int n, StrokeKind? only = null)
    {
        var got = new List<Smiting>();
        void On(Smiting h) => got.Add(h);
        s.Smote += On;
        for (int i = 0; i < n; i++)
        {
            b.Wounds = null; b.Dead = false; b.State = UnitState.Idle; b.Hp = b.MaxHp; b.Airborne = null;
            if (only is { } k) { a.NextStroke = k; a.StrokeFor = b; }
            s.Strike(a, b);
        }
        s.Smote -= On;
        b.Wounds = null; b.Dead = false; b.Hp = b.MaxHp;
        return got;
    }
    string Tally(List<Smiting> hs) => string.Join(" ", hs.GroupBy(h => h.Stroke).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"));
    int Repeats(List<Smiting> hs) { int r = 0; for (int i = 1; i < hs.Count; i++) if (hs[i].Stroke == hs[i - 1].Stroke) r++; return r; }

    // the ways a blow comes, by the weapon: a sword every way, an axe never with the point, a spear mostly with it, a
    // rider only down from the saddle, a hound with its teeth, a shieldbearer close up with his shield too
    int met0 = simK.BlowsMet, blows0 = simK.Blows;
    var (swA, swB) = Pair(simK, Kinds.Swordsman, Kinds.Orc); var swH = Rain(simK, swA, swB, 60);
    var (axA, axB) = Pair(simK, Kinds.Orc, Kinds.Swordsman); var axH = Rain(simK, axA, axB, 40);
    var (spA, spB) = Pair(simK, Kinds.Spearman, Kinds.Goblin); var spH = Rain(simK, spA, spB, 40);
    var (riA, riB) = Pair(simK, Kinds.Rider, Kinds.Goblin); var riH = Rain(simK, riA, riB, 30);
    var (hoA, hoB) = Pair(simK, Kinds.Hound, Kinds.Goblin); var hoH = Rain(simK, hoA, hoB, 20);
    var (shA, shB) = Pair(simK, Kinds.Shieldbearer, Kinds.Goblin, 6); var shH = Rain(simK, shA, shB, 40);
    int kinds = swH.Select(h => h.Stroke).Distinct().Count();
    float spearThrusts = spH.Count(h => h.Stroke == StrokeKind.Thrust) / (float)spH.Count;
    bool riderDown = riH.All(h => h.Stroke is StrokeKind.Overhead or StrokeKind.DiagR) && riH.Average(h => h.DY) < -.2f;
    float met = (simK.BlowsMet - met0) / (float)(simK.Blows - blows0);
    Check("a blow comes one of many ways by what strikes it, never the same twice running but a spear's thrust: a sword every way, an axe never with the point, a spear mostly with it, a rider down from the saddle, a hound with its teeth, a shield driven in close",
        kinds >= 6 && Repeats(swH) + Repeats(axH) + Repeats(shH) == 0 && axH.All(h => h.Stroke != StrokeKind.Thrust) && spearThrusts > .6f && riderDown
        && hoH.All(h => h.Stroke == StrokeKind.Bite) && shH.Any(h => h.Stroke == StrokeKind.Bash) && met > .9f,
        $"sword: {Tally(swH)}; axe: {Tally(axH)}; spear: {Tally(spH)}; rider: {Tally(riH)}, coming down {riH.Average(h => h.DY):0.00}; hound: {Tally(hoH)}; shield: {Tally(shH)}; " +
        $"{Repeats(swH) + Repeats(axH) + Repeats(shH)} repeated; {met * 100:0} % met the body along their sweep");
    Done(swA, swB, axA, axB, spA, spB, riA, riB, hoA, hoB, shA, shB);

    // where it lands is where the blade goes: down on the head from over it, across at the neck, up under the guard
    var (hA, hB) = Pair(simK, Kinds.Swordsman, Kinds.Orc);
    float Height(StrokeKind k) => Rain(simK, hA, hB, 12, k).Average(h => (h.Y - hB.Y) / hB.T.BodyH);
    float hOver = Height(StrokeKind.Overhead), hSweep = Height(StrokeKind.Sweep), hRise = Height(StrokeKind.Rising);
    // the wound runs the way the blade did: seen along the blade, an overhead's stands upright, taller than it is wide
    // across the body, and a sweep's lies flat, wider than tall. Each wound is taken slice by slice from the front of the
    // body to its back, the way the blade points in both, and the slices averaged, so that the edge's slant along the
    // blade is not taken for height: a sweep at the neck comes up to it at a slope and its wound rises two or three
    // half-voxels from back to front, so that over its whole box a sweep through an orc's jaw is as tall as it is deep
    // (3.2 by 2.9 over sixty blows), its section 2.3 across by 1.4 up, an overhead's 1.6 by 2.8. (Every orc's chest is
    // under rusted plate or a chain now, and a sweep at the chest rings off it and leaves nothing to measure.)
    (float across, float up) Span(StrokeKind k, int n)
    {
        float sx = 0, sy = 0; int cuts = 0;
        var plan = Bodies.Plan(hB.T, hB.Look);
        var cut = new List<(int x, int y, int z)>();
        for (int i = 0; i < n; i++)
        {
            hB.Wounds = null; hB.Dead = false; hB.Hp = hB.MaxHp;
            hA.NextStroke = k; hA.StrokeFor = hB;
            simK.Strike(hA, hB);
            if (hB.Wounds == null || hB.Wounds.Wounds.Count > 0) continue;
            cut.Clear();
            for (int p = 0; p < BodyPlan.Parts; p++)
            {
                var g0 = plan.Grid[p]; var g1 = hB.Wounds.Grid[p];
                if (g0 == null) continue;
                for (int q = 0; q < g0.M.Length; q++)
                {
                    if (g0.M[q] == 0 || g0.T[q] == (byte)Tissue.Gear || g1 != null && g1.M[q] != 0) continue;
                    cut.Add((q % g0.SX + g0.X0, q / (g0.SX * g0.SZ) + g0.Y0, q / g0.SX % g0.SZ + g0.Z0));
                }
            }
            if (cut.Count == 0) continue;
            float across = 0, up = 0; int slices = 0;
            foreach (var slice in cut.GroupBy(c => c.z))
            {
                across += slice.Max(c => c.x) - slice.Min(c => c.x) + 1; up += slice.Max(c => c.y) - slice.Min(c => c.y) + 1; slices++;
            }
            sx += across / slices; sy += up / slices; cuts++;
        }
        return cuts == 0 ? (0, 0) : (sx / cuts, sy / cuts);
    }
    var overSpan = Span(StrokeKind.Overhead, 10); var sweepSpan = Span(StrokeKind.Sweep, 10);
    Check("a blow lands where the blade's sweep meets the body, and cuts along it: down through the head from over it, across the neck, up under the guard; seen along the blade, an overhead wound taller than wide, a sweep's wider than tall",
        hOver > .8f && hOver > hSweep + .1f && hSweep > hRise + .15f && overSpan.up > overSpan.across * 1.4f && sweepSpan.across > sweepSpan.up,
        $"struck at {hOver:0.00}, {hSweep:0.00}, {hRise:0.00} of an orc's height; seen along the blade, an overhead cut {overSpan.across:0.0} across by {overSpan.up:0.0} up, a sweep {sweepSpan.across:0.0} by {sweepSpan.up:0.0} (half-voxels)");
    Done(hA, hB);

    // a sweep at the neck can take the head off; a cut from over the head splits it instead. The height and the side
    // of each blow are given, not drawn — the sweep's at the neck, the overhead's about the middle — so that what the
    // checks before this one strike does not decide it. Six sweeps at the most: over 960 goblins, 120 of each look, the
    // knight's sweeps at the neck take the head off within five 70 % of the time and within six 87 % (five did it 89 %
    // of the time when the figures were sculpted in half-voxels): his longsword, sculpted finer, is a half-voxel shorter,
    // the string of bones round a goblin's throat is cut through with the neck, and one holds up a pot lid
    int heads = 0, split = 0, overHeads = 0;
    for (int i = 0; i < 12; i++)
    {
        var (a, b) = Pair(simK, Kinds.Knight, Kinds.Goblin, Kinds.Knight.Reach + Kinds.Goblin.BodyR - 1 - i % 3 * 1.5f);
        b.Look = i % Bodies.Looks(b.T);
        float Spread(int j, float golden) => j * golden % 1;
        for (int n = 0; n < 6 && !b.Dead; n++) { a.NextStroke = StrokeKind.Sweep; a.StrokeFor = b; simK.Strike(a, b, .6f + .3f * Spread(i * 6 + n, .618f), .3f + .4f * Spread(i * 7 + n * 3, .382f)); }
        if (b.Wounds?.Wounds.Contains("отрублена голова") == true) heads++;
        var (c, d) = Pair(simK, Kinds.Knight, Kinds.Goblin, Kinds.Knight.Reach + Kinds.Goblin.BodyR - 1 - i % 3 * 1.5f);
        d.Look = i % Bodies.Looks(d.T);
        for (int n = 0; n < 5 && !d.Dead; n++) { c.NextStroke = StrokeKind.Overhead; c.StrokeFor = d; simK.Strike(c, d, .3f + .3f * Spread(i * 5 + n, .618f), .3f + .4f * Spread(i * 7 + n * 3, .382f)); }
        if (d.Wounds?.Wounds.Contains("отрублена голова") == true) overHeads++;
        if (d.Wounds?.Wounds.Contains("разбита голова") == true) split++;
        Done(a, b, c, d);
    }
    Check("a knight's sweep at the neck takes a goblin's head off; his cut from over the head splits it instead", heads >= 8 && overHeads == 0 && split >= 7,
        $"heads off {heads} of 12 by six sweeps at the neck, {overHeads} by five overheads, which split {split}");

    // what is carried on the back and touches the head too (a quiver, a pack, a ladder, a sack) does not hold a head on
    // that the neck no longer holds: it stays with the back it is slung on. The neck is cut clean through under the jaw,
    // all of it but what is carried, and what falls is looked at — real blows at the neck meet the shoulders (the head
    // sits on their line), the heart under them and the brain over them first, and are the checks above
    var carriers = new[] { Kinds.Archer, Kinds.Miner, Kinds.Sapper, Kinds.Thief };
    var headsOf = new int[carriers.Length];
    int slung = 0;
    for (int ci = 0; ci < carriers.Length; ci++)
        for (int i = 0; i < Bodies.Looks(carriers[ci]); i++)
        {
            var t = carriers[ci];
            var (a, b) = Pair(simK, Kinds.Knight, t, 40);
            b.Look = i;
            var body = b.Wounds = new BodyState(Bodies.Plan(t, i));
            var g0 = body.Grid[0];
            int under = body.Plan.HeadY0 - 1;
            for (int z = g0.Z0; z < g0.Z0 + g0.SZ; z++)
                for (int x = g0.X0; x < g0.X0 + g0.SX; x++)
                    if (g0.Mat(x, under, z) != 0 && g0.Kind(x, under, z) != Tissue.Gear) g0.Clear(x, under, z);
            simK.Sever(b, body);
            if (body.Wounds.Contains("отрублена голова")) headsOf[ci]++;
            for (int k = 0; k < g0.M.Length; k++) if (g0.T[k] == (byte)Tissue.Gear) { slung++; break; }
            Done(a, b);
        }
    Check("a neck cut through takes the head off one with a quiver, a pack, a ladder or a sack on his back, and what is slung stays on the back", carriers.Select((t, ci) => headsOf[ci] == Bodies.Looks(t)).All(x => x) && slung == carriers.Sum(t => Bodies.Looks(t)),
        $"heads off: {string.Join(", ", carriers.Select((t, ci) => $"{t.Name} {headsOf[ci]} of {Bodies.Looks(t)}"))}; what is carried still on {slung} of them");

    // a sliver off the fingers is no arm lost; cut through above the elbow, it is (a friend saw «отрублена левая рука»
    // over an arm still there)
    string CutArm(bool high)
    {
        var (a, b) = Pair(simK, Kinds.Knight, Kinds.Swordsman, 40);
        var body = b.Wounds = new BodyState(Bodies.Plan(b.T, b.Look));
        var g = body.Grid[(int)BodyPart.Off];
        int lo = int.MaxValue, hi = int.MinValue;
        for (int y = g.Y0; y < g.Y0 + g.SY; y++)
            for (int z = g.Z0; z < g.Z0 + g.SZ; z++)
                for (int x = g.X0; x < g.X0 + g.SX; x++)
                    if (g.Mat(x, y, z) != 0 && g.Kind(x, y, z) != Tissue.Gear) { lo = Math.Min(lo, y); hi = Math.Max(hi, y); }
        int at = high ? (lo + hi) / 2 + 2 : lo + 1;
        for (int z = g.Z0; z < g.Z0 + g.SZ; z++)
            for (int x = g.X0; x < g.X0 + g.SX; x++)
                if (g.Mat(x, at, z) != 0 && g.Kind(x, at, z) != Tissue.Gear) g.Clear(x, at, z);
        simK.Sever(b, body);
        string said = string.Join(", ", body.Wounds);
        Done(a, b);
        return said;
    }
    string sliver = CutArm(false), arm = CutArm(true);
    Check("a sliver off the fingers is not told as the arm cut off; the arm cut through above the elbow is",
        !sliver.Contains("рука") && arm.Contains("отрублена левая рука"), $"the fingers: «{sliver}»; the arm: «{arm}»");

    // the same valley, the same blows: the strokes are chosen without the dice of the simulation
    List<(StrokeKind, float)> Bout(Sim s)
    {
        var a = s.Spawn(Kinds.Swordsman, s.NearestStand(kx0 + 200, kz0 + 140, 2));
        var b = s.Spawn(Kinds.Orc, s.NearestStand(kx0 + 210, kz0 + 140, 2));
        a.X = kx0 + 200; a.Z = kz0 + 140; b.X = kx0 + 210; b.Z = kz0 + 140; a.Y = b.Y = kg; a.Facing = 0; b.Facing = MathF.PI;
        b.Hp = b.MaxHp = 1e6f;
        return Rain(s, a, b, 24).Select(h => (h.Stroke, h.Y)).ToList();
    }
    var bout1 = Bout(Field()); var bout2 = Bout(Field());
    Check("the same valley gives the same strokes in the same order, landing in the same spots", bout1.Count == 24 && bout1.SequenceEqual(bout2),
        $"{string.Join(" ", bout1.Take(8).Select(b => b.Item1))}…; the same {bout1.Zip(bout2).Count(p => p.First == p.Second)} of {bout1.Count}");

    // one gone into strikes the way the player swung the mouse just before: down, from over the head; across to the
    // left, a sweep; held still, the point; a spearman wishing a cut from over the head thrusts, as a spear does
    var (pA, pB) = Pair(simK, Kinds.Swordsman, Kinds.Orc);
    pB.Guard = true; pB.Home = pB.Cell; pB.ChainedUntil = 1e9;
    simK.Possess(pA);
    StrokeKind Swung(float right, float down)
    {
        simK.WishStrike = false;
        simK.LookYaw = MathF.Atan2(pB.Z - pA.Z, pB.X - pA.X) - right / 2; simK.LookPitch = down / 2;
        RunK(1.4);
        for (int i = 0; i < 4; i++) { simK.LookYaw += right / 4; simK.LookPitch -= down / 4; simK.Step(.05f); }
        double t0 = pA.StrokeTime;
        simK.WishStrike = true;
        simK.Step(.05f);
        simK.WishStrike = false;
        return pA.StrokeTime > t0 ? pA.Stroke : (StrokeKind)255;
    }
    var down = Swung(0, .3f); var left = Swung(-.35f, 0); var still = Swung(0, 0); var downLeft = Swung(-.25f, .25f);
    simK.Unpossess(null);
    var (qA, qB) = Pair(simK, Kinds.Spearman, Kinds.Orc);
    qB.Guard = true; qB.Home = qB.Cell; qB.ChainedUntil = 1e9;
    simK.Possess(qA);
    pA = qA; pB = qB;
    var spearDown = Swung(0, .3f);
    simK.Unpossess(null);
    Check("one gone into strikes the way the mouse swung just before the click: down from over the head, to the left a sweep, still the point, down and to the left the forehand; a spear thrusts whatever the swing",
        down == StrokeKind.Overhead && left == StrokeKind.Sweep && still == StrokeKind.Thrust && downLeft == StrokeKind.DiagR && spearDown == StrokeKind.Thrust,
        $"down {down}, left {left}, still {still}, down-left {downLeft}; the spearman down {spearDown}");
    Done(qA, qB);
}

// ---- the grass: tufts and flowers handed to the game to draw in blades, not meshed as cubes; turf burnt black by fire
// and by a blast, and the grass creeping back over it
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--grass")
{
    Console.WriteLine();
    var mpG = new MapParams();
    var wG = new VoxelWorld(mpG.SizeX, mpG.SizeY, mpG.SizeZ);
    var siteG = MapGen.Generate(wG, mpG);
    wG.Compact();
    // open, level turf in the middle of a chunk, with nothing on it but grass, west of the citadel; its tufts and
    // flowers are taken off it so the test has the turf to itself
    bool Open(int x, int z, int r)
    {
        int s0 = wG.SurfAt(x, z);
        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                byte up = wG.Get(x + dx, s0, z + dz);
                if (wG.SurfAt(x + dx, z + dz) != s0 || !Mat.IsGrass(wG.Get(x + dx, s0 - 1, z + dz)) || (up != Mat.Air && !Mat.IsSprig(up))) return false;
            }
        return true;
    }
    int gx = -1, gz = -1;
    for (int z = 48; z < wG.SZ - 48 && gx < 0; z += 32)
        for (int x = siteG.SpawnStrip + 48; x < siteG.KeepX - 60 && gx < 0; x += 32)
        {
            int cx0 = (x & ~31) + 16, cz0 = (z & ~31) + 16, s0 = wG.SurfAt(cx0, cz0);
            if ((s0 & 31) > 2 && (s0 & 31) < 29 && Open(cx0, cz0, 12)) { gx = cx0; gz = cz0; }
        }
    int gs = wG.SurfAt(gx, gz);
    for (int dz = -12; dz <= 12; dz++) for (int dx = -12; dx <= 12; dx++) for (int y = gs; y < gs + 3; y++) if (Mat.IsSprig(wG.Get(gx + dx, y, gz + dz))) wG.Set(gx + dx, y, gz + dz, Mat.Air);
    int gci = wG.ChunkIndex(gx >> 5, gs >> 5, gz >> 5);
    var gm = new GreedyMesher();
    var gpad = new byte[34 * 34 * 34];
    var bare = gm.MeshChunk(wG, gci, gpad, null, out _, out var swardBare);
    float[] bareUv = bare.Uv;
    int bareQuads = bare.Quads;
    wG.Set(gx, gs, gz, Mat.Tuft); wG.Set(gx, gs + 1, gz, Mat.Tuft);
    wG.Set(gx + 3, gs, gz, Mat.Flower);
    var grown = gm.MeshChunk(wG, gci, gpad, null, out _, out var sward);
    int lx = gx & 31, ly = gs & 31, lz = gz & 31;
    int tuft = Array.Find(sward?.Sprigs ?? Array.Empty<int>(), e => Sward.X(e) == lx && Sward.Y(e) == ly && Sward.Z(e) == lz);
    int flower = Array.Find(sward?.Sprigs ?? Array.Empty<int>(), e => Sward.X(e) == lx + 3 && Sward.Y(e) == ly && Sward.Z(e) == lz);
    bool turfUnder = Array.Exists(sward?.Turf ?? Array.Empty<int>(), e => Sward.X(e) == lx && Sward.Y(e) == ly - 1 && Sward.Z(e) == lz);
    Check("a tuft and a flower are handed over to be drawn in blades and leave the chunk's mesh, faces and shading, as it was",
        sward != null && sward.Sprigs.Length == (swardBare.Sprigs?.Length ?? 0) + 2 && Sward.Mat(tuft) == Mat.Tuft && Sward.Tall(tuft) == 2 && Sward.Mat(flower) == Mat.Flower && Sward.Tall(flower) == 1
            && turfUnder && grown.Quads == bareQuads && grown.Uv.AsSpan().SequenceEqual(bareUv) && swardBare.Turf.Length == sward.Turf.Length,
        $"{sward?.Sprigs?.Length ?? 0} sprigs (tuft {Sward.Tall(tuft)} tall), {sward?.Turf?.Length ?? 0} open turf; {grown.Quads} quads with them, {bareQuads} without");

    // a bank under turf: the earth in its side, two voxels of it under the turf, is grassed over; deeper it is earth
    for (int y = gs - 4; y < gs; y++) for (int z = gz - 2; z <= gz + 2; z++) for (int x = gx + 5; x <= gx + 7; x++) wG.Set(x, y, z, Mat.Air);
    var bankMesh = gm.MeshChunk(wG, gci, gpad, null, out _, out _);
    int banked = 0, earthy = 0;
    for (int q = 0; q < bankMesh.Quads; q++)
    {
        float skinQ = bankMesh.Uv2[q * 8 + 1], yQ = bankMesh.Pos[q * 12 + 1];
        bool side = bankMesh.Nrm[q * 12 + 1] == 0;
        int sx = (int)MathF.Round(bankMesh.Pos[q * 12]);
        if (!side || sx < gx + 5 || sx > gx + 8) continue;
        if (skinQ == 9) banked++;
        else if (skinQ == 0) earthy++;
    }
    for (int y = gs - 4; y < gs; y++) for (int z = gz - 2; z <= gz + 2; z++) for (int x = gx + 5; x <= gx + 7; x++)
        wG.Set(x, y, z, y == gs - 1 ? Mat.Grass : Mat.Soil);
    Check("the earth in the side of a bank is grassed over for two voxels under its turf, and is earth below that",
        banked > 0 && earthy > 0, $"{banked} grassed faces, {earthy} of earth in the pit's walls");

    // fire: a tuft burnt leaves the turf under it black, and the grass creeps back over it in the rain
    var simG = new Sim(wG, siteG);
    simG.Waves.Countdown = 1e9; simG.WildTarget = 0;
    void RunG(double seconds) { for (double t = 0; t < seconds; t += .05) simG.Step(.05f); }
    simG.Ignite(gx, gs + 1, gz);
    simG.Ignite(gx, gs, gz);
    RunG(8);
    bool black = wG.Get(gx, gs - 1, gz) == Mat.Scorch && wG.Get(gx, gs, gz) == Mat.Air;
    int scorched = simG.Scorched;
    // a patch seared beside it stays black a good while, and heals from its edges in the rain
    int patch = simG.SearRound(gx + 8, gs, gz, 4);
    RunG(20);
    int left20 = simG.Scorched;
    simG.Weather = Weather.Rain; simG.WeatherLeft = 600;
    RunG(300);
    Check("a tuft burnt leaves the turf under it black; burnt turf stays black a while, and the grass grows back over it from the turf beside it",
        black && scorched > 0 && patch > 20 && left20 > (patch + scorched) * .6 && simG.Scorched == 0 && Mat.IsGrass(wG.Get(gx, gs - 1, gz)),
        $"under the tuft: {(black ? "black" : Mat.NameOf(wG.Get(gx, gs - 1, gz)))}, {scorched} scorched; a patch of {patch} seared beside it, {left20} of all still black 20 s on; after five rainy minutes {simG.Scorched} left, {Mat.NameOf(wG.Get(gx, gs - 1, gz))} under the tuft");

    // a blast sears the open turf round its crater; the bare earth of the crater itself is not turf
    simG.Detonate(gx, gs, gz, 6, 0, "", always: true);
    int seared = 0;
    for (int dz = -12; dz <= 12; dz++) for (int dx = -12; dx <= 12; dx++) if (wG.Get(gx + dx, wG.SurfAt(gx + dx, gz + dz) - 1, gz + dz) == Mat.Scorch) seared++;
    Check("a blast sears the turf round its crater black", seared > 40 && simG.Scorched >= seared, $"{seared} voxels of turf seared within 1.5 m, {simG.Scorched} on the list");
    // the burnt turf is found again in a game read back
    var bufG = new System.IO.MemoryStream();
    simG.Save(bufG, mpG.Seed, "grass", "");
    var wG2 = new VoxelWorld(mpG.SizeX, mpG.SizeY, mpG.SizeZ);
    bufG.Position = 0;
    Sim simG2;
    using (var saveG = new SaveReader(bufG))
    {
        var siteG2 = MapGen.Generate(wG2, new MapParams { Seed = saveG.Seed });
        wG2.Compact();
        saveG.Valley(wG2, siteG2);
        simG2 = new Sim(wG2, siteG2);
        simG2.Load(saveG);
    }
    Check("burnt turf is still waiting to green again in a game read back", simG2.Scorched == simG.Scorched, $"{simG.Scorched} saved, {simG2.Scorched} read back");
}

// ---- the troops the lore opens: their data, their bodies, the ladders they will not take, what each one's lore does
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--troops")
{
    Console.WriteLine();
    // thirteen kinds after the first four, in the citadel's order, each waiting on its lore, with its time and its places
    string[] loresT = { "spearmen", "shieldmen", "halberds", "knights", "slingers", "crossbows", "bombards", "hounds", "riders", "horsearchers", "lancers", "trolls", "giants" };
    var wrongT = new System.Collections.Generic.List<string>();
    for (int i = 0; i < Kinds.Troops.Length; i++)
    {
        var t = Kinds.Troops[i];
        if (t.Team != Team.Player || !(t.Work < 1) || t.Lore != loresT[i] || t.Room < 1 || t.Cost <= 0 || Bodies.Looks(t) < 2
            || Bodies.Plan(t, 0) == Bodies.Plan(Kinds.Worker, 0)) wrongT.Add(t.Key);
    }
    bool orderT = Kinds.PlayerKinds.Length == 17 && Kinds.Troops.Length == 13 && Kinds.Troops.SequenceEqual(Kinds.PlayerKinds.Skip(4));
    Check("thirteen new kinds of ours in the citadel's order, each waiting on its lore, with the places it fills, each in a body of its own",
        orderT && wrongT.Count == 0,
        $"{Kinds.PlayerKinds.Length} kinds of ours; wrong: {string.Join(", ", wrongT)}; " + string.Join(", ", Kinds.Troops.Select(t => $"{t.Key} {t.Lore} ×{t.Room}")));

    // every piece of every body hangs together with its part by a face (else the first wound would knock it off), and
    // nothing of the inside shows; a man's chest in each, but the horse's neck has no heart in it
    int looseT = 0, showingT = 0, looksT = 0;
    var looseAt = new System.Collections.Generic.List<string>();
    foreach (var t in Kinds.Troops)
        for (int l = 0; l < Bodies.Looks(t); l++)
        {
            var plan = Bodies.Plan(t, l);
            looksT++;
            for (int p = 0; p < BodyPlan.Parts; p++)
            {
                var g = plan.Grid[p];
                if (g == null) continue;
                float jx = plan.JX[p] * BodyPlan.Sub, jy = plan.JY[p] * BodyPlan.Sub, jz = plan.JZ[p] * BodyPlan.Sub;
                int seed = -1; float best = float.MaxValue;
                for (int i = 0; i < g.M.Length; i++)
                {
                    if (g.M[i] == 0 || g.T[i] == (byte)Tissue.Gear) continue;
                    float x = i % g.SX + g.X0 + .5f - jx, z = i / g.SX % g.SZ + g.Z0 + .5f - jz, y = i / (g.SX * g.SZ) + g.Y0 + .5f - jy;
                    if (x * x + y * y + z * z < best) { best = x * x + y * y + z * z; seed = i; }
                }
                var kept = new bool[g.M.Length];
                var q = new System.Collections.Generic.Queue<int>();
                if (seed >= 0) { q.Enqueue(seed); kept[seed] = true; }
                while (q.Count > 0)
                {
                    int i = q.Dequeue(), x = i % g.SX, z = i / g.SX % g.SZ, y = i / (g.SX * g.SZ);
                    foreach (var (ax, ay, az) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                    {
                        int nx = x + ax, ny = y + ay, nz = z + az;
                        if (nx < 0 || ny < 0 || nz < 0 || nx >= g.SX || ny >= g.SY || nz >= g.SZ) continue;
                        int n = (ny * g.SZ + nz) * g.SX + nx;
                        if (g.M[n] == 0 || kept[n]) continue;
                        kept[n] = true; q.Enqueue(n);
                    }
                }
                int off = 0;
                string first = "";
                for (int i = 0; i < g.M.Length; i++)
                {
                    if (g.M[i] != 0 && !kept[i] && off++ == 0) first = $" at {i % g.SX + g.X0},{i / (g.SX * g.SZ) + g.Y0},{i / g.SX % g.SZ + g.Z0} ({Mat.NameOf(g.M[i])})";
                    if (g.T[i] is not ((byte)Tissue.Flesh or (byte)Tissue.Bone or (byte)Tissue.Brain or (byte)Tissue.Heart)) continue;
                    int x = i % g.SX + g.X0, z = i / g.SX % g.SZ + g.Z0, y = i / (g.SX * g.SZ) + g.Y0;
                    if (g.Mat(x + 1, y, z) == 0 || g.Mat(x - 1, y, z) == 0 || g.Mat(x, y + 1, z) == 0 || g.Mat(x, y - 1, z) == 0 || g.Mat(x, y, z + 1) == 0 || g.Mat(x, y, z - 1) == 0) showingT++;
                }
                if (off > 0) { looseT += off; looseAt.Add($"{t.Key}{l}:{(BodyPart)p} {off}{first}"); }
            }
        }
    var riderPlan = Bodies.Plan(Kinds.Rider, 0); var houndPlan = Bodies.Plan(Kinds.Hound, 0); var giantPlan = Bodies.Plan(Kinds.Giant, 0); var manPlan = Bodies.Plan(Kinds.Worker, 0);
    Check("the troops' bodies hold together, gear and all, by their faces, none of the inside shows; riders sit horses, the hound goes on four legs, a giant stands three metres and a rider under the gate",
        looseT == 0 && showingT == 0 && riderPlan.Mounted && !riderPlan.Beast && houndPlan.Beast && giantPlan.Top >= 23 && giantPlan.Top <= 25
        && riderPlan.Top <= 24 && riderPlan.Top >= 20 && riderPlan.Heart > 4 && riderPlan.Heart <= manPlan.Heart * 2 && riderPlan.Brain > 20 && houndPlan.Top <= 10,
        $"{looksT} looks; {looseT} half-voxels hanging loose {string.Join(" ", looseAt.Take(6))}; {showingT} inner on the surface; rider: top {riderPlan.Top}, heart {riderPlan.Heart} (a man's {manPlan.Heart}), brain {riderPlan.Brain}; hound top {houndPlan.Top}; giant top {giantPlan.Top}");

    var mpT = new MapParams();
    var wT = new VoxelWorld(mpT.SizeX, mpT.SizeY, mpT.SizeZ);
    var siteT = MapGen.Generate(wT, mpT);
    wT.Compact();
    var simT = new Sim(wT, siteT);
    simT.Waves.Countdown = 1e9; simT.WildTarget = 0;
    void RunT(double seconds) { for (double t = 0; t < seconds; t += .05) simT.Step(.05f); }
    void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
    float Metres(float a, float b, float c, float d) => MathF.Sqrt((a - c) * (a - c) + (b - d) * (b - d)) / VoxelWorld.PerMetre;
    // a field of level ground west of the citadel, bare of trees, and a stone pillar on it with a ladder of ours up its side
    int fx0 = (siteT.KeepX - 460) & ~7, fz0 = (siteT.KeepZ - 140) & ~7, fg = wT.SurfAt(fx0 + 160, fz0 + 140);
    for (int z = fz0; z < fz0 + 280; z++)
        for (int x = fx0; x < fx0 + 320; x++)
            for (int y = fg - 4; y < fg + 50; y++) wT.Set(x, y, z, y < fg ? Mat.Soil : Mat.Air);
    int px0 = fx0 + 40, pz0 = fz0 + 200;
    for (int z = pz0; z < pz0 + 32; z++)
        for (int x = px0; x < px0 + 32; x++)
            for (int y = fg; y < fg + 24; y++) wT.Set(x, y, z, Mat.Stone);
    for (int z = pz0 + 12; z < pz0 + 20; z++)
        for (int y = fg; y < fg + 24; y++) wT.Set(px0 - 1, y, z, Mat.Ladder);
    RunT(.5);
    // a soldier on foot climbs it; a rider and a hound find no way up, and one set on the ladder comes down off it
    int foot = simT.NearestStand(px0 - 12, pz0 + 16, 3), atop = simT.NearestStand(px0 + 16, pz0 + 16, 2);
    int ti0 = simT.Nav.I(atop), tk0 = simT.Nav.K(atop);
    bool Up(UnitType t) => simT.Paths.Find(foot, c => c == atop, c => PathFinder.Octile(simT.Nav.I(c) - ti0, simT.Nav.K(c) - tk0), t.Move, 20000) != null;
    bool spearUp = Up(Kinds.Spearman), riderUp = Up(Kinds.Rider), houndUp = Up(Kinds.Hound), lancerUp = Up(Kinds.Lancer);
    int rung = simT.Nav.CellOf(px0 - 1, fg + 13, pz0 + 16);
    var hanging = simT.Spawn(Kinds.Rider, rung);
    var climber = simT.Spawn(Kinds.Spearman, rung);
    float hang0 = hanging.Y;
    RunT(.2);
    Check("horses and hounds take no ladder: a spearman finds his way up a ladder onto a pillar, a rider, a lancer and a hound none, and a horse set on the rungs comes down off them",
        spearUp && !riderUp && !houndUp && !lancerUp && Kinds.Troops.All(t => t.Move.Ladders == !(t.Mounted || t.NoHands)) && hanging.Y < hang0 - 4 && simT.Nav.Ladder[climber.Cell] != 0,
        $"spearman up {spearUp}, rider {riderUp}, lancer {lancerUp}, hound {houndUp}; the horse on the rungs {hang0 - fg:0} → {hanging.Y - fg:0} voxels up, the spearman still on them {simT.Nav.Ladder[climber.Cell] != 0}");
    Gone(hanging); Gone(climber);

    // the hounds and the riders do no work: their labour is all set to none; the rest of the troops work as soldiers do
    var houndW = simT.Spawn(Kinds.Hound, simT.NearestStand(fx0 + 200, fz0 + 40, 1));
    var riderW = simT.Spawn(Kinds.Rider, simT.NearestStand(fx0 + 220, fz0 + 40, 3));
    var giantW = simT.Spawn(Kinds.Giant, simT.NearestStand(fx0 + 240, fz0 + 40, 3));
    Check("hounds and riders do no work (their priorities are all none); a giant and a tame troll work as soldiers do",
        houndW.Priority.All(p => p == 0) && riderW.Priority.All(p => p == 0) && giantW.Priority.Any(p => p > 0) && Kinds.TameTroll.Work > 0,
        $"hound {string.Join("", houndW.Priority)}, rider {string.Join("", riderW.Priority)}, giant {string.Join("", giantW.Priority)}");
    Gone(houndW); Gone(riderW); Gone(giantW);

    // one standing where it was put on the level field: ours held there, a raider guarding the spot (or held fast in
    // chains, so as to stand and be shot at)
    Unit Put(UnitType t, int ci, int ck, bool chained = false)
    {
        int c = simT.NearestStand(fx0 + ci * 8 + 4, fz0 + ck * 8 + 4, t.Move.Height);
        var u = simT.Spawn(t, c);
        (u.X, u.Y, u.Z) = simT.Nav.Feet(c);
        if (t.Team == Team.Horde) { u.Guard = true; u.Home = u.Cell; if (chained) u.ChainedUntil = 1e9; }
        else { u.Hold = true; u.Dest = u.Cell; u.Post = u.Cell; }
        return u;
    }
    void Face(Unit a, Unit b) { a.Facing = MathF.Atan2(b.Z - a.Z, b.X - a.X); b.Facing = MathF.Atan2(a.Z - b.Z, a.X - b.X); }

    // a spear reaches past a sword: a goblin two metres off is within a spearman's reach and not a swordsman's; a
    // little further, only with the long shafts
    var spearR = Put(Kinds.Spearman, 4, 4); var swordR = Put(Kinds.Swordsman, 4, 8); var halbR = Put(Kinds.Halberdier, 4, 12);
    var gobR1 = Put(Kinds.Goblin, 6, 4); var gobR2 = Put(Kinds.Goblin, 6, 8); var gobR3 = Put(Kinds.Goblin, 6, 13);
    var gobR4 = Put(Kinds.Goblin, 6, 5);
    bool spearIn = simT.InReach(spearR, gobR1), swordIn = simT.InReach(swordR, gobR2), spearFar0 = simT.InReach(spearR, gobR4), halbFar0 = simT.InReach(halbR, gobR3);
    simT.Learnt.Add("longshafts");
    bool spearFar1 = simT.InReach(spearR, gobR4), halbFar1 = simT.InReach(halbR, gobR3);
    simT.Learnt.Remove("longshafts");
    Check("a spear reaches past a sword: at two metres a spearman strikes and a swordsman does not; with the long shafts a spear and a halberd strike from a quarter further",
        spearIn && !swordIn && !spearFar0 && spearFar1 && !halbFar0 && halbFar1,
        $"spear {Kinds.Spearman.Reach / 8:0.00} m, sword {Kinds.Swordsman.Reach / 8:0.00} m: at {Sim.Dist(spearR, gobR1) / 8:0.00} m the spear {spearIn}, the sword {swordIn}; " +
        $"at {Sim.Dist(spearR, gobR4) / 8:0.00} m the spear {spearFar0} → {spearFar1}, the halberd at {Sim.Dist(halbR, gobR3) / 8:0.00} m {halbFar0} → {halbFar1}");
    foreach (var u in new[] { spearR, swordR, halbR, gobR1, gobR2, gobR3, gobR4 }) Gone(u);

    // blows taken over and over, the body made whole again before each: what they take on the whole
    float Blows(Unit a, Unit b, int n, bool gallop = false)
    {
        float sum = 0;
        int counted = 0;
        for (int i = 0; i < n; i++)
        {
            b.Wounds = null; b.Dead = false; b.State = UnitState.Idle; b.Hp = b.MaxHp = 10000; b.Airborne = null;
            a.Cooldown = 0; a.GallopUntil = gallop ? simT.Time + 1 : -1;
            simT.Strike(a, b);
            float d = 10000 - b.Hp;
            // a blow that split the head or pierced the heart is death, not a blow's worth
            if (b.Dead || d > 2000) continue;
            sum += d; counted++;
        }
        b.Dead = false; b.State = UnitState.Idle; b.Hp = b.MaxHp = b.T.Hp; b.Wounds = null; b.Airborne = null;
        return counted > 0 ? sum / counted : 0;
    }

    // the shield wall: a swordsman beside a shieldbearer takes a quarter less from a goblin's blows
    var shieldW = Put(Kinds.Shieldbearer, 12, 4); var coveredW = Put(Kinds.Swordsman, 14, 4); var aloneW = Put(Kinds.Swordsman, 30, 4);
    coveredW.Look = aloneW.Look = 0;
    var gobW = Put(Kinds.Goblin, 16, 4); var gobW2 = Put(Kinds.Goblin, 32, 4);
    Face(gobW, coveredW); Face(gobW2, aloneW);
    float coveredOff = Blows(gobW, coveredW, 160), aloneOff = Blows(gobW2, aloneW, 160);
    simT.Learnt.Add("shieldwall");
    float coveredOn = Blows(gobW, coveredW, 160), aloneOn = Blows(gobW2, aloneW, 160);
    Check("the shield wall: within three metres of a shieldbearer a swordsman takes a quarter less from a goblin's blows",
        simT.ShieldWall(coveredW) == .75f && simT.ShieldWall(aloneW) == 1 && coveredOn / aloneOn < .86f && coveredOn / aloneOn > .64f && coveredOff / aloneOff > .86f,
        $"a blow on the whole, without the lore: beside {coveredOff:0.0}, alone {aloneOff:0.0}; with it: beside {coveredOn:0.0}, alone {aloneOn:0.0} (×{coveredOn / aloneOn:0.00})");
    foreach (var u in new[] { shieldW, coveredW, aloneW, gobW, gobW2 }) Gone(u);

    // a giant's blow sends an orc flying; a tame troll's too
    var giantF = Put(Kinds.Giant, 20, 12); var orcF = Put(Kinds.Orc, 22, 12);
    Face(giantF, orcF);
    float of0x = orcF.X, of0z = orcF.Z;
    orcF.Hp = orcF.MaxHp = 2000;
    simT.Strike(giantF, orcF);
    bool flungF = orcF.Airborne != null;
    for (int i = 0; i < 80 && orcF.Airborne != null; i++) RunT(.05);
    float flewF = Metres(orcF.X, orcF.Z, of0x, of0z);
    var trollF = Put(Kinds.TameTroll, 20, 20); var gobF = Put(Kinds.Goblin, 22, 20);
    Face(trollF, gobF);
    gobF.Hp = gobF.MaxHp = 2000;
    simT.Strike(trollF, gobF);
    bool flungF2 = gobF.Airborne != null;
    Check("a giant's blow sends an orc flying through the air, and a tame troll's a goblin",
        flungF && flewF > 2 && flungF2 && orcF.Airborne == null,
        $"the orc thrown {flungF}, landed {flewF:0.0} m off; the goblin thrown {flungF2}");
    foreach (var u in new[] { giantF, orcF, trollF, gobF }) Gone(u);
    RunT(3);

    // a tame troll mends two hit points a second out of a fight
    var trollM = Put(Kinds.TameTroll, 26, 20);
    trollM.Hp = 300; trollM.LastHurt = -99;
    RunT(5);
    // two a second of its own, over what any of ours mends out of a fight (the more by the citadel)
    bool homeM = Metres(trollM.X, trollM.Z, siteT.KeepX, siteT.KeepZ) < 40;
    float anyM = trollM.MaxHp * (homeM ? .0035f : .0012f) * 5;
    Check("a tame troll mends as trolls do: two hit points a second out of a fight, over what any of ours mends", MathF.Abs(trollM.Hp - 300 - anyM - 10) < 1.2f,
        $"300 → {trollM.Hp:0.0} in five seconds, {anyM:0.0} of it as any of ours mends");
    Gone(trollM);

    // the charge: a lancer's blow straight from the gallop strikes twice as hard and throws a goblin off its feet; a
    // rider's only with the charge learnt; at a stand, neither
    // (measured on a mantlet: it takes a blow as it comes, and stands)
    var lancerC = Put(Kinds.Lancer, 12, 24); var riderC = Put(Kinds.Rider, 12, 32); var mantC = Put(Kinds.Mantlet, 15, 24, chained: true);
    var gobC = Put(Kinds.Goblin, 14, 30);
    Face(lancerC, mantC);
    float standL = Blows(lancerC, mantC, 60), gallopL = Blows(lancerC, mantC, 60, gallop: true);
    Face(riderC, gobC);
    gobC.Hp = gobC.MaxHp = 2000;
    riderC.GallopUntil = simT.Time + 1;
    simT.Strike(riderC, gobC);
    bool riderThrows0 = gobC.Airborne != null;
    RunT(3);
    simT.Learnt.Add("charge");
    gobC.Hp = 2000;
    Face(riderC, gobC);
    riderC.GallopUntil = simT.Time + 1;
    simT.Strike(riderC, gobC);
    bool riderThrows1 = gobC.Airborne != null;
    RunT(3);
    gobC.Hp = 2000;
    Face(riderC, gobC);
    riderC.GallopUntil = -1;
    simT.Strike(riderC, gobC);
    bool standThrows = gobC.Airborne != null;
    simT.Learnt.Remove("charge");
    Check("a lancer from the gallop strikes twice as hard and throws a goblin off its feet; a rider does so only with the charge, and nobody at a stand",
        gallopL / standL > 1.75f && gallopL / standL < 2.3f && !riderThrows0 && riderThrows1 && !standThrows,
        $"a lancer's blow standing {standL:0.0}, from the gallop {gallopL:0.0} (×{gallopL / standL:0.00}); a rider throws from the gallop {riderThrows0}, with the charge {riderThrows1}, standing {standThrows}");
    foreach (var u in new[] { lancerC, riderC, mantC, gobC }) Gone(u);
    RunT(3);

    // a rider rides up and charges by himself: the goblin it comes upon is thrown
    var lancerG = Put(Kinds.Lancer, 4, 22); var gobG = Put(Kinds.Goblin, 9, 22);
    gobG.Hp = gobG.MaxHp = 2000;
    float lg0x = lancerG.X;
    bool thrownG = false;
    for (int i = 0; i < 100 && !thrownG; i++) { RunT(.05); thrownG |= gobG.Airborne != null; }
    Check("a lancer that sees a goblin near its post rides at it and the goblin is thrown by the charge", thrownG, $"the lancer rode {Metres(lancerG.X, 0, lg0x, 0):0.0} m; thrown {thrownG}");
    foreach (var u in new[] { lancerG, gobG }) Gone(u);
    RunT(3);

    // the hunt: hounds and riders run a third faster after one that flees, and bite and strike it twice as hard
    var houndH = Put(Kinds.Hound, 30, 16); var mantH = Put(Kinds.Mantlet, 33, 16, chained: true);
    Face(houndH, mantH);
    mantH.Routed = true;
    float biteOff = Blows(houndH, mantH, 60);
    simT.Learnt.Add("hunt");
    float biteOn = Blows(houndH, mantH, 60);
    mantH.Routed = false;
    float biteSteady = Blows(houndH, mantH, 60);
    simT.Learnt.Remove("hunt");
    Gone(mantH);
    float Chase(bool hunt)
    {
        if (hunt) simT.Learnt.Add("hunt"); else simT.Learnt.Remove("hunt");
        var r = Put(Kinds.Rider, 34, 32);
        var g = Put(Kinds.Goblin, 12, 32);
        r.Pace = 1;
        g.Guard = false; g.Routed = true; g.Hp = g.MaxHp = 5000; g.Home = -1;
        RunT(.5);
        float x0 = r.X, z0 = r.Z;
        // in fine ticks: a walker stops at each cell of its path for the rest of that tick, and at a twentieth of a
        // second a gallop and one a third faster may both come to four ticks a cell
        for (int i = 0; i < 150; i++) simT.Step(.01f);
        float d = Metres(r.X, r.Z, x0, z0) / 1.5f;
        Gone(r); Gone(g);
        RunT(.5);
        return d;
    }
    float chase0 = Chase(false), chase1 = Chase(true);
    simT.Learnt.Remove("hunt");
    Check("the hunt: a rider runs a third faster after a raider that flees, and a hound bites one twice as hard",
        biteOn / biteOff > 1.75f && biteOn / biteOff < 2.3f && biteSteady / biteOff < 1.2f && chase1 / chase0 > 1.2f && chase0 > 2.5f,
        $"a hound's bite at one fleeing {biteOff:0.0}, with the hunt {biteOn:0.0}, at one standing {biteSteady:0.0}; a rider after one fleeing {chase0:0.0} m/s, with the hunt {chase1:0.0} m/s");
    Gone(houndH);
    RunT(1);

    // shots, counted as they are let fly: what flies and how hard
    var seen = new System.Collections.Generic.HashSet<Arrow>();
    var flown = new System.Collections.Generic.List<Arrow>();
    void Watch(double seconds) { for (double t = 0; t < seconds; t += .05) { simT.Step(.05f); foreach (var a in simT.Arrows) if (seen.Add(a)) flown.Add(a); } }

    // a sling stone: a stone, not an arrow, it does not stay in the body; of lead it strikes a third harder and knocks the raider back a step
    var slingL = Put(Kinds.Slinger, 4, 2); var trollL = Put(Kinds.Troll, 14, 2, chained: true);
    trollL.Hp = trollL.MaxHp = 50000;
    flown.Clear();
    Watch(12);
    var stones0 = flown.FindAll(a => a.From == slingL);
    float plainL = stones0.Count > 0 ? stones0.Average(a => a.Damage) : 0;
    int stuckL = trollL.Wounds?.Arrows.Count ?? 0;
    simT.Learnt.Add("leadshot");
    flown.Clear();
    // (sixteen metres off: further than a guard goes after one of ours)
    var gobL = Put(Kinds.Goblin, 20, 2);
    gobL.Hp = gobL.MaxHp = 50000;
    Gone(trollL);
    bool knocked = false;
    float gl0x = gobL.X, gl0z = gobL.Z;
    for (int i = 0; i < 240; i++) { Watch(.05); knocked |= gobL.Airborne != null; }
    var stones1 = flown.FindAll(a => a.From == slingL);
    float leadL = stones1.Count > 0 ? stones1.Average(a => a.Damage) : 0;
    Check("a slinger throws stones that do not stay in the body; with lead shot they strike a third harder and knock the raider back a step",
        stones0.Count >= 6 && stones0.All(a => a.Kind == Shot.Stone) && stuckL == 0 && leadL / plainL > 1.2f && leadL / plainL < 1.47f && knocked,
        $"{stones0.Count} stones in 12 s, a stone {plainL:0.0}, of lead {leadL:0.0} (×{leadL / plainL:0.00}); stuck in the troll {stuckL}; the goblin knocked off its feet {knocked}, {Metres(gobL.X, gobL.Z, gl0x, gl0z):0.0} m from where it stood");
    simT.Learnt.Remove("leadshot");
    Gone(gobL);

    // the fire pots: now and then a slinger throws a pot of burning pitch, and the raider and the ground by it burn
    simT.Learnt.Add("firepots");
    var gobP = Put(Kinds.Orc, 14, 2, chained: true);
    gobP.Hp = gobP.MaxHp = 50000;
    flown.Clear();
    bool burnt = false;
    // a dozen throws at the least, and till the pot has burnt: a pot is one throw in four, by chance
    for (int i = 0; i < 1200 && !(simT.Pitched > 0 && burnt && flown.Count >= 12); i++) { Watch(.05); burnt |= gobP.Burn > 0; }
    int potsP = flown.Count(a => a.Kind == Shot.FirePot), stonesP = flown.Count(a => a.Kind == Shot.Stone);
    Check("with the fire pots a slinger now and then throws a pot of burning pitch: the raider catches fire and so does the ground about it",
        potsP >= 1 && stonesP >= potsP && simT.Pitched > 0 && simT.Burning.Count > 0 && burnt,
        $"{stonesP} stones and {potsP} fire pots; the ground set alight {simT.Pitched} times, {simT.Burning.Count} voxels burning; the orc burnt {burnt}");
    simT.Learnt.Remove("firepots");
    Gone(gobP); Gone(slingL);
    RunT(1);

    // the bodkins: a crossbow bolt goes through the first raider into the one behind it
    var xbow = Put(Kinds.Crossbowman, 4, 33); var trollB = Put(Kinds.Troll, 16, 33, chained: true); var gobB = Put(Kinds.Goblin, 19, 33, chained: true);
    trollB.Hp = trollB.MaxHp = 50000; gobB.Hp = gobB.MaxHp = 50000;
    flown.Clear();
    Watch(9);
    bool behind0 = gobB.Hp < gobB.MaxHp;
    int bolts0 = flown.Count(a => a.From == xbow && a.Kind == Shot.Bolt);
    simT.Learnt.Add("bodkins");
    Watch(12);
    bool behind1 = gobB.Hp < gobB.MaxHp;
    Check("a crossbowman shoots bolts; with bodkin heads a bolt goes through the troll it strikes into the goblin behind",
        bolts0 >= 2 && !behind0 && behind1 && simT.Pierced > 0,
        $"{bolts0} bolts in 9 s, the goblin behind hurt {behind0}; with bodkins {simT.Pierced} through, the goblin {gobB.Hp:0} of {gobB.MaxHp:0}");
    simT.Learnt.Remove("bodkins");

    // the lore of bows is the bows': with yew a crossbow does not carry further, a horse archer's bow does
    var hArch = Put(Kinds.HorseArcher, 8, 18);
    float xr0 = simT.ShotRange(xbow), hr0 = simT.ShotRange(hArch);
    simT.Learnt.Add("yew");
    float xr1 = simT.ShotRange(xbow), hr1 = simT.ShotRange(hArch);
    simT.Learnt.Remove("yew");
    Check("the bows' lore stays with bows: with yew a horse archer shoots 15 % further, a crossbowman no further",
        xr1 == xr0 && MathF.Abs(hr1 / hr0 - 1.15f) < .01f, $"crossbow {xr0 / 8:0.0} → {xr1 / 8:0.0} m, horse archer's bow {hr0 / 8:0.0} → {hr1 / 8:0.0} m");
    foreach (var u in new[] { xbow, trollB, gobB, hArch }) Gone(u);
    RunT(1);

    // a pot of powder: its burst breaks a siege ladder against our wall and hurts and throws the goblin at its foot,
    // and leaves our masonry be
    int wx = fx0 + 280, wz = fz0 + 60;
    for (int z = wz - 12; z < wz + 12; z++)
        for (int x = wx; x < wx + 6; x++)
            for (int y = fg; y < fg + 32; y++) wT.Set(x, y, z, Mat.Stone);
    for (int z = wz - 1; z < wz + 1; z++)
        for (int y = fg; y < fg + 32; y++) wT.Set(wx - 1, y, z, Mat.SiegeLadder);
    RunT(.2);
    int StoneW() { int n = 0; for (int z = wz - 12; z < wz + 12; z++) for (int x = wx; x < wx + 6; x++) for (int y = fg; y < fg + 32; y++) if (wT.Get(x, y, z) == Mat.Stone) n++; return n; }
    int LadderW() { int n = 0; for (int z = wz - 2; z < wz + 2; z++) for (int y = fg; y < fg + 40; y++) if (wT.Get(wx - 1, y, z) == Mat.SiegeLadder) n++; return n; }
    var gobK = simT.Spawn(Kinds.Goblin, simT.NearestStand(wx - 6, wz + 4, 2));
    gobK.Guard = true; gobK.Home = gobK.Cell;
    int stoneK0 = StoneW(), ladderK0 = LadderW();
    float gk0 = gobK.Hp;
    int struck = simT.PotBurst(wx - 4, fg + 3, wz, Kinds.Bombardier.Damage, null);
    bool thrownK = gobK.Airborne != null;
    RunT(3);
    int stoneK1 = StoneW(), ladderK1 = LadderW();
    Check("a pot of powder bursting at the foot of our wall breaks the horde's siege ladder up it and hurts and throws the goblin by it, and our stone stands",
        ladderK0 >= 60 && ladderK1 == 0 && stoneK1 == stoneK0 && (gobK.Dead || gobK.Hp < gk0 - 10) && thrownK && struck > 0,
        $"ladder {ladderK0} → {ladderK1}, our stone {stoneK0} → {stoneK1}; the goblin {(gobK.Dead ? "dead: " + gobK.KilledBy : $"{gobK.Hp:0} of {gk0:0}")}, thrown {thrownK}");
    Gone(gobK);

    // a bombardier throws his pot at a raider, but not while one of ours stands by it
    var bomb = Put(Kinds.Bombardier, 4, 10); var gobT = Put(Kinds.Orc, 15, 10, chained: true); var ours = Put(Kinds.Spearman, 15, 12);
    gobT.Hp = gobT.MaxHp = 50000; ours.Hp = ours.MaxHp = 5000;
    int bursts0 = simT.Bursts;
    flown.Clear();
    Watch(7);
    int heldPots = flown.Count(a => a.Kind == Shot.Pot);
    Gone(ours);
    Watch(7);
    int thrownPots = flown.Count(a => a.Kind == Shot.Pot);
    Check("a bombardier holds his pot while one of ours stands within two metres of the raider, and throws it once he is gone",
        heldPots == 0 && thrownPots >= 1 && simT.Bursts > bursts0 && gobT.Hp < gobT.MaxHp,
        $"pots with ours by the orc {heldPots}, after {thrownPots}; bursts {simT.Bursts - bursts0}; the orc {gobT.Hp:0} of {gobT.MaxHp:0}");
    foreach (var u in new[] { bomb, gobT }) Gone(u);
    RunT(1);

    // the boulders: a giant tears a stone out of the ground and hurls it at a raider out of its reach; it comes down
    // and rolls on; a spearman of ours by the raider is not struck by it
    // (a mantlet left by its band: it stands where it is and strikes nobody)
    var giantB = Put(Kinds.Giant, 6, 16); var gobS = Put(Kinds.Mantlet, 22, 17); var oursS = Put(Kinds.Spearman, 23, 14);
    gobS.Hp = gobS.MaxHp = 5000; oursS.Hp = oursS.MaxHp = 5000;
    RunT(3);
    int hurled0 = simT.Hurled;
    simT.Learnt.Add("boulders");
    Flier boulder = null;
    bool rolled = false;
    for (int i = 0; i < 400 && !rolled; i++)
    {
        RunT(.05);
        boulder ??= simT.Fliers.Find(f => f.Cause == Sim.BoulderCause);
        if (boulder != null && boulder.Rolling) rolled = true;
    }
    float rollFrom = boulder?.X ?? 0, rollFromZ = boulder?.Z ?? 0;
    RunT(4);
    Check("with the boulders a giant hurls a stone at a mantlet out of its reach; it comes down and rolls on, and passes our spearman by beside it",
        hurled0 == 0 && simT.Hurled > 0 && rolled && oursS.Hp == oursS.MaxHp,
        $"stones hurled {simT.Hurled}; rolling {rolled}, rolled {(boulder != null ? Metres(boulder.X, boulder.Z, rollFrom, rollFromZ) : 0):0.0} m on; the mantlet {gobS.Hp:0} of {gobS.MaxHp:0}, our spearman {oursS.Hp:0} of {oursS.MaxHp:0}");
    simT.Learnt.Remove("boulders");
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

// ---- felling: trees round the fort felled by the household of a new game, crowns and all, nothing left up in the air
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--errands")
{
    // the siege as hard as chosen: fewer and softer raiders, or more and harder ones; and the errands of the first day,
    // one after another, each paid when it is done, kept in a save
    Console.WriteLine();
    int Peace(int n) => Sim.Roster(n, .6f).Count; int Usual(int n) => Sim.Roster(n, 1).Count; int Cruel(int n) => Sim.Roster(n, 1.35f).Count;
    var mpE = new MapParams();
    var wE = new VoxelWorld(mpE.SizeX, mpE.SizeY, mpE.SizeZ);
    var siteE = MapGen.Generate(wE, mpE);
    wE.Compact();
    var simE = new Sim(wE, siteE);
    simE.WildTarget = 0;
    var gateE = StartingFort.Raise(simE);
    simE.SeedMines();
    void RunE(double seconds) { for (double t = 0; t < seconds; t += .05) simE.Step(.05f); }
    simE.SetHardness(Hardness.Cruel);
    var hardGob = simE.Spawn(Kinds.Goblin, simE.Gate);
    float cruelHp = hardGob.MaxHp;
    hardGob.Dead = true; hardGob.DiedAt = -999;
    simE.SetHardness(Hardness.Peaceful);
    int planned = 0; foreach (var b in simE.Bands) if (b.State == BandState.Coming) planned += b.Roster.Count;
    Check("a peaceful siege brings fewer raiders, a cruel one more and hardier, and the first night's bands are planned for it",
        Peace(1) < Usual(1) && Usual(1) < Cruel(1) && Peace(6) < Usual(6) && Usual(6) < Cruel(6) && cruelHp > Kinds.Goblin.Hp * 1.1f && planned == Peace(1),
        $"wave 1: {Peace(1)} / {Usual(1)} / {Cruel(1)}, wave 6: {Peace(6)} / {Usual(6)} / {Cruel(6)}; a cruel goblin {cruelHp:0} hp of {Kinds.Goblin.Hp}; {planned} planned for the first night");
    simE.SetHardness(Hardness.Normal);

    simE.BeginErrands();
    var road = new List<string>();
    string Main() => simE.Errands.Count > 0 && simE.Errands[0].Main ? simE.Errands[0].Key : "-";
    road.Add(Main());
    double gold0 = simE.Stock.Gold, know0 = simE.Knowledge;
    int done0 = simE.ErrandsDone, smalls = simE.Errands.FindAll(e => !e.Main).Count;
    // begin mending
    var (fx0, fz0, fx1, fz1) = simE.FortArea;
    simE.Mend(new VoxelList(), new VoxelList());
    RunE(1.2); road.Add(Main());
    // an archer on the wall: set him there
    Unit bowE = null;
    int topX = -1, topZ = -1, topY = 0;
    for (int z = fz0; z <= fz1 && topX < 0; z += 2)
        for (int x = fx0; x <= fx1 && topX < 0; x += 2)
        {
            int g = wE.SurfAt(x, z);
            if (g - siteE.HTop[z * wE.SX + x] >= 20 && simE.Nav.Stand(x >> NavGrid.Shift, g >> NavGrid.Shift, z >> NavGrid.Shift, 2)) { topX = x; topZ = z; topY = g; }
        }
    if (topX >= 0)
    {
        int c = simE.Nav.CellOf(topX, topY, topZ);
        bowE = simE.Spawn(Kinds.Archer, c);
        simE.Command(new[] { bowE }, c);
    }
    RunE(1.2); road.Add(Main());
    // a quarry set aside
    simE.AddZone(fx0 - 120, fz0 - 40, fx0 - 80, fz0, Stockpile.Stone, 8);
    RunE(1.2); road.Add(Main());
    // the gate hung
    if (simE.FortGate != null) { simE.InstantLabour = true; simE.MendGate(simE.FortGate); simE.InstantLabour = false; }
    RunE(1.2); road.Add(Main());
    // the first night lived through
    simE.Waves.StartNow(simE);
    RunE(1);
    simE.Waves.CallOff(simE);
    simE.Waves.Number = Math.Max(simE.Waves.Number, 1);
    RunE(1.2); road.Add(Main());
    Check("the first day's errands come one after another — mend, an archer on the wall, a quarry, the gate, the night — each paid when done, with two small ones beside",
        string.Join(" ", road) == "mend post quarry gate night night" && simE.ErrandsDone - done0 >= 5 && simE.Stock.Gold > gold0 + 40 && simE.Knowledge >= know0 + 4 && smalls == 2,
        $"{string.Join(" → ", road)}; {simE.ErrandsDone - done0} done, gold +{simE.Stock.Gold - gold0:0}, knowledge +{simE.Knowledge - know0:0}; {smalls} small beside the first");

    // kept in a save
    simE.SetHardness(Hardness.Cruel);
    var msE = new System.IO.MemoryStream();
    simE.Save(msE, mpE.Seed, "test", "");
    msE.Position = 0;
    var wE2 = new VoxelWorld(mpE.SizeX, mpE.SizeY, mpE.SizeZ);
    Sim simE2;
    using (var reader = new SaveReader(msE))
    {
        var siteE2 = MapGen.Generate(wE2, new MapParams { Seed = reader.Seed });
        wE2.Compact();
        reader.Valley(wE2, siteE2);
        simE2 = new Sim(wE2, siteE2);
        simE2.Load(reader);
    }
    Check("the hardness and the errands are kept in a save",
        simE2.Hardness == Hardness.Cruel && !simE2.NoErrands && simE2.Errands.Count == simE.Errands.Count && simE2.Errands.ConvertAll(e => e.Key).SequenceEqual(simE.Errands.ConvertAll(e => e.Key)) && simE2.ErrandsDone == simE.ErrandsDone,
        $"hardness {simE2.Hardness}, errands {string.Join(", ", simE2.Errands.ConvertAll(e => e.Key))} (were {string.Join(", ", simE.Errands.ConvertAll(e => e.Key))}), {simE2.ErrandsDone} done");

    // a night held: its report and stars, and the dawn's gifts — a leader killed leaves a trophy; one taken, the rest go;
    // what is not taken is gone at the next horn
    simE.SetHardness(Hardness.Normal);
    simE.Waves.StartNow(simE);
    simE.Waves._queue.Clear();
    var leaderE = simE.Spawn(Kinds.Goblin, simE.NearestStand(fx0 - 60, fz0 + 30, 2));
    leaderE.Leader = true;
    simE.Hurt(leaderE, bowE, 1000, "test");
    for (int t = 0; t < 20 && simE.Waves.Active; t++) RunE(.5);
    var nightE = simE.LastNight;
    int giftsE = simE.Gifts.Count;
    bool trophyE = simE.Gifts.Exists(g => g.Kind == GiftKind.Trophy);
    int caravanE = simE.Gifts.FindIndex(g => g.Kind == GiftKind.Caravan);
    double stoneE = simE.Stock[Stockpile.Stone], woodE = simE.Stock[Stockpile.Wood], earthE = simE.Stock[Stockpile.Earth], goldE = simE.Stock.Gold;
    bool tookE = caravanE >= 0 && simE.TakeGift(caravanE);
    bool paidE = simE.Stock[Stockpile.Stone] > stoneE || simE.Stock[Stockpile.Wood] > woodE || simE.Stock[Stockpile.Earth] > earthE || simE.Stock.Gold > goldE;
    Check("a night held has its report and stars, and the dawn offers gifts — a leader killed leaves a trophy; one taken, the rest go",
        nightE != null && nightE.LeadersKilled >= 1 && nightE.Killed >= 1 && giftsE >= 3 && trophyE && tookE && paidE && simE.Gifts.Count == 0,
        $"night {nightE?.Number}: {nightE?.Killed} felled, {nightE?.LeadersKilled} of {nightE?.Leaders} leaders, {nightE?.Stars} stars; {giftsE} gifts, a trophy {trophyE}; taken {tookE}, paid {paidE}, {simE.Gifts.Count} left");
    // the next dawn's gifts left untaken are gone at the horn
    simE.Waves.StartNow(simE);
    simE.Waves._queue.Clear();
    for (int t = 0; t < 20 && simE.Waves.Active; t++) RunE(.5);
    int waitingE = simE.Gifts.Count;
    // kept in a save: the gifts waiting, and the night's report
    var msD = new System.IO.MemoryStream();
    simE.Save(msD, mpE.Seed, "test", "");
    msD.Position = 0;
    var wD = new VoxelWorld(mpE.SizeX, mpE.SizeY, mpE.SizeZ);
    Sim simD;
    using (var reader = new SaveReader(msD))
    {
        var siteD = MapGen.Generate(wD, new MapParams { Seed = reader.Seed });
        wD.Compact();
        reader.Valley(wD, siteD);
        simD = new Sim(wD, siteD);
        simD.Load(reader);
    }
    Check("the dawn's gifts and the night's report are kept in a save",
        simD.Gifts.Count == waitingE && simD.Gifts.ConvertAll(g => g.Key).SequenceEqual(simE.Gifts.ConvertAll(g => g.Key)) && simD.LastNight?.Number == simE.LastNight?.Number && simD.LastNight?.Stars == simE.LastNight?.Stars,
        $"{simD.Gifts.Count} gifts of {waitingE}, night {simD.LastNight?.Number} with {simD.LastNight?.Stars} stars");
    simE.Waves.StartNow(simE);
    Check("gifts not taken by the next horn are gone", waitingE >= 3 && simE.Gifts.Count == 0, $"{waitingE} waiting at dawn, {simE.Gifts.Count} after the horn");
    simE.Waves.CallOff(simE);

    // the errands of the day follow the coming night: a band's leader to kill, the side the chief means to take; the
    // leader killed in the night pays
    simE.Errands.RemoveAll(e => !e.Main);
    RunE(1.2);
    var smallE = simE.Errands.FindAll(e => !e.Main).ConvertAll(e => e.Key);
    var leaderErr = simE.Errands.Find(e => e.Key == "leader");
    int doneBefore = simE.ErrandsDone;
    bool slain = false;
    if (leaderErr != null)
    {
        var bandL = simE.Bands.Find(b => b.Id == (int)leaderErr.From);
        simE.Waves.StartNow(simE);
        simE.Waves._queue.Clear();
        var lead = simE.SpawnRaider(bandL.Roster[0], bandL);
        simE.Hurt(lead, bowE, 100000, "test");
        RunE(1.2);
        // (the night may well be over by now, and the next night's leader asked for already)
        slain = !simE.Errands.Exists(e => e.Key == "leader" && (int)e.From == bandL.Id) && simE.ErrandsDone > doneBefore;
        simE.Waves.CallOff(simE);
    }
    Check("the day's errands follow the coming night — a band's leader to kill, the side the chief means to take — and the leader killed pays",
        smallE.Contains("leader") && slain, $"small: {string.Join(", ", smallE)}; the leader killed and paid {slain}");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--chapters")
{
    // the chapters: a champion on each chapter's third night in a siege of one's own, harder than any leader; killed,
    // he opens the next tier of the lore and leaves three legendary trophies to choose from; the chief's fall breaks the
    // horde; one who got away comes back crowned; all of it kept in a save
    Console.WriteLine();
    var mpH = new MapParams();
    var wH = new VoxelWorld(mpH.SizeX, mpH.SizeY, mpH.SizeZ);
    var siteH = MapGen.Generate(wH, mpH);
    wH.Compact();
    var simH = new Sim(wH, siteH);
    simH.WildTarget = 0;
    StartingFort.Raise(simH);
    void RunH(double seconds) { for (double t = 0; t < seconds; t += .05) simH.Step(.05f); }
    simH.PlanWave(3);
    bool noneByItself = !simH.Bands.Exists(b => b.Champion > 0);
    simH.BeginErrands();
    simH.PlanWave(3);
    RunH(1.2);
    var bossBand = simH.Bands.Find(b => b.Champion > 0);
    Check("a chapter's champion comes on its third night in a siege of one's own, and not in the bench's by itself",
        noneByItself && bossBand is { Champion: 1, Title: "Гнилозуб" } && bossBand.Roster.Count >= 5 && simH.Errands.Exists(e => e.Key == "champion"),
        $"by itself {(noneByItself ? "none" : "one")}; in one's own: {bossBand?.Name} with {bossBand?.Roster.Count}, an errand {simH.Errands.Exists(e => e.Key == "champion")}");

    // the lore shut beyond its first tier until the champion falls
    var tier2 = Sim.Lores.First(l => l.Coin == Coin.Knowledge && l.Tier == 1 && l.Spell == null);
    foreach (var need in tier2.Needs) simH.Learnt.Add(need);
    simH.Knowledge = 100;
    bool shut = !simH.CanLearn(tier2);
    // the night: the champion with his band, killed by one of ours
    var (gxH, _, gzH) = simH.Nav.Feet(simH.Gate);
    simH.Waves.Number = 2;
    simH.Waves.StartNow(simH);
    simH.Waves._queue.Clear();
    var bossH = simH.SpawnRaider(bossBand.Roster[0], bossBand);
    float bossHp = bossH.MaxHp;
    var bowH = simH.Spawn(Kinds.Archer, simH.Gate);
    simH.Hurt(bossH, bowH, 100000, "test");
    for (int t = 0; t < 20 && simH.Waves.Active; t++) RunH(.5);
    bool relics = simH.Gifts.Count == 3 && simH.Gifts.TrueForAll(g => g.Kind == GiftKind.Relic);
    string firstRelic = simH.Gifts.Count > 0 ? simH.Gifts[0].Key : "";
    bool took = simH.TakeGift(0);
    Check("a champion is far harder than a leader; killed, he opens the next tier of the lore and leaves three legendary trophies to choose from",
        bossHp >= Kinds.Orc.Hp * 4 && bossH.Champion == 1 && bossH.Name.Contains("Гнилозуб") && shut && simH.TierOpen == 2 && simH.CanLearn(tier2) && relics && took && simH.HasRelic(firstRelic),
        $"{bossH.Name}: {bossHp:0} hp of an orc's {Kinds.Orc.Hp}; tier II shut before {shut}, open after {simH.TierOpen}, «{tier2.Name}» learnable {simH.CanLearn(tier2)}; " +
        $"legendary trophies {relics}, took «{firstRelic}» {took}");

    // a branch forks at its third tier: one way shuts the other, and what rests on both opens with either
    simH.TierOpen = 4; simH.Knowledge = 100;
    foreach (var k in new[] { "yew", "barbs", "braziers" }) simH.Learnt.Add(k);
    bool both = simH.CanLearn(Sim.LoreOf("volley")) && simH.CanLearn(Sim.LoreOf("owleye"));
    bool learnt = simH.Learn("volley");
    bool shutOther = !simH.CanLearn(Sim.LoreOf("owleye")) && simH.RivalShut(Sim.LoreOf("owleye"));
    bool topOpen = simH.CanLearn(Sim.LoreOf("longbows"));
    Check("in a siege of one's own a branch forks at its third tier: one way taken shuts the other, and the branch's crown opens with either",
        both && learnt && shutOther && topOpen, $"both open before {both}; «Частая стрельба» learnt {learnt}; «Совиный глаз» shut {shutOther}; «Длинные луки» open {topOpen}");

    // a champion who got away comes back crowned
    var backH = new Survivor { Kind = Kinds.Troll, Look = 0, Hp = Kinds.Troll.Hp * .5f, Champion = 2, Title = "Камнелом" };
    var bandH = new Warband { Id = 999, Name = "Ватага Проверки", State = BandState.Coming, Entry = simH.NearestStand(siteH.SpawnStrip / 2, wH.SZ / 2, 3) };
    simH.Bands.Add(bandH);
    var backU = simH.SpawnRaider(Kinds.Orc, bandH);
    var backC = simH.SpawnRaider(Kinds.Troll, bandH, backH);
    Check("a champion who got away comes back as champion", backC.Champion == 2 && backC.Title == "Камнелом" && backC.MaxHp >= Kinds.Troll.Hp * 3,
        $"{backC.Name}, {backC.MaxHp:0} hp");

    // the chief falls, and the whole horde breaks
    simH.Waves.Active = true;
    var chief = simH.Spawn(Kinds.Orc, simH.NearestStand((int)gxH - 80, (int)gzH, 2));
    chief.Champion = 4; chief.Title = "Гарм"; chief.Leader = true;
    var rest = new List<Unit>();
    for (int i = 0; i < 4; i++) rest.Add(simH.Spawn(Kinds.Goblin, simH.NearestStand((int)gxH - 90, (int)gzH + i * 8, 2)));
    simH.Hurt(chief, bowH, 100000, "test");
    Check("the horde's chief killed, the whole horde breaks and runs", chief.Dead && rest.TrueForAll(u => u.Routed) && simH.ChampionsKilled.Contains(4),
        $"the chief dead {chief.Dead}; {rest.Count(u => u.Routed)} of {rest.Count} running");
    foreach (var u in rest) { u.Dead = true; u.DiedAt = -999; }
    backU.Dead = backC.Dead = true; backU.DiedAt = backC.DiedAt = -999;
    simH.Waves.Active = false;

    // kept in a save
    var msH = new System.IO.MemoryStream();
    simH.Save(msH, mpH.Seed, "test", "");
    msH.Position = 0;
    var wH2 = new VoxelWorld(mpH.SizeX, mpH.SizeY, mpH.SizeZ);
    Sim simH2;
    using (var reader = new SaveReader(msH))
    {
        var siteH2 = MapGen.Generate(wH2, new MapParams { Seed = reader.Seed });
        wH2.Compact();
        reader.Valley(wH2, siteH2);
        simH2 = new Sim(wH2, siteH2);
        simH2.Load(reader);
    }
    Check("the chapters are kept in a save: the tier open, the champions killed, the trophies taken",
        simH2.TierOpen == simH.TierOpen && simH2.ChampionsKilled.SetEquals(simH.ChampionsKilled) && simH2.Relics.SequenceEqual(simH.Relics),
        $"tier {simH2.TierOpen}, killed {string.Join(",", simH2.ChampionsKilled)}, trophies {string.Join(",", simH2.Relics)}");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--labour")
{
    // the pace of the work: a block of rock broken out by one worker, and a wall of stone laid by one, each in about the
    // time the reckoning shown with the marking promises (the walking to it and about it on top)
    Console.WriteLine();
    var mpL = new MapParams();
    var wL = new VoxelWorld(mpL.SizeX, mpL.SizeY, mpL.SizeZ);
    var siteL = MapGen.Generate(wL, mpL);
    wL.Compact();
    var simL = new Sim(wL, siteL);
    simL.Waves.Countdown = 1e9; simL.WildTarget = 0;
    void RunL(double seconds) { for (double t = 0; t < seconds; t += .05) simL.Step(.05f); }
    var (glx, _, glz) = simL.Nav.Feet(simL.Gate);
    int bx = (int)glx - 60, bz = (int)glz - 8, bg = wL.SurfAt(bx + 8, bz + 8);
    var block = new VoxelList();
    for (int y = bg; y < bg + 8; y++)
        for (int z = bz; z < bz + 16; z++)
            for (int x = bx; x < bx + 16; x++) { wL.Set(x, y, z, Mat.Rock); block.Add(x, y, z, Mat.Rock); }
    simL.Nav.Flush();
    var est = new double[4];
    double promised = Labour.Estimate(wL, block, true, est);
    var miner = simL.Spawn(Kinds.Worker, simL.NearestStand(bx - 6, bz + 8, 4));
    double stone0 = simL.Stock[Stockpile.Stone];
    simL.DesignateRemove(block);
    double took = 0;
    while (simL.Orders.Total > 0 && took < promised * 6 + 30) { RunL(.5); took += .5; }
    Check("one worker breaks out a block of rock in about the time the marking reckons", simL.Orders.Total == 0 && took < promised * 1.8 + 6,
        $"{block.Count} voxels of rock, reckoned {promised:0} s, took {took:0} s; stone +{Fmt.M3(simL.Stock[Stockpile.Stone] - stone0)} m³");
    var wallL = Shapes.Line(wL, siteL, bx, bz + 30, bx + 24, bz + 30, new Stroke { Mat = Mat.Stone, Width = 4, Height = 12, Level = true }, Shapes.LevelTop(wL, bx + 12, bz + 30, 12));
    double promisedLay = Labour.Estimate(wL, wallL, false, est);
    simL.Stock[Stockpile.Stone] = Math.Max(simL.Stock[Stockpile.Stone], wallL.Count + 100);
    simL.DesignateBuild(wallL);
    double tookLay = 0;
    while (simL.Orders.BuildTotal > 0 && tookLay < promisedLay * 6 + 30) { RunL(.5); tookLay += .5; }
    Check("one worker lays a wall of stone in about the time the marking reckons", simL.Orders.BuildTotal == 0 && tookLay < promisedLay * 1.8 + 6,
        $"{wallL.Count} voxels of stone, reckoned {promisedLay:0} s, took {tookLay:0} s");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--felling")
{
    Console.WriteLine();
    var mpF = new MapParams();
    var wF = new VoxelWorld(mpF.SizeX, mpF.SizeY, mpF.SizeZ);
    var siteF = MapGen.Generate(wF, mpF);
    wF.Compact();
    var sim = new Sim(wF, siteF);
    sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
    StartingFort.Raise(sim);
    void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
    var crew = new List<Unit>();
    for (int i = 0; i < 4; i++) crew.Add(sim.Spawn(Kinds.Worker, sim.Gate));
    // the trees nearest the fort, each felled by a click on its trunk as the player does
    var trees = new List<(int x, int z, VoxelList plan)>();
    var taken = new HashSet<long>();
    long K(int x, int y, int z) => ((long)y * wF.SZ + z) * wF.SX + x;
    for (int r = 70; r < 480 && trees.Count < 6; r += 4)
        for (int a = 0; a < 64 && trees.Count < 6; a++)
        {
            int x = siteF.KeepX + (int)(Math.Cos(a * Math.Tau / 64) * r), z = siteF.KeepZ + (int)(Math.Sin(a * Math.Tau / 64) * r);
            if (!wF.InBounds(x, 2, z)) continue;
            int g = wF.SurfAt(x, z);
            if (!Mat.IsTrunk(wF.Get(x, g + 2, z))) continue;
            var plan = Shapes.Tree(wF, x, g + 2, z);
            bool fresh = true;
            for (int i = 0; i < plan.Count; i++) if (taken.Contains(K(plan.X(i), plan.Y(i), plan.Z(i)))) { fresh = false; break; }
            if (!fresh || plan.Count < 300) continue;
            for (int i = 0; i < plan.Count; i++) taken.Add(K(plan.X(i), plan.Y(i), plan.Z(i)));
            sim.DesignateRemove(plan);
            trees.Add((x, z, plan));
        }
    double woodF = sim.Stock[Stockpile.Wood];
    if (Environment.GetEnvironmentVariable("FELL_DEBUG") == "1")
    {
        // one tree's trunk cut through at three metres by hand: how much of its crown goes when the top comes down
        var (tx, tz, tplan) = trees[0];
        int Leaves() { int n = 0; for (int i = 0; i < tplan.Count; i++) if (Mat.IsLeafy(wF.Get(tplan.X(i), tplan.Y(i), tplan.Z(i)))) n++; return n; }
        int Wood() { int n = 0; for (int i = 0; i < tplan.Count; i++) if (Mat.IsTrunk(wF.Get(tplan.X(i), tplan.Y(i), tplan.Z(i)))) n++; return n; }
        int g0 = wF.SurfAt(tx, tz);
        Console.WriteLine($"     before: {Leaves()} leaves, {Wood()} wood in the plan of {tplan.Count}");
        for (int z = tz - 6; z <= tz + 6; z++) for (int x = tx - 6; x <= tx + 6; x++) if (Mat.IsTrunk(wF.Get(x, g0 + 24, z))) wF.Set(x, g0 + 24, z, Mat.Air);
        Run(2);
        Console.WriteLine($"     after the cut at 3 m: {Leaves()} leaves, {Wood()} wood");
    }
    Run(900);
    int left = 0, up = 0, marked = 0;
    var where = new List<string>();
    foreach (var (x, z, plan) in trees)
    {
        int l = 0, u = 0, m = 0, wood = 0, low = 99999;
        for (int i = 0; i < plan.Count; i++)
        {
            int px = plan.X(i), py = plan.Y(i), pz = plan.Z(i);
            byte b = wF.Get(px, py, pz);
            if (Mat.IsLeafy(b) || Mat.IsTrunk(b))
            {
                l++;
                if (Mat.IsTrunk(b)) wood++;
                low = Math.Min(low, py - wF.GroundTop(px, pz));
                if (py > wF.SurfAt(px, pz) + 1 || wF.Get(px, py - 1, pz) == Mat.Air) u++;
            }
            if (sim.Orders.At(px, py, pz) != 0) m++;
        }
        left += l; up += u; marked += m;
        if (l > 0 && Environment.GetEnvironmentVariable("FELL_WHY") == "1")
        {
            // what the leaves left touch: other leaves not of the plan, wood, the ground, nothing
            var inPlan = new HashSet<long>();
            for (int i = 0; i < plan.Count; i++) inPlan.Add(K(plan.X(i), plan.Y(i), plan.Z(i)));
            int touchOther = 0, touchWood = 0, alone = 0, minY = 999, maxY = 0;
            for (int i = 0; i < plan.Count; i++)
            {
                int px = plan.X(i), py = plan.Y(i), pz = plan.Z(i);
                if (!Mat.IsLeafy(wF.Get(px, py, pz))) continue;
                minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
                bool o = false, wd = false, any = false;
                for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx | dy | dz) == 0) continue;
                    byte b2 = wF.Get(px + dx, py + dy, pz + dz);
                    if (b2 != Mat.Air) any = true;
                    if (Mat.IsTrunk(b2)) wd = true;
                    if (Mat.IsLeafy(b2) && !inPlan.Contains(K(px + dx, py + dy, pz + dz))) o = true;
                }
                if (o) touchOther++; if (wd) touchWood++; if (!any) alone++;
            }
            Console.WriteLine($"     {x},{z}: left leaves at y {minY}…{maxY}: {touchOther} touch leaves not of this tree, {touchWood} touch wood, {alone} touch nothing");
            // the wood they touch: where, whether marked, whether it is in this tree's plan
            var woodSeen = new HashSet<long>();
            for (int i = 0; i < plan.Count && woodSeen.Count < 6; i++)
            {
                int px = plan.X(i), py = plan.Y(i), pz = plan.Z(i);
                if (!Mat.IsLeafy(wF.Get(px, py, pz))) continue;
                for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    int qx = px + dx, qy = py + dy, qz = pz + dz;
                    if (!Mat.IsTrunk(wF.Get(qx, qy, qz)) || !woodSeen.Add(K(qx, qy, qz))) continue;
                    int below = 0; while (below < 200 && Mat.IsTrunk(wF.Get(qx, qy - below - 1, qz))) below++;
                    Console.WriteLine($"       wood {Mat.NameOf(wF.Get(qx, qy, qz))} at {qx},{qy},{qz} ({qy - wF.GroundTop(qx, qz)} up), marked {sim.Orders.At(qx, qy, qz) != 0}, in the plan {inPlan.Contains(K(qx, qy, qz))}, wood straight under it {below}, ground under the column at {wF.SurfAt(qx, qz)}");
                }
            }
        }
        if (l > 0) where.Add($"{x},{z}: {l} left, {wood} of them wood, the lowest {low} over the ground ({u} up in the air, {m} still marked)");
    }
    foreach (var u in crew) if (u.Why != "") Console.WriteLine($"     {u.T.Name} #{u.Id}: {u.Why}");
    Check("the household fells six trees round the fort whole, crowns and all, nothing left hanging", trees.Count == 6 && left == 0,
        $"{trees.Count} trees, wood +{Fmt.M3((int)(sim.Stock[Stockpile.Wood] - woodF))} m³; " + (where.Count == 0 ? "all gone" : string.Join("; ", where)));
    // a game saved before that: a crown marked to be felled hanging with no trunk under it comes down when it is read back
    {
        (int x, int z, VoxelList plan) hung = (-1, -1, null);
        for (int r = 100; r < 480 && hung.plan == null; r += 6)
            for (int a2 = 0; a2 < 48 && hung.plan == null; a2++)
            {
                int x = siteF.KeepX - (int)(Math.Cos(a2 * Math.Tau / 48) * r), z = siteF.KeepZ + (int)(Math.Sin(a2 * Math.Tau / 48) * r);
                if (!wF.InBounds(x, 2, z)) continue;
                int g = wF.SurfAt(x, z);
                if (!Mat.IsTrunk(wF.Get(x, g + 2, z)) || taken.Contains(K(x, g + 2, z))) continue;
                var plan = Shapes.Tree(wF, x, g + 2, z);
                if (plan.Count > 2000) hung = (x, z, plan);
            }
        int crown = 0, after = 0;
        if (hung.plan != null)
        {
            sim.DesignateRemove(hung.plan);
            // the trunk taken out from under the crown at once, as the old felling left it
            for (int i = 0; i < hung.plan.Count; i++)
            {
                int px = hung.plan.X(i), py = hung.plan.Y(i), pz = hung.plan.Z(i);
                if (Mat.IsTrunk(wF.Get(px, py, pz))) { wF.Set(px, py, pz, Mat.Air); sim.Orders.Clear(px, py, pz); }
                else if (Mat.IsLeafy(wF.Get(px, py, pz))) crown++;
            }
            Run(1);
            sim.DropStrayLeaves();
            for (int i = 0; i < hung.plan.Count; i++) if (Mat.IsLeafy(wF.Get(hung.plan.X(i), hung.plan.Y(i), hung.plan.Z(i)))) after++;
        }
        Check("…and a crown left hanging by an older felling comes down when the game is read back", hung.plan != null && crown > 500 && after == 0,
            $"tree at {hung.x},{hung.z}: {crown} leaves hanging, {after} left");
    }
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--sandbox")
{
    // the sandbox: everything learnt, spells free and ready at once, the stores kept full, room for a hundred more,
    // and a band of the horde called up where it is pointed at, which does the citadel no harm
    Console.WriteLine();
    var mpS = new MapParams();
    var wS = new VoxelWorld(mpS.SizeX, mpS.SizeY, mpS.SizeZ);
    var siteS = MapGen.Generate(wS, mpS);
    wS.Compact();
    var simS = new Sim(wS, siteS);
    simS.WildTarget = 0;
    StartingFort.Raise(simS);
    void RunS(double seconds) { for (double t = 0; t < seconds; t += .05) simS.Step(.05f); }
    simS.BeginSandbox();
    bool allKnown = Sim.Lores.All(l => simS.Knows(l.Key));
    var spell = Sim.SpellOf(SpellKind.Explosion);
    simS.Stock.Gold -= 4000;
    RunS(.1);
    Check("the sandbox learns everything, makes spells free and ready at once, keeps the stores full and the army roomy",
        allKnown && simS.CostOf(spell) == 0 && simS.CooldownOf(spell) == 0 && simS.Stock.Gold >= 5000 && simS.Places >= 100 && simS.Errands.Count == 0,
        $"all known {allKnown}, cost {simS.CostOf(spell)}, cooldown {simS.CooldownOf(spell)}, gold {simS.Stock.Gold:0}, places {simS.Places}, errands {simS.Errands.Count}");
    double keep0 = simS.KeepHp;
    var called = simS.Summon(siteS.KeepX - 60, siteS.KeepZ, heavy: true);
    RunS(40);
    Check("…a band called up where it is pointed at comes whole and goes at the citadel, which takes no harm",
        called != null && called.Members.Count == 8 && called.Members.Exists(u => u.T == Kinds.Troll) && called.Members.Exists(u => u.T == Kinds.HillGiant) && simS.KeepHp >= keep0 && !simS.Over,
        $"band {called?.Name} of {called?.Members.Count}, state {called?.State}; citadel {keep0:0} → {simS.KeepHp:0}");

    // the horde on call: the day runs on to noon and stands there; called, the horde comes knowing all; and a save
    // keeps the sandbox a sandbox
    for (int i = 0; i < 900; i++) simS.Waves.Tick(simS, 1);
    bool stood = !simS.Waves.Active && simS.Waves.Number == 0 && Math.Abs(simS.Waves.Countdown - Waves.Prep / 2) < 1e-6;
    var msS = new System.IO.MemoryStream();
    simS.Save(msS, mpS.Seed, "test", "");
    msS.Position = 0;
    var wS2 = new VoxelWorld(mpS.SizeX, mpS.SizeY, mpS.SizeZ);
    Sim simS2;
    using (var reader = new SaveReader(msS))
    {
        var siteS2 = MapGen.Generate(wS2, new MapParams { Seed = reader.Seed });
        wS2.Compact();
        reader.Valley(wS2, siteS2);
        simS2 = new Sim(wS2, siteS2);
        simS2.Load(reader);
    }
    double intel = simS.Waves.StartNow(simS);
    Check("…the horde comes only when called: the day stands at noon for 15 minutes, called it comes knowing all, and a save keeps the sandbox so",
        stood && simS.Waves.Active && simS.Waves.Number == 1 && intel == 1 && simS2.Sandbox && simS2.Waves.OnCall,
        $"wave {simS.Waves.Number}, countdown {simS.Waves.Countdown:0}, intel {intel:0.00}; read back: sandbox {simS2.Sandbox}, on call {simS2.Waves.OnCall}");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--escape")
{
    // a worker shut in by the work cuts its own way out: walled round in a ring of stone, and at the bottom of a pit
    // it dug itself, four metres deep with sheer sides
    Console.WriteLine();
    var mpQ = new MapParams();
    var wQ = new VoxelWorld(mpQ.SizeX, mpQ.SizeY, mpQ.SizeZ);
    var siteQ = MapGen.Generate(wQ, mpQ);
    wQ.Compact();
    var simQ = new Sim(wQ, siteQ);
    simQ.WildTarget = 0;
    StartingFort.Raise(simQ);
    void RunQ(double seconds) { for (double t = 0; t < seconds; t += .05) simQ.Step(.05f); }
    bool FreeQ(Unit u) { var cells = new List<int>(); return simQ.Paths.Reach(u.Cell, u.T.Move, 240, true, cells) >= 240 || cells.Contains(simQ.Gate); }
    // a flat, open spot of the plateau away from the fort
    (int x, int z) Flat(int fromX, int fromZ)
    {
        for (int r = 0; r < 200; r += 8)
            for (int a = 0; a < 16; a++)
            {
                int x = fromX + (int)(Math.Cos(a * Math.Tau / 16) * r), z = fromZ + (int)(Math.Sin(a * Math.Tau / 16) * r);
                if (x < 60 || z < 60 || x > wQ.SX - 60 || z > wQ.SZ - 60) continue;
                int y = wQ.SurfAt(x, z), lo = y, hi = y;
                for (int dx = -40; dx <= 40; dx += 8) for (int dz = -40; dz <= 40; dz += 8) { int h = wQ.SurfAt(x + dx, z + dz); lo = Math.Min(lo, h); hi = Math.Max(hi, h); }
                if (hi - lo <= 3 && !siteQ.Forbidden(x, z) && Math.Abs(x - siteQ.KeepX) + Math.Abs(z - siteQ.KeepZ) > 140) return (x, z);
            }
        return (-1, -1);
    }

    var ring = Flat(siteQ.KeepX + 160, siteQ.KeepZ + 60);
    var walled = simQ.Spawn(Kinds.Worker, simQ.NearestStand(ring.x, ring.z, 2));
    simQ.InstantLabour = true;
    simQ.DesignateBuild(Shapes.Box(wQ, siteQ, ring.x - 24, ring.z - 24, ring.x + 24, ring.z + 24, new Stroke { Mat = Mat.Wood, Width = 8, Height = 32, Level = true, Hollow = true }, Shapes.LevelTop(wQ, ring.x, ring.z, 32)));
    simQ.InstantLabour = false;
    simQ.Nav.Flush();
    bool shutAtFirst = !FreeQ(walled);
    double cutAt = -1;
    for (int t = 0; t < 400 * 20 && !FreeQ(walled); t++) { simQ.Step(.05f); if (cutAt < 0 && walled.WayOutMarked > 0) cutAt = simQ.Time; }
    Check("a worker walled round in a ring of its own logs marks a way out and cuts through by itself",
        ring.x > 0 && shutAtFirst && FreeQ(walled) && cutAt > 0,
        $"ring at {ring}: shut in at first {shutAtFirst}, way out marked at {cutAt:0} s, free now {FreeQ(walled)}; looked last at {walled.NextWayOut:0}, " +
        $"reach {simQ.Paths.Reach(walled.Cell, walled.T.Move, 240, true)}, state {walled.State}, hold {walled.Hold}, inside {walled.Inside}, orders {simQ.Orders.Total}; {walled.Why}");
    walled.Dead = true; walled.DiedAt = -999;

    var pit = Flat(siteQ.KeepX - 160, siteQ.KeepZ + 120);
    var digger = simQ.Spawn(Kinds.Worker, simQ.NearestStand(pit.x, pit.z, 2));
    int deep = simQ.DesignateRemove(Shapes.Box(wQ, siteQ, pit.x - 20, pit.z - 20, pit.x + 20, pit.z + 20, new Stroke { Height = 32, Dig = true }, 0));
    for (int t = 0; t < 900 * 20 && simQ.Orders.Total > 0; t++) simQ.Step(.05f);
    RunQ(120);
    bool bottom = wQ.SurfAt(pit.x, pit.z) < wQ.SurfAt(pit.x + 60, pit.z) - 24;
    Check("a worker who dug a pit four metres deep comes up out of it by itself, cutting steps up its side",
        ring.x > 0 && pit.x > 0 && deep > 20000 && bottom && FreeQ(digger),
        $"pit at {pit}: {deep} voxels marked, {simQ.Orders.Total} left, the pit dug {bottom}; the digger free {FreeQ(digger)}, way out marked {(digger.WayOutMarked > 0 ? $"at {digger.WayOutMarked:0} s" : "never")}; {digger.Why}");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--pace")
{
    // the pace of the work: four workers raise a stone wall twenty metres long and two high, and dig a trench as long
    // and a metre deep, from the citadel's gate; how long each takes (the game is not about waiting on them)
    Console.WriteLine();
    var mpP = new MapParams();
    var wP = new VoxelWorld(mpP.SizeX, mpP.SizeY, mpP.SizeZ);
    var siteP = MapGen.Generate(wP, mpP);
    wP.Compact();
    var simP = new Sim(wP, siteP);
    simP.Waves.Countdown = 1e9; simP.WildTarget = 0;
    StartingFort.Raise(simP);
    simP.CancelArea(0, 0, wP.SX, wP.SZ);
    var crewP = new List<Unit>();
    for (int i = 0; i < 4; i++) crewP.Add(simP.Spawn(Kinds.Worker, simP.Gate));
    simP.Stock[Stockpile.Stone] += 200.0 * VoxelWorld.PerM3;
    int px = siteP.KeepX + siteP.KeepR + 40, pz = siteP.KeepZ - 80;
    int wallP = simP.DesignateBuild(Shapes.Line(wP, siteP, px, pz, px, pz + 160, new Stroke { Mat = Mat.Stone, Width = 8, Height = 16, Level = true }, Shapes.LevelTop(wP, px, pz, 16)));
    double t0 = simP.Time;
    for (int t = 0; t < 900 * 20 && simP.Orders.Total > 0; t++) simP.Step(.05f);
    double wallSecs = simP.Time - t0;
    int ditchP = simP.DesignateRemove(Shapes.Line(wP, siteP, px + 40, pz, px + 40, pz + 160, new Stroke { Width = 8, Height = 8, Dig = true }, 0));
    t0 = simP.Time;
    for (int t = 0; t < 900 * 20 && simP.Orders.Total > 0; t++) simP.Step(.05f);
    double ditchSecs = simP.Time - t0;
    Check("four workers raise a stone wall 20 m long and 2 m high from the citadel's gate in about a minute, and dig a trench as long in half that",
        wallP > 15000 && ditchP > 8000 && wallSecs < 70 && ditchSecs < 35,
        $"a wall of {wallP} voxels in {wallSecs:0} s ({wallP / Math.Max(1, wallSecs):0} a second), a trench of {ditchP} voxels in {ditchSecs:0} s ({ditchP / Math.Max(1, ditchSecs):0} a second)");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--seal")
{
    // building so that nobody is walled in: a worker raising a ring of wall round itself lays what it can from inside,
    // goes out through the last gap and shuts it from outside; of two inside a ring and one outside, none is shut in
    Console.WriteLine();
    var mpB = new MapParams();
    var wB = new VoxelWorld(mpB.SizeX, mpB.SizeY, mpB.SizeZ);
    var siteB = MapGen.Generate(wB, mpB);
    wB.Compact();
    var simB = new Sim(wB, siteB);
    simB.WildTarget = 0;
    StartingFort.Raise(simB);
    bool FreeB(Unit u) { var cells = new List<int>(); return simB.Paths.Reach(u.Cell, u.T.Move, 240, true, cells) >= 240 || cells.Contains(simB.Gate); }
    // a flat, open spot of the plateau away from the fort
    (int x, int z) FlatB(int fromX, int fromZ)
    {
        for (int r = 0; r < 200; r += 8)
            for (int a = 0; a < 16; a++)
            {
                int x = fromX + (int)(Math.Cos(a * Math.Tau / 16) * r), z = fromZ + (int)(Math.Sin(a * Math.Tau / 16) * r);
                if (x < 60 || z < 60 || x > wB.SX - 60 || z > wB.SZ - 60) continue;
                int y = wB.SurfAt(x, z), lo = y, hi = y;
                for (int dx = -32; dx <= 32; dx += 8) for (int dz = -32; dz <= 32; dz += 8) { int h = wB.SurfAt(x + dx, z + dz); lo = Math.Min(lo, h); hi = Math.Max(hi, h); }
                if (hi - lo <= 3 && !siteB.Forbidden(x, z) && Math.Abs(x - siteB.KeepX) + Math.Abs(z - siteB.KeepZ) > 140) return (x, z);
            }
        return (-1, -1);
    }
    simB.Stock[Stockpile.Wood] += 400.0 * VoxelWorld.PerM3;
    // a ring of logs three metres high round a space three metres across, its walls whole cells of the grid (a thinner
    // wall across two cells is no wall to the feet): higher than the legs climb
    VoxelList Ring((int x, int z) at)
    {
        int x0 = (at.x - 20) >> 3 << 3, z0 = (at.z - 20) >> 3 << 3;
        return Shapes.Box(wB, siteB, x0, z0, x0 + 39, z0 + 39, new Stroke { Mat = Mat.Wood, Width = 8, Height = 24, Level = true, Hollow = true },
            Shapes.LevelTop(wB, at.x, at.z, 24));
    }
    // the inside of a ring, walked from: few cells when the ring is shut
    int Inside((int x, int z) at) { simB.Nav.Flush(); return simB.Paths.Reach(simB.NearestStand(at.x, at.z, 2), Kinds.Worker.Move, 240, true); }
    var one = FlatB(siteB.KeepX + 170, siteB.KeepZ - 70);
    var alone = simB.Spawn(Kinds.Worker, simB.NearestStand(one.x, one.z, 2));
    int marked1 = simB.DesignateBuild(Ring(one));
    for (int t = 0; t < 600 * 20 && simB.Orders.Total > 0; t++) simB.Step(.05f);
    int left1 = simB.Orders.Total, inside1 = Inside(one);
    Check("a worker set to raise a ring of wall round itself goes out and builds it from outside, not walling itself in",
        one.x > 0 && marked1 > 3000 && left1 == 0 && FreeB(alone) && alone.WayOutMarked < 0 && inside1 < 240,
        $"ring at {one}: {marked1} voxels, {left1} left at {simB.Time:0} s; the worker free {FreeB(alone)}, a way out cut {(alone.WayOutMarked > 0 ? "yes" : "no")}, " +
        $"the ring shut {inside1 < 240} ({inside1} cells inside); {alone.Why}");
    var two = FlatB(siteB.KeepX - 170, siteB.KeepZ + 70);
    var crew = new[]
    {
        simB.Spawn(Kinds.Worker, simB.NearestStand(two.x - 4, two.z, 2)), simB.Spawn(Kinds.Worker, simB.NearestStand(two.x + 4, two.z + 4, 2)),
        simB.Spawn(Kinds.Worker, simB.NearestStand(two.x + 40, two.z, 2)),
    };
    double from2 = simB.Time;
    int marked2 = simB.DesignateBuild(Ring(two));
    for (int t = 0; t < 600 * 20 && simB.Orders.Total > 0; t++) simB.Step(.05f);
    int left2 = simB.Orders.Total, inside2 = Inside(two);
    Check("…and of two inside a ring and one outside, all come out before it is shut",
        two.x > 0 && marked2 > 3000 && left2 == 0 && crew.All(FreeB) && crew.All(u => u.WayOutMarked < 0) && inside2 < 240,
        $"ring at {two}: {marked2} voxels, {left2} left in {simB.Time - from2:0} s; free {string.Join(", ", crew.Select(FreeB))}, ways cut {crew.Count(u => u.WayOutMarked > 0)}, " +
        $"the ring shut {inside2 < 240} ({inside2} cells inside); {string.Join("; ", crew.Select(u => u.Why))}");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--house")
{
    // a house drawn with the rectangle: walls, a roof of logs, the door cut and the gate hung once the wall stands,
    // and bunks set in it, each a place in the household
    Console.WriteLine();
    var mpU = new MapParams();
    var wU = new VoxelWorld(mpU.SizeX, mpU.SizeY, mpU.SizeZ);
    var siteU = MapGen.Generate(wU, mpU);
    wU.Compact();
    var simU = new Sim(wU, siteU);
    simU.WildTarget = 0;
    StartingFort.Raise(simU);
    // a flat spot of the plateau a walk from the citadel
    (int x, int z) spot = (-1, -1);
    for (int r = 120; r < 400 && spot.x < 0; r += 8)
        for (int a = 0; a < 24 && spot.x < 0; a++)
        {
            int x = siteU.KeepX + (int)(Math.Cos(a * Math.Tau / 24) * r), z = siteU.KeepZ + (int)(Math.Sin(a * Math.Tau / 24) * r);
            if (x < 80 || z < 80 || x > wU.SX - 80 || z > wU.SZ - 80) continue;
            int lo = int.MaxValue, hi = 0;
            for (int dx = -32; dx <= 32; dx += 4) for (int dz = -32; dz <= 32; dz += 4) { int h = wU.SurfAt(x + dx, z + dz); lo = Math.Min(lo, h); hi = Math.Max(hi, h); if (Mat.IsTrunk(wU.Get(x + dx, h + 2, z + dz))) hi = 999; }
            if (hi - lo <= 2 && !siteU.Forbidden(x, z)) spot = (x, z);
        }
    var crewU = new List<Unit>();
    for (int i = 0; i < 6; i++) crewU.Add(simU.Spawn(Kinds.Worker, simU.Gate));
    simU.Stock[Stockpile.Wood] += 400.0 * VoxelWorld.PerM3;
    int places0 = simU.Places, gates0 = simU.Gates.Count;
    var st = new Stroke { Mat = Mat.Wood, Width = 4, Height = 32, Level = true };
    var house = Shapes.House(wU, siteU, spot.x - 28, spot.z - 16, spot.x + 28, spot.z + 16, st, Shapes.LevelTop(wU, spot.x, spot.z, 32), out var door, out string houseWhy);
    simU.InstantLabour = true;
    simU.DesignateBuild(house);
    simU.InstantLabour = false;
    simU.PlanHouse(door, spot.x - 28, spot.z - 16, spot.x + 28, spot.z + 16);
    for (int t = 0; t < 900 * 20 && simU.Places == places0; t++) simU.Step(.05f);
    var dwelling = simU.Zones.Find(z => z.Kind == Zone.Dwelling);
    Check("a house drawn with the rectangle stands, its door is cut and its gate hung, and bunks are set in it",
        spot.x > 0 && house != null && simU.Gates.Count > gates0 && dwelling is { Closed: true } && simU.Places > places0,
        $"at {spot}: {house?.Count} voxels{(houseWhy != null ? $" ({houseWhy})" : "")}, gates {gates0} → {simU.Gates.Count}, doors waiting {simU.HouseDoors.Count}, " +
        $"the dwelling closed {dwelling?.Closed} ({simU.CheckDwelling(spot.x - 28, spot.z - 16, spot.x + 28, spot.z + 16).Why}), gate ready {simU.Gates[^1].Ready}, " +
        $"orders left {simU.Orders.Total}, places {places0} → {simU.Places} at {simU.Time:0} s");
}

if (args.Length == 0 || args[0] == "--sim" || args[0] == "--acid") AcidBench.Run((what, ok, detail) => Check(what, ok, detail));
// the hill giant and the great ones' boulders (Giants.cs)
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--giants") GiantsBench.Run(Check);
// the magic wand: what it takes, laying over again, its blueprint (WandBench.cs)
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--wand") WandBench.Run(Check);
// houses the workers build: roofed whole, floored, furnished, and a storey on a storey (Houses.cs)
if (args.Length == 0 || args[0] == "--sim" || args[0] == "--houses") HousesBench.Run(Check);

if (args.Length > 0 && args[0] == "--stress")
{
    // not in the usual run: a long sandbox under fire, to find a step that takes too long or never ends. Every second
    // and a half a spell at a random spot near the citadel, every twenty seconds a band called up, telekinesis thrown;
    // each step timed, and one that has not ended in twenty seconds told with the stage and the unit it is stuck in
    Console.WriteLine();
    int seed = args.Length > 1 && int.TryParse(args[1], out int sd) ? sd : 7;
    double minutes = args.Length > 2 && double.TryParse(args[2], out double mn) ? mn : 15;
    var mpX = new MapParams();
    var wX = new VoxelWorld(mpX.SizeX, mpX.SizeY, mpX.SizeZ);
    var siteX = MapGen.Generate(wX, mpX);
    wX.Compact();
    var simX = new Sim(wX, siteX);
    StartingFort.Raise(simX);
    simX.BeginSandbox();
    foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Archer, Kinds.Archer, Kinds.Swordsman, Kinds.Swordsman }) simX.Spawn(t, simX.Gate);
    var rnd = new Random(seed);
    var kinds = new[] { SpellKind.Explosion, SpellKind.Meteor, SpellKind.Quake, SpellKind.Fall, SpellKind.Rift, SpellKind.FireRain, SpellKind.Thunder,
        SpellKind.BlackWind, SpellKind.Whirlwind, SpellKind.Hand, SpellKind.Mire, SpellKind.Plague, SpellKind.Chains, SpellKind.Raise, SpellKind.DeadFog,
        SpellKind.Dread, SpellKind.Telekinesis };
    string doing = "";
    long stepStart = 0;
    var stepping = new System.Threading.ManualResetEventSlim(false);
    var dog = new System.Threading.Thread(() =>
    {
        while (true)
        {
            System.Threading.Thread.Sleep(1000);
            if (!stepping.IsSet) continue;
            double took = (System.Diagnostics.Stopwatch.GetTimestamp() - System.Threading.Interlocked.Read(ref stepStart)) / (double)System.Diagnostics.Stopwatch.Frequency;
            if (took < 20) continue;
            Console.WriteLine($"FAIL a step has not ended in {took:0} s: stage {Sim.Stage}, unit {Sim.StageUnit}; at {simX.Time:0.0} s after «{doing}»");
            Environment.Exit(3);
        }
    }) { IsBackground = true };
    dog.Start();
    var slow = new List<(double ms, double at, string after, string stage)>();
    double nextSpell = 1, nextBand = 2, holdUntil = -1, total = 0, worst = 0;
    var clock = System.Diagnostics.Stopwatch.StartNew();
    while (simX.Time < minutes * 60)
    {
        if (simX.Time >= nextSpell)
        {
            nextSpell = simX.Time + 1.5;
            var k = kinds[rnd.Next(kinds.Length)];
            double ang = rnd.NextDouble() * Math.Tau, r = 60 + rnd.NextDouble() * 160;
            int x = Math.Clamp(siteX.KeepX + (int)(Math.Cos(ang) * r), 4, wX.SX - 5), z = Math.Clamp(siteX.KeepZ + (int)(Math.Sin(ang) * r), 4, wX.SZ - 5);
            int y = wX.SurfAt(x, z) + 1;
            string why = simX.Cast(k, x, y, z, x + 40, z + 10);
            doing = $"{k} at {x},{y},{z}" + (why != null ? $" (refused: {why})" : "");
            if (k == SpellKind.Telekinesis && why == null) { holdUntil = simX.Time + 2; simX.HoldAt(x, y + 60, z); }
        }
        if (holdUntil > 0 && simX.Time >= holdUntil) { simX.LetGo(); holdUntil = -1; doing = "telekinesis let go"; }
        if (simX.Time >= nextBand)
        {
            nextBand = simX.Time + 20;
            double ang = rnd.NextDouble() * Math.Tau;
            int x = Math.Clamp(siteX.KeepX + (int)(Math.Cos(ang) * 150), 4, wX.SX - 5), z = Math.Clamp(siteX.KeepZ + (int)(Math.Sin(ang) * 150), 4, wX.SZ - 5);
            bool heavy = rnd.Next(3) == 0;
            simX.Summon(x, z, heavy);
            doing = $"{(heavy ? "heavy band" : "band")} at {x},{z}";
        }
        System.Threading.Interlocked.Exchange(ref stepStart, System.Diagnostics.Stopwatch.GetTimestamp());
        stepping.Set();
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        simX.Step(.05f);
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        stepping.Reset();
        total += ms; worst = Math.Max(worst, ms);
        if (ms > 150) slow.Add((ms, simX.Time, doing, Sim.Stage));
    }
    slow.Sort((p1, p2) => p2.ms.CompareTo(p1.ms));
    Console.WriteLine($"stress: {minutes} min of the sandbox in {clock.Elapsed.TotalSeconds:0} s, a step {total / (minutes * 1200):0.0} ms on average, the worst {worst:0} ms; " +
        $"{simX.Units.Count} bodies, {simX.Bands.Count} bands at the end");
    foreach (var (ms, at, after, stage) in slow.GetRange(0, Math.Min(12, slow.Count))) Console.WriteLine($"  {ms,7:0} ms at {at,6:0.0} s after {after}");
    Check("the sandbox under fire: no step takes as long as a second", worst < 1000, $"worst {worst:0} ms");
}

Console.WriteLine(failures == 0 ? "\nall checks passed" : $"\n{failures} check(s) failed");
return failures == 0 ? 0 : 1;
