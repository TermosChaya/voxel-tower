using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// First person: the player gone into one of ours, seeing with its eyes. A camera of its own at the height of its eyes,
/// bobbing as it walks and shaken as the view is shaken; its arm with what it holds before the eyes, swinging as it
/// strikes and works, and for a bowman the bow in the other hand; a mark in the middle of the sight, red over one of
/// the horde in reach, green over what the pick can take; its name and health at the foot, and the keys. The mouse is
/// held and turns the head; the keys walk it; Esc lets it go and the view goes back over the valley above it. The
/// frame of the game is put away meanwhile, all but the chronicle's toasts.
/// </summary>
public partial class FirstPerson : Node3D
{
    Main _m;
    Camera3D _cam;
    Unit _u;
    float _yaw, _pitch;
    Vector3 _eye;
    bool _eyeSet;
    MeshInstance3D _main, _off;
    Figure _fig;
    int _rev = -1;
    ShaderMaterial _mat;
    CanvasLayer _layer;
    Sights _sights;
    Label _name, _hint, _aim;
    /// <summary>The keys as if held (the self-test walks it so): x right, y forward.</summary>
    public Vector2 TestMove;
    const float Look = .0026f, PitchMax = 1.35f;

    public bool On => _u != null;
    public Unit Unit => _u;
    public Camera3D Cam => _cam;

