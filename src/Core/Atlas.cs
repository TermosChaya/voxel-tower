using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What stands on the map of valleys and is told of: the first valley's castle, an outpost of ours, the place
/// an outpost is to go, the horde's lair and a camp of its in a valley it took, a lair of the horde taken, the mines of an
/// ore valley, the barrow of a holy one, and the treasures — the abandoned mine, the ruins, the den, the shrine of trial.</summary>
public enum AtlasThingKind : byte { Citadel, Outpost, OutpostPlace, Lair, HordeCamp, TakenLair, Mine, PlaceOfPower, DeadMine, Ruins, Den, Shrine }

/// <summary>A thing of the map where it stands: its cell, and the column before it (its door, its gate, its mouth).</summary>
public sealed class AtlasThing
{
    public AtlasThingKind Kind;
    public MapCell Cell;
    public int X, Z;
}

/// <summary>
/// The map of valleys as a land to look at and walk on, as the adventure map of «Heroes» is (docs/handoff/13-world-map.md,
/// the author's word of 26.10.2026): every cell of the campaign a hexagon of ground a hundred voxels across, a miniature
/// of its valley — the woods of a forest, the grey peaks of the mountains with snow on them, the marshes and lakes of a
/// river's valley, the rocks of an ore valley, the meadows of a holy one; the rivers and the roads over the sides the
/// campaign laid them by (Campaign.LayWays), so that neighbours agree; the first valley's castle among its fields, the
/// outposts, the horde's lairs in their scorched land, the treasures; round the field mountains and the sea. It is a voxel
/// field like a valley's, drawn by the game's own pipeline, at a scale of its own: a tree a few voxels, a castle thirty.
/// It is made from the campaign alone, the same campaign the same land (Atlas.Land.cs lays the ground, Atlas.Dress.cs
/// puts on it what grows and what is built, Atlas.Works.cs the buildings); what changes as the campaign goes on — who
/// holds a cell, a treasure taken — is put on again for that cell alone (<see cref="Refresh"/>).
/// </summary>
public sealed partial class Atlas
{
    /// <summary>A cell's hexagon: voxels from its middle to a corner (its straight sides 97 apart).</summary>
    public const int CellR = 56;
    /// <summary>The land round the field of cells, voxels: the mountains and the sea the map ends in.</summary>
    public const int Margin = 72;
    /// <summary>The field's height in voxels; the level ground's (the first air over it); the water's top (the first air over it).</summary>
    public const int Height = 128, Ground = 40, WaterLevel = 39;
    const float Root3 = 1.7320508f;

    public VoxelWorld World;
    public Campaign Campaign;
    /// <summary>What stands on the map and is told of, cell by cell.</summary>
    public readonly List<AtlasThing> Things = new();
    /// <summary>Where smoke rises: the lairs' fires, the castle's and the cottages' chimneys (voxels).</summary>
    public readonly List<(int X, int Y, int Z)> Smokes = new();
    /// <summary>Where a fire glows at night and by day: the lairs' fires, the shrines' ghost fire (voxels; true for the ghost fire).</summary>
    public readonly List<(int X, int Y, int Z, bool Ghost)> Fires = new();

    public readonly int SX, SZ;
    /// <summary>The middle of the first valley's cell, voxels.</summary>
    public readonly float OX, OZ;

    readonly Dictionary<(int, int), MapCell> _byAxial = new();
    readonly Dictionary<MapCell, int> _index = new();
    readonly Hex[] _hex;
    /// <summary>Per column (z·SX + x): the cell it is in (its index in the campaign's cells), −1 off the field.</summary>
    readonly short[] _cellOf;

    Atlas(Campaign c)
    {
        Campaign = c;
        int n = Math.Max(1, c.Rings);
        float w = Root3 * CellR * (2 * n + 1) + 2 * Margin, h = (3 * n + 2) * CellR + 2 * Margin;
        SX = ((int)MathF.Ceiling(w) + 31) / 32 * 32;
        SZ = ((int)MathF.Ceiling(h) + 31) / 32 * 32;
        OX = SX / 2f; OZ = SZ / 2f;
        _hex = new Hex[c.Cells.Count];
        for (int i = 0; i < c.Cells.Count; i++)
        {
            var cell = c.Cells[i];
            _byAxial[(cell.Q, cell.R)] = cell;
            _index[cell] = i;
            var (cx, cz) = Center(cell);
            _hex[i] = new Hex(cx, cz, CellR);
        }
        _cellOf = new short[SX * SZ];
        for (int z = 0; z < SZ; z++)
            for (int x = 0; x < SX; x++)
                _cellOf[z * SX + x] = (short)(CellAt(x + .5f, z + .5f) is { } cell ? _index[cell] : -1);
    }

