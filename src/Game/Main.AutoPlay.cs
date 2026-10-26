using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The big map playing by itself for the eye (--campaign --pose=autoplay --size=S|M [--days=60] [--film=folder]): a
    /// new campaign, our side played by the <see cref="AutoPlayer"/> as the bench's harness plays it (no valley under the
    /// map: the first valley lives by its summary), the map's clock run fast, the eye following our first hero; a picture
    /// at each moment worth seeing (the start, the first mine flagged, the first fight, an army of the horde near the first
    /// valley, the middle of the game from high, the end) and a frame every few for a film, put together by ffmpeg at the
    /// end. Never in play.
    /// </summary>
    AutoPlayer _auto;
    string _autoDir;
    int _autoDays = 60, _autoFrame, _autoFilm, _autoHold, _autoShot;
    string _autoPending;
    bool _autoMine, _autoFight, _autoNear, _autoMid, _autoEnd;
    readonly Stopwatch _autoWatch = new();

    /// <summary>Seconds of the map's clock a second of the eye's: a day of the map in some five seconds.</summary>
    const double AutoRate = 150;
    /// <summary>The film's frames at most, one each third frame drawn.</summary>
    const int AutoFilmMost = 1400, AutoFilmEvery = 3;

    void AutoPlayStart()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--days=") && int.TryParse(a["--days=".Length..], out int d)) _autoDays = d;
            if (a.StartsWith("--film=")) _autoDir = a["--film=".Length..];
        }
        _autoDir ??= ProjectSettings.GlobalizePath("user://autoplay");
        Directory.CreateDirectory(Path.Combine(_autoDir, "film"));
        foreach (var f in Directory.GetFiles(Path.Combine(_autoDir, "film"))) File.Delete(f);
        // the map on its own, no valley under it: the first valley by its summary, as in the bench
        Atlas.InValley = false; Atlas.Live = null;
        _auto = new AutoPlayer(Atlas);
        _auto.Begin();
        Sim.Waves.Countdown = 1e9;
        OpenAtlas();
        _autoWatch.Start();
        GD.Print($"[autoplay] seed {Atlas.Seed}, {Campaign.SizeName(Atlas.Width)}, {_autoDays} days → {_autoDir}");
    }

    /// <summary>A frame of the map playing by itself; true while it does (the valley's own frame is not run then).</summary>
    bool AutoPlayFrame(double delta)
    {
        if (_auto == null) return false;
        _autoFrame++;
        var v = WorldMap;
        // (the land of the map made about the eye first)
        if (_autoFrame < 120 || v == null) return true;
        if (_autoFrame == 120) Pending("01-start", Hero(), 40);
        if (_autoHold > 0)
        {
            // a picture waiting for the land about the eye to be made
            if (--_autoHold == 0 || v.PiecesMaking == 0 && _autoHold < 50) { _autoHold = 0; Picture(_autoPending); _autoPending = null; }
            return true;
        }
        double run = Math.Min(delta, .1) * AutoRate;
        while (run > 1e-6 && !Atlas.Won && !Atlas.Defeated)
        {
            double dt = Math.Min(5, run);
            Atlas.Advance(dt);
            _auto.Tick();
            run -= dt;
        }
        foreach (var n in Atlas.News) Hud.Log(n.Text, n.Kind is NewsKind.Lost or NewsKind.RaidComing ? Hud.Kind.Bad : n.Kind == NewsKind.Fought ? Hud.Kind.Warn : Hud.Kind.Ok, toast: n.Kind != NewsKind.Grew);
        Atlas.News.Clear();
        // the moments worth a picture
        var hero = Hero();
        if (!_autoMine && Atlas.Cells.Find(x => x.Thing is { Kind: ThingKind.Mine, Owner: Holder.Ours } && hero != null && x.Q == hero.Q && x.R == hero.R) is { } mine)
        { _autoMine = true; Pending("02-mine", mine, 34); return true; }
        if (!_autoFight && _auto.Fights.Count > 0)
        {
            _autoFight = true;
            var (fd, where, _, _) = _auto.Fights[0];
            var parts = where.Split(',');
            var fc = Atlas.Cell(int.Parse(parts[0]), int.Parse(parts[1]));
            Pending("03-fight", fc, 34); return true;
        }
        if (!_autoNear && Atlas.Home is { } home && Atlas.Parties.Find(e => e.Owner == Holder.Horde && e.GoalQ == home.Q && e.GoalR == home.R && e.Moving && e.Path.Count <= 6) is { } army)
        {
            _autoNear = true;
            var at = Atlas.CellOf(army);
            Pending("04-horde-near", Atlas.Cell((at.Col + home.Col) / 2, (at.Row + home.Row) / 2), 60); return true;
        }
        if (!_autoMid && Atlas.Clock.Day >= _autoDays / 2) { _autoMid = true; Pending("05-middle", Atlas.Cell(Atlas.Width / 2, Atlas.Height / 2), 999); return true; }
        if (Atlas.Clock.Day > _autoDays || Atlas.Won || Atlas.Defeated)
        {
            if (!_autoEnd) { _autoEnd = true; Pending("06-end", Atlas.Cell(Atlas.Width / 2, Atlas.Height / 2), 999); return true; }
            AutoPlayDone();
            return true;
        }
        // the eye after our first hero, a frame for the film every few
        if (hero != null) v.FocusHex(hero.Col, hero.Row);
        if (_autoFilm < AutoFilmMost && _autoFrame % AutoFilmEvery == 0) Film();
        return true;
    }

    MapParty Hero() => Atlas.Parties.Find(p => p.Owner == Holder.Ours && p.Party.Commander?.Hero != null);

    void Pending(string name, MapParty p, float dist) => Pending(name, p == null ? Atlas.Home : Atlas.CellOf(p), dist);

    /// <summary>A picture to take: the eye over a hex at a distance, the clock held till the land about it is made.</summary>
    void Pending(string name, MapCell at, float dist)
    {
        if (at == null || WorldMap?.Layout == null) return;
        var (x, z) = WorldMap.Layout.Center(at.Col, at.Row);
        WorldMap.Snap(x, z + 12, dist);
        _autoPending = name; _autoHold = 240;
    }

    void Picture(string name)
    {
        if (name == null) return;
        string file = Path.Combine(_autoDir, $"{name}.png");
        var img = GetViewport().GetTexture().GetImage();
        if (img.GetWidth() > 1600) img.Resize(1600, img.GetHeight() * 1600 / img.GetWidth(), Image.Interpolation.Lanczos);
        img.SavePng(file);
        _autoShot++;
        GD.Print($"[autoplay] day {Atlas.Clock.Day}: {file} ({Engine.GetFramesPerSecond()} fps)");
    }

    void Film()
    {
        var img = GetViewport().GetTexture().GetImage();
        img.Resize(960, img.GetHeight() * 960 / img.GetWidth(), Image.Interpolation.Bilinear);
        img.SaveJpg(Path.Combine(_autoDir, "film", $"f{_autoFilm++:00000}.jpg"), .82f);
    }

    /// <summary>The end: the film put together, what happened told, the game left.</summary>
    void AutoPlayDone()
    {
        string end = Atlas.Won ? $"победа на {Atlas.Clock.Day}-й день" : Atlas.Defeated ? $"поражение на {Atlas.Clock.Day}-й день" : $"{_autoDays} дней прошло";
        GD.Print($"[autoplay] {end}; {_autoShot} pictures, {_autoFilm} frames of film in {_autoWatch.Elapsed.TotalSeconds:0} s");
        File.WriteAllLines(Path.Combine(_autoDir, "said.txt"), _auto.Said);
        string ff = File.Exists("/opt/homebrew/bin/ffmpeg") ? "/opt/homebrew/bin/ffmpeg" : "ffmpeg";
        try
        {
            var p = Process.Start(new ProcessStartInfo(ff, $"-y -framerate 30 -i \"{Path.Combine(_autoDir, "film", "f%05d.jpg")}\" -vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" -c:v libx264 -pix_fmt yuv420p \"{Path.Combine(_autoDir, "map3d.mp4")}\"")
                { RedirectStandardError = true, RedirectStandardOutput = true });
            p.StandardError.ReadToEnd(); p.WaitForExit();
            GD.Print($"[autoplay] film {(p.ExitCode == 0 ? Path.Combine(_autoDir, "map3d.mp4") : "failed")}");
        }
        catch (Exception e) { GD.Print($"[autoplay] no film: {e.Message}"); }
        _auto = null;
        GetTree().Quit();
    }
}
