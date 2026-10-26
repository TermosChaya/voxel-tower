using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Walking the land of the map (Atlas.cs) as the heroes of «Heroes» do: a party of ours led to a spot of it walks there
/// by the way that costs least — along the roads, round the crags and the water, over a river at a ford — at the pace
/// of the ground under it. The cells still keep the time: open ground a cell wide takes the hours a cell takes to cross
/// (<see cref="MapPath.Hours"/>), a road its share of them, and the party is in the cell its feet are in. The way is
/// found over a grid of the land a few voxels to a step (A*), then drawn straight wherever the straight line costs no
/// more.
/// </summary>
public static class AtlasWalk
{
    /// <summary>The cells a cell's middle is from its neighbour's, in voxels of the land (as far apart as two cells' middles).</summary>
    public static float Spacing(Atlas land)
    {
        if (land == _spacedLand) return _spacing;
        _spacedLand = land;
        return _spacing = Measure(land);
    }

    static Atlas _spacedLand;
    static float _spacing;

    static float Measure(Atlas land)
    {
        var c = land.Campaign;
        foreach (var a in c.Cells)
            for (int s = 0; s < 6; s++)
                if (c.Neighbour(a, s) is { } b)
                {
                    var (ax, az) = land.Center(a); var (bx, bz) = land.Center(b);
                    return MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
                }
        return 80;
    }

    // the land as the ways read it, a voxel at a time: whether it can be stood on and what it costs (made once a land)
    static Atlas _gridLand;
    static bool[] _open;
    static float[] _cost;
    static int _sx, _sz;

    static void Grid(Atlas land)
    {
        if (land == _gridLand) return;
        _sx = land.World.SX; _sz = land.World.SZ;
        _open = new bool[_sx * _sz]; _cost = new float[_sx * _sz];
        for (int z = 0; z < _sz; z++)
            for (int x = 0; x < _sx; x++)
            {
                bool o = land.Walkable(x, z);
                _open[z * _sx + x] = o;
                _cost[z * _sx + x] = o ? land.Cost(x, z) : float.PositiveInfinity;
            }
        _gridLand = land;
    }

    /// <summary>The land changed under the ways (a cell laid anew): read it again before the next way is found.</summary>
    public static void Forget() => _gridLand = null;

    static bool OpenAt(int x, int z) => x >= 0 && z >= 0 && x < _sx && z < _sz && _open[z * _sx + x];

    /// <summary>Seconds of the map's time a party takes over a voxel of the land of so much cost.</summary>
    public static double SecondsPer(Atlas land, MapParty p, float cost) =>
        cost * 8 * MapPath.HourSeconds * MapPath.Pace(p) / Spacing(land);

