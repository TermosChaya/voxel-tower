using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The interface laid over the game, kept to the edges: one strip at the top with the treasury and the wave clock, the
/// map bottom left, the tool belt bottom centre, and everything else in tabs that hang on the side edges and slide out
/// when clicked — the chronicle, the goals and the blueprint scrolls on the left, the retinue with the chosen in detail
/// and the horde's warbands on the right. The latest lines of the chronicle show briefly under the strip. Health bars
/// and the selection frame are drawn straight over the field.
/// </summary>
public partial class Hud : CanvasLayer
{
    public enum Kind { Plain, Ok, Warn, Bad }

    /// <summary>How much of the bottom edge the panels cover, for centring the camera above them.</summary>
    public const float BottomBand = 130;
    /// <summary>How much of the top edge the strip covers.</summary>
    public const float TopBand = 84;

    const string HelpText = "Управление\n" +
        "Кисти ничего не строят сами: они размечают работу, а пешки делают её — кладут из запасов, копают, рубят.\n" +
        "1–5 — кисти, 6 — снять разметку, 7 — лестница, 8 — делянка (добыча с участка), 9 — ворота, 0 — починка, M — мост, B — снять чертёж, N — сетка, Esc — убрать кисть. Z, X, C — камень, брёвна, земля; " +
        "V — кирка: копать, рубить, ломать камень, разбирать свою кладку. Shift с кистью — тоже кирка.\n" +
        "[ и ] — толщина, − и = — высота (с киркой — глубина). F — форма или заливка, G — ровно или по рельефу.\n" +
        "Без кисти: ЛКМ — выбрать пешку, рамкой — нескольких; ПКМ по земле — послать, по зверю — охотиться, по врагу — атаковать.\n" +
        "Пробел — позвать орду раньше (она меньше узнает о вас), P — пауза, Tab — скорость, T — тревога, J — пути орды.\n" +
        "Ctrl+1…5 — запомнить выбранных отрядом, Shift+1…5 — выбрать отряд (дважды — показать).\n" +
        "Вкладки по краям выдвигаются щелчком: слева — задачи, летопись, чертежи; справа — дружина поимённо и орда.\n" +
        "ПКМ тянет карту, колесо — масштаб, СКМ или Q/E — поворот, WASD — сдвиг, Y — вид сверху, H — вернуть вид.";

    static readonly (Tool tool, Icons.Painter icon, string key, string tip)[] ToolDefs =
    {
        (Tool.Brush, Icons.Brush, "1", "Кисть\nВедите по земле — рабочие выложат столб материала до заданной высоты: так рисуют стены от руки. Шар налипает на поверхность. С киркой кисть размечает, что копать, а шар пробивает ходы. F — форма, Shift — кирка на один мазок."),
        (Tool.Line, Icons.StraightLine, "2", "Линия\nЗажмите и протяните: прямая стена заданной толщины и высоты. С киркой — ров или траншея."),
        (Tool.Box, Icons.Box, "3", "Прямоугольник\nОт угла до угла. Сплошной — помост или основание, стенки — двор или квадратная башня. С киркой — котлован. F — сплошной или стенки."),
        (Tool.Circle, Icons.Circle, "4", "Круг\nИз центра к краю. Кольцо — круглая башня, сплошной — бастион. С киркой — круглая яма или кольцевой ров. F — сплошной или кольцо."),
        (Tool.Extrude, Icons.Extrude, "5", "Выдавить\nЩелчок по ровной грани: она нарастёт на шаг выбранным материалом. С киркой — грань вынут на шаг вглубь: ступени в склоне, проход в скале."),
        (Tool.Unmark, Icons.Erase, "6", "Снять разметку\nРамка снимает всю разметку внутри, щелчок — разметку поблизости. Что уже сделано, остаётся."),
        (Tool.Ladder, Icons.Ladder, "7", "Лестница\nЩелчок по боковой грани стены, башни или обрыва: рабочие приставят лестницу от земли до верха. По лестницам ваши пешки поднимаются на стены. Орда тоже ими пользуется — и ставит свои."),
        (Tool.Gather, Icons.Gather, "8", "Делянка\nРамкой отметьте участок, откуда брать ресурс: Z — камень (скалы и валуны), X — лес, C — земля; у камня и земли — глубина. Размечать каждое дерево не нужно: свободные пешки сами берут оттуда работу понемногу, а разметка стен и рвов идёт первой."),
        (Tool.Gate, Icons.GateIcon, "9", "Ворота\nЩелчок по боковой грани стены: рабочие прорубят проём в два метра шириной и три высотой и навесят дубовые створки на железе. Свои открывают их на ходу, перед ордой они закрыты, и рубить их ей не легче камня. Щелчок по воротам без кисти — держать открытыми, на засов или снова сами."),
        (Tool.Repair, Icons.Mend, "0", "Починка\nЩелчок по разрушенной постройке: рабочие вернут ей прежний вид — разберут завал, где он лёг на кладку, доложат недостающее тем же камнем, навесят ворота. Помнится всё, что вы построили, и старая крепость тоже. Рамка чинит только то, что в ней."),
        (Tool.Bridge, Icons.BridgeIcon, "M", "Мост\nЗажмите на одном берегу и протяните до другого: рабочие перекинут мост, который идёт аркой, а не плоско. Каменный (Z) — на арке, с парапетами; деревянный (X) — настил на гнутых рёбрах, с перилами. Строят от берегов, стоя на уже уложенном. Пролёт — от 2 до 25 м."),
        (Tool.Capture, Icons.Scroll, "B", "Снять чертёж\nОбведите рамкой свою постройку — она станет чертежом. Копии ставятся из свитков справа, R поворачивает."),
    };

