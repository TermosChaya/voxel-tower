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

    /// <summary>A cell as those of ours keep the valley whose places they hold (<see cref="Unit.Billet"/>, <see cref="Traveller.Billet"/>).</summary>
    public static int KeyOf(MapCell c) => (c.Q + 64) * 128 + (c.R + 64);

    /// <summary>The cell of a key, or null.</summary>
    public MapCell CellOfKey(int key) => key < 0 ? null : At(key / 128 - 64, key % 128 - 64);

    /// <summary>
    /// What the rest of the realm keeps for the valley open now (<paramref name="cell"/>'s): the places held by its own who
    /// are away (gone over its edges, walking the map, kept in another valley of ours), the commanders not in it, and the
    /// levels of the town halls of the other valleys of ours. Set as it is entered and as the map runs.
    /// </summary>
    public void Reckon(Sim sim, MapCell cell)
    {
        int key = KeyOf(cell), places = 0, captains = 0, halls = 0;
        void Count(Traveller m)
        {
            if (m.Billet == key) places += m.Kind.Room;
            if (m.Kind == Kinds.Captain) captains++;
        }
        foreach (var p in sim.Departed) foreach (var m in p.Members) Count(m);
        foreach (var p in Parties) if (p.Owner == Owner.Ours) foreach (var m in p.Party.Members) Count(m);
        foreach (var c in Cells)
        {
            if (c == cell || c.Owner != Owner.Ours) continue;
            foreach (var m in c.Sum.Keep) Count(m);
            halls += c.Sum.Hall;
        }
        sim.AwayPlaces = places; sim.CaptainsAway = captains; sim.HallsAway = halls;
    }

    /// <summary>The places of a valley of ours we are not in, as the map keeps them: held by its own (here or away) of so many.</summary>
    public (int held, int guests) PlacesOf(MapCell cell)
    {
        int key = KeyOf(cell), held = 0, guests = 0;
        foreach (var m in cell.Sum.Keep) { if (m.Billet == key || m.Billet < 0) held += m.Kind.Room; else guests += m.Kind.Room; }
        foreach (var p in Parties) if (p.Owner == Owner.Ours) foreach (var m in p.Party.Members) if (m.Billet == key) held += m.Kind.Room;
        foreach (var c in Cells)
            if (c != cell && c.Owner == Owner.Ours) foreach (var m in c.Sum.Keep) if (m.Billet == key) held += m.Kind.Room;
        return (held, guests);
    }

    /// <summary>Our parties standing in a cell, not walking on.</summary>
    public List<MapParty> PartiesIn(MapCell c) => Parties.FindAll(p => p.Owner == Owner.Ours && p.Q == c.Q && p.R == c.R && !p.Moving);

    /// <summary>A party of ours gone out of the valley we are in, onto the map at its cell, and sent on toward a cell if it was sent to one.</summary>
    public MapParty Out(Party party, MapCell to)
    {
        foreach (var m in party.Members) if (m.Billet < 0) m.Billet = KeyOf(Here);
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
