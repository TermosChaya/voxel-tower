using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The look of the in-game interface: blackened iron plates with an inlaid bronze rule, corner brackets and rivets,
/// sunken wells for tools, parchment lettering, ember for whatever is selected and blood for what cannot be paid.
/// Everything is drawn in code, so there are no texture files to keep in sync.
/// </summary>
public static class Ornament
{
    public static readonly Color IronDark = new("0b080d"), Iron = new("16111a"), IronMid = new("201922"), IronLight = new("2b2330");
    public static readonly Color Bronze = new("b0864a"), BronzeLight = new("e2bb72"), BronzeDark = new("5b4122"), BronzeDeep = new("33240f");
    public static readonly Color Parchment = new("ecdfc2"), Aged = new("b4a181"), Faded = new("766a5a");
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
            v.VariationOpentype = new Godot.Collections.Dictionary { { TextServerManager.GetPrimaryInterface().NameToTag("wght"), wght } };
        return v;
    }

    /// <summary>A face as it is, its figures lining (all of a height with the capitals), not old-style.</summary>
    static FontVariation Plain(Font font, int spacing = 0)
    {
        var ts = TextServerManager.GetPrimaryInterface();
        return new FontVariation
        {
            BaseFont = font, SpacingGlyph = spacing,
            OpentypeFeatures = new Godot.Collections.Dictionary { { ts.NameToTag("lnum"), 1 }, { ts.NameToTag("onum"), 0 } },
        };
    }

    /// <summary>Hammered-metal grain laid faintly over every plate.</summary>
    public static readonly ImageTexture Grain = MakeGrain();

    static ImageTexture MakeGrain()
    {
        const int n = 128;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var a = new ValueNoise(91); var b = new ValueNoise(57); var r = new Mulberry32(17);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float v = (float)(a.Sample(x * .11, y * .11) * .55 + b.Sample(x * .37 + 40, y * .37) * .25 + r.Next() * .2);
                img.SetPixel(x, y, new Color(v, v, v));
            }
        return ImageTexture.CreateFromImage(img);
    }

    public static Color Fade(Color c, float a) => new(c, c.A * a);

    /// <summary>A material's own colour, as the interface shows it.</summary>
    public static Color MatColor(byte m) => new((Mat.Def[m].Srgb << 8) | 0xFF);

    public static Theme MakeTheme()
    {
        var t = new Theme { DefaultFont = Serif, DefaultFontSize = 15 };
        t.SetColor("font_color", "Label", Parchment);
        t.SetColor("font_outline_color", "Label", new Color(0, 0, 0, .85f));
        t.SetStylebox("normal", "Button", Btn(IronMid, BronzeDark));
        t.SetStylebox("hover", "Button", Btn(IronLight, Bronze));
        t.SetStylebox("pressed", "Button", Btn(EmberDim, Ember));
        t.SetStylebox("hover_pressed", "Button", Btn(EmberDim, EmberGlow));
        t.SetStylebox("disabled", "Button", Btn(Iron, IronLight));
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

    static StyleBoxFlat Btn(Color bg, Color border, int bw = 1, int radius = 2)
    {
        var b = new StyleBoxFlat { BgColor = bg, BorderColor = border, AntiAliasing = true };
        b.SetBorderWidthAll(bw);
        b.SetCornerRadiusAll(radius);
        b.ContentMarginLeft = b.ContentMarginRight = 8;
        b.ContentMarginTop = b.ContentMarginBottom = 3;
        return b;
    }

    // ------------------------------------------------------------------ drawing

    public static void Gradient(CanvasItem c, Rect2 r, Color top, Color bottom)
    {
        var p = new[] { r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y) };
        c.DrawPolygon(p, new[] { top, top, bottom, bottom });
    }

    /// <summary>An iron plate with an inlaid bronze rule; ornate plates get corner brackets and rivets.</summary>
    public static void Plate(CanvasItem c, Rect2 r, float alpha = .96f, bool ornate = true)
    {
        for (int i = 3; i >= 1; i--)
            c.DrawRect(new Rect2(r.Position + new Vector2(0, 3), r.Size).Grow(i * 2.5f), new Color(0, 0, 0, .1f * alpha));
        c.DrawRect(r, Fade(IronDark, alpha));
        var inner = r.Grow(-2);
        Gradient(c, inner, Fade(IronLight, alpha), Fade(Iron, alpha));
        c.DrawTextureRect(Grain, inner, true, new Color(1, 1, 1, .06f * alpha));
        c.DrawLine(new Vector2(inner.Position.X, inner.Position.Y + .5f), new Vector2(inner.End.X, inner.Position.Y + .5f), new Color(1, .9f, .75f, .12f * alpha), 1);
        c.DrawLine(new Vector2(inner.Position.X, inner.End.Y - .5f), new Vector2(inner.End.X, inner.End.Y - .5f), new Color(0, 0, 0, .5f * alpha), 1);
        var f = r.Grow(-5);
        c.DrawRect(f, Fade(BronzeDeep, alpha), false, 1);
        c.DrawRect(f.Grow(-1), Fade(Bronze, .5f * alpha), false, 1);
        if (!ornate) return;
        Corner(c, f.Position, new Vector2(1, 1), alpha);
        Corner(c, new Vector2(f.End.X, f.Position.Y), new Vector2(-1, 1), alpha);
        Corner(c, new Vector2(f.Position.X, f.End.Y), new Vector2(1, -1), alpha);
        Corner(c, f.End, new Vector2(-1, -1), alpha);
    }

    static void Corner(CanvasItem c, Vector2 p, Vector2 d, float alpha)
    {
        var gold = Fade(BronzeLight, .85f * alpha);
        var a = p + new Vector2(0, 15 * d.Y); var e = p + new Vector2(15 * d.X, 0);
        c.DrawPolyline(new[] { a + new Vector2(.8f, 1), p + new Vector2(.8f, 1), e + new Vector2(.8f, 1) }, new Color(0, 0, 0, .6f * alpha), 3.2f, true);
        c.DrawPolyline(new[] { a, p, e }, gold, 2f, true);
        Diamond(c, p + d * 6, 2.6f, gold);
        Rivet(c, p, 2.8f, alpha);
    }

    public static void Diamond(CanvasItem c, Vector2 q, float s, Color col) =>
        c.DrawColoredPolygon(new[] { q + new Vector2(0, -s), q + new Vector2(s, 0), q + new Vector2(0, s), q + new Vector2(-s, 0) }, col);

    public static void Rivet(CanvasItem c, Vector2 p, float rad, float alpha = 1)
    {
        c.DrawCircle(p + new Vector2(.7f, 1f), rad + .6f, new Color(0, 0, 0, .55f * alpha), true, -1, true);
        c.DrawCircle(p, rad, Fade(BronzeDark, alpha), true, -1, true);
        c.DrawCircle(p - new Vector2(rad * .3f, rad * .3f), rad * .55f, Fade(BronzeLight, .8f * alpha), true, -1, true);
    }

    /// <summary>A rule with a lozenge in the middle, between groups of things.</summary>
    public static void Divider(CanvasItem c, Vector2 a, Vector2 b, float alpha = 1)
    {
        var mid = (a + b) / 2;
        var dir = (b - a).Normalized();
        c.DrawLine(a, mid - dir * 9, Fade(BronzeDark, alpha), 1, true);
        c.DrawLine(mid + dir * 9, b, Fade(BronzeDark, alpha), 1, true);
        Diamond(c, mid, 3.5f, Fade(BronzeLight, .9f * alpha));
        c.DrawCircle(mid - dir * 7, 1.2f, Fade(Bronze, alpha), true, -1, true);
        c.DrawCircle(mid + dir * 7, 1.2f, Fade(Bronze, alpha), true, -1, true);
    }

    /// <summary>A sunken well a tool or material sits in. Selected wells glow like embers.</summary>
    public static void Well(CanvasItem c, Rect2 r, bool hover, bool selected, bool disabled)
    {
        if (selected)
            for (int i = 4; i >= 1; i--) c.DrawRect(r.Grow(i * 1.7f), new Color(Ember, .06f * (5 - i)), false, 1.7f);
        c.DrawRect(r, IronDark);
        var i1 = r.Grow(-1.5f);
        if (selected) Gradient(c, i1, new Color("2b190d"), new Color("452814"));
        else Gradient(c, i1, new Color("09070b"), hover ? new Color("251d29") : new Color("1a141d"));
        c.DrawLine(new Vector2(i1.Position.X, i1.Position.Y + .8f), new Vector2(i1.End.X, i1.Position.Y + .8f), new Color(0, 0, 0, .7f), 1.6f);
        c.DrawRect(r, selected ? Ember : hover ? BronzeLight : BronzeDark, false, selected ? 1.6f : 1f);
        if (disabled) c.DrawRect(i1, new Color(0, 0, 0, .5f));
    }

    /// <summary>Text with a dark halo, for lettering laid straight over the map.</summary>
    public static void Lettering(CanvasItem c, Font f, Vector2 at, string text, int size, Color col, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1)
    {
        c.DrawStringOutline(f, at, text, align, width, size, 5, new Color(0, 0, 0, .75f * col.A));
        c.DrawString(f, at, text, align, width, size, col);
    }
}
