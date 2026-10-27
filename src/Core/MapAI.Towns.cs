using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>A building of a town of the horde: each breeds its kinds a week, or gives its town walls, gold or a hero more.</summary>
public enum HordeWork : byte { Huts, Pit, Burrow, ChiefHall, TrollDen, WarCamp, Shaman, StoneWall, Yard, Alchemist, GiantCave }

/// <summary>
/// A town or a lair of the horde as the big map keeps it (MapAI.Towns.cs): what is built in it and what waits in it to
/// be hired. A lair breeds goblins and orcs by itself and keeps a garrison, but builds nothing and has no hero of its own.
/// </summary>
public sealed class HordeTown
{
    public int Key;
    public bool Lair;
    /// <summary>The buildings standing, a bit each (<see cref="HordeWork"/>).</summary>
    public uint Works;
    /// <summary>How many of each kind of the horde's (<see cref="Campaign.HordeKinds"/>) wait to be hired.</summary>
    public readonly int[] Pool = new int[Campaign.HordeKinds.Length];
    /// <summary>The day it last built: one building a day.</summary>
    public int BuiltDay;

    public bool Has(HordeWork w) => (Works >> (int)w & 1) != 0;

    internal void Write(BinaryWriter w)
    {
        w.Write(Key); w.Write(Lair); w.Write(Works); w.Write(BuiltDay);
        w.Write(Pool.Length); foreach (int n in Pool) w.Write(n);
    }

    internal static HordeTown Read(BinaryReader r)
    {
        var t = new HordeTown { Key = r.ReadInt32(), Lair = r.ReadBoolean(), Works = r.ReadUInt32(), BuiltDay = r.ReadInt32() };
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++) { int v = r.ReadInt32(); if (i < t.Pool.Length) t.Pool[i] = v; }
        return t;
    }
}

/// <summary>
/// The horde's towns on the big map, as a town of «Heroes» is (docs/handoff/13-world-map.md, 2.18). Each earns its gold
/// a dawn (more with the chief's tent), and the horde's mines give theirs; a town builds one building a day while the
/// treasury allows, in the order a Heroes player would, each one breeding its kinds a week (the goblins' huts and the
/// fighting pit from the start; then the sappers' burrow, the trolls' den, the shamans' hut, the yard of engines, the
/// brewery of acid, the hill giant's cave), or giving the town stone walls or a hero more (the war camp). At the start of
/// a week the towns breed; what is bred waits in the town to be hired, by its garrison when it is thin and by the
/// horde's heroes standing in it. A lair breeds a few goblins and orcs by itself.
/// </summary>
public sealed partial class Campaign
{
    /// <summary>The kinds a town of the horde breeds and hires, in the order the pool keeps them.</summary>
    public static readonly UnitType[] HordeKinds =
    {
        Kinds.Goblin, Kinds.Thief, Kinds.Orc, Kinds.Sapper, Kinds.Miner, Kinds.Shaman, Kinds.Witch, Kinds.Troll,
        Kinds.Alchemist, Kinds.Necromancer, Kinds.Ram, Kinds.Catapult, Kinds.Mantlet, Kinds.HillGiant,
    };

    /// <summary>What one of a kind of the horde's costs to hire, gold.</summary>
    public static int HordeCost(UnitType t) =>
        t == Kinds.HillGiant ? 350 : t == Kinds.Catapult ? 140 : t == Kinds.Troll ? 110 : t == Kinds.Ram ? 90 : t == Kinds.Necromancer ? 90
        : t == Kinds.Witch || t == Kinds.Alchemist ? 70 : t == Kinds.Shaman ? 60 : t == Kinds.Mantlet ? 50 : t == Kinds.Orc ? 30
        : t == Kinds.Sapper || t == Kinds.Miner ? 25 : t == Kinds.Thief ? 20 : 12;

    /// <summary>A building of the horde's: its name, what it costs, the building it wants first, and what it breeds a week.</summary>
    public sealed class WorkDef
    {
        public HordeWork Work;
        public string Name;
        public int Gold;
        public double Logs, Stone, Metal;
        public Res Rare;
        public int RareN;
        public HordeWork? Needs;
        public (UnitType kind, int week)[] Breeds = Array.Empty<(UnitType, int)>();
    }

