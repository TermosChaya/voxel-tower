using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Fire. What burns (<see cref="Mat.Catch"/>) catches from a burning neighbour — upward more readily than sideways, as
/// flames climb — and now and then from a spark that lands a little way off; it burns as long as its fuel lasts and
/// is then gone, or stands charred. Nothing beside water catches, and what burns beside it goes out. Whoever stands
/// in the flames or on them burns: our people and the beasts get out of them, the horde pushes through. Those of ours
/// set to put fires out beat them out of what we built and round the citadel; the forest far off may burn. Fire
/// arrows and the horde's torches set things alight, and a ditch floored with pitch burns long and twice as hot.
/// </summary>
public sealed partial class Sim
{
    /// <summary>What is burning: the voxel (packed) and the seconds of fuel left in it.</summary>
    public readonly Dictionary<int, float> Burning = new();
    /// <summary>Cells with something burning in them (for the way out of a fire), and blocks of two voxels (for who it
    /// touches), with how hot; made again at every beat of the fire.</summary>
    readonly Dictionary<int, float> _hot = new(), _hotBlocks = new();
    /// <summary>Cells where something of ours, or something round the citadel, is burning: work for those set to put fires out.</summary>
    readonly HashSet<int> _homeFires = new();
    /// <summary>Cells where the horde has raised a ladder or laid a deck, for archers with fire arrows to set alight.</summary>
    readonly HashSet<int> _works = new();
    readonly List<int> _fireKeys = new();
    float _fireAt;
    int _fireBeats;
    double _kindleSaid = -99;

    /// <summary>The most that may burn at once: past it, fire spreads no further.</summary>
    const int MaxBurning = 24000;
    /// <summary>Seconds between beats of the fire, and hit points a second a body loses burning (pitch twice).</summary>
    const float FireStep = .25f, BurnDamage = 6;
    /// <summary>How far round the citadel a fire is ours to put out, whatever burns there.</summary>
    const float HomeRange = 45 * VoxelWorld.PerMetre;

    /// <summary>Something caught fire, and something burnt away (for the flames, the smoke and the cinders).</summary>
    public Action<int, int, int, byte> Caught, Burnt;

    /// <summary>Fires in what we built or round the citadel, burning now.</summary>
    public int HomeFires => _homeFires.Count;

    /// <summary>Sets a voxel alight; false if it does not burn, burns already, or is wet. Pitch flashes over: all of
    /// it that lies together goes up at once.</summary>
    public bool Ignite(int x, int y, int z)
    {
        if (!Light(x, y, z)) return false;
        if (World.Get(x, y, z) == Mat.Pitch) Flash(x, y, z);
        return true;
    }

    bool Light(int x, int y, int z)
    {
        byte m = World.Get(x, y, z);
        if (!Mat.Burns(m) || Burning.Count >= MaxBurning) return false;
        int p = WaterPlanner.Pack(x, y, z);
        if (Burning.ContainsKey(p) || Wet(x, y, z)) return false;
        Burning[p] = Mat.Fuel[m] * (.75f + (float)Rng.NextDouble() * .5f);
        Caught?.Invoke(x, y, z, m);
        return true;
    }

    readonly Queue<int> _flash = new();

    /// <summary>Burning pitch runs through all the pitch that touches it, a few thousand voxels at most at a time.</summary>
    void Flash(int x, int y, int z)
    {
        _flash.Clear();
        _flash.Enqueue(WaterPlanner.Pack(x, y, z));
        int n = 0;
        while (_flash.Count > 0 && n++ < 4000)
        {
            var (px, py, pz) = WaterPlanner.Unpack(_flash.Dequeue());
            for (int d = 0; d < 6; d++)
            {
                int nx = px + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = py + (d == 2 ? 1 : d == 3 ? -1 : 0), nz = pz + (d == 4 ? 1 : d == 5 ? -1 : 0);
                if (World.Get(nx, ny, nz) == Mat.Pitch && Light(nx, ny, nz)) _flash.Enqueue(WaterPlanner.Pack(nx, ny, nz));
            }
        }
    }

    /// <summary>Water beside it: it does not catch, and goes out if it burns.</summary>
    bool Wet(int x, int y, int z) =>
        World.Get(x + 1, y, z) == Mat.Water || World.Get(x - 1, y, z) == Mat.Water || World.Get(x, y + 1, z) == Mat.Water ||
        World.Get(x, y - 1, z) == Mat.Water || World.Get(x, y, z + 1) == Mat.Water || World.Get(x, y, z - 1) == Mat.Water;

