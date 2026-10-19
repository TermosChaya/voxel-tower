using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The game without the engine: the valley, its navigation, the player's orders, the stores, the pawns, the horde and
/// the beasts. Step it with game seconds; the Godot layer only draws what is here and turns clicks into calls on it.
/// </summary>
public sealed partial class Sim
{
    public readonly VoxelWorld World;
    public readonly Site Site;
    public readonly NavGrid Nav;
    public readonly Orders Orders;
    public readonly Stockpile Stock = new();
    /// <summary>The horde's own timber and earth for siege works: what each wave brings, and half of what it hacks out.</summary>
    public readonly Stockpile HordeStock = new() { Gold = 0 };
    public readonly List<Unit> Units = new();
    public readonly PathFinder Paths;
    public readonly Random Rng = new(17);
    public readonly Waves Waves = new();

    public double Time;
    public float KeepHp = 1000, KeepMax = 1000;
    public bool Over, Won;
    /// <summary>Tests: orders are done the moment they are given.</summary>
    public bool InstantLabour;
    /// <summary>A cell before the citadel's door: hired pawns appear here and flee here.</summary>
    public readonly int Gate;

    int _nextId = 1;
    readonly int[] _bodies;
    readonly int[] _claims;

    /// <summary>Lines for the chronicle: text and kind (0 plain, 1 good, 2 warning, 3 bad).</summary>
    /// <summary>A line for the chronicle: what, how it is (0 plain, 1 good, 2 a warning, 3 bad), and where, if anywhere (NaN when not).</summary>
    public event Action<string, int, float, float> Said;

    public Sim(VoxelWorld w, Site s)
    {
        World = w; Site = s;
        Nav = new NavGrid(w);
        Nav.BuildAll();
        Orders = new Orders(w, s);
        Design = s.Design ??= new VoxelWorld(w.SX, w.SY, w.SZ);
        Arched = s.Arched ??= new VoxelWorld(w.SX, w.SY, w.SZ);
        Paths = new PathFinder(Nav);
        _bodies = new int[Nav.Count];
        _claims = new int[Nav.NX * Nav.NZ];
        Goals = FlowField.Goals(Nav);
        Gate = NearestStand(s.KeepX - s.KeepR - 12, s.KeepZ, 4);
        FindMines();
        PlanWave(1);
        Stock[Stockpile.Stone] = 16 * VoxelWorld.PerM3;
        Stock[Stockpile.Wood] = 24 * VoxelWorld.PerM3;
        Stock[Stockpile.Earth] = 16 * VoxelWorld.PerM3;
        w.Replaced += OnWaterEdit;
        w.Replaced += OnSupportEdit;
        w.Replaced += OnTrapEdit;
        // from here on, what is written to the valley is what a save has to keep
        w.TrackEdits = true;
    }

    public void Say(string text, int kind = 0) => Said?.Invoke(text, kind, float.NaN, float.NaN);

    /// <summary>A line for the chronicle about something that happened at a place: a click on it there takes the camera to it.</summary>
    public void Say(string text, int kind, float x, float z) => Said?.Invoke(text, kind, x, z);

    public void Say(string text, int kind, Unit at) => Say(text, kind, at.X, at.Z);

    /// <summary>The standable cell nearest to a voxel column (searching outward ring by ring).</summary>
    public int NearestStand(int x, int z, int h)
    {
        int ci = x >> NavGrid.Shift, ck = z >> NavGrid.Shift;
        for (int r = 0; r < 24; r++)
            for (int dk = -r; dk <= r; dk++)
                for (int di = -r; di <= r; di++)
                {
                    if (Math.Max(Math.Abs(di), Math.Abs(dk)) != r) continue;
                    int i = ci + di, k = ck + dk;
                    if (i < 0 || k < 0 || i >= Nav.NX || k >= Nav.NZ) continue;
                    for (int y = Nav.NY - 1; y >= 1; y--)
                        if (Nav.Stand(i, y, k, h)) return Nav.Id(i, y, k);
                }
        return -1;
    }