    public static readonly WorkDef[] HordeWorks =
    {
        new() { Work = HordeWork.Huts, Name = "Хижины гоблинов", Breeds = new[] { (Kinds.Goblin, 12), (Kinds.Thief, 1) } },
        new() { Work = HordeWork.Pit, Name = "Бойцовая яма", Gold = 300, Logs = 5, Breeds = new[] { (Kinds.Orc, 6) } },
        new() { Work = HordeWork.Burrow, Name = "Нора сапёров", Gold = 250, Logs = 5, Breeds = new[] { (Kinds.Sapper, 4), (Kinds.Miner, 2) } },
        new() { Work = HordeWork.ChiefHall, Name = "Шатёр вождя", Gold = 400, Logs = 5, Stone = 5 },
        new() { Work = HordeWork.TrollDen, Name = "Логово троллей", Gold = 650, Stone = 10, Needs = HordeWork.Pit, Breeds = new[] { (Kinds.Troll, 2) } },
        new() { Work = HordeWork.WarCamp, Name = "Ставка вождя", Gold = 600, Logs = 8, Needs = HordeWork.Pit },
        new() { Work = HordeWork.Shaman, Name = "Хижина шамана", Gold = 450, Logs = 5, Metal = 1, Needs = HordeWork.Pit, Breeds = new[] { (Kinds.Shaman, 2), (Kinds.Witch, 1) } },
        new() { Work = HordeWork.StoneWall, Name = "Каменный вал", Gold = 700, Stone = 20 },
        new() { Work = HordeWork.Yard, Name = "Двор машин", Gold = 550, Logs = 10, Metal = 1, Needs = HordeWork.Burrow, Breeds = new[] { (Kinds.Ram, 1), (Kinds.Catapult, 1), (Kinds.Mantlet, 1) } },
        new() { Work = HordeWork.Alchemist, Name = "Варильня", Gold = 500, Rare = Res.Mercury, RareN = 3, Needs = HordeWork.Shaman, Breeds = new[] { (Kinds.Alchemist, 1), (Kinds.Necromancer, 1) } },
        new() { Work = HordeWork.GiantCave, Name = "Пещера великана", Gold = 1200, Stone = 15, Rare = Res.Crystal, RareN = 3, Needs = HordeWork.TrollDen, Breeds = new[] { (Kinds.HillGiant, 1) } },
    };

    /// <summary>What a lair breeds a week by itself.</summary>
    static readonly (UnitType kind, int week)[] LairBreeds = { (Kinds.Goblin, 6), (Kinds.Orc, 2) };

    /// <summary>The order a town of the horde builds in: its army first, its gold and its walls between.</summary>
    static readonly HordeWork[] BuildOrder =
    {
        HordeWork.Pit, HordeWork.Burrow, HordeWork.ChiefHall, HordeWork.TrollDen, HordeWork.WarCamp, HordeWork.Shaman,
        HordeWork.StoneWall, HordeWork.Yard, HordeWork.Alchemist, HordeWork.GiantCave,
    };

    /// <summary>A town's gold a dawn, and a lair's; the chief's tent's more.</summary>
    public const int TownGold = 60, LairGold = 15, HallGold = 50;
    /// <summary>A town's walls: its palisade, its stone.</summary>
    public const float PalisadeWalls = .5f, StoneWalls = .85f;
    /// <summary>What the horde's treasury starts with: gold, logs, stone, metal.</summary>
    const int StartGold = 600, StartLogs = 15, StartStone = 15, StartMetal = 2;

    /// <summary>The state the map keeps for a town or a lair of the horde, made as it is first asked for.</summary>
    public HordeTown TownOf(MapCell c)
    {
        int key = KeyOf(c);
        if (Realm.Towns.TryGetValue(key, out var t)) return t;
        t = new HordeTown { Key = key, Lair = c.Thing?.Kind != ThingKind.HordeTown };
        t.Works |= 1u << (int)HordeWork.Huts;
        if (!t.Lair) t.Works |= 1u << (int)HordeWork.Pit;
        Breed(t);
        Realm.Towns[key] = t;
        return t;
    }

