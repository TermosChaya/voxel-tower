using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// Those who step aside from a tree being felled are safe (--fellsafe, Sim.Felling.cs): trees of the game's first valley
/// and of a forest and a mountain valley, the big ones of the woods and meadows and those on slopes among them, and the
/// tree of the tutorial's copper mine, felled one after another by a woodcutter with six soldiers of ours standing about
/// each, a metre and a half to six metres off.
/// Each soldier the warning sends out of the lane waits outside it, and none is struck — by a bough that comes down
/// under where it hung, by the trunk going its own plan's way and not a bough's, or by its log rolling down a slope.
/// FELLSAFE_TREES (a valley's count), FELLSAFE_SAY=1 (every tree), FELLSAFE_TRACE=n (one tree's woodcutter as it goes,
/// in the first valley or FELLSAFE_TRACE_VALLEY's; FELLSAFE_FINE=s every step from s seconds), FELLSAFE_TRACES=0 (the
/// valleys without their old works), FELLSAFE_WILDS=0 (without nature's dressing of the open).
/// </summary>
static class FellSafeBench
{
    public static void Run(Action<string, bool, string> check)
    {
        int want = int.TryParse(Environment.GetEnvironmentVariable("FELLSAFE_TREES"), out int n) ? n : 30;
        bool say = Environment.GetEnvironmentVariable("FELLSAFE_SAY") == "1";
        int trace = int.TryParse(Environment.GetEnvironmentVariable("FELLSAFE_TRACE"), out int tr) ? tr : -1;
        uint traceSeed = uint.TryParse(Environment.GetEnvironmentVariable("FELLSAFE_TRACE_VALLEY"), out uint tv) ? tv : 7;
        int trees = 0, felled = 0, unwarned = 0, standing = 0, rolled = 0, tumbled = 0, struck = 0, killed = 0;
        double felling = 0;
        var lines = new List<string>();
        foreach (var (seed, kind) in new[] { (7u, ValleyKind.First), (5u, ValleyKind.Forest), (3u, ValleyKind.Mountain) })
        {
            // (the valleys as the game makes them, the old works in their wild and nature's dressing of their open ground;
            // FELLSAFE_TRACES=0 and FELLSAFE_WILDS=0 without them)
            var mp = new MapParams { Seed = seed, Kind = kind, Hexagon = true, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize,
                Traces = Environment.GetEnvironmentVariable("FELLSAFE_TRACES") != "0", Wilds = Environment.GetEnvironmentVariable("FELLSAFE_WILDS") != "0" };
            var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
            var site = MapGen.Generate(w, mp);
            w.Compact();
            var sim = new Sim(w, site) { QuietLife = true };
            sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
            foreach (var u in sim.Units) if (u.Team == Team.Wild) { u.Dead = true; u.DiedAt = -999; }
            bool rolling = false, tumbling = false;
            void Run(double seconds) { for (double t = 0; t < seconds; t += .05) { sim.Step(.05f); rolling |= sim.Fliers.Exists(f => f.Rolling); tumbling |= sim.Fliers.Exists(f => !f.Rolling && f.Cause == "бревно"); } }

            // the trees: standing three metres and more (not a fallen trunk, a stump or a bush), out of the citadel's
            // clearing, well inside the valley (one by its edge may only fall over it, and is left standing), apart
            var rng = new Random(11);
            var cand = new List<(int x, int z)>();
            for (int z = 80; z < w.SZ - 80; z += 9)
                for (int x = 80; x < w.SX - 80; x += 9)
                {
                    if (!w.OnLand(x, z)) continue;
                    int g = w.SurfAt(x, z);
                    if (!Mat.IsTrunk(w.Get(x, g + 2, z)) && !Mat.IsTrunk(w.Get(x, g, z))) continue;
                    if ((x - site.KeepX) * (x - site.KeepX) + (z - site.KeepZ) * (z - site.KeepZ) < 120 * 120) continue;
                    bool inside = true;
                    for (int a = 0; a < 8 && inside; a++) inside = w.OnLand(x + (int)(Math.Cos(a * Math.Tau / 8) * 200), z + (int)(Math.Sin(a * Math.Tau / 8) * 200));
                    if (inside) cand.Add((x, z));
                }
            // (in the first valley the tree the tutorial's two were struck by at the copper mine first)
            cand = cand.OrderBy(c => seed == 7 && (c.x - 531) * (c.x - 531) + (c.z - 730) * (c.z - 730) < 30 * 30 ? 0 : 1).ThenBy(_ => rng.Next()).ToList();
            var list = new List<(int x, int z, VoxelList plan, int foot)>();
            foreach (var (cx, cz) in cand)
            {
                if (list.Count >= want) break;
                if (list.Exists(t => Math.Abs(t.x - cx) < 60 && Math.Abs(t.z - cz) < 60)) continue;
                int g = w.SurfAt(cx, cz);
                var plan = Shapes.Tree(w, cx, Mat.IsTrunk(w.Get(cx, g + 2, cz)) ? g + 2 : g, cz);
                int wood = 0, lo = int.MaxValue, hi = int.MinValue;
                for (int i = 0; i < plan.Count; i++)
                    if (Mat.IsTrunk(w.Get(plan.X(i), plan.Y(i), plan.Z(i)))) { wood++; lo = Math.Min(lo, plan.Y(i)); hi = Math.Max(hi, plan.Y(i)); }
                if (wood >= 200 && hi - lo >= 3 * VoxelWorld.PerMetre) list.Add((cx, cz, plan, lo));
            }

            var watch = new HashSet<Unit>();
            var hurts = new List<(int tree, int id, string cause)>();
            int current = -1;
            sim.Hurting += (u, dmg, cause) =>
            {
                if (!watch.Contains(u)) return;
                hurts.Add((current, u.Id, cause));
                if (seed == traceSeed && current == trace) Console.WriteLine($"       HURT {u.Id} at {sim.Time:0.00}: {u.X - list[current].x:0.0},{u.Z - list[current].z:0.0} y {u.Y:0} by «{cause}» {dmg:0}");
            };
            for (int ti = 0; ti < list.Count; ti++)
            {
                current = ti;
                var (tx, tz, plan, foot) = list[ti];
                int WoodUp() { int k = 0; for (int i = 0; i < plan.Count; i++) if (plan.Y(i) > foot + 16 && Mat.IsTrunk(w.Get(plan.X(i), plan.Y(i), plan.Z(i)))) k++; return k; }
                var feller = sim.Spawn(Kinds.Worker, sim.NearestStand(tx + 20, tz, Kinds.Worker.Move.Height));
                var about = new List<Unit>();
                for (int k = 0; k < 6; k++)
                {
                    double a = rng.NextDouble() * Math.Tau, r = (1.5 + rng.NextDouble() * 4.5) * VoxelWorld.PerMetre;
                    int cell = sim.NearestStand(tx + (int)(Math.Cos(a) * r), tz + (int)(Math.Sin(a) * r), Kinds.Swordsman.Move.Height);
                    var s = sim.Spawn(Kinds.Swordsman, cell);
                    s.Hold = true; s.Dest = s.Post = cell;
                    about.Add(s); watch.Add(s);
                }
                if (seed == traceSeed && ti == trace)
                    foreach (var (k, ox, oz) in site.Traces)
                        if (Math.Abs(ox - tx) < 80 && Math.Abs(oz - tz) < 80) Console.WriteLine($"       old work {k} at {ox - tx},{oz - tz}");
                Toppling down = null;
                void Seen(Toppling t)
                {
                    if (seed == traceSeed && ti == trace) Console.WriteLine($"       TOPPLED at {sim.Time:0.00} s: P{t.PX - tx:0.0},{t.PZ - tz:0.0} D{t.DX:0.00},{t.DZ:0.00} felled {t.Felled}");
                    if (MathF.Abs(t.PX - tx) < 16 && MathF.Abs(t.PZ - tz) < 16) down ??= t;
                }
                sim.Toppled += Seen;
                rolling = tumbling = false;
                sim.DesignateRemove(plan);
                int up0 = WoodUp();
                double t0 = sim.Time, downAt = -1;
                while (sim.Time - t0 < 150)
                {
                    Run(.5);
                    double fine = double.TryParse(Environment.GetEnvironmentVariable("FELLSAFE_FINE"), out double fv) ? fv : 1e9;
                    if (seed == traceSeed && ti == trace && sim.Time - t0 >= fine)
                        Console.WriteLine($"       ~{sim.Time - t0:0.0} s: cutter {feller.X - tx:0},{feller.Z - tz:0} {feller.State} «{feller.Why}» | "
                            + string.Join(" ", about.Select(u => $"{u.X - tx:0},{u.Z - tz:0}{(u.Avoiding != null ? "*" : "")}{(u.Path != null && u.PathI < u.Path.Count ? "p" : "")}"))
                            + " | lanes " + string.Join("; ", sim.Dangers.Select(d => $"P{d.PX - tx:0},{d.PZ - tz:0} D{d.DX:0.0},{d.DZ:0.0} L{d.Len:0} H{d.HalfFoot:0}/{d.Half:0} B{d.Back:0} R{d.RollLen:0}{(d.Kept ? " kept" : "")}{(d.By != null ? " by" : "")} u{d.Until - sim.Time:0}"))
                            + $" | wood up {WoodUp()}" + (sim.Fliers.Count > 0 ? $" fliers {sim.Fliers.Count}" : ""));
                    if (seed == traceSeed && ti == trace && (int)(sim.Time * 2) % 10 == 0)
                        Console.WriteLine($"       {sim.Time - t0:0} s: the woodcutter at {feller.X - tx:0},{feller.Z - tz:0} {feller.State} «{feller.Why}»; soldiers {string.Join(" ", about.Select(u => $"{u.X - tx:0},{u.Z - tz:0}{(u.Avoiding != null ? "*" : "")}"))}; {sim.Dangers.Count} lanes; wood up {WoodUp()}");
                    if (downAt < 0 && (down != null || WoodUp() < up0 / 10)) downAt = sim.Time;
                    if (downAt >= 0 && sim.Time - downAt > 6 && !sim.Fliers.Exists(f => f.Rolling)) break;
                }
                sim.Toppled -= Seen;
                trees++;
                if (downAt >= 0) { felled++; felling += downAt - t0; if (down == null || !down.Felled) unwarned++; } else standing++;
                if (rolling) rolled++;
                if (tumbling) tumbled++;
                var mine = hurts.FindAll(x => x.tree == ti);
                int hit = mine.Select(x => x.id).Distinct().Count(), dead = about.Count(u => u.Dead);
                struck += hit; killed += dead;
                if (say || hit > 0)
                    lines.Add($"valley {seed}, tree {ti} at {tx},{tz}: {(downAt < 0 ? "standing" : $"down in {downAt - t0:0} s")}{(rolling ? ", its log rolled" : "")}; {hit} struck, {dead} dead"
                        + (mine.Count > 0 ? " (" + string.Join(", ", mine.GroupBy(x => x.cause).Select(g => $"{g.Key} ×{g.Count()}")) + ")" : "")
                        + "; soldiers at " + string.Join(" ", about.Select(u => $"{u.X - tx:0},{u.Z - tz:0}{(u.Dead ? "†" : "")}")));
                // all of them gone before the next tree, and what was left of its work
                foreach (var u in about) { watch.Remove(u); if (!u.Dead) { u.Dead = true; u.DiedAt = -999; } }
                if (!feller.Dead) { feller.Dead = true; feller.DiedAt = -999; }
                sim.CancelArea(tx - 120, tz - 120, tx + 120, tz + 120);
                sim.Dangers.Clear();
            }
        }
        foreach (var l in lines) Console.WriteLine("     " + l);
        check("trees of three valleys, old works and nature's dressing in their wild, felled one after another with soldiers about them: those who step aside are not struck by the tree, a bough of it or its log, and no log too thick to roll is let go down a slope",
            trees >= Math.Min(3 * want, 60) && felled >= trees * 9 / 10 && struck == 0 && tumbled == 0,
            $"{trees} trees, {felled} down ({unwarned} without a warning, {standing} still standing), {rolled} with a log rolling, {tumbled} with one let go to tumble; {struck} soldiers struck, {killed} killed; felling took {(felled > 0 ? felling / felled : 0):0} s a tree");
        StuckLanes(check);
        AmongObstacles(check);
        AgainstTheButt(check);
    }

