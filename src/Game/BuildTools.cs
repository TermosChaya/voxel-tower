using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public enum Tool { None, Brush, Line, Box, Circle, Extrude, Erase, Dig, Capture, Paste }

public enum BrushForm { Column, Ball, Repaint }

public sealed class ToolOptions
{
    public byte Material = Mat.Stone;
    /// <summary>Thickness of lines and walls, nib of the brush and the eraser, width of the spade; voxels.</summary>
    public int Width = 8;
    public int Height = 32;
    public int Step = 4;
    public int DigDepth = 16;
    public BrushForm Form = BrushForm.Column;
    public bool Hollow;
    public bool Level = true;

    public static readonly int[] Widths = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32 };
    public static readonly int[] Heights = { 4, 8, 12, 16, 24, 32, 40, 48, 56, 64, 80, 96 };
    public static readonly int[] Steps = { 1, 2, 4, 8, 16 };
    public static readonly int[] Depths = { 8, 16, 24, 32 };

    public static int Next(int[] values, int current, int dir)
    {
        int i = Array.IndexOf(values, current);
        if (i < 0) { i = 0; while (i < values.Length - 1 && values[i] < current) i++; }
        return values[Math.Clamp(i + dir, 0, values.Length - 1)];
    }
}

/// <summary>
/// The player's hands. There are no ready-made buildings: walls, towers and stairs are drawn with brushes,
/// and whatever is worth repeating is captured as a blueprint. A ghost shows each stroke before it is laid
/// (ember when the treasury can pay, red when it cannot); laying is instant.
/// </summary>
public partial class BuildTools : Node3D
{
    public static string ToolName(Tool t) => t switch
    {
        Tool.Brush => "Кисть", Tool.Line => "Линия", Tool.Box => "Прямоугольник", Tool.Circle => "Круг",
        Tool.Extrude => "Выдавить", Tool.Erase => "Ластик", Tool.Dig => "Лопата", Tool.Capture => "Снять чертёж",
        Tool.Paste => "Чертёж", _ => "Стройка",
    };

    public static string Hint(Tool t) => t switch
    {
        Tool.Brush => "Ведите по земле или по стене — материал ложится следом. F меняет форму, Shift стирает.",
        Tool.Line => "Зажмите и протяните: прямая полоса заданной толщины и высоты. Так рисуют стены.",
        Tool.Box => "Протяните от угла до угла. F — сплошной или только стенки.",
        Tool.Circle => "Нажмите в центре и тяните до края. F — сплошной или кольцо, кольцо — это башня.",
        Tool.Extrude => "Щёлкните по грани своей кладки: вся ровная грань нарастёт на шаг выбранным материалом.",
        Tool.Erase => "Стирает свою кладку. Половина цены камня и брёвен вернётся, земля — целиком.",
        Tool.Dig => "Копает грунт и дёрн, скалу не берёт. Вырытая земля идёт на насыпи и земляные стены.",
        Tool.Capture => "Обведите рамкой свою постройку — она станет чертежом.",
        Tool.Paste => "Щёлкните, чтобы поставить копию. R поворачивает, Esc отменяет.",
        _ => "1–7 — кисти, Z X C — материал, B — снять чертёж. Нарисуйте стену или башню и сохраните её чертежом.",
    };

    const int MaxExtrudeFaces = 6000;

    Main _m;
    public Tool Tool { get; private set; }
    public readonly ToolOptions Opt = new();
    public Blueprint PasteBp { get; private set; }
    public int PasteRot { get; private set; }

    VoxelHit _hover;
    bool _hasHover;
    // while a shape or a wall stroke is dragged, its far end follows the plane it started on
    (int x, int y, int z) _dragPt;
    bool _hasDrag;
    bool _lmb, _paint, _shift, _hasStart;
    Vector2 _pressPos;
    (int x, int y, int z) _startPt;
    int _levelTop, _digFloor;
    (int x, int z) _lastG;
    Vector3 _lastHit;
    int _sumStone, _sumWood, _sumEarth;

