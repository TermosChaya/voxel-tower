using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// A kind of ours in the citadel's muster: its face, with the gold it costs in the corner; a click takes one on. A kind
/// the household has not learnt yet stands dark behind a lock; one there is no gold or no place for has its price in red.
/// The tooltip says what the kind does, what it costs, how long it is made ready, how many places it takes, and why not.
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

    /// <summary>Takes one of the kind on (the self-test calls it as a click would).</summary>
    public void Press() => _m.Command.Enlist(Kind);

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { Press(); AcceptEvent(); }
    }

    public override string _GetTooltip(Vector2 at)
    {
        var sim = _m.Sim;
        string why = sim.CantEnlist(Kind);
        string room = Kind.Room > 1 ? $", занимает {Kind.Room} {Places(Kind.Room)}" : "";
        string time = Fmt.One(sim.TrainTime(Kind));
        string foot = why == null ? $"Щелчок — нанять за {sim.HireCost(Kind)} золота, готов через {time} с{room}." : $"Нельзя: {why}.";
        if (!sim.Unlocked(Kind)) foot = $"Нужно знание «{Sim.LoreOf(Kind.Lore)?.Name ?? Kind.Lore}» (вкладка «Знания», страница «Войско»).";
        return $"{Kind.Name} · {sim.HireCost(Kind)} золота · {time} с{room}\n{Retinue.About(Kind)}\n{foot}";
    }

    static string Places(int n) => n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? "места" : n % 10 == 1 && n % 100 != 11 ? "место" : "мест";

    public override void _Draw()
    {
        var sim = _m.Sim;
        var r = new Rect2(Vector2.Zero, Size);
        bool open = sim.Unlocked(Kind), can = sim.CantEnlist(Kind) == null;
        Ornament.Gradient(this, r, new Color("2a2024"), new Color("0e0b0d"));
        float s = Size.X - 2;
        DrawTextureRect(Portraits.Of(Kind, 0, 48), new Rect2(1, 1, s, s), false, open ? Colors.White : new Color(.32f, .3f, .3f));
        if (!open)
        {
            // a padlock over the face
            var c = new Vector2(Size.X / 2, s / 2 + 3);
            DrawArc(c - new Vector2(0, 5), 4.5f, MathF.PI, MathF.Tau, 12, Ornament.Aged, 2, true);
            DrawRect(new Rect2(c - new Vector2(6.5f, 4.5f), new Vector2(13, 10)), Ornament.Aged);
            DrawCircle(c + new Vector2(0, .5f), 1.6f, Ornament.IronDark, true, -1, true);
        }
        // the price on a little plate along the foot
        var plate = new Rect2(0, Size.Y - 13, Size.X, 13);
        DrawRect(plate, new Color(0, 0, 0, .78f));
        DrawString(Ornament.Mono, new Vector2(0, Size.Y - 3), $"{sim.HireCost(Kind)}", HorizontalAlignment.Center, Size.X, 10,
            !open ? Ornament.Faded : can ? Ornament.BronzeLight : Ornament.Blood);
        DrawRect(r, _hover && open ? Craft.BrassLight : open ? Craft.Brass : Craft.BrassDark, false, _hover ? 1.6f : 1.1f);
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}

/// <summary>
/// One in the citadel's line: its face, and for the first a bar of how far it is made ready and the seconds left; the rest
/// wait dim. An empty place in the line is a faint frame. A click sends the one there away, its gold given back.
/// </summary>
public partial class RecruitFace : Control
{
    readonly Main _m;
    readonly int _i;

    public RecruitFace() { }

    public RecruitFace(Main m, int i)
    {
        _m = m; _i = i;
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = " ";
    }

    Recruit Here => _i < _m.Sim.Recruits.Count ? _m.Sim.Recruits[_i] : null;

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("Muster");
        if (IsVisibleInTree()) QueueRedraw();
        MouseDefaultCursorShape = Here != null ? CursorShape.PointingHand : CursorShape.Arrow;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && Here != null) { _m.Sim.Dismiss(_i); _m.Hud.Refresh(); AcceptEvent(); }
    }

    public override string _GetTooltip(Vector2 at)
    {
        var r = Here;
        if (r == null) return "Свободное место в очереди\nЩёлкните по портрету справа, чтобы нанять.";
        string state = _i == 0 ? $"готовится: ещё {Math.Ceiling(_m.Sim.TrainTime(r.Kind) - r.Done)} с" : $"ждёт очереди ({_i + 1}-й)";
        return $"{r.Kind.Name}: {state}\nЩелчок — отменить, {r.Paid} золота вернутся.";
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        var r = Here;
        if (r == null)
        {
            DrawRect(rect.Grow(-1), new Color(0, 0, 0, .35f));
            DrawRect(rect.Grow(-1), Ornament.Fade(Craft.BrassDark, .8f), false, 1);
            return;
        }
        Ornament.Gradient(this, rect, new Color("2a2024"), new Color("0e0b0d"));
        float s = Size.X - 2;
        DrawTextureRect(Portraits.Of(r.Kind, 0, 48), new Rect2(1, 1, s, s), false, _i == 0 ? Colors.White : new Color(.6f, .58f, .56f));
        if (_i == 0)
        {
            float f = _m.Sim.RecruitShare, by = s + 2, bh = Size.Y - by - 1;
            DrawRect(new Rect2(1, by, Size.X - 2, bh), new Color(0, 0, 0, .75f));
            DrawRect(new Rect2(1, by, (Size.X - 2) * f, bh), Ornament.Ember);
        }
        DrawRect(rect, _i == 0 ? Ornament.EmberGlow : Craft.Brass, false, 1.1f);
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}