    public bool IsBurning(int x, int y, int z) => Burning.ContainsKey(WaterPlanner.Pack(x, y, z));

    /// <summary>A fire there is ours to put out: in something we built, in the dressing of it, or round the citadel. Pitch
    /// is laid to burn: nobody puts it out.</summary>
    bool Ours(int x, int y, int z, byte m)
    {
        if (m == Mat.Pitch) return false;
        if (Mat.IsPlayer[m] || Mat.IsDecor[m] || MeantAt(x, y, z) != 0) return true;
        float dx = x - Site.KeepX, dz = z - Site.KeepZ;
        return dx * dx + dz * dz < HomeRange * HomeRange;
    }

    // the neighbours a fire reaches, and how much more readily: up most, then up and aside, aside, and down least
    static readonly (int dx, int dy, int dz, float k)[] Reach =
    {
        (1, 0, 0, 1), (-1, 0, 0, 1), (0, 0, 1, 1), (0, 0, -1, 1), (0, -1, 0, .5f), (0, 1, 0, 2.2f),
        (1, 1, 0, 1.2f), (-1, 1, 0, 1.2f), (0, 1, 1, 1.2f), (0, 1, -1, 1.2f),
    };

    void TickFire(float dt)
    {
        _fireAt += dt;
        if (_fireAt < FireStep) return;
        float step = _fireAt;
        _fireAt = 0;
        _hot.Clear();
        _hotBlocks.Clear();
        _homeFires.Clear();
        if (Burning.Count > 0)
        {
            _fireKeys.Clear();
            _fireKeys.AddRange(Burning.Keys);
            foreach (int p in _fireKeys)
            {
                var (x, y, z) = WaterPlanner.Unpack(p);
                byte m = World.Get(x, y, z);
                // dug out, hacked away, swung off on a gate's leaf, or drowned: nothing burns here any more
                if (!Mat.Burns(m) || Wet(x, y, z)) { Burning.Remove(p); continue; }
                float fuel = Burning[p] - step;
                if (fuel <= 0) { BurnOut(p, x, y, z, m); continue; }
                Burning[p] = fuel;
                int cell = Nav.CellOf(x, y, z), block = Block(x, y, z);
                if (!_hot.TryGetValue(cell, out float h) || h < Mat.Heat[m]) _hot[cell] = Mat.Heat[m];
                if (!_hotBlocks.TryGetValue(block, out h) || h < Mat.Heat[m]) _hotBlocks[block] = Mat.Heat[m];
                if (Ours(x, y, z, m)) _homeFires.Add(cell);
                if (Burning.Count >= MaxBurning) continue;
                foreach (var (dx, dy, dz, k) in Reach)
                {
                    int nx = x + dx, ny = y + dy, nz = z + dz;
                    byte n = World.Get(nx, ny, nz);
                    // fire runs over what the air gets at; into the heart of a beam it eats only slowly
                    if (Mat.Burns(n) && Rng.NextDouble() < Mat.Catch[n] * k * step * (Open(nx, ny, nz) ? 1 : .12f)) Ignite(nx, ny, nz);
                }
                // a spark lands a little way off, mostly upward
                if (Rng.NextDouble() < .01 * step)
                {
                    int sx = x + Rng.Next(-5, 6), sy = y + Rng.Next(-2, 7), sz = z + Rng.Next(-5, 6);
                    byte n = World.Get(sx, sy, sz);
                    if (Mat.Burns(n) && Rng.NextDouble() < Mat.Catch[n] * 2) Ignite(sx, sy, sz);
                }
            }
        }
        BurnBodies(step);
        if (++_fireBeats % 8 == 0) _works.RemoveWhere(c => !HasWorks(c));
    }

