using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The big world map as it is looked at, as the adventure map of «Heroes»: a land of hexes up to 144 × 144 read from
/// whatever gives their looks (IHexSource: the campaign, or a made-up world before it), drawn in a world of its own over
/// the whole screen. Near the eye it is made in voxels a piece at a time (HexTiles.cs) on the worker threads and put in
/// as each is ready, the pieces left behind let go; under them and from far off it is drawn plain (HexFar.cs), a flat
/// hexagon to a hex, so the land never shows a hole while the pieces come. The camera is that of «Heroes»: from the south
/// at a slant, moved by the screen's edges, the keys and a dragged mouse, brought nearer by the wheel from a walking view
/// of some sixteen hexes across to a strategic view of a whole region. The hexes' sides are a faint line on the ground,
/// brighter about the one under the pointer, gold about the one chosen; what is not seen lies in the dark and comes out
/// of it smoothly when seen; a small map in the corner shows all of it and takes the eye where it is clicked
/// (HexMapView.Fog.cs). The figures that walk it are put under <see cref="Figures"/> (HexMapFigures.cs); the way one
/// would go is laid out in arrows, green as far as it gets today (HexMapView.Marks.cs).
/// </summary>
public partial class HexMapView : Node3D
{
    public IHexSource Source { get; private set; }
    public HexMapLayout Layout { get; private set; }
    public Camera3D Cam;
    /// <summary>Where the figures are put: the map's world, in its scene units (a voxel <see cref="VoxelWorld.Vox"/>).</summary>
    public Node3D Figures;
    /// <summary>A click on the ground that was not a drag: where (the map's voxels x, z) and with which button.</summary>
    public event Action<Vector2, MouseButton> Clicked;
    /// <summary>The same click, as the hex it fell in (off the field it is not told).</summary>
    public event Action<int, int, MouseButton> ClickedHex;
    public bool IsOpen { get; private set; }
    /// <summary>The hex under the pointer, the one chosen (drawn gold); (−1, −1) none.</summary>
    public (int Col, int Row) Hovered { get; private set; } = (-1, -1);
    public (int Col, int Row) Chosen = (-1, -1);
    /// <summary>What the plate by the pointer says of a hex; by default its ground, its relief and its thing.</summary>
    public Func<int, int, string> Describe;
    /// <summary>Frames in which the map's own work took over 40 ms (one or two may be the machine's, not the map's).</summary>
    public int SlowWorkFrames;
    /// <summary>The eye drawn across the map by itself, voxels a second (to measure the drawing while it goes).</summary>
    public Vector2 AutoPan;
    /// <summary>The slowest frame since the map was opened, ms (pictures and the self-test read it).</summary>
    public double WorstFrameMs;

    CanvasLayer _layer, _ui;
    SubViewportContainer _box;
    SubViewport _vp;
    Node3D _world, _land;
    DirectionalLight3D _sun;
    Godot.Environment _env;
    PanelContainer _tip;
    Label _tipText;
    bool _hadRoot3D;

    // the camera: the point of the ground it looks at (the map's voxels), how far off it is (scene units), where it is going
    Vector2 _look, _lookTo;
    float _dist = 34, _distTo = 34;
    const float Pitch = 54 * MathF.PI / 180, Fov = 30, Nearest = 10;
    float _farthest = 120;
    /// <summary>How far off (scene units) the eye may be and the land still be made in voxels about it.</summary>
    public const float NearMost = 58;
    // a drag of the view
    MouseButton _dragBy;
    Vector2 _dragFrom;
    Vector3 _held;
    bool _dragged;

    static float V => VoxelWorld.Vox;

