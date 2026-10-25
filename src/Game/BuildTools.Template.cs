using System;
using System.Collections.Generic;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The «Шаблоны» tool: the ready templates (Sim.Templates.cs) and the player's own blueprints in one library in the
/// bottom panel ({Form} goes on to the next). The template in hand follows the cursor as its ghost: {Rotate} turns it,
/// the height keys raise or lower it, a click puts it up for the workers. A wall or a palisade is drawn from one end
/// to the other, and an end that comes within 5 m of a tower set before goes to the tower's middle; a point template
/// drawn with Shift held is set again along the drag, a step apart (a row of towers along where a wall will go).
/// </summary>
public partial class BuildTools
{
    public static Template TemplateAt(int pick) => Sim.Templates[Math.Clamp(pick, 0, Sim.Templates.Length - 1)];
    public Template TemplatePick => TemplateAt(Opt.TemplatePick);

    /// <summary>The height set for each template that has one, by its key.</summary>
    readonly Dictionary<string, int> _templateSize = new();
    public int TemplateSize(Template t) => _templateSize.TryGetValue(t.Key, out int s) ? s : t.Size;

    /// <summary>The middles of the towers set from the library this game: the ends of a wall drawn near one go to it.</summary>
    readonly List<(int x, int z)> _towers = new();
    const int TowerSnap = 40;

    /// <summary>The template in hand is drawn rather than set: a wall, or a row of point templates with Shift.</summary>
    bool TemplateDrag => Tool == Tool.Template && (TemplatePick.Line || _shift);

    public void PickTemplate(int pick) { Opt.TemplatePick = Math.Clamp(pick, 0, Sim.Templates.Length - 1); Changed(); }

    public void StepTemplate(int dir) => PickTemplate((Opt.TemplatePick + dir + Sim.Templates.Length) % Sim.Templates.Length);

    /// <summary>The height keys on a template: a metre at a time within what it allows.</summary>
    void StepTemplateSize(int dir)
    {
        var t = TemplatePick;
        if (t.SizeMax == 0) { Refuse($"у шаблона «{t.Name}» высота одна"); return; }
        _templateSize[t.Key] = Math.Clamp(TemplateSize(t) + dir * 8, t.SizeMin, t.SizeMax);
        Changed();
    }

    /// <summary>How far apart a row of point templates is set: their own size and a few metres between.</summary>
    static int RowStep(Template t) => t.Key switch { "roundtower" => 96, "squaretower" => 80, "watchtower" => 56, "barn" => 64, "gatehouse" => 112, _ => 48 };

    /// <summary>An end of a wall drawn near a tower set before goes to the tower's middle.</summary>
    (int x, int z) ToTower(int x, int z)
    {
        int best = TowerSnap * TowerSnap + 1;
        var at = (x, z);
        foreach (var (tx, tz) in _towers)
        {
            int d = (tx - x) * (tx - x) + (tz - z) * (tz - z);
            if (d < best) { best = d; at = (tx, tz); }
        }
        return at;
    }

    /// <summary>The template in hand planned where the cursor is, or along the drag: its plans, or why not.</summary>
    List<(int x, int z, TemplatePlan plan)> TemplatePlans(bool dragging)
    {
        var t = TemplatePick;
        int size = TemplateSize(t), rot = Opt.TemplateRot;
        var list = new List<(int, int, TemplatePlan)>();
        var g = HoverGround();
        // the stair goes down from the top of the wall under the cursor
        if (t.Key == "stairs") { list.Add((_hover.X, _hover.Z, t.Plan(_m.World, _m.Site, _hover.X, _hover.Z, 0, 0, rot, size))); return list; }
        // a wall not drawn yet: nothing but how to draw it
        if (!dragging) { list.Add((g.x, g.z, t.Line ? new TemplatePlan { Why = "зажмите и протяните от одного конца до другого" } : t.Plan(_m.World, _m.Site, g.x, g.z, 0, 0, rot, size))); return list; }
        if (t.Line)
        {
            var a = ToTower(_startPt.x, _startPt.z); var b = ToTower(_dragPt.x, _dragPt.z);
            list.Add((a.x, a.z, t.Plan(_m.World, _m.Site, a.x, a.z, b.x, b.z, rot, size)));
            return list;
        }
        int ax = _startPt.x, az = _startPt.z, bx = _dragPt.x, bz = _dragPt.z, step = RowStep(t);
        double len = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
        int n = Math.Min(12, (int)(len / step) + 1);
        for (int i = 0; i < n; i++)
        {
            double f = n == 1 ? 0 : i * step / len;
            int x = (int)Math.Round(ax + (bx - ax) * f), z = (int)Math.Round(az + (bz - az) * f);
            list.Add((x, z, t.Plan(_m.World, _m.Site, x, z, 0, 0, rot, size)));
        }
        return list;
    }

