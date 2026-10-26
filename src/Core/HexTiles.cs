using System;
using System.Collections.Generic;
using System.Threading;

namespace VoxelTower.Core;

/// <summary>
/// The world map's land made in voxels a piece at a time (HexMapLayout.Tile square), from the looks of its hexes alone
/// (HexLook.cs), so any piece can be made again the same at any time and none is kept but those drawn. It is one land,
/// not a board of tiles: every hex has a level of its own, and the ground runs smoothly from one hex's level to the
/// next over a broad lean of both (a weighed mean of the levels of the hexes about each column, each hex weighing the
/// more the nearer its middle); low rolls of the ground lie over all of it, hills swell where the hexes are hilly. The
/// grounds meet raggedly, each column taking the ground of the hex nearest to it as a gentle wander of the land has it,
/// and each ground is no one colour but drifts of its shades: lush and dry grass, bare earth, a scatter of flowers on a
/// meadow, pebbles on rough land, ash and cinders and cracks of lava in the horde's realm. Mountains are massifs: where
/// mountain hexes lie the rock rises in ridges and summits, cliffs banded in its layers, scree at their feet, snow on
/// the heights, foothills running out into the land about it; in the horde's realm they are black rock glowing at the
/// top. Lakes and the sea lie where the hexes are water, their shores running as the land falls rather than along the
/// hexes' sides, sand or reeds at the edge and deeper toward the middle. Rivers wind from a hex's middle out over the
/// sides they cross, in a bed cut into the land with a green bank, lava in the horde's land; a road winds the same way,
/// a worn track of earth (cobbles by a town), a bridge of planks with rails where it crosses a river. A thing
/// (HexModels.cs) stands at its hex's middle on ground made level for it, and woods are planted by HexModels too.
/// A piece is a world of 192 × 96 × 192 voxels, the piece in the middle of it with a chunk of margin round it of which
/// only the voxels next to the piece are made, so the mesher sees the faces at its sides as they are.
/// </summary>
public static class HexTiles
{
    /// <summary>The ground's height of a hex of height 0; a step of height (as the looks count it); the top of a lake's water; the floor of a lake.</summary>
    public const int Base = 16, Step = 2, WaterTop = 15, LakeFloor = 11;
    /// <summary>A piece's world: the margin about the piece, its side, its height.</summary>
    public const int Pad = 32, Size = HexMapLayout.Tile + 2 * Pad, SY = 96;
    /// <summary>Half the width of a road and of a river's bed; how near the middle a thing's ground is made level.</summary>
    const float RoadHalf = 1.45f, RiverHalf = 2.1f, Level = 9.5f;
    /// <summary>How far a column's ground wanders from its own place when it takes a hex's ground; a river's, a road's wander.</summary>
    const float Wander = 4.5f, RiverWander = 2.4f, RoadWander = 1.4f;

    /// <summary>The look of a hex; off the field a ring of the mountains that frame it, and past them the sea it lies in.</summary>
    public static HexLook LookOf(IHexSource src, int col, int row)
    {
        int w = src.Width, h = src.Height;
        if ((uint)col < (uint)w && (uint)row < (uint)h) return src.Look(col, row);
        int off = Math.Max(Math.Max(-col, col - (w - 1)), Math.Max(-row, row - (h - 1)));
        return off <= 1 ? new HexLook { Ground = HexGround.Rough, Relief = HexRelief.Mountain, Height = 2 } : new HexLook { Ground = HexGround.DeepWater };
    }

    /// <summary>
    /// What makes one hex unlike the next of its kind (how many trees, how tall its peak, where its mounds), 0…255, from its
    /// place alone: the look's Variant says what became of its thing (bit 0 spent, bit 1 kept by a stack).
    /// </summary>
    public static int Vary(int col, int row) => (int)(Hash.H3(col, row, 977) * 256);

    /// <summary>A hex's thing spent (picked up, opened, its week's given, its treasure taken); kept by a stack.</summary>
    public static bool Spent(in HexLook l) => (l.Variant & 1) != 0;
    public static bool Kept(in HexLook l) => (l.Variant & 2) != 0;

    static bool Wet(HexGround g) => g is HexGround.Water or HexGround.DeepWater;
    static bool Burnt(HexGround g) => g is HexGround.Scorched or HexGround.Lava;