    public override void _Ready()
    {
        _layer = new CanvasLayer { Layer = -1, Visible = false };
        AddChild(_layer);
        _box = new SubViewportContainer { Stretch = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        _box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_box);
        _vp = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Disabled, ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        _box.AddChild(_vp);
        _world = new Node3D();
        _vp.AddChild(_world);
        _land = new Node3D { Scale = Vector3.One * V };
        _world.AddChild(_land);
        Figures = new Node3D { Name = "Figures" };
        _world.AddChild(Figures);
        Cam = new Camera3D { Fov = Fov, Near = .1f, Far = 600 };
        _world.AddChild(Cam);
        Heavens();
        _ui = new CanvasLayer { Layer = 2, Visible = false };
        AddChild(_ui);
        _tip = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _tip.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Ornament.Fade(Ornament.Iron, .94f), BorderColor = Ornament.Fade(Ornament.Bronze, .7f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 9,
        });
        _tipText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(230, 0) };
        _tipText.AddThemeFontOverride("font", Ornament.Serif);
        _tipText.AddThemeFontSizeOverride("font_size", 15);
        _tipText.AddThemeColorOverride("font_color", Ornament.Parchment);
        _tip.AddChild(_tipText);
        _ui.AddChild(_tip);
        MiniReady();
        MarksReady();
    }

    /// <summary>Parts of the drawing left out, to weigh what each costs (--perf-without=shadows,ssao,glow,water,trees).</summary>
    public void Without(IEnumerable<string> what)
    {
        foreach (var w in what)
            switch (w)
            {
                case "shadows": _noShadows = true; break;
                case "ssao": _env.SsaoEnabled = false; break;
                case "glow": _env.GlowEnabled = false; break;
            }
    }
    bool _noShadows;

    /// <summary>What the map's own viewport drew last frame: triangles (thousands), draw calls, and the shadows' triangles.</summary>
    public (long kTris, long draws, long kShadow) Drawn() => (
        _vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.PrimitivesInFrame) / 1000,
        _vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame),
        _vp.GetRenderInfo(Viewport.RenderInfoType.Shadow, Viewport.RenderInfo.PrimitivesInFrame) / 1000);

    /// <summary>The map's sky and light: a clear afternoon, the colours full as in «Heroes», the far land lost in haze.</summary>
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
            AdjustmentSaturation = 1.3f,
            AdjustmentContrast = 1.06f,
            GlowEnabled = true,
            GlowIntensity = .45f,
            GlowHdrThreshold = 1.1f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            // (no screen-space shade: the voxels' corners are shaded as they are meshed, and it cost a fifth of the frame)
            SsaoEnabled = false,
            FogEnabled = false,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = new Color("6d7c93"),
            FogDensity = .3f,
            FogDepthBegin = 140,
            FogDepthEnd = 360,
            FogSkyAffect = 0,
        };
        _world.AddChild(new WorldEnvironment { Environment = _env });
        // beyond the map's land: a dark plain of mist far under it, so the eye never looks into nothing
        _world.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(4000, 4000) }, Position = new Vector3(0, (HexTiles.WaterTop - 6) * V, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("0d1016"), Roughness = 1 }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        _sun = new DirectionalLight3D
        {
            LightColor = new Color("fff0d8"), LightEnergy = 1.55f, ShadowEnabled = true, ShadowBias = .03f, ShadowNormalBias = 1,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal, DirectionalShadowMaxDistance = 80,
        };
        _world.AddChild(_sun);
        _sun.LookAtFromPosition(new Vector3(-12, 18, 8), Vector3.Zero, Vector3.Up);
        var fill = new DirectionalLight3D { LightColor = new Color("8fa3d8"), LightEnergy = .35f, ShadowEnabled = false };
        _world.AddChild(fill);
        fill.LookAtFromPosition(new Vector3(10, 8, -12), Vector3.Zero, Vector3.Up);
    }

    // ------------------------------------------------------------ opened and closed

    /// <summary>The map of a source shown over everything: laid out, its far view made, the eye over a point of it (the map's voxels).</summary>
    public void Open(IHexSource src, float atX = -1, float atZ = -1)
    {
        if (Source != src)
        {
            if (Source != null) Source.Changed -= OnChanged;
            Source = src;
            Layout = new HexMapLayout(src.Width, src.Height);
            Source.Changed += OnChanged;
            Clear();
            Lay();
        }
        if (atX >= 0) { _look = _lookTo = Clamp(new Vector2(atX, atZ)); }
        else if (_look == Vector2.Zero) _look = _lookTo = new Vector2(Layout.SX / 2f, Layout.SZ / 2f);
        if (IsOpen) return;
        IsOpen = true;
        WorstFrameMs = WorstPutMs = WorstWorkMs = 0;
        SlowWorkFrames = 0;
        _layer.Visible = _ui.Visible = true;
        _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        Cam.MakeCurrent();
        var root = GetTree().Root;
        _hadRoot3D = !root.Disable3D;
        root.Disable3D = true;
        Aim();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _layer.Visible = _ui.Visible = false;
        _tip.Visible = false;
        _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        if (_hadRoot3D) GetTree().Root.Disable3D = false;
        Hovered = (-1, -1);
    }

    /// <summary>A map laid out: its materials told where the hexes are, its fog, its far view, the small map.</summary>
    void Lay()
    {
        _solid ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
        _water ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
        _farMat ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/world_far.gdshader") };
        var lay = Layout;
        var atlas = new Vector4(lay.OX, lay.OZ, HexMapLayout.R, 1);
        var size = new Vector2(lay.FogW * HexMapLayout.HalfW, lay.FogH * HexMapLayout.RowStep);
        FogReady();
        foreach (var m in new[] { _solid, _water })
        {
            m.SetShaderParameter("atlas", atlas);
            m.SetShaderParameter("atlas_size", size);
            m.SetShaderParameter("atlas_fog", _fogTex);
            m.SetShaderParameter("atlas_through", .04f);
            m.SetShaderParameter("atlas_dark", new Vector3(.012f, .014f, .02f));
        }
        _farMat.SetShaderParameter("atlas_dark", new Vector3(.012f, .014f, .02f));
        _farMat.SetShaderParameter("atlas_size", size);
        _farMat.SetShaderParameter("atlas_fog", _fogTex);
        _water.SetShaderParameter("deep_color", new Vector3(.015f, .1f, .3f));
        _water.SetShaderParameter("shallow_color", new Vector3(.07f, .3f, .48f));
        _water.SetShaderParameter("clarity", 2.2f);
        _water.SetShaderParameter("map_size", new Vector2(lay.SX, lay.SZ));
        var still = Image.CreateEmpty(4, 4, false, Image.Format.Rg8);
        still.Fill(new Color(.5f, .5f, 0));
        _water.SetShaderParameter("flow_tex", ImageTexture.CreateFromImage(still));
        FarLay();
        MiniLay();
        // as far off as the whole map fits on the screen, or a region of the largest
        float wide = lay.SX * V / (2 * MathF.Tan(MathF.Atan(MathF.Tan(Fov / 2 * MathF.PI / 180) * 16 / 9f)));
        _farthest = Math.Clamp(wide * .9f, 60, 170);
        _dist = _distTo = 34;
    }

    /// <summary>A hex's look or fog changed at the source: the pieces it lies in made again if its look did, its fog laid again.</summary>
    void OnChanged(int col, int row)
    {
        if (!Layout.OnField(col, row)) return;
        _fogDirty = true;
        LookChanged(col, row);
    }

    // ------------------------------------------------------------ where things are

    /// <summary>The scene's point over a column of the map (its voxels), at the height one stands at there.</summary>
    public Vector3 ToScene(float x, float z)
    {
        var c = HexTiles.ColumnAt(Source, Layout, (int)MathF.Floor(x), (int)MathF.Floor(z));
        return new Vector3(x * V, Math.Max(c.Top, c.Water) * V, z * V);
    }

    /// <summary>The middle of a hex in the scene, at its ground.</summary>
    public Vector3 HexToScene(int col, int row)
    {
        var (x, z) = Layout.Center(col, row);
        return ToScene(x, z);
    }

    public bool Seen(int col, int row) => Source != null && Layout.OnField(col, row) && Source.Seen(col, row);

    /// <summary>The eye brought over a point of the map (its voxels), smoothly.</summary>
    public void Focus(float x, float z) => _lookTo = Clamp(new Vector2(x, z));

    /// <summary>The eye over a hex, smoothly.</summary>
    public void FocusHex(int col, int row) { var (x, z) = Layout.Center(col, row); Focus(x, z); }

    /// <summary>The eye at a point and a distance at once (pictures, tests).</summary>
    public void Snap(float x, float z, float dist)
    {
        _wantKnown = false;
        _look = _lookTo = Clamp(new Vector2(x, z));
        _dist = _distTo = Math.Clamp(dist, Nearest, _farthest);
        Aim();
    }

    /// <summary>How far off the eye is (scene units), and from the nearest (0) to the farthest (1).</summary>
    public float Distance => _dist;
    public float Zoom => (_dist - Nearest) / (_farthest - Nearest);
    public Vector2 Look => _look;

    Vector2 Clamp(Vector2 p) => Layout == null ? p : new(Math.Clamp(p.X, HexMapLayout.W, Layout.SX - HexMapLayout.W), Math.Clamp(p.Y, HexMapLayout.RowStep, Layout.SZ - HexMapLayout.RowStep * .5f));

    void Aim()
    {
        if (Layout == null) return;
        float gy = HexTiles.Base * V;
        var target = new Vector3(_look.X * V, gy, _look.Y * V);
        var back = new Vector3(0, MathF.Sin(Pitch), MathF.Cos(Pitch));
        Cam.Position = target + back * _dist;
        Cam.LookAt(target, Vector3.Up);
        _sun.DirectionalShadowMaxDistance = _dist * 1.5f + 8;
        _sun.Visible = true;
        // the sun's shadows only where the land is made in voxels
        _sun.ShadowEnabled = _dist < NearMost && !_noShadows;
    }

    /// <summary>The ground under a point of the screen, the map's voxels: the ray from the eye to the ground's height there, found again a few times.</summary>
    public bool GroundUnder(Vector2 screen, out Vector2 vox)
    {
        vox = default;
        if (Layout == null) return false;
        var o = Cam.ProjectRayOrigin(screen);
        var d = Cam.ProjectRayNormal(screen);
        if (d.Y > -1e-4f) return false;
        float y = HexTiles.Base * V;
        Vector3 p = o;
        for (int i = 0; i < 4; i++)
        {
            float t = (y - o.Y) / d.Y;
            p = o + d * t;
            int vx = (int)MathF.Floor(p.X / V), vz = (int)MathF.Floor(p.Z / V);
            if (vx < 0 || vz < 0 || vx >= Layout.SX || vz >= Layout.SZ) break;
            var c = HexTiles.ColumnAt(Source, Layout, vx, vz);
            y = Math.Max(c.Top, c.Water) * V;
        }
        vox = new Vector2(p.X / V, p.Z / V);
        return vox.X >= 0 && vox.Y >= 0 && vox.X < Layout.SX && vox.Y < Layout.SZ;
    }

    /// <summary>The hex under a point of the screen, (−1, −1) off the field.</summary>
    public (int Col, int Row) HexUnder(Vector2 screen)
    {
        if (!GroundUnder(screen, out var at)) return (-1, -1);
        var h = Layout.HexAt(at.X, at.Y);
        return Layout.OnField(h.Col, h.Row) ? h : (-1, -1);
    }

    /// <summary>The point of the level ground's plane under a point of the screen (scene units).</summary>
    Vector3 Plane(Vector2 screen)
    {
        var o = Cam.ProjectRayOrigin(screen);
        var d = Cam.ProjectRayNormal(screen);
        float gy = HexTiles.Base * V;
        if (MathF.Abs(d.Y) < 1e-4f) return o;
        return o + d * ((gy - o.Y) / d.Y);
    }

    /// <summary>What part of the map's ground the screen shows (its voxels, a box about the four corners on the level ground).</summary>
    public Rect2 GroundShown()
    {
        var size = GetViewport().GetVisibleRect().Size;
        float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
        foreach (var at in new[] { Vector2.Zero, new Vector2(size.X, 0), size, new Vector2(0, size.Y) })
        {
            var p = Plane(at) / V;
            x0 = MathF.Min(x0, p.X); x1 = MathF.Max(x1, p.X); z0 = MathF.Min(z0, p.Z); z1 = MathF.Max(z1, p.Z);
        }
        return new Rect2(x0, z0, x1 - x0, z1 - z0);
    }

    // ------------------------------------------------------------ the eye moved, the land kept up

    public override void _Process(double delta)
    {
        if (!IsOpen || Layout == null) return;
        if (delta * 1000 > WorstFrameMs && Engine.GetFramesDrawn() > 10) WorstFrameMs = delta * 1000;
        float dt = (float)Math.Min(delta, .1);
        var vp = GetViewport();
        var size = vp.GetVisibleRect().Size;
        var mouse = vp.GetMousePosition();
        var pan = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) pan.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) pan.X += 1;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) pan.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) pan.Y += 1;
        if (DisplayServer.WindowIsFocused() && _dragBy == MouseButton.None && !OverMini(mouse) && _hoverHold == null)
        {
            const float Edge = 6;
            if (mouse.X <= Edge) pan.X -= 1;
            if (mouse.X >= size.X - Edge) pan.X += 1;
            if (mouse.Y <= Edge) pan.Y -= 1;
            if (mouse.Y >= size.Y - Edge) pan.Y += 1;
        }
        var work = Stopwatch.StartNew();
        if (pan != Vector2.Zero) _lookTo = Clamp(_lookTo + pan.Normalized() * dt * (_dist * 22 + 120));
        if (AutoPan != Vector2.Zero) _lookTo = _look = Clamp(_look + AutoPan * dt);
        _look = _look.Lerp(_lookTo, 1 - MathF.Exp(-dt * 10));
        _dist = Mathf.Lerp(_dist, _distTo, 1 - MathF.Exp(-dt * 9));
        Aim();
        Pieces();
        Fog(dt);
        Hover(_hoverHold ?? mouse);
        var hv = Layout.OnField(Hovered.Col, Hovered.Row) ? Layout.Center(Hovered.Col, Hovered.Row) : (-1e5f, -1e5f);
        var ch = Layout.OnField(Chosen.Col, Chosen.Row) ? Layout.Center(Chosen.Col, Chosen.Row) : (-1e5f, -1e5f);
        _solid.SetShaderParameter("atlas_hover", new Vector4(hv.Item1, hv.Item2, ch.Item1, ch.Item2));
        FarShow();
        _mini.QueueRedraw();
        if (Engine.GetFramesDrawn() > 10)
        {
            double ms = work.Elapsed.TotalMilliseconds;
            WorstWorkMs = Math.Max(WorstWorkMs, ms);
            if (ms > 40) SlowWorkFrames++;
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsOpen || Layout == null) return;
        if (e is InputEventMouseButton b)
        {
            if (b.Pressed && b.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                float k = b.ButtonIndex == MouseButton.WheelUp ? .84f : 1.2f;
                float to = Math.Clamp(_distTo * k, Nearest, _farthest);
                if (k < 1 && GroundUnder(b.Position, out var at)) _lookTo = Clamp(_lookTo.Lerp(at, .2f));
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
                    {
                        Clicked?.Invoke(at, b.ButtonIndex);
                        var h = Layout.HexAt(at.X, at.Y);
                        if (Layout.OnField(h.Col, h.Row)) ClickedHex?.Invoke(h.Col, h.Row, b.ButtonIndex);
                    }
                }
                GetViewport().SetInputAsHandled();
            }
        }
        else if (e is InputEventMouseMotion m && _dragBy != MouseButton.None)
        {
            if (!_dragged && (m.Position - _dragFrom).Length() > 6) _dragged = true;
            if (_dragged)
            {
                var now = Plane(m.Position);
                var moved = new Vector2((_held.X - now.X) / V, (_held.Z - now.Z) / V);
                _look = _lookTo = Clamp(_look + moved);
                Aim();
                _held = Plane(m.Position);
            }
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventKey k && k.Pressed && k.Keycode is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right)
            GetViewport().SetInputAsHandled();
    }

    // ------------------------------------------------------------ what is under the pointer

    /// <summary>The pointer held at a point of the screen whatever the mouse does (pictures); null, the mouse again.</summary>
    public void HoverAt(Vector2? screen) => _hoverHold = screen;
    Vector2? _hoverHold;
    (int, int, HexLook, bool) _told;

    void Hover(Vector2 mouse)
    {
        bool over = OverMini(mouse) || _dragBy != MouseButton.None || mouse.X < 0 || mouse.Y < 0;
        Hovered = over ? (-1, -1) : HexUnder(mouse);
        if (Hovered.Col < 0) { _tip.Visible = false; _told = default; return; }
        var (c, r) = Hovered;
        var look = Source.Look(c, r);
        bool seen = Source.Seen(c, r);
        var tell = (c, r, look, seen);
        if (!tell.Equals(_told)) { _tipText.Text = (Describe ?? Tell)(c, r); _told = tell; }
        _tip.Visible = true;
        _tip.ResetSize();
        var size = GetViewport().GetVisibleRect().Size;
        var p = mouse + new Vector2(20, 18);
        if (p.X + _tip.Size.X > size.X - 8) p.X = mouse.X - _tip.Size.X - 16;
        if (p.Y + _tip.Size.Y > size.Y - 8) p.Y = mouse.Y - _tip.Size.Y - 12;
        _tip.Position = p;
    }

    /// <summary>What a hex is, said plainly: its thing and whose it is, its land.</summary>
    public string Tell(int col, int row)
    {
        if (!Source.Seen(col, row)) return "В тумане. Подведите героя ближе.";
        var l = Source.Look(col, row);
        var lines = new List<string>();
        string thing = Name(l.Icon);
        if (thing != null) lines.Add(thing + (l.Owner == 1 ? ". Наше." : l.Owner == 2 ? ". Орды." : "."));
        string land = l.Ground switch
        {
            HexGround.Grass => "Луг", HexGround.Dirt => "Земля", HexGround.Sand => "Песок", HexGround.Snow => "Снег", HexGround.Swamp => "Болото",
            HexGround.Rough => "Каменистая земля", HexGround.Lava => "Лава", HexGround.Scorched => "Выжженная земля", HexGround.Water => "Озеро", _ => "Глубокая вода",
        };
        if (l.Relief == HexRelief.Forest) land += ", лес";
        else if (l.Relief == HexRelief.Hills) land += ", холмы";
        else if (l.Relief == HexRelief.Mountain) land = "Горы. Не пройти";
        if (l.Roads != 0) land += ", дорога";
        if (l.Rivers != 0) land += ", река";
        lines.Add(land + ".");
        return string.Join("\n", lines);
    }

    /// <summary>A thing of the map by name.</summary>
    public static string Name(HexIcon icon) => icon switch
    {
        HexIcon.Citadel => "Цитадель", HexIcon.Outpost => "Застава", HexIcon.OutpostPlace => "Место для заставы", HexIcon.HordeTown => "Город орды",
        HexIcon.Lair => "Логово орды", HexIcon.MineLogs => "Лесопилка", HexIcon.MineStone => "Каменоломня", HexIcon.MineMetal => "Рудник",
        HexIcon.MineGold => "Золотой рудник", HexIcon.MineMercury => "Лаборатория алхимика", HexIcon.MineSulfur => "Серная дюна",
        HexIcon.MineCrystal => "Кристальная пещера", HexIcon.MineGems => "Пруд самоцветов", HexIcon.Pile => "Ресурсы", HexIcon.Chest => "Сундук",
        HexIcon.Campfire => "Костёр", HexIcon.Windmill => "Мельница", HexIcon.WaterWheel => "Водяная мельница", HexIcon.Shrine => "Святилище",
        HexIcon.Dwelling => "Жилище", HexIcon.Monster => "Стража", HexIcon.DeadMine => "Заброшенный рудник", HexIcon.Ruins => "Руины",
        HexIcon.Den => "Логово зверей", HexIcon.Trial => "Святилище испытаний", HexIcon.Signpost => "Указатель", HexIcon.Learning => "Камень знаний",
        HexIcon.Stables => "Конюшня", HexIcon.Observatory => "Обсерватория", HexIcon.Pass => "Застава в проходе", _ => null,
    };

    public override void _ExitTree()
    {
        if (Source != null) Source.Changed -= OnChanged;
        Clear();
    }
}
