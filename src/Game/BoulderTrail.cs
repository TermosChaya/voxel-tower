using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// A boulder hurled by a giant sheds earth and grit as it flies: crumbs of what it is made of falling away behind it, so
/// that its arc reads from far off.
/// </summary>
public partial class BoulderTrail : Node
{
    Main _m;
    double _next;

    public void Init(Main m) => _m = m;

    public override void _Process(double delta)
    {
        if (_m?.Sim == null || _m.Paused) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (now < _next) return;
        _next = now + .07;
        foreach (var f in _m.Sim.Fliers)
        {
            if (f.Carrier != null || f.Unit != null || f.Voxels.Count == 0 || f.Cause is not (Sim.HurledCause or Sim.BoulderCause or Sim.LordStoneCause)) continue;
            if (f.VX * f.VX + f.VY * f.VY + f.VZ * f.VZ < 25 * VoxelWorld.PerMetre * VoxelWorld.PerMetre) continue;
            var v = f.Voxels[(int)(now * 7) % f.Voxels.Count];
            _m.Debris.Spawn(v.m == Mat.Air ? Mat.Soil : v.m, (int)f.X, (int)f.Y, (int)f.Z, 1, .25f, .8f);
        }
    }
}
