using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

public sealed partial class MapParty
{
    /// <summary>A convoy's load: the stores it carries by resource (<see cref="Res"/>, logs, stone and metal, m³); null for any other party.</summary>
    public double[] Cargo;
    /// <summary>The valley a convoy set out from (its cell's key), to go back to if the one it goes to is lost.</summary>
    public int CargoFrom = -1;
    public bool Convoy => Cargo != null;
}

/// <summary>A hero waiting in the tavern to be hired: who he is (level 1, one skill of his calling) and the men who come with him.</summary>
public sealed class TavernHero
{
    public Hero Hero;
    public string Calling = "";
    public UnitType[] Army = Array.Empty<UnitType>();
}

/// <summary>
/// What the realm's gold goes on besides men, as in «Heroes» (docs/handoff/13-world-map.md, 2.18): heroes hired from the
/// tavern by the citadel, two to choose from each week; artifacts bought from the merchant there (or found in chests and
/// treasures, kept in the citadel's treasury when no hero is by to take them); convoys carrying a valley's stores to
/// another of ours across the map; and, on the bigger maps, more heroes from the start. The dear top storeys of the
/// town are the town's own (Sim.Town.cs, <see cref="Sim.TownGold"/>).
/// </summary>
public sealed partial class Campaign
{
    /// <summary>What a hero costs at the tavern, gold; how many heroes the realm can have at the most.</summary>
    public const int HeroPrice = 600, MostHeroes = 8;
    /// <summary>What sending a convoy costs: the carters' gold, and so much more each m³ of the load.</summary>
    public const int ConvoyFee = 20;
    public const double ConvoyPerM3 = 1;
    /// <summary>The share of a chest of each tier that has an artifact in it besides its gold or experience.</summary>
    public static float ChestFind(int tier) => .15f + .1f * Math.Clamp(tier, 0, 3);

    /// <summary>The two heroes waiting in the tavern this week (one hired is gone till the next).</summary>
    public readonly List<TavernHero> Tavern = new();
    /// <summary>What the merchant by the citadel sells this week: artifacts' keys.</summary>
    public readonly List<string> Market = new();
    /// <summary>Artifacts found with no hero by to take them: in the citadel's treasury, for a hero standing there.</summary>
    public readonly List<string> Vault = new();
    int _tavernWeek = -1, _marketWeek = -1, _hiredHeroes;

    /// <summary>How many heroes start a campaign by the first valley: one on a small map, two on a middling, three on the large ones.</summary>
    public int StartHeroCount => !Big ? 1 : Width >= 108 ? 3 : Width >= 72 ? 2 : 1;

    /// <summary>
    /// How much further a party of ours walks in a day on a bigger map (the moves of «Heroes» times this): the middling
    /// map 15 % more, the large 25 %, the very large 30 %, so that its far zones are reached in a campaign's weeks. The
    /// horde's armies walk as ever (its pace is its own, MapAI*.cs).
    /// </summary>
    public double PaceScale => !Big ? 1 : Width >= 144 ? 1.3 : Width >= 108 ? 1.25 : Width >= 72 ? 1.15 : 1;

    /// <summary>A party's moves a day on this map (<see cref="MapWalk.MaxMoves"/>, ours by the map's size, <see cref="PaceScale"/>).</summary>
    public int MovesOf(MapParty p) => p.Owner == Owner.Ours ? (int)Math.Round(MapWalk.MaxMoves(p, Week) * PaceScale) : MapWalk.MaxMoves(p, Week);

    /// <summary>The heroes of a new campaign set by the first valley's citadel (<see cref="StartHeroCount"/>): the first as ever, the others from the tavern's callings.</summary>
    public void StartHeroes()
    {
        AddHero();
        for (int i = 1; i < StartHeroCount; i++)
        {
            var t = Candidate(1000 + i, i);
            Enlist(t);
        }
    }

    // ------------------------------------------------------------ the tavern

