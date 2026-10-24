using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The alchemists at a wall of ours (--pose=acid, --at=seconds of it, 14 by itself): a stone wall west of the citadel
    /// and three alchemists of the horde a dozen metres before it with goblins about them, throwing their flasks at it; the
    /// acid running down its face, eating at its foot, the fumes rising. --look=1 the same at night, the acid glowing;
    /// --look=2 close on the foot of the wall.
    /// </summary>
    void Acid()
    {
        var sim = Sim; var w = World;
        if (_look == 1) Nightfall(); else sim.Waves.Countdown = 1e9;
        int wx = Site.KeepX - Site.KeepR - 90, kz = Site.KeepZ;
        int top = WallWest(-90, 32, 60);
        foreach (var (dx, dz) in new[] { (-112, -22), (-104, 8), (-118, 32) }) Camp(Kinds.Alchemist, wx + dx, kz + dz);
        foreach (var (dx, dz) in new[] { (-96, -10), (-92, 22), (-124, 0), (-100, 40) }) Camp(Kinds.Goblin, wx + dx, kz + dz);
        Advance(_poseSeconds);
        if (_look == 2) Rig.SetAt(w.WX(wx - 18), w.WY(top - 28), w.WZ(kz + 4), -1.15f, .5f, 4.2f);
        else Rig.SetAt(w.WX(wx - 62), w.WY(top - 22), w.WZ(kz + 6), -1.05f, .55f, 1.9f);
        Hud.Refresh();
    }
}
