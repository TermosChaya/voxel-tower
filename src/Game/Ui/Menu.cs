using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The game's own menu, over a darkened field: go on, save into a slot, read one back (a slot, the quick save, the
/// autosave), the controls, the settings, a new siege, the sandbox, the tutorial, quit. At the start of the game it is
/// the title: a new siege, the sandbox, the tutorial, a saved game, the controls, the settings, quit. The siege stands still while it is open. Esc or the medallion by the
/// clock opens it; the help medallion and {Help} open it on the controls.
/// </summary>
public partial class GameMenu : Control
{
    Main _m;
    VBoxContainer _page;
    Control _panel, _dim;
    bool _wasPaused;
    const float W = 600, H = 610;

    public void Init(Main m)
    {
        _m = m;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var dim = _dim = new ColorRect { Color = new Color(0, 0, 0, .55f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var panel = new BottomConsole.Plate(c => Craft.Plate(c, new Rect2(Vector2.Zero, c.Size))) { MouseFilter = MouseFilterEnum.Stop };
        panel.SetAnchorsPreset(LayoutPreset.Center);
        AddChild(panel);
        _panel = panel;
        Fit(W, H);
        var box = new MarginContainer();
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_right" }) box.AddThemeConstantOverride(side, 34);
        foreach (var side in new[] { "margin_top", "margin_bottom" }) box.AddThemeConstantOverride(side, 26);
        panel.AddChild(box);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        box.AddChild(col);
        var title = Hud.Text("Voxel Tower", Ornament.Display, 30, Ornament.EmberGlow);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(title);
        col.AddChild(new Rule(false));
        _page = new VBoxContainer();
        _page.AddThemeConstantOverride("separation", 8);
        _page.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(_page);
    }

    public bool IsOpen => Visible;
    /// <summary>The page shown: "main", "save", "load", "settings", "controls".</summary>
    public string PageName { get; private set; } = "main";
    /// <summary>The title of the game's start: its first page begins a game; Esc does not leave it.</summary>
    bool _title;

    public void OpenTitle()
    {
        _title = true;
        Open();
    }

    public void Open(string page = null)
    {
        if (Visible) { if (page != null) Go(page); return; }
        _wasPaused = _m.Paused;
        _m.Paused = true;
        Visible = true;
        Juice.FadeIn(_dim, .2);
        Juice.Appear(_panel, .95f, .22);
        Go(page);
    }

    void Go(string page)
    {
        switch (page)
        {
            case "report": Report_(); break;
            case "save": Saving(); break;
            case "load": Loading(); break;
            case "settings": Settings_(); break;
            case "hardness": Hardness_(); break;
            case "sandbox": Sandbox_(); break;
            case "campaign": Campaign_(); break;
            case "graphics": Picture_(); break;
            case "controls": Controls_(); break;
            default: Main_(); break;
        }
    }

    /// <summary>The panel this large in the middle of the screen, as far as the screen lets it.</summary>
    void Fit(float w, float h)
    {
        var vp = GetViewportRect().Size;
        if (vp.X > 0) { w = Math.Min(w, vp.X - 40); h = Math.Min(h, vp.Y - 40); }
        _panel.OffsetLeft = -w / 2; _panel.OffsetRight = w / 2; _panel.OffsetTop = -h / 2; _panel.OffsetBottom = h / 2;
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        _m.Paused = _wasPaused;
        _m.Hud.Refresh();
    }

    /// <summary>Esc: from a page back to the first, from the first out of the menu.</summary>
    public void Back()
    {
        if (_onMain) { if (!_title) Close(); }
        else Main_();
    }

    /// <summary>The title left for a game: the siege or the tutorial begins.</summary>
    /// <summary>How hard the new siege is: the three, each with what it means.</summary>
    void Hardness_()
    {
        _onMain = false;
        Clear();
        PageName = "hardness";
        _page.AddChild(Hud.CapsLabel("Сложность", 10));
        string[] about =
        {
            "Ватаги меньше, налётчики слабее, цитадель крепче. Для первой игры.",
            "Обычная сложность.",
            "Ватаги больше, налётчики крепче и бьют сильнее.",
        };
        for (int i = 0; i < 3; i++)
        {
            var h = (Hardness)i;
            _page.AddChild(Big(Sim.HardnessNames[i], Sim.HardnessNames[i] + "\n" + about[i], () => Start(h)));
            var note = Hud.Text(about[i], Ornament.SerifItalic, 12, Ornament.Aged, wrap: true);
            note.HorizontalAlignment = HorizontalAlignment.Center;
            note.CustomMinimumSize = new Vector2(320, 0);
            note.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            _page.AddChild(note);
        }
        _page.AddChild(new Rule(false));
        _page.AddChild(Big("Назад", "", Main_));
    }

    /// <summary>A new siege as hard as chosen: on the title the valley under it goes on as it is; in play the scene begins again.</summary>
    void Start(Hardness h)
    {
        if (_title) { _m.Sim.SetHardness(h); Begin(_m.Welcome); return; }
        Main.PendingHardness = h;
        _m.NewGame();
    }

    /// <summary>The sandbox, the horde in it coming only when called: with everything open, or a siege of one's own with the clock standing.</summary>
    void Sandbox_()
    {
        _onMain = false;
        Clear();
        Fit(W, 500);
        PageName = "sandbox";
        _page.AddChild(Hud.CapsLabel("Песочница", 10));
        var about = Hud.Text(Bindings.Fill("Орда приходит, только когда её позовёте: {CallWave} или кнопка «На штурм» у часов. {Summon} вызывает " +
            "ватагу орды под курсором, Shift+{Summon} — тяжёлую."), Ornament.Serif, 14, Ornament.Parchment, wrap: true);
        about.HorizontalAlignment = HorizontalAlignment.Center;
        about.CustomMinimumSize = new Vector2(420, 0);
        about.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(about);
        (string name, string note, bool open)[] kinds =
        {
            ("Всё открыто", "Всё изучено, запасы не кончаются, заклинания бесплатные, цитадель не падает. Волны идут и после двенадцатой.", true),
            ("Своими силами", "Обычная осада: изучать, добывать и строить самим, цитадель может пасть. Стоят только часы.", false),
        };
        foreach (var (name, note, open) in kinds)
        {
            _page.AddChild(Big(name, name + "\n" + note, () => Sandbox(open)));
            var line = Hud.Text(note, Ornament.SerifItalic, 12, Ornament.Aged, wrap: true);
            line.HorizontalAlignment = HorizontalAlignment.Center;
            line.CustomMinimumSize = new Vector2(320, 0);
            line.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            _page.AddChild(line);
        }
        _page.AddChild(new Rule(false));
        _page.AddChild(Big("Назад", "", Main_));
    }

    /// <summary>The sandbox begun: on the title in the valley under it; in play the scene begins again.</summary>
    void Sandbox(bool open)
    {
        if (_title) Begin(() => _m.StartSandbox(open));
        else _m.NewSandbox(open);
    }

    void Begin(Action start)
    {
        _title = false;
        Close();
        _m.Paused = false;
        _m.Hud.EndTitle();
        start();
    }

    bool _onMain;

    void Clear()
    {
        foreach (var c in _page.GetChildren()) c.QueueFree();
        // a page put in place of another fades in where it stands
        if (Visible) Juice.FadeIn(_page, .18);
        _capture = null;
        Fit(W, H);
    }

    static Button Big(string text, string tip, Action act, bool enabled = true)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(280, 38), TooltipText = Bindings.Fill(tip), Disabled = !enabled,
            MouseDefaultCursorShape = CursorShape.PointingHand, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        b.AddThemeFontOverride("font", Ornament.SerifBold);
        b.AddThemeFontSizeOverride("font_size", 16);
        b.Pressed += act;
        return b;
    }

    void Main_()
    {
        _onMain = true;
        Clear();
        PageName = "main";
        if (_title)
        {
            Fit(W, 592);
            var sub = Hud.Text("Оборона крепости в долине из вокселей", Ornament.SerifItalic, 15, Ornament.Aged);
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            _page.AddChild(sub);
            _page.AddChild(Big("Новая игра", "12 волн орды, по одной каждые 10 минут.", Hardness_));
            _page.AddChild(Big("Кампания", CampaignTip, Campaign_));
            _page.AddChild(Big("Песочница", "Орда приходит, только когда её позовёте.", Sandbox_));
            _page.AddChild(Big("Обучение", "Пошаговое знакомство с игрой.", () => Begin(() => _m.StartTutorial())));
            _page.AddChild(Big("Загрузить игру", "Ячейки, быстрое сохранение и автосохранение после каждой волны.", Loading));
            _page.AddChild(Big("Управление", "Все клавиши. Любую можно переназначить.", Controls_));
            _page.AddChild(Big("Настройки", "Интерфейс, звук, графика.", Settings_));
            _page.AddChild(Big("Отчёт об ошибке", "Собрать логи игры в файл, чтобы прислать автору.", Report_));
            _page.AddChild(Big("Выйти из игры", "", () => _m.GetTree().Quit()));
            var credits = Hud.Text("Значки: game-icons.net, CC BY 3.0 · шрифты PT Serif и Cormorant Garamond, OFL", Ornament.Serif, 11, Ornament.Fade(Ornament.Aged, .8f));
            credits.HorizontalAlignment = HorizontalAlignment.Center;
            _page.AddChild(credits);
            return;
        }
        Fit(W, H + 48);
        _page.AddChild(Big("Продолжить", "Вернуться к игре (Esc).", Close));
        if (Main.Atlas != null)
            _page.AddChild(Big("Сохранить кампанию", $"В её ячейку: {Saves.CampaignName(Main.CampSlot)}. То же делает {{QuickSave}}.", () =>
            {
                if (_m.SaveCampaign()) _m.Hud.Float("кампания сохранена", Ornament.Moss);
            }));
        else _page.AddChild(Big("Сохранить игру", "Три ячейки. Быстрое сохранение — {QuickSave}.", Saving));
        _page.AddChild(Big("Загрузить игру", "Ячейки, быстрое сохранение ({QuickLoad}) и автосохранение после каждой волны.", Loading));
        _page.AddChild(Big("Управление", "Все клавиши. Любую можно переназначить ({Help}).", Controls_));
        _page.AddChild(Big("Настройки", "Интерфейс, звук, графика.", Settings_));
        _page.AddChild(Big("Новая игра", "Несохранённый прогресс пропадёт.", Hardness_));
        _page.AddChild(Big("Кампания", CampaignTip + " Несохранённый прогресс пропадёт.", Campaign_));
        _page.AddChild(Big("Песочница", "Орда приходит, только когда её позовёте. Несохранённый прогресс пропадёт.", Sandbox_));
        _page.AddChild(Big(_m.Tutor is { Over: false } ? "Обучение сначала" : "Обучение", "Пошаговое знакомство с игрой. Несохранённый прогресс пропадёт.", () => _m.NewGame(tutorial: true)));
        _page.AddChild(Big("Отчёт об ошибке", "Собрать логи игры в файл, чтобы прислать автору.", Report_));
        _page.AddChild(Big("Выйти из игры", "Несохранённый прогресс пропадёт. Автосохранение делается после каждой волны.", () => _m.GetTree().Quit()));
    }

    const string CampaignTip = "Большая карта, как в «Героях»: от 36×36 до 144×144 клеток. Герои ходят по ней, поднимают флаги на рудниках, берут сокровища и идут на города орды.";

    /// <summary>
    /// How big a new campaign's map is: a big map of so many hexes a side (S 36, M 72, L 108, XL 144), or (0) a small test
    /// map of 3 rings or 2. Kept while the game runs.
    /// </summary>
    static int _side = 72, _rings = 3;
    /// <summary>How hard the horde plays a new campaign.</summary>
    static Difficulty _level = Difficulty.Normal;

    /// <summary>The campaigns' slots: a new campaign in one (what was in it gone), or one read back where it was left.</summary>
    void Campaign_()
    {
        _onMain = false;
        Clear();
        PageName = "campaign";
        Fit(W, 450);
        _page.AddChild(Hud.CapsLabel("Кампания", 10));
        _page.AddChild(Para("Большая карта, как в «Героях»: каждая клетка — долина. Карта поделена на зоны, между ними перевалы под стражей. " +
            "У героя ход на день; рудники платят каждое утро. Карта открывается клавишей {Board}.", Ornament.Parchment));
        // the size of a new campaign's map
        var size = new HBoxContainer();
        size.AddThemeConstantOverride("separation", 8);
        size.AddChild(Hud.Text("Новая карта:", Ornament.Serif, 13, Ornament.Aged));
        foreach (var (side, rings, name, tip) in new[]
        {
            (36, 0, "S", "36×36 клеток, 1 держава орды. Короткая кампания."), (72, 0, "M", "72×72 клетки, 2 державы орды."),
            (108, 0, "L", "108×108 клеток, 3 державы орды."), (144, 0, "XL", "144×144 клетки, 4 державы орды. Долгая кампания."),
            (0, 3, "Тест · 37", "Прежняя маленькая карта из 37 долин, для проверки."), (0, 2, "Тест · 19", "Прежняя маленькая карта из 19 долин, для проверки."),
        })
        {
            int sd = side, r = rings;
            var b = new Button { Text = name, FocusMode = FocusModeEnum.None, ToggleMode = true, ButtonPressed = _side == sd && (sd > 0 || _rings == r), TooltipText = tip,
                MouseDefaultCursorShape = CursorShape.PointingHand, CustomMinimumSize = new Vector2(sd > 0 ? 48 : 96, 30) };
            b.AddThemeFontSizeOverride("font_size", 13);
            b.Pressed += () => { _side = sd; if (r > 0) _rings = r; Campaign_(); };
            size.AddChild(b);
        }
        _page.AddChild(size);
        // how hard the horde plays
        var hard = new HBoxContainer();
        hard.AddThemeConstantOverride("separation", 8);
        hard.AddChild(Hud.Text("Орда:", Ornament.Serif, 13, Ornament.Aged));
        foreach (var (level, tip) in new[]
        {
            (Difficulty.Easy, "Орда богатеет медленнее, оставляет вас в покое 10 дней и идёт на первую долину не чаще раза в 5 дней."),
            (Difficulty.Normal, "Неделя покоя, потом войско орды на первую долину не чаще раза в 4 дня."),
            (Difficulty.Hard, "Орда богаче и растёт быстрее, покой 5 дней, войско на первую долину раз в 3 дня."),
        })
        {
            var lv = level;
            var b = new Button { Text = Core.Campaign.NameOf(lv), FocusMode = FocusModeEnum.None, ToggleMode = true, ButtonPressed = _level == lv, TooltipText = tip,
                MouseDefaultCursorShape = CursorShape.PointingHand, CustomMinimumSize = new Vector2(96, 30) };
            b.AddThemeFontSizeOverride("font_size", 13);
            b.Pressed += () => { _level = lv; Campaign_(); };
            hard.AddChild(b);
        }
        _page.AddChild(hard);
        foreach (string slot in Saves.CampaignSlots)
        {
            string s = slot;
            var peek = Saves.PeekCampaign(s);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            text.AddThemeConstantOverride("separation", 0);
            text.AddChild(Hud.Text(Saves.CampaignName(s), Ornament.SerifBold, 15, Ornament.Parchment));
            text.AddChild(Hud.Text(peek is { } p ? $"День {p.day} · {p.when:dd.MM, HH:mm}" : "пусто", Ornament.Serif, 12, Ornament.Aged));
            row.AddChild(text);
            Button Act(string name, string tip, Action act, bool enabled)
            {
                var b = new Button { Text = name, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(110, 32), Disabled = !enabled,
                    TooltipText = tip, MouseDefaultCursorShape = enabled ? CursorShape.PointingHand : CursorShape.Arrow };
                b.AddThemeFontSizeOverride("font_size", 14);
                b.Pressed += act;
                if (!enabled) b.Modulate = new Color(1, 1, 1, .4f);
                return b;
            }
            row.AddChild(Act("Новая", peek != null ? "Новая кампания в этой ячейке. Прежняя пропадёт." : "Новая кампания в этой ячейке.",
                () => Main.NewCampaign(_m.GetTree(), s, (uint)Random.Shared.Next(1, int.MaxValue), _rings, _side, _level), true));
            row.AddChild(Act("Продолжить", "С того места, где кампания сохранена.", () =>
            {
                if (!Main.LoadCampaign(_m.GetTree(), s)) _m.Hud.Float("эту кампанию не прочесть", Ornament.Blood);
            }, peek != null));
            _page.AddChild(row);
        }
        _page.AddChild(new Rule(false));
        _page.AddChild(Big("Назад", "", Main_));
    }

    /// <summary>The report of what went wrong: one text file on the desktop, with the logs, for the author.</summary>
    void Report_()
    {
        _onMain = false;
        Clear();
        PageName = "report";
        _page.AddChild(Hud.CapsLabel("Отчёт об ошибке", 10));
        if (Diag.LastEndedBadly) _page.AddChild(Para("Прошлый раз игра закрылась аварийно: зависла или упала. Отчёт поможет найти причину.", Ornament.EmberGlow));
        _page.AddChild(Para("В файл попадут логи игры, записи о зависаниях и сведения о компьютере и видеокарте. " +
            "Он сохранится на рабочий стол. Пришлите его автору.", Ornament.Parchment));
        var done = Para("", Ornament.Moss);
        _page.AddChild(Big("Создать отчёт", "", () =>
        {
            try
            {
                string path = Diag.Report(_m);
                done.Text = $"Готово: {path}";
                OS.ShellShowInFileManager(path);
            }
            catch (Exception e) { done.Text = $"Не получилось: {e.Message}"; done.AddThemeColorOverride("font_color", Ornament.Blood); }
        }));
        _page.AddChild(done);
        _page.AddChild(new Rule(false));
        _page.AddChild(Big("Назад", "", Main_));
    }

    static Label Para(string text, Color color)
    {
        var l = Hud.Text(Bindings.Fill(text), Ornament.Serif, 14, color, wrap: true);
        l.CustomMinimumSize = new Vector2(W - 90, 0);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        return l;
    }

    /// <summary>"Перед волной 3 · 22:40 игры · 18.10, 14:32", or "пусто", or what an earlier version of the game left.</summary>
    static string About(string slot)
    {
        var p = Saves.Peek(slot);
        return p is { } v ? $"{v.label} · {Fmt.Clock(v.time)} игры · {v.when:dd.MM, HH:mm}" : Saves.Stale(slot) ? "от прежней версии игры, не загружается" : "пусто";
    }

    Control Row(string slot, string button, Action act, bool enabled)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        text.AddChild(Hud.Text(Saves.NameOf(slot), Ornament.SerifBold, 15, Ornament.Parchment));
        text.AddChild(Hud.Text(About(slot), Ornament.Serif, 12, Ornament.Aged));
        row.AddChild(text);
        var b = new Button { Text = button, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 32), Disabled = !enabled, MouseDefaultCursorShape = CursorShape.PointingHand };
        b.AddThemeFontSizeOverride("font_size", 14);
        b.Pressed += act;
        // nothing there to read back: the button only shows faintly
        if (!enabled) { b.Modulate = new Color(1, 1, 1, .4f); b.MouseDefaultCursorShape = CursorShape.Arrow; }
        row.AddChild(b);
        return row;
    }

    void Saving()
    {
        _onMain = false;
        Clear();
        PageName = "save";
        _page.AddChild(Hud.CapsLabel("Сохранить игру", 10));
        foreach (string slot in Saves.Slots)
        {
            string s = slot;
            _page.AddChild(Row(s, "Сохранить", () =>
            {
                if (Saves.Write(_m, s)) _m.Hud.Float($"сохранено: {Saves.NameOf(s).ToLowerInvariant()}", Ornament.Moss);
                Saving();
            }, true));
        }
        _page.AddChild(new Rule(false));
        _page.AddChild(Big("Назад", "", Main_));
    }

    void Loading()
    {
        _onMain = false;
        Clear();
        PageName = "load";
        _page.AddChild(Hud.CapsLabel("Загрузить игру", 10));
        foreach (string slot in new[] { Saves.Auto, Saves.Quick, Saves.Slots[0], Saves.Slots[1], Saves.Slots[2] })
        {
            string s = slot;
            _page.AddChild(Row(s, "Загрузить", () => Saves.Read(_m, s), Saves.Peek(s) != null));
        }
        _page.AddChild(new Rule(false));
        _page.AddChild(Big("Назад", "", Main_));
    }

    void Settings_()
    {
        _onMain = false;
        Clear();
        PageName = "settings";
        _page.AddChild(Hud.CapsLabel("Настройки", 10));
        var scale = new Stepper("Размер интерфейса", "Размер интерфейса\nВ игре то же делают Ctrl и + или −, Ctrl+0 — обычный размер.",
            () => $"{Math.Round(Settings.UiScale * 100)} %", d => Settings.Step(_m.GetTree().Root, d));
        scale.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(scale);
        var volume = new Stepper("Громкость", "Громкость\nГромче слышно то, что ближе к центру экрана.", () => Settings.Volume <= 0 ? "без звука" : $"{Math.Round(Settings.Volume * 100)} %", d => Settings.StepVolume(d));
        volume.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(volume);
        var shake = new Stepper("Тряска и вспышки", "Тряска и вспышки\nТряска экрана от взрывов и обвалов, вспышки от больших взрывов и молний.",
            () => Settings.Shake <= 0 ? "нет" : Settings.Shake < 1 ? "слабая" : "сильная", d => Settings.StepShake(d));
        shake.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(shake);
        var slow = new Stepper("Замедление", "Замедление\nКороткое замедление времени после большого взрыва или обвала.", () => Settings.SlowMo ? "да" : "нет", d => Settings.ToggleSlowMo());
        slow.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(slow);
        var edge = new Stepper("Прокрутка у края", "Прокрутка у края\nВид сдвигается, когда мышь у края экрана. В окне это может мешать.", () => Settings.EdgeScroll ? "да" : "нет", d => Settings.ToggleEdgeScroll());
        edge.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(edge);
        var hints = new Stepper("Подсказки внизу", "Подсказки внизу\nСтрока над нижней панелью: что сейчас делают кнопки мыши и клавиши.", () => Settings.Hints ? "да" : "нет", d => Settings.ToggleHints());
        hints.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(hints);
        var advice = new Stepper("Советы", "Советы\nНад картой: что сейчас не так и что с этим сделать. Щелчок по совету делает это, крестик прячет его на 5 минут. " +
            "Ещё карточка, когда впервые берёте инструмент или открываете вкладку.", () => Settings.Advice ? "да" : "нет", d => Settings.ToggleAdvice());
        advice.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(advice);
        var unfold = new Stepper("Инструменты", "Инструменты\nПостепенно: в начале осады видны главные инструменты и вкладки, остальные появляются, когда становятся нужны. " +
            "Все сразу: всё с самого начала. Если выбрать «все сразу» во время игры, всё откроется сейчас.",
            () => Settings.Unfold ? "постепенно" : "все сразу",
            d => { Settings.ToggleUnfold(); if (!Settings.Unfold) _m.Hud.Unfold.OpenAll(); });
        unfold.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(unfold);
        var captain = new Stepper("Командир в начале", "Командир в начале\nВ новой осаде с рабочими приходит командир. Он сам строит бойцов к бою. " +
            "В него можно вселиться ({Possess}) и командовать от первого лица.", () => Settings.StartCaptain ? "да" : "нет", d => Settings.ToggleStartCaptain());
        captain.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(captain);
        var lines = new Stepper("Обводка вокселей", "Обводка вокселей\nТонкая тёмная линия по краям вокселей вблизи.", () => Settings.Lines ? "да" : "нет", d => Settings.ToggleLines());
        lines.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(lines);
        _page.AddChild(new Rule(false));
        _page.AddChild(Big("Графика", "Графика\nТени, сглаживание, чёткость, трава.", Picture_));
        _page.AddChild(Big("Назад", "", Main_));
    }

    void Picture_()
    {
        _onMain = false;
        Clear();
        PageName = "graphics";
        _page.AddChild(Hud.CapsLabel("Графика", 10));
        void Row(string name, string tip, Func<string> value, Action<int> step, bool whole = false)
        {
            // the whole picture changes every part of it: the page is laid out again to show them
            var st = new Stepper(name, name + "\n" + tip, value, d => { step(d); Graphics.Apply(_m); if (whole) Picture_(); else Settings_Refresh(); });
            st.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            _page.AddChild(st);
        }
        Row("Качество", "Общая настройка. Низкое — для слабого ноутбука, среднее — для встроенной видеокарты, высокое — обычное, " +
            "наивысшее — для мощного компьютера. Ниже можно изменить отдельные параметры.", () => Settings.Qualities[Settings.Quality], Settings.StepQuality, whole: true);
        _page.AddChild(new Rule(false));
        Row("Тени", "Тени от солнца и луны. Сильнее всего влияют на скорость.",
            () => Settings.Shadows switch { 0 => "нет", 1 => "простые", 2 => "мягкие", _ => "мягчайшие" }, Settings.StepShadows);
        Row("Сглаживание", "Сглаживание краёв. FXAA быстрее, но слегка размывает.",
            () => Settings.Smoothing switch { 0 => "нет", 1 => "FXAA", 2 => "MSAA 2×", _ => "MSAA 4×" }, Settings.StepSmoothing);
        Row("Чёткость", "Разрешение, в котором рисуется мир. Меньше — быстрее, картинку растягивает FSR. Интерфейс всегда чёткий.", () => $"{Math.Round(Settings.Details[Settings.Detail] * 100)} %", Settings.StepDetail);
        Row("Трава", "Травинки на лугах: нет, только вблизи или везде.",
            () => Settings.Grass switch { 0 => "нет", 1 => "вблизи", _ => "вся" }, Settings.StepGrass);
        Row("Затенение в углах", "Мягкая тень в углах и щелях (SSAO).",
            () => Settings.Occlusion ? "да" : "нет", _ => Settings.ToggleOcclusion());
        Row("Свечение", "Свечение огня, факелов и заклинаний.", () => Settings.Glow ? "да" : "нет", _ => Settings.ToggleGlow());
        Row("Счётчик кадров", "Кадры в секунду в левом верхнем углу.",
            () => Settings.ShowFps ? "да" : "нет", _ => Settings.ToggleFps());
        _page.AddChild(new Rule(false));
        _page.AddChild(Big("Назад", "", Settings_));
    }

    /// <summary>One part of the picture changed: the whole's name may have changed with it.</summary>
    void Settings_Refresh() => Picture_();
}
