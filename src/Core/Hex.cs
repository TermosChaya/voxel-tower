using System;

namespace VoxelTower.Core;

/// <summary>
/// The valley's land as a hexagon, the shape of a cell of the map the valleys will one day lie on: pointed to the north
/// and the south, its west and east sides straight and upright (z running north to south). So the cells of that map sit
/// in rows from west to east, and a neighbour to the west or the east shares a whole side with the valley: the horde
/// that comes from the west comes in over the straight western side, as it always has. The hexagon is the largest that
/// fits the world's box; outside it there is nothing, down to under the bedrock, and its six cut sides show the ground
/// in section as the box's four did.
/// </summary>
public sealed class Hex
{
    /// <summary>The middle, and the distance from it to a point (north and south) — voxels.</summary>
    public readonly float CX, CZ, R;
    /// <summary>Half the width across the straight sides: R·√3/2.</summary>
    public readonly float HalfW;

    const float Root3 = 1.7320508f;

    public Hex(float cx, float cz, float r)
    {
        CX = cx; CZ = cz; R = r;
        HalfW = r * Root3 / 2;
    }

    /// <summary>The largest such hexagon in a box of these sides, a voxel or two in from its edges.</summary>
    public static Hex In(int sx, int sz) => new(sx / 2f, sz / 2f, Math.Min(sz / 2f, sx / Root3) - 2);

    /// <summary>
    /// How far a point lies in from the nearest side (voxels): positive inside, negative outside (then how far out past
    /// the nearest side's line, which is the distance itself off a side, a little short of it off a corner).
    /// </summary>
    public float Edge(float x, float z)
    {
        float dx = Math.Abs(x - CX), dz = Math.Abs(z - CZ);
        return Math.Min(HalfW - dx, (R - dz - dx / Root3) * (Root3 / 2));
    }

    /// <summary>Whether the middle of a column is on the land.</summary>
    public bool Inside(int x, int z) => Edge(x + .5f, z + .5f) >= 0;

    /// <summary>How far a point is from the land, 0 on it.</summary>
    public float Distance(float x, float z)
    {
        if (Edge(x, z) >= 0) return 0;
        var (nx, nz) = Nearest(x, z);
        return MathF.Sqrt((x - nx) * (x - nx) + (z - nz) * (z - nz));
    }

    /// <summary>The nearest point of the land to a point (the point itself when it is on it).</summary>
    public (float x, float z) Nearest(float x, float z)
    {
        if (Edge(x, z) >= 0) return (x, z);
        float bx = x, bz = z, best = float.MaxValue;
        var c = Corners();
        for (int i = 0; i < 6; i++)
        {
            var (ax, az) = c[i]; var (cx, cz) = c[(i + 1) % 6];
            float ex = cx - ax, ez = cz - az, t = Math.Clamp(((x - ax) * ex + (z - az) * ez) / (ex * ex + ez * ez), 0, 1);
            float px = ax + ex * t, pz = az + ez * t, d = (x - px) * (x - px) + (z - pz) * (z - pz);
            if (d < best) { best = d; bx = px; bz = pz; }
        }
        return (bx, bz);
    }

    /// <summary>The six corners, from the northern point round by the east.</summary>
    public (float x, float z)[] Corners() => new[]
    {
        (CX, CZ - R), (CX + HalfW, CZ - R / 2), (CX + HalfW, CZ + R / 2),
        (CX, CZ + R), (CX - HalfW, CZ + R / 2), (CX - HalfW, CZ - R / 2),
    };

    /// <summary>Where the land begins and ends along a row (x), or (0, -1) for a row north or south of it.</summary>
    public (float x0, float x1) Row(float z)
    {
        float dz = Math.Abs(z - CZ);
        if (dz > R) return (0, -1);
        float half = dz <= R / 2 ? HalfW : Root3 * (R - dz);
        return (CX - half, CX + half);
    }

    /// <summary>The straight western side: its x, and the rows it runs between.</summary>
    public float WestX => CX - HalfW;
    public float WestZ0 => CZ - R / 2;
    public float WestZ1 => CZ + R / 2;
}