    /// <summary>The fuel is spent: the voxel is gone, or stands charred, and an order to take it out goes with it.</summary>
    void BurnOut(int p, int x, int y, int z, byte m)
    {
        Burning.Remove(p);
        byte left = Rng.NextDouble() < Mat.CharShare[m] ? Mat.Char : Mat.Air;
        World.Set(x, y, z, left);
        if (left == Mat.Air && Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
        Burnt?.Invoke(x, y, z, m);
    }

    /// <summary>The air gets at it: something beside it is open (air, leaves, grass, a ladder).</summary>
    bool Open(int x, int y, int z)
    {
        static bool Air(byte m) => m == Mat.Air || (Mat.Passable[m] && m != Mat.Water);
        return Air(World.Get(x + 1, y, z)) || Air(World.Get(x - 1, y, z)) || Air(World.Get(x, y + 1, z)) ||
            Air(World.Get(x, y - 1, z)) || Air(World.Get(x, y, z + 1)) || Air(World.Get(x, y, z - 1));
    }

    /// <summary>A cell burns, or the floor under it does.</summary>
    bool HotAt(int c) => _hot.ContainsKey(c) || (c >= Nav.Layer && _hot.ContainsKey(c - Nav.Layer));

    static int Block(int x, int y, int z) => (x >> 1) | (z >> 1) << 9 | (y >> 1) << 18;

    /// <summary>How hot the flames that touch a body are: something burning under its feet or against it; 0 for none.</summary>
    float FireOn(Unit u)
    {
        if (_hotBlocks.Count == 0) return 0;
        float heat = 0, r = u.T.BodyR + 1;
        int x0 = (int)(u.X - r) >> 1, x1 = (int)(u.X + r) >> 1, z0 = (int)(u.Z - r) >> 1, z1 = (int)(u.Z + r) >> 1;
        int y0 = Math.Max(0, (int)MathF.Floor(u.Y) - 1) >> 1, y1 = (int)(u.Y + u.T.BodyH * .7f) >> 1;
        for (int by = y0; by <= y1; by++)
            for (int bz = z0; bz <= z1; bz++)
                for (int bx = x0; bx <= x1; bx++)
                    if (_hotBlocks.TryGetValue(bx | bz << 9 | by << 18, out float h) && h > heat) heat = h;
        return heat;
    }

    /// <summary>Whoever stands in the flames, or on what burns, catches; whoever is alight burns a while longer.</summary>
    void BurnBodies(float step)
    {
        foreach (var u in Units)
        {
            if (u.Dead) continue;
            if (_hotBlocks.Count > 0)
            {
                float heat = FireOn(u);
                // burning pitch clings: whoever steps out of it burns on longer
                if (heat > 0) { u.Burn = Math.Max(u.Burn, heat > 1 ? 3 : 1.5f); u.BurnHeat = Math.Max(u.BurnHeat, heat); }
            }
            if (u.Burn <= 0) continue;
            u.Burn -= step;
            Hurt(u, null, BurnDamage * u.BurnHeat * step, "огонь");
            if (u.Burn <= 0) { u.Burn = 0; u.BurnHeat = 1; }
        }
    }

    /// <summary>One of ours, or a beast, standing in the flames gets out of them before anything else; true while it does.</summary>
    bool EscapeFire(Unit u, float dt)
    {
        if (_hotBlocks.Count == 0 || FireOn(u) <= 0) return false;
        if (u.Path == null || u.PathI >= u.Path.Count || HotAt(u.Path[^1]) || Time >= u.NextSearch)
        {
            u.NextSearch = Time + .6;
            int from = u.Cell;
            var path = Paths.Find(from, c => c != from && !HotAt(c), c => 0, u.T.Move, 900, gates: true);
            if (path == null || path.Count == 0) return false;
            u.Path = path; u.PathI = 0;
        }
        ReleaseSite(u);
        Walk(u, dt);
        u.State = UnitState.Flee;
        u.Why = "";
        return true;
    }

    // ------------------------------------------------------------ putting fires out

    /// <summary>
    /// A pawn set to put out fires goes to the nearest fire in what we built or round the citadel and beats it out,
    /// a burning voxel within reach at a time; it leaves its work for that only if putting fires out comes before it.
    /// </summary>
    bool FightFire(Unit u, float dt)
    {
        if (_homeFires.Count == 0 || !Work.Does(u, WorkKind.Fire) || !HasMainHand(u) || u.Hold) { u.FireCell = -1; return false; }
        if (u.FireCell < 0)
        {
            // at work it puts first: the fire must come before that
            if (u.Site >= 0)
            {
                int first = 9;
                for (int k = 0; k < Work.Kinds; k++) if ((u.SiteWork >> k & 1) != 0 && u.Priority[k] > 0) first = Math.Min(first, u.Priority[k]);
                if (u.Priority[(int)WorkKind.Fire] > first) return false;
            }
            if (Time < u.NextFireLook) return false;
            u.NextFireLook = Time + .5;
            int best = -1;
            float bd = 60 * VoxelWorld.PerMetre / NavGrid.CS;
            int ui = Nav.I(u.Cell), uk = Nav.K(u.Cell);
            foreach (int c in _homeFires)
            {
                if (u.Unreachable.TryGetValue(-1 - c, out double until) && Time < until) continue;
                float d = PathFinder.Octile(Nav.I(c) - ui, Nav.K(c) - uk) + Math.Abs(Nav.Y(c) - Nav.Y(u.Cell));
                if (d < bd) { bd = d; best = c; }
            }
            if (best < 0) return false;
            // somewhere to stand with a burning voxel of that fire within reach
            int at = FireVoxel(best);
            if (at < 0) return false;
            var (vx, vy, vz) = WaterPlanner.Unpack(at);
            bool Reaches(int c) => Math.Abs((Nav.I(c) << NavGrid.Shift) + 4 - vx) <= 10 && Math.Abs((Nav.K(c) << NavGrid.Shift) + 4 - vz) <= 10
                && Nav.Floor[c] - Labour.ReachDown <= vy && Nav.Floor[c] + Labour.ReachUp > vy;
            var path = Reaches(u.Cell) ? new List<int>() : Paths.Find(u.Cell, Reaches, c => H(c, vx >> NavGrid.Shift, vz >> NavGrid.Shift), u.T.Move, 20000, gates: true);
            if (path == null) { u.Unreachable[-1 - best] = Time + 10; return false; }
            ReleaseSite(u);
            u.Path = path.Count > 0 ? path : null;
            u.PathI = 0;
            u.FireCell = best;
        }
        if (!_homeFires.Contains(u.FireCell) && !NearFire(u)) { u.FireCell = -1; u.Path = null; return false; }
        u.Why = "";
        if (u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt); return true; }
        u.State = UnitState.Work;
        u.FireBeat += dt;
        if (u.FireBeat < .3f) return true;
        u.FireBeat = 0;
        if (!BeatOut(u)) { u.Unreachable[-1 - u.FireCell] = Time + 3; u.FireCell = -1; return false; }
        return true;
    }

