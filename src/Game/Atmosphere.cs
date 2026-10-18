using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The light of the valley through the day the siege keeps: a sun that comes up in the east while the walls go up,
/// stands high, goes golden and low over the west as the horde draws near, and sets red at the horn; the night of the
/// assault under a cold moon, the torches and the fires brighter in it; a grey dawn once the wave is beaten back. Over
/// it all a violet sky light, a cold rim light, flickering braziers and drifting embers.
/// </summary>
public partial class Atmosphere : Node3D
{
    readonly List<(OmniLight3D light, float phase)> _torches = new();
    double _t;
    bool _calm;
    DirectionalLight3D _sun, _rim;
    ShaderMaterial _skyMat;
    Sim _sim;
    /// <summary>The time of day and the darkness as they are shown: they follow the sim's, easing, so that a horde called early brings the night on over a few seconds.</summary>
    float _day = -1, _dark;
    /// <summary>The time of day and the darkness as they are shown now, for the day on the wave clock.</summary>
    public float ShownDay => Math.Max(0, _day);
    public float ShownDark => _dark;
    /// <summary>How bright torches and fires burn now (the dark makes more of them), for the fire's own lights too.</summary>
    public float LampBoost { get; private set; } = 1;

    public Godot.Environment Env { get; private set; }
    /// <summary>The embers hang around whatever this camera looks at.</summary>
    public CameraRig Follow;
    GpuParticles3D _embers, _rain;
    /// <summary>How far the weather has come on, as shown: rain and fog, easing in and out.</summary>
    float _wet, _fog;

    public void Build(VoxelWorld w, Site site, bool calm)
    {
        _calm = calm;
        _skyMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/dusk_sky.gdshader") };
        var sky = new Sky
        {
            SkyMaterial = _skyMat,
            ProcessMode = Sky.ProcessModeEnum.Quality,
            RadianceSize = Sky.RadianceSizeEnum.Size128,
        };
        Env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = .82f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.0f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = .92f,
            GlowEnabled = true,
            GlowIntensity = .45f,
            GlowBloom = 0f,
            GlowHdrThreshold = 1.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = new Color(0.114f, 0.086f, 0.149f),
            FogDensity = .55f,
            FogDepthBegin = 84f,
            FogDepthEnd = 170f,
            FogSkyAffect = 0f,
        };
        AddChild(new WorldEnvironment { Environment = Env });

        var sun = new DirectionalLight3D
        {
            LightColor = new Color("eccfb4"),
            LightEnergy = 1.42f,
            LightAngularDistance = 1.2f,
            ShadowEnabled = true,
            ShadowBias = .04f,
            ShadowNormalBias = 1.0f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = 115f,
        };
        AddChild(sun);
        sun.LookAtFromPosition(new Vector3(-14, 16, 9), new Vector3(0, 3, 0), Vector3.Up);
        _sun = sun;

        var rim = new DirectionalLight3D { LightColor = new Color("7a88d8"), LightEnergy = .55f, ShadowEnabled = false };
        AddChild(rim);
        rim.LookAtFromPosition(new Vector3(12, 10, -10), new Vector3(0, 3, 0), Vector3.Up);
        _rim = rim;

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

