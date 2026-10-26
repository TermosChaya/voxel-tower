using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The land of the map of valleys on the screen (Atlas.cs): a stand-in kept plain until the map's own land is drawn (the
/// fork «atlasland»). The field lies far below the valley, lit by a sun of its own that never sets; each cell coloured by
/// its kind, its crag, pond, river and roads, its borders drawn thin, the fog dark over what nobody of ours sees, what
/// holds a cell standing in its middle. Its camera looks down at a slant: the keys of the camera and a drag with the
/// second button move it, the wheel brings it closer. A click on the land is told with the spot of the land clicked
/// (<see cref="Clicked"/>); the figures walking it are put under <see cref="Figures"/>.
/// </summary>
public partial class AtlasView : Node3D
{
    public Atlas Atlas;
    public Camera3D Cam;
    public Node3D Figures;
    /// <summary>A click on the land (not a drag): the spot of the land, and the button.</summary>
    public event Action<Vector2, MouseButton> Clicked;
    /// <summary>What the map's land is drawn on: its own lights light this and nothing of the valley's.</summary>
    public const uint Layer = 1u << 19;
    /// <summary>Metres of the screen's world a voxel of the land is; where the land lies.</summary>
    const float U = .25f;
    static readonly Vector3 Base = new(0, -3000, 0);

    Campaign _c;
    float _tx, _tz, _dist = 60;
    const float Pitch = .92f;
    Vector2 _pressAt;
    MouseButton _pressed;
    bool _dragged;
    MeshInstance3D _ground, _fog;
    Node3D _things;
    double _fogAt;
    public bool IsOpen { get; private set; }

    public override void _Ready()
    {
        Visible = false;
        Position = Base;
        Cam = new Camera3D { Fov = 45, Near = .5f, Far = 2000, CullMask = Layer, Environment = Sky() };
        AddChild(Cam);
        var sun = new DirectionalLight3D { LightCullMask = Layer, ShadowEnabled = true, LightEnergy = 1.15f, LightColor = new Color(1f, .96f, .88f) };
        sun.RotationDegrees = new Vector3(-52, -35, 0);
        AddChild(sun);
        _things = new Node3D();
        AddChild(_things);
        Figures = new Node3D();
        AddChild(Figures);
    }

