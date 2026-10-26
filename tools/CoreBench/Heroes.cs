using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's heroes (--heroes): every commander a hero of level 1 with a name of his own; the experience of raiders
/// struck down about him, twice by his own hand, none for one far off; a level risen offers skills to choose, the same
/// to the same hero, and only an offered one is taken; each skill as it bites — Строй takes off a blow, Стрелки carry
/// further and loose faster, Лекарь mends faster, Тактика strikes a volley harder, Слово reaches further, Зодчий digs
/// faster, Разведка sees a band lying in wait before it rises, Поход walks the map faster and sees further on it; a
/// battle counted at once stronger with a hero and to his credit; a raid beaten off to his credit; a hero fallen lost,
/// his people grieving, a new one hired at level 1; all of it kept in a valley's save and in a campaign's.
/// </summary>
static class HeroesBench
{
    static Sim Fresh()
    {
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var s = new Sim(w, site) { WildTarget = 0 };
        s.SetSource(new MapRaids());
        s.Clock.Left = 1e9;
        return s;
    }

    static void Run(Sim s, double seconds) { for (double t = 0; t < seconds; t += .05) s.Step(.05f); }
    static void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
    static float Apart(Unit a, Unit b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>A level field of turf round a spot, open to the sky.</summary>
    static int Level(Sim s, int cx, int cz, int rx, int rz)
    {
        var w = s.World;
        int fg = w.SurfAt(cx, cz);
        for (int z = cz - rz; z <= cz + rz; z++)
            for (int x = cx - rx; x <= cx + rx; x++)
                for (int y = 2; y < w.SY - 1; y++)
                {
                    byte want = y < fg ? (y == fg - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
        s.Step(.05f);
        return fg;
    }

    /// <summary>One of the horde chained where it stands, a target that does not fight back.</summary>
    static Unit Dummy(Sim s, UnitType t, int x, int z, float hp = 0)
    {
        var u = s.Spawn(t, s.NearestStand(x, z, 2));
        u.Guard = true; u.Home = u.Cell; u.ChainedUntil = 1e9;
        if (hp > 0) u.Hp = u.MaxHp = hp;
        return u;
    }

    static void Hold(Sim s, params Unit[] us) { foreach (var u in us) s.Command(new[] { u }, u.Cell); }

    static void Clear(Hero h) { Array.Clear(h.Grade); h.Unspent = 0; h.Offer.Clear(); }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var s = Fresh();
        foreach (var u in s.Units) if (u.Team == Team.Player) Gone(u);
        var said = new List<string>();
        s.Said += (text, _, _, _) => said.Add(text);
        int kx = s.Site.KeepX, kz = s.Site.KeepZ, fx = kx - 450, fz = kz;
        int fg = Level(s, fx, fz, 150, 400);
        int Stand(int dx, int dz) => s.NearestStand(fx + dx, fz + dz, 2);

        // ---- every commander a hero: level 1, a name of his own, no skills
        var cap = s.Spawn(Kinds.Captain, Stand(0, 0));
        var other = s.Spawn(Kinds.Captain, Stand(-100, -360));
        Hold(s, cap, other);
        // (they wait for orders and stay where they are: nothing takes them off to form up or to carry the hurt)
        cap.Commands = other.Commands = false;
        cap.ChainedUntil = other.ChainedUntil = 1e9;
        check("every commander of ours is a hero: level 1, no experience, no skills, a name of his own, and his name is what he is called",
            cap.Hero != null && other.Hero != null && cap.Hero.Level == 1 && cap.Hero.Xp == 0 && cap.Hero.Grade.All(g => g == 0) && cap.Hero.Name.Length > 3
            && cap.Hero.Name != other.Hero.Name && cap.Name == $"Командир {cap.Hero.Name}",
            $"«{cap.Hero?.Name}» and «{other.Hero?.Name}», level {cap.Hero?.Level}, called «{cap.Name}»");

        // ---- experience: raiders struck down about him, twice by his own hand, none far off
        var swords = Enumerable.Range(0, 3).Select(i => s.Spawn(Kinds.Swordsman, Stand(16 + i * 8, 10))).ToArray();
        Hold(s, swords);
        foreach (var m in swords) m.ChainedUntil = 1e9;
        var near = Enumerable.Range(0, 5).Select(i => Dummy(s, Kinds.Goblin, fx + 40 + i * 6, fz - 20)).ToArray();
        var far = Dummy(s, Kinds.Goblin, fx + 100, fz + 370);
        int worth = Sim.KillXp(near[0]);
        for (int i = 0; i < 4; i++) s.Hurt(near[i], swords[i % 3], 999);
        s.Hurt(near[4], cap, 999);
        int xpNear = cap.Hero.Xp;
        s.Hurt(far, swords[0], 999);
        check("a hero gains for each raider struck down within his word's reach, twice for one by his own hand, nothing for one far off",
            xpNear == 6 * worth && cap.Hero.Xp == xpNear && cap.Hero.Kills == 1 && other.Hero.Xp == 0,
            $"a goblin is worth {worth}; 4 by his men and 1 by him: {xpNear} (want {6 * worth}); one 48 m off: +{cap.Hero.Xp - xpNear}; struck by him {cap.Hero.Kills}; the other commander, 46 m off them: {other.Hero.Xp}");

        // ---- a level risen: skills offered, the same to the same hero; only an offered one taken
        int rose = 0;
        s.HeroRose += _ => rose++;
        said.Clear();
        s.Glory(cap, 100 - cap.Hero.Xp);
        var offer = cap.Hero.Offer.ToList();
        var twin = new Hero { Seed = cap.Hero.Seed, Level = 2, Unspent = 1 };
        Heroes.MakeOffer(twin);
        var notOffered = Enum.GetValues<Skill>().First(k => !offer.Contains(k));
        bool refused = !s.ChooseSkill(cap, notOffered);
        bool took = s.ChooseSkill(cap, offer[0]);
        check("at 100 experience a hero rises to level 2: three skills are offered, the same to the same hero; one not offered is refused, an offered one taken",
            rose == 1 && offer.Count == 3 && offer.Distinct().Count() == 3 && twin.Offer.SequenceEqual(offer) && refused && took
            && cap.Hero[offer[0]] == 1 && cap.Hero.Unspent == 0 && cap.Hero.Offer.Count == 0 && said.Exists(t => t.Contains("достиг уровня 2")),
            $"level {cap.Hero.Level}, offered {string.Join(", ", offer)} (the twin {string.Join(", ", twin.Offer)}); refused {notOffered}: {refused}; took {offer[0]}: {took}");
        // two levels at once: two to choose, one after the other, a skill learnt offered again for its next grade
        s.Glory(cap, 450 - cap.Hero.Xp);
        int waiting = cap.Hero.Unspent;
        var first = cap.Hero.Offer.ToList();
        s.ChooseSkill(cap, first[0]);
        var second = cap.Hero.Offer.ToList();
        check("two levels at once wait to be chosen one after the other; an offer holds a further grade of a skill he has",
            cap.Hero.Level == 4 && waiting == 2 && second.Count == 3 && cap.Hero.Unspent == 1 && first.Exists(k => cap.Hero[k] > 0 || k == first[0]) && second.Exists(k => cap.Hero[k] > 0),
            $"level {cap.Hero.Level}, waiting {waiting}; first offer {string.Join(", ", first)}, then {string.Join(", ", second)}; skills {Heroes.Skills(cap.Hero)}");
        s.ChooseSkill(cap, second[0]);

        // ---- each skill as it bites
        var h = cap.Hero;
        var saved = h.Grade.ToArray();
        // Строй: a blow at one of his men
        float Blow()
        {
            var m = swords[0];
            m.Hp = m.MaxHp; m.WallUntil = 0;
            s.Hurt(m, null, 100);
            float taken = m.MaxHp - m.Hp;
            m.Hp = m.MaxHp; m.Pain = 0; m.Down = false;
            return taken;
        }
        Clear(h);
        float plain = Blow();
        h.Grade[(int)Skill.Line] = 3;
        float lined = Blow();
        check("Строй III: a blow at one of his men takes 18 % less", MathF.Abs(lined / plain - .82f) < .01f, $"a blow of 100: {plain:0.0} without, {lined:0.0} with ({lined / plain:0.000})");

        // Стрелки: an archer about him carries further and looses faster
        var archer = s.Spawn(Kinds.Archer, Stand(-12, -10));
        Hold(s, archer);
        Run(s, .1);
        Clear(h);
        float reach0 = s.ShotRange(archer);
        h.Grade[(int)Skill.Archers] = 2;
        float reach2 = s.ShotRange(archer);
        // the wait after his loosing in a volley, each way
        float Wait(int grade)
        {
            h.Grade[(int)Skill.Archers] = (byte)grade;
            cap.Ready[2] = 0;
            s.Call(cap, 2, archer.X, archer.Y, archer.Z - 15 * VoxelWorld.PerMetre);
            return archer.Cooldown;
        }
        float wait0 = Wait(0), wait3 = Wait(3);
        check("Стрелки: an archer about him carries 16 % further at II, looses again 15 % sooner at III",
            MathF.Abs(reach2 / reach0 - 1.16f) < .01f && wait0 > 0 && MathF.Abs(wait3 / wait0 - .85f) < .01f,
            $"reach {reach0 / VoxelWorld.PerMetre:0.0} → {reach2 / VoxelWorld.PerMetre:0.0} m; the wait to loose again {wait0:0.00} → {wait3:0.00} s");
        Gone(archer);

        // Лекарь: one of his men hurt mends faster
        float Mend(int grade)
        {
            h.Grade[(int)Skill.Healer] = (byte)grade;
            var m = swords[1];
            m.Hp = m.MaxHp * .5f; m.LastHurt = s.Time - 100;
            if (m.Wounds != null) m.Wounds.Bleed = 0;
            float hp0 = m.Hp;
            Run(s, 20);
            return m.Hp - hp0;
        }
        Clear(h);
        float mend0 = Mend(0), mend3 = Mend(3);
        check("Лекарь III: one of his men hurt, out in the field, mends 90 % faster", mend0 > 0 && MathF.Abs(mend3 / mend0 - 1.9f) < .08f,
            $"20 s: +{mend0:0.0} without, +{mend3:0.0} with ({mend3 / mend0:0.00})");

        // Тактика: his volley strikes harder
        var bows = Enumerable.Range(0, 6).Select(i => s.Spawn(Kinds.Archer, Stand(-20 + i * 6, -24))).ToArray();
        Hold(s, bows);
        var target = Dummy(s, Kinds.Orc, fx, fz - 24 - 200, 1e7f);
        Run(s, .2);
        float Volleys(int grade)
        {
            h.Grade[(int)Skill.Tactics] = (byte)grade;
            float sum = 0; int n = 0;
            for (int k = 0; k < 3; k++)
            {
                cap.Ready[2] = 0;
                int a0 = s.Arrows.Count;
                s.Call(cap, 2, target.X, target.Y + 8, target.Z);
                for (int i = a0; i < s.Arrows.Count; i++) { sum += s.Arrows[i].Damage; n++; }
                Run(s, 1);
            }
            return n == 0 ? 0 : sum / n;
        }
        Clear(h);
        float volley0 = Volleys(0), volley2 = Volleys(2);
        check("Тактика II: his «Залп!» strikes 40 % harder", volley0 > 0 && volley2 / volley0 > 1.25f && volley2 / volley0 < 1.55f,
            $"a shaft of the volley: {volley0:0.0} without, {volley2:0.0} with ({volley2 / volley0:0.00})");
        foreach (var b in bows) Gone(b);
        Gone(target);

        // Слово: his word reaches further
        var outer = s.Spawn(Kinds.Swordsman, s.NearestStand((int)cap.X, (int)cap.Z + 43 * VoxelWorld.PerMetre, 2));
        Hold(s, outer);
        Run(s, .1);
        Clear(h);
        float dist = Apart(cap, outer) / VoxelWorld.PerMetre;
        int heed0 = s.Heeding(cap);
        h.Grade[(int)Skill.Word] = 1;
        int heed1 = s.Heeding(cap);
        check("Слово I: his word reaches 46 m, not 40: a man 43 m off heeds it", dist > 41 && dist < 45.5f && heed1 == heed0 + 1,
            $"the man {dist:0.0} m off; heeding {heed0} → {heed1}");
        Gone(outer);

        // Зодчий: a worker about him digs faster
        double Dig(int grade, int dz)
        {
            Clear(h);
            h.Grade[(int)Skill.Builder] = (byte)grade;
            var worker = s.Spawn(Kinds.Worker, Stand(30, dz));
            worker.Pace = 1;
            s.Stock[Stockpile.Stone] = 1000;
            // a low block of stone beside him, to be laid
            var block = new VoxelList();
            for (int z = 0; z < 10; z++) for (int x = 0; x < 10; x++) for (int y = 0; y < 6; y++) block.Add(fx + 40 + x, fg + y, fz + dz + z, Mat.Stone);
            s.DesignateBuild(block);
            double t0 = s.Time;
            for (int i = 0; i < 3000; i++)
            {
                s.Step(.05f);
                bool done = true;
                for (int z = 0; z < 10 && done; z++) for (int x = 0; x < 10 && done; x++) for (int y = 0; y < 6; y++) if (!Mat.IsPlayer[s.World.Get(fx + 40 + x, fg + y, fz + dz + z)]) { done = false; break; }
                if (done) break;
            }
            Gone(worker);
            return s.Time - t0;
        }
        double dig0 = Dig(0, 30), dig3 = Dig(3, 80);
        check("Зодчий III: a worker about him lays a block of stone 30 % faster", dig3 < dig0 * .88 && dig0 < 140,
            $"600 voxels of stone: {dig0:0.0} s without, {dig3:0.0} s with ({dig3 / dig0:0.00})");

        // Поход: his party walks the map faster
        var party = new Party();
        party.Members.Add(Traveller.Of(cap));
        party.Members.Add(Traveller.Of(swords[0]));
        var mp = new MapParty { Party = party };
        Clear(h);
        double pace0 = MapPath.Pace(mp);
        h.Grade[(int)Skill.March] = 2;
        double pace2 = MapPath.Pace(mp);
        check("Поход II: his party takes 20 % less time over a cell of the map", Math.Abs(pace2 / pace0 - .8) < .001, $"pace {pace0:0.00} → {pace2:0.00}");

        // Разведка on the map: his party sees one cell further
        {
            var c = Campaign.New(5);
            var home = c.Home;
            var two = c.Cells.First(x => Campaign.Distance(x.Q, x.R, home.Q, home.R) == 2);
            Clear(h);
            var mp2 = new MapParty { Q = home.Q, R = home.R, Party = party, Owner = Owner.Ours };
            c.Parties.Add(mp2);
            bool seen0 = c.Seen(two);
            h.Grade[(int)Skill.Scouting] = 1;
            bool seen1 = c.Seen(two);
            check("Разведка I: his party on the map sees a valley two cells off, which it does not see without him",
                !seen0 && seen1, $"a valley two cells off seen: {seen0} → {seen1}");
        }

        // ---- a battle counted at once: stronger with a hero, and to his credit
        {
            Clear(h);
            h.Level = 5;
            h.Grade[(int)Skill.Line] = 2; h.Grade[(int)Skill.Tactics] = 2; h.Grade[(int)Skill.Archers] = 1;
            List<Traveller> Side(Hero hero)
            {
                var side = new List<Traveller> { new() { Kind = Kinds.Captain, Hp = Kinds.Captain.Hp, MaxHp = Kinds.Captain.Hp, Hero = hero } };
                for (int i = 0; i < 6; i++) side.Add(new Traveller { Kind = Kinds.Swordsman, Hp = Kinds.Swordsman.Hp, MaxHp = Kinds.Swordsman.Hp });
                for (int i = 0; i < 4; i++) side.Add(new Traveller { Kind = Kinds.Archer, Hp = Kinds.Archer.Hp, MaxHp = Kinds.Archer.Hp });
                return side;
            }
            var foes = Enumerable.Range(0, 12).Select(_ => new Traveller { Kind = Kinds.Goblin, Hp = Kinds.Goblin.Hp, MaxHp = Kinds.Goblin.Hp }).ToList();
            float plainS = QuickBattle.Strength(Side(null), null), heroS = QuickBattle.Strength(Side(h), null);
            float odds0 = QuickBattle.Chance(Side(null), null, foes, null, 0, ValleyKind.Forest), odds1 = QuickBattle.Chance(Side(h), null, foes, null, 0, ValleyKind.Forest);
            int won = 0, alive = 0, gained = 0, fell = 0, fights = 200;
            var rng = new Random(3);
            for (int i = 0; i < fights; i++)
            {
                var one = Heroes.Fresh(i);
                var side = Side(one);
                var them = Enumerable.Range(0, 8).Select(_ => new Traveller { Kind = Kinds.Goblin, Hp = Kinds.Goblin.Hp, MaxHp = Kinds.Goblin.Hp }).ToList();
                var r = QuickBattle.Fight(side, null, them, null, 0, ValleyKind.Forest, rng.NextDouble);
                bool stands = side.Exists(m => m.Hero == one);
                if (r.AttackerWon) { won++; if (stands) { alive++; if (one.Xp == Heroes.WonXp + Heroes.FoeXp * r.DefenderFell.Count) gained++; } }
                if (!stands && r.HeroesFell.Contains(one.Name)) fell++;
            }
            check("a battle counted at once: a hero of level 5 with Строй II, Тактика II and Стрелки I makes his side 19 % stronger and its odds better; won, he gains 25 and 5 for each foe fallen",
                MathF.Abs(heroS / plainS - 1.31f / 1.1f) < .01f && odds1 > odds0 && alive > 0 && gained == alive,
                $"strength {plainS:0} → {heroS:0} ({heroS / plainS:0.000}); odds against 12 goblins {odds0:0.00} → {odds1:0.00}; " +
                $"of {fights} fights won {won}, the hero standing after {alive} and credited rightly in {gained}; fell and told {fell}");
            h.Level = 4;
        }
        Array.Copy(saved, h.Grade, saved.Length);

        // ---- a raid beaten off to the credit of the heroes who stood against it
        {
            var s2 = Fresh();
            foreach (var u in s2.Units) if (u.Team == Team.Player) Gone(u);
            var c2 = s2.Spawn(Kinds.Captain, s2.NearestStand(s2.Site.KeepX - 60, s2.Site.KeepZ, 2));
            var men = Enumerable.Range(0, 8).Select(i => s2.Spawn(i < 5 ? Kinds.Swordsman : Kinds.Archer, s2.NearestStand(s2.Site.KeepX - 50 + i * 5, s2.Site.KeepZ + 20, 2))).ToArray();
            s2.Raid(Enumerable.Repeat(Kinds.Goblin, 5).ToList(), 4, .5);
            for (int i = 0; i < 600 && (s2.Raids.Active || s2.LastBattle == null); i++) Run(s2, .5);
            var report = s2.LastBattle;
            check("a raid beaten off: the hero who stood his people against it gains 30 and 10 for each step of its threat, besides those he and his struck down",
                report is { Held: true } && s2.HeroHeldXp == 30 + (int)Math.Round(10 * report.Threat) && c2.Hero.Xp > s2.HeroHeldXp,
                $"report {(report == null ? "none" : report.Held ? "held" : "fell")}, threat {report?.Threat:0.0}; for the raid {s2.HeroHeldXp}, all {c2.Hero.Xp}; level {c2.Hero.Level}");

            // ---- Разведка in the valley: a band lying in wait seen before it rises, nobody caught off guard
            foreach (var u in s2.Units) if (u.Team == Team.Player) Gone(u);
            var lord = s2.NewWarlord(2, Kinds.Goblin);
            s2.Raid(new List<UnitType> { Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Orc, Kinds.Orc, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Orc }, 4, .5, lord);
            var lurk = s2.Bands.FirstOrDefault(b => b.Lurk);
            for (int i = 0; i < 400 && lurk != null && !(lurk.State == BandState.Lurk && lurk.Members.Count(u => u.Lurking) >= 3); i++)
            {
                Run(s2, .5);
                foreach (var u in s2.Units) if (!u.Dead && u.Team == Team.Horde && u.Band != lurk && u.Band?.State is BandState.March or BandState.Rally or BandState.Storm) u.ChainedUntil = 1e9;
            }
            bool laid = lurk is { State: BandState.Lurk } && lurk.Members.Count(u => u.Lurking) >= 3;
            var said2 = new List<string>();
            s2.Said += (text, _, _, _) => said2.Add(text);
            if (laid)
            {
                var (sx, _, sz) = s2.Nav.Feet(lurk.LurkCell);
                float ox = sx - s2.Site.KeepX, oz = sz - s2.Site.KeepZ, ol = MathF.Max(1, MathF.Sqrt(ox * ox + oz * oz));
                var scout = s2.Spawn(Kinds.Captain, s2.NearestStand((int)(sx + ox / ol * 22 * VoxelWorld.PerMetre), (int)(sz + oz / ol * 22 * VoxelWorld.PerMetre), 2));
                s2.Command(new[] { scout }, scout.Cell);
                scout.ChainedUntil = 1e9;
                // a few seconds as he is, then with Разведка II (30 m)
                Run(s2, 3);
                bool waitedPlain = lurk.State == BandState.Lurk && s2.AmbushesSpotted == 0;
                scout.Hero.Grade[(int)Skill.Scouting] = 2;
                float pain0 = scout.Pain;
                Run(s2, 2);
                float off = lurk.Members.Where(u => !u.Dead).Min(u => Apart(u, scout)) / VoxelWorld.PerMetre;
                check("Разведка II: a hero 22 m off a band lying in wait sees it: it rises then, with nobody caught off guard and no «Засада!»; without it he does not",
                    waitedPlain && s2.AmbushesSpotted == 1 && s2.AmbushesSprung == 0 && lurk.State == BandState.Storm && lurk.Members.All(u => !u.Lurking)
                    && scout.Pain <= pain0 + .01f && said2.Exists(t => t.Contains("заметил засаду")) && !said2.Exists(t => t.StartsWith("Засада!")),
                    $"waited while he saw nothing: {waitedPlain}; spotted {s2.AmbushesSpotted}, sprung {s2.AmbushesSprung}; band {lurk.State}; the nearest of it {off:0} m off");
            }
            else check("Разведка II: a hero 22 m off a band lying in wait sees it", false, $"no band lay in wait: {lurk?.State}");
        }

        // ---- a hero fallen: lost with his skills, his men grieving; a new one hired at level 1
        {
            var ward = s.Spawn(Kinds.Swordsman, s.NearestStand((int)other.X + 16, (int)other.Z, 2));
            Hold(s, ward);
            ward.Ward = other;
            other.Hero.Grade[(int)Skill.Line] = 2;
            said.Clear();
            string name = other.Hero.Name;
            s.Hurt(other, null, 1e6f);
            Run(s, .1);
            var fresh = s.Spawn(Kinds.Captain, Stand(0, 60));
            check("a hero fallen is lost with his skills (the chronicle names them), his men strike weaker a minute; a new commander is a new hero of level 1",
                other.Dead && !s.HeroesHere().Contains(other) && said.Exists(t => t.Contains($"Командир {name} пал") && t.Contains("Строй II")) && ward.GriefUntil > s.Time + 50
                && said.Exists(t => t.Contains("пали духом")) && fresh.Hero.Level == 1 && fresh.Hero.Xp == 0 && fresh.Hero.Name != cap.Hero.Name,
                $"fallen {other.Dead}; said «{said.FirstOrDefault(t => t.Contains("пал")) ?? "-"}»; his man grieves {ward.GriefUntil - s.Time:0} s more; the new one «{fresh.Hero.Name}», level {fresh.Hero.Level}");
            Gone(fresh); Gone(ward);
        }

        // ---- kept in a valley's save
        {
            var mp0 = new MapParams();
            Sim back;
            s.Glory(cap, Heroes.XpFor(5) - cap.Hero.Xp);
            var ms2 = new MemoryStream();
            s.Save(ms2, mp0.Seed, "test", "");
            ms2.Position = 0;
            var w3 = new VoxelWorld(mp0.SizeX, mp0.SizeY, mp0.SizeZ);
            using (var reader = new SaveReader(ms2))
            {
                var site3 = MapGen.Generate(w3, new MapParams { Seed = reader.Seed });
                w3.Compact();
                reader.Valley(w3, site3);
                back = new Sim(w3, site3);
                back.Load(reader);
            }
            var hb = back.Units.Find(u => u.Id == cap.Id)?.Hero;
            var ha = cap.Hero;
            check("a hero is kept in a valley's save: his name, level, experience, skills, the levels waiting and the skills offered, those he struck down",
                hb != null && hb.Name == ha.Name && hb.Level == ha.Level && hb.Xp == ha.Xp && hb.Grade.SequenceEqual(ha.Grade) && hb.Unspent == ha.Unspent && ha.Unspent > 0
                && hb.Offer.SequenceEqual(ha.Offer) && hb.Kills == ha.Kills && hb.Seed == ha.Seed,
                hb == null ? "not read back" : $"«{hb.Name}» level {hb.Level} ({ha.Level}), xp {hb.Xp}, skills {Heroes.Skills(hb)} ({Heroes.Skills(ha)}), waiting {hb.Unspent}, offered {string.Join(",", hb.Offer)}");
        }

        // ---- kept in a campaign's: a party's hero on the map, and one going into a valley
        {
            var c = Campaign.New(9);
            var p = new Party();
            p.Members.Add(Traveller.Of(cap));
            p.Members.Add(Traveller.Of(swords[2]));
            c.Out(p, null);
            string dir = Path.Combine(Path.GetTempPath(), $"vt-heroes-{Environment.ProcessId}");
            c.Write(dir);
            var c2 = Campaign.Read(dir);
            Directory.Delete(dir, true);
            var hb = c2.Parties.FirstOrDefault()?.Party.Commander?.Hero;
            var ha = cap.Hero;
            // into a valley: the commander come in is the same hero
            var come = s.Arrive(c2.Parties[0].Party, 2);
            var cin = come.FirstOrDefault(u => u.T == Kinds.Captain);
            check("a hero goes with his party onto the map, is kept in the campaign's file and comes into another valley the same hero",
                hb != null && hb.Name == ha.Name && hb.Level == ha.Level && hb.Xp == ha.Xp && hb.Grade.SequenceEqual(ha.Grade) && hb.Offer.SequenceEqual(ha.Offer)
                && cin?.Hero != null && cin.Hero.Name == ha.Name && cin.Hero.Level == ha.Level,
                hb == null ? "not read back" : $"«{hb.Name}» level {hb.Level}, skills {Heroes.Skills(hb)}; come in as «{cin?.Hero?.Name}», level {cin?.Hero?.Level}");
        }
    }
}
