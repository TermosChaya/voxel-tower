using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>Who plays a side of the big map: our kingdom, the horde (more to come, and the player at the head of any of them).</summary>
public enum FactionKind : byte { Kingdom, Horde }

/// <summary>How hard the horde plays (docs/handoff/13-world-map.md, 2.18): what it earns and breeds, how long it leaves us be, how sure it wants to be before it strikes.</summary>
public enum Difficulty : byte { Easy, Normal, Hard }

/// <summary>
/// A faction of the big map as «Heroes» has a player: a side with its towns, its heroes, its treasury and what its
/// towns breed a week. Ours keeps its treasury where it always was — the realm's gold and rare resources, each valley's
/// stores (Campaign.Gold, Rare, MapCell.Sum) — and is played by the player (or, for the harness, by AutoPlayer.cs); the
/// horde's treasury, towns and heroes are its own (<see cref="Realm"/>, MapAI*.cs), played by the machine. The rest of
/// the game asks a faction through the campaign: <see cref="Campaign.TownsOf"/>, <see cref="Campaign.HeroesOf"/>,
/// <see cref="Campaign.Treasury"/>.
/// </summary>
public sealed class Faction
{
    public Owner Side;
    public FactionKind Kind;
    /// <summary>Its name, and as said of it («войско Орды»).</summary>
    public string Name, Of;
    /// <summary>Played by the machine.</summary>
    public bool Machine;
}

public sealed partial class Campaign
{
    /// <summary>The factions of the map: ours first, then the horde.</summary>
    public readonly Faction[] Factions =
    {
        new() { Side = Owner.Ours, Kind = FactionKind.Kingdom, Name = "Королевство", Of = "Королевства" },
        new() { Side = Owner.Horde, Kind = FactionKind.Horde, Name = "Орда", Of = "Орды", Machine = true },
    };

    /// <summary>How hard the horde plays this campaign.</summary>
    public Difficulty Difficulty = Difficulty.Normal;

    public Faction FactionOf(Owner side) => side == Owner.Horde ? Factions[1] : Factions[0];

    /// <summary>A faction's towns: ours its strongholds (the first valley, the outposts raised), the horde's its towns and lairs.</summary>
    public IEnumerable<MapCell> TownsOf(Owner side) => side == Owner.Horde ? Lairs : Strongholds;

    /// <summary>A faction's heroes on the map: the parties a hero of it leads (ours a commander-hero, the horde's a warlord-hero).</summary>
    public IEnumerable<MapParty> HeroesOf(Owner side)
    {
        foreach (var p in Parties)
            if (p.Owner == side && (side == Owner.Horde ? p.Lord?.Hero != null : p.Party.Commander?.Hero != null)) yield return p;
    }

    /// <summary>What a faction has of a resource: ours as the realm keeps it, the horde's in its own treasury.</summary>
    public double Treasury(Owner side, Res r) => side == Owner.Horde ? Realm.Stock[(int)r] : StockOf(r);

    // ------------------------------------------------------------ the difficulty's knobs

    /// <summary>The days at the start the horde leaves ours be: it strikes at nothing of ours till they are out.</summary>
    public int PeaceDays => Difficulty switch { Difficulty.Easy => 10, Difficulty.Hard => 5, _ => 7 };
    /// <summary>The fewest days between two armies of the horde set out against the first valley.</summary>
    public int MarchGap => Difficulty switch { Difficulty.Easy => 5, Difficulty.Hard => 3, _ => 4 };
    /// <summary>What the horde's towns earn and its mines give, against the normal.</summary>
    public double HordeIncome => Difficulty switch { Difficulty.Easy => .7, Difficulty.Hard => 1.35, _ => 1 };
    /// <summary>What the horde's towns breed a week, against the normal.</summary>
    public double HordeGrowth => Difficulty switch { Difficulty.Easy => .75, Difficulty.Hard => 1.3, _ => 1 };
    /// <summary>The chance the horde wants of winning before it strikes at ours.</summary>
    public float HordeWants => Difficulty switch { Difficulty.Easy => .8f, Difficulty.Hard => .7f, _ => WantOdds };
    /// <summary>The fewest men an army of the horde sets out against ours with.</summary>
    public int LeastArmy => Difficulty switch { Difficulty.Easy => 16, Difficulty.Hard => 10, _ => 12 };
    /// <summary>The most heroes a town of the horde keeps: one, and one more with its war camp, no more than this.</summary>
    public int HeroesPerTown => Difficulty switch { Difficulty.Easy => 1, Difficulty.Hard => 3, _ => 2 };

    public static string NameOf(Difficulty d) => d switch { Difficulty.Easy => "Лёгкая", Difficulty.Hard => "Трудная", _ => "Обычная" };
}
