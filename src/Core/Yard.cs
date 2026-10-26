using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What the «Обустройство» tool sets out in the settlement.</summary>
public enum DecorKind : byte { Campfire, Bench, Torch, Brazier, Well, Woodpile, Cart, Barrels, Fence, Signpost, FlowerBed }

/// <summary>
/// The small things a settlement is furnished with between its houses, each a little figure of voxels the workers put
/// together from the stores like anything else: a campfire in a ring of stones with its logs and flames, a bench, a
/// torch on a pole, a brazier on a stone pedestal, a well with its roof and bucket, a woodpile, a cart, barrels and a
/// crate, a length of fence, a signpost, a flower bed. Laid out a quarter turn at a time about the spot clicked, on its
/// floor; what makes light, smoke or a seat of it is told by <see cref="LightAt"/> and <see cref="Seats"/>.
/// </summary>
public static class Yard
{
    public static readonly DecorKind[] All = (DecorKind[])Enum.GetValues(typeof(DecorKind));

    public static string Name(DecorKind k) => k switch
    {
        DecorKind.Campfire => "Костёр", DecorKind.Bench => "Скамья", DecorKind.Torch => "Факел на шесте", DecorKind.Brazier => "Жаровня",
        DecorKind.Well => "Колодец", DecorKind.Woodpile => "Поленница", DecorKind.Cart => "Телега", DecorKind.Barrels => "Бочки и ящик",
        DecorKind.Fence => "Изгородь", DecorKind.Signpost => "Указатель", _ => "Клумба",
    };

    /// <summary>What it is for, for its tooltip.</summary>
    public static string About(DecorKind k) => k switch
    {
        DecorKind.Campfire => "Кольцо камней, поленья и огонь. Светит ночью, дымит. Вечером у костра греются и сидят. Дома рядом платят больше подати.",
        DecorKind.Bench => "На скамье отдыхают свободные люди. Дома рядом платят больше подати.",
        DecorKind.Torch => "Высокий факел на земле. Светит ночью. Дома рядом платят больше подати.",
        DecorKind.Brazier => "Жаровня на каменной тумбе. Светит ночью, у неё греются. Дома рядом платят больше подати.",
        DecorKind.Well => "Каменный сруб с крышей и ведром. Дома рядом платят больше подати.",
        DecorKind.Woodpile => "Сложенные поленья.",
        DecorKind.Cart => "Телега с мешками.",
        DecorKind.Barrels => "Две бочки и ящик.",
        DecorKind.Fence => "Звено изгороди, 2 м.",
        DecorKind.Signpost => "Столб с указателями.",
        _ => "Грядка цветов в каменном бордюре. Дома рядом платят больше подати.",
    };

    static readonly Dictionary<DecorKind, List<(int dx, int dy, int dz, byte m)>> _shapes = new();

