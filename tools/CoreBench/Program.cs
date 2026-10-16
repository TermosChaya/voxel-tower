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
    void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
    Check("a gate before the citadel", sim.Gate >= 0, sim.Gate.ToString());
    Check("cells beside the citadel to batter", sim.Goals.Count > 20, sim.Goals.Count.ToString());

    sim.Stock.Gold = 2000;
    var crew = new System.Collections.Generic.List<Unit>();
    for (int i = 0; i < 4; i++) crew.Add(sim.Hire(Kinds.Worker));
    Check("workers hired at the gate", crew.TrueForAll(u => u != null));

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

    // the horde finds its way; through a stone ring it has to hack
    sw.Restart();
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
        if (!u.Dead) Console.WriteLine($"     {u.T.Name} #{u.Id}: {u.State}, {u.Hp:0} hp, at {u.X / 16:0.0},{u.Y / 16:0.0},{u.Z / 16:0.0} m{(u.Why != "" ? ", " + u.Why : "")}");
    sim.Waves.Countdown = 1e9;

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
    // the orc alone against the stone: the garrison leaves the field
    foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
    sim.Step(.05f);
    var ringStroke = new Stroke { Mat = Mat.Stone, Width = 8, Height = 48, Hollow = true };
    sim.DesignateBuild(Shapes.Circle(w4, site4, site4.KeepX, site4.KeepZ, site4.KeepR + 44, ringStroke, 0));
    sim.InstantLabour = false;
    sim.ComputeFields();
    int hacked = 0;
    sim.Hacked = (x, y, z, m) => hacked++;
    var orc = sim.Spawn(Kinds.Orc, sim.NearestStand(site4.KeepX - site4.KeepR - 100, site4.KeepZ, 4));
    float d0 = MathF.Sqrt((orc.X - site4.KeepX) * (orc.X - site4.KeepX) + (orc.Z - site4.KeepZ) * (orc.Z - site4.KeepZ));
    Run(120);
    float d1 = MathF.Sqrt((orc.X - site4.KeepX) * (orc.X - site4.KeepX) + (orc.Z - site4.KeepZ) * (orc.Z - site4.KeepZ));
    Check("an orc hacks through the stone ring", hacked > 200 && d1 < d0 - 40, $"{hacked} voxels hacked, {d0 / 16:0.0} → {d1 / 16:0.0} m from the citadel, {orc.State}{(orc.Dead ? " (" + orc.KilledBy + ")" : "")}");

}

Console.WriteLine(failures == 0 ? "\nall checks passed" : $"\n{failures} check(s) failed");
return failures == 0 ? 0 : 1;
