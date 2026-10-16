using System;
using System.Collections.Generic;
using Godot;

namespace VoxelTower.Game;

/// <summary>A container drawn as an iron plate; its children sit inside the bronze rule.</summary>
public partial class OrnatePanel : MarginContainer
{
    public bool Ornate = true;
    public float Alpha = .96f;

    public OrnatePanel() : this(14) { }

    public OrnatePanel(int pad, bool ornate = true)
    {
        Ornate = ornate;
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            AddThemeConstantOverride(side, pad);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Draw() => Ornament.Plate(this, new Rect2(Vector2.Zero, Size), Alpha, Ornate);
}

/// <summary>Tooltips in the same iron and parchment: a title in bronze, then the description.</summary>
public static class Tooltip
{
    public static Control Make(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var parts = text.Split('\n', 2);
        var panel = new OrnatePanel(12, false);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
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
    readonly string _key;
    bool _selected, _disabled, _hover, _down;

    public string Caption;
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
    public bool Disabled { get => _disabled; set { if (_disabled == value) return; _disabled = value; QueueRedraw(); } }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left) return;
        if (mb.Pressed) _down = true;
        else if (_down) { _down = false; if (_hover && !_disabled) Clicked?.Invoke(); }
        QueueRedraw();
        AcceptEvent();
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
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
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
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
        var val = new Label { Text = value(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(58, 0) };
        val.AddThemeFontOverride("font", Ornament.SerifBold);
        val.AddThemeFontSizeOverride("font_size", 15);
        val.AddThemeColorOverride("font_color", Ornament.Parchment);
        AddChild(Small("−", () => { step(-1); val.Text = value(); }, tooltip));
        AddChild(val);
        AddChild(Small("+", () => { step(1); val.Text = value(); }, tooltip));
    }

    static Button Small(string text, Action act, string tip)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(26, 26), TooltipText = tip, MouseDefaultCursorShape = CursorShape.PointingHand };
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
            var b = new Button { Text = items[i], ToggleMode = true, ButtonGroup = group, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 26), TooltipText = tooltip, MouseDefaultCursorShape = CursorShape.PointingHand };
            b.SetPressedNoSignal(i == current);
            b.Pressed += () => pick(k);
            AddChild(b);
        }
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>The treasury banner at the top of the screen: gold and dug earth.</summary>
public partial class Banner : Control
{
    public string Gold = "0", Earth = "0";

    public Banner()
    {
        CustomMinimumSize = new Vector2(400, 60);
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = "Казна и земля\nЗолото платится за каждый воксель камня и брёвен. Землю для насыпей и земляных стен даёт лопата.";
    }

    public override void _Draw()
    {
        var s = Size;
        float k = 18;
        var outline = new[] { new Vector2(0, s.Y / 2), new Vector2(k, 0), new Vector2(s.X - k, 0), new Vector2(s.X, s.Y / 2), new Vector2(s.X - k, s.Y), new Vector2(k, s.Y) };
        var shadow = new Vector2[outline.Length];
        for (int i = 0; i < outline.Length; i++) shadow[i] = outline[i] + new Vector2(0, 4);
        DrawColoredPolygon(shadow, new Color(0, 0, 0, .35f));
        DrawPolygon(outline, new[] { Ornament.IronMid, Ornament.IronLight, Ornament.IronLight, Ornament.IronMid, Ornament.Iron, Ornament.Iron });
        var inner = new[] { new Vector2(7, s.Y / 2), new Vector2(k + 3, 5), new Vector2(s.X - k - 3, 5), new Vector2(s.X - 7, s.Y / 2), new Vector2(s.X - k - 3, s.Y - 5), new Vector2(k + 3, s.Y - 5), new Vector2(7, s.Y / 2) };
        DrawPolyline(inner, Ornament.Fade(Ornament.Bronze, .6f), 1, true);
        var rim = new Vector2[outline.Length + 1];
        outline.CopyTo(rim, 0); rim[^1] = outline[0];
        DrawPolyline(rim, Ornament.BronzeDeep, 1.5f, true);
        Ornament.Rivet(this, new Vector2(k * .62f, s.Y / 2), 3);
        Ornament.Rivet(this, new Vector2(s.X - k * .62f, s.Y / 2), 3);
        Ornament.Divider(this, new Vector2(s.X / 2, 12), new Vector2(s.X / 2, s.Y - 12), .9f);

        float half = s.X / 2;
        Group(new Rect2(30, 0, half - 40, s.Y), Icons.Coin, "Казна", Gold);
        Group(new Rect2(half + 12, 0, half - 40, s.Y), Icons.Earth, "Земля, м³", Earth);
    }

    void Group(Rect2 r, Icons.Painter icon, string caption, string value)
    {
        icon(this, new Rect2(r.Position + new Vector2(0, 11), new Vector2(38, 38)), Ornament.BronzeLight);
        DrawString(Ornament.Caps, r.Position + new Vector2(48, 24), caption.ToUpperInvariant(), HorizontalAlignment.Left, -1, 10, Ornament.Aged);
        DrawString(Ornament.SerifBold, r.Position + new Vector2(48, 47), value, HorizontalAlignment.Left, -1, 22, Ornament.Parchment);
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
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(new Rect2(r.Position + new Vector2(0, 3), r.Size), new Color(0, 0, 0, .35f));
        Ornament.Gradient(this, r, Ornament.Fade(Ornament.IronLight, .94f), Ornament.Fade(Ornament.Iron, .94f));
        DrawRect(r, Ornament.BronzeDark, false, 1);
        DrawRect(r.Grow(-2), Ornament.Fade(Ornament.Bronze, .35f), false, 1);
        float y = 8;
        foreach (var (t, col, f, size) in _lines) { y += size; DrawString(f, new Vector2(11, y), t, HorizontalAlignment.Left, -1, size, col); y += 6; }
    }
}

/// <summary>A round bronze medallion; the help one shows the controls on hover.</summary>
public partial class Medallion : Control
{
    readonly Icons.Painter _icon;
    bool _hover;

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
        DrawCircle(c + new Vector2(1, 2), r + 1, new Color(0, 0, 0, .5f), true, -1, true);
        DrawCircle(c, r, Ornament.IronDark, true, -1, true);
        DrawCircle(c, r - 2, Ornament.IronMid, true, -1, true);
        DrawArc(c, r - 1, 0, MathF.Tau, 40, _hover ? Ornament.BronzeLight : Ornament.Bronze, 1.6f, true);
        DrawArc(c, r - 5, 0, MathF.Tau, 40, Ornament.BronzeDeep, 1f, true);
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF.Tau / 8;
            DrawCircle(c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (r - 3), 1.1f, Ornament.BronzeDark, true, -1, true);
        }
        _icon?.Invoke(this, new Rect2(c - new Vector2(r * .62f, r * .62f), new Vector2(r * 1.24f, r * 1.24f)), _hover ? Ornament.BronzeLight : Ornament.Bronze);
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}
