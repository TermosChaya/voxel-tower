using System;
using System.Collections.Generic;
using Godot;

namespace VoxelTower.Game;

/// <summary>A tab hanging on the edge of the screen like a bookmark ribbon: an engraved icon, its name written down the length.</summary>
public partial class TabSpine : Control
{
    readonly Icons.Painter _icon;
    readonly string _caption;
    readonly bool _left;
    bool _hover;
    bool _selected;
    /// <summary>A small count on the tab (what is still to do), or nothing.</summary>
    public string Badge = "";
    public event Action Clicked;

    public TabSpine() { }

    public TabSpine(Icons.Painter icon, string caption, string tooltip, bool left)
    {
        _icon = icon; _caption = caption; _left = left;
        TooltipText = tooltip;
        float len = Ornament.Caps.GetStringSize(caption.ToUpperInvariant(), HorizontalAlignment.Left, -1, 10).X;
        CustomMinimumSize = new Vector2(34, 46 + len + 14);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; QueueRedraw(); } }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Clicked?.Invoke();
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var s = Size;
        // the ribbon: square on the edge it hangs from, a notch cut into the end toward the field
        float n = 7;
        var pts = _left
            ? new[] { new Vector2(0, 0), new Vector2(s.X, 0), new Vector2(s.X - 4, s.Y / 2), new Vector2(s.X, s.Y), new Vector2(0, s.Y) }
            : new[] { new Vector2(0, 0), new Vector2(s.X, 0), new Vector2(s.X, s.Y), new Vector2(0, s.Y), new Vector2(4, s.Y / 2) };
        var shadow = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) shadow[i] = pts[i] + new Vector2(_left ? 3 : -3, 3);
        DrawColoredPolygon(shadow, new Color(0, 0, 0, .4f));
        var top = _selected ? Ornament.IronLight : Ornament.IronMid;
        DrawPolygon(pts, new[] { top, top, Ornament.Iron, Ornament.Iron, Ornament.Iron });
        var rim = new Vector2[pts.Length + 1];
        pts.CopyTo(rim, 0); rim[^1] = pts[0];
        DrawPolyline(rim, _selected ? Ornament.Bronze : Ornament.BronzeDeep, 1.4f, true);
        // a bronze edge along the side that faces the field
        float ex = _left ? s.X - 2 : 2;
        DrawLine(new Vector2(ex, 3), new Vector2(ex, s.Y / 2 - n), _selected || _hover ? Ornament.BronzeLight : Ornament.Bronze, 1.5f, true);
        DrawLine(new Vector2(ex, s.Y / 2 + n), new Vector2(ex, s.Y - 3), _selected || _hover ? Ornament.BronzeLight : Ornament.Bronze, 1.5f, true);
        var ink = _selected ? Ornament.EmberGlow : _hover ? Ornament.BronzeLight : Ornament.Bronze;
        _icon?.Invoke(this, new Rect2(5, 7, s.X - 10, s.X - 10), ink);
        // the name, written down the ribbon
        DrawSetTransform(new Vector2(s.X / 2 + 4, 42), MathF.PI / 2, Vector2.One);
        DrawString(Ornament.Caps, Vector2.Zero, _caption.ToUpperInvariant(), HorizontalAlignment.Left, -1, 10, _selected ? Ornament.Parchment : Ornament.Aged);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        if (!string.IsNullOrEmpty(Badge))
        {
            var c = new Vector2(_left ? s.X - 6 : 6, 6);
            DrawCircle(c, 8, Ornament.EmberDim, true, -1, true);
            DrawArc(c, 8, 0, MathF.Tau, 20, Ornament.Ember, 1.2f, true);
            DrawString(Ornament.Mono, c + new Vector2(-6, 4), Badge, HorizontalAlignment.Center, 12, 10, Ornament.EmberGlow);
        }
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>
/// The panels that slide out from one edge of the screen: a column of tabs hangs on the edge, a click on one slides its
/// panel out (and the tabs with it), a click on the open one slides it back. One panel open at a time.
/// </summary>
public partial class SideDrawers : Control
{
    sealed class Page { public TabSpine Tab; public Control Content; public float Width; public string Key; }

