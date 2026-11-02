using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The citadel's castle as a new game finds it (--pose=castle), a few of the household about its yard and an archer on
    /// its gatehouse: --look=0 from high in the south-east, 1 from high in the north-west, 2 in the yard by the keep's door
    /// looking back at the gatehouse; 3…5 the same with all of it mended and its gate hung; 6 the gatehouse close from the
    /// road, mended; 7 straight down on it, mended. --bare hides the interface.
    /// </summary>
    void CastlePose()
    {
        var w = World; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.WildTarget = 0;
        foreach (var u in sim.Units) if (u.Team == Team.Wild) { u.Dead = true; u.DiedAt = -999; }
        StartingFort.Raise(sim);
        if (_look >= 3)
        {
            var (x0, z0, x1, z1) = sim.FortArea;
            var lay = new VoxelList(); var clear = new VoxelList();
            sim.InstantLabour = true;
            sim.MendPlan(sim.MeantIn(x0, z0, x1, z1), lay, clear);
            sim.Mend(lay, clear);
            if (sim.FortGate != null) sim.MendGate(sim.FortGate);
            sim.InstantLabour = false;
            sim.Decorate();
        }
        int kx = Site.KeepX, kz = Site.KeepZ, g = Site.KeepBase;
        foreach (var (t, dx, dz) in new[] { (Kinds.Worker, -52, -14), (Kinds.Worker, -46, -24), (Kinds.Worker, -30, 34), (Kinds.Swordsman, -36, 6), (Kinds.Archer, -60, 12) })
            sim.Spawn(t, sim.NearestStand(kx + dx, kz + dz, 2));
        // an archer up on the gatehouse
        int up = -1;
        for (int y = sim.Nav.NY - 1; y >= 1 && up < 0; y--)
        {
            int i = (kx - 88) >> NavGrid.Shift, k = (kz + 4) >> NavGrid.Shift;
            if (sim.Nav.Stand(i, y, k, 2)) up = sim.Nav.Id(i, y, k);
        }
        if (up >= 0) sim.Spawn(Kinds.Archer, up);
        Advance(2);
        int look = _look >= 3 && _look <= 5 ? _look - 3 : _look;
        switch (look)
        {
            case 0: Rig.SetAt(w.WX(kx - 24), w.WY(g), w.WZ(kz), 2.35f, .62f, .9f); break;
            case 1: Rig.SetAt(w.WX(kx - 24), w.WY(g), w.WZ(kz), -.8f, .62f, .9f); break;
            case 2: Rig.SetAt(w.WX(kx - 36), w.WY(g + 4), w.WZ(kz + 6), MathF.PI / 2 + .35f, .32f, 3.6f); break;
            case 6: Rig.SetAt(w.WX(kx - 84), w.WY(g + 6), w.WZ(kz), -MathF.PI / 2, .22f, 2.2f); break;
            default: Rig.SetAt(w.WX(kx - 24), w.WY(g), w.WZ(kz), 0, 1.45f, .8f); break;
        }
        if (_bare) Hud.Visible = false;
    }
}
