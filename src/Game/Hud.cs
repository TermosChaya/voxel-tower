using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The interface, made as the frame of the game rather than laid over it, and kept to the edges: an oak beam along
/// the top with the tabs and the stores, the wave clock hanging from it, the faces of the household under it; along
/// the bottom a console of boards between two stone plinths — the map of the valley in its ring on the left, the face
/// of the chosen, what is in hand or who is chosen in the middle, the tools on the right. The tabs let their panels
/// down from the beam: the goals, the chronicle, the blueprint scrolls, the work table, the horde. The latest lines
/// of the chronicle show briefly above the console; health bars and the selection frame are drawn on the field.
/// </summary>
public partial class Hud : CanvasLayer
{
    public enum Kind { Plain, Ok, Warn, Bad }

    /// <summary>How much of the bottom edge the console covers across the whole width, for centring the camera above it.</summary>
    public const float BottomBand = BottomConsole.Band;
    /// <summary>How much of the top edge the beam covers.</summary>
    public const float TopBand = TopBeam.Height;

    const string HelpText = "Управление\n" +
        "Кисти ничего не строят сами: они размечают работу, а пешки делают её — кладут из запасов, копают, рубят.\n" +
        "1–5 — кисти, 6 — снять разметку, 7 — лестница, 8 — делянка (добыча с участка), 9 — ворота, 0 — починка, M — мост, B — снять чертёж, N — сетка, Esc — убрать кисть. Z, X, C — камень, брёвна, земля; " +
        "V — кирка: копать, рубить, ломать камень, разбирать свою кладку. Shift с кистью — тоже кирка. Те же кисти — в сетке справа внизу.\n" +
        "[ и ] — толщина, − и = — высота (с киркой — глубина). F — форма или заливка, G — ровно или по рельефу. Ctrl+Z — отменить последнюю разметку.\n" +
        "Без кисти: ЛКМ — выбрать пешку, рамкой — нескольких; ПКМ по земле — послать (посланные вместе идут строем), по зверю — охотиться, по врагу — атаковать.\n" +
        "Лица дружины под верхней балкой: щелчок — выбрать, ещё раз — показать, Shift — добавить. Что с выбранным и какие работы ему велены — посередине внизу.\n" +
        "Вкладки на балке: задачи, летопись, чертежи, работы (кто что делает и в каком порядке, 1 — первым делом), орда.\n" +
        "Пробел — позвать орду раньше (она меньше узнает о вас), P — пауза, Tab — скорость, T — тревога, J — пути орды.\n" +
        "Ctrl+1…5 — запомнить выбранных отрядом, Shift+1…5 — выбрать отряд (дважды — показать).\n" +
        "ПКМ тянет карту, колесо — масштаб, СКМ или Q/E — поворот, WASD — сдвиг, Y — вид сверху, H — вернуть вид.\n" +
        "Ctrl и + или − — интерфейс крупнее или мельче, Ctrl+0 — как был (на Mac — ⌘).";

