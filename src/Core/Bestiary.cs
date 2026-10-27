using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What a creature of the wild does besides biting and striking (Sim.Bestiary.cs).</summary>
[Flags]
public enum BeastPower : ushort
{
    None = 0,
    /// <summary>One set upon, the rest of its kind near it come too.</summary>
    Pack = 1,
    /// <summary>Throws webs that hold whoever they catch, slowed, for a while.</summary>
    Web = 2,
    /// <summary>Its bite or its sting leaves a venom that goes on hurting.</summary>
    Venom = 4,
    /// <summary>Its blow throws men off their feet and back.</summary>
    Knock = 8,
    /// <summary>A look that stiffens limbs to stone for a while.</summary>
    Gaze = 16,
    /// <summary>Drinks the life it strikes: half of a blow's harm comes back to it.</summary>
    Drain = 32,
    /// <summary>Breathes fire: a cone of it before it, burning men and setting the ground and the timber alight.</summary>
    Breath = 64,
    /// <summary>Burns of itself: what it strikes and what it walks over takes fire.</summary>
    Ablaze = 128,
    /// <summary>Tears up boulders and hurls them.</summary>
    Boulders = 256,
    /// <summary>Charges with its head down, the first blow of a charge doubled and throwing.</summary>
    Charge = 512,
    /// <summary>Throws balls of burning pitch.</summary>
    Firebolt = 1024,
}

/// <summary>
/// A breed of the bestiary: its kind, its tier (1 to 7, as the creatures of «Heroes»), what it does besides fighting,
/// where it is found (the grounds and the reliefs of the big map it lives on), what a stack of it is called, and how
/// it meets a party much stronger than itself: runs, or (the people and the proud) asks to go with it.
/// </summary>
public sealed class Breed
{
    public UnitType Kind;
    public int Tier;
    public BeastPower Powers;
    public HexGround[] Grounds = Array.Empty<HexGround>();
    public HexRelief[] Reliefs = Array.Empty<HexRelief>();
    /// <summary>A stack of it: «стая волков», and one alone: «волк»; the genitive plural for counts: «волков».</summary>
    public string Group, One, Many;
    /// <summary>The genitive of a few (2–4): «волка».</summary>
    public string Few;
    public bool Joins, Flees;
    /// <summary>Found only on its own grounds and reliefs (a treant in the woods, a fire spirit on the burnt land).</summary>
    public bool Bound;
    /// <summary>Only ever one or a few of them (a dragon).</summary>
    public int Most = 40;

    /// <summary>How much at home it is on a hex: twice on its ground, again on its relief.</summary>
    public int Fit(HexGround g, HexRelief r)
    {
        bool onGround = Array.IndexOf(Grounds, g) >= 0, onRelief = Array.IndexOf(Reliefs, r) >= 0;
        if (Bound && !onGround && !onRelief) return 0;
        return 1 + (onGround ? 2 : 0) + (onRelief ? 2 : 0);
    }
}

/// <summary>
/// The neutral creatures of the world, as the wandering monsters of «Heroes» (the author's word of 27.10.2026: «помимо
/// орды врагами должны выступать и нейтральные юниты — целое обилие монстров, зверей и прочего»): what a stack keeping
/// a hex of the big map is, by its zone's tier and the hex's ground; what it is called; whether it runs or asks to join.
/// </summary>
public static class Bestiary
{
    static readonly HexGround[] Green = { HexGround.Grass, HexGround.Dirt }, Wild = { HexGround.Rough, HexGround.Snow },
        Wet = { HexGround.Swamp }, Dry = { HexGround.Sand, HexGround.Dirt }, Burnt = { HexGround.Scorched, HexGround.Lava };
    static readonly HexRelief[] Wood = { HexRelief.Forest }, High = { HexRelief.Hills }, Open = { HexRelief.Flat };

    static HexGround[] G(params HexGround[][] sets) { var l = new List<HexGround>(); foreach (var s in sets) l.AddRange(s); return l.ToArray(); }

