using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>What the map tells: a party come where it went, a battle fought, a cell taken or lost, a raid on its way, the lair grown.</summary>
public enum NewsKind : byte { Arrived, Fought, Taken, Lost, RaidComing, Grew }

/// <summary>A piece of news of the map, for the board and the chronicle: what kind, the line said, the cell and the party it is of.</summary>
public sealed class MapNews
{
    public NewsKind Kind;
    public string Text;
    public MapCell Cell;
    public MapParty Party;
}

/// <summary>What a party of the horde is out to do: take a cell nobody holds, strike at ours, go home to the lair, keep where it is.</summary>
public enum PartyAim : byte { None, Take, Strike, Return, Hold }

public sealed partial class MapParty
{
    public int Id;
    /// <summary>The horde's warlord at its head, if it has one (he is not among its members: <see cref="Sim.Raid"/> takes him apart).</summary>
    public Warlord Lord;
    public PartyAim Aim;
    /// <summary>The cell it is going to.</summary>
    public int GoalQ, GoalR;
    /// <summary>The cell it came into this one from (the side a raid comes in by), the same as its own when it has not walked.</summary>
    public int CameQ, CameR;
    /// <summary>How strong it was when it set out: the horde calls it home beaten when it has lost too much of that.</summary>
    public float SetOut;
    /// <summary>A party of ours stopped where the enemy is, for the player to go in and fight or have it counted.</summary>
    public bool Waits;
    /// <summary>The lair a party of the horde came out of and goes home to (its cell's key), −1 for none.</summary>
    public int Den = -1;

    public bool Moving => Path is { Count: > 0 };

    /// <summary>Its members' kinds, as a raid takes them.</summary>
    public List<UnitType> Roster()
    {
        var list = new List<UnitType>();
        foreach (var m in Party.Members) list.Add(m.Kind);
        return list;
    }
}

/// <summary>
/// The map's turn (docs/handoff/13-world-map.md, 2.3–2.9): the one clock runs, and with it every party walks its way
/// at the pace of the ground; at every dawn the valleys we are not in get what their workers dug and cut and the hurt
/// mend, and the horde musters and decides (MapAI.cs); where the horde's party walks into a cell of ours we are not in,
/// it fights its keepers by a quick battle, and where it walks into the valley we play in, it is a raid for the game to
/// bring in (<see cref="TakeRaid"/>). The towns and the mines of the valleys we hold pay into the realm's gold all the
/// while, the open valley by itself.
/// </summary>
public sealed partial class Campaign
{
    /// <summary>What the map has told since the game last took it (it keeps the last 200).</summary>
    public readonly List<MapNews> News = new();
    /// <summary>The valley open now, its own stores the realm's gold while it is (set as it is entered).</summary>
    public Sim Live;
    /// <summary>We are in the valley of <see cref="Here"/>: it lives by itself, and what of the horde walks into it is a raid.</summary>
    public bool InValley;
    /// <summary>Every lair of the horde taken: the campaign is won.</summary>
    public bool Won;
    /// <summary>The gold the valleys we were not in paid since the start (the bench reads it).</summary>
    public double EarnedAway;

    /// <summary>How many worker-days an outpost takes to raise where we are not.</summary>
    public const float OutpostWork = 3;
    /// <summary>The share of his health a hurt man of ours gets back a dawn in a valley of ours, and a raider at the lair.</summary>
    const float Mend = .3f, LairMend = .5f;
    /// <summary>The longest step of the map's time the turn takes at once, seconds: a cell takes 150 at least to cross.</summary>
    const double Chunk = 5;

    int _dawnDay = 1, _nextParty = 1;
    uint _rng;
    bool _rngSet, _defeatSaid;
    readonly List<MapParty> _raidsDue = new(), _arrivals = new();

