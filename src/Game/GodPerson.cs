using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The player in the household's god (Sim.Deities.cs): the view from its eyes, forty-odd metres over the valley, the
/// mouse turning its head and the keys walking it a stride at a time. The left button sweeps its right hand along the
/// ground before it (with Shift its left), the right brings its fist down where it looks, E tears up a lump of the land
/// and throws it where it looks, Space roars. While the god is up and the player is not in it, a plate under the top of
/// the screen says how long it has left and offers to go into it. Esc lets it go; it goes back to its own wits.
/// </summary>
public partial class GodPerson : Node3D
{
    Main _m;
    Camera3D _cam;
    Deity _d;
    float _yaw, _pitch;
    Vector3 _eye;
    bool _eyeSet;
    CanvasLayer _layer, _plateLayer;
    Label _name, _hint, _aim;
    Control _cross;
    PanelContainer _plate;
    Label _plateText;
    Button _plateGo;
    const float Look = .0022f, PitchMax = 1.3f;

    public bool On => _d != null;
    /// <summary>The plate offering the god is up.</summary>
    public bool PlateShown => _plate.Visible;
    public Deity God => _d;
    public Camera3D Cam => _cam;

    public void Init(Main m)
    {
        _m = m;
        // the valley under it as far as the far land's rim
        _cam = new Camera3D { Fov = 72, Near = .3f, Far = 1400, Current = false };
        AddChild(_cam);
        _layer = new CanvasLayer { Layer = 30, Visible = false };
        AddChild(_layer);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(root);
        _cross = new Crosshair { MouseFilter = Control.MouseFilterEnum.Ignore };
        _cross.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_cross);
        _name = Hud.Text("", Ornament.SerifBold, 18, Ornament.Parchment, outline: 6);
        _aim = Hud.Text("", Ornament.Serif, 14, Ornament.Aged, outline: 5);
        _hint = Hud.Text("", Ornament.Serif, 13, Ornament.Aged, outline: 5);
        foreach (var l in new[] { _name, _aim, _hint }) { l.MouseFilter = Control.MouseFilterEnum.Ignore; root.AddChild(l); }

