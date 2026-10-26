using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

/// <summary>
/// The map of valleys as it is played, over its land (AtlasView.cs, AtlasFigures.cs), as «Heroes» shows its world: the
/// day, the hour, the realm's gold, the pause and the speed, and the way back into the valley along the top; our heroes
/// down the right, their faces, a click choosing one and a second his card; under them the hero chosen — his army, where
/// he goes — or the valley chosen, with what can be done there. A click on the land with a hero chosen shows the way he
/// would walk there, a second click (or the second button) sends him; standing in a cell, «Войти в клетку» ({EnterCell})
/// takes him and his army into its valley. A hero come up against the enemy is asked in a card whether to go in or
/// fight it out at once; an army of the horde on its way to a valley of ours, whether to go and hold it, fight it out at
/// once before the walls, or wait.
/// </summary>
public partial class AtlasHud : Control
{
    Main _m;
    static Campaign C => Main.Atlas;
    AtlasView _view;
    AtlasFigures _figs;
    Label _when, _gold;
    readonly List<Button> _speeds = new();
    VBoxContainer _heroes, _about;
    Button _enter;
    PlatePanel _card;
    Label _cardTitle, _cardText;
    HBoxContainer _cardButtons;
    FirstLook _first;
    /// <summary>The map's own frame, up while the map is (the cards hang outside it, seen in the valley too).</summary>
    Control _frame;
    readonly List<MapParty> _arrived = new(), _warned = new();
    MapParty _warning;
    double _listAt;
    string _listKey = "";
    MapParty _lastClicked;
    double _lastClickAt;

    public const float Side = 340;
    /// <summary>The cell chosen on the map (with no hero chosen, what the right side tells of).</summary>
    public MapCell Cell { get; private set; }
    // (a hero fallen or gone off the map is chosen no more)
    public MapParty Hero => (_figs != null ? _figs.Chosen : _pick) is { } h && C != null && C.Parties.Contains(h) ? h : null;
    /// <summary>The hero chosen on the big map, while its own drawing of hexes is not yet there to keep him (no land figures).</summary>
    MapParty _pick;
    /// <summary>The way the hero chosen would walk on the big map, as planned and shown (MapWalk.Plan).</summary>
    public List<HexStep> Planned { get; private set; }
    (int col, int row) _plannedTo = (-1, -1);
    public bool IsOpen => _frame?.Visible == true;
    public bool CardShown => _card?.Visible == true;
    public MapParty CardParty => _arrived.Count > 0 ? _arrived[0] : null;
    public MapParty Warning => _warning;
    public bool EnterShown => _enter?.Visible == true;
    public AtlasFigures Figures => _figs;

    public void Init(Main m, AtlasView view)
    {
        _m = m;
        _view = view;
        // (the big map is drawn by its own view of hexes: no land, no figures of the land here)
        if (view != null)
        {
            _figs = new AtlasFigures();
            view.Figures.AddChild(_figs);
            _figs.Init(view);
            view.Clicked += OnClicked;
        }
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _frame = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_frame);

        // the top: the name, the day and the hour, the gold, the pause and the speeds, and back into the valley
        var top = new PlatePanel(10);
        top.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        top.OffsetBottom = 52;
        _frame.AddChild(top);
        // (the medallions of the map, the hero, the menu and the help hang at its right end, over it)
        var pad = new MarginContainer();
        pad.AddThemeConstantOverride("margin_right", 176);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        pad.AddChild(row);
        top.AddChild(pad);
        row.AddChild(Hud.Text("Карта долин", Ornament.Display, 24, Ornament.BronzeLight));
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _when = Hud.Text("", Ornament.SerifBold, 16, Ornament.Parchment);
        _when.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(_when);
        _gold = Hud.Text("", Ornament.SerifBold, 16, Ornament.Parchment);
        _gold.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(_gold);
        foreach (var (text, tip, act) in new (string, string, Action)[]
        {
            ("II", "Пауза ({Pause})", () => _m.Command.TogglePause()),
            ("×1", "Обычная скорость", () => _m.Command.SetSpeed(1)),
            ("×2", "Вдвое быстрее", () => _m.Command.SetSpeed(2)),
            ("×3", "Втрое быстрее", () => _m.Command.SetSpeed(3)),
        })
        {
            var b = Small(text, tip, act);
            b.CustomMinimumSize = new Vector2(40, 32);
            row.AddChild(b);
            _speeds.Add(b);
        }
        row.AddChild(Small("До рассвета", "Время до следующего рассвета: на карте сразу, долины проживут его сами. Ход героев на рассвете снова полный.", () => _m.ToDawn()));
        row.AddChild(Small("В долину", "Вернуться в долину, где вы сейчас ({Board}).", () => _m.CloseAtlas()));