    /// <summary>One of the voxels burning in a cell, or −1.</summary>
    int FireVoxel(int cell)
    {
        foreach (int p in Burning.Keys)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            if (Nav.CellOf(x, y, z) == cell) return p;
        }
        return -1;
    }

    /// <summary>Something burns within its reach.</summary>
    bool NearFire(Unit u) => FireInReach(u) >= 0;

    /// <summary>The burning voxel nearest the pawn within its reach (a cell to each side, down and up as at work), or −1.</summary>
    int FireInReach(Unit u)
    {
        int best = -1;
        float dist = float.MaxValue;
        float feet = u.Y;
        foreach (int p in Burning.Keys)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            float dx = x + .5f - u.X, dz = z + .5f - u.Z;
            if (Math.Abs(dx) > 12 || Math.Abs(dz) > 12 || y < feet - Labour.ReachDown || y > feet + Labour.ReachUp) continue;
            float d = dx * dx + dz * dz + (y - feet - 6) * (y - feet - 6) * .3f;
            if (d < dist) { dist = d; best = p; }
        }
        return best;
    }

    /// <summary>
    /// One beat with a wet sack: the nearest burning voxel in reach goes out, and the flames right round it with it.
    /// False when nothing is left in reach.
    /// </summary>
    bool BeatOut(Unit u)
    {
        int p = FireInReach(u);
        if (p < 0) return false;
        Burning.Remove(p);
        var (x, y, z) = WaterPlanner.Unpack(p);
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    Burning.Remove(WaterPlanner.Pack(x + dx, y + dy, z + dz));
        u.Swing = 1;
        u.Facing = MathF.Atan2(z + .5f - u.Z, x + .5f - u.X);
        return true;
    }

    // ------------------------------------------------------------ the horde's torches

    /// <summary>
    /// A raider with a torch, about to hack through something that burns, first sets alight the timber beside where it
    /// hacks (what it hacks out would only take the fire away with it): a few moments with the torch, then it hacks on
    /// while the fire eats along the wall. True while it is at it.
    /// </summary>
    bool Kindle(Unit u, float dt)
    {
        if (!u.Torch || (u.Kindling <= 0 && Time < u.NextTorch)) return false;
        // what we built of timber; a tree in its way it just hacks through
        bool Ours(int x, int y, int z) { byte m = World.Get(x, y, z); return Mat.Burns(m) && (Mat.IsPlayer[m] || MeantAt(x, y, z) != 0); }
        bool any = false;
        for (int i = u.CarveI; i < u.CarveList.Count && i < u.CarveI + 40 && !any; i++)
        {
            var (x, y, z) = u.CarveList[i];
            any = Ours(x, y, z);
        }
        if (!any) { u.Kindling = 0; return false; }
        u.State = UnitState.Siege;
        u.Kindling += dt;
        u.Swing = Math.Max(u.Swing, .5f);
        if (u.Kindling < 2.5f) return true;
        u.Kindling = 0;
        u.NextTorch = Time + 25;
        // the timber nearest its hand that the air gets at, a couple of cells aside from the way it and those after it
        // hack, where the fire is left to eat along the wall
        int ux = (int)u.X, uy = (int)u.Y, uz = (int)u.Z, best = -1, ci = Nav.I(u.CarveCell), ck = Nav.K(u.CarveCell);
        float bd = float.MaxValue;
        for (int y = uy; y < uy + 16; y++)
            for (int z = uz - 20; z <= uz + 20; z++)
                for (int x = ux - 20; x <= ux + 20; x++)
                {
                    if (Math.Max(Math.Abs((x >> NavGrid.Shift) - ci), Math.Abs((z >> NavGrid.Shift) - ck)) <= 1) continue;
                    if (!Ours(x, y, z) || !Open(x, y, z)) continue;
                    float d = (x - ux) * (x - ux) + (z - uz) * (z - uz) + (y - uy - 8) * (y - uy - 8);
                    if (d < bd) { bd = d; best = WaterPlanner.Pack(x, y, z); }
                }
        if (best < 0) { var (fx, fy, fz) = u.CarveList[u.CarveList.Count - 1]; best = WaterPlanner.Pack(fx, fy, fz); }
        var (kx, ky, kz) = WaterPlanner.Unpack(best);
        if (Ignite(kx, ky, kz) && Time - _kindleSaid > 20)
        {
            _kindleSaid = Time;
            Say($"{u.T.Name} поджигает {Mat.NameOf(World.Get(kx, ky, kz)).ToLowerInvariant()}.", 2, kx, kz);
        }
        return false;
    }

    // ------------------------------------------------------------ fire arrows

    /// <summary>A cell still holds some of the horde's ladder or deck.</summary>
    bool HasWorks(int c)
    {
        int x0 = Nav.I(c) << NavGrid.Shift, y0 = Nav.Y(c) << NavGrid.Shift, z0 = Nav.K(c) << NavGrid.Shift;
        for (int y = y0; y < y0 + NavGrid.CS; y++)
            for (int z = z0; z < z0 + NavGrid.CS; z++)
                for (int x = x0; x < x0 + NavGrid.CS; x++)
                    if (World.Get(x, y, z) is Mat.SiegeLadder or Mat.Deck) return true;
        return false;
    }

    /// <summary>A voxel of the horde's works in a cell that is not burning yet, or −1.</summary>
    int WorksVoxel(int c)
    {
        int x0 = Nav.I(c) << NavGrid.Shift, y0 = Nav.Y(c) << NavGrid.Shift, z0 = Nav.K(c) << NavGrid.Shift, found = -1, n = 0;
        for (int y = y0; y < y0 + NavGrid.CS; y++)
            for (int z = z0; z < z0 + NavGrid.CS; z++)
                for (int x = x0; x < x0 + NavGrid.CS; x++)
                {
                    if (World.Get(x, y, z) is not (Mat.SiegeLadder or Mat.Deck) || IsBurning(x, y, z)) continue;
                    // one of them at random, so two archers don't aim at the same rung
                    if (Rng.Next(++n) == 0) found = WaterPlanner.Pack(x, y, z);
                }
        return found;
    }

    /// <summary>Raiders standing on what burns (a deck, a ladder, pitch, timber) are worth a fire arrow most.</summary>
    bool OnTinder(Unit o) => Mat.Burns(World.Get((int)o.X, (int)MathF.Floor(o.Y) - 1, (int)o.Z)) || Climbing(o);

    /// <summary>
    /// An archer with fire arrows and no raider worth an arrow sets alight what the player pointed at, or else the
    /// nearest of the horde's ladders and decks it can see. True while it is at it.
    /// </summary>
    bool KindleShot(Unit u, float dt)
    {
        if (!u.FireArrows) return false;
        int target = -1;
        if (u.KindleAt >= 0)
        {
            var (kx, ky, kz) = WaterPlanner.Unpack(u.KindleAt);
            if (!Mat.Burns(World.Get(kx, ky, kz)) || IsBurning(kx, ky, kz)) u.KindleAt = -1;
            else target = u.KindleAt;
        }
        // between waves the horde's leftovers are timber to take apart, not to burn
        if (target < 0 && (Waves.Active || AnyRaider()))
        {
            float bd = u.T.Range;
            foreach (int c in _works)
            {
                var (fx, fy, fz) = Nav.Feet(c);
                float dx = fx - u.X, dz = fz - u.Z, d = MathF.Sqrt(dx * dx + dz * dz);
                if (d >= bd || !Sees(u, fx, fy + 4, fz)) continue;
                int v = WorksVoxel(c);
                if (v < 0) continue;
                bd = d; target = v;
            }
        }
        if (target < 0) return false;
        var (x, y, z) = WaterPlanner.Unpack(target);
        float tx = x + .5f, ty = y + .5f, tz = z + .5f, ddx = tx - u.X, ddz = tz - u.Z;
        float dist = MathF.Sqrt(ddx * ddx + ddz * ddz);
        if (dist > RangeAt(u, tx, tz) || !Sees(u, tx, ty, tz))
        {
            // the player's mark out of reach: closer, then shoot
            if (target != u.KindleAt) return false;
            if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
            {
                u.NextSearch = Time + 1;
                if (!GoTo(u, Nav.CellOf(x, y, z), adjacent: true, budget: 8000)) { u.KindleAt = -1; return false; }
            }
            ReleaseSite(u);
            Walk(u, dt);
            return true;
        }
        ReleaseSite(u);
        u.Path = null;
        u.State = UnitState.Fight;
        u.Facing = MathF.Atan2(ddz, ddx);
        if (u.Cooldown <= 0)
        {
            u.Swing = 1;
            Shots++;
            float chance = Math.Clamp(.95f - dist / u.T.Range * .4f, .45f, .95f);
            bool hits = Rng.NextDouble() < chance;
            // a miss comes down a little way off
            if (!hits) { tx += (float)(Rng.NextDouble() - .5) * 8; tz += (float)(Rng.NextDouble() - .5) * 8; }
            Arrows.Add(new Arrow
            {
                X0 = u.X, Y0 = u.Y + u.T.BodyH * .85f, Z0 = u.Z, X1 = tx, Y1 = ty, Z1 = tz,
                Dur = Math.Max(.08f, dist / ArrowSpeed), From = u, Hits = hits, Fire = true, At = target,
            });
            u.Cooldown = u.T.AttackTime * 1.35f * (.9f + (float)Rng.NextDouble() * .2f);
        }
        return true;
    }

    /// <summary>A fire arrow comes down: what it struck burns a while, and what it fell on may catch.</summary>
    void FireArrowLands(Arrow a)
    {
        if (a.At >= 0 && a.Hits)
        {
            var (kx, ky, kz) = WaterPlanner.Unpack(a.At);
            if (Rng.NextDouble() < .8) Ignite(kx, ky, kz);
            return;
        }
        if (a.Hits && a.Target is { Dead: false } t)
        {
            t.Burn = Math.Max(t.Burn, 2.5f);
            if (Rng.NextDouble() < .5) Ignite((int)t.X, (int)MathF.Floor(t.Y) - 1, (int)t.Z);
            return;
        }
        // a miss: down where it fell, on whatever is there
        int x = (int)a.X1, z = (int)a.Z1;
        for (int y = (int)a.Y1, end = y - 24; y > end && y > 0; y--)
        {
            byte m = World.Get(x, y, z);
            if (m == Mat.Air) continue;
            if (Mat.Burns(m) && Rng.NextDouble() < .65) Ignite(x, y, z);
            break;
        }
    }

    /// <summary>The player's pick for archers: shoot fire arrows, or plain ones.</summary>
    public void SetFireArrows(IEnumerable<Unit> who, bool on)
    {
        foreach (var u in who) if (!u.Dead && u.T.Ranged && u.Team == Team.Player) { u.FireArrows = on; if (!on) u.KindleAt = -1; }
    }

    /// <summary>Archers with fire arrows told to set something alight; returns how many took the order.</summary>
    public int KindleOrder(IEnumerable<Unit> who, int x, int y, int z)
    {
        if (!Mat.Burns(World.Get(x, y, z))) return 0;
        int n = 0;
        foreach (var u in who)
        {
            if (u.Dead || !u.T.Ranged || !u.FireArrows || u.Team != Team.Player) continue;
            u.KindleAt = WaterPlanner.Pack(x, y, z);
            u.Hold = false; u.Prey = null; u.Path = null; u.NextSearch = 0;
            n++;
        }
        return n;
    }
}
