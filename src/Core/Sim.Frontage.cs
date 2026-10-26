using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A town building shows its trade from outside (Sim.Town.cs). Once it works, a sign is put up on the wall beside its
/// door, painted with the mark of its trade, and a torch the other side of the door; a building with a hearth has a
/// stone chimney up the outside of a gable end, standing over the ridge, and smoke comes out of it; and the things of
/// its trade stand before it on either side of the door: the smith's anvil on its stump and his quenching tub, the
/// market's stalls under striped awnings, the stables' paddock, the range's butts, the hall's banner and its bell. Like
/// the walls' dressing (Sim.Decor.cs) it costs nothing and is in nobody's way: it hangs on the building's walls and goes
/// when they do, whatever is built where it stands takes its place, and what is gone is set out again a while after,
/// once there is room for it and no alarm.
/// </summary>
public sealed partial class Sim
{
    double _nextFrontage;

    /// <summary>How far either way of the door's middle its leaves swing, thickness and all: nothing low is set out nearer.</summary>
    const int Doorway = 12;

    /// <summary>The kinds of building with a hearth inside: they have a chimney.</summary>
    static readonly HashSet<string> ChimneyKinds = new() { "hall", "smithy", "lab", "infirmary", "barracks", "knights", "mine" };

    /// <summary>Every few seconds, while no alarm is up: what of the working buildings' frontage is missing is set out again.</summary>
    void TickFrontage()
    {
        if (Time < _nextFrontage || Buildings.Count == 0) return;
        _nextFrontage = Time + 5;
        if (Alarm) return;
        DressTown();
    }

    /// <summary>What of every working building's frontage is missing set out now (for the scenes staged and the tests).</summary>
    public void DressTown()
    {
        foreach (var b in Buildings) if (b.Ready) DressFront(b);
    }

    /// <summary>A building's door, if one is hung in its walls.</summary>
    public Gateway DoorOf(Building b)
    {
        foreach (var g in Gates)
        {
            var (mx, _, mz) = g.Middle;
            if (mx >= b.X0 - 2 && mx <= b.X1 + 2 && mz >= b.Z0 - 2 && mz <= b.Z1 + 2) return g;
        }
        return null;
    }

    /// <summary>A building's door: the middle of its threshold on the wall's outer face, and the way it looks out. False with no door hung.</summary>
    public bool FrontOf(Building b, out int x, out int y, out int z, out int nx, out int nz)
    {
        x = y = z = nx = nz = 0;
        if (DoorOf(b) is not { } g) return false;
        var (mx, my, mz) = g.Middle;
        if (g.AlongX) { nz = mz < b.CZ ? -1 : 1; z = nz < 0 ? b.Z0 : b.Z1; x = (int)MathF.Floor(mx); }
        else { nx = mx < b.CX ? -1 : 1; x = nx < 0 ? b.X0 : b.X1; z = (int)MathF.Floor(mz); }
        y = (int)my;
        return true;
    }

    /// <summary>The pieces of a building's frontage standing now (its door's lamp among the torches).</summary>
    public List<Fixture> FrontageOf(Building b)
    {
        var list = new List<Fixture>();
        foreach (var f in Fixtures)
        {
            if (f.Kind is not (FixtureKind.Sign or FixtureKind.Chimney or FixtureKind.Trade or FixtureKind.Torch)) continue;
            var (ax, _, az) = WaterPlanner.Unpack(f.Anchor);
            if (ax >= b.X0 && ax <= b.X1 && az >= b.Z0 && az <= b.Z1) list.Add(f);
        }
        return list;
    }

    /// <summary>Sets out what of a building's frontage is missing and has room for it now.</summary>
    void DressFront(Building b)
    {
        if (!FrontOf(b, out int fx, out int fy, out int fz, out int nx, out int nz)) return;
        // along the front, so that the door's middle is 0: its reach either way
        int ax = -nz, az = nx;
        int ua = nx != 0 ? (b.Z0 - fz) * az : (b.X0 - fx) * ax, ub = nx != 0 ? (b.Z1 - fz) * az : (b.X1 - fx) * ax;
        int umin = Math.Min(ua, ub), umax = Math.Max(ua, ub);
        // where the leaves of the doors about it swing
        var swing = new HashSet<int>();
        foreach (var g in Gates)
        {
            var (mx, _, mz) = g.Middle;
            if (mx < b.X0 - 40 || mx > b.X1 + 40 || mz < b.Z0 - 40 || mz > b.Z1 + 40) continue;
            for (int st = 0; st <= Gateway.StepOf(1); st++)
                for (int i = 0; i < Gateway.Voxels; i++) { var (x, y, z) = g.Place(i, st); swing.Add(WaterPlanner.Pack(x, y, z)); }
        }
        var have = FrontageOf(b);
        foreach (var kit in KitsOf(b.Kind)) SetOut(kit, fx, fy, fz, ax, az, nx, nz, umin, umax, swing);
        if (ChimneyKinds.Contains(b.Kind)) SetOutChimney(b, fy, nx, nz, have.Find(f => f.Kind == FixtureKind.Chimney));
    }

    /// <summary>A piece of the frontage hung: its voxels laid, kept with the rest of the dressing.</summary>
    void HangFront(Fixture f)
    {
        foreach (var (x, y, z, m) in f.Voxels) World.Set(x, y, z, m);
        Fixtures.Add(f);
        FixtureAdded?.Invoke(f);
    }

    /// <summary>The voxel can take a piece of dressing: air or a sprig of grass, nothing built, marked or set out over the column.</summary>
    bool HasRoom(int x, int y, int z)
    {
        if (!World.InBounds(x, y, z) || Site.Forbidden(x, z) || InYard(x, z)) return false;
        byte m = World.Get(x, y, z);
        return (m == Mat.Air || Mat.IsSprig(m)) && Orders.At(x, y, z) == 0 && MeantAt(x, y, z) == 0;
    }

