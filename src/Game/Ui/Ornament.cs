using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The look of the in-game interface: plain dark plates with a thin bronze line, wells for the tools, parchment
/// lettering, ember for whatever is selected and blood for what cannot be paid. Everything is drawn in code.
/// </summary>
public static class Ornament
{
    public static readonly Color IronDark = new("0b080d"), Iron = new("16111a"), IronMid = new("201922"), IronLight = new("2b2330");
    public static readonly Color Bronze = new("b0864a"), BronzeLight = new("e2bb72"), BronzeDark = new("5b4122"), BronzeDeep = new("33240f");
    public static readonly Color Parchment = new("ecdfc2"), Aged = new("b9a789"), Faded = new("8c806e");
    public static readonly Color Ember = new("eb8a34"), EmberGlow = new("ffad4f"), EmberDim = new("3d2411");
    public static readonly Color Blood = new("d24a33"), Moss = new("94b46c");

    // PT Serif for all the text and the figures, Cormorant Garamond for the large headings: both ship with the game
    // (SIL OFL, see fonts/); the system serif only steps in for a glyph they lack.
    static readonly SystemFont SystemSerif = Sys(400, false, "PT Serif", "Georgia", "Times New Roman", "Noto Serif", "DejaVu Serif");
    static readonly Font SerifFile = Bundled("res://fonts/PTSerif-Regular.ttf");
    static readonly Font SerifBoldFile = Bundled("res://fonts/PTSerif-Bold.ttf");
    static readonly Font SerifItalicFile = Bundled("res://fonts/PTSerif-Italic.ttf");
    static readonly Font CormorantFile = Bundled("res://fonts/CormorantGaramond-Variable.ttf");

    public static readonly Font Serif = Plain(SerifFile);
    public static readonly Font SerifBold = Plain(SerifBoldFile);
    public static readonly Font SerifItalic = Plain(SerifItalicFile);
    /// <summary>Large headings: the title, the tool's name over its options. Its figures stand on the line like the text's.</summary>
    public static readonly Font Display = Weight(CormorantFile, 650);
    /// <summary>Small figures and keys: the text's own serif in bold, so a number is never in another face than its words.</summary>
    public static readonly Font Mono = Plain(SerifBoldFile);
    /// <summary>Spaced capitals for captions.</summary>
    public static readonly FontVariation Caps = Plain(SerifBoldFile, spacing: 1);

    static SystemFont Sys(int weight, bool italic, params string[] names) => new()
    {
        FontNames = names, FontWeight = weight, FontItalic = italic,
        Antialiasing = TextServer.FontAntialiasing.Gray, Hinting = TextServer.Hinting.Light,
    };

    static Font Bundled(string path)
    {
        var f = ResourceLoader.Exists(path) ? GD.Load<FontFile>(path) : null;
        if (f == null) return SystemSerif;
        f.Fallbacks = new Godot.Collections.Array<Font> { SystemSerif };
        return f;
    }

    static FontVariation Weight(Font font, int wght, int spacing = 0)
    {
        var v = Plain(font, spacing);
        if (font is FontFile)
        {
            // let go of here and now, not by the collector's thread, which may not touch the engine
            using var axes = new Godot.Collections.Dictionary { { TextServerManager.GetPrimaryInterface().NameToTag("wght"), wght } };
            v.VariationOpentype = axes;
        }
        return v;
    }

    /// <summary>A face as it is, its figures lining (all of a height with the capitals), not old-style.</summary>
    static FontVariation Plain(Font font, int spacing = 0)
    {
        var ts = TextServerManager.GetPrimaryInterface();
        using var features = new Godot.Collections.Dictionary { { ts.NameToTag("lnum"), 1 }, { ts.NameToTag("onum"), 0 } };
        return new FontVariation { BaseFont = font, SpacingGlyph = spacing, OpentypeFeatures = features };
    }

    public static Color Fade(Color c, float a) => new(c, c.A * a);

    /// <summary>A material's own colour, as the interface shows it.</summary>
    public static Color MatColor(byte m) => new((Mat.Def[m].Srgb << 8) | 0xFF);

