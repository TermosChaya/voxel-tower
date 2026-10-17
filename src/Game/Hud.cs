using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The interface laid over the game instead of a side panel: the chronicle top left, the treasury and stores top centre,
/// the wave clock and the retinue top right, the map bottom left, the tool belt bottom centre and the blueprint scrolls
/// bottom right. Health bars and the selection frame are drawn straight over the field.
/// </summary>
public partial class Hud : CanvasLayer
{
    public enum Kind { Plain, Ok, Warn, Bad }

    /// <summary>How much of the bottom edge the panels cover, for centring the camera above them.</summary>
    public const float BottomBand = 200;
    /// <summary>How much of the top edge the treasury banner covers.</summary>
    public const float TopBand = 80;

    const string HelpText = "Управление\n" +
        "Кисти ничего не строят сами: они размечают работу, а пешки делают её — кладут из запасов, копают, рубят.\n" +
        "1–5 — кисти, 6 — снять разметку, 7 — лестница, B — снять чертёж, Esc — убрать кисть. Z, X, C — камень, брёвна, земля; " +
        "V — кирка: копать, рубить, ломать камень, разбирать свою кладку. Shift с кистью — тоже кирка.\n" +
        "[ и ] — толщина, − и = — высота (с киркой — глубина). F — форма или заливка, G — ровно или по рельефу.\n" +
        "Без кисти: ЛКМ — выбрать пешку, рамкой — нескольких; ПКМ по земле — послать, по зверю — охотиться, по врагу — атаковать.\n" +
        "Пробел — позвать орду раньше (она меньше узнает о вас), P — пауза, Tab — скорость, T — тревога.\n" +
        "ПКМ тянет карту, колесо — масштаб, СКМ или Q/E — поворот, WASD — сдвиг, H — вернуть вид.";

    static readonly (Tool tool, Icons.Painter icon, string key, string tip)[] ToolDefs =
    {
        (Tool.Brush, Icons.Brush, "1", "Кисть\nВедите по земле — рабочие выложат столб материала до заданной высоты: так рисуют стены от руки. Шар налипает на поверхность. С киркой кисть размечает, что копать, а шар пробивает ходы. F — форма, Shift — кирка на один мазок."),
        (Tool.Line, Icons.StraightLine, "2", "Линия\nЗажмите и протяните: прямая стена заданной толщины и высоты. С киркой — ров или траншея."),
        (Tool.Box, Icons.Box, "3", "Прямоугольник\nОт угла до угла. Сплошной — помост или основание, стенки — двор или квадратная башня. С киркой — котлован. F — сплошной или стенки."),
        (Tool.Circle, Icons.Circle, "4", "Круг\nИз центра к краю. Кольцо — круглая башня, сплошной — бастион. С киркой — круглая яма или кольцевой ров. F — сплошной или кольцо."),
        (Tool.Extrude, Icons.Extrude, "5", "Выдавить\nЩелчок по ровной грани: она нарастёт на шаг выбранным материалом. С киркой — грань вынут на шаг вглубь: ступени в склоне, проход в скале."),
        (Tool.Unmark, Icons.Erase, "6", "Снять разметку\nРамка снимает всю разметку внутри, щелчок — разметку поблизости. Что уже сделано, остаётся."),
        (Tool.Ladder, Icons.Ladder, "7", "Лестница\nЩелчок по боковой грани стены, башни или обрыва: рабочие приставят лестницу от земли до верха. По лестницам ваши пешки поднимаются на стены. Орда тоже ими пользуется — и ставит свои."),
        (Tool.Capture, Icons.Scroll, "B", "Снять чертёж\nОбведите рамкой свою постройку — она станет чертежом. Копии ставятся из свитков справа, R поворачивает."),
    };