    public static readonly (Tool tool, Icons.Painter icon, string key, string tip)[] ToolDefs =
    {
        (Tool.Brush, Icons.Brush, "1", "Кисть\nВедите по земле — рабочие выложат столб материала до заданной высоты: так рисуют стены от руки. Шар налипает на поверхность. С киркой кисть размечает, что копать, а шар пробивает ходы. F — форма, Shift — кирка на один мазок."),
        (Tool.Line, Icons.StraightLine, "2", "Линия\nЗажмите и протяните: прямая стена заданной толщины и высоты. С киркой — ров или траншея."),
        (Tool.Box, Icons.Box, "3", "Прямоугольник\nОт угла до угла. Сплошной — помост или основание, стенки — двор или квадратная башня. С киркой — котлован. F — сплошной или стенки."),
        (Tool.Circle, Icons.Circle, "4", "Круг\nИз центра к краю. Кольцо — круглая башня, сплошной — бастион. С киркой — круглая яма или кольцевой ров. F — сплошной или кольцо."),
        (Tool.Extrude, Icons.Extrude, "5", "Выдавить\nЩелчок по ровной грани: она нарастёт на шаг выбранным материалом. С киркой — грань вынут на шаг вглубь: ступени в склоне, проход в скале."),
        (Tool.Unmark, Icons.Erase, "6", "Снять разметку\nРамка снимает всю разметку внутри, щелчок — разметку поблизости. Что уже сделано, остаётся."),
        (Tool.Ladder, Icons.Ladder, "7", "Лестница\nЩелчок по боковой грани стены, башни или обрыва: рабочие приставят лестницу от земли до верха. По лестницам ваши пешки поднимаются на стены. Орда тоже ими пользуется — и ставит свои."),
        (Tool.Gather, Icons.Gather, "8", "Делянка\nРамкой отметьте участок, откуда брать ресурс: Z — камень (скалы и валуны), X — лес, C — земля; у камня и земли — глубина. Размечать каждое дерево не нужно: свободные пешки сами берут оттуда работу понемногу, а разметка стен и рвов идёт первой. В консоли можно выбрать и «убежище»: участок, куда по тревоге (T) уходят мирные."),
        (Tool.Gate, Icons.GateIcon, "9", "Ворота\nЩелчок по боковой грани стены: рабочие прорубят проём в два метра шириной и три высотой и навесят дубовые створки на железе. Свои открывают их на ходу, перед ордой они закрыты, и рубить их ей не легче камня. Щелчок по воротам без кисти — держать открытыми, на засов или снова сами."),
        (Tool.Repair, Icons.Mend, "0", "Починка\nЩелчок по разрушенной постройке: рабочие вернут ей прежний вид — разберут завал, где он лёг на кладку, доложат недостающее тем же камнем, навесят ворота. Помнится всё, что вы построили, и старая крепость тоже. Рамка чинит только то, что в ней."),
        (Tool.Bridge, Icons.BridgeIcon, "M", "Мост\nЗажмите на одном берегу и протяните до другого: рабочие перекинут мост, который идёт аркой, а не плоско. Каменный (Z) — на арке, с парапетами; деревянный (X) — настил на гнутых рёбрах, с перилами. Строят от берегов, стоя на уже уложенном. Пролёт — от 2 до 25 м. Подъёмный («Вид» в консоли) — дубовый настил над рвом от 2 до 6 м на петлях у берега, что ближе к цитадели: опущен, пока не подойдёт орда, тогда поднимается сам и сбрасывает в ров тех, кто на нём. Щелчок по нему без кисти — сам / опущен / поднят."),
        (Tool.Capture, Icons.Scroll, "B", "Снять чертёж\nОбведите рамкой свою постройку — она станет чертежом. Копии ставятся из свитков во вкладке «Чертежи», R поворачивает."),
    };

    public static readonly (byte mat, string key, int store, string tip)[] MatDefs =
    {
        (Mat.Stone, "Z", Stockpile.Stone, "Камень\nКладётся бутом — неровными камнями на растворе, дольше всего и крепче всего. Его ломают киркой в скалах и валунах; разобранная кладка возвращает три четверти."),
        (Mat.Wood, "X", Stockpile.Wood, "Брёвна\nРубят в лесу: кирка, щелчок по дереву. Ложатся быстрее камня, но топор орка берёт их легко."),
        (Mat.Rammed, "C", Stockpile.Earth, "Земля\nЕё даёт любой ров и котлован. Насыпь кладётся быстрее всего, а гоблину без кирки сквозь неё не пройти."),
    };

    public const string UndoTip = "Отменить — Ctrl+Z (на Mac ⌘Z)\nСнимает последнюю разметку, как не бывало: стену, ров, лестницу, мост, делянку, ворота, починку — и возвращает снятую. До сорока шагов назад. Что уже построено или выкопано, остаётся.";

    public const string PickTip = "Кирка\nКисти размечают не кладку, а работу кирки: выкопать ров, срубить лес, наломать камня, разобрать свою стену. Всё добытое идёт в запасы.";

    Main _m;
    Toasts _toasts;
    VBoxContainer _history, _goals;
    readonly List<Label> _goalLines = new();
    TopBeam _beam;
    HBoxContainer _tabs;
    DropPanel _drop;
    TabPlaque _goalsTab, _hordeTab;
    Stores _stores;
    WaveClock _clockPanel;
    Medallion _help, _menuButton;
    GameMenu _menu;
    EnvoyCard _envoy;
    ColonistBar _colonists;
    BottomConsole _console;
    MapRing _ring;
    PortraitFrame _portrait;
    Inspector _inspector;
    HordeInfo _horde;
    WorkTable _works;
    readonly Dictionary<Tool, Slot> _toolSlots = new();
    readonly Dictionary<byte, Slot> _matSlots = new();
    Slot _pickSlot;
    VBoxContainer _bpList;
    Label _bpCount;
    CursorTag _tag;
    Control _floats;
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

