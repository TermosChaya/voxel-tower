using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

public sealed partial class MapParty
{
    /// <summary>When a hero of the horde next looks at what to do, the map's seconds since the start (MapAI.Heroes.cs).</summary>
    public double ThinkAt;
    /// <summary>What a hero of the horde is about, as the harness tells it.</summary>
    public string Errand = "";
}

/// <summary>
/// The horde on the big map as a player of «Heroes» (docs/handoff/13-world-map.md, 2.18). It has heroes as we do: each
/// a warlord with a hero's record (Heroes.cs — a name, levels to 10, experience, skills), leading his army over the hexes
/// with a day's moves (MapWalk.cs), one standing in each of its towns at the start, more hired at a town while the
/// treasury allows (one a town, one more with its war camp). When a hero stands with nothing to do he looks over what
/// lies within two days' walk (six if nothing is near): piles, campfires, chests, mills, stones of learning, mines nobody
/// holds or ours, the beasts and the dead that keep a pass when he would beat them well, our valleys, our heroes —
/// each worth so much for the days it takes, the dearer the fight the less; and goes for the best. He goes round a stack
/// of the map's keepers he would not beat four times in five, and through those he would. At a town he hires what waits
/// in it, and two heroes meeting there put their armies together. Our heroes coming at a town of the horde its nearest
/// hero comes back to stand by it. Against ours it does nothing while the peace lasts at the start (a week, by the
/// difficulty), and then sets out against the first valley only with a real army (<see cref="LeastArmy"/>) that would win
/// as it wants (our heroes near it counted in), and no oftener than every few days (<see cref="MarchGap"/>). Its heroes
/// gain by battles, stones and chests, rise and choose their skills by a rule, fall, and are hired anew.
/// </summary>
public sealed partial class Campaign
{
    /// <summary>What a hero of the horde costs to hire.</summary>
    public const int HordeHeroCost = 500;
    /// <summary>How often a hero of the horde standing idle looks again at what to do, the map's seconds: two hours.</summary>
    const double ThinkEvery = 2 * MapPath.HourSeconds;
    /// <summary>A stack of the map's keepers a hero of the horde would beat less often than this he goes round.</summary>
    public const float AvoidOdds = .8f;
    /// <summary>The chance a hero of the horde wants before he falls on the map's keepers.</summary>
    public const float NeutralOdds = .85f;
    /// <summary>The most men a hero of the horde leads.</summary>
    public const int ArmyMost = 90;
    /// <summary>How near our heroes come to a town of the horde before its nearest hero comes back to stand by it, hexes.</summary>
    const int GuardNear = 6;

    /// <summary>The peace at the start is over: the horde may strike at ours.</summary>
    public bool AtWar => Clock.Day > PeaceDays;

    /// <summary>The horde's realm begun on the big map: its treasury, its towns, a hero standing in each of its towns.</summary>
    void BeginRealm()
    {
        var s = Realm.Stock;
        s[(int)Res.Gold] = StartGold * HordeIncome; s[(int)Res.Logs] = StartLogs; s[(int)Res.Stone] = StartStone; s[(int)Res.Metal] = StartMetal;
        foreach (var c in Lairs)
        {
            var t = TownOf(c);
            if (!t.Lair) NewHordeHero(c, 1);
        }
    }

    /// <summary>A hero of the horde hired at a town: a warlord with a hero's record, his first army about him.</summary>
    MapParty NewHordeHero(MapCell town, int level)
    {
        var lord = NewLord(1);
        // (the four kinds of warlord by turns: each has his own gift in a valley)
        lord.Kind = new[] { Kinds.Orc, Kinds.Goblin, Kinds.Troll, Kinds.Shaman }[lord.Id % 4];
        lord.Hero = new Hero { Name = lord.Name, Seed = (int)(Seed % 100003) * 31 + lord.Id * 7919 };
        if (level > 1) Heroes.Gain(lord.Hero, Heroes.XpFor(level));
        Spend(lord);
        var p = new MapParty
        {
            Id = _nextParty++, Owner = Owner.Horde, Party = new Party(), Q = town.Q, R = town.R, CameQ = town.Q, CameR = town.R,
            Lord = lord, Den = KeyOf(town), Aim = PartyAim.None, ThinkAt = Clock.Total,
        };
        foreach (var k in new[] { Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Orc, Kinds.Orc })
            p.Party.Members.Add(Raider(k));
        p.MovesMax = p.MovesLeft = MapWalk.MaxMoves(p, Week);
        if (Land != null) (p.AtX, p.AtZ) = Land.Center(town);
        Parties.Add(p);
        Realm.Hired++;
        return p;
    }