    /// <summary>The callings a hero of the tavern comes with: the skill he has a grade of and the men he brings.</summary>
    static readonly (string name, Skill skill, UnitType[] army)[] Callings =
    {
        ("воевода", Skill.Line, new[] { Kinds.Swordsman, Kinds.Swordsman, Kinds.Swordsman, Kinds.Spearman, Kinds.Spearman }),
        ("стрелок", Skill.Archers, new[] { Kinds.Archer, Kinds.Archer, Kinds.Archer, Kinds.Archer, Kinds.Swordsman }),
        ("всадник", Skill.March, new[] { Kinds.Rider, Kinds.Rider, Kinds.Archer }),
        ("лекарь", Skill.Healer, new[] { Kinds.Swordsman, Kinds.Swordsman, Kinds.Archer, Kinds.Archer, Kinds.Spearman }),
        ("зодчий", Skill.Builder, new[] { Kinds.Worker, Kinds.Worker, Kinds.Swordsman, Kinds.Swordsman, Kinds.Archer }),
        ("разведчик", Skill.Scouting, new[] { Kinds.Archer, Kinds.Archer, Kinds.Swordsman }),
    };

    /// <summary>A hero of the tavern: drawn by a number and a calling, a name not one of ours, a grade of the calling's skill.</summary>
    TavernHero Candidate(int draw, int calling)
    {
        var (name, skill, army) = Callings[(calling % Callings.Length + Callings.Length) % Callings.Length];
        var taken = new List<string>();
        foreach (var o in Parties) if (o.Owner == Owner.Ours && o.Party.Commander?.Hero is { } oh) taken.Add(oh.Name);
        foreach (var t in Tavern) taken.Add(t.Hero.Name);
        var h = Heroes.Fresh((int)(Seed * 13 + 977) + 7919 * draw, taken);
        h.Grade[(int)skill] = 1;
        return new TavernHero { Hero = h, Calling = name, Army = army };
    }

    /// <summary>The tavern as it is this week: two heroes of two callings, drawn again each week.</summary>
    public List<TavernHero> TavernNow()
    {
        if (_tavernWeek != Week)
        {
            _tavernWeek = Week;
            Tavern.Clear();
            int a = (int)(Hash.H3((int)Seed, Week, 401) * Callings.Length);
            int b = (a + 1 + (int)(Hash.H3((int)Seed, Week, 403) * (Callings.Length - 1))) % Callings.Length;
            Tavern.Add(Candidate(Week * 2 + _hiredHeroes * 31, a));
            Tavern.Add(Candidate(Week * 2 + 1 + _hiredHeroes * 31, b));
        }
        return Tavern;
    }

    /// <summary>
    /// The heroes of the map: those at the head of a party of ours. (The commanders standing in a valley's keep are its
    /// own, hired in its citadel by the valley's rule, <see cref="Sim.CaptainCap"/>; one led out onto the map is a hero of it.)
    /// </summary>
    public int HeroCount()
    {
        int n = 0;
        foreach (var p in Parties) if (p.Owner == Owner.Ours && p.Party.Commander?.Hero != null) n++;
        return n;
    }

    /// <summary>
    /// How many heroes the realm has room for: as the commanders of a valley (<see cref="Sim.CaptainCap"/>) two and one
    /// for each level of a town hall of ours, and on a bigger map one or two more; eight at the most.
    /// </summary>
    public int HeroCap()
    {
        int halls = 0;
        foreach (var c in Cells)
        {
            if (c.Owner != Owner.Ours) continue;
            halls += InValley && Live != null && c == Here ? Live.HallLevel() : c.Sum.Hall;
        }
        int more = !Big ? 0 : Width >= 108 ? 2 : Width >= 72 ? 1 : 0;
        return Math.Min(MostHeroes, Sim.MaxCaptains + halls + more);
    }

    /// <summary>Why the tavern's hero cannot be hired now, or null.</summary>
    public string CantHireHero(int i)
    {
        var list = TavernNow();
        if (i < 0 || i >= list.Count) return "в таверне никого";
        if (Home == null || Home.Owner != Owner.Ours) return "первая долина не наша";
        if (HeroCount() >= HeroCap()) return $"героев уже {HeroCap()}: ещё одного примет новый этаж ратуши";
        if (StockOf(Res.Gold) + 1e-9 < HeroPrice) return $"нужно {HeroPrice} золота";
        return null;
    }

