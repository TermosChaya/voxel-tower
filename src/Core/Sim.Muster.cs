using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>One taken on at the citadel and not out of its door yet: its kind, how long it has been made ready, and what was paid.</summary>
public sealed class Recruit
{
    public UnitType Kind;
    public float Done;
    public int Paid;
}

/// <summary>
/// The citadel's muster. A pawn is not had at a word: enlisting pays the gold at once and puts the recruit in the
/// citadel's line, and the first in the line is made ready — armed, drilled, its horse broken in — and walks out of the
/// door when its time is up, the next one after it. The household is only as many as it has places for: the citadel's
/// own, and a place for every bed in the dwellings built about it; a horse and its rider take two, a giant four. What
/// the household learns opens the kinds the citadel will take on at all.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The line at the citadel: the first is being made ready, the rest wait their turn.</summary>
    public readonly List<Recruit> Recruits = new();
    /// <summary>How long the line may grow.</summary>
    public const int RecruitsMax = 6;
    /// <summary>The places the citadel itself has, before any dwelling is built.</summary>
    public const int CitadelPlaces = 10;

    /// <summary>The places the household fills now: each of ours alive by its kind's room (the risen dead are not the household's).</summary>
    public int Household()
    {
        int n = 0;
        foreach (var u in Units)
            if (!u.Dead && u.Team == Team.Player && u.T != Kinds.Risen) n += u.T.Room;
        return n;
    }

    /// <summary>The places the household fills and the line will fill.</summary>
    public int Spoken()
    {
        int n = Household();
        foreach (var r in Recruits) n += r.Kind.Room;
        return n;
    }

    /// <summary>The places there are: the citadel's (more with barracks), and a place for each bed in a dwelling (two with bunks).</summary>
    public int Places => CitadelPlaces + (Knows("barracks") ? 4 : 0) + DwellingBeds() * (Knows("bunks") ? 2 : 1);

    /// <summary>The seconds the citadel takes to make one of this kind ready (less with a drill yard).</summary>
    public float TrainTime(UnitType t) => t.Train * (Knows("drillyard") ? .7f : 1);

    /// <summary>Whether the household has learnt what this kind waits on.</summary>
    public bool Unlocked(UnitType t) => t.Lore == "" || Knows(t.Lore);

    /// <summary>Why the citadel will not take one of this kind on now, or null if it will.</summary>
    public string CantEnlist(UnitType t)
    {
        if (Over) return "осада окончена";
        if (!Unlocked(t)) return $"сначала изучите «{LoreOf(t.Lore)?.Name ?? t.Lore}»";
        if (Recruits.Count >= RecruitsMax) return "очередь полна";
        int spoken = Spoken();
        if (spoken + t.Room > Places) return t.Room > 1 && spoken < Places
            ? $"нужно {t.Room} {PlacesWord(t.Room)}, свободно {Places - spoken}: постройте жилой дом"
            : $"мест нет: занято {spoken} из {Places}, постройте жилой дом";
        int cost = HireCost(t);
        if (Stock.Gold < cost) return $"нужно {cost} золота";
        return null;
    }

    static string PlacesWord(int n) => Plural(n, "место", "места", "мест");

    /// <summary>Takes one of this kind on: the gold paid now, and it stands in the line.</summary>
    public bool Enlist(UnitType t)
    {
        if (CantEnlist(t) != null) return false;
        int cost = HireCost(t);
        Stock.Gold -= cost;
        Recruits.Add(new Recruit { Kind = t, Paid = cost });
        Say(Recruits.Count == 1 ? $"В цитадели готовят: {t.Name.ToLowerInvariant()}." : $"В очередь цитадели: {t.Name.ToLowerInvariant()} ({Recruits.Count}-й).");
        return true;
    }

    /// <summary>Sends one in the line away, its gold given back whole.</summary>
    public bool Dismiss(int i)
    {
        if (i < 0 || i >= Recruits.Count) return false;
        var r = Recruits[i];
        Stock.Gold += r.Paid;
        Recruits.RemoveAt(i);
        Say($"{r.Kind.Name} отпущен из очереди, {r.Paid} золота вернулось.");
        return true;
    }

    /// <summary>How far the first in the line is made ready, nothing to one.</summary>
    public float RecruitShare => Recruits.Count == 0 ? 0 : Math.Clamp(Recruits[0].Done / TrainTime(Recruits[0].Kind), 0, 1);

    void TickMuster(float dt)
    {
        if (Recruits.Count == 0) return;
        var r = Recruits[0];
        r.Done = Math.Min(r.Done + dt, TrainTime(r.Kind));
        if (r.Done < TrainTime(r.Kind)) return;
        // ready: out it comes, unless the places were lost meanwhile (a dwelling burnt) — then it waits inside for one
        if (Household() + r.Kind.Room > Places) return;
        Recruits.RemoveAt(0);
        Arrive(r.Kind);
        Say($"{r.Kind.Name} готов и выходит из цитадели.", 1);
    }

    /// <summary>One of ours comes into the household in the citadel, and steps out of its door when the door is safe.</summary>
    Unit Arrive(UnitType t)
    {
        var u = Spawn(t, Gate);
        Outfit(u);
        Enter(u);
        u.Why = "в цитадели: собирается";
        return u;
    }

    /// <summary>What the household's lore gives a new soldier of its own: quilted coats.</summary>
    void Outfit(Unit u)
    {
        if (Soldier(u) && Knows("gambesons")) { u.MaxHp *= 1.2f; u.Hp = u.MaxHp; }
    }
}
