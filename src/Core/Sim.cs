using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The game without the engine: the valley, its navigation, the player's orders, the stores, the pawns and the horde.
/// Step it with game seconds; the Godot layer only draws what is here and turns clicks into calls on it.
/// </summary>
public sealed partial class Sim
{
    public readonly VoxelWorld World;
    public readonly Site Site;
    public readonly NavGrid Nav;
    public readonly Orders Orders;
    public readonly Stockpile Stock = new();
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
        Stock[Stockpile.Stone] = 2 * VoxelWorld.PerM3;
        Stock[Stockpile.Wood] = 3 * VoxelWorld.PerM3;
        Stock[Stockpile.Earth] = 2 * VoxelWorld.PerM3;
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
        u.X += (float)(Rng.NextDouble() - .5) * 3; u.Z += (float)(Rng.NextDouble() - .5) * 3;
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
        if (Nav.PendingEdits > 0) Nav.Flush();
        UpdateFields();
        Waves.Tick(this, dt);
        CountBodies();
        for (int n = 0; n < Units.Count; n++)
        {
            var u = Units[n];
            if (u.Dead) continue;
            u.Swing = Math.Max(0, u.Swing - dt * 3);
            u.Cooldown -= dt;
            Settle(u);
            if (u.Team == Team.Player) PawnTick(u, dt); else RaiderTick(u, dt);
        }
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

    /// <summary>If the ground went from under its feet, it falls to the first footing below.</summary>
    void Settle(Unit u)
    {
        int h = u.T.Move.Height, i = Nav.I(u.Cell), k = Nav.K(u.Cell), y = Nav.Y(u.Cell);
        if (u.Team == Team.Horde && Nav.Fill[u.Cell] >= NavGrid.SolidT) return;   // standing in its own tunnel face
        if (Nav.Fill[u.Cell] >= NavGrid.SolidT)
        {
            // walled in: step out to the nearest free footing
            int c = NearestStand((int)u.X, (int)u.Z, h);
            if (c >= 0) { u.Cell = c; (u.X, u.Y, u.Z) = Nav.Feet(c); u.Path = null; }
            return;
        }
        if (Nav.Stand(i, y, k, h) || (y >= 1 && Nav.Solid(i, y - 1, k))) return;
        for (int ny = y - 1; ny >= 1; ny--)
            if (Nav.Solid(i, ny - 1, k))
            {
                int fall = y - ny;
                u.Cell = Nav.Id(i, ny, k);
                u.Y = Nav.Floor[u.Cell];
                u.Path = null;
                if (fall >= 4) Hurt(u, null, (fall - 3) * 12, "падение");
                return;
            }
    }

    /// <summary>Walks along its path; true when it has arrived (or the path broke and needs replanning).</summary>
    bool Walk(Unit u, float dt)
    {
        if (u.Path == null || u.PathI >= u.Path.Count) { u.Path = null; return true; }
        int next = u.Path[u.PathI];
        if (!Passable(u, next)) { u.Path = null; return true; }
        var (tx, ty, tz) = Nav.Feet(next);
        float step = u.T.Move.Speed * NavGrid.CS * dt * (u.Hp < u.T.Hp * .4f ? .7f : 1);
        float dx = tx - u.X, dz = tz - u.Z, dist = MathF.Sqrt(dx * dx + dz * dz);
        u.State = u.State == UnitState.Flee ? UnitState.Flee : UnitState.Walk;
        if (dist > .01f) u.Facing = MathF.Atan2(dz, dx);
        u.Stride += step;
        if (dist <= step)
        {
            u.X = tx; u.Z = tz; u.Y = ty;
            u.Cell = next;
            u.PathI++;
            return u.PathI >= u.Path.Count;
        }
        u.X += dx / dist * step;
        u.Z += dz / dist * step;
        u.Y += (ty - u.Y) * Math.Min(1, step / dist);
        return false;
    }

    bool Passable(Unit u, int c)
    {
        int i = Nav.I(c), y = Nav.Y(c), k = Nav.K(c);
        return u.Team == Team.Horde ? Nav.Solid(i, y - 1, k) : Nav.Stand(i, y, k, u.T.Move.Height);
    }

    float H(int c, int ti, int tk) => PathFinder.Octile(Nav.I(c) - ti, Nav.K(c) - tk) * .95f;

    /// <summary>Plans a walk to a cell (or next to it); false if there is no way.</summary>
    public bool GoTo(Unit u, int target, bool adjacent = false, int budget = 40000)
    {
        int ti = Nav.I(target), tk = Nav.K(target), ty = Nav.Y(target);
        Func<int, bool> goal = adjacent
            ? c => Math.Abs(Nav.I(c) - ti) <= 1 && Math.Abs(Nav.K(c) - tk) <= 1 && Math.Abs(Nav.Y(c) - ty) <= 2
            : c => c == target;
        if (goal(u.Cell)) { u.Path = null; return true; }
        var path = Paths.Find(u.Cell, goal, c => H(c, ti, tk), u.T.Move, budget);
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
            int spot = Spread(target, n++);
            u.Hold = true;
            if (u.T.Work < 1) u.Post = spot;
            if (GoTo(u, spot) || GoTo(u, spot, adjacent: true)) ok++;
        }
        return ok;
    }

    /// <summary>Lets pawns go back to work by themselves.</summary>
    public void ReleaseHold(IEnumerable<Unit> units)
    {
        foreach (var u in units) { u.Hold = false; u.NextSearch = 0; }
    }

    /// <summary>The horn: soldiers hurry to their posts.</summary>
    public void Recall()
    {
        foreach (var u in Units)
            if (!u.Dead && u.Team == Team.Player && u.T.Work < 1 && u.Post >= 0)
            {
                ReleaseSite(u);
                GoTo(u, u.Post);
            }
    }

    /// <summary>Spots around a target for a group, so they don't all stand in one cell.</summary>
    int Spread(int target, int n)
    {
        if (n == 0) return target;
        int i = Nav.I(target), y = Nav.Y(target), k = Nav.K(target);
        for (int r = 1; r < 5; r++)
            for (int dk = -r; dk <= r; dk++)
                for (int di = -r; di <= r; di++)
                {
                    if (Math.Max(Math.Abs(di), Math.Abs(dk)) != r) continue;
                    for (int dy = 1; dy >= -2; dy--)
                        if (Nav.Stand(i + di, y + dy, k + dk, 4) && --n == 0) return Nav.Id(i + di, y + dy, k + dk);
                }
        return target;
    }
}
