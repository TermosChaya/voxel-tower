using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

/// <summary>
/// The map of valleys, over the valley in a campaign ({Board} or the medallion beside the menu's): the field of cells,
/// each drawn by its kind with what holds it and whose it is, the fog over what nobody of ours sees, our parties as
/// their commanders' faces walking from cell to cell, the horde's armies where they are seen. Above, the day, the hour
/// and the realm's gold, the pause and the speed (the map runs as the valley does); at the right the cell chosen with
/// what can be done there, our parties and our valleys. A click on a cell chooses it, a click on a face its party; with
/// a party chosen a right click sends it there. A party come to its cell asks, in a card under the beam even with the
/// board shut, whether to go into the valley or fight its garrison at once.
/// </summary>
public partial class MapBoard : Control
{
    Main _m;
    static Campaign C => Main.Atlas;
    Control _sheet;
    Field _field;
    Label _when, _gold;
    VBoxContainer _about, _parties, _valleys, _warn;
    Control _warnPlate;
    readonly List<Button> _speeds = new();
    PlatePanel _card;
    Label _cardTitle, _cardText;
    HBoxContainer _cardButtons;
    FirstLook _first;
    readonly List<MapParty> _arrived = new();
    double _listAt;

    /// <summary>The cell chosen on the board, and the party of ours chosen (or null).</summary>
    public MapCell Chosen { get; private set; }
    public MapParty Party { get; private set; }
    public bool IsOpen => _sheet?.Visible == true;
    /// <summary>The card of a party come to its cell is up, and whose.</summary>
    public bool CardShown => _card?.Visible == true;
    public MapParty CardParty => _arrived.Count > 0 ? _arrived[0] : null;
    /// <summary>The cells drawn on the last frame, and of them those in the fog (for the self-test).</summary>
    public int Drawn => _field?.Drawn ?? 0;
    public int Fogged => _field?.Fogged ?? 0;

    public const float Side = 380;

    public void Init(Main m)
    {
        _m = m;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _sheet = new Control { MouseFilter = MouseFilterEnum.Stop, Visible = false };
        _sheet.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_sheet);
        var shade = new ColorRect { Color = Ornament.Fade(Ornament.IronDark, .97f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _sheet.AddChild(shade);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var s in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(s, 18);
        _sheet.AddChild(margin);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 12);
        margin.AddChild(col);

        // the top: the name, the day and the hour, the gold, the pause and the speeds, and the way out
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 14);
        col.AddChild(top);
        top.AddChild(Hud.Text("Карта долин", Ornament.Display, 28, Ornament.BronzeLight));
        top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _when = Hud.Text("", Ornament.SerifBold, 16, Ornament.Parchment);
        _when.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        top.AddChild(_when);
        top.AddChild(new Glyph(Icons.Coin, Ornament.BronzeLight) { CustomMinimumSize = new Vector2(24, 24), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        _gold = Hud.Text("", Ornament.SerifBold, 16, Ornament.Parchment);
        _gold.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        top.AddChild(_gold);
        var speeds = new HBoxContainer();
        speeds.AddThemeConstantOverride("separation", 4);
        top.AddChild(speeds);
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
            speeds.AddChild(b);
            _speeds.Add(b);
        }
        top.AddChild(Small("Закрыть", "Вернуться в долину ({Board} или Esc).", Close));

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 16);
        col.AddChild(body);
        _field = new Field { B = this, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop,
            TooltipText = " " };
        body.AddChild(_field);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(Side, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(scroll);
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(right);
        _warn = Section(right, "Орда идёт");
        _warnPlate = (Control)_warn.GetParent().GetParent();
        _about = Section(right, null);
        _parties = Section(right, "Отряды");
        _valleys = Section(right, "Долины");

        _first = new FirstLook();
        _sheet.AddChild(_first);

        // the card of a party come to its cell, under the beam
        _card = new PlatePanel(14) { Visible = false };
        var cc = new VBoxContainer();
        cc.AddThemeConstantOverride("separation", 8);
        _cardTitle = Hud.Text("", Ornament.SerifBold, 16, Ornament.BronzeLight);
        cc.AddChild(_cardTitle);
        _cardText = Hud.Text("", Ornament.Serif, 13, Ornament.Parchment, wrap: true);
        _cardText.CustomMinimumSize = new Vector2(400, 0);
        cc.AddChild(_cardText);
        _cardButtons = new HBoxContainer();
        _cardButtons.AddThemeConstantOverride("separation", 8);
        cc.AddChild(_cardButtons);
        _card.AddChild(cc);
        AddChild(_card);
    }