    Node3D _voxSpace;
    MeshInstance3D _ghost, _shape, _cursor;
    ShaderMaterial _ghostMat;
    StandardMaterial3D _shapeMat;
    readonly GreedyMesher _mesher = new();
    byte[] _grid = Array.Empty<byte>();
    VoxelList _plan;
    string _planKey = "~";
    double _planClock, _clock;

    /// <summary>What the planned stroke costs, or null; and whether the treasury can pay for it.</summary>
    public string PlanText { get; private set; }
    public bool PlanAffordable { get; private set; }
    /// <summary>The material under the cursor and its height above the ground.</summary>
    public string HoverText { get; private set; } = "";

    public void Init(Main m)
    {
        _m = m;
        var w = m.World;
        _voxSpace = new Node3D { Position = new Vector3(w.OriginX, 0, w.OriginZ), Scale = Vector3.One * VoxelWorld.Vox };
        AddChild(_voxSpace);
        _ghostMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ghost.gdshader") };
        _ghost = new MeshInstance3D { MaterialOverride = _ghostMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        _voxSpace.AddChild(_ghost);
        _shapeMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(.9f, .55f, .25f, .22f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _shape = new MeshInstance3D { MaterialOverride = _shapeMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        AddChild(_shape);
        _cursor = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = .07f, OuterRadius = .095f, Rings = 24, RingSegments = 6 },
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Ornament.EmberGlow, NoDepthTest = true, RenderPriority = 10 },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_cursor);
    }

    // ---------------------------------------------------------------- state

    public void SetTool(Tool t)
    {
        Tool = Tool == t ? Tool.None : t;
        if (Tool != Tool.Paste) PasteBp = null;
        _lmb = _paint = _hasStart = false;
        _planKey = "~";
        _m.Hud.OnToolChanged();
    }

    public void StartPaste(Blueprint bp)
    {
        PasteBp = bp;
        PasteRot = 0;
        Tool = Tool.Paste;
        _lmb = _paint = false;
        _planKey = "~";
        _m.Hud.OnToolChanged();
    }

    public void Cancel() { if (Tool != Tool.None) SetTool(Tool.None); }

    public void SetMaterial(byte m) { Opt.Material = m; Changed(); }

    /// <summary>Call after any option changed: re-plans the ghost and updates the belt.</summary>
    public void Changed() { _planKey = "~"; _m.Hud.OnToolChanged(); }

    public void StepWidth(int dir) { Opt.Width = ToolOptions.Next(ToolOptions.Widths, Opt.Width, dir); Changed(); }

    /// <summary>The second number of the current tool: height, extrusion step or digging depth.</summary>
    public void StepHeight(int dir)
    {
        if (Tool == Tool.Extrude) Opt.Step = ToolOptions.Next(ToolOptions.Steps, Opt.Step, dir);
        else if (Tool == Tool.Dig) Opt.DigDepth = ToolOptions.Next(ToolOptions.Depths, Opt.DigDepth, dir);
        else Opt.Height = ToolOptions.Next(ToolOptions.Heights, Opt.Height, dir);
        Changed();
    }

    public void ToggleForm()
    {
        if (Tool == Tool.Brush) Opt.Form = (BrushForm)(((int)Opt.Form + 1) % 3);
        else Opt.Hollow = !Opt.Hollow;
        Changed();
    }

    public void ToggleLevel() { Opt.Level = !Opt.Level; Changed(); }

    Stroke StrokeNow() => new()
    {
        Mat = Opt.Material, Width = Opt.Width, Height = Opt.Height, Level = Opt.Level, Hollow = Opt.Hollow,
    };

