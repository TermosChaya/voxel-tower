using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;
using Camp = VoxelTower.Core.Campaign;

namespace VoxelTower.Game;

/// <summary>
/// The campaign's part of the tutorial: a small campaign of its own in a slot of its own (a map of 36 hexes a side, the
/// horde easy, so its peace lasts the lesson out) laid out for it — a chest two hexes from home on the way to a mine of
/// our zone, a pack of wolves by the mine the hero's men beat by far but not so far that they run, a stack of ogres a
/// little off that would beat them, workers and an architect in the hero's army, gold for the tavern and the merchant.
/// It is gone through on the map and in the valleys: the map and its hero, a way and its days, moves and the dawn, the
/// chest, the mine flagged and gone into, a garrison left there with the architect, the beasts weighed and beaten, home,
/// the tavern, the merchant, the hero's card, convoys, the horde's army falling on the mine and the battle in its
/// valley, the fog and the small map, the campaign saved. Its step, its places and its hero are kept while the scene
/// begins again for each valley gone into.
/// </summary>
public partial class Tutorial
{
    /// <summary>The slot the campaign's part plays in, and its seed.</summary>
    public const string Slot = "tutorial";
    public const uint Seed = 4127;
    /// <summary>Where the campaign's part laid its chest, chose its mine, set its wolves and its ogres; the hero it is about.</summary>
    public static (int col, int row) ChestAt, MineAt, WolvesAt, OgresAt;
    public static string HeroName = "";
    /// <summary>Whether its army of the horde has been sent at the mine (once, not again as the scene begins anew).</summary>
    public static bool ArmySent;
    /// <summary>The battles for points won when the part began, the saves and the small map's jumps when their steps began.</summary>
    public static int HeldFrom, SavesFrom, JumpsFrom;
    static int _dayFrom;

    /// <summary>Those of the hero's army the lesson leaves at the mine: the architect, two workers, two archers.</summary>
    static readonly UnitType[] Garrisoned = { Kinds.Architect, Kinds.Worker, Kinds.Worker, Kinds.Archer, Kinds.Archer };

