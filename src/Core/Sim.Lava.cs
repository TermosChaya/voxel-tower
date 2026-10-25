using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>
/// Lava: molten rock, the slowest of the liquids (Sim.Liquids.cs), glowing and running in a tongue. It sets alight
/// whatever burns that it touches — timber, grass, pitch — and burns whoever stands in it terribly. It cools as it
/// lies: from its skin inwards, after a while at rest, to dark stone; and meeting water it sets at once, the water
/// boiling off it in steam. It comes into the valley where a falling star strikes (Sim.Wonders.cs), and wherever it
/// is poured.
/// </summary>
public sealed partial class Sim
{
    /// <summary>Every voxel of lava in the valley, packed.</summary>
    public readonly HashSet<int> Lavas = new();
    // when each came to lie where it is (game seconds)
    readonly Dictionary<int, double> _lavaSince = new();
    readonly List<int> _cooled = new();
    double _lavaAt;

    /// <summary>Lava met water: where, the lava set and the water boiled off (for the steam and its hiss).</summary>
    public event Action<int, int, int> Quenched;
    /// <summary>Voxels of lava set to stone since the start, by cooling or by water.</summary>
    public int LavaSet { get; private set; }

    /// <summary>Lava lies this long, seconds, before its skin begins to set.</summary>
    const float LavaWarm = 12;
    /// <summary>What a voxel of lava with air or stone beside it sets in a second once it begins to (inside, a fifth of that).</summary>
    const float LavaCools = .06f;
    /// <summary>Health a second it costs to stand in it.</summary>
    const float LavaBurns = 40;

    void OnLavaEdit(int x, int y, int z, byte old)
    {
        byte now = World.Get(x, y, z);
        int p = WaterPlanner.Pack(x, y, z);
        if (now == Mat.Lava) { if (Lavas.Add(p)) _lavaSince[p] = Time; }
        else if (old == Mat.Lava) { Lavas.Remove(p); _lavaSince.Remove(p); }
    }

    static readonly (int dx, int dy, int dz)[] Faces = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

    /// <summary>
    /// A voxel of lava or of water on its turn, with lava about: lava beside water sets to stone, and the water by it
    /// boils away in part; lava beside what burns sets it alight. True when the voxel itself is gone into stone.
    /// </summary>
    bool Meets(int x, int y, int z, byte k)
    {
        for (int d = 0; d < 6; d++)
        {
            var (dx, dy, dz) = Faces[d];
            int nx = x + dx, ny = y + dy, nz = z + dz;
            byte m = World.Get(nx, ny, nz);
            if (k == LiquidLava)
            {
                if (m == Mat.Water) { Quench(x, y, z, nx, ny, nz); return true; }
                if (Mat.Burns(m) && !IsBurning(nx, ny, nz) && Rng.NextDouble() < .2) Ignite(nx, ny, nz);
            }
            else if (m == Mat.Lava) { Quench(nx, ny, nz, x, y, z); return false; }
        }
        return false;
    }

    /// <summary>The lava at one voxel set to stone by the water at another, a part of that water boiled off.</summary>
    void Quench(int lx, int ly, int lz, int wx, int wy, int wz)
    {
        Become(lx, ly, lz, Mat.Basalt);
        LavaSet++;
        var p = PoolAt(wx, wy, wz, false);
        if (p != null)
        {
            int li = Local(wx, wy, wz);
            float boiled = Math.Min(p.C[li].M - p.C[li].O, .4f);
            if (boiled > 0)
            {
                p.C[li].D -= boiled; p.C[li].O += boiled;
                LiquidDried += boiled;
                if (p.C[li].Moved != _turn) { p.C[li].Moved = _turn; _moved.Add(WaterPlanner.Pack(wx, wy, wz)); }
            }
        }
        Quenched?.Invoke(lx, ly, lz);
    }

