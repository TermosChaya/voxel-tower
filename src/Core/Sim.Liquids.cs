using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>
/// One of the valley's liquids and how it runs: how often it moves sideways (it always falls at once), how hard a
/// difference in level drives it, how thin it lies before it stops running out over dry ground, and from how full a
/// voxel of it is drawn.
/// </summary>
public sealed class Liquid
{
    public byte Mat;
    public string Name;
    /// <summary>It moves sideways every this many turns (60 a second): water every turn, lava every eighth.</summary>
    public int Every;
    /// <summary>How hard a difference in level drives it through a pipe, as against water (thick liquids less).</summary>
    public float Pull;
    /// <summary>What a pipe keeps of its flow from one turn to the next: the way on the liquid (thick liquids less).</summary>
    public float Way;
    /// <summary>How deep (voxels) it must stand to run on over dry level ground: thinner than that, it stays where it lies.</summary>
    public float Thin;
    /// <summary>A voxel is drawn as the liquid from this much in it, and stops being drawn below the second.</summary>
    public float ShowOn, ShowOff;
    /// <summary>A film of it creeps on over level ground and over edges however thin (acid, which goes flat by itself).</summary>
    public bool Creeps;
}

/// <summary>
/// The valley's liquids run as they would. Each voxel of water, acid, pitch or lava holds an amount of it (a voxel full
/// is 1; one pressed by what is laid on it holds more for a moment, and that rises). Sixty times a second a voxel that
/// may move falls at once into what is not full under it, and the column it is part of — the voxels of it one over
/// another, standing to its foot and all that is in it — passes liquid to each side through a pipe: the lowest voxel
/// beside it, up to its level, that could take some. The pull through a pipe grows with the difference in level
/// across it, the harder the deeper the water, and in shallow water keeps its way, so a sheet of water runs on over
/// the land; nothing is let rise above what it fills from. So a pond breached at its foot pours out through the breach
/// while its surface drops, a flood runs out over the land, a moat fills from the river a cut at a time, and a body
/// finds its level and stops. Each liquid runs at its own pace and lies as thick as it does: water quick and thin,
/// acid a little slower, pitch slow, lava slowest and in a tongue. A voxel is drawn as the liquid when it holds enough.
/// Only what moved, or stands beside what moved, is looked at; a body at rest costs nothing. The amounts are kept in
/// blocks of 16³ where something ran, and a block long at rest is let go — whole voxels the world keeps by itself,
/// voxels only part full are kept by themselves.
/// </summary>
public sealed partial class Sim
{
    public const int LiquidWater = 1, LiquidAcid = 2, LiquidPitch = 3, LiquidLava = 4;

    /// <summary>The liquids by their number (0 none): water, acid, pitch, lava.</summary>
    public static readonly Liquid[] Liquids =
    {
        null,
        new Liquid { Mat = Mat.Water, Name = "вода", Every = 1, Pull = 1, Way = .94f, Thin = .9f, ShowOn = .4f, ShowOff = .15f },
        new Liquid { Mat = Mat.Acid, Name = "кислота", Every = 2, Pull = .8f, Way = .9f, Thin = .6f, ShowOn = .3f, ShowOff = .1f, Creeps = true },
        new Liquid { Mat = Mat.Pitch, Name = "смола", Every = 3, Pull = .5f, Way = .8f, Thin = .6f, ShowOn = .35f, ShowOff = .15f },
        new Liquid { Mat = Mat.Lava, Name = "лава", Every = 6, Pull = .4f, Way = .6f, Thin = 1.4f, ShowOn = .45f, ShowOff = .2f },
    };

    /// <summary>The liquid a material is (0: none).</summary>
    public static readonly byte[] LiquidOf = MakeLiquidOf();

    static byte[] MakeLiquidOf()
    {
        var of = new byte[Mat.Count];
        for (int i = 1; i < Liquids.Length; i++) of[Liquids[i].Mat] = (byte)i;
        return of;
    }

    /// <summary>Turns a second: a voxel of water falls a voxel a turn, 7,5 m a second.</summary>
    public const int LiquidTurns = 60;
    /// <summary>Less than this changing in a voxel on a turn and what lies about it need not be looked at again.</summary>
    const float Restful = .002f;
    /// <summary>Less than this left in a voxel, and it is dry.</summary>
    const float Trace = .0005f;
    /// <summary>Differences in level smaller than this move nothing: a body that near level is level.</summary>
    const float Level = .03f;
    /// <summary>Turns a step may run at most: a slow frame does not make the water run all at once after it.</summary>
    const int MaxTurnsAStep = 6;
    /// <summary>
    /// Voxels the liquids may look at a second: a flood out over the whole valley runs slower then, in the game's time,
    /// rather than making every frame wait for it.
    /// </summary>
    const int LookASecond = 100000;
    /// <summary>Voxels one turn may look at: more on the list wait for the next turns, so no one frame is held up long.</summary>
    const int LookATurn = 12000;
    /// <summary>
    /// What a voxel of difference in level adds to the flow through a pipe in a turn, for each voxel of depth: weight's
    /// pull over a turn, g·dt²/dx (78 voxels a second² over a sixtieth of a second, squared).
    /// </summary>
    const float Weight = .0218f;
    /// <summary>What of the flow that came into a column keeps its way on through it into the next.</summary>
    const float KeepOn = .97f;
    /// <summary>The most a pipe pulls for a voxel of difference in level, a turn, however deep: more and a body would slosh.</summary>
    const float PullMost = .2f;

    const int BS = 16, BShift = 4, BMask = 15, B3 = BS * BS * BS;

