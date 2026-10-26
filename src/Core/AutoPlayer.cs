using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A player of ours for the big map that plays by itself, as a fair player of «Heroes» would — for the harness that
/// shows how a campaign goes (tools/CoreBench, --autoplay) and the game's debug flag (--campaign --autoplay), never in
/// play. Its heroes go for what is worth having near them (piles, campfires, chests, mines, mills, stones of learning,
/// treasures and the passes beyond, the horde's mines and towns), fight only where the odds are good, go home to hire
/// and to stand by the walls when an army of the horde comes for the first valley, and take their skills by a simple
/// rule. The first valley it does not play: its growth stands in a summary, as the valleys we are not in do — a town
/// paying more each day and built on while the gold allows (a step a day, each dearer: more gold, more men to hire and
/// better ones, higher walls), the citadel with a few more men to hire each dawn (<see cref="HomeDawn"/>).
/// </summary>
public sealed class AutoPlayer
{
    readonly Campaign _c;
    int _day;
    double _threatLook;
    /// <summary>How many hexes off the nearest army of the horde is that comes for the first valley and would beat its keep alone (max for none).</summary>
    int _threat = int.MaxValue;
    /// <summary>Hexes off the first valley within which such an army keeps a hero at home.</summary>
    public const int Near = 8;

    /// <summary>The first valley as the summary grows it: its town's gold a minute, its walls, the men its citadel has to hire.</summary>
    public const float TownFrom = 3, TownStep = .5f, TownMost = 15, WallsFrom = .3f, WallsStep = .01f, WallsMost = .7f;
    public const int ReadyFrom = 4, ReadyStep = 3, ReadyMost = 14;
    /// <summary>The men the citadel has ready to hire.</summary>
    public int Ready = ReadyFrom;
    /// <summary>The first valley's steps of building, as the summary has them; what the first and each next costs.</summary>
    public int Built;
    public const int BuildFrom = 150, BuildStep = 60, BuildMost = 20;
    /// <summary>From this step on a step is a dear top storey: it asks for two of a rare resource too, as the town's do in a campaign.</summary>
    public const int BuildRareFrom = 8;
    /// <summary>
    /// The places in the army the first valley has, as the summary builds its houses: the citadel's and five a step. All
    /// of ours, its keep and the heroes' armies, hold them; none is hired over them.
    /// </summary>
    public int Places => PlacesFrom + PlacesStep * Built;
    public const int PlacesFrom = 14, PlacesStep = 5;

    /// <summary>All of ours holding places: the first valley's keep and every party of ours.</summary>
    int Men()
    {
        int n = _c.Home?.Sum.Keep.Count ?? 0;
        foreach (var p in _c.Parties) if (p.Owner == Owner.Ours) n += p.Party.Members.Count;
        return n;
    }
    /// <summary>Odds a hero fights at, a town of the horde at, and below which he goes home from where he stands.</summary>
    public const float Bold = .75f, TownBold = .85f, Stand = .6f;
    /// <summary>The gold kept back for the citadel's men, and what another hero costs (one by the third day, a third later).</summary>
    public const int Reserve = 120, HeroCost = Campaign.HeroPrice;

    /// <summary>What it did, a line each (the day and the hour first): the harness's story.</summary>
    public readonly List<string> Said = new();
    /// <summary>The battles ours chose: the day, where, the odds, won.</summary>
    public readonly List<(int day, string where, float odds, bool won)> Fights = new();

    /// <summary>Where a hero was going and why, by his party's id (the harness draws it).</summary>
    public readonly Dictionary<int, string> Aims = new();
    /// <summary>Hexes found too strong to take, with the strength ours had then: looked at again only once ours have grown.</summary>
    readonly Dictionary<MapCell, float> _tooStrong = new();
    /// <summary>Observatories looked through (they lift the fog once).</summary>
    readonly HashSet<MapCell> _looked = new();

    public AutoPlayer(Campaign c) { _c = c; _day = c.Clock.Day; }

