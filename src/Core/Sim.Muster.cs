using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The citadel's muster. A pawn is had at a word: enlisting pays the gold, and the new one steps out of the citadel's
/// door at once (or as soon as the door is safe). The household is only as many as it has places for: the citadel's
/// own, and a place for every bed in the dwellings built about it; a horse and its rider take two, a giant four. What the
/// household learns opens the kinds the citadel will take on at all.
/// </summary>
public sealed partial class Sim
{
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

    /// <summary>The places there are: the citadel's (more with barracks), and a place for each bed in a dwelling (two with bunks).</summary>
    public int Places => CitadelPlaces + (Knows("barracks") ? 4 : 0) + DwellingBeds() * (Knows("bunks") ? 2 : 1) + (Sandbox ? 100 : 0);

    /// <summary>Whether the household has learnt what this kind waits on.</summary>
    public bool Unlocked(UnitType t) => t.Lore == "" || Knows(t.Lore);

    /// <summary>Why the citadel will not take one of this kind on now, or null if it will.</summary>
    public string CantEnlist(UnitType t)
    {
        if (Over) return "осада окончена";
        if (!Unlocked(t)) return $"сначала изучите «{LoreOf(t.Lore)?.Name ?? t.Lore}»";
        int have = Household();
        if (have + t.Room > Places) return t.Room > 1 && have < Places
            ? $"нужно {t.Room} {PlacesWord(t.Room)}, свободно {Places - have}: постройте жилой дом"
            : $"мест нет: занято {have} из {Places}, постройте жилой дом";
        int cost = HireCost(t);
        if (Stock.Gold < cost) return $"нужно {cost} золота";
        // an engine is built of the stores' timber besides
        if (t.Logs > 0 && Stock[Stockpile.Wood] + 1e-6 < t.Logs) return $"нужно {t.Logs:0.#} м³ брёвен";
        return null;
    }

    static string PlacesWord(int n) => Plural(n, "место", "места", "мест");

    /// <summary>Takes one of this kind on: the gold paid, and out of the citadel it comes.</summary>
    public bool Enlist(UnitType t)
    {
        if (CantEnlist(t) != null) return false;
        Stock.Gold -= HireCost(t);
        if (t.Logs > 0) Stock.Take(Stockpile.Wood, t.Logs);
        var u = Arrive(t);
        // an engine comes out with its crew (Sim.OurEngines.cs)
        if (t.Crew > 0)
        {
            for (int i = 0; i < t.Crew; i++) Arrive(Kinds.Crewman).Escort = u;
            Say($"{t.Name} собран и выходит из цитадели с расчётом.", 1);
            return true;
        }
        Say($"{t.Name} нанят и выходит из цитадели.", 1);
        return true;
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
