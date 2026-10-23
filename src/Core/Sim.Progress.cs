using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>How hard the siege is, chosen when it begins: fewer and weaker raiders, as it was meant, or more and harder ones.</summary>
public enum Hardness : byte { Peaceful, Normal, Cruel }

/// <summary>
/// An errand: one thing to do next, with its reward. There is always one main errand, and two small ones beside it;
/// the first day's main errands are a road through the siege's beginnings (begin mending the fort, put an archer on the
/// wall, set a quarry aside, hang the gate, live through the first night), and after it the main errand is the next
/// night. Each is looked at again every second or so, and paid out when it is done.
/// </summary>
public sealed class Errand
{
    public string Key, Text;
    public bool Main;
    public double Gold;
    public int Knowledge;
    /// <summary>Where it stood when it was given, for one that asks for more than there was (stone in store, the fort's soundness).</summary>
    public double From;
}

public sealed partial class Sim
{
    // ---------------------------------------------------------------- how hard

    public Hardness Hardness = Hardness.Normal;
    /// <summary>How many raiders come, against the usual; how hard they hit, the citadel too; how much they can take.</summary>
    public float HordeMany => Hardness switch { Hardness.Peaceful => .6f, Hardness.Cruel => 1.35f, _ => 1 };
    public float HordeHarm => Hardness switch { Hardness.Peaceful => .7f, Hardness.Cruel => 1.2f, _ => 1 };
    public float HordeHardy => Hardness switch { Hardness.Peaceful => .85f, Hardness.Cruel => 1.2f, _ => 1 };

    public static readonly string[] HardnessNames = { "Мирная", "Обычная", "Жестокая" };

    /// <summary>The siege made as hard as it was chosen; the first wave planned again for it.</summary>
    public void SetHardness(Hardness h)
    {
        Hardness = h;
        if (Waves.Active) return;
        Bands.RemoveAll(b => b.State == BandState.Coming);
        PlanWave(Waves.Number + 1);
    }

    // ---------------------------------------------------------------- errands

    /// <summary>What to do now: the main errand first, then the small ones. Empty in the tutorial, which has its own road.</summary>
    public readonly List<Errand> Errands = new();
    public int ErrandsDone;
    /// <summary>An errand done and paid for (for the bell, and the words over the panel).</summary>
    public event Action<Errand> ErrandDone;
    /// <summary>No errands at all (the tutorial, staged scenes and the bench, unless they ask for them).</summary>
    public bool NoErrands = true;

    // the first day's road, in order; each given when the one before it is done
    static readonly string[] FirstDay = { "mend", "post", "quarry", "gate", "night" };
    int _road;
    double _nextErrandLook;
    // how many times mending was marked (the first errand asks only that it be begun)
    internal int Mended;

    /// <summary>The errands begun: the first day's road and two small ones beside it.</summary>
    public void BeginErrands()
    {
        NoErrands = false;
        Errands.Clear();
        _road = 0;
        GiveRoad();
        GiveSmall();
    }

    void GiveRoad()
    {
        // what is done already is passed over (a fort whose gate hangs, a night already lived through)
        while (_road < FirstDay.Length)
        {
            var e = Make(FirstDay[_road++], main: true);
            if (e == null || Met(e)) continue;
            Errands.Insert(0, e);
            return;
        }
        var night = Make("night", main: true);
        if (night != null) Errands.Insert(0, night);
    }

    void GiveSmall()
    {
        // a champion coming makes room for himself among the small ones
        if (!Errands.Exists(e => e.Key == "champion") && Make("champion", main: false) is { } ch && !Met(ch))
        {
            if (Errands.Count >= 3 && Errands.FindLastIndex(e => !e.Main) is var last and >= 0) Errands.RemoveAt(last);
            Errands.Add(ch);
        }
        // what the coming night asks first, then the day's work
        foreach (string key in new[] { "leader", "weak", "thief", "copper", "stone", "silver", "wood", "hunt", "alive", "sanctum", "gold" })
        {
            if (Errands.Count >= 3) return;
            if (Errands.Exists(e => e.Key == key)) continue;
            var e = Make(key, main: false);
            if (e != null && !Met(e)) Errands.Add(e);
        }
    }

