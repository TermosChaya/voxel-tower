using System;

namespace VoxelTower.Core;

public enum MatGroup : byte { Earth, Wood, Stone, Rock, Hard }

public readonly struct MaterialDef
{
    public readonly string Name;
    public readonly uint Srgb;
    public readonly float Variance;
    public readonly MatGroup Group;
    public readonly bool Natural, Player, Earthy;
    public readonly float Glow;

    public MaterialDef(string name, uint srgb, float variance, MatGroup group, bool natural, bool player, bool earthy, float glow)
    {
        Name = name; Srgb = srgb; Variance = variance; Group = group;
        Natural = natural; Player = player; Earthy = earthy; Glow = glow;
    }
}

/// <summary>World materials. Ids match the browser prototype so maps and blueprints line up.</summary>
public static class Mat
{
    /// <summary>Colours for the figures of pawns and raiders; never placed in the world.</summary>
    public const byte SkinHuman = 32, SkinGoblin = 33, SkinOrc = 34, SkinTroll = 35, ClothBlue = 36, ClothRed = 37, ClothDark = 38,
        Leather = 39, Steel = 40, Mail = 41, Handle = 42, Bone = 43, Hair = 44, Eye = 45, Linen = 46, Brass = 47, Rag = 48,
        FurDeer = 49, FurBoar = 50, FurHare = 51, Hoof = 52, Belly = 53;

