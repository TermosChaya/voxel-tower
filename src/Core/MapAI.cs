using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>The horde's realm on the map: its warlords resting at the lair, and what it has mustered and sent out since the start.</summary>
public sealed class Realm
{
    /// <summary>The warlords at the lair, not leading a party now.</summary>
    public readonly List<Warlord> Lords = new();
    public int NextLord = 1;
    /// <summary>Raiders mustered and parties sent out since the start (the bench reads them).</summary>
    public int Mustered, Sent;
    /// <summary>The cells nobody holds were given their beasts, and the lair its garrison.</summary>
    public bool Ready;

    internal void Write(BinaryWriter w)
    {
        w.Write(Lords.Count);
        foreach (var l in Lords) Campaign.WriteLord(w, l);
        w.Write(NextLord); w.Write(Mustered); w.Write(Sent); w.Write(Ready);
    }

    internal void Read(BinaryReader r)
    {
        Lords.Clear();
        for (int n = r.ReadInt32(); n > 0; n--) if (Campaign.ReadLord(r) is { } l) Lords.Add(l);
        NextLord = r.ReadInt32(); Mustered = r.ReadInt32(); Sent = r.ReadInt32(); Ready = r.ReadBoolean();
    }
}

/// <summary>
/// The horde's turn on the map (docs/handoff/13-world-map.md, 2.7), once a dawn. The lair musters: so many raiders a day
/// and more for every cell the horde holds, of the kinds a raid of its tier brings — and the tier grows with the days
/// and the cells. A warlord comes to it every few days. Then it weighs where to send what it can spare over the
/// garrison it keeps: a cell nobody holds for what it is worth, a cell of ours for more (the first valley most), each
/// less the farther it is; it sends a party strong enough to win three times in four, and where the best it could do is
/// strike at ours and it nearly can, it waits a day to gather. With a party of ours at its door it keeps everyone at
/// home and calls back those near. Beaten parties go home (Campaign.Turn.cs).
/// </summary>
public sealed partial class Campaign
{
    public readonly Realm Realm = new();

    /// <summary>What the lair keeps at home, raiders, whatever it sends out.</summary>
    public const int LairKeep = 12;
    /// <summary>Raiders the lair musters a dawn, and more for every cell the horde holds (the lair among them).</summary>
    public const int MusterBase = 3, MusterPerCell = 1;
    /// <summary>The most the lair holds: it musters no more over that.</summary>
    public const int LairMost = 120;
    /// <summary>The fewest a party is sent out with.</summary>
    public const int LeastParty = 5;
    /// <summary>A warlord comes to the lair every so many days, three at most.</summary>
    public const int LordEvery = 5, MostLords = 3;
    /// <summary>The chance the horde wants of winning before it strikes.</summary>
    public const float WantOdds = .75f;
    /// <summary>What a day's way takes off a target's worth.</summary>
    const double DayCost = 1.5;

    /// <summary>The horde's lair, while it holds one (the first of them on the big map).</summary>
    public MapCell Lair => Cells.Find(c => c.Holding == Holding.HordeTown && c.Owner == Owner.Horde);
    /// <summary>The horde's lairs it still holds: one on the small map, two or three on the big one.</summary>
    public List<MapCell> Lairs => Cells.FindAll(c => c.Holding == Holding.HordeTown && c.Owner == Owner.Horde);
    /// <summary>The most warlords the realm has at once: three, and one more for every lair past the first.</summary>
    int LordsMost => MostLords + Math.Max(0, Lairs.Count - 1);

    /// <summary>
    /// How strong the lair's musters come, as the siege's nights count it: from 1, by a quarter a day and three tenths
    /// for every cell the horde holds past the lair, to 12 at most.
    /// </summary>
    public float Tier
    {
        get
        {
            int cells = 0;
            foreach (var c in Cells) if (c.Owner == Owner.Horde) cells++;
            return Math.Clamp(1 + (Clock.Day - 1) * .25f + .3f * Math.Max(0, cells - 1), 1, 12);
        }
    }

    /// <summary>A raider of a kind, whole.</summary>
    internal static Traveller Raider(UnitType t) => new() { Kind = t, Hp = t.Hp, MaxHp = t.Hp, Commands = false };

