using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// What stands in the middle of a valley of the map (--pose=holding with --holding=outpost or --holding=horde): the
    /// outpost's place, levelled and marked out round its stone, or the horde's town and its garrison at their posts; seen
    /// from the side the enemy comes by. --look=1 nearer.
    /// </summary>
    void HoldingPose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.WildTarget = 0;
        Advance(2);
        var (ix, iz) = (s.Hex ?? Hex.In(w.SX, w.SZ)).Inward(s.EntrySide);
        float zoom = s.Holding == Holding.HordeTown ? 1.2f : 1.7f;
        Rig.SetAt(w.WX(s.KeepX), w.WY(s.KeepBase), w.WZ(s.KeepZ), MathF.Atan2(ix, iz) + .5f, .62f, _look == 1 ? zoom * 1.8f : zoom);
    }

    /// <summary>
    /// A party of ours coming into the valley over a side (--pose=arrive, --look=0…5 the side, the east by itself): ten of
    /// them with their commander, a swordsman hurt, walking in from the edge in a file and forming up some way in; seen
    /// from inland (--at= seconds after they came, 9 by itself).
    /// </summary>
    void ArrivePose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.WildTarget = 0;
        int side = _look is >= 0 and < 6 ? _look : Hex.East;
        var hex = s.Hex ?? Hex.In(w.SX, w.SZ);
        var party = new Party();
        foreach (var k in new[] { Kinds.Swordsman, Kinds.Spearman, Kinds.Archer, Kinds.Swordsman, Kinds.Captain, Kinds.Spearman, Kinds.Archer, Kinds.Swordsman, Kinds.Worker, Kinds.Worker })
            party.Members.Add(new Traveller { Kind = k, Look = 11 + party.Members.Count * 13, Hp = k.Hp, MaxHp = k.Hp });
        party.Members[3].Hp = party.Members[3].MaxHp * .45f;
        // (the woods about the way in cleared, so they are to be seen)
        var (mx, mz) = hex.SidePoint(side, .5f, 20 * VoxelWorld.PerMetre);
        for (int z = (int)mz - 200; z < (int)mz + 200; z++)
            for (int x = (int)mx - 200; x < (int)mx + 200; x++)
                if (w.OnLand(x, z))
                    for (int y = w.SurfAt(x, z); y < w.SY; y++)
                        if (Mat.IsTrunk(w.Get(x, y, z)) || Mat.IsLeafy(w.Get(x, y, z))) w.Set(x, y, z, Mat.Air);
        var come = sim.Arrive(party, side);
        Advance(_poseSeconds == 14 ? 9 : _poseSeconds);
        float cx = 0, cz = 0;
        foreach (var u in come) { cx += u.X / come.Count; cz += u.Z / come.Count; }
        var (ix, iz) = hex.Inward(side);
        Rig.SetAt(w.WX(cx), w.WY(w.SurfAt((int)cx, (int)cz)), w.WZ(cz), MathF.Atan2(-ix, -iz) + .5f, .55f, 2f);
    }
}
