using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What a lore is paid in: the knowledge the beaten waves give, or what the old powers give — intervention and revelations.</summary>
public enum Coin : byte { Knowledge, Favour, Revelations }

/// <summary>
/// A thing the household can learn: what it is, what it does, its price and what it is paid in, what it rests on, and
/// where it stands on the tree — its page, its branch, how far along it and to which side.
/// </summary>
public sealed class Lore
{
    public string Key = "", Name = "", Branch = "", About = "";
    public int Cost;
    public Coin Coin;
    /// <summary>The lore it rests on — all of them before it; none for the first of a branch.</summary>
    public string[] Needs = Array.Empty<string>();
    /// <summary>How far along its branch it stands (0 the first, 3 the last), and to which side of it (0, ½ the middle, 1).</summary>
    public int Tier;
    public float Lane = .5f;
    /// <summary>The page of the tree: 0 the household's skills, 1 the troops, 2 the intervention of the old powers.</summary>
    public int Page;
    /// <summary>The spell this is the learning of, when it is one.</summary>
    public SpellKind? Spell;
    /// <summary>
    /// The other way of its branch at this tier: in a siege of one's own only one of the two is learnt — a
    /// specialisation — and what rests on both rests on either.
    /// </summary>
    public string Rival;
}

/// <summary>
/// What a wave beaten back gives: gold, and knowledge — what was learnt of the horde and of holding against it. The
/// knowledge goes on lore, a tree of three pages. Since 25.10.2026 the tree holds only new things, and what makes them
/// better is built (Sim.Town.cs, docs/handoff/08-town.md): the first two pages are the right to put up the town's
/// buildings — the town hall to the traps' shed on the first, the barracks to the workshop on the second, a chain of one
/// to three a branch — and a building's levels give the troops it hires and the lores that once were bought here. And the
/// intervention of the old powers: the four circles of spells and a branch of grace, learnt not for knowledge but for
/// what the powers give, intervention and revelations.
/// There is more of the knowledge than a siege gives: what to learn is a choice. What is learnt is learnt for good.
/// </summary>
public sealed partial class Sim
{
    static Lore L(string key, string branch, string name, int cost, int tier, float lane, string about, params string[] needs) =>
        new() { Key = key, Branch = branch, Name = name, Cost = cost, Tier = tier, Lane = lane, About = about, Needs = needs };
    static Lore U(string key, string branch, string name, int cost, int tier, float lane, string about, params string[] needs) =>
        new() { Key = key, Branch = branch, Name = name, Cost = cost, Tier = tier, Lane = lane, About = about, Needs = needs, Page = 1 };
    static Lore G(string key, string name, int cost, int tier, float lane, string about, params string[] needs) =>
        new() { Key = key, Branch = "Благодать", Name = name, Cost = cost, Coin = Coin.Favour, Tier = tier, Lane = lane, About = about, Needs = needs, Page = 2 };

    /// <summary>The pages of the tree, and the branches of each, in the order they go round.</summary>
    public static readonly string[] Pages = { "Город", "Войско", "Вмешательство" };
    public static readonly string[][] PageBranches =
    {
        new[] { "Хозяйство", "Ремёсла", "Кладка", "Дружина", "Ловушки" },
        new[] { "Пехота", "Стрелки", "Конница", "Великаны", "Машины" },
        new[] { "Круг земли", "Круг неба", "Круг ангелов", "Круг чудес", "Благодать" },
    };
    public static readonly string[] Branches = PageBranches[0];

    static Lore[] _lores;
    static Dictionary<string, Lore> _loreByKey;
    /// <summary>Every lore of every page.</summary>
    public static Lore[] Lores => _lores ??= MakeLores();

    static Lore[] MakeLores()
    {
        var all = new List<Lore>(Skills);
        all.AddRange(Troops);
        all.AddRange(Rites());
        return all.ToArray();
    }