    /// <summary>
    /// A hex's own level, the height of the ground at its middle before the land's rolls: its height counted gently (the
    /// first steps two voxels each, then a voxel and a little), a lake's floor, a mountain's foot.
    /// </summary>
    public static float LevelOf(in HexLook l)
    {
        if (Wet(l.Ground)) return LakeFloor - (l.Ground == HexGround.DeepWater ? 2 : 0);
        int h = Math.Min((int)l.Height, 10);
        return Base + (h <= 3 ? h * Step : 3 * Step + (h - 3) * 1.1f);
    }

    /// <summary>The ground's top at a hex's middle, as a thing on it stands (whole voxels).</summary>
    public static int LevelAt(in HexLook l) => (int)MathF.Round(LevelOf(l));

    /// <summary>How tall a mountain's summit stands over the mountain's foot.</summary>
    static float PeakOf(int vary) => 30 + vary % 13;

    /// <summary>A smooth noise 0…1 over the map, its features so many voxels across (a lattice of hashed values, eased between).</summary>
    public static double Patch(int x, int z, int scale, int salt)
    {
        int xi = Math.DivRem(x, scale, out int xr), zi = Math.DivRem(z, scale, out int zr);
        if (xr < 0) { xr += scale; xi--; }
        if (zr < 0) { zr += scale; zi--; }
        double fx = xr / (double)scale, fz = zr / (double)scale;
        fx = fx * fx * (3 - 2 * fx); fz = fz * fz * (3 - 2 * fz);
        double a = Hash.H3(xi, zi, salt), b = Hash.H3(xi + 1, zi, salt), c = Hash.H3(xi, zi + 1, salt), d = Hash.H3(xi + 1, zi + 1, salt);
        return (a + (b - a) * fx) * (1 - fz) + (c + (d - c) * fx) * fz;
    }

    static float N(int x, int z, int scale, int salt) => (float)Patch(x, z, scale, salt);

    /// <summary>A ridge of the noise: 1 along the middle of its rises, falling away to either side.</summary>
    static float Ridge(int x, int z, int scale, int salt) => 1 - MathF.Abs(2 * N(x, z, scale, salt) - 1);

    static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
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

    // ------------------------------------------------------------ the hexes about a column, kept to hand

    /// <summary>
    /// The looks of the hexes about where a thread is working, kept so a column does not ask the source for its seven
    /// hexes every time: made again when the work moves off it, or when a hex anywhere changed (<see cref="Touch"/>).
    /// </summary>
    sealed class Window
    {
        public IHexSource Src;
        public HexMapLayout Lay;
        public int Gen, C0, R0, Side;
        public HexLook[] Looks = Array.Empty<HexLook>();
        public bool Has(int c, int r) => c >= C0 && r >= R0 && c < C0 + Side && r < R0 + Side;
        public HexLook Look(int c, int r) => Has(c, r) ? Looks[(r - R0) * Side + (c - C0)] : LookOf(Src, c, r);
    }

    [ThreadStatic] static Window _win;
    static int _gen;

    /// <summary>A hex of the map changed its look: every thread's hexes kept to hand are read again.</summary>
    public static void Touch() => Interlocked.Increment(ref _gen);

    static Window WindowAt(IHexSource src, HexMapLayout lay, int col, int row, int half = 4)
    {
        var w = _win ??= new Window();
        int gen = Volatile.Read(ref _gen);
        if (w.Src == src && w.Lay == lay && w.Gen == gen && w.Has(col - 2, row - 2) && w.Has(col + 2, row + 2)) return w;
        w.Src = src; w.Lay = lay; w.Gen = gen;
        w.Side = 2 * half + 1; w.C0 = col - half; w.R0 = row - half;
        if (w.Looks.Length != w.Side * w.Side) w.Looks = new HexLook[w.Side * w.Side];
        for (int r = 0; r < w.Side; r++)
            for (int c = 0; c < w.Side; c++) w.Looks[r * w.Side + c] = LookOf(src, w.C0 + c, w.R0 + r);
        return w;
    }

    // ------------------------------------------------------------ a column of the land