    /// <summary>
    /// The way over the land from one spot to another: the spots to walk through, the first where it stands and the last
    /// where it is going (the nearest it can stand on, if that is not); null if there is none.
    /// </summary>
    public static List<(float X, float Z)> Find(Atlas land, float x0, float z0, float x1, float z1)
    {
        Grid(land);
        float sp = Spacing(land);
        int g = Math.Max(2, (int)MathF.Round(sp / 24));
        int nx = land.World.SX / g + 1, nz = land.World.SZ / g + 1;
        bool Open(int i, int k) => i >= 0 && k >= 0 && i < nx && k < nz && OpenAt(i * g + g / 2, k * g + g / 2);
        float CostAt(int i, int k) => _cost[(k * g + g / 2) * _sx + i * g + g / 2];
        (int i, int k) Snap(float x, float z)
        {
            int ci = (int)(x / g), ck = (int)(z / g);
            if (Open(ci, ck)) return (ci, ck);
            for (int r = 1; r < 12; r++)
            {
                (int, int) best = (-1, -1); float bd = float.MaxValue;
                for (int dk = -r; dk <= r; dk++)
                    for (int di = -r; di <= r; di++)
                    {
                        if (Math.Max(Math.Abs(di), Math.Abs(dk)) != r || !Open(ci + di, ck + dk)) continue;
                        float d = di * di + dk * dk;
                        if (d < bd) { bd = d; best = (ci + di, ck + dk); }
                    }
                if (best.Item1 >= 0) return best;
            }
            return (-1, -1);
        }
        var (si, sk) = Snap(x0, z0);
        var (ti, tk) = Snap(x1, z1);
        if (si < 0 || ti < 0) return null;
        int start = sk * nx + si, goal = tk * nx + ti;
        var cost = new Dictionary<int, float> { [start] = 0 };
        var back = new Dictionary<int, int>();
        var open = new PriorityQueue<int, float>();
        var done = new HashSet<int>();
        // (the cheapest a voxel can be is a road's)
        float least = (float)MapPath.RoadShare * g;
        float H(int i, int k) => MathF.Sqrt((i - ti) * (i - ti) + (k - tk) * (k - tk)) * least;
        open.Enqueue(start, H(si, sk));
        while (open.TryDequeue(out int at, out _))
        {
            if (at == goal) break;
            if (!done.Add(at)) continue;
            int ai = at % nx, ak = at / nx;
            float ca = CostAt(ai, ak);
            for (int dk = -1; dk <= 1; dk++)
                for (int di = -1; di <= 1; di++)
                {
                    if (di == 0 && dk == 0) continue;
                    int bi = ai + di, bk = ak + dk;
                    if (!Open(bi, bk)) continue;
                    // a step aslant only where both its corners can be stood on: no cutting the corner of a crag
                    if (di != 0 && dk != 0 && (!Open(ai + di, ak) || !Open(ai, ak + dk))) continue;
                    int b = bk * nx + bi;
                    if (done.Contains(b)) continue;
                    // nothing that cannot be stood on between the two, a thin stream or the corner of a crag
                    if (float.IsInfinity(LineCost(land, (ai * g + g / 2f, ak * g + g / 2f), (bi * g + g / 2f, bk * g + g / 2f)))) continue;
                    float step = (di != 0 && dk != 0 ? 1.4142f : 1) * g * (ca + CostAt(bi, bk)) / 2;
                    float gb = cost[at] + step;
                    if (cost.TryGetValue(b, out float had) && had <= gb) continue;
                    cost[b] = gb; back[b] = at;
                    open.Enqueue(b, gb + H(bi, bk));
                }
        }
        if (start != goal && !back.ContainsKey(goal)) return null;
        var nodes = new List<(float X, float Z)>();
        for (int n = goal; ; n = back[n])
        {
            nodes.Add((n % nx * g + g / 2f, n / nx * g + g / 2f));
            if (n == start) break;
        }
        nodes.Reverse();
        // where it stands and where it goes, as they are, if they can be stood on and the step to the grid is clear
        if (land.Walkable((int)x0, (int)z0) && !float.IsInfinity(LineCost(land, (x0, z0), nodes[0]))) nodes.Insert(0, (x0, z0));
        if (land.Walkable((int)x1, (int)z1) && !float.IsInfinity(LineCost(land, nodes[^1], (x1, z1)))) nodes.Add((x1, z1));
        return Straighten(land, nodes);
    }

    /// <summary>The cost of walking straight from one spot to another over the land, voxel by voxel; infinite where it cannot be stood on.</summary>
    public static float LineCost(Atlas land, (float X, float Z) a, (float X, float Z) b)
    {
        Grid(land);
        float dx = b.X - a.X, dz = b.Z - a.Z, len = MathF.Sqrt(dx * dx + dz * dz);
        // (every half voxel, its ends too: no corner of a crag cut between two looks)
        int n = Math.Max(1, (int)MathF.Ceiling(len * 2));
        float sum = 0;
        for (int i = 0; i <= n; i++)
        {
            float t = (float)i / n;
            int x = (int)(a.X + dx * t), z = (int)(a.Z + dz * t);
            if (!OpenAt(x, z)) return float.PositiveInfinity;
            if (i < n) sum += _cost[z * _sx + x];
        }
        return sum * len / n;
    }

