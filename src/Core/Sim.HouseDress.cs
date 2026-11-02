using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Houses dress themselves as the town's buildings do (Sim.Frontage.cs), each by its plot, so that no two of a street
/// read the same: once a house drawn with the rectangle stands closed as a dwelling, flower boxes hang under the windows
/// of some, a lamp burns by the door of others, a bench, a barrel and a crate or pots of flowers stand by the door, logs
/// are stacked against an end, a chimney goes up a gable end of most (square, narrowing at the top, with two pots on it,
/// or capped with a dressed stone), and behind some a vegetable garden in a low wattle fence, or a washing line on two
/// posts. Like the rest of the dressing it costs nothing and stands in nobody's way: off the ways and the roads, clear
/// of the doors, whatever is built where it stands takes its place, and what is gone is set out again a while after,
/// once there is room and no alarm.
/// </summary>
public sealed partial class Sim
{
    double _nextHouseDress;
    /// <summary>Whether the houses dress themselves (off for a scene of the houses as they were before).</summary>
    public bool HouseDressing = true;

    /// <summary>Every few seconds, while no alarm is up: what of the houses' dressing is missing is set out again.</summary>
    void TickHouseDress()
    {
        if (Time < _nextHouseDress) return;
        _nextHouseDress = Time + 6;
        if (Alarm || !HouseDressing) return;
        DressHouses();
    }

    /// <summary>What of every closed house's dressing is missing set out now (for the scenes staged and the tests).</summary>
    public void DressHouses()
    {
        foreach (var z in Zones)
            if (z.Kind == Zone.Dwelling && z.Closed && z.Depth > 1 && BuildingOn(z) == null) DressHouse(z);
    }

    /// <summary>The pieces of a house's dressing standing now: what is hung on or stands by its walls, its lamp and chimney among them.</summary>
    public List<Fixture> DressOf(Zone z)
    {
        var list = new List<Fixture>();
        foreach (var f in Fixtures)
        {
            if (f.Kind is not (FixtureKind.HouseProp or FixtureKind.WindowBox or FixtureKind.Garden or FixtureKind.Chimney or FixtureKind.Torch)) continue;
            var (ax, _, az) = WaterPlanner.Unpack(f.Anchor);
            if (ax >= z.X0 && ax <= z.X1 && az >= z.Z0 && az <= z.Z1) list.Add(f);
        }
        return list;
    }

    /// <summary>The roll of a house's plot for one of its dressing's choices.</summary>
    static double DressRoll(Zone z, int salt) => PlotRoll(z.X0, z.Z0, z.X1, z.Z1, 20 + salt);

    /// <summary>Sets out what of a house's dressing is missing and has room for it now.</summary>
    void DressHouse(Zone z)
    {
        var doors = DoorsOf(z);
        if (doors.Count == 0) return;
        var g = doors[0];
        var (mx, my, mz) = g.Middle;
        int nx = 0, nz = 0, fx, fz, fy = (int)my;
        if (g.AlongX) { nz = mz < (z.Z0 + z.Z1) / 2f ? -1 : 1; fz = nz < 0 ? z.Z0 : z.Z1; fx = (int)MathF.Floor(mx); }
        else { nx = mx < (z.X0 + z.X1) / 2f ? -1 : 1; fx = nx < 0 ? z.X0 : z.X1; fz = (int)MathF.Floor(mz); }
        int ax = -nz, az = nx;
        int ua = nx != 0 ? (z.Z0 - fz) * az : (z.X0 - fx) * ax, ub = nx != 0 ? (z.Z1 - fz) * az : (z.X1 - fx) * ax;
        int umin = Math.Min(ua, ub), umax = Math.Max(ua, ub);
        var swing = new HashSet<int>();
        foreach (var d in Gates)
        {
            var (dx, _, dz) = d.Middle;
            if (dx < z.X0 - 40 || dx > z.X1 + 40 || dz < z.Z0 - 40 || dz > z.Z1 + 40) continue;
            for (int st = 0; st <= Gateway.StepOf(1); st++)
                for (int i = 0; i < Gateway.Voxels; i++) { var (x, y, zz) = d.Place(i, st); swing.Add(WaterPlanner.Pack(x, y, zz)); }
        }
        // by the door: a lamp, then one thing on either side of it
        bool right = DressRoll(z, 1) < .5;
        if (DressRoll(z, 2) < .5) SetOut(DoorLamp(), fx, fy, fz, ax, az, nx, nz, umin, umax, swing, offWays: true);
        double by = DressRoll(z, 3);
        var near = by < .3 ? HouseBench : by < .55 ? HousePots(z) : by < .75 ? HouseBarrel : null;
        if (near != null) SetOut(right ? near : near.Mirror(), fx, fy, fz, ax, az, nx, nz, umin, umax, swing, offWays: true);
        if (DressRoll(z, 4) < .35)
        {
            var other = DressRoll(z, 5) < .5 ? HousePots(z) : HouseBarrel;
            SetOut(right ? other.Mirror() : other, fx, fy, fz, ax, az, nx, nz, umin, umax, swing, offWays: true);
        }
        if (DressRoll(z, 6) < .5) WindowBoxes(z);
        if (DressRoll(z, 7) < .75) SetOutChimney(z.X0, z.Z0, z.X1, z.Z1, fy, nx, nz, DressOf(z).Find(f => f.Kind == FixtureKind.Chimney),
            build: (int)(DressRoll(z, 8) * 4), prefer: DressRoll(z, 9) < .5 ? 1 : 0);
        if (DressRoll(z, 10) < .45) Woodpile(z, nx, nz, DressRoll(z, 11) < .5);
        double back = DressRoll(z, 12);
        if (back < .3) Garden(z, nx, nz);
        else if (back < .55) WashingLine(z, nx, nz);
    }

