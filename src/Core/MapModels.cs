using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// What stands on the big world map, each made once in voxels finer than the map's own and kept: a castle three of its
/// voxels to one of the land's, a tree two. A model is drawn in model units, one unit a voxel of the land at the size
/// the map was first laid out at (<see cref="HexMapLayout.R"/> 18): a hex 31 units across its flats, a hero in the
/// saddle some 24 tall. Its middle stands at (0, 0, 0) on the ground, its front to the south, toward the eye. Kept as
/// the mesher made it (MapModels.Get), it is copied into the pieces of the map where it stands (MapDress.cs), turned a
/// quarter at a time so its voxels stay square to the land's.
/// </summary>
public enum MapModel : byte
{
    // what grows, two voxels to a unit
    Spruce, Pine, Oak, Birch, DeadTree, Charred, SnowFir, Palm, Willow, FruitTree, Bush, Scrub, Rock, MossRock, Boulder,
    Flowers, Reeds, Lily, Stump, SnowRock,
    // the farms about our castle, three to a unit
    Field, Haystack, Farmhouse, Sheep, Scarecrow,
    // the things of the map
    Citadel, Outpost, OutpostPlace, HordeTown, Lair, Sawmill, Quarry, OreMine, GoldMine, Alchemist, SulfurDune, CrystalCave,
    GemPond, Pile, Chest, Campfire, Windmill, WaterMill, Shrine, Dwelling, DeadMine, Ruins, Den, Trial, Signpost, Learning,
    Stables, Observatory, Pass, Totem, BoneHeap,
    // what turns, drawn on its own
    Sails, Wheel,
}

/// <summary>
/// A small world a model is made in: so many of its voxels to a model unit (<see cref="K"/>), the model's middle at
/// the middle of its floor, room under the ground for a footing. Shapes are given in model units and take every voxel
/// whose middle they hold, so the same model made coarser (one voxel to a unit, for far off and for its shadow) keeps
/// its shape and loses only its fine work.
/// </summary>
public sealed class ModelCanvas
{
    public readonly VoxelWorld W;
    public readonly int K;
    readonly int _ox, _oy, _oz;
    /// <summary>A number of the model's own, that its stones and leaves fall the same each time it is made.</summary>
    public int Seed;

    public ModelCanvas(int k, float halfX, float halfZ, float up, float down = 4)
    {
        K = k;
        int sx = Round(2 * (int)MathF.Ceiling(halfX * k) + 4), sz = Round(2 * (int)MathF.Ceiling(halfZ * k) + 4);
        int under = (int)MathF.Ceiling(down * k) + 2;
        int sy = Round(under + (int)MathF.Ceiling(up * k) + 4);
        W = new VoxelWorld(sx, sy, sz);
        _ox = sx / 2; _oz = sz / 2; _oy = under;
    }

    static int Round(int n) => (n + VoxelWorld.CS - 1) / VoxelWorld.CS * VoxelWorld.CS;

    /// <summary>Where the model's middle lies in its world, voxels.</summary>
    public (int X, int Y, int Z) Origin => (_ox, _oy, _oz);

    /// <summary>The first and the last voxel (relative to the middle) whose middles lie in [a, b) units.</summary>
    (int, int) Span(float a, float b)
    {
        int i0 = (int)MathF.Ceiling(a * K - .5f), i1 = (int)MathF.Ceiling(b * K - .5f) - 1;
        // what is thinner than a voxel is a voxel thick, not nothing: a pole stays a pole made coarse
        if (i1 < i0 && b > a) i0 = i1 = (int)MathF.Floor((a + b) / 2 * K);
        return (i0, i1);
    }

    /// <summary>A voxel by its place relative to the middle, in voxels.</summary>
    public void Set(int i, int j, int l, byte m)
    {
        int x = i + _ox, y = j + _oy, z = l + _oz;
        if ((uint)x < (uint)W.SX && (uint)y < (uint)W.SY && (uint)z < (uint)W.SZ) W.SetRaw(x, y, z, m);
    }

