using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public enum Tool { None, Brush, Line, Box, Circle, Extrude, Ladder, Gate, Gather, Unmark, Capture, Paste }

public enum BrushForm { Column, Ball }

public sealed class ToolOptions
{
    public byte Material = Mat.Stone;
    /// <summary>The pick in hand instead of a material: strokes mark what is to be dug, quarried or felled.</summary>
    public bool Dig;
    /// <summary>Thickness of lines and walls, nib of the brush; voxels.</summary>
    public int Width = 8;
    public int Height = 32;
    /// <summary>How deep the pick goes below the surface.</summary>
    public int Depth = 16;
    public int Step = 4;
    public BrushForm Form = BrushForm.Column;
    public bool Hollow;
    public bool Level = true;

    public static readonly int[] Widths = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32 };
    public static readonly int[] Heights = { 4, 8, 12, 16, 24, 32, 40, 48, 56, 64, 80, 96 };
    public static readonly int[] Depths = { 4, 8, 12, 16, 24, 32, 48, 64, 96 };
    public static readonly int[] Steps = { 1, 2, 4, 8, 16 };

    public static int Next(int[] values, int current, int dir)
    {
        int i = Array.IndexOf(values, current);
        if (i < 0) { i = 0; while (i < values.Length - 1 && values[i] < current) i++; }
        return values[Math.Clamp(i + dir, 0, values.Length - 1)];
    }
}

/// <summary>
/// The player's hands. Nothing is laid or dug at once: every stroke marks work, and the pawns carry it out voxel by
/// voxel, laying from the stores and bringing back what they dig. There are no ready-made buildings either: walls,
/// towers and stairs are drawn with brushes, and whatever is worth repeating is captured as a blueprint. A ghost shows
/// each stroke before it is marked: ember when the stores hold enough, red when they don't or when the pick will take it.
/// </summary>
public partial class BuildTools : Node3D
{
    public static string ToolName(Tool t) => t switch
    {
        Tool.Brush => "Кисть", Tool.Line => "Линия", Tool.Box => "Прямоугольник", Tool.Circle => "Круг",
        Tool.Extrude => "Выдавить", Tool.Ladder => "Лестница", Tool.Gate => "Ворота", Tool.Gather => "Делянка", Tool.Unmark => "Снять разметку", Tool.Capture => "Снять чертёж",
        Tool.Paste => "Чертёж", _ => "Без кисти",
    };

    public static string Hint(Tool t, bool dig) => (t, dig) switch
    {
        (Tool.Brush, false) => "Ведите по земле — рабочие выложат столб до заданной высоты. F — шар. Shift или кирка (V) — размечать, что копать.",
        (Tool.Brush, true) => "Ведите по земле — рабочие выкопают полосу на заданную глубину. Шар (F) вынимает ком: так пробивают ходы. Щелчок по дереву — срубить.",
        (Tool.Line, false) => "Зажмите и протяните: стена заданной толщины и высоты. Рабочие сложат её из запасов.",
        (Tool.Line, true) => "Протяните — ров или траншея заданной ширины и глубины. Деревья на пути срубят целиком.",
        (Tool.Box, false) => "От угла до угла. F — сплошной помост или только стенки: двор, квадратная башня.",
        (Tool.Box, true) => "От угла до угла — котлован. Ровное дно или по рельефу (G).",
        (Tool.Circle, false) => "Из центра к краю. Кольцо — круглая башня, сплошной — бастион. F переключает.",
        (Tool.Circle, true) => "Из центра к краю — круглая яма или кольцевой ров вокруг башни (F).",
        (Tool.Extrude, false) => "Щёлкните по ровной грани: она нарастёт на шаг выбранным материалом.",
        (Tool.Extrude, true) => "Щёлкните по ровной грани: её вынут на шаг вглубь. Ступени в склоне, проход в скале.",
        (Tool.Ladder, _) => "Щёлкните по боковой грани стены, башни или обрыва — рабочие приставят лестницу от земли до верха. По ней ходят ваши пешки… и орда, если доберётся.",
        (Tool.Gate, _) => "Щёлкните по боковой грани стены — рабочие прорубят в ней проём и навесят ворота: створки внутри, со стороны цитадели. Своим они открываются сами, орде их придётся рубить.",
        (Tool.Gather, _) => "Протяните рамку — участок для добычи. Z — камень из скал и валунов, X — лес, C — земля. Свободные рабочие сами понемногу берут оттуда работу.",
        (Tool.Unmark, _) => "Протяните рамку — вся разметка и делянки в ней снимаются. Щелчок снимает разметку поблизости и делянку под курсором.",
        (Tool.Capture, _) => "Обведите рамкой свою постройку — она станет чертежом.",
        (Tool.Paste, _) => "Щёлкните, чтобы разметить копию. R поворачивает, Esc отменяет.",
        _ => "ЛКМ — выбрать пешек, рамкой — нескольких, ПКМ — послать, ПКМ по зверю — охотиться. 1–5 — кисти, 7 — лестница, V — кирка.",
    };