    VBoxContainer Section(VBoxContainer into, string caption)
    {
        var plate = new PlatePanel(14);
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

    // ------------------------------------------------------------ open and shut

    public void Toggle() { if (IsOpen) Close(); else Open(); }

    public void Open()
    {
        if (C == null) return;
        Chosen ??= C.Here;
        _sheet.Visible = true;
        Juice.FadeIn(_sheet, .15);
        Refresh();
        if (Settings.Meet("board"))
            _first.Show("Карта долин\nВыберите в долине командира и бойцов, щёлкните на карте другую долину и нажмите «Отправить выбранных сюда». " +
                "Отряд уйдёт за край долины и пойдёт по карте. Когда он дойдёт, войдите в долину или решите бой сразу. {Board} — закрыть карту.");
        else TroveHint();
    }

    /// <summary>The first treasure seen on the board: what treasures are and how they are taken, once.</summary>
    void TroveHint()
    {
        if (!C.Cells.Exists(c => Campaign.Untaken(c) && C.Seen(c)) || !Settings.Meet("trove")) return;
        _first.Show("Сокровища\nВ ничьих долинах лежат сокровища: заброшенный рудник, старые руины, логово зверей, святилище испытаний. " +
            "Их стерегут скелеты, волки и медведи. Перебейте стражу и подойдите к сундуку. В святилище держите людей у алтаря 3 минуты. " +
            "Наведите мышь на долину, чтобы увидеть награду и силу стражи.");
    }

    public void Close()
    {
        if (!IsOpen) return;
        _sheet.Visible = false;
        _first.Visible = false;
    }

    /// <summary>A party of ours come to the cell it was sent to: its card, and the cell chosen on the board.</summary>
    public void Arrived(MapParty p)
    {
        if (p == null || _arrived.Contains(p)) return;
        _arrived.Add(p);
        Chosen = C.CellOf(p); Party = p;
        Refresh();
    }

    public void Choose(MapCell c) { Chosen = c; Refresh(); }

    /// <summary>A party of ours chosen; chosen again, its hero's card opened.</summary>
    public void ChooseParty(MapParty p)
    {
        bool again = p != null && p == Party;
        Party = p;
        if (p != null) Chosen = p.Moving ? p.Path[^1] : C.CellOf(p);
        Refresh();
        if (again && p.Party.Commander?.Hero != null) _m.Hud.HeroPane.Show(p);
    }

    // ------------------------------------------------------------ what can be done

    /// <summary>The chosen in the valley sent toward the chosen cell; null, or why not (also floated).</summary>
    public string SendHere()
    {
        string why = _m.SendOut(Chosen);
        _m.Hud.Float(why ?? $"отряд уходит: {Valleys.Name(Chosen.Kind)}", why == null ? Ornament.Moss : Ornament.Blood);
        Refresh();
        return why;
    }

    /// <summary>The chosen party of the map led to the chosen cell (or one given).</summary>
    public bool Lead(MapCell to = null)
    {
        to ??= Chosen;
        if (Party == null || Party.Owner != Holder.Ours || to == null) return false;
        bool ok = C.Send(Party, to);
        _m.Hud.Float(ok ? $"отряд идёт: {Valleys.Name(to.Kind)}" : "туда не пройти", ok ? Ornament.Moss : Ornament.Blood);
        Refresh();
        return ok;
    }

    public void EnterWith(MapParty p) { _arrived.Remove(p); _m.EnterCell(p); }

    public QuickResult FightWith(MapParty p)
    {
        _arrived.Remove(p);
        var r = _m.QuickFight(p);
        if (Party == p && !C.Parties.Contains(p)) Party = null;
        Refresh();
        return r;
    }

    public void Later(MapParty p) { _arrived.Remove(p); Refresh(); }

    // ------------------------------------------------------------ what is shown

    public override void _Process(double delta)
    {
        if (C == null) return;
        var vp = GetViewportRect().Size;
        // the card under the beam in the middle, while the board is shut
        _arrived.RemoveAll(p => !C.Parties.Contains(p) || p.Moving);
        _card.Visible = _arrived.Count > 0 && !IsOpen && !_m.Hud.MenuOpen;
        if (_card.Visible) _card.Position = new Vector2(MathF.Round((vp.X - _card.Size.X) / 2), Hud.TopBand + 10);
        if (!IsOpen) return;
        _first.Position = new Vector2(30, vp.Y - _first.Size.Y - 30);
        _field.QueueRedraw();
        if ((_listAt -= delta) <= 0) { _listAt = .5; Lists(); }
    }

    /// <summary>All of it shown anew: the top, the cell chosen, the lists, the card.</summary>
    public void Refresh()
    {
        if (C == null || _about == null) return;
        Lists();
        About();
        Card();
        _field.QueueRedraw();
    }

    static string Upper(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>«Рудная долина», «Первая долина».</summary>
    public static string NameOf(MapCell c) => Upper(Valleys.Name(c.Kind));

    /// <summary>The hour of the map's day: the light from 6:00 to 20:00, the night from 20:00 to 6:00.</summary>
    public static string Hour(double at)
    {
        double h = at < DayClock.DayLength ? 6 + at / DayClock.DayLength * 14 : 20 + (at - DayClock.DayLength) / DayClock.NightLength * 10;
        h %= 24;
        int hh = (int)h, mm = (int)((h - hh) * 60);
        return $"{hh}:{mm:00}";
    }

    /// <summary>«Командир и 5 человек», «Разведка: 2 человека».</summary>
    public static string Who(MapParty p)
    {
        int n = p.Party.Members.Count, rest = n - (p.Party.Commander != null ? 1 : 0);
        if (p.Owner != Holder.Ours) return $"Войско орды: {n}";
        if (p.Scouting) return $"Разведка: {n} {Sim.Plural(n, "человек", "человека", "человек")}";
        return p.Party.Commander != null ? rest > 0 ? $"Командир и {rest} {Sim.Plural(rest, "человек", "человека", "человек")}" : "Командир" : $"{n} {Sim.Plural(n, "человек", "человека", "человек")}";
    }

    string Status(MapParty p)
    {
        var at = C.CellOf(p);
        if (p.Moving) return $"идёт: {Campaign.Names(p.Path[^1]).nom}, {Fmt.Clock(C.WalkLeft(p))}";
        return p.Waits ? $"ждёт у врага: {Campaign.Names(at).nom}" : $"стоит: {Campaign.Names(at).nom}";
    }

    void Lists()
    {
        if (C == null) return;
        var t = C.Clock;
        _when.Text = $"День {t.Day} · {Hour(t.At)}{(t.At >= DayClock.DayLength ? " · ночь" : "")}";
        _gold.Text = Fmt.Int(C.Gold);
        for (int i = 0; i < _speeds.Count; i++)
        {
            bool on = i == 0 ? _m.Paused : !_m.Paused && _m.Speed == i;
            _speeds[i].AddThemeColorOverride("font_color", on ? Ornament.EmberGlow : Ornament.Parchment);
        }
        // the horde's armies on their way to a valley of ours, whether they are seen or not: the news told of them
        foreach (var c in _warn.GetChildren()) c.QueueFree();
        var coming = C.Coming();
        _warnPlate.Visible = coming.Count > 0;
        foreach (var h in coming)
        {
            var goal = C.At(h.GoalQ, h.GoalR);
            // (the raiders, as the token and the news count them; the warlord leading them named apart)
            int n = h.Party.Members.Count;
            _warn.AddChild(Line($"На {Campaign.Names(goal).acc}: {n} {Sim.Plural(n, "налётчик", "налётчика", "налётчиков")}{(h.Lord != null ? $", ведёт {h.Lord.Name}" : "")}. Будет через {Fmt.Clock(C.WalkLeft(h))}.", Ornament.Blood));
        }
        foreach (var c in _parties.GetChildren()) c.QueueFree();
        int shown = 0;
        foreach (var p in C.Parties)
        {
            if (p.Owner != Holder.Ours) continue;
            shown++;
            var hero = p.Party.Commander?.Hero;
            string rank = hero == null ? "" : $" · ур. {hero.Level}{(hero.Pending ? " · новый уровень" : "")}";
            var b = Small($"{Who(p)}{rank}\n{Status(p)}", "Выбрать отряд на карте. Ещё раз — карточка его командира.", () => ChooseParty(p));
            b.Alignment = HorizontalAlignment.Left;
            b.CustomMinimumSize = new Vector2(Side - 40, 52);
            if (p.Party.Commander is { } cm) { b.Icon = Portraits.Of(cm.Kind, cm.Look, 40); b.ExpandIcon = false; }
            if (p == Party) b.AddThemeColorOverride("font_color", Ornament.EmberGlow);
            _parties.AddChild(b);
        }
        if (shown == 0) _parties.AddChild(Line("На карте нет отрядов. Выберите в долине командира и бойцов и отправьте их в другую долину.", Ornament.Aged));
        foreach (var c in _valleys.GetChildren()) c.QueueFree();
        foreach (var c in C.Cells)
        {
            if (c.Owner != Holder.Ours) continue;
            double gold = Campaign.AwayGold(c);
            string what = c.Home ? "цитадель" : "застава";
            if (gold > 0 && c != C.Here) what += $", +{Fmt.Int(gold)} зол./мин без вас";
            var b = Small($"{NameOf(c)}{(c == C.Here ? " · вы здесь" : "")}\n{what}", "Показать на карте.", () => Choose(c));
            b.Alignment = HorizontalAlignment.Left;
            b.CustomMinimumSize = new Vector2(Side - 40, 46);
            _valleys.AddChild(b);
        }
        if (C.HomeFell >= 0)
            _valleys.AddChild(Line($"Первая долина пала. Займите другую долину до дня {C.HomeFell + Campaign.DaysToRetake}.", Ornament.Blood));
    }

    static Label Line(string text, Color color)
    {
        var l = Hud.Text(text, Ornament.Serif, 13, color, wrap: true);
        l.CustomMinimumSize = new Vector2(Side - 40, 0);
        return l;
    }

    void About()
    {
        foreach (var c in _about.GetChildren()) c.QueueFree();
        var cell = Chosen ?? C.Here;
        bool seen = C.Seen(cell);
        _about.AddChild(Hud.Text(NameOf(cell) + (cell == C.Here ? " · вы здесь" : ""), Ornament.SerifBold, 17, Ornament.BronzeLight));
        if (!seen)
        {
            _about.AddChild(Line("Долина в тумане. Подведите отряд ближе, чтобы её разведать.", Ornament.Aged));
        }
        else
        {
            string owner = cell.Owner switch { Holder.Ours => "Наша.", Holder.Horde => "Во власти орды.", _ => "Ничья." };
            string holds = cell.Holding switch
            {
                Holding.Citadel => "Цитадель.",
                Holding.HordeTown => cell.Owner == Holder.Ours ? "Взятый город орды." : "Город орды за частоколом.",
                _ => cell.Outpost ? "Застава." : "Место для заставы.",
            };
            _about.AddChild(Line($"{owner} {holds}", Ornament.Parchment));
            int foes = C.Foes(cell);
            bool trove = Campaign.Untaken(cell);
            if (foes > 0 && cell.Owner != Holder.Ours && !trove)
                _about.AddChild(Line(cell.Owner == Holder.Horde ? $"Гарнизон орды: {foes}." : $"Долину стерегут звери: {foes}. Застава встанет, только когда их не будет.", Ornament.Blood));
            foreach (var (text, color) in TroveLines(cell)) _about.AddChild(Line(text, color));
            double gold = Campaign.AwayGold(cell), mines = ValleySummary.MineGold(cell.Kind);
            if (gold > 0 && cell != C.Here) _about.AddChild(Line($"Без вас платит {Fmt.Int(gold)} золота в минуту.", Ornament.Moss));
            else if (mines > 0 && cell.Owner != Holder.Ours) _about.AddChild(Line($"Рудники. Занятая, долина без вас платит {Fmt.Int(mines * ValleySummary.MineShare)} золота в минуту.", Ornament.Aged));
            var sum = cell.Sum;
            if (sum.Stone + sum.Logs + sum.Metal > .05)
                _about.AddChild(Line($"Добыто без вас, ждёт склада: камень {Fmt.One(sum.Stone)}, брёвна {Fmt.One(sum.Logs)}, металл {Fmt.One(sum.Metal)} м³.", Ornament.Aged));
            if (cell.Owner == Holder.None && sum.Raised > 0 && sum.Raised < 1) _about.AddChild(Line($"Застава строится: {Math.Round(sum.Raised * 100)} %.", Ornament.Moss));
        }
        // what can be done here
        var acts = new VBoxContainer();
        acts.AddThemeConstantOverride("separation", 6);
        _about.AddChild(acts);
        int chosen = 0; bool captain = false;
        var group = new List<Unit>();
        foreach (var u in _m.Selected) if (!u.Dead && u.Team == Team.Player) { chosen++; captain |= u.T == Kinds.Captain; group.Add(u); }
        // the chosen party's hero: his card
        if (Party != null && Party.Owner == Holder.Ours && C.Parties.Contains(Party) && Party.Party.Commander?.Hero is { } hero)
        {
            var pp = Party;
            acts.AddChild(Big($"Командир {hero.Name} · ур. {hero.Level}{(hero.Pending ? " · выберите умение" : "")}", "Карточка героя: опыт, умения, выбор умения на новом уровне.", () => _m.Hud.HeroPane.Show(pp)));
        }
        bool scouts = !captain && Main.Scouts(group);
        if (cell != C.Here)
        {
            var send = Big(scouts ? $"Отправить разведку сюда ({chosen})" : $"Отправить выбранных сюда ({chosen})",
                $"Выбранные в долине уйдут за её край в сторону этой долины и пойдут по карте. В отряде нужен командир. До {Campaign.ScoutMost} лёгких бойцов " +
                "без командира идут в разведку: быстрее и видят на 2 долины вокруг.", () => SendHere());
            send.Disabled = chosen == 0 || !captain && !scouts;
            acts.AddChild(send);
            if (chosen == 0 || !captain && !scouts)
                acts.AddChild(Line(chosen == 0 ? "Выберите в долине командира и бойцов рамкой." : $"Среди выбранных нет командира. Без него идут только до {Campaign.ScoutMost} лучников, пращников, всадников или псов, в разведку.", Ornament.Aged));
        }
        if (Party != null && Party.Owner == Holder.Ours && C.Parties.Contains(Party) && C.CellOf(Party) != cell)
            acts.AddChild(Big("Вести отряд сюда", "Выбранный на карте отряд пойдёт в эту долину. То же делает правая кнопка мыши.", () => Lead()));
        foreach (var p in C.PartiesIn(cell))
        {
            var pp = p;
            if (cell == C.Here) continue;
            acts.AddChild(Big($"Войти: {Who(p).ToLowerInvariant()}", "Отряд войдёт в долину, и вы будете играть в ней. Долина, где вы сейчас, сохранится.", () => EnterWith(pp)));
            if (C.Foes(cell) > 0)
                acts.AddChild(Big(Campaign.Untaken(cell) && cell.Trove == TroveKind.Shrine ? "Испытание сразу" : "Бой сразу",
                    "Бой решится сразу, по силе отрядов: вдвое сильнее побеждает 9 раз из 10, но теряет людей.", () => FightWith(pp)));
        }
        if (cell == C.Here && cell.Owner == Holder.None && !cell.Outpost)
            acts.AddChild(Big("Поставить заставу", "Рабочие построят каменную башню у камня заставы. Когда она встанет, долина ваша.", () =>
            {
                string why = _m.RaiseOutpost();
                _m.Hud.Float(why ?? "застава размечена", why == null ? Ornament.Moss : Ornament.Blood);
            }));
    }

    /// <summary>What is said of a cell's treasure on the board: what it is, who keeps it, what it gives, against the party chosen.</summary>
    List<(string, Color)> TroveLines(MapCell cell)
    {
        var lines = new List<(string, Color)>();
        if (cell.Trove == TroveKind.None) return lines;
        if (cell.TroveTaken)
        {
            lines.Add((cell.Trove == TroveKind.Shrine ? $"{Troves.Name(cell.Trove)}: испытание пройдено." : $"{Troves.Name(cell.Trove)}: сундук уже взят.", Ornament.Aged));
            return lines;
        }
        var keep = cell.Sum.Holders();
        if (cell.Trove == TroveKind.Shrine)
            lines.Add(($"{Troves.Name(cell.Trove)}. Удержите людей у алтаря {Campaign.TrialLength / 60:0} минуты: мертвецы придут {Campaign.TrialWaves} раза, всего {keep.Count}.", Ornament.EmberGlow));
        else
            lines.Add(($"{Troves.Name(cell.Trove)}. Стерегут: {Keepers(keep)}. Перебейте их и подойдите к сундуку.", Ornament.EmberGlow));
        lines.Add(($"Награда: {Campaign.RewardText(cell)}.", Ornament.Moss));
        // against the party chosen on the map, or those chosen in the valley
        var side = Party is { Owner: Holder.Ours } && C.Parties.Contains(Party) ? Party.Party.Members : Picked();
        if (side.Count > 0) lines.Add((Against(side, cell), Ornament.Parchment));
        return lines;
    }

    /// <summary>Those chosen in the valley, as they would go out.</summary>
    List<Traveller> Picked()
    {
        var list = new List<Traveller>();
        foreach (var u in _m.Selected) if (!u.Dead && u.Team == Team.Player) list.Add(Traveller.Of(u));
        return list;
    }

    /// <summary>«6 скелетов», «2 пещерных медведя и 4 волка».</summary>
    internal static string Keepers(List<Traveller> keep)
    {
        var counts = new List<(UnitType t, int n)>();
        foreach (var m in keep)
        {
            int i = counts.FindIndex(c => c.t == m.Kind);
            if (i < 0) counts.Add((m.Kind, 1)); else counts[i] = (m.Kind, counts[i].n + 1);
        }
        var parts = new List<string>();
        foreach (var (t, n) in counts) parts.Add($"{n} {Kind(t, n)}");
        return parts.Count == 0 ? "никто" : string.Join(" и ", parts);
    }

    static string Kind(UnitType t, int n) =>
        t == Kinds.Skeleton ? Sim.Plural(n, "скелет", "скелета", "скелетов")
        : t == Kinds.Wolf ? Sim.Plural(n, "волк", "волка", "волков")
        : t == Kinds.Bear ? Sim.Plural(n, "медведь", "медведя", "медведей")
        : t == Kinds.Boar ? Sim.Plural(n, "кабан", "кабана", "кабанов")
        : t.Name.ToLowerInvariant();

    /// <summary>How a party stands against a treasure's keepers: «стража слабее», «силы равны», «стража сильнее».</summary>
    internal static string Against(List<Traveller> side, MapCell cell)
    {
        var keep = cell.Sum.Holders();
        if (keep.Count == 0) return "";
        float odds = QuickBattle.Chance(side, null, keep, null, 0, cell.Kind);
        string word = odds >= .65f ? "стража слабее" : odds <= .35f ? "стража сильнее" : "силы равны";
        return $"Против выбранного отряда: {word} (победа {Math.Round(odds * 100)} из 100).";
    }

    static Button Big(string text, string tip, Action act)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, TooltipText = Bindings.Fill(text + "\n" + tip), MouseDefaultCursorShape = CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(Side - 40, 36) };
        b.AddThemeFontOverride("font", Ornament.SerifBold);
        b.AddThemeFontSizeOverride("font_size", 15);
        b.Pressed += act;
        return b;
    }