    /// <summary>
    /// The town: page one. Since the author's word of 25.10.2026 the lore opens only what is new — the right to put up
    /// a building (Sim.Town.cs); what the buildings' levels give is what the lore's improvements used to give.
    /// </summary>
    static readonly Lore[] Skills =
    {
        L("b_hall", "Хозяйство", "Ратуша", 2, 0, .5f, "Можно построить ратушу. Она платит золото: больше, чем больше город."),
        L("b_market", "Хозяйство", "Рынок", 3, 1, .5f, "Можно построить рынок: камень, брёвна и землю меняют на золото и обратно.", "b_hall"),
        L("b_smithy", "Ремёсла", "Кузница", 3, 0, .5f, "Можно построить кузницу: добрые кирки, окованные ворота, жаровни, а на втором этаже кольчуги и закалённые клинки."),
        L("b_mine", "Ремёсла", "Рудокопня", 3, 1, 0, "Можно построить рудокопню на рудном выходе: 0,3 м³ металла в минуту, а на втором этаже 0,7 м³.", "b_smithy"),
        L("b_carpentry", "Ремёсла", "Плотницкая", 3, 1, 1, "Можно построить плотницкую: тачки, пожарные вёдра, мокрые шкуры, а на втором этаже носилки, нары и гильдия мастеров.", "b_smithy"),
        L("b_masons", "Кладка", "Двор каменщиков", 3, 0, .5f, "Можно построить двор каменщиков: известковый раствор и артель, а на втором этаже тёсаный камень, цитадель на скале и крепость."),
        L("b_drill", "Дружина", "Учебный плац", 3, 0, .5f, "Можно построить учебный плац: наём дешевле, стёганки, а на втором этаже солдаты быстрее."),
        L("b_infirmary", "Дружина", "Лазарет", 3, 1, .5f, "Можно построить лазарет: раненые заживают вдвое быстрее, а на втором этаже ветераны.", "b_drill"),
        L("b_traps", "Ловушки", "Ловчий двор", 3, 0, .5f, "Можно построить ловчий двор: колья, смола и огненные стрелы сильнее, на втором этаже ещё сильнее."),
    };

    /// <summary>The troops: page two. Each opens the building that hires a kind; its second storey hires the better kinds.</summary>
    static readonly Lore[] Troops =
    {
        U("b_barracks", "Пехота", "Казарма", 3, 0, .5f, "Можно построить казарму: нанимает копейщиков, а на втором этаже щитоносцев и алебардщиков."),
        U("b_knights", "Пехота", "Рыцарский двор", 6, 1, .5f, "Можно построить рыцарский двор: нанимает рыцарей в полных латах. Нужны улучшенная казарма и кузница.", "b_barracks"),
        U("b_range", "Стрелки", "Стрельбище", 3, 0, .5f, "Можно построить стрельбище: нанимает пращников, улучшает лучников, а на втором этаже нанимает арбалетчиков."),
        U("b_lab", "Стрелки", "Лаборатория алхимиков", 5, 1, .5f, "Можно построить лабораторию: нанимает алхимиков, а на втором этаже бомбардиров.", "b_range"),
        U("b_kennel", "Конница", "Псарня", 2, 0, .5f, "Можно построить псарню: нанимает боевых псов."),
        U("b_stables", "Конница", "Конюшня", 5, 1, .5f, "Можно построить конюшню: нанимает всадников, а на втором этаже конных лучников и конных рыцарей.", "b_kennel"),
        U("b_trolls", "Великаны", "Логово троллей", 5, 0, .5f, "Можно построить логово: нанимает ручных троллей."),
        U("b_giants", "Великаны", "Чертог великанов", 7, 1, .5f, "Можно построить чертог: нанимает великанов.", "b_trolls"),
        U("b_workshop", "Машины", "Мастерская", 4, 0, .5f, "Можно построить мастерскую: собирает катапульты, а на втором этаже баллисты и требушеты."),
    };

