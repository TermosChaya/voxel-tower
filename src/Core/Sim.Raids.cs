using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>
/// The day and the night of the valley, kept on their own: the sun goes over in ten minutes, the last of them dusk, then
/// the night, and half a minute of dawn after it. A night holds as long as a raid that came in it is in the valley and
/// ends with it; a night with no raid in it lasts two and a half minutes. What brings the raids may bring them at
/// nightfall (the siege) or at any hour (the map, one day). The sun can be held at noon (the sandbox's horde on call):
/// the days are counted all the same, ten minutes each, for what grows by the day.
/// </summary>
public sealed class DayClock
{
    public const double DayLength = 600, Dusk = 60, DawnLength = 30, NightLength = 150;
    /// <summary>A day and the night after it, as the map counts them (a raid may hold a valley's night longer).</summary>
    public const double Cycle = DayLength + NightLength;
    /// <summary>The day it is, the first one 1: what grows by the day (the town's stocks) grows at each daybreak.</summary>
    public int Day = 1;
    /// <summary>Seconds of daylight left; set far on to keep the sun up (the tests and the staged scenes), or on by a truce.</summary>
    public double Left = DayLength;
    public bool Night;
    /// <summary>Seconds into the night.</summary>
    public double NightT;
    public double DawnLeft;
    /// <summary>The sun stands at noon (the sandbox's horde on call).</summary>
    public bool StandAtNoon;
    internal double Stood;

    /// <summary>How dark it is: 0 by day, 1 at night, and between at dusk and at dawn.</summary>
    public float Darkness => Night ? 1 : DawnLeft > 0 ? (float)(DawnLeft / DawnLength) : Left < Dusk ? (float)(1 - Left / Dusk) : 0;
    /// <summary>How far through the day it is, for the sun: 0 at dawn, ½ at noon, 1 at sunset and through the night.</summary>
    public float Share => Night ? 1 : (float)Math.Clamp(1 - Left / DayLength, 0, 1);

    internal void Tick(Sim s, float dt)
    {
        if (Night)
        {
            NightT += dt;
            // a raid that came at night holds it; with none, the night goes by itself
            if (!(s.Raids.Active && s.Raids.AtNight) && NightT >= NightLength) Break(s);
            return;
        }
        DawnLeft = Math.Max(0, DawnLeft - dt);
        if (StandAtNoon)
        {
            Left = Math.Max(DayLength / 2, Left - dt);
            if (Left <= DayLength / 2 && (Stood += dt) >= DayLength) { Stood -= DayLength; Day++; s.NewDay(); }
            return;
        }
        Left -= dt;
        if (Left <= 0) Fall(s);
    }

    /// <summary>The night falls now.</summary>
    public void Fall(Sim s)
    {
        if (Night) return;
        Night = true; NightT = 0; DawnLeft = 0;
        s.Nightfall();
    }

    /// <summary>The day breaks now: a new day, the sun up for ten minutes.</summary>
    public void Break(Sim s)
    {
        if (!Night) return;
        Night = false; NightT = 0;
        Left = DayLength; DawnLeft = DawnLength;
        Day++;
        s.NewDay();
    }

    /// <summary>The time as the map reads it off the valley: the day, and how far into it (the night's after the daylight).</summary>
    public ClockTime Now => new(Day, Night ? DayLength + Math.Min(NightT, NightLength) : Math.Clamp(DayLength - Left, 0, DayLength));

    /// <summary>
    /// The valley set to the map's time, as it is entered: the sun where it stands then, or the night so far on, without a
    /// nightfall or a daybreak said for it (a raid comes by the map, not by the valley's clock).
    /// </summary>
    public void Set(ClockTime t)
    {
        Day = Math.Max(1, t.Day);
        double at = Math.Clamp(t.At, 0, Cycle);
        Night = at >= DayLength;
        NightT = Night ? at - DayLength : 0;
        Left = Night ? 0 : DayLength - at;
        DawnLeft = Night ? 0 : Math.Max(0, DawnLength - at);
        Stood = 0;
    }

    internal void Write(BinaryWriter w) { w.Write(Day); w.Write(Left); w.Write(Night); w.Write(NightT); w.Write(DawnLeft); w.Write(StandAtNoon); w.Write(Stood); }

