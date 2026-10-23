using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// A kind of ours in the citadel's muster: its face, with the gold it costs in the corner; a click takes one on. A kind
/// the household has not learnt yet stands dark behind a lock; one there is no gold or no place for has its price in red.
/// The tooltip says what the kind does, what it costs, how many places it takes, and why not.
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
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { Juice.Press(this); Press(); AcceptEvent(); }
    }

    public override string _GetTooltip(Vector2 at)
    {
        var sim = _m.Sim;
        string why = sim.CantEnlist(Kind);
        string room = Kind.Room > 1 ? $", занимает {Kind.Room} {Places(Kind.Room)}" : "";
        string foot = why == null ? $"Щелчок — нанять за {sim.HireCost(Kind)} золота{room}. Выходит из цитадели сразу." : $"Нельзя: {why}.";
        if (!sim.Unlocked(Kind)) foot = $"Нужно знание «{Sim.LoreOf(Kind.Lore)?.Name ?? Kind.Lore}» (вкладка «Знания», страница «Войско»).";
        return $"{Kind.Name} · {sim.HireCost(Kind)} золота{room}\n{Retinue.About(Kind)}\n{foot}";
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
