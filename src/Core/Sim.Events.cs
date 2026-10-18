using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>The weather over the valley.</summary>
public enum Weather : byte { Clear, Rain, Fog, Wind }

/// <summary>What the horde's envoy offers: time for gold, a smaller wave for stores, the next wave without its engines.</summary>
public enum DealKind : byte { None, Delay, Tribute, NoEngines, Ransom }

/// <summary>The horde's offer while its envoy waits for an answer.</summary>
public sealed class Deal
{
    public DealKind Kind;
    /// <summary>What it asks: gold (store 0) or so many voxels of a store; for a ransom, what it pays for the prisoners.</summary>
    public int Store;
    public double Cost;
    public Unit Envoy;
    /// <summary>When it stops waiting and goes, taking no for an answer.</summary>
    public double Until;
}

/// <summary>
/// What comes of itself, now and then, without any storyteller weighing it: weather (rain that drowns fires and
/// spoils aim, fog that hides the horde till it is close, wind that drives fire before it), a rare beast in the valley
/// (a white stag worth a deal of gold to whoever runs it down, a cave bear that goes for anyone), and from the second
/// wave an envoy of the horde under a white rag, come to bargain: time for gold, a smaller wave for stores, or the
/// next wave without its engines. Kill the envoy, and the horde comes the angrier for it.
/// </summary>
public sealed partial class Sim
{
    public Weather Weather;
    /// <summary>How long the weather has yet to last.</summary>
    public double WeatherLeft;
    double _nextWeather = 200, _nextBeast = 150, _nextEnvoy = 120;
    /// <summary>The events' own dice, so that what comes of itself does not change how everything else falls out.</summary>
    readonly Random _fate = new(1337);
    /// <summary>The horde's offer waiting for an answer, or null.</summary>
    public Deal Offer;
    /// <summary>The horde's envoy was cut down: its next wave comes a fifth the stronger.</summary>
    public bool Angered;
    /// <summary>Taken from the next wave by a bargain: a share of its raiders, its engines.</summary>
    internal double _waveShare = 1;
    internal bool _waveNoEngines;

    /// <summary>What rain does to fire catching and burning on, and to aim; what fog does to how far a bow is any use.</summary>
    public float RainFire => Weather == Weather.Rain ? .3f : Weather == Weather.Wind ? 1.8f : 1;
    public float WeatherAim => Weather switch { Weather.Rain => .85f, Weather.Wind => .9f, _ => 1 };
    /// <summary>How far one sees in the fog, as a share; the Eye of the spells sees through it.</summary>
    public float FogRange => Weather == Weather.Fog && Time >= EyeUntil ? .6f : 1;

    public static string WeatherName(Weather w) => w switch { Weather.Rain => "дождь", Weather.Fog => "туман", Weather.Wind => "ветер", _ => "ясно" };

    /// <summary>Once a second: the weather changes, a rare beast comes, an envoy is sent.</summary>
    void TickEvents()
    {
        if (Over) return;
        if (Weather != Weather.Clear)
        {
            WeatherLeft -= .5;
            if (WeatherLeft <= 0)
            {
                Say(Weather switch { Weather.Rain => "Дождь кончился.", Weather.Fog => "Туман рассеялся.", _ => "Ветер стих." }, 0);
                Weather = Weather.Clear;
                _nextWeather = Time + 240 + _fate.NextDouble() * 240;
            }
        }
        else if (Time >= _nextWeather)
        {
            _nextWeather = Time + 180 + _fate.NextDouble() * 180;
            if (Waves.Number >= 1 && _fate.NextDouble() < .45) StartWeather((Weather)(1 + _fate.Next(3)), 150 + _fate.NextDouble() * 210);
        }
        if (Time >= _nextBeast)
        {
            _nextBeast = Time + 300 + _fate.NextDouble() * 300;
            RareBeast();
        }
        if (Offer == null && !Waves.Active && Waves.Number >= 1 && Time >= _nextEnvoy && Waves.Countdown > 150)
        {
            _nextEnvoy = Time + 400 + _fate.NextDouble() * 400;
            if (_fate.NextDouble() < .4) SendEnvoy();
        }
        if (Offer != null) WatchOffer();
    }

    /// <summary>Weather comes over the valley for so long.</summary>
    public void StartWeather(Weather w, double seconds)
    {
        Weather = w;
        WeatherLeft = seconds;
        Say(w switch
        {
            Weather.Rain => "Пошёл дождь: огонь едва берётся и гаснет, стрелы летят хуже.",
            Weather.Fog => "Опустился туман: лучники видят врага лишь вблизи.",
            _ => "Поднялся ветер: огонь бежит вдвое быстрее, стрелы сносит.",
        }, 2);
    }

    double _tickEventsAt;
    void TickEventsHalf(double now)
    {
        if (now - _tickEventsAt < .5) return;
        _tickEventsAt = now;
        TickEvents();
    }

    // ------------------------------------------------------------ rare beasts

