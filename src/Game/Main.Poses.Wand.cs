using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>What a staged scene does with the cursor a few frames before the picture, once the view has settled.</summary>
    Action _poseLater;

    /// <summary>
    /// The magic wand (--pose=wand): a stone wall west of the citadel with a walk of logs on it, the wand in hand.
    /// --look=0 the cursor on the stone with the pick: all of the stone lit, none of the logs; 1 on the walk; 2 the stone
    /// marked to come down, and the cursor on the walk with stone in hand, to lay it over again; 3 told exactly: one
    /// shade of the stone; 4 the stone chosen with Shift and the cursor on the walk, both lit to come down together.
    /// </summary>
    void WandPose()
    {
        var w = World; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int wx = Site.KeepX - Site.KeepR - 90, kz = Site.KeepZ;
        ClearTrees(wx - 120, kz - 80, wx + 30, kz + 80);
        int top = WallWest(-90, 24, 40);
        int walk = Shapes.LevelTop(w, wx, kz, 4);
        sim.InstantLabour = true;
        sim.DesignateBuild(Shapes.Line(w, Site, wx, kz - 40, wx, kz + 40, new Stroke { Mat = Mat.Wood, Width = 8, Height = 4, Level = true }, walk));
        sim.InstantLabour = false;
        int x0 = wx;
        while (Mat.IsMasonry(w.Get(x0 - 1, top - 12, kz))) x0--;
        Rig.SetAt(w.WX(wx - 30), w.WY(top - 12), w.WZ(kz + 6), -1.25f, .5f, 2.6f);
        Tools.SetTool(Tool.Wand);
        Tools.SetDig(true);
        if (_look == 3) Tools.Opt.WandExact = true;
        _poseLater = () =>
        {
            var onStone = Rig.Cam.UnprojectPosition(new Vector3(w.WX(x0 - .02f), w.WY(top - 12.5f), w.WZ(kz + 10.5f)));
            var onWalk = Rig.Cam.UnprojectPosition(new Vector3(w.WX(wx + .5f), w.WY(walk + .02f), w.WZ(kz - 12.5f)));
            void Move(Vector2 p) => Tools._UnhandledInput(new InputEventMouseMotion { Position = p });
            if (_look == 2)
            {
                Move(onStone);
                Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = onStone });
                Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = onStone });
                Tools.SetMaterial(Mat.Stone);
                Move(onWalk);
            }
            else if (_look == 4)
            {
                // the stone chosen with Shift, the cursor on the walk: both lit, to come down as one
                Move(onStone);
                Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = onStone, ShiftPressed = true });
                Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = onStone, ShiftPressed = true });
                Move(onWalk);
            }
            else Move(_look == 1 ? onWalk : onStone);
            Hud.Refresh();
        };
        Hud.Refresh();
    }
}