    /// <summary>What a tool does, in its first sentence, for the console while it is in hand.</summary>
    public static string ToolHint(Tool t)
    {
        foreach (var d in ToolDefs)
        {
            if (d.tool != t) continue;
            var text = d.tip.Split('\n', 2);
            if (text.Length < 2) return "";
            int dot = text[1].IndexOf(". ", StringComparison.Ordinal);
            return dot > 0 ? text[1][..(dot + 1)] : text[1];
        }
        return t == Tool.Paste ? "Щелчок по земле — разметить копию; R — повернуть." : "";
    }

    public void Init(Main m)
    {
        _m = m;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = Ornament.MakeTheme(), TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _overlay = new UnitOverlay();
        root.AddChild(_overlay);
        _overlay.Init(m);

        // the bottom console: the map in its ring, the face of the chosen, what is in hand or chosen, the tools
        _console = new BottomConsole();
        root.AddChild(_console);
        _ring = new MapRing();
        _console.Left.AddChild(_ring);
        _ring.Init(m);
        _ring.Position = new Vector2(BottomConsole.LeftW / 2 - MapRing.Outer - 2, BottomConsole.Wing / 2 - MapRing.Outer + 4);
        _portrait = new PortraitFrame();
        _console.Base.AddChild(_portrait);
        _portrait.Init(m);
        _portrait.Position = new Vector2(BottomConsole.LeftW + 10, 15);
        _portrait.Size = new Vector2(PortraitFrame.S, PortraitFrame.S);
        _inspector = new Inspector();
        _console.Base.AddChild(_inspector);
        _inspector.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _inspector.OffsetLeft = BottomConsole.LeftW + PortraitFrame.S + 24;
        _inspector.OffsetRight = -BottomConsole.RightW - 12;
        _inspector.OffsetTop = 17; _inspector.OffsetBottom = -9;
        _inspector.Init(m);
        var grid = new GridContainer { Columns = 4, Position = new Vector2(18, 21) };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var (tool, icon, key, tip) in ToolDefs)
        {
            var sl = new Slot(icon, key, tip, 52);
            var t = tool;
            sl.Clicked += () => _m.Tools.SetTool(t);
            _toolSlots[tool] = sl;
            grid.AddChild(sl);
        }
        foreach (var (mat, key, _, tip) in MatDefs)
        {
            var sl = new Slot(Icons.Material(mat), key, tip, 52) { Caption = "" };
            var mm = mat;
            sl.Clicked += () => _m.Tools.SetMaterial(mm);
            _matSlots[mat] = sl;
            grid.AddChild(sl);
        }
        _pickSlot = new Slot(Icons.Pick, "V", PickTip, 52) { Caption = "копать" };
        _pickSlot.Clicked += () => _m.Tools.SetDig(!_m.Tools.Opt.Dig);
        grid.AddChild(_pickSlot);
        _console.Right.AddChild(grid);

        // the latest lines of the chronicle, briefly, above the console
        _toasts = new Toasts();
        root.AddChild(_toasts);

        // what hangs from the beam goes behind it
        _drop = new DropPanel { Position = new Vector2(10, TopBeam.Height - 3) };
        root.AddChild(_drop);
        _beam = new TopBeam();
        root.AddChild(_beam);
        _tabs = new HBoxContainer { Position = new Vector2(16, 5) };
        _tabs.AddThemeConstantOverride("separation", 6);
        _beam.AddChild(_tabs);
        TabPlaque Tab(string key, Icons.Painter icon, string caption, string tip, Control page, float width)
        {
            var tab = new TabPlaque(icon, caption, tip);
            _tabs.AddChild(tab);
            _drop.Add(key, tab, page, width);
            return tab;
        }
        var goalsBox = new VBoxContainer();
        goalsBox.AddThemeConstantOverride("separation", 5);
        goalsBox.AddChild(CapsLabel("Задачи"));
        _goals = new VBoxContainer();
        _goals.AddThemeConstantOverride("separation", 3);
        goalsBox.AddChild(_goals);
        _goalsTab = Tab("goals", Icons.Scroll, "Задачи", "Задачи\nЧем заняться: починить крепость, занять рудники и держать их — они платят золотом на наём — и выстоять все волны.", goalsBox, 380);
        var chronicle = new VBoxContainer();
        chronicle.AddThemeConstantOverride("separation", 4);
        chronicle.AddChild(CapsLabel("Летопись"));
        _history = new VBoxContainer();
        _history.AddThemeConstantOverride("separation", 4);
        chronicle.AddChild(_history);
        Tab("chronicle", Icons.Horn, "Летопись", "Летопись\nВсё, что было: подсказки начала, вести о волнах, рудниках, павших и сделанном. Последние строки и так видны на минуту над консолью.", chronicle, 440);
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
        Tab("blueprints", Icons.Box, "Чертежи", "Свитки чертежей\nСнятые постройки: щелчок по свитку — разметить копию. B — снять новый чертёж рамкой.", bpBox, 380);
        _works = new WorkTable();
        _works.Init(m);
        Tab("works", Icons.Pick, "Работы", "Работы\nКто из дружины какую работу делает и в каком порядке, как в RimWorld: 1 — первым делом, 4 — в последнюю очередь, пусто — никогда. И кто что добывает сам, кто в ополчении.", _works, WorkTable.Width);
        _horde = new HordeInfo();
        _hordeTab = Tab("horde", Icons.Sword, "Орда", "Орда\nВатаги грядущей волны (или той, что в долине): из кого, кто ведёт, откуда войдут; их пути — на поле и на карте (J). И брёвна с землёй орды на осадные работы.", _horde, 360);
        _horde.Init(m);

