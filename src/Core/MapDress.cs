using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// What stands on a piece of the big world map, worked out from the looks of its hexes alone (so the same each time):
/// the thing of every hex seen at its middle (MapModels.cs); woods thick with trees of the ground's kinds — spruce, oak
/// and birch on the meadows, pine and palm by the sand, firs in the snow, willows over the swamp, the horde's burnt trunks
/// — in clumps of a kind, with bushes, stones and stumps between, spilling over a wood's edge into the open land beside
/// it; on open land a bush, a stone, a clump of flowers, now and then a tree alone; boulders on the hills; reeds where
/// the water is shallow, lilies a little further out; fields, ricks, a farmhouse, sheep and apple trees about our castle
/// and a field by an outpost; bones and totems about the horde's towns. All of it is joined into one mesh for near and
/// one coarse for far, and one shadow; what turns (sails, wheels) and what smokes or burns is told for the drawing to
/// add. Places are in model units (a voxel of the land at <see cref="HexMapLayout.R"/> 18), the map's voxels being
/// <see cref="Scale"/> of them.
/// </summary>
public static class MapDress
{
    /// <summary>The map's voxels to a model unit; the drawing's voxels to a model unit.</summary>
    public static float Scale => HexMapLayout.R / 18f;
    public const int K = MapModels.ThingK;

    public struct Placed
    {
        public MapModel Model;
        public int Variant, Rot;
        public byte Owner;
        public bool Spent;
        /// <summary>Where it stands, model units: its middle on the ground.</summary>
        public float X, Y, Z;
    }

    /// <summary>What turns on its own: a windmill's sails (its axle north to south), a water wheel (its axle west to east); its hub, the map's voxels.</summary>
    public struct Turner
    {
        public MapModel Model;
        public float X, Y, Z;
        public bool AxleWestEast;
        public int Col, Row;
    }

    public enum PlumeKind : byte { Smoke, Fire, Torch, GreenSmoke, Steam, Ghost }

    /// <summary>A smoke or a fire to be drawn at a point (the map's voxels), so big.</summary>
    public struct Plume
    {
        public PlumeKind Kind;
        public float X, Y, Z, Size;
        public int Col, Row;
    }

    public sealed class Dressed
    {
        /// <summary>The things fine and coarse, what grows fine and coarse, the shadow of all of it coarse.</summary>
        public MeshBuffers Things, ThingsFar, Growth, GrowthFar, Shadow;
        /// <summary>All of it fine (the bench weighs it).</summary>
        public int NearQuads => (Things?.Quads ?? 0) + (Growth?.Quads ?? 0);
        public readonly List<Placed> Placed = new();
        public readonly List<Turner> Turners = new();
        public readonly List<Plume> Plumes = new();
    }

    // ------------------------------------------------------------ the things

    /// <summary>The model of a thing a hex holds; null for none (and for a stack of monsters, drawn as a figure).</summary>
    public static MapModel? ModelOf(HexIcon icon) => icon switch
    {
        HexIcon.Citadel => MapModel.Citadel, HexIcon.Outpost => MapModel.Outpost, HexIcon.OutpostPlace => MapModel.OutpostPlace,
        HexIcon.HordeTown => MapModel.HordeTown, HexIcon.Lair => MapModel.Lair, HexIcon.MineLogs => MapModel.Sawmill,
        HexIcon.MineStone => MapModel.Quarry, HexIcon.MineMetal => MapModel.OreMine, HexIcon.MineGold => MapModel.GoldMine,
        HexIcon.MineMercury => MapModel.Alchemist, HexIcon.MineSulfur => MapModel.SulfurDune, HexIcon.MineCrystal => MapModel.CrystalCave,
        HexIcon.MineGems => MapModel.GemPond, HexIcon.Pile => MapModel.Pile, HexIcon.Chest => MapModel.Chest, HexIcon.Campfire => MapModel.Campfire,
        HexIcon.Windmill => MapModel.Windmill, HexIcon.WaterWheel => MapModel.WaterMill, HexIcon.Shrine => MapModel.Shrine,
        HexIcon.Dwelling => MapModel.Dwelling, HexIcon.DeadMine => MapModel.DeadMine, HexIcon.Ruins => MapModel.Ruins, HexIcon.Den => MapModel.Den,
        HexIcon.Trial => MapModel.Trial, HexIcon.Signpost => MapModel.Signpost, HexIcon.Learning => MapModel.Learning,
        HexIcon.Stables => MapModel.Stables, HexIcon.Observatory => MapModel.Observatory, HexIcon.Pass => MapModel.Pass,
        _ => null,
    };

