using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>What a night was, as the dawn after it reads it.</summary>
public sealed class NightReport
{
    public int Number, Killed, Lost, Leaders, LeadersKilled;
    /// <summary>The share of the citadel's strength the night cost (0 to 1).</summary>
    public float KeepLost;
    /// <summary>The three stars: the citadel unharmed, none of ours lost, every band's leader killed.</summary>
    public bool Unharmed, NoneLost, Headless;
    public int Stars => (Unharmed ? 1 : 0) + (NoneLost ? 1 : 0) + (Headless ? 1 : 0);
    /// <summary>Those of ours who felled the most, as "Лучник №4" and how many.</summary>
    public readonly List<(string who, int felled)> Best = new();
}

/// <summary>What a dawn's gift is: people come to the fort, a caravan's goods, a sign of the old powers, a leader's trophy.</summary>
public enum GiftKind : byte { Newcomers, Caravan, Omen, Trophy, Relic }

/// <summary>One gift a dawn offers: one of three (or four) is taken, the rest go.</summary>
public sealed class Gift
{
    public GiftKind Kind;
    public string Key, Title, About;
}

/// <summary>
/// The dawn after a night held: what the night was — how many fell of the horde and of ours, what the citadel lost,
/// which leaders were killed, who of ours felled the most — and the stars for how well it went: the citadel unharmed,
/// none of ours lost, every band's leader killed. Each star adds to the wave's gold and knowledge. And a gift to
/// choose, each with its reason in the valley: people come to a fort that held (workers, archers, a swordsman, a
/// priest, a knight), a caravan offers its goods, the old powers give a sign, and a leader killed leaves a trophy that
/// teaches something of the lore for nothing. Two stars or more, and there are four to choose from. What is not
/// chosen by the next horn is gone.
/// </summary>
public sealed partial class Sim
{
    public NightReport LastNight;
    /// <summary>The dawn's gifts, waiting to be chosen; empty once one is taken, or at the next horn.</summary>
    public readonly List<Gift> Gifts = new();
    public event Action<Gift> GiftTaken;

    // the night as it goes: kills before it, ours lost, the citadel at the horn, leaders killed, who felled whom
    int _night0Kills, _nightLost, _nightLeaders, _nightLeadersKilled;
    float _nightKeep0;
    readonly Dictionary<int, int> _nightFelled = new();

    /// <summary>The horn: the night's tally begins, and a dawn's gifts not taken are gone.</summary>
    internal void NightBegins(int bands)
    {
        if (Gifts.Count > 0) { Gifts.Clear(); Say("Дар рассвета так и не выбран: обоз ушёл, пришлые разошлись.", 2); }
        _night0Kills = Kills; _nightLost = 0; _nightLeaders = bands; _nightLeadersKilled = 0; _nightChampion = 0;
        _nightKeep0 = KeepHp;
        _nightFelled.Clear();
    }

    /// <summary>One fell in the night: counted for the dawn.</summary>
    void NightFell(Unit u, Unit by)
    {
        if (!Waves.Active) return;
        if (u.Team == Team.Player && u.T != Kinds.Risen) _nightLost++;
        if (u.Team != Team.Horde || u.T == Kinds.Thrall) return;
        if (u.Leader) _nightLeadersKilled++;
        if (by is { Team: Team.Player }) _nightFelled[by.Id] = _nightFelled.GetValueOrDefault(by.Id) + 1;
    }