    void Card()
    {
        foreach (var c in _cardButtons.GetChildren()) c.QueueFree();
        var p = CardParty;
        if (p == null) return;
        var cell = C.CellOf(p);
        int foes = C.Foes(cell);
        bool hands = p.Party.Members.Exists(m => m.Kind == Kinds.Worker);
        _cardTitle.Text = $"Отряд дошёл: {Campaign.Names(cell).nom}";
        bool trove = Campaign.Untaken(cell), shrine = trove && cell.Trove == TroveKind.Shrine;
        _cardText.Text = $"{Who(p)}." + (trove ? $" {Troves.Name(cell.Trove)}: {(shrine ? "удержите алтарь против мертвецов" : $"стража {foes}")}, награда {Campaign.RewardText(cell)}. {Against(p.Party.Members, cell)}"
            : foes > 0 ? $" Там врагов: {foes}." : cell.Owner == Holder.None ? hands ? " Долина ничья. С рабочими отряд сам поставит заставу: 3 рабочих за день." : " Долина ничья." : "");
        var enter = Small("Войти", "Отряд войдёт в долину, и вы будете играть в ней.", () => EnterWith(p));
        enter.AddThemeFontOverride("font", Ornament.SerifBold);
        _cardButtons.AddChild(enter);
        if (foes > 0) _cardButtons.AddChild(Small(shrine ? "Испытание сразу" : "Бой сразу", "Бой решится сразу, по силе отрядов.", () => FightWith(p)));
        _cardButtons.AddChild(Small("На карту", "Открыть карту долин.", () => { Later(p); Open(); }));
        _cardButtons.AddChild(Small("Позже", "Отряд подождёт в долине. Войти можно с карты.", () => Later(p)));
        _card.Size = Vector2.Zero;
    }