    /// <summary>
    /// The campaign laid out for the lesson: the hero given workers and an architect and a few soldiers more, the nearest
    /// open mine of our zone chosen and cleared of keepers, a chest on the way to it, wolves by it (as many as the men
    /// the hero keeps after the garrison beat five times over: not so few that they run), ogres two hexes off it (twice
    /// what the whole army is), gold for the tavern and the merchant, the fog lifted about all of it.
    /// </summary>
    public static void Prepare(Camp c)
    {
        var home = c.Home;
        var hero = c.Parties.Find(p => p.Owner == Holder.Ours && p.Party.Commander?.Hero != null);
        HeroName = hero?.Party.Commander.Hero.Name ?? "";
        var army = hero?.Party.Members ?? new List<Traveller>();
        foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Architect, Kinds.Swordsman, Kinds.Swordsman, Kinds.Archer })
            army.Add(new Traveller { Kind = t, Hp = t.Hp, MaxHp = t.Hp });
        bool Open(MapCell x) => x != null && x.Passable && x.Thing == null && x.Sum.Keep.Count == 0 && !x.Pass && !x.Home && x.Owner == Holder.None;
        // the mine: the nearest of our zone three to seven hexes off, its keepers sent away
        var mine = c.Cells.Where(x => x.Zone == home.Zone && x.Thing is { Kind: ThingKind.Mine, Owner: Holder.None }
                && Camp.Distance(x.Q, x.R, home.Q, home.R) is >= 3 and <= 7)
            .OrderBy(x => Camp.Distance(x.Q, x.R, home.Q, home.R)).First();
        mine.Sum.Keep.Clear();
        MineAt = (mine.Col, mine.Row);
        // the way to it, and the chest two hexes from home on it
        var way = MapPath.Find(c, home, mine, Holder.Ours) ?? new();
        var chest = way.Skip(1).Take(1).FirstOrDefault(Open) ?? c.Cells.Where(Open).OrderBy(x => Math.Abs(Camp.Distance(x.Q, x.R, home.Q, home.R) - 2)).First();
        chest.Thing = new MapThing { Kind = ThingKind.Chest, Amount = 1 };
        ChestAt = (chest.Col, chest.Row);
        // the wolves by the mine, off the way to it
        var wolves = Enumerable.Range(0, 6).Select(s => c.Neighbour(mine, s)).Where(x => Open(x) && x != chest && !way.Contains(x))
            .OrderBy(x => Camp.Distance(x.Q, x.R, home.Q, home.R)).First();
        var kept = army.Where(m => Array.IndexOf(Garrisoned, m.Kind) < 0).ToList();
        kept.AddRange(army.Where(m => m.Kind == Kinds.Archer).Skip(2));
        float ours = QuickBattle.Strength(kept, null), all = QuickBattle.Strength(army, null);
        List<Traveller> Stack(UnitType t, int n) => Enumerable.Range(0, n).Select(_ => Camp.Raider(t)).ToList();
        int nw = 1;
        while (nw < 20 && ours / QuickBattle.Strength(Stack(Kinds.Wolf, nw), null) >= 5.2f) nw++;
        wolves.Thing = new MapThing { Kind = ThingKind.Monster };
        wolves.Sum.Keep.AddRange(Stack(Kinds.Wolf, nw));
        WolvesAt = (wolves.Col, wolves.Row);
        // the ogres, two hexes off the mine and off the way
        var ogres = c.Cells.Where(x => Open(x) && x != chest && x != wolves && !way.Contains(x) && Camp.Distance(x.Q, x.R, mine.Q, mine.R) == 2)
            .OrderBy(x => Camp.Distance(x.Q, x.R, wolves.Q, wolves.R)).First();
        int no = 1;
        while (no < 30 && all / QuickBattle.Strength(Stack(Kinds.Ogre, no), null) > .5f) no++;
        ogres.Thing = new MapThing { Kind = ThingKind.Monster };
        ogres.Sum.Keep.AddRange(Stack(Kinds.Ogre, no));
        OgresAt = (ogres.Col, ogres.Row);
        c.Give(Res.Gold, 3000);
        c.Reveal(mine, 3);
        ArmySent = false;
    }

    Camp C => Main.Atlas;
    MapCell Cell((int col, int row) at) => C?.Cell(at.col, at.row);
    /// <summary>The tutorial's hero while he is on the map; null while he is in a valley.</summary>
    MapParty Hero => C?.Parties.Find(p => p.Owner == Holder.Ours && p.Party.Commander?.Hero?.Name == HeroName);
    Unit HeroHere => _m.Sim.Units.Find(u => !u.Dead && u.Team == Team.Player && u.Hero?.Name == HeroName);
    int Heroes => C?.HeroCount() ?? 0;
    (int col, int row)? HomeAt => C?.Home is { } h ? (h.Col, h.Row) : null;
    (int col, int row)? HeroAt => Hero is { } h ? (C.CellOf(h).Col, C.CellOf(h).Row) : null;
    string Walking => Hero is { Moving: true } h ? h.Tired ? "герой ждёт рассвета" : "герой идёт" : null;

    void BuildCampaign()
    {
        var m = _m;
        Vector2 look0 = default;
        float dist0 = 0;

        // ---- the map
        const string map = "Карта";
        Add(new Step
        {
            Chapter = map, Title = "Кампания",
            Text = "Кампания идёт на большой карте. Каждая клетка карты отдельная долина, в неё можно войти и играть в " +
                "ней. Ваша цитадель стоит в первой долине. По карте ходят ваши герои и герои орды.\n\nКампания выиграна, " +
                "когда взяты все логова орды. Она проиграна, если первая долина пала и за 3 дня вы не заняли другую.",
        });
        Add(new Step
        {
            Chapter = map, Title = "Обзор карты",
            Text = "Карту двигают {CamForward}{CamLeft}{CamBack}{CamRight}, стрелки или правая кнопка мыши. Колесо " +
                "приближает и отдаляет её. {Board} открывает карту и возвращает в долину.",
            Task = "Сдвиньте и отдалите карту",
            Enter = () => { if (m.WorldMap is { } v) { look0 = v.Look; dist0 = v.Distance; } },
            Done = () => m.WorldMap is { } v && (v.Look - look0).Length() > 24 && Math.Abs(v.Distance - dist0) > dist0 * .3f,
        });
        Add(new Step
        {
            Chapter = map, Title = "Герой",
            Text = "Ваш герой на коне стоит у цитадели. Он уже выбран: справа его армия и ход на сегодня. Армия ходит с " +
                "героем, на карте её не видно. Щелчок по другому герою выбирает его.",
            Point = "atlas:side",
            Hex = () => HeroAt,
        });
        Add(new Step
        {
            Chapter = map, Title = "Путь",
            Text = "Щёлкните по клетке, куда идти. Стрелки покажут путь: по зелёным герой пройдёт сегодня, по красным в " +
                "следующие дни. Справа написано, сколько дней идти. Сундук обведён рамкой.",
            Task = "Проложите путь к сундуку",
            Hex = () => ChestAt,
            Enter = () => m.WorldMap?.FocusHex(ChestAt.col, ChestAt.row),
            Done = () => m.Hud.MapPane.Planned is { Count: > 0 } p && p[^1].Col == ChestAt.col && p[^1].Row == ChestAt.row
                || Hero is { } h && (h.Moving || C.CellOf(h) == Cell(ChestAt)),
        });
        Add(new Step
        {
            Chapter = map, Title = "Идти",
            Text = "Щёлкните по концу пути ещё раз или нажмите {Rmb}. Герой идёт, пока не дойдёт или пока не кончится ход. " +
                "Ожидание ускоряет {Speed}.",
            Task = "Отправьте героя к сундуку",
            Hex = () => ChestAt,
            Done = () => Hero is { } h && (h.Moving || C.CellOf(h) == Cell(ChestAt)) || Cell(ChestAt).Thing is null or { Taken: true },
        });
        Add(new Step
        {
            Chapter = map, Title = "Сундук",
            Text = "В сундуке золото для казны или опыт для героя, выберите одно. Кучи ресурсов и костры герой забирает " +
                "сам, когда наступает на них.",
            Task = "Откройте сундук",
            Hex = () => ChestAt,
            Done = () => Cell(ChestAt).Thing is null or { Taken: true },
            Status = () => Walking,
        });
        Add(new Step
        {
            Chapter = map, Title = "Ход и рассвет",
            Text = "Каждый шаг тратит ход: трава 100, дорога 50, песок, снег, болото и лес больше. Горы и глубокая вода " +
                "непроходимы. На день у героя 1500 хода, потом он ждёт утра. «До рассвета» сразу переводит время на утро.",
            Task = "Нажмите «До рассвета»",
            Point = "atlas:dawn",
            Enter = () => _dayFrom = C.Clock.Day,
            Done = () => C.Clock.Day > _dayFrom,
        });

        // ---- a point held
        const string point = "Рудник";
        Add(new Step
        {
            Chapter = point, Title = "Рудник",
            Text = "Рудник ваш, когда на него зайдёт герой. Каждое утро он даёт свой ресурс: золото и редкое идут в " +
                "казну, камень, брёвна и металл на склад его долины. Казна видна вверху карты.",
            Task = "Займите рудник в рамке",
            Hex = () => MineAt,
            Enter = () => m.WorldMap?.FocusHex(MineAt.col, MineAt.row),
            Done = () => Cell(MineAt).Thing is { Owner: Holder.Ours },
            Status = () => Walking,
        });
        Add(new Step
        {
            Chapter = point, Title = "Войти в клетку",
            Text = "Пока герой стоит на клетке, кнопка «Войти в клетку» внизу или {EnterCell} открывает её долину. Герой " +
                "войдёт туда со всей армией.",
            Task = "Войдите в долину рудника",
            Point = "atlas:enter",
            Hex = () => MineAt,
            Done = () => !m.AtlasOpen && C.Here == Cell(MineAt) && HeroHere != null,
        });
        Add(new Step
        {
            Chapter = point, Title = "Гарнизон",
            Text = "Без гарнизона орда займёт рудник без боя. Выберите архитектора, двух рабочих и двух лучников и нажмите " +
                "значок крепости в панели выбранных («Оставить гарнизон»). Они останутся здесь, когда герой уйдёт.",
            Task = "Оставьте гарнизон с архитектором",
            Point = "console",
            Mark = () => m.Sim.Units.Find(u => !u.Dead && u.T == Kinds.Architect) is { } a ? ((int)a.X, (int)a.Z) : null,
            Done = () => m.Sim.Units.Exists(u => !u.Dead && u.T == Kinds.Architect && u.Garrison) && m.Sim.Units.Count(u => !u.Dead && u.Garrison) >= 3,
            Status = () => $"в гарнизоне: {m.Sim.Units.Count(u => !u.Dead && u.Garrison)}",
        });
        Add(new Step
        {
            Chapter = point, Title = "Архитектор",
            Text = "Архитектор сам размечает частокол или стену, ворота, башни и ров вокруг рудника, рабочие строят. На " +
                "его карточке выбирают размах стройки и видно, чего не хватает. Он работает и без вас.",
            Task = "Дождитесь первой разметки",
            Mark = () => m.Sim.Units.Find(u => !u.Dead && u.T == Kinds.Architect) is { } a ? ((int)a.X, (int)a.Z) : null,
            Enter = () =>
            {
                // (stone and logs in the valley's stores to build with)
                var st = m.Sim.Stock;
                if (st[Stockpile.Stone] < 20 * VoxelWorld.PerM3) st[Stockpile.Stone] = 20 * VoxelWorld.PerM3;
                if (st[Stockpile.Wood] < 20 * VoxelWorld.PerM3) st[Stockpile.Wood] = 20 * VoxelWorld.PerM3;
            },
            Done = () => m.Sim.ArchMarked > 0,
            Status = () => m.Sim.ArchDoing is { Length: > 0 } d ? $"архитектор: {d}" : null,
        });
        Add(new Step
        {
            Chapter = point, Title = "На карту",
            Text = "Остальные уходят с героем. Обведите рамкой всех своих, гарнизон останется. Откройте карту ({Board}) и " +
                "нажмите «Вывести на карту». Отряд выйдет у ближнего края.",
            Task = "Выведите героя на карту",
            Point = "atlas:side",
            Done = () => Hero != null,
        });

        // ---- the beasts
        const string beasts = "Существа";
        Add(new Step
        {
            Chapter = beasts, Title = "Существа",
            Text = "Многие клетки сторожат существа. Наведите курсор на волков в рамке: видно их число и шансы вашего " +
                "героя. Слабых бейте, сильных обходите. Кто намного слабее вас, разбегается или просится в отряд.",
            Task = "Наведите курсор на волков",
            Hex = () => WolvesAt,
            Enter = () => m.WorldMap?.FocusHex(WolvesAt.col, WolvesAt.row),
            Done = () => m.WorldMap is { } v && v.Hovered == WolvesAt,
        });
        Add(new Step
        {
            Chapter = beasts, Title = "Бой",
            Text = "Пошлите героя на волков. Дойдя, он остановится. «Войти» откроет их долину и бой в ней, «Бой сразу» " +
                "решит бой по силе отрядов. Огров рядом лучше обойти: они сильнее всей вашей армии.",
            Task = "Победите волков",
            Hex = () => WolvesAt,
            Done = () => Cell(WolvesAt).Sum.Keep.Count == 0,
            Status = () => Walking,
        });

        // ---- the treasury
        const string gold = "Казна";
        Add(new Step
        {
            Chapter = gold, Title = "Домой",
            Text = "Свои долины открываются и без героя. Нажмите Esc, чтобы снять выбор с героя, щёлкните по первой " +
                "долине и нажмите «Войти в долину».",
            Task = "Вернитесь в первую долину",
            Point = "atlas:side",
            Hex = () => HomeAt,
            Enter = () => { if (HomeAt is { } h) m.WorldMap?.FocusHex(h.col, h.row); },
            Done = () => !m.AtlasOpen && C.Here == C.Home,
        });
        Add(new Step
        {
            Chapter = gold, Title = "Таверна",
            Text = "Откройте карту, снимите выбор с героя и щёлкните по первой долине. На панели справа «Таверна»: в ней " +
                "каждую неделю 2 героя, каждый со своим войском. Герой стоит 600 золота.",
            Task = "Наймите второго героя",
            Point = "atlas:side",
            Hex = () => HomeAt,
            Done = () => Heroes >= 2,
        });
        Add(new Step
        {
            Chapter = gold, Title = "Торговец",
            Text = "Новый герой стоит у цитадели и уже выбран. Нажмите «Торговец» на панели справа. Артефакт даёт герою " +
                "умение, ход, обзор или доход.",
            Task = "Купите артефакт",
            Point = "atlas:side",
            Done = () => C.Parties.Any(p => p.Owner == Holder.Ours && p.Party.Commander?.Hero is { } h && (h.Worn.Any(k => k != null) || h.Bag.Count > 0)),
        });
        Add(new Step
        {
            Chapter = gold, Title = "Карточка героя",
            Text = "Щелчок по выбранному герою или кнопка «Карточка героя» открывает его карточку: уровень, опыт, умения и " +
                "артефакты. На новом уровне здесь выбирают умение.",
            Task = "Откройте карточку героя",
            Enter = () => m.Hud.MapPane?.CloseShop(),
            Done = () => m.Hud.HeroPane.IsOpen,
        });
        Add(new Step
        {
            Chapter = gold, Title = "Обоз",
            Text = "Обоз везёт брёвна, камень и металл из одной вашей долины в другую. Он ходит между долинами с " +
                "цитаделью или заставой, кнопка «Обоз» на их панели. В пути враг может отобрать груз.",
            Enter = () => m.Hud.HeroPane.Close(),
        });

        // ---- the horde
        const string horde = "Орда";
        Add(new Step
        {
            Chapter = horde, Title = "Орда",
            Text = "У орды свои герои и города. Город каждый день строит по постройке и раз в неделю растит войско. Герои " +
                "орды занимают рудники и нападают на ваши. В начале кампании орда несколько дней вас не трогает.",
        });
        Add(new Step
        {
            Chapter = horde, Title = "Бой за точку",
            Text = "К руднику идёт войско орды. Когда оно дойдёт, карта остановится и игра перейдёт в долину рудника. Бой " +
                "идёт, пока у одной стороны есть бойцы. Помогите гарнизону: выберите бойцов и щёлкните {RmbBy} по врагу. " +
                "Ожидание ускоряет {Speed}.",
            Task = "Отстойте рудник",
            Hex = () => MineAt,
            Enter = () =>
            {
                if (ArmySent || C == null) return;
                ArmySent = true;
                var mine = Cell(MineAt);
                var near = Enumerable.Range(0, 6).Select(s => C.Neighbour(mine, s))
                    .FirstOrDefault(n => n != null && n.Passable && n.Owner == Holder.None && n.Sum.Keep.Count == 0 && !n.Home);
                if (near == null) return;
                var p = new MapParty { Id = 9700, Owner = Holder.Horde, Party = new Party(), Q = near.Q, R = near.R, CameQ = near.Q, CameR = near.R, Aim = PartyAim.Strike };
                foreach (var t in new[] { Kinds.Goblin, Kinds.Goblin, Kinds.Goblin }) p.Party.Members.Add(Camp.Raider(t));
                C.Parties.Add(p);
                C.Send(p, mine);
            },
            Done = () => Main.DefencesHeld > HeldFrom,
            Status = () => m.Sim.Raids.Active && C.Here == Cell(MineAt) ? "бой за рудник" : C.Frozen != null ? "карта стоит" : "войско орды идёт к руднику",
        });

        // ---- the last of it
        const string last = "Напоследок";
        Add(new Step
        {
            Chapter = last, Title = "Туман и мини-карта",
            Text = "Откройте карту ({Board}). Тёмные клетки ещё не разведаны: герои видят вокруг себя, обсерватория " +
                "открывает больше. Мини-карта внизу слева показывает всю карту, щелчок по ней переносит туда вид.",
            Task = "Щёлкните по мини-карте",
            Point = "atlas:mini",
            Enter = () => JumpsFrom = m.WorldMap?.MiniJumps ?? 0,
            Done = () => m.WorldMap is { } v && v.MiniJumps > JumpsFrom,
        });
        Add(new Step
        {
            Chapter = last, Title = "Сохранение",
            Text = "{QuickSave} сохраняет кампанию, {QuickLoad} загружает её. В меню 3 ячейки для кампаний.",
            Task = "Сохраните кампанию",
            Enter = () => SavesFrom = Main.CampaignSaves,
            Done = () => Main.CampaignSaves > SavesFrom,
        });
        Add(new Step
        {
            Chapter = last, Title = "Обучение пройдено",
            Text = "Эту кампанию можно играть дальше. Новую начинают в меню «Кампания»: там выбирают размер карты от " +
                "36×36 до 144×144 клеток и сложность орды.",
        });
    }
}

public partial class Main
{
    /// <summary>
    /// The tutorial's campaign part begun: its own small campaign laid out for it (Tutorial.Prepare) in the tutorial's
    /// slot, the scene begun again on the map by the first valley, the part on its first step.
    /// </summary>
    public void StartCampaignTutorial()
    {
        string dir = Saves.CampaignDir(Tutorial.Slot);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Atlas = Campaign.NewWorld(Tutorial.Seed, 36);
        Atlas.Difficulty = Difficulty.Easy;
        Atlas.StartHeroes();
        Tutorial.Prepare(Atlas);
        CampSlot = Tutorial.Slot;
        Atlas.Write(dir);
        _entering = null; _atlasAtStart = true;
        PendingStart = null; PendingLoad = null;
        Tutorial.CampaignAt = 0;
        Tutorial.HeldFrom = DefencesHeld;
        GetTree().ReloadCurrentScene();
    }
}
