using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// What the game tells about itself when something goes wrong, for the player to send to the author.
///
/// A watchdog thread looks at the frames. When none has come for four seconds the game is taken to have hung, and
/// what it was busy with (the part of the frame, the simulation's stage and the unit in hand), the state of the siege
/// and the last of what happened are written down at once to user://logs/hangs.txt, while the game is still stuck;
/// the record grows if it stays stuck, and says so if it comes back. A mark of the session says at the next start
/// whether the last run ended as it should; if not, the title offers a report. The report is one text file on the
/// desktop: the build, the system and the video card, the settings, the hangs, the last events and the logs of the
/// last runs. Scripted runs (the self-test, the screenshots) are not watched and leave no mark.
/// </summary>
public static class Diag
{
    /// <summary>The part of the frame at work now, as Prof's scopes name it.</summary>
    public static volatile string Section = "";

    const double Stall = 4;
    const int NotesKept = 80;

    static long _frames, _beatTicks;
    static volatile bool _watching, _focused = true;
    static string _dir;
    static Sim _sim;
    static bool _started;
    static readonly Queue<string> _notes = new();

    /// <summary>The run before this one ended without closing: it hung, crashed or was killed.</summary>
    public static bool LastEndedBadly { get; private set; }

    static string Dir => _dir ??= ProjectSettings.GlobalizePath("user://logs");

