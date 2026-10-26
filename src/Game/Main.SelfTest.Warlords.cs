using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The warlords' kinds on the screen: a goblin warlord's army comes in by a side of the valley, ours sheltering;
    /// his bar names his kind, one of his bands lies down in wait, and a click on one of it lying there picks nobody
    /// of it. Cleared away after.
    /// </summary>
    async Task SelfTestWarlords(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var sim = Sim;
        // ours indoors, so nobody walks by the ambush before it is looked at
        if (!sim.Alarm) sim.RaiseAlarm();
        var lord = sim.NewWarlord(2, Kinds.Goblin);
        sim.Raid(new List<UnitType> { Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Orc }, 1, .5, lord);
        var lurk = sim.Bands.Find(b => b.Lurk);
        for (int i = 0; i < 300 && !(lurk is { State: BandState.Lurk } && lurk.Members.Exists(u => u.Lurking)); i++) Advance(.5);
        Hud.Refresh();
        await Frames(4);
        string line = Hud.LordLine;
        var lying = lurk?.Members.Find(u => u.Lurking);
        Unit picked = null;
        if (lying != null)
        {
            Rig.Set(World.WX(lying.X), World.WZ(lying.Z), -.75f, .55f, 1.2f, snap: true);
            await Frames(3);
            picked = Command.UnitAt(Command.Screen(lying, .5f), pawnsFirst: false);
        }
        check("a goblin warlord's army come in: his bar names his kind; one of his bands lies down in wait, and a click on one of it lying there picks nobody of it",
            line != null && line.Contains("гоблин-военачальник") && lying != null && picked?.Band != lurk,
            $"the bar «{line}»; the band in wait {lurk?.Name ?? "none"} {lurk?.State}, lying {lurk?.Members.FindAll(u => u.Lurking).Count}; picked {picked?.Name ?? "nobody"}");
        // the raid gone at once, as if it had got out
        sim.Raids._queue.Clear();
        foreach (var u in sim.Units) if (!u.Dead && u.Team == Team.Horde) { u.Dead = true; u.DiedAt = -999; u.Lurking = false; }
        Advance(.5);
        sim.StandDown();
        Hud.Refresh();
        await Frames(3);
        if (Hud.DawnShown) Hud.CloseDawn();
        await Frames(2);
    }
}
