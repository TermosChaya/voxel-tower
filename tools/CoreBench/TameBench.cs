using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The town's beasts (--tame, src/Core/Sim.Tame.cs), on a level field: a dwelling, a campfire, the kennel and the stables;
/// three workers and two archers. Hens come to the finished dwelling, three to it, and keep about its yard; a man walking
/// through them scatters them; the yard dog comes and keeps with a worker; horses and goats graze in the stables' paddock;
/// at night the dog lies by the fire; a band of the horde coming near sets the dog barking, and its third bark raises
/// the alarm; no hunter goes after a town beast and the herds count none of them; none of them takes a ladder; and they
/// cost little a tick.
/// </summary>
static class TameBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.SetSource(new MapRaids());
        sim.WildTarget = 0;
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        int fx0 = (site.KeepX - 460) & ~7, fz0 = (site.KeepZ - 140) & ~7, fg = w.SurfAt(fx0 + 160, fz0 + 140);
        for (int z = fz0; z < fz0 + 280; z++)
            for (int x = fx0; x < fx0 + 320; x++)
                for (int y = fg - 8; y < fg + 120; y++) w.Set(x, y, z, y < fg ? (y < fg - 4 ? Mat.Rock : y == fg - 1 ? Mat.Grass : Mat.Soil) : Mat.Air);
        Run(.5);
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        sim.Stock[Stockpile.Wood] = 4000.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Stone] = 4000.0 * VoxelWorld.PerM3; sim.Stock[Stockpile.Earth] = 4000.0 * VoxelWorld.PerM3;
        sim.Stock.Gold = 5000;
        sim.InstantLabour = true;
        // the dwelling, a campfire south of it
        int hx0 = fx0 + 30, hz0 = fz0 + 40, hx1 = hx0 + 36, hz1 = hz0 + 55;
        var house = Shapes.House(w, site, hx0, hz0, hx1, hz1, new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true }, Shapes.LevelTop(w, (hx0 + hx1) / 2, (hz0 + hz1) / 2, 32), out var door, out string why, out var arched);
        if (house == null) { check("the town's beasts: the dwelling is planned", false, why); return; }
        sim.DesignateBuild(house);
        sim.Arch(arched);
        sim.PlanHouse(door, hx0, hz0, hx1, hz1);
        int cx = fx0 + 110, cz = fz0 + 150;
        sim.PlaceDecor(DecorKind.Campfire, cx, cz, 0);
        for (int i = 0; i < 4; i++) { sim.CompleteAll(); Run(2); }
        // the kennel and the stables, east in the field
        sim.Knowledge = 200;
        sim.Learn("b_kennel"); sim.Learn("b_stables");
        string kennel = sim.PutUp(Sim.TownKindOf("kennel"), fx0 + 270, fz0 + 60, false, out var kb);
        for (int i = 0; i < 30 && kb is { Ready: false }; i++) { sim.CompleteAll(); Run(2); }
        string stables = sim.PutUp(Sim.TownKindOf("stables"), fx0 + 230, fz0 + 190, false, out var sb);
        for (int i = 0; i < 40 && sb is { Ready: false }; i++) { sim.CompleteAll(); Run(2); }
        sim.DressTown();
        sim.InstantLabour = false;
        var folk = new List<Unit>();
        foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Archer, Kinds.Archer })
            folk.Add(sim.Spawn(t, sim.NearestStand(cx - 30 + folk.Count * 12, cz - 20, t.Move.Height)));
        Console.WriteLine($"     the town: the dwelling {(sim.Zones.Exists(z => z.Kind == Zone.Dwelling && z.Closed) ? "closed" : "open")}, the kennel {kennel ?? (kb?.Ready == true ? "ready" : "not ready")}, the stables {stables ?? (sb?.Ready == true ? "ready" : "not ready")}");

        // ---- the beasts come, one by one
        int before = sim.TameCount();
        sim.TownBeasts = true;
        sim.Clock.Night = false; sim.Clock.DawnLeft = 0; sim.Clock.Left = DayClock.DayLength / 2;
        Run(80);
        var dwelling = sim.Zones.Find(z => z.Kind == Zone.Dwelling && z.Closed);
        if (Environment.GetEnvironmentVariable("TAME_DEBUG") == "1")
        {
            foreach (var z in sim.Zones.Where(z => z.Kind == Zone.Dwelling)) Console.WriteLine($"     a dwelling zone {z.X0},{z.Z0} to {z.X1},{z.Z1} closed {z.Closed}");
            foreach (var u in sim.Units.Where(u => !u.Dead && u.T.Tame)) Console.WriteLine($"     {u.T.Key} at {u.X:0},{u.Z:0}, home {(u.Home >= 0 ? sim.Nav.Feet(u.Home).ToString() : "none")}, {u.State}");
        }
        var hens = sim.Units.Where(u => !u.Dead && u.T == Kinds.Hen).ToList();
        bool henNear = dwelling != null && hens.All(h => h.X > dwelling.X0 - 80 && h.X < dwelling.X1 + 80 && h.Z > dwelling.Z0 - 80 && h.Z < dwelling.Z1 + 80);
        check("the town's beasts: none before the town asks for them; then three hens to the dwelling, about its yard, and the yard dog",
            before == 0 && hens.Count == 3 && henNear && sim.TameCount(Kinds.Cur) == 1,
            $"before {before}; hens {hens.Count} (about the dwelling {henNear}), dogs {sim.TameCount(Kinds.Cur)}");
        int horses = sim.TameCount(Kinds.Horse), goats = sim.TameCount(Kinds.Goat);
        bool inPen = sb != null && sim.Units.Where(u => !u.Dead && (u.T == Kinds.Horse || u.T == Kinds.Goat)).All(u => MathF.Abs(u.X - sb.CX) < 90 && MathF.Abs(u.Z - sb.CZ) < 110);
        check("the town's beasts: two horses and two goats graze by the stables once they work",
            sb is { Ready: true } && horses == 2 && goats == 2 && inPen, $"stables ready {sb?.Ready}, horses {horses}, goats {goats}, by the stables {inPen}");

        // ---- a man walks through the hens
        int scattered = sim.Scattered;
        if (hens.Count > 0)
        {
            var h0 = hens[0];
            var walker = folk[0];
            int far = sim.NearestStand((int)(h0.X + (h0.X - walker.X) * 3), (int)(h0.Z + (h0.Z - walker.Z) * 3), 2);
            sim.Command(new[] { walker }, far >= 0 ? far : sim.NearestStand((int)h0.X + 60, (int)h0.Z, 2));
            for (int k = 0; k < 300 && sim.Scattered == scattered; k++) sim.Step(.05f);
        }
        check("the town's beasts: a man walking through the hens scatters them", sim.Scattered > scattered, $"scattered {sim.Scattered - scattered}");

        // ---- the dog keeps with a worker by day
        var dog = sim.Units.FirstOrDefault(u => !u.Dead && u.T == Kinds.Cur);
        int nearTicks = 0, ticks = 0;
        for (int k = 0; k < 600; k++)
        {
            sim.Step(.05f);
            if (dog == null || k % 10 != 0) continue;
            ticks++;
            if (sim.Units.Any(o => !o.Dead && o.T == Kinds.Worker && MathF.Sqrt((o.X - dog.X) * (o.X - dog.X) + (o.Z - dog.Z) * (o.Z - dog.Z)) < 10 * VoxelWorld.PerMetre)) nearTicks++;
        }
        check("the town's beasts: by day the yard dog keeps within 10 m of a worker most of the time",
            dog != null && nearTicks >= ticks * .7, $"near a worker {nearTicks} of {ticks} looks");

        // ---- at night the dog lies by the fire
        sim.Clock.Night = true;
        Run(25);
        float byFire = dog == null ? 999 : MathF.Sqrt((dog.X - cx) * (dog.X - cx) + (dog.Z - cz) * (dog.Z - cz)) / VoxelWorld.PerMetre;
        check("the town's beasts: at night the yard dog lies by the fire", dog != null && byFire < 6, $"{byFire:0.0} m from the fire");
        sim.Clock.Night = false; sim.Clock.Left = DayClock.DayLength / 2;
        Run(5);

        // ---- no hunter goes after them, the herds count none of them, none takes a ladder
        var archers = folk.Where(u => u.T == Kinds.Archer).ToList();
        int sent = hens.Count > 0 ? sim.Hunt(archers, hens.FirstOrDefault(h => !h.Dead)) : -1;
        float hurtByOurs = 0;
        sim.Hurting += (u, dmg, cause) => { if (u.T.Tame && cause != null && (cause == Kinds.Archer.Name || cause == Kinds.Worker.Name)) hurtByOurs += dmg; };
        Run(20);
        int wildOnly = sim.Units.Count(u => !u.Dead && u.Team == Team.Wild && !u.T.Tame);
        check("the town's beasts: nobody hunts them, the herds count none of them, and none of them climbs a ladder",
            sent == 0 && hurtByOurs == 0 && sim.WildAlive() == wildOnly && Kinds.TameKinds.All(t => !t.Move.Ladders),
            $"archers sent after a hen {sent}, hurt by ours {hurtByOurs:0}, the herds counted {sim.WildAlive()} with {wildOnly} wild and {sim.TameCount()} of the town's about");

        // ---- the horde comes: the dog barks and raises the alarm
        int barks = sim.Barks;
        bool alarmBefore = sim.Alarm;
        Unit g = null;
        if (dog != null)
        {
            g = sim.Spawn(Kinds.Goblin, sim.NearestStand((int)dog.X - 150, (int)dog.Z, Kinds.Goblin.Move.Height));
            for (int k = 0; k < 200 && !sim.Alarm; k++) sim.Step(.05f);
        }
        check("the town's beasts: a raider near sets the yard dog barking, and its third bark raises the alarm",
            !alarmBefore && sim.Barks - barks >= 3 && sim.Alarm && sim.DogAlarms == 1, $"barks {sim.Barks - barks}, alarm {sim.Alarm}, raised by the dog {sim.DogAlarms}");
        if (g != null) { g.Dead = true; g.DiedAt = sim.Time; }
        sim.StandDown();

        // ---- what they cost
        Run(5);
        double t0 = BenchClock.CpuMs();
        for (int k = 0; k < 200; k++) sim.Step(.05f);
        double with = (BenchClock.CpuMs() - t0) / 200;
        foreach (var u in sim.Units) if (u.T.Tame) { u.Dead = true; u.DiedAt = -999; }
        sim.TownBeasts = false;
        Run(1);
        t0 = BenchClock.CpuMs();
        for (int k = 0; k < 200; k++) sim.Step(.05f);
        double without = (BenchClock.CpuMs() - t0) / 200;
        check("the town's beasts: a tick with them costs under half a millisecond more than without",
            with - without < .5, $"with {with:0.00} ms, without {without:0.00} ms");
    }
}