    /// <summary>
    /// What the ground of a column is: its top (the rock of a mountain with it), the level of water over it (0 none),
    /// the material of its top and of the earth under it, how much of its top is a mountain's rock, whether a bridge's
    /// deck lies on it (the bed under the water at <see cref="Bed"/>), a rail on the deck, and what grows or lies on it.
    /// </summary>
    public struct Column
    {
        public int Top, Water;
        public byte Mat, Under;
        public bool Bridge;
        /// <summary>The rise of a mountain's rock over the land under it, voxels (0 none); the land's top under it; the bed under a bridge.</summary>
        public int Mount, Land, Bed;
        /// <summary>The mountain's rock is the horde's black stone; it is snow from this height up (a mountain's).</summary>
        public bool Volcanic;
        public int SnowAt;
        /// <summary>A post of a bridge's rail on it; a flower, a pebble or a tuft on its top (0 nothing).</summary>
        public bool Rail;
        public byte Deco;
        /// <summary>How much of a wood the hexes about it make here, 0…1 (the far view roofs it over with crowns).</summary>
        public float Wood;
    }

    /// <summary>The ground at a column of the map, worked out from the hexes about it.</summary>
    public static Column ColumnAt(IHexSource src, HexMapLayout lay, int x, int z)
    {
        var (col, row) = lay.HexAt(x + .5f, z + .5f);
        return ColumnIn(WindowAt(src, lay, col, row), lay, x, z, col, row);
    }

