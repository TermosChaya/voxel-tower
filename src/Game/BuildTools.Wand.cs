using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The magic wand in hand: what is joined to the voxel under the cursor and of the same material (<see cref="Wand"/>)
/// lit as the cursor goes over it, and at a click taken down (the pick in hand), laid over again in the material in
/// hand (<see cref="Sim.Relay"/>), or kept as a blueprint. Shift and a click add the region under the cursor to what
/// is chosen already, and the next click does it to all of them as one marking (Ctrl+Z takes it back whole); Esc lets
/// what was chosen go. The option takes what touches by an edge or a corner too; the form key, exactly the one shade
/// under the cursor.
/// </summary>
public partial class BuildTools
{
    // the region under the cursor: made again only when the cursor leaves it, or the valley or the options change
    readonly VoxelList _wandL = new(), _wandPlan = new(), _wandTake = new();
    readonly HashSet<int> _wandSet = new();
    readonly double[] _wandCut = new double[Stockpile.Kinds];
    bool _wandWhole = true;
    int _wandRev = -1, _wandGen;
    string _wandOpts = "";
    // the regions chosen with Shift so far, and all of them with the one under the cursor
    readonly VoxelList _wandHeld = new(), _wandAll = new();
    readonly HashSet<int> _wandHeldSet = new();
    int _wandHeldN, _wandHeldGen, _wandAllKey = -1;

    /// <summary>Regions chosen with Shift and not done yet (the hints, the inspector, Esc).</summary>
    public int WandHeld => _wandHeldN;

    /// <summary>How many voxels the wand has under the cursor now (the self-test).</summary>
    public int WandCount => _hasHover ? _wandL.Count : 0;

    bool WandCorners => Opt.WandCorners;

    /// <summary>What a click acts on: the region under the cursor, with those chosen with Shift before it.</summary>
    VoxelList WandPick()
    {
        if (_wandHeldN == 0) return _wandL;
        int key = _wandGen * 7919 + _wandHeldGen;
        if (key == _wandAllKey) return _wandAll;
        _wandAllKey = key;
        _wandAll.Clear();
        for (int i = 0; i < _wandHeld.Count; i++) _wandAll.Add(_wandHeld.X(i), _wandHeld.Y(i), _wandHeld.Z(i), _wandHeld.M(i));
        if (_hasHover)
            for (int i = 0; i < _wandL.Count; i++)
                if (!_wandHeldSet.Contains(WaterPlanner.Pack(_wandL.X(i), _wandL.Y(i), _wandL.Z(i)))) _wandAll.Add(_wandL.X(i), _wandL.Y(i), _wandL.Z(i), _wandL.M(i));
        return _wandAll;
    }

    /// <summary>Shift and a click: the region under the cursor kept with those chosen already, nothing marked yet.</summary>
    void WandAdd()
    {
        WandRegion();
        if (_wandL.Count == 0) return;
        if (!_wandWhole) { Refuse($"столько разом не взять: больше {Fmt.M3(Wand.Cap)} м³ одного материала"); return; }
        int added = 0;
        for (int i = 0; i < _wandL.Count; i++)
            if (_wandHeldSet.Add(WaterPlanner.Pack(_wandL.X(i), _wandL.Y(i), _wandL.Z(i)))) { _wandHeld.Add(_wandL.X(i), _wandL.Y(i), _wandL.Z(i), _wandL.M(i)); added++; }
        if (added == 0) { Refuse("это уже выделено"); return; }
        _wandHeldN++; _wandHeldGen++;
        _planKey = "~";
        _m.Hud.Float($"к выделению +{Fmt.M3(added)} м³ · выделено областей: {_wandHeldN}", Ornament.BronzeLight);
        Sound.Ui("tick");
        _m.Hud.Refresh();
    }

