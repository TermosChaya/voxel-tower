using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The self-test's liquids, far west of the citadel: water let into a pit fills it by degrees, not at once, and is
    /// drawn as it comes; lava let out beside it glows, lights the ground about it, and where it runs into the water sets
    /// to stone in a hiss of steam. All of it cleared away after.
    /// </summary>
    async Task LiquidsSelfTest(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var w = World; var sim = Sim;
        int px = Site.KeepX - Site.KeepR - 300, pz = Site.KeepZ - 60, g = w.SurfAt(px, pz);
        // a pit 2 m across and 1 m deep, and a trickle of water let into it
        for (int z = pz - 8; z < pz + 8; z++) for (int x = px - 8; x < px + 8; x++) for (int y = g - 8; y < g + 20; y++) w.Set(x, y, z, Mat.Air);
        Rig.Focus(w.WX(px), w.WZ(pz));
        for (int i = 0; i < 64; i++) sim.Pour(Mat.Water, px - 7 + i % 14, g + 4, pz - 7 + i / 14 % 14, 8);
        Advance(.3);
        double soon = 0, later = 0;
        for (int z = pz - 8; z < pz + 8; z++) for (int x = px - 8; x < px + 8; x++) soon += w.Get(x, g - 8, z) == Mat.Water ? 1 : 0;
        Advance(6);
        await Frames(10);
        for (int z = pz - 8; z < pz + 8; z++) for (int x = px - 8; x < px + 8; x++) later += w.Get(x, g - 8, z) == Mat.Water ? 1 : 0;
        check("water let into a pit runs down and spreads over its floor by degrees, not at once",
            soon < later && later > 150, $"the pit's floor wet in {soon} of 256 voxels at 0,3 s, {later} at 6 s");

        // lava beside the pit: it glows, and running into the water sets to stone in steam
        int set0 = sim.LavaSet;
        sim.LetLava(px + 14, pz, 160);
        Advance(8);
        await Frames(20);
        var view = GetChildren().OfType<LavaView>().FirstOrDefault();
        bool lit = view != null && view.GetChildren().OfType<OmniLight3D>().Any(l => l.Visible);
        check("lava glows and lights the ground about it, and where it meets water sets to stone with a hiss",
            sim.Lavas.Count > 20 && lit && sim.LavaSet > set0 && Sound.Heard.Contains("fizz"),
            $"{sim.Lavas.Count} voxels of lava, its light {(lit ? "on" : "off")}, {sim.LavaSet - set0} set by water");
        foreach (int p in sim.Lavas.ToList()) { var (x, y, z) = WaterPlanner.Unpack(p); w.Set(x, y, z, Mat.Air); }
        for (int z = pz - 8; z < pz + 8; z++) for (int x = px - 8; x < px + 8; x++) for (int y = g - 8; y < g + 20; y++) if (w.Get(x, y, z) == Mat.Water) w.Set(x, y, z, Mat.Air);
        await Frames(2);
    }
}
