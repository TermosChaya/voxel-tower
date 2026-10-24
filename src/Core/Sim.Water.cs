using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoxelTower.Core;

/// <summary>
/// Water is finite and goes wherever the ground lets it. A body of water is just the voxels of it that touch. When
/// something opens at its edge below its level (a ditch cut into a pond, the bed dug out from under it) or is built
/// into it, the body is worked out afresh: its top layer runs off, the far side first, into the lowest open voxel at
/// its edge, again and again, so the water runs down slopes and over cliffs, pools in the lowest hollow it reaches and
/// settles level, and the pond drops by as much as ran out of it. The working out is done on a worker thread in the
/// game (at once in tests and scripted runs), then played back a few tens of thousands of voxels a second.
/// </summary>
public sealed partial class Sim
{
    /// <summary>Voxels water has run into since the start.</summary>
    public int Flooded { get; private set; }

    /// <summary>Work out where water goes on a worker thread (the game) rather than inside the step (tests, scripted runs).</summary>
    public bool WaterInBackground;

    /// <summary>Voxels of water moved a second of game time while it runs.</summary>
    public const float PourRate = 24000;

    /// <summary>
    /// When water last ran into each 4×4 patch of the valley (game seconds; far in the past where it never has): the
    /// way it spreads, earlier to later, is the way the current runs for a while after.
    /// </summary>
    public float[] WaterArrived;
    public const int FlowPatch = 4;
    /// <summary>When water last ran anywhere.</summary>
    public double LastPour = -1e9;

    void MarkArrival(int x, int z)
    {
        int w = World.SX / FlowPatch;
        if (WaterArrived == null) { WaterArrived = new float[w * (World.SZ / FlowPatch)]; Array.Fill(WaterArrived, -1e9f); }
        WaterArrived[z / FlowPatch * w + x / FlowPatch] = (float)Time;
        LastPour = Time;
    }

    /// <summary>Water still finding its level: something to work out, being worked out, or running.</summary>
    public bool WaterBusy => _water.Busy;
    /// <summary>Pitch still running.</summary>
    public bool PitchBusy => _pitch.Busy;
    /// <summary>Acid still running.</summary>
    public bool AcidBusy => _acid.Busy;

    /// <summary>
    /// A liquid's own running: what it is and how fast it goes, where it may have to move (something opened beside or
    /// under it, or was built into it — extra: voxels pushed aside), and the pour worked out and playing back.
    /// </summary>
    sealed class Flow
    {
        public readonly byte Mat;
        public readonly float Rate;
        public readonly List<(int x, int y, int z, int extra)> Events = new();
        public WaterPlanner Planner;
        public Task<WaterPlanner.Result> Task;
        public WaterPlanner.Result Pour;
        public int At;
        public float Due;
        public bool Pouring;
        public Flow(byte mat, float rate) { Mat = mat; Rate = rate; }
        public bool Busy => Events.Count > 0 || Task != null || Pour != null;
    }

    /// <summary>Voxels of pitch moved a second while it runs: it is thick and goes slowly.</summary>
    public const float PitchRate = 2400;

    /// <summary>Voxels of acid moved a second while it runs: it is thin and runs nearly as quick as water.</summary>
    public const float AcidRate = 12000;

    readonly Flow _water = new(Mat.Water, PourRate), _pitch = new(Mat.Pitch, PitchRate), _acid = new(Mat.Acid, AcidRate);

    // a liquid comes in from the sides and from above, never up from below
    static readonly (int, int, int)[] Inflow = { (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1), (0, 1, 0) };

    /// <summary>
    /// Every voxel written: one emptied beside water or pitch below its level, or under it, lets it move; one built into
    /// it pushes it aside; pitch poured somewhere runs off as far as it can.
    /// </summary>
    void OnWaterEdit(int x, int y, int z, byte old)
    {
        OnLiquidEdit(_water, x, y, z, old);
        OnLiquidEdit(_pitch, x, y, z, old);
        OnLiquidEdit(_acid, x, y, z, old);
        OnAcidEdit(x, y, z, old);
    }