    /// <summary>Whether a hex's thing is drawn: a pile or a chest once taken is gone.</summary>
    public static bool Shows(in HexLook l) => ModelOf(l.Icon) != null && !(HexTiles.Spent(l) && l.Icon is HexIcon.Pile or HexIcon.Chest);

    /// <summary>Whether a hex's thing turns: a windmill's sails or a water wheel, not once its week's is given.</summary>
    public static bool Turns(in HexLook l) => l.Icon is HexIcon.Windmill or HexIcon.WaterWheel && !HexTiles.Spent(l);

    /// <summary>Where a thing's smokes and fires are, model units from its middle.</summary>
    static IEnumerable<(PlumeKind, float, float, float, float)> PlumesOf(MapModel m, bool spent)
    {
        switch (m)
        {
            case MapModel.Citadel:
                yield return (PlumeKind.Smoke, -6.7f, 8, -1.5f, 1); yield return (PlumeKind.Smoke, -13.3f, 6.3f, 4.3f, .8f); yield return (PlumeKind.Smoke, 14.9f, 5.7f, 2.9f, .8f);
                yield return (PlumeKind.Torch, -2.4f, 3.5f, 11.2f, .5f); yield return (PlumeKind.Torch, 2.4f, 3.5f, 11.2f, .5f); break;
            case MapModel.HordeTown: yield return (PlumeKind.Fire, 0, 1.2f, 1.5f, 1.6f); break;
            case MapModel.Lair: yield return (PlumeKind.Torch, -3.6f, 3.8f, 1.6f, .55f); yield return (PlumeKind.Torch, 3.6f, 3.8f, 1.6f, .55f); break;
            case MapModel.OreMine: yield return (PlumeKind.Smoke, 6.2f, 8.4f, -2.5f, 1.1f); yield return (PlumeKind.Torch, -3.8f, 3.3f, -.2f, .45f); break;
            case MapModel.GoldMine: yield return (PlumeKind.Torch, -3.8f, 3.3f, -.2f, .45f); yield return (PlumeKind.Torch, 1.4f, 3.3f, -.2f, .45f); break;
            case MapModel.Alchemist: yield return (PlumeKind.GreenSmoke, 5.4f, 9.6f, -2.4f, .9f); break;
            case MapModel.SulfurDune: yield return (PlumeKind.Steam, -2, 2.6f, 1.4f, 1); yield return (PlumeKind.Steam, 3.4f, 2.6f, -1.6f, .9f); yield return (PlumeKind.Steam, -5.4f, 2.6f, -3, .8f); break;
            case MapModel.Campfire: if (!spent) yield return (PlumeKind.Fire, 0, 1, 0, 1); break;
            case MapModel.Farmhouse: yield return (PlumeKind.Smoke, 1.9f, 6.6f, -.6f, .7f); break;
            case MapModel.Dwelling: yield return (PlumeKind.Smoke, .3f, 7, -3, .7f); break;
            case MapModel.Shrine: yield return (PlumeKind.Ghost, 0, 4, 0, .7f); break;
            case MapModel.Trial: if (!spent) yield return (PlumeKind.Ghost, 0, 3.6f, 0, 1); break;
            case MapModel.DeadMine: yield return (PlumeKind.Ghost, 0, 1.2f, -4.4f, .5f); break;
            case MapModel.Outpost: yield return (PlumeKind.Torch, -1.6f, 3.4f, 7.8f, .4f); yield return (PlumeKind.Torch, 1.6f, 3.4f, 7.8f, .4f); break;
            case MapModel.Pass: yield return (PlumeKind.Torch, -2.6f, 3.6f, 2.6f, .45f); yield return (PlumeKind.Torch, 2.6f, 3.6f, 2.6f, .45f); break;
        }
    }

    // ------------------------------------------------------------ a piece dressed

    /// <summary>How far from its middle a thing stands as it is drawn, model units: nothing grows nor is put within it.</summary>
    static float Reach(MapModel m) => MapModels.Size(m) * m switch
    {
        MapModel.Citadel => 15, MapModel.HordeTown => 13, MapModel.Pile or MapModel.Chest or MapModel.Campfire or MapModel.Signpost or MapModel.Learning => 3,
        MapModel.OutpostPlace => 6.5f, MapModel.Pass => 11, _ => 9,
    };

