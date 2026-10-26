using System;

namespace VoxelTower.Core;

/// <summary>
/// The things of the big world map, three voxels to a unit, each to be known from the camera of «Heroes» at a glance and
/// to stand up to a closer look: our castle of pale stone, its towers' spires red, its keep tall with banners down its
/// face and a little town about its walls; the horde's town of hide huts and black timber in a ring of sharpened stakes
/// round its fire, totems of skulls, red rags; a sawmill with its saw and its logs, a quarry cut in steps with a crane, an
/// iron mine with rails and a cart and a furnace, a gold mine under a headframe, an alchemist's tower over a pool of
/// quicksilver, a dune of sulphur steaming, crystals bursting from a crag, a pond ringed with gems; piles of what the map
/// gives, a chest bound in brass, a campfire with its pot, a windmill and a water mill (their sails and wheel turn on
/// their own, MapModels.Sails/Wheel), a small temple, a dwelling with its pen, the dead mine, ruins, a beasts' den, a ring
/// of black stones, a signpost, a stone of learning, stables with horses, an observatory, a gate across a pass; and the
/// fields, ricks, houses and sheep of the farms about our castle.
/// </summary>
public static partial class MapModels
{
    static void Thing(ModelCanvas c, MapModel m, int v, byte own, bool spent)
    {
        switch (m)
        {
            case MapModel.Citadel: Citadel(c, own, v); break;
            case MapModel.Outpost: Outpost(c, own); break;
            case MapModel.OutpostPlace: OutpostPlace(c); break;
            case MapModel.HordeTown: HordeTown(c, own, v); break;
            case MapModel.Lair: Lair(c, own); break;
            case MapModel.Sawmill: Sawmill(c, own); break;
            case MapModel.Quarry: Quarry(c, own); break;
            case MapModel.OreMine: OreMine(c, own, gold: false); break;
            case MapModel.GoldMine: OreMine(c, own, gold: true); break;
            case MapModel.Alchemist: Alchemist(c, own); break;
            case MapModel.SulfurDune: SulfurDune(c, own); break;
            case MapModel.CrystalCave: CrystalCave(c, own); break;
            case MapModel.GemPond: GemPond(c, own); break;
            case MapModel.Pile: Pile(c, v); break;
            case MapModel.Chest: Chest(c); break;
            case MapModel.Campfire: Campfire(c, spent); break;
            case MapModel.Windmill: Windmill(c, own, spent); break;
            case MapModel.WaterMill: WaterMill(c, own); break;
            case MapModel.Shrine: Shrine(c); break;
            case MapModel.Dwelling: Dwelling(c, own); break;
            case MapModel.DeadMine: DeadMine(c, spent); break;
            case MapModel.Ruins: Ruins(c, spent); break;
            case MapModel.Den: Den(c, spent); break;
            case MapModel.Trial: Trial(c, spent); break;
            case MapModel.Signpost: Signpost(c); break;
            case MapModel.Learning: Learning(c); break;
            case MapModel.Stables: Stables(c); break;
            case MapModel.Observatory: Observatory(c); break;
            case MapModel.Pass: Pass(c, own); break;
            case MapModel.Totem: Totem(c, v); break;
            case MapModel.BoneHeap: BoneHeap(c, 0, 0, 1.4f); break;
            case MapModel.Field: Field(c, v); break;
            case MapModel.Haystack: Haystack(c, 0, 0); break;
            case MapModel.Farmhouse: Farmhouse(c, v); break;
            case MapModel.Sheep: Sheep(c, 0, 0, v); break;
            case MapModel.Scarecrow: Scarecrow(c); break;
            case MapModel.Sails: Sails(c); break;
            case MapModel.Wheel: Wheel(c); break;
        }
    }

    // ------------------------------------------------------------ the parts things are made of

    /// <summary>Merlons along the top of a wall from (x0, z0) to (x1, z1) (one of them the same), a unit high, a unit and a gap.</summary>
    static void Merlons(ModelCanvas c, float x0, float z0, float x1, float z1, float y, byte m, float thick = .6f)
    {
        bool alongX = MathF.Abs(x1 - x0) > MathF.Abs(z1 - z0);
        float len = alongX ? x1 - x0 : z1 - z0;
        for (float t = 0; t < len - .2f; t += 1.34f)
        {
            if (alongX) c.Box(x0 + t, y, z0 - thick / 2, x0 + t + .67f, y + .9f, z0 + thick / 2, m);
            else c.Box(x0 - thick / 2, y, z0 + t, x0 + thick / 2, y + .9f, z0 + t + .67f, m);
        }
    }

    /// <summary>A round tower: its wall, a band of corbels under its top, merlons, a spire of its own stuff so tall, a gilt knob.</summary>
    static void RoundTower(ModelCanvas c, float cx, float cz, float r, float h, byte wall, byte roof, float spire, byte band = 0)
    {
        c.Cyl(cx, cz, r, -3, h, wall);
        c.Paint(cx - r, -3, cz - r, cx + r, h, cz + r, band != 0 ? band : Mat.Stone, .22, 13);
        c.Cyl(cx, cz, r + .45f, h - .9f, h, Mat.StoneLight);
        for (int k = 0; k < 10; k++)
        {
            double a = k * Math.Tau / 10;
            float x = cx + (float)Math.Cos(a) * (r + .1f), z = cz + (float)Math.Sin(a) * (r + .1f);
            c.Box(x - .33f, h, z - .33f, x + .33f, h + .8f, z + .33f, Mat.StoneLight);
        }
        if (spire > 0)
        {
            c.Cone(cx, cz, r + .4f, h + .3f, spire, roof, .15f);
            c.Rod(cx, h + spire, cz, cx, h + spire + 1.4f, cz, .12f, Mat.Brass);
            c.Ball(cx, h + spire + .5f, cz, .3f, .3f, .3f, Mat.Brass, rough: 0);
        }
        // a slit of a window, lit
        c.Box(cx - .2f, h * .55f, cz + r - .3f, cx + .2f, h * .55f + 1.1f, cz + r + .05f, Mat.Window);
    }

    /// <summary>A house: walls from (x0, z0) to (x1, z1) so high, a gabled roof (ridge along the longer side), a door to the south, windows, a chimney.</summary>
    static void House(ModelCanvas c, float x0, float z0, float x1, float z1, float h, byte wall, byte roof, bool chimney = true, byte trim = 0)
    {
        c.Box(x0, -2, z0, x1, h, z1, wall);
        if (trim != 0)
        {
            // timbers framing the walls: corner posts, a sill, a beam under the eaves
            foreach (var x in new[] { x0, x1 - .34f }) foreach (var z in new[] { z0, z1 - .34f }) c.Box(x, 0, z, x + .34f, h, z + .34f, trim);
            c.Box(x0, h - .34f, z1 - .1f, x1, h, z1 + .05f, trim);
            c.Box(x0, h * .45f, z1 - .1f, x1, h * .45f + .3f, z1 + .05f, trim);
        }
        bool alongX = x1 - x0 >= z1 - z0;
        c.Gable(x0, z0, x1, z1, h, roof, alongX, 1.05f, .55f, wall);
        float mx = (x0 + x1) / 2;
        c.Box(mx - .5f, 0, z1 - .1f, mx + .5f, 1.9f, z1 + .05f, Mat.BoardDark);
        for (float x = x0 + 1; x < x1 - 1; x += 2.1f)
            if (MathF.Abs(x + .35f - mx) > 1) c.Box(x, h * .55f, z1 - .1f, x + .7f, h * .55f + .8f, z1 + .05f, Mat.Window);
        if (chimney)
        {
            float cx = x1 - 1.1f, cz = (z0 + z1) / 2 - (alongX ? .6f : 0);
            c.Box(cx - .45f, h, cz - .45f, cx + .45f, h + (alongX ? (z1 - z0) / 2 : (x1 - x0) / 2) + 1.6f, cz + .45f, Mat.Stone);
        }
    }

    /// <summary>A banner hanging down a wall's face to the south: cloth, a gilt hem and a gilt mark.</summary>
    static void Hanging(ModelCanvas c, float x, float y, float z, byte own, float w = 1.3f, float len = 4)
    {
        byte cloth = own == 2 ? Mat.ClothRed : Mat.Banner;
        c.Box(x - w / 2 - .2f, y, z, x + w / 2 + .2f, y + .34f, z + .34f, Mat.Wood);
        c.Box(x - w / 2, y - len, z, x + w / 2, y, z + .34f, cloth);
        c.Box(x - w / 2, y - len, z + .01f, x + w / 2, y - len + .34f, z + .36f, Mat.BannerTrim);
        c.Box(x - .2f, y - len * .5f, z + .2f, x + .2f, y - len * .5f + .67f, z + .45f, Mat.BannerTrim);
        // its tail cut in a point
        c.Box(x - w / 4, y - len - .34f, z, x + w / 4, y - len, z + .34f, cloth);
    }

    /// <summary>Logs laid one on another, a pyramid of them, so long, along x or z.</summary>
    static void Logs(ModelCanvas c, float cx, float cz, int rows, float len, bool alongX = true, float r = .5f)
    {
        for (int row = 0; row < rows; row++)
        {
            int n = rows - row;
            for (int k = 0; k < n; k++)
            {
                float o = (k - (n - 1) / 2f) * r * 2.05f, y = r + row * r * 1.75f;
                if (alongX) c.Rod(cx - len / 2, y, cz + o, cx + len / 2, y, cz + o, r, Mat.Trunk);
                else c.Rod(cx + o, y, cz - len / 2, cx + o, y, cz + len / 2, r, Mat.Trunk);
                // the cut ends pale
                if (alongX) { c.Paint(cx + len / 2 - .34f, y - r, cz + o - r, cx + len / 2 + .2f, y + r, cz + o + r, Mat.Lumber); }
                else c.Paint(cx + o - r, y - r, cz + len / 2 - .34f, cx + o + r, y + r, cz + len / 2 + .2f, Mat.Lumber);
            }
        }
    }

    static void Barrel(ModelCanvas c, float x, float z, float h = 1.5f)
    {
        c.Cyl(x, z, .6f, 0, h, Mat.Barrel);
        c.Cyl(x, z, .64f, h * .2f, h * .2f + .3f, Mat.Hoop);
        c.Cyl(x, z, .64f, h * .75f, h * .75f + .3f, Mat.Hoop);
    }

    static void Crate(ModelCanvas c, float x, float z, float s = 1.1f, float y = 0)
    {
        c.Box(x - s / 2, y, z - s / 2, x + s / 2, y + s, z + s / 2, Mat.Crate);
        c.Box(x - s / 2 - .05f, y, z + s / 2 - .2f, x + s / 2 + .05f, y + .3f, z + s / 2 + .05f, Mat.CrateEdge);
        c.Box(x - s / 2 - .05f, y + s - .3f, z + s / 2 - .2f, x + s / 2 + .05f, y + s, z + s / 2 + .05f, Mat.CrateEdge);
    }