    /// <summary>The tavern's hero hired: the gold paid, he and his men by the citadel on the map. Null if he cannot be.</summary>
    public MapParty HireHero(int i)
    {
        if (CantHireHero(i) != null) return null;
        var t = Tavern[i];
        Spend(HeroPrice);
        Tavern.RemoveAt(i);
        _hiredHeroes++;
        var p = Enlist(t);
        Tell(NewsKind.Arrived, $"Нанят герой {t.Hero.Name} ({t.Calling}), с ним {Men(p.Party.Members.Count - 1)}.", Home, p);
        return p;
    }

    /// <summary>A hero of the tavern put by the first valley's citadel with his men.</summary>
    MapParty Enlist(TavernHero t)
    {
        var p = AddHero();
        var captain = p.Party.Commander;
        captain.Hero = t.Hero;
        int key = KeyOf(Home);
        p.Party.Members.RemoveAll(m => m != captain);
        int look = 0;
        foreach (var k in t.Army) p.Party.Members.Add(new Traveller { Kind = k, Look = look++ % Math.Max(1, Bodies.Looks(k)), Hp = k.Hp, MaxHp = k.Hp, Billet = key });
        if (Big) p.MovesMax = p.MovesLeft = MovesOf(p);
        return p;
    }

    // ------------------------------------------------------------ the merchant and the treasury

    /// <summary>The merchant's goods this week: four minor artifacts and two major ones, drawn each week (a relic is never sold).</summary>
    public List<string> MarketNow()
    {
        if (_marketWeek != Week)
        {
            _marketWeek = Week;
            Market.Clear();
            void Draw(ArtTier tier, int n, int salt)
            {
                var pool = Artifacts.OfTier(tier);
                for (int k = 0; k < n && pool.Count > 0; k++)
                {
                    int i = (int)(Hash.H3((int)Seed, Week * 17 + k, salt) * pool.Count) % pool.Count;
                    Market.Add(pool[i].Key);
                    pool.RemoveAt(i);
                }
            }
            Draw(ArtTier.Minor, 4, 503);
            Draw(ArtTier.Major, 2, 509);
        }
        return Market;
    }

    /// <summary>Whether a hero of ours stands by the first valley's citadel (the merchant, the tavern's men and the treasury are there).</summary>
    public bool AtCitadel(MapParty p) =>
        p != null && p.Owner == Owner.Ours && Home != null && Home.Owner == Owner.Ours && p.Q == Home.Q && p.R == Home.R && !p.Moving && p.Party.Commander?.Hero != null;

    /// <summary>Why a hero cannot buy an artifact of the merchant now, or null.</summary>
    public string CantBuy(MapParty p, string key)
    {
        if (Artifacts.Of(key) is not { } a || !MarketNow().Contains(key)) return "этого у торговца нет";
        if (!AtCitadel(p)) return "торговец у цитадели: приведите героя к ней";
        if (StockOf(Res.Gold) + 1e-9 < a.Gold) return $"нужно {a.Gold} золота";
        foreach (var (i, n) in a.Rare)
            if (Rare[i] + 1e-9 < n) return $"нужно {MapThing.Amount_((Res)((int)Res.Mercury + i), n)}";
        return null;
    }

    /// <summary>An artifact bought from the merchant for a hero standing by the citadel: on him, or in his bag. False if it cannot be.</summary>
    public bool Buy(MapParty p, string key)
    {
        if (CantBuy(p, key) != null) return false;
        var a = Artifacts.Of(key);
        Spend(a.Gold);
        foreach (var (i, n) in a.Rare) SpendRare(i, n);
        Market.Remove(key);
        Artifacts.Give(p.Party.Commander.Hero, a);
        Tell(NewsKind.Taken, $"Куплено у торговца: «{a.Name}» ({a.About()}) для {p.Party.Commander.Hero.Name}.", Home, p);
        return true;
    }

    /// <summary>What the merchant gives for a piece of a rare resource, gold, and what he asks for one (the marketplace of «Heroes»).</summary>
    public const int RareSells = 20, RareBuys = 80;

    /// <summary>So many pieces of a rare resource (index from mercury) sold to the merchant for gold; how many were.</summary>
    public int SellRare(int i, int n)
    {
        n = Math.Min(n, (int)Rare[i]);
        if (n <= 0) return 0;
        SpendRare(i, n);
        Give(Res.Gold, n * RareSells, Home);
        return n;
    }