    const int MaxExtrudeFaces = 6000;
    /// <summary>How far round the cursor a click of the unmarking tool reaches, in voxels.</summary>
    public const int UnmarkR = 12;

    Main _m;
    public Tool Tool { get; private set; }
    public readonly ToolOptions Opt = new();
    public Blueprint PasteBp { get; private set; }
    public int PasteRot { get; private set; }

    VoxelHit _hover;
    bool _hasHover;
    // with the pick in hand the cursor also sees trees, to fell them whole
    VoxelHit _tree;
    bool _hasTree;
    // while a shape or a column stroke is dragged, its far end follows the plane it started on
    (int x, int y, int z) _dragPt;
    bool _hasDrag;
    bool _lmb, _paint, _shift, _hasStart;
    Vector2 _pressPos;
    (int x, int y, int z) _startPt;
    int _level;
    (int x, int z) _lastG;
    Vector3 _lastHit;
    int _strokeVox;
    double _strokeSec;
    readonly double[] _est = new double[4];
    readonly double[] _strokeEst = new double[4];

    Node3D _voxSpace;
    MeshInstance3D _ghost, _shape, _cursor;
    ShaderMaterial _ghostMat;
    StandardMaterial3D _shapeMat;
    readonly GreedyMesher _mesher = new();
    byte[] _grid = Array.Empty<byte>();
    VoxelList _plan;
    bool _planDig;
    string _planKey = "~";
    double _planClock, _clock, _revAt;
    int _rev;

    /// <summary>What the stroke under the cursor asks for, or null; and whether the stores hold enough.</summary>
    public string PlanText { get; private set; }
    public bool PlanAffordable { get; private set; }
    /// <summary>Voxels the stroke under the cursor takes from each store (negative when digging fills it).</summary>
    public readonly double[] PlanNeeds = new double[4];
    public bool PlanIsDig { get; private set; }
    /// <summary>The material under the cursor and what is marked there.</summary>
    public string HoverText { get; private set; } = "";
    /// <summary>The gate under the cursor, if the cursor is on one of its leaves.</summary>
    public Gateway HoverGate { get; private set; }
    /// <summary>The named place (a mine, a ruin) the cursor is over.</summary>
    public Landmark HoverPlace { get; private set; }
    GatePlan _gatePlan;

