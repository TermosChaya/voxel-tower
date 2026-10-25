using System;

namespace VoxelTower.Core;

/// <summary>
/// What kind of valley a cell of the map is. Every kind has the plateau and the citadel and the river; they differ in their
/// ground — more forest, more rock, more water — and in the places worth taking, so that taking a new valley is worth it:
/// the mines lie in the valleys of ore, the barrow and the other places of power in the holy ones. Until there is a map
/// of valleys, the first valley keeps every place it has always had (<see cref="MapParams.Classic"/>).
/// </summary>
public enum ValleyKind : byte { First, Ore, Sacred, Forest, Marsh, Mountain }

/// <summary>The named places a valley may have.</summary>
[Flags]
public enum Places
{
    None = 0,
    /// <summary>The copper mine, the silver vein and the gold workings.</summary>
    Mines = 1,
    /// <summary>The abandoned miners' houses by the copper mine.</summary>
    MinersHouses = 2,
    /// <summary>The old outpost by the gold workings.</summary>
    Outpost = 4,
    /// <summary>The ruined watchtower by the ford and the fallen one under the cliff.</summary>
    Watchtowers = 8,
    /// <summary>The old bridge on the road and the fallen one.</summary>
    Bridges = 16,
    /// <summary>The barrow, the black stone and the dead spring: the places of power.</summary>
    Power = 32,
    /// <summary>Only the silver vein, of the mines.</summary>
    Silver = 64,
    All = Mines | MinersHouses | Outpost | Watchtowers | Bridges | Power,
}

/// <summary>The kinds of valley: their names, their ground, and their places.</summary>
public static class Valleys
{
    /// <summary>How a kind's ground differs from the first valley's (1 the same).</summary>
    public readonly struct Ground
    {
        /// <summary>Trees: how thick the woods stand.</summary>
        public readonly double Trees;
        /// <summary>The rocky knolls: the noise over which one rises (lower, more of them), and how high.</summary>
        public readonly double Knoll, KnollHeight;
        /// <summary>The long ridges of the lowland, how high.</summary>
        public readonly double Ridges;
        /// <summary>The river, how wide; the meadows, how wet (added to the moisture).</summary>
        public readonly double River, Wet;
        /// <summary>The soil over the rock, how deep.</summary>
        public readonly double Soil;
        /// <summary>On a hexagon made by itself: how high the citadel's plateau stands over the lowland, how much the lowland
        /// swells and sinks, and how many lakes lie in its hollows.</summary>
        public readonly double Lift, Relief;
        public readonly int Lakes;

        public Ground(double trees, double knoll, double knollHeight, double ridges, double river, double wet, double soil,
            double lift = 1, double relief = 1, int lakes = 1)
        {
            Trees = trees; Knoll = knoll; KnollHeight = knollHeight; Ridges = ridges; River = river; Wet = wet; Soil = soil;
            Lift = lift; Relief = relief; Lakes = lakes;
        }
    }

    public static string Key(ValleyKind k) => k switch
    {
        ValleyKind.Ore => "ore", ValleyKind.Sacred => "sacred", ValleyKind.Forest => "forest",
        ValleyKind.Marsh => "marsh", ValleyKind.Mountain => "mountain", _ => "first",
    };

    public static string Name(ValleyKind k) => k switch
    {
        ValleyKind.Ore => "рудная долина", ValleyKind.Sacred => "священная долина", ValleyKind.Forest => "лесная долина",
        ValleyKind.Marsh => "речная долина", ValleyKind.Mountain => "горная долина", _ => "первая долина",
    };

    /// <summary>The kind of a key (first, ore, sacred, forest, marsh, mountain); null for none such.</summary>
    public static ValleyKind? Parse(string key)
    {
        foreach (ValleyKind k in Enum.GetValues(typeof(ValleyKind))) if (Key(k) == key) return k;
        return null;
    }

    /// <summary>
    /// How much iron a kind has: outcrops of ore come up to the surface, and veins lie hidden under the ground. The valleys
    /// of ore are full of it, the mountains have a good deal, every valley a little.
    /// </summary>
    public static (int outcrops, int hidden) OreOf(ValleyKind k) => k switch
    {
        ValleyKind.Ore => (8, 6),
        ValleyKind.Mountain => (6, 4),
        ValleyKind.First => (3, 3),
        ValleyKind.Marsh => (1, 2),
        _ => (2, 2),
    };

    public static Ground GroundOf(ValleyKind k) => k switch
    {
        // the valleys of ore: rock near the surface, many knolls, thin soil, fewer trees
        ValleyKind.Ore => new Ground(trees: .7, knoll: .6, knollHeight: 1.2, ridges: 1.2, river: 1, wet: -.05, soil: .6, lift: 1.1, relief: 1.2, lakes: 0),
        ValleyKind.Sacred => new Ground(trees: .8, knoll: .72, knollHeight: 1, ridges: 1, river: 1, wet: 0, soil: 1, lift: .9, relief: .9, lakes: 1),
        ValleyKind.Forest => new Ground(trees: 1.7, knoll: .74, knollHeight: .9, ridges: .9, river: .9, wet: .08, soil: 1.2, lift: .85, relief: 1, lakes: 2),
        // the river's valley: a broad river, wet meadows, low ground
        ValleyKind.Marsh => new Ground(trees: .9, knoll: .8, knollHeight: .7, ridges: .6, river: 1.8, wet: .25, soil: 1.2, lift: .6, relief: .6, lakes: 3),
        // the mountains: high ridges, crags, little soil and few trees
        ValleyKind.Mountain => new Ground(trees: .55, knoll: .56, knollHeight: 1.6, ridges: 2, river: .8, wet: -.1, soil: .5, lift: 1.3, relief: 1.5, lakes: 1),
        _ => new Ground(trees: 1, knoll: .7, knollHeight: 1, ridges: 1, river: 1, wet: 0, soil: 1, lift: 1, relief: 1, lakes: 1),
    };

    /// <summary>
    /// The places a kind has. The first valley with the old set (classic) has them all; without it only what is no prize
    /// to hold — the ruins and the bridges — so that the mines and the places of power are in other valleys.
    /// </summary>
    public static Places PlacesOf(ValleyKind k, bool classic) => k switch
    {
        ValleyKind.First => classic ? Places.All : Places.Watchtowers | Places.Bridges,
        ValleyKind.Ore => Places.Mines | Places.MinersHouses | Places.Outpost | Places.Bridges,
        ValleyKind.Sacred => Places.Power | Places.Watchtowers | Places.Bridges,
        ValleyKind.Forest => Places.Watchtowers | Places.Bridges,
        ValleyKind.Marsh => Places.Bridges | Places.Outpost,
        ValleyKind.Mountain => Places.Silver | Places.Watchtowers,
        _ => Places.All,
    };
}