    /// <summary>An errand of a kind, as it is worded and paid; null if there is nothing to it in this valley.</summary>
    Errand Make(string key, bool main)
    {
        Mine MineOf(int rank)
        {
            var mines = Mines.FindAll(m => !m.Power);
            mines.Sort((a, b) => a.Income.CompareTo(b.Income));
            return rank < mines.Count ? mines[rank] : null;
        }
        switch (key)
        {
            case "mend":
                if (FortArea == default) return null;
                return new Errand { Key = key, Main = main, Text = "Начните чинить крепость: {ToolRepair} и щелчок по разрушенной стене", Knowledge = 2 };
            case "post":
                return new Errand { Key = key, Main = main, Text = "Поставьте лучника на стену: выберите его и щёлкните {Rmb} по верху стены", Gold = 20 };
            case "quarry":
                return new Errand { Key = key, Main = main, Text = "Устройте каменоломню: {ToolGather}, {MatStone} и рамка по скалам или валунам", Knowledge = 2 };
            case "gate":
                if (FortGate == null) return null;
                return new Errand { Key = key, Main = main, Text = "Навесьте ворота крепости: {ToolRepair} и щелчок по проёму ворот", Gold = 25 };
            case "night":
            {
                int n = Waves.Number + (Waves.Active ? 0 : 1);
                if (n > Waves.Total) return null;
                return new Errand { Key = key, Main = main, From = n, Text = n == 1 ? "Выстойте первую ночь" : $"Выстойте ночь {n} из {Waves.Total}" };
            }
            case "copper":
            {
                var m = MineOf(0);
                if (m == null || m.Holder == Holder.Ours) return null;
                return new Errand { Key = key, Main = main, Text = $"Займите {m.Name.ToLowerInvariant()}: пошлите туда людей ({{Rmb}}). Даёт +{m.Income} золота в минуту", Gold = 20 };
            }
            case "silver":
            {
                var m = MineOf(1);
                if (m == null || m.Holder == Holder.Ours) return null;
                return new Errand { Key = key, Main = main, Text = m.Holder == Holder.Horde ? $"Отбейте у орды {m.Name.ToLowerInvariant()}: перебейте лагерь у входа" : $"Займите {m.Name.ToLowerInvariant()}", Gold = 40, Knowledge = 2 };
            }
            case "gold":
            {
                var m = MineOf(2);
                if (m == null || m.Holder == Holder.Ours) return null;
                return new Errand { Key = key, Main = main, Text = $"Займите {m.Name.ToLowerInvariant()} далеко на западе", Gold = 30, Knowledge = 3 };
            }
            case "stone":
                return new Errand { Key = key, Main = main, From = Stock[Stockpile.Stone], Text = "Добудьте 6 м³ камня", Knowledge = 2 };
            case "wood":
                return new Errand { Key = key, Main = main, From = Stock[Stockpile.Wood], Text = "Добудьте 6 м³ брёвен: {ToolGather}, {MatWood} и рамка по лесу", Gold = 15 };
            case "leader":
            {
                // the leader of a band coming tonight (not a champion's: he has his own errand)
                if (Waves.Active) return null;
                var b = Bands.Find(x => x.State == BandState.Coming && x.Champion == 0 && x.Name.StartsWith("Ватага "));
                if (b == null) return null;
                return new Errand { Key = key, Main = main, From = b.Id, Text = $"Убейте этой ночью вожака ватаги {b.Name[7..]}, у него красный стяг", Gold = 25, Knowledge = 1 };
            }
            case "weak":
            {
                // the side the chief means to take: make it stronger before he comes
                if (Waves.Active || Waves.Number < 1) return null;
                int side = -1, most = 0;
                for (int i = 0; i < Sides; i++) if (SideBands[i] > most) { most = SideBands[i]; side = i; }
                if (side < 0) return null;
                return new Errand { Key = key, Main = main, From = side, Text = $"Укрепите {SideNames[side]} сторону: вождь считает её слабой", Knowledge = 2 };
            }
            case "thief":
            {
                int n = Waves.Number + (Waves.Active ? 0 : 1);
                if (n < 2 || Waves.Active || !Bands.Exists(x => x.State == BandState.Coming && x.Roster.Contains(Kinds.Thief))) return null;
                return new Errand { Key = key, Main = main, From = n, Text = "Не дайте грабителю уйти с добычей этой ночью", Gold = 20 };
            }
            case "hunt":
                if (Waves.Active || WildAlive() == 0) return null;
                return new Errand { Key = key, Main = main, From = Hunted, Text = "Добудьте зверя: выберите лучника и щёлкните {Rmb} по оленю или кабану", Gold = 15 };
            case "alive":
                if (Waves.Number < 2 || !Units.Exists(u => !u.Dead && u.Team == Team.Player && Fighter(u) && !u.T.Ranged)) return null;
                return new Errand { Key = key, Main = main, From = Prisoners(), Text = "Возьмите пленного: выберите бойца и включите «Живьём» в нижней панели", Knowledge = 2 };
            case "champion":
            {
                // a champion coming tonight, or come back: kill him
                var b = Bands.Find(x => x.Champion > 0 && !ChampionsKilled.Contains(x.Champion));
                byte c = b?.Champion ?? 0;
                if (c == 0) foreach (var u in Units) if (!u.Dead && u.Champion > 0 && !ChampionsKilled.Contains(u.Champion)) c = u.Champion;
                if (c == 0) foreach (var v in Escaped) if (v.Champion > 0 && !ChampionsKilled.Contains(v.Champion)) c = v.Champion;
                if (c == 0) return null;
                var (_, of, _, _) = Champion(c);
                return new Errand { Key = key, Main = main, From = c, Text = c == 4 ? $"Убейте Вождя орды {of}: тогда вся орда побежит" : $"Убейте {of}, вожака главы {Roman(c)}: это откроет новый ярус знаний", Gold = 40, Knowledge = 3 };
            }
            case "sanctum":
                if (Waves.Number < 1 || !Mines.Exists(m => m.Power)) return null;
                return new Errand { Key = key, Main = main, Text = "Поставьте святилище на месте силы: {ToolGather} → «святилище»", Knowledge = 2 };
        }
        return null;
    }

