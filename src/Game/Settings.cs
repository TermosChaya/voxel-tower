using System;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The player's own settings, kept between games in user://settings.cfg: how large the interface is drawn, over what
/// the window's size already gives it (Ctrl and + or − steps it, Ctrl+0 puts it back as it was), how loud the sounds
/// are, how hard a blast shakes the view, whether the game's time slows for a moment after a great one and whether the
/// voxels are drawn each with a thin line round it.
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
    /// <summary>Whether every voxel is drawn with a thin dark line round its edge, where it is big enough on screen to read.</summary>
    public static bool Lines { get; private set; }
    /// <summary>What the player chose for the lines, kept apart from a run that draws them for itself (--lines).</summary>
    static bool _keptLines;
    /// <summary>A scripted run (a test, a screenshot) neither reads the player's settings nor keeps its own.</summary>
    static bool _scripted;
    public static bool IsScripted => _scripted;
    /// <summary>The step of the grid the brushes snap to as the player last left it (0: off), for the next game.</summary>
    public static int Grid { get; private set; }

    /// <summary>Reads what the player set last time, if anything.</summary>
    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(File) != Error.Ok) return;
        UiScale = Snap(cfg.GetValue("interface", "scale", 1f).AsSingle());
        Volume = Math.Clamp(MathF.Round(cfg.GetValue("sound", "volume", .8f).AsSingle() * 10) / 10, 0, 1);
        Shake = Math.Clamp(cfg.GetValue("view", "shake", 1f).AsSingle(), 0, 1);
        SlowMo = cfg.GetValue("view", "slowmo", true).AsBool();
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
        cfg.SetValue("view", "lines", _keptLines);
        cfg.SetValue("tools", "grid", Grid);
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
