using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The citadel's castle as a game finds it (the keep itself is laid with the valley, WorldGen.Keep), on the ground the
/// old fort of before stood on. The keep stands at the back of a bailey walled in stone on three sides, a round tower on
/// each western corner, a palisade as high as the walls behind the keep; on the west, in line with the keep's door, the
/// gate is hung in the wall between the two towers of the gatehouse standing out before it; stone stairs go up onto the
/// wall-walk from the yard by the north and the south walls; along the walls inside stand a hall and a barracks under
/// lean-to roofs, a store and a stable either side of the gatehouse; in the yard a well, a wayside cross, benches, a
/// woodpile, barrels and a cart; the household's flag flies over the keep. The years have been hard on it: the gate has
/// fallen off its hinges, each wall is breached where the old fort's was, the south-west tower is a stump, the palisade
/// has rotted down to stumps, the walls' tops are broken here and there, the south stair has lost its upper steps and
/// the stable its roof. All of it is remembered as it was built, so the workers mend it to what it was — the first
/// job of a game, and a sample of what can be built.
/// </summary>
public static class StartingFort
{
    /// <summary>The curtain's corners round the citadel, its thickness and height, in voxels.</summary>
    const int West = 84, East = 36, North = 60, South = 60, Thick = 6, High = 28;
    /// <summary>The towers on the western corners: radius, how far they rise over the curtain, the battlements on them.</summary>
    const int TowerR = 11, TowerOver = 12, Crenel = 6;
    /// <summary>
    /// The gatehouse: the gate in the west curtain, and a tower either side of it standing out before the wall (no
    /// further than the corner towers), so far out from its middle and either way of the gateway, so wide, rising higher
    /// than the corner towers; how far either way of the gateway they reach.
    /// </summary>
    const int GateDeep = 3, GateTowerAt = 21, GateTowerR = 8, GateTowerOver = 16, GateHalf = GateTowerAt + GateTowerR;
    /// <summary>How far west of the keep the stairs up onto the walk begin; how deep the lean-tos stand out from the walls.</summary>
    const int StairAt = 56, LeanDeep = 16;

    enum Part : byte { Tower, Core, Wall, Battlement, Gatehouse, Stair, Lean, Roof, Furnish, Flag, Palisade }
    /// <summary>
    /// Which wall, tower or building a voxel is part of: the four curtains, the four corner towers, the gatehouse's two
    /// towers, the two stairs, the hall, the barracks, the store, the stable, the cross, the keep's flag.
    /// </summary>
    const byte W = 0, N = 1, S = 2, E = 3, NW = 4, SW = 5, GN = 9, GS = 10, StairN = 11, StairS = 12,
        Hall = 13, Barracks = 14, Store = 15, Stable = 16, Cross = 17, Keep = 18;

    /// <summary>The ground the castle stands on, its towers and the gatehouse's included: the rectangle of columns, voxels.</summary>
    public static (int x0, int z0, int x1, int z1) Area(Site s) =>
        (s.KeepX - West - GateDeep - GateTowerR - 4, s.KeepZ - North - TowerR - 4, s.KeepX + East + 4, s.KeepZ + South + TowerR + 4);

    /// <summary>The things of the yard the last castle raised could not set out, and why (for the bench).</summary>
    public static string Refused = "";