    /// <summary>The voxels of a thing about its spot, unturned: x along it, z across (its seats face +z), y up from its floor.</summary>
    public static List<(int dx, int dy, int dz, byte m)> Shape(DecorKind k)
    {
        if (_shapes.TryGetValue(k, out var s)) return s;
        var at = new Dictionary<(int, int, int), byte>();
        void P(int x, int y, int z, byte m) => at[(x, y, z)] = m;
        bool H(int x, int y, int z, int salt, double p) => Hash.H3(x * 7 + salt, y * 13, z * 5) < p;
        switch (k)
        {
            case DecorKind.Campfire:
                for (int z = -5; z <= 5; z++)
                    for (int x = -5; x <= 5; x++)
                    {
                        double r = Math.Sqrt(x * x + z * z);
                        if (r >= 3.4 && r < 4.9)
                        {
                            P(x, 0, z, H(x, 0, z, 1, .5) ? Mat.StoneDark : Mat.Stone);
                            if (H(x, 1, z, 2, .45)) P(x, 1, z, Mat.StoneDark);
                        }
                        else if (r < 3.4) P(x, 0, z, r < 1.6 ? Mat.Ember : Mat.Char);
                    }
                // logs laid crosswise over the embers, two and two, each resting on those under it, and the flames
                // between them
                for (int x = -2; x <= 2; x++) { P(x, 1, -1, Mat.Wood); P(x, 1, 1, Mat.Wood); }
                for (int z = -2; z <= 2; z++) { P(-1, 2, z, Mat.Wood); P(1, 2, z, Mat.Wood); }
                // (flames only where they have the embers or a log to rest on: the fire's height is drawn over them)
                foreach (var (x, y, z) in new[] { (0, 1, 0), (1, 1, 0), (-1, 1, 0), (0, 2, 0), (0, 2, 1), (0, 2, -1) }) P(x, y, z, Mat.Flame);
                break;
            case DecorKind.Bench:
                for (int x = -6; x <= 5; x++)
                    for (int z = -1; z <= 1; z++) P(x, 3, z, Mat.Bench);
                foreach (int x in new[] { -5, 4 })
                    foreach (int z in new[] { -1, 1 })
                        for (int y = 0; y < 3; y++) P(x, y, z, Mat.Bench);
                // the back: two posts and two rails behind the seat
                foreach (int x in new[] { -5, 4 }) for (int y = 3; y <= 6; y++) P(x, y, -2, Mat.Bench);
                for (int x = -6; x <= 5; x++) { P(x, 5, -2, Mat.Bench); P(x, 6, -2, Mat.Bench); }
                break;
            case DecorKind.Torch:
                for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++) P(x, 0, z, Mat.StoneDark);
                for (int y = 1; y <= 16; y++) P(0, y, 0, Mat.Wood);
                for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++) P(x, 17, z, x == 0 && z == 0 ? Mat.Ember : Mat.Iron);
                foreach (var (x, y, z) in new[] { (0, 18, 0), (1, 18, 0), (-1, 18, 0), (0, 18, 1), (0, 18, -1) }) P(x, y, z, Mat.Flame);
                break;
            case DecorKind.Brazier:
                for (int y = 0; y <= 3; y++) for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++) P(x, y, z, Mat.StoneDark);
                for (int z = -2; z <= 2; z++)
                    for (int x = -2; x <= 2; x++)
                        P(x, 4, z, Math.Abs(x) == 2 || Math.Abs(z) == 2 ? Mat.Iron : Mat.Ember);
                foreach (var (x, y, z) in new[] { (0, 5, 0), (1, 5, 0), (-1, 5, 0), (0, 5, 1), (0, 5, -1), (1, 5, 1), (-1, 5, -1) }) P(x, y, z, Mat.Flame);
                break;
            case DecorKind.Well:
                for (int z = -5; z <= 5; z++)
                    for (int x = -5; x <= 5; x++)
                    {
                        double r = Math.Sqrt(x * x + z * z);
                        if (r >= 2.6 && r < 4.3) for (int y = 0; y <= 4; y++) P(x, y, z, H(x, y, z, 3, .45) ? Mat.StoneDark : Mat.Stone);
                        else if (r < 2.6) P(x, 0, z, Mat.Char);
                    }
                foreach (int x in new[] { -4, 4 }) for (int y = 5; y <= 12; y++) P(x, y, 0, Mat.Wood);
                for (int x = -4; x <= 4; x++) P(x, 13, 0, Mat.Wood);
                for (int x = -5; x <= 5; x++)
                {
                    for (int z = -2; z <= 2; z++) P(x, 14, z, Mat.Wood);
                    for (int z = -1; z <= 1; z++) P(x, 15, z, Mat.Wood);
                    P(x, 16, 0, Mat.Wood);
                }
                for (int y = 9; y <= 12; y++) P(0, y, 0, Mat.Bench);
                P(0, 7, 0, Mat.Cask); P(0, 8, 0, Mat.Hoop);
                break;
            case DecorKind.Woodpile:
                for (int x = -5; x <= 5; x++)
                    for (int z = -2; z <= 1; z++)
                        for (int y = 0; y <= 4; y++) P(x, y, z, Mat.Wood);
                for (int x = -4; x <= 4; x++) for (int z = -1; z <= 0; z++) P(x, 5, z, Mat.Wood);
                break;
            case DecorKind.Cart:
                foreach (int z in new[] { -4, 3 })
                    for (int x = -2; x <= 2; x++)
                        for (int y = 0; y <= 4; y++)
                        {
                            double d = Math.Sqrt(x * x + (y - 2) * (y - 2));
                            if (d <= 2.55) P(x, y, z, d > 1.8 ? Mat.Hoop : Mat.Cask);
                        }
                for (int x = -6; x <= 5; x++)
                    for (int z = -3; z <= 2; z++)
                    {
                        P(x, 3, z, Mat.Table); P(x, 4, z, Mat.Table);
                        if (x == -6 || x == 5 || z == -3 || z == 2) P(x, 5, z, Mat.Table);
                    }
                foreach (int z in new[] { -2, 1 }) for (int x = 6; x <= 11; x++) P(x, 3, z, Mat.Bench);
                for (int x = -4; x <= -1; x++) for (int z = -2; z <= 0; z++) for (int y = 5; y <= 6; y++) P(x, y, z, Mat.Sack);
                break;
            case DecorKind.Barrels:
                foreach (var (cx, cz) in new[] { (-3, 0), (2, -1) })
                    for (int z = cz - 2; z <= cz + 2; z++)
                        for (int x = cx - 2; x <= cx + 2; x++)
                        {
                            if ((x - cx) * (x - cx) + (z - cz) * (z - cz) > 5) continue;
                            for (int y = 0; y < 6; y++) P(x, y, z, y is 1 or 4 ? Mat.Hoop : Mat.Cask);
                        }
                for (int x = 0; x <= 3; x++)
                    for (int z = 2; z <= 5; z++)
                        for (int y = 0; y < 4; y++)
                        {
                            int edges = (x == 0 || x == 3 ? 1 : 0) + (z == 2 || z == 5 ? 1 : 0) + (y == 0 || y == 3 ? 1 : 0);
                            P(x, y, z, edges >= 2 ? Mat.CrateEdge : Mat.Crate);
                        }
                break;
            case DecorKind.Fence:
                foreach (int x in new[] { -8, -4, 0, 4, 8 }) for (int y = 0; y <= 7; y++) P(x, y, 0, Mat.Wood);
                for (int x = -8; x <= 8; x++) { P(x, 3, 0, Mat.Wood); P(x, 6, 0, Mat.Wood); }
                break;
            case DecorKind.Signpost:
                for (int y = 0; y <= 14; y++) P(0, y, 0, Mat.Wood);
                for (int x = 1; x <= 6; x++) { P(x, 11, 0, Mat.Table); P(x, 12, 0, Mat.Table); }
                P(7, 11, 0, Mat.Table);
                for (int x = -6; x <= -1; x++) { P(x, 8, 0, Mat.Bench); P(x, 9, 0, Mat.Bench); }
                P(-7, 9, 0, Mat.Bench);
                break;
            default:
                for (int x = -5; x <= 4; x++)
                    for (int z = -3; z <= 2; z++)
                    {
                        bool edge = x == -5 || x == 4 || z == -3 || z == 2;
                        P(x, 0, z, edge ? Mat.StoneDark : Mat.Soil);
                        if (!edge && Hash.H3(x * 11, 5, z * 3) < .7) P(x, 1, z, Hash.H3(x * 3, 9, z * 11) < .55 ? Mat.Flower : Mat.Bloom);
                    }
                break;
        }
        s = new List<(int, int, int, byte)>();
        foreach (var ((x, y, z), m) in at) s.Add((x, y, z, m));
        // from the bottom up, as they are put together
        s.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        _shapes[k] = s;
        return s;
    }

    /// <summary>A point of a thing turned so many quarter turns about its spot.</summary>
    public static (int x, int z) Turn(int dx, int dz, int rot) => (rot & 3) switch
    {
        0 => (dx, dz), 1 => (-dz, dx), 2 => (-dx, -dz), _ => (dz, -dx),
    };

    /// <summary>The same for a point between voxels (a light, a seat).</summary>
    public static (float x, float z) Turn(float dx, float dz, int rot) => (rot & 3) switch
    {
        0 => (dx, dz), 1 => (-dz, dx), 2 => (-dx, -dz), _ => (dz, -dx),
    };

    /// <summary>Where its fire burns, about its spot and over its floor, voxels; null for what has none.</summary>
    public static (float dx, float dy, float dz)? LightAt(DecorKind k) => k switch
    {
        DecorKind.Campfire => (.5f, 2.5f, .5f),
        DecorKind.Torch => (.5f, 18.6f, .5f),
        DecorKind.Brazier => (.5f, 5.6f, .5f),
        _ => null,
    };

    /// <summary>Whether people warm themselves at it.</summary>
    public static bool Warms(DecorKind k) => k is DecorKind.Campfire or DecorKind.Brazier;

    /// <summary>The places one sits on it: about its spot, the height of the seat over its floor, and the way one faces (unturned: +z).</summary>
    public static (float dx, float dz, float h)[] Seats(DecorKind k) => k switch
    {
        DecorKind.Bench => new[] { (-2.5f, .5f, 4f), (2f, .5f, 4f) },
        _ => Array.Empty<(float, float, float)>(),
    };

    /// <summary>
    /// The voxels of a thing set out on the ground at a spot, turned so many quarter turns, each in the world: null, with
    /// why, where it cannot stand (in water, on too steep a slope, where something is built).
    /// </summary>
    public static VoxelList Plan(VoxelWorld w, Site site, DecorKind k, int cx, int cz, int rot, out string why)
    {
        why = null;
        if (!w.InBounds(cx, 1, cz)) { why = "за краем"; return null; }
        int floor = w.SurfAt(cx, cz);
        var L = new VoxelList();
        foreach (var (dx, dy, dz, m) in Shape(k))
        {
            var (tx, tz) = Turn(dx, dz, rot);
            int x = cx + tx, z = cz + tz, y = floor + dy;
            if (!w.InBounds(x, y, z) || site != null && site.Forbidden(x, z)) { why = "за краем"; return null; }
            if (dy == 0)
            {
                int s = w.SurfAt(x, z);
                if (s > floor + 1 || s < floor - 1) { why = "слишком круто"; return null; }
            }
            byte now = w.Get(x, y, z);
            if (now == Mat.Water || now == Mat.Acid || now == Mat.Pitch) { why = "в воде"; return null; }
            if (!Brushes.Free(now)) { why = "мешает постройка"; return null; }
            L.Add(x, y, z, m);
        }
        return L;
    }
}
