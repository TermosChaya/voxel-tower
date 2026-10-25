using System;
using System.Diagnostics;
using System.IO;
using VoxelTower.Core;

/// <summary>
/// The bench's liquids (--liquids): water breached out of a reservoir into a walled basin runs out over seconds, not at
/// once, as a front moving at the pace water runs, and settles level on both sides with nothing made or lost; the amounts
/// are saved and read back mid-flow; a big flood out over open land costs little a second.
/// </summary>
static class LiquidsBench
{
    static (Sim sim, VoxelWorld w, Site site) NewSim()
    {
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        return (sim, w, site);
    }

    /// <summary>The game written down and read back into a valley made again.</summary>
    static Sim SavedAndRead(Sim sim)
    {
        var mp = new MapParams();
        var ms = new MemoryStream();
        sim.Save(ms, mp.Seed, "test", "");
        ms.Position = 0;
        var w2 = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        using var reader = new SaveReader(ms);
        var site2 = MapGen.Generate(w2, new MapParams { Seed = reader.Seed });
        w2.Compact();
        reader.Valley(w2, site2);
        var sim2 = new Sim(w2, site2);
        sim2.Load(reader);
        return sim2;
    }

    /// <summary>A level field of soil, air over it; its height.</summary>
    static int Field(Sim sim, VoxelWorld w, int fx0, int fx1, int fz0, int fz1)
    {
        int g = 0;
        for (int z = fz0; z < fz1; z++) for (int x = fx0; x < fx1; x++) g = Math.Max(g, w.SurfAt(x, z));
        g += 2;
        for (int z = fz0; z < fz1; z++)
            for (int x = fx0; x < fx1; x++)
            {
                for (int y = g; y < g + 60; y++) w.SetRaw(x, y, z, Mat.Air);
                for (int y = g - 40; y < g; y++) w.SetRaw(x, y, z, Mat.Soil);
            }
        for (int z = fz0; z < fz1; z++) for (int x = fx0; x < fx1; x++) w.Set(x, g - 1, z, Mat.Soil);
        sim.Nav.BuildAll();
        return g;
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        Basin(check);
        Wave(check);
        Lava(check);
        Flood(check);
    }

