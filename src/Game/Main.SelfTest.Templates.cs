using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The «Шаблоны» tool through its input, on a field evened out west of the citadel: its key takes it up with the
    /// round tower in hand; {Form} goes on through the library, {Rotate} turns, the height keys raise it; a click puts
    /// the tower up for the workers and Ctrl+Z takes it back; a stretch of wall drawn from near the tower starts at its
    /// middle; a template the settlement has not grown to is refused, and grown to it, with Shift a drag sets a row of
    /// watchtowers; built by many hands the tower stands whole with its battlements. Cleared away after.
    /// </summary>
    async Task SelfTestTemplates(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var w = World; var sim = Sim;
        Vector2 Screen(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(w.WX(x + .5f), w.WY(w.SurfAt(x, z)), w.WZ(z + .5f)));
        void Move(Vector2 p) => Tools._UnhandledInput(new InputEventMouseMotion { Position = p });
        void Button(Vector2 p, bool down, bool shift = false) => Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = p, ShiftPressed = shift });
        void Press(Key k, bool ctrl = false) => Tools._UnhandledInput(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true, CtrlPressed = ctrl });
        string said = "";
        async Task Drag(int ax, int az, int bx, int bz, bool shift = false)
        {
            var a = Screen(ax, az); var b = Screen(bx, bz);
            Move(a); await Frames(2);
            Button(a, true, shift);
            for (int i = 1; i <= 8; i++) { Move(a.Lerp(b, i / 8f)); await Frames(1); }
            await GhostCaughtUp();
            said = Tools.PlanText ?? "";
            Button(b, false, shift);
            await Frames(2);
        }
        async Task Click(int x, int z) { var p = Screen(x, z); Move(p); await GhostCaughtUp(); said = Tools.PlanText ?? ""; Button(p, true); Button(p, false); await Frames(2); }
        int Builds() => sim.Orders.BuildTotal;

        // a field of level ground, bare of trees
        int fx = Site.KeepX - Site.KeepR - 420, fz = Site.KeepZ - 120, g = w.SurfAt(fx, fz);
        ClearTrees(fx - 110, fz - 70, fx + 110, fz + 70);
        for (int z = fz - 64; z <= fz + 64; z++)
            for (int x = fx - 100; x <= fx + 100; x++)
                for (int y = g - 6; y < g + 64; y++)
                {
                    byte want = y < g ? (y == g - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
        sim.CancelArea(fx - 100, fz - 64, fx + 100, fz + 64);
        if (sim.Alarm) sim.StandDown();
        sim.Stock[Stockpile.Stone] = Math.Max(sim.Stock[Stockpile.Stone], 400.0 * VoxelWorld.PerM3);
        sim.Stock[Stockpile.Wood] = Math.Max(sim.Stock[Stockpile.Wood], 200.0 * VoxelWorld.PerM3);
        Rig.SetAt(w.WX(fx), w.WY(g), w.WZ(fz), -.66f, .6f, 2.2f);
        await Frames(6);

        // its key takes it up; {Form} goes on, {Rotate} turns, the height key raises
        Tools.SetTool(Tool.None);
        Press(Bindings.KeyOf(Act.ToolTemplate));
        bool taken = Tools.Tool == Tool.Template;
        Tools.PickTemplate(0);
        Press(Bindings.KeyOf(Act.Form));
        bool next = Tools.Opt.TemplatePick == 1;
        Tools.PickTemplate(0);
        Press(Bindings.KeyOf(Act.Rotate));
        bool turned = Tools.Opt.TemplateRot == 1;
        Press(Bindings.KeyOf(Act.HeightMore));
        bool raised = Tools.TemplateSize(Sim.Templates[0]) == Sim.Templates[0].Size + 8;
        Press(Bindings.KeyOf(Act.HeightLess));
        check("the templates' key takes up the library; {Form} goes on to the next, {Rotate} turns, the height keys raise and lower",
            taken && next && turned && raised && Tools.TemplateSize(Sim.Templates[0]) == Sim.Templates[0].Size,
            $"taken {taken}, next {next}, turned {turned}, raised {raised}");

        // a click puts the round tower up for the workers, Ctrl+Z takes it back, and a click again puts it up
        int b0 = Builds();
        await Click(fx - 60, fz);
        int tower = Builds() - b0;
        string towerSaid = said;
        Press(Key.Z, ctrl: true);
        int undone = Builds() - b0;
        await Click(fx - 60, fz);
        check("a click puts the round tower up for the workers, its ghost saying what it costs; Ctrl+Z takes it back",
            tower > 3000 && towerSaid.Contains("Круглая башня") && towerSaid.Contains("м³") && undone == 0 && Builds() - b0 == tower,
            $"{tower} voxels marked («{towerSaid}»); after Ctrl+Z {undone}; again {Builds() - b0}");

        // a stretch of wall drawn from near the tower starts at the tower's middle
        Tools.PickTemplate(Array.FindIndex(Sim.Templates, t => t.Key == "wall"));
        int b1 = Builds();
        await Drag(fx - 60 + 30, fz + 4, fx + 40, fz + 4);
        int wall = Builds() - b1;
        // drawn from 3 m off the tower's rim, it runs on from the tower's middle: marked between the rim and where the
        // drag began, and over the walk for its merlons
        bool snapped = sim.Orders.At(fx - 33, g + 8, fz + 2) != 0;
        int merlons = 0;
        for (int x = fx - 30; x < fx + 40; x++) for (int z = fz - 6; z < fz + 10; z++) if (sim.Orders.At(x, g + Sim.TemplateOf("wall").Size + 2, z) != 0) merlons++;
        check("a stretch of wall drawn from near a tower set before starts at the tower's middle, with its merlons",
            wall > 2000 && said.Contains("Участок стены") && snapped && merlons > 40,
            $"{wall} voxels marked («{said}»); between the rim and the drag's start {snapped}; {merlons} voxels of merlons");

        // a template the settlement has not grown to yet is refused; grown to it, with Shift a drag sets a row of them
        var watch = Sim.TemplateOf("watchtower");
        Tools.PickTemplate(Array.IndexOf(Sim.Templates, watch));
        int standing = sim.Standing;
        sim.Standing = 0;
        int b2 = Builds();
        await Drag(fx - 80, fz + 44, fx + 60, fz + 44, shift: true);
        int refused = Builds() - b2;
        string shutSaid = said;
        sim.Standing = 1;
        await Drag(fx - 80, fz + 44, fx + 60, fz + 44, shift: true);
        int row = Builds() - b2;
        var one = watch.Plan(w, Site, fx - 80, fz + 44, 0, 0, 1, 0);
        check("a template the settlement has not grown to yet is refused and says when it opens; grown to it, with Shift a drag sets a row of them a few metres apart",
            refused == 0 && shutSaid.Contains("откроется на уровне «Деревня»") && one.Voxels != null && row > one.Voxels.Count * 2 && said.Contains("Дозорная вышка ×"),
            $"refused: {refused} voxels marked («{shutSaid}»); then {row}, one {one.Voxels?.Count} («{said}»)");

        // built by many hands, the tower stands whole with its battlements
        sim.InstantLabour = true;
        for (int i = 0; i < 6; i++) { Advance(1); sim.CompleteAll(); }
        // (the highest of the rim on its western half, away from the stair the turn sent east and from the wall)
        int top = 0, mid = w.SurfAt(fx - 60, fz);
        for (int a = 90; a <= 270; a += 6)
            top = Math.Max(top, w.SurfAt(fx - 60 + (int)Math.Round(18 * Math.Cos(a * Math.PI / 180)), fz + (int)Math.Round(18 * Math.Sin(a * Math.PI / 180))));
        check("built, the round tower stands whole, its battlements up round the rim over its top",
            mid >= g + 24 && top >= mid + 4, $"its top {mid - g} voxels over the ground, the rim {top - g}");
        sim.InstantLabour = false;

        sim.Standing = standing;
        Tools.SetTool(Tool.None);
        sim.CancelArea(fx - 100, fz - 64, fx + 100, fz + 64);
        for (int z = fz - 64; z <= fz + 64; z++)
            for (int x = fx - 100; x <= fx + 100; x++)
                for (int y = g; y < g + 64; y++)
                    if (w.Get(x, y, z) != Mat.Air) w.Set(x, y, z, Mat.Air);
        Advance(1);
        await Frames(2);
    }
}