    /// <summary>A cart: its box on two wheels, its shafts to the south; what it carries heaped in it.</summary>
    static void Cart(ModelCanvas c, float x, float z, byte load, float y = 0)
    {
        c.Box(x - 1.3f, y + 1, z - .9f, x + 1.3f, y + 1.9f, z + .9f, Mat.Board);
        c.Box(x - 1.1f, y + 1.3f, z - .7f, x + 1.1f, y + 1.9f, z + .7f, Mat.Air);
        c.Rod(x - .4f, y + 1.2f, z + .9f, x - .4f, y + .7f, z + 3, .14f, Mat.Wood);
        c.Rod(x + .4f, y + 1.2f, z + .9f, x + .4f, y + .7f, z + 3, .14f, Mat.Wood);
        foreach (var s in new[] { -1, 1 })
        {
            c.Rod(x + s * 1.45f, y + .9f, z, x + s * 1.7f, y + .9f, z, .9f, Mat.BoardDark);
            c.Rod(x + s * 1.75f, y + .9f, z, x + s * 1.8f, y + .9f, z, .3f, Mat.Iron);
        }
        if (load != 0) c.Ball(x, y + 1.9f, z, 1.1f, .6f, .7f, load, Mat.Air, 0, .25f);
    }

    /// <summary>A fire: a ring of stones, logs crossed, the coals, the flames.</summary>
    static void Fire(ModelCanvas c, float x, float z, float s = 1, bool lit = true)
    {
        for (int k = 0; k < 9; k++)
        {
            double a = k * Math.Tau / 9;
            float sx = x + (float)Math.Cos(a) * 1.3f * s, sz = z + (float)Math.Sin(a) * 1.3f * s;
            c.Ball(sx, .2f, sz, .42f * s, .35f * s, .42f * s, Mat.Stone, Mat.StoneDark, .4, .2f);
        }
        c.Rod(x - .9f * s, .3f, z - .5f * s, x + .9f * s, .3f, z + .5f * s, .25f * s, Mat.Char);
        c.Rod(x - .9f * s, .3f, z + .5f * s, x + .9f * s, .3f, z - .5f * s, .25f * s, Mat.Char);
        if (!lit) { c.Cyl(x, z, .8f * s, 0, .3f, Mat.Ash); return; }
        c.Cyl(x, z, .7f * s, 0, .45f, Mat.Coals);
        c.Cone(x, z, .6f * s, .4f, 1.4f * s, Mat.Flame, 0);
        c.Cone(x + .3f * s, z - .2f * s, .35f * s, .4f, 1.9f * s, Mat.Flame, 0);
    }

    /// <summary>A torch on its post.</summary>
    static void Torch(ModelCanvas c, float x, float z, float h = 2.6f)
    {
        c.Box(x - .17f, -1, z - .17f, x + .17f, h, z + .17f, Mat.Wood);
        c.Box(x - .3f, h, z - .3f, x + .3f, h + .4f, z + .3f, Mat.Coals);
        c.Cone(x, z, .3f, h + .4f, .9f, Mat.Flame);
    }

    /// <summary>A fence of posts and two rails from one point to another.</summary>
    static void Fence(ModelCanvas c, float x0, float z0, float x1, float z1, byte m = Mat.Wood)
    {
        float len = MathF.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
        for (float t = 0; t <= len + .01f; t += 1.5f)
        {
            float x = x0 + (x1 - x0) * t / len, z = z0 + (z1 - z0) * t / len;
            c.Box(x - .17f, -1, z - .17f, x + .17f, 1.3f, z + .17f, m);
        }
        c.Rod(x0, .6f, z0, x1, .6f, z1, .1f, m);
        c.Rod(x0, 1.1f, z0, x1, 1.1f, z1, .1f, m);
    }

    /// <summary>A skull: bone, its sockets dark.</summary>
    static void Skull(ModelCanvas c, float x, float y, float z, float s = .5f)
    {
        c.Ball(x, y, z, s, s * .9f, s, Mat.Bone, rough: 0);
        c.Box(x - s * .55f, y - s * .1f, z + s * .6f, x - s * .1f, y + s * .35f, z + s * 1.05f, Mat.Char);
        c.Box(x + s * .1f, y - s * .1f, z + s * .6f, x + s * .55f, y + s * .35f, z + s * 1.05f, Mat.Char);
    }

    static void BoneHeap(ModelCanvas c, float x, float z, float s)
    {
        c.Ball(x, 0, z, s, s * .55f, s * .9f, Mat.BonePile, Mat.Bone, .4, .3f);
        Skull(c, x + s * .3f, s * .45f, z + s * .3f, .38f);
        c.Rod(x - s, .3f, z + .2f, x + s * .2f, .5f, z - s * .7f, .14f, Mat.Bone);
    }

    /// <summary>A crag: a mass of rock, lumpy, of two stones.</summary>
    static void Crag(ModelCanvas c, float x, float z, float rx, float ry, float rz, byte rock, byte rock2, float share = .35f)
    {
        c.Ball(x, 0, z, rx, ry, rz, rock, rock2, share, .1f);
        c.Ball(x - rx * .45f, ry * .2f, z + rz * .2f, rx * .55f, ry * .75f, rz * .6f, rock, rock2, share, .12f);
        c.Ball(x + rx * .5f, -ry * .1f, z + rz * .1f, rx * .5f, ry * .6f, rz * .6f, rock2, rock, share, .12f);
    }

    /// <summary>A mouth into a crag to the south: a dark passage, its lintel and posts of timber if <paramref name="propped"/>.</summary>
    static void Mouth(ModelCanvas c, float x, float z, float w, float h, float depth, bool propped, byte prop = Mat.Prop)
    {
        c.Cut(x - w / 2, 0, z - depth, x + w / 2, h, z + 8);
        c.Box(x - w / 2, 0, z - depth - .67f, x + w / 2, h, z - depth, Mat.Char);
        c.Box(x - w / 2 - .5f, -.5f, z - depth, x + w / 2 + .5f, 0, z + 1, Mat.Trodden);
        if (!propped) return;
        c.Box(x - w / 2, 0, z - .5f, x - w / 2 + .5f, h, z, prop);
        c.Box(x + w / 2 - .5f, 0, z - .5f, x + w / 2, h, z, prop);
        c.Box(x - w / 2 - .3f, h - .5f, z - .5f, x + w / 2 + .3f, h, z, prop);
    }

    // ------------------------------------------------------------ our castle

    static void Citadel(ModelCanvas c, byte own, int v)
    {
        own = Math.Max(own, (byte)1);
        // the rock it stands on, the yard paved
        c.Box(-12.5f, -4, -10.5f, 12.5f, .3f, 10.5f, Mat.Stone);
        c.Box(-10, .3f, -8.7f, 10, .5f, 8.7f, Mat.Cobble);
        // the curtain wall, a walk round its top behind merlons, a gate to the south
        const float H = 7;
        c.Box(-10, 0, -9.2f, 10, H, -7.9f, Mat.StoneLight);
        c.Box(-10, 0, 7.9f, 10, H, 9.2f, Mat.StoneLight);
        c.Box(-10.6f, 0, -9.2f, -9.3f, H, 9.2f, Mat.StoneLight);
        c.Box(9.3f, 0, -9.2f, 10.6f, H, 9.2f, Mat.StoneLight);
        c.Paint(-11, 0, -10, 11, H, 10, Mat.Keep, .3, 21);
        c.Paint(-11, 0, -10, 11, 1.4f, 10, Mat.Stone);
        Merlons(c, -10, 9.05f, 10, 9.05f, H, Mat.StoneLight);
        Merlons(c, -10, -9.05f, 10, -9.05f, H, Mat.StoneLight);
        Merlons(c, -10.45f, -9.2f, -10.45f, 9.2f, H, Mat.StoneLight);
        Merlons(c, 10.45f, -9.2f, 10.45f, 9.2f, H, Mat.StoneLight);
        // the four towers at the corners, their spires red and steep as in «Heroes»
        foreach (var (x, z) in new[] { (-10f, -8.6f), (10f, -8.6f), (-10f, 8.6f), (10f, 8.6f) })
            RoundTower(c, x, z, 2.5f, 11.5f, Mat.StoneLight, Mat.Tile, 8.5f);
        // the gatehouse: two square towers, an arch between, the portcullis, a banner over it
        foreach (var s in new[] { -1, 1 })
        {
            float gx = s * 3.3f;
            c.Box(gx - 1.6f, 0, 7.4f, gx + 1.6f, 10.5f, 10.3f, Mat.StoneLight);
            c.Paint(gx - 1.7f, 0, 7.3f, gx + 1.7f, 10.5f, 10.4f, Mat.Keep, .3, 23);
            Merlons(c, gx - 1.6f, 10.15f, gx + 1.6f, 10.15f, 10.5f, Mat.StoneLight);
            c.Pyramid(gx, 8.85f, 1.95f, 10.6f, 5.5f, Mat.Tile);
            c.Box(gx - .2f, 6.5f, 10.25f, gx + .2f, 7.6f, 10.4f, Mat.Window);
        }
        c.Box(-1.7f, 7, 7.4f, 1.7f, 9.6f, 10.3f, Mat.StoneLight);
        Merlons(c, -1.7f, 10.15f, 1.7f, 10.15f, 9.6f, Mat.StoneLight);
        c.Cut(-1.6f, 0, 7, 1.6f, 4.6f, 10.6f);
        c.Rod(0, 4.6f, 7, 0, 4.6f, 10.6f, 1.6f, Mat.Air);
        c.Box(-1.6f, 0, 8.6f, 1.6f, 5, 9, Mat.Iron);
        c.Rod(0, 4.6f, 7, 0, 4.6f, 8.6f, 1.6f, Mat.Air);
        c.Rod(0, 4.6f, 9, 0, 4.6f, 10.6f, 1.6f, Mat.Air);
        for (float x = -1.4f; x < 1.6f; x += .67f) c.Cut(x, .7f, 8.5f, x + .33f, 4.6f, 9.1f);
        c.Box(-1.6f, -.1f, 9.4f, 1.6f, .35f, 11.5f, Mat.DrawDeck);
        Hanging(c, 0, 9.4f, 10.3f, own, 1.6f, 3.2f);
        // the keep: tall, its corners turreted, a great spire, windows lit, banners down its face
        c.Box(-4.6f, 0, -7, 4.6f, 18, 1.4f, Mat.Keep);
        c.Paint(-4.7f, 0, -7.1f, 4.7f, 18, 1.5f, Mat.StoneLight, .25, 25);
        c.Paint(-4.7f, 0, -7.1f, 4.7f, 1.6f, 1.5f, Mat.Stone);
        c.Box(-4.9f, 17.4f, -7.3f, 4.9f, 18, 1.7f, Mat.StoneLight);
        Merlons(c, -4.6f, 1.6f, 4.6f, 1.6f, 18, Mat.StoneLight);
        Merlons(c, -4.6f, -7.2f, 4.6f, -7.2f, 18, Mat.StoneLight);
        foreach (var (x, z) in new[] { (-4.6f, -7f), (4.6f, -7f), (-4.6f, 1.4f), (4.6f, 1.4f) })
        {
            c.Cyl(x, z, 1.25f, 13, 20.5f, Mat.StoneLight);
            c.Cone(x, z, 1.6f, 20.5f, 5, Mat.Tile, .1f);
            c.Rod(x, 25.4f, z, x, 26.4f, z, .1f, Mat.Brass);
        }
        c.Pyramid(0, -2.8f, 4.1f, 18, 10, Mat.Tile);
        c.Rod(0, 28, -2.8f, 0, 29.5f, -2.8f, .15f, Mat.Brass);
        c.Flag(0, 29.2f, -2.8f, own, 4.5f, 3.4f);
        // dormer windows in the great spire
        c.Box(-.5f, 21, 1, .5f, 22.6f, 1.3f, Mat.Window);
        for (int row = 0; row < 3; row++)
            for (int k = -1; k <= 1; k++)
            {
                if (row == 0 && k == 0) continue;
                float wx = k * 2.6f, wy = 4.5f + row * 4.2f;
                c.Box(wx - .4f, wy, 1.35f, wx + .4f, wy + 1.4f, 1.55f, Mat.Window);
                c.Box(wx - .55f, wy + 1.4f, 1.3f, wx + .55f, wy + 1.75f, 1.65f, Mat.StoneLight);
            }
        c.Box(-1, 0, 1.35f, 1, 2.8f, 1.6f, Mat.Gate);
        Hanging(c, -2.6f, 16.5f, 1.4f, own, 1.2f, 4.6f);
        Hanging(c, 2.6f, 16.5f, 1.4f, own, 1.2f, 4.6f);
        // a hall along the west wall, the well, a smithy's lean-to by the east
        House(c, -8.9f, -6.5f, -5.6f, 3.5f, 4.5f, Mat.StoneWarm, Mat.Tile, chimney: true);
        c.Cyl(5.5f, 4.2f, .9f, 0, 1.1f, Mat.Stone, .35f, Mat.Water);
        c.Box(4.6f, 1.1f, 4.1f, 6.4f, 1.3f, 4.3f, Mat.Wood);
        c.Box(6.2f, 0, -5.6f, 9, 3.2f, -1.5f, Mat.Board);
        c.Gable(6.2f, -5.6f, 9, -1.5f, 3.2f, Mat.Slate, false, .9f, .4f, Mat.Board);
        Barrel(c, 7, 0.5f); Crate(c, 8.2f, .4f);
        // flags on the towers' spires toward the field
        c.Flag(-10, 20, 8.6f, own, 2.5f, 2.2f);
        c.Flag(10, 20, 8.6f, own, 2.5f, 2.2f);
        // the little town outside the walls: houses to east and west, a road's carts
        House(c, -16, 2.5f, -12.3f, 6.2f, 3.4f, Mat.Daub, v % 2 == 0 ? Mat.Thatch : Mat.Tile, trim: Mat.BoardDark);
        House(c, 12.3f, 1.2f, 16, 4.6f, 3.2f, Mat.Daub, Mat.Tile, trim: Mat.BoardDark);
        House(c, 11.8f, 7.2f, 15.2f, 10.4f, 3, Mat.Board, Mat.Thatch);
        House(c, -15.4f, 8.2f, -12, 11.4f, 3, Mat.Daub, Mat.Shingle, trim: Mat.BoardDark);
        Cart(c, 5.2f, 12.4f, Mat.Hay);
        Barrel(c, -11.5f, 7.2f, 1.3f); Crate(c, -11.4f, 12.4f); Crate(c, 11.2f, 6.3f, .9f);
        Torch(c, -2.4f, 11.2f); Torch(c, 2.4f, 11.2f);
    }