    /// <summary>Now and then a rare beast in the valley: a white stag, or from the third wave a cave bear.</summary>
    internal void RareBeast()
    {
        bool bear = Waves.Number >= 2 && _fate.NextDouble() < .45;
        if (!bear && _fate.NextDouble() > .5) return;
        var t = bear ? Kinds.Bear : Kinds.WhiteStag;
        for (int tries = 0; tries < 40; tries++)
        {
            int x = Site.SpawnStrip + 60 + _fate.Next(World.SX - Site.SpawnStrip - 120), z = 40 + _fate.Next(World.SZ - 80);
            if (Math.Abs(x - Site.KeepX) < 260 && Math.Abs(z - Site.KeepZ) < 260) continue;
            int c = NearestStand(x, z, t.Move.Height);
            if (c < 0 || Nav.Water[c] > 0) continue;
            var u = Spawn(t, c);
            u.Home = c;
            Say(bear ? "Из пещер вышел медведь: он бросается на всякого, кто подойдёт, — и на орду тоже."
                     : "В долине видели белого оленя: за него дадут 60 золота — если догонят.", 2, u);
            return;
        }
    }

    /// <summary>A bear goes for whoever comes near it, ours or the horde's; true while it is set on someone.</summary>
    bool BearTick(Unit u)
    {
        if (u.T != Kinds.Bear || u.Target is { Dead: false }) return false;
        Unit near = null; float nd = 9 * VoxelWorld.PerMetre;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Team == Team.Wild || o.Envoy || Kinds.IsEngine(o.T)) continue;
            float d = Dist(u, o);
            if (d < nd) { nd = d; near = o; }
        }
        if (near == null) return false;
        u.Target = near;
        u.LastHurt = Time;
        return true;
    }

    // ------------------------------------------------------------ the horde's envoy

    /// <summary>An envoy comes in from the west under a white rag and makes for the citadel.</summary>
    public void SendEnvoy()
    {
        var u = SpawnRaider(Kinds.Goblin);
        if (u == null) return;
        u.Envoy = true; u.Band = null; u.Raid = null; u.Torch = false;
        Offer = new Deal { Envoy = u, Until = double.MaxValue };
        Say("С запада идёт посол орды под белой тряпкой. Не трогайте его, если хотите выслушать.", 2, u);
    }

    /// <summary>The envoy on its way, waiting at the walls, or gone: the offer made when it is near enough to be heard.</summary>
    bool EnvoyTick(Unit u, float dt)
    {
        if (!u.Envoy) return false;
        u.Target = null;
        // the answer given, or no answer: back the way it came
        if (Offer == null || Offer.Envoy != u)
        {
            if (Offer?.Envoy == u) Offer = null;
            Retreat(u, dt);
            return true;
        }
        if (Offer.Kind != DealKind.None) { u.State = UnitState.Idle; u.Path = null; return true; }
        float dk = MathF.Sqrt((u.X - Site.KeepX) * (u.X - Site.KeepX) + (u.Z - Site.KeepZ) * (u.Z - Site.KeepZ));
        bool near = dk < Site.KeepR + 30 * VoxelWorld.PerMetre || Nearest(u, Team.Player, 10 * VoxelWorld.PerMetre) != null;
        if (near) { MakeOffer(u); return true; }
        if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
        {
            u.NextSearch = Time + 3;
            int c = NearestStand(Site.KeepX - Site.KeepR - 200, Site.KeepZ, u.T.Move.Height);
            if (c < 0 || !GoTo(u, c, adjacent: true, budget: 80000)) { MakeOffer(u); return true; }
        }
        Walk(u, dt);
        return true;
    }

    /// <summary>What the envoy offers: one of the bargains the next wave allows.</summary>
    void MakeOffer(Unit u)
    {
        var kinds = new List<DealKind> { DealKind.Delay, DealKind.Tribute };
        if (Bands.Exists(b => b.State == BandState.Coming && b.Roster.Exists(Kinds.IsEngine))) kinds.Add(DealKind.NoEngines);
        // its own people in our hands: it would buy them back, and asks about that first and most often
        if (Prisoners() > 0) { kinds.Add(DealKind.Ransom); kinds.Add(DealKind.Ransom); }
        var kind = kinds[_fate.Next(kinds.Count)];
        int n = Waves.Number + 1;
        var d = Offer ?? new Deal { Envoy = u };
        d.Kind = kind; d.Envoy = u; d.Until = Time + 90;
        switch (kind)
        {
            case DealKind.Delay: d.Store = 0; d.Cost = 50 + 10 * n; break;
            case DealKind.Tribute:
                d.Store = Stock[Stockpile.Wood] >= Stock[Stockpile.Stone] ? Stockpile.Wood : Stockpile.Stone;
                d.Cost = (4 + n / 2) * VoxelWorld.PerM3;
                break;
            case DealKind.Ransom: d.Store = 0; d.Cost = RansomFor(); break;
            default: d.Store = 0; d.Cost = 90 + 12 * n; break;
        }
        Offer = d;
        u.Path = null; u.State = UnitState.Idle;
        Say("Посол орды говорит: " + OfferText(d), 2, u);
    }

    /// <summary>"Дайте 70 золота — следующая волна придёт на три минуты позже."</summary>
    public static string OfferText(Deal d) => d.Kind switch
    {
        DealKind.Delay => $"«Дайте {d.Cost:0} золота — следующая волна придёт на три минуты позже».",
        DealKind.Tribute => $"«Отдайте {Fmt.M3(d.Cost)} м³ {(d.Store == Stockpile.Wood ? "брёвен" : d.Store == Stockpile.Stone ? "камня" : "земли")} — следующая волна будет на четверть меньше».",
        DealKind.NoEngines => $"«Дайте {d.Cost:0} золота — следующая волна придёт без осадных машин».",
        DealKind.Ransom => $"«Отпустите наших, что у вас в плену, — дадим за них {d.Cost:0} золота».",
        _ => "",
    };

    /// <summary>Whether the stores can pay what the envoy asks.</summary>
    public bool CanPay(Deal d) => d.Kind == DealKind.Ransom ? Prisoners() > 0 : d.Store == 0 ? Stock.Gold >= d.Cost : Stock[d.Store] >= d.Cost;

    /// <summary>The player's answer to the envoy: yes (if the stores can pay), or no. The envoy goes either way.</summary>
    public bool Answer(bool yes)
    {
        var d = Offer;
        if (d == null || d.Kind == DealKind.None) return false;
        Offer = null;
        if (!yes) { Say("Послу отказали. Он уходит.", 0, d.Envoy); return true; }
        if (!CanPay(d)) { Say(d.Kind == DealKind.Ransom ? "Пленных уже нет — посол уходит ни с чем." : "Заплатить нечем — посол уходит ни с чем.", 3, d.Envoy); return false; }
        if (d.Kind == DealKind.Ransom)
        {
            // what it pays now, for those still held
            double gold = RansomFor();
            int n = Prisoners();
            Ransom();
            Stock.Gold += gold;
            Say($"Сделка: пленные ({n}) отпущены к орде, за них {gold:0} золота.", 1, d.Envoy);
            return true;
        }
        if (d.Store == 0) Stock.Gold -= d.Cost; else Stock[d.Store] -= d.Cost;
        switch (d.Kind)
        {
            case DealKind.Delay:
                Waves.Countdown += 180;
                Say("Сделка: следующая волна придёт на три минуты позже.", 1, d.Envoy);
                break;
            case DealKind.Tribute:
                _waveShare = .75;
                ThinNextWave();
                Say("Сделка: следующая волна будет на четверть меньше.", 1, d.Envoy);
                break;
            case DealKind.NoEngines:
                _waveNoEngines = true;
                ThinNextWave();
                Say("Сделка: следующая волна придёт без осадных машин.", 1, d.Envoy);
                break;
        }
        return true;
    }

    /// <summary>The offer waits so long; the envoy cut down meanwhile angers the horde.</summary>
    void WatchOffer()
    {
        var d = Offer;
        // the horde is coming: no more talk
        if (Waves.Active && d.Envoy is { Dead: false }) { Offer = null; return; }
        if (d.Envoy == null || d.Envoy.Dead)
        {
            Offer = null;
            if (d.Envoy?.Dead == true && d.Envoy.DiedAt > 0) EnvoyKilled();
            return;
        }
        if (d.Kind != DealKind.None && Time >= d.Until) { Offer = null; Say("Посол не дождался ответа и ушёл.", 0, d.Envoy); }
    }

    /// <summary>An envoy struck down: the horde will come a fifth the stronger.</summary>
    void EnvoyKilled()
    {
        Angered = true;
        Say("Посла орды убили. Орда этого не забудет: следующая волна будет сильнее на пятую часть.", 3);
        AngerNextWave();
    }

    /// <summary>A bargain taken from the next wave: a share of its raiders, and its engines, struck off the rosters.</summary>
    void ThinNextWave()
    {
        foreach (var b in Bands)
        {
            if (b.State != BandState.Coming) continue;
            if (_waveNoEngines) b.Roster.RemoveAll(Kinds.IsEngine);
            int drop = (int)Math.Round(b.Roster.Count * (1 - _waveShare));
            // the weakest go first; the leader stays
            for (int i = 0; i < drop && b.Roster.Count > 1; i++) b.Roster.RemoveAt(b.Roster.Count - 1);
        }
        _waveShare = 1; _waveNoEngines = false;
        _nextTrails = 0;
    }

    /// <summary>The horde angered: a fifth more raiders in the next wave, goblins and orcs.</summary>
    void AngerNextWave()
    {
        foreach (var b in Bands)
        {
            if (b.State != BandState.Coming) continue;
            int more = Math.Max(1, b.Roster.Count / 5);
            for (int i = 0; i < more; i++) b.Roster.Add(i % 2 == 0 ? Kinds.Orc : Kinds.Goblin);
        }
        _nextTrails = 0;
    }
}
