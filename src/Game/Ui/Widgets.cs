using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>A container drawn as a plate, its children well inside its edge.</summary>
public partial class PlatePanel : MarginContainer
{
    public PlatePanel() : this(14) { }

    public PlatePanel(int pad)
    {
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            AddThemeConstantOverride(side, pad);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Draw() => Ornament.Plate(this, new Rect2(Vector2.Zero, Size));
}

/// <summary>Tooltips on a plate: a title in bronze, then the description.</summary>
public static class Tooltip
{
    public static Control Make(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        // the keys named as they are bound now
        text = Bindings.Fill(text);
        var parts = text.Split('\n', 2);
        var panel = new PlatePanel(14);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        var title = new Label { Text = parts[0] };
        title.AddThemeFontOverride("font", Ornament.SerifBold);
        title.AddThemeFontSizeOverride("font_size", 15);
        title.AddThemeColorOverride("font_color", Ornament.BronzeLight);
        box.AddChild(title);
        if (parts.Length > 1)
        {
            var body = new Label { Text = parts[1], AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(280, 0) };
            body.AddThemeFontSizeOverride("font_size", 13);
            body.AddThemeColorOverride("font_color", Ornament.Parchment);
            box.AddChild(body);
        }
        panel.AddChild(box);
        return panel;
    }
}

/// <summary>A sunken well with an engraved icon: a tool on the belt, a material, a blueprint.</summary>
public partial class Slot : Control
{
    readonly Icons.Painter _icon;
    string _key;
    bool _selected, _disabled, _hover, _down, _locked;

    /// <summary>Shut, as the tutorial keeps what it has not come to yet: an empty well that takes no click.</summary>
    public bool Locked { get => _locked; set { if (_locked == value) return; _locked = value; QueueRedraw(); } }

    public string Caption;
    /// <summary>The key's letter in the corner, as the command is bound now.</summary>
    public string Key { get => _key; set { if (_key == value) return; _key = value; QueueRedraw(); } }
    public event Action Clicked;

    public Slot() { }

    public Slot(Icons.Painter icon, string key, string tooltip, float size = 56)
    {
        _icon = icon;
        _key = key;
        TooltipText = tooltip;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; _down = false; QueueRedraw(); };
    }

    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; QueueRedraw(); } }
    bool _fresh;
    /// <summary>Just let out (Ui/Unfold.cs): an ember mark on it till it is first used.</summary>
    public bool Fresh { get => _fresh; set { if (_fresh == value) return; _fresh = value; QueueRedraw(); } }
    public bool Disabled { get => _disabled; set { if (_disabled == value) return; _disabled = value; QueueRedraw(); } }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left) return;
        if (_locked) { AcceptEvent(); return; }
        if (mb.Pressed) _down = true;
        else if (_down) { _down = false; if (_hover && !_disabled && Clicked != null) { Sound.Ui("tick"); Juice.Press(this); _fresh = false; Clicked(); } }
        QueueRedraw();
        AcceptEvent();
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        if (_locked)
        {
            Ornament.Well(this, r, false, false, true);
            Ornament.Diamond(this, Size / 2, Size.X * .08f, Ornament.Fade(Ornament.BronzeDark, .6f));
            return;
        }
        Ornament.Well(this, r, _hover && !_disabled, _selected, _disabled);
        float pad = Size.X * (_down ? .2f : .17f), side = Size.X - 2 * pad;
        var ir = new Rect2(pad, pad, side, side);
        if (Caption != null) { side *= .8f; ir = new Rect2((Size.X - side) / 2, pad * .55f, side, side); }
        var ink = _disabled ? Ornament.Faded : _selected ? Ornament.EmberGlow : _hover ? Ornament.BronzeLight : Ornament.Bronze;
        _icon?.Invoke(this, ir, ink);
        if (!string.IsNullOrEmpty(_key))
            Ornament.Lettering(this, Ornament.Mono, new Vector2(4, 13), _key, 11, _selected ? Ornament.EmberGlow : Ornament.Aged);
        if (Caption != null)
            Ornament.Lettering(this, Ornament.Mono, new Vector2(0, Size.Y - 5), Caption, 10, Ornament.Aged, HorizontalAlignment.Center, Size.X);
        if (_fresh) { Ornament.FreshMark(this, new Vector2(Size.X - 6, 6)); QueueRedraw(); }
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(_locked ? "Закрыто\nОткроется дальше в обучении." : forText);
}

