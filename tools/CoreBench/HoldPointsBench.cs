using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's points held (--holdpoints, Campaign.Hold.cs, Fortification.cs): the works about a point measured from the
/// voxels — nothing on bare ground, a ring of wall with towers, a gate and a ditch worth most of it, a valley left with
/// ours in it held by them with its works; a siege counted behind them — six behind good works hold against three and
/// four times their strength most times, in the open they fall, rams and a hill giant bring them down sooner; the
/// winners lose too, an even fight half, three to one a tenth; on the map a flagged mine with nobody to hold it, or a
/// point of ours with only workers in it, taken without a battle, its garrison and a party of ours standing there
/// fighting one battle together, the game's battle stopping the map till it is over; a mine's stone to its own
/// valley's stores; the player of the harness holding the mines it flags.
/// </summary>
static class HoldPointsBench
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

    /// <summary>A level field of turf round a spot, open to the sky; its ground's top.</summary>
    static int Level(Sim s, int cx, int cz, int r)
    {
        var w = s.World;
        int fg = w.SurfAt(cx, cz);
        for (int z = cz - r; z <= cz + r; z++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                for (int y = 2; y < w.SY - 1; y++)
                {
                    byte want = y < fg ? (y == fg - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
                if (s.Site.HTop != null) s.Site.HTop[z * w.SX + x] = fg;
            }
        s.Step(.05f);
        return fg;
    }

    /// <summary>A square of stone wall round a point (its half side, thickness 5 and height in voxels), towers at its corners, a ditch outside it.</summary>
    static void Works(Sim s, int px, int pz, int g, int radius, int high, bool towers, bool ditch)
    {
        var w = s.World;
        for (int z = pz - radius - 20; z <= pz + radius + 20; z++)
            for (int x = px - radius - 20; x <= px + radius + 20; x++)
            {
                int d = Math.Max(Math.Abs(x - px), Math.Abs(z - pz));
                if (d >= radius - 2 && d <= radius + 2) for (int y = g; y < g + high; y++) w.Set(x, y, z, Mat.Stone);
                if (ditch && d >= radius + 6 && d <= radius + 14) for (int y = g - 14; y < g; y++) w.Set(x, y, z, Mat.Air);
            }
        if (towers)
            foreach (var (tx, tz) in new[] { (px + radius, pz + radius), (px - radius, pz + radius), (px + radius, pz - radius), (px - radius, pz - radius) })
                for (int z = tz - 9; z <= tz + 9; z++)
                    for (int x = tx - 9; x <= tx + 9; x++)
                        if ((x - tx) * (x - tx) + (z - tz) * (z - tz) <= 81)
                            for (int y = g; y < g + high + 24; y++) w.Set(x, y, z, Mat.Stone);
        s.Step(.05f);
    }

    static Traveller Man(UnitType t) => new() { Kind = t, Hp = t.Hp, MaxHp = t.Hp };
    static List<Traveller> Side(params (UnitType t, int n)[] kinds)
    {
        var l = new List<Traveller>();
        foreach (var (t, n) in kinds) for (int i = 0; i < n; i++) l.Add(Man(t));
        return l;
    }
    static List<Traveller> Garrison6() => Side((Kinds.Archer, 2), (Kinds.Spearman, 2), (Kinds.Swordsman, 2));

    /// <summary>A horde of goblins and orcs about so many times a side's strength in the open.</summary>
    static List<Traveller> Horde(float times, List<Traveller> against)
    {
        float want = QuickBattle.Strength(against, null) * times;
        var h = new List<Traveller>();
        for (int i = 0; QuickBattle.Strength(h, null) < want && i < 400; i++) h.Add(Man(i % 3 == 2 ? Kinds.Orc : Kinds.Goblin));
        return h;
    }

    /// <summary>How often the defenders win so many fights, each fought anew.</summary>
    static float Holds(Func<List<Traveller>> att, Func<List<Traveller>> def, float walls, int fights, int seed)
    {
        var rnd = new Random(seed);
        int held = 0;
        for (int i = 0; i < fights; i++) if (!QuickBattle.Fight(att(), null, def(), null, walls, ValleyKind.First, rnd.NextDouble).AttackerWon) held++;
        return held / (float)fights;
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        // ---- the works measured from the voxels
        {
            var s = Fresh();
            int px = s.Site.KeepX - 330, pz = s.Site.KeepZ;
            int g = Level(s, px, pz, 150);
            var bare = Fortification.Measure(s, px, pz);
            Works(s, px, pz, g, 80, 26, towers: true, ditch: true);
            var plan = GatePlan.At(s.World, s.Site, new VoxelHit { X = px + 82, Y = g + 6, Z = pz + 30, NX = 1, M = s.World.Get(px + 82, g + 6, pz + 30) });
            int gate = plan != null ? s.Gates.Count : -1;
            if (plan != null) s.AddGate(plan, built: true);
            var ring = Fortification.Measure(s, px, pz);
            check("the works about a point measured from the voxels: nothing on bare ground; a ring of wall 3 m high with towers, a gate and a ditch worth the most of it",
                bare.Score < .05f && ring.Ring >= .9f && ring.High >= 2.9f && ring.High <= 3.6f && ring.Towers >= 2 && ring.Gates == 1 && ring.Ditch >= .8f && ring.Score >= 4f,
                $"bare {bare.Score:0.00}; the ring {ring.Ring:0.00} of the way round, {ring.High:0.0} m, towers {ring.Towers}, gates {ring.Gates} (planned {gate >= 0}), ditch {ring.Ditch:0.00}, worth {ring.Score:0.00} — «{ring.Words()}»");

            // a valley nobody holds left with ours standing in the ring: theirs, measured (the point where they stand: no citadel in it)
            s.Site.Holding = Holding.Outpost;
            var c = Campaign.NewWorld(5, 36);
            var cell = c.Cells.First(x => !x.Home && x.Passable && x.Owner == Owner.None && x.Thing == null);
            foreach (var t in new[] { Kinds.Archer, Kinds.Archer, Kinds.Spearman, Kinds.Swordsman, Kinds.Worker })
                s.Spawn(t, s.NearestStand(px + s.Rng.Next(-30, 31), pz + s.Rng.Next(-30, 31), 2));
            c.Summarise(cell, s);
            check("a valley nobody holds, left with ours in it and no enemy, is ours: its garrison those left, its works measured about where they stand",
                cell.Owner == Owner.Ours && cell.Sum.Keep.Count == 5 && cell.Sum.Walls >= 3.5f && c.Garrisoned(cell),
                $"owner {cell.Owner}, garrison {cell.Sum.Keep.Count}, works {cell.Sum.Walls:0.00} «{cell.Sum.Works.Words()}»");
        }

        // ---- the siege counted
        {
            var walls = 4.2f;
            float three = Holds(() => Horde(3, Garrison6()), Garrison6, walls, 400, 1),
                four = Holds(() => Horde(4, Garrison6()), Garrison6, walls, 400, 2),
                open = Holds(() => Horde(3, Garrison6()), Garrison6, 0, 400, 3),
                rams = Holds(() => { var h = Horde(3, Garrison6()); h.Add(Man(Kinds.Ram)); h.Add(Man(Kinds.Ram)); h.Add(Man(Kinds.HillGiant)); return h; }, Garrison6, walls, 400, 4);
            check("six behind good works hold against three times their strength most times, against four more often than not; in the open they fall",
                three >= .65f && four >= .5f && open <= .1f, $"held: ×3 {three:P0}, ×4 {four:P0}, ×3 in the open {open:P0}");
            check("rams and a hill giant batter the works down first: the same six fall the sooner", rams <= three - .25f, $"held ×3 with two rams and a hill giant {rams:P0} (without {three:P0})");
        }

        // ---- the winners lose too
        {
            var rnd = new Random(7);
            double LostShare(float times, int n)
            {
                double lost = 0; int won = 0;
                for (int i = 0; i < n; i++)
                {
                    var def = Garrison6(); def.AddRange(Garrison6());
                    var att = Horde(times, def);
                    int a0 = att.Count, d0 = def.Count;
                    var r = QuickBattle.Fight(att, null, def, null, 0, ValleyKind.First, rnd.NextDouble);
                    var (w, w0) = r.AttackerWon ? (att, a0) : (def, d0);
                    lost += 1 - w.Count / (double)w0; won++;
                }
                return lost / won;
            }
            double even = LostShare(1, 300), threeOne = LostShare(3, 300);
            check("the winners lose too: of an even fight about half, of three to one about a tenth", even >= .3 && even <= .6 && threeOne >= .03 && threeOne <= .15,
                $"lost by the winners: even {even:P0}, three to one {threeOne:P0}");
        }

        // ---- on the map
        {
            var c = Campaign.NewWorld(9, 36);
            var home = c.Home;
            MapCell Mine(Func<MapCell, bool> more) => c.Cells.Where(x => x.Thing is { Kind: ThingKind.Mine } && x.Passable && x.Sum.Keep.Count == 0 && !x.Home && more(x))
                .OrderBy(x => Campaign.Distance(x.Q, x.R, home.Q, home.R)).First();
            MapParty Raiders(MapCell at, int goblins, int orcs)
            {
                var near = Enumerable.Range(0, 6).Select(s => c.Neighbour(at, s)).First(n => n != null && n.Passable && n.Owner == Owner.None && n.Sum.Keep.Count == 0);
                var p = new MapParty { Id = 9000 + c.Parties.Count, Owner = Owner.Horde, Party = new Party(), Q = near.Q, R = near.R, CameQ = near.Q, CameR = near.R, Aim = PartyAim.Strike };
                for (int i = 0; i < goblins; i++) p.Party.Members.Add(Man(Kinds.Goblin));
                for (int i = 0; i < orcs; i++) p.Party.Members.Add(Man(Kinds.Orc));
                c.Parties.Add(p);
                c.Send(p, at);
                return p;
            }
            void Until(Func<bool> done) { for (int i = 0; i < 400 && !done(); i++) c.Advance(5); }

            // a mine flagged with nobody to hold it: taken by a small party, no battle
            var bare = Mine(_ => true);
            bare.Thing.Owner = Owner.Ours;
            var small = Raiders(bare, 3, 0);
            Until(() => bare.Thing.Owner == Owner.Horde || !c.Parties.Contains(small) && !small.Moving);
            bool noFight = !c.News.Exists(n => n.Kind == NewsKind.Fought && n.Cell == bare);
            check("a mine flagged with nobody to hold it is taken by a small party of the horde, without a battle", bare.Thing.Owner == Owner.Horde && bare.Owner == Owner.Horde && noFight,
                $"{bare.Thing.Name}: flag {bare.Thing.Owner}, the cell {bare.Owner}, fought {!noFight}");
            c.News.Clear();

            // a point of ours with only workers in it: the same
            var hands = Mine(x => x != bare);
            hands.Owner = Owner.Ours; hands.Thing.Owner = Owner.Ours;
            hands.Sum.Keep.AddRange(Side((Kinds.Worker, 3)));
            Raiders(hands, 3, 0);
            Until(() => hands.Owner != Owner.Ours);
            check("a point of ours with only workers in it is taken without a battle, the workers running", hands.Owner == Owner.Horde && c.News.Exists(n => n.Kind == NewsKind.Lost && n.Text.Contains("без боя")) && hands.Sum.Keep.All(m => m.Kind != Kinds.Worker),
                $"the cell {hands.Owner}, said «{c.News.FindLast(n => n.Cell == hands)?.Text}»");
            c.News.Clear();

            // a garrison and a party of ours standing there fight one battle together
            var held = Mine(x => x != bare && x != hands);
            held.Owner = Owner.Ours; held.Thing.Owner = Owner.Ours;
            held.Sum.Keep.AddRange(Garrison6());
            held.Sum.Walls = 2;
            var ours = new MapParty { Id = 8000, Owner = Owner.Ours, Party = new Party(), Q = held.Q, R = held.R, CameQ = held.Q, CameR = held.R };
            ours.Party.Members.AddRange(Side((Kinds.Swordsman, 4), (Kinds.Archer, 2)));
            c.Parties.Add(ours);
            int keep0 = held.Sum.Keep.Count, party0 = ours.Party.Members.Count;
            var big = Raiders(held, 30, 10);
            Until(() => !c.Parties.Contains(big) || c.News.Exists(n => n.Cell == held && n.Text.StartsWith("Орда")));
            var said = c.News.FindAll(n => n.Cell == held && n.Text.StartsWith("Орда"));
            check("counted where nobody plays, the garrison of a point and a party of ours standing there fight one battle together, behind its works",
                said.Count == 1 && !c.News.Exists(n => n.Text.Contains("наш отряд")) && (held.Sum.Keep.Count < keep0 || ours.Party.Members.Count < party0 || !c.Parties.Contains(ours)),
                $"{said.Count} battles said: «{said.FirstOrDefault()?.Text}»; the garrison {keep0} → {held.Sum.Keep.Count}, the party {party0} → {(c.Parties.Contains(ours) ? ours.Party.Members.Count : 0)}");
            c.News.Clear();

            // played: the map stops for the battle, the game takes it into the valley
            var fought = Mine(x => x != bare && x != hands && x != held);
            fought.Owner = Owner.Ours; fought.Thing.Owner = Owner.Ours;
            fought.Sum.Keep.AddRange(Garrison6());
            c.Interactive = true;
            var come = Raiders(fought, 6, 2);
            Until(() => c.Frozen != null);
            var clock = c.Clock;
            var others = c.Parties.Where(p => p != come && p.Moving).Select(p => (p, p.Q, p.R, p.Progress)).ToList();
            for (int i = 0; i < 20; i++) c.Advance(5);
            bool still = c.Clock.Equals(clock) && others.All(o => o.p.Q == o.Q && o.p.R == o.R && o.p.Progress == o.Progress);
            bool took = c.TakeDefence(out var enemy, out var at, out int side);
            c.DefenceOver(at, held: true, 1, 6);
            c.Advance(5);
            check("played, a party of the horde at a garrisoned point of ours stops the map — no clock, no party walks — till the game has fought it in the valley; then it runs on",
                at == fought && still && took && enemy == come && side >= 0 && !c.Parties.Contains(come) && c.Frozen == null && !c.Clock.Equals(clock),
                $"frozen at {(at == fought ? "the point" : "?")}, the map stood {still}, taken {took} by side {side}, after: frozen {c.Frozen != null}, clock {clock.Day}/{clock.At:0} → {c.Clock.Day}/{c.Clock.At:0}");
            c.Interactive = false;

            // a mine's stone to its own valley's stores while ours hold it
            double stone0 = fought.Sum.Stone, home0 = home.Sum.Stone;
            c.Give(Res.Stone, 4, fought);
            check("a mine's stone goes to the stores of its own valley while ours hold it (to build its works with)", fought.Sum.Stone - stone0 == 4 && home.Sum.Stone == home0,
                $"the point +{fought.Sum.Stone - stone0}, home +{home.Sum.Stone - home0}");

            // written and read back: the garrison, its works, a battle waiting
            fought.Sum.Walls = 2.5f;
            c.Interactive = true;
            Raiders(fought, 6, 2);
            Until(() => c.Frozen != null);
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vt-holdpoints");
            if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            c.Write(dir);
            var read = Campaign.Read(dir);
            var rf = read.Cell(fought.Col, fought.Row);
            check("a campaign written and read back keeps the garrison of a point, its works and the battle waiting for it",
                rf.Owner == Owner.Ours && rf.Sum.Keep.Count == fought.Sum.Keep.Count && Math.Abs(rf.Sum.Walls - 2.5f) < .01f && read.Frozen == rf && read.DefenceWaits,
                $"owner {rf.Owner}, garrison {rf.Sum.Keep.Count}, works {rf.Sum.Walls:0.00}, frozen {read.Frozen == rf}, waiting {read.DefenceWaits}");
        }

        // ---- the harness's player holds the mines it flags and raises works there, at some time of a game, in one game of
        // three at the least (a game turns on its fights: a hero lost to the wild early leaves none to hold anything)
        {
            var games = new List<string>();
            bool held = false;
            foreach (uint seed in new uint[] { 7, 3, 12 })
            {
                var c = Campaign.NewWorld(seed, 36);
                c.AddHero();
                var ap = new AutoPlayer(c);
                ap.Begin();
                // (the most held and the best works at any dawn of the game, not on its last: a point may fall later)
                int heldMines = 0, dawn = c.Clock.Day;
                float works = 0;
                while (c.Clock.Day <= 30 && !c.Won && !c.Defeated)
                {
                    c.Advance(5); ap.Tick(); c.News.Clear();
                    if (c.Clock.Day == dawn) continue;
                    dawn = c.Clock.Day;
                    heldMines = Math.Max(heldMines, c.Cells.Count(x => x.Owner == Owner.Ours && !x.Home && x.Thing is { Kind: ThingKind.Mine } && c.Garrisoned(x)));
                    works = Math.Max(works, c.Cells.Where(x => x.Owner == Owner.Ours && !x.Home).Select(x => x.Sum.Walls).DefaultIfEmpty(0).Max());
                }
                // (the works risen at all: a step a day for its workers, AutoPlayer.FortDawn)
                held |= ap.Garrisons >= 1 && heldMines >= 1 && works >= AutoPlayer.FortStep;
                games.Add($"seed {seed}: garrisons left {ap.Garrisons}, mines held at most {heldMines}, the best works {works:0.00}, to day {c.Clock.Day}");
            }
            check("the harness's player leaves garrisons at the mines it flags, and their workers raise works (in one game of three at the least)", held, string.Join("; ", games));
        }
    }
}
