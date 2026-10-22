using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The beam along the top edge of the screen: oak boards bound with iron, a brass lip along its lower edge. The tabs
/// sit on it at the left, the stores in the middle; the wave clock hangs from it on the right.
/// </summary>
public partial class TopBeam : Control
{
    public const float Height = 40;

    public TopBeam()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.TopWide);
        OffsetBottom = Height;
    }

    public override void _Draw()
    {
        float w = Size.X;
        // its shadow on the field
        Ornament.Gradient(this, new Rect2(0, Height, w, 12), new Color(0, 0, 0, .4f), new Color(0, 0, 0, 0));
        Craft.Wood(this, new Rect2(0, 0, w, Height - 7), .95f);
        Craft.Band(this, new Rect2(0, Height - 8, w, 8), 64);
        Craft.BrassEdge(this, new Vector2(0, Height - 8.5f), new Vector2(w, Height - 8.5f), 1.6f);
        // iron straps round the beam, and caps on its ends
        for (float x = 230; x < w - 60; x += 330) Craft.Band(this, new Rect2(x, 0, 9, Height - 8), 14);
        Craft.Band(this, new Rect2(0, 0, 8, Height), 14);
        Craft.Band(this, new Rect2(w - 8, 0, 8, Height), 14);
    }
}

/// <summary>A tab on the beam: an engraved icon and its name, ember when its panel hangs open, a count of what is still to do.</summary>
public partial class TabPlaque : Control
{
    readonly Icons.Painter _icon;
    readonly string _caption;
    bool _hover;
    public bool Selected;
    public string Badge = "";
    public event Action Clicked;

    public TabPlaque() { }

    readonly float _len;
    bool _compact;
    /// <summary>On a narrow beam the tab shows its icon only; its name is in the tooltip.</summary>
    public bool Compact
    {
        get => _compact;
        set { if (_compact == value) return; _compact = value; CustomMinimumSize = new Vector2(value ? 48 : _len + 50, 28); Size = CustomMinimumSize; QueueRedraw(); }
    }
    /// <summary>How much wider it is with its name shown.</summary>
    public float Saved => _len + 2;

    public TabPlaque(Icons.Painter icon, string caption, string tooltip)
    {
        _icon = icon; _caption = caption;
        TooltipText = tooltip;
        _len = Ornament.Caps.GetStringSize(caption.ToUpperInvariant(), HorizontalAlignment.Left, -1, 10).X;
        CustomMinimumSize = new Vector2(_len + 50, 28);
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
        Craft.Plaque(this, r, _hover, Selected);
        var ink = Selected ? Ornament.EmberGlow : _hover ? Ornament.BronzeLight : Ornament.Bronze;
        _icon?.Invoke(this, new Rect2(7, 5, Size.Y - 10, Size.Y - 10), ink);
        if (!_compact)
            DrawString(Ornament.Caps, new Vector2(Size.Y + 2, Size.Y / 2 + 4), _caption.ToUpperInvariant(), HorizontalAlignment.Left, -1, 10,
                Selected ? Ornament.Parchment : _hover ? Ornament.BronzeLight : Ornament.Aged);
        if (!string.IsNullOrEmpty(Badge))
        {
            var c = new Vector2(Size.X - 6, 5);
            DrawCircle(c, 7.5f, Ornament.EmberDim, true, -1, true);
            DrawArc(c, 7.5f, 0, MathF.Tau, 20, Ornament.Ember, 1.2f, true);
            DrawString(Ornament.Mono, c + new Vector2(-6, 3.6f), Badge, HorizontalAlignment.Center, 12, 9, Ornament.EmberGlow);
        }
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>
/// The panels that hang from the beam under its tabs: boards round a dark well with the page in it, let down when a
/// tab is clicked and drawn up again on a second click. One hangs at a time.
/// </summary>
public partial class DropPanel : Control
{
    sealed class Page { public string Key; public TabPlaque Tab; public Control Content; public float Width; }

    readonly List<Page> _pages = new();
    readonly ScrollContainer _scroll;
    Page _open;
    float _shown, _target;
    Tween _tween;
    /// <summary>How far down the screen it may hang.</summary>
    public float Bottom = 300;

    public string OpenKey => _target > 0 ? _open?.Key : null;
    /// <summary>The right edge of what hangs, in its parent's pixels (0 when nothing does).</summary>
    public float RightEdge => _target > 0 && _open != null ? Position.X + _open.Width : 0;

    public DropPanel()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        ClipContents = true;
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
        AddChild(_scroll);
    }

    public void Add(string key, TabPlaque tab, Control content, float width)
    {
        var p = new Page { Key = key, Tab = tab, Content = content, Width = width };
        tab.Clicked += () => { if (_open == p && _target > 0) Close(); else Open(p); };
        _pages.Add(p);
    }

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
        foreach (var q in _pages) { q.Tab.Selected = q == p; q.Tab.QueueRedraw(); }
        Slide(1);
    }