    void OnLiquidEdit(Flow f, int x, int y, int z, byte old)
    {
        if (f.Pouring) return;
        byte now = World.Get(x, y, z);
        if (old == f.Mat)
        {
            if (now != Mat.Air) f.Events.Add((x, y, z, 1));
            return;
        }
        // pitch poured or acid splashed finds its way down (water put down by hand stays as it was put)
        if (now == f.Mat && f.Mat is Mat.Pitch or Mat.Acid) { f.Events.Add((x, y, z, 0)); return; }
        if (now != Mat.Air) return;
        foreach (var (dx, dy, dz) in Inflow)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (World.Get(nx, ny, nz) != f.Mat || y >= Surface(f.Mat, nx, ny, nz)) continue;
            f.Events.Add((x, y, z, 0));
            return;
        }
    }

    /// <summary>The height a body of a liquid stands to (exclusive): up its column from a voxel of it.</summary>
    int Surface(byte liquid, int x, int y, int z)
    {
        while (y + 1 < World.SY && World.Get(x, y + 1, z) == liquid) y++;
        return y + 1;
    }

    /// <summary>Works out where water, pitch and acid have to go when something happened at their edge, and lets them run there.</summary>
    void TickWater(float dt)
    {
        TickFlow(_water, dt);
        TickFlow(_pitch, dt);
        TickFlow(_acid, dt);
    }

    void TickFlow(Flow f, float dt)
    {
        if (f.Pour == null && f.Task != null)
        {
            if (!f.Task.IsCompleted) return;
            if (f.Task.Status == TaskStatus.RanToCompletion) { f.Pour = f.Task.Result; f.At = 0; }
            f.Task = null;
        }
        if (f.Pour == null)
        {
            if (f.Events.Count == 0) return;
            var events = f.Events.ToArray();
            f.Events.Clear();
            f.Planner ??= new WaterPlanner(World, f.Mat);
            if (WaterInBackground)
            {
                var planner = f.Planner;
                f.Task = Task.Run(() => planner.Plan(events));
                return;
            }
            f.Pour = f.Planner.Plan(events);
            f.At = 0;
        }
        Pour(f, dt);
    }

    /// <summary>Plays a worked-out pour back, so many voxels a second; starts over if the ground changed under it.</summary>
    void Pour(Flow f, float dt)
    {
        var moves = f.Pour.Moves;
        f.Due += f.Rate * dt;
        f.Pouring = true;
        try
        {
            while (f.Due >= 1 && f.At < moves.Count)
            {
                int from = moves[f.At], to = moves[f.At + 1];
                var (tx, ty, tz) = WaterPlanner.Unpack(to);
                bool ok = World.Get(tx, ty, tz) == Mat.Air;
                int fx = 0, fy = 0, fz = 0;
                if (from >= 0)
                {
                    (fx, fy, fz) = WaterPlanner.Unpack(from);
                    ok &= World.Get(fx, fy, fz) == f.Mat;
                }
                if (!ok) { Replan(f); return; }
                if (from >= 0) World.Set(fx, fy, fz, Mat.Air);
                World.Set(tx, ty, tz, f.Mat);
                if (f == _water) { MarkArrival(tx, tz); Flooded++; }
                f.At += 2;
                f.Due--;
            }
        }
        finally { f.Pouring = false; }
        if (f.At < moves.Count) return;
        foreach (int p in f.Pour.Again) { var (x, y, z) = WaterPlanner.Unpack(p); f.Events.Add((x, y, z, 0)); }
        f.Pour = null;
        f.Due = 0;
    }

    /// <summary>The ground changed under a pour (something built or dug where the liquid was going): drop the rest and work it out again.</summary>
    void Replan(Flow f)
    {
        var moves = f.Pour.Moves;
        int pushed = 0;
        for (int i = f.At; i < moves.Count; i += 2) if (moves[i] < 0) pushed++;
        var (x, y, z) = WaterPlanner.Unpack(moves[f.At + 1]);
        f.Events.Add((x, y, z, pushed));
        if (moves[f.At] >= 0) { (x, y, z) = WaterPlanner.Unpack(moves[f.At]); f.Events.Add((x, y, z, 0)); }
        foreach (int p in f.Pour.Again) { (x, y, z) = WaterPlanner.Unpack(p); f.Events.Add((x, y, z, 0)); }
        f.Pour = null;
        f.Due = 0;
    }
}

/// <summary>
/// Works out where bodies of a liquid end up — water, or pitch, each blind to the other as to any solid. Keeps its own
/// scratch copy of what has turned to liquid or to air, reads the world and writes nothing to it, so it can run on a
/// worker thread; one plan at a time.
/// </summary>
sealed class WaterPlanner
{
    public sealed class Result
    {
        /// <summary>Pairs: the voxel the water leaves (−1 for water pushed aside by building) and the voxel it runs into.</summary>
        public readonly List<int> Moves = new();
        /// <summary>Places to look at again after the pour: bodies given up on before they settled.</summary>
        public readonly List<int> Again = new();
    }

    /// <summary>Moves in one plan; a body still moving after that is picked up again when the pour has run.</summary>
    const int MaxMoves = 1 << 21;

    static readonly (int, int, int)[] Six = { (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1), (0, -1, 0), (0, 1, 0) };
    const byte Solid = 0, Wet = 1, Dry = 2;