    /// <summary>A night beaten back: its report, the stars' extra reward, and the gifts to choose from.</summary>
    internal void Dawn(int night)
    {
        var r = new NightReport
        {
            Number = night, Killed = Kills - _night0Kills, Lost = _nightLost, Leaders = _nightLeaders, LeadersKilled = _nightLeadersKilled,
            KeepLost = Math.Clamp((_nightKeep0 - KeepHp) / Math.Max(1, KeepMax), 0, 1),
        };
        r.Unharmed = r.KeepLost < .02f;
        r.NoneLost = r.Lost == 0;
        r.Headless = r.Leaders > 0 && r.LeadersKilled >= r.Leaders;
        var best = new List<(int id, int n)>();
        foreach (var (id, n) in _nightFelled) best.Add((id, n));
        best.Sort((a, b) => b.n != a.n ? b.n.CompareTo(a.n) : a.id.CompareTo(b.id));
        foreach (var (id, n) in best)
        {
            if (r.Best.Count >= 3) break;
            var u = Units.Find(o => o.Id == id);
            if (u != null) r.Best.Add(($"{u.T.Name} №{id}", n));
        }
        LastNight = r;
        OpenTiers(night);
        // (the stars' reward and the gifts are a siege of one's own: not the tutorial's, nor the bench's unless asked)
        if (NoErrands) return;
        // each star adds to the wave's reward
        if (r.Stars > 0)
        {
            Stock.Gold += 15 * r.Stars;
            Knowledge += r.Stars;
            Say($"Звёзды ночи: {r.Stars} из 3 — ещё {15 * r.Stars} золота и {r.Stars} {Plural(r.Stars, "знание", "знания", "знаний")}.", 1);
        }
        if (night >= Waves.Total) return;
        Gifts.Clear();
        // a champion fell this night: his trophies, the legendary ones, are the dawn's gifts
        if (_nightChampion > 0)
        {
            Gifts.AddRange(RelicGifts(night));
            _nightChampion = 0;
            if (Gifts.Count > 0) { Say("Рассвет: трофеи вожака главы — выберите один из трёх, до следующего рога.", 1); return; }
        }
        var trophy = Trophy(night);
        var deal = new List<Gift>();
        if (trophy != null) deal.Add(trophy);
        foreach (var k in new[] { GiftKind.Newcomers, GiftKind.Caravan, GiftKind.Omen })
        {
            var g = GiftOf(k, night, 0);
            if (g != null) deal.Add(g);
        }
        int want = r.Stars >= 2 ? 4 : 3;
        // short of four: a second caravan's goods
        if (deal.Count < want && GiftOf(GiftKind.Caravan, night, 1) is { } more) deal.Add(more);
        for (int i = 0; i < Math.Min(want, deal.Count); i++) Gifts.Add(deal[i]);
        if (Gifts.Count > 0) Say("Рассвет: выберите один из даров — до следующего рога.", 1);
    }

    /// <summary>A draw of the dawn's own, so that the valley's other chances run as they did.</summary>
    double DawnDraw(int night, int salt) => Hash.H3(night * 977 + salt, Kills, ErrandsDone + 13);

    /// <summary>A gift of a kind for this dawn (the <paramref name="nth"/> of that kind), or null if there is none to give.</summary>
    Gift GiftOf(GiftKind kind, int night, int nth)
    {
        switch (kind)
        {
            case GiftKind.Newcomers:
            {
                int room = Places - Household();
                var who = new List<(string key, string title, string about, int n)>();
                if (room >= 3) who.Add(("workers", "Три работника", "Беженцы из сожжённых деревень просятся в крепость: три рабочих рук.", 3));
                if (room >= 2) who.Add(("archers", "Двое лучников", "Охотники из леса пришли с луками: двое лучников без найма.", 2));
                if (room >= 1) who.Add(("swordsman", "Мечник", "Бывалый воин просит места у очага: мечник без найма.", 1));
                if (room >= 1 && Mines.Exists(m => m.Power)) who.Add(("priest", "Жрец-отшельник", "Отшельник с холмов пришёл служить старым силам: жрец без найма.", 1));
                if (room >= 2 && night >= 4) who.Add(("knight", "Странствующий рыцарь", "Рыцарь без господина предлагает свой меч до конца осады: рыцарь без найма и без знания.", 2));
                if (who.Count == 0) return null;
                var w = who[(int)(DawnDraw(night, 1 + nth) * who.Count) % who.Count];
                return new Gift { Kind = kind, Key = w.key, Title = w.title, About = w.about };
            }
            case GiftKind.Caravan:
            {
                var goods = new List<(string key, string title, string about)>
                {
                    ("stone", "Обоз камня", "Каменщики с равнины везут тёсаный камень: 8 м³ камня."),
                    ("wood", "Обоз брёвен", "Лесорубы сплавили плоты: 8 м³ брёвен."),
                    ("earth", "Обоз земли", "Возы с глиной и дёрном: 12 м³ земли."),
                    ("gold", "Купеческий кошель", $"Купцы платят за защиту дороги: {50 + 10 * night} золота."),
                };
                int i0 = (int)(DawnDraw(night, 7) * goods.Count) % goods.Count;
                var g = goods[(i0 + nth * 2) % goods.Count];
                return new Gift { Kind = kind, Key = g.key, Title = g.title, About = g.about };
            }
            case GiftKind.Omen:
            {
                var signs = new List<(string key, string title, string about)>
                {
                    ("favour", "Знамение", "Над долиной пролетел белый ворон: старые силы смотрят на вас — 15 вмешательства."),
                    ("revelation", "Откровение", "Жрецам приснилось слово силы: одно откровение — им открывают заклинания."),
                };
                var s = signs[(int)(DawnDraw(night, 11) * signs.Count) % signs.Count];
                return new Gift { Kind = kind, Key = s.key, Title = s.title, About = s.about };
            }
        }
        return null;
    }

