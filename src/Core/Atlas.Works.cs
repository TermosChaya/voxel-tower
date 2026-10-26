using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The buildings of the map of valleys, each made of voxels at the map's own scale and turned to face the south, where the
/// eye looks from: the first valley's castle with its round towers, its keep and its flags among cottages and a windmill;
/// an outpost's tower, the stone and stakes of a place for one; the horde's lair behind its palisade, its hide tents, its
/// totem and its fire, and a camp of the horde in a cell it took; a lair taken, burnt; the mines of an ore valley, the
/// barrow of a holy one; and the treasures — the abandoned mine with its ghost fire and its chest, the ruins, the den
/// with its bones, the ring of stones round the altar of a shrine of trial. Our flags are blue, the horde's red.
/// </summary>
public sealed partial class Atlas
{
    void Box(int x0, int y0, int z0, int x1, int y1, int z1, byte m, Func<int, int, bool> within, bool over = true)
    {
        for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++) Put(x, y, z, m, within, Lie.Built, over);
    }

    /// <summary>A round tower's body: a cylinder of stone, darker in bands.</summary>
    void Drum(float cx, float cz, float r, int y0, int y1, byte m, byte band, Func<int, int, bool> within)
    {
        for (int y = y0; y <= y1; y++)
            for (int z = (int)MathF.Floor(cz - r); z <= (int)MathF.Ceiling(cz + r); z++)
                for (int x = (int)MathF.Floor(cx - r); x <= (int)MathF.Ceiling(cx + r); x++)
                    if (Dist(x + .5f, z + .5f, cx, cz) <= r) Put(x, y, z, (y - y0) % 4 == 3 && Hash.H3(x, y, z) < .5 ? band : m, within, Lie.Built, true);
    }

    /// <summary>A cone of a roof or a tent over a round body.</summary>
    void Cone(float cx, float cz, float r, int y0, int tall, byte m, Func<int, int, bool> within, byte trim = 0)
    {
        for (int k = 0; k < tall; k++)
        {
            float rr = r * (1 - k / (float)tall) + .2f;
            for (int z = (int)MathF.Floor(cz - rr); z <= (int)MathF.Ceiling(cz + rr); z++)
                for (int x = (int)MathF.Floor(cx - rr); x <= (int)MathF.Ceiling(cx + rr); x++)
                    if (Dist(x + .5f, z + .5f, cx, cz) <= rr) Put(x, y0 + k, z, k == 0 && trim != 0 ? trim : m, within, Lie.Built, true);
        }
    }

    /// <summary>A flag on its pole, the cloth out to the east.</summary>
    void Flag(int x, int y, int z, byte cloth, byte trim, Func<int, int, bool> within, int pole = 4)
    {
        for (int k = 0; k < pole; k++) Put(x, y + k, z, Mat.Iron, within, Lie.Built, true);
        for (int k = 0; k < 2; k++)
            for (int j = 1; j <= 3; j++) Put(x + j, y + pole - 1 - k, z, j == 3 && k == 1 ? trim : cloth, within, Lie.Built, true);
    }

    /// <summary>Teeth along the top of a wall: every other voxel of its outer edge a voxel higher.</summary>
    void Merlons(int x0, int z0, int x1, int z1, int y, byte m, Func<int, int, bool> within)
    {
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                if ((x == x0 || x == x1 || z == z0 || z == z1) && (x + z) % 2 == 0) Put(x, y, z, m, within, Lie.Built, true);
    }

    int Floor(float x, float z) => _h[Math.Clamp((int)z, 0, SZ - 1) * SX + Math.Clamp((int)x, 0, SX - 1)];

    void Thing(AtlasThingKind k, MapCell c, float x, float z) => Things.Add(new AtlasThing { Kind = k, Cell = c, X = (int)x, Z = (int)z });

    /// <summary>The buildings of a cell, as its holder and its treasure now are.</summary>
    void Works(MapCell c, Func<int, int, bool> within)
    {
        var (cx, cz) = Center(c);
        if (c.Home)
        {
            Castle(c, cx, cz, within);
            Village(c, cx, cz, within);
        }
        else if (c.Holding == Holding.HordeTown)
        {
            if (c.Owner == Owner.Horde) Lair(c, cx, cz, within);
            else TakenLair(c, cx, cz, within);
        }
        else if (c.Owner == Owner.Ours) Outpost(c, cx, cz, within);
        else if (c.Owner == Owner.Horde) Camp(c, cx, cz, within);
        else OutpostPlace(c, cx, cz, within);
        if (!_spot.TryGetValue(c, out var sp)) return;
        bool ours = c.Owner == Owner.Ours;
        switch (c.Trove)
        {
            case TroveKind.DeadMine: Mine(c, sp.x, sp.z, within, dead: true); break;
            case TroveKind.Ruins: Ruins(c, sp.x, sp.z, within); break;
            case TroveKind.Den: Den(c, sp.x, sp.z, within); break;
            case TroveKind.Shrine: Shrine(c, sp.x, sp.z, within); break;
            default:
                if (c.Kind == ValleyKind.Ore) Mine(c, sp.x, sp.z, within, dead: false, ours);
                else if (c.Kind == ValleyKind.Sacred) Barrow(c, sp.x, sp.z, within);
                break;
        }
    }

    // ------------------------------------------------------------ ours

    /// <summary>
    /// The first valley's castle on its mound: a square of curtain walls with round towers at the corners under red cones,
    /// a gatehouse to the south with its two small towers, the keep within under a slate roof, a hall along the east wall,
    /// the yard paved; our flags on the towers and the keep (the horde's, if it took the castle).
    /// </summary>
    void Castle(MapCell c, float fcx, float fcz, Func<int, int, bool> within)
    {
        int cx = (int)fcx, cz = (int)fcz, g = Floor(cx, cz);
        bool fallen = c.Owner == Owner.Horde;
        byte cloth = fallen ? Mat.ClothRed : Mat.Banner, trim = fallen ? Mat.Bone : Mat.BannerTrim;
        const int Half = 11, WallH = 7;
        // the foundation, down to the slope of the mound
        Box(cx - Half - 1, g - 4, cz - Half - 1, cx + Half + 1, g - 1, cz + Half + 1, Mat.KeepShade, within, over: false);
        // the yard
        Box(cx - Half + 2, g, cz - Half + 2, cx + Half - 2, g, cz + Half - 2, Mat.Flag, within);
        // the curtain walls, two thick
        for (int y = g; y < g + WallH; y++)
            for (int z = cz - Half; z <= cz + Half; z++)
                for (int x = cx - Half; x <= cx + Half; x++)
                {
                    bool wall = x <= cx - Half + 1 || x >= cx + Half - 1 || z <= cz - Half + 1 || z >= cz + Half - 1;
                    if (!wall) continue;
                    Put(x, y, z, Hash.H3(x, y, z) < .3 ? Mat.KeepShade : Mat.StoneLight, within, Lie.Built, true);
                }
        Merlons(cx - Half, cz - Half, cx + Half, cz + Half, g + WallH, Mat.StoneLight, within);
        // the gate through the south wall
        for (int y = g; y < g + 4; y++)
            for (int x = cx - 2; x <= cx + 2; x++)
                for (int z = cz + Half - 1; z <= cz + Half; z++) Put(x, y, z, z == cz + Half - 1 ? Mat.Gate : Mat.Air, within, Lie.None, true);
        for (int x = cx - 2; x <= cx + 2; x++) Put(x, g + 4, cz + Half, Mat.KeepDark, within, Lie.Built, true);
        // the corner towers and the gatehouse's two
        foreach (var (tx, tz) in new[] { (cx - Half + 1, cz - Half + 1), (cx + Half - 1, cz - Half + 1), (cx - Half + 1, cz + Half - 1), (cx + Half - 1, cz + Half - 1) })
        {
            Drum(tx + .5f, tz + .5f, 3.3f, g, g + 11, Mat.StoneLight, Mat.KeepShade, within);
            Cone(tx + .5f, tz + .5f, 4f, g + 12, 6, Mat.Tile, within, Mat.KeepDark);
            Flag(tx, g + 18, tz, cloth, trim, within);
            // its arrow slits toward the south and the west
            Put(tx, g + 7, tz + 3, Mat.KeepDark, within, Lie.Built, true);
            Put(tx - 3, g + 7, tz, Mat.KeepDark, within, Lie.Built, true);
        }
        foreach (int gx in new[] { cx - 4, cx + 4 })
        {
            Drum(gx + .5f, cz + Half + .5f, 2.3f, g, g + 9, Mat.StoneLight, Mat.KeepShade, within);
            Cone(gx + .5f, cz + Half + .5f, 2.9f, g + 10, 5, Mat.Tile, within);
        }
        // the keep: tall and square, windows dark, under a slate pyramid with the great flag
        int kx0 = cx - 5, kx1 = cx + 3, kz0 = cz - 8, kz1 = cz;
        for (int y = g; y < g + 17; y++)
            for (int z = kz0; z <= kz1; z++)
                for (int x = kx0; x <= kx1; x++)
                {
                    bool face = x == kx0 || x == kx1 || z == kz0 || z == kz1;
                    if (!face) continue;
                    bool window = (y - g) % 5 == 3 && (x == cx - 1 || z == cz - 4) && y > g + 3;
                    Put(x, y, z, window ? Mat.KeepDark : Hash.H3(x, y, z) < .25 ? Mat.Keep : Mat.StoneLight, within, Lie.Built, true);
                }
        Box(kx0 + 1, g + 16, kz0 + 1, kx1 - 1, g + 16, kz1 - 1, Mat.StoneLight, within);
        Merlons(kx0, kz0, kx1, kz1, g + 17, Mat.StoneLight, within);
        for (int k = 0; k < 5; k++) Box(kx0 + 1 + k, g + 17 + k, kz0 + 1 + k, kx1 - 1 - k, g + 17 + k, kz1 - 1 - k, Mat.Roof, within);
        Flag(cx - 1, g + 22, cz - 4, cloth, trim, within, 5);
        Smokes.Add((kx1 - 1, g + 22, kz0 + 1));
        // the hall along the east wall, its roof red
        for (int y = g + 1; y < g + 5; y++)
            for (int z = cz - 6; z <= cz + 5; z++)
                for (int x = cx + 5; x <= cx + 8; x++)
                    if (x == cx + 5 || z == cz - 6 || z == cz + 5) Put(x, y, z, Mat.Daub, within, Lie.Built, true);
        for (int k = 0; k < 3; k++) Box(cx + 5 + k, g + 5 + k, cz - 6, cx + 9 - k, g + 5 + k, cz + 5, Mat.Tile, within);
        Thing(AtlasThingKind.Citadel, c, cx, cz + Half + 3);
    }

    /// <summary>The cottages among the fields about the castle and a windmill on a rise: whitewashed, under thatch or tiles, smoke from their chimneys.</summary>
    void Village(MapCell c, float cx, float cz, Func<int, int, bool> within)
    {
        var rnd = new Mulberry32(SeedOf(c, 21));
        var placed = new List<(float x, float z)>();
        bool Free(float x, float z, float r)
        {
            for (int dz = -(int)r; dz <= (int)r; dz++)
                for (int dx = -(int)r; dx <= (int)r; dx++)
                {
                    int px = (int)x + dx, pz = (int)z + dz;
                    if (CellOfColumn(px, pz) != c) return false;
                    int i = pz * SX + px;
                    if (_wet[i] > _h[i] || (_lie[i] & (Lie.Road | Lie.Field | Lie.River | Lie.Lake | Lie.Built)) != 0 || _dRoad[i] < 2.5f) return false;
                }
            foreach (var p in placed) if (Dist(x, z, p.x, p.z) < r + 6) return false;
            return Slope((int)x, (int)z) <= 1;
        }
        for (int tries = 0, n = 0; tries < 120 && n < 7; tries++)
        {
            float ang = (float)rnd.Next() * MathF.Tau, d = 21 + (float)rnd.Next() * 22;
            float x = cx + MathF.Cos(ang) * d, z = cz + MathF.Sin(ang) * d;
            bool along = rnd.Next() < .5;
            if (!Free(x, z, 4)) continue;
            placed.Add((x, z));
            n++;
            Cottage((int)x, (int)z, along, rnd.Next() < .55 ? Mat.Thatch : Mat.Tile, within);
        }
        for (int tries = 0; tries < 60; tries++)
        {
            float ang = (float)rnd.Next() * MathF.Tau, d = 26 + (float)rnd.Next() * 16;
            float x = cx + MathF.Cos(ang) * d, z = cz + MathF.Sin(ang) * d;
            if (!Free(x, z, 3)) continue;
            Windmill((int)x, (int)z, within);
            break;
        }
    }

    void Cottage(int x, int z, bool along, byte roof, Func<int, int, bool> within)
    {
        int g = Floor(x, z);
        int hx = along ? 3 : 2, hz = along ? 2 : 3;
        for (int y = g; y < g + 3; y++)
            for (int dz = -hz; dz <= hz; dz++)
                for (int dx = -hx; dx <= hx; dx++)
                {
                    bool edge = Math.Abs(dx) == hx || Math.Abs(dz) == hz;
                    if (!edge) continue;
                    bool corner = Math.Abs(dx) == hx && Math.Abs(dz) == hz;
                    bool door = !along ? dz == hz && dx == 0 && y < g + 2 : dz == hz && dx == 0 && y < g + 2;
                    Put(x + dx, y, z + dz, door ? Mat.Wood : corner || y == g ? Mat.Wood : Mat.Daub, within, Lie.Built, true);
                }
        // a gable roof along the house's length, a voxel over its walls
        int half = along ? hz : hx;
        for (int k = 0; k <= half + 1; k++)
            for (int s = -(half + 1) + k; s <= half + 1 - k; s++)
                for (int l = -(along ? hx : hz) - 1; l <= (along ? hx : hz) + 1; l++)
                {
                    if (Math.Abs(s) != half + 1 - k && k != half + 1) continue;
                    int px = along ? x + l : x + s, pz = along ? z + s : z + l;
                    Put(px, g + 3 + k, pz, roof, within, Lie.Built, true);
                }
        int chx = along ? x + hx - 1 : x, chz = along ? z : z + hz - 1;
        for (int y = g + 3; y < g + 3 + half + 3; y++) Put(chx, y, chz, Mat.StoneDark, within, Lie.Built, true);
        Smokes.Add((chx, g + 3 + half + 3, chz));
    }

    void Windmill(int x, int z, Func<int, int, bool> within)
    {
        int g = Floor(x, z);
        Drum(x + .5f, z + .5f, 2.2f, g, g + 7, Mat.Daub, Mat.StoneLight, within);
        Cone(x + .5f, z + .5f, 2.8f, g + 8, 4, Mat.Thatch, within);
        // the sails on the south face
        int hy = g + 7, fz = z + 3;
        Put(x, hy, fz - 1, Mat.Wood, within, Lie.Built, true);
        Put(x, hy, fz, Mat.Wood, within, Lie.Built, true);
        for (int k = 1; k <= 5; k++)
        {
            Put(x + k, hy + k, fz, k > 1 ? Mat.Linen : Mat.Plank, within, Lie.Built, true);
            Put(x - k, hy - k, fz, k > 1 ? Mat.Linen : Mat.Plank, within, Lie.Built, true);
            Put(x - k, hy + k, fz, k > 1 ? Mat.Linen : Mat.Plank, within, Lie.Built, true);
            if (hy - k > g) Put(x + k, hy - k, fz, k > 1 ? Mat.Linen : Mat.Plank, within, Lie.Built, true);
        }
    }

    /// <summary>An outpost of ours: a square stone tower with its teeth and our flag, stakes about it.</summary>
    void Outpost(MapCell c, float fcx, float fcz, Func<int, int, bool> within)
    {
        int cx = (int)fcx, cz = (int)fcz, g = Floor(cx, cz);
        for (int y = g; y < g + 12; y++)
            for (int z = cz - 3; z <= cz + 2; z++)
                for (int x = cx - 3; x <= cx + 2; x++)
                {
                    bool face = x == cx - 3 || x == cx + 2 || z == cz - 3 || z == cz + 2;
                    if (!face && y > g) continue;
                    bool slit = (y - g) % 4 == 2 && (x == cx || z == cz) && y > g + 1;
                    Put(x, y, z, slit ? Mat.KeepDark : Hash.H3(x, y, z) < .3 ? Mat.KeepShade : Mat.StoneLight, within, Lie.Built, true);
                }
        Box(cx - 2, g + 11, cz - 2, cx + 1, g + 11, cz + 1, Mat.StoneLight, within);
        Merlons(cx - 3, cz - 3, cx + 2, cz + 2, g + 12, Mat.StoneLight, within);
        Flag(cx, g + 12, cz, Mat.Banner, Mat.BannerTrim, within);
        for (int k = 0; k < 18; k++)
        {
            float a = k / 18f * MathF.Tau;
            if (MathF.Sin(a) > .85f) continue;
            int px = cx + (int)MathF.Round(MathF.Cos(a) * 7), pz = cz + (int)MathF.Round(MathF.Sin(a) * 7);
            int pg = Floor(px, pz);
            Put(px, pg, pz, Mat.Stakes, within, Lie.Built, true);
            Put(px, pg + 1, pz, Mat.Stakes, within, Lie.Built, true);
        }
        Thing(AtlasThingKind.Outpost, c, cx, cz + 5);
    }

    /// <summary>
    /// The place an outpost is to go in a cell nobody holds: its stone, and a stake at each corner of its square, a little
    /// north of the middle, which is kept clear for a hero to stand on.
    /// </summary>
    void OutpostPlace(MapCell c, float fcx, float fcz, Func<int, int, bool> within)
    {
        int cx = (int)fcx, cz = (int)fcz - 6, g = Floor(cx, cz);
        Box(cx, g, cz, cx + 1, g + 2, cz + 1, Mat.StoneLight, within);
        Put(cx, g + 3, cz, Mat.StoneLight, within, Lie.Built, true);
        foreach (var (dx, dz) in new[] { (-5, -4), (5, -4), (-5, 4), (5, 4) })
        {
            int px = cx + dx, pz = cz + dz, pg = Floor(px, pz);
            for (int y = pg; y < pg + 3; y++) Put(px, y, pz, Mat.Wood, within, Lie.Built, true);
        }
        Thing(AtlasThingKind.OutpostPlace, c, cx, cz + 6);
    }

    // ------------------------------------------------------------ the horde's

    /// <summary>
    /// The horde's lair: a ring of pointed logs with its gate toward the first valley, skulls on the gate posts, hide tents
    /// within, the totem with the horde's red cloth in the middle, the fire before it, bones about.
    /// </summary>
    void Lair(MapCell c, float fcx, float fcz, Func<int, int, bool> within)
    {
        int cx = (int)fcx, cz = (int)fcz;
        var (hx, hz) = Center(Campaign.Home ?? c);
        float gate = MathF.Atan2(hz - fcz, hx - fcx);
        const float R = 14;
        for (int k = 0; k < 120; k++)
        {
            float a = k / 120f * MathF.Tau;
            float off = MathF.Abs(MathF.IEEERemainder(a - gate, MathF.Tau));
            if (off < .2f) continue;
            int px = cx + (int)MathF.Round(MathF.Cos(a) * R), pz = cz + (int)MathF.Round(MathF.Sin(a) * R), pg = Floor(px, pz);
            int tall = 5 + (k % 2);
            for (int y = pg; y < pg + tall; y++) Put(px, y, pz, y == pg + tall - 1 ? Mat.Stakes : Mat.Trunk, within, Lie.Built, true);
            if (off < .32f)
            {
                for (int y = pg + tall; y < pg + tall + 3; y++) Put(px, y, pz, Mat.Trunk, within, Lie.Built, true);
                Put(px, pg + tall + 3, pz, Mat.Bone, within, Lie.Built, true);
            }
        }
        // the trodden ground within, the tents of pale hides on it
        for (int dz = -13; dz <= 13; dz++)
            for (int dx = -13; dx <= 13; dx++)
                if (dx * dx + dz * dz < 13 * 13) Put(cx + dx, Floor(cx + dx, cz + dz) - 1, cz + dz, Hash.H3(cx + dx, 5, cz + dz) < .3 ? Mat.Ash : Mat.Trodden, within, Lie.None, true);
        var rnd = new Mulberry32(SeedOf(c, 31));
        for (int k = 0; k < 6; k++)
        {
            float a = gate + MathF.PI * .35f + k * MathF.PI * 1.3f / 6, d = 8 + (float)rnd.Next() * 2.5f;
            float tx = cx + MathF.Cos(a) * d, tz = cz + MathF.Sin(a) * d;
            int tg = Floor(tx, tz);
            Cone(tx, tz, 2.6f, tg, 5, k % 2 == 0 ? Mat.Sack : Mat.Fawn, within, Mat.LeatherDark);
            Put((int)tx, tg + 5, (int)tz, Mat.Bone, within, Lie.Built, true);
        }
        Totem(cx, cz, 14, within);
        int fx = cx + (int)MathF.Round(MathF.Cos(gate) * 4), fz = cz + (int)MathF.Round(MathF.Sin(gate) * 4);
        Fire(fx, fz, within);
        for (int k = 0; k < 10; k++)
        {
            int bx = cx + (int)((rnd.Next() * 2 - 1) * 11), bz = cz + (int)((rnd.Next() * 2 - 1) * 11);
            if (Dist(bx, bz, cx, cz) < 11) Put(bx, Floor(bx, bz), bz, rnd.Next() < .5 ? Mat.Bone : Mat.BonePile, within);
        }
        Thing(AtlasThingKind.Lair, c, cx + MathF.Cos(gate) * (R + 3), cz + MathF.Sin(gate) * (R + 3));
    }

    void Totem(int x, int z, int tall, Func<int, int, bool> within)
    {
        int g = Floor(x, z);
        for (int y = g; y < g + tall; y++) Put(x, y, z, y % 4 == 0 ? Mat.Warpaint : Mat.Trunk, within, Lie.Built, true);
        Box(x - 1, g + tall, z - 1, x + 1, g + tall + 1, z + 1, Mat.Bone, within);
        Put(x - 1, g + tall, z + 1, Mat.KeepDark, within, Lie.Built, true);
        Put(x + 1, g + tall, z + 1, Mat.KeepDark, within, Lie.Built, true);
        for (int k = 0; k < 5; k++)
            for (int j = 1; j <= 2; j++) Put(x + j, g + tall - 1 - k, z, Mat.ClothRed, within, Lie.Built, true);
    }

    void Fire(int x, int z, Func<int, int, bool> within)
    {
        int g = Floor(x, z);
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) }) Put(x + dx, g, z + dz, Mat.Rock, within, Lie.Built, true);
        Put(x, g, z, Mat.Ember, within, Lie.Built, true);
        Put(x, g + 1, z, Mat.Flame, within, Lie.Built, true);
        Fires.Add((x, g + 1, z, false));
        Smokes.Add((x, g + 3, z));
    }

    /// <summary>A lair the horde lost: its palisade burnt down to stumps, its tents fallen into heaps of ash, our flag if it is ours.</summary>
    void TakenLair(MapCell c, float fcx, float fcz, Func<int, int, bool> within)
    {
        int cx = (int)fcx, cz = (int)fcz;
        for (int k = 0; k < 120; k += 2)
        {
            float a = k / 120f * MathF.Tau;
            int px = cx + (int)MathF.Round(MathF.Cos(a) * 14), pz = cz + (int)MathF.Round(MathF.Sin(a) * 14), pg = Floor(px, pz);
            int tall = (int)(Hash.H3(px, 3, pz) * 3);
            for (int y = pg; y < pg + tall; y++) Put(px, y, pz, Mat.Char, within, Lie.Built, true);
        }
        Box(cx - 1, Floor(cx, cz), cz - 1, cx + 1, Floor(cx, cz), cz + 1, Mat.Char, within);
        if (c.Owner == Owner.Ours) Flag(cx, Floor(cx, cz) + 1, cz, Mat.Banner, Mat.BannerTrim, within, 6);
        Thing(AtlasThingKind.TakenLair, c, cx, cz + 16);
    }

    /// <summary>A camp of the horde in a cell it took: three hide tents, its totem and its fire.</summary>
    void Camp(MapCell c, float fcx, float fcz, Func<int, int, bool> within)
    {
        int cx = (int)fcx, cz = (int)fcz;
        foreach (var (dx, dz) in new[] { (-5, -3), (5, -4), (0, -7) })
        {
            int g = Floor(cx + dx, cz + dz);
            Cone(cx + dx + .5f, cz + dz + .5f, 2.4f, g, 4, Mat.Sack, within, Mat.LeatherDark);
        }
        Totem(cx, cz, 9, within);
        Fire(cx, cz + 4, within);
        Thing(AtlasThingKind.HordeCamp, c, cx, cz + 7);
    }

    // ------------------------------------------------------------ the mines, the barrows, the treasures

    /// <summary>A knoll of rock about a point, a mouth cut into its south face.</summary>
    int Knoll(int x, int z, float r, int tall, Func<int, int, bool> within)
    {
        int g = Floor(x, z);
        for (int dz = -(int)r - 1; dz <= (int)r + 1; dz++)
            for (int dx = -(int)r - 1; dx <= (int)r + 1; dx++)
            {
                float d = MathF.Sqrt(dx * dx + dz * dz) / r;
                if (d >= 1) continue;
                int top = g + (int)MathF.Round(tall * MathF.Sqrt(1 - d * d) * (.85f + .3f * (float)Hash.H3(x + dx, 9, z + dz)));
                int pg = Floor(x + dx, z + dz);
                for (int y = pg; y < top; y++) Put(x + dx, y, z + dz, y == top - 1 && d < .5f && Hash.H3(x + dx, y, z + dz) < .4 ? Mat.Moss : Hash.H3(x + dx, y, z + dz) < .3 ? Mat.StoneDark : Mat.Rock, within, Lie.Built, true);
            }
        return g;
    }

    /// <summary>The dark mouth of a mine or a cave in the south face of a knoll, framed in timber or not.</summary>
    void Mouth(int x, int z, int g, int depth, bool timber, Func<int, int, bool> within)
    {
        for (int dz = 0; dz < depth; dz++)
            for (int y = g; y < g + 3; y++)
                for (int dx = -1; dx <= 1; dx++) Put(x + dx, y, z - dz, dz >= depth - 2 ? Mat.Hollow : Mat.Air, within, Lie.None, true);
        if (!timber) return;
        for (int y = g; y < g + 4; y++) { Put(x - 2, y, z + 1, Mat.Wood, within, Lie.Built, true); Put(x + 2, y, z + 1, Mat.Wood, within, Lie.Built, true); }
        for (int dx = -2; dx <= 2; dx++) Put(x + dx, g + 3, z + 1, Mat.Wood, within, Lie.Built, true);
    }

    /// <summary>
    /// The mines of an ore valley, or the abandoned mine kept by the dead: a knoll of rock, the timbered mouth, the heap of
    /// ore before it; the dead one's timbers broken, skulls about, a ghost fire at its mouth and the chest, till it is taken.
    /// </summary>
    void Mine(MapCell c, float sx, float sz, Func<int, int, bool> within, bool dead, bool ours = false)
    {
        int x = (int)sx, z = (int)sz;
        int g = Knoll(x, z - 3, 8, 8, within);
        int mz = z + 3;
        Mouth(x, mz, g, 6, true, within);
        if (dead) Put(x + 2, g + 3, mz + 1, Mat.Air, within, Lie.None, true);
        // the heap of ore and a cart beside it
        for (int k = 0; k < 7; k++)
        {
            int ox = x + 3 + k % 3, oz = mz + 2 + k / 3;
            Put(ox, Floor(ox, oz), oz, dead ? Mat.Rubble : Mat.Ore, within, Lie.Built);
        }
        Put(x + 4, Floor(x + 4, mz + 2) + 1, mz + 3, dead ? Mat.Rubble : Mat.Ore, within, Lie.Built);
        Box(x - 5, Floor(x - 4, mz + 3), mz + 2, x - 3, Floor(x - 4, mz + 3), mz + 3, Mat.Plank, within);
        bool taken = dead && c.TroveTaken;
        if (dead)
        {
            foreach (var (dx, dz) in new[] { (-3, 4), (2, 5), (-1, 6) }) Put(x + dx, Floor(x + dx, mz + dz), mz + dz, Mat.Bone, within);
            if (!taken)
            {
                Chest(x, mz + 1, within);
                Put(x - 1, g, mz - 1, Mat.SoulFire, within, Lie.Built, true);
                Put(x - 1, g + 1, mz - 1, Mat.SoulFire, within, Lie.Built, true);
                Fires.Add((x - 1, g + 1, mz - 1, true));
            }
        }
        if (ours || taken && c.Owner == Owner.Ours) Flag(x + 2, g + 7, z - 3, Mat.Banner, Mat.BannerTrim, within);
        Thing(dead ? AtlasThingKind.DeadMine : AtlasThingKind.Mine, c, x, mz + 4);
    }

    void Chest(int x, int z, Func<int, int, bool> within)
    {
        int g = Floor(x, z);
        Put(x, g, z, Mat.Wood, within, Lie.Built, true);
        Put(x + 1, g, z, Mat.Wood, within, Lie.Built, true);
        Put(x, g + 1, z, Mat.Brass, within, Lie.Built, true);
        Put(x + 1, g + 1, z, Mat.BannerTrim, within, Lie.Built, true);
    }

    /// <summary>The ruins: walls of old weathered stone broken to every height, two columns, rubble, the chest in the middle till it is taken.</summary>
    void Ruins(MapCell c, float sx, float sz, Func<int, int, bool> within)
    {
        int x = (int)sx, z = (int)sz;
        const int Hx = 7, Hz = 5;
        for (int dz = -Hz; dz <= Hz; dz++)
            for (int dx = -Hx; dx <= Hx; dx++)
            {
                bool wall = Math.Abs(dx) == Hx || Math.Abs(dz) == Hz || dx == -1 && dz < 0;
                if (!wall) continue;
                double h = Hash.H3(x + dx, 77, z + dz);
                if (dz == Hz && Math.Abs(dx) <= 1) continue;
                int g = Floor(x + dx, z + dz), tall = h < .25 ? 0 : 1 + (int)(h * 6 * (1 - Math.Abs(dx) / (Hx + 4f)));
                for (int y = g; y < g + tall; y++) Put(x + dx, y, z + dz, Hash.H3(x + dx, y, z + dz) < .25 ? Mat.Moss : Mat.Ruin, within, Lie.Built, true);
            }
        foreach (int dx in new[] { -4, 4 })
        {
            int g = Floor(x + dx, z + Hz + 2), tall = dx < 0 ? 7 : 4;
            Box(x + dx, g, z + Hz + 2, x + dx, g + tall - 1, z + Hz + 2, Mat.Ruin, within);
        }
        var rnd = new Mulberry32(SeedOf(c, 41));
        for (int k = 0; k < 14; k++)
        {
            int rx = x + (int)((rnd.Next() * 2 - 1) * (Hx + 3)), rz = z + (int)((rnd.Next() * 2 - 1) * (Hz + 3));
            Put(rx, Floor(rx, rz), rz, Mat.Rubble, within, Lie.None);
        }
        if (!c.TroveTaken) Chest(x, z + 1, within);
        else if (c.Owner == Owner.Ours) Flag(x, Floor(x, z), z, Mat.Banner, Mat.BannerTrim, within);
        Thing(AtlasThingKind.Ruins, c, x, z + Hz + 3);
    }

    /// <summary>The den: a cave in a knoll of rock, bones before its mouth.</summary>
    void Den(MapCell c, float sx, float sz, Func<int, int, bool> within)
    {
        int x = (int)sx, z = (int)sz;
        int g = Knoll(x, z - 3, 9, 9, within);
        int mz = z + 4;
        Mouth(x, mz, g, 7, false, within);
        // a wider, rounder mouth than a mine's
        for (int y = g; y < g + 2; y++) { Put(x - 2, y, mz, Mat.Air, within, Lie.None, true); Put(x + 2, y, mz, Mat.Air, within, Lie.None, true); }
        var rnd = new Mulberry32(SeedOf(c, 43));
        for (int k = 0; k < 9; k++)
        {
            int bx = x + (int)((rnd.Next() * 2 - 1) * 5), bz = mz + 1 + (int)(rnd.Next() * 4);
            Put(bx, Floor(bx, bz), bz, k % 3 == 0 ? Mat.BonePile : Mat.Bone, within);
        }
        if (c.TroveTaken && c.Owner == Owner.Ours) Flag(x + 3, g + 8, z - 3, Mat.Banner, Mat.BannerTrim, within);
        Thing(AtlasThingKind.Den, c, x, mz + 4);
    }

    /// <summary>The shrine of trial: nine black stones in a ring about an altar, the ghost fire on it till its trial is passed, then a fire of ours.</summary>
    void Shrine(MapCell c, float sx, float sz, Func<int, int, bool> within)
    {
        int x = (int)sx, z = (int)sz;
        for (int k = 0; k < 9; k++)
        {
            float a = k / 9f * MathF.Tau + .2f;
            int px = x + (int)MathF.Round(MathF.Cos(a) * 7), pz = z + (int)MathF.Round(MathF.Sin(a) * 7), g = Floor(px, pz);
            int tall = 4 + (int)(Hash.H3(px, 5, pz) * 3);
            Box(px, g, pz, px + 1, g + tall - 1, pz + 1, Mat.Menhir, within);
        }
        int ag = Floor(x, z);
        Box(x - 1, ag, z - 1, x + 1, ag + 1, z + 1, Mat.StoneLight, within);
        bool passed = c.TroveTaken;
        Put(x, ag + 2, z, passed ? Mat.Ember : Mat.SoulFire, within, Lie.Built, true);
        Put(x, ag + 3, z, passed ? Mat.Flame : Mat.SoulFire, within, Lie.Built, true);
        Fires.Add((x, ag + 3, z, !passed));
        if (passed && c.Owner == Owner.Ours) Flag(x + 3, ag, z + 3, Mat.Banner, Mat.BannerTrim, within);
        Thing(AtlasThingKind.Shrine, c, x, z + 9);
    }

    /// <summary>The barrow of a holy valley: a green mound with the black stone on it and three standing stones about.</summary>
    void Barrow(MapCell c, float sx, float sz, Func<int, int, bool> within)
    {
        int x = (int)sx, z = (int)sz;
        for (int dz = -6; dz <= 6; dz++)
            for (int dx = -6; dx <= 6; dx++)
            {
                float d = MathF.Sqrt(dx * dx + dz * dz) / 6;
                if (d >= 1) continue;
                int g = Floor(x + dx, z + dz), top = g + (int)MathF.Round(3.5f * (1 - d * d));
                for (int y = g; y < top; y++) Put(x + dx, y, z + dz, y == top - 1 ? Mat.GrassLush : Mat.Soil, within, Lie.Built, true);
            }
        int bg = Floor(x, z) + 3;
        Box(x, bg, z, x + 1, bg + 4, z + 1, Mat.Menhir, within);
        foreach (var (dx, dz) in new[] { (-8, 2), (7, 3), (1, -8) })
        {
            int g = Floor(x + dx, z + dz);
            Box(x + dx, g, z + dz, x + dx, g + 2, z + dz, Mat.StoneLight, within);
        }
        Thing(AtlasThingKind.PlaceOfPower, c, x, z + 8);
    }
}