    /// <summary>A game begins (and again after each new game or load): watched from here on, unless scripted.</summary>
    public static void Start(Sim sim, bool scripted)
    {
        _sim = sim;
        Beat(true);
        if (scripted) return;
        Directory.CreateDirectory(Dir);
        string mark = Path.Combine(Dir, "session.txt");
        if (!_started)
        {
            _started = true;
            LastEndedBadly = File.Exists(mark) && File.ReadAllText(mark).StartsWith("running");
            new Thread(Watch) { IsBackground = true, Name = "watchdog" }.Start();
        }
        File.WriteAllText(mark, $"running {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        _watching = true;
    }

    /// <summary>The scene is let go (a new game, a load, or the game shut as it should): nothing to watch till the next.</summary>
    public static void Stop()
    {
        _watching = false;
        if (!_started) return;
        try { File.WriteAllText(Path.Combine(Dir, "session.txt"), $"closed {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n"); } catch (IOException) { }
    }

    /// <summary>A frame has come: called at the head of every frame.</summary>
    public static void Beat(bool focused)
    {
        Interlocked.Increment(ref _frames);
        Interlocked.Exchange(ref _beatTicks, Stopwatch.GetTimestamp());
        _focused = focused;
    }

    /// <summary>Something that happened, for the hang records and the report: the chronicle's lines and the player's doings.</summary>
    public static void Note(string text)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}{(_sim != null ? $", {_sim.Time:0} с игры" : "")}] {text}";
        lock (_notes)
        {
            _notes.Enqueue(line);
            while (_notes.Count > NotesKept) _notes.Dequeue();
        }
    }

    static string Notes(int last)
    {
        lock (_notes)
        {
            var all = new List<string>(_notes);
            return string.Join("\n", all.GetRange(Math.Max(0, all.Count - last), Math.Min(last, all.Count)));
        }
    }

    // ---------------------------------------------------------------- the watchdog

    static void Watch()
    {
        long stuckAt = -1;
        double told = 0;
        while (true)
        {
            Thread.Sleep(500);
            if (!_watching) { stuckAt = -1; continue; }
            long frames = Interlocked.Read(ref _frames);
            double since = (Stopwatch.GetTimestamp() - Interlocked.Read(ref _beatTicks)) / (double)Stopwatch.Frequency;
            if (since < Stall)
            {
                if (stuckAt >= 0 && frames != stuckAt) Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Игра снова отвечает: зависание длилось около {told:0} с.\n");
                stuckAt = -1;
                continue;
            }
            if (stuckAt == frames)
            {
                // still stuck: a line every half a minute, with where it is now (a slow loop moves, a dead one does not)
                if (since - told >= 30) { told = since; Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Всё ещё не отвечает: {since:0} с. {Where()}\n"); }
                continue;
            }
            stuckAt = frames;
            told = since;
            Write(Record(since, frames));
        }
    }

    static string Where()
    {
        string unit = "";
        int id = Sim.StageUnit;
        if (id > 0 && _sim != null)
            try { var u = _sim.Units.Find(x => x.Id == id); if (u != null) unit = $", боец №{id} ({u.T.Name}, {u.State}, клетка {u.Cell})"; }
            catch (Exception) { unit = $", боец №{id}"; }
        return $"Часть кадра: {(Section == "" ? "вне кода игры (отрисовка или драйвер)" : Section)}; шаг симуляции: {Sim.Stage}{unit}.";
    }

    static string Record(double since, long frames)
    {
        var s = new StringBuilder();
        s.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Игра не отвечает {since:0} с (кадр {frames}).");
        s.AppendLine("  " + Where());
        try
        {
            if (_sim is { } sim)
                s.AppendLine($"  Игра: {sim.Time:0} с, набег {sim.Raids.Count} ({(sim.Raids.Active ? "идёт" : "ещё нет")}), день {sim.Clock.Day}, угроза {sim.PlannedThreat:0.##}, набеги {sim.Source.Key}, всех тел {sim.Units.Count}, ватаг {sim.Bands.Count}" +
                    $"{(sim.Sandbox ? ", песочница" : sim.Waves.OnCall ? ", песочница своими силами" : "")}{(sim.Over ? ", осада окончена" : "")}.");
        }
        catch (Exception) { }
        if (!_focused) s.AppendLine("  Окно было не в фокусе: возможно, система просто притормозила игру в фоне.");
        s.AppendLine("  Последние события:");
        foreach (var line in Notes(25).Split('\n')) s.AppendLine("    " + line);
        return s.ToString();
    }

    static void Write(string text)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Path.Combine(Dir, "hangs.txt"), text);
        }
        catch (Exception) { }
    }

    // ---------------------------------------------------------------- the report

    /// <summary>The report written to the desktop (or the game's own folder, or the folder given): where it went.</summary>
    public static string Report(Main m, string to = null)
    {
        var s = new StringBuilder();
        s.AppendLine("Voxel Tower — отчёт об ошибке");
        s.AppendLine($"Создан: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        s.AppendLine($"Сборка: {Build()}");
        s.AppendLine($"Система: {OS.GetName()} {OS.GetVersion()}, {OS.GetProcessorName()} × {OS.GetProcessorCount()}");
        try
        {
            using var mem = OS.GetMemoryInfo();
            s.AppendLine($"Память: {Gb(mem["physical"])} ГБ, свободно {Gb(mem["available"])} ГБ");
        }
        catch (Exception) { }
        s.AppendLine($"Видеокарта: {RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetVideoAdapterVendor()}), API {RenderingServer.GetVideoAdapterApiVersion()}, " +
            $"драйвер: {string.Join(" ", OS.GetVideoAdapterDriverInfo())}");
        s.AppendLine($"Отрисовка: {RenderingServer.GetCurrentRenderingMethod()}, {RenderingServer.GetCurrentRenderingDriverName()}");
        s.AppendLine($"Экран: {DisplayServer.ScreenGetSize()} (масштаб {DisplayServer.ScreenGetScale()}), окно {DisplayServer.WindowGetSize()}, {DisplayServer.WindowGetMode()}");
        s.AppendLine($"Язык системы: {OS.GetLocale()}; кадров в секунду: {Engine.GetFramesPerSecond()}");
        if (m?.Sim is { } sim)
            s.AppendLine($"Игра: {sim.Time:0} с, набег {sim.Raids.Count}, отбито {sim.Raids.Repelled}, день {sim.Clock.Day}, набеги {sim.Source.Key}, сложность {sim.Hardness}, всех тел {sim.Units.Count}, ватаг {sim.Bands.Count}" +
                $"{(sim.Sandbox ? ", песочница" : sim.Waves.OnCall ? ", песочница своими силами" : "")}{(m.Tutor != null ? ", обучение" : "")}");
        s.AppendLine($"Прошлый запуск: {(LastEndedBadly ? "закрылся аварийно (завис, упал или был снят)" : "закрылся как обычно")}");

        Part(s, "Зависания", FileTail(Path.Combine(Dir, "hangs.txt"), 60_000) ?? "не было");
        Part(s, "Последние события", Notes(NotesKept));
        Part(s, "Настройки", FileTail(ProjectSettings.GlobalizePath("user://settings.cfg"), 20_000) ?? "нет");
        // the logs of the last runs, newest first: the one before this is the run that went wrong
        var logs = new List<FileInfo>();
        try { logs.AddRange(new DirectoryInfo(Dir).GetFiles("*.log")); } catch (Exception) { }
        logs.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
        for (int i = 0; i < Math.Min(4, logs.Count); i++)
            Part(s, $"Лог {logs[i].Name} ({logs[i].LastWriteTime:yyyy-MM-dd HH:mm:ss})", FileTail(logs[i].FullName, 300_000) ?? "не читается");

        string folder = to ?? OS.GetSystemDir(OS.SystemDir.Desktop);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) folder = Dir;
        string path = Path.Combine(folder, $"VoxelTower-отчёт-{DateTime.Now:yyyy-MM-dd-HHmm}.txt");
        File.WriteAllText(path, s.ToString(), new UTF8Encoding(true));
        return path;
    }

    static void Part(StringBuilder s, string title, string body)
    {
        s.AppendLine();
        s.AppendLine($"===== {title} =====");
        s.AppendLine(body.TrimEnd());
    }

    /// <summary>The end of a file, at most so many bytes of it; null if there is none.</summary>
    static string FileTail(string path, int most)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var f = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long from = Math.Max(0, f.Length - most);
            f.Seek(from, SeekOrigin.Begin);
            using var r = new StreamReader(f, Encoding.UTF8);
            return (from > 0 ? "…\n" : "") + r.ReadToEnd();
        }
        catch (Exception e) { return $"не читается: {e.Message}"; }
    }

    static string Gb(Variant v) => (v.AsInt64() / 1024.0 / 1024 / 1024).ToString("0.0");

    /// <summary>The build: its version with the commit it was made from, and when the game's code was built.</summary>
    static string Build()
    {
        var a = typeof(Diag).Assembly;
        string version = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        string when = "";
        try { if (a.Location != "") when = $", собрана {File.GetLastWriteTime(a.Location):yyyy-MM-dd HH:mm}"; } catch (Exception) { }
        return version + when;
    }
}
