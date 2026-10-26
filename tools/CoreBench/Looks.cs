using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's looks of the town's buildings (--looks, Sim.Looks.cs): every kind has two looks or more, each its own and
/// no two alike, and no kind's first look is another's; the kinds differ in body as they should (the barracks and the
/// stables long, the giants' hall and the workshop tall, the
/// forge, the mason's yard and the carpenter's a small shed by a big open part) and in footprint; each look of each kind
/// is put up and built by the workers to the end — its room closed, its door hung, furnished, at work — with everything
/// built on to it standing and nothing come down; and one look of each kind is raised a storey by the workers, its roof
/// and what stands on its ridge going back up over it. LOOKS_ONLY=kind tries that kind alone.
/// </summary>
static class LooksBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        string only = Environment.GetEnvironmentVariable("LOOKS_ONLY");
        // the looks themselves: how many, all different, and every kind's first its own
        string Sig(Look l) => $"{l.Walls}/{l.Roof}/{l.Covering}/{l.Crown}/{l.DD}/{l.DW}/{l.DH}/" + string.Join("+", l.Annexes.Select(a => $"{a.Kind}{a.At}{a.Depth}{(a.Gabled ? "g" : "")}{(a.Round ? "r" : "")}{(a.Walled ? "w" : "")}{(a.AtEnd ? "e" : "")}"));
        var few = Sim.TownKinds.Where(k => Sim.LooksOf(k).Length < 2).Select(k => k.Key).ToList();
        var alike = Sim.TownKinds.Where(k => Sim.LooksOf(k).Select(Sig).Distinct().Count() < Sim.LooksOf(k).Length).Select(k => k.Key).ToList();
        var firsts = Sim.TownKinds.Select(k => (k.Wall, Sig(Sim.LooksOf(k)[0]))).ToList();
        int names = Sim.TownKinds.SelectMany(k => Sim.LooksOf(k)).Select(l => l.Name).Distinct().Count();
        check("every kind of town building has two looks or more, each its own, and no two kinds' first looks alike",
            few.Count == 0 && alike.Count == 0 && firsts.Distinct().Count() == Sim.TownKinds.Length && names == Sim.TownKinds.Sum(k => Sim.LooksOf(k).Length),
            $"{Sim.TownKinds.Sum(k => Sim.LooksOf(k).Length)} looks of {Sim.TownKinds.Length} kinds; with fewer than two: {string.Join(", ", few)}; alike within: {string.Join(", ", alike)}; distinct firsts {firsts.Distinct().Count()}");

        // the bodies: the kinds as long, wide and tall as they are meant to stand, and no two looks of one footprint
        // and height but where a kind has only so many
        TownKind K(string key) => Sim.TownKindOf(key);
        int Len(TownKind k) => k.D;
        int Rise(TownKind k) => Sim.RiseOf(k, null);
        var house = new TownKind();
        bool longest = Len(K("barracks")) >= 96 && Len(K("stables")) >= 104 && Len(K("infirmary")) >= 88;
        bool wide = Sim.TownKinds.All(k => k.W <= 38) && new[] { "giants", "workshop", "trolls", "hall", "barracks", "stables" }.All(key => K(key).W == 38);
        bool tall = Rise(K("giants")) >= 36 && Rise(K("workshop")) >= 32 && Rise(K("hall")) > Rise(house) && Rise(K("stables")) == Rise(house);
        bool small = new[] { "smithy", "carpentry", "masons", "lab", "mine" }.All(key => K(key).W * K(key).D < house.W * house.D);
        var bodies = Sim.TownKinds.SelectMany(k => Sim.LooksOf(k).Select(l => (k.W + l.DW, k.D + l.DD, Sim.RiseOf(k, l)))).Distinct().Count();
        var plans = Sim.TownKinds.SelectMany(k => Sim.LooksOf(k).Select(l => string.Join("+", l.Annexes.Select(a => $"{a.Kind}{a.At}{(a.Walled ? "w" : "")}{(a.AtEnd ? "e" : "")}")))).Distinct().Count();
        check("the kinds of town buildings differ in body: the barracks, the stables and the infirmary long, the big ones as wide as a floor spans, the giants' hall, the workshop and the town hall tall, the forge and the yards' sheds small",
            longest && wide && tall && small && bodies >= 20 && plans >= 20,
            $"long {longest} (barracks {Len(K("barracks"))}, stables {Len(K("stables"))}, infirmary {Len(K("infirmary"))}), wide {wide}, tall {tall} (giants {Rise(K("giants"))}, workshop {Rise(K("workshop"))}, hall {Rise(K("hall"))}), small {small}; {bodies} bodies and {plans} plans of {Sim.TownKinds.Sum(k => Sim.LooksOf(k).Length)} looks");

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
        // (no water anywhere: the fields evened out for the looks would draw a river or a lake into them)
        for (int x = 0; x < w.SX; x++)
            for (int z = 0; z < w.SZ; z++)
                for (int y = 1; y < w.SY; y++)
                    if (w.Get(x, y, z) == Mat.Water) w.Set(x, y, z, Mat.Air);
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
        // (what is still marked about a building that will not go up, for LOOKS_SAY)
        void Marked(Building b)
        {
            if (!say) return;
            int g = w.GroundTop((int)b.CX, (int)b.CZ);
            var rows = new SortedDictionary<string, (int n, int x0, int x1, int z0, int z1)>();
            for (int z = b.Z0 - 60; z <= b.Z1 + 60; z++)
                for (int x = b.X0 - 60; x <= b.X1 + 60; x++)
                    for (int y = g - 6; y < g + 140; y++)
                    {
                        // (its own, not a neighbour's)
                        if (sim.BuildingAt(x, z) is { } other && other != b) continue;
                        byte o = sim.Orders.At(x, y, z);
                        if (o == 0) continue;
                        string key = $"{y - g,3} {(Orders.IsRemove(o) ? "out" : Mat.NameOf(o))} (now {Mat.NameOf(w.Get(x, y, z))}, footed {sim.Footed(x, y, z)})";
                        var r = rows.TryGetValue(key, out var was) ? was : (n: 0, x0: int.MaxValue, x1: int.MinValue, z0: int.MaxValue, z1: int.MinValue);
                        rows[key] = (r.n + 1, Math.Min(r.x0, x - b.X0), Math.Max(r.x1, x - b.X0), Math.Min(r.z0, z - b.Z0), Math.Max(r.z1, z - b.Z0));
                    }
            foreach (var (key, r) in rows) Console.WriteLine($"  still marked at {key}: {r.n}, x {r.x0}..{r.x1}, z {r.z0}..{r.z1} of {b.X1 - b.X0 + 1}×{b.Z1 - b.Z0 + 1}");
            if (b.Target > b.Level)
            {
                var zz = sim.ZoneOf(b);
                var over = zz == null ? null : Shapes.OverHouse(w, b.X0, b.Z0, b.X1, b.Z1, zz.Depth);
                var (fs, fp, fb) = sim.FurnitureOf(b);
                Console.WriteLine($"  furniture: {fs} of {fp} standing, blocked: {fb}");
                Console.WriteLine($"  raising: storeys waiting {sim.Storeys.Count}, zone {(zz == null ? "none" : $"{zz.X0},{zz.Z0}-{zz.X1},{zz.Z1} depth {zz.Depth}, closed {zz.Closed} «{zz.Why}», planned {zz.Planned}")}, over it {over?.Count}"
                    + (over is { Count: > 0 } ? $" e.g. {Mat.NameOf(w.Get(over.X(0), over.Y(0), over.Z(0)))} at {over.X(0) - b.X0},{over.Y(0) - g},{over.Z(0) - b.Z0}" : ""));
            }
        }
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
            // (what stands by an end may be wider than the end)
            int span = looks.SelectMany(l => l.Annexes).Where(a => a.At is Flank.Left or Flank.Right).Select(a => a.Span).DefaultIfEmpty(0).Max();
            int pitch = Math.Max(k.W, span) + 2 * deep + 40, along = pitch * looks.Length;
            // (north of the citadel, the row kept inside the valley however long it is)
            int cz = kz - Math.Max(site.KeepR + 190, 280), cx = Math.Clamp(kx, along / 2 + 60, w.SX - along / 2 - 60), g = w.SurfAt(cx, cz);
            for (int x = cx - along / 2 - 40; x <= cx + along / 2 + 40; x++)
                for (int z = cz - 130; z <= cz + 130; z++)
                {
                    if (!w.InBounds(x, 0, z) || site.Forbidden(x, z)) continue;
                    for (int y = g - 30; y < g + 140; y++)
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
        // (LOOKS_WAIT=seconds: how long the workers are given, shorter to look into one that will not go up)
        int wait = int.TryParse(Environment.GetEnvironmentVariable("LOOKS_WAIT"), out int ww) ? ww : 3000;
        for (int s = 0; s < wait && put.Exists(p => !p.b.Ready); s++) Run(1);
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
            if (!b.Ready) Marked(b);
            if (say && !b.Ready && sim.FrontOf(b, out int fx, out int fy, out int fz, out int nx, out int nz))
                for (int u = -12; u <= 12; u++)
                    for (int kk = 1; kk <= 8; kk++)
                        for (int y = fy + 3; y < fy + 20; y++)
                        {
                            int x = nx != 0 ? fx + nx * kk : fx + u, z = nx != 0 ? fz + u : fz + nz * kk;
                            byte m = w.Get(x, y, z);
                            if (m != Mat.Air && !Mat.Passable[m]) Console.WriteLine($"  before the door: {Mat.NameOf(m)} at {x},{y},{z} (u {u}, out {kk}, up {y - fy})");
                        }
            // (how long the ten workers took over all the kind's looks at once, and how much each asked)
            Console.WriteLine($"  {k.Name} «{look?.Name}»: {b.X1 - b.X0 + 1}×{b.Z1 - b.Z0 + 1}×{b.Top0 - w.GroundTop((int)b.CX, (int)b.CZ)} voxels, {plan.Count} voxels laid ({plan.Count / (double)VoxelWorld.PerM3:0.0} m³)");
            bool works = b.Ready && b.Level == 1 && (k.Grants1.Length == 0 || k.Grants1.All(sim.Knows)) && (k.Income1 == 0 || sim.TownIncome() > 0) && (k.Metal1 == 0 || true);
            string hire = k.Hires.Length == 0 ? "-" : sim.CantHireAt(b, k.Hires[0].kind) ?? "hires";
            bool hires = k.Hires.Length == 0 || hire == "hires" || hire.StartsWith("мест") || hire.StartsWith("нужно");
            check($"{k.Name} «{look?.Name}» is built by the workers to the end: its room closed, its door hung, furnished, at work, all built on to it standing, nothing come down",
                b.Ready && zone is { Closed: true } && sim.DoorOf(b) != null && works && hires && stands >= plan.Count * 97 / 100 && (note == null || k.OnOre && note.Contains("неровно")) && fell == 0,
                $"at {b.X0},{b.Z0}-{b.X1},{b.Z1} (top {b.Top0}); ready {b.Ready} after {sim.Time - t0:0} s, closed {zone?.Closed} ({zone?.Why}), door {sim.DoorOf(b) != null}, works {works}, hires «{hire}»; {stands} of {plan.Count} of its plan standing; left out «{note}»; fell {fell}");
        }
        // one look raised a storey by the workers: its roof, and what stands on its ridge, back up over it
        // (LOOKS_RAISE=n: that look raised instead)
        int which = int.TryParse(Environment.GetEnvironmentVariable("LOOKS_RAISE"), out int wr) ? wr % looks.Length : Array.IndexOf(Sim.TownKinds, k) % looks.Length;
        var (rb, _, _) = put[which];
        if (k.MaxLevel < 2) return;
        int fell0 = fell;
        string raise = sim.Raise(rb);
        double r0 = sim.Time;
        for (int s = 0; s < wait && rb.Level < 2; s++) Run(1);
        Run(10);
        if (rb.Level < 2) Marked(rb);
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
            raise == null && rb.Level == 2 && rb.Ready && high > Sim.RiseOf(k, rb.Look) + Shapes.HouseHeight - 4 && covered > 200 && crowned && fell == fell0,
            $"raise «{raise}», level {rb.Level} after {sim.Time - r0:0} s, ready {rb.Ready}, {high} voxels high; {covered} of its covering over it, bell {crown}; fell {fell - fell0}");
    }
}
