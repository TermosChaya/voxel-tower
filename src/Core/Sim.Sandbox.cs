using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The sandbox, for trying the whole game at once (the title's «Песочница», or --sandbox): every lore learnt and every
/// spell known, the stores and the intervention full and kept full, spells free and ready again at once, room for a
/// hundred more in the army, and a citadel nothing breaks. The horde comes only
/// when called, wave after wave past the twelfth, and bands of it can be called up anywhere in the valley to try things
/// on. No errands, chapters or gifts: it is no siege of one's own. A siege of one's own can have the horde on call too
/// (the sandbox «своими силами»): only the clock stands still in it.
/// </summary>
public sealed partial class Sim
{
    public bool Sandbox;
    /// <summary>
    /// Whether the frame unfolds in this siege: its main tools and tabs at the start, the rest let out as they are
    /// wanted; and what has been let out so far, by the frame's keys ("tool:Bridge", "tab:lore"). Kept in a save; the
    /// frame (Game/Ui/Unfold.cs) reads and writes them.
    /// </summary>
    public bool Unfolds;
    public readonly HashSet<string> LetOut = new();

    public void BeginSandbox()
    {
        Sandbox = true;
        SetSource(new OnCallRaids());
        NoErrands = true;
        Errands.Clear();
        foreach (var l in Lores)
        {
            if (l.Spell is { } k) Known.Add(k);
            else Learnt.Add(l.Key);
        }
        // and all the buildings give, and every kind the citadel will take on at once (Sim.Town.cs)
        foreach (var g in TownGrants()) Learnt.Add(g);
        Fill();
    }

    /// <summary>The stores and the intervention topped up, so that nothing tried runs short.</summary>
    void Fill()
    {
        Stock.Gold = Math.Max(Stock.Gold, 5000);
        foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth, Stockpile.Metal }) Stock[k] = Math.Max(Stock[k], 1000.0 * VoxelWorld.PerM3);
        Favour = Math.Max(Favour, FavourCap);
        Revelations = Math.Max(Revelations, 20);
        Knowledge = Math.Max(Knowledge, 50);
    }

    /// <summary>A band of the horde called up at a place of the valley: light (goblins and orcs) or heavy (trolls, a shaman and a hill giant).</summary>
    /// <summary>
    /// The sandbox: an army of the horde's realm walks in by the side of the land nearest a spot, as one will from the
    /// world map, a warlord of the second or third level at its head with his guard, of a kind as the army makes likely
    /// (Sim.Warlords.cs). The warlord, or null.
    /// </summary>
    public Warlord SummonArmy(int x, int z)
    {
        int edge = 0; float best = float.MaxValue;
        for (int e = 0; e < 6; e++)
        {
            var (ex, ez) = EdgePoint(e, .5);
            float d = (ex - x) * (ex - x) + (ez - z) * (ez - z);
            if (d < best) { best = d; edge = e; }
        }
        var roster = new List<UnitType> { Kinds.Orc, Kinds.Orc, Kinds.Orc, Kinds.Orc, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Sapper, Kinds.Sapper, Kinds.Troll, Kinds.Shaman };
        var lord = WarlordFor(3 + Rng.Next(6), roster);
        Raid(roster, edge, .5, lord);
        return lord;
    }

    public Warband Summon(int x, int z, bool heavy)
    {
        // room to stand for the tallest of the band
        int at = NearestStand(x, z, (heavy ? Kinds.Troll : Kinds.Orc).Move.Height);
        if (at < 0) return null;
        var b = new Warband { Id = _nextBand++, State = BandState.Coming, Entry = at, Name = heavy ? "Тяжёлая ватага" : "Ватага" };
        b.Roster.AddRange(heavy
            ? new[] { Kinds.Troll, Kinds.Orc, Kinds.Orc, Kinds.Troll, Kinds.Orc, Kinds.Shaman, Kinds.Alchemist, Kinds.HillGiant }
            : new[] { Kinds.Orc, Kinds.Goblin, Kinds.Goblin, Kinds.Orc, Kinds.Goblin, Kinds.Goblin, Kinds.Orc });
        Bands.Add(b);
        foreach (var t in b.Roster) SpawnRaider(t, b);
        return b;
    }
}
