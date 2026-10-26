using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The middle of the bottom console, saying what is in hand or who is chosen: a brush shows its settings; one pawn its
/// name, health, its wounds part by part of the body, what it is doing and the work it is set to; several their faces;
/// nobody chosen, the household by kind and the squads; the citadel clicked, its muster — the places, the line, and the
/// kinds to take on. Orders for the chosen run along its foot.
/// </summary>
public partial class Inspector : Control
{
    Main _m;
    string _key = "";
    Control _pane;
    readonly List<Action> _refresh = new();

    /// <summary>The one pawn shown in full, if one is.</summary>
    public Unit One { get; private set; }
    /// <summary>What it shows now: "home", "keep", "tool", "one:…" and so on.</summary>
    public string KeyNow => _key;

    public void Init(Main m)
    {
        _m = m;
        MouseFilter = MouseFilterEnum.Stop;
        Refresh(true);
    }

    public override void _Draw() => Craft.Well(this, new Rect2(Vector2.Zero, Size));

    /// <summary>Shows what should be shown now, made anew if that changed (or <paramref name="rebuild"/>); else brings it up to date.</summary>
    public void Refresh(bool rebuild = false)
    {
        if (_m == null) return;
        string key = Key();
        if (rebuild || key != _key) { _key = key; Build(); }
        foreach (var r in _refresh) r();
    }

    string Key()
    {
        if (_m.Tools.Tool == Tool.Spell) return "spell:" + (int)_m.Tools.CurrentSpell;
        if (_m.Tools.Tool != Tool.None) return "tool";
        var sel = Chosen();
        if (sel.Count == 0 && _m.KeepChosen) return "keep";
        if (sel.Count == 0 && _m.Inspected is { } look) return "look:" + look.Id;
        // the household's pane is made anew when a kind comes into it or goes out of it
        if (sel.Count == 0)
        {
            var kinds = new System.Text.StringBuilder("home:");
            foreach (var t in Kinds.PlayerKinds) kinds.Append(_m.Sim.Count(t) > 0 ? '1' : '0');
            return kinds.ToString();
        }
        return (sel.Count == 1 ? "one:" : "many:") + string.Join(",", sel.ConvertAll(u => u.Id));
    }

    List<Unit> Chosen()
    {
        var list = new List<Unit>();
        foreach (var u in _m.Selected) if (!u.Dead) list.Add(u);
        list.Sort((a, b) => a.T == b.T ? a.Id.CompareTo(b.Id) : Array.IndexOf(Kinds.PlayerKinds, a.T).CompareTo(Array.IndexOf(Kinds.PlayerKinds, b.T)));
        return list;
    }

