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
    public event Action<string, int> Said;

    public Sim(VoxelWorld w, Site s)
    {
        World = w; Site = s;
        Nav = new NavGrid(w);
        Nav.BuildAll();
        Orders = new Orders(w, s);
        Paths = new PathFinder(Nav);
        _bodies = new int[Nav.Count];
        _claims = new int[Nav.NX * Nav.NZ];
        Goals = FlowField.Goals(Nav);
        Gate = NearestStand(s.KeepX - s.KeepR - 12, s.KeepZ, 4);
        Stock[Stockpile.Stone] = 16 * VoxelWorld.PerM3;
        Stock[Stockpile.Wood] = 24 * VoxelWorld.PerM3;
        Stock[Stockpile.Earth] = 16 * VoxelWorld.PerM3;
        w.Replaced += OnWaterEdit;
    }

    public void Say(string text, int kind = 0) => Said?.Invoke(text, kind);

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
        var u = new Unit { Id = _nextId++, T = t, Hp = t.Hp, Cell = cell };
        (u.X, u.Y, u.Z) = Nav.Feet(cell);
        float lane = Math.Max(.5f, NavGrid.CS / 2f - t.BodyR - .5f);
        u.OffX = (float)(Rng.NextDouble() * 2 - 1) * lane; u.OffZ = (float)(Rng.NextDouble() * 2 - 1) * lane;
        u.Pace = .9f + (float)Rng.NextDouble() * .2f;
        u.X += u.OffX; u.Z += u.OffZ;
        u.Facing = (float)(Rng.NextDouble() * Math.PI * 2);
        if (t.Team == Team.Player && t.Work < 1) u.Post = cell;
        Units.Add(u);
        return u;
    }

    /// <summary>Takes on a pawn at the citadel's gate if the treasury allows.</summary>
    public Unit Hire(UnitType t)
    {
        if (Stock.Gold < t.Cost || Gate < 0) return null;
        Stock.Gold -= t.Cost;
        var u = Spawn(t, Gate);
        Say($"{t.Name} пришёл к воротам.");
        return u;
    }

    public void Step(float dt)
    {
        if (Over) return;
        Time += dt;
        TickWater(dt);
        TickGates(dt);
        if (Nav.PendingEdits > 0) Nav.Flush();
        UpdateFields();
        Waves.Tick(this, dt);
        TendWildlife();
        CountBodies();
        for (int n = 0; n < Units.Count; n++)
        {
            var u = Units[n];
            if (u.Dead) continue;
            u.Swing = Math.Max(0, u.Swing - dt * 3);
            u.Cooldown -= dt;
            Settle(u);
            if (u.Team == Team.Player) PawnTick(u, dt);
            else if (u.Team == Team.Horde) RaiderTick(u, dt);
            else WildTick(u, dt);
        }
        Separate();
        TickArrows(dt);
        Units.RemoveAll(u => u.Dead && Time - u.DiedAt > 30);
        if (KeepHp <= 0 && !Over) { Over = true; Say("Цитадель пала.", 3); }
    }

    /// <summary>Marks the cells bodies stand in, so nothing gets built into anyone.</summary>
    void CountBodies()
    {
        Array.Clear(_bodies);
        foreach (var u in Units)
            if (!u.Dead)
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
        if (Nav.Stand(i, y, k, h) || (y >= 1 && Nav.Solid(i, y - 1, k)) || Nav.Ladder[u.Cell] != 0) return;
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
        float speed = u.T.Move.Speed * NavGrid.CS * pace * u.Pace * (u.Hp < u.T.Hp * .4f ? .7f : 1) * (Nav.Water[u.Cell] > 0 ? .6f : 1);
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
        return u.Team == Team.Horde ? Nav.Solid(i, y - 1, k) || Nav.Ladder[c] != 0 : Nav.CanBe(i, y, k, u.T.Move.Height);
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
            if (u.Dead || Nav.Ladder[u.Cell] != 0) continue;
            for (int b = a + 1; b < Units.Count; b++)
            {
                var v = Units[b];
                if (v.Dead || Nav.Ladder[v.Cell] != 0) continue;
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

    void Nudge(Unit u, float dx, float dz)
    {
        u.X += dx; u.Z += dz;
        if (u.Path != null && u.PathI < u.Path.Count) return;
        float r = Math.Min(u.T.BodyR, NavGrid.CS / 2f - .5f);
        int x0 = Nav.I(u.Cell) << NavGrid.Shift, z0 = Nav.K(u.Cell) << NavGrid.Shift;
        u.X = Math.Clamp(u.X, x0 + r, x0 + NavGrid.CS - r);
        u.Z = Math.Clamp(u.Z, z0 + r, z0 + NavGrid.CS - r);
    }

    /// <summary>Whether a unit is hanging on a ladder rather than standing.</summary>
    public bool Climbing(Unit u) => !u.Dead && Nav.Ladder[u.Cell] != 0 && !Nav.Solid(Nav.I(u.Cell), Nav.Y(u.Cell) - 1, Nav.K(u.Cell));

    float H(int c, int ti, int tk) => PathFinder.Octile(Nav.I(c) - ti, Nav.K(c) - tk) * .95f;

    /// <summary>Plans a walk to a cell (or next to it); false if there is no way.</summary>
    public bool GoTo(Unit u, int target, bool adjacent = false, int budget = 40000)
    {
        int ti = Nav.I(target), tk = Nav.K(target), ty = Nav.Y(target);
        Func<int, bool> goal = adjacent
            ? c => Math.Abs(Nav.I(c) - ti) <= 1 && Math.Abs(Nav.K(c) - tk) <= 1 && Math.Abs(Nav.Y(c) - ty) <= 2
            : c => c == target;
        if (goal(u.Cell)) { u.Path = null; return true; }
        var path = Paths.Find(u.Cell, goal, c => H(c, ti, tk), u.T.Move, budget, gates: u.Team == Team.Player);
        if (path == null) return false;
        u.Path = path; u.PathI = 0;
        return true;
    }

    // ------------------------------------------------------------ the player's commands

    /// <summary>Sends pawns to a spot and keeps them there; soldiers make it their post.</summary>
    public int Command(IEnumerable<Unit> units, int target)
    {
        int ok = 0, n = 0;
        foreach (var u in units)
        {
            if (u.Dead || u.Team != Team.Player) continue;
            ReleaseSite(u);
            Busy(u);
            int spot = Spread(target, n++, u.T.Move.Height);
            u.Hold = true;
            u.Dest = spot;
            u.Prey = null;
            if (u.T.Work < 1) u.Post = spot;
            if (GoTo(u, spot) || GoTo(u, spot, adjacent: true)) ok++;
        }
        return ok;
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
            if (!u.Dead && u.Team == Team.Player && u.T.Work < 1 && u.Post >= 0)
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