    /// <summary>Lava cooling where it lies, burning what stands in it; every half a second.</summary>
    void TickLava()
    {
        if (Lavas.Count == 0 || Time - _lavaAt < .5) return;
        float dt = (float)Math.Min(1, Time - _lavaAt);
        _lavaAt = Time;
        _cooled.Clear();
        foreach (int p in Lavas)
        {
            if (Time - _lavaSince.GetValueOrDefault(p, Time) < LavaWarm) continue;
            var (x, y, z) = WaterPlanner.Unpack(p);
            // still running, it keeps its heat
            var pool = PoolAt(x, y, z, false);
            if (pool != null && pool.C[Local(x, y, z)].Due > _turn) continue;
            // its skin sets first: what has air over it or stone beside it
            bool skin = false;
            for (int d = 0; d < 6 && !skin; d++)
            {
                byte m = World.Get(x + Faces[d].dx, y + Faces[d].dy, z + Faces[d].dz);
                skin = m != Mat.Lava;
            }
            if (Rng.NextDouble() < LavaCools * dt * (skin ? 1 : .2f)) _cooled.Add(p);
        }
        foreach (int p in _cooled)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            Become(x, y, z, Mat.Basalt);
            LavaSet++;
        }
        // whoever stands in it or on it burns, and badly
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null) continue;
            int x = (int)MathF.Floor(u.X), y = (int)MathF.Floor(u.Y), z = (int)MathF.Floor(u.Z);
            if (World.Get(x, y, z) != Mat.Lava && World.Get(x, y - 1, z) != Mat.Lava) continue;
            u.Burn = Math.Max(u.Burn, 6);
            u.BurnHeat = Math.Max(u.BurnHeat, 3);
            Hurt(u, null, LavaBurns * dt * (Kinds.IsEngine(u.T) ? 2 : 1), "лава");
        }
    }

    /// <summary>Molten rock let out on the ground about a spot, so many voxels of it, the most in the middle.</summary>
    public void LetLava(int x, int z, float voxels)
    {
        float all = 0;
        for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) all += 3 - Math.Max(Math.Abs(dx), Math.Abs(dz));
        for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int sx = x + dx, sz = z + dz;
                if (sx < 0 || sz < 0 || sx >= World.SX || sz >= World.SZ) continue;
                int y = World.SurfAt(sx, sz);
                while (y < World.SY - 1 && !Open(World.Get(sx, y, sz))) y++;
                Pour(Mat.Lava, sx, y, sz, voxels * (3 - Math.Max(Math.Abs(dx), Math.Abs(dz))) / all);
            }
    }

    /// <summary>
    /// One caught where lava ran (it is got out of it at once, the cell being as deep water to it): burnt badly, and
    /// burning on a while.
    /// </summary>
    void Scald(Unit u)
    {
        // anywhere about where it stands: the lava has reached the ground it stands on
        int x = (int)MathF.Floor(u.X), y = (int)MathF.Floor(u.Y), z = (int)MathF.Floor(u.Z), r = NavGrid.CS / 2;
        bool lava = false;
        for (int dy = -1; dy < 3 && !lava; dy++)
            for (int dz = -r; dz <= r && !lava; dz++)
                for (int dx = -r; dx <= r && !lava; dx++) lava = World.Get(x + dx, y + dy, z + dz) == Mat.Lava;
        if (!lava || u.Dead) return;
        u.Burn = Math.Max(u.Burn, 6);
        u.BurnHeat = Math.Max(u.BurnHeat, 3);
        Hurt(u, null, LavaBurns * .5f * (Kinds.IsEngine(u.T) ? 2 : 1), "лава");
    }

    void WriteLava(BinaryWriter w)
    {
        w.Write(LavaSet);
        w.Write(_lavaSince.Count);
        foreach (var (p, t) in _lavaSince) { w.Write(p); w.Write(t); }
    }

    void ReadLava(BinaryReader r)
    {
        LavaSet = r.ReadInt32();
        _lavaSince.Clear(); Lavas.Clear();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++) { int p = r.ReadInt32(); _lavaSince[p] = r.ReadDouble(); Lavas.Add(p); }
    }
}