    void Build()
    {
        _pane?.QueueFree();
        _refresh.Clear();
        One = null;
        var box = new MarginContainer { MouseFilter = MouseFilterEnum.Pass };
        box.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var (side, px) in new[] { ("margin_left", 14), ("margin_right", 12), ("margin_top", 8), ("margin_bottom", 8) }) box.AddThemeConstantOverride(side, px);
        var sel = Chosen();
        box.AddChild(_key == "tool" ? ToolPane() : _key == "keep" ? KeepPane() : _key.StartsWith("spell:") ? SpellPane() : _key.StartsWith("look:") && _m.Inspected != null ? LookPane(_m.Inspected)
            : _key.StartsWith("home") ? HomePane() : sel.Count == 1 ? OnePane(sel[0]) : GroupPane(sel));
        AddChild(box);
        _pane = box;
    }

    static Button Btn(string text, string tip, Action act, float w = 0)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(w, 24), TooltipText = Bindings.Fill(tip), MouseDefaultCursorShape = CursorShape.PointingHand };
        b.AddThemeFontSizeOverride("font_size", 12);
        b.Pressed += act;
        return b;
    }

    /// <summary>
    /// A square button with an icon of the set, its name and what it does in its tooltip; the icon in bronze, lighter
    /// under the pointer, ember while it is on.
    /// </summary>
    partial class Glyph : Button
    {
        readonly Icons.Painter _icon;

        public Glyph() { }

        public Glyph(Icons.Painter icon, string tip, Action act, bool toggle = false)
        {
            _icon = icon;
            TooltipText = Bindings.Fill(tip);
            FocusMode = FocusModeEnum.None;
            ToggleMode = toggle;
            CustomMinimumSize = new Vector2(32, 28);
            MouseDefaultCursorShape = CursorShape.PointingHand;
            Pressed += act;
        }

        public override void _Draw()
        {
            float s = Math.Min(Size.X, Size.Y) - 9;
            var ink = Disabled ? Ornament.Faded : ButtonPressed ? Ornament.EmberGlow : IsHovered() ? Ornament.BronzeLight : Ornament.Bronze;
            _icon?.Invoke(this, new Rect2((Size - new Vector2(s, s)) / 2, new Vector2(s, s)), ink);
        }

        public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
    }

    /// <summary>Nothing: a short stroke, for «bring in nothing».</summary>
    static void Nothing(CanvasItem c, Rect2 r, Color ink) => c.DrawLine(new Vector2(r.Position.X + r.Size.X * .25f, r.GetCenter().Y), new Vector2(r.End.X - r.Size.X * .25f, r.GetCenter().Y), ink, 2);

    /// <summary>The alarm's horn: lit while the alarm is up, the stand-down then in its tooltip.</summary>
    Button AlarmGlyph()
    {
        var b = new Glyph(Icons.Horn, Commander.AlarmTip, () => _m.Command.Alarm(), toggle: true);
        KeyTags.Tag(b, Act.Alarm);
        void Show()
        {
            bool up = _m.Sim.Alarm;
            b.SetPressedNoSignal(up);
            b.TooltipText = up ? Commander.StandDownTip : Commander.AlarmTip;
        }
        Show();
        _refresh.Add(Show);
        return b;
    }

    /// <summary>The alarm's button: "Тревога", or "Отбой" while it is up, glowing then.</summary>
    Button AlarmButton(float w)
    {
        var b = Btn("", "", () => _m.Command.Alarm(), w);
        KeyTags.Tag(b, Act.Alarm);
        void Show()
        {
            bool up = _m.Sim.Alarm;
            b.Text = up ? "Отбой" : "Тревога";
            b.TooltipText = up ? Commander.StandDownTip : Commander.AlarmTip;
            b.Modulate = up ? new Color(1.25f, .95f, .8f) : Colors.White;
        }
        Show();
        _refresh.Add(Show);
        return b;
    }

    // ---------------------------------------------------------------- a brush in hand

    Control ToolPane()
    {
        var t = _m.Tools; var o = t.Opt;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 14);
        head.AddChild(Hud.Text(t.Tool == Tool.Paste && t.PasteBp != null ? $"Чертёж «{t.PasteBp.Name}»" : BuildTools.ToolName(t.Tool), Ornament.Display, 22, Ornament.EmberGlow));
        var mat = Hud.CapsLabel((t.Tool == Tool.Gather ? (t.Shelter ? "убежище от орды" : t.Sanctum ? "святилище жрецов" : t.Dwelling ? "жилой дом" : "добывать: " + (t.GatherKind switch { Stockpile.Wood => "лес", Stockpile.Earth => "землю", Stockpile.Metal => "руду", _ => "камень" }))
            : t.Tool == Tool.Yard ? (o.YardPick < BuildTools.WayCount ? "дорожка: " : "вещь: ") + BuildTools.YardName(o.YardPick).ToLowerInvariant()
            : t.Tool == Tool.Template ? (t.DecorTab ? "вещь: " + Yard.Name(Yard.All[Math.Clamp(o.TemplateDecor, 0, Yard.All.Length - 1)]).ToLowerInvariant() : "шаблон: " + t.TemplatePick.Name.ToLowerInvariant())
            : t.Tool == Tool.Wand && o.WandCapture ? "снять чертёж" : o.Dig ? "кирка: копать и рубить" : (t.Tool == Tool.Wand ? "переложить в: " : "материал: ") + Mat.NameOf(o.Material).ToLowerInvariant()), 9);
        mat.VerticalAlignment = VerticalAlignment.Center;
        head.AddChild(mat);
        var hint = Hud.Text(Hud.ToolHint(t.Tool), Ornament.SerifItalic, 12, Ornament.Aged);
        hint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        head.AddChild(hint);
        var undo = Btn("Отменить · Ctrl+Z", Hud.UndoTip, () => _m.Tools.Undo());
        KeyTags.Tag(undo, () => "Ctrl+Z");
        undo.Disabled = _m.Sim.UndoDepth == 0;
        _refresh.Add(() => undo.Disabled = _m.Sim.UndoDepth == 0);
        head.AddChild(undo);
        col.AddChild(head);
        var opts = new HBoxContainer();
        opts.AddThemeConstantOverride("separation", 16);
        Options(opts);
        col.AddChild(opts);
        // what the brushes that lay are laying: the three of the stores and pitch, or a trap
        if (!o.Dig && t.Tool is Tool.Brush or Tool.Line or Tool.Box or Tool.Circle or Tool.Extrude)
        {
            var mats = new HBoxContainer();
            mats.AddThemeConstantOverride("separation", 18);
            mats.AddChild(new Choice("Материал", MaterialTip, new[] { "камень", "брёвна", "земля", "смола" }, Array.IndexOf(Laid, o.Material), i => t.SetMaterial(Laid[i])));
            mats.AddChild(new Choice("Ловушка", TrapTip, new[] { "колья", "настил", "камни", "брёвна", "порох" }, Array.IndexOf(Traps, o.Material), i => t.SetMaterial(Traps[i])));
            col.AddChild(mats);
        }
        // what a gate's leaves are of
        if (t.Tool == Tool.Gate)
            col.AddChild(new Choice("Створки", "Створки\nДубовые берутся из запаса брёвен и горят. Железные берутся из запаса металла, не горят, " +
                "и орда рубит их втрое дольше. Клавиши {MatWood} и {MatMetal}.",
                new[] { "дуб", "железо" }, t.IronGate ? 1 : 0, i => t.SetMaterial(i == 1 ? Mat.Plate : Mat.Wood)));
        // what the wand lays over again with
        if (t.Tool == Tool.Wand && !o.Dig && !o.WandCapture)
            col.AddChild(new Choice("Переложить в", "Переложить в\nРабочие разберут выделенное и сложат заново из этого материала. Разобранное вернётся в запас. Клавиши {MatStone}, {MatWood}, {MatEarth} и {MatMetal}.",
                new[] { "камень", "брёвна", "земля", "металл" }, Array.IndexOf(Relaid, o.Material), i => t.SetMaterial(Relaid[i])));
        // the settlement's palette: the ways, then the things
        if (t.Tool == Tool.Yard) col.AddChild(YardPalette());
        // the library: the ready templates, the bridge, the settlement's rank, and the blueprints taken
        if (t.Tool == Tool.Template) col.AddChild(TemplateLibrary());
        // the blueprints taken so far, with the tool that takes them and with a copy in hand
        if (t.Tool is Tool.Capture or Tool.Paste || t.Tool == Tool.Wand && o.WandCapture) col.AddChild(Shelf());
        if (opts.GetChildCount() == 0) col.AddChild(Hud.Text(Bindings.Fill("Esc — убрать кисть. {Help} — все клавиши."), Ornament.SerifItalic, 12, Ornament.Faded));
        return col;
    }

    /// <summary>The «Обустройство» palette: a row of the kinds of way and a row of the things, each with what it is in its tooltip.</summary>
    Control YardPalette()
    {
        var t = _m.Tools; var o = t.Opt;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        Choice Row(string caption, int from, int count)
        {
            var names = new string[count];
            for (int i = 0; i < count; i++) names[i] = BuildTools.YardName(from + i).ToLowerInvariant();
            var c = new Choice(caption, caption, names, o.YardPick - from, i => t.PickYard(from + i));
            int k = 0;
            foreach (var child in c.GetChildren())
                if (child is Button b) { b.TooltipText = Bindings.Fill(BuildTools.YardName(from + k) + "\n" + BuildTools.YardAbout(from + k)); k++; }
            return c;
        }
        var ways = Row("Дорожка", 0, BuildTools.WayCount);
        KeyTags.Tag(ways, () => Bindings.Label(Act.Form));
        box.AddChild(ways);
        box.AddChild(Row("Вещь", BuildTools.WayCount, BuildTools.YardCount - BuildTools.WayCount));
        return box;
    }

    /// <summary>The icon of each template in the library, by its key.</summary>
    static readonly Dictionary<string, string> TemplateIcons = new()
    {
        ["roundtower"] = "stone-tower", ["squaretower"] = "hill-fort", ["wall"] = "stone-wall", ["palisade"] = "stakes-fence",
        ["gatehouse"] = "gate", ["watchtower"] = "all-seeing-eye", ["barn"] = "medieval-barracks", ["stairs"] = "3d-stairs", ["floor"] = "wood-pile",
        ["house"] = "bunk-beds",
    };

    public const string TemplateTabTip = "Раздел библиотеки\nОборона: башни, стены, ворота, вышка. Постройки: жилой дом, амбар, настил. Декор: костёр, скамья, факел, колодец и другие вещи для поселения.";

    /// <summary>
    /// The library in one row: a medallion for each template, its name and what it is in its tooltip, those the
    /// settlement has not grown to yet faded; the bridge, which is the bridge tool's; then the blueprints taken.
    /// </summary>
    Control TemplateLibrary()
    {
        var t = _m.Tools; var o = t.Opt;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        // the tabs first: defence, buildings, the settlement's things
        row.AddChild(new Choice("Раздел", TemplateTabTip, Sim.TemplateTabs, o.TemplateTab, i => t.PickTemplateTab(i)));
        row.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0) });
        if (o.TemplateTab == 2)
        {
            // the things, as the «Обустройство» tool has them
            var names = new string[Yard.All.Length];
            for (int i = 0; i < names.Length; i++) names[i] = Yard.Name(Yard.All[i]).ToLowerInvariant();
            var things = new Choice("Вещь", "Вещь\nСтавится щелчком, {Rotate} поворачивает. Рабочие соберут её из запасов.", names, o.TemplateDecor, i => t.PickTemplateDecor(i));
            int n = 0;
            foreach (var child in things.GetChildren())
                if (child is Button b) { b.TooltipText = Bindings.Fill(Yard.Name(Yard.All[n]) + "\n" + Yard.About(Yard.All[n]) + " Ставится щелчком, {Rotate} поворачивает."); n++; }
            KeyTags.Tag(things, () => Bindings.Label(Act.Form));
            row.AddChild(things);
            return row;
        }
        var head = Hud.CapsLabel("шаблон", 9);
        head.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(head);
        bool first = true;
        for (int i = 0; i < Sim.Templates.Length; i++)
        {
            int k = i;
            var tp = Sim.Templates[i];
            if (tp.Tab != o.TemplateTab) continue;
            bool open = _m.Sim.TemplateOpen(tp);
            var g = new Glyph(Icons.Of(TemplateIcons.GetValueOrDefault(tp.Key, "stone-tower")),
                tp.Name + "\n" + tp.About + (open ? "" : $"\nОткроется на уровне «{Sim.StandingNames[tp.Opens]}»."), () => t.PickTemplate(k), toggle: true)
            { CustomMinimumSize = new Vector2(34, 30) };
            g.SetPressedNoSignal(o.TemplatePick == i);
            if (!open) g.Modulate = new Color(1, 1, 1, .4f);
            if (first) KeyTags.Tag(g, () => Bindings.Label(Act.Form));
            first = false;
            row.AddChild(g);
        }
        if (o.TemplateTab == 0)
            row.AddChild(new Glyph(Icons.BridgeIcon, "Мост ({ToolBridge})\nЩелчок возьмёт инструмент «Мост»: протяните его от берега до берега.", () => t.SetTool(Tool.Bridge)) { CustomMinimumSize = new Vector2(34, 30) });
        row.AddChild(new Control { CustomMinimumSize = new Vector2(14, 0) });
        row.AddChild(Shelf());
        return row;
    }

    public const string StandingTip = "Уровень поселения\nРастёт с тем, что построено: жилые дома с лежанками, здания города по их уровню, вещи «Обустройства» и вся кладка рабочих. " +
        "Каждый новый уровень даёт золото, некоторые открывают шаблоны.";

    /// <summary>The blueprints taken so far, a button each: a click takes up a copy to place, the cross throws the blueprint away.</summary>
    Control Shelf()
    {
        var t = _m.Tools;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var head = Hud.CapsLabel("чертежи", 9);
        head.VerticalAlignment = VerticalAlignment.Center;
        head.CustomMinimumSize = new Vector2(0, 26);
        row.AddChild(head);
        if (_m.Blueprints.Count == 0)
        {
            var none = Hud.Text(Bindings.Fill("Чертежей пока нет. Обведите постройку рамкой ({ToolCapture})."), Ornament.SerifItalic, 12, Ornament.Aged);
            none.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(none);
            return row;
        }
        var shelf = new HBoxContainer();
        shelf.AddThemeConstantOverride("separation", 4);
        foreach (var bp in _m.Blueprints)
        {
            var b = bp;
            var take = Btn(bp.Name, $"{bp.Name}\n{Len(bp.W)} × {Len(bp.D)} × {Len(bp.H)}, {bp.Describe()}. Щелчок берёт копию: поставьте её щелчком по земле, {{Rotate}} поворачивает.",
                () => t.StartPaste(b));
            take.ToggleMode = true;
            take.SetPressedNoSignal(t.PasteBp == b);
            shelf.AddChild(take);
            shelf.AddChild(Btn("×", "Удалить чертёж", () => _m.RemoveBlueprint(b)));
            shelf.AddChild(new Control { CustomMinimumSize = new Vector2(6, 0) });
        }
        var scroll = new ScrollContainer { VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 28) };
        scroll.AddChild(shelf);
        row.AddChild(scroll);
        return row;
    }

    static readonly byte[] Laid = { Mat.Stone, Mat.Wood, Mat.Rammed, Mat.Pitch };
    static readonly byte[] Relaid = { Mat.Stone, Mat.Wood, Mat.Rammed, Mat.Plate };
    static readonly byte[] Traps = { Mat.Stakes, Mat.TrapDeck, Mat.DropStone, Mat.DropLog, Mat.Powder };

    const string TrapTip = "Ловушки\nКолья: частокол заданной высоты. Кто идёт сквозь него, замедляется, ранится и понемногу его " +
        "ломает. Орда обходит колья, если обход короткий. Из брёвен, горят.\nНастил: тонкие жерди вровень с землёй " +
        "над ямой или рвом. Наши по нему ходят, под ордой он проваливается. Выройте под ним глубокую яму с кольями " +
        "на дне. Из брёвен.\nКамни: сложите на стене у края. Когда налётчик подойдёт к подножию, наш человек рядом " +
        "столкнёт их вниз. Камни катятся по склону и сбивают всех на пути.\nБрёвна: так же, вдоль края стены. " +
        "Скатываются на налётчиков у подножия. Горят.\nПорох: бочки с порохом, селитру делают из запаса земли. " +
        "Бочка взрывается от огня через полсекунды, соседние рвутся следом, по дорожке пороха огонь бежит дальше. " +
        "Подорвать можно огненной стрелой: выберите лучника, включите огненные стрелы и щёлкните по бочкам. Рабочие порох " +
        "не тушат.";

    const string MaterialTip = "Материал\nКамень ({MatStone}), брёвна ({MatWood}), земля ({MatEarth}) и железо ({MatMetal}) берутся из запасов. Железо прочнее всего и не горит. Смолу варят из " +
        "брёвен. Слой смолы на дне рва загорается целиком от огненной стрелы и горит долго и жарко. Рабочие смолу " +
        "не тушат, сгоревшую восстанавливает починка ({ToolRepair}).";

    void Options(HBoxContainer opts)
    {
        var t = _m.Tools; var o = t.Opt;
        bool dig = o.Dig;
        const string widthTip = "Толщина\nТолщина линии и стен, ширина рва, размер кисти. Клавиши {WidthLess} и {WidthMore}.";
        const string heightTip = "Высота\nВысота кладки. Клавиши {HeightLess} и {HeightMore}.";
        const string depthTip = "Глубина\nГлубина от поверхности. Клавиши {HeightLess} и {HeightMore}.";
        const string levelTip = "Верх\nРовный — на одной высоте с началом штриха, по рельефу — повторяет землю. Клавиша {Level}.";
        const string floorTip = "Дно\nРовное — на одной глубине с началом штриха, по рельефу — повторяет поверхность. Клавиша {Level}.";
        // (each with its keys, shown on it while Alt is held)
        static Control Keyed(Control c, Func<string> keys) { KeyTags.Tag(c, keys); return c; }
        static string Pair(Act a, Act b) => $"{Bindings.Label(a)} {Bindings.Label(b)}";
        Control Width(string caption = "Толщина") => Keyed(new Stepper(caption, widthTip, () => Len(o.Width), d => t.StepWidth(d)), () => Pair(Act.WidthLess, Act.WidthMore));
        Control Height() => Keyed(dig ? new Stepper("Глубина", depthTip, () => Len(o.Depth), d => t.StepHeight(d)) : new Stepper("Высота", heightTip, () => Len(o.Height), d => t.StepHeight(d)),
            () => Pair(Act.HeightLess, Act.HeightMore));
        Control Level() => Keyed(dig
            ? new Choice("Дно", floorTip, new[] { "ровное", "по рельефу" }, o.Level ? 0 : 1, i => { if ((i == 0) != o.Level) t.ToggleLevel(); })
            : new Choice("Верх", levelTip, new[] { "ровный", "по рельефу" }, o.Level ? 0 : 1, i => { if ((i == 0) != o.Level) t.ToggleLevel(); }), () => Bindings.Label(Act.Level));
        switch (t.Tool)
        {
            case Tool.Brush:
                opts.AddChild(Keyed(new Choice("Форма", "Форма кисти\nСтолб рисует стену заданной высоты, шар налипает на поверхность. С киркой столб копает полосу, шар вынимает ком. Клавиша {Form}.",
                    new[] { "столб", "шар" }, (int)o.Form, i => { o.Form = (BrushForm)i; t.Changed(); }), () => Bindings.Label(Act.Form)));
                opts.AddChild(Width("Размер"));
                if (o.Form == BrushForm.Column) { opts.AddChild(Height()); opts.AddChild(Level()); }
                break;
            case Tool.Line:
                if (dig)
                    opts.AddChild(Keyed(new Choice("Вид", "Вид\nРов: канава заданной ширины и глубины. Подкоп: ход под землёй от начала протяжки вниз и вдаль, " +
                        "метр шириной и два высотой, на заданной глубине. Ведите его навстречу подкопу орды: прорвавшись, рабочие выбьют её крепь, и ход обвалится. Клавиша {Form}.",
                        new[] { "ров", "подкоп" }, o.Mine ? 1 : 0, i => { o.Mine = i == 1; t.Changed(); }), () => Bindings.Label(Act.Form)));
                if (!(dig && o.Mine)) opts.AddChild(Width(dig ? "Ширина" : "Толщина"));
                opts.AddChild(Height());
                if (!(dig && o.Mine)) opts.AddChild(Level());
                break;
            case Tool.Box:
            case Tool.Circle:
                if (t.Tool == Tool.Box)
                    opts.AddChild(new Choice("Заливка", "Заливка\nСплошная фигура, только стенки заданной толщины или дом: стены, пол, двускатная крыша и ворота. " +
                        "Дом сам становится жилым: рабочие поставят лежанки, каждая даёт место в войске. Дом поверх дома — второй этаж. Клавиша {Form}.",
                        new[] { "сплошной", "стенки", "дом" }, o.House ? 2 : o.Hollow ? 1 : 0, i =>
                        {
                            o.Hollow = i >= 1; o.House = i == 2;
                            t.Changed();
                        }));
                else
                    opts.AddChild(new Choice("Заливка", "Заливка\nСплошная фигура или только стенки заданной толщины. Клавиша {Form}.",
                        new[] { "сплошной", "кольцо" }, o.Hollow ? 1 : 0, i => { if ((i == 1) != o.Hollow) t.ToggleForm(); }));
                if (o.Hollow) opts.AddChild(Width());
                opts.AddChild(Height()); opts.AddChild(Level());
                break;
            case Tool.Bridge:
                opts.AddChild(new Choice("Вид", "Вид моста\nАрочный: каменный или деревянный, пролёт до 25 м. Подъёмный: настил над рвом от 2 до 6 м. Когда подходит орда, " +
                    "он поднимается сам и сбрасывает в ров тех, кто на нём. Щелчок по подъёмному мосту без кисти: сам, опущен или поднят.",
                    new[] { "арочный", "подъёмный" }, o.DrawBridge ? 1 : 0, i => { o.DrawBridge = i == 1; t.Changed(); }));
                opts.AddChild(new Stepper("Проход", "Ширина прохода\nНе меньше 1,25 м, парапеты или перила добавляются к ней. Клавиши {WidthLess} и {WidthMore}.",
                    () => Len(Math.Max(Bridges.MinWidth, o.Width)), d => t.StepWidth(d)));
                opts.AddChild(Hud.CapsLabel(o.DrawBridge || o.Material == Mat.Wood ? "деревянный" : "каменный", 11, Ornament.BronzeLight));
                break;
            case Tool.Extrude:
                opts.AddChild(new Stepper("Шаг", "Шаг\nНа сколько грань нарастает или углубляется за щелчок. Клавиши {HeightLess} и {HeightMore}.", () => Len(o.Step), d => t.StepHeight(d)));
                break;
            case Tool.Gather:
                opts.AddChild(new Choice("Участок", "Участок\nКамень ({MatStone}), лес ({MatWood}), земля ({MatEarth}): здесь рабочие добывают ресурс. " +
                    "Руда ({MatMetal}): рудник, рабочие роют до железной руды и берут её в запас металла.\n" +
                    "Убежище: сюда по тревоге ({Alarm}) уходят рабочие. Лучше под крышей и за стенами.\n" +
                    "Святилище: здесь днём молятся жрецы. На месте силы, пока оно ваше, молитва даёт втрое больше.\n" +
                    "Жилой дом: обведите закрытую комнату со стенами, крышей и воротами. Рабочие поставят лежанки, каждая даёт место в войске. " +
                    "Построить новый дом целиком: прямоугольник ({ToolBox}) с заливкой «дом».",
                    new[] { "камень", "лес", "земля", "руда", "убежище", "святилище", "жилой дом" },
                    t.GatherKind switch { Stockpile.Wood => 1, Stockpile.Earth => 2, Stockpile.Metal => 3, Zone.Shelter => 4, Zone.Sanctum => 5, Zone.Dwelling => 6, _ => 0 },
                    i => { if (i == 4) t.SetShelter(true); else if (i == 5) t.SetSanctum(true); else if (i == 6) t.SetDwelling(true); else t.SetMaterial(new[] { Mat.Stone, Mat.Wood, Mat.Rammed, Mat.Plate }[i]); }));
                if (t.GatherKind is Stockpile.Stone or Stockpile.Earth or Stockpile.Metal)
                    opts.AddChild(new Stepper("Глубина", "Глубина\nДо какой глубины брать камень, землю или руду. Клавиши {HeightLess} и {HeightMore}.", () => Len(o.Depth), d => t.StepHeight(d)));
                break;
            case Tool.Wand:
                opts.AddChild(Keyed(new Choice("Щелчок", "Что делает щелчок\nСнести: рабочие разберут выделенное, материал вернётся в запас. Кирка ({Pick}).\n" +
                    "Переложить: разберут и сложат заново из камня, брёвен, земли или металла ({MatStone}, {MatWood}, {MatEarth}, {MatMetal}).\nЧертёж: выделенное сохранится как чертёж, его копии ставят инструментом «Снять чертёж» ({ToolCapture}).",
                    new[] { "снести", "переложить", "чертёж" }, o.WandCapture ? 2 : dig ? 0 : 1, i =>
                    {
                        if (i == 0) t.SetDig(true);
                        else if (i == 1) t.SetMaterial(Array.IndexOf(Relaid, o.Material) >= 0 ? o.Material : Mat.Stone);
                        else { o.Dig = false; o.WandCapture = true; t.Changed(); }
                    }), () => Bindings.Label(Act.Pick)));
                opts.AddChild(Keyed(new Choice("Материал", "Что считать тем же материалом\nЛюбой оттенок: камень любого цвета вместе с раствором, любые стволы, дёрн с землёй долины. " +
                    "Точно: только воксели того же цвета, что под курсором. Клавиша {Form}.",
                    new[] { "любой оттенок", "точно" }, o.WandExact ? 1 : 0, i => { o.WandExact = i == 1; t.Changed(); }), () => Bindings.Label(Act.Form)));
                opts.AddChild(new Choice("Соседи", "Соседи\nПо граням: только то, что касается гранью. С углами: и то, что касается ребром или углом, например стена под 45°.",
                    new[] { "по граням", "с углами" }, o.WandCorners ? 1 : 0, i => { o.WandCorners = i == 1; t.Changed(); }));
                opts.AddChild(Hud.CapsLabel(t.WandHeld > 0 ? $"выделено: {t.WandHeld} · Esc сбросит" : "Shift: добавить", 10, Ornament.Aged));
                break;
            case Tool.Yard when o.YardPick < BuildTools.WayCount:
                opts.AddChild(Width("Ширина"));
                break;
            case Tool.Yard:
                opts.AddChild(Hud.CapsLabel($"поворот {o.YardRot * 90}° · {Bindings.Label(Act.Rotate)} — повернуть", 10, Ornament.Aged));
                break;
            case Tool.Template when t.DecorTab:
                opts.AddChild(Hud.CapsLabel($"поворот {o.TemplateRot * 90}° · {Bindings.Label(Act.Rotate)} — повернуть", 10, Ornament.Aged));
                break;
            case Tool.Template:
            {
                var tp = t.TemplatePick;
                // a house: its length by the height keys, its depth by the width keys, the side its door looks out of turned
                if (tp.Key == "house")
                {
                    opts.AddChild(Keyed(new Stepper("Длина", "Длина\nДлина дома. Клавиши {HeightLess} и {HeightMore}.", () => Len(t.TemplateSize(tp)), d => t.StepHeight(d)), () => Pair(Act.HeightLess, Act.HeightMore)));
                    opts.AddChild(Keyed(new Stepper("Глубина", "Глубина\nГлубина дома. Клавиши {WidthLess} и {WidthMore}.", () => Len(t.HouseDepth), d => t.StepHouseDepth(d)), () => Pair(Act.WidthLess, Act.WidthMore)));
                }
                else if (tp.SizeMax > 0) opts.AddChild(Keyed(new Stepper("Высота", "Высота\nВысота шаблона. Клавиши {HeightLess} и {HeightMore}.", () => Len(t.TemplateSize(tp)), d => t.StepHeight(d)), () => Pair(Act.HeightLess, Act.HeightMore)));
                opts.AddChild(Hud.CapsLabel(tp.Key == "house" ? $"дверь {Shapes.SideWords[Sim.SideOfTurn(o.TemplateRot)]} · {Bindings.Label(Act.Rotate)} — другая сторона" : tp.Line ? "протяните от конца до конца" : $"поворот {o.TemplateRot * 90}° · {Bindings.Label(Act.Rotate)} — повернуть · Shift и протяжка — ряд", 10, Ornament.Aged));
                var rank = Hud.CapsLabel("поселение: " + _m.Sim.StandingLine().ToLowerInvariant(), 10, Ornament.BronzeLight);
                rank.TooltipText = StandingTip;
                rank.MouseFilter = MouseFilterEnum.Pass;
                opts.AddChild(rank);
                break;
            }
            case Tool.Paste when t.PasteBp != null:
                opts.AddChild(Hud.CapsLabel($"поворот {t.PasteRot * 90}° · {Bindings.Label(Act.Rotate)} — повернуть · {t.PasteBp.Describe()}", 10, Ornament.Aged));
                break;
        }
        if (t.Tool is Tool.Line or Tool.Box or Tool.Circle or Tool.Paste or Tool.Gather or Tool.Repair or Tool.Capture or Tool.Unmark or Tool.Bridge || t.Tool == Tool.Yard && o.YardPick < BuildTools.WayCount)
            opts.AddChild(Keyed(new Choice("Сетка", "Сетка\nКонцы штрихов встают в узлы сетки, линии идут вдоль осей или под 45°. Сетка отсчитывается от центра цитадели. Клавиша {Grid}.",
                new[] { "нет", "50 см", "1 м", "2 м" }, Array.IndexOf(ToolOptions.Grids, o.Grid), i => t.SetGrid(ToolOptions.Grids[i])), () => Bindings.Label(Act.Grid)));
    }

    public static string Len(int voxels)
    {
        double m = voxels / (double)VoxelWorld.PerMetre;
        return m < 1 ? $"{Math.Round(m * 100)} см" : (Fmt.One(m) + " м").Replace(",0 м", " м");
    }

    // ---------------------------------------------------------------- a spell in hand

    Control SpellPane()
    {
        var sp = Sim.SpellOf(_m.Tools.CurrentSpell);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 14);
        head.AddChild(Hud.Text(sp.Name, Ornament.Display, 22, Ornament.EmberGlow));
        var circle = Hud.CapsLabel(Sim.Circles[sp.Circle], 9);
        circle.VerticalAlignment = VerticalAlignment.Center;
        head.AddChild(circle);
        col.AddChild(head);
        var about = Hud.Text(sp.About, Ornament.Serif, 13, Ornament.Parchment, wrap: true);
        about.CustomMinimumSize = new Vector2(640, 0);
        col.AddChild(about);
        if (sp.Kind == SpellKind.Weather)
        {
            // which weather: rain, fog or wind
            var pick = new HBoxContainer();
            pick.AddThemeConstantOverride("separation", 6);
            var buttons = new List<Button>();
            for (int i = 0; i < Sim.Summonable.Length; i++)
            {
                int n = i;
                var w = Sim.Summonable[i];
                var b = Btn(char.ToUpper(Sim.WeatherName(w)[0]) + Sim.WeatherName(w)[1..], w switch
                {
                    Weather.Rain => "Дождь: пожары гаснут, смола не горит, огненный дождь слабее вдвое, молния сильнее в 1,5 раза, стрелы летят хуже.",
                    Weather.Fog => "Туман: все лучники видят врага только вблизи.",
                    _ => "Ветер: огонь бежит вдвое быстрее, стрелы сносит.",
                }, () => { _m.Tools.WeatherPick = n; }, 110);
                b.ToggleMode = true;
                buttons.Add(b);
                pick.AddChild(b);
            }
            col.AddChild(pick);
            _refresh.Add(() => { for (int i = 0; i < buttons.Count; i++) buttons[i].SetPressedNoSignal(_m.Tools.WeatherPick == i); });
        }
        var state = Hud.Text("", Ornament.SerifItalic, 13, Ornament.BronzeLight);
        col.AddChild(state);
        _refresh.Add(() =>
        {
            double ready = _m.Sim.ReadyIn(sp.Kind), have = _m.Sim.Favour;
            string how = sp.Kind switch
            {
                _ when sp.Stroke => "проведите линию по земле",
                SpellKind.Weather => "выберите погоду и щёлкните по полю",
                SpellKind.Telekinesis => "зажмите кнопку на ком-то или на камне и ведите; отпустите — бросок",
                SpellKind.Skin => "щёлкните по нашей постройке",
                SpellKind.Recall => "щёлкните рядом с нашим",
                SpellKind.Fall or SpellKind.Explosion => "щёлкните по стене, скале, земле или постройке",
                _ when sp.Radius <= 0 => "щёлкните по полю где угодно",
                _ => "щёлкните по полю",
            };
            state.Text = $"{_m.Sim.CostOf(sp):0} вмешательства (есть {have:0})" + (ready > 0 ? $" · будет готово через {ready:0} с" : " · готово") + $" · {how} · Esc — убрать";
            state.AddThemeColorOverride("font_color", ready > 0 || have < _m.Sim.CostOf(sp) ? Ornament.Blood.Lightened(.3f) : Ornament.BronzeLight);
        });
        return col;
    }

    // ---------------------------------------------------------------- one pawn

    Control OnePane(Unit u)
    {
        One = u;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 16);
        var who = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        who.AddThemeConstantOverride("separation", 0);
        var name = Hud.Text($"{u.Name} №{u.Id}", Ornament.SerifBold, 16, Ornament.Parchment);
        who.AddChild(name);
        var bar = new HealthBar { Unit = u, CustomMinimumSize = new Vector2(260, 14) };
        who.AddChild(bar);
        var doing = Hud.Text("", Ornament.SerifItalic, 13, Ornament.BronzeLight);
        doing.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        doing.CustomMinimumSize = new Vector2(330, 0);
        who.AddChild(doing);
        var hurt = Hud.Text("", Ornament.Serif, 12, Ornament.Blood);
        hurt.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        hurt.CustomMinimumSize = new Vector2(330, 0);
        who.AddChild(hurt);
        top.AddChild(who);
        var chart = new BodyChart { Unit = u, CustomMinimumSize = new Vector2(58, 74), TooltipText = "Раны\nЗелёное — цело, жёлтое — задето, оранжевое — изранено или кость сломана, красное — тяжело ранено, перечёркнутое — отрублено." };
        top.AddChild(chart);
        var work = new VBoxContainer();
        work.AddThemeConstantOverride("separation", 3);
        var cellList = new List<PriorityCell>();
        if (u.T.Prays)
        {
            // a priest does none of the pawns' work: what it does instead
            work.AddChild(Hud.CapsLabel("Жрец", 8));
            var about = Hud.Text("Днём молится в святилище и копит вмешательство, ночью уходит в цитадель. На месте силы молитва даёт втрое больше.",
                Ornament.Serif, 12, Ornament.Aged, wrap: true);
            about.CustomMinimumSize = new Vector2(300, 0);
            work.AddChild(about);
        }
        else
        {
            work.AddChild(Hud.CapsLabel("Работы · 1 — первым делом, пусто — никогда", 8));
            var cells = new HBoxContainer();
            cells.AddThemeConstantOverride("separation", 4);
            for (int k = 0; k < Work.Kinds; k++) { var c = new PriorityCell(_m, u, (WorkKind)k); cells.AddChild(c); cellList.Add(c); }
            work.AddChild(cells);
        }
        top.AddChild(work);
        col.AddChild(top);
        var orders = Orders(new List<Unit> { u });
        col.AddChild(orders);
        _refresh.Add(() =>
        {
            doing.Text = Retinue.DoingNow(u, _m.Sim);
            string h = Retinue.Hurt(u);
            hurt.Text = u.Dead ? "пал" : h != "" ? h : u.Hp < u.MaxHp - .5f ? "ушиблен" : "цел";
            hurt.AddThemeColorOverride("font_color", h != "" || u.Dead ? Ornament.Blood : Ornament.Moss);
            bar.QueueRedraw(); chart.QueueRedraw();
            foreach (var c in cellList) c.QueueRedraw();
        });
        return col;
    }

    /// <summary>The orders for the chosen: back to work, stay, the horn; and what to bring in and whether to stand and fight.</summary>
    HBoxContainer Orders(List<Unit> chosen)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        row.AddChild(new Glyph(Icons.Pick, "За работу\nВыбранные снова сами ищут работу.", () => _m.Command.ReleaseSelected()));
        row.AddChild(new Glyph(Icons.Of("knight-banner"), "Стоять здесь\nВыбранные остаются на месте. Для солдат это место становится постом.", () => _m.Command.HoldSelected()));
        row.AddChild(AlarmGlyph());
        if (chosen.Count == 1)
        {
            var possess = new Glyph(Icons.Eye, "Вселиться ({Possess})\nУправлять им от первого лица. {FpForward}{FpLeft}{FpBack}{FpRight} — идти, мышь — смотреть, {Lmb} — удар, " +
                "выстрел или кирка, {Rmb} — блок у бойца, кладка у рабочего, {FpSwitch} — в другого, Esc — выйти." +
                (chosen[0].T == Kinds.Captain ? " Командир: {FpStone}–{FpFourth} — призывы, {FpOrder} — приказ взглядом." : ""), () => _m.FP.Enter(chosen[0]));
            KeyTags.Tag(possess, Act.Possess);
            row.AddChild(possess);
        }
        var duty = new List<Button>();
        // priests bring nothing in
        bool labour = chosen.Exists(u => !u.T.Prays);
        if (labour)
        {
        row.AddChild(new Rule(true));
        var cap = Hud.CapsLabel("Добывать", 8);
        cap.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(cap);
        foreach (var (icon, kind, tip) in new (Icons.Painter, int, string)[]
        {
            (Nothing, Stockpile.None, "Ничего не добывать самим\nТолько размеченная работа и делянки."),
            (Icons.Of("stone-block"), Stockpile.Stone, "Добывать камень\nБез размеченной работы выбранные сами ломают ближайшие валуны и руины."),
            (Icons.Of("wood-pile"), Stockpile.Wood, "Добывать брёвна\nБез размеченной работы выбранные сами рубят ближайшие деревья."),
            (Icons.Of("dig-hole"), Stockpile.Earth, "Добывать землю\nБез размеченной работы выбранные копают общую яму в стороне от крепости."),
            (Icons.Of("iron-ingot"), Stockpile.Metal, "Добывать металл\nБез размеченной работы выбранные сами ищут ближайшую железную руду: рыжую скалу-выход или жилу у поверхности."),
        })
        {
            int k = kind;
            var b = new Glyph(icon, tip, () => _m.Command.DutySelected(k), toggle: true);
            duty.Add(b);
            row.AddChild(b);
        }
        }
        bool workers = chosen.Exists(u => u.T.Work >= 1 && !u.T.Prays), archers = chosen.Exists(u => u.T.Ranged), melee = chosen.Exists(Sim.TakesAlive);
        Button militia = null, fire = null, alive = null, commands = null;
        if (chosen.Exists(u => u.T == Kinds.Captain))
        {
            row.AddChild(new Rule(true));
            commands = new Glyph(Icons.Helm, "Командует сам\nКомандир строит бойцов в 40 м у стены, к которой идёт орда, ставит стрелков на стену, отводит раненых, " +
                "после боя зовёт рабочих чинить. {Rmb} по земле — место, которое он держит. Ещё раз — ждёт ваших приказов.", () => _m.Command.CommandsSelected(), toggle: true);
            row.AddChild(commands);
            if (chosen.Count == 1 && chosen[0].Hero is { } hero)
            {
                var one = chosen[0];
                row.AddChild(new Glyph(Icons.Of("black-knight-helm"), $"Карточка героя · уровень {hero.Level}\nОпыт, умения, отряд. На новом уровне здесь выбирают умение. То же — {{Rmb}} по его лицу в ряду наверху.",
                    () => _m.Hud.HeroPane.Show(one)));
            }
        }
        if (archers)
        {
            row.AddChild(new Rule(true));
            fire = new Glyph(Icons.Of("flaming-arrow"), "Огненные стрелы\nПоджигают лестницы и настилы орды, смолу и налётчиков. Лучники стреляют реже. " +
                "{Rmb} по дереву или смоле — поджечь. Ещё раз — обычные стрелы.",
                () => _m.Command.FireArrowsSelected(), toggle: true);
            row.AddChild(fire);
        }
        if (melee)
        {
            row.AddChild(new Rule(true));
            alive = new Glyph(Icons.Of("manacles"), "Брать живым\nМечники и ополчение связывают налётчика, когда у него остаётся 25 % здоровья (скованного Цепями — 60 %). " +
                "Свободный рабочий отведёт пленного в цитадель. Пленных приносят в жертву или отдают послу орды за выкуп. Ещё раз — снова бить насмерть.", () => _m.Command.TakeAliveSelected(), toggle: true);
            row.AddChild(alive);
        }
        if (workers)
        {
            row.AddChild(new Rule(true));
            militia = new Glyph(Icons.Of("war-pick"), "Ополчение\nРабочие не убегают от орды, а дерутся кирками. Ещё раз — снова убегают.", () => _m.Command.MilitiaSelected(), toggle: true);
            row.AddChild(militia);
        }
        _refresh.Add(() =>
        {
            int[] kinds = { Stockpile.None, Stockpile.Stone, Stockpile.Wood, Stockpile.Earth, Stockpile.Metal };
            for (int i = 0; i < duty.Count; i++) duty[i].SetPressedNoSignal(chosen.TrueForAll(u => u.T.Prays || u.Duty == kinds[i]));
            militia?.SetPressedNoSignal(chosen.TrueForAll(u => u.T.Work < 1 || u.T.Prays || u.Militia));
            fire?.SetPressedNoSignal(chosen.TrueForAll(u => !u.T.Ranged || u.FireArrows));
            alive?.SetPressedNoSignal(chosen.TrueForAll(u => !Sim.TakesAlive(u) || u.TakeAlive));
            commands?.SetPressedNoSignal(chosen.TrueForAll(u => u.T != Kinds.Captain || u.Commands));
        });
        return row;
    }

    // ---------------------------------------------------------------- a raider or a beast, looked at

    Control LookPane(Unit u)
    {
        One = u;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 16);
        var who = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        who.AddThemeConstantOverride("separation", 0);
        string band = u.Band != null ? " · " + u.Band.Name : "";
        who.AddChild(Hud.Text(u.Name + band, Ornament.SerifBold, 16, u.Team == Team.Horde ? Ornament.Blood.Lightened(.25f) : Ornament.Moss));
        var bar = new HealthBar { Unit = u, CustomMinimumSize = new Vector2(260, 14) };
        who.AddChild(bar);
        var doing = Hud.Text("", Ornament.SerifItalic, 13, Ornament.BronzeLight);
        doing.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        doing.CustomMinimumSize = new Vector2(380, 0);
        who.AddChild(doing);
        var hurt = Hud.Text("", Ornament.Serif, 12, Ornament.Blood);
        hurt.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        hurt.CustomMinimumSize = new Vector2(380, 0);
        who.AddChild(hurt);
        top.AddChild(who);
        var chart = new BodyChart { Unit = u, CustomMinimumSize = new Vector2(58, 74), TooltipText = "Раны\nЗелёное — цело, жёлтое — задето, оранжевое — изранено или кость сломана, красное — тяжело ранено, перечёркнутое — отрублено." };
        top.AddChild(chart);
        var about = new VBoxContainer();
        about.AddThemeConstantOverride("separation", 2);
        about.AddChild(Hud.CapsLabel(u.Team == Team.Horde ? "Налётчик" : "Зверь", 8));
        string note = u.Team == Team.Wild
            ? $"{u.T.Bounty} зол. за добычу. Убегает от людей. Выберите своих и щёлкните по нему {{Rmb}}."
            : (u.Leader ? $"Вожак: {u.T.Bounty * 3} зол. за голову. " : $"{u.T.Bounty} зол. за голову. ")
              + (u.Returned ? "Сбежал в прошлый раз и вернулся. " : "") + "Выберите своих и щёлкните по нему {Rmb}.";
        if (u.Bound)
            note = u.Inside ? "Пленный в цитадели. Его можно принести в жертву (вкладка «Таинства») или отдать послу орды за выкуп. Ночью без стражи он сбежит."
                : "Связан. Свободный рабочий отведёт его в цитадель. Если оставить надолго, развяжется и убежит.";
        var noteLabel = Hud.Text(Bindings.Fill(note), Ornament.Serif, 12, Ornament.Aged, wrap: true);
        noteLabel.CustomMinimumSize = new Vector2(330, 0);
        about.AddChild(noteLabel);
        top.AddChild(about);
        col.AddChild(top);
        _refresh.Add(() =>
        {
            doing.Text = Retinue.Doing(u, _m.Sim);
            string h = Retinue.Hurt(u);
            hurt.Text = u.Dead ? (u.Team == Team.Wild ? "добыт" : "пал") : h != "" ? h : u.Hp < u.MaxHp - .5f ? "ушиблен" : "цел";
            bar.QueueRedraw(); chart.QueueRedraw();
        });
        return col;
    }

    // ---------------------------------------------------------------- several

    Control GroupPane(List<Unit> chosen)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        var counts = new List<string>();
        foreach (var t in Kinds.PlayerKinds) { int n = chosen.FindAll(u => u.T == t).Count; if (n > 0) counts.Add(Retinue.Plural(t, n)); }
        col.AddChild(Hud.Text($"Выбрано {chosen.Count}: {string.Join(", ", counts)} · щелчок по портрету — выбрать одного", Ornament.SerifBold, 13, Ornament.BronzeLight));
        var faces = new HFlowContainer();
        faces.AddThemeConstantOverride("h_separation", 4);
        faces.AddThemeConstantOverride("v_separation", 3);
        foreach (var u in chosen.GetRange(0, Math.Min(chosen.Count, 24))) faces.AddChild(new MiniCard(_m, u) { CustomMinimumSize = new Vector2(34, 42) });
        col.AddChild(faces);
        col.AddChild(Orders(chosen));
        _refresh.Add(() => { foreach (var c in faces.GetChildren()) if (c is MiniCard m) m.QueueRedraw(); });
        return col;
    }

    // ---------------------------------------------------------------- the citadel

    /// <summary>The kinds' tiles in the citadel's muster, for the self-test.</summary>
    public KindTile TileOf(UnitType t) => _tiles.Find(k => k.Kind == t);
    readonly List<KindTile> _tiles = new();

    Control KeepPane()
    {
        var sim = _m.Sim;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(236, 0) };
        left.AddThemeConstantOverride("separation", 3);
        left.AddChild(Hud.CapsLabel("Цитадель · набор войска", 9));
        var head = Hud.Text("", Ornament.SerifBold, 14, Ornament.Parchment);
        left.AddChild(head);
        var note = Hud.Text("", Ornament.Serif, 12, Ornament.Aged);
        note.TooltipText = "Места в войске\nЦитадель даёт 10 мест (с «Казармами» 14). Каждая лежанка в жилом доме даёт ещё одно. Дом проще всего нарисовать " +
            "прямоугольником с заливкой «дом»; ещё можно обвести закрытую комнату с воротами делянкой «жилой дом». Всадник занимает 2 места, великан 4, катапульта и баллиста с расчётом 3, требушет 5.";
        note.MouseFilter = MouseFilterEnum.Stop;
        left.AddChild(note);
        row.AddChild(left);
        row.AddChild(new Rule(true));
        var kinds = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        kinds.AddThemeConstantOverride("h_separation", 4);
        kinds.AddThemeConstantOverride("v_separation", 3);
        _tiles.Clear();
        foreach (var t in Kinds.PlayerKinds)
        {
            var tile = new KindTile(_m, t) { CustomMinimumSize = new Vector2(35, 44) };
            _tiles.Add(tile);
            kinds.AddChild(tile);
        }
        row.AddChild(kinds);
        _refresh.Add(() =>
        {
            int have = sim.Household(), places = sim.Places, beds = places - Sim.CitadelPlaces - (sim.Knows("barracks") ? 4 : 0);
            head.Text = $"Войско: {have} из {places} мест{Hud.PlacesNote(sim, " · ")}";
            head.AddThemeColorOverride("font_color", have >= places ? Ornament.Ember : Ornament.Parchment);
            note.Text = beds > 0 ? $"места: цитадель {places - beds}, жилые дома {beds}" : Bindings.Fill("места дают цитадель и жилые дома ({ToolGather})");
            foreach (var k in _tiles) k.QueueRedraw();
        });
        return row;
    }

    // ---------------------------------------------------------------- nobody chosen

    Control HomePane()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var present = new List<UnitType>();
        foreach (var kind in Kinds.PlayerKinds) if (_m.Sim.Count(kind) > 0) present.Add(kind);
        var keep = Btn($"Цитадель · {Bindings.Label(Act.Keep)}", "Цитадель ({Keep})\nНабор войска. Новобранцы готовятся по очереди. Войско не может быть больше числа мест.", () => _m.Command.OpenKeep(), 98);
        keep.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        row.AddChild(keep);
        foreach (var kind in present.GetRange(0, Math.Min(present.Count, 4)))
        {
            var t = kind;
            var block = new HBoxContainer();
            block.AddThemeConstantOverride("separation", 6);
            var face = new KindFace { Kind = t, CustomMinimumSize = new Vector2(44, 44), SizeFlagsVertical = SizeFlags.ShrinkBegin, TooltipText = Retinue.Tip(t) };
            face.Clicked += () => _m.Command.SelectKind(t);
            block.AddChild(face);
            var info = new VBoxContainer();
            info.AddThemeConstantOverride("separation", 0);
            var name = Hud.Text("", Ornament.SerifBold, 14, Ornament.Parchment);
            info.AddChild(name);
            var note = Hud.Text("", Ornament.Serif, 12, Ornament.Aged);
            note.CustomMinimumSize = new Vector2(112, 0);
            info.AddChild(note);
            block.AddChild(info);
            row.AddChild(block);
            _refresh.Add(() =>
            {
                var (n, text) = Retinue.Summary(_m.Sim, t);
                name.Text = $"{Retinue.Many(t)} · {n}";
                note.Text = text;
            });
        }
        row.AddChild(new Rule(true));
        var squads = new VBoxContainer();
        squads.AddThemeConstantOverride("separation", 4);
        var sqCap = Hud.CapsLabel("Отряды", 8);
        sqCap.TooltipText = "Отряды\nCtrl+цифра (или Ctrl+щелчок) — запомнить выбранных, Shift+цифра (или щелчок) — выбрать.";
        squads.AddChild(sqCap);
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 4);
        var buttons = new List<Button>();
        for (int i = 0; i < 5; i++)
        {
            int n = i;
            var b = new Button
            {
                Text = Commander.Roman(i), FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(42, 26), MouseDefaultCursorShape = CursorShape.PointingHand,
                TooltipText = $"Отряд {Commander.Roman(i)}\nЩелчок или Shift+{i + 1} — выбрать, дважды — показать. Ctrl+щелчок или Ctrl+{i + 1} — запомнить выбранных.",
            };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
                {
                    if (mb.CtrlPressed || mb.MetaPressed) _m.Command.RememberSquad(n); else _m.Command.CallSquad(n);
                    b.AcceptEvent();
                }
            };
            buttons.Add(b);
            line.AddChild(b);
        }
        squads.AddChild(line);
        squads.AddChild(AlarmButton(90));
        row.AddChild(squads);
        _refresh.Add(() =>
        {
            var sq = _m.Command.Squads;
            for (int i = 0; i < buttons.Count; i++) { sq[i].RemoveAll(u => u.Dead); buttons[i].Text = sq[i].Count > 0 ? $"{Commander.Roman(i)}·{sq[i].Count}" : Commander.Roman(i); }
        });
        return row;
    }
}

