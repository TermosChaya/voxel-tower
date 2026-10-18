using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The wave clock beside the treasury at the top: which wave is next and how long until it comes (or how many raiders are
/// in the valley), the early call, the speed of the game, the horde's ways, and how much of the citadel still stands —
/// all in one strip.
/// </summary>
public partial class WaveClock : OrnatePanel
{
    Main _m;
    Label _title, _note, _time, _unit;
    Button _call, _trails;
    readonly List<Button> _speeds = new();
    KeepBar _keep;

    public WaveClock() : base(10) { }

    public void Init(Main m)
    {
        _m = m;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);
        var head = new HBoxContainer();
        _title = Hud.CapsLabel("", 9);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_title);
        _note = Hud.CapsLabel("", 9, Ornament.EmberGlow);
        head.AddChild(_note);
        col.AddChild(head);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var timeBox = new VBoxContainer { CustomMinimumSize = new Vector2(84, 0) };
        timeBox.AddThemeConstantOverride("separation", -8);
        _time = Hud.Text("", Ornament.Display, 28, Ornament.Parchment);
        timeBox.AddChild(_time);
        _unit = Hud.CapsLabel("", 8, Ornament.Aged);
        timeBox.AddChild(_unit);
        row.AddChild(timeBox);
        _call = new Button
        {
            FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(150, 28), MouseDefaultCursorShape = CursorShape.PointingHand,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            TooltipText = "Позвать орду сейчас\nЗолота за это не дают. Зато орда придёт, зная о вашей обороне меньше: сколько времени до волны прошло, столько она и разведала. Пробел.",
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
                Text = labels[i], ToggleMode = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(32, 26),
                MouseDefaultCursorShape = CursorShape.PointingHand, SizeFlagsVertical = SizeFlags.ShrinkCenter,
                TooltipText = "Скорость\nПауза останавливает всё, кроме разметки: кисти работают и на паузе. P — пауза, Tab — сменить скорость.",
            };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.Pressed += () => { if (k == 0) _m.Command.TogglePause(); else _m.Command.SetSpeed(k); };
            _speeds.Add(b);
            row.AddChild(b);
        }
        _trails = new Button
        {
            Text = "Пути", ToggleMode = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(46, 26),
            MouseDefaultCursorShape = CursorShape.PointingHand, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            TooltipText = "Пути орды\nКуда пойдут ватаги следующей волны — каждая своей пунктирной линией от места, где войдёт в долину, до цитадели; " +
                "красные кольца — где она будет прорубаться, янтарные — где поставит лестницу, наведёт мост или засыплет ров. " +
                "Пути пересчитываются, пока вы строите: перекройте дорогу — и линия уйдёт в обход. Клавиша J.",
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
            _title.Text = $"ВОЛНА {w.Number} ИЗ {Waves.Total} · РАЗВЕДКА {w.Intel * 100:0} %";
            _time.Text = (sim.RaidersAlive() + w.Coming).ToString();
            _unit.Text = "В ДОЛИНЕ";
            _call.Text = "Орда в долине"; _call.Disabled = true;
            _call.TooltipText = $"Запасы орды\nНа лестницы и мосты: брёвна {Fmt.M3(sim.HordeStock[Stockpile.Wood])} м³. На засыпку рвов: земля {Fmt.M3(sim.HordeStock[Stockpile.Earth])} м³. " +
                "Каждая волна приносит свои, и половина того, что орда прорубит, идёт в дело.";
        }
        else
        {
            int bands = sim.Bands.FindAll(b => b.State == BandState.Coming).Count;
            _title.Text = $"ВОЛНА {w.Number + 1} ИЗ {Waves.Total}" + (bands > 0 ? $" · ВАТАГ {bands}" : "");
            _time.Text = Fmt.Clock(w.Countdown);
            _unit.Text = "ДО ОРДЫ";
            _call.Text = $"На штурм · {w.IntelNow * 100:0} %";
            _call.Disabled = false;
        }
        _note.Text = _m.Paused ? "ПАУЗА" : _m.Speed > 1 ? $"×{_m.Speed}" : "";
        for (int i = 0; i < _speeds.Count; i++) _speeds[i].SetPressedNoSignal(i == 0 ? _m.Paused : !_m.Paused && _m.Speed == i);
        _trails.SetPressedNoSignal(sim.WantTrails);
        _keep.Value = Math.Clamp(sim.KeepHp / sim.KeepMax, 0, 1);
        _keep.QueueRedraw();
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>How much of the citadel still stands, as a bar under the clock.</summary>
public partial class KeepBar : Control
{
    public float Value = 1;