    static int KindIndex(UnitType k) => Array.IndexOf(HordeKinds, k);

    /// <summary>A week's breeding of a town into what waits in it to be hired (three weeks' worth at the most).</summary>
    internal void Breed(HordeTown t)
    {
        void Add((UnitType kind, int week)[] what)
        {
            foreach (var (kind, week) in what)
            {
                int i = KindIndex(kind), n = Math.Max(1, (int)Math.Round(week * HordeGrowth));
                if (i >= 0) t.Pool[i] = Math.Min(t.Pool[i] + n, 3 * n);
            }
        }
        if (t.Lair) { Add(LairBreeds); return; }
        foreach (var d in HordeWorks) if (t.Has(d.Work)) Add(d.Breeds);
    }

    /// <summary>Whether the horde's treasury holds what a building costs.</summary>
    bool Affords(WorkDef d)
    {
        var s = Realm.Stock;
        return s[(int)Res.Gold] >= d.Gold && s[(int)Res.Logs] >= d.Logs && s[(int)Res.Stone] >= d.Stone && s[(int)Res.Metal] >= d.Metal
            && (d.RareN == 0 || s[(int)d.Rare] >= d.RareN);
    }

    void Pay(WorkDef d)
    {
        var s = Realm.Stock;
        s[(int)Res.Gold] -= d.Gold; s[(int)Res.Logs] -= d.Logs; s[(int)Res.Stone] -= d.Stone; s[(int)Res.Metal] -= d.Metal;
        if (d.RareN > 0) s[(int)d.Rare] -= d.RareN;
    }

    /// <summary>
    /// A town of the horde builds what comes next in its order and it can pay for, one a day: the first of the order not
    /// standing whose building before it stands. The walls rise with the stone wall.
    /// </summary>
    void Build(HordeTown t, MapCell cell)
    {
        if (t.Lair || t.BuiltDay == Clock.Day) return;
        foreach (var w in BuildOrder)
        {
            if (t.Has(w)) continue;
            var d = HordeWorks[(int)w];
            if (d.Needs is { } need && !t.Has(need)) continue;
            // (a building it cannot pay for yet waits, the order kept; one wanting a rare resource the horde has none of is
            // gone by till it has)
            if (!Affords(d)) { if (d.RareN > 0 && Realm.Stock[(int)d.Rare] < d.RareN) continue; return; }
            Pay(d);
            t.Works |= 1u << (int)w;
            t.BuiltDay = Clock.Day;
            Realm.Built++;
            if (w == HordeWork.StoneWall) cell.Sum.Walls = Math.Max(cell.Sum.Walls, StoneWalls);
            // what it breeds comes for the rest of the week at once, as a Heroes town's new dwelling has its first growth
            foreach (var (kind, week) in d.Breeds)
            {
                int i = KindIndex(kind);
                if (i >= 0) t.Pool[i] += Math.Max(1, (int)Math.Round(week * HordeGrowth));
            }
            return;
        }
    }

    /// <summary>The garrison a town of the horde keeps at the least, by the weeks gone: more each week, a lair fewer.</summary>
    int GarrisonWant(HordeTown t) => t.Lair ? Math.Min(16, 4 + Week) : Math.Min(40, 10 + 2 * Week);

    /// <summary>
    /// So many hired of what waits in a town, the strongest kinds first, into a list (a hero's army, the town's garrison),
    /// gold kept back; engines only when they are wanted, those who only come raiding only into an army. How many were.
    /// </summary>
    int HireFrom(HordeTown t, List<Traveller> into, int most, double keep, bool engines, bool army)
    {
        int got = 0;
        var gold = Realm.Stock;
        // the kinds by their worth in a battle, the strongest first
        for (int pass = 0; pass < 2 && got < most; pass++)
            foreach (int i in HireOrder)
            {
                var k = HordeKinds[i];
                if (Kinds.IsEngine(k) && (!engines || pass == 0)) continue;
                if (!army && !Sim.Holds(k)) continue;
                int engines0 = 0;
                foreach (var m in into) if (Kinds.IsEngine(m.Kind)) engines0++;
                while (got < most && t.Pool[i] > 0 && gold[(int)Res.Gold] - HordeCost(k) >= keep && (!Kinds.IsEngine(k) || engines0 < 2))
                {
                    gold[(int)Res.Gold] -= HordeCost(k);
                    t.Pool[i]--; got++;
                    into.Add(Raider(k));
                    if (Kinds.IsEngine(k)) engines0++;
                }
            }
        Realm.Mustered += got;
        return got;
    }

