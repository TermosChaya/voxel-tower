using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The self-test's wounded (Sim.Carry.cs), out on the field west of the citadel: a swordsman with both legs broken
    /// and splinted, a worker chosen and sent to it with a right click through the orders' handler: it lifts it onto its
    /// shoulder, the panel says it carries it, and the splints are drawn.
    /// </summary>
    async Task SelfTestCarry(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var sim = Sim; var w = World;
        if (sim.Alarm) sim.StandDown();
        foreach (var r in sim.Units) if (r.Team == Team.Horde && !r.Dead) { r.Dead = true; r.DiedAt = -999; }
        // (the rest of ours not looking for the fallen meanwhile: this one is to be sent by the click)
        foreach (var o in sim.Units) if (o.Team == Team.Player) o.NextLookForFallen = sim.Time + 60;
        int cx = Site.KeepX - Site.KeepR - 180, cz = Site.KeepZ - 140;
        ClearTrees(cx - 50, cz - 50, cx + 50, cz + 50);
        var hurt = sim.Spawn(Kinds.Swordsman, sim.NearestStand(cx, cz, 4));
        sim.BreakBone(hurt, Break.LegR); sim.BreakBone(hurt, Break.LegL);
        sim.SplintBones(hurt);
        var carrier = sim.Spawn(Kinds.Worker, sim.NearestStand(cx + 30, cz + 10, 4));
        Advance(.3);
        Selected.Clear(); Selected.Add(carrier); Hud.OnSelectionChanged();
        Rig.Focus(w.WX(cx), w.WZ(cz));
        await Frames(30);
        string aid = Sim.AidFor(hurt);
        Command.OrderTo(Command.Screen(hurt, .2f));
        bool sent = carrier.Carrying == hurt;
        for (int i = 0; i < 60 && !Sim.OnShoulder(hurt); i++) { Advance(.25); await Frames(1); }
        bool up = Sim.OnShoulder(hurt);
        await Frames(3);
        string doing = Retinue.DoingNow(carrier, sim);
        int splints = Units.SplintsDrawn;
        check("a right click on one of ours down for good sends the chosen to carry it: lifted onto the shoulder, told in the panel, its splints drawn",
            aid == "вынести к цитадели" && sent && up && doing.StartsWith("несёт раненого") && splints >= 6,
            $"the hint «{aid}»; sent {sent}; on the shoulder {up}; «{doing}»; {splints} splints and bindings drawn");
        Selected.Clear(); Hud.OnSelectionChanged();
        hurt.Dead = carrier.Dead = true; hurt.DiedAt = carrier.DiedAt = -999;
        hurt.CarriedBy = null; carrier.Carrying = null; hurt.Aloft = false;
        await Frames(2);
    }
}
