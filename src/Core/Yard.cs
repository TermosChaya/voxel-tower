using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What the «Обустройство» tool sets out in the settlement.</summary>
public enum DecorKind : byte { Campfire, Bench, Torch, Brazier, Well, Woodpile, Cart, Barrels, Fence, Signpost, FlowerBed,
    LampPost, Pots, HayBales, Trough, Stall, Shrine, NoticeBoard, Coop, Barrow, Statue }

/// <summary>
/// The small things a settlement is furnished with between its houses, each a little figure of voxels the workers put
/// together from the stores like anything else: a campfire in a ring of stones with its logs and flames, a bench, a
/// torch on a pole, a brazier on a stone pedestal, a well with its roof and bucket, a woodpile, a cart, barrels and a
/// crate, a length of fence, a signpost, a flower bed; a lamp on a post, pots of flowers, bales of hay, a water trough,
/// a market stall under a striped awning, a wayside shrine with its candle, a notice board, a hen coop, a barrow, a
/// statue on a plinth. Laid out a quarter turn at a time about the spot clicked, on its floor; what makes light, smoke
/// or a seat of it is told by <see cref="LightAt"/> and <see cref="Seats"/>. The town sets some out by itself as it
/// grows (Sim.TownDecor.cs).
/// </summary>
public static class Yard
{
    public static readonly DecorKind[] All = (DecorKind[])Enum.GetValues(typeof(DecorKind));