    static Column ColumnIn(Window win, HexMapLayout lay, int x, int z, int col, int row)
    {
        float px = x + .5f, pz = z + .5f;
        var own = win.Look(col, row);
        var (ocx, ocz) = lay.Center(col, row);
        float dx = px - ocx, dz = pz - ocz;
        // the hexes about: the land's level a weighed mean of theirs, and how much of each kind of land is here
        const float W2 = HexMapLayout.W * HexMapLayout.W;
        float sw = 0, sh = 0, wet = 0, deep = 0, mtn = 0, wood = 0, hill = 0, peak = 0;
        bool volcanic = false;
        for (int k = -1; k < 6; k++)
        {
            int c = col, r = row;
            if (k >= 0) (c, r) = HexMapLayout.Neighbour(col, row, k);
            var l = k < 0 ? own : win.Look(c, r);
            var (cx, cz) = lay.Center(c, r);
            float d2 = (px - cx) * (px - cx) + (pz - cz) * (pz - cz);
            if (d2 >= W2) continue;
            float t = 1 - d2 / W2, wgt = t * t;
            sw += wgt;
            sh += wgt * LevelOf(l);
            if (Wet(l.Ground)) { wet += wgt; if (l.Ground == HexGround.DeepWater) deep += wgt; }
            if (l.Relief == HexRelief.Mountain)
            {
                mtn += wgt;
                if (Burnt(l.Ground)) volcanic = true;
                // its summit a little off its middle as its variant has it, and two lesser tops about it, so a range is
                // a crowd of peaks and not one cone to a hex
                int vm = Vary(c, r);
                for (int top = 0; top < 3; top++)
                {
                    float a = vm * .1f + top * 2.1f, off = top == 0 ? 2.5f : 8.5f;
                    float ox = MathF.Cos(a) * off, oz = MathF.Sin(a) * off;
                    float tall = PeakOf(vm) * (top == 0 ? 1 : .58f + .1f * top), reach = HexMapLayout.R * (top == 0 ? 1.3f : .85f);
                    float pd = MathF.Sqrt((px - cx - ox) * (px - cx - ox) + (pz - cz - oz) * (pz - cz - oz));
                    if (pd < reach) peak = MathF.Max(peak, tall * MathF.Pow(1 - pd / reach, 1.15f));
                }
            }
            else if (l.Relief == HexRelief.Forest) wood += wgt;
            else if (l.Relief == HexRelief.Hills) hill += wgt;
        }
        float inv = 1 / sw;
        float h = sh * inv;
        wet *= inv; deep *= inv; mtn *= inv; wood *= inv; hill *= inv;

        // the ground a column takes: its nearest hex's, as the land's gentle wander has it, so grounds meet raggedly
        float wx = (N(x, z, 13, 304) - .5f) * 2 * Wander, wz = (N(x, z, 13, 305) - .5f) * 2 * Wander;
        var (gc, gr) = lay.HexAt(px + wx, pz + wz);
        var g = (gc == col && gr == row ? own : win.Look(gc, gr)).Ground;
        if (Wet(g)) g = own.Ground;

        float edge = N(x, z, 6, 303), p1 = N(x, z, 17, 311), p2 = N(x, z, 6, 312);
        double hs = Hash.H3(x, z, 313);
        float r0 = MathF.Sqrt(dx * dx + dz * dz);
        bool thing = own.Icon != HexIcon.None && own.Icon != HexIcon.Monster && !Wet(own.Ground);
        float flat = 0;

        // roads and rivers, wandering a little about the straight way from the middle to a side
        float river = own.Rivers == 0 ? float.MaxValue
            : WayDist(own.Rivers, dx + (N(x, z, 9, 321) - .5f) * 2 * RiverWander, dz + (N(x, z, 9, 322) - .5f) * 2 * RiverWander, 1.02f);
        float road = own.Roads == 0 ? float.MaxValue
            : WayDist(own.Roads, dx + (N(x, z, 11, 323) - .5f) * 2 * RoadWander, dz + (N(x, z, 11, 324) - .5f) * 2 * RoadWander, 1.02f);
        flat = Math.Max(flat, 1 - Smooth(RoadHalf, RoadHalf + 3, road));
        flat = Math.Max(flat, 1 - Smooth(RiverHalf, RiverHalf + 3, river));
        if (thing) flat = Math.Max(flat, 1 - Smooth(Level - 1, Level + 3, r0));

        // the land's low rolls, and the hills' swells
        float roll = (N(x, z, 37, 301) - .5f) * 2f + (N(x, z, 13, 302) - .5f) * .5f;
        h += roll * (1 - flat * .8f) * (1 - Smooth(.2f, .5f, wet));
        if (hill > 0) h += hill * (1.5f + 5.5f * Ridge(x, z, 15, 306) * N(x, z, 8, 307)) * (1 - flat);
        // a thing stands on its hex's own level
        if (thing && r0 < Level) h = LevelOf(own) + (r0 > Level - 3 ? (h - LevelOf(own)) * (r0 - (Level - 3)) / 3 : 0);

        // the mountains: a massif of ridges and summits where their hexes are, running out in foothills
        int mount = 0, snowAt = 0;
        float shape = Smooth(.3f, .82f, mtn + (edge - .5f) * .26f);
        if (shape > 0)
        {
            float r1 = Ridge(x, z, 31, 308), r2 = Ridge(x, z, 12, 309);
            float massif = 10 + 20 * r1 * r1 + 7 * r2 * N(x, z, 7, 310);
            float m = shape * MathF.Max(massif * (.55f + .45f * mtn), peak * (.55f + .45f * shape));
            // the rock craggy: knobs and gullies down its faces
            m *= .86f + .28f * N(x, z, 5, 328);
            mount = (int)MathF.Round(m);
            snowAt = (int)MathF.Round(h + (g == HexGround.Snow ? 12 : 29) + p2 * 6);
        }

        var c0 = new Column { Volcanic = volcanic };
        // lakes and the sea: under the water where the hexes about are water enough, the shore running as the land falls
        float thr = .5f + (edge - .5f) * .16f;
        bool under = wet > thr && mount == 0;
        if (under)
        {
            float k = Smooth(thr, thr + .3f, wet);
            h = WaterTop - 1 - k * (deep > .3f ? 5.5f : 3.5f);
            c0.Water = WaterTop;
        }
        else
        {
            if (wet > thr - .17f) h += (WaterTop + .7f - h) * Smooth(thr - .17f, thr, wet);
            h = MathF.Max(h, WaterTop + .6f);
        }
        int land = Math.Min(SY - 30, (int)MathF.Round(h));
        c0.Land = land;
        c0.Top = land;
        c0.Under = Burnt(g) ? Mat.Basalt : g == HexGround.Sand ? Mat.Sand : Mat.Soil;

        // the ground's top: drifts of its shades
        float woody = wood + (N(x, z, 9, 326) - .5f) * .5f + (p2 - .5f) * .15f;
        bool woodFloor = woody > .5f && g is not (HexGround.Snow or HexGround.Sand or HexGround.Scorched or HexGround.Lava);
        c0.Mat = g switch
        {
            HexGround.Grass => woodFloor ? (p1 < .4f ? Mat.Soil : p1 < .62f ? Mat.Moss : Mat.GrassLush)
                : p1 < .26f ? Mat.GrassLush : p1 > .74f ? Mat.GrassDry : p2 > .91f ? Mat.Soil : Mat.Grass,
            HexGround.Dirt => woodFloor ? (p1 < .5f ? Mat.Soil : Mat.Moss) : p1 < .42f ? Mat.GrassDry : p1 < .7f ? Mat.Trodden : p2 > .4f ? Mat.Grass : Mat.Soil,
            HexGround.Sand => p2 > .86f ? Mat.Gravel : p1 > .82f ? Mat.GrassDry : Mat.Sand,
            HexGround.Snow => p2 > .86f ? Mat.Rock : woodFloor && p1 < .3f ? Mat.Soil : Mat.Snow,
            HexGround.Swamp => p1 < .38f ? Mat.Moss : p1 < .66f ? Mat.GrassLush : Mat.Reedbed,
            HexGround.Rough => p2 > .76f ? Mat.Rock : p1 < .3f ? Mat.Soil : p1 > .72f ? Mat.GrassDry : Mat.Gravel,
            HexGround.Lava => N(x, z, 23, 327) > .86f && p2 > .55f ? Mat.Lava : p2 > .7f ? Mat.Ash : Mat.Basalt,
            HexGround.Scorched => p1 < .22f ? Mat.Basalt : p1 > .74f ? Mat.Rock : p2 > .9f ? Mat.Char : Mat.Ash,
            _ => p1 < .5f ? Mat.Sand : Mat.Soil,
        };
        c0.Wood = woody;
        // what lies on it: a meadow's flowers in drifts, pebbles on rough land, tufts on sand, cinders on ash
        if (!woodFloor && !thing)
        {
            if (g is HexGround.Grass or HexGround.Dirt && N(x, z, 15, 314) > .72f && hs < .009)
            {
                float hue = N(x, z, 23, 315);
                c0.Deco = hue < .55f ? Mat.BloomYellow : hue < .7f ? Mat.BloomWhite : hue < .88f ? Mat.BloomBlue : Mat.BloomRed;
            }
            else if (g is HexGround.Rough or HexGround.Dirt && hs > .994) c0.Deco = Mat.Gravel;
            else if (g == HexGround.Sand && hs > .985) c0.Deco = Mat.Tuft;
        }
        // the horde's land: thin cracks of lava running over it
        if (Burnt(g) && !under)
        {
            float cr = MathF.Abs(N(x, z, 21, 316) - .5f);
            if (cr < (g == HexGround.Lava ? .02f : .011f) && N(x, z, 37, 317) > .45f) c0.Mat = Mat.Lava;
        }
        if (under) c0.Mat = h > WaterTop - 2.5f ? (g == HexGround.Swamp ? Mat.Soil : Mat.Sand) : h > WaterTop - 4 ? Mat.Gravel : Mat.Soil;
        else if (wet > thr - .09f && c0.Top <= WaterTop + 1) c0.Mat = g == HexGround.Swamp || p1 < .3f ? Mat.Reedbed : p2 > .6f ? Mat.Gravel : Mat.Sand;
        // a swamp's pools among its moss
        if (!under && g == HexGround.Swamp && !thing && mount == 0)
        {
            float pool = N(x, z, 7, 318) * .7f + N(x, z, 3, 319) * .3f;
            if (pool > .62f && r0 > 3) { c0.Water = c0.Top; c0.Top -= 1; c0.Mat = Mat.Soil; c0.Deco = 0; }
            else if (pool > .57f) { c0.Mat = Mat.Reedbed; c0.Deco = 0; }
        }

        // rivers and roads over the land
        if (!under && mount <= 1)
        {
            float bend = RiverHalf + (N(x, z, 8, 325) - .5f) * .7f;
            bool lava = Burnt(own.Ground);
            if (river < bend)
            {
                int surface = land - 1;
                if (road < RoadHalf + .5f)
                {
                    // a bridge: a deck of planks over the water, rails at its sides
                    c0.Bridge = true;
                    c0.Bed = surface - 1;
                    c0.Water = lava ? 0 : surface;
                    c0.Top = surface + 1;
                    c0.Mat = Mat.Plank;
                    c0.Rail = road > RoadHalf - .35f && ((x + z) & 1) == 0;
                }
                else if (lava) { c0.Top = surface; c0.Mat = Mat.Lava; }
                else
                {
                    c0.Water = surface;
                    c0.Top = surface - (river < bend - .9f ? 2 : 1);
                    c0.Mat = river < bend - .9f ? Mat.Gravel : Mat.Sand;
                }
                c0.Deco = 0;
            }
            else if (road < RoadHalf + (edge - .5f) * .7f && c0.Water == 0)
            {
                bool town = own.Icon is HexIcon.Citadel or HexIcon.Outpost or HexIcon.HordeTown;
                c0.Mat = town ? Mat.Cobble : g == HexGround.Snow ? Mat.Gravel : road < .8f && hs < .7 ? Mat.Dust : Mat.Trodden;
                if (hs > .93) c0.Mat = Mat.Gravel;
                c0.Deco = 0;
            }
            else if (river < bend + 1.5f && g is HexGround.Grass or HexGround.Dirt or HexGround.Swamp) { c0.Mat = p2 > .7f ? Mat.Reedbed : Mat.GrassLush; c0.Deco = 0; }
        }
        // the earth trodden about the horde's towns, the citadel's yard
        if (thing && r0 < Level - 2 && c0.Water == 0 && !c0.Bridge)
        {
            if (own.Icon is HexIcon.HordeTown or HexIcon.Lair) c0.Mat = p2 < .5f ? Mat.Ash : Mat.Trodden;
            else if (own.Icon == HexIcon.Citadel) c0.Mat = Mat.Trodden;
            c0.Deco = 0;
        }

        // the rock over it all
        if (mount > 0)
        {
            c0.Mount = mount;
            c0.SnowAt = snowAt;
            c0.Top = Math.Min(SY - 4, c0.Land + mount);
            if (c0.Water > 0 && c0.Water <= c0.Top) c0.Water = 0;
            c0.Deco = 0;
            c0.Bridge = false;
        }
        return c0;
    }