    /// <summary>The gate it was raised with (hanging off its hinges: it has to be mended).</summary>
    public static Gateway Raise(Sim sim)
    {
        var w = sim.World; var s = sim.Site;
        int kx = s.KeepX, kz = s.KeepZ, b = s.KeepBase, top = b + High;
        int x0 = kx - West, x1 = kx + East, z0 = kz - North, z1 = kz + South;
        var part = new Dictionary<int, (Part part, byte of)>();
        var noise = new ValueNoise(4211);
        int gx = x0 - GateDeep;

        // ---- the ground it stands on: the bailey levelled to the keep's foot, a cobbled way across it from the
        // gatehouse to the keep's door, and the ground outside cut down along the walls (sloping back up beyond two
        // metres), so that they stand their full height over whoever comes at them however the land rises about them
        int lx0 = gx - GateTowerR - 18, lx1 = x1 + 18, lz0 = z0 - TowerR - 18, lz1 = z1 + TowerR + 18;
        for (int z = lz0; z <= lz1; z++)
            for (int x = lx0; x <= lx1; x++)
            {
                if (!w.InBounds(x, 1, z) || !w.OnLand(x, z) || s.Forbidden(x, z) || s.WaterTop != null && s.WaterTop[z * w.SX + x] > 0) continue;
                if (Brushes.Dist(x - kx + .5, z - kz + .5) <= s.KeepR + 1) continue;
                // (the ground as it stands, a boulder on it too)
                int i = z * w.SX + x, cur = Math.Max(s.HTop[i], w.GroundTop(x, z));
                bool inside = x > x0 && x < x1 && z > z0 && z < z1;
                // how far outside the walls (the gatehouse's front stands out west of the curtain)
                int dOut = Math.Max(Math.Max(Math.Max(z0 - z, z - z1), 0), Math.Max(x0 - x, x - x1));
                if (z >= kz - GateHalf && z < kz + GateHalf && x < x0) dOut = Math.Max(0, gx - x);
                int to = inside ? b : cur > b + 4 && dOut <= 16 ? b + 4 : cur;
                if (inside && b - cur > 24) to = cur;
                to = Math.Max(to, cur - 40);
                if (to == cur && !inside) continue;
                byte topMat = w.Get(x, cur - 1, z);
                bool way = inside && Math.Abs(z - kz) <= 8 && x < kx;
                byte face = way ? Sim.WayMat(WayKind.Cobble, x, z) : Mat.IsGrass(topMat) || topMat is Mat.Soil or Mat.Path or Mat.Moss or Mat.Peat ? (inside ? Mat.Grass : topMat) : topMat == 0 ? Mat.Grass : topMat;
                // clear the column down to the new ground (what grows on it, and what stood over the cut), then fill up to it
                for (int y = Math.Min(w.SY - 1, Math.Max(cur, to) + 40); y >= to; y--)
                {
                    byte m = w.Get(x, y, z);
                    if (m != 0 && Wild(m)) w.Set(x, y, z, Mat.Air);
                }
                for (int y = Math.Max(1, to - 6); y < to; y++)
                {
                    byte m = w.Get(x, y, z);
                    byte want = y == to - 1 ? face : Mat.Soil;
                    if (m == 0 || Mat.IsVeg[m] || y == to - 1 && m != want && Wild(m)) w.Set(x, y, z, want);
                }
                s.HTop[i] = to;
            }

        // ---- the castle as it was built: everything laid and remembered
        void Put(VoxelList L, Part p, byte of)
        {
            if (L == null) return;
            for (int i = 0; i < L.Count; i++)
            {
                int x = L.X(i), y = L.Y(i), z = L.Z(i);
                if (!w.InBounds(x, y, z)) continue;
                byte m = L.M(i);
                if (m == 0) { if (Mat.IsVeg[w.Get(x, y, z)]) w.Set(x, y, z, Mat.Air); continue; }
                int k = WaterPlanner.Pack(x, y, z);
                if (part.ContainsKey(k) || !Brushes.Free(w.Get(x, y, z))) continue;
                part[k] = (p, of);
                w.Set(x, y, z, m);
                sim.Remember(x, y, z, m);
            }
        }
        var corners = new[] { (x0, z0, NW), (x0, z1, SW) };
        var shell = new Stroke { Mat = Mat.Stone, Width = 3, Height = High + TowerOver, Level = true, Hollow = true };
        var core = new Stroke { Mat = Mat.Rammed, Width = 1, Height = High + TowerOver - 4, Level = true };
        foreach (var (tx, tz, of) in corners)
        {
            Put(Shapes.Circle(w, s, tx, tz, TowerR, shell, top + TowerOver), Part.Tower, of);
            Put(Shapes.Circle(w, s, tx, tz, TowerR - 2.5, core, top + TowerOver - 4), Part.Core, of);
        }
        // the gatehouse: a tower either side of the gate in the west curtain, at the keep's door, standing out before the wall
        // and over the corner towers (the gateway itself is cut in the curtain below, its head the wall's own)
        var gshell = new Stroke { Mat = Mat.Stone, Width = 3, Height = High + GateTowerOver, Level = true, Hollow = true };
        var gcore = new Stroke { Mat = Mat.Rammed, Width = 1, Height = High + GateTowerOver - 4, Level = true };
        foreach (var (tz, of) in new[] { (kz - GateTowerAt, GN), (kz + GateTowerAt, GS) })
        {
            Put(Shapes.Circle(w, s, gx, tz, GateTowerR, gshell, top + GateTowerOver), Part.Gatehouse, of);
            Put(Shapes.Circle(w, s, gx, tz, GateTowerR - 2.5, gcore, top + GateTowerOver - 4), Part.Core, of);
        }
        var wall = new Stroke { Mat = Mat.Stone, Width = Thick, Height = High, Level = true };
        Put(Shapes.Line(w, s, x0, z0, x0, z1, wall, top), Part.Wall, W);
        Put(Shapes.Line(w, s, x0, z0, x1, z0, wall, top), Part.Wall, N);
        Put(Shapes.Line(w, s, x0, z1, x1, z1, wall, top), Part.Wall, S);
        // the east side behind the keep a palisade: logs set upright as high as the walls, every third one standing a
        // little proud
        var logs = new VoxelList();
        for (int z = z0; z <= z1; z++)
            for (int x = x1 - 1; x <= x1 + 1; x++)
            {
                if (!w.InBounds(x, 1, z) || s.Forbidden(x, z)) continue;
                int g = w.SurfAt(x, z), log = (z - z0) / 3, peak = top + (log % 3 == 1 ? 3 : log % 2);
                for (int y = g; y < peak; y++) logs.Add(x, y, z, Mat.Wood);
            }
        Put(logs, Part.Palisade, E);

        // battlements along the outer edge of the curtains, round the tops of the towers and over the gatehouse
        var crest = new VoxelList();
        bool NearTower(int x, int z)
        {
            foreach (var (tx, tz, _) in corners) if (Brushes.Dist(x - tx, z - tz) < TowerR + 1) return true;
            return false;
        }
        void Merlons(int x, int z, int along, int over, Part on, byte of)
        {
            if (!w.InBounds(x, over, z) || !part.TryGetValue(WaterPlanner.Pack(x, over - 1, z), out var v) || v.part != on || v.of != of
                || NearTower(x, z) || (along % 10 + 10) % 10 >= 6) return;
            for (int y = over; y < over + Crenel; y++) crest.Add(x, y, z, Shapes.Masonry(x, y, z));
        }
        void Crest(byte of) { Put(crest, Part.Battlement, of); crest.Clear(); }
        for (int z = z0; z <= z1; z++) for (int x = x0 - 3; x <= x0 - 2; x++) Merlons(x, z, z - z0, top, Part.Wall, W);
        Crest(W);
        for (int x = x0; x <= x1; x++) for (int z = z0 - 3; z <= z0 - 2; z++) Merlons(x, z, x - x0, top, Part.Wall, N);
        Crest(N);
        for (int x = x0; x <= x1; x++) for (int z = z1 + 2; z <= z1 + 3; z++) Merlons(x, z, x - x0, top, Part.Wall, S);
        Crest(S);
        void Rim(int tx, int tz, int r, int over, Part on, byte of)
        {
            for (int z = tz - r - 1; z <= tz + r + 1; z++)
                for (int x = tx - r - 1; x <= tx + r + 1; x++)
                {
                    if (!w.InBounds(x, over, z) || !part.TryGetValue(WaterPlanner.Pack(x, over - 1, z), out var v) || v.part != on || v.of != of) continue;
                    if (Brushes.Dist(x + .5 - tx, z + .5 - tz) < r - 2) continue;
                    double a = Math.Atan2(z + .5 - tz, x + .5 - tx);
                    if ((int)Math.Floor((a + Math.PI) / (Math.PI * 2) * 24) % 2 == 1) continue;
                    for (int y = over; y < over + Crenel - 2; y++) crest.Add(x, y, z, Shapes.Masonry(x, y, z));
                }
            Crest(of);
        }
        foreach (var (tx, tz, of) in corners) Rim(tx, tz, TowerR, top + TowerOver, Part.Tower, of);
        Rim(gx, kz - GateTowerAt, GateTowerR, top + GateTowerOver, Part.Gatehouse, GN);
        Rim(gx, kz + GateTowerAt, GateTowerR, top + GateTowerOver, Part.Gatehouse, GS);

        // stone stairs up onto the walk from the yard, along the inner faces of the north and south walls
        foreach (var (sz, of) in new[] { (z0 + Thick / 2 + 6, StairN), (z1 - Thick / 2 - 6, StairS) })
            Put(Brushes.Stairs(w, s, kx - StairAt, sz, top, kx + 8, sz, Mat.Stone), Part.Stair, of);

        // the lean-tos along the walls inside: a hall on the north and a barracks on the south behind the stairs, a store
        // and a stable against the west wall either side of the gatehouse; and what is in them
        int inN = z0 + Thick / 2 + 1, inS = z1 - Thick / 2 - 1, inW = x0 + Thick / 2 + 1;
        // (what is in them first, on the ground, before the roofs are over it)
        Furnish(w, Put, kx - 20, inN, 1, 0, 42, 0, 1, Hall);
        Furnish(w, Put, kx - 20, inS, 1, 0, 42, 0, -1, Barracks);
        Furnish(w, Put, inW, kz - 47, 0, 1, 14, 1, 0, Store);
        Furnish(w, Put, inW, kz + 33, 0, 1, 14, 1, 0, Stable);
        LeanTo(w, s, Put, kx - 20, inN, 1, 0, 42, 0, 1, top, b, Mat.Shingle, true, Hall);
        LeanTo(w, s, Put, kx - 20, inS, 1, 0, 42, 0, -1, top, b, Mat.Tile, true, Barracks);
        LeanTo(w, s, Put, inW, kz - 47, 0, 1, 14, 1, 0, top, b, Mat.Shingle, false, Store);
        LeanTo(w, s, Put, inW, kz + 33, 0, 1, 14, 1, 0, top, b, Mat.Thatch, false, Stable);
        // a wayside cross in the yard, south of the way to the keep's door
        Put(WaysideCross(w, s, kx - 52, kz + 24), Part.Furnish, Cross);
        // the household's flag over the keep, flown from a staff set on the iron of its roof
        var flag = new VoxelList();
        int pole = b + WorldGen.KeepHigh + 29;
        for (int y = pole; y < pole + 8; y++) flag.Add(kx, y, kz, Mat.Wood);
        for (int dy = 0; dy < 6; dy++)
            for (int dx = 1; dx <= 10; dx++)
                flag.Add(kx + dx, pole + 7 - dy, kz, dy == 0 || dy == 5 || dx == 10 ? Mat.BannerTrim : Mat.Banner);
        Put(flag, Part.Flag, Keep);

        // the gate in the gatehouse, in line with the citadel's door
        Gateway gate = null;
        var outer = Face(w, gx - 40, kz, +1, top - 10);
        var plan = outer.HasValue ? GatePlan.At(w, s, outer.Value) : null;
        if (plan != null) gate = sim.AddGate(plan, built: true);

        // ---- what the years left of it
        // how high each stretch of the curtains still stands, as a share of its height: breached down to the ground where
        // the old fort was (the west wall behind the store, the north behind the hall, the south behind its stair: a way
        // in and out each, through the open store, the hall's door, up and down the stair), the tops broken off here and
        // there everywhere else; the palisade behind the keep rotted down to stumps
        double Standing(byte of, int u)
        {
            double n = noise.Sample(u * .06 + of * 31, 3.7);
            switch (of)
            {
                case W: return u >= z0 + 20 && u <= z0 + 31 ? .05 : .95 + .12 * n;
                case N: return u >= kx + 2 && u <= kx + 13 ? 0 : .9 + .14 * n;
                case S: return u >= kx - 44 && u <= kx - 31 ? .06 : .9 + .14 * n;
                default: return u >= kz - 14 && u <= kz - 3 ? 0 : .86 + .16 * n;
            }
        }
        int StandsTo(Part p, byte of, int x, int y, int z)
        {
            switch (p)
            {
                case Part.Battlement when of is NW or SW: return of == NW && Hash.H3(x, z, 59) < .8 ? int.MaxValue : 0;
                case Part.Battlement when of is GN or GS: return Hash.H3(x, z, 57) < .88 ? int.MaxValue : 0;
                case Part.Wall:
                case Part.Battlement:
                {
                    int u = of is W or E ? z : x;
                    double f = Standing(of, u);
                    if (p == Part.Battlement) return f >= 1 && Hash.H3(x, z, 61) < .72 ? int.MaxValue : 0;
                    int to = b + (int)Math.Round(f * High) - (int)(noise.Sample(u * .45, of * 7.1) * 3);
                    // the broken top crumbles unevenly
                    return f >= 1 ? int.MaxValue : y >= to - 2 && Hash.H3(x * 3 + y, z, 63) < .35 ? y : to;
                }
                case Part.Tower when of == NW:
                {
                    double a = Math.Atan2(z + .5 - z0, x + .5 - x0);
                    return b + High + TowerOver - (int)(noise.Sample(a * 2 + 9, 1.3) * 5);
                }
                case Part.Tower when of == SW:
                {
                    // a stump, standing highest on the side toward the yard
                    double a = Math.Atan2(z + .5 - z1, x + .5 - x0), up = .5 + .5 * Math.Cos(a + 2.4);
                    return b + (int)((High + TowerOver) * (.22 + .58 * up * up)) - (int)(Hash.H3(x, z, 65) * 3);
                }
                case Part.Core: return of == SW ? b + 8 + (int)(Hash.H3(x, z, 67) * 4) : int.MaxValue;
                case Part.Palisade:
                {
                    int log = (z - z0) / 3;
                    return b + 1 + (int)(Hash.H3(log, 2, 69) * 4);
                }
                case Part.Stair: return of == StairS ? b + 13 : int.MaxValue;
                case Part.Roof when of == Stable: return Hash.H3(x, y, z * 3) < .18 ? int.MaxValue : 0;
                case Part.Lean when of == Stable: return Hash.H3(x, 3, z) < .5 ? b + 4 + (int)(Hash.H3(x, 4, z) * 8) : int.MaxValue;
                default: return int.MaxValue;
            }
        }
        // holes knocked through the curtains here and there, too small to get through
        bool Holed(byte of, int x, int y, int z) =>
            of == W && z >= kz + 36 && z <= kz + 39 && y >= b + 9 && y < b + 13 ||
            of == N && x >= kx - 40 && x <= kx - 37 && y >= b + 12 && y < b + 15 ||
            of == S && x >= kx - 46 && x <= kx - 43 && y >= b + 6 && y < b + 9;
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
        // and what fell lies at the foot, mostly outside the walls: stones from the masonry, broken timber from the stable
        foreach (var (x, y, z, p, of) in fell)
        {
            if (p == Part.Core) continue;
            bool timber = p is Part.Lean or Part.Roof or Part.Palisade;
            double chance = timber ? .2 : .1, h = Hash.H3(x * 7 + y, z * 3, 71);
            if (h > chance) continue;
            double out1 = Hash.H3(x + 73, y * 5, z) < .65 ? 1 : -1, dist = 2 + Hash.H3(y, x, z * 11) * 9, side = (Hash.H3(z, y, x * 13) - .5) * 6;
            (double nx, double nz) = of switch
            {
                N => (0.0, -1.0),
                S => (0.0, 1.0),
                E => (1.0, 0.0),
                NW => Norm(x + .5 - x0, z + .5 - z0),
                SW => Norm(x + .5 - x0, z + .5 - z1),
                StairS or Barracks => (0.0, -1.0),
                Stable => (1.0, 0.0),
                _ => (-1.0, 0.0),
            };
            if (p == Part.Tower || p == Part.Battlement && of is NW or SW || timber || p == Part.Stair) out1 = 1;
            if (timber) dist = Hash.H3(y, x, z * 11) * 4;
            int rx = x + (int)Math.Round(nx * out1 * (dist + 3) - nz * side), rz = z + (int)Math.Round(nz * out1 * (dist + 3) + nx * side);
            if (!w.InBounds(rx, 0, rz) || s.Forbidden(rx, rz) || s.WaterTop != null && s.WaterTop[rz * w.SX + rx] > 0) continue;
            int ry = w.SurfAt(rx, rz);
            // (scattered, not heaped: a heap at the foot would be a step up the wall)
            if (ry <= 0 || ry > b + 6 || ry > s.HTop[rz * w.SX + rx] + 1 || !Brushes.Free(w.Get(rx, ry, rz))) continue;
            byte m = p == Part.Palisade ? Mat.Wood : timber ? Mat.Lumber : Hash.H3(rx, ry, rz) < .12 ? Mat.Moss : Shapes.Masonry(rx, ry, rz);
            w.Set(rx, ry, rz, m);
        }
        if (gate != null) sim.UnhangGate(gate);
        // what of the ruin would not stand has come down long ago: what hung off the broken courses too (a roof's edge
        // over a wall broken lower than it, say), so that nothing of it comes down in a game's first moments as if knocked
        for (int round = 0; round < 3; round++)
            if (sim.Subside(gx - GateTowerR - 8, z0 - TowerR - 8, x1 + 8, z1 + TowerR + 8, weigh: round > 0) == 0 && round > 0) break;

        // the yard's furnishing, set out whole: a well by the way to the keep, benches before the hall, a woodpile by the
        // barracks, barrels by the store and a cart by the stable
        bool instant = sim.InstantLabour;
        sim.InstantLabour = true;
        Refused = "";
        foreach (var (kind, x, z, rot) in new[]
        {
            (DecorKind.Well, kx - 52, kz - 22, 0), (DecorKind.Bench, kx - 9, inN + LeanDeep + 9, 0), (DecorKind.Bench, kx + 13, inN + LeanDeep + 9, 0),
            (DecorKind.Woodpile, kx + 12, inS - LeanDeep - 6, 2), (DecorKind.Barrels, inW + LeanDeep + 8, kz - 40, 3), (DecorKind.Cart, inW + LeanDeep + 10, kz + 40, 1),
        })
        {
            // where it was meant to stand, or the nearest spot about it where it will (rubble may lie there)
            string why = null;
            bool set = false;
            for (int r = 0; r <= 12 && !set; r += 3)
                for (int a = 0; a < (r == 0 ? 1 : 8) && !set; a++)
                {
                    int px = x + (int)Math.Round(Math.Cos(a * Math.PI / 4) * r), pz = z + (int)Math.Round(Math.Sin(a * Math.PI / 4) * r);
                    why = sim.PlaceDecor(kind, px, pz, rot);
                    set = why == null;
                }
            if (!set) Refused += $"{kind} ({x - kx}, {z - kz}): {why}; ";
        }
        sim.InstantLabour = instant;

        // what is left standing keeps its torches, its banners and what lies about the yard
        sim.Decorate();
        sim.Nav.Flush();
        sim.FortArea = Area(s);
        sim.FortGate = gate;
        return gate;
    }

