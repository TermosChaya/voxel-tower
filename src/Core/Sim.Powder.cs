using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Powder about to go up: the voxels of it (packed), where their middle is, and when it blows; a charge a sapper of the
/// horde laid against a wall, whose fuse one of ours may still cut.
/// </summary>
public sealed class Charge
{
    public readonly List<int> Voxels = new();
    public float X, Y, Z;
    public double BlowAt;
    /// <summary>Laid by a sapper with a fuse burning down; and how long one of ours has been at the fuse, cutting it.</summary>
    public bool Planted;
    public double Cutting;
    public string Cause = "";
    /// <summary>The one of ours running to cut its fuse; nobody can get there in time, and ours keep clear of it.</summary>
    public Unit Defuser;
    public bool Abandoned;
}

/// <summary>
/// Black powder. The brush lays it in kegs; fire sets it off — a spark, a burning neighbour, a fire arrow, a raider's
/// torch, lightning — and a moment later it goes up, with everything about it: the heart of the blast to dust, the rest
/// flung off in pieces, the bodies by it thrown and hurt, what burns about it alight, and the powder near it set off in
/// turn. A trail of it burns along at a run, a cell at a time, each going up as the fire reaches it; a great stack goes
/// up as several. The horde's sappers carry charges of it: where the horde would have to hack through a wall of ours,
/// a sapper lays its charge against the wall instead, lights the fuse and runs. One of ours who gets to the fuse in
/// time cuts it, and the powder is ours: the nearest free fighter runs for it by itself if it can be there in time,
/// and if nobody can, ours near it get away from the wall before it goes. A sapper cut down while burning goes up with
/// its charge where it falls.
/// </summary>
public sealed partial class Sim
{
    public readonly List<Charge> Charges = new();
    /// <summary>Powder voxels already set to go up, and with what.</summary>
    readonly Dictionary<int, Charge> _fused = new();

    /// <summary>Seconds from a keg catching to its going up; a sapper's fuse; how fast fire runs along a trail of powder.</summary>
    const double PowderFuse = .5, SapperFuse = 6;
    const float TrailRun = 6 * VoxelWorld.PerMetre;
    /// <summary>The kegs a sapper carries: a cube this many voxels a side (half a metre).</summary>
    const int SapperKeg = 4;
    /// <summary>The most blasts that go off at one step: the rest a step later.</summary>
    const int BlastsAtOnce = 3;
    double _plantSaid = -99, _cutSaid = -99, _runSaid = -99, _clearSaid = -99, _tendAt;
    /// <summary>How near a sapper's charge ours are got away from (voxels), and how far they go: past where its pieces fall.</summary>
    const float Danger = 12 * VoxelWorld.PerMetre, SafeOff = 15 * VoxelWorld.PerMetre;
    /// <summary>How long cutting a fuse takes, and how far off it one must stand to cut it.</summary>
    const double CutFor = 1.5;
    const float CutReach = 1.6f * VoxelWorld.PerMetre;

    /// <summary>A blast went off: where, and how far it reached (voxels).</summary>
    public event Action<float, float, float, float> Exploded;
    /// <summary>A sapper laid its charge and lit the fuse.</summary>
    public event Action<Charge> Planted;

    /// <summary>How far a blast of this much powder reaches, in voxels: half a metre for a voxel of it, two for a cube of half a metre, three and a half at most.</summary>
    static float BlastReach(int powder) => Math.Clamp(MathF.Cbrt(powder) * .5f, .5f, 3.5f) * VoxelWorld.PerMetre;

