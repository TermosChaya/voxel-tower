using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The self-test's acid: an alchemist of the horde set down before a stone wall of ours far west of the citadel throws
    /// at it; its flasks break, the acid lies and eats, the glass is heard and the fumes and the stains are drawn. All of it
    /// cleared away after.
    /// </summary>
    async Task AcidSelfTest(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var w = World; var sim = Sim;
        int wx = Site.KeepX - Site.KeepR - 260, kz = Site.KeepZ + 40;
        int top = Shapes.LevelTop(w, wx, kz, 24);
        sim.InstantLabour = true;
        sim.DesignateBuild(Shapes.Line(w, Site, wx, kz - 16, wx, kz + 16, new Stroke { Mat = Mat.Stone, Width = 8, Height = 24, Level = true }, top));
        sim.InstantLabour = false;
        var alch = sim.Spawn(Kinds.Alchemist, sim.NearestStand(wx - 100, kz, 2));
        alch.Guard = true; alch.Home = alch.Cell; alch.Hp = alch.MaxHp = 1e5f;
        int broken0 = sim.FlasksBroken, ate0 = sim.AcidAte;
        Rig.Focus(w.WX(wx - 20), w.WZ(kz));
        Advance(14);
        await Frames(20);
        int laid = sim.Acids.Count, etched = sim.Etched.Count;
        check("an alchemist throws flasks of acid at a wall of ours: they break, the acid lies and eats it, the glass is heard",
            sim.FlasksBroken > broken0 && (laid > 0 || sim.AcidAte > ate0) && sim.AcidAte > ate0 && Sound.Heard.Contains("glass"),
            $"{sim.FlasksBroken - broken0} broken, {laid} voxels of acid lying, {etched} being eaten, {sim.AcidAte - ate0} eaten through");
        alch.Dead = true; alch.DiedAt = -999;
        foreach (int p in sim.Acids.ToList()) { var (x, y, z) = WaterPlanner.Unpack(p); w.Set(x, y, z, Mat.Air); }
        sim.Arrows.RemoveAll(a => a.Kind == VoxelTower.Core.Shot.Flask);
        await Frames(2);
    }
}