    /// <summary>The way drawn straight from each spot to the farthest one ahead the straight line costs no more to get to.</summary>
    static List<(float X, float Z)> Straighten(Atlas land, List<(float X, float Z)> nodes)
    {
        if (nodes.Count <= 2) return nodes;
        var way = new List<(float X, float Z)> { nodes[0] };
        int i = 0;
        while (i < nodes.Count - 1)
        {
            int best = i + 1;
            float along = 0;
            for (int j = i + 1; j < nodes.Count; j++)
            {
                along += LineCost(land, nodes[j - 1], nodes[j]);
                if (j - i > 40) break;
                if (j > i + 1 && LineCost(land, nodes[i], nodes[j]) <= along * 1.01f + .01f) best = j;
            }
            way.Add(nodes[best]);
            i = best;
        }
        return way;
    }

    /// <summary>How long a way over the land is, voxels.</summary>
    public static float Length(List<(float X, float Z)> way)
    {
        float len = 0;
        for (int i = 1; i < way.Count; i++) len += MathF.Sqrt((way[i].X - way[i - 1].X) * (way[i].X - way[i - 1].X) + (way[i].Z - way[i - 1].Z) * (way[i].Z - way[i - 1].Z));
        return len;
    }

    /// <summary>The cells a way over the land goes into, one after another, after the one it starts in.</summary>
    public static List<MapCell> CellsOf(Atlas land, List<(float X, float Z)> way)
    {
        var cells = new List<MapCell>();
        if (way == null || way.Count == 0) return cells;
        var at = land.CellAt(way[0].X, way[0].Z);
        for (int i = 1; i < way.Count; i++)
        {
            float dx = way[i].X - way[i - 1].X, dz = way[i].Z - way[i - 1].Z, len = MathF.Sqrt(dx * dx + dz * dz);
            int n = Math.Max(1, (int)MathF.Ceiling(len));
            for (int k = 1; k <= n; k++)
            {
                var c = land.CellAt(way[i - 1].X + dx * k / n, way[i - 1].Z + dz * k / n);
                if (c == null || c == at) continue;
                cells.Add(c);
                at = c;
            }
        }
        return cells;
    }
}

/// <summary>Where a party stands on the land of the map, and the way it walks over it.</summary>
public sealed partial class MapParty
{
    /// <summary>Where it stands on the map's land (x, z); NaN until it is put there.</summary>
    public float AtX = float.NaN, AtZ = float.NaN;
    /// <summary>The spots of the land it is still to walk through, the last where it stops; null standing (or walking from cell to cell, as the horde does).</summary>
    public List<(float X, float Z)> Trail;
    /// <summary>The way it faces, radians from the east toward the south (as the land's x and z).</summary>
    public float Facing;
    public bool Walking => Trail is { Count: > 0 };
    public bool Placed => !float.IsNaN(AtX);
    /// <summary>
    /// Ours sent into the valley we play in: they walk into it once they reach its cell. Else a party of ours come to
    /// that cell stands on the map by it, to be led in with «Войти в клетку».
    /// </summary>
    public bool WalkIn;
}

/// <summary>The heroes of ours walking the land of the map, and the first of them, who the campaign starts with.</summary>
public sealed partial class Campaign
{
    /// <summary>The land of the map, as the game has it made (Atlas.cs): the heroes walk it. Not kept with the map; made again from it.</summary>
    public Atlas Land { get; private set; }

    /// <summary>The land made for the map: every party on it is put where it stands.</summary>
    public void Attach(Atlas land)
    {
        Land = land;
        foreach (var p in Parties) if (!p.Placed) PlaceIn(p, CellOf(p));
    }

    /// <summary>
    /// The army the first hero sets out with: his swordsmen and his archers. The first valley keeps its workers, a
    /// commander and nothing else: its soldiers walk with him, as a town's do with its hero in «Heroes».
    /// </summary>
    public static readonly UnitType[] HeroArmy = { Kinds.Swordsman, Kinds.Swordsman, Kinds.Archer, Kinds.Archer };