    /// <summary>
    /// The dressing standing in a house's wall where its door is about to be cut, or before it, taken down: the door
    /// wins (a house beside it moved its door here, or the player put one in with the gate tool).
    /// </summary>
    void ClearBefore(Shapes.HouseDoor d)
    {
        int threshold = d.Y - 8, across = d.NX != 0 ? d.Z : d.X;
        for (int i = Fixtures.Count - 1; i >= 0; i--)
        {
            // (a yard's fire stands with its yard thing, and goes with it)
            if (Fixtures[i].Kind is FixtureKind.Campfire or FixtureKind.Brazier) continue;
            foreach (var (x, y, z, _) in Fixtures[i].Voxels)
            {
                int u = (d.NX != 0 ? z : x) - across, k = d.NX != 0 ? (x - d.X) * d.NX : (z - d.Z) * d.NZ;
                if (Math.Abs(u) > Gateway.Half + 6 || k < -d.Thick || k > 10 || y < threshold || y >= threshold + 22) continue;
                TakeDown(i);
                break;
            }
        }
    }

    // ------------------------------------------------------------ by the door

    /// <summary>A bench of boards against the wall on the door's right, its seat knee-high.</summary>
    static readonly Kit HouseBench = MakeBench();

    static Kit MakeBench()
    {
        var k = new Kit { Kind = FixtureKind.HouseProp, AU = 18 };
        k.Box(14, 3, 1, 23, 3, 2, Mat.Crate);
        foreach (int u in new[] { 14, 23 }) k.Box(u, 0, 1, u, 2, 2, Mat.CrateEdge);
        return k;
    }

    /// <summary>A barrel, and a crate beside it, on the door's right.</summary>
    static readonly Kit HouseBarrel = MakeBarrel();

    static Kit MakeBarrel()
    {
        var k = new Kit { Kind = FixtureKind.HouseProp, AU = 17 };
        Barrel(k, 14, 1, 6);
        Crate(k, 19, 0, 1, 4);
        return k;
    }

    /// <summary>Two clay pots of flowers on the door's right, the flowers the house's own colour.</summary>
    static Kit HousePots(Zone z)
    {
        byte bloom = Blooms[(int)(DressRoll(z, 13) * Blooms.Length) % Blooms.Length];
        var k = new Kit { Kind = FixtureKind.HouseProp, AU = 16 };
        foreach (int u0 in new[] { 14, 18 })
        {
            k.Box(u0, 0, 1, u0 + 1, 1, 2, Mat.Paint);
            k.Box(u0, 2, 1, u0 + 1, 2, 2, bloom);
        }
        return k;
    }

