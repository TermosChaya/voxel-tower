using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// Felling on the screen: a pine grown on a field of turf evened out west of the citadel, marked to be felled, with a worker set to
    /// it and two more at their posts about it. Before its last blows the feller calls «Берегись!»: the word shows over it
    /// and the shout is heard; the tree goes over as it was felled, onto none of ours. Cleared away after.
    /// </summary>
    async Task SelfTestFelling(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var w = World; var sim = Sim;
        int cx = Site.KeepX - Site.KeepR - 420, cz = Site.KeepZ + 120, g = w.SurfAt(cx, cz);
        ClearTrees(cx - 120, cz - 120, cx + 120, cz + 120);
        for (int z = cz - 110; z <= cz + 110; z++)
            for (int x = cx - 110; x <= cx + 110; x++)
                for (int y = g - 6; y < g + 120; y++)
                {
                    byte want = y < g ? (y == g - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
        if (sim.Alarm) sim.StandDown();
        Advance(.5);
        Flora.Pine(w, cx, cz, 96);
        VoxelList tree = null;
        for (int y = g; y < g + 8 && tree == null; y++)
            if (Mat.IsTrunk(w.Get(cx, y, cz))) tree = Shapes.Tree(w, cx, y, cz);
        if (tree == null) { check("a pine grows for the felling test", false, $"none at {cx},{cz}"); return; }
        sim.DesignateRemove(tree);
        Unit Put(int dx, int dz, bool hold)
        {
            var u = sim.Spawn(Kinds.Worker, sim.NearestStand(cx + dx, cz + dz, Kinds.Worker.Move.Height));
            if (hold) { u.Hold = true; u.Dest = u.Post = u.Cell; }
            return u;
        }
        var feller = Put(-12, 0, false);
        var by = new[] { Put(64, 8, true), Put(-24, 56, true) };
        Rig.SetAt(w.WX(cx), w.WY(g), w.WZ(cz), -.6f, .8f, 1.6f);
        int shouts = 0, heard0 = Sound.Played;
        bool shown = false, heard = false;
        sim.Shouted += (_, word) => { if (word == "Берегись!") shouts++; };
        Toppling going = null;
        int onto = 0;
        sim.Toppled += t =>
        {
            going ??= t;
            int top = t.Voxels.Count == 0 ? 0 : t.Voxels.Max(v => v.y) - (int)t.PY;
            foreach (var u in sim.Units)
            {
                if (u.Dead || u.Inside || u.Team != Team.Player) continue;
                float ax = u.X - t.PX, az = u.Z - t.PZ, along = ax * t.DX + az * t.DZ, across = MathF.Abs(ax * t.DZ - az * t.DX);
                if (along > 0 && along < top + 4 && across < 12) onto++;
            }
        };
        for (int i = 0; i < 600 && going == null; i++)
        {
            Advance(.2);
            await Frames(1);
            shown |= Shouts.Showing.Contains("Берегись!");
            heard |= shouts > 0 && Sound.Heard.Contains("warn");
        }
        // (the word stays a moment over the feller once called)
        for (int i = 0; i < 4 && !shown; i++) { await Frames(1); shown |= Shouts.Showing.Contains("Берегись!"); }
        check("a worker fells a pine: it calls «Берегись!» before the last blows (the word over it, the shout heard), and the tree goes over as felled onto none of ours",
            going is { Felled: true } && shouts > 0 && shown && heard && onto == 0,
            $"{(going == null ? "not felled" : $"felled {going.Felled}, the way {going.DX:0.0},{going.DZ:0.0}")}; {shouts} called, shown {shown}, heard {heard} ({Sound.Played - heard0} sounds); {onto} of ours in its way");
        Advance(4);
        foreach (var u in by.Append(feller)) { u.Dead = true; u.DiedAt = -999; }
        sim.CancelArea(cx - 140, cz - 140, cx + 140, cz + 140);
        ClearTrees(cx - 140, cz - 140, cx + 140, cz + 140);
        await Frames(2);
    }
}