    readonly bool _left;
    readonly List<Page> _pages = new();
    readonly OrnatePanel _panel;
    readonly ScrollContainer _scroll;
    readonly VBoxContainer _tabs;
    Page _open;
    float _shown, _target;
    Tween _tween;

    /// <summary>The page that is open, by its key, or null.</summary>
    public string OpenKey => _target > 0 ? _open?.Key : null;

    public SideDrawers() { }

    public SideDrawers(bool left, float top, float bottom)
    {
        _left = left;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(left ? LayoutPreset.LeftWide : LayoutPreset.RightWide);
        OffsetTop = top; OffsetBottom = -bottom;
        _panel = new OrnatePanel(12) { Visible = false };
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _panel.AddChild(_scroll);
        AddChild(_panel);
        _tabs = new VBoxContainer();
        _tabs.AddThemeConstantOverride("separation", 6);
        AddChild(_tabs);
    }

    public TabSpine Add(string key, Icons.Painter icon, string caption, string tooltip, Control content, float width)
    {
        var page = new Page { Key = key, Content = content, Width = width, Tab = new TabSpine(icon, caption, tooltip, _left) };
        page.Tab.Clicked += () => Toggle(page);
        _tabs.AddChild(page.Tab);
        _pages.Add(page);
        return page.Tab;
    }

    void Toggle(Page p)
    {
        if (_open == p && _target > 0) Close();
        else Open(p);
    }

    /// <summary>Opens the page with this key (switching from another if one is open).</summary>
    public void Open(string key) { foreach (var p in _pages) if (p.Key == key) { Open(p); return; } }

    void Open(Page p)
    {
        if (_open != p)
        {
            if (_open != null) _scroll.RemoveChild(_open.Content);
            _open = p;
            _scroll.AddChild(p.Content);
            p.Content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }
        foreach (var q in _pages) q.Tab.Selected = q == p;
        Slide(1);
    }

    public void Close()
    {
        foreach (var q in _pages) q.Tab.Selected = false;
        Slide(0);
    }

    void Slide(float to)
    {
        _target = to;
        _panel.Visible = true;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(v => { _shown = v; Lay(); }), _shown, to, .22).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _tween.TweenCallback(Callable.From(() => { if (_target == 0) _panel.Visible = false; }));
    }

    public override void _Process(double delta) => Lay();

    /// <summary>Places the panel (slid out by how far it is open) and the tabs on its outer side.</summary>
    void Lay()
    {
        float w = _open?.Width ?? 0, h = Size.Y, x = w * _shown;
        _scroll.CustomMinimumSize = new Vector2(Math.Max(0, w - 24), 0);
        _panel.Size = new Vector2(w, Math.Min(h, (_open?.Content.GetCombinedMinimumSize().Y ?? 0) + 26));
        if (_left)
        {
            _panel.Position = new Vector2(x - w, 0);
            _tabs.Position = new Vector2(x, 12);
        }
        else
        {
            float right = Size.X;
            _panel.Position = new Vector2(right - x, 0);
            _tabs.Position = new Vector2(right - x - _tabs.Size.X, 12);
        }
    }
}

/// <summary>The latest lines of the chronicle, briefly, over the top left of the field; the whole of it is in its tab.</summary>
public partial class Toasts : VBoxContainer
{
    readonly List<(Label label, double born)> _lines = new();
    double _clock;
    public const int Most = 3;
    const double Life = 9;

    public Toasts()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 2);
    }

    public void Push(string text, Color color)
    {
        var l = Hud.Text(text, Ornament.Serif, 14, color, wrap: true, outline: 6);
        l.CustomMinimumSize = new Vector2(620, 0);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(l);
        _lines.Add((l, _clock));
        while (_lines.Count > Most) { _lines[0].label.QueueFree(); _lines.RemoveAt(0); }
    }

    public override void _Process(double delta)
    {
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
