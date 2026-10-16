using System.Globalization;

namespace VoxelTower.Core;

/// <summary>Russian number formatting without depending on the system's culture data.</summary>
public static class Fmt
{
    public static string One(double v) => v.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
    public static string Int(double v) => ((long)System.Math.Round(v)).ToString(CultureInfo.InvariantCulture);
    public static string M3(int voxels) => One(voxels / (double)VoxelWorld.PerM3);
    public static string M3(double voxels) => One(voxels / VoxelWorld.PerM3);

    /// <summary>"40 с", "3 мин", "1 ч 10 мин": how long a job takes.</summary>
    public static string Duration(double seconds)
    {
        if (seconds < 59.5) return $"{System.Math.Max(1, (int)System.Math.Round(seconds))} с";
        if (seconds < 3570) return $"{(int)System.Math.Round(seconds / 60)} мин";
        int h = (int)(seconds / 3600), m = (int)System.Math.Round((seconds - h * 3600) / 60);
        return m > 0 ? $"{h} ч {m} мин" : $"{h} ч";
    }

    /// <summary>"4:05": a countdown.</summary>
    public static string Clock(double seconds)
    {
        int s = System.Math.Max(0, (int)System.Math.Ceiling(seconds));
        return $"{s / 60}:{s % 60:00}";
    }
}
