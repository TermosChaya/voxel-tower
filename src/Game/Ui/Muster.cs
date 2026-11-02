using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// A kind of ours in the citadel's muster: its face, with the gold it costs in the corner; a click takes one on. The
/// citadel's own kinds come out of its door; the rest are hired in their buildings, and their tiles carry a house in the
/// corner: a click hires one there (out of that building's door), or, with the building not put up yet, opens the town
/// to put it up. The price is the same as the building asks. A kind the household has not learnt yet stands dark behind
/// a lock; one there is no gold or no place for has its price in red. The tooltip says what the kind does, what it
/// costs, how many places it takes, where it is hired and why not.
/// </summary>
public partial class KindTile : Control
{
    readonly Main _m;
    public readonly UnitType Kind;
    bool _hover;

    public KindTile() { }

    public KindTile(Main m, UnitType kind)
    {
        _m = m; Kind = kind;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = kind.Name;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    /// <summary>Whether the citadel itself takes this kind on (the household's own, or any in the open sandbox).</summary>
    bool Here => Sim.CitadelHires(Kind) || _m.Sim.Sandbox;

    /// <summary>
    /// Takes one of the kind on (the self-test calls it as a click would): at the citadel, or in its building; with no
    /// building of its kind put up, the town is opened to put one up.
    /// </summary>
    public void Press()
    {
        var sim = _m.Sim;
        if (Here) { _m.Command.Enlist(Kind); return; }
        if (sim.HirerOf(Kind) is { } b) { _m.Command.HireAt(b, Kind); return; }
        _m.Hud.OpenPanel("town");
        if (Sim.HirerKind(Kind) is { } h) _m.Hud.Float($"{Kind.Name}: постройте здание «{h.kind.Name}»", Ornament.EmberGlow);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { Juice.Press(this); Press(); AcceptEvent(); }
    }

    public override string _GetTooltip(Vector2 at)
    {
        var sim = _m.Sim;
        string room = Kind.Room > 1 ? $", занимает {Kind.Room} {Places(Kind.Room)}" : "";
        string logs = (Kind.Logs > 0 ? $" и {Kind.Logs:0.#} м³ брёвен" : "") + (Kind.Metal > 0 ? $" и {Kind.Metal:0.##} м³ металла" : "");
        string head = $"{Kind.Name} · {sim.HireCost(Kind)} золота{logs}{room}{sim.HireNote(Kind)}\n{Retinue.About(Kind)}\n";
        if (Here)
        {
            if (!sim.Unlocked(Kind)) return head + $"Нужно знание «{Sim.LoreOf(Kind.Lore)?.Name ?? Kind.Lore}» (вкладка «Знания»).";
            string why = sim.CantEnlist(Kind);
            return head + (why == null ? "Щелчок — нанять. Выходит из цитадели сразу." : $"Нельзя: {why}.");
        }
        if (Sim.HirerKind(Kind) is not { } h) return head + "Цитадель таких не нанимает.";
        string where = $"Нанимают в здании «{h.kind.Name}»{(h.level > 1 ? " второго уровня" : "")}";
        if (sim.HirerOf(Kind) is not { } b) return head + $"{where}. Его ещё нет: щелчок открывает вкладку «Город».";
        string no = sim.CantHireAt(b, Kind);
        int waiting = b.Stock.TryGetValue(Kind.Key, out float n) ? (int)n : 0;
        return head + (no == null ? $"{where}, ждут найма {waiting}. Щелчок — нанять: выходит из двери здания." : $"{where}. Нельзя: {no}.");
    }

    static string Places(int n) => n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? "места" : n % 10 == 1 && n % 100 != 11 ? "место" : "мест";

    public override void _Draw()
    {
        var sim = _m.Sim;
        var r = new Rect2(Vector2.Zero, Size);
        bool here = Here;
        var hirer = here ? null : sim.HirerOf(Kind);
        // open: it can be had where it is hired, gold and places aside; can: it can be had now
        bool open = here ? sim.Unlocked(Kind) : hirer != null && hirer.Ready && Sim.HirerKind(Kind) is { } h && hirer.Level >= h.level;
        bool can = here ? sim.CantEnlist(Kind) == null : hirer != null && sim.CantHireAt(hirer, Kind) == null;
        Ornament.Gradient(this, r, new Color("2a2024"), new Color("0e0b0d"));
        float s = Size.X - 2;
        DrawTextureRect(Portraits.Of(Kind, 0, 48), new Rect2(1, 1, s, s), false, open ? Colors.White : new Color(.32f, .3f, .3f));
        if (here && !open)
        {
            // a padlock over the face: not learnt yet
            var c = new Vector2(Size.X / 2, s / 2 + 3);
            DrawArc(c - new Vector2(0, 5), 4.5f, MathF.PI, MathF.Tau, 12, Ornament.Aged, 2, true);
            DrawRect(new Rect2(c - new Vector2(6.5f, 4.5f), new Vector2(13, 10)), Ornament.Aged);
            DrawCircle(c + new Vector2(0, .5f), 1.6f, Ornament.IronDark, true, -1, true);
        }
        if (!here)
        {
            // a house in the upper right: hired in its building; how many wait there, in the upper left
            var c = new Vector2(Size.X - 7.5f, 7.5f);
            DrawCircle(c, 6.4f, new Color(0, 0, 0, .72f), true, -1, true);
            var ink = open ? Ornament.BronzeLight : Ornament.Aged;
            DrawColoredPolygon(new[] { c + new Vector2(-4.2f, -.4f), c + new Vector2(0, -4.4f), c + new Vector2(4.2f, -.4f) }, ink);
            DrawRect(new Rect2(c + new Vector2(-3, -.6f), new Vector2(6, 4.4f)), ink);
            DrawRect(new Rect2(c + new Vector2(-.9f, 1.4f), new Vector2(1.8f, 2.4f)), new Color(0, 0, 0, .85f));
            if (open && !sim.Sandbox && hirer.Stock.TryGetValue(Kind.Key, out float n))
            {
                DrawRect(new Rect2(1, 1, 11, 12), new Color(0, 0, 0, .72f));
                DrawString(Ornament.Mono, new Vector2(1, 10.5f), $"{(int)n}", HorizontalAlignment.Center, 11, 10, n >= 1 ? Ornament.Parchment : Ornament.Faded);
            }
        }
        // the price on a little plate along the foot: the same as wherever it is hired
        var plate = new Rect2(0, Size.Y - 13, Size.X, 13);
        DrawRect(plate, new Color(0, 0, 0, .78f));
        DrawString(Ornament.Mono, new Vector2(0, Size.Y - 3), $"{sim.HireCost(Kind)}", HorizontalAlignment.Center, Size.X, 10,
            !open ? Ornament.Faded : can ? Ornament.BronzeLight : Ornament.Blood);
        DrawRect(r, _hover && open ? Craft.BrassLight : open ? Craft.Brass : Craft.BrassDark, false, _hover ? 1.6f : 1.1f);
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}