    /// <summary>The flowers a house grows in its boxes and pots: poppies, daisies, cornflowers, or the meadow's yellow and violet.</summary>
    static readonly byte[] Blooms = { Mat.FlowerRed, Mat.FlowerWhite, Mat.FlowerBlue, Mat.Flower, Mat.Bloom };

    // ------------------------------------------------------------ on the walls and by them

    /// <summary>
    /// A box of flowers under each window in the outer face of a house's walls: boards a quarter of a metre out from the
    /// wall a hand below the sill, earth in it and the flowers over it, the house's colour.
    /// </summary>
    void WindowBoxes(Zone z)
    {
        byte bloom = Blooms[(int)(DressRoll(z, 14) * Blooms.Length) % Blooms.Length];
        int shared = SharedSidesOf(z);
        // the walls' outer faces: each side, its way out and the columns along it (not one shared with a house beside it:
        // its face is in the other's room)
        for (int side = 0; side < 4; side++)
        {
            if ((shared & 1 << side) != 0) continue;
            var (nx, nz) = Shapes.SideNormal(side);
            bool alongX = nz != 0;
            int face = side switch { 0 => z.Z0, 1 => z.Z1, 2 => z.X0, _ => z.X1 };
            int a0 = alongX ? z.X0 : z.Z0, a1 = alongX ? z.X1 : z.Z1;
            for (int a = a0 + 1; a < a1; a++)
            {
                int x = alongX ? a : face, zz = alongX ? face : a;
                // the lowest of a window's glass, where the glass begins over the one under it
                for (int y = Math.Max(2, z.Depth - Shapes.HouseHeight - 4); y < z.Depth && y < World.SY - 1; y++)
                {
                    byte m = World.Get(x, y, zz);
                    if (m is not (Mat.Window or Mat.WindowLog) || World.Get(x, y - 1, zz) is Mat.Window or Mat.WindowLog) continue;
                    BoxUnder(z, x, y, zz, nx, nz, alongX, bloom);
                }
            }
        }
    }

    void BoxUnder(Zone z, int x, int glass, int zz, int nx, int nz, bool alongX, byte bloom)
    {
        int tx = alongX ? 1 : 0, tz = alongX ? 0 : 1;
        // the wall it hangs on: under the sill, standing
        int ay = glass - 2;
        if (!Built(x, ay, zz)) return;
        var f = new Fixture { Kind = FixtureKind.WindowBox, Anchor = WaterPlanner.Pack(x, ay, zz) };
        for (int k = -2; k <= 2; k++)
            for (int t = 1; t <= 2; t++)
            {
                int px = x + tx * k + nx * t, pz = zz + tz * k + nz * t;
                bool end = k == -2 || k == 2;
                f.Voxels.Add((px, glass - 2, pz, end ? Mat.CrateEdge : Mat.Crate));
                f.Voxels.Add((px, glass - 1, pz, end || t == 2 ? Mat.CrateEdge : Mat.Cask));
                if (!end && t == 1) f.Voxels.Add((px, glass, pz, bloom));
            }
        foreach (var (px, py, pz, _) in f.Voxels) if (!HasRoom(px, py, pz) || ZoneAt(px, pz) is { Kind: Zone.Dwelling }) return;
        int lo = Math.Min(x - 3, x + nx * 3), hi = Math.Max(x + 3, x + nx * 3), lz = Math.Min(zz - 3, zz + nz * 3), hz = Math.Max(zz + 3, zz + nz * 3);
        if (!NobodyIn(lo, lz, hi, hz, glass - 2, glass)) return;
        HangFront(f);
    }

