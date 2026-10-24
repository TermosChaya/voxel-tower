using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// Alchemists of ours at work (--pose=ouralchemist, --at=seconds of it, 14 by itself; at 2 or 3 the rungs are being eaten,
    /// at 5 or 6 the climbers come down): a stone wall west of the citadel,
    /// the horde's ladder up its outer face with two goblins on it and more at its foot, and two alchemists of ours on the
    /// wall throwing at the rungs under the climbers; the rungs greening and eaten through, the climbers coming down.
    /// --look=1 close on the ladder; --look=2 a knot of raiders out on the field, with the bombardiers learnt, the fumes.
    /// </summary>
    void OurAlchemists()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9;
        sim.Learnt.Add("alchemists");
        if (_look == 2) sim.Learnt.Add("bombards");
        int top = WallWest(-90, 32, 60);
        int wx = Site.KeepX - Site.KeepR - 90, kz = Site.KeepZ;
        // the outer face of the wall, and the horde's ladder up it to the top
        int face = wx;
        while (face > wx - 16 && Mat.IsPlayer[w.Get(face, top - 1, kz)]) face--;
        for (int z = kz - 1; z <= kz; z++)
            for (int y = w.GroundTop(face, z); y < top; y++) if (w.Get(face, y, z) == Mat.Air) w.Set(face, y, z, Mat.SiegeLadder);
        Advance(.2);
        if (_look == 2)
            foreach (var (dx, dz) in new[] { (-60, -6), (-54, 2), (-62, 8), (-50, -2), (-58, 14) })
            {
                var g = Camp(Kinds.Goblin, face + dx, kz + dz);
                g.ChainedUntil = 1e9; g.Hp = g.MaxHp = 5000;
            }
        else
        {
            foreach (int h in new[] { 22, 11 })
            {
                int c = sim.Nav.CellOf(face, w.GroundTop(face, kz) + h, kz);
                var g = sim.Spawn(Kinds.Goblin, c);
                g.Cell = c; (g.X, g.Y, g.Z) = (face + .5f, w.GroundTop(face, kz) + h, kz + .5f);
                g.ChainedUntil = 1e9; g.Hp = g.MaxHp = 5000;
            }
            foreach (var (dx, dz) in new[] { (-10, 6), (-16, -8), (-22, 12) }) Camp(Kinds.Goblin, face + dx, kz + dz);
            Camp(Kinds.Orc, face - 18, kz + 2);
        }
        foreach (int dz in new[] { 22, -26 })
        {
            var a = sim.Spawn(Kinds.OurAlchemist, sim.NearestStand(wx, kz + dz, 2));
            a.Hold = true; a.Dest = a.Post = a.Cell; a.Hp = a.MaxHp = 5000;
        }
        Advance(_poseSeconds);
        if (_look == 1) Rig.SetAt(w.WX(face - 22), w.WY(top - 14), w.WZ(kz + 4), -1.1f, .45f, 4f);
        else if (_look == 2) Rig.SetAt(w.WX(face - 40), w.WY(top - 20), w.WZ(kz + 6), -1.05f, .55f, 2.2f);
        else Rig.SetAt(w.WX(face - 46), w.WY(top - 18), w.WZ(kz + 6), -1.05f, .5f, 2.1f);
        Hud.Refresh();
    }
}