    /// <summary>
    /// The first hero, on the map by the first valley's citadel with his army: a commander with a name of his own (Heroes.cs),
    /// holding the first valley's places as his men do. Returns his party.
    /// </summary>
    public MapParty AddHero()
    {
        var home = Home;
        var p = new MapParty { Id = _nextParty++, Q = home.Q, R = home.R, CameQ = home.Q, CameR = home.R, Owner = Owner.Ours, Party = new Party() };
        int key = KeyOf(home);
        var captain = new Traveller { Kind = Kinds.Captain, Look = (int)(Seed % 3), Hp = Kinds.Captain.Hp, MaxHp = Kinds.Captain.Hp, Billet = key };
        captain.Hero = Heroes.Fresh((int)(Seed * 7 + 11));
        p.Party.Members.Add(captain);
        int look = 0;
        foreach (var t in HeroArmy) p.Party.Members.Add(new Traveller { Kind = t, Look = look++ % Bodies.Looks(t), Hp = t.Hp, MaxHp = t.Hp, Billet = key });
        Parties.Add(p);
        if (Land != null) PlaceIn(p, home);
        // on the big map: a day's moves, and the fog lifted round him
        if (Big) { p.MovesMax = p.MovesLeft = MapWalk.MaxMoves(p, Week); Reveal(home, SightOf(p) + 2); }
        return p;
    }

    /// <summary>
    /// A party put on the land of its cell: by the side it came over (or by a side given), a third of the way in; else
    /// by the middle, a little to the east of what stands there.
    /// </summary>
    public void PlaceIn(MapParty p, MapCell cell, int side = -1)
    {
        if (Land == null || cell == null) return;
        var (cx, cz) = Land.Center(cell);
        if (side < 0 && (p.CameQ != p.Q || p.CameR != p.R) && At(p.CameQ, p.CameR) is { } came) side = MapPath.Side(cell, came);
        float sp = AtlasWalk.Spacing(Land);
        (float x, float z) want;
        if (side >= 0)
        {
            float a = MathF.PI / 180 * (-60 + 60 * side);
            want = (cx + MathF.Cos(a) * sp * .3f, cz + MathF.Sin(a) * sp * .3f);
            p.Facing = a + MathF.PI;
        }
        else want = (cx + sp * .2f, cz + sp * .06f);
        (p.AtX, p.AtZ) = StandNear(cell, want.x, want.z);
    }

    /// <summary>The nearest spot of a cell's land that can be stood on, round a spot.</summary>
    (float, float) StandNear(MapCell cell, float x, float z)
    {
        for (int r = 0; r < 40; r++)
            for (int k = 0; k < Math.Max(1, r * 6); k++)
            {
                float a = k * MathF.Tau / Math.Max(1, r * 6), px = x + MathF.Cos(a) * r, pz = z + MathF.Sin(a) * r;
                if (Land.Walkable((int)px, (int)pz) && Land.CellAt(px, pz) == cell) return (px, pz);
            }
        return Land.Center(cell);
    }

    /// <summary>
    /// A party of ours led over the land to a spot (the player's click): its way found, the cells it walks into its way
    /// from cell to cell. False if it cannot get there.
    /// </summary>
    public bool Lead(MapParty p, float x, float z)
    {
        if (Land == null || p.Owner != Owner.Ours) return false;
        if (!p.Placed) PlaceIn(p, CellOf(p));
        var way = AtlasWalk.Find(Land, p.AtX, p.AtZ, x, z);
        if (way == null || way.Count < 2) return false;
        var goal = Land.CellAt(way[^1].X, way[^1].Z);
        if (goal == null) return false;
        way.RemoveAt(0);
        p.Trail = way;
        p.Path = AtlasWalk.CellsOf(Land, Prepend(p, way));
        p.Progress = 0; p.Waits = false;
        p.GoalQ = goal.Q; p.GoalR = goal.R;
        return true;
    }