    static void Outpost(ModelCanvas c, byte own)
    {
        own = Math.Max(own, (byte)1);
        c.Box(-5, -3, -5, 5, .2f, 5, Mat.Trodden);
        c.Cyl(0, -1, 3.2f, -3, 12, Mat.StoneLight);
        c.Paint(-3.3f, -3, -4.3f, 3.3f, 12, 2.3f, Mat.Stone, .3, 3);
        // the timber gallery hung out round its top, a steep roof of slate
        c.Cyl(0, -1, 3.9f, 11, 13.6f, Mat.Board, .4f);
        for (int k = 0; k < 12; k++)
        {
            double a = k * Math.Tau / 12;
            c.Rod((float)Math.Cos(a) * 3.2f, 10, -1 + (float)Math.Sin(a) * 3.2f, (float)Math.Cos(a) * 3.9f, 11, -1 + (float)Math.Sin(a) * 3.9f, .15f, Mat.Wood);
        }
        c.Cone(0, -1, 4.4f, 13.6f, 6.5f, Mat.Slate, .1f);
        c.Flag(0, 20, -1, own, 3.5f, 2.6f);
        c.Box(-.8f, 0, 2.05f, .8f, 2.6f, 2.35f, Mat.Gate);
        c.Box(-.25f, 6.5f, 2.1f, .25f, 7.8f, 2.35f, Mat.Window);
        // a palisade round it, a gate to the south
        for (int k = 0; k < 40; k++)
        {
            double a = k * Math.Tau / 40;
            if (Math.Abs(a - Math.PI / 2) < .3) continue;
            float x = (float)Math.Cos(a) * 7.6f, z = (float)Math.Sin(a) * 7.2f, h = 3 + (k % 3) * .3f;
            c.Cyl(x, z, .38f, -2, h, Mat.Stakes);
            c.Cone(x, z, .38f, h, .7f, Mat.Stakes);
        }
        Hanging(c, 0, 3.6f, 7.6f, own, .9f, 1.8f);
        House(c, 3.5f, 2, 6.4f, 4.6f, 2.4f, Mat.Board, Mat.Thatch, chimney: false);
        Barrel(c, -4, 3.5f); Torch(c, -1.6f, 7.8f); Torch(c, 1.6f, 7.8f);
    }

    static void OutpostPlace(ModelCanvas c)
    {
        // the corners of the ground marked out, a cairn in the middle, timber laid ready
        foreach (var (x, z) in new[] { (-6f, -6f), (6f, -6f), (-6f, 6f), (6f, 6f) })
        {
            c.Box(x - .2f, -1, z - .2f, x + .2f, 2.6f, z + .2f, Mat.Wood);
            c.Box(x - 1.1f, 2, z - .1f, x - .2f, 2.6f, z + .1f, Mat.ClothWhite);
        }
        c.Ball(0, .4f, 0, 1.6f, 1.2f, 1.5f, Mat.Stone, Mat.StoneLight, .4);
        c.Ball(.2f, 1.6f, 0, .9f, .7f, .9f, Mat.StoneLight, Mat.Stone, .4);
        Logs(c, 3.2f, 2.6f, 3, 4.2f, true, .42f);
        c.Box(-4, 0, 2, -2.6f, 1.2f, 3.4f, Mat.Block);
        c.Box(-4.2f, 1.2f, 2.2f, -3, 2.2f, 3.2f, Mat.Block);
    }

    // ------------------------------------------------------------ the horde's

    static void HordeTown(ModelCanvas c, byte own, int v)
    {
        own = Math.Max(own, (byte)2);
        c.Cyl(0, 0, 12.6f, -2, .15f, Mat.Ash);
        c.Paint(-13, -2, -13, 13, .2f, 13, Mat.Trodden, .4, 3);
        // the palisade: sharpened trunks, black and grey, a gate of tusks to the south
        for (int k = 0; k < 64; k++)
        {
            double a = k * Math.Tau / 64;
            if (Math.Abs(a - Math.PI / 2) < .2) continue;
            float x = (float)Math.Cos(a) * 12, z = (float)Math.Sin(a) * 11.5f, h = 3.8f + (float)c.H(k, 0, 0, 5) * 1.6f;
            byte m = k % 4 == 0 ? Mat.Snag : Mat.Char;
            c.Cyl(x, z, .45f, -2, h, m);
            c.Cone(x, z, .45f, h, 1, m);
        }
        foreach (var s in new[] { -1, 1 })
        {
            c.Box(s * 2.2f - .35f, -2, 11.2f, s * 2.2f + .35f, 6.5f, 11.9f, Mat.Char);
            c.Rod(s * 2.2f, 6.2f, 11.6f, s * .6f, 8.4f, 12.1f, .3f, Mat.Tusk);
            Skull(c, s * 2.2f, 7, 11.6f, .55f);
        }
        c.Box(-2.5f, 5.3f, 11.3f, 2.5f, 5.8f, 11.8f, Mat.Char);
        // the huts of hide, the poles of their frames out of their tops
        for (int k = 0; k < 6; k++)
        {
            double a = Math.PI * (1.08 + k * .17) + (v % 3) * .05;
            float hx = (float)Math.Cos(a) * 7.6f, hz = (float)Math.Sin(a) * 6.8f + 1.5f;
            if (k == 5) { hx = 7.2f; hz = 5.4f; }
            float r = 2.2f + (k % 2) * .5f;
            c.Cyl(hx, hz, r, -1, 1.8f, Mat.Hide);
            c.Cone(hx, hz, r + .3f, 1.8f, 3.6f, k % 2 == 0 ? Mat.LeatherDark : Mat.Hide, 0, Mat.Hide, .3);
            for (int p = 0; p < 3; p++) c.Rod(hx, 4.6f, hz, hx + (p - 1) * .7f, 6.4f, hz + (p == 1 ? .6f : -.3f), .12f, Mat.Snag);
            c.Box(hx - .5f, 0, hz + r - .4f, hx + .5f, 1.6f, hz + r + .1f, Mat.Char);
        }
        // the chief's long hall at the back, horns crossed on its gables
        c.Box(-5.2f, -2, -10.4f, 5.2f, 3.3f, -5.6f, Mat.Char);
        c.Paint(-5.3f, -2, -10.5f, 5.3f, 3.3f, -5.5f, Mat.Snag, .2, 7);
        c.Gable(-5.2f, -10.4f, 5.2f, -5.6f, 3.3f, Mat.LeatherDark, true, 1.15f, .6f, Mat.Char);
        foreach (var s in new[] { -1, 1 })
        {
            c.Rod(s * 5.6f, 5.4f, -8, s * 6.6f, 7.6f, -7.2f, .22f, Mat.Horn);
            c.Rod(s * 5.6f, 5.4f, -8, s * 6.6f, 7.6f, -8.8f, .22f, Mat.Horn);
        }
        c.Box(-.8f, 0, -5.65f, .8f, 2.4f, -5.4f, Mat.Maw);
        Skull(c, 0, 3, -5.3f, .6f);
        // the fire in the middle, the totems, the war banners, the bones
        Fire(c, 0, 1.5f, 1.3f);
        Totem(c, -3.6f, 3.5f, 7.5f, own);
        Totem(c, 4.2f, -2.4f, 6, own);
        BoneHeap(c, 4.4f, 8.6f, 1.2f);
        BoneHeap(c, -5.6f, 7.6f, .9f);
        c.Flag(-3.6f, 0, -4.2f, own, 9, 3.2f);
        // a watchtower of trunks over the east side
        foreach (var (x, z) in new[] { (8f, -6f), (10f, -6f), (8f, -4f), (10f, -4f) }) c.Box(x - .3f, -2, z - .3f, x + .3f, 7.5f, z + .3f, Mat.Char);
        c.Box(7.4f, 7.5f, -6.6f, 10.6f, 8, -3.4f, Mat.Snag);
        c.Pyramid(9, -5, 2, 8.6f, 2.4f, Mat.Hide);
        c.Box(7.6f, 8, -3.6f, 10.4f, 8.6f, -3.4f, Mat.Snag);
    }

