using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A standing order to get one resource out of an area: trees to fell, rock to break, or earth to dig down to a depth.
/// Nothing is marked up front; whenever a pawn has nothing else to do, a little more of the area is marked near it.
/// </summary>
public sealed class Zone
{
    public int Id, X0, Z0, X1, Z1, Kind, Depth;
    /// <summary>Per column (index (z − Z0)·W + (x − X0)): how far down stone and earth are taken, from where the surface was.</summary>
    public int[] Floor;
    /// <summary>Voxels marked out of it so far.</summary>
    public int Marked;

    public int W => X1 - X0 + 1;
    public int D => Z1 - Z0 + 1;
    public bool Contains(int x, int z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;
    public int FloorAt(int x, int z) => Floor == null ? 0 : Floor[(z - Z0) * W + (x - X0)];

    public string Name => Name0(Kind);
    public static string Name0(int kind) => kind switch { Stockpile.Wood => "Делянка", Stockpile.Stone => "Каменоломня", _ => "Карьер" };
}

public sealed partial class Sim
{
    public readonly List<Zone> Zones = new();
    int _nextZone = 1;
    /// <summary>Bumped whenever a zone is added, used up or taken back, for whoever draws them.</summary>
    public int ZonesVersion { get; private set; }

    /// <summary>How many voxels of a zone may stand marked at once: enough to keep a crew busy, not the whole forest.</summary>
    const int ZonePending = 6000;