    public byte Get(int i, int j, int l)
    {
        int x = i + _ox, y = j + _oy, z = l + _oz;
        return (uint)x < (uint)W.SX && (uint)y < (uint)W.SY && (uint)z < (uint)W.SZ ? W.Get(x, y, z) : Mat.Air;
    }

    /// <summary>The middle of a voxel, units.</summary>
    public float At(int i) => (i + .5f) / K;

    /// <summary>A hash of a voxel's place and the model's number, 0…1.</summary>
    public double H(int i, int j, int l, int salt = 0) => Hash.H3(i * 31 + Seed, j * 17 + salt, l * 13 - Seed);

    /// <summary>
    /// The same of the cell of a model unit a voxel lies in: stones of two shades, leaves of two greens are mixed a unit
    /// at a time, as blocks and tufts, not voxel by voxel — a speckle the eye cannot see at the map's distance and the
    /// mesher cannot join.
    /// </summary>
    public double HC(int i, int j, int l, int salt = 0) => H(Div(i), Div(j), Div(l), salt);

    int Div(int a) => a >= 0 ? a / K : (a - K + 1) / K;

    public void Box(float x0, float y0, float z0, float x1, float y1, float z1, byte m)
    {
        var (i0, i1) = Span(x0, x1); var (j0, j1) = Span(y0, y1); var (l0, l1) = Span(z0, z1);
        for (int j = j0; j <= j1; j++)
            for (int l = l0; l <= l1; l++)
                for (int i = i0; i <= i1; i++) Set(i, j, l, m);
    }

    /// <summary>A box of air cut out of what is there.</summary>
    public void Cut(float x0, float y0, float z0, float x1, float y1, float z1) => Box(x0, y0, z0, x1, y1, z1, Mat.Air);

    /// <summary>The voxels of a box that are not air painted another material.</summary>
    public void Paint(float x0, float y0, float z0, float x1, float y1, float z1, byte m, double share = 1, int salt = 0)
    {
        var (i0, i1) = Span(x0, x1); var (j0, j1) = Span(y0, y1); var (l0, l1) = Span(z0, z1);
        for (int j = j0; j <= j1; j++)
            for (int l = l0; l <= l1; l++)
                for (int i = i0; i <= i1; i++)
                    if (Get(i, j, l) != Mat.Air && (share >= 1 || HC(i, j, l, salt) < share)) Set(i, j, l, m);
    }

    /// <summary>An upright cylinder, hollow inside when <paramref name="inner"/> is given (a material, or air).</summary>
    public void Cyl(float cx, float cz, float r, float y0, float y1, byte m, float wall = 0, byte inner = Mat.Air)
    {
        var (i0, i1) = Span(cx - r, cx + r); var (l0, l1) = Span(cz - r, cz + r); var (j0, j1) = Span(y0, y1);
        for (int l = l0; l <= l1; l++)
            for (int i = i0; i <= i1; i++)
            {
                float dx = At(i) - cx, dz = At(l) - cz, d = MathF.Sqrt(dx * dx + dz * dz);
                if (d > r) continue;
                bool core = wall > 0 && d < r - wall;
                if (core && inner == Mat.Air) continue;
                for (int j = j0; j <= j1; j++) Set(i, j, l, core ? inner : m);
            }
    }

    /// <summary>An upright cone (or a cone cut short, to <paramref name="rTop"/>), rising from y0 so high.</summary>
    public void Cone(float cx, float cz, float r, float y0, float h, byte m, float rTop = 0, byte m2 = 0, double share2 = 0)
    {
        var (i0, i1) = Span(cx - r, cx + r); var (l0, l1) = Span(cz - r, cz + r); var (j0, j1) = Span(y0, y0 + h);
        for (int j = j0; j <= j1; j++)
        {
            float t = Math.Clamp((At(j) - y0) / h, 0, 1), rr = r + (rTop - r) * t;
            for (int l = l0; l <= l1; l++)
                for (int i = i0; i <= i1; i++)
                {
                    float dx = At(i) - cx, dz = At(l) - cz;
                    if (dx * dx + dz * dz > rr * rr) continue;
                    Set(i, j, l, m2 != 0 && HC(i, j, l, 3) < share2 ? m2 : m);
                }
        }
    }

