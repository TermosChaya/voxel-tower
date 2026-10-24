using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The magic wand in hand: what is joined to the voxel under the cursor and of the same material (<see cref="Wand"/>)
/// lit as the cursor goes over it, and at a click taken down (the pick in hand), laid over again in the material in
/// hand (<see cref="Sim.Relay"/>), or kept as a blueprint. Shift, or the option, takes what touches by an edge or a
/// corner too; the form key, exactly the one shade under the cursor.
/// </summary>
public partial class BuildTools
{
    // the region under the cursor: made again only when the cursor leaves it, or the valley or the options change
    readonly VoxelList _wandL = new(), _wandPlan = new(), _wandTake = new();
    readonly HashSet<int> _wandSet = new();
    readonly double[] _wandCut = new double[4];
    bool _wandWhole = true;
    int _wandRev = -1, _wandGen;
    string _wandOpts = "";

    /// <summary>How many voxels the wand has under the cursor now (the self-test).</summary>
    public int WandCount => _hasHover ? _wandL.Count : 0;

    bool WandCorners => Opt.WandCorners || Input.IsKeyPressed(global::Godot.Key.Shift);

    /// <summary>The region under the cursor, made again if the cursor has left it or anything it hangs on has changed.</summary>
    void WandRegion()
    {
        if (!_hasHover) return;
        bool corners = WandCorners;
        string o = $"{Opt.WandExact}|{corners}";
        if (_wandRev == _rev && o == _wandOpts && _wandSet.Contains(WaterPlanner.Pack(_hover.X, _hover.Y, _hover.Z))) return;
        _wandWhole = Wand.Fill(_m.World, _hover.X, _hover.Y, _hover.Z, Opt.WandExact, corners, _wandL, _wandSet);
        _wandRev = _rev; _wandOpts = o; _wandGen++;
    }

    /// <summary>What of the region is ours to lay over again in the material in hand: as it stands, and as it will be laid.</summary>
    void WandRelayPlan()
    {
        _wandTake.Clear(); _wandPlan.Clear();
        var site = _m.Site;
        byte mat = Opt.Material;
        for (int i = 0; i < _wandL.Count; i++)
        {
            byte m = _wandL.M(i);
            int x = _wandL.X(i), y = _wandL.Y(i), z = _wandL.Z(i);
            if (!Mat.IsPlayer[m] || site.Forbidden(x, z) || Shapes.Family(m) == Shapes.Family(mat)) continue;
            _wandTake.Add(x, y, z, m);
            _wandPlan.Add(x, y, z, Shapes.Lay(mat, x, y, z));
        }
    }

    /// <summary>What it is laid over again in: «в камень», «в брёвна», «в землю».</summary>
    static string Into(byte m) => m switch { Mat.Rammed => "землю", Mat.Wood => "брёвна", _ => "камень" };

