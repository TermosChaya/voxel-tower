using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The clock in the top right corner: which wave is next and how long until it comes, how much the horde has scouted of
/// the defences so far, the early call, the speed of the game, and how much of the citadel still stands.
/// </summary>
public partial class WaveClock : OrnatePanel
{
    Main _m;
    Label _title, _note, _time, _unit;
    Button _call;
    readonly List<Button> _speeds = new();
    KeepBar _keep;

    public WaveClock() : base(12) { }

    public void Init(Main m)
    {
        _m = m;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        var head = new HBoxContainer();
        _title = Hud.CapsLabel("");
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_title);
        _note = Hud.CapsLabel("", 10, Ornament.EmberGlow);
        head.AddChild(_note);
        col.AddChild(head);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var timeBox = new VBoxContainer { CustomMinimumSize = new Vector2(96, 0) };
        timeBox.AddThemeConstantOverride("separation", -6);
        _time = Hud.Text("", Ornament.Display, 38, Ornament.Parchment);
        timeBox.AddChild(_time);
        _unit = Hud.CapsLabel("", 9, Ornament.Aged);
        timeBox.AddChild(_unit);
        row.AddChild(timeBox);
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 5);
        _call = new Button
        {
            FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(176, 30), MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = "Позвать орду сейчас\nЗолота за это не дают. Зато орда придёт, зная о вашей обороне меньше: сколько времени до волны прошло, столько она и разведала. " +
                "Пока разведка ни на что не влияет — тактика орды против ваших слабых мест будет следующим шагом. Пробел.",
        };
        _call.Pressed += () => _m.Command.CallWave();
        right.AddChild(_call);
        var speeds = new HBoxContainer();
        speeds.AddThemeConstantOverride("separation", 3);
        string[] labels = { "II", "×1", "×2", "×3" };
        for (int i = 0; i < labels.Length; i++)
        {
            int k = i;
            var b = new Button
            {
                Text = labels[i], ToggleMode = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(40, 24),
                MouseDefaultCursorShape = CursorShape.PointingHand,
                TooltipText = "Скорость\nПауза останавливает всё, кроме разметки: кисти работают и на паузе. P — пауза, Tab — сменить скорость.",
            };
            b.Pressed += () => { if (k == 0) _m.Command.TogglePause(); else _m.Command.SetSpeed(k); };
            _speeds.Add(b);
            speeds.AddChild(b);
        }
        right.AddChild(speeds);
        row.AddChild(right);
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
            _unit.Text = "НАЛЁТЧИКОВ В ДОЛИНЕ";
            _call.Text = "Орда в долине"; _call.Disabled = true;
            _call.TooltipText = $"Запасы орды\nНа лестницы и мосты: брёвна {Fmt.M3(sim.HordeStock[Stockpile.Wood])} м³. На засыпку рвов: земля {Fmt.M3(sim.HordeStock[Stockpile.Earth])} м³. " +
                "Каждая волна приносит свои, и половина того, что орда прорубит, идёт в дело.";
        }
        else
        {
            _title.Text = $"ВОЛНА {w.Number + 1} ИЗ {Waves.Total}";
            _time.Text = Fmt.Clock(w.Countdown);
            _unit.Text = "ДО ПРИХОДА ОРДЫ";
            _call.Text = $"На штурм · разведка {w.IntelNow * 100:0} %";
            _call.Disabled = false;
        }
        _note.Text = _m.Paused ? "ПАУЗА" : _m.Speed > 1 ? $"×{_m.Speed}" : "";
        for (int i = 0; i < _speeds.Count; i++) _speeds[i].SetPressedNoSignal(i == 0 ? _m.Paused : !_m.Paused && _m.Speed == i);
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
        CustomMinimumSize = new Vector2(0, 18);
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = "Цитадель\nОрда идёт к ней и бьёт в стены. Когда полоса опустеет, игра проиграна.";
    }

    public override void _Draw()
    {
        float x0 = 86, h = 8, y = (Size.Y - h) / 2;
        DrawString(Ornament.Caps, new Vector2(0, y + h), "ЦИТАДЕЛЬ", HorizontalAlignment.Left, -1, 9, Ornament.Aged);
        var r = new Rect2(x0, y, Size.X - x0, h);
        DrawRect(r, new Color(0, 0, 0, .6f));
        var fill = Value > .6f ? Ornament.Moss : Value > .3f ? Ornament.Ember : Ornament.Blood;
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * Value, h)), fill);
        DrawRect(r, Ornament.BronzeDark, false, 1);
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>
/// The pawns: how many of each kind and what they are busy with, hiring at the gate, and orders for the chosen ones.
/// </summary>
public partial class Retinue : OrnatePanel
{
    Main _m;
    readonly Dictionary<UnitType, (Label name, Label note, Button hire)> _rows = new();
    Label _total, _selTitle, _selNote;
    Control _selBox;