/// <summary>A labelled value with − and + on either side.</summary>
public partial class Stepper : HBoxContainer
{
    public Stepper() { }

    public Stepper(string caption, string tooltip, Func<string> value, Action<int> step)
    {
        AddThemeConstantOverride("separation", 4);
        TooltipText = tooltip;
        MouseFilter = MouseFilterEnum.Pass;
        var cap = Hud.CapsLabel(caption);
        cap.VerticalAlignment = VerticalAlignment.Center;
        AddChild(cap);
        // wide enough for the longest of its words, so that the row does not shift as the value changes
        var val = new Label { Text = value(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(118, 0) };
        val.AddThemeFontOverride("font", Ornament.SerifBold);
        val.AddThemeFontSizeOverride("font_size", 15);
        val.AddThemeColorOverride("font_color", Ornament.Parchment);
        AddChild(Small("−", () => { step(-1); val.Text = value(); }, tooltip));
        AddChild(val);
        AddChild(Small("+", () => { step(1); val.Text = value(); }, tooltip));
    }

    static Button Small(string text, Action act, string tip)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(26, 26), TooltipText = Bindings.Fill(tip), MouseDefaultCursorShape = CursorShape.PointingHand };
        b.AddThemeFontSizeOverride("font_size", 16);
        b.Pressed += act;
        return b;
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>A row of mutually exclusive choices, like "Столб · Шар · Перекрасить".</summary>
public partial class Choice : HBoxContainer
{
    public Choice() { }

    public Choice(string caption, string tooltip, string[] items, int current, Action<int> pick)
    {
        AddThemeConstantOverride("separation", 3);
        TooltipText = tooltip;
        var cap = Hud.CapsLabel(caption);
        cap.VerticalAlignment = VerticalAlignment.Center;
        AddChild(cap);
        var group = new ButtonGroup();
        for (int i = 0; i < items.Length; i++)
        {
            int k = i;
            var b = new Button { Text = items[i], ToggleMode = true, ButtonGroup = group, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 26), TooltipText = Bindings.Fill(tooltip), MouseDefaultCursorShape = CursorShape.PointingHand };
            b.SetPressedNoSignal(i == current);
            b.Pressed += () => pick(k);
            AddChild(b);
        }
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>A small tag that follows the cursor: what the stroke will cost, or what lies under the cursor.</summary>
public partial class CursorTag : Control
{
    readonly List<(string text, Color color, Font font, int size)> _lines = new();

    public CursorTag() { MouseFilter = MouseFilterEnum.Ignore; }

    public void Show(string main, Color mainColor, string sub)
    {
        _lines.Clear();
        if (!string.IsNullOrEmpty(main)) _lines.Add((main, mainColor, Ornament.SerifBold, 14));
        if (!string.IsNullOrEmpty(sub)) _lines.Add((sub, Ornament.Aged, Ornament.SerifItalic, 13));
        Visible = _lines.Count > 0;
        if (!Visible) return;
        float w = 0, h = 10;
        foreach (var (t, _, f, size) in _lines) { w = Math.Max(w, f.GetStringSize(t, HorizontalAlignment.Left, -1, size).X); h += size + 6; }
        Size = new Vector2(w + 22, h);
        var vp = GetViewportRect().Size;
        var mouse = GetViewport().GetMousePosition();
        if (mouse.X < 0 || mouse.Y < 0 || mouse.X > vp.X || mouse.Y > vp.Y) { Visible = false; return; }
        var m = mouse + new Vector2(22, 24);
        if (m.X + Size.X > vp.X - 8) m.X = mouse.X - 16 - Size.X;
        if (m.Y + Size.Y > vp.Y - 8) m.Y = mouse.Y - 16 - Size.Y;
        Position = new Vector2(Math.Max(8, m.X), Math.Max(8, m.Y));
        QueueRedraw();
    }

