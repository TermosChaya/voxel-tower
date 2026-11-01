using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The town's life (--pose=life), on the square of --pose=square with a pond by its east house and a sanctuary west
    /// of it, the valley's clock as the map keeps it: --look=0 the square by day, its people at their pastimes (rings
    /// talking and at dice, a whetstone, the pond, a broom at a door, the flower bed, the sanctuary); 1 the dice and the
    /// ring of three close, with what they say; 2 noon, the meal on the benches; 3 the pond and the broom close; 4 the night,
    /// asleep about the fire and a soldier on watch; 5 a band of the horde gathering west of the square, chanting; 6 two
    /// passing greeting each other close.
    /// </summary>
    void LifePose()
    {
        var w = World; var sim = Sim;
        sim.SetSource(new MapRaids());
        sim.Clock.Set(new ClockTime(1, _look == 2 ? 300 : 150));
        var (hx, hz) = SquareBuild();
        sim.Clock.Set(new ClockTime(1, _look == 2 ? 300 : 150));
        int g0 = w.SurfAt(hx, hz);
        // the pond north of the east house, a metre deep; the sanctuary west of the square
        for (int z = hz - 68; z < hz - 44; z++)
            for (int x = hx + 56; x < hx + 92; x++)
                for (int y = g0 - 8; y < g0 + 4; y++) w.Set(x, y, z, y < g0 ? Mat.Water : Mat.Air);
        sim.AddZone(hx - 106, hz + 34, hx - 72, hz + 68, Zone.Sanctum, 0);
        Advance(1);

        Unit At(UnitType t, int dx, int dz) => sim.Spawn(t, sim.NearestStand(hx + dx, hz + dz, t.Move.Height));
        var folk = new List<Unit>();
        Unit Take(Unit u, Pastime p) { if (!sim.TakeUp(u, p)) GD.Print($"[pose] life: {u.T.Key} found no place for {p}"); folk.Add(u); return u; }
        // three soldiers at dice by the cart, three workers talking by the well
        var dice = new[] { At(Kinds.Swordsman, 26, -36), At(Kinds.Spearman, 30, -33), At(Kinds.Archer, 29, -39) };
        Take(dice[0], Pastime.Dice); folk.Add(dice[1]); folk.Add(dice[2]);
        var talk = new[] { At(Kinds.Worker, -30, 22), At(Kinds.Worker, -27, 26), At(Kinds.Worker, -33, 27) };
        Take(talk[0], Pastime.Talk); Take(talk[2], Pastime.Talk); folk.Add(talk[1]);
        var fisher = Take(At(Kinds.Worker, 50, -30), Pastime.Fish);
        var sweeper = Take(At(Kinds.Worker, 60, 20), Pastime.Sweep);
        Take(At(Kinds.Worker, -40, -30), Pastime.Tend);
        Take(At(Kinds.Worker, -70, 40), Pastime.Kneel);
        Take(At(Kinds.Swordsman, 10, 30), Pastime.Sharpen);
        var eaters = new List<Unit>();
        if (_look == 2)
            foreach (var (dx, dz) in new[] { (-6, -20), (6, -20), (-20, 4), (20, -4), (-4, 20) })
                eaters.Add(Take(At(dx % 2 == 0 ? Kinds.Worker : Kinds.Spearman, dx, dz), Pastime.Eat));
        // a pair to greet each other: one walking past the other
        var walker = At(Kinds.Worker, -10, 50);
        var stander = At(Kinds.Archer, 4, 52);
        folk.Add(walker); folk.Add(stander);
        // kept at it while the scene is looked at, each from when it gets there
        void Keep()
        {
            foreach (var u in folk)
            {
                if (u.Pastime is Pastime.Stroll or Pastime.None or Pastime.Look or Pastime.Stretch) continue;
                u.PastimeUntil = sim.Time + 999;
                if (u.Group != null) u.Group.Until = sim.Time + 999;
            }
        }
        Keep();
        for (int t = 0; t < 24; t++) { Advance(1); Keep(); }
        if (_look == 4)
        {
            // the night: some asleep about the fire, every other soldier on watch
            sim.Clock.Set(new ClockTime(1, DayClock.DayLength + 30));
            foreach (var u in folk) if (u.T == Kinds.Worker || u.Id % 2 == 0) { if (sim.ToBed(u, bunk: false)) sim.InBed(u); }
            Advance(2);
        }
        Warband band = null;
        if (_look == 5)
        {
            band = sim.Summon(hx - 220, hz - 10, false);
            Advance(.5);
            GD.Print($"[pose] life: the band {band?.State}, {band?.Members.Count} of it");
        }
        int ring3 = 0;
        foreach (var u in folk) if (u.Group is { Members.Count: 3 }) ring3++;
        GD.Print($"[pose] life at {hx},{hz}: " + string.Join("; ", folk.ConvertAll(u => $"{u.T.Key} {u.Pastime}{(u.Pastime == Pastime.Stroll ? ">" + u.After : "")}")) + $"; in rings of three {ring3}");

        // what is said, a moment before the picture is taken
        _poseLater = () =>
        {
            if (_look is 0 or 1)
            {
                sim.SayAloud(talk[0], "Хорошая стена.", Voice.Chat);
                sim.SayAloud(talk[2], "Ха-ха!", Voice.Laugh);
                sim.SayAloud(dice[0], "Шесть!", Voice.Chat);
            }
            if (_look == 2 && eaters.Count > 1) { sim.SayAloud(eaters[0], "Похлёбка горячая.", Voice.Chat); sim.SayAloud(eaters[1], "Передай соль.", Voice.Chat); }
            if (_look == 3) sim.SayAloud(sweeper, "Доброе утро!", Voice.Greet);
            if (_look == 5 && band != null)
            {
                band.State = BandState.Forming;
                int n = 0;
                foreach (var m in band.Members) if (!m.Dead && n++ < 4) sim.SayAloud(m, "Хо!", Voice.Chant);
                for (int i = 0; i < 6; i++) sim.Step(.05f);
                band.State = BandState.Forming;
            }
            if (_look == 6)
            {
                walker.GreetAt = stander.GreetAt = sim.Time; walker.GreetTo = stander; stander.GreetTo = walker;
                sim.SayAloud(walker, "Здорово!", Voice.Greet);
            }
        };

        float gy = w.WY(g0);
        (float x, float z) Mid(params Unit[] us) { float x = 0, z = 0; foreach (var u in us) { x += u.X; z += u.Z; } return (x / us.Length, z / us.Length); }
        switch (_look)
        {
            case 1: { var (mx, mz) = Mid(dice[0], talk[0]); Rig.SetAt(w.WX(mx), gy, w.WZ(mz), -.5f, .5f, 2.6f); break; }
            case 2: { var (mx, mz) = eaters.Count > 0 ? Mid(eaters.ToArray()) : (hx, hz); Rig.SetAt(w.WX(mx), gy, w.WZ(mz), -.66f, .55f, 2.7f); break; }
            case 3: { var (mx, mz) = Mid(fisher, sweeper); Rig.SetAt(w.WX(mx), gy, w.WZ(mz), -.9f, .55f, 2.4f); break; }
            case 4: Rig.SetAt(w.WX(hx), gy, w.WZ(hz + 4), -.66f, .55f, 2.6f); break;
            case 5: { var (mx, mz) = band?.Leader != null ? Mid(band.Leader) : (hx - 220, hz); Rig.SetAt(w.WX(mx), gy, w.WZ(mz), -.66f, .5f, 2.6f); break; }
            case 6: { var (mx, mz) = Mid(walker, stander); Rig.SetAt(w.WX(mx), gy, w.WZ(mz), -.66f, .45f, 3.2f); break; }
            default: Rig.Set(w.WX(hx), w.WZ(hz), -.66f, .62f, 1.2f, snap: true); break;
        }
        Hud.Refresh();
    }
}
