using System;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The orthographic diorama camera. Wheel, a pinch or two fingers up and down on a trackpad, PageUp/PageDown zoom;
/// middle button (or Alt + left) orbits, right button drags the map, WASD or arrows pan, Q/E turn,
/// Y looks straight down and back, H returns home (the keys as the player bound them). A blast or a great fall near what it looks at shakes it.
/// </summary>
public partial class CameraRig : Node3D
{
    public Camera3D Cam { get; private set; }
    /// <summary>Height of the interface along the bottom and top edges, in canvas pixels; the diorama centres between them.</summary>
    public float BottomInset = 0, TopInset = 0;

    const float Dist = 80f, ViewH = 15.5f, LookY = 4f;
    // the people of the valley are about two metres tall in a 128-metre valley: start closer, and let the view come closer still
    const float HomeYaw = -.66f, HomePitch = .62f, HomeZoom = 1f, MinZoom = .28f, MaxZoom = 8f;
    float _homeX = .4f, _homeZ = .3f;

    /// <summary>The part of the map the camera may look at, in world metres (x, z).</summary>
    public Rect2 Bounds = new(-11, -8, 22, 16);

    /// <summary>Where H returns the view to.</summary>
    public void HomeAt(float wx, float wz) { _homeX = wx; _homeZ = wz; Home(true); }

    /// <summary>Looking straight down on the valley, like on a map. Turning still works; the slant comes back with Y or H.</summary>
    public bool TopDown { get; private set; }
    const float Overhead = MathF.PI / 2;
    float _slant = HomePitch;

    public void ToggleTopDown()
    {
        if (TopDown) { TopDown = false; _pitch = _slant; return; }
        TopDown = true;
        _slant = _pitch;
        _pitch = Overhead;
    }

    /// <summary>How hard the view shakes now, nothing to all; it dies away by itself.</summary>
    float _trauma, _shakeT;

    /// <summary>Shakes the view, the more the harder; one jolt on another adds up, to a limit.</summary>
    public void Shake(float amount) { if (amount > 0) _trauma = Math.Min(1, _trauma + amount); }

    /// <summary>How hard the view is shaking now.</summary>
    public float Shaking => _trauma;

    float _tx, _tz, _yaw, _pitch, _zoom;     // where the camera is heading
    float _cx, _cz, _cyaw, _cpitch, _czoom;  // where it is now
    bool _rotating, _panning, _rmbMoved;
    Vector2 _rmbFrom;

    /// <summary>Right button released without dragging the map.</summary>
    public event Action<Vector2> RightClick;

