using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The self-test's magic wand, driven through its input handler: on a stone wall with a walk of logs on it far west
    /// of the citadel, the cursor on the stone lights all of the stone and none of the logs; a click with the pick marks
    /// it to come down, Ctrl+Z takes that back; with earth in hand a click on the walk marks it to be laid again in
    /// earth; in its third way a click keeps the stone as a blueprint of stone alone. Cleared away after.
    /// </summary>
    async Task SelfTestWand(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Move(Vector2 p) => Tools._UnhandledInput(new InputEventMouseMotion { Position = p });
        async Task Click(Vector2 p)
        {
            Move(p); await Frames(3);
            Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = p });
            Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = p });
            await Frames(2);
        }
        var w = World; var sim = Sim;
        int wx = Site.KeepX - Site.KeepR - 300, kz = Site.KeepZ - 60;
        ClearTrees(wx - 40, kz - 40, wx + 40, kz + 40);
        int top = Shapes.LevelTop(w, wx, kz, 24);
        sim.InstantLabour = true;
        var wall = Shapes.Line(w, Site, wx, kz - 24, wx, kz + 24, new Stroke { Mat = Mat.Stone, Width = 8, Height = 24, Level = true }, top);
        sim.DesignateBuild(wall);
        int walk = Shapes.LevelTop(w, wx, kz, 4);
        var walkway = Shapes.Line(w, Site, wx, kz - 24, wx, kz + 24, new Stroke { Mat = Mat.Wood, Width = 8, Height = 4, Level = true }, walk);
        sim.DesignateBuild(walkway);
        sim.InstantLabour = false;
        // what the two strokes laid (not some stray stone of the tests before that the wall does not touch)
        int stone = 0, wood = 0, x0 = wx;
        for (int i = 0; i < wall.Count; i++) if (Mat.IsMasonry(w.Get(wall.X(i), wall.Y(i), wall.Z(i)))) { stone++; x0 = Math.Min(x0, wall.X(i)); }
        for (int i = 0; i < walkway.Count; i++) if (w.Get(walkway.X(i), walkway.Y(i), walkway.Z(i)) == Mat.Wood) wood++;
        Rig.SetAt(w.WX(wx - 40), w.WY(top - 8), w.WZ(kz), -1.57f, .5f, 3f);
        await Frames(4);
        Vector2 OnFace(float y) => Rig.Cam.UnprojectPosition(new Vector3(w.WX(x0 - .02f), w.WY(y), w.WZ(kz + .5f)));
        var onStone = OnFace(top - 12.5f);
        var onWalk = Rig.Cam.UnprojectPosition(new Vector3(w.WX(wx + .5f), w.WY(walk + .02f), w.WZ(kz + .5f)));

        Tools.SetTool(Tool.Wand);
        Tools.SetDig(true);
        Move(onStone); await Frames(3);
        int litStone = Tools.WandCount;
        string said = Tools.PlanText ?? "";
        int removals0 = sim.Orders.Total - sim.Orders.BuildTotal;
        await Click(onStone);
        int marked = sim.Orders.Total - sim.Orders.BuildTotal - removals0;
        Move(onWalk); await Frames(3);
        int litWood = Tools.WandCount;
        var z1 = new InputEventKey { PhysicalKeycode = Key.Z, Keycode = Key.Z, Pressed = true, CtrlPressed = true };
        Tools._UnhandledInput(z1);
        int afterUndo = sim.Orders.Total - sim.Orders.BuildTotal - removals0;
        check("the wand on a stone wall lights all its stone and none of the logs on it; a click with the pick marks it to come down, Ctrl+Z takes it back",
            // (a stone or two at the wall's foot over a step of the ground may touch the rest by an edge only)
            litStone >= stone - 8 && litStone <= stone && said.StartsWith("Снести") && marked == litStone && litWood == wood && afterUndo == 0,
            $"stone {stone}, lit {litStone} («{said}»); marked {marked}; the walk {wood}, lit {litWood}; after Ctrl+Z {afterUndo}");

        // earth in hand: the walk laid over again in earth
        Tools.SetMaterial(Mat.Rammed);
        await Click(onWalk);
        int relays = sim.Relays.Count;
        string relaid = Tools.PlanText ?? "";
        // its third way: a blueprint of the stone alone
        int bps = Blueprints.Count;
        Tools.Opt.WandCapture = true; Tools.Changed();
        await Click(onStone);
        var bp = Blueprints.Count > bps ? Blueprints[^1] : null;
        check("with earth in hand the wand marks the walk to be laid again in earth; in its third way it keeps the stone alone as a blueprint",
            relays == wood && bp != null && bp.Stone == litStone && bp.Wood == 0,
            $"to lay again {relays} of {wood} («{relaid}»); blueprint {bp?.Describe()}, stone {bp?.Stone}, logs {bp?.Wood}");
        if (bp != null) RemoveBlueprint(bp);
        Tools.Opt.WandCapture = false;
        Tools.SetTool(Tool.None);
        sim.CancelArea(wx - 16, kz - 40, wx + 16, kz + 40);
        sim.Relays.Clear();
        for (int z = kz - 32; z <= kz + 32; z++)
            for (int x = wx - 8; x <= wx + 8; x++)
                for (int y = 0; y < w.SY; y++) { byte m = w.Get(x, y, z); if (Mat.IsMasonry(m) || m == Mat.Wood) w.Set(x, y, z, Mat.Air); }
        await Frames(2);
    }
}
