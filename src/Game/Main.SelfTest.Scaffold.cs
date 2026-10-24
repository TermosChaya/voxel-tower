using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>The self-test of the builders' scaffolding and of thin decks walked on.</summary>
public partial class Main
{
    /// <summary>
    /// Scaffolding in the game: one of ours left up on a block of stone eight metres high with no way down lashes a
    /// column of it down the block's side (the panel says so), climbs down by it, and it is taken down after, the logs
    /// back in the stores; and a deck of planks a voxel thick over a ditch is ground to stand on.
    /// </summary>
    async Task SelfTestScaffold(Action<string, bool, string> check)
    {
        var sim = Sim; var w = World;
        if (sim.Alarm) sim.StandDown();
        foreach (var r in sim.Units) if (r.Team == Team.Horde && !r.Dead) { r.Dead = true; r.DiedAt = -999; }
        int cx = (Site.KeepX - Site.KeepR - 140) & ~7, cz = (Site.KeepZ - 130) & ~7;
        ClearTrees(cx - 48, cz - 48, cx + 72, cz + 72);
        int g = 0;
        for (int z = cz; z < cz + 24; z++) for (int x = cx; x < cx + 24; x++) g = Math.Max(g, w.SurfAt(x, z));
        for (int z = cz; z < cz + 24; z++) for (int x = cx; x < cx + 24; x++) for (int y = w.SurfAt(x, z); y < g + 64; y++) w.Set(x, y, z, Mat.Stone);
        Advance(.2);
        sim.Stock[Stockpile.Wood] = Math.Max(sim.Stock[Stockpile.Wood], 20 * VoxelWorld.PerM3);
        var up = sim.Spawn(Kinds.Worker, sim.NearestStand(cx + 12, cz + 12, 2));
        (up.X, up.Y, up.Z) = sim.Nav.Feet(up.Cell);
        bool onTop = up.Y >= g + 62, raised = false, down = false;
        string said = "";
        bool Its() => sim.Scaffolds.Exists(s => s.By == up);
        for (int i = 0; i < 300 && !(down && !Its()); i++)
        {
            Advance(.5);
            foreach (var s in sim.Scaffolds) if (s.Escape && s.Voxels.Count > 200) raised = true;
            string doing = Retinue.DoingNow(up, sim);
            if (doing.Contains("леса")) said = doing;
            if (up.Y < g + 4) down = true;
        }
        int left = 0;
        for (int z = cz - 16; z < cz + 40; z++) for (int x = cx - 16; x < cx + 40; x++) for (int y = g - 8; y < g + 72; y++) if (w.Get(x, y, z) == Mat.Scaffold) left++;
        check("one of ours left up on a block eight metres high lashes scaffolding down its side, says so, climbs down by it, and it is taken down after",
            onTop && raised && down && left == 0 && !Its() && said != "",
            $"on top {onTop}, raised {raised}, down {down} (at {up.Y - g:0}), «{said}»; {left} voxels of it standing, its own still up {Its()}");
        up.Dead = true; up.DiedAt = -999;
        for (int z = cz; z < cz + 24; z++) for (int x = cx; x < cx + 24; x++) for (int y = g - 8; y < g + 64; y++) if (w.Get(x, y, z) == Mat.Stone) w.Set(x, y, z, Mat.Air);
        // a deck of planks a voxel thick over a ditch two metres across, its ends on the ground either side: ground to stand on
        int dx0 = cx, dz0 = cz + 50, dg = w.SurfAt(dx0 + 12, dz0 + 8);
        for (int z = dz0; z < dz0 + 16; z++) for (int x = dx0 - 8; x < dx0 + 32; x++) for (int y = dg - 16; y < dg + 16; y++)
            w.Set(x, y, z, y == dg - 1 ? Mat.Wood : y < dg - 1 && (x < dx0 + 4 || x >= dx0 + 20) ? Mat.Soil : Mat.Air);
        Advance(.2);
        int cell = sim.Nav.CellOf(dx0 + 12, dg + 1, dz0 + 8);
        bool ground = sim.Nav.Stand(sim.Nav.I(cell), sim.Nav.Y(cell), sim.Nav.K(cell), 2);
        string Cell(int c) => $"y{sim.Nav.Y(c)} fill {sim.Nav.Fill[c]} deck {sim.Nav.Deck[c]} floor {sim.Nav.Floor[c] - dg}";
        check("a deck of planks a voxel thick over a ditch is ground to stand on", ground,
            $"stands {ground}; deck at {dg - 1}: {Cell(cell + sim.Nav.Layer)} / {Cell(cell)} / {Cell(cell - sim.Nav.Layer)} / {Cell(cell - 2 * sim.Nav.Layer)}");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
