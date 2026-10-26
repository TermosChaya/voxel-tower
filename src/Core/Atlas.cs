using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What stands on the land of the map: the citadel, an outpost, the horde's lair, a treasure.</summary>
public enum AtlasThingKind : byte { Citadel, Outpost, Lair, Trove }

/// <summary>A thing standing on the map's land, in its cell, at a spot of the land (x, z).</summary>
public sealed class AtlasThing
{
    public AtlasThingKind Kind;
    public MapCell Cell;
    public int X, Z;
}

/// <summary>
/// The land of the map of valleys, walked by the heroes as in «Heroes»: the cells of the campaign laid side by side,
/// pointed north and south, each as wide as <see cref="R"/> voxels from its middle to a corner. A stand-in kept small
/// until the map's own land is made (the fork «atlasland»): a level field with each cell's ground — a crag in a
/// mountain cell, a pond in a marsh, its river and its roads (Campaign.LayWays) — enough to be walked round, along and
/// over at a ford. Cost: the hours a voxel of it takes against open ground (1 open, <see cref="MapPath.RoadShare"/> on a
/// road, the cell's hours over 8 elsewhere, a ford's two hours more across its width).
/// </summary>
public sealed class Atlas
{
    public VoxelWorld World;
    public Campaign Campaign;
    public readonly List<AtlasThing> Things = new();

    /// <summary>From a cell's middle to a corner, voxels; how far round the field reaches past the outer cells.</summary>
    public const int R = 48, Rim = 24;
    static readonly float R3 = MathF.Sqrt(3);
    float _ox, _oz;
    MapCell[] _cellOf;
    byte[] _ground;
    const byte Open = 0, Crag = 1, Pond = 2, River = 3, Road = 4, Ford = 5, Off = 255;