    /// <summary>Sets an area aside for one resource. Stone and earth are taken down to <paramref name="depth"/> below today's surface.</summary>
    public Zone AddZone(int x0, int z0, int x1, int z1, int kind, int depth)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        x0 = Math.Max(0, x0); z0 = Math.Max(0, z0); x1 = Math.Min(World.SX - 1, x1); z1 = Math.Min(World.SZ - 1, z1);
        var zone = new Zone { Id = _nextZone++, X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Kind = kind, Depth = Math.Max(1, depth) };
        if (kind != Stockpile.Wood)
        {
            zone.Floor = new int[zone.W * zone.D];
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++) zone.Floor[(z - z0) * zone.W + (x - x0)] = Math.Max(3, World.SurfAt(x, z) - zone.Depth);
        }
        Zones.Add(zone);
        ZonesVersion++;
        return zone;
    }

    /// <summary>Takes back every zone that overlaps a rectangle; returns how many.</summary>
    public int RemoveZones(int x0, int z0, int x1, int z1)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        int n = Zones.RemoveAll(z => z.X0 <= x1 && z.X1 >= x0 && z.Z0 <= z1 && z.Z1 >= z0);
        if (n > 0) ZonesVersion++;
        return n;
    }

    /// <summary>The zone a column lies in, or null.</summary>
    public Zone ZoneAt(int x, int z)
    {
        for (int i = Zones.Count - 1; i >= 0; i--) if (Zones[i].Contains(x, z)) return Zones[i];
        return null;
    }

    /// <summary>
    /// What an area holds of a resource, sampled: trees standing in it, or voxels of rock or earth between the surface
    /// and <paramref name="depth"/> below it. For the tool's tag before the zone is set.
    /// </summary>
    public (int trees, double voxels) Survey(int x0, int z0, int x1, int z1, int kind, int depth)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        x0 = Math.Max(0, x0); z0 = Math.Max(0, z0); x1 = Math.Min(World.SX - 1, x1); z1 = Math.Min(World.SZ - 1, z1);
        if (kind == Stockpile.Wood)
        {
            int trees = 0;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (TreeFoot(x, z, out int y) && World.Get(x - 1, y, z) != Mat.Trunk && World.Get(x, y, z - 1) != Mat.Trunk) trees++;
            return (trees, trees * 4900.0);
        }
        double vox = 0;
        for (int z = z0; z <= z1; z += 2)
            for (int x = x0; x <= x1; x += 2)
            {
                int top = World.SurfAt(x, z), floor = Math.Max(3, top - depth);
                for (int y = top - 1; y >= floor && Takes(kind, World.Get(x, y, z)); y--) vox += 4;
            }
        return (0, vox);
    }

    /// <summary>A trunk standing on the ground at this column: where a tree can be felled from.</summary>
    bool TreeFoot(int x, int z, out int y)
    {
        y = World.SurfAt(x, z);
        return World.Get(x, y, z) == Mat.Trunk;
    }

    /// <summary>The voxels a zone of this kind takes: rock for stone, natural earth for earth.</summary>
    static bool Takes(int kind, byte m) => kind switch
    {
        Stockpile.Stone => m is Mat.Rock or Mat.Moss,
        Stockpile.Earth => m is Mat.Soil or Mat.Grass or Mat.Path,
        _ => false,
    };

    /// <summary>
    /// Marks the next bit of the nearest zone with work left, close to the pawn: one whole tree, or a patch of rock or
    /// earth a metre and a half across. False when there is nothing to mark (and zones that are used up go away).
    /// </summary>
    bool FeedFromZone(Unit u)
    {
        while (Zones.Count > 0)
        {
            Zone best = null; float bd = float.MaxValue;
            foreach (var z in Zones)
            {
                float dx = Math.Max(0, Math.Max(z.X0 - u.X, u.X - z.X1)), dz = Math.Max(0, Math.Max(z.Z0 - u.Z, u.Z - z.Z1));
                float d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = z; }
            }
            if (Pending(best) >= ZonePending) return false;
            int n = MarkNext(best, u.X, u.Z, out bool exhausted);
            if (n > 0) { best.Marked += n; return true; }
            if (!exhausted) return false;
            Zones.Remove(best);
            ZonesVersion++;
            Say(best.Kind switch
            {
                Stockpile.Wood => "Делянка вырублена.",
                Stockpile.Stone => "Каменоломня выбрана до дна.",
                _ => "Карьер выкопан.",
            }, 1);
        }
        return false;
    }

    /// <summary>Voxels already marked inside a zone and not yet done.</summary>
    int Pending(Zone z)
    {
        int n = 0;
        foreach (int col in Orders.Active)
        {
            int cx = (col % Nav.NX << NavGrid.Shift) + 4, cz = (col / Nav.NX << NavGrid.Shift) + 4;
            if (z.Contains(cx, cz)) n += Orders.ColCount[col];
        }
        return n;
    }

    /// <summary>
    /// Marks the target nearest to a point inside a zone; returns how many voxels it marked. A target that is already
    /// marked is passed over for the next nearest. <paramref name="exhausted"/> says the zone holds nothing more at all.
    /// </summary>
    int MarkNext(Zone z, float fx, float fz, out bool exhausted)
    {
        exhausted = false;
        int cx = Math.Clamp((int)fx, z.X0, z.X1), cz = Math.Clamp((int)fz, z.Z0, z.Z1);
        int reach = Math.Max(z.W, z.D), found = 0;
        for (int r = 0; r <= reach; r += 2)
            for (int dz = -r; dz <= r; dz += 2)
                for (int dx = -r; dx <= r; dx += 2)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    int x = cx + dx, zz = cz + dz;
                    if (!z.Contains(x, zz)) continue;
                    VoxelList plan = null;
                    if (z.Kind == Stockpile.Wood)
                    {
                        if (TreeFoot(x, zz, out int y)) plan = Shapes.Tree(World, x, y, zz);
                    }
                    else
                    {
                        int top = World.SurfAt(x, zz);
                        if (top - 1 >= z.FloorAt(x, zz) && Takes(z.Kind, World.Get(x, top - 1, zz))) plan = Patch(z, x, zz);
                    }
                    if (plan == null) continue;
                    found++;
                    int n = Orders.PlanRemove(plan);
                    if (n > 0) return n;
                    // already marked (a pawn is on it): look a little further, but don't scan the whole area for it
                    if (found > 40) return 0;
                }
        exhausted = found == 0;
        return 0;
    }

    /// <summary>A patch of rock or earth around a column, from the surface down to the zone's floor, and the plants over it.</summary>
    VoxelList Patch(Zone z, int x0, int z0)
    {
        var L = new VoxelList();
        const int r = 6;
        for (int zz = z0 - r; zz <= z0 + r; zz++)
            for (int x = x0 - r; x <= x0 + r; x++)
            {
                if (!z.Contains(x, zz) || (x - x0) * (x - x0) + (zz - z0) * (zz - z0) > r * r) continue;
                int top = World.SurfAt(x, zz), floor = z.FloorAt(x, zz);
                int y = top - 1;
                for (; y >= floor && Takes(z.Kind, World.Get(x, y, zz)); y--) L.Add(x, y, zz, Mat.Air);
                if (y < top - 1) Brushes.ClearVegAbove(World, L, x, zz, top);
            }
        return L;
    }
}