    /// <summary>
    /// How far about a hex's middle the land is made level under its thing, the map's voxels (HexTiles.cs): the
    /// ground under its model, its footing a little beyond, a castle and its walls nearly the whole hex.
    /// </summary>
    public static float LevelOf(HexIcon icon) => ModelOf(icon) is { } m ? Math.Clamp((Reach(m) - 1) * Scale, 6, 14.5f) : 9.5f;

    /// <summary>How far south of a hex's middle one stands before its thing, the map's voxels: at its gate, the thing whole to the eye.</summary>
    public static float StandOff(HexIcon icon) => ModelOf(icon) is { } m && icon is not (HexIcon.Pile or HexIcon.Chest or HexIcon.Campfire or HexIcon.Signpost)
        ? MathF.Min(Reach(m) + 1.5f, 16.5f) * Scale : 0;

    /// <summary>A hash of a hex and a number, 0…1.</summary>
    static double H(int col, int row, int k, int salt) => Hash.H3(col * 92821 + k * 7, row * 68917 + salt, k * 31 + salt * 3);

    public static Dressed Dress(IHexSource src, HexMapLayout lay, int tx, int tz)
    {
        var d = new Dressed();
        float x0 = tx * HexMapLayout.Tile, z0 = tz * HexMapLayout.Tile, x1 = x0 + HexMapLayout.Tile, z1 = z0 + HexMapLayout.Tile;
        var hexes = new List<(int Col, int Row)>();
        lay.HexesIn(x0 - HexMapLayout.R, z0 - HexMapLayout.R, x1 + HexMapLayout.R, z1 + HexMapLayout.R, hexes);
        foreach (var (col, row) in hexes)
        {
            var (cx, cz) = lay.Center(col, row);
            // a hex is the piece's where its middle lies
            if (cx < x0 || cx >= x1 || cz < z0 || cz >= z1) continue;
            Hex(src, lay, col, row, cx, cz, d);
        }
        d.Things = Join(d.Placed, coarse: false, shadow: false, grows: false);
        d.ThingsFar = Join(d.Placed, coarse: true, shadow: false, grows: false);
        d.Growth = Join(d.Placed, coarse: false, shadow: false, grows: true);
        d.GrowthFar = Join(d.Placed, coarse: true, shadow: false, grows: true);
        d.Shadow = Join(d.Placed, coarse: true, shadow: true, grows: null);
        return d;
    }