    /// <summary>A painted icon as a control.</summary>
    sealed partial class Glyph : Control
    {
        readonly Icons.Painter _icon; readonly Color _ink;
        public Glyph(Icons.Painter icon, Color ink) { _icon = icon; _ink = ink; MouseFilter = MouseFilterEnum.Ignore; }
        public override void _Draw() => _icon(this, new Rect2(Vector2.Zero, Size), _ink);
    }

    // ------------------------------------------------------------ the field

    /// <summary>The cells and the parties on them, pointed north and south, north up.</summary>
    sealed partial class Field : Control
    {
        public MapBoard B;
        float _s;
        Vector2 _o;
        public int Drawn, Fogged;
        static readonly float R3 = MathF.Sqrt(3);

        static readonly Color Ours = new("5b86c8"), Theirs = new("d24a33"), Fog = new("1d1820");

        static Color KindColor(ValleyKind k) => k switch
        {
            ValleyKind.Ore => new Color("80644d"), ValleyKind.Sacred => new Color("8c8a5c"), ValleyKind.Forest => new Color("3f6340"),
            ValleyKind.Marsh => new Color("4b7672"), ValleyKind.Mountain => new Color("76727c"), _ => new Color("6c8648"),
        };

        static string TroveIcon(TroveKind k) => k switch
        {
            TroveKind.DeadMine => "raise-skeleton", TroveKind.Ruins => "jeweled-chalice", TroveKind.Den => "jawbone", TroveKind.Shrine => "sword-altar", _ => null,
        };

