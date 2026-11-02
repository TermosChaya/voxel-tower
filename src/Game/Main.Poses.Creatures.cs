using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The small life (--pose=creatures), on the square of --pose=square with a pond a metre deep north of its east house,
    /// reeds and moss about it, and a copse of trees west of the square: --look=0 the square by day, the flocks on the
    /// roofs and in the trees; 1 crows wheeling over four of the horde lying dead west of the square; 2 the flower bed
    /// close, butterflies and bees; 3 the pond at dusk, fireflies and bats; 4 through a worker's eyes by the pond by day,
    /// midges over it and a fish jumping; the town's beasts, come as the town keeps them (the stables and the kennel put up
    /// south of the square): 5 the hens about the houses and the yard dog with a worker; 6 the horses and goats in the
    /// stables' paddock; 7 the dog barking at a goblin coming; 8 a hen and the dog close.
    /// </summary>
    void CreaturesPose()
    {
        var w = World; var sim = Sim;
        sim.SetSource(new MapRaids());
        var (hx, hz) = SquareBuild();
        sim.Clock.Set(new ClockTime(1, _look == 3 ? DayClock.DayLength - 25 : 200));
        sim.Weather = Weather.Clear;
        int g0 = w.SurfAt(hx, hz);
        // the pond north of the east house, reeds and moss on its bank
        int px0 = hx + 56, pz0 = hz - 68, pw = 36, pd = 24;
        for (int z = pz0 - 6; z < pz0 + pd + 6; z++)
            for (int x = px0 - 6; x < px0 + pw + 6; x++)
            {
                bool inside = x >= px0 && x < px0 + pw && z >= pz0 && z < pz0 + pd;
                if (inside) for (int y = g0 - 8; y < g0 + 6; y++) w.Set(x, y, z, y < g0 ? Mat.Water : Mat.Air);
                else if (Hash.H3(x, 9, z) < .5)
                {
                    int top = w.SurfAt(x, z);
                    w.Set(x, top - 1, z, Mat.Moss);
                    if (Hash.H3(z, 11, x) < .3 && w.Get(x, top, z) == 0) w.Set(x, top, z, Mat.Reed);
                }
            }
        // a copse west of the square
        foreach (var (dx, dz) in new[] { (-62, -58), (-80, -28), (-58, 58), (-82, 30) })
        {
            int tx = hx + dx, tz = hz + dz, tg = w.SurfAt(tx, tz);
            for (int y = tg; y < tg + 30; y++) w.Set(tx, y, tz, Mat.Trunk);
            for (int y = tg + 22; y < tg + 38; y++)
                for (int ddz = -9; ddz <= 9; ddz++)
                    for (int ddx = -9; ddx <= 9; ddx++)
                        if (ddx * ddx + ddz * ddz + (y - tg - 30) * (y - tg - 30) < 75 && w.Get(tx + ddx, y, tz + ddz) == 0) w.Set(tx + ddx, y, tz + ddz, Mat.Leaf);
        }
        Advance(1);
        float gy = w.WY(g0);
        switch (_look)
        {
            case 1:
            {
                // four of the horde dead west of the square
                for (int i = 0; i < 4; i++)
                {
                    int at = sim.NearestStand(hx - 36 + i * 9, hz + 46 + i % 2 * 8, 2);
                    if (at < 0) continue;
                    var g = sim.Spawn(i % 2 == 0 ? Kinds.Goblin : Kinds.Orc, at);
                    sim.Hurt(g, null, 9999);
                }
                Advance(.5);
                Rig.Set(w.WX(hx - 24), w.WZ(hz + 50), -.66f, .62f, 1.1f, snap: true);
                Life.Warm(22);
                break;
            }
            case 2: Rig.SetAt(w.WX(hx - 48), w.WY(w.SurfAt(hx - 48, hz - 36)), w.WZ(hz - 36), -.9f, .55f, 2.6f); Life.Warm(25); break;
            case 3: Rig.SetAt(w.WX(px0 + pw / 2), gy, w.WZ(pz0 + pd / 2), -.9f, .55f, 2.4f); Life.Warm(20); break;
            case 4:
            {
                var me = sim.Spawn(Kinds.Worker, sim.NearestStand(px0 - 8, pz0 + pd / 2, 2));
                Advance(.3);
                FP.Enter(me);
                FP.Face(0, -.12f);
                Life.Warm(25);
                break;
            }
            case >= 5 and <= 8:
                TownBeastsScene(hx, hz, gy);
                break;
            default: Rig.Set(w.WX(hx - 20), w.WZ(hz), -.66f, .62f, 1.3f, snap: true); Life.Warm(45); break;
        }
        GD.Print($"[pose] creatures: " + string.Join(", ", Enum.GetValues<Critter>().Select(k => $"{k} {Life.Life.Count(k)}")) + $"; take-offs {Life.Life.TakeOffs}, caws {Life.Life.Caws}"
            + $"; the town's beasts: hens {sim.TameCount(Kinds.Hen)}, dogs {sim.TameCount(Kinds.Cur)}, horses {sim.TameCount(Kinds.Horse)}, goats {sim.TameCount(Kinds.Goat)}, barks {sim.Barks}; {Life.Life.Said()}");
        Hud.Refresh();
    }

    /// <summary>The town's beasts come as the town keeps them: the kennel and the stables put up south of the square, a minute and a half of the town.</summary>
    void TownBeastsScene(int hx, int hz, float gy)
    {
        var w = World; var sim = Sim;
        sim.Stock[Stockpile.Wood] += 2000.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Stone] += 2000.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Earth] += 2000.0 * VoxelWorld.PerM3;
        sim.Stock.Gold += 2000;
        sim.Knowledge = 200;
        sim.Learn("b_kennel"); sim.Learn("b_stables");
        sim.InstantLabour = true;
        // each put up on the first spot about the square it fits
        var spots = new[] { (0, 150), (150, 0), (-150, 0), (0, -150), (130, 130), (-130, 130), (130, -130), (-130, -130), (0, 230), (230, 0), (-230, 0), (0, -230) };
        Building Put(string key, int skip)
        {
            foreach (var (dx, dz) in spots.Skip(skip))
            {
                if (sim.PutUp(Sim.TownKindOf(key), hx + dx, hz + dz, false, out var b) == null && b != null)
                {
                    for (int i = 0; i < 40 && !b.Ready; i++) { sim.CompleteAll(); Advance(2); }
                    return b;
                }
            }
            return null;
        }
        var kb = Put("kennel", 0);
        var sb = Put("stables", 1);
        sim.DressTown();
        sim.InstantLabour = false;
        GD.Print($"[pose] creatures: the kennel {(kb?.Ready == true ? "ready" : "none")}, the stables {(sb?.Ready == true ? $"ready at {sb.CX - hx:0},{sb.CZ - hz:0}" : "none")}");
        var folk = new System.Collections.Generic.List<Unit>();
        foreach (var (dx, dz) in new[] { (-20, 30), (10, 40), (30, -30) })
        {
            int at = sim.NearestStand(hx + dx, hz + dz, 2);
            if (at >= 0) folk.Add(sim.Spawn(Kinds.Worker, at));
        }
        sim.TownBeasts = true;
        sim.Clock.Set(new ClockTime(1, 200));
        Advance(100);
        var dog = sim.Units.Find(u => !u.Dead && u.T == Kinds.Cur);
        var hen = sim.Units.Find(u => !u.Dead && u.T == Kinds.Hen);
        switch (_look)
        {
            case 6:
            {
                var horse = sim.Units.Find(u => !u.Dead && u.T == Kinds.Horse);
                if (horse != null) Rig.SetAt(w.WX(horse.X), w.WY(horse.Y), w.WZ(horse.Z), -.9f, .55f, 2.6f);
                else if (sb != null) Rig.Set(w.WX(sb.CX), w.WZ(sb.CZ), -.66f, .62f, 1.3f, snap: true);
                break;
            }
            case 7:
            {
                if (dog != null)
                {
                    sim.Spawn(Kinds.Goblin, sim.NearestStand((int)dog.X - 220, (int)dog.Z + 20, 2));
                    for (int i = 0; i < 80 && sim.Barks == 0; i++) Advance(.1);
                    Rig.SetAt(w.WX(dog.X - 40), w.WY(dog.Y), w.WZ(dog.Z), -.9f, .55f, 2.4f);
                }
                break;
            }
            case 8:
            {
                var at = hen ?? dog;
                if (at != null) Rig.SetAt(w.WX(at.X), w.WY(at.Y), w.WZ(at.Z), -.9f, .5f, 3.4f);
                break;
            }
            default:
                Rig.Set(w.WX(hx), w.WZ(hz), -.66f, .62f, 1.3f, snap: true);
                break;
        }
        Life.Warm(20);
    }
}