/// <summary>A bar of how much of a unit is left, with the numbers beside it.</summary>
public partial class HealthBar : Control
{
    public Unit Unit;

    public HealthBar() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        if (Unit == null) return;
        float f = Math.Clamp(Unit.Hp / Unit.MaxHp, 0, 1), w = Size.X - 80, h = 8, y = (Size.Y - h) / 2;
        var r = new Rect2(0, y, w, h);
        DrawRect(r.Grow(1), new Color(0, 0, 0, .7f));
        DrawRect(new Rect2(r.Position, new Vector2(w * f, h)), f > .6f ? Ornament.Moss : f > .3f ? Ornament.Ember : Ornament.Blood);
        DrawLine(r.Position + new Vector2(0, 1), r.Position + new Vector2(w * f, 1), new Color(1, 1, 1, .2f), 1);
        DrawRect(r, Craft.BrassDark, false, 1);
        DrawString(Ornament.Serif, new Vector2(w + 8, y + h + 1), $"{Math.Ceiling(Math.Max(0, Unit.Hp))} из {Math.Ceiling(Unit.MaxHp)}", HorizontalAlignment.Left, -1, 12, Ornament.Parchment);
    }
}

/// <summary>
/// A body drawn as its parts — head, trunk, arms, legs — each coloured by how much of it is left: green whole, yellow
/// scratched, orange badly hurt, red barely there; a part cut off is only an outline, struck through.
/// </summary>
public partial class BodyChart : Control
{
    public Unit Unit;
    static readonly Dictionary<(int id, int rev), float[]> Cache = new();