    /// <summary>A reservoir 4 m deep breached into a walled basin beside it.</summary>
    static void Basin(Action<string, bool, string> check)
    {
        var (sim, w, site) = NewSim();
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        int kx = site.KeepX, kz = site.KeepZ;
        int fx0 = kx - 420, fx1 = kx - 60, fz0 = kz - 120, fz1 = kz + 120;
        int g = Field(sim, w, fx0, fx1, fz0, fz1);
        Run(.5);
        // the reservoir 6 m across, full, and east of it the basin as long, its walls of stone 4,5 m high
        int rx0 = fx0 + 20, rx1 = rx0 + 48, bx1 = rx1 + 48, rz0 = kz - 24, rz1 = kz + 24;
        for (int z = rz0; z < rz1; z++)
            for (int x = rx0; x < bx1; x++)
            {
                bool wall = x < rx0 + 4 || (x >= rx1 - 4 && x < rx1) || x >= bx1 - 4 || z < rz0 + 4 || z >= rz1 - 4;
                for (int y = g; y < g + 36; y++) w.Set(x, y, z, wall ? Mat.Stone : y < g + 32 && x < rx1 ? Mat.Water : Mat.Air);
            }
        Run(1);
        double Water(Sim s, int x0, int x1)
        {
            double n = 0;
            for (int z = rz0; z < rz1; z++) for (int x = x0; x < x1; x++) for (int y = g; y < g + 40; y++) n += s.LiquidAt(x, y, z, out byte k) * (k == Sim.LiquidWater ? 1 : 0);
            return n;
        }
        int Front() { int far = 0; for (int x = rx1; x < bx1; x++) for (int z = rz0; z < rz1; z += 2) if (w.Get(x, g, z) == Mat.Water) { far = x - rx1; break; } return far; }
        double total = sim.LiquidMass(Mat.Water), inside0 = Water(sim, rx0, rx1 - 4);
        for (int z = kz - 8; z < kz + 8; z++) for (int x = rx1 - 4; x < rx1; x++) for (int y = g; y < g + 36; y++) w.Set(x, y, z, Mat.Air);
        Run(.5);
        double out05 = Water(sim, rx1, bx1);
        int front05 = Front();
        Run(1.5);
        double out2 = Water(sim, rx1, bx1);
        int front2 = Front();

        // saved mid-flow and read back: the amounts as they were, and it runs on from them
        double saved = sim.LiquidMass(Mat.Water), part = Water(sim, rx1, bx1);
        var sim2 = SavedAndRead(sim);
        double loaded = sim2.LiquidMass(Mat.Water), part2 = Water(sim2, rx1, bx1);
        sim2.Step(.05f);
        check("the liquids are saved mid-flow and read back as they were, and run on",
            Math.Abs(loaded - saved) < 1 && Math.Abs(part2 - part) < 1 && sim2.WaterBusy,
            $"{saved:0.0} voxels of water saved, {loaded:0.0} read back; {part:0.0} in the basin, read back {part2:0.0}");

        double settledIn = 2;
        var sw = Stopwatch.StartNew();
        int Top(int x) { int y = g; while (w.Get(x, y, kz) == Mat.Water) y++; return y - g; }
        while (sim.WaterBusy && settledIn < 300) { Run(1); settledIn++; }
        double total1 = sim.LiquidMass(Mat.Water);
        int inTop = Top(rx0 + 20), outTop = Top(rx1 + 30);
        Console.WriteLine($"  [liquids] reservoir {inside0:0} voxels; in the basin after 0,5 s {out05:0} (front {front05}), after 2 s {out2:0} (front {front2}); " +
            $"settled in {settledIn:0} s ({sw.ElapsedMilliseconds} ms); level {inTop} inside and {outTop} in the basin; mass {total:0.0} → {total1:0.0} (dried {sim.LiquidDried:0.0})");
        check("water breached out of a reservoir pours out over seconds, its front at the pace water runs, and settles level; nothing made or lost",
            out05 < inside0 * .1 && out2 > out05 * 2 && front2 > front05 && front2 >= 20 && !sim.WaterBusy && Math.Abs(inTop - outTop) <= 1 && inTop >= 8 &&
            Math.Abs(total1 - total) < 2,
            $"in the basin after 0,5 s {out05 / inside0:P0}, after 2 s {out2 / inside0:P0}; front {front05 / 8.0:0.0} → {front2 / 8.0:0.0} m at 2 s; " +
            $"settled in {settledIn:0} s, level {inTop} inside and {outTop} in the basin; mass {total:0.0} → {total1:0.0}");
    }