    /// <summary>What is kept of one voxel where something ran.</summary>
    struct Cell
    {
        // how much it holds, what came and went this turn, what went out of it this turn; and what it holds
        public float M, D, O;
        public byte K;
        // the column it is part of, worked out on the turn in CAt: its foot, top, level and what is in it
        public ushort Y0, Y1;
        public float H, V;
        // the turn it was last put on the list to be looked at, and last moved; and the turn its column was put down to
        // pass liquid sideways (kept at the foot), with the job it is then
        public int Due, Moved, CAt, Sent, JIx;
        // of the turn in TAt, where what it is to take (as a column's foot, or dry) is added up; and the turns its
        // pipes' flows out (P) and what came in (In) were kept
        public int TAt, TIx, PAt, InAt;
    }

    /// <summary>The amounts kept for a block where something ran: how much of which liquid each voxel holds.</summary>
    sealed class Pool
    {
        public int Bi;
        public readonly Cell[] C = new Cell[B3];
        // kept at a column's foot: what its pipes passed on its last turn each way (e, w, s, n), and what came into it
        public readonly float[] P = new float[B3 * 4], In = new float[B3 * 4];
        /// <summary>When anything in it last ran.</summary>
        public double Stirred;
        public Pool(int bi) { Bi = bi; }
    }

    Pool[] _pools;
    readonly Stack<Pool> _spare = new();
    const int SpareMost = 16;
    bool[] _asleep;
    readonly List<Pool> _poolList = new();
    // the voxels only part full in blocks let go, by block: (voxel in the block, liquid, amount)
    readonly Dictionary<int, List<(int li, byte k, float m)>> _sleeping = new();
    int _bx, _by, _bz;
    // the voxels to look at this turn and the next, packed
    List<int> _now = new(), _next = new();
    readonly List<int> _moved = new();
    int _turn = 1;
    float _turnDue, _lookCredit;
    double _poolsLooked;
    // writing the liquids' own voxels into the world: not something done to them from outside
    bool _liquidWriting;
    // voxels of each liquid on the list for the next turn
    readonly int[] _busy = new int[5];
    /// <summary>What has been lost to drying out (amounts too small to keep), in voxels, since the start.</summary>
    public double LiquidDried { get; private set; }
    /// <summary>Voxels looked at on the last turn, and the most on one turn since the last call to <see cref="TakeLiquidPeak"/>.</summary>
    public int LiquidLooked { get; private set; }
    int _liquidPeak;
    public int TakeLiquidPeak() { int p = _liquidPeak; _liquidPeak = 0; return p; }
    /// <summary>Blocks of amounts kept just now.</summary>
    public int LiquidBlocks => _poolList.Count;

    /// <summary>The water running through a column: what runs out of it a turn along x and z (voxels), its foot, how deep it stands.</summary>
    public readonly record struct Flow(float X, float Z, int Y0, float Depth);

    /// <summary>
    /// Where the water runs, by the column (packed with y 0), from the last turns: what carries people off their feet,
    /// washes loose things along and drifts pitch afloat on it.
    /// </summary>
    public readonly Dictionary<int, Flow> Current = new();

    static int Local(int x, int y, int z) => ((y & BMask) << 8) | ((z & BMask) << 4) | (x & BMask);
    int BlockOf(int x, int y, int z)
    {
        if (_pools == null) MakePools();
        return ((y >> BShift) * _bz + (z >> BShift)) * _bx + (x >> BShift);
    }

    void MakePools()
    {
        _bx = World.SX / BS; _by = World.SY / BS; _bz = World.SZ / BS;
        _pools = new Pool[_bx * _by * _bz];
        _asleep = new bool[_pools.Length];
    }

    /// <summary>Liquid may be in it: air, a liquid, or the grass it runs over and flattens.</summary>
    static bool Open(byte m) => m == Mat.Air || LiquidOf[m] != 0 || Mat.IsSprig(m);

    /// <summary>The amounts of a block: those kept, or those of a block let go woken, or (to make) read from the world.</summary>
    Pool PoolOf(int bi, bool make)
    {
        var p = _pools[bi];
        if (p != null) return p;
        if (!make && !_asleep[bi]) return null;
        List<(int li, byte k, float m)> parts = null;
        bool asleep = _asleep[bi] && _sleeping.TryGetValue(bi, out parts);
        if (_spare.Count > 0) { p = _spare.Pop(); p.Bi = bi; }
        else p = new Pool(bi);
        _pools[bi] = p;
        _poolList.Add(p);
        // the amounts as the world has them: a voxel of a liquid is full of it
        int x0 = bi % _bx * BS, z0 = bi / _bx % _bz * BS, y0 = bi / (_bx * _bz) * BS;
        for (int ly = 0; ly < BS; ly++)
            for (int lz = 0; lz < BS; lz++)
                for (int lx = 0; lx < BS; lx++)
                {
                    byte k = LiquidOf[World.Get(x0 + lx, y0 + ly, z0 + lz)];
                    if (k == 0) continue;
                    int li = (ly << 8) | (lz << 4) | lx;
                    p.C[li].K = k; p.C[li].M = 1;
                }
        if (asleep)
        {
            foreach (var (li, k, m) in parts) { p.C[li].K = k; p.C[li].M = m; }
            _sleeping.Remove(bi);
        }
        _asleep[bi] = false;
        p.Stirred = Time;
        return p;
    }

    Pool PoolAt(int x, int y, int z, bool make) => PoolOf(BlockOf(x, y, z), make);

    /// <summary>How much of which liquid a voxel holds (a solid one or one outside the valley none).</summary>
    public float LiquidAt(int x, int y, int z, out byte kind)
    {
        kind = 0;
        if (!World.InBounds(x, y, z)) return 0;
        var p = PoolAt(x, y, z, false);
        if (p != null) { int li = Local(x, y, z); kind = p.C[li].K; return p.C[li].M; }
        kind = LiquidOf[World.Get(x, y, z)];
        return kind != 0 ? 1 : 0;
    }

