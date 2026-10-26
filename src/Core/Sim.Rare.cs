using System;

namespace VoxelTower.Core;

/// <summary>
/// The realm's rare resources as a valley of a campaign sees them (MapThing.cs): mercury, sulfur, crystals, gems, kept
/// by the realm and set on the valley as it is gone into, as its gold is. A few of the top steps of the town ask for
/// some in a campaign — the alchemists' second storey mercury and sulfur, the Capitol crystals and gems; out of a
/// campaign (the siege, the sandbox) nothing asks for them.
/// </summary>
public sealed partial class Sim
{
    public readonly double[] Rare = new double[4];

    /// <summary>What of the rare resources a step of a town building asks for, in a campaign: (index, how many) pairs, or none.</summary>
    public (int res, int n)[] RareCost(string key, int level)
    {
        if (!Campaign) return Array.Empty<(int, int)>();
        return (key, level) switch
        {
            ("lab", 2) => new[] { (0, 3), (1, 3) },
            ("hall", 3) => new[] { (2, 5), (3, 3) },
            _ => Array.Empty<(int, int)>(),
        };
    }

    /// <summary>Why the rare resources do not suffice for a step, or null.</summary>
    public string RareShort(string key, int level)
    {
        foreach (var (i, n) in RareCost(key, level))
            if (Rare[i] + 1e-9 < n) return $"нужно {n} ({MapThing.NameOf((Res)((int)Res.Mercury + i))})";
        return null;
    }

    /// <summary>The rare resources a step asks for, spent.</summary>
    public void SpendRare(string key, int level)
    {
        foreach (var (i, n) in RareCost(key, level)) Rare[i] = Math.Max(0, Rare[i] - n);
    }
}