    /// <summary>The map's own luck, from its seed and kept with it: the same seed plays the same.</summary>
    internal double Roll()
    {
        if (!_rngSet) { _rng = Seed * 2246822519u + 0x5bd1e995u; _rngSet = true; }
        unchecked
        {
            _rng += 0x6D2B79F5;
            uint t = _rng;
            t = (t ^ (t >> 15)) * (t | 1);
            t = (t + (t ^ (t >> 7)) * (t | 61)) ^ t;
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    /// <summary>
    /// The map's clock runs so many seconds: the gold of the valleys we hold comes in, the parties walk, each dawn come
    /// brings the day's work and the horde's turn. The clock may also be set from outside (from the valley we play in)
    /// and this called with nothing: a dawn passed is then still kept.
    /// </summary>
    public void Advance(double seconds)
    {
        double seconds0 = seconds;
        Ready();
        while (seconds > 1e-9)
        {
            double dt = Math.Min(Chunk, seconds);
            seconds -= dt;
            double at = Clock.At + dt;
            int day = Clock.Day;
            while (at >= DayClock.Cycle) { at -= DayClock.Cycle; day++; }
            Clock = new ClockTime(day, at);
            Earn(dt);
            Walk(dt);
            Raise(dt);
            Dawns();
        }
        Dawns();
        Judge();
        if (Live != null && InValley && Here != null) Reckon(Live, Here);
        LookAtHall(Math.Max(seconds0, .02));
    }

    void Dawns()
    {
        while (_dawnDay < Clock.Day) { _dawnDay++; Dawn(); }
    }

    /// <summary>
    /// An enemy party has walked into the valley we play in: the game brings it in as a raid by the side it came across
    /// (<see cref="Sim.Raid"/> with <see cref="MapParty.Roster"/> and its warlord). It is off the map now; what is left of
    /// it after the raid comes back by <see cref="RaidOver"/>.
    /// </summary>
    public bool TakeRaid(out MapParty enemy, out int side)
    {
        enemy = null; side = -1;
        if (_raidsDue.Count == 0) return false;
        enemy = _raidsDue[0];
        _raidsDue.RemoveAt(0);
        side = SideFrom(enemy);
        return true;
    }

    /// <summary>A party of ours has walked into the valley we play in: the game brings it in (<see cref="Sim.Arrive"/>) by that side.</summary>
    public bool TakeArrival(out MapParty ours, out int side)
    {
        ours = null; side = -1;
        if (_arrivals.Count == 0) return false;
        ours = _arrivals[0];
        _arrivals.RemoveAt(0);
        side = SideFrom(ours);
        return true;
    }

    /// <summary>The side of the cell we are in that a party came across.</summary>
    int SideFrom(MapParty p)
    {
        var here = At(p.Q, p.R);
        var came = At(p.CameQ, p.CameR);
        if (here == null) return Hex.West;
        int side = came == null || came == here ? -1 : MapPath.Side(here, came);
        return side >= 0 ? side : came == null || came == here ? here.EntrySide : SideToward(here, came.Q, came.R);
    }

    /// <summary>
    /// A raid brought in by <see cref="TakeRaid"/> is over: those of it who got away alive, and its warlord if he did,
    /// go back the way they came, home to the lair.
    /// </summary>
    public void RaidOver(MapParty enemy, IEnumerable<UnitType> survivors, bool lordAway)
    {
        var p = new MapParty { Id = _nextParty++, Owner = Owner.Horde, Party = new Party(), Q = enemy.CameQ, R = enemy.CameR, CameQ = enemy.Q, CameR = enemy.R };
        foreach (var t in survivors) p.Party.Members.Add(Raider(t));
        if (lordAway && enemy.Lord != null) { p.Lord = enemy.Lord; p.Lord.Fate = LordFate.Fled; }
        if (p.Party.Members.Count == 0 && p.Lord == null) return;
        Parties.Add(p);
        GoHome(p);
    }

    // ------------------------------------------------------------ the ways

    /// <summary>Sends a party on its way to a cell (ours: the player's order); false if there is no way there.</summary>
    public bool Send(MapParty p, MapCell to)
    {
        var from = At(p.Q, p.R);
        var way = MapPath.Find(this, from, to, p.Owner);
        if (way == null) return false;
        p.Path = way; p.Progress = 0; p.Waits = false;
        p.GoalQ = to.Q; p.GoalR = to.R;
        return true;
    }

    void Walk(double dt)
    {
        foreach (var p in Parties.ToArray())
        {
            if (!Parties.Contains(p)) continue;
            // a party walking the land of the map (AtlasWalk.cs) goes by its feet, the cells it crosses its steps
            if (p.Trail != null) { if (Land != null) WalkTrail(p, dt); continue; }
            if (!p.Moving) continue;
            var from = At(p.Q, p.R); var to = p.Path[0];
            p.Progress += dt;
            double need = MapPath.StepSeconds(p, from, to);
            if (p.Progress < need) continue;
            p.Progress -= need;
            p.CameQ = p.Q; p.CameR = p.R;
            p.Q = to.Q; p.R = to.R;
            p.Path.RemoveAt(0);
            if (!p.Moving) p.Progress = 0;
            // (on the land, at the middle of the cell it stepped into)
            if (Land != null) (p.AtX, p.AtZ) = Land.Center(to);
            if (p.Owner == Owner.Horde) HordeReach(p, to); else OursReach(p, to);
        }
    }

    /// <summary>
    /// A party of ours comes into a cell: into the valley we play in, it walks in if it was sent into it (else it stands
    /// on the map by it, to be led in with «Войти в клетку»); where the enemy stands, it stops for the player to choose;
    /// at the end of its way, it is said.
    /// </summary>
    void OursReach(MapParty p, MapCell cell)
    {
        if (InValley && cell == Here && p.WalkIn)
        {
            Parties.Remove(p);
            _arrivals.Add(p);
            Tell(NewsKind.Arrived, $"Отряд пришёл {Names(cell).prep}: {Men(p.Party.Members.Count)}.", cell, p);
            return;
        }
        int foes = Foes(cell);
        if (foes > 0)
        {
            p.Path.Clear(); p.Progress = 0; p.Waits = true;
            Tell(NewsKind.Arrived, $"Отряд дошёл до {Names(cell).gen}. Там врагов: {foes}. Войдите в долину или посчитайте бой сразу.", cell, p);
            return;
        }
        if (!p.Moving) Tell(NewsKind.Arrived, $"Отряд пришёл {Names(cell).prep}.", cell, p);
    }

    /// <summary>The enemies in a cell for a party of ours: the horde's parties standing there and the cell's keepers if they are not ours.</summary>
    public int Foes(MapCell cell)
    {
        int n = cell.Owner != Owner.Ours ? cell.Sum.Holders().Count : 0;
        foreach (var h in Parties) if (h.Owner == Owner.Horde && h.Q == cell.Q && h.R == cell.R) n += h.Party.Members.Count + (h.Lord != null ? 1 : 0);
        return n;
    }

    /// <summary>
    /// A party of the horde comes into a cell: into the valley we play in, it is a raid; on a party of ours standing
    /// there, it falls on it; into a cell of ours, it falls on the keepers; on the beasts of a cell nobody holds, on
    /// them; and where it was going, it does what it came for.
    /// </summary>
    void HordeReach(MapParty p, MapCell cell)
    {
        if (InValley && cell == Here)
        {
            Parties.Remove(p);
            _raidsDue.Add(p);
            Tell(NewsKind.RaidComing, $"Войско орды входит {Names(cell).prep}: {Raiders(p.Party.Members.Count)}{Led(p)}.", cell, p);
            return;
        }
        foreach (var ours in Parties.ToArray())
        {
            if (ours.Owner != Owner.Ours || ours.Q != cell.Q || ours.R != cell.R || ours.Moving) continue;
            if (!Battle(p, ours.Party.Members, null, cell.Owner == Owner.Ours ? cell.Sum.Walls : 0, cell, ours)) return;
        }
        if (cell.Owner == Owner.Ours) { Assault(p, cell); return; }
        // (the keepers of a treasure keep to it: the horde goes by them, and they let it)
        if (cell.Owner == Owner.None && cell.Sum.Keep.Count > 0 && !Untaken(cell))
        {
            if (!Battle(p, cell.Sum.Keep, null, 0, cell, null)) return;
        }
        if (p.Moving) return;
        if (cell.Owner == Owner.None && p.Aim is PartyAim.Take or PartyAim.Strike) { Occupy(p, cell); return; }
        if (cell.Holding == Holding.HordeTown && cell.Owner == Owner.Horde) { BackHome(p, cell); return; }
        GoHome(p);
    }

    /// <summary>
    /// The horde's party falls on those standing in a cell (a party of ours, or the beasts that keep it): a quick battle.
    /// True if the party won and goes on; beaten, what is left of it goes home. A party of ours beaten runs for the
    /// nearest cell of ours.
    /// </summary>
    bool Battle(MapParty p, List<Traveller> them, Warlord theirs, float walls, MapCell cell, MapParty ours)
    {
        var r = QuickBattle.Fight(p.Party.Members, p.Lord, them, theirs, walls, cell.Kind, Roll);
        if (r.AttackerLordFell) p.Lord = null;
        if (ours != null) HeroNews(r, cell, ours);
        if (ours != null && them.Count == 0) Parties.Remove(ours);
        string they = ours != null ? "наших" : "зверей";
        if (r.AttackerWon)
        {
            if (ours != null && them.Count > 0) Flee(ours, cell);
            Tell(NewsKind.Fought, $"Бой {Names(cell).prep}: орда разбила {(ours != null ? "наш отряд" : "зверей")}. Пало {they} {r.DefenderFell.Count}, налётчиков {r.AttackerFell.Count}.", cell, p);
            return true;
        }
        Tell(NewsKind.Fought, $"Бой {Names(cell).prep}: {(ours != null ? "наш отряд отбил" : "звери отбили")} орду. Пало налётчиков {r.AttackerFell.Count}, {they} {r.DefenderFell.Count}.", cell, p);
        if (ours != null) ours.Waits = false;
        Beaten(p);
        return false;
    }

    /// <summary>
    /// The horde's party falls on a valley of ours it is not ours to watch: its keepers behind their walls. Won, the cell
    /// is the horde's — some stay to hold it, the rest go home — and ours who got away run for the nearest cell of ours.
    /// </summary>
    void Assault(MapParty p, MapCell cell)
    {
        var keep = cell.Sum.Keep;
        int ours = keep.Count;
        var r = QuickBattle.Fight(p.Party.Members, p.Lord, keep, null, cell.Sum.Walls, cell.Kind, Roll);
        if (r.AttackerLordFell) p.Lord = null;
        HeroNews(r, cell, null);
        if (!r.AttackerWon)
        {
            Tell(NewsKind.Fought, $"Орда напала на {Names(cell).acc} и отбита. Пало налётчиков {r.AttackerFell.Count}, наших {r.DefenderFell.Count}.", cell, p);
            Beaten(p);
            return;
        }
        // ours who got away run
        if (keep.Count > 0)
        {
            var run = new MapParty { Id = _nextParty++, Owner = Owner.Ours, Party = new Party(), Q = cell.Q, R = cell.R, CameQ = cell.Q, CameR = cell.R };
            run.Party.Members.AddRange(keep);
            keep.Clear();
            Parties.Add(run);
            Flee(run, cell);
        }
        Fall(cell);
        Tell(NewsKind.Lost, cell.Home
            ? $"Орда взяла первую долину. Пало наших {r.DefenderFell.Count}. {DaysToRetake} дня, чтобы занять другую долину заставой или вернуть эту."
            : $"Орда взяла {Names(cell).acc}. Пало наших {r.DefenderFell.Count}.", cell, p);
        Occupy(p, cell, said: false);
    }

    /// <summary>A cell of ours is the horde's now: its town and its walls left as they are, the outpost's work undone.</summary>
    internal void Fall(MapCell cell)
    {
        cell.Owner = Owner.Horde;
        cell.Sum.TownGold = 0;
        cell.Sum.Walls *= .5f;
        cell.Sum.Raised = 0;
        if (cell.Home && HomeFell < 0) HomeFell = Clock.Day;
    }

    /// <summary>The horde holds a cell: some of the party stay as its garrison, the rest go home.</summary>
    void Occupy(MapParty p, MapCell cell, bool said = true)
    {
        if (cell.Owner != Owner.Horde) cell.Owner = Owner.Horde;
        var men = p.Party.Members;
        int stay = Math.Clamp(men.Count / 3, Math.Min(3, men.Count), 8);
        // those fit to hold it stay: not the engines, nor those who only come raiding
        for (int i = men.Count - 1; i >= 0 && stay > 0; i--)
        {
            var t = men[i].Kind;
            if (Kinds.IsEngine(t) || t == Kinds.Miner || t == Kinds.Sapper || t == Kinds.Thief) continue;
            cell.Sum.Keep.Add(men[i]); men.RemoveAt(i); stay--;
        }
        if (said) Tell(NewsKind.Taken, $"Орда заняла {Names(cell).acc}.", cell, p);
        if (men.Count == 0 && p.Lord == null) { Parties.Remove(p); return; }
        GoHome(p);
    }

    /// <summary>A party of the horde home at the lair: its men into the lair's garrison, its warlord rests there.</summary>
    void BackHome(MapParty p, MapCell lair)
    {
        lair.Sum.Keep.AddRange(p.Party.Members);
        if (p.Lord != null) Realm.Lords.Add(p.Lord);
        Parties.Remove(p);
    }

    /// <summary>A party of the horde beaten: what is left of it goes home, if anything is.</summary>
    void Beaten(MapParty p)
    {
        if (p.Party.Members.Count == 0 && p.Lord == null) { Parties.Remove(p); return; }
        GoHome(p);
    }

    /// <summary>
    /// A party of the horde sets off home: to the lair it came out of while the horde holds it, else to the nearest; with
    /// no lair left, it keeps where it is.
    /// </summary>
    void GoHome(MapParty p)
    {
        var here = At(p.Q, p.R);
        var lair = CellOfKey(p.Den) is { Holding: Holding.HordeTown, Owner: Owner.Horde } own ? own : null;
        if (lair == null && here != null)
        {
            int best = int.MaxValue;
            foreach (var l in Lairs) if (Distance(l.Q, l.R, here.Q, here.R) < best) { best = Distance(l.Q, l.R, here.Q, here.R); lair = l; }
            if (lair != null) p.Den = KeyOf(lair);
        }
        if (lair == null || here == null) { p.Aim = PartyAim.Hold; p.Path = null; return; }
        if (here == lair) { BackHome(p, lair); return; }
        p.Aim = PartyAim.Return;
        if (!Send(p, lair)) { p.Aim = PartyAim.Hold; p.Path = null; }
    }

    /// <summary>A party of ours that got away runs for the nearest cell of ours; with none, it scatters.</summary>
    void Flee(MapParty p, MapCell from)
    {
        MapCell best = null; int near = int.MaxValue;
        foreach (var c in Cells)
            if (c.Owner == Owner.Ours && c != from && Distance(c.Q, c.R, from.Q, from.R) < near) { near = Distance(c.Q, c.R, from.Q, from.R); best = c; }
        if (best == null || !Send(p, best)) { Parties.Remove(p); return; }
        p.Waits = false;
    }

    /// <summary>
    /// The player has a party of ours that stands where the enemy is fight it out at once: the horde's parties standing
    /// there first, then the cell's keepers. Won, a cell of the horde's is nobody's (an outpost raised holds it), the
    /// first valley ours again, the lair taken the campaign won. Null if there was nobody to fight.
    /// </summary>
    public QuickResult FightHere(MapParty ours)
    {
        var cell = At(ours.Q, ours.R);
        QuickResult last = null;
        ours.Waits = false;
        foreach (var h in Parties.ToArray())
        {
            if (h.Owner != Owner.Horde || h.Q != cell.Q || h.R != cell.R) continue;
            last = QuickBattle.Fight(ours.Party.Members, null, h.Party.Members, h.Lord, 0, cell.Kind, Roll);
            if (last.DefenderLordFell) h.Lord = null;
            HeroNews(last, cell, ours);
            if (!last.AttackerWon)
            {
                Tell(NewsKind.Fought, $"Бой {Names(cell).prep}: отряд разбит. Пало наших {last.AttackerFell.Count}, налётчиков {last.DefenderFell.Count}.", cell, ours);
                if (ours.Party.Members.Count == 0) Parties.Remove(ours); else Flee(ours, cell);
                return last;
            }
            Beaten(h);
            Tell(NewsKind.Fought, $"Бой {Names(cell).prep}: отряд разбил войско орды. Пало налётчиков {last.DefenderFell.Count}, наших {last.AttackerFell.Count}.", cell, ours);
        }
        if (cell.Owner != Owner.Ours && cell.Sum.Keep.Count > 0)
        {
            bool lair = cell.Holding == Holding.HordeTown && cell.Owner == Owner.Horde;
            var keepers = cell.Owner == Owner.Horde ? "гарнизон" : Untaken(cell) ? cell.Trove == TroveKind.Shrine ? "мертвецов святилища" : "стражу сокровища" : "зверей";
            // those met in the valley fight: the raiders who only come raiding are not in its garrison
            var holders = cell.Sum.Holders();
            last = holders.Count == 0 ? new QuickResult { AttackerWon = true, Odds = 1 }
                : QuickBattle.Fight(ours.Party.Members, null, holders, null, cell.Owner == Owner.Horde ? cell.Sum.Walls : 0, cell.Kind, Roll);
            HeroNews(last, cell, ours);
            cell.Sum.Keep.RemoveAll(m => Sim.Holds(m.Kind) && !holders.Contains(m));
            if (!last.AttackerWon)
            {
                Tell(NewsKind.Fought, $"Бой {Names(cell).prep}: отряд не взял {keepers}. Пало наших {last.AttackerFell.Count}, врагов {last.DefenderFell.Count}.", cell, ours);
                if (ours.Party.Members.Count == 0) Parties.Remove(ours); else Flee(ours, cell);
                return last;
            }
            Tell(NewsKind.Fought, $"Бой {Names(cell).prep}: отряд разбил {keepers}. Пало врагов {last.DefenderFell.Count}, наших {last.AttackerFell.Count}.", cell, ours);
            cell.Sum.Keep.Clear();
            if (cell.Owner == Owner.Horde) { Retake(cell, lair, ours); PartyGlory(ours, cell); }
            else if (Untaken(cell)) TakeTrove(cell, ours);
        }
        if (ours.Party.Members.Count == 0) Parties.Remove(ours);
        return last;
    }

    /// <summary>A cell taken from the horde: the first valley ours again, the lair the campaign won, any other nobody's.</summary>
    void Retake(MapCell cell, bool lair, MapParty ours)
    {
        cell.Owner = cell.Home ? Owner.Ours : Owner.None;
        cell.Sum.Raised = 0;
        if (cell.Home) { HomeFell = -1; Tell(NewsKind.Taken, "Первая долина снова наша.", cell, ours); }
        if (lair && Lair == null && !Won)
        {
            Won = true;
            Tell(NewsKind.Taken, "Логово орды взято. Победа.", cell, ours);
        }
        else if (lair && Lair != null)
        {
            int left = Lairs.Count;
            Tell(NewsKind.Taken, $"Логово орды взято. Осталось логов: {left}.", cell, ours);
        }
    }

    // ------------------------------------------------------------ the valleys we are not in

    /// <summary>The gold of the valleys we hold and are not in: their towns' and their mines' a minute, a little each step.</summary>
    void Earn(double dt)
    {
        double g = Blessed(Blessing.Plenty) ? PlentyGold / 60 * dt : 0;
        foreach (var c in Cells)
        {
            if (c.Owner != Owner.Ours || InValley && c == Here) continue;
            g += (c.Sum.TownGold + ValleySummary.MineGold(c.Kind) * ValleySummary.MineShare) / 60 * dt;
        }
        if (g <= 0) return;
        EarnedAway += g;
        if (InValley && Live != null) Live.Stock.Gold += g; else Gold += g;
    }

    /// <summary>
    /// A party of ours with workers that stands in a cell nobody holds, and no beast in it, raises an outpost there;
    /// raised, the cell is ours and the workers stay to work it.
    /// </summary>
    void Raise(double dt)
    {
        foreach (var p in Parties.ToArray())
        {
            if (p.Owner != Owner.Ours || p.Moving || p.Waits) continue;
            var c = At(p.Q, p.R);
            if (c == null || c.Owner != Owner.None || c.Sum.Keep.Count > 0 || InValley && c == Here || Foes(c) > 0) continue;
            int hands = 0;
            foreach (var m in p.Party.Members) if (m.Kind == Kinds.Worker) hands++;
            if (hands == 0) continue;
            c.Sum.Raised += (float)(hands * OutpostHands(p) / OutpostWork * dt / DayClock.Cycle);
            if (c.Sum.Raised < 1) continue;
            c.Sum.Raised = 1;
            c.Owner = Owner.Ours;
            for (int i = p.Party.Members.Count - 1; i >= 0; i--)
                if (p.Party.Members[i].Kind == Kinds.Worker) { c.Sum.Keep.Add(p.Party.Members[i]); p.Party.Members.RemoveAt(i); }
            if (p.Party.Members.Count == 0) Parties.Remove(p);
            Tell(NewsKind.Taken, $"Застава {Names(c).prep} готова: долина наша.", c, p);
            PartyGlory(p, c);
        }
    }

    /// <summary>
    /// A dawn on the map: the valleys we hold and are not in get a day of their workers' digging and cutting, the hurt
    /// of ours in them and in parties standing in them mend, the horde's at the lair too; then the horde's turn.
    /// </summary>
    void Dawn()
    {
        float mend = Blessed(Blessing.Mending) ? Mend * 2 : Mend;
        foreach (var c in Cells)
        {
            if (c.Owner == Owner.Ours && !(InValley && c == Here))
            {
                var (stone, logs, metal) = ValleySummary.Yield(c.Kind);
                int hands = Math.Min(c.Sum.Workers, ValleySummary.YieldHands);
                c.Sum.Stone += stone * hands; c.Sum.Logs += logs * hands; c.Sum.Metal += metal * hands;
                foreach (var m in c.Sum.Keep) Heal(m, mend);
            }
            if (c.Holding == Holding.HordeTown && c.Owner == Owner.Horde) foreach (var m in c.Sum.Keep) Heal(m, LairMend);
        }
        // our parties: standing in a valley of ours as it mends (its hero's Лекарь the more), elsewhere only by him
        foreach (var p in Parties)
        {
            if (p.Owner != Owner.Ours) continue;
            float share = PartyMend(p, !p.Moving && At(p.Q, p.R) is { Owner: Owner.Ours }) * (mend / Mend);
            if (share > 0) foreach (var m in p.Party.Members) Heal(m, share);
        }
        HordeDawn();
    }

    static void Heal(Traveller m, float share) => m.Hp = Math.Min(m.MaxHp, m.Hp + m.MaxHp * share);

    /// <summary>The first valley lost and no other taken in the days allowed: said once.</summary>
    void Judge()
    {
        if (Defeated && !_defeatSaid)
        {
            _defeatSaid = true;
            Tell(NewsKind.Lost, $"Кампания проиграна: {DaysToRetake} дня без своей долины.", Home, null);
        }
    }

    // ------------------------------------------------------------ the open valley and the map

    /// <summary>
    /// A valley we come into gets what the map kept for it: what its workers got while we were away into its stores; of
    /// ours in it, those who fell in battles counted while we were away are gone; never visited and ours or the horde's
    /// already, its keepers come in or stand at their posts.
    /// </summary>
    public void Settle(MapCell cell, Sim sim)
    {
        var s = cell.Sum;
        sim.Stock[Stockpile.Stone] += s.Stone * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] += s.Logs * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Metal] += s.Metal * VoxelWorld.PerM3;
        s.Stone = s.Logs = s.Metal = 0;
        if (cell.Visited && s.Known && cell.Owner == Owner.Ours) sim.FellAway(s.Keep);
        else if (!cell.Visited && cell.Owner == Owner.Ours && s.Keep.Count > 0)
        {
            var come = new Party { Side = cell.EntrySide };
            come.Members.AddRange(s.Keep);
            sim.Arrive(come, cell.EntrySide);
        }
        else if (!cell.Visited && cell.Owner == Owner.Horde && s.Keep.Count > 0)
        {
            // the garrison the map keeps for it, as the board counts it (those who only come raiding wait outside it)
            var kinds = new List<UnitType>();
            foreach (var m in s.Keep) kinds.Add(m.Kind);
            sim.Garrison(kinds);
        }
    }