    /// <summary>The intervention: page three — the spells, circle by circle, and the boons of grace.</summary>
    static Lore[] Rites()
    {
        Lore S(SpellKind k, int tier, float lane, params SpellKind[] needs)
        {
            var sp = SpellOf(k);
            return new Lore
            {
                Key = SpellKey(k), Branch = Circles[sp.Circle], Name = sp.Name, About = sp.About, Cost = sp.Learn, Coin = sp.ByFavour ? Coin.Favour : Coin.Revelations,
                Tier = tier, Lane = lane, Page = 2, Spell = k, Needs = Array.ConvertAll(needs, SpellKey),
            };
        }
        // the waking of the god: at the heart of the page, on the greatest of every circle and the strength of faith
        var wake = S(SpellKind.Awaken, Heart, .5f, SpellKind.Quake, SpellKind.Meteor, SpellKind.Host, SpellKind.Whirlwind);
        // (and the vessel, for the store it takes)
        wake.Branch = ""; wake.Needs = new[] { SpellKey(SpellKind.Quake), SpellKey(SpellKind.Meteor), SpellKey(SpellKind.Host), SpellKey(SpellKind.Whirlwind), "might", "vessel" };
        // (the circle of death, the blood moon and the black wind, and the reaping are of the dark faith, kept for a side to come)
        return new[]
        {
            S(SpellKind.Hand, 0, .5f), S(SpellKind.Rift, 1, 0, SpellKind.Hand), S(SpellKind.Mire, 1, 1, SpellKind.Hand),
            S(SpellKind.Fall, 2, 0, SpellKind.Rift), S(SpellKind.Skin, 2, 1, SpellKind.Mire), S(SpellKind.Quake, 3, .5f, SpellKind.Fall, SpellKind.Skin),
            S(SpellKind.FireRain, 0, .5f), S(SpellKind.Thunder, 1, 0, SpellKind.FireRain), S(SpellKind.Weather, 1, 1, SpellKind.FireRain),
            S(SpellKind.Hail, 2, 0, SpellKind.Thunder), S(SpellKind.FirePillar, 2, 1, SpellKind.Weather), S(SpellKind.Meteor, 3, .5f, SpellKind.Hail, SpellKind.FirePillar),
            S(SpellKind.Guardian, 0, .5f), S(SpellKind.Dread, 1, 0, SpellKind.Guardian), S(SpellKind.Chains, 1, 1, SpellKind.Guardian),
            S(SpellKind.Destroyer, 2, 0, SpellKind.Dread), S(SpellKind.Cherub, 2, 1, SpellKind.Chains), S(SpellKind.Host, 3, .5f, SpellKind.Destroyer, SpellKind.Cherub),
            S(SpellKind.Telekinesis, 0, .5f), S(SpellKind.Recall, 1, 0, SpellKind.Telekinesis), S(SpellKind.Eye, 1, 1, SpellKind.Telekinesis),
            S(SpellKind.Explosion, 2, 0, SpellKind.Recall), S(SpellKind.Oath, 2, 1, SpellKind.Eye), S(SpellKind.Whirlwind, 3, .5f, SpellKind.Explosion, SpellKind.Oath),
            G("rosary", "Чётки", 30, 0, .5f, "Молитва жрецов даёт в 1,5 раза больше вмешательства."),
            G("vessel", "Сосуд", 40, 1, 0, "Запас вмешательства вдвое больше: до 300, а не до 150.", "rosary"),
            G("altar", "Жертвенник", 40, 1, 1, "Обряд даёт в 1,5 раза больше вмешательства и ещё одно откровение.", "rosary"),
            G("litany", "Литании", 60, 2, 0, "Заклинания на 20 % дешевле.", "vessel"),
            G("vigil", "Бдение", 60, 2, 1, "Заклинания перезаряжаются на 33 % быстрее.", "altar"),
            G("might", "Сила веры", 90, 3, .5f, "Заклинания на 33 % сильнее и действуют на 33 % дольше.", "litany", "vigil"),
            wake,
        };
    }

    /// <summary>The tier of the one lore at the heart of its page, past the four rings: the waking of the god.</summary>
    public const int Heart = 4;

    /// <summary>The lore key of a spell's learning.</summary>
    public static string SpellKey(SpellKind k) => "spell." + k;

    /// <summary>Knowledge to spend on lore.</summary>
    public double Knowledge;
    /// <summary>The lore learnt, but for the spells: those are the <see cref="Known"/> spells.</summary>
    public readonly HashSet<string> Learnt = new();