    /// <summary>
    /// Sets the powder touching a voxel to go up: all of it that lies together (kegs touching at an edge or a corner
    /// too), in charges of a metre and a half along it, each a little later the farther the fire has to run to it. A
    /// stack goes up as one, a trail cell by cell along its length. Powder already set goes up no later than this.
    /// </summary>
    void Fuse(int x, int y, int z, double delay, string cause, bool planted = false)
    {
        int start = WaterPlanner.Pack(x, y, z);
        if (_fused.TryGetValue(start, out var already))
        {
            if (!planted) already.BlowAt = Math.Min(already.BlowAt, Time + delay);
            return;
        }
        if (World.Get(x, y, z) != Mat.Powder) return;
        // how far the fire runs to each voxel of it, through the powder
        var run = new Dictionary<int, int> { [start] = 0 };
        var order = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0 && order.Count < 6000)
        {
            int p = queue.Dequeue();
            if (_fused.TryGetValue(p, out var other)) { if (!planted) other.BlowAt = Math.Min(other.BlowAt, Time + delay + run[p] / TrailRun); continue; }
            order.Add(p);
            var (px, py, pz) = WaterPlanner.Unpack(p);
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = px + dx, ny = py + dy, nz = pz + dz, q = WaterPlanner.Pack(nx, ny, nz);
                        if ((dx | dy | dz) == 0 || run.ContainsKey(q) || !World.InBounds(nx, ny, nz) || World.Get(nx, ny, nz) != Mat.Powder) continue;
                        run[q] = run[p] + 1;
                        queue.Enqueue(q);
                    }
        }
        // a charge to each stretch of it, and each stretch split where it does not hang together
        const int Stretch = 12;
        foreach (int seed in order)
        {
            if (_fused.ContainsKey(seed)) continue;
            int band = run[seed] / Stretch;
            var c = new Charge { Planted = planted, Cause = cause, BlowAt = Time + delay + run[seed] / TrailRun };
            var piece = new Queue<int>();
            piece.Enqueue(seed); _fused[seed] = c;
            float sx = 0, sy = 0, sz = 0;
            while (piece.Count > 0)
            {
                int p = piece.Dequeue();
                c.Voxels.Add(p);
                var (px, py, pz) = WaterPlanner.Unpack(p);
                sx += px; sy += py; sz += pz;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int q = WaterPlanner.Pack(px + dx, py + dy, pz + dz);
                            if ((dx | dy | dz) == 0 || _fused.ContainsKey(q) || !run.TryGetValue(q, out int rq) || rq / Stretch != band) continue;
                            _fused[q] = c;
                            piece.Enqueue(q);
                        }
            }
            c.X = sx / c.Voxels.Count + .5f; c.Y = sy / c.Voxels.Count + .5f; c.Z = sz / c.Voxels.Count + .5f;
            Charges.Add(c);
        }
    }

    /// <summary>The powder whose time has come goes up; a sapper's fuse one of ours has got to is cut.</summary>
    void TickCharges(float dt)
    {
        if (Charges.Count == 0) return;
        // twice a second: who runs for the fuses, and who gets away from them
        if (Time - _tendAt >= .5)
        {
            _tendAt = Time;
            foreach (var c in Charges) if (c.Planted && c.BlowAt > Time) Tend(c);
        }
        int blasts = 0;
        for (int i = Charges.Count - 1; i >= 0; i--)
        {
            if (i >= Charges.Count) continue;
            var c = Charges[i];
            if (c.Planted && c.BlowAt - Time > .3 && CutFuse(c, dt)) continue;
            if (c.BlowAt > Time || blasts >= BlastsAtOnce) continue;
            Charges.Remove(c);
            int n = 0;
            foreach (int p in c.Voxels)
            {
                _fused.Remove(p);
                var (x, y, z) = WaterPlanner.Unpack(p);
                if (World.Get(x, y, z) != Mat.Powder) continue;
                World.Set(x, y, z, Mat.Air);
                if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
                Burning.Remove(p);
                n++;
            }
            // dug out or blown away before it went: nothing left to go up
            if (n == 0) continue;
            float r = BlastReach(n) * (Knows("naphtha") ? 1.33f : 1);
            Detonate(c.X, c.Y, c.Z, r, 24 * r / VoxelWorld.PerMetre, c.Cause, always: true);
            blasts++;
        }
    }

    /// <summary>One of ours at a sapper's burning fuse cuts it in a second and a half; true when it is cut. The powder is ours then.</summary>
    bool CutFuse(Charge c, float dt)
    {
        Unit by = null;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null || u.Team != Team.Player || u.T == Kinds.Risen) continue;
            if (ToCharge(u.X, u.Y + 4, u.Z, c) < CutReach) { by = u; break; }
        }
        c.Cutting = by != null ? c.Cutting + dt : Math.Max(0, c.Cutting - dt);
        if (c.Cutting < CutFor) return false;
        Charges.Remove(c);
        foreach (int p in c.Voxels) { _fused.Remove(p); Burning.Remove(p); }
        if (Time - _cutSaid > 10)
        {
            _cutSaid = Time;
            Say($"{by?.T.Name ?? "Наш"} перерезал фитиль: заряд сапёра не взорвётся, а порох теперь наш.", 1, c.X, c.Z);
        }
        return true;
    }

    static float ToCharge(float x, float y, float z, Charge c) => MathF.Sqrt((x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y) + (z - c.Z) * (z - c.Z));

    /// <summary>
    /// A sapper's fuse burning down: the nearest free fighter of ours runs to cut it if it can be there in time, and
    /// gives it up when it no longer can; if nobody can, ours near it get away from the wall.
    /// </summary>
    void Tend(Charge c)
    {
        double left = c.BlowAt - Time;
        if (c.Defuser is { } d && (d.Dead || d.Inside || d.Airborne != null || d.Captor != null || d.Defusing != c || !InTime(d, c, left)))
        {
            if (d.Defusing == c) { d.Defusing = null; d.Path = null; }
            c.Defuser = null;
            c.Abandoned = true;
        }
        // one may still come by in time, even when nobody could before: it runs for it, and the rest keep clear anyway
        if (c.Defuser == null)
        {
            var runner = Runner(c, left);
            if (runner != null)
            {
                c.Defuser = runner; runner.Defusing = c; runner.Shunning = null;
                ReleaseSite(runner); StopBuild(runner);
                runner.Target = null; runner.Prey = null;
                if (Time - _runSaid > 10) { _runSaid = Time; Say($"{runner.T.Name} бежит перерезать фитиль.", 1, c.X, c.Z); }
            }
            else if (!c.Abandoned)
            {
                c.Abandoned = true;
                if (Time - _clearSaid > 10) { _clearSaid = Time; Say("К фитилю заряда не успеть: наши уходят от стены.", 3, c.X, c.Z); }
            }
        }
        if (!c.Abandoned) return;
        // nobody will get there: ours near it walk off
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null || u == Possessed || u.Team != Team.Player || u.Captor != null || u.Shunning == c || u == c.Defuser) continue;
            if (ToCharge(u.X, u.Y + 4, u.Z, c) > Danger) continue;
            if (u.Defusing != null) { u.Defusing = null; }
            u.Shunning = c;
            u.Path = null;
            ReleaseSite(u); StopBuild(u);
        }
    }

    /// <summary>Whether one running for a fuse can be there and cut it before it goes: by the way left to it.</summary>
    bool InTime(Unit u, Charge c, double left)
    {
        if (ToCharge(u.X, u.Y + 4, u.Z, c) < CutReach) return left > CutFor - c.Cutting + .1;
        int cells = u.Path == null ? int.MaxValue : u.Path.Count - u.PathI;
        if (cells == int.MaxValue) return false;
        return cells / (u.T.Move.Speed * u.Pace * 1.2) + CutFor + .3 < left;
    }

    /// <summary>The nearest free fighter of ours that can get to a fuse and cut it in time, with its way planned there; or nobody.</summary>
    Unit Runner(Charge c, double left)
    {
        var free = new List<Unit>();
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null || u == Possessed || u.Team != Team.Player || u.Captor != null || u.T == Kinds.Risen || u.T.Prays) continue;
            if (!(u.T.Work < 1 || u.Militia) || u.T.NoHands || u.Mending || u.Defusing != null || u.State == UnitState.Fight || !HasMainHand(u)) continue;
            if (ToCharge(u.X, u.Y + 4, u.Z, c) > 30 * VoxelWorld.PerMetre) continue;
            free.Add(u);
        }
        free.Sort((a, b) => ToCharge(a.X, a.Y, a.Z, c).CompareTo(ToCharge(b.X, b.Y, b.Z, c)));
        int target = Nav.CellOf(c.X, c.Y, c.Z), ti = Nav.I(target), tk = Nav.K(target), ty = Nav.Y(target);
        for (int n = 0; n < Math.Min(4, free.Count); n++)
        {
            var u = free[n];
            if (ToCharge(u.X, u.Y + 4, u.Z, c) < CutReach) { u.Path = null; return u; }
            var path = Paths.Find(u.Cell, x => Math.Abs(Nav.I(x) - ti) <= 1 && Math.Abs(Nav.K(x) - tk) <= 1 && Math.Abs(Nav.Y(x) - ty) <= 2,
                x => H(x, ti, tk), u.T.Move, 6000, gates: true);
            if (path == null || path.Count / (u.T.Move.Speed * u.Pace * 1.2) + CutFor + .3 >= left) continue;
            u.Path = path; u.PathI = 0;
            return u;
        }
        return null;
    }

    /// <summary>One of ours running for a fuse: to it, past whatever is in the way, and at it till it is cut. False when it is over.</summary>
    bool Defuse(Unit u, float dt)
    {
        var c = u.Defusing;
        if (!Charges.Contains(c) || c.Defuser != u) { u.Defusing = null; return false; }
        if (ToCharge(u.X, u.Y + 4, u.Z, c) < CutReach)
        {
            u.Path = null;
            u.State = UnitState.Work;
            u.Facing = MathF.Atan2(c.Z - u.Z, c.X - u.X);
            u.Why = "режет фитиль заряда";
            return true;
        }
        if (u.Path == null || u.PathI >= u.Path.Count)
        {
            if (!GoTo(u, Nav.CellOf(c.X, c.Y, c.Z), adjacent: true, budget: 6000)) { u.Defusing = null; c.Defuser = null; return false; }
            // standing by it already, and still not near enough: closer, inside the cell
            if (u.Path == null) { u.Path = new List<int> { Nav.CellOf(c.X, c.Y, c.Z) }; u.PathI = 0; }
        }
        Walk(u, dt, 1.2f);
        u.Why = "бежит резать фитиль";
        return true;
    }

    /// <summary>One of ours keeping clear of a charge nobody can get to: off to well away from it, and there till it goes. False when it is over.</summary>
    bool KeepClear(Unit u, float dt)
    {
        var c = u.Shunning;
        if (!Charges.Contains(c)) { u.Shunning = null; return false; }
        if (ToCharge(u.X, u.Y + 4, u.Z, c) >= SafeOff) { u.Path = null; u.State = UnitState.Idle; u.Why = "переждать взрыв"; return true; }
        if (u.Path == null || u.PathI >= u.Path.Count)
        {
            if (Time < u.NextSearch) { u.State = UnitState.Idle; return true; }
            u.NextSearch = Time + .5;
            var path = Paths.Find(u.Cell, x => { var (fx, fy, fz) = Nav.Feet(x); return ToCharge(fx, fy + 4, fz, c) >= SafeOff; }, x => 0, u.T.Move, 2500, gates: true);
            if (path == null) { u.State = UnitState.Idle; return true; }
            u.Path = path; u.PathI = 0;
        }
        Walk(u, dt, 1.2f);
        u.State = UnitState.Flee;
        u.Why = "уходит от заряда";
        return true;
    }

    /// <summary>
    /// A blast at a spot, reaching this far (voxels), hurting as hard as this at its heart. What is there and breaks goes
    /// up — the heart of it to dust, the rest in pieces that fly off on their arcs, strike whoever they come down on and
    /// lie in heaps of what they were; what it held up comes down after. Those about it are hurt and thrown, engines
    /// battered, what burns round it catches, and powder near it goes up a moment later. Returns how much it did: voxels
    /// broken and bodies struck.
    /// </summary>
    public int Detonate(float fx, float fy, float fz, float r, float harm, string cause, bool always = false)
    {
        int x = (int)fx, y = (int)fy, z = (int)fz, ri = (int)MathF.Ceiling(r);
        // powder near it is not blown about: its fire reaches half again as far as the blast breaks, and that goes up itself, a moment after
        int pr = (int)(r * 1.5f) + VoxelWorld.PerMetre;
        for (int dy = -pr; dy <= pr; dy++)
            for (int dz = -pr; dz <= pr; dz++)
                for (int dx = -pr; dx <= pr; dx++)
                {
                    int vx = x + dx, vy = y + dy, vz = z + dz;
                    if (dx * dx + dy * dy + dz * dz > pr * pr || !World.InBounds(vx, vy, vz) || World.Get(vx, vy, vz) != Mat.Powder) continue;
                    if (!_fused.ContainsKey(WaterPlanner.Pack(vx, vy, vz))) Fuse(vx, vy, vz, .12 + MathF.Sqrt(dx * dx + dy * dy + dz * dz) / TrailRun, cause);
                }
        var shell = new List<(int x, int y, int z)>();
        int dust = 0;
        float core = r * .4f;
        for (int dy = -ri; dy <= ri; dy++)
            for (int dz = -ri; dz <= ri; dz++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    float d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 > r * r) continue;
                    int vx = x + dx, vy = y + dy, vz = z + dz;
                    if (!World.InBounds(vx, vy, vz) || vy < 4) continue;
                    byte m = World.Get(vx, vy, vz);
                    if (!Breakable(m) || m == Mat.Powder) continue;
                    if (d2 >= core * core) { shell.Add((vx, vy, vz)); continue; }
                    // the heart of it goes to dust
                    World.Set(vx, vy, vz, Mat.Air);
                    if (Orders.At(vx, vy, vz) != 0) Orders.Clear(vx, vy, vz);
                    Burning.Remove(WaterPlanner.Pack(vx, vy, vz));
                    dust++;
                }
        int done = dust + shell.Count;
        if (shell.Count > 0)
        {
            // the rest breaks into pieces about points strewn through it, each voxel going with the nearest
            var pieces = Split(shell, 150);
            int room = MaxAloft - Aloft;
            float kick = Math.Clamp(r / (2.5f * VoxelWorld.PerMetre), .5f, 1.3f);
            foreach (var piece in pieces)
            {
                if (piece.Count == 0) continue;
                float cx = 0, cy = 0, cz = 0;
                foreach (var (px, py, pz) in piece) { cx += px; cy += py; cz += pz; }
                cx = cx / piece.Count - x; cy = cy / piece.Count - y; cz = cz / piece.Count - z;
                float od = MathF.Max(.5f, MathF.Sqrt(cx * cx + cy * cy + cz * cz));
                // out from the heart and up: what lay under it goes up too
                float hx = cx / od, hz = cz / od, up = MathF.Max(0, cy / od) * .8f + .6f, hl = MathF.Sqrt(hx * hx + up * up + hz * hz);
                float speed = (11 - 4 * MathF.Min(1, od / r)) * VoxelWorld.PerMetre * kick * (.8f + (float)Rng.NextDouble() * .4f);
                var f = piece.Count <= room ? Loose(piece, hx / hl * speed, up / hl * speed, hz / hl * speed, cause, room) : null;
                if (f != null) { room -= f.Voxels.Count; continue; }
                // no room in the air for it: dust
                foreach (var (px, py, pz) in piece)
                {
                    World.Set(px, py, pz, Mat.Air);
                    if (Orders.At(px, py, pz) != 0) Orders.Clear(px, py, pz);
                    Burning.Remove(WaterPlanner.Pack(px, py, pz));
                }
            }
        }
        // those about it: hurt, and thrown out from it; engines battered where they stand
        float reach = r * 1.8f;
        var near = Units.FindAll(u => !u.Dead && !u.Inside && MathF.Abs(u.X - fx) < reach && MathF.Abs(u.Z - fz) < reach);
        foreach (var u in near)
        {
            float dx = u.X - fx, dz = u.Z - fz, dy = u.Y + u.T.BodyH * .5f - fy, d = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (d > reach) continue;
            float k = 1 - d / reach;
            done++;
            if (Kinds.IsEngine(u.T)) { Hurt(u, null, harm * 3.3f * (.5f + .5f * k), cause); continue; }
            Hurt(u, null, harm * k, cause);
            float h = MathF.Sqrt(dx * dx + dz * dz), ux = h > .5f ? dx / h : MathF.Cos(u.Id), uz = h > .5f ? dz / h : MathF.Sin(u.Id);
            float push = (6 + 9 * k) * VoxelWorld.PerMetre * MathF.Min(1, 15 / u.T.BodyH) * Math.Clamp(r / (2.5f * VoxelWorld.PerMetre), .5f, 1.2f);
            Fling(u, ux * push * .8f, push * .75f, uz * push * .8f, cause);
        }
        // what burns round it catches
        int lit = 0, rr = ri + VoxelWorld.PerMetre;
        for (int dy = -rr; dy <= rr && lit < 40; dy += 2)
            for (int dz = -rr; dz <= rr && lit < 40; dz += 2)
                for (int dx = -rr; dx <= rr && lit < 40; dx += 2)
                {
                    int vx = x + dx, vy = y + dy, vz = z + dz;
                    if (dx * dx + dy * dy + dz * dz > rr * rr || !World.InBounds(vx, vy, vz) || !Mat.Burns(World.Get(vx, vy, vz))) continue;
                    if (Rng.NextDouble() < .3 && Ignite(vx, vy, vz)) lit++;
                }
        if (done > 0) InvalidateFields();
        if (done > 0 || always)
        {
            // the heat of it sears the turf round the crater
            SearRound(fx, fy, fz, r * 1.5f);
            Exploded?.Invoke(fx, fy, fz, r);
        }
        return done;
    }

    /// <summary>
    /// Kegs set down at a spot: a cube of powder this many voxels a side on whatever is under it, in the air there.
    /// Returns the voxels laid.
    /// </summary>
    List<(int x, int y, int z)> SetKegs(int x0, int y0, int z0, int size)
    {
        var laid = new List<(int, int, int)>();
        for (int z = z0; z < z0 + size; z++)
            for (int x = x0; x < x0 + size; x++)
            {
                if (x < 2 || z < 2 || x >= World.SX - 2 || z >= World.SZ - 2) continue;
                // down onto the ground at the spot, then up as high as the kegs stand
                int y = Math.Clamp(y0, 2, World.SY - size - 2);
                while (y > 2 && World.Get(x, y - 1, z) is var b && (b == Mat.Air || Mat.Passable[b])) y--;
                while (y < World.SY - 2 && World.Get(x, y, z) is var a && a != Mat.Air && !Mat.Passable[a]) y++;
                for (int k = 0; k < size; k++)
                {
                    byte at = World.Get(x, y + k, z);
                    if (at != Mat.Air && !(Mat.Passable[at] && !Mat.IsLadder[at])) break;
                    World.Set(x, y + k, z, Mat.Powder);
                    laid.Add((x, y + k, z));
                }
            }
        return laid;
    }

    /// <summary>
    /// A sapper with a charge up against a wall of ours that it would have to hack through: it sets its kegs down
    /// against the wall, lights the fuse and runs. False when what is in the way is not a wall of ours.
    /// </summary>
    bool PlantCharge(Unit u, int next)
    {
        int di = Nav.I(next) - Nav.I(u.Cell), dk = Nav.K(next) - Nav.K(u.Cell);
        if (di == 0 && dk == 0) return false;
        int x0 = Nav.I(next) << NavGrid.Shift, y0 = Nav.Y(next) << NavGrid.Shift, z0 = Nav.K(next) << NavGrid.Shift;
        bool ours = false;
        for (int y = y0; y < y0 + NavGrid.CS * 2 && !ours; y++)
            for (int z = z0; z < z0 + NavGrid.CS && !ours; z++)
                for (int x = x0; x < x0 + NavGrid.CS && !ours; x++)
                    if (Mat.IsPlayer[World.Get(x, y, z)] && World.Get(x, y, z) != Mat.Powder) ours = true;
        if (!ours) return false;
        // at the foot of the wall, on the side of the cell toward it
        int cx = Nav.I(u.Cell) << NavGrid.Shift, cz = Nav.K(u.Cell) << NavGrid.Shift;
        int kx = di > 0 ? cx + NavGrid.CS - SapperKeg : di < 0 ? cx : cx + (NavGrid.CS - SapperKeg) / 2;
        int kz = dk > 0 ? cz + NavGrid.CS - SapperKeg : dk < 0 ? cz : cz + (NavGrid.CS - SapperKeg) / 2;
        var laid = SetKegs(kx, (int)u.Y + 1, kz, SapperKeg);
        if (laid.Count < 12)
        {
            // no room for the kegs: back to hacking like any
            foreach (var (x, y, z) in laid) World.Set(x, y, z, Mat.Air);
            u.Charge = false;
            return false;
        }
        var (lx, ly, lz) = laid[0];
        Fuse(lx, ly, lz, SapperFuse, "заряд сапёра", planted: true);
        u.Charge = false;
        u.Swing = 1;
        StopCarve(u); StopBuild(u);
        // its work is done: it runs for it
        u.Routed = true; u.Path = null;
        if (_fused.TryGetValue(WaterPlanner.Pack(lx, ly, lz), out var c)) Planted?.Invoke(c);
        if (Time - _plantSaid > 15)
        {
            _plantSaid = Time;
            Say("Сапёр заложил заряд под стену, взрыв через 6 секунд. Перережьте фитиль или уведите своих от стены.", 3, u);
        }
        return true;
    }

    /// <summary>A sapper cut down with its charge on its back: the kegs fall where it fell, and go up if it was burning.</summary>
    void DropCharge(Unit u, string cause)
    {
        u.Charge = false;
        var laid = SetKegs((int)u.X - 1, (int)u.Y + 1, (int)u.Z - 1, 3);
        if (laid.Count == 0) return;
        bool fire = u.Burn > 0 || cause is "огонь" or "молния" or "огненный дождь" or "взрыв" or "порох" or "заряд сапёра";
        if (fire) { var (x, y, z) = laid[0]; Fuse(x, y, z, .4, "заряд сапёра"); }
    }
}