        if (!calm) { _embers = Embers(); AddChild(_embers); }
        _rain = Rain();
        AddChild(_rain);
    }

    readonly Dictionary<Fixture, OmniLight3D> _sconces = new();
    VoxelWorld _w;

    /// <summary>Lights the torches the buildings dress themselves with, as they are set up and taken down.</summary>
    public void Watch(Sim sim)
    {
        _sim = sim;
        _w = sim.World;
        foreach (var f in sim.Fixtures) Lit(f);
        sim.FixtureAdded += Lit;
        sim.FixtureRemoved += f =>
        {
            if (!_sconces.Remove(f, out var l)) return;
            _torches.RemoveAll(t => t.light == l);
            l.QueueFree();
        };
    }

    void Lit(Fixture f)
    {
        if (f.Kind != FixtureKind.Torch || _sconces.ContainsKey(f)) return;
        var l = new OmniLight3D
        {
            LightColor = new Color("ff8a3a"),
            LightEnergy = 1.6f,
            OmniRange = 3.2f,
            OmniAttenuation = 1.6f,
            ShadowEnabled = false,
            Position = new Vector3(_w.WX(f.LX), _w.WY(f.LY), _w.WZ(f.LZ)),
        };
        AddChild(l);
        _sconces[f] = l;
        _torches.Add((l, f.LX * .37f + f.LZ * .11f));
    }

    /// <summary>Rain: streaks falling all round whatever the camera looks at.</summary>
    static GpuParticles3D Rain()
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(26, 1, 20),
            Direction = new Vector3(.12f, -1, .06f),
            Spread = 3f,
            InitialVelocityMin = 13f,
            InitialVelocityMax = 16f,
            Gravity = new Vector3(0, -6, 0),
        };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(.72f, .78f, .86f, .28f),
        };
        return new GpuParticles3D
        {
            Amount = 2400,
            Lifetime = 1.6f,
            Emitting = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(.014f, .42f), Material = mat },
            LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-30, -30, -24), new Vector3(60, 34, 48)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
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
            Position = new Vector3(0, 9, 0),
            LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-13, -7, -10), new Vector3(26, 16, 20)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_embers != null && Follow != null) { var t0 = Follow.Target; _embers.Position = new Vector3(t0.X, 9, t0.Y); }
        if (Follow != null) { var t1 = Follow.Target; _rain.Position = new Vector3(t1.X, 20, t1.Y); }
        if (_sim != null)
        {
            float k = _calm ? 1 : (float)delta / 5;
            _wet = Mathf.MoveToward(_wet, _sim.Weather == Weather.Rain ? 1 : 0, k);
            _fog = Mathf.MoveToward(_fog, _sim.Weather == Weather.Fog ? 1 : 0, k);
            _rain.Emitting = _wet > .3f;
            _rain.AmountRatio = Math.Clamp(_wet, .05f, 1);
        }
        if (_sim != null)
        {
            var wv = _sim.Waves;
            // a staged scene stops the clock far out: it keeps the afternoon light it always had
            float day = wv.Countdown > Waves.Prep * 2 ? .7f : wv.Day, k = (float)delta / 4;
            if (_day < 0 || _calm) { _day = day; _dark = wv.Darkness; }
            else { _day = Mathf.MoveToward(_day, day, Math.Max(k, Math.Abs(day - _day) * k * 2)); _dark = Mathf.MoveToward(_dark, wv.Darkness, k); }
            Light(_day, _dark);
        }
        float t = _calm ? 0 : (float)_t;
        foreach (var (l, ph) in _torches)
        {
            float base0 = _sconces.Count > 0 && l.OmniRange < 4 ? 1.6f : 2.4f;
            l.LightEnergy = base0 * LampBoost * (.8f + .22f * MathF.Sin(t * 9 + ph) + .1f * MathF.Sin(t * 23 + ph * 2));
        }
    }

    // the day's sun at morning, noon and sunset, and the night's moon
    static readonly Color Morning = new("f0cfae"), Noon = new("eee0cc"), Evening = new("ffb066"), Sunset = new("ff6c3c"), Moon = new("7d92d8");

    /// <summary>
    /// The light for a time of day (0 dawn, ½ noon, 1 sunset) and a darkness (0 day, 1 night): where the sun stands and
    /// what colour it is, how much the sky lights, the fog, how bright the lamps.
    /// </summary>
    void Light(float day, float dark)
    {
        // up in the east at dawn, high in the south at noon, down in the west at sunset: the horde comes out of the sunset
        float az = MathF.PI * day * .92f + .12f, el = Mathf.DegToRad(10 + 48 * MathF.Sin(MathF.PI * Math.Clamp(day, .04f, .96f)));
        var dir = new Vector3(MathF.Cos(az) * MathF.Cos(el), MathF.Sin(el), MathF.Sin(az) * MathF.Cos(el) * .7f + .35f);
        var col = day < .2f ? Morning.Lerp(Noon, day / .2f) : day < .72f ? Noon : day < .93f ? Noon.Lerp(Evening, (day - .72f) / .21f) : Evening.Lerp(Sunset, (day - .93f) / .07f);
        float energy = day < .12f ? Mathf.Lerp(.9f, 1.42f, day / .12f) : day > .9f ? Mathf.Lerp(1.42f, 1.0f, (day - .9f) / .1f) : 1.42f;
        // the moon stands high in the south-west all night
        var moonDir = new Vector3(-.4f, .72f, .48f).Normalized();
        dir = dir.Normalized().Lerp(moonDir, dark).Normalized();
        _sun.LightColor = col.Lerp(Moon, dark);
        _sun.LightEnergy = Mathf.Lerp(energy, .3f, dark);
        _sun.LookAtFromPosition(dir * 20, Vector3.Zero, Mathf.Abs(dir.Y) > .98f ? Vector3.Forward : Vector3.Up);
        _rim.LightEnergy = Mathf.Lerp(.55f, .22f, dark);
        // rain greys the light and dims it; fog comes in close, pale by day and dark at night
        Env.AmbientLightEnergy = Mathf.Lerp(.82f, .27f, dark) * (1 - .18f * _wet);
        _sun.LightEnergy *= 1 - .45f * _wet - .3f * _fog;
        var fogDay = new Color(0.114f, 0.086f, 0.149f).Lerp(new Color(.34f, .35f, .38f), _fog * .8f + _wet * .3f);
        Env.FogLightColor = fogDay.Lerp(new Color(.03f, .035f, .07f), dark);
        Env.FogDepthBegin = Mathf.Lerp(84, 22, _fog) - 20 * _wet;
        Env.FogDepthEnd = Mathf.Lerp(170, 80, _fog) - 30 * _wet;
        Env.FogDensity = Mathf.Lerp(.55f, .82f, Math.Max(_fog, _wet * .5f));
        _skyMat.SetShaderParameter("amb_sky", new Vector3(0.254f, 0.246f, 0.578f).Lerp(new Vector3(.09f, .11f, .3f), dark));
        _skyMat.SetShaderParameter("bg_top", new Vector3(0.027f, 0.017f, 0.040f).Lerp(new Vector3(.006f, .008f, .02f), dark));
        LampBoost = 1 + .7f * dark;
    }
}
