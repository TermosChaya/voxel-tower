using System;

namespace VoxelTower.Core;

/// <summary>
/// What the ground under the feet of one standing in a cell is to walk on (NavGrid.Footing): open air (a ladder, a deck
/// being laid), turf, a trodden way, a laid way of earth, gravel, boards or stone, a built floor (a wall's walk, a house's
/// floor, a bridge's deck), moss, ground grown over thick (tall grass, ferns, reeds, bushes), rubble and scree, sand, snow,
/// a bog's peat, shallow water over the feet.
/// </summary>
public enum Footing : byte { Open, Turf, Trodden, Dirt, Gravel, Boards, Paved, Floor, Moss, Rough, Scree, Sand, Snow, Bog, Water }

/// <summary>How fast one kind of walker goes over each footing (1 on turf), and what a step there costs in its plans.</summary>
public sealed class Paces
{
    public readonly float[] Of = new float[Going.Footings];
    /// <summary>What a step onto each footing counts for in a walker's plans, against one on turf: the time it takes, but for water, which ours keep out of.</summary>
    public readonly float[] Cost = new float[Going.Footings];

    public Paces(float trodden, float dirt, float gravel, float boards, float paved, float floor, float moss, float rough, float scree, float sand, float snow, float bog, float water, float wet = 0)
    {
        float[] p = { 1, 1, trodden, dirt, gravel, boards, paved, floor, moss, rough, scree, sand, snow, bog, water };
        for (int f = 0; f < Going.Footings; f++) { Of[f] = p[f]; Cost[f] = 1 / p[f]; }
        // a floor of ours (a wall's top, a deck, a house's floor) is walked quickly but sought no more than turf: it is
        // where the work is, not a way
        Cost[(int)Footing.Floor] = Math.Max(1, Cost[(int)Footing.Floor]);
        if (wet > 0) Cost[(int)Footing.Water] = wet;
    }
}

/// <summary>
/// The going (Going.cs): how much faster or slower each kind of ours goes over each kind of ground, so that a laid way
/// is plainly worth it — a dirt way a quarter faster than turf, cobbles a third and more; tall grass, rubble and sand a
/// quarter slower, snow and a bog a third and more, shallow water more than half — and the planned ways count each step
/// by the same (a man keeps to the road round a field of tall grass, crosses by a bridge rather than wade). A hound
/// keeps the pace it keeps everywhere, the road no quicker to it; a horse is quick on a road and slow in a bog or on
/// scree; a wheeled engine most of all. The horde and the beasts of the wild go as they always went (Mover.Feet null):
/// the raids and the battles are balanced on that.
/// </summary>
public static class Going
{
    public const int Footings = 15;

    /// <summary>The footings' names, for the hover over the ground and the path tool.</summary>
    public static readonly string[] Names =
        { "воздух", "трава", "тропинка", "тропа", "гравий", "мостки", "мостовая", "настил", "мох", "заросли", "осыпь", "песок", "снег", "топь", "вода" };

    /// <summary>Ours on foot: their ways quick, the rough slow, and water kept out of where a dry way goes round at all fairly.</summary>
    public static readonly Paces Folk = new(1.15f, 1.25f, 1.3f, 1.3f, 1.35f, 1.2f, .85f, .75f, .75f, .75f, .65f, .55f, .45f, wet: 8);
    /// <summary>A hound of ours: at home in the grass and the woods, no quicker on a road.</summary>
    public static readonly Paces Beast = new(1, 1, 1, 1, 1, 1, 1, .95f, .85f, .85f, .8f, .7f, .5f);
    /// <summary>A horse and its rider: quick on a road, slow in the rough, slower in a bog and on scree.</summary>
    public static readonly Paces Hooves = new(1.15f, 1.3f, 1.35f, 1.25f, 1.4f, 1.15f, .8f, .7f, .55f, .65f, .55f, .45f, .4f, wet: 6);
    /// <summary>A wheeled engine of ours: a road is what it goes on; the rough, sand and scree drag at it, a bog most.</summary>
    public static readonly Paces Wheels = new(1.2f, 1.4f, 1.45f, 1.4f, 1.5f, 1.3f, .9f, .8f, .7f, .75f, .7f, .55f, .4f);
    /// <summary>A giant or a tame troll of ours: its long legs over the rough, the road little quicker.</summary>
    public static readonly Paces Big = new(1.05f, 1.1f, 1.1f, 1.1f, 1.1f, 1.05f, 1f, .95f, .9f, .85f, .85f, .8f, .6f);

    /// <summary>The pace of the footing named by its byte.</summary>
    public static float Of(Paces p, byte f) => p.Of[f];

    /// <summary>How thick plants must stand about the feet for the ground to be rough going: plant voxels of 48 looked at (16 columns, knee-high).</summary>
    public const int RoughVeg = 16;

    /// <summary>Whether plants growing thick on a footing make it rough going: the ground's, not a laid way's or a floor's.</summary>
    public static bool Overgrows(Footing f) => f is Footing.Turf or Footing.Moss or Footing.Sand or Footing.Trodden or Footing.Bog;

    /// <summary>A footing grown over thick: rough going, but a bog's reeds and tussocks are its own.</summary>
    public static Footing Overgrown(Footing f) => f == Footing.Bog ? Footing.Bog : Footing.Rough;

    /// <summary>What a material is to walk on.</summary>
    public static Footing Ground(byte m) => m switch
    {
        Mat.Air or Mat.Water => Footing.Open,
        // (the valley's own old roads are trodden ways: what is laid in earth is quicker)
        Mat.Trodden or Mat.Path => Footing.Trodden,
        Mat.DirtPath => Footing.Dirt,
        Mat.Gravel => Footing.Gravel,
        Mat.Boardwalk => Footing.Boards,
        Mat.Cobble or Mat.Flag => Footing.Paved,
        Mat.Moss => Footing.Moss,
        Mat.Peat => Footing.Bog,
        Mat.Rubble or Mat.Lumber or Mat.Ruin or Mat.OreHeap or Mat.BonePile => Footing.Scree,
        Mat.Sand => Footing.Sand,
        Mat.Snow => Footing.Snow,
        _ when Mat.IsGrass(m) || m is Mat.Soil or Mat.Rock or Mat.Basalt or Mat.Clay or Mat.Scorch or Mat.Ash or Mat.Bed or Mat.Ore || Mat.IsIronOre(m) => Footing.Turf,
        _ when Mat.IsVeg[m] => Footing.Rough,
        _ when Mat.IsPlayer[m] => Footing.Floor,
        _ => Footing.Turf,
    };
}

public static partial class Kinds
{
    /// <summary>The going each kind keeps over the ground (Going.cs): ours on foot keep the folk's, which every mover has to begin with.</summary>
    static Kinds()
    {
        // ours on foot: the folk's going, and they keep to the doors, gates and stairs where there are any (Pathing.cs)
        foreach (var t in new[] { Worker, Archer, Swordsman, Captain, Architect, Priest, Spearman, Shieldbearer, Halberdier, Knight, Slinger, Crossbowman, Bombardier, OurAlchemist, Crewman })
        {
            t.Move.Feet = Going.Folk;
            t.Move.Careful = true;
        }
        foreach (var t in new[] { Rider, HorseArcher, Lancer }) t.Move.Feet = Going.Hooves;
        foreach (var t in new[] { Mangonel, Ballista, Trebuchet }) t.Move.Feet = Going.Wheels;
        foreach (var t in new[] { TameTroll, Giant }) t.Move.Feet = Going.Big;
        Hound.Move.Feet = Going.Beast;
    }
}
