using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// A word called out by one of ours (Sim.Felling.cs, Sim.Undermine.cs: a feller's «Берегись!» before a tree goes over, a
/// digger's before what is built comes down) over the one who called it, rising a little and fading in two seconds.
/// None from someone's eyes, nor over the menu.
/// </summary>
public partial class ShoutTags : CanvasLayer
{
    /// <summary>Seconds a word stays over the one who called it.</summary>
    const double Lasts = 2;
    Main _m;
    Control _root;
    readonly List<(Unit u, Label tag, double at)> _tags = new();
    // (called out during the sim's step: the tags are made in the frame after)
    readonly List<(Unit u, string word)> _called = new();
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
        m.Sim.Shouted += (u, word) => _called.Add((u, word));
    }

    public override void _Process(double delta)
    {
        // (a word called out stays while the game stands still)
        if (!_m.Paused) _t += delta;
        foreach (var (u, word) in _called)
        {
            // one word at a time over each
            for (int i = _tags.Count - 1; i >= 0; i--) if (_tags[i].u == u) { _tags[i].tag.QueueFree(); _tags.RemoveAt(i); }
            var tag = Hud.Text(word, Ornament.SerifBold, 18, new Color("ffd890"), outline: 7);
            tag.MouseFilter = Control.MouseFilterEnum.Ignore;
            tag.Visible = false;
            _root.AddChild(tag);
            _tags.Add((u, tag, _t));
        }
        _called.Clear();
        var cam = GetViewport().GetCamera3D();
        bool hidden = cam == null || _m.FP.On || _m.Hud.MenuOpen || _m.Hud.TitleOpen;
        for (int i = _tags.Count - 1; i >= 0; i--)
        {
            var (u, tag, at) = _tags[i];
            double age = _t - at;
            if (age > Lasts || u.Dead || !_m.Sim.Units.Contains(u)) { tag.QueueFree(); _tags.RemoveAt(i); continue; }
            var pos = new Vector3(_m.World.WX(u.X), _m.World.WY(u.Y + u.T.BodyH + 6 + (float)age * 5), _m.World.WZ(u.Z));
            tag.Visible = !hidden && !cam.IsPositionBehind(pos);
            if (!tag.Visible) continue;
            tag.Modulate = new Color(1, 1, 1, (float)Math.Clamp((Lasts - age) / .6, 0, 1));
            tag.ResetSize();
            tag.Position = cam.UnprojectPosition(pos) - new Vector2(tag.Size.X / 2, tag.Size.Y);
        }
    }
}