    readonly VoxelWorld _w;
    readonly byte _liquid;
    // per voxel, what the plan has made of it so far (0: as in the world), and the pass that last reached it
    readonly byte[][] _state;
    readonly int[][] _mark;
    readonly List<int> _used = new();
    // air at the edge of the body, by height, first come first served at each height
    readonly Queue<int>[] _open;
    int _openLow, _openCount, _pass, _top;
    readonly Queue<int> _bfs = new();
    readonly List<int> _body = new();

    public WaterPlanner(VoxelWorld w, byte liquid = Mat.Water)
    {
        _liquid = liquid;
        if (w.SX > 1024 || w.SZ > 1024 || w.SY > 2048) throw new ArgumentException("the water planner packs voxels into 10 + 10 + 11 bits");
        _w = w;
        _state = new byte[w.ChunkCount][];
        _mark = new int[w.ChunkCount][];
        _open = new Queue<int>[w.SY];
        for (int y = 0; y < w.SY; y++) _open[y] = new Queue<int>();
    }

    public static int Pack(int x, int y, int z) => (y << 20) | (z << 10) | x;
    public static (int x, int y, int z) Unpack(int p) => (p & 1023, p >> 20, (p >> 10) & 1023);

    int ChunkOf(int x, int y, int z) => _w.ChunkIndex(x >> VoxelWorld.Shift, y >> VoxelWorld.Shift, z >> VoxelWorld.Shift);
    static int Local(int x, int y, int z) => ((y & VoxelWorld.Mask) << 10) | ((z & VoxelWorld.Mask) << 5) | (x & VoxelWorld.Mask);

    /// <summary>Solid (or outside the field), water or air, as the plan has it.</summary>
    byte Kind(int x, int y, int z)
    {
        if (!_w.InBounds(x, y, z)) return Solid;
        var s = _state[ChunkOf(x, y, z)];
        if (s != null)
        {
            byte v = s[Local(x, y, z)];
            if (v != 0) return v;
        }
        byte m = _w.Get(x, y, z);
        return m == _liquid ? Wet : m == Mat.Air ? Dry : Solid;
    }

    byte Kind(int p) { var (x, y, z) = Unpack(p); return Kind(x, y, z); }

    void Make(int p, byte kind)
    {
        var (x, y, z) = Unpack(p);
        int ci = ChunkOf(x, y, z);
        if (_state[ci] == null) { _state[ci] = new byte[VoxelWorld.CS3]; _used.Add(ci); }
        _state[ci][Local(x, y, z)] = kind;
    }

    int MarkOf(int x, int y, int z)
    {
        var m = _mark[ChunkOf(x, y, z)];
        return m == null ? 0 : m[Local(x, y, z)];
    }

    void SetMark(int x, int y, int z, int pass)
    {
        int ci = ChunkOf(x, y, z);
        if (_mark[ci] == null) { _mark[ci] = new int[VoxelWorld.CS3]; _used.Add(ci); }
        _mark[ci][Local(x, y, z)] = pass;
    }

    /// <summary>Works out where the water touched by these events ends up.</summary>
    public Result Plan((int x, int y, int z, int extra)[] events)
    {
        var r = new Result();
        try
        {
            var todo = new List<(int x, int y, int z, int extra)>(events);
            while (todo.Count > 0 && r.Moves.Count < MaxMoves)
            {
                var e = todo[^1];
                todo.RemoveAt(todo.Count - 1);
                int seed = WaterAt(e.x, e.y, e.z);
                if (seed < 0) continue;
                int pass = Gather(seed);
                // every other event at the same body comes along
                int extra = e.extra;
                for (int i = todo.Count - 1; i >= 0; i--)
                {
                    int s = WaterAt(todo[i].x, todo[i].y, todo[i].z);
                    if (s < 0) continue;
                    var (sx, sy, sz) = Unpack(s);
                    if (MarkOf(sx, sy, sz) != pass) continue;
                    extra += todo[i].extra;
                    todo.RemoveAt(i);
                }
                Settle(r, seed, extra);
            }
        }
        finally { Release(); }
        return r;
    }

    /// <summary>A voxel of water at a place or beside it, or −1.</summary>
    int WaterAt(int x, int y, int z)
    {
        if (Kind(x, y, z) == Wet) return Pack(x, y, z);
        foreach (var (dx, dy, dz) in Six)
            if (Kind(x + dx, y + dy, z + dz) == Wet) return Pack(x + dx, y + dy, z + dz);
        return -1;
    }

