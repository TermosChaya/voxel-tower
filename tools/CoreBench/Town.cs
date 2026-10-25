using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's town (--town, Sim.Town.cs): the lore's first two pages open only buildings, and every improvement they
/// used to give is given by a building's level instead; a building is refused till its lore and the buildings it needs;
/// the barracks put up by the workers themselves stands closed and furnished and works — it hires spearmen out of its
/// stock at its door, the citadel no longer does, the household has its four places more — and raised a storey it hires
/// shieldbearers and gives the shield wall; the stock grows by a wave; the town hall pays by the town's size, the
/// market trades; and all of it is read back from a save.
/// </summary>
static class TownBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        // the lore: buildings only on the town's and the troops' pages; each old improvement given by some building's level
        string[] improvements =
        {
            "yew", "barbs", "braziers", "volley", "owleye", "longbows", "mortar", "masons", "ironbound", "granite", "bastion", "fortress", "picks", "barrows",
            "buckets", "carts", "hides", "guild", "gambesons", "drill", "banners", "mail", "sergeants", "veterans", "spikes", "tar", "soaked", "naphtha",
            "firestorm", "deathtraps", "shieldwall", "longshafts", "leadshot", "bodkins", "hunt", "charge", "barracks", "drillyard", "bunks", "boulders",
            "pitchkegs", "windlass", "heavystones",
            "spearmen", "shieldmen", "halberds", "knights", "slingers", "crossbows", "bombards", "alchemists", "hounds", "riders", "horsearchers", "lancers",
            "trolls", "giants", "mangonels", "ballistas", "trebuchets",
        };
        var granted = new HashSet<string>(Sim.TownGrants());
        var opened = new HashSet<string>(Sim.TownKinds.Select(k => k.Lore));
        var notBuilding = Sim.Lores.Where(l => l.Page < 2 && !opened.Contains(l.Key)).Select(l => l.Key).ToList();
        var lost = improvements.Where(k => !granted.Contains(k)).ToList();
        var inTree = improvements.Where(k => Sim.LoreOf(k) != null).ToList();
        check("the lore's town and troops pages open only buildings, and every improvement they used to give is a building's level now",
            notBuilding.Count == 0 && lost.Count == 0 && inTree.Count == 0 && Sim.TownKinds.All(k => Sim.LoreOf(k.Lore) != null),
            $"not a building: {string.Join(", ", notBuilding)}; given by none: {string.Join(", ", lost)}; still in the tree: {string.Join(", ", inTree)}; {Sim.TownKinds.Length} kinds of building");

        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        StartingFort.Raise(sim);
        int kx = site.KeepX, kz = site.KeepZ;
        // level spots of the plateau a walk from the citadel, far enough apart for a building each
        var spots = new List<(int x, int z)>();
        for (int r = 120; r < 420 && spots.Count < 4; r += 8)
            for (int a = 0; a < 24 && spots.Count < 4; a++)
            {
                int x = kx + (int)(Math.Cos(a * Math.Tau / 24) * r), z = kz + (int)(Math.Sin(a * Math.Tau / 24) * r);
                if (x < 90 || z < 90 || x > w.SX - 90 || z > w.SZ - 90 || spots.Exists(s => Math.Abs(s.x - x) < 110 && Math.Abs(s.z - z) < 110)) continue;
                int lo = int.MaxValue, hi = 0;
                for (int dx = -44; dx <= 44; dx += 4) for (int dz = -44; dz <= 44; dz += 4) { int h = w.SurfAt(x + dx, z + dz); lo = Math.Min(lo, h); hi = Math.Max(hi, h); if (Mat.IsTrunk(w.Get(x + dx, h + 2, z + dz))) hi = 999; }
                if (hi - lo <= 2 && !site.Forbidden(x, z)) spots.Add((x, z));
            }
        if (spots.Count < 3) { check("level ground for the town", false, $"{spots.Count} spots"); return; }
        for (int i = 0; i < 6; i++) sim.Spawn(Kinds.Worker, sim.Gate);
        sim.Stock[Stockpile.Wood] += 800.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 800.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Metal] += 40.0 * VoxelWorld.PerM3;
        sim.Stock.Gold = 2000;
        void Run(double s) { for (double t = 0; t < s; t += .05) sim.Step(.05f); }

        // refused till its lore, and the market till a town hall stands
        var barracks = Sim.TownKindOf("barracks");
        string before = sim.CantBuild(barracks);
        sim.Knowledge = 40;
        bool learnt = sim.Learn("b_barracks") && sim.Learn("b_hall") && sim.Learn("b_market");
        string after = sim.CantBuild(barracks), market = sim.CantBuild(Sim.TownKindOf("market"));
        check("a building is refused till its lore is learnt, and one till the buildings it needs stand",
            before != null && learnt && after == null && market != null && market.Contains("Ратуша"), $"before «{before}»; learnt {learnt}; after «{after}»; the market «{market}»");

        // the barracks put up and built by the workers: closed, furnished, working
        string put = sim.PutUp(barracks, spots[0].x, spots[0].z, false, out var b);
        double built = -1;
        for (int i = 0; i < 1200 && b != null && !b.Ready; i++) { Run(.5); if (b.Ready) built = sim.Time; }
        var zone = b == null ? null : sim.ZoneOf(b);
        int placesWith = sim.Places;
        string atCitadel = sim.CantEnlist(Kinds.Spearman);
        float stock0 = b == null ? 0 : b.Stock.GetValueOrDefault(Kinds.Spearman.Key);
        var spear = b == null ? null : sim.HireAt(b, Kinds.Spearman);
        float stock1 = b == null ? 0 : b.Stock.GetValueOrDefault(Kinds.Spearman.Key);
        float fromDoor = spear == null ? 999 : MathF.Sqrt((spear.X - b.CX) * (spear.X - b.CX) + (spear.Z - b.CZ) * (spear.Z - b.CZ)) / VoxelWorld.PerMetre;
        check("the barracks put up and built by the workers stands closed and furnished and works: it hires spearmen at its door out of its stock, the citadel does not, and the household has four places more",
            put == null && b is { Ready: true, Level: 1 } && zone?.Name == "Казарма" && sim.Knows("spearmen") && sim.Knows("barracks") && atCitadel != null
            && stock0 >= 4 && spear != null && stock1 == stock0 - 1 && fromDoor < 12,
            $"put up «{put}»; ready {b?.Ready} at {built:0} s, level {b?.Level}, called «{zone?.Name}»; knows spearmen {sim.Knows("spearmen")}; " +
            $"the citadel: «{atCitadel}»; stock {stock0:0.#} → {stock1:0.#}; the spearman {(spear == null ? "not hired" : $"{fromDoor:0.0} m from it")}; places {placesWith}");

        // raised a storey: shieldbearers, the shield wall
        bool knewWall = sim.Knows("shieldwall");
        string raise = b == null ? "no barracks" : sim.Raise(b);
        double raised = -1;
        for (int i = 0; i < 1600 && b != null && b.Level < 2; i++) { Run(.5); if (b.Level >= 2) raised = sim.Time - built; }
        var shield = b == null ? null : sim.HireAt(b, Kinds.Shieldbearer);
        check("raised a storey the barracks hires shieldbearers and gives the shield wall",
            raise == null && b?.Level == 2 && !knewWall && sim.Knows("shieldwall") && sim.Knows("shieldmen") && shield != null,
            $"raise «{raise}»; level {b?.Level} after {raised:0} s; the shield wall before {knewWall}, after {sim.Knows("shieldwall")}; a shieldbearer {(shield != null ? "hired" : sim.CantHireAt(b, Kinds.Shieldbearer))}");

        // the stock grows by its growth at each daybreak (a week in the Heroes), no more than twice it; within the day not at all
        if (b != null) b.Stock[Kinds.Spearman.Key] = 0;
        Run(150);
        float within = b == null ? -1 : b.Stock.GetValueOrDefault(Kinds.Spearman.Key);
        int day0 = sim.Clock.Day;
        void Daybreak() { sim.Clock.Night = true; sim.Clock.Break(sim); }
        Daybreak();
        float grown = b == null ? 0 : b.Stock.GetValueOrDefault(Kinds.Spearman.Key);
        Daybreak(); Daybreak();
        float full = b == null ? 0 : b.Stock.GetValueOrDefault(Kinds.Spearman.Key);
        check("a building's stock grows by its growth at each daybreak, up to twice it, and not within the day",
            within == 0 && MathF.Abs(grown - 4) < .01f && MathF.Abs(full - 8) < .01f && sim.Clock.Day == day0 + 3,
            $"within the day {within:0.00}, a day on {grown:0.00}, three days on {full:0.00}; day {day0} → {sim.Clock.Day}");

        // the town hall pays by the town's size; the market trades (put up at once, as a staged scene does)
        sim.InstantLabour = true;
        string hallPut = sim.PutUp(Sim.TownKindOf("hall"), spots[1].x, spots[1].z, false, out var hall);
        sim.InstantLabour = false;
        for (int i = 0; i < 240 && hall != null && !hall.Ready; i++) Run(.5);
        sim.InstantLabour = true;
        string marketPut = sim.PutUp(Sim.TownKindOf("market"), spots[2].x, spots[2].z, false, out var mk);
        sim.InstantLabour = false;
        for (int i = 0; i < 240 && mk != null && !mk.Ready; i++) Run(.5);
        // (the mines held pay besides: the town pays at least its share)
        float income = sim.TownIncome();
        double g0 = sim.Stock.Gold;
        Run(60);
        double paid = sim.Stock.Gold - g0;
        double stone0 = sim.Stock[Stockpile.Stone];
        string sold = sim.Trade(Stockpile.Stone, true);
        double gSold = sim.Stock.Gold;
        string bought = sim.Trade(Stockpile.Wood, false);
        check("the town hall pays gold by the town's size, and the market trades the stores for gold and back",
            hallPut == null && marketPut == null && hall?.Ready == true && mk?.Ready == true && income >= 4 && paid >= income - .5 && sold == null && bought == null
            && sim.Stock[Stockpile.Stone] < stone0 && gSold > 0,
            $"hall «{hallPut}» ready {hall?.Ready}, market «{marketPut}» ready {mk?.Ready}; {income:0.#} a minute, paid {paid:0.#} in one; sold «{sold}», bought «{bought}»");

        // all of it read back
        var buf = new System.IO.MemoryStream();
        sim.Save(buf, mp.Seed, "town", "");
        buf.Position = 0;
        var w2 = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        Sim sim2;
        using (var save = new SaveReader(buf))
        {
            var site2 = MapGen.Generate(w2, new MapParams { Seed = save.Seed });
            w2.Compact();
            save.Valley(w2, site2);
            sim2 = new Sim(w2, site2);
            sim2.Load(save);
        }
        var b2 = sim2.BuildingOf("barracks");
        check("the town is read back from a save: its buildings, their levels and stocks, and what they give",
            sim2.Buildings.Count == sim.Buildings.Count && b2 is { Level: 2, Ready: true } && sim2.Knows("shieldwall") && sim2.ZoneOf(b2)?.Name == "Казарма"
            && Math.Abs(b2.Stock.GetValueOrDefault(Kinds.Spearman.Key) - b.Stock.GetValueOrDefault(Kinds.Spearman.Key)) < .01f,
            $"{sim2.Buildings.Count} of {sim.Buildings.Count}; the barracks level {b2?.Level}, ready {b2?.Ready}; the shield wall {sim2.Knows("shieldwall")}");
    }
}
