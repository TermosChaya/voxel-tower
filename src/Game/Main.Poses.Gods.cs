using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The gods (--pose=god): the Colossus called up in the open ground west of the citadel with a band of the horde
    /// coming at it. --look=0 it sweeps through the band, the hand halfway round; 1 it breaks up out of the ground, the
    /// earth going up about it; 2 its fist comes down on the band; 3 it stands; 4 the Devourer against it; 5 the player
    /// in it, its hand going through the band below; 6 the Devourer alone, standing; 7 the intervention's page of the
    /// tree with every circle learnt to its end, the waking at its heart to be learnt.
    /// </summary>
    void GodPose()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        if (_look == 7)
        {
            foreach (var l in Sim.LoresOn(2)) if (l.Tier < Sim.Heart) { if (l.Spell is { } k) sim.Known.Add(k); else sim.Grant(l); }
            sim.Revelations = 9; sim.Favour = 150;
            Hud.OpenPanel("lore");
            Hud.Lore.ShowPage(2);
            return;
        }
        const float M = VoxelWorld.PerMetre;
        int gx = Site.KeepX - Site.KeepR - (int)(46 * M), gz = Site.KeepZ;
        ClearTrees(gx - 420, gz - 260, gx + 160, gz + 260);
        // a band of the horde coming from the west (not for it standing alone)
        for (int i = 0; i < (_look is 3 or 6 ? 0 : 42); i++)
        {
            var t = i % 7 == 0 ? Kinds.Troll : i % 3 == 0 ? Kinds.Orc : Kinds.Goblin;
            int c = sim.NearestStand(gx - (int)(22 * M) - i % 6 * 12, gz - 50 + i / 6 * 14, t.Move.Height);
            var u = sim.Spawn(t, c);
            (u.X, u.Y, u.Z) = sim.Nav.Feet(c);
            u.Guard = true; u.Home = u.Cell;
        }
        if (_look is not (3 or 6)) sim.Spawn(Kinds.Ram, sim.NearestStand(gx - (int)(30 * M), gz + 60, 2)).Guard = true;
        if (_look == 4) sim.Awaken(DeityKind.Devourer, gx - 40 * M, gz, lasts: 600);
        if (_look == 6) sim.Awaken(DeityKind.Devourer, gx, gz, lasts: 600);
        else sim.Awaken(DeityKind.Colossus, gx, gz, lasts: 600);
        var god = sim.Gods.Find(g => !g.Gone && (_look == 6 || g.Team == Team.Player));
        void Run(double s) { for (double t = 0; t < s; t += .05) sim.Step(.05f); }
        if (_look == 1) Run(5.2);
        else
        {
            Run(Sim.GodRiseFor + .3);
            god.Facing = MathF.PI;
            if (_look == 5)
            {
                Gods.Enter(god);
                Gods.Face(MathF.PI, -.55f);
                sim.GodWishSweep = 1;
                Run(.1);
                while (god.Act == DeityAct.Sweep && god.Into(sim.Time) < 1.2) sim.Step(.05f);
            }
            else if (_look is 0 or 2)
            {
                sim.Ride(god);
                sim.GodLookYaw = MathF.PI;
                if (_look == 0) sim.GodWishSweep = 1;
                else { sim.GodWishSlam = true; sim.GodAimX = gx - 16 * M; sim.GodAimZ = gz; sim.GodAimY = w.SurfAt((int)sim.GodAimX, gz); }
                Run(.1);
                double at = _look == 0 ? 1.25 : Sim.SlamHit + .08;
                while (god.Act is DeityAct.Sweep or DeityAct.Slam && god.Into(sim.Time) < at) sim.Step(.05f);
            }
            else if (_look == 4) Run(6);
        }
        Paused = true;
        _shotFrames = Math.Max(_shotFrames, 70);
        // from before it and a little to its side, far enough back to see the whole of it
        float y = w.WY(w.SurfAt(gx, gz)) + 18;
        // (the camera stands off toward (sin yaw, cos yaw) of what it looks at: before the god, which faces west, it is west)
        float yaw = _look == 4 ? -1.2f : -1.05f;
        if (_cam is { Length: > 0 }) yaw = _cam[0];
        Rig.SetAt(w.WX(gx - 8 * M), y, w.WZ(gz), yaw, .38f, .3f);
        Hud.Refresh();
    }
}
