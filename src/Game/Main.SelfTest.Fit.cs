using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The interface in the least room it has, the largest size of it (110 %): every tool in hand, with its pick and its
    /// fills, keeps all its settings inside the console between the map and the tools and above the screen's edge, the
    /// console's middle rising for those with many; the household of sixty along the top goes in two rows inside its
    /// room, the last card «+N», which lets all of them out and folds them up again.
    /// </summary>
    async Task SelfTestFit(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var root = GetTree().Root;
        float scaleWas = root.ContentScaleFactor;
        root.ContentScaleFactor = 1.1f;
        await Frames(3);
        var vp = GetViewport().GetVisibleRect().Size;
        var ins = Hud.Inspector;
        float left = BottomConsole.LeftW, right = vp.X - BottomConsole.RightW;

        // the tools, each as it is taken up, then in its other forms
        var cases = new List<(string name, Action set)>();
        foreach (var d in Hud.ToolDefs)
        {
            var t = d.tool;
            cases.Add((t.ToString(), () => { Tools.SetTool(t); Tools.SetDig(false); }));
            if (t is Tool.Line or Tool.Box or Tool.Circle or Tool.Brush or Tool.Wand) cases.Add((t + " with the pick", () => { Tools.SetTool(t); Tools.SetDig(true); }));
        }
        cases.Add(("Box, walls", () => { Tools.SetTool(Tool.Box); Tools.SetDig(false); Tools.Opt.Hollow = true; Tools.Opt.House = false; Tools.Changed(); }));
        cases.Add(("Box, a house", () => { Tools.SetTool(Tool.Box); Tools.SetDig(false); Tools.Opt.Hollow = true; Tools.Opt.House = true; Tools.Changed(); }));
        cases.Add(("Circle, a ring", () => { Tools.SetTool(Tool.Circle); Tools.SetDig(false); Tools.Opt.Hollow = true; Tools.Changed(); }));
        cases.Add(("Yard, a thing", () => { Tools.SetTool(Tool.Yard); Tools.PickYard(BuildTools.WayCount); }));
        for (int tab = 0; tab < Sim.TemplateTabs.Length; tab++) { int k = tab; cases.Add(($"Template, tab {k}", () => { Tools.SetTool(Tool.Template); Tools.PickTemplateTab(k); })); }
        var bad = new List<string>();
        float raisedMost = 0;
        foreach (var (name, set) in cases)
        {
            set();
            ins.Refresh(true);
            await Frames(5);
            var box = ins.GetGlobalRect();
            raisedMost = Math.Max(raisedMost, ins.Raise);
            if (box.Position.X < left - 1 || box.End.X > right + 1 || box.Position.Y < Hud.TopBand || box.End.Y > vp.Y + 1) { bad.Add($"{name}: the console at {box}"); continue; }
            if (OutOf(ins, new Rect2(box.Position.X, 0, box.Size.X, vp.Y)) is { } stray) bad.Add($"{name}: «{stray}» sticks out");
        }
        Tools.Opt.Hollow = Tools.Opt.House = false;
        Tools.SetDig(false);
        Tools.SetTool(Tool.None);
        check("in the largest interface every tool keeps its settings inside the console, between the map and the tools and above the screen's edge, the console rising where they are many",
            bad.Count == 0 && raisedMost > 0, bad.Count == 0 ? $"{cases.Count} tools and forms, the console risen {raisedMost:0} at most" : string.Join("; ", bad.GetRange(0, Math.Min(4, bad.Count))));

        // a household of sixty: two rows, «+N» on the last card; let out, all of them; folded up again
        var added = new List<Unit>();
        int have = 0;
        foreach (var u in Sim.Units) if (!u.Dead && u.Team == Team.Player && !u.T.Called) have++;
        for (int i = have; i < 60; i++)
        {
            int c = Sim.NearestStand(Site.KeepX + (i % 10 - 5) * 10, Site.KeepZ + Site.KeepR + 30 + i / 10 * 10, Kinds.Worker.Move.Height);
            if (c >= 0) added.Add(Sim.Spawn(Kinds.Worker, c));
        }
        await Frames(4);
        var bar = Hud.Colonists;
        var at = bar.GetGlobalRect();
        int rows = bar.Rows, folded = bar.Folded;
        bool inside = at.Position.X >= bar.From - 1 && at.End.X <= bar.To + 1 && at.Position.X >= 0 && at.End.X <= vp.X;
        var more = bar.MoreAt;
        if (more is { } m) bar._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = m });
        await Frames(4);
        int openRows = bar.Rows, openFolded = bar.Folded;
        var openAt = bar.GetGlobalRect();
        bool openInside = openAt.Position.X >= bar.From - 1 && openAt.End.X <= bar.To + 1 && openAt.End.Y < vp.Y - BottomConsole.Band;
        if (bar.MoreAt is { } back) bar._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = back });
        await Frames(4);
        int foldedRows = bar.Rows;
        check("sixty of ours go along the top in two rows inside their room, the last card «+N»; it lets all of them out, and folds them up again",
            rows == 2 && folded > 0 && inside && more != null && openFolded == 0 && openRows > 2 && openInside && foldedRows == 2,
            $"{rows} rows, {folded} left out, inside {inside}; let out: {openRows} rows, {openFolded} left out, inside {openInside}; folded again: {foldedRows} rows");
        foreach (var u in added) { u.Dead = true; u.DiedAt = -999; }
        root.ContentScaleFactor = scaleWas;
        await Frames(4);
    }

    /// <summary>The first thing shown in a control that sticks out of a rectangle (its words, or its kind), or null; what a scroll holds is its own.</summary>
    static string OutOf(Control root, Rect2 bounds)
    {
        foreach (var n in root.GetChildren())
        {
            if (n is not Control c || !c.IsVisibleInTree() || c.Size.X < 1 || c.Size.Y < 1) continue;
            var r = c.GetGlobalRect();
            if (r.Position.X < bounds.Position.X - 1 || r.End.X > bounds.End.X + 1 || r.Position.Y < bounds.Position.Y - 1 || r.End.Y > bounds.End.Y + 1)
                return (c as Label)?.Text ?? (c as Button)?.Text ?? c.GetType().Name;
            if (c is ScrollContainer) continue;
            if (OutOf(c, bounds) is { } deeper) return deeper;
        }
        return null;
    }
}
