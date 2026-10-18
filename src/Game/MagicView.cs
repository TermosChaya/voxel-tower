using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// What is seen of the old powers at work: when a life is given at the altar, a flash of dark violet light over it and
/// a swarm of pale motes rising from it and fading.
/// </summary>
public partial class MagicView : Node3D
{
    sealed class Flash { public OmniLight3D Light; public CpuParticles3D Motes; public double At; }

    Main _m;
    readonly List<Flash> _flashes = new();
    StandardMaterial3D _mote;
    double _t;
    static readonly Color Violet = new(.58f, .32f, .98f), Pale = new(.68f, .98f, .82f);

    public void Init(Main m)
    {
        _m = m;
        _mote = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, EmissionEnabled = true, Emission = Pale, EmissionEnergyMultiplier = 1.5f,
        };
        m.Sim.RiteDone += Given;
    }

    /// <summary>A life given at the altar: the flash and the motes over where it knelt.</summary>
    void Given(float x, float y, float z)
    {
        var w = _m.World;
        var at = new Vector3(w.WX(x), w.WY(y + 6), w.WZ(z));
        var light = new OmniLight3D { Position = at + Vector3.Up * .6f, LightColor = Violet, LightEnergy = 9, OmniRange = 9, ShadowEnabled = false };
        AddChild(light);
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(Pale, .95f));
        ramp.SetColor(1, new Color(Violet, 0));
        var motes = new CpuParticles3D
        {
            Position = at, Amount = 48, Lifetime = 2.2, OneShot = true, Explosiveness = .85f, Emitting = true,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = .45f,
            Direction = Vector3.Up, Spread = 35, InitialVelocityMin = .6f, InitialVelocityMax = 2.2f, Gravity = new Vector3(0, .35f, 0),
            DampingMin = .4f, DampingMax = .9f, ScaleAmountMin = .5f, ScaleAmountMax = 1.2f, ColorRamp = ramp,
            Mesh = new BoxMesh { Size = Vector3.One * .06f, Material = _mote },
        };
        AddChild(motes);
        _flashes.Add(new Flash { Light = light, Motes = motes, At = _t });
    }

    public override void _Process(double delta)
    {
        _t += delta;
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            var f = _flashes[i];
            float age = (float)(_t - f.At);
            // a hard flash, then a slow fading glow
            f.Light.LightEnergy = age < .15f ? 9 : 9 * MathF.Exp(-(age - .15f) * 1.6f);
            if (age < 3.5f) continue;
            f.Light.QueueFree(); f.Motes.QueueFree();
            _flashes.RemoveAt(i);
        }
    }

    public override void _ExitTree()
    {
        if (_m?.Sim != null) _m.Sim.RiteDone -= Given;
    }
}