    static Godot.Environment Sky() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("2b3440"),
        AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(.7f, .72f, .76f), AmbientLightEnergy = .8f,
        TonemapMode = Godot.Environment.ToneMapper.Filmic,
    };

    public void Open(Campaign c)
    {
        if (Atlas?.Campaign != c) Atlas = Atlas.Build(c);
        if (_c != c || _ground == null) { _c = c; Draw(); }
        Visible = true;
        Cam.Current = true;
        IsOpen = true;
        Things();
        _fogAt = 0;
    }

    public void Close()
    {
        Visible = false;
        Cam.Current = false;
        IsOpen = false;
    }

    public Vector3 ToScene(float x, float z) => Base + new Vector3(x * U, Atlas.GroundAt((int)x, (int)z) * U, z * U);

    public bool Seen(MapCell c) => _c?.Seen(c) ?? true;

    public void Focus(float x, float z) { _tx = x; _tz = z; }

    // ------------------------------------------------------------ the camera

    public override void _Process(double delta)
    {
        if (!IsOpen) return;
        float dt = (float)delta, sp = _dist * 1.1f / U * dt;
        float mx = 0, mz = 0;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) mz -= 1;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) mz += 1;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) mx -= 1;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) mx += 1;
        _tx = Math.Clamp(_tx + mx * sp, 0, Atlas.World.SX);
        _tz = Math.Clamp(_tz + mz * sp, 0, Atlas.World.SZ);
        var at = ToScene(_tx, _tz) - Position;
        Cam.Position = at + new Vector3(0, MathF.Sin(Pitch), MathF.Cos(Pitch)) * _dist;
        Cam.LookAt(Position + at, Vector3.Up);
        if ((_fogAt -= delta) <= 0) { _fogAt = .5; Fog(); }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsOpen) return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) _dist = Math.Max(14, _dist / 1.12f);
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) _dist = Math.Min(160, _dist * 1.12f);
            else if (mb.Pressed) { _pressAt = mb.Position; _pressed = mb.ButtonIndex; _dragged = false; }
            else if (mb.ButtonIndex == _pressed)
            {
                _pressed = MouseButton.None;
                if (!_dragged && Ground(mb.Position) is { } g) Clicked?.Invoke(g, mb.ButtonIndex);
            }
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseMotion mm && _pressed is MouseButton.Right or MouseButton.Middle)
        {
            if (!_dragged && (mm.Position - _pressAt).Length() > 6) _dragged = true;
            if (_dragged)
            {
                float k = _dist / Math.Max(1f, GetViewport().GetVisibleRect().Size.Y) * 1.2f / U;
                _tx = Math.Clamp(_tx - mm.Relative.X * k, 0, Atlas.World.SX);
                _tz = Math.Clamp(_tz - mm.Relative.Y * k / MathF.Sin(Pitch), 0, Atlas.World.SZ);
            }
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>The spot of the land under a point of the screen; null off it.</summary>
    public Vector2? Ground(Vector2 screen)
    {
        var o = Cam.ProjectRayOrigin(screen); var d = Cam.ProjectRayNormal(screen);
        float y = (Base + new Vector3(0, 4 * U, 0)).Y;
        if (MathF.Abs(d.Y) < 1e-5f) return null;
        float t = (y - o.Y) / d.Y;
        if (t < 0) return null;
        var p = (o + d * t - Base) / U;
        if (p.X < 0 || p.Z < 0 || p.X >= Atlas.World.SX || p.Z >= Atlas.World.SZ) return null;
        return new Vector2(p.X, p.Z);
    }

    // ------------------------------------------------------------ the land drawn

    static Color KindColor(ValleyKind k) => k switch
    {
        ValleyKind.Ore => new Color("8a7155"), ValleyKind.Sacred => new Color("9c9a66"), ValleyKind.Forest => new Color("3d6b3c"),
        ValleyKind.Marsh => new Color("4f7a62"), ValleyKind.Mountain => new Color("7d7b80"), _ => new Color("6f9a4a"),
    };

    void Draw()
    {
        _ground?.QueueFree();
        int sx = Atlas.World.SX, sz = Atlas.World.SZ, g = 3;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int z = 0; z < sz; z += g)
            for (int x = 0; x < sx; x += g)
            {
                var cell = Atlas.CellAt(x + g / 2f, z + g / 2f);
                if (cell == null) continue;
                int mx = x + g / 2, mz = z + g / 2;
                var col = KindColor(cell.Kind);
                float cost = Atlas.Cost(mx, mz);
                if (!Atlas.Walkable(mx, mz)) col = cell.Kind == ValleyKind.Mountain ? new Color("5c5a60") : new Color("3c6f9c");
                else if (cost < .7f) col = new Color("c8ad7a");
                else if (cost > 2) col = new Color("7fa6c2");
                // the border of a cell darker
                var (cx, cz) = Atlas.Center(cell);
                float r = MathF.Sqrt((mx - cx) * (mx - cx) + (mz - cz) * (mz - cz));
                if (Atlas.CellAt(mx + g, mz) != cell || Atlas.CellAt(mx - g, mz) != cell || Atlas.CellAt(mx, mz + g) != cell || Atlas.CellAt(mx, mz - g) != cell)
                    col = col.Darkened(.28f);
                else col = col.Lightened(.05f * (1 - r / Atlas.R));
                st.SetColor(col);
                Vector3 a = new(x * U, 4 * U, z * U), b = new((x + g) * U, 4 * U, z * U), c = new((x + g) * U, 4 * U, (z + g) * U), d = new(x * U, 4 * U, (z + g) * U);
                st.SetNormal(Vector3.Up);
                st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
                st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
            }
        _ground = new MeshInstance3D { Mesh = st.Commit(), Layers = Layer, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .95f, CullMode = BaseMaterial3D.CullModeEnum.Disabled } };
        AddChild(_ground);
    }

    /// <summary>What holds each cell, standing in its middle: the citadel, an outpost's tower, the horde's lair; a treasure's chest.</summary>
    void Things()
    {
        foreach (var n in _things.GetChildren()) n.QueueFree();
        foreach (var t in Atlas.Things)
        {
            var (size, col) = t.Kind switch
            {
                AtlasThingKind.Citadel => (new Vector3(2.6f, 3.6f, 2.6f), new Color("9d9ba0")),
                AtlasThingKind.Lair => (new Vector3(3.4f, 1.8f, 3.4f), new Color("7a2c22")),
                AtlasThingKind.Outpost => (new Vector3(1.4f, 2.8f, 1.4f), new Color("a39d90")),
                _ => (new Vector3(1, .8f, .7f), new Color("c9a23a")),
            };
            var box = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Layers = Layer,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = col, Roughness = .8f } };
            box.Position = ToScene(t.X, t.Z) - Position + new Vector3(0, size.Y / 2, 0);
            _things.AddChild(box);
        }
    }

    /// <summary>The fog over the cells nobody of ours sees: a dark hexagon over each.</summary>
    void Fog()
    {
        _fog?.QueueFree();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int n = 0;
        foreach (var c in _c.Cells)
        {
            if (Seen(c)) continue;
            n++;
            var (cx, cz) = Atlas.Center(c);
            var mid = new Vector3(cx * U, 6 * U, cz * U);
            for (int i = 0; i < 6; i++)
            {
                float a0 = MathF.PI / 180 * (60 * i - 30), a1 = MathF.PI / 180 * (60 * i + 30);
                st.AddVertex(mid);
                st.AddVertex(mid + new Vector3(MathF.Cos(a0), 0, MathF.Sin(a0)) * Atlas.R * U);
                st.AddVertex(mid + new Vector3(MathF.Cos(a1), 0, MathF.Sin(a1)) * Atlas.R * U);
            }
        }
        _fog = null;
        if (n == 0) return;
        _fog = new MeshInstance3D { Mesh = st.Commit(), Layers = Layer, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.08f, .08f, .11f, .7f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled } };
        AddChild(_fog);
    }
}
