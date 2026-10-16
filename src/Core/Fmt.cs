using System.Globalization;

namespace VoxelTower.Core;

/// <summary>Russian number formatting without depending on the system's culture data.</summary>
public static class Fmt
{
    public static string One(double v) => v.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
    public static string Int(double v) => ((long)System.Math.Round(v)).ToString(CultureInfo.InvariantCulture);
    public static string M3(int voxels) => One(voxels / (double)VoxelWorld.PerM3);
}
