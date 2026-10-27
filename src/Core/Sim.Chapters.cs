using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>
/// The siege in four chapters of three nights: goblins, iron, sorcery, the great horde. On the third night of each
/// chapter its champion comes with a guard of his own — Gnilozub the orc, Kamnelom the troll, Mor the necromancer —
/// and on the twelfth the horde's chief himself, Garm: half again as hard to kill as any leader thrice over (Garm six
/// times), harder-hitting, never breaking. A champion killed opens the next tier of the lore at once (else it opens a
/// night later) and leaves his trophy: the dawn after offers three of the legendary ones, each changing how the siege
/// goes, one to take. One who got away comes back the next night as he went. Garm killed, the whole horde breaks and
/// runs. All of it a siege of one's own: the tutorial and the bench, unless they ask, go without.
/// </summary>
public sealed partial class Sim
{
    /// <summary>A siege of one's own (not the tutorial, a staged scene or the bench).</summary>
    public bool OwnSiege => !NoErrands;

    public static int ChapterOf(int night) => Math.Clamp((night - 1) / 3 + 1, 1, 4);
    public static readonly string[] ChapterNames = { "", "Гоблины", "Железо", "Колдовство", "Великая орда" };
    public static readonly string[] ChapterAbout =
    {
        "",
        "Гоблины и сапёры. На третью ночь первый таран и вожак главы.",
        "Орки, шаманы, камнемёты, сапёры с порохом и осадная башня.",
        "Ворожеи гасят огни, некроманты поднимают павших, машин вдвое больше.",
        "Вся орда с троллями. В последнюю ночь приходит Вождь.",
    };
    public static string Roman(int c) => c switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", _ => "" };

    /// <summary>Each chapter's champion: his name and its genitive, what he is, and his guard.</summary>
    public static (string title, string of, UnitType kind, UnitType[] guard) Champion(int chapter) => chapter switch
    {
        1 => ("Гнилозуб", "Гнилозуба", Kinds.Orc, new[] { Kinds.Orc, Kinds.Orc, Kinds.Goblin, Kinds.Goblin }),
        2 => ("Камнелом", "Камнелома", Kinds.Troll, new[] { Kinds.Orc, Kinds.Orc, Kinds.Orc, Kinds.Sapper }),
        // (the necromancer of before is of the dark faith, a draft now: the third is a shaman of the swamps)
        3 => ("Тинник", "Тинника", Kinds.Shaman, new[] { Kinds.Orc, Kinds.Orc, Kinds.Goblin, Kinds.Shaman }),
        _ => ("Гарм", "Гарма", Kinds.Orc, new[] { Kinds.Orc, Kinds.Orc, Kinds.Orc, Kinds.Orc, Kinds.Troll, Kinds.Troll }),
    };

    /// <summary>The chapters whose champion has been killed.</summary>
    public readonly HashSet<int> ChampionsKilled = new();
    /// <summary>The highest tier of the lore open to learn in a siege of one's own.</summary>
    public int TierOpen = 1;
    /// <summary>The legendary trophies taken.</summary>
    public readonly List<string> Relics = new();
    public bool HasRelic(string key) => Relics.Contains(key);
    // a champion killed this night: his trophies the dawn's gifts
    int _nightChampion;

    /// <summary>The champion's band for a chapter's third night (Garm's for the twelfth), or null if it is not one.</summary>
    Warband ChampionBand(int night)
    {
        if (!OwnSiege || night % 3 != 0 || night > RaidsInAll) return null;
        int c = ChapterOf(night);
        if (ChampionsKilled.Contains(c)) return null;
        var (title, of, kind, guard) = Champion(c);
        var b = new Warband { Id = _nextBand++, State = BandState.Coming, Champion = (byte)c, Title = title };
        b.Roster.Add(kind);
        b.Roster.AddRange(guard);
        var (ex, ez) = Site.Hex == null ? (Site.EntryX, Site.EntryZ(World.SZ / 2)) : Site.Entry(.5);
        b.Entry = NearestStand(ex, ez, 3);
        b.Name = c == 4 ? $"Дружина Вождя {of}" : $"Дружина {of}";
        return b;
    }

    /// <summary>A champion takes the lead of his band: crowned, three (Garm six) times as hard to kill again.</summary>
    void Crown(Unit u, byte chapter, string title)
    {
        u.Champion = chapter;
        u.Title = title;
        float k = chapter == 4 ? 6 : 3;
        u.MaxHp *= k;
        u.Hp = u.MaxHp;
        u.Leader = true;
        // he leads as a warlord does, with his cry and his blow, but as hard to kill as a champion is (Sim.Warlords.cs)
        var lord = u.Lord ?? new Warlord { Id = _nextLord++, Name = title, Of = title, Kind = u.T, Level = chapter == 4 ? Warlord.MaxLevel : chapter + 1, Champion = true };
        if (u.Band != null) { u.Band.Lord = lord; u.Band.LordsOwn = true; }
        Ennoble(u, lord);
    }

