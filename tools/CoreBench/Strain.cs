using System;
using System.Collections.Generic;
using VoxelTower.Core;

/// <summary>
/// Weight and cracks, and buildings going over whole (--strain): a column holds, one carrying too much cracks and gives
/// way, a tower dug out under one side goes over that way and smashes the wall it falls on, one dug out a little less
/// cracks on that side first and goes over after, one propped up again stops cracking, blows crack a wall till it gives
/// way, a fort as a player builds one stands uncracked, a slab into a pond splashes, mending lays cracked stones anew,
/// a great ring hacked at costs no long steps, and the cracks are kept in a save. STRAIN_POSE=1 plays the undermine
/// pose instead, a line every quarter second.
/// </summary>
static class StrainBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        int kx = site.KeepX, kz = site.KeepZ;
        int Count(Func<byte, bool> what, int x0, int z0, int x1, int z1, int y0 = 0, int y1 = 191)
        {
            int n = 0;
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) if (what(w.Get(x, y, z))) n++;
            return n;
        }
        int CracksIn(int x0, int z0, int x1, int z1)
        {
            int n = 0;
            foreach (int p in sim.Cracks.Keys) { var (x, _, z) = WaterPlanner.Unpack(p); if (x >= x0 && x <= x1 && z >= z0 && z <= z1) n++; }
            return n;
        }
        // a level patch of ground: the highest of it, with everything over it cleared
        int Flatten(int x0, int z0, int x1, int z1)
        {
            int g = 0;
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) g = Math.Max(g, w.SurfAt(x, z));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    for (int y = g; y < g + 100; y++) w.Set(x, y, z, Mat.Air);
                    for (int y = g - 1; y >= 0 && !Mat.IsAnchor[w.Get(x, y, z)]; y--) w.Set(x, y, z, Mat.Soil);
                    for (int y = g - 8; y < g; y++) w.Set(x, y, z, Mat.Soil);
                }
            return g;
        }
        static bool Stone(byte m) => Mat.IsMasonry(m);
        if (Environment.GetEnvironmentVariable("STRAIN_POSE") != null)
        {
            int tx = site.KeepX - site.KeepR - 80, tz = site.KeepZ + 30, g = w.SurfAt(tx, tz);
            for (int z = tz - 50; z < tz + 50; z++)
                for (int x = tx - 30; x < tx + 100; x++)
                {
                    for (int y = g; y < g + 90; y++) if (w.Get(x, y, z) != Mat.Air) w.Set(x, y, z, Mat.Air);
                    for (int y = g - 6; y < g; y++) if (!Mat.IsEarthy[w.Get(x, y, z)]) w.Set(x, y, z, Mat.Soil);
                    w.Set(x, g - 1, z, Mat.Grass);
                }
            sim.InstantLabour = true;
            sim.DesignateBuild(Shapes.Circle(w, site, tx, tz, 12, new Stroke { Mat = Mat.Stone, Width = 4, Height = 64, Level = true, Hollow = true }, g + 64));
            sim.DesignateBuild(Shapes.Line(w, site, tx + 44, tz - 40, tx + 44, tz + 24, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Level = true }, g + 32));
            sim.InstantLabour = false;
            Run(1);
            int ring = Count(Stone, tx - 14, tz - 14, tx + 14, tz + 14, g, g + 70);
            sim.Collapsed = (n, x, y, z) => Console.WriteLine($"  t {sim.Time:0.00}: collapsed {n} at {x - tx:0},{y - g:0},{z - tz:0}");
            sim.GoingOver = f => Console.WriteLine($"  t {sim.Time:0.00}: going over {f.Voxels.Count}");
            sim.Crumbled = (x, y, z, m) => Console.WriteLine($"  t {sim.Time:0.00}: crumbled {x - tx},{y - g},{z - tz}");
            for (int z = tz - 14; z <= tz + 14; z++) for (int x = tx + 3; x <= tx + 16; x++) for (int y = g - 4; y < g; y++) w.Set(x, y, z, Mat.Air);
            double worstStep = 0;
            for (int k = 0; k < 80; k++)
            {
                for (int j = 0; j < 5; j++)
                {
                    long s0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    sim.Step(.05f);
                    worstStep = Math.Max(worstStep, (System.Diagnostics.Stopwatch.GetTimestamp() - s0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                }
                Console.WriteLine($"  t {sim.Time:0.00}: {Count(Stone, tx - 14, tz - 14, tx + 14, tz + 14, g, g + 70)} of {ring} standing, {sim.Cracks.Count} cracks, {sim.Fliers.Count} fliers");
                if (sim.Time > 99) foreach (var f in sim.Fliers) Console.WriteLine($"     flier {f.Id} {f.Voxels.Count} at {f.X - tx:0},{f.Y - g:0},{f.Z - tz:0} v {f.VX:0},{f.VY:0},{f.VZ:0} rolling {f.Rolling} tipping {f.Tipping} since {sim.Time - f.Since:0.0}");
            }
            int highest = 0;
            for (int z = tz - 50; z < tz + 50; z++) for (int x = tx - 30; x < tx + 100; x++) for (int y = g + 90; y >= g; y--) if (Stony(w.Get(x, y, z))) { highest = Math.Max(highest, y - g); break; }
            Console.WriteLine($"  highest stone or rubble at the end: {highest} over the ground; the slowest step {worstStep:0.0} ms");
            return;
        }
        static bool Stony(byte m) => Mat.IsMasonry(m) || m == Mat.Rubble;

        // a column of stone a quarter metre square and five high stands uncracked; the same with a slab of two metres
        // square on it cracks at once, and gives way within a few seconds, the slab coming down
        int cx0 = kx - 180, cz0 = kz + 150, cg = Flatten(cx0 - 20, cz0 - 20, cx0 + 60, cz0 + 20);
        for (int z = cz0; z < cz0 + 2; z++) for (int x = cx0; x < cx0 + 2; x++) for (int y = cg; y < cg + 40; y++) w.Set(x, y, z, Mat.Stone);
        Run(3);
        int alone = CracksIn(cx0 - 4, cz0 - 4, cx0 + 6, cz0 + 6);
        int sx0 = cx0 + 36;
        for (int z = cz0; z < cz0 + 2; z++) for (int x = sx0; x < sx0 + 2; x++) for (int y = cg; y < cg + 16; y++) w.Set(x, y, z, Mat.Stone);
        for (int z = cz0 - 7; z < cz0 + 9; z++) for (int x = sx0 - 7; x < sx0 + 9; x++) for (int y = cg + 16; y < cg + 24; y++) w.Set(x, y, z, Mat.Stone);
        int slab0 = Count(Stone, sx0 - 7, cz0 - 7, sx0 + 8, cz0 + 8, cg + 16, cg + 24);
        Run(.5);
        int early = CracksIn(sx0 - 8, cz0 - 8, sx0 + 9, cz0 + 9);
        double gaveAt = -1;
        for (int t = 0; t < 120 && gaveAt < 0; t++) { Run(.1); if (Count(Stone, sx0 - 7, cz0 - 7, sx0 + 8, cz0 + 8, cg + 16, cg + 24) < slab0 / 2) gaveAt = t * .1 + .5; }
        check("a stone column five metres high stands uncracked; one with a slab on it far heavier than it bears cracks at once and gives way in seconds",
            alone == 0 && early > 0 && gaveAt > 1 && gaveAt < 10,
            $"{alone} cracks in the lone column; {early} cracks half a second after the slab was laid on the other, {slab0} voxels of slab gone from its height after {gaveAt:0.0} s");

        // a hollow tower of stone two and a half metres square and eight high, a wall of stone a few metres east of it;
        // the ground dug out under the east two thirds of the tower: it goes over east, whole, onto the wall, breaking it,
        // and lies in rubble beyond its foot; the goblin standing between them is struck
        int tx0 = kx - 200, tz0 = kz - 170, tg = Flatten(tx0 - 30, tz0 - 30, tx0 + 120, tz0 + 50);
        void Tower(int x0, int z0, int g)
        {
            for (int z = z0; z < z0 + 20; z++)
                for (int x = x0; x < x0 + 20; x++)
                    for (int y = g; y < g + 64; y++)
                        if (x < x0 + 3 || x >= x0 + 17 || z < z0 + 3 || z >= z0 + 17 || y >= g + 60) w.Set(x, y, z, Mat.Stone);
        }
        Tower(tx0, tz0, tg);
        int wallX = tx0 + 44;
        for (int z = tz0 - 20; z < tz0 + 40; z++) for (int x = wallX; x < wallX + 6; x++) for (int y = tg; y < tg + 32; y++) w.Set(x, y, z, Mat.Stone);
        Run(2);
        int wall0 = Count(Stone, wallX, tz0 - 4, wallX + 5, tz0 + 24, tg, tg + 40);
        var goblin = sim.Spawn(Kinds.Goblin, sim.NearestStand(tx0 + 32, tz0 + 10, 2));
        goblin.Raid = null; goblin.Guard = true; goblin.Home = goblin.Cell;
        int over = 0;
        sim.GoingOver = f => over++;
        double t0 = sim.Time;
        for (int z = tz0 - 2; z < tz0 + 22; z++) for (int x = tx0 + 7; x < tx0 + 24; x++) for (int y = tg - 6; y < tg; y++) w.Set(x, y, z, Mat.Air);
        double wentAt = -1;
        for (int t = 0; t < 100 && wentAt < 0; t++) { Run(.1); if (over > 0) wentAt = sim.Time - t0; }
        Run(8);
        int standing = Count(Stone, tx0, tz0, tx0 + 19, tz0 + 19, tg + 24, tg + 70), beyond = Count(Stony, tx0 + 24, tz0 - 20, tx0 + 110, tz0 + 40, tg - 6, tg + 30);
        int wall1 = Count(Stone, wallX, tz0 - 4, wallX + 5, tz0 + 24, tg, tg + 40);
        check("a tower dug out under two thirds of its foot goes over that way whole within a second, breaks the wall it falls on and lies in rubble beyond its foot; the goblin under it is struck",
            wentAt >= 0 && wentAt < 1 && standing < 200 && beyond > 6000 && wall1 < wall0 - 200 && (goblin.Dead || goblin.Hp < goblin.MaxHp),
            $"went over after {wentAt:0.0} s ({over} times); {standing} of it standing over three metres, {beyond} of stone and rubble beyond its foot; the wall {wall0} → {wall1}; the goblin {(goblin.Dead ? "dead, " + goblin.KilledBy : $"{goblin.Hp:0} of {goblin.MaxHp:0}")}");
        if (!goblin.Dead) { goblin.Dead = true; goblin.DiedAt = -999; }
        sim.GoingOver = null;

        // the same tower dug out under the east two fifths only: it stands at first, cracks along its east foot, crumbles there
        // and goes over east after some seconds
        int ux0 = kx - 100, uz0 = kz + 250, ug = Flatten(ux0 - 30, uz0 - 30, ux0 + 90, uz0 + 50);
        Tower(ux0, uz0, ug);
        Run(2);
        over = 0;
        sim.GoingOver = f => over++;
        for (int z = uz0 - 2; z < uz0 + 22; z++) for (int x = ux0 + 12; x < ux0 + 24; x++) for (int y = ug - 6; y < ug; y++) w.Set(x, y, z, Mat.Air);
        t0 = sim.Time;
        Run(1);
        bool stoodFirst = over == 0;
        int cracked = 0;
        double went2 = -1;
        for (int t = 0; t < 400 && went2 < 0; t++) { Run(.1); cracked = Math.Max(cracked, CracksIn(ux0, uz0, ux0 + 19, uz0 + 19)); if (over > 0) went2 = sim.Time - t0; }
        Run(8);
        int east = Count(Stony, ux0 + 24, uz0 - 20, ux0 + 90, uz0 + 40, ug - 6, ug + 30), west = Count(Stony, ux0 - 30, uz0 - 20, ux0 - 4, uz0 + 40, ug - 6, ug + 30);
        check("a tower dug out under two fifths of its foot stands at first, cracks along that side and goes over that way some seconds after",
            stoodFirst && cracked > 20 && went2 > 2 && went2 < 40 && east > 4 * west + 2000,
            $"stood the first second {stoodFirst}; {cracked} cracks at most; went over after {went2:0.0} s; stone and rubble east {east}, west {west}");
        sim.GoingOver = null;

        // the same tower dug out under two fifths of its foot and the hole filled with earth again a second after, while
        // it cracks: propped up, it stops cracking and stands
        int qx0 = kx - 300, qz0 = kz - 280, qg = Flatten(qx0 - 30, qz0 - 30, qx0 + 60, qz0 + 50);
        Tower(qx0, qz0, qg);
        Run(2);
        over = 0;
        sim.GoingOver = f => over++;
        for (int z = qz0 - 2; z < qz0 + 22; z++) for (int x = qx0 + 12; x < qx0 + 24; x++) for (int y = qg - 6; y < qg; y++) w.Set(x, y, z, Mat.Air);
        Run(1.2);
        int propped0 = CracksIn(qx0, qz0, qx0 + 19, qz0 + 19), stood0 = Count(Stone, qx0, qz0, qx0 + 19, qz0 + 19, qg, qg + 70);
        float sum0 = 0; foreach (var (p, c) in sim.Cracks) { var (x, _, z) = WaterPlanner.Unpack(p); if (x >= qx0 && x < qx0 + 20 && z >= qz0 && z < qz0 + 20) sum0 += c; }
        for (int z = qz0 - 2; z < qz0 + 22; z++) for (int x = qx0 + 12; x < qx0 + 24; x++) for (int y = qg - 6; y < qg; y++) w.Set(x, y, z, Mat.Soil);
        // (the weight is found again on what it stands on within a moment)
        Run(1);
        sum0 = 0; foreach (var (p, c) in sim.Cracks) { var (x, _, z) = WaterPlanner.Unpack(p); if (x >= qx0 && x < qx0 + 20 && z >= qz0 && z < qz0 + 20) sum0 += c; }
        Run(9);
        float sum1 = 0; foreach (var (p, c) in sim.Cracks) { var (x, _, z) = WaterPlanner.Unpack(p); if (x >= qx0 && x < qx0 + 20 && z >= qz0 && z < qz0 + 20) sum1 += c; }
        int stood1 = Count(Stone, qx0, qz0, qx0 + 19, qz0 + 19, qg, qg + 70);
        check("a tower cracking over a hole dug under it, propped up by filling the hole with earth again, stops cracking and stands",
            propped0 > 0 && over == 0 && sum1 < sum0 + 1 && stood1 > stood0 - 60,
            $"{propped0} cracks when filled, {sum0:0.0} in all a second after, {sum1:0.0} ten seconds after; went over {over} times; {stood0} → {stood1} standing");
        sim.GoingOver = null;

        // a wall struck three times at one spot: cracked about it by the first blow, cracked through by the third
        int bx0 = kx - 60, bz0 = kz + 330, bg = Flatten(bx0 - 10, bz0 - 10, bx0 + 50, bz0 + 20);
        for (int z = bz0; z < bz0 + 8; z++) for (int x = bx0; x < bx0 + 40; x++) for (int y = bg; y < bg + 24; y++) w.Set(x, y, z, Mat.Stone);
        Run(1);
        int wallB = Count(Stone, bx0, bz0, bx0 + 39, bz0 + 7, bg, bg + 30);
        sim.Jar(bx0 + 20, bg + 12, bz0 - 1, 8, .55f);
        Run(.5);
        int one = CracksIn(bx0, bz0, bx0 + 39, bz0 + 7), lost1 = wallB - Count(Stone, bx0, bz0, bx0 + 39, bz0 + 7, bg, bg + 30);
        sim.Jar(bx0 + 20, bg + 12, bz0 - 1, 8, .55f); Run(.5);
        sim.Jar(bx0 + 20, bg + 12, bz0 - 1, 8, .55f); Run(2);
        int lost3 = wallB - Count(Stone, bx0, bz0, bx0 + 39, bz0 + 7, bg, bg + 30);
        check("blows crack a wall about where they land, and three at one spot crack it through there",
            one > 30 && lost1 == 0 && lost3 > 10,
            $"{one} cracked by the first blow, {lost1} given way; {lost3} given way after the third");

        // a fort as a player builds one — a wall three metres high and a metre and a half thick, a gate in it, a round
        // tower at its end with earth rammed inside, a timber floor on the wall — stands a minute without a crack
        sim.Stock[Stockpile.Stone] = sim.Stock[Stockpile.Earth] = 1e7;
        sim.InstantLabour = true;
        int fx = kx + 80, fz0 = kz - 120, fz1 = kz + 60;
        int top = Shapes.LevelTop(w, fx, (fz0 + fz1) / 2, 40);
        sim.DesignateBuild(Shapes.Line(w, site, fx, fz0, fx, fz1, new Stroke { Mat = Mat.Stone, Width = 12, Height = 40, Level = true }, top));
        int tt = Shapes.LevelTop(w, fx, fz1 + 8, 64);
        sim.DesignateBuild(Shapes.Circle(w, site, fx, fz1 + 8, 15, new Stroke { Mat = Mat.Stone, Width = 5, Height = 64, Level = true, Hollow = true }, tt));
        sim.DesignateBuild(Shapes.Circle(w, site, fx, fz1 + 8, 11, new Stroke { Mat = Mat.Rammed, Width = 1, Height = 56, Level = true }, Shapes.LevelTop(w, fx, fz1 + 8, 56)));
        sim.InstantLabour = false;
        Run(2);
        // the opening of a gate, two metres wide and three high, through it
        int gz = (fz0 + fz1) / 2;
        for (int z = gz - Gateway.Half; z < gz + Gateway.Half; z++)
            for (int x = fx - 8; x <= fx + 8; x++)
            {
                int foot = top - 60;
                while (foot < top && !Mat.IsMasonry(w.Get(x, foot, z))) foot++;
                for (int y = foot; y < foot + Gateway.Height; y++) if (Mat.IsMasonry(w.Get(x, y, z))) w.Set(x, y, z, Mat.Air);
            }
        Run(60);
        int fortCracks = CracksIn(fx - 30, fz0 - 30, fx + 30, fz1 + 40), fortStands = Count(Stone, fx - 8, gz - 6, fx + 8, gz + 6, top - 12, top);
        check("a fort as a player builds one, a gate's opening in its wall and a tower at its end, stands a minute without a crack", fortCracks == 0 && fortStands > 1000,
            $"{fortCracks} cracks, {fortStands} of wall over the opening");

        // a slab of stone let fall into a pond: it goes into the water with a splash and lies on the bottom, broken up
        var (pondX, pondZ, _, level) = site.Ponds[1];
        int surface = w.SurfAt(pondX, pondZ), splashes = 0, splashN = 0;
        sim.Plunged = (x, y, z, n, speed) => { splashes++; splashN = Math.Max(splashN, n); };
        var slab = new List<(sbyte, sbyte, sbyte, byte)>();
        for (int dz = -6; dz < 6; dz++) for (int dy = -2; dy < 2; dy++) for (int dx = -10; dx < 10; dx++) slab.Add(((sbyte)dx, (sbyte)dy, (sbyte)dz, Mat.Stone));
        sim.Hurl(slab, pondX + .5f, level + 40, pondZ + .5f, 0, 0, 0, "обвал");
        Run(4);
        int underWater = 0;
        for (int z = pondZ - 20; z <= pondZ + 20; z++) for (int x = pondX - 24; x <= pondX + 24; x++) for (int y = surface - 4; y < level; y++) if (Stony(w.Get(x, y, z))) underWater++;
        check("a slab let fall into a pond goes in with a splash and lies under the water", splashes > 0 && splashN >= 900 && underWater > 600,
            $"{splashes} splashes, the greatest of {splashN} voxels; {underWater} of stone and rubble under the surface");
        sim.Plunged = null;

        // a cracked wall of ours mended: the cracked stones are taken out and laid anew, and the cracks are gone
        sim.Jar(fx - 7, top - 12, gz + 30, 7, .7f);
        int crackedFort = CracksIn(fx - 10, gz + 20, fx + 10, gz + 40);
        var lay = new VoxelList(); var clear = new VoxelList();
        sim.MendPlan(sim.MeantIn(fx - 10, gz + 20, fx + 10, gz + 40), lay, clear);
        sim.InstantLabour = true; sim.Mend(lay, clear); sim.InstantLabour = false;
        Run(2);
        int mended = CracksIn(fx - 10, gz + 20, fx + 10, gz + 40), fortAfter = Count(Stone, fx - 8, gz + 20, fx + 8, gz + 40, top - 40, top);
        check("mending a cracked wall of ours lays its cracked stones anew: the cracks are gone and the wall stands", crackedFort > 20 && clear.Count > 0 && mended < crackedFort / 3 && fortAfter > 2500,
            $"{crackedFort} cracks, {clear.Count} stones to lay anew, {lay.Count} to lay; {mended} cracks after, {fortAfter} of wall there");

        // a great ring of stone, more than is weighed as one body, hacked away at its foot a voxel a step, as a band
        // hacks a breach: no step takes long for the weighing
        int gx0 = kx - 420, gz0 = kz + 60, gg = Flatten(gx0 - 50, gz0 - 50, gx0 + 50, gz0 + 50);
        for (int z = gz0 - 44; z <= gz0 + 44; z++)
            for (int x = gx0 - 44; x <= gx0 + 44; x++)
            {
                float d = MathF.Sqrt((x - gx0) * (x - gx0) + (z - gz0) * (z - gz0));
                if (d >= 36 && d < 44) for (int y = gg; y < gg + 32; y++) w.Set(x, y, z, Mat.Stone);
            }
        Run(2);
        int ringN = Count(Stone, gx0 - 45, gz0 - 45, gx0 + 45, gz0 + 45, gg - 2, gg + 40);
        double slowest = 0;
        int cut = 0;
        for (int y = gg; y < gg + 12 && cut < 240; y++)
            for (int x = gx0 - 44; x <= gx0 - 36 && cut < 240; x++)
                for (int z = gz0 - 6; z <= gz0 + 6 && cut < 240; z += 3)
                {
                    if (!Mat.IsMasonry(w.Get(x, y, z))) continue;
                    w.Set(x, y, z, Mat.Air);
                    cut++;
                    long s0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    sim.Step(.05f);
                    slowest = Math.Max(slowest, (System.Diagnostics.Stopwatch.GetTimestamp() - s0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                }
        check("a great ring hacked away at its foot a voxel a step: no step takes long for the weighing", cut > 100 && slowest < 40,
            $"{ringN} voxels of ring, {cut} hacked out, the slowest step {slowest:0.0} ms");

        // the cracks are kept in a save and read back
        sim.Jar(fx - 7, top - 12, (fz0 + fz1) / 2 - 30, 6, .4f);
        int before = sim.Cracks.Count;
        float sum = 0; foreach (var c in sim.Cracks.Values) sum += c;
        var ms = new System.IO.MemoryStream();
        sim.Save(ms, mp.Seed, "strain", "");
        ms.Position = 0;
        Sim back;
        using (var reader = new SaveReader(ms))
        {
            var wL = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
            var siteL = MapGen.Generate(wL, new MapParams { Seed = reader.Seed });
            wL.Compact();
            reader.Valley(wL, siteL);
            back = new Sim(wL, siteL);
            back.Load(reader);
        }
        float sumBack = 0; foreach (var c in back.Cracks.Values) sumBack += c;
        check("the cracks are kept in a save", before > 0 && back.Cracks.Count == before && Math.Abs(sumBack - sum) < .01f,
            $"{before} cracks ({sum:0.0} in all) saved, {back.Cracks.Count} ({sumBack:0.0}) read back");
    }
}
