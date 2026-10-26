using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's ground for what the realm's gold goes on (--economy, Campaign.Economy.cs, Artifacts.cs): the heroes a
/// new campaign starts with by its size and the faster walk of ours on the bigger maps; the tavern's two heroes, their
/// price and the realm's room for them; artifacts put on and taken off, what they give a hero's skills, moves, sight
/// and the realm's dawn; the merchant's week of goods and a purchase by the citadel; the rare resources sold and bought;
/// an artifact in a chest and in a treasure, the treasury when no hero is by; a convoy carried home and one waylaid;
/// all of it written and read back; the dear top storeys of the town in a campaign; thirty days of the autoplayer
/// spending the gold and the rare resources on heroes, things and the town.
/// </summary>
static class EconomyBench
{
    static MapParty HeroAt(Campaign c, MapCell at)
    {
        var p = c.Parties.First(x => x.Owner == Owner.Ours && x.Party.Commander?.Hero != null);
        p.Q = at.Q; p.R = at.R; p.CameQ = at.Q; p.CameR = at.R; p.Path = null; p.Progress = 0; p.StepCost = 0;
        p.MovesMax = p.MovesLeft = c.MovesOf(p);
        return p;
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();

        // ---- the heroes a campaign starts with, and the pace of ours on the bigger maps
        {
            var counts = new List<int>();
            var paces = new List<int>();
            foreach (int side in new[] { 36, 72, 108 })
            {
                var c = Campaign.NewWorld(7, side);
                c.StartHeroes();
                counts.Add(c.Parties.Count(p => p.Owner == Owner.Ours && p.Party.Commander?.Hero != null));
                paces.Add(c.Parties.First(p => p.Owner == Owner.Ours).MovesMax);
            }
            check("a campaign starts with one hero on a small map, two on a middling and three on a large, and ours walk further on the bigger maps",
                counts.SequenceEqual(new[] { 1, 2, 3 }) && paces[1] > paces[0] && paces[2] > paces[1],
                $"heroes {string.Join("/", counts)}, the first hero's moves {string.Join("/", paces)}");
        }

        // ---- the tavern
        {
            var c = Campaign.NewWorld(7, 36);
            c.StartHeroes();
            c.Gold = 5000;
            var tav = c.TavernNow();
            bool fresh = tav.Count == 2 && tav[0].Calling != tav[1].Calling && tav.All(t => t.Hero.Level == 1 && t.Hero.Grade.Count(g => g == 1) == 1 && t.Army.Length > 0)
                && tav[0].Hero.Name != tav[1].Hero.Name;
            int army = tav[0].Army.Length;
            string name = tav[0].Hero.Name;
            var p = c.HireHero(0);
            bool paid = Math.Abs(c.Gold - (5000 - Campaign.HeroPrice)) < 1e-6;
            bool home = p != null && p.Q == c.Home.Q && p.R == c.Home.R && p.Party.Members.Count == army + 1 && p.Party.Commander.Hero.Name == name;
            int cap = c.HeroCap();
            string full = c.CantHireHero(0);
            c.Home.Sum.Hall = 1;
            int cap2 = c.HeroCap();
            var p2 = c.HireHero(0);
            c.SkipToDawn();
            for (int d = 0; d < 7; d++) c.SkipToDawn();
            var next = c.TavernNow();
            check("the tavern has two heroes of two callings at level 1, each with a grade of his calling's skill and his men; one hired costs 600 and stands by the citadel; the realm's room for heroes is two and a town hall's level more; next week two others wait",
                fresh && paid && home && cap == 2 && full != null && cap2 == 3 && p2 != null && next.Count == 2 && next.All(t => t.Hero.Name != name),
                $"the tavern: {string.Join(", ", tav.Select(t => t.Calling))}; hired {name} with {army}, paid {paid}, at home {home}; room {cap}, then «{full}», with a hall {cap2}, the third {(p2 != null ? "hired" : "not")}; next week {string.Join(", ", next.Select(t => t.Hero.Name))}");
        }

        // ---- artifacts on and off, and what they give
        {
            var c = Campaign.NewWorld(9, 36);
            c.StartHeroes();
            var p = HeroAt(c, c.Home);
            var h = p.Party.Commander.Hero;
            int tactics0 = h[Skill.Tactics], moves0 = MapWalk.MaxMoves(p, c.Week), sight0 = Campaign.SightOf(p);
            float steady0 = Heroes.Steady(h, false);
            Artifacts.Give(h, Artifacts.Of("sharp_sword"));
            bool sword = h.Worn[(int)ArtSlot.Weapon] == "sharp_sword" && h[Skill.Tactics] == tactics0 + 1 && Heroes.Steady(h, false) > steady0;
            Artifacts.Give(h, Artifacts.Of("tracker_bow"));
            bool bagged = h.Bag.Contains("tracker_bow");
            bool swapped = Artifacts.PutOn(h, "tracker_bow") && h.Worn[(int)ArtSlot.Weapon] == "tracker_bow" && h.Bag.Contains("sharp_sword") && h[Skill.Tactics] == tactics0 && h[Skill.Archers] >= 1;
            bool off = Artifacts.TakeOff(h, ArtSlot.Weapon) && h.Worn[(int)ArtSlot.Weapon] == null && h.Bag.Count == 2;
            Artifacts.Give(h, Artifacts.Of("road_boots"));
            Artifacts.Give(h, Artifacts.Of("far_ring"));
            bool feet = MapWalk.MaxMoves(p, c.Week) == moves0 + 200 && Campaign.SightOf(p) == sight0 + 2;
            // a skill learnt to its top and a thing over it: one grade over the top at the most
            h.Grade[(int)Skill.Line] = Heroes.Grades;
            Artifacts.Give(h, Artifacts.Of("guard_plate"));
            bool capped = h[Skill.Line] == Artifacts.MostGrade;
            check("an artifact put on gives its skill's grade (and so the hero's weight in a battle counted at once), a second for the same place goes into the bag and can be swapped in, taken off it is in the bag; boots and a ring give moves and sight; a skill is lifted one grade over its top at the most",
                sword && bagged && swapped && off && feet && capped,
                $"sword {sword}, bow into the bag {bagged}, swapped {swapped}, off {off}; moves {moves0} → {MapWalk.MaxMoves(p, c.Week)}, sight {sight0} → {Campaign.SightOf(p)}; Строй learnt 3 with the plate {h[Skill.Line]}");
        }

        // ---- a dawn's gift of what a hero wears
        {
            var a = Campaign.NewWorld(9, 36); a.StartHeroes();
            var b = Campaign.NewWorld(9, 36); b.StartHeroes();
            Artifacts.Give(a.Parties.First(p => p.Party.Commander != null).Party.Commander.Hero, Artifacts.Of("purse_ring"));
            Artifacts.Give(a.Parties.First(p => p.Party.Commander != null).Party.Commander.Hero, Artifacts.Of("lord_crown"));
            a.SkipToDawn(); b.SkipToDawn();
            double more = a.Gold - b.Gold;
            check("a hero's purse ring and crown give the realm their gold at every dawn", Math.Abs(more - 125) < 1e-6, $"{more:0.##} gold more than without them");
        }

        // ---- the merchant, the rare resources' trade
        {
            var c = Campaign.NewWorld(9, 36);
            c.StartHeroes();
            var goods = c.MarketNow();
            bool week = goods.Count == 6 && goods.Count(k => Artifacts.Of(k).Tier == ArtTier.Minor) == 4 && goods.Count(k => Artifacts.Of(k).Tier == ArtTier.Major) == 2
                && goods.Distinct().Count() == 6 && c.MarketNow().SequenceEqual(goods);
            var p = HeroAt(c, c.Home);
            var away = c.Cells.First(x => x.Passable && x != c.Home && Campaign.Distance(x.Q, x.R, c.Home.Q, c.Home.R) == 3);
            HeroAt(c, away);
            c.Gold = 5000;
            for (int i = 0; i < c.Rare.Length; i++) c.Rare[i] = 10;
            var major = goods.First(k => Artifacts.Of(k).Tier == ArtTier.Major);
            string far = c.CantBuy(p, major);
            HeroAt(c, c.Home);
            var art = Artifacts.Of(major);
            double rare0 = art.Rare.Length > 0 ? c.Rare[art.Rare[0].res] : 0;
            bool bought = c.Buy(p, major);
            bool paid = Math.Abs(c.Gold - (5000 - art.Gold)) < 1e-6 && (art.Rare.Length == 0 || Math.Abs(c.Rare[art.Rare[0].res] - (rare0 - art.Rare[0].n)) < 1e-6)
                && p.Party.Commander.Hero.Worn[(int)art.Slot] == major && !c.Market.Contains(major);
            double gold0 = c.Gold, crystals0 = c.Rare[2];
            int sold = c.SellRare(2, 5);
            bool sale = sold == 5 && Math.Abs(c.Gold - (gold0 + 5 * Campaign.RareSells)) < 1e-6 && Math.Abs(c.Rare[2] - (crystals0 - 5)) < 1e-6;
            gold0 = c.Gold; double mercury0 = c.Rare[0];
            int got = c.BuyRare(0, 2);
            bool buy = got == 2 && Math.Abs(c.Gold - (gold0 - 2 * Campaign.RareBuys)) < 1e-6 && Math.Abs(c.Rare[0] - (mercury0 + 2)) < 1e-6;
            check("the merchant by the citadel has four minor artifacts and two major ones a week, no relic; a hero away from the citadel cannot buy, one by it pays its gold and rare resources and wears it; rare resources are sold for gold and bought",
                week && far != null && bought && paid && sale && buy,
                $"goods: {string.Join(", ", goods.Select(k => Artifacts.Of(k).Name))}; away «{far}»; «{art.Name}» for {art.Price()} bought {bought}, paid and worn {paid}; 5 crystals sold {sale}, 2 mercury bought {buy}");
        }

        // ---- an artifact in a chest, in a treasure; the treasury
        {
            var c = Campaign.NewWorld(5, 72);
            c.StartHeroes();
            var p = HeroAt(c, c.Home);
            var h = p.Party.Commander.Hero;
            // a chest whose roll has an artifact in it
            var chest = c.Cells.FirstOrDefault(x => x.Thing is { Kind: ThingKind.Chest, Taken: false } t && Hash.H3((int)x.Seed, 71, 5) < Campaign.ChestFind((int)t.Amount));
            int had = h.Worn.Count(k => k != null) + h.Bag.Count;
            if (chest != null) c.Chests.Add((p, chest));
            string said = chest == null ? "" : c.OpenChest(chest, true);
            bool inChest = chest != null && said.Contains("Артефакт") && h.Worn.Count(k => k != null) + h.Bag.Count == had + 1;
            // a treasure of the far zones taken by the hero; another with nobody by: into the treasury
            var troves = c.Cells.Where(x => x.Trove != TroveKind.None && x.Trove != TroveKind.Shrine && Campaign.Untaken(x)).OrderByDescending(Campaign.RingOf).ToList();
            var far = troves.FirstOrDefault(x => Campaign.RingOf(x) >= 2);
            had = h.Worn.Count(k => k != null) + h.Bag.Count;
            string farSaid = far == null ? "" : c.TakeTrove(far, p);
            var got = h.Worn.Concat(h.Bag).Where(k => k != null).Select(Artifacts.Of).ToList();
            bool rich = far != null && h.Worn.Count(k => k != null) + h.Bag.Count == had + 1 && got.Any(a => a.Tier >= ArtTier.Major);
            var near = troves.LastOrDefault(x => x != far);
            int vault0 = c.Vault.Count;
            if (near != null) c.TakeTrove(near);
            bool kept = near != null && c.Vault.Count == vault0 + 1;
            bool taken = kept && c.TakeFromVault(p, c.Vault[^1]) && c.Vault.Count == vault0;
            check("a chest whose lot it is has an artifact in it for the hero who opens it; a treasure of the far zones a major one or a relic; one taken with no hero by goes into the citadel's treasury, and a hero by the citadel takes it",
                inChest && rich && kept && taken,
                $"chest {(chest == null ? "none with a lot" : $"at {chest.Col},{chest.Row}: «{said}»")}; far treasure (ring {(far == null ? -1 : Campaign.RingOf(far))}) {rich}; the treasury {kept}, taken {taken}");
        }

        // ---- convoys
        {
            var c = Campaign.NewWorld(7, 36);
            c.StartHeroes();
            c.Gold = 1000;
            var home = c.Home;
            // an outpost of ours four to eight hexes off
            var post = c.Cells.Where(x => x.Passable && x.Thing == null && x.Sum.Keep.Count == 0 && !x.Pass && x.Zone == home.Zone)
                .Select(x => (x, d: Campaign.Distance(x.Q, x.R, home.Q, home.R))).Where(t => t.d >= 4 && t.d <= 8).OrderBy(t => t.d).First().x;
            post.Owner = Owner.Ours; post.Outpost = true; post.Holding = Holding.Outpost; post.Sum.Raised = 1;
            post.Sum.Stone = 20; post.Sum.Logs = 6;
            double stone0 = home.Sum.Stone, logs0 = home.Sum.Logs;
            var cart = c.SendConvoy(post, home, new[] { 6.0, 20.0, 0 }, out string why);
            bool sent = cart != null && post.Sum.Stone < 1e-6 && post.Sum.Logs < 1e-6 && Math.Abs(c.Gold - (1000 - Campaign.ConvoyCost(26))) < 1e-6 && cart.MovesMax == c.MovesOf(cart) && cart.MovesMax < c.MovesOf(c.Parties.First(x => x.Party.Commander != null));
            for (int t = 0; t < 6 * DayClock.Cycle && c.Parties.Contains(cart); t += 5) c.Advance(5);
            bool came = !c.Parties.Contains(cart) && Math.Abs(home.Sum.Stone - (stone0 + 20)) < 1e-6 && Math.Abs(home.Sum.Logs - (logs0 + 6)) < 1e-6;
            // another, and a party of the horde standing in its way: waylaid, its load lost
            post.Sum.Stone = 10;
            var way = MapWalk.Find(c, post, home, Owner.Ours);
            var cart2 = c.SendConvoy(post, home, new[] { 0, 10.0, 0 }, out _);
            var block = way[Math.Min(2, way.Count - 2)];
            var horde = new MapParty { Id = 9999, Owner = Owner.Horde, Q = block.Q, R = block.R, CameQ = block.Q, CameR = block.R, Party = new Party() };
            for (int i = 0; i < 6; i++) horde.Party.Members.Add(new Traveller { Kind = Kinds.Goblin, Hp = Kinds.Goblin.Hp, MaxHp = Kinds.Goblin.Hp });
            c.Parties.Add(horde);
            double stone1 = home.Sum.Stone;
            for (int t = 0; t < 6 * DayClock.Cycle && cart2 != null && c.Parties.Contains(cart2); t += 5) c.Advance(5);
            bool lost = cart2 != null && !c.Parties.Contains(cart2) && Math.Abs(home.Sum.Stone - stone1) < 1e-6;
            check("a convoy takes a valley's stores, its fee paid, walks slower than a hero and unloads into the other valley of ours; one walking into the horde's men is waylaid and its load lost",
                sent && came && lost, $"sent {sent} («{why}»), {(cart != null ? $"moves {cart.MovesMax}" : "")}; arrived {came} (stone {stone0:0.#} → {home.Sum.Stone:0.#}); waylaid {lost}");
        }

        // ---- written and read back
        {
            var c = Campaign.NewWorld(7, 36);
            c.StartHeroes();
            c.Gold = 5000;
            c.TavernNow(); c.MarketNow();
            var p = c.Parties.First(x => x.Party.Commander != null);
            Artifacts.Give(p.Party.Commander.Hero, Artifacts.Of("mail"));
            Artifacts.Give(p.Party.Commander.Hero, Artifacts.Of("guard_plate"));
            c.Vault.Add("lord_crown");
            var post = c.Cells.Where(x => x.Passable && x.Thing == null && x.Sum.Keep.Count == 0 && !x.Pass && x.Zone == c.Home.Zone && Campaign.Distance(x.Q, x.R, c.Home.Q, c.Home.R) == 6).First();
            post.Owner = Owner.Ours; post.Outpost = true; post.Holding = Holding.Outpost; post.Sum.Raised = 1; post.Sum.Metal = 2;
            var cart = c.SendConvoy(post, c.Home, new[] { 0, 0, 2.0 }, out _);
            c.Advance(60);
            string dir = Path.Combine(Path.GetTempPath(), "vt-economy-bench");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            c.Write(dir);
            var r = Campaign.Read(dir);
            var rp = r.Parties.First(x => x.Id == p.Id);
            var rc = r.Parties.FirstOrDefault(x => x.Id == cart.Id);
            bool same = rp.Party.Commander.Hero.Worn.SequenceEqual(p.Party.Commander.Hero.Worn) && rp.Party.Commander.Hero.Bag.SequenceEqual(p.Party.Commander.Hero.Bag)
                && rp.Party.Commander.Hero[Skill.Line] == p.Party.Commander.Hero[Skill.Line]
                && r.Vault.SequenceEqual(c.Vault) && r.Market.SequenceEqual(c.Market) && r.TavernNow().Select(t => t.Hero.Name).SequenceEqual(c.Tavern.Select(t => t.Hero.Name))
                && rc != null && rc.Convoy && Math.Abs(rc.Cargo[(int)Res.Metal] - 2) < 1e-6 && rc.CargoFrom == cart.CargoFrom;
            check("what the heroes wear and carry, the treasury, the merchant's goods, the tavern and a convoy on its way are written with the campaign and read back the same", same,
                $"worn {string.Join(",", rp.Party.Commander.Hero.Worn.Where(k => k != null))}, bag {string.Join(",", rp.Party.Commander.Hero.Bag)}; treasury {string.Join(",", r.Vault)}; convoy {(rc?.Convoy == true ? Campaign.Load(rc.Cargo) : "lost")}");
        }

        // ---- the town's dear top storeys in a campaign
        {
            var mp = new MapParams();
            var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
            var site = MapGen.Generate(w, mp);
            var s = new Sim(w, site) { WildTarget = 0 };
            var hall = Sim.TownKindOf("hall");
            int plain2 = s.UpGold(hall, 2), plain3 = s.UpGold(hall, 3), rareOut = s.RareCost("giants", 2).Length;
            s.Campaign = true;
            int camp2 = s.UpGold(hall, 2), camp3 = s.UpGold(hall, 3), rareIn = s.RareCost("giants", 2).Length;
            check("in a campaign a town building's second storey costs three times its gold and the third four times, and the dear dwellings ask for rare resources; out of one as ever",
                plain2 == hall.UpGold && plain3 == hall.Up3Gold && camp2 == 3 * hall.UpGold && camp3 == 4 * hall.Up3Gold && rareOut == 0 && rareIn > 0,
                $"the town hall's second storey {plain2} → {camp2}, the Capitol {plain3} → {camp3}; the giants' hall asks for {rareIn} rare resources in a campaign, {rareOut} out of one");
        }

        // ---- thirty days of the autoplayer: the gold and the rare resources spent
        {
            var c = Campaign.NewWorld(7, 36);
            c.StartHeroes();
            var ap = new AutoPlayer(c);
            ap.Begin();
            for (int day = 0; day < 30; day++)
                for (int t = 0; t < DayClock.Cycle; t += 10) { c.Advance(10); ap.Tick(); }
            int hired = ap.Said.Count(x => x.Contains("Нанят герой")), steps = ap.Built, things = ap.Said.Count(x => x.Contains("купил") || x.Contains("из сокровищницы"));
            int found = c.Parties.Where(p => p.Party.Commander?.Hero != null).Sum(p => p.Party.Commander.Hero.Worn.Count(k => k != null) + p.Party.Commander.Hero.Bag.Count);
            check("in thirty days the autoplayer spends the realm's gold and rare resources: a hero from the tavern, the town's steps (the dear ones with rare resources), artifacts found or bought",
                hired >= 1 && steps >= 6 && found + things >= 1 && c.Gold < 3000,
                $"heroes hired {hired}, town steps {steps}, artifacts on the heroes {found}, bought {things}; gold now {c.Gold:0}, rare {string.Join("/", c.Rare.Select(x => x.ToString("0")))}");
        }
    }
}