    /// <summary>So many pieces of a rare resource bought of the merchant for gold, as the gold goes; how many were.</summary>
    public int BuyRare(int i, int n)
    {
        n = Math.Min(n, (int)(StockOf(Res.Gold) / RareBuys));
        if (n <= 0) return 0;
        Spend(n * RareBuys);
        Give((Res)((int)Res.Mercury + i), n, Home);
        return n;
    }

    /// <summary>An artifact from the citadel's treasury given a hero standing by it. False if it is not there or he is not.</summary>
    public bool TakeFromVault(MapParty p, string key)
    {
        if (!AtCitadel(p) || !Vault.Remove(key)) return false;
        Artifacts.Give(p.Party.Commander.Hero, Artifacts.Of(key));
        return true;
    }

    /// <summary>
    /// A chest opened: as in «Heroes», now and then an artifact lies in it besides its gold or experience, the likelier
    /// the richer the chest (<see cref="ChestFind"/>). What is said of it.
    /// </summary>
    internal string ChestArtifact(MapThing t, MapParty p, MapCell cell)
    {
        int tier = (int)t.Amount;
        if (Hash.H3((int)cell.Seed, 71, 5) >= ChestFind(tier)) return "";
        var pool = Artifacts.OfTier(ArtTier.Minor);
        var a = pool[(int)(Hash.H3((int)cell.Seed, 73, 7) * pool.Count) % pool.Count];
        return " " + Found(a, p?.Party.Commander?.Hero);
    }

    /// <summary>
    /// A treasure taken: an artifact in it as well, minor in the near zones, major further, a relic now and then in the
    /// farthest. The hero who took it has it, or one of ours in the valley, or else the citadel's treasury.
    /// </summary>
    internal string TroveArtifact(MapCell c, int ring, MapParty by)
    {
        if (c.Trove == TroveKind.Shrine) return "";
        var tier = ring <= 1 ? ArtTier.Minor : ring == 2 || Hash.H3((int)c.Seed, 79, 3) < .5 ? ArtTier.Major : ArtTier.Relic;
        var pool = Artifacts.OfTier(tier);
        var a = pool[(int)(Hash.H3((int)c.Seed, 83, 11) * pool.Count) % pool.Count];
        var hero = by?.Party.Commander?.Hero;
        if (hero == null && InValley && Live != null)
            foreach (var u in Live.Units) if (!u.Dead && u.Hero != null && u.Team == Team.Player) { hero = u.Hero; break; }
        return " " + Found(a, hero);
    }

    string Found(Artifact a, Hero hero)
    {
        if (hero != null) { Artifacts.Give(hero, a); return $"Артефакт: «{a.Name}» ({a.About()}), у героя {hero.Name}."; }
        Vault.Add(a.Key);
        return $"Артефакт: «{a.Name}» ({a.About()}), в сокровищнице цитадели.";
    }

    /// <summary>The realm's gold spent, from the open valley's while one is open.</summary>
    void Spend(double gold)
    {
        if (InValley && Live != null) Live.Stock.Gold -= gold; else Gold -= gold;
    }

    void SpendRare(int i, double n)
    {
        Rare[i] = Math.Max(0, Rare[i] - n);
        if (Live != null) Live.Rare[i] = Rare[i];
    }

    /// <summary>A dawn's gifts of what our heroes wear (the purse ring's gold, the gem ring's gems…), wherever they are.</summary>
    void ArtifactDawn()
    {
        void Of(Hero h) { foreach (var (r, n) in Artifacts.Dailies(h)) Give(r, n, Home); }
        foreach (var p in Parties) if (p.Owner == Owner.Ours) Of(p.Party.Commander?.Hero);
        foreach (var c in Cells)
        {
            if (c.Owner != Owner.Ours || c.Sum.Keep.Count == 0 || InValley && c == Here) continue;
            foreach (var m in c.Sum.Keep) if (m.Hero != null) Of(m.Hero);
        }
        if (InValley && Live != null) foreach (var u in Live.Units) if (!u.Dead && u.Team == Team.Player && u.Hero != null) Of(u.Hero);
    }

    // ------------------------------------------------------------ convoys

    static readonly Res[] Hauled = { Res.Logs, Res.Stone, Res.Metal };

