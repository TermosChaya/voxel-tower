using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The game's own menu, over a darkened field: go on, save into a slot, read one back (a slot, the quick save, the
/// autosave), the settings, quit. The siege stands still while it is open. Esc or the medallion by the clock opens it.
/// </summary>
public partial class GameMenu : Control
{
    Main _m;
    VBoxContainer _page;
    bool _wasPaused;
    const float W = 600, H = 470;

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
        panel.OffsetLeft = -W / 2; panel.OffsetRight = W / 2; panel.OffsetTop = -H / 2; panel.OffsetBottom = H / 2;
        AddChild(panel);
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
            default: Main_(); break;
        }
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
        if (_onMain) Close(); else Main_();
    }

    bool _onMain;

    void Clear()
    {
        foreach (var c in _page.GetChildren()) c.QueueFree();
    }

    static Button Big(string text, string tip, Action act, bool enabled = true)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(280, 38), TooltipText = tip, Disabled = !enabled,
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
        _page.AddChild(Big("Продолжить", "Вернуться к осаде (Esc).", Close));
        _page.AddChild(Big("Сохранить игру", "Записать игру в одну из трёх ячеек. Быстро — F5.", Saving));
        _page.AddChild(Big("Загрузить игру", "Продолжить сохранённую игру: из ячейки, быстрого сохранения (F9) или автосохранения после отбитой волны.", Loading));
        _page.AddChild(Big("Настройки", "Размер интерфейса.", Settings_));
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
        _page.AddChild(Hud.CapsLabel("Настройки", 10));
        var scale = new Stepper("Размер интерфейса", "Размер интерфейса\nКрупнее или мельче балка, консоль, панели и подсказки. Ctrl и + или − в игре — то же, Ctrl+0 — как был.",
            () => $"{Math.Round(Settings.UiScale * 100)} %", d => Settings.Step(_m.GetTree().Root, d));
        scale.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _page.AddChild(scale);
        var note = Hud.Text("Звук и прочие настройки появятся позже.", Ornament.SerifItalic, 12, Ornament.Faded);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        _page.AddChild(note);
        _page.AddChild(new Rule(false));
        _page.AddChild(Big("Назад", "", Main_));
    }
}
