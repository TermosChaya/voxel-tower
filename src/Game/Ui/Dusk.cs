using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// At dusk, under the beam in the middle: what comes tonight — each band with its leader and what it brings, where
/// the chief means to lead them, who comes for the first time and what is good against them — and what is to come
/// the night after. It hangs till the horn and a little after, or till it is put away.
/// </summary>
public partial class DuskCard : Control
{
    Main _m;
    Label _head, _bands, _new, _next;
    public const float W = 560;
    int _shut = -1;

    /// <summary>What each kind of the horde is, and what is good against it, for the first night it comes.</summary>
    public static readonly Dictionary<string, string> Against = new()
    {
        ["goblin"] = "быстрые и мелкие, пролезают там, где орк застрянет",
        ["orc"] = "рубит дерево намного быстрее камня, стройте из камня",
        ["sapper"] = "ставит лестницы и мосты, засыпает рвы",
        ["miner"] = "роет подкоп под стену из-за двадцати метров и поджигает крепь. Слушайте у стен и копайте навстречу",
        ["thief"] = "крадёт запасы и уводит одиноких рабочих, держите рядом солдата",
        ["ram"] = "проламывает стены и ворота, против него ров, смола и огонь",
        ["catapult"] = "бьёт по стенам издали, дальше выстрела лучника, его нужно разбить вылазкой",
        ["shaman"] = "лечит своих и разрушает стены",
        ["mantlet"] = "дощатый щит перед ватагой, стрелы его не пробивают, заходите сбоку",
        ["troll"] = "ломает всё и забирается на уступ в 2 м",
        ["witch"] = "гасит факелы и пожары вокруг себя",
        ["tower"] = "подъезжает к стене, и по ней лезут наверх; её можно сжечь огненными стрелами",
        ["necro"] = "поднимает павших налётчиков",
        ["alchemist"] = "бросает склянки с кислотой, она разъедает стены; стреляйте в него первым",
        ["hillgiant"] = "мечет валуны с 30 м и проламывает стены, ров переходит вброд. Стрелы его почти не ранят: бейте копьями и огнём",
    };

    public void Init(Main m)
    {
        _m = m;
        CustomMinimumSize = new Vector2(W, 0);
        Size = new Vector2(W, 10);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var plate = new BottomConsole.Plate(c => Craft.Plate(c, new Rect2(Vector2.Zero, c.Size))) { MouseFilter = MouseFilterEnum.Ignore };
        plate.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(plate);
        var col = new VBoxContainer { Position = new Vector2(18, 12) };
        col.AddThemeConstantOverride("separation", 5);
        col.CustomMinimumSize = new Vector2(W - 36, 0);
        AddChild(col);
        var top = new HBoxContainer();
        _head = Hud.CapsLabel("", 10, Ornament.EmberGlow);
        _head.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(_head);
        var shut = new Button { Text = "×", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(24, 22), TooltipText = "Убрать до следующих сумерек", MouseDefaultCursorShape = CursorShape.PointingHand };
        shut.AddThemeFontSizeOverride("font_size", 14);
        shut.Pressed += () => { _shut = Night; Visible = false; };
        top.AddChild(shut);
        col.AddChild(top);
        _bands = Hud.Text("", Ornament.Serif, 13, Ornament.Parchment, wrap: true);
        _new = Hud.Text("", Ornament.SerifItalic, 13, Ornament.Parchment, wrap: true);
        _next = Hud.Text("", Ornament.Serif, 12, Ornament.Aged, wrap: true);
        foreach (var l in new[] { _bands, _new, _next }) { l.CustomMinimumSize = new Vector2(W - 36, 0); col.AddChild(l); }
        col.Resized += () => Size = new Vector2(W, col.Size.Y + 24);
    }

    int Night => _m.Sim.Raids.Count + (_m.Sim.Raids.Active ? 0 : 1);

    /// <summary>Whether it is to hang now: from dusk till a little after the horn, in a siege of one's own.</summary>
    bool Due()
    {
        var sim = _m.Sim; var w = sim.Waves;
        if (sim.NoErrands || _m.Tutor != null || sim.Over || _shut == Night || _m.FP.On) return false;
        return !w.Active ? sim.RaidSoon(Waves.Dusk) : sim.Time - _hornAt < 12;
    }

    double _hornAt = -99;
    int _hornFor = -1;

    public void Refresh()
    {
        var sim = _m.Sim;
        if (sim.Raids.Active && _hornFor != sim.Raids.Count) { _hornFor = sim.Raids.Count; _hornAt = sim.Time; }
        bool due = Due();
        if (due != Visible) { Visible = due; if (due) Juice.Appear(this); }
        if (!due) return;
        int n = Night;
        int ch = Sim.ChapterOf(n);
        int all = sim.Source.Total(sim);
        _head.Text = ($"Глава {Sim.Roman(ch)} · {Sim.ChapterNames[ch]} · ночь {n}" + (all > 0 ? $" из {all}" : "")).ToUpperInvariant();
        // the bands, each with its leader and what it brings
        var lines = new List<string>();
        // a chapter's first night: what it brings
        if ((n - 1) % 3 == 0) lines.Add(Sim.ChapterAbout[ch]);
        foreach (var b in sim.Bands)
        {
            if (b.State != BandState.Coming && !sim.Raids.Active) continue;
            lines.Add(b.Champion == 4 ? $"{b.Name}: Вождь орды {b.Title} с охраной ({HordeInfo.Kinds(b)}). Если он погибнет, вся орда побежит."
                : b.Champion > 0 ? $"{b.Name}: вожак главы {b.Title} с охраной ({HordeInfo.Kinds(b)}). Его гибель откроет новый ярус знаний."
                : $"{b.Name}: {HordeInfo.Kinds(b)}");
        }
        int side = -1, most = 0;
        for (int i = 0; i < Sim.Sides; i++) if (sim.SideBands[i] > most) { most = sim.SideBands[i]; side = i; }
        if (side >= 0) lines.Add($"Вождь поведёт их на {Sim.SideNames[side]} сторону: он считает её самой слабой.");
        _bands.Text = string.Join("\n", lines);
        // who comes for the first time (by the threat the raid is drawn up for), and what is good against it
        int level = Math.Max(1, (int)sim.PlannedThreat);
        var fresh = Sim.NewAt(level);
        var said = new List<string>();
        foreach (var t in fresh) if (Against.TryGetValue(t.Key, out string how)) said.Add($"{t.Name}: {how}");
        _new.Visible = said.Count > 0;
        _new.Text = (n == 1 ? "Кто это:\n" : "Впервые этой ночью:\n") + string.Join("\n", said);
        // and the night after
        if (all > 0 && n >= all) _next.Text = "Это последняя ночь осады.";
        else
        {
            var then = Sim.NewAt(level + 1);
            string boss = (n + 1) % 3 == 0 ? (n + 1 == all ? " Придёт Вождь орды." : $" Придёт вожак главы {Sim.Roman(Sim.ChapterOf(n + 1))}.") : "";
            _next.Text = (then.Count == 0 ? "Следующей ночью те же, но больше." : $"Следующей ночью впервые: {string.Join(", ", then.ConvertAll(t => t.Name.ToLowerInvariant()))}.") + boss;
        }
    }
}