    public static Theme MakeTheme()
    {
        var t = new Theme { DefaultFont = Serif, DefaultFontSize = 15 };
        t.SetColor("font_color", "Label", Parchment);
        t.SetColor("font_outline_color", "Label", new Color(0, 0, 0, .85f));
        t.SetStylebox("normal", "Button", Btn(new Color("221c27"), Fade(Bronze, .3f)));
        t.SetStylebox("hover", "Button", Btn(Craft.Raised, Fade(BronzeLight, .6f)));
        t.SetStylebox("pressed", "Button", Btn(EmberDim, Ember));
        t.SetStylebox("hover_pressed", "Button", Btn(EmberDim, EmberGlow));
        t.SetStylebox("disabled", "Button", Btn(Fade(Iron, .7f), new Color(0, 0, 0, 0)));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetColor("font_color", "Button", Parchment);
        t.SetColor("font_hover_color", "Button", BronzeLight);
        t.SetColor("font_pressed_color", "Button", EmberGlow);
        t.SetColor("font_hover_pressed_color", "Button", EmberGlow);
        t.SetColor("font_focus_color", "Button", Parchment);
        t.SetColor("font_disabled_color", "Button", Faded);
        t.SetFont("font", "Button", SerifBold);
        t.SetFontSize("font_size", "Button", 13);
        t.SetStylebox("panel", "TooltipPanel", new StyleBoxEmpty());
        t.SetColor("font_color", "TooltipLabel", Parchment);
        var track = Btn(Fade(IronDark, .6f), Fade(IronDark, 0), 0, 3);
        t.SetStylebox("scroll", "VScrollBar", track);
        t.SetStylebox("grabber", "VScrollBar", Btn(BronzeDark, BronzeDark, 0, 3));
        t.SetStylebox("grabber_highlight", "VScrollBar", Btn(Bronze, Bronze, 0, 3));
        t.SetStylebox("grabber_pressed", "VScrollBar", Btn(BronzeLight, BronzeLight, 0, 3));
        return t;
    }

    static StyleBoxFlat Btn(Color bg, Color border, int bw = 1, int radius = 4)
    {
        var b = new StyleBoxFlat { BgColor = bg, BorderColor = border, AntiAliasing = true };
        b.SetBorderWidthAll(bw);
        b.SetCornerRadiusAll(radius);
        b.ContentMarginLeft = b.ContentMarginRight = 10;
        b.ContentMarginTop = b.ContentMarginBottom = 4;
        return b;
    }

    // ------------------------------------------------------------------ drawing

    public static void Gradient(CanvasItem c, Rect2 r, Color top, Color bottom)
    {
        var p = new[] { r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y) };
        c.DrawPolygon(p, new[] { top, top, bottom, bottom });
    }

    /// <summary>A plate: plain dark, a thin bronze line round it, a soft shadow under it (Craft.Plate).</summary>
    public static void Plate(CanvasItem c, Rect2 r, float alpha = 1) => Craft.Plate(c, r, alpha);

    /// <summary>A lozenge: a mark on the map, an empty well's sign.</summary>
    public static void Diamond(CanvasItem c, Vector2 q, float s, Color col) =>
        c.DrawColoredPolygon(new[] { q + new Vector2(0, -s), q + new Vector2(s, 0), q + new Vector2(0, s), q + new Vector2(-s, 0) }, col);

    /// <summary>The mark of something just let out: an ember dot with a glow, at a corner.</summary>
    public static void FreshMark(CanvasItem c, Vector2 at)
    {
        float pulse = .6f + .4f * Mathf.Sin(Time.GetTicksMsec() / 1000f * 4);
        c.DrawCircle(at, 6, new Color(EmberGlow, .25f * pulse), true, -1, true);
        c.DrawCircle(at, 3.5f, EmberGlow, true, -1, true);
    }

    /// <summary>A thin line between groups of things.</summary>
    public static void Divider(CanvasItem c, Vector2 a, Vector2 b, float alpha = 1) => c.DrawLine(a, b, Fade(Craft.Line, alpha), 1, true);

    /// <summary>A well a tool or material sits in: a little darker than its plate, its rim lit under the pointer, ember when selected.</summary>
    public static void Well(CanvasItem c, Rect2 r, bool hover, bool selected, bool disabled)
    {
        if (selected)
            for (int i = 3; i >= 1; i--) c.DrawRect(r.Grow(i * 1.6f), new Color(Ember, .05f * (4 - i)), false, 1.6f);
        _well.BgColor = selected ? new Color("3a2213") : hover ? Craft.Raised : Craft.Deep;
        _well.BorderColor = selected ? Ember : hover ? Fade(BronzeLight, .7f) : Fade(Bronze, .28f);
        _well.SetBorderWidthAll(selected ? 2 : 1);
        c.DrawStyleBox(_well, r);
        if (disabled) c.DrawRect(r.Grow(-1), new Color(0, 0, 0, .5f));
    }

    static readonly StyleBoxFlat _well = new() { AntiAliasing = true, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };

    /// <summary>Text with a dark halo, for lettering laid straight over the map.</summary>
    public static void Lettering(CanvasItem c, Font f, Vector2 at, string text, int size, Color col, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1)
    {
        c.DrawStringOutline(f, at, text, align, width, size, 5, new Color(0, 0, 0, .75f * col.A));
        c.DrawString(f, at, text, align, width, size, col);
    }
}