    /// <summary>The map of a campaign, made whole: the land, what grows and is built on it, where one may walk.</summary>
    public static Atlas Build(Campaign c)
    {
        var a = new Atlas(c);
        a.World = new VoxelWorld(a.SX, Height, a.SZ);
        // (the first layer of chunks is rock right through, the ground lies in the second)
        for (int cz = 0; cz < a.World.CZ; cz++)
            for (int cx = 0; cx < a.World.CX; cx++) a.World.SetChunk(a.World.ChunkIndex(cx, 0, cz), null, Mat.Rock);
        a.Land();
        a.Dress(null);
        return a;
    }

    /// <summary>
    /// A cell put on again as the campaign now has it — its holder, its treasure — and its neighbours with it, whose ground
    /// the horde's ash spreads to and whose trees lean over it. Only their chunks are made again.
    /// </summary>
    public void Refresh(MapCell c)
    {
        if (c == null || !_index.ContainsKey(c)) return;
        var set = new HashSet<MapCell> { c };
        for (int s = 0; s < 6; s++) if (Campaign.Neighbour(c, s) is { } n) set.Add(n);
        Dress(set);
    }

    // ------------------------------------------------------------ where things are

    /// <summary>The middle of a cell, voxels.</summary>
    public (float X, float Z) Center(MapCell c) => (OX + Root3 * CellR * (c.Q + c.R / 2f), OZ + 1.5f * CellR * c.R);

    /// <summary>A cell's hexagon.</summary>
    public Hex HexOf(MapCell c) => _hex[_index[c]];

    /// <summary>The cell a point is in, null off the field.</summary>
    public MapCell CellAt(float x, float z) => _byAxial.TryGetValue(Axial(x, z), out var c) ? c : null;

    /// <summary>The axial cell (q, r) a point lies in, on the field or off it.</summary>
    (int q, int r) Axial(float x, float z)
    {
        float px = (x - OX) / CellR, pz = (z - OZ) / CellR;
        float fq = Root3 / 3 * px - pz / 3, fr = 2f / 3 * pz, fs = -fq - fr;
        int q = (int)MathF.Round(fq), r = (int)MathF.Round(fr), s = (int)MathF.Round(fs);
        float dq = MathF.Abs(q - fq), dr = MathF.Abs(r - fr), ds = MathF.Abs(s - fs);
        if (dq > dr && dq > ds) q = -r - s;
        else if (dr > ds) r = -q - s;
        return (q, r);
    }

    /// <summary>The cell a column is in, null off the field (as <see cref="CellAt"/>, from what was worked out once).</summary>
    public MapCell CellOfColumn(int x, int z) =>
        (uint)x < (uint)SX && (uint)z < (uint)SZ && _cellOf[z * SX + x] is var i && i >= 0 ? Campaign.Cells[i] : null;

    /// <summary>Whether a column is on the field of cells.</summary>
    public bool OnField(int x, int z) => (uint)x < (uint)SX && (uint)z < (uint)SZ && _cellOf[z * SX + x] >= 0;

    /// <summary>The height one stands at on a column: the first air over its ground, or over the water on it.</summary>
    public int GroundAt(int x, int z)
    {
        if ((uint)x >= (uint)SX || (uint)z >= (uint)SZ) return Ground;
        int i = z * SX + x;
        return Math.Max(_h[i], _wet[i]);
    }

    /// <summary>Whether one may walk a column: on the field, not up a crag, through a river off its ford or a lake, nor through a wall.</summary>
    public bool Walkable(int x, int z) => Cost(x, z) < float.PositiveInfinity;

    /// <summary>
    /// What a step onto a column costs, in the hours of a voxel against open ground (AtlasWalk.cs walks by it, and the
    /// cells keep the time by it): 1 on open ground, a road its share (<see cref="MapPath.RoadShare"/>), a ford its two
    /// hours more over its width (3,5), elsewhere the hours its cell takes to cross over a cell's eight
    /// (<see cref="MapPath.Hours"/>: the woods, the marshes and the mountains slower); infinite where one cannot go.
    /// </summary>
    public float Cost(int x, int z) => (uint)x < (uint)SX && (uint)z < (uint)SZ ? _cost[z * SX + x] : float.PositiveInfinity;

    /// <summary>The cost of a ford's voxel: its two hours more over the four voxels or so a party wades.</summary>
    public const float FordCost = 3.5f;

    /// <summary>The way the water runs over a column of a river, voxels a second (still water and dry land: none).</summary>
    public (float X, float Z) FlowAt(int x, int z)
    {
        if ((uint)x >= (uint)SX || (uint)z >= (uint)SZ) return (0, 0);
        int i = z * SX + x;
        if (_wet[i] <= _h[i] || (_lie[i] & Lie.River) == 0) return (0, 0);
        return (_flowX[i] * 2.2f, _flowZ[i] * 2.2f);
    }

