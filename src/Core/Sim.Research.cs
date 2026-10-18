using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>A thing the household can learn with what the beaten waves taught it: what it is, what it does, its price, what it rests on.</summary>
public sealed class Lore
{
    public string Key = "", Name = "", Branch = "", About = "";
    public int Cost;
    /// <summary>The lore it rests on, or null.</summary>
    public string Needs;
}

/// <summary>
/// What a wave beaten back gives: gold, and knowledge — what was learnt of the horde and of holding against it. The
/// knowledge goes on lore, in five branches of three, each of the later two resting on the first of its branch:
/// bows, masonry, labour, arms, traps and fire. What is learnt is learnt for good.
/// </summary>
public sealed partial class Sim
{
    public static readonly Lore[] Lores =
    {
        new() { Key = "yew", Branch = "Луки", Name = "Тисовые луки", Cost = 2, About = "Лучники бьют на 15 % дальше." },
        new() { Key = "barbs", Branch = "Луки", Name = "Гранёные наконечники", Cost = 3, Needs = "yew", About = "Стрелы ранят на четверть сильнее." },
        new() { Key = "braziers", Branch = "Луки", Name = "Жаровни на стенах", Cost = 3, Needs = "yew", About = "Факелы на стенах светят ночью на 9 м вместо 6: лучники видят врага у стен дальше." },
        new() { Key = "mortar", Branch = "Кладка", Name = "Известковый раствор", Cost = 2, About = "Орда рубит и ломает камень на треть дольше." },
        new() { Key = "masons", Branch = "Кладка", Name = "Артель каменщиков", Cost = 3, Needs = "mortar", About = "Кладут и строят на четверть быстрее." },
        new() { Key = "ironbound", Branch = "Кладка", Name = "Окованные ворота", Cost = 3, Needs = "mortar", About = "Ворота и подъёмные мосты выдерживают вдвое больше ударов, прежде чем их прорубят." },
        new() { Key = "picks", Branch = "Труд", Name = "Добрые кирки", Cost = 2, About = "Копают, рубят и ломают камень на четверть быстрее." },
        new() { Key = "barrows", Branch = "Труд", Name = "Тачки", Cost = 3, Needs = "picks", About = "Добытого в запасы приходит на пятую часть больше." },
        new() { Key = "buckets", Branch = "Труд", Name = "Пожарные вёдра", Cost = 2, Needs = "picks", About = "Огонь тушат вдвое быстрее." },
        new() { Key = "gambesons", Branch = "Дружина", Name = "Стёганки", Cost = 2, About = "Лучники и мечники крепче на пятую часть." },
        new() { Key = "drill", Branch = "Дружина", Name = "Учебный двор", Cost = 3, Needs = "gambesons", About = "Наём на пятую часть дешевле." },
        new() { Key = "banners", Branch = "Дружина", Name = "Знамёна у цитадели", Cost = 3, Needs = "gambesons", About = "Раненые у цитадели и в ней выздоравливают вдвое быстрее." },
        new() { Key = "spikes", Branch = "Ловушки", Name = "Острые колья", Cost = 2, About = "Колья ранят на 60 % сильнее." },
        new() { Key = "tar", Branch = "Ловушки", Name = "Густая смола", Cost = 3, Needs = "spikes", About = "Смола горит в полтора раза дольше." },
        new() { Key = "soaked", Branch = "Ловушки", Name = "Пропитанные стрелы", Cost = 3, Needs = "spikes", About = "Огненные стрелы поджигают чаще, а лучник с ними стреляет почти так же быстро, как простыми." },
    };

    public static readonly string[] Branches = { "Луки", "Кладка", "Труд", "Дружина", "Ловушки" };

    /// <summary>Knowledge to spend on lore.</summary>
    public double Knowledge;
    public readonly HashSet<string> Learnt = new();
    public bool Knows(string key) => Learnt.Contains(key);

    public static Lore LoreOf(string key) => Array.Find(Lores, l => l.Key == key);

    /// <summary>Whether a lore can be learnt now: not known yet, what it rests on known, the knowledge for it at hand.</summary>
    public bool CanLearn(Lore l) => !Knows(l.Key) && (l.Needs == null || Knows(l.Needs)) && Knowledge >= l.Cost;

    /// <summary>Learns a lore, spending the knowledge for it; what it does to those already in the household is done at once.</summary>
    public bool Learn(string key)
    {
        var l = LoreOf(key);
        if (l == null || !CanLearn(l)) return false;
        Knowledge -= l.Cost;
        Learnt.Add(key);
        if (key == "gambesons")
            foreach (var u in Units)
                if (!u.Dead && u.Team == Team.Player && u.T.Work < 1) { u.MaxHp *= 1.2f; u.Hp *= 1.2f; }
        Say($"Изучено: «{l.Name}». {l.About}", 1);
        return true;
    }

    /// <summary>A wave beaten back: gold and knowledge for it, more the later the wave.</summary>
    internal void Reward(int wave)
    {
        double gold = 30 + 10 * wave;
        int know = 2 + wave / 3;
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

    /// <summary>How far a bow carries by day: its own reach, and 15 % more with yew — ours only; what the household learns is its own.</summary>
    public float BowRange(Unit a) => a.T.Range * (a.Team == Team.Player && Knows("yew") ? 1.15f : 1);
    float ArrowHarm => Knows("barbs") ? 1.25f : 1;
    float TorchLight => (Knows("braziers") ? 9 : 6) * VoxelWorld.PerMetre;
    /// <summary>How much slower the horde hacks stone set in lime mortar.</summary>
    float StoneHack => Knows("mortar") ? .7f : 1;
    float LayPace => Knows("masons") ? 1.25f : 1;
    float DigPace => Knows("picks") ? 1.25f : 1;
    float YieldMore => Knows("barrows") ? 1.2f : 1;
    float BeatPace => Knows("buckets") ? 2 : 1;
    /// <summary>What hiring a kind costs now.</summary>
    public int HireCost(UnitType t) => (int)Math.Round(t.Cost * (Knows("drill") ? .8 : 1));
    float MendHome => Knows("banners") ? 2 : 1;
    float StakeHarm => Knows("spikes") ? 1.6f : 1;
    float PitchBurn => Knows("tar") ? 1.5f : 1;
}
