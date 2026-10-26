using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

/// <summary>
/// The map of valleys as it is looked at and walked on (Atlas.cs makes its land): a world of its own, drawn into a
/// viewport of its own over the whole screen, with its own sky, its sun at a fair afternoon, its own camera — that of
/// «Heroes», looking down at the land from the south at a slant, moved by the screen's edges, the keys and a dragged
/// mouse, brought nearer and farther by the wheel. What is not seen lies dark under drifting cloud; the cells' sides are a
/// faint line on the ground, brighter about the cell under the pointer, gold about the one chosen; smoke goes up from the
/// chimneys and the lairs' fires, the ghost fires of the shrines burn green. Pointed at, a cell or a thing says what it
/// is in a plate by the pointer; a small map in the corner shows all of it and where the eye is, and takes the eye where
/// it is clicked. The figures that walk the map (the heroes, the horde's armies) are put under <see cref="Figures"/>; a
/// click on the ground is told by <see cref="Clicked"/>, in the map's voxels.
/// </summary>
public partial class AtlasView : Node3D
{
    public Atlas Atlas;
    public Camera3D Cam;
    /// <summary>Where the figures that walk the map are put (in the map's own world).</summary>
    public Node3D Figures;
    /// <summary>A click on the map's ground that was not a drag: where (the map's voxels x, z) and with which button.</summary>
    public event Action<Vector2, MouseButton> Clicked;
    public bool IsOpen { get; private set; }
    /// <summary>The cell under the pointer, the one chosen (drawn gold), the party a treasure's keepers are weighed against.</summary>
    public MapCell Hovered { get; private set; }
    public MapCell Chosen;
    public MapParty Party;
    /// <summary>How long the map took to make and to mesh, ms.</summary>
    public double BuildMs, MeshMs;
    /// <summary>The visual layer the figures of the map are put on (its world is its own, so any layer would show).</summary>
    public const uint Layer = 1u << 19;

    Campaign _c;
    /// <summary>The land the view was meshed of.</summary>
    Atlas _shown;
    CanvasLayer _layer, _ui;
    SubViewportContainer _box;
    SubViewport _vp;
    Node3D _world, _fx;
    WorldView _view;
    DirectionalLight3D _sun;
    Godot.Environment _env;
    Image _fogImg;
    ImageTexture _fogTex;
    byte[] _fogPx;
    string _seenKey = "";
    MultiMeshInstance3D _clouds;
    ShaderMaterial _cloudMat;
    PanelContainer _tip;
    Label _tipText;
    Minimap _mini;
    readonly Dictionary<MapCell, (Holder owner, bool taken, bool outpost)> _was = new();

    // the camera: the point of the ground it looks at (the map's voxels), how far off it is (scene units), where it is going
    Vector2 _look, _lookTo;
    float _dist = 22, _distTo = 22;
    const float Pitch = 54 * MathF.PI / 180, Fov = 30, Nearest = 3.2f;
    /// <summary>How far off (0 nearest, 1 farthest) the land is drawn at its coarse level.</summary>
    public static float CoarseFrom = .55f;
    float _farthest = 48;
    // a drag of the view: the button, where it began, the ground point taken hold of, whether it moved enough to be one
    MouseButton _dragBy;
    Vector2 _dragFrom;
    Vector3 _held;
    bool _dragged;
    bool _hadRoot3D;
    double _caughtAt;

    /// <summary>The fog's grid: a texel for so many voxels.</summary>
    const int FogStep = 4;

