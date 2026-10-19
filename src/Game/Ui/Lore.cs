using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The lore the household can learn, as a tree let down from the beam: the knowledge at hand, and the five branches one
/// under another, each four deep from left to right — the first, two resting on it, a further one on each, and the last
/// resting on both — with brass lines running from what a lore rests on to it. A plaque learnt has a moss rim; one to be
/// learnt now glows at its rim, and a click learns it; one that wants more knowledge is plain; one whose roots are not
/// learnt yet is faint. Its tooltip says what it does and what it wants.
/// </summary>
public partial class LorePanel : VBoxContainer
{
    Main _m;
    Label _head;
    TreeCanvas _tree;
    readonly List<(Lore lore, Button button)> _plaques = new();
    public const float Width = 980;
    // the tree's layout, in canvas pixels: a column of branch names, then four tiers
    const float LabelW = 118, CellW = 178, CellGap = 34, PlaqueH = 36, LaneGap = 8, BranchGap = 16, Top = 4;
    static readonly float BranchH = 2 * PlaqueH + LaneGap;
    static readonly float TreeW = LabelW + 4 * CellW + 3 * CellGap;
    static float TreeH => Top + Sim.Branches.Length * BranchH + (Sim.Branches.Length - 1) * BranchGap;

    /// <summary>Where a lore's plaque stands on the tree.</summary>
    static Rect2 PlaqueRect(Lore l)
    {
        int b = Array.IndexOf(Sim.Branches, l.Branch);
        return new Rect2(LabelW + l.Tier * (CellW + CellGap), Top + b * (BranchH + BranchGap) + l.Lane * (PlaqueH + LaneGap), CellW, PlaqueH);
    }

    public void Init(Main m)
    {
        _m = m;
        AddThemeConstantOverride("separation", 6);
        AddChild(Hud.CapsLabel("Знания"));
        _head = Hud.Text("", Ornament.SerifItalic, 13, Ornament.Aged, wrap: true);
        _head.CustomMinimumSize = new Vector2(TreeW, 0);
        AddChild(_head);
        _tree = new TreeCanvas(this) { CustomMinimumSize = new Vector2(TreeW, TreeH), MouseFilter = MouseFilterEnum.Pass };
        AddChild(_tree);
        foreach (var l in Sim.Lores)
        {
            var lore = l;
            var r = PlaqueRect(l);
            var b = new LorePlaque
            {
                FocusMode = FocusModeEnum.None, Position = r.Position, Size = r.Size, MouseDefaultCursorShape = CursorShape.PointingHand,
                ClipText = true, AutowrapMode = TextServer.AutowrapMode.WordSmart, Alignment = HorizontalAlignment.Center,
            };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.Pressed += () => { if (_m.Sim.Learn(lore.Key)) { Refresh(); _m.Hud.Refresh(); } };
            _tree.AddChild(b);
            _plaques.Add((l, b));
        }
        Refresh();
    }

    // the rims of the plaques, by how they stand
    static readonly StyleBoxFlat Learnt = Rim(new Color(.11f, .13f, .09f), Ornament.Moss, 2, null);
    static readonly StyleBoxFlat Ready = Rim(Ornament.EmberDim, Ornament.EmberGlow, 2, Ornament.Fade(Ornament.Ember, .55f));
    static readonly StyleBoxFlat ReadyHover = Rim(new Color(.33f, .2f, .09f), Ornament.EmberGlow, 2, Ornament.Fade(Ornament.EmberGlow, .7f));
    static readonly StyleBoxFlat Wanting = Rim(Ornament.IronMid, Ornament.BronzeDark, 1, null);
    static readonly StyleBoxFlat Shut = Rim(Ornament.Iron, Ornament.IronLight, 1, null);

