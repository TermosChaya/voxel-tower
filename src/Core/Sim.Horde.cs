using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoxelTower.Core;

public sealed partial class Sim
{
    /// <summary>Cells beside the citadel where raiders batter it.</summary>
    public readonly List<int> Goals;
    HashSet<int> _goalSet;

    /// <summary>The way to the citadel for each kind of raider; rebuilt in the background as the valley changes.</summary>
    public Dictionary<UnitType, FlowField> Fields { get; private set; } = new();
    Task<Dictionary<UnitType, FlowField>> _fieldTask;
    int _fieldVersion = -1;
    double _fieldAt = -99;
    public double LastFieldMs { get; private set; }

    /// <summary>Builds every raider's field now, on this thread (tests and the first wave).</summary>
    public void ComputeFields()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Fields = BuildFields();
        LastFieldMs = sw.Elapsed.TotalMilliseconds;
        _fieldVersion = Nav.Version; _fieldAt = Time;
    }

    Dictionary<UnitType, FlowField> BuildFields()
    {
        var d = new Dictionary<UnitType, FlowField>();
        foreach (var t in Kinds.HordeKinds) d[t] = FlowField.Compute(Nav, t.Move, Goals, 900);
        return d;
    }

    void UpdateFields()
    {
        if (_fieldTask != null)
        {
            if (!_fieldTask.IsCompleted) return;
            if (_fieldTask.Status == TaskStatus.RanToCompletion) Fields = _fieldTask.Result;
            _fieldTask = null;
        }
        bool needed = Waves.Active || Waves.Countdown < 12;
        if (!needed || (_fieldVersion == Nav.Version && Fields.Count > 0) || Time - _fieldAt < 2) return;
        // the grid keeps changing while this runs; a field built from a slightly stale picture is still a good guide
        _fieldVersion = Nav.Version; _fieldAt = Time;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _fieldTask = Task.Run(() => { var f = BuildFields(); LastFieldMs = sw.Elapsed.TotalMilliseconds; return f; });
    }

    // ------------------------------------------------------------ raiders

    public Unit SpawnRaider(UnitType t)
    {
        for (int tries = 0; tries < 12; tries++)
        {
            int z = 24 + Rng.Next(World.SZ - 48);
            int c = NearestStand(Site.SpawnStrip / 2, z, t.Move.Height);
            if (c >= 0) return Spawn(t, c);
        }
        return null;
    }

    void RaiderTick(Unit u, float dt)
    {
        _goalSet ??= new HashSet<int>(Goals);
        var foe = Nearest(u, Team.Player, u.T.Reach + 8);
        if (foe != null && InReach(u, foe))
        {
            u.State = UnitState.Fight;
            u.Facing = MathF.Atan2(foe.Z - u.Z, foe.X - u.X);
            if (u.Cooldown <= 0) { Strike(u, foe); u.Cooldown = u.T.AttackTime * (.9f + (float)Rng.NextDouble() * .2f); }
            return;
        }
        if (_goalSet.Contains(u.Cell))
        {
            u.State = UnitState.Siege;
            u.Facing = MathF.Atan2(Site.KeepZ - u.Z, Site.KeepX - u.X);
            if (u.Cooldown <= 0) { KeepHp -= u.T.KeepDamage; u.Swing = 1; u.Cooldown = u.T.AttackTime; }
            return;
        }
        if (u.CarveCell >= 0) { Carve(u, dt); return; }
        if (u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt); return; }
        if (!Fields.TryGetValue(u.T, out var f)) { u.State = UnitState.Idle; return; }
        int next = f.Next(Nav, u.Cell, out float carve);
        if (next < 0) { u.State = UnitState.Idle; return; }
        if (carve > 0) { StartCarve(u, next); return; }
        u.Path ??= new List<int>();
        u.Path.Clear(); u.Path.Add(next); u.PathI = 0;
        Walk(u, dt);
    }

    /// <summary>Lists the voxels in the way into the next cell (and above its head when climbing), nearest first.</summary>
    void StartCarve(Unit u, int next)
    {
        u.CarveCell = next; u.CarveList.Clear(); u.CarveI = 0; u.CarveEffort = 0;
        int h = u.T.Move.Height, qi = Nav.I(next), qy = Nav.Y(next), qk = Nav.K(next), py = Nav.Y(u.Cell);
        var cells = new List<int>();
        for (int y = qy; y < qy + h && y < Nav.NY; y++) cells.Add(Nav.Id(qi, y, qk));
        for (int y = py + h; y < py + h + (qy - py) && y < Nav.NY; y++) cells.Add(Nav.Id(Nav.I(u.Cell), y, Nav.K(u.Cell)));
        foreach (int c in cells)
        {
            if (Nav.Fill[c] < NavGrid.SolidT) continue;
            int x0 = Nav.I(c) << NavGrid.Shift, y0 = Nav.Y(c) << NavGrid.Shift, z0 = Nav.K(c) << NavGrid.Shift;
            for (int y = y0; y < y0 + NavGrid.CS; y++)
                for (int z = z0; z < z0 + NavGrid.CS; z++)
                    for (int x = x0; x < x0 + NavGrid.CS; x++)
                        if (Blocks(World.Get(x, y, z))) u.CarveList.Add((x, y, z));
        }
        float hx = u.X, hy = u.Y + u.T.BodyH * .6f, hz = u.Z;
        u.CarveList.Sort((a, b) =>
            ((a.x - hx) * (a.x - hx) + (a.y - hy) * (a.y - hy) + (a.z - hz) * (a.z - hz)).CompareTo((b.x - hx) * (b.x - hx) + (b.y - hy) * (b.y - hy) + (b.z - hz) * (b.z - hz)));
        u.State = UnitState.Carve;
    }

    static bool Blocks(byte m) => m != 0 && m != Mat.Leaf && m != Mat.Foliage && m != Mat.Tuft;

    static int Group(byte m) => Mat.Def[m].Group switch { MatGroup.Earth => 0, MatGroup.Wood => 1, MatGroup.Stone => 2, MatGroup.Rock => 3, _ => -1 };

    void Carve(Unit u, float dt)
    {
        u.State = UnitState.Carve;
        u.CarveEffort += dt;
        var rate = u.T.Move.Carve;
        var target = u.CarveCell;
        u.Facing = MathF.Atan2((Nav.K(target) << NavGrid.Shift) + 4 - u.Z, (Nav.I(target) << NavGrid.Shift) + 4 - u.X);
        int removed = 0;
        while (u.CarveI < u.CarveList.Count)
        {
            var (x, y, z) = u.CarveList[u.CarveI];
            byte m = World.Get(x, y, z);
            if (!Blocks(m)) { u.CarveI++; continue; }
            int g = Group(m);
            if (g < 0 || rate == null || rate[g] <= 0) { StopCarve(u); return; }
            float cost = 1f / rate[g];
            if (u.CarveEffort < cost) break;
            u.CarveEffort -= cost;
            u.CarveI++;
            World.Set(x, y, z, Mat.Air);
            if (Orders.At(x, y, z) == Orders.Remove) Orders.Clear(x, y, z);
            Hacked?.Invoke(x, y, z, m);
            removed++;
            u.Swing = 1;
        }
        if (u.CarveI >= u.CarveList.Count || (removed > 0 && WayOpen(u))) StopCarve(u);
    }

    /// <summary>Called for every voxel the horde hacks out, for chips.</summary>
    public Action<int, int, int, byte> Hacked;

    bool WayOpen(Unit u)
    {
        int h = u.T.Move.Height, c = u.CarveCell, qi = Nav.I(c), qy = Nav.Y(c), qk = Nav.K(c);
        for (int y = qy; y < qy + h && y < Nav.NY; y++)
        {
            int x0 = qi << NavGrid.Shift, y0 = y << NavGrid.Shift, z0 = qk << NavGrid.Shift, solid = 0;
            for (int yy = y0; yy < y0 + NavGrid.CS; yy += 2)
                for (int z = z0; z < z0 + NavGrid.CS; z += 2)
                    for (int x = x0; x < x0 + NavGrid.CS; x += 2)
                        if (Blocks(World.Get(x, yy, z))) solid++;
            if (solid * 8 >= NavGrid.SolidT) return false;
        }
        return true;
    }

    void StopCarve(Unit u)
    {
        u.CarveCell = -1;
        u.CarveList.Clear();
        u.CarveI = 0;
    }
}

