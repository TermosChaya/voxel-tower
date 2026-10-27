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
/// FELLSAFE_TREES (a valley's count), FELLSAFE_SAY=1 (every tree), FELLSAFE_TRACE=n (one tree's woodcutter as it goes).
/// </summary>
static class FellSafeBench
{
    public static void Run(Action<string, bool, string> check)
    {
        int want = int.TryParse(Environment.GetEnvironmentVariable("FELLSAFE_TREES"), out int n) ? n : 30;
        bool say = Environment.GetEnvironmentVariable("FELLSAFE_SAY") == "1";
        int trace = int.TryParse(Environment.GetEnvironmentVariable("FELLSAFE_TRACE"), out int tr) ? tr : -1;
        int trees = 0, felled = 0, unwarned = 0, standing = 0, rolled = 0, struck = 0, killed = 0;
        double felling = 0;
        var lines = new List<string>();
        foreach (var (seed, kind) in new[] { (7u, ValleyKind.First), (5u, ValleyKind.Forest), (3u, ValleyKind.Mountain) })
        {
            var mp = new MapParams { Seed = seed, Kind = kind, Hexagon = true, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize };
            var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
            var site = MapGen.Generate(w, mp);
            w.Compact();
            var sim = new Sim(w, site);
            sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
            foreach (var u in sim.Units) if (u.Team == Team.Wild) { u.Dead = true; u.DiedAt = -999; }
            bool rolling = false;
            void Run(double seconds) { for (double t = 0; t < seconds; t += .05) { sim.Step(.05f); rolling |= sim.Fliers.Exists(f => f.Rolling); } }

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
            sim.Hurting += (u, dmg, cause) => { if (watch.Contains(u)) hurts.Add((current, u.Id, cause)); };
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
                Toppling down = null;
                void Seen(Toppling t) { if (MathF.Abs(t.PX - tx) < 16 && MathF.Abs(t.PZ - tz) < 16) down ??= t; }
                sim.Toppled += Seen;
                rolling = false;
                sim.DesignateRemove(plan);
                int up0 = WoodUp();
                double t0 = sim.Time, downAt = -1;
                while (sim.Time - t0 < 150)
                {
                    Run(.5);
                    if (seed == 7 && ti == trace && (int)(sim.Time * 2) % 10 == 0)
                        Console.WriteLine($"       {sim.Time - t0:0} s: the woodcutter at {feller.X - tx:0},{feller.Z - tz:0} {feller.State} «{feller.Why}»; soldiers {string.Join(" ", about.Select(u => $"{u.X - tx:0},{u.Z - tz:0}{(u.Avoiding != null ? "*" : "")}"))}; {sim.Dangers.Count} lanes; wood up {WoodUp()}");
                    if (downAt < 0 && (down != null || WoodUp() < up0 / 10)) downAt = sim.Time;
                    if (downAt >= 0 && sim.Time - downAt > 6 && !sim.Fliers.Exists(f => f.Rolling)) break;
                }
                sim.Toppled -= Seen;
                trees++;
                if (downAt >= 0) { felled++; felling += downAt - t0; if (down == null || !down.Felled) unwarned++; } else standing++;
                if (rolling) rolled++;
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
        check("trees of three valleys felled one after another with soldiers about them: those who step aside are not struck by the tree, a bough of it or its log",
            trees >= Math.Min(3 * want, 60) && felled >= trees * 9 / 10 && struck == 0,
            $"{trees} trees, {felled} down ({unwarned} without a warning, {standing} still standing), {rolled} with a log rolling; {struck} soldiers struck, {killed} killed; felling took {(felled > 0 ? felling / felled : 0):0} s a tree");
        StuckLanes(check);
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
        var sim = new Sim(w, site);
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
}