    /// <summary>The ghost of the template in hand, and what it costs, how long it takes, or why not.</summary>
    void TemplatePlanAtHover()
    {
        var t = TemplatePick;
        bool dragging = TemplateDrag && _lmb && _hasStart && _hasDrag;
        if (!_hasHover) { _ghost.Visible = false; PlanText = null; return; }
        var g = HoverGround();
        var st = _m.Sim.Stock;
        string key = $"Q|{t.Key}|{TemplateSize(t)}|{Opt.TemplateRot}|{(dragging ? $"{_startPt}|{_dragPt}" : t.Key == "stairs" ? $"{_hover.X},{_hover.Z}" : $"{g.x >> 1},{g.z >> 1}")}|{_rev}|{_m.Sim.Standing}|{(int)(st[1] / 400)}|{(int)(st[2] / 400)}";
        if (key == _planKey || dragging && _clock - _planClock < .1) return;
        _planKey = key; _planClock = _clock;
        var plans = TemplatePlans(dragging);
        var all = new VoxelList();
        string why = null;
        foreach (var (_, _, p) in plans)
        {
            if (p?.Voxels == null) { why ??= p?.Why ?? "здесь нельзя"; continue; }
            for (int i = 0; i < p.Voxels.Count; i++) all.Add(p.Voxels.X(i), p.Voxels.Y(i), p.Voxels.Z(i), p.Voxels.M(i));
        }
        string name = plans.Count > 1 ? $"{t.Name} × {plans.Count}" : t.Name;
        if (all.Count == 0) { _ghost.Visible = false; PlanText = $"{name}: {why}"; PlanAffordable = false; return; }
        double sec = Labour.Estimate(_m.World, all, false, _est);
        Array.Copy(_est, PlanNeeds, Stockpile.Kinds);
        string shortOf = "";
        foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood })
            if (_est[k] > st[k] + .5) shortOf += (shortOf.Length > 0 ? ", " : "") + $"{Stockpile.Name(k)} {Fmt.M3(st[k])}";
        string shut = _m.Sim.TemplateOpen(t) ? null : $"откроется на уровне «{Sim.StandingNames[t.Opens]}»";
        PlanAffordable = shortOf.Length == 0 && shut == null && why == null;
        PlanIsDig = false;
        PlanText = $"{name}: {Summary(_est, false, sec)}" + (shut != null ? $" — {shut}" : why != null ? $" — {why}" : shortOf.Length > 0 ? $" — в запасе {shortOf}" : "");
        Ghost(MeshPlan(all, false));
        _ghost.Visible = _ghost.Mesh != null;
    }

    /// <summary>The template put up where the cursor is, or along the drag; the towers among it remembered for the walls to come.</summary>
    void PutTemplateHere(bool click)
    {
        var t = TemplatePick;
        if (!_hasHover && !_hasDrag) return;
        bool dragging = TemplateDrag && _hasStart && _hasDrag && !click;
        if (t.Line && !dragging) { Refuse($"{t.Name}: зажмите и протяните от одного конца до другого"); return; }
        var plans = TemplatePlans(dragging);
        var all = new VoxelList();
        int put = 0;
        string why = null;
        foreach (var (x, z, p) in plans)
        {
            string no = _m.Sim.PutTemplate(t, p);
            if (no != null) { why ??= no; continue; }
            put++;
            for (int i = 0; i < p.Voxels.Count; i++) all.Add(p.Voxels.X(i), p.Voxels.Y(i), p.Voxels.Z(i), p.Voxels.M(i));
            if (t.Key is "roundtower" or "squaretower") _towers.Add((x, z));
        }
        if (put == 0) { Refuse($"{t.Name}: {why}"); return; }
        double sec = Labour.Estimate(_m.World, all, false, _est);
        _markName = t.Name;
        _m.Hud.Log($"{(put > 1 ? $"{t.Name} × {put}" : t.Name)}: {Summary(_est, false, sec)}" + (why != null ? $". Не все: {why}" : ""));
        Marked(t.Name, all, false, all.Count);
        TemplatesPut++;
        _planKey = "~";
    }

    /// <summary>How many times a template was put up this game (the tutorial and the let-out ask).</summary>
    public int TemplatesPut;
}