    /// <summary>A warlord-hero's skills chosen as they come, by a rule: the march, tactics, the line, the word, the healer, building, the bows, scouting; his warlord's level after his.</summary>
    static void Spend(Warlord lord)
    {
        var h = lord.Hero;
        while (h.Pending)
        {
            Skill pick = h.Offer[0];
            foreach (var s in new[] { Skill.March, Skill.Tactics, Skill.Line, Skill.Word, Skill.Healer, Skill.Builder, Skill.Archers, Skill.Scouting })
                if (h.Offer.Contains(s)) { pick = s; break; }
            Heroes.Choose(h, pick);
        }
        // a warlord's level in a valley (1 to 5) from his hero's (1 to 10), a level more for his fighting skills
        int fight = h[Skill.Line] + h[Skill.Tactics] + h[Skill.Word];
        lord.Level = Math.Clamp(1 + (h.Level - 1) / 2 + (fight >= 3 ? 1 : 0), 1, Warlord.MaxLevel);
    }

    /// <summary>
    /// A battle counted at once over, for the hero of the horde in it: what he gained by it (as ours do: a battle won and
    /// each of the beaten fallen; a battle lost a little), his skills chosen; fallen, he is lost to the horde.
    /// </summary>
    void HordeGlory(MapParty p, QuickResult r, bool attacker)
    {
        if (r == null || p?.Lord?.Hero is not { } h) return;
        bool won = attacker == r.AttackerWon;
        int theirs = attacker ? r.DefenderFell.Count : r.AttackerFell.Count;
        bool fell = attacker ? r.AttackerLordFell : r.DefenderLordFell;
        if (fell)
        {
            Realm.Fell++;
            Tell(NewsKind.Fought, $"Герой орды {h.Name} пал.", CellOf(p), p);
            return;
        }
        int xp = won ? Heroes.WonXp + Heroes.FoeXp * theirs : Heroes.FoeXp * theirs / 2;
        if (Heroes.Gain(h, xp) > 0) Spend(p.Lord);
    }

    /// <summary>
    /// The horde's dawn on the big map: its towns earn, breed, build and keep their garrisons (MapAI.Towns.cs); its
    /// heroes mend (in a town as a lair mends, abroad by their Лекарь), and another is hired while there is room and gold.
    /// </summary>
    void HordeDawnBig()
    {
        var towns = Lairs;
        if (towns.Count == 0) return;
        TownsDawn(towns);
        foreach (var p in Parties)
        {
            if (p.Owner != Owner.Horde || p.Lord?.Hero == null) continue;
            var at = CellOf(p);
            bool home = at != null && at.Holding == Holding.HordeTown && at.Owner == Owner.Horde;
            float share = home ? LairMend : Heroes.MarchMend * p.Lord.Hero[Skill.Healer];
            if (share > 0) foreach (var m in p.Party.Members) Heal(m, share);
        }
        HireHeroes(towns);
    }

    /// <summary>Another hero hired at the town with most waiting in it, while the horde has fewer than its towns allow and the gold.</summary>
    void HireHeroes(List<MapCell> towns)
    {
        int heroes = 0, most = 0;
        foreach (var _ in HeroesOf(Owner.Horde)) heroes++;
        MapCell best = null; int bestPool = -1;
        foreach (var c in towns)
        {
            var t = TownOf(c);
            if (t.Lair) continue;
            most += Math.Min(HeroesPerTown, 1 + (t.Has(HordeWork.WarCamp) ? 1 : 0));
            int pool = 0;
            foreach (int n in t.Pool) pool += n;
            if (pool > bestPool) { bestPool = pool; best = c; }
        }
        if (best == null || heroes >= most || Realm.Stock[(int)Res.Gold] < HordeHeroCost + 150) return;
        Realm.Stock[(int)Res.Gold] -= HordeHeroCost;
        var p = NewHordeHero(best, Math.Min(6, 1 + Week / 2));
        Tell(NewsKind.Grew, $"У орды новый герой: {p.Lord.Hero.Name}.", best, p);
    }