    public override void _Ready()
    {
        // (under the game's frame, which draws the map's own over it, as over a valley)
        _layer = new CanvasLayer { Layer = -1, Visible = false };
        AddChild(_layer);
        _box = new SubViewportContainer { Stretch = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        _box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_box);
        _vp = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Disabled, ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        _box.AddChild(_vp);
        _world = new Node3D();
        _vp.AddChild(_world);
        Figures = new Node3D { Name = "Figures" };
        _world.AddChild(Figures);
        _fx = new Node3D();
        _world.AddChild(_fx);
        Cam = new Camera3D { Fov = Fov, Near = .05f, Far = 400 };
        _world.AddChild(Cam);
        Heavens();
        _ui = new CanvasLayer { Layer = 2, Visible = false };
        AddChild(_ui);
        // the small map in the lower left corner, clear of the frame's heroes down the right
        _mini = new Minimap(this);
        _mini.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _mini.OffsetLeft = 16; _mini.OffsetRight = 16 + 236; _mini.OffsetTop = -16 - 214; _mini.OffsetBottom = -16;
        _ui.AddChild(_mini);
        _tip = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _tip.AddThemeStyleboxOverride("panel", Plate());
        _tipText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(250, 0) };
        _tipText.AddThemeFontOverride("font", Ornament.Serif);
        _tipText.AddThemeFontSizeOverride("font_size", 15);
        _tipText.AddThemeColorOverride("font_color", Ornament.Parchment);
        _tip.AddChild(_tipText);
        _ui.AddChild(_tip);
    }

    static StyleBoxFlat Plate() => new()
    {
        BgColor = Ornament.Fade(Ornament.Iron, .94f), BorderColor = Ornament.Fade(Ornament.Bronze, .7f),
        BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 9,
    };

    /// <summary>The map's sky and light: a clear afternoon, the colours a little fuller than a valley's, the edges of the map lost in a dark haze.</summary>
    void Heavens()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color("5f86b8"), SkyHorizonColor = new Color("b9c9d6"), GroundHorizonColor = new Color("2a3038"),
            GroundBottomColor = new Color("0b0d12"), SunAngleMax = 20,
        };
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky, RadianceSize = Sky.RadianceSizeEnum.Size64 },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 1.0f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.12f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.28f,
            AdjustmentContrast = 1.06f,
            GlowEnabled = true,
            GlowIntensity = .5f,
            GlowHdrThreshold = 1.1f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            SsaoEnabled = true,
            SsaoRadius = .6f,
            SsaoIntensity = 1.4f,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = new Color("6d7c93"),
            FogDensity = .35f,
            FogDepthBegin = 60,
            FogDepthEnd = 140,
            FogSkyAffect = 0,
        };
        _world.AddChild(new WorldEnvironment { Environment = _env });
        _sun = new DirectionalLight3D
        {
            LightColor = new Color("fff0d8"), LightEnergy = 1.55f, ShadowEnabled = true, ShadowBias = .03f, ShadowNormalBias = 1,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits, DirectionalShadowMaxDistance = 60,
        };
        _world.AddChild(_sun);
        _sun.LookAtFromPosition(new Vector3(-12, 18, 8), Vector3.Zero, Vector3.Up);
        var fill = new DirectionalLight3D { LightColor = new Color("8fa3d8"), LightEnergy = .35f, ShadowEnabled = false };
        _world.AddChild(fill);
        fill.LookAtFromPosition(new Vector3(10, 8, -12), Vector3.Zero, Vector3.Up);
    }

    // ------------------------------------------------------------ opened and closed

    /// <summary>
    /// The map shown over everything for a campaign: made the first time (and for another campaign), else the cells whose
    /// holder or treasure changed since it was last shown put on again; the valley under it is no longer drawn.
    /// </summary>
    public void Open(Campaign c)
    {
        // (the game makes the land and gives it before it opens the map; made here otherwise)
        if (Atlas == null || Atlas.Campaign != c)
        {
            var sw = Stopwatch.StartNew();
            Atlas = VoxelTower.Core.Atlas.Build(c);
            BuildMs = sw.Elapsed.TotalMilliseconds;
        }
        if (_view == null || _shown != Atlas) Show(c);
        else CatchUp();
        if (IsOpen) return;
        IsOpen = true;
        _layer.Visible = _ui.Visible = true;
        _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        Cam.MakeCurrent();
        var root = GetTree().Root;
        _hadRoot3D = !root.Disable3D;
        root.Disable3D = true;
        Fog(force: true);
        _mini.QueueRedraw();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _layer.Visible = _ui.Visible = false;
        _tip.Visible = false;
        _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        if (_hadRoot3D) GetTree().Root.Disable3D = false;
        Hovered = null;
    }

    /// <summary>The map's land meshed for a campaign, its fog, clouds, smoke and fires laid; the eye on the first valley.</summary>
    void Show(Campaign c)
    {
        _c = c;
        _shown = Atlas;
        _view?.QueueFree();
        _view = new WorldView { Async = false };
        _world.AddChild(_view);
        _view.Init(Atlas.World);
        MeshMs = _view.LastFullMeshMs;
        var w = Atlas.World;
        _fogImg = Image.CreateEmpty(Atlas.SX / FogStep, Atlas.SZ / FogStep, false, Image.Format.L8);
        _fogPx = new byte[_fogImg.GetWidth() * _fogImg.GetHeight()];
        _fogTex = ImageTexture.CreateFromImage(_fogImg);
        var solid = _view.SolidMaterial;
        solid.SetShaderParameter("atlas", new Vector4(Atlas.OX, Atlas.OZ, Atlas.CellR, 1));
        solid.SetShaderParameter("atlas_size", new Vector2(Atlas.SX, Atlas.SZ));
        solid.SetShaderParameter("atlas_fog", _fogTex);
        var water = _view.WaterMaterial;
        water.SetShaderParameter("atlas", new Vector4(Atlas.OX, Atlas.OZ, Atlas.CellR, 1));
        water.SetShaderParameter("atlas_size", new Vector2(Atlas.SX, Atlas.SZ));
        water.SetShaderParameter("atlas_fog", _fogTex);
        // (the map's water is a few voxels deep where a valley's is metres: it is drawn as if it were deep)
        water.SetShaderParameter("deep_color", new Vector3(.012f, .06f, .15f));
        water.SetShaderParameter("shallow_color", new Vector3(.04f, .19f, .3f));
        water.SetShaderParameter("clarity", 10f);
        water.SetShaderParameter("map_size", new Vector2(Atlas.SX, Atlas.SZ));
        water.SetShaderParameter("flow_tex", Flow());
        Remember();
        Effects();
        _seenKey = "";
        Fog(force: true);
        var (hx, hz) = Atlas.Center(c.Home ?? c.Cells[0]);
        _look = _lookTo = new Vector2(hx, hz + 8);
        _farthest = Atlas.SX * VoxelWorld.Vox * .5f / MathF.Tan(MathF.Atan(MathF.Tan(Fov / 2 * MathF.PI / 180) * 16 / 9f)) * .95f;
        _dist = _distTo = 18;
        Aim();
    }

    /// <summary>The cells whose holder or treasure is no longer what the map shows, put on again; the effects laid again if any were.</summary>
    void CatchUp()
    {
        bool any = false;
        foreach (var c in _c.Cells)
        {
            if (_was.TryGetValue(c, out var was) && was == (c.Owner, c.TroveTaken, c.Outpost)) continue;
            Atlas.Refresh(c);
            any = true;
        }
        if (!any) return;
        Remember();
        _view.RemeshDirty(int.MaxValue);
        _view.RemeshShadows();
        Effects();
    }

    /// <summary>A cell put on again at once, the campaign's change shown (the flow calls it when a cell is taken while the map is open).</summary>
    public void Refresh(MapCell c)
    {
        if (Atlas == null) return;
        Atlas.Refresh(c);
        Remember();
        _view.RemeshDirty(int.MaxValue);
        _view.RemeshShadows();
        Effects();
    }

    void Remember()
    {
        _was.Clear();
        foreach (var c in _c.Cells) _was[c] = (c.Owner, c.TroveTaken, c.Outpost);
    }

    /// <summary>The way the rivers run, for the water to ripple along them: still elsewhere.</summary>
    ImageTexture Flow()
    {
        int sx = Atlas.SX, sz = Atlas.SZ;
        var px = new byte[sx * sz * 2];
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                var (fx, fz) = Atlas.FlowAt(x, z);
                int i = (z * sx + x) * 2;
                px[i] = (byte)Math.Clamp(128 + fx * 1.4f / 6 * 127, 0, 255);
                px[i + 1] = (byte)Math.Clamp(128 + fz * 1.4f / 6 * 127, 0, 255);
            }
        return ImageTexture.CreateFromImage(Image.CreateFromData(sx, sz, false, Image.Format.Rg8, px));
    }

    // ------------------------------------------------------------ smoke, fire, clouds

    void Effects()
    {
        foreach (var n in _fx.GetChildren()) n.QueueFree();
        var w = Atlas.World;
        foreach (var (x, y, z) in Atlas.Smokes) _fx.AddChild(Smoke(new Vector3(w.WX(x + .5f), w.WY(y), w.WZ(z + .5f))));
        foreach (var (x, y, z, ghost) in Atlas.Fires)
        {
            _fx.AddChild(new OmniLight3D
            {
                Position = new Vector3(w.WX(x + .5f), w.WY(y + 1.5f), w.WZ(z + .5f)),
                LightColor = ghost ? new Color("7dffc2") : new Color("ff8a3a"), LightEnergy = ghost ? 1.1f : 1.0f, OmniRange = .8f, LightSpecular = 0,
            });
        }
        Clouds();
    }

    static GpuParticles3D Smoke(Vector3 at)
    {
        var pm = new ParticleProcessMaterial
        {
            Direction = new Vector3(.3f, 1, 0), Spread = 12, InitialVelocityMin = .18f, InitialVelocityMax = .3f, Gravity = new Vector3(.05f, .02f, 0),
            ScaleMin = .9f, ScaleMax = 1.4f, Color = new Color(.78f, .78f, .8f, .55f),
            ScaleCurve = new CurveTexture { Curve = Rising() }, ColorRamp = new GradientTexture1D { Gradient = Fading() },
        };
        var quad = new QuadMesh { Size = new Vector2(.22f, .22f) };
        quad.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true, AlbedoTexture = Puff(),
        };
        return new GpuParticles3D { Position = at, Amount = 10, Lifetime = 3.5, ProcessMaterial = pm, DrawPass1 = quad, VisibilityAabb = new Aabb(new Vector3(-2, -1, -2), new Vector3(4, 4, 4)) };
    }

    static Curve Rising() { var c = new Curve(); c.AddPoint(new Vector2(0, .4f)); c.AddPoint(new Vector2(1, 1.6f)); return c; }
    static Gradient Fading() { var g = new Gradient(); g.SetColor(0, new Color(1, 1, 1, .7f)); g.SetColor(1, new Color(1, 1, 1, 0)); return g; }

    static ImageTexture _puff;
    /// <summary>A soft round puff, made once.</summary>
    static ImageTexture Puff()
    {
        if (_puff != null) return _puff;
        const int N = 32;
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = new Vector2(x - N / 2f + .5f, y - N / 2f + .5f).Length() / (N / 2f);
                img.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp(1 - d, 0, 1) * Mathf.Clamp(1 - d, 0, 1)));
            }
        return _puff = ImageTexture.CreateFromImage(img);
    }

    /// <summary>A sheet of cloud over each cell in the fog and over the land off the field, high over its peaks.</summary>
    void Clouds()
    {
        _clouds?.QueueFree();
        _clouds = null;
        var w = Atlas.World;
        var sheets = new List<Transform3D>();
        foreach (var c in _c.Cells)
        {
            var (cx, cz) = Atlas.Center(c);
            float y = w.WY(Math.Max(Atlas.TopOf(c), Atlas.Ground + 10) + 4);
            float size = Atlas.CellR * 2.4f * VoxelWorld.Vox;
            sheets.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(size, 1, size)), new Vector3(w.WX(cx), y, w.WZ(cz))));
        }
        _cloudMat ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/atlas_cloud.gdshader") };
        _cloudMat.SetShaderParameter("atlas_fog", _fogTex);
        _cloudMat.SetShaderParameter("atlas_size", new Vector2(Atlas.SX, Atlas.SZ));
        _cloudMat.SetShaderParameter("to_vox", new Vector3(w.OriginX, w.OriginZ, 1 / VoxelWorld.Vox));
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new PlaneMesh { Size = Vector2.One } };
        mm.InstanceCount = sheets.Count;
        for (int i = 0; i < sheets.Count; i++) mm.SetInstanceTransform(i, sheets[i]);
        _clouds = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = _cloudMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _fx.AddChild(_clouds);
    }

    // ------------------------------------------------------------ the fog

    /// <summary>
    /// The fog laid again when what is seen changed: each texel the cell it lies in seen or not, softened over a few
    /// texels; off the field the land seen as far as the cells beside it are, fading into the dark toward the map's edge.
    /// </summary>
    void Fog(bool force = false)
    {
        if (Atlas == null) return;
        var key = new System.Text.StringBuilder(_c.Cells.Count);
        foreach (var c in _c.Cells) key.Append(Seen(c) ? '1' : '0');
        string k = key.ToString();
        if (!force && k == _seenKey) return;
        _seenKey = k;
        int fw = _fogImg.GetWidth(), fh = _fogImg.GetHeight();
        var raw = new float[fw * fh];
        for (int j = 0; j < fh; j++)
            for (int i = 0; i < fw; i++)
            {
                int x = i * FogStep + FogStep / 2, z = j * FogStep + FogStep / 2;
                var cell = Atlas.CellOfColumn(x, z) ?? Atlas.CellAt(x, z) ?? NearestCell(x, z);
                float v = cell != null && Seen(cell) ? 1 : 0;
                if (!Atlas.OnField(x, z))
                {
                    // the land off the field fades out toward the edge of the map
                    float e = MathF.Min(MathF.Min(x, Atlas.SX - x), MathF.Min(z, Atlas.SZ - z));
                    v *= Mathf.SmoothStep(4, Atlas.Margin * .75f, e);
                }
                raw[j * fw + i] = v;
            }
        // softened: a box blur, twice
        var tmp = new float[raw.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            for (int j = 0; j < fh; j++)
                for (int i = 0; i < fw; i++)
                {
                    float s = 0; int n = 0;
                    for (int d = -2; d <= 2; d++) { int ii = Math.Clamp(i + d, 0, fw - 1); s += raw[j * fw + ii]; n++; }
                    tmp[j * fw + i] = s / n;
                }
            for (int j = 0; j < fh; j++)
                for (int i = 0; i < fw; i++)
                {
                    float s = 0; int n = 0;
                    for (int d = -2; d <= 2; d++) { int jj = Math.Clamp(j + d, 0, fh - 1); s += tmp[jj * fw + i]; n++; }
                    raw[j * fw + i] = s / n;
                }
        }
        for (int i = 0; i < raw.Length; i++) _fogPx[i] = (byte)Math.Clamp(raw[i] * 255, 0, 255);
        _fogImg.SetData(fw, fh, false, Image.Format.L8, _fogPx);
        _fogTex.Update(_fogImg);
        _mini?.QueueRedraw();
    }

    MapCell NearestCell(int x, int z)
    {
        MapCell best = null; float bd = float.MaxValue;
        foreach (var c in _c.Cells)
        {
            var (cx, cz) = Atlas.Center(c);
            float d = (cx - x) * (cx - x) + (cz - z) * (cz - z);
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    /// <summary>Whether a cell is seen now (the campaign's own fog; all of it when the campaign reveals the map).</summary>
    public bool Seen(MapCell c) => _c != null && (_c.RevealAll || _c.Seen(c));

    // ------------------------------------------------------------ where things are

    /// <summary>The scene's point over a column of the map, at the height one stands at there.</summary>
    public Vector3 ToScene(float x, float z)
    {
        var w = Atlas.World;
        return new Vector3(w.WX(x), w.WY(Atlas.GroundAt((int)MathF.Floor(x), (int)MathF.Floor(z))), w.WZ(z));
    }

    /// <summary>The eye brought over a point of the map (the map's voxels), smoothly.</summary>
    public void Focus(float x, float z) => _lookTo = Clamp(new Vector2(x, z));

    /// <summary>The eye at a point and a distance at once (pictures, tests).</summary>
    public void Snap(float x, float z, float dist)
    {
        _look = _lookTo = Clamp(new Vector2(x, z));
        _dist = _distTo = Math.Clamp(dist, Nearest, _farthest);
        Aim();
    }

    /// <summary>How far off the eye is, from the nearest (0) to the farthest (1).</summary>
    public float Zoom => (_dist - Nearest) / (_farthest - Nearest);

    Vector2 Clamp(Vector2 p) => new(Math.Clamp(p.X, Atlas.Margin * .6f, Atlas.SX - Atlas.Margin * .6f), Math.Clamp(p.Y, Atlas.Margin * .4f, Atlas.SZ - Atlas.Margin * .3f));

    void Aim()
    {
        var w = Atlas.World;
        float gy = w.WY(Atlas.Ground);
        var target = new Vector3(w.WX(_look.X), gy, w.WZ(_look.Y));
        var back = new Vector3(0, MathF.Sin(Pitch), MathF.Cos(Pitch));
        Cam.Position = target + back * _dist;
        Cam.LookAt(target, Vector3.Up);
        _sun.DirectionalShadowMaxDistance = _dist * 2.2f + 10;
    }

    /// <summary>The ground under a point of the screen: the first voxel the ray from the eye meets, in the map's voxels.</summary>
    public bool GroundUnder(Vector2 screen, out Vector3 vox)
    {
        vox = default;
        if (Atlas == null) return false;
        var w = Atlas.World;
        var o = Cam.ProjectRayOrigin(screen);
        var d = Cam.ProjectRayNormal(screen);
        double ox = (o.X - w.OriginX) / VoxelWorld.Vox, oy = o.Y / VoxelWorld.Vox, oz = (o.Z - w.OriginZ) / VoxelWorld.Vox;
        if (!Raycast.Cast(w, ox, oy, oz, d.X, d.Y, d.Z, out var hit)) return false;
        vox = new Vector3(hit.X + .5f, hit.Y + 1, hit.Z + .5f);
        return true;
    }

    // ------------------------------------------------------------ the eye moved

    public override void _Process(double delta)
    {
        if (!IsOpen || Atlas == null) return;
        float dt = (float)Math.Min(delta, .1);
        var vp = GetViewport();
        var size = vp.GetVisibleRect().Size;
        var mouse = vp.GetMousePosition();
        // the keys and the screen's edges
        var pan = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) pan.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) pan.X += 1;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) pan.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) pan.Y += 1;
        if (DisplayServer.WindowIsFocused() && _dragBy == MouseButton.None && !_mini.GetGlobalRect().HasPoint(mouse))
        {
            const float Edge = 6;
            if (mouse.X <= Edge) pan.X -= 1;
            if (mouse.X >= size.X - Edge) pan.X += 1;
            if (mouse.Y <= Edge) pan.Y -= 1;
            if (mouse.Y >= size.Y - Edge) pan.Y += 1;
        }
        if (pan != Vector2.Zero) _lookTo = Clamp(_lookTo + pan.Normalized() * dt * (40 + _dist * 14));
        _look = _look.Lerp(_lookTo, 1 - MathF.Exp(-dt * 10));
        _dist = Mathf.Lerp(_dist, _distTo, 1 - MathF.Exp(-dt * 9));
        Aim();
        Fog();
        // (a cell the game laid anew while the map is open: its smoke and fires, and what it remembers, caught up)
        if ((_caughtAt -= delta) <= 0)
        {
            _caughtAt = .5;
            foreach (var c in _c.Cells)
                if (!_was.TryGetValue(c, out var was) || was != (c.Owner, c.TroveTaken, c.Outpost)) { CatchUp(); break; }
        }
        Hover(_hoverHold ?? mouse);
        var w = Atlas.World;
        var hover = Hovered != null ? Atlas.Center(Hovered) : (-1e5f, -1e5f);
        var pick = Chosen != null ? Atlas.Center(Chosen) : (-1e5f, -1e5f);
        _view.SolidMaterial.SetShaderParameter("atlas_hover", new Vector4(hover.Item1, hover.Item2, pick.Item1, pick.Item2));
        // (from far off, the land drawn a level coarser: a quarter of the faces in an eighth of the draws)
        _view.ChooseLevel(Zoom > CoarseFrom ? 1 : 4, false);
        _view.RemeshDirty(64);
        _view.RemeshShadows();
        _view.RemeshFar();
        _mini.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsOpen || Atlas == null) return;
        if (e is InputEventMouseButton b)
        {
            if (b.Pressed && b.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                float k = b.ButtonIndex == MouseButton.WheelUp ? .82f : 1.22f;
                float to = Math.Clamp(_distTo * k, Nearest, _farthest);
                // toward the point under the pointer as it comes nearer
                if (k < 1 && GroundUnder(b.Position, out var at)) _lookTo = Clamp(_lookTo.Lerp(new Vector2(at.X, at.Z), .22f));
                _distTo = to;
                GetViewport().SetInputAsHandled();
                return;
            }
            if (b.ButtonIndex is MouseButton.Left or MouseButton.Right or MouseButton.Middle)
            {
                if (b.Pressed)
                {
                    _dragBy = b.ButtonIndex;
                    _dragFrom = b.Position;
                    _dragged = false;
                    _held = Plane(b.Position);
                }
                else if (b.ButtonIndex == _dragBy)
                {
                    _dragBy = MouseButton.None;
                    if (!_dragged && b.ButtonIndex != MouseButton.Middle && GroundUnder(b.Position, out var at))
                        Clicked?.Invoke(new Vector2(at.X, at.Z), b.ButtonIndex);
                }
                GetViewport().SetInputAsHandled();
            }
        }
        else if (e is InputEventMouseMotion m && _dragBy != MouseButton.None)
        {
            if (!_dragged && (m.Position - _dragFrom).Length() > 6) _dragged = true;
            // the left button drags too, as on any map; the ground taken hold of stays under the pointer
            if (_dragged)
            {
                var now = Plane(m.Position);
                var w = Atlas.World;
                var moved = new Vector2((_held.X - now.X) / VoxelWorld.Vox, (_held.Z - now.Z) / VoxelWorld.Vox);
                _look = _lookTo = Clamp(_look + moved);
                Aim();
                _held = Plane(m.Position);
            }
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventKey k && k.Pressed && k.Keycode is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right)
            GetViewport().SetInputAsHandled();
    }

    /// <summary>The point of the level ground's plane under a point of the screen (scene units).</summary>
    Vector3 Plane(Vector2 screen)
    {
        var o = Cam.ProjectRayOrigin(screen);
        var d = Cam.ProjectRayNormal(screen);
        float gy = Atlas.World.WY(Atlas.Ground);
        if (MathF.Abs(d.Y) < 1e-4f) return o;
        float t = (gy - o.Y) / d.Y;
        return o + d * t;
    }

    // ------------------------------------------------------------ what is under the pointer

    /// <summary>The pointer held at a point of the screen, whatever the mouse does (staged scenes); null, the mouse again.</summary>
    public void HoverAt(Vector2? screen) => _hoverHold = screen;
    Vector2? _hoverHold;
    (MapCell, AtlasThing, Holder, bool, bool, MapParty) _told;

    void Hover(Vector2 mouse)
    {
        MapCell cell = null;
        AtlasThing thing = null;
        bool overUi = _mini.GetGlobalRect().HasPoint(mouse) || _dragBy != MouseButton.None || mouse.X < 0 || mouse.Y < 0;
        if (!overUi && GroundUnder(mouse, out var at))
        {
            cell = Atlas.CellAt(at.X, at.Z);
            float best = 9 * 9;
            foreach (var t in Atlas.Things)
            {
                float d = (t.X - at.X) * (t.X - at.X) + (t.Z - at.Z) * (t.Z - at.Z);
                if (d < best) { best = d; thing = t; }
            }
            if (thing != null) cell = thing.Cell;
        }
        Hovered = cell;
        if (cell == null) { _tip.Visible = false; _told = default; return; }
        // (said again only when the pointer comes over something else, or what is there changed)
        var tell = (cell, thing, cell.Owner, cell.TroveTaken, _c.Seen(cell) || _c.RevealAll, Party);
        if (!tell.Equals(_told)) { _tipText.Text = Describe(cell, thing); _told = tell; }
        _tip.Visible = true;
        _tip.ResetSize();
        var size = GetViewport().GetVisibleRect().Size;
        var p = mouse + new Vector2(20, 18);
        if (p.X + _tip.Size.X > size.X - 8) p.X = mouse.X - _tip.Size.X - 16;
        if (p.Y + _tip.Size.Y > size.Y - 8) p.Y = mouse.Y - _tip.Size.Y - 12;
        _tip.Position = p;
    }

    /// <summary>What a cell (and the thing pointed at in it) is: its kind, whose it is, what stands there, who keeps its treasure and how they stand against the party chosen.</summary>
    public string Describe(MapCell c, AtlasThing thing = null)
    {
        string name = c.Home ? "Первая долина" : Upper(Valleys.Name(c.Kind));
        if (!Seen(c)) return $"{name}\nВ тумане. Подведите отряд ближе.";
        var lines = new List<string> { name };
        lines.Add(c.Owner switch { Holder.Ours => "Наша.", Holder.Horde => "Во власти орды.", _ => "Ничья." });
        string what = thing?.Kind switch
        {
            AtlasThingKind.Citadel => c.Owner == Holder.Horde ? "Цитадель взята ордой." : "Цитадель.",
            AtlasThingKind.Outpost => "Наша застава.",
            AtlasThingKind.OutpostPlace => "Место для заставы.",
            AtlasThingKind.Lair => "Логово орды за частоколом.",
            AtlasThingKind.HordeCamp => "Стан орды.",
            AtlasThingKind.TakenLair => "Сожжённое логово орды.",
            AtlasThingKind.Mine => "Рудники.",
            AtlasThingKind.PlaceOfPower => "Курган, место силы.",
            AtlasThingKind.DeadMine or AtlasThingKind.Ruins or AtlasThingKind.Den or AtlasThingKind.Shrine => Troves.Name(c.Trove) + ".",
            _ => null,
        };
        if (what != null) lines.Add(what);
        int foes = _c.Foes(c);
        if (foes > 0 && c.Owner != Holder.Ours && !Campaign.Untaken(c))
            lines.Add(c.Owner == Holder.Horde ? $"Гарнизон орды: {foes}." : $"Стерегут звери: {foes}.");
        if (c.Trove != TroveKind.None)
        {
            if (c.TroveTaken) lines.Add(c.Trove == TroveKind.Shrine ? "Испытание пройдено." : "Сундук уже взят.");
            else
            {
                var keep = c.Sum.Holders();
                lines.Add(c.Trove == TroveKind.Shrine ? $"Испытание: мертвецы придут {Campaign.TrialWaves} раза, всего {keep.Count}." : $"Стерегут: {MapBoard.Keepers(keep)}.");
                lines.Add($"Награда: {Campaign.RewardText(c)}.");
                if (Party is { Owner: Holder.Ours } && _c.Parties.Contains(Party) && MapBoard.Against(Party.Party.Members, c) is { Length: > 0 } against) lines.Add(against);
            }
        }
        double gold = Campaign.AwayGold(c);
        if (gold > 0) lines.Add($"Без вас платит {Fmt.Int(gold)} золота в минуту.");
        return string.Join("\n", lines);
    }

    static string Upper(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ------------------------------------------------------------ the small map in the corner

    /// <summary>The whole map small: each cell its kind's colour or the fog's, its holder's rim, the things as dots, the eye's ground as a frame; a click takes the eye there.</summary>
    sealed partial class Minimap : Control
    {
        readonly AtlasView _v;
        public Minimap(AtlasView v) { _v = v; MouseFilter = MouseFilterEnum.Stop; }

        static readonly Color Fog = new("17141b"), Ours = new("5b86c8"), Theirs = new("d24a33");

        static Color KindColor(ValleyKind k) => k switch
        {
            ValleyKind.First => new Color("7d9a52"), ValleyKind.Forest => new Color("3f6a3a"), ValleyKind.Mountain => new Color("8a8a92"),
            ValleyKind.Marsh => new Color("4f8a8a"), ValleyKind.Ore => new Color("9a7a52"), _ => new Color("a2a462"),
        };

        float Scale => MathF.Min((Size.X - 12) / _v.Atlas.SX, (Size.Y - 12) / _v.Atlas.SZ);
        Vector2 ToMini(float x, float z) => new Vector2(6, 6) + new Vector2(x, z) * Scale;

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), Ornament.Fade(Ornament.Iron, .92f));
            DrawRect(new Rect2(Vector2.Zero, Size), Ornament.Fade(Ornament.Bronze, .6f), false, 1);
            var a = _v.Atlas;
            if (a == null) return;
            foreach (var c in _v._c.Cells)
            {
                var hex = a.HexOf(c);
                var pts = new Vector2[6];
                var corners = hex.Corners();
                for (int i = 0; i < 6; i++) pts[i] = ToMini(corners[i].x, corners[i].z);
                bool seen = _v.Seen(c);
                DrawColoredPolygon(pts, seen ? KindColor(c.Kind) : Fog);
                var rim = c.Owner == Holder.Ours ? Ours : c.Owner == Holder.Horde ? Theirs : Ornament.Fade(Ornament.IronDark, .8f);
                for (int i = 0; i < 6; i++) DrawLine(pts[i], pts[(i + 1) % 6], seen || c.Owner == Holder.Ours ? rim : Ornament.Fade(Ornament.IronDark, .8f), c.Owner == Holder.None ? 1 : 2);
                if (c == _v.Chosen) for (int i = 0; i < 6; i++) DrawLine(pts[i], pts[(i + 1) % 6], Ornament.BronzeLight, 2);
            }
            foreach (var t in a.Things)
            {
                if (!_v.Seen(t.Cell)) continue;
                var col = t.Kind switch
                {
                    AtlasThingKind.Citadel or AtlasThingKind.Outpost => Ours,
                    AtlasThingKind.Lair or AtlasThingKind.HordeCamp => Theirs,
                    AtlasThingKind.DeadMine or AtlasThingKind.Ruins or AtlasThingKind.Den or AtlasThingKind.Shrine => t.Cell.TroveTaken ? Ornament.Faded : Ornament.EmberGlow,
                    _ => Ornament.Parchment,
                };
                DrawCircle(ToMini(t.X, t.Z), t.Kind is AtlasThingKind.Citadel or AtlasThingKind.Lair ? 3.5f : 2.2f, col);
            }
            // the ground the eye sees: the four corners of the screen on the level ground
            var size = _v.GetViewport().GetVisibleRect().Size;
            var w = a.World;
            var q = new Vector2[5];
            var at = new[] { Vector2.Zero, new Vector2(size.X, 0), size, new Vector2(0, size.Y) };
            for (int i = 0; i < 4; i++)
            {
                var p = _v.Plane(at[i]);
                q[i] = ToMini((p.X - w.OriginX) / VoxelWorld.Vox, (p.Z - w.OriginZ) / VoxelWorld.Vox);
                q[i] = new Vector2(Math.Clamp(q[i].X, 0, Size.X), Math.Clamp(q[i].Y, 0, Size.Y));
            }
            q[4] = q[0];
            DrawPolyline(q, Ornament.Parchment, 1);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (_v.Atlas == null) return;
            Vector2? at = e switch
            {
                InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } b => b.Position,
                InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } m => m.Position,
                _ => null,
            };
            if (at is not { } pos) return;
            var v = (pos - new Vector2(6, 6)) / Scale;
            _v.Focus(v.X, v.Y);
            AcceptEvent();
        }
    }
}
