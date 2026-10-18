using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A standing order to get one resource out of an area: trees to fell, rock to break, or earth to dig down to a depth.
/// Nothing is marked up front; whenever a pawn has nothing else to do, a little more of the area is marked near it.
/// Or an area set aside as a shelter: nothing is taken out of it, and when the alarm is up those who don't fight wait in it.
/// </summary>
public sealed class Zone
{
    public int Id, X0, Z0, X1, Z1, Kind, Depth;
    /// <summary>Per column (index (z − Z0)·W + (x − X0)): how far down stone and earth are taken, from where the surface was.</summary>
    public int[] Floor;
    /// <summary>Voxels marked out of it so far.</summary>
    public int Marked;
    /// <summary>Set by a pawn told to bring in a resource, not by the player.</summary>
    public bool Auto;

    /// <summary>The kind of an area that is a shelter, past the kinds of the stores.</summary>
    public const int Shelter = 9;

    public int W => X1 - X0 + 1;
    public int D => Z1 - Z0 + 1;
    public bool Contains(int x, int z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;
    public int FloorAt(int x, int z) => Floor == null ? 0 : Floor[(z - Z0) * W + (x - X0)];

    public string Name => Name0(Kind);
    public static string Name0(int kind) => kind switch { Stockpile.Wood => "Делянка", Stockpile.Stone => "Каменоломня", Shelter => "Убежище", _ => "Карьер" };
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
        if (kind is Stockpile.Stone or Stockpile.Earth)
        {
            zone.Floor = new int[zone.W * zone.D];
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++) zone.Floor[(z - z0) * zone.W + (x - x0)] = Math.Max(3, World.SurfAt(x, z) - zone.Depth);
        }
        Zones.Add(zone);
        Orders.Writing?.Added.Add(zone);
        ZonesVersion++;
        return zone;
    }

    /// <summary>Takes back every zone that overlaps a rectangle; returns how many.</summary>
    public int RemoveZones(int x0, int z0, int x1, int z1)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        int n = 0;
        for (int i = Zones.Count - 1; i >= 0; i--)
        {
            var z = Zones[i];
            if (z.X0 > x1 || z.X1 < x0 || z.Z0 > z1 || z.Z1 < z0) continue;
            Zones.RemoveAt(i);
            Orders.Writing?.Removed.Add(z);
            n++;
        }
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
            // a trunk's foot, with its roots, is one patch of columns: count the patches
            int trees = 0, w = x1 - x0 + 1;
            var seen = new bool[w * (z1 - z0 + 1)];
            var stack = new Stack<(int x, int z)>();
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (seen[(z - z0) * w + x - x0] || !TreeFoot(x, z, out _)) continue;
                    trees++;
                    seen[(z - z0) * w + x - x0] = true;
                    stack.Push((x, z));
                    while (stack.Count > 0)
                    {
                        var (cx, cz) = stack.Pop();
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = cx + dx, nz = cz + dz;
                                if (nx < x0 || nx > x1 || nz < z0 || nz > z1 || seen[(nz - z0) * w + nx - x0] || !TreeFoot(nx, nz, out _)) continue;
                                seen[(nz - z0) * w + nx - x0] = true;
                                stack.Push((nx, nz));
                            }
                    }
                }
            return (trees, trees * WoodPerTree);
        }
        double vox = 0;
        for (int z = z0; z <= z1; z += 2)
            for (int x = x0; x <= x1; x += 2)
            {
                int top = World.SurfAt(x, z);
                if (CutSpan(kind, x, z, top, Math.Max(3, top - depth), out int bottom)) vox += (top - bottom) * 4;
            }
        return (0, vox);
    }

    /// <summary>Voxels of timber a tree of the valley gives, on average, for the survey's estimate.</summary>
    public const double WoodPerTree = 4900;

    /// <summary>A trunk standing on the ground at this column: where a tree can be felled from.</summary>
    bool TreeFoot(int x, int z, out int y)
    {
        y = World.SurfAt(x, z);
        return Mat.IsTrunk(World.Get(x, y, z));
    }

    /// <summary>
    /// How far down a column of a stone or earth zone can be taken: from the surface (<paramref name="top"/>) down to
    /// <paramref name="bottom"/>, through what the zone takes and no deeper than its floor. False when there is nothing
    /// to take, or when water stands beside the column or on it: the crews leave the shore alone, and only the player's
    /// own pick lets water into a cut.
    /// </summary>
    bool CutSpan(int kind, int x, int z, int top, int floor, out int bottom)
    {
        int y = top - 1;
        while (y >= floor && Takes(kind, World.Get(x, y, z))) y--;
        bottom = y + 1;
        return bottom < top && !WaterNear(x, z, bottom);
    }

    static readonly (int dx, int dz)[] Side4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>Water on a column, or beside it anywhere above <paramref name="y0"/>, that a cut down to there would let in.</summary>
    bool WaterNear(int x, int z, int y0)
    {
        if (World.Get(x, World.SurfAt(x, z), z) == Mat.Water) return true;
        foreach (var (dx, dz) in Side4)
        {
            int nx = x + dx, nz = z + dz, y = World.SurfAt(nx, nz);
            if (World.Get(nx, y, nz) != Mat.Water) continue;
            while (World.Get(nx, y, nz) == Mat.Water) y++;
            if (y > y0) return true;
        }
        return false;
    }

    /// <summary>The voxels a zone of this kind takes: rock for stone, natural earth for earth.</summary>
    static bool Takes(int kind, byte m) => kind switch
    {
        Stockpile.Stone => m is Mat.Rock or Mat.Moss or Mat.Ore or Mat.Ruin,
        Stockpile.Earth => m is Mat.Soil or Mat.Path || Mat.IsGrass(m),
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
                if (z.Kind == Zone.Shelter || z.Auto && u.Duty != Stockpile.None && z != u.DutyZone) continue;
                // not the work it does
                if (!Work.Does(u, Work.For(z.Kind))) continue;
                float dx = Math.Max(0, Math.Max(z.X0 - u.X, u.X - z.X1)), dz = Math.Max(0, Math.Max(z.Z0 - u.Z, u.Z - z.Z1));
                float d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = z; }
            }
            if (best == null) return false;
            if (Feed(best, u, out bool spent)) return true;
            if (!spent) return false;
        }
        return false;
    }

    /// <summary>Marks a little more of one zone near the pawn; a zone with nothing left goes away (spent).</summary>
    bool Feed(Zone z, Unit u, out bool spent)
    {
        spent = false;
        if (Pending(z) >= ZonePending) return false;
        int n = MarkNext(z, u.X, u.Z, out bool exhausted);
        if (n > 0) { z.Marked += n; return true; }
        if (!exhausted) return false;
        spent = true;
        Zones.Remove(z);
        ZonesVersion++;
        if (!z.Auto)
            Say(z.Kind switch
            {
                Stockpile.Wood => "Делянка вырублена.",
                Stockpile.Stone => "Каменоломня выбрана до дна.",
                _ => "Карьер выкопан.",
            }, 1, (z.X0 + z.X1) / 2f, (z.Z0 + z.Z1) / 2f);
        return false;
    }

    /// <summary>
    /// A pawn told to bring in stone, timber or earth without an area set aside picks for itself: the nearest boulder or
    /// ruin for stone, the nearest tree for timber, and for earth a pit at a fair distance from the citadel and its
    /// walls. It sets a small area round it and works it till it is spent, then finds the next.
    /// </summary>
    bool DutyWork(Unit u)
    {
        if (u.Duty == Stockpile.None || !Work.Does(u, Work.For(u.Duty))) return false;
        for (int tries = 0; tries < 3; tries++)
        {
            if (u.DutyZone == null || !Zones.Contains(u.DutyZone)) u.DutyZone = DutyZone(u);
            if (u.DutyZone == null) { u.Why = $"рядом нечего добывать: {Stockpile.Name(u.Duty)}"; return false; }
            if (Feed(u.DutyZone, u, out bool spent)) return true;
            if (!spent) return false;
        }
        return false;
    }

    Zone DutyZone(Unit u)
    {
        int kind = u.Duty;
        if (kind == Stockpile.Earth)
        {
            // one pit, dug by all who are sent for earth
            foreach (var z in Zones) if (z.Auto && z.Kind == Stockpile.Earth) return z;
            var (px, pz) = PitSpot(u);
            if (px < 0) return null;
            var pit = AddZone(px - 12, pz - 12, px + 12, pz + 12, Stockpile.Earth, 16);
            pit.Auto = true;
            return pit;
        }
        int ux = (int)u.X, uz = (int)u.Z;
        for (int r = 0; r <= 480; r += 4)
            for (int dz = -r; dz <= r; dz += 4)
                for (int dx = -r; dx <= r; dx += 4)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    int x = ux + dx, z = uz + dz;
                    if (x < 8 || z < 8 || x >= World.SX - 8 || z >= World.SZ - 8 || Site.Forbidden(x, z)) continue;
                    int top = World.SurfAt(x, z);
                    bool hit = kind == Stockpile.Wood
                        ? TreeFoot(x, z, out _)
                        : World.Get(x, top - 1, z) is var m && (m == Mat.Ruin || (m is Mat.Rock or Mat.Moss) && top > Site.HTop[z * World.SX + x] + 2);
                    if (!hit || (kind == Stockpile.Stone && WaterNear(x, z, top - 4))) continue;
                    int a = kind == Stockpile.Wood ? 16 : 12;
                    var zone = AddZone(x - a, z - a, x + a, z + a, kind, 24);
                    zone.Auto = true;
                    return zone;
                }
        return null;
    }

    /// <summary>Where the pit for earth goes: flat dry grass at a fair distance from the citadel, clear of walls, water and other areas.</summary>
    (int x, int z) PitSpot(Unit u)
    {
        int best = -1, bx = -1, bz = -1;
        for (int ring = 150; ring <= 230; ring += 40)
            for (int a = 0; a < 360; a += 15)
            {
                int x = Site.KeepX + (int)(Math.Cos(a * Math.PI / 180) * ring), z = Site.KeepZ + (int)(Math.Sin(a * Math.PI / 180) * ring);
                if (x < 24 || z < 24 || x >= World.SX - 24 || z >= World.SZ - 24 || ZoneAt(x, z) != null) continue;
                int lo = int.MaxValue, hi = int.MinValue, grass = 0;
                bool bad = false;
                for (int dz = -12; dz <= 12 && !bad; dz += 4)
                    for (int dx = -12; dx <= 12 && !bad; dx += 4)
                    {
                        int cx = x + dx, cz = z + dz, top = World.SurfAt(cx, cz);
                        byte m = World.Get(cx, top - 1, cz);
                        if (Site.Forbidden(cx, cz) || Mat.IsPlayer[m] || m == Mat.Ruin || WaterNear(cx, cz, top - 16)) bad = true;
                        if (Mat.IsGrass(m) || m == Mat.Soil) grass++;
                        lo = Math.Min(lo, top); hi = Math.Max(hi, top);
                    }
                if (bad || hi - lo > 6) continue;
                float du = MathF.Sqrt((x - u.X) * (x - u.X) + (z - u.Z) * (z - u.Z));
                int score = grass * 40 - (hi - lo) * 30 - (int)(du / 8);
                if (score > best) { best = score; bx = x; bz = z; }
            }
        return (bx, bz);
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
                    else if (CutSpan(z.Kind, x, zz, World.SurfAt(x, zz), z.FloorAt(x, zz), out _)) plan = Patch(z, x, zz);
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

    /// <summary>
    /// A patch of rock or earth around a column, from the surface down to the zone's floor, and the plants over it;
    /// a tree standing in it comes down whole.
    /// </summary>
    VoxelList Patch(Zone z, int x0, int z0)
    {
        var L = new VoxelList();
        const int r = 6;
        for (int zz = z0 - r; zz <= z0 + r; zz++)
            for (int x = x0 - r; x <= x0 + r; x++)
            {
                if (!z.Contains(x, zz) || (x - x0) * (x - x0) + (zz - z0) * (zz - z0) > r * r) continue;
                int top = World.SurfAt(x, zz);
                if (!CutSpan(z.Kind, x, zz, top, z.FloorAt(x, zz), out int bottom)) continue;
                for (int y = top - 1; y >= bottom; y--) L.Add(x, y, zz, Mat.Air);
                Brushes.ClearVegAbove(World, L, x, zz, top);
            }
        return Shapes.WithTrees(World, L);
    }
}
