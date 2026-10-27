using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>What a thing of the big map is (the things of «Heroes»' adventure map, ours where we have them).</summary>
public enum ThingKind : byte
{
    Citadel, HordeTown, Lair, Mine, Pile, Chest, Campfire, Windmill, WaterWheel, Shrine, Dwelling, Monster, Trove, Signpost,
    Learning, Stables, Observatory,
}

/// <summary>The realm's resources: ours, and the four rare ones of «Heroes» (the author's choice of 26.10.2026).</summary>
public enum Res : byte { Gold, Logs, Stone, Earth, Metal, Mercury, Sulfur, Crystal, Gems }

/// <summary>
/// A thing of the big map, in its hex: a mine flagged by whoever walks in and paying each dawn; a pile of something or a
/// campfire, picked up once; a chest, its gold or its experience; a windmill or a water wheel giving once a week; a
/// learning stone, stables, an observatory, a signpost, a shrine's blessing; a dwelling hiring each week; a stack of
/// beasts or the dead in the way; a treasure of a valley (Campaign.Troves.cs); the towns. Who keeps it stands in its
/// cell's summary (<see cref="ValleySummary.Keep"/>): beaten, it can be had.
/// </summary>
public sealed class MapThing
{
    public ThingKind Kind;
    public Res Res;
    /// <summary>A mine's day, a pile's heap; a chest's tier.</summary>
    public float Amount;
    /// <summary>Whose flag stands on it (a mine, a dwelling).</summary>
    public Owner Owner;
    /// <summary>Picked up, opened, given: nothing more to have.</summary>
    public bool Taken;
    /// <summary>The week it last gave (a windmill, a water wheel), −1 never.</summary>
    public int Week = -1;
    public string Text;
    /// <summary>A dwelling's kind and how many wait to be hired.</summary>
    public UnitType Breed;
    public int Stock;
    public Blessing Blessing;
    /// <summary>The heroes (their seeds) who learnt at a learning stone.</summary>
    public readonly List<int> Visitors = new();

    /// <summary>What a mine of a resource gives a dawn.</summary>
    public static float Daily(Res r) => r switch
    {
        Res.Gold => 60, Res.Logs or Res.Stone => 4, Res.Metal => .5f, _ => 1,
    };

    /// <summary>A pile's heap, by a roll of the dice.</summary>
    public static float PileOf(Res r, double roll) => r switch
    {
        Res.Gold => 50 + (int)(roll * 6) * 10, Res.Logs or Res.Stone => 5 + (int)(roll * 6), Res.Metal => .5f + (int)(roll * 3) * .25f,
        _ => 2 + (int)(roll * 4),
    };

    /// <summary>A dwelling's growth a week.</summary>
    public static int Growth(UnitType t) => t == Kinds.Knight || t == Kinds.Rider ? 2 : t == Kinds.Halberdier || t == Kinds.Crossbowman ? 3 : 4;

    /// <summary>A stack keeping a hex, the stronger the further out: wolves and boars, then bears, then the dead and trolls.</summary>
    public static List<UnitType> Stack(int tier, Func<double> roll)
    {
        var list = new List<UnitType>();
        void Add(UnitType t, int n) { for (int i = 0; i < n; i++) list.Add(t); }
        double d = roll();
        switch (Math.Clamp(tier, 0, 5))
        {
            case 0: Add(d < .5 ? Kinds.Wolf : Kinds.Boar, 3 + (int)(roll() * 2)); break;
            case 1: Add(d < .5 ? Kinds.Wolf : Kinds.Boar, 4 + (int)(roll() * 3)); if (roll() < .4) Add(Kinds.Bear, 1); break;
            case 2: if (d < .5) { Add(Kinds.Bear, 2); Add(Kinds.Wolf, 4); } else Add(Kinds.Skeleton, 7 + (int)(roll() * 3)); break;
            case 3: Add(Kinds.Skeleton, 9 + (int)(roll() * 3)); Add(Kinds.Bear, 2); break;
            case 4: Add(Kinds.Skeleton, 12); Add(Kinds.Bear, 3); Add(Kinds.Troll, 1); break;
            default: Add(Kinds.Skeleton, 16); Add(Kinds.Bear, 3); Add(Kinds.Troll, 2); break;
        }
        return list;
    }

    public static string NameOf(Res r) => r switch
    {
        Res.Gold => "золото", Res.Logs => "брёвна", Res.Stone => "камень", Res.Earth => "земля", Res.Metal => "металл",
        Res.Mercury => "ртуть", Res.Sulfur => "сера", Res.Crystal => "кристаллы", _ => "самоцветы",
    };

    /// <summary>An amount of a resource as the game writes it: gold whole, the stores in cubic metres, the rare ones in pieces.</summary>
    public static string Amount_(Res r, double n) => r switch
    {
        Res.Gold => $"{n:0} зол.", Res.Logs or Res.Stone or Res.Earth or Res.Metal => $"{n:0.##} м³ ({NameOf(r)})", _ => $"{n:0} ({NameOf(r)})",
    };

    public static string MineName(Res r) => r switch
    {
        Res.Gold => "Золотой рудник", Res.Logs => "Лесопилка", Res.Stone => "Каменоломня", Res.Metal => "Рудник металла",
        Res.Mercury => "Ртутная лаборатория", Res.Sulfur => "Серная дюна", Res.Crystal => "Кристальная пещера", _ => "Пруд самоцветов",
    };

    public string Name => Kind switch
    {
        ThingKind.Citadel => "Цитадель", ThingKind.HordeTown => "Город орды", ThingKind.Lair => "Логово орды", ThingKind.Mine => MineName(Res),
        ThingKind.Pile => $"Куча: {NameOf(Res)}", ThingKind.Chest => "Сундук", ThingKind.Campfire => "Костёр", ThingKind.Windmill => "Мельница",
        ThingKind.WaterWheel => "Водяное колесо", ThingKind.Shrine => "Святыня", ThingKind.Dwelling => "Жилище", ThingKind.Monster => "Стража",
        ThingKind.Trove => "Сокровище", ThingKind.Signpost => "Указатель", ThingKind.Learning => "Камень знаний", ThingKind.Stables => "Конюшни",
        _ => "Обсерватория",
    };

    internal void Write(BinaryWriter w)
    {
        w.Write((byte)Owner); w.Write(Taken); w.Write(Week); w.Write(Stock);
        w.Write(Visitors.Count); foreach (int v in Visitors) w.Write(v);
    }

    internal void Read(BinaryReader r)
    {
        Owner = (Owner)r.ReadByte(); Taken = r.ReadBoolean(); Week = r.ReadInt32(); Stock = r.ReadInt32();
        Visitors.Clear();
        for (int n = r.ReadInt32(); n > 0; n--) Visitors.Add(r.ReadInt32());
    }
}

/// <summary>
/// The things of the big map as a hero has them, walking in (the rules of «Heroes»): what they give, once, a day or a
/// week; the realm's rare resources; a chest's choice and a dwelling's hiring waiting for the player.
/// </summary>
public sealed partial class Campaign
{
    /// <summary>The realm's rare resources: mercury, sulfur, crystals, gems (as <see cref="Res.Mercury"/> on).</summary>
    public readonly double[] Rare = new double[4];
    /// <summary>A chest opened by a hero, its choice waiting for the player: gold for the realm or experience for him.</summary>
    public readonly List<(MapParty p, MapCell cell)> Chests = new();
    /// <summary>A dwelling a hero stands at, for the player to hire from.</summary>
    public readonly List<(MapParty p, MapCell cell)> Dwellings = new();
    /// <summary>The stables add so many moves to a hero for the rest of the week.</summary>
    public const int StablesMoves = 400;
    /// <summary>A learning stone's experience, and a chest's (its gold the same, by its tier).</summary>
    public const int LearningXp = 50;
    /// <summary>An observatory lifts the fog so many hexes round.</summary>
    public const int ObservatoryReach = 10;

    /// <summary>The week of the map, from 0.</summary>
    public int Week => (Clock.Day - 1) / 7;

    public static (int gold, int xp) ChestOf(MapThing t) { int tier = (int)t.Amount; return (60 + 30 * tier, 50 + 25 * tier); }

    /// <summary>The realm's stock of a resource: gold and the rare ones the realm's own, the stores the first valley's.</summary>
    public double StockOf(Res r) => r switch
    {
        Res.Gold => InValley && Live != null ? Live.Stock.Gold : Gold,
        >= Res.Mercury => Rare[r - Res.Mercury],
        _ => StoreOf(Home, r),
    };

    double StoreOf(MapCell c, Res r)
    {
        if (c == null) return 0;
        if (InValley && Live != null && c == Here) return Live.Stock[StoreIndex(r)] / VoxelWorld.PerM3;
        return r switch { Res.Logs => c.Sum.Logs, Res.Stone => c.Sum.Stone, Res.Metal => c.Sum.Metal, _ => 0 };
    }

    static int StoreIndex(Res r) => r switch { Res.Logs => Stockpile.Wood, Res.Stone => Stockpile.Stone, Res.Earth => Stockpile.Earth, _ => Stockpile.Metal };

    /// <summary>
    /// A resource given the realm: gold to its treasury (the open valley's while one is), the rare ones to the realm's
    /// stock, the stores to the valley it came from while ours hold it, else to the nearest valley of ours with a
    /// stronghold (the open one straight in, any other kept for it till it is gone into, as its workers' are).
    /// </summary>
    public void Give(Res r, double n, MapCell near = null)
    {
        if (n <= 0) return;
        if (r == Res.Gold) { if (InValley && Live != null) Live.Stock.Gold += n; else Gold += n; return; }
        if (r >= Res.Mercury) { Rare[r - Res.Mercury] += n; if (Live != null) Live.Rare[r - Res.Mercury] = Rare[r - Res.Mercury]; return; }
        // a mine's stone, logs and metal to the stores of its own valley while ours hold it (to build its works with)
        var to = near is { Owner: Owner.Ours } ? near : NearestOurs(near) ?? Home;
        if (to == null) return;
        if (InValley && Live != null && to == Here) { Live.Stock[StoreIndex(r)] += n * VoxelWorld.PerM3; return; }
        switch (r)
        {
            case Res.Logs: to.Sum.Logs += n; break;
            case Res.Stone: to.Sum.Stone += n; break;
            case Res.Metal: to.Sum.Metal += n; break;
        }
    }

    /// <summary>The valley of ours with a stronghold nearest a hex (the first valley if none is given).</summary>
    MapCell NearestOurs(MapCell near)
    {
        if (near == null) return Home;
        MapCell best = null; int bd = int.MaxValue;
        foreach (var c in Strongholds)
        {
            int d = Distance(c.Q, c.R, near.Q, near.R);
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    /// <summary>The valleys of ours with a stronghold: the first valley, and those with an outpost raised.</summary>
    public IEnumerable<MapCell> Strongholds
    {
        get
        {
            foreach (var c in Cells)
                if (c.Owner == Owner.Ours && (c.Home || c.Sum.Raised > 0 || c.Outpost && c.Holding == Holding.Outpost && c.Sum.Raised > 0)) yield return c;
        }
    }

    /// <summary>
    /// A hero of ours walks into a hex of the big map with nothing in it to stop him: what lies there he has, as in
    /// «Heroes». The fog lifts round him first.
    /// </summary>
    public void Visit(MapParty p, MapCell cell)
    {
        // (a convoy's carters take nothing on their way: only a hero has what he walks into)
        if (cell?.Thing is not { } t || p.Owner != Owner.Ours || p.Convoy) return;
        var hero = p.Party.Commander?.Hero;
        switch (t.Kind)
        {
            case ThingKind.Pile when !t.Taken:
                t.Taken = true;
                Give(t.Res, t.Amount, cell);
                Tell(NewsKind.Taken, $"Подобрано: {MapThing.Amount_(t.Res, t.Amount)}.", cell, p);
                break;
            case ThingKind.Campfire when !t.Taken:
                t.Taken = true;
                int gold = 40 + (int)(Roll() * 4) * 10;
                double more = MapThing.PileOf(t.Res, Roll()) * .6;
                Give(Res.Gold, gold, cell); Give(t.Res, more, cell);
                Tell(NewsKind.Taken, $"У костра оставлено: {gold} зол. и {MapThing.Amount_(t.Res, more)}.", cell, p);
                break;
            case ThingKind.Chest when !t.Taken:
                if (!Chests.Exists(x => x.cell == cell)) Chests.Add((p, cell));
                var (cg, cx) = ChestOf(t);
                Tell(NewsKind.Taken, $"Сундук: {cg} зол. в казну или {cx} опыта герою.", cell, p);
                break;
            case ThingKind.Mine when t.Owner != Owner.Ours:
                t.Owner = Owner.Ours;
                Tell(NewsKind.Taken, $"{t.Name} наш: {MapThing.Amount_(t.Res, t.Amount)} в день.", cell, p);
                Reveal(cell, 2);
                break;
            case ThingKind.Windmill or ThingKind.WaterWheel:
                if (t.Week == Week) { Tell(NewsKind.Arrived, $"{t.Name}: на этой неделе уже взято.", cell, p); break; }
                t.Week = Week;
                if (t.Kind == ThingKind.WaterWheel) { Give(Res.Gold, 75, cell); Tell(NewsKind.Taken, "Водяное колесо: 75 зол.", cell, p); }
                else { int n = 3 + (int)(Roll() * 4); Give(t.Res, n, cell); Tell(NewsKind.Taken, $"Мельница: {MapThing.Amount_(t.Res, n)}.", cell, p); }
                break;
            case ThingKind.Learning when hero != null && !t.Visitors.Contains(hero.Seed):
                t.Visitors.Add(hero.Seed);
                int rose = Heroes.Gain(hero, LearningXp);
                Tell(NewsKind.Taken, $"Камень знаний: {hero.Name} +{LearningXp} опыта" + (rose > 0 ? $", уровень {hero.Level}." : "."), cell, p);
                break;
            case ThingKind.Stables when p.StablesWeek != Week:
                p.StablesWeek = Week;
                p.MovesMax = MovesOf(p);
                p.MovesLeft += StablesMoves;
                Tell(NewsKind.Taken, $"Конюшни: +{StablesMoves} хода до конца недели.", cell, p);
                break;
            case ThingKind.Observatory:
                Reveal(cell, ObservatoryReach);
                Tell(NewsKind.Arrived, "Обсерватория: туман рассеялся вокруг.", cell, p);
                break;
            case ThingKind.Signpost when t.Text != null:
                Tell(NewsKind.Arrived, $"Указатель: {t.Text}", cell, p);
                break;
            case ThingKind.Shrine when !t.Taken:
                t.Taken = true;
                if (!Blessings.Contains(t.Blessing)) Blessings.Add(t.Blessing);
                Tell(NewsKind.Taken, $"Святыня: {BlessingText(t.Blessing)}.", cell, p);
                break;
            case ThingKind.Dwelling:
                t.Owner = Owner.Ours;
                if (!Dwellings.Exists(x => x.cell == cell)) Dwellings.Add((p, cell));
                Tell(NewsKind.Arrived, $"Жилище: {t.Stock} готовы наняться ({t.Breed.Name}), {HireCost(t.Breed)} зол. каждый.", cell, p);
                break;
            case ThingKind.Trove when Untaken(cell) && cell.Sum.Keep.Count == 0 && cell.Trove != TroveKind.Shrine:
                TakeTrove(cell, p);
                break;
        }
        HexChanged(cell);
    }

    /// <summary>A chest's choice: its gold to the realm, or its experience to the hero who opened it.</summary>
    public string OpenChest(MapCell cell, bool gold)
    {
        int i = Chests.FindIndex(x => x.cell == cell);
        if (i < 0 || cell.Thing is not { Kind: ThingKind.Chest, Taken: false } t) return null;
        var p = Chests[i].p;
        Chests.RemoveAt(i);
        t.Taken = true;
        var (g, xp) = ChestOf(t);
        string said;
        if (gold || p.Party.Commander?.Hero == null) { Give(Res.Gold, g, cell); said = $"Из сундука: {g} зол."; }
        else
        {
            var hero = p.Party.Commander.Hero;
            int rose = Heroes.Gain(hero, xp);
            said = $"Из сундука: {hero.Name} +{xp} опыта" + (rose > 0 ? $", уровень {hero.Level}." : ".");
        }
        said += ChestArtifact(t, p, cell);
        Tell(NewsKind.Taken, said, cell, p);
        HexChanged(cell);
        return said;
    }

    /// <summary>What a man of a kind costs to hire at a dwelling of the map, gold.</summary>
    public static int HireCost(UnitType t) => t == Kinds.Knight ? 60 : t == Kinds.Rider || t == Kinds.Halberdier ? 40 : t == Kinds.Crossbowman || t == Kinds.Swordsman ? 30 : 20;

    /// <summary>So many hired at a dwelling into the army of the hero standing at it, as the gold goes; how many were.</summary>
    public int Hire(MapCell cell, int n)
    {
        int i = Dwellings.FindIndex(x => x.cell == cell);
        if (i < 0 || cell.Thing is not { Kind: ThingKind.Dwelling } t) return 0;
        var p = Dwellings[i].p;
        if (!Parties.Contains(p) || p.Q != cell.Q || p.R != cell.R) { Dwellings.RemoveAt(i); return 0; }
        int cost = HireCost(t.Breed), got = 0;
        int key = KeyOf(Home ?? cell);
        while (got < n && t.Stock > 0 && StockOf(Res.Gold) >= cost)
        {
            if (InValley && Live != null) Live.Stock.Gold -= cost; else Gold -= cost;
            t.Stock--; got++;
            p.Party.Members.Add(new Traveller { Kind = t.Breed, Look = got % Math.Max(1, Bodies.Looks(t.Breed)), Hp = t.Breed.Hp, MaxHp = t.Breed.Hp, Billet = key });
        }
        if (got > 0) Tell(NewsKind.Taken, $"Нанято в жилище: {got} ({t.Breed.Name}), {got * cost} зол.", cell, p);
        if (t.Stock == 0) Dwellings.RemoveAt(i);
        return got;
    }

    /// <summary>
    /// The big map's dawn: the mines pay their day (ours to the realm, the horde's to its musters), the heroes' moves are
    /// whole again; a new week grows the dwellings.
    /// </summary>
    void WorldDawn()
    {
        if (!Big) return;
        bool week = (Clock.Day - 1) % 7 == 0;
        foreach (var c in Cells)
        {
            if (c.Thing is not { } t) continue;
            if (t.Kind == ThingKind.Mine && t.Owner == Owner.Ours) Give(t.Res, t.Amount, c);
            if (week && t.Kind == ThingKind.Dwelling) t.Stock = Math.Min(t.Stock + MapThing.Growth(t.Breed), MapThing.Growth(t.Breed) * 3);
        }
        foreach (var p in Parties)
        {
            p.MovesMax = MovesOf(p);
            p.MovesLeft = p.MovesMax;
            p.Tired = false;
            p.Steps = MapWalk.StepsOf(this, p);
        }
        if (week && Clock.Day > 1) Tell(NewsKind.Grew, $"Неделя {Week + 1}. Жилища пополнились.", Home, null);
    }

    /// <summary>The mines of the realm and what they give a day, for the frame.</summary>
    public (int mines, double[] daily) Mines()
    {
        var daily = new double[9];
        int n = 0;
        foreach (var c in Cells)
            if (c.Thing is { Kind: ThingKind.Mine, Owner: Owner.Ours } t) { n++; daily[(int)t.Res] += t.Amount; }
        return (n, daily);
    }

    // ------------------------------------------------------------ the fog of the big map

    bool[] _explored;
    /// <summary>A hex's look or fog changed, for the drawing (CampaignHexes.cs).</summary>
    public event Action<int, int> HexChangedAt;

    void HexChanged(MapCell c) { if (Big && c != null) HexChangedAt?.Invoke(c.Col, c.Row); }

    /// <summary>Whether a hex of the big map has been seen (it stays seen, as in «Heroes»).</summary>
    public bool Explored(MapCell c) => !Big || _explored != null && c != null && _explored[IndexOf(c)];

    /// <summary>The fog lifted so many hexes round a hex.</summary>
    public void Reveal(MapCell c, int reach)
    {
        if (!Big || c == null) return;
        _explored ??= new bool[Width * Height];
        for (int dr = -reach; dr <= reach; dr++)
            for (int dq = -reach; dq <= reach; dq++)
            {
                if (Math.Abs(dq + dr) > reach) continue;
                var h = At(c.Q + dq, c.R + dr);
                if (h == null) continue;
                int i = IndexOf(h);
                if (_explored[i]) continue;
                _explored[i] = true;
                HexChangedAt?.Invoke(h.Col, h.Row);
            }
    }

    /// <summary>How far a hero of ours sees on the big map: four hexes, more with Разведка; a scouting party six.</summary>
    public static int SightOf(MapParty p) => (p.Scouting ? 6 : 4 + GradeOf(p, Skill.Scouting)) + (p.Party.Commander?.Hero?.ArtSight ?? 0);

    void WriteWorld(BinaryWriter w)
    {
        w.Write(Width); w.Write(Height);
        if (!Big) return;
        foreach (double r in Rare) w.Write(r);
        // the fog, a bit a hex
        var bits = new byte[(Width * Height + 7) / 8];
        if (_explored != null) for (int i = 0; i < _explored.Length; i++) if (_explored[i]) bits[i >> 3] |= (byte)(1 << (i & 7));
        w.Write(bits.Length); w.Write(bits);
        // what changed of the things and the hexes: only those the game touched
        var changed = new List<MapCell>();
        foreach (var c in Cells) if (c.Thing != null || c.Owner != Owner.None || c.Visited || c.Outpost || c.TroveTaken) changed.Add(c);
        w.Write(changed.Count);
        foreach (var c in changed)
        {
            w.Write(IndexOf(c)); w.Write((byte)c.Owner); w.Write((byte)c.Holding); w.Write(c.Visited); w.Write(c.Outpost); w.Write(c.TroveTaken);
            w.Write(c.Thing != null);
            c.Thing?.Write(w);
        }
        w.Write(Chests.Count); foreach (var (p, c) in Chests) { w.Write(p.Id); w.Write(IndexOf(c)); }
    }

    void ReadWorld(BinaryReader r)
    {
        Width = r.ReadInt32(); Height = r.ReadInt32();
        if (!Big) return;
        for (int i = 0; i < Rare.Length; i++) Rare[i] = r.ReadDouble();
        var bits = r.ReadBytes(r.ReadInt32());
        _explored = new bool[Width * Height];
        for (int i = 0; i < _explored.Length; i++) _explored[i] = (bits[i >> 3] >> (i & 7) & 1) != 0;
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var c = _grid[r.ReadInt32()];
            c.Owner = (Owner)r.ReadByte(); c.Holding = (Holding)r.ReadByte(); c.Visited = r.ReadBoolean(); c.Outpost = r.ReadBoolean(); c.TroveTaken = r.ReadBoolean();
            // (a thing the map was not made with — none such now — read past)
            if (r.ReadBoolean()) (c.Thing ?? new MapThing()).Read(r);
        }
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            int id = r.ReadInt32(), i = r.ReadInt32();
            var p = Parties.Find(x => x.Id == id);
            if (p != null) Chests.Add((p, _grid[i]));
        }
    }
}
