using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What the stores hold. Materials are counted in voxels (4096 to the cubic metre).</summary>
public sealed class Stockpile
{
    public const int None = 0, Stone = 1, Wood = 2, Earth = 3;
    public double Gold = 150;
    readonly double[] _v = new double[4];

    public double this[int kind] { get => _v[kind]; set => _v[kind] = value; }

    public static string Name(int kind) => kind switch { Stone => "камень", Wood => "брёвна", Earth => "земля", _ => "" };

    public bool Take(int kind, double n)
    {
        if (_v[kind] + 1e-9 < n) return false;
        _v[kind] -= n;
        return true;
    }
}

/// <summary>
/// The work in a voxel: how long it takes to lay or to dig out, and what it gives back.
/// Units of work; a worker does <see cref="WorkPerSecond"/> of them a second.
/// </summary>
public static class Labour
{
    public const double WorkPerSecond = 300;
    /// <summary>How far from where they stand pawns reach: a cell to each side, 1.5 m down and 2.5 m up.</summary>
    public const int ReachDown = 24, ReachUp = 40;

    static readonly float[] DigCost = new float[Mat.Count], LayCost = new float[Mat.Count], Share = new float[Mat.Count];
    static readonly byte[] Kind = new byte[Mat.Count];

    static Labour()
    {
        void D(byte m, float dig, int kind, float share) { DigCost[m] = dig; Kind[m] = (byte)kind; Share[m] = share; }
        foreach (var m in new[] { Mat.Soil, Mat.Grass, Mat.Path, Mat.Tuft }) D(m, 1, Stockpile.Earth, 1);
        D(Mat.Rammed, 1, Stockpile.Earth, 1);
        D(Mat.Rock, 4, Stockpile.Stone, 1); D(Mat.Moss, 4, Stockpile.Stone, 1);
        D(Mat.Stone, 2.5f, Stockpile.Stone, .75f); D(Mat.Mortar, 2.5f, Stockpile.Stone, .75f);
        // the voxel trunks are drawn thinner than real ones: a voxel of trunk stands for six of timber,
        // and needles and leaves give a little brushwood
        D(Mat.Trunk, 2, Stockpile.Wood, 6); D(Mat.Wood, 2, Stockpile.Wood, .75f);
        D(Mat.Leaf, .25f, Stockpile.Wood, .15f); D(Mat.Foliage, .25f, Stockpile.Wood, .15f);
        // ladders come apart quickly, and what the horde leaves behind is timber too
        D(Mat.Ladder, 1, Stockpile.Wood, .75f); D(Mat.SiegeLadder, .5f, Stockpile.Wood, .75f); D(Mat.Deck, 1.5f, Stockpile.Wood, .75f);
        D(Mat.Gate, 2.5f, Stockpile.Wood, .75f); D(Mat.GateShut, 2.5f, Stockpile.Wood, .75f);
        LayCost[Mat.Rammed] = 1; LayCost[Mat.Wood] = 1.5f; LayCost[Mat.Stone] = 2; LayCost[Mat.Mortar] = 2;
        // a ladder is joinery: rails and rungs take longer than the logs they are made of; a gate more so
        LayCost[Mat.Ladder] = 6;
        LayCost[Mat.Gate] = 4;
    }

    /// <summary>Pawns can take this out of the world (bedrock and the citadel they cannot).</summary>
    public static bool Diggable(byte m) => m != 0 && DigCost[m] > 0;
    public static float Dig(byte m) => DigCost[m];
    public static float Lay(byte m) => LayCost[m] > 0 ? LayCost[m] : 1;
    /// <summary>The store a voxel of this material goes to when dug, and how many voxels of it arrive.</summary>
    public static int YieldKind(byte m) => Kind[m];
    public static float YieldShare(byte m) => Share[m];
    /// <summary>The store a voxel of this material is laid from.</summary>
    public static int CostKind(byte m) => m switch
    {
        Mat.Stone or Mat.Mortar => Stockpile.Stone,
        Mat.Wood or Mat.Ladder or Mat.Gate or Mat.GateShut => Stockpile.Wood,
        Mat.Rammed => Stockpile.Earth,
        _ => Stockpile.None,
    };