    /// <summary>"брёвна 3,4 м³ · проём: камень +4,0 м³ · 6 мин работы одного".</summary>
    string GateSummary(GatePlan gp)
    {
        var lay = new double[4]; var cut = new double[4];
        double sec = Labour.Estimate(_m.World, gp.Leaves, false, lay) + Labour.Estimate(_m.World, gp.Cut, true, cut);
        var parts = new List<string>();
        foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth })
            if (lay[k] > .5) parts.Add($"{Stockpile.Name(k)} {Fmt.M3(lay[k])} м³");
        var got = new List<string>();
        foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth })
            if (cut[k] < -.5) got.Add($"{Stockpile.Name(k)} +{Fmt.M3(-cut[k])} м³");
        if (got.Count > 0) parts.Add("проём: " + string.Join(", ", got));
        parts.Add($"{Fmt.Duration(sec)} работы одного");
        return string.Join(" · ", parts);
    }

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

    /// <summary>A material to lay; puts the pick away.</summary>
    public void SetMaterial(byte m) { Opt.Material = m; Opt.Dig = false; Changed(); }

    /// <summary>Takes up the pick (or puts it away): the brushes then mark digging instead of laying.</summary>
    public void SetDig(bool dig)
    {
        Opt.Dig = dig;
        if (dig && Tool is Tool.None or Tool.Capture or Tool.Unmark or Tool.Paste or Tool.Ladder or Tool.Gate or Tool.Gather) { Tool = Tool.Line; PasteBp = null; }
        Changed();
    }

    /// <summary>Call after any option changed: re-plans the ghost and updates the belt.</summary>
    public void Changed() { _planKey = "~"; _m.Hud.OnToolChanged(); }

    public void StepWidth(int dir) { Opt.Width = ToolOptions.Next(ToolOptions.Widths, Opt.Width, dir); Changed(); }

    /// <summary>The second number of the current tool: height, digging depth or extrusion step.</summary>
    public void StepHeight(int dir)
    {
        if (Tool == Tool.Extrude) Opt.Step = ToolOptions.Next(ToolOptions.Steps, Opt.Step, dir);
        else if (Opt.Dig || Tool == Tool.Gather) Opt.Depth = ToolOptions.Next(ToolOptions.Depths, Opt.Depth, dir);
        else Opt.Height = ToolOptions.Next(ToolOptions.Heights, Opt.Height, dir);
        Changed();
    }

    public void ToggleForm()
    {
        if (Tool == Tool.Brush) Opt.Form = Opt.Form == BrushForm.Column ? BrushForm.Ball : BrushForm.Column;
        else Opt.Hollow = !Opt.Hollow;
        Changed();
    }

    public void ToggleLevel() { Opt.Level = !Opt.Level; Changed(); }

    /// <summary>What a gathering area takes: the material in hand says which.</summary>
    public int GatherKind => Opt.Material switch { Mat.Wood => Stockpile.Wood, Mat.Rammed => Stockpile.Earth, _ => Stockpile.Stone };

    /// <summary>The pick is in hand, or Shift turns the brush into one for this stroke.</summary>
    bool DigNow => Opt.Dig || (Tool == Tool.Brush && (_lmb ? _shift : Input.IsKeyPressed(global::Godot.Key.Shift)));

    Stroke StrokeNow() => new()
    {
        Mat = Opt.Material, Width = Opt.Width, Height = DigNow ? Opt.Depth : Opt.Height, Level = Opt.Level, Hollow = Opt.Hollow, Dig = DigNow,
    };

    // ---------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion mm:
                Pick(mm.Position);
                if (Tool == Tool.None) _m.Command.Motion(mm);
                else if (_lmb && _paint) PaintTo();
                break;
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left && !mb.AltPressed:
                Pick(mb.Position);
                if (Tool == Tool.None && !_lmb) { _m.Command.Button(mb); break; }
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
            global::Godot.Key.Key4 => Tool.Circle, global::Godot.Key.Key5 => Tool.Extrude, global::Godot.Key.Key6 => Tool.Unmark,
            global::Godot.Key.Key7 => Tool.Ladder, global::Godot.Key.Key8 => Tool.Gather, global::Godot.Key.Key9 => Tool.Gate, global::Godot.Key.B => Tool.Capture,
            _ => Tool.None,
        };
        if (t != Tool.None) { SetTool(t); return true; }
        switch (code)
        {
            case global::Godot.Key.Escape:
                if (Tool != Tool.None) SetTool(Tool.None); else _m.Command.ClearSelection();
                return true;
            case global::Godot.Key.Z: SetMaterial(Mat.Stone); return true;
            case global::Godot.Key.X: SetMaterial(Mat.Wood); return true;
            case global::Godot.Key.C: SetMaterial(Mat.Rammed); return true;
            case global::Godot.Key.V: SetDig(!Opt.Dig); return true;
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
        _strokeVox = 0; _strokeSec = 0; Array.Clear(_strokeEst);
        if (!_hasHover) return;
        var w = _m.World;
        _startPt = _hover.Ground(w);
        _level = Shapes.Level(w, _startPt.x, _startPt.z, StrokeNow());
        if (Tool == Tool.Brush)
        {
            if (DigNow && _hasTree) { FellTree(); _lmb = false; return; }
            _paint = true;
            _lastG = (_startPt.x, _startPt.z);
            _lastHit = new Vector3(_hover.X, _hover.Y, _hover.Z);
            Dab(_hover);
        }
    }

    void Release(InputEventMouseButton mb)
    {
        _lmb = false;
        var w = _m.World;
        bool click = (mb.Position - _pressPos).Length() <= 8;
        bool dig = Opt.Dig || (Tool == Tool.Brush && _shift);
        if (_paint)
        {
            _paint = false;
            if (_strokeVox > 0) _m.Hud.Log($"{(dig ? "Копать" : ToolName(Tool))}: {Summary(_strokeEst, dig, _strokeSec)}");
            return;
        }
        if (Tool is Tool.Line or Tool.Box or Tool.Circle or Tool.Capture or Tool.Unmark or Tool.Gather)
        {
            if (click && Tool == Tool.Unmark) { UnmarkAt(); return; }
            if (click && Tool == Tool.Gather) { Refuse("протяните рамку по участку"); return; }
            if (click && dig && _hasTree) { FellTree(); return; }
            if (!_hasStart || !(_hasDrag || _hasHover)) return;
            var a = _startPt; var b = _hasDrag ? _dragPt : _hover.Ground(w);
            if (Tool == Tool.Capture) { Capture(a, b); return; }
            if (Tool == Tool.Unmark)
            {
                int n = _m.Sim.CancelArea(a.x, a.z, b.x, b.z), zones = _m.Sim.RemoveZones(a.x, a.z, b.x, b.z);
                if (n == 0 && zones == 0) Refuse("в рамке нет разметки");
                else _m.Hud.Float((n > 0 ? $"разметка снята: {Fmt.M3(n)} м³" : "") + (zones > 0 ? (n > 0 ? ", " : "") + $"делянок снято: {zones}" : ""), Ornament.BronzeLight);
                return;
            }
            if (Tool == Tool.Gather) { SetZone(a, b); return; }
            Designate(PlanDrag(a, b, dig), dig, ToolName(Tool));
            return;
        }
        if (!click || !_hasHover) return;
        if (Tool == Tool.Ladder)
        {
            var plan = Shapes.LadderAt(w, _m.Site, _hover);
            if (plan.Count == 0) { Refuse("лестницу ставят к боковой грани стены или обрыва"); return; }
            Designate(plan, false, "Лестница");
        }
        else if (Tool == Tool.Gate)
        {
            var gp = GatePlan.At(w, _m.Site, _hover);
            if (gp == null) { Refuse("ворота ставят в боковую грань стены толщиной до 4 м"); return; }
            _m.Sim.AddGate(gp);
            _m.Hud.Log("Ворота: " + GateSummary(gp));
            _planKey = "~";
        }
        else if (Tool == Tool.Extrude)
        {
            var plan = Shapes.Extrude(w, _m.Site, _hover, Opt.Step, Opt.Material, MaxExtrudeFaces, out bool tooBig, dig);
            if (tooBig) { Refuse($"грань слишком велика: больше {MaxExtrudeFaces} вокселей"); return; }
            if (plan == null || plan.Count == 0) { Refuse(dig ? "вдавить можно открытую ровную грань" : "выдавить можно открытую ровную грань"); return; }
            Designate(plan, dig, dig ? "Вдавить" : "Выдавить");
        }
        else if (Tool == Tool.Paste && PasteBp != null) Designate(PlanPasteAtHover(), false, PasteBp.Name);
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

    void UnmarkAt()
    {
        if (!_hasHover) return;
        int n = _m.Sim.CancelOrders(_hover.X, _hover.Y, _hover.Z, UnmarkR);
        int zones = _m.Sim.RemoveZones(_hover.X, _hover.Z, _hover.X, _hover.Z);
        if (n == 0 && zones == 0) Refuse("здесь нет разметки");
        else _m.Hud.Float(zones > 0 ? "делянка снята" : $"разметка снята: {Fmt.M3(n)} м³", Ornament.BronzeLight);
    }

    /// <summary>"Делянка: деревьев 12, ≈ 115 м³ брёвен" or "Каменоломня: ≈ 9,5 м³ камня до 2 м вглубь".</summary>
    string SurveyText(int x0, int z0, int x1, int z1, out bool any)
    {
        int kind = GatherKind;
        var (trees, vox) = _m.Sim.Survey(x0, z0, x1, z1, kind, Opt.Depth);
        any = vox > 0;
        string name = Zone.Name0(kind);
        if (kind == Stockpile.Wood) return trees == 0 ? $"{name}: деревьев нет" : $"{name}: деревьев {trees}, ≈ {Fmt.M3(vox)} м³ брёвен";
        string depth = Len(Opt.Depth);
        return vox <= 0 ? $"{name}: {(kind == Stockpile.Stone ? "скалы" : "земли")} здесь нет" : $"{name}: ≈ {Fmt.M3(vox)} м³ {(kind == Stockpile.Stone ? "камня" : "земли")} до {depth} вглубь";
    }

    static string Len(int voxels)
    {
        double m = voxels / (double)VoxelWorld.PerMetre;
        return m < 1 ? $"{Math.Round(m * 100)} см" : (Fmt.One(m) + " м").Replace(",0 м", " м");
    }

    void SetZone((int x, int y, int z) a, (int x, int y, int z) b)
    {
        string text = SurveyText(a.x, a.z, b.x, b.z, out bool any);
        if (!any) { Refuse(text); return; }
        _m.Sim.AddZone(a.x, a.z, b.x, b.z, GatherKind, Opt.Depth);
        _m.Hud.Log(text + ". Свободные рабочие возьмутся за неё сами.");
    }

    void FellTree()
    {
        var plan = Shapes.Tree(_m.World, _tree.X, _tree.Y, _tree.Z);
        Designate(plan, true, "Срубить дерево");
    }

    VoxelList PlanDrag((int x, int y, int z) a, (int x, int y, int z) b, bool dig)
    {
        var w = _m.World; var site = _m.Site; var st = StrokeNow();
        st.Dig = dig;
        st.Height = dig ? Opt.Depth : Opt.Height;
        var plan = Tool switch
        {
            Tool.Line => Shapes.Line(w, site, a.x, a.z, b.x, b.z, st, _level),
            Tool.Box => Shapes.Box(w, site, a.x, a.z, b.x, b.z, st, _level),
            Tool.Circle => Shapes.Circle(w, site, a.x, a.z, Brushes.Dist(b.x - a.x, b.z - a.z) + .5, st, _level),
            _ => null,
        };
        return dig ? Shapes.WithTrees(_m.World, plan) : plan;
    }

    // ---------------------------------------------------------------- freehand strokes

    double NibR => Shapes.NibRadius(Opt.Width);
    bool ColumnStroke => Tool == Tool.Brush && Opt.Form == BrushForm.Column;

    /// <summary>Carries the stroke from the last dab to the cursor without leaving gaps.</summary>
    void PaintTo()
    {
        if (ColumnStroke)
        {
            if (!_hasDrag) return;
            var d = _dragPt;
            if (Math.Abs(d.x - _lastG.x) + Math.Abs(d.z - _lastG.z) < 1) return;
            Mark(Shapes.Line(_m.World, _m.Site, _lastG.x, _lastG.z, d.x, d.z, StrokeNow(), _level));
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
            Dab(at);
        }
        _lastHit = hit;
        _lastG = (g.x, g.z);
    }

    void Dab(VoxelHit h)
    {
        var w = _m.World; var site = _m.Site;
        double r = NibR;
        if (ColumnStroke)
        {
            var g = h.Ground(w);
            Mark(Shapes.Line(w, site, g.x, g.z, g.x, g.z, StrokeNow(), _level));
        }
        else if (DigNow) Mark(Shapes.BallDig(w, site, h.X, h.Y, h.Z, Math.Max(.5, r)));
        else
        {
            int off = Math.Max(1, (int)Math.Ceiling(r));
            Mark(Shapes.Ball(w, site, h.X + h.NX * off, h.Y + h.NY * off, h.Z + h.NZ * off, r, Opt.Material));
        }
    }

    /// <summary>One dab of a freehand stroke: marked quietly, summed up when the button is let go.</summary>
    void Mark(VoxelList plan)
    {
        if (plan == null || plan.Count == 0) return;
        bool dig = DigNow;
        if (dig) plan = Shapes.WithTrees(_m.World, plan);
        _strokeSec += Labour.Estimate(_m.World, plan, dig, _est);
        for (int k = 0; k < 4; k++) _strokeEst[k] += _est[k];
        _strokeVox += dig ? _m.Sim.DesignateRemove(plan) : _m.Sim.DesignateBuild(plan);
        _planKey = "~";
    }

    /// <summary>Marks a whole shape for the pawns and writes in the chronicle what it will take.</summary>
    bool Designate(VoxelList plan, bool dig, string what)
    {
        if (plan == null || plan.Count == 0) { Refuse(dig ? "здесь нечего копать" : "здесь нечего класть: всё уже занято"); return false; }
        double sec = Labour.Estimate(_m.World, plan, dig, _est);
        int n = dig ? _m.Sim.DesignateRemove(plan) : _m.Sim.DesignateBuild(plan);
        if (n == 0) { Refuse(dig ? "здесь нечего копать" : "здесь нечего класть: всё уже занято"); return false; }
        _m.Hud.Log($"{what}: {Summary(_est, dig, sec)}");
        _planKey = "~";
        return true;
    }

    /// <summary>"камень 2,5 м³ · 3 мин работы одного" or, digging, "земля +1,8 · камень +0,4 м³ · 2 мин работы одного".</summary>
    public static string Summary(double[] stores, bool dig, double seconds)
    {
        var parts = new List<string>();
        foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth })
        {
            double v = stores[k];
            if (dig && v < -.5) parts.Add($"{Stockpile.Name(k)} +{Fmt.M3(-v)}");
            else if (!dig && v > .5) parts.Add($"{Stockpile.Name(k)} {Fmt.M3(v)}");
        }
        string what = parts.Count > 0 ? string.Join(" · ", parts) + " м³" : dig ? "без добычи" : "";
        string work = $"{Fmt.Duration(seconds)} работы одного";
        return what.Length > 0 ? $"{what} · {work}" : work;
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
        double ox = (o.X - w.OriginX) / VoxelWorld.Vox, oy = o.Y / VoxelWorld.Vox, oz = (o.Z - w.OriginZ) / VoxelWorld.Vox;
        _hasHover = Raycast.Cast(w, ox, oy, oz, d.X, d.Y, d.Z, out _hover, skipVeg: true);
        _hasTree = false;
        if (DigNow && Raycast.Cast(w, ox, oy, oz, d.X, d.Y, d.Z, out var veg) && veg.M is Mat.Trunk or Mat.Leaf or Mat.Foliage)
        {
            _tree = veg;
            _hasTree = true;
        }
        _hasDrag = false;
        if (_lmb && _hasStart && (Tool is Tool.Line or Tool.Box or Tool.Circle or Tool.Capture or Tool.Unmark or Tool.Gather || ColumnStroke) && Math.Abs(d.Y) > 1e-4f)
        {
            float t = (w.WY(_startPt.y) - o.Y) / d.Y;
            if (t > 0)
            {
                var p = o + d * t;
                _dragPt = (Math.Clamp(w.VX(p.X), 0, w.SX - 1), _startPt.y, Math.Clamp(w.VZ(p.Z), 0, w.SZ - 1));
                _hasDrag = true;
            }
        }
        HoverGate = _hasHover && _hover.M is Mat.Gate or Mat.GateShut ? _m.Sim.GateAt(_hover.X, _hover.Y, _hover.Z) : null;
        HoverPlace = null;
        if (_hasHover)
            foreach (var l in _m.Site.Landmarks)
                if (Brushes.Dist(_hover.X - l.X, _hover.Z - l.Z) < l.R) { HoverPlace = l; break; }
        if (!_hasHover) { HoverText = ""; return; }
        int ground = w.GroundTop(_hover.X, _hover.Z);
        float above = (_hover.Y + 1 - ground) / (float)VoxelWorld.PerMetre;
        string text = _hasTree ? (_tree.M == Mat.Trunk ? "дерево: ствол" : "дерево: крона")
            : Mat.IsPlayer[_hover.M] && above > .05f ? $"{Mat.NameOf(_hover.M)} · {Fmt.One(above)} м над землёй" : Mat.NameOf(_hover.M);
        byte order = _m.Sim.Orders.At(_hover.X, _hover.Y, _hover.Z);
        if (order == 0) { var f = _hover.Front; order = _m.Sim.Orders.At(f.x, f.y, f.z); }
        if (order == Orders.Remove) text += " · размечено: копать";
        else if (order != 0) text += $" · размечено: {Mat.NameOf(order).ToLowerInvariant()}";
        var zone = _m.Sim.ZoneAt(_hover.X, _hover.Z);
        if (zone != null) text += $" · {zone.Name.ToLowerInvariant()}";
        HoverText = text;
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
        var h = _hasTree ? _tree : _hover;
        var p = new Vector3(w.WX(h.X + .5f + h.NX * .52f), w.WY(h.Y + .5f + h.NY * .52f), w.WZ(h.Z + .5f + h.NZ * .52f));
        Basis b = h.NX != 0 ? new Basis(Vector3.Forward, Mathf.Pi / 2) : h.NZ != 0 ? new Basis(Vector3.Right, Mathf.Pi / 2) : Basis.Identity;
        _cursor.Transform = new Transform3D(b, p);
    }

    void UpdateShape()
    {
        var w = _m.World;
        bool show = false;
        if ((Tool is Tool.Capture or Tool.Unmark or Tool.Gather) && _lmb && _hasStart && _hasDrag)
        {
            var a = _startPt; var b = _dragPt;
            int x0 = Math.Min(a.x, b.x), x1 = Math.Max(a.x, b.x) + 1, z0 = Math.Min(a.z, b.z), z1 = Math.Max(a.z, b.z) + 1;
            var c = Tool == Tool.Unmark ? new Color(.83f, .3f, .22f, .26f) : Tool == Tool.Gather ? ZoneView.Tint(GatherKind, .3f) : new Color(.9f, .62f, .3f, .28f);
            SetShape(new BoxMesh { Size = new Vector3((x1 - x0) * VoxelWorld.Vox, .04f, (z1 - z0) * VoxelWorld.Vox) },
                new Vector3(w.WX((x0 + x1) / 2f), w.WY(Math.Max(a.y, b.y)) + .05f, w.WZ((z0 + z1) / 2f)), c);
            show = true;
        }
        else if (Tool == Tool.Unmark && _hasHover)
        {
            float r = (UnmarkR + .5f) * VoxelWorld.Vox;
            SetShape(new SphereMesh { Radius = r, Height = r * 2, RadialSegments = 24, Rings = 12 },
                new Vector3(w.WX(_hover.X + .5f), w.WY(_hover.Y + .5f), w.WZ(_hover.Z + .5f)), new Color(.83f, .3f, .22f, .22f));
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
        // the valley changes all the time while the pawns work: re-plan for it twice a second, not every frame
        if (w.Revision != _rev && _clock - _revAt > .5) { _rev = w.Revision; _revAt = _clock; }
        var st = _m.Sim.Stock;
        string stock = $"{(int)(st[1] / 400)}|{(int)(st[2] / 400)}|{(int)(st[3] / 400)}";
        bool dig = DigNow;
        string opts = $"{dig}|{Opt.Material}|{Opt.Width}|{Opt.Height}|{Opt.Depth}|{Opt.Step}|{Opt.Level}|{Opt.Hollow}|{Opt.Form}|{_rev}|{stock}";
        string key = "";
        bool dragging = Tool is Tool.Line or Tool.Box or Tool.Circle && _lmb && _hasStart && _hasDrag;
        if (Tool == Tool.Gather)
        {
            // a gathering area shows what it holds, no ghost
            string gk = _lmb && _hasStart && _hasDrag ? $"G|{_startPt}|{_dragPt}|{GatherKind}|{Opt.Depth}|{_rev}" : "";
            if (gk == _planKey || (gk.Length > 0 && _clock - _planClock < .15)) return;
            _planKey = gk; _planClock = _clock;
            _ghost.Visible = false;
            bool any = false;
            PlanText = gk.Length == 0 ? null : SurveyText(_startPt.x, _startPt.z, _dragPt.x, _dragPt.z, out any);
            PlanAffordable = any;
            return;
        }
        if (dragging) key = $"D{Tool}|{_startPt}|{_dragPt}|{opts}";
        else if (dig && _hasTree && Tool is Tool.Brush or Tool.Line or Tool.Box or Tool.Circle) key = $"T|{_tree.X},{_tree.Y},{_tree.Z}|{_rev}";
        else if (_hasHover)
        {
            var g = _hover.Ground(w);
            string h = $"{_hover.X},{_hover.Y},{_hover.Z},{_hover.NX}{_hover.NY}{_hover.NZ}";
            key = Tool switch
            {
                Tool.Line or Tool.Box or Tool.Circle => $"L|{g}",
                Tool.Brush when Opt.Form == BrushForm.Column => $"C|{g}|{(_paint ? _level : -1)}",
                Tool.Brush => $"B|{h}",
                Tool.Extrude => $"E|{h}",
                Tool.Ladder => $"A|{h}",
                Tool.Gate => $"W|{h}",
                Tool.Paste when PasteBp != null => $"P|{PasteBp.Id}|{g}|{PasteRot}",
                _ => "",
            };
            if (key.Length > 0) key += "|" + opts;
        }
        if (key == _planKey) return;
        if (key.Length > 0 && _planKey.Length > 1 && _clock - _planClock < .05) return;
        _planKey = key;
        _planClock = _clock;
        if (key.Length == 0) { _plan = null; _ghost.Visible = false; PlanText = null; return; }

        var site = _m.Site;
        var gp = _hover.Ground(w);
        double r = NibR;
        string name = dig ? "Копать" : ToolName(Tool);
        var stroke = StrokeNow();
        int lv = Shapes.Level(w, gp.x, gp.z, stroke);
        if (key[0] == 'T') { _plan = Shapes.Tree(w, _tree.X, _tree.Y, _tree.Z); name = "Срубить дерево"; }
        else if (dragging) _plan = PlanDrag(_startPt, _dragPt, dig);
        else if (Tool is Tool.Line or Tool.Box or Tool.Circle)
        {
            _plan = Shapes.Line(w, site, gp.x, gp.z, gp.x, gp.z, stroke, lv);
            name = dig ? "Копать, начало" : ToolName(Tool) + ", начало";
        }
        else if (Tool == Tool.Brush && Opt.Form == BrushForm.Column) _plan = Shapes.Line(w, site, gp.x, gp.z, gp.x, gp.z, stroke, _paint ? _level : lv);
        else if (Tool == Tool.Brush && dig) _plan = Shapes.BallDig(w, site, _hover.X, _hover.Y, _hover.Z, Math.Max(.5, r));
        else if (Tool == Tool.Brush)
        {
            int off = Math.Max(1, (int)Math.Ceiling(r));
            _plan = Shapes.Ball(w, site, _hover.X + _hover.NX * off, _hover.Y + _hover.NY * off, _hover.Z + _hover.NZ * off, r, Opt.Material);
        }
        else if (Tool == Tool.Extrude)
        {
            _plan = Shapes.Extrude(w, site, _hover, Opt.Step, Opt.Material, MaxExtrudeFaces, out bool tooBig, dig);
            if (tooBig) { _ghost.Visible = false; PlanText = $"Грань больше {MaxExtrudeFaces} вокселей"; PlanAffordable = false; return; }
            if (dig) name = "Вдавить";
        }
        else if (Tool == Tool.Ladder) { _plan = Shapes.LadderAt(w, site, _hover); name = "Лестница"; dig = false; }
        else if (Tool == Tool.Gate) { _gatePlan = GatePlan.At(w, site, _hover); _plan = _gatePlan?.Leaves; name = "Ворота"; dig = false; }
        else if (Tool == Tool.Paste) { _plan = PlanPasteAtHover(); name = PasteBp.Name; dig = false; }
        if (key[0] == 'T') dig = true;
        _planDig = dig;

        double sec = Labour.Estimate(w, _plan, dig, _est);
        Array.Copy(_est, PlanNeeds, 4);
        PlanIsDig = dig;
        if (_plan == null || _plan.Count == 0 || sec <= 0) { _ghost.Visible = false; PlanText = null; return; }
        string shortOf = "";
        if (!dig)
            foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth })
                if (_est[k] > st[k] + .5) shortOf += (shortOf.Length > 0 ? ", " : "") + $"{Stockpile.Name(k)} {Fmt.M3(st[k])}";
        PlanAffordable = dig || shortOf.Length == 0;
        PlanText = $"{name}: {(Tool == Tool.Gate && _gatePlan != null ? GateSummary(_gatePlan) : Summary(_est, dig, sec))}" + (shortOf.Length > 0 ? $" — в запасе {shortOf}" : "");
        _ghost.Mesh = MeshPlan(_plan, dig);
        _ghost.Visible = _ghost.Mesh != null;
        _ghostMat.SetShaderParameter("tint", dig ? new Vector3(.85f, .2f, .14f) : PlanAffordable ? new Vector3(.9f, .55f, .25f) : new Vector3(.85f, .2f, .16f));
        _ghostMat.SetShaderParameter("tint_mix", dig ? .62f : PlanAffordable ? .35f : .6f);
        _ghostMat.SetShaderParameter("inflate", dig ? .1f : 0f);
    }

    ArrayMesh MeshPlan(VoxelList L, bool dig)
    {
        if (L == null || L.Count == 0) return null;
        int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
        var w = _m.World;
        for (int i = 0; i < L.Count; i++)
        {
            if (!dig && L.M(i) == 0) continue;
            int x = L.X(i), y = L.Y(i), z = L.Z(i);
            x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z);
            x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z);
        }
        if (x0 == int.MaxValue) return null;
        int dx = x1 - x0 + 1, dy = y1 - y0 + 1, dz = z1 - z0 + 1, px = dx + 2, pz = dz + 2;
        long size = (long)px * (dy + 2) * pz;
        if (size > 24_000_000) return null;
        if (_grid.Length < size) _grid = new byte[size]; else Array.Clear(_grid, 0, (int)size);
        for (int i = 0; i < L.Count; i++)
        {
            int x = L.X(i), y = L.Y(i), z = L.Z(i);
            byte m = dig ? w.Get(x, y, z) : L.M(i);
            if (m == 0 || (dig && !Labour.Diggable(m))) continue;
            _grid[((y - y0 + 1) * pz + (z - z0 + 1)) * px + (x - x0 + 1)] = m;
        }
        return GodotMesh.From(_mesher.Mesh(_grid, dx, dy, dz, x0, y0, z0));
    }
}
