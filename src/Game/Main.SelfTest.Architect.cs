using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The architect through the game: the citadel sends one to the mason's yard to hire him; one in the household,
    /// holding a field west of the citadel where two sides of a stone ring stand already, looks it over, takes the
    /// standing walls into his line and marks its first pieces for the workers, none of them new wall along the old;
    /// chosen, his plan shows on the ground and his card has his switch; switched off he marks nothing more. Cleared
    /// away after.
    /// </summary>
    async Task SelfTestArchitect(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var sim = Sim;
        string where = sim.CantEnlist(Kinds.Architect) ?? "";
        int fx = Site.KeepX - Site.KeepR - 340, fz = Site.KeepZ + 120, R = 80;
        double stone0 = sim.Stock[Stockpile.Stone], wood0 = sim.Stock[Stockpile.Wood];
        sim.Stock[Stockpile.Stone] += 200 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] += 100 * VoxelWorld.PerM3;
        sim.HoldPoint = (fx, fz, 24);
        sim.ArchPlan = null;
        // the north and west sides of a ring the household built by hand
        var old = sim.RaiseWalls(new[] { (fx + R, fz - R), (fx - R, fz - R), (fx - R, fz + R) }, closed: false, 24, 8);
        int marked0 = sim.ArchMarked;
        var arch = sim.Spawn(Kinds.Architect, sim.NearestStand(fx, fz + 40, 2));
        // he looks the ground over a slice a step, then marks his first pieces
        for (int i = 0; i < 60 && sim.ArchPlan == null; i++) Advance(1);
        Advance(6);
        int marked = sim.ArchMarked - marked0;
        var plan = sim.ArchPlan;
        // what he planned new along the old sides: none of his new wall within two metres of them but where it meets them
        int along = 0;
        if (plan != null)
            foreach (var w in plan.Works)
            {
                if (w.Kind != ArchWorkKind.Wall || w.Stage != 0) continue;
                for (int i = 2; i + 3 < w.Line.Length; i += 2)
                {
                    int x = w.Line[i], z = w.Line[i + 1];
                    if (Math.Abs(z - (fz - R)) < 16 && x > fx - R + 24 && x < fx + R - 24 || Math.Abs(x - (fx - R)) < 16 && z > fz - R + 24 && z < fz + R - 24) along++;
                }
            }
        bool stands = plan?.Edge.Contains(1) == true, fresh = plan?.Edge.Contains(0) == true;
        Selected.Clear(); Selected.Add(arch);
        Hud.OnSelectionChanged();
        Rig.Focus(World.WX(fx), World.WZ(fz));
        int shown = 0;
        for (int i = 0; i < 120 && shown == 0; i++) { await Frames(1); shown = ArchView.Shown; }
        Command.ArchitectSelected();
        bool off = !arch.Commands;
        int before = sim.ArchMarked;
        Advance(30);
        check("the citadel sends one to the mason's yard for an architect; one in the household looks the point he holds over, takes the two sides standing there into his line with no new wall along them, marks its first pieces, his plan shows on the ground while he is chosen, and switched off he marks nothing more",
            where.Contains("Двор каменщиков") && plan != null && stands && fresh && along == 0 && marked > 0 && shown > 0 && off && sim.ArchMarked == before,
            $"«{where}»; plan {(plan == null ? "none" : $"{plan.Ring.Count} corners, reach {plan.H / 8.0:0} m, in {(plan.Stone ? "stone" : "timber")}: «{plan.Found}»")}, along what stands {stands}, new {fresh}, " +
            $"corners of new wall along the old sides {along}, marked {marked}, pieces in the ghost {shown}, off {off}, marked while off {sim.ArchMarked - before}");
        Command.ClearSelection();
        arch.Dead = true; arch.DiedAt = -999;
        int r = (sim.ArchPlan?.H ?? 100) + 120;
        sim.CancelArea(fx - r, fz - r, fx + r, fz + r);
        // the old sides taken down again, and forgotten
        for (int i = 0; i < old.Count; i++) { World.Set(old.X(i), old.Y(i), old.Z(i), Mat.Air); sim.Remember(old.X(i), old.Y(i), old.Z(i), Mat.Air); }
        sim.Nav.Flush();
        sim.ArchPlan = null; sim.HoldPoint = null;
        sim.Stock[Stockpile.Stone] = stone0; sim.Stock[Stockpile.Wood] = wood0;
        await Frames(2);
    }
}