    public static readonly Breed[] All =
    {
        new() { Kind = Kinds.Wolf, Tier = 1, Powers = BeastPower.Pack, Grounds = G(Green, Wild), Reliefs = Wood, Group = "Стая волков", One = "волк", Few = "волка", Many = "волков", Flees = true },
        new() { Kind = Kinds.Bandit, Tier = 1, Grounds = G(Green, Dry), Reliefs = Wood, Group = "Шайка разбойников", One = "разбойник", Few = "разбойника", Many = "разбойников", Joins = true, Flees = true },
        new() { Kind = Kinds.Spider, Tier = 1, Powers = BeastPower.Web | BeastPower.Venom, Grounds = Wet, Reliefs = Wood, Group = "Выводок пауков", One = "паук", Few = "паука", Many = "пауков" },
        new() { Kind = Kinds.Skeleton, Tier = 2, Grounds = G(Dry, Burnt), Reliefs = Open, Group = "Отряд скелетов", One = "скелет", Few = "скелета", Many = "скелетов" },
        new() { Kind = Kinds.Warg, Tier = 2, Powers = BeastPower.Pack, Grounds = G(Wild, Burnt), Reliefs = High, Group = "Стая варгов", One = "варг", Few = "варга", Many = "варгов", Flees = true },
        new() { Kind = Kinds.Harpy, Tier = 2, Grounds = Wild, Reliefs = High, Group = "Стая гарпий", One = "гарпия", Few = "гарпии", Many = "гарпий", Flees = true },
        new() { Kind = Kinds.Cultist, Tier = 2, Powers = BeastPower.Firebolt, Grounds = G(Burnt, Dry), Reliefs = Open, Group = "Круг культистов", One = "культист", Few = "культиста", Many = "культистов", Joins = true },
        new() { Kind = Kinds.Bear, Tier = 3, Grounds = G(Green, Wild), Reliefs = new[] { HexRelief.Forest, HexRelief.Hills }, Group = "Медведи", One = "пещерный медведь", Few = "пещерных медведя", Many = "пещерных медведей" },
        new() { Kind = Kinds.Centaur, Tier = 3, Grounds = G(Green, Dry), Reliefs = Open, Group = "Табун кентавров", One = "кентавр", Few = "кентавра", Many = "кентавров", Joins = true, Flees = true },
        new() { Kind = Kinds.Ghoul, Tier = 3, Powers = BeastPower.Venom, Grounds = G(Wet, Burnt), Reliefs = Open, Group = "Упыри", One = "упырь", Few = "упыря", Many = "упырей" },
        new() { Kind = Kinds.Ogre, Tier = 4, Powers = BeastPower.Knock, Grounds = G(Dry, Wild), Reliefs = High, Group = "Огры", One = "огр", Few = "огра", Many = "огров", Joins = true },
        new() { Kind = Kinds.WildTroll, Tier = 4, Grounds = G(Wet, Wild), Reliefs = Wood, Group = "Лесные тролли", One = "лесной тролль", Few = "лесных тролля", Many = "лесных троллей" },
        new() { Kind = Kinds.Wraith, Tier = 4, Powers = BeastPower.Drain, Grounds = G(Wet, Burnt, Wild), Reliefs = Open, Group = "Призраки", One = "призрак", Few = "призрака", Many = "призраков" },
        new() { Kind = Kinds.Minotaur, Tier = 5, Powers = BeastPower.Charge | BeastPower.Knock, Grounds = G(Dry, Wild), Reliefs = Open, Group = "Минотавры", One = "минотавр", Few = "минотавра", Many = "минотавров", Joins = true },
        new() { Kind = Kinds.Griffin, Tier = 5, Grounds = Wild, Reliefs = High, Group = "Грифоны", One = "грифон", Few = "грифона", Many = "грифонов", Flees = true },
        new() { Kind = Kinds.Basilisk, Tier = 5, Powers = BeastPower.Gaze, Grounds = G(Wet, Dry), Reliefs = Open, Group = "Василиски", One = "василиск", Few = "василиска", Many = "василисков" },
        new() { Kind = Kinds.Treant, Tier = 6, Reliefs = Wood, Bound = true, Group = "Энты", One = "энт", Few = "энта", Many = "энтов", Most = 12 },
        new() { Kind = Kinds.Golem, Tier = 6, Powers = BeastPower.Boulders, Grounds = Wild, Reliefs = High, Group = "Каменные големы", One = "каменный голем", Few = "каменных голема", Many = "каменных големов", Most = 12 },
        new() { Kind = Kinds.FireSpirit, Tier = 6, Powers = BeastPower.Ablaze, Grounds = Burnt, Bound = true, Group = "Огненные духи", One = "огненный дух", Few = "огненных духа", Many = "огненных духов", Most = 12 },
        new() { Kind = Kinds.Wyvern, Tier = 6, Powers = BeastPower.Venom, Grounds = G(Wet, Wild), Reliefs = High, Group = "Виверны", One = "виверна", Few = "виверны", Many = "виверн", Most = 10 },
        new() { Kind = Kinds.Dragon, Tier = 7, Powers = BeastPower.Breath, Grounds = G(Wild, Burnt), Reliefs = High, Group = "Дракон", One = "дракон", Few = "дракона", Many = "драконов", Most = 3 },
    };

    static readonly Dictionary<UnitType, Breed> ByKind = new();
    static Bestiary() { foreach (var b in All) ByKind[b.Kind] = b; }

    /// <summary>The breed of a kind, null for one that is no creature of the wild (the horde's, ours).</summary>
    public static Breed Of(UnitType t) => t != null && ByKind.TryGetValue(t, out var b) ? b : null;

