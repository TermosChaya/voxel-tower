using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Where the hexes of the world map (HexLook.cs) lie in the voxels it is drawn in: pointed north and south, a hex
/// <see cref="R"/> voxels from its middle to a corner, every odd row half a hex to the east, as «Heroes» lays its
/// squares — so the whole map of 144 × 144 hexes is some 4600 × 4000 voxels, and it is drawn a piece at a time
/// (<see cref="Tile"/> voxels square) about where the eye is. A margin of land lies about the field: three half-hexes
/// at the west and east, two rows at the north and south. The fog is laid on a grid of half-hexes by rows
/// (<see cref="FogW"/> × <see cref="FogH"/>), each cell of it within one hex, so that a texture of it is read straight
/// from a voxel's place: texel (i, j) covers x from i·<see cref="HalfW"/>, z from j·<see cref="RowStep"/>.
/// </summary>
public sealed class HexMapLayout
{
    /// <summary>Voxels from a hex's middle to a corner.</summary>
    public const int R = 18;
    public const float Root3 = 1.7320508f;
    /// <summary>A hex across its flats, west to east; half of it; the distance from one row's middles to the next's.</summary>
    public const float W = R * Root3, HalfW = W / 2, RowStep = 1.5f * R;
    /// <summary>The margin of land about the field: half-hexes at the west and the east, rows at the north and the south.</summary>
    public const int MarginHalves = 3, MarginRows = 2;
    /// <summary>The side of a piece of the map built and drawn at once, voxels.</summary>
    public const int Tile = 128;

    public readonly int Cols, Rows;
    /// <summary>The map's size in voxels, the margin with it.</summary>
    public readonly int SX, SZ;
    /// <summary>The middle of hex (0, 0), voxels.</summary>
    public readonly float OX, OZ;
    /// <summary>The fog's grid: half-hexes across, rows down, the margin with them.</summary>
    public readonly int FogW, FogH;
    public readonly int TilesX, TilesZ;

    public HexMapLayout(int cols, int rows)
    {
        Cols = cols; Rows = rows;
        FogW = 2 * cols + 1 + 2 * MarginHalves;
        FogH = rows + 2 * MarginRows;
        OX = (MarginHalves + 1) * HalfW;
        OZ = (MarginRows + .5f) * RowStep;
        SX = (int)MathF.Ceiling(FogW * HalfW);
        SZ = (int)MathF.Ceiling(FogH * RowStep);
        TilesX = (SX + Tile - 1) / Tile;
        TilesZ = (SZ + Tile - 1) / Tile;
    }

    /// <summary>The sizes «Heroes» has: S 36, M 72, L 108, XL 144 hexes a side.</summary>
    public static int SideOf(string size) => size?.ToUpperInvariant() switch { "S" => 36, "M" => 72, "L" => 108, "XL" => 144, _ => 36 };

    public bool OnField(int col, int row) => (uint)col < (uint)Cols && (uint)row < (uint)Rows;

    /// <summary>The middle of a hex, voxels.</summary>
    public (float X, float Z) Center(int col, int row) => (OX + W * (col + .5f * (row & 1)), OZ + RowStep * row);

    /// <summary>The hex a point of the map lies in (it may be off the field: see <see cref="OnField"/>).</summary>
    public (int Col, int Row) HexAt(float x, float z)
    {
        float rx = (x - OX) / R, rz = (z - OZ) / R;
        float fq = Root3 / 3 * rx - rz / 3, fr = 2f / 3 * rz, fs = -fq - fr;
        float q = MathF.Round(fq), r = MathF.Round(fr), s = MathF.Round(fs);
        float dq = MathF.Abs(q - fq), dr = MathF.Abs(r - fr), ds = MathF.Abs(s - fs);
        if (dq > dr && dq > ds) q = -r - s; else if (dr > ds) r = -q - s;
        int ri = (int)r, qi = (int)q;
        return (qi + (ri - (ri & 1)) / 2, ri);
    }

