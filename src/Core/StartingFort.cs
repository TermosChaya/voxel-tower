using System;

namespace VoxelTower.Core;

/// <summary>
/// What stands round the citadel when a game begins: a stone curtain half built, a working gate in its west side where
/// the horde comes from, ladders up onto it from the yard, a corner tower finished and another begun. What is left of it
/// is marked out for the workers: the north wall to raise, a breach in the south wall, battlements, the second tower, and
/// a timber palisade along the east side that the stores already cover. A first job, and a sample of what can be built.
/// </summary>
public static class StartingFort
{
    /// <summary>The curtain's corners round the citadel, its thickness and height, in voxels.</summary>
    const int West = 84, East = 36, North = 60, South = 60, Thick = 6, High = 28;

    /// <summary>The gate it was raised with.</summary>
    public static Gateway Raise(Sim sim)
    {
        var w = sim.World; var s = sim.Site;
        int kx = s.KeepX, kz = s.KeepZ, top = s.KeepBase + High;
        int x0 = kx - West, x1 = kx + East, z0 = kz - North, z1 = kz + South;
        var stone = new Stroke { Mat = Mat.Stone, Width = Thick, Height = High, Level = true };
        bool instant = sim.InstantLabour;

        void Build(VoxelList L) { sim.InstantLabour = true; sim.DesignateBuild(L); sim.InstantLabour = instant; }
        void Mark(VoxelList L) => sim.DesignateBuild(L);
        VoxelList Wall(int ax, int az, int bx, int bz, int to, byte mat = Mat.Stone, int width = Thick) =>
            Shapes.Line(w, s, ax, az, bx, bz, new Stroke { Mat = mat, Width = width, Height = High, Level = true }, to);

        // what stands: the west curtain, the west half of the north one, most of the south one, a low course elsewhere
        Build(Wall(x0, z0, x0, z1, top));
        Build(Wall(x0, z0, kx - 24, z0, top));
        Build(Wall(kx - 24, z0, x1, z0, s.KeepBase + 8));
        Build(Wall(x0, z1, kx - 16, z1, top));
        Build(Wall(kx + 12, z1, x1, z1, s.KeepBase + 12));
        // the north-west tower, finished: a stone shell round an earth core, a parapet over its platform
        Build(Shapes.Circle(w, s, x0, z0, 11, new Stroke { Mat = Mat.Stone, Width = 3, Height = High + 12, Level = true, Hollow = true }, top + 12));
        Build(Shapes.Circle(w, s, x0, z0, 8.5, new Stroke { Mat = Mat.Rammed, Width = 1, Height = High + 8, Level = true }, top + 8));
        // the south-west one, begun
        Build(Shapes.Circle(w, s, x0, z1, 11, new Stroke { Mat = Mat.Stone, Width = 3, Height = 12, Level = true, Hollow = true }, s.KeepBase + 12));

        // the gate, west, in line with the citadel's door
        Gateway gate = null;
        var outer = Face(w, x0 - 40, kz, +1, top - 10);
        var plan = outer.HasValue ? GatePlan.At(w, s, outer.Value) : null;
        if (plan != null) gate = sim.AddGate(plan, built: true);
        // ladders up the inner face of the west curtain, either side of the gate
        foreach (int lz in new[] { kz - 34, kz + 34 })
        {
            var inner = Face(w, x0 + 30, lz, -1, top - 10);
            if (inner.HasValue) Build(Shapes.LadderAt(w, s, inner.Value));
        }

        // what is left to do
        Mark(Wall(kx - 24, z0, x1, z0, top));
        Mark(Wall(kx - 16, z1, kx + 12, z1, top));
        Mark(Wall(kx + 12, z1, x1, z1, top));
        Mark(Shapes.Circle(w, s, x0, z1, 11, new Stroke { Mat = Mat.Stone, Width = 3, Height = High + 12, Level = true, Hollow = true }, top + 12));
        for (int z = z0 + 16; z <= z1 - 16; z += 10)
            Mark(Shapes.Box(w, s, x0 - 3, z, x0 - 1, z + 4, new Stroke { Mat = Mat.Stone, Width = 1, Height = 6 }, 0));
        Mark(Wall(x1, z0, x1, z1, s.KeepBase + 22, Mat.Wood, 3));
        return gate;
    }

    /// <summary>The first blocking face met walking from a column along x, at a height: a side face to click, as a hit.</summary>
    static VoxelHit? Face(VoxelWorld w, int x, int z, int dir, int y)
    {
        for (int n = 0; n < 120; n++, x += dir)
        {
            byte m = w.Get(x, y, z);
            if (m != 0 && !Mat.Passable[m]) return new VoxelHit { X = x, Y = y, Z = z, NX = -dir, M = m };
        }
        return null;
    }
}