    /// <summary>Whether an errand is done.</summary>
    bool Met(Errand e)
    {
        switch (e.Key)
        {
            case "mend": return Mended > 0;
            case "post":
                foreach (var u in Units)
                {
                    if (u.Dead || u.Inside || u.Team != Team.Player || !u.T.Ranged || u.Airborne != null) continue;
                    int x = (int)u.X, z = (int)u.Z;
                    if (!World.InBounds(x, 0, z)) continue;
                    // on something of ours two metres over the ground as the valley was made
                    if (u.Y - Site.HTop[z * World.SX + x] >= 2 * VoxelWorld.PerMetre) return true;
                }
                return false;
            case "quarry": return Zones.Exists(z => z.Kind == Stockpile.Stone);
            case "gate": return FortGate is { Ready: true, Broken: false };
            case "night": return Waves.Number >= (int)e.From && !Waves.Active || Won;
            case "copper": case "silver": case "gold":
            {
                var mines = Mines.FindAll(m => !m.Power);
                mines.Sort((a, b) => a.Income.CompareTo(b.Income));
                int rank = e.Key == "copper" ? 0 : e.Key == "silver" ? 1 : 2;
                return rank >= mines.Count || mines[rank].Holder == Holder.Ours;
            }
            case "stone": return Stock[Stockpile.Stone] - e.From >= 6 * VoxelWorld.PerM3;
            case "wood": return Stock[Stockpile.Wood] - e.From >= 6 * VoxelWorld.PerM3;
            case "leader": return _slainBands.Contains((int)e.From) || Bands.Find(b => b.Id == (int)e.From) is { LostLeader: true };
            case "weak": return SideBands[(int)e.From] == 0 || SideSeen[(int)e.From] < .5f;
            case "thief": return Waves.Number >= (int)e.From && !Waves.Active && !_thiefAway;
            case "hunt": return Hunted > e.From;
            case "alive": return Prisoners() > e.From;
            case "champion": return ChampionsKilled.Contains((int)e.From);
            case "sanctum":
                foreach (var z in Zones) if (z.Kind == Zone.Sanctum && PowerUnder(z) is { Holder: Holder.Ours }) return true;
                return false;
        }
        return false;
    }

