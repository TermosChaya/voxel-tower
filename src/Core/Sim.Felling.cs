using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A place to keep out of for a moment: the lane a tree goes down along (from its foot along the way it falls, over all
/// the ground its wood comes down on, a bough's as much as the trunk's, and the strip a log lying across a slope rolls
/// down), or what a dig is about to bring down (a box round it). Those of ours in it walk out of it and wait till it is
/// over.
/// </summary>
public sealed class Danger
{
    /// <summary>A lane: from Back behind (PX, PZ) along (DX, DZ) to Len ahead of it, HalfFoot to either side at the foot
    /// and Half at the far end, and RollLen more to the side its log rolls to (RollSide, +1 toward (−DZ, DX), −1 away).
    /// A box (DX = DZ = 0): Len and Half to either side of (PX, PZ) in x and z.</summary>
    public float PX, PZ, DX, DZ, Len, Half, HalfFoot, Back, RollLen;
    public int RollSide;
    /// <summary>The log of its tree rolling down the slope: the lane is kept while it rolls, from RollFrom, a while at most.</summary>
    public Flier Roller;
    public double RollFrom;
    /// <summary>A tree's lane (not a digger's box): kept no longer than <see cref="Sim.TreeLaneMost"/> since Born, the last time it was in use (a blow at its tree, its fall, its log's roll).</summary>
    public bool Tree;
    public double Born = -1;
    /// <summary>The heights it covers, voxels (a lane covers all).</summary>
    public int Y0 = int.MinValue, Y1 = int.MaxValue;
    public double Until;
    /// <summary>What those keeping out of it say they are doing; the one whose work it is (it keeps to it).</summary>
    public string Why = "";
    public Unit By;
    /// <summary>Kept clear the whole while (a feller's lane, a digger's box): whoever walks into it is sent out again.</summary>
    public bool Kept;

    public bool Has(float x, float y, float z, float margin)
    {
        if (y < Y0 - margin || y > Y1 + margin) return false;
        if (DX == 0 && DZ == 0) return MathF.Abs(x - PX) < Len + margin && MathF.Abs(z - PZ) < Half + margin;
        float ax = x - PX, az = z - PZ, along = ax * DX + az * DZ, side = az * DX - ax * DZ, across = MathF.Abs(side);
        if (along < -Back - margin || along > Len + margin) return false;
        float half = HalfFoot + (Half - HalfFoot) * Math.Clamp(along / MathF.Max(1, Len), 0, 1);
        return across < half + margin || RollLen > 0 && MathF.Sign(side) == RollSide && across < half + RollLen + margin;
    }

    /// <summary>In a lane ahead of its foot, as its trunk sweeps it: where its feller must not stand (it stands at the
    /// tree's back, by the butt; what comes down behind the foot is the low wood, laid down slow).</summary>
    public bool HasAhead(float x, float z, float margin)
    {
        float ax = x - PX, az = z - PZ, along = ax * DX + az * DZ, across = MathF.Abs(ax * DZ - az * DX);
        if (along < -margin || along > Len + margin) return false;
        return across < HalfFoot + (Half - HalfFoot) * Math.Clamp(along / MathF.Max(1, Len), 0, 1) + margin;
    }
}

/// <summary>Which way a tree is to go over, chosen before its last blows: its foot and middle, the way, the lane kept clear.</summary>
public sealed class FellPlan
{
    public float CX, CZ, PX, PZ, DX, DZ;
    public int Base, Top;
    public double Made, Warned = -1;
    /// <summary>It could only come down on something built, or on those of ours who would not leave: it is left standing.</summary>
    public bool Left;
    public string Why = "дерево некуда валить: упадёт на постройку";
    /// <summary>How many times its way was chosen again, those in the lane not having left.</summary>
    public int Tries;
    public Danger Lane;
    /// <summary>Its feller was sent elsewhere by the player, or fell, before it was cut through: nobody holds its lane any more, and a feller coming to it after chooses its way afresh.</summary>
    public bool Abandoned;
    /// <summary>The wood it was made for (packed): a bough cut off first and the trunk after are each their own tree.</summary>
    public HashSet<int> Wood;
}

/// <summary>
/// Felling as woodcutters do it. Before the blow that would bring a tree down (the last of its butt holding it to the
/// ground, found by following its wood), the feller chooses the way it is to go: of eight, the one that comes down on
/// nobody, on nothing built and on no other tree, along the slope rather than across it (a log lying across a slope rolls),
/// with its lean, and away from the feller, who stands at its back. It calls out «Берегись!», those of ours in the lane
/// walk out of it, and only when it is clear do the last blows land. A tree that could only come down on something
/// built is left standing, and the feller says why. A tree that comes down by itself (burnt through, blown up, hacked
/// through by the horde) goes as it leans, missing whoever it can of either side and away from the nearest, and the
/// nearest of ours cries out. What it comes down on it strikes, as hard as a log swung down from its height: so it is
/// worth keeping out of the way.
/// </summary>
public sealed partial class Sim
{
    public readonly List<Danger> Dangers = new();
    readonly List<FellPlan> _fells = new();

    /// <summary>One shouts a word, for the view to show over it and the sound to voice (a feller's «Берегись!»).</summary>
    public Action<Unit, string> Shouted;

    /// <summary>The eight ways, a unit long each.</summary>
    static readonly (float x, float z)[] Ways8 =
    {
        (1, 0), (.7071f, .7071f), (0, 1), (-.7071f, .7071f), (-1, 0), (-.7071f, -.7071f), (0, -1), (.7071f, -.7071f),
    };

    /// <summary>What a tree that would come down is: its middle, the middle of its butt, its foot and top, how far its wood spreads.</summary>
    struct FellBulk
    {
        public float CX, CZ, PX, PZ, Spread, Butt;
        public int Base, Top, Count;
        public HashSet<int> Wood;
    }

    readonly Queue<int> _fellQueue = new();

    /// <summary>
    /// Whether taking out this voxel of a trunk brings down what stands on it: the wood joined to it that, without it,
    /// no longer touches the ground anywhere (as the rules of what holds what up have it, a trunk stands while it touches
    /// the ground). The tree is what would come down.
    /// </summary>
    bool WouldFell(int x, int y, int z, out FellBulk tree)
    {
        tree = default;
        int cut = WaterPlanner.Pack(x, y, z);
        for (int d = 0; d < 6; d++)
        {
            var (nx, ny, nz) = Step6(x, y, z, d);
            if (!World.InBounds(nx, ny, nz) || !Mat.IsTrunk(World.Get(nx, ny, nz))) continue;
            int s = WaterPlanner.Pack(nx, ny, nz);
            if (tree.Wood != null && tree.Wood.Contains(s)) continue;
            var seen = new HashSet<int> { s };
            _fellQueue.Clear();
            _fellQueue.Enqueue(s);
            bool grounded = false;
            // as the rules of what holds what up have it (Sim.Collapse.cs Adrift): what is built and joined to it, held where
            // any of it rests on the ground (timber on water too) or is set into rock from the side
            while (_fellQueue.Count > 0 && !grounded)
            {
                var (px, py, pz) = WaterPlanner.Unpack(_fellQueue.Dequeue());
                byte here = World.Get(px, py, pz);
                for (int e = 0; e < 6; e++)
                {
                    var (qx, qy, qz) = Step6(px, py, pz, e);
                    if (qy < 0) { grounded = true; break; }
                    if (!World.InBounds(qx, qy, qz)) continue;
                    int q = WaterPlanner.Pack(qx, qy, qz);
                    if (q == cut) continue;
                    byte m = World.Get(qx, qy, qz);
                    if (e == 3 ? Ground(m) || m == Mat.Water && Floats(here) : e != 2 && HoldsAside(here, m)) { grounded = true; break; }
                    if (!Bound(m)) continue;
                    if (seen.Add(q)) _fellQueue.Enqueue(q);
                }
                // too much to follow: a tree that big is held, as far as anyone cutting it can tell
                if (seen.Count > 30000) { grounded = true; break; }
            }
            if (grounded || seen.Count < 30) continue;
            // the wood that would come down, measured
            seen.RemoveWhere(p => { var (px, py, pz) = WaterPlanner.Unpack(p); return !Mat.IsTrunk(World.Get(px, py, pz)); });
            if (seen.Count < 30) continue;
            tree = Measure(seen);
            return true;
        }
        return false;
    }

    /// <summary>A voxel partway up a standing trunk: wood under it, and two metres of wood straight over it.</summary>
    bool MidTrunk(int x, int y, int z)
    {
        if (y < 1 || !Mat.IsTrunk(World.Get(x, y - 1, z))) return false;
        for (int d = 1; d <= 2 * VoxelWorld.PerMetre; d++)
            if (!World.InBounds(x, y + d, z) || !Mat.IsTrunk(World.Get(x, y + d, z))) return false;
        return true;
    }

    /// <summary>
    /// Whether a voxel of a trunk is one of the last few of its butt at its height: what is left of the wood across the
    /// trunk there, joined to it, is three voxels or fewer.
    /// </summary>
    bool NearlyCut(int x, int y, int z)
    {
        int left = 0;
        var seen = new HashSet<int> { WaterPlanner.Pack(x, y, z) };
        var q = new Queue<(int x, int z)>();
        q.Enqueue((x, z));
        while (q.Count > 0)
        {
            var (px, pz) = q.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int nx = px + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = pz + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (Math.Abs(nx - x) > 10 || Math.Abs(nz - z) > 10 || !World.InBounds(nx, y, nz) || !Mat.IsTrunk(World.Get(nx, y, nz))) continue;
                if (!seen.Add(WaterPlanner.Pack(nx, y, nz))) continue;
                if (++left > 3) return false;
                q.Enqueue((nx, nz));
            }
        }
        return true;
    }

    /// <summary>The wood standing on a voxel of a butt — joined to what is over it — as a tree that would come down (a tree's worth of it at least).</summary>
    bool TreeAbove(int x, int y, int z, out FellBulk tree)
    {
        tree = default;
        if (!World.InBounds(x, y + 1, z) || !Mat.IsTrunk(World.Get(x, y + 1, z))) return false;
        var seen = new HashSet<int> { WaterPlanner.Pack(x, y + 1, z) };
        _fellQueue.Clear();
        _fellQueue.Enqueue(WaterPlanner.Pack(x, y + 1, z));
        while (_fellQueue.Count > 0 && seen.Count < 30000)
        {
            var (px, py, pz) = WaterPlanner.Unpack(_fellQueue.Dequeue());
            for (int e = 0; e < 6; e++)
            {
                var (qx, qy, qz) = Step6(px, py, pz, e);
                if (qy <= y || !World.InBounds(qx, qy, qz) || !Mat.IsTrunk(World.Get(qx, qy, qz))) continue;
                int qp = WaterPlanner.Pack(qx, qy, qz);
                if (seen.Add(qp)) _fellQueue.Enqueue(qp);
            }
        }
        if (seen.Count < 200) return false;
        tree = Measure(seen);
        return true;
    }

    /// <summary>Wood that would come down, measured: its middle, its foot, how far it spreads, how thick its butt.</summary>
    static FellBulk Measure(HashSet<int> wood)
    {
        float sx = 0, sz = 0;
        int lo = int.MaxValue, hi = int.MinValue;
        foreach (int p in wood) { var (px, py, pz) = WaterPlanner.Unpack(p); sx += px; sz += pz; lo = Math.Min(lo, py); hi = Math.Max(hi, py); }
        float cx = sx / wood.Count, cz = sz / wood.Count, bx = 0, bz = 0, spread = 0, butt = 0;
        int nb = 0;
        foreach (int p in wood)
        {
            var (px, py, pz) = WaterPlanner.Unpack(p);
            spread = MathF.Max(spread, MathF.Max(MathF.Abs(px - cx), MathF.Abs(pz - cz)));
            if (py <= lo + 2) { bx += px; bz += pz; nb++; }
        }
        bx /= Math.Max(1, nb); bz /= Math.Max(1, nb);
        foreach (int p in wood) { var (px, py, pz) = WaterPlanner.Unpack(p); if (py <= lo + 2) butt = MathF.Max(butt, MathF.Max(MathF.Abs(px - bx), MathF.Abs(pz - bz))); }
        return new FellBulk { CX = cx, CZ = cz, PX = bx + .5f, PZ = bz + .5f, Spread = spread, Butt = butt + 1, Base = lo, Top = hi, Count = wood.Count, Wood = wood };
    }

    /// <summary>The lane a tree goes down along one of the ways, over its own wood (see the other <c>LaneOf</c>).</summary>
    Danger LaneOf(in FellBulk b, float dx, float dz) => LaneOf(Unpacked(b.Wood), b.PX, b.PZ, b.Base, b.Top, b.Spread, b.Butt, dx, dz);

    static IEnumerable<(int x, int y, int z)> Unpacked(HashSet<int> wood) { foreach (int p in wood) yield return WaterPlanner.Unpack(p); }

    /// <summary>How much wood two trees share (packed voxels).</summary>
    static int Overlap(HashSet<int> a, HashSet<int> b)
    {
        if (a == null || b == null) return 0;
        if (a.Count > b.Count) (a, b) = (b, a);
        int n = 0;
        foreach (int p in a) if (b.Contains(p)) n++;
        return n;
    }

    /// <summary>
    /// The lane a tree goes down along one of the ways: all the ground its wood comes down on, and more about it — each
    /// piece of it as far along the way as it stood high over the cut, where it lies (<see cref="Lay"/>), so that a bough
    /// reaching out to the side or back comes down there, under where it hung, and one reaching forward past the top; as
    /// long as it stands and its crown beyond at the least, as wide as the crown; and, if it would come to lie across a
    /// slope steep enough to start it rolling, the strip it would roll down. What lies within half a metre of the cut
    /// comes down slow and is left out (its feller stands there).
    /// </summary>
    Danger LaneOf(IEnumerable<(int x, int y, int z)> wood, float px, float pz, int baseY, int top, float spread, float butt, float dx, float dz)
    {
        float m = VoxelWorld.PerMetre, back = 0, ahead = 0, wide = 0;
        var at = new List<(float a, float c)>();
        foreach (var (x, y, z) in wood)
        {
            if (y - baseY < m / 2) continue;
            float ax = x + .5f - px, az = z + .5f - pz, a = ax * dx + az * dz + (y - baseY), c = MathF.Abs(ax * dz - az * dx);
            at.Add((a, c));
            back = MathF.Max(back, -a); ahead = MathF.Max(ahead, a); wide = MathF.Max(wide, c);
        }
        float len = MathF.Max(top - baseY + 3 * m, ahead + 1.5f * m), half = MathF.Max(spread + 2 * m, wide + 1.5f * m), foot = butt + m;
        // narrow at the foot only as far as nothing comes down outside it there
        foreach (var (a, c) in at)
        {
            float t = Math.Clamp(a / len, 0, 1);
            if (t < .9f) foot = MathF.Max(foot, (c + 1.5f * m - half * t) / (1 - t));
        }
        var lane = new Danger { PX = px, PZ = pz, DX = dx, DZ = dz, Len = len, Half = half, HalfFoot = foot, Back = back > 0 ? back + 1.5f * m : 0, Why = "отошёл: валят дерево", Kept = true, Tree = true, Born = Time };
        (lane.RollSide, lane.RollLen) = RollOf(lane, top - baseY, butt);
        return lane;
    }

    /// <summary>
    /// Whether a tree laid down along a lane would lie across a slope steep enough to start its log rolling (as
    /// <see cref="RollFelled"/> has it), to which side (+1 toward (−DZ, DX), −1 away) and how far: down that side from
    /// along its length, metre by metre, till the ground is too flat to keep a log going, and a few metres more.
    /// </summary>
    (int side, float reach) RollOf(Danger lane, int height, float butt)
    {
        float nx = -lane.DZ, nz = lane.DX, off = butt + 4, rise = 0, m = VoxelWorld.PerMetre;
        int n = 0;
        for (float s = height * .25f; s <= height * .75f; s += m)
        {
            int px = (int)(lane.PX + lane.DX * s + nx * off), pz = (int)(lane.PZ + lane.DZ * s + nz * off);
            int qx = (int)(lane.PX + lane.DX * s - nx * off), qz = (int)(lane.PZ + lane.DZ * s - nz * off);
            if (!World.InBounds(px, 2, pz) || !World.InBounds(qx, 2, qz)) continue;
            rise += World.SurfAt(px, pz) - World.SurfAt(qx, qz); n++;
        }
        if (n == 0) return (0, 0);
        float slope = rise / n / (2 * off);
        if (2f / 3 * MathF.Abs(slope) / (1 + slope * slope) < .14f) return (0, 0);
        int side = -MathF.Sign(rise);
        float reach = 0;
        for (float s = height * .25f; s <= height * .75f + 1; s += MathF.Max(1, height * .25f))
        {
            float d = 0, cx = lane.PX + lane.DX * s, cz = lane.PZ + lane.DZ * s;
            while (d < 30 * m)
            {
                int ax = (int)(cx + nx * side * d), az = (int)(cz + nz * side * d), bx = (int)(cx + nx * side * (d + m)), bz = (int)(cz + nz * side * (d + m));
                if (!World.OnLand(ax, az) || !World.OnLand(bx, bz)) break;
                float drop = (World.SurfAt(ax, az) - World.SurfAt(bx, bz)) / m;
                if (2f / 3 * drop / (1 + drop * drop) < .12f) break;
                d += m;
            }
            reach = MathF.Max(reach, d + 4 * m);
        }
        return (side, reach);
    }

    /// <summary>
    /// What it costs to fell a tree along one way: those it would come down on (ours most, and one who cannot get out of
    /// the way most of all), the feller if it is not at the tree's back, what is built or other trees in its sweep, the
    /// land's edge, a slope across it (the log rolls), less with its lean. The worth of what is built in its way apart.
    /// For a tree going over by itself (not <paramref name="careful"/>), only those under it and its lean.
    /// </summary>
    float FellCost(in FellBulk b, float dx, float dz, Unit feller, bool horde, out float built, bool careful = true)
    {
        built = 0;
        var lane = LaneOf(b, dx, dz);
        float cost = 0;
        // those it would come down on
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u == feller || !lane.Has(u.X, u.Y, u.Z, u.T.BodyR + 2)) continue;
            bool stuck = u.Down || u.PinKg > 0 || u.Captor != null;
            if (u.Team == (horde ? Team.Horde : Team.Player)) cost += stuck ? 8000 : 2000;
            else if (u.Team == Team.Wild) cost += 40;
        }
        // with its lean
        float lx = b.CX - b.PX, lz = b.CZ - b.PZ, ll = MathF.Sqrt(lx * lx + lz * lz);
        if (ll > .5f) cost -= 60 * (lx * dx + lz * dz) / ll * MathF.Min(1, ll / 6);
        if (!careful) return cost;
        // the feller: at its back, out of the lane
        if (feller != null)
        {
            float along = (feller.X - b.PX) * dx + (feller.Z - b.PZ) * dz;
            if (lane.HasAhead(feller.X, feller.Z, feller.T.BodyR + 2)) cost += 3000;
            else if (along > -2) cost += 250;
        }
        // the ground it sweeps, sampled every metre: what is built in it, other trees, the edge of the land
        float nx = -dz, nz = dx;
        int height = b.Top - b.Base, trees = 0;
        for (float s = VoxelWorld.PerMetre - lane.Back; s < lane.Len; s += VoxelWorld.PerMetre)
        {
            float half = lane.HalfFoot + (lane.Half - lane.HalfFoot) * Math.Clamp(s / lane.Len, 0, 1);
            // the height the falling tree passes at so far from its foot
            int over = (int)MathF.Sqrt(MathF.Max(0, height * height - s * s)) + 4;
            for (float a = -half; a <= half; a += VoxelWorld.PerMetre)
            {
                int x = (int)(b.PX + dx * s + nx * a), z = (int)(b.PZ + dz * s + nz * a);
                if (!World.OnLand(x, z)) { built += .5f; continue; }
                int g = World.SurfAt(x, z);
                for (int y = Math.Max(1, g - 1); y < Math.Min(World.SY, Math.Max(g, b.Base) + over); y++)
                {
                    byte m = World.Get(x, y, z);
                    if (m == Mat.Air) continue;
                    if (Mat.IsTrunk(m)) { if (!b.Wood.Contains(WaterPlanner.Pack(x, y, z))) { trees++; break; } continue; }
                    // (scaffolding is only poles and boards, put up again as easily)
                    if (m == Mat.Scaffold) { cost += 40; break; }
                    if (Mat.IsPlayer[m]) { built++; break; }
                }
            }
        }
        cost += built * 120 + Math.Min(trees, 20) * 10;
        // a slope across the lane: a log lying across it rolls down it
        float rise = 0, off = b.Butt + 4;
        int n = 0;
        for (float s = height * .25f; s <= height * .75f; s += VoxelWorld.PerMetre)
        {
            int px = (int)(b.PX + dx * s + nx * off), pz = (int)(b.PZ + dz * s + nz * off);
            int qx = (int)(b.PX + dx * s - nx * off), qz = (int)(b.PZ + dz * s - nz * off);
            if (!World.InBounds(px, 2, pz) || !World.InBounds(qx, 2, qz)) continue;
            rise += World.SurfAt(px, pz) - World.SurfAt(qx, qz); n++;
        }
        // (those below it on the side it rolls to are in its lane, counted with those it comes down on)
        if (n > 0)
        {
            float slope = rise / n / (2 * off), push = 2f / 3 * MathF.Abs(slope) / (1 + slope * slope);
            cost += push >= .14f ? 600 : push * 500;
        }
        return cost;
    }

    /// <summary>The way a tree is to go: the cheapest of the eight (see <see cref="FellCost"/>), and what is built in its way.</summary>
    (float dx, float dz) FellWay(in FellBulk b, Unit feller, bool horde, out float built)
    {
        float best = float.MaxValue;
        built = 0;
        (float, float) way = Ways8[0];
        foreach (var (dx, dz) in Ways8)
        {
            float c = FellCost(b, dx, dz, feller, horde, out float bt);
            if (c < best) { best = c; way = (dx, dz); built = bt; }
        }
        return way;
    }

    /// <summary>The plan for a tree about to be brought down: the one made for it already, or a new one.</summary>
    FellPlan PlanFell(in FellBulk b, Unit feller)
    {
        // (the one made for this very wood: a bough cut off first, and the trunk it grew from after, are each their own)
        foreach (var p in _fells)
            if (!p.Abandoned && Time - p.Made < 300 && Overlap(p.Wood, b.Wood) * 10 >= Math.Max(p.Wood?.Count ?? 0, b.Wood.Count) * 6) return p;
        // one made before for much of this wood, the tree having changed under it since (a bough of it cut off, a
        // neighbour's crown gone from it), and not cut through: done with, its lane let go
        for (int i = _fells.Count - 1; i >= 0; i--)
        {
            var p = _fells[i];
            if (p.Wood == null || Overlap(p.Wood, b.Wood) * 10 < p.Wood.Count * 3 || CutThrough(p)) continue;
            Dangers.Remove(p.Lane);
            _fells.RemoveAt(i);
        }
        var (dx, dz) = FellWay(b, feller, feller?.Team == Team.Horde, out float built);
        var plan = new FellPlan { CX = b.CX, CZ = b.CZ, PX = b.PX, PZ = b.PZ, DX = dx, DZ = dz, Base = b.Base, Top = b.Top, Made = Time, Left = built >= 1, Wood = b.Wood };
        plan.Lane = LaneOf(b, dx, dz);
        plan.Lane.Until = Time + 15;
        _fells.Add(plan);
        return plan;
    }

    /// <summary>
    /// Before a blow on a trunk: if it is one of the last that bring the tree down, the way is chosen, the warning called,
    /// those of ours in the lane sent out of it, and the blow held till the lane is clear. True while the feller holds it.
    /// </summary>
    bool FellHold(Unit u, (int x, int y, int z, byte m) j)
    {
        if (!Orders.IsRemove(j.m)) return false;
        if (!Mat.IsTrunk(World.Get(j.x, j.y, j.z))) return DropHold(u, j);
        // (a hold is kept for a moment, so the tree is not weighed afresh every frame; that it is not the last is never
        // kept, as another may have cut the rest of the butt meanwhile)
        int key = WaterPlanner.Pack(j.x, j.y, j.z);
        if (u.FellHold && u.FellLooked == key && Time < u.FellLookedUntil) return true;
        u.FellLooked = key; u.FellLookedUntil = Time + .5;
        u.FellHold = false;
        if (!WouldFell(j.x, j.y, j.z, out var tree) && !(NearlyCut(j.x, j.y, j.z) && TreeAbove(j.x, j.y, j.z, out tree))) return false;
        var plan = PlanFell(tree, u);
        if (plan.Left)
        {
            // nowhere to put it down: the tree stays, its marks taken off it, butt, crown and all
            LeaveTree(tree);
            if (Time - _fellLeftSaid > 20)
            {
                _fellLeftSaid = Time;
                Say(plan.Tries > 0 ? "Дерево не свалить: в любую сторону под ним люди. Его оставили." : "Дерево не свалить: в любую сторону оно упадёт на постройку. Его оставили.", 1, u);
            }
            u.Why = plan.Why;
            u.Unreachable[u.Site] = Time + 20;
            ReleaseSite(u);
            u.FellHold = true;
            return true;
        }
        // (its lane in use while its feller is at it: kept however long the felling takes; only one nobody is at any more
        // goes after TreeLaneMost)
        plan.Lane.Born = Time;
        if (!Dangers.Contains(plan.Lane)) Dangers.Add(plan.Lane);
        // (a tree it held before is no longer its work: its lane, kept as long as it would be, is kept from it as from
        // anyone, so that it does not stand under that tree to cut another)
        if (plan.Lane.By != u)
            foreach (var p in _fells)
                if (p != plan && p.Lane.By == u) p.Lane.By = null;
        plan.Lane.By = u;
        plan.Lane.Until = Math.Max(plan.Lane.Until, Time + 6);
        // the feller in the lane after all (it came round to the front): it leaves this side to another
        if (plan.Lane.HasAhead(u.X, u.Z, u.T.BodyR))
        {
            u.Why = "не с той стороны: дерево упадёт сюда";
            u.Unreachable[u.Site] = Time + 20;
            ReleaseSite(u);
            u.FellHold = true;
            return true;
        }
        if (plan.Warned < 0)
        {
            plan.Warned = Time;
            // a bough is let down without a word; a tree, or a great limb, with one
            if (tree.Count >= 200) Shouted?.Invoke(u, "Берегись!");
        }
        // (never while one of ours is under it: one who cannot get out of the way in half a minute is left there, and
        // the way chosen again later, round them)
        int stay = ClearLane(plan.Lane, u);
        if (stay == 0) return false;
        u.Why = "ждёт, пока отойдут от дерева";
        u.FellHold = true;
        if (Time - plan.Warned > 30)
        {
            // the way chosen again, round them; after three times the tree is left
            Dangers.Remove(plan.Lane);
            plan.Warned = -1;
            if (++plan.Tries >= 3) { plan.Left = true; plan.Why = "дерево некуда валить: под ним люди"; }
            else
            {
                var (dx, dz) = FellWay(tree, u, u.Team == Team.Horde, out float built);
                plan.DX = dx; plan.DZ = dz; plan.Left = built >= 1;
                plan.Lane = LaneOf(tree, dx, dz);
                plan.Lane.Until = Time + 15;
            }
        }
        return true;
    }

    double _fellLeftSaid = -99;

    /// <summary>A tree left standing: the marks taken off all of it, the butt under the cut and the crown about it.</summary>
    void LeaveTree(in FellBulk tree)
    {
        int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
        foreach (int p in tree.Wood) { var (x, _, z) = WaterPlanner.Unpack(p); x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z); }
        x0 = Math.Max(0, x0 - 24); z0 = Math.Max(0, z0 - 24); x1 = Math.Min(World.SX - 1, x1 + 24); z1 = Math.Min(World.SZ - 1, z1 + 24);
        int y0 = Math.Max(1, tree.Base - 24), y1 = Math.Min(World.SY - 1, tree.Top + 24);
        for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (Orders.At(x, y, z) == 0) continue;
                    byte m = World.Get(x, y, z);
                    if (Mat.IsTrunk(m) || Mat.IsLeafy(m)) Orders.Clear(x, y, z);
                }
    }

    /// <summary>Sends those of ours in a danger out of it (all but the one whose work it is); how many are still in it.</summary>
    int ClearLane(Danger d, Unit worker)
    {
        int stay = 0;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o == worker || o.Team != Team.Player || o.Airborne != null) continue;
            if (!d.Has(o.X, o.Y, o.Z, o.T.BodyR + 2))
            {
                // one about to walk into it (on the way out of another's lane, or to work): it goes round, and the tree
                // waits for it
                if (o.Path == null || o.PathI >= o.Path.Count || o == Possessed) continue;
                bool into = false;
                for (int i = o.PathI; i < o.Path.Count && i < o.PathI + 4 && !into; i++) { var (px, py, pz) = Nav.Feet(o.Path[i]); into = d.Has(px, py, pz, o.T.BodyR); }
                if (!into) continue;
                stay++;
                o.Path = null; o.NextSearch = 0;
                continue;
            }
            stay++;
            if (o == Possessed || o.Down || o.PinKg > 0 || o.Captor != null || o.Carrying != null) continue;
            if (o.Avoiding != d) { o.Avoiding = d; o.Path = null; o.NextSearch = 0; }
        }
        return stay;
    }

    /// <summary>
    /// One of ours keeping out of a danger: out of it by the nearest way, and waiting outside it, turned to it, till it is
    /// over. False when there is nothing to keep out of.
    /// </summary>
    bool KeepOut(Unit u, float dt)
    {
        var d = u.Avoiding;
        if (d == null) return false;
        // (what it said of the danger goes with it, so that nobody stands over «отошёл: валят дерево» after)
        if (Time > d.Until || !Dangers.Contains(d)) { u.Avoiding = null; if (u.Why == d.Why) u.Why = ""; return false; }
        float r = u.T.BodyR + 4;
        if (!d.Has(u.X, u.Y, u.Z, r))
        {
            // out of it, and the alarm called: to shelter or the posts (whoever's way lies into the lane is turned again by
            // ClearLane), not waiting here till the tree is down
            if (Alarm) { u.Avoiding = null; if (u.Why == d.Why) u.Why = ""; return false; }
            u.Path = null;
            u.State = UnitState.Idle;
            u.Why = d.Why;
            float fx = d.DX == 0 && d.DZ == 0 ? d.PX : d.PX + d.DX * d.Len * .5f, fz = d.DX == 0 && d.DZ == 0 ? d.PZ : d.PZ + d.DZ * d.Len * .5f;
            u.Facing = MathF.Atan2(fz - u.Z, fx - u.X);
            return true;
        }
        if (u.Path == null || u.PathI >= u.Path.Count)
        {
            if (Time < u.NextSearch) { u.State = UnitState.Idle; return true; }
            u.NextSearch = Time + .5;
            if (u.Site >= 0) ReleaseSite(u);
            StopCarve(u); StopBuild(u);
            // (out of it to where no tree's lane is, and round them on the way: in a wood felled by many, the way out of
            // one lane is not to cross another as its tree comes down)
            bool lanes = false;
            foreach (var o in Dangers) if (o != d && (o.DX != 0 || o.DZ != 0)) { lanes = true; break; }
            bool Other(int c) { var (px, py, pz) = Nav.Feet(c); foreach (var o in Dangers) if (o != d && (o.DX != 0 || o.DZ != 0) && o.Has(px, py, pz, r)) return true; return false; }
            var path = Paths.Find(u.Cell, c => { var (px, py, pz) = Nav.Feet(c); return !d.Has(px, py, pz, r + 2) && (!lanes || !Other(c)); }, c => 0, MoverOf(u), 2500, gates: true,
                lane: lanes ? c => Other(c) ? 12 : 0 : null);
            path ??= Paths.Find(u.Cell, c => { var (px, py, pz) = Nav.Feet(c); return !d.Has(px, py, pz, r + 2); }, c => 0, MoverOf(u), 2500, gates: true);
            if (path == null) { u.State = UnitState.Idle; return true; }
            u.Path = path; u.PathI = 0;
        }
        Walk(u, dt, 1.3f);
        u.State = UnitState.Flee;
        u.Why = d.Why;
        return true;
    }

    /// <summary>
    /// A tree going over: its lane kept clear till it is down, those of ours who walked into it sent out again; one that
    /// nobody felled (burnt through, blown up, cut loose by another's fall) gets a lane of its own, and the nearest of ours
    /// cries out.
    /// </summary>
    void TreeGoing(Toppling t, FellPlan plan, List<(int x, int y, int z, byte m, byte order)> pieces, int baseY)
    {
        Danger lane;
        if (plan != null) lane = plan.Lane;
        else
        {
            float spread = 0;
            int top = baseY;
            var wood = new List<(int x, int y, int z)>();
            foreach (var (x, y, z, m, _) in pieces)
            {
                if (!Mat.IsTrunk(m)) continue;
                wood.Add((x, y, z));
                top = Math.Max(top, y); spread = MathF.Max(spread, MathF.Max(MathF.Abs(x - t.PX), MathF.Abs(z - t.PZ)));
            }
            lane = LaneOf(wood, t.PX, t.PZ, baseY, top, spread, 3, t.DX, t.DZ);
            lane.Kept = false;
        }
        t.Lane = lane;
        // (its feller's work done: it keeps out of it as all of ours do, and does not walk back in to its next work)
        lane.By = null;
        lane.Until = t.To + 1;
        lane.Born = Time;
        if (!Dangers.Contains(lane)) Dangers.Add(lane);
        if (ClearLane(lane, null) == 0 || plan != null) return;
        Unit near = null;
        float nd = 30 * VoxelWorld.PerMetre;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Team != Team.Player) continue;
            float d = MathF.Abs(u.X - t.PX) + MathF.Abs(u.Z - t.PZ);
            if (d < nd) { nd = d; near = u; }
        }
        if (near != null) Shouted?.Invoke(near, "Берегись!");
    }

    /// <summary>How long a rolling log keeps its lane at most, seconds; how long any tree's lane is kept at most.</summary>
    public const double RollLaneMost = 20, TreeLaneMost = 120;

    /// <summary>The dangers over are let go; the plans of trees long since down forgotten.</summary>
    void TickDangers()
    {
        // a tree cut through but held a while (its crown caught in another's): its lane is kept clear till it comes down;
        // one whose feller was sent elsewhere by the player, or fell, before it was cut through is let go, and its lane
        // goes a few seconds after the last blow held, so that nobody stands waiting by a tree that nobody will fell (and
        // is there when it comes down by itself)
        foreach (var p in _fells)
        {
            if (p.Warned < 0 || p.Left || Time - p.Warned >= 60 || !Dangers.Contains(p.Lane)) continue;
            bool cut = CutThrough(p);
            if (!cut && p.Lane.By is { Dead: true } or { Hold: true }) p.Abandoned = true;
            if (cut || !p.Abandoned) p.Lane.Until = Math.Max(p.Lane.Until, Time + 1);
        }
        // a felled tree's log rolling down a slope: its lane kept till it stops, and no longer than a log rolls (one
        // caught rocking on the slope would otherwise hold those keeping out of it for good)
        foreach (var d in Dangers)
            if (d.Roller != null)
            {
                if (d.Roller.Rolling && Fliers.Contains(d.Roller) && Time - d.RollFrom < RollLaneMost) d.Until = Math.Max(d.Until, Time + 1);
                else d.Roller = null;
            }
        // and no tree's lane is kept longer than any felling takes, whatever kept it (a tree's way chosen thrice over
        // and its lane held a while each time is well inside it)
        if (Dangers.Count > 0) Dangers.RemoveAll(d => Time > d.Until || d.Tree && d.Born >= 0 && Time - d.Born > TreeLaneMost);
        if (_fells.Count > 0) _fells.RemoveAll(p => Time - p.Made > 300);
        // whoever of ours walks into one while it lasts is sent out again
        foreach (var d in Dangers) if (d.Kept) ClearLane(d, d.By);
        TickUndercut();
    }

    /// <summary>Its butt cut through: its lowest wood resting on nothing (held only by its crown caught in another's).</summary>
    bool CutThrough(FellPlan p)
    {
        int px = (int)p.PX, pz = (int)p.PZ, wood = 0;
        for (int z = pz - 8; z <= pz + 8; z++)
            for (int x = px - 8; x <= px + 8; x++)
            {
                if (!World.InBounds(x, p.Base, z) || !Mat.IsTrunk(World.Get(x, p.Base, z))) continue;
                wood++;
                byte under = World.Get(x, p.Base - 1, z);
                if (under != Mat.Air && !Mat.Passable[under] && !Mat.IsLeafy(under)) return false;
            }
        return wood > 0;
    }

    /// <summary>
    /// The way a tree coming down goes: the way chosen for it by its feller, if there was one (the plan is done with);
    /// else as it leans, missing whoever it can and away from the nearest: no one cut it as it should be cut.
    /// </summary>
    (float dx, float dz) FallWay(float tx, float tz, int baseY, int top, List<(int x, int y, int z, byte m, byte order)> pieces, out FellPlan plan)
    {
        plan = null;
        // its foot: the middle of its lowest wood
        float fx = 0, fz = 0;
        int nf = 0;
        foreach (var (x, y, z, m, _) in pieces) if (Mat.IsTrunk(m) && y <= baseY + 2) { fx += x; fz += z; nf++; }
        if (nf > 0) { fx /= nf; fz /= nf; } else { fx = tx; fz = tz; }
        // the plan made for the wood that falls: most of that wood in it (its roots, if it came down with them, are more
        // than the plan was made for), and most of the plan's wood in it (not a bough's plan for the trunk it grew on);
        // and its lane kept clear now (not one let go, which nobody keeps out of any more)
        int trunks = 0, most = 0;
        foreach (var (_, _, _, m, _) in pieces) if (Mat.IsTrunk(m)) trunks++;
        foreach (var p in _fells)
        {
            if (p.Wood == null || p.Wood.Count == 0 || !Dangers.Contains(p.Lane)) continue;
            int n = 0;
            foreach (var (x, y, z, m, _) in pieces) if (Mat.IsTrunk(m) && p.Wood.Contains(WaterPlanner.Pack(x, y, z))) n++;
            if (n > most && n * 10 >= p.Wood.Count * 6 && n * 10 >= trunks * 4) { most = n; plan = p; }
        }
        if (plan != null) { _fells.Remove(plan); return (plan.DX, plan.DZ); }
        var wood = new HashSet<int>();
        float spread = 0, bx = 0, bz = 0, butt = 0;
        int nb = 0;
        foreach (var (x, y, z, m, _) in pieces)
        {
            if (!Mat.IsTrunk(m)) continue;
            wood.Add(WaterPlanner.Pack(x, y, z));
            spread = MathF.Max(spread, MathF.Max(MathF.Abs(x - tx), MathF.Abs(z - tz)));
            if (y <= baseY + 2) { bx += x; bz += z; nb++; }
        }
        bx = nb > 0 ? bx / nb : tx; bz = nb > 0 ? bz / nb : tz;
        foreach (var (x, y, z, m, _) in pieces) if (Mat.IsTrunk(m) && y <= baseY + 2) butt = MathF.Max(butt, MathF.Max(MathF.Abs(x - bx), MathF.Abs(z - bz)));
        var b = new FellBulk { CX = tx, CZ = tz, PX = bx + .5f, PZ = bz + .5f, Spread = spread, Butt = butt + 1, Base = baseY, Top = top, Count = wood.Count, Wood = wood };
        // nobody cutting it: as it leans, missing whoever it can, and away from the nearest
        Unit near = null;
        float nd = 12 * VoxelWorld.PerMetre;
        foreach (var u in Units) { if (u.Dead || u.Inside) continue; float d = MathF.Abs(u.X - b.PX) + MathF.Abs(u.Z - b.PZ); if (d < nd) { nd = d; near = u; } }
        // (nobody near: any way at all)
        if (near == null) return Rng.Next(4) switch { 0 => (1f, 0f), 1 => (-1f, 0f), 2 => (0f, 1f), _ => (0f, -1f) };
        float ax = b.PX - near.X, az = b.PZ - near.Z, al = MathF.Max(1, MathF.Sqrt(ax * ax + az * az));
        float best = float.MaxValue;
        (float, float) way = Ways8[0];
        foreach (var (dx, dz) in Ways8)
        {
            float c = FellCost(b, dx, dz, null, false, out _, careful: false) + FellCost(b, dx, dz, null, true, out _, careful: false) - 100 * (dx * ax + dz * az) / al;
            if (c < best) { best = c; way = (dx, dz); }
        }
        return way;
    }
}