    /// <summary>"брёвна 3,1 м³ · разборка: камень +2,0 м³ · 4 мин работы одного".</summary>
    string RelaySummary(double sec)
    {
        string lay = Summary(_est, false, sec), got = "";
        foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth })
            if (_wandCut[k] < -.5) got += (got.Length > 0 ? ", " : "") + $"{Stockpile.Name(k)} +{Fmt.M3(-_wandCut[k])}";
        return got.Length > 0 ? lay.Replace(" · " + Fmt.Duration(sec), $" · разборка: {got} м³ · {Fmt.Duration(sec)}") : lay;
    }

    /// <summary>The ghost and the words over it for the region under the cursor and what the click would do with it.</summary>
    void WandPlan(bool any, bool dig, Stockpile st)
    {
        if (!any) { _ghost.Visible = false; PlanText = null; return; }
        var w = _m.World;
        string name = Mat.NameOf(_wandL.M(0)).ToLowerInvariant();
        string what = _wandWhole ? $"{name}, {Fmt.M3(_wandL.Count)} м³" : $"{name}: больше {Fmt.M3(Wand.Cap)} м³, столько разом не взять";
        PlanIsDig = dig && !Opt.WandCapture;
        Vector3 tint; float mix, inflate = .08f;
        if (Opt.WandCapture)
        {
            _wandPlan.Clear();
            for (int i = 0; i < _wandL.Count; i++) if (Mat.IsPlayer[_wandL.M(i)]) _wandPlan.Add(_wandL.X(i), _wandL.Y(i), _wandL.Z(i), _wandL.M(i));
            PlanText = _wandPlan.Count == 0 ? $"Палочка: {what} · чертёж снимают только с вашей кладки" : $"Чертёж: {what}";
            PlanAffordable = _wandPlan.Count > 0 && _wandWhole;
            Ghost(MeshPlan(_wandPlan.Count > 0 ? _wandPlan : _wandL, false));
            tint = new Vector3(.55f, .78f, .42f); mix = .45f;
        }
        else if (dig)
        {
            double sec = Labour.Estimate(w, _wandL, true, _est);
            System.Array.Copy(_est, PlanNeeds, 4);
            PlanText = sec <= 0 ? $"Палочка: {what} · это не копают" : $"Снести {what} · {Summary(_est, true, sec)}";
            PlanAffordable = sec > 0 && _wandWhole;
            Ghost(MeshPlan(_wandL, true));
            tint = new Vector3(.85f, .2f, .14f); mix = .62f; inflate = .1f;
        }
        else
        {
            WandRelayPlan();
            double sec = Labour.Estimate(w, _wandTake, true, _wandCut) + Labour.Estimate(w, _wandPlan, false, _est);
            System.Array.Copy(_est, PlanNeeds, 4);
            string shortOf = "";
            foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth })
                if (_est[k] > st[k] + .5) shortOf += (shortOf.Length > 0 ? ", " : "") + $"{Stockpile.Name(k)} {Fmt.M3(st[k])}";
            PlanAffordable = _wandPlan.Count > 0 && shortOf.Length == 0 && _wandWhole;
            PlanText = _wandPlan.Count == 0
                ? $"Палочка: {what} · переложить можно только вашу кладку из другого материала"
                : $"Переложить {what}, в {Into(Opt.Material)}: {RelaySummary(sec)}" + (shortOf.Length > 0 ? $" — в запасе {shortOf}" : "");
            Ghost(MeshPlan(_wandPlan.Count > 0 ? _wandPlan : _wandL, false));
            tint = PlanAffordable ? new Vector3(.9f, .55f, .25f) : new Vector3(.85f, .2f, .16f); mix = PlanAffordable ? .35f : .6f;
        }
        if (!_wandWhole) { tint = new Vector3(.85f, .2f, .16f); mix = .7f; }
        _ghost.Visible = _ghost.Mesh != null;
        _ghostMat.SetShaderParameter("tint", tint);
        _ghostMat.SetShaderParameter("tint_mix", mix);
        _ghostMat.SetShaderParameter("inflate", inflate);
    }

    /// <summary>A click: the region under the cursor taken down, laid over again, or kept as a blueprint.</summary>
    void WandAt()
    {
        WandRegion();
        if (_wandL.Count == 0) return;
        if (!_wandWhole) { Refuse($"столько разом не взять: больше {Fmt.M3(Wand.Cap)} м³ одного материала"); return; }
        _planKey = "~";
        if (Opt.WandCapture)
        {
            var bp = Blueprint.FromVoxels(_wandL, _m.NextBlueprintId, null);
            if (bp == null) { Refuse("чертёж снимают только с вашей кладки"); return; }
            _m.NextBlueprintId++;
            _m.AddBlueprint(bp);
            _m.Hud.Log($"Снят {bp.Name}: {bp.Describe()}", Hud.Kind.Ok);
            _m.Hud.Float($"чертёж: {bp.Name}", Ornament.Moss);
            Sound.Ui("mark");
            return;
        }
        if (Opt.Dig) { Designate(_wandL, true, "Снести"); return; }
        WandRelayPlan();
        if (_wandTake.Count == 0) { Refuse("переложить можно только вашу кладку из другого материала"); return; }
        var w = _m.World;
        double sec = Labour.Estimate(w, _wandTake, true, _wandCut) + Labour.Estimate(w, _wandPlan, false, _est);
        int n = _m.Sim.Relay(_wandL, Opt.Material);
        if (n == 0) { Refuse("это уже размечено"); return; }
        _markName = "Переложить";
        _m.Hud.Log($"Переложить в {Into(Opt.Material)}: {RelaySummary(sec)}");
        Marked("Переложить", _wandTake, true, n);
    }
}
