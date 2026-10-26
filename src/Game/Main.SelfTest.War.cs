using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The war on the screen: an army with a warlord at its head comes in by a side of the valley, and his bar under the
    /// faces names him; the horde's panel tells his bands' spirit; killed, his army runs, and once the raid is over the
    /// card after it tells how it went, the warlord fallen among it, and is closed with its button.
    /// </summary>
    async Task SelfTestWar(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var sim = Sim;
        if (sim.Alarm) sim.StandDown();
        var lord = sim.NewWarlord(2);
        sim.Raid(new List<UnitType> { Kinds.Goblin, Kinds.Goblin, Kinds.Orc }, 1, .5, lord);
        Unit at = null;
        for (int i = 0; i < 60 && at == null; i++) { Advance(.5); foreach (var u in sim.Units) if (u.Lord == lord && !u.Dead) at = u; }
        Hud.Refresh();
        await Frames(4);
        string shown = Hud.LordShown;
        Hud.OpenPanel("horde");
        await Frames(3);
        string horde = Hud.HordeSaid;
        bool panel = horde.Contains("военачальник " + lord.Name) && horde.Contains("дух ");
        Hud.ClosePanel();
        check("a warlord's army come in: his bar under the faces names him, the horde's panel tells who leads and his bands' spirit",
            at != null && shown == lord.Name && panel, $"the warlord {(at == null ? "never came" : "came")}; the bar shows {shown ?? "nobody"}; the panel {(panel ? "tells" : "does not tell")}");
        if (at == null) return;
        sim.Hurt(at, null, 1e6f, "test");
        // his army runs; the rest of the raid gone at once, as if it had got out
        sim.Raids._queue.Clear();
        foreach (var u in sim.Units) if (!u.Dead && u.Team == Team.Horde) { u.Dead = true; u.DiedAt = -999; }
        Advance(.5);
        Hud.Refresh();
        await Frames(4);
        string said = Hud.DawnSaid;
        check("the raid over, the card after it tells how it went: who came and fell, the warlord fallen",
            (said.Contains("ВОЛНА") || said.Contains("НАБЕГ")) && said.Contains("Орда: пришло") && said.Contains($"военачальник {lord.Name}, уровень 2: пал") && Hud.LordShown == null,
            $"the card: «{said.Replace("\n", " / ")}»; the bar {Hud.LordShown ?? "gone"}");
        Hud.CloseDawn();
        await Frames(2);
        check("…and is closed with its button", !Hud.DawnShown, $"still shown {Hud.DawnShown}");
    }
}