    internal void Read(BinaryReader r)
    {
        Day = r.ReadInt32(); Left = r.ReadDouble(); Night = r.ReadBoolean(); NightT = r.ReadDouble(); DawnLeft = r.ReadDouble();
        StandAtNoon = r.ReadBoolean(); Stood = r.ReadDouble();
    }
}

/// <summary>
/// What brings the horde's raids, and when: the siege's clock (a raid at every nightfall, twelve in all), the sandbox's
/// call, and one day the map, where a raid comes when the enemy's army walks into the valley's cell (by
/// <see cref="Sim.Raid"/>). Whatever brings them, the raid itself, the night, the threat, the rewards and the rest go
/// their own ways.
/// </summary>
public interface IRaidSource
{
    /// <summary>Its name in a save.</summary>
    string Key { get; }
    /// <summary>The sun held at noon while no raid is called.</summary>
    bool HoldsNoon { get; }
    /// <summary>Its next raid is drawn up ahead and shown on the map (the siege's and the sandbox's; not the map's, whose armies say for themselves).</summary>
    bool Foretold { get; }
    /// <summary>How many raids it brings in all; 0 when they go on.</summary>
    int Total(Sim s);
    /// <summary>Seconds until its next raid by its clock; −1 when it keeps no clock (or a raid is in the valley).</summary>
    double Until(Sim s);
    /// <summary>The threat its next raid comes with, before what the settlement draws on itself.</summary>
    float Threat(Sim s);
    /// <summary>How much the horde has learnt of the defences now (0 to 1).</summary>
    double IntelNow(Sim s);
    /// <summary>What a raid of it is called in the chronicle and on the clock: «Волна 3».</summary>
    string Title(int n);
    void Tick(Sim s, float dt);
    /// <summary>The night falls.</summary>
    void Nightfall(Sim s);
    /// <summary>The player calls the horde now; false when it cannot be called.</summary>
    bool CallNow(Sim s);
    /// <summary>The chronicle's line when a raid of it is beaten back.</summary>
    string RepelledLine(Sim s);
    /// <summary>A truce bought from the envoy puts the next raid off so long; nothing where there is no clock to put off.</summary>
    void Delay(Sim s, double seconds);
}

/// <summary>
/// The siege as it has always gone: a raid at every nightfall, twelve in all, each stronger than the one before; the
/// horde scouts the defences through the day and knows the more the later it comes. Called early, it comes at once and
/// the night with it, knowing only what it scouted so far.
/// </summary>
public sealed class SiegeScenario : IRaidSource
{
    public string Key => "siege";
    public bool HoldsNoon => false;
    public bool Foretold => true;
    public int Total(Sim s) => s.Sandbox ? 0 : Waves.Total;
    public double Until(Sim s) => s.Raids.Active || s.Clock.Night ? -1 : s.Clock.Left;
    /// <summary>Its raids come one a night, each night's the stronger: the threat is the night's number.</summary>
    public float Threat(Sim s) => s.Raids.Count + (s.Raids.Active ? 0 : 1);
    public double IntelNow(Sim s) => Math.Clamp(1 - s.Clock.Left / DayClock.DayLength, 0, 1);
    public string Title(int n) => $"Волна {n}";
    double _was = -1, _called = -1;

    public void Tick(Sim s, float dt)
    {
        var c = s.Clock;
        if (!c.Night && !s.Raids.Active && _was >= DayClock.Dusk && c.Left < DayClock.Dusk)
            s.Say(s.Raids.Count == 0
                ? "Закат. Через минуту придёт орда. Ночью лучники хорошо видят только освещённых врагов."
                : "Солнце садится: через минуту орда.", 2);
        _was = c.Night ? -1 : c.Left;
    }

    public void Nightfall(Sim s)
    {
        double intel = _called >= 0 ? _called : 1;
        _called = -1;
        if (!s.Raids.Active) s.BeginRaid(intel, atNight: true);
    }

    public bool CallNow(Sim s)
    {
        _called = IntelNow(s);
        if (s.Clock.Night) Nightfall(s); else s.Clock.Fall(s);
        return true;
    }

    public string RepelledLine(Sim s) => $"Волна {s.Raids.Count} отбита. До следующей 10 минут.";
    public void Delay(Sim s, double seconds) => s.Clock.Left += seconds;
}