    /// <summary>The ground a piece stands on: the first free voxel over the valley's own ground and any paving laid in it (not over a roof's eaves).</summary>
    int FrontGround(int x, int z)
    {
        int y = World.GroundTop(x, z);
        while (y < World.SY - 1 && Mat.IsPavement[World.Get(x, y, z)]) y++;
        return y;
    }

    /// <summary>Nobody standing within a box of columns, at its height.</summary>
    bool NobodyIn(int x0, int z0, int x1, int z1, int y0, int y1)
    {
        foreach (var u in Units)
            if (!u.Dead && !u.Inside && u.X > x0 - 2 && u.X < x1 + 3 && u.Z > z0 - 2 && u.Z < z1 + 3 && u.Y > y0 - 20 && u.Y < y1 + 2) return false;
        return true;
    }

    /// <summary>
    /// A piece set out before the front: over the highest ground under it, what rests on the ground let down to it
    /// wherever it lies lower, so long as the ground is about level; hung on the wall by the threshold's height if it
    /// hangs. Where the door is not in the middle of its wall and a piece would go past the end of the front, it is
    /// moved in toward the door, never into the doorway. Nothing of it when any of it has no room, or is where a door's
    /// leaves swing, or the wall it hangs on does not stand.
    /// </summary>
    void SetOut(Kit kit, int fx, int fy, int fz, int ax, int az, int nx, int nz, int umin, int umax, HashSet<int> swing)
    {
        int klo = int.MaxValue, khi = int.MinValue, under = int.MaxValue;
        foreach (var ((u, y, _), _) in kit.V) { klo = Math.Min(klo, u); khi = Math.Max(khi, u); under = Math.Min(under, y); }
        int shift = khi > umax ? umax - khi : klo < umin ? umin - klo : 0;
        if (klo + shift < umin || khi + shift > umax || under < 24 && klo + shift < Doorway && khi + shift > -Doorway)
        {
            if (kit.Else != null) SetOut(kit.Else, fx, fy, fz, ax, az, nx, nz, umin, umax, swing);
            return;
        }
        (int x, int z) At(int u, int t) => (fx + ax * (u + shift) + nx * t, fz + az * (u + shift) + nz * t);
        // the columns it stands over, and the lowest of it over each
        var low = new Dictionary<(int u, int t), int>();
        foreach (var ((u, y, t), _) in kit.V)
            if (!low.TryGetValue((u, t), out int was) || y < was) low[(u, t)] = y;
        int lo = int.MaxValue, hi = int.MinValue;
        var ground = new Dictionary<(int u, int t), int>();
        int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
        foreach (var (c, _) in low)
        {
            var (x, z) = At(c.u, c.t);
            if (!World.InBounds(x, 0, z)) return;
            int g = FrontGround(x, z);
            if (World.Get(x, g - 1, z) is Mat.Water or Mat.Acid or Mat.Pitch or Mat.Lava) return;
            ground[c] = g;
            lo = Math.Min(lo, g); hi = Math.Max(hi, g);
            x0 = Math.Min(x0, x); z0 = Math.Min(z0, z); x1 = Math.Max(x1, x); z1 = Math.Max(z1, z);
        }
        if (!kit.Hung && hi - lo > 3) return;
        int baseY = kit.Hung ? fy : hi;
        // the wall it hangs on, at the spot it names or the nearest of the front to it outside the doorway; a rough
        // stone face may be set back a voxel
        int au = Math.Clamp((kit.AU ?? kit.MidU) + shift, umin + 2, umax - 2) - shift;
        if (Math.Abs(au + shift) < Doorway && under < 24) au = (au < 0 ? -Doorway : Doorway) - shift;
        int ay = (kit.Hung ? fy : baseY) + (kit.AY ?? 4);
        var (wx, wz) = At(au, 0);
        if (!Built(wx, ay, wz)) { (wx, wz) = At(au, -1); if (!Built(wx, ay, wz)) return; }
        var f = new Fixture { Kind = kit.Kind, Anchor = WaterPlanner.Pack(wx, ay, wz) };
        int top = baseY;
        foreach (var ((u, y, t), m) in kit.V)
        {
            var (x, z) = At(u, t);
            int yy = baseY + y;
            if (!HasRoom(x, yy, z) || swing.Contains(WaterPlanner.Pack(x, yy, z))) return;
            f.Voxels.Add((x, yy, z, m));
            top = Math.Max(top, yy);
            // what rests on the ground, let down to it
            if (kit.Hung || y != 0 || low[(u, t)] != 0) continue;
            for (int k = ground[(u, t)]; k < baseY; k++)
            {
                if (!HasRoom(x, k, z)) return;
                f.Voxels.Add((x, k, z, m));
            }
        }
        // (what hangs over a man's head may hang over anyone)
        if (under < 15 && !NobodyIn(x0, z0, x1, z1, baseY, top)) return;
        if (kit.Light is var (lu, ly, lt))
        {
            var (lx, lz) = At(lu, lt);
            (f.LX, f.LY, f.LZ) = (lx + .5f + nx * .2f, baseY + ly + 1.4f, lz + .5f + nz * .2f);
        }
        f.Voxels.Sort((p, q) => p.y != q.y ? p.y.CompareTo(q.y) : p.z != q.z ? p.z.CompareTo(q.z) : p.x.CompareTo(q.x));
        HangFront(f);
    }

