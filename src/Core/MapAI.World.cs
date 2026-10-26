using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The horde on the big map, a simple player of «Heroes» till a fuller one comes: each of its towns and lairs musters
/// at every dawn as on the small map; every other dawn it looks over what lies within its reach (thirty hexes, two days'
/// walk and a half; the first valley twice as far) — mines nobody
/// holds or ours, the dwellings of ours, the valleys of ours with a stronghold, the first valley always — and sends a
/// party to the one worth most for its way that it can beat three times in four, waiting to gather where the best it
/// could do is strike at ours and it nearly can. A town keeps two parties out at most.
/// </summary>
public sealed partial class Campaign
{
    /// <summary>How far a town of the horde looks for what to take, hexes.</summary>
    public const int HordeLook = 30;
    /// <summary>What is worth taking on the big map, gathered once a dawn.</summary>
    List<MapCell> _wide;

    void DecideWide(MapCell lair)
    {
        int key = KeyOf(lair);
        // (each town weighs every other dawn, the towns by turns: the ways of the big map are days long)
        if ((Clock.Day + key) % 2 != 0) return;
        // ours at the door: all home
        foreach (var p in Parties)
            if (p.Owner == Owner.Ours && Distance(p.Q, p.R, lair.Q, lair.R) <= 2)
            {
                foreach (var h in Parties.ToArray())
                    if (h.Owner == Owner.Horde && h.Den == key && h.Aim != PartyAim.Return && Distance(h.Q, h.R, lair.Q, lair.R) <= 4) GoHome(h);
                return;
            }
        int abroad = 0;
        foreach (var p in Parties) if (p.Owner == Owner.Horde && p.Den == key && p.Aim is PartyAim.Take or PartyAim.Strike) abroad++;
        if (abroad >= 2) return;
        var pool = new List<Traveller>(lair.Sum.Keep);
        int spare = pool.Count - (lair.Thing?.Kind == ThingKind.Lair ? LairKeep / 2 : LairKeep);
        if (spare < LeastParty) return;
        var lord = Realm.Lords.Count > 0 ? Realm.Lords[0] : null;
        // what is worth taking within reach, the nearest and the dearest first looked at closely
        var near = new List<(MapCell c, double score)>();
        // (what is worth taking gathered once a dawn for all the towns)
        _wide ??= Cells.FindAll(c => c.Owner != Owner.Horde && WorthWide(c) > 0);
        foreach (var c in _wide)
        {
            if (c == lair || c.Owner == Owner.Horde) continue;
            double worth = WorthWide(c);
            if (worth <= 0) continue;
            int d = Distance(lair.Q, lair.R, c.Q, c.R);
            // (the first valley it looks for twice as far)
            if (d > (c.Home ? 2 * HordeLook : HordeLook)) continue;
            if (Parties.Exists(p => p.Owner == Owner.Horde && p.GoalQ == c.Q && p.GoalR == c.R && p.Aim is PartyAim.Take or PartyAim.Strike)) continue;
            near.Add((c, worth - d / 10.0 * DayCost));
        }
        near.Sort((a, b) => b.score.CompareTo(a.score));
        Target best = null, most = null;
        // one search out of the town for all its targets: two days' walk and a half
        int stamp = MapWalk.Spread(this, lair, Owner.Horde, 2 * MapWalk.Moves + MapWalk.Moves / 2);
        var ways = new List<(MapCell c, List<MapCell> way)>();
        foreach (var (c, _) in near)
        {
            if (ways.Count >= 4) break;
            if (MapWalk.SpreadWay(this, stamp, c) is { } w) ways.Add((c, w));
        }
        foreach (var (c, way) in ways)
        {
            var t = new Target { Cell = c, Way = way };
            t.Defence = Defenders(c, out t.Walls);
            t.Score = WorthWide(c) - way.Count / 10.0 * DayCost;
            t.Party = Muster(pool, spare, lord, t);
            if (most == null || t.Score > most.Score) most = t;
            if (t.Party != null && (best == null || t.Score > best.Score)) best = t;
        }
        if (most == null) return;
        if (most != best && most.Cell.Owner == Owner.Ours && (best == null || most.Score > best.Score + 1) && Strength(pool, spare, lord) >= most.Need * .6f) return;
        if (best == null) return;
        Send(lair, best, lord);
    }

    /// <summary>What a hex of the big map is worth to the horde: the first valley most, a valley of ours, a rare mine, a mine, a dwelling.</summary>
    static double WorthWide(MapCell c)
    {
        if (c.Home) return 8;
        if (c.Owner == Owner.Ours && c.Sum.Raised > 0) return 6;
        if (c.Thing is { Kind: ThingKind.Mine } m) return m.Res is Res.Gold or >= Res.Mercury ? 5 : 4;
        if (c.Thing is { Kind: ThingKind.Dwelling, Owner: Owner.Ours }) return 2.5;
        return 0;
    }
}
