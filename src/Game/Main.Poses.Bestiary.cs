using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>The set-ups for a look at the creatures of the wild (--pose=bestiary, Bestiary.cs).</summary>
public partial class Main
{
    /// <summary>
    /// The creatures of the wild for a close look, one row a time on the cleared grove facing the camera (--look=0…4: the
    /// people of the wild and the dead; the beasts; the great ones on two legs; the winged and the scaled; the dragon);
    /// and fights showing what they do (--look=10…13): a dragon breathing on a line of ours, harpies over archers on a
    /// rise, a treant in a wood against swordsmen, centaurs and cultists against the horde.
    /// </summary>
    void BestiaryPose()
    {
        var sim = Sim;
        sim.Waves.Countdown = 1e9;
        foreach (var u in sim.Units) if (u.Team != Team.Player || !u.Inside) { u.Dead = true; u.DiedAt = -999; }
        var rows = new[]
        {
            new[] { Kinds.Bandit, Kinds.Cultist, Kinds.Ghoul, Kinds.Wraith, Kinds.Harpy, Kinds.Skeleton },
            new[] { Kinds.Wolf, Kinds.Warg, Kinds.Spider, Kinds.Centaur, Kinds.Bear },
            new[] { Kinds.Ogre, Kinds.WildTroll, Kinds.Minotaur, Kinds.Golem, Kinds.FireSpirit },
            new[] { Kinds.Griffin, Kinds.Basilisk, Kinds.Wyvern, Kinds.Treant },
            new[] { Kinds.Dragon },
        };
        const float yaw = -.3f, pitch = .38f;
        float sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
        var (gx, gz) = GroveAt;
        float cx = gx + 150, cz = gz + 110;
        if (_look >= 0 && _look < rows.Length)
        {
            Paused = true;
            static float Spacing(UnitType t) => 2 * t.BodyR + 10 + (t.BodyH >= 24 ? 10 : 0) + (t.Hover > 0 ? 6 : 0);
            float span = 0;
            foreach (var t in rows[_look]) span += Spacing(t) * Math.Min(2, Bodies.Looks(t));
            float along = -span / 2;
            foreach (var t in rows[_look])
                for (int l = 0; l < Math.Min(2, Bodies.Looks(t)); l++)
                {
                    along += Spacing(t) / 2;
                    float x = cx + along * cos, z = cz - along * sin;
                    var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height));
                    u.Look = l;
                    u.X = x; u.Z = z; u.Y = World.SurfAt((int)x, (int)z);
                    // turned a little off the camera, the four-legged more, to show their sides
                    u.Facing = MathF.Atan2(cos, sin) + (t.NoHands || t == Kinds.Centaur ? 1.05f : .45f);
                    u.Hold = true;
                    along += Spacing(t) / 2;
                }
            float tall = _look == 4 ? 40 : _look == 2 || _look == 3 ? 26 : 14;
            float over = (World.WY(World.SurfAt((int)cx, (int)cz) + tall * .5f) - 4) / MathF.Tan(pitch);
            float zoom = Math.Clamp(248 / (span / 1.5f + 14), 1.1f, 7);
            Rig.Set(World.WX(cx) - sin * over, World.WZ(cz) - cos * over, yaw, pitch, zoom, snap: true);
            Hud.Refresh();
            return;
        }
        // ---- the fights
        Unit Ours(UnitType t, float x, float z)
        {
            var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height));
            u.Hold = true; u.Post = u.Cell; u.Dest = u.Cell;
            return u;
        }
        Unit Wild(UnitType t, float x, float z, Unit on)
        {
            var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height));
            u.Target = on; u.LastHurt = sim.Time;
            return u;
        }
        float fx = cx, fz = cz, look = 8, tilt = .5f;
        switch (_look)
        {
            case 10:
            {
                // a dragon before a line of ours, coming on; the shot as it breathes over them
                var line = new List<Unit>();
                for (int i = 0; i < 7; i++) line.Add(Ours(i % 3 == 0 ? Kinds.Archer : Kinds.Swordsman, cx + 50, cz - 36 + i * 12));
                var dragon = Wild(Kinds.Dragon, cx - 30, cz, line[3]);
                dragon.PowerAt = sim.Time + 100;
                Advance(_poseSeconds == 14 ? .3 : _poseSeconds);
                dragon.PowerAt = sim.Time;
                Advance(.1);
                fx = (dragon.X + cx + 50) / 2; fz = cz; look = 8;
                _shotFrames = Math.Min(_shotFrames, 8);
                break;
            }
            case 11:
            {
                // harpies swooping on a knot of archers and swordsmen, the shot as they come in over them
                var men = new List<Unit>();
                for (int i = 0; i < 6; i++) men.Add(Ours(i % 2 == 0 ? Kinds.Archer : Kinds.Swordsman, cx + (i % 3) * 8, cz + (i / 3) * 8));
                for (int i = 0; i < 6; i++) Wild(Kinds.Harpy, cx - 60 + i * 7, cz - 30 + (i % 2) * 14, men[i % men.Count]);
                fx = cx - 18; fz = cz - 6; look = 8;
                Advance(_poseSeconds == 14 ? 1.1 : _poseSeconds);
                break;
            }
            case 12:
            {
                // a treant stepping out of a stand of spruce and birch, swordsmen coming at it over the open
                var rng = new Random(7);
                for (int i = 0; i < 14; i++)
                {
                    int x = (int)cx - 64 + i * 10 + rng.Next(-3, 4), z = (int)cz - 18 - rng.Next(0, 36);
                    if (i % 3 == 0) Flora.Birch(World, x, z, 50 + rng.Next(16));
                    else Flora.Spruce(World, x, z, 60 + rng.Next(24));
                }
                // the trees are set in raw: their chunks meshed anew
                for (int kz = ((int)cz - 80) >> VoxelWorld.Shift; kz <= ((int)cz + 10) >> VoxelWorld.Shift; kz++)
                    for (int kx = ((int)cx - 90) >> VoxelWorld.Shift; kx <= ((int)cx + 90) >> VoxelWorld.Shift; kx++)
                        for (int ky = 0; ky < World.CY; ky++) World.MarkChunk(kx, ky, kz);
                var men = new List<Unit>();
                for (int i = 0; i < 5; i++) men.Add(Ours(Kinds.Swordsman, cx - 16 + i * 8, cz + 34 + (i % 2) * 6));
                Wild(Kinds.Treant, cx, cz - 8, men[2]);
                fx = cx; fz = cz + 6; look = 9; tilt = .55f;
                Advance(_poseSeconds == 14 ? 2.5 : _poseSeconds);
                break;
            }
            case 13:
            {
                // centaurs shooting and cultists throwing fire at the horde's band, the band going for them
                var band = new List<Unit>();
                for (int i = 0; i < 8; i++)
                {
                    var g = sim.Spawn(i % 3 == 0 ? Kinds.Orc : Kinds.Goblin, sim.NearestStand((int)cx + 70 + (i % 4) * 6, (int)cz - 12 + (i / 4) * 10, 2));
                    g.Guard = true; g.Home = g.Cell;
                    band.Add(g);
                }
                for (int i = 0; i < 3; i++) Wild(Kinds.Centaur, cx - 30, cz - 20 + i * 14, band[i]);
                for (int i = 0; i < 3; i++) Wild(Kinds.Cultist, cx - 10, cz - 14 + i * 12, band[3 + i]);
                fx = cx + 18; look = 8;
                Advance(_poseSeconds == 14 ? 3 : _poseSeconds);
                break;
            }
        }
        Paused = true;
        float over2 = (World.WY(World.SurfAt((int)fx, (int)fz) + 10) - 4) / MathF.Tan(tilt);
        Rig.Set(World.WX(fx) - MathF.Sin(-.5f) * over2, World.WZ(fz) - MathF.Cos(-.5f) * over2, -.5f, tilt, Math.Clamp(look / 4f, 1, 3), snap: true);
        Hud.Refresh();
    }
}
