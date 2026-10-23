using System;
using System.Collections.Generic;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The player's own settings, kept between games in user://settings.cfg: how large the interface is drawn, over what
/// the window's size already gives it (Ctrl and + or − steps it, Ctrl+0 puts it back as it was), how loud the sounds
/// are, how hard a blast shakes the view, whether the game's time slows for a moment after a great one and whether the
/// voxels are drawn each with a thin line round it; and how much the picture asks of the machine (see <see cref="Graphics"/>).
/// </summary>
public static class Settings
{
    const string File = "user://settings.cfg";
    /// <summary>The sizes the interface can be drawn at, as shares of its usual size.</summary>
    public static readonly float[] Scales = { .7f, .8f, .9f, 1f, 1.1f };
    public static float UiScale { get; private set; } = 1;
    /// <summary>How loud the sounds are, from nothing to full, in tenths.</summary>
    public static float Volume { get; private set; } = .8f;
    /// <summary>How hard a blast or a great fall shakes the view: not at all, a little, fully.</summary>
    public static float Shake { get; private set; } = 1;
    public static readonly float[] Shakes = { 0, .5f, 1 };
    /// <summary>Whether the game's time slows for a moment after a great blast or fall near what the view looks at.</summary>
    public static bool SlowMo { get; private set; } = true;
    /// <summary>Whether the view goes along as the pointer is brought to an edge of the screen.</summary>
    public static bool EdgeScroll { get; private set; } = true;
    /// <summary>Whether the line over the console tells what the mouse and the keys do now.</summary>
    public static bool Hints { get; private set; } = true;
    /// <summary>Whether advice on what is amiss shows over the map, and a card the first time a tool or a tab is met.</summary>
    public static bool Advice { get; private set; } = true;
    /// <summary>The tools and tabs met already, that want no card again.</summary>
    static readonly HashSet<string> _met = new();
    /// <summary>Whether every voxel is drawn with a thin dark line round its edge, where it is big enough on screen to read.</summary>
    public static bool Lines { get; private set; }
    /// <summary>What the player chose for the lines, kept apart from a run that draws them for itself (--lines).</summary>
    static bool _keptLines;

    // ---------------------------------------------------------------- the picture

    /// <summary>The picture as a whole: low, medium, high, the highest — or the player's own mix (<see cref="Own"/>).</summary>
    public static int Quality { get; private set; } = 2;
    public const int Own = 4;
    public static readonly string[] Qualities = { "низкая", "средняя", "высокая", "наивысшая", "своя" };
    /// <summary>Shadows: none, plain, soft, softest; the edges smoothed: not at all, cheaply (FXAA), twice, four times over (MSAA);
    /// the share of the screen's pixels the world is drawn at; the grass: none of the blades, only close to, all of it;
    /// the corners darkened (ambient occlusion); the glow round what burns and shines; the frames a second shown.</summary>
    public static int Shadows { get; private set; } = 2;
    public static int Smoothing { get; private set; } = 2;
    public static int Detail { get; private set; } = 3;
    public static int Grass { get; private set; } = 2;
    public static bool Occlusion { get; private set; }
    public static bool Glow { get; private set; } = true;
    public static bool ShowFps { get; private set; }
    public static readonly float[] Details = { .5f, .67f, .8f, 1f };

    static readonly (int shadows, int smoothing, int detail, int grass, bool occlusion, bool glow)[] Presets =
    {
        (0, 1, 1, 0, false, false),
        (1, 1, 2, 1, false, true),
        (2, 2, 3, 2, false, true),
        (3, 3, 3, 2, true, true),
    };

    /// <summary>A step to a lower or higher picture as a whole: every part of it set to that one's.</summary>
    public static void StepQuality(int dir)
    {
        int q = Math.Clamp((Quality == Own ? 2 : Quality) + dir, 0, Presets.Length - 1);
        SetQuality(q);
        Save();
    }

    /// <summary>A scripted run with a picture of its own (--quality), nothing kept.</summary>
    public static void ScriptQuality(int q) => SetQuality(Math.Clamp(q, 0, Presets.Length - 1));

    static void SetQuality(int q)
    {
        Quality = q;
        (Shadows, Smoothing, Detail, Grass, Occlusion, Glow) = Presets[q];
    }

    /// <summary>One part of the picture changed by itself: the whole is the player's own from now on (unless it is one of them again).</summary>
    static void Mixed()
    {
        Quality = Own;
        for (int q = 0; q < Presets.Length; q++)
            if (Presets[q] == (Shadows, Smoothing, Detail, Grass, Occlusion, Glow)) Quality = q;
        Save();
    }