    public const byte Air = 0, Bed = 1, Soil = 2, Grass = 3, Rock = 4, Stone = 5, Mortar = 6, Wood = 7, Rammed = 8,
        Keep = 9, KeepDark = 10, Roof = 11, Leaf = 12, Trunk = 13, Ember = 14, Path = 15, Moss = 16, Iron = 17, Tuft = 18, Plank = 19, Foliage = 20,
        Ladder = 21, SiegeLadder = 22, Deck = 23, Water = 24, Gate = 25, GateShut = 26, Ore = 27, Ruin = 28, GateOpen = 29,
        StoneDark = 30, StoneLight = 31;
    /// <summary>More of the world, past the figures' colours: grass of other shades, flowers, birch bark.</summary>
    public const byte GrassDry = 54, GrassLush = 55, Flower = 56, Bloom = 57, Birch = 58, KeepShade = 59;
    /// <summary>What dresses the buildings by itself: banners, torches in their sconces, barrels, crates and sacks in the yards.</summary>
    public const byte Banner = 60, BannerTrim = 61, Sconce = 62, Flame = 63, Cask = 64, Hoop = 65, Crate = 66, CrateEdge = 67, Sack = 68;
    /// <summary>Two more shades of laid stone: a warm one and a cool one.</summary>
    public const byte StoneWarm = 69, StoneCool = 70;
    /// <summary>The trees in more shades: needles and leaves where the sun is on them, leaves turning, a pine's rusty upper bark, a dead tree's grey wood.</summary>
    public const byte NeedleLight = 71, FoliageLight = 72, FoliageGold = 73, PineBark = 74, Snag = 75;
    /// <summary>The boards of a shield, in two shades: lighter than the timber of the walls.</summary>
    public const byte Board = 76, BoardDark = 77;
    /// <summary>What a fire leaves of timber, and pitch poured to be set alight: a liquid, thick and slow, that runs into the
    /// lowest place it can reach, and a ditch of it burns long and hot.</summary>
    public const byte Char = 78, Pitch = 79;
    /// <summary>Inside the bodies, and more colours for them: never placed in the world.</summary>
    public const byte Flesh = 80, Brain = 81, Heart = 82, Blood = 83, HairBrown = 84, HairGinger = 85, HairGrey = 86, SkinTan = 87,
        SkinPale = 88, SkinGoblinDark = 89, SkinGoblinPale = 90, SkinOrcGrey = 91, ClothGreen = 92, ClothBrown = 93, ClothOchre = 94, Straw = 95;
    /// <summary>What a fall leaves: broken masonry and broken timber lying in a heap. Neither stands for the building it
    /// was; mending clears it away and lays the building anew.</summary>
    public const byte Rubble = 96, Lumber = 97;
    /// <summary>The eyes of goblins, catching what light there is: two sparks out of the dark at night.</summary>
    public const byte EyeGlint = 98;
    /// <summary>
    /// Traps the brushes lay: sharpened stakes standing a hand apart, that tear and slow whoever goes among them; a trap
    /// floor of thin poles under turf, laid flush over a pit, that holds our people and gives way under the horde; stones
    /// heaped on the battlements, to be tipped onto whoever comes to the foot of the wall.
    /// </summary>
    public const byte Stakes = 99, TrapDeck = 100, DropStone = 101;
    /// <summary>The oak planks of a drawbridge: they turn with it, so they are the bridge's own business, like a gate's leaves.</summary>
    public const byte DrawDeck = 102;
    /// <summary>The ladder a siege tower drops against a wall: inside the tower's timbers, so it cannot be thrown down.</summary>
    public const byte TowerLadder = 103;
    /// <summary>The black stone of the valley's places of power, older than anyone's memory of them: no pick or axe bites on it.</summary>
    public const byte Menhir = 104;
    /// <summary>For the figures: the priests' robes, dark as old blood, and their tarnished trim; the pale fire in a dead man's eyes.</summary>
    public const byte Robe = 105, RobeTrim = 106, SoulFire = 107;
    /// <summary>
    /// Kegs of black powder, stacked as the brush lays them: fire sets them off — the first spark, a burning neighbour, a
    /// fire arrow, a torch — and they go up with everything about them, each keg setting off the next.
    /// </summary>
    public const byte Powder = 108;
    /// <summary>
    /// Logs laid on the battlements to be rolled down: when a raider comes to the foot of the wall under them and one of
    /// ours stands by them, he tips them over, and they come down on it and roll on down the slope before the wall.
    /// </summary>
    public const byte DropLog = 109;
    /// <summary>
    /// The furnishing of a dwelling, which the workers knock together of timber once the room is closed and has its door:
    /// the frame of a bunk, the straw and linen of its bedding, a table and a bench. A body walks through them as through
    /// a banner, the builders lay them as they lay anything, and they burn.
    /// </summary>
    public const byte Bunk = 110, Bedding = 111, Table = 112, Bench = 113;
    /// <summary>
    /// For the figures of our troops: the coats of horses (bay, black, dapple grey, chestnut), a mastiff's fawn, the
    /// quilted cloth of a gambeson, the fired clay of a bombardier's pots, the white of a knight's surcoat.
    /// </summary>
    public const byte HorseBay = 114, HorseBlack = 115, HorseGrey = 116, HorseChestnut = 117, Fawn = 118, Gambeson = 119, Clay = 120, ClothWhite = 121;
    /// <summary>Turf a fire has gone over, or a blast seared: black, until the grass creeps back over it.</summary>
    public const byte Scorch = 122;
    /// <summary>
    /// For the figures drawn finer than their half-voxels: a blade's honed edge and the dark of its fuller, rust, the dark
    /// leather wound round a grip, the whites of the eyes, a mouth, a tusk's yellowed ivory, warpaint in chalk and in red
    /// ochre, the dark ink of a tattoo, a troll's darker hide and the stony growths on it, raw hide, horn, an ash shaft,
    /// the red of a maw and of an old scar.
    /// </summary>
    public const byte SteelEdge = 123, SteelDark = 124, Rust = 125, LeatherDark = 126, EyeWhite = 127, Lips = 128, Tusk = 129, Warpaint = 130,
        WarpaintRed = 131, Ink = 132, SkinTrollDark = 133, Crag = 134, Hide = 135, Horn = 136, Shaft = 137, Maw = 138, Scar = 139;
    public const int Count = 140;