    static readonly (byte mat, string key, int store, string tip)[] MatDefs =
    {
        (Mat.Stone, "Z", Stockpile.Stone, "Камень\nКладётся бутом — неровными камнями на растворе, дольше всего и крепче всего. Его ломают киркой в скалах и валунах; разобранная кладка возвращает три четверти."),
        (Mat.Wood, "X", Stockpile.Wood, "Брёвна\nРубят в лесу: кирка, щелчок по дереву. Ложатся быстрее камня, но топор орка берёт их легко."),
        (Mat.Rammed, "C", Stockpile.Earth, "Земля\nЕё даёт любой ров и котлован. Насыпь кладётся быстрее всего, а гоблину без кирки сквозь неё не пройти."),
    };

    const string PickTip = "Кирка\nКисти размечают не кладку, а работу кирки: выкопать ров, срубить лес, наломать камня, разобрать свою стену. Всё добытое идёт в запасы.";

    Main _m;
    Banner _banner;
    Toasts _toasts;
    VBoxContainer _history;
    Minimap _map;
    Button _topView;
    VBoxContainer _goals;
    readonly List<Label> _goalLines = new();
    SideDrawers _left, _right;
    TabSpine _goalsTab, _hordeTab;
    HordeInfo _horde;
    Label _toolName, _toolMat;
    HBoxContainer _opts, _beltHead;
    OrnatePanel _belt;
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
    double _clock, _refreshAt, _soundAt;
    (int meant, int standing) _sound;

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