    public KeepBar()
    {
        CustomMinimumSize = new Vector2(0, 12);
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = "Цитадель\nОрда идёт к ней и бьёт в стены. Когда полоса опустеет, игра проиграна.";
    }

    public override void _Draw()
    {
        float x0 = 70, h = 6, y = (Size.Y - h) / 2;
        DrawString(Ornament.Caps, new Vector2(0, y + h), "ЦИТАДЕЛЬ", HorizontalAlignment.Left, -1, 8, Ornament.Aged);
        var r = new Rect2(x0, y, Size.X - x0, h);
        DrawRect(r, new Color(0, 0, 0, .6f));
        var fill = Value > .6f ? Ornament.Moss : Value > .3f ? Ornament.Ember : Ornament.Blood;
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * Value, h)), fill);
        DrawRect(r, Ornament.BronzeDark, false, 1);
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>
/// The retinue, in its tab on the right: how many of each kind and what they are busy with, hiring at the gate, the five
/// squads, and the chosen one by one — how hurt each is, what it lost, what it is doing — with the orders for them.
/// </summary>
public partial class Retinue : VBoxContainer
{
    Main _m;
    readonly Dictionary<UnitType, (Label name, Label note, Button hire)> _rows = new();
    readonly List<Button> _squads = new();
    Label _total, _selTitle, _selNote;
    VBoxContainer _list;
    string _listKey = "";

    static readonly (UnitType t, Icons.Painter icon, string tip)[] Defs =
    {
        (Kinds.Worker, Icons.Pick, "Рабочий\nКопает, рубит, ломает камень и кладёт стены — всё, что вы разметили. От орды бежит к цитадели. Щелчок — выбрать всех, ещё раз — показать."),
        (Kinds.Archer, Icons.Bow, "Лучник\nСтреляет на 24 м, первыми бьёт тех, кто ломает стены, ставит лестницы или лезет по ним. Сверху бьёт точнее. Лучший охотник. Щелчок — выбрать всех."),
        (Kinds.Swordsman, Icons.Sword, "Мечник\nДержит землю на 12 м вокруг поста, в кольчуге и со щитом. Сбрасывает осадные лестницы. Щелчок — выбрать всех."),
    };

    public static Icons.Painter IconOf(UnitType t) => t == Kinds.Archer ? Icons.Bow : t == Kinds.Swordsman ? Icons.Sword : Icons.Pick;

