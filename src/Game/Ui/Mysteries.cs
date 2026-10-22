using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The mysteries, as a panel let down from the beam: the intervention at hand and the revelations, where the priests
/// pray and how well they are heard, the sacrifices that can be asked for now — a prisoner, a carcass from the hunt,
/// or one of our own (two clicks: it cannot be taken back) — and the spells in their four circles: one learnt is
/// taken in hand with a click; one not yet learnt opens the lore tree on its page of the intervention, where it is learnt.
/// </summary>
public partial class MysteryPanel : VBoxContainer
{
    Main _m;
    Label _head, _where, _rite;
    Button _prisoner, _carcass, _own;
    readonly List<(Spell spell, Button button)> _spells = new();
    double _ownArmed = -99;
    Unit _ownFor;
    public const float Width = 760;

    public void Init(Main m)
    {
        _m = m;
        AddThemeConstantOverride("separation", 6);
        AddChild(Hud.CapsLabel("Таинства"));
        _head = Hud.Text("", Ornament.SerifBold, 14, Ornament.Parchment, wrap: true);
        _head.CustomMinimumSize = new Vector2(Width - 30, 0);
        AddChild(_head);
        _where = Hud.Text("", Ornament.SerifItalic, 13, Ornament.Aged, wrap: true);
        _where.CustomMinimumSize = new Vector2(Width - 30, 0);
        AddChild(_where);
        AddChild(Hud.CapsLabel("Жертвы · днём, в мирное время", 9, Ornament.BronzeLight));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _prisoner = Plaque("Пленного в жертву\nЖрец отведёт к алтарю в святилище самого ценного пленного. " +
            "Гоблин: 15 вмешательства и 1 откровение, орк или рудокоп: 25 и 2, тролль: 40 и 3. За вожака вмешательства в 1,5 раза больше, " +
            "на месте силы тоже. Пленных берут мечники с приказом «Живьём».", () => Ask(RiteKind.Prisoner));
        _carcass = Plaque("Тушу в жертву\nЖрец отнесёт к алтарю лучшую тушу с охоты. Заяц: 1 вмешательство, олень: 4, кабан: 5, " +
            "медведь: 10, белый олень: 15. Откровений туши не дают.", () => Ask(RiteKind.Carcass));
        _own = Plaque("Своего в жертву\nВыберите одного из своих (не жреца) и щёлкните дважды. Даёт 50 вмешательства и 2 откровения, " +
            "но 5 минут дружина работает на 25 % медленнее. Отменить нельзя.", AskOwn);
        row.AddChild(_prisoner); row.AddChild(_carcass); row.AddChild(_own);
        AddChild(row);
        _rite = Hud.Text("", Ornament.SerifItalic, 13, Ornament.EmberGlow, wrap: true);
        _rite.CustomMinimumSize = new Vector2(Width - 30, 0);
        AddChild(_rite);
        AddChild(Hud.CapsLabel("Заклинания · изучаются во вкладке «Знания»", 9, Ornament.BronzeLight));
        // a row to each circle: its name, then its spells, as many to a line as there is room for
        var circles = new VBoxContainer();
        circles.AddThemeConstantOverride("separation", 6);
        for (int c = 0; c < Sim.Circles.Length; c++)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            var name = Hud.CapsLabel(Sim.Circles[c], 8, Ornament.Aged);
            name.CustomMinimumSize = new Vector2(104, 0);
            name.VerticalAlignment = VerticalAlignment.Center;
            line.AddChild(name);
            var flow = new HFlowContainer { CustomMinimumSize = new Vector2(Width - 150, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            flow.AddThemeConstantOverride("h_separation", 6);
            flow.AddThemeConstantOverride("v_separation", 5);
            foreach (var sp in Sim.Spells)
            {
                if (sp.Circle != c) continue;
                var spell = sp;
                var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(196, 30), MouseDefaultCursorShape = CursorShape.PointingHand };
                b.AddThemeFontSizeOverride("font_size", 12);
                b.Pressed += () => SpellPressed(spell);
                flow.AddChild(b);
                _spells.Add((sp, b));
            }
            line.AddChild(flow);
            circles.AddChild(line);
        }
        AddChild(circles);
        Refresh();
    }