    /// <summary>What was chosen with Shift let go.</summary>
    public void WandDrop()
    {
        if (_wandHeldN == 0) return;
        _wandHeld.Clear(); _wandHeldSet.Clear(); _wandAll.Clear();
        _wandHeldN = 0; _wandHeldGen++; _wandAllKey = -1;
        _planKey = "~";
        _m.Hud.Refresh();
    }

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
    void WandRelayPlan(VoxelList pick)
    {
        _wandTake.Clear(); _wandPlan.Clear();
        var site = _m.Site;
        byte mat = Opt.Material;
        for (int i = 0; i < pick.Count; i++)
        {
            byte m = pick.M(i);
            int x = pick.X(i), y = pick.Y(i), z = pick.Z(i);
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
        foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth, Stockpile.Metal })
            if (_wandCut[k] < -.5) got += (got.Length > 0 ? ", " : "") + $"{Stockpile.Name(k)} +{Fmt.M3(-_wandCut[k])}";
        return got.Length > 0 ? lay.Replace(" · " + Fmt.Duration(sec), $" · разборка: {got} м³ · {Fmt.Duration(sec)}") : lay;
    }

    /// <summary>The ghost and the words over it for the region under the cursor and what the click would do with it.</summary>
    void WandPlan(bool any, bool dig, Stockpile st)
    {
        if (!any) { _ghost.Visible = false; PlanText = null; return; }
        var w = _m.World;
        var pick = WandPick();
        string name = Mat.NameOf(pick.M(0)).ToLowerInvariant();
        string what = !_wandWhole ? $"{name}: больше {Fmt.M3(Wand.Cap)} м³, столько разом не взять"
            : _wandHeldN > 0 ? $"{Fmt.M3(pick.Count)} м³ в {_wandHeldN + 1} областях" : $"{name}, {Fmt.M3(pick.Count)} м³";
        PlanIsDig = dig && !Opt.WandCapture;
        Vector3 tint; float mix, inflate = .08f;
        if (Opt.WandCapture)
        {
            _wandPlan.Clear();
            for (int i = 0; i < pick.Count; i++) if (Mat.IsPlayer[pick.M(i)]) _wandPlan.Add(pick.X(i), pick.Y(i), pick.Z(i), pick.M(i));
            PlanText = _wandPlan.Count == 0 ? $"Палочка: {what} · чертёж снимают только с вашей кладки" : $"Чертёж: {what}";
            PlanAffordable = _wandPlan.Count > 0 && _wandWhole;
            GhostFor(_wandPlan.Count > 0 ? _wandPlan : pick, false);
            tint = new Vector3(.55f, .78f, .42f); mix = .45f;
        }
        else if (dig)
        {
            double sec = Labour.Estimate(w, pick, true, _est);
            System.Array.Copy(_est, PlanNeeds, Stockpile.Kinds);
            PlanText = sec <= 0 ? $"Палочка: {what} · это не копают" : $"Снести {what} · {Summary(_est, true, sec)}";
            PlanAffordable = sec > 0 && _wandWhole;
            GhostFor(pick, true);
            tint = new Vector3(.85f, .2f, .14f); mix = .62f; inflate = .1f;
        }
        else
        {
            WandRelayPlan(pick);
            double sec = Labour.Estimate(w, _wandTake, true, _wandCut) + Labour.Estimate(w, _wandPlan, false, _est);
            System.Array.Copy(_est, PlanNeeds, Stockpile.Kinds);
            string shortOf = "";
            foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth, Stockpile.Metal })
                if (_est[k] > st[k] + .5) shortOf += (shortOf.Length > 0 ? ", " : "") + $"{Stockpile.Name(k)} {Fmt.M3(st[k])}";
            PlanAffordable = _wandPlan.Count > 0 && shortOf.Length == 0 && _wandWhole;
            PlanText = _wandPlan.Count == 0
                ? $"Палочка: {what} · переложить можно только вашу кладку из другого материала"
                : $"Переложить {what}, в {Into(Opt.Material)}: {RelaySummary(sec)}" + (shortOf.Length > 0 ? $" — в запасе {shortOf}" : "");
            GhostFor(_wandPlan.Count > 0 ? _wandPlan : pick, false);
            tint = PlanAffordable ? new Vector3(.9f, .55f, .25f) : new Vector3(.85f, .2f, .16f); mix = PlanAffordable ? .35f : .6f;
        }
        if (!_wandWhole) { tint = new Vector3(.85f, .2f, .16f); mix = .7f; }
        _ghost.Visible = _ghost.Mesh != null;
        _ghostMat.SetShaderParameter("tint", tint);
        _ghostMat.SetShaderParameter("tint_mix", mix);
        _ghostMat.SetShaderParameter("inflate", inflate);
    }

    /// <summary>
    /// A click: the region under the cursor (with those chosen with Shift) taken down, laid over again, or kept as a
    /// blueprint, as one marking; with Shift, kept with those chosen.
    /// </summary>
    void WandAt()
    {
        if (_shift) { WandAdd(); return; }
        WandRegion();
        if (_wandL.Count == 0 && _wandHeldN == 0) return;
        if (_wandL.Count > 0 && !_wandWhole) { Refuse($"столько разом не взять: больше {Fmt.M3(Wand.Cap)} м³ одного материала"); return; }
        var pick = WandPick();
        try { WandDo(pick); }
        finally { WandDrop(); }
    }

    void WandDo(VoxelList pick)
    {
        _planKey = "~";
        if (Opt.WandCapture)
        {
            var bp = Blueprint.FromVoxels(pick, _m.NextBlueprintId, null);
            if (bp == null) { Refuse("чертёж снимают только с вашей кладки"); return; }
            _m.NextBlueprintId++;
            _m.AddBlueprint(bp);
            _m.Hud.Log($"Снят {bp.Name}: {bp.Describe()}", Hud.Kind.Ok);
            _m.Hud.Float($"чертёж: {bp.Name}", Ornament.Moss);
            Sound.Ui("mark");
            return;
        }
        if (Opt.Dig) { Designate(pick, true, "Снести"); return; }
        WandRelayPlan(pick);
        if (_wandTake.Count == 0) { Refuse("переложить можно только вашу кладку из другого материала"); return; }
        var w = _m.World;
        double sec = Labour.Estimate(w, _wandTake, true, _wandCut) + Labour.Estimate(w, _wandPlan, false, _est);
        int n = _m.Sim.Relay(pick, Opt.Material);
        if (n == 0) { Refuse("это уже размечено"); return; }
        _markName = "Переложить";
        _m.Hud.Log($"Переложить в {Into(Opt.Material)}: {RelaySummary(sec)}");
        Marked("Переложить", _wandTake, true, n);
    }
}
