using System;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// The player's own settings, kept between games in user://settings.cfg: how large the interface is drawn, over what
/// the window's size already gives it (Ctrl and + or − steps it, Ctrl+0 puts it back as it was), and how loud the
/// sounds are.
/// </summary>
public static class Settings
{
    const string File = "user://settings.cfg";
    /// <summary>The sizes the interface can be drawn at, as shares of its usual size.</summary>
    public static readonly float[] Scales = { .7f, .8f, .9f, 1f, 1.1f };
    public static float UiScale { get; private set; } = 1;
    /// <summary>How loud the sounds are, from nothing to full, in tenths.</summary>
    public static float Volume { get; private set; } = .8f;
    /// <summary>A scripted run (a test, a screenshot) neither reads the player's settings nor keeps its own.</summary>
    static bool _scripted;

    /// <summary>Reads what the player set last time, if anything.</summary>
    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(File) != Error.Ok) return;
        UiScale = Snap(cfg.GetValue("interface", "scale", 1f).AsSingle());
        Volume = Math.Clamp(MathF.Round(cfg.GetValue("sound", "volume", .8f).AsSingle() * 10) / 10, 0, 1);
    }

    static void Save()
    {
        if (_scripted) return;
        var cfg = new ConfigFile();
        // whatever else is kept there stays
        cfg.Load(File);
        cfg.SetValue("interface", "scale", UiScale);
        cfg.SetValue("sound", "volume", Volume);
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
        // a scripted run makes its sounds, but nobody needs to hear them
        Volume = 0;
    }

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
