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
    /// <summary>Inside the bodies, and more colours for them: never placed in the world.</summary>
    public const byte Flesh = 80, Brain = 81, Heart = 82, Blood = 83, HairBrown = 84, HairGinger = 85, HairGrey = 86, SkinTan = 87,
        SkinPale = 88, SkinGoblinDark = 89, SkinGoblinPale = 90, SkinOrcGrey = 91, ClothGreen = 92, ClothBrown = 93, ClothOchre = 94, Straw = 95;
    public const int Count = 96;

    public static readonly MaterialDef[] Def = new MaterialDef[Count];
    public static readonly bool[] IsVeg = new bool[Count], IsPlayer = new bool[Count], IsEarthy = new bool[Count],
        IsNatural = new bool[Count], IsAnchor = new bool[Count];
    /// <summary>The dressing of the buildings: whatever is built or laid where it stands simply takes its place.</summary>
    public static readonly bool[] IsDecor = new bool[Count];
    /// <summary>A body passes through it: leaves, grass, ladders and water. Nothing is hacked out of the way for these.</summary>
    public static readonly bool[] Passable = new bool[Count];
    /// <summary>Ladders: bodies hang on them and climb.</summary>
    public static readonly bool[] IsLadder = new bool[Count];
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
        D(Stone, "Камень", 0x5f5b5e, .14f, MatGroup.Stone, Player);
        D(Mortar, "Раствор", 0x2c282d, .08f, MatGroup.Stone, Player);
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
        D(StoneDark, "Камень", 0x4b484c, .14f, MatGroup.Stone, Player);
        D(StoneLight, "Камень", 0x6e6a6b, .14f, MatGroup.Stone, Player);
        D(GrassDry, "Дёрн", 0x585d34, .30f, MatGroup.Earth, Natural | Earthy);
        D(GrassLush, "Дёрн", 0x3c5a30, .30f, MatGroup.Earth, Natural | Earthy);
        D(Flower, "Цветы", 0xb09a46, .30f, MatGroup.Earth, Natural | Earthy);
        D(Bloom, "Цветы", 0x7c5a84, .30f, MatGroup.Earth, Natural | Earthy);
        D(Birch, "Берёза", 0xcfc8b8, .22f, MatGroup.Wood, Natural);
        D(Banner, "Стяг", 0x2f4d7a, .1f, MatGroup.Wood, Decor);
        D(BannerTrim, "Стяг", 0xb8913e, .1f, MatGroup.Wood, Decor);
        D(Sconce, "Факел", 0x2a2a30, .08f, MatGroup.Wood, Decor);
        D(Flame, "Факел", 0xff8a3a, .35f, MatGroup.Wood, Decor | Glows);
        D(Cask, "Бочка", 0x5c3e27, .16f, MatGroup.Wood, Decor);
        D(Hoop, "Бочка", 0x323238, .1f, MatGroup.Wood, Decor);
        D(Crate, "Ящик", 0x6e5134, .14f, MatGroup.Wood, Decor);
        D(CrateEdge, "Ящик", 0x47311f, .12f, MatGroup.Wood, Decor);
        D(Sack, "Мешки", 0x8c7c5b, .14f, MatGroup.Wood, Decor);

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

        foreach (var m in new[] { Leaf, Trunk, Tuft, Foliage, Flower, Bloom, Birch }) IsVeg[m] = true;
        foreach (var m in new[] { Leaf, Tuft, Foliage, Ladder, SiegeLadder, Water, Flower, Bloom, Banner, BannerTrim, Sconce, Flame }) Passable[m] = true;
        IsLadder[Ladder] = IsLadder[SiegeLadder] = true;
        // what holds the world up: loose pieces resting on these never fall
        foreach (var m in new[] { Bed, Soil, Grass, GrassDry, GrassLush, Rock, Path, Moss, Tuft, Keep, KeepShade, KeepDark, Roof, Ember, Iron, Plank }) IsAnchor[m] = true;
    }

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
    public static bool IsTrunk(byte m) => m is Trunk or Birch;

    /// <summary>Turf of any shade.</summary>
    public static bool IsGrass(byte m) => m is Grass or GrassDry or GrassLush;

    /// <summary>Laid stone of any shade, or its mortar.</summary>
    public static bool IsMasonry(byte m) => m is Stone or Mortar or StoneDark or StoneLight;

    /// <summary>A voxel of a gate's leaf: shut (ours walk through it), barred, or swinging and open.</summary>
    public static bool IsLeaf(byte m) => m is Gate or GateShut or GateOpen;
}