/// <summary>Preparation and assault: a countdown between waves, an early call for a bounty, and the horn.</summary>
public sealed class Waves
{
    public const int Total = 12;
    public const double Prep = 300;

    public int Number;
    public bool Active;
    public double Countdown = Prep;
    readonly Queue<UnitType> _queue = new();
    double _spawnT;

    public int Coming => _queue.Count;

    public void Tick(Sim s, float dt)
    {
        if (s.Over) return;
        if (!Active)
        {
            Countdown -= dt;
            if (Countdown <= 0) Start(s);
            return;
        }
        _spawnT -= dt;
        if (_queue.Count > 0 && _spawnT <= 0)
        {
            s.SpawnRaider(_queue.Dequeue());
            _spawnT = .6 + s.Rng.NextDouble() * .5;
        }
        if (_queue.Count == 0 && !s.AnyRaider())
        {
            Active = false;
            Countdown = Prep;
            s.Say($"Волна {Number} отбита. До следующей пять минут.", 1);
            if (Number >= Total) { s.Won = true; s.Over = true; s.Say("Осада снята: долина устояла.", 1); }
        }
    }

    /// <summary>Calls the horde before the time is up; the rest of the countdown pays a bounty.</summary>
    public int StartNow(Sim s)
    {
        if (Active || s.Over) return 0;
        int bonus = (int)(Countdown / 5);
        s.Stock.Gold += bonus;
        Start(s);
        return bonus;
    }

    void Start(Sim s)
    {
        Number++;
        Active = true;
        _queue.Clear();
        int n = Number;
        var list = new List<UnitType>();
        for (int i = 0; i < 2 + n; i++) list.Add(Kinds.Goblin);
        for (int i = 0; i < n / 2 + 1; i++) list.Add(Kinds.Orc);
        for (int i = 0; i < (n + 1) / 3; i++) list.Add(Kinds.Miner);
        for (int i = 0; i < (n >= 5 ? (n - 3) / 3 : 0); i++) list.Add(Kinds.Troll);
        // shuffle so the kinds come mixed
        for (int i = list.Count - 1; i > 0; i--) { int j = s.Rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        foreach (var t in list) _queue.Enqueue(t);
        _spawnT = 1;
        if (s.Fields.Count == 0) s.ComputeFields();
        s.Recall();
        s.Say($"Рог! Волна {Number}: {list.Count} налётчиков идут с запада.", 2);
    }
}
