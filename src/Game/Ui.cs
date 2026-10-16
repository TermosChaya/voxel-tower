using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The builder's ledger look, carried over from the prototype: ash-violet dusk, ember-gold for what you can act on,
/// blood-red for what the horde plans, steel-blue for your own people.
/// </summary>
public static class Ui
{
    public static readonly Color Bg = Color.FromHtml("0f0c13"), Panel = Color.FromHtml("16121a"), Panel2 = Color.FromHtml("201b25");
    public static readonly Color Line = Color.FromHtml("2c2632"), LineStrong = Color.FromHtml("4d4555");
    public static readonly Color Fg = Color.FromHtml("ece3d4"), Muted = Color.FromHtml("a39994");
    public static readonly Color Accent = Color.FromHtml("e58a3a"), AccentDim = Color.FromHtml("3a2313");
    public static readonly Color Horde = Color.FromHtml("cf4a3c"), Guard = Color.FromHtml("8fa9cf");
    public static readonly Color Ok = Color.FromHtml("7fae5c"), Warn = Color.FromHtml("d9b03e"), Bad = Color.FromHtml("d4513d");

    public static readonly SystemFont Display = Sys("Georgia", "Times New Roman", "Noto Serif", "DejaVu Serif");
    public static readonly SystemFont Body = Sys("Avenir Next", "Helvetica Neue", "Segoe UI", "Noto Sans", "Arial");
    public static readonly SystemFont Mono = Sys("Menlo", "SF Mono", "Consolas", "DejaVu Sans Mono");
    /// <summary>Small spaced capitals for section labels.</summary>
    public static readonly FontVariation Kicker = new() { BaseFont = Mono, SpacingGlyph = 2 };

    static SystemFont Sys(params string[] names) => new() { FontNames = names, Antialiasing = TextServer.FontAntialiasing.Gray };

    public static StyleBoxFlat Box(Color bg, Color border, int borderW = 1, int radius = 4, int padX = 10, int padY = 6)
    {
        var b = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        b.SetBorderWidthAll(borderW);
        b.SetCornerRadiusAll(radius);
        b.ContentMarginLeft = b.ContentMarginRight = padX;
        b.ContentMarginTop = b.ContentMarginBottom = padY;
        return b;
    }

    public static Theme MakeTheme()
    {
        var t = new Theme { DefaultFont = Body, DefaultFontSize = 15 };
        t.SetColor("font_color", "Label", Fg);
        t.SetStylebox("normal", "Button", Box(Panel2, LineStrong));
        t.SetStylebox("hover", "Button", Box(Panel2.Lightened(.04f), Accent.Darkened(.35f)));
        t.SetStylebox("pressed", "Button", Box(AccentDim, Accent));
        t.SetStylebox("hover_pressed", "Button", Box(AccentDim.Lightened(.05f), Accent));
        t.SetStylebox("disabled", "Button", Box(Panel, Line));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetColor("font_color", "Button", Fg);
        t.SetColor("font_hover_color", "Button", Fg);
        t.SetColor("font_pressed_color", "Button", Fg);
        t.SetColor("font_hover_pressed_color", "Button", Fg);
        t.SetColor("font_focus_color", "Button", Fg);
        t.SetColor("font_disabled_color", "Button", Muted.Darkened(.3f));
        t.SetFontSize("font_size", "Button", 14);
        t.SetStylebox("panel", "PanelContainer", Box(Panel, Line, 0, 0, 0, 0));
        t.SetStylebox("panel", "TooltipPanel", Box(Panel2, LineStrong));
        t.SetColor("font_color", "TooltipLabel", Fg);
        return t;
    }

    public static Label Text(string text, int size = 14, Color? color = null, Font font = null, bool wrap = false)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Fg);
        if (font != null) l.AddThemeFontOverride("font", font);
        if (wrap) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; }
        return l;
    }

    public static Label Section(string text) => Text(text.ToUpperInvariant(), 11, Muted, Kicker);

    public static Button Button(string text, System.Action onPress, bool toggle = false)
    {
        var b = new Button { Text = text, ToggleMode = toggle, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        if (onPress != null) b.Pressed += onPress;
        return b;
    }
}
