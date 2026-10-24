using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The frames that come late (--spikes, and counted in --perf): at the head of every frame the one before is measured
/// from head to head, and if it took over 25 ms a line is written of what it went on: the views' parts of it (Prof),
/// the simulation's stages, the garbage collected and how long that stopped the game, the drawing on the processor, and
/// what was done in it by count (chunks meshed, grass made…). What none of these account for is the rest: the engine's
/// own work, a shader made for the first time, the system.
/// </summary>
public static class Hitches
{
    public static bool On;
    /// <summary>Every late frame written out, not only counted.</summary>
    public static bool Tell;
    const double Late = 25;

    static long _head;
    static int _gen0, _gen1, _gen2;
    static TimeSpan _paused;
    static long _alloc;
    static Rid _vp;
    /// <summary>Since <see cref="Reset"/>: the frames, those over 25 and over 50 ms, the slowest, and the bytes allocated.</summary>
    public static int Frames, Over25, Over50;
    public static double Worst;
    public static long Allocated;
    static readonly List<(string, long)> _parts = new();
    static readonly StringBuilder _line = new();

    public static void Start(Viewport vp)
    {
        On = true;
        Prof.On = true;
        Sim.StageTicks ??= new Dictionary<string, long>();
        _vp = vp.GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_vp, true);
        Reset();
    }

    /// <summary>Counting begins again from this frame.</summary>
    public static void Reset()
    {
        Frames = Over25 = Over50 = 0;
        Worst = 0;
        Allocated = 0;
        _head = 0;
    }

    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>At the head of a frame: the one before measured, written out if it was late, and its records cleared.</summary>
    public static void Frame(Sim sim)
    {
        double gameTime = sim?.Time ?? 0;
        if (!On) return;
        long now = Stopwatch.GetTimestamp(), alloc = GC.GetTotalAllocatedBytes();
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var paused = GC.GetTotalPauseDuration();
        if (_head != 0)
        {
            double took = Ms(now - _head);
            Frames++;
            Allocated += alloc - _alloc;
            Worst = Math.Max(Worst, took);
            if (took > Late) Over25++;
            if (took > 50) Over50++;
            if (took > Late && Tell) Write(sim, took, gameTime, alloc - _alloc, g0 - _gen0, g1 - _gen1, g2 - _gen2, (paused - _paused).TotalMilliseconds);
        }
        _head = now;
        _alloc = alloc;
        _gen0 = g0; _gen1 = g1; _gen2 = g2;
        _paused = paused;
        Prof.Frame.Clear();
        Prof.Counts.Clear();
        Sim.StageTicks?.Clear();
        Sim.SlowestUnit = 0;
        Sim.SlowestUnitTicks = 0;
    }

    static void Write(Sim sim, double took, double gameTime, long bytes, int g0, int g1, int g2, double pausedMs)
    {
        _line.Clear();
        _line.Append($"[spike] {took:0} ms at {gameTime:0.0} s of the game:");
        double accounted = 0;
        _parts.Clear();
        foreach (var (k, v) in Prof.Frame) _parts.Add((k, v));
        _parts.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        foreach (var (k, v) in _parts)
        {
            double ms = Ms(v);
            accounted += ms;
            if (ms < .5) break;
            _line.Append($" {k} {ms:0.0}");
            if (k == "Sim" && Sim.StageTicks is { Count: > 0 } st)
            {
                var stages = new List<(string, long)>();
                foreach (var (sk, sv) in st) stages.Add((sk, sv));
                stages.Sort((a, b) => b.Item2.CompareTo(a.Item2));
                _line.Append(" (");
                for (int i = 0; i < Math.Min(4, stages.Count); i++) _line.Append($"{(i > 0 ? ", " : "")}{stages[i].Item1} {Ms(stages[i].Item2):0.0}");
                if (Sim.SlowestUnit != 0 && Ms(Sim.SlowestUnitTicks) >= 2 && sim?.Units.Find(u => u.Id == Sim.SlowestUnit) is { } slow)
                    _line.Append($"; the slowest {slow.T.Key} #{slow.Id} {Ms(Sim.SlowestUnitTicks):0.0} ({slow.State}{(string.IsNullOrEmpty(slow.Why) ? "" : ", " + slow.Why)})");
                _line.Append(')');
            }
            _line.Append(',');
        }
        double drawing = RenderingServer.ViewportGetMeasuredRenderTimeCpu(_vp) + RenderingServer.GetFrameSetupTimeCpu();
        _line.Append($" drawing {drawing:0.0}, the rest {Math.Max(0, took - accounted - drawing):0.0}");
        if (g0 + g1 + g2 > 0) _line.Append($"; garbage collected {g0}/{g1}/{g2} (generations 0/1/2), the game stopped {pausedMs:0.0} ms");
        _line.Append($"; {bytes / 1024} KB allocated");
        if (Prof.Counts.Count > 0)
        {
            _line.Append(';');
            foreach (var (k, v) in Prof.Counts) _line.Append($" {k} {v}");
        }
        GD.Print(_line.ToString());
    }
}