    /// <summary>
    /// The chimney of a building with a hearth: a stack of stone against the outside of a gable end in its middle, a
    /// little over the ridge, open at the top, a breast of it filling in to the wall under the roof's edge; at the end
    /// away from the door, or the other when that has no room. Its smoke comes out at the top (<see cref="Fixture.LX"/>).
    /// One that stands is left be, unless a storey put up since leaves it under the ridge: then it is built up again.
    /// </summary>
    void SetOutChimney(Building b, int fy, int doorNx, int doorNz, Fixture standing)
    {
        bool alongX = b.X1 - b.X0 >= b.Z1 - b.Z0;
        int c = alongX ? (b.Z0 + b.Z1) / 2 : (b.X0 + b.X1) / 2;
        for (int end = 1; end >= 0; end--)
        {
            int s = end == 1 ? 1 : -1, face = alongX ? (end == 1 ? b.X1 : b.X0) : (end == 1 ? b.Z1 : b.Z0);
            // (not at the end the door is in)
            if (alongX ? doorNx == s : doorNz == s) continue;
            (int x, int z) At(int along, int across) => alongX ? (face + s * along, c + across) : (c + across, face + s * along);
            // the ridge over the middle of the gable
            var (rx, rz) = At(0, 0);
            int ridge = -1;
            for (int y = fy; y < Math.Min(World.SY - 8, fy + 140); y++)
            {
                byte m = World.Get(rx, y, rz);
                if (m != Mat.Air && (Mat.IsPlayer[m] || MeantAt(rx, y, rz) != 0)) ridge = y;
            }
            if (ridge < fy + 16) continue;
            if (standing != null)
            {
                if (standing.LY > ridge + 2) return;
                TakeDown(Fixtures.IndexOf(standing));
                standing = null;
            }
            for (int s0 = 1; s0 <= 3; s0++)
            {
                int lo = int.MaxValue, hi = int.MinValue;
                bool wet = false;
                for (int a = s0; a < s0 + 5; a++)
                    for (int k = -2; k <= 2; k++)
                    {
                        var (x, z) = At(a, k);
                        int g = FrontGround(x, z);
                        lo = Math.Min(lo, g); hi = Math.Max(hi, g);
                        wet |= World.Get(x, g - 1, z) is Mat.Water or Mat.Acid or Mat.Pitch or Mat.Lava;
                    }
                if (wet || hi - lo > 3) break;
                int top = ridge + 5;
                var (wx, wz) = At(0, 0);
                int ay = hi + 4;
                if (!Built(wx, ay, wz)) { (wx, wz) = At(-1, 0); if (!Built(wx, ay, wz)) break; }
                var f = new Fixture { Kind = FixtureKind.Chimney, Anchor = WaterPlanner.Pack(wx, ay, wz) };
                bool room = true;
                for (int y = lo; y <= top && room; y++)
                    for (int a = s0; a < s0 + 5 && room; a++)
                        for (int k = -2; k <= 2; k++)
                        {
                            bool rim = a == s0 || a == s0 + 4 || k == -2 || k == 2;
                            // the flue open at the top, the rim of it blackened
                            if (y >= top - 1 && !rim) continue;
                            var (x, z) = At(a, k);
                            if (y < FrontGround(x, z)) continue;
                            if (!HasRoom(x, y, z)) { room = false; break; }
                            f.Voxels.Add((x, y, z, y == top ? Mat.Hoop : Mat.Flue));
                        }
                if (!room) continue;
                // the breast: from the stack in to the wall, as high as the roof's edge leaves room
                for (int a = 1; a < s0; a++)
                    for (int k = -2; k <= 2; k++)
                    {
                        var (x, z) = At(a, k);
                        for (int y = FrontGround(x, z); y < top && HasRoom(x, y, z); y++) f.Voxels.Add((x, y, z, Mat.Flue));
                    }
                var (sx, sz) = At(s0 + 2, 0);
                if (!NobodyIn(Math.Min(sx, wx) - 3, Math.Min(sz, wz) - 3, Math.Max(sx, wx) + 3, Math.Max(sz, wz) + 3, lo, top)) return;
                (f.LX, f.LY, f.LZ) = (sx + .5f, top + 1, sz + .5f);
                HangFront(f);
                return;
            }
        }
    }

    // ------------------------------------------------------------ what each trade sets out

    /// <summary>
    /// A piece of a frontage as it is drawn: voxels along the front (u, from the door's middle), up (y, over the ground,
    /// or over the threshold for what hangs on the wall) and out from the wall (t, from 1). The wall it hangs on is named
    /// by AU and AY, or found nearest to it.
    /// </summary>
    sealed class Kit
    {
        public FixtureKind Kind = FixtureKind.Trade;
        public bool Hung;
        public int? AU, AY;
        /// <summary>Where it goes instead when the front has no room for it where it is drawn (not when something stands there).</summary>
        public Kit Else;
        /// <summary>Where its flame burns, for a lamp.</summary>
        public (int u, int y, int t)? Light;
        /// <summary>
        /// The places by it where one of ours passes the time at its trade (Sim.Streets.cs): where one stands, the way
        /// one faces from there (along u and out t), and what one does.
        /// </summary>
        public readonly List<(float u, float t, float du, float dt, Pastime does)> Stands = new();
        public readonly Dictionary<(int u, int y, int t), byte> V = new();
        public void Box(int u0, int y0, int t0, int u1, int y1, int t1, byte m)
        {
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                for (int t = Math.Min(t0, t1); t <= Math.Max(t0, t1); t++)
                    for (int u = Math.Min(u0, u1); u <= Math.Max(u0, u1); u++) V[(u, y, t)] = m;
        }
        public void Dot(int u, int y, int t, byte m) => V[(u, y, t)] = m;
        public void Post(int u, int t, int h, byte m) => Box(u, 0, t, u, h - 1, t, m);
        public void Cut(int u0, int y0, int t0, int u1, int y1, int t1)
        {
            for (int y = y0; y <= y1; y++) for (int t = t0; t <= t1; t++) for (int u = u0; u <= u1; u++) V.Remove((u, y, t));
        }
        public int MidU
        {
            get { int lo = int.MaxValue, hi = int.MinValue; foreach (var ((u, _, _), _) in V) { lo = Math.Min(lo, u); hi = Math.Max(hi, u); } return (lo + hi) / 2; }
        }
        /// <summary>The same the other side of the door.</summary>
        public Kit Mirror()
        {
            var k = new Kit { Kind = Kind, Hung = Hung, AU = -AU, AY = AY };
            foreach (var ((u, y, t), m) in V) k.V[(-u, y, t)] = m;
            foreach (var (u, t, du, dt, does) in Stands) k.Stands.Add((-u, t, -du, dt, does));
            return k;
        }
    }

    static readonly Dictionary<string, Kit[]> _kits = new();