        _stores = new Stores();
        _beam.AddChild(_stores);
        _clockPanel = new WaveClock();
        root.AddChild(_clockPanel);
        _clockPanel.Init(m);
        _help = new Medallion(Icons.Question, HelpText) { Size = new Vector2(38, 38) };
        root.AddChild(_help);
        _menuButton = new Medallion(Icons.Menu, "Меню (Esc)\nСохранить и загрузить игру, настройки, выйти. Быстрое сохранение — F5, загрузить его — F9.") { Size = new Vector2(38, 38) };
        _menuButton.Clicked += OpenMenu;
        root.AddChild(_menuButton);
        _colonists = new ColonistBar();
        root.AddChild(_colonists);
        _colonists.Init(m);

        _floats = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _floats.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_floats);
        _tag = new CursorTag { Visible = false };
        root.AddChild(_tag);
        _envoy = new EnvoyCard();
        root.AddChild(_envoy);
        _envoy.Init(m);
        // the menu over everything
        _menu = new GameMenu();
        root.AddChild(_menu);
        _menu.Init(m);

        RebuildBlueprints();
        OnToolChanged();
        Refresh();
        // a new game opens on what there is to do
        _drop.Open("goals");
    }

    // ---------------------------------------------------------------- state

    public void Refresh()
    {
        if (_stores == null) return;
        RefreshGoals();
        var st = _m.Sim.Stock;
        _stores.Values[0] = Fmt.Int(Math.Floor(st.Gold));
        var t = _m.Tools;
        for (int k = 1; k <= 3; k++)
        {
            _stores.Values[k] = Fmt.M3(st[k]);
            _stores.Short[k] = t.Tool != Tool.None && t.PlanText != null && !t.PlanIsDig && t.PlanNeeds[k] > st[k] + .5;
        }
        foreach (var (mat, _, store, _) in MatDefs)
        {
            var s = _matSlots[mat];
            string cap = Fmt.M3(st[store]);
            if (s.Caption != cap) { s.Caption = cap; s.QueueRedraw(); }
        }
        _stores.QueueRedraw();
        _clockPanel.Refresh();
        _inspector.Refresh();
        _envoy.Refresh();
    }

    public override void _Process(double delta)
    {
        if (_stores == null) return;
        _clock += delta;
        _refreshAt -= delta;
        var vp = GetViewport().GetVisibleRect().Size;
        // the beam's furniture: the tabs at the left, the clock hanging at the right, the stores between
        _clockPanel.Position = new Vector2(vp.X - WaveClock.W - 98, 0);
        _help.Position = new Vector2(vp.X - 50, 1);
        _menuButton.Position = new Vector2(vp.X - 92, 1);
        _envoy.Position = new Vector2(vp.X - EnvoyCard.W - 14, WaveClock.H + 10);
        float tabsEnd = _tabs.Position.X + _tabs.Size.X;
        // on a narrow screen (or a large interface) the stores close up rather than go under the clock
        float storesW = Math.Clamp(_clockPanel.Position.X - tabsEnd - 28, Stores.MinW, Stores.W);
        _stores.Size = new Vector2(storesW, 30);
        _stores.Position = new Vector2(Math.Max(tabsEnd + 14, tabsEnd + (_clockPanel.Position.X - tabsEnd - storesW) / 2), 4);
        _drop.Bottom = vp.Y - BottomConsole.Wing - 6;
        _colonists.From = Math.Max(14, _drop.RightEdge + 12);
        _colonists.To = _clockPanel.Position.X - 12;
        _colonists.Top = TopBeam.Height + 5;
        _toasts.Position = new Vector2((vp.X - _toasts.Size.X) / 2, vp.Y - BottomConsole.Band - 14 - _toasts.Size.Y);
        Unit first = null;
        foreach (var u in _m.Selected) if (!u.Dead && (first == null || u.Id < first.Id)) first = u;
        _portrait.Unit = _inspector.One ?? first;
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
            if (u == null && t.HoverBridge is { } draw)
            {
                _tag.Show("Подъёмный мост · " + Commander.BridgeWord(draw), draw.Broken ? Ornament.Blood : Ornament.EmberGlow,
                    draw.Broken || !draw.Ready ? "" : "щелчок — сменить: сам / опущен / поднят");
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
        if (_inspector == null) return;
        var t = _m.Tools;
        foreach (var (tool, s) in _toolSlots) s.Selected = tool == t.Tool;
        foreach (var (mat, s) in _matSlots) s.Selected = !t.Opt.Dig && mat == t.Opt.Material;
        _pickSlot.Selected = t.Opt.Dig;
        _inspector.Refresh(rebuild: true);
        RebuildBlueprints();
    }

    public void OnSelectionChanged() => _inspector?.Refresh();

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
            info.AddChild(Text($"{Inspector.Len(bp.W)} × {Inspector.Len(bp.D)} × {Inspector.Len(bp.H)}", Ornament.Serif, 12, Ornament.Aged));
            info.AddChild(Text(bp.Describe(), Ornament.Serif, 12, Ornament.BronzeLight));
            row.AddChild(info);
            var del = new Button { Text = "×", FocusMode = Control.FocusModeEnum.None, TooltipText = "Сжечь свиток", CustomMinimumSize = new Vector2(26, 26), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            del.Pressed += () => _m.RemoveBlueprint(b);
            row.AddChild(del);
            _bpList.AddChild(row);
        }
    }

    // ---------------------------------------------------------------- messages

    /// <summary>A line for the chronicle; one that happened at a place (voxels of the valley) takes the camera there when clicked.</summary>
    /// <summary>The chronicle, oldest first, as it was logged: what a save keeps of it.</summary>
    public readonly List<(string text, Kind kind, float x, float z)> Lines = new();

    public void Log(string text, Kind kind = Kind.Plain, float x = float.NaN, float z = float.NaN, bool toast = true)
    {
        if (_toasts == null) return;
        var color = kind switch { Kind.Ok => Ornament.Moss, Kind.Warn => Ornament.BronzeLight, Kind.Bad => Ornament.Blood, _ => Ornament.Parchment };
        if (toast) _toasts.Push(text, color);
        Lines.Add((text, kind, x, z));
        if (Lines.Count > 80) Lines.RemoveAt(0);
        // and into the chronicle's tab, newest on top
        var l = Text(float.IsNaN(x) ? text : "◎ " + text, Ornament.Serif, 13, color, wrap: true);
        if (!float.IsNaN(x))
        {
            float px = x, pz = z;
            l.MouseFilter = Control.MouseFilterEnum.Stop;
            l.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            l.TooltipText = "Где это было\nЩелчок — посмотреть на это место.";
            l.MouseEntered += () => l.AddThemeColorOverride("font_color", color.Lightened(.3f));
            l.MouseExited += () => l.AddThemeColorOverride("font_color", color);
            l.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                {
                    _m.Rig.Focus(_m.World.WX(px), _m.World.WZ(pz));
                    l.AcceptEvent();
                }
            };
        }
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
    public Minimap Map => _ring.Map;

    /// <summary>Which panel hangs from the beam (null for none), and who the console shows in full, for tests.</summary>
    public string OpenTab => _drop?.OpenKey;
    public Unit Inspecting => _inspector?.One;
    public void OpenPanel(string key) => _drop.Open(key);
    public bool MenuOpen => _menu?.IsOpen == true;
    public void OpenMenu() => _menu?.Open();
    public void OpenMenu(string page) => _menu?.Open(page);
    public void CloseMenu() => _menu?.Close();
    /// <summary>Esc with the menu open: back a page, or out of it.</summary>
    public void MenuBack() => _menu?.Back();
    public ColonistBar Colonists => _colonists;
    public WorkTable Works => _works;
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
