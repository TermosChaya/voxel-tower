using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public enum Tool { None, Brush, Line, Box, Circle, Extrude, Ladder, Gate, Repair, Bridge, Gather, Unmark, Capture, Paste, Spell, Wand, Building, Yard, Template }

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
    /// <summary>The rectangle draws a house: walls, a roof of logs and a door, a dwelling once it stands.</summary>
    public bool House;
    /// <summary>The side of a house drawn its door goes in (Shapes.SideNormal), or −1: the first that opens onto free ground. {Rotate} goes round them.</summary>
    public int HouseDoor = -1;
    /// <summary>The line with the pick digs a countermine: a bore under the ground from its start, as deep as the depth, toward the horde's mine.</summary>
    public bool Mine;
    public bool Level = true;
    /// <summary>The bridge tool hangs a drawbridge instead of throwing an arch across.</summary>
    public bool DrawBridge;
    /// <summary>The magic wand: takes exactly the same voxel only (not any shade of stone with its mortar); takes what touches
    /// by an edge or a corner too; makes a blueprint of what it took instead of taking it down or laying it over again.</summary>
    public bool WandExact, WandCorners, WandCapture;
    /// <summary>The «Обустройство» tool: what of its palette is in hand (the ways first, then the things), and how a thing is turned.</summary>
    public int YardPick, YardRot;
    /// <summary>The «Шаблоны» tool: the template in hand (Sim.Templates), and how it is turned.</summary>
    public int TemplatePick, TemplateRot;
    /// <summary>The library's tab (defence, buildings, decor: Sim.TemplateTabs), and the decor in hand on its last tab.</summary>
    public int TemplateTab, TemplateDecor;
    /// <summary>Snapping to a grid round the citadel's middle, its step in voxels (0: off); the step it had when it was last on.</summary>
    public int Grid, GridLast = 8;

    public static readonly int[] Widths = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32 };
    public static readonly int[] Heights = { 4, 8, 12, 16, 24, 32, 40, 48, 56, 64, 80, 96 };
    public static readonly int[] Depths = { 4, 8, 12, 16, 24, 32, 48, 64, 96 };
    public static readonly int[] Steps = { 1, 2, 4, 8, 16 };
    public static readonly int[] Grids = { 0, 4, 8, 16 };

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
        Tool.Extrude => "Выдавить", Tool.Ladder => "Лестница", Tool.Gate => "Ворота", Tool.Repair => "Починка", Tool.Bridge => "Мост", Tool.Gather => "Делянка", Tool.Unmark => "Снять разметку", Tool.Capture => "Снять чертёж",
        Tool.Paste => "Чертёж", Tool.Spell => "Заклинание", Tool.Wand => "Волшебная палочка", Tool.Building => "Здание", Tool.Yard => "Обустройство", Tool.Template => "Шаблоны", _ => "Без кисти",
    };

    public static string Hint(Tool t, bool dig) => Bindings.Fill(HintText(t, dig));

    static string HintText(Tool t, bool dig) => (t, dig) switch
    {
        (Tool.Brush, false) => "Ведите по земле: рабочие выложат стену до заданной высоты. {Form} — шар. Shift или кирка ({Pick}) — копать.",
        (Tool.Brush, true) => "Ведите по земле: рабочие выкопают полосу на заданную глубину. Шар ({Form}) вынимает ком. Щелчок по дереву — срубить.",
        (Tool.Line, false) => "Зажмите и протяните: стена заданной толщины и высоты.",
        (Tool.Line, true) => "Протяните: ров заданной ширины и глубины. Деревья на пути срубят. Вид «подкоп» ({Form}): ход под землёй " +
            "на заданной глубине, навстречу подкопу орды. Рабочие крепят его брёвнами.",
        (Tool.Box, false) => "От угла до угла. {Form} — сплошной, стенки или дом.",
        (Tool.Box, true) => "От угла до угла: котлован. {Level} — ровное дно или по рельефу.",
        (Tool.Circle, false) => "От центра к краю. {Form} — кольцо или сплошной.",
        (Tool.Circle, true) => "От центра к краю: круглая яма или кольцевой ров ({Form}).",
        (Tool.Extrude, false) => "Щёлкните по ровной грани, она нарастёт на шаг.",
        (Tool.Extrude, true) => "Щёлкните по ровной грани, её углубят на шаг.",
        (Tool.Ladder, _) => "Щёлкните по стене, башне или обрыву. Рабочие поставят лестницу. Орда тоже может по ней залезть.",
        (Tool.Gate, _) => "Щёлкните по стене. Рабочие прорубят проём и навесят ворота. Свои проходят, орде придётся их рубить.",
        (Tool.Repair, _) => "Щёлкните по разрушенной постройке, рабочие её восстановят. Рамкой можно чинить по частям.",
        (Tool.Bridge, _) => "Протяните от берега до берега. Камень ({MatStone}) или брёвна ({MatWood}), вид моста — внизу. {WidthLess} и {WidthMore} — ширина прохода.",
        (Tool.Gather, _) => "Обведите участок рамкой. {MatStone} — камень, {MatWood} — лес, {MatEarth} — земля, {MatMetal} — руда. Свободные рабочие будут добывать там сами.",
        (Tool.Unmark, _) => "Рамка снимает всю разметку и делянки внутри. Щелчок снимает разметку рядом и делянку под курсором.",
        (Tool.Capture, _) => "Обведите постройку рамкой, чтобы сохранить чертёж.",
        (Tool.Paste, _) => "Щелчок ставит копию. R поворачивает, Esc отменяет.",
        (Tool.Building, _) => "Щелчок ставит здание: рабочие построят стены, крышу и дверь и обставят его. {Rotate} поворачивает, {Form} меняет вид здания, Esc отменяет.",
        (Tool.Wand, true) => "Наведите на постройку: подсветится всё прилегающее из того же материала. Щелчок: рабочие это снесут.",
        (Tool.Wand, false) => "Наведите на постройку: подсветится всё прилегающее из того же материала. Щелчок: переложат из выбранного материала.",
        (Tool.Yard, _) => "Внизу выберите дорожку или вещь ({Form} — следующая). Дорожку протяните по земле, вещь поставьте щелчком, {Rotate} поворачивает.",
        (Tool.Template, _) => "Внизу выберите шаблон ({Form} — следующий). Щелчок ставит, {Rotate} поворачивает, {HeightLess} и {HeightMore} — высота. Стену протяните. Shift и протяжка — ряд.",
        _ => "{Lmb} — выбрать, рамкой — нескольких, {Rmb} — послать. {ToolBrush}–{ToolExtrude} — кисти, {Pick} — кирка, {Help} — все клавиши.",
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
    readonly double[] _est = new double[Stockpile.Kinds];
    readonly double[] _strokeEst = new double[Stockpile.Kinds];

    Node3D _voxSpace;
    MeshInstance3D _ghost, _shape, _cursor;
    // the grid's nodes round the cursor while snapping
    MultiMeshInstance3D _gridDots;
    string _gridKey = "";
    ShaderMaterial _ghostMat;
    StandardMaterial3D _shapeMat;
    readonly GreedyMesher _mesher = new() { Pooled = true };
    byte[] _grid = Array.Empty<byte>();
    VoxelList _plan;
    bool _planDig;
    string _planKey = "~";
    double _planClock, _clock, _revAt;
    // how long the last ghost took to plan and mesh, so that a large one (a blueprint's copy) is planned again for the
    // changing valley less often
    double _planMs;
    int _rev;

    /// <summary>What the stroke under the cursor asks for, or null; and whether the stores hold enough.</summary>
    public string PlanText { get; private set; }
    public bool PlanAffordable { get; private set; }
    /// <summary>Voxels the stroke under the cursor takes from each store (negative when digging fills it).</summary>
    public readonly double[] PlanNeeds = new double[Stockpile.Kinds];
    public bool PlanIsDig { get; private set; }
    /// <summary>The material under the cursor and what is marked there.</summary>
    public string HoverText { get; private set; } = "";
    /// <summary>The gate under the cursor, if the cursor is on one of its leaves.</summary>
    public Gateway HoverGate { get; private set; }
    /// <summary>The drawbridge under the cursor with no brush in hand, or null.</summary>
    public Drawbridge HoverBridge { get; private set; }
    /// <summary>The named place (a mine, a ruin) the cursor is over.</summary>
    public Landmark HoverPlace { get; private set; }
    /// <summary>The citadel is under the cursor: a click opens its muster.</summary>
    public bool HoverKeep { get; private set; }
    GatePlan _gatePlan;

    /// <summary>"брёвна 3,4 м³ · проём: камень +4,0 м³ · 6 мин работы одного".</summary>
    /// <summary>The gate tool hangs leaves of iron plate: metal was chosen for it.</summary>
    public bool IronGate => Opt.Material == Mat.Plate;

    string GateSummary(GatePlan gp)
    {
        var lay = new double[Stockpile.Kinds]; var cut = new double[Stockpile.Kinds];
        double sec = Labour.Estimate(_m.World, gp.Leaves, false, lay) + Labour.Estimate(_m.World, gp.Cut, true, cut);
        var parts = new List<string>();
        foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth, Stockpile.Metal })
            if (lay[k] > .5) parts.Add($"{Stockpile.Name(k)} {Fmt.M3(lay[k])} м³");
        var got = new List<string>();
        foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth, Stockpile.Metal })
            if (cut[k] < -.5) got.Add($"{Stockpile.Name(k)} +{Fmt.M3(-cut[k])} м³");
        if (got.Count > 0) parts.Add("проём: " + string.Join(", ", got));
        parts.Add($"{Fmt.Duration(sec)} работы одного");
        return string.Join(" · ", parts);
    }

    public void Init(Main m)
    {
        _m = m;
        // the grid as the player left it last time
        if (Settings.Grid > 0) Opt.Grid = Opt.GridLast = Settings.Grid;
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
        _gridDots = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new PlaneMesh { Size = Vector2.One } },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                VertexColorUseAsAlbedo = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_gridDots);
    }

    // ---------------------------------------------------------------- state

    public void SetTool(Tool t)
    {
        // what the tutorial has not come to yet stays shut
        if (t != Tool.None && t != Tool.Paste && t != Tool.Spell && t != Tool.Building && _m.Hud.Locked(t)) { Refuse(_m.Hud.LockedWhy); return; }
        // what telekinesis holds is let go when the spell is put away
        Throw();
        Tool = Tool == t ? Tool.None : t;
        // what the wand had chosen with Shift is let go with it
        WandDrop();
        // these only build
        if (Tool is Tool.Bridge or Tool.Repair or Tool.Gate or Tool.Ladder or Tool.Yard or Tool.Template) Opt.Dig = false;
        if (Tool != Tool.Paste) PasteBp = null;
        if (Tool != Tool.Building) { Placing = null; ShowOutcrops(false); }
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

    /// <summary>The spell in hand while the tool is a spell.</summary>
    public SpellKind CurrentSpell { get; private set; }

    /// <summary>Takes a spell in hand: the next click on the field (or a stroke, for the Hand) casts it.</summary>
    public void TakeSpell(SpellKind k)
    {
        Throw();
        Tool = Tool.Spell;
        CurrentSpell = k;
        PasteBp = null;
        _lmb = _paint = _hasStart = false;
        _planKey = "~";
        _m.Hud.OnToolChanged();
    }

    bool SpellStroke => Tool == Tool.Spell && Sim.SpellOf(CurrentSpell).Stroke;

    /// <summary>The weather the spell calls down: an index into <see cref="Sim.Summonable"/>.</summary>
    public int WeatherPick;
    /// <summary>Telekinesis has hold of something, and the button is still down: it follows the cursor.</summary>
    bool _holding;
    /// <summary>Where the cursor was last seen over the field.</summary>
    Vector2 _mouse;

    /// <summary>A material to lay; puts the pick away.</summary>
    public void SetMaterial(byte m)
    {
        if (_m.Hud.MaterialsLocked) { Refuse("материалы откроются дальше в обучении"); return; }
        Opt.Material = m; Opt.Dig = false; Opt.WandCapture = false; Shelter = Sanctum = Dwelling = false; Changed();
    }

    /// <summary>The area tool sets aside a shelter, not a resource to take.</summary>
    public bool Shelter;
    public void SetShelter(bool on) { Shelter = on; if (on) Sanctum = Dwelling = false; Changed(); }
    /// <summary>The area tool sets aside a sanctuary, where the priests pray.</summary>
    public bool Sanctum;
    public void SetSanctum(bool on) { Sanctum = on; if (on) Shelter = Dwelling = false; Changed(); }
    /// <summary>The area tool calls a room a dwelling, where the household sleeps.</summary>
    public bool Dwelling;
    public void SetDwelling(bool on) { Dwelling = on; if (on) Shelter = Sanctum = false; Changed(); }

    /// <summary>Takes up the pick (or puts it away): the brushes then mark digging instead of laying.</summary>
    public void SetDig(bool dig)
    {
        if (_m.Hud.PickLocked) { Refuse("кирка откроется дальше в обучении"); return; }
        Opt.Dig = dig;
        if (dig) Opt.WandCapture = false;
        if (dig && Tool is Tool.None or Tool.Capture or Tool.Unmark or Tool.Paste or Tool.Ladder or Tool.Gate or Tool.Repair or Tool.Bridge or Tool.Gather or Tool.Template) { Tool = Tool.Line; PasteBp = null; }
        Changed();
    }

    /// <summary>Call after any option changed: re-plans the ghost and updates the belt.</summary>
    public void Changed() { _planKey = "~"; _m.Hud.OnToolChanged(); }

    public void StepWidth(int dir)
    {
        // a house template: its depth across its door's wall
        if (Tool == Tool.Template && !DecorTab && TemplatePick.Key == "house") { StepHouseDepth(dir); return; }
        Opt.Width = ToolOptions.Next(ToolOptions.Widths, Opt.Width, dir);
        Changed();
    }

    /// <summary>The second number of the current tool: height, digging depth or extrusion step.</summary>
    public void StepHeight(int dir)
    {
        if (Tool == Tool.Template) { StepTemplateSize(dir); return; }
        if (Tool == Tool.Extrude) Opt.Step = ToolOptions.Next(ToolOptions.Steps, Opt.Step, dir);
        else if (Opt.Dig || Tool == Tool.Gather) Opt.Depth = ToolOptions.Next(ToolOptions.Depths, Opt.Depth, dir);
        else Opt.Height = ToolOptions.Next(ToolOptions.Heights, Opt.Height, dir);
        Changed();
    }

    public void ToggleForm()
    {
        if (Tool == Tool.Brush) Opt.Form = Opt.Form == BrushForm.Column ? BrushForm.Ball : BrushForm.Column;
        // the rectangle goes solid, walls, house and round again
        else if (Tool == Tool.Box) { if (Opt.House) { Opt.House = false; Opt.Hollow = false; } else if (Opt.Hollow) Opt.House = true; else Opt.Hollow = true; }
        // the wand: any shade of the material, or exactly the one under the cursor
        else if (Tool == Tool.Wand) Opt.WandExact = !Opt.WandExact;
        // the line with the pick: a ditch, or a countermine under the ground
        else if (Tool == Tool.Line) Opt.Mine = !Opt.Mine;
        // the settlement's ways and things: on to the next of the palette
        else if (Tool == Tool.Yard) { StepYard(1); return; }
        // the templates: on to the next of the library
        else if (Tool == Tool.Template) { StepTemplate(1); return; }
        // a town building: on to the next of its looks
        else if (Tool == Tool.Building) { StepLook(); Changed(); return; }
        else Opt.Hollow = !Opt.Hollow;
        Changed();
    }

    public void ToggleLevel() { Opt.Level = !Opt.Level; Changed(); }

    /// <summary>Snapping to the grid on or off; it comes back with the step it had.</summary>
    public void ToggleGrid()
    {
        if (Opt.Grid > 0) { Opt.GridLast = Opt.Grid; Opt.Grid = 0; }
        else Opt.Grid = Opt.GridLast;
        Settings.KeepGrid(Opt.Grid);
        _m.Hud.Float(Opt.Grid > 0 ? $"сетка: {Inspector.Len(Opt.Grid)}" : "сетка выключена", Ornament.BronzeLight);
        Changed();
    }

    public void SetGrid(int step) { Opt.Grid = step; if (step > 0) Opt.GridLast = step; Settings.KeepGrid(step); Changed(); }

    /// <summary>Strokes of this tool start and end on the grid while snapping is on.</summary>
    bool Snapping => Opt.Grid > 0 && Tool is Tool.Line or Tool.Box or Tool.Circle or Tool.Capture or Tool.Unmark or Tool.Gather or Tool.Repair or Tool.Paste or Tool.Bridge;

    /// <summary>
    /// A point moved to the nearest node of the grid, which runs from the citadel's middle: what is drawn on it comes
    /// out even, and what is drawn the same on either side of the citadel comes out symmetrical.
    /// </summary>
    (int x, int y, int z) Snap((int x, int y, int z) p)
    {
        if (!Snapping) return p;
        int g = Opt.Grid, ox = _m.Site.KeepX, oz = _m.Site.KeepZ;
        int x = ox + (int)Math.Round((p.x - ox) / (double)g) * g, z = oz + (int)Math.Round((p.z - oz) / (double)g) * g;
        return (Math.Clamp(x, 0, _m.World.SX - 1), p.y, Math.Clamp(z, 0, _m.World.SZ - 1));
    }

    /// <summary>A line on the grid keeps to the eight directions: along x, along z or at 45°, a whole number of steps long.</summary>
    (int x, int y, int z) Straighten((int x, int y, int z) a, (int x, int y, int z) b)
    {
        int g = Opt.Grid, dx = b.x - a.x, dz = b.z - a.z;
        if (dx == 0 && dz == 0) return b;
        int oct = ((int)Math.Round(Math.Atan2(dz, dx) / (Math.PI / 4)) % 8 + 8) % 8;
        int ux = oct is 0 or 1 or 7 ? 1 : oct is 3 or 4 or 5 ? -1 : 0, uz = oct is 1 or 2 or 3 ? 1 : oct is 5 or 6 or 7 ? -1 : 0;
        double along = ux != 0 && uz != 0 ? (Math.Abs(dx) + Math.Abs(dz)) / 2.0 : Math.Abs(ux != 0 ? dx : dz);
        int n = (int)Math.Round(along / g) * g;
        return (Math.Clamp(a.x + ux * n, 0, _m.World.SX - 1), b.y, Math.Clamp(a.z + uz * n, 0, _m.World.SZ - 1));
    }

    /// <summary>The ground under the cursor, on the grid when snapping.</summary>
    (int x, int y, int z) HoverGround() => Snap(_hover.Ground(_m.World));

    /// <summary>What a gathering area takes: the material in hand says which; or a shelter.</summary>
    public int GatherKind => Shelter ? Zone.Shelter : Sanctum ? Zone.Sanctum : Dwelling ? Zone.Dwelling : Opt.Material switch { Mat.Wood => Stockpile.Wood, Mat.Rammed => Stockpile.Earth, Mat.Plate => Stockpile.Metal, _ => Stockpile.Stone };

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
                _mouse = mm.Position;
                Pick(mm.Position);
                if (Tool == Tool.None) _m.Command.Motion(mm);
                else if (_lmb && _paint) PaintTo();
                break;
            case InputEventMouseButton mb when mb.ButtonIndex == Bindings.First && !mb.AltPressed:
                _mouse = mb.Position;
                Pick(mb.Position);
                if (Tool == Tool.None && !_lmb) { _m.Command.Button(mb); break; }
                if (mb.Pressed) Press(mb);
                else if (_lmb) Release(mb);
                break;
            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: global::Godot.Key.Z } z when z.CtrlPressed || z.MetaPressed:
                Undo();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey k when k.Pressed && !k.CtrlPressed && !k.MetaPressed:
                if (Key(k)) GetViewport().SetInputAsHandled();
                break;
        }
    }

    bool Key(InputEventKey k)
    {
        var code = k.PhysicalKeycode;
        // with the menu open only Esc means anything; with the map of valleys open, Esc shuts it
        if (_m.Hud.MenuOpen && code != global::Godot.Key.Escape) return false;
        if (_m.Hud.Board?.IsOpen == true && !_m.Hud.MenuOpen)
        {
            if (code != global::Godot.Key.Escape) return false;
            _m.Hud.Board.Close();
            return true;
        }
        // Shift and a digit calls up a squad, it is no tool
        if (k.ShiftPressed && code is >= global::Godot.Key.Key0 and <= global::Godot.Key.Key9) return false;
        var act = Bindings.Find(code, Bindings.Scope.Field);
        // only the steps of the thickness and the height go on while their key is held
        if (k.Echo && act is not (Act.WidthLess or Act.WidthMore or Act.HeightLess or Act.HeightMore)) return false;
        Tool t = act switch
        {
            Act.ToolBrush => Tool.Brush, Act.ToolLine => Tool.Line, Act.ToolBox => Tool.Box, Act.ToolCircle => Tool.Circle,
            Act.ToolExtrude => Tool.Extrude, Act.ToolUnmark => Tool.Unmark, Act.ToolLadder => Tool.Ladder, Act.ToolGather => Tool.Gather,
            Act.ToolGate => Tool.Gate, Act.ToolRepair => Tool.Repair, Act.ToolCapture => Tool.Capture, Act.ToolBridge => Tool.Bridge, Act.ToolWand => Tool.Wand,
            Act.ToolYard => Tool.Yard, Act.ToolTemplate => Tool.Template,
            _ => Tool.None,
        };
        if (t != Tool.None) { SetTool(t); return true; }
        if (code == global::Godot.Key.Escape)
        {
            // a brush in hand is put down, the chosen let go; with nothing of that, the menu
            if (_m.Hud.MenuOpen) _m.Hud.MenuBack();
            // the wand's chosen regions let go before the wand itself
            else if (Tool == Tool.Wand && WandHeld > 0) WandDrop();
            else if (Tool != Tool.None) SetTool(Tool.None);
            else if (_m.Selected.Count > 0 || _m.Inspected != null || _m.KeepChosen) { _m.Command.ClearSelection(); _m.Inspected = null; _m.KeepChosen = false; _m.Hud.OnSelectionChanged(); }
            else _m.Hud.OpenMenu();
            return true;
        }
        switch (act)
        {
            case Act.MatStone: SetMaterial(Mat.Stone); return true;
            case Act.MatWood: SetMaterial(Mat.Wood); return true;
            case Act.MatEarth: SetMaterial(Mat.Rammed); return true;
            case Act.MatMetal: SetMaterial(Mat.Plate); return true;
            case Act.Pick: SetDig(!Opt.Dig); return true;
            case Act.WidthLess: StepWidth(-1); return true;
            case Act.WidthMore: StepWidth(1); return true;
            case Act.HeightLess: StepHeight(-1); return true;
            case Act.HeightMore: StepHeight(1); return true;
            case Act.Form: ToggleForm(); return true;
            case Act.Level: ToggleLevel(); return true;
            case Act.Grid: ToggleGrid(); return true;
            case Act.Rotate when Tool == Tool.Paste: PasteRot = (PasteRot + 1) & 3; Changed(); return true;
            case Act.Rotate when Tool == Tool.Building: TurnBuilding(); Changed(); return true;
            case Act.Rotate when Tool == Tool.Yard: Opt.YardRot = (Opt.YardRot + 1) & 3; Changed(); return true;
            case Act.Rotate when Tool == Tool.Template: Opt.TemplateRot = (Opt.TemplateRot + 1) & 3; Changed(); return true;
            // a house drawn: its door goes round the sides, from where it opens by itself
            case Act.Rotate when Tool == Tool.Box && Opt.House: Opt.HouseDoor = Opt.HouseDoor >= 3 ? -1 : Opt.HouseDoor + 1; Changed(); return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- taking back

    /// <summary>What the marking being drawn is called, for the chronicle when it is taken back.</summary>
    string _markName;

    /// <summary>How many markings of the pick the hand has made (a first ditch lets out the bridge: Ui/Unfold.cs).</summary>
    public int DigMarkings;

    /// <summary>The hand is lifted: what it marked becomes one step for Ctrl+Z.</summary>
    void Lift()
    {
        int made = _m.Sim.MarkingsMade;
        _m.Sim.EndMarking(_markName ?? ToolName(Tool));
        if (Opt.Dig && _m.Sim.MarkingsMade > made) DigMarkings++;
        _markName = null;
    }

    /// <summary>Ctrl+Z: the last marking is taken back as far as it still can be; what is built or dug stays.</summary>
    public void Undo()
    {
        if (_lmb) return;
        var mk = _m.Sim.UndoMarking(out int n);
        if (mk == null) { Refuse("отменять нечего"); return; }
        string what = mk.What.Length > 0 ? mk.What : "разметка";
        _m.Hud.Log($"Отменено: {what}" + (n > 0 ? $" ({Fmt.M3(n)} м³ разметки)" : ""));
        _m.Hud.Float($"отменено: {what.ToLowerInvariant()}", Ornament.BronzeLight);
        _planKey = "~";
    }

    void Press(InputEventMouseButton mb)
    {
        _markName = null;
        _m.Sim.BeginMarking();
        _lmb = true;
        _pressPos = mb.Position;
        _shift = mb.ShiftPressed;
        _hasStart = _hasHover;
        _strokeVox = 0; _strokeSec = 0; Array.Clear(_strokeEst); _strokePlan.Clear();
        if (!_hasHover) return;
        var w = _m.World;
        if (Tool == Tool.Spell && CurrentSpell == SpellKind.Telekinesis) { GrabHere(); return; }
        _startPt = Tool == Tool.Brush ? _hover.Ground(w) : HoverGround();
        _level = Shapes.Level(w, _startPt.x, _startPt.z, StrokeNow());
        if (Tool == Tool.Brush)
        {
            if (DigNow && _hasTree) { FellTree(); _lmb = false; Lift(); return; }
            _paint = true;
            _lastG = (_startPt.x, _startPt.z);
            _lastHit = new Vector3(_hover.X, _hover.Y, _hover.Z);
            Dab(_hover);
        }
    }

    void Release(InputEventMouseButton mb)
    {
        try { Let(mb); }
        finally { Lift(); }
    }

    void Let(InputEventMouseButton mb)
    {
        _lmb = false;
        var w = _m.World;
        bool click = (mb.Position - _pressPos).Length() <= 8;
        if (Tool == Tool.Spell) { if (CurrentSpell == SpellKind.Telekinesis) Throw(); else CastHere(click); return; }
        bool dig = Opt.Dig || (Tool == Tool.Brush && _shift);
        if (_paint)
        {
            _paint = false;
            _markName = dig ? "Копать" : ToolName(Tool);
            if (_strokeVox > 0)
            {
                _m.Hud.Log($"{_markName}: {Summary(_strokeEst, dig, _strokeSec)}");
                Marked(_markName, _strokePlan, dig, _strokeVox);
            }
            return;
        }
        // a template set where it was clicked, or drawn along the drag (BuildTools.Template.cs)
        if (Tool == Tool.Template) { PutTemplateHere(click); return; }
        if (Tool is Tool.Line or Tool.Box or Tool.Circle or Tool.Capture or Tool.Unmark or Tool.Gather or Tool.Repair or Tool.Bridge || Paving)
        {
            if (click && Tool == Tool.Unmark) { UnmarkAt(); return; }
            if (click && Tool == Tool.Repair) { RepairAt(); return; }
            if (click && Tool == Tool.Bridge) { Refuse("протяните мост от берега до берега"); return; }
            if (click && Tool == Tool.Gather) { Refuse("протяните рамку по участку"); return; }
            if (click && dig && _hasTree) { FellTree(); return; }
            if (!_hasStart || !(_hasDrag || _hasHover)) return;
            var a = _startPt; var b = _hasDrag ? _dragPt : HoverGround();
            if (Tool == Tool.Capture) { Capture(a, b); return; }
            if (Tool == Tool.Unmark)
            {
                _markName = "Снятие разметки";
                int n = _m.Sim.CancelArea(a.x, a.z, b.x, b.z), zones = _m.Sim.RemoveZones(a.x, a.z, b.x, b.z);
                if (n == 0 && zones == 0) Refuse("в рамке нет разметки");
                else _m.Hud.Float((n > 0 ? $"разметка снята: {Fmt.M3(n)} м³" : "") + (zones > 0 ? (n > 0 ? ", " : "") + $"участков снято: {zones}" : ""), Ornament.BronzeLight);
                return;
            }
            if (Tool == Tool.Gather) { SetZone(a, b); return; }
            if (Tool == Tool.Repair) { RepairIn(a, b); return; }
            if (Tool == Tool.Bridge && Opt.DrawBridge) { HangDrawbridge(a, b); return; }
            if (Tool == Tool.Bridge && BridgeRefusal(a, b) is { } why) { Refuse(why); return; }
            if (Tool == Tool.Box && Opt.House && !dig) { MarkHouse(a, b); return; }
            if (Tool == Tool.Line && Opt.Mine && dig) { MarkCountermine(a, b); return; }
            if (Paving) { MarkWay(a, b); return; }
            Designate(PlanDrag(a, b, dig), dig, ToolName(Tool), arched: Tool == Tool.Bridge);
            return;
        }
        if (!click || !_hasHover) return;
        if (Tool == Tool.Ladder)
        {
            var plan = Shapes.LadderAt(w, _m.Site, _hover);
            if (plan.Count == 0) { Refuse("лестницу ставят к стене или обрыву"); return; }
            Designate(plan, false, "Лестница");
        }
        else if (Tool == Tool.Gate)
        {
            // on a gate: walled up again (Sim.Houses.cs)
            if (HoverGate is { } there)
            {
                string no = _m.Sim.WallUp(there);
                if (no != null) { Refuse(no); return; }
                _markName = "Заложить ворота";
                _m.Hud.Log("Ворота заложат: рабочие снимут створки и заложат проём тем же, из чего стена.", Hud.Kind.Ok);
                Sound.Ui("mark");
                _planKey = "~";
                return;
            }
            // on a house's wall: a door of the house, its leaves outside, where the ground before it is free
            if (_m.Sim.HouseWallAt(_hover, out var house, out int side, out int along))
            {
                string no = _m.Sim.AddHouseDoor(house, side, along);
                if (no != null) { Refuse($"Дверь: {no}"); return; }
                _markName = "Дверь";
                _m.Hud.Log($"{house.Name}: новая дверь {Shapes.SideWords[side]}. Рабочие прорубят её и навесят створки. Ненужную дверь заложите щелчком воротами по ней.", Hud.Kind.Ok);
                Sound.Ui("mark");
                _planKey = "~";
                return;
            }
            var gp = GatePlan.At(w, _m.Site, _hover, iron: IronGate);
            if (gp == null) { Refuse("ворота ставят в стену толщиной до 4 м"); return; }
            _markName = "Ворота";
            _m.Sim.AddGate(gp);
            _m.Hud.Log("Ворота: " + GateSummary(gp));
            Sound.Ui("mark");
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
        else if (Tool == Tool.Wand) WandAt();
        else if (Tool == Tool.Building) PutUpAtHover();
        else if (Tool == Tool.Yard) PlaceYard();
    }

    /// <summary>
    /// The spell in hand let go at the field: the Fall at the voxel aimed at, the Hand along the stroke, the rest at the
    /// ground under the cursor.
    /// </summary>
    void CastHere(bool click)
    {
        var sp = Sim.SpellOf(CurrentSpell);
        if (!_hasStart) return;
        string why;
        if (sp.Stroke)
        {
            if (!_hasDrag || click) { Refuse("проведите линию по земле"); return; }
            why = _m.Sim.Cast(CurrentSpell, _startPt.x + .5f, _startPt.y, _startPt.z + .5f, _dragPt.x + .5f, _dragPt.z + .5f);
        }
        else
        {
            if (!click || !_hasHover) return;
            var at = sp.AtVoxel ? (_hover.X, _hover.Y, _hover.Z) : HoverGround();
            why = _m.Sim.Cast(CurrentSpell, at.Item1 + .5f, at.Item2, at.Item3 + .5f, variant: WeatherPick);
        }
        if (why != null) Refuse(why);
        _planKey = "~";
        _m.Hud.Refresh();
    }

    /// <summary>Telekinesis takes hold of what is under the cursor as the button goes down.</summary>
    void GrabHere()
    {
        string why = _m.Sim.Cast(SpellKind.Telekinesis, _hover.X + .5f, _hover.Y + .5f, _hover.Z + .5f);
        _holding = why == null;
        if (why != null) Refuse(why);
        else Steer();
        _planKey = "~";
        _m.Hud.Refresh();
    }

    /// <summary>The hand that holds goes three metres over the ground under the cursor; the button let up (anywhere), it throws.</summary>
    void Steer()
    {
        if (_m.Sim.HeldNow == null) { _holding = false; return; }
        // the button let up anywhere at all, over the panels too (a script has no hand on the mouse: it lets go by its own events)
        if (!_lmb || !_m.Scripted && !Input.IsMouseButtonPressed(Bindings.First)) { Throw(); return; }
        // picked again every frame: the view may move under a still cursor
        Pick(_mouse);
        if (!_hasHover) return;
        var g = _hover.Ground(_m.World);
        _m.Sim.HoldAt(g.x + .5f, g.y + 3 * VoxelWorld.PerMetre, g.z + .5f);
    }

    void Throw()
    {
        if (!_holding) return;
        _holding = false;
        _m.Sim.LetGo();
    }

    // ---------------------------------------------------------------- mending

    // the building under the cursor, kept while the cursor stays on it and nothing meant changes
    List<int> _bld;
    readonly HashSet<int> _bldSet = new();
    int _bldRev = -1;
    (int x0, int z0, int x1, int z1) _bldBox;
    readonly VoxelList _mendLay = new(), _mendClear = new(), _mendGhost = new();
    List<Gateway> _mendGates = new();

    /// <summary>The building under the cursor (the voxel hit, the air in front of it, or right beside), or null.</summary>
    List<int> HoverBuilding()
    {
        if (!_hasHover) return null;
        var sim = _m.Sim;
        var f = _hover.Front;
        int seed = sim.MeantNear(f.x, f.y, f.z);
        if (seed < 0) return null;
        if (_bld != null && _bldRev == sim.DesignRev && _bldSet.Contains(seed)) return _bld;
        _bld = sim.BuildingFrom(seed, _bldSet);
        _bldRev = sim.DesignRev;
        int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
        foreach (int p in _bld)
        {
            var (x, _, z) = WaterPlanner.Unpack(p);
            x0 = Math.Min(x0, x); z0 = Math.Min(z0, z); x1 = Math.Max(x1, x); z1 = Math.Max(z1, z);
        }
        _bldBox = (x0, z0, x1, z1);
        return _bld;
    }

    /// <summary>What mending these meant voxels takes, and the broken gates in the rectangle with them.</summary>
    // how much of the mending in view is marked already and waits for the workers
    int _mendWaiting;

    void PlanMend(List<int> voxels, int x0, int z0, int x1, int z1)
    {
        _mendLay.Clear(); _mendClear.Clear(); _mendGhost.Clear();
        _mendWaiting = _m.Sim.MendPlan(voxels, _mendLay, _mendClear);
        _mendGates = _m.Sim.BrokenGatesIn(x0, z0, x1, z1);
        for (int i = 0; i < _mendLay.Count; i++) _mendGhost.Add(_mendLay.X(i), _mendLay.Y(i), _mendLay.Z(i), _mendLay.M(i));
        foreach (var g in _mendGates) _m.Sim.GateLeaves(g, _mendGhost);
    }

    /// <summary>"камень 3,2 м³ · завал 0,4 м³ · ворота · 5 мин работы одного".</summary>
    string MendSummary(out bool affordable)
    {
        var lay = new double[Stockpile.Kinds]; var cut = new double[Stockpile.Kinds];
        double sec = Labour.Estimate(_m.World, _mendGhost, false, lay) + Labour.Estimate(_m.World, _mendClear, true, cut);
        Array.Copy(lay, PlanNeeds, Stockpile.Kinds);
        var parts = new List<string>();
        string shortOf = "";
        var st = _m.Sim.Stock;
        foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth, Stockpile.Metal })
        {
            if (lay[k] > .5) parts.Add($"{Stockpile.Name(k)} {Fmt.M3(lay[k])} м³");
            if (lay[k] > st[k] + .5) shortOf += (shortOf.Length > 0 ? ", " : "") + $"{Stockpile.Name(k)} {Fmt.M3(st[k])}";
        }
        if (_mendClear.Count > 0) parts.Add($"разобрать завал {Fmt.M3(_mendClear.Count)} м³");
        if (_mendGates.Count > 0) parts.Add(_mendGates.Count == 1 ? "навесить ворота" : $"навесить ворота: {_mendGates.Count}");
        parts.Add($"{Fmt.Duration(sec)} работы одного");
        affordable = shortOf.Length == 0;
        return string.Join(" · ", parts) + (shortOf.Length > 0 ? $" — в запасе {shortOf}" : "");
    }

    /// <summary>A click: the whole building under the cursor is mended to what it was.</summary>
    void RepairAt()
    {
        var b = HoverBuilding();
        if (b == null)
        {
            // a gateway with its leaves gone has nothing meant in it: a click in it hangs the gate again
            _mendLay.Clear(); _mendClear.Clear(); _mendGhost.Clear();
            _mendGates = _hasHover ? _m.Sim.BrokenGatesIn(_hover.X - 24, _hover.Z - 24, _hover.X + 24, _hover.Z + 24) : new List<Gateway>();
            foreach (var g in _mendGates) _m.Sim.GateLeaves(g, _mendGhost);
            if (_mendGates.Count == 0) { Refuse("здесь нечего чинить"); return; }
        }
        else
        {
            var (x0, z0, x1, z1) = _bldBox;
            PlanMend(b, x0 - 8, z0 - 8, x1 + 8, z1 + 8);
        }
        Mend();
    }

    /// <summary>A frame: only what of the buildings stands in it is mended.</summary>
    void RepairIn((int x, int y, int z) a, (int x, int y, int z) b)
    {
        PlanMend(_m.Sim.MeantIn(a.x, a.z, b.x, b.z), a.x, a.z, b.x, b.z);
        Mend();
    }

    void Mend()
    {
        if (_mendGhost.Count == 0 && _mendClear.Count == 0)
        {
            if (_mendWaiting > 0) { _m.Hud.Float("починка уже размечена: рабочие придут, когда освободятся", Ornament.Moss); return; }
            Refuse("цело: чинить нечего");
            return;
        }
        string text = MendSummary(out _);
        _markName = "Починка";
        int n = _m.Sim.Mend(_mendLay, _mendClear);
        foreach (var g in _mendGates) n += _m.Sim.MendGate(g);
        if (n == 0) { Refuse("всё, что нужно, уже размечено"); return; }
        _m.Hud.Log($"Починка: {text}");
        Sound.Ui("mark");
        _planKey = "~";
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
        _markName = "Снятие разметки";
        int n = _m.Sim.CancelOrders(_hover.X, _hover.Y, _hover.Z, UnmarkR);
        var zone = _m.Sim.ZoneAt(_hover.X, _hover.Z);
        int zones = _m.Sim.RemoveZones(_hover.X, _hover.Z, _hover.X, _hover.Z);
        if (n == 0 && zones == 0) Refuse("здесь нет разметки");
        else _m.Hud.Float(zones > 0 ? (zone?.Kind switch { Zone.Shelter => "убежище снято", Zone.Sanctum => "святилище снято", Zone.Dwelling => "жилой дом снят", _ => "делянка снята" }) : $"разметка снята: {Fmt.M3(n)} м³", Ornament.BronzeLight);
    }

    /// <summary>"Делянка: деревьев 12, ≈ 82,0 м³ брёвен" or "Каменоломня: ≈ 9,5 м³ камня до 2 м вглубь".</summary>
    string SurveyText(int x0, int z0, int x1, int z1, out bool any)
    {
        int kind = GatherKind;
        if (kind == Zone.Shelter)
        {
            // how many it holds: a place to a cell of standing room
            int places = _m.Sim.ShelterRoom(x0, z0, x1, z1);
            any = places > 0;
            return places == 0 ? "Убежище: здесь негде встать" : $"Убежище: {places} {PlaceWord(places)}, сюда уходят по тревоге";
        }
        if (kind == Zone.Dwelling)
        {
            // whether the room in the frame is closed, roofed and has its door, and how many bunks it takes
            var room = _m.Sim.CheckDwelling(x0, z0, x1, z1);
            any = room.Ok || room.Area > 0 || room.Why.StartsWith("нет") || room.Why.StartsWith("стены");
            return room.Ok ? $"Жилой дом: {Fmt.One(room.Area)} м², лежанок {room.Bunks}, +{room.Bunks} {PlaceWord(room.Bunks)} в войске"
                : $"Жилой дом: {Bindings.Fill(room.Why)}";
        }
        if (kind == Zone.Sanctum)
        {
            var (room, power) = _m.Sim.SanctumRoom(x0, z0, x1, z1);
            any = room > 0;
            if (room == 0) return "Святилище: здесь негде встать";
            string at = power == null ? "на месте силы молитва дала бы втрое больше"
                : power.Holder == Holder.Ours ? $"на месте силы «{power.Name}»: молитва даёт втрое больше"
                : power.Holder == Holder.Horde ? $"место силы «{power.Name}» у орды: пока его не отбить, святилище не работает"
                : $"на месте силы «{power.Name}»: когда жрецы его займут, молитва даст втрое больше";
            return $"Святилище: {room} {PlaceWord(room)} для молитвы, {at}";
        }
        var (trees, vox) = _m.Sim.Survey(x0, z0, x1, z1, kind, Opt.Depth);
        any = vox > 0;
        string name = Zone.Name0(kind);
        if (kind == Stockpile.Wood) return trees == 0 ? $"{name}: деревьев нет" : $"{name}: деревьев {trees}, ≈ {Fmt.M3(vox)} м³ брёвен";
        string depth = Len(Opt.Depth);
        // a mine: the ore in reach, and the metal it gives (half of it)
        if (kind == Stockpile.Metal)
            return vox <= 0 ? $"{name}: руды здесь нет до {depth} вглубь" : $"{name}: ≈ {Fmt.M3(vox)} м³ руды до {depth} вглубь, металла выйдет ≈ {Fmt.M3(vox / 2)} м³";
        return vox <= 0 ? $"{name}: {(kind == Stockpile.Stone ? "скалы" : "земли")} здесь нет" : $"{name}: ≈ {Fmt.M3(vox)} м³ {(kind == Stockpile.Stone ? "камня" : "земли")} до {depth} вглубь";
    }

    static string PlaceWord(int n)
    {
        int m10 = n % 10, m100 = n % 100;
        return m10 == 1 && m100 != 11 ? "место" : m10 is >= 2 and <= 4 && m100 is < 12 or > 14 ? "места" : "мест";
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
        _markName = Zone.Name0(GatherKind);
        _m.Sim.AddZone(a.x, a.z, b.x, b.z, GatherKind, Opt.Depth);
        Sound.Ui("mark");
        _m.Hud.Log(text + (GatherKind switch
        {
            Zone.Shelter => ". Тревога — T.",
            Zone.Sanctum => ". Днём здесь молятся жрецы: их набирают в цитадели (щелчок по ней или K).",
            Zone.Dwelling => text.Contains("лежанок") ? ". Рабочие обставят его из запаса брёвен; каждая готовая лежанка — место в войске."
                : ". Отметка подождёт: когда дом закроют — стены, крыша, ворота ({ToolGate}) — рабочие обставят его. " +
                  "Построить дом целиком: прямоугольник ({ToolBox}) с заливкой «дом» ({Form}).",
            _ => ". Свободные рабочие возьмутся за неё сами.",
        }));
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
            Tool.Line when Opt.Mine && dig => _m.Sim.PlanCountermine(a.x, a.z, b.x, b.z, Opt.Depth, out _),
            Tool.Line => Shapes.Line(w, site, a.x, a.z, b.x, b.z, st, _level),
            Tool.Box when Opt.House && !dig => HousePreview(a, b, st),
            Tool.Box => Shapes.Box(w, site, a.x, a.z, b.x, b.z, st, _level),
            Tool.Circle => Shapes.Circle(w, site, a.x, a.z, Radius(a, b), st, _level),
            Tool.Bridge => Bridges.Plan(w, site, a.x, a.z, b.x, b.z, Opt.Width, BridgeMat),
            _ => null,
        };
        return dig ? Shapes.WithTrees(_m.World, plan) : plan;
    }

    /// <summary>
    /// A countermine from where the drag began to where it ends: its bore marked to be dug (the workers shore it with logs
    /// as they go), kept by the sim to break into the horde's mine where it meets it.
    /// </summary>
    void MarkCountermine((int x, int y, int z) a, (int x, int y, int z) b)
    {
        var plan = _m.Sim.PlanCountermine(a.x, a.z, b.x, b.z, Opt.Depth, out var mine);
        if (plan.Count == 0 || mine == null) { Refuse("подкоп ведут от земли вниз и вдаль, протяните его"); return; }
        if (!Designate(plan, true, "Подкоп")) return;
        _m.Sim.AddCountermine(mine);
        _m.Hud.Log($"Подкоп: {Inspector.Len(mine.Cells.Count * VoxelWorld.PerMetre)} под землёй. Рабочие роют его и крепят брёвнами, без брёвен он осыплется.", Hud.Kind.Ok);
    }

    /// <summary>
    /// A house from corner to corner: its walls, floor and roof marked (the roof to stand as an arch), its door to be cut
    /// once the wall stands, its floor a dwelling. Drawn over a house, a second storey on it, or why not.
    /// </summary>
    void MarkHouse((int x, int y, int z) a, (int x, int y, int z) b)
    {
        var st = StrokeNow();
        st.Dig = false;
        int x0 = Math.Min(a.x, b.x), z0 = Math.Min(a.z, b.z), x1 = Math.Max(a.x, b.x), z1 = Math.Max(a.z, b.z);
        // drawn up against a house it shares its wall (Sim.Houses.cs); over one's room it is a storey on it
        var plan = _m.Sim.PlanHouseAt(x0, z0, x1, z1, st, Opt.HouseDoor, out var door, out string why, out var arched, out var rect, out int shared, out var under, out var moves);
        if (under != null)
        {
            string no = _m.Sim.PlanStorey(under, x0, z0, x1, z1, st.Mat);
            if (no != null) { Refuse(no); return; }
            _markName = "Второй этаж";
            Sound.Ui("mark");
            _m.Hud.Log("Второй этаж: рабочие снимут крышу, поставят лестницу, настелют пол и поднимут стены с новой крышей.", Hud.Kind.Ok);
            return;
        }
        if (plan == null) { Refuse(Bindings.Fill(why)); return; }
        if (!Designate(plan, false, "Дом")) return;
        _m.Sim.Arch(arched);
        _m.Sim.PlanHouse(door, rect.x0, rect.z0, rect.x1, rect.z1);
        _m.Sim.MoveDoors(moves);
        var eaves = _m.Sim.EavesOverHouse(rect);
        if (eaves.Count > 0) _m.Sim.DesignateRemove(eaves);
        HousesDrawn++;
        _m.Hud.Log($"Дом: дверь {Shapes.SideWords[door.Side]}" + (shared != 0 ? ", стена общая с соседним домом" : "") +
            ". Когда стены встанут, рабочие прорубят дверь и навесят ворота, потом поставят лежанки. Каждая лежанка — место в войске.", Hud.Kind.Ok);
    }

    /// <summary>Houses drawn with the rectangle this game (the tests ask).</summary>
    public int HousesDrawn;

    /// <summary>What a house drawn now says over its ghost: where its door goes, that it shares a wall, or why not.</summary>
    string _houseNote;
    /// <summary>The rectangle of the house last drawn, as the mouse gave it (for the self-test to say).</summary>
    public (int x0, int z0, int x1, int z1) HouseDrawn { get; private set; }
    /// <summary>The voxel face under the cursor, if any (for the self-test to say).</summary>
    public VoxelHit? Hovered => _hasHover ? _hover : null;

    /// <summary>What a house drawn here would be: a house, or over a house the storey that would go up on it.</summary>
    VoxelList HousePreview((int x, int y, int z) a, (int x, int y, int z) b, Stroke st)
    {
        int x0 = Math.Min(a.x, b.x), z0 = Math.Min(a.z, b.z), x1 = Math.Max(a.x, b.x), z1 = Math.Max(a.z, b.z);
        HouseDrawn = (x0, z0, x1, z1);
        var plan = _m.Sim.PlanHouseAt(x0, z0, x1, z1, st, Opt.HouseDoor, out var door, out string why, out _, out _, out int shared, out var under);
        if (under != null)
        {
            _houseNote = "второй этаж на дом";
            return under.Depth > 1 ? Shapes.Storey(_m.World, _m.Site, under.X0, under.Z0, under.X1, under.Z1, under.Depth, st.Mat, out _, out _, out _) : null;
        }
        if (plan == null) { _houseNote = Bindings.Fill(why); return null; }
        _houseNote = $"дверь {Shapes.SideWords[door.Side]}" + (Opt.HouseDoor < 0 ? Bindings.Fill(" ({Rotate} — выбрать сторону)") : "") + (shared != 0 ? ", стена общая с соседом" : "");
        // the doorway shown open in the ghost, where it will be cut
        int threshold = door.Y - 8, across = door.NX != 0 ? door.Z : door.X, u0 = (across - Gateway.Half + 4) & ~7;
        var shown = new VoxelList();
        for (int i = 0; i < plan.Count; i++)
        {
            int x = plan.X(i), y = plan.Y(i), z = plan.Z(i), u = door.NX != 0 ? z : x, k = door.NX != 0 ? (door.X - x) * door.NX : (door.Z - z) * door.NZ;
            if (y >= threshold && y < threshold + Gateway.Height && u >= u0 && u < u0 + Gateway.Width && k >= 0 && k < Shapes.HouseWall) continue;
            shown.Add(x, y, z, plan.M(i));
        }
        return shown;
    }

    /// <summary>A drawbridge from the bank at A over the gap to B, hinged on the bank nearer the citadel.</summary>
    void HangDrawbridge((int x, int y, int z) a, (int x, int y, int z) b)
    {
        var plan = Drawbridge.Plan(_m.World, _m.Site, a.x, a.z, b.x, b.z, Opt.Width, out string why);
        if (plan == null) { Refuse(why); return; }
        _markName = "Подъёмный мост";
        _m.Sim.AddDrawbridge(plan);
        Sound.Ui("mark");
        _m.Hud.Log($"Подъёмный мост: {Fmt.One(plan.Length / (double)VoxelWorld.PerMetre)} м. Когда подходит орда, мост поднимается сам. Щелчок по мосту без кисти: сам, опущен или поднят.");
    }

    /// <summary>Bridges are of stone or of timber; earth in hand means stone.</summary>
    byte BridgeMat => Opt.Material == Mat.Wood ? Mat.Wood : Mat.Stone;

    /// <summary>Why a bridge cannot go from A to B, or null.</summary>
    static string BridgeRefusal((int x, int y, int z) a, (int x, int y, int z) b)
    {
        double span = Brushes.Dist(b.x - a.x, b.z - a.z);
        if (span < Bridges.MinSpan) return "мост короче двух метров не нужен";
        if (span > Bridges.MaxSpan) return $"пролёт длиннее {Bridges.MaxSpan / VoxelWorld.PerMetre} м не выстоит";
        return null;
    }

    /// <summary>A circle's radius from its middle to the cursor; on the grid, in whole half-steps.</summary>
    double Radius((int x, int y, int z) a, (int x, int y, int z) b)
    {
        double d = Brushes.Dist(b.x - a.x, b.z - a.z);
        if (Snapping) { double half = Opt.Grid / 2.0; d = Math.Max(half, Math.Round(d / half) * half); }
        return d + .5;
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
        for (int k = 0; k < Stockpile.Kinds; k++) _strokeEst[k] += _est[k];
        int n = dig ? _m.Sim.DesignateRemove(plan) : _m.Sim.DesignateBuild(plan);
        _strokeVox += n;
        if (n > 0) for (int i = 0; i < plan.Count; i++) _strokePlan.Add(plan.X(i), plan.Y(i), plan.Z(i), plan.M(i));
        _planKey = "~";
    }

    /// <summary>Marks a whole shape for the pawns and writes in the chronicle what it will take.</summary>
    bool Designate(VoxelList plan, bool dig, string what, bool arched = false)
    {
        if (plan == null || plan.Count == 0) { Refuse(dig ? "здесь нечего копать" : "здесь нечего класть: всё уже занято"); return false; }
        double sec = Labour.Estimate(_m.World, plan, dig, _est);
        int n = dig ? _m.Sim.DesignateRemove(plan) : _m.Sim.DesignateBuild(plan, arched);
        if (n == 0) { Refuse(dig ? "здесь нечего копать" : "здесь нечего класть: всё уже занято"); return false; }
        _markName = what;
        _m.Hud.Log($"{what}: {Summary(_est, dig, sec)}");
        Marked(what, plan, dig, n);
        _planKey = "~";
        return true;
    }

    /// <summary>"камень 2,5 м³ · 3 мин работы одного" or, digging, "земля +1,8 · камень +0,4 м³ · 2 мин работы одного".</summary>
    public static string Summary(double[] stores, bool dig, double seconds)
    {
        var parts = new List<string>();
        foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth, Stockpile.Metal })
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
        Sound.Ui("nope");
    }

    // every voxel a freehand stroke marked, to follow the stroke's work to its end
    readonly VoxelList _strokePlan = new();

    /// <summary>Work marked: a knock heard, said by the pointer, and followed till it is done.</summary>
    void Marked(string what, VoxelList plan, bool dig, int voxels)
    {
        Sound.Ui("mark");
        _m.Hud.Float($"{what}: {Fmt.M3(voxels)} м³", Ornament.Moss);
        _m.Progress?.Track(what, plan, dig);
    }

    // ---------------------------------------------------------------- picking and previews

    void Pick(Vector2 screen)
    {
        var (o, d) = _m.Rig.Ray(screen);
        var w = _m.World;
        double ox = (o.X - w.OriginX) / VoxelWorld.Vox, oy = o.Y / VoxelWorld.Vox, oz = (o.Z - w.OriginZ) / VoxelWorld.Vox;
        _hasHover = Raycast.Cast(w, ox, oy, oz, d.X, d.Y, d.Z, out _hover, skipVeg: true);
        _hasTree = false;
        if (DigNow && Raycast.Cast(w, ox, oy, oz, d.X, d.Y, d.Z, out var veg, skipGrass: true) && (Mat.IsTrunk(veg.M) || Mat.IsLeafy(veg.M)))
        {
            _tree = veg;
            _hasTree = true;
        }
        _hasDrag = false;
        if (_lmb && _hasStart && (Tool is Tool.Line or Tool.Box or Tool.Circle or Tool.Capture or Tool.Unmark or Tool.Gather or Tool.Repair || Paving || ColumnStroke || SpellStroke || TemplateDrag) && Math.Abs(d.Y) > 1e-4f)
        {
            float t = (w.WY(_startPt.y) - o.Y) / d.Y;
            if (t > 0)
            {
                var p = o + d * t;
                _dragPt = (Math.Clamp(w.VX(p.X), 0, w.SX - 1), _startPt.y, Math.Clamp(w.VZ(p.Z), 0, w.SZ - 1));
                if (!ColumnStroke)
                {
                    _dragPt = Snap(_dragPt);
                    if (Tool == Tool.Line && Snapping) _dragPt = Straighten(_startPt, _dragPt);
                }
                _hasDrag = true;
            }
        }
        if (_lmb && _hasStart && Tool == Tool.Bridge && _hasHover)
        {
            // a bridge ends on the far bank, whatever its height: the ground under the cursor, not the plane it began on
            _dragPt = HoverGround();
            if (Snapping) _dragPt = Straighten(_startPt, _dragPt);
            _hasDrag = true;
        }
        HoverGate = _hasHover && Mat.IsLeaf(_hover.M) ? _m.Sim.GateAt(_hover.X, _hover.Y, _hover.Z) : null;
        HoverBridge = _hasHover && _hover.M == Mat.DrawDeck ? _m.Sim.DrawbridgeAt(_hover.X, _hover.Y, _hover.Z) : null;
        var site = _m.Site;
        HoverKeep = _hasHover && Brushes.Dist(_hover.X - site.KeepX + .5, _hover.Z - site.KeepZ + .5) <= site.KeepR + .5 && _hover.Y >= site.KeepBase - 2;
        HoverPlace = null;
        if (_hasHover)
            foreach (var l in _m.Site.Landmarks)
                if (Brushes.Dist(_hover.X - l.X, _hover.Z - l.Z) < l.R) { HoverPlace = l; break; }
        if (!_hasHover) { HoverText = ""; return; }
        int ground = w.GroundTop(_hover.X, _hover.Z);
        float above = (_hover.Y + 1 - ground) / (float)VoxelWorld.PerMetre;
        string text = _hasTree ? (Mat.IsTrunk(_tree.M) ? "дерево: ствол" : "дерево: крона")
            : Mat.IsPlayer[_hover.M] && above > .05f ? $"{Mat.NameOf(_hover.M)} · {Fmt.One(above)} м над землёй" : Mat.NameOf(_hover.M);
        byte order = _m.Sim.Orders.At(_hover.X, _hover.Y, _hover.Z);
        if (order == 0) { var f = _hover.Front; order = _m.Sim.Orders.At(f.x, f.y, f.z); }
        if (Orders.IsRemove(order)) text += " · размечено: копать";
        else if (order != 0) text += $" · размечено: {Mat.NameOf(order).ToLowerInvariant()}";
        var zone = _m.Sim.ZoneAt(_hover.X, _hover.Z);
        if (zone != null) text += $" · {zone.Name.ToLowerInvariant()}";
        // a house lived in: what it pays and what would make it pay more (Sim.Dues.cs)
        if (zone != null && _m.Sim.DuesLine(zone) is { Length: > 0 } dues) text += $" · {dues}";
        HoverText = text;
    }

    /// <summary>Points the tools at a spot on the map as if the mouse were there (screenshots and tests).</summary>
    public void PointAt(Vector3 world) => Pick(_m.Rig.Cam.UnprojectPosition(world));

    /// <summary>The list the blueprint's ghost is planned into, kept from one plan to the next.</summary>
    readonly VoxelList _pasteGhost = new();
    /// <summary>The plan the ghost was last drawn for, and whether as a dig: the same again is not drawn again.</summary>
    ulong _ghostHash;
    bool _ghostDig;

    VoxelList PlanPasteAtHover(VoxelList into = null)
    {
        if (PasteBp == null || !_hasHover) return null;
        var g = HoverGround();
        int w = PasteBp.RotW(PasteRot), d = PasteBp.RotD(PasteRot);
        return PasteBp.PlanPaste(_m.World, _m.Site, g.x - (w >> 1), g.z - (d >> 1), PasteRot, g.y, into);
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("BuildTools");
        _clock += delta;
        if (_holding) Steer();
        UpdateCursor();
        UpdateShape();
        UpdatePlan();
        UpdateGrid();
    }

    /// <summary>
    /// The grid laid on the ground round the cursor, as the brushes snap to it: thin lines from node to node over the
    /// lie of the land, a dot at each node, the two lines through the citadel's middle brighter; it fades out with the
    /// distance, and a line does not hang down a cliff.
    /// </summary>
    void UpdateGrid()
    {
        if (!Snapping || !_hasHover) { _gridDots.Visible = false; _gridKey = ""; return; }
        var w = _m.World;
        var c = _lmb && _hasDrag ? _dragPt : HoverGround();
        int g = Opt.Grid, n = g <= 8 ? 12 : 9;
        string key = $"{c.x},{c.z},{g},{_rev}";
        _gridDots.Visible = true;
        if (key == _gridKey) return;
        _gridKey = key;
        var mm = _gridDots.Multimesh;
        int side = 2 * n + 1;
        mm.InstanceCount = side * side + 2 * side * (side - 1);
        float v = VoxelWorld.Vox;
        var hidden = new Transform3D(Basis.Identity.Scaled(Vector3.One * 1e-4f), Vector3.Zero);
        int i = 0;
        bool In(int x, int z) => x >= 0 && z >= 0 && x < w.SX && z < w.SZ;
        Vector3 At(int x, int z) => new(w.WX(x + .5f), w.WY(w.SurfAt(x, z)) + .35f * v, w.WZ(z + .5f));
        float Fade(float di, float dk) => 1 - MathF.Sqrt(di * di + dk * dk) / (n + .5f);
        bool Axis(int x, int z) => x == _m.Site.KeepX || z == _m.Site.KeepZ;
        void Hide() { mm.SetInstanceTransform(i, hidden); mm.SetInstanceColor(i, new Color(0, 0, 0, 0)); i++; }
        // the nodes
        for (int dk = -n; dk <= n; dk++)
            for (int di = -n; di <= n; di++)
            {
                int x = c.x + di * g, z = c.z + dk * g;
                float fade = Fade(di, dk);
                if (!In(x, z) || fade <= 0) { Hide(); continue; }
                bool axis = Axis(x, z);
                float size = (axis ? 1.6f : 1.1f) * v;
                mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(size, 1, size)), At(x, z)));
                mm.SetInstanceColor(i++, axis ? new Color(1f, .82f, .52f, .95f * fade) : new Color(.95f, .7f, .4f, .75f * fade));
            }
        // the lines between them, along x and along z
        for (int along = 0; along < 2; along++)
            for (int dk = -n; dk <= n; dk++)
                for (int di = -n; di < n; di++)
                {
                    int x0 = c.x + (along == 0 ? di : dk) * g, z0 = c.z + (along == 0 ? dk : di) * g;
                    int x1 = x0 + (along == 0 ? g : 0), z1 = z0 + (along == 0 ? 0 : g);
                    float fade = Fade(di + .5f, dk);
                    if (!In(x0, z0) || !In(x1, z1) || fade <= 0 || Math.Abs(w.SurfAt(x0, z0) - w.SurfAt(x1, z1)) > g * 1.5f) { Hide(); continue; }
                    var p0 = At(x0, z0); var p1 = At(x1, z1);
                    // a line through the citadel's middle runs along its own axis
                    bool axis = along == 0 ? z0 == _m.Site.KeepZ : x0 == _m.Site.KeepX;
                    var d = p1 - p0;
                    var across = new Vector3(-d.Z, 0, d.X).Normalized() * ((axis ? .7f : .38f) * v);
                    mm.SetInstanceTransform(i, new Transform3D(new Basis(d, Vector3.Up * .01f, across), (p0 + p1) / 2));
                    mm.SetInstanceColor(i++, axis ? new Color(1f, .8f, .5f, .8f * fade) : new Color(.92f, .68f, .4f, .5f * fade));
                }
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
        if ((Tool is Tool.Capture or Tool.Unmark or Tool.Gather or Tool.Repair) && _lmb && _hasStart && _hasDrag)
        {
            var a = _startPt; var b = _dragPt;
            int x0 = Math.Min(a.x, b.x), x1 = Math.Max(a.x, b.x) + 1, z0 = Math.Min(a.z, b.z), z1 = Math.Max(a.z, b.z) + 1;
            var c = Tool == Tool.Unmark ? new Color(.83f, .3f, .22f, .26f) : Tool == Tool.Gather ? ZoneView.Tint(GatherKind, .3f)
                : Tool == Tool.Repair ? new Color(.55f, .72f, .42f, .26f) : new Color(.9f, .62f, .3f, .28f);
            ShapeBox(new Vector3((x1 - x0) * VoxelWorld.Vox, .04f, (z1 - z0) * VoxelWorld.Vox),
                new Vector3(w.WX((x0 + x1) / 2f), w.WY(Math.Max(a.y, b.y)) + .05f, w.WZ((z0 + z1) / 2f)), c);
            show = true;
        }
        else if (Tool == Tool.Unmark && _hasHover)
        {
            ShapeBall((UnmarkR + .5f) * VoxelWorld.Vox, new Vector3(w.WX(_hover.X + .5f), w.WY(_hover.Y + .5f), w.WZ(_hover.Z + .5f)), new Color(.83f, .3f, .22f, .22f));
            show = true;
        }
        else if (Tool == Tool.Spell && _hasHover)
        {
            // where the spell will fall: its circle on the ground, the ball the Fall knocks out, the bank of the Hand
            var sp = Sim.SpellOf(CurrentSpell);
            var violet = new Color(.62f, .38f, .95f, .24f);
            if (sp.Stroke && _lmb && _hasStart && _hasDrag)
            {
                float dx = _dragPt.x - _startPt.x, dz = _dragPt.z - _startPt.z, len = Math.Min(MathF.Sqrt(dx * dx + dz * dz), 16 * VoxelWorld.PerMetre);
                float yaw = -MathF.Atan2(dz, dx), l = Math.Max(1, MathF.Sqrt(dx * dx + dz * dz));
                ShapeBox(new Vector3(len * VoxelWorld.Vox, 20 * VoxelWorld.Vox, 2 * VoxelWorld.PerMetre * VoxelWorld.Vox),
                    new Vector3(w.WX(_startPt.x + dx / l * len / 2 + .5f), w.WY(_startPt.y + 10), w.WZ(_startPt.z + dz / l * len / 2 + .5f)), violet, yaw);
                show = true;
            }
            else if (sp.AtVoxel && !_holding)
            {
                ShapeBall(sp.Radius * VoxelWorld.Vox, new Vector3(w.WX(_hover.X + .5f), w.WY(_hover.Y + .5f), w.WZ(_hover.Z + .5f)), violet);
                show = true;
            }
            else if (sp.Radius > 0)
            {
                var g = HoverGround();
                ShapeDisc(sp.Radius * VoxelWorld.Vox, new Vector3(w.WX(g.x + .5f), w.WY(g.y) + .08f, w.WZ(g.z + .5f)), violet);
                show = true;
            }
        }
        _shape.Visible = show;
    }

    // the shape over the ground the tool takes in: one box, one ball and one disc, made once and resized as it goes,
    // rather than a new mesh every frame left to the collector (which takes them down on a thread of its own)
    BoxMesh _shapeBox;
    SphereMesh _shapeBall;
    CylinderMesh _shapeDisc;

    void ShapeBox(Vector3 size, Vector3 at, Color c, float yaw = 0)
    {
        _shapeBox ??= new BoxMesh();
        if (_shapeBox.Size != size) _shapeBox.Size = size;
        SetShape(_shapeBox, at, c, yaw);
    }

    void ShapeBall(float r, Vector3 at, Color c)
    {
        _shapeBall ??= new SphereMesh { RadialSegments = 24, Rings = 12 };
        if (_shapeBall.Radius != r) { _shapeBall.Radius = r; _shapeBall.Height = r * 2; }
        SetShape(_shapeBall, at, c);
    }

    void ShapeDisc(float r, Vector3 at, Color c)
    {
        _shapeDisc ??= new CylinderMesh { Height = .06f, RadialSegments = 40 };
        if (_shapeDisc.TopRadius != r) { _shapeDisc.TopRadius = r; _shapeDisc.BottomRadius = r; }
        SetShape(_shapeDisc, at, c);
    }

    void SetShape(PrimitiveMesh mesh, Vector3 at, Color c, float yaw = 0)
    {
        _shape.Rotation = new Vector3(0, yaw, 0);
        if (_shape.Mesh != mesh) _shape.Mesh = mesh;
        _shape.Position = at;
        _shapeMat.AlbedoColor = c;
    }

    long _optsHash;
    string _opts, _stock;
    static long Mix(long h, long v) => unchecked((h ^ v) * 1099511628211L + 7);

    /// <summary>The world's revision about the cursor and the drag, 12 m round them (a blueprint's ghost reaches so far).</summary>
    int PlanRevision(VoxelWorld w)
    {
        if (!_hasHover) return w.Revision;
        var h = HoverGround();
        int x0 = h.x, z0 = h.z, x1 = h.x, z1 = h.z;
        if (_hasStart) { x0 = Math.Min(x0, _startPt.x); z0 = Math.Min(z0, _startPt.z); x1 = Math.Max(x1, _startPt.x); z1 = Math.Max(z1, _startPt.z); }
        if (_hasDrag) { x0 = Math.Min(x0, _dragPt.x); z0 = Math.Min(z0, _dragPt.z); x1 = Math.Max(x1, _dragPt.x); z1 = Math.Max(z1, _dragPt.z); }
        const int Room = 12 * VoxelWorld.PerMetre;
        return w.RevisionIn(x0 - Room, z0 - Room, x1 + Room, z1 + Room);
    }

    void UpdatePlan()
    {
        var w = _m.World;
        // the valley changes all the time while the pawns work: re-plan for it twice a second, not every frame, and a
        // ghost that is long in the making no more than a hundredth of the time
        // and only for a change near what the hand holds: the cursor, where a drag began, with room for the ghost
        int rev = PlanRevision(w);
        if (rev != _rev && _clock - _revAt > Math.Max(.5, _planMs * .1)) { _rev = rev; _revAt = _clock; }
        var st = _m.Sim.Stock;
        bool dig = DigNow;
        // the words of what the hand holds and of the stores, made again only when one of them changed: built every
        // frame they came to a good deal of garbage for nothing
        int s1 = (int)(st[1] / 400), s2 = (int)(st[2] / 400), s3 = (int)(st[3] / 400);
        long oh = Mix(Mix(Mix(Mix(Mix(Mix(Mix(Mix(dig ? 1 : 0, Opt.Material), Opt.Width), Opt.Height), Opt.Depth), Opt.Step), Opt.Level ? 1 : 0), Opt.Hollow ? 1 : 0), Opt.House ? 1 : 0);
        oh = Mix(Mix(Mix(Mix(Mix(Mix(Mix(Mix(oh, Opt.HouseDoor), Opt.Mine ? 1 : 0), (int)Opt.Form), Opt.YardPick), Opt.YardRot), _rev), s1), Mix(s2, s3));
        if (oh != _optsHash || _opts == null)
        {
            _optsHash = oh;
            _stock = $"{s1}|{s2}|{s3}";
            _opts = $"{dig}|{Opt.Material}|{Opt.Width}|{Opt.Height}|{Opt.Depth}|{Opt.Step}|{Opt.Level}|{Opt.Hollow}|{Opt.House}|{Opt.HouseDoor}|{Opt.Mine}|{Opt.Form}|{Opt.YardPick}|{Opt.YardRot}|{_rev}|{_stock}";
        }
        string stock = _stock, opts = _opts;
        string key = "";
        bool dragging = (Tool is Tool.Line or Tool.Box or Tool.Circle or Tool.Bridge || Paving) && _lmb && _hasStart && _hasDrag;
        if (Tool == Tool.Spell)
        {
            // a spell shows what it takes and whether it is ready, no ghost
            _ghost.Visible = false;
            var sp = Sim.SpellOf(CurrentSpell);
            double ready = _m.Sim.ReadyIn(sp.Kind), have = _m.Sim.Favour;
            string pick = sp.Kind == SpellKind.Weather ? $" · {Sim.WeatherName(Sim.Summonable[WeatherPick])}" : "";
            PlanText = $"«{sp.Name}»{pick} · {_m.Sim.CostOf(sp):0} вмешательства" + (ready > 0 ? $" · через {ready:0} с" : "") + (have < _m.Sim.CostOf(sp) ? $" — есть {have:0}" : "")
                + (_holding && _m.Sim.HeldNow is { } held ? $" · держит ещё {Math.Max(0, held.HeldUntil - _m.Sim.Time):0.0} с" : "");
            PlanAffordable = ready <= 0 && have >= _m.Sim.CostOf(sp);
            PlanIsDig = false;
            _planKey = "~";
            return;
        }
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
        if (Tool == Tool.Building)
        {
            // a town building's ghost where it would go up (BuildTools.Town.cs)
            BuildingPlanAtHover();
            return;
        }
        if (Tool == Tool.Template)
        {
            // the template's ghost, at the cursor or along the drag (BuildTools.Template.cs)
            TemplatePlanAtHover();
            return;
        }
        if (Tool == Tool.Wand)
        {
            // the region under the cursor, shown as it is (and what will be laid in its place)
            WandRegion();
            string wk = _hasHover && _wandL.Count > 0 || _wandHeldN > 0 ? $"V|{_wandGen}|{_wandHeldGen}|{dig}|{Opt.Material}|{Opt.WandCapture}|{stock}" : "";
            if (wk == _planKey) return;
            _planKey = wk; _planClock = _clock;
            WandPlan(wk.Length > 0, dig, st);
            return;
        }
        if (Tool == Tool.Repair)
        {
            // a mending shows what is missing, as it will be laid again
            bool drag = _lmb && _hasStart && _hasDrag;
            var bld = drag ? null : HoverBuilding();
            string mk = drag ? $"M|{_startPt}|{_dragPt}|{_rev}|{_m.Sim.DesignRev}|{stock}" : bld != null && bld.Count > 0 ? $"M|{bld[0]}|{_rev}|{_m.Sim.DesignRev}|{stock}" : "";
            if (mk == _planKey || (mk.Length > 0 && _clock - _planClock < .15)) return;
            _planKey = mk; _planClock = _clock;
            if (mk.Length == 0) { _ghost.Visible = false; PlanText = null; return; }
            if (drag) PlanMend(_m.Sim.MeantIn(_startPt.x, _startPt.z, _dragPt.x, _dragPt.z), _startPt.x, _startPt.z, _dragPt.x, _dragPt.z);
            else { var (x0, z0, x1, z1) = _bldBox; PlanMend(bld, x0 - 8, z0 - 8, x1 + 8, z1 + 8); }
            PlanIsDig = false;
            if (_mendGhost.Count == 0 && _mendClear.Count == 0) { _ghost.Visible = false; PlanText = _mendWaiting > 0 ? "Починка уже размечена: ждёт рабочих" : "Цело: чинить нечего"; PlanAffordable = true; return; }
            PlanText = "Починка: " + MendSummary(out bool ok);
            PlanAffordable = ok;
            GhostFor(_mendGhost, false);
            _ghost.Visible = _ghost.Mesh != null;
            _ghostMat.SetShaderParameter("tint", ok ? new Vector3(.55f, .78f, .42f) : new Vector3(.85f, .2f, .16f));
            _ghostMat.SetShaderParameter("tint_mix", ok ? .3f : .6f);
            _ghostMat.SetShaderParameter("inflate", 0f);
            return;
        }
        if (dragging) key = $"D{Tool}|{_startPt}|{_dragPt}|{opts}";
        else if (dig && _hasTree && Tool is Tool.Brush or Tool.Line or Tool.Box or Tool.Circle) key = $"T|{_tree.X},{_tree.Y},{_tree.Z}|{_rev}";
        else if (_hasHover)
        {
            var g = Tool == Tool.Brush ? _hover.Ground(w) : HoverGround();
            string h = $"{_hover.X},{_hover.Y},{_hover.Z},{_hover.NX}{_hover.NY}{_hover.NZ}";
            key = Tool switch
            {
                Tool.Line or Tool.Box or Tool.Circle => $"L|{g}",
                Tool.Yard => $"Y|{g}",
                Tool.Bridge => $"G|{g}",
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
        long planT0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try { Replan(key, dragging, dig, st); }
        finally { _planMs = (System.Diagnostics.Stopwatch.GetTimestamp() - planT0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }
    }

    /// <summary>The ghost and the words over it made again for what the key says the tool is at now.</summary>
    void Replan(string key, bool dragging, bool dig, Stockpile st)
    {
        var w = _m.World;
        var site = _m.Site;
        var gp = Tool == Tool.Brush ? _hover.Ground(w) : HoverGround();
        double r = NibR;
        string name = dig ? "Копать" : ToolName(Tool);
        var stroke = StrokeNow();
        int lv = Shapes.Level(w, gp.x, gp.z, stroke);
        if (key[0] == 'T') { _plan = Shapes.Tree(w, _tree.X, _tree.Y, _tree.Z); name = "Срубить дерево"; }
        else if (dragging && Tool == Tool.Bridge && Opt.DrawBridge)
        {
            var db = Drawbridge.Plan(w, site, _startPt.x, _startPt.z, _dragPt.x, _dragPt.z, Opt.Width, out string why);
            if (db == null) { _ghost.Visible = false; PlanText = "Подъёмный мост: " + why; PlanAffordable = false; return; }
            _plan = db.Deck(w);
            name = "Подъёмный мост";
            dig = false;
        }
        else if (dragging && Tool == Tool.Bridge)
        {
            if (BridgeRefusal(_startPt, _dragPt) is { } why) { _ghost.Visible = false; PlanText = "Мост: " + why; PlanAffordable = false; return; }
            _plan = PlanDrag(_startPt, _dragPt, false);
            dig = false;
        }
        else if (dragging && Paving) { _plan = WayAt(_startPt, _dragPt); name = Sim.WayName(WayPick); dig = false; }
        else if (dragging)
        {
            _houseNote = null;
            _plan = PlanDrag(_startPt, _dragPt, dig);
            // a house: where its door goes, or why it cannot be here
            if (Tool == Tool.Box && Opt.House && !dig && _houseNote != null)
            {
                if (_plan == null) { _ghost.Visible = false; PlanText = $"Дом: {_houseNote}"; PlanAffordable = false; return; }
                name = $"Дом, {_houseNote}";
            }
        }
        else if (Paving) { _plan = WayAt(gp, gp); name = Sim.WayName(WayPick) + ", начало"; dig = false; }
        else if (Tool == Tool.Yard)
        {
            _plan = DecorAtHover(out string why);
            name = Yard.Name(DecorPick);
            dig = false;
            if (_plan == null) { _ghost.Visible = false; PlanText = $"{name}: {why}"; PlanAffordable = false; _planDig = false; return; }
        }
        else if (Tool == Tool.Bridge) { _ghost.Visible = false; PlanText = null; return; }
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
        else if (Tool == Tool.Gate)
        {
            dig = false;
            _gatePlan = null;
            if (HoverGate is { } there)
            {
                // a gate under the cursor: what walling it up lays
                _plan = _m.Sim.WallUpPlan(there);
                name = "Заложить ворота";
            }
            else if (_m.Sim.HouseWallAt(_hover, out var house, out int side, out int along))
            {
                string no = _m.Sim.HouseDoorAt(house, side, along, out var door);
                if (no != null) { _ghost.Visible = false; PlanText = $"Дверь {Shapes.SideWords[side]}: {no}"; PlanAffordable = false; return; }
                _gatePlan = GatePlan.At(w, site, new VoxelHit { X = door.X, Y = door.Y, Z = door.Z, NX = door.NX, NZ = door.NZ }, door.Y - 8, iron: IronGate, outward: true);
                _plan = _gatePlan?.Leaves;
                name = $"Дверь дома {Shapes.SideWords[side]}";
            }
            else { _gatePlan = GatePlan.At(w, site, _hover, iron: IronGate); _plan = _gatePlan?.Leaves; name = "Ворота"; }
        }
        else if (Tool == Tool.Paste) { _plan = PlanPasteAtHover(_pasteGhost); name = PasteBp.Name; dig = false; }
        if (key[0] == 'T') dig = true;
        _planDig = dig;

        double sec = Labour.Estimate(w, _plan, dig, _est);
        Array.Copy(_est, PlanNeeds, Stockpile.Kinds);
        PlanIsDig = dig;
        if (_plan == null || _plan.Count == 0 || sec <= 0) { _ghost.Visible = false; PlanText = null; return; }
        string shortOf = "";
        if (!dig)
            foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth, Stockpile.Metal })
                if (_est[k] > st[k] + .5) shortOf += (shortOf.Length > 0 ? ", " : "") + $"{Stockpile.Name(k)} {Fmt.M3(st[k])}";
        PlanAffordable = dig || shortOf.Length == 0;
        PlanText = $"{name}: {(Tool == Tool.Gate && _gatePlan != null ? GateSummary(_gatePlan) : Summary(_est, dig, sec))}" + (shortOf.Length > 0 ? $" — в запасе {shortOf}" : "");
        GhostFor(_plan, dig);
        _ghost.Visible = _ghost.Mesh != null;
        _ghostMat.SetShaderParameter("tint", dig ? new Vector3(.85f, .2f, .14f) : PlanAffordable ? new Vector3(.9f, .55f, .25f) : new Vector3(.85f, .2f, .16f));
        _ghostMat.SetShaderParameter("tint_mix", dig ? .62f : PlanAffordable ? .35f : .6f);
        // a way lies where the turf is now: drawn a hair over it
        _ghostMat.SetShaderParameter("inflate", dig ? .1f : Paving ? .06f : 0f);
    }

    /// <summary>The ghost's mesh changed, the old one let go at once rather than left to the garbage collector.</summary>
    void Ghost(ArrayMesh mesh)
    {
        var old = _ghost.Mesh;
        _ghost.Mesh = mesh;
        if (old != mesh) old?.Dispose();
    }

    /// <summary>
    /// The ghost of a plan: meshed again only when the plan is not the one already shown — the valley changing near
    /// the ghost (a worker busy beside it) plans it again, and mostly it comes out the same.
    /// </summary>
    void GhostFor(VoxelList plan, bool dig)
    {
        ulong hash = plan?.Hash() ?? 0;
        if (hash == _ghostHash && dig == _ghostDig && _ghost.Mesh != null && hash != 0) return;
        Ghost(MeshPlan(plan, dig));
        _ghostHash = _ghost.Mesh != null ? hash : 0;
        _ghostDig = dig;
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
            // grass and flowers are drawn in blades: the ghost of a dig shows the turf they stand in, not cubes of them
            if (m == 0 || (dig && (!Labour.Diggable(m) || Mat.IsSprig(m)))) continue;
            _grid[((y - y0 + 1) * pz + (z - z0 + 1)) * px + (x - x0 + 1)] = m;
        }
        var made = _mesher.Mesh(_grid, dx, dy, dz, x0, y0, z0);
        var mesh = GodotMesh.From(made);
        made?.Return();
        return mesh;
    }
}