    static List<(float X, float Z)> Prepend(MapParty p, List<(float X, float Z)> way)
    {
        var all = new List<(float X, float Z)>(way.Count + 1) { (p.AtX, p.AtZ) };
        all.AddRange(way);
        return all;
    }

    /// <summary>The way a party would walk to a spot, from where it stands, and the seconds of the map's time it would take; null if there is none.</summary>
    public (List<(float X, float Z)> way, double seconds)? WayTo(MapParty p, float x, float z)
    {
        if (Land == null) return null;
        if (!p.Placed) PlaceIn(p, CellOf(p));
        var way = AtlasWalk.Find(Land, p.AtX, p.AtZ, x, z);
        if (way == null || way.Count < 2) return null;
        return (way, WaySeconds(p, way));
    }

    /// <summary>The seconds of the map's time a party takes along a way over the land.</summary>
    public double WaySeconds(MapParty p, List<(float X, float Z)> way)
    {
        double s = 0;
        for (int i = 1; i < way.Count; i++) s += LineSeconds(p, way[i - 1], way[i]);
        return s;
    }

    double LineSeconds(MapParty p, (float X, float Z) a, (float X, float Z) b)
    {
        float c = AtlasWalk.LineCost(Land, a, b);
        // (a stretch that cannot be stood on: the land changed under the way, it is gone over as open ground)
        if (float.IsInfinity(c)) c = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
        return AtlasWalk.SecondsPer(Land, p, 1) * c;
    }

    /// <summary>Seconds of the map's time a party walking the land has still to walk.</summary>
    public double TrailLeft(MapParty p) => !p.Walking || Land == null ? 0 : WaySeconds(p, Prepend(p, p.Trail));

    /// <summary>
    /// A party walks the land for so many seconds: along its way at the pace of the ground under its feet, into the
    /// next cell when its feet cross into it — then it is in that cell, as a party walking from cell to cell is when its
    /// step is done, and may be stopped there by the enemy.
    /// </summary>
    void WalkTrail(MapParty p, double dt)
    {
        double per = AtlasWalk.SecondsPer(Land, p, 1);
        while (dt > 1e-9 && p.Walking)
        {
            var (tx, tz) = p.Trail[0];
            float dx = tx - p.AtX, dz = tz - p.AtZ, len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 1e-3f) { p.Trail.RemoveAt(0); continue; }
            // a voxel at most at a time, at the cost of the ground it stands on
            float c = Land.Walkable((int)p.AtX, (int)p.AtZ) ? Land.Cost((int)p.AtX, (int)p.AtZ) : 1;
            float step = MathF.Min(1, len);
            double need = per * c * step;
            float go = need <= dt ? step : (float)(step * dt / need);
            dt -= need <= dt ? need : dt;
            p.AtX += dx / len * go; p.AtZ += dz / len * go;
            p.Facing = MathF.Atan2(dz, dx);
            if (go >= len - 1e-4f) p.Trail.RemoveAt(0);
            var cell = Land.CellAt(p.AtX, p.AtZ);
            if (cell != null && (cell.Q != p.Q || cell.R != p.R))
            {
                p.CameQ = p.Q; p.CameR = p.R;
                p.Q = cell.Q; p.R = cell.R;
                if (p.Path is { Count: > 0 } && p.Path[0] == cell) p.Path.RemoveAt(0);
                else p.Path = AtlasWalk.CellsOf(Land, Prepend(p, p.Trail));
                if (p.Owner == Owner.Horde) HordeReach(p, cell); else OursReach(p, cell);
                // stopped by the enemy, or gone into the valley: no further
                if (!Parties.Contains(p) || p.Waits) { p.Trail = null; p.Path?.Clear(); return; }
            }
        }
        if (!p.Walking) { p.Trail = null; p.Path?.Clear(); p.Progress = 0; }
    }
}
