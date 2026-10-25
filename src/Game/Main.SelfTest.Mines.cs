using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The self-test's mines: the line with the pick as a countermine, dragged out through its input handler on open ground
    /// far west of the citadel, marks a bore under the ground and the sim keeps it; a mine of the horde heard there shows
    /// its mark over the face and is heard. All of it taken away after.
    /// </summary>
    async Task MinesSelfTest(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var w = World; var sim = Sim;
        int ax = Site.KeepX - Site.KeepR - 200, az = Site.KeepZ - 120;
        ClearTrees(ax - 40, az - 40, ax + 120, az + 40);
        Rig.Focus(w.WX(ax + 40), w.WZ(az));
        await Frames(30);
        Vector2 Screen(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(w.WX(x + .5f), w.WY(w.SurfAt(x, z)), w.WZ(z + .5f)));
        void Move(Vector2 p) => Tools._UnhandledInput(new InputEventMouseMotion { Position = p });
        void Button(Vector2 p, bool down) => Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = p });
        Tools.SetTool(Tool.Line);
        Tools.SetDig(true);
        if (!Tools.Opt.Mine) Tools.ToggleForm();
        int mines0 = sim.Tunnels.Count, orders0 = sim.Orders.Total;
        var a = Screen(ax, az); var b = Screen(ax + 80, az);
        Move(a); await Frames(2); Button(a, true);
        for (int i = 1; i <= 8; i++) { Move(a.Lerp(b, i / 8f)); await Frames(1); }
        Button(b, false);
        await Frames(2);
        var ours = sim.Tunnels.Count > mines0 ? sim.Tunnels[^1] : null;
        bool under = false;
        if (ours != null)
        {
            // (its roof, the top of the cells it is dug through, a metre and more under the ground)
            under = (sim.Nav.Y(ours.Cells[ours.Cells.Count / 2]) + 1) * NavGrid.CS <= w.SurfAt(ax + 40, az) - NavGrid.CS;
        }
        check("the line with the pick as a countermine marks a bore under the ground, and the mine is kept",
            ours is { Team: Team.Player } && sim.Orders.Total > orders0 + 4000 && under,
            ours == null ? "no mine kept" : $"{ours.Cells.Count} steps, {sim.Orders.Total - orders0} voxels marked, under the ground {under}");
        if (Tools.Opt.Mine) Tools.ToggleForm();
        Tools.SetTool(Tool.None);
        sim.CancelArea(ax - 8, az - 8, ax + 88, az + 8);
        if (ours != null) sim.Tunnels.Remove(ours);

        // a mine of the horde heard: its mark over the face, and the picks heard
        var theirs = sim.DigMineNow(ax, az + 30, ax + 80, az + 30, 24, Team.Horde);
        var (fx, fyy, fz) = sim.Nav.Feet(theirs.Cells[^1]);
        theirs.Heard = true; theirs.MarkX = fx; theirs.MarkZ = fz;
        theirs.Dug = theirs.Cells.Count - 1;
        int played = Sound.Played;
        sim.PicksHeard?.Invoke(theirs, fx, w.SurfAt((int)fx, (int)fz), fz);
        await Frames(3);
        check("a mine of the horde heard shows a mark trembling on the ground over its face, and the picks are heard",
            Tunnels.Shown >= 1 && Sound.Played > played && Sound.Heard.Contains("knock"), $"{Tunnels.Shown} marks shown; heard {Sound.Heard.Contains("knock")}");
        sim.Tunnels.Remove(theirs);
        await Frames(2);
    }
}