        /// <summary>What the cell under the mouse is, said when it rests there: its name, whose, its treasure and what it gives.</summary>
        public override string _GetTooltip(Vector2 at)
        {
            if (C == null || CellAt(at) is not { } c) return "";
            if (!C.Seen(c)) return "Долина в тумане. Подведите отряд или разведку ближе.";
            string owner = c.Owner switch { Holder.Ours => "наша", Holder.Horde => "во власти орды", _ => "ничья" };
            string text = $"{NameOf(c)}, {owner}";
            if (c.Holding == Holding.HordeTown && c.Owner == Holder.Horde) text += $"\nЛогово орды: гарнизон {c.Sum.Holders().Count}";
            if (c.Trove != TroveKind.None)
                text += c.TroveTaken ? $"\n{Troves.Name(c.Trove)}: взято" : $"\n{Troves.Name(c.Trove)}: стража {c.Sum.Holders().Count}, награда {Campaign.RewardText(c)}";
            return text;
        }

        static string KindIcon(ValleyKind k) => k switch
        {
            ValleyKind.Ore => "ore-pick", ValleyKind.Sacred => "holy-symbol", ValleyKind.Forest => "axe-in-stump",
            ValleyKind.Marsh => "swamp", ValleyKind.Mountain => "mountains", _ => null,
        };