    /// <summary>What a convoy of so much costs to send: the carters' gold and a gold a m³.</summary>
    public static int ConvoyCost(double m3) => ConvoyFee + (int)Math.Ceiling(m3 * ConvoyPerM3);

    /// <summary>A valley's store of logs, stone or metal, m³ (the open valley's from its stock).</summary>
    public double StoreIn(MapCell c, Res r) => StoreOf(c, r);

    /// <summary>Why a convoy cannot go from one valley of ours to another with this load (logs, stone, metal, m³), or null.</summary>
    public string CantConvoy(MapCell from, MapCell to, double[] load)
    {
        if (from == null || to == null || from == to) return "выберите две разные долины";
        bool Stands(MapCell c) { foreach (var s in Strongholds) if (s == c) return true; return false; }
        if (!Stands(from) || !Stands(to)) return "обоз ходит между нашими долинами с цитаделью или заставой";
        double sum = 0;
        for (int k = 0; k < Hauled.Length; k++)
        {
            double n = load.Length > k ? load[k] : 0;
            if (n < 0) return "груз не может быть меньше нуля";
            if (n > StoreOf(from, Hauled[k]) + 1e-6) return $"в долине нет {n:0.#} м³ ({MapThing.NameOf(Hauled[k])})";
            sum += n;
        }
        if (sum <= 1e-6) return "нечего везти";
        if (StockOf(Res.Gold) + 1e-9 < ConvoyCost(sum)) return $"нужно {ConvoyCost(sum)} золота";
        if (MapWalk.Find(this, from, to, Owner.Ours) == null) return "туда нет пути";
        return null;
    }

    /// <summary>
    /// A convoy sent: the load taken from the valley's store, two carters with the carts set out on the map, slow, the
    /// fee paid. Null (and why) if it cannot go.
    /// </summary>
    public MapParty SendConvoy(MapCell from, MapCell to, double[] load, out string why)
    {
        why = CantConvoy(from, to, load);
        if (why != null) return null;
        var cargo = new double[9];
        double sum = 0;
        for (int k = 0; k < Hauled.Length; k++)
        {
            double n = load.Length > k ? load[k] : 0;
            if (n <= 0) continue;
            Take(from, Hauled[k], n);
            cargo[(int)Hauled[k]] = n;
            sum += n;
        }
        Spend(ConvoyCost(sum));
        int key = KeyOf(from);
        var p = new MapParty { Id = _nextParty++, Q = from.Q, R = from.R, CameQ = from.Q, CameR = from.R, Owner = Owner.Ours, Party = new Party(), Cargo = cargo, CargoFrom = key };
        for (int i = 0; i < 2; i++) p.Party.Members.Add(new Traveller { Kind = Kinds.Worker, Look = i, Hp = Kinds.Worker.Hp, MaxHp = Kinds.Worker.Hp, Billet = key });
        Parties.Add(p);
        if (Land != null) PlaceIn(p, from);
        if (Big) p.MovesMax = p.MovesLeft = MovesOf(p);
        Send(p, to);
        Tell(NewsKind.Arrived, $"Обоз вышел из {Names(from).gen}: {Load(cargo)}.", from, p);
        return p;
    }

    /// <summary>«4 м³ (камень), 2 м³ (брёвна)».</summary>
    public static string Load(double[] cargo)
    {
        var parts = new List<string>();
        foreach (var r in Hauled) if (cargo[(int)r] > 0) parts.Add(MapThing.Amount_(r, cargo[(int)r]));
        return string.Join(", ", parts);
    }

    /// <summary>A store of a valley taken from, m³ (the open valley's from its stock).</summary>
    void Take(MapCell c, Res r, double n)
    {
        if (InValley && Live != null && c == Here) { Live.Stock[StoreIndex(r)] = Math.Max(0, Live.Stock[StoreIndex(r)] - n * VoxelWorld.PerM3); return; }
        switch (r)
        {
            case Res.Logs: c.Sum.Logs = Math.Max(0, c.Sum.Logs - n); break;
            case Res.Stone: c.Sum.Stone = Math.Max(0, c.Sum.Stone - n); break;
            case Res.Metal: c.Sum.Metal = Math.Max(0, c.Sum.Metal - n); break;
        }
    }