    /// <summary>
    /// A reservoir breached on a band of the horde standing before it: the wave knocks them off their feet and carries
    /// the light ones away; a knight of ours in plate let down into deep water goes under and drowns, a worker swims.
    /// </summary>
    static void Wave(Action<string, bool, string> check)
    {
        var (sim, w, site) = NewSim();
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        int kx = site.KeepX, kz = site.KeepZ;
        int fx0 = kx - 420, fx1 = kx - 60, fz0 = kz - 120, fz1 = kz + 120;
        int g = Field(sim, w, fx0, fx1, fz0, fz1);
        int rx0 = fx0 + 20, rx1 = rx0 + 48, rz0 = kz - 24, rz1 = kz + 24;
        for (int z = rz0; z < rz1; z++)
            for (int x = rx0; x < rx1; x++)
            {
                bool wall = x < rx0 + 4 || x >= rx1 - 4 || z < rz0 + 4 || z >= rz1 - 4;
                for (int y = g; y < g + 36; y++) w.Set(x, y, z, wall ? Mat.Stone : y < g + 32 ? Mat.Water : Mat.Air);
            }
        Run(2);
        var band = new System.Collections.Generic.List<(Unit u, float x, float z)>();
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, 6, -6), (Kinds.Goblin, 8, 4), (Kinds.Goblin, 14, -2), (Kinds.Orc, 10, 0), (Kinds.Goblin, 18, 6), (Kinds.Orc, 22, -4) })
        {
            var u = sim.Spawn(t, sim.NearestStand(rx1 + dx, kz + dz, t.Move.Height));
            u.Guard = true; u.Home = u.Cell;
            band.Add((u, u.X, u.Z));
        }
        var hit = new System.Collections.Generic.HashSet<Unit>();
        for (int z = kz - 8; z < kz + 8; z++) for (int x = rx1 - 4; x < rx1; x++) for (int y = g; y < g + 36; y++) w.Set(x, y, z, Mat.Air);
        for (int t = 0; t < 120; t++) { sim.Step(.05f); foreach (var (u, _, _) in band) if (u.Airborne != null || u.Down) hit.Add(u); }
        int swept = 0, felled = 0;
        var moved = new System.Collections.Generic.List<string>();
        foreach (var (u, x, z) in band)
        {
            float d = MathF.Sqrt((u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z)) / VoxelWorld.PerMetre;
            moved.Add($"{d:0.0}");
            if (d > 1) swept++;
            else if (u.Down || u.Dead) felled++;
        }
        check("a wave out of a breached reservoir knocks a band off its feet and carries some of it away", hit.Count >= 4 && swept >= 2,
            $"of {band.Count}: {hit.Count} off their feet, {swept} carried more than a metre (moved {string.Join(", ", moved)} m)");

        foreach (var (u, _, _) in band) { u.Dead = true; u.DiedAt = -999; }
        // a knight in plate and a worker let down into the reservoir's deep water
        var knight = sim.Spawn(Kinds.Knight, sim.NearestStand(rx0 + 10, kz - 10, Kinds.Knight.Move.Height));
        var worker = sim.Spawn(Kinds.Worker, sim.NearestStand(rx0 + 10, kz + 10, Kinds.Worker.Move.Height));
        foreach (var u in new[] { knight, worker })
        {
            int c = sim.Nav.Id((int)u.X >> NavGrid.Shift, (g + 2) >> NavGrid.Shift, (int)u.Z >> NavGrid.Shift);
            u.Cell = c; (u.X, u.Y, u.Z) = sim.Nav.Feet(c);
        }
        Run(10);
        check("deep water drowns a knight weighed down by his plate, while a worker swims ashore", knight.Dead && !worker.Dead,
            $"knight {(knight.Dead ? "drowned" : $"alive, {knight.Hp:0} hp")}; worker {(worker.Dead ? "dead: " + string.Join(", ", worker.Wounds?.Wounds ?? new()) : "alive")} at {worker.X - rx0:0},{worker.Y - g:0},{worker.Z - kz:0}");
    }

    /// <summary>
    /// Lava let out on the field beside a fence of logs and a ditch of water: it runs, slower than water; sets the logs
    /// alight; meets the water in steam and sets to stone; burns a goblin standing in it; cools to stone by itself; and a
    /// falling star leaves it in its crater.
    /// </summary>
    static void Lava(Action<string, bool, string> check)
    {
        var (sim, w, site) = NewSim();
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        int kx = site.KeepX, kz = site.KeepZ;
        int fx0 = kx - 420, fx1 = kx - 60, fz0 = kz - 120, fz1 = kz + 120;
        int g = Field(sim, w, fx0, fx1, fz0, fz1);
        int lx = fx0 + 80, lz = kz;
        for (int x = lx - 20; x < lx + 20; x++) for (int y = g; y < g + 10; y++) for (int z = lz + 8; z < lz + 10; z++) w.Set(x, y, z, Mat.Wood);
        for (int x = lx - 20; x < lx + 20; x++) for (int y = g - 4; y < g; y++) for (int z = lz - 12; z < lz - 8; z++) w.Set(x, y, z, Mat.Water);
        var gob = sim.Spawn(Kinds.Goblin, sim.NearestStand(lx + 3, lz, 2));
        gob.Guard = true; gob.Home = gob.Cell; gob.ChainedUntil = 1e9;
        float hp0 = gob.Hp;
        Run(1);
        int quenched = 0;
        sim.Quenched += (_, _, _) => quenched++;
        double water0 = sim.LiquidMass(Mat.Water);
        sim.LetLava(lx, lz, 400);
        int Reach()
        {
            int far = 0;
            foreach (int p in sim.Lavas) { var (x, _, z) = WaterPlanner.Unpack(p); far = Math.Max(far, Math.Max(Math.Abs(x - lx), Math.Abs(z - lz))); }
            return far;
        }
        Run(2);
        int reach2 = Reach();
        Run(10);
        int reach12 = Reach(), lava12 = sim.Lavas.Count, burning = sim.Burning.Count;
        bool burnt = gob.Dead || gob.Hp < hp0 * .5f;
        Console.WriteLine($"  [liquids] lava: out {reach2} voxels at 2 s, {reach12} at 12 s; {lava12} voxels of it, {burning} burning, {quenched} set by water, {sim.LavaSet} set in all");
        check("lava runs out slowly, sets timber alight and burns whoever stands in it; meeting water it sets to stone in steam",
            reach2 < 9 && reach12 > reach2 && burning > 0 && burnt && quenched > 0 && sim.LiquidMass(Mat.Water) < water0,
            $"out {reach2 / 8.0:0.0} m at 2 s and {reach12 / 8.0:0.0} m at 12 s; {burning} voxels alight; goblin {(gob.Dead ? "burnt to death" : $"{gob.Hp:0} of {hp0:0} hp")}; {quenched} voxels set by the water");
        Run(240);
        int basalt = 0;
        for (int x = lx - 24; x < lx + 24; x++) for (int z = lz - 24; z < lz + 24; z++) for (int y = g - 6; y < g + 8; y++) if (w.Get(x, y, z) == Mat.Basalt) basalt++;
        check("lava lying still cools to dark stone in a few minutes", sim.Lavas.Count < lava12 / 10 && basalt > 100,
            $"{lava12} voxels of lava at 12 s, {sim.Lavas.Count} left at 4 min; {basalt} of stone set from it");

        // a falling star brought down on the field
        sim.Favour = 600;
        foreach (var sp in Sim.Spells) sim.Known.Add(sp.Kind);
        int sx = lx + 60, before = sim.Lavas.Count;
        sim.Cast(SpellKind.Meteor, sx, g, lz);
        Run(4);
        int near = 0;
        foreach (int p in sim.Lavas) { var (x, _, z) = WaterPlanner.Unpack(p); if (Math.Abs(x - sx) < 30 && Math.Abs(z - lz) < 30) near++; }
        check("a falling star leaves lava glowing in its crater", near > 20, $"{near} voxels of lava about where it struck");
    }

    /// <summary>The same reservoir breached out over the open field: what a flood over the land costs.</summary>
    static void Flood(Action<string, bool, string> check)
    {
        var (sim, w, site) = NewSim();
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        int kx = site.KeepX, kz = site.KeepZ;
        int fx0 = kx - 420, fx1 = kx - 60, fz0 = kz - 120, fz1 = kz + 120;
        int g = Field(sim, w, fx0, fx1, fz0, fz1);
        Run(.5);
        int rx0 = fx0 + 20, rx1 = rx0 + 48, rz0 = kz - 24, rz1 = kz + 24;
        for (int z = rz0; z < rz1; z++)
            for (int x = rx0; x < rx1; x++)
            {
                bool wall = x < rx0 + 4 || x >= rx1 - 4 || z < rz0 + 4 || z >= rz1 - 4;
                for (int y = g; y < g + 36; y++) w.Set(x, y, z, wall ? Mat.Stone : y < g + 32 ? Mat.Water : Mat.Air);
            }
        Run(1);
        var quiet = Stopwatch.StartNew();
        Run(10);
        long still = quiet.ElapsedMilliseconds;
        for (int z = kz - 8; z < kz + 8; z++) for (int x = rx1 - 4; x < rx1; x++) for (int y = g; y < g + 36; y++) w.Set(x, y, z, Mat.Air);
        sim.TakeLiquidPeak();
        var sw = Stopwatch.StartNew();
        Run(30);
        long ms = sw.ElapsedMilliseconds;
        int far = 0;
        for (int x = rx1; x < fx1; x++) for (int z = fz0; z < fz1; z += 2) if (w.Get(x, g, z) == Mat.Water) { far = x - rx1; break; }
        double cost = (ms - still * 3) / 30.0;
        Console.WriteLine($"  [liquids] flood over the field: {ms} ms for 30 s of game, {still} ms for 10 s before it; out {far / 8.0:0.0} m; " +
            $"most voxels looked at in a turn {sim.TakeLiquidPeak()}, blocks kept {sim.LiquidBlocks}");
        check("a big flood over open land costs little a second of game", cost < 70 && far > 40,
            $"{cost:0} ms a second of game over a quiet one; it ran out {far / 8.0:0.0} m in 30 s");
    }
}
