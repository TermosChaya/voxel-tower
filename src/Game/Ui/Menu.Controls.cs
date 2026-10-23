using System;
using System.Collections.Generic;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The page of the controls: every command of the game by its group — the camera, the brushes, the material and the
/// brush's settings, the household and the orders, the time and the game, the eyes of one gone into — with what it
/// does and the key it is on, and what the mouse does beside them, its two buttons swapped if the player likes. A click
/// on a key and then another key puts the command there; the command that had that key takes the old one. Hovering a
/// row tells it in full.
/// </summary>
public partial class GameMenu
{
    /// <summary>The command waiting for its new key, while the page listens for one.</summary>
    Act? _capture;
    readonly Dictionary<Act, (Button key, Label alt)> _keyButtons = new();
    Label _note;
    Button _reset;

    /// <summary>A row of the page that tells itself in full when hovered.</summary>
    partial class TipRow : HBoxContainer
    {
        public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
    }

    const string Intro = "Чтобы переназначить команду, щёлкните по её клавише и нажмите новую.";

    void Controls_()
    {
        _onMain = false;
        Clear();
        PageName = "controls";
        Fit(1040, 760);
        _keyButtons.Clear();
        _page.AddChild(Hud.CapsLabel("Управление", 10));
        var intro = Hud.Text(Intro, Ornament.SerifItalic, 13, Ornament.Aged, wrap: true);
        _page.AddChild(intro);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _page.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);
        list.AddChild(MouseRow());
        foreach (string group in Bindings.Groups)
        {
            var head = Hud.CapsLabel(group, 11, Ornament.BronzeLight);
            head.CustomMinimumSize = new Vector2(0, 30);
            head.VerticalAlignment = VerticalAlignment.Bottom;
            list.AddChild(head);
            list.AddChild(new Rule(false));
            foreach (var d in Bindings.Defs) if (d.Group == group) list.AddChild(CommandRow(d));
            foreach (var (g, what, about) in Bindings.Fixed) if (g == group) list.AddChild(FixedRow(what, about));
        }
        _page.AddChild(new Rule(false));
        _note = Hud.Text("", Ornament.SerifItalic, 13, Ornament.BronzeLight, wrap: true);
        _page.AddChild(_note);
        var foot = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        foot.AddThemeConstantOverride("separation", 16);
        _reset = Big("Вернуть все клавиши", "Вернуть клавиши по умолчанию.", () =>
        {
            Bindings.Reset();
            _capture = null;
            Relabel();
            _note.Text = "Клавиши сброшены.";
        });
        foot.AddChild(_reset);
        foot.AddChild(Big("Назад", "", Main_));
        _page.AddChild(foot);
        Relabel();
    }

    /// <summary>Hud.ToolDefs and MatDefs tell the tools and the materials in full; the rest tell what the page says.</summary>
    static string FullTip(Bindings.Def d)
    {
        foreach (var t in Hud.ToolDefs) if (t.act == d.Act) return t.tip;
        foreach (var m in Hud.MatDefs) if (m.act == d.Act) return m.tip;
        if (d.Act == Act.Pick) return Hud.PickTip;
        return d.About == "" ? d.Name : $"{d.Name}\n{d.About}";
    }

    Control CommandRow(Bindings.Def d)
    {
        var row = new TipRow { TooltipText = FullTip(d), MouseFilter = MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", 12);
        var name = Hud.Text(d.Name, Ornament.SerifBold, 14, Ornament.Parchment);
        name.CustomMinimumSize = new Vector2(220, 0);
        name.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(name);
        var about = Hud.Text(Bindings.Fill(d.About), Ornament.Serif, 13, Ornament.Aged, wrap: true);
        about.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        about.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(about);
        var key = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(118, 30), MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = "Щёлкните и нажмите новую клавишу." };
        key.AddThemeFontOverride("font", Ornament.SerifBold);
        key.AddThemeFontSizeOverride("font_size", 15);
        var act = d.Act;
        key.Pressed += () => Listen(act);
        row.AddChild(key);
        var alt = Hud.Text("", Ornament.Serif, 12, Ornament.Faded);
        alt.CustomMinimumSize = new Vector2(64, 0);
        alt.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(alt);
        _keyButtons[act] = (key, alt);
        return row;
    }

    Control FixedRow(string what, string about)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var name = Hud.Text("", Ornament.SerifBold, 14, Ornament.Parchment);
        name.CustomMinimumSize = new Vector2(220, 0);
        row.AddChild(name);
        var text = Hud.Text(Bindings.Fill(about), Ornament.Serif, 13, Ornament.Aged, wrap: true);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(text);
        // the mouse and the held keys are no keys to put elsewhere: told in the key's place, not on a button
        var key = Hud.Text(Bindings.Fill(what), Ornament.SerifBold, 13, Ornament.BronzeLight);
        key.CustomMinimumSize = new Vector2(118 + 12 + 64, 30);
        key.HorizontalAlignment = HorizontalAlignment.Left;
        key.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(key);
        return row;
    }

    /// <summary>The mouse's two buttons, as they are or swapped: the page is drawn again with them, every row saying them anew.</summary>
    Control MouseRow()
    {
        var row = new TipRow { TooltipText = "Кнопки мыши\nПоменять местами левую и правую кнопку на поле и от первого лица. Панели и меню работают от левой.",
            MouseFilter = MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", 12);
        var name = Hud.Text("Кнопки мыши", Ornament.SerifBold, 14, Ornament.Parchment);
        name.CustomMinimumSize = new Vector2(220, 0);
        name.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(name);
        var about = Hud.Text(Bindings.Fill("{Lmb} выбирает людей, рисует и бьёт. {Rmb} посылает, убирает кисть, двигает вид и кладёт блок."),
            Ornament.Serif, 13, Ornament.Aged, wrap: true);
        about.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        about.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(about);
        var swap = new Button { Text = Bindings.SwapMouse ? "Поменяны" : "Как обычно", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(118 + 12 + 64, 30),
            MouseDefaultCursorShape = CursorShape.PointingHand, ToggleMode = true, ButtonPressed = Bindings.SwapMouse };
        swap.AddThemeFontOverride("font", Ornament.SerifBold);
        swap.AddThemeFontSizeOverride("font_size", 15);
        swap.Pressed += () =>
        {
            Bindings.SetSwapMouse(!Bindings.SwapMouse);
            Controls_();
            _note.Text = Bindings.SwapMouse ? "Кнопки мыши поменяны: правая выбирает и рисует, левая посылает." : "Кнопки мыши как обычно.";
        };
        row.AddChild(swap);
        return row;
    }

    /// <summary>The keys on their buttons as they are bound now; the one waiting for its key says so.</summary>
    void Relabel()
    {
        foreach (var (act, (key, alt)) in _keyButtons)
        {
            key.Text = _capture == act ? "…" : Bindings.Label(act);
            string second = Bindings.AltLabel(act);
            alt.Text = second.Length > 0 ? "или " + second : "";
        }
        if (_reset != null) _reset.Disabled = !Bindings.Customised;
    }

    void Listen(Act act)
    {
        _capture = act;
        _note.Text = $"Нажмите клавишу для «{Bindings.Of(act).Name}». Esc — отмена.";
        Relabel();
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || _capture is not { } act || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        GetViewport().SetInputAsHandled();
        var key = k.PhysicalKeycode;
        _capture = null;
        if (key == Key.Escape) { _note.Text = "Клавиша осталась прежней."; Relabel(); return; }
        if (Bindings.Refused(act, key) is { } why) { _note.Text = why + "."; Relabel(); return; }
        var name = Bindings.Of(act).Name;
        var other = Bindings.Bind(act, key);
        _note.Text = other is { } o
            ? $"«{name}»: {Bindings.Label(act)}. «{Bindings.Of(o).Name}» перенесена на {Bindings.Label(o)}."
            : $"«{name}»: {Bindings.Label(act)}.";
        Relabel();
    }
}