    /// <summary>
    /// The map made ready for its turn, once: each lair's garrison as the horde's town has it behind its palisade, the
    /// keepers of the treasures (Campaign.Troves.cs), and beasts keeping each other cell nobody holds past the first ring —
    /// two bears and two boars — to be beaten before it is anybody's.
    /// </summary>
    void Ready()
    {
        if (Realm.Ready) return;
        Realm.Ready = true;
        foreach (var lair in Lairs)
        {
            if (lair.Sum.Keep.Count > 0) continue;
            foreach (var t in Sim.Roster(LairThreat, 1)) lair.Sum.Keep.Add(Raider(t));
            // its palisade
            lair.Sum.Walls = .5f;
        }
        // (the big map's keepers were laid with it: its treasures', its mines', its passes')
        if (Big) { if (Realm.Lords.Count == 0) Realm.Lords.Add(NewLord(1)); return; }
        KeepTroves();
        foreach (var c in Cells)
        {
            if (c.Owner != Owner.None || Distance(0, 0, c.Q, c.R) < 2 || c.Sum.Keep.Count > 0 || c.Trove != TroveKind.None) continue;
            foreach (var t in new[] { Kinds.Bear, Kinds.Bear, Kinds.Boar, Kinds.Boar }) c.Sum.Keep.Add(Raider(t));
        }
        if (Realm.Lords.Count == 0) Realm.Lords.Add(NewLord(1));
    }

    /// <summary>The map made ready for its turn now, if it was not (a valley gone into before the map first ran: its keepers given).</summary>
    public void Prepare() => Ready();

    /// <summary>A new warlord of the horde's realm: an orc, a troll from the third level.</summary>
    Warlord NewLord(int level)
    {
        int id = Realm.NextLord++;
        var names = Sim.LordNames;
        var (name, of) = names[(int)(Roll() * names.Length)];
        level = Math.Clamp(level, 1, Warlord.MaxLevel);
        return new Warlord { Id = id, Name = name, Of = of, Level = level, Kind = level >= 3 ? Kinds.Troll : Kinds.Orc };
    }

    /// <summary>
    /// The horde's dawn: each lair musters (the cells the horde holds shared out among its lairs), a warlord may come to
    /// the realm, and each lair decides.
    /// </summary>
    void HordeDawn()
    {
        var lairs = Lairs;
        if (lairs.Count == 0) return;
        int cells = 0;
        foreach (var c in Cells) if (c.Owner == Owner.Horde) cells++;
        var kinds = Sim.Roster(Tier, 1);
        foreach (var lair in lairs)
        {
            int n = Math.Min(MusterBase + MusterPerCell * Math.Max(1, cells / lairs.Count), Math.Max(0, LairMost - lair.Sum.Keep.Count));
            for (int i = 0; i < n; i++) lair.Sum.Keep.Add(Raider(kinds[(int)(Roll() * kinds.Count)]));
            Realm.Mustered += n;
            if (n > 0) Tell(NewsKind.Grew, $"Логово орды набрало {Raiders(n)}, в нём {lair.Sum.Keep.Count}.", lair, null);
        }
        int lords = Realm.Lords.Count;
        foreach (var p in Parties) if (p.Lord != null) lords++;
        if (Clock.Day % LordEvery == 0 && lords < LordsMost) Realm.Lords.Add(NewLord(1 + Clock.Day / 10));
        foreach (var lair in lairs) Decide(lair);
    }