    /// <summary>All of a liquid in the valley, voxels (a voxel full is one).</summary>
    public double LiquidMass(byte mat)
    {
        byte k = LiquidOf[mat];
        double n = 0;
        for (int ci = 0; ci < World.ChunkCount; ci++)
        {
            var d = World.ChunkData(ci);
            if (d == null) { if (World.ChunkUniform(ci) == mat) n += VoxelWorld.CS3; continue; }
            foreach (byte b in d) if (b == mat) n++;
        }
        // where amounts are kept, those and not the world's voxels
        foreach (var p in _poolList)
        {
            int x0 = p.Bi % _bx * BS, z0 = p.Bi / _bx % _bz * BS, y0 = p.Bi / (_bx * _bz) * BS;
            for (int li = 0; li < B3; li++)
            {
                if (p.C[li].K == k) n += p.C[li].M;
                if (World.Get(x0 + (li & BMask), y0 + (li >> 8), z0 + (li >> 4 & BMask)) == mat) n--;
            }
        }
        foreach (var (bi, parts) in _sleeping)
        {
            int x0 = bi % _bx * BS, z0 = bi / _bx % _bz * BS, y0 = bi / (_bx * _bz) * BS;
            foreach (var (li, pk, m) in parts)
            {
                if (pk != k) continue;
                bool shown = World.Get(x0 + (li & BMask), y0 + (li >> 8), z0 + (li >> 4 & BMask)) == mat;
                n += m - (shown ? 1 : 0);
            }
        }
        return n;
    }

    /// <summary>Some of a liquid let into the valley at a voxel (a flask, a vent, a tunnel let flood): it runs from there.</summary>
    public void Pour(byte mat, int x, int y, int z, float amount)
    {
        byte k = LiquidOf[mat];
        if (k == 0 || !World.InBounds(x, y, z) || !Open(World.Get(x, y, z))) return;
        var p = PoolAt(x, y, z, true);
        int li = Local(x, y, z);
        if (p.C[li].K != 0 && p.C[li].K != k) return;
        p.C[li].K = k;
        p.C[li].M += amount;
        Show(p, li, x, y, z);
        Stir(x, y, z);
    }

    /// <summary>The voxel and those about it looked at on the next turn.</summary>
    void Stir(int x, int y, int z)
    {
        Due(x, y, z);
        Due(x + 1, y, z); Due(x - 1, y, z); Due(x, y + 1, z); Due(x, y - 1, z); Due(x, y, z + 1); Due(x, y, z - 1);
    }

    /// <summary>A voxel with liquid in it on the list for the next turn.</summary>
    void Due(int x, int y, int z)
    {
        if (!World.InBounds(x, y, z)) return;
        var p = PoolAt(x, y, z, false);
        if (p == null)
        {
            if (LiquidOf[World.Get(x, y, z)] == 0) return;
            p = PoolAt(x, y, z, true);
        }
        int li = Local(x, y, z);
        byte k = p.C[li].K;
        if (k == 0 || p.C[li].M <= 0 || p.C[li].Due == _turn + 1) return;
        p.C[li].Due = _turn + 1;
        _next.Add(WaterPlanner.Pack(x, y, z));
        _busy[k]++;
    }

    /// <summary>
    /// Something was written into the valley by anything but the liquids themselves: a liquid poured, splashed or let
    /// down there runs from it; something built into a liquid pushes it out over itself; a liquid taken away (dug out,
    /// eaten, burnt) is gone; and whatever lies beside a voxel emptied runs into it.
    /// </summary>
    void OnLiquidEdit(int x, int y, int z, byte old)
    {
        if (_liquidWriting) return;
        byte now = World.Get(x, y, z);
        byte kNow = LiquidOf[now], kOld = LiquidOf[old];
        var p = PoolAt(x, y, z, false);
        int li = Local(x, y, z);
        if (p == null)
        {
            if (kNow == 0 && kOld == 0 && !LiquidBeside(x, y, z)) return;
            p = PoolAt(x, y, z, true);
            // the block made just now read the world as it is after the write: what was there before is the old voxel
            p.C[li].K = kOld; p.C[li].M = kOld != 0 ? 1 : 0;
        }
        byte k = p.C[li].K;
        float m = p.C[li].M;
        if (kNow != 0)
        {
            p.C[li].M = k == kNow ? Math.Max(1, m) : 1;
            p.C[li].K = kNow;
        }
        else if (!Open(now))
        {
            p.C[li].K = 0; p.C[li].M = 0;
            if (k != 0 && m > 0) Displace(x, y, z, k, m);
        }
        else if (k != 0 && (kOld == k || m < Liquids[k].ShowOn))
        {
            // what the world showed of it taken away (or what was not shown, with the voxel it was in)
            p.C[li].K = 0; p.C[li].M = 0;
        }
        p.Stirred = Time;
        Stir(x, y, z);
    }

    bool LiquidBeside(int x, int y, int z) =>
        LiquidOf[World.Get(x + 1, y, z)] != 0 || LiquidOf[World.Get(x - 1, y, z)] != 0 || LiquidOf[World.Get(x, y + 1, z)] != 0 ||
        LiquidOf[World.Get(x, y - 1, z)] != 0 || LiquidOf[World.Get(x, y, z + 1)] != 0 || LiquidOf[World.Get(x, y, z - 1)] != 0;

    static readonly (int, int, int)[] Outward = { (0, 1, 0), (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1), (0, -1, 0) };