    /// <summary>A hex dressed: its thing, what grows on it, its farm or the horde's bones, its shore.</summary>
    static void Hex(IHexSource src, HexMapLayout lay, int col, int row, float cx, float cz, Dressed d)
    {
        var l = src.Look(col, row);
        float s = Scale;
        bool seen = src.Seen(col, row), thing = false;
        int vr = HexTiles.Vary(col, row);
        var spots = new List<(float X, float Z, float R)>();

        void Put(MapModel m, float ux, float uz, int variant, int rot = 0, byte owner = 0, bool spent = false, float lift = 0)
        {
            float mx = cx + ux * s, mz = cz + uz * s;
            var c = HexTiles.ColumnAt(src, lay, (int)MathF.Floor(mx), (int)MathF.Floor(mz));
            d.Placed.Add(new Placed { Model = m, Variant = variant, Rot = rot, Owner = owner, Spent = spent, X = mx / s, Y = c.Top / s + lift, Z = mz / s });
        }

        // the thing at its middle, if it is seen
        if (seen && Shows(l))
        {
            var m = ModelOf(l.Icon).Value;
            bool spent = HexTiles.Spent(l);
            int variant = m == MapModel.Pile ? vr % 8 : vr % 3;
            var c = HexTiles.ColumnAt(src, lay, (int)cx, (int)cz);
            float y = c.Top / s;
            d.Placed.Add(new Placed { Model = m, Variant = variant, Owner = l.Owner, Spent = spent, X = cx / s, Y = y, Z = cz / s });
            thing = true;
            float z = MapModels.Size(m);
            spots.Add((0, 0, Reach(m)));
            foreach (var (k, px, py, pz, size) in PlumesOf(m, spent))
                d.Plumes.Add(new Plume { Kind = k, X = cx + px * z * s, Y = (y + py * z) * s, Z = cz + pz * z * s, Size = size * z, Col = col, Row = row });
            if (Turns(l))
            {
                if (m == MapModel.Windmill) d.Turners.Add(new Turner { Model = MapModel.Sails, X = cx, Y = (y + 11.4f * z) * s, Z = cz + 4.1f * z * s, Col = col, Row = row });
                else d.Turners.Add(new Turner { Model = MapModel.Wheel, X = cx + 5.2f * z * s, Y = (y + 3.2f * z) * s, Z = cz, AxleWestEast = true, Col = col, Row = row });
            }
        }
        else if (l.Icon != HexIcon.None && l.Icon != HexIcon.Monster) spots.Add((0, 0, 11.5f));
        // the things of the hexes about it reach into it: room left for them
        for (int side = 0; side < 6; side++)
        {
            var (nc, nr) = HexMapLayout.Neighbour(col, row, side);
            if (!lay.OnField(nc, nr)) continue;
            var n = src.Look(nc, nr);
            if (!Shows(n) || !src.Seen(nc, nr)) continue;
            var (tx, tz) = HexMapLayout.Toward(side);
            spots.Add((tx / s, tz / s, Reach(ModelOf(n.Icon).Value)));
        }

        bool wet = l.Ground is HexGround.Water or HexGround.DeepWater;
        if (wet) { Shore(src, lay, col, row, cx, cz, l, d, deep: true); return; }
        if (l.Relief == HexRelief.Mountain) return;

        // a place free for something of a radius (units): on the hex, off its roads and rivers, out of the water, clear of what is there
        bool Free(float ux, float uz, float r, float edge = .6f)
        {
            float dx = ux * s, dz = uz * s;
            if (HexMapLayout.EdgeDistance(dx, dz) < edge * s) return false;
            if (WayNear(l.Roads, dx, dz) < (2 + r * .6f) * s) return false;
            if (WayNear(l.Rivers, dx, dz) < (3.2f + r * .5f) * s) return false;
            foreach (var p in spots) if ((p.X - ux) * (p.X - ux) + (p.Z - uz) * (p.Z - uz) < (p.R + r) * (p.R + r)) return false;
            var c = HexTiles.ColumnAt(src, lay, (int)MathF.Floor(cx + dx), (int)MathF.Floor(cz + dz));
            // (nor on a bridge, nor up a mountain's rock beyond its foothills)
            return c.Water <= c.Top && !c.Bridge && c.Mount <= 5;
        }
        bool Try(MapModel m, float ux, float uz, float r, int variant, int rot = 0, float edge = .6f)
        {
            if (!Free(ux, uz, r, edge)) return false;
            spots.Add((ux, uz, r));
            Put(m, ux, uz, variant, rot);
            return true;
        }
        (float, float) Somewhere(int k, int salt, float reach = 15.2f)
        {
            double a = H(col, row, k, salt) * Math.Tau, r = Math.Sqrt(H(col, row, k, salt + 1)) * reach;
            return ((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r));
        }

        var g = l.Ground;
        // what lies beside: our castle (its farms), an outpost (a field), the horde's towns (their bones)
        HexIcon near = HexIcon.None;
        int castleSide = -1;
        for (int side = 0; side < 6; side++)
        {
            var (nc, nr) = HexMapLayout.Neighbour(col, row, side);
            if (!lay.OnField(nc, nr)) continue;
            var n = src.Look(nc, nr);
            if (n.Icon is HexIcon.Citadel) { near = HexIcon.Citadel; castleSide = side; }
            else if (n.Icon is HexIcon.Outpost && near != HexIcon.Citadel) near = HexIcon.Outpost;
            else if (n.Icon is HexIcon.HordeTown or HexIcon.Lair && near == HexIcon.None) near = HexIcon.HordeTown;
        }
        bool tilled = g is HexGround.Grass or HexGround.Dirt && !thing;
        // ---- a wood: thick, of the ground's kinds in clumps, bushes and stones between; by our castle it is cleared for
        // the fields on the castle's side, the trees left on the far side
        if (l.Relief == HexRelief.Forest)
        {
            int want = 10 + vr % 4, put = 0;
            float awayX = 0, awayZ = 0;
            if (tilled && near == HexIcon.Citadel)
            {
                Farm(col, row, vr, rich: true);
                var (tx, tz) = HexMapLayout.Toward(castleSide);
                float len = MathF.Sqrt(tx * tx + tz * tz);
                awayX = -tx / len; awayZ = -tz / len;
                want = 6 + vr % 3;
            }
            for (int k = 0; k < 170 && put < want; k++)
            {
                var (ux, uz) = Somewhere(k, 1, 16.4f);
                if (thing && ux * ux + uz * uz < 12 * 12) continue;
                if ((awayX != 0 || awayZ != 0) && ux * awayX + uz * awayZ < 5) continue;
                var tree = TreeOf(g, Clump(cx + ux * s, cz + uz * s, (int)g), H(col, row, k, 9));
                if (Try(tree, ux, uz, Girth(tree), (int)(H(col, row, k, 4) * 6), (int)(H(col, row, k, 5) * 4), .1f)) put++;
            }
            for (int k = 0; k < 40 && put < want + 3; k++)
            {
                var (ux, uz) = Somewhere(k, 21, 16);
                var low = BushOf(g, H(col, row, k, 22));
                if (Try(low, ux, uz, 1.3f, (int)(H(col, row, k, 23) * 6), (int)(H(col, row, k, 24) * 4), .4f)) put++;
            }
            for (int k = 0; k < 6; k++)
            {
                var (ux, uz) = Somewhere(k, 31, 15);
                if (Try(k % 2 == 0 ? (g == HexGround.Snow ? MapModel.SnowRock : MapModel.MossRock) : MapModel.Stump, ux, uz, 1.2f, k, k % 4)) break;
            }
            return;
        }
        // ---- the open land beside a wood: its edge spills over a little
        for (int side = 0; side < 6; side++)
        {
            var (nc, nr) = HexMapLayout.Neighbour(col, row, side);
            var n = HexTiles.LookOf(src, nc, nr);
            if (n.Relief != HexRelief.Forest || !lay.OnField(nc, nr)) continue;
            var (tx, tz) = HexMapLayout.Toward(side);
            float len = MathF.Sqrt(tx * tx + tz * tz), ox = tx / len, oz = tz / len;
            int many = (int)(H(col, row, side, 41) * 2.4);
            for (int k = 0; k < 6 && many > 0; k++)
            {
                float along = 10.5f + (float)H(col, row, side * 10 + k, 42) * 4.5f, across = ((float)H(col, row, side * 10 + k, 43) - .5f) * 15;
                float ux = ox * along - oz * across, uz = oz * along + ox * across;
                var tree = H(col, row, side * 10 + k, 44) < .3 ? BushOf(n.Ground, H(col, row, k, 45)) : TreeOf(n.Ground, Clump(cx + ux * s, cz + uz * s, (int)n.Ground), H(col, row, k, 46));
                if (Try(tree, ux, uz, Girth(tree), (int)(H(col, row, k, 47) * 6), (int)(H(col, row, k, 48) * 4), .1f)) many--;
            }
        }
        // ---- the farms about our castle, a field by an outpost; the horde's bones about its towns
        if (tilled && near == HexIcon.Citadel) { Farm(col, row, vr, rich: true); return; }
        if (tilled && near == HexIcon.Outpost) Farm(col, row, vr, rich: false);
        if (near == HexIcon.HordeTown || g is HexGround.Scorched or HexGround.Lava)
        {
            if (H(col, row, 0, 61) < (near == HexIcon.HordeTown ? .6 : .04)) { var (ux, uz) = Somewhere(0, 62, 12); Try(MapModel.Totem, ux, uz, 2, vr % 3); }
            if (H(col, row, 0, 63) < (near == HexIcon.HordeTown ? .6 : .07)) { var (ux, uz) = Somewhere(0, 64, 13); Try(MapModel.BoneHeap, ux, uz, 2, 0, vr % 4); }
        }
        // ---- a hex's own: boulders on the hills, a bush, a stone, flowers, now and then a tree alone
        if (l.Relief == HexRelief.Hills)
        {
            int many = 2 + vr % 2;
            for (int k = 0; k < 14 && many > 0; k++)
            {
                var (ux, uz) = Somewhere(k, 71, 13);
                var rock = g == HexGround.Snow ? MapModel.SnowRock : k % 3 == 0 ? MapModel.Boulder : g is HexGround.Grass or HexGround.Dirt ? MapModel.MossRock : MapModel.Rock;
                if (Try(rock, ux, uz, rock == MapModel.Boulder ? 3 : 1.6f, k, k % 4)) many--;
            }
        }
        // (open land is quiet: a bush in some hexes, flowers in a few, a stone now and then; the woods' edges and the farms give it life)
        int bushes = vr % 9 == 0 ? 2 : vr % 4 == 0 ? 1 : 0;
        if (g == HexGround.Swamp) bushes++;
        for (int k = 0; k < 16 && bushes > 0; k++)
        {
            var (ux, uz) = Somewhere(k, 81);
            if (Try(BushOf(g, H(col, row, k, 82)), ux, uz, 1.4f, k, k % 4)) bushes--;
        }
        if (g is HexGround.Grass && vr % 7 == 3) { var (ux, uz) = Somewhere(1, 91, 12); Try(MapModel.Flowers, ux, uz, 2.5f, vr % 4, vr % 4); }
        if (g is HexGround.Grass or HexGround.Dirt or HexGround.Rough or HexGround.Sand && vr % 9 == 1) { var (ux, uz) = Somewhere(2, 92, 13); Try(g == HexGround.Grass ? MapModel.MossRock : MapModel.Rock, ux, uz, 2, vr, vr % 4); }
        if (g == HexGround.Rough && vr % 3 == 0) { var (ux, uz) = Somewhere(3, 93, 13); Try(MapModel.Boulder, ux, uz, 3.5f, vr, vr % 4); }
        // a tree alone, or two: oaks and birches on the meadows, palms by the sand, firs in the snow, willows over the swamp
        if (H(col, row, 0, 101) < (g == HexGround.Swamp ? .6 : g is HexGround.Grass or HexGround.Dirt ? .18 : .2))
        {
            int many = g == HexGround.Swamp ? 2 : 1 + (int)(H(col, row, 0, 102) * 1.6);
            for (int k = 0; k < 12 && many > 0; k++)
            {
                var (ux, uz) = Somewhere(k, 103, 13);
                var tree = g switch
                {
                    HexGround.Swamp => k % 2 == 0 ? MapModel.Willow : MapModel.DeadTree,
                    HexGround.Sand => MapModel.Palm, HexGround.Snow => MapModel.SnowFir,
                    HexGround.Scorched or HexGround.Lava => MapModel.Charred, HexGround.Rough => MapModel.Pine,
                    _ => H(col, row, k, 104) < .55 ? MapModel.Oak : MapModel.Birch,
                };
                if (Try(tree, ux, uz, Girth(tree), k, k % 4)) many--;
            }
        }
        // the swamp's pools and every shore: reeds in the shallows, lilies further out
        if (g == HexGround.Swamp || l.Rivers != 0 || HasWaterBeside(src, col, row)) Shore(src, lay, col, row, cx, cz, l, d, deep: false);

        void Farm(int c, int r, int v, bool rich)
        {
            // the fields first, square to the land, then a farmhouse, ricks, sheep, an apple tree or two
            (float, float)[] slots = { (-7.5f, -5.5f), (7.5f, -5.5f), (-7.5f, 5.5f), (7.5f, 5.5f), (0, -9), (0, 9), (-10, 0), (10, 0) };
            int fields = rich ? 1 + v % 2 : 1, house = rich && v % 3 != 0 ? 1 : 0;
            for (int k = 0; k < slots.Length && fields > 0; k++)
            {
                var (ux, uz) = slots[(k + v) % slots.Length];
                if (Try(MapModel.Field, ux, uz, 5.6f, (v + k) % 4, ((v + k) & 1) * 2, 1.5f)) fields--;
            }
            for (int k = 0; k < slots.Length && house > 0; k++)
            {
                var (ux, uz) = slots[(k + v + 3) % slots.Length];
                if (Try(MapModel.Farmhouse, ux, uz, 4.6f, v, 0, 1.5f)) house--;
            }
            int ricks = rich ? 1 + v % 2 : v % 2, sheep = rich ? 2 + v % 3 : 0, apples = rich ? 1 + v % 2 : 0;
            for (int k = 0; k < 20 && ricks + sheep + apples > 0; k++)
            {
                var (ux, uz) = Somewhere(k, 51, 13);
                if (ricks > 0 && Try(MapModel.Haystack, ux, uz, 1.9f, k)) { ricks--; continue; }
                if (sheep > 0 && Try(MapModel.Sheep, ux, uz, 1.2f, k, k % 4)) { sheep--; continue; }
                if (apples > 0 && Try(MapModel.FruitTree, ux, uz, 2.4f, k, k % 4)) apples--;
            }
            if (rich && v % 2 == 0) { var (ux, uz) = Somewhere(0, 55, 12); Try(MapModel.Scarecrow, ux, uz, 1, 0); }
        }
    }

