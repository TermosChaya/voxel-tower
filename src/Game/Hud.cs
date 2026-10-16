using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>The builder's ledger on the right, the hover line at the bottom left, and messages that float up at the cursor.</summary>
public partial class Hud : CanvasLayer
{
    public enum Kind { Plain, Ok, Warn, Bad }
    public const float Width = 380;

    Main _m;
    Label _gold, _earth, _hint, _hover, _plan;
    VBoxContainer _opts, _bps, _log;
    Control _floats;
    readonly Dictionary<Tool, Button> _toolButtons = new();
    readonly List<(string text, Kind kind)> _lines = new();
    double _refreshAt;

    public void Init(Main m)
    {
        _m = m;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = Ui.MakeTheme() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // what is under the cursor and what the plan would cost, bottom left over the map
        var info = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        info.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        info.GrowVertical = Control.GrowDirection.Begin;
        info.OffsetLeft = 18; info.OffsetBottom = -16;
        info.AddThemeConstantOverride("separation", 2);
        _plan = Ui.Text("", 15, Ui.Accent, Ui.Mono);
        _hover = Ui.Text("", 13, Ui.Muted, Ui.Mono);
        info.AddChild(_plan);
        info.AddChild(_hover);
        root.AddChild(info);

        _floats = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _floats.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_floats);

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        panel.OffsetLeft = -Width;
        var ps = Ui.Box(Ui.Panel, Ui.Line, 0, 0, 0, 0);
        ps.BorderWidthLeft = 1;
        panel.AddThemeStyleboxOverride("panel", ps);
        root.AddChild(panel);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var (side, v) in new[] { ("margin_left", 22), ("margin_right", 22), ("margin_top", 20), ("margin_bottom", 22) })
            margin.AddThemeConstantOverride(side, v);
        scroll.AddChild(margin);
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 14);
        margin.AddChild(col);

        // title
        var head = new VBoxContainer();
        head.AddThemeConstantOverride("separation", 2);
        head.AddChild(Ui.Section("Кровь и камень · Godot 4.7"));
        var title = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeFontOverride("normal_font", Ui.Display);
        title.AddThemeFontSizeOverride("normal_font_size", 32);
        title.Text = $"[color=#{Ui.Fg.ToHtml(false)}]СУМРАЧНЫЙ[/color] [color=#{Ui.Accent.ToHtml(false)}]ВАЛ[/color]";
        head.AddChild(title);
        col.AddChild(head);

        // treasury
        var stats = new GridContainer { Columns = 2 };
        stats.AddThemeConstantOverride("h_separation", 24);
        stats.AddChild(Stat("Золото", out _gold));
        stats.AddChild(Stat("Земля, м³", out _earth));
        col.AddChild(stats);
        var w = m.World;
        col.AddChild(Ui.Text($"Поле {Fmt.Int(w.SX * VoxelWorld.Vox)} × {Fmt.Int(w.SZ * VoxelWorld.Vox)} м, воксель 6,25 см, {Fmt.One(w.SX * (double)w.SY * w.SZ / 1e6)} млн вокселей.", 12, Ui.Muted, null, true));
        col.AddChild(Rule());

        // tools
        col.AddChild(Ui.Section("Стройка из материала"));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        foreach (var (t, name, key) in new[] { (Tool.Wall, "Стена", "1"), (Tool.Tower, "Башня", "2"), (Tool.Stairs, "Ступени", "3"), (Tool.Ladder, "Лестница", "4"), (Tool.Brush, "Кисть", "5"), (Tool.Dig, "Копать", "6"), (Tool.Fill, "Насыпать", "7"), (Tool.Remove, "Разобрать", "8") })
            grid.AddChild(ToolButton(t, name, key));
        col.AddChild(grid);
        _opts = new VBoxContainer();
        _opts.AddThemeConstantOverride("separation", 8);
        col.AddChild(_opts);
        _hint = Ui.Text("", 13, Ui.Muted, null, true);
        col.AddChild(_hint);
        col.AddChild(Rule());

        // blueprints
        var bpHead = new HBoxContainer();
        var bpLabel = Ui.Section("Чертежи");
        bpLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bpLabel.VerticalAlignment = VerticalAlignment.Center;
        bpHead.AddChild(bpLabel);
        var capture = ToolButton(Tool.Capture, "Снять чертёж", "B");
        capture.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        capture.CustomMinimumSize = new Vector2(150, 34);
        bpHead.AddChild(capture);
        col.AddChild(bpHead);
        _bps = new VBoxContainer();
        _bps.AddThemeConstantOverride("separation", 8);
        col.AddChild(_bps);
        col.AddChild(Rule());

        // log and controls
        col.AddChild(Ui.Section("Летопись"));
        _log = new VBoxContainer();
        _log.AddThemeConstantOverride("separation", 5);
        col.AddChild(_log);
        col.AddChild(Rule());
        col.AddChild(Ui.Section("Управление"));
        col.AddChild(Ui.Text("ЛКМ строит. ПКМ тянет карту, щелчок ПКМ отменяет инструмент. Колесо — масштаб. СКМ или Alt+ЛКМ — поворот, Q/E тоже. WASD или стрелки — сдвиг. H — вернуть вид. Esc — убрать инструмент.", 12, Ui.Muted, null, true));

        RebuildBlueprints();
        OnToolChanged();
        Refresh();
    }

    static Control Stat(string label, out Label value)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 0);
        v.AddChild(Ui.Section(label));
        value = Ui.Text("0", 26, Ui.Fg, Ui.Mono);
        v.AddChild(value);
        return v;
    }

    static Control Rule()
    {
        var r = new ColorRect { Color = Ui.Line, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore };
        return r;
    }

    Button ToolButton(Tool t, string name, string key)
    {
        var b = Ui.Button(name, () => _m.Tools.SetTool(t), toggle: true);
        b.Alignment = HorizontalAlignment.Left;
        b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        b.CustomMinimumSize = new Vector2(0, 36);
        if (t == Tool.Ladder) b.TooltipText = BuildTools.Hints[Tool.Ladder];
        var k = Ui.Text(key, 11, Ui.Muted, Ui.Mono);
        k.AnchorLeft = 1; k.AnchorRight = 1; k.AnchorTop = 0; k.AnchorBottom = 1;
        k.OffsetLeft = -28; k.OffsetRight = -10;
        k.HorizontalAlignment = HorizontalAlignment.Right;
        k.VerticalAlignment = VerticalAlignment.Center;
        b.AddChild(k);
        _toolButtons[t] = b;
        return b;
    }

    // ---------------------------------------------------------------- state

    public void Refresh()
    {
        _gold.Text = Fmt.Int(Math.Floor(_m.Wallet.Gold));
        _earth.Text = Fmt.One(_m.Wallet.Earth);
    }

    public override void _Process(double delta)
    {
        if (_gold == null) return;
        _refreshAt -= delta;
        if (_refreshAt <= 0) { _refreshAt = .2; Refresh(); }
        var tools = _m.Tools;
        _hover.Text = tools.HoverText;
        _plan.Text = tools.PlanText ?? "";
        _plan.AddThemeColorOverride("font_color", tools.PlanAffordable ? Ui.Accent : Ui.Bad);
    }

    public void OnToolChanged()
    {
        var t = _m.Tools.Tool;
        foreach (var (tool, b) in _toolButtons) b.SetPressedNoSignal(tool == t);
        _hint.Text = BuildTools.Hints.TryGetValue(t, out var h) ? h : "Выберите инструмент: стена, башня, ступени, кисть. Всё строится из материала и стоит денег.";
        RebuildOptions();
    }

    void RebuildOptions()
    {
        foreach (var c in _opts.GetChildren()) c.QueueFree();
        var o = _m.Tools.Opt;
        var mats = new[] { (Mat.Stone, "Камень"), (Mat.Wood, "Брёвна"), (Mat.Rammed, "Земля") };
        switch (_m.Tools.Tool)
        {
            case Tool.Wall:
            {
                var items = new List<(WallProfile, string)>();
                foreach (var p in Brushes.Profiles) items.Add((p, p.Key switch { "palisade" => "Частокол", "stone" => "Камень", "rampart" => "Вал", _ => "Земля" }));
                _opts.AddChild(Seg("Профиль", items, o.Profile, v => o.Profile = v));
                var c = o.Profile.CostPerMetre();
                string earth = c.Earth > 0 ? $" и {Fmt.M3(c.Earth)} м³ земли" : "";
                _opts.AddChild(Note($"{o.Profile.Name}: {o.Profile.Info}. Метр стоит {c.Gold} зол.{earth}"));
                break;
            }
            case Tool.Tower:
                _opts.AddChild(Seg("Ширина", new List<(int, string)> { (12, "1,5 м"), (16, "2 м"), (20, "2,5 м") }, o.TowerR, v => o.TowerR = v));
                _opts.AddChild(Seg("Высота", new List<(int, string)> { (48, "3 м"), (56, "3,5 м"), (64, "4 м") }, o.TowerH, v => o.TowerH = v));
                _opts.AddChild(Note("Каменная оболочка, внутри утрамбованная земля, наверху площадка с зубцами. Подниматься — по ступеням."));
                break;
            case Tool.Stairs:
                _opts.AddChild(Seg("Материал", new List<(byte, string)>(mats), o.Material, v => o.Material = v));
                _opts.AddChild(Note("Ступень — полметра: ровно на столько шагает воин и орк."));
                break;
            case Tool.Brush:
                _opts.AddChild(Seg("Материал", new List<(byte, string)>(mats), o.Material, v => o.Material = v));
                _opts.AddChild(Seg("Размер", new List<(int, string)> { (2, "мелко"), (3, "средне"), (5, "крупно") }, o.BrushR, v => o.BrushR = v));
                break;
            case Tool.Dig:
                _opts.AddChild(Seg("Глубина", new List<(int, string)> { (8, "0,5 м"), (16, "1 м"), (24, "1,5 м") }, o.DigDepth, v => o.DigDepth = v));
                _opts.AddChild(Note("Скалу лопатой не взять. 1 золото землекопам за кубометр."));
                break;
            case Tool.Paste:
                var bp = _m.Tools.PasteBp;
                if (bp != null) _opts.AddChild(Note($"{bp.Name}: поворот {_m.Tools.PasteRot * 90}°. R — повернуть, Esc — отменить."));
                break;
        }
    }

    static Label Note(string s) => Ui.Text(s, 12, Ui.Muted, null, true);

    Control Seg<T>(string label, List<(T value, string text)> items, T current, Action<T> set)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        var l = Ui.Text(label, 12, Ui.Muted);
        l.CustomMinimumSize = new Vector2(76, 0);
        l.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(l);
        var group = new ButtonGroup();
        foreach (var (v, text) in items)
        {
            var value = v;
            var b = Ui.Button(text, () => { set(value); _m.Tools.OptionsChanged(); RebuildOptions(); }, toggle: true);
            b.ButtonGroup = group;
            b.SetPressedNoSignal(EqualityComparer<T>.Default.Equals(v, current));
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.CustomMinimumSize = new Vector2(0, 30);
            b.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(b);
        }
        return row;
    }

    public void RebuildBlueprints()
    {
        if (_bps == null) return;
        foreach (var c in _bps.GetChildren()) c.QueueFree();
        if (_m.Blueprints.Count == 0) { _bps.AddChild(Note("Постройте что-нибудь, затем нажмите «Снять чертёж» и обведите постройку рамкой.")); return; }
        foreach (var bp in _m.Blueprints)
        {
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel2, Ui.Line, 1, 4, 12, 10));
            var v = new VBoxContainer();
            v.AddThemeConstantOverride("separation", 3);
            var top = new HBoxContainer();
            var name = Ui.Text(bp.Name, 15, Ui.Fg);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            top.AddChild(name);
            var place = Ui.Button("Поставить", () => _m.Tools.StartPaste(bp));
            place.AddThemeFontSizeOverride("font_size", 13);
            top.AddChild(place);
            var del = Ui.Button("×", () => _m.RemoveBlueprint(bp));
            del.TooltipText = "Удалить чертёж";
            del.CustomMinimumSize = new Vector2(30, 0);
            top.AddChild(del);
            v.AddChild(top);
            v.AddChild(Ui.Text($"{Fmt.One(bp.W * VoxelWorld.Vox)}×{Fmt.One(bp.D * VoxelWorld.Vox)}×{Fmt.One(bp.H * VoxelWorld.Vox)} м · {bp.Describe()}", 12, Ui.Muted, null, true));
            var c = bp.Cost;
            v.AddChild(Ui.Text(c.Earth > 0 ? $"{c.Gold} зол. · {Fmt.M3(c.Earth)} м³ земли" : $"{c.Gold} зол.", 13, Ui.Accent, Ui.Mono));
            card.AddChild(v);
            _bps.AddChild(card);
        }
    }

    // ---------------------------------------------------------------- messages

    public void Log(string text, Kind kind = Kind.Plain)
    {
        _lines.Insert(0, (text, kind));
        if (_lines.Count > 7) _lines.RemoveAt(_lines.Count - 1);
        foreach (var c in _log.GetChildren()) c.QueueFree();
        for (int i = 0; i < _lines.Count; i++)
        {
            var (t, k) = _lines[i];
            var color = k switch { Kind.Ok => Ui.Ok, Kind.Warn => Ui.Warn, Kind.Bad => Ui.Bad, _ => i == 0 ? Ui.Fg : Ui.Muted };
            _log.AddChild(Ui.Text(t, 13, color, null, true));
        }
    }

    /// <summary>A short message that rises from the cursor and fades.</summary>
    public void Float(string text, Color color)
    {
        var l = Ui.Text(text, 14, color, Ui.Body);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, .9f));
        l.AddThemeConstantOverride("outline_size", 6);
        var at = _floats.GetLocalMousePosition();
        l.Position = at + new Vector2(14, -26);
        _floats.AddChild(l);
        var tw = l.CreateTween();
        tw.TweenProperty(l, "position:y", l.Position.Y - 34, 1.4).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tw.Parallel().TweenProperty(l, "modulate:a", 0f, .6).SetDelay(.9);
        tw.TweenCallback(Callable.From(l.QueueFree));
    }
}