    static Kit[] KitsOf(string kind)
    {
        if (_kits.TryGetValue(kind, out var kits)) return kits;
        var list = new List<Kit> { Sign(kind), DoorLamp() };
        list.AddRange(Trade(kind));
        return _kits[kind] = list.ToArray();
    }

    /// <summary>The mark painted on each trade's sign, five by five, its top row first; the board's colour and the paint's.</summary>
    static (byte board, byte ink, string[] mark) SignOf(string kind) => kind switch
    {
        "hall" => (Mat.Banner, Mat.BannerTrim, new[] { ".....", "#.#.#", "#####", "#####", "....." }),
        "market" => (Mat.Signboard, Mat.BannerTrim, new[] { ".###.", "#####", "##.##", "#####", ".###." }),
        "smithy" => (Mat.Signboard, Mat.Hoop, new[] { ".....", "#####", ".###.", "..#..", ".###." }),
        "carpentry" => (Mat.Signboard, Mat.Hoop, new[] { ".##..", "####.", ".##..", "..#..", "..#.." }),
        "masons" => (Mat.Signboard, Mat.Hoop, new[] { "#####", "#####", "..#..", "..#..", "..#.." }),
        "drill" => (Mat.Signboard, Mat.Hoop, new[] { "#...#", ".#.#.", "..#..", ".#.#.", "#...#" }),
        "infirmary" => (Mat.Canvas, Mat.Paint, new[] { "..#..", "..#..", "#####", "..#..", "..#.." }),
        "traps" => (Mat.Signboard, Mat.Hoop, new[] { "#.#.#", "#####", ".....", "#####", "#.#.#" }),
        "barracks" => (Mat.Banner, Mat.BannerTrim, new[] { "..#..", ".###.", "..#..", "..#..", "..#.." }),
        "knights" => (Mat.Paint, Mat.Canvas, new[] { "#####", "#####", "#####", ".###.", "..#.." }),
        "range" => (Mat.Bale, Mat.Paint, new[] { ".###.", "#...#", "#.#.#", "#...#", ".###." }),
        "lab" => (Mat.Signboard, Mat.Brew, new[] { "..#..", "..#..", ".###.", "#####", "#####" }),
        "kennel" => (Mat.Signboard, Mat.BonePile, new[] { ".....", "#...#", ".###.", "#...#", "....." }),
        "stables" => (Mat.Signboard, Mat.Hoop, new[] { ".###.", "#...#", "#...#", "#...#", "....." }),
        "trolls" => (Mat.CrateEdge, Mat.BonePile, new[] { "#####", "#.#.#", "#####", ".#.#.", "....." }),
        "giants" => (Mat.Signboard, Mat.Hewn, new[] { ".##..", "###..", ".###.", "..##.", "...#." }),
        "workshop" => (Mat.Signboard, Mat.Hoop, new[] { ".#.#.", "#####", "##.##", "#####", ".#.#." }),
        "mine" => (Mat.Signboard, Mat.Hoop, new[] { "##.##", ".#.#.", "..#..", ".#.#.", "#...#" }),
        _ => (Mat.Signboard, Mat.Hoop, new[] { ".....", ".....", "..#..", ".....", "....." }),
    };

    /// <summary>
    /// The sign: a board in a dark frame put up flat on the wall beside the door, a man's height off the ground, the mark
    /// of the trade painted in the middle of it; on the door's other side when this one has no wall enough.
    /// </summary>
    static Kit Sign(string kind)
    {
        var k = new Kit { Kind = FixtureKind.Sign, Hung = true, AU = 16, AY = 16 };
        var (board, ink, mark) = SignOf(kind);
        for (int y = 12; y <= 20; y++)
            for (int u = 12; u <= 20; u++)
            {
                bool edge = y == 12 || y == 20 || u == 12 || u == 20;
                bool inMark = u >= 14 && u <= 18 && y >= 14 && y <= 18;
                k.Dot(u, y, 1, edge ? Mat.CrateEdge : inMark && mark[18 - y][u - 14] == '#' ? ink : board);
            }
        // the iron pins it hangs by
        k.Dot(13, 21, 1, Mat.Hoop); k.Dot(19, 21, 1, Mat.Hoop);
        var other = new Kit { Kind = FixtureKind.Sign, Hung = true, AU = -16, AY = 16 };
        foreach (var ((u, y, t), m) in k.V) other.V[(u - 32, y, t)] = m;
        k.Else = other;
        return k;
    }

    /// <summary>A torch in a sconce by the door, on the side the sign is not: the door lit at night.</summary>
    static Kit DoorLamp()
    {
        var k = new Kit { Kind = FixtureKind.Torch, Hung = true, AU = -13, AY = 17, Light = (-13, 18, 1) };
        k.Dot(-13, 17, 1, Mat.Sconce); k.Dot(-13, 18, 1, Mat.Sconce); k.Dot(-13, 19, 1, Mat.Flame);
        var other = new Kit { Kind = FixtureKind.Torch, Hung = true, AU = 13, AY = 17, Light = (13, 18, 1) };
        other.Dot(13, 17, 1, Mat.Sconce); other.Dot(13, 18, 1, Mat.Sconce); other.Dot(13, 19, 1, Mat.Flame);
        k.Else = other;
        return k;
    }

    // a few things more than one trade sets out

    /// <summary>A barrel: a ring of staves with two iron hoops, its top closed or, for a tub, water standing in it.</summary>
    static void Barrel(Kit k, int u0, int t0, int h, bool tub = false)
    {
        for (int y = 0; y < h; y++)
            for (int t = t0; t < t0 + 4; t++)
                for (int u = u0; u < u0 + 4; u++)
                {
                    bool corner = (u == u0 || u == u0 + 3) && (t == t0 || t == t0 + 3);
                    if (corner) continue;
                    bool inner = u > u0 && u < u0 + 3 && t > t0 && t < t0 + 3;
                    k.Dot(u, y, t, tub && inner && y == h - 1 ? Mat.Banner : y == 1 || y == h - 2 ? Mat.Hoop : Mat.Cask);
                }
    }