    /// <summary>
    /// What a plan asks of the stores and of the workers. Fills <paramref name="stores"/> (indexed by store) with voxels
    /// taken from it, or given to it when negative; returns seconds of one worker's labour.
    /// </summary>
    public static double Estimate(VoxelWorld w, VoxelList L, bool dig, double[] stores)
    {
        Array.Clear(stores);
        double work = 0;
        if (L == null) return 0;
        for (int i = 0; i < L.Count; i++)
        {
            byte m = L.M(i);
            if (dig)
            {
                byte cur = w.Get(L.X(i), L.Y(i), L.Z(i));
                if (!Diggable(cur)) continue;
                work += Dig(cur);
                int k = YieldKind(cur);
                if (k != Stockpile.None) stores[k] -= YieldShare(cur);
            }
            else if (m != 0)
            {
                work += Lay(m);
                int k = CostKind(m);
                if (k != Stockpile.None) stores[k] += 1;
            }
        }
        return work / WorkPerSecond;
    }
}

/// <summary>
/// Work the player has marked out: a layer as big as the world where each voxel is nothing, a material to lay,
/// or <see cref="Remove"/>. Pawns pick it up by the metre-wide column of cells.
/// </summary>
public sealed class Orders
{
    public const byte Remove = 255;

    public readonly VoxelWorld Layer;
    readonly VoxelWorld _w;
    readonly Site _site;
    readonly int _nx, _nz;
    /// <summary>Orders waiting in each column of cells (index k·NX + i).</summary>
    public readonly int[] ColCount;
    public readonly HashSet<int> Active = new();
    public int Total { get; private set; }
    public int BuildTotal { get; private set; }

    public Orders(VoxelWorld w, Site site)
    {
        _w = w; _site = site;
        Layer = new VoxelWorld(w.SX, w.SY, w.SZ);
        _nx = w.SX >> NavGrid.Shift; _nz = w.SZ >> NavGrid.Shift;
        ColCount = new int[_nx * _nz];
    }

    public byte At(int x, int y, int z) => y < 0 ? (byte)0 : Layer.Get(x, y, z);

    void Count(int x, int z, int d, bool build)
    {
        int col = (z >> NavGrid.Shift) * _nx + (x >> NavGrid.Shift);
        ColCount[col] += d;
        if (ColCount[col] > 0) Active.Add(col); else Active.Remove(col);
        Total += d;
        if (build) BuildTotal += d;
    }

    /// <summary>Marks a planned building for the pawns; returns how many voxels were added.</summary>
    public int PlanBuild(VoxelList L)
    {
        if (L == null) return 0;
        int n = 0;
        for (int i = 0; i < L.Count; i++)
        {
            byte m = L.M(i);
            int x = L.X(i), y = L.Y(i), z = L.Z(i);
            if (m == 0 || !_w.InBounds(x, y, z) || !Brushes.Free(_w.Get(x, y, z)) || _site.Forbidden(x, z)) continue;
            byte cur = Layer.Get(x, y, z);
            if (cur == m) continue;
            if (cur != 0) Count(x, z, -1, cur != Remove);
            Layer.Set(x, y, z, m);
            Count(x, z, 1, true);
            n++;
        }
        return n;
    }

    /// <summary>Marks voxels to be dug out, chopped or taken apart; returns how many were added.</summary>
    public int PlanRemove(VoxelList L)
    {
        if (L == null) return 0;
        int n = 0;
        for (int i = 0; i < L.Count; i++)
        {
            int x = L.X(i), y = L.Y(i), z = L.Z(i);
            if (!_w.InBounds(x, y, z) || !Labour.Diggable(_w.Get(x, y, z)) || _site.Forbidden(x, z)) continue;
            byte cur = Layer.Get(x, y, z);
            if (cur == Remove) continue;
            if (cur != 0) Count(x, z, -1, true);
            Layer.Set(x, y, z, Remove);
            Count(x, z, 1, false);
            n++;
        }
        return n;
    }

