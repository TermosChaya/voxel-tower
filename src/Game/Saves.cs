using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Saved games on disk, in user://saves: three slots of the player's own, a quick save (F5, read back with F9) and the
/// autosave written each time a wave is beaten back. A save is the sim's own (<see cref="Sim.Save"/>) with what the
/// screen keeps put in beside it: where the camera looks, the speed, the squads and the chronicle. Reading one back
/// starts the scene again with it waiting (<see cref="Main.PendingLoad"/>): the valley is made again from its seed and
/// what play changed of it laid over.
/// </summary>
public static class Saves
{
    public const string Quick = "quick", Auto = "auto";
    public static readonly string[] Slots = { "slot1", "slot2", "slot3" };

    public static string PathOf(string slot) => ProjectSettings.GlobalizePath($"user://saves/{slot}.sav");

    public static string NameOf(string slot) => slot switch
    {
        Quick => "Быстрое сохранение",
        Auto => "Автосохранение",
        _ => Array.IndexOf(Slots, slot) is int i and >= 0 ? "Ячейка " + (i + 1) : slot,
    };

    /// <summary>What a slot holds, for the list: its label and when it was written, or null when it is empty or unreadable.</summary>
    public static (string label, double time, DateTime when)? Peek(string slot)
    {
        string path = PathOf(slot);
        if (!File.Exists(path)) return null;
        try
        {
            using var f = File.OpenRead(path);
            using var r = new SaveReader(f);
            return (r.Label, r.Time, File.GetLastWriteTime(path));
        }
        catch { return null; }
    }

    /// <summary>A slot that holds a file the game cannot read: written by an earlier version of it, whose saves are not read.</summary>
    public static bool Stale(string slot) => File.Exists(PathOf(slot)) && Peek(slot) == null;

    /// <summary>What the screen keeps beside the sim: the camera, the speed, the squads and the chronicle.</summary>
    sealed class Screen
    {
        public float[] Cam { get; set; }
        public int Speed { get; set; }
        public List<List<int>> Squads { get; set; }
        public List<Line> Log { get; set; }
    }

    sealed class Line
    {
        public string T { get; set; }
        public int K { get; set; }
        public float? X { get; set; }
        public float? Z { get; set; }
    }

    /// <summary>Writes the game in play to a slot; false (and a word why in the log) if it could not be written.</summary>
    public static bool Write(Main m, string slot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOf(slot)));
            var aim = m.Rig.Aim;
            var screen = new Screen
            {
                Cam = new[] { aim.x, aim.z, aim.yaw, aim.pitch, aim.zoom },
                Speed = m.Speed,
                Squads = new List<List<int>>(),
                Log = new List<Line>(),
            };
            foreach (var sq in m.Command.Squads) screen.Squads.Add(sq.FindAll(u => !u.Dead).ConvertAll(u => u.Id));
            foreach (var (text, kind, x, z) in m.Hud.Lines)
                screen.Log.Add(new Line { T = text, K = (int)kind, X = float.IsNaN(x) ? null : x, Z = float.IsNaN(z) ? null : z });
            var sim = m.Sim;
            string label = sim.Waves.Active ? $"Волна {sim.Waves.Number} в долине" : $"Перед волной {sim.Waves.Number + 1}";
            string tmp = PathOf(slot) + ".part";
            using (var f = File.Create(tmp)) sim.Save(f, m.Seed, label, JsonSerializer.Serialize(screen));
            File.Move(tmp, PathOf(slot), overwrite: true);
            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[voxel-tower] could not save to {slot}: {e.Message}");
            m.Hud.Log("Не удалось сохранить: " + e.Message, Hud.Kind.Bad);
            return false;
        }
    }

    /// <summary>Starts the scene again with a slot waiting to be read back.</summary>
    public static void Read(Main m, string slot)
    {
        if (!File.Exists(PathOf(slot))) { m.Hud.Float("здесь ничего не сохранено", Ornament.Blood); return; }
        Main.PendingLoad = PathOf(slot);
        m.GetTree().ReloadCurrentScene();
    }

    /// <summary>Puts back what the screen kept: the chronicle, the camera, the speed and the squads.</summary>
    public static void Apply(Main m, string extra)
    {
        if (string.IsNullOrEmpty(extra)) return;
        Screen screen;
        try { screen = JsonSerializer.Deserialize<Screen>(extra); }
        catch { return; }
        if (screen == null) return;
        if (screen.Log != null)
            for (int i = screen.Log.Count - 1; i >= 0; i--)
            {
                var l = screen.Log[i];
                m.Hud.Log(l.T, (Hud.Kind)Math.Clamp(l.K, 0, 3), l.X ?? float.NaN, l.Z ?? float.NaN, toast: false);
            }
        if (screen.Cam is { Length: 5 } c) m.Rig.Set(c[0], c[1], c[2], c[3], c[4], snap: true);
        if (screen.Speed is >= 1 and <= 3) m.Speed = screen.Speed;
        if (screen.Squads != null)
            for (int i = 0; i < screen.Squads.Count && i < m.Command.Squads.Length; i++)
            {
                m.Command.Squads[i].Clear();
                foreach (int id in screen.Squads[i])
                {
                    var u = m.Sim.Units.Find(o => o.Id == id && !o.Dead);
                    if (u != null) m.Command.Squads[i].Add(u);
                }
            }
    }
}