    /// <summary>
    /// What the map keeps of a valley as we leave it (ValleySummary.cs). Ours: its keepers, walls and town; fallen, it is
    /// the horde's and they keep it. The horde's: its garrison as the fighting left it, and none left, it is taken from
    /// the horde. Nobody's: its beasts as they were (they are the map's, not the valley's).
    /// </summary>
    public void Summarise(MapCell cell, Sim sim)
    {
        if (cell.Owner == Owner.Ours)
        {
            cell.Sum.Take(sim, sim.Lost ? Team.Horde : Team.Player, KeyOf(cell));
            if (sim.Lost) Fall(cell);
        }
        else if (cell.Owner == Owner.Horde)
        {
            cell.Sum.Take(sim, Team.Horde);
            if (cell.Sum.Keep.Count == 0) Retake(cell, cell.Holding == Holding.HordeTown, null);
        }
    }

    // ------------------------------------------------------------ what is said

    void Tell(NewsKind kind, string text, MapCell cell, MapParty p)
    {
        News.Add(new MapNews { Kind = kind, Text = text, Cell = cell, Party = p });
        if (News.Count > 200) News.RemoveRange(0, News.Count - 200);
    }

    /// <summary>A cell named as said: by itself, «in» it, «at» it (what is taken or struck), «of» it.</summary>
    public static (string nom, string prep, string acc, string gen) Names(MapCell c)
    {
        if (c.Holding == Holding.HordeTown) return ("логово орды", "в логове орды", "логово орды", "логова орды");
        return c.Kind switch
        {
            ValleyKind.First => ("первая долина", "в первой долине", "первую долину", "первой долины"),
            ValleyKind.Ore => ("рудная долина", "в рудной долине", "рудную долину", "рудной долины"),
            ValleyKind.Sacred => ("священная долина", "в священной долине", "священную долину", "священной долины"),
            ValleyKind.Forest => ("лесная долина", "в лесной долине", "лесную долину", "лесной долины"),
            ValleyKind.Marsh => ("речная долина", "в речной долине", "речную долину", "речной долины"),
            _ => ("горная долина", "в горной долине", "горную долину", "горной долины"),
        };
    }