    /// <summary>
    /// The horde weighs where to send what the lair can spare, and sends one party a dawn at most: to the target worth
    /// most for its way, that it can beat three times in four; it waits a day where the best it could do is strike at
    /// ours and it nearly can. With ours at its door it calls back those near and sends nobody.
    /// </summary>
    void Decide(MapCell lair)
    {
        if (Big) { DecideWide(lair); return; }
        // ours at the door: all home
        bool threat = false;
        foreach (var p in Parties) if (p.Owner == Owner.Ours && Distance(p.Q, p.R, lair.Q, lair.R) <= 1) threat = true;
        int key = KeyOf(lair);
        if (threat)
        {
            foreach (var p in Parties.ToArray())
                if (p.Owner == Owner.Horde && p.Aim != PartyAim.Return && Distance(p.Q, p.R, lair.Q, lair.R) <= 2) { p.Den = key; GoHome(p); }
            return;
        }
        // (the cells the horde holds shared out among its lairs: so many parties out of each)
        int cells = 0, abroad = 0, lairs = Math.Max(1, Lairs.Count);
        foreach (var c in Cells) if (c.Owner == Owner.Horde) cells++;
        foreach (var p in Parties) if (p.Owner == Owner.Horde && p.Aim is PartyAim.Take or PartyAim.Strike && (p.Den == key || lairs == 1)) abroad++;
        if (abroad >= 1 + cells / lairs / 2) return;
        // what it can spare: all over the garrison it keeps, engines and all
        var pool = new List<Traveller>(lair.Sum.Keep);
        int spare = pool.Count - LairKeep;
        if (spare < LeastParty) return;
        var lord = Realm.Lords.Count > 0 ? Realm.Lords[0] : null;
        Target best = null, most = null;
        foreach (var c in Cells)
        {
            // (the treasures of the map it leaves be: it neither loots them nor fights their keepers for nothing)
            if (c == lair || c.Owner == Owner.Horde || Untaken(c)) continue;
            if (Parties.Exists(p => p.Owner == Owner.Horde && p.GoalQ == c.Q && p.GoalR == c.R && p.Aim is PartyAim.Take or PartyAim.Strike)) continue;
            var way = MapPath.Find(this, lair, c, Owner.Horde);
            if (way == null) continue;
            var t = new Target { Cell = c, Way = way };
            t.Defence = Defenders(c, out t.Walls);
            t.Score = Worth(c) - MapPath.WayHours(lair, way) / 24 * DayCost;
            t.Party = Muster(pool, spare, lord, t);
            if (most == null || t.Score > most.Score) most = t;
            if (t.Party != null && (best == null || t.Score > best.Score)) best = t;
        }
        if (most == null) return;
        // the best it could do is to strike at ours: if it nearly can, it gathers for it
        if (most != best && most.Cell.Owner == Owner.Ours && (best == null || most.Score > best.Score + 1) && Strength(pool, spare, lord) >= most.Need * .6f) return;
        if (best == null) return;
        Send(lair, best, lord);
    }

    sealed class Target
    {
        public MapCell Cell;
        public List<MapCell> Way;
        public List<Traveller> Defence, Party;
        public float Walls, Need;
        public double Score;
    }

    /// <summary>What a cell is worth to the horde: the first valley most, then the ore, the holy, the mountains; ours more than nobody's.</summary>
    static double Worth(MapCell c)
    {
        if (c.Home) return 8;
        double v = c.Kind switch { ValleyKind.Ore => 4, ValleyKind.Sacred => 3, ValleyKind.Mountain => 2.5, ValleyKind.Forest => 2, _ => 1.5 };
        return c.Owner == Owner.Ours ? v + 2 : v;
    }