/// <summary>
/// The sandbox's horde: it comes only when called, the sun standing at noon meanwhile; called, it comes at once, the
/// night with it, knowing all it would have learnt by sunset. Each call brings the next of the siege's nights.
/// </summary>
public sealed class OnCallRaids : IRaidSource
{
    public string Key => "call";
    public bool HoldsNoon => true;
    public bool Foretold => true;
    public int Total(Sim s) => s.Sandbox ? 0 : Waves.Total;
    public double Until(Sim s) => -1;
    public float Threat(Sim s) => s.Raids.Count + (s.Raids.Active ? 0 : 1);
    public double IntelNow(Sim s) => 1;
    public string Title(int n) => $"Волна {n}";
    public void Tick(Sim s, float dt) { }
    public void Nightfall(Sim s) { if (!s.Raids.Active) s.BeginRaid(1, atNight: true); }

    public bool CallNow(Sim s)
    {
        if (s.Clock.Night) Nightfall(s); else s.Clock.Fall(s);
        return true;
    }

    public string RepelledLine(Sim s) => $"Волна {s.Raids.Count} отбита. Следующая придёт, когда её позовёте.";
    public void Delay(Sim s, double seconds) { }
}

/// <summary>
/// The map's way, ready for it: no clock of raids. The days and the nights go by of themselves; a raid comes only when
/// something calls <see cref="Sim.Raid"/> — one day the enemy's army walking into the valley's cell — by day or by
/// night, by any side. The threat grows by the day. It cannot be called by the player.
/// </summary>
public sealed class MapRaids : IRaidSource
{
    public string Key => "map";
    public bool HoldsNoon => false;
    public bool Foretold => false;
    public int Total(Sim s) => 0;
    public double Until(Sim s) => -1;
    public float Threat(Sim s) => s.Clock.Day;
    public double IntelNow(Sim s) => .5;
    public string Title(int n) => $"Набег {n}";
    public void Tick(Sim s, float dt) { }
    public void Nightfall(Sim s) { }
    public bool CallNow(Sim s) => false;
    public string RepelledLine(Sim s) => $"Набег {s.Raids.Count} отбит.";
    public void Delay(Sim s, double seconds) { }
}

/// <summary>
/// A raid of the horde in the valley: its bands coming in one after another, a band at a run, the next some seconds
/// later somewhere else; over when none is left to come and none is left in the valley. It does not know what brought
/// it — the siege's clock, the sandbox's call, one day an army on the map — nor how strong it is: it is told.
/// </summary>
public sealed class Raids
{
    /// <summary>A raid in the valley now.</summary>
    public bool Active;
    /// <summary>Raids come so far (the present one among them), and raids beaten back.</summary>
    public int Count, Repelled;
    /// <summary>How much the horde learnt of the defences before the present raid (0 to 1).</summary>
    public double Intel;
    /// <summary>The threat the present (or the last) raid came with.</summary>
    public float Threat = 1;
    /// <summary>The present raid came at nightfall: the night lasts as long as it does.</summary>
    public bool AtNight;
    internal readonly Queue<(UnitType kind, Warband band, Survivor back)> _queue = new();
    double _giveUpAt;
    internal Warband _lastBand;
    internal double _spawnT;

    public int Coming => _queue.Count;

    static string Bands(int n) => n == 1 ? "одна ватага" : n is >= 2 and <= 4 ? $"{n} ватаги" : $"{n} ватаг";

    /// <summary>The raid comes: the bands drawn up for it march in one after another.</summary>
    internal void Begin(Sim s, double intel, bool atNight)
    {
        Intel = intel;
        Count++;
        Active = true;
        AtNight = atNight;
        _queue.Clear();
        if (!s.Bands.Exists(b => b.State == BandState.Coming)) s.PlanRaid(s.Threat, Count);
        Threat = s.PlannedThreat;
        var bands = s.Muster();
        int total = 0, back = 0;
        foreach (var b in bands)
        {
            foreach (var t in b.Roster) _queue.Enqueue((t, b, null));
            foreach (var v in b.Veterans) _queue.Enqueue((v.Kind, b, v));
            total += b.Size; back += b.Veterans.Count;
        }
        s.Escaped.Clear();
        s.NightBegins(bands.Count);
        s.BattleBegins(Count, Threat, bands);
        // timber for ladders and bridges, earth for fills, brought along: the more, the stronger the raid
        s.HordeStock[Stockpile.Wood] += (2.4 + 1 * Threat) * VoxelWorld.PerM3;
        s.HordeStock[Stockpile.Earth] += (3.2 + 2 * Threat) * VoxelWorld.PerM3;
        _spawnT = 1;
        // the fields are rebuilt in the background for what the horde brought; until then the old ones lead the way
        if (s.Fields.Count == 0) s.ComputeFields();
        s.InvalidateFields();
        s.Recall();
        string intelSaid = Intel >= .995 ? "" : $" Разведать вас она успела на {Intel * 100:0} %.";
        string again = back == 0 ? "" : back == 1 ? " С ними и один из бежавших в прошлый раз." : $" С ними и бежавшие в прошлый раз: {back}.";
        s.Say($"Рог! {s.Source.Title(Count)}: {total} налётчиков, {Bands(bands.Count)}, идут {Sim.FromEdge(bands, s.HomeEdge)}.{again}{intelSaid}", 2);
    }