    /// <summary>
    /// The water's edge of a hex: reeds where it is shallow (a voxel or two over its bed), lilies on it a little further
    /// out; a lake's hex all about its shore, a swamp's pools and a river's banks a few of each.
    /// </summary>
    static void Shore(IHexSource src, HexMapLayout lay, int col, int row, float cx, float cz, in HexLook l, Dressed d, bool deep)
    {
        float s = Scale;
        // (the shore's own reed beds are the land's, HexTiles.cs: here only reeds standing out in the water, and lilies)
        int reeds = deep ? 1 + HexTiles.Vary(col, row) % 2 : 0, lilies = deep ? 3 : 1;
        var taken = new List<(float, float)>();
        for (int k = 0; k < 60 && reeds + lilies > 0; k++)
        {
            double a = H(col, row, k, 111) * Math.Tau, r = (deep ? .45 : .1) + H(col, row, k, 112) * (deep ? .5 : .78);
            float ux = (float)(Math.Cos(a) * r * 17), uz = (float)(Math.Sin(a) * r * 17);
            float mx = cx + ux * s, mz = cz + uz * s;
            if (HexMapLayout.EdgeDistance(ux * s, uz * s) < 1) continue;
            var c = HexTiles.ColumnAt(src, lay, (int)MathF.Floor(mx), (int)MathF.Floor(mz));
            int depth = c.Water - c.Top;
            bool near = false;
            foreach (var (tx, tz) in taken) if ((tx - ux) * (tx - ux) + (tz - uz) * (tz - uz) < 16) { near = true; break; }
            if (near || c.Water == 0) continue;
            if (reeds > 0 && depth >= 1 && depth <= 2)
            {
                d.Placed.Add(new Placed { Model = MapModel.Reeds, Variant = k % 6, Rot = k % 4, X = mx / s, Y = c.Top / s, Z = mz / s });
                taken.Add((ux, uz)); reeds--;
            }
            else if (lilies > 0 && depth >= 2 && depth <= 5)
            {
                d.Placed.Add(new Placed { Model = MapModel.Lily, Variant = k % 6, Rot = k % 4, X = mx / s, Y = c.Water / s, Z = mz / s });
                taken.Add((ux, uz)); lilies--;
            }
        }
    }