        void Fit()
        {
            int rings = 0;
            foreach (var c in C.Cells) rings = Math.Max(rings, Campaign.Distance(0, 0, c.Q, c.R));
            float across = rings * 2 + 1.2f;
            _s = MathF.Min(Size.X / (R3 * across), Size.Y / (1.5f * (rings * 2) + 2.3f));
            _o = Size / 2;
        }

        public Vector2 Centre(MapCell c) => _o + new Vector2(R3 * _s * (c.Q + c.R / 2f), 1.5f * _s * c.R);

        Vector2[] Corners(Vector2 c, float s)
        {
            var pts = new Vector2[6];
            for (int i = 0; i < 6; i++) { float a = MathF.PI / 180 * (60 * i - 30); pts[i] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * s; }
            return pts;
        }

        public MapCell CellAt(Vector2 p)
        {
            var d = p - _o;
            float q = (R3 / 3 * d.X - d.Y / 3) / _s, r = 2f / 3 * d.Y / _s, x = q, z = r, y = -x - z;
            float rx = MathF.Round(x), ry = MathF.Round(y), rz = MathF.Round(z);
            float dx = MathF.Abs(rx - x), dy = MathF.Abs(ry - y), dz = MathF.Abs(rz - z);
            if (dx > dy && dx > dz) rx = -ry - rz; else if (dy <= dz) rz = -rx - ry;
            return C.At((int)rx, (int)rz);
        }

        /// <summary>Where a party is drawn: between its cell and the next by how far it has come, apart from the others there.</summary>
        Vector2 TokenAt(MapParty p, Dictionary<(int, int), int> crowd)
        {
            var at = Centre(C.CellOf(p));
            if (p.Moving) at = at.Lerp(Centre(p.Path[0]), (float)C.StepShare(p));
            var key = ((int)MathF.Round(at.X / 8), (int)MathF.Round(at.Y / 8));
            int k = crowd.GetValueOrDefault(key);
            crowd[key] = k + 1;
            if (k > 0) at += new Vector2(MathF.Cos(k * 2.1f), MathF.Sin(k * 2.1f)) * _s * .3f;
            return at + new Vector2(0, _s * .38f);
        }

        List<(MapParty p, Vector2 at)> Tokens()
        {
            var list = new List<(MapParty, Vector2)>();
            var crowd = new Dictionary<(int, int), int>();
            foreach (var p in C.Parties)
            {
                if (p.Owner != Holder.Ours && !C.Seen(C.CellOf(p)) && !(p.Moving && C.Seen(p.Path[0]))) continue;
                list.Add((p, TokenAt(p, crowd)));
            }
            return list;
        }