    public void Init(Main m)
    {
        _m = m;
        // it sees out over the valley to the far land's rim
        _cam = new Camera3D { Fov = 74, Near = .015f, Far = 480, Current = false };
        AddChild(_cam);
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
        _mat.SetShaderParameter("grid", (float)BodyPlan.Sub);
        _main = new MeshInstance3D { MaterialOverride = _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        _off = new MeshInstance3D { MaterialOverride = _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        _cam.AddChild(_main);
        _cam.AddChild(_off);
        _layer = new CanvasLayer { Layer = 30, Visible = false };
        AddChild(_layer);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(root);
        _sights = new Sights(this) { MouseFilter = Control.MouseFilterEnum.Ignore };
        _sights.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_sights);
        _name = Hud.Text("", Ornament.SerifBold, 17, Ornament.Parchment, outline: 6);
        _aim = Hud.Text("", Ornament.Serif, 14, Ornament.Aged, outline: 5);
        _hint = Hud.Text("", Ornament.Serif, 13, Ornament.Aged, outline: 5);
        foreach (var l in new[] { _name, _aim, _hint }) { l.MouseFilter = Control.MouseFilterEnum.Ignore; root.AddChild(l); }
        m.Sim.Unpossessed += why => { if (_u != null) Leave(why); };
    }

    /// <summary>Goes into one of ours; false if it cannot be gone into.</summary>
    public bool Enter(Unit u)
    {
        if (u == null || !_m.Sim.Possess(u)) return false;
        _u = u;
        _yaw = u.Facing; _pitch = -.08f;
        _eyeSet = false;
        _m.Tools.SetTool(Tool.None);
        _m.Selected.Clear(); _m.Inspected = null; _m.KeepChosen = false;
        _m.Hud.OnSelectionChanged();
        _m.Hud.FirstPerson(true);
        _m.Rig.Held = true;
        _m.Rig.Set(_m.World.WX(u.X), _m.World.WZ(u.Z), -u.Facing - MathF.PI / 2, .62f, 2.4f, snap: true);
        _m.Units.HiddenId = u.Id;
        _m.Units.ForceNear = true;
        _m.Atmos.FogShift = -62;
        _m.Cover.Visible = false;
        _fig = Figure.For(u.T, u.Look);
        _rev = -1;
        _cam.MakeCurrent();
        _layer.Visible = true;
        if (!_m.Scripted) Input.MouseMode = Input.MouseModeEnum.Captured;
        _m.Hud.Log($"Вы в нём: {u.Name.ToLowerInvariant()} №{u.Id}. Esc — выйти.", Hud.Kind.Ok);
        Update(0);
        return true;
    }

    /// <summary>Turns the head (scripted scenes and tests): the facing as <see cref="Core.Unit.Facing"/>, and up or down.</summary>
    public void Face(float yaw, float pitch)
    {
        _yaw = yaw; _pitch = Math.Clamp(pitch, -PitchMax, PitchMax);
        Update(0);
    }

    /// <summary>Lets go of the one gone into, of the player's own will.</summary>
    public void Exit() => Leave(null);

    void Leave(string why)
    {
        var u = _u;
        _u = null;
        if (_m.Sim.Possessed != null) _m.Sim.Unpossess(null);
        _m.Rig.Held = false;
        if (u != null) _m.Rig.Set(_m.World.WX(u.X), _m.World.WZ(u.Z), -_yaw - MathF.PI / 2, .62f, 2.4f, snap: true);
        _m.Rig.Cam.MakeCurrent();
        _m.Hud.FirstPerson(false);
        _m.Units.HiddenId = -1;
        _m.Units.ForceNear = false;
        _m.Atmos.FogShift = 0;
        _m.Cover.Visible = true;
        _layer.Visible = false;
        _main.Visible = _off.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        TestMove = Vector2.Zero;
        if (why != null) _m.Hud.Log($"Вы вышли: {why}.", Hud.Kind.Warn);
        // the one left is chosen, if it is still about
        if (u != null && !u.Dead && !u.Inside) { _m.Selected.Add(u); _m.Hud.OnSelectionChanged(); }
    }

    public override void _Input(InputEvent e)
    {
        if (_u == null) return;
        var sim = _m.Sim;
        switch (e)
        {
            case InputEventMouseMotion mm when Input.MouseMode == Input.MouseModeEnum.Captured:
                _yaw = Mathf.Wrap(_yaw + mm.Relative.X * Look, -MathF.PI, MathF.PI);
                _pitch = Math.Clamp(_pitch - mm.Relative.Y * Look, -PitchMax, PitchMax);
                break;
            case InputEventMouseMotion:
                break;
            case InputEventMouseButton mb:
                if (Input.MouseMode != Input.MouseModeEnum.Captured && !_m.Scripted && mb.Pressed) Input.MouseMode = Input.MouseModeEnum.Captured;
                if (mb.ButtonIndex == MouseButton.Left) sim.WishStrike = mb.Pressed;
                else if (mb.ButtonIndex == MouseButton.Right) sim.WishLay = mb.Pressed && _u.T.Work > 0;
                break;
            case InputEventKey { Pressed: true, Echo: false } k:
                if (k.PhysicalKeycode == Key.Escape) { Exit(); break; }
                if (k.PhysicalKeycode == Key.F && Sim.Kindles(_u.T)) { _u.FireArrows = !_u.FireArrows; break; }
                if (k.PhysicalKeycode is Key.Key1 or Key.Key2 or Key.Key3)
                {
                    sim.LayMat = k.PhysicalKeycode == Key.Key1 ? Mat.Stone : k.PhysicalKeycode == Key.Key2 ? Mat.Wood : Mat.Rammed;
                    break;
                }
                // the pause, the speed and the quick save go on as ever
                if (k.PhysicalKeycode is Key.P or Key.Tab or Key.F5 or Key.F9) return;
                break;
            case InputEventKey:
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_u == null) return;
        // the game's menu takes the mouse back
        if (_m.Hud.MenuOpen && Input.MouseMode == Input.MouseModeEnum.Captured) Input.MouseMode = Input.MouseModeEnum.Visible;
        Update((float)delta);
    }

    static bool Down(Key k) => Input.IsPhysicalKeyPressed(k);

    void Update(float dt)
    {
        var sim = _m.Sim;
        var u = _u;
        var w = _m.World;
        // the wish: forward and to the side as the keys are held, turned to where it looks
        float fwd = TestMove.Y, side = TestMove.X;
        if (!_m.Hud.MenuOpen)
        {
            if (Down(Key.W) || Down(Key.Up)) fwd += 1;
            if (Down(Key.S) || Down(Key.Down)) fwd -= 1;
            if (Down(Key.D) || Down(Key.Right)) side += 1;
            if (Down(Key.A) || Down(Key.Left)) side -= 1;
        }
        float fx = MathF.Cos(_yaw), fz = MathF.Sin(_yaw), rx = -fz, rz = fx;
        float wx = fx * fwd + rx * side, wz = fz * fwd + rz * side, wl = MathF.Sqrt(wx * wx + wz * wz);
        if (wl > 1) { wx /= wl; wz /= wl; }
        sim.WishX = wx; sim.WishZ = wz;
        sim.WishRun = Down(Key.Shift);
        sim.WishClimb = Down(Key.Space) ? 1 : Down(Key.C) || Down(Key.Ctrl) ? -1 : 0;
        sim.LookYaw = _yaw; sim.LookPitch = _pitch;
        // the eyes: at the height of its head, the steps and the sim's ticks smoothed out, bobbing as it walks
        float bob = u.State == UnitState.Walk ? MathF.Sin(u.Stride * .55f) * .55f : 0;
        var eye = new Vector3(w.WX(u.X), w.WY(u.Y + u.T.BodyH * .88f + bob), w.WZ(u.Z));
        if (!_eyeSet || dt <= 0) { _eye = eye; _eyeSet = true; }
        else _eye = _eye.Lerp(eye, 1 - MathF.Exp(-dt * 22));
        var dir = new Vector3(fx * MathF.Cos(_pitch), MathF.Sin(_pitch), fz * MathF.Cos(_pitch));
        var basis = Basis.LookingAt(dir, Vector3.Up);
        // the shaking of the view, as the great moments shake it
        float k = _m.Rig.Shaking;
        if (k > 0 && Settings.Shake > 0)
        {
            float a = k * k * Settings.Shake * .045f, t = Time.GetTicksMsec() / 1000f;
            basis = basis * new Basis(Vector3.Right, a * MathF.Sin(t * 37)) * new Basis(Vector3.Up, a * MathF.Sin(t * 29 + 1.3f));
        }
        _cam.GlobalTransform = new Transform3D(basis, _eye + dir * .02f);
        // the sounds, the rain and the embers follow it
        _m.Rig.Focus(w.WX(u.X), w.WZ(u.Z));
        Hands(u);
        Words(u, sim);
        _sights.QueueRedraw();
    }

    /// <summary>Its arms before the eyes: the one with what it holds (and the bow arm), swinging as it strikes or works.</summary>
    void Hands(Unit u)
    {
        if (u.Wounds != null && u.Wounds.Rev != _rev)
        {
            _rev = u.Wounds.Rev;
            _main.Mesh = Figure.MeshOf(u.Wounds.Grid[(int)Part.Main]);
            _off.Mesh = Figure.MeshOf(u.Wounds.Grid[(int)Part.Off]);
        }
        else if (u.Wounds == null && _rev != 0)
        {
            _rev = 0;
            _main.Mesh = _fig.Mesh[(int)Part.Main];
            _off.Mesh = _fig.Mesh[(int)Part.Off];
        }
        float swing = u.Swing, main, off;
        if (u.T.Ranged) { main = -1.42f + swing * .3f; off = -1.5f; }
        else if (u.State == UnitState.Work) { main = Mathf.Lerp(-.9f, -2.3f, swing); off = -.5f; }
        else { main = Mathf.Lerp(-1.1f, -2.5f, swing * swing) + (1 - swing) * .1f; off = _fig.Shield ? -1.1f : -.5f; }
        Place(_main, Part.Main, main, new Vector3(.1f, -.1f, -.13f));
        Place(_off, Part.Off, off, new Vector3(-.1f, -.1f, -.13f));
        // a beast's "arms" are its forelegs, and a rider's are drawn on the horse's frame: neither is held before the eyes
        bool hands = !_fig.Beast && !_fig.Mounted;
        _main.Visible = hands && _main.Mesh != null;
        _off.Visible = hands && _off.Mesh != null && (u.T.Ranged || _fig.Shield);
    }

    /// <summary>A part of the figure hung from its joint at a spot before the eyes, swung about the joint.</summary>
    void Place(MeshInstance3D mi, Part p, float angle, Vector3 shoulder)
    {
        var j = _fig.Joint[(int)p];
        // drawn at half its size and near: the figures' arms are thick, and seen from the eye they would fill the sight
        var turn = new Basis(Vector3.Up, MathF.PI).Scaled(Vector3.One * VoxelWorld.Vox * .5f);
        var swing = new Transform3D(Basis.Identity, j) * new Transform3D(new Basis(Vector3.Right, angle), Vector3.Zero) * new Transform3D(Basis.Identity, -j);
        mi.Transform = new Transform3D(turn, shoulder - turn * j) * swing;
    }

    /// <summary>The words at the foot: its name and health, what the mark rests on, and the keys.</summary>
    void Words(Unit u, Sim sim)
    {
        var vp = GetViewport().GetVisibleRect().Size;
        string hurt = Retinue.Hurt(u);
        _name.Text = $"{u.Name} №{u.Id} · {Math.Ceiling(Math.Max(0, u.Hp))} из {Math.Ceiling(u.MaxHp)}" + (hurt != "" ? $" · {hurt}" : "")
            + (u.T.Ranged && u.FireArrows ? " · огненные стрелы" : "") + (u.T.Work > 0 ? $" · кладёт: {Mat.NameOf(sim.LayMat).ToLowerInvariant()}" : "");
        _name.Position = new Vector2(24, vp.Y - 64);
        var seen = sim.Sighted(u, 40 * VoxelWorld.PerMetre, out float along);
        string aim = seen != null ? $"{seen.Name} · {Math.Ceiling(Math.Max(0, seen.Hp))} из {Math.Ceiling(seen.MaxHp)} · {along / VoxelWorld.PerMetre:0} м"
            : sim.LookedAt(u, 4 * VoxelWorld.PerMetre, out var hit) ? Mat.NameOf(hit.M) : "";
        _aim.Text = aim;
        _aim.Position = new Vector2((vp.X - _aim.Size.X) / 2, vp.Y / 2 + 26);
        _hint.Text = "WASD — идти, Shift — бегом · мышь — смотреть · ЛКМ — " + (u.T.Ranged ? "выстрел" : u.T.Work > 0 ? "удар или кирка" : "удар")
            + (u.T.Work > 0 ? " · ПКМ — класть (1 камень, 2 брёвна, 3 земля)" : "") + (Sim.Kindles(u.T) ? " · F — огненные стрелы" : "")
            + " · Пробел / C — вверх и вниз по лестнице · Esc — выйти";
        _hint.Position = new Vector2((vp.X - _hint.Size.X) / 2, vp.Y - 34);
        _sights.Foe = seen != null && seen.Team != Team.Player && along <= (u.T.Ranged ? sim.BowRange(u) : u.T.Reach + seen.T.BodyR + 2);
        _sights.Work = !_sights.Foe && u.T.Work > 0 && sim.LookedAt(u, 4 * VoxelWorld.PerMetre, out var h2) && Labour.Diggable(h2.M);
        _sights.Health = Math.Clamp(u.Hp / u.MaxHp, 0, 1);
    }

    /// <summary>The mark in the middle of the sight and the health along the foot.</summary>
    partial class Sights : Control
    {
        readonly FirstPerson _fp;
        public bool Foe, Work;
        public float Health = 1;
        public Sights(FirstPerson fp) { _fp = fp; }
        public Sights() { }

        public override void _Draw()
        {
            var c = Size / 2;
            var col = Foe ? Ornament.Blood : Work ? Ornament.Moss : Ornament.EmberGlow;
            DrawArc(c, 9, 0, MathF.Tau, 32, new Color(0, 0, 0, .55f), 3.5f, true);
            DrawArc(c, 9, 0, MathF.Tau, 32, col, 1.6f, true);
            for (int i = 0; i < 4; i++)
            {
                var d = new Vector2(MathF.Cos(i * MathF.PI / 2), MathF.Sin(i * MathF.PI / 2));
                DrawLine(c + d * 13, c + d * 19, new Color(0, 0, 0, .55f), 3.5f, true);
                DrawLine(c + d * 13, c + d * 19, col, 1.6f, true);
            }
            DrawCircle(c, 1.6f, col, true, -1, true);
            // the health, a bar along the foot under its name
            var bar = new Rect2(24, Size.Y - 38, 260, 9);
            DrawRect(bar.Grow(1.5f), new Color(0, 0, 0, .7f));
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * Health, bar.Size.Y)), Health > .6f ? Ornament.Moss : Health > .3f ? Ornament.Ember : Ornament.Blood);
            DrawRect(bar, Craft.Brass, false, 1);
        }
    }
}