    public Unit Spawn(UnitType t, int cell)
    {
        var u = new Unit { Id = _nextId++, T = t, Hp = t.Hp, MaxHp = t.Hp, Cell = cell };
        u.Look = Bodies.LookOf(t, u.Id);
        (u.X, u.Y, u.Z) = Nav.Feet(cell);
        float lane = Math.Max(.5f, NavGrid.CS / 2f - t.BodyR - .5f);
        u.OffX = (float)(Rng.NextDouble() * 2 - 1) * lane; u.OffZ = (float)(Rng.NextDouble() * 2 - 1) * lane;
        u.Pace = .9f + (float)Rng.NextDouble() * .2f;
        u.X += u.OffX; u.Z += u.OffZ;
        u.Facing = (float)(Rng.NextDouble() * Math.PI * 2);
        if (t.Team == Team.Player && t.Work < 1) u.Post = cell;
        // a priest does no work of the pawns', nor a hound or a rider
        if (t.Prays || t.Work <= 0) Array.Fill(u.Priority, (byte)0);
        // some of the horde come with torches, picked by the raider (not by the dice, which the rest of the valley shares)
        u.Torch = t.TorchShare > 0 && Hash.H3(u.Id, 11, 7) < t.TorchShare;
        // from the fifth wave some of the sappers come with a charge of powder on their backs, half of them from the ninth
        u.Charge = t == Kinds.Sapper && Waves.Number >= 5 && Hash.H3(u.Id, 23, 5) < (Waves.Number >= 9 ? .5f : .3f);
        Units.Add(u);
        return u;
    }

    /// <summary>
    /// Takes on a pawn at the citadel's gate at once, if the treasury allows: for the tests and the scripted scenes. The
    /// player takes them on through the citadel's line (<see cref="Enlist"/>), which takes time and wants places.
    /// </summary>
    public Unit Hire(UnitType t)
    {
        int cost = HireCost(t);
        if (Stock.Gold < cost || Gate < 0) return null;
        Stock.Gold -= cost;
        var u = Spawn(t, Gate);
        Outfit(u);
        Say($"{t.Name} пришёл к воротам.");
        return u;
    }

    public void Step(float dt)
    {
        // the pawns' work and the horde's are not the player's hand: none of it goes into a marking being drawn
        var hand = Orders.Writing;
        Orders.Writing = null;
        try { StepWorld(dt); }
        finally { Orders.Writing = hand; }
    }

    void StepWorld(float dt)
    {
        if (Over) return;
        Time += dt;
        TickWater(dt);
        TickFire(dt);
        TickCharges(dt);
        TickLight();
        TickSpells();
        TickCurses();
        TickCollapse(dt);
        TickFlight(dt);
        TickGates(dt);
        TickDrawbridges(dt);
        TickDecor();
        TickMines(dt);
        if (Nav.PendingEdits > 0) Nav.Flush();
        UpdateFields();
        Waves.Tick(this, dt);
        TickBands();
        TickTrails();
        TendWildlife();
        TickShelter();
        TickMuster(dt);
        TickDwellings();
        TickTraps();
        TickEventsHalf(Time);
        CountBodies();
        for (int n = 0; n < Units.Count; n++)
        {
            var u = Units[n];
            if (u.Dead) continue;
            // in the citadel: out of the world till it comes out
            if (u.Inside) { if (u.Team == Team.Horde) Imprisoned(u, dt); else Indoors(u, dt); continue; }
            u.Swing = Math.Max(0, u.Swing - dt * 3);
            // in the air: nothing to do but come down
            if (u.Airborne != null) continue;
            u.Cooldown -= dt;
            Settle(u);
            if (u == Possessed) PossessedTick(u, dt);
            else if (u.Team == Team.Player) PawnTick(u, dt);
            else if (u.Team == Team.Horde) RaiderTick(u, dt);
            else WildTick(u, dt);
        }
        Separate();
        Unstack();
        TickArrows(dt);
        KeepPossession();
        TickBleeding(dt);
        Units.RemoveAll(u => u.Dead && Time - u.DiedAt > 30);
        if (KeepHp <= 0 && !Over) { Over = true; Say("Цитадель пала.", 3); }
    }

