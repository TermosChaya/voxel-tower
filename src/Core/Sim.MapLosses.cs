using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The valley come back to after battles the map counted while we were away (Campaign.Turn.cs): those of ours who fell
/// in them are no longer in it.
/// </summary>
public sealed partial class Sim
{
    /// <summary>
    /// Of ours in the valley, of each kind, as many are gone as there are more of them here than the map kept alive —
    /// the most hurt first, quietly: they fell far from here. Returns how many.
    /// </summary>
    public int FellAway(IReadOnlyList<Traveller> kept)
    {
        var left = new Dictionary<UnitType, int>();
        foreach (var m in kept) left[m.Kind] = left.GetValueOrDefault(m.Kind) + 1;
        var mine = Units.FindAll(u => !u.Dead && u.Team == Team.Player && !u.Bound && u.Captor == null);
        // the healthiest stay
        mine.Sort((a, b) => (b.Hp / Math.Max(1, b.MaxHp)).CompareTo(a.Hp / Math.Max(1, a.MaxHp)));
        int gone = 0;
        foreach (var u in mine)
        {
            if (left.TryGetValue(u.T, out int n) && n > 0) { left[u.T] = n - 1; continue; }
            if (u.Captive != null) { u.Captive.Captor = null; u.Captive = null; }
            if (u == Possessed) continue;
            ReleaseSite(u);
            u.Dead = true; u.DiedAt = -999; u.State = UnitState.Dead; u.Path = null; u.Hp = 0;
            gone++;
        }
        return gone;
    }
}
