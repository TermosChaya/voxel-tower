using System;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The player's own settings, kept between games in user://settings.cfg. For now only how large the interface is
/// drawn, over what the window's size already gives it: Ctrl and + or − steps it, Ctrl+0 puts it back as it was.
/// </summary>
public static class Settings
{
    const string File = "user://settings.cfg";
    /// <summary>The sizes the interface can be drawn at, as shares of its usual size.</summary>
    public static readonly float[] Scales = { .7f, .8f, .9f, 1f, 1.1f };
    public static float UiScale { get; private set; } = 1;
    /// <summary>A scripted run (a test, a screenshot) neither reads the player's settings nor keeps its own.</summary>
    static bool _scripted;

    /// <summary>Reads what the player set last time, if anything.</summary>
    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(File) != Error.Ok) return;
        UiScale = Snap(cfg.GetValue("interface", "scale", 1f).AsSingle());
    }

    static void Save()
    {
        if (_scripted) return;
        var cfg = new ConfigFile();
        // whatever else is kept there stays
        cfg.Load(File);
        cfg.SetValue("interface", "scale", UiScale);
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
