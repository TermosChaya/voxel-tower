using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>
/// What a commander of ours can be good at, as a hero of Heroes of Might and Magic is: each learnt in three grades, one
/// grade a level (<see cref="Hero"/>, Sim.Heroes.cs).
/// </summary>
public enum Skill : byte { Line, Archers, March, Healer, Tactics, Word, Scouting, Builder }

/// <summary>
/// A commander of ours as the campaign's hero: his name, his level from 1 to 10 and the experience that brings him on,
/// the grades of his skills, the levels he has risen and not yet chosen a skill for and the skills offered for the next,
/// and those he struck down himself. He goes with his commander from valley to valley (<see cref="Traveller"/>) and is
/// lost with him.
/// </summary>
public sealed class Hero
{
    public string Name = "";
    public int Level = 1;
    /// <summary>All the experience he has had, from the first.</summary>
    public int Xp;
    /// <summary>The grade of each skill, 0 (not learnt) to <see cref="Heroes.Grades"/>.</summary>
    public readonly byte[] Grade = new byte[Heroes.Count];
    /// <summary>Levels risen with no skill chosen for them yet.</summary>
    public int Unspent;
    /// <summary>The skills offered for the first of them; empty when there is nothing to choose.</summary>
    public readonly List<Skill> Offer = new();
    /// <summary>Those of the horde he struck down himself.</summary>
    public int Kills;
    /// <summary>What his offers are drawn by: the same hero is offered the same skills at the same level.</summary>
    public int Seed;

    public byte this[Skill s] => Grade[(int)s];
    public bool Pending => Unspent > 0 && Offer.Count > 0;
    /// <summary>The experience his level began at, and the next level's (the last level's twice over when there is none).</summary>
    public int From => Heroes.XpFor(Level);
    public int Next => Level >= Heroes.MaxLevel ? Heroes.XpFor(Heroes.MaxLevel) : Heroes.XpFor(Level + 1);
    /// <summary>«Ратибор Седой, ур. 3».</summary>
    public override string ToString() => $"{Name}, ур. {Level}";
}

/// <summary>
/// The heroes' rules, without a valley: the names, the skills and what each grade does, the experience each level wants,
/// the skills offered at a level and the one chosen, a hero's worth in a battle counted at once, and his record in a
/// save. The valley's side of it — who earns what, where each skill bites — is Sim.Heroes.cs.
/// </summary>
public static class Heroes
{
    public const int Count = 8, Grades = 3, MaxLevel = 10;

    /// <summary>The experience a hero has had by the time he reaches each level (index by the level; 1 is where he starts).</summary>
    static readonly int[] XpTable = { 0, 0, 100, 250, 450, 700, 1000, 1400, 1900, 2500, 3200 };
    public static int XpFor(int level) => XpTable[Math.Clamp(level, 1, MaxLevel)];

    public static readonly string[] Names = { "Строй", "Стрелки", "Поход", "Лекарь", "Тактика", "Слово", "Разведка", "Зодчий" };
    public static string NameOf(Skill s) => Names[(int)s];

    /// <summary>What a skill does, grade by grade, for its tooltip and the level's choice: the same numbers the valley and the map use.</summary>
    public static string About(Skill s, int grade)
    {
        int g = Math.Clamp(grade, 1, Grades);
        return s switch
        {
            Skill.Line => $"Бойцы в его 40 м получают на {Pct(LineGuard * g)} меньше урона.",
            Skill.Archers => $"Стрелки в его 40 м бьют на {Pct(BowReach * g)} дальше и стреляют на {Pct(BowPace * g)} чаще.",
            Skill.March => $"Его отряд идёт по карте на {Pct(MarchPace * g)} быстрее.",
            Skill.Healer => $"Раненые в его 40 м и в его отряде заживают на {Pct(Mending * g)} быстрее.",
            Skill.Tactics => $"«Залп!» бьёт на {Pct(VolleyHarm * g)} сильнее, идущие за ним по «За мной!» бьют на {Pct(ChargeArm * g)} сильнее.",
            Skill.Word => $"Его слово и призывы достают на {Pct(WordReach * g)} дальше, ободрённые бьют на {Pct(WordArm * g)} сильнее.",
            Skill.Scouting => $"Видит засаду за {ScoutMetres(g)} м. Его отряд видит на карте на {g} {Sim.Plural(g, "долину", "долины", "долин")} дальше.",
            Skill.Builder => $"Рабочие в его 40 м строят и копают на {Pct(BuildPace * g)} быстрее, застава с его отрядом встаёт на {Pct(OutpostPace * g)} быстрее.",
            _ => "",
        };
    }

