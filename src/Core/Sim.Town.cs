using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What stands inside a town building and makes it that building: a forge and an anvil, targets, stalls…</summary>
public enum Prop : byte { Anvil, Forge, Rack, Target, Stall, Workbench, Counter, Desk, Bunk, Still, Hay, Casks, Crates, Kennel, Seat, Blocks, Ingots }

/// <summary>
/// A kind of town building (docs/handoff/08-town.md): what it is called and what it is for, the lore that opens it and
/// the buildings it wants standing first, the house it goes up as (its outer size in voxels and the walls' material),
/// the gold the masters take for it and for its second storey, what it hires at each level and how many grow in ten
/// minutes, what each level gives (the keys of what were the lore's improvements), its income, and what stands in it.
/// </summary>
public sealed class TownKind
{
    public string Key = "", Name = "", About = "", Lore = "", Level2 = "";
    /// <summary>The house it goes up as: the outer size across and along, voxels (across no more than 38: a house's floor is no wider).</summary>
    public int W = 37, D = 57;
    public byte Wall = Mat.Stone;
    public int MaxLevel = 2;
    /// <summary>The masters' gold for building it, and for raising its second storey.</summary>
    public int Gold, UpGold;
    /// <summary>What it hires: the kind, from which level, and how many of it grow in ten minutes.</summary>
    public (UnitType kind, int level, int growth)[] Hires = Array.Empty<(UnitType, int, int)>();
    /// <summary>What each level gives: the keys <see cref="Sim.Knows"/> answers to.</summary>
    public string[] Grants1 = Array.Empty<string>(), Grants2 = Array.Empty<string>();
    /// <summary>Buildings that must stand first, each at a level.</summary>
    public (string key, int level)[] Needs = Array.Empty<(string, int)>();
    /// <summary>Gold a minute at each level, before what the settlement's size adds.</summary>
    public float Income1, Income2;
    /// <summary>Metal a minute at each level, cubic metres (the miners' building), and whether it stands only on an outcrop of ore.</summary>
    public float Metal1, Metal2;
    public bool OnOre;
    /// <summary>Metal its iron asks, cubic metres, for building it and for its second storey.</summary>
    public float MetalCost, UpMetal;
    public Prop[] Props1 = Array.Empty<Prop>(), Props2 = Array.Empty<Prop>();
}

/// <summary>
/// A town building of ours on the ground: its kind, the rectangle of its walls (the dwelling zone it stands as), its
/// level and the level it is going up to, whether it stands closed and furnished and so works, the top of its first
/// walls (to tell when a second storey has gone up), and how many of each kind it hires wait to be taken on.
/// </summary>
public sealed class Building
{
    public string Kind = "";
    public int X0, Z0, X1, Z1;
    /// <summary>The level it works at (0 while it first goes up), and the one it is being raised to.</summary>
    public int Level, Target = 1;
    public bool Ready;
    public int Top0;
    public readonly Dictionary<string, float> Stock = new();
    public TownKind K => Sim.TownKindOf(Kind);
    public float CX => (X0 + X1) / 2f;
    public float CZ => (Z0 + Z1) / 2f;
}