    public void Close()
    {
        foreach (var q in _pages) { q.Tab.Selected = false; q.Tab.QueueRedraw(); }
        Slide(0);
    }

    void Slide(float to)
    {
        _target = to;
        Visible = true;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(v => { _shown = v; Lay(); }), _shown, to, .2).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _tween.TweenCallback(Callable.From(() => { if (_target == 0) Visible = false; }));
    }

    public override void _Process(double delta) { if (Visible) Lay(); }

    const float Border = 12;

    float _full;

    void Lay()
    {
        if (_open == null) return;
        float w = _open.Width;
        float want = _open.Content.GetCombinedMinimumSize().Y + 2 * Border + 6;
        _full = Math.Min(want, Math.Max(80, Bottom - Position.Y));
        Size = new Vector2(w, _full * _shown);
        // it comes down from under the beam: what is not yet down is above the top edge, cut off
        _scroll.Position = new Vector2(Border + 4, Border + 3 + Size.Y - _full);
        _scroll.Size = new Vector2(w - 2 * Border - 8, _full - 2 * Border - 6);
        _open.Content.CustomMinimumSize = new Vector2(w - 2 * Border - 20, 0);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_open != null) Craft.Framed(this, new Rect2(0, Size.Y - _full, _open.Width, _full));
    }
}

/// <summary>
/// The wave clock, hanging from the beam on two chains: which wave is next and how long until it comes (or how many
/// raiders are in the valley), the time of day, the early call, the speed of the game, the horde's ways, and how much
/// of the citadel still stands.
/// </summary>
public partial class WaveClock : Control
{
    Main _m;
    Label _title, _note, _time, _unit;
    Button _call, _trails;
    readonly List<Button> _speeds = new();
    KeepBar _keep;
    DayDial _dial;
    const string CallTip = "Позвать орду сейчас ({CallWave})\nНаграды за это нет, но орда хуже знает вашу оборону: процент — сколько она успела разведать. Сразу наступит ночь.";
    public const float W = 470, H = 70;

