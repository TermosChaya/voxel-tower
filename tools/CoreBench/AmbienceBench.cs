using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The small life about the view (--ambience, src/Core/Ambience.cs), on a level field: a copse of trees, a stone wall four
/// metres high, a meadow of flowers, a pond a metre deep with reeds and moss on its bank. By day flocks of sparrows settle
/// in the trees and pigeons on the wall, each bird sitting on something; a man walking up to a flock puts it up at once,
/// and it settles again further off; a blast puts one up; crows come to the dead by day, wheel high over them and come down
/// to them; butterflies and bees by the flowers by day, none at night nor in the rain; fireflies over the pond at night,
/// none by day; frogs croak there at night and never by day; fish jump only out of water three voxels deep and more, and
/// come down in it; midges dance over the pond by day; and all of it costs little a frame.
/// </summary>
static class AmbienceBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.SetSource(new MapRaids());
        sim.WildTarget = 0;
        // a field of level turf west of the citadel
        int fx0 = (site.KeepX - 460) & ~7, fz0 = (site.KeepZ - 140) & ~7, fg = w.SurfAt(fx0 + 160, fz0 + 140);
        for (int z = fz0; z < fz0 + 280; z++)
            for (int x = fx0; x < fx0 + 320; x++)
                for (int y = fg - 8; y < fg + 120; y++) w.Set(x, y, z, y < fg ? (y < fg - 4 ? Mat.Rock : y == fg - 1 ? Mat.Grass : Mat.Soil) : Mat.Air);
        sim.Step(.05f);
        foreach (var u in sim.Units) u.Dead = true;
        sim.Units.Clear();
        // a copse of six trees in the field's north-west, trunks four metres and crowns two across
        var trees = new List<(int x, int z)>();
        for (int i = 0; i < 6; i++)
        {
            int tx = fx0 + 40 + i % 3 * 28, tz = fz0 + 30 + i / 3 * 30;
            trees.Add((tx, tz));
            for (int y = fg; y < fg + 32; y++) w.Set(tx, y, tz, Mat.Trunk);
            for (int y = fg + 26; y < fg + 40; y++)
                for (int dz = -8; dz <= 8; dz++)
                    for (int dx = -8; dx <= 8; dx++)
                        if (dx * dx + dz * dz + (y - fg - 33) * (y - fg - 33) < 64 && w.Get(tx + dx, y, tz + dz) == 0) w.Set(tx + dx, y, tz + dz, Mat.Leaf);
        }
        // a stone wall four metres high across the field's middle
        for (int z = fz0 + 100; z < fz0 + 180; z++)
            for (int x = fx0 + 200; x < fx0 + 204; x++)
                for (int y = fg; y < fg + 32; y++) w.Set(x, y, z, Mat.Stone);
        // a meadow of flowers in the south-west, a pond a metre deep in the south-east with reeds and moss about it
        int mx0 = fx0 + 30, mz0 = fz0 + 180;
        for (int z = mz0; z < mz0 + 70; z++)
            for (int x = mx0; x < mx0 + 90; x++)
                if (Hash.H3(x, 3, z) < .25) w.Set(x, fg, z, new[] { Mat.Flower, Mat.Bloom, Mat.FlowerRed, Mat.FlowerWhite, Mat.FlowerBlue }[(int)(Hash.H3(z, 5, x) * 5) % 5]);
        int px0 = fx0 + 230, pz0 = fz0 + 200, pw = 64, pd = 56;
        for (int z = pz0 - 6; z < pz0 + pd + 6; z++)
            for (int x = px0 - 6; x < px0 + pw + 6; x++)
            {
                bool inside = x >= px0 && x < px0 + pw && z >= pz0 && z < pz0 + pd;
                if (inside) for (int y = fg - 8; y < fg; y++) w.Set(x, y, z, Mat.Water);
                else if (Hash.H3(x, 9, z) < .5) { w.Set(x, fg - 1, z, Mat.Moss); if (Hash.H3(z, 11, x) < .35) w.Set(x, fg, z, Mat.Reed); }
            }
        var amb = new Ambience(sim, 3);
        float cx = fx0 + 160, cz = fz0 + 140, reach = 45 * VoxelWorld.PerMetre;
        void Noon() { sim.Clock.Night = false; sim.Clock.DawnLeft = 0; sim.Clock.Left = DayClock.DayLength / 2; }
        void Night() { sim.Clock.Night = true; sim.Clock.DawnLeft = 0; }
        void Tick(double seconds, float fx, float fz, bool small = true, Action each = null)
        {
            for (double t = 0; t < seconds; t += 1 / 30.0) { amb.Tick(1 / 30f, fx, fg, fz, reach, small); each?.Invoke(); }
        }
        Noon();
        sim.Weather = Weather.Clear;

        // ---- the birds by day
        Tick(45, cx, cz);
        var sparrows = amb.SettledFlocks(Critter.Sparrow);
        var pigeons = amb.SettledFlocks(Critter.Pigeon);
        int perched = 0, floating = 0;
        foreach (var m in amb.Motes)
        {
            if (m.Kind is not (Critter.Sparrow or Critter.Pigeon) || !m.Perched) continue;
            perched++;
            int x = (int)MathF.Floor(m.X), y = (int)MathF.Round(m.Y), z = (int)MathF.Floor(m.Z);
            if (w.Get(x, y - 1, z) == 0 || w.Get(x, y, z) != 0)
            {
                floating++;
                if (Environment.GetEnvironmentVariable("AMB_DEBUG") == "1")
                    Console.WriteLine($"     {m.Kind} at {m.X:0.0},{m.Y:0.0},{m.Z:0.0}: under {w.Get(x, y - 1, z)}, at {w.Get(x, y, z)}, column top {w.ColTop(x, z)}");
            }
        }
        check("small life: by day sparrows settle in the trees and pigeons on the wall, every bird sitting on something",
            sparrows.Count >= 1 && pigeons.Count >= 1 && perched >= 6 && floating == 0,
            $"settled sparrow flocks {sparrows.Count} ({string.Join(", ", sparrows.Select(f => f.n))}), pigeon flocks {pigeons.Count}, birds perched {perched}, of them on nothing {floating}");

        // ---- someone comes near a flock
        bool upNear = false, settledAway = false;
        float awayBy = 0;
        if (sparrows.Count > 0)
        {
            var f0 = sparrows[0];
            int ups = amb.TakeOffs;
            var walker = sim.Spawn(Kinds.Worker, sim.NearestStand((int)f0.x + 24, (int)f0.z, 2));
            Tick(.5, cx, cz);
            upNear = amb.TakeOffs > ups && amb.Aloft(Critter.Sparrow, f0.x, f0.z, 30 * VoxelWorld.PerMetre);
            // the man goes off; the flock settles again somewhere
            walker.X = fx0 + 300; walker.Z = fz0 + 270;
            for (int k = 0; k < 40 && !settledAway; k++)
            {
                Tick(1, cx, cz);
                foreach (var s in amb.SettledFlocks(Critter.Sparrow))
                {
                    float d = MathF.Sqrt((s.x - f0.x) * (s.x - f0.x) + (s.z - f0.z) * (s.z - f0.z));
                    if (d >= 10 * VoxelWorld.PerMetre) { settledAway = true; awayBy = d / VoxelWorld.PerMetre; }
                }
            }
            sim.Units.Remove(walker);
        }
        check("small life: a man walking up to a flock puts it up at once, and it settles again 10 m and more away",
            upNear && settledAway, $"put up {upNear}, settled again {(settledAway ? $"{awayBy:0} m off" : "no")}");

        // ---- a blast
        Tick(20, cx, cz);
        var any = amb.SettledFlocks(Critter.Sparrow).Concat(amb.SettledFlocks(Critter.Pigeon)).ToList();
        bool blastUp = false;
        if (any.Count > 0)
        {
            int ups = amb.TakeOffs;
            amb.Startle(any[0].x + 40, any[0].y, any[0].z, 3 * VoxelWorld.PerMetre);
            Tick(.3, cx, cz);
            blastUp = amb.TakeOffs > ups;
        }
        check("small life: a blast near a flock puts it up", blastUp, $"settled flocks {any.Count}");

        // ---- the dead, and the crows
        var dead = new List<Unit>();
        for (int i = 0; i < 3; i++)
        {
            var g = sim.Spawn(Kinds.Goblin, sim.NearestStand((int)cx + 30 + i * 12, (int)cz + 40, Kinds.Goblin.Move.Height));
            g.Dead = true; g.DiedAt = sim.Time;
            dead.Add(g);
        }
        float dx0 = dead.Average(d => d.X), dz0 = dead.Average(d => d.Z);
        bool wheeled = false;
        float highest = 0;
        Tick(30, cx, cz, each: () =>
        {
            foreach (var m in amb.Motes)
            {
                if (m.Kind != Critter.Crow || m.Perched) continue;
                float flat = MathF.Sqrt((m.X - dx0) * (m.X - dx0) + (m.Z - dz0) * (m.Z - dz0)), over = (m.Y - fg) / VoxelWorld.PerMetre;
                if (flat < 25 * VoxelWorld.PerMetre && over >= 10) { wheeled = true; highest = MathF.Max(highest, over); }
            }
        });
        int crowsDown = 0;
        for (int k = 0; k < 60 && crowsDown < 3; k++)
        {
            Tick(1, cx, cz);
            crowsDown = amb.Motes.Count(m => m.Kind == Critter.Crow && m.Perched && MathF.Sqrt((m.X - dx0) * (m.X - dx0) + (m.Z - dz0) * (m.Z - dz0)) < 8 * VoxelWorld.PerMetre);
        }
        check("small life: crows come to the dead by day, wheel high over them and come down to them",
            wheeled && crowsDown >= 3, $"wheeled over them {wheeled} (up to {highest:0} m), crows down by them {crowsDown}, caws {amb.Caws}");
        foreach (var d in dead) sim.Units.Remove(d);

        // ---- butterflies and bees by the flowers, by day only
        float mcx = mx0 + 45, mcz = mz0 + 35;
        Tick(25, mcx, mcz);
        int flies = amb.Count(Critter.Butterfly), bees = amb.Count(Critter.Bee);
        int onFlower = 0, sitting = 0;
        foreach (var m in amb.Motes)
        {
            if (m.Kind != Critter.Butterfly || !m.Perched) continue;
            sitting++;
            int x = (int)MathF.Floor(m.X), z = (int)MathF.Floor(m.Z);
            byte under = w.Get(x, (int)MathF.Floor(m.Y) - 1, z);
            if (under is Mat.Flower or Mat.Bloom or Mat.FlowerRed or Mat.FlowerWhite or Mat.FlowerBlue) onFlower++;
        }
        sim.Weather = Weather.Rain;
        Tick(3, mcx, mcz);
        int inRain = amb.Count(Critter.Butterfly) + amb.Count(Critter.Bee);
        sim.Weather = Weather.Clear;
        Night();
        Tick(3, mcx, mcz);
        int atNight = amb.Count(Critter.Butterfly) + amb.Count(Critter.Bee);
        check("small life: butterflies and bees by the flowers by day, sitting on the flowers; none in the rain or at night",
            flies >= 3 && bees >= 1 && sitting > 0 && onFlower == sitting && inRain == 0 && atNight == 0,
            $"butterflies {flies}, bees {bees}, sitting {sitting} (on a flower {onFlower}), in the rain {inRain}, at night {atNight}");

        // ---- the pond: fireflies and frogs at night, fish and midges by day
        float pcx = px0 + pw / 2f, pcz = pz0 + pd / 2f;
        int croaks0 = amb.Croaks;
        Tick(15, pcx, pcz);
        int flies2 = amb.Count(Critter.Firefly), croaksNight = amb.Croaks - croaks0;
        // each over the water or the damp ground: its column topped with water, reeds, lilies, moss or peat
        bool glowOver = amb.Motes.Where(m => m.Kind == Critter.Firefly).All(m =>
        {
            int x = (int)MathF.Floor(m.Hx), z = (int)MathF.Floor(m.Hz);
            return w.Get(x, w.ColTop(x, z) - 1, z) is Mat.Water or Mat.Reed or Mat.Lily or Mat.Moss or Mat.Peat;
        });
        Noon();
        Tick(3, pcx, pcz);
        int fireDay = amb.Count(Critter.Firefly);
        croaks0 = amb.Croaks;
        int leaps0 = amb.Leaps, badLeaps = 0, splashes = 0, dry = 0;
        var seenFish = new HashSet<Mote>();
        void Splash(float x, float y, float z)
        {
            splashes++;
            if (w.Get((int)MathF.Floor(x), (int)MathF.Round(y) - 1, (int)MathF.Floor(z)) != Mat.Water) dry++;
        }
        amb.Splashed += Splash;
        Tick(60, pcx, pcz, each: () =>
        {
            // each fish as it goes up: out of water three voxels deep at least
            foreach (var m in amb.Motes)
            {
                if (m.Kind != Critter.Fish || !seenFish.Add(m)) continue;
                int x = (int)MathF.Floor(m.Hx), y = (int)MathF.Round(m.Hy), z = (int)MathF.Floor(m.Hz);
                if (w.Get(x, y, z) != Mat.Water || w.Get(x, y - 1, z) != Mat.Water || w.Get(x, y - 2, z) != Mat.Water) badLeaps++;
            }
        });
        amb.Splashed -= Splash;
        int croaksDay = amb.Croaks - croaks0, leaps = amb.Leaps - leaps0, midges = amb.Count(Critter.Midge);
        check("small life: fireflies over the pond and frogs croaking at night, neither by day",
            flies2 >= 5 && glowOver && croaksNight >= 5 && fireDay == 0 && croaksDay == 0,
            $"fireflies at night {flies2} (all over water or damp ground {glowOver}), croaks at night {croaksNight}; by day fireflies {fireDay}, croaks {croaksDay}");
        check("small life: fish jump by day out of water three voxels deep and more and come down in it; midges dance over the pond",
            leaps >= 3 && badLeaps == 0 && dry == 0 && midges >= 20, $"leaps in a minute {leaps}, out of shallow water {badLeaps}, splashes {splashes} (not on water {dry}), midges {midges}");

        // ---- what it costs
        Noon();
        Tick(10, mcx, mcz);
        double t0 = BenchClock.CpuMs();
        int frames = 0;
        for (int k = 0; k < 600; k++) { amb.Tick(1 / 60f, mcx + k * .3f, fg, mcz, reach, true); frames++; }
        double dayMs = (BenchClock.CpuMs() - t0) / frames;
        Night();
        Tick(10, pcx, pcz);
        t0 = BenchClock.CpuMs();
        for (int k = 0; k < 600; k++) amb.Tick(1 / 60f, pcx, fg, pcz, reach, true);
        double nightMs = (BenchClock.CpuMs() - t0) / 600;
        check("small life: a frame of all of it costs under 0.3 ms by day and by night",
            dayMs < .3 && nightMs < .3, $"by day {dayMs:0.000} ms with {amb.Motes.Count} about, by night {nightMs:0.000} ms");
    }
}
