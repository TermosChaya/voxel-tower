using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

public static class Prices
{
    /// <summary>Gold per cubic metre. Earth is not bought: it comes out of the moat.</summary>
    public const double Stone = 10, Wood = 5;
    /// <summary>Gold per cubic metre paid to the diggers.</summary>
    public const double Dig = 1;
    /// <summary>Share of the price returned when you take your own masonry apart.</summary>
    public const double Refund = .5;
}

/// <summary>Material counts of a plan, in voxels, and what they cost.</summary>
public readonly struct Cost
{
    public readonly int Stone, Wood, Earth;
    public Cost(int stone, int wood, int earth) { Stone = stone; Wood = wood; Earth = earth; }
    public int Gold => (int)Math.Ceiling((Stone * Prices.Stone + Wood * Prices.Wood) / VoxelWorld.PerM3);
    public double EarthM3 => Earth / (double)VoxelWorld.PerM3;
    public bool IsEmpty => Stone == 0 && Wood == 0 && Earth == 0;
    public int Voxels => Stone + Wood + Earth;
}

public sealed class Wallet
{
    public double Gold = 420;
    /// <summary>Cubic metres of dug earth in store.</summary>
    public double Earth = 24;
}

public enum BuildResult { Ok, Nothing, Occupied, NoGold, NoEarth }

/// <summary>Turns plans into masonry when the treasury can pay. Building is instant.</summary>
public static class Builder
{
    public static Cost CostOf(VoxelList L)
    {
        int st = 0, wd = 0, ea = 0;
        if (L == null) return default;
        for (int i = 0; i < L.Count; i++)
        {
            byte m = L.M(i);
            if (m == Mat.Stone || m == Mat.Mortar) st++; else if (m == Mat.Wood) wd++; else if (m == Mat.Rammed) ea++;
        }
        return new Cost(st, wd, ea);
    }

    public static BuildResult Check(Wallet wal, VoxelList L, out Cost cost)
    {
        cost = default;
        if (L == null || L.Count == 0) return BuildResult.Nothing;
        cost = CostOf(L);
        if (cost.IsEmpty) return BuildResult.Occupied;
        if (wal.Gold < cost.Gold) return BuildResult.NoGold;
        if (wal.Earth + 1e-6 < cost.EarthM3) return BuildResult.NoEarth;
        return BuildResult.Ok;
    }

    public static BuildResult TryBuild(VoxelWorld w, Wallet wal, VoxelList L, out Cost cost)
    {
        var r = Check(wal, L, out cost);
        if (r != BuildResult.Ok) return r;
        wal.Gold -= cost.Gold;
        wal.Earth -= cost.EarthM3;
        for (int i = 0; i < L.Count; i++) w.Set(L.X(i), L.Y(i), L.Z(i), L.M(i));
        return BuildResult.Ok;
    }

    /// <summary>Writes a plan without paying: world generation and tests.</summary>
    public static void ApplyFree(VoxelWorld w, VoxelList L, bool raw)
    {
        if (L == null) return;
        for (int i = 0; i < L.Count; i++)
        {
            if (raw) w.SetRaw(L.X(i), L.Y(i), L.Z(i), L.M(i));
            else w.Set(L.X(i), L.Y(i), L.Z(i), L.M(i));
        }
    }

    /// <summary>
    /// Re-lays the player's own voxels in another material: the new material is paid in full,
    /// the old one returns at the usual share (earth goes back to the store whole).
    /// </summary>
    public static BuildResult TryRepaint(VoxelWorld w, Wallet wal, VoxelList L, out Cost cost, out double refund)
    {
        cost = default; refund = 0;
        if (L == null || L.Count == 0) return BuildResult.Nothing;
        cost = CostOf(L);
        double earthBack = 0;
        for (int i = 0; i < L.Count; i++)
        {
            byte old = w.Get(L.X(i), L.Y(i), L.Z(i));
            if (old == Mat.Stone || old == Mat.Mortar) refund += Prices.Stone * Prices.Refund / VoxelWorld.PerM3;
            else if (old == Mat.Wood) refund += Prices.Wood * Prices.Refund / VoxelWorld.PerM3;
            else if (old == Mat.Rammed) earthBack += 1.0 / VoxelWorld.PerM3;
        }
        if (wal.Gold + refund < cost.Gold) return BuildResult.NoGold;
        if (wal.Earth + earthBack + 1e-6 < cost.EarthM3) return BuildResult.NoEarth;
        wal.Gold += refund - cost.Gold;
        wal.Earth += earthBack - cost.EarthM3;
        for (int i = 0; i < L.Count; i++) w.Set(L.X(i), L.Y(i), L.Z(i), L.M(i));
        return BuildResult.Ok;
    }