    static bool HasWaterBeside(IHexSource src, int col, int row)
    {
        for (int side = 0; side < 6; side++)
        {
            var (nc, nr) = HexMapLayout.Neighbour(col, row, side);
            if (HexTiles.LookOf(src, nc, nr).Ground is HexGround.Water or HexGround.DeepWater) return true;
        }
        return false;
    }

    /// <summary>Which of the ground's trees grows at a place: by a noise in patches, so a wood is clumps of a kind.</summary>
    static MapModel TreeOf(HexGround g, double clump, double h) => g switch
    {
        HexGround.Snow => h < .88 ? MapModel.SnowFir : MapModel.DeadTree,
        HexGround.Swamp => clump < .45 ? MapModel.Willow : clump < .75 ? MapModel.Birch : MapModel.DeadTree,
        HexGround.Sand => clump < .45 ? MapModel.Palm : clump < .8 ? MapModel.Pine : MapModel.Scrub,
        HexGround.Rough => clump < .55 ? MapModel.Pine : clump < .85 ? MapModel.Spruce : MapModel.DeadTree,
        HexGround.Scorched or HexGround.Lava => h < .8 ? MapModel.Charred : MapModel.DeadTree,
        HexGround.Dirt => clump < .4 ? MapModel.Oak : clump < .7 ? MapModel.Birch : MapModel.Pine,
        _ => clump < .48 ? MapModel.Spruce : clump < .72 ? MapModel.Oak : clump < .9 ? MapModel.Birch : MapModel.Pine,
    };