    static int[] _hireOrder;
    /// <summary>The horde's kinds by their worth in a battle, the strongest first.</summary>
    static int[] HireOrder
    {
        get
        {
            if (_hireOrder != null) return _hireOrder;
            var idx = new List<int>();
            for (int i = 0; i < HordeKinds.Length; i++) idx.Add(i);
            idx.Sort((a, b) =>
            {
                var (ha, da) = QuickBattle.Worth(HordeKinds[a], HordeKinds[a].Hp);
                var (hb, db) = QuickBattle.Worth(HordeKinds[b], HordeKinds[b].Hp);
                return (hb * db).CompareTo(ha * da);
            });
            return _hireOrder = idx.ToArray();
        }
    }

    /// <summary>
    /// The horde's towns at a dawn: each earns its gold and the horde's mines give theirs; at a week's start each breeds;
    /// each builds what comes next; a thin garrison is hired up from what waits in its town.
    /// </summary>
    void TownsDawn(List<MapCell> towns)
    {
        bool week = (Clock.Day - 1) % 7 == 0;
        var s = Realm.Stock;
        foreach (var cell in towns)
        {
            var t = TownOf(cell);
            s[(int)Res.Gold] += (t.Lair ? LairGold : TownGold + (t.Has(HordeWork.ChiefHall) ? HallGold : 0)) * HordeIncome;
            if (week) Breed(t);
        }
        // the horde's mines
        foreach (int i in HordeMines())
        {
            var c = Cells[i];
            if (c.Thing is { Kind: ThingKind.Mine, Owner: Owner.Horde } m) s[(int)m.Res] += m.Amount * HordeIncome;
        }
        foreach (var cell in towns) Build(TownOf(cell), cell);
        foreach (var cell in towns)
        {
            var t = TownOf(cell);
            int want = GarrisonWant(t), have = cell.Sum.Holders().Count;
            // (it keeps gold for a building and a hero's hiring: a third of the treasury at the most for the garrison)
            if (have < want) HireFrom(t, cell.Sum.Keep, want - have, s[(int)Res.Gold] * .66, engines: false, army: false);
        }
    }

    List<int> _hordeMines;
    int _hordeMinesDay = -1;

    /// <summary>The cells (their index in <see cref="Cells"/>) of the mines of the big map, gathered once a day.</summary>
    List<int> HordeMines()
    {
        if (_hordeMines != null && _hordeMinesDay == Clock.Day) return _hordeMines;
        _hordeMines ??= new List<int>();
        _hordeMines.Clear();
        for (int i = 0; i < Cells.Count; i++) if (Cells[i].Thing is { Kind: ThingKind.Mine }) _hordeMines.Add(i);
        _hordeMinesDay = Clock.Day;
        return _hordeMines;
    }

    /// <summary>What the horde's realm has built and holds, said for the harness: its treasury, each town's buildings.</summary>
    public string HordeReport()
    {
        var s = Realm.Stock;
        var towns = new List<string>();
        foreach (var c in Lairs)
        {
            var t = TownOf(c);
            if (t.Lair) continue;
            int n = 0;
            foreach (var d in HordeWorks) if (t.Has(d.Work)) n++;
            towns.Add($"{c.Col},{c.Row}: построек {n}, гарнизон {c.Sum.Holders().Count}");
        }
        int heroes = 0, best = 0;
        foreach (var p in HeroesOf(Owner.Horde)) { heroes++; best = Math.Max(best, p.Lord.Hero.Level); }
        return $"орда: золото {s[(int)Res.Gold]:0}, брёвна {s[(int)Res.Logs]:0}, камень {s[(int)Res.Stone]:0}, металл {s[(int)Res.Metal]:0.0}; героев {heroes} (лучший ур. {best}); {string.Join("; ", towns)}";
    }
}
