using System;

namespace VoxelTower.Core;

/// <summary>
/// What stands on a hex of the world map, in voxels (HexTiles.cs puts them in a piece's world): a wood's three to six
/// trees, a boulder on a hill; and its thing at its middle, one model to be known at a glance from the camera of
/// «Heroes» — our citadel a small castle under red roofs and a flag, an outpost a tower, the horde's town a dark
/// palisade of huts round a fire, its lair a cave in a rock with bones and a skull on a pole; a sawmill with its logs,
/// a quarry of cut blocks, mines in a rock with ore, gold, crystals; a lab with a pool of quicksilver, a dune of sulphur,
/// a pond of gems; a pile of a resource, a chest, a campfire, a windmill, a water wheel, a shrine, a dwelling, a dead
/// mine, ruins, a den, a ring of standing stones, a signpost, a stone of learning, stables, an observatory, a gate in a
/// pass. What is held by someone flies a flag: blue ours, red the horde's. Sizes as a hex is 31 voxels across.
/// </summary>
public static class HexModels
{
    // ------------------------------------------------------------ the shapes things are made of

    static void Box(HexCanvas c, int x0, int y0, int z0, int x1, int y1, int z1, byte m)
    {
        for (int y = y0; y < y1; y++)
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++) c.Put(x, y, z, m);
    }

    static void Drum(HexCanvas c, float cx, float cz, float r, int y0, int y1, byte m, byte hollow = 0)
    {
        for (int z = (int)MathF.Floor(cz - r); z <= (int)MathF.Ceiling(cz + r); z++)
            for (int x = (int)MathF.Floor(cx - r); x <= (int)MathF.Ceiling(cx + r); x++)
            {
                float d = MathF.Sqrt((x + .5f - cx) * (x + .5f - cx) + (z + .5f - cz) * (z + .5f - cz));
                if (d > r) continue;
                for (int y = y0; y < y1; y++) c.Put(x, y, z, hollow != 0 && d < r - 1.2f ? hollow : m);
            }
    }

    static void Cone(HexCanvas c, float cx, float cz, float r, int y0, int tall, byte m)
    {
        for (int k = 0; k < tall; k++)
        {
            float rr = r * (1 - k / (float)tall) + .35f;
            Drum(c, cx, cz, rr, y0 + k, y0 + k + 1, m);
        }
    }

    static void Blob(HexCanvas c, float cx, float cy, float cz, float rx, float ry, float rz, byte m, byte m2 = 0, int seed = 0)
    {
        for (int y = (int)(cy - ry); y <= (int)(cy + ry); y++)
            for (int z = (int)(cz - rz); z <= (int)(cz + rz); z++)
                for (int x = (int)(cx - rx); x <= (int)(cx + rx); x++)
                {
                    float dx = (x + .5f - cx) / rx, dy = (y + .5f - cy) / ry, dz = (z + .5f - cz) / rz;
                    float d = dx * dx + dy * dy + dz * dz;
                    if (d > 1 + (Hash.H3(x, y, z + seed) - .5) * .35) continue;
                    c.Put(x, y, z, m2 != 0 && Hash.H3(x + seed, y, z) < .3 ? m2 : m);
                }
    }

    /// <summary>A flag on its pole: blue for ours, red for the horde's.</summary>
    static void Flag(HexCanvas c, int x, int y, int z, byte owner, int pole = 7)
    {
        Box(c, x, y, z, x + 1, y + pole, z + 1, Mat.Wood);
        byte cloth = owner == 1 ? Mat.ClothBlue : owner == 2 ? Mat.ClothRed : Mat.ClothWhite;
        Box(c, x + 1, y + pole - 3, z, x + 5, y + pole, z + 1, cloth);
        Box(c, x + 1, y + pole - 3, z, x + 5, y + pole - 2, z + 1, Mat.BannerTrim);
        c.Put(x, y + pole, z, Mat.Brass);
    }

    static void Tree(HexCanvas c, int x, int z, int y, int kind, double size, int seed)
    {
        switch (kind)
        {
            case 0:
            {
                // a broadleaf: a short trunk, a round crown
                int t = 2 + (int)(size * 2);
                Box(c, x, y, z, x + 1, y + t + 1, z + 1, Mat.Trunk);
                float r = 2f + (float)size * .9f;
                Blob(c, x + .5f, y + t + r * .8f, z + .5f, r, r * .9f, r, Mat.Foliage, Mat.Leaf, seed);
                Blob(c, x + .5f + ((seed & 1) == 0 ? 1.3f : -1.3f), y + t + r * 1.2f, z + .5f + ((seed & 2) == 0 ? 1f : -1f), r * .7f, r * .7f, r * .7f, Mat.FoliageLight, Mat.Foliage, seed + 3);
                break;
            }
            case 1:
            {
                // a fir: a cone of needles in tiers
                int t = 1, tall = 7 + (int)(size * 4);
                Box(c, x, y, z, x + 1, y + t + tall - 1, z + 1, Mat.PineBark);
                for (int k = 0; k < tall; k++)
                {
                    float r = 2.9f * (1 - k / (float)tall) + .3f + ((k % 3) == 0 ? .5f : 0);
                    for (int dz = -4; dz <= 4; dz++)
                        for (int dx = -4; dx <= 4; dx++)
                            if (dx * dx + dz * dz <= r * r) c.Put(x + dx, y + t + k, z + dz, Hash.H3(x + dx, k, z + dz) < .25 ? Mat.NeedleLight : Mat.Leaf);
                }
                break;
            }
            case 2:
            {
                // a fir under snow
                int t = 2, tall = 8 + (int)(size * 3);
                Box(c, x, y, z, x + 1, y + t + tall - 1, z + 1, Mat.PineBark);
                for (int k = 0; k < tall; k++)
                {
                    float r = 3f * (1 - k / (float)tall) + .5f;
                    for (int dz = -4; dz <= 4; dz++)
                        for (int dx = -4; dx <= 4; dx++)
                            if (dx * dx + dz * dz <= r * r) c.Put(x + dx, y + t + k, z + dz, (k & 1) == 1 && dx * dx + dz * dz >= (r - 1) * (r - 1) ? Mat.Snow : Mat.Leaf);
                }
                break;
            }
            case 3:
            {
                // a willow of the swamp: a stooped crown hanging low
                int t = 3;
                Box(c, x, y, z, x + 1, y + t + 1, z + 1, Mat.Trunk);
                Blob(c, x + .5f, y + t + 2, z + .5f, 3.4f, 2f, 3.4f, Mat.FoliageLight, Mat.Foliage, seed);
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.PI / 4 + seed;
                    int hx = x + (int)Math.Round(Math.Cos(a) * 3), hz = z + (int)Math.Round(Math.Sin(a) * 3);
                    Box(c, hx, y + 2, hz, hx + 1, y + t + 1, hz + 1, Mat.FoliageLight);
                }
                break;
            }
            case 4:
            {
                // dead and black: the horde's land
                int t = 6 + (int)(size * 3);
                Box(c, x, y, z, x + 1, y + t, z + 1, Mat.Snag);
                int bx = (seed & 1) == 0 ? 1 : -1;
                c.Put(x + bx, y + t - 2, z, Mat.Snag); c.Put(x + 2 * bx, y + t - 1, z, Mat.Snag);
                c.Put(x, y + t - 3, z - bx, Mat.Snag);
                break;
            }
            default:
            {
                // an autumn birch
                int t = 4 + (int)(size * 2);
                Box(c, x, y, z, x + 1, y + t, z + 1, Mat.Birch);
                Blob(c, x + .5f, y + t + 1.5f, z + .5f, 2.2f, 2.6f, 2.2f, Mat.FoliageGold, Mat.FoliageLight, seed);
                break;
            }
        }
    }

    // ------------------------------------------------------------ the relief

    /// <summary>The trees of a wood, the boulders of hills, a bush or a stone now and then on open land.</summary>
    public static void Relief(HexCanvas c, in HexLook l, float cx, float cz, int col, int row)
    {
        if (l.Ground is HexGround.Water or HexGround.DeepWater || l.Relief == HexRelief.Mountain) return;
        bool thing = l.Icon != HexIcon.None && l.Icon != HexIcon.Monster;
        int vr = HexTiles.Vary(col, row), seed = col * 7919 + row * 104729 + vr;
        if (l.Relief == HexRelief.Forest)
        {
            int want = 9 + vr % 5, put = 0;
            Span<(float x, float z)> at = stackalloc (float, float)[16];
            for (int k = 0; k < 90 && put < want; k++)
            {
                double a = Hash.H3(seed, k, 1) * Math.Tau, d = Math.Sqrt(Hash.H3(seed, k, 2)) * HexMapLayout.R * .84;
                float x = cx + (float)(Math.Cos(a) * d), z = cz + (float)(Math.Sin(a) * d);
                if (HexMapLayout.EdgeDistance(x - cx, z - cz) < 2.4f) continue;
                if (thing && d < 11) continue;
                if (Near(l.Roads, x - cx, z - cz) < 3.4f || Near(l.Rivers, x - cx, z - cz) < 4f) continue;
                bool crowded = false;
                for (int i = 0; i < put; i++) if ((at[i].x - x) * (at[i].x - x) + (at[i].z - z) * (at[i].z - z) < 14) { crowded = true; break; }
                if (crowded) continue;
                at[put++] = (x, z);
                int kind = l.Ground switch
                {
                    HexGround.Snow => 2,
                    HexGround.Swamp => Hash.H3(seed, k, 3) < .7 ? 3 : 4,
                    HexGround.Scorched or HexGround.Lava => 4,
                    HexGround.Rough => 1,
                    HexGround.Dirt => Hash.H3(seed, k, 3) < .45 ? 5 : 0,
                    HexGround.Sand => Hash.H3(seed, k, 3) < .5 ? 5 : 0,
                    _ => Hash.H3(seed, k, 3) < .55 ? 1 : 0,
                };
                int ix = (int)x, iz = (int)z;
                Tree(c, ix, iz, c.Ground(ix, iz), kind, Hash.H3(seed, k, 4) * .8, seed + k);
            }
            return;
        }
        if (thing) return;
        if (l.Relief == HexRelief.Hills)
        {
            for (int k = 0; k < 1 + vr % 2; k++)
            {
                double a = Hash.H3(seed, k, 5) * Math.Tau;
                int x = (int)(cx + Math.Cos(a) * 9), z = (int)(cz + Math.Sin(a) * 9);
                if (Near(l.Roads, x - cx, z - cz) < 3 || Near(l.Rivers, x - cx, z - cz) < 4) continue;
                Blob(c, x + .5f, c.Ground(x, z) + .3f, z + .5f, 2f, 1.6f, 1.8f, l.Ground == HexGround.Snow ? Mat.Snow : Mat.Rock, Mat.StoneDark, seed);
            }
            return;
        }
        // open land: a bush or a stone in one hex of three, flowers on a meadow
        if (vr % 3 == 0 && l.Ground is not (HexGround.Lava or HexGround.Scorched))
        {
            double a = Hash.H3(seed, 0, 6) * Math.Tau;
            int x = (int)(cx + Math.Cos(a) * 10), z = (int)(cz + Math.Sin(a) * 10);
            if (Near(l.Roads, x - cx, z - cz) > 3 && Near(l.Rivers, x - cx, z - cz) > 4 && HexMapLayout.EdgeDistance(x - cx, z - cz) > 3)
            {
                int y = c.Ground(x, z);
                if (l.Ground is HexGround.Grass or HexGround.Dirt or HexGround.Swamp) Blob(c, x + .5f, y + .8f, z + .5f, 1.8f, 1.4f, 1.8f, Mat.Foliage, Mat.FoliageLight, seed);
                else Blob(c, x + .5f, y + .3f, z + .5f, 1.5f, 1.2f, 1.4f, l.Ground == HexGround.Snow ? Mat.Snow : Mat.Rock, Mat.StoneDark, seed);
            }
        }
    }

    static float Near(byte sides, float dx, float dz)
    {
        float best = float.MaxValue;
        for (int s = 0; s < 6; s++)
        {
            if ((sides & (1 << s)) == 0) continue;
            var (tx, tz) = HexMapLayout.Toward(s);
            float vx = tx * .51f, vz = tz * .51f;
            float t = Math.Clamp((dx * vx + dz * vz) / (vx * vx + vz * vz), 0, 1);
            float ex = dx - vx * t, ez = dz - vz * t;
            best = MathF.Min(best, MathF.Sqrt(ex * ex + ez * ez));
        }
        return best;
    }

    // ------------------------------------------------------------ the things

    /// <summary>
    /// A hex's thing at its middle (cx, cz), on its ground (y the first voxel over it), as unlike others of its kind as
    /// <paramref name="vary"/> says. Spent, it shows it: a windmill's sails gone, a water wheel's wheel, a treasure's
    /// bones and its fire out.
    /// </summary>
    public static void Thing(HexCanvas c, in HexLook l, float fx, float fz, int y, int vary)
    {
        int x = (int)fx, z = (int)fz;
        int seed = vary * 31 + x;
        byte own = l.Owner;
        bool spent = HexTiles.Spent(l);
        switch (l.Icon)
        {
            case HexIcon.Citadel: Citadel(c, x, y, z, own); break;
            case HexIcon.Outpost:
                Drum(c, x + .5f, z + .5f, 3.2f, y, y + 11, Mat.Stone);
                Drum(c, x + .5f, z + .5f, 3.8f, y + 11, y + 12, Mat.StoneLight);
                Cone(c, x + .5f, z + .5f, 3.6f, y + 12, 6, Mat.Slate);
                Box(c, x, y, z + 3, x + 1, y + 3, z + 4, Mat.Wood);
                Stakes(c, x + .5f, z + .5f, 7, y, 3, Mat.Wood, gateAt: 1);
                Flag(c, x, y + 18, z, Math.Max(own, (byte)1), 5);
                break;
            case HexIcon.OutpostPlace:
                Stakes(c, x + .5f, z + .5f, 6, y, 2, Mat.Wood, gateAt: -1, every: 4);
                Box(c, x, y, z, x + 2, y + 3, z + 2, Mat.Menhir);
                break;
            case HexIcon.HordeTown: HordeTown(c, x, y, z, own, seed); break;
            case HexIcon.Lair:
                Blob(c, x + .5f, y + 2, z - 1.5f, 8, 7, 6.5f, Mat.Basalt, Mat.Rock, seed);
                Box(c, x - 2, y, z + 2, x + 3, y + 5, z + 6, Mat.Air);
                Box(c, x - 3, y + 5, z + 4, x + 4, y + 6, z + 6, Mat.Char);
                for (int k = 0; k < 6; k++) c.Put(x - 5 + (int)(Hash.H3(seed, k, 1) * 11), y, z + 6 + (int)(Hash.H3(seed, k, 2) * 4), Mat.BonePile);
                // a skull on a pole by the mouth
                Box(c, x + 5, y, z + 6, x + 6, y + 7, z + 7, Mat.Char);
                Box(c, x + 4, y + 7, z + 6, x + 7, y + 9, z + 8, Mat.Bone);
                c.Put(x + 4, y + 8, z + 7, Mat.Char); c.Put(x + 6, y + 8, z + 7, Mat.Char);
                Flag(c, x - 6, y, z + 5, Math.Max(own, (byte)2), 8);
                break;
            case HexIcon.MineLogs:
                // a sawmill: a shed, its logs, a blade
                Box(c, x - 6, y, z - 4, x + 2, y + 5, z + 2, Mat.Board);
                Box(c, x - 4, y, z + 2, x - 2, y + 3, z + 3, Mat.Air);
                Roof(c, x - 7, z - 5, x + 3, z + 3, y + 5, Mat.Shingle);
                for (int k = 0; k < 3; k++) Box(c, x + 3, y + k, z - 3 + k, x + 9, y + k + 1, z - 1 + k, Mat.Trunk);
                Box(c, x + 3, y, z + 2, x + 9, y + 1, z + 4, Mat.Lumber);
                Drum(c, x - 2, z + 4.5f, 1.6f, y + 1, y + 2, Mat.Steel);
                Flag(c, x - 6, y + 9, z - 4, own, 4);
                break;
            case HexIcon.MineStone:
                // a quarry: a cliff cut in steps, blocks stacked
                Blob(c, x - 2, y + 1, z - 3, 8, 6, 5.5f, Mat.Stone, Mat.StoneLight, seed);
                Box(c, x - 4, y + 2, z, x + 3, y + 7, z + 3, Mat.Air);
                Box(c, x - 4, y, z + 1, x + 3, y + 2, z + 3, Mat.Air);
                for (int k = 0; k < 4; k++) { int bx = x + 4 + (k & 1) * 3, bz = z + 2 + (k >> 1) * 3; Box(c, bx, y, bz, bx + 2, y + 2, bz + 2, Mat.StoneLight); }
                Box(c, x + 5, y + 2, z + 3, x + 7, y + 4, z + 5, Mat.Stone);
                Flag(c, x - 7, y, z + 3, own, 8);
                break;
            case HexIcon.MineMetal: Mine(c, x, y, z, own, Mat.OreRust, Mat.IronOre, seed); break;
            case HexIcon.MineGold: Mine(c, x, y, z, own, Mat.Nugget, Mat.Brass, seed); break;
            case HexIcon.MineCrystal:
                Mine(c, x, y, z, own, Mat.Crystal, Mat.Crystal, seed);
                for (int k = 0; k < 5; k++)
                {
                    int sx = x - 6 + (int)(Hash.H3(seed, k, 3) * 12), sz = z - 6 + (int)(Hash.H3(seed, k, 4) * 5), h = 3 + (int)(Hash.H3(seed, k, 5) * 4);
                    int gy = TopOf(c, sx, sz, y);
                    Box(c, sx, gy, sz, sx + 1, gy + h, sz + 1, Mat.Crystal);
                }
                break;
            case HexIcon.MineGems:
                // a pond with gems about it and a hut
                Drum(c, x + .5f, z + 1.5f, 5.5f, y - 1, y, Mat.Water);
                Drum(c, x + .5f, z + 1.5f, 6.5f, y - 2, y - 1, Mat.Sand);
                for (int k = 0; k < 9; k++)
                {
                    double a = k * Math.Tau / 9;
                    int gx = x + (int)Math.Round(Math.Cos(a) * 6.8), gz = z + 1 + (int)Math.Round(Math.Sin(a) * 6.8);
                    c.Put(gx, y, gz, k % 3 == 0 ? Mat.Crystal : Mat.Gem);
                }
                Hut(c, x - 9, y, z - 7, Mat.Board, Mat.Thatch);
                Flag(c, x + 7, y, z - 5, own, 7);
                break;
            case HexIcon.MineMercury:
                // the alchemist's lab: a hut with a crooked chimney and a pool of quicksilver
                Hut(c, x - 5, y, z - 5, Mat.Daub, Mat.Slate);
                Box(c, x, y + 4, z - 4, x + 1, y + 10, z - 3, Mat.Brass);
                Drum(c, x + 4.5f, z + 4.5f, 3.2f, y - 1, y, Mat.Quicksilver);
                Drum(c, x + 4.5f, z + 4.5f, 3.6f, y, y + 1, Mat.Stone, Mat.Air);
                Box(c, x - 6, y, z + 3, x - 5, y + 2, z + 4, Mat.Phial);
                Flag(c, x - 6, y, z - 7, own, 8);
                break;
            case HexIcon.MineSulfur:
                Blob(c, x + .5f, y - 1, z + .5f, 8.5f, 5, 7.5f, Mat.Sulphur, Mat.Sand, seed);
                for (int k = 0; k < 3; k++)
                {
                    int vx = x - 4 + k * 4, vz = z - 2 + (k & 1) * 4, gy = TopOf(c, vx, vz, y);
                    Drum(c, vx + .5f, vz + .5f, 1.4f, gy - 1, gy, Mat.Char);
                }
                Flag(c, x + 8, y, z + 5, own, 8);
                break;
            case HexIcon.Pile: Pile(c, x, y, z, vary); break;
            case HexIcon.Chest:
                Box(c, x - 2, y, z - 1, x + 3, y + 3, z + 2, Mat.Board);
                Box(c, x - 2, y + 3, z - 1, x + 3, y + 4, z + 2, Mat.BoardDark);
                Box(c, x - 2, y + 1, z - 1, x + 3, y + 2, z + 2, Mat.Brass);
                Box(c, x, y + 2, z + 2, x + 1, y + 3, z + 3, Mat.Brass);
                Box(c, x - 3, y - 1, z - 2, x + 4, y, z + 3, Mat.StoneLight);
                break;
            case HexIcon.Campfire:
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.Tau / 8;
                    c.Put(x + (int)Math.Round(Math.Cos(a) * 2.6), y, z + (int)Math.Round(Math.Sin(a) * 2.6), Mat.Stone);
                }
                Box(c, x, y, z, x + 1, y + 1, z + 1, Mat.Coals);
                c.Put(x, y + 1, z, Mat.Flame);
                Box(c, x - 6, y, z + 3, x - 3, y + 1, z + 4, Mat.Trunk);
                Box(c, x + 4, y, z - 4, x + 5, y + 1, z - 1, Mat.Trunk);
                Box(c, x + 3, y, z + 4, x + 5, y + 2, z + 6, Mat.Sack);
                break;
            case HexIcon.Windmill:
                Drum(c, x + .5f, z + .5f, 3, y, y + 9, Mat.Daub);
                Cone(c, x + .5f, z + .5f, 3.6f, y + 9, 5, Mat.Thatch);
                Box(c, x, y, z + 3, x + 1, y + 3, z + 4, Mat.Wood);
                // the sails, a cross before its south face (gone once its week's flour is given)
                for (int k = -7; k <= 7 && !spent; k++)
                {
                    c.Put(x + k, y + 9 + (int)(k * .2f), z + 4, Mat.Plank);
                    c.Put(x - (int)(k * .2f), y + 9 + k, z + 4, Mat.Plank);
                }
                Box(c, x, y + 9, z + 4, x + 1, y + 10, z + 5, Mat.Wood);
                break;
            case HexIcon.WaterWheel:
                Hut(c, x - 5, y, z - 4, Mat.Board, Mat.Shingle);
                for (int k = 0; k < 24 && !spent; k++)
                {
                    double a = k * Math.Tau / 24;
                    c.Put(x + 3, y + 3 + (int)Math.Round(Math.Sin(a) * 3.5), z + (int)Math.Round(Math.Cos(a) * 3.5), k % 3 == 0 ? Mat.Wood : Mat.Plank);
                }
                Box(c, x + 3, y + 3, z, x + 4, y + 4, z + 1, Mat.Wood);
                break;
            case HexIcon.Shrine:
                Box(c, x - 4, y, z - 4, x + 5, y + 1, z + 5, Mat.StoneLight);
                foreach (var (px, pz) in new[] { (-3, -3), (3, -3), (-3, 3), (3, 3) }) Box(c, x + px, y + 1, z + pz, x + px + 1, y + 7, z + pz + 1, Mat.StoneLight);
                Roof(c, x - 4, z - 4, x + 5, z + 5, y + 7, Mat.Slate);
                Box(c, x, y + 1, z, x + 1, y + 3, z + 1, Mat.Stone);
                c.Put(x, y + 3, z, Mat.SoulFire);
                break;
            case HexIcon.Dwelling:
                Hut(c, x - 4, y, z - 4, Mat.Board, Mat.Thatch);
                Stakes(c, x + .5f, z + .5f, 8, y, 2, Mat.Wood, gateAt: 1, every: 2);
                Flag(c, x + 5, y, z - 3, own, 8);
                break;
            case HexIcon.DeadMine:
                Blob(c, x + .5f, y + 1, z - 2.5f, 8, 6, 5.5f, Mat.Rock, Mat.StoneDark, seed);
                Box(c, x - 2, y, z, x + 3, y + 4, z + 3, Mat.Air);
                Box(c, x - 2, y, z + 2, x - 1, y + 4, z + 3, Mat.Snag); Box(c, x + 2, y, z + 2, x + 3, y + 3, z + 3, Mat.Snag);
                Box(c, x - 2, y + 3, z + 2, x + 2, y + 4, z + 3, Mat.Snag);
                Box(c, x - 1, y, z + 1, x + 2, y + 2, z + 3, Mat.Rubble);
                for (int k = 0; k < 4 && !spent; k++) c.Put(x - 4 + k * 3, y, z + 5 + (k & 1), Mat.Bone);
                break;
            case HexIcon.Ruins:
                for (int k = 0; k < 6; k++)
                {
                    double a = k * Math.Tau / 6 + .3;
                    int px = x + (int)Math.Round(Math.Cos(a) * 6.5), pz = z + (int)Math.Round(Math.Sin(a) * 6.5);
                    int h = 2 + (int)(Hash.H3(seed, k, 7) * 7);
                    Box(c, px, y, pz, px + 2, y + h, pz + 2, Mat.Ruin);
                }
                Box(c, x - 6, y, z - 6, x + 2, y + 3, z - 5, Mat.Ruin);
                Box(c, x - 6, y, z - 6, x - 5, y + 5, z + 1, Mat.Ruin);
                Box(c, x - 1, y, z - 1, x + 2, y + 1, z + 2, Mat.Rubble);
                break;
            case HexIcon.Den:
                Blob(c, x + .5f, y + 1, z - 2.5f, 8.5f, 6.5f, 6, Mat.Rock, Mat.Moss, seed);
                Box(c, x - 2, y, z + 1, x + 3, y + 4, z + 4, Mat.Air);
                for (int k = 0; k < 7 && !spent; k++) c.Put(x - 5 + (int)(Hash.H3(seed, k, 1) * 11), y, z + 4 + (int)(Hash.H3(seed, k, 2) * 4), k % 2 == 0 ? Mat.BonePile : Mat.Bone);
                break;
            case HexIcon.Trial:
                for (int k = 0; k < 7; k++)
                {
                    double a = k * Math.Tau / 7;
                    int px = x + (int)Math.Round(Math.Cos(a) * 7), pz = z + (int)Math.Round(Math.Sin(a) * 7);
                    Box(c, px, y, pz, px + 2, y + 6 + (k & 1), pz + 2, Mat.Menhir);
                }
                Box(c, x - 1, y, z - 1, x + 2, y + 2, z + 2, Mat.StoneDark);
                c.Put(x, y + 2, z, spent ? Mat.Char : Mat.SoulFire);
                break;
            case HexIcon.Signpost:
                Box(c, x, y, z, x + 1, y + 6, z + 1, Mat.Wood);
                Box(c, x - 2, y + 4, z + 1, x + 3, y + 6, z + 2, Mat.Signboard);
                break;
            case HexIcon.Learning:
                Box(c, x - 1, y, z - 1, x + 2, y + 9, z + 1, Mat.Menhir);
                for (int k = 1; k < 8; k += 2) c.Put(x + (k / 2 % 2), y + k, z + 1, Mat.Crystal);
                Box(c, x - 3, y - 1, z - 3, x + 4, y, z + 3, Mat.StoneLight);
                break;
            case HexIcon.Stables:
                Box(c, x - 7, y, z - 5, x + 3, y + 5, z, Mat.Board);
                Roof(c, x - 8, z - 6, x + 4, z + 1, y + 5, Mat.Thatch);
                Box(c, x - 4, y, z, x - 1, y + 4, z + 1, Mat.Air);
                Stakes(c, x + 4.5f, z + 4.5f, 5, y, 2, Mat.Wood, gateAt: -1, every: 2);
                Box(c, x + 6, y, z + 2, x + 8, y + 2, z + 4, Mat.Hay);
                break;
            case HexIcon.Observatory:
                Drum(c, x + .5f, z + .5f, 3.4f, y, y + 14, Mat.StoneLight);
                Blob(c, x + .5f, y + 14, z + .5f, 3.6f, 3.4f, 3.6f, Mat.Slate);
                Box(c, x + 1, y + 16, z, x + 5, y + 17, z + 1, Mat.Brass);
                Box(c, x, y, z + 3, x + 1, y + 3, z + 4, Mat.Wood);
                break;
            case HexIcon.Pass:
                // a gate across the way through the pass: two towers and a bar between them
                Drum(c, x - 4.5f, z + .5f, 2.4f, y, y + 10, Mat.StoneDark);
                Drum(c, x + 5.5f, z + .5f, 2.4f, y, y + 10, Mat.StoneDark);
                Cone(c, x - 4.5f, z + .5f, 2.6f, y + 10, 4, Mat.Slate);
                Cone(c, x + 5.5f, z + .5f, 2.6f, y + 10, 4, Mat.Slate);
                Box(c, x - 3, y + 7, z, x + 4, y + 9, z + 1, Mat.StoneDark);
                Box(c, x - 2, y, z, x + 3, y + 1, z + 1, Mat.Wood);
                Flag(c, x - 5, y + 14, z, own, 4);
                break;
        }
    }

    /// <summary>The first free voxel over a column near a thing (what was already put there counts).</summary>
    static int TopOf(HexCanvas c, int x, int z, int from)
    {
        int y = Math.Max(1, from - 3);
        while (y < HexTiles.SY - 1 && c.Get(x, y, z) != Mat.Air) y++;
        return y;
    }

    /// <summary>A gabled roof over a box from x0 to x1 (exclusive), its ridge running west to east.</summary>
    static void Roof(HexCanvas c, int x0, int z0, int x1, int z1, int y, byte m)
    {
        int half = (z1 - z0 + 1) / 2;
        for (int k = 0; k < half; k++) Box(c, x0, y + k, z0 + k, x1, y + k + 1, z1 - k, m);
    }

    static void Hut(HexCanvas c, int x0, int y, int z0, byte wall, byte roof)
    {
        Box(c, x0, y, z0, x0 + 7, y + 4, z0 + 6, wall);
        Box(c, x0 + 3, y, z0 + 6 - 1, x0 + 4, y + 3, z0 + 6, Mat.Air);
        Box(c, x0 + 3, y, z0 + 6 - 1, x0 + 4, y + 3, z0 + 6, Mat.Wood);
        Roof(c, x0 - 1, z0 - 1, x0 + 8, z0 + 7, y + 4, roof);
    }

    /// <summary>A ring of stakes, a gap for a gate (on the south, or none), so many voxels apart.</summary>
    static void Stakes(HexCanvas c, float cx, float cz, float r, int y, int tall, byte m, int gateAt, int every = 1)
    {
        int n = (int)(r * Math.Tau);
        for (int k = 0; k < n; k += every)
        {
            double a = k * Math.Tau / n;
            if (gateAt >= 0 && Math.Abs(a - Math.PI / 2) < .35) continue;
            int x = (int)MathF.Floor(cx + (float)Math.Cos(a) * r), z = (int)MathF.Floor(cz + (float)Math.Sin(a) * r);
            int gy = TopOf(c, x, z, y);
            Box(c, x, gy, z, x + 1, gy + tall + (k % 3 == 0 ? 1 : 0), z + 1, m);
        }
    }

    static void Citadel(HexCanvas c, int x, int y, int z, byte own)
    {
        const int h = 9;
        // the curtain and its merlons
        Box(c, x - 9, y, z - 9, x + 10, y + 6, z - 8, Mat.Stone); Box(c, x - 9, y, z + 9, x + 10, y + 6, z + 10, Mat.Stone);
        Box(c, x - 9, y, z - 9, x - 8, y + 6, z + 10, Mat.Stone); Box(c, x + 9, y, z - 9, x + 10, y + 6, z + 10, Mat.Stone);
        for (int k = -9; k <= 9; k += 2)
        {
            c.Put(x + k, y + 6, z - 9, Mat.StoneLight); c.Put(x + k, y + 6, z + 9, Mat.StoneLight);
            c.Put(x - 9, y + 6, z + k, Mat.StoneLight); c.Put(x + 9, y + 6, z + k, Mat.StoneLight);
        }
        // the gate, south
        Box(c, x - 1, y, z + 9, x + 2, y + 4, z + 10, Mat.Air);
        Box(c, x - 1, y, z + 9, x + 2, y + 3, z + 10, Mat.Gate);
        // the towers at the corners, under red roofs
        foreach (var (tx, tz) in new[] { (-9, -9), (9, -9), (-9, 9), (9, 9) })
        {
            Drum(c, x + tx + .5f, z + tz + .5f, 2.8f, y, y + h, Mat.StoneLight);
            Cone(c, x + tx + .5f, z + tz + .5f, 3.4f, y + h, 6, Mat.Tile);
        }
        // the keep
        Drum(c, x + .5f, z - .5f, 4.6f, y, y + 15, Mat.Stone);
        for (int k = 0; k < 12; k++)
        {
            double a = k * Math.Tau / 12;
            c.Put(x + (int)Math.Round(Math.Cos(a) * 4.3), y + 15, z + (int)Math.Round(Math.Sin(a) * 4.3) - 1, Mat.StoneLight);
        }
        Cone(c, x + .5f, z - .5f, 4.4f, y + 15, 8, Mat.Tile);
        for (int k = 0; k < 3; k++) c.Put(x, y + 6 + k * 3, z + 4, Mat.Window);
        Flag(c, x, y + 23, z - 1, Math.Max(own, (byte)1), 5);
        // the yard's well
        Drum(c, x - 5.5f, z + 5.5f, 1.4f, y, y + 1, Mat.Stone, Mat.Water);
    }

    static void HordeTown(HexCanvas c, int x, int y, int z, byte own, int seed)
    {
        Stakes(c, x + .5f, z + .5f, 11.5f, y, 5, Mat.Snag, gateAt: 1);
        // huts under dark thatch round a fire, a hall at the back
        for (int k = 0; k < 4; k++)
        {
            double a = Math.PI * (1.15 + k * .23);
            float hx = x + .5f + (float)Math.Cos(a) * 6.5f, hz = z + .5f + (float)Math.Sin(a) * 6.5f;
            Drum(c, hx, hz, 2.4f, y, y + 3, Mat.Char);
            Cone(c, hx, hz, 3, y + 3, 4, Mat.LeatherDark);
        }
        Box(c, x - 4, y, z - 9, x + 5, y + 4, z - 5, Mat.Char);
        Roof(c, x - 5, z - 10, x + 6, z - 4, y + 4, Mat.LeatherDark);
        Box(c, x, y, z, x + 1, y + 1, z + 1, Mat.Coals);
        c.Put(x, y + 1, z, Mat.Flame);
        for (int k = 0; k < 5; k++) c.Put(x - 4 + (int)(Hash.H3(seed, k, 1) * 9), y, z + 3 + (int)(Hash.H3(seed, k, 2) * 4), Mat.BonePile);
        // the totem and the banners
        Box(c, x + 6, y, z + 2, x + 7, y + 9, z + 3, Mat.Char);
        Box(c, x + 5, y + 9, z + 2, x + 8, y + 11, z + 4, Mat.Bone);
        Flag(c, x - 2, y + 8, z - 7, Math.Max(own, (byte)2), 6);
    }

    static void Mine(HexCanvas c, int x, int y, int z, byte own, byte ore, byte glint, int seed)
    {
        Blob(c, x + .5f, y + 1, z - 3, 8.5f, 6.5f, 6, Mat.Rock, Mat.StoneDark, seed);
        // the mouth, timbered
        Box(c, x - 2, y, z, x + 3, y + 4, z + 4, Mat.Air);
        Box(c, x - 2, y, z + 3, x - 1, y + 4, z + 4, Mat.Wood); Box(c, x + 2, y, z + 3, x + 3, y + 4, z + 4, Mat.Wood);
        Box(c, x - 3, y + 4, z + 3, x + 4, y + 5, z + 4, Mat.Wood);
        // the heap of what it gives, and a cart
        Blob(c, x + 6.5f, y, z + 5.5f, 2.6f, 2, 2.4f, ore, glint, seed);
        Box(c, x - 6, y, z + 5, x - 3, y + 2, z + 7, Mat.Board);
        c.Put(x - 6, y, z + 7, Mat.Iron); c.Put(x - 4, y, z + 7, Mat.Iron);
        Box(c, x - 6, y + 2, z + 5, x - 3, y + 3, z + 7, ore);
        Flag(c, x - 1, y + 8, z - 3, own, 5);
    }

    static void Pile(HexCanvas c, int x, int y, int z, int variant)
    {
        switch (variant % 8)
        {
            case 0: for (int k = 0; k < 3; k++) Box(c, x - 3 + k, y + k, z - 1, x + 4 - k, y + k + 1, z + 2, Mat.Trunk); break;
            case 1: Box(c, x - 2, y, z - 2, x + 2, y + 2, z + 2, Mat.StoneLight); Box(c, x - 1, y + 2, z - 1, x + 1, y + 3, z + 1, Mat.Stone); break;
            case 2: Blob(c, x + .5f, y, z + .5f, 2.6f, 2, 2.4f, Mat.OreRust, Mat.IronOre, variant); break;
            case 3: Blob(c, x + .5f, y, z + .5f, 2.2f, 1.6f, 2.2f, Mat.Nugget, Mat.Brass, variant); break;
            case 4: Blob(c, x + .5f, y, z + .5f, 2.4f, 1.8f, 2.2f, Mat.Sulphur, Mat.Sand, variant); break;
            case 5: Box(c, x - 1, y, z - 1, x + 1, y + 2, z + 1, Mat.Phial); Box(c, x + 1, y, z, x + 2, y + 1, z + 1, Mat.Quicksilver); break;
            case 6: for (int k = 0; k < 4; k++) c.Put(x - 1 + (k & 1) * 2, y + (k >> 1), z - 1 + (k >> 1), Mat.Crystal); Box(c, x, y, z, x + 1, y + 3, z + 1, Mat.Crystal); break;
            default: Box(c, x - 1, y, z - 1, x + 2, y + 1, z + 2, Mat.Sack); c.Put(x, y + 1, z, Mat.Gem); c.Put(x + 1, y + 1, z, Mat.Gem); break;
        }
    }
}