    /// <summary>A totem: a pole of black wood, skulls up it, red rags on a crossbar.</summary>
    static void Totem(ModelCanvas c, float x, float z, float h, byte own)
    {
        c.Box(x - .4f, -2, z - .4f, x + .4f, h, z + .4f, Mat.Char);
        for (float y = 1.6f; y < h - .8f; y += 2.1f) Skull(c, x, y, z + .2f, .55f);
        c.Box(x - 1.6f, h - 1.6f, z - .15f, x + 1.6f, h - 1.2f, z + .15f, Mat.Snag);
        c.Box(x - 1.5f, h - 3.4f, z, x - .9f, h - 1.6f, z + .25f, Mat.ClothRed);
        c.Box(x + .9f, h - 3, z, x + 1.5f, h - 1.6f, z + .25f, Mat.ClothRed);
        c.Rod(x, h, z, x - .9f, h + 1.3f, z, .18f, Mat.Horn);
        c.Rod(x, h, z, x + .9f, h + 1.3f, z, .18f, Mat.Horn);
    }

    static void Totem(ModelCanvas c, int v) => Totem(c, 0, 0, 6.5f + v % 3, 2);

    static void Lair(ModelCanvas c, byte own)
    {
        own = Math.Max(own, (byte)2);
        c.Cyl(0, 3, 8, -1, .1f, Mat.Ash);
        Crag(c, 0, -4.5f, 10, 9, 7, Mat.Basalt, Mat.Crag, .3f);
        Mouth(c, 0, -.2f, 4.4f, 5.4f, 4, true, Mat.Char);
        // tusks over its mouth, skulls on poles, torches, bones strewn
        c.Rod(-2.6f, 5.6f, .2f, -.8f, 7.8f, 1.4f, .32f, Mat.Tusk);
        c.Rod(2.6f, 5.6f, .2f, .8f, 7.8f, 1.4f, .32f, Mat.Tusk);
        Skull(c, 0, 6.6f, .6f, .9f);
        Torch(c, -3.6f, 1.6f, 3); Torch(c, 3.6f, 1.6f, 3);
        foreach (var x in new[] { -6.2f, 6 })
        {
            c.Box(x - .25f, -1, 3.8f, x + .25f, 5, 4.3f, Mat.Char);
            Skull(c, x, 5.5f, 4.1f, .6f);
        }
        BoneHeap(c, -3.4f, 5.6f, 1.3f);
        BoneHeap(c, 3.4f, 6.4f, 1);
        c.Flag(5, 5.5f, -1.5f, own, 7, 3);
    }

    // ------------------------------------------------------------ the mines

    static void Sawmill(ModelCanvas c, byte own)
    {
        c.Box(-8, -2, -5, 8, .2f, 5, Mat.Trodden);
        // the mill: a long shed open to the south, its saw and the trunk on the bench before it
        c.Box(-7, -2, -4.5f, 1.5f, 4.4f, .2f, Mat.Board);
        c.Paint(-7.1f, 0, -4.6f, 1.6f, 4.4f, .3f, Mat.BoardDark, .3, 3);
        c.Cut(-6.3f, 0, -3.6f, .8f, 3.6f, .4f);
        foreach (var x in new[] { -7f, -3.2f, 1.2f }) c.Box(x, 0, -.2f, x + .4f, 4.4f, .2f, Mat.Wood);
        c.Gable(-7, -4.5f, 1.5f, .2f, 4.4f, Mat.Shingle, true, 1, .6f, Mat.Board);
        c.Box(-5.6f, 0, -1.6f, -.8f, 1.2f, -.6f, Mat.Wood);
        c.Rod(-6, 1.7f, -1.1f, -.4f, 1.7f, -1.1f, .5f, Mat.Trunk);
        c.Rod(-3.2f, 1.6f, -1.6f, -3.2f, 1.6f, -.6f, 1.25f, Mat.Steel);
        c.Rod(-3.2f, 1.6f, -1.25f, -3.2f, 1.6f, -.95f, .3f, Mat.Iron);
        c.Flag(-2.7f, 6.6f, -2.2f, own, 4, 2.4f);
        // the logs piled, the boards stacked, the stumps
        Logs(c, 4.8f, -2.6f, 4, 5.6f, false, .55f);
        Logs(c, -3.2f, 3.2f, 3, 6.4f, true, .5f);
        for (int k = 0; k < 6; k++) c.Box(3.6f, k * .34f, 2.4f, 7.4f, k * .34f + .3f, 4.2f, k % 2 == 0 ? Mat.Plank : Mat.Board);
        c.Cyl(6.4f, -5.6f, .7f, -1, .8f, Mat.Trunk); c.Cyl(6.4f, -5.6f, .55f, .8f, .95f, Mat.Lumber);
        c.Cyl(-8.2f, 2, .65f, -1, .7f, Mat.Trunk); c.Cyl(-8.2f, 2, .5f, .7f, .85f, Mat.Lumber);
        Cart(c, 1.4f, 5.6f, 0);
        c.Rod(.4f, 2.3f, 5.6f, 2.4f, 2.3f, 5.6f, .4f, Mat.Trunk);
    }

    static void Quarry(ModelCanvas c, byte own)
    {
        // the stone cut back in steps, its faces dressed pale
        Crag(c, -1, -5.5f, 10, 7.5f, 6, Mat.Stone, Mat.StoneLight, .3f);
        for (int s = 0; s < 3; s++) c.Cut(-6 + s * 1.2f, s * 2.2f, -5 + s * 1.6f, 4 - s * 1.2f, 9, 8);
        c.Paint(-6.5f, -1, -6, 4.5f, 8, 0, Mat.Hewn, .6, 9);
        c.Box(-8, -2, -2, 8, .2f, 6, Mat.Gravel);
        // blocks cut and stacked, a wooden crane with one hanging, a cart of them
        for (int k = 0; k < 6; k++) { float x = 3.6f + (k % 3) * 1.45f, y = (k / 3) * 1.2f, z = 1.8f + (k / 3) * .3f; c.Box(x, y, z, x + 1.3f, y + 1.15f, z + 1.6f, Mat.Block); }
        c.Box(-6.8f, 0, 2.5f, -5.2f, 1.2f, 4.1f, Mat.Block);
        c.Rod(-3.6f, 0, 4.2f, -3.6f, 9, 3.2f, .3f, Mat.Wood);
        c.Rod(-2.4f, 0, 2.4f, -3.6f, 9, 3.2f, .25f, Mat.Wood);
        c.Rod(-3.6f, 8.4f, 3.2f, 1.6f, 9.6f, 1.6f, .25f, Mat.Wood);
        c.Rod(1.4f, 9.4f, 1.6f, 1.4f, 4.6f, 1.6f, .08f, Mat.Rag);
        c.Box(.8f, 3.6f, 1, 2, 4.6f, 2.2f, Mat.Block);
        Cart(c, -1, 5.6f, Mat.Block);
        c.Flag(5.6f, 3.4f, -3.6f, own, 6, 2.6f);
    }

    static void OreMine(ModelCanvas c, byte own, bool gold)
    {
        byte ore = gold ? Mat.Nugget : Mat.OreRust, ore2 = gold ? Mat.Brass : Mat.IronOre;
        Crag(c, 0, -5.2f, 10, 8, 6.5f, Mat.Rock, Mat.StoneDark, .35f);
        c.Paint(-10, 0, -12, 10, 9, -1, ore, .06, 17);
        Mouth(c, -1.2f, -1.2f, 3.6f, 4.2f, 4, true);
        // the rails out of its mouth, a cart on them with its load, the heap
        for (float z = -4; z < 8; z += .9f) c.Box(-3, -.1f, z, .6f, .25f, z + .4f, Mat.Plank);
        c.Box(-2.5f, .25f, -4, -2.2f, .45f, 8, Mat.Iron);
        c.Box(-.2f, .25f, -4, .1f, .45f, 8, Mat.Iron);
        c.Box(-2.6f, .5f, 3.8f, .2f, 2, 6.2f, Mat.Iron);
        c.Box(-2.3f, 1.2f, 4.1f, -.1f, 2, 5.9f, Mat.Air);
        c.Ball(-1.2f, 2, 5, 1.2f, .7f, 1, ore, ore2, .4);
        c.Ball(5, .3f, 4, 2.6f, 1.9f, 2.4f, ore, ore2, .4, .3f);
        c.Rod(4, 1.6f, 5.8f, 5.6f, 2.8f, 6.6f, .12f, Mat.Wood);
        c.Box(5.3f, 2.6f, 6.3f, 6.1f, 2.9f, 6.9f, Mat.Steel);
        if (gold)
        {
            // a headframe over a shaft, its wheel on top
            foreach (var (x, z) in new[] { (5.5f, -4f), (8.5f, -4f), (5.5f, -1f), (8.5f, -1f) }) c.Rod(x, -1, z, 7, 10, -2.5f, .25f, Mat.Wood);
            c.Box(5.8f, 4, -3.7f, 8.2f, 4.3f, -1.3f, Mat.Wood);
            c.Rod(7, 10.4f, -2.8f, 7, 10.4f, -2.2f, 1.5f, Mat.BoardDark);
            c.Rod(7, 10.4f, -2.9f, 7, 10.4f, -2.1f, .3f, Mat.Iron);
            c.Paint(3, 0, 2, 7.6f, 3, 6.6f, Mat.Nugget, .5, 19);
        }
        else
        {
            // a furnace by the heap, its glow and its chimney
            c.Cyl(6.2f, -2.5f, 2, -1, 3.6f, Mat.Stone);
            c.Cone(6.2f, -2.5f, 2, 3.6f, 1.6f, Mat.Clay, .9f);
            c.Box(5.8f, 5, -2.9f, 6.6f, 8.2f, -2.1f, Mat.Stone);
            c.Box(5.6f, .2f, -.6f, 6.8f, 1.4f, -.4f, Mat.Coals);
            Barrel(c, 8.4f, .6f, 1.2f);
        }
        Torch(c, -3.8f, -.2f, 2.6f); Torch(c, 1.4f, -.2f, 2.6f);
        c.Flag(-1.2f, 7.2f, -3.6f, own, 4, 2.4f);
    }