    /// <summary>A liquid pushed out of a voxel by what was built into it: over it, or else beside it, pressed.</summary>
    void Displace(int x, int y, int z, byte k, float m)
    {
        foreach (var (dx, dy, dz) in Outward)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (!World.InBounds(nx, ny, nz) || !Open(World.Get(nx, ny, nz))) continue;
            var p = PoolAt(nx, ny, nz, true);
            int li = Local(nx, ny, nz);
            if (p.C[li].K != 0 && p.C[li].K != k) continue;
            p.C[li].K = k;
            p.C[li].M += m;
            Stir(nx, ny, nz);
            return;
        }
        LiquidDried += m;
    }

    /// <summary>
    /// A voxel of a liquid becomes something else by itself — lava set to stone, a liquid turned into another — and not
    /// by anything done to it: what it held is gone into what it became.
    /// </summary>
    void Become(int x, int y, int z, byte mat)
    {
        var p = PoolAt(x, y, z, false);
        if (p != null)
        {
            int li = Local(x, y, z);
            byte k = LiquidOf[mat];
            if (k != 0) p.C[li].K = k;
            else { p.C[li].K = 0; p.C[li].M = 0; }
            p.Stirred = Time;
        }
        bool was = _liquidWriting;
        _liquidWriting = true;
        try { World.Set(x, y, z, mat); }
        finally { _liquidWriting = was; }
        Stir(x, y, z);
    }

    /// <summary>The liquids run: so many turns a second, at most a few a step.</summary>
    void TickLiquids(float dt)
    {
        _turnDue += dt * LiquidTurns;
        _lookCredit = Math.Min(_lookCredit + LookASecond * dt, LookASecond * .1f);
        int turns = 0;
        while (_turnDue >= 1 && turns < MaxTurnsAStep && _lookCredit > 0) { _turnDue--; Turn(); turns++; _lookCredit -= LiquidLooked; }
        if (_turnDue > 2) _turnDue = 2;
        if (Time - _poolsLooked > 2) { _poolsLooked = Time; LetGoOfPools(); }
        TickFlood();
        TickLava();
    }

    /// <summary>
    /// One turn: every voxel on the list falls or rises as it must, and the columns they are part of plan and pass what
    /// runs sideways (all reckoned from the amounts as they stood at the turn's start); then what came and went is
    /// added up, shown in the world, and whatever changed, and what lies about it, goes on the list for the next turn.
    /// </summary>
    void Turn()
    {
        (_now, _next) = (_next, _now);
        _next.Clear();
        Array.Clear(_busy);
        // too many to look at in one turn: the rest wait for the next
        if (_now.Count > LookATurn)
        {
            for (int i = LookATurn; i < _now.Count; i++)
            {
                var (x, y, z) = WaterPlanner.Unpack(_now[i]);
                var p = PoolAt(x, y, z, false);
                if (p == null) continue;
                int li = Local(x, y, z);
                p.C[li].Due = _turn + 2;
                _next.Add(_now[i]);
                _busy[p.C[li].K]++;
            }
            _now.RemoveRange(LookATurn, _now.Count - LookATurn);
        }
        _turn++;
        LiquidLooked = _now.Count;
        if (_now.Count > _liquidPeak) _liquidPeak = _now.Count;
        if (_now.Count == 0) return;
        if (Current.Count > 0 && _turn % 2 == 0) Current.Clear();
        _moved.Clear();
        _jobs.Clear();
        foreach (int c in _now) Pass(c);
        Sideways();
        _liquidWriting = true;
        try { foreach (int c in _moved) Settle(c); }
        finally { _liquidWriting = false; }
    }

    static readonly (int dx, int dz)[] Runs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>
    /// What one voxel does this turn: it falls into what is not full under it, what it holds over full rises, and —
    /// once a turn for the column it is part of — the column is put down to pass liquid sideways.
    /// </summary>
    void Pass(int c)
    {
        var (x, y, z) = WaterPlanner.Unpack(c);
        var p = PoolAt(x, y, z, false);
        if (p == null) return;
        int li = Local(x, y, z);
        byte k = p.C[li].K;
        float m = p.C[li].M;
        if (k == 0 || m <= 0) return;
        // lava and water meeting: steam, and the lava set to stone (Sim.Lava.cs)
        if (Lavas.Count > 0 && (k == LiquidLava || k == LiquidWater) && Meets(x, y, z, k)) return;
        float avail = m - p.C[li].O;
        bool falling = false;
        // down into what is not full under it, at once
        if (y > 0 && Open(World.Get(x, y - 1, z)))
        {
            var pb = PoolAt(x, y - 1, z, true);
            int lb = Local(x, y - 1, z);
            if ((pb.C[lb].K == 0 || pb.C[lb].K == k) && pb.C[lb].M < .999f)
            {
                float f = Math.Min(avail, 1 - pb.C[lb].M);
                if (f > 0) { Give(p, li, c, pb, lb, WaterPlanner.Pack(x, y - 1, z), k, f); avail -= f; falling = true; }
            }
        }
        // what it holds over full rises, if it can
        if (avail > 1.001f && y + 1 < World.SY && Open(World.Get(x, y + 1, z)))
        {
            var pa = PoolAt(x, y + 1, z, true);
            int la = Local(x, y + 1, z);
            if (pa.C[la].K == 0 || pa.C[la].K == k) Give(p, li, c, pa, la, WaterPlanner.Pack(x, y + 1, z), k, (avail - 1) * .5f);
        }
        // falling, it does not run sideways in the air
        if (falling) return;
        var col = Column(x, y, z, k);
        var pf = PoolAt(x, col.y0, z, true);
        int lf = Local(x, col.y0, z);
        if (pf.C[lf].Sent == _turn) return;
        pf.C[lf].Sent = _turn;
        pf.C[lf].JIx = -1;
        // nor does a column standing on what is not full
        if (col.y0 > 0 && Open(World.Get(x, col.y0 - 1, z)))
        {
            LiquidAt(x, col.y0 - 1, z, out byte kb);
            if (kb == 0 || kb == k) return;
        }
        // a slow liquid runs sideways only on its own turns, and waits for them on the list
        if (_turn % Liquids[k].Every != 0) { Due(x, y, z); return; }
        pf.C[lf].JIx = _jobs.Count;
        _jobs.Add(new Job(x, z, col, k, pf, lf));
    }

    /// <summary>A column of a liquid, worked out on a turn: its foot and top voxel, the level it stands to, how much is in it.</summary>
    readonly record struct Col(int y0, int y1, float head, float vol);

    /// <summary>
    /// The column a voxel of a liquid is part of: the voxels of it one over another, touching. It stands to its foot
    /// and all that is in it, as if it had settled already (what is still falling into its lower voxels, or pressed
    /// into them, counted as it will lie). Worked out once a turn, and kept with every voxel of it.
    /// </summary>
    Col Column(int x, int y, int z, byte k)
    {
        var p = PoolAt(x, y, z, true);
        int li = Local(x, y, z);
        if (p.C[li].CAt == _turn) return new Col(p.C[li].Y0, p.C[li].Y1, p.C[li].H, p.C[li].V);
        // down to its foot and up to its top, a voxel at a time through the blocks
        var q = p;
        int lq = li, y0 = y;
        float vol = p.C[li].M;
        while (y0 > 0)
        {
            int yb = y0 - 1;
            var qb = (yb & BMask) == BMask ? PoolAt(x, yb, z, true) : q;
            int lb = (yb & BMask) == BMask ? Local(x, yb, z) : lq - BS * BS;
            if (qb.C[lb].K != k || qb.C[lb].M <= Trace) break;
            q = qb; lq = lb; y0 = yb;
            vol += q.C[lq].M;
        }
        var foot = q;
        int lfoot = lq, y1 = y;
        q = p; lq = li;
        while (y1 + 1 < World.SY)
        {
            int ya = y1 + 1;
            var qa = (ya & BMask) == 0 ? PoolAt(x, ya, z, true) : q;
            int la = (ya & BMask) == 0 ? Local(x, ya, z) : lq + BS * BS;
            if (qa.C[la].K != k || qa.C[la].M <= Trace) break;
            q = qa; lq = la; y1 = ya;
            vol += q.C[lq].M;
        }
        float head = y0 + vol;
        q = foot; lq = lfoot;
        for (int yy = y0; ; yy++)
        {
            ref var c = ref q.C[lq];
            c.Y0 = (ushort)y0; c.Y1 = (ushort)y1; c.H = head; c.V = vol; c.CAt = _turn;
            if (yy == y1) break;
            if (((yy + 1) & BMask) == 0) { q = PoolAt(x, yy + 1, z, true); lq = Local(x, yy + 1, z); }
            else lq += BS * BS;
        }
        return new Col(y0, y1, head, vol);
    }

    /// <summary>A column's turn sideways waiting to be worked out: where it stands, what it is, the block and voxel of its foot.</summary>
    readonly record struct Job(int x, int z, Col col, byte k, Pool pf, int lf);
    /// <summary>A flow planned through a pipe: from which column, which way, into what (and where that adds up), how much.</summary>
    struct Plan
    {
        public int Job, Side, X, Y, Z, Slot;
        public bool Dry;
        public float Flow;
    }

    readonly List<Job> _jobs = new();
    readonly List<Plan> _plans = new();
    // by job: where its plans start, what it is to give, what its flows are cut down to
    int[] _jobFirst = new int[256];
    float[] _jobGive = new float[256], _jobCut = new float[256];
    // what each column (or dry voxel) taking this turn is to take, from how high a source, standing how high itself,
    // the job it is (or -1); and what share of it it may take
    int _slots;
    float[] _tSum = new float[256], _tFrom = new float[256], _tLevel = new float[256], _tCut = new float[256];
    int[] _tJob = new int[256], _tLi = new int[256];
    Pool[] _tPool = new Pool[256];

    /// <summary>Where what a column or a dry voxel takes this turn is added up, made the first time it is asked for.</summary>
    int Slot(Pool p, int li, float level)
    {
        if (p.C[li].TAt == _turn) return p.C[li].TIx;
        if (_slots == _tSum.Length)
        {
            int n = _slots * 2;
            Array.Resize(ref _tSum, n); Array.Resize(ref _tFrom, n); Array.Resize(ref _tLevel, n); Array.Resize(ref _tCut, n); Array.Resize(ref _tJob, n);
            Array.Resize(ref _tLi, n); Array.Resize(ref _tPool, n);
        }
        int s = _slots++;
        p.C[li].TAt = _turn; p.C[li].TIx = s;
        _tSum[s] = 0; _tFrom[s] = float.MinValue; _tLevel[s] = level;
        _tJob[s] = p.C[li].Sent == _turn ? p.C[li].JIx : -1;
        _tPool[s] = p; _tLi[s] = li;
        return s;
    }

    /// <summary>
    /// The columns' turn sideways. Each column first plans what it would pass through each of its pipes: each way, the
    /// first voxel beside it that could take some (from its foot up to its level) is the pipe's mouth, and the pull
    /// through the pipe grows by the difference between the column's level and the level beyond — the other column's,
    /// the dry voxel's own, or the ground under it over an edge — the harder the deeper the liquid; in shallow water it
    /// keeps its way. Then nothing is let rise above the sources it fills from: whatever is to fill a column or a dry
    /// voxel from all sides is cut down to half the difference between it and the highest of them, besides what it
    /// gives on itself. What every column gives is taken off its top first; then what the other sides get is laid on
    /// their tops, as they are after that, or into the dry voxels.
    /// </summary>
    void Sideways()
    {
        _plans.Clear();
        _slots = 0;
        if (_jobFirst.Length < _jobs.Count + 1)
        {
            int n = Math.Max(_jobs.Count + 1, _jobFirst.Length * 2);
            Array.Resize(ref _jobFirst, n); Array.Resize(ref _jobGive, n); Array.Resize(ref _jobCut, n);
        }
        for (int j = 0; j < _jobs.Count; j++) { _jobFirst[j] = _plans.Count; PlanPipes(j); }
        _jobFirst[_jobs.Count] = _plans.Count;
        // twice: what a column gives on is what it may give once those it gives to are cut down in their turn
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < _slots; i++)
            {
                float allowed = Math.Max(0, _tFrom[i] - _tLevel[i]) * .5f + (_tJob[i] >= 0 ? _jobGive[_tJob[i]] : 0);
                _tCut[i] = _tSum[i] > allowed ? allowed / _tSum[i] : 1;
            }
            if (pass == 1) break;
            for (int j = 0; j < _jobs.Count; j++)
            {
                float g = 0;
                for (int i = _jobFirst[j]; i < _jobFirst[j + 1]; i++) g += _plans[i].Flow * _tCut[_plans[i].Slot];
                _jobGive[j] = g;
            }
        }
        for (int j = 0; j < _jobs.Count; j++)
        {
            var job = _jobs[j];
            float total = 0;
            for (int i = _jobFirst[j]; i < _jobFirst[j + 1]; i++)
            {
                var pl = _plans[i];
                pl.Flow *= _tCut[pl.Slot];
                _plans[i] = pl;
                total += pl.Flow;
            }
            float left = total;
            for (int yy = job.col.y1; yy >= job.col.y0 && left > 0; yy--)
            {
                var q = PoolAt(job.x, yy, job.z, true);
                int lq = Local(job.x, yy, job.z);
                float t = Math.Min(left, q.C[lq].M - q.C[lq].O);
                if (t <= 0) continue;
                Take(q, lq, WaterPlanner.Pack(job.x, yy, job.z), t);
                left -= t;
            }
            _jobCut[j] = total > 0 ? (total - left) / total : 0;

        }
        for (int j = 0; j < _jobs.Count; j++)
        {
            var job = _jobs[j];
            int pi = job.lf * 4;
            Array.Clear(job.pf.P, pi, 4);
            job.pf.C[job.lf].PAt = _turn;
            float fx = 0, fz = 0, all = 0;
            for (int i = _jobFirst[j]; i < _jobFirst[j + 1]; i++)
            {
                var pl = _plans[i];
                float flow = pl.Flow * _jobCut[j];
                if (flow <= 0) continue;
                job.pf.P[pi + pl.Side] = flow;
                all += flow;
                // and what came in keeps its way on into the next column
                var rp = _tPool[pl.Slot];
                int rl = _tLi[pl.Slot];
                if (rp.C[rl].InAt != _turn) { rp.C[rl].InAt = _turn; Array.Clear(rp.In, rl * 4, 4); }
                rp.In[rl * 4 + pl.Side] += flow;
                if (pl.Dry) Put(pl.X, pl.Y, pl.Z, job.k, flow);
                else LayOnTop(pl.X, pl.Y, pl.Z, job.k, flow);
                fx += Runs[pl.Side].dx * flow; fz += Runs[pl.Side].dz * flow;
            }
            if (job.k == LiquidWater && fx * fx + fz * fz > .0004f) Current[WaterPlanner.Pack(job.x, 0, job.z)] = new Flow(fx, fz, job.col.y0, job.col.head - job.col.y0);
            // a column something runs through is looked at again, though what comes in makes up for what goes out
            if (all > Restful) Due(job.x, job.col.y1, job.z);
        }
    }

    /// <summary>What one column would pass through its pipes this turn, before anything is cut down.</summary>
    void PlanPipes(int j)
    {
        var (x, z, col, k, pf, lf) = _jobs[j];
        var L = Liquids[k];
        float depth = col.head - col.y0;
        float pull = Math.Min(PullMost, Weight * Math.Max(1, depth)) * L.Pull;
        // what runs keeps its way, but a thin film drags on the ground and keeps none, and lies still on a slighter
        // slope than deep water does
        float way = L.Way * Math.Clamp((depth - .3f) / .9f, 0, 1);
        float still = L.Creeps ? Level : Level + Math.Max(0, 1 - depth) * .1f;
        // a shallow run carries its way on from one column into the next; in a deep body that would slosh
        float keep = KeepOn * Math.Clamp(1 - (depth - 2) / 3, 0, 1);
        // afloat on running water, it drifts with it: as fast as the water runs, as deep as it lies
        float driftX = 0, driftZ = 0;
        if (k != LiquidWater && Current.Count > 0 && LiquidOf[World.Get(x, col.y0 - 1, z)] == LiquidWater
            && Current.TryGetValue(WaterPlanner.Pack(x, 0, z), out var under) && under.Depth > .5f)
        {
            driftX = under.X / under.Depth * depth * L.Every;
            driftZ = under.Z / under.Depth * depth * L.Every;
        }
        int first = _plans.Count;
        float total = 0;
        int top = Math.Min(World.SY - 1, (int)MathF.Ceiling(col.head) - 1);
        for (int s = 0; s < 4; s++)
        {
            // what ran this way out of it on its last turn, or what ran into it this way, keeping its way on
            float was = _turn - pf.C[lf].PAt <= L.Every ? pf.P[lf * 4 + s] : 0;
            if (_turn - pf.C[lf].InAt <= L.Every) was = Math.Max(was, pf.In[lf * 4 + s] * keep);
            var (dx, dz) = Runs[s];
            float drift = Math.Max(0, dx * driftX + dz * driftZ);
            int nx = x + dx, nz = z + dz, my = -1;
            bool dry = false;
            // the pipe's mouth: the lowest voxel beside the column, up to its level, that is open to this liquid
            for (int yy = col.y0; yy <= top; yy++)
            {
                if (!World.InBounds(nx, yy, nz) || !Open(World.Get(nx, yy, nz))) continue;
                float nm = LiquidAt(nx, yy, nz, out byte nk);
                if (nk != 0 && nk != k) continue;
                my = yy; dry = nm <= Trace;
                break;
            }
            if (my < 0) continue;
            float beyond;
            int fy;
            if (!dry)
            {
                var nc = Column(nx, my, nz, k);
                beyond = nc.head;
                fy = nc.y0;
                // a film thinner than the liquid runs does not run on over level ground, wet or dry, unless it is running
                if (nc.y0 == col.y0 && depth < L.Thin && was < .05f && drift <= 0 && !L.Creeps) continue;
            }
            else
            {
                // over an edge: the ground under the dry voxel, a few voxels down at most — or what is full of this
                // liquid, or any of another (pitch on water lies on the water as on ground)
                int fl = my;
                while (fl > 0 && fl > my - 8 && Open(World.Get(nx, fl - 1, nz)))
                {
                    float mb = LiquidAt(nx, fl - 1, nz, out byte kb);
                    if (kb != 0 && (kb != k || mb >= .999f)) break;
                    fl--;
                }
                beyond = fl;
                // on dry level ground it runs out only standing deep enough, or running that way; and a film half as thin
                // clings to an edge rather than trickle over it for ever
                if (col.head - my < (fl == my ? L.Thin : L.Creeps ? 0 : L.Thin * .5f) && was < .05f && drift <= 0) continue;
                fy = my;
            }
            float drive = col.head - beyond;
            // near enough level, and nothing running that way: at rest
            if (drive < still && was < Restful * 3 && drift <= 0) continue;
            float flow = was * way + pull * drive + drift;
            // never back the other way, never more than stands over the mouth
            if (drive <= 0) flow = Math.Min(flow, was * .5f + drift);
            flow = Math.Min(flow, Math.Max(0, col.head - my));
            if (flow <= Restful) continue;
            int slot = Slot(PoolAt(nx, fy, nz, true), Local(nx, fy, nz), beyond);
            _plans.Add(new Plan { Job = j, Side = s, X = nx, Y = my, Z = nz, Slot = slot, Dry = dry, Flow = flow });
            total += flow;
        }
        if (total > col.vol)
        {
            float sc = col.vol / total;
            for (int i = first; i < _plans.Count; i++) { var pl = _plans[i]; pl.Flow *= sc; _plans[i] = pl; }
            total = col.vol;
        }
        _jobGive[j] = total;
        // added up where they go once the column's own flows are cut to what is in it
        for (int i = first; i < _plans.Count; i++)
        {
            var pl = _plans[i];
            _tSum[pl.Slot] += pl.Flow;
            _tFrom[pl.Slot] = Math.Max(_tFrom[pl.Slot], col.head);
        }
    }

    /// <summary>
    /// Liquid laid on the top of the column a voxel is part of, as the top is after what was taken off it this turn:
    /// into its top voxel to full, the rest over it.
    /// </summary>
    void LayOnTop(int x, int y, int z, byte k, float f)
    {
        var col = Column(x, y, z, k);
        int yt = col.y1;
        while (yt > col.y0)
        {
            var q = PoolAt(x, yt, z, true);
            int lq = Local(x, yt, z);
            if (q.C[lq].M + q.C[lq].D > Trace) break;
            yt--;
        }
        while (true)
        {
            var q = PoolAt(x, yt, z, true);
            int lq = Local(x, yt, z);
            float into = Math.Min(f, Math.Max(0, 1 - q.C[lq].M - q.C[lq].D));
            if (into > 0) { Put(x, yt, z, k, into); f -= into; }
            if (f <= 0) return;
            int up = yt + 1;
            if (up > col.y1 + 4 || up >= World.SY || !Open(World.Get(x, up, z))) break;
            LiquidAt(x, up, z, out byte ka);
            if (ka != 0 && ka != k) break;
            yt = up;
        }
        // under a roof, pressed
        Put(x, yt, z, k, f);
    }

    /// <summary>So much of a liquid taken from a voxel this turn.</summary>
    void Take(Pool p, int li, int c, float f)
    {
        p.C[li].D -= f;
        p.C[li].O += f;
        if (p.C[li].Moved != _turn) { p.C[li].Moved = _turn; _moved.Add(c); }
    }

    /// <summary>So much of a liquid laid into a voxel this turn (a dry one is the first liquid's to come into it).</summary>
    void Put(int x, int y, int z, byte k, float f)
    {
        var p = PoolAt(x, y, z, true);
        int li = Local(x, y, z);
        p.C[li].D += f;
        if (p.C[li].K == 0) p.C[li].K = k;
        if (p.C[li].Moved != _turn) { p.C[li].Moved = _turn; _moved.Add(WaterPlanner.Pack(x, y, z)); }
    }

    /// <summary>So much of a liquid passed from one voxel to another this turn (added up when the turn is over).</summary>
    void Give(Pool from, int lf, int cf, Pool to, int lt, int ct, byte k, float f)
    {
        from.C[lf].D -= f;
        from.C[lf].O += f;
        to.C[lt].D += f;
        if (to.C[lt].K == 0) to.C[lt].K = k;
        if (from.C[lf].Moved != _turn) { from.C[lf].Moved = _turn; _moved.Add(cf); }
        if (to.C[lt].Moved != _turn) { to.C[lt].Moved = _turn; _moved.Add(ct); }
    }

    /// <summary>What came and went at a voxel this turn added up and shown; it and what lies about it looked at next turn if it changed.</summary>
    void Settle(int c)
    {
        var (x, y, z) = WaterPlanner.Unpack(c);
        var p = PoolAt(x, y, z, true);
        int li = Local(x, y, z);
        float d = p.C[li].D;
        byte k0 = p.C[li].K;
        p.C[li].D = 0;
        p.C[li].O = 0;
        float m = p.C[li].M + d;
        if (m < Trace) { LiquidDried += m; m = 0; }
        p.C[li].M = m;
        if (m == 0) p.C[li].K = 0;
        Show(p, li, x, y, z);
        if (Math.Abs(d) > Restful)
        {
            // what lies under it need not run again for this unless it is now the top of what is left
            p.Stirred = Time;
            Due(x, y, z);
            Due(x + 1, y, z); Due(x - 1, y, z); Due(x, y + 1, z); Due(x, y, z + 1); Due(x, y, z - 1);
            if (k0 == 0 || m < Liquids[k0].ShowOn) Due(x, y - 1, z);
        }
        else if (m > 0 && Math.Abs(d) > Restful * .5f) Due(x, y, z);
    }

    /// <summary>The world shows a voxel as the liquid in it once it holds enough, and as air once it holds too little.</summary>
    void Show(Pool p, int li, int x, int y, int z)
    {
        byte k = p.C[li].K;
        float m = p.C[li].M;
        byte cur = World.Get(x, y, z);
        byte shown = LiquidOf[cur];
        bool was = _liquidWriting;
        _liquidWriting = true;
        try
        {
            if (k != 0 && shown != k && m >= Liquids[k].ShowOn && Open(cur))
            {
                World.Set(x, y, z, Liquids[k].Mat);
                if (k == LiquidWater) { MarkArrival(x, z); Flooded++; }
            }
            else if (shown != 0 && (k != shown || m < Liquids[shown].ShowOff)) World.Set(x, y, z, Mat.Air);
        }
        finally { _liquidWriting = was; }
    }

    /// <summary>
    /// Every few seconds: the amounts kept for blocks long at rest are let go — where every voxel holds a whole voxel of
    /// a liquid or nothing, at once (what is left over is so little it is lost); otherwise the voxels only part full are
    /// kept by themselves, and the block is woken when anything about it runs again.
    /// </summary>
    void LetGoOfPools()
    {
        for (int i = _poolList.Count - 1; i >= 0; i--)
        {
            var p = _poolList[i];
            if (Time - p.Stirred < 6) continue;
            List<(int, byte, float)> parts = null;
            float lost = 0;
            bool due = false;
            for (int j = 0; j < B3; j++)
            {
                if (p.C[j].Due > _turn) { due = true; break; }
                float m = p.C[j].M;
                byte k = p.C[j].K;
                if (k == 0) { lost += m; continue; }
                if (Math.Abs(m - 1) <= .01f) { lost += m - 1; continue; }
                (parts ??= new()).Add((j, k, m));
            }
            if (due) continue;
            LiquidDried += lost;
            if (parts != null) { _sleeping[p.Bi] = parts; _asleep[p.Bi] = true; }
            _pools[p.Bi] = null;
            // kept to be used again, rather than made anew (each is a few hundred kilobytes)
            if (_spare.Count < SpareMost) { Array.Clear(p.C); Array.Clear(p.P); Array.Clear(p.In); _spare.Push(p); }
            _poolList.RemoveAt(i);
        }
    }

    /// <summary>A liquid still running: something on the list to look at.</summary>
    bool Running(int k) => _busy[k] > 0;

    // ------------------------------------------------------------ saving

    /// <summary>The amounts where something ran: each voxel kept with a liquid not whole, those of blocks let go, and the list to look at.</summary>
    void WriteLiquids(BinaryWriter w)
    {
        var all = new List<(int bi, int li, byte k, float m)>();
        foreach (var p in _poolList)
            for (int li = 0; li < B3; li++)
                if (p.C[li].K != 0 ? p.C[li].M != 1 : p.C[li].M != 0) all.Add((p.Bi, li, p.C[li].K, p.C[li].M));
        foreach (var (bi, parts) in _sleeping)
            foreach (var (li, k, m) in parts) all.Add((bi, li, k, m));
        w.Write(all.Count);
        foreach (var (bi, li, k, m) in all) { w.Write(bi); w.Write((ushort)li); w.Write(k); w.Write(m); }
        w.Write(_next.Count);
        foreach (int c in _next) w.Write(c);
    }

    void ReadLiquids(BinaryReader r)
    {
        MakePools(); _poolList.Clear(); _sleeping.Clear(); _now.Clear(); _next.Clear(); Current.Clear();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            int bi = r.ReadInt32(), li = r.ReadUInt16();
            byte k = r.ReadByte(); float m = r.ReadSingle();
            if (!_sleeping.TryGetValue(bi, out var parts)) _sleeping[bi] = parts = new();
            parts.Add((li, k, m));
            _asleep[bi] = true;
        }
        int due = r.ReadInt32();
        for (int i = 0; i < due; i++) { var (x, y, z) = WaterPlanner.Unpack(r.ReadInt32()); Due(x, y, z); }
    }
}
