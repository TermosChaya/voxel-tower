using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// An army of the horde on its way to a valley of ours met at once, by the map's reckoning (the player's «Быстрый бой»
/// on its warning): a valley we are not in fights it from behind its walls as if it had come; the valley we are in
/// sends out everyone who can fight to meet it before the walls, and those who fall there fall in the valley. Won, what
/// is left of the army goes home; lost, it walks on, and what is left of it comes as a raid.
/// </summary>
public sealed partial class Campaign
{
    /// <summary>The horde's army met now; null if it is not one on its way to a valley of ours.</summary>
    public QuickResult DefendNow(MapParty enemy)
    {
        if (enemy == null || enemy.Owner != Owner.Horde || !Parties.Contains(enemy)) return null;
        var cell = At(enemy.GoalQ, enemy.GoalR);
        if (cell == null || cell.Owner != Owner.Ours && !(InValley && cell == Here)) return null;
        if (!(InValley && cell == Here && Live != null))
        {
            // a valley we are not in: as if it had come there
            enemy.CameQ = enemy.Q; enemy.CameR = enemy.R;
            enemy.Q = cell.Q; enemy.R = cell.R;
            enemy.Path = null; enemy.Trail = null; enemy.Progress = 0;
            if (Land != null) (enemy.AtX, enemy.AtZ) = Land.Center(cell);
            int had = cell.Sum.Keep.Count;
            Assault(enemy, cell);
            return new QuickResult { AttackerWon = cell.Owner == Owner.Horde, Odds = had };
        }
        // the valley we are in: everyone who can fight, out before the walls
        var sim = Live;
        var by = new Dictionary<Traveller, Unit>();
        var ours = new List<Traveller>();
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.Team != Team.Player || u.Bound || u.Envoy || u.Captor != null || u.T == Kinds.Risen) continue;
            var t = Traveller.Of(u, KeyOf(cell));
            by[t] = u; ours.Add(t);
        }
        var stood = new List<Traveller>(ours);
        var r = QuickBattle.Fight(enemy.Party.Members, enemy.Lord, ours, null, ValleySummary.WallsOf(sim), cell.Kind, Roll);
        if (r.AttackerLordFell) enemy.Lord = null;
        HeroNews(r, cell, null);
        foreach (var t in stood)
        {
            var u = by[t];
            if (!ours.Contains(t)) sim.Hurt(u, null, 1e6f, "пал в бою у стен");
            else u.Hp = Math.Min(u.Hp, t.Hp);
        }
        if (!r.AttackerWon)
        {
            Tell(NewsKind.Fought, $"Бой у стен {Names(cell).gen}: войско орды разбито. Пало налётчиков {r.AttackerFell.Count}, наших {r.DefenderFell.Count}.", cell, enemy);
            Beaten(enemy);
        }
        else Tell(NewsKind.RaidComing, $"Бой у стен {Names(cell).gen} проигран: пало наших {r.DefenderFell.Count}, налётчиков {r.AttackerFell.Count}. Остальные идут дальше.", cell, enemy);
        return r;
    }
}