        public override void _Draw()
        {
            if (C == null) return;
            Fit();
            Drawn = Fogged = 0;
            var font = Ornament.Serif;
            int fs = (int)Math.Clamp(_s * .17f, 11, 16);
            foreach (var c in C.Cells)
            {
                var ctr = Centre(c);
                bool seen = C.Seen(c);
                var pts = Corners(ctr, _s - 2);
                DrawColoredPolygon(pts, seen ? KindColor(c.Kind).Darkened(.18f) : Fog);
                // a lighter middle, as land seen from above; its river and its road over it
                if (seen) { DrawColoredPolygon(Corners(ctr, _s * .78f), KindColor(c.Kind)); Ways(c, ctr); }
                var ring = new Vector2[7]; Array.Copy(pts, ring, 6); ring[6] = pts[0];
                DrawPolyline(ring, Ornament.Fade(Ornament.Bronze, seen ? .45f : .2f), 1, true);
                Drawn++;
                if (!seen) { Fogged++; Ornament.Lettering(this, font, ctr + new Vector2(-_s, fs * .35f), "?", fs + 4, Ornament.Fade(Ornament.Aged, .5f), HorizontalAlignment.Center, _s * 2); continue; }
                // whose it is: a band round the inside in its colour
                if (c.Owner != Holder.None)
                {
                    var own = Corners(ctr, _s - 6);
                    var band = new Vector2[7]; Array.Copy(own, band, 6); band[6] = own[0];
                    DrawPolyline(band, Ornament.Fade(c.Owner == Holder.Ours ? Ours : Theirs, .9f), 4, true);
                }
                // what holds it, in the middle; the kind under it
                string hold = c.Holding switch
                {
                    Holding.Citadel => "castle",
                    Holding.HordeTown => c.Owner == Holder.Ours ? "hill-fort" : "goblin-head",
                    _ => c.Outpost ? "stone-tower" : "hill-fort",
                };
                var ink = c.Holding == Holding.HordeTown && c.Owner != Holder.Ours ? Theirs : c.Owner == Holder.Ours ? Ornament.Parchment : Ornament.Fade(Ornament.Parchment, .55f);
                float g = _s * .5f;
                Icons.Glyph(this, new Rect2(ctr - new Vector2(g / 2, g * .85f), new Vector2(g, g)), ink, hold);
                string name = Valleys.Name(c.Kind).Split(' ')[0];
                Ornament.Lettering(this, font, ctr + new Vector2(-_s, g * .15f + fs * .8f), Upper(name), fs, Ornament.Parchment, HorizontalAlignment.Center, _s * 2);
                if (KindIcon(c.Kind) is { } ki) Icons.Glyph(this, new Rect2(ctr + new Vector2(-_s * .62f, -_s * .2f), new Vector2(_s * .22f, _s * .22f)), Ornament.Fade(Ornament.Parchment, .8f), ki);
                int keep = c.Owner != Holder.Ours ? c.Sum.Holders().Count : 0;
                if (keep > 0)
                    Ornament.Lettering(this, Ornament.SerifBold, ctr + new Vector2(_s * .32f, -_s * .02f), keep.ToString(), fs, c.Owner == Holder.Horde ? Theirs : Ornament.Aged);
                // its treasure, in the corner: dim once taken
                if (TroveIcon(c.Trove) is { } ti)
                    Icons.Glyph(this, new Rect2(ctr + new Vector2(_s * .36f, -_s * .62f), new Vector2(_s * .26f, _s * .26f)),
                        c.TroveTaken ? Ornament.Fade(Ornament.Aged, .5f) : Ornament.BronzeLight, ti);
                double gold = c == C.Here ? 0 : Campaign.AwayGold(c);
                if (gold > 0) Ornament.Lettering(this, Ornament.SerifBold, ctr + new Vector2(-_s, -_s * .62f), $"+{Fmt.Int(gold)}", fs, Ornament.BronzeLight, HorizontalAlignment.Center, _s * 2);
                if (c == C.Here)
                {
                    var here = Corners(ctr, _s - 11);
                    var hr = new Vector2[7]; Array.Copy(here, hr, 6); hr[6] = here[0];
                    DrawPolyline(hr, Ornament.EmberGlow, 2, true);
                }
            }
            // the chosen cell
            if (B.Chosen is { } ch)
            {
                var pts = Corners(Centre(ch), _s - 1);
                var ring = new Vector2[7]; Array.Copy(pts, ring, 6); ring[6] = pts[0];
                DrawPolyline(ring, Ornament.BronzeLight, 3, true);
            }
            // the ways our parties walk
            foreach (var p in C.Parties)
            {
                bool ours = p.Owner == Holder.Ours;
                if (!p.Moving || !ours && !(C.Seen(C.CellOf(p)) || C.Seen(p.Path[0]))) continue;
                var from = Centre(C.CellOf(p)).Lerp(Centre(p.Path[0]), (float)C.StepShare(p));
                foreach (var next in p.Path)
                {
                    var to = Centre(next);
                    var col = !ours ? Ornament.Fade(Theirs, .85f) : p == B.Party ? Ornament.EmberGlow : Ornament.Fade(Ornament.BronzeLight, .8f);
                    Dashes(from, to, col);
                    from = to;
                }
                DrawCircle(from, 5, !ours ? Theirs : p == B.Party ? Ornament.EmberGlow : Ornament.BronzeLight, true, -1, true);
            }
            // the parties
            float t = _s * .36f;
            foreach (var (p, at) in Tokens())
            {
                var box = new Rect2(at - new Vector2(t / 2, t / 2), new Vector2(t, t));
                DrawRect(box.Grow(3), p.Owner == Holder.Ours ? (p == B.Party ? Ornament.EmberGlow : Ours) : Theirs);
                DrawRect(box, Ornament.Iron);
                if (p.Owner == Holder.Ours && p.Party.Commander is { } cm)
                {
                    DrawTextureRect(Portraits.Of(cm.Kind, cm.Look, 64), box, false);
                    // a skill to choose: a star over the face
                    if (cm.Hero is { Pending: true }) Icons.Glyph(this, new Rect2(box.Position + new Vector2(t * .55f, -t * .3f), new Vector2(t * .55f, t * .55f)), Ornament.EmberGlow, "black-knight-helm");
                }
                else Icons.Glyph(this, box.Grow(-3), Theirs, "goblin-head");
                Ornament.Lettering(this, Ornament.SerifBold, at + new Vector2(t / 2 + 3, t / 2), p.Party.Members.Count.ToString(), fs - 1, Ornament.Parchment);
            }
        }

