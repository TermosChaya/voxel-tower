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
        Ladder = 21, SiegeLadder = 22, Deck = 23, Water = 24;
    public const int Count = 64;

    public static readonly MaterialDef[] Def = new MaterialDef[Count];
    public static readonly bool[] IsVeg = new bool[Count], IsPlayer = new bool[Count], IsEarthy = new bool[Count],
        IsNatural = new bool[Count], IsAnchor = new bool[Count];
    /// <summary>A body passes through it: leaves, grass, ladders and water. Nothing is hacked out of the way for these.</summary>
    public static readonly bool[] Passable = new bool[Count];
    /// <summary>Ladders: bodies hang on them and climb.</summary>
    public static readonly bool[] IsLadder = new bool[Count];
    /// <summary>Linear RGB, three floats per material.</summary>
    public static readonly float[] Lin = new float[Count * 3];
    public static readonly float[] Variance = new float[Count], Emission = new float[Count];

    const int Natural = 1, Player = 2, Earthy = 4, Glows = 8;

    static Mat()
    {
        D(Bed, "Материк", 0x221f27, .10f, MatGroup.Hard, Natural);
        D(Soil, "Грунт", 0x4b3727, .22f, MatGroup.Earth, Natural | Earthy);
        D(Grass, "Дёрн", 0x3a482a, .30f, MatGroup.Earth, Natural | Earthy);
        D(Rock, "Скала", 0x4c4a53, .24f, MatGroup.Rock, Natural);
        D(Stone, "Камень", 0x726f73, .15f, MatGroup.Stone, Player);
        D(Mortar, "Раствор", 0x3d383f, .08f, MatGroup.Stone, Player);
        D(Wood, "Брёвна", 0x5e412b, .22f, MatGroup.Wood, Player);
        D(Rammed, "Земля", 0x5a4332, .16f, MatGroup.Earth, Player | Earthy);
        D(Keep, "Камень цитадели", 0x8e867f, .10f, MatGroup.Hard, 0);
        D(KeepDark, "Камень цитадели", 0x48414b, .08f, MatGroup.Hard, 0);
        D(Roof, "Сланец", 0x3b3349, .16f, MatGroup.Hard, 0);
        D(Leaf, "Хвоя", 0x1e2c21, .30f, MatGroup.Wood, Natural);
        D(Trunk, "Ствол", 0x3b2a21, .18f, MatGroup.Wood, Natural);
        D(Ember, "Жар", 0xff7a2a, .35f, MatGroup.Hard, Glows);
        D(Path, "Тропа", 0x5b4939, .20f, MatGroup.Earth, Natural | Earthy);
        D(Moss, "Мох", 0x394a2b, .28f, MatGroup.Rock, Natural);
        D(Iron, "Железо", 0x2a2a30, .08f, MatGroup.Hard, 0);
        D(Tuft, "Трава", 0x46582e, .34f, MatGroup.Earth, Natural | Earthy);
        D(Plank, "Доски", 0x5a3d26, .18f, MatGroup.Hard, 0);
        D(Foliage, "Листва", 0x34431f, .32f, MatGroup.Wood, Natural);
        D(Ladder, "Лестница", 0x7a5634, .2f, MatGroup.Wood, Player);
        D(SiegeLadder, "Осадная лестница", 0x4a3726, .22f, MatGroup.Wood, 0);
        D(Deck, "Настил орды", 0x4f3d2c, .26f, MatGroup.Wood, 0);
        D(Water, "Вода", 0x1f3a44, .04f, MatGroup.Hard, Natural);

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

        foreach (var m in new[] { Leaf, Trunk, Tuft, Foliage }) IsVeg[m] = true;
        foreach (var m in new[] { Leaf, Tuft, Foliage, Ladder, SiegeLadder, Water }) Passable[m] = true;
        IsLadder[Ladder] = IsLadder[SiegeLadder] = true;
        // what holds the world up: loose pieces resting on these never fall
        foreach (var m in new[] { Bed, Soil, Grass, Rock, Path, Moss, Tuft, Keep, KeepDark, Roof, Ember, Iron, Plank }) IsAnchor[m] = true;
    }

    static void D(byte id, string name, uint srgb, float variance, MatGroup group, int flags)
    {
        float glow = (flags & Glows) != 0 ? 2.6f : 0f;
        Def[id] = new MaterialDef(name, srgb, variance, group, (flags & Natural) != 0, (flags & Player) != 0, (flags & Earthy) != 0, glow);
        IsPlayer[id] = (flags & Player) != 0;
        IsEarthy[id] = (flags & Earthy) != 0;
        IsNatural[id] = (flags & Natural) != 0;
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
}
