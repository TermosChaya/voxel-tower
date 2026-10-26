using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// What the map's ground is dressed in as the campaign now has it: the columns of ground and water, the ash of the land
/// the horde holds (all of a cell it holds, spreading into the cells about it the more it holds) with fire in its cracks,
/// the woods, groves and lone trees of each kind of cell (dead ones in the ash), the flowers of the meadows, the reeds of
/// the marshes, the stones of the hills; and the buildings (Atlas.Works.cs). Made for all the map at once, or again for a
/// few cells: what grows in a cell next to them and leans over into them is put back too, and nothing is put outside them.
/// </summary>
public sealed partial class Atlas
{
    /// <summary>Per column: the highest voxel put over the ground by the dressing, and one above it (to be cleared before it is dressed again).</summary>
    int[] _built;

    /// <summary>The cells given (all of them and the land off the field, for null) dressed as the campaign now has them.</summary>
    void Dress(HashSet<MapCell> cells)
    {
        _built ??= new int[SX * SZ];
        Func<int, int, bool> within = cells == null ? null : (x, z) =>
            (uint)x < (uint)SX && (uint)z < (uint)SZ && _cellOf[z * SX + x] is var ci && ci >= 0 && cells.Contains(Campaign.Cells[ci]);
        Taints();
        int x0 = 0, x1 = SX - 1, z0 = 0, z1 = SZ - 1;
        if (cells != null)
        {
            x0 = SX; z0 = SZ; x1 = z1 = 0;
            foreach (var c in cells)
            {
                var (cx, cz) = Center(c);
                x0 = Math.Min(x0, (int)(cx - CellR - 2)); x1 = Math.Max(x1, (int)(cx + CellR + 2));
                z0 = Math.Min(z0, (int)(cz - CellR - 2)); z1 = Math.Max(z1, (int)(cz + CellR + 2));
            }
            x0 = Math.Max(0, x0); z0 = Math.Max(0, z0); x1 = Math.Min(SX - 1, x1); z1 = Math.Min(SZ - 1, z1);
        }
        // the ground and the water, what stood over them cleared away
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                if (within == null || within(x, z)) Column(x, z);
        // what was told of in these cells, told of again as it is put back
        if (cells == null) { Things.Clear(); Smokes.Clear(); Fires.Clear(); }
        else
        {
            Things.RemoveAll(t => cells.Contains(t.Cell));
            Smokes.RemoveAll(s => CellOfColumn(s.X, s.Z) is { } c && cells.Contains(c));
            Fires.RemoveAll(s => CellOfColumn(s.X, s.Z) is { } c && cells.Contains(c));
        }
        // what grows: in these cells and those about them (their trees lean over), put only within these
        var grow = new HashSet<MapCell>();
        foreach (var c in cells ?? (IEnumerable<MapCell>)Campaign.Cells)
        {
            grow.Add(c);
            if (cells != null) for (int s = 0; s < 6; s++) if (Campaign.Neighbour(c, s) is { } n) grow.Add(n);
        }
        // (in the campaign's order, as a new map grows them: where two crowns meet, the same one is over the other)
        foreach (var c in Campaign.Cells) if (grow.Contains(c)) Grow(c, within);
        if (cells == null) GrowOffField();
        foreach (var c in Campaign.Cells) if (cells == null || cells.Contains(c)) Works(c, within);
        Weigh(within);
        // what is told of stands where one may walk up to it: its front moved to the nearest such column
        foreach (var t in Things)
        {
            if (cells != null && !cells.Contains(t.Cell) || Walkable(t.X, t.Z)) continue;
            for (int r = 1; r <= 8; r++)
            {
                bool found = false;
                for (int dz = -r; dz <= r && !found; dz++)
                    for (int dx = -r; dx <= r && !found; dx++)
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == r && Walkable(t.X + dx, t.Z + dz) && CellOfColumn(t.X + dx, t.Z + dz) == t.Cell)
                        { t.X += dx; t.Z += dz; found = true; }
                if (found) break;
            }
        }
        foreach (var c in cells ?? (IEnumerable<MapCell>)Campaign.Cells)
        {
            var hex = HexOf(c);
            int top = Ground;
            var (cx, cz) = Center(c);
            for (int z = (int)(cz - CellR); z <= (int)(cz + CellR); z++)
                for (int x = (int)(cx - hex.HalfW); x <= (int)(cx + hex.HalfW); x++)
                    if (CellOfColumn(x, z) == c) top = Math.Max(top, Math.Max(_h[z * SX + x], _built[z * SX + x]));
            _cellTop[c] = top;
        }
        if (cells != null)
            for (int cy = 0; cy < World.CY; cy++)
                for (int cz = z0 >> VoxelWorld.Shift; cz <= z1 >> VoxelWorld.Shift; cz++)
                    for (int cx = x0 >> VoxelWorld.Shift; cx <= x1 >> VoxelWorld.Shift; cx++) World.MarkChunk(cx, cy, cz);
    }

    /// <summary>The middles of the cells the horde holds: its ash lies in a blot about each.</summary>
    readonly List<(float x, float z)> _held = new();

    void Taints()
    {
        _held.Clear();
        foreach (var c in Campaign.Cells) if (c.Owner == Owner.Horde) _held.Add(Center(c));
    }

    /// <summary>
    /// How deep the horde's ash lies at a column: all of it within most of a cell the horde holds, thinning raggedly over
    /// its sides into the cells about it; the more cells it holds, the more of the map lies under it.
    /// </summary>
    float TaintAt(int x, int z, MapCell c)
    {
        // (the land off the field is never dressed again, so the ash keeps to the field)
        if (_held.Count == 0 || c == null) return 0;
        float best = 0, wob = (Fbm(_n3, x / 9f + 90, z / 9f) - .5f) * 30;
        foreach (var (hx, hz) in _held)
        {
            if (MathF.Abs(x - hx) > CellR * 1.6f || MathF.Abs(z - hz) > CellR * 1.6f) continue;
            float d = Dist(x + .5f, z + .5f, hx, hz) + wob;
            best = MathF.Max(best, 1 - Smooth((d - CellR * .74f) / (CellR * .55f)));
        }
        return best;
    }

    /// <summary>A column's ground and water put in as they are, what stood over them cleared.</summary>
    void Column(int x, int z)
    {
        int i = z * SX + x, h = _h[i], wet = _wet[i];
        var c = CellOfColumn(x, z);
        byte top = _top[i];
        float taint = TaintAt(x, z, c);
        _lie[i] &= ~(Lie.Built | Lie.Trunk | Lie.Wood | Lie.Clear);
        if (taint > 0 && wet <= h && top != Mat.Snow)
        {
            float n = Fbm(_n2, x / 6f + 300, z / 6f);
            bool road = top == Mat.Dust;
            if (!road && n < taint * .95f) top = Mat.Ash;
            else if (taint > .5f && Mat.IsGrass(top) && n < taint * 1.2f) top = Mat.Scorch;
            // the cracks the fire shows in
            if (taint >= .9f && !road && (_lie[i] & Lie.Peak) == 0 && Crack(x, z)) top = Mat.Lava;
        }
        int clearTo = Math.Max(_built[i], Math.Max(h, wet));
        for (int y = 32; y < h; y++)
        {
            byte m = y == h - 1 ? top
                : top == Mat.Snow && y >= h - 3 ? Mat.Snow
                : top is Mat.Lava ? (y >= h - 2 ? Mat.Basalt : Mat.Rock)
                : y >= h - 3 && top is not (Mat.Rock or Mat.StoneDark or Mat.Moss or Mat.Snow) ? (top == Mat.Sand ? Mat.Sand : Mat.Soil)
                : Mat.Rock;
            World.SetRaw(x, y, z, m);
        }
        for (int y = h; y < wet; y++) World.SetRaw(x, y, z, Mat.Water);
        for (int y = Math.Max(h, wet); y < clearTo; y++) World.SetRaw(x, y, z, Mat.Air);
        _built[i] = Math.Max(h, wet);
    }

    /// <summary>Whether the ash cracks open at a column: thin wandering lines where two cells of a noise meet.</summary>
    bool Crack(int x, int z)
    {
        float a = Fbm(_n1, x / 11f + 70, z / 11f + 30), b = Fbm(_n1, (x + 1) / 11f + 70, z / 11f + 30), c = Fbm(_n1, x / 11f + 70, (z + 1) / 11f + 30);
        const float At = .5f;
        return (a - At) * (b - At) < 0 || (a - At) * (c - At) < 0;
    }

    // ------------------------------------------------------------ what grows

    /// <summary>A voxel put over the ground where nothing stands, within the cells dressed; the column's height kept.</summary>
    void Put(int x, int y, int z, byte m, Func<int, int, bool> within, Lie mark = Lie.None, bool over = false)
    {
        if ((uint)x >= (uint)SX || (uint)z >= (uint)SZ || y < 0 || y >= Height) return;
        if (within != null && !within(x, z)) return;
        if (within == null && !OnField(x, z) && mark == Lie.Built) return;
        if (!over && World.Get(x, y, z) != Mat.Air) return;
        World.SetRaw(x, y, z, m);
        int i = z * SX + x;
        if (y + 1 > _built[i]) _built[i] = y + 1;
        _lie[i] |= mark;
    }

    /// <summary>The trees, flowers, reeds and stones of a cell, the same each time it is dressed.</summary>
    void Grow(MapCell c, Func<int, int, bool> within)
    {
        var rnd = new Mulberry32(SeedOf(c, 11));
        var (cx, cz) = Center(c);
        float keepOff = c.Home ? 30 : c.Holding == Holding.HordeTown ? 22 : 13;
        var spot = _spot.TryGetValue(c, out var sp) ? sp : (x: -999f, z: -999f);
        int x0 = (int)(cx - CellR), x1 = (int)(cx + CellR), z0 = (int)(cz - CellR), z1 = (int)(cz + CellR);
        // the trees on a jittered grid, each kept or not by the woods' thickness there
        const int Step = 3;
        for (int gz = z0; gz <= z1; gz += Step)
            for (int gx = x0; gx <= x1; gx += Step)
            {
                double j1 = rnd.Next(), j2 = rnd.Next(), pick = rnd.Next(), shape = rnd.Next(), size = rnd.Next();
                int x = gx + (int)(j1 * Step), z = gz + (int)(j2 * Step);
                if (CellOfColumn(x, z) != c) continue;
                int i = z * SX + x;
                var lie = _lie[i];
                if (_wet[i] > _h[i] || (lie & (Lie.Road | Lie.Field | Lie.Shore | Lie.River | Lie.Lake)) != 0) continue;
                if (_dRoad[i] < 3 || _dRiver[i] < 5 || Dist(x, z, cx, cz) < keepOff || Dist(x, z, spot.x, spot.z) < 13) continue;
                if (_top[i] is Mat.Snow or Mat.Rock or Mat.StoneDark || _h[i] > Ground + 16) continue;
                if (Slope(x, z) > 1) continue;
                float clump = Fbm(_n2, x / 22f + 11, z / 22f + 5);
                float dens = c.Kind switch
                {
                    ValleyKind.Forest => .78f * Smooth((clump - .28f) / .22f) * (Dist(x, z, cx, cz) < 32 ? .5f : 1),
                    ValleyKind.Mountain => .5f * Smooth((clump - .3f) / .25f),
                    ValleyKind.Marsh => .2f * Smooth((clump - .35f) / .25f),
                    ValleyKind.Ore => .14f * Smooth((clump - .3f) / .3f),
                    ValleyKind.Sacred => .26f * Smooth((clump - .45f) / .2f),
                    _ => .2f * Smooth((clump - .5f) / .18f),
                };
                dens *= 1 - TaintAt(x, z, c) * .55f;
                if (pick >= dens) continue;
                int h = _h[i];
                float dead = TaintAt(x, z, c);
                if (dead > .3f && shape < dead) { Snag(x, h, z, size, within); continue; }
                switch (c.Kind)
                {
                    case ValleyKind.Mountain: Conifer(x, h, z, size, within); break;
                    case ValleyKind.Forest: if (shape < .45) Conifer(x, h, z, size, within); else Broadleaf(x, h, z, size, shape, within); break;
                    case ValleyKind.Marsh: if (shape < .7) Willow(x, h, z, size, within); else Birch(x, h, z, size, within); break;
                    case ValleyKind.Ore: if (shape < .6) Conifer(x, h, z, size * .7, within); else Birch(x, h, z, size, within); break;
                    case ValleyKind.Sacred: if (shape < .3) Birch(x, h, z, size, within); else Broadleaf(x, h, z, size, shape, within); break;
                    default: if (shape < .2) Birch(x, h, z, size, within); else Broadleaf(x, h, z, size, shape, within); break;
                }
            }
        // flowers in the meadows, reeds at the water, stones on the hills
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                // (a cell's own columns, though only those within are put: a stone may lie over into the next)
                if (CellOfColumn(x, z) != c) continue;
                bool inside = within == null || within(x, z);
                int i = z * SX + x, h = _h[i];
                var lie = _lie[i];
                double r = Hash.H3(x, z, 401);
                if (_wet[i] > h)
                {
                    // reeds in the shallows of the marshes and at the edges of the lakes
                    bool shallow = _wet[i] - h <= 1 && (lie & (Lie.Marsh | Lie.Lake | Lie.River)) != 0 && (lie & Lie.Ford) == 0;
                    if (shallow && r < (c.Kind == ValleyKind.Marsh ? .3 : .12) && TaintAt(x, z, c) < .5f)
                        for (int y = h; y <= _wet[i]; y++) Put(x, y, z, Mat.Reedbed, within, Lie.None, over: true);
                    continue;
                }
                // (a trunk standing there is told by the column's marks, not by what the field holds now, so that a cell
                // dressed again decides as a new map did)
                if ((lie & (Lie.Road | Lie.Field | Lie.Trunk)) != 0) continue;
                byte top = World.Get(x, h - 1, z);
                if (inside && Mat.IsGrass(top) && TaintAt(x, z, c) < .2f && c.Kind is ValleyKind.Sacred or ValleyKind.First or ValleyKind.Forest or ValleyKind.Marsh)
                {
                    float patch = Fbm(_n2, x / 9f + 200, z / 9f);
                    float want = c.Kind == ValleyKind.Sacred ? .55f : .64f;
                    if (patch > want && r < (patch - want) * 2.4)
                    {
                        float hue = Fbm(_n1, x / 26f + 9, z / 26f + 90);
                        byte m = hue < .45f ? Mat.BloomRed : hue < .6f ? Mat.BloomYellow : hue < .66f ? Mat.BloomWhite : hue < .7f ? Mat.BloomBlue : Mat.BloomYellow;
                        // (in the turf itself, a patch of colour on the meadow, not a block over it)
                        World.SetRaw(x, h - 1, z, m);
                    }
                }
                if (c.Kind is ValleyKind.Ore or ValleyKind.Mountain && r < .012 && (lie & Lie.Peak) == 0 && top != Mat.Snow)
                {
                    Put(x, h, z, r < .006 ? Mat.Rock : Mat.StoneDark, within);
                    if (r < .004) Put(x + 1, h, z, Mat.Rock, within);
                }
            }
    }

    int Slope(int x, int z)
    {
        int h = _h[z * SX + x], s = 0;
        for (int d = 0; d < 4; d++)
        {
            int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
            if ((uint)nx < (uint)SX && (uint)nz < (uint)SZ) s = Math.Max(s, Math.Abs(_h[nz * SX + nx] - h));
        }
        return s;
    }

    /// <summary>The trees of the hills and the dunes off the field, where the land rises.</summary>
    void GrowOffField()
    {
        var rnd = new Mulberry32(Campaign.Seed * 131 + 7);
        for (int gz = 0; gz < SZ; gz += 3)
            for (int gx = 0; gx < SX; gx += 3)
            {
                int x = gx + (int)(rnd.Next() * 3), z = gz + (int)(rnd.Next() * 3);
                double pick = rnd.Next(), size = rnd.Next();
                // (clear of the field, so that no crown leans over a cell dressed again)
                if (x >= SX || z >= SZ || OnField(x, z) || OnField(x + 5, z) || OnField(x - 5, z) || OnField(x, z + 5) || OnField(x, z - 5)) continue;
                int i = z * SX + x;
                if (_wet[i] > _h[i] || _h[i] > Ground + 14 || _top[i] is Mat.Snow or Mat.Rock or Mat.StoneDark or Mat.Sand || Slope(x, z) > 1) continue;
                if (_dRiver[i] < 5 || _dRoad[i] < 3) continue;
                float clump = Fbm(_n2, x / 18f, z / 18f + 70);
                if (pick < .45f * Smooth((clump - .35f) / .2f)) Conifer(x, _h[i], z, size, null);
            }
    }

    void Trunk(int x, int y0, int y1, int z, byte m, Func<int, int, bool> within)
    {
        for (int y = y0; y < y1; y++) Put(x, y, z, m, within, Lie.Trunk);
    }

    /// <summary>A crown: a blob of leaves round a point, its edge broken.</summary>
    void Crown(float cx, float cy, float cz, float rx, float ry, byte a, byte b, Func<int, int, bool> within, int seed)
    {
        for (int y = (int)MathF.Floor(cy - ry); y <= (int)MathF.Ceiling(cy + ry); y++)
            for (int z = (int)MathF.Floor(cz - rx); z <= (int)MathF.Ceiling(cz + rx); z++)
                for (int x = (int)MathF.Floor(cx - rx); x <= (int)MathF.Ceiling(cx + rx); x++)
                {
                    float dx = (x + .5f - cx) / rx, dy = (y + .5f - cy) / ry, dz = (z + .5f - cz) / rx;
                    float d = dx * dx + dy * dy + dz * dz;
                    double h = Hash.H3(x * 3 + seed, y, z);
                    if (d > 1 || d > .62f && h < .35) continue;
                    Put(x, y, z, h < .22 ? b : a, within, Lie.Wood);
                }
    }

    void Broadleaf(int x, int h, int z, double size, double shape, Func<int, int, bool> within)
    {
        int t = 2 + (int)(size * 2);
        float r = 1.9f + (float)size * 1.2f;
        Trunk(x, h, h + t, z, Mat.Trunk, within);
        byte a = shape > .93 ? Mat.FoliageGold : shape > .7 ? Mat.FoliageLight : Mat.Foliage, b = shape > .7 ? Mat.Foliage : Mat.FoliageLight;
        Crown(x + .5f, h + t + r * .75f, z + .5f, r, r * .85f, a, b, within, x);
    }

    void Conifer(int x, int h, int z, double size, Func<int, int, bool> within)
    {
        int t = 1 + (int)(size * 1.6), tall = 5 + (int)(size * 5);
        float r0 = 1.6f + (float)size * 1.3f;
        Trunk(x, h, h + t, z, Mat.PineBark, within);
        for (int k = 0; k <= tall; k++)
        {
            // in tiers: each a little wider at its foot than the one above
            float f = 1 - k / (float)(tall + 1), r = r0 * f * (1 + .18f * ((k % 2) == 0 ? 1 : -.3f)) + .35f;
            int y = h + t + k;
            for (int dz = -(int)r - 1; dz <= (int)r + 1; dz++)
                for (int dx = -(int)r - 1; dx <= (int)r + 1; dx++)
                {
                    float d = MathF.Sqrt(dx * dx + dz * dz);
                    if (d > r) continue;
                    Put(x + dx, y, z + dz, Hash.H3(x + dx, y, z + dz) < .25 ? Mat.NeedleLight : Mat.Leaf, within, Lie.Wood);
                }
        }
        Put(x, h + t + tall + 1, z, Mat.Leaf, within, Lie.Wood);
    }

    void Birch(int x, int h, int z, double size, Func<int, int, bool> within)
    {
        int t = 3 + (int)(size * 2);
        Trunk(x, h, h + t, z, Mat.Birch, within);
        Crown(x + .5f, h + t + .8f, z + .5f, 1.5f + (float)size * .6f, 1.9f, Mat.FoliageLight, Mat.FoliageGold, within, z);
    }

    void Willow(int x, int h, int z, double size, Func<int, int, bool> within)
    {
        int t = 2;
        float r = 2.4f + (float)size * 1.2f;
        Trunk(x, h, h + t + 1, z, Mat.Trunk, within);
        Crown(x + .5f, h + t + 1.5f, z + .5f, r, 1.6f, Mat.FoliageLight, Mat.Foliage, within, x + z);
        // the hanging edge
        for (int dz = -(int)r; dz <= (int)r; dz++)
            for (int dx = -(int)r; dx <= (int)r; dx++)
            {
                float d = MathF.Sqrt(dx * dx + dz * dz);
                if (d < r - 1 || d > r || Hash.H3(x + dx, h, z + dz) < .4) continue;
                Put(x + dx, h + t, z + dz, Mat.FoliageLight, within, Lie.Wood);
                if (Hash.H3(x + dx, h + 1, z + dz) < .5) Put(x + dx, h + t - 1, z + dz, Mat.FoliageLight, within, Lie.Wood);
            }
    }

    void Snag(int x, int h, int z, double size, Func<int, int, bool> within)
    {
        int t = 3 + (int)(size * 3);
        Trunk(x, h, h + t, z, Mat.Snag, within);
        int by = h + t - 1 - (int)(size * 2), dir = (int)(size * 40) % 4;
        int bx = dir == 0 ? 1 : dir == 1 ? -1 : 0, bz = dir == 2 ? 1 : dir == 3 ? -1 : 0;
        Put(x + bx, by, z + bz, Mat.Snag, within, Lie.Wood);
        Put(x + 2 * bx, by + 1, z + 2 * bz, Mat.Snag, within, Lie.Wood);
    }
}