        static readonly Color Water = new("5a8db8"), Bank = new("2f4f6a"), RoadDust = new("e0c98f"), RoadEdge = new("5b4a32");

        /// <summary>The middle of a side of a cell drawn at a centre: the sides from the north-eastern round by the east, as the valleys number them.</summary>
        Vector2 SideMid(Vector2 ctr, int side)
        {
            float a = MathF.PI / 180 * (-60 + 60 * side);
            return ctr + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (_s - 2) * R3 / 2;
        }

        /// <summary>A point a part t of the way along a side of a cell drawn at a centre, from its first corner round by the east.</summary>
        Vector2 Along(Vector2 ctr, int side, float t)
        {
            float a0 = MathF.PI / 180 * (60 * side - 90), a1 = MathF.PI / 180 * (60 * side - 30);
            var p0 = ctr + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * (_s - 2);
            var p1 = ctr + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * (_s - 2);
            return p0.Lerp(p1, Math.Clamp(t, 0, 1));
        }

        /// <summary>
        /// A cell's river and road, as its valley has them (Campaign.LayWays): the river in over the middle of one side and
        /// out over another, bending through the land; the road from each of its sides to the middle, where the valley's
        /// stronghold stands. Where two cells' roads meet at a side, the way along them is quicker.
        /// </summary>
        void Ways(MapCell c, Vector2 ctr)
        {
            int ra = -1, rb = -1;
            for (int s = 0; s < 6; s++) if (c.Edges[s] is { River: >= 0 }) { if (ra < 0) ra = s; else rb = s; }
            if (rb >= 0)
            {
                // (where along each side the valley has it: a quarter of the way from one corner or the other)
                Vector2 a = Along(ctr, ra, Campaign.RiverAt(c, ra)), b = Along(ctr, rb, Campaign.RiverAt(c, rb)), mid = (a + b) / 2;
                var across = (b - a).Normalized().Orthogonal();
                // bent toward the middle, and a little aside by the cell's seed
                var bend = ctr.Lerp(mid, .35f) + across * _s * (((c.Seed >> 5) & 7) / 7f - .5f) * .25f;
                var pts = new Vector2[13];
                for (int i = 0; i < pts.Length; i++)
                {
                    float t = i / (pts.Length - 1f);
                    pts[i] = a * (1 - t) * (1 - t) + bend * 2 * t * (1 - t) + b * t * t;
                }
                DrawPolyline(pts, Bank, _s * .09f, true);
                DrawPolyline(pts, Water, _s * .06f, true);
            }
            for (int s = 0; s < 6; s++)
                if (c.Edges[s] is { Road: >= 0 })
                {
                    DrawLine(SideMid(ctr, s), ctr, Ornament.Fade(RoadEdge, .7f), MathF.Max(3, _s * .055f), true);
                    DrawLine(SideMid(ctr, s), ctr, RoadDust, MathF.Max(2, _s * .03f), true);
                }
        }

        void Dashes(Vector2 a, Vector2 b, Color col)
        {
            float len = a.DistanceTo(b), step = 9;
            for (float d = 0; d < len; d += step * 2)
                DrawLine(a.Lerp(b, d / len), a.Lerp(b, MathF.Min(len, d + step) / len), col, 2.5f, true);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is not InputEventMouseButton { Pressed: true } mb || C == null) return;
            if (mb.ButtonIndex == MouseButton.Left)
            {
                float t = _s * .36f;
                foreach (var (p, at) in Tokens())
                    if (p.Owner == Holder.Ours && mb.Position.DistanceTo(at) < t * .75f) { B.ChooseParty(p); AcceptEvent(); return; }
                if (CellAt(mb.Position) is { } c) B.Choose(c);
                AcceptEvent();
            }
            else if (mb.ButtonIndex == MouseButton.Right && CellAt(mb.Position) is { } c)
            {
                B.Choose(c);
                if (B.Party != null) B.Lead(c);
                AcceptEvent();
            }
        }
    }
}
