using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The town's buildings put up from their ready plans (Sim.Town.cs): taken from the «Город» tab, the building follows
/// the cursor as a ghost of its house — walls, floor, roof, door — R turns it, a click puts it up for the workers,
/// Esc or the right button puts it away. The plan is the same house the rectangle's «дом» draws, of the kind's size
/// and walls; what makes it that building is furnished inside once it stands.
/// </summary>
public partial class BuildTools
{
    /// <summary>The kind of building being put up, while the building tool is in hand.</summary>
    public TownKind Placing { get; private set; }
    bool _turned;
    string _placeWhy;

    /// <summary>Takes up a building's plan to put it up where the cursor points.</summary>
    public void StartBuilding(TownKind k)
    {
        Placing = k;
        _turned = false;
        Tool = Tool.Building;
        WandDrop();
        PasteBp = null;
        _lmb = _paint = _hasStart = false;
        _planKey = "~";
        _m.Hud.OnToolChanged();
    }

    /// <summary>The building's ghost where the cursor points, and what it costs or why not.</summary>
    void BuildingPlanAtHover()
    {
        var k = Placing;
        if (k == null || !_hasHover) { _ghost.Visible = false; PlanText = null; return; }
        var g = HoverGround();
        string key = $"T|{k.Key}|{g.x >> 1}|{g.z >> 1}|{_turned}|{_rev}|{(int)_m.Sim.Stock.Gold}";
        if (key == _planKey) return;
        _planKey = key; _planClock = _clock;
        var plan = _m.Sim.BuildingPlan(k, g.x, g.z, _turned, out _, out string why, out _);
        string no = _m.Sim.CantBuild(k);
        _placeWhy = plan == null ? why : no;
        if (plan == null) { _ghost.Visible = false; PlanText = $"{k.Name}: {why}"; PlanAffordable = false; return; }
        Ghost(MeshPlan(plan, false));
        _ghost.Visible = _ghost.Mesh != null;
        double sec = Labour.Estimate(_m.World, plan, false, _est);
        PlanText = $"{k.Name} · {k.Gold} золота мастерам · {Summary(_est, false, sec)}" + (no != null ? $" — {no}" : "");
        PlanAffordable = no == null;
        PlanIsDig = false;
    }

    /// <summary>The building put up where the cursor points, or why not.</summary>
    void PutUpAtHover()
    {
        var k = Placing;
        if (k == null || !_hasHover) return;
        var g = HoverGround();
        string no = _m.Sim.PutUp(k, g.x, g.z, _turned, out _);
        if (no != null) { Refuse(no); return; }
        _markName = k.Name;
        Sound.Ui("mark");
        _m.Hud.Log($"{k.Name}: рабочие строят стены, крышу и дверь, потом обставят внутри. Тогда здание заработает.", Hud.Kind.Ok);
        // one of each kind: the plan is put away once it is put up
        SetTool(Tool.None);
    }

    /// <summary>Turns the building in hand a quarter round.</summary>
    void TurnBuilding() { _turned = !_turned; _planKey = "~"; }
}