    static readonly (UnitType t, Icons.Painter icon, string tip)[] Defs =
    {
        (Kinds.Worker, Icons.Pick, "Рабочий\nКопает, рубит, ломает камень и кладёт стены — всё, что вы разметили. Работает вдвое с лишним быстрее солдата и приносит больше. От орды бежит к цитадели. Щелчок — выбрать всех."),
        (Kinds.Archer, Icons.Bow, "Лучник\nСтреляет на 24 м, первыми бьёт тех, кто ломает стены, ставит лестницы или лезет по ним. Сверху бьёт точнее. Лучший охотник: ПКМ по зверю. Между волнами помогает с работой. Щелчок — выбрать всех."),
        (Kinds.Swordsman, Icons.Sword, "Мечник\nДержит землю на 12 м вокруг поста, в броне и со щитом. Сбрасывает осадные лестницы, до которых дотянется. Между волнами помогает с работой. Щелчок — выбрать всех."),
    };

    public Retinue() : base(12) { }

    public void Init(Main m)
    {
        _m = m;
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        col.AddThemeConstantOverride("separation", 6);
        var head = new HBoxContainer();
        var title = Hud.CapsLabel("Дружина");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        _total = Hud.CapsLabel("", 10, Ornament.Faded);
        head.AddChild(_total);
        col.AddChild(head);
        foreach (var (t, icon, tip) in Defs)
        {
            var kind = t;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var slot = new Slot(icon, null, tip, 40);
            slot.Clicked += () => _m.Command.SelectKind(kind);
            row.AddChild(slot);
            var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            info.AddThemeConstantOverride("separation", -2);
            var name = Hud.Text("", Ornament.SerifBold, 14, Ornament.Parchment);
            info.AddChild(name);
            var note = Hud.Text("", Ornament.Serif, 12, Ornament.Aged);
            info.AddChild(note);
            row.AddChild(info);
            var hire = new Button
            {
                Text = $"Нанять · {t.Cost}", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(96, 28),
                SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseDefaultCursorShape = CursorShape.PointingHand,
                TooltipText = $"Нанять: {t.Name.ToLowerInvariant()}\n{t.Cost} золота. Приходит к воротам цитадели. Золото дают убитые налётчики и ранний вызов орды.",
            };
            hire.Pressed += () => _m.Command.Hire(kind);
            row.AddChild(hire);
            _rows[t] = (name, note, hire);
            col.AddChild(row);
        }
        col.AddChild(new Rule(false));
        var sel = new VBoxContainer();
        sel.AddThemeConstantOverride("separation", 4);
        _selTitle = Hud.Text("", Ornament.SerifBold, 14, Ornament.BronzeLight);
        sel.AddChild(_selTitle);
        _selNote = Hud.Text("", Ornament.SerifItalic, 13, Ornament.Aged, wrap: true);
        _selNote.CustomMinimumSize = new Vector2(300, 0);
        sel.AddChild(_selNote);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 4);
        buttons.AddChild(Btn("За работу", "За работу\nВыбранные снова сами ищут работу по разметке.", () => _m.Command.ReleaseSelected()));
        buttons.AddChild(Btn("Стоять", "Стоять здесь\nВыбранные остаются на месте; для солдат это место становится постом.", () => _m.Command.HoldSelected()));
        buttons.AddChild(Btn("Тревога", "Тревога (T)\nВсе солдаты бегут на свои посты. Рог трубит и сам, когда приходит орда.", () => _m.Command.Alarm()));
        sel.AddChild(buttons);
        _selBox = sel;
        col.AddChild(sel);
        AddChild(col);
        Refresh();
    }

    static Button Btn(string text, string tip, Action act)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 26), TooltipText = tip, MouseDefaultCursorShape = CursorShape.PointingHand, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        b.Pressed += act;
        return b;
    }

    /// <summary>What a pawn (or anyone) is doing, in a few words.</summary>
    public static string Doing(Unit u, Sim sim)
    {
        if (u.Dead) return u.Team == Team.Wild ? "добыт" : "пал";
        if (u.PushCell >= 0) return "сбрасывает лестницу";
        if (sim.Climbing(u)) return "лезет по лестнице";
        if (u.Team == Team.Wild)
            return u.State switch { UnitState.Flee => "убегает", UnitState.Fight => "бросается в атаку", UnitState.Walk => "бродит", _ => "пасётся" };
        if (u.Team == Team.Horde)
            return u.State switch
            {
                UnitState.Carve => "прорубает дорогу", UnitState.Siege => "бьёт в цитадель", UnitState.Fight => "бьётся",
                UnitState.Work => u.Building switch { Works.Ladder => "ставит лестницу", Works.Bridge => "кладёт мост", Works.Fill => "засыпает ров", _ => "строит" },
                _ => "идёт к цитадели",
            };
        if (u.Prey is { Dead: false } prey) return (prey.Team == Team.Wild ? "охотится: " : "атакует: ") + prey.T.Name.ToLowerInvariant();
        switch (u.State)
        {
            case UnitState.Work:
                return u.JobI < u.Jobs.Count && u.Jobs[u.JobI].m != Orders.Remove ? "строит" : "копает";
            case UnitState.Fight: return "бьётся";
            case UnitState.Flee: return "бежит к цитадели";
            case UnitState.Walk: return u.Site >= 0 ? "идёт к работе" : u.Hold ? "идёт по приказу" : "идёт";
        }
        if (u.Hold) return "стоит по приказу";
        return u.Why != "" ? u.Why : "без дела";
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
        var sel = _m.Selected;
        if (sel.Count == 0)
        {
            _selTitle.Text = "Никто не выбран";
            _selNote.Text = "Щёлкните по пешке или обведите нескольких рамкой, затем ПКМ по земле — пойдут туда и встанут.";
            return;
        }
        if (sel.Count == 1)
        {
            foreach (var u in sel)
            {
                _selTitle.Text = $"{u.T.Name} · {Math.Ceiling(Math.Max(0, u.Hp))} из {u.T.Hp}";
                _selNote.Text = Doing(u, _m.Sim) + (u.T.Work < 1 && u.Post >= 0 ? " · пост назначен" : "");
            }
            return;
        }
        var counts = new Dictionary<UnitType, int>();
        int work = 0, held = 0;
        foreach (var u in sel)
        {
            counts[u.T] = counts.TryGetValue(u.T, out int c) ? c + 1 : 1;
            if (u.State == UnitState.Work) work++;
            if (u.Hold) held++;
        }
        var parts = new List<string>();
        foreach (var (t, n) in counts) parts.Add(Plural(t, n));
        _selTitle.Text = $"Выбрано: {sel.Count}";
        _selNote.Text = string.Join(", ", parts) + (work > 0 ? $" · за работой {work}" : "") + (held > 0 ? $" · стоят по приказу {held}" : "");
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
            bool hurt = u.Hp < u.T.Hp - .5f;
            bool why = u.Team == Team.Player && !u.Hold && u.State == UnitState.Idle && u.Why != "";
            if (!sel && !hurt && !why) continue;
            var top = _m.Command.Screen(u, 1) - new Vector2(0, 7);
            if (top.X < -40 || top.Y < -40 || top.X > vp.X + 40 || top.Y > vp.Y + 40) continue;
            if (sel || hurt)
            {
                float bw = Math.Clamp(u.T.BodyH * ppv * .8f, 22, 70), f = Math.Clamp(u.Hp / u.T.Hp, 0, 1);
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
