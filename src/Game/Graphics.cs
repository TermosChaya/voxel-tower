using System;
using System.Collections.Generic;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The picture as the player set it in the settings (<see cref="Settings"/>), put to the renderer: the sun's shadows
/// (none, plain on a smaller map with a hard edge, soft on a large one, or softest, their penumbra widening away from
/// what casts them, which costs the most of all), the edges smoothed (not at all, FXAA, or MSAA
/// two or four times over), the share of the screen's pixels the world is drawn at (filled out to the screen with FSR,
/// the interface always drawn whole), the blades of grass (none, only close to, all), the corners darkened, the glow.
/// A weak machine gets its frames back with the lower ones; the highest asks the most of it.
/// </summary>
public static class Graphics
{
    public static void Apply(Main m)
    {
        var vp = m.GetViewport();
        int aa = Settings.Smoothing;
        vp.Msaa3D = aa == 3 ? Viewport.Msaa.Msaa4X : aa == 2 ? Viewport.Msaa.Msaa2X : Viewport.Msaa.Disabled;
        vp.ScreenSpaceAA = aa == 1 ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        float detail = Settings.Details[Settings.Detail];
        vp.Scaling3DMode = detail < 1 ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        vp.Scaling3DScale = detail;
        RenderingServer.DirectionalShadowAtlasSetSize(Settings.Shadows >= 2 ? 4096 : 2048, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(Settings.Shadows switch
        {
            3 => RenderingServer.ShadowQuality.SoftHigh, 2 => RenderingServer.ShadowQuality.SoftMedium, _ => RenderingServer.ShadowQuality.Hard,
        });
        if (m.Atmos?.Sun is { } sun)
        {
            sun.ShadowEnabled = Settings.Shadows > 0;
            // the penumbra widening away from what casts it is the dearest part of a shadow: only the softest have it
            sun.LightAngularDistance = Settings.Shadows >= 3 ? 1.2f : 0;
        }
        if (m.Atmos?.Env is { } env)
        {
            env.GlowEnabled = Settings.Glow;
            env.SsaoEnabled = Settings.Occlusion;
            env.SsaoRadius = 1.2f;
            env.SsaoIntensity = 1.6f;
            env.SsaoDetail = .5f;
        }
    }
}

/// <summary>The frames a second, and how long the slowest of the last ones took, small in the top left corner — when the player asked for it.</summary>
public partial class FrameMeter : Label
{
    double _worst, _since;
    float _shown = -1;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Position = new Vector2(8, 4);
        AddThemeFontOverride("font", Ornament.SerifBold);
        AddThemeFontSizeOverride("font_size", 13);
        AddThemeColorOverride("font_color", Ornament.Parchment);
        AddThemeColorOverride("font_outline_color", Ornament.IronDark);
        AddThemeConstantOverride("outline_size", 4);
    }

    public override void _Process(double delta)
    {
        Visible = Settings.ShowFps;
        if (!Visible) return;
        _worst = Math.Max(_worst, delta);
        if ((_since += delta) < .5) return;
        Text = $"{Engine.GetFramesPerSecond():0} к/с · хуже всего {_worst * 1000:0} мс";
        _since = 0; _worst = 0;
    }
}

/// <summary>
/// Where a frame's time goes, for --perf: every view's work each frame timed under its name, summed till it is read.
/// Nothing is timed unless a measuring run asked for it.
/// </summary>
public static class Prof
{
    public static bool On;
    static readonly System.Collections.Generic.Dictionary<string, long> _ticks = new(), _bytes = new();
    /// <summary>The same for this frame alone, and what was done in it by count (chunks meshed…), for the late frames (<see cref="Hitches"/>).</summary>
    public static readonly System.Collections.Generic.Dictionary<string, long> Frame = new(), Counts = new();

    /// <summary>A part of the frame timed, and named to the watchdog while it runs (Diag.Section).</summary>
    public readonly struct Scope : IDisposable
    {
        readonly string _name, _outer;
        readonly long _t0, _b0;
        public Scope(string name)
        {
            _name = name; _outer = Diag.Section; Diag.Section = name;
            _t0 = On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            _b0 = On ? GC.GetAllocatedBytesForCurrentThread() : 0;
        }
        public void Dispose()
        {
            Diag.Section = _outer;
            if (!On || _t0 == 0) return;
            long d = System.Diagnostics.Stopwatch.GetTimestamp() - _t0;
            _ticks[_name] = _ticks.GetValueOrDefault(_name) + d;
            Frame[_name] = Frame.GetValueOrDefault(_name) + d;
            _bytes[_name] = _bytes.GetValueOrDefault(_name) + GC.GetAllocatedBytesForCurrentThread() - _b0;
        }
    }

    public static Scope Time(string name) => new(name);

    /// <summary>So much of something done this frame (chunks meshed, grass made…), told with a late frame.</summary>
    public static void Count(string name, int n = 1)
    {
        if (On) Counts[name] = Counts.GetValueOrDefault(name) + n;
    }

    public static void Reset() { _ticks.Clear(); _bytes.Clear(); }

    /// <summary>Who allocated the most on the main thread, in kilobytes a frame: "BuildTools 410, Hud 12 …".</summary>
    public static string Allocating(int frames, int n = 8)
    {
        var all = new System.Collections.Generic.List<(string, long)>();
        foreach (var (k, v) in _bytes) all.Add((k, v));
        all.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        var parts = new System.Collections.Generic.List<string>();
        for (int i = 0; i < Math.Min(n, all.Count); i++) parts.Add($"{all[i].Item1} {all[i].Item2 / 1024.0 / Math.Max(1, frames):0.0}");
        return string.Join(", ", parts);
    }

    /// <summary>The heaviest, in milliseconds a frame over so many frames: "UnitView 12.3, GrassView 4.1 …".</summary>
    public static string Heaviest(int frames, int n = 10)
    {
        var all = new System.Collections.Generic.List<(string, double)>();
        foreach (var (k, v) in _ticks) all.Add((k, v * 1000.0 / System.Diagnostics.Stopwatch.Frequency / Math.Max(1, frames)));
        all.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        var parts = new System.Collections.Generic.List<string>();
        for (int i = 0; i < Math.Min(n, all.Count); i++) parts.Add($"{all[i].Item1} {all[i].Item2:0.00}");
        return string.Join(", ", parts);
    }
}