/// <summary>
/// The town (docs/handoff/08-town.md). As in Heroes of Might and Magic, what the household can become rests on its
/// buildings: the lore opens the right to put one up, it is put up from a ready plan — a house with its floor, its
/// roof and its door, and inside what makes it that building — by the workers, and once it stands closed and
/// furnished it works: it hires its kinds from a stock that grows in ten minutes, and its levels (a second storey
/// raised on it) give what the lore's improvements used to give, under the same keys, so that everything that asks
/// <see cref="Knows"/> goes on as it did. The town hall pays gold by the size of the settlement, the market trades the
/// stores for gold and back. The citadel hires only the household's own: workers, archers, swordsmen and priests.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The town's buildings, in the order they were put up.</summary>
    public readonly List<Building> Buildings = new();
    /// <summary>What the standing buildings give at their levels, for <see cref="Knows"/>.</summary>
    readonly HashSet<string> _granted = new();
    double _nextTown;
    /// <summary>Bumped whenever a building is put up, rises a level or is lost, for whoever draws them.</summary>
    public int TownVersion { get; private set; }

    /// <summary>A building's stock grows by its growth at each daybreak, as at the start of a week in the Heroes (Sim.Raids.cs, DayClock).</summary>
    public const float GrowthSpan = (float)DayClock.DayLength;

    static (UnitType, int, int) H(UnitType t, int level, int growth) => (t, level, growth);

    /// <summary>Every kind of town building, in the order the «Город» tab shows them.</summary>
    public static readonly TownKind[] TownKinds =
    {
        new() { Key = "hall", Name = "Ратуша", Lore = "b_hall", Gold = 60, UpGold = 120, Income1 = 3, Income2 = 6,
            About = "Платит золото: 3 в минуту и по 1 за каждые 2 здания города. Собирает подать с жилых домов.", Level2 = "6 золота в минуту и по 1 за каждое здание.",
            Props1 = new[] { Prop.Desk, Prop.Crates }, Props2 = new[] { Prop.Desk, Prop.Casks } },
        new() { Key = "market", Name = "Рынок", Lore = "b_market", Gold = 50, UpGold = 100, Income2 = 2, Wall = Mat.Wood,
            About = "Меняет запасы на золото и обратно: 1 м³ камня или брёвен продаётся за 3, покупается за 9.", Level2 = "Продаёт по 4, покупает по 7, даёт 2 золота в минуту.",
            Needs = new[] { ("hall", 1) }, Props1 = new[] { Prop.Counter, Prop.Crates, Prop.Casks }, Props2 = new[] { Prop.Crates, Prop.Casks } },
        new() { Key = "smithy", Name = "Кузница", Lore = "b_smithy", Gold = 50, UpGold = 110, MetalCost = .25f, UpMetal = 1,
            About = "Добрые кирки: копают и рубят на 25 % быстрее. Окованные ворота: ворота и мосты выдерживают вдвое больше. Жаровни на стенах: факелы светят дальше.",
            Level2 = "Кольчуги: солдаты получают на 20 % меньше урона. Закалённые клинки: в ближнем бою бьют на 20 % сильнее.",
            Grants1 = new[] { "picks", "ironbound", "braziers" }, Grants2 = new[] { "mail", "sergeants" },
            Props1 = new[] { Prop.Forge, Prop.Anvil, Prop.Rack }, Props2 = new[] { Prop.Rack, Prop.Crates } },
        new() { Key = "carpentry", Name = "Плотницкая", Lore = "b_carpentry", Gold = 40, UpGold = 90, Wall = Mat.Wood,
            About = "Тачки: добыча на 20 % больше. Пожарные вёдра: огонь тушат вдвое быстрее. Мокрые шкуры: наше дерево загорается вдвое реже.",
            Level2 = "Тропы и носилки: рабочие на 20 % быстрее. Двухъярусные нары: лежанка даёт 2 места. Гильдия мастеров: работа ещё на 20 % быстрее.",
            Needs = new[] { ("smithy", 1) }, Grants1 = new[] { "barrows", "buckets", "hides" }, Grants2 = new[] { "carts", "bunks", "guild" },
            Props1 = new[] { Prop.Workbench, Prop.Blocks }, Props2 = new[] { Prop.Workbench, Prop.Crates } },
        new() { Key = "masons", Name = "Двор каменщиков", Lore = "b_masons", Gold = 50, UpGold = 120,
            About = "Известковый раствор: орда ломает камень на 33 % дольше. Артель каменщиков: строят на 25 % быстрее.",
            Level2 = "Тёсаный камень, цитадель на скале и крепость: камни бьют вдвое меньше, цитадель получает вдвое меньше урона, орда ломает камень ещё дольше.",
            Grants1 = new[] { "mortar", "masons" }, Grants2 = new[] { "granite", "bastion", "fortress" },
            Props1 = new[] { Prop.Blocks, Prop.Workbench }, Props2 = new[] { Prop.Blocks, Prop.Blocks } },
        new() { Key = "drill", Name = "Учебный плац", Lore = "b_drill", Gold = 40, UpGold = 90,
            About = "Наём на 20 % дешевле, стёганки: у новых солдат на 20 % больше здоровья.", Level2 = "Солдаты ходят и бегают на 15 % быстрее.",
            Grants1 = new[] { "drill", "gambesons" }, Grants2 = new[] { "drillyard" },
            Props1 = new[] { Prop.Rack, Prop.Target }, Props2 = new[] { Prop.Rack, Prop.Target } },
        new() { Key = "infirmary", Name = "Лазарет", Lore = "b_infirmary", Gold = 40, UpGold = 90, Wall = Mat.Wood,
            About = "Раненые у цитадели и в лазарете заживают вдвое быстрее.", Level2 = "Ветераны: раненые остаются в строю дольше.",
            Needs = new[] { ("drill", 1) }, Grants1 = new[] { "banners" }, Grants2 = new[] { "veterans" },
            Props1 = new[] { Prop.Bunk, Prop.Bunk }, Props2 = new[] { Prop.Bunk, Prop.Bunk } },
        new() { Key = "traps", Name = "Ловчий двор", Lore = "b_traps", Gold = 40, UpGold = 90, Wall = Mat.Wood,
            About = "Острые колья, густая смола, пропитанные стрелы: колья ранят сильнее, смола горит дольше, огненные стрелы чаще поджигают.",
            Level2 = "Горючая смесь, огненный ливень и ловчая наука: взрывы шире, горят дольше, колья и ямы страшнее.",
            Grants1 = new[] { "spikes", "tar", "soaked" }, Grants2 = new[] { "naphtha", "firestorm", "deathtraps" },
            Props1 = new[] { Prop.Rack, Prop.Casks }, Props2 = new[] { Prop.Casks, Prop.Crates } },
        new() { Key = "barracks", Name = "Казарма", Lore = "b_barracks", Gold = 50, UpGold = 110,
            About = "Нанимает копейщиков. В войске на 4 места больше.", Level2 = "Нанимает щитоносцев и алебардщиков. Стена щитов и длинные древки.",
            Hires = new[] { H(Kinds.Spearman, 1, 4), H(Kinds.Shieldbearer, 2, 3), H(Kinds.Halberdier, 2, 3) },
            Grants1 = new[] { "spearmen", "barracks" }, Grants2 = new[] { "shieldmen", "halberds", "shieldwall", "longshafts" },
            Props1 = new[] { Prop.Rack, Prop.Bunk, Prop.Bunk }, Props2 = new[] { Prop.Rack, Prop.Bunk } },
        new() { Key = "knights", Name = "Рыцарский двор", Lore = "b_knights", Gold = 120, MaxLevel = 1, MetalCost = .5f,
            About = "Нанимает рыцарей в полных латах.",
            Needs = new[] { ("barracks", 2), ("smithy", 1) }, Hires = new[] { H(Kinds.Knight, 1, 2) }, Grants1 = new[] { "knights" },
            Props1 = new[] { Prop.Rack, Prop.Stall } },
        new() { Key = "range", Name = "Стрельбище", Lore = "b_range", Gold = 40, UpGold = 100, Wall = Mat.Wood,
            About = "Нанимает пращников. Тисовые луки и гранёные наконечники: лучники бьют дальше и сильнее.",
            Level2 = "Нанимает арбалетчиков. Частая стрельба, совиный глаз, длинные луки, бронебойные болты, ядра и горшки.",
            Hires = new[] { H(Kinds.Slinger, 1, 4), H(Kinds.Crossbowman, 2, 3) },
            Grants1 = new[] { "slingers", "yew", "barbs" }, Grants2 = new[] { "crossbows", "volley", "owleye", "longbows", "bodkins", "leadshot" },
            Props1 = new[] { Prop.Target, Prop.Target, Prop.Rack }, Props2 = new[] { Prop.Target, Prop.Rack } },
        new() { Key = "lab", Name = "Лаборатория алхимиков", Lore = "b_lab", Gold = 80, UpGold = 140,
            About = "Нанимает алхимиков: их кислота проедает лестницы и машины орды.", Level2 = "Нанимает бомбардиров, алхимики бросают склянки едкого дыма.",
            Needs = new[] { ("range", 1) }, Hires = new[] { H(Kinds.OurAlchemist, 1, 2), H(Kinds.Bombardier, 2, 2) },
            Grants1 = new[] { "alchemists" }, Grants2 = new[] { "bombards" },
            Props1 = new[] { Prop.Still, Prop.Casks }, Props2 = new[] { Prop.Still, Prop.Crates } },
        new() { Key = "kennel", Name = "Псарня", Lore = "b_kennel", Gold = 30, UpGold = 70, Wall = Mat.Wood,
            About = "Нанимает боевых псов.", Level2 = "Травля: псы и всадники догоняют бегущих и бьют их сильнее.",
            Hires = new[] { H(Kinds.Hound, 1, 4) }, Grants1 = new[] { "hounds" }, Grants2 = new[] { "hunt" },
            Props1 = new[] { Prop.Kennel, Prop.Hay }, Props2 = new[] { Prop.Kennel, Prop.Hay } },
        new() { Key = "stables", Name = "Конюшня", Lore = "b_stables", Gold = 80, UpGold = 150, Wall = Mat.Wood, D = 73,
            About = "Нанимает всадников.", Level2 = "Нанимает конных лучников и конных рыцарей. Натиск: удар с разгона вдвое сильнее.",
            Needs = new[] { ("kennel", 1) }, Hires = new[] { H(Kinds.Rider, 1, 3), H(Kinds.HorseArcher, 2, 2), H(Kinds.Lancer, 2, 1) },
            Grants1 = new[] { "riders" }, Grants2 = new[] { "horsearchers", "lancers", "charge" },
            Props1 = new[] { Prop.Stall, Prop.Stall, Prop.Hay }, Props2 = new[] { Prop.Hay, Prop.Crates } },
        new() { Key = "trolls", Name = "Логово троллей", Lore = "b_trolls", Gold = 90, UpGold = 120,
            About = "Нанимает ручных троллей.", Level2 = "Глыбы: тролли и великаны бросают в орду камни.",
            Needs = new[] { ("barracks", 1) }, Hires = new[] { H(Kinds.TameTroll, 1, 1) }, Grants1 = new[] { "trolls" }, Grants2 = new[] { "boulders" },
            Props1 = new[] { Prop.Seat, Prop.Hay }, Props2 = new[] { Prop.Blocks } },
        new() { Key = "giants", Name = "Чертог великанов", Lore = "b_giants", Gold = 140, UpGold = 160, D = 73,
            About = "Нанимает великанов. Великан занимает 4 места.", Level2 = "Глыбы: великаны и тролли бросают в орду камни.",
            Needs = new[] { ("trolls", 1) }, Hires = new[] { H(Kinds.Giant, 1, 1) }, Grants1 = new[] { "giants" }, Grants2 = new[] { "boulders" },
            Props1 = new[] { Prop.Seat, Prop.Seat }, Props2 = new[] { Prop.Blocks } },
        new() { Key = "workshop", Name = "Мастерская", Lore = "b_workshop", Gold = 80, UpGold = 160, Wall = Mat.Wood, D = 73, MetalCost = .5f, UpMetal = 1,
            About = "Собирает катапульты. Вороты: машины заряжаются быстрее.",
            Level2 = "Собирает баллисты и требушеты. Бочонки со смолой и тяжёлые камни.",
            Needs = new[] { ("carpentry", 1) }, Hires = new[] { H(Kinds.Mangonel, 1, 1), H(Kinds.Ballista, 2, 1), H(Kinds.Trebuchet, 2, 1) },
            Grants1 = new[] { "mangonels", "windlass" }, Grants2 = new[] { "ballistas", "trebuchets", "pitchkegs", "heavystones" },
            Props1 = new[] { Prop.Workbench, Prop.Blocks, Prop.Crates }, Props2 = new[] { Prop.Workbench, Prop.Crates } },
        // the miners' building, on an outcrop of iron ore (Sim.Metal.cs)
        new() { Key = "mine", Name = "Рудокопня", Lore = "b_mine", Gold = 60, UpGold = 120, Metal1 = .3f, Metal2 = .7f, OnOre = true, W = 33, D = 41,
            About = "Добывает металл: 0,3 м³ в минуту.", Level2 = "0,7 м³ металла в минуту.",
            Props1 = new[] { Prop.Forge, Prop.Ingots, Prop.Crates }, Props2 = new[] { Prop.Ingots, Prop.Workbench } },
    };

    static Dictionary<string, TownKind> _townByKey;
    public static TownKind TownKindOf(string key)
    {
        if (_townByKey == null)
        {
            var map = new Dictionary<string, TownKind>();
            foreach (var k in TownKinds) map[k.Key] = k;
            _townByKey = map;
        }
        return key != null && _townByKey.TryGetValue(key, out var t) ? t : null;
    }

    /// <summary>Every key a building gives at some level: what were the lore's improvements (the sandbox has them all).</summary>
    public static IEnumerable<string> TownGrants()
    {
        foreach (var k in TownKinds)
        {
            foreach (var g in k.Grants1) yield return g;
            foreach (var g in k.Grants2) yield return g;
        }
    }

    /// <summary>The kinds the citadel itself takes on: the household's own. The rest are hired in their buildings.</summary>
    public static bool CitadelHires(UnitType t) => t == Kinds.Worker || t == Kinds.Archer || t == Kinds.Swordsman || t == Kinds.Priest || t == Kinds.Captain;

    /// <summary>The town building of a kind, if one is put up (standing or going up).</summary>
    public Building BuildingOf(string kind) => Buildings.Find(b => b.Kind == kind);

    /// <summary>The town building a dwelling zone is, or null.</summary>
    public Building BuildingOn(Zone z) => z == null ? null : Buildings.Find(b => b.X0 == z.X0 && b.Z0 == z.Z0 && b.X1 == z.X1 && b.Z1 == z.Z1);

    /// <summary>The dwelling zone a building stands as.</summary>
    public Zone ZoneOf(Building b) => Zones.Find(z => z.Kind == Zone.Dwelling && z.X0 == b.X0 && z.Z0 == b.Z0 && z.X1 == b.X1 && z.Z1 == b.Z1);

    /// <summary>The building whose walls stand round a column, or null.</summary>
    public Building BuildingAt(int x, int z) => Buildings.Find(b => x >= b.X0 - 1 && x <= b.X1 + 1 && z >= b.Z0 - 1 && z <= b.Z1 + 1);

    /// <summary>Whether a building of a kind stands working at a level or higher.</summary>
    public bool Stands(string kind, int level = 1) => Buildings.Exists(b => b.Kind == kind && b.Ready && b.Level >= level);

    /// <summary>Why a kind of building cannot be put up now, or null.</summary>
    public string CantBuild(TownKind k)
    {
        if (Over) return "осада окончена";
        if (!Sandbox && !Knows(k.Lore)) return $"сначала изучите «{k.Name}» во вкладке «Знания»";
        if (BuildingOf(k.Key) != null) return "такое здание уже есть";
        foreach (var (key, level) in k.Needs)
            if (!Stands(key, level)) return $"сначала нужна {(level > 1 ? "улучшенная " : "")}«{TownKindOf(key)?.Name}»";
        if (!Sandbox && Stock.Gold < k.Gold) return $"нужно {k.Gold} золота";
        if (k.MetalCost > 0 && !HasMetal(k.MetalCost)) return $"нужно {k.MetalCost:0.##} м³ металла";
        return null;
    }

    /// <summary>The rectangle a building of this kind put up round a column would stand on: along x unless turned.</summary>
    public static (int x0, int z0, int x1, int z1) BuildingRect(TownKind k, int cx, int cz, bool turned)
    {
        int sx = turned ? k.D : k.W, sz = turned ? k.W : k.D;
        int x0 = cx - sx / 2, z0 = cz - sz / 2;
        return (x0, z0, x0 + sx - 1, z0 + sz - 1);
    }

    /// <summary>
    /// What a building of this kind put up round a column would be: its house, laid level with the ground at its middle
    /// (null with why not).
    /// </summary>
    public VoxelList BuildingPlan(TownKind k, int cx, int cz, bool turned, out Shapes.HouseDoor door, out string why, out VoxelList arched)
        => BuildingPlan(k, cx, cz, turned, out door, out why, out arched, out _);

    /// <summary>
    /// As above, with the rectangle it takes: put up against a house or a building, it shares its wall (Sim.Houses.cs),
    /// and its door goes to a side that opens onto free ground.
    /// </summary>
    public VoxelList BuildingPlan(TownKind k, int cx, int cz, bool turned, out Shapes.HouseDoor door, out string why, out VoxelList arched, out (int x0, int z0, int x1, int z1) rect)
        => BuildingPlan(k, cx, cz, turned, out door, out why, out arched, out rect, out _);

    public VoxelList BuildingPlan(TownKind k, int cx, int cz, bool turned, out Shapes.HouseDoor door, out string why, out VoxelList arched, out (int x0, int z0, int x1, int z1) rect, out List<DoorMove> moves)
    {
        moves = null;
        var (x0, z0, x1, z1) = BuildingRect(k, cx, cz, turned);
        rect = (x0, z0, x1, z1);
        door = default; arched = null;
        // (the land convex, its four corners on it are the whole of it)
        if (x0 < 2 || z0 < 2 || x1 >= World.SX - 2 || z1 >= World.SZ - 2
            || !World.OnLand(x0, z0) || !World.OnLand(x1, z0) || !World.OnLand(x0, z1) || !World.OnLand(x1, z1)) { why = "здесь край долины"; return null; }
        // the miners' building stands on an outcrop of ore (Sim.Metal.cs)
        if (k.OnOre && OutcropUnder(x0, z0, x1, z1) < 0) { why = "ставится на рудный выход: ржавая скала, где жила выходит наружу"; return null; }
        var st = new Stroke { Mat = k.Wall, Width = Shapes.HouseWall, Height = Shapes.HouseHeight + 4, Level = true };
        // (a building goes where its kind must, on rough ground too: its door wants only nothing built before it)
        var plan = PlanHouseAt(x0, z0, x1, z1, st, -1, out door, out why, out arched, out rect, out _, out var over, out moves, lax: true);
        if (over != null) { why = "здесь уже стоит дом"; return null; }
        return plan;
    }

    /// <summary>
    /// A building of this kind put up round a column: the masters paid, its house marked for the workers (its roof to
    /// stand as an arch), its door to be cut once the wall stands, its floor a room to furnish as the building. Why not, or null.
    /// </summary>
    public string PutUp(TownKind k, int cx, int cz, bool turned, out Building b)
    {
        b = null;
        string no = CantBuild(k);
        if (no != null) return no;
        var plan = BuildingPlan(k, cx, cz, turned, out var door, out string why, out var arched, out var rect, out var moves);
        if (plan == null) return why;
        var (x0, z0, x1, z1) = rect;
        if (PutHouse(plan, arched, door, rect, moves) == 0) return "здесь нечего класть";
        if (!Sandbox) Stock.Gold -= k.Gold;
        TakeMetal(k.MetalCost);
        b = new Building { Kind = k.Key, X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Top0 = door.Top };
        Buildings.Add(b);
        if (ZoneOf(b) is { } zone) zone.Title = k.Name;
        TownVersion++;
        Say($"{k.Name}: рабочие строят стены, крышу и дверь, потом обставят. Тогда здание заработает.", 1, b.CX, b.CZ);
        return null;
    }

    /// <summary>Why a building cannot be raised a level now, or null.</summary>
    public string CantRaise(Building b)
    {
        var k = b.K;
        if (b.Level >= k.MaxLevel || b.Target > b.Level) return b.Target > b.Level ? "уже строится" : "выше некуда";
        if (!b.Ready) return "здание ещё не готово";
        if (!Sandbox && Stock.Gold < k.UpGold) return $"нужно {k.UpGold} золота";
        if (k.UpMetal > 0 && !HasMetal(k.UpMetal)) return $"нужно {k.UpMetal:0.##} м³ металла";
        return null;
    }

    /// <summary>A building raised a level: the masters paid, and a second storey put up on it (Sim.Storeys.cs). Why not, or null.</summary>
    public string Raise(Building b)
    {
        string no = CantRaise(b);
        if (no != null) return no;
        var zone = ZoneOf(b);
        if (zone == null) return "здания больше нет";
        no = PlanStorey(zone, b.X0, b.Z0, b.X1, b.Z1, b.K.Wall);
        if (no != null) return no;
        if (!Sandbox) Stock.Gold -= b.K.UpGold;
        TakeMetal(b.K.UpMetal);
        b.Target = b.Level + 1;
        TownVersion++;
        Say($"{b.K.Name}: рабочие поднимают второй этаж. Когда его обставят, здание станет лучше.", 1, b.CX, b.CZ);
        return null;
    }

    /// <summary>Why one of a kind cannot be hired in a building now, or null.</summary>
    public string CantHireAt(Building b, UnitType t)
    {
        if (Over) return "осада окончена";
        if (!b.Ready) return "здание ещё не готово";
        int at = -1;
        foreach (var (kind, level, _) in b.K.Hires) if (kind == t) at = level;
        if (at < 0) return "здесь таких не нанимают";
        if (b.Level < at) return "сначала улучшите здание";
        if (!Sandbox && (!b.Stock.TryGetValue(t.Key, out float n) || n < 1)) return "пока некого: прибудут к утру";
        int have = Household();
        if (have + t.Room > Places) return $"мест нет: занято {have} из {Places}, постройте жилой дом";
        int cost = HireCost(t);
        if (Stock.Gold < cost) return $"нужно {cost} золота";
        if (t.Logs > 0 && Stock[Stockpile.Wood] + 1e-6 < t.Logs * VoxelWorld.PerM3) return $"нужно {t.Logs:0.#} м³ брёвен";
        if (t.Metal > 0 && !HasMetal(t.Metal)) return $"нужно {t.Metal:0.##} м³ металла";
        return null;
    }

    /// <summary>One of a kind taken on in a building: paid for, out of its stock, and out of its door it comes.</summary>
    public Unit HireAt(Building b, UnitType t)
    {
        if (CantHireAt(b, t) != null) return null;
        Stock.Gold -= HireCost(t);
        if (t.Logs > 0) Stock.Take(Stockpile.Wood, t.Logs * VoxelWorld.PerM3);
        TakeMetal(t.Metal);
        if (!Sandbox) b.Stock[t.Key] = b.Stock[t.Key] - 1;
        int cell = DoorStand(b);
        var u = Spawn(t, cell >= 0 ? cell : Gate);
        Outfit(u);
        if (t.Crew > 0)
        {
            for (int i = 0; i < t.Crew; i++) { var c = Spawn(Kinds.Crewman, u.Cell); c.Escort = u; }
            Say($"{t.Name} собран: {b.K.Name}, с расчётом.", 1, b.CX, b.CZ);
        }
        else Say($"{t.Name} нанят: {b.K.Name}.", 1, b.CX, b.CZ);
        return u;
    }

    /// <summary>Where one hired steps out: on the ground before the building's side that faces the citadel.</summary>
    int DoorStand(Building b)
    {
        var (gx, _, gz) = Gate >= 0 ? Nav.Feet(Gate) : (b.CX, 0f, b.CZ);
        float dx = gx - b.CX, dz = gz - b.CZ;
        int x = (int)b.CX, z = (int)b.CZ;
        if (Math.Abs(dx) > Math.Abs(dz)) x = dx > 0 ? b.X1 + 10 : b.X0 - 10;
        else z = dz > 0 ? b.Z1 + 10 : b.Z0 - 10;
        return NearestStand(x, z, 4);
    }

    /// <summary>What the market gives a cubic metre of a store for, and asks for one, at its level (0 with no market).</summary>
    public (int sell, int buy) MarketRates(int kind)
    {
        var m = Buildings.Find(b => b.Kind == "market" && b.Ready);
        if (m == null) return (0, 0);
        bool up = m.Level >= 2;
        // metal dearer than the rest: a cubic metre of iron is a deal of smith's work
        return kind == Stockpile.Earth ? (up ? 2 : 1, up ? 3 : 4) : kind == Stockpile.Metal ? (up ? 10 : 8, up ? 16 : 20) : (up ? 4 : 3, up ? 7 : 9);
    }

    /// <summary>Trades a cubic metre of a store at the market: sold for gold, or bought. Why not, or null.</summary>
    public string Trade(int kind, bool sell)
    {
        var (s, b) = MarketRates(kind);
        if (s == 0) return "нужен готовый рынок";
        if (sell)
        {
            if (!Stock.Take(kind, VoxelWorld.PerM3)) return $"нет 1 м³: {Stockpile.Name(kind)}";
            Stock.Gold += s;
        }
        else
        {
            if (Stock.Gold < b) return $"нужно {b} золота";
            Stock.Gold -= b;
            Stock[kind] += VoxelWorld.PerM3;
        }
        return null;
    }

    /// <summary>The gold the town pays a minute now: the town hall's by the settlement's size, the market's, and the houses' dues (Sim.Dues.cs).</summary>
    public float TownIncome()
    {
        float g = 0;
        int built = 0;
        foreach (var b in Buildings) if (b.Ready) built++;
        foreach (var b in Buildings)
        {
            if (!b.Ready) continue;
            var k = b.K;
            if (k.Key == "hall") g += b.Level >= 2 ? k.Income2 + built : k.Income1 + built / 2;
            else g += b.Level >= 2 ? k.Income2 : k.Income1;
        }
        return g + _dues;
    }

    /// <summary>
    /// What the town would do best to have next, for the «Город» tab: a town hall first; then houses lived in; then the
    /// cheapest building the lore has opened and the gold will pay for, else a second storey the gold will pay for;
    /// then what would make the houses pay more dues; else what to learn. Null when there is nothing to say.
    /// </summary>
    public string TownNext()
    {
        var hall = TownKindOf("hall");
        if (BuildingOf("hall") == null)
            return CantBuild(hall) is { } no && no.StartsWith("сначала изучите") ? "Дальше: изучите «Ратуша» во «Знаниях». Она платит золото и собирает подать с домов."
                : "Дальше: поставьте ратушу. Она платит золото и собирает подать с домов.";
        if (!Zones.Exists(LivedIn))
            return "Дальше: постройте жилой дом (прямоугольник, «дом»). Каждая лежанка даёт место в войске и подать.";
        TownKind cheapest = null;
        foreach (var k in TownKinds)
            if (CantBuild(k) == null && (cheapest == null || k.Gold < cheapest.Gold)) cheapest = k;
        if (cheapest != null) return $"Дальше можно: «{cheapest.Name}», {cheapest.Gold} зол. {cheapest.About}";
        foreach (var b in Buildings)
            if (CantRaise(b) == null) return $"Дальше можно: второй этаж «{b.K.Name}», {b.K.UpGold} зол. {b.K.Level2}";
        foreach (var z in Zones)
            if (LivedIn(z) && ComfortOf(z) != (Comfort.Water | Comfort.Fire | Comfort.Rest | Comfort.Paving))
                return "Дальше: обустройте посёлок. Колодец, костёр, скамья или клумба и дорожка у дома прибавляют подать.";
        foreach (var k in TownKinds)
            if (BuildingOf(k.Key) == null && !Knows(k.Lore)) return $"Дальше: откройте новое здание во «Знаниях», например «{k.Name}».";
        return null;
    }

    /// <summary>What the standing buildings give, gathered again (a level reached or lost, a game read back).</summary>
    void Regrant()
    {
        _granted.Clear();
        foreach (var b in Buildings)
        {
            if (!b.Ready) continue;
            var k = b.K;
            if (b.Level >= 1) _granted.UnionWith(k.Grants1);
            if (b.Level >= 2) _granted.UnionWith(k.Grants2);
        }
    }

    /// <summary>A new day: each building's stock of those it hires grows by its growth, up to twice that, as a week in the Heroes.</summary>
    internal void TownDay()
    {
        int grew = 0;
        foreach (var b in Buildings)
        {
            if (!b.Ready) continue;
            foreach (var (kind, level, growth) in b.K.Hires)
            {
                if (b.Level < level) continue;
                b.Stock.TryGetValue(kind.Key, out float n);
                float now = Math.Min(growth * 2, n + growth);
                if (now > n) grew++;
                b.Stock[kind.Key] = now;
            }
        }
        if (grew > 0) { TownVersion++; Say("Новый день: в зданиях города прибавилось желающих наняться.", 0); }
    }

    /// <summary>The town looked over now and then: its stocks growing, its income paid; a building gone is let go.</summary>
    void TickTown(float dt)
    {
        if (Buildings.Count == 0) return;
        // gold and stocks run with the game's time, every step
        float income = TownIncome();
        if (income > 0 && !Over) Stock.Gold += income / 60 * dt;
        // and metal, from the miners' buildings (Sim.Metal.cs)
        TickMetal(dt);
        if (Time < _nextTown) return;
        _nextTown = Time + 2;
        CountDues();
        for (int i = Buildings.Count - 1; i >= 0; i--)
        {
            var b = Buildings[i];
            if (ZoneOf(b) == null)
            {
                Buildings.RemoveAt(i);
                Regrant();
                TownVersion++;
                Say($"{b.K?.Name ?? "Здание"} больше не стоит.", 2, b.CX, b.CZ);
            }
        }
    }

    /// <summary>
    /// A building's room looked over (from <see cref="LookOver"/>): open — not working; closed — furnished as its kind
    /// wants at its level, and once that stands it works; a storey gone up and furnished — the next level.
    /// </summary>
    void LookOverBuilding(Zone z, Building b, DwellingCheck check, List<int> floor, bool quiet)
    {
        var k = b.K;
        z.Title = k.Name;
        z.Beds = 0;
        z.BunksWhole.Clear();
        bool was = b.Ready;
        if (!check.Ok)
        {
            // a storey going up opens the roof: the building works on below meanwhile
            if (b.Target > b.Level && b.Level > 0) return;
            if (was)
            {
                b.Ready = false;
                Regrant();
                TownVersion++;
                if (!quiet) Say($"{k.Name} открыта ({check.Why}) и не работает, пока её не закроют.", 2, b.CX, b.CZ);
            }
            return;
        }
        long key = FloorKey(floor) * 31 + b.Target;
        if (!_furnishing.TryGetValue(z.Id, out var plan) || plan.Key != key)
        {
            var fresh = FurnishBuilding(floor, k, b.Target);
            fresh.Key = key;
            if (plan != null) Unfurnish(plan, fresh);
            _furnishing[z.Id] = plan = fresh;
        }
        var want = new VoxelList();
        int standing = 0;
        for (int i = 0; i < plan.Voxels.Count; i++)
        {
            int x = plan.Voxels.X(i), y = plan.Voxels.Y(i), zz = plan.Voxels.Z(i);
            byte m = plan.Voxels.M(i), now = World.Get(x, y, zz);
            if (now == m) { standing++; continue; }
            if (Brushes.Free(now) && Orders.At(x, y, zz) == 0) want.Add(x, y, zz, m);
        }
        if (want.Count > 0) DesignateBuild(want);
        z.Planned = plan.Bunks.Count;
        bool furnished = standing >= plan.Voxels.Count * .85f;
        // a storey: up only once its walls and roof are laid as well as its furniture
        bool raised = b.Target <= 1 || z.Depth > b.Top0 && !Storeys.Exists(s => s.X0 == b.X0 && s.Z0 == b.Z0) && !BuildOrdersIn(b);
        if (!furnished || !raised) return;
        if (!b.Ready || b.Level < b.Target)
        {
            bool first = b.Level == 0;
            b.Ready = true;
            b.Level = b.Target;
            foreach (var (kind, level, growth) in k.Hires)
                if (b.Level >= level && !b.Stock.ContainsKey(kind.Key)) b.Stock[kind.Key] = growth;
            Regrant();
            TownVersion++;
            if (!quiet) Say(first ? $"{k.Name} готова и работает. {k.About}" : was ? $"{k.Name}: второй этаж готов. {k.Level2}" : $"{k.Name} снова закрыта и работает.", 1, b.CX, b.CZ);
            // a deed: a building put up, or raised a storey (not one only closed again)
            if (!quiet && (first || was)) BuildingDeed(first ? k.Name : $"{k.Name}, второй этаж");
        }
    }

    /// <summary>Whether anything is still marked to be laid on a building's columns (its storey going up).</summary>
    bool BuildOrdersIn(Building b)
    {
        int g = World.GroundTop((int)b.CX, (int)b.CZ);
        for (int z = b.Z0 - Shapes.Eaves; z <= b.Z1 + Shapes.Eaves; z += 1)
            for (int x = b.X0 - Shapes.Eaves; x <= b.X1 + Shapes.Eaves; x += 1)
                for (int y = Math.Max(1, g - 4); y < Math.Min(World.SY - 1, g + 2 * Shapes.HouseHeight + 24); y++)
                {
                    byte o = Orders.At(x, y, z);
                    if (o != 0 && !Orders.IsRemove(o)) return true;
                }
        return false;
    }

    // ------------------------------------------------------------ what stands inside

    /// <summary>
    /// The furnishing of a town building: on the lowest floor what its first level wants, on a second storey (when it is
    /// being raised to it) what the second wants; each piece by a wall where it can, else wherever it fits, with room
    /// to pass round it. Each piece counts as one of the plan's «bunks», to tell how much of it stands.
    /// </summary>
    Furnishing FurnishBuilding(List<int> floorCells, TownKind k, int level)
    {
        var plan = new Furnishing();
        var clear = new HashSet<int>();
        foreach (var g in Gates) clear.UnionWith(g.Cells);
        foreach (int p in floorCells)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            for (int i = 0; i < 4; i++) if (Mat.IsLadder[World.Get(x, y + i, z)]) { clear.Add(Nav.CellOf(x, y, z)); break; }
        }
        // the floors by height: the lowest, and what stands well over it (a second storey)
        int lowest = int.MaxValue;
        foreach (int p in floorCells) lowest = Math.Min(lowest, WaterPlanner.Unpack(p).y);
        var low = new Dictionary<int, int>();
        var high = new Dictionary<int, int>();
        int Col(int x, int z) => z * 4096 + x;
        foreach (int p in floorCells)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            if (clear.Contains(Nav.CellOf(x, y, z))) continue;
            var into = y > lowest + 12 ? high : low;
            if (!into.TryGetValue(Col(x, z), out int was) || y < was) into[Col(x, z)] = y;
        }
        Place(plan, low, k.Props1);
        if (level >= 2) Place(plan, high.Count > 20 ? high : low, k.Props2);
        return plan;
    }

    /// <summary>The pieces set on one floor: each where its footprint is free and level, a wall at its back if it can.</summary>
    void Place(Furnishing plan, Dictionary<int, int> floor, Prop[] props)
    {
        if (floor.Count == 0) return;
        int Col(int x, int z) => z * 4096 + x;
        // the hollows between a rough wall's stones are no floor: whatever stands well over the usual goes
        var heights = new List<int>(floor.Values);
        heights.Sort();
        int usual = heights[heights.Count / 2];
        var off = new List<int>();
        foreach (var (key, y) in floor) if (y > usual + 2) off.Add(key);
        foreach (int key in off) floor.Remove(key);
        var taken = new HashSet<int>();
        int Fits(int x0, int z0, int w, int d, int margin)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            for (int z = z0 - margin; z < z0 + d + margin; z++)
                for (int x = x0 - margin; x < x0 + w + margin; x++)
                {
                    if (taken.Contains(Col(x, z))) return -1;
                    if (x < x0 || x >= x0 + w || z < z0 || z >= z0 + d) continue;
                    if (!floor.TryGetValue(Col(x, z), out int y)) return -1;
                    lo = Math.Min(lo, y); hi = Math.Max(hi, y);
                }
            return hi - lo <= 2 ? hi : -1;
        }
        int FloorAt(int x, int z, int at) => floor.TryGetValue(Col(x, z), out int y) ? y : at;
        bool Solid(int x, int y, int z) => !Mat.Passable[World.Get(x, y + 3, z)] && World.Get(x, y + 3, z) != Mat.Air;
        var cols = new List<int>(floor.Keys);
        cols.Sort();
        foreach (var prop in props)
        {
            var (w, d) = Size(prop);
            bool done = false;
            // by a wall first: one of its long sides against it; then anywhere it fits
            for (int pass = 0; pass < 2 && !done; pass++)
                foreach (int key in cols)
                {
                    int x = key % 4096, z = key / 4096;
                    foreach (bool turn in new[] { false, true })
                    {
                        int pw = turn ? d : w, pd = turn ? w : d;
                        int y = Fits(x, z, pw, pd, 1);
                        if (y < 0) continue;
                        if (pass == 0)
                        {
                            bool wall = turn ? Solid(x - 1, y, z) || Solid(x + pw, y, z) : Solid(x, y, z - 1) || Solid(x, y, z + pd);
                            if (!wall) continue;
                        }
                        int from = plan.Voxels.Count;
                        Draw(plan.Voxels, prop, x, y, z, pw, pd, turn, (xx, zz, at) => FloorAt(xx, zz, at));
                        plan.Bunks.Add((from, plan.Voxels.Count - from));
                        for (int zz = z - 1; zz < z + pd + 1; zz++)
                            for (int xx = x - 1; xx < x + pw + 1; xx++) taken.Add(Col(xx, zz));
                        done = true;
                        break;
                    }
                    if (done) break;
                }
        }
    }

    /// <summary>A piece's footprint, voxels: across and along.</summary>
    static (int w, int d) Size(Prop p) => p switch
    {
        Prop.Anvil => (4, 3), Prop.Forge => (7, 6), Prop.Rack => (8, 3), Prop.Target => (7, 3), Prop.Stall => (10, 14),
        Prop.Workbench => (10, 5), Prop.Counter => (12, 4), Prop.Desk => (8, 5), Prop.Bunk => (8, 16), Prop.Still => (8, 5),
        Prop.Hay => (5, 5), Prop.Casks => (6, 3), Prop.Crates => (6, 3), Prop.Kennel => (7, 6), Prop.Seat => (9, 7), Prop.Blocks => (6, 4),
        Prop.Ingots => (6, 4),
        _ => (4, 4),
    };

    /// <summary>A piece of a town building's furnishing at (x, y, z), the floor under each column found by <paramref name="floorAt"/>.</summary>
    static void Draw(VoxelList L, Prop p, int x, int y, int z, int w, int d, bool turn, Func<int, int, int, int> floorAt)
    {
        void Box(int x0, int y0, int z0, int x1, int y1, int z1, byte m)
        {
            for (int yy = y0; yy < y1; yy++)
                for (int zz = z0; zz < z1; zz++)
                    for (int xx = x0; xx < x1; xx++) L.Add(xx, yy, zz, m);
        }
        // a post from the floor up to a height over y
        void Post(int px, int pz, int h, byte m) { for (int k = floorAt(px, pz, y); k < y + h; k++) L.Add(px, k, pz, m); }
        int cx = x + w / 2, cz = z + d / 2;
        switch (p)
        {
            case Prop.Anvil:
                Box(cx - 1, y, cz - 1, cx + 1, y + 3, cz + 1, Mat.Rack);
                Box(x, y + 3, z, x + w, y + 5, z + d, Mat.Anvil);
                break;
            case Prop.Forge:
                Box(x, y, z, x + w, y + 4, z + d, Mat.Forge);
                Box(x + 1, y + 4, z + 1, x + w - 1, y + 5, z + d - 1, Mat.Coals);
                Box(x + 2, y + 5, z + 2, x + w - 2, y + 6, z + d - 2, Mat.Anvil);
                break;
            case Prop.Rack:
                // a frame of boards, and spears and blades stood up in it, their heads of steel
                Post(x, z, 9, Mat.Rack); Post(x + w - 1, z, 9, Mat.Rack);
                Box(x, y + 9, z, x + w, y + 10, z + 1, Mat.Rack);
                Box(x, y + 2, z, x + w, y + 3, z + 1, Mat.Rack);
                for (int i = 1; i < w - 1; i += 2) { Box(x + i, y, z + 1, x + i + 1, y + 11, z + 2, Mat.Rack); L.Add(x + i, y + 11, z + 1, Mat.Anvil); }
                break;
            case Prop.Target:
                // a butt of straw on two legs, a red ring painted on it
                Post(x + 1, z + 1, 4, Mat.Rack); Post(x + w - 2, z + 1, 4, Mat.Rack);
                for (int dy = 0; dy < 7; dy++)
                    for (int dx = 0; dx < w; dx++)
                    {
                        float rx = dx - (w - 1) / 2f, ry = dy - 3;
                        if (rx * rx + ry * ry > 12.5f) continue;
                        bool ring = Math.Abs(MathF.Sqrt(rx * rx + ry * ry) - 2) < .6f;
                        L.Add(x + dx, y + 4 + dy, z + 1, ring ? Mat.ButtRing : Mat.Butt);
                    }
                break;
            case Prop.Stall:
                // a stall: a rail of boards round three sides, straw on its floor, a manger at its head
                for (int i = 0; i < d; i++) { Post(x, z + i, 6, Mat.Rack); Post(x + w - 1, z + i, 6, Mat.Rack); }
                for (int i = 1; i < w - 1; i++) Post(x + i, z, 6, Mat.Rack);
                Box(x + 1, y, z + 1, x + w - 1, y + 1, z + d, Mat.Hay);
                Box(x + 2, y + 3, z + 1, x + w - 2, y + 5, z + 3, Mat.Rack);
                break;
            case Prop.Workbench:
                Box(x, y + 4, z, x + w, y + 5, z + d, Mat.Rack);
                Post(x, z, 4, Mat.Rack); Post(x + w - 1, z, 4, Mat.Rack); Post(x, z + d - 1, 4, Mat.Rack); Post(x + w - 1, z + d - 1, 4, Mat.Rack);
                Box(x + 1, y + 5, z + 1, x + 5, y + 7, z + 3, Mat.Goods);
                L.Add(x + w - 3, y + 5, z + 2, Mat.Anvil); L.Add(x + w - 2, y + 5, z + 2, Mat.Rack);
                break;
            case Prop.Counter:
                Box(x, y, z, x + w, y + 5, z + 2, Mat.Rack);
                Box(x, y + 5, z, x + w, y + 6, z + d, Mat.Rack);
                Box(x + 1, y + 6, z + 1, x + 3, y + 8, z + 3, Mat.Goods);
                Box(x + w - 4, y + 6, z + 1, x + w - 1, y + 9, z + 3, Mat.Barrel);
                break;
            case Prop.Desk:
                Box(x, y + 4, z, x + w, y + 5, z + d, Mat.Table);
                Post(x, z, 4, Mat.Table); Post(x + w - 1, z, 4, Mat.Table); Post(x, z + d - 1, 4, Mat.Table); Post(x + w - 1, z + d - 1, 4, Mat.Table);
                L.Add(x + 2, y + 5, z + 1, Mat.Goods); L.Add(x + 3, y + 5, z + 1, Mat.Goods);
                break;
            case Prop.Bunk:
                Box(x, y + 2, z, x + w, y + 3, z + d, Mat.Bunk);
                Box(x + 1, y + 3, z + 1, x + w - 1, y + 4, z + d - 1, Mat.Bedding);
                Post(x, z, 2, Mat.Bunk); Post(x + w - 1, z, 2, Mat.Bunk); Post(x, z + d - 1, 2, Mat.Bunk); Post(x + w - 1, z + d - 1, 2, Mat.Bunk);
                Box(x, y + 3, z, x + w, y + 6, z + 1, Mat.Bunk);
                break;
            case Prop.Still:
                // a table of the art: flasks on it, a copper still with its coil
                Box(x, y + 4, z, x + w, y + 5, z + d, Mat.Rack);
                Post(x, z, 4, Mat.Rack); Post(x + w - 1, z, 4, Mat.Rack); Post(x, z + d - 1, 4, Mat.Rack); Post(x + w - 1, z + d - 1, 4, Mat.Rack);
                for (int i = 1; i < w - 4; i += 2) L.Add(x + i, y + 5, z + 1, Mat.Goods);
                Box(x + w - 4, y + 5, z + 1, x + w - 1, y + 8, z + 4, Mat.Goods);
                L.Add(x + w - 3, y + 8, z + 2, Mat.Goods);
                break;
            case Prop.Hay:
                Box(x, y, z, x + w, y + 3, z + d, Mat.Hay);
                Box(x + 1, y + 3, z + 1, x + w - 1, y + 4, z + d - 1, Mat.Hay);
                break;
            case Prop.Casks:
                for (int i = 0; i + 2 < w; i += 3) { Box(x + i, y, z, x + i + 3, y + 4, z + 3, Mat.Barrel); Box(x + i, y + 1, z, x + i + 3, y + 2, z + 3, Mat.Anvil); }
                break;
            case Prop.Crates:
                Box(x, y, z, x + 3, y + 3, z + 3, Mat.Goods);
                Box(x + 3, y, z, x + 6, y + 3, z + 3, Mat.Goods);
                Box(x + 1, y + 3, z, x + 4, y + 5, z + 3, Mat.Goods);
                break;
            case Prop.Kennel:
                Box(x, y, z, x + w, y + 5, z + 1, Mat.Rack);
                Box(x, y, z, x + 1, y + 5, z + d, Mat.Rack);
                Box(x + w - 1, y, z, x + w, y + 5, z + d, Mat.Rack);
                Box(x, y + 5, z, x + w, y + 6, z + d, Mat.Rack);
                Box(x + 1, y, z + 1, x + w - 1, y + 1, z + d, Mat.Hay);
                break;
            case Prop.Seat:
                // a giant's seat: a block of rough stone, straw strewn before it
                Box(x, y, z, x + w, y + 5, z + 4, Mat.Block);
                Box(x, y, z + 4, x + w, y + 1, z + d, Mat.Hay);
                break;
            case Prop.Ingots:
                // a pallet of boards, and the iron cast in bars stacked on it crosswise, a course a way
                Box(x, y, z, x + w, y + 1, z + d, Mat.Rack);
                for (int k = 0; k < 3; k++)
                    for (int i = 0; i < (k % 2 == 0 ? w : d); i += 2)
                        if (k % 2 == 0) Box(x + i, y + 1 + k, z, x + i + 1, y + 2 + k, z + d, Mat.Anvil);
                        else Box(x, y + 1 + k, z + i, x + w, y + 2 + k, z + i + 1, Mat.Anvil);
                break;
            case Prop.Blocks:
                Box(x, y, z, x + 3, y + 3, z + 3, Mat.Block);
                Box(x + 3, y, z, x + 6, y + 2, z + 3, Mat.Block);
                Box(x + 1, y + 3, z, x + 3, y + 5, z + 2, Mat.Goods);
                break;
        }
    }

    // ------------------------------------------------------------ kept

    void WriteTown(System.IO.BinaryWriter w)
    {
        w.Write(Buildings.Count);
        foreach (var b in Buildings)
        {
            w.Write(b.Kind); w.Write(b.X0); w.Write(b.Z0); w.Write(b.X1); w.Write(b.Z1);
            w.Write(b.Level); w.Write(b.Target); w.Write(b.Ready); w.Write(b.Top0);
            w.Write(b.Stock.Count);
            foreach (var (k, n) in b.Stock) { w.Write(k); w.Write(n); }
        }
    }

    void ReadTown(System.IO.BinaryReader r)
    {
        Buildings.Clear();
        for (int i = r.ReadInt32(); i > 0; i--)
        {
            var b = new Building { Kind = r.ReadString(), X0 = r.ReadInt32(), Z0 = r.ReadInt32(), X1 = r.ReadInt32(), Z1 = r.ReadInt32() };
            b.Level = r.ReadInt32(); b.Target = r.ReadInt32(); b.Ready = r.ReadBoolean(); b.Top0 = r.ReadInt32();
            for (int n = r.ReadInt32(); n > 0; n--) b.Stock[r.ReadString()] = r.ReadSingle();
            if (TownKindOf(b.Kind) == null) continue;
            Buildings.Add(b);
            if (ZoneOf(b) is { } zone) zone.Title = b.K.Name;
        }
        Regrant();
        TownVersion++;
    }
}