    public override void _Ready()
    {
        Cam = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Near = .1f, Far = 400f,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
        };
        AddChild(Cam);
        GetWindow().MouseExited += () => _inside = false;
        GetWindow().FocusExited += () => _inside = false;
        Cam.MakeCurrent();
        Home(true);
    }

    public void Home(bool snap = false) { Set(_homeX, _homeZ, HomeYaw, HomePitch, HomeZoom, snap); Homes++; }

    /// <summary>How many times the view was sent home (the tutorial counts them).</summary>
    public int Homes;

    /// <summary>Where the camera is heading: its target, turn, tilt and zoom, as <see cref="Set"/> takes them.</summary>
    public (float x, float z, float yaw, float pitch, float zoom) Aim => (_tx, _tz, _yaw, _pitch, _zoom);

    public void Set(float tx, float tz, float yaw, float pitch, float zoom, bool snap)
    {
        (_tx, _tz, _yaw, _pitch, _zoom) = (ClampX(tx), ClampZ(tz), yaw, Math.Clamp(pitch, .35f, Overhead), Math.Clamp(zoom, MinZoom, MaxZoom));
        TopDown = _pitch > Overhead - .01f;
        if (!snap) return;
        (_cx, _cz, _cyaw, _cpitch, _czoom) = (_tx, _tz, _yaw, _pitch, _zoom);
        Apply();
    }

    /// <summary>Closer (factor over 1) or further.</summary>
    public void ZoomBy(float factor) => _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);

    /// <summary>Held: the view is someone's eyes now (first person), and the keys and the mouse do not move it.</summary>
    public bool Held;

    // whether the pointer is over the window (moved over it since it last left it), for the scrolling at its edges
    bool _inside;

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseMotion) _inside = true;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Held) return;
        // a trackpad: pinch, or two fingers up and down
        if (e is InputEventMagnifyGesture mg) { ZoomBy(mg.Factor); return; }
        if (e is InputEventPanGesture pg) { ZoomBy(MathF.Pow(1.07f, -pg.Delta.Y)); return; }
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) _zoom = Math.Clamp(_zoom * 1.12f, MinZoom, MaxZoom);
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) _zoom = Math.Clamp(_zoom / 1.12f, MinZoom, MaxZoom);
            else if (mb.ButtonIndex == MouseButton.Middle) _rotating = mb.Pressed;
            // the buttons as the player has them (Bindings.SwapMouse): the first with Alt turns, the second drags and sends
            else if (mb.ButtonIndex == Bindings.First && mb.AltPressed) _rotating = mb.Pressed;
            else if (mb.ButtonIndex == Bindings.First && !mb.Pressed) _rotating = false;
            else if (mb.ButtonIndex == Bindings.Second)
            {
                if (mb.Pressed) { _panning = true; _rmbMoved = false; _rmbFrom = mb.Position; }
                else { _panning = false; if (!_rmbMoved) RightClick?.Invoke(mb.Position); }
            }
        }
        else if (e is InputEventMouseMotion mm)
        {
            if (_rotating)
            {
                _yaw -= mm.Relative.X * .006f;
                if (!TopDown) _pitch = Math.Clamp(_pitch + mm.Relative.Y * .004f, .35f, 1.4f);
                _cyaw = _yaw; _cpitch = _pitch;
            }
            if (_panning)
            {
                if (!_rmbMoved && (mm.Position - _rmbFrom).Length() > 6) _rmbMoved = true;
                if (_rmbMoved) Pan(mm.Relative);
            }
        }
        else if (e is InputEventKey k && k.Pressed && !k.Echo && !k.CtrlPressed && !k.MetaPressed)
        {
            switch (Bindings.Find(k.PhysicalKeycode, Bindings.Scope.Field))
            {
                case Act.CamHome: Home(); break;
                case Act.CamTop: ToggleTopDown(); break;
                case Act.CamZoomIn: ZoomBy(1.3f); break;
                case Act.CamZoomOut: ZoomBy(1 / 1.3f); break;
                default: return;
            }
            GetViewport().SetInputAsHandled();
        }
    }

    void Pan(Vector2 d)
    {
        float k = Cam.Size / Math.Max(1f, GetViewport().GetVisibleRect().Size.Y);
        float fx = -MathF.Sin(_cyaw), fz = -MathF.Cos(_cyaw), rx = MathF.Cos(_cyaw), rz = -MathF.Sin(_cyaw), kk = k / MathF.Sin(_cpitch);
        _tx = ClampX(_tx - rx * d.X * k + fx * d.Y * kk);
        _tz = ClampZ(_tz - rz * d.X * k + fz * d.Y * kk);
        _cx = _tx; _cz = _tz;
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("CameraRig");
        float dt = (float)delta;
        float sp = 6f / _zoom * dt;
        float fx = -MathF.Sin(_yaw), fz = -MathF.Cos(_yaw), rx = MathF.Cos(_yaw), rz = -MathF.Sin(_yaw);
        float mx = 0, mz = 0;
        if (Held) goto settle;
        if (Down(Act.CamForward)) { mx += fx; mz += fz; }
        if (Down(Act.CamBack)) { mx -= fx; mz -= fz; }
        if (Down(Act.CamRight)) { mx += rx; mz += rz; }
        if (Down(Act.CamLeft)) { mx -= rx; mz -= rz; }
        // the pointer at an edge of the screen: the view goes that way, the nearer the edge the faster
        if (Settings.EdgeScroll && _inside && !_panning && !_rotating && Input.MouseMode == Input.MouseModeEnum.Visible && GetWindow().HasFocus())
        {
            var vp = GetViewport().GetVisibleRect().Size;
            var mp = GetViewport().GetMousePosition();
            const float band = 8;
            float l = band - mp.X, r = mp.X - (vp.X - band), t = band - mp.Y, b = mp.Y - (vp.Y - band);
            if (l > 0) { mx -= rx * Math.Min(1, l / band + .3f); mz -= rz * Math.Min(1, l / band + .3f); }
            if (r > 0) { mx += rx * Math.Min(1, r / band + .3f); mz += rz * Math.Min(1, r / band + .3f); }
            if (t > 0) { mx += fx * Math.Min(1, t / band + .3f); mz += fz * Math.Min(1, t / band + .3f); }
            if (b > 0) { mx -= fx * Math.Min(1, b / band + .3f); mz -= fz * Math.Min(1, b / band + .3f); }
        }
        if (mx != 0 || mz != 0) { _tx = ClampX(_tx + mx * sp); _tz = ClampZ(_tz + mz * sp); }
        if (Down(Act.CamTurnLeft)) _yaw += 1.6f * dt;
        if (Down(Act.CamTurnRight)) _yaw -= 1.6f * dt;

        settle:
        float a = 1 - MathF.Exp(-dt * 14f);
        _cx += (_tx - _cx) * a; _cz += (_tz - _cz) * a;
        _cyaw += (_yaw - _cyaw) * a; _cpitch += (_pitch - _cpitch) * a;
        _czoom += (_zoom - _czoom) * a;
        _trauma = Math.Max(0, _trauma - dt * 1.3f);
        _shakeT += dt;
        Apply();
    }

    /// <summary>A shaking, one way of it: three tremors over one another, from a slow sway to a rattle.</summary>
    static float Wobble(float t, float seed) => MathF.Sin(t * 31 + seed) * .55f + MathF.Sin(t * 47 + seed * 2.3f) * .3f + MathF.Sin(t * 83 + seed * 4.1f) * .15f;

    static bool Down(Act a) => Bindings.Held(a) && !Input.IsKeyPressed(Key.Ctrl) && !Input.IsKeyPressed(Key.Meta);

    void Apply()
    {
        if (Cam == null) return;
        var vp = GetViewport().GetVisibleRect().Size;
        float aspect = vp.X / Math.Max(1f, vp.Y - BottomInset - TopInset);
        Cam.Size = ViewH * Math.Max(1f, 1.45f / aspect) / _czoom;
        float cp = MathF.Cos(_cpitch), spn = MathF.Sin(_cpitch);
        var target = new Vector3(_cx, LookY, _cz);
        var pos = target + new Vector3(MathF.Sin(_cyaw) * cp * Dist, spn * Dist, MathF.Cos(_cyaw) * cp * Dist);
        // straight down, "up" on the screen is the way the camera was facing
        var up = cp < .02f ? new Vector3(-MathF.Sin(_cyaw), 0, -MathF.Cos(_cyaw)) : Vector3.Up;
        Cam.LookAtFromPosition(pos, target, up);
        Cam.VOffset = -(BottomInset - TopInset) * .5f * Cam.Size / Math.Max(1f, vp.Y);
        // shaking: the view jolted aside and turned a little, as the square of how hard, so a small jolt stays small
        float k = _trauma * _trauma * Settings.Shake;
        if (k > 1e-4f)
        {
            float amp = k * .035f * Cam.Size;
            Cam.HOffset = amp * Wobble(_shakeT, 0);
            Cam.VOffset += amp * Wobble(_shakeT, 1.7f);
            Cam.RotateObjectLocal(Vector3.Forward, k * .02f * Wobble(_shakeT, 3.1f));
        }
        else Cam.HOffset = 0;
    }

    /// <summary>The point of the map the camera is heading for, in world metres (x, z).</summary>
    public Vector2 Target => new(_tx, _tz);

    /// <summary>
    /// Puts a point at its own height (world metres) in the middle of the view: the camera looks at a plane near the
    /// ground, so a point high on a hill is aimed at where the line of sight through it meets that plane.
    /// </summary>
    public void SetAt(float wx, float wy, float wz, float yaw, float pitch, float zoom)
    {
        float s = (wy - LookY) / MathF.Max(.05f, MathF.Sin(pitch)), cp = MathF.Cos(pitch);
        Set(wx - s * MathF.Sin(yaw) * cp, wz - s * MathF.Cos(yaw) * cp, yaw, pitch, zoom, snap: true);
    }

    /// <summary>Looks at a point of the map, in world metres.</summary>
    public void Focus(float wx, float wz)
    {
        _tx = ClampX(wx);
        _tz = ClampZ(wz);
    }

    float ClampX(float x) => Math.Clamp(x, Bounds.Position.X, Bounds.End.X);
    float ClampZ(float z) => Math.Clamp(z, Bounds.Position.Y, Bounds.End.Y);

    /// <summary>Where the corners of the screen land on a horizontal plane at height y, in world metres.</summary>
    public Vector3[] GroundFootprint(float y)
    {
        var vp = GetViewport().GetVisibleRect().Size;
        var corners = new[] { new Vector2(0, TopInset), new Vector2(vp.X, TopInset), new Vector2(vp.X, vp.Y - BottomInset), new Vector2(0, vp.Y - BottomInset) };
        var pts = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            var o = Cam.ProjectRayOrigin(corners[i]); var d = Cam.ProjectRayNormal(corners[i]);
            float t = Math.Abs(d.Y) < 1e-4f ? 0 : (y - o.Y) / d.Y;
            pts[i] = o + d * t;
        }
        return pts;
    }

    /// <summary>A ray through a screen point, in world metres.</summary>
    public (Vector3 origin, Vector3 dir) Ray(Vector2 screen) => (Cam.ProjectRayOrigin(screen), Cam.ProjectRayNormal(screen));
}