    /// <summary>The height one stands at on a column of the map: the ground's top (a mountain's with it), or the water over it.</summary>
    public static int StandAt(IHexSource src, HexMapLayout lay, int x, int z)
    {
        var c = ColumnAt(src, lay, x, z);
        return Math.Max(c.Top, c.Water);
    }

    /// <summary>How high the mountains about a column rise over the land under them.</summary>
    public static float MountainAt(IHexSource src, HexMapLayout lay, int x, int z) => ColumnAt(src, lay, x, z).Mount;

    // ------------------------------------------------------------ a piece made

    /// <summary>The rock of a mountain at a height: bands of grey and darker stone, wandering a little.</summary>
    static byte Band(int x, int y, int z, bool volcanic)
    {
        if (volcanic) return Hash.H3(x >> 1, y, z >> 1) < .12 ? Mat.Char : Mat.Basalt;
        int b = (y + (int)(Patch(x, z, 19, 331) * 5)) / 3 % 3;
        return b == 0 ? Mat.StoneDark : b == 1 ? Mat.Rock : Hash.H3(x >> 2, y, z >> 2) < .5 ? Mat.Rock : Mat.Gravel;
    }

    /// <summary>A piece of the map made into its world: the ground of every column in it and the one about it, the rock and its snow, the woods, the things.</summary>
    public static VoxelWorld Build(IHexSource src, HexMapLayout lay, int tx, int tz)
    {
        var w = new VoxelWorld(Size, SY, Size);
        int ox = tx * HexMapLayout.Tile - Pad, oz = tz * HexMapLayout.Tile - Pad;
        var canvas = new HexCanvas(w, ox, oz, Pad - 1, Pad + HexMapLayout.Tile + 1);
        // the hexes of the piece and about it kept to hand
        var (mc, mr) = lay.HexAt(ox + Size / 2f, oz + Size / 2f);
        var win = WindowAt(src, lay, mc, mr, 6);
        // the columns worked out first, so each knows how steep it stands over its neighbours
        int lo = Pad - 2, hi = Pad + HexMapLayout.Tile + 2, n = hi - lo;
        var cols = new Column[n * n];
        var has = new bool[n * n];
        for (int lz = lo; lz < hi; lz++)
            for (int lx = lo; lx < hi; lx++)
            {
                int x = ox + lx, z = oz + lz;
                if (x < 0 || z < 0 || x >= lay.SX || z >= lay.SZ) continue;
                var (hc, hr) = lay.HexAt(x + .5f, z + .5f);
                if (!win.Has(hc - 1, hr - 1) || !win.Has(hc + 1, hr + 1)) win = WindowAt(src, lay, hc, hr, 6);
                cols[(lz - lo) * n + (lx - lo)] = ColumnIn(win, lay, x, z, hc, hr);
                has[(lz - lo) * n + (lx - lo)] = true;
            }
        int TopAt(int lx, int lz, int or)
        {
            int i = (lz - lo) * n + (lx - lo);
            return lx < lo || lz < lo || lx >= hi || lz >= hi || !has[i] ? or : cols[i].Top;
        }
        for (int lz = Pad - 1; lz < Pad + HexMapLayout.Tile + 1; lz++)
            for (int lx = Pad - 1; lx < Pad + HexMapLayout.Tile + 1; lx++)
            {
                int i = (lz - lo) * n + (lx - lo);
                if (!has[i]) continue;
                var c = cols[i];
                int x = ox + lx, z = oz + lz, top = c.Top;
                int steep = Math.Max(Math.Max(Math.Abs(top - TopAt(lx - 1, lz, top)), Math.Abs(top - TopAt(lx + 1, lz, top))),
                    Math.Max(Math.Abs(top - TopAt(lx, lz - 1, top)), Math.Abs(top - TopAt(lx, lz + 1, top))));
                if (c.Bridge)
                {
                    // the bed, the water over it, the deck over that
                    w.FillColumnRaw(lx, lz, 0, Math.Max(1, c.Bed), Mat.Soil);
                    if (c.Water > c.Bed) w.FillColumnRaw(lx, lz, c.Bed, c.Water, Mat.Water);
                    else if (c.Bed > 0) w.FillColumnRaw(lx, lz, c.Bed, top - 1, Mat.Lava);
                    w.SetRaw(lx, top - 1, lz, Mat.Plank);
                    if (c.Rail) w.SetRaw(lx, top, lz, Mat.Wood);
                    continue;
                }
                int land = c.Mount > 0 ? c.Land : top;
                w.FillColumnRaw(lx, lz, 0, Math.Max(1, land - 4), Mat.Rock);
                if (land - 1 > 0) w.FillColumnRaw(lx, lz, Math.Max(1, land - 4), land - 1, c.Under);
                if (c.Mount > 0)
                {
                    // the mountain's rock in its bands; its top as the slope has it: a cliff bare, a ledge under scree or
                    // snow, a foothill grassed
                    for (int y = Math.Max(1, land - 1); y < top - 1; y++) w.SetRaw(lx, y, lz, Band(x, y, z, c.Volcanic));
                    byte cap;
                    if (c.Volcanic) cap = steep >= 3 ? Band(x, top - 1, z, true) : c.Mount > 26 && Hash.H3(x >> 1, top, z >> 1) < .35 ? Mat.Lava : Mat.Ash;
                    else if (top >= c.SnowAt && steep <= 4) cap = Mat.Snow;
                    else if (steep >= 3) cap = Band(x, top - 1, z, false);
                    else if (c.Mount <= 4) cap = c.Mat is Mat.Grass or Mat.GrassLush or Mat.GrassDry or Mat.Moss or Mat.Snow or Mat.Sand ? (c.Mat == Mat.Sand ? Mat.Gravel : c.Mat) : Mat.GrassDry;
                    else cap = Hash.H3(x, top, z) < .55 ? Mat.Gravel : Mat.Rock;
                    w.SetRaw(lx, top - 1, lz, cap);
                    // the snow lies a little down a cliff's face too
                    if (cap == Mat.Snow && top - 2 > land) w.SetRaw(lx, top - 2, lz, Mat.Snow);
                }
                else if (top > 0)
                {
                    // a bank too steep for turf shows its earth (the mesher grows grass down a bank's side)
                    w.SetRaw(lx, top - 1, lz, c.Mat);
                    if (c.Deco != 0 && c.Water == 0 && top < SY - 1) w.SetRaw(lx, top, lz, c.Deco);
                }
                if (c.Water > top) w.FillColumnRaw(lx, lz, top, c.Water, Mat.Water);
            }
        // what stands on it: the hexes whose things or trees may reach into the piece
        var hexes = new List<(int Col, int Row)>();
        lay.HexesIn(ox + Pad - HexMapLayout.R - 4, oz + Pad - HexMapLayout.R - 4, ox + Pad + HexMapLayout.Tile + HexMapLayout.R + 4, oz + Pad + HexMapLayout.Tile + HexMapLayout.R + 4, hexes);
        canvas.Src = src; canvas.Lay = lay;
        foreach (var (col, row) in hexes)
        {
            var l = src.Look(col, row);
            var (cx, cz) = lay.Center(col, row);
            canvas.Hex = (col, row);
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
