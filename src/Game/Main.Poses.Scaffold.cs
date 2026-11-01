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
    /// planks a voxel thick over a ditch, a worker walking across it; --look=3 scaffolding twelve metres high whose lower
    /// half the player marked to be taken out, coming down whole from the top (--at=, 5 by itself); --look=4 scaffolding
    /// burnt through at its foot, what was over it falling (--at=, half a second by itself).
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
        if (_look is 3 or 4)
        {
            // a column of scaffolding as the workers raise it: a pole at each corner, a ring of boards at each cell's middle
            int i = cx >> NavGrid.Shift, k = cz >> NavGrid.Shift, y0 = g >> NavGrid.Shift, y1 = y0 + (_look == 3 ? 11 : 7);
            var s = new Scaffold { Done = sim.Time, Used = sim.Time, Site = k * sim.Nav.NX + i };
            int x0 = i << NavGrid.Shift, z0 = k << NavGrid.Shift, e = NavGrid.CS - 1;
            for (int y = y0 << NavGrid.Shift; y < (y1 + 1) << NavGrid.Shift; y++)
                for (int z = z0; z <= z0 + e; z++)
                    for (int x = x0; x <= x0 + e; x++)
                    {
                        bool pole = (x == x0 || x == x0 + e) && (z == z0 || z == z0 + e);
                        bool board = (y & e) == 3 && (x == x0 || x == x0 + e || z == z0 || z == z0 + e);
                        if ((pole || board) && w.Get(x, y, z) == Mat.Air) { w.Set(x, y, z, Mat.Scaffold); s.Voxels.Add(WaterPlanner.Pack(x, y, z)); }
                    }
            s.Columns.Add((i, k, y0, y1, false));
            s.From.Add(sim.Nav.Id(i, y0, k));
            sim.Scaffolds.Add(s);
            sim.Nav.Flush();
            Advance(1.2);
            if (_look == 3)
            {
                Put(-24, 8);
                var lower = new VoxelList();
                foreach (int p in s.Voxels) { var (x, y, z) = WaterPlanner.Unpack(p); if (y < g + 48) lower.Add(x, y, z, 0); }
                sim.DesignateRemove(lower);
                Advance(given ? _poseSeconds : 5);
            }
            else
            {
                for (int y = g; y < g + 9; y++) for (int z = z0; z <= z0 + e; z++) for (int x = x0; x <= x0 + e; x++) if (w.Get(x, y, z) == Mat.Scaffold) w.Set(x, y, z, Mat.Air);
                Advance(given ? _poseSeconds : 1.6);
            }
            // (held still, so the picture is of that moment)
            Paused = true;
            Rig.SetAt(w.WX(cx + 4), w.WY(g + (_look == 3 ? 40 : 24)), w.WZ(cz + 4), -.8f, .4f, _look == 3 ? 2.4f : 3f);
            Hud.Refresh();
            return;
        }
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

    /// <summary>
    /// The old fort of a campaign's first valley mended (--campaign --pose=mended): the whole of it marked with the mend
    /// as the player marks it, its gate hung, the household given stone and timber for it, and the gate seen from outside
    /// with the wall over it standing (--at= seconds of the mending, 150 by itself).
    /// </summary>
    void MendedPose()
    {
        var sim = Sim; var w = World; var g = sim.FortGate;
        if (g == null) return;
        // (the fort found from the wall beside the gate's left jamb, as a click there finds it)
        int u = g.U0 - 3, a = g.P - g.In * 4;
        var fort = sim.BuildingFrom(sim.MeantNear(g.AlongX ? u : a, g.Y + 8, g.AlongX ? a : u));
        var lay = new VoxelList(); var clear = new VoxelList();
        sim.MendPlan(fort, lay, clear);
        sim.Mend(lay, clear);
        sim.MendGate(g);
        sim.Stock[Stockpile.Stone] += 400 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] += 100 * VoxelWorld.PerM3;
        Advance(_poseSeconds == 14 ? 150 : _poseSeconds);
        int cu = g.U0 + Gateway.Width / 2, ca = g.P - g.In * 12;
        float yaw = g.AlongX ? MathF.Atan2(0, g.In) : MathF.Atan2(g.In, 0);
        Rig.SetAt(w.WX(g.AlongX ? cu : ca), w.WY(g.Y + 8), w.WZ(g.AlongX ? ca : cu), yaw + .5f, .38f, 2.8f);
        Hud.Refresh();
    }
}