    public override void _Draw()
    {
        Craft.Plate(this, new Rect2(Vector2.Zero, Size));
        float y = 8;
        foreach (var (t, col, f, size) in _lines) { y += size; DrawString(f, new Vector2(11, y), t, HorizontalAlignment.Left, -1, size, col); y += 6; }
    }
}

/// <summary>A round button with an icon: the menu, the help that opens the page of the controls, the views on the map's ring.</summary>
public partial class Medallion : Control
{
    readonly Icons.Painter _icon;
    bool _hover, _lit;
    /// <summary>A click on it.</summary>
    public event Action Clicked;
    /// <summary>Its icon glowing like an ember: what it turns on is on.</summary>
    public bool Lit { get => _lit; set { if (_lit == value) return; _lit = value; QueueRedraw(); } }

    public Medallion() { }

    public Medallion(Icons.Painter icon, string tooltip)
    {
        _icon = icon;
        TooltipText = tooltip;
        CustomMinimumSize = new Vector2(46, 46);
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public override void _Draw()
    {
        var c = Size / 2; float r = Math.Min(Size.X, Size.Y) / 2 - 2;
        DrawCircle(c + new Vector2(0, 2), r + 1, new Color(0, 0, 0, .3f), true, -1, true);
        DrawCircle(c, r, _hover ? Craft.Raised : Craft.Fill, true, -1, true);
        DrawArc(c, r - .5f, 0, MathF.Tau, 40, _hover ? Ornament.Fade(Ornament.BronzeLight, .7f) : Craft.Line, 1, true);
        _icon?.Invoke(this, new Rect2(c - new Vector2(r * .62f, r * .62f), new Vector2(r * 1.24f, r * 1.24f)), _lit ? Ornament.EmberGlow : _hover ? Ornament.BronzeLight : Ornament.Bronze);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (Clicked == null || e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
        Sound.Ui("tick");
        Juice.Press(this);
        Clicked();
        AcceptEvent();
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>The latest lines of the chronicle, briefly, above the console; the whole of it is in its tab.</summary>
public partial class Toasts : VBoxContainer
{
    readonly List<(Control line, double born)> _lines = new();
    double _clock;
    public const int Most = 3;
    const double Life = 9;

    public Toasts()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 2);
    }

    /// <summary>The dark band under each line: it is read over bright grass as well as over the night.</summary>
    static readonly StyleBoxFlat Band = MakeBand();

    static StyleBoxFlat MakeBand()
    {
        var b = new StyleBoxFlat { BgColor = new Color(.035f, .027f, .045f, .72f), AntiAliasing = true };
        b.SetCornerRadiusAll(5);
        b.ContentMarginLeft = b.ContentMarginRight = 12;
        b.ContentMarginTop = 3; b.ContentMarginBottom = 4;
        return b;
    }

    public void Push(string text, Color color)
    {
        var l = Hud.Text(text, Ornament.Serif, 14, color, wrap: true, outline: 3);
        // as wide as the words, up to a line of 620
        float w = Ornament.Serif.GetStringSize(text, HorizontalAlignment.Left, -1, 14).X;
        l.CustomMinimumSize = new Vector2(Math.Min(620, w + 4), 0);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        var band = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        band.AddThemeStyleboxOverride("panel", Band);
        band.AddChild(l);
        AddChild(band);
        Juice.Appear(band, .9f, .22);
        _lines.Add((band, _clock));
        while (_lines.Count > Most) { _lines[0].line.QueueFree(); _lines.RemoveAt(0); }
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("Widgets");
        _clock += delta;
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            var (l, born) = _lines[i];
            double age = _clock - born;
            if (age > Life) { l.QueueFree(); _lines.RemoveAt(i); continue; }
            l.Modulate = new Color(1, 1, 1, age < Life - 2 ? 1 : (float)((Life - age) / 2));
        }
    }
}
