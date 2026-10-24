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

    const string HelpTip = "Управление ({Help})\nВсе клавиши и кнопки мыши. Любую клавишу можно переназначить.";

    public static readonly (Tool tool, Icons.Painter icon, Act act, string tip)[] ToolDefs =
    {
        (Tool.Brush, Icons.Brush, Act.ToolBrush, "Кисть ({ToolBrush})\nРисует стену от руки до заданной высоты. Шар налипает на поверхность. С киркой кисть размечает, что копать. {Form} меняет форму, Shift даёт кирку на один мазок."),
        (Tool.Line, Icons.StraightLine, Act.ToolLine, "Линия ({ToolLine})\nПрямая стена заданной толщины и высоты. С киркой — ров. {Grid} включает сетку."),
        (Tool.Box, Icons.Box, Act.ToolBox, "Прямоугольник ({ToolBox})\nТянется от угла до угла: сплошной помост, одни стенки или дом (стены, крыша из брёвен и ворота; потом рабочие поставят лежанки). С киркой — котлован. {Form} переключает."),
        (Tool.Circle, Icons.Circle, Act.ToolCircle, "Круг ({ToolCircle})\nТянется от центра к краю: кольцо башни или сплошной бастион. С киркой — яма или кольцевой ров. {Form} переключает сплошной и кольцо."),
        (Tool.Extrude, Icons.Extrude, Act.ToolExtrude, "Выдавить ({ToolExtrude})\nЩелчок по ровной грани наращивает её на шаг. С киркой грань углубляется: так делают ступени в склоне и проходы в скале."),
        (Tool.Unmark, Icons.Erase, Act.ToolUnmark, "Снять разметку ({ToolUnmark})\nРамка снимает всю разметку внутри, щелчок — рядом с курсором. Сделанное остаётся."),
        (Tool.Ladder, Icons.Ladder, Act.ToolLadder, "Лестница ({ToolLadder})\nЩелчок по стене, башне или обрыву. Рабочие поставят лестницу от земли до верха. Орда тоже лазит по лестницам и ставит свои."),
        (Tool.Gather, Icons.Gather, Act.ToolGather, "Делянка ({ToolGather})\nУчасток, где рабочие сами добывают ресурс: {MatStone} — камень, {MatWood} — лес, {MatEarth} — землю. Для камня и земли задаётся глубина. Стены и рвы рабочие делают в первую очередь.\nВ нижней панели делянку можно сделать убежищем (туда уходят по тревоге {Alarm}), святилищем (там молятся жрецы) или жилым домом (закрытая комната с воротами, каждая лежанка в нём даёт место в войске)."),
        (Tool.Gate, Icons.GateIcon, Act.ToolGate, "Ворота ({ToolGate})\nЩелчок по стене. Рабочие прорубят проём 2 × 3 м и навесят дубовые створки. Свои проходят, перед ордой ворота закрыты. Щелчок по воротам без кисти: открываются сами, открыты или на засове."),
        (Tool.Repair, Icons.Mend, Act.ToolRepair, "Починка ({ToolRepair})\nЩелчок по разрушенной постройке. Рабочие уберут завалы, восстановят кладку и ворота, переложат треснувшие камни. Чинится всё, что вы построили, и старая крепость. Рамкой можно чинить по частям."),
        (Tool.Bridge, Icons.BridgeIcon, Act.ToolBridge, "Мост ({ToolBridge})\nПротяните от берега до берега, пролёт от 2 до 25 м. Каменный ({MatStone}) идёт аркой и с парапетами, деревянный ({MatWood}) — настил с перилами.\nПодъёмный мост (вид выбирают в нижней панели) — настил над рвом от 2 до 6 м. Когда подходит орда, он поднимается сам и сбрасывает в ров тех, кто на нём. Щелчок по нему без кисти: сам, опущен или поднят."),
        (Tool.Capture, Icons.Scroll, Act.ToolCapture, "Снять чертёж ({ToolCapture})\nОбведите постройку рамкой, и она сохранится в нижней панели этого инструмента. {Rotate} поворачивает копию."),
        (Tool.Wand, Icons.WandIcon, Act.ToolWand, "Волшебная палочка ({ToolWand})\nНаведите на постройку: подсветится всё, что примыкает к ней из того же материала. Например, каменная стена без деревянного настила на ней. " +
            "С киркой ({Pick}) щелчок сносит выделенное, с камнем, брёвнами или землёй перекладывает его из этого материала. В нижней панели с выделенного можно снять чертёж.\n" +
            "{Form} берёт только воксели того же цвета. Shift берёт и то, что касается ребром или углом."),
    };

    /// <summary>The tools on the belt, a row to each purpose: building, the works of the fort, marking out.</summary>
    static readonly Tool[][] Belt =
    {
        new[] { Tool.Brush, Tool.Line, Tool.Box, Tool.Circle, Tool.Extrude },
        new[] { Tool.Gate, Tool.Bridge, Tool.Ladder, Tool.Repair },
        new[] { Tool.Gather, Tool.Unmark, Tool.Wand, Tool.Capture },
    };
    const float SlotSize = 44;

    public static readonly (byte mat, Act act, int store, string tip)[] MatDefs =
    {
        (Mat.Stone, Act.MatStone, Stockpile.Stone, "Камень ({MatStone})\nСамый прочный, но кладётся дольше всего. Добывают киркой в скалах и валунах. Разобранная кладка возвращает 75 % камня."),
        (Mat.Wood, Act.MatWood, Stockpile.Wood, "Брёвна ({MatWood})\nРубят в лесу. Кладутся быстрее камня, но орки легко их рубят, и они горят."),
        (Mat.Rammed, Act.MatEarth, Stockpile.Earth, "Земля ({MatEarth})\nДаёт любой ров и котлован. Кладётся быстрее всего. Гоблин без кирки сквозь насыпь не пройдёт."),
    };

    public const string UndoTip = "Отменить: Ctrl+Z (на Mac ⌘Z)\nСнимает последнюю разметку или возвращает снятую, до 40 шагов назад. Построенное и выкопанное остаётся.";

    public const string PickTip = "Кирка ({Pick})\nС киркой кисти размечают раскопки: рвы, рубку леса, добычу камня, разборку стен. Всё добытое идёт в запасы.";

    Main _m;
    Control _root;
    Toasts _toasts;
    Hints _hints;
    Advisor _advisor;
    FirstLook _first;
    string _lastTab;
    VBoxContainer _history, _goals;
    readonly List<Label> _goalLines = new();
    TopBeam _beam;
    HBoxContainer _tabs;
    DropPanel _drop;
    TabPlaque _goalsTab, _hordeTab, _loreTab, _worksTab;
    VBoxContainer _toolGrid;
    LorePanel _lore;
    MysteryPanel _mysteries;
    Stores _stores;
    WaveClock _clockPanel;
    Medallion _help, _menuButton;
    GameMenu _menu;
    EnvoyCard _envoy;
    DuskCard _dusk;
    DawnCard _dawn;
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
        // no caption smaller than this: spaced capitals below it are no longer read at a glance
        l.AddThemeFontSizeOverride("font_size", Math.Max(size, 9));
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
            return Bindings.Fill(dot > 0 ? text[1][..(dot + 1)] : text[1]);
        }
        return t == Tool.Paste ? Bindings.Fill("Щелчок по земле ставит копию, {Rotate} поворачивает.") : "";
    }

    public void Init(Main m)
    {
        _m = m;
        Juice.HookButtons(m.GetTree());
        var root = _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = Ornament.MakeTheme(), TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled };
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
        // the tools in rows by what they are for, a gap between the rows: building, the works of the fort, marking out
        // what is to be taken or set aside, and the material with the pick
        var belt = _toolGrid = new VBoxContainer { Position = new Vector2(14, 24) };
        belt.AddThemeConstantOverride("separation", 12);
        foreach (var row in Belt)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 4);
            belt.AddChild(line);
            foreach (var tool in row)
            {
                var (_, icon, act, tip) = Array.Find(ToolDefs, d => d.tool == tool);
                var sl = new Slot(icon, Bindings.Label(act), tip, SlotSize);
                var t = tool;
                sl.Clicked += () => _m.Tools.SetTool(t);
                _toolSlots[tool] = sl;
                line.AddChild(sl);
            }
        }
        var stuff = new HBoxContainer();
        stuff.AddThemeConstantOverride("separation", 4);
        belt.AddChild(stuff);
        foreach (var (mat, act, _, tip) in MatDefs)
        {
            var sl = new Slot(Icons.Material(mat), Bindings.Label(act), tip, SlotSize) { Caption = "" };
            var mm = mat;
            sl.Clicked += () => _m.Tools.SetMaterial(mm);
            _matSlots[mat] = sl;
            stuff.AddChild(sl);
        }
        _pickSlot = new Slot(Icons.Pick, Bindings.Label(Act.Pick), PickTip, SlotSize) { Caption = "копать" };
        _pickSlot.Clicked += () => _m.Tools.SetDig(!_m.Tools.Opt.Dig);
        stuff.AddChild(_pickSlot);
        _console.Right.AddChild(belt);

        // the latest lines of the chronicle, briefly, above the console
        _toasts = new Toasts();
        root.AddChild(_toasts);
        // and under them what the mouse and the keys do now
        _hints = new Hints();
        root.AddChild(_hints);
        _hints.Init(m);
        // what is amiss now, over the map
        _advisor = new Advisor();
        root.AddChild(_advisor);
        _advisor.Init(m);
        // and the card the first time a tool or a tab is met
        _first = new FirstLook();
        root.AddChild(_first);

        // what hangs from the beam goes behind it
        _drop = new DropPanel { Position = new Vector2(10, TopBeam.Height - 3) };
        root.AddChild(_drop);
        _beam = new TopBeam();
        root.AddChild(_beam);
        _tabs = new HBoxContainer { Position = new Vector2(16, 5) };
        _tabs.AddThemeConstantOverride("separation", 10);
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
        // what to do next, with its reward; under it what the whole siege asks, and under that all that happened
        _errandsHead = CapsLabel("Поручения", 10, Ornament.EmberGlow);
        goalsBox.AddChild(_errandsHead);
        _errands = new VBoxContainer();
        _errands.AddThemeConstantOverride("separation", 4);
        goalsBox.AddChild(_errands);
        _goalsHead = CapsLabel("Цели осады");
        goalsBox.AddChild(_goalsHead);
        _goals = new VBoxContainer();
        _goals.AddThemeConstantOverride("separation", 3);
        goalsBox.AddChild(_goals);
        var chronicleHead = CapsLabel("Летопись");
        chronicleHead.CustomMinimumSize = new Vector2(0, 30);
        chronicleHead.VerticalAlignment = VerticalAlignment.Bottom;
        goalsBox.AddChild(chronicleHead);
        _history = new VBoxContainer();
        _history.AddThemeConstantOverride("separation", 4);
        goalsBox.AddChild(_history);
        _goalsTab = Tab("goals", Icons.Of("checklist"), "Задачи", "Задачи\nПоручения: одно главное и два малых, за каждое награда. Ниже цели на всю осаду: починить крепость, занять рудники, " +
            "выстоять все волны. Ещё ниже летопись: все события игры, последние строки минуту видны над нижней панелью.", goalsBox, 440);
        _works = new WorkTable();
        _works.Init(m);
        var worksTab = _worksTab = Tab("works", Icons.Pick, "Работы", "Работы\nКто какую работу делает и в каком порядке: 1 — первым делом, 4 — в последнюю очередь, пусто — никогда. Здесь же: кто добывает сам и кто в ополчении.", _works, WorkTable.Width);
        _horde = new HordeInfo();
        _hordeTab = Tab("horde", Icons.Of("goblin-head"), "Орда", "Орда\nВатаги ближайшей волны: состав, вожак, откуда придут. Их пути видны на поле и на карте ({Trails}). Ниже запасы орды на осадные работы.", _horde, 360);
        _horde.Init(m);
        _lore = new LorePanel();
        _lore.Init(m);
        _loreTab = Tab("lore", Icons.Tome, "Знания", "Знания\nДрево улучшений на трёх страницах: «Умения», «Войско» и «Вмешательство». Умения и войско покупают за знания, их дают за отбитые волны. Заклинания покупают за вмешательство и откровения. На четвёртой странице «Таинства»: вмешательство, святилища, жертвы. Число на вкладке — сколько знаний можно потратить, ✦ — можно изучить заклинание.", _lore, LorePanel.Width);
        _mysteries = new MysteryPanel();
        _mysteries.Init(m);
        _lore.AddMysteries(_mysteries, "Таинства\nВмешательство и откровения, святилища и жрецы, жертвы: пленные, туши с охоты или свои люди. " +
            "Число на вкладке — сколько пленных ждёт в цитадели.");
        _squeeze.AddRange(new[] { worksTab, _goalsTab, _hordeTab, _loreTab });

        _stores = new Stores();
        _beam.AddChild(_stores);
        _clockPanel = new WaveClock();
        root.AddChild(_clockPanel);
        _clockPanel.Init(m);
        _help = new Medallion(Icons.Question, HelpTip) { Size = new Vector2(38, 38) };
        _help.Clicked += () => OpenMenu("controls");
        root.AddChild(_help);
        KeyTags.Tag(_help, Act.Help);
        _menuButton = new Medallion(Icons.Menu, "Меню (Esc)\nСохранение, загрузка, управление, настройки, обучение, выход. Быстрое сохранение {QuickSave}, загрузка {QuickLoad}.") { Size = new Vector2(38, 38) };
        _menuButton.Clicked += OpenMenu;
        root.AddChild(_menuButton);
        KeyTags.Tag(_menuButton, () => "Esc");
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
        _dusk = new DuskCard();
        root.AddChild(_dusk);
        _dusk.Init(m);
        _dawn = new DawnCard();
        root.AddChild(_dawn);
        _dawn.Init(m);
        // the menu over everything
        _menu = new GameMenu();
        root.AddChild(_menu);
        _menu.Init(m);
        // and over all of it the keys on the buttons, while Alt is held
        root.AddChild(new KeyTags());
        Unfold = new Unfold(m);

        OnToolChanged();
        Refresh();
        Bindings.Changed += Rebound;
        TreeExiting += () => Bindings.Changed -= Rebound;
        // a new game opens on what there is to do
        _drop.Open("goals");
    }

    // ---------------------------------------------------------------- state

    static readonly Func<double, string> Whole = v => Fmt.Int(Math.Floor(v)), Cubic = v => Fmt.M3(v);

    public void Refresh()
    {
        if (_stores == null) return;
        RefreshGoals();
        var st = _m.Sim.Stock;
        _stores.Set(0, Math.Floor(st.Gold), Whole, 5);
        _stores.Set(4, Math.Floor(_m.Sim.Favour), Whole, 5);
        int have = _m.Sim.Household(), places = _m.Sim.Places;
        _stores.Values[5] = $"{have}/{places}";
        _stores.Short[5] = have >= places;
        var t = _m.Tools;
        for (int k = 1; k <= 3; k++)
        {
            _stores.Set(k, st[k], Cubic, 2 * VoxelWorld.PerM3);
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
        _dusk.Refresh();
        _dawn.Refresh();
    }

    /// <summary>In first person: the frame of the game is put away; the chronicle's toasts, the floats and the menu stay.</summary>
    public bool InFirstPerson { get; private set; }

    public void FirstPerson(bool on)
    {
        InFirstPerson = on;
        foreach (CanvasItem c in new CanvasItem[] { _overlay, _console, _drop, _beam, _help, _menuButton, _tag, _envoy, _dusk, _dawn }) c.Visible = !on;
        _clockPanel.Visible = !on && !_hidden.Contains(Part.Clock);
        _colonists.Visible = !on && !_hidden.Contains(Part.Faces);
        if (on) _drop.Close();
    }

    // ---------------------------------------------------------------- the frame let out part by part

    /// <summary>The parts of the frame the tutorial lets out one by one; in a game of one's own all of them are out from the start.</summary>
    public enum Part { Map, Faces, Console, Stores, Clock, Goals, Works, Horde, Lore, Magic }

    readonly HashSet<Part> _hidden = new();
    readonly HashSet<Tool> _lockedTools = new();
    // the tools not yet let out in a siege that unfolds (Ui/Unfold.cs): not on the belt at all, their keys refused
    readonly HashSet<Tool> _notYet = new();
    /// <summary>The frame unfolding in a siege of one's own.</summary>
    public Unfold Unfold { get; private set; }
    bool _matsLocked, _pickLocked;

    /// <summary>The tutorial's start: only the field and the frame's bare boards; every tool slot empty and shut.</summary>
    public void Conceal()
    {
        foreach (Part p in Enum.GetValues(typeof(Part))) { _hidden.Add(p); Shown(p, false); }
        foreach (var (tool, s) in _toolSlots) { _lockedTools.Add(tool); s.Locked = true; }
        _matsLocked = _pickLocked = true;
        foreach (var s in _matSlots.Values) s.Locked = true;
        _pickSlot.Locked = true;
        _drop.Close();
    }

    /// <summary>Everything out, as in a game of one's own (the tutorial ended or left).</summary>
    public void RevealAll()
    {
        foreach (Part p in Enum.GetValues(typeof(Part))) Reveal(p);
        foreach (var t in new List<Tool>(_lockedTools)) Unlock(t);
        UnlockMaterials(); UnlockPick();
    }

    public void Reveal(Part p)
    {
        if (!_hidden.Remove(p)) return;
        Shown(p, true);
    }

    public bool Hidden(Part p) => _hidden.Contains(p);

    void Shown(Part p, bool on)
    {
        switch (p)
        {
            case Part.Map: _ring.Visible = on; break;
            case Part.Faces: _colonists.Visible = on && !InFirstPerson; break;
            case Part.Console: _portrait.Visible = _inspector.Visible = on; break;
            case Part.Stores: _stores.Visible = on; break;
            case Part.Clock: _clockPanel.Visible = on && !InFirstPerson; break;
            case Part.Magic: _lore.ShowMysteries(on); break;
            default: if (TabOf(p) is { } tab) { tab.Visible = on; if (!on && _drop.OpenKey == KeyOf(p)) _drop.Close(); } break;
        }
    }

    static string KeyOf(Part p) => p switch
    {
        Part.Goals => "goals", Part.Works => "works",
        Part.Horde => "horde", Part.Lore => "lore", _ => null,
    };

    TabPlaque TabOf(Part p) => p switch
    {
        Part.Goals => _goalsTab, Part.Works => _worksTab,
        Part.Horde => _hordeTab, Part.Lore => _loreTab, _ => null,
    };

    public void Unlock(Tool t)
    {
        if (!_lockedTools.Remove(t)) return;
        if (_toolSlots.TryGetValue(t, out var s)) s.Locked = false;
    }

    public void UnlockMaterials() { _matsLocked = false; foreach (var s in _matSlots.Values) s.Locked = false; }
    public void UnlockPick() { _pickLocked = false; _pickSlot.Locked = false; }

    /// <summary>A tool, the materials or the pick not yet let out by the tutorial: its key does nothing either.</summary>
    public bool Locked(Tool t) => _lockedTools.Contains(t) || _notYet.Contains(t);
    /// <summary>Why a tool's key does nothing.</summary>
    public string LockedWhy => _notYet.Count > 0 && (_m.Tutor == null || _m.Tutor.Over) ? "это откроется позже" : "это откроется дальше в обучении";

    /// <summary>A tool kept off the belt till the siege lets it out, or let out.</summary>
    public void Withhold(Tool t, bool withheld)
    {
        if (withheld) _notYet.Add(t); else _notYet.Remove(t);
        if (_toolSlots.TryGetValue(t, out var s)) s.Visible = !withheld;
    }

    /// <summary>A part of the frame kept hidden till the siege lets it out, or let out.</summary>
    public void Withhold(Part p, bool withheld)
    {
        if (!withheld) { Reveal(p); return; }
        if (_hidden.Add(p)) Shown(p, false);
    }

    /// <summary>Whether a tool's well wears the mark of new (the self-test reads it).</summary>
    public bool Fresh(Tool t) => _toolSlots.TryGetValue(t, out var s) && s.Fresh;
    /// <summary>The mark of new on a tool or a tab just let out.</summary>
    public void Freshen(Tool t) { if (_toolSlots.TryGetValue(t, out var s)) s.Fresh = true; }
    public void Freshen(Part p) { if (TabOf(p) is { } tab) { tab.Fresh = true; tab.QueueRedraw(); } }
    public bool MaterialsLocked => _matsLocked;
    public bool PickLocked => _pickLocked;

    /// <summary>What the tutorial points at: a tool's slot, a material's, the pick's, a tab, the faces, the map and its buttons…</summary>
    public Control Target(string key) => key switch
    {
        "faces" => _colonists, "map" => _ring, "map:top" => _ring.TopButton, "map:home" => _ring.HomeButton, "console" => _inspector,
        "portrait" => _portrait, "stores" => _stores, "clock" => _clockPanel, "help" => _help, "menu" => _menuButton, "tools" => _toolGrid,
        "pick" => _pickSlot,
        _ when key.StartsWith("tool:") && Enum.TryParse<Tool>(key[5..], out var t) && _toolSlots.TryGetValue(t, out var s) => s,
        _ when key.StartsWith("mat:") && byte.TryParse(key[4..], out var m) && _matSlots.TryGetValue(m, out var ms) => ms,
        _ when key.StartsWith("tab:") => key[4..] switch
        {
            "goals" => _goalsTab, "works" => _worksTab,
            "horde" => _hordeTab, "lore" => _loreTab, "magic" => _loreTab, _ => null,
        },
        _ => null,
    };

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("Hud");
        if (_stores == null) return;
        _clock += delta;
        _refreshAt -= delta;
        var vp = GetViewport().GetVisibleRect().Size;
        if (TitleOpen) { _hints.Visible = _advisor.Visible = _first.Visible = false; return; }
        if (InFirstPerson)
        {
            // only the toasts, in the upper corner, out of the sight
            _toasts.Position = new Vector2(24, 20);
            _tag.Visible = false;
            _hints.Visible = _advisor.Visible = _first.Visible = false;
            if (_refreshAt <= 0) _refreshAt = .2;
            return;
        }
        // the beam's furniture: the tabs at the left, the clock hanging at the right, the stores between
        _clockPanel.Position = new Vector2(vp.X - WaveClock.W - 98, 0);
        _help.Position = new Vector2(vp.X - 50, 1);
        _menuButton.Position = new Vector2(vp.X - 92, 1);
        _envoy.Position = new Vector2(vp.X - EnvoyCard.W - 14, WaveClock.H + 10);
        // the dawn's gifts under the beam in the middle, and the dusk's card under them if the gifts still wait
        _dawn.Position = new Vector2(MathF.Round((vp.X - DawnCard.W) / 2), TopBand + 10);
        _dusk.Position = new Vector2(MathF.Round((vp.X - DuskCard.W) / 2), TopBand + 10 + (_dawn.Visible ? _dawn.Size.Y + 8 : 0));
        Squeeze(_clockPanel.Position.X);
        float tabsEnd = _tabs.Position.X + _tabs.Size.X;
        // on a narrow screen (or a large interface) the stores close up rather than go under the clock
        float storesW = Math.Clamp(_clockPanel.Position.X - tabsEnd - 28, Stores.MinW, Stores.W);
        _stores.Size = new Vector2(storesW, 30);
        _stores.Position = new Vector2(Math.Max(tabsEnd + 14, tabsEnd + (_clockPanel.Position.X - tabsEnd - storesW) / 2), 4);
        _drop.Bottom = vp.Y - BottomConsole.Wing - 6;
        _colonists.From = Math.Max(14, _drop.RightEdge + 12);
        _colonists.To = _clockPanel.Position.X - 12;
        _colonists.Top = TopBeam.Height + 5;
        // a wide panel let down leaves the faces no room before the clock: they go down under it instead
        if (_colonists.To - _colonists.From < 240)
        {
            _colonists.To = vp.X - 14;
            _colonists.Top = _clockPanel.Position.Y + _clockPanel.Size.Y + 6;
        }
        // the hints just over the console between the map and the tools, the toasts over them
        Unfold.Tick();
        _hints.Visible = Settings.Hints && _console.Visible;
        _hints.Room = vp.X - BottomConsole.LeftW - BottomConsole.RightW - 40;
        _hints.Position = new Vector2(MathF.Round((vp.X - _hints.Size.X) / 2), vp.Y - BottomConsole.Band - 8 - Hints.H);
        float hinted = _hints.Visible ? Hints.H + 8 : 0;
        _advisor.Visible = Settings.Advice && _console.Visible && (_m.Tutor == null || _m.Tutor.Over);
        _advisor.Position = new Vector2(14, vp.Y - BottomConsole.Wing - 10 - _advisor.Size.Y);
        if (!_console.Visible) _first.Visible = false;
        _first.Position = new Vector2(vp.X - _first.Size.X - 14, vp.Y - BottomConsole.Wing - 10 - _first.Size.Y);
        if (OpenTab != _lastTab)
        {
            _lastTab = OpenTab;
            // (the mysteries are a page of the lore, met with it)
            if (_lastTab is not (null or "magic") && Target("tab:" + _lastTab) is TabPlaque tab) Meet("tab:" + _lastTab, tab.TooltipText);
        }
        _toasts.Position = new Vector2((vp.X - _toasts.Size.X) / 2, vp.Y - BottomConsole.Band - 14 - hinted - _toasts.Size.Y);
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
                    gate.Broken || !gate.Ready ? "" : "щелчок: открываются сами / открыты / на засове");
                return;
            }
            if (u == null && t.HoverBridge is { } draw)
            {
                _tag.Show("Подъёмный мост · " + Commander.BridgeWord(draw), draw.Broken ? Ornament.Blood : Ornament.EmberGlow,
                    draw.Broken || !draw.Ready ? "" : "щелчок: сам / опущен / поднят");
                return;
            }
            if (u == null && t.HoverKeep)
            {
                var sim = _m.Sim;
                _tag.Show($"Цитадель · войско {sim.Household()} из {sim.Places}", Ornament.EmberGlow, Bindings.Fill("щелчок или {Keep} — набор войска"));
                return;
            }
            if (u == null && t.HoverPlace is { } place)
            {
                var (head2, sub2, col) = PlaceTag(place);
                _tag.Show(head2, col, Bindings.Fill(sub2));
                return;
            }
            if (u == null) { _tag.Visible = false; return; }
            string head = u.Team == Team.Wild ? $"{u.T.Name} · {u.T.Bounty} зол. за добычу" : $"{u.Name} · {Math.Ceiling(Math.Max(0, u.Hp))} из {Math.Ceiling(u.MaxHp)}";
            string sub = Retinue.Doing(u, _m.Sim);
            if (u.Team != Team.Player && _m.Selected.Count > 0) sub += Bindings.Fill(u.Team == Team.Wild ? " · {Rmb} — охотиться" : " · {Rmb} — атаковать");
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
            yield return standing >= meant - meant / 200 ? ("Крепость цела", true) : ($"Починить крепость ({{ToolRepair}}): цела на {whole} %", false);
            if (sim.FortGate is { } g)
                yield return g.Ready && !g.Broken ? ("Ворота навешены", true) : g.Broken ? ("Навесить ворота: починкой по проёму", false) : ("Ворота навешивают", false);
        }
        // a sanctuary on a place of power we hold
        bool heard = false;
        foreach (var z in sim.Zones) if (z.Kind == Zone.Sanctum && sim.PowerUnder(z) is { Holder: Holder.Ours }) heard = true;
        if (sim.Waves.Number > 0 || sim.Zones.Exists(z => z.Kind == Zone.Sanctum))
            yield return heard ? ("Святилище на месте силы", true) : ("Святилище на месте силы ({ToolGather} → «святилище») и жрецы в нём", false);
        foreach (var m in sim.Mines)
            // (one an errand already asks for is not said twice)
            if (!m.Power && !sim.Errands.Exists(e => e.Text.Contains(m.Name.ToLowerInvariant()))) yield return m.Holder switch
            {
                Holder.Ours => ($"{m.Name} ваш: +{m.Income} зол./мин", true),
                Holder.Horde => ($"Отбить у орды: {m.Name}, +{m.Income} зол./мин", false),
                _ => ($"Занять: {m.Name}, +{m.Income} зол./мин", false),
            };
        var w = sim.Waves;
        int held = Math.Max(0, w.Number - (w.Active ? 1 : 0));
        // (the sandbox with everything open has no end to stand till)
        if (!sim.Sandbox) yield return sim.Won ? ("Осада снята", true) : ($"Выстоять: отбито волн {held} из {Waves.Total}", false);
    }

    readonly List<Label> _errandLines = new();
    /// <summary>The errands' lines shown now, and whether the card of what comes tonight hangs (the self-test asks).</summary>
    public int ErrandsShown { get { int n = 0; foreach (var l in _errandLines) if (l.Visible) n++; return n; } }
    public bool DuskShown => _dusk.Visible;
    public bool DawnShown => _dawn.Visible;
    VBoxContainer _errands;
    Label _errandsHead, _goalsHead;

    /// <summary>The errands: the main one large and bright, the small ones under it, each with its reward.</summary>
    int RefreshErrands()
    {
        var list = _m.Sim.Errands;
        _errandsHead.Visible = _errands.Visible = list.Count > 0;
        _goalsHead.Visible = list.Count > 0;
        int i = 0;
        foreach (var e in list)
        {
            if (i >= _errandLines.Count)
            {
                var l = Text("", Ornament.Serif, 13, Ornament.Parchment, wrap: true);
                _errands.AddChild(l);
                _errandLines.Add(l);
            }
            var line = _errandLines[i++];
            string pay = Sim.Pay(e);
            string text = (e.Main ? "▶ " : "◇ ") + Bindings.Fill(e.Text) + (pay == "" ? "" : $" (награда: {pay})");
            // a new errand in the place of one done fades in
            if (line.Text != text && line.Text != "" && line.IsVisibleInTree()) Juice.FadeIn(line, .45);
            line.Text = text;
            line.AddThemeFontOverride("font", e.Main ? Ornament.SerifBold : Ornament.Serif);
            line.AddThemeFontSizeOverride("font_size", e.Main ? 14 : 13);
            line.AddThemeColorOverride("font_color", e.Main ? Ornament.EmberGlow : Ornament.Parchment);
            line.Visible = true;
        }
        for (; i < _errandLines.Count; i++) _errandLines[i].Visible = false;
        return list.Count;
    }

    void RefreshGoals()
    {
        int i = 0, open = RefreshErrands();
        foreach (var (text, done) in Goals())
        {
            if (i >= _goalLines.Count)
            {
                var l = Text("", Ornament.Serif, 13, Ornament.Parchment, wrap: true);
                _goals.AddChild(l);
                _goalLines.Add(l);
            }
            var line = _goalLines[i++];
            line.Text = (done ? "◆ " : "◇ ") + Bindings.Fill(text);
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
        // the lore's tab tells what can be learnt, or else what the mysteries wait on
        string lb = _lore.Badge();
        if (lb == "" && !Hidden(Part.Magic)) lb = _mysteries.Badge();
        if (_loreTab.Badge != lb) { _loreTab.Badge = lb; _loreTab.QueueRedraw(); }
        _lore.Refresh();
        _mysteries.Refresh();
    }

    /// <summary>
    /// The tabs give up their names, the least looked at first, when the beam has no room for them and the stores before
    /// the clock; they take them back when there is room again.
    /// </summary>
    void Squeeze(float clockX)
    {
        float room = clockX - 28 - Stores.MinW - 14 - (_tabs.Position.X + _tabs.GetCombinedMinimumSize().X);
        if (room < 0)
        {
            foreach (var t in _squeeze) if (!t.Compact) { t.Compact = true; _tabs.ResetSize(); return; }
        }
        else
            for (int i = _squeeze.Count - 1; i >= 0; i--)
                if (_squeeze[i].Compact)
                {
                    if (room > _squeeze[i].Saved + 8) { _squeeze[i].Compact = false; _tabs.ResetSize(); }
                    return;
                }
    }

    /// <summary>The tabs in the order they give up their names when the beam is crowded.</summary>
    readonly List<TabPlaque> _squeeze = new();

    /// <summary>What the tag says over a mine, a ruin or a place of power.</summary>
    (string head, string sub, Color color) PlaceTag(Landmark l)
    {
        if (l.Kind == LandmarkKind.Ruin) return (l.Name, "старая кладка: починить ({ToolRepair}) или разобрать на камень ({ToolGather}, {MatStone})", Ornament.Aged);
        var m = _m.Sim.Mines.Find(x => x.Place == l);
        if (m == null) return (l.Name, "", Ornament.Aged);
        if (m.Power)
        {
            const string why = "святилище здесь даёт втрое больше вмешательства ({ToolGather} → «святилище»)";
            if (m.Ours > 0 && m.Theirs > 0) return ($"{m.Name} · место силы · за него бьются", why, Ornament.EmberGlow);
            return m.Holder switch
            {
                Holder.Ours => ($"{m.Name} · место силы · ваше", why + "; орда придёт его осквернить, держите здесь людей", Ornament.Moss),
                Holder.Horde => ($"{m.Name} · место силы · осквернено", "перебейте орду и займите его: пока оно у орды, святилище здесь не работает", Ornament.Blood),
                _ => ($"{m.Name} · место силы · ничьё", "пошлите сюда людей, займут за несколько секунд; " + why, Ornament.EmberGlow),
            };
        }
        string pay = $"+{m.Income} зол./мин";
        if (m.Ours > 0 && m.Theirs > 0) return ($"{m.Name} · за него бьются", pay, Ornament.EmberGlow);
        return m.Holder switch
        {
            Holder.Ours => ($"{m.Name} · ваш · {pay}", "держите людей у входа: орда придёт его отбивать", Ornament.Moss),
            Holder.Horde => ($"{m.Name} · у орды", $"перебейте орду у входа и займите его · {pay}", Ornament.Blood),
            _ => ($"{m.Name} · ничей", $"пошлите людей к входу, займут за несколько секунд · {pay}", Ornament.EmberGlow),
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
        if (_toolSlots.TryGetValue(t.Tool, out var taken)) taken.Fresh = false;
        foreach (var (tool, _, _, tip) in ToolDefs) if (tool == t.Tool) Meet("tool:" + tool, tip);
    }

    /// <summary>A tool or a tab met: the first time, outside the tutorial, its card; in the tutorial it is only counted as met.</summary>
    void Meet(string key, string tip)
    {
        bool tutorial = _m.Tutor != null && !_m.Tutor.Over;
        if (!tutorial && !Settings.Advice) return;
        if (Settings.Meet(key) && !tutorial) _first.Show(tip);
    }

    public void OnSelectionChanged() => _inspector?.Refresh();

    /// <summary>A key was put on another command: the letters on the tools and what the console says follow it.</summary>
    void Rebound()
    {
        foreach (var (tool, _, act, _) in ToolDefs) _toolSlots[tool].Key = Bindings.Label(act);
        foreach (var (mat, act, _, _) in MatDefs) _matSlots[mat].Key = Bindings.Label(act);
        _pickSlot.Key = Bindings.Label(Act.Pick);
        _inspector.Refresh(rebuild: true);
    }

    /// <summary>The blueprints changed: the bottom panel shows them again if it shows them now.</summary>
    public void RebuildBlueprints()
    {
        if (_inspector != null && _m.Tools.Tool is Tool.Capture or Tool.Paste) _inspector.Refresh(rebuild: true);
    }

    // ---------------------------------------------------------------- messages

    /// <summary>A line for the chronicle; one that happened at a place (voxels of the valley) takes the camera there when clicked.</summary>
    /// <summary>The chronicle, oldest first, as it was logged: what a save keeps of it.</summary>
    public readonly List<(string text, Kind kind, float x, float z)> Lines = new();

    public void Log(string text, Kind kind = Kind.Plain, float x = float.NaN, float z = float.NaN, bool toast = true)
    {
        if (_toasts == null) return;
        text = Bindings.Fill(text);
        Diag.Note(text);
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
            l.TooltipText = "Показать место";
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
        // the oldest lines go at once: one only queued for freeing still counts among the children till the frame's
        // end, and a loop waiting for the count to drop waited for ever (the game hung at the 81st line)
        while (_history.GetChildCount() > 80)
        {
            var old = _history.GetChild(_history.GetChildCount() - 1);
            _history.RemoveChild(old);
            old.QueueFree();
        }
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
    /// <summary>The panel let down, by its key: "magic" for the lore on its page of the mysteries.</summary>
    public string OpenTab => _drop?.OpenKey == "lore" && _lore.Page == 3 ? "magic" : _drop?.OpenKey;
    public Unit Inspecting => _inspector?.One;
    /// <summary>The console's middle (the self-test presses its tiles); whether it shows the citadel's muster.</summary>
    public Inspector Inspector => _inspector;
    public bool KeepShown => _inspector?.KeyNow == "keep";
    /// <summary>Lets a panel down; "lore:2" lets down the lore on its page of the intervention.</summary>
    public void OpenPanel(string key)
    {
        if (key.StartsWith("lore:") && int.TryParse(key[5..], out int page)) { _drop.Open("lore"); _lore.ShowPage(page); return; }
        if (key == "magic") { _drop.Open("lore"); _lore.ShowPage(3); return; }
        _drop.Open(key);
    }
    public void ClosePanel() => _drop.Close();
    public LorePanel Lore => _lore;
    public MysteryPanel Mysteries => _mysteries;
    public bool MenuOpen => _menu?.IsOpen == true;
    /// <summary>The page of the menu open now ("main", "save", "load", "settings", "controls"), or null with it shut.</summary>
    public string MenuPage => MenuOpen ? _menu.PageName : null;

    /// <summary>Something of the tutorial's laid into the frame: over everything but the menu.</summary>
    public void Mount(Control c)
    {
        _root.AddChild(c);
        _root.MoveChild(c, _menu.GetIndex());
    }

    /// <summary>The title over the valley at the start of the game: the frame put away, the menu's first page the title's.</summary>
    public bool TitleOpen { get; private set; }

    public void ShowTitle()
    {
        TitleOpen = true;
        Bare(true);
        _menu.OpenTitle();
    }

    /// <summary>The title gone: the frame back, as much of it as is let out.</summary>
    public void EndTitle()
    {
        TitleOpen = false;
        Bare(false);
    }

    void Bare(bool on)
    {
        foreach (CanvasItem c in new CanvasItem[] { _overlay, _console, _drop, _beam, _help, _menuButton, _tag, _toasts }) c.Visible = !on;
        _clockPanel.Visible = !on && !_hidden.Contains(Part.Clock);
        _colonists.Visible = !on && !_hidden.Contains(Part.Faces);
        _envoy.Visible = !on;
    }
    public void OpenMenu() => _menu?.Open();
    public void OpenMenu(string page) => _menu?.Open(page);
    public void CloseMenu() => _menu?.Close();
    /// <summary>Esc with the menu open: back a page, or out of it.</summary>
    public void MenuBack() => _menu?.Back();
    public ColonistBar Colonists => _colonists;
    /// <summary>How many lines the chronicle's tab holds.</summary>
    public int HistoryCount => _history.GetChildCount();
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
