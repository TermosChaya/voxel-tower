using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// What is seen of the household's faith (Sim.Heavens.cs, Sim.Angels.cs): a pillar of fire standing on the field,
/// burning from the ground to thirty metres up, flames running up it and lighting the field about it; the light out of
/// heaven the host comes down in, and the flash and the motes going up where one of the host goes up again; a cold glare
/// over a hailstorm; gold light where an angel is sent, dark red where the destroyer passes.
/// </summary>
public partial class MagicView
{
    sealed class Column { public MeshInstance3D Core, Glow; public StandardMaterial3D CoreMat, GlowMat; public OmniLight3D Light; public CpuParticles3D Flames; }
    readonly Dictionary<FirePost, Column> _columns = new();
    /// <summary>The beams of light the host came down in, fading: the beam and when it came.</summary>
    readonly List<(MeshInstance3D beam, StandardMaterial3D mat, double at)> _heavenBeams = new();
    static readonly Color Gold = new(1f, .84f, .5f), FireGold = new(1f, .58f, .2f);

    void InitHeaven(Main m)
    {
        m.Sim.HostCame += HostCame;
        m.Sim.AngelRose += AngelRose;
    }

    void ExitHeaven()
    {
        _m.Sim.HostCame -= HostCame;
        _m.Sim.AngelRose -= AngelRose;
    }

    /// <summary>The flash of a spell of the faith as it is cast.</summary>
    void CastHeaven(SpellKind k, Vector3 ground, float r)
    {
        switch (k)
        {
            case SpellKind.Hail:
                Flare(ground + Vector3.Up * 14, new Color(.78f, .86f, 1f), 4, 30, 5);
                break;
            case SpellKind.FirePillar:
                Flare(ground + Vector3.Up * 3, FireGold, 6, 18, 1.5f, Motes(ground, FireGold, 60, 3f, 1.2f, .1f));
                break;
            case SpellKind.Guardian:
            case SpellKind.Cherub:
                Spread(ground, r, Gold);
                Flare(ground + Vector3.Up * 4, Gold, 5, 14, 2.5f, Motes(ground, Gold, 50, 1.6f, r * VoxelWorld.Vox * .4f));
                break;
            case SpellKind.Destroyer:
                Spread(ground, r, new Color(.25f, .05f, .05f));
                Flare(ground + Vector3.Up * 6, new Color(.7f, .12f, .08f), 4, 20, 3.5f);
                break;
            case SpellKind.Host:
                Spread(ground, r, Gold);
                break;
        }
    }

    /// <summary>The host comes down at a spot: a beam of light out of the sky over it, a glare and motes of gold.</summary>
    void HostCame(float x, float y, float z)
    {
        var at = At(x, y, z);
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoColor = new Color(Gold, .6f),
        };
        var beam = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 3.2f, BottomRadius = 2.2f, Height = 160, RadialSegments = 16, Rings = 1 },
            MaterialOverride = mat, Position = at + Vector3.Up * 80, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(beam);
        _heavenBeams.Add((beam, mat, _t));
        Flare(at + Vector3.Up * 4, Gold, 8, 24, 3, Motes(at, Gold, 120, 2.4f, 2.2f, .08f, 2.5f));
    }

    /// <summary>One of the host goes up again: a flash, and motes of light going up off where it stood.</summary>
    void AngelRose(float x, float y, float z)
    {
        var at = At(x, y, z);
        Flare(at + Vector3.Up * 1.5f, Gold, 4, 8, 1.5f, Motes(at + Vector3.Up * .8f, Gold, 40, 3.5f, .4f, .07f, 2));
    }

    /// <summary>Each pillar of fire as it stands, and the beams of the host fading.</summary>
    void Heaven(Sim sim)
    {
        foreach (var p in sim.FirePillars)
        {
            if (_columns.ContainsKey(p)) continue;
            var c = new Column();
            var at = At(p.X, p.Y, p.Z);
            const float high = 15;
            c.CoreMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoColor = new Color(1f, .82f, .45f, 0),
            };
            c.GlowMat = (StandardMaterial3D)c.CoreMat.Duplicate();
            c.Core = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .3f, BottomRadius = .6f, Height = high, RadialSegments = 14, Rings = 1 },
                MaterialOverride = c.CoreMat, Position = at + Vector3.Up * high / 2, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            c.Glow = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .8f, BottomRadius = 1.25f, Height = high * 1.05f, RadialSegments = 18, Rings = 1 },
                MaterialOverride = c.GlowMat, Position = at + Vector3.Up * high / 2, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            c.Light = new OmniLight3D { Position = at + Vector3.Up * 4, LightColor = FireGold, LightEnergy = 0, OmniRange = 16, ShadowEnabled = false };
            var ramp = new Gradient();
            ramp.SetColor(0, new Color(1f, .85f, .45f, .55f));
            ramp.SetColor(1, new Color(1f, .35f, .08f, 0));
            c.Flames = new CpuParticles3D
            {
                Position = at, Amount = 50, Lifetime = 1.6f, Emitting = true, EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 1.1f,
                Direction = Vector3.Up, Spread = 8, InitialVelocityMin = 5, InitialVelocityMax = 9, Gravity = new Vector3(0, 1.5f, 0),
                ScaleAmountMin = .6f, ScaleAmountMax = 1.4f, ColorRamp = ramp, Mesh = new BoxMesh { Size = Vector3.One * .16f, Material = _ember },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            foreach (var n in new Node3D[] { c.Core, c.Glow, c.Light, c.Flames }) AddChild(n);
            _columns[p] = c;
        }
        List<FirePost> gone = null;
        foreach (var (p, c) in _columns)
        {
            double left = p.Until - sim.Time;
            if (!sim.FirePillars.Contains(p) || left <= 0)
            {
                foreach (var n in new Node3D[] { c.Core, c.Glow, c.Light, c.Flames }) n.QueueFree();
                (gone ??= new()).Add(p);
                continue;
            }
            // up in a moment, a flicker as it burns, down in its last two seconds
            float a = (float)Math.Clamp(left / 2, 0, 1) * (.85f + .15f * MathF.Sin((float)_t * 11 + p.X));
            c.CoreMat.AlbedoColor = new Color(1f, .8f, .42f, .42f * a);
            c.GlowMat.AlbedoColor = new Color(1f, .45f, .12f, .14f * a);
            c.Light.LightEnergy = 3 * a;
            c.Flames.Emitting = left > .5;
        }
        if (gone != null) foreach (var p in gone) _columns.Remove(p);
        for (int i = _heavenBeams.Count - 1; i >= 0; i--)
        {
            var (beam, mat, at) = _heavenBeams[i];
            float age = (float)(_t - at);
            mat.AlbedoColor = new Color(Gold, .6f * Math.Clamp(1 - (age - 1.2f) / 1.5f, 0, 1));
            if (age < 3) continue;
            beam.QueueFree();
            _heavenBeams.RemoveAt(i);
        }
    }
}