    /// <summary>
    /// Nobody waits by a tree for good (the author's playtest, 27.10.2026: three stood «отошёл: валят дерево» long after
    /// the felling, and the alarm did not move them): a tree's lane kept on and on, nobody at its tree, is let go after the
    /// longest a felling takes, and those waiting by one go when the alarm is called.
    /// </summary>
    static void StuckLanes(Action<string, bool, string> check)
    {
        var mp = new MapParams { Seed = 7, Kind = ValleyKind.First, Hexagon = true, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize };
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site) { QuietLife = true };
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        foreach (var u in sim.Units) if (u.Team == Team.Wild) { u.Dead = true; u.DiedAt = -999; }
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        // two workers and a swordsman in the open, off the citadel
        var ours = new[] { Kinds.Worker, Kinds.Worker, Kinds.Swordsman }
            .Select((k, i) => sim.Spawn(k, sim.NearestStand(site.KeepX - 140 + i * 10, site.KeepZ + 60, k.Move.Height))).ToList();
        // a lane laid by them, kept on and on (as a log caught rocking on a slope kept it), and them waiting by it
        Danger Lane()
        {
            var u0 = ours[0];
            var d = new Danger { PX = u0.X + 30, PZ = u0.Z, DX = 1, DZ = 0, Len = 60, Half = 12, HalfFoot = 6, Why = "отошёл: валят дерево", Kept = true, Tree = true, Born = sim.Time, Until = sim.Time + 1e6 };
            sim.Dangers.Add(d);
            foreach (var u in ours) { u.Avoiding = d; u.Path = null; }
            return d;
        }
        var held = Lane();
        Run(30);
        bool waited = ours.All(u => u.Avoiding == held);
        Run(Sim.TreeLaneMost);
        bool let = !sim.Dangers.Contains(held) && ours.All(u => u.Avoiding == null);
        // waiting by one, the alarm called: they go
        var again = Lane();
        Run(2);
        var at = ours.Select(u => (u.X, u.Z)).ToList();
        sim.RaiseAlarm();
        Run(6);
        // (none keeping out of it any more, nor saying so; the workers gone off a metre and more, or into the citadel —
        // a soldier with no post on a wall here has nowhere to go, and stands)
        bool alarmed = ours.All(u => u.Avoiding != again && u.Why != again.Why)
            && ours.Select((u, i) => u.T.Work < 1 || u.Inside || MathF.Abs(u.X - at[i].X) + MathF.Abs(u.Z - at[i].Z) > VoxelWorld.PerMetre).All(b => b);
        string how = string.Join(", ", ours.Select((u, i) => $"{u.T.Name} {u.State} «{u.Why}» moved {MathF.Abs(u.X - at[i].X) + MathF.Abs(u.Z - at[i].Z):0}"));
        sim.StandDown();
        sim.Dangers.Remove(again);
        check("nobody waits by a tree for good: a tree's lane kept on and on is let go after the longest a felling takes, and those waiting by one go when the alarm is called",
            waited && let && alarmed,
            $"{ours.Count} of ours; waiting by it at 30 s {waited}; the lane let go and them with it by {Sim.TreeLaneMost + 30:0} s {let}; on the alarm they left it {alarmed} ({how})");
    }

    /// <summary>
    /// Trees felled among what stands about them in the wild (the old works of people, MapGen.Traces.cs): about each, the
    /// low wall of an old pen turning a corner, a pile of grey logs and two boulders, a metre and a half to four metres off,
    /// and six soldiers among them, two of them in the wall's corner, so that the way out of a lane goes round the wall or
    /// over the logs. None is struck by the tree, a bough of it or its log; none is left standing in a lane its tree comes
    /// down along (whoever cannot get out is waited for, and the tree goes another way or is left).
    /// FELLSAFE_OBSTACLES (trees, 14), FELLSAFE_SAY=1 (every tree).
    /// </summary>
    static void AmongObstacles(Action<string, bool, string> check)
    {
        int want = int.TryParse(Environment.GetEnvironmentVariable("FELLSAFE_OBSTACLES"), out int n) ? n : 14;
        bool say = Environment.GetEnvironmentVariable("FELLSAFE_SAY") == "1";
        var mp = new MapParams { Seed = 5, Kind = ValleyKind.Forest, Hexagon = true, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize };
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var rng = new Random(23);
        // the trees: as the valleys' (three metres and more of standing wood, well inside the valley, apart)
        var trees = new List<(int x, int z, VoxelList plan, int foot)>();
        for (int z = 100; z < w.SZ - 100 && trees.Count < want; z += 11)
            for (int x = 100; x < w.SX - 100 && trees.Count < want; x += 11)
            {
                if (!w.OnLand(x, z) || (x - site.KeepX) * (x - site.KeepX) + (z - site.KeepZ) * (z - site.KeepZ) < 140 * 140) continue;
                if (trees.Exists(t => Math.Abs(t.x - x) < 80 && Math.Abs(t.z - z) < 80)) continue;
                int g = w.SurfAt(x, z);
                if (!Mat.IsTrunk(w.Get(x, g + 2, z)) && !Mat.IsTrunk(w.Get(x, g, z))) continue;
                bool inside = true;
                for (int a = 0; a < 8 && inside; a++) inside = w.OnLand(x + (int)(Math.Cos(a * Math.Tau / 8) * 200), z + (int)(Math.Sin(a * Math.Tau / 8) * 200));
                if (!inside) continue;
                var plan = Shapes.Tree(w, x, Mat.IsTrunk(w.Get(x, g + 2, z)) ? g + 2 : g, z);
                int wood = 0, lo = int.MaxValue, hi = int.MinValue;
                for (int i = 0; i < plan.Count; i++)
                    if (Mat.IsTrunk(w.Get(plan.X(i), plan.Y(i), plan.Z(i)))) { wood++; lo = Math.Min(lo, plan.Y(i)); hi = Math.Max(hi, plan.Y(i)); }
                if (wood >= 200 && hi - lo >= 3 * VoxelWorld.PerMetre) trees.Add((x, z, plan, lo));
            }
        // what stands about each: laid on the ground as the old works are, never into the tree's own wood
        int laid = 0;
        void Put(int x, int y, int z, byte m)
        {
            if (!w.InBounds(x, y, z)) return;
            byte was = w.Get(x, y, z);
            if (Mat.IsTrunk(was) || Mat.IsLeafy(was) && y > w.SurfAt(x, z) + 6) return;
            w.Set(x, y, z, m); laid++;
        }
        void Column(int x, int z, int high, byte m) { int g = w.SurfAt(x, z); for (int y = g; y < g + high; y++) Put(x, y, z, m); }
        var corners = new List<(int x, int z)>();
        foreach (var (tx, tz, _, _) in trees)
        {
            // the old pen's wall: two stretches of four metres meeting at a corner, a metre high, two stones thick
            double a = rng.NextDouble() * Math.Tau;
            int cx = tx + (int)(Math.Cos(a) * 26), cz = tz + (int)(Math.Sin(a) * 26);
            int sx = Math.Sign(tx - cx) == 0 ? 1 : Math.Sign(tx - cx), sz = Math.Sign(tz - cz) == 0 ? 1 : Math.Sign(tz - cz);
            for (int k = 0; k < 32; k++)
                for (int t = 0; t < 2; t++)
                {
                    Column(cx - sx * k, cz + t * sz, 8, Mat.Ruin);
                    Column(cx + t * sx, cz - sz * k, 8, Mat.Ruin);
                }
            corners.Add((cx - sx * 6, cz - sz * 6));
            // a pile of logs two metres long the other side of the tree
            double b = a + Math.PI * (.8 + rng.NextDouble() * .4);
            int lx = tx + (int)(Math.Cos(b) * 22), lz = tz + (int)(Math.Sin(b) * 22);
            bool alongX = rng.Next(2) == 0;
            foreach (var (o, y) in new[] { (-1, 0), (1, 0), (0, 2) })
                for (int k = -8; k <= 8; k++)
                    for (int t = 0; t <= 1; t++)
                        for (int yy = 0; yy <= 1; yy++)
                        {
                            int px = alongX ? lx + k : lx + o + t, pz = alongX ? lz + o + t : lz + k;
                            Put(px, w.SurfAt(px, pz) + y + yy, pz, Mat.Snag);
                        }
            // two boulders
            for (int k = 0; k < 2; k++)
            {
                double c = rng.NextDouble() * Math.Tau, r = 14 + rng.NextDouble() * 18;
                int bx = tx + (int)(Math.Cos(c) * r), bz = tz + (int)(Math.Sin(c) * r), br = 4 + rng.Next(3);
                int g = w.SurfAt(bx, bz);
                for (int dz = -br; dz <= br; dz++)
                    for (int dx = -br; dx <= br; dx++)
                        for (int dy = -1; dy <= br; dy++)
                            if (dx * dx + dz * dz + dy * dy * 1.6f <= br * br) Put(bx + dx, g + dy, bz + dz, Mat.Rock);
            }
        }
        var sim = new Sim(w, site) { QuietLife = true };
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        foreach (var u in sim.Units) if (u.Team == Team.Wild) { u.Dead = true; u.DiedAt = -999; }
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }

        var watch = new HashSet<Unit>();
        int struck = 0, felled = 0, inLane = 0, cornered = 0, left = 0;
        var lines = new List<string>();
        int current = -1;
        var hurt = new HashSet<(int tree, int id)>();
        sim.Hurting += (u, dmg, cause) => { if (watch.Contains(u)) hurt.Add((current, u.Id)); };
        for (int ti = 0; ti < trees.Count; ti++)
        {
            current = ti;
            var (tx, tz, plan, foot) = trees[ti];
            int WoodUp() { int k = 0; for (int i = 0; i < plan.Count; i++) if (plan.Y(i) > foot + 16 && Mat.IsTrunk(w.Get(plan.X(i), plan.Y(i), plan.Z(i)))) k++; return k; }
            var feller = sim.Spawn(Kinds.Worker, sim.NearestStand(tx + 20, tz, Kinds.Worker.Move.Height));
            var about = new List<Unit>();
            for (int k = 0; k < 6; k++)
            {
                // (two in the corner of the old wall, the rest about the tree)
                int cell;
                if (k < 2) cell = sim.NearestStand(corners[ti].x + k * 6, corners[ti].z - k * 6, Kinds.Swordsman.Move.Height);
                else
                {
                    double a = rng.NextDouble() * Math.Tau, r = (1.5 + rng.NextDouble() * 3) * VoxelWorld.PerMetre;
                    cell = sim.NearestStand(tx + (int)(Math.Cos(a) * r), tz + (int)(Math.Sin(a) * r), Kinds.Swordsman.Move.Height);
                }
                var s = sim.Spawn(Kinds.Swordsman, cell);
                s.Hold = true; s.Dest = s.Post = cell;
                about.Add(s); watch.Add(s);
            }
            Toppling down = null;
            void Seen(Toppling t) { if (MathF.Abs(t.PX - tx) < 16 && MathF.Abs(t.PZ - tz) < 16) down ??= t; }
            sim.Toppled += Seen;
            sim.DesignateRemove(plan);
            int up0 = WoodUp();
            double t0 = sim.Time, downAt = -1;
            int under = 0;
            while (sim.Time - t0 < 180)
            {
                Run(.25);
                if (downAt < 0 && (down != null || WoodUp() < up0 / 10))
                {
                    downAt = sim.Time;
                    // whoever is still in the lane it comes down along as it goes
                    if (down?.Lane is { } lane) foreach (var s in about) if (!s.Dead && lane.Has(s.X, s.Y, s.Z, 0)) under++;
                }
                if (downAt >= 0 && sim.Time - downAt > 6 && !sim.Fliers.Exists(f => f.Rolling)) break;
            }
            sim.Toppled -= Seen;
            int hit = hurt.Count(h => h.tree == ti);
            struck += hit; inLane += under;
            if (downAt >= 0) felled++; else left++;
            // the two from the corner: out of it at the warning, if their place was in the lane
            bool moved = about.Take(2).Any(u => u.Avoiding != null || MathF.Abs(u.X - (corners[ti].x)) > 8 || MathF.Abs(u.Z - corners[ti].z) > 8);
            if (moved) cornered++;
            if (say || hit > 0 || under > 0)
                lines.Add($"tree {ti} at {tx},{tz}: {(downAt < 0 ? "standing" : $"down in {downAt - t0:0} s")}; {hit} struck, {under} in its lane as it went; soldiers at "
                    + string.Join(" ", about.Select(u => $"{u.X - tx:0},{u.Z - tz:0}{(u.Dead ? "†" : "")}")) + $"; the woodcutter «{feller.Why}»");
            foreach (var u in about) { watch.Remove(u); if (!u.Dead) { u.Dead = true; u.DiedAt = -999; } }
            if (!feller.Dead) { feller.Dead = true; feller.DiedAt = -999; }
            sim.CancelArea(tx - 120, tz - 120, tx + 120, tz + 120);
            sim.Dangers.Clear();
        }
        foreach (var l in lines) Console.WriteLine("     " + l);
        check("trees felled among old walls, piles of logs and boulders with soldiers about them, two in the wall's corner: none struck, none in the lane as its tree goes",
            trees.Count >= Math.Min(want, 10) && felled >= trees.Count * 3 / 4 && struck == 0 && inLane == 0,
            $"{trees.Count} trees, {laid} voxels of walls, logs and boulders about them; {felled} down, {left} left standing; {struck} soldiers struck, {inLane} in a lane as its tree went; the corner's soldiers had to go round at {cornered} trees");
    }

    /// <summary>
    /// Those standing right against a tree's butt as it is felled, a ring of eight a metre from its middle, on the slopes
    /// of a mountain valley: those in its lane are sent out of it, and those left at its back by the stump are not struck
    /// as it goes over (the wood of the cut settles on the stump, it does not fly back), nor by its log, which on a slope it
    /// is too thick to roll down lies where it falls. FELLSAFE_BUTT (trees, 12).
    /// </summary>
    static void AgainstTheButt(Action<string, bool, string> check)
    {
        int want = int.TryParse(Environment.GetEnvironmentVariable("FELLSAFE_BUTT"), out int n) ? n : 12;
        bool say = Environment.GetEnvironmentVariable("FELLSAFE_SAY") == "1";
        var mp = new MapParams { Seed = 3, Kind = ValleyKind.Mountain, Hexagon = true, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize };
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site) { QuietLife = true };
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        foreach (var u in sim.Units) if (u.Team == Team.Wild) { u.Dead = true; u.DiedAt = -999; }
        bool tumbling = false;
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) { sim.Step(.05f); tumbling |= sim.Fliers.Exists(f => !f.Rolling && f.Cause == "бревно"); } }
        // the biggest trees, and most on a slope, first
        var trees = new List<(int x, int z, VoxelList plan, int foot, int wood, int slope)>();
        for (int z = 100; z < w.SZ - 100; z += 7)
            for (int x = 100; x < w.SX - 100; x += 7)
            {
                if (!w.OnLand(x, z) || (x - site.KeepX) * (x - site.KeepX) + (z - site.KeepZ) * (z - site.KeepZ) < 140 * 140) continue;
                int g = w.SurfAt(x, z);
                if (!Mat.IsTrunk(w.Get(x, g + 2, z))) continue;
                var plan = Shapes.Tree(w, x, g + 2, z);
                int wood = 0, lo = int.MaxValue, hi = int.MinValue;
                for (int i = 0; i < plan.Count; i++)
                    if (Mat.IsTrunk(w.Get(plan.X(i), plan.Y(i), plan.Z(i)))) { wood++; lo = Math.Min(lo, plan.Y(i)); hi = Math.Max(hi, plan.Y(i)); }
                if (wood < 400 || hi - lo < 4 * VoxelWorld.PerMetre) continue;
                int slope = Math.Abs(w.SurfAt(x + 24, z) - w.SurfAt(x - 24, z)) + Math.Abs(w.SurfAt(x, z + 24) - w.SurfAt(x, z - 24));
                trees.Add((x, z, plan, lo, wood, slope));
            }
        var picked = new List<(int x, int z, VoxelList plan, int foot, int wood, int slope)>();
        foreach (var t in trees.OrderByDescending(t => t.slope * 4 + t.wood / 40))
        {
            if (picked.Count >= want) break;
            if (picked.Exists(p => Math.Abs(p.x - t.x) < 80 && Math.Abs(p.z - t.z) < 80)) continue;
            picked.Add(t);
        }
        var watch = new HashSet<Unit>();
        int struck = 0, felled = 0, tumbled = 0, behind = 0;
        int current = -1;
        var hurt = new HashSet<(int tree, int id)>();
        sim.Hurting += (u, dmg, cause) => { if (watch.Contains(u)) hurt.Add((current, u.Id)); };
        var lines = new List<string>();
        for (int ti = 0; ti < picked.Count; ti++)
        {
            current = ti;
            var (tx, tz, plan, foot, _, slope) = picked[ti];
            int WoodUp() { int k = 0; for (int i = 0; i < plan.Count; i++) if (plan.Y(i) > foot + 16 && Mat.IsTrunk(w.Get(plan.X(i), plan.Y(i), plan.Z(i)))) k++; return k; }
            var feller = sim.Spawn(Kinds.Worker, sim.NearestStand(tx + 16, tz, Kinds.Worker.Move.Height));
            var ring = new List<Unit>();
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.Tau / 8;
                int cell = sim.NearestStand(tx + (int)Math.Round(Math.Cos(a) * VoxelWorld.PerMetre), tz + (int)Math.Round(Math.Sin(a) * VoxelWorld.PerMetre), Kinds.Swordsman.Move.Height);
                if (ring.Exists(r => r.Cell == cell)) continue;
                var s = sim.Spawn(Kinds.Swordsman, cell);
                s.Hold = true; s.Dest = s.Post = cell;
                ring.Add(s); watch.Add(s);
            }
            sim.DesignateRemove(plan);
            int up0 = WoodUp();
            tumbling = false;
            double t0 = sim.Time, downAt = -1;
            while (sim.Time - t0 < 180)
            {
                Run(.25);
                if (downAt < 0 && WoodUp() < up0 / 10)
                {
                    downAt = sim.Time;
                    foreach (var s in ring) if (!s.Dead && MathF.Abs(s.X - tx) + MathF.Abs(s.Z - tz) < 2.5f * VoxelWorld.PerMetre) behind++;
                }
                if (downAt >= 0 && sim.Time - downAt > 6 && !sim.Fliers.Exists(f => f.Rolling)) break;
            }
            int hit = hurt.Count(h => h.tree == ti);
            struck += hit;
            if (downAt >= 0) felled++;
            if (tumbling) tumbled++;
            if (say || hit > 0 || tumbling)
                lines.Add($"tree {ti} at {tx},{tz} (slope {slope}): {(downAt < 0 ? "standing" : $"down in {downAt - t0:0} s")}{(tumbling ? ", its log let go to tumble" : "")}; {hit} struck; ring at "
                    + string.Join(" ", ring.Select(u => $"{u.X - tx:0},{u.Z - tz:0}{(u.Dead ? "†" : "")}")));
            foreach (var u in ring) { watch.Remove(u); if (!u.Dead) { u.Dead = true; u.DiedAt = -999; } }
            if (!feller.Dead) { feller.Dead = true; feller.DiedAt = -999; }
            sim.CancelArea(tx - 120, tz - 120, tx + 120, tz + 120);
            sim.Dangers.Clear();
        }
        foreach (var l in lines) Console.WriteLine("     " + l);
        check("big trees on mountain slopes felled with a ring of soldiers a metre from the butt: those left at its back by the stump are not struck, and no log is let go to tumble",
            picked.Count >= Math.Min(want, 8) && felled >= picked.Count * 3 / 4 && struck == 0 && tumbled == 0,
            $"{picked.Count} trees, {felled} down; {behind} soldiers still by the stump as their tree went; {struck} struck; {tumbled} logs let go to tumble");
    }
}