    /// <summary>Marks the cells bodies stand in, so nothing gets built into anyone.</summary>
    void CountBodies()
    {
        Array.Clear(_bodies);
        foreach (var u in Units)
            if (!u.Dead && !u.Inside && u.Airborne == null)
                for (int d = 0; d < u.T.Move.Height && Nav.Y(u.Cell) + d < Nav.NY; d++) _bodies[u.Cell + d * Nav.Layer]++;
    }

    // ------------------------------------------------------------ moving

    /// <summary>If the ground went from under its feet, it falls to the first footing below; out of deep water it swims ashore.</summary>
    void Settle(Unit u)
    {
        int h = u.T.Move.Height, i = Nav.I(u.Cell), k = Nav.K(u.Cell), y = Nav.Y(u.Cell);
        if (u.Team == Team.Horde && Nav.Fill[u.Cell] >= NavGrid.SolidT) return;   // standing in its own tunnel face
        if (Nav.Fill[u.Cell] >= NavGrid.SolidT || Nav.Deep(u.Cell))
        {
            // walled in, or the water rose over it: out to the nearest free footing
            int c = NearestStand((int)u.X, (int)u.Z, h);
            if (c >= 0) { u.Cell = c; (u.X, u.Y, u.Z) = Nav.Feet(c); u.Path = null; StopCarve(u); StopBuild(u); }
            return;
        }
        // a horse or a hound does not hang on a ladder: it comes off it
        if (Nav.Stand(i, y, k, h) || (y >= 1 && Nav.Solid(i, y - 1, k)) || Nav.Ladder[u.Cell] != 0 && u.T.Move.Ladders) return;
        for (int ny = y - 1; ny >= 1; ny--)
            if (Nav.Solid(i, ny - 1, k))
            {
                int fall = y - ny;
                u.Cell = Nav.Id(i, ny, k);
                u.Y = Nav.Floor[u.Cell];
                u.Path = null;
                StopCarve(u); StopBuild(u);
                if (fall >= 4) Hurt(u, null, (fall - 3) * 12, "падение");
                return;
            }
    }

    /// <summary>
    /// Walks along its path at a pace (1 is full speed); true when it has arrived (or the path broke and needs replanning).
    /// Ladders are climbed at half speed, shallow water is waded slowly.
    /// </summary>
    bool Walk(Unit u, float dt, float pace = 1)
    {
        if (u.Path == null || u.PathI >= u.Path.Count) { u.Path = null; return true; }
        int next = u.Path[u.PathI];
        if (u.Team == Team.Player && Gates.Count > 0)
            // knock ahead, so the gate is opening by the time we are there
            for (int n = u.PathI + 1; n < Math.Min(u.Path.Count, u.PathI + 4); n++) Knock(u.Path[n]);
        if (!Passable(u, next))
        {
            // a shut gate of ours opens for us: wait for it
            if (u.Team == Team.Player && Knock(next)) { u.State = UnitState.Idle; return false; }
            u.Path = null;
            return true;
        }
        var (tx, ty, tz) = Nav.Feet(next);
        bool climbing = Nav.I(next) == Nav.I(u.Cell) && Nav.K(next) == Nav.K(u.Cell) && next != u.Cell;
        if (climbing) { tx = u.X; tz = u.Z; ty = (Nav.Y(next) << NavGrid.Shift) + (Nav.Solid(Nav.I(next), Nav.Y(next) - 1, Nav.K(next)) ? Nav.Floor[next] - (Nav.Y(next) << NavGrid.Shift) : 0); }
        else { tx += u.OffX; tz += u.OffZ; }
        float speed = u.T.Move.Speed * NavGrid.CS * pace * u.Pace * CartPace(u) * TroopPace(u) * (u.Hp < u.MaxHp * .4f ? .7f : 1) * (Nav.Water[u.Cell] > 0 ? .6f : 1)
            * (Nav.Spikes[u.Cell] > 0 ? .4f : 1) * Gait(u) * (Mires.Count > 0 && InMire(u.X, u.Z) ? .35f : 1)
            * (DeadFogs.Count > 0 && u.Team == Team.Horde && !Deathless(u) && InDeadFog(u.X, u.Z) ? .5f : 1);
        float step = speed * dt * (climbing ? .5f : 1);
        float dx = tx - u.X, dz = tz - u.Z, dyv = ty - u.Y;
        float dist = climbing ? MathF.Abs(dyv) : MathF.Sqrt(dx * dx + dz * dz);
        u.State = u.State == UnitState.Flee ? UnitState.Flee : UnitState.Walk;
        if (!climbing && dist > .01f) u.Facing = MathF.Atan2(dz, dx);
        u.Stride += step;
        if (dist <= step)
        {
            u.X = tx; u.Z = tz; u.Y = ty;
            u.Cell = next;
            u.PathI++;
            return u.PathI >= u.Path.Count;
        }
        // in a crowd going the same way bodies push each other off the very spot they aim for: inside the cell will do
        if (!climbing && dist < NavGrid.CS * .5f && ((int)MathF.Floor(u.X) >> NavGrid.Shift) == Nav.I(next) && ((int)MathF.Floor(u.Z) >> NavGrid.Shift) == Nav.K(next)
            && MathF.Abs(dyv) < 2)
        {
            u.Y = ty;
            u.Cell = next;
            u.PathI++;
            return u.PathI >= u.Path.Count;
        }
        if (climbing) { u.Y += MathF.Sign(dyv) * step; return false; }
        u.X += dx / dist * step;
        u.Z += dz / dist * step;
        u.Y += dyv * Math.Min(1, step / dist);
        return false;
    }