    static void Alchemist(ModelCanvas c, byte own)
    {
        c.Box(-6, -2, -5, 6, .2f, 5, Mat.Flag);
        // the tower, its slate cone, its crooked brass chimney; the workroom beside it
        c.Cyl(-2.4f, -2.2f, 2.6f, -2, 10, Mat.StoneCool);
        c.Paint(-5, -2, -5, .2f, 10, .4f, Mat.StoneDark, .25, 5);
        c.Cone(-2.4f, -2.2f, 3.2f, 10, 5.5f, Mat.Slate, .1f);
        c.Box(-2.7f, 3.4f, .2f, -2.1f, 4.6f, .45f, Mat.Window);
        c.Box(-2.7f, 7, .2f, -2.1f, 8.2f, .45f, Mat.Window);
        c.Box(1, -2, -4.2f, 5.6f, 3.6f, -.6f, Mat.Daub);
        c.Gable(1, -4.2f, 5.6f, -.6f, 3.6f, Mat.Slate, true, 1, .5f, Mat.Daub);
        c.Rod(4.4f, 4.6f, -2.4f, 4.4f, 7, -2.4f, .45f, Mat.Brass);
        c.Rod(4.4f, 7, -2.4f, 5.4f, 8.2f, -2.4f, .45f, Mat.Brass);
        c.Rod(5.4f, 8.2f, -2.4f, 5.4f, 9.4f, -2.4f, .45f, Mat.Brass);
        c.Box(2.6f, 0, -.65f, 3.6f, 2.2f, -.45f, Mat.BoardDark);
        // the table of glass before it, flasks glowing; the pool of quicksilver in its stone rim
        c.Box(-1.4f, 0, 1.2f, 1.6f, 1.2f, 2.2f, Mat.Table);
        for (int k = 0; k < 4; k++) { float x = -1 + k * .8f; c.Ball(x, 1.6f, 1.7f, .35f, .45f, .35f, k % 2 == 0 ? Mat.Phial : Mat.Brew, rough: 0); }
        c.Rod(1.2f, 1.4f, 1.7f, 2.1f, 2.3f, 1.7f, .1f, Mat.Brass);
        c.Cyl(3.4f, 3.2f, 2.3f, -1, .5f, Mat.StoneLight);
        c.Cyl(3.4f, 3.2f, 1.8f, -.5f, .4f, Mat.Quicksilver);
        Barrel(c, -4.8f, 2.6f, 1.3f); Crate(c, -4.4f, 4.2f, .9f);
        c.Flag(-2.4f, 15.4f, -2.2f, own, 2.6f, 2.2f);
    }

    static void SulfurDune(ModelCanvas c, byte own)
    {
        c.Lump(0, -1.4f, -1, 9.5f, 4.6f, 7.5f, Mat.Sulphur, Mat.Sand, .3, .2f);
        c.Lump(-3, 1.2f, -2.4f, 4.2f, 3, 3.6f, Mat.Sulphur, Mat.Ore, .2, .2f);
        // the vents, crusted and smoking; the crystals of sulphur growing up from them
        foreach (var (x, z) in new[] { (-2f, 1.4f), (3.4f, -1.6f), (-5.4f, -3f) })
        {
            float y = 2.4f;
            c.Cyl(x, z, 1.1f, y - 1, y + .3f, Mat.Char);
            c.Cyl(x, z, .55f, y - .8f, y + .4f, Mat.Air);
            for (int k = 0; k < 4; k++)
            {
                double a = k * 1.6 + x;
                c.Rod(x + (float)Math.Cos(a) * 1.2f, y, z + (float)Math.Sin(a) * 1.2f, x + (float)Math.Cos(a) * 1.7f, y + 1.6f, z + (float)Math.Sin(a) * 1.7f, .28f, Mat.Sulphur);
            }
        }
        // the gatherers' hut, their barrels
        House(c, 4.4f, 3, 8, 5.6f, 2.6f, Mat.Board, Mat.Thatch, chimney: false);
        Barrel(c, 3.2f, 5.6f, 1.2f); Barrel(c, 2.2f, 4.6f, 1.2f);
        c.Flag(6.4f, 0, 1.4f, own, 7, 2.6f);
    }

    static void CrystalCave(ModelCanvas c, byte own)
    {
        Crag(c, 0, -4.4f, 9.5f, 8, 6.5f, Mat.Crag, Mat.Rock, .4f);
        Mouth(c, -1.6f, -1.2f, 3.4f, 4, 4, false);
        // the crystals bursting from it: great ones blue and red, smaller ones about
        void Spike(float x, float y, float z, float dx, float dz, float len, float r, byte m)
        {
            c.Rod(x, y, z, x + dx * len, y + len, z + dz * len, r, m);
            c.Cone(x + dx * len, z + dz * len, r, y + len - .2f, r * 1.8f, m);
        }
        Spike(2.6f, 1.5f, -.6f, .25f, .2f, 5.5f, .95f, Mat.Crystal);
        Spike(3.4f, 1, .4f, .5f, .3f, 3.6f, .7f, Mat.Crystal);
        Spike(1.6f, 1.2f, .3f, -.2f, .35f, 3.2f, .6f, CrystalRed);
        Spike(-4.6f, 2.8f, -1.4f, -.35f, .3f, 4.4f, .85f, CrystalRed);
        Spike(-5.6f, 2, -.4f, -.4f, .4f, 2.8f, .55f, Mat.Crystal);
        Spike(-1.4f, 6.4f, -4.4f, .1f, -.1f, 4, .8f, Mat.Crystal);
        Spike(5.4f, 0, 4.2f, .2f, .3f, 2, .45f, CrystalRed);
        Spike(-6.4f, 0, 4.6f, -.2f, .2f, 1.8f, .4f, Mat.Crystal);
        c.Flag(4.4f, 7.4f, -4.6f, own, 4, 2.4f);
    }

    static void GemPond(ModelCanvas c, byte own)
    {
        c.Cyl(0, 1, 6.4f, -1.5f, .2f, Mat.Sand);
        c.Cyl(0, 1, 5.4f, -1.2f, .1f, Mat.Water);
        // rocks about the shore, gems in them and on the shore
        for (int k = 0; k < 9; k++)
        {
            double a = k * Math.Tau / 9 + .3;
            float x = (float)Math.Cos(a) * 6.2f, z = 1 + (float)Math.Sin(a) * 5.8f;
            if (Math.Abs(a - Math.PI / 2) < .5) continue;
            c.Ball(x, .2f, z, 1.3f, 1, 1.2f, Mat.Rock, Mat.StoneLight, .3, .3f);
            c.Paint(x - 1.4f, 0, z - 1.4f, x + 1.4f, 1.4f, z + 1.4f, k % 3 == 0 ? Mat.Crystal : Mat.Gem, .25, k);
        }
        c.Ball(0, -.1f, 1, 1.4f, .8f, 1.2f, Mat.Gem, Mat.Crystal, .3, .3f);
        // the jeweller's hut and his baskets
        House(c, -8, -6.4f, -3.6f, -2.6f, 3, Mat.Daub, Mat.Tile, trim: Mat.BoardDark);
        c.Cyl(-2.4f, -1.4f, .7f, 0, .8f, Mat.Straw);
        c.Paint(-3.2f, .6f, -2.2f, -1.6f, .9f, -.6f, Mat.Gem, .7, 2);
        c.Flag(4.6f, 0, -4.6f, own, 7, 2.6f);
    }

    // ------------------------------------------------------------ what lies to be picked up

    static void Pile(ModelCanvas c, int v)
    {
        // every pile on a patch of trodden earth, a stake with a rag by it to be seen from afar
        c.Cyl(0, 0, 3.4f, -1, .3f, Mat.Trodden);
        c.Box(2.6f, -1, -1.8f, 2.8f, 2.6f, -1.6f, Mat.Wood);
        c.Box(1.9f, 2, -1.75f, 2.6f, 2.5f, -1.65f, Mat.ClothWhite);
        switch (v % 8)
        {
            case 0: Logs(c, -.4f, 0, 3, 4.2f, true, .5f); Logs(c, .6f, 2, 2, 3.2f, true, .42f); c.Rod(-2.6f, .4f, -1.6f, -1, .4f, -2.4f, .4f, Mat.Trunk); break;
            case 1:
                for (int k = 0; k < 6; k++) { float x = -1.8f + (k % 3) * 1.2f, y = (k / 3) * 1.05f, z = -.8f + (k / 3) * .25f; c.Box(x, y, z, x + 1.1f, y + 1, z + 1.3f, k % 2 == 0 ? Mat.Block : Mat.Hewn); }
                c.Ball(1.8f, .3f, 1.6f, .8f, .6f, .7f, Mat.Stone, Mat.StoneLight, .4); c.Ball(-2, .2f, 1.4f, .6f, .45f, .55f, Mat.Rubble); break;
            case 2:
                c.Ball(0, .2f, 0, 2, 1.4f, 1.8f, Mat.OreRust, Mat.IronOre, .45, .3f);
                c.Rod(1, .8f, 1.4f, 2.2f, 2.4f, 1.8f, .12f, Mat.Wood); c.Box(2, 2.2f, 1.5f, 2.9f, 2.5f, 2, Mat.Steel); break;
            case 3:
                c.Ball(0, .1f, 0, 1.8f, 1.1f, 1.6f, Mat.Nugget, Mat.Brass, .35, .2f);
                c.Box(1.4f, 0, -1.2f, 3, 1.2f, .2f, Mat.Board); c.Box(1.6f, .9f, -1, 2.8f, 1.3f, 0, Mat.Nugget);
                c.Box(1.4f, .4f, -1.25f, 3, .7f, .25f, Mat.Brass); break;
            case 4: c.Ball(0, .1f, 0, 2, 1.3f, 1.7f, Mat.Sulphur, Mat.Ore, .2, .3f); c.Rod(-.6f, .8f, .3f, -.4f, 2.6f, .6f, .35f, Mat.Sulphur); break;
            case 5:
                Crate(c, 0, 0, 1.6f);
                for (int k = 0; k < 4; k++) c.Ball(-.5f + (k % 2) * 1, 1.85f, -.4f + (k / 2) * .8f, .3f, .4f, .3f, Mat.Quicksilver, rough: 0);
                c.Ball(1.8f, .45f, .8f, .45f, .55f, .45f, Mat.Phial, rough: 0); break;
            case 6:
                c.Ball(0, 0, 0, 1.4f, .7f, 1.3f, Mat.Rock, Mat.Crag, .3);
                c.Rod(0, .3f, 0, .3f, 3, .2f, .5f, Mat.Crystal); c.Rod(.6f, .3f, .4f, 1.6f, 2, .8f, .4f, Mat.Crystal);
                c.Rod(-.4f, .3f, .3f, -1.4f, 1.8f, .6f, .38f, CrystalRed); break;
            default:
                c.Ball(0, .5f, 0, 1.4f, 1.1f, 1.2f, Mat.Sack, rough: .15f); c.Box(-.4f, 1.4f, -.3f, .4f, 1.9f, .3f, Mat.Rag);
                c.Paint(-1.6f, 0, -1.4f, 1.6f, 1.9f, 1.6f, Mat.Gem, .15, 4);
                c.Ball(1.6f, .2f, .9f, .6f, .4f, .6f, Mat.Gem, Mat.Crystal, .4, .2f); break;
        }
    }

