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
    static readonly Dictionary<string, string> Against = new()
    {
        ["goblin"] = "быстрые и низкие, пролезают где орк застрянет; лучники с башен",
        ["orc"] = "рубит дерево в разы быстрее камня: стены — из камня",
        ["sapper"] = "ставит лестницы и мосты, засыпает рвы; лучники бьют его первым",
        ["miner"] = "прорубает землю и скалу: каменная кладка держит дольше",
        ["thief"] = "крадёт из цитадели и уводит одиноких рабочих: держите рядом солдата",
        ["ram"] = "проламывает стены и ворота: ров, смола, огненные стрелы",
        ["catapult"] = "бьёт стены издали, за выстрелом лучника: выйдите и разбейте его",
        ["shaman"] = "лечит своих и портит стены: бейте его первым",
        ["mantlet"] = "щит из досок перед вожаком: стрелы бьют в него, заходите сбоку",
        ["troll"] = "ломает всё и лезет на уступ в два метра: огонь и копья",
        ["witch"] = "гасит факелы и пожары вокруг себя",
        ["tower"] = "встаёт к стене и спускает лестницу: жгите её огненными стрелами",
        ["necro"] = "поднимает павших орды: убейте его первым",
    };

    public void Init(Main m)
    {
        _m = m;
        CustomMinimumSize = new Vector2(W, 0);
        Size = new Vector2(W, 10);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var plate = new BottomConsole.Plate(c => Craft.Framed(c, new Rect2(Vector2.Zero, c.Size), 10)) { MouseFilter = MouseFilterEnum.Ignore };
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

    int Night => _m.Sim.Waves.Number + (_m.Sim.Waves.Active ? 0 : 1);

    /// <summary>Whether it is to hang now: from dusk till a little after the horn, in a siege of one's own.</summary>
    bool Due()
    {
        var sim = _m.Sim; var w = sim.Waves;
        if (sim.NoErrands || _m.Tutor != null || sim.Over || _shut == Night || _m.FP.On) return false;
        return !w.Active ? w.Countdown < Waves.Dusk : sim.Time - _hornAt < 12;
    }

    double _hornAt = -99;
    int _hornFor = -1;

    public void Refresh()
    {
        var sim = _m.Sim;
        if (sim.Waves.Active && _hornFor != sim.Waves.Number) { _hornFor = sim.Waves.Number; _hornAt = sim.Time; }
        bool due = Due();
        if (due != Visible) Visible = due;
        if (!due) return;
        int n = Night;
        _head.Text = $"Ночь {n} из {Waves.Total} · что идёт".ToUpperInvariant();
        // the bands, each with its leader and what it brings
        var lines = new List<string>();
        foreach (var b in sim.Bands)
        {
            if (b.State != BandState.Coming && !sim.Waves.Active) continue;
            lines.Add($"{b.Name}: {HordeInfo.Kinds(b)}");
        }
        int side = -1, most = 0;
        for (int i = 0; i < Sim.Sides; i++) if (sim.SideBands[i] > most) { most = sim.SideBands[i]; side = i; }
        if (side >= 0) lines.Add($"Вождь поведёт их на {Sim.SideNames[side]} сторону — она слабее всего на его глаз (кольцо вокруг цитадели).");
        _bands.Text = string.Join("\n", lines);
        // who comes for the first time, and what is good against it
        var fresh = Sim.NewAt(n);
        var said = new List<string>();
        foreach (var t in fresh) if (Against.TryGetValue(t.Key, out string how)) said.Add($"{t.Name}: {how}");
        _new.Visible = said.Count > 0;
        _new.Text = (n == 1 ? "Кто это:\n" : "Впервые этой ночью:\n") + string.Join("\n", said);
        // and the night after
        if (n >= Waves.Total) _next.Text = "Это последняя ночь осады.";
        else
        {
            var then = Sim.NewAt(n + 1);
            _next.Text = then.Count == 0 ? $"Следующей ночью — те же, только больше." : $"Следующей ночью впервые: {string.Join(", ", then.ConvertAll(t => t.Name.ToLowerInvariant()))}.";
        }
    }
}
