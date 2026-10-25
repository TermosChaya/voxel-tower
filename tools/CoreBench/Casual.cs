using System;
using System.Collections.Generic;
using VoxelTower.Core;

/// <summary>
/// The bench's easier building (--casual): how long four workers take over the usual works — a stone wall twenty
/// metres long, a round tower, a house of stone, a moat — each in a valley of its own from the citadel's gate on level
/// ground.
/// </summary>
static class CasualBench
{
    /// <summary>A valley with the old fort, four workers at the gate and stores enough, and level spots a walk from the citadel.</summary>
    static (Sim sim, VoxelWorld w, Site site, List<(int x, int z)> spots) Valley()
    {
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        StartingFort.Raise(sim);
        sim.CancelArea(0, 0, w.SX, w.SZ);
        for (int i = 0; i < 4; i++) sim.Spawn(Kinds.Worker, sim.Gate);
        sim.Stock[Stockpile.Stone] += 1200.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] += 600.0 * VoxelWorld.PerM3;
        int kx = site.KeepX, kz = site.KeepZ;
        var spots = new List<(int x, int z)>();
        for (int r = 110; r < 460 && spots.Count < 6; r += 8)
            for (int a = 0; a < 24 && spots.Count < 6; a++)
            {
                int x = kx + (int)(Math.Cos(a * Math.Tau / 24) * r), z = kz + (int)(Math.Sin(a * Math.Tau / 24) * r);
                if (x < 100 || z < 100 || x > w.SX - 100 || z > w.SZ - 100 || spots.Exists(s => Math.Abs(s.x - x) < 120 && Math.Abs(s.z - z) < 120)) continue;
                int lo = int.MaxValue, hi = 0;
                for (int dx = -48; dx <= 48; dx += 4) for (int dz = -48; dz <= 48; dz += 4) { int h = w.SurfAt(x + dx, z + dz); lo = Math.Min(lo, h); hi = Math.Max(hi, h); if (Mat.IsTrunk(w.Get(x + dx, h + 2, z + dz))) hi = 999; }
                if (hi - lo <= 2 && !site.Forbidden(x, z)) spots.Add((x, z));
            }
        return (sim, w, site, spots);
    }

    /// <summary>How long the work marked takes the workers to the last voxel, seconds; -1 if not done in the time given.</summary>
    static double Until(Sim sim, int limit = 900)
    {
        double t0 = sim.Time;
        for (int t = 0; t < limit * 20 && sim.Orders.Total > 0; t++) sim.Step(.05f);
        return sim.Orders.Total > 0 ? -1 : sim.Time - t0;
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var (simA, wA, siteA, spotsA) = Valley();
        if (spotsA.Count < 1) { check("level ground for the works", false, "none"); return; }
        int fell = 0;
        simA.Collapsed += (n, _, _, _) => fell += n;
        var (ax, az) = spotsA[0];
        simA.DesignateBuild(Shapes.Line(wA, siteA, ax - 80, az, ax + 79, az, new Stroke { Mat = Mat.Stone, Width = 8, Height = 16, Level = true }, Shapes.LevelTop(wA, ax, az, 16)));
        double wall = Until(simA);

        var (simB, wB, siteB, spotsB) = Valley();
        simB.Collapsed += (n, _, _, _) => fell += n;
        var (bx, bz) = spotsB[0];
        simB.DesignateBuild(Shapes.Circle(wB, siteB, bx, bz, 24, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Hollow = false, Level = true }, Shapes.LevelTop(wB, bx, bz, 32)));
        double tower = Until(simB);

        var (simC, wC, siteC, spotsC) = Valley();
        simC.Collapsed += (n, _, _, _) => fell += n;
        var (cx, cz) = spotsC[0];
        int x0 = cx - 18, z0 = cz - 28, x1 = cx + 18, z1 = cz + 27;
        var plan = Shapes.House(wC, siteC, x0, z0, x1, z1, new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true }, Shapes.LevelTop(wC, cx, cz, 32), out var door, out _, out var arched);
        double house = -1;
        if (plan != null)
        {
            simC.DesignateBuild(plan);
            simC.Arch(arched);
            simC.PlanHouse(door, x0, z0, x1, z1);
            house = Until(simC);
        }

        var (simD, wD, siteD, spotsD) = Valley();
        var (dx0, dz0) = spotsD[0];
        simD.DesignateRemove(Shapes.Line(wD, siteD, dx0 - 80, dz0, dx0 + 79, dz0, new Stroke { Width = 8, Height = 8, Dig = true }, 0));
        double moat = Until(simD);

        check("four workers build the usual works briskly: a stone wall 20 m by 2 m in under 35 s, a solid round tower 6 m across and 4 m high in under 60 s, a stone house in under 65 s, a moat 20 m long in under 28 s, and nothing falls",
            wall > 0 && wall < 35 && tower > 0 && tower < 60 && house > 0 && house < 65 && moat > 0 && moat < 28 && fell == 0,
            $"the wall {wall:0} s, the tower {tower:0} s, the house {house:0} s, the moat {moat:0} s; fell {fell}");
    }
}