    /// <summary>Logs stacked against one of a house's ends, their cut faces out, a little roof of boards over them.</summary>
    void Woodpile(Zone z, int doorNx, int doorNz, bool farEnd)
    {
        // the ends: the short walls (the house's door is in a long one, or an end; then the other)
        bool alongX = z.X1 - z.X0 >= z.Z1 - z.Z0;
        int s = farEnd ? 1 : -1;
        if (alongX ? doorNx == s : doorNz == s) s = -s;
        int face = alongX ? (s > 0 ? z.X1 : z.X0) : (s > 0 ? z.Z1 : z.Z0);
        int c = alongX ? (z.Z0 + z.Z1) / 2 : (z.X0 + z.X1) / 2;
        (int x, int z) At(int along, int out_) => alongX ? (face + s * out_, c + along) : (c + along, face + s * out_);
        var (wx, wz) = At(0, 0);
        int lo = int.MaxValue, hi = int.MinValue;
        for (int a = -6; a <= 6; a++)
            for (int t = 1; t <= 3; t++)
            {
                var (x, zz) = At(a, t);
                if (!World.InBounds(x, 1, zz) || KeptClear(x, zz) || ZoneAt(x, zz) is { Kind: Zone.Dwelling }) return;
                int g = FrontGround(x, zz);
                if (World.Get(x, g - 1, zz) is Mat.Water or Mat.Acid or Mat.Pitch or Mat.Lava) return;
                lo = Math.Min(lo, g); hi = Math.Max(hi, g);
            }
        if (hi - lo > 2 || !Built(wx, hi + 2, wz)) return;
        var f = new Fixture { Kind = FixtureKind.HouseProp, Anchor = WaterPlanner.Pack(wx, hi + 2, wz) };
        for (int a = -6; a <= 6; a++)
            for (int t = 1; t <= 3; t++)
            {
                var (x, zz) = At(a, t);
                int top = Math.Abs(a) == 6 ? 4 : 5;
                for (int y = FrontGround(x, zz); y < hi + top; y++)
                    f.Voxels.Add((x, y, zz, (a + y + t) % 3 == 0 ? Mat.CrateEdge : Mat.Cask));
                // the boards over it, sloping off the wall
                f.Voxels.Add((x, hi + top + (t == 1 ? 1 : 0), zz, Mat.Signboard));
            }
        foreach (var (px, py, pz, _) in f.Voxels) if (!HasRoom(px, py, pz)) return;
        HangFront(f);
    }

    // ------------------------------------------------------------ behind

    /// <summary>The side of a house away from its door: the way out of it, its face and the columns along it.</summary>
    static (int nx, int nz, int face, bool alongX, int a0, int a1) Behind(Zone z, int doorNx, int doorNz)
    {
        int nx = -doorNx, nz = -doorNz;
        bool alongX = nz != 0;
        int face = nz < 0 ? z.Z0 : nz > 0 ? z.Z1 : nx < 0 ? z.X0 : z.X1;
        return (nx, nz, face, alongX, alongX ? z.X0 : z.Z0, alongX ? z.X1 : z.Z1);
    }

    /// <summary>
    /// A vegetable garden behind a house: rows of greens and flowering beans a pace apart, and a low wattle fence of
    /// posts and two rails round its three outer sides with a gap in the far one. On level turf only.
    /// </summary>
    void Garden(Zone z, int doorNx, int doorNz)
    {
        var (nx, nz, face, alongX, a0, a1) = Behind(z, doorNx, doorNz);
        if (nx == 0 && nz == 0) return;
        int from = a0 + 8, to = a1 - 8, depth = 22;
        if (to - from < 16) return;
        (int x, int z) At(int a, int t) => alongX ? (a, face + nz * t) : (face + nx * t, a);
        int lo = int.MaxValue, hi = int.MinValue;
        for (int a = from; a <= to; a++)
            for (int t = 3; t <= depth; t++)
            {
                var (x, zz) = At(a, t);
                if (!World.InBounds(x, 1, zz) || Site.Forbidden(x, zz) || KeptClear(x, zz) || ZoneAt(x, zz) is { Kind: Zone.Dwelling }) return;
                int g = World.SurfAt(x, zz);
                byte under = World.Get(x, g - 1, zz);
                if (!Mat.IsGrass(under) && under is not (Mat.Soil or Mat.Moss)) return;
                lo = Math.Min(lo, g); hi = Math.Max(hi, g);
            }
        if (hi - lo > 2) return;
        var (wx, wz) = At((from + to) / 2, 0);
        if (!Built(wx, lo + 2, wz) && !Built(wx, lo + 6, wz)) return;
        var f = new Fixture { Kind = FixtureKind.Garden, Anchor = WaterPlanner.Pack(wx, Built(wx, lo + 2, wz) ? lo + 2 : lo + 6, wz) };
        byte bloom = Blooms[(int)(DressRoll(z, 15) * Blooms.Length) % Blooms.Length];
        int gap = (from + to) / 2;
        for (int a = from; a <= to; a++)
            for (int t = 3; t <= depth; t++)
            {
                var (x, zz) = At(a, t);
                int g = World.SurfAt(x, zz);
                bool edge = a == from || a == to || t == depth;
                if (edge)
                {
                    if (t == depth && Math.Abs(a - gap) <= 3) continue;
                    bool post = t == depth ? (a - from) % 5 == 0 || a == to : (depth - t) % 5 == 0 || t == 3;
                    for (int y = 0; y < 4; y++) if (post || y is 1 or 3) f.Voxels.Add((x, g + y, zz, post ? Mat.CrateEdge : Mat.Crate));
                    continue;
                }
                // the rows across it, a voxel of path between each pair
                if (a <= from + 1 || a >= to - 1 || t <= 4 || t >= depth - 1 || t % 3 == 0) continue;
                f.Voxels.Add((x, g, zz, (a + t) % 4 == 0 ? bloom : Mat.Fern));
            }
        foreach (var (px, py, pz, _) in f.Voxels) if (!HasRoom(px, py, pz)) return;
        HangFront(f);
    }