    /// <summary>A square pyramid, a spire over a square tower: half its side at the foot, so high.</summary>
    public void Pyramid(float cx, float cz, float half, float y0, float h, byte m)
    {
        var (j0, j1) = Span(y0, y0 + h);
        for (int j = j0; j <= j1; j++)
        {
            float t = Math.Clamp((At(j) - y0) / h, 0, 1), hw = half * (1 - t) + .2f / K;
            var (i0, i1) = Span(cx - hw, cx + hw); var (l0, l1) = Span(cz - hw, cz + hw);
            for (int l = l0; l <= l1; l++)
                for (int i = i0; i <= i1; i++) Set(i, j, l, m);
        }
    }

    /// <summary>An egg of a shape, its skin a little rough (<paramref name="rough"/> of its size), of one material or two.</summary>
    public void Ball(float cx, float cy, float cz, float rx, float ry, float rz, byte m, byte m2 = 0, double share2 = 0, float rough = .12f)
    {
        var (i0, i1) = Span(cx - rx, cx + rx); var (j0, j1) = Span(cy - ry, cy + ry); var (l0, l1) = Span(cz - rz, cz + rz);
        for (int j = j0; j <= j1; j++)
            for (int l = l0; l <= l1; l++)
                for (int i = i0; i <= i1; i++)
                {
                    float dx = (At(i) - cx) / rx, dy = (At(j) - cy) / ry, dz = (At(l) - cz) / rz;
                    float d = dx * dx + dy * dy + dz * dz;
                    if (d > 1 + (HC(i, j, l, 7) - .5) * rough * 2) continue;
                    Set(i, j, l, m2 != 0 && HC(i, j, l, 11) < share2 ? m2 : m);
                }
    }

    /// <summary>
    /// A lump of rock: an egg of a shape as <see cref="Ball"/>, but taken a model unit at a time, every cell of it in or
    /// out whole and of one stone — a crag of blocks, as the land's own rock is, and few faces however fine its model.
    /// </summary>
    public void Lump(float cx, float cy, float cz, float rx, float ry, float rz, byte m, byte m2 = 0, double share2 = 0, float rough = .25f)
    {
        int ci0 = (int)MathF.Floor(cx - rx), ci1 = (int)MathF.Ceiling(cx + rx), cj0 = (int)MathF.Floor(cy - ry), cj1 = (int)MathF.Ceiling(cy + ry);
        int cl0 = (int)MathF.Floor(cz - rz), cl1 = (int)MathF.Ceiling(cz + rz);
        for (int cj = cj0; cj <= cj1; cj++)
            for (int cl = cl0; cl <= cl1; cl++)
                for (int ci = ci0; ci <= ci1; ci++)
                {
                    float dx = (ci + .5f - cx) / rx, dy = (cj + .5f - cy) / ry, dz = (cl + .5f - cz) / rz;
                    if (dx * dx + dy * dy + dz * dz > 1 + (H(ci, cj, cl, 7) - .5) * rough * 2) continue;
                    byte mm = m2 != 0 && H(ci, cj, cl, 11) < share2 ? m2 : m;
                    for (int j = cj * K; j < (cj + 1) * K; j++)
                        for (int l = cl * K; l < (cl + 1) * K; l++)
                            for (int i = ci * K; i < (ci + 1) * K; i++) Set(i, j, l, mm);
                }
    }