    /// <summary>More bands join the raid in the valley (one more army walked in): they come after those still to come.</summary>
    internal void Join(List<Warband> bands)
    {
        foreach (var b in bands) foreach (var t in b.Roster) _queue.Enqueue((t, b, null));
    }

    internal void Tick(Sim s, float dt)
    {
        if (!Active) return;
        _spawnT -= dt;
        if (_queue.Count > 0 && _spawnT <= 0)
        {
            var (kind, band, back) = _queue.Dequeue();
            if (band != null) s.SpawnRaider(kind, band, back); else s.SpawnRaider(kind);
            _lastBand = band;
            // a band comes in at a run, one after another; the next band some seconds later, somewhere else
            bool sameBand = _queue.Count > 0 && _queue.Peek().band == band;
            _spawnT = sameBand ? .35 + s.Rng.NextDouble() * .2 : 4 + s.Rng.NextDouble() * 3;
        }
        // none of the raid left but its engines: they are given up and burn (Sim.Engines.cs)
        if (_queue.Count == 0 && s.Time >= _giveUpAt) { _giveUpAt = s.Time + 1; s.GiveUpEngines(); }
        if (_queue.Count == 0 && !s.AnyRaider())
        {
            Active = false;
            Repelled++;
            s.CloseBattle(held: true);
            s.Bands.RemoveAll(b => b.State != BandState.Coming);
            s.RaidRepelled();
        }
    }

    /// <summary>Ends the raid at once: no more raiders come, those in the valley vanish, the day is back (tests and staged scenes).</summary>
    public void CallOff(Sim s)
    {
        _queue.Clear();
        foreach (var u in s.Units) if (u.Team == Team.Horde && !u.Dead) { u.Dead = true; u.DiedAt = -999; }
        Active = false;
        s.Clock.Night = false; s.Clock.NightT = 0;
        s.Clock.Left = DayClock.DayLength;
        s.Bands.Clear();
        s.PlanRaid();
    }
}

/// <summary>
/// The siege's old face, over the day's clock, the raid in the valley and what brings the raids: what the screens, the
/// tests and the staged scenes read and set as they always did. «Волна» is the siege's word for a raid; the number is
/// the raids come, and the countdown the daylight left.
/// </summary>
public sealed class Waves
{
    readonly Sim _s;
    internal Waves(Sim s) { _s = s; }

    public const int Total = 12;
    public const double Prep = DayClock.DayLength, Dusk = DayClock.Dusk, Dawn = DayClock.DawnLength;

    public int Number { get => _s.Raids.Count; set => _s.Raids.Count = value; }
    /// <summary>A raid in the valley; set by hand (the staged scenes), the night comes and goes with it.</summary>
    public bool Active
    {
        get => _s.Raids.Active;
        set { _s.Raids.Active = value; _s.Raids.AtNight = value; _s.Clock.Night = value; _s.Clock.NightT = 0; }
    }
    public double Countdown { get => _s.Clock.Left; set => _s.Clock.Left = value; }
    public double DawnLeft { get => _s.Clock.DawnLeft; set => _s.Clock.DawnLeft = value; }
    public bool OnCall
    {
        get => _s.Source is OnCallRaids;
        set { if (value != OnCall) _s.SetSource(value ? new OnCallRaids() : new SiegeScenario()); }
    }
    public float Darkness => _s.Clock.Darkness;
    public float Day => _s.Clock.Share;
    public double Intel { get => _s.Raids.Intel; set => _s.Raids.Intel = value; }
    public double IntelNow => _s.Source.IntelNow(_s);
    public int Coming => _s.Raids.Coming;
    internal Queue<(UnitType kind, Warband band, Survivor back)> _queue => _s.Raids._queue;
    internal Warband _lastBand { get => _s.Raids._lastBand; set => _s.Raids._lastBand = value; }
    internal double _spawnT { get => _s.Raids._spawnT; set => _s.Raids._spawnT = value; }

