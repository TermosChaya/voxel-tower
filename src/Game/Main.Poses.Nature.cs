using System;
using System.Linq;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The valley's small things (--pose=nature, MapGen.Nature.cs), the nearest of each to the citadel: --look=0 a bush at
    /// the woods' edge, 1 a drift of flowers in a meadow, 2 reeds and lilies on a lake's shore, 3 a fallen tree with its
    /// root plate and pit, 4 a hollow in an old trunk, 5 the whole valley from high up, 6 ferns and mushrooms on the
    /// forest floor, 7 juniper on a dry slope, 8 a nest on a dead tree. The fallen tree, the hollow and the forest
    /// floor are seen through a rider's eyes (no hands in the picture), as under the trees the view from above sees only
    /// their crowns.
    /// </summary>
    void NaturePose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.WildTarget = 0;
        (int x, int z) Nearest(Detail k, Func<(Detail Kind, int X, int Z, int R, float DX, float DZ), bool> also = null)
        {
            var all = s.Details.Where(d => d.Kind == k && (also == null || also(d))).OrderBy(d => Brushes.Dist(d.X - s.KeepX, d.Z - s.KeepZ)).ToList();
            return all.Count == 0 ? (s.KeepX - 200, s.KeepZ) : (all[0].X, all[0].Z);
        }
        float yaw = -.66f, pitch = .5f, zoom = 3f;
        int cx = s.KeepX, cz = s.KeepZ, cy;
        switch (_look)
        {
            case 1:
                (cx, cz) = Densest(c => c is Mat.Flower or Mat.Bloom or Mat.FlowerRed or Mat.FlowerWhite or Mat.FlowerBlue);
                zoom = 2.4f;
                break;
            case 2:
                (cx, cz) = Densest(c => c is Mat.Reed or Mat.Lily);
                zoom = 3.2f;
                break;
            case 3:
            {
                var d = s.Details.Where(d => d.Kind == Detail.FallenTree).OrderBy(d => Brushes.Dist(d.X - s.KeepX, d.Z - s.KeepZ)).FirstOrDefault();
                if (d.R > 0)
                {
                    // from one side of it and a little behind its foot: the root plate and the pit near, the trunk running away
                    float sx = -d.DZ, sz = d.DX;
                    Look(d.X - d.DX * 14 + sx * 34, d.Z - d.DZ * 14 + sz * 34, d.X + d.DX * d.R * .3f, d.Z + d.DZ * d.R * .3f, -.36f);
                    return;
                }
                break;
            }
            case 4:
            {
                var d = s.Details.Where(d => d.Kind == Detail.Hollow).OrderBy(d => Brushes.Dist(d.X - s.KeepX, d.Z - s.KeepZ)).FirstOrDefault();
                if (d.R > 0)
                {
                    // standing before it, looking up at it
                    float g = w.SurfAt(d.X, d.Z), dist = 26;
                    Look(d.X + d.DX * dist, d.Z + d.DZ * dist, d.X, d.Z, MathF.Atan2(d.R + 2 - g - 20, dist));
                    return;
                }
                break;
            }
            case 5:
                cx = s.KeepX; cz = s.KeepZ; zoom = .3f; pitch = 1.05f;
                break;
            case 6:
            {
                var (fx, fz) = Densest(c => c is Mat.Fern or Mat.Mushroom);
                Look(fx - 30, fz - 30, fx, fz, -.3f);
                return;
            }
            case 7:
                (cx, cz) = Densest(c => c == Mat.Juniper);
                zoom = 2.6f;
                break;
            case 8:
            {
                var d = s.Details.Where(d => d.Kind == Detail.Nest).OrderBy(d => Brushes.Dist(d.X - s.KeepX, d.Z - s.KeepZ)).FirstOrDefault();
                if (d.R > 0)
                {
                    Rig.SetAt(w.WX(d.X + .5f), w.WY(d.R), w.WZ(d.Z + .5f), yaw, .45f, 6f);
                    Hud.Refresh();
                    return;
                }
                break;
            }
            default:
                (cx, cz) = Nearest(Detail.Hazel);
                zoom = 3f;
                break;
        }
        cy = w.SurfAt(cx, cz);
        GD.Print($"[voxel-tower] nature: look {_look} at {cx},{cz}");
        Rig.SetAt(w.WX(cx), w.WY(cy), w.WZ(cz), yaw, pitch, zoom);
        Hud.Refresh();
    }

    /// <summary>Through the eyes of a rider standing at (x, z), turned to (tx, tz), the head tilted so (up is positive).</summary>
    void Look(float x, float z, float tx, float tz, float tilt)
    {
        var sim = Sim;
        var me = sim.Spawn(Kinds.Rider, sim.NearestStand((int)x, (int)z, Kinds.Rider.Move.Height));
        me.Hold = true;
        Advance(.3);
        FP.Bare = true;
        FP.Enter(me);
        FP.Face(MathF.Atan2(tz - me.Z, tx - me.X), tilt);
        GD.Print($"[voxel-tower] nature: look {_look} from {me.X:0},{me.Z:0} at {tx:0},{tz:0}");
        Paused = true;
        Hud.Refresh();
    }

    /// <summary>The middle of the patch of the lowland with the most of something on its ground, near the citadel first.</summary>
    (int x, int z) Densest(Func<byte, bool> what)
    {
        var w = World; var s = Site;
        int best = -1, bx = s.KeepX - 200, bz = s.KeepZ;
        for (int z = 40; z < w.SZ - 40; z += 24)
            for (int x = 40; x < w.SX - 40; x += 24)
            {
                if (!w.OnLand(x, z) || s.Hex.Edge(x, z) < 60) continue;
                int n = 0;
                for (int dz = -20; dz <= 20; dz += 2)
                    for (int dx = -20; dx <= 20; dx += 2)
                    {
                        int xx = x + dx, zz = z + dz, g = w.SurfAt(xx, zz);
                        byte m = w.Get(xx, g, zz);
                        if (m == Mat.Water) m = w.Get(xx, w.ColTop(xx, zz) - 1, zz);
                        if (what(m)) n++;
                    }
                // (nearer the citadel counts for a little)
                n = n * 1000 - (int)Brushes.Dist(x - s.KeepX, z - s.KeepZ);
                if (n > best) (best, bx, bz) = (n, x, z);
            }
        return (bx, bz);
    }
}
