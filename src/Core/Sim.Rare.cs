using System;

namespace VoxelTower.Core;

/// <summary>
/// The realm's rare resources as a valley of a campaign sees them (MapThing.cs): mercury, sulfur, crystals, gems, kept
/// by the realm and set on the valley as it is gone into, as its gold is. The top steps of the dear buildings ask for
/// some in a campaign, as the dwellings of «Heroes» do — the alchemists' second storey mercury and sulfur, the Capitol
/// crystals and gems, the giants', the trolls', the workshop's, the stables' and the market's second storeys theirs (<see
/// cref="RareCost"/>); out of a campaign (the siege, the sandbox) nothing asks for them.
/// </summary>
public sealed partial class Sim
{
    public readonly double[] Rare = new double[4];

    /// <summary>What of the rare resources a step of a town building asks for, in a campaign: (index, how many) pairs, or none.</summary>
    public (int res, int n)[] RareCost(string key, int level)
    {
        if (!Campaign) return Array.Empty<(int, int)>();
        // (the dear dwellings of «Heroes» ask for the rare ones: the alchemists mercury and sulfur, the giants and the
        // capitol crystals and gems, the engines sulfur and mercury, the knights' horses and the trolls' den crystals and sulfur)
        return (key, level) switch
        {
            ("lab", 2) => new[] { (0, 3), (1, 3) },
            ("hall", 3) => new[] { (2, 5), (3, 3) },
            ("market", 2) => new[] { (3, 2) },
            ("giants", 2) => new[] { (2, 3), (3, 2) },
            ("trolls", 2) => new[] { (1, 3) },
            ("workshop", 2) => new[] { (0, 2), (1, 2) },
            ("stables", 2) => new[] { (2, 2) },
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
