using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The self-test's town life (Sim.Life.cs, Sim.Voices.cs, UnitView.Life.cs), on a clearing west of the citadel: a
    /// worker sent to sleep by a campfire lies down on its back there, and is drawn lying; one eating sits with a bowl
    /// drawn in its hand; two passing greet each other, the greeting's word shown over the one who says it and heard; a
    /// ring of three talks.
    /// </summary>
    async Task SelfTestLife(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var sim = Sim; var w = World;
        if (sim.Alarm) sim.StandDown();
        foreach (var r in sim.Units) if (r.Team == Team.Horde && !r.Dead) { r.Dead = true; r.DiedAt = -999; }
        int cx = Site.KeepX - Site.KeepR - 160, cz = Site.KeepZ + 120;
        ClearTrees(cx - 60, cz - 60, cx + 60, cz + 60);
        var sleeper = sim.Spawn(Kinds.Worker, sim.NearestStand(cx, cz, 4));
        var eater = sim.Spawn(Kinds.Worker, sim.NearestStand(cx + 20, cz, 4));
        var a = sim.Spawn(Kinds.Worker, sim.NearestStand(cx - 20, cz + 20, 4));
        var b = sim.Spawn(Kinds.Archer, sim.NearestStand(cx - 4, cz + 20, 4));
        Rig.Focus(w.WX(cx), w.WZ(cz));
        Advance(.3);
        // asleep where it stands (no bunk, no fire near): on its back
        sim.ToBed(sleeper);
        sim.InBed(sleeper);
        sim.TakeUp(eater, Pastime.Eat);
        // (at its meal and standing still at it: a step aside to a free cell is no meal yet)
        for (int i = 0; i < 80 && (eater.Pastime != Pastime.Eat || eater.State != UnitState.Idle || eater.Path != null && eater.PathI < eater.Path.Count); i++) { Advance(.25); await Frames(1); }
        eater.PastimeUntil = sim.Time + 60;
        // (the sleeper's staged bed, InBed, holds till the scene ends: given a minute instead, it was woken at once by the
        // day wherever the run before had left the clock by day, and the check found nobody asleep)
        // (lying down takes the view about a second and a half of its frames' time: waited for by the clock, up to 6 s,
        // not by a count of frames, which at 120 frames a second gave it 0.75 s and it was drawn lying only by halves)
        var lyingDown = System.Diagnostics.Stopwatch.StartNew();
        while (lyingDown.Elapsed.TotalSeconds < 6 && (!Units.Lying(sleeper.Id) || Units.PropsDrawn == 0)) await Frames(1);
        bool lying = Units.Lying(sleeper.Id), bowl = Units.PropsDrawn > 0;
        check("one asleep lies on its back and is drawn lying; one eating sits with a bowl drawn in its hand",
            sleeper.Pastime == Pastime.Sleep && lying && eater.Pastime == Pastime.Eat && bowl,
            $"the sleeper {sleeper.Pastime}, drawn lying {lying} after {lyingDown.Elapsed.TotalSeconds:0.0} s{(Paused ? ", the game paused" : "")}; the eater {eater.Pastime} {eater.State} at {eater.X:0},{eater.Z:0}, things in hands drawn {Units.PropsDrawn}");
        // a greeting: the word over the one who says it, and heard
        bool wasHeard = Sound.Heard.Contains("greet");
        a.GreetAt = b.GreetAt = sim.Time; a.GreetTo = b; b.GreetTo = a;
        sim.SayAloud(a, "Здорово!", Voice.Greet);
        await Frames(4);
        bool shown = Shouts.Showing.Contains("Здорово!");
        check("two of ours greet each other: the word over the one who says it, and its voice heard",
            shown && Sound.Heard.Contains("greet"),
            $"shown {shown} ({string.Join(", ", Shouts.Showing)}), heard {Sound.Heard.Contains("greet")} (before {wasHeard})");
        foreach (var u in new[] { sleeper, eater, a, b }) { u.Dead = true; u.DiedAt = -999; }
        await Frames(2);
    }
}