    /// <summary>A round rod from one point to another, so thick.</summary>
    public void Rod(float ax, float ay, float az, float bx, float by, float bz, float r, byte m)
    {
        float vx = bx - ax, vy = by - ay, vz = bz - az, len2 = vx * vx + vy * vy + vz * vz;
        var (i0, i1) = Span(MathF.Min(ax, bx) - r, MathF.Max(ax, bx) + r);
        var (j0, j1) = Span(MathF.Min(ay, by) - r, MathF.Max(ay, by) + r);
        var (l0, l1) = Span(MathF.Min(az, bz) - r, MathF.Max(az, bz) + r);
        float rr = MathF.Max(r, .5f / K);
        for (int j = j0; j <= j1; j++)
            for (int l = l0; l <= l1; l++)
                for (int i = i0; i <= i1; i++)
                {
                    float px = At(i) - ax, py = At(j) - ay, pz = At(l) - az;
                    float t = len2 > 0 ? Math.Clamp((px * vx + py * vy + pz * vz) / len2, 0, 1) : 0;
                    float ex = px - vx * t, ey = py - vy * t, ez = pz - vz * t;
                    if (ex * ex + ey * ey + ez * ez <= rr * rr) Set(i, j, l, m);
                }
    }

    /// <summary>
    /// A gabled roof over a box from (x0, z0) to (x1, z1) whose eaves are at y, its ridge running west to east (or north to
    /// south), rising <paramref name="pitch"/> for each unit in; it hangs over the walls by <paramref name="over"/>.
    /// </summary>
    public void Gable(float x0, float z0, float x1, float z1, float y, byte m, bool alongX = true, float pitch = 1, float over = .5f, byte gableEnd = 0)
    {
        float wx0 = x0, wx1 = x1, wz0 = z0, wz1 = z1;
        x0 -= over; x1 += over; z0 -= over; z1 += over;
        float half = alongX ? (z1 - z0) / 2 : (x1 - x0) / 2, mid = alongX ? (z0 + z1) / 2 : (x0 + x1) / 2;
        var (i0, i1) = Span(x0, x1); var (l0, l1) = Span(z0, z1); var (j0, j1) = Span(y - .4f, y + half * pitch + 1);
        for (int l = l0; l <= l1; l++)
            for (int i = i0; i <= i1; i++)
            {
                float across = alongX ? MathF.Abs(At(l) - mid) : MathF.Abs(At(i) - mid);
                float top = y + (half - across) * pitch;
                // under the slope a skin of the roof's stuff a unit deep; within the walls the gables filled with theirs
                bool inWall = gableEnd != 0 && At(i) > wx0 && At(i) < wx1 && At(l) > wz0 && At(l) < wz1;
                for (int j = j0; j <= j1; j++)
                {
                    float yy = At(j);
                    if (yy > top) break;
                    if (yy >= top - 1.1f) Set(i, j, l, m);
                    else if (inWall && yy >= y) Set(i, j, l, gableEnd);
                }
            }
    }

    /// <summary>A roof hipped all round, its ridge west to east, the slopes <paramref name="pitch"/>.</summary>
    public void Hip(float x0, float z0, float x1, float z1, float y, byte m, float pitch = .8f, float over = .5f)
    {
        x0 -= over; x1 += over; z0 -= over; z1 += over;
        float hx = (x1 - x0) / 2, hz = (z1 - z0) / 2, cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
        var (i0, i1) = Span(x0, x1); var (l0, l1) = Span(z0, z1); var (j0, j1) = Span(y, y + MathF.Min(hx, hz) * pitch + 1);
        for (int l = l0; l <= l1; l++)
            for (int i = i0; i <= i1; i++)
            {
                float inx = hx - MathF.Abs(At(i) - cx), inz = hz - MathF.Abs(At(l) - cz);
                float top = y + MathF.Min(inx, inz) * pitch;
                for (int j = j0; j <= j1; j++) { float yy = At(j); if (yy > top) break; if (yy < top - 1.1f && inx > over && inz > over) continue; Set(i, j, l, m); }
            }
    }

