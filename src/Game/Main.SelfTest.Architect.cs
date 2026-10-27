using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The architect through the game: the citadel sends one to the mason's yard to hire him; one in the household,
    /// holding a field west of the citadel, plans a ring round it and marks its first pieces for the workers; chosen, his
    /// plan shows as a ghost on the ground and his card has his switch; switched off he marks nothing more. Cleared away after.
    /// </summary>
    async Task SelfTestArchitect(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var sim = Sim;
        string where = sim.CantEnlist(Kinds.Architect) ?? "";
        int fx = Site.KeepX - Site.KeepR - 340, fz = Site.KeepZ + 120;
        double stone0 = sim.Stock[Stockpile.Stone], wood0 = sim.Stock[Stockpile.Wood];
        sim.Stock[Stockpile.Stone] += 200 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] += 100 * VoxelWorld.PerM3;
        sim.HoldPoint = (fx, fz, 24);
        sim.ArchPlan = null;
        int marked0 = sim.ArchMarked;
        var arch = sim.Spawn(Kinds.Architect, sim.NearestStand(fx, fz + 40, 2));
        Advance(10);
        int marked = sim.ArchMarked - marked0;
        Selected.Clear(); Selected.Add(arch);
        Hud.OnSelectionChanged();
        Rig.Focus(World.WX(fx), World.WZ(fz));
        int shown = 0;
        for (int i = 0; i < 120 && shown == 0; i++) { await Frames(1); shown = ArchView.Shown; }
        Command.ArchitectSelected();
        bool off = !arch.Commands;
        int before = sim.ArchMarked;
        Advance(30);
        check("the citadel sends one to the mason's yard for an architect; one in the household plans a ring round the point he holds and marks its first pieces, his plan shows as a ghost while he is chosen, and switched off he marks nothing more",
            where.Contains("Двор каменщиков") && sim.ArchPlan != null && marked > 0 && shown > 0 && off && sim.ArchMarked == before,
            $"«{where}»; plan {(sim.ArchPlan == null ? "none" : $"{2 * sim.ArchPlan.H / 8.0:0} m square in {(sim.ArchPlan.Stone ? "stone" : "timber")}")}, marked {marked}, pieces in the ghost {shown}, off {off}, marked while off {sim.ArchMarked - before}");
        Command.ClearSelection();
        arch.Dead = true; arch.DiedAt = -999;
        int r = (sim.ArchPlan?.H ?? 100) + 120;
        sim.CancelArea(fx - r, fz - r, fx + r, fz + r);
        sim.ArchPlan = null; sim.HoldPoint = null;
        sim.Stock[Stockpile.Stone] = stone0; sim.Stock[Stockpile.Wood] = wood0;
        await Frames(2);
    }
}
