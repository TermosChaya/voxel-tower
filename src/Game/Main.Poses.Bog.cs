using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The horde's homeland, a bog valley (--valley=bog [--holding=horde --horde-works=all] --demo --pose=bog): --look=0
    /// the lowland from high over it, its pools and mires among the birches and the drowned trees, the island in the
    /// middle; 1 close on a mire by its pools; 2 the island over the bog from low down. --cam=yaw,pitch,zoom moves the eye.
    /// </summary>
    void BogPose()
    {
        var sim = Sim; var w = World; var s = Site;
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        float Ground(float x, float z) => w.WY(w.SurfAt(Math.Clamp((int)x, 0, w.SX - 1), Math.Clamp((int)z, 0, w.SZ - 1)));
        float at(int k, float d) => _cam is { Length: > 2 } ? _cam[k] : d;
        switch (_look)
        {
            case 1:
            {
                // the mire with most pools about it
                float mx = s.KeepX - 200, mz = s.KeepZ;
                int best = -1;
                foreach (var (x, z, r) in s.Mires)
                {
                    int near = 0;
                    foreach (var p in s.Pools) if (Brushes.Dist(p.X - x, p.Z - z) < r + 120) near++;
                    if (near > best) { best = near; mx = x; mz = z; }
                }
                Rig.SetAt(w.WX(mx), Ground(mx, mz), w.WZ(mz), at(0, .6f), at(1, .55f), at(2, 1.1f));
                break;
            }
            case 2:
                Rig.SetAt(w.WX(s.KeepX), Ground(s.KeepX, s.KeepZ), w.WZ(s.KeepZ), at(0, .9f), at(1, .4f), at(2, .5f));
                break;
            default:
                Rig.SetAt(w.WX(s.KeepX - 260), Ground(s.KeepX - 260, s.KeepZ), w.WZ(s.KeepZ + 60), at(0, -1.1f), at(1, .75f), at(2, .32f));
                break;
        }
        GD.Print($"[pose] bog: {s.Kind}, {s.Pools.Count} pools, {s.Mires.Count} mires, {sim.Mires.Count} in the sim");
        Hud.Refresh();
        if (_bare) Hud.Visible = false;
    }
}