    static readonly (byte mat, string key, int store, string tip)[] MatDefs =
    {
        (Mat.Stone, "Z", Stockpile.Stone, "Камень\nКладётся рядами кирпича на растворе, дольше всего и крепче всего. Его ломают киркой в скалах и валунах; разобранная кладка возвращает три четверти."),
        (Mat.Wood, "X", Stockpile.Wood, "Брёвна\nРубят в лесу: кирка, щелчок по дереву. Ложатся быстрее камня, но топор орка берёт их легко."),
        (Mat.Rammed, "C", Stockpile.Earth, "Земля\nЕё даёт любой ров и котлован. Насыпь кладётся быстрее всего, а гоблину без кирки сквозь неё не пройти."),
    };

    const string PickTip = "Кирка\nКисти размечают не кладку, а работу кирки: выкопать ров, срубить лес, наломать камня, разобрать свою стену. Всё добытое идёт в запасы.";

    Main _m;
    Banner _banner;
    VBoxContainer _chronicle;
    readonly List<(Label label, double born)> _lines = new();
    Minimap _map;
    Label _toolName, _toolMat, _toolHint;
    HBoxContainer _opts;
    readonly Dictionary<Tool, Slot> _toolSlots = new();
    readonly Dictionary<byte, Slot> _matSlots = new();
    Slot _pickSlot;
    VBoxContainer _bpList;
    Label _bpCount;
    CursorTag _tag;
    Control _floats;
    WaveClock _clockPanel;
    Retinue _retinue;
    UnitOverlay _overlay;
    Control _finale;
    double _clock, _refreshAt;

    public static Label CapsLabel(string text, int size = 10, Color? color = null)
    {
        var l = new Label { Text = text.ToUpperInvariant(), MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", Ornament.Caps);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Ornament.Aged);
        return l;
    }

    public static Label Text(string text, Font font, int size, Color color, bool wrap = false, int outline = 0)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        if (outline > 0) l.AddThemeConstantOverride("outline_size", outline);
        if (wrap) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    public void Init(Main m)
    {
        _m = m;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = Ornament.MakeTheme() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _overlay = new UnitOverlay();
        root.AddChild(_overlay);
        _overlay.Init(m);

        // the chronicle: lettering straight over the map, top left
        _chronicle = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(24, 18), CustomMinimumSize = new Vector2(430, 0) };
        _chronicle.AddThemeConstantOverride("separation", 3);
        root.AddChild(_chronicle);

        // the treasury and the stores, top centre
        _banner = new Banner();
        _banner.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _banner.OffsetLeft = -310; _banner.OffsetRight = 310; _banner.OffsetTop = 12; _banner.OffsetBottom = 72;
        root.AddChild(_banner);

        var help = new Medallion(Icons.Question, HelpText);
        help.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        help.OffsetLeft = -66; help.OffsetRight = -20; help.OffsetTop = 16; help.OffsetBottom = 62;
        root.AddChild(help);

        // the wave clock and the retinue, down the right edge
        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 10);
        right.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        right.OffsetLeft = right.OffsetRight = -78;
        right.OffsetTop = right.OffsetBottom = 14;
        right.GrowHorizontal = Control.GrowDirection.Begin;
        _clockPanel = new WaveClock();
        right.AddChild(_clockPanel);
        _retinue = new Retinue();
        right.AddChild(_retinue);
        root.AddChild(right);
        _clockPanel.Init(m);
        _retinue.Init(m);

        // the map, bottom left
        var mapPanel = new OrnatePanel(14);
        Pin(mapPanel, Control.LayoutPreset.BottomLeft, 16, -16);
        var mapBox = new VBoxContainer();
        mapBox.AddThemeConstantOverride("separation", 6);
        mapBox.AddChild(CapsLabel("Карта долины"));
        _map = new Minimap();
        mapBox.AddChild(_map);
        mapPanel.AddChild(mapBox);
        root.AddChild(mapPanel);
        _map.Init(m);

