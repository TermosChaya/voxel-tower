namespace VoxelTower.Core;

/// <summary>
/// What the land of the big world map once had put into its own voxels as it was made (HexTiles.cs calls these for
/// every hex of a piece): nothing now. Its trees, its things, its farms and shores are made finer than the land and kept
/// (MapModels.cs), placed piece by piece (MapDress.cs) and drawn over it (HexMapView.Dress.cs); the land keeps only its
/// ground, levelled under a thing as before.
/// </summary>
public static class HexModels
{
    /// <summary>What grows on a hex, put into the land's voxels: nothing (MapDress.cs places it).</summary>
    public static void Relief(HexCanvas c, in HexLook l, float cx, float cz, int col, int row) { }

    /// <summary>A hex's thing put into the land's voxels: nothing (MapDress.cs places it).</summary>
    public static void Thing(HexCanvas c, in HexLook l, float fx, float fz, int y, int vary) { }
}
