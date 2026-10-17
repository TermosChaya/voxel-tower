using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// A flag on a pole at the mouth of every mine, in the colours of whoever holds it: pale for nobody, our blue, the horde's
/// red. It climbs the pole as the mine is being taken and sinks as it is being lost, and stirs in the wind.
/// </summary>
public partial class MineView : Node3D
{
    sealed class Flag { public Mine Mine; public MeshInstance3D Cloth; public StandardMaterial3D Mat; public Vector3 Foot; }

    public static readonly Color Nobody = new(.78f, .74f, .66f), Ours = new(.36f, .55f, .82f), Horde = new(.72f, .16f, .12f);
    const float Pole = 36, ClothW = 10, ClothH = 7;

    Main _m;
    readonly List<Flag> _flags = new();
    double _t;

    public void Init(Main m)
    {
        _m = m;
        var w = m.World;
        float v = VoxelWorld.Vox;
        var wood = new StandardMaterial3D { AlbedoColor = new Color("3b2a1f"), Roughness = .9f };
        foreach (var mine in m.Sim.Mines)
        {
            int sx = mine.Place.StandX, sz = mine.Place.StandZ;
            var foot = new Vector3(w.WX(sx + .5f), w.WY(w.SurfAt(sx, sz)), w.WZ(sz + .5f));
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.2f * v, Pole * v, 1.2f * v) }, MaterialOverride = wood, Position = foot + new Vector3(0, Pole * v / 2, 0) });
            AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 1.1f * v, Height = 2.2f * v }, MaterialOverride = wood, Position = foot + new Vector3(0, Pole * v, 0) });
            var mat = new StandardMaterial3D { AlbedoColor = Nobody, Roughness = .85f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            var cloth = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(ClothW * v, ClothH * v, .3f * v) }, MaterialOverride = mat };
            AddChild(cloth);
            _flags.Add(new Flag { Mine = mine, Cloth = cloth, Mat = mat, Foot = foot });
        }
    }

    public override void _Process(double delta)
    {
        _t += delta;
        float v = VoxelWorld.Vox;
        foreach (var f in _flags)
        {
            float g = f.Mine.Grip;
            f.Mat.AlbedoColor = g > .02f ? Ours : g < -.02f ? Horde : Nobody;
            float up = (6 + Math.Abs(g) * (Pole - 6 - ClothH)) * v;
            float sway = MathF.Sin((float)_t * 1.7f + f.Foot.X * 3) * .35f;
            var basis = new Basis(Vector3.Up, sway);
            f.Cloth.Transform = new Transform3D(basis, f.Foot + new Vector3(0, up + ClothH * v / 2, 0) + basis * new Vector3(ClothW * v / 2 + .6f * v, 0, 0));
        }
    }
}
