using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoxelTower.Core;

/// <summary>
/// A copy of something the player built: vertical runs of the player's own materials inside a rectangle.
/// Pasting writes the same runs elsewhere, rotated in quarter turns, and costs what the masonry costs.
/// </summary>
public sealed class Blueprint
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Footprint and height, in voxels.</summary>
    public int W { get; set; }
    public int D { get; set; }
    public int H { get; set; }
    /// <summary>Five ints per run: dx, dz, y above the lowest run, length, material.</summary>
    public int[] Runs { get; set; } = Array.Empty<int>();
    public int Stone { get; set; }
    public int Wood { get; set; }
    public int Earth { get; set; }

    [JsonIgnore]
    public Cost Cost => new(Stone, Wood, Earth);

    public static Blueprint Capture(VoxelWorld w, int x0, int z0, int x1, int z1, int id, string name)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        x0 = Math.Max(0, x0); z0 = Math.Max(0, z0); x1 = Math.Min(w.SX - 1, x1); z1 = Math.Min(w.SZ - 1, z1);
        var runs = new List<int>();
        int ymin = w.SY, ymax = 0, st = 0, wd = 0, ea = 0;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int y = 1;
                while (y < w.SY)
                {
                    byte m = w.Get(x, y, z);
                    if (m != 0 && Mat.IsPlayer[m])
                    {
                        int len = 1;
                        while (y + len < w.SY && w.Get(x, y + len, z) == m) len++;
                        runs.Add(x - x0); runs.Add(z - z0); runs.Add(y); runs.Add(len); runs.Add(m);
                        ymin = Math.Min(ymin, y); ymax = Math.Max(ymax, y + len);
                        if (Mat.IsMasonry(m)) st += len; else if (m == Mat.Wood) wd += len; else ea += len;
                        y += len;
                    }
                    else y++;
                }
            }
        if (runs.Count == 0) return null;
        for (int r = 2; r < runs.Count; r += 5) runs[r] -= ymin;
        return new Blueprint
        {
            Id = id, Name = string.IsNullOrEmpty(name) ? $"Чертёж {id}" : name,
            W = x1 - x0 + 1, D = z1 - z0 + 1, H = ymax - ymin,
            Runs = runs.ToArray(), Stone = st, Wood = wd, Earth = ea,
        };
    }

    /// <summary>
    /// A blueprint of these voxels only (what the magic wand took), not of everything in their rectangle: of them what
    /// is ours, in runs up each column as <see cref="Capture"/> keeps it. Null when none is ours.
    /// </summary>
    public static Blueprint FromVoxels(VoxelList L, int id, string name)
    {
        var cols = new Dictionary<(int x, int z), List<(int y, byte m)>>();
        int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
        for (int i = 0; i < L.Count; i++)
        {
            byte m = L.M(i);
            if (m == 0 || !Mat.IsPlayer[m]) continue;
            int x = L.X(i), z = L.Z(i);
            if (!cols.TryGetValue((x, z), out var col)) cols[(x, z)] = col = new List<(int, byte)>();
            col.Add((L.Y(i), m));
            x0 = Math.Min(x0, x); z0 = Math.Min(z0, z); x1 = Math.Max(x1, x); z1 = Math.Max(z1, z);
        }
        if (cols.Count == 0) return null;
        var runs = new List<int>();
        int ymin = int.MaxValue, ymax = 0, st = 0, wd = 0, ea = 0;
        foreach (var ((x, z), col) in cols)
        {
            col.Sort((a, b) => a.y.CompareTo(b.y));
            for (int k = 0; k < col.Count;)
            {
                var (y, m) = col[k];
                int len = 1;
                while (k + len < col.Count && col[k + len].y == y + len && col[k + len].m == m) len++;
                runs.Add(x - x0); runs.Add(z - z0); runs.Add(y); runs.Add(len); runs.Add(m);
                ymin = Math.Min(ymin, y); ymax = Math.Max(ymax, y + len);
                if (Mat.IsMasonry(m)) st += len; else if (m == Mat.Wood) wd += len; else ea += len;
                k += len;
            }
        }
        for (int r = 2; r < runs.Count; r += 5) runs[r] -= ymin;
        return new Blueprint
        {
            Id = id, Name = string.IsNullOrEmpty(name) ? $"Чертёж {id}" : name,
            W = x1 - x0 + 1, D = z1 - z0 + 1, H = ymax - ymin,
            Runs = runs.ToArray(), Stone = st, Wood = wd, Earth = ea,
        };
    }

    public int RotW(int rot) => (rot & 1) == 1 ? D : W;
    public int RotD(int rot) => (rot & 1) == 1 ? W : D;

    static (int x, int z) Rot(int x, int z, int w, int d, int r) => (r & 3) switch
    {
        0 => (x, z),
        1 => (d - 1 - z, x),
        2 => (w - 1 - x, d - 1 - z),
        _ => (z, w - 1 - x),
    };

    /// <summary>Plans a copy with its corner at (ax, az), its lowest run resting at baseY.</summary>
    public VoxelList PlanPaste(VoxelWorld w, Site site, int ax, int az, int rot, int baseY)
    {
        var L = new VoxelList();
        var r = Runs;
        for (int n = 0; n < r.Length; n += 5)
        {
            var (rx, rz) = Rot(r[n], r[n + 1], W, D, rot);
            int x = ax + rx, z = az + rz;
            if (x < 0 || x >= w.SX || z < 0 || z >= w.SZ || site.Forbidden(x, z)) continue;
            int y0 = baseY + r[n + 2];
            for (int y = Math.Max(0, y0); y < y0 + r[n + 3] && y < w.SY; y++)
            {
                byte cur = w.Get(x, y, z);
                if (cur != 0 && !Mat.IsVeg[cur]) continue;
                L.Add(x, y, z, (byte)r[n + 4]);
            }
        }
        return L;
    }

    /// <summary>"камень 2,5 · брёвна 0,5 · земля 5,8 м³"</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Stone > 0) parts.Add("камень " + Fmt.M3(Stone));
        if (Wood > 0) parts.Add("брёвна " + Fmt.M3(Wood));
        if (Earth > 0) parts.Add("земля " + Fmt.M3(Earth));
        return string.Join(" · ", parts) + " м³";
    }
}
