using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The world map's land made in voxels a piece at a time (HexMapLayout.Tile square), from the looks of its hexes alone
/// (HexLook.cs), so any piece can be made again the same at any time and none is kept but those drawn. Every hex is one
/// walking tile, read at a glance: its ground one colour (grass, earth, sand, snow, the moss of a swamp with its pools,
/// rough gravel, the horde's ash and lava), at its height a step or two above the next; towards a side it leans to the
/// next hex's height, so the land runs on; a lake's hex is water with a shore; hills a mound; a mountain a sharp peak
/// that fills its hex, the peaks of a range one ridge; a wood three to six trees; roads and rivers run from its middle
/// out over the sides they cross, a bridge of planks where a road crosses a river; its thing (HexModels.cs) stands at
/// its middle on ground made level for it. A piece is a world of 192 × 64 × 192 voxels, the piece in the middle of it
/// with a chunk of margin round it of which only the voxels next to the piece are made, so the mesher sees the faces
/// at its sides as they are.
/// </summary>
public static class HexTiles
{
    /// <summary>The ground's height of a hex of height 0; a step of height; the top of a lake's water; the floor of a lake.</summary>
    public const int Base = 16, Step = 2, WaterTop = 15, LakeFloor = 11;
    /// <summary>A piece's world: the margin about the piece, its side, its height.</summary>
    public const int Pad = 32, Size = HexMapLayout.Tile + 2 * Pad, SY = 64;
    /// <summary>How near a side a hex leans to the next one's height, voxels.</summary>
    const float Blend = 8;
    /// <summary>Half the width of a road and of a river's bed; how near the middle a thing's ground is made level.</summary>
    const float RoadHalf = 1.6f, RiverHalf = 2.3f, Level = 9.5f;

    /// <summary>The look of a hex, off the field the mountains that frame it.</summary>
    public static HexLook LookOf(IHexSource src, int col, int row) =>
        (uint)col < (uint)src.Width && (uint)row < (uint)src.Height ? src.Look(col, row)
        : new HexLook { Ground = HexGround.Rough, Relief = HexRelief.Mountain, Height = 2 };

    /// <summary>
    /// What makes one hex unlike the next of its kind (how many trees, how tall its peak, where its mounds), 0…255, from its
    /// place alone: the look's Variant says what became of its thing (bit 0 spent, bit 1 kept by a stack).
    /// </summary>
    public static int Vary(int col, int row) => (int)(Hash.H3(col, row, 977) * 256);

    /// <summary>A hex's thing spent (picked up, opened, its week's given, its treasure taken); kept by a stack.</summary>
    public static bool Spent(in HexLook l) => (l.Variant & 1) != 0;
    public static bool Kept(in HexLook l) => (l.Variant & 2) != 0;

    static bool Wet(HexGround g) => g is HexGround.Water or HexGround.DeepWater;

    /// <summary>A hex's own level: its height's steps, a lake's floor.</summary>
    static float LevelOf(in HexLook l) => Wet(l.Ground) ? LakeFloor - (l.Ground == HexGround.DeepWater ? 2 : 0) : Base + l.Height * Step;

    /// <summary>How tall a mountain's peak stands over its hex's ground.</summary>
    static float PeakOf(int vary) => 24 + vary % 11;

    /// <summary>A smooth noise 0…1 over the map, its features so many voxels across (a lattice of hashed values, eased between).</summary>
    public static double Patch(int x, int z, int scale, int salt)
    {
        int xi = Math.DivRem(x, scale, out int xr), zi = Math.DivRem(z, scale, out int zr);
        double fx = xr / (double)scale, fz = zr / (double)scale;
        fx = fx * fx * (3 - 2 * fx); fz = fz * fz * (3 - 2 * fz);
        double a = Hash.H3(xi, zi, salt), b = Hash.H3(xi + 1, zi, salt), c = Hash.H3(xi, zi + 1, salt), d = Hash.H3(xi + 1, zi + 1, salt);
        return (a + (b - a) * fx) * (1 - fz) + (c + (d - c) * fx) * fz;
    }

