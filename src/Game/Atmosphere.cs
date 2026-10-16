using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>Dusk: violet sky light, a low warm sun with soft shadows, a cold rim light, flickering braziers and drifting embers.</summary>
public partial class Atmosphere : Node3D
{
    readonly List<(OmniLight3D light, float phase)> _torches = new();
    double _t;
    bool _calm;

    public Godot.Environment Env { get; private set; }

    public void Build(VoxelWorld w, Site site, bool calm)
    {
        _calm = calm;
        var sky = new Sky
        {
            SkyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/dusk_sky.gdshader") },
            ProcessMode = Sky.ProcessModeEnum.Quality,
            RadianceSize = Sky.RadianceSizeEnum.Size128,
        };
        Env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = .7f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.0f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = .88f,
            GlowEnabled = true,
            GlowIntensity = .45f,
            GlowBloom = 0f,
            GlowHdrThreshold = 1.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = new Color(0.114f, 0.086f, 0.149f),
            FogDensity = .55f,
            FogDepthBegin = 62f,
            FogDepthEnd = 124f,
            FogSkyAffect = 0f,
        };
        AddChild(new WorldEnvironment { Environment = Env });

        var sun = new DirectionalLight3D
        {
            LightColor = new Color("eccfb4"),
            LightEnergy = 1.3f,
            LightAngularDistance = 1.2f,
            ShadowEnabled = true,
            ShadowBias = .04f,
            ShadowNormalBias = 1.0f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = 110f,
        };
        AddChild(sun);
        sun.LookAtFromPosition(new Vector3(-14, 16, 9), new Vector3(0, 3, 0), Vector3.Up);

        var rim = new DirectionalLight3D { LightColor = new Color("7a88d8"), LightEnergy = .55f, ShadowEnabled = false };
        AddChild(rim);
        rim.LookAtFromPosition(new Vector3(12, 10, -10), new Vector3(0, 3, 0), Vector3.Up);

        var rnd = new Random(7);
        foreach (var (x, y, z) in site.Torches)
        {
            var l = new OmniLight3D
            {
                LightColor = new Color("ff8a3a"),
                LightEnergy = 2.4f,
                OmniRange = 6f,
                OmniAttenuation = 2f,
                ShadowEnabled = false,
                Position = new Vector3(w.WX(x), w.WY(y), w.WZ(z)),
            };
            AddChild(l);
            _torches.Add((l, (float)(rnd.NextDouble() * 6)));
        }

        if (!calm) AddChild(Embers());
    }

    /// <summary>Ash and sparks rising slowly over the field.</summary>
    static GpuParticles3D Embers()
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(11, 5, 8),
            Direction = new Vector3(0, 1, 0),
            Spread = 25f,
            InitialVelocityMin = .05f,
            InitialVelocityMax = .25f,
            Gravity = Vector3.Zero,
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = .6f,
            TurbulenceNoiseScale = 6f,
            TurbulenceInfluenceMin = .01f,
            TurbulenceInfluenceMax = .04f,
            ScaleMin = .6f,
            ScaleMax = 1.3f,
        };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = new Color(1f, .6f, .29f, .75f),
            NoDepthTest = false,
        };
        return new GpuParticles3D
        {
            Amount = 150,
            Lifetime = 40,
            Preprocess = 40,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(.035f, .035f), Material = mat },
            Position = new Vector3(0, 6, 0),
            VisibilityAabb = new Aabb(new Vector3(-13, -7, -10), new Vector3(26, 16, 20)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    public override void _Process(double delta)
    {
        _t += delta;
        float t = _calm ? 0 : (float)_t;
        foreach (var (l, ph) in _torches)
            l.LightEnergy = 2.4f * (.8f + .22f * MathF.Sin(t * 9 + ph) + .1f * MathF.Sin(t * 23 + ph * 2));
    }
}
