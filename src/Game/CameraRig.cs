using System;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The orthographic diorama camera. Wheel zooms, middle button (or Alt + left) orbits,
/// right button drags the map, WASD or arrows pan, Q/E turn, H returns home.
/// </summary>
public partial class CameraRig : Node3D
{
    public Camera3D Cam { get; private set; }
    /// <summary>Height of the interface along the bottom and top edges, in canvas pixels; the diorama centres between them.</summary>
    public float BottomInset = 0, TopInset = 0;

    const float Dist = 80f, ViewH = 15.5f, LookY = 4f;
    const float HomeYaw = -.66f, HomePitch = .62f, HomeZoom = .74f, MinZoom = .28f, MaxZoom = 5f;
    float _homeX = .4f, _homeZ = .3f;

    /// <summary>The part of the map the camera may look at, in world metres (x, z).</summary>
    public Rect2 Bounds = new(-11, -8, 22, 16);

    /// <summary>Where H returns the view to.</summary>
    public void HomeAt(float wx, float wz) { _homeX = wx; _homeZ = wz; Home(true); }

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
        Cam.MakeCurrent();
        Home(true);
    }

    public void Home(bool snap = false) => Set(_homeX, _homeZ, HomeYaw, HomePitch, HomeZoom, snap);

    public void Set(float tx, float tz, float yaw, float pitch, float zoom, bool snap)
    {
        (_tx, _tz, _yaw, _pitch, _zoom) = (ClampX(tx), ClampZ(tz), yaw, Math.Clamp(pitch, .35f, 1.4f), Math.Clamp(zoom, MinZoom, MaxZoom));
        if (!snap) return;
        (_cx, _cz, _cyaw, _cpitch, _czoom) = (_tx, _tz, _yaw, _pitch, _zoom);
        Apply();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) _zoom = Math.Clamp(_zoom * 1.12f, MinZoom, MaxZoom);
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) _zoom = Math.Clamp(_zoom / 1.12f, MinZoom, MaxZoom);
            else if (mb.ButtonIndex == MouseButton.Middle) _rotating = mb.Pressed;
            else if (mb.ButtonIndex == MouseButton.Left && mb.AltPressed) _rotating = mb.Pressed;
            else if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed) _rotating = false;
            else if (mb.ButtonIndex == MouseButton.Right)
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
                _pitch = Math.Clamp(_pitch + mm.Relative.Y * .004f, .35f, 1.4f);
                _cyaw = _yaw; _cpitch = _pitch;
            }
            if (_panning)
            {
                if (!_rmbMoved && (mm.Position - _rmbFrom).Length() > 6) _rmbMoved = true;
                if (_rmbMoved) Pan(mm.Relative);
            }
        }
        else if (e is InputEventKey k && k.Pressed && !k.Echo && (k.PhysicalKeycode == Key.H || k.PhysicalKeycode == Key.Home))
            Home();
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
        float dt = (float)delta;
        float sp = 6f / _zoom * dt;
        float fx = -MathF.Sin(_yaw), fz = -MathF.Cos(_yaw), rx = MathF.Cos(_yaw), rz = -MathF.Sin(_yaw);
        float mx = 0, mz = 0;
        if (Down(Key.W) || Down(Key.Up)) { mx += fx; mz += fz; }
        if (Down(Key.S) || Down(Key.Down)) { mx -= fx; mz -= fz; }
        if (Down(Key.D) || Down(Key.Right)) { mx += rx; mz += rz; }
        if (Down(Key.A) || Down(Key.Left)) { mx -= rx; mz -= rz; }
        if (mx != 0 || mz != 0) { _tx = ClampX(_tx + mx * sp); _tz = ClampZ(_tz + mz * sp); }
        if (Down(Key.Q)) _yaw += 1.6f * dt;
        if (Down(Key.E)) _yaw -= 1.6f * dt;

        float a = 1 - MathF.Exp(-dt * 14f);
        _cx += (_tx - _cx) * a; _cz += (_tz - _cz) * a;
        _cyaw += (_yaw - _cyaw) * a; _cpitch += (_pitch - _cpitch) * a;
        _czoom += (_zoom - _czoom) * a;
        Apply();
    }

    static bool Down(Key k) => Input.IsPhysicalKeyPressed(k) && !Input.IsKeyPressed(Key.Ctrl) && !Input.IsKeyPressed(Key.Meta);

    void Apply()
    {
        if (Cam == null) return;
        var vp = GetViewport().GetVisibleRect().Size;
        float aspect = vp.X / Math.Max(1f, vp.Y - BottomInset - TopInset);
        Cam.Size = ViewH * Math.Max(1f, 1.45f / aspect) / _czoom;
        float cp = MathF.Cos(_cpitch), spn = MathF.Sin(_cpitch);
        var target = new Vector3(_cx, LookY, _cz);
        var pos = target + new Vector3(MathF.Sin(_cyaw) * cp * Dist, spn * Dist, MathF.Cos(_cyaw) * cp * Dist);
        Cam.LookAtFromPosition(pos, target, Vector3.Up);
        Cam.VOffset = -(BottomInset - TopInset) * .5f * Cam.Size / Math.Max(1f, vp.Y);
    }

    /// <summary>The point of the map the camera is heading for, in world metres (x, z).</summary>
    public Vector2 Target => new(_tx, _tz);

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