    /// <summary>
    /// A lean-to against a wall: so long along it from (ax, az) going (ux, uz), standing out from it (dx, dz) as deep as
    /// <see cref="LeanDeep"/>; its roof of shingles, tiles or thatch, two courses thick so that each course rests on the
    /// next, from under the wall-walk down to the eaves over its front; timber posts along the front, its ends of daub;
    /// closed (its front daubed, a door in its middle, windows either side) or open between its posts.
    /// </summary>
    static void LeanTo(VoxelWorld w, Site s, Action<VoxelList, Part, byte> put, int ax, int az, int ux, int uz, int len, int dx, int dz,
        int top, int b, byte roof, bool closed, byte of)
    {
        var walls = new VoxelList(); var cover = new VoxelList();
        int high = top - 4, low = b + 17, mid = len / 2;
        for (int u = -1; u <= len + 1; u++)
            for (int d = 0; d <= LeanDeep + 2; d++)
            {
                int x = ax + ux * u + dx * d, z = az + uz * u + dz * d;
                if (!w.InBounds(x, 1, z) || s.Forbidden(x, z)) continue;
                int roofY = high - (high - low) * d / LeanDeep;
                cover.Add(x, roofY, z, roof);
                cover.Add(x, roofY + 1, z, roof);
                if (u < 0 || u > len || d > LeanDeep) continue;
                int g = w.SurfAt(x, z);
                bool end = u == 0 || u == len, front = d == LeanDeep;
                // posts along the front and against the wall at the back, a beam along each under the roof, and rafters from
                // the one to the other a stride apart: it stands on its own timbers, whatever is left of the wall behind it
                bool back = d == 0, post = (front || back) && (u % 12 == 0 || end), rafter = u % 6 == 0 || end;
                for (int y = g; y < roofY; y++)
                {
                    int h = y - g;
                    if (post) { walls.Add(x, y, z, Mat.Wood); continue; }
                    // the beams and the rafters under the roof
                    if ((front || back || rafter) && y == roofY - 1) { walls.Add(x, y, z, Mat.Wood); continue; }
                    if (end) { walls.Add(x, y, z, Mat.Daub); continue; }
                    if (!front || !closed) continue;
                    if (Math.Abs(u - mid) <= 4 && h < 16) continue;
                    if (Math.Abs(Math.Abs(u - mid) - 13) <= 1 && h >= 7 && h < 11) continue;
                    walls.Add(x, y, z, Mat.Daub);
                }
            }
        put(walls, Part.Lean, of);
        put(cover, Part.Roof, of);
    }