    public void Init(Main m)
    {
        _m = m;
        CustomMinimumSize = new Vector2(W, H);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        var col = new VBoxContainer { Position = new Vector2(16, 9), Size = new Vector2(W - 32, H - 16) };
        col.AddThemeConstantOverride("separation", 1);
        var head = new HBoxContainer();
        _title = Hud.CapsLabel("", 8);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_title);
        _dial = new DayDial();
        head.AddChild(_dial);
        _note = Hud.CapsLabel("", 8, Ornament.EmberGlow);
        head.AddChild(_note);
        col.AddChild(head);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 5);
        _time = Hud.Text("", Ornament.Display, 26, Ornament.Parchment);
        _time.CustomMinimumSize = new Vector2(62, 0);
        _time.ClipText = true;
        _time.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_time);
        _unit = Hud.CapsLabel("", 7, Ornament.Aged);
        _unit.CustomMinimumSize = new Vector2(52, 0);
        _unit.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_unit);
        _call = new Button
        {
            FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(124, 26), MouseDefaultCursorShape = CursorShape.PointingHand,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            TooltipText = CallTip,
        };
        _call.AddThemeFontSizeOverride("font_size", 12);
        _call.Pressed += () => _m.Command.CallWave();
        row.AddChild(_call);
        string[] labels = { "II", "×1", "×2", "×3" };
        for (int i = 0; i < labels.Length; i++)
        {
            int k = i;
            var b = new Button
            {
                Text = labels[i], ToggleMode = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(29, 24),
                MouseDefaultCursorShape = CursorShape.PointingHand, SizeFlagsVertical = SizeFlags.ShrinkCenter,
                TooltipText = "Скорость\nНа паузе можно размечать. {Pause} — пауза, {Speed} — скорость.",
            };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.Pressed += () => { if (k == 0) _m.Command.TogglePause(); else _m.Command.SetSpeed(k); };
            _speeds.Add(b);
            row.AddChild(b);
        }
        _trails = new Button
        {
            Text = "Пути", ToggleMode = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(44, 24),
            MouseDefaultCursorShape = CursorShape.PointingHand, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            TooltipText = "Пути орды ({Trails})\nКуда пойдут ватаги следующей волны. Красные кольца — где они будут прорубаться, жёлтые — где поставят лестницу, " +
                "наведут мост или засыплют ров. Пути меняются, пока вы строите.",
        };
        _trails.AddThemeFontSizeOverride("font_size", 12);
        _trails.Pressed += () => _m.Command.ToggleTrails();
        row.AddChild(_trails);
        col.AddChild(row);
        _keep = new KeepBar();
        col.AddChild(_keep);
        AddChild(col);
        Refresh();
    }

    public override void _Draw()
    {
        // two chains up to the beam, the sign on them: boards edged with iron
        foreach (float x in new[] { 34f, W - 34 })
            for (float y = -8; y < 6; y += 5)
            {
                DrawArc(new Vector2(x, y + 2), 2.4f, 0, MathF.Tau, 10, new Color("0c0a0e"), 2.2f, true);
                DrawArc(new Vector2(x, y + 2), 2.4f, 0, MathF.Tau, 10, new Color("4a4450"), 1, true);
            }
        var r = new Rect2(0, 4, W, H - 4);
        for (int i = 3; i >= 1; i--) DrawRect(new Rect2(r.Position + new Vector2(0, 4), r.Size).Grow(i * 2), new Color(0, 0, 0, .12f));
        Craft.Wood(this, r, .85f);
        Craft.Inset(this, r.Grow(-5), .55f);
        DrawRect(r, Craft.OakDark, false, 1.5f);
        Craft.Corner(this, r.Position, new Vector2(1, 1), 14);
        Craft.Corner(this, new Vector2(r.End.X, r.Position.Y), new Vector2(-1, 1), 14);
        Craft.Corner(this, new Vector2(r.Position.X, r.End.Y), new Vector2(1, -1), 14);
        Craft.Corner(this, r.End, new Vector2(-1, -1), 14);
    }

    public void Refresh()
    {
        if (_m == null) return;
        var sim = _m.Sim; var w = sim.Waves;
        if (sim.Over)
        {
            _title.Text = (sim.Won ? "Осада снята" : "Цитадель пала").ToUpperInvariant();
            _time.Text = sim.Won ? $"{Waves.Total}/{Waves.Total}" : $"{w.Number}/{Waves.Total}";
            _unit.Text = "ВОЛН ОТБИТО";
            _call.Text = "Конец"; _call.Disabled = true;
        }
        else if (w.Active)
        {
            _title.Text = $"ВОЛНА {w.Number} ИЗ {Waves.Total} · РАЗВЕДКА {w.Intel * 100:0} %" +
                (sim.Weather != Weather.Clear ? " · " + Sim.WeatherName(sim.Weather).ToUpperInvariant() : "");
            _time.Text = (sim.RaidersAlive() + w.Coming).ToString();
            _unit.Text = "В ДОЛИНЕ";
            _call.Text = "Орда в долине"; _call.Disabled = true;
            _call.TooltipText = $"Запасы орды\nБрёвна на лестницы и мосты: {Fmt.M3(sim.HordeStock[Stockpile.Wood])} м³. Земля на засыпку рвов: {Fmt.M3(sim.HordeStock[Stockpile.Earth])} м³. " +
                "Каждая волна приносит свои запасы, а половина прорубленного идёт орде.";
        }
        else
        {
            int bands = sim.Bands.FindAll(b => b.State == BandState.Coming).Count;
            _title.Text = $"ВОЛНА {w.Number + 1} ИЗ {Waves.Total}" + (bands > 0 ? $" · ВАТАГ {bands}" : "") +
                (w.DawnLeft > 0 ? " · РАССВЕТ" : w.Countdown < Waves.Dusk ? " · ЗАКАТ" : "") +
                (sim.Weather != Weather.Clear ? " · " + Sim.WeatherName(sim.Weather).ToUpperInvariant() : "");
            _time.Text = w.Countdown < 100 * 60 ? Fmt.Clock(w.Countdown) : "—";
            _unit.Text = "ДО ОРДЫ";
            _call.Text = $"На штурм · {w.IntelNow * 100:0} %";
            _call.Disabled = false;
            _call.TooltipText = CallTip;
        }
        if (_m.Atmos != null) { _dial.Day = _m.Atmos.ShownDay; _dial.Dark = _m.Atmos.ShownDark; }
        _dial.QueueRedraw();
        _note.Text = _m.Paused ? "ПАУЗА" : _m.Speed > 1 ? $"×{_m.Speed}" : "";
        for (int i = 0; i < _speeds.Count; i++) _speeds[i].SetPressedNoSignal(i == 0 ? _m.Paused : !_m.Paused && _m.Speed == i);
        _trails.SetPressedNoSignal(sim.WantTrails);
        _keep.Value = Math.Clamp(sim.KeepHp / sim.KeepMax, 0, 1);
        _keep.QueueRedraw();
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>
/// The stores on the beam: gold, then stone, logs and earth in cubic metres, the intervention, and the household against
/// its places; what a stroke lacks shows in blood, and so does a household with no place left.
/// </summary>
public partial class Stores : Control
{
    public readonly string[] Values = { "0", "0", "0", "0", "0", "0" };
    public readonly bool[] Short = new bool[6];
    public const float W = 610, MinW = 440;

    static readonly (Icons.Painter icon, string tip)[] Groups =
    {
        (Icons.Coin, "Золото\nНа него набирают войско. Золото дают рудники, убитые налётчики, охота и отбитые волны."),
        (Icons.Material(Mat.Stone), "Камень, м³\nДобывают в скалах, валунах и руинах. Разобранная кладка возвращает 75 % камня."),
        (Icons.Material(Mat.Wood), "Брёвна, м³\nРубят в лесу. Идут на деревянные стены, лестницы, мосты и ворота."),
        (Icons.Earth, "Земля, м³\nДаёт любой ров и котлован. Насыпь кладётся быстрее всего."),
        (Icons.Eye, "Вмешательство\nТратится на заклинания. Его копят жрецы молитвой в святилище (на месте силы втрое быстрее) и жертвы."),
        (Icons.Helm, "Войско: занято мест из скольких\nМеста дают цитадель (10, с «Казармами» 14) и жилые дома, по месту на лежанку. " +
            "Без свободных мест набор не идёт. Набор — щелчок по цитадели или K."),
    };

    public Stores()
    {
        CustomMinimumSize = new Vector2(MinW, 30);
        MouseFilter = MouseFilterEnum.Stop;
    }

    float Cell => (Size.X - 12) / Groups.Length;

    public override string _GetTooltip(Vector2 at) => Groups[Math.Clamp((int)((at.X - 6) / Cell), 0, Groups.Length - 1)].tip;

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        Craft.Inset(this, r, .9f);
        for (int g = 0; g < Groups.Length; g++)
        {
            float x = 6 + g * Cell;
            if (g > 0) Ornament.Diamond(this, new Vector2(x, Size.Y / 2), 3.2f, Ornament.Fade(Ornament.BronzeLight, .85f));
            Groups[g].icon(this, new Rect2(x + 10, (Size.Y - 22) / 2, 22, 22), Ornament.BronzeLight);
            int size = Cell < 96 ? 18 : 21;
            DrawString(Ornament.SerifBold, new Vector2(x + 36, Size.Y / 2 + size * .36f), Values[g], HorizontalAlignment.Left, Cell - 38, size, Short[g] ? Ornament.Blood : Ornament.Parchment);
        }
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>How much of the citadel still stands, as a bar under the clock.</summary>
public partial class KeepBar : Control
{
    public float Value = 1;

    public KeepBar()
    {
        CustomMinimumSize = new Vector2(0, 9);
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = "Цитадель\nЕсли полоса опустеет, игра проиграна.";
    }

    public override void _Draw()
    {
        float x0 = 64, h = 5, y = (Size.Y - h) / 2;
        DrawString(Ornament.Caps, new Vector2(0, y + h), "ЦИТАДЕЛЬ", HorizontalAlignment.Left, -1, 7, Ornament.Aged);
        var r = new Rect2(x0, y, Size.X - x0, h);
        DrawRect(r, new Color(0, 0, 0, .6f));
        var fill = Value > .6f ? Ornament.Moss : Value > .3f ? Ornament.Ember : Ornament.Blood;
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * Value, h)), fill);
        DrawRect(r, Ornament.BronzeDark, false, 1);
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>
/// The day on the wave clock: a sun going over a little arc from dawn to sunset, reddening low and sinking as the dusk
/// comes on; a crescent moon and a few stars while the horde is in the valley; the sun up out of the dark at dawn.
/// </summary>
public partial class DayDial : Control
{
    public float Day, Dark;

    public DayDial()
    {
        CustomMinimumSize = new Vector2(38, 12);
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = "Сутки\nДнём строят, ночью приходит орда. За минуту до волны садится солнце. " +
            "В темноте лучники хорошо видят только освещённое: у факелов, огня и цитадели. Остальных видно лишь вблизи, и стрелы чаще мажут.";
    }

    public override void _Draw()
    {
        float w = Size.X, cx = w / 2, by = Size.Y - 1.5f, r = Math.Min(cx - 3, Size.Y - 3);
        // the ground and the sun's way over it
        DrawLine(new Vector2(1, by), new Vector2(w - 1, by), Ornament.Aged, 1);
        DrawArc(new Vector2(cx, by), r, MathF.PI, MathF.Tau, 18, new Color(Ornament.Aged, .5f), 1, true);
        // the sun, along its way by the time of day, gold going red low in the west; it fades as the dark comes on
        float a = MathF.PI * (1 + Math.Clamp(Day, 0, 1)), low = 1 - MathF.Abs(MathF.Sin(a));
        var sun = new Vector2(cx + MathF.Cos(a) * r, by + MathF.Sin(a) * r);
        var gold = Ornament.EmberGlow.Lerp(Ornament.Blood, Day > .5f ? low : 0);
        if (Dark < 1)
        {
            DrawCircle(sun, 3.4f, new Color(gold, .25f * (1 - Dark)));
            DrawCircle(sun, 2.2f, new Color(gold, 1 - Dark));
        }
        // the moon and the stars of the night
        if (Dark > 0)
        {
            var moon = new Color("c9d3ee") { A = Dark };
            DrawArc(new Vector2(cx, by - r + 1.5f), 2.3f, MathF.PI * .55f, MathF.PI * 1.45f, 10, moon, 1.7f, true);
            foreach (var (sx, sy) in new[] { (-9f, 4f), (8f, 2.5f), (11f, 6f) })
                DrawRect(new Rect2(cx + sx, by - r + sy, 1, 1), new Color(moon, Dark * .75f));
        }
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>
/// The console along the bottom edge: a band of oak boards under an iron strap across the whole width, and at its ends
/// two plinths of dressed stone rising higher — the map of the valley in its ring on the left, the tools on the right.
/// Only what is drawn stops the mouse: the field between the plinths stays the field's.
/// </summary>
public partial class BottomConsole : Control
{
    public const float Band = 126, Wing = 250, LeftW = 252, RightW = 264;

    public readonly Control Base, Left, Right;

    public BottomConsole()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.BottomWide);
        OffsetTop = -Wing;
        Base = new Plate(p => DrawBase(p)) { MouseFilter = MouseFilterEnum.Stop };
        Base.SetAnchorsPreset(LayoutPreset.BottomWide);
        Base.OffsetTop = -Band;
        AddChild(Base);
        Left = new Plate(p => DrawPlinth(p, true)) { MouseFilter = MouseFilterEnum.Stop };
        Left.SetAnchorsPreset(LayoutPreset.BottomLeft);
        Left.OffsetTop = -Wing; Left.OffsetRight = LeftW;
        AddChild(Left);
        Right = new Plate(p => DrawPlinth(p, false)) { MouseFilter = MouseFilterEnum.Stop };
        Right.SetAnchorsPreset(LayoutPreset.BottomRight);
        Right.OffsetTop = -Wing; Right.OffsetLeft = -RightW;
        AddChild(Right);
    }

    static void DrawBase(Control c)
    {
        float w = c.Size.X, h = c.Size.Y;
        Ornament.Gradient(c, new Rect2(0, -12, w, 12), new Color(0, 0, 0, 0), new Color(0, 0, 0, .4f));
        Craft.Wood(c, new Rect2(0, 8, w, h - 8), .9f);
        Craft.Band(c, new Rect2(0, 0, w, 10), 64);
        Craft.BrassEdge(c, new Vector2(0, 10.5f), new Vector2(w, 10.5f), 1.6f);
        for (float x = 380; x < w - 300; x += 360) Craft.Band(c, new Rect2(x, 10, 8, h - 10), 18);
    }

    /// <summary>A stone plinth: courses of dressed stone with a coping along its top and iron at its inner edge.</summary>
    static void DrawPlinth(Control c, bool left)
    {
        float w = c.Size.X, h = c.Size.Y;
        var body = new Rect2(0, 14, w, h - 14);
        Ornament.Gradient(c, new Rect2(left ? w : -12, 14, 12, h - 14), left ? new Color(0, 0, 0, .35f) : new Color(0, 0, 0, 0), left ? new Color(0, 0, 0, 0) : new Color(0, 0, 0, .35f));
        Craft.Stone(c, body, .92f);
        // the coping: a long stone with a rounded top, darker
        var cope = new Rect2(-4, 2, w + 8, 16);
        Craft.Stone(c, cope, .75f);
        c.DrawLine(new Vector2(cope.Position.X, cope.Position.Y + .5f), new Vector2(cope.End.X, cope.Position.Y + .5f), new Color(1, .95f, .9f, .25f), 1);
        c.DrawLine(new Vector2(cope.Position.X, cope.End.Y), new Vector2(cope.End.X, cope.End.Y), new Color(0, 0, 0, .55f), 1.5f);
        // the edge toward the field, bound in iron
        Craft.Band(c, new Rect2(left ? w - 8 : 0, 18, 8, h - 18), 22);
    }

    /// <summary>A control that only draws what it is given.</summary>
    public partial class Plate : Control
    {
        readonly Action<Control> _draw;
        public Plate() { }
        public Plate(Action<Control> draw) { _draw = draw; }
        public override void _Draw() => _draw?.Invoke(this);
    }
}

/// <summary>
/// The map of the valley set in a ring of iron and brass, the quarters of the world marked on it, with buttons to zoom
/// and to look straight down. The map is cut to the ring; only the far corners of the valley fall outside it.
/// </summary>
public partial class MapRing : Control
{
    public const float Outer = 118, Inner = 103, Side = Inner * 1.62f;
    Main _m;
    Medallion _top;
    public Minimap Map { get; private set; }
    /// <summary>The buttons of the view on the ring: looking straight down and back, home to the citadel (the tutorial points at them).</summary>
    public Control TopButton => _top;
    public Control HomeButton { get; private set; }

    public void Init(Main m)
    {
        _m = m;
        CustomMinimumSize = new Vector2(Outer * 2, Outer * 2);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Ignore;
        var mask = new Disc { Position = Vector2.Zero, Size = Size, ClipChildren = ClipChildrenMode.Only, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(mask);
        Map = new Minimap();
        mask.AddChild(Map);
        Map.Init(m);
        Map.CustomMinimumSize = new Vector2(Side, Side);
        Map.Size = new Vector2(Side, Side);
        Map.Position = new Vector2(Outer - Side / 2, Outer - Side / 2);
        AddChild(new Ring { Size = Size, MouseFilter = MouseFilterEnum.Ignore });
        // the buttons sit on the ring, low on either side of the south: the zoom outermost, the views within
        foreach (var (text, factor, angle, tip) in new[]
        {
            ("−", 1 / 1.3f, 2.86f, "Отдалить ({CamZoomOut})"),
            ("+", 1.3f, .28f, "Приблизить ({CamZoomIn})"),
        })
        {
            var b = Knob(text, tip, angle);
            float f = factor;
            b.Pressed += () => _m.Rig.ZoomBy(f);
        }
        _top = ViewKnob(Icons.TopView, "Вид сверху ({CamTop})\nЕщё раз — обычный вид.", 2.3f);
        _top.Clicked += () => _m.Rig.ToggleTopDown();
        var home = ViewKnob(Icons.HomeView, "Вернуть вид к крепости ({CamHome})", .84f);
        home.Clicked += () => _m.Rig.Home();
        HomeButton = home;
    }

    Medallion ViewKnob(Icons.Painter icon, string tip, float angle)
    {
        var b = new Medallion(icon, tip) { Size = new Vector2(30, 30), CustomMinimumSize = new Vector2(30, 30), MouseDefaultCursorShape = CursorShape.PointingHand };
        float r = (Outer + Inner) / 2;
        b.Position = new Vector2(Outer + MathF.Cos(angle) * r - 15, Outer + MathF.Sin(angle) * r - 15);
        AddChild(b);
        return b;
    }

    Button Knob(string text, string tip, float angle)
    {
        var b = new Button
        {
            Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(24, 24), Size = new Vector2(24, 24),
            MouseDefaultCursorShape = CursorShape.PointingHand, TooltipText = Bindings.Fill(tip), Flat = false,
        };
        b.AddThemeFontSizeOverride("font_size", 13);
        float r = (Outer + Inner) / 2;
        b.Position = new Vector2(Outer + MathF.Cos(angle) * r - 12, Outer + MathF.Sin(angle) * r - 12);
        AddChild(b);
        return b;
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("Frame");
        if (_m != null) _top.Lit = _m.Rig.TopDown;
    }

    public override void _Draw()
    {
        // under the map: the land beyond the valley, dark
        DrawCircle(new Vector2(Outer, Outer), Inner + 1, new Color("16110e"), true, -1, true);
        for (int i = 0; i < 4; i++) DrawCircle(new Vector2(Outer, Outer), Inner - 4 - i * 6, new Color(1, .9f, .7f, .012f), true, -1, true);
    }

    /// <summary>What the map is cut to: a disc.</summary>
    public partial class Disc : Control
    {
        public override void _Draw() => DrawCircle(Size / 2, Inner, Colors.White, true, -1, true);
    }

    /// <summary>The ring: a band of black iron between brass lips, riveted, with the quarters of the world on brass plates.</summary>
    public partial class Ring : Control
    {
        public override void _Draw()
        {
            var c = Size / 2;
            float mid = (Outer + Inner) / 2, band = Outer - Inner;
            DrawArc(c + new Vector2(1, 2), mid, 0, MathF.Tau, 96, new Color(0, 0, 0, .5f), band + 3, true);
            DrawArc(c, mid, 0, MathF.Tau, 96, Craft.IronBlack, band, true);
            DrawArc(c, mid - band * .18f, 0, MathF.Tau, 96, Craft.IronEdge, band * .35f, true);
            DrawArc(c, Outer - .8f, 0, MathF.Tau, 96, Craft.Brass, 2.2f, true);
            DrawArc(c, Outer - .2f, 0, MathF.Tau, 96, Craft.BrassDark, 1, true);
            DrawArc(c, Inner + .6f, 0, MathF.Tau, 96, Craft.Brass, 2, true);
            DrawArc(c, Inner - .3f, 0, MathF.Tau, 96, new Color(0, 0, 0, .7f), 1.2f, true);
            for (int i = 0; i < 16; i++)
            {
                float a = i * MathF.Tau / 16 + MathF.Tau / 32;
                Craft.Rivet(this, c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * mid, 2);
            }
            // north up, the horde's west to the left
            foreach (var (letter, a) in new[] { ("С", -MathF.PI / 2), ("В", 0f), ("Ю", MathF.PI / 2), ("З", MathF.PI) })
            {
                var p = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * mid;
                Ornament.Diamond(this, p, 9, new Color(0, 0, 0, .5f));
                Ornament.Diamond(this, p, 8, Craft.BrassDark);
                Ornament.Diamond(this, p, 6.6f, Craft.Brass);
                DrawString(Ornament.Caps, p + new Vector2(-6, 3.6f), letter, HorizontalAlignment.Center, 12, 9, letter == "З" ? Ornament.Blood.Darkened(.3f) : Craft.OakDark);
            }
        }
    }
}

/// <summary>
/// The face of the one chosen (or of the first of many) in a brass frame on the console; with nobody chosen, the
/// garrison's arms: a blue shield with a gold cross.
/// </summary>
public partial class PortraitFrame : Control
{
    Main _m;
    public Unit Unit;
    public const float S = 106;

    public void Init(Main m)
    {
        _m = m;
        CustomMinimumSize = new Vector2(S, S);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "Показать на поле";
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && Unit is { Dead: false } u)
        {
            _m.Rig.Focus(_m.World.WX(u.X), _m.World.WZ(u.Z));
            AcceptEvent();
        }
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r.Grow(3), new Color(0, 0, 0, .55f));
        Ornament.Gradient(this, r, new Color("2b2126"), new Color("0d0a0c"));
        if (Unit != null)
        {
            DrawTextureRect(Portraits.Of(Unit, 96), r.Grow(-4), false, Unit.Dead ? new Color(.45f, .42f, .42f) : Colors.White);
            if (Unit.Leader) DrawRect(r.Grow(-2), Ornament.Blood, false, 1.5f);
        }
        else Arms(new Rect2(r.Position + new Vector2(18, 12), r.Size - new Vector2(36, 24)));
        Craft.BrassRect(this, r, 2.5f);
        DrawRect(r.Grow(-3), Craft.BrassDark, false, 1);
        foreach (var p in new[] { r.Position, new Vector2(r.End.X, r.Position.Y), new Vector2(r.Position.X, r.End.Y), r.End })
            Ornament.Rivet(this, p, 3.2f);
    }

    /// <summary>The garrison's arms: a heater shield of blue with a gold cross, as on the banners.</summary>
    void Arms(Rect2 r)
    {
        float x0 = r.Position.X, x1 = r.End.X, y0 = r.Position.Y, w = r.Size.X, h = r.Size.Y, cx = x0 + w / 2;
        var pts = new List<Vector2> { new(x0, y0), new(x1, y0) };
        // straight down the sides, then curving in to the point
        for (int i = 0; i <= 10; i++)
        {
            float t = i / 10f;
            pts.Add(new Vector2(x1 - w / 2 * (1 - MathF.Cos(t * MathF.PI / 2)), y0 + h * .45f + h * .55f * MathF.Sin(t * MathF.PI / 2)));
        }
        for (int i = 9; i >= 0; i--)
        {
            float t = i / 10f;
            pts.Add(new Vector2(x0 + w / 2 * (1 - MathF.Cos(t * MathF.PI / 2)), y0 + h * .45f + h * .55f * MathF.Sin(t * MathF.PI / 2)));
        }
        var poly = pts.ToArray();
        var shadow = new Vector2[poly.Length];
        for (int i = 0; i < poly.Length; i++) shadow[i] = poly[i] + new Vector2(1.5f, 2);
        DrawColoredPolygon(shadow, new Color(0, 0, 0, .5f));
        DrawColoredPolygon(poly, new Color("2f4d7a"));
        DrawRect(new Rect2(cx - 4, y0 + 4, 8, h * .84f), new Color("b8913e"));
        DrawRect(new Rect2(x0 + 6, y0 + h * .3f, w - 12, 8), new Color("b8913e"));
        var rim = new Vector2[poly.Length + 1];
        poly.CopyTo(rim, 0); rim[^1] = poly[0];
        DrawPolyline(rim, Craft.BrassLight, 1.6f, true);
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}
