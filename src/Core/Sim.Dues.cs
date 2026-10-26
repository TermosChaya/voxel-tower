using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What about a house makes it a good place to live: water near, a fire, somewhere to sit or something growing, a paved way at the door.</summary>
[Flags]
public enum Comfort : byte { None = 0, Water = 1, Fire = 2, Rest = 4, Paving = 8 }

/// <summary>
/// Dues. Once a town hall works (Sim.Town.cs), every house lived in pays it a little gold a minute for each bunk in it,
/// and half as much again for each comfort it has about it: a well near it, a fire burning near it (a campfire, a
/// brazier, a torch on a pole), somewhere to sit or something growing (a bench, a flower bed), and a paved way at its
/// door (Sim.Yard.cs, Sim.Paths.cs). So the houses drawn and furnished, and what is set out among them, pay toward the
/// soldiers they shelter, and a settlement well kept pays better than a camp of bare huts. Counted again every couple
/// of seconds; nothing kept in the save.
/// </summary>
public sealed partial class Sim
{
    /// <summary>A bunk's dues a minute, and the share more each comfort about its house adds.</summary>
    public const float DuesPerBunk = .1f, ComfortShare = .5f;
    /// <summary>How much more the houses pay a Capitol (the town hall's third storey).</summary>
    public const float CapitolDues = 1.5f;
    /// <summary>How near a thing set out must stand to a house's walls to count for it, in voxels (12 m); and how near its paving.</summary>
    const int ComfortReach = 96, PavingReach = 24;

    float _dues;
    readonly Dictionary<int, Comfort> _comfort = new();

    /// <summary>The dues the houses pay a minute now (none without a town hall at work).</summary>
    public float Dues() => _dues;

    /// <summary>A house is lived in: closed, with whole bunks in it, and not a town building.</summary>
    public bool LivedIn(Zone z) => z.Kind == Zone.Dwelling && z.Closed && z.Beds > 0 && BuildingOn(z) == null;

    /// <summary>The comforts a house has about it, as last counted.</summary>
    public Comfort ComfortOf(Zone z) => _comfort.TryGetValue(z.Id, out var c) ? c : Comfort.None;

    /// <summary>What a house pays a minute: its bunks' dues, and a share more for each comfort; nothing without a working hall.</summary>
    public float DuesOf(Zone z)
    {
        if (!LivedIn(z) || !Stands("hall")) return 0;
        int n = 0;
        for (var c = ComfortOf(z); c != 0; c &= c - 1) n++;
        // (the Capitol, the hall's third storey, takes half as much again)
        return z.Beds * DuesPerBunk * (1 + ComfortShare * n) * (Stands("hall", 3) ? CapitolDues : 1);
    }

    /// <summary>The houses' comforts and dues counted again.</summary>
    void CountDues()
    {
        _comfort.Clear();
        _dues = 0;
        foreach (var z in Zones)
        {
            if (!LivedIn(z)) continue;
            _comfort[z.Id] = Comforts(z);
            _dues += DuesOf(z);
        }
    }

    /// <summary>What there is about a house: the things set out standing near its walls, and paving laid by them.</summary>
    Comfort Comforts(Zone z)
    {
        var c = Comfort.None;
        foreach (var y in Yards)
        {
            if (!y.Ready) continue;
            int dx = Math.Max(0, Math.Max(z.X0 - y.X, y.X - z.X1)), dz = Math.Max(0, Math.Max(z.Z0 - y.Z, y.Z - z.Z1));
            if (dx * dx + dz * dz > ComfortReach * ComfortReach) continue;
            c |= y.Kind switch
            {
                DecorKind.Well => Comfort.Water,
                DecorKind.Campfire or DecorKind.Brazier or DecorKind.Torch => Comfort.Fire,
                DecorKind.Bench or DecorKind.FlowerBed => Comfort.Rest,
                _ => Comfort.None,
            };
        }
        // paving laid by the player (not a way only trodden) within a few metres of the walls, a good few voxels of it
        int laid = 0;
        for (int x = z.X0 - PavingReach; x <= z.X1 + PavingReach && laid < 24; x += 2)
            for (int zz = z.Z0 - PavingReach; zz <= z.Z1 + PavingReach && laid < 24; zz += 2)
            {
                if (x >= z.X0 && x <= z.X1 && zz >= z.Z0 && zz <= z.Z1 || !World.InBounds(x, 1, zz)) continue;
                byte m = World.Get(x, World.SurfAt(x, zz) - 1, zz);
                if (Mat.IsPavement[m] && m != Mat.Trodden) laid++;
            }
        if (laid >= 24) c |= Comfort.Paving;
        return c;
    }

    /// <summary>How the dues go, for the player.</summary>
    public const string DuesTip = "Подать\nПока работает ратуша, каждый жилой дом платит 0,1 золота в минуту за лежанку. Каждое удобство рядом прибавляет половину: колодец; костёр, жаровня или факел; скамья или клумба; мощёная дорожка у дома. Наведите на дом, чтобы увидеть его подать.";

    /// <summary>A comfort's name for the player.</summary>
    public static string ComfortName(Comfort c) => c switch
    {
        Comfort.Water => "колодец",
        Comfort.Fire => "огонь",
        Comfort.Rest => "скамья или клумба",
        _ => "мощёная дорожка",
    };

    /// <summary>A house's dues for the player: what it pays, what it has about it and what would make it pay more.</summary>
    public string DuesLine(Zone z)
    {
        if (!LivedIn(z)) return "";
        if (!Stands("hall")) return "подать платят ратуше: поставьте её";
        var have = new List<string>();
        var want = new List<string>();
        foreach (var c in new[] { Comfort.Water, Comfort.Fire, Comfort.Rest, Comfort.Paving }) (ComfortOf(z).HasFlag(c) ? have : want).Add(ComfortName(c));
        return $"подать {DuesOf(z):0.#} зол./мин" + (have.Count > 0 ? $", рядом: {string.Join(", ", have)}" : "") + (want.Count > 0 ? $"; больше даст: {string.Join(", ", want)}" : "");
    }
}
