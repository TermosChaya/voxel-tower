using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>The set-ups for a look at the builders' scaffolding and at thin decks walked on (--pose=scaffold).</summary>
public partial class Main
{
    /// <summary>
    /// Scaffolding at work (--pose=scaffold), on the level ground beside the citadel: a round stone tower eight metres
    /// high marked, and four workers raising it, putting up scaffolding beside it once its top is out of their reach
    /// (--at= seconds of the work, 75 by itself); --look=1 one of ours left on top of a block of stone eight metres high
    /// lashing a column of scaffolding down its side (--at=, 30 by itself; 45 climbing down by it); --look=2 a deck of
    /// planks a voxel thick over a ditch, a worker walking across it.
    /// </summary>
    void Scaffolding()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9;
        sim.Stock[Stockpile.Wood] = sim.Stock[Stockpile.Stone] = 4000.0 * VoxelWorld.PerM3;
        var (gx, gz) = GroveAt;
        int cx = (gx + 150) & ~7, cz = (gz + 110) & ~7, g = w.SurfAt(cx, cz);
        bool given = _poseSeconds != 14;
        Unit Put(int dx, int dz) => sim.Spawn(Kinds.Worker, sim.NearestStand(cx + dx, cz + dz, 2));
        if (_look == 1)
        {
            for (int z = cz; z < cz + 24; z++) for (int x = cx; x < cx + 24; x++) for (int y = g; y < g + 64; y++) w.Set(x, y, z, Mat.Stone);
            Advance(.2);
            var up = Put(12, 12);
            (up.X, up.Y, up.Z) = sim.Nav.Feet(up.Cell);
            Advance(given ? _poseSeconds : 30);
            Rig.SetAt(w.WX(cx + 20), w.WY(g + 30), w.WZ(cz + 10), -.9f, .42f, 1.7f);
            Selected.Add(up); Hud.OnSelectionChanged();
            Hud.Refresh();
            return;
        }
        if (_look == 2)
        {
            // a ditch two metres deep right across, and a deck of planks a voxel thick over it
            for (int z = cz - 60; z < cz + 60; z++) for (int x = cx; x < cx + 24; x++) for (int y = g - 16; y < g; y++) w.Set(x, y, z, Mat.Air);
            for (int z = cz - 12; z < cz + 12; z++) for (int x = cx - 8; x < cx + 32; x++) w.Set(x, g - 1, z, Mat.Wood);
            Advance(.2);
            var walker = Put(-30, 0);
            walker.Hold = true;
            sim.GoTo(walker, sim.NearestStand(cx + 60, cz, 2));
            Advance(given ? _poseSeconds : 1.5);
            Rig.SetAt(w.WX(cx + 12), w.WY(g), w.WZ(cz), -.75f, .5f, 3.2f);
            Hud.Refresh();
            return;
        }
        var tower = Shapes.Circle(w, Site, cx, cz, 24, new Stroke { Mat = Mat.Stone, Width = 6, Height = 64, Level = true, Hollow = true }, Shapes.LevelTop(w, cx, cz, 64));
        sim.DesignateBuild(tower);
        for (int i = 0; i < 4; i++) Put(-40 + i * 10, -40);
        Advance(given ? _poseSeconds : 75);
        Rig.SetAt(w.WX(cx), w.WY(g + 30), w.WZ(cz), -.7f, .45f, 1.25f);
        Hud.Refresh();
    }
}