    /// <summary>A leader killed this night leaves a trophy: something of the lore learnt from it for nothing.</summary>
    Gift Trophy(int night)
    {
        if (_nightLeadersKilled == 0) return null;
        var can = new List<Lore>();
        foreach (var l in Lores) if (l.Coin == Coin.Knowledge && l.Spell == null && !Knows(l.Key) && Opened(l)) can.Add(l);
        if (can.Count == 0) return null;
        var lore = can[(int)(DawnDraw(night, 23) * can.Count) % can.Count];
        return new Gift { Kind = GiftKind.Trophy, Key = lore.Key, Title = "Трофей вожака", About = $"Со стяга вожака снято знание «{lore.Name}»: {lore.About}" };
    }

    /// <summary>Takes one of the dawn's gifts; the rest go. False if there is no such gift.</summary>
    public bool TakeGift(int i)
    {
        if (i < 0 || i >= Gifts.Count) return false;
        var g = Gifts[i];
        Gifts.Clear();
        switch (g.Kind)
        {
            case GiftKind.Newcomers:
                var kinds = g.Key switch
                {
                    "workers" => new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker },
                    "archers" => new[] { Kinds.Archer, Kinds.Archer },
                    "swordsman" => new[] { Kinds.Swordsman },
                    "priest" => new[] { Kinds.Priest },
                    "knight" => new[] { Kinds.Knight },
                    _ => Array.Empty<UnitType>(),
                };
                foreach (var t in kinds) Spawn(t, Gate);
                break;
            case GiftKind.Caravan:
                switch (g.Key)
                {
                    case "stone": Stock[Stockpile.Stone] += 8 * VoxelWorld.PerM3; break;
                    case "wood": Stock[Stockpile.Wood] += 8 * VoxelWorld.PerM3; break;
                    case "earth": Stock[Stockpile.Earth] += 12 * VoxelWorld.PerM3; break;
                    case "gold": Stock.Gold += 50 + 10 * (LastNight?.Number ?? 1); break;
                }
                break;
            case GiftKind.Omen:
                if (g.Key == "favour") GainFavour(15);
                else Revelations++;
                break;
            case GiftKind.Trophy:
                if (LoreOf(g.Key) is { } l && !Knows(l.Key)) Grant(l);
                break;
            case GiftKind.Relic:
                if (!HasRelic(g.Key)) Relics.Add(g.Key);
                break;
        }
        Say($"Дар рассвета: {g.Title.ToLowerInvariant()}.", 1);
        GiftTaken?.Invoke(g);
        return true;
    }

    // ---------------------------------------------------------------- saved

    void WriteDawn(BinaryWriter w)
    {
        w.Write(_night0Kills); w.Write(_nightLost); w.Write(_nightLeaders); w.Write(_nightLeadersKilled); w.Write(_nightKeep0);
        w.Write(_nightFelled.Count);
        foreach (var (id, n) in _nightFelled) { w.Write(id); w.Write(n); }
        w.Write(LastNight != null);
        if (LastNight is { } r)
        {
            w.Write(r.Number); w.Write(r.Killed); w.Write(r.Lost); w.Write(r.Leaders); w.Write(r.LeadersKilled); w.Write(r.KeepLost);
            w.Write(r.Unharmed); w.Write(r.NoneLost); w.Write(r.Headless);
            w.Write(r.Best.Count);
            foreach (var (who, n) in r.Best) { w.Write(who); w.Write(n); }
        }
        w.Write(Gifts.Count);
        foreach (var g in Gifts) { w.Write((byte)g.Kind); w.Write(g.Key); w.Write(g.Title); w.Write(g.About); }
    }

    void ReadDawn(BinaryReader rd)
    {
        _night0Kills = rd.ReadInt32(); _nightLost = rd.ReadInt32(); _nightLeaders = rd.ReadInt32(); _nightLeadersKilled = rd.ReadInt32(); _nightKeep0 = rd.ReadSingle();
        _nightFelled.Clear();
        for (int i = rd.ReadInt32(); i > 0; i--) { int id = rd.ReadInt32(); _nightFelled[id] = rd.ReadInt32(); }
        LastNight = null;
        if (rd.ReadBoolean())
        {
            var r = new NightReport
            {
                Number = rd.ReadInt32(), Killed = rd.ReadInt32(), Lost = rd.ReadInt32(), Leaders = rd.ReadInt32(), LeadersKilled = rd.ReadInt32(), KeepLost = rd.ReadSingle(),
                Unharmed = rd.ReadBoolean(), NoneLost = rd.ReadBoolean(), Headless = rd.ReadBoolean(),
            };
            for (int i = rd.ReadInt32(); i > 0; i--) { string who = rd.ReadString(); r.Best.Add((who, rd.ReadInt32())); }
            LastNight = r;
        }
        Gifts.Clear();
        for (int i = rd.ReadInt32(); i > 0; i--) Gifts.Add(new Gift { Kind = (GiftKind)rd.ReadByte(), Key = rd.ReadString(), Title = rd.ReadString(), About = rd.ReadString() });
    }
}