    public void Init(Main m)
    {
        _m = m;
        AddThemeConstantOverride("separation", 5);
        var head = new HBoxContainer();
        var title = Hud.CapsLabel("Дружина");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        _total = Hud.CapsLabel("", 10, Ornament.Faded);
        head.AddChild(_total);
        AddChild(head);
        foreach (var (t, icon, tip) in Defs)
        {
            var kind = t;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            var slot = new Slot(icon, null, tip, 34);
            slot.Clicked += () => _m.Command.SelectKind(kind);
            row.AddChild(slot);
            var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            info.AddThemeConstantOverride("separation", -3);
            var name = Hud.Text("", Ornament.SerifBold, 13, Ornament.Parchment);
            info.AddChild(name);
            var note = Hud.Text("", Ornament.Serif, 11, Ornament.Aged);
            info.AddChild(note);
            row.AddChild(info);
            var hire = new Button
            {
                Text = $"Нанять · {t.Cost}", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(88, 24),
                SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseDefaultCursorShape = CursorShape.PointingHand,
                TooltipText = $"Нанять: {t.Name.ToLowerInvariant()}\n{t.Cost} золота. Приходит к воротам цитадели. Золото дают рудники, убитые налётчики и охота.",
            };
            hire.AddThemeFontSizeOverride("font_size", 12);
            hire.Pressed += () => _m.Command.Hire(kind);
            row.AddChild(hire);
            _rows[t] = (name, note, hire);
            AddChild(row);
        }
        AddChild(new Rule(false));
        var squads = new HBoxContainer();
        squads.AddThemeConstantOverride("separation", 4);
        var sq = Hud.CapsLabel("Отряды", 9);
        sq.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        sq.VerticalAlignment = VerticalAlignment.Center;
        squads.AddChild(sq);
        for (int i = 0; i < 5; i++)
        {
            int n = i;
            var b = new Button
            {
                Text = Commander.Roman(i), FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(40, 24), MouseDefaultCursorShape = CursorShape.PointingHand,
                TooltipText = $"Отряд {Commander.Roman(i)}\nЩелчок или Shift+{i + 1} — выбрать его, дважды — показать. Ctrl+щелчок или Ctrl+{i + 1} — запомнить в нём выбранных.",
            };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
                {
                    if (mb.CtrlPressed || mb.MetaPressed) _m.Command.RememberSquad(n); else _m.Command.CallSquad(n);
                    b.AcceptEvent();
                }
            };
            _squads.Add(b);
            squads.AddChild(b);
        }
        AddChild(squads);
        AddChild(new Rule(false));
        var selHead = new HBoxContainer();
        _selTitle = Hud.Text("", Ornament.SerifBold, 13, Ornament.BronzeLight);
        _selTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        selHead.AddChild(_selTitle);
        AddChild(selHead);
        _selNote = Hud.Text("", Ornament.SerifItalic, 12, Ornament.Aged, wrap: true);
        AddChild(_selNote);
        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 2);
        AddChild(_list);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 4);
        buttons.AddChild(Btn("За работу", "За работу\nВыбранные снова сами ищут работу по разметке.", () => _m.Command.ReleaseSelected()));
        buttons.AddChild(Btn("Стоять", "Стоять здесь\nВыбранные остаются на месте; для солдат это место становится постом.", () => _m.Command.HoldSelected()));
        buttons.AddChild(Btn("Тревога", "Тревога (T)\nВсе солдаты бегут на свои посты. Рог трубит и сам, когда приходит орда.", () => _m.Command.Alarm()));
        AddChild(buttons);
        var duties = new HBoxContainer();
        duties.AddThemeConstantOverride("separation", 4);
        duties.AddChild(Btn("Камень", "Добывать камень\nКогда размеченной работы нет, выбранные сами ломают ближайший валун или руины, а выломав, ищут следующие.", () => _m.Command.DutySelected(Stockpile.Stone)));
        duties.AddChild(Btn("Брёвна", "Добывать брёвна\nКогда размеченной работы нет, выбранные сами рубят ближайшие деревья.", () => _m.Command.DutySelected(Stockpile.Wood)));
        duties.AddChild(Btn("Землю", "Добывать землю\nКогда размеченной работы нет, выбранные роют яму поодаль от крепости — одну на всех.", () => _m.Command.DutySelected(Stockpile.Earth)));
        duties.AddChild(Btn("Ополчение", "Ополчение\nРабочие не бегут от орды, а встречают её с кирками, как мечники. Ещё раз — снова бегут. Солдатам не нужно.", () => _m.Command.MilitiaSelected()));
        AddChild(duties);
        Refresh();
    }

    static Button Btn(string text, string tip, Action act)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 24), TooltipText = tip, MouseDefaultCursorShape = CursorShape.PointingHand, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        b.AddThemeFontSizeOverride("font_size", 12);
        b.Pressed += act;
        return b;
    }

    /// <summary>What a pawn (or anyone) is doing, in a few words.</summary>
    public static string Doing(Unit u, Sim sim)
    {
        if (u.Dead) return u.Team == Team.Wild ? "добыт" : "пал";
        string hurt = Hurt(u);
        string what = DoingNow(u, sim);
        return hurt == "" ? what : what + " · " + hurt;
    }

    /// <summary>"отрублена правая рука, истекает кровью", or nothing for one unhurt.</summary>
    public static string Hurt(Unit u)
    {
        var w = u.Wounds;
        if (w == null) return "";
        var parts = new List<string>(w.Wounds);
        if (w.Bleed > .8f) parts.Add("истекает кровью");
        else if (w.Bleed > .15f) parts.Add("кровоточит");
        return string.Join(", ", parts);
    }

    static string DoingNow(Unit u, Sim sim)
    {
        if (u.PushCell >= 0) return "сбрасывает лестницу";
        if (sim.Climbing(u)) return "лезет по лестнице";
        if (u.Team == Team.Wild)
            return u.State switch { UnitState.Flee => "убегает", UnitState.Fight => "бросается в атаку", UnitState.Walk => "бродит", _ => "пасётся" };
        if (u.Team == Team.Horde)
            return u.State switch
            {
                UnitState.Carve => "прорубает дорогу", UnitState.Siege => "бьёт в цитадель", UnitState.Fight => "бьётся",
                UnitState.Work => u.Building switch { Works.Ladder => "ставит лестницу", Works.Bridge => "кладёт мост", Works.Fill => "засыпает ров", _ => "строит" },
                UnitState.Flee => "уползает прочь",
                _ => "идёт к цитадели",
            };
        if (u.Prey is { Dead: false } prey) return (prey.Team == Team.Wild ? "охотится: " : "атакует: ") + prey.T.Name.ToLowerInvariant();
        switch (u.State)
        {
            case UnitState.Work:
                if (u.JobI >= u.Jobs.Count) return "работает";
                var job = u.Jobs[u.JobI];
                if (!Orders.IsRemove(job.m)) return "строит";
                byte at = sim.World.Get(job.x, job.y, job.z);
                return Mat.IsVeg[at] ? "рубит лес" : at is Mat.Rock or Mat.Moss or Mat.Ruin or Mat.Ore || Mat.IsMasonry(at) ? "ломает камень" : "копает";
            case UnitState.Fight: return "бьётся";
            case UnitState.Flee: return "бежит к цитадели";
            case UnitState.Walk:
                return u.Site >= 0 ? "идёт к работе" : u.Hold ? "идёт по приказу"
                    : u.Pastime == Pastime.Stroll ? (u.After == Pastime.Warm ? "идёт погреться к огню" : "прогуливается") : "идёт";
        }
        string leisure = u.Pastime switch
        {
            Pastime.Look => "осматривается", Pastime.Sit => "отдыхает", Pastime.Talk => "беседует", Pastime.Stretch => "потягивается",
            Pastime.Warm => "греется у огня", Pastime.Watch => "смотрит на запад", _ => "",
        };
        if (u.Hold) return leisure == "" ? "стоит по приказу" : "стоит по приказу, " + leisure;
        if (u.Why != "") return u.Why;
        if (u.Duty != Stockpile.None) return $"ищет, где добыть: {Stockpile.Name(u.Duty)}";
        return leisure != "" ? leisure : "без дела";
    }

    public void Refresh()
    {
        if (_m == null) return;
        var sim = _m.Sim;
        int total = 0;
        foreach (var (t, (name, note, hire)) in _rows)
        {
            int n = 0, busy = 0, idle = 0, fight = 0, hunt = 0;
            foreach (var u in sim.Units)
            {
                if (u.Dead || u.T != t) continue;
                n++;
                if (u.State is UnitState.Work) busy++;
                else if (u.Prey != null) hunt++;
                else if (u.State is UnitState.Fight) fight++;
                else if (u.State == UnitState.Idle && !u.Hold) idle++;
            }
            total += n;
            name.Text = $"{Many(t)} · {n}";
            var parts = new List<string>();
            if (busy > 0) parts.Add($"за работой {busy}");
            if (fight > 0) parts.Add($"в бою {fight}");
            if (hunt > 0) parts.Add($"на охоте {hunt}");
            if (idle > 0) parts.Add($"без дела {idle}");
            note.Text = n == 0 ? "никого" : parts.Count > 0 ? string.Join(" · ", parts) : "в пути";
            hire.Disabled = sim.Stock.Gold < t.Cost || sim.Over;
        }
        _total.Text = total.ToString();
        RefreshSelection();
    }

    void RefreshSelection()
    {
        var sq = _m.Command.Squads;
        for (int i = 0; i < _squads.Count; i++)
        {
            sq[i].RemoveAll(u => u.Dead);
            _squads[i].Text = sq[i].Count > 0 ? $"{Commander.Roman(i)}·{sq[i].Count}" : Commander.Roman(i);
        }
        var sel = _m.Selected;
        if (sel.Count == 0)
        {
            _selTitle.Text = "Никто не выбран";
            _selNote.Text = "Щелчок по пешке или рамка; ПКМ по земле — послать.";
            _selNote.Visible = true;
        }
        else
        {
            _selTitle.Text = sel.Count == 1 ? "Выбран" : $"Выбрано: {sel.Count}";
            _selNote.Visible = false;
        }
        // one row for each of the chosen (the first ten), made anew when the chosen change
        var chosen = new List<Unit>(sel);
        chosen.Sort((a, b) => a.T == b.T ? a.Id.CompareTo(b.Id) : Array.IndexOf(Kinds.PlayerKinds, a.T).CompareTo(Array.IndexOf(Kinds.PlayerKinds, b.T)));
        string key = string.Join(",", chosen.ConvertAll(u => u.Id));
        if (key != _listKey)
        {
            _listKey = key;
            foreach (var c in _list.GetChildren()) c.QueueFree();
            for (int i = 0; i < Math.Min(10, chosen.Count); i++)
            {
                var row = new UnitRow(_m, chosen[i]);
                _list.AddChild(row);
            }
            if (chosen.Count > 10) _list.AddChild(Hud.Text($"и ещё {chosen.Count - 10}", Ornament.SerifItalic, 12, Ornament.Aged));
        }
        foreach (var c in _list.GetChildren()) if (c is UnitRow r) r.QueueRedraw();
    }

    /// <summary>"Рабочие", "Лучники", "Мечники".</summary>
    public static string Many(UnitType t) => t.Key switch { "worker" => "Рабочие", "archer" => "Лучники", "sword" => "Мечники", _ => t.Name };

    /// <summary>"4 рабочих", "1 лучник", "2 мечника".</summary>
    public static string Plural(UnitType t, int n)
    {
        string[] forms = t.Key switch
        {
            "worker" => new[] { "рабочий", "рабочих", "рабочих" },
            "archer" => new[] { "лучник", "лучника", "лучников" },
            "sword" => new[] { "мечник", "мечника", "мечников" },
            _ => new[] { t.Name.ToLowerInvariant(), t.Name.ToLowerInvariant(), t.Name.ToLowerInvariant() },
        };
        int m10 = n % 10, m100 = n % 100;
        string f = m10 == 1 && m100 != 11 ? forms[0] : m10 is >= 2 and <= 4 && (m100 < 12 || m100 > 14) ? forms[1] : forms[2];
        return $"{n} {f}";
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>
/// The horde, in its tab on the right: the warbands of the coming wave (or of the one in the valley) — each in its own
/// colour, as its way is drawn on the field — what they are made of, who leads them, where they are; and what timber
/// and earth the horde has for its siege works.
/// </summary>
public partial class HordeInfo : VBoxContainer
{
    Main _m;
    VBoxContainer _bands;
    Label _stores;
    string _key = "";

    public void Init(Main m)
    {
        _m = m;
        AddThemeConstantOverride("separation", 6);
        AddChild(Hud.CapsLabel("Орда"));
        _bands = new VBoxContainer();
        _bands.AddThemeConstantOverride("separation", 8);
        AddChild(_bands);
        AddChild(new Rule(false));
        _stores = Hud.Text("", Ornament.Serif, 12, Ornament.Aged, wrap: true);
        AddChild(_stores);
        Refresh();
    }

    static string Kinds(Warband b)
    {
        var counts = new Dictionary<string, int>();
        var list = b.State == BandState.Coming ? b.Roster : b.Members.FindAll(u => !u.Dead).ConvertAll(u => u.T);
        foreach (var t in list) counts[t.Name.ToLowerInvariant()] = counts.TryGetValue(t.Name.ToLowerInvariant(), out int c) ? c + 1 : 1;
        var parts = new List<string>();
        foreach (var (name, n) in counts) parts.Add(n > 1 ? $"{name} ×{n}" : name);
        return string.Join(", ", parts);
    }

    static string Where(Main m, Warband b)
    {
        if (b.Target != null) return "на " + b.Target.Name.ToLowerInvariant();
        int z = b.Leader is { Dead: false } l ? (int)l.Z : b.Entry >= 0 ? m.Sim.Nav.K(b.Entry) << NavGrid.Shift : m.World.SZ / 2;
        float t = z / (float)m.World.SZ;
        string side = t < .33f ? "с северо-запада" : t > .67f ? "с юго-запада" : "с запада";
        return b.State switch
        {
            BandState.Coming => "войдёт " + side,
            BandState.Forming => "собирается " + side,
            BandState.Rally => "собирается у стен",
            BandState.Storm => "на штурме",
            _ => "в походе",
        };
    }

    public void Refresh()
    {
        if (_m == null) return;
        var sim = _m.Sim;
        string key = "";
        foreach (var b in sim.Bands) key += $"{b.Id}:{b.State}:{b.Strength};";
        if (key != _key)
        {
            _key = key;
            foreach (var c in _bands.GetChildren()) c.QueueFree();
            if (sim.Bands.Count == 0) _bands.AddChild(Hud.Text("Ватаг пока не видно.", Ornament.SerifItalic, 12, Ornament.Aged));
            foreach (var b in sim.Bands)
            {
                var box = new VBoxContainer();
                box.AddThemeConstantOverride("separation", -2);
                int n = b.State == BandState.Coming ? b.Roster.Count : b.Strength;
                box.AddChild(Hud.Text($"{b.Name} · {n}", Ornament.SerifBold, 13, TrailView.ColorOf(b).Lightened(.3f)));
                box.AddChild(Hud.Text(Where(_m, b), Ornament.SerifItalic, 12, Ornament.Aged));
                var what = Hud.Text(Kinds(b), Ornament.Serif, 12, Ornament.Parchment, wrap: true);
                box.AddChild(what);
                _bands.AddChild(box);
            }
        }
        _stores.Text = $"Брёвна орды {Fmt.M3(sim.HordeStock[Stockpile.Wood])} м³ — на лестницы и мосты, земля {Fmt.M3(sim.HordeStock[Stockpile.Earth])} м³ — на засыпку рвов. " +
            "Каждая волна приносит свои, и половина того, что орда прорубит, идёт в дело.";
    }
}

/// <summary>One of the chosen in the retinue's list: what it is, how much of it is left, what it lost and what it is doing.</summary>
public partial class UnitRow : Control
{
    readonly Main _m;
    readonly Unit _u;
    bool _hover;

    public UnitRow() { }

    public UnitRow(Main m, Unit u)
    {
        _m = m; _u = u;
        CustomMinimumSize = new Vector2(0, 36);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "Щелчок — выбрать только его, ещё раз — показать.";
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            _m.Command.SelectOne(_u);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var s = Size;
        if (_hover) DrawRect(new Rect2(Vector2.Zero, s), Ornament.Fade(Ornament.IronLight, .7f));
        var ink = _u.Dead ? Ornament.Faded : Ornament.Bronze;
        Retinue.IconOf(_u.T)(this, new Rect2(2, 4, 26, 26), ink);
        string name = $"{_u.Name} №{_u.Id}";
        DrawString(Ornament.SerifBold, new Vector2(34, 15), name, HorizontalAlignment.Left, s.X - 120, 13, _u.Dead ? Ornament.Faded : Ornament.Parchment);
        // health, as a bar with the number
        float bw = 70, bx = s.X - bw - 4, f = Math.Clamp(_u.Hp / _u.MaxHp, 0, 1);
        var bar = new Rect2(bx, 7, bw, 6);
        DrawRect(bar.Grow(1), new Color(0, 0, 0, .6f));
        DrawRect(new Rect2(bar.Position, new Vector2(bw * f, bar.Size.Y)), f > .6f ? Ornament.Moss : f > .3f ? Ornament.Ember : Ornament.Blood);
        // what it is doing, and what it lost, in a smaller hand
        string doing = Retinue.Doing(_u, _m.Sim);
        bool hurt = _u.Wounds != null && (_u.Wounds.Wounds.Count > 0 || _u.Wounds.Bleed > .15f);
        DrawString(Ornament.SerifItalic, new Vector2(34, 30), doing, HorizontalAlignment.Left, s.X - 38, 11, hurt ? Ornament.Blood : Ornament.Aged);
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>
/// Drawn over the field: health over the hurt and the chosen, why an idle worker is idle, and the selection frame.
/// </summary>
public partial class UnitOverlay : Control
{
    Main _m;

    public void Init(Main m)
    {
        _m = m;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_m?.Sim == null) return;
        var vp = GetViewportRect().Size;
        float ppv = vp.Y / Math.Max(.01f, _m.Rig.Cam.Size) * VoxelWorld.Vox;
        foreach (var u in _m.Sim.Units)
        {
            if (u.Dead) continue;
            bool sel = _m.Selected.Contains(u);
            bool hurt = u.Hp < u.MaxHp - .5f;
            bool why = u.Team == Team.Player && !u.Hold && u.State == UnitState.Idle && u.Why != "";
            if (!sel && !hurt && !why) continue;
            var top = _m.Command.Screen(u, 1) - new Vector2(0, 7);
            if (top.X < -40 || top.Y < -40 || top.X > vp.X + 40 || top.Y > vp.Y + 40) continue;
            if (sel || hurt)
            {
                float bw = Math.Clamp(u.T.BodyH * ppv * .8f, 22, 70), f = Math.Clamp(u.Hp / u.MaxHp, 0, 1);
                var r = new Rect2(top.X - bw / 2, top.Y - 5, bw, 5);
                DrawRect(r.Grow(1), new Color(0, 0, 0, .65f));
                var c = u.Team == Team.Horde ? Ornament.Blood : f > .6f ? Ornament.Moss : f > .3f ? Ornament.Ember : Ornament.Blood;
                DrawRect(new Rect2(r.Position, new Vector2(bw * f, r.Size.Y)), c);
                top.Y -= 8;
            }
            if (why)
                Ornament.Lettering(this, Ornament.SerifItalic, top + new Vector2(-80, -2), u.Why, 12, Ornament.BronzeLight, HorizontalAlignment.Center, 160);
        }
        if (_m.Command.Frame is Rect2 f2)
        {
            DrawRect(f2, new Color(Ornament.Ember, .08f));
            DrawRect(f2, Ornament.EmberGlow, false, 1.2f);
        }
    }
}
