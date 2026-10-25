using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The town's buildings put up from their ready plans (Sim.Town.cs): taken from the «Город» tab, the building follows
/// the cursor as a ghost of its house — walls, floor, roof, door — R turns it, a click puts it up for the workers,
/// Esc or the right button puts it away. The plan is the same house the rectangle's «дом» draws, of the kind's size
/// and walls; what makes it that building is furnished inside once it stands. While the miners' building is in hand a
/// pillar of rusty light stands over every outcrop of ore it may go on.
/// </summary>
public partial class BuildTools
{
    /// <summary>The kind of building being put up, while the building tool is in hand.</summary>
    public TownKind Placing { get; private set; }
    bool _turned;
    string _placeWhy;
    MultiMeshInstance3D _outcrops;
    /// <summary>The outcrops marked now for the building in hand.</summary>
    public int OutcropMarks => _outcrops is { Visible: true } ? _outcrops.Multimesh.VisibleInstanceCount : 0;

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
        ShowOutcrops(k.OnOre);
        _m.Hud.OnToolChanged();
    }

    /// <summary>The outcrops a building that stands on one may go on (those with ore enough left), marked, or the marks put away.</summary>
    void ShowOutcrops(bool on)
    {
        if (!on) { if (_outcrops != null) _outcrops.Visible = false; return; }
        var w = _m.World;
        if (_outcrops == null)
        {
            _outcrops = new MultiMeshInstance3D
            {
                Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CylinderMesh { TopRadius = .3f, BottomRadius = .45f, Height = 16, RadialSegments = 8, Rings = 1 } },
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(1, .55f, .2f, .55f),
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_outcrops);
        }
        var crops = _m.Site.Outcrops;
        var mm = _outcrops.Multimesh;
        mm.InstanceCount = crops.Count;
        int n = 0;
        foreach (var o in crops)
        {
            if (_m.Sim.OreLeft(o.X, o.Z, o.R) < 40 || _m.Sim.BuildingAt(o.X, o.Z) != null) continue;
            mm.SetInstanceTransform(n++, new Transform3D(Basis.Identity, new Vector3(w.WX(o.X + .5f), w.WY(o.Y) + 8, w.WZ(o.Z + .5f))));
        }
        mm.VisibleInstanceCount = n;
        _outcrops.Visible = n > 0;
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
        PlanText = $"{k.Name} · {k.Gold} золота мастерам{(k.MetalCost > 0 ? $" · {k.MetalCost:0.##} м³ металла" : "")} · {Summary(_est, false, sec)}" + (no != null ? $" — {no}" : "");
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