    public void Tick(Sim s, float dt) => s.TickRaids(dt);
    public void CallOff(Sim s) => s.Raids.CallOff(s);
    /// <summary>Calls the horde before its time; it comes knowing only what it scouted so far.</summary>
    public double StartNow(Sim s) => s.CallRaid() ? s.Raids.Intel : 0;
}

public sealed partial class Sim
{
    /// <summary>The day and the night.</summary>
    public readonly DayClock Clock = new();
    /// <summary>The raid in the valley, and the raids so far.</summary>
    public readonly Raids Raids = new();
    /// <summary>What brings the raids.</summary>
    public IRaidSource Source { get; private set; } = new SiegeScenario();
    /// <summary>The threat the bands coming next were drawn up for.</summary>
    public float PlannedThreat = 1;

    /// <summary>A raid comes before long: within so many seconds by its source's clock.</summary>
    public bool RaidSoon(double seconds) => Source.Until(this) is >= 0 and var u && u < seconds;

    /// <summary>How many raids the source brings in all, or as good as no end.</summary>
    public int RaidsInAll => Source.Total(this) is > 0 and var t ? t : int.MaxValue;

    /// <summary>A source of raids by its name: «siege», «call», «map».</summary>
    public static IRaidSource SourceOf(string key) => key switch { "call" => new OnCallRaids(), "map" => new MapRaids(), _ => new SiegeScenario() };

    public void SetSource(IRaidSource src)
    {
        Source = src;
        Clock.StandAtNoon = src.HoldsNoon;
    }

    // ------------------------------------------------------------ the threat

    /// <summary>What the settlement draws on itself by its size: a quarter of a night's strength a rank (Sim.Templates.cs).</summary>
    public float WealthThreat => WealthShare * Standing;
    /// <summary>How much a rank of the settlement adds to the threat (0 turns it off).</summary>
    public static float WealthShare = .25f;

    /// <summary>
    /// How strong the next raid comes: what brings it says (the siege: the night's number; the map: the army that walks
    /// in), and what the settlement draws on itself by its size is added. The horde's kinds come in by its whole part,
    /// the common ones by all of it.
    /// </summary>
    public float Threat => Source.Threat(this) + WealthThreat;

    /// <summary>The next raid drawn up for the threat now.</summary>
    public void PlanRaid() => PlanRaid(Threat, Raids.Count + 1);

    /// <summary>The raid of the siege's night n drawn up as it always was (the tests and the staged scenes).</summary>
    public void PlanWave(int n) => PlanRaid(n, n);

    // ------------------------------------------------------------ the raids' turn

    internal void TickRaids(float dt)
    {
        if (Over) return;
        Clock.Tick(this, dt);
        Source.Tick(this, dt);
        Raids.Tick(this, dt);
    }

    internal void Nightfall() => Source.Nightfall(this);

    /// <summary>A raid begins now, the horde knowing so much (0 to 1) of the defences.</summary>
    public void BeginRaid(double intel, bool atNight) => Raids.Begin(this, intel, atNight);

    /// <summary>The player calls the horde now (the button, the space bar, the sandbox's call).</summary>
    public bool CallRaid() => !Raids.Active && !Over && Source.CallNow(this);