    public enum DigResult { Ok, Nothing, NoGold }

    /// <summary>
    /// Digs a disc down to floorY. Spades take loose earth and plants, never rock or masonry.
    /// Every cubic metre costs the diggers' pay and goes into the earth store.
    /// </summary>
    public static DigResult Dig(VoxelWorld w, Site site, Wallet wal, int cx, int cz, int floorY, int r, out int earthVox, Action<int, int, int, byte> removed = null)
    {
        earthVox = 0;
        var list = new List<int>();
        for (int z = cz - r; z <= cz + r; z++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= w.SX || z < 0 || z >= w.SZ || Brushes.Dist(x - cx, z - cz) > r || site.Forbidden(x, z)) continue;
                for (int y = w.SY - 1; y >= Math.Max(1, floorY); y--)
                {
                    byte m = w.Get(x, y, z);
                    if (m == 0) continue;
                    if (!Mat.IsEarthy[m] && !Mat.IsVeg[m]) break;
                    list.Add(x); list.Add(y); list.Add(z);
                    if (Mat.IsEarthy[m]) earthVox++;
                }
            }
        if (list.Count == 0) return DigResult.Nothing;
        double gold = earthVox * Prices.Dig / VoxelWorld.PerM3;
        if (wal.Gold < gold) return DigResult.NoGold;
        wal.Gold -= gold;
        wal.Earth += earthVox / (double)VoxelWorld.PerM3;
        for (int n = 0; n < list.Count; n += 3)
        {
            byte m = w.Get(list[n], list[n + 1], list[n + 2]);
            w.Set(list[n], list[n + 1], list[n + 2], Mat.Air);
            removed?.Invoke(list[n], list[n + 1], list[n + 2], m);
        }
        return DigResult.Ok;
    }

    /// <summary>Takes apart a ball of the player's own masonry; half the price comes back, earth goes to the store.</summary>
    public static int RemoveSphere(VoxelWorld w, Wallet wal, int cx, int cy, int cz, int r, Action<int, int, int, byte> removed = null)
        => RemoveSphere(w, wal, cx, cy, cz, r + .3, removed);

    public static int RemoveSphere(VoxelWorld w, Wallet wal, int cx, int cy, int cz, double radius, Action<int, int, int, byte> removed = null)
    {
        int st = 0, wd = 0, ea = 0, r = (int)Math.Ceiling(radius);
        for (int y = Math.Max(1, cy - r); y <= Math.Min(w.SY - 1, cy + r); y++)
            for (int z = Math.Max(0, cz - r); z <= Math.Min(w.SZ - 1, cz + r); z++)
                for (int x = Math.Max(0, cx - r); x <= Math.Min(w.SX - 1, cx + r); x++)
                {
                    if (Brushes.Dist(x - cx, y - cy, z - cz) > radius + .01) continue;
                    byte m = w.Get(x, y, z);
                    if (m == 0 || !Mat.IsPlayer[m]) continue;
                    if (m == Mat.Stone || m == Mat.Mortar) st++; else if (m == Mat.Wood) wd++; else if (m == Mat.Rammed) ea++;
                    w.Set(x, y, z, Mat.Air);
                    removed?.Invoke(x, y, z, m);
                }
        wal.Gold += (st * Prices.Stone + wd * Prices.Wood) * Prices.Refund / VoxelWorld.PerM3;
        wal.Earth += ea / (double)VoxelWorld.PerM3;
        return st + wd + ea;
    }
}