        // the right: our heroes, and the hero or the valley chosen
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(Side, 0) };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.RightWide);
        scroll.OffsetLeft = -Side - 12; scroll.OffsetRight = -12; scroll.OffsetTop = 64; scroll.OffsetBottom = -12;
        scroll.MouseFilter = MouseFilterEnum.Ignore;
        _frame.AddChild(scroll);
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        right.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(right);
        _heroes = Section(right, "Герои");
        _about = Section(right, null);

        // under the middle: into the cell the hero stands in
        _enter = new Button { FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand, Visible = false, CustomMinimumSize = new Vector2(320, 48) };
        _enter.AddThemeFontOverride("font", Ornament.SerifBold);
        _enter.AddThemeFontSizeOverride("font_size", 18);
        _enter.Pressed += () => EnterChosen();
        _frame.AddChild(_enter);

        _first = new FirstLook();
        _frame.AddChild(_first);

        // the card of a hero come up against the enemy, or of an army of the horde coming
        _card = new PlatePanel(14) { Visible = false };
        var cc = new VBoxContainer();
        cc.AddThemeConstantOverride("separation", 8);
        _cardTitle = Hud.Text("", Ornament.SerifBold, 16, Ornament.BronzeLight);
        cc.AddChild(_cardTitle);
        _cardText = Hud.Text("", Ornament.Serif, 13, Ornament.Parchment, wrap: true);
        _cardText.CustomMinimumSize = new Vector2(420, 0);
        cc.AddChild(_cardText);
        _cardButtons = new HBoxContainer();
        _cardButtons.AddThemeConstantOverride("separation", 8);
        cc.AddChild(_cardButtons);
        _card.AddChild(cc);
        AddChild(_card);
    }

    VBoxContainer Section(VBoxContainer into, string caption)
    {
        var plate = new PlatePanel(12);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        plate.AddChild(box);
        into.AddChild(plate);
        var inner = new VBoxContainer();
        inner.AddThemeConstantOverride("separation", 6);
        if (caption != null) box.AddChild(Hud.CapsLabel(caption, 10));
        box.AddChild(inner);
        return inner;
    }

    static Button Small(string text, string tip, Action act)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, TooltipText = Bindings.Fill(tip), MouseDefaultCursorShape = CursorShape.PointingHand };
        b.AddThemeFontSizeOverride("font_size", 14);
        b.Pressed += act;
        return b;
    }

    static Button Big(string text, string tip, Action act)
    {
        var b = new Button { Text = Bindings.Fill(text), FocusMode = FocusModeEnum.None, TooltipText = Bindings.Fill(text + "\n" + tip), MouseDefaultCursorShape = CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(Side - 36, 34) };
        b.AddThemeFontOverride("font", Ornament.SerifBold);
        b.AddThemeFontSizeOverride("font_size", 14);
        b.Pressed += act;
        return b;
    }

    static Label Line(string text, Color color)
    {
        var l = Hud.Text(Bindings.Fill(text), Ornament.Serif, 13, color, wrap: true);
        l.CustomMinimumSize = new Vector2(Side - 36, 0);
        return l;
    }

    // ------------------------------------------------------------ open and shut

    public void Open()
    {
        _frame.Visible = true;
        Juice.FadeIn(_frame, .15);
        if (Hero == null || !C.Parties.Contains(Hero))
        {
            var first = C.Parties.Find(p => p.Owner == Holder.Ours && p.Party.Commander != null) ?? C.Parties.Find(p => p.Owner == Holder.Ours);
            Choose(first, focus: true);
            if (first == null && _view != null) { Cell = C.Here; var (x, z) = _view.Atlas.Center(C.Here); _view.Focus(x, z); }
        }
        else Focus(Hero);
        Refresh();
        if (Settings.Meet("atlas"))
            _first.Show("Карта долин\nЩёлкните героя, потом место на карте: появится путь и время в пути. Щёлкните ещё раз или правой кнопкой, и герой пойдёт. " +
                "Стоя в долине, нажмите «Войти в клетку» ({EnterCell}): герой с армией войдёт в неё. {Board} — вернуться в долину.");
    }

    public void Close()
    {
        _frame.Visible = false;
        _first.Visible = false;
    }

    // ------------------------------------------------------------ choosing and leading

    /// <summary>A hero of ours chosen (or none), the camera to him.</summary>
    public void Choose(MapParty p, bool focus = false)
    {
        if (_figs != null)
        {
            if (_figs.Chosen != p) _figs.Hide();
            _figs.Chosen = p;
        }
        else { if (_pick != p) Planned = null; _pick = p; }
        if (p != null) Cell = C.CellOf(p);
        if (p != null && focus) Focus(p);
        Refresh();
    }

    /// <summary>The big map's drawing (HexMapView.cs) brought over a hero, where there is no land of the small maps to.</summary>
    public Action<MapParty> FocusOn;

    void Focus(MapParty p)
    {
        if (_figs == null || _view == null) { if (p != null) FocusOn?.Invoke(p); return; }
        var (x, z, _) = _figs.Spot(p);
        _view.Focus(x, z);
    }

    /// <summary>A click on the land: a hero of ours under it chosen (chosen again, his card); with a hero chosen, the way shown, and on a second click on it, or with the second button, walked.</summary>
    void OnClicked(Vector2 spot, MouseButton button)
    {
        if (C == null || !IsOpen) return;
        if (button == Bindings.First)
        {
            var who = _figs.At(spot);
            if (who != null)
            {
                bool again = who == Hero || who == _lastClicked && Time.GetTicksMsec() / 1000.0 - _lastClickAt < .4;
                _lastClicked = who; _lastClickAt = Time.GetTicksMsec() / 1000.0;
                Choose(who);
                if (again && who.Party.Commander?.Hero != null) _m.Hud.HeroPane.Show(who);
                return;
            }
            if (Hero != null)
            {
                if (_figs.Shown != null && new Vector2(_figs.ShownTo.X - spot.X, _figs.ShownTo.Z - spot.Y).Length() < AtlasWalk.Spacing(_view.Atlas) * .08f) Go(spot);
                else Preview(spot);
                return;
            }
            Cell = _view.Atlas.CellAt(spot.X, spot.Y) ?? Cell;
            Refresh();
        }
        else if (button == Bindings.Second && Hero != null) Go(spot);
    }

    /// <summary>
    /// A click on a hex of the big map (HexMapView.cs): a hero of ours standing on it chosen (chosen again, his card); with
    /// a hero chosen, the way planned to it, and on a second click on the same hex, or with the second button, walked.
    /// </summary>
    public void HexClicked(int col, int row, MouseButton button)
    {
        if (C == null || !IsOpen || !C.Big) return;
        if (button == Bindings.First)
        {
            var who = C.Parties.Find(p => p.Owner == Holder.Ours && p.Col == col && p.Row == row && !p.Moving);
            if (who != null && (who != Hero || Planned == null))
            {
                bool again = who == Hero || who == _lastClicked && Time.GetTicksMsec() / 1000.0 - _lastClickAt < .4;
                _lastClicked = who; _lastClickAt = Time.GetTicksMsec() / 1000.0;
                Choose(who);
                if (again && who.Party.Commander?.Hero != null) _m.Hud.HeroPane.Show(who);
                return;
            }
            if (Hero != null) { PlanTo(col, row); return; }
            Cell = C.Cell(col, row) ?? Cell;
            Refresh();
        }
        else if (button == Bindings.Second && Hero != null) GoTo(col, row);
    }

    /// <summary>The way the hero chosen would walk to a spot, shown; false if there is none.</summary>
    public bool Preview(Vector2 spot)
    {
        var way = Hero == null ? null : C.WayTo(Hero, spot.X, spot.Y);
        if (way == null) { _m.Hud.Float("туда не пройти", Ornament.Blood); _figs.Hide(); return false; }
        _figs.Show(Hero, way.Value.way, way.Value.seconds);
        Cell = _view.Atlas.CellAt(spot.X, spot.Y) ?? Cell;
        Refresh();
        return true;
    }

    /// <summary>The hero chosen sent to a spot: he walks there. False if he cannot.</summary>
    public bool Go(Vector2 spot)
    {
        if (Hero == null) return false;
        if (_figs.Shown == null || new Vector2(_figs.ShownTo.X - spot.X, _figs.ShownTo.Z - spot.Y).Length() > 2) Preview(spot);
        bool ok = C.Lead(Hero, spot.X, spot.Y);
        if (!ok) { _m.Hud.Float("туда не пройти", Ornament.Blood); return false; }
        Sound.Ui("tick");
        _arrived.Remove(Hero);
        Refresh();
        return true;
    }

    /// <summary>
    /// The big map: the way the hero chosen would walk to a hex, planned with its days (green today, the rest after: the
    /// drawing of hexes shows <see cref="Planned"/>); false if there is none. Planned again on the same hex, he goes.
    /// </summary>
    public bool PlanTo(int col, int row)
    {
        if (Hero == null || !C.Big) return false;
        if (Planned != null && _plannedTo == (col, row)) return GoTo(col, row);
        var steps = MapWalk.Plan(C, Hero, col, row);
        if (steps == null || steps.Count == 0) { _m.Hud.Float("туда не пройти", Ornament.Blood); Planned = null; return false; }
        Planned = steps; _plannedTo = (col, row);
        Cell = C.Cell(col, row) ?? Cell;
        Refresh();
        return true;
    }

    /// <summary>The big map: the hero chosen sent to a hex; false if he cannot go there.</summary>
    public bool GoTo(int col, int row)
    {
        if (Hero == null || !C.Big || C.Cell(col, row) is not { } to) return false;
        if (!C.Send(Hero, to)) { _m.Hud.Float("туда не пройти", Ornament.Blood); return false; }
        Planned = null; _plannedTo = (-1, -1);
        Sound.Ui("tick");
        _arrived.Remove(Hero);
        Refresh();
        return true;
    }

    /// <summary>The hero chosen stops where he is.</summary>
    void Halt()
    {
        if (Hero == null) return;
        Hero.Trail = null;
        // (on the big map a step paid for is walked to its end)
        if (C.Big && Hero.StepCost > 0 && Hero.Path is { Count: > 1 }) Hero.Path.RemoveRange(1, Hero.Path.Count - 1);
        else { Hero.Path?.Clear(); Hero.Progress = 0; }
        Hero.Steps = MapWalk.StepsOf(C, Hero);
        Planned = null;
        _figs?.Hide();
        Refresh();
    }

    /// <summary>«Войти в клетку»: the hero chosen and his army into the valley of the cell he stands in.</summary>
    public bool EnterChosen()
    {
        var p = Hero;
        if (p == null || p.Walking || p.Moving || !C.Parties.Contains(p)) return false;
        _arrived.Remove(p);
        _m.EnterHero(p);
        return true;
    }

    /// <summary>A hero of ours come to the enemy (or to a treasure): his card, the camera to him.</summary>
    public void Arrived(MapParty p)
    {
        if (p == null || _arrived.Contains(p)) return;
        _arrived.Add(p);
        if (IsOpen) Choose(p, focus: true);
        Refresh();
    }

    /// <summary>An army of the horde on its way to a valley of ours: its card, once.</summary>
    public void Warn(MapParty enemy)
    {
        if (enemy == null || _warned.Contains(enemy)) return;
        _warned.Add(enemy);
        _warning = enemy;
        Refresh();
    }

    public QuickResult FightWith(MapParty p)
    {
        _arrived.Remove(p);
        var r = _m.QuickFight(p);
        if (Hero == p && !C.Parties.Contains(p)) Choose(null);
        Refresh();
        return r;
    }

    public QuickResult DefendNow(MapParty enemy)
    {
        _warning = null;
        var r = C.DefendNow(enemy);
        _m.TellMap();
        Refresh();
        return r;
    }

    // ------------------------------------------------------------ keys

    /// <summary>The map's keys: {EnterCell} goes into the cell, Esc lets the hero chosen go.</summary>
    public bool Key(InputEventKey k)
    {
        if (!IsOpen) return false;
        if (Bindings.Is(k, Act.EnterCell)) { if (CardParty is { } p) _m.EnterHero(p); else EnterChosen(); return true; }
        if (k.PhysicalKeycode == global::Godot.Key.Escape && (Hero != null || _figs?.Shown != null || Planned != null))
        {
            if (_figs?.Shown != null) _figs.Hide(); else if (Planned != null) Planned = null; else Choose(null);
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------ what is shown

    public override void _Process(double delta)
    {
        if (C == null) return;
        var vp = GetViewportRect().Size;
        _arrived.RemoveAll(p => !C.Parties.Contains(p));
        if (_warning != null && (!C.Parties.Contains(_warning) || !_warning.Moving && !_warning.Walking)) _warning = null;
        bool card = (CardParty != null || _warning != null || C.Chests.Count > 0 || DwellingDue != null) && !_m.Hud.MenuOpen;
        if (card != _card.Visible) { _card.Visible = card; Card(); }
        // under the map's bar in the middle of the land; in the valley, under its beam
        if (_card.Visible) _card.Position = IsOpen ? new Vector2(MathF.Round((vp.X - Side - _card.Size.X) / 2), 64) : new Vector2(MathF.Round((vp.X - _card.Size.X) / 2), Hud.TopBand + 10);
        if (!IsOpen) return;
        _first.Position = new Vector2(16, vp.Y - _first.Size.Y - 16);
        // «Войти в клетку» under the middle while the hero chosen stands in a cell
        var h = Hero;
        bool can = h != null && C.Parties.Contains(h) && !h.Walking && !h.Moving && CardParty != h;
        _enter.Visible = can;
        if (can)
        {
            _enter.Text = Bindings.Fill($"Войти в клетку: {Campaign.Names(C.CellOf(h)).nom}  ({{EnterCell}})");
            _enter.Size = Vector2.Zero;
            _enter.Position = new Vector2(MathF.Round((vp.X - Side - _enter.Size.X) / 2), vp.Y - _enter.Size.Y - 28);
        }
        if ((_listAt -= delta) <= 0) { _listAt = .5; Lists(); }
    }

    public void Refresh()
    {
        if (C == null || _about == null) return;
        Lists();
        About();
        Card();
    }

    void Lists()
    {
        var t = C.Clock;
        _when.Text = $"День {t.Day} · {MapBoard.Hour(t.At)}{(t.At >= DayClock.DayLength ? " · ночь" : "")}";
        _gold.Text = C.Big
            ? $"Золото {Fmt.Int(C.StockOf(Res.Gold))} · ртуть {C.Rare[0]:0} · сера {C.Rare[1]:0} · кристаллы {C.Rare[2]:0} · самоцветы {C.Rare[3]:0}"
            : $"Золото {Fmt.Int(C.Gold)}";
        for (int i = 0; i < _speeds.Count; i++)
        {
            bool on = i == 0 ? _m.Paused : !_m.Paused && _m.Speed == i;
            _speeds[i].AddThemeColorOverride("font_color", on ? Ornament.EmberGlow : Ornament.Parchment);
        }
        // the heroes' list made anew only when what it says changed (a button made anew under the mouse loses its click)
        var key = new System.Text.StringBuilder(Hero?.Id.ToString() ?? "-");
        foreach (var p in C.Parties) if (p.Owner == Holder.Ours) key.Append('|').Append(p.Id).Append(p.Party.Commander?.Hero?.Level).Append(p.Party.Commander?.Hero?.Pending).Append(Status(p));
        if (key.ToString() == _listKey) return;
        _listKey = key.ToString();
        foreach (var c in _heroes.GetChildren()) c.QueueFree();
        int shown = 0;
        foreach (var p in C.Parties)
        {
            if (p.Owner != Holder.Ours) continue;
            shown++;
            var hero = p.Party.Commander?.Hero;
            string name = hero != null ? $"{hero.Name} · ур. {hero.Level}{(hero.Pending ? " · новый уровень" : "")}" : MapBoard.Who(p);
            var pp = p;
            var b = Small($"{name}\n{Status(p)}", "Выбрать героя. Ещё раз — его карточка.", () =>
            {
                if (Hero == pp && pp.Party.Commander?.Hero != null) _m.Hud.HeroPane.Show(pp);
                Choose(pp, focus: true);
            });
            b.Alignment = HorizontalAlignment.Left;
            b.CustomMinimumSize = new Vector2(Side - 36, 52);
            if (p.Party.Commander is { } cm) { b.Icon = Portraits.Of(cm.Kind, cm.Look, 40); b.ExpandIcon = false; }
            if (p == Hero) b.AddThemeColorOverride("font_color", Ornament.EmberGlow);
            _heroes.AddChild(b);
        }
        if (shown == 0) _heroes.AddChild(Line("На карте нет героев. Выберите в долине командира и бойцов: {Board}, «Вывести на карту».", Ornament.Aged));
    }

    string Status(MapParty p)
    {
        if (p.Walking || p.Moving)
        {
            var goal = C.At(p.GoalQ, p.GoalR) ?? C.CellOf(p);
            // (on the big map by the days its moves give)
            if (C.Big && p.Steps.Count > 0) return $"идёт: {Campaign.Names(goal).nom}, {(p.Steps[^1].Day == 0 ? "сегодня" : $"дней {p.Steps[^1].Day + 1}")}{(p.Tired ? ", ждёт рассвета" : "")}";
            return $"идёт: {Campaign.Names(goal).nom}, {AtlasFigures.Hours(C.WalkLeft(p))}";
        }
        return p.Waits ? $"у врага: {Campaign.Names(C.CellOf(p)).nom}" : $"стоит: {Campaign.Names(C.CellOf(p)).nom}";
    }

    /// <summary>«2 мечника, 2 лучника»: who goes with a hero.</summary>
    public static string Army(MapParty p)
    {
        var counts = new List<(UnitType t, int n)>();
        foreach (var m in p.Party.Members)
        {
            if (m.Kind == Kinds.Captain) continue;
            int i = counts.FindIndex(c => c.t == m.Kind);
            if (i < 0) counts.Add((m.Kind, 1)); else counts[i] = (m.Kind, counts[i].n + 1);
        }
        var parts = new List<string>();
        foreach (var (t, n) in counts) parts.Add($"{t.Name.ToLowerInvariant()} {n}");
        return parts.Count == 0 ? "никого" : string.Join(", ", parts);
    }

    void About()
    {
        foreach (var c in _about.GetChildren()) c.QueueFree();
        var h = Hero;
        if (h != null && C.Parties.Contains(h))
        {
            var hero = h.Party.Commander?.Hero;
            _about.AddChild(Hud.Text(hero != null ? $"Командир {hero.Name}" : MapBoard.Who(h), Ornament.SerifBold, 17, Ornament.BronzeLight));
            if (hero != null) _about.AddChild(Line($"Уровень {hero.Level}. Умения: {Heroes.Skills(hero)}.", Ornament.Parchment));
            _about.AddChild(Line($"Армия: {Army(h)}. Её видно, когда герой войдёт в долину.", Ornament.Aged));
            _about.AddChild(Line(Upper(Status(h)) + ".", Ornament.Parchment));
            if (C.Big)
            {
                _about.AddChild(Line($"Ход: {h.MovesLeft} из {h.MovesMax}." + (h.Tired ? " Ждёт рассвета: на следующий шаг не хватает." : "")
                    + (Planned is { Count: > 0 } pl ? $" Путь: {pl.Count} кл., дней {pl[^1].Day + 1}. Щёлкните ещё раз — идти." : ""), h.Tired ? Ornament.Aged : Ornament.Moss));
            }
            var cell = C.CellOf(h);
            CellLines(cell);
            if (h.Walking || h.Moving) _about.AddChild(Big("Стоять", "Герой остановится там, где он сейчас.", Halt));
            else
            {
                _about.AddChild(Big($"Войти в клетку ({{EnterCell}})", "Герой с армией войдёт в долину, и вы будете играть в ней.", () => EnterChosen()));
                if (C.Foes(cell) > 0 && cell.Owner != Holder.Ours)
                    _about.AddChild(Big(Campaign.Untaken(cell) && cell.Trove == TroveKind.Shrine ? "Испытание сразу" : "Бой сразу",
                        "Бой решится сразу, по силе отрядов: вдвое сильнее побеждает 9 раз из 10, но теряет людей.", () => FightWith(h)));
            }
            if (hero != null) _about.AddChild(Big("Карточка героя", "Опыт, умения, выбор умения на новом уровне.", () => _m.Hud.HeroPane.Show(h)));
            return;
        }
        var c0 = Cell ?? C.Here;
        _about.AddChild(Hud.Text(MapBoard.NameOf(c0) + (c0 == C.Here ? " · вы здесь" : ""), Ornament.SerifBold, 17, Ornament.BronzeLight));
        CellLines(c0);
        // our own valleys are gone into without a hero, as a town in «Heroes»
        if (c0.Owner == Holder.Ours)
            _about.AddChild(Big(c0 == C.Here ? "В долину" : "Войти в долину", "Открыть эту долину: её город, её стены, её людей.", () => _m.GoToValley(c0)));
        // the chosen of the valley
        int chosen = 0; bool captain = false;
        var group = new List<Unit>();
        foreach (var u in _m.Selected) if (!u.Dead && u.Team == Team.Player) { chosen++; captain |= u.T == Kinds.Captain; group.Add(u); }
        if (chosen > 0 && (captain || Main.Scouts(group)))
        {
            _about.AddChild(Big($"Вывести на карту ({chosen})", "Выбранные в долине сразу выйдут на карту у её края. В отряде нужен командир, или до 3 лёгких бойцов в разведку.", () =>
            {
                string why = _m.LeaveToMap(group);
                _m.Hud.Float(why ?? "отряд на карте", why == null ? Ornament.Moss : Ornament.Blood);
            }));
        }
    }

    static string Upper(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    void CellLines(MapCell cell)
    {
        if (!C.Seen(cell)) { _about.AddChild(Line("Долина в тумане. Подведите героя ближе, чтобы её разведать.", Ornament.Aged)); return; }
        string owner = cell.Owner switch { Holder.Ours => "Наша.", Holder.Horde => "Во власти орды.", _ => "Ничья." };
        string holds = cell.Holding switch
        {
            Holding.Citadel => "Цитадель.",
            Holding.HordeTown => cell.Owner == Holder.Ours ? "Взятый город орды." : "Город орды за частоколом.",
            _ => cell.Outpost ? "Застава." : "Место для заставы.",
        };
        _about.AddChild(Line($"{MapBoard.NameOf(cell)}. {owner} {holds}", Ornament.Parchment));
        int foes = C.Foes(cell);
        if (foes > 0 && cell.Owner != Holder.Ours && !Campaign.Untaken(cell))
            _about.AddChild(Line(cell.Owner == Holder.Horde ? $"Гарнизон орды: {foes}." : $"Долину стерегут звери: {foes}.", Ornament.Blood));
        if (Campaign.Untaken(cell))
            _about.AddChild(Line($"{Troves.Name(cell.Trove)}. Награда: {Campaign.RewardText(cell)}.", Ornament.EmberGlow));
        if (cell.Thing is { } t && C.Big) _about.AddChild(Line(ThingLine(cell, t), t.Owner == Holder.Ours ? Ornament.Moss : Ornament.EmberGlow));
        double gold = Campaign.AwayGold(cell);
        if (gold > 0 && cell != C.Here) _about.AddChild(Line($"Без вас платит {Fmt.Int(gold)} золота в минуту.", Ornament.Moss));
    }

    /// <summary>A dwelling a hero of ours stands at with someone to hire, the player not yet done with it.</summary>
    (MapParty p, MapCell cell)? DwellingDue
    {
        get
        {
            foreach (var d in C.Dwellings)
                if (C.Parties.Contains(d.p) && d.p.Q == d.cell.Q && d.p.R == d.cell.R && d.cell.Thing is { Stock: > 0 } && !_doneWith.Contains(d.cell)) return d;
            return null;
        }
    }
    readonly HashSet<MapCell> _doneWith = new();

    /// <summary>What a thing of the big map is and gives, in a line.</summary>
    string ThingLine(MapCell cell, MapThing t) => t.Kind switch
    {
        ThingKind.Mine => $"{t.Name}: {MapThing.Amount_(t.Res, t.Amount)} в день. {(t.Owner == Holder.Ours ? "Наш." : t.Owner == Holder.Horde ? "Под флагом орды." : "Ничей: войдите героем, чтобы поднять флаг.")}",
        ThingKind.Pile => t.Taken ? "Здесь ничего не осталось." : $"Куча: {MapThing.Amount_(t.Res, t.Amount)}. Подберёт герой.",
        ThingKind.Chest => t.Taken ? "Сундук открыт." : $"Сундук: {Campaign.ChestOf(t).gold} золота или {Campaign.ChestOf(t).xp} опыта.",
        ThingKind.Campfire => t.Taken ? "Костёр погас." : "Костёр: у него оставлены золото и припасы.",
        ThingKind.Windmill => t.Week == C.Week ? "Мельница: на этой неделе уже взято." : $"Мельница: раз в неделю {MapThing.NameOf(t.Res)}.",
        ThingKind.WaterWheel => t.Week == C.Week ? "Водяное колесо: на этой неделе уже взято." : "Водяное колесо: раз в неделю 75 золота.",
        ThingKind.Learning => $"Камень знаний: +{Campaign.LearningXp} опыта каждому герою один раз.",
        ThingKind.Stables => $"Конюшни: +{Campaign.StablesMoves} хода до конца недели.",
        ThingKind.Observatory => $"Обсерватория: туман рассеется на {Campaign.ObservatoryReach} клеток вокруг.",
        ThingKind.Signpost => t.Text ?? "Указатель.",
        ThingKind.Shrine => t.Taken ? "Святыня: благословение уже получено." : $"Святыня: {Campaign.BlessingText(t.Blessing)}.",
        ThingKind.Dwelling => $"Жилище: {t.Breed.Name.ToLowerInvariant()}, готовы наняться {t.Stock}, по {Campaign.HireCost(t.Breed)} зол.",
        ThingKind.Monster => cell.Sum.Keep.Count > 0 ? $"Стража: {cell.Sum.Keep.Count}." : "Путь свободен.",
        _ => t.Name,
    };

    void Card()
    {
        foreach (var c in _cardButtons.GetChildren()) c.QueueFree();
        _doneWith.RemoveWhere(c => !C.Dwellings.Exists(d => d.cell == c));
        if (C.Chests.Count > 0)
        {
            var (_, cell) = C.Chests[0];
            var (gold, xp) = Campaign.ChestOf(cell.Thing);
            _cardTitle.Text = "Сундук";
            _cardText.Text = $"В сундуке {gold} золота. Можно отдать его в казну или раздать знания: герой получит {xp} опыта.";
            var g = Small($"{gold} золота", "Золото в казну.", () => { _m.Hud.Log(C.OpenChest(cell, gold: true), Hud.Kind.Ok); Refresh(); });
            g.AddThemeFontOverride("font", Ornament.SerifBold);
            _cardButtons.AddChild(g);
            _cardButtons.AddChild(Small($"{xp} опыта", "Опыт герою, открывшему сундук.", () => { _m.Hud.Log(C.OpenChest(cell, gold: false), Hud.Kind.Ok); Refresh(); }));
        }
        else if (DwellingDue is { } dw)
        {
            var t = dw.cell.Thing;
            int cost = Campaign.HireCost(t.Breed);
            _cardTitle.Text = $"Жилище: {t.Breed.Name.ToLowerInvariant()}";
            _cardText.Text = $"Готовы наняться: {t.Stock}, по {cost} золота. Нанятые пойдут с героем.";
            var all = Small($"Нанять всех ({t.Stock * cost} зол.)", "Нанять всех, на сколько хватит золота.", () => { C.Hire(dw.cell, t.Stock); _m.TellMap(); Refresh(); });
            all.AddThemeFontOverride("font", Ornament.SerifBold);
            _cardButtons.AddChild(all);
            _cardButtons.AddChild(Small("Одного", $"Нанять одного за {cost} золота.", () => { C.Hire(dw.cell, 1); _m.TellMap(); Refresh(); }));
            _cardButtons.AddChild(Small("Позже", "Вернуться к жилищу можно в любой день. Новые готовы наняться каждую неделю.", () => { _doneWith.Add(dw.cell); Refresh(); }));
        }
        else if (CardParty is { } p)
        {
            var cell = C.CellOf(p);
            int foes = C.Foes(cell);
            bool trove = Campaign.Untaken(cell), shrine = trove && cell.Trove == TroveKind.Shrine;
            _cardTitle.Text = $"Герой дошёл: {Campaign.Names(cell).nom}";
            _cardText.Text = $"{MapBoard.Who(p)}." + (trove ? $" {Troves.Name(cell.Trove)}: {(shrine ? "удержите алтарь против мертвецов" : $"стража {foes}")}, награда {Campaign.RewardText(cell)}."
                : foes > 0 ? $" Там врагов: {foes}." : "");
            var enter = Small("Войти", "Герой с армией войдёт в долину, и вы будете играть в ней.", () => { _arrived.Remove(p); _m.EnterHero(p); });
            enter.AddThemeFontOverride("font", Ornament.SerifBold);
            _cardButtons.AddChild(enter);
            if (foes > 0) _cardButtons.AddChild(Small(shrine ? "Испытание сразу" : "Бой сразу", "Бой решится сразу, по силе отрядов.", () => FightWith(p)));
            _cardButtons.AddChild(Small("Позже", "Герой подождёт у долины.", () => { _arrived.Remove(p); Refresh(); }));
        }
        else if (_warning is { } e)
        {
            var goal = C.At(e.GoalQ, e.GoalR);
            int n = e.Party.Members.Count;
            _cardTitle.Text = $"Орда идёт на {Campaign.Names(goal).acc}";
            _cardText.Text = $"{n} {Sim.Plural(n, "налётчик", "налётчика", "налётчиков")}{(e.Lord != null ? $", ведёт {e.Lord.Name}" : "")}. Будет через {AtlasFigures.Hours(C.WalkLeft(e))}.";
            var hold = Small("Войти и защищать", "Открыть эту долину: орда придёт туда набегом.", () => { _warning = null; _m.GoToValley(goal); });
            hold.AddThemeFontOverride("font", Ornament.SerifBold);
            _cardButtons.AddChild(hold);
            _cardButtons.AddChild(Small("Быстрый бой", "Бой у стен решится сразу, по силе: защищают все, кто в долине, стены помогают. Проиграв, орда идёт дальше.", () => DefendNow(e)));
            _cardButtons.AddChild(Small("Ждать", "Орда придёт сама. Долина будет защищаться без вас.", () => { _warning = null; Refresh(); }));
        }
        _card.Size = Vector2.Zero;
    }
}
