using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The magic wand: the voxels joined to one by their faces (or by their edges and corners too) that are of the same
/// material as it. «The same» is as the player would call it — any shade of laid stone and the mortar between, any
/// trunk, the turf and the earth of the valley, rock and the moss on it, a gate shut or open — or, asked for, exactly
/// the same voxel. Taken so far and no further (<see cref="Cap"/>): the whole valley's ground is no one thing.
/// </summary>
public static class Wand
{
    /// <summary>How many voxels the wand takes at most: a stretch of wall 30 m long, a metre thick and 4 high.</summary>
    public const int Cap = 80_000;

    /// <summary>What a material counts as for the wand when it is not to be told exactly.</summary>
    public static byte Kind(byte m)
    {
        if (Mat.IsMasonry(m)) return Mat.Stone;
        if (Mat.IsTrunk(m)) return Mat.Trunk;
        if (Mat.IsLeafy(m)) return Mat.Leaf;
        if (Mat.IsGrass(m)) return Mat.Soil;
        return m switch
        {
            Mat.Soil or Mat.Path or Mat.Tuft or Mat.Flower or Mat.Bloom => Mat.Soil,
            Mat.Moss => Mat.Rock,
            Mat.GateShut or Mat.GateOpen => Mat.Gate,
            Mat.IronGateShut or Mat.IronGateOpen => Mat.IronGate,
            _ => m,
        };
    }

    public static bool Same(byte a, byte b, bool exact) => exact ? a == b : Kind(a) == Kind(b);

    // the six faces, and the twenty sides, edges and corners about a voxel
    static readonly (int x, int y, int z)[] Faces = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
    static readonly (int x, int y, int z)[] Around = MakeAround();

    static (int, int, int)[] MakeAround()
    {
        var a = new List<(int, int, int)>();
        for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++) if (x != 0 || y != 0 || z != 0) a.Add((x, y, z));
        return a.ToArray();
    }

    /// <summary>
    /// The region of the voxel at (x, y, z) into a list (with what each is) and a set of its packed places (to know a
    /// voxel of it again). False when it is more than <paramref name="cap"/> and was cut off there; empty for air.
    /// </summary>
    public static bool Fill(VoxelWorld w, int x, int y, int z, bool exact, bool corners, VoxelList into, HashSet<int> seen, int cap = Cap)
    {
        into.Clear(); seen.Clear();
        if (!w.InBounds(x, y, z)) return true;
        byte seed = w.Get(x, y, z);
        if (seed == Mat.Air) return true;
        var steps = corners ? Around : Faces;
        var todo = new Stack<int>();
        int p0 = WaterPlanner.Pack(x, y, z);
        seen.Add(p0); todo.Push(p0); into.Add(x, y, z, seed);
        while (todo.Count > 0)
        {
            var (cx, cy, cz) = WaterPlanner.Unpack(todo.Pop());
            foreach (var (dx, dy, dz) in steps)
            {
                int nx = cx + dx, ny = cy + dy, nz = cz + dz;
                if (!w.InBounds(nx, ny, nz)) continue;
                byte m = w.Get(nx, ny, nz);
                if (m == Mat.Air || !Same(m, seed, exact)) continue;
                int p = WaterPlanner.Pack(nx, ny, nz);
                if (!seen.Add(p)) continue;
                if (into.Count >= cap) return false;
                into.Add(nx, ny, nz, m);
                todo.Push(p);
            }
        }
        return true;
    }
}