    public static readonly MaterialDef[] Def = new MaterialDef[Count];
    public static readonly bool[] IsVeg = new bool[Count], IsPlayer = new bool[Count], IsEarthy = new bool[Count],
        IsNatural = new bool[Count], IsAnchor = new bool[Count];
    /// <summary>The dressing of the buildings: whatever is built or laid where it stands simply takes its place.</summary>
    public static readonly bool[] IsDecor = new bool[Count];
    /// <summary>A dwelling's furnishing: walked through, but built on as on anything solid.</summary>
    public static readonly bool[] IsFurniture = new bool[Count];
    /// <summary>How it moves in the wind: 0 not at all, 1 at its tips (grass, flowers), 2 as a mass (leaves).</summary>
    public static readonly byte[] SwayKind = new byte[Count];
    /// <summary>Dressed stone: each stone drawn whole, without the grid of its voxels.</summary>
    public static readonly bool[] Smooth = new bool[Count];
    /// <summary>Bright metal on the figures — steel, mail, brass: it catches the light instead of drinking it.</summary>
    public static readonly bool[] Metal = new bool[Count];
    /// <summary>
    /// What the voxel shader draws on a face finer than its voxels: 0 nothing, 1 dressed stone, 2 metal, 3 turf (blades on
    /// its top, a fringe of them over the earth down its sides), 4 leaves (each voxel a few of them, lit and shaded), 5 bark
    /// (furrows up the trunk), 6 birch bark (dark dashes across the white), 7 needles (short strokes every way), 8 burnt
    /// turf (ash and charred stubble).
    /// </summary>
    public static readonly byte[] Skin = new byte[Count];
    /// <summary>A body passes through it: leaves, grass, ladders and water. Nothing is hacked out of the way for these.</summary>
    public static readonly bool[] Passable = new bool[Count];
    /// <summary>Ladders: bodies hang on them and climb.</summary>
    public static readonly bool[] IsLadder = new bool[Count];
    /// <summary>
    /// Fire: how readily it catches from a burning neighbour (a chance a second), how many seconds it burns once
    /// caught, what is left of it when it has burnt (timber may stand charred), and how hot it burns (pitch twice).
    /// </summary>
    public static readonly float[] Catch = new float[Count], Fuel = new float[Count], Heat = new float[Count], CharShare = new float[Count];
    /// <summary>Linear RGB, three floats per material.</summary>
    public static readonly float[] Lin = new float[Count * 3];
    public static readonly float[] Variance = new float[Count], Emission = new float[Count];

    const int Natural = 1, Player = 2, Earthy = 4, Glows = 8, Decor = 16;

