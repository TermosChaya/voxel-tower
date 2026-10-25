using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>The kinds of way the path tool lays.</summary>
public enum WayKind : byte { Dirt, Gravel, Cobble, Flags, Boards }

/// <summary>
/// The settlement's ways. A way is laid with the path tool along a line on the ground, as wide as the brush: the turf of
/// each column under it is cut away and the way's own stuff laid in its place, flush with the ground and following its
/// slope (rammed earth, gravel, cobbles of two shades, flagstones a stone to every three voxels, a boardwalk of planks),
/// the tufts and flowers on it cleared. People go faster along ways (<see cref="WayPace"/>) and keep to them when it
/// costs them little (<c>PathFinder.OnPath</c>). And the valley wears ways of its own: where ours
/// walk the turf again and again it is trodden bare (Mat.Trodden), along the routes they keep between the houses, the
/// citadel and their work; left unwalked a while, a trodden way grows over again. A way can be laid over a trodden one.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The material a way of a kind has at a column: stones in a pattern for the cobbles and the flags.</summary>
    public static byte WayMat(WayKind kind, int x, int z) => kind switch
    {
        WayKind.Dirt => Mat.DirtPath,
        WayKind.Gravel => Mat.Gravel,
        WayKind.Cobble => Hash.H3(x, 17, z) < .55 ? Mat.Cobble : Mat.Flag,
        // flagstones three voxels a side, laid in courses that are each shifted half a stone
        WayKind.Flags => (x + ((z / 3) & 1) * 2) % 3 == 0 || z % 3 == 0 ? Mat.Cobble : Mat.Flag,
        _ => Mat.Boardwalk,
    };

    /// <summary>The name of a kind of way, as the tool's panel shows it.</summary>
    public static string WayName(WayKind kind) => kind switch
    {
        WayKind.Dirt => "Тропа", WayKind.Gravel => "Гравий", WayKind.Cobble => "Булыжник", WayKind.Flags => "Плиты", _ => "Мостки",
    };

    /// <summary>
    /// The top voxel of every column within half a width of the line between two points, each with the way's stuff it is
    /// to be: nothing where the top is ours already (a building), water, or not to be touched.
    /// </summary>
    public static VoxelList WayPlan(VoxelWorld w, Site site, int x0, int z0, int x1, int z1, int width, WayKind kind)
    {
        var L = new VoxelList();
        float half = Math.Max(.5f, width / 2f);
        int lx = Math.Min(x0, x1) - (int)half - 1, hx = Math.Max(x0, x1) + (int)half + 1, lz = Math.Min(z0, z1) - (int)half - 1, hz = Math.Max(z0, z1) + (int)half + 1;
        float dx = x1 - x0, dz = z1 - z0, len2 = Math.Max(1e-4f, dx * dx + dz * dz);
        for (int z = lz; z <= hz; z++)
            for (int x = lx; x <= hx; x++)
            {
                if (!w.InBounds(x, 1, z) || site != null && site.Forbidden(x, z)) continue;
                float t = Math.Clamp(((x - x0) * dx + (z - z0) * dz) / len2, 0, 1);
                float ex = x0 + dx * t - x, ez = z0 + dz * t - z;
                if (ex * ex + ez * ez > half * half) continue;
                int top = w.SurfAt(x, z) - 1;
                if (top < 1) continue;
                byte m = w.Get(x, top, z);
                if (!Paveable(m) || w.Get(x, top + 1, z) == Mat.Water) continue;
                L.Add(x, top, z, WayMat(kind, x, z));
            }
        return L;
    }

    /// <summary>What a way may be laid over: the valley's turf and earth, a trodden way, or a way of another kind.</summary>
    public static bool Paveable(byte m) => Mat.IsPavement[m] || Mat.IsGrass(m) || m is Mat.Soil or Mat.Scorch or Mat.Path or Mat.Moss;

    /// <summary>
    /// Lays a way: each top voxel that is not yet what it is to be is marked to be taken up, and its new stuff to be laid
    /// in its place once it is (as the magic wand lays a building over again); the plants on it cleared at once. Returns
    /// how many voxels were marked.
    /// </summary>
    public int Pave(VoxelList plan)
    {
        if (plan == null) return 0;
        var take = new VoxelList();
        for (int i = 0; i < plan.Count; i++)
        {
            int x = plan.X(i), y = plan.Y(i), z = plan.Z(i);
            byte m = World.Get(x, y, z), want = plan.M(i);
            if (m == want || !Paveable(m)) continue;
            take.Add(x, y, z, m);
            for (int yy = y + 1; yy < y + 4 && World.InBounds(x, yy, z); yy++)
            {
                byte above = World.Get(x, yy, z);
                if (above is Mat.Tuft or Mat.Flower or Mat.Bloom) World.Set(x, yy, z, Mat.Air);
                else break;
            }
        }
        int n = DesignateRemove(take);
        for (int i = 0; i < plan.Count; i++)
        {
            int x = plan.X(i), y = plan.Y(i), z = plan.Z(i);
            if (Orders.IsRemove(Orders.At(x, y, z)) || InstantLabour && Brushes.Free(World.Get(x, y, z))) Relays[WaterPlanner.Pack(x, y, z)] = plan.M(i);
        }
        if (InstantLabour) TickRelay(1);
        return n;
    }

    /// <summary>How much faster one goes along a way of each stuff: a trodden one a little, cobbles and flags most.</summary>
    static float PaceOn(byte m) => m switch
    {
        Mat.Trodden or Mat.DirtPath => 1.12f,
        Mat.Gravel => 1.18f,
        Mat.Boardwalk => 1.22f,
        Mat.Cobble or Mat.Flag => 1.28f,
        _ => 1,
    };

    /// <summary>The pace a walker keeps for the ground under its feet: faster along a way.</summary>
    float WayPace(Unit u)
    {
        int x = (int)u.X, z = (int)u.Z, y = (int)MathF.Floor(u.Y + .01f) - 1;
        if (!World.InBounds(x, y, z)) return 1;
        byte m = World.Get(x, y, z);
        return Mat.IsPavement[m] ? PaceOn(m) : 1;
    }

    // ------------------------------------------------------------ the ways the valley wears by itself

    /// <summary>How worn each column of turf is, by the voxels walked over it (a pass across a column adds about one).</summary>
    readonly Dictionary<int, float> _wear = new();
    /// <summary>The columns trodden bare by walking, which grow over again when they are left.</summary>
    readonly HashSet<int> _worn = new();
    float _wearDue;

    /// <summary>The passes over a column of turf that tread it bare; how many a trodden way holds before it grows over again.</summary>
    public const float WearToTread = 24, WearMost = 60;
    /// <summary>How much of its wear a column loses a second when nobody walks on it: a trodden way left an hour grows over.</summary>
    public float WearFades = .02f;

    /// <summary>One of ours walked so many voxels: the turf under its feet, and either side of them a little, is worn.</summary>
    void Tread(Unit u, float step)
    {
        if (u.Team != Team.Player || u.Inside || u.Airborne != null || step <= 0) return;
        int x = (int)u.X, z = (int)u.Z, y = (int)MathF.Floor(u.Y + .01f) - 1;
        if (!World.InBounds(x, y, z)) return;
        byte m = World.Get(x, y, z);
        if (!Mat.IsGrass(m) && m != Mat.Trodden) return;
        Wear(x, y, z, step);
        Wear(x + 1, y, z, step * .5f); Wear(x - 1, y, z, step * .5f);
        Wear(x, y, z + 1, step * .5f); Wear(x, y, z - 1, step * .5f);
    }

    void Wear(int x, int y, int z, float by)
    {
        if (!World.InBounds(x, y, z)) return;
        int key = z * World.SX + x;
        float was = _wear.GetValueOrDefault(key), now = Math.Min(WearMost, was + by);
        _wear[key] = now;
        if (now < WearToTread || was >= WearToTread && _worn.Contains(key)) return;
        // the top of the column, as it is: turf trodden to bare earth, the tufts and flowers on it gone
        int top = World.SurfAt(x, z) - 1;
        if (top < 1 || Math.Abs(top - y) > 2 || !Mat.IsGrass(World.Get(x, top, z)) || MeantAt(x, top, z) != 0 || Orders.At(x, top, z) != 0) return;
        World.Set(x, top, z, Mat.Trodden);
        for (int yy = top + 1; yy < top + 4 && World.InBounds(x, yy, z); yy++)
        {
            byte above = World.Get(x, yy, z);
            if (above is Mat.Tuft or Mat.Flower or Mat.Bloom) World.Set(x, yy, z, Mat.Air);
            else break;
        }
        _worn.Add(key);
    }

    /// <summary>Every couple of seconds: the wear fades where nobody walks, and a trodden way left long enough grows over.</summary>
    void TickWays(float dt)
    {
        _wearDue -= dt;
        if (_wearDue > 0 || _wear.Count == 0) return;
        float fade = WearFades * (2 - _wearDue);
        _wearDue = 2;
        List<int> gone = null;
        foreach (var key in new List<int>(_wear.Keys))
        {
            float v = _wear[key] - fade;
            if (v > 0) { _wear[key] = v; continue; }
            (gone ??= new List<int>()).Add(key);
        }
        if (gone == null) return;
        foreach (int key in gone)
        {
            _wear.Remove(key);
            if (!_worn.Remove(key)) continue;
            int x = key % World.SX, z = key / World.SX, top = World.SurfAt(x, z) - 1;
            if (top >= 1 && World.Get(x, top, z) == Mat.Trodden) World.Set(x, top, z, Mat.Grass);
        }
    }

    /// <summary>How many columns of turf are trodden bare by walking now (for the tests).</summary>
    public int TroddenCount => _worn.Count;
}