    /// <summary>A champion's blow: a third (Garm's half) again as hard.</summary>
    static float ChampionArm(Unit a) => a.Champion == 4 ? 1.5f : a.Champion > 0 ? 1.3f : 1;

    /// <summary>A champion killed: said, counted; Garm's fall breaks the whole horde.</summary>
    void ChampionFell(Unit u)
    {
        ChampionsKilled.Add(u.Champion);
        if (Raids.Active) _nightChampion = u.Champion;
        if (u.Champion == 4)
        {
            foreach (var o in Units) if (!o.Dead && o.Team == Team.Horde && !o.Bound && !Kinds.IsEngine(o.T)) o.Routed = true;
            Say($"Вождь орды {u.Title} убит. Орда бежит.", 1, u);
        }
        else Say($"{u.Title}, вожак главы {Roman(u.Champion)}, убит. Трофей можно будет выбрать на рассвете.", 1, u);
    }

    /// <summary>At dawn: the lore's next tier opens with a champion killed, or a night after his chapter's third night.</summary>
    void OpenTiers(int night)
    {
        // a night after the champion's night the tier opens anyway
        int open = Math.Max(TierOpen, (night - 1) / 3 + 1);
        foreach (int c in ChampionsKilled) open = Math.Max(open, c + 1);
        open = Math.Min(open, 4);
        if (open > TierOpen && OwnSiege) Say($"Открыт ярус знаний {Roman(open)}.", 1);
        TierOpen = open;
    }

    // ---------------------------------------------------------------- the legendary trophies

    static readonly (string key, string title, string about)[] RelicPool =
    {
        ("jaw", "Челюсть вожака на воротах", "Налётчики убегают от ран на 25 % раньше."),
        ("hide", "Шкура тролля на стенах", "Орда ломает каменную кладку на 33 % дольше."),
        ("drum", "Боевой барабан", "Наши бойцы бьют и стреляют на 17 % чаще."),
        ("horn", "Рог вожака", "За убитых налётчиков на 50 % больше золота."),
        ("eye", "Око шамана", "Ночью лучники стреляют на полную дальность даже в темноте."),
        ("staff", "Посох колдуна", "Заклинания на 33 % дешевле."),
        ("shield", "Щит вожака над цитаделью", "Цитадель получает от орды вдвое меньше урона."),
    };

    /// <summary>Three of the legendary trophies not yet taken, for the dawn after a champion fell.</summary>
    List<Gift> RelicGifts(int night)
    {
        var can = new List<(string key, string title, string about)>();
        foreach (var r in RelicPool) if (!HasRelic(r.key)) can.Add(r);
        // three of them in an order of the dawn's own
        var order = new List<(double k, int i)>();
        for (int i = 0; i < can.Count; i++) order.Add((DawnDraw(night, 31 + i * 7), i));
        order.Sort((a, b) => a.k.CompareTo(b.k));
        var gifts = new List<Gift>();
        for (int j = 0; j < Math.Min(3, order.Count); j++)
        {
            var r = can[order[j].i];
            gifts.Add(new Gift { Kind = GiftKind.Relic, Key = r.key, Title = r.title, About = r.about });
        }
        return gifts;
    }

    /// <summary>What the trophies do, where it is read.</summary>
    float FearOfUs => HasRelic("jaw") ? .75f : 1;
    float HideHack => HasRelic("hide") ? .7f : 1;
    float Drum => HasRelic("drum") ? 1.17f : 1;
    float KeepHurt => Sandbox ? 0 : HordeHarm * (HasRelic("shield") ? .5f : 1);

    // ---------------------------------------------------------------- saved

    void WriteChapters(BinaryWriter w)
    {
        w.Write(TierOpen); w.Write(_nightChampion);
        w.Write(ChampionsKilled.Count); foreach (int c in ChampionsKilled) w.Write(c);
        w.Write(Relics.Count); foreach (var r in Relics) w.Write(r);
    }

    void ReadChapters(BinaryReader r)
    {
        TierOpen = r.ReadInt32(); _nightChampion = r.ReadInt32();
        ChampionsKilled.Clear(); for (int i = r.ReadInt32(); i > 0; i--) ChampionsKilled.Add(r.ReadInt32());
        Relics.Clear(); for (int i = r.ReadInt32(); i > 0; i--) Relics.Add(r.ReadString());
    }
}