    /// <summary>A flag on its pole, its cloth to the west: blue ours, red the horde's, white nobody's; a gilt knob on top.</summary>
    public void Flag(float x, float y, float z, byte owner, float pole = 7, float cloth = 3.2f)
    {
        Box(x - .17f, y, z - .17f, x + .17f, y + pole, z + .17f, Mat.Wood);
        byte c = owner == 1 ? MapModels.FlagBlue : owner == 2 ? MapModels.FlagRed : Mat.ClothWhite;
        float t = y + pole - .2f;
        for (float k = 0; k < cloth; k += 1f / K)
        {
            // the cloth narrows to a swallow's tail at its end
            float drop = 1.9f - (k > cloth * .7f ? (k - cloth * .7f) * .9f : 0);
            Box(x - .17f - k - 1f / K, t - drop, z - .1f, x - .17f - k, t, z + .1f, c);
        }
        Box(x - .17f - cloth, t - .35f, z - .12f, x - .17f, t - .1f, z + .12f, Mat.BannerTrim);
        Ball(x, y + pole + .2f, z, .3f, .3f, .3f, Mat.Brass, rough: 0);
    }
}

/// <summary>The models of the big world map made, kept and handed out (see <see cref="MapModel"/>).</summary>
public static partial class MapModels
{
    /// <summary>The fine voxels to a model unit of a thing; of what grows.</summary>
    public const int ThingK = 3, GrowK = 2;
    /// <summary>The map's own flags, cloth that stirs in the wind: blue ours, red the horde's; crystals red as blood.</summary>
    public const byte FlagBlue = Mat.MapFlagBlue, FlagRed = Mat.MapFlagRed, CrystalRed = Mat.CrystalRed;

    /// <summary>A model as the mesher made it: its faces (positions in its own voxels about its middle), its shadow.</summary>
    public sealed class Made
    {
        public MeshBuffers Solid, Shadow;
        public int K;
    }

    static readonly ConcurrentDictionary<long, Made> _kept = new();

    static long Key(MapModel m, int variant, byte owner, bool spent, int k) => ((((long)m * 256 + variant) * 4 + owner) * 2 + (spent ? 1 : 0)) * 8 + k;

    /// <summary>The fine voxels to a unit a model is made at: things three, what grows two (coarse: one).</summary>
    public static int KOf(MapModel m, bool coarse) => coarse ? 1 : Grows(m) ? GrowK : ThingK;

    public static bool Grows(MapModel m) => m <= MapModel.SnowRock;

    /// <summary>
    /// How much larger than drawn a model stands on the map: the things as large as in «Heroes», a castle over two hexes
    /// high, a mine filling its hex and more, a chest to be seen; trees a third larger than life against the hexes.
    /// </summary>
    public static float Size(MapModel m) => m switch
    {
        MapModel.Pile => 2.3f,
        MapModel.Chest or MapModel.Campfire or MapModel.Signpost or MapModel.Learning or MapModel.OutpostPlace => 1.9f,
        MapModel.Citadel => 1.45f,
        MapModel.HordeTown or MapModel.Pass or MapModel.Stables => 1.35f,
        MapModel.Field or MapModel.Haystack or MapModel.Farmhouse or MapModel.Sheep or MapModel.Scarecrow => 1.3f,
        MapModel.Totem or MapModel.BoneHeap => 1.4f,
        MapModel.Sails or MapModel.Wheel or MapModel.Windmill or MapModel.WaterMill => 1.55f,
        MapModel.Flowers or MapModel.Lily => 1.2f,
        _ when Grows(m) => 1.35f,
        _ => 1.5f,
    };

    /// <summary>A model made (once, then kept): its kind, which of its looks, whose, spent or not, so fine.</summary>
    public static Made Get(MapModel m, int variant, byte owner, bool spent, int k) =>
        _kept.GetOrAdd(Key(m, variant, owner, spent, k), _ => Make(m, variant, owner, spent, k));

    /// <summary>How many models are kept made (the bench reads it).</summary>
    public static int KeptCount => _kept.Count;

    static Made Make(MapModel m, int variant, byte owner, bool spent, int k)
    {
        var c = Draw(m, variant, owner, spent, k);
        var made = Mesh(c);
        made.K = k;
        return made;
    }

