using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The gods (--pose=god): the Archangel called down into the open ground west of the citadel with a band of the horde
    /// coming at it. --look=0 its sword through the band, halfway round; 1 it comes down in the light; 2 the pillar of
    /// heaven comes down on the band; 3 it stands; 4 the Great Toad against it; 5 the player in it, the sword going
    /// through the band below; 6 the Great Toad alone; 7 the intervention's page of the tree with every circle learnt to
    /// its end, the appearance at its heart to be learnt; 8 the toad's tongue out at a file of ours; 9 the Archangel in
    /// flight over the band.
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
        bool toad = _look is 6 or 8;
        // a band of the horde coming from the west, or for the toad a file of ours
        int band = _look is 3 or 6 ? 0 : 42;
        for (int i = 0; i < band; i++)
        {
            var t = toad ? (i % 3 == 0 ? Kinds.Archer : Kinds.Swordsman) : i % 7 == 0 ? Kinds.Troll : i % 3 == 0 ? Kinds.Orc : Kinds.Goblin;
            int c = sim.NearestStand(gx - (int)(22 * M) - i % 6 * 12, gz - 50 + i / 6 * 14, t.Move.Height);
            var u = sim.Spawn(t, c);
            (u.X, u.Y, u.Z) = sim.Nav.Feet(c);
            if (toad) { u.Hold = true; u.Dest = u.Cell; u.Post = u.Cell; } else { u.Guard = true; u.Home = u.Cell; }
        }
        if (!toad && band > 0) sim.Spawn(Kinds.Ram, sim.NearestStand(gx - (int)(30 * M), gz + 60, 2)).Guard = true;
        // (the toad to the archangel's side, well inside the valley)
        if (_look == 4) sim.Awaken(DeityKind.Toad, gx, gz + 45 * M, lasts: 600);
        sim.Awaken(toad ? DeityKind.Toad : DeityKind.Archangel, gx, gz, lasts: 600);
        var god = sim.Gods.Find(g => !g.Gone && g.T.Kind == (toad ? DeityKind.Toad : DeityKind.Archangel));
        void Run(double s) { for (double t = 0; t < s; t += .05) sim.Step(.05f); }
        if (_look == 1) Run(5.5);
        else
        {
            Run(Sim.GodRiseFor + .3);
            // (the archangel turned to the band; the toad left facing whatever it went for)
            if (!toad) god.Facing = MathF.PI;
            if (_look == 5)
            {
                Gods.Enter(god);
                Gods.Face(MathF.PI, -.55f);
                sim.GodWishSweep = 1;
                Run(.1);
                while (god.Act == DeityAct.Sweep && god.Into(sim.Time) < 1.2) sim.Step(.05f);
            }
            else if (_look is 0 or 2 or 9)
            {
                sim.Ride(god);
                sim.GodLookYaw = MathF.PI;
                if (_look == 0) sim.GodWishSweep = 1;
                else if (_look == 2) { sim.GodWishSlam = true; sim.GodAimX = gx - 26 * M; sim.GodAimZ = gz; sim.GodAimY = w.SurfAt((int)sim.GodAimX, gz); }
                else { sim.GodWishLeap = true; sim.GodAimX = gx - 50 * M; sim.GodAimZ = gz + 10 * M; sim.GodAimY = w.SurfAt((int)sim.GodAimX, (int)sim.GodAimZ); }
                Run(.1);
                double at = _look == 0 ? 1.25 : _look == 2 ? Sim.SlamHit + .08 : Sim.LeapCrouch + Sim.FlightFor * .45;
                while (god.Act is DeityAct.Sweep or DeityAct.Slam or DeityAct.Leap && god.Into(sim.Time) < at) sim.Step(.05f);
            }
            else if (_look == 8)
            {
                while (god.Act != DeityAct.Tongue && sim.Time < 400) sim.Step(.05f);
                while (god.Act == DeityAct.Tongue && god.Into(sim.Time) < Sim.TongueGrab + .05) sim.Step(.05f);
            }
            else if (_look == 4) Run(6);
        }
        Paused = true;
        _shotFrames = Math.Max(_shotFrames, 70);
        // (the camera stands off toward (sin yaw, cos yaw) of what it looks at: before the god, which faces west, it is west)
        float y = w.WY(w.SurfAt(gx, gz)) + (_look == 1 ? 26 : 18);
        float yaw = _look == 4 ? -1.2f : -1.05f;
        if (_cam is { Length: > 0 }) yaw = _cam[0];
        Rig.SetAt(w.WX(gx - 8 * M), y, w.WZ(gz + (_look == 4 ? 22 * M : 0)), yaw, .38f, .3f);
        Hud.Refresh();
    }
}