    /// <summary>A washing line behind a house: two posts, a line between them, the washing hung on it in the house's colours.</summary>
    void WashingLine(Zone z, int doorNx, int doorNz)
    {
        var (nx, nz, face, alongX, a0, a1) = Behind(z, doorNx, doorNz);
        if (nx == 0 && nz == 0) return;
        int mid = (a0 + a1) / 2, half = Math.Min(12, (a1 - a0) / 2 - 6), t = 9;
        if (half < 6) return;
        (int x, int z) At(int a, int tt) => alongX ? (a, face + nz * tt) : (face + nx * tt, a);
        int lo = int.MaxValue, hi = int.MinValue;
        for (int a = mid - half; a <= mid + half; a++)
            for (int tt = t - 1; tt <= t + 1; tt++)
            {
                var (x, zz) = At(a, tt);
                if (!World.InBounds(x, 1, zz) || Site.Forbidden(x, zz) || KeptClear(x, zz) || ZoneAt(x, zz) is { Kind: Zone.Dwelling }) return;
                int g = FrontGround(x, zz);
                if (World.Get(x, g - 1, zz) is Mat.Water or Mat.Acid or Mat.Pitch or Mat.Lava) return;
                lo = Math.Min(lo, g); hi = Math.Max(hi, g);
            }
        if (hi - lo > 3) return;
        var (wx, wz) = At(mid, 0);
        if (!Built(wx, hi + 2, wz)) return;
        var f = new Fixture { Kind = FixtureKind.HouseProp, Anchor = WaterPlanner.Pack(wx, hi + 2, wz) };
        int top = hi + 14;
        foreach (int a in new[] { mid - half, mid + half })
        {
            var (x, zz) = At(a, t);
            for (int y = FrontGround(x, zz); y <= top; y++) f.Voxels.Add((x, y, zz, Mat.CrateEdge));
        }
        byte[] cloth = { Mat.Canvas, Mat.Banner, Mat.Canvas, Mat.Paint, Mat.Sack, Mat.Canvas };
        int n = (int)(DressRoll(z, 16) * 3);
        for (int a = mid - half + 1; a < mid + half; a++)
        {
            var (x, zz) = At(a, t);
            f.Voxels.Add((x, top - 1, zz, Mat.Hoop));
            // a shirt, a sheet, a cloth: three voxels wide each, a voxel between
            int k = a - (mid - half + 2);
            if (k < 0 || k % 4 == 3) continue;
            int piece = k / 4, drop = (piece + n) % 2 == 0 ? 5 : 3;
            byte m = cloth[(piece + n) % cloth.Length];
            for (int y = top - 2; y >= top - 1 - drop; y--) f.Voxels.Add((x, y, zz, m));
        }
        foreach (var (px, py, pz, _) in f.Voxels) if (!HasRoom(px, py, pz)) return;
        if (!NobodyIn(Math.Min(At(mid - half, t).x, At(mid + half, t).x), Math.Min(At(mid - half, t).z, At(mid + half, t).z),
            Math.Max(At(mid - half, t).x, At(mid + half, t).x), Math.Max(At(mid - half, t).z, At(mid + half, t).z), hi, top)) return;
        HangFront(f);
    }
}