        // the tool belt, bottom centre
        var belt = new OrnatePanel(16);
        Pin(belt, Control.LayoutPreset.CenterBottom, 0, -16);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 7);
        var head = new HBoxContainer();
        _toolName = Text("", Ornament.Display, 24, Ornament.EmberGlow);
        _toolName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(_toolName);
        _toolMat = CapsLabel("", 10);
        _toolMat.VerticalAlignment = VerticalAlignment.Center;
        head.AddChild(_toolMat);
        col.AddChild(head);
        _toolHint = Text("", Ornament.SerifItalic, 13, Ornament.Aged, wrap: true);
        _toolHint.CustomMinimumSize = new Vector2(0, 34);
        col.AddChild(_toolHint);
        _opts = new HBoxContainer();
        _opts.AddThemeConstantOverride("separation", 18);
        col.AddChild(_opts);
        col.AddChild(new Rule(false));
        var slots = new HBoxContainer();
        slots.AddThemeConstantOverride("separation", 7);
        foreach (var (tool, icon, key, tip) in ToolDefs)
        {
            var s = new Slot(icon, key, tip);
            var t = tool;
            s.Clicked += () => _m.Tools.SetTool(t);
            _toolSlots[tool] = s;
            slots.AddChild(s);
        }
        slots.AddChild(new Rule(true));
        foreach (var (mat, key, _, tip) in MatDefs)
        {
            var s = new Slot(Icons.Material(mat), key, tip) { Caption = "" };
            var mm = mat;
            s.Clicked += () => _m.Tools.SetMaterial(mm);
            _matSlots[mat] = s;
            slots.AddChild(s);
        }
        _pickSlot = new Slot(Icons.Pick, "V", PickTip) { Caption = "копать" };
        _pickSlot.Clicked += () => _m.Tools.SetDig(!_m.Tools.Opt.Dig);
        slots.AddChild(_pickSlot);
        col.AddChild(slots);
        belt.AddChild(col);
        root.AddChild(belt);

        // blueprint scrolls, bottom right
        var bpPanel = new OrnatePanel(14);
        Pin(bpPanel, Control.LayoutPreset.BottomRight, -16, -16);
        var bpBox = new VBoxContainer();
        bpBox.AddThemeConstantOverride("separation", 8);
        var bpHead = new HBoxContainer();
        var bpTitle = CapsLabel("Свитки чертежей");
        bpTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bpHead.AddChild(bpTitle);
        _bpCount = CapsLabel("", 10, Ornament.Faded);
        bpHead.AddChild(_bpCount);
        bpBox.AddChild(bpHead);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(300, 176) };
        _bpList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _bpList.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_bpList);
        bpBox.AddChild(scroll);
        bpPanel.AddChild(bpBox);
        root.AddChild(bpPanel);

        _floats = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _floats.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_floats);
        _tag = new CursorTag { Visible = false };
        root.AddChild(_tag);

        RebuildBlueprints();
        OnToolChanged();
        Refresh();
    }

    /// <summary>Anchors a panel to a corner or edge; it grows away from that edge as its content needs.</summary>
    static void Pin(Control c, Control.LayoutPreset preset, float x, float y)
    {
        c.SetAnchorsPreset(preset);
        c.OffsetLeft = c.OffsetRight = x;
        c.OffsetTop = c.OffsetBottom = y;
        c.GrowVertical = Control.GrowDirection.Begin;
        c.GrowHorizontal = preset switch
        {
            Control.LayoutPreset.CenterBottom => Control.GrowDirection.Both,
            Control.LayoutPreset.BottomRight => Control.GrowDirection.Begin,
            _ => Control.GrowDirection.End,
        };
    }

    // ---------------------------------------------------------------- state

    public void Refresh()
    {
        if (_banner == null) return;
        var st = _m.Sim.Stock;
        _banner.Values[0] = Fmt.Int(Math.Floor(st.Gold));
        var t = _m.Tools;
        for (int k = 1; k <= 3; k++)
        {
            _banner.Values[k] = Fmt.M3(st[k]);
            _banner.Short[k] = t.Tool != Tool.None && t.PlanText != null && !t.PlanIsDig && t.PlanNeeds[k] > st[k] + .5;
        }
        foreach (var (mat, _, store, _) in MatDefs)
        {
            var s = _matSlots[mat];
            string cap = Fmt.M3(st[store]);
            if (s.Caption != cap) { s.Caption = cap; s.QueueRedraw(); }
        }
        _banner.QueueRedraw();
        _clockPanel.Refresh();
        _retinue.Refresh();
    }

    public override void _Process(double delta)
    {
        if (_banner == null) return;
        _clock += delta;
        _refreshAt -= delta;
        if (_refreshAt <= 0) { _refreshAt = .2; Refresh(); }
        foreach (var (label, born) in _lines)
        {
            double age = _clock - born;
            label.Modulate = new Color(1, 1, 1, age < 12 ? 1 : (float)Math.Max(.35, 1 - (age - 12) / 8));
        }
        var t = _m.Tools;
        bool overUi = GetViewport().GuiGetHoveredControl() != null;
        if (overUi) _tag.Visible = false;
        else if (t.Tool != Tool.None) _tag.Show(t.PlanText, t.PlanAffordable ? Ornament.EmberGlow : Ornament.Blood, t.HoverText);
        else
        {
            // with no brush in hand the tag tells who is under the cursor
            var u = _m.Command.UnitAt(GetViewport().GetMousePosition(), pawnsFirst: _m.Selected.Count == 0);
            if (u == null) { _tag.Visible = false; return; }
            string head = u.Team == Team.Wild ? $"{u.T.Name} · {u.T.Bounty} зол. за добычу" : $"{u.T.Name} · {Math.Ceiling(Math.Max(0, u.Hp))} из {u.T.Hp}";
            string sub = Retinue.Doing(u, _m.Sim);
            if (u.Team != Team.Player && _m.Selected.Count > 0) sub += u.Team == Team.Wild ? " · ПКМ — охотиться" : " · ПКМ — атаковать";
            _tag.Show(head, u.Team == Team.Horde ? Ornament.Blood : u.Team == Team.Wild ? Ornament.Moss : Ornament.EmberGlow, sub);
        }
    }

    public void OnToolChanged()
    {
        if (_toolName == null) return;
        var t = _m.Tools;
        foreach (var (tool, s) in _toolSlots) s.Selected = tool == t.Tool;
        foreach (var (mat, s) in _matSlots) s.Selected = !t.Opt.Dig && mat == t.Opt.Material;
        _pickSlot.Selected = t.Opt.Dig;
        _toolName.Text = t.Tool == Tool.Paste && t.PasteBp != null ? $"Чертёж «{t.PasteBp.Name}»" : BuildTools.ToolName(t.Tool);
        _toolMat.Text = (t.Opt.Dig ? "кирка: копать и рубить" : "материал: " + Mat.NameOf(t.Opt.Material).ToLowerInvariant()).ToUpperInvariant();
        _toolHint.Text = BuildTools.Hint(t.Tool, t.Opt.Dig);
        RebuildOptions();
        RebuildBlueprints();
    }

    public void OnSelectionChanged() => _retinue?.Refresh();

    static string Len(int voxels)
    {
        double m = voxels * VoxelWorld.Vox;
        return m < 1 ? $"{Math.Round(m * 100)} см" : (Fmt.One(m) + " м").Replace(",0 м", " м");
    }

    void RebuildOptions()
    {
        foreach (var c in _opts.GetChildren()) c.QueueFree();
        var t = _m.Tools; var o = t.Opt;
        bool dig = o.Dig;
        const string widthTip = "Толщина\nТолщина линии и стен, ширина рва, размер кисти. Клавиши [ и ].";
        const string heightTip = "Высота\nНасколько поднимется кладка. Клавиши − и =.";
        const string depthTip = "Глубина\nНасколько глубоко копать от поверхности. Клавиши − и =.";
        const string levelTip = "Верх\nРовный: верх на одной высоте от точки, где начали. По рельефу: каждый столбец поднимается над своей землёй. Клавиша G.";
        const string floorTip = "Дно\nРовное: дно на одной глубине от точки, где начали. По рельефу: каждый столбец углубляется от своей поверхности. Клавиша G.";
        Control Width(string caption = "Толщина") => new Stepper(caption, widthTip, () => Len(o.Width), d => t.StepWidth(d));
        Control Height() => dig ? new Stepper("Глубина", depthTip, () => Len(o.Depth), d => t.StepHeight(d)) : new Stepper("Высота", heightTip, () => Len(o.Height), d => t.StepHeight(d));
        Control Level() => dig
            ? new Choice("Дно", floorTip, new[] { "ровное", "по рельефу" }, o.Level ? 0 : 1, i => { if ((i == 0) != o.Level) t.ToggleLevel(); })
            : new Choice("Верх", levelTip, new[] { "ровный", "по рельефу" }, o.Level ? 0 : 1, i => { if ((i == 0) != o.Level) t.ToggleLevel(); });
        switch (t.Tool)
        {
            case Tool.Brush:
                _opts.AddChild(new Choice("Форма", "Форма кисти\nСтолб — стена от руки до заданной высоты (с киркой — полоса на глубину). Шар — налипает на поверхность (с киркой — вынимает ком). Клавиша F.",
                    new[] { "столб", "шар" }, (int)o.Form, i => { o.Form = (BrushForm)i; t.Changed(); }));
                _opts.AddChild(Width("Размер"));
                if (o.Form == BrushForm.Column) { _opts.AddChild(Height()); _opts.AddChild(Level()); }
                break;
            case Tool.Line:
                _opts.AddChild(Width(dig ? "Ширина" : "Толщина")); _opts.AddChild(Height()); _opts.AddChild(Level());
                break;
            case Tool.Box:
            case Tool.Circle:
                _opts.AddChild(new Choice("Заливка", "Заливка\nСплошная фигура или только стенки заданной толщины. Клавиша F.",
                    new[] { "сплошной", t.Tool == Tool.Box ? "стенки" : "кольцо" }, o.Hollow ? 1 : 0, i => { if ((i == 1) != o.Hollow) t.ToggleForm(); }));
                if (o.Hollow) _opts.AddChild(Width());
                _opts.AddChild(Height()); _opts.AddChild(Level());
                break;
            case Tool.Extrude:
                _opts.AddChild(new Stepper("Шаг", "Шаг\nНа сколько нарастает или уходит вглубь грань за щелчок. Клавиши − и =.", () => Len(o.Step), d => t.StepHeight(d)));
                break;
            case Tool.Paste when t.PasteBp != null:
                _opts.AddChild(CapsLabel($"поворот {t.PasteRot * 90}° · R — повернуть · {t.PasteBp.Describe()}", 10, Ornament.Aged));
                break;
        }
        _opts.Visible = _opts.GetChildCount() > 0;
    }

    public void RebuildBlueprints()
    {
        if (_bpList == null) return;
        foreach (var c in _bpList.GetChildren()) c.QueueFree();
        _bpCount.Text = _m.Blueprints.Count > 0 ? $"{_m.Blueprints.Count}" : "";
        if (_m.Blueprints.Count == 0)
        {
            var empty = Text("Свитков пока нет. Постройте стену или башню, нажмите B и обведите её рамкой — получится чертёж, который размечается снова одним щелчком.",
                Ornament.SerifItalic, 13, Ornament.Aged, wrap: true);
            empty.CustomMinimumSize = new Vector2(280, 0);
            _bpList.AddChild(empty);
            return;
        }
        foreach (var bp in _m.Blueprints)
        {
            var b = bp;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var icon = new Slot(Icons.Scroll, null, $"{bp.Name}\nЩелчок — разметить копию. R поворачивает, Esc отменяет.", 42) { Selected = _m.Tools.PasteBp == bp };
            icon.Clicked += () => _m.Tools.StartPaste(b);
            row.AddChild(icon);
            var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            info.AddThemeConstantOverride("separation", 0);
            info.AddChild(Text(bp.Name, Ornament.SerifBold, 14, Ornament.Parchment));
            info.AddChild(Text($"{Len(bp.W)} × {Len(bp.D)} × {Len(bp.H)}", Ornament.Serif, 12, Ornament.Aged));
            info.AddChild(Text(bp.Describe(), Ornament.Serif, 12, Ornament.BronzeLight));
            row.AddChild(info);
            var del = new Button { Text = "×", FocusMode = Control.FocusModeEnum.None, TooltipText = "Сжечь свиток", CustomMinimumSize = new Vector2(26, 26), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            del.Pressed += () => _m.RemoveBlueprint(b);
            row.AddChild(del);
            _bpList.AddChild(row);
        }
    }

    // ---------------------------------------------------------------- messages

    public void Log(string text, Kind kind = Kind.Plain)
    {
        if (_chronicle == null) return;
        var color = kind switch { Kind.Ok => Ornament.Moss, Kind.Warn => Ornament.BronzeLight, Kind.Bad => Ornament.Blood, _ => Ornament.Parchment };
        var l = Text(text, Ornament.Serif, 15, color, wrap: true, outline: 6);
        l.CustomMinimumSize = new Vector2(430, 0);
        _chronicle.AddChild(l);
        _chronicle.MoveChild(l, 0);
        _lines.Insert(0, (l, _clock));
        while (_lines.Count > 6) { _lines[^1].label.QueueFree(); _lines.RemoveAt(_lines.Count - 1); }
    }

    /// <summary>A short message that rises from the cursor and fades.</summary>
    public void Float(string text, Color color)
    {
        var l = Text(text, Ornament.SerifBold, 15, color, outline: 6);
        l.Position = _floats.GetLocalMousePosition() + new Vector2(16, -30);
        _floats.AddChild(l);
        var tw = l.CreateTween();
        tw.TweenProperty(l, "position:y", l.Position.Y - 34, 1.4).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tw.Parallel().TweenProperty(l, "modulate:a", 0f, .6).SetDelay(.9);
        tw.TweenCallback(Callable.From(l.QueueFree));
    }

    /// <summary>The end of the siege, written large across the middle of the screen.</summary>
    public void Finale(string title, string sub, bool won)
    {
        if (_finale != null) return;
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        box.SetAnchorsPreset(Control.LayoutPreset.Center);
        box.GrowHorizontal = Control.GrowDirection.Both; box.GrowVertical = Control.GrowDirection.Both;
        var t = Text(title, Ornament.Display, 72, won ? Ornament.BronzeLight : Ornament.Blood, outline: 12);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(t);
        var s = Text(sub, Ornament.SerifItalic, 20, Ornament.Parchment, outline: 8);
        s.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(s);
        _floats.AddChild(box);
        _finale = box;
    }

    /// <summary>The map control, for tests.</summary>
    public Minimap Map => _map;
}

/// <summary>An ornamental rule: horizontal between the belt's rows, vertical between its groups.</summary>
public partial class Rule : Control
{
    readonly bool _vertical;

    public Rule() { }

    public Rule(bool vertical)
    {
        _vertical = vertical;
        CustomMinimumSize = vertical ? new Vector2(16, 0) : new Vector2(0, 10);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        if (_vertical) Ornament.Divider(this, new Vector2(Size.X / 2, 4), new Vector2(Size.X / 2, Size.Y - 4));
        else Ornament.Divider(this, new Vector2(10, Size.Y / 2), new Vector2(Size.X - 10, Size.Y / 2));
    }
}
