using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// What stands round the citadel when a game begins: an old stone fort gone half to ruin. Its curtain walls are broken
/// down unevenly and breached in three places, their rubble lying at the foot; one corner tower still stands, the other
/// is a stump; the timber palisade on the east side has rotted down to a few posts; the gate in the west wall has fallen
/// off its hinges, and one of the two ladders up onto the wall is gone. All of it is remembered as it was built, so the
/// workers mend it to what it was — the first job of a game, and a sample of what can be built.
/// </summary>
public static class StartingFort
{
    /// <summary>The curtain's corners round the citadel, its thickness and height, in voxels.</summary>
    const int West = 84, East = 36, North = 60, South = 60, Thick = 6, High = 28;
    /// <summary>The towers at the western corners: radius, how far they rise over the curtain, the battlements on them.</summary>
    const int TowerR = 11, TowerOver = 12, Crenel = 6;

    enum Part : byte { Tower, Core, Wall, Battlement, Palisade, Ladder }
    /// <summary>Which wall or tower a voxel is part of: the west, north and south curtains, the two towers, the second ladder.</summary>
    const byte W = 0, N = 1, S = 2, NW = 3, SW = 4, Fallen = 5;

    /// <summary>The gate it was raised with (hanging off its hinges: it has to be mended).</summary>
    public static Gateway Raise(Sim sim)
    {
        var w = sim.World; var s = sim.Site;
        int kx = s.KeepX, kz = s.KeepZ, b = s.KeepBase, top = b + High;
        int x0 = kx - West, x1 = kx + East, z0 = kz - North, z1 = kz + South;
        var part = new Dictionary<int, (Part part, byte of)>();
        var noise = new ValueNoise(4211);

        // ---- the fort as it was built: everything laid and remembered
        void Put(VoxelList L, Part p, byte of)
        {
            for (int i = 0; i < L.Count; i++)
            {
                int x = L.X(i), y = L.Y(i), z = L.Z(i);
                byte m = L.M(i);
                if (m == 0) { if (Mat.IsVeg[w.Get(x, y, z)]) w.Set(x, y, z, Mat.Air); continue; }
                int k = WaterPlanner.Pack(x, y, z);
                if (part.ContainsKey(k) || !Brushes.Free(w.Get(x, y, z))) continue;
                part[k] = (p, of);
                w.Set(x, y, z, m);
                sim.Remember(x, y, z, m);
            }
        }
        var shell = new Stroke { Mat = Mat.Stone, Width = 3, Height = High + TowerOver, Level = true, Hollow = true };
        var core = new Stroke { Mat = Mat.Rammed, Width = 1, Height = High + TowerOver - 4, Level = true };
        foreach (var (tz, of) in new[] { (z0, NW), (z1, SW) })
        {
            Put(Shapes.Circle(w, s, x0, tz, TowerR, shell, top + TowerOver), Part.Tower, of);
            Put(Shapes.Circle(w, s, x0, tz, TowerR - 2.5, core, top + TowerOver - 4), Part.Core, of);
        }
        var wall = new Stroke { Mat = Mat.Stone, Width = Thick, Height = High, Level = true };
        Put(Shapes.Line(w, s, x0, z0, x0, z1, wall, top), Part.Wall, W);
        Put(Shapes.Line(w, s, x0, z0, x1, z0, wall, top), Part.Wall, N);
        Put(Shapes.Line(w, s, x0, z1, x1, z1, wall, top), Part.Wall, S);
        // ladders up the inner face of the west curtain, either side of the gate, and one up the standing tower
        foreach (var (lz, of) in new[] { (kz - 34, W), (kz + 34, Fallen) })
        {
            var inner = Face(w, x0 + 30, lz, -1, top - 10);
            if (inner.HasValue) Put(Shapes.LadderAt(w, s, inner.Value), Part.Ladder, of);
        }
        var tower = Face(w, x0 + 30, z0 + 6, -1, top);
        if (tower.HasValue) Put(Shapes.LadderAt(w, s, tower.Value), Part.Ladder, NW);
        // battlements along the outer edge of the curtains and round the tops of the towers
        var crest = new VoxelList();
        bool NearTower(int x, int z) => Brushes.Dist(x - x0, z - z0) < TowerR + 1 || Brushes.Dist(x - x0, z - z1) < TowerR + 1;
        void Merlons(int x, int z, int along, byte of)
        {
            if (!part.TryGetValue(WaterPlanner.Pack(x, top - 1, z), out var v) || v.part != Part.Wall || NearTower(x, z) || (along % 10 + 10) % 10 >= 6) return;
            for (int y = top; y < top + Crenel; y++) crest.Add(x, y, z, Shapes.Masonry(x, y, z));
        }
        for (int z = z0; z <= z1; z++) for (int x = x0 - 3; x <= x0 - 2; x++) Merlons(x, z, z - z0, W);
        Put(crest, Part.Battlement, W); crest.Clear();
        for (int x = x0; x <= x1; x++) for (int z = z0 - 3; z <= z0 - 2; z++) Merlons(x, z, x - x0, N);
        Put(crest, Part.Battlement, N); crest.Clear();
        for (int x = x0; x <= x1; x++) for (int z = z1 + 2; z <= z1 + 3; z++) Merlons(x, z, x - x0, S);
        Put(crest, Part.Battlement, S); crest.Clear();
        foreach (var (tz, of) in new[] { (z0, NW), (z1, SW) })
        {
            for (int z = tz - TowerR - 1; z <= tz + TowerR + 1; z++)
                for (int x = x0 - TowerR - 1; x <= x0 + TowerR + 1; x++)
                {
                    if (!part.TryGetValue(WaterPlanner.Pack(x, top + TowerOver - 1, z), out var v) || v.part != Part.Tower) continue;
                    double a = Math.Atan2(z + .5 - tz, x + .5 - x0);
                    if ((int)Math.Floor((a + Math.PI) / (Math.PI * 2) * 24) % 2 == 1) continue;
                    for (int y = top + TowerOver; y < top + TowerOver + Crenel - 2; y++) crest.Add(x, y, z, Shapes.Masonry(x, y, z));
                }
            Put(crest, Part.Battlement, of); crest.Clear();
        }
        // the palisade on the east side: logs set upright, every third one standing a little proud
        var logs = new VoxelList();
        for (int z = z0; z <= z1; z++)
            for (int x = x1 - 1; x <= x1 + 1; x++)
            {
                if (s.Forbidden(x, z)) continue;
                int g = w.SurfAt(x, z), log = (z - z0) / 3, peak = b + 20 + (log % 3 == 1 ? 3 : log % 2);
                for (int y = g; y < peak; y++) logs.Add(x, y, z, Mat.Wood);
            }
        Put(logs, Part.Palisade, 0);
        // the gate, west, in line with the citadel's door
        Gateway gate = null;
        var outer = Face(w, x0 - 40, kz, +1, top - 10);
        var plan = outer.HasValue ? GatePlan.At(w, s, outer.Value) : null;
        if (plan != null) gate = sim.AddGate(plan, built: true);

        // ---- what the years left of it
        // how high each stretch of the curtains still stands, as a share of its height: the gate's stretch whole,
        // three breaches down to the ground, the north wall low at its eastern end, the tops broken off everywhere else
        double Standing(byte of, int u)
        {
            double n = noise.Sample(u * .06 + of * 31, 3.7);
            switch (of)
            {
                case W:
                    if (Math.Abs(u - kz) <= 14) return 2;
                    if (u >= z0 + 20 && u <= z0 + 31) return .05;
                    return .88 + .16 * n;
                case N:
                    if (u >= kx + 2 && u <= kx + 13) return 0;
                    return u < kx - 30 ? .86 + .18 * n : .46 + .3 * n;
                default:
                    if (u >= kx - 44 && u <= kx - 31) return .06;
                    return .78 + .24 * n;
            }
        }
        int StandsTo(Part p, byte of, int x, int y, int z)
        {
            switch (p)
            {
                case Part.Battlement when of >= NW: return of == NW && Hash.H3(x, z, 59) < .6 ? int.MaxValue : 0;
                case Part.Wall:
                case Part.Battlement:
                {
                    int u = of == W ? z : x;
                    double f = Standing(of, u);
                    if (p == Part.Battlement) return f >= 1 && Hash.H3(x, z, 61) < .72 ? int.MaxValue : 0;
                    int to = b + (int)Math.Round(f * High) - (int)(noise.Sample(u * .45, of * 7.1) * 3);
                    // the broken top crumbles unevenly
                    return f >= 1 ? int.MaxValue : y >= to - 2 && Hash.H3(x * 3 + y, z, 63) < .35 ? y : to;
                }
                case Part.Tower:
                {
                    int tz = of == NW ? z0 : z1;
                    double a = Math.Atan2(z + .5 - tz, x + .5 - x0);
                    if (of == NW) return b + High + TowerOver - (int)(noise.Sample(a * 2 + 9, 1.3) * 5);
                    double up = .5 + .5 * Math.Cos(a - 2.4);
                    return b + (int)((High + TowerOver) * (.22 + .58 * up * up)) - (int)(Hash.H3(x, z, 65) * 3);
                }
                case Part.Core: return of == NW ? int.MaxValue : b + 8 + (int)(Hash.H3(x, z, 67) * 4);
                case Part.Palisade:
                {
                    int log = (z - z0) / 3;
                    return Hash.H3(log, 0, 69) < .38 ? b + 8 + (int)(Hash.H3(log, 1, 69) * 14) : b + 1 + (int)(Hash.H3(log, 2, 69) * 4);
                }
                case Part.Ladder: return of == Fallen ? 0 : int.MaxValue;
                default: return int.MaxValue;
            }
        }
        // holes knocked through the curtains here and there, too small to get through
        bool Holed(byte of, int x, int y, int z) =>
            of == W && z >= kz + 24 && z <= kz + 27 && y >= b + 9 && y < b + 13 ||
            of == N && x >= kx - 58 && x <= kx - 55 && y >= b + 12 && y < b + 15 ||
            of == S && x >= kx - 12 && x <= kx - 9 && y >= b + 6 && y < b + 9;
        var fell = new List<(int x, int y, int z, Part p, byte of)>();
        var tops = new Dictionary<int, int>();
        foreach (var (k, (p, of)) in part)
        {
            var (x, y, z) = WaterPlanner.Unpack(k);
            if (w.Get(x, y, z) == Mat.Air) continue;
            bool gone = y >= StandsTo(p, of, x, y, z) || p == Part.Wall && Holed(of, x, y, z);
            if (!gone)
            {
                if (p is Part.Wall or Part.Tower) { int c = z * w.SX + x; tops[c] = Math.Max(tops.TryGetValue(c, out int t) ? t : 0, y); }
                continue;
            }
            w.Set(x, y, z, Mat.Air);
            fell.Add((x, y, z, p, of));
        }
        // moss on the broken courses
        foreach (var (c, y) in tops)
        {
            int x = c % w.SX, z = c / w.SX;
            if (MeantAbove(sim, x, y, z) && Hash.H3(x, y, z * 5) < .3) w.Set(x, y, z, Mat.Moss);
        }
        // and what fell lies at the foot, mostly outside: stones from the masonry, rotten logs from the palisade
        foreach (var (x, y, z, p, of) in fell)
        {
            if (p == Part.Ladder || p == Part.Core) continue;
            double chance = p == Part.Palisade ? .04 : .1, h = Hash.H3(x * 7 + y, z * 3, 71);
            if (h > chance) continue;
            double out1 = Hash.H3(x + 73, y * 5, z) < .65 ? 1 : -1, dist = 2 + Hash.H3(y, x, z * 11) * 9, side = (Hash.H3(z, y, x * 13) - .5) * 6;
            (double nx, double nz) = of switch
            {
                W => (-1.0, 0.0),
                N => (0.0, -1.0),
                S => (0.0, 1.0),
                _ when p == Part.Palisade => (1.0, 0.0),
                _ => Norm(x + .5 - x0, z + .5 - (of == NW ? z0 : z1)),
            };
            if (p == Part.Tower || p == Part.Battlement && of >= NW) out1 = 1;
            int rx = x + (int)Math.Round(nx * out1 * (dist + 3) - nz * side), rz = z + (int)Math.Round(nz * out1 * (dist + 3) + nx * side);
            if (!w.InBounds(rx, 0, rz) || s.Forbidden(rx, rz) || s.WaterTop != null && s.WaterTop[rz * w.SX + rx] > 0) continue;
            int ry = w.SurfAt(rx, rz);
            if (ry <= 0 || ry > b + 6 || !Brushes.Free(w.Get(rx, ry, rz))) continue;
            byte m = p == Part.Palisade ? Mat.Wood : Hash.H3(rx, ry, rz) < .12 ? Mat.Moss : Shapes.Masonry(rx, ry, rz);
            w.Set(rx, ry, rz, m);
        }
        if (gate != null) sim.UnhangGate(gate);
        sim.Nav.Flush();
        sim.FortArea = (x0 - TowerR - 4, z0 - TowerR - 4, x1 + 4, z1 + TowerR + 4);
        sim.FortGate = gate;
        return gate;
    }

    static (double, double) Norm(double x, double z)
    {
        double l = Math.Sqrt(x * x + z * z);
        return l < 1e-6 ? (1, 0) : (x / l, z / l);
    }

    /// <summary>Something was meant to stand on top of this voxel and is missing: it is a broken course.</summary>
    static bool MeantAbove(Sim sim, int x, int y, int z) => sim.MeantAt(x, y + 1, z) != 0 && sim.World.Get(x, y + 1, z) == Mat.Air;

    /// <summary>The first blocking face met walking from a column along x, at a height: a side face to click, as a hit.</summary>
    static VoxelHit? Face(VoxelWorld w, int x, int z, int dir, int y)
    {
        for (int n = 0; n < 120; n++, x += dir)
        {
            byte m = w.Get(x, y, z);
            if (m != 0 && !Mat.Passable[m]) return new VoxelHit { X = x, Y = y, Z = z, NX = -dir, M = m };
        }
        return null;
    }
}