    static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        return m10 == 1 && m100 != 11 ? one : m10 is >= 2 and <= 4 && (m100 < 12 || m100 > 14) ? few : many;
    }

    internal static string Raiders(int n) => $"{n} {Plural(n, "налётчик", "налётчика", "налётчиков")}";
    static string Men(int n) => $"{n} {Plural(n, "человек", "человека", "человек")}";
    static string Led(MapParty p) => p.Lord != null ? $", ведёт {p.Lord.Name}" : "";

    // ------------------------------------------------------------ the map's file

    internal static void WriteLord(BinaryWriter w, Warlord l)
    {
        w.Write(l != null);
        if (l == null) return;
        w.Write(l.Id); w.Write(l.Name); w.Write(l.Of); w.Write(l.Kind.Key); w.Write(l.Level); w.Write(l.Xp); w.Write(l.Health); w.Write((byte)l.Fate);
    }

    internal static Warlord ReadLord(BinaryReader r)
    {
        if (!r.ReadBoolean()) return null;
        var l = new Warlord { Id = r.ReadInt32(), Name = r.ReadString(), Of = r.ReadString() };
        l.Kind = Sim.KindOf(r.ReadString()) ?? Kinds.Orc;
        l.Level = r.ReadInt32(); l.Xp = r.ReadSingle(); l.Health = r.ReadSingle(); l.Fate = (LordFate)r.ReadByte();
        return l;
    }

    /// <summary>A party's way and what it is out to do, after the party itself.</summary>
    void WriteWay(BinaryWriter w, MapParty p)
    {
        w.Write((byte)p.Owner); w.Write(p.Id);
        w.Write(p.Path?.Count ?? 0);
        if (p.Path != null) foreach (var c in p.Path) { w.Write(c.Q); w.Write(c.R); }
        w.Write(p.Progress);
        WriteLord(w, p.Lord);
        w.Write((byte)p.Aim); w.Write(p.GoalQ); w.Write(p.GoalR); w.Write(p.CameQ); w.Write(p.CameR); w.Write(p.SetOut); w.Write(p.Waits); w.Write(p.Den);
        // where it stands on the land and the way it walks over it
        w.Write(p.AtX); w.Write(p.AtZ); w.Write(p.Facing); w.Write(p.WalkIn);
        w.Write(p.Trail?.Count ?? -1);
        if (p.Trail != null) foreach (var (x, z) in p.Trail) { w.Write(x); w.Write(z); }
    }

    void ReadWay(BinaryReader r, MapParty p)
    {
        p.Owner = (Owner)r.ReadByte(); p.Id = r.ReadInt32();
        int n = r.ReadInt32();
        p.Path = new List<MapCell>();
        for (int i = 0; i < n; i++) { var c = At(r.ReadInt32(), r.ReadInt32()); if (c != null) p.Path.Add(c); }
        p.Progress = r.ReadDouble();
        p.Lord = ReadLord(r);
        p.Aim = (PartyAim)r.ReadByte(); p.GoalQ = r.ReadInt32(); p.GoalR = r.ReadInt32(); p.CameQ = r.ReadInt32(); p.CameR = r.ReadInt32();
        p.SetOut = r.ReadSingle(); p.Waits = r.ReadBoolean(); p.Den = r.ReadInt32();
        p.AtX = r.ReadSingle(); p.AtZ = r.ReadSingle(); p.Facing = r.ReadSingle(); p.WalkIn = r.ReadBoolean();
        int trail = r.ReadInt32();
        if (trail >= 0)
        {
            p.Trail = new List<(float X, float Z)>(trail);
            for (int i = 0; i < trail; i++) p.Trail.Add((r.ReadSingle(), r.ReadSingle()));
        }
    }

    /// <summary>The turn's own state, after the parties: the dawns seen, the luck, the parties off the map into the valley, what each cell keeps, the horde's realm.</summary>
    void WriteTurn(BinaryWriter w)
    {
        w.Write(_dawnDay); w.Write(_nextParty); w.Write(_rng); w.Write(_rngSet); w.Write(_defeatSaid); w.Write(Won); w.Write(EarnedAway);
        foreach (var list in new[] { _raidsDue, _arrivals })
        {
            w.Write(list.Count);
            foreach (var p in list) { w.Write(p.Q); w.Write(p.R); p.Party.Write(w); WriteWay(w, p); }
        }
        foreach (var c in Cells) c.Sum.Write(w);
        Realm.Write(w);
    }

    void ReadTurn(BinaryReader r)
    {
        _dawnDay = r.ReadInt32(); _nextParty = r.ReadInt32(); _rng = r.ReadUInt32(); _rngSet = r.ReadBoolean(); _defeatSaid = r.ReadBoolean();
        Won = r.ReadBoolean(); EarnedAway = r.ReadDouble();
        foreach (var list in new[] { _raidsDue, _arrivals })
            for (int n = r.ReadInt32(); n > 0; n--)
            {
                var p = new MapParty { Q = r.ReadInt32(), R = r.ReadInt32(), Party = Party.Read(r) };
                ReadWay(r, p);
                list.Add(p);
            }
        foreach (var c in Cells) c.Sum.Read(r);
        Realm.Read(r);
    }
}
