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

        // each working building shows its trade outside: a sign by its door, a chimney over a hearth, the things of its
        // trade before it; none of it where the door's leaves swing, and the door still walked out of
        Run(12);
        string Front(Building x) => x == null ? "none" : string.Join(" ", sim.FrontageOf(x).GroupBy(f => f.Kind).Select(g => $"{g.Key}×{g.Count()}"));
        int Of(Building x, FixtureKind k) => x == null ? 0 : sim.FrontageOf(x).Count(f => f.Kind == k);
        bool inSwing = false;
        foreach (var x in new[] { hall, mk, b })
            if (x != null && sim.DoorOf(x) is { } door)
            {
                var swept = new HashSet<(int, int, int)>();
                for (int st = 0; st <= Gateway.StepOf(1); st++) for (int i = 0; i < Gateway.Voxels; i++) swept.Add(door.Place(i, st));
                foreach (var f in sim.FrontageOf(x)) foreach (var (vx, vy, vz, _) in f.Voxels) inSwing |= swept.Contains((vx, vy, vz));
            }
        var outOfDoor = b == null ? null : sim.HireAt(b, Kinds.Spearman);
        check("each working building shows its trade outside: a sign by its door, a chimney over its hearth, the things of its trade before it, none of it where the door swings",
            Of(hall, FixtureKind.Sign) == 1 && Of(hall, FixtureKind.Chimney) == 1 && Of(hall, FixtureKind.Trade) >= 3
            && Of(mk, FixtureKind.Sign) == 1 && Of(mk, FixtureKind.Chimney) == 0 && Of(mk, FixtureKind.Trade) == 2
            && Of(b, FixtureKind.Sign) == 1 && Of(b, FixtureKind.Chimney) == 1 && Of(b, FixtureKind.Trade) >= 1 && !inSwing && outOfDoor != null,
            $"the hall: {Front(hall)}; the market: {Front(mk)}; the barracks: {Front(b)}; in the door's swing {inSwing}; one hired {(outOfDoor != null ? "out of the door" : "not")}");

        // the houses pay dues to the town hall by their bunks, and more for each comfort about them: a well, then paving
        // at the door; a house only drawn counts nothing toward the settlement's rank
        string dues = "no level ground for a house";
        bool duesOk = false;
        // level ground near the hall, clear of the buildings and their fronts
        (int x, int z)? houseAt = null;
        for (int r = 90; r < 360 && houseAt == null && hall != null; r += 8)
            for (int a = 0; a < 32 && houseAt == null; a++)
            {
                int x = (int)(hall.CX + MathF.Cos(a * MathF.Tau / 32) * r), z = (int)(hall.CZ + MathF.Sin(a * MathF.Tau / 32) * r);
                int lo = int.MaxValue, hi = 0;
                bool clear = true;
                for (int dx = -40; dx <= 40 && clear; dx += 4)
                    for (int dz = -44; dz <= 44 && clear; dz += 4)
                    {
                        int h = w.SurfAt(x + dx, z + dz);
                        lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                        clear = !site.Forbidden(x + dx, z + dz) && sim.BuildingAt(x + dx, z + dz) == null && sim.ZoneAt(x + dx, z + dz) == null
                            && !Mat.IsTrunk(w.Get(x + dx, h + 2, z + dz)) && w.Get(x + dx, h - 1, z + dz) != Mat.Water;
                    }
                if (clear && hi - lo <= 2) houseAt = (x, z);
            }
        if (houseAt is var (hx, hz))
        {
            var st = new Stroke { Mat = Mat.Stone, Width = Shapes.HouseWall, Height = Shapes.HouseHeight, Level = true };
            int points0 = sim.SettlementPoints();
            var plan = sim.PlanHouseAt(hx - 18, hz - 22, hx + 18, hz + 22, st, -1, out var hd, out string hwhy, out var harched, out var hrect, out _, out _, out var hmoves);
            int drawn = plan == null ? 0 : sim.PutHouse(plan, harched, hd, hrect, hmoves);
            Run(3);
            int pointsDrawn = sim.SettlementPoints();
            sim.InstantLabour = true;
            sim.CompleteAll();
            var home = sim.Zones.Find(z => z.Kind == Zone.Dwelling && z.X0 == hrect.x0 && z.Z0 == hrect.z0);
            for (int i = 0; i < 60 && home != null && !sim.LivedIn(home); i++) { Run(1); sim.CompleteAll(); sim.LookOver(home); }
            sim.InstantLabour = false;
            Run(3);
            float bare = home == null ? 0 : sim.DuesOf(home);
            int pointsLived = sim.SettlementPoints();
            // a well beside it
            sim.InstantLabour = true;
            string well = "";
            foreach (var (wx, wz) in new[] { (hrect.x1 + 24, (hrect.z0 + hrect.z1) / 2), (hrect.x0 - 24, (hrect.z0 + hrect.z1) / 2), ((hrect.x0 + hrect.x1) / 2, hrect.z0 - 24) })
                if ((well = sim.PlaceDecor(DecorKind.Well, wx, wz, 0)) == null) break;
            sim.InstantLabour = false;
            Run(3);
            float watered = home == null ? 0 : sim.DuesOf(home);
            // a cobbled way along it
            sim.InstantLabour = true;
            sim.Pave(Sim.WayPlan(w, site, hrect.x0 - 8, hrect.z1 + 10, hrect.x1 + 8, hrect.z1 + 10, 12, WayKind.Cobble));
            sim.CompleteAll();
            sim.InstantLabour = false;
            Run(3);
            float paved = home == null ? 0 : sim.DuesOf(home);
            float town = sim.TownIncome();
            int beds = home?.Beds ?? 0;
            duesOk = drawn > 0 && home != null && beds > 0 && pointsDrawn == points0 && pointsLived == points0 + 10 && well == null
                && MathF.Abs(bare - beds * Sim.DuesPerBunk) < .01f && MathF.Abs(watered - bare * 1.5f) < .01f && MathF.Abs(paved - bare * 2) < .01f
                && sim.Dues() >= paved - .01f && town >= sim.Dues() + 3;
            dues = $"house «{hwhy}», {beds} bunks; points {points0} → drawn {pointsDrawn} → lived {pointsLived}; dues bare {bare:0.##}, with a well «{well}» {watered:0.##}, paved {paved:0.##}; " +
                $"comforts {(home == null ? "-" : sim.ComfortOf(home).ToString())}; all dues {sim.Dues():0.##}, the town {town:0.##} a minute";
        }
        check("houses lived in pay dues to the town hall by their bunks, half as much again for a well and again for paving; a house only drawn counts nothing toward the rank", duesOk, dues);
        var house = houseAt == null ? null : sim.Zones.Find(z => z.Kind == Zone.Dwelling && sim.LivedIn(z));

        // each building's roof covered as its trade has it: tiles on the hall and the market, slate on the barracks; a
        // house's of logs; thatch and shingles burn, tiles and slate do not, and a thatched roof catches at a spark
        int Covered(Building x, byte m)
        {
            int n = 0;
            if (x == null) return 0;
            for (int z = x.Z0 - Shapes.Eaves; z <= x.Z1 + Shapes.Eaves; z++)
                for (int xx = x.X0 - Shapes.Eaves; xx <= x.X1 + Shapes.Eaves; xx++)
                    for (int y = x.Top0; y < Math.Min(w.SY, x.Top0 + 3 * Shapes.HouseHeight + 24); y++) if (w.Get(xx, y, z) == m) n++;
            return n;
        }
        int houseRoof = 0;
        if (house != null)
            for (int z = house.Z0 - 2; z <= house.Z1 + 2; z++)
                for (int x = house.X0 - 2; x <= house.X1 + 2; x++)
                    for (int y = house.Depth; y < house.Depth + 24; y++) if (Mat.IsRoofing(w.Get(x, y, z))) houseRoof++;
        // a patch of thatch and one of tiles on a stone plinth out in the field, a spark put to each
        var (px, pz) = spots.Count > 3 ? spots[3] : (kx - 300, kz + 40);
        int pg = w.SurfAt(px, pz);
        for (int x = px - 2; x <= px + 16; x++)
            for (int z = pz - 2; z <= pz + 8; z++)
            {
                for (int y = pg - 2; y < pg + 4; y++) w.Set(x, y, z, Mat.Stone);
                for (int y = pg + 4; y < pg + 12; y++) w.Set(x, y, z, Mat.Air);
            }
        for (int x = 0; x < 6; x++) for (int z = 0; z < 6; z++) { w.Set(px + x, pg + 4, pz + z, Mat.Thatch); w.Set(px + 9 + x, pg + 4, pz + z, Mat.Tile); }
        bool sparkThatch = sim.Ignite(px, pg + 4, pz), sparkTile = sim.Ignite(px + 9, pg + 4, pz);
        Run(18);
        int thatchLeft = 0, tileLeft = 0;
        for (int x = 0; x < 6; x++) for (int z = 0; z < 6; z++) { if (w.Get(px + x, pg + 4, pz + z) == Mat.Thatch && !sim.IsBurning(px + x, pg + 4, pz + z)) thatchLeft++; if (w.Get(px + 9 + x, pg + 4, pz + z) == Mat.Tile) tileLeft++; }
        check("a thatched roof catches at a spark and burns through, a tiled one takes no fire",
            sparkThatch && !sparkTile && thatchLeft < 30 && tileLeft == 36, $"spark on thatch {sparkThatch}, on tiles {sparkTile}; 18 s on, unburnt thatch {thatchLeft} of 36, tiles {tileLeft} of 36");
        check("each town building's roof is covered as its trade has it: tiles on the hall and the market, slate on the barracks, a house's of logs; thatch and shingles burn, tiles and slate do not",
            Covered(hall, Mat.Tile) > 400 && Covered(mk, Mat.Tile) > 400 && Covered(b, Mat.Slate) > 400 && Covered(b, Mat.Tile) == 0 && houseRoof == 0 && house != null
            && Sim.TownKinds.All(k => Mat.IsRoofing(k.Roof)) && Sim.TownKinds.Select(k => k.Roof).Distinct().Count() == 4
            && Mat.Burns(Mat.Thatch) && Mat.Burns(Mat.Shingle) && !Mat.Burns(Mat.Tile) && !Mat.Burns(Mat.Slate) && Mat.Catch[Mat.Thatch] > 4 * Mat.Catch[Mat.Wood],
            $"tiles on the hall {Covered(hall, Mat.Tile)}, on the market {Covered(mk, Mat.Tile)}; slate on the barracks {Covered(b, Mat.Slate)}, tiles there {Covered(b, Mat.Tile)}; the house's covering {houseRoof}");

        // the street by day: idle workers go to the market's stalls to keep them and to choose, to the hall's board, and
        // stand there facing them; the alarm, the dusk and work each send them off at once
        sim.Clock.Night = false; sim.Clock.DawnLeft = 0; sim.Clock.Left = DayClock.DayLength;
        if (sim.Alarm) sim.StandDown();
        Run(6);
        var places = sim.Street.GroupBy(s => s.Does).ToDictionary(g => g.Key, g => g.Count());
        var idle = sim.Units.Where(u => !u.Dead && u.T == Kinds.Worker).ToList();
        int atStreet = 0, facing = 0;
        var doing = new HashSet<Pastime>();
        for (int i = 0; i < 160 && atStreet < 3; i++)
        {
            Run(1);
            atStreet = facing = 0;
            foreach (var u in idle)
            {
                if (u.Spot == null || u.Pastime != u.Spot.Does) continue;
                atStreet++; doing.Add(u.Pastime);
                if (MathF.Abs(u.X - u.Spot.X) < .01f && MathF.Abs(u.Z - u.Spot.Z) < .01f && MathF.Abs(MathF.IEEERemainder(u.Facing - u.Spot.Facing, MathF.Tau)) < .5f) facing++;
            }
        }
        double streetAt = sim.Time;
        sim.RaiseAlarm();
        Run(1.5);
        int afterAlarm = idle.Count(u => u.Spot != null);
        sim.StandDown();
        for (int i = 0; i < 160 && idle.Count(u => u.Spot != null && u.Pastime == u.Spot.Does) < 2; i++) Run(1);
        int back = idle.Count(u => u.Spot != null && u.Pastime == u.Spot.Does);
        sim.Clock.Left = DayClock.Dusk * .5;
        Run(1.5);
        int atDusk = idle.Count(u => u.Spot != null);
        sim.Clock.Left = DayClock.DayLength;
        for (int i = 0; i < 160 && idle.Count(u => u.Spot != null && u.Pastime == u.Spot.Does) < 2; i++) Run(1);
        var keepers = idle.Where(u => u.Spot != null && u.Pastime == u.Spot.Does).ToList();
        // work for everyone: a long trench dug by the citadel
        var trench = new VoxelList();
        int tg = w.SurfAt(kx + 200, kz - 60);
        for (int x = kx + 160; x < kx + 240; x++) for (int z = kz - 64; z < kz - 56; z++) for (int y = w.SurfAt(x, z) - 4; y < w.SurfAt(x, z); y++) trench.Add(x, y, z, Mat.Air);
        sim.DesignateRemove(trench);
        Run(4);
        int stayed = keepers.Count(u => u.Spot != null);
        sim.CancelArea(kx + 150, kz - 70, kx + 250, kz - 50);
        check("by day idle workers go to the town's street, to keep the market's stalls, to choose at them, to read the hall's board, and stand at their places facing them; the alarm, the dusk and work send them off",
            places.GetValueOrDefault(Pastime.Sell) == 2 && places.GetValueOrDefault(Pastime.Shop) == 4 && places.GetValueOrDefault(Pastime.Read) == 1
            && atStreet >= 3 && facing == atStreet && afterAlarm == 0 && back >= 2 && atDusk == 0 && keepers.Count >= 2 && stayed == 0,
            $"places {string.Join(", ", places.Select(p => $"{p.Key} {p.Value}"))}; at them {atStreet} ({string.Join(", ", doing)}), {facing} standing on theirs facing it, by {streetAt:0} s; " +
            $"after the alarm {afterAlarm}, back {back}; at dusk {atDusk}; given work {stayed} of {keepers.Count} stayed");

        // the Capitol: the hall's third storey waits for the settlement to be a «Посад»; raised by the workers it pays more
        // and the houses pay it half as much again; its third floor is walked up to from its door
        sim.InstantLabour = true;
        string capWait = hall == null ? "no hall" : sim.Raise(hall);
        for (int i = 0; i < 120 && hall != null && hall.Level < 2; i++) { Run(.5); sim.CompleteAll(); }
        int standing0 = sim.Standing;
        string early = hall == null ? "no hall" : sim.CantRaise(hall);
        sim.Standing = 3;
        float income2 = sim.TownIncome(), dues2 = house == null ? 0 : sim.DuesOf(house);
        string capRaise = hall == null ? "no hall" : sim.Raise(hall);
        for (int i = 0; i < 160 && hall != null && hall.Level < 3; i++) { Run(.5); sim.CompleteAll(); }
        sim.InstantLabour = false;
        Run(3);
        var hz0 = hall == null ? null : sim.ZoneOf(hall);
        int high = hz0 == null ? 0 : hz0.Depth - w.GroundTop((int)hall.CX, (int)hall.CZ);
        float income3 = sim.TownIncome(), dues3 = house == null ? 0 : sim.DuesOf(house);
        // up to the third floor from the door: a cell standing in the room two storeys over the first
        bool upstairs = false;
        if (hall != null && hz0 != null && sim.DoorOf(hall) is { } hdoor)
        {
            var (dx, dy, dz) = hdoor.Middle;
            int from = sim.StandNear((int)dx + (int)MathF.Sign(dx - hall.CX) * 10, (int)dy, (int)dz + (int)MathF.Sign(dz - hall.CZ) * 10, 2);
            int floor3 = hall.Top0 + Shapes.HouseHeight;
            var path = sim.Paths.Find(from, c => sim.Nav.Y(c) * 8 >= floor3 - 4 && sim.Nav.I(c) * 8 > hall.X0 && sim.Nav.I(c) * 8 < hall.X1 && sim.Nav.K(c) * 8 > hall.Z0 && sim.Nav.K(c) * 8 < hall.Z1,
                c => 0, Kinds.Worker.Move, 60000, gates: true);
            upstairs = path != null;
        }
        check("the Capitol, the hall's third storey, waits for a «Посад», is raised by the workers three storeys high, pays more, the houses half as much again, and its top floor is walked up to",
            capWait == null && hall?.Level >= 2 && early != null && early.Contains("Посад") && capRaise == null && hall?.Level == 3 && hall.Ready
            && high > 3 * Shapes.HouseHeight - 4 && income3 >= income2 + 6 && MathF.Abs(dues3 - dues2 * Sim.CapitolDues) < .01f && dues2 > 0 && upstairs,
            $"second storey «{capWait}», the third at level 2 «{early}» (rank {standing0}); raised «{capRaise}», level {hall?.Level}, ready {hall?.Ready}, {high} voxels high; " +
            $"income {income2:0.#} → {income3:0.#}; a house's dues {dues2:0.##} → {dues3:0.##}; up to the third floor {upstairs}");

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
        check("the town is read back from a save: its buildings, their levels and stocks, and what they give, and what stands before them",
            sim2.Buildings.Count == sim.Buildings.Count && sim2.FrontageOf(b2).Count == sim.FrontageOf(b).Count && sim2.FrontageOf(b2).Count > 0 && b2 is { Level: 2, Ready: true } && sim2.Knows("shieldwall") && sim2.ZoneOf(b2)?.Name == "Казарма"
            && Math.Abs(b2.Stock.GetValueOrDefault(Kinds.Spearman.Key) - b.Stock.GetValueOrDefault(Kinds.Spearman.Key)) < .01f,
            $"{sim2.Buildings.Count} of {sim.Buildings.Count}; the barracks level {b2?.Level}, ready {b2?.Ready}; the shield wall {sim2.Knows("shieldwall")}");
    }
}