    void Say(string s) => Said.Add($"д{_c.Clock.Day} {_c.Clock.At / DayClock.Cycle * 24:00}ч  {s}");

    IEnumerable<MapParty> OurHeroes()
    {
        foreach (var p in _c.Parties) if (p.Owner == Owner.Ours && p.Party.Commander?.Hero != null) yield return p;
    }

    static string Where(MapCell c) => $"{c.Col},{c.Row}";

    /// <summary>
    /// The first valley as a fresh campaign has it on the map: the household (four workers, a swordsman, an archer) in
    /// its keep, the citadel's walls, a town hall's gold.
    /// </summary>
    public void Begin()
    {
        var home = _c.Home;
        if (home == null) return;
        int key = Campaign.KeyOf(home);
        if (home.Sum.Keep.Count == 0)
            foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Swordsman, Kinds.Archer })
                home.Sum.Keep.Add(Man(t, key));
        home.Sum.Walls = Math.Max(home.Sum.Walls, WallsFrom);
        home.Sum.TownGold = Math.Max(home.Sum.TownGold, TownFrom);
        Say($"Начало: герой у цитадели {Where(home)}, золото {_c.Gold:0}.");
    }

    static Traveller Man(UnitType t, int key) => new() { Kind = t, Look = 0, Hp = t.Hp, MaxHp = t.Hp, Billet = key };

    /// <summary>Called as often as the map's clock is run: the dawn's business, then what each hero should do now.</summary>
    public void Tick()
    {
        if (_c.Clock.Day != _day) { _day = _c.Clock.Day; HomeDawn(); }
        // a chest's choice: gold while the realm is poor, experience after the second week
        foreach (var (p, cell) in _c.Chests.ToArray())
            if (p.Owner == Owner.Ours)
            {
                bool gold = _c.Clock.Day <= 14;
                Say($"Сундук {Where(cell)}: {(gold ? "золото" : "опыт")}. {_c.OpenChest(cell, gold)}");
            }
        // a dwelling: hired from while the gold lasts over the reserve
        foreach (var (p, cell) in _c.Dwellings.ToArray())
        {
            if (p.Owner != Owner.Ours || cell.Thing is not { } t || t.Stock == 0) continue;
            int can = Math.Min((int)((_c.Gold - Reserve) / Campaign.HireCost(t.Breed)), Places - Men());
            if (can <= 0) continue;
            int got = _c.Hire(cell, can);
            if (got > 0) Say($"Жилище {Where(cell)}: нанято {got} ({t.Breed.Name}).");
        }
        foreach (var p in OurHeroes())
        {
            var h = p.Party.Commander.Hero;
            while (h.Pending) { var s = Pick(h.Offer); Heroes.Choose(h, s); Say($"{h.Name}: уровень {h.Level}, умение «{Heroes.NameOf(s)}»."); }
        }
        // an army of the horde heading for the first valley: the heroes that can be there first go home (looked at now and then)
        if ((_threatLook -= 1) <= 0) { _threatLook = 20; Defend(); }
        // a party of ours whose hero fell: home, into the keep
        foreach (var p in _c.Parties.ToArray())
        {
            if (p.Owner != Owner.Ours || p.Party.Commander?.Hero != null || p.Convoy || p.Moving || p.Waits || _c.Home == null) continue;
            if (p.Q == _c.Home.Q && p.R == _c.Home.R) { _c.Home.Sum.Keep.AddRange(p.Party.Members); _c.Parties.Remove(p); Say($"Отряд без героя вошёл в гарнизон: {p.Party.Members.Count}."); }
            else _c.Send(p, _c.Home);
        }
        foreach (var p in new List<MapParty>(OurHeroes()))
        {
            if (!_c.Parties.Contains(p)) continue;
            if (p.Waits) { AtFoes(p); continue; }
            if (p.Moving || p.Tired) continue;
            Decide(p);
        }
    }

    /// <summary>Skills by a simple rule: the march first, then the line, the bows, tactics, the healer, scouting, the word, building.</summary>
    static Skill Pick(List<Skill> offer)
    {
        foreach (var s in new[] { Skill.March, Skill.Line, Skill.Archers, Skill.Tactics, Skill.Healer, Skill.Scouting, Skill.Word, Skill.Builder })
            if (offer.Contains(s)) return s;
        return offer[0];
    }

    /// <summary>
    /// The first valley's dawn as its summary has it: its town pays more, its walls rise, its citadel has more men to
    /// hire; they are hired into its keep while it is thin, into a hero's army when he stands there; another hero is
    /// hired when the gold allows.
    /// </summary>
    void HomeDawn()
    {
        var home = _c.Home;
        if (home == null || home.Owner != Owner.Ours) return;
        home.Sum.TownGold = Math.Min(TownMost + Built, home.Sum.TownGold + TownStep);
        home.Sum.Walls = Math.Min(WallsMost + Built * .01f, home.Sum.Walls + WallsStep);
        Ready = Math.Min(ReadyMost + Built, Ready + ReadyStep + Built / 3);
        // a step of building while the gold allows: the town pays a gold more a minute, the citadel hires more and better
        // the rare resources over what the dear steps want sold to the merchant for gold (the marketplace of «Heroes»)
        for (int i = 0; i < _c.Rare.Length; i++)
            if (_c.Rare[i] > 20) { int sold = _c.SellRare(i, (int)_c.Rare[i] - 20); if (sold > 0) Say($"Продано торговцу: {sold} ({MapThing.NameOf((Res)((int)Res.Mercury + i))}), {sold * Campaign.RareSells} зол."); }
        int cost = BuildFrom + BuildStep * Built;
        int rare = 0;
        for (int i = 1; i < _c.Rare.Length; i++) if (_c.Rare[i] > _c.Rare[rare]) rare = i;
        bool dear = Built >= BuildRareFrom;
        if (Built < BuildMost && _c.Gold >= cost + Reserve && (!dear || _c.Rare[rare] >= 2))
        {
            _c.Gold -= cost; Built++;
            if (dear) _c.Rare[rare] -= 2;
            home.Sum.TownGold += 1;
            // (the town hall's levels as the steps go: the realm has room for more heroes)
            home.Sum.Hall = Built >= 12 ? 3 : Built >= 6 ? 2 : Built >= 2 ? 1 : 0;
            Say($"Город: построено ({Built}-й шаг, {cost} зол.{(dear ? $", 2 ({MapThing.NameOf((Res)((int)Res.Mercury + rare))})" : "")}), доход города {home.Sum.TownGold:0} зол. в минуту.");
        }
        int key = Campaign.KeyOf(home);
        // the keep: six and a third of the days gone, at the least
        int want = KeepWant, hired = 0;
        while (home.Sum.Keep.Count < want && Ready > 0 && _c.Gold >= Campaign.HireCost(Kinds.Spearman) && Men() < Places)
        {
            var t = home.Sum.Keep.Count % 3 == 0 ? Kinds.Archer : Kinds.Spearman;
            _c.Gold -= Campaign.HireCost(t); Ready--; hired++;
            home.Sum.Keep.Add(Man(t, key));
        }
        if (hired > 0) Say($"Цитадель: в гарнизон нанято {hired}, в нём {home.Sum.Keep.Count}.");
        int heroes = 0;
        foreach (var _ in OurHeroes()) heroes++;
        // another hero from the tavern while the realm has room for him: with none left at once, else from the third
        // day while the gold lasts over the reserve, a little more kept back for each hero more
        var tavern = _c.TavernNow();
        // (only while the places leave room for his men besides: the armies first)
        if (tavern.Count > 0 && (heroes == 0 ? _c.Gold >= HeroCost : _c.Clock.Day >= 3 && (Men() + 4 <= Places || heroes <= _c.StartHeroCount) && _c.Gold >= HeroCost + Reserve + 300 * (heroes - 1)))
        {
            int pick = 0;
            for (int i = 1; i < tavern.Count; i++) if (Army(tavern[i]) > Army(tavern[pick])) pick = i;
            string calling = tavern[pick].Calling;
            var p = _c.HireHero(pick);
            if (p != null) Say($"Нанят герой {p.Party.Commander.Hero.Name} ({calling}) с отрядом из {p.Party.Members.Count - 1}.");
        }
        foreach (var p in OurHeroes()) if (p.Q == home.Q && p.R == home.R && !p.Moving) HireInto(p);
        // the stores of an outpost taken home by a convoy, every third day
        if (_c.Clock.Day % 3 == 0)
            foreach (var c in new List<MapCell>(_c.Strongholds))
            {
                if (c == home) continue;
                var load = new[] { _c.StoreIn(c, Res.Logs), _c.StoreIn(c, Res.Stone), _c.StoreIn(c, Res.Metal) };
                if (load[0] + load[1] + load[2] < 15 || _c.Gold < Campaign.ConvoyCost(load[0] + load[1] + load[2]) + Reserve) continue;
                if (_c.SendConvoy(c, home, load, out _) != null) Say($"Обоз {Where(c)} → домой: {Campaign.Load(new[] { 0, load[0], load[1], 0, load[2], 0, 0, 0, 0.0 })}.");
            }
    }

    /// <summary>The men the first valley's keep should have: six and a third of the days gone, twenty-four at most.</summary>
    int KeepWant => Math.Min(24, 6 + _c.Clock.Day / 3);

    /// <summary>
    /// A hero standing at home: the keep's fighting men over what it should have go with him, then the citadel's men are
    /// hired into his army, the reserve kept.
    /// </summary>
    void HireInto(MapParty p)
    {
        var keep = _c.Home.Sum.Keep;
        int moved = 0;
        for (int i = keep.Count - 1; i >= 0 && keep.Count > KeepWant; i--)
            if (keep[i].Kind != Kinds.Worker) { p.Party.Members.Add(keep[i]); keep.RemoveAt(i); moved++; }
        if (moved > 0) Say($"{p.Party.Commander.Hero.Name} взял из гарнизона {moved}, в армии {p.Party.Members.Count}.");
        int got = 0;
        while (Ready > 0 && _c.Gold - Reserve >= Campaign.HireCost(Best(got)) && Men() < Places)
        {
            var t = Best(got);
            _c.Gold -= Campaign.HireCost(t); Ready--; got++;
            p.Party.Members.Add(Man(t, Campaign.KeyOf(_c.Home)));
        }
        if (got > 0) Say($"{p.Party.Commander.Hero.Name} нанял в цитадели {got}, в армии {p.Party.Members.Count}.");
        Equip(p);
    }

    /// <summary>The strength a tavern's hero comes with.</summary>
    static float Army(TavernHero t)
    {
        var men = new List<Traveller>();
        foreach (var k in t.Army) men.Add(Man(k, -1));
        return QuickBattle.Strength(men, null);
    }

    /// <summary>
    /// A hero standing by the citadel: what the treasury keeps for him, then — once his army cannot grow now, the
    /// citadel having nobody ready or the places none — the merchant's best for a place he has nothing in (or something
    /// rarer than he wears there), while the gold lasts well over the reserve: the men first, the things after.
    /// </summary>
    void Equip(MapParty p)
    {
        var h = p.Party.Commander.Hero;
        foreach (var key in _c.Vault.ToArray())
            if (Artifacts.Of(key) is { } a && Better(h, a) && _c.TakeFromVault(p, key)) Say($"{h.Name} взял из сокровищницы «{a.Name}».");
        if (Ready > 0 && Men() < Places) return;
        for (bool bought = true; bought;)
        {
            bought = false;
            Artifact best = null;
            foreach (var key in _c.MarketNow())
                if (Artifacts.Of(key) is { } a && Better(h, a) && _c.CantBuy(p, key) == null && _c.Gold - a.Gold >= Reserve + 400 && (best == null || a.Tier > best.Tier || a.Tier == best.Tier && a.Gold > best.Gold)) best = a;
            if (best != null && _c.Buy(p, best.Key)) { bought = true; Say($"{h.Name} купил «{best.Name}» ({best.Price()})."); }
        }
    }

    /// <summary>Whether a thing is worth putting on: its place empty, or what is there of a lower tier.</summary>
    static bool Better(Hero h, Artifact a) => h.Worn[(int)a.Slot] is not { } w || Artifacts.Of(w) is not { } now || a.Tier > now.Tier;

    /// <summary>The man the citadel hires next for a hero's army, the better the more the town is built: swordsmen, spearmen, archers; halberdiers and crossbowmen; knights.</summary>
    UnitType Best(int n) => (n % 4, Built) switch
    {
        (0, >= 8) => Kinds.Knight,
        (1, >= 3) => Kinds.Halberdier,
        (3, >= 3) => Kinds.Crossbowman,
        (2, _) => Kinds.Archer,
        (1, _) => Kinds.Spearman,
        _ => Kinds.Swordsman,
    };

    float Strength(MapParty p) => QuickBattle.Strength(p.Party.Members, null);

    /// <summary>
    /// An army of the horde on its way to the first valley: each hero who can be home before it and whose men would
    /// turn the battle goes home to stand by the walls.
    /// </summary>
    void Defend()
    {
        var home = _c.Home;
        _threat = int.MaxValue;
        if (home == null || home.Owner != Owner.Ours) return;
        foreach (var e in _c.Parties)
        {
            if (e.Owner != Owner.Horde || e.GoalQ != home.Q || e.GoalR != home.R || e.Aim is not (PartyAim.Strike or PartyAim.Take) || !e.Moving) continue;
            int eta = e.Path.Count;
            float theirs = QuickBattle.Strength(e.Party.Members, e.Lord), keep = QuickBattle.Strength(home.Sum.Keep, null, home.Sum.Walls);
            if (keep > theirs * 1.5f) continue;
            _threat = Math.Min(_threat, eta);
            foreach (var p in OurHeroes())
            {
                if (p.GoalQ == home.Q && p.GoalR == home.R && (p.Moving || p.Q == home.Q && p.R == home.R)) continue;
                var way = MapWalk.Find(_c, _c.CellOf(p), home, Owner.Ours);
                // home in time, and not before he must set out; and only where he and the keep together would hold
                if (way == null || way.Count > eta + 2 || way.Count < eta - 4) continue;
                var both = new List<Traveller>(home.Sum.Keep);
                both.AddRange(p.Party.Members);
                if (QuickBattle.Chance(e.Party.Members, e.Lord, both, null, home.Sum.Walls, home.Kind) > .5f) continue;
                if (_c.Send(p, home))
                {
                    Aims[p.Id] = "домой: защищать";
                    Say($"{p.Party.Commander.Hero.Name} идёт защищать цитадель: войско орды ({e.Party.Members.Count}{(e.Lord != null ? ", с военачальником" : "")}) в {eta} кл. от неё.");
                }
            }
        }
    }

    /// <summary>A hero stopped where the enemy stands: fought at once if the odds are good, else he goes home.</summary>
    void AtFoes(MapParty p)
    {
        var cell = _c.CellOf(p);
        float odds = Odds(p, cell);
        if (odds >= Stand)
        {
            int before = p.Party.Members.Count;
            var r = _c.FightHere(p);
            bool won = r == null || r.AttackerWon;
            Fights.Add((_c.Clock.Day, Where(cell), odds, won));
            Say($"Бой {Where(cell)} ({What(cell)}): шансы {odds:0.00}, {(won ? "победа" : "поражение")}, наших было {before}, осталось {(_c.Parties.Contains(p) ? p.Party.Members.Count : 0)}.");
            if (!won) _tooStrong[cell] = Strength(p) * 1.5f;
            return;
        }
        _tooStrong[cell] = Strength(p);
        p.Waits = false;
        Say($"{p.Party.Commander.Hero.Name} отступает от {Where(cell)} ({What(cell)}): шансы {odds:0.00}.");
        if (_c.Home != null) { _c.Send(p, _c.Home); Aims[p.Id] = "домой: отступил"; }
    }

    /// <summary>A hero's chance against what keeps a hex: its keepers (not ours) and the horde's parties standing in it.</summary>
    float Odds(MapParty p, MapCell cell)
    {
        var them = new List<Traveller>();
        Warlord lord = null;
        if (cell.Owner != Owner.Ours) them.AddRange(cell.Sum.Holders());
        foreach (var h in _c.Parties)
            if (h.Owner == Owner.Horde && h.Q == cell.Q && h.R == cell.R) { them.AddRange(h.Party.Members); lord ??= h.Lord; }
        if (them.Count == 0 && lord == null) return 1;
        return QuickBattle.Chance(p.Party.Members, null, them, lord, cell.Owner == Owner.Horde ? cell.Sum.Walls : 0, cell.Kind);
    }

    static string What(MapCell c) => c.Thing?.Name ?? (c.Pass ? "перевал" : c.Owner == Owner.Horde ? "земля орды" : "клетка");

    /// <summary>
    /// What a hero stands to have of a hex, in gold as it were, and whether he must fight for it; 0 for nothing worth
    /// the walk.
    /// </summary>
    double Worth(MapParty p, MapCell c, out bool fight)
    {
        fight = false;
        double v = 0;
        var hero = p.Party.Commander.Hero;
        if (c.Thing is { } t)
            v = t.Kind switch
            {
                ThingKind.Pile when !t.Taken => t.Res switch { Res.Gold => t.Amount, Res.Logs or Res.Stone => t.Amount * 8, Res.Metal => t.Amount * 60, _ => t.Amount * 30 },
                ThingKind.Campfire when !t.Taken => 80,
                ThingKind.Chest when !t.Taken => 110,
                ThingKind.Mine when t.Owner != Owner.Ours => (t.Res switch { Res.Gold => 600, Res.Logs or Res.Stone => 250, Res.Metal => 300, _ => 320 }) + (t.Owner == Owner.Horde ? 100 : 0),
                ThingKind.Windmill or ThingKind.WaterWheel when t.Week != _c.Week => 90,
                ThingKind.Learning when !t.Visitors.Contains(hero.Seed) => 130,
                ThingKind.Stables when p.StablesWeek != _c.Week => 50,
                ThingKind.Observatory when !_looked.Contains(c) => 80,
                ThingKind.Shrine when !t.Taken => 250,
                ThingKind.Dwelling when t.Stock > 0 && _c.Gold - Reserve >= Campaign.HireCost(t.Breed) && Men() < Places => 40 * Math.Min(t.Stock, (int)((_c.Gold - Reserve) / Campaign.HireCost(t.Breed))),
                ThingKind.Trove when Campaign.Untaken(c) => Campaign.RewardOf(c.Trove, Campaign.RingOf(c)).gold + Campaign.RewardOf(c.Trove, Campaign.RingOf(c)).metal * 60 + 100,
                ThingKind.HordeTown or ThingKind.Lair when c.Owner == Owner.Horde => 2500,
                _ => 0,
            };
        // a pass kept by beasts or the dead, to a zone not yet seen: the way on
        if (v == 0 && c.Pass && c.Sum.Keep.Count > 0 && c.Owner == Owner.None) v = 140;
        // a hex of the horde's (a mine flagged red, a valley it took): worth taking back
        if (c.Owner == Owner.Horde && c.Holding != Holding.HordeTown) v = Math.Max(v, 150);
        // the first valley taken: back before the days run out, above all
        if (c.Home && c.Owner == Owner.Horde) v = 10000;
        if (v <= 0) return 0;
        if (_c.Foes(c) > 0 || c.Owner == Owner.Horde && c.Sum.Keep.Count > 0)
        {
            fight = true;
            float now = Strength(p);
            if (_tooStrong.TryGetValue(c, out float then) && now < then * 1.2f) return 0;
            float odds = Odds(p, c);
            if (odds < (c.Holding == Holding.HordeTown ? TownBold : Bold)) return 0;
            // (the dearer the fight, the less it is worth: what the odds leave of the army)
            v *= odds;
        }
        return v;
    }

    /// <summary>
    /// A hero standing with nothing to do: at home he hires; then he goes for the hex worth most for the days it
    /// takes him to get there (three days' walk looked over, eight if nothing is near); with nothing worth it, he goes
    /// to the edge of the fog.
    /// </summary>
    void Decide(MapParty p)
    {
        var here = _c.CellOf(p);
        if (here == null) return;
        if (here == _c.Home)
        {
            HireInto(p);
            // an army of the horde near that the keep would not hold alone: he stands by the walls
            if (_threat <= Near) { Aims[p.Id] = "дома: орда близко"; return; }
        }
        if (here.Thing is { Kind: ThingKind.Observatory }) _looked.Add(here);
        int max = Math.Max(1, p.MovesMax > 0 ? p.MovesMax : _c.MovesOf(p));
        MapCell best = null;
        double bestScore = 0, bestDays = 0;
        bool bestFight = false;
        foreach (int reach in new[] { 3, 8 })
        {
            int stamp = MapWalk.Spread(_c, here, Owner.Ours, reach * max);
            var buf = _c.WalkBuffers(_c.Width * _c.Height);
            for (int i = 0; i < buf.Mark.Length; i++)
            {
                if (buf.Mark[i] < stamp) continue;
                var c = _c.Cell(i % _c.Width, i / _c.Width);
                if (c == here || !_c.Explored(c)) continue;
                // (another hero already going there)
                bool taken = false;
                foreach (var o in OurHeroes()) if (o != p && o.Moving && o.GoalQ == c.Q && o.GoalR == c.R) taken = true;
                if (taken) continue;
                double v = Worth(p, c, out bool fight);
                if (v <= 0) continue;
                double days = buf.Cost[i] / (double)max;
                double score = v / (1 + days);
                if (score > bestScore) { bestScore = score; best = c; bestDays = days; bestFight = fight; }
            }
            if (best != null) break;
        }
        string why;
        if (best == null)
        {
            best = Frontier(p, here);
            why = "к краю тумана";
            // with nothing to go for, home: to hire and to stand by the walls
            if (best == null)
            {
                if (here == _c.Home || _c.Home == null || _c.Home.Owner != Owner.Ours) return;
                best = _c.Home; why = "домой: нечего делать";
            }
        }
        else why = $"{What(best)}{(bestFight ? " (бой)" : "")}, {bestDays:0.0} дн.";
        if (!_c.Send(p, best)) { _tooStrong[best] = float.MaxValue; return; }
        Aims[p.Id] = why;
        Say($"{p.Party.Commander.Hero.Name} ({p.Party.Members.Count} чел., ход {p.MovesLeft}/{p.MovesMax}) → {Where(best)}: {why}.");
    }

    /// <summary>
    /// The nearest hex at the edge of the fog a hero can walk to: seen, with unseen beyond it; one kept by beasts or the
    /// dead (a pass out of the zone) if he would beat them.
    /// </summary>
    MapCell Frontier(MapParty p, MapCell here)
    {
        int stamp = MapWalk.Spread(_c, here, Owner.Ours, 20 * MapWalk.Moves);
        var buf = _c.WalkBuffers(_c.Width * _c.Height);
        MapCell best = null;
        int bestCost = int.MaxValue;
        for (int i = 0; i < buf.Mark.Length; i++)
        {
            if (buf.Mark[i] < stamp || buf.Cost[i] >= bestCost) continue;
            var c = _c.Cell(i % _c.Width, i / _c.Width);
            if (c == here || !c.Passable) continue;
            if (_c.Foes(c) > 0 && (_tooStrong.TryGetValue(c, out float then) && Strength(p) < then * 1.2f || Odds(p, c) < Bold)) continue;
            bool edge = false;
            for (int s = 0; s < 6 && !edge; s++) if (_c.Neighbour(c, s) is { } n && !_c.Explored(n)) edge = true;
            if (!edge) continue;
            best = c; bestCost = buf.Cost[i];
        }
        return best;
    }
}