    /// <summary>
    /// A raid from outside the siege's clock — one day the enemy's army walking into the valley's cell: these kinds
    /// come in by that side of the land (0 the side from the northern point round by the east, <see cref="WestEdge"/> the
    /// western), knowing so much of the defences, led by a warlord if it has one (he comes at the head of a band of his
    /// own guard, in the middle of the side; every band is his army). It comes at whatever hour it is; with a raid in the
    /// valley already, they join it.
    /// </summary>
    public void Raid(IReadOnlyList<UnitType> roster, int edge, double intel = .5, Warlord lord = null)
    {
        var list = new List<UnitType>(roster);
        if (list.Count == 0 && lord == null) return;
        var bands = list.Count > 0 ? DrawBands(list, edge, Raids.Count + 1) : new List<Warband>();
        if (lord != null)
        {
            var own = new Warband { Id = _nextBand++, State = BandState.Coming, Name = $"Дружина {lord.Of}", LordsOwn = true };
            own.Roster.Add(lord.Kind);
            own.Roster.AddRange(LordGuard(lord));
            var (ex, ez) = EdgePoint(edge, .5);
            own.Entry = NearestStand(ex, ez, 3);
            bands.Insert(0, own);
            foreach (var b in bands) b.Lord = lord;
            Rested(lord);
            lord.Fate = LordFate.Coming;
            // a goblin lays one of his bands in wait (Sim.Ambush.cs)
            if (lord.Gift == LordGift.Ambush) TellOffAmbush(bands);
        }
        foreach (var b in bands) b.Edge = edge;
        if (Raids.Active)
        {
            Bands.AddRange(bands);
            Raids.Join(bands);
            _nightLeaders += bands.Count;
            return;
        }
        // what the source had drawn up for its own raid waits for it
        var waiting = Bands.FindAll(b => b.State == BandState.Coming);
        Bands.RemoveAll(b => b.State == BandState.Coming);
        Bands.AddRange(bands);
        float planned = PlannedThreat;
        PlannedThreat = ThreatOf(list) + (lord?.Level ?? 0);
        Raids.Begin(this, intel, atNight: Clock.Night);
        PlannedThreat = planned;
        Bands.AddRange(waiting);
    }

    /// <summary>The threat a roster stands for, read back by its size against the siege's nights.</summary>
    public float ThreatOf(IReadOnlyList<UnitType> roster)
    {
        for (int n = 1; n <= 20; n++) if (Roster(n, 1).Count >= roster.Count) return n;
        return 20;
    }

    /// <summary>A raid beaten back: said, rewarded, its dawn; the night over, and the next drawn up (or the end of it all).</summary>
    internal void RaidRepelled()
    {
        int total = Source.Total(this);
        bool last = total > 0 && Raids.Count >= total;
        Say(Source.RepelledLine(this), 1);
        RaidReward();
        Dawn(Raids.Count, last);
        StandDown(beaten: true);
        if (Raids.AtNight && Clock.Night) Clock.Break(this);
        if (last) { Won = true; Over = true; Say("Осада снята. Вы выстояли.", 1); }
        else if (Source.Foretold) PlanRaid();
    }

    /// <summary>A new day: what grows by the day grows.</summary>
    internal void NewDay() => TownDay();

    // ------------------------------------------------------------ deeds

    /// <summary>Knowledge earned by deeds and not yet paid out: it comes with a raid's reward.</summary>
    public double DeedKnowledge;
    /// <summary>The landmarks taken that have paid their knowledge (each once).</summary>
    readonly HashSet<string> _deedPlaces = new();

    /// <summary>What a raider's death is worth in knowledge: the stronger and the rarer, the more; a band's leader more again.</summary>
    static double DeedOf(Unit u) =>
        (u.T == Kinds.HillGiant ? .5 : u.T == Kinds.Troll ? .25 : u.T == Kinds.Shaman || u.T == Kinds.Witch || u.T == Kinds.Necromancer ? .25
            : u.T == Kinds.Alchemist ? .15 : Kinds.IsEngine(u.T) ? .1 : 0) + (u.Leader ? .25 : 0) + (u.Champion > 0 ? 1 : 0);

    /// <summary>A raider fell: its worth set by till the raid's reward.</summary>
    void KillDeed(Unit u)
    {
        if (u.Team == Team.Horde && u.T != Kinds.Thrall) DeedKnowledge += DeedOf(u);
    }

    /// <summary>A place taken (a mine, a place of power) the first time: a knowledge or two for it, at once.</summary>
    internal void PlaceDeed(string key, string name, int know)
    {
        if (!_deedPlaces.Add(key)) return;
        Knowledge += know;
        Say($"За {name}: {know} {Plural(know, "знание", "знания", "знаний")}.", 1);
    }

    /// <summary>A building of the town put up (or raised a storey): a knowledge for it, at once.</summary>
    internal void BuildingDeed(string name)
    {
        Knowledge += 1;
        Say($"За постройку «{name}»: 1 знание.", 1);
    }

    /// <summary>
    /// A raid beaten back: gold by how strong it came, and knowledge for beating it back (two, and as much again as its
    /// threat) with what its fallen were worth on top.
    /// </summary>
    void RaidReward()
    {
        double gold = 30 + 10 * Math.Round(Raids.Threat);
        double deeds = 2 + Math.Round(Raids.Threat) + DeedKnowledge;
        int know = (int)Math.Floor(deeds);
        DeedKnowledge = deeds - know;
        Stock.Gold += gold;
        Knowledge += know;
        Say($"Награда за волну: {gold:0} золота и {know} {Plural(know, "знание", "знания", "знаний")}.", 1);
    }