    /// <summary>A crate of boards with darker edges, s on a side.</summary>
    static void Crate(Kit k, int u0, int y0, int t0, int s)
    {
        for (int y = 0; y < s; y++)
            for (int t = 0; t < s; t++)
                for (int u = 0; u < s; u++)
                {
                    int edges = (u == 0 || u == s - 1 ? 1 : 0) + (t == 0 || t == s - 1 ? 1 : 0) + (y == 0 || y == s - 1 ? 1 : 0);
                    k.Dot(u0 + u, y0 + y, t0 + t, edges >= 2 ? Mat.CrateEdge : Mat.Crate);
                }
    }

    /// <summary>A rack of spears against the wall: two posts and two rails, the spears stood up in it, their heads of iron.</summary>
    static Kit SpearRack(int u0, int u1)
    {
        var k = new Kit();
        k.Post(u0, 3, 12, Mat.CrateEdge); k.Post(u1, 3, 12, Mat.CrateEdge);
        k.Box(u0, 3, 3, u1, 3, 3, Mat.CrateEdge); k.Box(u0, 10, 3, u1, 10, 3, Mat.CrateEdge);
        for (int u = u0 + 2; u < u1; u += 2) { k.Box(u, 1, 4, u, 13, 4, Mat.CrateEdge); k.Box(u, 14, 4, u, 15, 4, Mat.Hoop); }
        return k;
    }

    /// <summary>A banner on a pole, hung from an arm toward the door's side, in the plane of the front: a field, a band and a mark.</summary>
    static Kit BannerPole(int u, int t, byte field, byte trim)
    {
        var k = new Kit();
        k.Post(u, t, 31, Mat.CrateEdge);
        k.Dot(u, 31, t, Mat.BannerTrim);
        int d = u > 0 ? 1 : -1;
        k.Box(u + d, 28, t, u + 5 * d, 28, t, Mat.CrateEdge);
        for (int y = 16; y <= 27; y++)
            for (int i = 1; i <= 5; i++)
            {
                // swallow-tailed at the foot
                if (y == 16 && i == 3) continue;
                k.Dot(u + i * d, y, t, y == 26 || y == 21 && i == 3 ? trim : field);
            }
        return k;
    }

    /// <summary>A butt of straw on two legs, its face to the front, a red ring and a red middle painted on it.</summary>
    static Kit Butt(int cu, int t)
    {
        var k = new Kit();
        k.Post(cu - 3, t + 1, 8, Mat.CrateEdge); k.Post(cu + 3, t + 1, 8, Mat.CrateEdge);
        for (int dy = -3; dy <= 3; dy++)
            for (int du = -3; du <= 3; du++)
            {
                float r2 = du * du + dy * dy;
                if (r2 > 12.5f) continue;
                float r = MathF.Sqrt(r2);
                k.Dot(cu + du, 9 + dy, t, Math.Abs(r - 2) < .6f || r < .8f ? Mat.Paint : Mat.Bale);
            }
        return k;
    }

