using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The middle of the bottom console, saying what is in hand or who is chosen: a brush shows its settings; one pawn its
/// name, health, its wounds part by part of the body, what it is doing and the work it is set to; several their faces;
/// nobody chosen, the household by kind with hiring and the squads. Orders for the chosen run along its foot.
/// </summary>
public partial class Inspector : Control
{
    Main _m;
    string _key = "";
    Control _pane;
    readonly List<Action> _refresh = new();

    /// <summary>The one pawn shown in full, if one is.</summary>
    public Unit One { get; private set; }

    public void Init(Main m)
    {
        _m = m;
        MouseFilter = MouseFilterEnum.Stop;
        Refresh(true);
    }

    public override void _Draw() => Craft.Inset(this, new Rect2(Vector2.Zero, Size));

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
        if (sel.Count == 0 && _m.Inspected is { } look) return "look:" + look.Id;
        if (sel.Count == 0) return "home";
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
        box.AddChild(_key == "tool" ? ToolPane() : _key.StartsWith("spell:") ? SpellPane() : _key.StartsWith("look:") && _m.Inspected != null ? LookPane(_m.Inspected)
            : sel.Count == 0 ? HomePane() : sel.Count == 1 ? OnePane(sel[0]) : GroupPane(sel));
        AddChild(box);
        _pane = box;
    }

    static Button Btn(string text, string tip, Action act, float w = 0)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(w, 24), TooltipText = tip, MouseDefaultCursorShape = CursorShape.PointingHand };
        b.AddThemeFontSizeOverride("font_size", 12);
        b.Pressed += act;
        return b;
    }

    /// <summary>The alarm's button: "Тревога", or "Отбой" while it is up, glowing then.</summary>
    Button AlarmButton(float w)
    {
        var b = Btn("", "", () => _m.Command.Alarm(), w);
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
        var mat = Hud.CapsLabel((t.Tool == Tool.Gather ? (t.Shelter ? "убежище от орды" : t.Sanctum ? "святилище жрецов" : "добывать: " + (t.GatherKind switch { Stockpile.Wood => "лес", Stockpile.Earth => "землю", _ => "камень" }))
            : o.Dig ? "кирка: копать и рубить" : "материал: " + Mat.NameOf(o.Material).ToLowerInvariant()), 9);
        mat.VerticalAlignment = VerticalAlignment.Center;
        head.AddChild(mat);
        var hint = Hud.Text(Hud.ToolHint(t.Tool), Ornament.SerifItalic, 12, Ornament.Aged);
        hint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        head.AddChild(hint);
        var undo = Btn("Отменить · Ctrl+Z", Hud.UndoTip, () => _m.Tools.Undo());
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
            mats.AddChild(new Choice("Ловушка", TrapTip, new[] { "колья", "настил", "камни" }, Array.IndexOf(Traps, o.Material), i => t.SetMaterial(Traps[i])));
            col.AddChild(mats);
        }
        if (opts.GetChildCount() == 0) col.AddChild(Hud.Text("Esc — убрать кисть. Щелчок по ячейке справа или клавиша — взять другую.", Ornament.SerifItalic, 12, Ornament.Faded));
        return col;
    }

    static readonly byte[] Laid = { Mat.Stone, Mat.Wood, Mat.Rammed, Mat.Pitch };
    static readonly byte[] Traps = { Mat.Stakes, Mat.TrapDeck, Mat.DropStone };

    const string TrapTip = "Ловушки\nКолья — частокол заострённых кольев в ладонь друг от друга, высотой с «Высоту»: кто идёт сквозь них, идёт " +
        "медленно и ранится и понемногу их вытаптывает; орда обходит их, если обход недолог, наши — всегда, где есть как. Из брёвен, горят.\n" +
        "Настил — ловчий: тонкие жерди под дёрном вровень с землёй, где начат мазок, и только над тем, что ниже, — над ямой, рвом. Наших держит, " +
        "под ордой проваливается весь, сколько его в трёх метрах. Выройте под ним яму глубже человеческого роста и поставьте на дне колья. Из брёвен.\n" +
        "Камни — на сброс: сложите их на стене у края. Когда налётчик встанет у подножия под ними, а наш будет рядом с камнями, он столкнёт их " +
        "врагу на голову; камни бьют всех, кто под ними, и ложатся обломками. Из камня.";

    const string MaterialTip = "Материал\nКамень (Z), брёвна (X), земля (C) — из запасов. Смола варится из брёвен: слой её на дне рва " +
        "вспыхивает весь разом от огненной стрелы и горит долго и вдвое жарче — налётчики в нём сгорают, а кто выбежал, горит ещё. " +
        "Рабочие смолу не тушат; сгоревшую возвращает починка (0).";

    void Options(HBoxContainer opts)
    {
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
                opts.AddChild(new Choice("Форма", "Форма кисти\nСтолб — стена от руки до заданной высоты (с киркой — полоса на глубину). Шар — налипает на поверхность (с киркой — вынимает ком). Клавиша F.",
                    new[] { "столб", "шар" }, (int)o.Form, i => { o.Form = (BrushForm)i; t.Changed(); }));
                opts.AddChild(Width("Размер"));
                if (o.Form == BrushForm.Column) { opts.AddChild(Height()); opts.AddChild(Level()); }
                break;
            case Tool.Line:
                opts.AddChild(Width(dig ? "Ширина" : "Толщина")); opts.AddChild(Height()); opts.AddChild(Level());
                break;
            case Tool.Box:
            case Tool.Circle:
                opts.AddChild(new Choice("Заливка", "Заливка\nСплошная фигура или только стенки заданной толщины. Клавиша F.",
                    new[] { "сплошной", t.Tool == Tool.Box ? "стенки" : "кольцо" }, o.Hollow ? 1 : 0, i => { if ((i == 1) != o.Hollow) t.ToggleForm(); }));
                if (o.Hollow) opts.AddChild(Width());
                opts.AddChild(Height()); opts.AddChild(Level());
                break;
            case Tool.Bridge:
                opts.AddChild(new Choice("Вид", "Вид моста\nАрочный — каменный или деревянный мост дугой от берега до берега, до 25 м. Подъёмный — дубовый настил на петлях " +
                    "над рвом от 2 до 6 м, у берега, что ближе к цитадели: опущен, пока не подойдёт орда, тогда поднимается сам — кто на нём, летит в ров; " +
                    "поднятый — стена из брёвен. Щелчок по подъёмному мосту без кисти — сам / опущен / поднят.",
                    new[] { "арочный", "подъёмный" }, o.DrawBridge ? 1 : 0, i => { o.DrawBridge = i == 1; t.Changed(); }));
                opts.AddChild(new Stepper("Проход", "Ширина прохода\nПо мосту ходят строем: уже 1,25 м его не сделать, к ширине добавляются парапеты или перила. Клавиши [ и ].",
                    () => Len(Math.Max(Bridges.MinWidth, o.Width)), d => t.StepWidth(d)));
                opts.AddChild(Hud.CapsLabel(o.DrawBridge || o.Material == Mat.Wood ? "деревянный" : "каменный", 11, Ornament.BronzeLight));
                break;
            case Tool.Extrude:
                opts.AddChild(new Stepper("Шаг", "Шаг\nНа сколько нарастает или уходит вглубь грань за щелчок. Клавиши − и =.", () => Len(o.Step), d => t.StepHeight(d)));
                break;
            case Tool.Gather:
                opts.AddChild(new Choice("Участок", "Участок\nКамень (Z), лес (X), земля (C) — откуда брать этот запас. Убежище — куда по тревоге (T) уходят мирные " +
                    "и где пережидают её: под крышей, за стенами; ближайшее из убежищ и цитадели. Святилище — где днём молятся жрецы и копят вмешательство " +
                    "для заклинаний; на месте силы (Курган, Чёрный камень, Мёртвый родник), пока оно ваше, молитву слышат втрое лучше.",
                    new[] { "камень", "лес", "земля", "убежище", "святилище" }, t.GatherKind switch { Stockpile.Wood => 1, Stockpile.Earth => 2, Zone.Shelter => 3, Zone.Sanctum => 4, _ => 0 },
                    i => { if (i == 3) t.SetShelter(true); else if (i == 4) t.SetSanctum(true); else t.SetMaterial(new[] { Mat.Stone, Mat.Wood, Mat.Rammed }[i]); }));
                if (t.GatherKind is Stockpile.Stone or Stockpile.Earth)
                    opts.AddChild(new Stepper("Глубина", "Глубина\nДо какой глубины от нынешней поверхности брать камень или землю. Клавиши − и =.", () => Len(o.Depth), d => t.StepHeight(d)));
                break;
            case Tool.Paste when t.PasteBp != null:
                opts.AddChild(Hud.CapsLabel($"поворот {t.PasteRot * 90}° · R — повернуть · {t.PasteBp.Describe()}", 10, Ornament.Aged));
                break;
        }
        if (t.Tool is Tool.Line or Tool.Box or Tool.Circle or Tool.Paste or Tool.Gather or Tool.Repair or Tool.Capture or Tool.Unmark or Tool.Bridge)
            opts.AddChild(new Choice("Сетка", "Сетка\nПривязка к сетке от центра цитадели: начало и конец штриха встают в её узлы, линии идут вдоль осей или под 45°, " +
                "круги — целыми шагами. Стены выходят ровными, а одинаковое по обе стороны цитадели — симметричным. Клавиша N.",
                new[] { "нет", "50 см", "1 м", "2 м" }, Array.IndexOf(ToolOptions.Grids, o.Grid), i => t.SetGrid(ToolOptions.Grids[i])));
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
        var state = Hud.Text("", Ornament.SerifItalic, 13, Ornament.BronzeLight);
        col.AddChild(state);
        _refresh.Add(() =>
        {
            double ready = _m.Sim.ReadyIn(sp.Kind), have = _m.Sim.Favour;
            string how = sp.Stroke ? "проведите линию по земле" : sp.Radius <= 0 ? "щёлкните по полю где угодно" : sp.Kind == SpellKind.Fall ? "щёлкните по стене, скале или постройке" : "щёлкните по полю";
            state.Text = $"{sp.Cost:0} вмешательства (есть {have:0})" + (ready > 0 ? $" · будет готово через {ready:0} с" : " · готово") + $" · {how} · Esc — убрать";
            state.AddThemeColorOverride("font_color", ready > 0 || have < sp.Cost ? Ornament.Blood.Lightened(.3f) : Ornament.BronzeLight);
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
        var chart = new BodyChart { Unit = u, CustomMinimumSize = new Vector2(58, 74), TooltipText = "Тело\nЧто от него осталось, по частям: зелёное цело, жёлтое задето, рыжее изранено, красное едва держится; перечёркнутое отрублено." };
        top.AddChild(chart);
        var work = new VBoxContainer();
        work.AddThemeConstantOverride("separation", 3);
        var cellList = new List<PriorityCell>();
        if (u.T.Prays)
        {
            // a priest does none of the pawns' work: what it does instead
            work.AddChild(Hud.CapsLabel("Жрец", 8));
            var about = Hud.Text("Днём молится в святилище, ночью уходит в цитадель. Молитва копит вмешательство; на месте силы её слышат втрое лучше.",
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
            hurt.Text = u.Dead ? "пал" : h != "" ? h : u.Hp < u.MaxHp - .5f ? "ушиблен" : "цел и невредим";
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
        row.AddChild(Btn("За работу", "За работу\nВыбранные снова сами ищут работу по разметке.", () => _m.Command.ReleaseSelected(), 92));
        row.AddChild(Btn("Стоять", "Стоять здесь\nВыбранные остаются на месте; для солдат это место становится постом.", () => _m.Command.HoldSelected(), 70));
        row.AddChild(AlarmButton(70));
        var duty = new List<Button>();
        // priests bring nothing in
        bool labour = chosen.Exists(u => !u.T.Prays);
        if (labour)
        {
        row.AddChild(new Rule(true));
        var cap = Hud.CapsLabel("Добывать", 8);
        cap.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(cap);
        foreach (var (text, kind, tip) in new[]
        {
            ("—", Stockpile.None, "Ничего не добывать самим\nТолько размеченная работа и делянки."),
            ("камень", Stockpile.Stone, "Добывать камень\nКогда размеченной работы нет, выбранные сами ломают ближайший валун или руины, а выломав, ищут следующие."),
            ("брёвна", Stockpile.Wood, "Добывать брёвна\nКогда размеченной работы нет, выбранные сами рубят ближайшие деревья."),
            ("землю", Stockpile.Earth, "Добывать землю\nКогда размеченной работы нет, выбранные роют яму поодаль от крепости — одну на всех."),
        })
        {
            int k = kind;
            var b = Btn(text, tip, () => _m.Command.DutySelected(k));
            b.ToggleMode = true;
            duty.Add(b);
            row.AddChild(b);
        }
        }
        bool workers = chosen.Exists(u => u.T.Work >= 1 && !u.T.Prays), archers = chosen.Exists(u => u.T.Ranged), melee = chosen.Exists(Sim.TakesAlive);
        Button militia = null, fire = null, alive = null;
        if (archers)
        {
            row.AddChild(new Rule(true));
            fire = Btn("Огонь", "Огненные стрелы\nЛучники стреляют огнём: поджигают лестницы и настилы орды, смолу и доски под ногами налётчиков, " +
                "а налётчик, в которого попали, горит. Стреляют реже. ПКМ по деревянному, по дереву или смоле — поджечь. Ещё раз — простые стрелы.",
                () => _m.Command.FireArrowsSelected(), 60);
            fire.ToggleMode = true;
            row.AddChild(fire);
        }
        if (melee)
        {
            row.AddChild(new Rule(true));
            alive = Btn("Живьём", "Брать живым\nМечники и ополчение не добивают израненного налётчика, а валят и связывают его, когда в нём остаётся четверть сил " +
                "(скованного Цепями — три пятых). Свободный рабочий отведёт пленного в цитадель, если рядом нет орды. Пленных приносят в жертву (вкладка «Таинства») " +
                "или отдают послу орды за выкуп. Ещё раз — снова бить насмерть.", () => _m.Command.TakeAliveSelected(), 70);
            alive.ToggleMode = true;
            row.AddChild(alive);
        }
        if (workers)
        {
            row.AddChild(new Rule(true));
            militia = Btn("Ополчение", "Ополчение\nРабочие не бегут от орды, а встречают её с кирками, как мечники. Ещё раз — снова бегут. Солдатам не нужно.", () => _m.Command.MilitiaSelected(), 92);
            militia.ToggleMode = true;
            row.AddChild(militia);
        }
        _refresh.Add(() =>
        {
            int[] kinds = { Stockpile.None, Stockpile.Stone, Stockpile.Wood, Stockpile.Earth };
            for (int i = 0; i < duty.Count; i++) duty[i].SetPressedNoSignal(chosen.TrueForAll(u => u.T.Prays || u.Duty == kinds[i]));
            militia?.SetPressedNoSignal(chosen.TrueForAll(u => u.T.Work < 1 || u.T.Prays || u.Militia));
            fire?.SetPressedNoSignal(chosen.TrueForAll(u => !u.T.Ranged || u.FireArrows));
            alive?.SetPressedNoSignal(chosen.TrueForAll(u => !Sim.TakesAlive(u) || u.TakeAlive));
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
        var chart = new BodyChart { Unit = u, CustomMinimumSize = new Vector2(58, 74), TooltipText = "Тело\nЧто от него осталось, по частям: зелёное цело, жёлтое задето, рыжее изранено, красное едва держится; перечёркнутое отрублено." };
        top.AddChild(chart);
        var about = new VBoxContainer();
        about.AddThemeConstantOverride("separation", 2);
        about.AddChild(Hud.CapsLabel(u.Team == Team.Horde ? "Налётчик" : "Зверь", 8));
        string note = u.Team == Team.Wild
            ? $"{u.T.Bounty} зол. за добычу; убегает от тех, кто подходит. Выберите своих и ПКМ по нему — охота."
            : (u.Leader ? $"Вожак: втрое дороже за голову ({u.T.Bounty * 3} зол.). " : $"{u.T.Bounty} зол. за голову. ")
              + (u.Returned ? "Бежал в прошлый раз и вернулся. " : "") + "Выберите своих и ПКМ по нему — в атаку.";
        if (u.Bound)
            note = u.Inside ? "Пленный в цитадели: его можно принести в жертву (вкладка «Таинства») или отдать послу орды за выкуп. Ночью без стражи у двери он выберется."
                : "Связан: свободный рабочий отведёт его в цитадель, если рядом нет орды. Оставленный надолго — распутается и убежит.";
        var noteLabel = Hud.Text(note, Ornament.Serif, 12, Ornament.Aged, wrap: true);
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
        col.AddChild(Hud.Text($"Выбрано {chosen.Count}: {string.Join(", ", counts)} · щелчок по лицу — выбрать одного", Ornament.SerifBold, 13, Ornament.BronzeLight));
        var faces = new HFlowContainer();
        faces.AddThemeConstantOverride("h_separation", 4);
        faces.AddThemeConstantOverride("v_separation", 3);
        foreach (var u in chosen.GetRange(0, Math.Min(chosen.Count, 24))) faces.AddChild(new MiniCard(_m, u) { CustomMinimumSize = new Vector2(34, 42) });
        col.AddChild(faces);
        col.AddChild(Orders(chosen));
        _refresh.Add(() => { foreach (var c in faces.GetChildren()) if (c is MiniCard m) m.QueueRedraw(); });
        return col;
    }

    // ---------------------------------------------------------------- nobody chosen

    Control HomePane()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        foreach (var kind in Kinds.PlayerKinds)
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
            var hire = Btn("", "", () => _m.Command.Hire(t), 98);
            hire.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            info.AddChild(hire);
            block.AddChild(info);
            row.AddChild(block);
            _refresh.Add(() =>
            {
                var (n, text) = Retinue.Summary(_m.Sim, t);
                name.Text = $"{Retinue.Many(t)} · {n}";
                note.Text = text;
                int cost = _m.Sim.HireCost(t);
                hire.Text = $"Нанять · {cost}";
                hire.TooltipText = $"Нанять: {t.Name.ToLowerInvariant()}\n{cost} золота. Приходит к воротам цитадели. Золото дают рудники, убитые налётчики, охота и отбитые волны.";
                hire.Disabled = _m.Sim.Stock.Gold < cost || _m.Sim.Over;
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
                TooltipText = $"Отряд {Commander.Roman(i)}\nЩелчок или Shift+{i + 1} — выбрать его, дважды — показать. Ctrl+щелчок или Ctrl+{i + 1} — запомнить в нём выбранных.",
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
        TooltipText = Work.Tips[(int)k] + "\n\nЩелчок — по очереди 1, 2, 3, 4, никогда; ПКМ — обратно. 1 — первым делом.";
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
        DrawString(Ornament.Display, new Vector2(Size.Y - 2, Size.Y / 2 + 8), p == 0 ? "—" : p.ToString(), HorizontalAlignment.Center, Size.X - Size.Y, 20, InkOf(p));
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
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { Clicked?.Invoke(); AcceptEvent(); }
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