    // ---------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion mm:
                Pick(mm.Position);
                if (_lmb && _paint) PaintTo();
                break;
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left && !mb.AltPressed:
                Pick(mb.Position);
                if (mb.Pressed) Press(mb);
                else if (_lmb) Release(mb);
                break;
            case InputEventKey k when k.Pressed && !k.CtrlPressed && !k.MetaPressed:
                if (Key(k)) GetViewport().SetInputAsHandled();
                break;
        }
    }

    bool Key(InputEventKey k)
    {
        var code = k.PhysicalKeycode;
        if (k.Echo && code is not (global::Godot.Key.Bracketleft or global::Godot.Key.Bracketright or global::Godot.Key.Minus or global::Godot.Key.Equal)) return false;
        Tool t = code switch
        {
            global::Godot.Key.Key1 => Tool.Brush, global::Godot.Key.Key2 => Tool.Line, global::Godot.Key.Key3 => Tool.Box,
            global::Godot.Key.Key4 => Tool.Circle, global::Godot.Key.Key5 => Tool.Extrude, global::Godot.Key.Key6 => Tool.Erase,
            global::Godot.Key.Key7 => Tool.Dig, global::Godot.Key.Key8 or global::Godot.Key.B => Tool.Capture,
            _ => Tool.None,
        };
        if (t != Tool.None) { SetTool(t); return true; }
        switch (code)
        {
            case global::Godot.Key.Escape: SetTool(Tool.None); return true;
            case global::Godot.Key.Z: SetMaterial(Mat.Stone); return true;
            case global::Godot.Key.X: SetMaterial(Mat.Wood); return true;
            case global::Godot.Key.C: SetMaterial(Mat.Rammed); return true;
            case global::Godot.Key.Bracketleft: StepWidth(-1); return true;
            case global::Godot.Key.Bracketright: StepWidth(1); return true;
            case global::Godot.Key.Minus: StepHeight(-1); return true;
            case global::Godot.Key.Equal: StepHeight(1); return true;
            case global::Godot.Key.F: ToggleForm(); return true;
            case global::Godot.Key.G: ToggleLevel(); return true;
            case global::Godot.Key.R when Tool == Tool.Paste: PasteRot = (PasteRot + 1) & 3; Changed(); return true;
        }
        return false;
    }

    void Press(InputEventMouseButton mb)
    {
        _lmb = true;
        _pressPos = mb.Position;
        _shift = mb.ShiftPressed;
        _hasStart = _hasHover;
        _sumStone = _sumWood = _sumEarth = 0;
        if (!_hasHover) return;
        var w = _m.World;
        _startPt = _hover.Ground(w);
        _levelTop = Shapes.LevelTop(w, _startPt.x, _startPt.z, Opt.Height);
        if (Tool is Tool.Brush or Tool.Erase or Tool.Dig)
        {
            _paint = true;
            _lastG = (_startPt.x, _startPt.z);
            _lastHit = new Vector3(_hover.X, _hover.Y, _hover.Z);
            if (Tool == Tool.Dig) _digFloor = (_hover.NY == 1 ? _hover.Y + 1 : _hover.Y) - Opt.DigDepth;
            Dab(_hover, first: true);
        }
    }

    void Release(InputEventMouseButton mb)
    {
        _lmb = false;
        var w = _m.World;
        if (_paint)
        {
            _paint = false;
            var sum = new Cost(_sumStone, _sumWood, _sumEarth);
            if (!sum.IsEmpty) _m.Hud.Log($"{ToolName(Tool)}: {Describe(sum)}");
            return;
        }
        if (Tool is Tool.Line or Tool.Box or Tool.Circle or Tool.Capture)
        {
            if (!_hasStart || !(_hasDrag || _hasHover)) return;
            var a = _startPt; var b = _hasDrag ? _dragPt : _hover.Ground(w);
            if (Tool == Tool.Capture) { Capture(a, b); return; }
            var plan = PlanDrag(a, b);
            if (plan == null || plan.Count == 0) { Refuse("здесь нечего класть: всё уже занято"); return; }
            Build(plan, ToolName(Tool));
            return;
        }
        if ((mb.Position - _pressPos).Length() > 8 || !_hasHover) return;
        if (Tool == Tool.Extrude)
        {
            var plan = Shapes.Extrude(w, _m.Site, _hover, Opt.Step, Opt.Material, MaxExtrudeFaces, out bool tooBig);
            if (tooBig) { Refuse($"грань слишком велика: больше {MaxExtrudeFaces} вокселей"); return; }
            if (plan == null || plan.Count == 0) { Refuse("выдавить можно открытую ровную грань"); return; }
            Build(plan, "Выдавить");
        }
        else if (Tool == Tool.Paste && PasteBp != null) Build(PlanPasteAtHover(), PasteBp.Name);
    }

    void Capture((int x, int y, int z) a, (int x, int y, int z) b)
    {
        var bp = Blueprint.Capture(_m.World, a.x, a.z, b.x, b.z, _m.NextBlueprintId, null);
        if (bp == null) { Refuse("в рамке нет вашей кладки"); return; }
        _m.NextBlueprintId++;
        _m.AddBlueprint(bp);
        _m.Hud.Log($"Снят {bp.Name}: {bp.Describe()}", Hud.Kind.Ok);
        SetTool(Tool.None);
    }

    VoxelList PlanDrag((int x, int y, int z) a, (int x, int y, int z) b)
    {
        var w = _m.World; var site = _m.Site; var st = StrokeNow();
        return Tool switch
        {
            Tool.Line => Shapes.Line(w, site, a.x, a.z, b.x, b.z, st, _levelTop),
            Tool.Box => Shapes.Box(w, site, a.x, a.z, b.x, b.z, st, _levelTop),
            Tool.Circle => Shapes.Circle(w, site, a.x, a.z, Brushes.Dist(b.x - a.x, b.z - a.z) + .5, st, _levelTop),
            _ => null,
        };
    }

    // ---------------------------------------------------------------- freehand strokes

    double NibR => Shapes.NibRadius(Opt.Width);
    bool ColumnStroke => Tool == Tool.Brush && Opt.Form == BrushForm.Column && !_shift;

    /// <summary>Carries the stroke from the last dab to the cursor without leaving gaps.</summary>
    void PaintTo()
    {
        if (ColumnStroke)
        {
            if (!_hasDrag) return;
            var d = _dragPt;
            if (Math.Abs(d.x - _lastG.x) + Math.Abs(d.z - _lastG.z) < 1) return;
            Lay(Shapes.Line(_m.World, _m.Site, _lastG.x, _lastG.z, d.x, d.z, StrokeNow(), _levelTop));
            _lastG = (d.x, d.z);
            return;
        }
        if (!_hasHover) return;
        var h = _hover;
        var g = h.Ground(_m.World);
        var hit = new Vector3(h.X, h.Y, h.Z);
        float spacing = (float)Math.Max(1, NibR * .7), dist = hit.DistanceTo(_lastHit);
        if (dist < spacing) return;
        int n = (int)Math.Ceiling(dist / spacing);
        for (int i = 1; i <= n; i++)
        {
            var p = _lastHit.Lerp(hit, i / (float)n);
            var at = h;
            at.X = (int)MathF.Round(p.X); at.Y = (int)MathF.Round(p.Y); at.Z = (int)MathF.Round(p.Z);
            Dab(at, first: false);
        }
        _lastHit = hit;
        _lastG = (g.x, g.z);
    }

    void Dab(VoxelHit h, bool first)
    {
        var w = _m.World; var site = _m.Site; var wal = _m.Wallet;
        double r = NibR;
        if (Tool == Tool.Brush && _shift) Builder.RemoveSphere(w, wal, h.X, h.Y, h.Z, r, Chips(.15f));
        else if (Tool == Tool.Brush && Opt.Form == BrushForm.Column)
        {
            var g = h.Ground(w);
            Lay(Shapes.Line(w, site, g.x, g.z, g.x, g.z, StrokeNow(), _levelTop));
        }
        else if (Tool == Tool.Brush && Opt.Form == BrushForm.Ball)
        {
            int off = Math.Max(1, (int)Math.Ceiling(r));
            Lay(Shapes.Ball(w, site, h.X + h.NX * off, h.Y + h.NY * off, h.Z + h.NZ * off, r, Opt.Material));
        }
        else if (Tool == Tool.Brush)
        {
            var res = Builder.TryRepaint(w, wal, Shapes.Repaint(w, h.X, h.Y, h.Z, Math.Max(1, r), Opt.Material), out var c, out _);
            if (res == BuildResult.Ok) Sum(c);
            else if (res == BuildResult.NoGold) Refuse("не хватает золота на перекраску");
            else if (res == BuildResult.NoEarth) Refuse("не хватает земли: копайте лопатой");
        }
        else if (Tool == Tool.Erase)
        {
            int n = Builder.RemoveSphere(w, wal, h.X, h.Y, h.Z, r, Chips(.15f));
            if (n == 0 && first) Refuse("стереть можно только свою кладку");
        }
        else if (Tool == Tool.Dig)
        {
            var d = Builder.Dig(w, site, wal, h.X, h.Z, _digFloor, Math.Max(1, Opt.Width / 2), out _, Chips(.03f));
            if (d == Builder.DigResult.NoGold) Refuse("нечем платить землекопам");
        }
        _m.Hud.Refresh();
    }

    void Lay(VoxelList L)
    {
        var r = Builder.TryBuild(_m.World, _m.Wallet, L, out var c);
        if (r == BuildResult.Ok) { Sum(c); _m.Hud.Refresh(); }
        else if (r == BuildResult.NoGold) Refuse($"нужно {c.Gold} золота");
        else if (r == BuildResult.NoEarth) Refuse($"нужно {Fmt.One(c.EarthM3)} м³ земли: копайте лопатой");
    }

    void Sum(Cost c) { _sumStone += c.Stone; _sumWood += c.Wood; _sumEarth += c.Earth; }

    Action<int, int, int, byte> Chips(float chance) => (x, y, z, m) =>
    {
        if (Random.Shared.NextDouble() < chance) _m.Debris.Spawn(m, x, y, z, 1, 1.4f);
    };

    bool Build(VoxelList L, string what)
    {
        var r = Builder.TryBuild(_m.World, _m.Wallet, L, out var c);
        switch (r)
        {
            case BuildResult.Ok:
                _m.Hud.Log($"{what}: {Describe(c)}");
                _m.Hud.Refresh();
                _planKey = "~";
                return true;
            case BuildResult.NoGold: Refuse($"нужно {c.Gold} золота"); break;
            case BuildResult.NoEarth: Refuse($"нужно {Fmt.One(c.EarthM3)} м³ земли: копайте лопатой"); break;
            case BuildResult.Occupied: Refuse("здесь уже занято"); break;
            default: Refuse("здесь не построить"); break;
        }
        return false;
    }

    /// <summary>"камень 2,5 · земля 5,8 м³ · 28 зол."</summary>
    public static string Describe(Cost c)
    {
        var parts = new List<string>();
        if (c.Stone > 0) parts.Add("камень " + Fmt.M3(c.Stone));
        if (c.Wood > 0) parts.Add("брёвна " + Fmt.M3(c.Wood));
        if (c.Earth > 0) parts.Add("земля " + Fmt.M3(c.Earth));
        string vol = parts.Count > 0 ? string.Join(" · ", parts) + " м³" : "";
        return c.Gold > 0 ? $"{vol} · {c.Gold} зол." : vol;
    }

    string _lastRefuse = ""; double _lastRefuseAt = -9;
    void Refuse(string msg)
    {
        if (msg == _lastRefuse && _clock - _lastRefuseAt < .8) return;
        _lastRefuse = msg; _lastRefuseAt = _clock;
        _m.Hud.Float(msg, Ornament.Blood);
    }

    // ---------------------------------------------------------------- picking and previews

    void Pick(Vector2 screen)
    {
        var (o, d) = _m.Rig.Ray(screen);
        var w = _m.World;
        _hasHover = Raycast.Cast(w, (o.X - w.OriginX) / VoxelWorld.Vox, o.Y / VoxelWorld.Vox, (o.Z - w.OriginZ) / VoxelWorld.Vox, d.X, d.Y, d.Z, out _hover, skipVeg: true);
        _hasDrag = false;
        if (_lmb && _hasStart && (Tool is Tool.Line or Tool.Box or Tool.Circle or Tool.Capture || ColumnStroke) && Math.Abs(d.Y) > 1e-4f)
        {
            float t = (w.WY(_startPt.y) - o.Y) / d.Y;
            if (t > 0)
            {
                var p = o + d * t;
                _dragPt = (Math.Clamp(w.VX(p.X), 0, w.SX - 1), _startPt.y, Math.Clamp(w.VZ(p.Z), 0, w.SZ - 1));
                _hasDrag = true;
            }
        }
        if (!_hasHover) { HoverText = ""; return; }
        int ground = w.GroundTop(_hover.X, _hover.Z);
        float above = (_hover.Y + 1 - ground) * VoxelWorld.Vox;
        HoverText = Mat.IsPlayer[_hover.M] && above > .05f ? $"{Mat.NameOf(_hover.M)} · {Fmt.One(above)} м над землёй" : Mat.NameOf(_hover.M);
    }

    /// <summary>Points the tools at a spot on the map as if the mouse were there (screenshots and tests).</summary>
    public void PointAt(Vector3 world) => Pick(_m.Rig.Cam.UnprojectPosition(world));

    VoxelList PlanPasteAtHover()
    {
        if (PasteBp == null || !_hasHover) return null;
        var g = _hover.Ground(_m.World);
        int w = PasteBp.RotW(PasteRot), d = PasteBp.RotD(PasteRot);
        return PasteBp.PlanPaste(_m.World, _m.Site, g.x - (w >> 1), g.z - (d >> 1), PasteRot, g.y);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        UpdateCursor();
        UpdateShape();
        UpdatePlan();
    }

    void UpdateCursor()
    {
        _cursor.Visible = _hasHover && Tool != Tool.None;
        if (!_cursor.Visible) return;
        var w = _m.World;
        var p = new Vector3(w.WX(_hover.X + .5f + _hover.NX * .52f), w.WY(_hover.Y + .5f + _hover.NY * .52f), w.WZ(_hover.Z + .5f + _hover.NZ * .52f));
        Basis b = _hover.NX != 0 ? new Basis(Vector3.Forward, Mathf.Pi / 2) : _hover.NZ != 0 ? new Basis(Vector3.Right, Mathf.Pi / 2) : Basis.Identity;
        _cursor.Transform = new Transform3D(b, p);
    }

    void UpdateShape()
    {
        var w = _m.World;
        bool show = false;
        bool erasing = Tool == Tool.Erase || (Tool == Tool.Brush && Input.IsKeyPressed(global::Godot.Key.Shift));
        if (_hasHover && Tool == Tool.Dig)
        {
            var g = _hover.Ground(w);
            float h = Opt.DigDepth * VoxelWorld.Vox, r = Math.Max(1, Opt.Width / 2) * VoxelWorld.Vox + .02f;
            SetShape(new CylinderMesh { TopRadius = r, BottomRadius = r, Height = h, RadialSegments = 32 },
                new Vector3(w.WX(_hover.X + .5f), w.WY(g.y) - h / 2, w.WZ(_hover.Z + .5f)), new Color(.55f, .4f, .28f, .3f));
            show = true;
        }
        else if (_hasHover && erasing)
        {
            float r = (float)(NibR + .5) * VoxelWorld.Vox;
            SetShape(new SphereMesh { Radius = r, Height = r * 2, RadialSegments = 24, Rings = 12 },
                new Vector3(w.WX(_hover.X + .5f), w.WY(_hover.Y + .5f), w.WZ(_hover.Z + .5f)), new Color(.83f, .3f, .22f, .32f));
            show = true;
        }
        else if (Tool == Tool.Capture && _lmb && _hasStart && _hasDrag)
        {
            var a = _startPt; var b = _dragPt;
            int x0 = Math.Min(a.x, b.x), x1 = Math.Max(a.x, b.x) + 1, z0 = Math.Min(a.z, b.z), z1 = Math.Max(a.z, b.z) + 1;
            SetShape(new BoxMesh { Size = new Vector3((x1 - x0) * VoxelWorld.Vox, .04f, (z1 - z0) * VoxelWorld.Vox) },
                new Vector3(w.WX((x0 + x1) / 2f), w.WY(Math.Max(a.y, b.y)) + .05f, w.WZ((z0 + z1) / 2f)), new Color(.9f, .62f, .3f, .28f));
            show = true;
        }
        _shape.Visible = show;
    }

    string _shapeKey = "";
    void SetShape(PrimitiveMesh mesh, Vector3 at, Color c)
    {
        string key = mesh switch
        {
            BoxMesh bm => "B" + bm.Size, CylinderMesh cm => "C" + cm.Height + "/" + cm.TopRadius, SphereMesh sm => "S" + sm.Radius, _ => "?",
        };
        if (key != _shapeKey) { _shape.Mesh = mesh; _shapeKey = key; }
        _shape.Position = at;
        _shapeMat.AlbedoColor = c;
    }

    void UpdatePlan()
    {
        var w = _m.World;
        string key = "";
        bool shift = Input.IsKeyPressed(global::Godot.Key.Shift);
        bool dragging = Tool is Tool.Line or Tool.Box or Tool.Circle && _lmb && _hasStart && _hasDrag;
        if (dragging) key = $"D{Tool}|{_startPt}|{_dragPt}|{Opt.Material}|{Opt.Width}|{Opt.Height}|{Opt.Level}|{Opt.Hollow}|{w.Revision}|{(int)_m.Wallet.Gold}|{(int)(_m.Wallet.Earth * 10)}";
        else if (_hasHover)
        {
            var g = _hover.Ground(w);
            string h = $"{_hover.X},{_hover.Y},{_hover.Z},{_hover.NX}{_hover.NY}{_hover.NZ}";
            key = Tool switch
            {
                Tool.Line => $"L|{g}",
                Tool.Brush when !shift && Opt.Form == BrushForm.Column => $"C|{g}|{(_paint ? _levelTop : -1)}",
                Tool.Brush when !shift => $"B{Opt.Form}|{h}",
                Tool.Extrude => $"E|{h}",
                Tool.Paste when PasteBp != null => $"P|{PasteBp.Id}|{g}|{PasteRot}",
                _ => "",
            };
            if (key.Length > 0)
                key += $"|{Opt.Material}|{Opt.Width}|{Opt.Height}|{Opt.Step}|{Opt.Level}|{Opt.Hollow}|{w.Revision}|{(int)_m.Wallet.Gold}|{(int)(_m.Wallet.Earth * 10)}";
        }
        if (key == _planKey) return;
        if (key.Length > 0 && _planKey.Length > 1 && _clock - _planClock < .05) return;
        _planKey = key;
        _planClock = _clock;
        if (key.Length == 0) { _plan = null; _ghost.Visible = false; PlanText = null; return; }

        var site = _m.Site;
        var gp = _hover.Ground(w);
        double r = NibR;
        bool repaint = false;
        string name = ToolName(Tool);
        int lt = Shapes.LevelTop(w, gp.x, gp.z, Opt.Height);
        if (dragging) _plan = PlanDrag(_startPt, _dragPt);
        else if (Tool == Tool.Line) { _plan = Shapes.Line(w, site, gp.x, gp.z, gp.x, gp.z, StrokeNow(), lt); name = "Линия, начало"; }
        else if (Tool == Tool.Brush && Opt.Form == BrushForm.Column) _plan = Shapes.Line(w, site, gp.x, gp.z, gp.x, gp.z, StrokeNow(), _paint ? _levelTop : lt);
        else if (Tool == Tool.Brush && Opt.Form == BrushForm.Ball)
        {
            int off = Math.Max(1, (int)Math.Ceiling(r));
            _plan = Shapes.Ball(w, site, _hover.X + _hover.NX * off, _hover.Y + _hover.NY * off, _hover.Z + _hover.NZ * off, r, Opt.Material);
        }
        else if (Tool == Tool.Brush) { _plan = Shapes.Repaint(w, _hover.X, _hover.Y, _hover.Z, Math.Max(1, r), Opt.Material); repaint = true; name = "Перекраска"; }
        else if (Tool == Tool.Extrude)
        {
            _plan = Shapes.Extrude(w, site, _hover, Opt.Step, Opt.Material, MaxExtrudeFaces, out bool tooBig);
            if (tooBig) { _ghost.Visible = false; PlanText = $"Грань больше {MaxExtrudeFaces} вокселей"; PlanAffordable = false; return; }
        }
        else if (Tool == Tool.Paste) { _plan = PlanPasteAtHover(); name = PasteBp.Name; }

        BuildResult check;
        Cost cost;
        if (repaint)
        {
            cost = Builder.CostOf(_plan);
            check = _plan == null || _plan.Count == 0 ? BuildResult.Nothing : _m.Wallet.Gold + 1 >= cost.Gold ? BuildResult.Ok : BuildResult.NoGold;
        }
        else check = Builder.Check(_m.Wallet, _plan, out cost);
        if (check is BuildResult.Nothing or BuildResult.Occupied) { _ghost.Visible = false; PlanText = null; return; }
        PlanAffordable = check == BuildResult.Ok;
        PlanText = $"{name}: {Describe(cost)}" + check switch
        {
            BuildResult.NoGold => " — не хватает золота",
            BuildResult.NoEarth => " — не хватает земли",
            _ => "",
        };
        _ghost.Mesh = MeshPlan(_plan);
        _ghost.Visible = _ghost.Mesh != null;
        _ghostMat.SetShaderParameter("tint", PlanAffordable ? new Vector3(.9f, .55f, .25f) : new Vector3(.85f, .2f, .16f));
        _ghostMat.SetShaderParameter("tint_mix", PlanAffordable ? .35f : .6f);
    }

    ArrayMesh MeshPlan(VoxelList L)
    {
        if (L == null || !L.Bounds(out int x0, out int y0, out int z0, out int x1, out int y1, out int z1)) return null;
        int dx = x1 - x0 + 1, dy = y1 - y0 + 1, dz = z1 - z0 + 1, px = dx + 2, pz = dz + 2;
        long size = (long)px * (dy + 2) * pz;
        if (size > 24_000_000) return null;
        if (_grid.Length < size) _grid = new byte[size]; else Array.Clear(_grid, 0, (int)size);
        for (int i = 0; i < L.Count; i++)
        {
            byte m = L.M(i);
            if (m != 0) _grid[((L.Y(i) - y0 + 1) * pz + (L.Z(i) - z0 + 1)) * px + (L.X(i) - x0 + 1)] = m;
        }
        return GodotMesh.From(_mesher.Mesh(_grid, dx, dy, dz, x0, y0, z0));
    }
}
