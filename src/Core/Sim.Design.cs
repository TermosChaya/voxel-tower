using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// What the buildings were meant to be. Every voxel the workers lay is remembered here, material and all, and forgotten
/// only when the player has it taken down; what the horde hacks out and what the years broke stays remembered. So a
/// broken wall is mended to what it was, stone for stone: what is missing is laid again, and rubble lying where the
/// masonry should stand is cleared out of the way first. The valley's ruins and the fort a game begins with come with
/// what they were meant to be, too.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The material each voxel of a building is meant to hold, or nothing; a layer as big as the world.</summary>
    public readonly VoxelWorld Design;
    /// <summary>Voxels being cleared of rubble, to be laid as they were meant once they are free.</summary>
    readonly HashSet<int> _relay = new();
    readonly VoxelList _one = new();
    /// <summary>Goes up whenever what is meant changes (not when a voxel is laid again as it was meant).</summary>
    public int DesignRev { get; private set; }

    /// <summary>The meant material at a voxel, or nothing (outside the world too).</summary>
    public byte MeantAt(int x, int y, int z) => World.InBounds(x, y, z) ? Design.Get(x, y, z) : (byte)0;

    /// <summary>Remembers a voxel of a building. Gate leaves and a drawbridge's planks are their own business: they turn.</summary>
    public void Remember(int x, int y, int z, byte m)
    {
        // what turns on hinges and what runs off as a liquid are not building
        if (Mat.Swings(m) || m == Mat.SiegeLadder || m == Mat.Pitch || !World.InBounds(x, y, z)) return;
        // laid, or laid again: worth dressing once it is done
        DecorDirty(x, y, z);
        if (Design.Get(x, y, z) == m) return;
        Design.SetRaw(x, y, z, m);
        DesignRev++;
    }

    /// <summary>
    /// Forgets a voxel the player had taken down, and what was meant to stand on it and is already gone: a wall taken
    /// down from the top leaves nothing of itself remembered above.
    /// </summary>
    void Forget(int x, int y, int z)
    {
        if (MeantAt(x, y, z) == 0) return;
        Design.SetRaw(x, y, z, 0);
        DesignRev++;
        for (int yy = y + 1; yy < World.SY && Design.Get(x, yy, z) != 0 && Brushes.Free(World.Get(x, yy, z)); yy++) Design.SetRaw(x, yy, z, 0);
    }

    /// <summary>Something stands where a voxel was meant that will do: the meant material, any stone for stone.</summary>
    public static bool Stands(byte cur, byte meant) =>
        cur == meant || Mat.IsMasonry(meant) && (Mat.IsMasonry(cur) || cur == Mat.Ruin) || meant == Mat.Ruin && Mat.IsMasonry(cur);

    /// <summary>A meant voxel at this one or right beside it (a click on the ground at the foot of a breach), packed, or −1.</summary>
    public int MeantNear(int x, int y, int z)
    {
        for (int r = 0; r <= 2; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) == r && MeantAt(x + dx, y + dy, z + dz) != 0)
                            return WaterPlanner.Pack(x + dx, y + dy, z + dz);
        return -1;
    }

    /// <summary>
    /// The building a meant voxel belongs to: every meant voxel joined to it face to face, packed, up to a limit
    /// (<paramref name="seen"/>, if given, is filled with the same).
    /// </summary>
    public List<int> BuildingFrom(int seed, HashSet<int> seen = null, int limit = 400_000)
    {
        var list = new List<int>();
        if (seed < 0) return list;
        seen ??= new HashSet<int>();
        seen.Clear();
        seen.Add(seed);
        var queue = new Queue<int>();
        queue.Enqueue(seed);
        while (queue.Count > 0 && list.Count < limit)
        {
            int p = queue.Dequeue();
            list.Add(p);
            var (px, py, pz) = WaterPlanner.Unpack(p);
            foreach (var (dx, dy, dz) in Around)
            {
                int nx = px + dx, ny = py + dy, nz = pz + dz;
                if (MeantAt(nx, ny, nz) == 0) continue;
                int q = WaterPlanner.Pack(nx, ny, nz);
                if (seen.Add(q)) queue.Enqueue(q);
            }
        }
        return list;
    }

    /// <summary>The broken gates standing in a rectangle of columns (a building's, or one framed): mending a building hangs them again.</summary>
    public List<Gateway> BrokenGatesIn(int x0, int z0, int x1, int z1)
    {
        var list = new List<Gateway>();
        foreach (var g in Gates)
        {
            var (mx, _, mz) = g.Middle;
            if (g.Broken && mx >= Math.Min(x0, x1) && mx <= Math.Max(x0, x1) && mz >= Math.Min(z0, z1) && mz <= Math.Max(z0, z1)) list.Add(g);
        }
        return list;
    }

    /// <summary>The leaves a broken gate would get, for a preview.</summary>
    public void GateLeaves(Gateway g, VoxelList into)
    {
        for (int i = 0; i < Gateway.Voxels; i++)
        {
            var (x, y, z) = g.Place(i, 0);
            byte m = World.Get(x, y, z);
            if (m == Mat.Air || Mat.IsLeaf(m)) into.Add(x, y, z, Mat.Gate);
        }
    }

    /// <summary>Every meant voxel on the columns of a rectangle, packed.</summary>
    public List<int> MeantIn(int x0, int z0, int x1, int z1)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        x0 = Math.Max(0, x0); z0 = Math.Max(0, z0); x1 = Math.Min(World.SX - 1, x1); z1 = Math.Min(World.SZ - 1, z1);
        var list = new List<int>();
        for (int cz = z0 >> VoxelWorld.Shift; cz <= z1 >> VoxelWorld.Shift; cz++)
            for (int cx = x0 >> VoxelWorld.Shift; cx <= x1 >> VoxelWorld.Shift; cx++)
                for (int cy = 0; cy < World.CY; cy++)
                {
                    var d = Design.ChunkData(Design.ChunkIndex(cx, cy, cz));
                    if (d == null) continue;
                    for (int i = 0; i < d.Length; i++)
                    {
                        if (d[i] == 0) continue;
                        int x = cx * VoxelWorld.CS + (i & 31), z = cz * VoxelWorld.CS + (i >> 5 & 31), y = cy * VoxelWorld.CS + (i >> 10);
                        if (x >= x0 && x <= x1 && z >= z0 && z <= z1) list.Add(WaterPlanner.Pack(x, y, z));
                    }
                }
        return list;
    }

    /// <summary>
    /// What mending some meant voxels takes: what to lay where they are missing, and what to clear first where something
    /// else lies in the way (rubble, a siege fill, moss on a broken course). What is already marked is left out.
    /// </summary>
    /// <summary>
    /// What a building wants to be whole again: the voxels to lay, and the rubble to clear first. What is marked for it
    /// already is left out, and counted: the result is how much of the mending waits for the workers.
    /// </summary>
    public int MendPlan(List<int> voxels, VoxelList lay, VoxelList clear)
    {
        int waiting = 0;
        foreach (int p in voxels)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            byte want = Design.Get(x, y, z), cur = World.Get(x, y, z);
            if (want == 0 || Site.Forbidden(x, z)) continue;
            byte o = Orders.At(x, y, z);
            // standing, but cracked: taken out and laid anew
            if (Stands(cur, want))
            {
                if (Cracks.GetValueOrDefault(p) >= .1f && Labour.Diggable(cur)) { if (!Orders.IsRemove(o)) clear.Add(x, y, z, Mat.Air); else waiting++; }
                continue;
            }
            if (Brushes.Free(cur)) { if (o != want) lay.Add(x, y, z, want); else waiting++; }
            else if (Labour.Diggable(cur) && !Mat.IsLeaf(cur)) { if (!Orders.IsRemove(o)) clear.Add(x, y, z, Mat.Air); else waiting++; }
        }
        return waiting;
    }

    /// <summary>Marks a mending for the workers; returns the voxels marked.</summary>
    public int Mend(VoxelList lay, VoxelList clear)
    {
        Mended++;
        for (int i = 0; i < clear.Count; i++)
        {
            int p = WaterPlanner.Pack(clear.X(i), clear.Y(i), clear.Z(i));
            if (_relay.Add(p)) Orders.Writing?.Relaid.Add(p);
        }
        int n = DesignateRemove(clear);
        return n + DesignateBuild(lay);
    }

    /// <summary>A voxel was dug out by our people: forget it if that was the building itself, lay it anew if it was rubble in its way.</summary>
    void Dug(int x, int y, int z, byte was)
    {
        byte meant = MeantAt(x, y, z);
        if (meant == 0) return;
        if (_relay.Remove(WaterPlanner.Pack(x, y, z)))
        {
            _one.Clear();
            _one.Add(x, y, z, meant);
            Orders.PlanBuild(_one);
            return;
        }
        if (Stands(was, meant)) Forget(x, y, z);
    }

    /// <summary>Of the meant voxels on the columns of a rectangle: how many there are, and how many stand.</summary>
    public (int meant, int standing) Soundness(int x0, int z0, int x1, int z1)
    {
        int meant = 0, standing = 0;
        foreach (int p in MeantIn(x0, z0, x1, z1))
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            meant++;
            if (!Brushes.Free(World.Get(x, y, z))) standing++;
        }
        return (meant, standing);
    }
}