    /// <summary>The hex across a side (<see cref="Hex.NorthEast"/> first, clockwise), on the field or not.</summary>
    public static (int Col, int Row) Neighbour(int col, int row, int side)
    {
        bool odd = (row & 1) == 1;
        return side switch
        {
            Hex.NorthEast => (odd ? col + 1 : col, row - 1),
            Hex.East => (col + 1, row),
            Hex.SouthEast => (odd ? col + 1 : col, row + 1),
            Hex.SouthWest => (odd ? col : col - 1, row + 1),
            Hex.West => (col - 1, row),
            _ => (odd ? col : col - 1, row - 1),
        };
    }

    /// <summary>The way from a hex's middle to the middle of the one across a side, voxels.</summary>
    public static (float X, float Z) Toward(int side) => side switch
    {
        Hex.NorthEast => (HalfW, -RowStep),
        Hex.East => (W, 0),
        Hex.SouthEast => (HalfW, RowStep),
        Hex.SouthWest => (-HalfW, RowStep),
        Hex.West => (-W, 0),
        _ => (-HalfW, -RowStep),
    };

    /// <summary>The side of a hex a point near it (relative to its middle) is nearest.</summary>
    public static int SideToward(float dx, float dz)
    {
        // the sides' ways are 60° apart, east at 0°, north-east at −60° (z grows south)
        float a = MathF.Atan2(dz, dx) * 180 / MathF.PI;
        int k = (int)MathF.Round(a / 60f);
        return (((k % 6) + 6) % 6) switch { 0 => Hex.East, 1 => Hex.SouthEast, 2 => Hex.SouthWest, 3 => Hex.West, 4 => Hex.NorthWest, _ => Hex.NorthEast };
    }

    /// <summary>How far in from its nearest side a point (relative to the hex's middle) lies, voxels; negative outside.</summary>
    public static float EdgeDistance(float dx, float dz)
    {
        float ox = MathF.Abs(dx), oz = MathF.Abs(dz);
        return MathF.Min(R * .8660254f - ox, (R - oz - ox * .57735f) * .8660254f);
    }

    /// <summary>The hex a cell of the fog's grid lies in; (−1, −1) for the margin.</summary>
    public (int Col, int Row) FogHex(int i, int j)
    {
        int row = j - MarginRows;
        if ((uint)row >= (uint)Rows) return (-1, -1);
        int k = i - MarginHalves - (row & 1);
        if (k < 0) return (-1, -1);
        int col = k / 2;
        return col < Cols ? (col, row) : (-1, -1);
    }

    /// <summary>Every hex on the field that comes within a box of the map (voxels, a margin about it).</summary>
    public void HexesIn(float x0, float z0, float x1, float z1, List<(int Col, int Row)> into)
    {
        into.Clear();
        int r0 = Math.Max(0, (int)MathF.Floor((z0 - OZ - R) / RowStep)), r1 = Math.Min(Rows - 1, (int)MathF.Ceiling((z1 - OZ + R) / RowStep));
        for (int row = r0; row <= r1; row++)
        {
            float off = OX + W * .5f * (row & 1);
            int c0 = Math.Max(0, (int)MathF.Floor((x0 - off - HalfW) / W)), c1 = Math.Min(Cols - 1, (int)MathF.Ceiling((x1 - off + HalfW) / W));
            for (int col = c0; col <= c1; col++) into.Add((col, row));
        }
    }

    /// <summary>The pieces (tiles) a box of the map touches.</summary>
    public (int tx0, int tz0, int tx1, int tz1) TilesIn(float x0, float z0, float x1, float z1) =>
        (Math.Clamp((int)MathF.Floor(x0 / Tile), 0, TilesX - 1), Math.Clamp((int)MathF.Floor(z0 / Tile), 0, TilesZ - 1),
         Math.Clamp((int)MathF.Floor(x1 / Tile), 0, TilesX - 1), Math.Clamp((int)MathF.Floor(z1 / Tile), 0, TilesZ - 1));
}
