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
    /// <summary>The strip along the western edge where the horde walks in; nothing may be built there (the x it ends at).</summary>
    public int SpawnStrip = 16;
    /// <summary>The rows of the western edge the horde comes in by: on a hexagon its straight side, short of the corners.</summary>
    public int WestZ0 = 40, WestZ1 = 984;
    /// <summary>The land's shape, when it is a hexagon (null for the whole box).</summary>
    public Hex Hex;
    /// <summary>What kind of valley this is, and whether the first valley keeps every place it always had.</summary>
    public ValleyKind Kind;
    public bool Classic = true;

    /// <summary>A row of the western edge to come in by, as near a wanted one as the hexagon's straight side allows.</summary>
    public int EntryZ(int z) => Hex == null ? z : Math.Clamp(z, WestZ0, WestZ1);
    /// <summary>Where along the western edge to look for a place to come in (or go out) by: the middle of the strip.</summary>
    public int EntryX => SpawnStrip / 2;

    /// <summary>The box the valley is made in, across (voxels).</summary>
    public int SX, SZ;
    /// <summary>
    /// The side of the hexagon the horde comes in by and goes out by (<see cref="Hex.West"/> and the others): the west as
    /// it always was, but any side may be the one — on the map of valleys, the side the enemy's army stands at.
    /// </summary>
    public int EntrySide = Hex.West;
    /// <summary>How deep the horde's strip along its side is (voxels): nothing is built there.</summary>
    public const int StripDepth = 24;

    /// <summary>Whether a spot is in the horde's strip along the side it comes in by (on the box, the western strip).</summary>
    public bool InStrip(float x, float z)
    {
        if (Hex == null) return x < SpawnStrip;
        var (d, t) = Hex.FromSide(EntrySide, x, z);
        return d < StripDepth && t > -.02f && t < 1.02f;
    }

    /// <summary>
    /// A spot just in from the side the horde comes in by, a part t (0…1) of the way along it, short of its corners; on
    /// the box, a row of the western edge.
    /// </summary>
    public (int x, int z) Entry(double t) => Hex == null ? (EntryX, Math.Clamp((int)(t * SZ), WestZ0, WestZ1)) : EntryOn(EntrySide, t);

    /// <summary>A spot just in from a side of the hexagon (any of the six), a part t of the way along it, short of its corners.</summary>
    public (int x, int z) EntryOn(int side, double t)
    {
        var (x, z) = Hex.SidePoint(side, (float)(.08 + .84 * Math.Clamp(t, 0, 1)), StripDepth / 2f);
        return ((int)x, (int)z);
    }

    /// <summary>The spot of the horde's side nearest a point: where one there goes out of the valley, or comes in.</summary>
    public (int x, int z) EntryNear(float x, float z)
    {
        if (Hex == null) return (EntryX, EntryZ((int)z));
        var (_, t) = Hex.FromSide(EntrySide, x, z);
        return Entry((t - .08) / .84);
    }

    /// <summary>Whether a spot is on the land and a margin in from its edge (on the box, from the box's sides).</summary>
    public bool Inland(float x, float z, float margin)
    {
        if (Hex != null) return Hex.Edge(x, z) >= margin;
        return x >= margin && z >= margin && x < SX - margin && z < SZ - margin;
    }

    /// <summary>A spot pulled in onto the land, a margin in from its edge, if it is not that far in already.</summary>
    public (float x, float z) PullIn(float x, float z, float margin)
    {
        if (Inland(x, z, margin)) return (x, z);
        if (Hex == null) return (Math.Clamp(x, margin, SX - 1 - margin), Math.Clamp(z, margin, SZ - 1 - margin));
        var (nx, nz) = Hex.Nearest(x, z);
        float dx = Hex.CX - nx, dz = Hex.CZ - nz, l = MathF.Max(1, MathF.Sqrt(dx * dx + dz * dz));
        // from the nearest point of the edge, in toward the middle till it is the margin in
        for (float k = 0; k < 4 * margin + 8; k += 2)
        {
            float px = nx + dx / l * k, pz = nz + dz / l * k;
            if (Hex.Edge(px, pz) >= margin) return (px, pz);
        }
        return (Hex.CX, Hex.CZ);
    }

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
    /// <summary>The lakes in the lowland's hollows (a hexagon made by itself): centre, radius, the height of their water.</summary>
    public readonly List<(int X, int Z, int R, int Level)> Lakes = new();

    /// <summary>
    /// The river's course as a line (voxels) with its half width at each point, from where it comes in to where it goes
    /// out, carried on some way out of the box at either end for the far land; null if there is none.
    /// </summary>
    public List<(float X, float Z, float Half)> River;
    /// <summary>The roads as lines (voxels), carried on out of the box where they leave the valley.</summary>
    public readonly List<List<(float X, float Z)>> Roads = new();

    /// <summary>
    /// The river at a spot: how far it is from the river's middle line (voxels), the river's half width there, and the way
    /// the river runs there (a unit vector, downstream). False when there is no river.
    /// </summary>
    public bool RiverAt(float x, float z, out float off, out float half, out float dx, out float dz)
    {
        off = 1e9f; half = 0; dx = 0; dz = 1;
        if (River == null || River.Count < 2) return false;
        _riverLine ??= new Segments(River.Count - 1, i => (River[i].X, River[i].Z, River[i + 1].X, River[i + 1].Z));
        int seg = _riverLine.Nearest(x, z, out off, out float t);
        var (ax, az, ah) = River[seg]; var (bx, bz, bh) = River[seg + 1];
        half = ah + (bh - ah) * t;
        float ex = bx - ax, ez = bz - az, l = MathF.Max(1e-3f, MathF.Sqrt(ex * ex + ez * ez));
        dx = ex / l; dz = ez / l;
        return true;
    }

    /// <summary>How far a spot is from the nearest road's middle line (voxels); very far when there is none.</summary>
    public float RoadDist(float x, float z)
    {
        if (_roadLines == null)
        {
            var segs = new List<(float, float, float, float)>();
            foreach (var road in Roads) for (int i = 0; i + 1 < road.Count; i++) segs.Add((road[i].X, road[i].Z, road[i + 1].X, road[i + 1].Z));
            if (segs.Count == 0) return 1e9f;
            _roadLines = new Segments(segs.Count, i => segs[i]);
        }
        _roadLines.Nearest(x, z, out float d, out _);
        return d;
    }
    Segments _riverLine, _roadLines;

    /// <summary>Mines worth holding and ruins of what stood here before: things to find, take and keep.</summary>
    public readonly List<Landmark> Landmarks = new();

    /// <summary>The outcrops of iron ore (MapGen.Ore.cs): their middle, the top of it, and how far they reach, voxels.</summary>
    public readonly List<(int X, int Z, int Y, int R)> Outcrops = new();

    /// <summary>What the ruins were meant to be, voxel by voxel, so they can be raised again; the game keeps adding to it.</summary>
    public VoxelWorld Design;
    /// <summary>Where the old bridges arch over the river: they stand as long as they touch the ground (<see cref="Sim.Arched"/>).</summary>
    public VoxelWorld Arched;

    /// <summary>Columns nobody may build on: off the land, the map's western edge (the horde's entry), the citadel and its gate approach.</summary>
    public bool Forbidden(int x, int z)
    {
        if (InStrip(x + .5f, z + .5f)) return true;
        if (Hex != null && Hex.Edge(x + .5f, z + .5f) < 0) return true;
        if (Math.Sqrt((double)(x - KeepX) * (x - KeepX) + (double)(z - KeepZ) * (z - KeepZ)) < KeepR + 3) return true;
        if (x >= KeepX - KeepR - 16 && x < KeepX - KeepR && Math.Abs(z - KeepZ) < 8) return true;
        return false;
    }

    /// <summary>The old road through the lowland, as a z for each x.</summary>
    public static double PathZ(double x) => 100 + 96 * Smooth01(x / 150) + Math.Sin(x * .045) * 7;

    public static double Smooth01(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
}

