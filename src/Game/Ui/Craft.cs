using Godot;

namespace VoxelTower.Game;

/// <summary>
/// What the frame of the interface is made of: plain dark plates with rounded corners, a soft shadow under them and one
/// thin bronze line round them; wells a little darker where things are read and pressed; tabs that light up under the
/// pointer and are underlined in ember when open. Nothing more, so that the field, the figures and the icons carry the
/// look.
/// </summary>
public static class Craft
{
    /// <summary>The plates' fill, a well's, a raised thing under the pointer; and the line round a plate.</summary>
    public static readonly Color Fill = new("17131b"), Deep = new("0d0b10"), Raised = new("2a2230"), Line = new(Ornament.Bronze, .3f);
    public static readonly Color Brass = new("a8803f"), BrassLight = new("e6c47a"), BrassDark = new("4e3819");

    /// <summary>How round a plate's corners are, and a well's.</summary>
    public const int Round = 5, WellRound = 4;

    static readonly StyleBoxFlat _plate = Box(Fill, Line, 1, Round, true), _well = Box(Deep, new Color(0, 0, 0, 0), 0, WellRound, false),
        _bar = Box(Fill, new Color(0, 0, 0, 0), 0, 0, true);

    static StyleBoxFlat Box(Color fill, Color line, int width, int round, bool shadow)
    {
        var b = new StyleBoxFlat { BgColor = fill, BorderColor = line, AntiAliasing = true };
        b.SetBorderWidthAll(width);
        b.SetCornerRadiusAll(round);
        if (shadow) { b.ShadowColor = new Color(0, 0, 0, .35f); b.ShadowSize = 10; b.ShadowOffset = new Vector2(0, 3); }
        return b;
    }

    /// <summary>A plate as a container's own box, with room inside it.</summary>
    public static StyleBoxFlat PlateBox()
    {
        var b = Box(new Color(Fill, .95f), Line, 1, Round, true);
        b.ContentMarginLeft = b.ContentMarginRight = 14; b.ContentMarginTop = b.ContentMarginBottom = 7;
        return b;
    }

    /// <summary>A plate: the panels, the clock, the dialogs, a menu.</summary>
    public static void Plate(CanvasItem c, Rect2 r, float alpha = 1)
    {
        _plate.BgColor = new Color(Fill, .95f * alpha);
        _plate.BorderColor = new Color(Line, Line.A * alpha);
        _plate.ShadowColor = new Color(0, 0, 0, .35f * alpha);
        c.DrawStyleBox(_plate, r);
    }

    /// <summary>A bar along the screen's edge, the beam above and the band below: a plate without corners, a line on its side toward the field.</summary>
    public static void Bar(CanvasItem c, Rect2 r, bool lineBelow)
    {
        c.DrawStyleBox(_bar, r);
        float y = lineBelow ? r.End.Y - .5f : r.Position.Y + .5f;
        c.DrawLine(new Vector2(r.Position.X, y), new Vector2(r.End.X, y), Line, 1);
    }

    /// <summary>A well sunk a little into a plate, where text is read and buttons are pressed.</summary>
    public static void Well(CanvasItem c, Rect2 r, float alpha = 1)
    {
        _well.BgColor = new Color(Deep, .8f * alpha);
        c.DrawStyleBox(_well, r);
    }

    /// <summary>A tab, a button of the frame: plain, lit under the pointer, underlined in ember when its panel is open.</summary>
    public static void Tab(CanvasItem c, Rect2 r, bool hover, bool selected)
    {
        if (selected || hover)
        {
            _well.BgColor = selected ? new Color(Ornament.EmberDim, .9f) : new Color(Raised, .9f);
            c.DrawStyleBox(_well, r);
        }
        if (selected) c.DrawLine(new Vector2(r.Position.X + 4, r.End.Y - 1), new Vector2(r.End.X - 4, r.End.Y - 1), Ornament.Ember, 2);
    }
}
