using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// A house drawn with the rectangle's house form, through the tool's input as the player draws it: its roof pitched
    /// over the walls and standing as an arch, a floor of logs, the dwelling keeping the walls' top; built, it is closed
    /// and furnished; drawn again over itself, it raises a second storey with a ladder up to it, and is one dwelling.
    /// </summary>
    async Task SelfTestHouses(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        Vector2 Screen(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(World.WX(x + .5f), World.WY(World.SurfAt(x, z)), World.WZ(z + .5f)));
        void Button(Vector2 p, bool down) => Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = p });
        async Task Drag(int ax, int az, int bx, int bz)
        {
            var a = Screen(ax, az); var b = Screen(bx, bz);
            Tools._UnhandledInput(new InputEventMouseMotion { Position = a });
            Button(a, true);
            for (int i = 1; i <= 8; i++) { Tools._UnhandledInput(new InputEventMouseMotion { Position = a.Lerp(b, i / 8f) }); await Frames(1); }
            Button(b, false);
            await Frames(2);
        }
        // level open ground east of the citadel, cleared of trees
        bool Level(int x, int z)
        {
            int h = World.SurfAt(x, z);
            for (int dz = -36; dz <= 36; dz += 6)
                for (int dx = -24; dx <= 24; dx += 6)
                    if (!World.InBounds(x + dx, 1, z + dz) || Math.Abs(World.SurfAt(x + dx, z + dz) - h) > 1 || Site.Forbidden(x + dx, z + dz)) return false;
            return true;
        }
        // (on a hexagonal valley the land ends some sixteen metres east of the citadel: the search starts nearer)
        var (hx, hz) = Nearest(Site.KeepX + Site.KeepR + (World.Land != null ? 60 : 120), Site.KeepZ - 60, 200, Level);
        ClearTrees(hx - 40, hz - 50, hx + 40, hz + 50);
        if (Sim.Alarm) Sim.StandDown();
        Rig.Focus(World.WX(hx), World.WZ(hz));
        await Frames(30);
        Tools.SetTool(Tool.Box);
        Tools.Opt.Dig = false; Tools.Opt.Hollow = true; Tools.Opt.House = true; Tools.Opt.Material = Mat.Stone;
        int x0 = hx - 18, z0 = hz - 27, x1 = hx + 18, z1 = hz + 27;
        await Drag(x0, z0, x1, z1);
        var home = Sim.Zones.Find(z => z.Kind == Zone.Dwelling && z.X0 == x0 && z.Z0 == z0);
        if (home == null)
        {
            check("a house drawn with the rectangle's «дом» is laid out as a dwelling", false, $"no dwelling at {x0},{z0} (level ground found at {hx},{hz}: {Level(hx, hz)})");
            return;
        }
        int top = home?.Depth ?? 0, floorTop = top - Shapes.HouseHeight;
        // the ridge runs along the long side, here along z: six courses up the slope, twelve voxels in from the eaves,
        // and the ridge itself ten courses up over the middle
        int rx = x0 - Shapes.Eaves + 12;
        bool roofMeant = top > 0 && Sim.Orders.At(rx, top + 6, hz) != 0 && Sim.Arched.Get(rx, top + 6, hz) != 0 && Sim.Orders.At(hx, top + 10, hz) != 0;
        bool floorMeant = top > 0 && Sim.Orders.At(hx, floorTop - 1, hz) != 0;
        // the walls, floor and roof laid at once, as by many hands; the door cut and the gate hung by the household,
        // then the furnishing laid at once
        Sim.CompleteAll();
        for (int i = 0; i < 90 && Sim.Gates.Find(g => g.Y == floorTop) is not { Ready: true }; i++) Advance(1);
        // nothing is laid where someone stands: those who hung the door are let out of the way a few times over
        for (int i = 0; i < 8 && home.Beds < 2; i++)
        {
            Sim.LookOver(home);
            Sim.CompleteAll();
            Sim.LookOver(home);
            if (home.Beds < 2) Advance(1);
        }
        bool roofed = top > 0 && World.Get(rx, top + 6, hz) == Mat.Wood && World.Get(hx, top + 10, hz) == Mat.Wood && World.Get(hx, top + 6, hz) == Mat.Air;
        bool floored = top > 0 && World.Get(hx + 3, floorTop - 1, hz + 5) == Mat.Wood;
        int beds = home?.Beds ?? 0;
        check("the house form marks a pitched roof of logs standing as an arch, and a floor of logs; built, the house is closed and furnished",
            home != null && floorMeant && roofMeant && roofed && floored && home.Closed && beds >= 2,
            $"dwelling {(home == null ? "none" : $"top {top}, closed {home.Closed} «{home.Why}», bunks {beds}")}; floor meant {floorMeant}, roof meant {roofMeant}, roofed {roofed}, floored {floored}; bunks planned {home.Planned}");
        // drawn again over it: a second storey
        await Drag(x0, z0, x1, z1);
        bool waiting = Sim.Storeys.Count == 1;
        Sim.CompleteAll();
        Advance(3);
        Sim.CompleteAll();
        Sim.LookOver(home);
        Sim.CompleteAll();
        Sim.LookOver(home);
        bool ladder = false;
        for (int z = z0; z <= z1 && !ladder; z++) for (int x = x0; x <= x1 && !ladder; x++) ladder = Mat.IsLadder[World.Get(x, top - 4, z)];
        check("drawn again over a house, the house form raises a second storey on it with a ladder up through its floor, one dwelling furnished on both",
            waiting && home != null && home.Depth == top + Shapes.HouseHeight && ladder && home.Closed && home.Beds > beds,
            $"storey waiting {waiting}, top {top} → {home?.Depth}, ladder {ladder}, closed {home?.Closed} «{home?.Why}», bunks {beds} → {home?.Beds}");
        Tools.SetTool(Tool.None);
        Tools.Opt.House = false; Tools.Opt.Hollow = false;
        await Frames(2);
    }
}
