using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The hexagonal valley in the game: the land a hexagon, a wall drawn out past its edge marked only on the land, the
    /// far land built round it, and the camera kept over it.
    /// </summary>
    async Task SelfTestHex(Action<string, bool, string> check)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var land = World.Land;
        // a wall drawn from the plateau out east over the edge: none of it off the land
        int x0 = Site.KeepX + 40, z0 = Site.KeepZ - 90, x1 = World.SX - 4;
        var line = Shapes.Line(World, Site, x0, z0, x1, z0, new Stroke { Mat = Mat.Rock, Width = 3, Height = 8 }, 0);
        int offLand = 0;
        for (int i = 0; i < line.Count; i++) if (!World.OnLand(line.X(i), line.Z(i))) offLand++;
        int far = Atmos?.Far?.Triangles ?? 0;
        check("the valley is a hexagon: a wall drawn out past its edge is marked only on the land, and the far land lies round it",
            land != null && line.Count > 0 && offLand == 0 && far > 1000,
            $"hexagon {land != null}; {line.Count} voxels of the wall, {offLand} of them off the land; far land {far} triangles");
    }
}
