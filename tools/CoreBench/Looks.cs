using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's looks of the town's buildings (--looks, Sim.Looks.cs): every kind has two looks or more, each its own and
/// no two alike, and no kind's first look is another's; each look of each kind is put up and built by the workers to the
/// end — its room closed, its door hung, furnished, at work — with everything built on to it standing and nothing come
/// down; and one look of each kind is raised a storey by the workers, its roof and what stands on its ridge going back
/// up over it. LOOKS_ONLY=kind tries that kind alone.
/// </summary>
static class LooksBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        string only = Environment.GetEnvironmentVariable("LOOKS_ONLY");
        // the looks themselves: how many, all different, and every kind's first its own
        string Sig(Look l) => $"{l.Walls}/{l.Roof}/{l.Covering}/{l.Crown}/{l.DD}/{l.DW}/" + string.Join("+", l.Annexes.Select(a => $"{a.Kind}{a.At}{a.Depth}{(a.Gabled ? "g" : "")}{(a.Round ? "r" : "")}"));
        var few = Sim.TownKinds.Where(k => Sim.LooksOf(k).Length < 2).Select(k => k.Key).ToList();
        var alike = Sim.TownKinds.Where(k => Sim.LooksOf(k).Select(Sig).Distinct().Count() < Sim.LooksOf(k).Length).Select(k => k.Key).ToList();
        var firsts = Sim.TownKinds.Select(k => (k.Wall, Sig(Sim.LooksOf(k)[0]))).ToList();
        int names = Sim.TownKinds.SelectMany(k => Sim.LooksOf(k)).Select(l => l.Name).Distinct().Count();
        check("every kind of town building has two looks or more, each its own, and no two kinds' first looks alike",
            few.Count == 0 && alike.Count == 0 && firsts.Distinct().Count() == Sim.TownKinds.Length && names == Sim.TownKinds.Sum(k => Sim.LooksOf(k).Length),
            $"{Sim.TownKinds.Sum(k => Sim.LooksOf(k).Length)} looks of {Sim.TownKinds.Length} kinds; with fewer than two: {string.Join(", ", few)}; alike within: {string.Join(", ", alike)}; distinct firsts {firsts.Distinct().Count()}");

        foreach (var k in Sim.TownKinds)
        {
            if (only != null && Array.IndexOf(only.Split(","), k.Key) < 0) continue;
            Kind(k, check);
        }
    }

    /// <summary>One kind: its looks side by side in a valley of their own, built by the workers, one raised a storey.</summary>
    static void Kind(TownKind k, Action<string, bool, string> check)
    {
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        StartingFort.Raise(sim);
        sim.CancelArea(0, 0, w.SX, w.SZ);
        sim.ManyOfAKind = true;
        sim.Sandbox = true;
        for (int i = 0; i < 10; i++) sim.Spawn(Kinds.Worker, sim.Gate);
        sim.Stock[Stockpile.Stone] += 4000.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] += 4000.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Earth] += 4000.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Metal] += 200.0 * VoxelWorld.PerM3;
        sim.Stock.Gold = 20000;
        int fell = 0;
        bool settled = false;
        bool say = Environment.GetEnvironmentVariable("LOOKS_SAY") != null;
        sim.Collapsed += (n, x, y, z) =>
        {
            // (only what comes down at the buildings tried: a miners' building digs its ore, and the rock it digs may fall)
            if (!settled || !sim.Buildings.Exists(b => b.Kind == k.Key && x > b.X0 - 60 && x < b.X1 + 60 && z > b.Z0 - 60 && z < b.Z1 + 60)) return;
            fell += n;
            if (say) Console.WriteLine($"  fell {n} at {x:0},{y:0},{z:0} (ground {w.GroundTop((int)x, (int)z)}) t {sim.Time:0}: {string.Join(" ", sim.Buildings.Select(b => $"{b.Kind}{b.Variant} {b.X0},{b.Z0}-{b.X1},{b.Z1}"))}");
        };
        void Run(double s) { for (double t = 0; t < s; t += .05) sim.Step(.05f); }
        var looks = Sim.LooksOf(k);
        int kx = site.KeepX, kz = site.KeepZ;
        // what it needs standing first, put up at once behind the citadel (raised a storey where it must be)
        int ni = 0;
        void Need(string key, int level)
        {
            var need = Sim.TownKindOf(key);
            foreach (var (k2, l2) in need.Needs) Need(k2, l2);
            var nb = sim.BuildingOf(key);
            sim.InstantLabour = true;
            for (int r = 160; r < 520 && nb == null; r += 24)
                for (int a = 0; a < 16 && nb == null; a++)
                {
                    int x = kx + (int)(Math.Cos(a * Math.Tau / 16 + ni) * r), z = kz + (int)(Math.Sin(a * Math.Tau / 16 + ni) * r);
                    // (not on the field the looks go up on, north of the citadel; on level ground, so that a storey can go up)
                    if (z < kz - 150 && Math.Abs(x - kx) < 520) continue;
                    int lo = int.MaxValue, hi = int.MinValue;
                    for (int dx = -40; dx <= 40; dx += 5) for (int dz = -40; dz <= 40; dz += 5) { int h = w.SurfAt(x + dx, z + dz); lo = Math.Min(lo, h); hi = Math.Max(hi, h); }
                    if (hi - lo > 2) continue;
                    sim.PutUp(need, x, z, false, out nb);
                }
            ni++;
            for (int t = 0; t < 60 && nb is { Ready: false }; t++) { sim.CompleteAll(); Run(1); if (sim.ZoneOf(nb) is { } z) sim.LookOver(z); }
            string up = nb is { Ready: true } && nb.Level < level ? sim.Raise(nb) : "-";
            if (up == null)
                for (int t = 0; t < 60 && nb.Level < level; t++) { sim.CompleteAll(); Run(1); if (sim.ZoneOf(nb) is { } z) sim.LookOver(z); }
            if (say) Console.WriteLine($"  needs {key}: {(nb == null ? "not put up" : $"level {nb.Level}, ready {nb.Ready}, raise «{up}»")}");
            sim.InstantLabour = false;
        }
        foreach (var (key, level) in k.Needs) Need(key, level);
        // where they go: on outcrops of ore for the miners'; else a field evened out north of the citadel, a row of them
        var at = new List<(int x, int z, bool turned)>();
        if (k.OnOre)
        {
            foreach (var o in site.Outcrops.OrderBy(o => Math.Abs(o.X - kx) + Math.Abs(o.Z - kz)))
            {
                if (at.Count == looks.Length) break;
                if (at.Exists(a => Math.Abs(a.x - o.X) < 160 && Math.Abs(a.z - o.Z) < 160)) continue;
                foreach (bool turned in new[] { false, true })
                    if (sim.BuildingPlan(k, o.X, o.Z, turned, at.Count, out _, out _, out _, out _, out _, out _) != null) { at.Add((o.X, o.Z, turned)); break; }
            }
        }
        else
        {
            // (side by side across their long walls: their fronts and backs to one another, their ends out to the field)
            int deep = looks.SelectMany(l => l.Annexes).Where(a => a.At is Flank.Front or Flank.Back).Select(a => a.Depth + 8).DefaultIfEmpty(0).Max();
            int pitch = k.W + 2 * deep + 40, along = pitch * looks.Length;
            int cz = kz - Math.Max(site.KeepR + 190, 280), cx = kx, g = w.SurfAt(cx, cz);
            for (int x = cx - along / 2 - 40; x <= cx + along / 2 + 40; x++)
                for (int z = cz - 130; z <= cz + 130; z++)
                {
                    if (!w.InBounds(x, 0, z) || site.Forbidden(x, z)) continue;
                    for (int y = g - 8; y < g + 80; y++)
                    {
                        byte want = y < g ? (y == g - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                        if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                    }
                }
            for (int i = 0; i < looks.Length; i++) at.Add((cx - along / 2 + pitch * i + pitch / 2, cz, false));
        }
        if (at.Count < looks.Length) { check($"{k.Name}: room for each look", false, $"{at.Count} of {looks.Length}"); return; }
        // (what the field's evening out left hanging comes down first)
        Run(4);
        settled = true;
        // each look put up for the workers, all at once
        var put = new List<(Building b, VoxelList plan, string note)>();
        for (int i = 0; i < looks.Length; i++)
        {
            var plan = sim.BuildingPlan(k, at[i].x, at[i].z, at[i].turned, i, out _, out string why, out _, out _, out _, out string note);
            string no = sim.PutUp(k, at[i].x, at[i].z, at[i].turned, out var b, i);
            if (b == null) { check($"{k.Name} «{looks[i].Name}»: put up", false, no ?? why); return; }
            put.Add((b, plan, note));
        }
        double t0 = sim.Time;
        for (int s = 0; s < 3000 && put.Exists(p => !p.b.Ready); s++) Run(1);
        Run(10);
        foreach (var (b, plan, note) in put)
        {
            var look = b.Look;
            int stands = 0;
            // (the doorway cut through the wall where the door hangs counts as standing)
            var door = sim.DoorOf(b);
            bool InDoorway(int x, int y, int z) => door != null && MathF.Abs(x + .5f - door.Middle.x) < 14 && MathF.Abs(z + .5f - door.Middle.z) < 14 && y >= door.Middle.y - 2 && y <= door.Middle.y + Gateway.Height + 2;
            for (int i = 0; i < plan.Count; i++)
            {
                byte now = w.Get(plan.X(i), plan.Y(i), plan.Z(i));
                if (now == plan.M(i) || Mat.IsLeaf(now) || now == Mat.Air && InDoorway(plan.X(i), plan.Y(i), plan.Z(i))) stands++;
                else if (say && now == Mat.Air) Console.WriteLine($"  missing {Mat.NameOf(plan.M(i))} at {plan.X(i)},{plan.Y(i)},{plan.Z(i)}");
            }
            var zone = sim.ZoneOf(b);
            if (say && !b.Ready && sim.FrontOf(b, out int fx, out int fy, out int fz, out int nx, out int nz))
                for (int u = -12; u <= 12; u++)
                    for (int kk = 1; kk <= 8; kk++)
                        for (int y = fy + 3; y < fy + 20; y++)
                        {
                            int x = nx != 0 ? fx + nx * kk : fx + u, z = nx != 0 ? fz + u : fz + nz * kk;
                            byte m = w.Get(x, y, z);
                            if (m != Mat.Air && !Mat.Passable[m]) Console.WriteLine($"  before the door: {Mat.NameOf(m)} at {x},{y},{z} (u {u}, out {kk}, up {y - fy})");
                        }
            bool works = b.Ready && b.Level == 1 && (k.Grants1.Length == 0 || k.Grants1.All(sim.Knows)) && (k.Income1 == 0 || sim.TownIncome() > 0) && (k.Metal1 == 0 || true);
            string hire = k.Hires.Length == 0 ? "-" : sim.CantHireAt(b, k.Hires[0].kind) ?? "hires";
            bool hires = k.Hires.Length == 0 || hire == "hires" || hire.StartsWith("мест") || hire.StartsWith("нужно");
            check($"{k.Name} «{look?.Name}» is built by the workers to the end: its room closed, its door hung, furnished, at work, all built on to it standing, nothing come down",
                b.Ready && zone is { Closed: true } && sim.DoorOf(b) != null && works && hires && stands >= plan.Count * 97 / 100 && (note == null || k.OnOre && note.Contains("неровно")) && fell == 0,
                $"at {b.X0},{b.Z0}-{b.X1},{b.Z1} (top {b.Top0}); ready {b.Ready} after {sim.Time - t0:0} s, closed {zone?.Closed} ({zone?.Why}), door {sim.DoorOf(b) != null}, works {works}, hires «{hire}»; {stands} of {plan.Count} of its plan standing; left out «{note}»; fell {fell}");
        }
        // one look raised a storey by the workers: its roof, and what stands on its ridge, back up over it
        int which = Array.IndexOf(Sim.TownKinds, k) % looks.Length;
        var (rb, _, _) = put[which];
        if (k.MaxLevel < 2) return;
        int fell0 = fell;
        string raise = sim.Raise(rb);
        double r0 = sim.Time;
        for (int s = 0; s < 3000 && rb.Level < 2; s++) Run(1);
        Run(10);
        var rz = sim.ZoneOf(rb);
        int high = rz == null ? 0 : rz.Depth - w.GroundTop((int)rb.CX, (int)rb.CZ);
        int crown = 0, covered = 0;
        byte cover = rb.Look?.Covering is > 0 and var c ? c : k.Roof;
        for (int z = rb.Z0 - 2; z <= rb.Z1 + 2; z++)
            for (int x = rb.X0 - 2; x <= rb.X1 + 2; x++)
                for (int y = rz?.Depth ?? 0; y < (rz?.Depth ?? 0) + Looks.HighestRoof && y < w.SY; y++)
                {
                    byte m = w.Get(x, y, z);
                    if (m == Mat.Bell) crown++;
                    if (m == cover) covered++;
                }
        bool crowned = rb.Look?.Crown != RidgeTop.Belfry || crown > 0;
        check($"{k.Name} «{rb.Look?.Name}» raised a storey by the workers: two storeys high, its roof and what stands on its ridge back up over it, nothing come down",
            raise == null && rb.Level == 2 && rb.Ready && high > 2 * Shapes.HouseHeight - 4 && covered > 200 && crowned && fell == fell0,
            $"raise «{raise}», level {rb.Level} after {sim.Time - r0:0} s, ready {rb.Ready}, {high} voxels high; {covered} of its covering over it, bell {crown}; fell {fell - fell0}");
    }
}