    /// <summary>An errand that can no longer be done: the night it was for is over, the band gone, the thief away.</summary>
    bool Failed(Errand e) => e.Key switch
    {
        "leader" => !Bands.Exists(b => b.Id == (int)e.From) && !_slainBands.Contains((int)e.From),
        "weak" => Waves.Active,
        "thief" => _thiefAway && Waves.Number >= (int)e.From,
        _ => false,
    };

    // the bands whose leaders fell, and whether a thief got away with something, this night
    readonly HashSet<int> _slainBands = new();
    internal bool _thiefAway;

    /// <summary>Once a second: the errands done are paid out, and new ones given in their place.</summary>
    void TickErrands()
    {
        if (NoErrands || Time < _nextErrandLook) return;
        _nextErrandLook = Time + 1;
        // the horn: what is left of the first day's road gives way to the night
        if (Waves.Active && Errands.Count > 0 && Errands[0].Main && Errands[0].Key != "night")
        {
            Errands.RemoveAt(0);
            _road = FirstDay.Length;
            if (Make("night", main: true) is { } night) Errands.Insert(0, night);
        }
        for (int i = 0; i < Errands.Count; i++)
        {
            var e = Errands[i];
            if (!e.Main && Failed(e)) { Errands.RemoveAt(i--); continue; }
            if (!Met(e)) continue;
            Errands.RemoveAt(i--);
            ErrandsDone++;
            Stock.Gold += e.Gold;
            Knowledge += e.Knowledge;
            string pay = Pay(e);
            Say($"Поручение выполнено: {Plain(e.Text)}{(pay == "" ? "" : ". Награда: " + pay)}.", 1);
            ErrandDone?.Invoke(e);
            if (e.Main) GiveRoad();
        }
        GiveSmall();
    }

    /// <summary>What an errand pays, in words: "20 золота и 2 знания".</summary>
    public static string Pay(Errand e)
    {
        var parts = new List<string>();
        if (e.Gold > 0) parts.Add($"{e.Gold:0} золота");
        if (e.Knowledge > 0) parts.Add($"{e.Knowledge} {Plural(e.Knowledge, "знание", "знания", "знаний")}");
        return string.Join(" и ", parts);
    }

    /// <summary>The errand's words up to the colon: what is to be done, without how.</summary>
    static string Plain(string text)
    {
        int c = text.IndexOf(':');
        string s = c > 0 ? text[..c] : text;
        return char.ToLowerInvariant(s[0]) + s[1..];
    }

    // ---------------------------------------------------------------- the coming night, as the horde shows it

    /// <summary>The kinds that first come with wave n (none for the first: everything is new then).</summary>
    public static List<UnitType> NewAt(int n)
    {
        var now = new HashSet<UnitType>(Roster(n, 1));
        var before = new HashSet<UnitType>(n > 1 ? Roster(n - 1, 1) : new List<UnitType>());
        var fresh = new List<UnitType>();
        foreach (var t in now) if (!before.Contains(t)) fresh.Add(t);
        return fresh;
    }

    // ---------------------------------------------------------------- saved

    void WriteProgress(System.IO.BinaryWriter w)
    {
        w.Write((byte)Hardness); w.Write(NoErrands); w.Write(_road); w.Write(ErrandsDone); w.Write(Mended);
        w.Write(Errands.Count);
        foreach (var e in Errands) { w.Write(e.Key); w.Write(e.Main); w.Write(e.From); }
    }

    void ReadProgress(System.IO.BinaryReader r)
    {
        Hardness = (Hardness)r.ReadByte(); NoErrands = r.ReadBoolean(); _road = r.ReadInt32(); ErrandsDone = r.ReadInt32(); Mended = r.ReadInt32();
        Errands.Clear();
        for (int i = r.ReadInt32(); i > 0; i--)
        {
            string key = r.ReadString(); bool main = r.ReadBoolean(); double from = r.ReadDouble();
            var e = Make(key, main);
            if (e == null) continue;
            e.From = from;
            Errands.Add(e);
        }
    }
}
