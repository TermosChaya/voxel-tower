using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// Our engines at work west of the citadel (--pose=ourengines, --at=seconds of it, 3 by itself): a trebuchet up on a
    /// platform of logs, a stone-thrower and a ballista on the ground beside it, their crews at them; the horde's catapult
    /// far off on the field with its guard, and a band of goblins and orcs with a hill giant coming on nearer. The
    /// trebuchet throws at the catapult, the stone-thrower into the band, the ballista at the giant. --look=1 close on the
    /// band as the stones come down in it; --look=2 the trebuchet close, its beam up; --look=3 the catapult and the band
    /// from the side, its stone on the arc between them.
    /// </summary>
    void OurEnginesPose()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9;
        foreach (string k in new[] { "mangonels", "ballistas", "trebuchets", "windlass", "heavystones" }) sim.Learnt.Add(k);
        int kx = Site.KeepX, kz = Site.KeepZ, px = kx - Site.KeepR - 50;
        // the platform: a block of logs six metres square and two high
        int top = Shapes.LevelTop(w, px, kz, 16);
        sim.InstantLabour = true;
        sim.DesignateBuild(Shapes.Line(w, Site, px, kz - 22, px, kz + 22, new Stroke { Mat = Mat.Wood, Width = 48, Height = 16, Level = true }, top));
        sim.InstantLabour = false;
        Unit Engine(UnitType t, int x, int z)
        {
            var e = sim.Spawn(t, sim.NearestStand(x, z, t.Move.Height));
            e.Rig = 1;
            e.Facing = System.MathF.PI;
            for (int i = 0; i < t.Crew; i++)
            {
                var c = sim.Spawn(Kinds.Crewman, sim.NearestStand((int)e.X + 20, (int)e.Z + (i - 1) * 8, 2));
                c.Escort = e;
            }
            return e;
        }
        var treb = Engine(Kinds.Trebuchet, px, kz);
        var cart = Engine(Kinds.Mangonel, px - 10, kz + 54);
        Engine(Kinds.Ballista, px - 10, kz - 50);
        // the horde: its catapult out on the field with its guard, a band nearer with a hill giant beside it
        var cat = Camp(Kinds.Catapult, px - 380, kz - 6);
        foreach (var (dx, dz) in new[] { (12, 14), (14, -12) }) Camp(dx > 12 ? Kinds.Goblin : Kinds.Orc, (int)cat.X + dx, (int)cat.Z + dz).Escort = cat;
        foreach (var (t, dx, dz) in new[] { (Kinds.Orc, -220, 40), (Kinds.Goblin, -212, 52), (Kinds.Goblin, -226, 30), (Kinds.Orc, -216, 64), (Kinds.Goblin, -230, 58),
                     (Kinds.Orc, -208, 76), (Kinds.Goblin, -222, 86) })
            Camp(t, px + dx, kz + dz);
        Camp(Kinds.HillGiant, px - 250, kz - 44).Hp = 5000;
        Advance(_poseSeconds == 14 ? 1.3 : _poseSeconds);
        GD.Print($"[pose] our engines: trebuchet at {treb.X:0},{treb.Y:0},{treb.Z:0}, the catapult of the horde at {cat.X:0},{cat.Z:0} ({(cat.Dead ? "broken" : $"{cat.Hp:0} hp")}); thrown {sim.EngineShots}, stones aloft {sim.Fliers.Count}");
        if (_look == 1) Rig.SetAt(w.WX(px - 215), w.WY(w.SurfAt(px - 215, kz + 58)), w.WZ(kz + 58), -.9f, .55f, 2.6f);
        else if (_look == 2) Rig.SetAt(w.WX(treb.X), w.WY(treb.Y + 20), w.WZ(treb.Z), -.6f, .32f, 2.1f);
        else if (_look == 3) Rig.SetAt(w.WX(cart.X - 110), w.WY(top + 10), w.WZ(cart.Z + 20), .25f, .3f, 1.05f);
        else Rig.SetAt(w.WX(px - 190), w.WY(top + 24), w.WZ(kz + 10), 0f, .3f, .62f);
        Hud.Refresh();
    }
}
