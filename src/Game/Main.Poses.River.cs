using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// Where the river is crossed (--pose=river, best with --valley=marsh): --look=0 the road's old bridge, 1 the ford a
    /// third of the way down the river, 2 the side the horde comes by over the river from the citadel; anywhere else with
    /// the camera's own --cam=x,z,yaw,pitch,zoom.
    /// </summary>
    void RiverPose()
    {
        var sim = Sim; var w = World; var s = Site;
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        float Ground(float x, float z) => w.WY(w.SurfAt(Math.Clamp((int)x, 0, w.SX - 1), Math.Clamp((int)z, 0, w.SZ - 1)));
        // the river's course inside the land, as the valley was made with it
        int i0 = -1, i1 = -1;
        for (int i = 0; i < s.River.Count; i++)
            if (s.Hex == null || s.Hex.Edge(s.River[i].X, s.River[i].Z) > 30) { if (i0 < 0) i0 = i; i1 = i; }
        float fx = s.KeepX - 420, fz = s.KeepZ;
        if (_look == 0) { foreach (var l in s.Landmarks) if (l.Name == "Старый мост") (fx, fz) = (l.X, l.Z); }
        else if (_look == 1 && i0 >= 0) { var f = s.River[i0 + (i1 - i0) / 3]; (fx, fz) = (f.X, f.Z); }
        Rig.SetAt(w.WX(fx), Ground(fx, fz), w.WZ(fz), -1.0f, .8f, .4f);
        GD.Print($"[pose] river: {s.Kind}, looking at {fx:0},{fz:0}");
        Hud.Refresh();
    }
}
