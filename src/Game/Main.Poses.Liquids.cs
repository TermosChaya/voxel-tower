using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The sluice opened on the horde (--pose=wave, --at=seconds after it opens, 3 by itself): a reservoir behind a dam
    /// of stone west of the citadel, the gate in its east wall opened on a band of the horde standing before it; the
    /// water pours out through the gateway as a wave and carries them off their feet. --look=1 close on the gateway;
    /// --look=2 from high over it, a while after, the flood spread out over the field.
    /// </summary>
    void Wave()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9;
        int rx = Site.KeepX - Site.KeepR - 230, rz = Site.KeepZ - 40, rg = 0;
        // a level yard for it, of earth
        for (int z = rz - 40; z < rz + 40; z++) for (int x = rx - 40; x < rx + 140; x++) rg = Math.Max(rg, w.SurfAt(x, z));
        for (int z = rz - 40; z < rz + 40; z++)
            for (int x = rx - 40; x < rx + 140; x++)
            {
                for (int y = rg; y < rg + 60; y++) w.Set(x, y, z, Mat.Air);
                for (int y = rg - 12; y < rg; y++) w.Set(x, y, z, y == rg - 1 ? Mat.Grass : Mat.Soil);
            }
        sim.InstantLabour = true;
        int top = Shapes.LevelTop(w, rx, rz, 32);
        sim.DesignateBuild(Shapes.Box(w, Site, rx - 24, rz - 24, rx + 24, rz + 24, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Level = true, Hollow = true }, top));
        sim.InstantLabour = false;
        for (int z = rz - 16; z < rz + 17; z++) for (int x = rx - 16; x < rx + 17; x++) for (int y = rg; y < rg + 26; y++) if (w.Get(x, y, z) == Mat.Air) w.Set(x, y, z, Mat.Water);
        Advance(2);
        int ex = rx + 16;
        while (w.Get(ex + 1, rg + 6, rz) != Mat.Air) ex++;
        var plan = GatePlan.At(w, Site, new VoxelHit { X = ex, Y = rg + 6, Z = rz, NX = 1, M = w.Get(ex, rg + 6, rz) });
        var sluice = plan == null ? null : sim.AddGate(plan, built: true);
        if (sluice != null) sim.SetGateMode(sluice, GateMode.Shut);
        Advance(1);
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, 6, -4), (Kinds.Orc, 9, 5), (Kinds.Goblin, 12, 0), (Kinds.Goblin, 11, 9), (Kinds.Orc, 17, -7), (Kinds.Goblin, 20, 3), (Kinds.Troll, 30, -2) })
            Camp(t, ex + dx, rz + dz);
        Advance(.5);
        if (sluice != null) sim.SetGateMode(sluice, GateMode.Open);
        Advance(_look == 2 ? 20 : _poseSeconds == 14 ? 3 : _poseSeconds);
        if (_look == 1) Rig.SetAt(w.WX(ex + 14), w.WY(rg + 4), w.WZ(rz), -2.4f, .42f, 3.6f);
        else if (_look == 2) Rig.SetAt(w.WX(ex + 40), w.WY(rg), w.WZ(rz), -2.1f, .95f, 1.3f);
        else Rig.SetAt(w.WX(ex + 22), w.WY(rg + 2), w.WZ(rz), -2.3f, .5f, 2.3f);
        Hud.Refresh();
    }

    /// <summary>
    /// Lava (--pose=lava, --at=seconds of it, 6 by itself): molten rock let out on a slope west of the citadel, running
    /// down between a fence of logs, which it sets alight, and a ditch of water, where it meets the water in steam and
    /// sets to stone. --look=1 the same at night, the lava lighting the ground about it; --look=2 a falling star brought
    /// down there, its crater full of lava; --look=3 a minute later, the lava cooled to dark stone.
    /// </summary>
    void LavaPose()
    {
        var sim = Sim; var w = World;
        if (_look == 1) Nightfall(); else sim.Waves.Countdown = 1e9;
        int lx = Site.KeepX - Site.KeepR - 170, lz = Site.KeepZ + 30, g = w.SurfAt(lx, lz);
        // a fence of logs along one side of where it will run, a ditch of water along the other
        sim.InstantLabour = true;
        sim.DesignateBuild(Shapes.Line(w, Site, lx - 14, lz + 8, lx + 26, lz + 8, new Stroke { Mat = Mat.Wood, Width = 3, Height = 12 }, 0));
        sim.InstantLabour = false;
        for (int x = lx - 14; x < lx + 26; x++)
            for (int z = lz - 14; z < lz - 8; z++)
            {
                int s = w.SurfAt(x, z);
                for (int y = s - 4; y < s; y++) w.Set(x, y, z, Mat.Water);
            }
        Advance(1);
        if (_look == 2)
        {
            Sorcerous();
            sim.Cast(SpellKind.Meteor, lx + 20, g, lz);
            Advance(_poseSeconds == 14 ? 16 : _poseSeconds);
        }
        else
        {
            sim.LetLava(lx, lz, 420);
            Advance(_look == 3 ? 90 : _poseSeconds == 14 ? 6 : _poseSeconds);
        }
        Rig.SetAt(w.WX(lx + 4), w.WY(g - 2), w.WZ(lz), -1.9f, .62f, _look == 2 ? 1.9f : 2.6f);
        Hud.Refresh();
    }
}
