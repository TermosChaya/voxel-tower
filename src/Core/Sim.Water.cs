using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

public sealed partial class Sim
{
    // air that water may run into, and the level of the water it came from (it fills below that, never above)
    readonly Queue<(int x, int y, int z, int level)> _flood = new();

    /// <summary>Voxels water ran into since the start.</summary>
    public int Flooded { get; private set; }

    /// <summary>Called for every voxel written: one emptied beside water or under it lets the water in.</summary>
    void OnEditWater(int x, int y, int z)
    {
        if (World.Get(x, y, z) != Mat.Air) return;
        foreach (var (dx, dy, dz) in Inflow)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (World.Get(nx, ny, nz) != Mat.Water) continue;
            int level = Surface(nx, ny, nz);
            if (y < level && Dug(x, y, z)) _flood.Enqueue((x, y, z, level));
            return;
        }
    }

    // water comes in from the sides and from above, never up from below
    static readonly (int, int, int)[] Inflow = { (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1), (0, 1, 0) };
    static readonly (int, int, int)[] Seep = { (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1), (0, -1, 0), (0, 1, 0) };

    /// <summary>The height a body of water stands to (exclusive): up its column from a voxel of it.</summary>
    int Surface(int x, int y, int z)
    {
        while (y + 1 < World.SY && World.Get(x, y + 1, z) == Mat.Water) y++;
        return y + 1;
    }

    /// <summary>
    /// Whether a voxel lies in something dug out: below the bare ground, or the water over it, as the valley was made.
    /// Water runs only into such hollows and never out over the land, so a ditch cut from a pond fills, and the
    /// meadow beyond it stays dry even where it lies lower than the pond.
    /// </summary>
    bool Dug(int x, int y, int z)
    {
        if (Site.HTop == null) return true;
        int i = z * World.SX + x;
        return y < Site.HTop[i] || Site.WaterTop != null && y < Site.WaterTop[i];
    }

    /// <summary>Lets water run into the hollows dug beside it, a few thousand voxels a step, up to the level it came from.</summary>
    void FlowWater(int budget = 3000)
    {
        while (_flood.Count > 0 && budget-- > 0)
        {
            var (x, y, z, level) = _flood.Dequeue();
            if (y >= level || y < 1 || World.Get(x, y, z) != Mat.Air || !Dug(x, y, z)) continue;
            World.Set(x, y, z, Mat.Water);
            Flooded++;
            foreach (var (dx, dy, dz) in Seep)
            {
                int nx = x + dx, ny = y + dy, nz = z + dz;
                if (ny < level && World.InBounds(nx, ny, nz) && World.Get(nx, ny, nz) == Mat.Air && Dug(nx, ny, nz)) _flood.Enqueue((nx, ny, nz, level));
            }
        }
    }

    /// <summary>Water still on its way somewhere.</summary>
    public int FloodPending => _flood.Count;
}
