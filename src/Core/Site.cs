using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

public enum LandmarkKind : byte { Mine, Ruin, Power }

/// <summary>
/// A named place of the valley: a mine (what it pays a minute while it is ours, whether the horde camps at it), a ruin, or
/// a place of power, where prayer is heard better than anywhere else.
/// </summary>
public sealed class Landmark
{
    public LandmarkKind Kind;
    public string Name;
    /// <summary>Its middle and how far it spreads, in voxels; for a mine, the spot in front of the adit to hold.</summary>
    public int X, Z, R, StandX, StandZ;
    public int Income;
    public bool Guarded;
}

/// <summary>Where things stand on the map: the citadel, the starter wall, torch positions.</summary>
public sealed class Site
{
    public int KeepX = 286, KeepZ = 112, KeepR = 24;
    public int KeepBase = 72;
    public int WallX = 232;
    /// <summary>The strip along the western edge where the horde walks in; nothing may be built there.</summary>
    public int SpawnStrip = 16;

    /// <summary>Torch and brazier lights, in voxel coordinates.</summary>
    public readonly List<(float X, float Y, float Z)> Torches = new();

    /// <summary>Height of the bare terrain per column (index z·SX + x), before rocks, trees and walls.</summary>
    public int[] HTop;

    /// <summary>How high water stood in each column when the valley was made; 0 where it was dry, null if there was none.</summary>
    public short[] WaterTop;

    /// <summary>The river's middle and half its width at each z (it runs north to south, toward larger z); null if none.</summary>
    public float[] RiverMid, RiverHalfWidth;

    /// <summary>The ponds the valley was made with: centre, radius and the height their water stood to.</summary>
    public readonly List<(int X, int Z, int R, int Level)> Ponds = new();

    /// <summary>Mines worth holding and ruins of what stood here before: things to find, take and keep.</summary>
    public readonly List<Landmark> Landmarks = new();

    /// <summary>What the ruins were meant to be, voxel by voxel, so they can be raised again; the game keeps adding to it.</summary>
    public VoxelWorld Design;
    /// <summary>Where the old bridges arch over the river: they stand as long as they touch the ground (<see cref="Sim.Arched"/>).</summary>
    public VoxelWorld Arched;

    /// <summary>Columns nobody may build on: the map's western edge (the horde's entry), the citadel and its gate approach.</summary>
    public bool Forbidden(int x, int z)
    {
        if (x < SpawnStrip) return true;
        if (Math.Sqrt((double)(x - KeepX) * (x - KeepX) + (double)(z - KeepZ) * (z - KeepZ)) < KeepR + 3) return true;
        if (x >= KeepX - KeepR - 16 && x < KeepX - KeepR && Math.Abs(z - KeepZ) < 8) return true;
        return false;
    }

    /// <summary>The old road through the lowland, as a z for each x.</summary>
    public static double PathZ(double x) => 100 + 96 * Smooth01(x / 150) + Math.Sin(x * .045) * 7;

    public static double Smooth01(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
}
