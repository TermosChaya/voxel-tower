using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The light of the valley through the day the siege keeps: a sun that comes up in the east while the walls go up,
/// stands high, goes golden and low over the west as the horde draws near, and sets red at the horn; the night of the
/// assault under a cold moon, the torches and the fires brighter in it; a grey dawn once the wave is beaten back. Over
/// it all a violet sky light, a cold rim light, flickering braziers and drifting embers. The sky itself goes with the
/// day (dusk_sky.gdshader, told what to paint each frame in the sky_* globals): blue and hazy by day, gold and red as
/// the sun goes down, stars and the moon at night, the blood moon red, clouds drifting on the wind, thick and grey in the
/// rain, lightning lighting them; and beyond the valley the far land, seen only from someone's eyes.
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
    /// <summary>How much nearer the fog begins (Godot units): seen from someone's eyes the valley is near, not eighty units off.</summary>
    public float FogShift;
    /// <summary>The time of day and the darkness set from outside (the trailer's shots), 0 to 1; below 0, the sim's own.</summary>
    public float DayOverride = -1, DarkOverride = -1;
    /// <summary>How much thicker the air is than the weather makes it, 0 to 1 (the trailer's far looks over the valley).</summary>
    public float Haze;
    /// <summary>
    /// The valley is seen in perspective (someone's eyes, the trailer's lens): the sun and the moon give no glint then.
    /// The pixel whose way into the scene runs straight at the sun got a glint of nothing over nothing, and the glow spread
    /// it black over the whole frame; from the diorama's camera, every pixel looking the same way, that never happens.
    /// </summary>
    public bool Perspective;
    GpuParticles3D _embers, _rain;
    /// <summary>How far the weather has come on, as shown: rain and fog, easing in and out.</summary>
    float _wet, _fog;
    /// <summary>The land beyond the valley, round it to the mountains, seen only from someone's eyes.</summary>
    public FarLand Far { get; private set; }
    /// <summary>Thunder far off after the sheet lightning of a night of rain, as loud as it is (0 to 1): the sounds hear of it.</summary>
    public event Action<float> Thunder;
    // the sky's own clock and the clouds' drift, wound on by the wind; both stand still in a calm run, so that a screenshot
    // comes out the same every time
    double _skyT;
    Vector2 _drift = new(.37f, .11f);
    float _cloudWind = 1;
    /// <summary>The violet light the sky lends the scene as it was last told to the sky: told again only when it changes,
    /// for every telling works out the light of the whole sky over again.</summary>
    Vector3 _ambSky = new(-1, -1, -1);
    /// <summary>A time of day and a darkness held by a test or a staged scene instead of the sim's; shown at once.</summary>
    (float day, float dark)? _pin;
    bool _snap;
    /// <summary>Where the day's sun stands, as its light comes from it; the sky lets it go down under the horizon at dusk.</summary>
    Vector3 _sunDir = Vector3.Up;
    // lightning: towards where it was seen, when (on our own clock _t, which runs on while the game is stopped) and how
    // bright; the next sheet lightning of a night of rain (game seconds), and the thunder after the last one (our clock)
    Vector3 _flashDir = Vector3.Up;
    double _flashAt = -1e9, _nextSheet, _thunderAt = -1;
    float _flashPower, _thunderLoud;
    readonly Random _rnd = new(23);
    /// <summary>What the sky was told last, the sky_* globals by name: the rendering server will not say them back outside the
    /// editor, and the tests want to see that the sky goes with the day.</summary>
    public readonly Dictionary<string, Vector4> Told = new();
    /// <summary>A sky_* global, its name made into the engine's own once and for all: told every frame by a plain string, it
    /// would be looked up in the engine's table afresh each time and leave one more object behind for the collector.</summary>
    sealed class SkyGlobal
    {
        public readonly string Name;
        public readonly StringName Id;
        public SkyGlobal(string name) { Name = name; Id = new StringName(name); }
    }
    static readonly SkyGlobal SkySun = new("sky_sun"), SkyMoon = new("sky_moon"), SkyMood = new("sky_mood"), SkyZenith = new("sky_zenith"),
        SkyHorizon = new("sky_horizon"), SkyGlow = new("sky_glow"), SkyLight = new("sky_light"), SkyView = new("sky_view"),
        SkyFlash = new("sky_flash"), SkyClock = new("sky_clock");

    public void Build(VoxelWorld w, Site site, bool calm)
    {
        _calm = calm;
        _skyMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/dusk_sky.gdshader") };
        _skyMat.SetShaderParameter("cloud_noise", CloudNoise());
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
                // no glint of its own: seen in perspective (first person, the trailer), the one pixel a light stands
                // straight behind gave a glint of nothing over nothing, and the glow spread it black over the whole frame
                LightSpecular = 0,
                Position = new Vector3(w.WX(x), w.WY(y), w.WZ(z)),
            };
            AddChild(l);
            _torches.Add((l, (float)(rnd.NextDouble() * 6)));
        }

        if (!calm) { _embers = Embers(); AddChild(_embers); }
        _rain = Rain();
        AddChild(_rain);
        Far = new FarLand();
        AddChild(Far);
        Far.Build(w, site);
        // the sky is painted for the camera as it stands this frame: after the cameras have moved
        ProcessPriority = 10;
    }

    /// <summary>
    /// The stuff of the clouds, worked out once at the start with Godot's own noise: four noises that tile, packed into the
    /// channels of one texture — the heaps of the low clouds, their finer stuff, the long streaks of the high ones, and a
    /// fine grain for the moon's face, the milky band and the mountains.
    /// </summary>
    static ImageTexture CloudNoise()
    {
        const int N = 512;
        var data = new byte[N * N * 4];
        var layers = new (float freq, int octaves, int seed)[] { (.0125f, 5, 11), (.03f, 4, 23), (.009f, 5, 37), (.045f, 3, 41) };
        for (int c = 0; c < 4; c++)
        {
            var (freq, octaves, seed) = layers[c];
            var noise = new FastNoiseLite
            {
                NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Seed = seed, Frequency = freq,
                FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = octaves,
            };
            var img = noise.GetSeamlessImage(N, N);
            if (img.GetFormat() != Image.Format.L8) img.Convert(Image.Format.L8);
            var px = img.GetData();
            for (int i = 0; i < N * N; i++) data[i * 4 + c] = px[i];
        }
        var packed = Image.CreateFromData(N, N, false, Image.Format.Rgba8, data);
        packed.GenerateMipmaps();
        return ImageTexture.CreateFromImage(packed);
    }

    /// <summary>Holds the sky and the light at a time of day and a darkness (tests, staged scenes), shown at once; or, eased,
    /// gone over to the way they go over to the sim's.</summary>
    public void Pin(float day, float dark, bool eased = false) { _pin = (day, dark); _snap |= !eased; }

    /// <summary>Lets the sky and the light follow the sim's day again, shown at once.</summary>
    public void Unpin() { _pin = null; _snap = true; }

    /// <summary>Shows the weather, the day and the dark as they are now, without easing into them.</summary>
    public void Snap() => _snap = true;

    /// <summary>Lightning towards a direction: the clouds that way light up, and the air a little, for a moment. The moment
    /// is the eye's, not the game's, as the bolt's own flare is: lightning seen just as the game is stopped dies away all
    /// the same.</summary>
    public void Flash(Vector3 towards, float power)
    {
        _flashDir = towards.Normalized();
        _flashAt = _t;
        _flashPower = power;
    }

    /// <summary>Lightning at a spot of the world (scene units), seen from wherever the camera is.</summary>
    public void FlashAt(Vector3 at, float power)
    {
        var d = at - (GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero);
        d.Y = 0;
        Flash((d.LengthSquared() < 1e-4f ? Vector3.Forward : d.Normalized()) + Vector3.Up * .45f, power);
    }

    /// <summary>How bright the lightning is now: a stroke and two flickers after it, over a second or so. A calm run holds it
    /// at its stroke, so that a screenshot of it comes out the same every time.</summary>
    float FlashNow()
    {
        double t = _flashAt < -1e8 ? -1 : _calm ? 0 : _t - _flashAt;
        if (t < 0 || t > 1.5) return 0;
        static float Pulse(double t, double at, double len) => t < at ? 0 : (float)Math.Exp(-(t - at) / len);
        return _flashPower * Math.Min(1.2f, Pulse(t, 0, .07) + .55f * Pulse(t, .12, .05) + .85f * Pulse(t, .27, .1));
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
        // a bolt out of the sky lights the clouds over it
        sim.Struck += (x, y, z) => FlashAt(new Vector3(_w.WX(x), _w.WY(y), _w.WZ(z)), 1);
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
            LightSpecular = 0,
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
            bool now = _calm || _snap;
            float k = now ? 1 : (float)delta / 5;
            _wet = Mathf.MoveToward(_wet, _sim.Weather == Weather.Rain ? 1 : 0, k);
            _fog = Mathf.MoveToward(_fog, _sim.Weather == Weather.Fog ? 1 : 0, k);
            _blood = Mathf.MoveToward(_blood, _sim.Time < _sim.BloodMoonUntil ? 1 : 0, now ? 1 : (float)delta / 2);
            _rain.Emitting = _wet > .3f;
            _rain.AmountRatio = Math.Clamp(_wet, .05f, 1);
        }
        if (_sim != null)
        {
            var wv = _sim.Waves;
            // a staged scene stops the clock far out: it keeps the afternoon light it always had
            float day = _pin?.day ?? (wv.Countdown > Waves.Prep * 2 ? .7f : wv.Day), dark = _pin?.dark ?? wv.Darkness, k = (float)delta / 4;
            if (_day < 0 || _calm || _snap) { _day = day; _dark = dark; }
            else
            {
                // a wave beaten back starts the day over in the dark: the morning is taken up at once, under the moon's
                // light, and the dawn comes in the dark's own time; easing back over the sky would bring the sun up over
                // the south and down again in the night
                _day = day < _day - .5f ? day : Mathf.MoveToward(_day, day, Math.Max(k, Math.Abs(day - _day) * k * 2));
                _dark = Mathf.MoveToward(_dark, dark, k);
            }
            _snap = false;
            // a shot of the trailer sets its own light, and keeps to it
            if (DayOverride >= 0) _day = DayOverride;
            if (DarkOverride >= 0) _dark = DarkOverride;
            Light(_day, _dark);
            Heavens(_day, _dark, (float)delta);
        }
        float t = _calm ? 0 : (float)_t;
        // a torch a witch put out is dark till it is lit again
        if (_sim != null) foreach (var (f, l) in _sconces) l.Visible = f.DarkUntil <= _sim.Time;
        foreach (var (l, ph) in _torches)
        {
            float base0 = _sconces.Count > 0 && l.OmniRange < 4 ? 1.6f : 2.4f;
            l.LightEnergy = base0 * LampBoost * (.8f + .22f * MathF.Sin(t * 9 + ph) + .1f * MathF.Sin(t * 23 + ph * 2));
        }
    }

    // the day's sun at morning, noon and sunset, and the night's moon
    static readonly Color Morning = new("f0cfae"), Noon = new("eee0cc"), Evening = new("ffb066"), Sunset = new("ff6c3c"), Moon = new("7d92d8"), BloodMoon = new("d8341f");
    /// <summary>The moon stands high in the south-west all night.</summary>
    static readonly Vector3 MoonDir = new Vector3(-.4f, .72f, .48f).Normalized();
    /// <summary>How far the red moon of the spell is in the sky, eased.</summary>
    float _blood;

    /// <summary>
    /// The light for a time of day (0 dawn, ½ noon, 1 sunset) and a darkness (0 day, 1 night): where the sun stands and
    /// what colour it is, how much the sky lights, the fog, how bright the lamps.
    /// </summary>
    void Light(float day, float dark)
    {
        // up in the east at dawn, high in the south at noon, down in the west at sunset: the horde comes out of the sunset
        float az = MathF.PI * day * .92f + .12f, el = Mathf.DegToRad(10 + 48 * MathF.Sin(MathF.PI * Math.Clamp(day, .04f, .96f)));
        var dir = new Vector3(MathF.Cos(az) * MathF.Cos(el), MathF.Sin(el), MathF.Sin(az) * MathF.Cos(el) * .7f + .35f);
        _sunDir = dir.Normalized();
        var col = day < .2f ? Morning.Lerp(Noon, day / .2f) : day < .72f ? Noon : day < .93f ? Noon.Lerp(Evening, (day - .72f) / .21f) : Evening.Lerp(Sunset, (day - .93f) / .07f);
        float energy = day < .12f ? Mathf.Lerp(.9f, 1.42f, day / .12f) : day > .9f ? Mathf.Lerp(1.42f, 1.0f, (day - .9f) / .1f) : 1.42f;
        dir = dir.Normalized().Lerp(MoonDir, dark).Normalized();
        // under the red moon of the spell the night is lit, and lit red
        _sun.LightColor = col.Lerp(Moon.Lerp(BloodMoon, _blood), dark);
        _sun.LightEnergy = Mathf.Lerp(energy, .3f + .45f * _blood, dark);
        _sun.LookAtFromPosition(dir * 20, Vector3.Zero, Mathf.Abs(dir.Y) > .98f ? Vector3.Forward : Vector3.Up);
        _rim.LightEnergy = Mathf.Lerp(.55f, .22f, dark);
        _sun.LightSpecular = _rim.LightSpecular = Perspective ? 0 : 1;
        // rain greys the light and dims it; fog comes in close, pale by day and dark at night
        // and lightning lights everything for a moment
        Env.AmbientLightEnergy = Mathf.Lerp(.82f, .27f + .2f * _blood, dark) * (1 - .18f * _wet) + .35f * FlashNow();
        _sun.LightEnergy *= 1 - .45f * _wet - .3f * _fog;
        var fogDay = new Color(0.114f, 0.086f, 0.149f).Lerp(new Color(.34f, .35f, .38f), _fog * .8f + _wet * .3f);
        Env.FogLightColor = fogDay.Lerp(new Color(.03f, .035f, .07f), dark);
        Env.FogDepthBegin = Mathf.Lerp(84, 22, _fog) - 20 * _wet + FogShift;
        Env.FogDepthEnd = Mathf.Lerp(170, 80, _fog) - 30 * _wet + FogShift * .6f;
        Env.FogDensity = Mathf.Lerp(.55f, .82f, Math.Max(Math.Max(_fog, _wet * .5f), Haze));
        var amb = new Vector3(0.254f, 0.246f, 0.578f).Lerp(new Vector3(.09f, .11f, .3f).Lerp(new Vector3(.42f, .07f, .06f), _blood), dark);
        if (Math.Max(Math.Abs(amb.X - _ambSky.X), Math.Max(Math.Abs(amb.Y - _ambSky.Y), Math.Abs(amb.Z - _ambSky.Z))) > 1f / 512)
        {
            _ambSky = amb;
            _skyMat.SetShaderParameter("amb_sky", amb);
        }
        LampBoost = 1 + .7f * dark;
    }

    /// <summary>A sky at one moment (linear colours): overhead, at the horizon, the glow over the horizon round the sun, and
    /// how much of the pink band there is opposite it.</summary>
    readonly record struct Tone(Vector3 Zenith, Vector3 Horizon, Vector3 Glow, float Belt)
    {
        public Tone To(Tone o, float t) => new(Zenith.Lerp(o.Zenith, t), Horizon.Lerp(o.Horizon, t), Glow.Lerp(o.Glow, t), Mathf.Lerp(Belt, o.Belt, t));
    }

    // the sky through the day the siege keeps, muted and leaning to violet like the valley's dusk: the morning, the noon,
    // the evening light, the sunset and the grey-rose daybreak with the sun on the horizon, the night, the blood moon's
    static readonly Tone DawnSky = new(new(.09f, .11f, .25f), new(.38f, .31f, .33f), new(.22f, .12f, .07f), .12f),
        NoonSky = new(new(.075f, .13f, .31f), new(.30f, .34f, .43f), new(.05f, .04f, .03f), 0),
        EveningSky = new(new(.08f, .10f, .25f), new(.42f, .33f, .27f), new(.28f, .14f, .05f), .08f),
        SunsetSky = new(new(.05f, .045f, .13f), new(.55f, .22f, .10f), new(.65f, .2f, .05f), .4f),
        DaybreakSky = new(new(.035f, .04f, .11f), new(.34f, .19f, .18f), new(.42f, .16f, .08f), .3f),
        NightSky = new(new(.0035f, .005f, .014f), new(.016f, .02f, .04f), Vector3.Zero, 0),
        BloodSky = new(new(.016f, .002f, .005f), new(.115f, .015f, .014f), new(.06f, .004f, 0), 0);

    static float Smooth(float t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

    static Vector3 Lin(Color c) { var l = c.SrgbToLinear(); return new Vector3(l.R, l.G, l.B); }

    /// <summary>
    /// What the sky is to paint this frame, told to it in the sky_* globals: where the sun stands as the sky shows it (going
    /// down under the horizon with the dusk and coming up out of it with the dawn, while the light itself glides over to
    /// the moon), the colours of the air from the palette by the sun's height and the hour, greyed by the rain and paled
    /// by the fog; the sunlight the clouds are lit by; the stars, the moon, the clouds' drift on the wind and how much of
    /// the sky they cover; the lightning; and how the camera looks, for the diorama's backdrop. Someone's eyes see the fog
    /// in the colour of the horizon, so the far end of the valley goes into the sky without a seam.
    /// </summary>
    void Heavens(float day, float dark, float dt)
    {
        float az = MathF.Atan2(_sunDir.Z, _sunDir.X), el = MathF.Asin(Math.Clamp(_sunDir.Y, -1, 1)) - Mathf.DegToRad(28) * dark;
        var sun = new Vector3(MathF.Cos(az) * MathF.Cos(el), MathF.Sin(el), MathF.Sin(az) * MathF.Cos(el));
        float e = Mathf.RadToDeg(el);
        bool morning = day < .5f;

        var tone = day < .25f ? DawnSky.To(NoonSky, Smooth(day / .25f)) : day < .62f ? NoonSky : NoonSky.To(EveningSky, Smooth((day - .62f) / .33f));
        tone = tone.To(morning ? DaybreakSky : SunsetSky, Smooth((12 - e) / 12)).To(NightSky, Smooth((1 - e) / 12));
        tone = tone.To(BloodSky, _blood * (.3f + .7f * dark));
        // rain greys it all and hides the glow; fog flattens it into its own pale grey, dark at night
        float grey = .2126f * tone.Horizon.X + .7152f * tone.Horizon.Y + .0722f * tone.Horizon.Z;
        tone = tone.To(new Tone(new Vector3(.62f, .65f, .72f) * grey * .6f, new Vector3(.8f, .82f, .87f) * grey * .78f, Vector3.Zero, 0), _wet * .85f);
        var fogSky = new Vector3(.5f, .52f, .56f).Lerp(new Vector3(.02f, .024f, .04f), Smooth(dark * 1.4f));
        tone = tone with { Horizon = tone.Horizon.Lerp(fogSky, _fog * .8f), Zenith = tone.Zenith.Lerp(tone.Horizon.Lerp(fogSky, _fog * .8f), _fog * .65f) };

        // the sunlight on the clouds and in the disc: white by day, gold low down, red at the horizon, gone a little under it
        var warm = morning ? Morning : Evening;
        var tint = e > 25 ? Noon : e > 8 ? warm.Lerp(Noon, (e - 8) / 17) : e > 0 ? Sunset.Lerp(warm, e / 8) : Sunset;
        var light = Lin(tint) * .62f * Smooth((e + 7) / 7) * (1 - .8f * _wet) * (1 - .6f * _fog) * (1 - .5f * _blood * dark);
        // the moon comes out once the sun is down: in the dusk it is only a pale ghost of itself
        float moon = Smooth((dark - .3f) / .5f);
        float stars = Smooth((-e - 2) / 10) * (1 - .95f * _wet) * (1 - .9f * _fog);
        float haze = .35f + .65f * Math.Max(_wet, _fog);

        // the clouds drift on the wind, which the weather blows harder or softer, as the grass feels it
        float windAim = _sim.Weather switch { Weather.Wind => 2.3f, Weather.Rain => 1.45f, Weather.Fog => .45f, _ => 1 };
        _cloudWind += (windAim - _cloudWind) * (1 - MathF.Exp(-dt * .5f));
        if (!_calm) { _skyT += dt; _drift += new Vector2(.0042f, .0013f) * _cloudWind * dt; }
        float cover = Mathf.Lerp(Mathf.Lerp(.27f, .6f, _fog), .97f, _wet) + (_sim.Weather == Weather.Wind ? .1f : 0);

        // sheet lightning in the clouds of a night of rain, now and then while the game goes on, and its thunder a few
        // seconds after, as the flash is seen: the thunder of one seen comes even if the game is stopped
        if (!_calm && _wet > .6f && dark > .5f)
        {
            if (_nextSheet <= 0) _nextSheet = _sim.Time + 4 + 10 * _rnd.NextDouble();
            else if (_sim.Time >= _nextSheet)
            {
                double a = _rnd.NextDouble() * Math.Tau;
                Flash(new Vector3((float)Math.Cos(a), .22f + .3f * (float)_rnd.NextDouble(), (float)Math.Sin(a)), .45f + .55f * (float)_rnd.NextDouble());
                _thunderAt = _t + 2 + 4 * _rnd.NextDouble();
                _thunderLoud = .2f + .4f * _flashPower;
                _nextSheet = _sim.Time + 6 + 14 * _rnd.NextDouble();
            }
        }
        else _nextSheet = 0;
        if (_thunderAt >= 0 && _t >= _thunderAt) { _thunderAt = -1; Thunder?.Invoke(_thunderLoud); }

        // how the camera looks: someone's eyes look along their rays; the diorama's backdrop is made from its turn and tilt
        var cam = GetViewport().GetCamera3D();
        bool eyes = cam != null && cam.Projection == Camera3D.ProjectionType.Perspective;
        var fwd = cam != null ? -cam.GlobalBasis.Z : Vector3.Forward;
        var vp = GetViewport().GetVisibleRect().Size;

        void G(SkyGlobal g, Vector4 v) { RenderingServer.GlobalShaderParameterSet(g.Id, v); Told[g.Name] = v; }
        G(SkySun, new Vector4(sun.X, sun.Y, sun.Z, (1 - .85f * _wet) * (1 - .7f * _fog)));
        G(SkyMoon, new Vector4(MoonDir.X, MoonDir.Y, MoonDir.Z, _blood));
        G(SkyMood, new Vector4(day, dark, _wet, _fog));
        G(SkyZenith, new Vector4(tone.Zenith.X, tone.Zenith.Y, tone.Zenith.Z, stars));
        G(SkyHorizon, new Vector4(tone.Horizon.X, tone.Horizon.Y, tone.Horizon.Z, haze));
        G(SkyGlow, new Vector4(tone.Glow.X, tone.Glow.Y, tone.Glow.Z, tone.Belt) * (1 - .9f * _wet) * (1 - .8f * _fog));
        G(SkyLight, new Vector4(light.X, light.Y, light.Z, moon * (1 - .7f * _wet) * (1 - .6f * _fog)));
        G(SkyView, new Vector4(MathF.Atan2(fwd.X, fwd.Z), MathF.Asin(Math.Clamp(-fwd.Y, -1, 1)), eyes ? 1 : 0, vp.X / Math.Max(1, vp.Y)));
        G(SkyFlash, new Vector4(_flashDir.X, _flashDir.Y, _flashDir.Z, FlashNow()));
        G(SkyClock, new Vector4((float)_skyT, _drift.X, _drift.Y, cover));

        // from someone's eyes the fog is the air at the horizon (the environment takes its colours as sRGB)
        if (eyes)
        {
            var h = new Color(tone.Horizon.X, tone.Horizon.Y, tone.Horizon.Z).LinearToSrgb();
            Env.FogLightColor = h;
        }
    }
}
