using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>
/// What the map knows of a valley it does not hold open: who keeps it (ours, or the horde's garrison, or the dead that
/// guard a cell nobody holds), how much its walls are worth to its keepers, what its town pays a minute, how far an
/// outpost raised by a party of ours has got, and what its workers have dug and cut while we were away, waiting for
/// its stores when we come. It is taken off the valley as we leave it (<see cref="Take"/>) and kept with the map.
/// </summary>
public sealed class ValleySummary
{
    /// <summary>Those who keep the valley: they fight a quick battle for it, and come back into it when we do.</summary>
    public readonly List<Traveller> Keep = new();
    /// <summary>The walls and towers about its stronghold, as a share added to its keepers' strength (0 to 1.5).</summary>
    public float Walls;
    /// <summary>Gold its town pays a minute (the town hall, the market, the houses' dues).</summary>
    public float TownGold;
    /// <summary>How far an outpost raised by a party of ours has got, 0 to 1 (an outpost of the valley's own counts done).</summary>
    public float Raised;
    /// <summary>Stone, logs and metal its workers got while we were away, cubic metres: into its stores when we come.</summary>
    public double Stone, Logs, Metal;
    /// <summary>It was taken off the valley itself, not guessed.</summary>
    public bool Known;

    /// <summary>How far round the stronghold its walls are counted (40 m), and the cubic metres of masonry that make a wall's worth of 1.</summary>
    public const float FortReach = 40 * VoxelWorld.PerMetre, WallMetres = 400;
    /// <summary>The mines' gold while nobody of ours stands at each: so much of what they pay with our men at them.</summary>
    public const float MineShare = .6f;

    public int Workers
    {
        get { int n = 0; foreach (var m in Keep) if (m.Kind == Kinds.Worker) n++; return n; }
    }

    /// <summary>
    /// What the valley is now, as we leave it: those of a side who keep it (ours, or the horde's), its walls by the
    /// masonry and timber built about its stronghold, its town's pay; what it had earned while we were away is in its
    /// stores by now.
    /// </summary>
    public void Take(Sim sim, Team team)
    {
        Keep.Clear();
        foreach (var u in sim.Units)
            if (!u.Dead && u.Team == team && !u.Bound && !u.Envoy && u.Captor == null) Keep.Add(Traveller.Of(u));
        Walls = WallsOf(sim);
        TownGold = team == Team.Player ? sim.TownIncome() : 0;
        Stone = Logs = Metal = 0;
        Known = true;
    }

    /// <summary>The worth of the walls about a valley's stronghold: masonry and timber standing over the bare ground within reach.</summary>
    public static float WallsOf(Sim sim)
    {
        var w = sim.World; var s = sim.Site;
        if (s.HTop == null) return 0;
        int r = (int)FortReach, n = 0;
        for (int z = Math.Max(0, s.KeepZ - r); z <= Math.Min(w.SZ - 1, s.KeepZ + r); z++)
            for (int x = Math.Max(0, s.KeepX - r); x <= Math.Min(w.SX - 1, s.KeepX + r); x++)
            {
                int dx = x - s.KeepX, dz = z - s.KeepZ;
                if (dx * dx + dz * dz > r * r) continue;
                int g = s.HTop[z * w.SX + x], top = w.ColTop(x, z);
                if (top <= g + 4) continue;
                for (int y = g; y <= top; y++)
                {
                    byte m = w.Get(x, y, z);
                    if (Mat.IsMasonry(m) || m == Mat.Wood) n++;
                }
            }
        return Math.Clamp(n / (float)VoxelWorld.PerM3 / WallMetres, 0, 1.5f);
    }

    /// <summary>The gold a minute the mines of a kind of valley pay with our men at each (the copper mine 3, the silver vein 6, the gold workings 10).</summary>
    public static float MineGold(ValleyKind k)
    {
        var p = Valleys.PlacesOf(k, classic: false);
        return (p & Places.Mines) != 0 ? 3 + 6 + 10 : (p & Places.Silver) != 0 ? 6 : 0;
    }

    /// <summary>What a worker of a valley of this kind gets a day while we are away, cubic metres: metal from ore, stone from the mountains, logs from the woods.</summary>
    public static (double stone, double logs, double metal) Yield(ValleyKind k) => k switch
    {
        ValleyKind.Ore => (0, 0, .25),
        ValleyKind.Mountain => (.5, 0, .15),
        ValleyKind.Forest => (0, .6, 0),
        ValleyKind.Marsh => (0, .3, 0),
        _ => (0, 0, 0),
    };

    /// <summary>How many workers of a valley count for what it gets a day.</summary>
    public const int YieldHands = 8;

    internal void Write(BinaryWriter w)
    {
        w.Write(Keep.Count);
        foreach (var m in Keep) m.Write(w);
        w.Write(Walls); w.Write(TownGold); w.Write(Raised); w.Write(Stone); w.Write(Logs); w.Write(Metal); w.Write(Known);
    }

    internal void Read(BinaryReader r)
    {
        Keep.Clear();
        for (int n = r.ReadInt32(); n > 0; n--) if (Traveller.Read(r) is { } m) Keep.Add(m);
        Walls = r.ReadSingle(); TownGold = r.ReadSingle(); Raised = r.ReadSingle();
        Stone = r.ReadDouble(); Logs = r.ReadDouble(); Metal = r.ReadDouble(); Known = r.ReadBoolean();
    }
}

public sealed partial class MapCell
{
    /// <summary>What the map knows of the valley while it is not open (ValleySummary.cs).</summary>
    public readonly ValleySummary Sum = new();
}
