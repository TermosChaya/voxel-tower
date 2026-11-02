using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// An old work of people in the valley's wild close (--pose=traces, MapGen.Traces.cs): --look=N the N-th of them as
    /// the valley was made (--valley= and --seed= choose the valley), from a few metres off; --cam=yaw,pitch,zoom as for
    /// the places.
    /// </summary>
    void TracesPose()
    {
        var sim = Sim; var w = World; var s = Site;
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        if (_bare) Hud.Visible = false;
        float at(int k, float d) => _cam is { Length: > 2 } ? _cam[k] : d;
        if (s.Traces.Count == 0) { GD.Print("[pose] traces: the valley has none"); return; }
        var (kind, x, z) = s.Traces[Math.Clamp(_look, 0, s.Traces.Count - 1)];
        float zoom = kind is OldWork.Ring or OldWork.Footing or OldWork.Fence or OldWork.Ruin ? 3.2f : 5.5f;
        Rig.SetAt(w.WX(x), w.WY(w.SurfAt(x, z)), w.WZ(z), at(0, -.9f), at(1, .5f), at(2, zoom));
        GD.Print($"[pose] trace {_look}: {kind} at {x},{z}; the valley's: {string.Join(", ", s.Traces.ConvertAll(t => t.Kind.ToString()))}");
        Hud.Refresh();
    }
}
