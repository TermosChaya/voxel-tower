using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The looks of the houses drawn with the rectangle, so that a street of them is not one box over and over (the author:
/// «делаем дома разными»). What a house is built of picks the looks it may have: of logs a log house under shingles or
/// planks, a framed house with daub between its timbers under tiles or thatch, a daubed one under steep thatch; of stone a
/// plain stone house under slate, one of big rough stones, a stone ground floor under framing; of earth a daubed house or a
/// rammed one under tiles. Its plot picks one of them, and as surely the wash on its daub (white lime, ochre, rose, a
/// grey-blue or none), the paint on its shutters, and whether a porch stands over its door or a woodshed leans on an end.
/// The same plot drawn again is the same house; a storey put up on it goes on in its look. Everything of it is laid by
/// the workers from the stores as any house is, the porch and the shed outside its walls no part of its room.
/// </summary>
public sealed partial class Sim
{
    /// <summary>A house's look before its plot's wash, paint and parts: its name, its walls, its roof's form and covering.</summary>
    readonly record struct HouseStyle(string Name, WallStyle Walls, RoofForm Roof, byte Covering);

    static readonly HouseStyle[] LogHouses =
    {
        new("сруб под дранкой", WallStyle.Plain, RoofForm.Gable, Mat.Shingle),
        new("сруб с полувальмой", WallStyle.Plain, RoofForm.HalfHip, 0),
        new("фахверк под черепицей", WallStyle.Frame, RoofForm.Gable, Mat.Tile),
        new("фахверк под соломой", WallStyle.Frame, RoofForm.HalfHip, Mat.Thatch),
        new("мазанка под соломой", WallStyle.Plaster, RoofForm.Steep, Mat.Thatch),
        new("фахверк с вальмой", WallStyle.Frame, RoofForm.Hip, Mat.Shingle),
    };

    static readonly HouseStyle[] StoneHouses =
    {
        new("каменный под сланцем", WallStyle.Plain, RoofForm.Gable, Mat.Slate),
        new("из бута под черепицей", WallStyle.Rough, RoofForm.Hip, Mat.Tile),
        new("каменный низ, фахверк", WallStyle.StoneBase, RoofForm.HalfHip, Mat.Tile),
        new("каменный под соломой", WallStyle.Plain, RoofForm.Steep, Mat.Thatch),
        new("каменный низ под дранкой", WallStyle.StoneBase, RoofForm.Gable, Mat.Shingle),
    };

    static readonly HouseStyle[] EarthHouses =
    {
        new("мазанка под соломой", WallStyle.Plaster, RoofForm.Steep, Mat.Thatch),
        new("глинобитный под черепицей", WallStyle.Plain, RoofForm.Hip, Mat.Tile),
        new("мазанка с полувальмой", WallStyle.Plaster, RoofForm.HalfHip, Mat.Shingle),
    };

    /// <summary>The washes a house's daub may have (the plain daub twice as often as any one colour), and its shutters' paints.</summary>
    static readonly byte[] Washes = { Mat.Daub, Mat.Daub, Mat.DaubWhite, Mat.DaubOchre, Mat.DaubRose, Mat.DaubBlue };
    static readonly byte[] Paints = { 0, 0, Mat.ShutterGreen, Mat.ShutterRed, Mat.ShutterBlue };

    /// <summary>The looks of houses made so far, by what picked them: the same plot drawn every frame is not a new look each time.</summary>
    readonly Dictionary<long, Look> _houseLooks = new();

    /// <summary>The styles a house built of a material may have; none for what is no house's wall (iron, the rest).</summary>
    static HouseStyle[] StylesOf(byte wallMat) =>
        wallMat == Mat.Wood ? LogHouses : Mat.IsMasonry(wallMat) ? StoneHouses : wallMat == Mat.Rammed ? EarthHouses : null;

    /// <summary>The roll of a house's plot for one of its choices: the same plot, the same roll.</summary>
    static double PlotRoll(int x0, int z0, int x1, int z1, int salt) => Hash.H3((x0 >> 2) * 31 + (x1 >> 2), salt * 977 + 13, (z0 >> 2) * 17 + (z1 >> 2));

    /// <summary>
    /// The look of a house of this material on this plot: one of its material's styles, its daub's wash, its shutters'
    /// paint, a porch over its door (some houses), a woodshed by one of its ends (fewer); null for a material with none.
    /// </summary>
    public Look HouseLook(byte wallMat, int x0, int z0, int x1, int z1)
    {
        var styles = StylesOf(wallMat);
        if (styles == null) return null;
        int style = Math.Min(styles.Length - 1, (int)(PlotRoll(x0, z0, x1, z1, 1) * styles.Length));
        var s = styles[style];
        bool daubed = s.Walls is WallStyle.Frame or WallStyle.Plaster or WallStyle.StoneBase;
        int wash = daubed ? Math.Min(Washes.Length - 1, (int)(PlotRoll(x0, z0, x1, z1, 2) * Washes.Length)) : 0;
        int paint = Math.Min(Paints.Length - 1, (int)(PlotRoll(x0, z0, x1, z1, 3) * Paints.Length));
        bool porch = PlotRoll(x0, z0, x1, z1, 4) < .4;
        double shedRoll = PlotRoll(x0, z0, x1, z1, 5);
        int shed = shedRoll < .15 ? 1 : shedRoll < .3 ? 2 : 0;
        long key = ((long)wallMat << 24) | ((long)style << 16) | ((long)wash << 12) | ((long)paint << 8) | (porch ? 16L : 0) | (uint)shed;
        if (_houseLooks.TryGetValue(key, out var look)) return look;
        var annexes = new List<Annex>();
        if (porch) annexes.Add(PorchOn(s.Covering));
        // a woodshed open to the front, logs stacked under it, against the end on the door's left or right
        if (shed > 0) annexes.Add(ShedOn(shed == 1 ? Flank.Left : Flank.Right, 18, 0, s.Covering, Prop.Logs));
        look = new Look
        {
            Name = s.Name, Walls = s.Walls, Roof = s.Roof, Covering = s.Covering, Annexes = annexes.ToArray(),
            Daub = Washes[wash], Shutter = Paints[paint],
        };
        _houseLooks[key] = look;
        return look;
    }

    /// <summary>
    /// Whether a column is kept clear of what a house puts up outside its walls: a way laid or trodden there, a road
    /// close by, the ground before another door.
    /// </summary>
    bool KeptClear(int x, int z)
    {
        if (!World.InBounds(x, 1, z)) return true;
        int top = World.SurfAt(x, z) - 1;
        if (top >= 0 && Mat.IsPavement[World.Get(x, top, z)]) return true;
        if (Site.RoadDist(x, z) < 8) return true;
        const int Before = 18;
        foreach (var g in Gates)
        {
            var (mx, _, mz) = g.Middle;
            if ((mx - x) * (mx - x) + (mz - z) * (mz - z) < Before * Before) return true;
        }
        foreach (var (d, _) in HouseDoors)
            if ((d.X - x) * (d.X - x) + (d.Z - z) * (d.Z - z) < Before * Before) return true;
        return false;
    }

    /// <summary>The look a house that stands on a dwelling's plot was put up in, if it was drawn with one (its wall's material told by what stands).</summary>
    Look HouseLookOf(Zone z, byte wallMat) => z.Kind == Zone.Dwelling && BuildingOn(z) == null ? HouseLook(wallMat, z.X0, z.Z0, z.X1, z.Z1) : null;
}
