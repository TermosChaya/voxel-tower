using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// Mines (--pose=countermine): a stone wall of ours west of the citadel, a worker of ours behind it, and a miner of the
    /// horde come at it from the west: --look=0 (--at=seconds, 70 by itself) it is under the ground, the earth heaped by
    /// the mouth, and the mark over its face trembling as ours hear the picks; --look=1 its mine dug, and our countermine
    /// dug across it and broken in, a swordsman of ours and the miner in it at the breach, the ground over them lifted off
    /// for the picture; --look=2 (--at=seconds after the firing, 1.2 by itself) the chamber under the wall, its posts
    /// alight, the ground before it lifted off; with --at=60 the wall come down into it; --look=3 (--at=seconds after the
    /// digging, 2.4 by itself) a round tower in the middle of the wall, the ground dug out under its outer half: it tears
    /// out of the wall and goes over.
    /// </summary>
    void Countermine()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9;
        int wx = Site.KeepX - Site.KeepR - 40, kz = Site.KeepZ;
        int top = WallWest(-40, 32, 60);
        // the rest of a ring of it round the citadel, so that the horde's way goes through the wall and not round it
        if (_look != 3)
        {
            sim.InstantLabour = true;
            sim.DesignateBuild(Shapes.Circle(w, Site, Site.KeepX, kz, Site.KeepR + 44, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Level = true, Hollow = true }, top));
            sim.InstantLabour = false;
            Advance(.5);
        }
        // the household kept in the citadel, out of the way
        foreach (var u in sim.Units) if (u.Team == Team.Player && !u.Dead) u.Inside = true;
        if (_look == 3)
        {
            int ground = w.SurfAt(wx - 24, kz);
            sim.InstantLabour = true;
            sim.DesignateBuild(Shapes.Circle(w, Site, wx, kz, 12, new Stroke { Mat = Mat.Stone, Width = 4, Height = 64, Level = true, Hollow = true }, ground + 64));
            sim.InstantLabour = false;
            Advance(1);
            for (int z = kz - 14; z <= kz + 14; z++) for (int x = wx - 16; x < wx + 4; x++) for (int y = ground - 6; y < ground; y++) w.Set(x, y, z, Mat.Air);
            Advance(_poseSeconds == 14 ? 2.4 : _poseSeconds);
            Paused = true;
            Rig.SetAt(w.WX(wx - 30), w.WY(ground + 12), w.WZ(kz + 10), -1.25f, .42f, 1.6f);
            Hud.Refresh();
            return;
        }
        sim.ComputeFields();
        // where the miner comes from: twenty-five metres out, or as far as the ground keeps the wall's height
        int ground0 = w.SurfAt(wx - 12, kz), from = wx - 12;
        while (from > wx - 200 && Math.Abs(w.SurfAt(from - 1, kz) - ground0) < 12) from--;
        int g = w.SurfAt((from + wx) / 2, kz);
        GD.Print($"[countermine] the wall at x {wx}, level ground out to {from}: {(wx - from) / VoxelWorld.PerMetre} m");
        if (_look == 1)
        {
            var theirs = sim.DigMineNow(wx - 170, kz, wx + 4, kz, 24, Team.Horde);
            var plan = sim.PlanCountermine(wx + 24, kz + 60, wx - 68, kz - 4, 24, out var ours);
            sim.InstantLabour = true;
            sim.DesignateRemove(plan);
            sim.InstantLabour = false;
            sim.AddCountermine(ours);
            Advance(1);
            int at = theirs.Cells.FindIndex(c => Math.Abs(sim.Nav.I(c) - ((wx - 68) >> NavGrid.Shift)) <= 0);
            if (at < 0) at = theirs.Cells.Count / 2;
            var near = theirs.Cells[Math.Min(theirs.Cells.Count - 1, at + 1)];
            var miner = sim.Spawn(Kinds.Miner, near); (miner.X, miner.Y, miner.Z) = sim.Nav.Feet(near); miner.Guard = true; miner.Home = near; miner.Hp = miner.MaxHp = 1e4f;
            var ourEnd = ours.Cells[^2];
            var sword = sim.Spawn(Kinds.Swordsman, ourEnd); (sword.X, sword.Y, sword.Z) = sim.Nav.Feet(ourEnd); sword.Hold = true; sword.Dest = sword.Post = ourEnd; sword.Hp = sword.MaxHp = 1e4f;
            Advance(.6);
            // the ground over the breach lifted off for the picture, down to the roofs of the bores
            var (bx, by, bz) = sim.Nav.Feet(theirs.Cells[Math.Max(0, at)]);
            int roof = (int)by + 2 * NavGrid.CS;
            for (int z = (int)bz - 40; z <= (int)bz + 28; z++)
                for (int x = (int)bx - 40; x <= (int)bx + 40; x++)
                    for (int y = roof; y < roof + 40; y++)
                    {
                        byte m = w.Get(x, y, z);
                        if (m != Mat.Air && !Mat.IsPlayer[m] && m != Mat.Prop) w.SetRaw(x, y, z, Mat.Air);
                    }
            Paused = true;
            Rig.SetAt(w.WX(bx), w.WY(by + 6), w.WZ(bz + 4), -.7f, .62f, 3.6f);
            Hud.Refresh();
            return;
        }
        var listener = sim.Spawn(Kinds.Worker, sim.NearestStand(wx + 30, kz + 6, 4));
        listener.Hold = true; listener.Dest = listener.Post = listener.Cell;
        var comer = sim.Spawn(Kinds.Miner, sim.NearestStand(Math.Max(from + 8, wx - 200), kz, 2));
        comer.Hp = comer.MaxHp = 1e5f;
        if (_look == 2)
        {
            Tunnel mine = null;
            for (int i = 0; i < 9000 && mine is not { State: TunnelState.Fired }; i++) { Advance(.1); mine ??= sim.Tunnels.Find(t => t.Team == Team.Horde); }
            GD.Print($"[countermine] at {sim.Time:0} s the mine {mine?.State}, {mine?.Dug} of {mine?.Cells.Count} steps, posts {mine?.PostsSet}");
            Advance(_poseSeconds == 14 ? 1.2 : _poseSeconds);
            // the ground before the wall lifted off over the chamber, down to its floor, for the picture
            if (mine != null)
            {
                var (ex, ey, ez) = sim.Nav.Feet(mine.Cells[^1]);
                for (int z = (int)ez - 24; z <= (int)ez + 24; z++)
                    for (int x = (int)ex - 30; x < (int)ex - 4; x++)
                        for (int y = (int)ey; y < (int)ey + 40; y++)
                        {
                            byte m = w.Get(x, y, z);
                            if (m != Mat.Air && !Mat.IsPlayer[m] && m != Mat.Prop) w.SetRaw(x, y, z, Mat.Air);
                        }
                // the wall come down: seen from higher, over the smoke
                if (_poseSeconds >= 30) Rig.SetAt(w.WX(ex), w.WY(top - 8), w.WZ(ez), -1.2f, .75f, 3.6f);
                else Rig.SetAt(w.WX(ex - 6), w.WY(ey + 8), w.WZ(ez + 2), -1.45f, .38f, 3.2f);
            }
            Paused = true;
            Hud.Refresh();
            return;
        }
        Advance(_poseSeconds == 14 ? 70 : _poseSeconds);
        Rig.SetAt(w.WX((from + wx) / 2), w.WY(g), w.WZ(kz + 6), -1.05f, .62f, 1.6f);
        Hud.Refresh();
    }
}
