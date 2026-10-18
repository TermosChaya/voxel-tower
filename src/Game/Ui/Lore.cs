using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The lore the household can learn, as a panel let down from the beam: the knowledge at hand, and the five branches
/// side by side, each a column of three plaques — learnt (bright), to be learnt now (a click learns it), or not yet
/// (faint, with what it wants in its tooltip).
/// </summary>
public partial class LorePanel : VBoxContainer
{
    Main _m;
    Label _head;
    readonly List<(Lore lore, Button button)> _plaques = new();
    public const float Width = 760;

    public void Init(Main m)
    {
        _m = m;
        AddThemeConstantOverride("separation", 6);
        AddChild(Hud.CapsLabel("Знания"));
        _head = Hud.Text("", Ornament.SerifItalic, 13, Ornament.Aged, wrap: true);
        _head.CustomMinimumSize = new Vector2(Width - 30, 0);
        AddChild(_head);
        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 8);
        AddChild(cols);
        foreach (string branch in Sim.Branches)
        {
            var col = new VBoxContainer { CustomMinimumSize = new Vector2(140, 0) };
            col.AddThemeConstantOverride("separation", 5);
            col.AddChild(Hud.CapsLabel(branch, 9, Ornament.BronzeLight));
            foreach (var l in Sim.Lores)
            {
                if (l.Branch != branch) continue;
                var lore = l;
                var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(140, 46), MouseDefaultCursorShape = CursorShape.PointingHand,
                    ClipText = true, AutowrapMode = TextServer.AutowrapMode.WordSmart };
                b.AddThemeFontSizeOverride("font_size", 12);
                b.Pressed += () => { if (_m.Sim.Learn(lore.Key)) { Refresh(); _m.Hud.Refresh(); } };
                col.AddChild(b);
                _plaques.Add((l, b));
            }
            cols.AddChild(col);
        }
        Refresh();
    }

    public void Refresh()
    {
        if (_m == null) return;
        var sim = _m.Sim;
        _head.Text = $"Знаний: {sim.Knowledge:0}. Их дают отбитые волны — чем дальше, тем больше. В каждой ветви сначала первое, потом любое из двух других.";
        foreach (var (l, b) in _plaques)
        {
            bool known = sim.Knows(l.Key), can = sim.CanLearn(l), open = l.Needs == null || sim.Knows(l.Needs);
            // the price never wraps onto a line of its own
            b.Text = known ? $"✓ {l.Name}" : $"{l.Name}\u00a0·\u00a0{l.Cost}";
            b.Disabled = !can;
            // the learnt in moss, as done goals are; what wants more, faint
            b.AddThemeColorOverride("font_disabled_color", known ? Ornament.Moss : new Color(Ornament.Aged, .55f));
            b.Modulate = known || can ? Colors.White : new Color(1, 1, 1, .6f);
            string why = known ? "Изучено." : !open ? $"Сначала: «{Sim.LoreOf(l.Needs).Name}»." : can ? $"Щелчок — изучить за {l.Cost}." : $"Нужно {l.Cost} знаний.";
            b.TooltipText = $"{l.Name}\n{l.About} {why}";
        }
    }

    /// <summary>The plaque of a lore (the self-test presses it).</summary>
    public Button PlaqueOf(string key) => _plaques.Find(p => p.lore.Key == key).button;

    /// <summary>"3": the knowledge at hand, for the tab's badge, or nothing when nothing can be learnt.</summary>
    public string Badge()
    {
        var sim = _m.Sim;
        foreach (var l in Sim.Lores) if (sim.CanLearn(l)) return $"{sim.Knowledge:0}";
        return "";
    }

    public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
}
