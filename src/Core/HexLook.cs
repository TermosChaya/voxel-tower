using System;

namespace VoxelTower.Core;

/// <summary>The ground a hex of the world map is, as the eye takes it from above (docs/handoff/13-world-map.md).</summary>
public enum HexGround : byte { Grass, Dirt, Sand, Snow, Swamp, Rough, Lava, Scorched, Water, DeepWater }

/// <summary>What stands up out of a hex's ground: nothing, low hills, a wood, or mountains no one walks over.</summary>
public enum HexRelief : byte { Flat, Hills, Forest, Mountain }

/// <summary>The one thing a hex of the world map holds and shows, if any.</summary>
public enum HexIcon : byte
{
    None, Citadel, Outpost, OutpostPlace, HordeTown, Lair,
    MineLogs, MineStone, MineMetal, MineGold, MineMercury, MineSulfur, MineCrystal, MineGems,
    Pile, Chest, Campfire, Windmill, WaterWheel, Shrine, Dwelling, Monster,
    DeadMine, Ruins, Den, Trial, Signpost, Learning, Stables, Observatory, Pass,
}

/// <summary>
/// How a hex of the world map looks, all the drawing of the map needs of it: its ground and relief, a height step, the
/// sides a road and a river leave it by (a bit for each side, north-east first, clockwise, as <see cref="Hex"/>), the
/// thing it shows, whose that is (0 nobody, 1 ours, 2 the horde's), a variant for the eye, and the zone it lies in.
/// </summary>
public struct HexLook
{
    public HexGround Ground;
    public HexRelief Relief;
    public byte Height;
    public byte Roads, Rivers;
    public HexIcon Icon;
    public byte Owner;
    public byte Variant;
    public short Zone;
}

/// <summary>
/// A world map of hexes for the eye: a rectangle of Width × Height hexes in odd-r offset (every odd row half a hex to
/// the east), col 0…Width−1 west to east, row 0…Height−1 north to south; axial q = col − (row − (row &amp; 1)) / 2, r = row.
/// </summary>
public interface IHexSource
{
    int Width { get; }
    int Height { get; }
    HexLook Look(int col, int row);
    /// <summary>Whether the hex is out of the fog for us.</summary>
    bool Seen(int col, int row);
    /// <summary>A hex's look or fog changed (taken, flagged, picked up, seen).</summary>
    event Action<int, int> Changed;
}