    /// <summary>Drops one order (done or no longer possible).</summary>
    public void Clear(int x, int y, int z)
    {
        byte cur = Layer.Get(x, y, z);
        if (cur == 0) return;
        Layer.Set(x, y, z, 0);
        Count(x, z, -1, cur != Remove);
    }

    /// <summary>Takes back every order inside a ball; returns how many.</summary>
    public int Cancel(int cx, int cy, int cz, double radius)
    {
        int n = 0, r = (int)Math.Ceiling(radius);
        for (int y = Math.Max(0, cy - r); y <= Math.Min(_w.SY - 1, cy + r); y++)
            for (int z = Math.Max(0, cz - r); z <= Math.Min(_w.SZ - 1, cz + r); z++)
                for (int x = Math.Max(0, cx - r); x <= Math.Min(_w.SX - 1, cx + r); x++)
                {
                    if (Brushes.Dist(x - cx, y - cy, z - cz) > radius + .01 || Layer.Get(x, y, z) == 0) continue;
                    Clear(x, y, z); n++;
                }
        return n;
    }

    /// <summary>Takes back every order standing on the columns of a rectangle, at any height; returns how many.</summary>
    public int CancelArea(int x0, int z0, int x1, int z1)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        x0 = Math.Max(0, x0); z0 = Math.Max(0, z0); x1 = Math.Min(_w.SX - 1, x1); z1 = Math.Min(_w.SZ - 1, z1);
        int n = 0;
        for (int cz = z0 >> VoxelWorld.Shift; cz <= z1 >> VoxelWorld.Shift; cz++)
            for (int cx = x0 >> VoxelWorld.Shift; cx <= x1 >> VoxelWorld.Shift; cx++)
                for (int cy = 0; cy < _w.CY; cy++)
                {
                    var d = Layer.ChunkData(Layer.ChunkIndex(cx, cy, cz));
                    if (d == null) continue;
                    for (int i = 0; i < d.Length; i++)
                    {
                        if (d[i] == 0) continue;
                        int x = cx * VoxelWorld.CS + (i & 31), z = cz * VoxelWorld.CS + (i >> 5 & 31), y = cy * VoxelWorld.CS + (i >> 10);
                        if (x < x0 || x > x1 || z < z0 || z > z1) continue;
                        Clear(x, y, z); n++;
                    }
                }
        return n;
    }

    /// <summary>Lowest and highest voxel of the orders in a column of cells, or false when it has none.</summary>
    public bool Span(int col, out int y0, out int y1)
    {
        int x0 = col % _nx << NavGrid.Shift, z0 = col / _nx << NavGrid.Shift;
        y0 = int.MaxValue; y1 = int.MinValue;
        for (int cy = 0; cy < _w.CY; cy++)
        {
            int ci = Layer.ChunkIndex(x0 >> VoxelWorld.Shift, cy, z0 >> VoxelWorld.Shift);
            var d = Layer.ChunkData(ci);
            if (d == null) continue;
            for (int ly = 0; ly < VoxelWorld.CS; ly++)
                for (int dz = 0; dz < NavGrid.CS; dz++)
                {
                    int row = (ly << 10) | (((z0 + dz) & VoxelWorld.Mask) << 5) | (x0 & VoxelWorld.Mask);
                    for (int dx = 0; dx < NavGrid.CS; dx++)
                        if (d[row + dx] != 0)
                        {
                            int y = cy * VoxelWorld.CS + ly;
                            if (y < y0) y0 = y;
                            if (y > y1) y1 = y;
                        }
                }
        }
        return y1 >= 0;
    }
}