    public BodyChart() { MouseFilter = MouseFilterEnum.Stop; }

    /// <summary>How much is left of the head, trunk, right arm, left arm, right leg and left leg, each from 0 to 1.</summary>
    public static float[] Left(Unit u)
    {
        var shares = new float[6];
        Array.Fill(shares, 1);
        var w = u.Wounds;
        if (w == null) return shares;
        if (Cache.TryGetValue((u.Id, w.Rev), out var cached)) return cached;
        var plan = w.Plan;
        var had = new int[6]; var has = new int[6];
        for (int p = 0; p < BodyPlan.Parts; p++)
        {
            var g0 = plan.Grid[p]; var g = w.Grid[p];
            if (g0 == null) continue;
            for (int i = 0; i < g0.M.Length; i++)
            {
                if (g0.M[i] == 0 || g0.T[i] == (byte)Tissue.Gear) continue;
                int y = i / (g0.SX * g0.SZ) + g0.Y0;
                int slot = p switch { 0 => y >= plan.HeadY0 ? 0 : 1, 1 => 2, 2 => 3, 3 => 4, _ => 5 };
                had[slot]++;
                if (g != null && g.M[i] != 0) has[slot]++;
            }
        }
        for (int k = 0; k < 6; k++) shares[k] = had[k] == 0 ? 1 : has[k] / (float)had[k];
        if (!w.MainHand) shares[2] = Math.Min(shares[2], .2f);
        if (!w.OffHand) shares[3] = Math.Min(shares[3], .2f);
        // a bone broken shows as a part badly hurt, whatever is left of it (Sim.Trauma.cs)
        if ((w.Broken & Break.Main) != 0) shares[2] = Math.Min(shares[2], .6f);
        if ((w.Broken & Break.Off) != 0) shares[3] = Math.Min(shares[3], .6f);
        if ((w.Broken & Break.LegR) != 0) shares[4] = Math.Min(shares[4], .6f);
        if ((w.Broken & Break.LegL) != 0) shares[5] = Math.Min(shares[5], .6f);
        if ((w.Broken & Break.Ribs) != 0) shares[1] = Math.Min(shares[1], .75f);
        Cache[(u.Id, w.Rev)] = shares;
        return shares;
    }

