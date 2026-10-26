using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The warlord in the valley, under the faces in the middle of the top: his name and level, how much of him is left,
/// and his army's spirit (its bands' together). A click takes the camera to him. Gone when no warlord is in the field.
/// </summary>
public partial class LordBar : Control
{
    Main _m;
    Unit _lord;
    public const float W = 340, H = 44;
    /// <summary>The warlord shown, by name; and the line over his bars.</summary>
    public string Who => _lord?.Lord?.Name;
    public string Line => _lord?.Lord is { } l ? Name(l) : null;

    string Name(Warlord l) => $"{l.Name} · {l.Title} · ур. {l.Level}" + (_lord.Routed ? " · уходит" : "");

    public void Init(Main m)
    {
        _m = m;
        Size = CustomMinimumSize = new Vector2(W, H);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        Visible = false;
    }

    /// <summary>The warlord to show: the first on his feet in the valley who is no champion (the dusk card and the chronicle tell of those).</summary>
    Unit Find()
    {
        foreach (var u in _m.Sim.LordsHere) if (!u.Dead && !u.Inside && u.Lord is { Champion: false }) return u;
        return null;
    }

    /// <summary>His army's spirit: its bands' in the valley, by how many each has living.</summary>
    static float Spirit(Sim sim, Warlord lord, out bool broken)
    {
        float sum = 0; int n = 0; broken = true;
        foreach (var b in sim.Bands)
        {
            if (b.Lord != lord || b.State == BandState.Coming) continue;
            int alive = b.Alive;
            sum += b.Spirit * alive; n += alive;
            if (!b.Broken) broken = false;
        }
        return n == 0 ? 0 : sum / n;
    }

    public override void _Process(double delta)
    {
        if (_m?.Sim == null) return;
        var lord = Find();
        bool due = lord != null && !_m.Hud.InFirstPerson && !_m.AtlasOpen;
        if (due != Visible) { Visible = due; if (due) Juice.Appear(this); }
        _lord = lord;
        if (!due) return;
        var l = lord.Lord;
        float spirit = Spirit(_m.Sim, l, out bool broken);
        string gift = l.Gift switch
        {
            LordGift.Boulders => "мечет валуны в стены и в людей на них",
            LordGift.Curse => "проклинает кладку стен, и она ломается втрое быстрее",
            LordGift.Ambush => "прячет одну ватагу в засаде в траве",
            _ => "удар оземь раскидывает тех, кто рядом",
        };
        TooltipText = $"{Cap(l.Title)} {l.Name}, уровень {l.Level}\nВедёт войско орды. Рядом с ним налётчики терпят больше боли. Клич снимает им боль и возвращает бежавших, " +
            $"{gift}. Убейте его или возьмите в плен, и войско побежит.\nДух войска: " +
            (broken ? "войско бежит." : $"{spirit * 100:0} %. Падает от потерь, огня, взрывов и полётов, при 20 % ватага бежит.") + "\nЩелчок: показать его.";
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_lord?.Lord is not { } l) return;
        var r = new Rect2(Vector2.Zero, Size);
        Ornament.Plate(this, r, .92f);
        float spirit = Spirit(_m.Sim, l, out bool broken);
        string name = Name(l);
        Ornament.Lettering(this, Ornament.SerifBold, new Vector2(12, 17), name, 13, Ornament.Parchment);
        // his health, and his army's spirit under it
        float x0 = 12, w = Size.X - 24;
        Bar(new Rect2(x0, 23, w, 6), Math.Clamp(_lord.Hp / _lord.MaxHp, 0, 1), Ornament.Blood);
        Bar(new Rect2(x0, 33, w, 4), broken ? 0 : spirit, spirit < Sim.WaverSpirit ? Ornament.Ember : Ornament.BronzeLight);
        // the mark where a band breaks
        float bx = x0 + w * Sim.BreakSpirit;
        DrawLine(new Vector2(bx, 31), new Vector2(bx, 39), Ornament.Faded, 1);
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    void Bar(Rect2 r, float f, Color c)
    {
        DrawRect(r, new Color(0, 0, 0, .6f));
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * f, r.Size.Y)), c);
        DrawRect(r, Ornament.BronzeDark, false, 1);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } || _lord == null) return;
        var w = _m.World;
        _m.Rig.Focus(w.WX(_lord.X), w.WZ(_lord.Z));
        Sound.Ui("tick");
        AcceptEvent();
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>What the report of a raid says, line by line, for the card after it (Ui/Dawn.cs).</summary>
public static class BattleLines
{
    static string Kinds(Dictionary<UnitType, int> d)
    {
        var parts = new List<string>();
        foreach (var (t, n) in d) parts.Add(n > 1 ? $"{t.Name.ToLowerInvariant()} {n}" : t.Name.ToLowerInvariant());
        return string.Join(", ", parts);
    }

    public static List<string> Of(BattleReport r)
    {
        int came = BattleReport.Sum(r.Came), fell = BattleReport.Sum(r.Fell), fled = BattleReport.Sum(r.Fled), taken = BattleReport.Sum(r.Taken);
        int ours = BattleReport.Sum(r.OursFell);
        var lines = new List<string>
        {
            $"Орда: пришло {came}, пало {fell}, бежало {fled}" + (taken > 0 ? $", в плену {taken}." : "."),
            ours == 0 ? "Наши: потерь нет." : $"Наши: пало {ours} ({Kinds(r.OursFell)}).",
        };
        if (r.Lord != null)
            lines.Add($"{(r.LordTitle is { Length: > 0 } t ? char.ToUpperInvariant(t[0]) + t[1..] : "Военачальник")} {r.Lord}, уровень {r.LordLevel}: " + r.LordFate switch
            {
                LordFate.Fell => "пал.",
                LordFate.Taken => "взят в плен.",
                LordFate.Fled => "ушёл живым" + (r.LordSlew > 0 ? $", его войско убило наших: {r.LordSlew}." : "."),
                _ => "не вступил в бой.",
            });
        int broken = r.Bands.FindAll(b => b.Broken).Count;
        if (r.Bands.Count > 0) lines.Add($"Ватаг: {r.Bands.Count}, из них бежали: {broken}.");
        var damage = new List<string>();
        if (r.Broke > 0) damage.Add($"разрушено построек {Fmt.M3(r.Broke)} м³");
        if (r.KeepLost >= .01f) damage.Add($"цитадель потеряла {r.KeepLost * 100:0} %");
        if (damage.Count > 0) lines.Add(char.ToUpperInvariant(damage[0][0]) + string.Join(", ", damage)[1..] + ".");
        lines.Add($"Бой шёл {Fmt.Clock(r.Seconds)}.");
        return lines;
    }
}
