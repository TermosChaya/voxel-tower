using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>
/// Holding a point of the map (docs/handoff/13-world-map.md, holding a point). A mine flagged, or any cell, is the realm's
/// only as long as someone holds it: ours left standing in its valley as we leave it are its garrison and the cell is
/// ours (<see cref="Hold"/>), its mine's stone, logs and metal going to its own stores to build with. A cell of ours with
/// nobody in it to fight is taken by the first party of the horde that comes, without a battle. One with a garrison is
/// fought for: while the game is played (<see cref="Interactive"/>) the map stops — no party walks, no clock runs — and
/// the game opens the valley, where the party comes in as a raid and the battle is fought out in the voxels till one
/// side has nobody left to fight (<see cref="TakeDefence"/>, <see cref="DefenceOver"/>); counted where nobody plays (the
/// harness, the bench) it is a quick battle behind the works the valley was measured to have (Fortification.cs), the
/// garrison and every party of ours standing there fighting it together. The horde weighs a point by the same odds
/// (<see cref="OddsOn"/>).
/// </summary>
public sealed partial class Campaign
{
    /// <summary>The game is played: a party of the horde at a point of ours with a garrison stops the map for a battle in its valley.</summary>
    public bool Interactive;
    /// <summary>The point whose battle stops the map (the map does not run till it is over); null while the map runs.</summary>
    public MapCell Frozen;
    /// <summary>The battles for points of ours waiting for the game to fight them: the party and where.</summary>
    readonly List<(MapParty p, MapCell cell)> _defenceDue = new();

    /// <summary>All of ours who would fight for a cell: its garrison and every party of ours standing in it.</summary>
    public List<Traveller> DefendersOf(MapCell cell)
    {
        var all = new List<Traveller>();
        if (cell.Owner == Owner.Ours) all.AddRange(cell.Sum.Keep);
        foreach (var p in Parties)
            if (p.Owner == Owner.Ours && p.Q == cell.Q && p.R == cell.R && !p.Moving) all.AddRange(p.Party.Members);
        return all;
    }

    /// <summary>One of a garrison who fights for it: anyone but a worker or a priest.</summary>
    public static bool Fights(Traveller m) => m.Kind.Work < 1 && !m.Kind.Prays && m.Kind != Kinds.Risen;

    /// <summary>Whether anyone of ours would fight for a cell.</summary>
    public bool Garrisoned(MapCell cell) => DefendersOf(cell).Exists(Fights);

    /// <summary>
    /// The chance a party of the horde (and its warlord) takes a cell of ours, before it is fought: against all of ours
    /// who would fight for it, behind its works. The horde's choices weigh points by it (MapAI); none to fight, it is 1.
    /// </summary>
    public float OddsOn(MapCell cell, IReadOnlyList<Traveller> attackers, Warlord lord)
    {
        var def = DefendersOf(cell);
        if (!def.Exists(Fights)) return 1;
        return QuickBattle.Chance(attackers, lord, def, null, cell.Owner == Owner.Ours ? cell.Sum.Walls : 0, cell.Kind);
    }

    /// <summary>
    /// A valley nobody holds, left with ours standing in it and no enemy: they are its garrison, the cell ours, its mine
    /// flagged ours, its works measured. Said.
    /// </summary>
    internal void Hold(MapCell cell, Sim sim)
    {
        cell.Owner = Owner.Ours;
        _held = null;
        cell.Sum.Take(sim, Team.Player, KeyOf(cell));
        if (cell.Thing is { Kind: ThingKind.Mine or ThingKind.Dwelling } t) t.Owner = Owner.Ours;
        Tell(NewsKind.Taken, $"{Cap(Names(cell).nom)} под охраной: {Garrison(cell)}.", cell, null);
        HexChanged(cell);
    }