    static Color Of(float share) => share >= .97f ? Ornament.Moss : share >= .8f ? new Color("d6c35a") : share >= .5f ? Ornament.Ember : Ornament.Blood;

    public override void _Draw()
    {
        if (Unit == null) return;
        var s = Left(Unit);
        float cx = Size.X / 2, k = Size.Y / 74;
        void Part(Rect2 r, float share, bool round = false)
        {
            r = new Rect2(cx + r.Position.X * k, r.Position.Y * k, r.Size.X * k, r.Size.Y * k);
            bool gone = share < .3f;
            var col = Of(share);
            if (round)
            {
                var c = r.GetCenter(); float rad = r.Size.X / 2;
                DrawCircle(c, rad + 1, new Color(0, 0, 0, .7f), true, -1, true);
                if (!gone) DrawCircle(c, rad, col, true, -1, true);
                else DrawArc(c, rad, 0, MathF.Tau, 20, col, 1.2f, true);
            }
            else
            {
                DrawRect(r.Grow(1), new Color(0, 0, 0, .7f));
                if (!gone) DrawRect(r, col); else DrawRect(r, col, false, 1.2f);
            }
            if (gone)
            {
                DrawLine(r.Position, r.End, Ornament.Blood, 1.4f, true);
                DrawLine(new Vector2(r.Position.X, r.End.Y), new Vector2(r.End.X, r.Position.Y), Ornament.Blood, 1.4f, true);
            }
        }
        Part(new Rect2(-7, 1, 14, 14), s[0], true);
        Part(new Rect2(-9, 17, 18, 24), s[1]);
        Part(new Rect2(-16, 17, 6, 22), s[2]);
        Part(new Rect2(10, 17, 6, 22), s[3]);
        Part(new Rect2(-8, 43, 7, 28), s[4]);
        Part(new Rect2(1, 43, 7, 28), s[5]);
        if (Unit.Wounds is { Bleed: > .15f } w)
        {
            var c = new Vector2(Size.X - 6, 8);
            DrawColoredPolygon(new[] { c + new Vector2(0, -6), c + new Vector2(3.4f, 0), c + new Vector2(-3.4f, 0) }, w.Bleed > .8f ? new Color("e0412d") : new Color("b0352a"));
            DrawCircle(c + new Vector2(0, 1.4f), 3.6f, w.Bleed > .8f ? new Color("e0412d") : new Color("b0352a"), true, -1, true);
        }
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>
/// A pawn's priority for one kind of work, in a well with the work's icon: 1 in ember is what it does first, 4 last, an
/// empty well never. A click raises it (none → 1 → 2 → 3 → 4 → none), the right button lowers it.
/// </summary>
public partial class PriorityCell : Control
{
    readonly Main _m;
    readonly Unit _u;
    readonly WorkKind _k;
    bool _hover;

    public PriorityCell() { }

    public PriorityCell(Main m, Unit u, WorkKind k)
    {
        _m = m; _u = u; _k = k;
        CustomMinimumSize = new Vector2(58, 40);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = Work.Tips[(int)k] + "\n\nЩелчок меняет очередь: 1, 2, 3, 4, никогда. ПКМ — обратно.";
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public static Icons.Painter IconOf(WorkKind k) => k switch { WorkKind.Build => Icons.Box, WorkKind.Dig => Icons.Shovel, WorkKind.Fell => Icons.Gather, WorkKind.Fire => Icons.Flame, _ => Icons.Pick };

    public static Color InkOf(byte p) => p switch { 1 => Ornament.EmberGlow, 2 => Ornament.BronzeLight, 3 => Ornament.Parchment, 4 => Ornament.Aged, _ => Ornament.Faded };

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } mb || mb.ButtonIndex is not (MouseButton.Left or MouseButton.Right)) return;
        _m.Sim.SetPriority(_u, _k, Work.Cycle(_u.Priority[(int)_k], mb.ButtonIndex == MouseButton.Right));
        QueueRedraw();
        AcceptEvent();
    }

