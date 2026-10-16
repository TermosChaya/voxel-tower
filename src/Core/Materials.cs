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
    public const byte Air = 0, Bed = 1, Soil = 2, Grass = 3, Rock = 4, Stone = 5, Mortar = 6, Wood = 7, Rammed = 8,
        Keep = 9, KeepDark = 10, Roof = 11, Leaf = 12, Trunk = 13, Ember = 14, Path = 15, Moss = 16, Iron = 17, Tuft = 18, Plank = 19;
    public const int Count = 64;

    public static readonly MaterialDef[] Def = new MaterialDef[Count];
    public static readonly bool[] IsVeg = new bool[Count], IsPlayer = new bool[Count], IsEarthy = new bool[Count],
        IsNatural = new bool[Count], IsAnchor = new bool[Count];
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

        foreach (var m in new[] { Leaf, Trunk, Tuft }) IsVeg[m] = true;
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
