using System;

namespace VoxelTower.Core;

/// <summary>
/// Metal (the author, 25.10.2026: «Добавить новый вид материала — металл»). Iron ore lies in veins in the rock
/// (MapGen.Ore.cs) and comes up in outcrops; whatever breaks it out — the pick, a quarry, a mine set with the
/// делянка's metal, a tunnel dug through a vein — sends half of it to the store of metal, smelting implied. A mine
/// (<see cref="Stockpile.Metal"/> as a zone's kind, «Рудник») is an open pit down to the ore within its depth, its sides
/// a stair's pitch, and leaves the ground where no ore is near; one told to bring in metal finds ore showing or just
/// under the ground for itself. The
/// miners' building, «Рудокопня», stands only on an outcrop and brings in metal by itself, as the gold mines bring
/// gold. Metal is laid as iron plate, the strongest of what is built (Materials.cs, Sim.Strain.cs), hangs as iron
/// gates (Sim.Gates.cs), and the smithy, the workshop, the engines and the troops in plate cost some of it.
/// </summary>
public sealed partial class Sim
{
    /// <summary>Ore was struck once: the chronicle has said so.</summary>
    public bool OreStruck;
    /// <summary>Metal brought in so far, voxels: by hand and by the miners' building.</summary>
    public double MetalMined;

    /// <summary>
    /// How far down a column would be taken for its ore: through earth and rock to the lowest ore in it above the floor,
    /// or nothing where there is no ore (and nothing where water stands beside it, as for any zone). A mine's pit is
    /// shaped from these (<see cref="MineFloor"/>).
    /// </summary>
    bool OreSpan(int x, int z, int top, int floor, out int bottom)
    {
        bottom = top;
        int lowest = -1;
        for (int y = top - 1; y >= Math.Max(3, floor); y--)
        {
            byte m = World.Get(x, y, z);
            if (Mat.IsIronOre(m)) lowest = y;
            else if (!(m is Mat.Rock or Mat.Moss or Mat.Soil or Mat.Path or Mat.Scorch || Mat.IsGrass(m))) break;
        }
        if (lowest < 0) return false;
        bottom = lowest;
        return !WaterNear(x, z, bottom);
    }

    /// <summary>
    /// A mine's floor, column by column: an open pit down to the lowest ore within its depth where there is ore, its sides
    /// going up from there a voxel for every voxel across (a stair's pitch, walked down and up again) to the ground about
    /// it; where no ore is near, the floor stands at the ground and nothing is taken.
    /// </summary>
    int[] MineFloor(Zone z)
    {
        int W = z.W, D = z.D;
        var f = new float[W * D];
        var top = new int[W * D];
        for (int k = 0; k < D; k++)
            for (int i = 0; i < W; i++)
            {
                int x = z.X0 + i, zz = z.Z0 + k, t = World.SurfAt(x, zz), c = k * W + i;
                top[c] = t;
                f[c] = OreSpan(x, zz, t, Math.Max(3, t - z.Depth), out int bottom) ? bottom : float.MaxValue;
            }
        // the sides: each column's floor no lower than its neighbours' and a voxel more for each voxel away (two sweeps)
        const float Side = 1, Corner = 1.414f;
        for (int k = 0; k < D; k++)
            for (int i = 0; i < W; i++)
            {
                int c = k * W + i;
                if (i > 0) f[c] = Math.Min(f[c], f[c - 1] + Side);
                if (k == 0) continue;
                f[c] = Math.Min(f[c], f[c - W] + Side);
                if (i > 0) f[c] = Math.Min(f[c], f[c - W - 1] + Corner);
                if (i < W - 1) f[c] = Math.Min(f[c], f[c - W + 1] + Corner);
            }
        for (int k = D - 1; k >= 0; k--)
            for (int i = W - 1; i >= 0; i--)
            {
                int c = k * W + i;
                if (i < W - 1) f[c] = Math.Min(f[c], f[c + 1] + Side);
                if (k == D - 1) continue;
                f[c] = Math.Min(f[c], f[c + W] + Side);
                if (i < W - 1) f[c] = Math.Min(f[c], f[c + W + 1] + Corner);
                if (i > 0) f[c] = Math.Min(f[c], f[c + W - 1] + Corner);
            }
        var floor = new int[W * D];
        for (int c = 0; c < W * D; c++) floor[c] = f[c] >= top[c] ? top[c] : Math.Max(3, (int)MathF.Ceiling(f[c]));
        return floor;
    }