    static void Chest(ModelCanvas c)
    {
        c.Box(-2.2f, -1, -1.6f, 2.2f, .2f, 1.6f, Mat.StoneLight);
        c.Box(-1.8f, .2f, -1.1f, 1.8f, 2, 1.1f, Mat.Board);
        c.Rod(-1.8f, 2, 0, 1.8f, 2, 0, 1.1f, Mat.Board);
        c.Cut(-2, -1, -2, 2, 2, -1.1f); c.Cut(-2, -1, 1.1f, 2, 2, 2);
        // its bands, its corners and its lock in brass
        foreach (var x in new[] { -1.8f, -.2f, 1.45f }) { c.Box(x, .2f, -1.15f, x + .35f, 2.2f, 1.15f, Mat.Brass); c.Rod(x, 2, 0, x + .35f, 2, 0, 1.18f, Mat.Brass); }
        c.Box(-1.85f, .2f, 1.05f, 1.85f, .5f, 1.2f, Mat.Brass);
        c.Box(-.4f, 1.3f, 1.05f, .4f, 2.1f, 1.3f, Mat.Nugget);
        // coins spilt on the slab
        c.Box(1.9f, .2f, .6f, 2.4f, .4f, 1.2f, Mat.Nugget); c.Box(-2.4f, .2f, .9f, -1.9f, .4f, 1.4f, Mat.Nugget);
    }

    static void Campfire(ModelCanvas c, bool spent)
    {
        Fire(c, 0, 0, 1.15f, !spent);
        if (spent) return;
        // the pot hung on its tripod, the logs to sit on, a bedroll and a sack
        c.Rod(-1.4f, 0, -1, 0, 3.2f, 0, .12f, Mat.Wood); c.Rod(1.4f, 0, -1, 0, 3.2f, 0, .12f, Mat.Wood); c.Rod(0, 0, 1.6f, 0, 3.2f, 0, .12f, Mat.Wood);
        c.Rod(0, 3.1f, 0, 0, 2.2f, 0, .06f, Mat.Iron);
        c.Ball(0, 1.8f, 0, .7f, .55f, .7f, Mat.Iron, rough: 0);
        c.Rod(-3.6f, .5f, -1.2f, -2.6f, .5f, 1.4f, .45f, Mat.Trunk);
        c.Rod(2.2f, .5f, -2.8f, 3.6f, .5f, -1, .45f, Mat.Trunk);
        c.Box(1.6f, 0, 2, 3.8f, .35f, 3, Mat.Linen); c.Rod(1.6f, .4f, 2.5f, 1.6f, .4f, 2.5f, .45f, Mat.Rag);
        c.Ball(-2.6f, .6f, 2.6f, .7f, .7f, .6f, Mat.Sack, rough: .2f);
        Barrel(c, 3.6f, 1.2f, 1.2f);
    }

    // ------------------------------------------------------------ what turns

    static void Windmill(ModelCanvas c, byte own, bool spent)
    {
        c.Cyl(0, 0, 3.4f, -2, 1.2f, Mat.Stone);
        c.Cone(0, 0, 3.1f, 1.2f, 10.5f, Mat.Board, 2.2f, Mat.BoardDark, .2);
        c.Cone(0, 0, 2.9f, 11.6f, 4.2f, Mat.Thatch, .2f);
        c.Box(-.7f, 1.2f, 2.6f, .7f, 3.4f, 3.05f, Mat.BoardDark);
        c.Box(-.35f, 6.6f, 2.15f, .35f, 7.6f, 2.55f, Mat.Window);
        // the hub on its axle to the south; the sails turn on it of themselves (Sails), gone once spent
        c.Rod(0, 11.4f, 1.5f, 0, 11.4f, 3.7f, .5f, Mat.Wood);
        c.Rod(0, 11.4f, 3.6f, 0, 11.4f, 4, .7f, Mat.Iron);
        if (spent) c.Rod(.2f, 11.4f, 3.9f, 1.4f, 7.6f, 4.1f, .18f, Mat.Snag);
        for (int k = 0; k < 4; k++) c.Ball(-2.6f + k * .6f, .6f, 3.6f + (k % 2) * .8f, .55f, .6f, .5f, Mat.Sack, rough: .15f);
        Fence(c, 3.6f, 3.4f, 6.4f, -1);
    }

