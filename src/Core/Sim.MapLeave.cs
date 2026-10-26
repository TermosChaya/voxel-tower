using System.Collections.Generic;

namespace VoxelTower.Core;

public sealed partial class Sim
{
    /// <summary>
    /// Ours taken off the valley at once and onto the map, as a party gone out over a side (the player's «На карту»):
    /// each as he is, as one going over the edge is (his wounds, his work, his look). Not one lying down, pinned, held,
    /// carried or carrying. Returns the party, or null if none could go.
    /// </summary>
    public Party Withdraw(IEnumerable<Unit> units, int side)
    {
        side = ((side % 6) + 6) % 6;
        var party = new Party { Side = side };
        foreach (var u in new List<Unit>(units))
        {
            if (u.Dead || u.Team != Team.Player || u.T == Kinds.Risen || u.Down || u.PinKg > 0 || u.CarriedBy != null || u.Carrying != null || u.Captor != null) continue;
            party.Members.Add(Traveller.Of(u, CellKey));
            if (u.Captive != null) { u.Captive.Captor = null; u.Captive = null; }
            ReleaseSite(u);
            u.LeaveBy = -1;
            u.Dead = true; u.DiedAt = -999; u.State = UnitState.Dead; u.Path = null;
        }
        if (party.Members.Count == 0) return null;
        int n = party.Members.Count;
        Say($"Отряд вышел на карту {EdgeToward(side)}: {n} {Plural(n, "человек", "человека", "человек")}.", 1);
        return party;
    }
}
