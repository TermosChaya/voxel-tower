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
/// knowledge goes on lore, a tree of three pages. The household's skills: five branches — bows, masonry, labour, arms,
/// traps — each four deep: the first of a branch, two that rest on it, a further one on each of those, and the last,
/// resting on both; one rests on another branch besides its own. The troops: four branches of the same shape, where
/// the kinds the citadel will take on are learnt — spearmen to knights, slingers to bombardiers, hounds to mounted
/// knights, barracks to giants — with what makes them better. And the intervention of the old powers: the four circles
/// of spells and a branch of grace, learnt not for knowledge but for what the powers give, intervention and revelations.
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
    public static readonly string[] Pages = { "Умения", "Войско", "Вмешательство" };
    public static readonly string[][] PageBranches =
    {
        new[] { "Луки", "Кладка", "Труд", "Дружина", "Ловушки" },
        new[] { "Пехота", "Стрелки", "Конница", "Великаны" },
        new[] { "Круг земли", "Круг огня и неба", "Круг смерти", "Круг воли", "Благодать" },
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
        // each branch of the household's skills forks at its third tier: one way or the other
        foreach (var (a, b) in new[] { ("volley", "owleye"), ("granite", "bastion"), ("carts", "hides"), ("mail", "sergeants"), ("naphtha", "firestorm") })
        {
            all.Find(l => l.Key == a).Rival = b;
            all.Find(l => l.Key == b).Rival = a;
        }
        return all.ToArray();
    }

    /// <summary>The household's skills: page one.</summary>
    static readonly Lore[] Skills =
    {
        L("yew", "Луки", "Тисовые луки", 2, 0, .5f, "Лучники бьют на 15 % дальше."),
        L("barbs", "Луки", "Гранёные наконечники", 3, 1, 0, "Стрелы ранят на 25 % сильнее.", "yew"),
        L("braziers", "Луки", "Жаровни на стенах", 3, 1, 1, "Факелы на стенах светят ночью на 9 м вместо 6, и лучники видят врага у стен дальше.", "yew"),
        L("volley", "Луки", "Частая стрельба", 4, 2, 0, "Лучники стреляют на 20 % чаще.", "barbs"),
        L("owleye", "Луки", "Совиный глаз", 4, 2, 1, "В темноте и тумане лучники теряют вдвое меньше дальности и меткости.", "braziers"),
        L("longbows", "Луки", "Длинные луки", 6, 3, .5f, "Лучники бьют ещё на 15 % дальше.", "volley", "owleye"),
        L("mortar", "Кладка", "Известковый раствор", 2, 0, .5f, "Орда ломает камень на 33 % дольше."),
        L("masons", "Кладка", "Артель каменщиков", 3, 1, 0, "Строят на 25 % быстрее.", "mortar"),
        L("ironbound", "Кладка", "Окованные ворота", 3, 1, 1, "Ворота и подъёмные мосты выдерживают вдвое больше ударов.", "mortar"),
        L("granite", "Кладка", "Тёсаный камень", 4, 2, 0, "Камни камнемёта выбивают из кладки вдвое меньше.", "masons"),
        L("bastion", "Кладка", "Цитадель на скале", 4, 2, 1, "Цитадель получает вдвое меньше урона от орды и камнемётов.", "ironbound"),
        L("fortress", "Кладка", "Крепость", 6, 3, .5f, "Орда ломает камень ещё на 20 % дольше.", "granite", "bastion"),
        L("picks", "Труд", "Добрые кирки", 2, 0, .5f, "Копают, рубят и добывают камень на 25 % быстрее."),
        L("barrows", "Труд", "Тачки", 3, 1, 0, "Добыча приносит на 20 % больше.", "picks"),
        L("buckets", "Труд", "Пожарные вёдра", 2, 1, 1, "Огонь тушат вдвое быстрее.", "picks"),
        L("carts", "Труд", "Тропы и носилки", 4, 2, 0, "Рабочие ходят на 20 % быстрее.", "barrows"),
        L("hides", "Труд", "Мокрые шкуры", 4, 2, 1, "Наши деревянные стены, лестницы и колья загораются вдвое реже.", "buckets"),
        L("guild", "Труд", "Гильдия мастеров", 6, 3, .5f, "Копают, рубят и строят ещё на 20 % быстрее.", "carts", "masons"),
        L("gambesons", "Дружина", "Стёганки", 2, 0, .5f, "У наших солдат на 20 % больше здоровья."),
        L("drill", "Дружина", "Учебный двор", 3, 1, 0, "Наём на 20 % дешевле.", "gambesons"),
        L("banners", "Дружина", "Знамёна у цитадели", 3, 1, 1, "Раненые у цитадели и в ней выздоравливают вдвое быстрее.", "gambesons"),
        L("mail", "Дружина", "Кольчуги", 5, 2, 0, "Наши солдаты получают на 20 % меньше урона.", "drill"),
        L("sergeants", "Дружина", "Десятники", 4, 2, 1, "В ближнем бою наши солдаты бьют на 20 % сильнее.", "banners"),
        L("veterans", "Дружина", "Ветераны", 6, 3, .5f, "Раненые остаются в строю, пока у них больше 20 % здоровья, а не 33 %.", "mail", "sergeants"),
        L("spikes", "Ловушки", "Острые колья", 2, 0, .5f, "Колья ранят на 60 % сильнее."),
        L("tar", "Ловушки", "Густая смола", 3, 1, 0, "Смола горит в 1,5 раза дольше.", "spikes"),
        L("soaked", "Ловушки", "Пропитанные стрелы", 3, 1, 1, "Огненные стрелы чаще поджигают, а лучник стреляет ими почти так же быстро, как обычными.", "spikes"),
        L("naphtha", "Ловушки", "Горючая смесь", 5, 2, 0, "Радиус взрыва пороха больше на 33 %.", "tar"),
        L("firestorm", "Ловушки", "Огненный ливень", 4, 2, 1, "Налётчик, подожжённый стрелой, горит вдвое дольше, и земля под ним чаще загорается.", "soaked"),
        L("deathtraps", "Ловушки", "Ловчая наука", 6, 3, .5f, "Колья ранят ещё на 33 % сильнее и ломаются вдвое реже, волчья яма калечит вдвое сильнее.", "naphtha", "firestorm"),
    };

    /// <summary>The troops: page two. The kinds the citadel takes on once they are learnt, and what makes them better.</summary>
    static readonly Lore[] Troops =
    {
        U("spearmen", "Пехота", "Копейщики", 3, 0, .5f, "Цитадель набирает копейщиков. Копьё бьёт дальше меча и достаёт тех, кто лезет по лестнице."),
        U("shieldmen", "Пехота", "Щитоносцы", 4, 1, 0, "Цитадель набирает щитоносцев, самых живучих пехотинцев.", "spearmen"),
        U("halberds", "Пехота", "Алебардщики", 4, 1, 1, "Цитадель набирает алебардщиков. Алебарда бьёт тяжело.", "spearmen"),
        U("shieldwall", "Пехота", "Стена щитов", 4, 2, 0, "Наши бойцы в 3 м от щитоносца получают на 25 % меньше урона.", "shieldmen"),
        U("longshafts", "Пехота", "Длинные древки", 4, 2, 1, "Копья и алебарды бьют на 25 % дальше.", "halberds"),
        U("knights", "Пехота", "Рыцари", 7, 3, .5f, "Цитадель набирает рыцарей в полных латах.", "shieldwall", "longshafts"),
        U("slingers", "Стрелки", "Пращники", 2, 0, .5f, "Цитадель набирает пращников. Дешёвые и быстрые."),
        U("crossbows", "Стрелки", "Арбалетчики", 4, 1, 0, "Цитадель набирает арбалетчиков. Долго перезаряжают, но бьют сильнее и дальше лука.", "slingers"),
        U("leadshot", "Стрелки", "Свинцовые ядра", 3, 1, 1, "Пращники бьют на 33 % сильнее и отбрасывают налётчика на шаг.", "slingers"),
        U("bodkins", "Стрелки", "Бронебойные болты", 4, 2, 0, "Арбалетный болт пробивает налётчика насквозь и ранит того, кто за ним.", "crossbows"),
        U("firepots", "Стрелки", "Огненные горшки", 4, 2, 1, "Пращники иногда бросают горшок с горящей смолой. Он поджигает налётчика и землю под ним.", "leadshot"),
        U("bombards", "Стрелки", "Бомбардиры", 7, 3, .5f, "Цитадель набирает бомбардиров. Их горшки с порохом взрываются среди орды и ломают лестницы и машины, но не нашу кладку.", "bodkins", "firepots"),
        U("hounds", "Конница", "Боевые псы", 2, 0, .5f, "Цитадель набирает боевых псов. Быстрые и дешёвые, по лестницам не лазят."),
        U("riders", "Конница", "Всадники", 5, 1, 0, "Цитадель набирает всадников. Вдвое быстрее пеших, по лестницам не лазят.", "hounds"),
        U("hunt", "Конница", "Травля", 3, 1, 1, "Псы и всадники догоняют бегущих налётчиков на 33 % быстрее и бьют их вдвое сильнее. Сбежавшие не вернутся со следующей волной.", "hounds"),
        U("horsearchers", "Конница", "Конные лучники", 6, 2, 0, "Цитадель набирает конных лучников. Стреляют с коня и уходят от погони.", "riders"),
        U("charge", "Конница", "Натиск", 4, 2, 1, "Удар конницы с разгона вдвое сильнее и сбивает налётчика с ног.", "hunt"),
        U("lancers", "Конница", "Конные рыцари", 8, 3, .5f, "Цитадель набирает конных рыцарей. Удар копья с разгона сбивает с ног даже тролля.", "horsearchers", "charge"),
        U("barracks", "Великаны", "Казармы", 3, 0, .5f, "В цитадели на 4 места больше."),
        U("drillyard", "Великаны", "Учебный плац", 3, 1, 0, "Новобранцы готовятся на 33 % быстрее.", "barracks"),
        U("bunks", "Великаны", "Двухъярусные нары", 4, 1, 1, "Каждая лежанка в жилом доме даёт 2 места в войске.", "barracks"),
        U("trolls", "Великаны", "Ручные тролли", 6, 2, 0, "Цитадель набирает ручных троллей. Бьют сильно, вне боя быстро заживают.", "drillyard"),
        U("giants", "Великаны", "Великаны", 7, 2, 1, "Цитадель набирает великанов. Удар молота раскидывает налётчиков. Великан занимает 4 места.", "bunks"),
        U("boulders", "Великаны", "Глыбы", 6, 3, .5f, "Великаны и тролли бросают в орду камни, которые катятся сквозь ряды.", "trolls", "giants"),
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
        var reap = G("reap", "Жатва", 4, 3, .5f, "Каждый налётчик, убитый заклинанием, возвращает 2 вмешательства.", SpellKey(SpellKind.Plague), SpellKey(SpellKind.Raise));
        reap.Branch = "Круг смерти"; reap.Coin = Coin.Revelations;
        return new[]
        {
            S(SpellKind.Hand, 0, .5f), S(SpellKind.Rift, 1, 0, SpellKind.Hand), S(SpellKind.Mire, 1, 1, SpellKind.Hand),
            S(SpellKind.Fall, 2, 0, SpellKind.Rift), S(SpellKind.Skin, 2, 1, SpellKind.Mire), S(SpellKind.Quake, 3, .5f, SpellKind.Fall, SpellKind.Skin),
            S(SpellKind.FireRain, 0, .5f), S(SpellKind.Thunder, 1, 0, SpellKind.FireRain), S(SpellKind.Weather, 1, 1, SpellKind.FireRain),
            S(SpellKind.BlackWind, 2, 0, SpellKind.Thunder), S(SpellKind.BloodMoon, 2, 1, SpellKind.Weather), S(SpellKind.Meteor, 3, .5f, SpellKind.BlackWind, SpellKind.BloodMoon),
            S(SpellKind.Dread, 0, .5f), S(SpellKind.DeadFog, 1, 0, SpellKind.Dread), S(SpellKind.Chains, 1, 1, SpellKind.Dread),
            S(SpellKind.Plague, 2, 0, SpellKind.DeadFog), S(SpellKind.Raise, 2, 1, SpellKind.Chains), reap,
            S(SpellKind.Telekinesis, 0, .5f), S(SpellKind.Recall, 1, 0, SpellKind.Telekinesis), S(SpellKind.Eye, 1, 1, SpellKind.Telekinesis),
            S(SpellKind.Explosion, 2, 0, SpellKind.Recall), S(SpellKind.Oath, 2, 1, SpellKind.Eye), S(SpellKind.Whirlwind, 3, .5f, SpellKind.Explosion, SpellKind.Oath),
            G("rosary", "Чётки", 30, 0, .5f, "Молитва жрецов даёт в 1,5 раза больше вмешательства."),
            G("vessel", "Сосуд", 40, 1, 0, "Запас вмешательства вдвое больше: до 300, а не до 150.", "rosary"),
            G("altar", "Жертвенник", 40, 1, 1, "Жертва даёт в 1,5 раза больше вмешательства и ещё одно откровение.", "rosary"),
            G("litany", "Литании", 60, 2, 0, "Заклинания на 20 % дешевле.", "vessel"),
            G("vigil", "Бдение", 60, 2, 1, "Заклинания перезаряжаются на 33 % быстрее.", "altar"),
            G("might", "Сила веры", 90, 3, .5f, "Заклинания на 33 % сильнее и действуют на 33 % дольше.", "litany", "vigil"),
        };
    }

    /// <summary>The lore key of a spell's learning.</summary>
    public static string SpellKey(SpellKind k) => "spell." + k;

    /// <summary>Knowledge to spend on lore.</summary>
    public double Knowledge;
    /// <summary>The lore learnt, but for the spells: those are the <see cref="Known"/> spells.</summary>
    public readonly HashSet<string> Learnt = new();

    public bool Knows(string key) => LoreOf(key) is { Spell: { } k } ? Known.Contains(k) : Learnt.Contains(key);

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

    /// <summary>A wave beaten back: gold and knowledge for it, more the later the wave.</summary>
    internal void Reward(int wave)
    {
        double gold = 30 + 10 * wave;
        int know = 3 + wave;
        Stock.Gold += gold;
        Knowledge += know;
        Say($"Награда за волну: {gold:0} золота и {know} {Plural(know, "знание", "знания", "знаний")}.", 1);
    }

    static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        return m10 == 1 && m100 != 11 ? one : m10 is >= 2 and <= 4 && m100 is < 12 or > 14 ? few : many;
    }

    // what the lore does, where it is read

    /// <summary>One of our soldiers: an archer or a swordsman (not a worker, a priest or the risen dead).</summary>
    static bool Soldier(Unit u) => u.Team == Team.Player && u.T.Work < 1 && u.T != Kinds.Risen;

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
    public double CostOf(Spell s) => s.Cost * (Knows("litany") ? .8 : 1) * (HasRelic("staff") ? .67 : 1);
    public double CooldownOf(Spell s) => s.Cooldown * (Knows("vigil") ? .7 : 1);
    /// <summary>How much harder the spells strike and how much longer they last, with the strength of faith.</summary>
    float Might => Knows("might") ? 1.3f : 1;
    /// <summary>What the spells kill with: a raider killed so gives intervention back with the reaping.</summary>
    static readonly HashSet<string> SpellCauses = new() { "заклятый обвал", "провал", "огненный дождь", "молния", "мор", "туман мёртвых", "взрыв", "чёрный ветер", "землетрясение", "звезда", "смерч", "телекинез" };
}
