using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// A word called out over the one who called it, rising a little and fading in two seconds: a feller's «Берегись!» before
/// a tree goes over, a digger's before what is built comes down (Sim.Felling.cs, Sim.Undermine.cs), and what is said
/// aloud about the valley (Sim.Voices.cs): greetings and talk in small pale letters, and only near the middle of the
/// view, so that the valley is not covered in them; a call at work as a warning is; a commander's praise and the cheer
/// after it in gold; the horde's chant and roar in red. None from someone's eyes, nor over the menu.
/// </summary>
public partial class ShoutTags : CanvasLayer
{
    /// <summary>Seconds a word stays over the one who called it.</summary>
    const double Lasts = 2;
    Main _m;
    Control _root;
    readonly List<(Unit u, Label tag, double at, double lasts, bool near)> _tags = new();
    // (called out during the sim's step: the tags are made in the frame after); the voice, or −1 for a warning
    readonly List<(Unit u, string word, int voice)> _called = new();
    double _t;

    /// <summary>The words over ours now, for the self-test.</summary>
    public IEnumerable<string> Showing { get { foreach (var t in _tags) if (t.tag.Visible) yield return t.tag.Text; } }

    public void Init(Main m)
    {
        _m = m;
        Layer = 2;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        m.Sim.Shouted += (u, word) => _called.Add((u, word, -1));
        m.Sim.Spoke += (u, word, v) => _called.Add((u, word, (int)v));
    }

    /// <summary>How a word is shown by what kind of thing it is: its letters, size, colour, how long it stays, and whether only near the middle of the view.</summary>
    static (Font font, int size, Color color, double lasts, bool near) Style(int voice) => voice switch
    {
        (int)Voice.Greet or (int)Voice.Chat => (Ornament.Serif, 15, new Color("f2e8d2"), 1.8, true),
        (int)Voice.Laugh => (Ornament.SerifItalic, 15, new Color("e9dbb6"), 1.4, true),
        (int)Voice.Praise or (int)Voice.Cheer => (Ornament.SerifBold, 18, new Color("ffe27a"), 2, false),
        (int)Voice.Chant or (int)Voice.Roar => (Ornament.SerifBold, 18, new Color("ff9070"), 1.6, false),
        _ => (Ornament.SerifBold, 18, new Color("ffd890"), Lasts, false),
    };

    /// <summary>Whether a spot of the valley is near the middle of the view (some forty metres, nearer as one zooms in).</summary>
    bool NearView(Unit u)
    {
        var w = _m.World;
        var c = _m.Rig.Target;
        float dx = w.WX(u.X) - c.X, dz = w.WZ(u.Z) - c.Y;
        float reach = 40 / MathF.Sqrt(Math.Max(.4f, _m.Rig.Aim.zoom));
        return dx * dx + dz * dz < reach * reach;
    }

    public override void _Process(double delta)
    {
        // (a word called out stays while the game stands still)
        if (!_m.Paused) _t += delta;
        foreach (var (u, word, voice) in _called)
        {
            var (font, size, color, lasts, near) = Style(voice);
            if (near && !NearView(u)) continue;
            // one word at a time over each
            for (int i = _tags.Count - 1; i >= 0; i--) if (_tags[i].u == u) { _tags[i].tag.QueueFree(); _tags.RemoveAt(i); }
            var tag = Hud.Text(word, font, size, color, outline: near ? 6 : 7);
            tag.MouseFilter = Control.MouseFilterEnum.Ignore;
            tag.Visible = false;
            _root.AddChild(tag);
            _tags.Add((u, tag, _t, lasts, near));
        }
        _called.Clear();
        var cam = GetViewport().GetCamera3D();
        bool hidden = cam == null || _m.FP.On || _m.Hud.MenuOpen || _m.Hud.TitleOpen;
        for (int i = _tags.Count - 1; i >= 0; i--)
        {
            var (u, tag, at, lasts, _) = _tags[i];
            double age = _t - at;
            if (age > lasts || u.Dead || !_m.Sim.Units.Contains(u)) { tag.QueueFree(); _tags.RemoveAt(i); continue; }
            var pos = new Vector3(_m.World.WX(u.X), _m.World.WY(u.Y + u.T.BodyH + 6 + (float)age * 5), _m.World.WZ(u.Z));
            tag.Visible = !hidden && !cam.IsPositionBehind(pos);
            if (!tag.Visible) continue;
            tag.Modulate = new Color(1, 1, 1, (float)Math.Clamp((lasts - age) / .6, 0, 1));
            tag.ResetSize();
            tag.Position = cam.UnprojectPosition(pos) - new Vector2(tag.Size.X / 2, tag.Size.Y);
        }
    }
}
