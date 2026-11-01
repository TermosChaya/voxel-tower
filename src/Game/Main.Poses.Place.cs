using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// A named place of the valley close (--pose=place --look=N: its N-th, in the order the valley laid them; --cam=yaw,
    /// pitch,zoom): for a look at one made up against the top of the world, a mine's knoll or a watchtower on a
    /// mountain's peak (--valley=mountain --seed=9 the silver mine, 36 the fallen tower).
    /// </summary>
    void PlacePose()
    {
        var sim = Sim; var w = World; var s = Site;
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        float at(int k, float d) => _cam is { Length: > 2 } ? _cam[k] : d;
        if (s.Landmarks.Count == 0) { GD.Print("[pose] place: the valley has none"); return; }
        var l = s.Landmarks[Math.Clamp(_look, 0, s.Landmarks.Count - 1)];
        Rig.SetAt(w.WX(l.X), w.WY(w.SurfAt(l.X, l.Z)), w.WZ(l.Z), at(0, -.9f), at(1, .6f), at(2, .9f));
        GD.Print($"[pose] place {_look}: {l.Name} at {l.X},{l.Z}, its ground {w.SurfAt(l.X, l.Z)} of the world's {w.SY}; the places: {string.Join(", ", s.Landmarks.ConvertAll(m => m.Name))}");
        Hud.Refresh();
    }
}