    public static string Name(DecorKind k) => k switch
    {
        DecorKind.Campfire => "Костёр", DecorKind.Bench => "Скамья", DecorKind.Torch => "Факел на шесте", DecorKind.Brazier => "Жаровня",
        DecorKind.Well => "Колодец", DecorKind.Woodpile => "Поленница", DecorKind.Cart => "Телега", DecorKind.Barrels => "Бочки и ящик",
        DecorKind.Fence => "Изгородь", DecorKind.Signpost => "Указатель", DecorKind.LampPost => "Фонарь", DecorKind.Pots => "Цветы в горшках",
        DecorKind.HayBales => "Тюки сена", DecorKind.Trough => "Поилка", DecorKind.Stall => "Прилавок", DecorKind.Shrine => "Придорожный крест",
        DecorKind.NoticeBoard => "Доска объявлений", DecorKind.Coop => "Курятник", DecorKind.Barrow => "Тачка", DecorKind.Statue => "Статуя",
        _ => "Клумба",
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
        DecorKind.LampPost => "Фонарь на столбе. Светит ночью. Дома рядом платят больше подати.",
        DecorKind.Pots => "3 глиняных горшка с цветами. Дома рядом платят больше подати.",
        DecorKind.HayBales => "3 тюка сена.",
        DecorKind.Trough => "Каменная поилка.",
        DecorKind.Stall => "Прилавок под полосатым навесом, на нём товар.",
        DecorKind.Shrine => "Каменный столб с крестом под кровлей, свеча в нише. Светит ночью. Дома рядом платят больше подати.",
        DecorKind.NoticeBoard => "Доска под навесом, на ней листы.",
        DecorKind.Coop => "Курятник на ножках со сходнями.",
        DecorKind.Barrow => "Тачка с камнями.",
        DecorKind.Statue => "Статуя на постаменте. Дома рядом платят больше подати.",
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
            case DecorKind.LampPost:
                for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++) P(x, 0, z, Mat.StoneDark);
                for (int y = 1; y <= 17; y++) P(0, y, 0, Mat.Wood);
                // the lantern on top of the post: an iron floor, iron at its corners, its candle on an ember of a wick, an
                // iron cap and a finial (laid from the bottom up, as the workers lay everything)
                for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++) { P(x, 18, z, Mat.Iron); P(x, 21, z, Mat.Iron); }
                foreach (int x in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 }) for (int y = 19; y <= 20; y++) P(x, y, z, Mat.Iron);
                P(0, 19, 0, Mat.Ember); P(0, 20, 0, Mat.Flame);
                P(0, 22, 0, Mat.Iron);
                break;
            case DecorKind.Pots:
                foreach (var (cx, s0, h) in new[] { (-4, 3, 3), (0, 2, 2), (3, 3, 4) })
                {
                    for (int y = 0; y < h; y++)
                        for (int z = 0; z < s0; z++)
                            for (int x = cx; x < cx + s0; x++)
                            {
                                // the rim a little wider than the pot's foot
                                if (y == 0 && s0 == 3 && (x == cx || x == cx + 2) && (z == 0 || z == 2)) continue;
                                P(x, y, z - s0 / 2, Mat.Tile);
                            }
                    for (int z = 0; z < s0; z++)
                        for (int x = cx; x < cx + s0; x++)
                            P(x, h, z - s0 / 2, H(x, h, z, 6, .5) ? Mat.FlowerRed : cx == 0 ? Mat.FlowerWhite : Mat.Flower);
                }
                break;
            case DecorKind.HayBales:
                foreach (var (x0, y0, z0) in new[] { (-5, 0, -2), (0, 0, -2), (-3, 3, -2) })
                    for (int y = y0; y < y0 + 3; y++)
                        for (int z = z0; z < z0 + 4; z++)
                            for (int x = x0; x < x0 + 5; x++) P(x, y, z, Mat.Hay);
                break;
            case DecorKind.Trough:
                for (int x = -5; x <= 5; x++)
                    for (int z = -2; z <= 2; z++)
                    {
                        bool wall = x == -5 || x == 5 || z == -2 || z == 2;
                        P(x, 0, z, Mat.Stone);
                        if (wall) { P(x, 1, z, Mat.Stone); P(x, 2, z, H(x, 2, z, 7, .4) ? Mat.StoneDark : Mat.Stone); }
                        else P(x, 1, z, Mat.StoneDark);
                    }
                break;
            case DecorKind.Stall:
                foreach (int x in new[] { -6, -2, 2, 6 }) foreach (int z in new[] { -3, 3 }) for (int y = 0; y <= 12; y++) P(x, y, z, Mat.Wood);
                // the counter along the front, and the goods on it
                for (int x = -5; x <= 5; x++) { P(x, 4, 3, Mat.Table); P(x, 5, 3, Mat.Table); for (int y = 0; y < 4; y++) P(x, y, 3, Mat.Wood); }
                for (int x = -4; x <= -2; x++) { P(x, 6, 3, Mat.Sack); P(x, 7, 3, Mat.Sack); }
                for (int x = 0; x <= 1; x++) for (int y = 6; y <= 7; y++) P(x, y, 3, y == 7 ? Mat.CrateEdge : Mat.Crate);
                P(3, 6, 3, Mat.Cask); P(3, 7, 3, Mat.Hoop); P(4, 6, 3, Mat.Cask);
                // the awning: a striped roof of straw and fired clay falling to the front, its beams on the posts
                for (int x = -6; x <= 6; x++) { P(x, 13, -3, Mat.Wood); P(x, 13, 3, Mat.Wood); }
                for (int x = -6; x <= 6; x++)
                    for (int z = -4; z <= 4; z++)
                        if (z != -3 && z != 3) P(x, z < 0 ? 14 : 13, z, ((x + 6) / 2) % 2 == 0 ? Mat.Thatch : Mat.Tile);
                for (int x = -6; x <= 6; x++) { P(x, 14, -3, ((x + 6) / 2) % 2 == 0 ? Mat.Thatch : Mat.Tile); P(x, 14, 3, ((x + 6) / 2) % 2 == 0 ? Mat.Thatch : Mat.Tile); }
                break;
            case DecorKind.Shrine:
                for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++) for (int y = 0; y <= 10; y++)
                    // a niche facing the way, the candle in it
                    if (!(z == 1 && x == 0 && y is >= 6 and <= 8)) P(x, y, z, y == 0 ? Mat.StoneDark : Mat.StoneLight);
                P(0, 6, 1, Mat.Ember); P(0, 7, 1, Mat.Flame);
                // its little roof, and the cross over it
                for (int x = -2; x <= 2; x++) { P(x, 11, -2, Mat.Wood); P(x, 11, 2, Mat.Wood); for (int z = -1; z <= 1; z++) P(x, 12, z, Mat.Wood); }
                for (int x = -2; x <= 2; x++) P(x, 11, 0, Mat.Wood);
                for (int y = 13; y <= 16; y++) P(0, y, 0, Mat.Iron);
                P(-1, 15, 0, Mat.Iron); P(1, 15, 0, Mat.Iron);
                break;
            case DecorKind.NoticeBoard:
                foreach (int x in new[] { -5, 0, 5 }) for (int y = 0; y <= 13; y++) P(x, y, 0, Mat.Wood);
                for (int x = -4; x <= 4; x++) for (int y = 5; y <= 11; y++) P(x, y, 0, Mat.Table);
                // the sheets pinned to it
                foreach (var (x, y) in new[] { (-3, 9), (-2, 9), (-3, 8), (-2, 8), (1, 10), (2, 10), (1, 9), (2, 9), (1, 8), (-1, 6), (0, 6), (3, 7) })
                    P(x, y, 1, Mat.Canvas);
                for (int x = -6; x <= 6; x++) { P(x, 14, 0, Mat.Wood); P(x, 13, -1, Mat.Wood); P(x, 13, 1, Mat.Wood); }
                break;
            case DecorKind.Coop:
                foreach (int x in new[] { -3, 3 }) foreach (int z in new[] { -2, 2 }) for (int y = 0; y <= 2; y++) P(x, y, z, Mat.Wood);
                for (int x = -3; x <= 3; x++)
                    for (int z = -2; z <= 2; z++)
                        for (int y = 3; y <= 7; y++)
                        {
                            bool shell = x == -3 || x == 3 || z == -2 || z == 2 || y == 3;
                            // its little door in the front
                            if (!shell || z == 2 && x is >= -1 and <= 0 && y is >= 4 and <= 5) continue;
                            P(x, y, z, Mat.Wood);
                        }
                // its roof of shingles, the ridge a course over the eaves (laid on them: nothing of a roof hangs by a corner)
                for (int x = -4; x <= 4; x++) for (int z = -3; z <= 3; z++) { P(x, 8, z, Mat.Shingle); if (Math.Abs(z) <= 1) P(x, 9, z, Mat.Shingle); }
                // the ramp down from the door
                for (int st = 0; st < 4; st++) for (int y = 0; y <= 3 - st; y++) { P(-1, y, 3 + st, Mat.Bench); P(0, y, 3 + st, Mat.Bench); }
                break;
            case DecorKind.Barrow:
                // the wheel at the front, the tray behind it on its legs, the two handles out behind
                for (int y = 0; y <= 3; y++) for (int x = 3; x <= 6; x++) if ((x - 4.5) * (x - 4.5) + (y - 1.5) * (y - 1.5) < 4.2) P(x, y, 0, (x - 4.5) * (x - 4.5) + (y - 1.5) * (y - 1.5) > 1.6 ? Mat.Hoop : Mat.Cask);
                for (int x = -3; x <= 3; x++) for (int z = -2; z <= 2; z++) { P(x, 2, z, Mat.Table); if (Math.Abs(z) == 2 || x == -3 || x == 3) P(x, 3, z, Mat.Table); }
                // (the handles down to the ground behind, resting there as a barrow put down does)
                foreach (int z in new[] { -2, 2 }) { P(-3, 0, z, Mat.Wood); P(-3, 1, z, Mat.Wood); for (int x = -7; x <= -4; x++) P(x, 1, z, Mat.Wood); P(-8, 0, z, Mat.Wood); P(-8, 1, z, Mat.Wood); }
                for (int x = -2; x <= 1; x++) for (int z = -1; z <= 1; z++) if (H(x, 3, z, 9, .7)) P(x, 3, z, Mat.Hewn);
                break;
            case DecorKind.Statue:
                for (int y = 0; y <= 4; y++) for (int z = -2; z <= 2; z++) for (int x = -2; x <= 2; x++)
                    if (y < 4 || Math.Abs(x) < 2 && Math.Abs(z) < 2) P(x, y, z, y == 0 || y == 4 ? Mat.StoneDark : Mat.StoneLight);
                // a figure in a long robe, its arms at its sides, a staff in its hand
                for (int y = 5; y <= 12; y++) for (int z = -1; z <= 0; z++) for (int x = -1; x <= 1; x++) if (y <= 10 || x == 0) P(x, y, z, Mat.Stone);
                for (int y = 13; y <= 14; y++) for (int z = -1; z <= 0; z++) for (int x = -1; x <= 0; x++) P(x, y, z, Mat.Stone);
                for (int y = 5; y <= 15; y++) P(2, y, 0, Mat.StoneDark);
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
        DecorKind.LampPost => (.5f, 20.6f, .5f),
        DecorKind.Shrine => (.5f, 7.4f, 1.6f),
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