    /// <summary>The horde's heroes standing with nothing to do look at what to do, each not oftener than every two hours.</summary>
    void HordeThink()
    {
        if (!Big) return;
        bool any = false;
        foreach (var p in Parties) if (p.Owner == Owner.Horde && p.Lord?.Hero != null && !p.Moving && !p.Tired && p.ThinkAt <= Clock.Total) { any = true; break; }
        if (!any) return;
        // (the hexes kept and the towns gathered once for all the heroes' searches)
        _cacheKept = true;
        _townsNow = Lairs;
        try
        {
            for (int i = 0; i < Parties.Count; i++)
            {
                var p = Parties[i];
                if (p.Owner != Owner.Horde || p.Lord?.Hero == null || p.Moving || p.Tired || p.ThinkAt > Clock.Total) continue;
                p.ThinkAt = Clock.Total + ThinkEvery;
                HeroDecide(p);
            }
        }
        finally { _cacheKept = false; _keptOurs = _keptHorde = null; _townsNow = null; }
    }

    float Might(MapParty p) => QuickBattle.Strength(p.Party.Members, p.Lord);

    /// <summary>
    /// What a hero of the horde standing idle does: at a town he hires and the heroes there put their armies together;
    /// a town of the horde our heroes come at, he goes back to stand by; else he goes for the best of what lies near, or
    /// strikes at the first valley when he may; with nothing worth it, home to hire, or he waits.
    /// </summary>
    void HeroDecide(MapParty p)
    {
        var here = CellOf(p);
        if (here == null) return;
        Spend(p.Lord);
        bool town = here.Holding == Holding.HordeTown && here.Owner == Owner.Horde;
        if (town) AtTown(p, here);
        if (!Parties.Contains(p)) return;
        if (Guard(p, here)) return;
        // a hero with hardly an army: home to hire first
        if (!town && p.Party.Members.Count < 5 && NearestTown(here) is { } back && SendHero(p, back, PartyAim.Return, "домой: нанять")) return;
        if (Pick(p, here)) return;
        if (!town && NearestTown(here) is { } home && SendHero(p, home, PartyAim.Return, "домой: нечего делать")) return;
        p.Errand = "ждёт";
        // (things change slowly: he looks again at the next dawn)
        p.ThinkAt = Math.Max(Clock.Total + ThinkEvery, Clock.Day * DayClock.Cycle + 1);
    }

    /// <summary>The horde's towns and lairs, gathered once for a pass of its heroes' thinking.</summary>
    List<MapCell> _townsNow;
    List<MapCell> TownsNow => _townsNow ?? Lairs;