    public bool Knows(string key) => LoreOf(key) is { Spell: { } k } ? Known.Contains(k) : Learnt.Contains(key) || _granted.Contains(key);

    public static Lore LoreOf(string key)
    {
        if (key == null) return null;
        if (_loreByKey == null)
        {
            var map = new Dictionary<string, Lore>();
            foreach (var l in Lores) map[l.Key] = l;
            _loreByKey = map;
        }
        return _loreByKey.TryGetValue(key, out var lore) ? lore : null;
    }

    /// <summary>The lores of one page of the tree.</summary>
    public static IEnumerable<Lore> LoresOn(int page)
    {
        foreach (var l in Lores) if (l.Page == page) yield return l;
    }

    /// <summary>What there is to pay a lore with, in its own coin.</summary>
    public double Wallet(Coin c) => c switch { Coin.Favour => Favour, Coin.Revelations => Revelations, _ => Knowledge };

    /// <summary>Whether all a lore rests on is known.</summary>
    public bool Opened(Lore l) => Array.TrueForAll(l.Needs, n => Knows(n) || OwnSiege && LoreOf(n)?.Rival is { } r && Array.IndexOf(l.Needs, r) >= 0 && Knows(r));

    /// <summary>In a siege of one's own, the other way of a fork already taken: this one is shut.</summary>
    public bool RivalShut(Lore l) => OwnSiege && l.Rival != null && Knows(l.Rival);

    /// <summary>Whether a lore can be learnt now: not known yet, all it rests on known, what it costs at hand.</summary>
    public bool CanLearn(Lore l) => !Knows(l.Key) && Opened(l) && Wallet(l.Coin) >= l.Cost && TierAllows(l) && !RivalShut(l);

    /// <summary>In a siege of one's own the lore bought with knowledge opens tier by tier, as the chapters' champions fall.</summary>
    public bool TierAllows(Lore l) => !OwnSiege || l.Coin != Coin.Knowledge || l.Tier < TierOpen;

    /// <summary>Learns a lore, paying for it; what it does to those already in the household is done at once.</summary>
    public bool Learn(string key)
    {
        var l = LoreOf(key);
        if (l == null || !CanLearn(l)) return false;
        if (l.Spell is { } spell) return LearnSpell(spell);
        if (l.Coin == Coin.Favour) Favour -= l.Cost;
        else if (l.Coin == Coin.Revelations) Revelations -= l.Cost;
        else Knowledge -= l.Cost;
        Grant(l);
        return true;
    }

    /// <summary>A lore known from now on, paid for or not (a leader's trophy): what it does to the household done at once.</summary>
    internal void Grant(Lore l)
    {
        string key = l.Key;
        Learnt.Add(key);
        if (key == "gambesons")
            foreach (var u in Units)
                if (!u.Dead && Soldier(u)) { u.MaxHp *= 1.2f; u.Hp *= 1.2f; }
        Say($"Изучено: «{l.Name}». {l.About}", 1);
    }