    static Mat()
    {
        // the land a little brighter, what is built of it darker
        D(Bed, "Материк", 0x221f27, .10f, MatGroup.Hard, Natural);
        D(Soil, "Грунт", 0x584130, .22f, MatGroup.Earth, Natural | Earthy);
        D(Grass, "Дёрн", 0x4a5c33, .30f, MatGroup.Earth, Natural | Earthy);
        D(Rock, "Скала", 0x5a5862, .24f, MatGroup.Rock, Natural);
        D(Stone, "Камень", 0x605c5e, .04f, MatGroup.Stone, Player);
        D(Mortar, "Раствор", 0x2a262b, .05f, MatGroup.Stone, Player);
        D(Wood, "Брёвна", 0x4c3423, .22f, MatGroup.Wood, Player);
        D(Rammed, "Земля", 0x4b3829, .16f, MatGroup.Earth, Player | Earthy);
        // the citadel: rough masonry of big stones in two shades, darker mortar
        D(Keep, "Камень цитадели", 0x756e68, .10f, MatGroup.Hard, 0);
        D(KeepShade, "Камень цитадели", 0x5f5a57, .10f, MatGroup.Hard, 0);
        D(KeepDark, "Камень цитадели", 0x3a343b, .08f, MatGroup.Hard, 0);
        D(Roof, "Сланец", 0x3b3349, .16f, MatGroup.Hard, 0);
        D(Leaf, "Хвоя", 0x263828, .30f, MatGroup.Wood, Natural);
        D(Trunk, "Ствол", 0x45332a, .18f, MatGroup.Wood, Natural);
        D(Ember, "Жар", 0xff7a2a, .35f, MatGroup.Hard, Glows);
        D(Path, "Тропа", 0x6a5642, .20f, MatGroup.Earth, Natural | Earthy);
        D(Moss, "Мох", 0x475a34, .28f, MatGroup.Rock, Natural);
        D(Iron, "Железо", 0x2a2a30, .08f, MatGroup.Hard, 0);
        D(Tuft, "Трава", 0x566c37, .34f, MatGroup.Earth, Natural | Earthy);
        D(Plank, "Доски", 0x4a3220, .18f, MatGroup.Hard, 0);
        D(Foliage, "Листва", 0x415426, .32f, MatGroup.Wood, Natural);
        D(Ladder, "Лестница", 0x7a5634, .2f, MatGroup.Wood, Player);
        D(SiegeLadder, "Осадная лестница", 0x4a3726, .22f, MatGroup.Wood, 0);
        D(Deck, "Настил орды", 0x4f3d2c, .26f, MatGroup.Wood, 0);
        D(Water, "Вода", 0x1f3a44, .04f, MatGroup.Hard, Natural);
        // oak bound with iron: the horde hacks it no faster than masonry
        D(Gate, "Ворота", 0x4b3322, .26f, MatGroup.Stone, Player);
        D(GateShut, "Ворота на засове", 0x45301f, .26f, MatGroup.Stone, Player);
        // leaves swinging or standing open: in everyone's way, to be walked round
        D(GateOpen, "Ворота", 0x4b3322, .26f, MatGroup.Stone, Player);
        // gold-bearing rock in the veins of the mines, and the weathered masonry of old ruins
        D(Ore, "Руда", 0xa98436, .3f, MatGroup.Rock, Natural);
        D(Ruin, "Старая кладка", 0x5f5d57, .24f, MatGroup.Stone, Natural);
        // rough masonry: stones of three shades in dark mortar
        D(StoneDark, "Камень", 0x4a474b, .04f, MatGroup.Stone, Player);
        D(StoneLight, "Камень", 0x726d6c, .04f, MatGroup.Stone, Player);
        D(StoneWarm, "Камень", 0x6a6058, .04f, MatGroup.Stone, Player);
        D(StoneCool, "Камень", 0x56595f, .04f, MatGroup.Stone, Player);
        D(GrassDry, "Дёрн", 0x585d34, .30f, MatGroup.Earth, Natural | Earthy);
        D(GrassLush, "Дёрн", 0x3c5a30, .30f, MatGroup.Earth, Natural | Earthy);
        D(Flower, "Цветы", 0xb09a46, .30f, MatGroup.Earth, Natural | Earthy);
        D(Bloom, "Цветы", 0x7c5a84, .30f, MatGroup.Earth, Natural | Earthy);
        D(Birch, "Берёза", 0xcfc8b8, .22f, MatGroup.Wood, Natural);
        D(NeedleLight, "Хвоя", 0x37522f, .28f, MatGroup.Wood, Natural);
        D(FoliageLight, "Листва", 0x5a6c2d, .28f, MatGroup.Wood, Natural);
        D(FoliageGold, "Листва", 0x8a7a2e, .26f, MatGroup.Wood, Natural);
        D(PineBark, "Ствол", 0x7a4b30, .2f, MatGroup.Wood, Natural);
        D(Snag, "Сухостой", 0x6b635b, .18f, MatGroup.Wood, Natural);
        D(Banner, "Стяг", 0x2f4d7a, .1f, MatGroup.Wood, Decor);
        D(BannerTrim, "Стяг", 0xb8913e, .1f, MatGroup.Wood, Decor);
        D(Sconce, "Факел", 0x2a2a30, .08f, MatGroup.Wood, Decor);
        D(Flame, "Факел", 0xff8a3a, .35f, MatGroup.Wood, Decor | Glows);
        D(Cask, "Бочка", 0x5c3e27, .16f, MatGroup.Wood, Decor);
        D(Hoop, "Бочка", 0x323238, .1f, MatGroup.Wood, Decor);
        D(Crate, "Ящик", 0x6e5134, .14f, MatGroup.Wood, Decor);
        D(CrateEdge, "Ящик", 0x47311f, .12f, MatGroup.Wood, Decor);
        D(Sack, "Мешки", 0x8c7c5b, .14f, MatGroup.Wood, Decor);
        D(Char, "Головни", 0x1d1714, .34f, MatGroup.Earth, 0);
        D(Pitch, "Смола", 0x17120f, .08f, MatGroup.Earth, Player);
        D(Rubble, "Обломки", 0x5b5755, .34f, MatGroup.Stone, 0);
        D(Lumber, "Обломки дерева", 0x45311f, .3f, MatGroup.Wood, 0);
        D(EyeGlint, "глаз", 0xffc24a, .05f, MatGroup.Hard, Glows);
        D(Stakes, "Колья", 0x9c7b4c, .22f, MatGroup.Wood, Player);
        D(TrapDeck, "Ловчий настил", 0x56632f, .3f, MatGroup.Wood, Player);
        D(DropStone, "Камни на сброс", 0x8e8881, .32f, MatGroup.Stone, Player);
        D(DrawDeck, "Подъёмный мост", 0x6e4b2b, .16f, MatGroup.Wood, Player);
        D(TowerLadder, "Лестница башни", 0x5a3d24, .2f, MatGroup.Wood, 0);
        D(Menhir, "Чёрный камень", 0x1d1a22, .1f, MatGroup.Hard, Natural);
        D(Powder, "Бочки с порохом", 0x6a3322, .3f, MatGroup.Wood, Player);
        D(DropLog, "Брёвна на сброс", 0x5e4128, .2f, MatGroup.Wood, Player);
        D(Bunk, "Лежанка", 0x6e5034, .16f, MatGroup.Wood, Player); D(Bedding, "Постель", 0x9c8a5e, .18f, MatGroup.Wood, Player);
        D(Table, "Стол", 0x7d5c39, .14f, MatGroup.Wood, Player); D(Bench, "Скамья", 0x684a2f, .14f, MatGroup.Wood, Player);
        foreach (var m in new[] { Bunk, Bedding, Table, Bench }) IsFurniture[m] = Passable[m] = true;
        D(Robe, "ряса", 0x3e1518, .14f, MatGroup.Hard, 0); D(RobeTrim, "кайма", 0x7a6236, .12f, MatGroup.Hard, 0);
        D(SoulFire, "мертвенный огонь", 0x9ef2c4, .08f, MatGroup.Hard, Glows);

        D(SkinHuman, "кожа", 0xc29878, .08f, MatGroup.Hard, 0); D(SkinGoblin, "кожа", 0x6d8a39, .12f, MatGroup.Hard, 0);
        D(SkinOrc, "кожа", 0x56673f, .12f, MatGroup.Hard, 0); D(SkinTroll, "кожа", 0x66705f, .16f, MatGroup.Hard, 0);
        D(ClothBlue, "сукно", 0x2e4562, .14f, MatGroup.Hard, 0); D(ClothRed, "сукно", 0x6b2320, .14f, MatGroup.Hard, 0);
        D(ClothDark, "сукно", 0x302a2e, .14f, MatGroup.Hard, 0); D(Leather, "кожа", 0x4a3424, .14f, MatGroup.Hard, 0);
        D(Steel, "сталь", 0xa7abb1, .12f, MatGroup.Hard, 0); D(Mail, "кольчуга", 0x6d7075, .3f, MatGroup.Hard, 0);
        D(Handle, "древко", 0x4b3423, .14f, MatGroup.Hard, 0); D(Bone, "кость", 0xd9cfb6, .06f, MatGroup.Hard, 0);
        D(Hair, "волосы", 0x1d1916, .1f, MatGroup.Hard, 0); D(Eye, "глаз", 0x1a1416, .04f, MatGroup.Hard, 0);
        D(Linen, "холст", 0x8a7a5a, .12f, MatGroup.Hard, 0); D(Brass, "латунь", 0xb08a3e, .1f, MatGroup.Hard, 0);
        D(Rag, "тряпьё", 0x4a3b2c, .2f, MatGroup.Hard, 0);
        D(FurDeer, "шерсть", 0x7a5232, .14f, MatGroup.Hard, 0); D(FurBoar, "щетина", 0x3b2e27, .2f, MatGroup.Hard, 0);
        D(FurHare, "мех", 0x86745f, .14f, MatGroup.Hard, 0); D(Hoof, "копыто", 0x221c1a, .06f, MatGroup.Hard, 0);
        D(Belly, "мех", 0xcdbb9c, .1f, MatGroup.Hard, 0);
        D(Flesh, "плоть", 0x7a1d1a, .18f, MatGroup.Hard, 0); D(Brain, "мозг", 0xa8676c, .14f, MatGroup.Hard, 0);
        D(Heart, "сердце", 0x5c0d10, .12f, MatGroup.Hard, 0); D(Blood, "кровь", 0x4c0909, .12f, MatGroup.Hard, 0);
        D(HairBrown, "волосы", 0x4a2f1e, .12f, MatGroup.Hard, 0); D(HairGinger, "волосы", 0x8e4a20, .12f, MatGroup.Hard, 0);
        D(HairGrey, "волосы", 0x8d8984, .1f, MatGroup.Hard, 0); D(SkinTan, "кожа", 0xa47758, .08f, MatGroup.Hard, 0);
        D(SkinPale, "кожа", 0xd6b298, .08f, MatGroup.Hard, 0); D(SkinGoblinDark, "кожа", 0x52702c, .12f, MatGroup.Hard, 0);
        D(SkinGoblinPale, "кожа", 0x8c9c54, .12f, MatGroup.Hard, 0); D(SkinOrcGrey, "кожа", 0x5c6457, .12f, MatGroup.Hard, 0);
        D(ClothGreen, "сукно", 0x344a2e, .14f, MatGroup.Hard, 0); D(ClothBrown, "сукно", 0x5c4430, .14f, MatGroup.Hard, 0);
        D(ClothOchre, "сукно", 0x8c6a2a, .14f, MatGroup.Hard, 0); D(Straw, "солома", 0xab9352, .2f, MatGroup.Hard, 0);
        D(Board, "доски", 0x80603d, .16f, MatGroup.Hard, 0); D(BoardDark, "доски", 0x6a4c30, .16f, MatGroup.Hard, 0);
        D(HorseBay, "шерсть", 0x5e3520, .12f, MatGroup.Hard, 0); D(HorseBlack, "шерсть", 0x221d1c, .1f, MatGroup.Hard, 0);
        D(HorseGrey, "шерсть", 0x9b968e, .2f, MatGroup.Hard, 0); D(HorseChestnut, "шерсть", 0x7d4524, .12f, MatGroup.Hard, 0);
        D(Fawn, "шерсть", 0x9a7a52, .14f, MatGroup.Hard, 0); D(Gambeson, "стёганка", 0x9a8b68, .16f, MatGroup.Hard, 0);
        D(Clay, "глина", 0x8e5634, .14f, MatGroup.Hard, 0); D(ClothWhite, "сукно", 0xb9b2a3, .1f, MatGroup.Hard, 0);
        D(Scorch, "Гарь", 0x2e2822, .26f, MatGroup.Earth, Natural | Earthy);
        D(SteelEdge, "лезвие", 0xdfe3e8, .04f, MatGroup.Hard, 0); D(SteelDark, "дол", 0x60656d, .08f, MatGroup.Hard, 0);
        D(Rust, "ржавчина", 0x553423, .22f, MatGroup.Hard, 0); D(LeatherDark, "обмотка", 0x2b1d15, .12f, MatGroup.Hard, 0);
        D(EyeWhite, "белок", 0xd9d1c2, .03f, MatGroup.Hard, 0); D(Lips, "губы", 0x7c3f37, .08f, MatGroup.Hard, 0);
        D(Tusk, "клык", 0xcfbb85, .07f, MatGroup.Hard, 0); D(Warpaint, "краска", 0xdcd5bf, .06f, MatGroup.Hard, 0);
        D(WarpaintRed, "охра", 0x9b3b23, .08f, MatGroup.Hard, 0); D(Ink, "наколка", 0x1f2c2b, .06f, MatGroup.Hard, 0);
        D(SkinTrollDark, "кожа", 0x495245, .14f, MatGroup.Hard, 0); D(Crag, "нарост", 0x76726a, .22f, MatGroup.Hard, 0);
        D(Hide, "шкура", 0x6b4d33, .2f, MatGroup.Hard, 0); D(Horn, "рог", 0x4e4133, .1f, MatGroup.Hard, 0);
        D(Shaft, "древко", 0x7d5c3b, .12f, MatGroup.Hard, 0); D(Maw, "пасть", 0x3c1213, .08f, MatGroup.Hard, 0);
        D(Scar, "шрам", 0x8f5f55, .08f, MatGroup.Hard, 0);

        foreach (var m in new[] { Leaf, Trunk, Tuft, Foliage, Flower, Bloom, Birch, NeedleLight, FoliageLight, FoliageGold, PineBark, Snag }) IsVeg[m] = true;
        foreach (var m in new[] { Leaf, Tuft, Foliage, NeedleLight, FoliageLight, FoliageGold, Ladder, SiegeLadder, Water, Flower, Bloom, Banner, BannerTrim, Sconce, Flame, Stakes, Pitch, TowerLadder }) Passable[m] = true;
        IsLadder[Ladder] = IsLadder[SiegeLadder] = IsLadder[TowerLadder] = true;
        foreach (var m in new[] { Tuft, Flower, Bloom }) SwayKind[m] = 1;
        foreach (var m in new[] { Leaf, Foliage, NeedleLight, FoliageLight, FoliageGold }) SwayKind[m] = 2;
        foreach (var m in new[] { Stone, StoneDark, StoneLight, StoneWarm, StoneCool, Mortar, Keep, KeepShade, KeepDark }) Smooth[m] = true;
        foreach (var m in new[] { Steel, Mail, Brass, Pitch, SteelEdge, SteelDark }) Metal[m] = true;
        for (int m = 0; m < Count; m++)
            Skin[m] = Smooth[m] ? (byte)1 : Metal[m] ? (byte)2 : IsGrass((byte)m) ? (byte)3 : m is Leaf or NeedleLight ? (byte)7
                : IsLeafy((byte)m) ? (byte)4 : m == Birch ? (byte)6 : IsTrunk((byte)m) ? (byte)5 : m == Scorch ? (byte)8 : (byte)0;
        // what holds the world up: loose pieces resting on these never fall
        foreach (var m in new[] { Bed, Soil, Grass, GrassDry, GrassLush, Scorch, Rock, Path, Moss, Tuft, Keep, KeepShade, KeepDark, Roof, Ember, Iron, Plank, Menhir }) IsAnchor[m] = true;

        // what burns: needles and leaves flare up and are gone in seconds, grass and flowers in a moment; timber
        // catches slowly and burns long, and some of it stands charred after; a living trunk and oak bound with iron
        // slower still, a dead tree readily; pitch catches from anything, runs with fire and burns long, twice as hot
        void F(float caught, float fuel, float charred, params byte[] ms) { foreach (var m in ms) { Catch[m] = caught; Fuel[m] = fuel; CharShare[m] = charred; Heat[m] = 1; } }
        F(.5f, 4, 0, Leaf, Foliage, NeedleLight, FoliageLight, FoliageGold);
        F(.35f, 1.5f, 0, Tuft, Flower, Bloom);
        F(.03f, 40, .6f, Trunk, Birch, PineBark);
        F(.1f, 30, .6f, Snag);
        F(.04f, 35, .35f, Wood, DropLog);
        F(.03f, 30, .35f, Plank);
        F(.12f, 15, 0, Ladder, SiegeLadder);
        F(.1f, 20, .2f, Deck);
        F(.03f, 45, .2f, Gate, GateShut, GateOpen);
        F(.3f, 6, 0, Banner, BannerTrim, Sack);
        F(.08f, 20, .3f, Cask, Crate, CrateEdge);
        F(.06f, 25, .3f, Lumber);
        F(.06f, 20, .3f, Bunk, Table, Bench);
        F(.3f, 6, 0, Bedding);
        F(.05f, 25, .35f, Stakes);
        F(.06f, 18, .3f, TrapDeck);
        F(.03f, 40, .35f, DrawDeck);
        F(3, 50, 0, Pitch);
        Heat[Pitch] = 2;
        // powder takes fire from anything at once; what it does then is not burning
        F(4, 2, 0, Powder);
    }

