using System;

namespace VoxelTower.Core;

/// <summary>
/// The outpost of a valley taken on the map: a square stone tower, battlements and a stair to the top, put up for the
/// workers beside the stone of the outpost's place (or in the yard of a horde's town taken, toward its gap). It is the
/// realm's to put, whatever rank the settlement has.
/// </summary>
public sealed partial class Sim
{
    /// <summary>How far from the middle the outpost's tower stands (4½ m), and how high (5 m).</summary>
    public const int OutpostOff = 36, OutpostHeight = 40;

    /// <summary>Where the outpost's tower goes: beside the stone, or in a taken town's yard toward the side we came by.</summary>
    public (int x, int z) OutpostSpot()
    {
        if (Site.Holding == Holding.HordeTown && Site.Hex != null)
        {
            var (ix, iz) = Site.Hex.Inward(Site.EntrySide);
            return (Site.KeepX - (int)Math.Round(ix * OutpostOff), Site.KeepZ - (int)Math.Round(iz * OutpostOff));
        }
        return (Site.KeepX + OutpostOff, Site.KeepZ);
    }

    /// <summary>The outpost's tower marked for the workers; null, or why not.</summary>
    public string RaiseOutpost()
    {
        if (Site.HasKeep) return "здесь цитадель";
        var t = TemplateOf("squaretower");
        var (x, z) = OutpostSpot();
        var plan = t.Plan(World, Site, x, z, x, z, 0, OutpostHeight);
        if (plan?.Voxels == null) return plan?.Why ?? "здесь нельзя";
        if (DesignateBuild(plan.Voxels) == 0) return "здесь нечего класть";
        if (plan.Arched != null) Arch(plan.Arched);
        return null;
    }

    /// <summary>
    /// The outpost's tower stands whole: its walls, a metre thick round a hollow middle, laid up to their top all round
    /// (the scaffolds the workers put up and take down again inside it are no part of it).
    /// </summary>
    public bool OutpostStands() => OutpostLaid() >= .97;

    /// <summary>The share of the outpost's walls laid to their full height, 0 to 1.</summary>
    public double OutpostLaid()
    {
        if (Site.HasKeep) return 0;
        var (cx, cz) = OutpostSpot();
        const int S = 16, Wall = 8;
        int g = Site.HTop[cz * World.SX + cx], top = g + OutpostHeight - 1, walls = 0, laid = 0;
        for (int z = cz - S; z < cz + S; z++)
            for (int x = cx - S; x < cx + S; x++)
            {
                int ex = Math.Min(x - (cx - S), cx + S - 1 - x), ez = Math.Min(z - (cz - S), cz + S - 1 - z);
                if (Math.Min(ex, ez) >= Wall) continue;
                walls++;
                // laid from the ground up, a wall whole at its top is whole below it (but for the way in and the slits)
                if (Mat.IsMasonry(World.Get(x, top, z))) laid++;
            }
        return walls == 0 ? 0 : laid / (double)walls;
    }
}