    Button Plaque(string tip, Action act)
    {
        var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(220, 40), MouseDefaultCursorShape = CursorShape.PointingHand, TooltipText = tip };
        b.AddThemeFontSizeOverride("font_size", 13);
        b.Pressed += act;
        return b;
    }

    /// <summary>A spell's plaque: one learnt goes into the hand (the panel goes up, out of the way); one not yet opens the tree where it is learnt.</summary>
    void SpellPressed(Spell sp)
    {
        var sim = _m.Sim;
        if (sim.Known.Contains(sp.Kind))
        {
            _m.Tools.TakeSpell(sp.Kind);
            _m.Hud.ClosePanel();
            return;
        }
        _m.Hud.OpenPanel("lore");
        _m.Hud.Lore.ShowPage(2);
    }

    void Ask(RiteKind kind, Unit victim = null)
    {
        string why = _m.Sim.Sacrifice(kind, victim);
        if (why != null) _m.Hud.Float(Bindings.Fill(why), Ornament.Blood);
        _ownArmed = -99;
        Refresh();
        _m.Hud.Refresh();
    }

    /// <summary>One of ours: the first click asks whether it is meant, the second gives it.</summary>
    void AskOwn()
    {
        var one = OwnChosen();
        if (one == null) { _m.Hud.Float("выберите одного из своих, не жреца", Ornament.Blood); return; }
        if (_ownFor == one && Now - _ownArmed < 5) { Ask(RiteKind.Own, one); return; }
        _ownFor = one;
        _ownArmed = Now;
        Refresh();
    }

    /// <summary>The one of ours chosen, if exactly one is and it may be given.</summary>
    Unit OwnChosen()
    {
        if (_m.Selected.Count != 1) return null;
        Unit u = null;
        foreach (var s in _m.Selected) u = s;
        return u.Dead || u.Team != Team.Player || u.T.Prays ? null : u;
    }

    /// <summary>Seconds of the real clock: the second click is asked for within a few of them, paused or not.</summary>
    static double Now => Time.GetTicksMsec() / 1000.0;

    public void Refresh()
    {
        if (_m == null) return;
        var sim = _m.Sim;
        int priests = 0, praying = 0;
        foreach (var u in sim.Units) if (!u.Dead && u.T.Prays) { priests++; if (u.Pastime == Pastime.Pray) praying++; }
        _head.Text = $"Вмешательство: {sim.Favour:0} из {sim.FavourCap:0} · Откровения: {sim.Revelations} · жрецов {priests}" + (praying > 0 ? $", молятся {praying} (+{sim.PrayerNow():0.#} в минуту)" : "");
        _where.Text = Where(sim);
        int prisoners = sim.Prisoners(), bound = sim.BoundInField();
        _prisoner.Text = $"Пленного · {prisoners}" + (bound > 0 ? $" (и связанных в поле {bound})" : "");
        _carcass.Text = $"Тушу · {sim.Carcasses.Count}";
        var one = OwnChosen();
        bool armed = one != null && one == _ownFor && Now - _ownArmed < 5;
        _own.Text = armed ? "Точно? Ещё раз — отдать" : one != null ? $"Своего: {one.T.Name.ToLowerInvariant()} №{one.Id}" : "Своего (выберите одного)";
        _own.Modulate = armed ? new Color(1.3f, .8f, .75f) : Colors.White;
        bool busy = sim.RiteNow != null, day = !sim.Waves.Active, hasPriest = priests > 0, hasSanctum = sim.Zones.Exists(z => z.Kind == Zone.Sanctum);
        bool can = !busy && day && hasPriest && hasSanctum;
        _prisoner.Disabled = !can || prisoners == 0;
        _carcass.Disabled = !can || sim.Carcasses.Count == 0;
        _own.Disabled = !can || one == null;
        foreach (var (sp, b) in _spells)
        {
            bool known = sim.Known.Contains(sp.Kind), learnable = sim.CanLearnSpell(sp);
            double ready = sim.ReadyIn(sp.Kind);
            double cost = sim.CostOf(sp);
            b.Text = known ? $"{sp.Name} · {cost:0}" + (ready > 0 ? $" · {ready:0} с" : "") : $"{sp.Name} · не изучено";
            b.Disabled = false;
            b.Modulate = known ? (ready > 0 || sim.Favour < cost ? new Color(.85f, .8f, .8f) : new Color(1.1f, 1.02f, 1.2f)) : learnable ? Colors.White : new Color(1, 1, 1, .55f);
            string price = $"{sp.Learn} {(sp.ByFavour ? "вмешательства" : "откровений")}";
            string what = known ? (ready > 0 ? $"Будет готово через {ready:0} с. " : "") + $"Щелчок — выбрать. Стоит {cost:0} вмешательства, перезарядка {sim.CooldownOf(sp):0} с."
                : (learnable ? $"Можно изучить за {price}. " : sim.Opened(Sim.LoreOf(Sim.SpellKey(sp.Kind))) ? $"Изучается за {price}. " : "Сначала изучите предыдущее заклинание круга. ")
                  + "Щелчок — открыть древо.";
            b.TooltipText = $"{sp.Name} · {Sim.Circles[sp.Circle].ToLowerInvariant()}\n{sp.About} {what}";
        }
        _rite.Text = sim.RiteNow is { } r ? RiteText(r) : !day ? "Ночью жрецы в цитадели. Жертвы приносят днём." : !hasPriest ? "Жрецов нет. Наберите их в цитадели (K)."
            : !hasSanctum ? Bindings.Fill("Святилища нет. Отметьте его делянкой ({ToolGather}) и выберите внизу «святилище».") : sim.Time < sim.MournUntil ? $"Дружина скорбит ещё {sim.MournUntil - sim.Time:0} с, работа идёт медленнее." : "";
    }

    static string RiteText(Rite r) => r.Kind switch
    {
        RiteKind.Prisoner => r.Stage == 0 ? $"Обряд: жрец идёт в цитадель за пленным — {r.Victim.Name.ToLowerInvariant()}." :
            r.Stage == 1 ? $"Обряд: жрец ведёт к алтарю пленного — {r.Victim.Name.ToLowerInvariant()}." : "Обряд у алтаря…",
        RiteKind.Carcass => r.Stage == 1 ? $"Обряд: жрец несёт к алтарю тушу — {r.Beast.Name.ToLowerInvariant()}." : "Обряд у алтаря…",
        _ => r.Stage == 1 ? $"Обряд: жрец ведёт к алтарю — {r.Victim.T.Name.ToLowerInvariant()} №{r.Victim.Id}." : "Обряд у алтаря…",
    };

    /// <summary>Where the priests pray and how well they are heard there.</summary>
    static string Where(Sim sim)
    {
        var parts = new List<string>();
        foreach (var z in sim.Zones)
        {
            if (z.Kind != Zone.Sanctum) continue;
            var m = sim.PowerUnder(z);
            parts.Add(m == null ? "святилище на простой земле" : m.Holder switch
            {
                Holder.Ours => $"святилище на месте силы «{m.Name}»: молитва даёт втрое больше",
                Holder.Horde => $"святилище на месте силы «{m.Name}» не работает, пока место у орды",
                _ => $"святилище на месте силы «{m.Name}»: займите его, и молитва даст втрое больше",
            });
        }
        return parts.Count == 0 ? "Святилищ нет. На месте силы (Курган, Чёрный камень, Мёртвый родник) молитва даёт втрое больше."
            : char.ToUpper(string.Join("; ", parts)[0]) + string.Join("; ", parts)[1..] + ".";
    }

    /// <summary>A spell's plaque (the self-test presses it).</summary>
    public Button SpellPlaque(SpellKind k) => _spells.Find(s => s.spell.Kind == k).button;

    /// <summary>The plaque that asks for a sacrifice of a kind (the self-test presses it).</summary>
    public Button PlaqueOf(RiteKind kind) => kind switch { RiteKind.Prisoner => _prisoner, RiteKind.Carcass => _carcass, _ => _own };

    /// <summary>For the tab: how many prisoners wait, or nothing.</summary>
    public string Badge()
    {
        int n = _m.Sim.Prisoners();
        return n > 0 ? n.ToString() : "";
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}