    static string Pct(float share) => $"{Math.Round(share * 100)} %";

    // ------------------------------------------------------------ what each grade does

    /// <summary>Of a blow at one of ours under a commander with Строй: so much less for each grade.</summary>
    public const float LineGuard = .06f;
    /// <summary>A bow's reach and its pace with Стрелки, each grade.</summary>
    public const float BowReach = .08f, BowPace = .05f;
    /// <summary>A party's pace on the map with Поход, each grade.</summary>
    public const float MarchPace = .1f;
    /// <summary>The mending of the hurt with Лекарь, each grade; and a dawn's mending of his party on the march, a share of health.</summary>
    public const float Mending = .3f, MarchMend = .08f;
    /// <summary>A volley's blow and the blows of those following him with Тактика, each grade.</summary>
    public const float VolleyHarm = .2f, ChargeArm = .1f;
    /// <summary>His word's reach and the heartening of those he rallies with Слово, each grade.</summary>
    public const float WordReach = .15f, WordArm = .05f;
    /// <summary>The workers' hands about him and an outpost's raising by his party with Зодчий, each grade.</summary>
    public const float BuildPace = .1f, OutpostPace = .25f;
    /// <summary>The experience of a battle counted at once won, and for each of the beaten fallen; of a valley taken.</summary>
    public const int WonXp = 25, FoeXp = 5, TakeXp = 150;
    /// <summary>How far, metres, a commander with Разведка sees a band lying in wait.</summary>
    public static int ScoutMetres(int grade) => 10 + 10 * Math.Clamp(grade, 0, Grades);

    /// <summary>The share a battle counted at once is steadied by a hero: a little for each level, more for his fighting skills.</summary>
    public static float Steady(Hero h, bool archers)
    {
        if (h == null) return 0;
        return .01f * (h.Level - 1) + .03f * h[Skill.Line] + .04f * h[Skill.Tactics] + .02f * h[Skill.Word] + (archers ? .03f * h[Skill.Archers] : 0);
    }

    /// <summary>A party's pace on the map with its commander's Поход: below 1 is faster.</summary>
    public static double March(Hero h) => h == null ? 1 : 1 - MarchPace * h[Skill.March];

    // ------------------------------------------------------------ the names

    static readonly string[] Given =
    {
        "Ратибор", "Всеслав", "Мирослав", "Велимир", "Твердислав", "Любомир", "Остромир", "Будимир", "Радим", "Светозар",
        "Ждан", "Милован", "Тихомир", "Добромир", "Станимир", "Борислав", "Влас", "Горазд", "Яромир", "Ведислав",
        "Хотен", "Нежата", "Путята", "Смеян", "Боян", "Вышата", "Гордей", "Зорян", "Лучезар", "Островид",
    };
    static readonly string[] Byname =
    {
        "Седой", "Хромец", "Рыжий", "Молчун", "Беспалый", "Крепкорукий", "Сокол", "Тур", "Волчий Хвост", "Кривой",
        "Бородач", "Ворон", "Тихий", "Медведь", "Безбородый", "Пёстрый", "Гусляр", "Кремень",
    };