    /// <summary>
    /// The convoys on the map, as the clock runs: one come where it was going unloads into that valley's store; one
    /// stopped by an enemy in its way is lost with its load; one whose valley is lost meanwhile turns back.
    /// </summary>
    void Haul()
    {
        foreach (var p in Parties.ToArray())
        {
            if (!p.Convoy || p.Owner != Owner.Ours) continue;
            var cell = At(p.Q, p.R);
            if (p.Waits)
            {
                Parties.Remove(p);
                Tell(NewsKind.Lost, $"Обоз перехвачен {Names(cell).prep}: пропало {Load(p.Cargo)}.", cell, p);
                continue;
            }
            if (p.Moving) continue;
            var goal = At(p.GoalQ, p.GoalR);
            if (cell != goal) continue;
            if (goal.Owner != Owner.Ours)
            {
                var back = CellOfKey(p.CargoFrom);
                if (back != null && back != goal && back.Owner == Owner.Ours && Send(p, back)) { Tell(NewsKind.Lost, $"Долина потеряна, обоз повернул назад.", cell, p); continue; }
                Parties.Remove(p);
                Tell(NewsKind.Lost, $"Обоз пропал: долина не наша.", cell, p);
                continue;
            }
            foreach (var r in Hauled) if (p.Cargo[(int)r] > 0) Store(goal, r, p.Cargo[(int)r]);
            Parties.Remove(p);
            Tell(NewsKind.Taken, $"Обоз пришёл в {Names(goal).acc}: {Load(p.Cargo)}.", goal, p);
        }
    }

    /// <summary>A store of a valley added to, m³ (the open valley's stock while it is open).</summary>
    void Store(MapCell c, Res r, double n)
    {
        if (InValley && Live != null && c == Here) { Live.Stock[StoreIndex(r)] += n * VoxelWorld.PerM3; return; }
        switch (r)
        {
            case Res.Logs: c.Sum.Logs += n; break;
            case Res.Stone: c.Sum.Stone += n; break;
            case Res.Metal: c.Sum.Metal += n; break;
        }
    }

    // ------------------------------------------------------------ the record

    void WriteEconomy(BinaryWriter w)
    {
        w.Write(_tavernWeek); w.Write(_hiredHeroes);
        w.Write(Tavern.Count);
        foreach (var t in Tavern)
        {
            Heroes.Write(w, t.Hero); w.Write(t.Calling);
            w.Write(t.Army.Length); foreach (var k in t.Army) w.Write(k.Key);
        }
        w.Write(_marketWeek);
        w.Write(Market.Count); foreach (var k in Market) w.Write(k);
        w.Write(Vault.Count); foreach (var k in Vault) w.Write(k);
        int convoys = 0;
        foreach (var p in Parties) if (p.Convoy) convoys++;
        w.Write(convoys);
        foreach (var p in Parties)
        {
            if (!p.Convoy) continue;
            w.Write(p.Id); w.Write(p.CargoFrom);
            foreach (var r in Hauled) w.Write(p.Cargo[(int)r]);
        }
    }

    void ReadEconomy(BinaryReader r)
    {
        _tavernWeek = r.ReadInt32(); _hiredHeroes = r.ReadInt32();
        Tavern.Clear();
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var t = new TavernHero { Hero = Heroes.Read(r), Calling = r.ReadString() };
            var army = new List<UnitType>();
            for (int k = r.ReadInt32(); k > 0; k--) if (Sim.KindOf(r.ReadString()) is { } kind) army.Add(kind);
            t.Army = army.ToArray();
            if (t.Hero != null) Tavern.Add(t);
        }
        _marketWeek = r.ReadInt32();
        Market.Clear();
        for (int n = r.ReadInt32(); n > 0; n--) { string k = r.ReadString(); if (Artifacts.Of(k) != null) Market.Add(k); }
        Vault.Clear();
        for (int n = r.ReadInt32(); n > 0; n--) { string k = r.ReadString(); if (Artifacts.Of(k) != null) Vault.Add(k); }
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            int id = r.ReadInt32(), from = r.ReadInt32();
            var cargo = new double[9];
            foreach (var res in Hauled) cargo[(int)res] = r.ReadDouble();
            var p = Parties.Find(x => x.Id == id);
            if (p != null) { p.Cargo = cargo; p.CargoFrom = from; }
        }
    }
}