    /// <summary>The town or lair of the horde nearest a hex.</summary>
    MapCell NearestTown(MapCell from)
    {
        MapCell best = null; int bd = int.MaxValue;
        foreach (var c in TownsNow)
        {
            int d = Distance(c.Q, c.R, from.Q, from.R);
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    /// <summary>
    /// A hero of the horde at a town: the other heroes standing idle in it give the strongest of them their men (two kept
    /// each); then he hires what waits there, the gold for the town's next building kept back, engines only at war.
    /// </summary>
    void AtTown(MapParty p, MapCell cell)
    {
        foreach (var o in Parties.ToArray())
        {
            if (o == p || o.Owner != Owner.Horde || o.Lord?.Hero == null || o.Moving || o.Q != p.Q || o.R != p.R) continue;
            var (big, small) = Might(p) >= Might(o) ? (p, o) : (o, p);
            var men = small.Party.Members;
            men.Sort((a, b) => Worth1(b).CompareTo(Worth1(a)));
            int moved = 0;
            while (men.Count > 2 && big.Party.Members.Count < ArmyMost) { big.Party.Members.Add(men[0]); men.RemoveAt(0); moved++; }
            if (moved > 0) { big.Errand = $"взял {moved} у {small.Lord.Hero.Name}"; small.ThinkAt = Clock.Total; }
        }
        var t = TownOf(cell);
        double keep = 150;
        foreach (var w in BuildOrder)
            if (!t.Lair && !t.Has(w) && (HordeWorks[(int)w].Needs is not { } need || t.Has(need))) { keep = Math.Max(keep, HordeWorks[(int)w].Gold); break; }
        int got = HireFrom(t, p.Party.Members, ArmyMost - p.Party.Members.Count, keep, engines: AtWar, army: true);
        if (got > 0) p.Errand = $"нанял {got}, в армии {p.Party.Members.Count}";
        p.MovesMax = MapWalk.MaxMoves(p, Week);
    }

    /// <summary>
    /// A town of the horde our heroes come at, whose garrison would not hold alone: its nearest hero within three days
    /// comes back to stand by it (one is enough). True if this one went.
    /// </summary>
    bool Guard(MapParty p, MapCell here)
    {
        foreach (var town in TownsNow)
        {
            if (Distance(town.Q, town.R, here.Q, here.R) > 25) continue;
            var ours = new List<Traveller>();
            foreach (var o in Parties)
                if (o.Owner == Owner.Ours && Distance(o.Q, o.R, town.Q, town.R) <= GuardNear) ours.AddRange(o.Party.Members);
            if (ours.Count == 0) continue;
            var keep = town.Sum.Holders();
            if (QuickBattle.Chance(ours, null, keep, null, town.Sum.Walls, town.Kind) < .4f) continue;
            // one hero is enough
            bool kept = false;
            foreach (var h in Parties)
                if (h != p && h.Owner == Owner.Horde && h.Lord?.Hero != null && (h.Q == town.Q && h.R == town.R && !h.Moving || h.GoalQ == town.Q && h.GoalR == town.R && h.Moving)) kept = true;
            if (kept) continue;
            if (here == town) { p.Errand = "стоит в городе: наши близко"; p.ThinkAt = Clock.Total + ThinkEvery; return true; }
            if (SendHero(p, town, PartyAim.Hold, "защищать город")) return true;
        }
        return false;
    }

    /// <summary>A hero of the horde sent to a hex with what he goes for; false if there is no way.</summary>
    bool SendHero(MapParty p, MapCell to, PartyAim aim, string errand)
    {
        if (!Send(p, to)) return false;
        p.Aim = aim;
        p.SetOut = Might(p);
        p.Errand = errand;
        Realm.Sent++;
        return true;
    }

    // ------------------------------------------------------------ what a hero of the horde goes for

    /// <summary>
    /// The best of what lies within a day's walk of a hero of the horde (three if nothing is near), or the first valley
    /// when he may strike at it and it lies within that walk: sent on his way, true; false if nothing was worth it.
    /// </summary>
    bool Pick(MapParty p, MapCell here)
    {
        int max = Math.Max(1, p.MovesMax > 0 ? p.MovesMax : MapWalk.MaxMoves(p, Week));
        // the first valley, when he may strike at it: weighed with what his searches reach, no search of its own
        float strikeOdds = StrikeOdds(p, here);
        var home = Home;
        int homeAt = strikeOdds > 0 && home != null ? IndexOf(home) : -1;
        double strikeScore = 0;
        MapCell best = null; double bestScore = 0, bestDays = 0; string bestWhy = null; PartyAim bestAim = PartyAim.Take;
        var found = new List<(MapCell c, double v, double days, bool fight, bool ours)>();
        // (where our parties stand and where the other heroes of the horde go, gathered once for the hexes looked at)
        _oursAt.Clear(); _goneTo.Clear();
        foreach (var o in Parties)
        {
            if (o.Owner == Owner.Ours && !o.Moving) _oursAt.Add(IndexOf(CellOf(o)));
            else if (o != p && o.Owner == Owner.Horde && o.Moving && At(o.GoalQ, o.GoalR) is { } g) _goneTo.Add(IndexOf(g));
        }
        foreach (int reach in new[] { 1, 3 })
        {
            found.Clear();
            var was = _shun; _shun = p; _shunCache.Clear();
            int stamp;
            try { stamp = MapWalk.Spread(this, here, Owner.Horde, reach * max); }
            finally { _shun = was; }
            var buf = WalkBuffers(Width * Height);
            if (homeAt >= 0 && buf.Mark[homeAt] >= stamp) strikeScore = 3000 * strikeOdds / (1 + buf.Cost[homeAt] / (double)max);
            foreach (int i in buf.Reached)
            {
                var c = Cells[i];
                if (c == here || _goneTo.Contains(i)) continue;
                double v = HordeWorth(p, c, out bool fight, out bool ours);
                if (v <= 0) continue;
                found.Add((c, v, buf.Cost[i] / (double)max, fight, ours));
            }
            // the best for their days looked at closely, the fights weighed only for them
            found.Sort((a, b) => (b.v / (1 + b.days)).CompareTo(a.v / (1 + a.days)));
            for (int k = 0; k < found.Count && k < 12; k++)
            {
                var (c, v, days, fight, ours) = found[k];
                double score = v / (1 + days);
                if (score <= bestScore) break;
                if (fight)
                {
                    if (ours && p.Party.Members.Count < LeastArmy / 2) continue;
                    var them = Against(c, out float walls);
                    float odds = QuickBattle.Chance(p.Party.Members, p.Lord, them, null, walls, c.Kind);
                    if (odds < (ours ? HordeWants : NeutralOdds)) continue;
                    score *= odds;
                }
                if (score > bestScore) { bestScore = score; best = c; bestDays = days; bestWhy = What(c); bestAim = ours ? PartyAim.Strike : PartyAim.Take; }
            }
            if (best != null || strikeScore > 0) break;
        }
        if (strikeScore > bestScore && home != null && SendHero(p, home, PartyAim.Strike, "на первую долину"))
        {
            Realm.LastMarch = Clock.Day; Realm.Marches++;
            int days = p.Steps.Count > 0 ? p.Steps[^1].Day : 0;
            Tell(NewsKind.RaidComing, $"Войско орды идёт на первую долину: {Raiders(p.Party.Members.Count)}, ведёт {p.Lord.Hero.Name}. Будет через {(days == 0 ? "день" : $"{days + 1} дн.")}", home, p);
            return true;
        }
        if (best == null) return false;
        return SendHero(p, best, bestAim, $"{bestWhy}, {bestDays:0.0} дн.");
    }

    /// <summary>The hexes our parties stand in, and those the other heroes of the horde go to, for a hero's look about.</summary>
    readonly HashSet<int> _oursAt = new(), _goneTo = new();

    static string What(MapCell c) => c.Thing?.Name ?? (c.Pass ? "перевал" : c.Owner == Owner.Ours ? "наша долина" : "клетка");

    /// <summary>
    /// What a hex is worth to a hero of the horde, in gold as it were, and whether he must fight for it (and whether
    /// against ours); 0 for nothing worth the walk. Ours are left be while the peace lasts; the treasures of the map too.
    /// </summary>
    double HordeWorth(MapParty p, MapCell c, out bool fight, out bool ours)
    {
        fight = false; ours = false;
        double v = 0;
        var h = p.Lord.Hero;
        if (c.Thing is { } t)
            v = t.Kind switch
            {
                ThingKind.Pile when !t.Taken => t.Res switch { Res.Gold => t.Amount, Res.Logs or Res.Stone => t.Amount * 10, Res.Metal => t.Amount * 60, _ => t.Amount * 40 },
                ThingKind.Campfire when !t.Taken => 80,
                ThingKind.Chest when !t.Taken => 110,
                ThingKind.Mine when t.Owner == Owner.None => t.Res switch { Res.Gold => 600, Res.Logs or Res.Stone => 250, Res.Metal => 300, _ => 380 },
                ThingKind.Mine when t.Owner == Owner.Ours && AtWar => (t.Res switch { Res.Gold => 600, Res.Logs or Res.Stone => 250, Res.Metal => 300, _ => 380 }) + 120,
                ThingKind.Windmill or ThingKind.WaterWheel when t.Week != Week => 90,
                ThingKind.Learning when !t.Visitors.Contains(h.Seed) => 130,
                ThingKind.Stables when p.StablesWeek != Week => 40,
                ThingKind.Dwelling when t.Owner == Owner.Ours && AtWar => 60,
                _ => 0,
            };
        if (c.Owner == Owner.Ours && !c.Home)
        {
            if (!AtWar) return 0;
            if (c.Sum.Raised > 0 || c.Sum.Keep.Count > 0) v = Math.Max(v, 500);
        }
        if (c.Home) return 0;
        if (Untaken(c)) return 0;
        // a pass or a hex kept by the map's beasts or the dead: the way on, and a hero's experience
        if (v == 0 && c.Owner == Owner.None && c.Sum.Keep.Count > 0 && (c.Pass || c.Thing?.Kind == ThingKind.Monster)) v = 60;
        // our heroes abroad, when they are weaker
        if (AtWar && _oursAt.Contains(IndexOf(c))) { v = Math.Max(v, 700); ours = true; fight = true; }
        if (v <= 0) return 0;
        if (c.Owner == Owner.None && c.Sum.Keep.Count > 0) fight = true;
        if (c.Owner == Owner.Ours && c.Sum.Keep.Exists(m => Sim.Holds(m.Kind))) { fight = true; ours = true; }
        if (c.Owner == Owner.Ours) ours = true;
        return v;
    }

    /// <summary>Who stands against a hero of the horde in a hex, and the walls: the map's keepers, or ours (their keepers and parties).</summary>
    List<Traveller> Against(MapCell c, out float walls)
    {
        walls = 0;
        if (c.Owner == Owner.None) return c.Sum.Keep;
        return Defenders(c, out walls);
    }

    /// <summary>
    /// The chance a hero of the horde would beat the first valley with, if he may strike at it now: none while the peace
    /// lasts, while the last army against it set out too lately or another is on its way, with too small an army, from
    /// further than four days' walk, or where he would not win as the horde wants with our heroes near it counted in.
    /// </summary>
    float StrikeOdds(MapParty p, MapCell here)
    {
        var home = Home;
        if (home == null || home.Owner != Owner.Ours || !AtWar || Clock.Day - Realm.LastMarch < MarchGap || p.Party.Members.Count < LeastArmy) return 0;
        if (Distance(here.Q, here.R, home.Q, home.R) > 60) return 0;
        foreach (var o in Parties) if (o.Owner == Owner.Horde && o.Aim == PartyAim.Strike && o.GoalQ == home.Q && o.GoalR == home.R && o.Moving) return 0;
        var them = Defenders(home, out float walls);
        // our heroes within two days of it come back to stand by it
        foreach (var o in Parties)
            if (o.Owner == Owner.Ours && (o.Q != home.Q || o.R != home.R) && Distance(o.Q, o.R, home.Q, home.R) <= 12) them.AddRange(o.Party.Members);
        float odds = QuickBattle.Chance(p.Party.Members, p.Lord, them, null, walls, home.Kind);
        return odds >= HordeWants ? odds : 0;
    }

    // ------------------------------------------------------------ the ways round the map's keepers

    /// <summary>The hero of the horde whose way is being found, and his strength (MapWalk.cs asks <see cref="Shuns"/>).</summary>
    MapParty _shun;
    readonly Dictionary<int, bool> _shunCache = new();

    /// <summary>
    /// Whether the hero of the horde whose way is being found goes round a hex kept by the map's beasts or the dead:
    /// those he would not beat four times in five. Nobody's way is being found, or a party without a hero: none.
    /// </summary>
    internal bool Shuns(MapCell b)
    {
        if (_shun == null || b.Sum.Keep.Count == 0) return false;
        int i = IndexOf(b);
        if (_shunCache.TryGetValue(i, out bool s)) return s;
        s = QuickBattle.Chance(_shun.Party.Members, _shun.Lord, b.Sum.Keep, null, 0, b.Kind) < AvoidOdds;
        _shunCache[i] = s;
        return s;
    }

    /// <summary>
    /// Whether the hero of the horde whose way is being found goes round a hex as he walks: a valley of ours (he falls on
    /// it only where he goes, and on the first valley only as an army sent against it).
    /// </summary>
    internal bool GoesRound(MapCell b) => _shun != null && b.Owner == Owner.Ours;

    /// <summary>The way for a party: a hero of the horde's going round the keepers he would not beat.</summary>
    List<MapCell> WayFor(MapParty p, MapCell from, MapCell to)
    {
        if (!Big || p.Owner != Owner.Horde || p.Lord?.Hero == null) return MapWalk.Find(this, from, to, p.Owner);
        var was = _shun; _shun = p; _shunCache.Clear();
        try { return MapWalk.Find(this, from, to, p.Owner); }
        finally { _shun = was; }
    }

    // ------------------------------------------------------------ what a hero of the horde has of a hex

    /// <summary>
    /// A hero of the horde walks into a hex with nothing in it to stop him: what lies there the horde has, as ours do —
    /// a pile, a campfire, a chest's gold (its experience once the treasury is full), a mine flagged (two of his weakest
    /// left in it when it is a dear one and his army is big), a mill's week, a stone's experience, the stables' moves; a
    /// dwelling of ours flagged red, so that we hire no more there. The treasures of the map it leaves be.
    /// </summary>
    void HordeVisit(MapParty p, MapCell cell)
    {
        if (!Big || cell?.Thing is not { } t) return;
        var h = p.Lord?.Hero;
        var s = Realm.Stock;
        switch (t.Kind)
        {
            case ThingKind.Pile when !t.Taken:
                t.Taken = true;
                s[(int)t.Res] += t.Amount;
                Realm.Picked++;
                break;
            case ThingKind.Campfire when !t.Taken:
                t.Taken = true;
                Realm.Picked++;
                s[(int)Res.Gold] += 40 + (int)(Roll() * 4) * 10;
                s[(int)t.Res] += MapThing.PileOf(t.Res, Roll()) * .6;
                break;
            case ThingKind.Chest when !t.Taken:
                t.Taken = true;
                var (gold, xp) = ChestOf(t);
                if (h != null && s[(int)Res.Gold] > 2000) { if (Heroes.Gain(h, xp) > 0) Spend(p.Lord); }
                else s[(int)Res.Gold] += gold;
                break;
            case ThingKind.Mine when t.Owner != Owner.Horde && (t.Owner != Owner.Ours || AtWar):
                bool ours = t.Owner == Owner.Ours;
                t.Owner = Owner.Horde;
                if (ours) Tell(NewsKind.Lost, $"{t.Name} теперь у орды: её флаг поднял {h?.Name ?? "её отряд"}.", cell, p);
                // a dear mine held: two of the weakest who can hold it stay
                if ((t.Res == Res.Gold || t.Res >= Res.Mercury) && p.Party.Members.Count >= 16 && cell.Owner == Owner.None)
                {
                    var men = p.Party.Members;
                    men.Sort((a, b) => Worth1(a).CompareTo(Worth1(b)));
                    for (int i = 0, n = 0; i < men.Count && n < 2; )
                        if (Sim.Holds(men[i].Kind)) { cell.Sum.Keep.Add(men[i]); men.RemoveAt(i); n++; } else i++;
                    if (cell.Sum.Keep.Count > 0) { cell.Owner = Owner.Horde; _held = null; }
                }
                break;
            case ThingKind.Windmill or ThingKind.WaterWheel when t.Week != Week:
                t.Week = Week;
                if (t.Kind == ThingKind.WaterWheel) s[(int)Res.Gold] += 75;
                else s[(int)t.Res] += 3 + (int)(Roll() * 4);
                break;
            case ThingKind.Learning when h != null && !t.Visitors.Contains(h.Seed):
                t.Visitors.Add(h.Seed);
                if (Heroes.Gain(h, LearningXp) > 0) Spend(p.Lord);
                break;
            case ThingKind.Stables when p.StablesWeek != Week:
                p.StablesWeek = Week;
                p.MovesMax = MapWalk.MaxMoves(p, Week);
                p.MovesLeft += StablesMoves;
                break;
            case ThingKind.Dwelling when t.Owner == Owner.Ours && AtWar:
                t.Owner = Owner.Horde;
                break;
            default: return;
        }
        HexChanged(cell);
    }

    /// <summary>A hero of the horde at the end of his way: he looks at what to do next at once.</summary>
    void HeroArrived(MapParty p)
    {
        p.Aim = PartyAim.None;
        p.ThinkAt = Clock.Total;
    }

    /// <summary>The bench's ways in: a hero of the horde made to look at what to do now; the way he would walk to a hex.</summary>
    internal void ThinkNow(MapParty p)
    {
        _cacheKept = true;
        try { HeroDecide(p); }
        finally { _cacheKept = false; _keptOurs = _keptHorde = null; }
    }

    internal List<MapCell> WayOf(MapParty p, MapCell to) => WayFor(p, CellOf(p), to);

    /// <summary>A party a hero of the horde leads, on the big map: it plays as a hero, not as a band sent out.</summary>
    bool HordeHero(MapParty p) => Big && p.Owner == Owner.Horde && p.Lord?.Hero != null;
}
