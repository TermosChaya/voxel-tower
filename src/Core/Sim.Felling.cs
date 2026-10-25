using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A place to keep out of for a moment: the lane a tree goes down along (from its foot along the way it falls, as long
/// as it is tall, narrow at the butt and as wide as its crown at the top end), or what a dig is about to bring down (a
/// box round it). Those of ours in it walk out of it and wait till it is over.
/// </summary>
public sealed class Danger
{
    /// <summary>A lane: from (PX, PZ) along (DX, DZ) Len long, HalfFoot to either side at the foot and Half at the far end.
    /// A box (DX = DZ = 0): Len and Half to either side of (PX, PZ) in x and z.</summary>
    public float PX, PZ, DX, DZ, Len, Half, HalfFoot;
    /// <summary>The heights it covers, voxels (a lane covers all).</summary>
    public int Y0 = int.MinValue, Y1 = int.MaxValue;
    public double Until;
    /// <summary>What those keeping out of it say they are doing; the one whose work it is (it keeps to it).</summary>
    public string Why = "";
    public Unit By;

    public bool Has(float x, float y, float z, float margin)
    {
        if (y < Y0 - margin || y > Y1 + margin) return false;
        if (DX == 0 && DZ == 0) return MathF.Abs(x - PX) < Len + margin && MathF.Abs(z - PZ) < Half + margin;
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
    /// <summary>It could only come down on something built: it is left standing.</summary>
    public bool Left;
    public Danger Lane;
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

    /// <summary>The lane a tree goes down along one of the ways: as long as it stands and its crown beyond, as wide as the crown.</summary>
    static Danger LaneOf(float px, float pz, int baseY, int top, float spread, float butt, float dx, float dz) => new()
    {
        PX = px, PZ = pz, DX = dx, DZ = dz, Len = top - baseY + 3 * VoxelWorld.PerMetre,
        Half = spread + 2 * VoxelWorld.PerMetre, HalfFoot = butt + VoxelWorld.PerMetre, Why = "отошёл: валят дерево",
    };

    /// <summary>
    /// What it costs to fell a tree along one way: those it would come down on (ours most, and one who cannot get out of
    /// the way most of all), the feller if it is not at the tree's back, what is built or other trees in its sweep, the
    /// land's edge, a slope across it (the log rolls), less with its lean. The worth of what is built in its way apart.
    /// For a tree going over by itself (not <paramref name="careful"/>), only those under it and its lean.
    /// </summary>
    float FellCost(in FellBulk b, float dx, float dz, Unit feller, bool horde, out float built, bool careful = true)
    {
        built = 0;
        var lane = LaneOf(b.PX, b.PZ, b.Base, b.Top, b.Spread, b.Butt, dx, dz);
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
            if (lane.Has(feller.X, feller.Y, feller.Z, feller.T.BodyR + 2)) cost += 3000;
            else if (along > -2) cost += 250;
        }
        // the ground it sweeps, sampled every metre: what is built in it, other trees, the edge of the land
        float nx = -dz, nz = dx;
        int height = b.Top - b.Base, trees = 0;
        for (float s = VoxelWorld.PerMetre; s < lane.Len; s += VoxelWorld.PerMetre)
        {
            float half = lane.HalfFoot + (lane.Half - lane.HalfFoot) * s / lane.Len;
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
                    if (Mat.IsPlayer[m] || m == Mat.Scaffold) { built++; break; }
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
        if (n > 0)
        {
            float slope = rise / n / (2 * off), push = 2f / 3 * MathF.Abs(slope) / (1 + slope * slope);
            cost += push >= .14f ? 600 : push * 500;
            // and those below it, on the side it rolls to, as if it came down on them
            if (push >= .14f)
                foreach (var u in Units)
                {
                    if (u.Dead || u.Inside || u == feller || u.Team != (horde ? Team.Horde : Team.Player)) continue;
                    float ax = u.X - b.PX, az = u.Z - b.PZ, along = ax * dx + az * dz, side = ax * nx + az * nz;
                    if (along > -4 && along < lane.Len && MathF.Sign(side) == -MathF.Sign(rise) && MathF.Abs(side) < 30 * VoxelWorld.PerMetre) cost += 1500;
                }
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
        foreach (var p in _fells)
            if (MathF.Abs(p.PX - b.PX) < 10 && MathF.Abs(p.PZ - b.PZ) < 10 && Math.Abs(p.Base - b.Base) < 16 && Time - p.Made < 300) return p;
        var (dx, dz) = FellWay(b, feller, feller?.Team == Team.Horde, out float built);
        var plan = new FellPlan { CX = b.CX, CZ = b.CZ, PX = b.PX, PZ = b.PZ, DX = dx, DZ = dz, Base = b.Base, Top = b.Top, Made = Time, Left = built >= 1 };
        plan.Lane = LaneOf(b.PX, b.PZ, b.Base, b.Top, b.Spread, b.Butt, dx, dz);
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
            // nowhere to put it down: the tree stays, its marks taken off it, crown and all
            var whole = Shapes.Tree(World, j.x, j.y, j.z);
            for (int i = 0; i < whole.Count; i++) if (Orders.At(whole.X(i), whole.Y(i), whole.Z(i)) != 0) Orders.Clear(whole.X(i), whole.Y(i), whole.Z(i));
            if (Time - _fellLeftSaid > 20) { _fellLeftSaid = Time; Say("Дерево не свалить: в любую сторону оно упадёт на постройку. Его оставили.", 1, u); }
            u.Why = "дерево некуда валить: упадёт на постройку";
            u.FellHold = true;
            return true;
        }
        if (!Dangers.Contains(plan.Lane)) Dangers.Add(plan.Lane);
        plan.Lane.By = u;
        plan.Lane.Until = Math.Max(plan.Lane.Until, Time + 6);
        // the feller in the lane after all (it came round to the front): it leaves this side to another
        if (plan.Lane.Has(u.X, u.Y, u.Z, u.T.BodyR))
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
        if (stay > 0)
        {
            u.Why = "ждёт, пока отойдут от дерева";
            u.FellHold = true;
            if (Time - plan.Warned > 30)
            {
                _fells.Remove(plan);
                Dangers.Remove(plan.Lane);
                u.Unreachable[u.Site] = Time + 20;
                ReleaseSite(u);
            }
            return true;
        }
        return false;
    }

    double _fellLeftSaid = -99;

    /// <summary>Sends those of ours in a danger out of it (all but the one whose work it is); how many are still in it.</summary>
    int ClearLane(Danger d, Unit worker)
    {
        int stay = 0;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o == worker || o.Team != Team.Player || o.Airborne != null) continue;
            if (!d.Has(o.X, o.Y, o.Z, o.T.BodyR + 2)) continue;
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
        if (Time > d.Until || !Dangers.Contains(d)) { u.Avoiding = null; return false; }
        float r = u.T.BodyR + 4;
        if (!d.Has(u.X, u.Y, u.Z, r))
        {
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
            var path = Paths.Find(u.Cell, c => { var (px, py, pz) = Nav.Feet(c); return !d.Has(px, py, pz, r + 2); }, c => 0, MoverOf(u), 2500, gates: true);
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
            foreach (var (x, y, z, m, _) in pieces) { if (!Mat.IsTrunk(m)) continue; top = Math.Max(top, y); spread = MathF.Max(spread, MathF.Max(MathF.Abs(x - t.PX), MathF.Abs(z - t.PZ))); }
            lane = LaneOf(t.PX, t.PZ, baseY, top, spread, 3, t.DX, t.DZ);
        }
        lane.Until = t.To + 1;
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

    /// <summary>The dangers over are let go; the plans of trees long since down forgotten.</summary>
    void TickDangers()
    {
        // a tree cut through but held a while (its crown caught in another's): its lane is kept clear till it comes down
        foreach (var p in _fells) if (p.Warned >= 0 && !p.Left && Time - p.Warned < 60 && Dangers.Contains(p.Lane)) p.Lane.Until = Math.Max(p.Lane.Until, Time + 1);
        if (Dangers.Count > 0) Dangers.RemoveAll(d => Time > d.Until);
        if (_fells.Count > 0) _fells.RemoveAll(p => Time - p.Made > 300);
        // whoever of ours walks into one while it lasts is sent out again
        foreach (var d in Dangers) ClearLane(d, d.By);
        TickUndercut();
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
        // (its roots, if it came down with them, reach lower than the cut its plan was made at: matched by the foot and the top)
        foreach (var p in _fells)
            if (MathF.Abs(p.PX - fx - .5f) < 12 && MathF.Abs(p.PZ - fz - .5f) < 12 && Math.Abs(p.Top - top) < 24) { plan = p; break; }
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
        float ax = near == null ? 0 : b.PX - near.X, az = near == null ? 0 : b.PZ - near.Z, al = MathF.Max(1, MathF.Sqrt(ax * ax + az * az));
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