    /// <summary>A windmill's four sails, drawn in the plane of x and y about its hub at (0, 8, 0), to turn on its own.</summary>
    static void Sails(ModelCanvas c)
    {
        const float hub = 8;
        c.Rod(0, hub, -.4f, 0, hub, .4f, .55f, Mat.Iron);
        for (int k = 0; k < 4; k++)
        {
            double a = k * Math.PI / 2 + .35;
            float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a), nx = -dy, ny = dx;
            c.Rod(0, hub, 0, dx * 7.4f, hub + dy * 7.4f, 0, .16f, Mat.Wood);
            // a lattice of laths, the canvas over most of it
            for (float t = 1.4f; t < 7.2f; t += .7f)
                c.Rod(dx * t, hub + dy * t, -.05f, dx * t + nx * 1.6f, hub + dy * t + ny * 1.6f, -.05f, .08f, Mat.Plank);
            c.Rod(dx * 1.4f + nx * 1.6f, hub + dy * 1.4f + ny * 1.6f, 0, dx * 7.2f + nx * 1.6f, hub + dy * 7.2f + ny * 1.6f, 0, .1f, Mat.Plank);
            for (float t = 1.8f; t < 6.9f; t += .34f)
                for (float u = .25f; u < 1.45f; u += .34f)
                    c.Box(dx * t + nx * u - .17f, hub + dy * t + ny * u - .17f, .05f, dx * t + nx * u + .17f, hub + dy * t + ny * u + .17f, .25f, Mat.Canvas);
        }
    }

    static void WaterMill(ModelCanvas c, byte own)
    {
        // the race of water past it, the mill of stone below and boards above, the wheel on its east side (Wheel)
        c.Box(3.6f, -1.6f, -5, 6.8f, .1f, 5, Mat.Water);
        c.Box(3.2f, -1.6f, -5, 3.6f, .5f, 5, Mat.Stone); c.Box(6.8f, -1.6f, -5, 7.2f, .5f, 5, Mat.Stone);
        c.Box(-4.4f, -2, -3.6f, 3.2f, 2.6f, 3.4f, Mat.StoneLight);
        c.Paint(-4.5f, -2, -3.7f, 3.3f, 2.6f, 3.5f, Mat.Stone, .3, 3);
        c.Box(-4.4f, 2.6f, -3.6f, 3.2f, 5.4f, 3.4f, Mat.Board);
        c.Gable(-4.4f, -3.6f, 3.2f, 3.4f, 5.4f, Mat.Shingle, true, 1, .6f, Mat.Board);
        c.Box(-1, 0, 3.35f, .6f, 2.2f, 3.6f, Mat.BoardDark);
        c.Box(-3.2f, 3.4f, 3.35f, -2.4f, 4.4f, 3.6f, Mat.Window); c.Box(1.4f, 3.4f, 3.35f, 2.2f, 4.4f, 3.6f, Mat.Window);
        c.Rod(3, 3.2f, 0, 5.4f, 3.2f, 0, .35f, Mat.Wood);
        for (int k = 0; k < 3; k++) c.Ball(-3.4f + k * 1.1f, .6f, 4.4f, .55f, .6f, .5f, Mat.Sack, rough: .15f);
        Cart(c, -6.4f, 2, Mat.Sack);
    }

    /// <summary>A water wheel, drawn in the plane of x and y about its axle at (0, 4, 0), to turn on its own.</summary>
    static void Wheel(ModelCanvas c)
    {
        const float hub = 4;
        c.Rod(0, hub, -.9f, 0, hub, .9f, .45f, Mat.Iron);
        for (int k = 0; k < 16; k++)
        {
            double a = k * Math.Tau / 16;
            float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a);
            if (k % 2 == 0) c.Rod(0, hub, -.55f, dx * 3.2f, hub + dy * 3.2f, -.55f, .12f, Mat.Wood);
            if (k % 2 == 0) c.Rod(0, hub, .55f, dx * 3.2f, hub + dy * 3.2f, .55f, .12f, Mat.Wood);
            // a paddle across the rims
            c.Box(dx * 3.6f - .2f, hub + dy * 3.6f - .2f, -.8f, dx * 3.6f + .2f, hub + dy * 3.6f + .2f, .8f, Mat.Plank);
        }
        for (int k = 0; k < 48; k++)
        {
            double a = k * Math.Tau / 48;
            float x = (float)Math.Cos(a) * 3.2f, y = hub + (float)Math.Sin(a) * 3.2f;
            c.Box(x - .2f, y - .2f, -.75f, x + .2f, y + .2f, -.35f, Mat.BoardDark);
            c.Box(x - .2f, y - .2f, .35f, x + .2f, y + .2f, .75f, Mat.BoardDark);
        }
    }

    // ------------------------------------------------------------ the places that are visited

    static void Shrine(ModelCanvas c)
    {
        c.Box(-5.4f, -2, -4.6f, 5.4f, .7f, 4.6f, Mat.Hewn);
        c.Box(-4.8f, .7f, -4, 4.8f, 1.3f, 4, Mat.Flag);
        c.Box(-2, -.2f, 4.6f, 2, .45f, 5.6f, Mat.Hewn);
        // six columns, the dome over them, the altar and its pale fire
        foreach (var (x, z) in new[] { (-3.8f, -3f), (0f, -3.4f), (3.8f, -3f), (-3.8f, 3f), (3.8f, 3f), (0, 3.4f) })
        {
            if (x == 0 && z > 0) continue;
            c.Cyl(x, z, .55f, 1.3f, 7, Mat.StoneLight);
            c.Box(x - .7f, 6.6f, z - .7f, x + .7f, 7.2f, z + .7f, Mat.Hewn);
        }
        c.Box(-4.6f, 7.2f, -3.9f, 4.6f, 7.9f, 3.9f, Mat.Hewn);
        c.Ball(0, 7.9f, 0, 4.2f, 3.6f, 3.6f, Mat.Slate, rough: 0);
        c.Cut(-5, 4, -5, 5, 7.9f, 5);
        c.Box(-4.6f, 7.2f, -3.9f, 4.6f, 7.9f, 3.9f, Mat.Hewn);
        c.Rod(0, 11.3f, 0, 0, 13.2f, 0, .18f, Mat.Brass); c.Ball(0, 13.4f, 0, .4f, .4f, .4f, Mat.Brass, rough: 0);
        c.Box(-1, 1.3f, -1, 1, 2.8f, 1, Mat.Hewn);
        c.Box(-.5f, 2.8f, -.5f, .5f, 3.3f, .5f, Mat.SoulFire);
        c.Cone(0, 0, .45f, 3.3f, 1.4f, Mat.SoulFire);
        foreach (var x in new[] { -2.4f, 2.4f }) { c.Box(x - .15f, 1.3f, 1.6f, x + .15f, 2, 1.9f, Mat.Bone); c.Box(x - .1f, 2, 1.65f, x + .1f, 2.3f, 1.85f, Mat.Flame); }
    }

    static void Dwelling(ModelCanvas c, byte own)
    {
        c.Box(-7, -2, -5, 7, .2f, 5, Mat.Trodden);
        House(c, -6.4f, -4.4f, 1.4f, -.4f, 3.4f, Mat.Board, Mat.Thatch, chimney: true);
        c.Paint(-6.5f, 0, -4.5f, 1.5f, 3.4f, -.3f, Mat.BoardDark, .25, 4);
        // the pen of those to be hired, a trough and a rick, the banner
        Fence(c, 2.4f, -4.2f, 7, -4.2f); Fence(c, 7, -4.2f, 7, 3); Fence(c, 2.4f, 3, 7, 3); Fence(c, 2.4f, -4.2f, 2.4f, -1);
        c.Box(3.2f, 0, 2, 5.6f, .8f, 2.6f, Mat.BoardDark); c.Box(3.4f, .5f, 2.1f, 5.4f, .75f, 2.5f, Mat.Water);
        Haystack(c, -4.4f, 2.6f);
        c.Flag(1.2f, 0, 1.6f, own, 7.4f, 2.8f);
    }

    static void DeadMine(ModelCanvas c, bool spent)
    {
        Crag(c, 0, -5, 9.5f, 7.5f, 6.4f, Mat.Rock, Mat.StoneDark, .45f);
        c.Paint(-10, 0, -12, 10, 9, 0, Mat.Moss, .15, 3);
        Mouth(c, 0, -1.2f, 3.6f, 4, 4.4f, false);
        // its timbers fallen in, the ghost-light deep in it
        c.Rod(-1.8f, 0, -.6f, -1.6f, 3.8f, -.4f, .25f, Mat.Snag);
        c.Rod(1.8f, 0, -.6f, .6f, 2.6f, -.4f, .25f, Mat.Snag);
        c.Rod(-2.2f, 3.6f, -.4f, 1.6f, 1.2f, .6f, .25f, Mat.Snag);
        c.Box(-.7f, .4f, -4.8f, .7f, 1.6f, -4.2f, Mat.SoulFire);
        // a cart on its side, the rails broken, gravestones and bones
        c.Box(3, 0, 2.6f, 5.4f, 1.4f, 4, Mat.Iron); c.Box(3.3f, .3f, 2.9f, 5.1f, 1.4f, 3.7f, Mat.Air);
        c.Box(-1.5f, -.1f, .5f, -1.2f, .2f, 4.5f, Mat.Rust);
        foreach (var x in new[] { -4.6f, -6.4f }) { c.Box(x - .5f, -1, 4, x + .5f, 1.8f, 4.4f, Mat.StoneDark); c.Box(x - .9f, 1.1f, 4, x + .9f, 1.5f, 4.4f, Mat.StoneDark); }
        if (!spent) { BoneHeap(c, 1.6f, 3.4f, .9f); Skull(c, -.6f, .4f, 2.4f, .5f); }
    }

    static void Ruins(ModelCanvas c, bool spent)
    {
        c.Box(-7, -2, -6, 7, .3f, 6, Mat.Flag);
        c.Paint(-7, -2, -6, 7, .3f, 6, Mat.Moss, .25, 2);
        // a colonnade broken at all heights, a wall with its arch, drums of columns fallen
        float[] tall = { 7.5f, 4.2f, 6.8f, 2.4f, 5.4f, 3.2f };
        for (int k = 0; k < 6; k++)
        {
            float x = -5 + k * 2;
            c.Cyl(x, -3.4f, .65f, -1, tall[k], Mat.Ruin);
            c.Box(x - .8f, tall[k] - .4f, -4.2f, x + .8f, tall[k], -2.6f, k % 2 == 0 ? Mat.Ruin : Mat.Air);
        }
        c.Box(-5.8f, 7.1f, -4, -1.2f, 7.6f, -2.8f, Mat.Ruin);
        c.Box(3, -1, -1, 6.6f, 6.2f, .4f, Mat.Ruin);
        c.Cut(3.9f, 0, -1.2f, 5.7f, 3.4f, .6f);
        c.Rod(4.8f, 3.4f, -1.2f, 4.8f, 3.4f, .6f, .9f, Mat.Air);
        c.Cut(5.6f, 4.6f, -1.2f, 7, 6.4f, .6f);
        c.Rod(-4.6f, .6f, 2.4f, -1.6f, .6f, 3.8f, .62f, Mat.Ruin);
        c.Rod(1.2f, .6f, 3.6f, 3, .6f, 2.6f, .62f, Mat.Ruin);
        c.Paint(-7, 0, -6, 7, 8, 6, Mat.Moss, .12, 7);
        c.Ball(-2, 0, 1, 1.2f, .7f, 1, Mat.Rubble, Mat.Ruin, .4); c.Ball(5.4f, 0, 3.2f, 1.1f, .6f, 1, Mat.Rubble, Mat.Ruin, .4);
        if (!spent) { c.Box(-.9f, .3f, -.5f, .9f, 1.4f, .7f, Mat.Board); c.Box(-.9f, .6f, -.55f, .9f, .9f, .75f, Mat.Brass); c.Box(-.4f, 1.2f, -.3f, .4f, 1.6f, .3f, Mat.Nugget); }
    }

    static void Den(ModelCanvas c, bool spent)
    {
        Crag(c, 0, -4.6f, 9.5f, 7.6f, 6.6f, Mat.Rock, Mat.Moss, .4f);
        Mouth(c, .4f, -1.4f, 3.8f, 3.6f, 4.6f, false);
        // what was eaten before it: a carcass's ribs, bones; claw marks on the stone
        if (!spent)
        {
            c.Ball(-3.4f, .4f, 3.2f, 1.4f, .7f, .9f, Mat.Flesh, Mat.Blood, .3);
            for (int k = 0; k < 4; k++) c.Rod(-4.4f + k * .6f, .6f, 2.6f, -4.4f + k * .6f, 1.8f, 3.4f, .1f, Mat.Bone);
            BoneHeap(c, 3, 3.4f, 1.1f);
            Skull(c, 1.4f, .45f, 1.8f, .5f);
        }
        for (int k = 0; k < 3; k++) c.Box(-3.2f + k * .5f, 2.6f, -.4f, -3 + k * .5f, 4.4f, -.1f, Mat.StoneDark);
        // bushes grown over its shoulders
        c.Ball(-6, 4.6f, -3, 2, 1.4f, 1.8f, Mat.Foliage, Mat.FoliageLight, .3);
        c.Ball(5.6f, 3.6f, -2.4f, 1.7f, 1.2f, 1.5f, Mat.Foliage, Mat.Juniper, .3);
    }

    static void Trial(ModelCanvas c, bool spent)
    {
        c.Cyl(0, 0, 8.4f, -2, .25f, Mat.Flag);
        c.Paint(-9, -2, -9, 9, .3f, 9, Mat.Moss, .2, 4);
        for (int k = 0; k < 7; k++)
        {
            double a = k * Math.Tau / 7 - Math.PI / 2;
            float x = (float)Math.Cos(a) * 7, z = (float)Math.Sin(a) * 7, h = 6.4f + (k % 2) * 1.4f;
            c.Box(x - .9f, -1, z - .65f, x + .9f, h, z + .65f, Mat.Menhir);
            c.Box(x - .7f, h, z - .5f, x + .7f, h + .6f, z + .5f, Mat.Menhir);
            // a rune cut into its face, lit
            for (float y = 1.4f; y < h - 1; y += 1.3f) c.Box(x - .25f, y, z - .7f, x + .25f, y + .6f, z + .7f, spent ? Mat.Char : Mat.SoulFire);
        }
        c.Box(-1.5f, .25f, -1.1f, 1.5f, 1.8f, 1.1f, Mat.Hewn);
        c.Box(-1.7f, 1.8f, -1.3f, 1.7f, 2.2f, 1.3f, Mat.StoneLight);
        if (!spent) { c.Cone(0, 0, .8f, 2.2f, 2.4f, Mat.SoulFire); c.Cone(.4f, .2f, .45f, 2.2f, 3.2f, Mat.SoulFire); }
        else c.Box(-.6f, 2.2f, -.6f, .6f, 2.5f, .6f, Mat.Ash);
    }

    static void Signpost(ModelCanvas c)
    {
        c.Ball(0, 0, 0, 1.2f, .7f, 1.1f, Mat.Stone, Mat.StoneLight, .4);
        c.Box(-.25f, 0, -.25f, .25f, 6, .25f, Mat.Wood);
        // its boards, pointing their ways
        c.Box(.25f, 4.6f, -.15f, 3, 5.4f, .15f, Mat.Signboard); c.Box(3, 4.75f, -.15f, 3.4f, 5.25f, .15f, Mat.Signboard);
        c.Box(-3, 3.4f, -.15f, -.25f, 4.2f, .15f, Mat.Signboard); c.Box(-3.4f, 3.55f, -.15f, -3, 4.05f, .15f, Mat.Signboard);
        c.Box(-.15f, 2.4f, .25f, .15f, 3.1f, 2.4f, Mat.Signboard);
        c.Box(-.4f, 6, -.4f, .4f, 6.3f, .4f, Mat.Wood);
    }

    static void Learning(ModelCanvas c)
    {
        c.Box(-3.4f, -2, -3.4f, 3.4f, .5f, 3.4f, Mat.Hewn);
        c.Box(-2.6f, .5f, -2.6f, 2.6f, 1, 2.6f, Mat.Hewn);
        // the stone itself, tapering, its runes lit; a lectern with the open book
        for (float y = 1; y < 12; y += .34f)
        {
            float hw = 1.3f - (y - 1) * .06f;
            c.Box(-hw, y, -hw * .8f, hw, y + .34f, hw * .8f, Mat.Menhir);
        }
        c.Pyramid(0, 0, .65f, 12, 1.4f, Mat.Menhir);
        for (float y = 2; y < 11; y += 1.6f) { c.Box(-.35f, y, .85f, .35f, y + .9f, 1.1f, Mat.Crystal); c.Box(-.2f, y + .3f, -1.1f, .2f, y + .7f, -.85f, Mat.Crystal); }
        c.Box(2, 1, 2, 2.4f, 2.4f, 2.4f, Mat.Wood);
        c.Box(1.4f, 2.4f, 1.7f, 3, 2.7f, 2.7f, Mat.BoardDark);
        c.Box(1.5f, 2.7f, 1.8f, 2.9f, 2.9f, 2.6f, Mat.Linen);
    }

    static void Stables(ModelCanvas c)
    {
        c.Box(-8, -2, -5.4f, 8, .2f, 5.4f, Mat.Trodden);
        c.Box(-7, -2, -5, 4, 3.4f, -.6f, Mat.Board);
        c.Gable(-7, -5, 4, -.6f, 3.4f, Mat.Thatch, true, 1, .6f, Mat.Board);
        for (int k = 0; k < 4; k++) { float x = -6.2f + k * 2.6f; c.Box(x, 0, -.65f, x + 1.6f, 2.4f, -.4f, Mat.BoardDark); c.Box(x, 1.2f, -.7f, x + 1.6f, 1.4f, -.35f, Mat.Board); }
        c.Box(-2.4f, 4.4f, -.75f, -1, 5.4f, -.5f, Mat.Hay);
        // the paddock, its horses, the trough and bales
        Fence(c, -7, 1, 7, 1); Fence(c, 7, 1, 7, 5); Fence(c, -7, 5, 7, 5); Fence(c, -7, 1, -7, 5);
        Horse(c, -3.6f, 3, Mat.HorseBay, true); Horse(c, 2.2f, 3.2f, Mat.HorseGrey, false);
        c.Box(4.8f, 0, 1.6f, 6.4f, .9f, 2.4f, Mat.BoardDark); c.Box(5, .55f, 1.7f, 6.2f, .85f, 2.3f, Mat.Water);
        c.Box(5.2f, 0, -4.2f, 7.4f, 1.2f, -2.6f, Mat.Bale); c.Box(5.6f, 1.2f, -3.9f, 7, 2.2f, -2.9f, Mat.Bale);
    }

    /// <summary>A horse standing, its body along x, its head to the east or the west.</summary>
    static void Horse(ModelCanvas c, float x, float z, byte coat, bool east)
    {
        float s = east ? 1 : -1;
        c.Ball(x, 2.2f, z, 1.6f, .75f, .62f, coat, rough: 0);
        foreach (var (dx, dz) in new[] { (-1f, -.35f), (-1f, .35f), (1f, -.35f), (1f, .35f) }) c.Box(x + dx - .17f, 0, z + dz - .17f, x + dx + .17f, 1.8f, z + dz + .17f, coat);
        c.Rod(x + s * 1.3f, 2.5f, z, x + s * 2, 3.6f, z, .38f, coat);
        c.Ball(x + s * 2.4f, 3.5f, z, .62f, .38f, .32f, coat, rough: 0);
        c.Rod(x + s * 1.4f, 3, z, x + s * 1.9f, 4, z, .16f, Mat.Hair);
        c.Rod(x - s * 1.6f, 2.4f, z, x - s * 1.9f, 1.2f, z, .16f, Mat.Hair);
    }

    static void Observatory(ModelCanvas c)
    {
        c.Box(-4.6f, -2, -4.6f, 4.6f, .4f, 4.6f, Mat.Hewn);
        c.Cyl(0, -.6f, 3.1f, -2, 13, Mat.StoneLight);
        for (float y = 2.5f; y < 13; y += 3.4f) c.Cyl(0, -.6f, 3.3f, y, y + .5f, Mat.Hewn);
        c.Box(-.3f, 5, 2.35f, .3f, 6.4f, 2.6f, Mat.Window); c.Box(-.3f, 9, 2.35f, .3f, 10.4f, 2.6f, Mat.Window);
        c.Box(-.9f, 0, 2.3f, .9f, 2.6f, 2.6f, Mat.Gate);
        // the dome, its slit, the great glass looking out of it
        c.Ball(0, 13, -.6f, 3.4f, 3.1f, 3.4f, Mat.Slate, rough: 0);
        c.Cut(-4, 9, -5, 4, 13, 4);
        c.Box(-3.4f, 12.6f, -4, 3.4f, 13.2f, 2.8f, Mat.Hewn);
        c.Box(-.4f, 13, -4.2f, .4f, 16.4f, 3, Mat.Brass);
        c.Rod(0, 14.4f, -.6f, 3.4f, 18.4f, 1.6f, .55f, Mat.Brass);
        c.Rod(3.2f, 18.2f, 1.5f, 3.6f, 18.7f, 1.7f, .6f, Mat.SteelDark);
        House(c, 2.6f, -3.6f, 6.4f, .6f, 2.8f, Mat.StoneLight, Mat.Slate, chimney: false);
    }

    static void Pass(ModelCanvas c, byte own)
    {
        // a wall across the way between two square towers, a gate in it
        c.Box(-11, -2, -1.6f, 11, 6.4f, 1.6f, Mat.StoneDark);
        c.Paint(-11.1f, -2, -1.7f, 11.1f, 6.4f, 1.7f, Mat.Stone, .35, 3);
        Merlons(c, -11, 1.4f, 11, 1.4f, 6.4f, Mat.StoneDark);
        foreach (var s in new[] { -1, 1 })
        {
            float x = s * 5.2f;
            c.Box(x - 2, -2, -2.4f, x + 2, 10.4f, 2.4f, Mat.StoneDark);
            c.Paint(x - 2.1f, -2, -2.5f, x + 2.1f, 10.4f, 2.5f, Mat.Stone, .3, 5);
            Merlons(c, x - 2, 2.25f, x + 2, 2.25f, 10.4f, Mat.StoneDark);
            c.Pyramid(x, 0, 2.3f, 10.6f, 4.2f, Mat.Slate);
            c.Box(x - .2f, 6.6f, 2.35f, x + .2f, 7.8f, 2.55f, Mat.Window);
        }
        c.Cut(-1.9f, 0, -2.6f, 1.9f, 4.2f, 2.6f);
        c.Rod(0, 4.2f, -2.6f, 0, 4.2f, 2.6f, 1.9f, Mat.Air);
        c.Box(-1.9f, 0, -.3f, 1.9f, 4.3f, .1f, Mat.Gate);
        Hanging(c, 0, 6, 1.6f, own == 0 ? (byte)1 : own, 1.4f, 2.4f);
        c.Flag(5.2f, 14.8f, 0, own, 3, 2.4f);
        Torch(c, -2.6f, 2.6f, 2.8f); Torch(c, 2.6f, 2.6f, 2.8f);
    }

    // ------------------------------------------------------------ the farms

    /// <summary>A field: furrows of a crop (ripe wheat, green rows, cabbages or pumpkins) in a low fence.</summary>
    static void Field(ModelCanvas c, int v)
    {
        const float W = 5, D = 4;
        c.Box(-W, -1.2f, -D, W, .15f, D, Mat.Soil);
        int crop = v % 4;
        for (float z = -D + .4f; z < D - .4f; z += 1.34f)
        {
            switch (crop)
            {
                case 0: c.Box(-W + .4f, .15f, z, W - .4f, 1.1f, z + .9f, Mat.Wheat); break;
                case 1: c.Box(-W + .4f, .15f, z, W - .4f, .6f, z + .7f, Mat.GrassLush); break;
                case 2: for (float x = -W + .9f; x < W - .6f; x += 1.2f) c.Ball(x, .5f, z + .4f, .5f, .4f, .5f, Mat.FoliageLight, Mat.Foliage, .3); break;
                default:
                    c.Box(-W + .4f, .15f, z, W - .4f, .35f, z + .9f, Mat.Foliage);
                    for (float x = -W + 1.2f; x < W - .8f; x += 2.1f) c.Ball(x + (z % 2), .55f, z + .45f, .55f, .45f, .55f, Mat.ClothOchre, rough: 0);
                    break;
            }
        }
        Fence(c, -W, D + .2f, W, D + .2f);
        Fence(c, W + .2f, -D, W + .2f, D);
    }

    static void Haystack(ModelCanvas c, float x, float z)
    {
        c.Cyl(x, z, 1.6f, -1, 1.2f, Mat.Hay);
        c.Cone(x, z, 1.7f, 1.2f, 2.4f, Mat.Hay, .3f, Mat.Straw, .3);
        c.Box(x - .1f, 3.4f, z - .1f, x + .1f, 4.2f, z + .1f, Mat.Wood);
    }

    static void Farmhouse(ModelCanvas c, int v)
    {
        House(c, -3, -2.2f, 3, 2.2f, 2.8f, v % 2 == 0 ? Mat.Daub : Mat.Board, v % 3 == 0 ? Mat.Tile : Mat.Thatch, chimney: true, trim: Mat.BoardDark);
        c.Box(3, -1.6f, -1.8f, 5, 1.8f, 1.6f, Mat.Board);
        c.Gable(3, -1.6f, 5, 1.6f, 1.8f, Mat.Shingle, false, .7f, .3f, Mat.Board);
        Fence(c, -4, 3.4f, 1, 3.4f);
        Barrel(c, -3.6f, 2.6f, 1.2f);
        c.Cyl(4.2f, 2.8f, .7f, 0, .9f, Mat.Stone, .25f, Mat.Water);
    }

    static void Sheep(ModelCanvas c, float x, float z, int v)
    {
        float s = v % 2 == 0 ? 1 : -1;
        c.Ball(x, 1.15f, z, 1, .72f, .66f, Mat.ClothWhite, Mat.Belly, .3, .2f);
        foreach (var (dx, dz) in new[] { (-.55f, -.3f), (-.55f, .3f), (.55f, -.3f), (.55f, .3f) }) c.Box(x + dx - .15f, 0, z + dz - .15f, x + dx + .15f, .7f, z + dz + .15f, Mat.Hair);
        c.Ball(x + s * 1.05f, 1.4f, z, .42f, .36f, .34f, Mat.Hair, rough: 0);
    }

    static void Scarecrow(ModelCanvas c)
    {
        c.Box(-.17f, -1, -.17f, .17f, 4.4f, .17f, Mat.Wood);
        c.Box(-1.6f, 3, -.12f, 1.6f, 3.3f, .12f, Mat.Wood);
        c.Box(-.6f, 1.6f, -.35f, .6f, 3.4f, .35f, Mat.Rag);
        c.Ball(0, 4, 0, .5f, .5f, .5f, Mat.Straw, rough: .1f);
        c.Cone(0, 0, .9f, 4.3f, 1, Mat.Straw);
    }
}