        // the plate offering to go into it, while it is up
        _plateLayer = new CanvasLayer { Layer = 12 };
        AddChild(_plateLayer);
        _plate = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _plate.AddThemeStyleboxOverride("panel", Craft.PlateBox());
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        _plate.AddChild(row);
        _plateText = Hud.Text("", Ornament.SerifBold, 15, Ornament.Parchment);
        row.AddChild(_plateText);
        _plateGo = new Button { Text = "Вселиться", FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = Bindings.Fill("Вселиться в Колосса ({Possess})\nУправлять им от первого лица: {FpForward}{FpLeft}{FpBack}{FpRight} — шаги, {Lmb} — смести рукой, {Rmb} — удар кулаком, {FpSwitch} — вырвать глыбу и бросить, {FpUp} — рёв. Esc — выйти.") };
        _plateGo.AddThemeFontOverride("font", Ornament.SerifBold);
        _plateGo.Pressed += () => { if (m.Sim.OurGod is { } g) Enter(g); };
        row.AddChild(_plateGo);
        _plateLayer.AddChild(_plate);
        m.Sim.GodGone += d => { if (d == _d) Leave(d.Hp <= 0 ? "Колосс повержен" : "Колосс ушёл под землю"); };
    }

    /// <summary>Goes into the household's god; false if it cannot be gone into now (still coming up, or going under).</summary>
    public bool Enter(Deity d)
    {
        if (d == null || d.Act == DeityAct.Rising || !_m.Sim.Ride(d)) return false;
        if (_m.FP.On) _m.FP.Exit();
        _d = d;
        _yaw = d.Facing; _pitch = -.35f;
        _eyeSet = false;
        _m.Tools.SetTool(Tool.None);
        _m.Selected.Clear(); _m.Inspected = null;
        _m.Hud.OnSelectionChanged();
        _m.Hud.FirstPerson(true);
        _m.Rig.Held = true;
        _m.GodView.HiddenId = d.Id;
        _m.Atmos.FogShift = -40;
        _m.Atmos.Perspective = true;
        _m.Cover.Visible = false;
        _cam.MakeCurrent();
        _layer.Visible = true;
        _plate.Visible = false;
        if (!_m.Scripted) Input.MouseMode = Input.MouseModeEnum.Captured;
        _m.Hud.Log($"Вы в {d.T.Name.ToLowerInvariant()}е. Esc — выйти.", Hud.Kind.Ok);
        Update(0);
        return true;
    }

    /// <summary>Lets go of the god, of the player's own will.</summary>
    public void Exit() => Leave(null);

    void Leave(string why)
    {
        var d = _d;
        if (d == null) return;
        _d = null;
        _m.Sim.Unride();
        _m.Rig.Held = false;
        _m.Rig.Set(_m.World.WX(d.X), _m.World.WZ(d.Z), -_yaw - MathF.PI / 2, .5f, .45f, snap: true);
        _m.Rig.Cam.MakeCurrent();
        _m.Hud.FirstPerson(false);
        _m.GodView.HiddenId = -1;
        _m.Atmos.FogShift = 0;
        _m.Atmos.Perspective = false;
        _m.Cover.Visible = true;
        _layer.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (why != null) _m.Hud.Log($"Вы вышли: {why}.", Hud.Kind.Warn);
    }

    /// <summary>Turns the head (the self-test, the pictures).</summary>
    public void Face(float yaw, float pitch) { _yaw = yaw; _pitch = Math.Clamp(pitch, -PitchMax, PitchMax); if (_d != null) Update(0); }

    public override void _Input(InputEvent e)
    {
        if (_d == null || _m.Hud.MenuOpen) return;
        var sim = _m.Sim;
        switch (e)
        {
            case InputEventMouseMotion mm when Input.MouseMode == Input.MouseModeEnum.Captured:
                _yaw = Mathf.Wrap(_yaw + mm.Relative.X * Look, -MathF.PI, MathF.PI);
                _pitch = Math.Clamp(_pitch - mm.Relative.Y * Look, -PitchMax, PitchMax);
                break;
            case InputEventMouseButton { Pressed: true } mb:
                if (Input.MouseMode != Input.MouseModeEnum.Captured && !_m.Scripted) { Input.MouseMode = Input.MouseModeEnum.Captured; break; }
                if (mb.ButtonIndex == Bindings.First) sim.GodWishSweep = mb.ShiftPressed ? -1 : 1;
                else if (mb.ButtonIndex == Bindings.Second) sim.GodWishSlam = true;
                break;
            case InputEventKey { Pressed: true, Echo: false } k:
                if (k.PhysicalKeycode == Key.Escape) { Exit(); break; }
                if (Bindings.Is(k, Act.FpSwitch)) sim.GodWishLift = true;
                else if (Bindings.Is(k, Act.FpUp)) sim.GodWishRoar = true;
                else if (Bindings.Is(k, Act.Pause) || Bindings.Is(k, Act.Speed) || Bindings.Is(k, Act.QuickSave)) return;
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("GodPerson");
        Plate();
        if (_d == null) return;
        if (_m.Hud.MenuOpen && Input.MouseMode == Input.MouseModeEnum.Captured) Input.MouseMode = Input.MouseModeEnum.Visible;
        Update((float)delta);
    }

    /// <summary>The plate offering the god to go into, while it is up and the player is not in it.</summary>
    void Plate()
    {
        var g = _m.Sim.OurGod;
        bool show = g != null && _d == null && !_m.FP.On && g.Act is not (DeityAct.Sinking or DeityAct.Fallen) && !_m.AtlasOpen;
        _plate.Visible = show;
        if (!show) return;
        double left = Math.Max(0, g.Until - _m.Sim.Time);
        _plateText.Text = g.Act == DeityAct.Rising ? "Колосс выходит из-под земли" : $"Колосс · {(int)left / 60}:{(int)left % 60:00}";
        _plateGo.Disabled = g.Act == DeityAct.Rising;
        var vp = GetViewport().GetVisibleRect().Size;
        _plate.Position = new Vector2((vp.X - _plate.Size.X) / 2, 58);
    }

    static bool Down(Act a) => Bindings.Held(a);

    void Update(float dt)
    {
        var sim = _m.Sim;
        var d = _d;
        var w = _m.World;
        float fwd = 0, side = 0;
        if (!_m.Hud.MenuOpen)
        {
            if (Down(Act.FpForward)) fwd += 1;
            if (Down(Act.FpBack)) fwd -= 1;
            if (Down(Act.FpRight)) side += 1;
            if (Down(Act.FpLeft)) side -= 1;
        }
        float fx = MathF.Cos(_yaw), fz = MathF.Sin(_yaw), rx = -fz, rz = fx;
        float wx = fx * fwd + rx * side, wz = fz * fwd + rz * side, wl = MathF.Sqrt(wx * wx + wz * wz);
        if (wl > 1) { wx /= wl; wz /= wl; }
        sim.GodWishX = wx; sim.GodWishZ = wz;
        sim.GodLookYaw = _yaw;
        // the eyes: in its head as the view draws it, the jolts of its steps smoothed out
        Vector3 eye;
        if (_m.GodView.TryHead(d, out var head)) eye = head.Origin + head.Basis.Y.Normalized() * d.T.Height * .06f * VoxelWorld.Vox + head.Basis.Z.Normalized() * 3;
        else eye = new Vector3(w.WX(d.X), w.WY(d.Y + d.T.Height * .9f), w.WZ(d.Z));
        if (!_eyeSet || dt <= 0) { _eye = eye; _eyeSet = true; }
        else _eye = _eye.Lerp(eye, 1 - MathF.Exp(-dt * 6));
        var dir = new Vector3(fx * MathF.Cos(_pitch), MathF.Sin(_pitch), fz * MathF.Cos(_pitch));
        var basis = Basis.LookingAt(dir, Vector3.Up);
        float k = _m.Rig.Shaking;
        if (k > 0 && Settings.Shake > 0)
        {
            float a = k * k * Settings.Shake * .03f, t = Time.GetTicksMsec() / 1000f;
            basis = basis * new Basis(Vector3.Right, a * MathF.Sin(t * 23)) * new Basis(Vector3.Up, a * MathF.Sin(t * 17 + 1.3f));
        }
        _cam.GlobalTransform = new Transform3D(basis, _eye);
        _m.Rig.Focus(w.WX(d.X), w.WZ(d.Z));
        // where it looks: the ground (or what stands on it) the line from its eyes meets
        float ox = (_eye.X - w.OriginX) / VoxelWorld.Vox, oy = _eye.Y / VoxelWorld.Vox, oz = (_eye.Z - w.OriginZ) / VoxelWorld.Vox;
        if (Raycast.Cast(w, ox, oy, oz, dir.X, dir.Y, dir.Z, out var hit, skipVeg: true))
        {
            sim.GodAimX = hit.X + .5f; sim.GodAimY = hit.Y + 1; sim.GodAimZ = hit.Z + .5f;
        }
        else
        {
            sim.GodAimX = d.X + fx * d.T.SlamFar; sim.GodAimZ = d.Z + fz * d.T.SlamFar; sim.GodAimY = d.Y;
        }
        Words(d, sim);
    }

    /// <summary>Its name and the time it has left, where it looks and how far, the keys.</summary>
    void Words(Deity d, Sim sim)
    {
        var vp = GetViewport().GetVisibleRect().Size;
        double left = Math.Max(0, d.Until - sim.Time);
        string foe = d.Foe is { } f ? $" · {f.T.Name}: {Math.Max(0, f.Hp) / f.T.Hp * 100:0} %" : "";
        _name.Text = $"{d.T.Name} · {(int)left / 60}:{(int)left % 60:00} · {Math.Max(0, d.Hp) / d.T.Hp * 100:0} %{foe} · убито {d.Kills}";
        _name.Position = new Vector2(96, vp.Y - 84);
        float dist = MathF.Sqrt((sim.GodAimX - d.X) * (sim.GodAimX - d.X) + (sim.GodAimZ - d.Z) * (sim.GodAimZ - d.Z)) / VoxelWorld.PerMetre;
        _aim.Text = d.Held != null ? $"в руке глыба · бросить туда: {dist:0} м" : $"взгляд: {dist:0} м";
        _aim.Position = new Vector2(vp.X / 2 + 22, vp.Y / 2 + 14);
        _hint.Text = Bindings.Fill("{FpForward}{FpLeft}{FpBack}{FpRight} — шаг · {Lmb} — смести рукой (Shift — левой) · {Rmb} — кулаком в землю · {FpSwitch} — вырвать глыбу, ещё раз — бросить · {FpUp} — рёв · Esc — выйти");
        _hint.Position = new Vector2(96, vp.Y - 56);
    }

    /// <summary>The mark in the middle of the sight.</summary>
    partial class Crosshair : Control
    {
        public override void _Draw()
        {
            var c = Size / 2;
            var ink = new Color(1, .82f, .55f, .85f);
            DrawArc(c, 9, 0, MathF.Tau, 24, ink, 1.6f, true);
            DrawCircle(c, 1.6f, ink, true, -1, true);
        }
    }
}