    /// <summary>
    /// What stands in a lean-to: the hall's long table and its benches, the barracks' bunks, the store's casks, crates and
    /// sacks, the stable's straw and its trough.
    /// </summary>
    static void Furnish(VoxelWorld w, Action<VoxelList, Part, byte> put, int ax, int az, int ux, int uz, int len, int dx, int dz, byte of)
    {
        var L = new VoxelList();
        void P(int u, int h, int d, byte m)
        {
            int x = ax + ux * u + dx * d, z = az + uz * u + dz * d;
            if (w.InBounds(x, 1, z)) L.Add(x, w.SurfAt(x, z) + h, z, m);
        }
        switch (of)
        {
            case Hall:
                for (int u = 8; u <= len - 8; u++)
                {
                    for (int d = 7; d <= 9; d++) P(u, 4, d, Mat.Table);
                    P(u, 2, 5, Mat.Bench); P(u, 2, 11, Mat.Bench);
                    if (u == 8 || u == len - 8) { for (int h = 0; h < 4; h++) { P(u, h, 7, Mat.Table); P(u, h, 9, Mat.Table); } P(u, 0, 5, Mat.Bench); P(u, 1, 5, Mat.Bench); P(u, 0, 11, Mat.Bench); P(u, 1, 11, Mat.Bench); }
                }
                break;
            case Barracks:
                for (int at = 3; at + 8 <= len - 2; at += 10)
                    for (int u = at; u < at + 8; u++)
                        for (int d = 1; d <= 6; d++)
                        {
                            for (int h = 0; h < 3; h++) if (h == 2 || u == at || u == at + 7 || d == 1 || d == 6) P(u, h, d, Mat.Bunk);
                            P(u, 3, d, Mat.Bedding);
                        }
                break;
            case Store:
                // two casks on end, hooped, a crate on another and sacks by them
                foreach (int at in new[] { 2, 7 })
                    for (int u = at; u < at + 4; u++)
                        for (int d = 2; d < 6; d++)
                            for (int h = 0; h < 6; h++) P(u, h, d, h is 1 or 4 ? Mat.Hoop : Mat.Cask);
                for (int u = 2; u < 7; u++)
                    for (int d = 8; d < 13; d++)
                        for (int h = 0; h < 5; h++)
                            P(u, h, d, u is 2 or 6 || d is 8 or 12 || h is 0 or 4 ? Mat.CrateEdge : Mat.Crate);
                for (int u = 9; u < 12; u++) for (int d = 9; d < 12; d++) for (int h = 0; h < 3; h++) P(u, h, d, Mat.Sack);
                break;
            case Stable:
                // hay heaped against the wall, and a trough along the front
                for (int u = 1; u < len; u++)
                    for (int d = 1; d < 7; d++)
                        for (int h = 0; h < 5 - Math.Abs(d - 3) / 2 - (u * 7 % 3 == 0 ? 1 : 0); h++) P(u, h, d, Mat.Thatch);
                for (int u = 3; u < len - 2; u++)
                    for (int d = 11; d <= 13; d++)
                        for (int h = 0; h < 3; h++) if (h == 0 || d != 12 || u == 3 || u == len - 3) P(u, h, d, Mat.Wood);
                break;
        }
        put(L, Part.Furnish, of);
    }

