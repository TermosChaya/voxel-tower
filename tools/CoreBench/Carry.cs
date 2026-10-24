using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's --carry: the wounded looked after by their own (Sim.Carry.cs). One who cannot get up carried into the
/// citadel and mending there three times as fast; the carrier putting it down when struck; one limping helped along
/// at twice its pace; the pinned calling out; the player sending the chosen to dig one out and to carry one off.
/// </summary>
static class CarryBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        int kx = site.KeepX, kz = site.KeepZ;
        // the household at the gate kept out of it: these tests bring their own
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        Run(.1);
        var (gx, _, gz) = sim.Nav.Feet(sim.Gate);
        // a clear field west of the gate, its trees gone
        for (int z = (int)gz - 200; z < (int)gz + 200; z++)
            for (int x = (int)gx - 400; x < (int)gx; x++)
                for (int y = w.SurfAt(x, z), top = w.ColTop(x, z); y < top; y++)
                    if (Mat.IsVeg[w.Get(x, y, z)]) w.Set(x, y, z, Mat.Air);
        Unit At(UnitType t, float x, float z)
        {
            int c = sim.NearestStand(Math.Clamp((int)x, 8, w.SX - 9), Math.Clamp((int)z, 8, w.SZ - 9), t.Move.Height);
            if (c < 0) throw new InvalidOperationException($"nowhere to stand by {x:0},{z:0}");
            return sim.Spawn(t, c);
        }
        void Gone(Unit u) { if (!u.Dead) { u.Dead = true; u.DiedAt = -999; } }

        // both legs broken out in the field, 25 m from the gate: a worker near lifts it and carries it into the citadel,
        // slower than it walks, and there it knits three times as fast
        var hurt = At(Kinds.Swordsman, gx - 200, gz + 20);
        sim.BreakBone(hurt, Break.LegR); sim.BreakBone(hurt, Break.LegL);
        hurt.Wounds.Splinted = Break.LegR | Break.LegL;
        var carrier = At(Kinds.Worker, gx - 160, gz + 20);
        Run(.5);
        bool lifted = false; float carried = 0, carriedMoved = 0, lx = carrier.X, lz = carrier.Z;
        double t0 = sim.Time;
        for (int i = 0; i < 240 && !hurt.Inside; i++)
        {
            Run(.25);
            if (Sim.OnShoulder(hurt))
            {
                lifted = true;
                carried += .25f; carriedMoved += MathF.Sqrt((carrier.X - lx) * (carrier.X - lx) + (carrier.Z - lz) * (carrier.Z - lz));
            }
            lx = carrier.X; lz = carrier.Z;
        }
        float pace = carried > 0 ? carriedMoved / carried / (Kinds.Worker.Move.Speed * NavGrid.CS) : 0;
        int s = (int)Math.Log2((int)Break.LegR);
        float knit0 = hurt.Wounds.Knit[s];
        Run(30);
        float knitRate = (hurt.Wounds.Knit[s] - knit0) / 30 * Sim.KnitFor;
        check("one with both legs broken is lifted by a worker near and carried into the citadel, at about three fifths of a walk; there it knits three times as fast",
            lifted && hurt.Inside && hurt.Mending && pace > .35f && pace < .8f && knitRate > 5 && carrier.Carrying == null,
            $"lifted {lifted}; in the citadel {hurt.Inside} after {sim.Time - t0 - 30:0} s; the carrier's pace {pace:0.00} of its walk; knitting ×{knitRate:0.0} (splinted ×2, by the citadel ×3)");
        Gone(hurt); Gone(carrier);

        // carrying, the carrier is struck: it puts the wounded down at once
        var hurt2 = At(Kinds.Worker, gx - 260, gz - 60);
        sim.BreakBone(hurt2, Break.LegR); sim.BreakBone(hurt2, Break.LegL);
        var carrier2 = At(Kinds.Worker, gx - 240, gz - 60);
        for (int i = 0; i < 80 && !Sim.OnShoulder(hurt2); i++) Run(.25);
        bool up2 = Sim.OnShoulder(hurt2);
        Run(1);
        sim.Hurt(carrier2, null, 5, "удар");
        Run(.1);
        check("the carrier struck puts the wounded down at once, and it lies there",
            up2 && !Sim.OnShoulder(hurt2) && hurt2.CarriedBy == null && carrier2.Carrying == null && hurt2.Down && !hurt2.Dead,
            $"on the shoulder {up2}; after the blow: carried {Sim.OnShoulder(hurt2)}, down {hurt2.Down}, the carrier {(carrier2.Carrying == null ? "free" : "still carrying")}");
        Gone(hurt2); Gone(carrier2);

        // one leg broken, limping to the citadel: a worker near comes to its side, and it goes twice as fast for it
        var limp = At(Kinds.Spearman, gx - 300, gz + 120);
        sim.BreakBone(limp, Break.LegL);
        Run(2);
        float alone = Sim.Gait(limp);
        var helper = At(Kinds.Worker, gx - 280, gz + 120);
        bool propped = false; float gaitWith = 0;
        for (int i = 0; i < 200 && !limp.Inside; i++)
        {
            Run(.25);
            if (Sim.Propped(limp)) { propped = true; gaitWith = Math.Max(gaitWith, Sim.Gait(limp)); }
        }
        check("one limping on a broken leg is helped along by a worker near, its arm over the shoulder, at twice its pace, into the citadel",
            limp.Mending && propped && gaitWith >= alone * 1.8f && limp.Inside && helper.Propping == null,
            $"mending {limp.Mending}; helped {propped}; the gait {alone:0.00} alone, {gaitWith:0.00} helped; in the citadel {limp.Inside}");
        Gone(limp); Gone(helper);

        // pinned with nobody near: it calls out, again and again
        int calls = 0;
        sim.CalledOut += _ => calls++;
        void Heap(Unit u, int r, int h)
        {
            int ux = (int)u.X, uz = (int)u.Z, uy = (int)MathF.Floor(u.Y + .01f);
            for (int y = uy; y < uy + h; y++)
                for (int z = uz - r; z <= uz + r; z++)
                    for (int x = ux - r; x <= ux + r; x++)
                        if (w.Get(x, y, z) == Mat.Air) w.Set(x, y, z, Mat.Rubble);
            u.StruckAt = sim.Time;
        }
        var buried = At(Kinds.Worker, gx - 340, gz - 140);
        Heap(buried, 1, 6);
        Run(12);
        int called = calls;
        // the player sends two (out of hearing, 75 m off) to dig it out: both dig, side by side
        var a = At(Kinds.Worker, gx - 340 + 600, gz - 140);
        var b = At(Kinds.Worker, gx - 340 + 620, gz - 140);
        int sent = sim.OrderAid(new[] { a, b }, buried);
        bool both = false, digging = false;
        for (int i = 0; i < 160 && buried.PinKg > 0; i++)
        {
            Run(.25);
            both |= a.Freeing == buried && b.Freeing == buried;
            digging |= a.Freeing == buried && a.State == UnitState.Work || b.Freeing == buried && b.State == UnitState.Work;
        }
        check("one pinned calls out for help while nobody digs, twice in 12 s; the player sends two from out of hearing, both go to it and it is dug out",
            called >= 2 && sent == 2 && both && digging && buried.PinKg == 0 && !buried.Dead,
            $"called {called} times; sent {sent}; both on their way {both}, digging {digging}; now {buried.PinKg:0} kg, {(buried.Dead ? "dead" : $"{buried.Hp:0} hp")}");
        Gone(buried); Gone(a); Gone(b);

        // the player sends one to a comrade down for good, far off: it carries it in
        var down = At(Kinds.Archer, gx - 120, gz - 180);
        sim.BreakBone(down, Break.LegR); sim.BreakBone(down, Break.LegL);
        Run(.5);
        // (both further than one of ours sees a comrade down by itself)
        var sentOne = At(Kinds.Worker, gx - 120 + 320, gz - 180);
        var other = At(Kinds.Worker, gx - 120 + 380, gz - 180);
        int n = sim.OrderAid(new[] { sentOne, other }, down);
        bool bySent = false;
        double t1 = sim.Time;
        for (int i = 0; i < 800 && !down.Inside; i++) { Run(.25); bySent |= Sim.OnShoulder(down) && down.CarriedBy == sentOne; }
        check("the player sends the chosen to one down for good: the nearest of them carries it into the citadel",
            n == 1 && bySent && down.Inside, $"sent {n}; carried by the nearest sent {bySent}; in the citadel {down.Inside} after {sim.Time - t1:0} s");
        Gone(down); Gone(sentOne); Gone(other);
    }
}