    /// <summary>It can catch fire.</summary>
    public static bool Burns(byte m) => Catch[m] > 0;

    static void D(byte id, string name, uint srgb, float variance, MatGroup group, int flags)
    {
        float glow = (flags & Glows) != 0 ? 2.6f : 0f;
        Def[id] = new MaterialDef(name, srgb, variance, group, (flags & Natural) != 0, (flags & Player) != 0, (flags & Earthy) != 0, glow);
        IsPlayer[id] = (flags & Player) != 0;
        IsEarthy[id] = (flags & Earthy) != 0;
        IsNatural[id] = (flags & Natural) != 0;
        IsDecor[id] = (flags & Decor) != 0;
        Lin[id * 3] = ToLinear((srgb >> 16) & 255);
        Lin[id * 3 + 1] = ToLinear((srgb >> 8) & 255);
        Lin[id * 3 + 2] = ToLinear(srgb & 255);
        Variance[id] = variance;
        Emission[id] = glow;
    }

    static float ToLinear(uint c8)
    {
        float c = c8 / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    public static string NameOf(byte m) => m == Air ? "воздух" : Def[m].Name ?? "?";

    /// <summary>The trunk of a tree, birch or any other.</summary>
    public static bool IsTrunk(byte m) => m is Trunk or Birch or PineBark or Snag;
    /// <summary>Needles and leaves, in any of their shades.</summary>
    public static bool IsLeafy(byte m) => m is Leaf or Foliage or NeedleLight or FoliageLight or FoliageGold;

    /// <summary>A tuft of grass or a flower: the game draws it in blades and petals, not as a cube.</summary>
    public static bool IsSprig(byte m) => m is Tuft or Flower or Bloom;

    /// <summary>Turf of any shade.</summary>
    public static bool IsGrass(byte m) => m is Grass or GrassDry or GrassLush;

    /// <summary>Laid stone of any shade, or its mortar.</summary>
    public static bool IsMasonry(byte m) => m is Stone or Mortar or StoneDark or StoneLight or StoneWarm or StoneCool;

    /// <summary>A voxel of a gate's leaf: shut (ours walk through it), barred, or swinging and open.</summary>
    public static bool IsLeaf(byte m) => m is Gate or GateShut or GateOpen;

    /// <summary>What turns on hinges, a gate's leaves or a drawbridge's planks: it holds itself up, and is not remembered as building.</summary>
    public static bool Swings(byte m) => IsLeaf(m) || m == DrawDeck;
}
