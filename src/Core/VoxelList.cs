using System;

namespace VoxelTower.Core;

/// <summary>A planned set of voxel writes: what a brush would build, before it is paid for.</summary>
public sealed class VoxelList
{
    int[] _a = new int[8192];
    public int Count { get; private set; }

    /// <summary>Base height and platform height of a planned tower, in voxels.</summary>
    public int MetaBase, MetaPlat;

    public void Add(int x, int y, int z, byte m)
    {
        int n = Count * 4;
        if (n + 4 > _a.Length) Array.Resize(ref _a, _a.Length * 2);
        _a[n] = x; _a[n + 1] = y; _a[n + 2] = z; _a[n + 3] = m;
        Count++;
    }

    public void Clear() => Count = 0;

    public int X(int i) => _a[i * 4];
    public int Y(int i) => _a[i * 4 + 1];
    public int Z(int i) => _a[i * 4 + 2];
    public byte M(int i) => (byte)_a[i * 4 + 3];

    /// <summary>Entries that put material down (the rest clear plants out of the way).</summary>
    public int Solid { get { int n = 0; for (int i = 0; i < Count; i++) if (_a[i * 4 + 3] != 0) n++; return n; } }

    public bool Bounds(out int x0, out int y0, out int z0, out int x1, out int y1, out int z1)
    {
        x0 = y0 = z0 = int.MaxValue; x1 = y1 = z1 = int.MinValue;
        bool any = false;
        for (int i = 0; i < Count; i++)
        {
            if (_a[i * 4 + 3] == 0) continue;
            any = true;
            int x = _a[i * 4], y = _a[i * 4 + 1], z = _a[i * 4 + 2];
            if (x < x0) x0 = x; if (x > x1) x1 = x;
            if (y < y0) y0 = y; if (y > y1) y1 = y;
            if (z < z0) z0 = z; if (z > z1) z1 = z;
        }
        return any;
    }
}