    /// <summary>The things of a trade set out before its building, on either side of the door (the right side drawn with u over 0).</summary>
    static IEnumerable<Kit> Trade(string kind)
    {
        Kit k;
        switch (kind)
        {
            case "hall":
                // the town's banner by the door, the bell in its frame, the board where notices are pinned
                yield return BannerPole(-14, 3, Mat.Banner, Mat.BannerTrim);
                k = new Kit();
                k.Post(21, 8, 19, Mat.CrateEdge); k.Post(27, 8, 19, Mat.CrateEdge);
                k.Box(21, 19, 8, 27, 19, 8, Mat.CrateEdge);
                k.Box(20, 20, 7, 28, 20, 9, Mat.CrateEdge);
                k.Dot(24, 18, 8, Mat.Hoop); k.Dot(24, 17, 8, Mat.BannerTrim);
                k.Box(23, 14, 7, 25, 16, 9, Mat.BannerTrim);
                k.Box(22, 13, 6, 26, 13, 10, Mat.BannerTrim);
                k.Dot(24, 12, 8, Mat.Hoop);
                yield return k;
                k = new Kit();
                k.Post(-27, 6, 14, Mat.CrateEdge); k.Post(-21, 6, 14, Mat.CrateEdge);
                k.Box(-26, 6, 6, -22, 13, 6, Mat.Signboard);
                k.Box(-25, 10, 6, -24, 12, 6, Mat.Canvas); k.Box(-23, 7, 6, -22, 9, 6, Mat.Canvas); k.Dot(-26, 8, 6, Mat.Canvas);
                // one reading what is pinned up
                k.Stands.Add((-23.5f, 10.5f, 0, -1, Pastime.Read));
                yield return k;
                break;
            case "market":
                // two stalls under striped awnings, goods on their counters
                k = new Kit();
                k.Post(-24, 4, 17, Mat.CrateEdge); k.Post(-15, 4, 17, Mat.CrateEdge);
                k.Post(-24, 10, 14, Mat.CrateEdge); k.Post(-15, 10, 14, Mat.CrateEdge);
                k.Box(-23, 0, 10, -16, 5, 10, Mat.Signboard);
                k.Box(-24, 6, 8, -15, 6, 10, Mat.Signboard);
                for (int t = 3; t <= 11; t++)
                    for (int u = -25; u <= -14; u++) k.Dot(u, 17 - (t - 3) / 2, t, (u + 25) / 2 % 2 == 0 ? Mat.Paint : Mat.Canvas);
                for (int u = -25; u <= -14; u += 2) k.Dot(u, 12, 11, (u + 25) / 2 % 2 == 0 ? Mat.Paint : Mat.Canvas);
                // the stallholder behind the counter, two buying before it
                k.Stands.Add((-19.5f, 6, 0, 1, Pastime.Sell));
                k.Stands.Add((-22, 14.5f, .3f, -1, Pastime.Shop)); k.Stands.Add((-17, 14.5f, -.3f, -1, Pastime.Shop));
                var other = new Kit();
                foreach (var (p, m) in k.V) other.V[p] = m;
                other.Stands.AddRange(k.Stands);
                k.Box(-23, 7, 8, -21, 8, 9, Mat.Crate); k.Box(-20, 7, 8, -19, 9, 9, Mat.Sack); k.Box(-18, 7, 9, -17, 8, 9, Mat.Paint);
                yield return k;
                // the other stall, its goods cloth and brass
                other.Box(-23, 7, 8, -22, 7, 9, Mat.BannerTrim); other.Box(-21, 7, 8, -18, 8, 9, Mat.Canvas); other.Dot(-17, 7, 9, Mat.Paint);
                yield return other.Mirror();
                break;
            case "smithy":
                // the anvil on its stump, the quenching tub; the coal heap, the tongs hung on the wall
                k = new Kit();
                k.Box(15, 0, 6, 17, 2, 8, Mat.Cask);
                k.Box(15, 3, 7, 17, 3, 7, Mat.Hoop);
                k.Box(14, 4, 6, 18, 5, 8, Mat.Hoop);
                k.Dot(13, 5, 7, Mat.Hoop);
                // the smith at it, hammering
                k.Stands.Add((16.5f, 11, 0, -1, Pastime.Hammer));
                yield return k;
                k = new Kit();
                Barrel(k, 21, 5, 4, tub: true);
                yield return k;
                k = new Kit();
                k.Box(-25, 0, 3, -16, 0, 8, Mat.Hoop); k.Box(-24, 1, 4, -17, 1, 7, Mat.Hoop); k.Box(-23, 2, 5, -18, 2, 6, Mat.Hoop);
                yield return k;
                k = new Kit { Hung = true, AU = -20, AY = 13 };
                k.Box(-25, 13, 1, -15, 13, 1, Mat.CrateEdge);
                foreach (int u in new[] { -24, -21, -18 }) k.Box(u, 10, 1, u, 12, 1, Mat.Hoop);
                yield return k;
                break;
            case "carpentry":
                // a log on two trestles, ready for the saw; planks stacked to season; logs piled
                k = new Kit();
                foreach (int u in new[] { 15, 23 }) { k.Box(u, 0, 6, u, 4, 6, Mat.CrateEdge); k.Box(u, 0, 9, u, 4, 9, Mat.CrateEdge); k.Box(u, 5, 6, u, 5, 9, Mat.CrateEdge); }
                k.Box(13, 6, 7, 25, 7, 8, Mat.Cask);
                k.Box(13, 6, 7, 13, 7, 8, Mat.Crate); k.Box(25, 6, 7, 25, 7, 8, Mat.Crate);
                // a sawyer at the log
                k.Stands.Add((19.5f, 11.5f, 0, -1, Pastime.Saw));
                yield return k;
                k = new Kit();
                for (int y = 0; y <= 4; y++) k.Box(-26, y, 3, -15, y, 7, y % 2 == 0 ? Mat.Signboard : Mat.Crate);
                yield return k;
                k = new Kit();
                k.Box(-26, 0, 10, -16, 1, 11, Mat.Cask); k.Box(-26, 0, 12, -16, 1, 13, Mat.Cask); k.Box(-26, 2, 11, -16, 3, 12, Mat.Cask);
                k.Box(-16, 0, 10, -16, 3, 13, Mat.Crate);
                k.Cut(-16, 2, 10, -16, 3, 10); k.Cut(-16, 2, 13, -16, 3, 13);
                yield return k;
                break;
            case "masons":
                // dressed blocks stacked, one on the banker being worked, the chips swept up
                k = new Kit();
                k.Box(13, 0, 4, 16, 3, 7, Mat.Hewn); k.Box(18, 0, 4, 21, 3, 7, Mat.Hewn); k.Box(15, 4, 4, 19, 7, 7, Mat.Hewn);
                yield return k;
                k = new Kit();
                k.Box(23, 0, 5, 26, 3, 8, Mat.Hewn);
                yield return k;
                k = new Kit();
                foreach (var (u, t) in new[] { (-25, 5), (-25, 9), (-16, 5), (-16, 9) }) k.Post(u, t, 5, Mat.CrateEdge);
                k.Box(-26, 5, 5, -15, 5, 9, Mat.Signboard);
                k.Box(-23, 6, 6, -19, 9, 8, Mat.Hewn);
                k.Box(-17, 6, 7, -17, 6, 9, Mat.CrateEdge); k.Box(-18, 6, 9, -16, 7, 9, Mat.CrateEdge);
                // a mason dressing the block on the banker
                k.Stands.Add((-20.5f, 12.5f, 0, -1, Pastime.Hammer));
                yield return k;
                k = new Kit();
                k.Box(-27, 0, 12, -22, 0, 15, Mat.Hewn); k.Box(-26, 1, 13, -23, 1, 14, Mat.Hewn);
                yield return k;
                break;
            case "drill":
                // two dummies of sacking on posts to strike at; a rack of spears
                foreach (var (u, t) in new[] { (15, 10), (23, 12) })
                {
                    k = new Kit();
                    k.Post(u, t, 13, Mat.CrateEdge);
                    k.Box(u - 3, 10, t, u + 3, 10, t, Mat.CrateEdge);
                    k.Box(u - 1, 4, t - 1, u + 1, 9, t + 1, Mat.Sack);
                    k.Box(u - 1, 11, t - 1, u + 1, 13, t + 1, Mat.Sack);
                    // a soldier striking at it
                    k.Stands.Add((u + .5f, t + 5, 0, -1, Pastime.Spar));
                    yield return k;
                }
                yield return SpearRack(-26, -15);
                break;
            case "infirmary":
                // a white banner with the red cross; beds of herbs; water in a barrel
                k = new Kit();
                k.Post(-14, 3, 28, Mat.CrateEdge);
                k.Box(-15, 26, 3, -19, 26, 3, Mat.CrateEdge);
                k.Box(-15, 14, 3, -19, 25, 3, Mat.Canvas);
                k.Box(-17, 16, 3, -17, 23, 3, Mat.Paint); k.Box(-16, 20, 3, -18, 20, 3, Mat.Paint);
                yield return k;
                k = new Kit();
                k.Box(-27, 0, 4, -15, 1, 10, Mat.CrateEdge);
                k.Cut(-26, 0, 5, -16, 1, 9);
                for (int t = 5; t <= 9; t++)
                    for (int u = -26; u <= -16; u++)
                    {
                        if ((u + t) % 2 == 0) k.Dot(u, 0, t, (u * 7 + t * 3) % 3 == 0 ? Mat.Bloom : Mat.Flower);
                    }
                yield return k;
                k = new Kit();
                Barrel(k, 23, 5, 5);
                yield return k;
                break;
            case "traps":
                // a cage for what is caught; a net hung out to mend; crates of irons
                k = new Kit();
                k.Box(14, 0, 5, 21, 0, 11, Mat.CrateEdge);
                for (int t = 5; t <= 11; t++)
                    for (int u = 14; u <= 21; u++)
                        if ((u == 14 || u == 21 || t == 5 || t == 11) && (u + t) % 2 == 0) k.Box(u, 1, t, u, 7, t, Mat.CrateEdge);
                k.Box(14, 8, 5, 21, 8, 11, Mat.CrateEdge);
                yield return k;
                k = new Kit();
                k.Post(-26, 3, 15, Mat.CrateEdge); k.Post(-15, 3, 15, Mat.CrateEdge);
                k.Box(-26, 14, 3, -15, 14, 3, Mat.CrateEdge);
                for (int y = 4; y <= 13; y++) for (int u = -25; u <= -16; u++) if ((u + y) % 2 == 0) k.Dot(u, y, 3, Mat.Sack);
                yield return k;
                k = new Kit();
                Crate(k, -25, 0, 8, 4); Crate(k, -20, 0, 9, 3);
                yield return k;
                break;
            case "barracks":
                // spears in a rack, shields hung on a rail
                yield return SpearRack(-26, -15);
                k = new Kit();
                k.Post(14, 3, 12, Mat.CrateEdge); k.Post(26, 3, 12, Mat.CrateEdge);
                k.Box(14, 10, 3, 26, 10, 3, Mat.CrateEdge);
                int n = 0;
                foreach (int cu in new[] { 16, 20, 24 })
                {
                    byte field = n++ % 2 == 0 ? Mat.Banner : Mat.Paint;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int du = -2; du <= 2; du++)
                            if (du * du + dy * dy <= 5) k.Dot(cu + du, 7 + dy, 4, du == 0 && dy == 0 ? Mat.BannerTrim : field);
                }
                yield return k;
                break;
            case "knights":
                // the quintain to ride at, pennants on their poles
                k = new Kit();
                k.Post(20, 10, 15, Mat.CrateEdge);
                k.Box(15, 13, 10, 25, 13, 10, Mat.CrateEdge);
                k.Box(14, 9, 10, 16, 12, 10, Mat.Paint); k.Dot(15, 11, 10, Mat.BannerTrim);
                k.Box(24, 9, 9, 26, 12, 11, Mat.Sack);
                yield return k;
                foreach (int pu in new[] { -14, -22 })
                {
                    k = new Kit();
                    k.Post(pu, 4, 24, Mat.CrateEdge);
                    for (int y = 19; y <= 22; y++)
                        for (int i = 1; i <= y - 17; i++) k.Dot(pu - i, y, 4, y % 2 == 0 ? Mat.Paint : Mat.Canvas);
                    yield return k;
                }
                break;
            case "range":
                // two butts out in front; a stand of arrows by the wall
                yield return Butt(-20, 15);
                yield return Butt(20, 15);
                k = new Kit();
                k.Post(13, 2, 8, Mat.CrateEdge); k.Post(19, 2, 8, Mat.CrateEdge);
                k.Box(13, 7, 2, 19, 7, 2, Mat.CrateEdge);
                for (int u = 14; u <= 18; u++) { k.Box(u, 1, 3, u, 9, 3, Mat.CrateEdge); k.Dot(u, 10, 3, Mat.Canvas); }
                yield return k;
                break;
            case "lab":
                // shelves of green glass; a cauldron that glows; barrels of what it is made of
                k = new Kit();
                k.Post(-13, 2, 15, Mat.CrateEdge); k.Post(-25, 2, 15, Mat.CrateEdge);
                k.Box(-13, 5, 2, -25, 5, 3, Mat.Signboard); k.Box(-13, 10, 2, -25, 10, 3, Mat.Signboard);
                for (int u = -14; u >= -24; u -= 2) { k.Dot(u, 6, 2, Mat.Brew); if (u % 4 == 0) k.Dot(u, 7, 2, Mat.Brew); }
                for (int u = -15; u >= -23; u -= 2) { k.Dot(u, 11, 2, Mat.Brew); if (u % 4 == -1) k.Dot(u, 12, 2, Mat.Brew); }
                yield return k;
                k = new Kit();
                k.Box(17, 1, 7, 23, 3, 11, Mat.Hoop);
                k.Box(18, 3, 8, 22, 3, 10, Mat.Brew);
                foreach (var (u, t) in new[] { (17, 7), (23, 7), (17, 11), (23, 11) }) k.Dot(u, 0, t, Mat.Hoop);
                yield return k;
                k = new Kit();
                Barrel(k, 24, 3, 5);
                yield return k;
                break;
            case "kennel":
                // two doghouses either side, a bone gnawed
                k = new Kit();
                k.Box(14, 0, 5, 20, 4, 10, Mat.Crate);
                k.Cut(16, 0, 10, 18, 2, 10);
                k.Box(13, 5, 4, 21, 5, 11, Mat.CrateEdge); k.Box(14, 6, 4, 20, 6, 11, Mat.CrateEdge); k.Box(16, 7, 4, 18, 7, 11, Mat.CrateEdge);
                yield return k;
                yield return k.Mirror();
                k = new Kit();
                k.Box(23, 0, 13, 25, 0, 13, Mat.BonePile);
                foreach (var (u, t) in new[] { (23, 12), (23, 14), (25, 12), (25, 14) }) k.Dot(u, 0, t, Mat.BonePile);
                yield return k;
                break;
            case "stables":
                // a paddock railed off, hay in bales, a trough of water
                k = new Kit();
                for (int u = 13; u <= 33; u++)
                    for (int t = 3; t <= 21; t++)
                    {
                        if (u != 13 && u != 33 && t != 3 && t != 21) continue;
                        // the way in, on the side toward the door
                        if (u == 13 && t >= 9 && t <= 14) continue;
                        bool post = (u - 13) % 4 == 0 && (t == 3 || t == 21) || (t - 3) % 4 == 0 && (u == 13 || u == 33) || t == 21 && (u == 13 || u == 33);
                        if (post) k.Post(u, t, 9, Mat.CrateEdge);
                        else { k.Dot(u, 4, t, Mat.CrateEdge); k.Dot(u, 8, t, Mat.CrateEdge); }
                    }
                k.Post(13, 8, 9, Mat.CrateEdge); k.Post(13, 15, 9, Mat.CrateEdge);
                yield return k;
                k = new Kit();
                k.Box(-27, 0, 4, -22, 3, 7, Mat.Bale); k.Box(-20, 0, 4, -15, 3, 7, Mat.Bale); k.Box(-24, 4, 4, -18, 7, 7, Mat.Bale);
                yield return k;
                k = new Kit();
                k.Box(-27, 0, 11, -16, 2, 13, Mat.CrateEdge);
                k.Box(-26, 2, 12, -17, 2, 12, Mat.Banner);
                yield return k;
                break;
            case "trolls":
                // a heap of gnawed bones; boulders rolled up for seats
                k = new Kit();
                k.Box(-26, 0, 4, -17, 0, 10, Mat.BonePile); k.Box(-25, 1, 5, -18, 1, 9, Mat.BonePile); k.Box(-23, 2, 6, -20, 2, 8, Mat.BonePile);
                foreach (var (u, t) in new[] { (-25, 4), (-19, 9), (-22, 7) }) k.Dot(u, u == -22 ? 2 : 0, t, Mat.Hewn);
                yield return k;
                foreach (var (u0, t0, s, h) in new[] { (14, 5, 5, 4), (21, 4, 6, 5) })
                {
                    k = new Kit();
                    for (int y = 0; y < h; y++)
                        for (int t = 0; t < s; t++)
                            for (int u = 0; u < s; u++)
                            {
                                float du = u - (s - 1) / 2f, dt = t - (s - 1) / 2f, dy = y - (h - 1) * .35f;
                                if (du * du + dt * dt + dy * dy * 1.6f <= s * s * .3f) k.Dot(u0 + u, y, t0 + t, Mat.Hewn);
                            }
                    yield return k;
                }
                break;
            case "giants":
                // two standing stones flanking the door; a club as long as a man is tall, three times, leant on the wall
                k = new Kit();
                k.Box(23, 0, 3, 26, 23, 6, Mat.Hewn); k.Cut(23, 23, 3, 23, 23, 6); k.Cut(26, 22, 3, 26, 23, 6);
                yield return k;
                yield return k.Mirror();
                k = new Kit();
                for (int y = 0; y <= 24; y++)
                {
                    int t = 8 - y / 4;
                    if (y < 17) k.Box(-18, y, t, -16, y, t + 2, Mat.Cask);
                    else { k.Box(-19, y, t - 1, -15, y, t + 3, Mat.Cask); if (y % 3 == 0) { k.Dot(-19, y, t + 1, Mat.Hoop); k.Dot(-15, y, t + 1, Mat.Hoop); } }
                }
                yield return k;
                break;
            case "workshop":
                // a crane with a dressed block hanging from it; a cart wheel against the wall; timbers stacked
                k = new Kit();
                k.Post(26, 4, 27, Mat.CrateEdge); k.Post(26, 9, 27, Mat.CrateEdge);
                k.Box(26, 27, 3, 26, 28, 18, Mat.CrateEdge);
                k.Box(26, 16, 16, 26, 26, 16, Mat.Sack);
                k.Dot(26, 15, 16, Mat.Hoop);
                k.Box(25, 11, 15, 27, 14, 17, Mat.Hewn);
                yield return k;
                k = new Kit();
                for (int dy = -5; dy <= 5; dy++)
                    for (int du = -5; du <= 5; du++)
                    {
                        float r = MathF.Sqrt(du * du + dy * dy);
                        if (r < 1.2f) k.Dot(-20 + du, 5 + dy, 2, Mat.Hoop);
                        else if (Math.Abs(r - 4.5f) < .8f || (du == 0 || dy == 0) && r < 4) k.Dot(-20 + du, 5 + dy, 2, Mat.CrateEdge);
                    }
                yield return k;
                k = new Kit();
                k.Box(-32, 0, 9, -14, 1, 10, Mat.Cask); k.Box(-32, 0, 12, -14, 1, 13, Mat.Cask); k.Box(-32, 2, 10, -14, 3, 12, Mat.Cask);
                yield return k;
                break;
            case "mine":
                // a heap of the ore brought up; a cart of it on its four wheels
                k = new Kit();
                k.Box(12, 0, 3, 19, 0, 10, Mat.OreHeap); k.Box(13, 1, 4, 18, 1, 9, Mat.OreHeap); k.Box(14, 2, 5, 17, 2, 8, Mat.OreHeap); k.Box(15, 3, 6, 16, 3, 7, Mat.OreHeap);
                yield return k;
                k = new Kit();
                k.Box(-19, 2, 4, -13, 5, 8, Mat.Crate);
                k.Box(-18, 5, 5, -14, 6, 7, Mat.OreHeap);
                foreach (var (u, t) in new[] { (-18, 3), (-15, 3), (-18, 9), (-15, 9) }) k.Box(u, 0, t, u + 1, 1, t, Mat.Hoop);
                k.Box(-12, 4, 6, -11, 4, 6, Mat.CrateEdge);
                yield return k;
                break;
        }
    }
}
