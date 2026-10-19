using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A thing the household can learn with what the beaten waves taught it: what it is, what it does, its price, what it
/// rests on, and where it stands in its branch.
/// </summary>
public sealed class Lore
{
    public string Key = "", Name = "", Branch = "", About = "";
    public int Cost;
    /// <summary>The lore it rests on — all of them before it; none for the first of a branch.</summary>
    public string[] Needs = Array.Empty<string>();
    /// <summary>How far along its branch it stands (0 the first, 3 the last), and to which side of it (0, ½ the middle, 1).</summary>
    public int Tier;
    public float Lane = .5f;
}

/// <summary>
/// What a wave beaten back gives: gold, and knowledge — what was learnt of the horde and of holding against it. The
/// knowledge goes on lore, a tree of five branches — bows, masonry, labour, arms, traps — each four deep: the first of a
/// branch, two that rest on it, a further one on each of those, and the last, resting on both; one rests on another
/// branch besides its own. There is more of it than a siege gives knowledge for: what to learn is a choice. What is
/// learnt is learnt for good.
/// </summary>
public sealed partial class Sim
{
    static Lore L(string key, string branch, string name, int cost, int tier, float lane, string about, params string[] needs) =>
        new() { Key = key, Branch = branch, Name = name, Cost = cost, Tier = tier, Lane = lane, About = about, Needs = needs };

    public static readonly Lore[] Lores =
    {
        L("yew", "Луки", "Тисовые луки", 2, 0, .5f, "Лучники бьют на 15 % дальше."),
        L("barbs", "Луки", "Гранёные наконечники", 3, 1, 0, "Стрелы ранят на четверть сильнее.", "yew"),
        L("braziers", "Луки", "Жаровни на стенах", 3, 1, 1, "Факелы на стенах светят ночью на 9 м вместо 6: лучники видят врага у стен дальше.", "yew"),
        L("volley", "Луки", "Частая стрельба", 4, 2, 0, "Лучники стреляют на пятую часть чаще.", "barbs"),
        L("owleye", "Луки", "Совиный глаз", 4, 2, 1, "В темноте и тумане лучники теряют вдвое меньше дальности и меткости.", "braziers"),
        L("longbows", "Луки", "Длинные луки", 6, 3, .5f, "Лучники бьют ещё на 15 % дальше.", "volley", "owleye"),
        L("mortar", "Кладка", "Известковый раствор", 2, 0, .5f, "Орда рубит и ломает камень на треть дольше."),
        L("masons", "Кладка", "Артель каменщиков", 3, 1, 0, "Кладут и строят на четверть быстрее.", "mortar"),
        L("ironbound", "Кладка", "Окованные ворота", 3, 1, 1, "Ворота и подъёмные мосты выдерживают вдвое больше ударов, прежде чем их прорубят.", "mortar"),
        L("granite", "Кладка", "Тёсаный камень", 4, 2, 0, "Камень камнемёта выбивает из кладки вдвое меньше.", "masons"),
        L("bastion", "Кладка", "Цитадель на скале", 4, 2, 1, "Цитадель теряет вдвое меньше от ударов орды и камней камнемёта.", "ironbound"),
        L("fortress", "Кладка", "Крепость", 6, 3, .5f, "Орда рубит камень ещё на пятую часть дольше.", "granite", "bastion"),
        L("picks", "Труд", "Добрые кирки", 2, 0, .5f, "Копают, рубят и ломают камень на четверть быстрее."),
        L("barrows", "Труд", "Тачки", 3, 1, 0, "Добытого в запасы приходит на пятую часть больше.", "picks"),
        L("buckets", "Труд", "Пожарные вёдра", 2, 1, 1, "Огонь тушат вдвое быстрее.", "picks"),
        L("carts", "Труд", "Тропы и носилки", 4, 2, 0, "Рабочие ходят на пятую часть быстрее.", "barrows"),
        L("hides", "Труд", "Мокрые шкуры", 4, 2, 1, "Наше дерево — стены, лестницы, колья — загорается вдвое реже.", "buckets"),
        L("guild", "Труд", "Гильдия мастеров", 6, 3, .5f, "Копают, рубят и кладут ещё на пятую часть быстрее.", "carts", "masons"),
        L("gambesons", "Дружина", "Стёганки", 2, 0, .5f, "Лучники и мечники крепче на пятую часть."),
        L("drill", "Дружина", "Учебный двор", 3, 1, 0, "Наём на пятую часть дешевле.", "gambesons"),
        L("banners", "Дружина", "Знамёна у цитадели", 3, 1, 1, "Раненые у цитадели и в ней выздоравливают вдвое быстрее.", "gambesons"),
        L("mail", "Дружина", "Кольчуги", 5, 2, 0, "Лучники и мечники получают на пятую часть меньше урона.", "drill"),
        L("sergeants", "Дружина", "Десятники", 4, 2, 1, "В ближнем бою лучники и мечники бьют на пятую часть сильнее.", "banners"),
        L("veterans", "Дружина", "Ветераны", 6, 3, .5f, "Раненые бойцы держатся в строю, пока здоровья больше пятой части, а не трети.", "mail", "sergeants"),
        L("spikes", "Ловушки", "Острые колья", 2, 0, .5f, "Колья ранят на 60 % сильнее."),
        L("tar", "Ловушки", "Густая смола", 3, 1, 0, "Смола горит в полтора раза дольше.", "spikes"),
        L("soaked", "Ловушки", "Пропитанные стрелы", 3, 1, 1, "Огненные стрелы поджигают чаще, а лучник с ними стреляет почти так же быстро, как простыми.", "spikes"),
        L("naphtha", "Ловушки", "Горючая смесь", 5, 2, 0, "Бочки с порохом рвут на треть шире.", "tar"),
        L("firestorm", "Ловушки", "Огненный ливень", 4, 2, 1, "Налётчик, в которого попала огненная стрела, горит вдвое дольше, и земля под ним занимается чаще.", "soaked"),
        L("deathtraps", "Ловушки", "Ловчая наука", 6, 3, .5f, "Колья ранят ещё на треть сильнее и вытаптываются вдвое реже, волчья яма калечит вдвое сильнее.", "naphtha", "firestorm"),
    };

