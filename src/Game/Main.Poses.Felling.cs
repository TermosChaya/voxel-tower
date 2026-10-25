using System;
using System.Collections.Generic;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// Felling as woodcutters do it (--pose=felling, --at=part of the way over, .5 by itself; --look=1 from its other side): a pine on the level ground
    /// by the citadel, marked to be felled, a worker at it and three more about it at their own work; the game stopped
    /// as the tree goes over, the way its feller chose, away from all of them, the feller at its back and its
    /// «Берегись!» over it.
    /// </summary>
    void FellingPose()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9;
        var (gx, gz) = GroveAt;
        var (cx, cz) = DryLevel(gx + 150, gz + 110, 60);
        double part = _poseSeconds is > 0 and < 1 ? _poseSeconds : .5;
        // (where a pine has room to grow)
        for (int i = 0; i < 40 && !Flora.Pine(w, cx, cz, 104); i++) cx += 8;
        int g = w.SurfAt(cx, cz);
        for (int y = g; y < g + 8; y++)
            if (Mat.IsTrunk(w.Get(cx, y, cz))) { sim.DesignateRemove(Shapes.Tree(w, cx, y, cz)); break; }
        Unit Put(int dx, int dz)
        {
            var u = sim.Spawn(Kinds.Worker, sim.NearestStand(cx + dx, cz + dz, Kinds.Worker.Move.Height));
            u.Hold = true; u.Dest = u.Post = u.Cell;
            return u;
        }
        // the others about it, kept to where they stand (the feller chooses the way that misses them)
        Put(70, 10); Put(-30, 60); Put(10, -64);
        var feller = sim.Spawn(Kinds.Worker, sim.NearestStand(cx - 12, cz, Kinds.Worker.Move.Height));
        Toppling going = null;
        sim.Toppled += t => going ??= t;
        for (int i = 0; i < 2400 && going == null; i++) Advance(.05);
        if (going != null) Advance(Math.Max(0, (going.To - going.From) * part - .05));
        Paused = true;
        // from high up beside it, square to the way it goes, the whole of it in sight and the ground it goes down on
        float dx = going?.DX ?? 1, dz = going?.DZ ?? 0;
        int side = _look == 1 ? 1 : -1;
        Rig.SetAt(w.WX(cx + dx * 44), w.WY(g), w.WZ(cz + dz * 44), MathF.Atan2(-dz * side, dx * side), .95f, 1.5f);
        Hud.Refresh();
    }

    /// <summary>The column nearest a point with dry ground about it, level within a few voxels, so far round.</summary>
    (int x, int z) DryLevel(int x0, int z0, int round) => Nearest(x0, z0, 200, (x, z) =>
    {
        var w = World;
        int g = w.SurfAt(x, z);
        for (int dz = -round; dz <= round; dz += 8)
            for (int dx = -round; dx <= round; dx += 8)
            {
                int px = x + dx, pz = z + dz;
                if (!w.InBounds(px, 2, pz) || w.Get(px, w.SurfAt(px, pz), pz) == Mat.Water || w.Get(px, w.SurfAt(px, pz) - 1, pz) == Mat.Water
                    || Math.Abs(w.SurfAt(px, pz) - g) > 6) return false;
            }
        return true;
    });

    /// <summary>
    /// A pit dug as a careful hand digs it (--pose=pit): a shaft three metres across and four deep marked on the level
    /// ground by the citadel and dug by three workers, from its edge down, nobody digging out the ground under its own
    /// feet; the way out they make for themselves (steps cut up its side, or scaffolding where steps cannot be cut);
    /// one of them at its bottom, one on the way up, one at the edge.
    /// </summary>
    void PitPose()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9;
        var (gx, gz) = GroveAt;
        var (x0, z0) = DryLevel(gx + 140, gz + 100, 64);
        // bare of bushes, and earth all the way down
        for (int z = z0 - 64; z < z0 + 88; z++)
            for (int x = x0 - 64; x < x0 + 88; x++)
                for (int y = w.GroundTop(x, z) - 2, y1 = Math.Min(w.SY, y + 60); y < y1; y++)
                    if (Mat.IsTrunk(w.Get(x, y, z)) || Mat.IsLeafy(w.Get(x, y, z))) w.Set(x, y, z, Mat.Air);
        int top = w.SurfAt(x0 + 12, z0 + 12);
        for (int z = z0; z < z0 + 24; z++)
            for (int x = x0; x < x0 + 24; x++)
                for (int y = top - 34; y < w.SurfAt(x, z) - 1; y++)
                    if (Mat.Def[w.Get(x, y, z)].Group == MatGroup.Rock) w.Set(x, y, z, Mat.Soil);
        var shaft = new VoxelList();
        for (int z = z0; z < z0 + 24; z++)
            for (int x = x0; x < x0 + 24; x++)
                for (int y = w.SurfAt(x, z) - 1; y >= top - 32; y--)
                    if (Labour.Diggable(w.Get(x, y, z))) shaft.Add(x, y, z, Mat.Air);
        sim.DesignateRemove(shaft);
        var crew = new List<Unit>();
        for (int i = 0; i < 3; i++) crew.Add(sim.Spawn(Kinds.Worker, sim.NearestStand(x0 + 12 + (i - 1) * 16, z0 + 40, Kinds.Worker.Move.Height)));
        bool Marked()
        {
            for (int y = top - 40; y < top + 8; y++) for (int z = z0 - 120; z < z0 + 144; z++) for (int x = x0 - 120; x < x0 + 144; x++) if (sim.Orders.At(x, y, z) != 0) return true;
            return false;
        }
        for (int s = 0; s < 120 && Marked(); s++) Advance(5);
        // one at the bottom, one on the way up, one at the edge, as they come and go
        int bottom = sim.NearestStand(x0 + 12, z0 + 12, Kinds.Worker.Move.Height);
        foreach (var u in crew) { u.Hold = true; u.Site = -1; u.Jobs.Clear(); }
        crew[0].Cell = bottom; (crew[0].X, crew[0].Y, crew[0].Z) = sim.Nav.Feet(bottom); crew[0].Dest = crew[0].Post = bottom;
        crew[1].Cell = bottom; (crew[1].X, crew[1].Y, crew[1].Z) = sim.Nav.Feet(bottom);
        crew[1].Dest = crew[1].Post = sim.NearestStand(x0 + 12, z0 + 60, Kinds.Worker.Move.Height);
        Advance(2.2);
        Paused = true;
        Rig.SetAt(w.WX(x0 + 12), w.WY(top - 16), w.WZ(z0 + 12), -.75f, 1.0f, 3.4f);
        Hud.Refresh();
    }
}
