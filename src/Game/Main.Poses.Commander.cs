using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// A commander and its people (--pose=commander): a stone wall of ours west of the citadel with a ladder up its inner
    /// face, a band of the horde standing beyond it, a commander inside with swordsmen and archers. --look=0 from above,
    /// the commander chosen: the swordsmen formed up inside the wall, the archers on it, the ring of how far its word
    /// reaches; 1 in its eyes, the four calls over the keys, «Залп!» just called and the arrows in the air; 2 the
    /// swordsmen chosen and sent with a right click, the rings where they go; 3 a worker gone into before a wall marked
    /// out, laying it, the mark brass; 4 a swordsman gone into, its shield up, a goblin before it; 5 the same, a heavy
    /// blow drawn back.
    /// </summary>
    void CommanderPose()
    {
        var w = World; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.Stock[Stockpile.Stone] = 60 * VoxelWorld.PerM3;
        int wx = Site.KeepX - Site.KeepR - 90, kz = Site.KeepZ;
        ClearTrees(wx - 200, kz - 90, wx + 80, kz + 90);
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        int top = WallWest(-90, 32, 60), g = w.SurfAt(wx + 30, kz);
        // the ladder up its inner face, built
        int face = wx;
        while (Mat.IsMasonry(w.Get(face + 1, top - 12, kz))) face++;
        sim.InstantLabour = true;
        sim.DesignateBuild(Shapes.LadderAt(w, Site, new VoxelHit { X = face, Y = top - 16, Z = kz + 24, NX = 1, M = Mat.Stone }));
        sim.CompleteAll();
        sim.InstantLabour = false;
        Advance(.3);

        if (_look is 3) { CommanderPoseWorker(face, top, kz); return; }
        if (_look is 4 or 5) { CommanderPoseGuard(face, kz); return; }

        var cap = sim.Spawn(Kinds.Captain, sim.NearestStand(face + 64, kz, 2));
        var men = new List<Unit>();
        for (int i = 0; i < 6; i++) men.Add(sim.Spawn(Kinds.Swordsman, sim.NearestStand(face + 50 + i % 3 * 10, kz + 30 + i / 3 * 10, 2)));
        for (int i = 0; i < 3; i++) men.Add(sim.Spawn(Kinds.Archer, sim.NearestStand(face + 50 + i * 10, kz - 34, 2)));
        Advance(1);
        foreach (var (t, dz) in new[] { (Kinds.Goblin, -24), (Kinds.Orc, -8), (Kinds.Goblin, 6), (Kinds.Goblin, 20), (Kinds.Troll, 0), (Kinds.Orc, 34) })
        {
            var o = Camp(t, wx - 150 + Math.Abs(dz) / 2, kz + dz);
            o.ChainedUntil = 1e9;
        }
        // the commander looks the field over and forms them up
        Advance(14);

        if (_look == 1)
        {
            FP.Enter(cap);
            // towards the troll beyond the wall, over the archers on it
            var foe = sim.Units.Find(u => u.Team == Team.Horde && !u.Dead && u.T == Kinds.Troll);
            float dx = foe.X - cap.X, dz = foe.Z - cap.Z;
            FP.Face(MathF.Atan2(dz, dx), .16f);
            Advance(.1);
            FP.CallOut(1);
            FP.CallOut(2);
            Advance(.45);
            Paused = true;
            Hud.Refresh();
            return;
        }
        if (_look == 2)
        {
            // the swordsmen sent forward with a right click, a little before the picture
            foreach (var u in men) if (u.T == Kinds.Swordsman) Selected.Add(u);
            Hud.OnSelectionChanged();
            Rig.SetAt(w.WX(face + 40), w.WY(g), w.WZ(kz - 6), .37f, .78f, 2.4f);
            _poseLater = () =>
            {
                var at = Rig.Cam.UnprojectPosition(new Vector3(w.WX(face + 60), w.WY(w.SurfAt(face + 60, kz - 26)), w.WZ(kz - 26)));
                Command.OrderTo(at);
            };
            Hud.Refresh();
            return;
        }
        Selected.Add(cap);
        Hud.OnSelectionChanged();
        Rig.SetAt(w.WX(face + 10), w.WY(g), w.WZ(kz), .37f, .8f, 1.25f);
        Hud.Refresh();
    }

    /// <summary>A worker gone into before a stretch of wall marked out west of the wall, laying it, the mark brass.</summary>
    void CommanderPoseWorker(int face, int top, int kz)
    {
        var w = World; var sim = Sim;
        int x = face - 70;
        var plan = Shapes.Line(w, Site, x, kz - 30, x, kz + 30, new Stroke { Mat = Mat.Stone, Width = 8, Height = 16, Level = true }, Shapes.LevelTop(w, x, kz, 16));
        sim.DesignateBuild(plan);
        var me = sim.Spawn(Kinds.Worker, sim.NearestStand(x - 26, kz - 6, 2));
        Advance(.3);
        FP.Enter(me);
        // looking down at the foot of the marked wall before it, the wall of stone behind, laying it
        FP.Face(.12f, -.5f);
        sim.WishLay = true;
        Advance(6);
        Paused = true;
        Hud.Refresh();
    }

    /// <summary>A swordsman gone into, its shield up before a goblin; with --look=5 a heavy blow drawn back.</summary>
    void CommanderPoseGuard(int face, int kz)
    {
        var sim = Sim;
        var me = sim.Spawn(Kinds.Swordsman, sim.NearestStand(face + 60, kz, 2));
        var foe = Camp(Kinds.Goblin, face + 40, kz + 2);
        foe.ChainedUntil = 1e9;
        Advance(.3);
        FP.Enter(me);
        float dx = foe.X - me.X, dz = foe.Z - me.Z;
        float face2 = MathF.Atan2(foe.Y + foe.T.BodyH * .7f - (me.Y + me.T.BodyH * .88f), MathF.Sqrt(dx * dx + dz * dz));
        FP.Face(MathF.Atan2(dz, dx), Math.Clamp(face2, -.6f, .05f));
        if (_look == 5) { sim.WishStrike = true; Advance(.6); }
        else { sim.WishGuard = true; Advance(.4); }
        Paused = true;
        Hud.Refresh();
    }
}
