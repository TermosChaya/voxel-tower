using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's ground for the neutral creatures (--bestiary, Bestiary.cs, Sim.Bestiary.cs, Campaign.Bestiary.cs): every
/// breed a kind the saves know, with a body, in tiers that grow; the big map's stacks of them by its zones' tiers and the
/// hexes' grounds, every pass kept, a dragon only far out; a stack far weaker running from a party, the people of the wild
/// asking to join it and going with it as ours, a strong one holding; a stack beaten gone; the horde's party fighting a
/// stack in its way; in a valley the powers — a web that holds, venom that eats on, a dragon's breath that burns, a
/// golem's boulder, a troll's wounds closing and not under fire, an ogre's blow and a minotaur's charge throwing men, a
/// wraith drinking what it takes, a fire spirit unhurt by fire and a treant burning fast — and a pack of the wild and the
/// horde's raiders fighting each other.
/// </summary>
static class BestiaryBench
{
    static void Step(Sim s, double seconds) { for (double t = 0; t < seconds; t += .05) s.Step(.05f); }

    static Traveller Man(UnitType k) => new() { Kind = k, Hp = k.Hp, MaxHp = k.Hp };

    static Sim Fresh()
    {
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var s = new Sim(w, site) { WildTarget = 0 };
        s.SetSource(new MapRaids());
        s.Clock.Left = 1e9;
        foreach (var u in s.Units) { u.Dead = true; u.DiedAt = -999; }
        return s;
    }

    /// <summary>One of a kind at so many metres east of a spot on the open ground west of the citadel.</summary>
    static Unit Put(Sim s, UnitType t, float metres, int north = 0)
    {
        int x = s.Site.KeepX - s.Site.KeepR - 120 + (int)(metres * VoxelWorld.PerMetre), z = s.Site.KeepZ + 60 + north * 8;
        var u = s.Spawn(t, s.NearestStand(x, z, t.Move.Height));
        u.Facing = 0;
        return u;
    }

    static MapParty Party(Campaign c, MapCell at, int swords, int bows, Owner owner = Owner.Ours, UnitType foot = null)
    {
        var p = new MapParty { Owner = owner, Q = at.Q, R = at.R, CameQ = at.Q, CameR = at.R, Party = new Party() };
        if (owner == Owner.Ours) p.Party.Members.Add(Man(Kinds.Captain));
        for (int i = 0; i < swords; i++) p.Party.Members.Add(Man(foot ?? Kinds.Swordsman));
        for (int i = 0; i < bows; i++) p.Party.Members.Add(Man(Kinds.Archer));
        c.Parties.Add(p);
        return p;
    }