    public static void StepShadows(int dir) { Shadows = Math.Clamp(Shadows + dir, 0, 3); Mixed(); }
    public static void StepSmoothing(int dir) { Smoothing = Math.Clamp(Smoothing + dir, 0, 3); Mixed(); }
    public static void StepDetail(int dir) { Detail = Math.Clamp(Detail + dir, 0, Details.Length - 1); Mixed(); }
    public static void StepGrass(int dir) { Grass = Math.Clamp(Grass + dir, 0, 2); Mixed(); }
    public static void ToggleOcclusion() { Occlusion = !Occlusion; Mixed(); }
    public static void ToggleGlow() { Glow = !Glow; Mixed(); }
    public static void ToggleFps() { ShowFps = !ShowFps; Save(); }

    /// <summary>The first time the game runs: the picture by the machine — a graphics chip built into the processor gets the medium one.</summary>
    static void Guess()
    {
        string card = RenderingServer.GetVideoAdapterName().ToLowerInvariant();
        bool weak = card.Contains("intel") || card.Contains("uhd") || card.Contains("iris") || card.Contains("vega") && !card.Contains("rx")
            || card.Contains("mali") || card.Contains("adreno") || card.Contains("llvmpipe") || card.Contains("swiftshader");
        SetQuality(weak ? 1 : 2);
    }
    /// <summary>A scripted run (a test, a screenshot) neither reads the player's settings nor keeps its own.</summary>
    static bool _scripted;
    public static bool IsScripted => _scripted;
    /// <summary>The step of the grid the brushes snap to as the player last left it (0: off), for the next game.</summary>
    public static int Grid { get; private set; }

