using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// What is said about our people wherever the interface shows them: what one is doing in a few words, what it has lost,
/// how a kind is getting on, the kinds' names in the plural.
/// </summary>
public static class Retinue
{
    static readonly (UnitType t, string tip)[] Defs =
    {
        (Kinds.Worker, "Рабочий\nКопает, рубит, ломает камень и кладёт стены — всё, что вы разметили. От орды бежит к цитадели. Щелчок — выбрать всех, ещё раз — показать."),
        (Kinds.Archer, "Лучник\nСтреляет на 24 м, первыми бьёт тех, кто ломает стены, ставит лестницы или лезет по ним. Сверху бьёт точнее. " +
            "Ночью в темноте видит врага лишь на 13 м — дальше только освещённого. Лучший охотник. Щелчок — выбрать всех."),
        (Kinds.Swordsman, "Мечник\nДержит землю на 12 м вокруг поста, в кольчуге и со щитом. Сбрасывает осадные лестницы. Щелчок — выбрать всех."),
    };

    public static string Tip(UnitType t) => Array.Find(Defs, d => d.t == t).tip ?? t.Name;

    /// <summary>What a pawn (or anyone) is doing, in a few words.</summary>
    public static string Doing(Unit u, Sim sim)
    {
        if (u.Dead) return u.Team == Team.Wild ? "добыт" : "пал";
        string hurt = Hurt(u);
        string what = DoingNow(u, sim);
        if (u.Burn > 0) hurt = hurt == "" ? "горит" : "горит, " + hurt;
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

    public static string DoingNow(Unit u, Sim sim)
    {
        if (u.Dead) return u.Team == Team.Wild ? "добыт" : "пал";
        if (u.PushCell >= 0) return "сбрасывает лестницу";
        if (sim.Climbing(u)) return "лезет по лестнице";
        if (u.Burn > 0 && u.State == UnitState.Flee && u.Team != Team.Horde) return "выбегает из огня";
        if (u.Team == Team.Wild)
            return u.State switch { UnitState.Flee => "убегает", UnitState.Fight => "бросается в атаку", UnitState.Walk => "бродит", _ => "пасётся" };
        if (u.Team == Team.Horde && u.T == Kinds.Thief && (u.Loot > 0 || u.Captive != null))
            return u.Captive != null ? $"уводит в плен: {u.Captive.T.Name.ToLowerInvariant()}" : "уносит: " + Sim.LootText(u);
        if (u.Team == Team.Horde && u.T == Kinds.Thief && u.State == UnitState.Fight && u.Target is { Team: Team.Player, T.Work: >= 1 })
            return "бьёт рабочего, чтобы связать";
        if (u.Team == Team.Horde && u.T == Kinds.Thief && u.State == UnitState.Walk)
            return "крадётся к цитадели";
        if (u.Captor != null) return "в плену: его уводят на запад";
        if (u.T == Kinds.Catapult) return u.State == UnitState.Siege ? "мечет камни по стенам" : "его катят туда, откуда бросать";
        if (u.T == Kinds.Ram) return u.State switch { UnitState.Carve => "проламывает стену", UnitState.Siege => "бьёт в цитадель", _ => "катится к стенам" };
        if (u.Escort is { Dead: false } && u.State != UnitState.Fight) return "охраняет камнемёт";
        if (u.T == Kinds.Mantlet) return "прикрывает ватагу от стрел";
        if (u.T == Kinds.SiegeTower) return u.Docked.Count > 0 ? "стоит у стены: по ней лезут наверх" : "катится к стене";
        if (u.Team == Team.Horde)
            return u.State switch
            {
                UnitState.Carve => u.SapCost > 0 ? "подкапывает стену" : "прорубает дорогу", UnitState.Siege => u.Kindling > 0 ? "поджигает факелом" : "бьёт в цитадель", UnitState.Fight => "бьётся",
                UnitState.Work => u.Building switch { Works.Ladder => "ставит лестницу", Works.Bridge => "кладёт мост", Works.Fill => "засыпает ров", _ => "строит" },
                UnitState.Flee => u.Routed && Sim.Armed(u) ? "бежит прочь" : "уползает прочь",
                _ => (u.Returned ? "идёт к цитадели · бежал в прошлый раз" : "идёт к цитадели") + (u.Torch ? " · с факелом" : ""),
            };
        if (u.Inside) return u.Mending ? "отлёживается в цитадели" : sim.Alarm ? "в цитадели: тревога" : "в цитадели: пережидает орду";
        if (u.Mending) return u.State == UnitState.Fight ? "ранен, отбивается" : "ранен, отходит к цитадели";
        if (sim.Alarm && u.Refuge >= 0 && !u.Hold && u.State != UnitState.Flee)
            return u.Refuge == sim.Gate ? "по тревоге идёт в цитадель" : u.Cell == u.Refuge ? "в убежище: тревога" : "по тревоге идёт в убежище";
        if (u.Patient is { Dead: false } patient) return "перевязывает: " + patient.T.Name.ToLowerInvariant();
        if (u.FireCell >= 0) return u.State == UnitState.Work ? "тушит пожар" : "бежит тушить пожар";
        if (u.KindleAt >= 0) return "поджигает огненной стрелой";
        if (u.Prey is { Dead: false } prey) return (prey.Team == Team.Wild ? "охотится: " : "атакует: ") + prey.T.Name.ToLowerInvariant();
        switch (u.State)
        {
            case UnitState.Work:
                if (u.JobI >= u.Jobs.Count) return "работает";
                var job = u.Jobs[u.JobI];
                if (!Orders.IsRemove(job.m)) return "строит";
                byte at = sim.World.Get(job.x, job.y, job.z);
                return Mat.IsVeg[at] ? "рубит лес" : at is Mat.Rock or Mat.Moss or Mat.Ruin or Mat.Ore || Mat.IsMasonry(at) ? "ломает камень" : "копает";
            case UnitState.Fight: return u.FireArrows ? "стреляет огнём" : "бьётся";
            case UnitState.Flee: return u.Refuge >= 0 && u.Refuge != sim.Gate ? "бежит в убежище" : "бежит в цитадель";
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

    /// <summary>How many of a kind there are and what they are busy with: "за работой 3 · без дела 1".</summary>
    public static (int count, string text) Summary(Sim sim, UnitType t)
    {
        int n = 0, busy = 0, idle = 0, fight = 0, hunt = 0, inside = 0;
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.T != t) continue;
            n++;
            if (u.Inside) inside++;
            else if (u.State is UnitState.Work) busy++;
            else if (u.Prey != null) hunt++;
            else if (u.State is UnitState.Fight) fight++;
            else if (u.State == UnitState.Idle && !u.Hold) idle++;
        }
        var parts = new List<string>();
        if (busy > 0) parts.Add($"за работой {busy}");
        if (fight > 0) parts.Add($"в бою {fight}");
        if (hunt > 0) parts.Add($"на охоте {hunt}");
        if (idle > 0) parts.Add($"без дела {idle}");
        if (inside > 0) parts.Add($"в цитадели {inside}");
        return (n, n == 0 ? "никого" : parts.Count > 0 ? string.Join(" · ", parts) : "в пути");
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
}

/// <summary>
/// The horde, in its tab on the beam: the warbands of the coming wave (or of the one in the valley) — each in its own
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
        if (b.State == BandState.Coming && b.Veterans.Count > 0)
            parts.Add($"и бежавшие в прошлый раз: {string.Join(", ", b.Veterans.ConvertAll(v => v.Kind.Name.ToLowerInvariant()))}");
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
        foreach (var b in sim.Bands) key += $"{b.Id}:{b.State}:{b.Strength}:{b.Side}:{(int)(b.TrailSeconds / 10)};";
        if (key != _key)
        {
            _key = key;
            foreach (var c in _bands.GetChildren()) c.QueueFree();
            if (sim.Bands.Count == 0) _bands.AddChild(Hud.Text("Ватаг пока не видно.", Ornament.SerifItalic, 12, Ornament.Aged));
            foreach (var b in sim.Bands)
            {
                var box = new VBoxContainer();
                box.AddThemeConstantOverride("separation", -2);
                int n = b.State == BandState.Coming ? b.Size : b.Strength;
                box.AddChild(Hud.Text($"{b.Name} · {n}", Ornament.SerifBold, 13, TrailView.ColorOf(b).Lightened(.3f)));
                box.AddChild(Hud.Text(Where(_m, b), Ornament.SerifItalic, 12, Ornament.Aged));
                if (b.Side >= 0 && b.State is BandState.Coming or BandState.Forming or BandState.March)
                    box.AddChild(Hud.Text($"пойдёт на {Sim.SideNames[b.Side]} сторону: {b.Why}" + (b.TrailSeconds > 0 ? $"; путь до цитадели ~{Fmt.Clock(b.TrailSeconds)}" : ""),
                        Ornament.Serif, 12, Ornament.BronzeLight, wrap: true));
                var what = Hud.Text(Kinds(b), Ornament.Serif, 12, Ornament.Parchment, wrap: true);
                box.AddChild(what);
                _bands.AddChild(box);
            }
        }
        _stores.Text = $"Брёвна орды {Fmt.M3(sim.HordeStock[Stockpile.Wood])} м³ — на лестницы и мосты, земля {Fmt.M3(sim.HordeStock[Stockpile.Earth])} м³ — на засыпку рвов. " +
            "Каждая волна приносит свои, и половина того, что орда прорубит, идёт в дело.";
    }
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
            if (u.Dead || u.Inside) continue;
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