    public override void _Draw()
    {
        byte p = _u.Priority[(int)_k];
        var r = new Rect2(Vector2.Zero, Size);
        Ornament.Well(this, r, _hover, p == 1, false);
        IconOf(_k)(this, new Rect2(5, 5, Size.Y - 10, Size.Y - 10), p == 0 ? Ornament.Fade(Ornament.Faded, .6f) : Ornament.Bronze);
        DrawString(Ornament.SerifBold, new Vector2(Size.Y - 2, Size.Y / 2 + 8), p == 0 ? "—" : p.ToString(), HorizontalAlignment.Center, Size.X - Size.Y, 20, InkOf(p));
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>One of several chosen: its face and how much of it is left; a click picks it alone.</summary>
public partial class MiniCard : Control
{
    readonly Main _m;
    readonly Unit _u;

    public MiniCard() { }

    public MiniCard(Main m, Unit u)
    {
        _m = m; _u = u;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = $"{u.Name} №{u.Id}";
    }

    public override string _GetTooltip(Vector2 at) => $"{_u.Name} №{_u.Id}\n{Retinue.Doing(_u, _m.Sim)}. Щелчок — выбрать только его.";

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { _m.Command.SelectOne(_u); AcceptEvent(); }
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        Ornament.Gradient(this, r, new Color("2a2024"), new Color("0e0b0d"));
        DrawTextureRect(Portraits.Of(_u, 48), new Rect2(1, 1, Size.X - 2, Size.X - 2), false, _u.Dead ? new Color(.4f, .4f, .4f) : Colors.White);
        float f = Math.Clamp(_u.Hp / _u.MaxHp, 0, 1), by = Size.X + 1, bh = Size.Y - by - 2;
        DrawRect(new Rect2(1, by, Size.X - 2, bh), new Color(0, 0, 0, .7f));
        DrawRect(new Rect2(1, by, (Size.X - 2) * f, bh), f > .6f ? Ornament.Moss : f > .3f ? Ornament.Ember : Ornament.Blood);
        DrawRect(r, _u.Wounds is { Bleed: > .15f } ? Ornament.Blood : Craft.Brass, false, 1);
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>The face of a kind, for the household with nobody chosen; a click picks all of that kind.</summary>
public partial class KindFace : Control
{
    public UnitType Kind;
    public event Action Clicked;
    bool _hover;

    public KindFace()
    {
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { if (Clicked != null) Sound.Ui("tick"); Clicked?.Invoke(); AcceptEvent(); }
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        Ornament.Gradient(this, r, new Color("2a2024"), new Color("0e0b0d"));
        DrawTextureRect(Portraits.Of(Kind, 0, 48), r.Grow(-2), false);
        DrawRect(r, _hover ? Craft.BrassLight : Craft.Brass, false, 1.2f);
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}
