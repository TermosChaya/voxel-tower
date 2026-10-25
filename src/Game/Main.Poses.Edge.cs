using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The land's edge and beasts by it (--pose=edge): a herd of deer and two boars grazing 10 m in from the north-western
    /// side, seen from beyond it over the cut of the land; --look=1 a goblin come up behind them from inland, and they
    /// bolt along the edge rather than over it (--at= seconds after, 5 by itself).
    /// </summary>
    void EdgePose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.WildTarget = 0;
        // only the herd here: the valley's own beasts let go
        foreach (var u in sim.Units) if (u.Team == Team.Wild) { u.Dead = true; u.DiedAt = -999; }
        var hex = s.Hex ?? Hex.In(w.SX, w.SZ);
        var (ix, iz) = hex.Inward(Hex.NorthWest);
        // (along the side, the way round from its first corner)
        float ax = -iz, az = ix;
        // (where along the side the river is furthest off)
        float best = -1, at = .5f;
        foreach (float t in new[] { .2f, .3f, .4f, .5f, .6f, .7f, .8f })
        {
            var (px, pz) = hex.SidePoint(Hex.NorthWest, t, 80);
            float off = s.RiverAt(px, pz, out float o, out float half, out _, out _) ? o - half : 1e9f;
            if (off > best) (best, at) = (off, t);
        }
        var (cx, cz) = hex.SidePoint(Hex.NorthWest, at, 80);
        // (a meadow cleared there, drawn again after)
        for (int z = (int)cz - 150; z < (int)cz + 150; z++)
            for (int x = (int)cx - 150; x < (int)cx + 150; x++)
                if (w.OnLand(x, z))
                    for (int y = w.SurfAt(x, z); y < w.SY; y++)
                        if (Mat.IsTrunk(w.Get(x, y, z)) || Mat.IsLeafy(w.Get(x, y, z))) w.Set(x, y, z, Mat.Air);
        var rng = new Random(3);
        void Beast(UnitType t, float along, float inward)
        {
            int c = sim.NearestStand((int)(cx + ax * along + ix * inward), (int)(cz + az * along + iz * inward), t.Move.Height);
            if (c < 0) return;
            var u = sim.Spawn(t, c);
            u.Home = c;
            u.Facing = (float)(rng.NextDouble() * Math.PI * 2);
            u.NextSearch = sim.Time + rng.NextDouble() * 6;
        }
        for (int i = 0; i < 6; i++) Beast(Kinds.Deer, -40 + i * 16 + rng.Next(-5, 6), rng.Next(-14, 15));
        Beast(Kinds.Boar, 46, 10); Beast(Kinds.Boar, 58, -6);
        if (_look == 1)
        {
            var goblin = sim.Spawn(Kinds.Goblin, sim.NearestStand((int)(cx + ix * 130), (int)(cz + iz * 130), 2));
            goblin.Guard = true; goblin.Home = goblin.Cell;
            Advance(_poseSeconds == 14 ? 5 : _poseSeconds);
        }
        else Advance(1);
        // from out beyond the side, over the cut of the land, at the herd and the wood behind it
        float yaw = MathF.Atan2(-ix, -iz) + .55f;
        Rig.SetAt(w.WX(cx), w.WY(w.SurfAt((int)cx, (int)cz)), w.WZ(cz), yaw, .45f, 1.6f);
    }
}