    public static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        return m10 == 1 && m100 != 11 ? one : m10 is >= 2 and <= 4 && m100 is < 12 or > 14 ? few : many;
    }

    // what the lore does, where it is read

    /// <summary>One of our soldiers: an archer or a swordsman (not a worker, a priest or the risen dead).</summary>
    static bool Soldier(Unit u) => u.Team == Team.Player && u.T.Work < 1 && !u.T.Called && !Kinds.IsEngine(u.T);

    /// <summary>How far a bow carries by day: its own reach, 15 % more with yew and as much again with longbows — ours
    /// only; what the household learns is its own.</summary>
    public float BowRange(Unit a) => a.T.Range * (a.Team == Team.Player ? (Knows("yew") ? 1.15f : 1) * (Knows("longbows") ? 1.15f : 1) : 1);
    /// <summary>How much of its reach and aim the dark takes from a bow of ours: all of it, or half with the owl's eye.</summary>
    float DarkCost(Unit a) => a.Team == Team.Player && Knows("owleye") ? .5f : 1;
    /// <summary>How long our archers take between arrows, as a share.</summary>
    float VolleyPace(Unit a) => a.Team == Team.Player && Knows("volley") ? .8f : 1;
    float ArrowHarm => Knows("barbs") ? 1.25f : 1;
    float TorchLight => (Knows("braziers") ? 9 : 6) * VoxelWorld.PerMetre;
    /// <summary>How much slower the horde hacks stone set in lime mortar, and in a fortress's.</summary>
    float StoneHack => (Knows("mortar") ? .7f : 1) * (Knows("fortress") ? .8f : 1);
    float LayPace => (Knows("masons") ? 1.25f : 1) * (Knows("guild") ? 1.2f : 1);
    float DigPace => (Knows("picks") ? 1.25f : 1) * (Knows("guild") ? 1.2f : 1);
    /// <summary>How much of a blow the citadel takes.</summary>
    float KeepGuard => Knows("bastion") ? .5f : 1;
    /// <summary>How fast our workers walk, as a share.</summary>
    float CartPace(Unit u) => u.Team == Team.Player && u.T.Work >= 1 && Knows("carts") ? 1.2f : 1;
    /// <summary>How readily our own timber catches.</summary>
    float HideCatch(byte m) => Mat.IsPlayer[m] && Knows("hides") ? .5f : 1;
    /// <summary>How much of a blow one of our soldiers takes, in mail.</summary>
    float MailGuard(Unit u) => Soldier(u) && Knows("mail") ? .8f : 1;
    /// <summary>How hard one of our soldiers strikes close in, with sergeants over them.</summary>
    float SergeantArm(Unit u) => Soldier(u) && Knows("sergeants") ? 1.2f : 1;
    /// <summary>At how much of its health a wounded fighter of ours leaves the line (holding a spot, or not).</summary>
    float Steadfast(bool hold) => Knows("veterans") ? (hold ? .12f : .2f) : (hold ? .2f : .35f);
    float YieldMore => Knows("barrows") ? 1.2f : 1;
    float BeatPace => Knows("buckets") ? 2 : 1;
    /// <summary>What hiring a kind costs now.</summary>
    public int HireCost(UnitType t) => (int)Math.Round(t.Cost * (Knows("drill") ? .8 : 1));
    float MendHome => Knows("banners") ? 2 : 1;
    float StakeHarm => (Knows("spikes") ? 1.6f : 1) * (Knows("deathtraps") ? 1.3f : 1);
    float StakeHold => Knows("deathtraps") ? .5f : 1;
    float PitHarm => Knows("deathtraps") ? 2 : 1;
    float PitchBurn => Knows("tar") ? 1.5f : 1;

    // what grace does

    /// <summary>The most intervention the household can hold: 150, twice that with the vessel.</summary>
    public double FavourCap => Knows("vessel") ? 300 : 150;
    /// <summary>Intervention given by the old powers, up to what the household can hold (never taking away what it holds over it).</summary>
    internal void GainFavour(double v)
    {
        if (v <= 0 || Favour >= FavourCap) return;
        Favour = Math.Min(FavourCap, Favour + v);
    }
    float PrayerMore => Knows("rosary") ? 1.5f : 1;
    float SacrificeMore => Knows("altar") ? 1.5f : 1;
    /// <summary>What casting a spell takes now, and how long before it can be cast again.</summary>
    public double CostOf(Spell s) => Sandbox ? 0 : s.Cost * (Knows("litany") ? .8 : 1) * (HasRelic("staff") ? .67 : 1);
    public double CooldownOf(Spell s) => Sandbox ? 0 : s.Cooldown * (Knows("vigil") ? .7 : 1);
    /// <summary>How much harder the spells strike and how much longer they last, with the strength of faith.</summary>
    float Might => Knows("might") ? 1.3f : 1;
    /// <summary>What the spells kill with: a raider killed so gives intervention back with the reaping.</summary>
    static readonly HashSet<string> SpellCauses = new() { "заклятый обвал", "провал", "огненный дождь", "молния", "мор", "туман мёртвых", "взрыв", "чёрный ветер", "землетрясение", "звезда", "смерч", "телекинез" };
}