    /// <summary>
    /// A new hero at level 1, his name drawn by the number given (a commander's, a valley's) and not one of those taken
    /// if it can be helped.
    /// </summary>
    public static Hero Fresh(int seed, ICollection<string> taken = null)
    {
        string name = "";
        for (int k = 0; k < 12; k++)
        {
            int g = (int)(Hash.H3(seed, 41 + k, 7) * Given.Length), b = (int)(Hash.H3(seed, 43 + k, 11) * Byname.Length);
            name = $"{Given[g]} {Byname[b]}";
            if (taken == null || !taken.Contains(name)) break;
        }
        return new Hero { Name = name, Seed = seed };
    }

    // ------------------------------------------------------------ the levels

    /// <summary>Experience given a hero: the levels it brings him to, each with a skill to choose. How many he rose.</summary>
    public static int Gain(Hero h, int xp)
    {
        if (h == null || xp <= 0) return 0;
        h.Xp += xp;
        int rose = 0;
        while (h.Level < MaxLevel && h.Xp >= XpFor(h.Level + 1)) { h.Level++; h.Unspent++; rose++; }
        if (rose > 0 && h.Offer.Count == 0) MakeOffer(h);
        return rose;
    }

    /// <summary>
    /// The skills offered for the next level chosen: three if there are as many left to learn, one of them a further
    /// grade of a skill he has, the rest new ones; the same hero at the same level is offered the same.
    /// </summary>
    public static void MakeOffer(Hero h)
    {
        h.Offer.Clear();
        if (h.Unspent <= 0) return;
        int chosen = h.Level - h.Unspent;
        var known = new List<Skill>(); var fresh = new List<Skill>();
        for (int i = 0; i < Count; i++)
        {
            if (h.Grade[i] >= Grades) continue;
            (h.Grade[i] > 0 ? known : fresh).Add((Skill)i);
        }
        int salt = 0;
        Skill Draw(List<Skill> from)
        {
            var s = from[(int)(Hash.H3(h.Seed, chosen * 13 + salt++, 29) * from.Count) % from.Count];
            from.Remove(s);
            return s;
        }
        if (known.Count > 0) h.Offer.Add(Draw(known));
        while (h.Offer.Count < 3 && fresh.Count > 0) h.Offer.Add(Draw(fresh));
        while (h.Offer.Count < 3 && known.Count > 0) h.Offer.Add(Draw(known));
    }

    /// <summary>One of the offered skills chosen: a grade more of it, and the next offer made if more levels wait. False if it was not offered.</summary>
    public static bool Choose(Hero h, Skill s)
    {
        if (h == null || h.Unspent <= 0 || !h.Offer.Contains(s) || h.Grade[(int)s] >= Grades) return false;
        h.Grade[(int)s]++;
        h.Unspent--;
        MakeOffer(h);
        return true;
    }

    /// <summary>«Строй II, Стрелки I»: the skills he has, or «нет умений».</summary>
    public static string Skills(Hero h)
    {
        var list = new List<string>();
        for (int i = 0; i < Count; i++) if (h.Grade[i] > 0) list.Add($"{Names[i]} {Sim.Roman(h.Grade[i])}");
        return list.Count == 0 ? "нет умений" : string.Join(", ", list);
    }

    // ------------------------------------------------------------ the record

    public static void Write(BinaryWriter w, Hero h)
    {
        w.Write(h != null);
        if (h == null) return;
        w.Write(h.Name); w.Write(h.Level); w.Write(h.Xp); w.Write(h.Grade); w.Write(h.Unspent); w.Write(h.Kills); w.Write(h.Seed);
        w.Write((byte)h.Offer.Count);
        foreach (var s in h.Offer) w.Write((byte)s);
    }

    public static Hero Read(BinaryReader r)
    {
        if (!r.ReadBoolean()) return null;
        var h = new Hero { Name = r.ReadString(), Level = r.ReadInt32(), Xp = r.ReadInt32() };
        r.ReadBytes(Count).CopyTo(h.Grade, 0);
        h.Unspent = r.ReadInt32(); h.Kills = r.ReadInt32(); h.Seed = r.ReadInt32();
        for (int n = r.ReadByte(); n > 0; n--) h.Offer.Add((Skill)r.ReadByte());
        return h;
    }
}