    public static readonly string[] Branches = { "Луки", "Кладка", "Труд", "Дружина", "Ловушки" };

    /// <summary>Knowledge to spend on lore.</summary>
    public double Knowledge;
    public readonly HashSet<string> Learnt = new();
    public bool Knows(string key) => Learnt.Contains(key);

    public static Lore LoreOf(string key) => Array.Find(Lores, l => l.Key == key);

    /// <summary>Whether all a lore rests on is known.</summary>
    public bool Opened(Lore l) => Array.TrueForAll(l.Needs, Knows);

    /// <summary>Whether a lore can be learnt now: not known yet, all it rests on known, the knowledge for it at hand.</summary>
    public bool CanLearn(Lore l) => !Knows(l.Key) && Opened(l) && Knowledge >= l.Cost;

    /// <summary>Learns a lore, spending the knowledge for it; what it does to those already in the household is done at once.</summary>
    public bool Learn(string key)
    {
        var l = LoreOf(key);
        if (l == null || !CanLearn(l)) return false;
        Knowledge -= l.Cost;
        Learnt.Add(key);
        if (key == "gambesons")
            foreach (var u in Units)
                if (!u.Dead && Soldier(u)) { u.MaxHp *= 1.2f; u.Hp *= 1.2f; }
        Say($"Изучено: «{l.Name}». {l.About}", 1);
        return true;
    }

    /// <summary>A wave beaten back: gold and knowledge for it, more the later the wave.</summary>
    internal void Reward(int wave)
    {
        double gold = 30 + 10 * wave;
        int know = 2 + wave / 2;
        Stock.Gold += gold;
        Knowledge += know;
        Say($"Награда за волну: {gold:0} золота и {know} {Plural(know, "знание", "знания", "знаний")} — их тратят во вкладке «Знания».", 1);
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
}