    /// <summary>Reads what the player set last time, if anything.</summary>
    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(File) != Error.Ok || !cfg.HasSection("graphics")) Guess();
        if (cfg.Load(File) != Error.Ok) return;
        if (cfg.HasSection("graphics"))
        {
            Quality = Math.Clamp(cfg.GetValue("graphics", "quality", 2).AsInt32(), 0, Own);
            Shadows = Math.Clamp(cfg.GetValue("graphics", "shadows", 2).AsInt32(), 0, 3);
            Smoothing = Math.Clamp(cfg.GetValue("graphics", "smoothing", 2).AsInt32(), 0, 3);
            Detail = Math.Clamp(cfg.GetValue("graphics", "detail", 3).AsInt32(), 0, Details.Length - 1);
            Grass = Math.Clamp(cfg.GetValue("graphics", "grass", 2).AsInt32(), 0, 2);
            Occlusion = cfg.GetValue("graphics", "occlusion", false).AsBool();
            Glow = cfg.GetValue("graphics", "glow", true).AsBool();
            ShowFps = cfg.GetValue("graphics", "fps", false).AsBool();
        }
        UiScale = Snap(cfg.GetValue("interface", "scale", 1f).AsSingle());
        Volume = Math.Clamp(MathF.Round(cfg.GetValue("sound", "volume", .8f).AsSingle() * 10) / 10, 0, 1);
        Shake = Math.Clamp(cfg.GetValue("view", "shake", 1f).AsSingle(), 0, 1);
        SlowMo = cfg.GetValue("view", "slowmo", true).AsBool();
        EdgeScroll = cfg.GetValue("view", "edge_scroll", true).AsBool();
        Hints = cfg.GetValue("interface", "hints", true).AsBool();
        Advice = cfg.GetValue("interface", "advice", true).AsBool();
        foreach (string k in cfg.GetValue("interface", "met", new string[0]).AsStringArray()) _met.Add(k);
        Lines = _keptLines = cfg.GetValue("view", "lines", false).AsBool();
        int grid = cfg.GetValue("tools", "grid", 0).AsInt32();
        Grid = Array.IndexOf(ToolOptions.Grids, grid) > 0 ? grid : 0;
        Bindings.Load(cfg);
    }

    static void Save()
    {
        if (_scripted) return;
        var cfg = new ConfigFile();
        // whatever else is kept there stays
        cfg.Load(File);
        cfg.SetValue("interface", "scale", UiScale);
        cfg.SetValue("sound", "volume", Volume);
        cfg.SetValue("view", "shake", Shake);
        cfg.SetValue("view", "slowmo", SlowMo);
        cfg.SetValue("view", "edge_scroll", EdgeScroll);
        cfg.SetValue("interface", "hints", Hints);
        cfg.SetValue("interface", "advice", Advice);
        var met = new string[_met.Count];
        _met.CopyTo(met);
        cfg.SetValue("interface", "met", met);
        cfg.SetValue("view", "lines", _keptLines);
        cfg.SetValue("tools", "grid", Grid);
        cfg.SetValue("graphics", "quality", Quality);
        cfg.SetValue("graphics", "shadows", Shadows);
        cfg.SetValue("graphics", "smoothing", Smoothing);
        cfg.SetValue("graphics", "detail", Detail);
        cfg.SetValue("graphics", "grass", Grass);
        cfg.SetValue("graphics", "occlusion", Occlusion);
        cfg.SetValue("graphics", "glow", Glow);
        cfg.SetValue("graphics", "fps", ShowFps);
        cfg.Save(File);
    }

    static float Snap(float s)
    {
        float best = 1;
        foreach (float v in Scales) if (Math.Abs(v - s) < Math.Abs(best - s)) best = v;
        return best;
    }

    /// <summary>A scripted run: the interface at this size (or the usual one), and nothing read or kept.</summary>
    public static void Scripted(float scale)
    {
        _scripted = true;
        UiScale = scale > 0 ? Snap(scale) : 1;
        // a scripted run makes its sounds, but nobody needs to hear them; and it keeps the view and the time steady
        Volume = 0;
        Shake = 0;
        SlowMo = false;
        EdgeScroll = false;
    }

    /// <summary>A scripted run that looks at the shaking and the slowing has them for itself, and nothing kept.</summary>
    public static void Feel(float shake, bool slowMo) { Shake = shake; SlowMo = slowMo; }

    /// <summary>A scripted run that is to be heard (the trailer) has its sound this loud, and nothing kept.</summary>
    public static void Hear(float volume) { Volume = Math.Clamp(volume, 0, 1); ApplyVolume(); }

    /// <summary>The grid's step as the player set it, kept for next time.</summary>
    public static void KeepGrid(int step)
    {
        if (Grid == step) return;
        Grid = step;
        Save();
    }

    /// <summary>A step more or less shaking; kept for next time.</summary>
    public static void StepShake(int dir)
    {
        int i = Array.IndexOf(Shakes, Shake);
        if (i < 0) i = Shakes.Length - 1;
        Shake = Shakes[Math.Clamp(i + dir, 0, Shakes.Length - 1)];
        Save();
    }

    /// <summary>The moment of slowed time after a great blast, on or off; kept for next time.</summary>
    public static void ToggleSlowMo()
    {
        SlowMo = !SlowMo;
        Save();
    }

    /// <summary>The view going along at the screen's edges, on or off; kept for next time.</summary>
    public static void ToggleEdgeScroll()
    {
        EdgeScroll = !EdgeScroll;
        Save();
    }

    /// <summary>The hints over the console, on or off; kept for next time.</summary>
    public static void ToggleHints()
    {
        Hints = !Hints;
        Save();
    }

    /// <summary>A tool or a tab met: true the first time, and kept so; a scripted run meets nothing.</summary>
    public static bool Meet(string key)
    {
        if (_scripted || !_met.Add(key)) return false;
        Save();
        return true;
    }

    /// <summary>The advice over the map, on or off; kept for next time.</summary>
    public static void ToggleAdvice()
    {
        Advice = !Advice;
        Save();
    }

    /// <summary>The lines round the voxels, on or off; kept for next time.</summary>
    public static void ToggleLines()
    {
        Lines = _keptLines = !Lines;
        ApplyLines();
        Save();
    }

    /// <summary>A scripted run that looks at the lines round the voxels draws them, or not, for itself.</summary>
    public static void ScriptLines(bool on) { Lines = on; ApplyLines(); }

    /// <summary>Tells every voxel the shader draws whether to draw its lines.</summary>
    public static void ApplyLines() => RenderingServer.GlobalShaderParameterSet("voxel_lines", Lines ? 1f : 0f);

    /// <summary>Sets how loud everything is heard.</summary>
    public static void ApplyVolume()
    {
        AudioServer.SetBusMute(0, Volume <= 0);
        AudioServer.SetBusVolumeDb(0, Volume <= 0 ? -80 : Mathf.LinearToDb(Volume));
    }

    /// <summary>A tenth louder or quieter; kept for next time.</summary>
    public static void StepVolume(int dir)
    {
        Volume = Math.Clamp(MathF.Round(Volume * 10 + dir) / 10, 0, 1);
        ApplyVolume();
        Save();
    }

    /// <summary>Sets the interface's size on the window: the beam, the console, the panels and the tooltips all follow.</summary>
    public static void Apply(Window root) => root.ContentScaleFactor = UiScale;

    /// <summary>One step larger or smaller, or back to the usual size with 0; kept for next time. Returns the new size.</summary>
    public static float Step(Window root, int dir)
    {
        int i = Array.IndexOf(Scales, UiScale);
        if (i < 0) i = Array.IndexOf(Scales, 1f);
        UiScale = dir == 0 ? 1 : Scales[Math.Clamp(i + dir, 0, Scales.Length - 1)];
        Apply(root);
        Save();
        return UiScale;
    }
}