    static MapModel BushOf(HexGround g, double h) => g switch
    {
        HexGround.Snow => MapModel.SnowRock,
        HexGround.Sand or HexGround.Rough or HexGround.Scorched or HexGround.Lava => h < .7 ? MapModel.Scrub : MapModel.Rock,
        _ => h < .8 ? MapModel.Bush : MapModel.Scrub,
    };

    /// <summary>How much room a tree takes, units: its crown's half-width and a little.</summary>
    static float Girth(MapModel m) => MapModels.Size(m) * m switch
    {
        MapModel.Oak or MapModel.Willow => 3.3f, MapModel.Palm => 2.8f, MapModel.Spruce or MapModel.SnowFir => 2.3f,
        MapModel.Pine => 2.4f, MapModel.Birch or MapModel.FruitTree => 1.9f, _ => 1.4f,
    };

    static double Clump(float x, float z, int salt) => HexTiles.Patch((int)x + 4096, (int)z + 4096, 23, 300 + salt);

    /// <summary>How far a point (relative to a hex's middle, the map's voxels) lies from the ways run from its middle out over the sides marked.</summary>
    public static float WayNear(byte sides, float dx, float dz)
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

    // ------------------------------------------------------------ joined into a mesh

    /// <summary>
    /// What is placed joined into one mesh, every model copied where it stands and turned its quarters, in the drawing's
    /// voxels (<see cref="K"/> to a model unit): the fine models near, the coarse ones far, the coarse ones' shadows.
    /// </summary>
    static MeshBuffers Join(List<Placed> placed, bool coarse, bool shadow, bool? grows)
    {
        int quads = 0;
        var made = new MapModels.Made[placed.Count];
        for (int i = 0; i < placed.Count; i++)
        {
            var p = placed[i];
            if (grows != null && MapModels.Grows(p.Model) != grows) continue;
            made[i] = MapModels.Get(p.Model, p.Variant, p.Owner, p.Spent, MapModels.KOf(p.Model, coarse));
            var b = shadow ? made[i].Shadow : made[i].Solid;
            if (b != null) quads += b.Quads;
        }
        if (quads == 0) return null;
        int verts = quads * 4;
        var m = new MeshBuffers
        {
            Pos = new float[verts * 3], Nrm = new float[verts * 3], Idx = new int[quads * 6], Quads = quads,
            Col = shadow ? null : new float[verts * 4], Uv = shadow ? null : new float[verts * 2], Uv2 = shadow ? null : new float[verts * 2],
        };
        int v0 = 0, i0 = 0;
        for (int i = 0; i < placed.Count; i++)
        {
            if (made[i] == null) continue;
            var b = shadow ? made[i].Shadow : made[i].Solid;
            if (b == null) continue;
            var p = placed[i];
            float f = K / (float)made[i].K * MapModels.Size(p.Model), ox = p.X * K, oy = p.Y * K, oz = p.Z * K;
            int v = b.Vertices;
            for (int j = 0; j < v; j++)
            {
                float x = b.Pos[j * 3], y = b.Pos[j * 3 + 1], z = b.Pos[j * 3 + 2];
                float nx = b.Nrm[j * 3], ny = b.Nrm[j * 3 + 1], nz = b.Nrm[j * 3 + 2];
                (x, z) = Turn(x, z, p.Rot); (nx, nz) = Turn(nx, nz, p.Rot);
                int o = (v0 + j) * 3;
                m.Pos[o] = ox + x * f; m.Pos[o + 1] = oy + y * f; m.Pos[o + 2] = oz + z * f;
                m.Nrm[o] = nx; m.Nrm[o + 1] = ny; m.Nrm[o + 2] = nz;
            }
            if (!shadow)
            {
                Array.Copy(b.Col, 0, m.Col, v0 * 4, v * 4);
                Array.Copy(b.Uv, 0, m.Uv, v0 * 2, v * 2);
                Array.Copy(b.Uv2, 0, m.Uv2, v0 * 2, v * 2);
            }
            int n = b.Quads * 6;
            for (int j = 0; j < n; j++) m.Idx[i0 + j] = b.Idx[j] + v0;
            v0 += v; i0 += n;
        }
        return m;
    }

    /// <summary>A point turned so many quarters about the up axis.</summary>
    static (float, float) Turn(float x, float z, int quarters) => (quarters & 3) switch { 1 => (-z, x), 2 => (-x, -z), 3 => (z, -x), _ => (x, z) };
}