        // the strip at the top: the treasury and the stores, and the wave clock beside them
        var strip = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        strip.AddThemeConstantOverride("separation", 10);
        strip.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        strip.GrowHorizontal = Control.GrowDirection.Both;
        strip.OffsetTop = 8;
        _banner = new Banner(compact: true) { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        strip.AddChild(_banner);
        _clockPanel = new WaveClock();
        strip.AddChild(_clockPanel);
        root.AddChild(strip);
        _clockPanel.Init(m);

        var help = new Medallion(Icons.Question, HelpText);
        help.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        help.OffsetLeft = -60; help.OffsetRight = -16; help.OffsetTop = 14; help.OffsetBottom = 58;
        root.AddChild(help);

        // the latest lines of the chronicle, briefly, over the tool belt (placed every frame, as the belt grows and shrinks)
        _toasts = new Toasts();
        root.AddChild(_toasts);

        // the tabs on the left: the goals, the chronicle, the blueprint scrolls
        _left = new SideDrawers(left: true, top: 96, bottom: 312);
        root.AddChild(_left);
        var goalsBox = new VBoxContainer();
        goalsBox.AddThemeConstantOverride("separation", 5);
        goalsBox.AddChild(CapsLabel("Задачи"));
        _goals = new VBoxContainer();
        _goals.AddThemeConstantOverride("separation", 3);
        goalsBox.AddChild(_goals);
        _goalsTab = _left.Add("goals", Icons.Scroll, "Задачи", "Задачи\nЧем заняться: починить крепость, занять рудники и держать их — они платят золотом на наём — и выстоять все волны.", goalsBox, 330);
        var chronicle = new VBoxContainer();
        chronicle.AddThemeConstantOverride("separation", 4);
        chronicle.AddChild(CapsLabel("Летопись"));
        _history = new VBoxContainer();
        _history.AddThemeConstantOverride("separation", 4);
        chronicle.AddChild(_history);
        _left.Add("chronicle", Icons.Horn, "Летопись", "Летопись\nВсё, что было: подсказки начала, вести о волнах, рудниках, павших и сделанном. Последние строки и так видны на минуту под полосой сверху.", chronicle, 400);
        var bpBox = new VBoxContainer();
        bpBox.AddThemeConstantOverride("separation", 8);
        var bpHead = new HBoxContainer();
        var bpTitle = CapsLabel("Свитки чертежей");
        bpTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bpHead.AddChild(bpTitle);
        _bpCount = CapsLabel("", 10, Ornament.Faded);
        bpHead.AddChild(_bpCount);
        bpBox.AddChild(bpHead);
        _bpList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _bpList.AddThemeConstantOverride("separation", 6);
        bpBox.AddChild(_bpList);
        _left.Add("blueprints", Icons.Box, "Чертежи", "Свитки чертежей\nСнятые постройки: щелчок по свитку — разметить копию. B — снять новый чертёж рамкой.", bpBox, 330);

        // the tabs on the right: the retinue with the chosen, the horde
        _right = new SideDrawers(left: false, top: 96, bottom: 16);
        root.AddChild(_right);
        _retinue = new Retinue();
        _right.Add("retinue", Icons.Sword, "Дружина", "Дружина\nСколько у вас людей и чем они заняты, наём, пять отрядов (Ctrl+цифра — запомнить, Shift+цифра — выбрать), выбранные поимённо — со здоровьем, ранами и делом — и приказы им.", _retinue, 350);
        _retinue.Init(m);
        _horde = new HordeInfo();
        _hordeTab = _right.Add("horde", Icons.Horn, "Орда", "Орда\nВатаги грядущей волны (или той, что в долине): из кого, кто ведёт, откуда войдут; их пути — на поле и на карте (J). И брёвна с землёй орды на осадные работы.", _horde, 320);
        _horde.Init(m);

        // the map, bottom left
        var mapPanel = new OrnatePanel(10);
        Pin(mapPanel, Control.LayoutPreset.BottomLeft, 12, -12);
        var mapBox = new VBoxContainer();
        mapBox.AddThemeConstantOverride("separation", 5);
        var mapHead = new HBoxContainer();
        mapHead.AddThemeConstantOverride("separation", 3);
        var mapTitle = CapsLabel("Карта", 9);
        mapTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        mapTitle.VerticalAlignment = VerticalAlignment.Center;
        mapHead.AddChild(mapTitle);
        _topView = new Button
        {
            Text = "Сверху", ToggleMode = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 20),
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = "Вид сверху\nСмотреть на долину ровно сверху, как на карту, и обратно. Y. Поворот (Q/E, СКМ) работает и так.",
        };
        _topView.AddThemeFontSizeOverride("font_size", 11);
        _topView.Pressed += () => _m.Rig.ToggleTopDown();
        foreach (var (text, factor, tip) in new[] { ("−", 1 / 1.3f, "Отдалить\nКолесо, щипок или два пальца на тачпаде, PageDown."), ("+", 1.3f, "Приблизить\nКолесо, щипок или два пальца на тачпаде, PageUp.") })
        {
            var zb = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(22, 20), MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = tip };
            zb.AddThemeFontSizeOverride("font_size", 12);
            float f = factor;
            zb.Pressed += () => _m.Rig.ZoomBy(f);
            mapHead.AddChild(zb);
        }
        mapHead.AddChild(_topView);
        mapBox.AddChild(mapHead);
        _map = new Minimap();
        mapBox.AddChild(_map);
        mapPanel.AddChild(mapBox);
        root.AddChild(mapPanel);
        _map.Init(m);

        // the tool belt, bottom centre: the tool and its settings over a row of slots
        var belt = _belt = new OrnatePanel(10);
        Pin(belt, Control.LayoutPreset.CenterBottom, 0, -10);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 5);
        _beltHead = new HBoxContainer();
        _beltHead.AddThemeConstantOverride("separation", 14);
        _toolName = Text("", Ornament.Display, 20, Ornament.EmberGlow);
        _beltHead.AddChild(_toolName);
        _toolMat = CapsLabel("", 9);
        _toolMat.VerticalAlignment = VerticalAlignment.Center;
        _toolMat.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _beltHead.AddChild(_toolMat);
        col.AddChild(_beltHead);
        _opts = new HBoxContainer();
        _opts.AddThemeConstantOverride("separation", 16);
        col.AddChild(_opts);
        var slots = new HBoxContainer();
        slots.AddThemeConstantOverride("separation", 5);
        foreach (var (tool, icon, key, tip) in ToolDefs)
        {
            var sl = new Slot(icon, key, tip, 46);
            var t = tool;
            sl.Clicked += () => _m.Tools.SetTool(t);
            _toolSlots[tool] = sl;
            slots.AddChild(sl);
        }
        slots.AddChild(new Rule(true));
        foreach (var (mat, key, _, tip) in MatDefs)
        {
            var sl = new Slot(Icons.Material(mat), key, tip, 46) { Caption = "" };
            var mm = mat;
            sl.Clicked += () => _m.Tools.SetMaterial(mm);
            _matSlots[mat] = sl;
            slots.AddChild(sl);
        }
        _pickSlot = new Slot(Icons.Pick, "V", PickTip, 46) { Caption = "копать" };
        _pickSlot.Clicked += () => _m.Tools.SetDig(!_m.Tools.Opt.Dig);
        slots.AddChild(_pickSlot);
        col.AddChild(slots);
        belt.AddChild(col);
        root.AddChild(belt);

        _floats = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _floats.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_floats);
        _tag = new CursorTag { Visible = false };
        root.AddChild(_tag);

        RebuildBlueprints();
        OnToolChanged();
        Refresh();
        // a new game opens on what there is to do
        _left.Open("goals");
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
        RefreshGoals();
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
        if (_topView.ButtonPressed != _m.Rig.TopDown) _topView.SetPressedNoSignal(_m.Rig.TopDown);
        var vp = GetViewport().GetVisibleRect().Size;
        _toasts.Position = new Vector2((vp.X - _toasts.Size.X) / 2, _belt.Position.Y - 10 - _toasts.Size.Y);
        if (_refreshAt <= 0) { _refreshAt = .2; Refresh(); }
        var t = _m.Tools;
        bool overUi = GetViewport().GuiGetHoveredControl() != null;
        if (overUi) _tag.Visible = false;
        else if (t.Tool != Tool.None) _tag.Show(t.PlanText, t.PlanAffordable ? Ornament.EmberGlow : Ornament.Blood, t.HoverText);
        else
        {
            // with no brush in hand the tag tells who is under the cursor
            var u = _m.Command.UnitAt(GetViewport().GetMousePosition(), pawnsFirst: _m.Selected.Count == 0);
            if (u == null && t.HoverGate is { } gate)
            {
                _tag.Show("Ворота · " + Commander.GateWord(gate), gate.Broken ? Ornament.Blood : Ornament.EmberGlow,
                    gate.Broken || !gate.Ready ? "" : "щелчок — сменить: открываются сами / стоят открытыми / на засове");
                return;
            }
            if (u == null && t.HoverPlace is { } place)
            {
                var (head2, sub2, col) = PlaceTag(place);
                _tag.Show(head2, col, sub2);
                return;
            }
            if (u == null) { _tag.Visible = false; return; }
            string head = u.Team == Team.Wild ? $"{u.T.Name} · {u.T.Bounty} зол. за добычу" : $"{u.Name} · {Math.Ceiling(Math.Max(0, u.Hp))} из {Math.Ceiling(u.MaxHp)}";
            string sub = Retinue.Doing(u, _m.Sim);
            if (u.Team != Team.Player && _m.Selected.Count > 0) sub += u.Team == Team.Wild ? " · ПКМ — охотиться" : " · ПКМ — атаковать";
            _tag.Show(head, u.Team == Team.Horde ? Ornament.Blood : u.Team == Team.Wild ? Ornament.Moss : Ornament.EmberGlow, sub);
        }
    }

    /// <summary>The lines of the goals panel: what is done and what is still to do.</summary>
    IEnumerable<(string text, bool done)> Goals()
    {
        var sim = _m.Sim;
        if (sim.FortArea != default)
        {
            // how sound the fort is: counted over its whole design, so not every fifth of a second
            if (_clock >= _soundAt)
            {
                _soundAt = _clock + 2;
                var (x0, z0, x1, z1) = sim.FortArea;
                _sound = sim.Soundness(x0, z0, x1, z1);
            }
            var (meant, standing) = _sound;
            int whole = meant == 0 ? 100 : (int)(standing * 100L / meant);
            yield return standing >= meant - meant / 200 ? ("Крепость цела", true) : ($"Починить крепость (0 — починка): цела на {whole} %", false);
            if (sim.FortGate is { } g)
                yield return g.Ready && !g.Broken ? ("Ворота навешены", true) : g.Broken ? ("Навесить ворота: починкой по проёму", false) : ("Ворота навешивают", false);
        }
        foreach (var m in sim.Mines)
            yield return m.Holder switch
            {
                Holder.Ours => ($"{m.Name} ваш: +{m.Income} зол./мин", true),
                Holder.Horde => ($"Отбить у орды: {m.Name}, +{m.Income} зол./мин", false),
                _ => ($"Занять: {m.Name}, +{m.Income} зол./мин", false),
            };
        var w = sim.Waves;
        int held = Math.Max(0, w.Number - (w.Active ? 1 : 0));
        yield return sim.Won ? ("Осада снята", true) : ($"Выстоять: отбито волн {held} из {Waves.Total}", false);
    }

    void RefreshGoals()
    {
        int i = 0, open = 0;
        foreach (var (text, done) in Goals())
        {
            if (i >= _goalLines.Count)
            {
                var l = Text("", Ornament.Serif, 13, Ornament.Parchment, wrap: true);
                _goals.AddChild(l);
                _goalLines.Add(l);
            }
            var line = _goalLines[i++];
            line.Text = (done ? "◆ " : "◇ ") + text;
            line.AddThemeColorOverride("font_color", done ? Ornament.Moss : Ornament.Parchment);
            line.Visible = true;
            if (!done) open++;
        }
        for (; i < _goalLines.Count; i++) _goalLines[i].Visible = false;
        // what is still to do, on the tab
        string badge = open > 0 ? open.ToString() : "";
        if (_goalsTab.Badge != badge) { _goalsTab.Badge = badge; _goalsTab.QueueRedraw(); }
        int bands = 0;
        foreach (var b in _m.Sim.Bands) if (b.State != BandState.Coming || !_m.Sim.Waves.Active) bands++;
        string hb = bands > 0 ? bands.ToString() : "";
        if (_hordeTab.Badge != hb) { _hordeTab.Badge = hb; _hordeTab.QueueRedraw(); }
        _horde.Refresh();
    }

    /// <summary>What the tag says over a mine or a ruin.</summary>
    (string head, string sub, Color color) PlaceTag(Landmark l)
    {
        if (l.Kind == LandmarkKind.Ruin) return (l.Name, "старая кладка: её можно поднять как стояла (0 — починка) или разобрать на камень (8 и Z)", Ornament.Aged);
        var m = _m.Sim.Mines.Find(x => x.Place == l);
        if (m == null) return (l.Name, "", Ornament.Aged);
        string pay = $"+{m.Income} зол./мин";
        if (m.Ours > 0 && m.Theirs > 0) return ($"{m.Name} · за него бьются", pay, Ornament.EmberGlow);
        return m.Holder switch
        {
            Holder.Ours => ($"{m.Name} · ваш · {pay}", "держите людей у входа: орда придёт его отбивать. Руда в жиле — ещё золото", Ornament.Moss),
            Holder.Horde => ($"{m.Name} · у орды", $"перебейте её у входа и встаньте там сами · {pay}", Ornament.Blood),
            _ => ($"{m.Name} · ничей", $"пошлите людей к входу — займут за несколько секунд · {pay}", Ornament.EmberGlow),
        };
    }

    public void OnToolChanged()
    {
        if (_toolName == null) return;
        var t = _m.Tools;
        foreach (var (tool, s) in _toolSlots) s.Selected = tool == t.Tool;
        foreach (var (mat, s) in _matSlots) s.Selected = !t.Opt.Dig && mat == t.Opt.Material;
        _pickSlot.Selected = t.Opt.Dig;
        _toolName.Text = t.Tool == Tool.Paste && t.PasteBp != null ? $"Чертёж «{t.PasteBp.Name}»" : BuildTools.ToolName(t.Tool);
        _toolMat.Text = (t.Tool == Tool.Gather ? "добывать: " + (t.GatherKind switch { Stockpile.Wood => "лес", Stockpile.Earth => "землю", _ => "камень" })
            : t.Opt.Dig ? "кирка: копать и рубить" : "материал: " + Mat.NameOf(t.Opt.Material).ToLowerInvariant()).ToUpperInvariant();
        // with no brush in hand the belt is only its slots
        _beltHead.Visible = t.Tool != Tool.None;
        RebuildOptions();
        RebuildBlueprints();
    }

    public void OnSelectionChanged()
    {
        _retinue?.Refresh();
        // chosen pawns: the retinue slides out with them, one by one
        if (_m.Selected.Count > 0 && _right != null && _right.OpenKey != "retinue") _right.Open("retinue");
    }

    static string Len(int voxels)
    {
        double m = voxels / (double)VoxelWorld.PerMetre;
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
            case Tool.Bridge:
                _opts.AddChild(new Stepper("Проход", "Ширина прохода\nПо мосту ходят строем: уже 1,25 м его не сделать, к ширине добавляются парапеты или перила. Клавиши [ и ].",
                    () => Len(Math.Max(Bridges.MinWidth, o.Width)), d => t.StepWidth(d)));
                _opts.AddChild(CapsLabel(o.Material == Mat.Wood ? "деревянный" : "каменный", 11, Ornament.BronzeLight));
                break;
            case Tool.Extrude:
                _opts.AddChild(new Stepper("Шаг", "Шаг\nНа сколько нарастает или уходит вглубь грань за щелчок. Клавиши − и =.", () => Len(o.Step), d => t.StepHeight(d)));
                break;
            case Tool.Gather:
                _opts.AddChild(CapsLabel(t.GatherKind switch { Stockpile.Wood => "лес", Stockpile.Earth => "земля", _ => "камень" }, 11, Ornament.BronzeLight));
                if (t.GatherKind != Stockpile.Wood)
                    _opts.AddChild(new Stepper("Глубина", "Глубина\nДо какой глубины от нынешней поверхности брать камень или землю. Клавиши − и =.", () => Len(o.Depth), d => t.StepHeight(d)));
                break;
            case Tool.Paste when t.PasteBp != null:
                _opts.AddChild(CapsLabel($"поворот {t.PasteRot * 90}° · R — повернуть · {t.PasteBp.Describe()}", 10, Ornament.Aged));
                break;
        }
        if (t.Tool is Tool.Line or Tool.Box or Tool.Circle or Tool.Paste or Tool.Gather or Tool.Repair or Tool.Capture or Tool.Unmark or Tool.Bridge)
            _opts.AddChild(new Choice("Сетка", "Сетка\nПривязка к сетке от центра цитадели: начало и конец штриха встают в её узлы, линии идут вдоль осей или под 45°, " +
                "круги — целыми шагами. Стены выходят ровными, а одинаковое по обе стороны цитадели — симметричным. Клавиша N.",
                new[] { "нет", "50 см", "1 м", "2 м" }, Array.IndexOf(ToolOptions.Grids, o.Grid), i => t.SetGrid(ToolOptions.Grids[i])));
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
        if (_toasts == null) return;
        var color = kind switch { Kind.Ok => Ornament.Moss, Kind.Warn => Ornament.BronzeLight, Kind.Bad => Ornament.Blood, _ => Ornament.Parchment };
        _toasts.Push(text, color);
        // and into the chronicle's tab, newest on top
        var l = Text(text, Ornament.Serif, 13, color, wrap: true);
        _history.AddChild(l);
        _history.MoveChild(l, 0);
        while (_history.GetChildCount() > 80) _history.GetChild(_history.GetChildCount() - 1).QueueFree();
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

    /// <summary>Which tab is open on either side (null for none), for tests.</summary>
    public string LeftOpen => _left?.OpenKey;
    public string RightOpen => _right?.OpenKey;
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
