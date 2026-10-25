using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// Our engines (--ourengines): the branch of the lore they are learnt in; taken on at the muster for gold and timber,
/// out of the citadel with their crews; sent somewhere, packed up, rolled and set up there; an engine of the horde
/// before a band as the mark, a stone breaking it and the deck a band stands on; a trebuchet throwing from beyond the
/// horde's catapult's reach; a ballista's bolt through a file of raiders and a giant staggered; the pitch kegs set
/// alight; no stone where ours stand by the band; the crew killed and the engine idle till a worker takes their place;
/// and saved and read back.
/// </summary>
static class OurEnginesBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        // the branch: since 25.10.2026 one node, the workshop (Sim.Town.cs), whose levels give the engines and their lores
        var branch = Sim.LoresOn(1).Where(l => l.Branch == "Машины").ToList();
        var works = Sim.TownKindOf("workshop");
        bool shaped = branch.Count == 1 && branch[0].Key == "b_workshop" && Sim.PageBranches[1].Contains("Машины") && works?.Lore == "b_workshop"
            && Kinds.OurEngines.All(t => works.Hires.Any(h => h.kind == t) && works.Grants1.Concat(works.Grants2).Contains(t.Lore)
                && Kinds.Troops.Contains(t) && Kinds.IsEngine(t) && Bodies.Looks(t) == 2 && t.Crew > 0 && t.Logs > 0);
        check("our engines are put together in the workshop, which a branch of the troops, «Машины», opens, and taken on with timber and a crew",
            shaped, $"{branch.Count} lores: {string.Join(", ", branch.Select(l => $"{l.Key} {l.Tier}"))}; the workshop hires {string.Join(", ", works?.Hires.Select(h => h.kind.Name) ?? new string[0])}");

        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
        float Metres(float a, float b, float c, float d) => MathF.Sqrt((a - c) * (a - c) + (b - d) * (b - d)) / VoxelWorld.PerMetre;
        // a long field of level ground west of the citadel, bare of trees
        int fx0 = (site.KeepX - 1000) & ~7, fz0 = (site.KeepZ - 120) & ~7, fg = w.SurfAt(Math.Max(8, fx0) + 400, fz0 + 120);
        fx0 = Math.Max(16, fx0);
        void Level()
        {
            for (int z = fz0; z < fz0 + 240; z++)
                for (int x = fx0; x < fx0 + 880; x++)
                    for (int y = fg - 26; y < w.SY; y++) w.Set(x, y, z, y < fg ? (y < fg - 4 ? Mat.Rock : Mat.Soil) : Mat.Air);
            sim.Burning.Clear();
            Run(.5);
        }
        Level();
        int midZ = fz0 + 120;
        Unit Put(UnitType t, int x, int z)
        {
            int c = sim.NearestStand(x, z, t.Move.Height);
            var u = sim.Spawn(t, c);
            (u.X, u.Y, u.Z) = sim.Nav.Feet(c);
            if (t.Team == Team.Horde) { u.Guard = true; u.Home = u.Cell; }
            return u;
        }
        // an engine of ours set up where it is put, its crew about it
        (Unit e, List<Unit> crew) Engine(UnitType t, int x, int z)
        {
            var e = Put(t, x, z);
            e.Rig = 1;
            var crew = new List<Unit>();
            for (int i = 0; i < t.Crew; i++) { var c = Put(Kinds.Crewman, x - 24 - i * 6, z + (i - 1) * 8); c.Escort = e; crew.Add(c); }
            return (e, crew);
        }
        // everything of the test before gone, and the field level and bare again (its rubble would stand in a bolt's way)
        void Clean()
        {
            foreach (var u in sim.Units) if (u.Team != Team.Player || Kinds.IsEngine(u.T) || u.T == Kinds.Crewman || u.X > fx0 && u.X < fx0 + 880 && u.Z > fz0 && u.Z < fz0 + 240) Gone(u);
            sim.Fliers.Clear(); sim.Arrows.Clear();
            Level();
        }

        // not at the citadel since 25.10.2026: assembled at a workshop (Sim.Town.cs), gold and timber paid, out of its
        // door with its crew (the workshop here as it would stand, built and working)
        sim.Knowledge = 100;
        string before = sim.CantEnlist(Kinds.Mangonel);
        bool learnt = sim.Learn("b_workshop");
        var (gx, _, gz) = sim.Nav.Feet(sim.Gate);
        var shop = new Building { Kind = "workshop", X0 = (int)gx - 18, Z0 = (int)gz + 30, X1 = (int)gx + 18, Z1 = (int)gz + 66, Level = 1, Ready = true };
        shop.Stock[Kinds.Mangonel.Key] = 1;
        sim.Buildings.Add(shop);
        sim.Stock.Gold = 1000; sim.Stock[Stockpile.Wood] = 1;
        string shortWood = sim.CantHireAt(shop, Kinds.Mangonel);
        sim.Stock[Stockpile.Wood] = 10 * VoxelWorld.PerM3;
        double gold0 = sim.Stock.Gold, wood0 = sim.Stock[Stockpile.Wood];
        int people0 = sim.Household();
        bool took = sim.HireAt(shop, Kinds.Mangonel) != null;
        sim.Buildings.Remove(shop);
        var mustered = sim.Units.Find(u => u.T == Kinds.Mangonel && !u.Dead);
        int crewOut = mustered == null ? 0 : sim.CrewOf(mustered);
        check("a stone-thrower is not taken on at the citadel; the workshop assembles one for gold and three cubic metres of timber, and it comes out with a crew of two, three places in all",
            before != null && learnt && shortWood != null && shortWood.Contains("брёвен") && took && crewOut == 2 && sim.Household() - people0 == 3
            && Math.Abs(wood0 - sim.Stock[Stockpile.Wood] - Kinds.Mangonel.Logs * VoxelWorld.PerM3) < .01 && sim.Stock.Gold < gold0,
            $"before: «{before}»; short of timber: «{shortWood}»; crew {crewOut}; places +{sim.Household() - people0}; timber {wood0} → {sim.Stock[Stockpile.Wood]}");
        foreach (var u in sim.Units) if (!u.Dead && (u.T == Kinds.Mangonel || u.T == Kinds.Crewman)) Gone(u);

        // sent somewhere: packed up, rolled there by its crew, set up again
        var (cart, cartCrew) = Engine(Kinds.Mangonel, fx0 + 600, midZ);
        int to = sim.NearestStand(fx0 + 600, midZ + 60 + 40, 3);
        sim.Command(new[] { cart }, to);
        bool packed = false;
        for (int i = 0; i < 300 && !(cart.Rig >= 1 && cart.Path == null && Metres(cart.X, cart.Z, fx0 + 600, midZ) > 5); i++)
        {
            Run(.2);
            if (cart.Rig <= 0) packed = true;
        }
        float moved = Metres(cart.X, cart.Z, fx0 + 600, midZ);
        check("sent somewhere, a stone-thrower is packed up, rolled there by its crew, and set up again where it stops, the crew by it",
            packed && moved > 8 && cart.Rig >= 1 && sim.CrewAt(cart) == 2,
            $"packed {packed}, moved {moved:0.0} m, set up {cart.Rig:0.00}, crew at it {sim.CrewAt(cart)}; at {sim.Time:0} s");
        Clean();

        // a band of goblins and a horde catapult both within its reach: the catapult is the mark; stones break it up
        var (m1, _) = Engine(Kinds.Mangonel, fx0 + 700, midZ);
        var hordeCat = Put(Kinds.Catapult, fx0 + 700 - 300, midZ + 20);
        hordeCat.ChainedUntil = 1e9;
        var band = new List<Unit>();
        for (int i = 0; i < 6; i++) band.Add(Put(Kinds.Goblin, fx0 + 700 - 200 + i % 3 * 6, midZ - 30 + i / 3 * 6));
        foreach (var g in band) g.ChainedUntil = 1e9;
        float catHp = hordeCat.Hp;
        int shots0 = sim.EngineShots;
        Unit firstMark = null;
        for (int i = 0; i < 400 && sim.EngineShots < shots0 + 6; i++)
        {
            Run(.2);
            if (firstMark == null && sim.EngineShots > shots0) firstMark = m1.HurlAt;
        }
        Run(6);
        check("a stone-thrower takes the horde's catapult for its mark before a band, and its stones break it up",
            firstMark == hordeCat && hordeCat.Hp < catHp * .8f,
            $"first mark {firstMark?.T.Key ?? "none"}; the catapult {catHp:0} → {hordeCat.Hp:0} hp ({(hordeCat.Dead ? "broken" : "standing")}); {sim.EngineShots - shots0} thrown");
        Clean();

        // a deck of the horde's with a band on it: the stones break it up under them
        int dx0 = fx0 + 400, dz0 = midZ - 16;
        for (int z = dz0; z < dz0 + 32; z++)
            for (int x = dx0; x < dx0 + 32; x++)
                for (int y = fg; y < fg + 2; y++) w.Set(x, y, z, Mat.Deck);
        Run(.3);
        int Decks() { int n = 0; for (int z = dz0; z < dz0 + 32; z++) for (int x = dx0; x < dx0 + 32; x++) for (int y = fg; y < fg + 2; y++) if (w.Get(x, y, z) == Mat.Deck) n++; return n; }
        int deck0 = Decks();
        var onDeck = new List<Unit>();
        for (int i = 0; i < 5; i++) { var g = Put(Kinds.Orc, dx0 + 8 + i % 3 * 7, dz0 + 10 + i / 3 * 10); g.ChainedUntil = 1e9; g.Hp = g.MaxHp = 1e5f; onDeck.Add(g); }
        var (m2, _) = Engine(Kinds.Mangonel, dx0 + 16 + 300, midZ);
        shots0 = sim.EngineShots;
        for (int i = 0; i < 500 && sim.EngineShots < shots0 + 6; i++) Run(.2);
        Run(6);
        int deck1 = Decks();
        check("a stone-thrower's stones come down on a band on the horde's deck and break the deck up under it",
            deck1 < deck0 - 30, $"deck {deck0} → {deck1} voxels, {sim.EngineShots - shots0} thrown");
        Clean();

        // a trebuchet throws from beyond the reach of the horde's catapult, and its stone comes down by the mark
        sim.Learnt.Add("trebuchets");
        var (treb, _) = Engine(Kinds.Trebuchet, fx0 + 820, midZ);
        var farCat = Put(Kinds.Catapult, fx0 + 820 - 75 * VoxelWorld.PerMetre, midZ);
        farCat.ChainedUntil = 1e9; farCat.Hp = farCat.MaxHp = 1e6f;
        var near = new List<(float x, float z)>();
        sim.Landed += (x, y, z, n, s) => { if (n >= 100) near.Add((x, z)); };
        shots0 = sim.EngineShots;
        for (int i = 0; i < 400 && sim.EngineShots < shots0 + 3; i++) Run(.2);
        Run(7);
        float bestMiss = near.Count == 0 ? 999 : near.Min(p => Metres(p.x, p.z, farCat.X, farCat.Z));
        float apart = Metres(treb.X, treb.Z, farCat.X, farCat.Z);
        check("a trebuchet throws at the horde's catapult from 75 metres, beyond the 40 it throws, and its stone comes down by it",
            sim.EngineShots > shots0 && apart > 60 && apart * VoxelWorld.PerMetre > Kinds.Catapult.Range * 1.5f && bestMiss < 4,
            $"{sim.EngineShots - shots0} thrown from {apart:0} m; nearest landing {bestMiss:0.0} m off; the catapult {farCat.MaxHp - farCat.Hp:0} hp the worse");
        Clean();

        // a ballista's bolt through a file of goblins, and a hill giant staggered by one
        sim.Learnt.Add("ballistas");
        var (bal, _) = Engine(Kinds.Ballista, fx0 + 600, midZ);
        var file = new List<Unit>();
        for (int i = 0; i < 4; i++) { var g = Put(Kinds.Goblin, fx0 + 600 - 220 - i * 10, midZ); g.ChainedUntil = 1e9; g.Hp = g.MaxHp = 300; file.Add(g); }
        int through0 = sim.SpearsThrough;
        shots0 = sim.EngineShots;
        for (int i = 0; i < 100 && sim.EngineShots < shots0 + 2; i++) Run(.1);
        Run(1);
        int struck = file.Count(g => g.Hp < g.MaxHp);
        check("a ballista's bolt goes on through a file of raiders, three and four of them struck by one",
            sim.SpearsThrough - through0 >= 2 && struck >= 3, $"{sim.SpearsThrough - through0} times through; {struck} of 4 struck");
        foreach (var g in file) Gone(g);
        var giant = Put(Kinds.HillGiant, fx0 + 600 - 260, midZ);
        giant.Hp = giant.MaxHp = 1e5f; giant.Guard = true;
        bal.Cooldown = 0; bal.HurlAt = null;
        double staggered = -1;
        for (int i = 0; i < 100 && staggered < 0; i++) { Run(.1); if (giant.StaggerUntil > sim.Time) staggered = sim.Time; }
        check("a ballista's bolt staggers a hill giant where it stands, its throw put off", staggered > 0 && giant.NextHurl > sim.Time,
            $"staggered at {staggered:0.0} s, next throw at {giant.NextHurl:0.0}");
        Clean();

        // the pitch kegs: every other throw a keg of burning pitch, and what it comes down on burns
        sim.Learnt.Add("pitchkegs");
        var (m3, _) = Engine(Kinds.Mangonel, fx0 + 600, midZ);
        var lot = new List<Unit>();
        for (int i = 0; i < 6; i++) { var g = Put(Kinds.Orc, fx0 + 600 - 220 + i % 3 * 6, midZ + i / 3 * 6); g.ChainedUntil = 1e9; g.Hp = g.MaxHp = 1e5f; lot.Add(g); }
        int burning0 = sim.Burning.Count, kegs = 0;
        shots0 = sim.EngineShots;
        for (int i = 0; i < 400 && sim.EngineShots < shots0 + 4; i++) { Run(.2); kegs = Math.Max(kegs, sim.Arrows.Count(a => a.Kind == Shot.Keg)); }
        Run(4);
        check("with the pitch kegs every other throw is a keg of burning pitch, and the ground and the band where it breaks catch",
            kegs > 0 && (sim.Burning.Count > burning0 || lot.Any(g => g.Burn > 0)), $"kegs in the air {kegs}; burning {burning0} → {sim.Burning.Count}; orcs alight {lot.Count(g => g.Burn > 0)}");
        Clean();
        sim.Learnt.Remove("pitchkegs");

        // none thrown where ours stand by the band
        var (m4, _) = Engine(Kinds.Mangonel, fx0 + 600, midZ);
        var knot = new List<Unit>();
        for (int i = 0; i < 5; i++) { var g = Put(Kinds.Goblin, fx0 + 600 - 220 + i * 4, midZ); g.ChainedUntil = 1e9; knot.Add(g); }
        var ourMan = Put(Kinds.Swordsman, fx0 + 600 - 212, midZ + 10);
        ourMan.Hold = true; ourMan.Dest = ourMan.Cell;
        shots0 = sim.EngineShots;
        Run(20);
        check("a stone-thrower throws nothing at a band with one of ours by it", sim.EngineShots == shots0, $"{sim.EngineShots - shots0} thrown");
        Clean();

        // the crew cut down: it stands; a worker idle near it takes their place, and it throws again
        var (m5, crew5) = Engine(Kinds.Mangonel, fx0 + 600, midZ);
        var mark5 = Put(Kinds.Orc, fx0 + 600 - 220, midZ);
        mark5.ChainedUntil = 1e9; mark5.Hp = mark5.MaxHp = 1e5f;
        foreach (var c in crew5) { c.Dead = true; c.DiedAt = sim.Time; }
        shots0 = sim.EngineShots;
        Run(15);
        int idleShots = sim.EngineShots - shots0;
        var hand = Put(Kinds.Worker, fx0 + 600 + 80, midZ + 40);
        for (int i = 0; i < 300 && sim.EngineShots == shots0 + idleShots; i++) Run(.2);
        check("an engine whose crew is cut down stands idle; a worker idle near it takes their place and it throws again",
            idleShots == 0 && hand.Escort == m5 && sim.EngineShots > shots0, $"thrown with no crew {idleShots}; the worker at it {hand.Escort == m5}; thrown after {sim.EngineShots - shots0}");

        // saved and read back: the engine, set up where it was, its crew still its own
        var ms = new System.IO.MemoryStream();
        sim.Save(ms, mp.Seed, "engines", "");
        ms.Position = 0;
        var wL = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        Sim simL;
        using (var reader = new SaveReader(ms))
        {
            var siteL = MapGen.Generate(wL, new MapParams { Seed = reader.Seed });
            wL.Compact();
            reader.Valley(wL, siteL);
            simL = new Sim(wL, siteL);
            simL.Load(reader);
        }
        var backE = simL.Units.Find(u => u.Id == m5.Id);
        var backC = simL.Units.Find(u => u.Id == hand.Id);
        for (double t = 0; t < .5; t += .05) simL.Step(.05f);
        check("an engine is saved and read back as itself, set up where it stood, the one who took the crew's place still at it",
            backE != null && backE.T == Kinds.Mangonel && backE.Rig >= 1 && backC != null && backC.Escort == backE,
            backE == null ? "gone" : $"{backE.T.Key}, set up {backE.Rig:0.0}, crew {(backC?.Escort == backE)}");
    }
}