    /// <summary>The highest ground and building of a cell, for what hangs over it (the clouds of the fog).</summary>
    public int TopOf(MapCell c) => _cellTop.TryGetValue(c, out int t) ? t : Ground;

    /// <summary>The thing of a kind standing in a cell, if there is one.</summary>
    public AtlasThing ThingIn(MapCell c, params AtlasThingKind[] kinds)
    {
        foreach (var t in Things) if (t.Cell == c && Array.IndexOf(kinds, t.Kind) >= 0) return t;
        return null;
    }

    /// <summary>
    /// The cheapest walk between two columns over the ground, by the costs of <see cref="Cost"/> (A*, eight ways):
    /// the columns stepped on, null when there is no way. For the checks of the bench; the hero's walk is his own.
    /// </summary>
    public List<(int x, int z)> Walk(int x0, int z0, int x1, int z1, int most = 400000)
    {
        if (!Walkable(x0, z0) || !Walkable(x1, z1)) return null;
        var g = new Dictionary<int, float>();
        var from = new Dictionary<int, int>();
        var open = new PriorityQueue<int, float>();
        int start = z0 * SX + x0, goal = z1 * SX + x1;
        g[start] = 0;
        open.Enqueue(start, 0);
        int looked = 0;
        while (open.Count > 0 && looked++ < most)
        {
            int at = open.Dequeue();
            if (at == goal)
            {
                var path = new List<(int, int)>();
                for (int p = goal; ; p = from[p]) { path.Add((p % SX, p / SX)); if (p == start) break; }
                path.Reverse();
                return path;
            }
            int ax = at % SX, az = at / SX;
            float ga = g[at];
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int bx = ax + dx, bz = az + dz;
                    float c = Cost(bx, bz);
                    if (c == float.PositiveInfinity) continue;
                    float step = (dx != 0 && dz != 0 ? 1.4142f : 1) * c;
                    int b = bz * SX + bx;
                    float gb = ga + step;
                    if (g.TryGetValue(b, out float old) && old <= gb) continue;
                    g[b] = gb;
                    from[b] = at;
                    float hx = bx - x1, hz = bz - z1;
                    open.Enqueue(b, gb + .6f * MathF.Sqrt(hx * hx + hz * hz));
                }
        }
        return null;
    }

    // ------------------------------------------------------------ the costs of walking

    /// <summary>What lies on a column besides its ground: a road, a ford, a river's water, a field, a wall, a trunk, the woods.</summary>
    [Flags]
    enum Lie : ushort { None = 0, Road = 1, Ford = 2, River = 4, Field = 8, Built = 16, Trunk = 32, Wood = 64, Shore = 128, Lake = 256, Marsh = 512, Peak = 1024, Clear = 2048 }

    float[] _cost;
    readonly Dictionary<MapCell, int> _cellTop = new();

    /// <summary>The costs of the columns of the cells given (all, for null), from their ground and what stands on it.</summary>
    void Weigh(Func<int, int, bool> within)
    {
        _cost ??= new float[SX * SZ];
        for (int z = 0; z < SZ; z++)
            for (int x = 0; x < SX; x++)
            {
                if (within != null && !within(x, z)) continue;
                int i = z * SX + x;
                _cost[i] = CostOf(x, z, i);
            }
    }

    float CostOf(int x, int z, int i)
    {
        const float No = float.PositiveInfinity;
        if (_cellOf[i] < 0) return No;
        var lie = _lie[i];
        if ((lie & Lie.Built) != 0) return No;
        var cell = Campaign.Cells[_cellOf[i]];
        // a river and its banks are crossed only at a ford: a band of the road over it wide enough for a walk over a
        // grid of a few voxels to find, and the river's band so wide that no such walk steps over it
        float band = cell.Kind == ValleyKind.Marsh ? 4.6f : 3.6f;
        if (_dRiver[i] < band) return _dRoad[i] < 4.2f ? FordCost : No;
        int h = _h[i], depth = _wet[i] - h;
        // a lake's water and its shallows no; a marsh's shallow pools are waded
        if (depth > 1 || depth > 0 && (lie & Lie.Marsh) == 0) return No;
        // too steep a step up or down to a neighbour: a crag
        for (int d = 0; d < 4; d++)
        {
            int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
            if ((uint)nx < (uint)SX && (uint)nz < (uint)SZ && Math.Abs(_h[nz * SX + nx] - h) > 2) return No;
        }
        if ((lie & Lie.Road) != 0) return (float)MapPath.RoadShare;
        return (float)(MapPath.Hours(cell) / 8);
    }

    /// <summary>The roads of a cell (not the tracks), each a line of points from its middle out to a side (for the checks).</summary>
    public IReadOnlyList<(float x, float z)[]> RoadsOf(MapCell c) => _mainRoads.TryGetValue(c, out var l) ? l : Array.Empty<(float, float)[]>();
}