    bool Passable(Unit u, int c)
    {
        int i = Nav.I(c), y = Nav.Y(c), k = Nav.K(c);
        if (Nav.Deep(c)) return false;
        return u.Team == Team.Horde ? Nav.Solid(i, y - 1, k) || Nav.Ladder[c] != 0 : Nav.CanBe(i, y, k, u.T.Move.Height, ladders: u.T.Move.Ladders);
    }

    /// <summary>
    /// Bodies keep a little apart: two that come closer than their shoulders allow are eased away from each other,
    /// the bigger one less. Someone standing still is kept inside its own cell, so where it stands stays true.
    /// </summary>
    void Separate()
    {
        for (int a = 0; a < Units.Count; a++)
        {
            var u = Units[a];
            if (u.Dead || u.Inside || u.Airborne != null || Nav.Ladder[u.Cell] != 0) continue;
            for (int b = a + 1; b < Units.Count; b++)
            {
                var v = Units[b];
                if (v.Dead || v.Inside || v.Airborne != null || Nav.Ladder[v.Cell] != 0) continue;
                float dx = v.X - u.X, dz = v.Z - u.Z, min = u.T.BodyR + v.T.BodyR + .5f;
                if (dx * dx + dz * dz >= min * min || MathF.Abs(v.Y - u.Y) > Math.Min(u.T.BodyH, v.T.BodyH) * .8f) continue;
                float d = MathF.Sqrt(dx * dx + dz * dz);
                if (d < .01f) { dx = u.Id < v.Id ? 1 : -1; dz = .3f; d = MathF.Sqrt(dx * dx + dz * dz); }
                float push = (min - d) * .5f, wu = v.T.BodyR / (u.T.BodyR + v.T.BodyR) * 2, wv = 2 - wu;
                Nudge(u, -dx / d * push * wu, -dz / d * push * wu);
                Nudge(v, dx / d * push * wv, dz / d * push * wv);
            }
        }
    }

    double _nextUnstack;
    readonly Dictionary<int, Unit> _stood = new();
    static readonly int[] DX8 = { 1, -1, 0, 0, 1, 1, -1, -1 }, DK8 = { 0, 0, 1, -1, 1, -1, 1, -1 };