    /// <summary>
    /// The body of water a voxel belongs to, in order of distance from it (into <see cref="_body"/>), marked with a new
    /// pass; its top layer's height goes to <see cref="_top"/>.
    /// </summary>
    int Gather(int seed)
    {
        int pass = ++_pass;
        _body.Clear();
        _bfs.Clear();
        _top = -1;
        var (x0, y0, z0) = Unpack(seed);
        SetMark(x0, y0, z0, pass);
        _bfs.Enqueue(seed);
        while (_bfs.Count > 0)
        {
            int p = _bfs.Dequeue();
            _body.Add(p);
            var (x, y, z) = Unpack(p);
            if (y > _top) _top = y;
            foreach (var (dx, dy, dz) in Six)
            {
                int nx = x + dx, ny = y + dy, nz = z + dz;
                if (Kind(nx, ny, nz) != Wet || MarkOf(nx, ny, nz) == pass) continue;
                SetMark(nx, ny, nz, pass);
                _bfs.Enqueue(Pack(nx, ny, nz));
            }
        }
        return pass;
    }

    /// <summary>
    /// Lets one body find its level: water pushed aside by building goes into the lowest open voxels at its edge first,
    /// then, layer by layer, its top runs off into the lowest open voxel as long as that lies below it.
    /// </summary>
    void Settle(Result r, int seed, int extra)
    {
        for (int layers = 0; layers < 4096 && r.Moves.Count < MaxMoves; layers++)
        {
            int pass = Gather(seed);
            OpenAll(pass);
            if (extra > 0)
            {
                while (extra > 0 && PeekOpen(pass, out int f))
                {
                    TakeOpen();
                    Fill(f, pass);
                    r.Moves.Add(-1); r.Moves.Add(f);
                    extra--;
                }
                extra = 0;
                continue;
            }
            if (!PeekOpen(pass, out int low) || (low >> 20) >= _top) return;
            // the top layer runs off, the far side first, so what is left of it stays joined to where it runs out
            int outflow = WetBeside(low, pass);
            int top = _top;
            pass = Gather(outflow);
            int moved = 0;
            for (int i = _body.Count - 1; i >= 0; i--)
            {
                int t = _body[i];
                if (t >> 20 != top) continue;
                if (!PeekOpen(pass, out int f) || (f >> 20) >= top) return;
                TakeOpen();
                Make(t, Dry);
                Fill(f, pass);
                r.Moves.Add(t); r.Moves.Add(f);
                moved++;
                if (r.Moves.Count >= MaxMoves) { r.Again.Add(f); return; }
            }
            if (moved == 0) return;
            seed = Kind(outflow) == Wet ? outflow : r.Moves[^1];
        }
    }

    void Fill(int f, int pass)
    {
        Make(f, Wet);
        var (x, y, z) = Unpack(f);
        SetMark(x, y, z, pass);
        OpenAround(x, y, z);
    }

    /// <summary>A voxel of the gathered body next to an open one.</summary>
    int WetBeside(int p, int pass)
    {
        var (x, y, z) = Unpack(p);
        foreach (var (dx, dy, dz) in Six)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (Kind(nx, ny, nz) == Wet && MarkOf(nx, ny, nz) == pass) return Pack(nx, ny, nz);
        }
        return -1;
    }

    // ---------------------------------------------------------------- the open voxels at the body's edge

    int _openPass;

    /// <summary>Starts the edge afresh: every air voxel touching the gathered body.</summary>
    void OpenAll(int pass)
    {
        foreach (var q in _open) q.Clear();
        _openCount = 0;
        _openLow = _w.SY;
        _openPass = ++_pass;
        foreach (int p in _body)
        {
            var (x, y, z) = Unpack(p);
            OpenAround(x, y, z);
        }
    }

    void OpenAround(int x, int y, int z)
    {
        foreach (var (dx, dy, dz) in Six)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (Kind(nx, ny, nz) != Dry || MarkOf(nx, ny, nz) == _openPass) continue;
            SetMark(nx, ny, nz, _openPass);
            _open[ny].Enqueue(Pack(nx, ny, nz));
            _openCount++;
            if (ny < _openLow) _openLow = ny;
        }
    }

    /// <summary>The lowest open voxel still air and still touching the body; stale ones are dropped (and may come back).</summary>
    bool PeekOpen(int pass, out int p)
    {
        while (_openCount > 0)
        {
            while (_open[_openLow].Count == 0) _openLow++;
            p = _open[_openLow].Peek();
            if (Kind(p) == Dry && WetBeside(p, pass) >= 0) return true;
            _open[_openLow].Dequeue();
            _openCount--;
            var (x, y, z) = Unpack(p);
            SetMark(x, y, z, 0);
        }
        p = -1;
        return false;
    }

    void TakeOpen() { _open[_openLow].Dequeue(); _openCount--; }

    /// <summary>Lets go of the scratch layers between plans.</summary>
    void Release()
    {
        foreach (int ci in _used) { _state[ci] = null; _mark[ci] = null; }
        _used.Clear();
        foreach (var q in _open) q.Clear();
        _openCount = 0;
        _body.Clear();
        _bfs.Clear();
    }
}