    /// <summary>The distance from a point to a segment.</summary>
    static float SegDist(float px, float pz, float ax, float az, float bx, float bz)
    {
        float vx = bx - ax, vz = bz - az, wx = px - ax, wz = pz - az;
        float t = Math.Clamp((wx * vx + wz * vz) / (vx * vx + vz * vz), 0, 1);
        float dx = wx - vx * t, dz = wz - vz * t;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>How far a point lies from the roads (or the rivers) of its hex, run from its middle out over the sides marked.</summary>
    static float WayDist(byte sides, float dx, float dz, float end)
    {
        if (sides == 0) return float.MaxValue;
        float best = float.MaxValue;
        int n = 0;
        for (int s = 0; s < 6; s++)
        {
            if ((sides & (1 << s)) == 0) continue;
            n++;
            var (tx, tz) = HexMapLayout.Toward(s);
            best = MathF.Min(best, SegDist(dx, dz, 0, 0, tx * .5f * end, tz * .5f * end));
        }
        // a way that ends here: a pool or a turning place at the middle
        if (n == 1) best = MathF.Min(best, MathF.Sqrt(dx * dx + dz * dz) - 1.2f);
        return best;
    }

    /// <summary>What the ground of a column is: its top, the level of water over it (0 none), the material of its top.</summary>
    public struct Column
    {
        public int Top, Water;
        public byte Mat, Under;
        public bool Bridge;
    }

    /// <summary>The ground at a column of the map, worked out from the hexes about it.</summary>
    public static Column ColumnAt(IHexSource src, HexMapLayout lay, int x, int z)
    {
        var (col, row) = lay.HexAt(x + .5f, z + .5f);
        var l = LookOf(src, col, row);
        int vr = Vary(col, row);
        var (cx, cz) = lay.Center(col, row);
        float dx = x + .5f - cx, dz = z + .5f - cz;
        float e = HexMapLayout.EdgeDistance(dx, dz);
        float h = LevelOf(l);
        bool wet = Wet(l.Ground), shore = false;
        // toward the side, the height leans halfway to the next hex's
        if (e < Blend)
        {
            int side = HexMapLayout.SideToward(dx, dz);
            var (nc, nr) = HexMapLayout.Neighbour(col, row, side);
            var n = LookOf(src, nc, nr);
            float s = 1 - e / Blend;
            float t = .5f * s * s * (3 - 2 * s);
            float hn = LevelOf(n);
            h += (hn - h) * t;
            if (Wet(n.Ground) && !wet) { shore = true; if (e < Blend * .55f) h = MathF.Min(h, WaterTop - .2f); }
        }
        float r = MathF.Sqrt(dx * dx + dz * dz);
        bool thing = l.Icon != HexIcon.None && l.Icon != HexIcon.Monster;
        if (thing && r < Level && !wet) h = LevelOf(l) + (r > Level - 3 ? (h - LevelOf(l)) * (r - (Level - 3)) / 3 : 0);
        // hills: two or three low mounds
        if (l.Relief == HexRelief.Hills && !thing)
        {
            int n = 2 + vr % 2;
            for (int k = 0; k < n; k++)
            {
                double a = (vr * .7 + k * Math.Tau / n);
                float mx = (float)Math.Cos(a) * 6.5f, mz = (float)Math.Sin(a) * 6.5f;
                float d = MathF.Sqrt((dx - mx) * (dx - mx) + (dz - mz) * (dz - mz)) / 8f;
                if (d < 1) { float c = MathF.Cos(d * MathF.PI / 2); h = MathF.Max(h, LevelOf(l) + (3.2f + k) * c * c); }
            }
        }
        var col0 = new Column { Top = (int)MathF.Round(h), Water = 0 };
        var g = l.Ground;
        // the ground's shades in patches of a few voxels, not speckled voxel by voxel: patches mesh into few faces
        double v = Patch(x, z, 7, 77), f = Patch(x, z, 4, 91);
        col0.Under = Mat.Soil;
        col0.Mat = g switch
        {
            HexGround.Grass => l.Relief == HexRelief.Forest ? (v < .55 ? Mat.Soil : Mat.GrassDry) : v < .28 ? Mat.GrassLush : v > .8 ? Mat.GrassDry : Mat.Grass,
            HexGround.Dirt => v < .3 ? Mat.Trodden : v > .82 ? Mat.GrassDry : Mat.Soil,
            HexGround.Sand => f > .86 ? Mat.Gravel : Mat.Sand,
            HexGround.Snow => f > .88 ? Mat.Rock : Mat.Snow,
            HexGround.Swamp => v < .35 ? Mat.Reedbed : Mat.Moss,
            HexGround.Rough => f > .8 ? Mat.Rock : v < .25 ? Mat.Soil : Mat.Gravel,
            HexGround.Lava => Mat.Basalt,
            HexGround.Scorched => f > .85 ? Mat.Char : Mat.Ash,
            _ => v < .5 ? Mat.Sand : Mat.Soil,
        };
        if (g == HexGround.Sand) col0.Under = Mat.Sand;
        if (g is HexGround.Lava or HexGround.Scorched) col0.Under = Mat.Basalt;
        // lava in cracks over the horde's burnt ground
        if (g == HexGround.Lava || g == HexGround.Scorched && f > .78)
        {
            double crack = Math.Abs(Math.Sin((x * .37 + Math.Sin(z * .21) * 2.4) + vr) + Math.Sin(z * .29 - x * .11));
            if (crack < (g == HexGround.Lava ? .09 : .05)) { col0.Mat = Mat.Lava; col0.Top -= 0; }
        }
        // a lake: water to its top, a shore of sand
        if (wet || col0.Top < WaterTop)
        {
            if (col0.Top < WaterTop) col0.Water = WaterTop;
            col0.Mat = col0.Top >= WaterTop - 2 ? Mat.Sand : Mat.Soil;
            col0.Under = Mat.Soil;
        }
        else if (shore && col0.Top <= WaterTop + 2 && g != HexGround.Swamp) col0.Mat = Mat.Sand;
        if (!wet)
        {
            // a swamp's pools
            if (g == HexGround.Swamp && !thing)
            {
                double p = Math.Sin(x * .41 + vr) * Math.Sin(z * .37 - vr * .3) + Hash.H3(x >> 2, z >> 2, 5) * .6;
                if (p > .62 && e > 2) { col0.Water = col0.Top; col0.Top -= 1; col0.Mat = Mat.Soil; }
            }
            // roads and rivers
            float end = 1.02f;
            float road = WayDist(l.Roads, dx, dz, end), river = WayDist(l.Rivers, dx, dz, end);
            if (river < RiverHalf)
            {
                if (road < RoadHalf + .4f) { col0.Bridge = true; col0.Mat = Mat.Plank; }
                else { col0.Water = col0.Top - 1; col0.Top -= 2; col0.Mat = river < RiverHalf - .8f ? Mat.Soil : Mat.Sand; }
            }
            else if (road < RoadHalf && col0.Water == 0) col0.Mat = l.Icon == HexIcon.Citadel || g == HexGround.Snow ? Mat.Cobble : Mat.Dust;
            else if (river < RiverHalf + 1.2f && g is HexGround.Grass or HexGround.Dirt or HexGround.Swamp) col0.Mat = Mat.GrassLush;
        }
        // things' ground: the earth trodden about the horde's towns, the citadel's yard
        if (thing && r < Level - 2 && col0.Water == 0 && !col0.Bridge)
        {
            if (l.Icon is HexIcon.HordeTown or HexIcon.Lair) col0.Mat = v < .5 ? Mat.Ash : Mat.Trodden;
            else if (l.Icon == HexIcon.Citadel) col0.Mat = Mat.Trodden;
        }
        return col0;
    }

    /// <summary>The height one stands at on a column of the map: the ground's top, or a mountain's.</summary>
    public static int StandAt(IHexSource src, HexMapLayout lay, int x, int z)
    {
        var c = ColumnAt(src, lay, x, z);
        int top = Math.Max(c.Top, c.Water);
        float m = MountainAt(src, lay, x, z);
        return Math.Max(top, (int)(c.Top + m));
    }

    /// <summary>How high the mountains about a column rise over its ground: the most of the peaks of its hex and those beside.</summary>
    public static float MountainAt(IHexSource src, HexMapLayout lay, int x, int z)
    {
        var (col, row) = lay.HexAt(x + .5f, z + .5f);
        float best = 0;
        for (int s = -1; s < 6; s++)
        {
            var (c, r) = s < 0 ? (col, row) : HexMapLayout.Neighbour(col, row, s);
            var l = LookOf(src, c, r);
            if (l.Relief != HexRelief.Mountain) continue;
            var (cx, cz) = lay.Center(c, r);
            // the peak a little off the middle, as the variant has it
            int vm = Vary(c, r);
            float ox = (vm % 7 - 3) * .7f, oz = (vm / 7 % 7 - 3) * .7f;
            float dx = x + .5f - cx - ox, dz = z + .5f - cz - oz;
            float d = MathF.Sqrt(dx * dx + dz * dz), reach = HexMapLayout.R * 1.2f;
            if (d < reach)
            {
                float k = 1 - d / reach;
                float h = PeakOf(vm) * MathF.Pow(k, 1.35f);
                // the rock's ribs down its sides
                float ang = MathF.Atan2(dz, dx);
                h += (MathF.Sin(ang * 5 + vm) * .5f + .5f) * 3.5f * k * (1 - k) * 4;
                best = MathF.Max(best, h);
            }
            // a ridge to each mountain beside it, so a range is one wall and not a string of beads
            for (int t = 0; t < 3; t++)
            {
                var (mc, mr) = HexMapLayout.Neighbour(c, r, t);
                var m = LookOf(src, mc, mr);
                if (m.Relief != HexRelief.Mountain) continue;
                var (ex, ez) = lay.Center(mc, mr);
                float rd = SegDist(x + .5f, z + .5f, cx, cz, ex, ez), across = HexMapLayout.R * .8f;
                if (rd >= across) continue;
                float kk = 1 - rd / across;
                best = MathF.Max(best, MathF.Min(PeakOf(vm), PeakOf(Vary(mc, mr))) * .55f * MathF.Pow(kk, 1.2f));
            }
        }
        return best;
    }

    // ------------------------------------------------------------ a piece made

    /// <summary>A piece of the map made into its world: the ground of every column in it and the one about it, the woods, the peaks, the things.</summary>
    public static VoxelWorld Build(IHexSource src, HexMapLayout lay, int tx, int tz)
    {
        var w = new VoxelWorld(Size, SY, Size);
        int ox = tx * HexMapLayout.Tile - Pad, oz = tz * HexMapLayout.Tile - Pad;
        var canvas = new HexCanvas(w, ox, oz, Pad - 1, Pad + HexMapLayout.Tile + 1);
        // the ground
        for (int lz = Pad - 1; lz < Pad + HexMapLayout.Tile + 1; lz++)
            for (int lx = Pad - 1; lx < Pad + HexMapLayout.Tile + 1; lx++)
            {
                int x = ox + lx, z = oz + lz;
                if (x < 0 || z < 0 || x >= lay.SX || z >= lay.SZ) continue;
                var c = ColumnAt(src, lay, x, z);
                int top = c.Top;
                w.FillColumnRaw(lx, lz, 0, Math.Max(1, top - 4), Mat.Rock);
                if (top - 1 > 0) w.FillColumnRaw(lx, lz, Math.Max(1, top - 4), top - 1, c.Under);
                if (top > 0) w.SetRaw(lx, top - 1, lz, c.Mat);
                if (c.Water > top) w.FillColumnRaw(lx, lz, top, c.Water, Mat.Water);
                // the mountains
                float m = MountainAt(src, lay, x, z);
                if (m > .6f)
                {
                    int peak = top + (int)MathF.Round(m);
                    var (hc, hr) = lay.HexAt(x + .5f, z + .5f);
                    bool snowy = LookOf(src, hc, hr).Ground == HexGround.Snow;
                    float snowAt = top + (snowy ? 6 : 24) + (float)Hash.H3(x >> 1, z >> 1, 9) * 3;
                    for (int y = top - 1; y < Math.Min(SY - 1, peak); y++)
                    {
                        byte rock = y >= snowAt && y >= peak - 6 ? Mat.Snow : Hash.H3(x >> 2, y >> 1, z >> 2) < .3 ? Mat.Rock : y > top + 8 ? Mat.StoneDark : Mat.Rock;
                        w.SetRaw(lx, y, lz, rock);
                    }
                    if (c.Water > 0) w.FillColumnRaw(lx, lz, Math.Max(top, peak), c.Water, Mat.Water);
                }
            }
        // what stands on it: the hexes whose things or trees may reach into the piece
        var hexes = new List<(int Col, int Row)>();
        lay.HexesIn(ox + Pad - HexMapLayout.R - 4, oz + Pad - HexMapLayout.R - 4, ox + Pad + HexMapLayout.Tile + HexMapLayout.R + 4, oz + Pad + HexMapLayout.Tile + HexMapLayout.R + 4, hexes);
        foreach (var (col, row) in hexes)
        {
            var l = src.Look(col, row);
            var (cx, cz) = lay.Center(col, row);
            canvas.Hex = (col, row);
            canvas.Src = src; canvas.Lay = lay;
            HexModels.Relief(canvas, l, cx, cz, col, row);
            if (l.Icon != HexIcon.None && l.Icon != HexIcon.Monster)
                HexModels.Thing(canvas, l, cx, cz, ColumnAt(src, lay, (int)cx, (int)cz).Top, Vary(col, row));
        }
        return w;
    }

    // ------------------------------------------------------------ a piece meshed

    sealed class Kit
    {
        public readonly GreedyMesher Mesher = new() { Pooled = true };
        public readonly byte[] Pad = new byte[34 * 34 * 34], Wet = new byte[34 * 34 * 34];
    }
    [ThreadStatic] static Kit _kit;

    /// <summary>
    /// A piece's world meshed: its chunks (not those of the margin) into one mesh of the ground and what stands on it,
    /// one of its water, their vertices moved to the map's own voxels so the shaders see the map's places.
    /// </summary>
    public static (MeshBuffers solid, MeshBuffers water) Mesh(VoxelWorld w, int tx, int tz)
    {
        var (s, wa, sh) = Mesh(w, tx, tz, shadow: false);
        return (s, wa);
    }

    /// <summary>A piece meshed as above, and its shadow: the same voxels as the fewest faces that hold their shape, every face of them one whatever it is of.</summary>
    public static (MeshBuffers solid, MeshBuffers water, MeshBuffers shadow) Mesh(VoxelWorld w, int tx, int tz, bool shadow)
    {
        var k = _kit ??= new Kit();
        int ox = tx * HexMapLayout.Tile - Pad, oz = tz * HexMapLayout.Tile - Pad;
        var solids = new List<MeshBuffers>();
        var waters = new List<MeshBuffers>();
        var shades = new List<MeshBuffers>();
        int n0 = Pad / VoxelWorld.CS, n1 = (Pad + HexMapLayout.Tile) / VoxelWorld.CS;
        for (int cy = 0; cy < w.CY; cy++)
            for (int cz = n0; cz < n1; cz++)
                for (int cx = n0; cx < n1; cx++)
                {
                    int ci = w.ChunkIndex(cx, cy, cz);
                    if (w.IsEmpty(ci)) continue;
                    MeshBuffers s, water, shade = null;
                    if (shadow) s = k.Mesher.MeshChunk(w, ci, k.Pad, k.Wet, out water, out _, out shade);
                    else s = k.Mesher.MeshChunk(w, ci, k.Pad, k.Wet, out water);
                    if (s != null && s.Quads > 0) solids.Add(s); else s?.Return();
                    if (water != null && water.Quads > 0) waters.Add(water); else water?.Return();
                    if (shade != null && shade.Quads > 0) shades.Add(shade); else shade?.Return();
                }
        return (Merge(solids, ox, oz), Merge(waters, ox, oz), Merge(shades, ox, oz));
    }

    /// <summary>Meshes joined into one, moved by so many voxels; the borrowed buffers given back.</summary>
    static MeshBuffers Merge(List<MeshBuffers> parts, int ox, int oz)
    {
        if (parts.Count == 0) return null;
        int quads = 0;
        bool col = parts[0].Col != null, uv = parts[0].Uv != null, uv2 = parts[0].Uv2 != null;
        foreach (var p in parts) { col &= p.Col != null; uv &= p.Uv != null; uv2 &= p.Uv2 != null; }
        foreach (var p in parts) quads += p.Quads;
        int verts = quads * 4;
        var m = new MeshBuffers
        {
            Pos = new float[verts * 3], Nrm = new float[verts * 3], Col = col ? new float[verts * 4] : null,
            Uv = uv ? new float[verts * 2] : null, Uv2 = uv2 ? new float[verts * 2] : null, Idx = new int[quads * 6], Quads = quads,
        };
        int v0 = 0, i0 = 0;
        foreach (var p in parts)
        {
            int v = p.Vertices;
            for (int i = 0; i < v; i++)
            {
                m.Pos[(v0 + i) * 3] = p.Pos[i * 3] + ox;
                m.Pos[(v0 + i) * 3 + 1] = p.Pos[i * 3 + 1];
                m.Pos[(v0 + i) * 3 + 2] = p.Pos[i * 3 + 2] + oz;
            }
            Array.Copy(p.Nrm, 0, m.Nrm, v0 * 3, v * 3);
            if (col) Array.Copy(p.Col, 0, m.Col, v0 * 4, v * 4);
            if (uv) Array.Copy(p.Uv, 0, m.Uv, v0 * 2, v * 2);
            if (uv2) Array.Copy(p.Uv2, 0, m.Uv2, v0 * 2, v * 2);
            int ni = p.Quads * 6;
            for (int i = 0; i < ni; i++) m.Idx[i0 + i] = p.Idx[i] + v0;
            v0 += v; i0 += ni;
            p.Return();
        }
        return m;
    }
}

/// <summary>
/// Where the world map's things and trees are put in voxels (HexModels.cs): a piece's world, written only within the
/// piece and the voxel round it, in the map's own voxels; the ground's height under any column of the map.
/// </summary>
public sealed class HexCanvas
{
    readonly VoxelWorld _w;
    readonly int _ox, _oz, _lo, _hi;
    public IHexSource Src;
    public HexMapLayout Lay;
    /// <summary>The hex being dressed.</summary>
    public (int Col, int Row) Hex;

    public HexCanvas(VoxelWorld w, int ox, int oz, int lo, int hi) { _w = w; _ox = ox; _oz = oz; _lo = lo; _hi = hi; }

    public void Put(int x, int y, int z, byte m)
    {
        int lx = x - _ox, lz = z - _oz;
        if (lx < _lo || lz < _lo || lx >= _hi || lz >= _hi || (uint)y >= HexTiles.SY) return;
        _w.SetRaw(lx, y, lz, m);
    }

    public byte Get(int x, int y, int z)
    {
        int lx = x - _ox, lz = z - _oz;
        if (lx < _lo || lz < _lo || lx >= _hi || lz >= _hi) return Mat.Air;
        return _w.Get(lx, y, lz);
    }

    /// <summary>The ground's top under a column (worked out from the hexes, whether or not it is in the piece).</summary>
    public int Ground(int x, int z) => HexTiles.ColumnAt(Src, Lay, x, z).Top;
}
