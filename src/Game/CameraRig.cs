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
    /// <summary>Width of the panel on the right, in canvas pixels; the diorama centres in what is left.</summary>
    public float PanelWidth = 380;

    const float Dist = 60f, ViewH = 15.5f, LookY = 3f;
    const float HomeTx = .4f, HomeTz = .2f, HomeYaw = -.66f, HomePitch = .62f, HomeZoom = .8f;

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

    public void Home(bool snap = false) => Set(HomeTx, HomeTz, HomeYaw, HomePitch, HomeZoom, snap);

    public void Set(float tx, float tz, float yaw, float pitch, float zoom, bool snap)
    {
        (_tx, _tz, _yaw, _pitch, _zoom) = (tx, tz, yaw, Math.Clamp(pitch, .35f, 1.4f), Math.Clamp(zoom, .5f, 5f));
        if (!snap) return;
        (_cx, _cz, _cyaw, _cpitch, _czoom) = (_tx, _tz, _yaw, _pitch, _zoom);
        Apply();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) _zoom = Math.Clamp(_zoom * 1.12f, .5f, 5f);
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) _zoom = Math.Clamp(_zoom / 1.12f, .5f, 5f);
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
        _tx = Math.Clamp(_tx - rx * d.X * k + fx * d.Y * kk, -11f, 11f);
        _tz = Math.Clamp(_tz - rz * d.X * k + fz * d.Y * kk, -8f, 8f);
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
        if (mx != 0 || mz != 0) { _tx = Math.Clamp(_tx + mx * sp, -11f, 11f); _tz = Math.Clamp(_tz + mz * sp, -8f, 8f); }
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
        var vp = GetViewport().GetVisibleRect().Size;
        float free = Math.Max(240f, vp.X - PanelWidth), aspect = free / Math.Max(1f, vp.Y);
        Cam.Size = ViewH * Math.Max(1f, 1.45f / aspect) / _czoom;
        float cp = MathF.Cos(_cpitch), spn = MathF.Sin(_cpitch);
        var target = new Vector3(_cx, LookY, _cz);
        var pos = target + new Vector3(MathF.Sin(_cyaw) * cp * Dist, spn * Dist, MathF.Cos(_cyaw) * cp * Dist);
        Cam.LookAtFromPosition(pos, target, Vector3.Up);
        Cam.HOffset = PanelWidth * .5f * Cam.Size / Math.Max(1f, vp.Y);
    }

    /// <summary>A ray through a screen point, in world metres.</summary>
    public (Vector3 origin, Vector3 dir) Ray(Vector2 screen) => (Cam.ProjectRayOrigin(screen), Cam.ProjectRayNormal(screen));
}