    /// <summary>Which way they come in, for the horn: «с запада», or from several sides.</summary>
    internal static string FromEdge(List<Warband> bands, int home)
    {
        var edges = new HashSet<int>();
        foreach (var b in bands) edges.Add(b.Edge < 0 ? home : b.Edge);
        if (edges.Count != 1) return "с разных сторон";
        foreach (int e in edges) return EdgeFrom[e];
        return "с запада";
    }

    /// <summary>The land's sides, from the northern point round by the east; the western is the fifth.</summary>
    public const int WestEdge = 4;
    /// <summary>The side the siege's own raids come in by: the valley's (<see cref="Site.EntrySide"/>); on the box, the west.</summary>
    public int HomeEdge => Site.Hex == null ? WestEdge : Site.EntrySide;
    /// <summary>«с востока»: where a side of the land is, as the horn and the horde's tab say it.</summary>
    public static string EdgeName(int edge) => EdgeFrom[((edge < 0 ? WestEdge : edge) % 6 + 6) % 6];

    /// <summary>The horde's tab's line: how strong the next raid is drawn up, of what, and what it is made of.</summary>
    public string ThreatLine()
    {
        float from = Source.Threat(this), wealth = WealthThreat;
        string of = Source is MapRaids ? $"день {Clock.Day}" : $"ночь {from:0}";
        string line = $"Угроза {PlannedThreat:0.##}: {of}" + (wealth > 0 ? $" и поселение +{wealth:0.##}." : ".");
        int coming = 0, bands = 0;
        foreach (var b in Bands) if (b.State == BandState.Coming) { coming += b.Size; bands++; }
        if (coming > 0) line += $" Ожидается {coming} налётчиков, ватаг: {bands}.";
        return line;
    }
    static readonly string[] EdgeFrom = { "с северо-востока", "с востока", "с юго-востока", "с юго-запада", "с запада", "с северо-запада" };
    /// <summary>«на восток»: the way out to a side of the land.</summary>
    public static string EdgeToward(int edge) => EdgeTo[((edge < 0 ? WestEdge : edge) % 6 + 6) % 6];
    /// <summary>«на востоке»: out by a side of the land.</summary>
    public static string EdgeBy(int edge) => EdgeAt[((edge < 0 ? WestEdge : edge) % 6 + 6) % 6];
    static readonly string[] EdgeTo = { "на северо-восток", "на восток", "на юго-восток", "на юго-запад", "на запад", "на северо-запад" };
    static readonly string[] EdgeAt = { "на северо-востоке", "на востоке", "на юго-востоке", "на юго-западе", "на западе", "на северо-западе" };

    // ------------------------------------------------------------ the save

    void WriteRaids(BinaryWriter w)
    {
        Clock.Write(w);
        w.Write(Source.Key);
        w.Write(Raids.Repelled); w.Write(Raids.Threat); w.Write(Raids.AtNight);
        w.Write(PlannedThreat); w.Write(DeedKnowledge);
        w.Write(_deedPlaces.Count); foreach (string k in _deedPlaces) w.Write(k);
        var edged = Bands.FindAll(b => b.Edge >= 0);
        w.Write(edged.Count); foreach (var b in edged) { w.Write(b.Id); w.Write(b.Edge); }
    }

    void ReadRaids(BinaryReader r)
    {
        Clock.Read(r);
        string key = r.ReadString();
        SetSource(SourceOf(key));
        Clock.StandAtNoon = Source.HoldsNoon;
        Raids.Repelled = r.ReadInt32(); Raids.Threat = r.ReadSingle(); Raids.AtNight = r.ReadBoolean();
        PlannedThreat = r.ReadSingle(); DeedKnowledge = r.ReadDouble();
        _deedPlaces.Clear();
        for (int i = r.ReadInt32(); i > 0; i--) _deedPlaces.Add(r.ReadString());
        for (int i = r.ReadInt32(); i > 0; i--)
        {
            int id = r.ReadInt32(), edge = r.ReadInt32();
            if (Bands.Find(b => b.Id == id) is { } b) b.Edge = edge;
        }
    }
}