    /// <summary>A stone cross on three steps, as a wayside shrine has.</summary>
    static VoxelList WaysideCross(VoxelWorld w, Site s, int cx, int cz)
    {
        var L = new VoxelList();
        if (!w.InBounds(cx, 1, cz) || s.Forbidden(cx, cz)) return L;
        int g = w.SurfAt(cx, cz);
        for (int step = 0; step < 3; step++)
        {
            int r = 3 - step;
            for (int z = cz - r; z <= cz + r; z++) for (int x = cx - r; x <= cx + r; x++) L.Add(x, g + step, z, Shapes.Masonry(x, g + step, z));
        }
        for (int y = g + 3; y < g + 17; y++) L.Add(cx, y, cz, Mat.StoneLight);
        for (int dz = -3; dz <= 3; dz++) L.Add(cx, g + 13, cz + dz, Mat.StoneLight);
        return L;
    }

    /// <summary>
    /// The lean-tos inside the castle as they stand (a hall, a barracks, a store, a stable): rectangles of columns, voxels.
    /// Buildings, not walls: the architect's look over what stands takes no line along their fronts. None where a castle
    /// was never raised, or an old fort of a game saved before stands instead.
    /// </summary>
    public static List<(int x0, int z0, int x1, int z1)> Sheds(Sim sim)
    {
        var s = sim.Site;
        var list = new List<(int, int, int, int)>();
        if (!s.HasKeep || sim.FortArea == default) return list;
        int kx = s.KeepX, kz = s.KeepZ, b = s.KeepBase;
        int inN = kz - North + Thick / 2 + 1, inS = kz + South - Thick / 2 - 1, inW = kx - West + Thick / 2 + 1;
        // each with the post at its back corner it was raised with
        foreach (var (px, pz, r) in new[]
        {
            (kx - 20, inN, (kx - 21, inN, kx + 23, inN + LeanDeep + 2)), (kx - 20, inS, (kx - 21, inS - LeanDeep - 2, kx + 23, inS)),
            (inW, kz - 47, (inW, kz - 48, inW + LeanDeep + 2, kz - 32)), (inW, kz + 33, (inW, kz + 32, inW + LeanDeep + 2, kz + 48)),
        })
            for (int y = b - 4; y < b + 8; y++)
                if (sim.MeantAt(px, y, pz) == Mat.Wood) { list.Add(r); break; }
        return list;
    }