    static StyleBoxFlat Rim(Color bg, Color border, int width, Color? glow)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border, ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 2, ContentMarginBottom = 2 };
        s.SetBorderWidthAll(width);
        s.SetCornerRadiusAll(3);
        if (glow is { } g) { s.ShadowColor = g; s.ShadowSize = 5; }
        return s;
    }

    /// <summary>How a lore stands now: 0 learnt, 1 to be learnt now, 2 open but wanting knowledge, 3 shut (its roots not learnt).</summary>
    int StateOf(Lore l)
    {
        var sim = _m.Sim;
        return sim.Knows(l.Key) ? 0 : sim.CanLearn(l) ? 1 : sim.Opened(l) ? 2 : 3;
    }

    public void Refresh()
    {
        if (_m == null) return;
        var sim = _m.Sim;
        _head.Text = $"Знаний: {sim.Knowledge:0}. Изучено {sim.Learnt.Count} из {Sim.Lores.Length}. Знания дают отбитые волны — чем дальше, тем больше; " +
            "на всё древо за осаду их не хватит, так что выбирайте. Линии ведут от того, на чём умение стоит; последнее в ветви стоит на двух.";
        foreach (var (l, b) in _plaques)
        {
            int state = StateOf(l);
            b.Text = state == 0 ? $"{l.Name}\n✓ изучено" : $"{l.Name}\nцена {l.Cost}";
            b.Disabled = state != 1;
            var look = state switch { 0 => Learnt, 1 => Ready, 2 => Wanting, _ => Shut };
            foreach (string s in new[] { "normal", "disabled", "pressed" }) b.AddThemeStyleboxOverride(s, look);
            b.AddThemeStyleboxOverride("hover", state == 1 ? ReadyHover : look);
            b.AddThemeColorOverride("font_disabled_color", state == 0 ? Ornament.Moss : state == 2 ? Ornament.Aged : Ornament.Faded);
            b.AddThemeColorOverride("font_color", Ornament.EmberGlow);
            b.AddThemeColorOverride("font_hover_color", Ornament.Parchment);
            b.Modulate = state == 3 ? new Color(1, 1, 1, .8f) : Colors.White;
            string why = state switch
            {
                0 => "Изучено.",
                1 => $"Щелчок — изучить за {l.Cost}.",
                2 => $"Нужно {l.Cost} {Plural(l.Cost)}, есть {sim.Knowledge:0}.",
                _ => "Сначала: " + string.Join(", ", Array.ConvertAll(Array.FindAll(l.Needs, n => !sim.Knows(n)), n => $"«{Sim.LoreOf(n).Name}»")) + ".",
            };
            string rests = l.Needs.Length == 0 ? "" : " Стоит на: " + string.Join(", ", Array.ConvertAll(l.Needs, n => $"«{Sim.LoreOf(n).Name}»")) + ".";
            b.TooltipText = $"{l.Name} · {l.Branch}\n{l.About}{rests} {why}";
        }
        _tree.QueueRedraw();
    }

    static string Plural(int n) => n % 10 == 1 && n % 100 != 11 ? "знание" : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? "знания" : "знаний";

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

    /// <summary>A plaque of the tree: a button that shows its tooltip on the iron plate, as the rest of the game does.</summary>
    partial class LorePlaque : Button
    {
        public override GodotObject _MakeCustomTooltip(string forText) => Tooltip.Make(forText);
    }

    /// <summary>The tree's ground: the branches' names and marks down the left, and the brass lines between the plaques.</summary>
    partial class TreeCanvas : Control
    {
        readonly LorePanel _p;
        public TreeCanvas(LorePanel p) { _p = p; }

        static Icons.Painter MarkOf(string branch) => branch switch
        {
            "Луки" => Icons.Bow, "Кладка" => Icons.Box, "Труд" => Icons.Pick, "Дружина" => Icons.Sword, _ => Icons.Flame,
        };

        public override void _Draw()
        {
            if (_p._m == null) return;
            var sim = _p._m.Sim;
            // the branches: a name and a mark at the head of each, a faint rule between them
            for (int i = 0; i < Sim.Branches.Length; i++)
            {
                float y0 = Top + i * (BranchH + BranchGap), mid = y0 + BranchH / 2;
                MarkOf(Sim.Branches[i])(this, new Rect2(4, mid - 15, 30, 30), Ornament.Bronze);
                Ornament.Lettering(this, Ornament.Caps, new Vector2(40, mid + 5), Sim.Branches[i].ToUpperInvariant(), 10, Ornament.BronzeLight);
                if (i > 0) Ornament.Divider(this, new Vector2(8, y0 - BranchGap / 2), new Vector2(TreeW - 8, y0 - BranchGap / 2), .35f);
            }
            // the lines: out of the right of what it rests on, down or up in the gap before it, into its left
            foreach (var l in Sim.Lores)
            {
                var to = PlaqueRect(l);
                foreach (string n in l.Needs)
                {
                    var from = PlaqueRect(Sim.LoreOf(n));
                    bool both = sim.Knows(n) && sim.Knows(l.Key), open = sim.Knows(n);
                    var c = both ? Ornament.Moss : open ? Ornament.BronzeLight : new Color(Ornament.Faded, .7f);
                    float xa = from.End.X, ya = from.Position.Y + from.Size.Y / 2, xb = to.Position.X, yb = to.Position.Y + to.Size.Y / 2, xv = xb - CellGap / 2;
                    var pts = new[] { new Vector2(xa, ya), new Vector2(xv, ya), new Vector2(xv, yb), new Vector2(xb, yb) };
                    DrawPolyline(pts, new Color(0, 0, 0, .55f), 3.5f, true);
                    DrawPolyline(pts, c, open ? 1.8f : 1.2f, true);
                }
            }
        }
    }
}