    /// <summary>A mine set for one told to bring in metal: round the nearest ore showing, or lying within two metres of the surface.</summary>
    Zone OreDutyZone(Unit u)
    {
        foreach (var z in Zones) if (z.Auto && z.Kind == Stockpile.Metal && Survey(z.X0, z.Z0, z.X1, z.Z1, Stockpile.Metal, z.Depth).voxels > 0) return z;
        int ux = (int)u.X, uz = (int)u.Z;
        for (int r = 0; r <= 560; r += 4)
            for (int dz = -r; dz <= r; dz += 4)
                for (int dx = -r; dx <= r; dx += 4)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    int x = ux + dx, z = uz + dz;
                    if (x < 8 || z < 8 || x >= World.SX - 8 || z >= World.SZ - 8 || Site.Forbidden(x, z) || !World.OnLand(x, z)) continue;
                    int top = World.SurfAt(x, z);
                    if (!OreSpan(x, z, top, top - 16, out _)) continue;
                    var zone = AddZone(x - 14, z - 14, x + 14, z + 14, Stockpile.Metal, 32);
                    zone.Auto = true;
                    return zone;
                }
        return null;
    }

    /// <summary>A voxel of ore broken out: metal counted, and the first time the chronicle says what it is for.</summary>
    void StruckOre(byte m, double metal)
    {
        MetalMined += metal;
        if (OreStruck) return;
        OreStruck = true;
        Say("Добыта железная руда, металл идёт в запасы. Из него кладут стены и ворота, он нужен кузнице, мастерской, машинам и латникам.", 1);
    }

    // ------------------------------------------------------------ the miners' building

    /// <summary>The outcrop a building of this kind round a column would stand on, or −1 (only for a kind that needs one).</summary>
    public int OutcropUnder(int x0, int z0, int x1, int z1)
    {
        for (int i = 0; i < Site.Outcrops.Count; i++)
        {
            var o = Site.Outcrops[i];
            // its middle under the house or within a couple of metres of its walls, and enough of it there still
            if (o.X < x0 - 16 || o.X > x1 + 16 || o.Z < z0 - 16 || o.Z > z1 + 16) continue;
            if (OreLeft(o.X, o.Z, o.R) < 40) continue;
            return i;
        }
        return -1;
    }

    /// <summary>Voxels of ore left about an outcrop's middle, sampled (dug away, it is no outcrop any more).</summary>
    public int OreLeft(int cx, int cz, int r)
    {
        int n = 0;
        for (int z = cz - r; z <= cz + r; z += 2)
            for (int x = cx - r; x <= cx + r; x += 2)
            {
                if (!World.InBounds(x, 1, z)) continue;
                int top = World.SurfAt(x, z);
                for (int y = top - 1; y >= Math.Max(3, top - 12); y--) if (Mat.IsIronOre(World.Get(x, y, z))) n++;
            }
        return n;
    }

    /// <summary>The metal the town brings in a minute now, cubic metres: the miners' buildings at their levels.</summary>
    public float TownMetal()
    {
        float m = 0;
        foreach (var b in Buildings)
        {
            if (!b.Ready) continue;
            var k = b.K;
            m += b.Level >= 2 ? k.Metal2 : k.Metal1;
        }
        return m;
    }

    /// <summary>The metal a kind of building or a storey of it asks, cubic metres, and whether the store has it.</summary>
    bool HasMetal(float m3) => Sandbox || Stock[Stockpile.Metal] + 1e-6 >= m3 * VoxelWorld.PerM3;

    void TakeMetal(float m3) { if (!Sandbox && m3 > 0) Stock.Take(Stockpile.Metal, m3 * VoxelWorld.PerM3); }

    /// <summary>The metal brought in by the town's buildings, a step of it.</summary>
    void TickMetal(float dt)
    {
        float m3 = TownMetal();
        if (m3 <= 0 || Over) return;
        double v = m3 * VoxelWorld.PerM3 / 60 * dt;
        Stock[Stockpile.Metal] += v;
        MetalMined += v;
    }
}
