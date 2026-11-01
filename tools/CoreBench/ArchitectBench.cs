using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The architect (--architect, Sim.Architect.cs and Sim.Architect.Survey.cs). On bare ground (an outpost's valley
/// levelled forty metres about its place, a road through it from east to west, six workers, the stores full) he plans a
/// line round the place and has it built — the wall, its gate on the road, the posts, the ditch and the stakes — and
/// how many minutes each takes is said; nothing falls, the place is left as it was, an engine of ours is stood inside
/// the line by the horde's side, a hole knocked in what he built is mended, and through his line the horde has no way
/// to the stone but through a gate. With little stone he puts up a palisade; switched off he marks nothing. Then what
/// he does with what stands (the workers as an army, at once): a ring of stone walls the household built by hand —
/// his line is that ring, and he adds no wall and no palisade along it, only what it lacks; two sides of such a ring —
/// taken into his line, the rest closed, nothing laid along them; the old fort a new game begins with — his line is the
/// fort's, he mends its breaches and hangs its gate, lays nothing new by its curtain but what mends it, a tower or a
/// stair, and the citadel is shut in by it; and what he builds after the player walls the way he meant to build is
/// planned again round it. ARCHITECT_SAY=1 tells what he says and plans, ARCHITECT_ONLY=bare|hand|half|fort runs one part,
/// ARCHITECT_REAL=1 has six workers (not an army) at the old fort for half an hour and tells what is left marked and why.
/// </summary>
static class ArchitectBench
{
    static bool Say => Environment.GetEnvironmentVariable("ARCHITECT_SAY") == "1";

    public static void Run(Action<string, bool, string> check)
    {
        string only = Environment.GetEnvironmentVariable("ARCHITECT_ONLY") ?? "";
        if (only is "" or "bare") Bare(check);
        if (only is "" or "hand") HandRing(check);
        if (only is "" or "half") HalfRing(check);
        if (only is "" or "fort") OldFort(check);
    }

    // ------------------------------------------------------------ bare ground

    static void Bare(Action<string, bool, string> check)
    {
        // ---- the stores full: the ring goes up in stone
        var sim = Valley(7, 600, 300, out int cx, out int cz);
        int falls = 0;
        // (what falls about the ring from now on, the ground about it let settle first)
        Step(sim, 20);
        sim.Collapsed += (n, x, y, z) => { if (Math.Abs(x - cx) < 240 && Math.Abs(z - cz) < 240) { falls += n; if (Say) Console.WriteLine($"      [{sim.Time:0}] fell {n} at ({x - cx:0}, {y:0}, {z - cz:0})"); } };
        if (Say) sim.Said += (t, _, _, _) => Console.WriteLine($"      [{sim.Time:0}] {t}");
        long placeBefore = PlaceSum(sim, cx, cz);
        var arch = sim.Spawn(Kinds.Architect, sim.NearestStand(cx + 12, cz + 12, 2));
        var engine = sim.Spawn(Kinds.Mangonel, sim.NearestStand(cx - 20, cz - 20, Kinds.Mangonel.Move.Height));
        for (int i = 0; i < Kinds.Mangonel.Crew; i++) sim.Spawn(Kinds.Crewman, sim.NearestStand(cx - 24 - i * 4, cz - 26, 2)).Escort = engine;
        var at = new Dictionary<string, double>();
        double t0 = sim.Time;
        for (int s = 0; s < 60 * 60 && !AllDone(sim); s++)
        {
            Step(sim, 1);
            var p = sim.ArchPlan;
            if (p == null) continue;
            foreach (var (name, mask) in Stages(sim))
                if (!at.ContainsKey(name) && mask != 0 && (p.Done & mask) == mask) at[name] = (sim.Time - t0) / 60;
        }
        var plan = sim.ArchPlan;
        Tell(sim);
        string Took(string k) => at.TryGetValue(k, out double m) ? $"{m:0.0}" : "—";
        bool all = AllDone(sim);
        check("on bare ground the architect plans a line round the outpost's place and the workers raise it whole — the wall, its gate, the corners, the ditch and the stakes — nothing falling",
            plan != null && plan.Stone && all && falls == 0 && sim.ArchMarked >= 8 && plan.Ring.Count >= 4,
            $"line {(plan == null ? "none" : $"{plan.Ring.Count} corners, reach {plan.H / 8.0:0} m, in {(plan.Stone ? "stone" : "timber")}, «{plan.Found}»")}; minutes: wall {Took("wall")}, gate {Took("gate")}, corners {Took("corners")}, ditch {Took("ditch")}, stakes {Took("stakes")}, all {Took("all")}; " +
            $"pieces marked {sim.ArchMarked}, fallen {falls}; doing «{sim.ArchDoing}», short of «{sim.ArchLacks}»");
        // the gate on the road (it runs east and west through the place), the place untouched, the engine inside the line
        // by the horde's side
        var gate = sim.ArchGate();
        bool onRoad = gate is { } g0 && Math.Abs(g0.z - (cz + 4)) < 24 && (g0.side == 1 || g0.side == 3);
        var (ex, ez) = sim.Site.EntryOn(sim.Site.EntrySide, .5);
        float hx = ex - cx, hz = ez - cz, hl = MathF.Sqrt(hx * hx + hz * hz);
        bool inside = sim.InsideArchRing(engine.X, engine.Z);
        float toward = ((engine.X - cx) * hx + (engine.Z - cz) * hz) / hl;
        check("his gate is on the road where it crosses his line, nothing of his stands on the outpost's place, and our engine is stood inside the line on the horde's side",
            onRoad && PlaceSum(sim, cx, cz) == placeBefore && inside && toward > 0,
            $"gate {(gate is { } g1 ? $"at ({g1.x - cx}, {g1.z - cz}) looking out {g1.side}" : "none")}, the road at z {4}; the place {(PlaceSum(sim, cx, cz) == placeBefore ? "untouched" : "built on")}; the engine at ({engine.X - cx:0}, {engine.Z - cz:0}), inside {inside}, {toward:0} toward the horde");

        // a hole knocked into his wall on the horde's side: he has it mended
        var hole = WallToward(sim, cx, cz, hx / hl, hz / hl);
        int knocked = 0;
        var was = new List<(int x, int y, int z, byte m)>();
        if (hole is { } h)
        {
            int gy = sim.World.GroundTop(h.x, h.z);
            for (int y = gy + 4; y < gy + 14; y++)
                for (int dz = -6; dz <= 6; dz++)
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        int x = h.x + dx, z = h.z + dz;
                        byte m = sim.World.Get(x, y, z);
                        if (!Mat.IsMasonry(m)) continue;
                        was.Add((x, y, z, m)); sim.World.Set(x, y, z, Mat.Air); knocked++;
                    }
        }
        int mended0 = sim.ArchMended;
        Step(sim, 120);
        int back = was.Count(v => sim.World.Get(v.x, v.y, v.z) == v.m);
        check("a hole knocked into his wall he has mended by the workers",
            knocked > 100 && sim.ArchMended > mended0 && back >= knocked * 9 / 10, $"{knocked} knocked out, {back} back, mendings {mended0} → {sim.ArchMended}");

        // ---- little stone: a palisade first; switched off: nothing marked
        var poor = Valley(7, 4, 300, out int px, out int pz);
        var parch = poor.Spawn(Kinds.Architect, poor.NearestStand(px + 12, pz + 12, 2));
        parch.Commands = false;
        Step(poor, 40);
        int markedOff = poor.ArchMarked;
        parch.Commands = true;
        Step(poor, 40);
        check("switched off he marks nothing; with little stone in the stores he puts the line up as a palisade",
            markedOff == 0 && poor.ArchPlan is { Stone: false } && poor.ArchMarked > 0,
            $"marked while off {markedOff}; then plan in {(poor.ArchPlan == null ? "none" : poor.ArchPlan.Stone ? "stone" : "timber")}, marked {poor.ArchMarked}; «{poor.ArchDoing}»");

        // ---- the horde's way to the stone: shut out by his line (it must break in), straight on the bare place; and the
        // same raid on each, told
        var bareSim = Valley(7, 600, 300, out _, out _);
        int wayFortified = WayIn(sim, cx, cz, out bool byGates, out string crossed), wayBare = WayIn(bareSim, cx, cz, out _, out _);
        RaidLosses(sim, out string f1, out _);
        RaidLosses(bareSim, out string f2, out _);
        check("through his line the horde has no way in to the outpost's stone but through a gate it must hack, where on the bare place it walks straight there",
            wayBare > 0 && (wayFortified < 0 || byGates), $"the way in: {(wayFortified < 0 ? "none" : wayFortified + $" cells, {crossed}")} through the line, {wayBare} cells on the bare place; a raid of eight on each — fortified: {f1}; bare: {f2}");
    }

    // ------------------------------------------------------------ what stands

    /// <summary>A ring of stone walls the household built by hand round the place, its gate on the road.</summary>
    static void HandRing(Action<string, bool, string> check)
    {
        const int R = 120;
        var sim = Valley(7, 600, 300, out int cx, out int cz);
        if (Say) sim.Said += (t, _, _, _) => Console.WriteLine($"      [{sim.Time:0}] {t}");
        var corners = new[] { (cx - R, cz - R), (cx + R, cz - R), (cx + R, cz + R), (cx - R, cz + R) };
        sim.RaiseWalls(corners, closed: true, 24, 8, gates: true);
        int gates0 = sim.Gates.Count;
        var built = Snapshot(sim, cx, cz, R + 80);
        var wall = WallColumns(sim, cx, cz, R + 16);
        sim.InstantLabour = true;
        sim.Spawn(Kinds.Architect, sim.NearestStand(cx + 12, cz + 12, 2));
        RunToEnd(sim);
        var plan = sim.ArchPlan;
        Tell(sim);
        int onLine = wall.Count(c => NearLine(plan, c.x, c.z, 12));
        var (timber, beside) = Laid(sim, built, wall, plan);
        int newWalls = plan?.Works.Count(w => w.Kind == ArchWorkKind.Wall) ?? -1;
        var fort = Fortification.Measure(sim, cx, cz);
        int way = WayIn(sim, cx, cz, out bool byGates, out string crossed);
        check("a ring of stone walls built by hand is his line: no new wall and no palisade along it, only what it lacks; once that is done the place is shut in, the way in only through its gate",
            plan != null && onLine >= wall.Count * 9 / 10 && newWalls == 0 && timber == 0 && beside == 0 && fort.Ring >= .95f && (way < 0 || byGates) && sim.Gates.Count == gates0,
            $"«{plan?.Found}»; {onLine} of {wall.Count} columns of the hand-built wall on his line; new walls {newWalls}, timber laid {timber}, voxels laid beside the wall not of a tower, a stair or mending {beside}; " +
            $"works: {Works(plan)}; {fort.Words()}; the way in {(way < 0 ? "none" : $"{way} cells, {crossed}")}; gates {gates0} → {sim.Gates.Count}");
    }

    /// <summary>Two sides of such a ring, the north and the west: taken into his line, the rest closed.</summary>
    static void HalfRing(Action<string, bool, string> check)
    {
        const int R = 120;
        var sim = Valley(7, 600, 300, out int cx, out int cz);
        if (Say) sim.Said += (t, _, _, _) => Console.WriteLine($"      [{sim.Time:0}] {t}");
        sim.RaiseWalls(new[] { (cx + R, cz - R), (cx - R, cz - R), (cx - R, cz + R) }, closed: false, 24, 8);
        var built = Snapshot(sim, cx, cz, R + 80);
        var wall = WallColumns(sim, cx, cz, R + 16);
        sim.InstantLabour = true;
        sim.Spawn(Kinds.Architect, sim.NearestStand(cx + 12, cz + 12, 2));
        RunToEnd(sim);
        var plan = sim.ArchPlan;
        Tell(sim);
        int onLine = wall.Count(c => NearLine(plan, c.x, c.z, 12));
        var (timber, beside) = Laid(sim, built, wall, plan);
        var fort = Fortification.Measure(sim, cx, cz);
        int way = WayIn(sim, cx, cz, out bool byGates, out string crossed), walk = WalkIn(sim, cx, cz, out string walked);
        var gate = sim.ArchGate();
        check("two sides built by hand are taken into his line and the rest closed with new wall, none laid along them; once done the place is shut in, the way in only through his gate on the road",
            plan != null && onLine >= wall.Count * 85 / 100 && beside == 0 && fort.Ring >= .95f && (way < 0 || byGates) && walk > 0 && gate is { } g && Math.Abs(g.z - (cz + 4)) < 24,
            $"«{plan?.Found}»; {onLine} of {wall.Count} columns of the hand-built sides on his line; voxels laid beside them not of a tower, a stair or mending {beside}, timber {timber}; works: {Works(plan)}; " +
            $"{fort.Words()}; the horde's way in {(way < 0 ? "none" : $"{way} cells, {crossed}")}; a worker's, the gates open: {(walk < 0 ? "none" : $"{walk} cells, {walked}")}; gate {(gate is { } g2 ? $"at ({g2.x - cx}, {g2.z - cz})" : "none")}");

        // ---- the player walls part of the way he meant to build before he marks it: he plans again round it
        var sim2 = Valley(7, 600, 300, out cx, out cz);
        sim2.Spawn(Kinds.Architect, sim2.NearestStand(cx + 12, cz + 12, 2));
        Step(sim2, 5);
        var first = sim2.ArchPlan;
        // a stretch of his new wall not marked yet, walled by the player along its line
        int wi = first == null ? -1 : first.Works.FindIndex(w => w.Kind == ArchWorkKind.Wall && w.Stage == 0 && (first.Marked >> first.Works.IndexOf(w) & 1) == 0 && w.Line.Length >= 4);
        int plans0 = sim2.ArchPlans;
        bool walled = false;
        if (wi >= 0)
        {
            // two metres out from his line, the length of it
            var w = first.Works[wi];
            var pts = new List<(int, int)>();
            for (int i = 0; i + 1 < w.Line.Length; i += 2)
            {
                float dx = w.Line[i] - cx, dz = w.Line[i + 1] - cz, d = MathF.Max(1, MathF.Sqrt(dx * dx + dz * dz));
                pts.Add((w.Line[i] + (int)(dx / d * 16), w.Line[i + 1] + (int)(dz / d * 16)));
            }
            sim2.RaiseWalls(pts, closed: false, 24, 8);
            walled = true;
        }
        Step(sim2, 90);
        var again = sim2.ArchPlan;
        bool doubled = again != null && wi >= 0 && again.Works.Exists(w => w.Kind == ArchWorkKind.Wall && w.Stage == 0 && (again.Marked >> again.Works.IndexOf(w) & 1) == 0 && SameLine(w, first.Works[wi]));
        check("a stretch he meant to build, walled by the player before he marked it: he plans again and takes it as it stands",
            walled && sim2.ArchPlans > plans0 && !doubled,
            $"walled {walled}; plans {plans0} → {sim2.ArchPlans}; the stretch planned again as new {doubled}; «{again?.Found}»");
    }

    /// <summary>The old fort a new game begins with, half in ruin, round the citadel.</summary>
    static void OldFort(Action<string, bool, string> check)
    {
        var mp = new MapParams { Seed = 3, Kind = ValleyKind.Forest, Hexagon = true, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize };
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site) { WildTarget = 0, QuietLife = true };
        sim.Clock.Left = 1e9;
        foreach (var u in sim.Units) if (u.Team == Team.Wild) { u.Dead = true; u.DiedAt = -999; }
        if (Say) sim.Said += (t, _, _, _) => Console.WriteLine($"      [{sim.Time:0}] {t}");
        StartingFort.Raise(sim);
        int cx = site.KeepX, cz = site.KeepZ;
        sim.Stock[Stockpile.Stone] = 600 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] = 300 * VoxelWorld.PerM3;
        sim.Stock.Gold = 9999;
        var (fx0, fz0, fx1, fz1) = sim.FortArea;
        var sound0 = sim.Soundness(fx0, fz0, fx1, fz1);
        // the curtain: the columns meant to stand in stone a man and a half and more
        var curtain = new List<(int x, int z)>();
        for (int z = fz0; z <= fz1; z++)
            for (int x = fx0; x <= fx1; x++)
            {
                int g = site.HTop[z * w.SX + x], high = 0;
                for (int y = g; y < g + 48; y++) { byte m = sim.MeantAt(x, y, z); if (Mat.IsMasonry(m)) high = y + 1 - g; }
                if (high >= 12) curtain.Add((x, z));
            }
        var built = Snapshot(sim, cx, cz, 220);
        if (Environment.GetEnvironmentVariable("ARCHITECT_REAL") == "1")
        {
            // (to look into: six workers at it for half an hour, and what is left marked and why)
            for (int i = 0; i < 6; i++) sim.Spawn(Kinds.Worker, sim.NearestStand(cx + 30 + i * 4, cz + 10, 2));
            sim.Spawn(Kinds.Architect, sim.NearestStand(cx + 30, cz, 2));
            Step(sim, 1800);
            Tell(sim);
            foreach (var piece in sim.ArchPieces(sim.ArchPlan))
            {
                if ((sim.ArchPlan.Done >> piece.Index & 1) != 0) continue;
                int shown = 0;
                for (int z = piece.Z0; z <= piece.Z1; z++)
                    for (int x = piece.X0; x <= piece.X1; x++)
                        for (int y = 1; y < sim.World.SY && shown < 8; y++)
                        {
                            byte o = sim.Orders.At(x, y, z);
                            if (o == 0) continue;
                            shown++;
                            Console.WriteLine($"      left of {piece.Name}: ({x - cx}, {y - site.KeepBase}, {z - cz}) order {o}, there {Mat.NameOf(sim.World.Get(x, y, z))}, under {Mat.NameOf(sim.World.Get(x, y - 1, z))}, meant {Mat.NameOf(sim.MeantAt(x, y, z))}");
                        }
            }
            foreach (var u in sim.Units) if (!u.Dead && u.Team == Team.Player) Console.WriteLine($"      {u.T.Name} #{u.Id} {u.State} «{u.Why}» at ({u.X - cx:0}, {u.Y - site.KeepBase:0}, {u.Z - cz:0})");
            return;
        }
        sim.InstantLabour = true;
        sim.Spawn(Kinds.Architect, sim.NearestStand(cx + 30, cz, 2));
        // a few pieces done, the game saved and read back: the plan goes on as it was
        for (int s = 0; s < 600 && !(sim.ArchPlan is { } pp && System.Numerics.BitOperations.PopCount(pp.Done) >= 3); s++) Step(sim, 1);
        var mid = sim.ArchPlan;
        int works = mid?.Works.Count ?? -1;
        ulong done = mid?.Done ?? 0;
        string said = mid?.Found ?? "";
        sim = Reload(sim);
        sim.InstantLabour = true;
        bool kept = sim.ArchPlan is { } back && back.Works.Count == works && back.Done == done && back.Found == said && back.Ring.Count == mid.Ring.Count;
        RunToEnd(sim);
        var plan = sim.ArchPlan;
        Tell(sim);
        int onLine = curtain.Count(c => NearLine(plan, c.x, c.z, 12));
        int newWalls = plan?.Works.Count(k => k.Kind == ArchWorkKind.Wall && k.Stage == 0) ?? -1;
        var (_, beside) = Laid(sim, built, curtain, plan);
        var sound = sim.Soundness(fx0, fz0, fx1, fz1);
        int way = WayIn(sim, cx, cz, out bool byGates, out string crossed), walk = WalkIn(sim, cx, cz, out string walked);
        bool hung = sim.FortGate is { Broken: false };
        check("in the old fort his line is the fort's: he lays no new wall by its curtain but what mends it, a tower or a stair, mends its breaches and hangs its gate, and the citadel is shut in, the way in only through the gate",
            plan != null && kept && onLine >= curtain.Count * 85 / 100 && newWalls == 0 && beside == 0 && sound.standing >= sound.meant * 95 / 100 && hung && (way < 0 || byGates) && walk > 0,
            $"«{plan?.Found}»; saved with {System.Numerics.BitOperations.PopCount(done)} pieces done and read back {(kept ? "as it was" : "changed")}; {onLine} of {curtain.Count} columns of the curtain on his line; new walls {newWalls}; voxels laid by the curtain not of mending, a tower, a stair or stone for the palisade {beside}; " +
            $"the fort sound {sound0.standing * 100 / Math.Max(1, sound0.meant)} % → {sound.standing * 100 / Math.Max(1, sound.meant)} %, its gate hung {hung}; works: {Works(plan)}; the horde's way in {(way < 0 ? "none" : $"{way} cells, {crossed}")}; a worker's, the gates open: {(walk < 0 ? "none" : $"{walk} cells, {walked}")}");
    }

    // ------------------------------------------------------------ helpers

    /// <summary>The game saved and read back into a valley made again from its seed.</summary>
    static Sim Reload(Sim s)
    {
        var ms = new System.IO.MemoryStream();
        s.Save(ms, 3, "architect", "");
        ms.Position = 0;
        using var reader = new SaveReader(ms);
        var mp = new MapParams { Seed = reader.Seed, Kind = reader.Kind, Hexagon = reader.Hexagon, SizeX = reader.SizeX, SizeZ = reader.SizeZ, Holding = reader.Holding };
        var w2 = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site2 = MapGen.Generate(w2, mp);
        w2.Compact();
        reader.Valley(w2, site2);
        var sim2 = new Sim(w2, site2) { WildTarget = 0, QuietLife = true };
        sim2.Load(reader);
        sim2.Clock.Left = 1e9;
        return sim2;
    }

    static void Tell(Sim sim)
    {
        if (!Say || sim.ArchPlan is not { } plan) return;
        Console.WriteLine($"      plan: «{plan.Found}», {plan.Ring.Count} corners, reach {plan.H}, made in {sim.ArchPlanMs:0} ms");
        foreach (var piece in sim.ArchPieces(plan))
            Console.WriteLine($"      piece {piece.Index} {piece.Work} «{piece.Name}» stage {piece.Stage}: marked {(plan.Marked >> piece.Index & 1)}, done {(plan.Done >> piece.Index & 1)}, after {piece.After:x}, box ({piece.X0},{piece.Z0})–({piece.X1},{piece.Z1})");
        foreach (var u in sim.Units) if (!u.Dead && u.Team == Team.Player && u.T == Kinds.Architect) Console.WriteLine($"      {u.T.Name} «{u.Why}»");
    }

    static string Works(ArchPlan plan) => plan == null ? "none" : string.Join(", ", plan.Works.GroupBy(w => w.Kind).Select(g => $"{g.Key} {g.Count()}"));

    /// <summary>The architect left to it till all is done (or an hour), the workers an army.</summary>
    static void RunToEnd(Sim sim)
    {
        for (int s = 0; s < 60 * 60 && !AllDone(sim); s++) Step(sim, 1);
        // (and what he mends after, a minute)
        Step(sim, 40);
    }

    /// <summary>What was free and what was meant about the place before he came: packed voxels of ours standing then.</summary>
    static HashSet<int> Snapshot(Sim sim, int cx, int cz, int r)
    {
        var set = new HashSet<int>();
        var w = sim.World;
        for (int z = cz - r; z <= cz + r; z++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (!w.OnLand(x, z)) continue;
                int g = sim.Site.HTop[z * w.SX + x];
                for (int y = Math.Max(1, g - 2); y < Math.Min(w.SY, g + 60); y++)
                {
                    byte m = w.Get(x, y, z);
                    if (Mat.IsPlayer[m] || sim.MeantAt(x, y, z) != 0) set.Add(WaterPlanner.Pack(x, y, z));
                }
            }
        return set;
    }

    /// <summary>The columns of our walls about the place, a man and a half high and more.</summary>
    static List<(int x, int z)> WallColumns(Sim sim, int cx, int cz, int r)
    {
        var list = new List<(int x, int z)>();
        var w = sim.World;
        for (int z = cz - r; z <= cz + r; z++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (!w.OnLand(x, z)) continue;
                int g = sim.Site.HTop[z * w.SX + x], top = w.SurfAt(x, z);
                if (top - g >= 12 && Mat.IsPlayer[w.Get(x, top - 1, z)]) list.Add((x, z));
            }
        return list;
    }

    /// <summary>
    /// What he had laid since the snapshot: timber anywhere about (posts aside), and voxels within two metres of the
    /// given wall columns that are not mending what was meant there before, nor in a tower, a post, a stair, a raised
    /// stretch, a gate or the stone that takes a palisade's place.
    /// </summary>
    static (int timber, int beside) Laid(Sim sim, HashSet<int> before, List<(int x, int z)> wall, ArchPlan plan)
    {
        var w = sim.World;
        int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
        foreach (var (x, z) in wall) { x0 = Math.Min(x0, x); z0 = Math.Min(z0, z); x1 = Math.Max(x1, x); z1 = Math.Max(z1, z); }
        if (wall.Count == 0) return (0, 0);
        x0 -= 80; z0 -= 80; x1 += 80; z1 += 80;
        int nx = x1 - x0 + 1;
        var near = new bool[nx * (z1 - z0 + 1)];
        foreach (var (x, z) in wall)
            for (int dz = -16; dz <= 16; dz++)
                for (int dx = -16; dx <= 16; dx++) near[(z + dz - z0) * nx + x + dx - x0] = true;
        // (where new wall meets what stands it is laid by it, three metres at each end of it)
        var joints = new List<(int x, int z)>();
        if (plan != null)
            foreach (var k in plan.Works)
                if (k.Kind == ArchWorkKind.Wall && k.Line.Length >= 4) { joints.Add((k.Line[0], k.Line[1])); joints.Add((k.Line[^2], k.Line[^1])); }
        var allowed = new List<Sim.ArchPiece>();
        if (plan != null)
            foreach (var piece in sim.ArchPieces(plan))
                if (piece.Work is ArchWorkKind.Tower or ArchWorkKind.Post or ArchWorkKind.Stair or ArchWorkKind.Raise or ArchWorkKind.Gate or ArchWorkKind.Gatehouse or ArchWorkKind.Mend or ArchWorkKind.Hang
                    || piece.Work == ArchWorkKind.Wall && piece.Stage == 5)
                    allowed.Add(piece);
        int timber = 0, beside = 0;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (!w.OnLand(x, z)) continue;
                int g = sim.Site.HTop[z * w.SX + x];
                for (int y = Math.Max(1, g - 2); y < Math.Min(w.SY, g + 60); y++)
                {
                    byte m = w.Get(x, y, z);
                    if (!Mat.IsPlayer[m] || Mat.Passable[m] || before.Contains(WaterPlanner.Pack(x, y, z))) continue;
                    bool inAllowed = allowed.Exists(a => x >= a.X0 && x <= a.X1 && z >= a.Z0 && z <= a.Z1) || joints.Exists(j => Math.Abs(j.x - x) < 24 && Math.Abs(j.z - z) < 24);
                    if (m == Mat.Wood && !inAllowed) timber++;
                    if (near[(z - z0) * nx + x - x0] && !inAllowed && m != Mat.Gate && m != Mat.IronGate)
                    {
                        if (beside < 3 && Say) Console.WriteLine($"      laid beside the wall: {Mat.NameOf(m)} at ({x}, {y}, {z})");
                        beside++;
                    }
                }
            }
        return (timber, beside);
    }

    /// <summary>Whether a column is within so far of the plan's line.</summary>
    static bool NearLine(ArchPlan plan, int x, int z, double by)
    {
        if (plan == null || plan.Ring.Count < 2) return false;
        for (int i = 0; i < plan.Ring.Count; i++)
        {
            var (ax, az) = plan.Ring[i]; var (bx, bz) = plan.Ring[(i + 1) % plan.Ring.Count];
            double dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz, t = l2 < 1e-9 ? 0 : Math.Clamp(((x - ax) * dx + (z - az) * dz) / l2, 0, 1);
            double ex = ax + dx * t - x, ez = az + dz * t - z;
            if (ex * ex + ez * ez <= by * by) return true;
        }
        return false;
    }

    static bool SameLine(ArchWork a, ArchWork b)
    {
        if (a.Line.Length < 4 || b.Line.Length < 4) return false;
        double ax = (a.Line[0] + a.Line[^2]) / 2.0, az = (a.Line[1] + a.Line[^1]) / 2.0, bx = (b.Line[0] + b.Line[^2]) / 2.0, bz = (b.Line[1] + b.Line[^1]) / 2.0;
        return Math.Abs(ax - bx) < 16 && Math.Abs(az - bz) < 16;
    }

    /// <summary>The first column of our stone wall going out from the place a way.</summary>
    static (int x, int z)? WallToward(Sim sim, int cx, int cz, float ux, float uz)
    {
        var w = sim.World;
        for (int r = 24; r < 400; r++)
        {
            int x = cx + (int)(ux * r), z = cz + (int)(uz * r);
            int top = w.SurfAt(x, z), g = w.GroundTop(x, z);
            if (top - g >= 12 && Mat.IsMasonry(w.Get(x, top - 1, z))) return (x + (int)(ux * 4), z + (int)(uz * 4));
        }
        return null;
    }

    /// <summary>How many cells an orc walks from where the horde comes into the valley to the place, and whether where it crosses the plan's line it goes only through one of our gates (which the horde must hack); −1 for no way.</summary>
    static int WayIn(Sim sim, int cx, int cz, out bool byGates, out string crossed)
    {
        byGates = true;
        crossed = "";
        var (ex, ez) = sim.Site.EntryOn(sim.Site.EntrySide, .5);
        int from = sim.NearestStand(ex, ez, Kinds.Orc.Move.Height);
        var way = sim.Paths.Find(from, c => { var (x, _, z) = sim.Nav.Feet(c); return Math.Abs(x - cx) < 24 && Math.Abs(z - cz) < 24; }, _ => 0, Kinds.Orc.Move, 600000);
        if (way == null || sim.ArchPlan is not { } plan) return way?.Count ?? -1;
        bool wasIn = false;
        int crossings = 0, atGates = 0;
        for (int i = 0; i < way.Count; i++)
        {
            var (x, _, z) = sim.Nav.Feet(way[i]);
            bool inside = sim.InsideArchRing(x, z);
            if (i > 0 && inside != wasIn)
            {
                crossings++;
                bool gate = sim.Gates.Exists(g => { var (gx, _, gz) = g.Middle; return Math.Abs(gx - x) < 16 && Math.Abs(gz - z) < 16; });
                if (gate) atGates++; else byGates = false;
                if (Say) Console.WriteLine($"      the way crosses the line at ({x - cx:0}, {z - cz:0}), {(gate ? "through a gate" : "not by a gate")}");
            }
            wasIn = inside;
        }
        crossed = $"crossing the line {crossings} times, {atGates} through our gates";
        return way.Count;
    }

    /// <summary>How many cells a worker walks from where the horde comes in to the place with our gates open, and where it crosses the line; −1 for no way.</summary>
    static int WalkIn(Sim sim, int cx, int cz, out string crossed)
    {
        crossed = "";
        var (ex, ez) = sim.Site.EntryOn(sim.Site.EntrySide, .5);
        int from = sim.NearestStand(ex, ez, 2);
        var way = sim.Paths.Find(from, c => { var (x, _, z) = sim.Nav.Feet(c); return Math.Abs(x - cx) < 40 && Math.Abs(z - cz) < 40; }, _ => 0, Kinds.Worker.Move, 600000, gates: true);
        if (way == null) return -1;
        bool wasIn = false;
        int crossings = 0, atGates = 0;
        for (int i = 0; i < way.Count; i++)
        {
            var (x, _, z) = sim.Nav.Feet(way[i]);
            bool inside = sim.InsideArchRing(x, z);
            if (i > 0 && inside != wasIn)
            {
                crossings++;
                if (sim.Gates.Exists(g => { var (gx, _, gz) = g.Middle; return Math.Abs(gx - x) < 16 && Math.Abs(gz - z) < 16; })) atGates++;
            }
            wasIn = inside;
        }
        crossed = $"crossing the line {crossings} times, {atGates} through our gates";
        return way.Count;
    }

    static readonly UnitType[] Raiders = { Kinds.Orc, Kinds.Orc, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Orc, Kinds.Goblin };

    /// <summary>A band of eight from the horde's side against six workers and four archers about the place: how many of ours fall, and how soon the band gets to the stone.</summary>
    static int RaidLosses(Sim sim, out string said, out double reached)
    {
        var (cx, cz) = (sim.Site.KeepX, sim.Site.KeepZ);
        foreach (var u in sim.Units) if (u.Team == Team.Player && u.T != Kinds.Worker && !Kinds.IsOurEngine(u.T)) { u.Dead = true; u.DiedAt = -999; }
        var ours = sim.Units.Where(u => !u.Dead && u.Team == Team.Player && u.T == Kinds.Worker).ToList();
        for (int i = 0; i < 4; i++) ours.Add(sim.Spawn(Kinds.Archer, sim.NearestStand(cx + 8 + i * 4, cz - 8, 2)));
        int before = ours.Count(u => !u.Dead);
        int came0 = sim.Units.Count(u => u.Team == Team.Horde);
        sim.Raid(Raiders, sim.Site.EntrySide);
        double t0 = sim.Time, reachedAt = -1;
        int most = 0;
        while (sim.Time - t0 < 600 && (sim.Raids.Active || sim.Units.Exists(u => !u.Dead && u.Team == Team.Horde)))
        {
            Step(sim, 1);
            most = Math.Max(most, sim.Units.Count(u => !u.Dead && u.Team == Team.Horde));
            if (reachedAt < 0 && sim.Units.Exists(u => !u.Dead && u.Team == Team.Horde && Math.Abs(u.X - cx) < 24 && Math.Abs(u.Z - cz) < 24)) reachedAt = sim.Time;
        }
        int fell = before - ours.Count(u => !u.Dead), horde = sim.Units.Count(u => u.Team == Team.Horde && u.Dead);
        reached = reachedAt < 0 ? 1e9 : reachedAt - t0;
        said = $"the stone reached {(reached > 1e8 ? "never" : $"after {reached:0} s")}, {fell} of {before} of ours fell, {horde} raiders of {sim.Units.Count(u => u.Team == Team.Horde) - came0} (at most {most} at once), {(sim.Time - t0) / 60:0.0} min, active {sim.Raids.Active}";
        return fell;
    }

    /// <summary>The plan's stages as masks of its pieces, by what the bench says of them.</summary>
    static IEnumerable<(string name, ulong mask)> Stages(Sim sim)
    {
        var pieces = sim.ArchPieces(sim.ArchPlan);
        ulong M(Func<Sim.ArchPiece, bool> f) { ulong m = 0; foreach (var p in pieces) if (f(p)) m |= 1UL << p.Index; return m; }
        yield return ("wall", M(p => p.Stage == 0));
        yield return ("gate", M(p => p.Stage == 1));
        yield return ("corners", M(p => p.Stage == 2));
        yield return ("ditch", M(p => p.Stage == 3));
        yield return ("stakes", M(p => p.Stage == 4));
        yield return ("all", M(_ => true));
    }

    static bool AllDone(Sim sim)
    {
        if (sim.ArchPlan is not { } p) return false;
        ulong all = 0;
        foreach (var piece in sim.ArchPieces(p)) all |= 1UL << piece.Index;
        return (p.Done & all) == all;
    }

    /// <summary>The outpost's square as it is, voxel by voxel, summed.</summary>
    static long PlaceSum(Sim sim, int cx, int cz)
    {
        long s = 0;
        for (int z = cz - Holdings.Square; z <= cz + Holdings.Square; z += 2)
            for (int x = cx - Holdings.Square; x <= cx + Holdings.Square; x += 2)
                for (int y = sim.Site.KeepBase - 4; y < sim.Site.KeepBase + 40; y++)
                {
                    // (the turf trodden bare by the workers is no building)
                    byte m = sim.World.Get(x, y, z);
                    s = s * 31 + (m == Mat.Trodden || Mat.IsSprig(m) ? Mat.Grass : m);
                }
        return s;
    }

    static void Step(Sim sim, double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }

    /// <summary>
    /// An outpost's valley with its place levelled forty metres about (ground at the place's height, nothing standing
    /// on it but the outpost's own), a road through it from east to west, no beasts, no raid of its own, six workers
    /// and the stores as given.
    /// </summary>
    static Sim Valley(uint seed, double stone, double wood, out int cx, out int cz)
    {
        var mp = new MapParams { Seed = seed, Kind = ValleyKind.Forest, Hexagon = true, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize, Holding = Holding.Outpost };
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        cx = site.KeepX; cz = site.KeepZ;
        int g = site.KeepBase, R = 320;
        for (int z = cz - R; z <= cz + R; z++)
            for (int x = cx - R; x <= cx + R; x++)
            {
                if (!w.OnLand(x, z) || Math.Abs(x - cx) <= Holdings.Square + 2 && Math.Abs(z - cz) <= Holdings.Square + 2) continue;
                for (int y = Math.Max(1, g - 8); y < Math.Min(w.SY, g + 80); y++)
                {
                    byte want = y < g ? (y == g - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
                int i = z * w.SX + x;
                site.HTop[i] = g;
                if (site.WaterTop != null) site.WaterTop[i] = 0;
            }
        // the trees about it taken away whole, so that none stands cut in half at its edge
        for (int z = cz - R - 120; z <= cz + R + 120; z++)
            for (int x = cx - R - 120; x <= cx + R + 120; x++)
            {
                if (!w.InBounds(x, 1, z)) continue;
                for (int y = 1; y < w.SY; y++) { byte m = w.Get(x, y, z); if (Mat.IsTrunk(m) || Mat.IsLeafy(m)) w.Set(x, y, z, Mat.Air); }
            }
        site.Roads.Clear();
        site.Roads.Add(new List<(float X, float Z)> { (cx - 600, cz + 4), (cx + 600, cz + 4) });
        w.Compact();
        var sim = new Sim(w, site) { WildTarget = 0, QuietLife = true };
        sim.Clock.Left = 1e9;
        foreach (var u in sim.Units) if (u.Team == Team.Wild) { u.Dead = true; u.DiedAt = -999; }
        foreach (var u in sim.Units) if (u.Team == Team.Player && u.T == Kinds.Worker) { u.Dead = true; u.DiedAt = -999; }
        sim.Stock[Stockpile.Stone] = stone * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] = wood * VoxelWorld.PerM3;
        for (int i = 0; i < 6; i++) sim.Spawn(Kinds.Worker, sim.NearestStand(cx + 24 + i * 6, cz + 30, 2));
        return sim;
    }
}