/// <summary>
/// The segments of lines across the ground (the river, the roads), taken a run of them at a time with a box round each
/// run: the nearest to a spot is looked for first in the run whose box is nearest, and the other runs are passed over at
/// a glance when their boxes lie further off than what is found already.
/// </summary>
sealed class Segments
{
    const int Run = 12;
    readonly (float ax, float az, float bx, float bz)[] _segs;
    readonly (float x0, float z0, float x1, float z1)[] _boxes;

    public Segments(int count, Func<int, (float ax, float az, float bx, float bz)> seg)
    {
        _segs = new (float, float, float, float)[count];
        _boxes = new (float, float, float, float)[(count + Run - 1) / Run];
        for (int g = 0; g < _boxes.Length; g++)
        {
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            for (int i = g * Run; i < Math.Min(count, (g + 1) * Run); i++)
            {
                var s = _segs[i] = seg(i);
                x0 = Math.Min(x0, Math.Min(s.ax, s.bx)); x1 = Math.Max(x1, Math.Max(s.ax, s.bx));
                z0 = Math.Min(z0, Math.Min(s.az, s.bz)); z1 = Math.Max(z1, Math.Max(s.az, s.bz));
            }
            _boxes[g] = (x0, z0, x1, z1);
        }
    }

    /// <summary>The segment nearest a spot, how far it is, and how far along the segment (0…1) its nearest point lies.</summary>
    public int Nearest(float x, float z, out float best, out float bestT)
    {
        best = float.MaxValue; bestT = 0;
        int found = 0, first = 0;
        Span<float> off = _boxes.Length <= 256 ? stackalloc float[_boxes.Length] : new float[_boxes.Length];
        for (int g = 0; g < _boxes.Length; g++)
        {
            var (x0, z0, x1, z1) = _boxes[g];
            float dx = MathF.Max(0, MathF.Max(x0 - x, x - x1)), dz = MathF.Max(0, MathF.Max(z0 - z, z - z1));
            off[g] = dx * dx + dz * dz;
            if (off[g] < off[first]) first = g;
        }
        Look(first, x, z, ref best, ref bestT, ref found);
        for (int g = 0; g < _boxes.Length; g++)
            if (g != first && off[g] < best * best) Look(g, x, z, ref best, ref bestT, ref found);
        return found;
    }

    void Look(int g, float x, float z, ref float best, ref float bestT, ref int found)
    {
        for (int i = g * Run; i < Math.Min(_segs.Length, (g + 1) * Run); i++)
        {
            var (ax, az, bx, bz) = _segs[i];
            float ex = bx - ax, ez = bz - az, ee = MathF.Max(1e-6f, ex * ex + ez * ez);
            float t = Math.Clamp(((x - ax) * ex + (z - az) * ez) / ee, 0, 1);
            float px = ax + ex * t - x, pz = az + ez * t - z, d = MathF.Sqrt(px * px + pz * pz);
            if (d < best) { best = d; bestT = t; found = i; }
        }
    }
}