    /// <summary>
    /// Who the horde expects to fight for a cell, and the walls: the keepers and the parties of ours standing in it, as
    /// last known; the first valley never left guessed at a household with a few soldiers behind half a wall.
    /// </summary>
    List<Traveller> Defenders(MapCell c, out float walls)
    {
        var list = new List<Traveller>();
        walls = c.Owner == Owner.Ours ? c.Sum.Walls : 0;
        if (c.Owner == Owner.Ours && !c.Sum.Known && c.Sum.Keep.Count == 0 && c.Home)
        {
            foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Archer, Kinds.Archer, Kinds.Swordsman, Kinds.Swordsman })
                list.Add(Raider(t));
            walls = .5f;
        }
        else list.AddRange(c.Sum.Keep);
        foreach (var p in Parties) if (p.Owner == Owner.Ours && p.Q == c.Q && p.R == c.R) list.AddRange(p.Party.Members);
        return list;
    }

    /// <summary>The strength of the strongest party the lair could send of what it spares.</summary>
    float Strength(List<Traveller> pool, int spare, Warlord lord)
    {
        var all = Strongest(pool, spare);
        return QuickBattle.Strength(all, lord);
    }

    /// <summary>The strongest the lair can spare, so many of them.</summary>
    static List<Traveller> Strongest(List<Traveller> pool, int n)
    {
        var sorted = new List<Traveller>(pool);
        sorted.Sort((a, b) => Worth1(b).CompareTo(Worth1(a)));
        return sorted.GetRange(0, Math.Min(n, sorted.Count));
    }

    static double Worth1(Traveller m)
    {
        var (hp, dps) = QuickBattle.Worth(m.Kind, m.Hp);
        return Math.Sqrt(hp * dps);
    }

    /// <summary>
    /// A party for a target drawn of what the lair spares: raiders taken in the lair's order (its luck) until the party
    /// would win three times in four, or null if all it spares would not; engines go only where there are walls.
    /// </summary>
    List<Traveller> Muster(List<Traveller> pool, int spare, Warlord lord, Target t)
    {
        var ground = t.Cell.Kind;
        var party = new List<Traveller>();
        // the stronger first, so that a party is no bigger than it needs be
        var order = Strongest(pool, pool.Count);
        foreach (var m in order)
        {
            if (party.Count >= spare) break;
            if (Kinds.IsEngine(m.Kind) && t.Walls <= 0) continue;
            party.Add(m);
            if (party.Count >= LeastParty && QuickBattle.Chance(party, lord, t.Defence, null, t.Walls, ground) >= WantOdds) break;
        }
        t.Need = NeedOf(t);
        if (party.Count < LeastParty || QuickBattle.Chance(party, lord, t.Defence, null, t.Walls, ground) < WantOdds) return null;
        return party;
    }

    /// <summary>The strength a party needs to win three times in four against a target.</summary>
    static float NeedOf(Target t)
    {
        float d = QuickBattle.Strength(t.Defence, null, t.Walls);
        if (t.Cell.Kind == ValleyKind.Mountain) d *= 1.15f;
        if (t.Cell.Kind == ValleyKind.Marsh) d /= .9f;
        return d * (float)Math.Pow(WantOdds / (1 - WantOdds), 1 / QuickBattle.Sharpness);
    }

    /// <summary>A party of the horde sent out of the lair to a target: said if it goes for ours.</summary>
    void Send(MapCell lair, Target t, Warlord lord)
    {
        var p = new MapParty
        {
            Id = _nextParty++, Owner = Owner.Horde, Party = new Party(), Q = lair.Q, R = lair.R, CameQ = lair.Q, CameR = lair.R,
            Aim = t.Cell.Owner == Owner.Ours ? PartyAim.Strike : PartyAim.Take, Den = KeyOf(lair),
        };
        foreach (var m in t.Party) { lair.Sum.Keep.Remove(m); p.Party.Members.Add(m); }
        // a warlord leads a strike, or a party of eight and more
        if (lord != null && (p.Aim == PartyAim.Strike || p.Party.Members.Count >= 8)) { p.Lord = lord; Realm.Lords.Remove(lord); lord.Fate = LordFate.Coming; }
        p.SetOut = QuickBattle.Strength(p.Party.Members, p.Lord);
        Parties.Add(p);
        Realm.Sent++;
        Send(p, t.Cell);
        if (p.Aim == PartyAim.Strike && Big)
        {
            int days = p.Steps.Count > 0 ? p.Steps[^1].Day : 0;
            Tell(NewsKind.RaidComing, $"Войско орды идёт на {Names(t.Cell).acc}: {Raiders(p.Party.Members.Count)}{Led(p)}. Будет через {(days == 0 ? "день" : $"{days + 1} дн.")}", t.Cell, p);
        }
        else if (p.Aim == PartyAim.Strike)
        {
            double minutes = MapPath.WayHours(lair, p.Path) * MapPath.Pace(p) * MapPath.HourSeconds / 60;
            Tell(NewsKind.RaidComing, $"Войско орды идёт на {Names(t.Cell).acc}: {Raiders(p.Party.Members.Count)}{Led(p)}. Будет через {Math.Max(1, (int)Math.Round(minutes))} мин.", t.Cell, p);
        }
    }
}
