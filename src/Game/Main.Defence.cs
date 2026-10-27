using System;
using System.Collections.Generic;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

/// <summary>
/// The battle for a point of ours (Campaign.Hold.cs): a party of the horde come to a cell of ours with a garrison stops
/// the map, and the game goes there — the scene begun again on that valley, the party coming in as a raid by the side
/// it came, the battle fought out in the voxels till one side has nobody left to fight in it (ours do not run; theirs
/// may). Then the outcome is the map's — held with what is left, or the horde's — the valley's day set back to the
/// map's time (the battle took none of it), and the map runs on.
/// </summary>
public partial class Main
{
    /// <summary>The battle the scene is begun again on: the horde's party and the side it comes in by. Kept while the scene begins again.</summary>
    static (MapParty enemy, int side)? _defending;
    /// <summary>The self-test lets a battle for a point stop the map (scripted runs count it at once otherwise).</summary>
    static bool _selfDefence;
    /// <summary>The point whose battle is being fought in this valley; how many fought for it and came against it.</summary>
    MapCell _defence;
    int _defOurs0, _defTheirs0, _escapedThen;

    /// <summary>Whether the map stops for battles for our points here: in play, not in the scripted runs (pictures, the harness's films, tests).</summary>
    bool DefenceInteractive => !Scripted || _selfDefence;

    /// <summary>Those of ours in the valley who fight for it.</summary>
    int OurFighters()
    {
        int n = 0;
        foreach (var u in Sim.Units) if (u.Team == Team.Player && Fortification.Fighter(u) && !u.Down) n++;
        return n;
    }

    /// <summary>
    /// A battle for a point of ours waiting: in this valley it starts at once; elsewhere the game goes there (the scene
    /// begun again on it). True if the scene is to begin again.
    /// </summary>
    bool DefenceDue()
    {
        if (Atlas == null || !Atlas.TakeDefence(out var enemy, out var cell, out int side)) return false;
        if (cell == Atlas.Here) { DefenceBegins(enemy, side); return false; }
        _defending = (enemy, side);
        if (FP.On) FP.Exit();
        if (AtlasOpen) CloseAtlas();
        GoToValley(cell);
        return true;
    }

    /// <summary>The scene begun on a point fought for: the party brought in, or (read back mid-battle) the battle still on.</summary>
    void DefenceStart()
    {
        if (_defending is { } d)
        {
            _defending = null;
            DefenceBegins(d.enemy, d.side);
        }
        else if (Atlas.Frozen == Atlas.Here) { _defence = Atlas.Here; _defOurs0 = OurFighters(); _defTheirs0 = 0; }
    }

    void DefenceBegins(MapParty enemy, int side)
    {
        _defence = Atlas.Here;
        _defOurs0 = OurFighters();
        _defTheirs0 = enemy.Party.Members.Count + (enemy.Lord != null ? 1 : 0);
        _escapedThen = 0;
        Sim.Raid(enemy.Roster(), side, lord: enemy.Lord);
        _raiding.Add(enemy);
        if (!Sim.Alarm) Sim.RaiseAlarm();
        Paused = false;
        Hud.Log($"Орда напала на {Campaign.Names(_defence).acc}: {Campaign.Raiders(enemy.Party.Members.Count)}. Карта ждёт, пока идёт бой.", Hud.Kind.Bad);
        Hud.Float("бой за точку", Ornament.Blood);
    }

    /// <summary>
    /// On each frame of a battle for a point: with nobody of ours left to fight in a valley without a citadel, it is lost;
    /// the raid over, the battle is: its outcome the map's, the valley's day the map's, the map running on.
    /// </summary>
    void DefenceTick()
    {
        if (_defence == null || _defence != Atlas.Here) return;
        if (Sim.Raids.Active && !Sim.Lost && OurFighters() == 0) Sim.Overrun();
        if (Sim.Raids.Active || _raiding.Count > 0) return;
        int theirs = 0;
        foreach (var u in Sim.Units) if (!u.Dead && u.Team == Team.Horde && !u.Envoy && !u.Bound) theirs++;
        bool held = !Sim.Lost;
        int oursFell = Math.Max(0, _defOurs0 - OurFighters()), theirsFell = Math.Max(0, _defTheirs0 - theirs - _escapedThen);
        var cell = _defence;
        _defence = null;
        // the battle took none of the map's time: the valley's day back to it
        Sim.SetClock(Atlas.Clock);
        Atlas.DefenceOver(cell, held, oursFell, theirsFell);
        Hud.Log(held ? $"{Campaign.Names(cell).nom}: бой выигран. Пало наших {oursFell}, налётчиков {theirsFell}. Карта снова идёт."
            : $"{Campaign.Names(cell).nom}: бой проигран, долина за ордой. Карта снова идёт.", held ? Hud.Kind.Ok : Hud.Kind.Bad);
        Hud.Float(held ? "точка удержана" : "точка потеряна", held ? Ornament.EmberGlow : Ornament.Blood);
        RefreshLand(cell);
        MapsChanged();
    }
}
