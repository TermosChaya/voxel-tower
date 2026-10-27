using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The neutral stacks of the big map as a party meets them (Bestiary.cs), as the wandering monsters of «Heroes» do: a
/// stack far weaker than the party coming at it runs off and leaves the way open; the people of the wild and the proud
/// ask to go with it instead — the player takes them into the party or fights them. And what keeps a hex, for the
/// horde's minds to weigh (MapAI.cs).
/// </summary>
public sealed partial class Campaign
{
    /// <summary>How many times stronger than a stack a party of ours must be for the stack to run or to ask to join it.</summary>
    public const float FleeRatio = 6, JoinRatio = 3;

    /// <summary>Stacks of the wild asking to go with a party of ours, the player not yet done with them.</summary>
    public readonly List<(MapParty p, MapCell cell)> Joiners = new();

    static readonly List<Traveller> NoKeepers = new();

    /// <summary>
    /// The neutral stack keeping a hex of the big map, and how strong it stands (QuickBattle.Strength): an empty list
    /// and nought where there is none (the hex held by someone, or by a treasure's keepers that keep to it).
    /// </summary>
    public (List<Traveller> keep, float strength) GuardAt(int col, int row)
    {
        var c = Cell(col, row);
        if (c == null || c.Owner != Owner.None || c.Sum.Keep.Count == 0 || Bestiary.Of(c.Sum.Keep[0].Kind) == null) return (NoKeepers, 0);
        return (c.Sum.Keep, QuickBattle.Strength(c.Sum.Keep, null));
    }

    /// <summary>
    /// A party of ours walks up to a stack of one breed of the wild keeping a hex nobody holds: six times stronger, a
    /// stack that runs from the strong runs and the way is open; three times, one that may join asks to (and the party
    /// waits for the player). True if either: the stack ran, or it asks.
    /// </summary>
    internal bool Meet(MapParty p, MapCell cell)
    {
        if (!Big || cell.Owner != Owner.None || cell.Sum.Keep.Count == 0 || Untaken(cell)) return false;
        foreach (var h in Parties) if (h.Owner == Owner.Horde && h.Q == cell.Q && h.R == cell.R) return false;
        var b = Bestiary.Of(cell.Sum.Keep[0].Kind);
        if (b == null || cell.Sum.Keep.Exists(m => m.Kind != b.Kind)) return false;
        float ratio = QuickBattle.Strength(p.Party.Members, null) / MathF.Max(1, QuickBattle.Strength(cell.Sum.Keep, null));
        string what = Bestiary.Label(cell.Sum.Keep);
        if (b.Joins && ratio >= JoinRatio && Kinds.OursOf(b.Kind) != null)
        {
            if (!Joiners.Exists(j => j.cell == cell)) Joiners.Add((p, cell));
            p.Path?.Clear(); p.Progress = 0; p.Waits = true;
            Tell(NewsKind.Arrived, $"{what}. Видят силу отряда и просятся к вам. Взять их или сразиться?", cell, p);
            return true;
        }
        if (b.Flees && ratio >= FleeRatio)
        {
            cell.Sum.Keep.Clear();
            HexChanged(cell);
            Tell(NewsKind.Arrived, $"{what}: разбежались, завидев отряд. Путь свободен.", cell, p);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The player's answer to a stack asking to join: taken, they go with the party as ours and the hex is open; not
    /// taken, the party fights them at once (a quick battle). What is said of it.
    /// </summary>
    public string Join(MapCell cell, bool take)
    {
        int i = Joiners.FindIndex(j => j.cell == cell);
        if (i < 0) return null;
        var p = Joiners[i].p;
        Joiners.RemoveAt(i);
        p.Waits = false;
        if (!take || !Parties.Contains(p))
        {
            string named = Bestiary.Label(cell.Sum.Keep);
            var r = Parties.Contains(p) ? FightHere(p) : null;
            return r == null ? null : r.AttackerWon ? $"Бой выигран: {named.ToLowerInvariant()} разбиты." : "Бой проигран.";
        }
        int n = 0;
        foreach (var m in cell.Sum.Keep)
        {
            if (Kinds.OursOf(m.Kind) is not { } ours) continue;
            p.Party.Members.Add(new Traveller { Kind = ours, Hp = Math.Min(ours.Hp, m.Hp), MaxHp = ours.Hp });
            n++;
        }
        string said = $"{Bestiary.Label(cell.Sum.Keep)}: идут с отрядом. В отряде теперь {p.Party.Members.Count}.";
        cell.Sum.Keep.Clear();
        HexChanged(cell);
        Tell(NewsKind.Arrived, said, cell, p);
        return n > 0 ? said : null;
    }
}
