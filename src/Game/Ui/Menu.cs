using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The game's own menu, over a darkened field: go on, save into a slot, read one back (a slot, the quick save, the
/// autosave), the controls, the settings, a new siege, the tutorial, quit. At the start of the game it is the title:
/// a new siege, the tutorial, a saved game, the controls, the settings, quit. The siege stands still while it is open. Esc or the medallion by the
/// clock opens it; the help medallion and {Help} open it on the controls.
/// </summary>
public partial class GameMenu : Control
{
    Main _m;
    VBoxContainer _page;
    Control _panel;
    bool _wasPaused;
    const float W = 600, H = 560;

    public void Init(Main m)
    {
        _m = m;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var dim = new ColorRect { Color = new Color(0, 0, 0, .55f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var panel = new BottomConsole.Plate(c => Craft.Framed(c, new Rect2(Vector2.Zero, c.Size), 12)) { MouseFilter = MouseFilterEnum.Stop };
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
        var title = Hud.Text("Сумрачный вал", Ornament.Display, 30, Ornament.EmberGlow);
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
        if (Visible) return;
        _wasPaused = _m.Paused;
        _m.Paused = true;
        Visible = true;
        switch (page)
        {
            case "save": Saving(); break;
            case "load": Loading(); break;
            case "settings": Settings_(); break;
            case "hardness": Hardness_(); break;
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
            "Ватаги меньше, налётчики слабее и бьют мягче, цитадель держится дольше. Чтобы спокойно освоиться.",
            "Осада, как она задумана.",
            "Ватаги больше, налётчики крепче и бьют сильнее. Для тех, кто уже выстоял.",
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
        if (_title) { _m.Sim.SetHardness(h); Begin(false); return; }
        Main.PendingHardness = h;
        _m.NewGame();
    }

    void Begin(bool tutorial)
    {
        _title = false;
        Close();
        _m.Paused = false;
        _m.Hud.EndTitle();
        if (tutorial) _m.StartTutorial(); else _m.Welcome();
    }

    bool _onMain;

    void Clear()
    {
        foreach (var c in _page.GetChildren()) c.QueueFree();
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
            Fit(W, 452);
            var sub = Hud.Text("Оборона крепости в долине из вокселей", Ornament.SerifItalic, 15, Ornament.Aged);
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            _page.AddChild(sub);
            _page.AddChild(Big("Новая игра", "Осада: двенадцать волн орды, раз в десять минут. Старая крепость вокруг цитадели наполовину разрушена.", Hardness_));
            _page.AddChild(Big("Обучение", "Отдельный режим: шаг за шагом, за руку — через всё, что умеет игра. Орда не придёт, пока не дойдёт до неё урок.", () => Begin(true)));
            _page.AddChild(Big("Загрузить игру", "Продолжить сохранённую игру: из ячейки, быстрого сохранения или автосохранения после отбитой волны.", Loading));
            _page.AddChild(Big("Управление", "Все клавиши и кнопки мыши и что они делают; любую клавишу можно переназначить.", Controls_));
            _page.AddChild(Big("Настройки", "Размер интерфейса, громкость, тряска, замедление, обводка вокселей.", Settings_));
            _page.AddChild(Big("Выйти из игры", "", () => _m.GetTree().Quit()));
            var credits = Hud.Text("Значки: game-icons.net, CC BY 3.0 · шрифты PT Serif и Cormorant Garamond, OFL", Ornament.Serif, 11, Ornament.Fade(Ornament.Aged, .8f));
            credits.HorizontalAlignment = HorizontalAlignment.Center;
            _page.AddChild(credits);
            return;
        }
        _page.AddChild(Big("Продолжить", "Вернуться к осаде (Esc).", Close));
        _page.AddChild(Big("Сохранить игру", "Записать игру в одну из трёх ячеек. Быстро — {QuickSave}.", Saving));
        _page.AddChild(Big("Загрузить игру", "Продолжить сохранённую игру: из ячейки, быстрого сохранения ({QuickLoad}) или автосохранения после отбитой волны.", Loading));
        _page.AddChild(Big("Управление", "Все клавиши и кнопки мыши и что они делают; любую клавишу можно переназначить ({Help}).", Controls_));
        _page.AddChild(Big("Настройки", "Размер интерфейса, громкость, тряска, замедление, обводка вокселей.", Settings_));
        _page.AddChild(Big("Новая игра", "Начать новую осаду. Несохранённое здесь пропадёт.", Hardness_));
        _page.AddChild(Big(_m.Tutor is { Over: false } ? "Обучение сначала" : "Обучение", "Отдельный режим: за руку через всё, что умеет игра. Несохранённое здесь пропадёт.", () => _m.NewGame(tutorial: true)));
        _page.AddChild(Big("Выйти из игры", "Закрыть игру. Несохранённое пропадёт (автосохранение пишется после каждой отбитой волны).", () => _m.GetTree().Quit()));
    }

    /// <summary>"Перед волной 3 · 22:40 игры · 18.10, 14:32", or "пусто".</summary>
    static string About(string slot)
    {
        var p = Saves.Peek(slot);
        return p is { } v ? $"{v.label} · {Fmt.Clock(v.time)} игры · {v.when:dd.MM, HH:mm}" : "пусто";
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
        var scale = new Stepper("Размер интерфейса", "Размер интерфейса\nКрупнее или мельче балка, консоль, панели и подсказки. Ctrl и + или − в игре — то же, Ctrl+0 — как был.",
            () => $"{Math.Round(Settings.UiScale * 100)} %", d => Settings.Step(_m.GetTree().Root, d));
        scale.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(scale);
        var volume = new Stepper("Громкость", "Громкость\nКак громко слышна долина: кирки, тетивы, мечи, рог, огонь, дождь, заклинания. Звуки делает сама игра; " +
            "слышно то, что ближе к середине экрана.", () => Settings.Volume <= 0 ? "без звука" : $"{Math.Round(Settings.Volume * 100)} %", d => Settings.StepVolume(d));
        volume.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(volume);
        var shake = new Stepper("Тряска и вспышки", "Тряска и вспышки\nКак сильно взрыв, обвал или тяжёлое падение рядом встряхивают вид, и вспыхивает ли " +
            "экран от большого взрыва или молнии: никак, слабо или в полную силу.",
            () => Settings.Shake <= 0 ? "нет" : Settings.Shake < 1 ? "слабая" : "сильная", d => Settings.StepShake(d));
        shake.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(shake);
        var slow = new Stepper("Замедление", "Замедление\nПосле большого взрыва или обвала рядом с серединой вида время на миг замедляется, чтобы разглядеть, " +
            "как летят обломки, — и возвращается само.", () => Settings.SlowMo ? "да" : "нет", d => Settings.ToggleSlowMo());
        slow.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(slow);
        var edge = new Stepper("Прокрутка у края", "Прокрутка у края\nПодведите мышь к краю экрана — вид поедет в ту сторону, как от WASD. " +
            "Если мешает (например, в окне), выключите.", () => Settings.EdgeScroll ? "да" : "нет", d => Settings.ToggleEdgeScroll());
        edge.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(edge);
        var lines = new Stepper("Обводка вокселей", "Обводка вокселей\nТонкая тёмная линия по краю каждого вокселя, когда он на экране достаточно крупный, " +
            "чтобы её было видно: мир смотрится сложенным из кубиков. Без неё земля, камень и дерево глаже, а трава, листва и кора " +
            "всё равно прорисованы мельче вокселей.", () => Settings.Lines ? "да" : "нет", d => Settings.ToggleLines());
        lines.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(lines);
        _page.AddChild(new Rule(false));
        _page.AddChild(Big("Графика", "Графика\nКачество картинки: тени, сглаживание, чёткость, трава, — для слабого или сильного компьютера.", Picture_));
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
        Row("Качество", "Всё сразу: низкое — для слабого ноутбука (без теней и травы, мир рисуется с меньшей чёткостью), " +
            "среднее — для встроенной видеокарты, высокое — как задумано, наивысшее — мягчайшие тени, четырёхкратное сглаживание " +
            "и затенение в углах. Ниже можно поменять что-то одно.", () => Settings.Qualities[Settings.Quality], Settings.StepQuality, whole: true);
        _page.AddChild(new Rule(false));
        Row("Тени", "Тени от солнца и луны: нет, простые (с резким краем), мягкие, мягчайшие. Самое дорогое в картинке.",
            () => Settings.Shadows switch { 0 => "нет", 1 => "простые", 2 => "мягкие", _ => "мягчайшие" }, Settings.StepShadows);
        Row("Сглаживание", "Сглаживание лесенки по краям: нет, быстрое (FXAA, чуть размывает), двойное и четверное (MSAA).",
            () => Settings.Smoothing switch { 0 => "нет", 1 => "FXAA", 2 => "MSAA 2×", _ => "MSAA 4×" }, Settings.StepSmoothing);
        Row("Чёткость", "С какой долей точек экрана рисуется мир (интерфейс — всегда в полную): меньше — быстрее, а до экрана " +
            "картинку дотягивает FSR.", () => $"{Math.Round(Settings.Details[Settings.Detail] * 100)} %", Settings.StepDetail);
        Row("Трава", "Травинки на лугах: нет (луг остаётся цветным), только вблизи, вся.",
            () => Settings.Grass switch { 0 => "нет", 1 => "вблизи", _ => "вся" }, Settings.StepGrass);
        Row("Затенение в углах", "Мягкая тень в углах и щелях, у подножия стен и между вокселями (SSAO).",
            () => Settings.Occlusion ? "да" : "нет", _ => Settings.ToggleOcclusion());
        Row("Свечение", "Ореол вокруг огня, факелов, искр и заклинаний.", () => Settings.Glow ? "да" : "нет", _ => Settings.ToggleGlow());
        Row("Счётчик кадров", "Сколько кадров в секунду и самый долгий из последних — мелко в левом верхнем углу.",
            () => Settings.ShowFps ? "да" : "нет", _ => Settings.ToggleFps());
        _page.AddChild(new Rule(false));
        _page.AddChild(Big("Назад", "", Settings_));
    }

    /// <summary>One part of the picture changed: the whole's name may have changed with it.</summary>
    void Settings_Refresh() => Picture_();
}