    /// <summary>Whether ours hold a valley as we leave it: someone of ours in it and no enemy standing (the horde's, nor the beasts that keep it).</summary>
    internal static bool HeldBy(Sim sim)
    {
        bool ours = false;
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.Bound || u.Captor != null) continue;
            if (u.Team == Team.Player && u.T != Kinds.Risen) ours = true;
            else if (u.Team == Team.Horde && !u.Envoy && !u.Routed) return false;
            else if (u.Team == Team.Wild && u.Guard) return false;
        }
        return ours;
    }

    /// <summary>
    /// Some of a party of ours left to hold the cell it stands in (on the map, as «Оставить гарнизон» does in a valley):
    /// they are its garrison, the cell ours, its mine flagged ours. The works it has stay as they were measured.
    /// </summary>
    public void Garrison(MapParty p, MapCell cell, IEnumerable<Traveller> stay)
    {
        foreach (var m in new List<Traveller>(stay))
            if (p.Party.Members.Remove(m)) cell.Sum.Keep.Add(m);
        if (cell.Owner != Owner.Ours) { cell.Owner = Owner.Ours; _held = null; }
        if (cell.Thing is { Kind: ThingKind.Mine or ThingKind.Dwelling } t) t.Owner = Owner.Ours;
        if (p.Party.Members.Count == 0) Parties.Remove(p);
        Tell(NewsKind.Taken, $"{Cap(Names(cell).nom)} под охраной: {Garrison(cell)}.", cell, p);
        HexChanged(cell);
    }

    /// <summary>A garrison said: «гарнизон 6, стена кругом, 3 м · 2 башни».</summary>
    public string Garrison(MapCell cell)
    {
        int n = cell.Sum.Keep.Count;
        return n == 0 ? "гарнизона нет" : $"гарнизон {n}, {cell.Sum.Works.Words()}";
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    /// <summary>
    /// A party of the horde at a cell of ours we are not in: with nobody there to fight, it takes it; with a garrison
    /// (or a party of ours standing there), while the game is played the map stops and the game fights it out in the
    /// valley; counted, a quick battle behind the works.
    /// </summary>
    void Defend(MapParty p, MapCell cell)
    {
        if (!Garrisoned(cell))
        {
            // the workers and the like there run
            var keep = cell.Sum.Keep;
            if (keep.Count > 0)
            {
                var run = new MapParty { Id = _nextParty++, Owner = Owner.Ours, Party = new Party(), Q = cell.Q, R = cell.R, CameQ = cell.Q, CameR = cell.R };
                run.Party.Members.AddRange(keep);
                keep.Clear();
                Parties.Add(run);
                Flee(run, cell);
            }
            Fall(cell);
            Tell(NewsKind.Lost, $"Орда заняла {Names(cell).acc} без боя: там не было гарнизона.", cell, p);
            Occupy(p, cell, said: false);
            return;
        }
        if (Interactive)
        {
            // (it stops where it stands: the game takes it off the map into the valley)
            p.Path?.Clear(); p.Trail = null; p.Progress = 0; p.StepCost = 0; p.Waits = true;
            _defenceDue.Add((p, cell));
            Frozen = cell;
            Tell(NewsKind.RaidComing, $"Орда напала на {Names(cell).acc}: {Raiders(p.Party.Members.Count)}{Led(p)}. Бой в долине.", cell, p);
            return;
        }
        Assault(p, cell);
    }

    /// <summary>
    /// A battle for a point of ours the game is to fight: the party of the horde, the cell (the scene opens on it), the
    /// side it comes in by. Off the map now; what is left of it after comes back by <see cref="RaidOver"/>.
    /// </summary>
    public bool TakeDefence(out MapParty enemy, out MapCell cell, out int side)
    {
        enemy = null; cell = null; side = -1;
        if (_defenceDue.Count == 0) return false;
        (enemy, cell) = _defenceDue[0];
        _defenceDue.RemoveAt(0);
        Parties.Remove(enemy);
        var came = At(enemy.CameQ, enemy.CameR);
        side = came != null && came != cell ? MapPath.Side(cell, came) : -1;
        if (side < 0) side = came != null && came != cell ? SideToward(cell, came.Q, came.R) : cell.EntrySide;
        return true;
    }

    /// <summary>A battle for a point of ours waits to be fought (the game is to open its valley).</summary>
    public bool DefenceWaits => _defenceDue.Count > 0;

    /// <summary>
    /// The battle for a point fought out in its valley: the map runs again. Held, the point is still ours with what is
    /// left of its garrison; lost, the horde holds it (the valley's fall said by the game). Said.
    /// </summary>
    public void DefenceOver(MapCell cell, bool held, int oursFell, int theirsFell)
    {
        if (Frozen == cell) Frozen = null;
        Tell(held ? NewsKind.Fought : NewsKind.Lost, held
            ? $"Бой {Names(cell).prep} выигран. Пало наших {oursFell}, налётчиков {theirsFell}."
            : $"Бой {Names(cell).prep} проигран, долина за ордой. Пало наших {oursFell}, налётчиков {theirsFell}.", cell, null);
        HexChanged(cell);
    }

    void WriteHold(BinaryWriter w)
    {
        w.Write(Frozen != null ? KeyOf(Frozen) : -1);
        w.Write(_defenceDue.Count);
        foreach (var (p, cell) in _defenceDue) { w.Write(KeyOf(cell)); w.Write(p.Q); w.Write(p.R); p.Party.Write(w); WriteWay(w, p); }
    }

    void ReadHold(BinaryReader r)
    {
        Frozen = CellOfKey(r.ReadInt32());
        _defenceDue.Clear();
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var cell = CellOfKey(r.ReadInt32());
            var p = new MapParty { Q = r.ReadInt32(), R = r.ReadInt32(), Party = Party.Read(r) };
            ReadWay(r, p);
            if (cell != null) _defenceDue.Add((p, cell));
        }
    }
}
