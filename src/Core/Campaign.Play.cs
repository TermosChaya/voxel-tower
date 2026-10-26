using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The campaign as the player plays it, over the map's own turn (Campaign.Turn.cs, MapAI.cs): parties of ours gone out
/// of the valley put on the map and sent on, how long they have still to walk, what of the map can be seen, an outpost
/// raised in the valley we play in making it ours, what a valley pays while we are away, and which of the horde's armies
/// are on their way to a valley of ours.
/// </summary>
public sealed partial class Campaign
{
    /// <summary>The commanders the realm starts with, at the first valley's citadel.</summary>
    public const int StartCommanders = 2;

    public MapCell CellOf(MapParty p) => At(p.Q, p.R);

    /// <summary>Our parties standing in a cell, not walking on.</summary>
    public List<MapParty> PartiesIn(MapCell c) => Parties.FindAll(p => p.Owner == Owner.Ours && p.Q == c.Q && p.R == c.R && !p.Moving);

    /// <summary>A party of ours gone out of the valley we are in, onto the map at its cell, and sent on toward a cell if it was sent to one.</summary>
    public MapParty Out(Party party, MapCell to)
    {
        var p = new MapParty { Id = _nextParty++, Q = HereQ, R = HereR, CameQ = HereQ, CameR = HereR, Party = party, Owner = Owner.Ours };
        Parties.Add(p);
        if (to != null && to != Here) Send(p, to);
        return p;
    }

    /// <summary>The side of its cell a party goes into the valley by: the side it came over from the cell before.</summary>
    public int EntrySideOf(MapParty p) => SideFrom(p);

    /// <summary>How far a walking party has come toward the next cell, 0 to 1.</summary>
    public double StepShare(MapParty p) =>
        !p.Moving ? 0 : Math.Clamp(p.Progress / Math.Max(1e-6, MapPath.StepSeconds(p, CellOf(p), p.Path[0])), 0, 1);

    /// <summary>Seconds of the map's clock a party has still to walk; 0 standing.</summary>
    public double WalkLeft(MapParty p)
    {
        if (!p.Moving) return 0;
        double left = -p.Progress;
        var at = CellOf(p);
        foreach (var next in p.Path) { left += MapPath.StepSeconds(p, at, next); at = next; }
        return Math.Max(0, left);
    }

    /// <summary>
    /// What of the map we see: the cells of ours, the one we are in, those our parties stand in or are halfway into, and
    /// every cell next to one of them; the rest lies in the fog.
    /// </summary>
    public bool Seen(MapCell c)
    {
        foreach (var o in Cells)
        {
            bool ours = o.Owner == Owner.Ours || o == Here || Parties.Exists(p => p.Owner == Owner.Ours && (p.Q == o.Q && p.R == o.R || p.Moving && p.Path[0] == o && StepShare(p) >= .5));
            if (ours && Distance(o.Q, o.R, c.Q, c.R) <= 1) return true;
        }
        return false;
    }

    /// <summary>The outpost raised in the valley we play in stands: the cell is ours, as an outpost raised by a party of ours on the map makes it.</summary>
    public void OutpostStands(MapCell c)
    {
        if (c.Owner == Owner.Ours) return;
        c.Owner = Owner.Ours;
        c.Sum.Raised = 1;
        c.Sum.Keep.Clear();
        Tell(NewsKind.Taken, $"Застава {Names(c).prep} готова: долина наша.", c, null);
    }

    /// <summary>The gold a minute a valley of ours pays while we are away from it: its town as we left it and its mines.</summary>
    public static double AwayGold(MapCell c) =>
        c.Owner == Owner.Ours ? c.Sum.TownGold + ValleySummary.MineGold(c.Kind) * ValleySummary.MineShare : 0;

    /// <summary>The horde's armies on their way to a valley of ours (or to the one we are in), nearest first.</summary>
    public List<MapParty> Coming()
    {
        var list = Parties.FindAll(p => p.Owner == Owner.Horde && p.Moving && p.Aim == PartyAim.Strike
            && At(p.GoalQ, p.GoalR) is { } goal && (goal.Owner == Owner.Ours || goal == Here));
        list.Sort((a, b) => WalkLeft(a).CompareTo(WalkLeft(b)));
        return list;
    }
}