    /// <summary>A model drawn on a canvas of its size.</summary>
    public static ModelCanvas Draw(MapModel m, int variant, byte owner, bool spent, int k)
    {
        var (hx, hz, up) = Bounds(m);
        var c = new ModelCanvas(k, hx, hz, up) { Seed = (int)m * 977 + variant * 131 };
        if (Grows(m)) Grow(c, m, variant);
        else Thing(c, m, variant, owner, spent);
        return c;
    }

    /// <summary>How far a model reaches from its middle each way and how high it stands, units.</summary>
    static (float, float, float) Bounds(MapModel m) => m switch
    {
        MapModel.Citadel => (17, 15, 38),
        MapModel.HordeTown => (15, 15, 22),
        MapModel.Lair or MapModel.OreMine or MapModel.GoldMine or MapModel.CrystalCave or MapModel.DeadMine or MapModel.Den => (13, 12, 22),
        MapModel.Pass => (13, 8, 22),
        MapModel.Observatory or MapModel.Outpost => (11, 11, 30),
        MapModel.Windmill or MapModel.Sails => (9, 9, 26),
        MapModel.Field => (6, 6, 4),
        MapModel.Sheep => (2, 2, 3),
        MapModel.Pine or MapModel.Spruce or MapModel.SnowFir => (5, 5, 20),
        MapModel.Oak or MapModel.Willow or MapModel.Palm => (7, 7, 16),
        _ when Grows(m) => (5, 5, 14),
        _ => (13, 13, 24),
    };

    // ------------------------------------------------------------ meshed

    sealed class Kit
    {
        public readonly GreedyMesher Mesher = new();
        public readonly byte[] Pad = new byte[34 * 34 * 34];
    }
    [ThreadStatic] static Kit _kit;

    /// <summary>A canvas meshed: its chunks joined into one mesh and one shadow, their places about the model's middle.</summary>
    static Made Mesh(ModelCanvas c)
    {
        var k = _kit ??= new Kit();
        var w = c.W;
        var solids = new List<MeshBuffers>();
        var shades = new List<MeshBuffers>();
        for (int ci = 0; ci < w.ChunkCount; ci++)
        {
            if (w.IsEmpty(ci)) continue;
            var s = k.Mesher.MeshChunk(w, ci, k.Pad, null, out _, out _, out var sh);
            if (s != null && s.Quads > 0) solids.Add(s);
            if (sh != null && sh.Quads > 0) shades.Add(sh);
        }
        var (ox, oy, oz) = c.Origin;
        return new Made { Solid = Join(solids, -ox, -oy, -oz), Shadow = Join(shades, -ox, -oy, -oz) };
    }

    /// <summary>Meshes joined into one, moved by so many voxels.</summary>
    static MeshBuffers Join(List<MeshBuffers> parts, float ox, float oy, float oz)
    {
        if (parts.Count == 0) return null;
        int quads = 0;
        bool col = true, uv = true, uv2 = true;
        foreach (var p in parts) { quads += p.Quads; col &= p.Col != null; uv &= p.Uv != null; uv2 &= p.Uv2 != null; }
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
                m.Pos[(v0 + i) * 3 + 1] = p.Pos[i * 3 + 1] + oy;
                m.Pos[(v0 + i) * 3 + 2] = p.Pos[i * 3 + 2] + oz;
            }
            Array.Copy(p.Nrm, 0, m.Nrm, v0 * 3, v * 3);
            if (col) Array.Copy(p.Col, 0, m.Col, v0 * 4, v * 4);
            if (uv) Array.Copy(p.Uv, 0, m.Uv, v0 * 2, v * 2);
            if (uv2) Array.Copy(p.Uv2, 0, m.Uv2, v0 * 2, v * 2);
            int ni = p.Quads * 6;
            for (int i = 0; i < ni; i++) m.Idx[i0 + i] = p.Idx[i] + v0;
            v0 += v; i0 += ni;
        }
        return m;
    }
}