    /// <summary>Whether a column is on the castle's curtain walls, between the corner towers.</summary>
    public static bool OnCurtain(Site s, int x, int z)
    {
        int x0 = s.KeepX - West, x1 = s.KeepX + East, z0 = s.KeepZ - North, z1 = s.KeepZ + South;
        foreach (var (cx, cz) in new[] { (x0, z0), (x0, z1), (x1, z0), (x1, z1) }) if (Brushes.Dist(x - cx, z - cz) < TowerR + 1) return false;
        bool alongZ = z >= z0 && z <= z1, alongX = x >= x0 && x <= x1;
        return alongZ && (Math.Abs(x - x0) <= Thick / 2 + 1 || Math.Abs(x - x1) <= Thick / 2 + 1) || alongX && (Math.Abs(z - z0) <= Thick / 2 + 1 || Math.Abs(z - z1) <= Thick / 2 + 1);
    }

    /// <summary>
    /// Whether a column is on the castle's west side: its curtain, the gatehouse's towers, the towers on its corners, and
    /// the store and the stable leaning on it.
    /// </summary>
    public static bool WestSide(Site s, int x, int z)
    {
        int x0 = s.KeepX - West, z0 = s.KeepZ - North, z1 = s.KeepZ + South, kz = s.KeepZ, gx = x0 - GateDeep;
        if (Math.Abs(x - x0) <= Thick / 2 + 1 && z >= z0 && z <= z1) return true;
        int inW = x0 + Thick / 2 + 1;
        if (x >= inW && x <= inW + LeanDeep + 2 && (z >= kz - 48 && z <= kz - 32 || z >= kz + 32 && z <= kz + 48)) return true;
        if (Brushes.Dist(x - gx, z - (kz - GateTowerAt)) <= GateTowerR + .5 || Brushes.Dist(x - gx, z - (kz + GateTowerAt)) <= GateTowerR + .5) return true;
        return Brushes.Dist(x - x0, z - z0) <= TowerR + .5 || Brushes.Dist(x - x0, z - z1) <= TowerR + .5;
    }

    /// <summary>What the valley grew or was made of, and nobody built: ground, rock, trees and what grows.</summary>
    static bool Wild(byte m) => Mat.IsNatural[m] || Mat.IsVeg[m] || Mat.IsTrunk(m) || m is Mat.Leaf or Mat.Foliage or Mat.NeedleLight or Mat.FoliageLight or Mat.FoliageGold or Mat.Tuft;

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