    public static Atlas Build(Campaign c)
    {
        int rings = 0;
        foreach (var cell in c.Cells) rings = Math.Max(rings, Campaign.Distance(0, 0, cell.Q, cell.R));
        int sx = (int)MathF.Ceiling(R3 * R * (2 * rings + 1)) + 2 * Rim, sz = (int)MathF.Ceiling(1.5f * R * 2 * rings + 2 * R) + 2 * Rim;
        // (a world's sides are whole chunks)
        sx = (sx + 31) / 32 * 32; sz = (sz + 31) / 32 * 32;
        var a = new Atlas { Campaign = c, World = new VoxelWorld(sx, 32, sz), _ox = sx / 2f, _oz = sz / 2f };
        a._cellOf = new MapCell[sx * sz];
        a._ground = new byte[sx * sz];
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                var cell = a.Nearest(x + .5f, z + .5f);
                a._cellOf[z * sx + x] = cell;
                a._ground[z * sx + x] = cell == null ? Off : Open;
            }
        foreach (var cell in c.Cells) a.Lay(cell);
        a.Things.Clear();
        foreach (var cell in c.Cells) a.Refresh(cell);
        return a;
    }

    public (float X, float Z) Center(MapCell c) => (_ox + R3 * R * (c.Q + c.R / 2f), _oz + 1.5f * R * c.R);

    public MapCell CellAt(float x, float z)
    {
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z);
        if (ix < 0 || iz < 0 || ix >= World.SX || iz >= World.SZ) return null;
        return _cellOf[iz * World.SX + ix];
    }

    public int GroundAt(int x, int z) => 4;

    public bool Walkable(int x, int z)
    {
        if (x < 0 || z < 0 || x >= World.SX || z >= World.SZ) return false;
        byte g = _ground[z * World.SX + x];
        return g is Open or Road or Ford;
    }

    public float Cost(int x, int z)
    {
        if (!Walkable(x, z)) return float.PositiveInfinity;
        int i = z * World.SX + x;
        return _ground[i] switch
        {
            Road => (float)MapPath.RoadShare,
            Ford => 3.5f,
            _ => (float)(MapPath.Hours(_cellOf[i]) / 8),
        };
    }

    /// <summary>What a cell shows changed (whose it is, its outpost, its treasure taken): the things on it laid anew.</summary>
    public void Refresh(MapCell c)
    {
        Things.RemoveAll(t => t.Cell == c);
        var (cx, cz) = Center(c);
        AtlasThingKind? kind = c.Holding switch
        {
            Holding.Citadel => AtlasThingKind.Citadel,
            Holding.HordeTown => c.Owner == Owner.Horde ? AtlasThingKind.Lair : AtlasThingKind.Outpost,
            _ => c.Outpost ? AtlasThingKind.Outpost : null,
        };
        if (kind is { } k) Things.Add(new AtlasThing { Kind = k, Cell = c, X = (int)cx, Z = (int)cz });
        if (c.Trove != TroveKind.None && !c.TroveTaken) Things.Add(new AtlasThing { Kind = AtlasThingKind.Trove, Cell = c, X = (int)(cx + R * .4f), Z = (int)(cz - R * .3f) });
    }

    MapCell Nearest(float x, float z)
    {
        float px = (x - _ox) / R, pz = (z - _oz) / R;
        float q = R3 / 3 * px - pz / 3, r = 2f / 3 * pz, y = -q - r;
        float rq = MathF.Round(q), rr = MathF.Round(r), ry = MathF.Round(y);
        float dq = MathF.Abs(rq - q), dr = MathF.Abs(rr - r), dy = MathF.Abs(ry - y);
        if (dq > dr && dq > dy) rq = -ry - rr; else if (dr > dy) rr = -rq - ry;
        return Campaign.At((int)rq, (int)rr);
    }

    /// <summary>A point a part t of the way along a side of a cell, from its first corner round by the east.</summary>
    (float x, float z) Along(MapCell c, int side, float t)
    {
        var (cx, cz) = Center(c);
        float a0 = MathF.PI / 180 * (60 * side - 90), a1 = MathF.PI / 180 * (60 * side - 30);
        float x0 = cx + MathF.Cos(a0) * R, z0 = cz + MathF.Sin(a0) * R, x1 = cx + MathF.Cos(a1) * R, z1 = cz + MathF.Sin(a1) * R;
        t = Math.Clamp(t, 0, 1);
        return (x0 + (x1 - x0) * t, z0 + (z1 - z0) * t);
    }

    /// <summary>A cell's ground: its crag or its pond out of the way of its roads, its river through it, its roads from its sides to its middle.</summary>
    void Lay(MapCell c)
    {
        var (cx, cz) = Center(c);
        // the side nothing comes in by, for a crag or a pond
        int free = -1;
        for (int k = 0; k < 6 && free < 0; k++)
        {
            int s = (int)((c.Seed >> 3) + k) % 6;
            if (c.Edges[s] is not { Road: >= 0 } && c.Edges[s] is not { River: >= 0 }) free = s;
        }
        if (free >= 0 && c.Kind is ValleyKind.Mountain or ValleyKind.Marsh)
        {
            float a = MathF.PI / 180 * (-60 + 60 * free);
            float bx = cx + MathF.Cos(a) * R * .48f, bz = cz + MathF.Sin(a) * R * .48f, rad = R * (c.Kind == ValleyKind.Mountain ? .34f : .26f);
            Disc(bx, bz, rad, c, c.Kind == ValleyKind.Mountain ? Crag : Pond);
        }
        // the river: in over one side, bent toward the middle, out over another
        int ra = -1, rb = -1;
        for (int s = 0; s < 6; s++) if (c.Edges[s] is { River: >= 0 }) { if (ra < 0) ra = s; else rb = s; }
        if (rb >= 0)
        {
            var (ax, az) = Along(c, ra, Campaign.RiverAt(c, ra));
            var (bx, bz) = Along(c, rb, Campaign.RiverAt(c, rb));
            float mx = cx + ((ax + bx) / 2 - cx) * .35f, mz = cz + ((az + bz) / 2 - cz) * .35f;
            for (int i = 0; i <= 80; i++)
            {
                float t = i / 80f, u = 1 - t;
                Disc(ax * u * u + mx * 2 * t * u + bx * t * t, az * u * u + mz * 2 * t * u + bz * t * t, 3f, c, River);
            }
        }
        // the roads, from each road side's middle to the cell's: over the river a ford
        for (int s = 0; s < 6; s++)
        {
            if (c.Edges[s] is not { Road: >= 0 }) continue;
            float a = MathF.PI / 180 * (-60 + 60 * s), mx = cx + MathF.Cos(a) * R * R3 / 2, mz = cz + MathF.Sin(a) * R * R3 / 2;
            for (int i = 0; i <= 60; i++)
            {
                float t = i / 60f;
                Disc(mx + (cx - mx) * t, mz + (cz - mz) * t, 2.5f, c, Road);
            }
        }
    }

    void Disc(float x, float z, float rad, MapCell c, byte what)
    {
        for (int iz = (int)(z - rad); iz <= (int)(z + rad) + 1; iz++)
            for (int ix = (int)(x - rad); ix <= (int)(x + rad) + 1; ix++)
            {
                if (ix < 0 || iz < 0 || ix >= World.SX || iz >= World.SZ) continue;
                float dx = ix + .5f - x, dz = iz + .5f - z;
                if (dx * dx + dz * dz > rad * rad) continue;
                int i = iz * World.SX + ix;
                if (_cellOf[i] != c) continue;
                byte had = _ground[i];
                _ground[i] = what == Road && had == River ? Ford : what == River && had is Road or Ford ? Ford : had is Crag or Pond ? had : what;
            }
    }
}
