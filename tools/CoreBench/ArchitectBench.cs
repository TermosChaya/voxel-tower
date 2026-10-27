using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The architect (--architect, Sim.Architect.cs). An outpost's valley levelled forty metres about its place, a road
/// through it from east to west, six workers and the stores full: he plans a ring round the place and has it built —
/// the ring, its gate on the road's side, the posts at the corners, the ditch and the stakes — and how many minutes
/// each takes is said; nothing falls, the outpost's place is left as it was, an engine of ours is stood inside the
/// ring by the horde's side. With little stone he puts up a palisade; a hole knocked in what he had built he has
/// mended; switched off he marks nothing; and through his ring the horde has no way to the outpost's stone but through
/// a gate it must hack, where on the bare place it walks straight there (a raid of eight on each told besides). ARCHITECT_SAY=1 tells what he says.
/// </summary>
static class ArchitectBench
{
    public static void Run(Action<string, bool, string> check)
    {
        bool say = Environment.GetEnvironmentVariable("ARCHITECT_SAY") == "1";

        // ---- the stores full: the ring goes up in stone
        var sim = Valley(7, 600, 300, out int cx, out int cz);
        int falls = 0;
        // (what falls about the ring from now on, the ground about it let settle first)
        Step(sim, 20);
        sim.Collapsed += (n, x, y, z) => { if (Math.Abs(x - cx) < 240 && Math.Abs(z - cz) < 240) { falls += n; if (say) Console.WriteLine($"      [{sim.Time:0}] fell {n} at ({x - cx:0}, {y:0}, {z - cz:0})"); } };
        if (say) sim.Said += (t, _, _, _) => Console.WriteLine($"      [{sim.Time:0}] {t}");
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
        if (say && plan != null)
            foreach (var piece in sim.ArchPieces(plan))
            {
                int left = 0;
                for (int z = piece.Z0; z <= piece.Z1; z++) for (int x = piece.X0; x <= piece.X1; x++) for (int y = 1; y < sim.World.SY; y++) if (sim.Orders.At(x, y, z) != 0) left++;
                Console.WriteLine($"      piece {piece.Index} {piece.Name}: marked {(plan.Marked >> piece.Index & 1)}, done {(plan.Done >> piece.Index & 1)}, still marked in its box {left}");
            }
        if (say) foreach (var u in sim.Units) if (!u.Dead && u.Team == Team.Player) Console.WriteLine($"      {u.T.Name} #{u.Id} {u.State} «{u.Why}» at ({u.X - cx:0}, {u.Y:0}, {u.Z - cz:0})");
        string Took(string k) => at.TryGetValue(k, out double m) ? $"{m:0.0}" : "—";
        bool all = AllDone(sim);
        check("the architect plans a ring round the outpost's place and the workers raise it whole — the ring, its gate, the corners, the ditch and the stakes — nothing falling",
            plan != null && plan.Stone && all && falls == 0 && sim.ArchMarked >= 10,
            $"ring {(plan == null ? "none" : $"{2 * plan.H / 8.0:0} m square, in {(plan.Stone ? "stone" : "timber")}")}; minutes: ring {Took("ring")}, gate {Took("gate")}, corners {Took("corners")}, ditch {Took("ditch")}, stakes {Took("stakes")}, all {Took("all")}; " +
            $"pieces marked {sim.ArchMarked}, fallen {falls}; doing «{sim.ArchDoing}», short of «{sim.ArchLacks}»");
        // the gate on the road's side (the road runs east and west through the place), the place untouched, the
        // engine inside the ring by the horde's side
        var (ox, oz) = plan == null ? (0, 0) : (plan.HordeSide switch { 0 => (0, -1), 1 => (1, 0), 2 => (0, 1), _ => (-1, 0) });
        bool inside = plan != null && Math.Abs(engine.X - cx) < plan.H && Math.Abs(engine.Z - cz) < plan.H;
        float toward = (engine.X - cx) * ox + (engine.Z - cz) * oz;
        check("his gate is on a side the road comes in by, nothing of his stands on the outpost's place, and our engine is stood inside the ring on the horde's side",
            plan != null && (plan.GateSide == 1 || plan.GateSide == 3) && PlaceSum(sim, cx, cz) == placeBefore && inside && toward > 0 && sim.Gates.Count >= 0,
            plan == null ? "no plan" : $"gate on side {plan.GateSide} at {plan.GateAt}, the horde's side {plan.HordeSide}; the place {(PlaceSum(sim, cx, cz) == placeBefore ? "untouched" : "built on")}; the engine at ({engine.X - cx:0}, {engine.Z - cz:0}) from the place, inside {inside}, {toward:0} toward the horde");

        // a hole knocked into his wall: he has it mended
        int hx = cx + (plan?.H ?? 0) * ox, hz = cz + (plan?.H ?? 0) * oz, knocked = 0, g = sim.World.SurfAt(hx + ox * 30, hz + oz * 30);
        var was = new List<(int x, int y, int z, byte m)>();
        for (int y = g + 4; y < g + 14; y++)
            for (int a = -6; a <= 6; a++)
                for (int d = -4; d < 4; d++)
                {
                    int x = hx + (oz != 0 ? a : d), z = hz + (ox != 0 ? a : d);
                    byte m = sim.World.Get(x, y, z);
                    if (!Mat.IsMasonry(m)) continue;
                    was.Add((x, y, z, m)); sim.World.Set(x, y, z, Mat.Air); knocked++;
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
        check("switched off he marks nothing; with little stone in the stores he puts the ring up as a palisade",
            markedOff == 0 && poor.ArchPlan is { Stone: false } && poor.ArchMarked > 0,
            $"marked while off {markedOff}; then plan in {(poor.ArchPlan == null ? "none" : poor.ArchPlan.Stone ? "stone" : "timber")}, marked {poor.ArchMarked}; «{poor.ArchDoing}»");

        // ---- a raid on the fortified place and on the bare one: fewer of ours fall behind his works
        // ---- the horde's way to the stone: shut out by his ring (it must break in), straight on the bare place; and the
        // same raid on each, told
        var bareSim = Valley(7, 600, 300, out _, out _);
        int wayFortified = WayIn(sim, cx, cz, out bool byGates), wayBare = WayIn(bareSim, cx, cz);
        RaidLosses(sim, out string f1, out _);
        RaidLosses(bareSim, out string f2, out _);
        check("through his ring the horde has no way in to the outpost's stone but through a gate it must hack, where on the bare place it walks straight there",
            wayBare > 0 && (wayFortified < 0 || byGates), $"the way in: {(wayFortified < 0 ? "none" : wayFortified + $" cells, {(byGates ? "only through our gates" : "past them")}")} through the ring, {wayBare} cells on the bare place; a raid of eight on each — fortified: {f1}; bare: {f2}");
    }

    /// <summary>How many cells an orc walks from where the horde comes into the valley to the outpost's stone, not through our gates; −1 for no way.</summary>
    static int WayIn(Sim sim, int cx, int cz) => WayIn(sim, cx, cz, out _);

    /// <summary>The same, and whether where it crosses the ring it goes only through one of our gates (which the horde must hack).</summary>
    static int WayIn(Sim sim, int cx, int cz, out bool byGates)
    {
        byGates = true;
        var (ex, ez) = sim.Site.EntryOn(sim.Site.EntrySide, .5);
        int from = sim.NearestStand(ex, ez, Kinds.Orc.Move.Height);
        var way = sim.Paths.Find(from, c => { var (x, _, z) = sim.Nav.Feet(c); return Math.Abs(x - cx) < 24 && Math.Abs(z - cz) < 24; }, _ => 0, Kinds.Orc.Move, 600000);
        if (way != null && sim.ArchPlan is { } p && Environment.GetEnvironmentVariable("ARCHITECT_SAY") == "1")
            foreach (int c in way)
            {
                var (x, y, z) = sim.Nav.Feet(c);
                if (Math.Abs(Math.Max(Math.Abs(x - cx), Math.Abs(z - cz)) - p.H) < 12) Console.WriteLine($"      the way crosses the ring at ({x - cx:0}, {y:0}, {z - cz:0}): {Mat.NameOf(sim.World.Get((int)x, (int)y, (int)z))}, under {Mat.NameOf(sim.World.Get((int)x, (int)y - 1, (int)z))}");
            }
        if (way != null && sim.ArchPlan is { } plan)
            foreach (int c in way)
            {
                var (x, _, z) = sim.Nav.Feet(c);
                if (Math.Abs(Math.Max(Math.Abs(x - cx), Math.Abs(z - cz)) - plan.H) >= 6) continue;
                bool gate = sim.Gates.Exists(g => { var (gx, _, gz) = g.Middle; return Math.Abs(gx - x) < 14 && Math.Abs(gz - z) < 14; });
                if (!gate) byGates = false;
            }
        return way?.Count ?? -1;
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
            if (Environment.GetEnvironmentVariable("ARCHITECT_SAY") == "1" && (int)(sim.Time - t0) % 30 == 0)
                foreach (var h in sim.Units) if (!h.Dead && h.Team == Team.Horde) Console.WriteLine($"      [{sim.Time - t0:0}] {h.T.Name} {h.State} at ({h.X - cx:0}, {h.Z - cz:0}) routed {h.Routed}");
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
        yield return ("ring", M(p => p.Stage == 0));
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
                for (int y = sim.Site.KeepBase - 4; y < sim.Site.KeepBase + 40; y++) s = s * 31 + sim.World.Get(x, y, z);
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
        var sim = new Sim(w, site) { WildTarget = 0 };
        sim.Clock.Left = 1e9;
        foreach (var u in sim.Units) if (u.Team == Team.Wild) { u.Dead = true; u.DiedAt = -999; }
        foreach (var u in sim.Units) if (u.Team == Team.Player && u.T == Kinds.Worker) { u.Dead = true; u.DiedAt = -999; }
        sim.Stock[Stockpile.Stone] = stone * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] = wood * VoxelWorld.PerM3;
        for (int i = 0; i < 6; i++) sim.Spawn(Kinds.Worker, sim.NearestStand(cx + 24 + i * 6, cz + 30, 2));
        return sim;
    }
}