    public static bool Has(UnitType t, BeastPower p) => Of(t) is { } b && (b.Powers & p) != 0;

    /// <summary>
    /// How strong one of a kind stands in a quick battle (QuickBattle.Strength of one alone): a stack of n of one kind is n
    /// times that.
    /// </summary>
    public static float Power(UnitType t) => MathF.Sqrt(t.Hp * (t.AttackTime > 0 ? t.Damage / t.AttackTime : 0) * (t.Ranged ? 1.2f : 1));

    /// <summary>
    /// The strength a stack of a zone of a tier comes to (the tier of the map: 0 next to our own zone, more for each
    /// pass further out and at the passes into the horde's realms): growing two thirds again a tier.
    /// </summary>
    public static float Budget(int tier) => 120 * MathF.Pow(1.65f, Math.Clamp(tier, 0, 8));

    /// <summary>The creatures' tiers a map's tier keeps: about its own and one either side, the seventh only far out.</summary>
    static bool Fits(Breed b, int tier) => b.Tier == 7 ? tier >= 5 : Math.Abs(b.Tier - (tier + 1)) <= 1;

    /// <summary>
    /// A stack keeping a hex: one breed, weighted by how much at home it is on the hex's ground and relief among those
    /// of about the tier, so many of them as the tier's strength comes to, give or take a fifth. Three rolls, always.
    /// </summary>
    public static List<UnitType> Stack(int tier, HexGround ground, HexRelief relief, Func<double> roll)
    {
        double pick = roll(), size = roll(), dragon = roll();
        var fit = new List<(Breed b, int w)>();
        int sum = 0;
        foreach (var b in All)
        {
            if (!Fits(b, tier)) continue;
            // a dragon one in seven of the stacks far out, where the map's tier allows it at all
            // (squared: a breed at home on the hex is many times likelier than one that is not)
            int home = b.Fit(ground, relief), w = b.Tier == 7 ? (dragon < 1 / 7.0 ? 40 : 0) : home * home;
            if (w <= 0) continue;
            fit.Add((b, w)); sum += w;
        }
        var list = new List<UnitType>();
        if (sum == 0) return list;
        double at = pick * sum;
        Breed chosen = fit[^1].b;
        foreach (var (b, w) in fit) { if (at < w) { chosen = b; break; } at -= w; }
        int n = (int)Math.Round(Budget(tier) * (.8 + .4 * size) / Power(chosen.Kind));
        n = Math.Clamp(n, 1, chosen.Most);
        for (int i = 0; i < n; i++) list.Add(chosen.Kind);
        return list;
    }

    /// <summary>A count of a breed as said: «12 волков», «1 волк», «3 волка».</summary>
    public static string Count(Breed b, int n) => $"{n} {Plural(n, b.One, b.Few, b.Many)}";

    /// <summary>
    /// What keeps a hex, as the map's hover says it: «Стая волков: 12»; mixed, the breeds in turn: «Медведи: 2, стая
    /// волков: 4»; the horde's and ours by their own names.
    /// </summary>
    public static string Label(IReadOnlyList<Traveller> keep)
    {
        if (keep == null || keep.Count == 0) return "";
        var counts = new List<(UnitType t, int n)>();
        foreach (var m in keep)
        {
            int i = counts.FindIndex(c => c.t == m.Kind);
            if (i < 0) counts.Add((m.Kind, 1)); else counts[i] = (m.Kind, counts[i].n + 1);
        }
        var parts = new List<string>();
        foreach (var (t, n) in counts)
        {
            var b = Of(t);
            string name = b == null ? t.Name : n == 1 ? Cap(b.One) : b.Group;
            parts.Add(parts.Count == 0 ? $"{name}: {n}" : $"{name.ToLowerInvariant()}: {n}");
        }
        return string.Join(", ", parts);
    }

    /// <summary>The same as what a blow falls on, for what is said of a fight: «стаю волков (12)».</summary>
    public static string Accusative(IReadOnlyList<Traveller> keep)
    {
        if (keep == null || keep.Count == 0) return "зверей";
        var b = Of(keep[0].Kind);
        if (b == null) return "зверей";
        if (keep.Count == 1) return b.One;
        // «стая» → «стаю», «шайка» → «шайку»; the plural names stay as they are
        string g = b.Group.ToLowerInvariant(), head = g.Split(' ')[0];
        if (head.EndsWith('а')) g = head[..^1] + "у" + g[head.Length..];
        else if (head.EndsWith('я')) g = head[..^1] + "ю" + g[head.Length..];
        return $"{g} ({keep.Count})";
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        return m10 == 1 && m100 != 11 ? one : m10 is >= 2 and <= 4 && (m100 < 12 || m100 > 14) ? few : many;
    }
}