    /// <summary>
    /// Two standing in one cell can't be eased apart (each is kept inside its own cell): the one met second steps into
    /// a free cell beside. So a herd, or pawns that idle at one spot, don't stand in one heap.
    /// </summary>
    void Unstack()
    {
        if (Time < _nextUnstack) return;
        _nextUnstack = Time + .5;
        _stood.Clear();
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null || u == Possessed || u.State != UnitState.Idle || u.Path != null && u.PathI < u.Path.Count || Nav.Ladder[u.Cell] != 0) continue;
            if (u.Team == Team.Player && u.Site >= 0) continue;
            if (_stood.TryAdd(u.Cell, u)) continue;
            int c = FreeBeside(u);
            if (c < 0) continue;
            u.Path ??= new List<int>();
            // one that stands where it was sent stands beside it now; one still on its way keeps going there
            if (u.Dest == u.Cell) u.Dest = c;
            u.Path.Clear(); u.Path.Add(c); u.PathI = 0;
            _stood[c] = u;
        }
    }

    /// <summary>A free cell to stand in right beside a unit, or −1.</summary>
    int FreeBeside(Unit u)
    {
        int i = Nav.I(u.Cell), y = Nav.Y(u.Cell), k = Nav.K(u.Cell), h = u.T.Move.Height, start = Rng.Next(8);
        for (int n = 0; n < 8; n++)
        {
            int d = (start + n) % 8, ni = i + DX8[d], nk = k + DK8[d];
            for (int dy = 0; dy >= -1; dy--)
            {
                if (!Nav.Stand(ni, y + dy, nk, h, gates: false)) continue;
                int c = Nav.Id(ni, y + dy, nk);
                if (_bodies[c] > 0 || _stood.ContainsKey(c) || Nav.Water[c] > 0) break;
                return c;
            }
        }
        return -1;
    }

    void Nudge(Unit u, float dx, float dz)
    {
        u.X += dx; u.Z += dz;
        if (u.Path != null && u.PathI < u.Path.Count) return;
        // the one gone into may stand right at the edge of its cell, but is not shoved out of it into a wall or over a drop
        if (u == Possessed)
        {
            int px0 = Nav.I(u.Cell) << NavGrid.Shift, pz0 = Nav.K(u.Cell) << NavGrid.Shift;
            u.X = Math.Clamp(u.X, px0 + .05f, px0 + NavGrid.CS - .05f);
            u.Z = Math.Clamp(u.Z, pz0 + .05f, pz0 + NavGrid.CS - .05f);
            return;
        }
        float r = Math.Min(u.T.BodyR, NavGrid.CS / 2f - .5f);
        int x0 = Nav.I(u.Cell) << NavGrid.Shift, z0 = Nav.K(u.Cell) << NavGrid.Shift;
        u.X = Math.Clamp(u.X, x0 + r, x0 + NavGrid.CS - r);
        u.Z = Math.Clamp(u.Z, z0 + r, z0 + NavGrid.CS - r);
    }

    /// <summary>Whether a unit is hanging on a ladder rather than standing.</summary>
    public bool Climbing(Unit u) => !u.Dead && Nav.Ladder[u.Cell] != 0 && !Nav.Solid(Nav.I(u.Cell), Nav.Y(u.Cell) - 1, Nav.K(u.Cell));

    float H(int c, int ti, int tk) => PathFinder.Octile(Nav.I(c) - ti, Nav.K(c) - tk) * .95f;

    /// <summary>Plans a walk to a cell (or next to it), keeping to a lane if given; false if there is no way.</summary>
    public bool GoTo(Unit u, int target, bool adjacent = false, int budget = 40000, Func<int, float> lane = null)
    {
        int ti = Nav.I(target), tk = Nav.K(target), ty = Nav.Y(target);
        Func<int, bool> goal = adjacent
            ? c => Math.Abs(Nav.I(c) - ti) <= 1 && Math.Abs(Nav.K(c) - tk) <= 1 && Math.Abs(Nav.Y(c) - ty) <= 2
            : c => c == target;
        if (goal(u.Cell)) { u.Path = null; return true; }
        var path = Paths.Find(u.Cell, goal, c => H(c, ti, tk), u.T.Move, budget, gates: u.Team == Team.Player, lane: lane);
        if (path == null) return false;
        u.Path = path; u.PathI = 0;
        // a way somewhere on purpose is no stroll
        if (u.Pastime == Pastime.Stroll) u.Pastime = Pastime.None;
        return true;
    }

    // ------------------------------------------------------------ the player's commands

    /// <summary>
    /// Sends pawns to a spot and keeps them there; soldiers make it their post. A group goes as one: each to a place
    /// of its own in ranks across the way they go, keeping to its own line on the way, so they walk side by side.
    /// </summary>
    public int Command(IEnumerable<Unit> units, int target)
    {
        var group = new List<Unit>();
        foreach (var u in units) if (!u.Dead && u.Team == Team.Player) group.Add(u);
        var (spots, lanes) = Formation(group, target);
        int ok = 0;
        for (int n = 0; n < group.Count; n++)
        {
            var u = group[n];
            // sent out of the citadel: out of its door first
            if (u.Inside) { u.Mending = false; Leave(u); }
            u.Refuge = -1;
            ReleaseSite(u);
            Busy(u);
            int spot = spots[n];
            u.Hold = true;
            u.Dest = spot;
            u.Prey = null;
            if (u.T.Work < 1) u.Post = spot;
            if (GoTo(u, spot, lane: lanes[n]) || GoTo(u, spot, adjacent: true, lane: lanes[n])) ok++;
        }
        return ok;
    }

    /// <summary>How far apart a group sent somewhere stands, across and front to back, in voxels.</summary>
    const float FileGap = NavGrid.CS;

    /// <summary>
    /// Where each of a group goes when sent somewhere together: places a metre apart in a few ranks across the way they
    /// go, the nearest to the spot in front and in every rank the ones on the left to the left, so nobody crosses
    /// another's way; and for each the line it keeps to on the way there.
    /// </summary>
    (int[] spots, Func<int, float>[] lanes) Formation(List<Unit> group, int target)
    {
        int n = group.Count;
        var spots = new int[n];
        var lanes = new Func<int, float>[n];
        if (n <= 1) { Array.Fill(spots, target); return (spots, lanes); }
        var (tx, _, tz) = Nav.Feet(target);
        float cx = 0, cz = 0;
        foreach (var u in group) { cx += u.X; cz += u.Z; }
        cx /= n; cz /= n;
        // the way they go, and across it
        float fx = tx - cx, fz = tz - cz, fl = MathF.Sqrt(fx * fx + fz * fz);
        if (fl < FileGap) { fx = 1; fz = 0; } else { fx /= fl; fz /= fl; }
        float rx = -fz, rz = fx;
        int cols = n <= 5 ? n : Math.Max(4, (int)MathF.Ceiling(MathF.Sqrt(n * 2f)));
        var order = new List<int>();
        for (int i = 0; i < n; i++) order.Add(i);
        order.Sort((a, b) => ((group[b].X - cx) * fx + (group[b].Z - cz) * fz).CompareTo((group[a].X - cx) * fx + (group[a].Z - cz) * fz));
        var taken = new HashSet<int>();
        int level = Nav.Y(target);
        for (int from = 0; from < n; from += cols)
        {
            var rank = order.GetRange(from, Math.Min(cols, n - from));
            rank.Sort((a, b) => ((group[a].X - cx) * rx + (group[a].Z - cz) * rz).CompareTo((group[b].X - cx) * rx + (group[b].Z - cz) * rz));
            float back = from / cols * FileGap;
            for (int k = 0; k < rank.Count; k++)
            {
                var u = group[rank[k]];
                float side = (k - (rank.Count - 1) / 2f) * FileGap;
                int spot = PlaceNear(tx + rx * side - fx * back, tz + rz * side - fz * back, level, u.T.Move.Height, taken);
                if (spot < 0) spot = Spread(target, taken.Count, u.T.Move.Height);
                spots[rank[k]] = spot;
                taken.Add(spot);
                // a little dearer every step off its line: of ways alike it takes the one along its own
                float line = side;
                lanes[rank[k]] = c =>
                {
                    float x = (Nav.I(c) + .5f) * NavGrid.CS - tx, z = (Nav.K(c) + .5f) * NavGrid.CS - tz;
                    return MathF.Min(MathF.Abs(x * rx + z * rz - line) / NavGrid.CS, 2) * .1f;
                };
            }
        }
        return (spots, lanes);
    }

    /// <summary>A free cell to stand in nearest a point, at about the level given; −1 if there is none close by.</summary>
    int PlaceNear(float x, float z, int level, int h, HashSet<int> taken)
    {
        int ci = (int)MathF.Floor(x) >> NavGrid.Shift, ck = (int)MathF.Floor(z) >> NavGrid.Shift;
        for (int r = 0; r < 4; r++)
            for (int dk = -r; dk <= r; dk++)
                for (int di = -r; di <= r; di++)
                {
                    if (Math.Max(Math.Abs(di), Math.Abs(dk)) != r) continue;
                    for (int dy = 1; dy >= -2; dy--)
                    {
                        int i = ci + di, y = level + dy, k = ck + dk;
                        if (!Nav.Stand(i, y, k, h)) continue;
                        int c = Nav.Id(i, y, k);
                        if (taken.Contains(c) || Nav.Deep(c)) continue;
                        return c;
                    }
                }
        return -1;
    }

    /// <summary>Sets a pawn's priority for a kind of work; if it was at that work and now may not, it leaves it.</summary>
    public void SetPriority(Unit u, WorkKind k, byte priority)
    {
        u.Priority[(int)k] = Math.Clamp(priority, (byte)0, (byte)4);
        if (u.Site >= 0 && (u.SiteWork & Work.Allowed(u)) != u.SiteWork) ReleaseSite(u);
        u.NextSearch = 0;
    }

    /// <summary>Tells pawns to bring in a resource by themselves when nothing marked needs them (or none, to stop).</summary>
    public void SetDuty(IEnumerable<Unit> units, int kind)
    {
        foreach (var u in units)
        {
            if (u.Dead || u.Team != Team.Player || u.T.Prays) continue;
            u.Duty = kind;
            // sent to bring it in, it does that work, whatever else it was set to
            if (kind != Stockpile.None && !Work.Does(u, Work.For(kind))) u.Priority[(int)Work.For(kind)] = Work.Usual;
            u.DutyZone = null;
            u.Hold = false; u.Dest = -1; u.Prey = null;
            u.NextSearch = 0;
            Busy(u);
        }
    }

    /// <summary>Calls workers up to fight like swordsmen, or lets them go back to running from the horde.</summary>
    public void SetMilitia(IEnumerable<Unit> units, bool on)
    {
        foreach (var u in units) if (!u.Dead && u.Team == Team.Player && u.T.Work >= 1 && !u.T.Prays) u.Militia = on;
    }

    /// <summary>Lets pawns go back to work by themselves.</summary>
    public void ReleaseHold(IEnumerable<Unit> units)
    {
        foreach (var u in units) { u.Hold = false; u.Dest = -1; u.Prey = null; u.NextSearch = 0; Busy(u); }
    }

    /// <summary>The horn: soldiers drop the hunt and whatever else they were at and hurry to their posts.</summary>
    public void Recall()
    {
        foreach (var u in Units)
            if (!u.Dead && u.Team == Team.Player && u.T.Work < 1 && u.Post >= 0 && u.T != Kinds.Risen)
            {
                ReleaseSite(u);
                Busy(u);
                u.Prey = null;
                GoTo(u, u.Post);
            }
    }

    /// <summary>Spots around a target for a group, so they don't all stand in one cell.</summary>
    int Spread(int target, int n, int h)
    {
        if (n == 0) return target;
        int i = Nav.I(target), y = Nav.Y(target), k = Nav.K(target);
        for (int r = 1; r < 5; r++)
            for (int dk = -r; dk <= r; dk++)
                for (int di = -r; di <= r; di++)
                {
                    if (Math.Max(Math.Abs(di), Math.Abs(dk)) != r) continue;
                    for (int dy = 1; dy >= -2; dy--)
                        if (Nav.Stand(i + di, y + dy, k + dk, h) && --n == 0) return Nav.Id(i + di, y + dy, k + dk);
                }
        return target;
    }
}