    static void Keep(MapCell c, UnitType t, int n)
    {
        c.Owner = Owner.None;
        c.Sum.Keep.Clear();
        for (int i = 0; i < n; i++) c.Sum.Keep.Add(Man(t));
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();

        // ---- the breeds: known to the saves, with bodies, in tiers that grow
        var tiers = Bestiary.All.GroupBy(b => b.Tier).OrderBy(g => g.Key).Select(g => (tier: g.Key, power: g.Average(b => Bestiary.Power(b.Kind)))).ToList();
        bool known = Bestiary.All.All(b => Sim.KindOf(b.Kind.Key) == b.Kind) && Kinds.Joiners.All(t => Sim.KindOf(t.Key) == t);
        bool growing = true;
        for (int i = 1; i < tiers.Count; i++) growing &= tiers[i].power > tiers[i - 1].power;
        // (a beast's heart is not sculpted, as the bear's and the wolf's are not: the brain is)
        int bodies = 0, organs = 0;
        var hollow = new List<string>();
        foreach (var b in Bestiary.All)
            for (int l = 0; l < Bodies.Looks(b.Kind); l++)
            {
                var plan = Bodies.Plan(b.Kind, l);
                bodies++;
                if (plan.Brain > 0 && (plan.Beast || plan.Heart > 0)) organs++; else hollow.Add($"{b.Kind.Key}:{l} brain {plan.Brain} heart {plan.Heart}");
            }
        var ourBandit = Bodies.Plan(Kinds.OurBandit, 0);
        check("the bestiary: twenty-one breeds in seven tiers, each stronger than the last on the whole, every one a kind the saves know, with a body and a brain in its head (and a heart, the people of it)",
            Bestiary.All.Length >= 18 && tiers.Count == 7 && tiers[0].tier == 1 && growing && known && organs == bodies && ourBandit.Brain > 0,
            $"{Bestiary.All.Length} breeds; tiers {string.Join(", ", tiers.Select(t => $"{t.tier}: {t.power:0}"))}; known {known}; {organs} of {bodies} bodies whole {string.Join("; ", hollow)}");

        // ---- the big map's stacks: by the zones' tiers, at home on their grounds, every pass kept, a dragon far out
        var byTier = new Dictionary<int, List<double>>();
        int stacks = 0, passes = 0, keptPasses = 0, dragons = 0, dragonsNear = 0, treants = 0, treantsWood = 0, fire = 0, fireBurnt = 0, kinds = 0;
        var seen = new HashSet<UnitType>();
        for (uint seed = 1; seed <= 5; seed++)
        {
            // four M maps, and an XL one for its far zones (the sixth and seventh tiers live there)
            var c = Campaign.NewWorld(seed, seed == 5 ? 144 : 72);
            foreach (var cell in c.Cells)
            {
                if (cell.Pass) { passes++; if (cell.Sum.Keep.Count > 0) keptPasses++; }
                if (cell.Thing?.Kind != ThingKind.Monster || cell.Sum.Keep.Count == 0 || Bestiary.Of(cell.Sum.Keep[0].Kind) is not { } b) continue;
                stacks++;
                seen.Add(b.Kind);
                if (!byTier.TryGetValue(cell.Tier, out var l)) byTier[cell.Tier] = l = new List<double>();
                l.Add(QuickBattle.Strength(cell.Sum.Keep, null));
                if (b.Kind == Kinds.Dragon) { dragons++; if (cell.Tier < 3) dragonsNear++; }
                if (b.Kind == Kinds.Treant) { treants++; if (cell.Relief == HexRelief.Forest) treantsWood++; }
                if (b.Kind == Kinds.FireSpirit) { fire++; if (cell.Ground is HexGround.Scorched or HexGround.Lava) fireBurnt++; }
            }
        }
        kinds = seen.Count;
        var means = byTier.OrderBy(k => k.Key).Select(k => (k.Key, mean: k.Value.Average(), n: k.Value.Count)).ToList();
        bool stronger = means.Count >= 3 && means[^1].mean > means[0].mean * 4;
        check("the big map's stacks: of many breeds, the stronger the further out, every pass kept, a dragon rare and only far out, treants only in the woods and fire spirits only on the burnt land",
            stacks > 80 && kinds >= 16 && stronger && keptPasses * 2 >= passes - 2 && dragons < stacks / 20 && dragonsNear == 0 && treantsWood == treants && fireBurnt == fire,
            $"{stacks} stacks of {kinds} breeds on four M maps and an XL; by tier {string.Join(", ", means.Select(m => $"{m.Key}: {m.mean:0} ×{m.n}"))}; passes kept {keptPasses} of {passes / 2} (two hexes each); dragons {dragons} ({dragonsNear} near); treants {treantsWood}/{treants} in woods; fire spirits {fireBurnt}/{fire} on burnt land");

        // ---- meeting a stack: the weak run, the people of the wild ask to join and go with the party, the strong hold
        {
            var c = Campaign.NewWorld(5, 36);
            var cells = c.Cells.Where(x => x.Passable && x.Owner == Owner.None && x.Thing == null && x.Trove == TroveKind.None && !x.Pass).Take(8).ToList();
            var strong = Party(c, cells[0], 12, 8);
            Keep(cells[1], Kinds.Wolf, 3);
            bool ran = c.Meet(strong, cells[1]) && cells[1].Sum.Keep.Count == 0 && c.GuardAt(cells[1].Col, cells[1].Row).strength == 0;
            Keep(cells[2], Kinds.Bandit, 4);
            bool asked = c.Meet(strong, cells[2]) && c.Joiners.Exists(j => j.cell == cells[2]) && cells[2].Sum.Keep.Count == 4;
            int before = strong.Party.Members.Count;
            c.Join(cells[2], take: true);
            bool joined = strong.Party.Members.Count == before + 4 && strong.Party.Members.Count(m => m.Kind == Kinds.OurBandit) == 4 && cells[2].Sum.Keep.Count == 0 && Kinds.OurBandit.Team == Team.Player;
            Keep(cells[3], Kinds.Ogre, 6);
            bool held = !c.Meet(strong, cells[3]) && cells[3].Sum.Keep.Count == 6;
            var weak = Party(c, cells[0], 2, 0);
            Keep(cells[4], Kinds.Bandit, 4);
            bool scorned = !c.Meet(weak, cells[4]) && !c.Joiners.Exists(j => j.cell == cells[4]);
            check("a stack far weaker runs from a party and the way is open; bandits ask to join a strong one and go with it as ours; ogres six strong hold; bandits laugh at two men",
                ran && asked && joined && held && scorned,
                $"wolves ran {ran}; bandits asked {asked}, joined {joined} ({strong.Party.Members.Count} in the party); ogres held {held}; bandits against two {scorned}; said: {c.News.LastOrDefault()?.Text}");

            // a stack beaten is gone, and the hex is open
            Keep(cells[5], Kinds.Basilisk, 1);
            strong.Q = cells[5].Q; strong.R = cells[5].R;
            var r = c.FightHere(strong);
            check("a stack beaten is gone: the hex has no keepers and nobody to fight",
                r != null && r.AttackerWon && cells[5].Sum.Keep.Count == 0 && c.Foes(cells[5]) == 0 && c.GuardAt(cells[5].Col, cells[5].Row).keep.Count == 0,
                $"won {r?.AttackerWon}, keepers left {cells[5].Sum.Keep.Count}, foes {c.Foes(cells[5])}");

            // the horde's party walking into a stack's hex fights it
            Keep(cells[6], Kinds.Ogre, 5);
            var horde = Party(c, cells[7], 18, 0, Owner.Horde, Kinds.Goblin);
            int hordeMen = horde.Party.Members.Count;
            c.Send(horde, cells[6]);
            for (int t = 0; t < 400 && (c.Parties.Contains(horde) && (horde.Q != cells[6].Q || horde.R != cells[6].R) || horde.Moving); t++) c.Advance(30);
            var said = c.News.Where(n => n.Cell == cells[6]).Select(n => n.Text).ToList();
            bool fought = said.Exists(t => t.StartsWith("Бой")) && (cells[6].Sum.Keep.Count < 5 || horde.Party.Members.Count < hordeMen);
            check("the horde's party walking into a stack's hex fights it (an obstacle to the horde too), and the news names the stack",
                fought && said.Exists(t => t.Contains("огр")), $"news there: {string.Join(" | ", said)}; ogres left {cells[6].Sum.Keep.Count}, goblins {horde.Party.Members.Count} of {hordeMen}");
        }

        // ---- in a valley: the powers
        {
            var s = Fresh();
            var man = Put(s, Kinds.Swordsman, 10); man.Hold = true; man.Post = man.Cell;
            var spider = Put(s, Kinds.Spider, 0);
            spider.Target = man; spider.LastHurt = s.Time;
            bool webbed = false; int webs = 0;
            for (int t = 0; t < 120 && !webbed; t++) { s.Step(.05f); webbed = man.SlowUntil > s.Time; }
            for (int z = (int)man.Z - 4; z <= man.Z + 4; z++) for (int x = (int)man.X - 4; x <= man.X + 4; x++) for (int y = s.World.SurfAt(x, z) - 1; y < s.World.SurfAt(x, z) + 2; y++) if (s.World.Get(x, y, z) == Mat.Web) webs++;
            Step(s, 4);
            bool venom = man.VenomUntil > s.Time - 5 && man.Hp < man.MaxHp;
            check("a spider throws a web that holds whoever it catches, slowed, and leaves it on the ground; its bite leaves venom",
                webbed && webs > 0 && venom, $"webbed {webbed}, {webs} web voxels, venom {venom}, hp {man.Hp:0}/{man.MaxHp:0}");
        }
        {
            var s = Fresh();
            var gobs = new List<Unit>();
            for (int i = 0; i < 4; i++) { var g = Put(s, Kinds.Goblin, 12 + i * .6f, i - 2); g.Guard = true; g.Home = g.Cell; gobs.Add(g); }
            var dragon = Put(s, Kinds.Dragon, 0);
            dragon.Target = gobs[0]; dragon.LastHurt = s.Time;
            Step(s, 2.5);
            int burning = gobs.Count(g => g.Burn > 0 || g.Dead), fires = s.Burning.Count, seared = 0;
            for (int z = (int)dragon.Z - 150; z < dragon.Z + 150; z++)
                for (int x = (int)dragon.X - 150; x < dragon.X + 150; x++)
                    if (s.World.InBounds(x, 1, z) && s.World.Get(x, s.World.SurfAt(x, z) - 1, z) == Mat.Scorch) seared++;
            check("a dragon breathes fire: those before it burn, the ground under the cone catches or is seared", burning >= 3 && fires + seared > 10,
                $"{burning} of {gobs.Count} goblins alight or dead, {fires} voxels burning, {seared} seared");
            dragon.Burn = 5; float hp = dragon.Hp;
            Step(s, 1);
            check("fire does not hurt a dragon", dragon.Hp >= hp - 1 && dragon.Burn == 0, $"hp {hp:0} → {dragon.Hp:0}");
        }
        {
            var s = Fresh();
            var man = Put(s, Kinds.Swordsman, 18); man.Hold = true; man.Post = man.Cell;
            var golem = Put(s, Kinds.Golem, 0);
            golem.Target = man; golem.LastHurt = s.Time;
            bool threw = false;
            for (int t = 0; t < 160 && !threw; t++) { s.Step(.05f); threw = s.Fliers.Exists(f => f.Cause == Sim.HurledCause && f.Unit == null) || s.Hurling(golem); }
            check("a stone golem tears up a boulder and hurls it at whoever it is set on", threw, $"threw {threw}");
        }
        {
            var s = Fresh();
            var troll = Put(s, Kinds.WildTroll, 0);
            troll.Hp = 200;
            Step(s, 5);
            float healed = troll.Hp;
            troll.Burn = 3; troll.BurnHeat = 1;
            Step(s, 4);
            float after = troll.Hp;
            Step(s, 4);
            check("a troll of the woods: its wounds close, but not while fire is on it and a while after",
                healed > 220 && after < healed && troll.Hp <= after + 1, $"200 → {healed:0} healed; burnt → {after:0}; after the fire {troll.Hp:0}");
        }
        {
            var s = Fresh();
            var men = new List<Unit>();
            for (int i = 0; i < 3; i++) { var m = Put(s, Kinds.Swordsman, 2.2f, i - 1); m.Hold = true; m.Post = m.Cell; men.Add(m); }
            var ogre = Put(s, Kinds.Ogre, 0);
            ogre.Target = men[1]; ogre.LastHurt = s.Time;
            bool thrown = false;
            for (int t = 0; t < 200 && !thrown; t++) { s.Step(.05f); thrown = men.Exists(m => m.Airborne != null); }
            check("an ogre's blow throws a man off his feet", thrown, $"thrown {thrown}");
            var s2 = Fresh();
            var man = Put(s2, Kinds.Swordsman, 14); man.Hold = true; man.Post = man.Cell;
            var bull = Put(s2, Kinds.Minotaur, 0);
            bull.Target = man; bull.LastHurt = s2.Time;
            bool charged = false, flung = false;
            for (int t = 0; t < 200 && !flung; t++) { s2.Step(.05f); charged |= bull.ChargeUntil > s2.Time; flung = man.Airborne != null; }
            check("a minotaur charges with its head down and its first blow throws", charged && flung, $"charged {charged}, thrown {flung}");
        }
        {
            var s = Fresh();
            var man = Put(s, Kinds.Swordsman, 1.6f); man.Hold = true; man.Post = man.Cell; man.Hp = man.MaxHp = 5000;
            var wraith = Put(s, Kinds.Wraith, 0);
            wraith.Hp = 100; wraith.Target = man; wraith.LastHurt = s.Time;
            // (the man strikes back: what the wraith got back is what it has over what it would have, the blows it took counted)
            float taken = 0, dealt = 0;
            // (its blows as they landed, not the bleeding after them; one taken on the blade hurts a little and gives nothing)
            s.Hurting += (b, dmg, cause) => { if (b == wraith) taken += dmg; else if (b == man && cause == wraith.T.Name) dealt += dmg; };
            Step(s, 4);
            float drank = wraith.Hp - 100 + taken;
            check("a wraith drinks the life it strikes: half of each blow comes back to it", dealt > 20 && drank > dealt * .3f && drank < dealt * .8f,
                $"its blows took {dealt:0}, it drank {drank:0}; wraith 100 → {wraith.Hp:0} with {taken:0} taken");
        }
        {
            var s = Fresh();
            var fs = Put(s, Kinds.FireSpirit, 0);
            var treant = Put(s, Kinds.Treant, 20);
            var ogre = Put(s, Kinds.Ogre, 40);
            float f0 = fs.Hp, t0 = treant.Hp, o0 = ogre.Hp;
            foreach (var u in new[] { fs, treant, ogre }) { u.Burn = 3; u.BurnHeat = 1; }
            Step(s, 2);
            float tl = t0 - treant.Hp + treant.T.Regen * 0, ol = o0 - ogre.Hp;
            check("a fire spirit stands in fire unhurt; a treant burns three times as fast as an ogre", fs.Hp >= f0 - 1 && tl > ol * 2.5f,
                $"fire spirit {f0:0} → {fs.Hp:0}; treant lost {tl:0}, ogre {ol:0}");
        }
        {
            var s = Fresh();
            var gobs = new List<Unit>();
            for (int i = 0; i < 4; i++) { var g = Put(s, Kinds.Orc, 6 + i * .5f, i - 2); g.Guard = true; g.Home = g.Cell; gobs.Add(g); }
            var warg = Put(s, Kinds.Warg, 0);
            var mate = Put(s, Kinds.Warg, -3, 2);
            bool turned = false;
            for (int t = 0; t < 300; t++) { s.Step(.05f); turned |= gobs.Exists(g => g.Target == warg || g.Target == mate); }
            check("a pack of the wild falls on the horde's raiders as on ours, comes together, and the raiders fight back",
                turned && mate.Target != null && (warg.Hp < warg.MaxHp || mate.Hp < mate.MaxHp || warg.Dead || mate.Dead) && gobs.Exists(g => g.Hp < g.MaxHp),
                $"raiders turned {turned}; the second warg set on {mate.Target?.T.Name}; wargs {warg.Hp:0}, {mate.Hp:0}; orcs {string.Join(", ", gobs.Select(g => $"{g.Hp:0}"))}");
        }
    }
}
