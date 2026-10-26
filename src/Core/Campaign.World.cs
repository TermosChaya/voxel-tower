using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What a zone of the big map is to us: our own, a realm of the horde's, nobody's, nobody's and rich (further out, better kept).</summary>
public enum ZoneKind : byte { Home, Horde, Neutral, Treasure }

/// <summary>
/// A zone of the big map, as the random maps of «Heroes» lay them out: a stretch of one ground walled off from its
/// neighbours by mountains or deep water, gone into from them only by a pass, and every pass kept by a stack of beasts
/// or the dead. Its tier is how many passes lie between it and our own zone.
/// </summary>
public sealed class MapZone
{
    public int Id;
    public ZoneKind Kind = ZoneKind.Neutral;
    public int Tier;
    public HexGround Ground;
    /// <summary>Its middle: where its town stands, if it has one.</summary>
    public int Col, Row;
    public int Area;
    /// <summary>The zones it has a pass into.</summary>
    public readonly List<int> Links = new();
}

public sealed partial class MapCell
{
    /// <summary>The big map's look of the hex (docs/handoff/13-world-map.md): its ground, what stands up out of it, a height step.</summary>
    public HexGround Ground;
    public HexRelief Relief;
    public byte Height;
    /// <summary>The sides a road and a river leave the hex by, a bit for each (<see cref="Hex.NorthEast"/> first, round by the east).</summary>
    public byte Roads, Rivers;
    /// <summary>The zone it lies in, −1 on the small maps.</summary>
    public short Zone = -1;
    /// <summary>The one thing it holds, if any (MapThing.cs).</summary>
    public MapThing Thing;
    /// <summary>A pass between two zones.</summary>
    public bool Pass;
    /// <summary>Its zone's tier: how many passes lie between it and our own zone.</summary>
    public byte Tier;

    /// <summary>Its column and row on the big map (odd-r: every odd row half a hex to the east).</summary>
    public int Col => Q + (R - (R & 1)) / 2;
    public int Row => R;
    /// <summary>Whether anyone walks into it: not a mountain, not deep water.</summary>
    public bool Passable => Relief != HexRelief.Mountain && Ground != HexGround.DeepWater;
}

/// <summary>
/// The big map, copied from the random maps of «Heroes of Might and Magic III» (the author's word of 26.10.2026): a
/// rectangle of 36, 72, 108 or 144 hexes a side, every hex a valley made only when gone into, so a map of twenty
/// thousand of them costs a line each. It is cut into zones walled off by mountain ridges and lakes; each pass between
/// two of them kept by a stack. Our zone in a corner, the horde's realms (one to four) as far from it as the zones allow,
/// the rest nobody's, the further out the richer and the better kept. In each zone one ground with patches of another,
/// woods, hills and a few crags and lakes; rivers run from the hills to the lakes and the sea; roads run from the towns
/// to the passes. The things of the map lie sparse, each in its hex (MapThing.cs).
/// </summary>
public sealed partial class Campaign
{
    /// <summary>The big map's size in hexes (0 on the small maps of rings).</summary>
    public int Width, Height;
    public bool Big => Width > 0;
    /// <summary>The sides of the big maps: S, M, L, XL.</summary>
    public static readonly int[] Sides = { 36, 72, 108, 144 };
    public readonly List<MapZone> Zones = new();
    MapCell[] _grid;

    public static string SizeName(int side) => side switch { <= 36 => "S", <= 72 => "M", <= 108 => "L", _ => "XL" };

    /// <summary>A hex of the big map by column and row; null off it (and on the small maps).</summary>
    public MapCell Cell(int col, int row) =>
        _grid != null && col >= 0 && row >= 0 && col < Width && row < Height ? _grid[row * Width + col] : null;

    public static (int q, int r) Axial(int col, int row) => (col - (row - (row & 1)) / 2, row);
    public static (int col, int row) Offset(int q, int r) => (q + (r - (r & 1)) / 2, r);

    /// <summary>A hex's index on the big map (row by row), −1 off it.</summary>
    public int IndexOf(MapCell c) => c == null || !Big ? -1 : c.Row * Width + c.Col;

    /// <summary>A hex's middle on a plane of hexes one across: x to the east, z to the south.</summary>
    public static (double x, double z) Plane(int col, int row) => (col + .5 * (row & 1), row * .8660254);

    /// <summary>A new big map of so many hexes a side.</summary>
    public static Campaign NewWorld(uint seed, int side)
    {
        side = Math.Clamp(side, 24, 160);
        var c = new Campaign { Seed = seed, Width = side, Height = side };
        new WorldMaker(c, seed).Make();
        return c;
    }

    /// <summary>The grid made for the cells laid row by row (made, or read back).</summary>
    void Grid()
    {
        _grid = new MapCell[Width * Height];
        foreach (var c in Cells) { var (col, row) = (c.Col, c.Row); if (col >= 0 && row >= 0 && col < Width && row < Height) _grid[row * Width + col] = c; }
    }

    /// <summary>
    /// The valley kind of a hex of the big map, from its look: a mine's is an ore valley, a shrine's a holy one, a wood a
    /// forest, a swamp a marsh, hills, rough ground and snow mountains, the rest the plain of the first.
    /// </summary>
    public static ValleyKind KindOfHex(MapCell c)
    {
        if (c.Home) return ValleyKind.First;
        if (c.Thing is { Kind: ThingKind.Mine } || c.Trove == TroveKind.DeadMine) return ValleyKind.Ore;
        if (c.Thing is { Kind: ThingKind.Shrine } || c.Trove == TroveKind.Shrine) return ValleyKind.Sacred;
        if (c.Relief == HexRelief.Forest) return ValleyKind.Forest;
        if (c.Ground is HexGround.Swamp or HexGround.Water || c.Rivers != 0) return ValleyKind.Marsh;
        if (c.Relief == HexRelief.Hills || c.Ground is HexGround.Rough or HexGround.Snow) return ValleyKind.Mountain;
        return ValleyKind.First;
    }

    /// <summary>
    /// How far out a hex lies, as the small map's rings count it, for what keeps a treasure and what it gives: on the big
    /// map its zone's tier and one.
    /// </summary>
    public static int RingOf(MapCell c) => c.Zone < 0 ? Distance(0, 0, c.Q, c.R) : c.Tier + 1;

    /// <summary>The roads and rivers of a hex of the big map onto the sides its valley is made with.</summary>
    internal void EdgesOfHex(MapCell c)
    {
        int first = -1;
        for (int s = 0; s < 6; s++)
        {
            c.Edges[s] ??= new HexEdge();
            var (dq, dr) = Step[s];
            c.Edges[s].Seed = EdgeSeed(Seed, c.Q, c.R, c.Q + dq, c.R + dr);
            c.Edges[s].Road = (c.Roads >> s & 1) != 0 ? .5f : -1;
            c.Edges[s].River = -1;
        }
        // (a valley's river runs in over one side and out over another: the first two the map's river takes)
        for (int s = 0; s < 6; s++)
        {
            if ((c.Rivers >> s & 1) == 0) continue;
            var n = Neighbour(c, s);
            bool firstOfPair = n == null || IndexOf(n) > IndexOf(c);
            c.Edges[s].River = firstOfPair ? .25f : .75f;
            if (first < 0) first = s; else { c.Edges[s].River = 1 - c.Edges[s].River; break; }
        }
        // the enemy comes by a road's side, or by the side toward the nearest realm of the horde
        int road = -1;
        for (int s = 0; s < 6; s++) if ((c.Roads >> s & 1) != 0) { road = s; break; }
        if (road >= 0) c.EntrySide = road;
    }
}

/// <summary>The making of a big map, kept apart: the zones, the walls between them and their passes, the ground, the waters, the roads, the things.</summary>
sealed class WorldMaker
{
    readonly Campaign _c;
    readonly Mulberry32 _rnd;
    readonly ValueNoise _n1, _n2, _n3, _n4;
    readonly int W, H;
    MapCell[] _g;
    bool[] _blocked;
    readonly List<(int a, int b)> _links = new();
    readonly Dictionary<long, List<(int a, int b)>> _borders = new();
    readonly List<int>[] _zoneHexes;
    int _zones;

    public WorldMaker(Campaign c, uint seed)
    {
        _c = c; W = c.Width; H = c.Height;
        _rnd = new Mulberry32(seed * 2654435761u + 977);
        _n1 = new ValueNoise(seed * 31u + 1); _n2 = new ValueNoise(seed * 37u + 2); _n3 = new ValueNoise(seed * 41u + 3); _n4 = new ValueNoise(seed * 43u + 4);
        _zones = Math.Max(5, (int)Math.Round(W / 7.2) + 2);
        _zoneHexes = new List<int>[_zones];
    }

    double R() => _rnd.Next();
    int RInt(int n) => Math.Min(n - 1, (int)(R() * n));
    MapCell G(int col, int row) => col >= 0 && row >= 0 && col < W && row < H ? _g[row * W + col] : null;
    int Idx(MapCell c) => c.Row * W + c.Col;
    MapCell Nb(MapCell c, int s) { var (dq, dr) = Campaign.Step[s]; var (col, row) = Campaign.Offset(c.Q + dq, c.R + dr); return G(col, row); }
    static long Pair(int a, int b) => a < b ? (long)a << 32 | (uint)b : (long)b << 32 | (uint)a;

    public void Make()
    {
        _g = new MapCell[W * H];
        _blocked = new bool[W * H];
        for (int row = 0; row < H; row++)
            for (int col = 0; col < W; col++)
            {
                var (q, r) = Campaign.Axial(col, row);
                var cell = new MapCell { Q = q, R = r, Seed = Campaign.CellSeedOf(_c.Seed, q, r), Holding = Holding.Outpost };
                _g[row * W + col] = cell;
                _c.Cells.Add(cell);
            }
        _c.UseGrid(_g);
        Zones();
        Roles();
        Walls();
        Ground();
        Waters();
        Roads();
        Things();
        foreach (var cell in _c.Cells) { _c.EdgesOfHex(cell); cell.Kind = Campaign.KindOfHex(cell); cell.Tier = (byte)Math.Min(255, _c.Zones[cell.Zone].Tier); }
        EntrySides();
    }

    // ------------------------------------------------------------ the zones

    void Zones()
    {
        // their middles, spread by a least distance (smaller and smaller till they all fit), clear of the rim
        double area = W * H * .866, least = .8 * Math.Sqrt(area / _zones);
        var seeds = new List<(double x, double z, int col, int row)>();
        for (int tries = 0; seeds.Count < _zones && tries < 40000; tries++)
        {
            if (tries % 4000 == 3999) least *= .9;
            int col = 3 + RInt(W - 6), row = 3 + RInt(H - 6);
            var (x, z) = Campaign.Plane(col, row);
            bool ok = true;
            foreach (var s in seeds) if ((s.x - x) * (s.x - x) + (s.z - z) * (s.z - z) < least * least) { ok = false; break; }
            if (ok) seeds.Add((x, z, col, row));
        }
        _zones = seeds.Count;
        for (int i = 0; i < _zones; i++)
        {
            _c.Zones.Add(new MapZone { Id = i, Col = seeds[i].col, Row = seeds[i].row });
            _zoneHexes[i] = new List<int>();
        }
        // each hex the nearest middle's, the borders bent by noise
        double warp = .45 * least;
        foreach (var cell in _g)
        {
            var (x, z) = Campaign.Plane(cell.Col, cell.Row);
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < _zones; i++)
            {
                double d = Math.Sqrt((seeds[i].x - x) * (seeds[i].x - x) + (seeds[i].z - z) * (seeds[i].z - z));
                d += (_n1.Sample(x * .13 + i * 7.31, z * .13 - i * 3.7) - .5) * warp;
                if (d < bd) { bd = d; best = i; }
            }
            cell.Zone = (short)best;
        }
        // each zone of a piece: a hex cut off from its zone's middle goes to the zone of most of its neighbours that is
        for (int pass = 0; pass < 6; pass++)
        {
            var reached = new bool[W * H];
            foreach (var z in _c.Zones)
            {
                var start = G(z.Col, z.Row);
                if (start.Zone != z.Id) start.Zone = (short)z.Id;
                var q = new Queue<MapCell>();
                q.Enqueue(start); reached[Idx(start)] = true;
                while (q.Count > 0)
                {
                    var a = q.Dequeue();
                    for (int s = 0; s < 6; s++)
                        if (Nb(a, s) is { } b && !reached[Idx(b)] && b.Zone == a.Zone) { reached[Idx(b)] = true; q.Enqueue(b); }
                }
            }
            bool moved = false;
            foreach (var cell in _g)
            {
                if (reached[Idx(cell)]) continue;
                var count = new Dictionary<short, int>();
                for (int s = 0; s < 6; s++) if (Nb(cell, s) is { } b && reached[Idx(b)]) count[b.Zone] = count.GetValueOrDefault(b.Zone) + 1;
                short pick = cell.Zone; int most = 0;
                foreach (var (k, v) in count) if (v > most) { most = v; pick = k; }
                if (pick != cell.Zone) { cell.Zone = pick; moved = true; }
            }
            if (!moved) break;
        }
        foreach (var cell in _g) _zoneHexes[cell.Zone].Add(Idx(cell));
        foreach (var z in _c.Zones) z.Area = _zoneHexes[z.Id].Count;
        // the borders: every two neighbouring hexes of two zones
        foreach (var cell in _g)
            for (int s = 0; s < 3; s++)
                if (Nb(cell, s) is { } b && b.Zone != cell.Zone)
                {
                    long key = Pair(cell.Zone, b.Zone);
                    if (!_borders.TryGetValue(key, out var list)) _borders[key] = list = new List<(int, int)>();
                    list.Add(cell.Zone < b.Zone ? (Idx(cell), Idx(b)) : (Idx(b), Idx(cell)));
                }
    }

    /// <summary>The zones' neighbours, as the borders have them.</summary>
    List<int> Neighbours(int z)
    {
        var list = new List<int>();
        foreach (var key in _borders.Keys)
        {
            int a = (int)(key >> 32), b = (int)(key & 0xffffffff);
            if (a == z) list.Add(b); else if (b == z) list.Add(a);
        }
        return list;
    }

    int[] Steps(int from, Func<int, int, bool> linked)
    {
        var d = new int[_zones];
        Array.Fill(d, int.MaxValue);
        d[from] = 0;
        var q = new Queue<int>();
        q.Enqueue(from);
        while (q.Count > 0)
        {
            int a = q.Dequeue();
            foreach (int b in Neighbours(a)) if (d[b] == int.MaxValue && linked(a, b)) { d[b] = d[a] + 1; q.Enqueue(b); }
        }
        return d;
    }

    void Roles()
    {
        // ours: the zone nearest a corner of the seed's
        var corners = new[] { (0, 0), (W - 1, 0), (0, H - 1), (W - 1, H - 1) };
        var (kc, kr) = corners[RInt(4)];
        var (cx, cz) = Campaign.Plane(kc, kr);
        MapZone home = null; double hd = double.MaxValue;
        foreach (var z in _c.Zones)
        {
            var (x, zz) = Campaign.Plane(z.Col, z.Row);
            double d = (x - cx) * (x - cx) + (zz - cz) * (zz - cz);
            if (d < hd) { hd = d; home = z; }
        }
        home.Kind = ZoneKind.Home;
        var fromHome = Steps(home.Id, (_, _) => true);
        // the horde's realms: one a 36 hexes of side, each as far from ours and from the others as can be
        int realms = Math.Clamp(W / 36, 1, 4);
        var horde = new List<MapZone>();
        for (int k = 0; k < realms; k++)
        {
            MapZone pick = null; double best = -1;
            foreach (var z in _c.Zones)
            {
                if (z.Kind != ZoneKind.Neutral || z == home || fromHome[z.Id] < 2 && _zones > 4) continue;
                var (x, zz) = Campaign.Plane(z.Col, z.Row);
                var (hx, hz) = Campaign.Plane(home.Col, home.Row);
                double far = fromHome[z.Id] * 100 + Math.Sqrt((x - hx) * (x - hx) + (zz - hz) * (zz - hz));
                foreach (var o in horde)
                {
                    var (ox, oz) = Campaign.Plane(o.Col, o.Row);
                    far = Math.Min(far, Steps(o.Id, (_, _) => true)[z.Id] * 100 + Math.Sqrt((x - ox) * (x - ox) + (zz - oz) * (zz - oz)));
                }
                if (far > best) { best = far; pick = z; }
            }
            if (pick == null) break;
            pick.Kind = ZoneKind.Horde;
            horde.Add(pick);
        }
        // the passes: a tree out from ours, and a third of the other borders besides, for ways round
        var inTree = new bool[_zones];
        inTree[home.Id] = true;
        var q = new Queue<int>();
        q.Enqueue(home.Id);
        var linked = new HashSet<long>();
        while (q.Count > 0)
        {
            int a = q.Dequeue();
            var next = Neighbours(a);
            for (int i = next.Count - 1; i > 0; i--) { int j = RInt(i + 1); (next[i], next[j]) = (next[j], next[i]); }
            foreach (int b in next)
            {
                if (inTree[b]) continue;
                inTree[b] = true;
                linked.Add(Pair(a, b));
                q.Enqueue(b);
            }
        }
        foreach (var key in _borders.Keys) if (!linked.Contains(key) && R() < .33) linked.Add(key);
        foreach (var key in linked)
        {
            int a = (int)(key >> 32), b = (int)(key & 0xffffffff);
            _links.Add((a, b));
            _c.Zones[a].Links.Add(b); _c.Zones[b].Links.Add(a);
        }
        // the tiers: passes from ours; past the second nobody's zones are rich
        var tier = Steps(home.Id, (a, b) => linked.Contains(Pair(a, b)));
        foreach (var z in _c.Zones)
        {
            z.Tier = tier[z.Id] == int.MaxValue ? 4 : tier[z.Id];
            if (z.Kind == ZoneKind.Neutral && (z.Tier >= 3 || z.Tier == 2 && R() < .4)) z.Kind = ZoneKind.Treasure;
            // the ground: ours green, the horde's burnt, the rest by how far north or south it lies
            double lat = z.Row / (double)H, roll = R();
            z.Ground = z.Kind switch
            {
                ZoneKind.Home => HexGround.Grass,
                ZoneKind.Horde => HexGround.Scorched,
                _ when lat < .28 => roll < .55 ? HexGround.Snow : roll < .8 ? HexGround.Rough : HexGround.Dirt,
                _ when lat > .72 => roll < .5 ? HexGround.Sand : roll < .75 ? HexGround.Dirt : HexGround.Swamp,
                _ => roll < .4 ? HexGround.Grass : roll < .6 ? HexGround.Dirt : roll < .8 ? HexGround.Swamp : HexGround.Rough,
            };
        }
    }

    // ------------------------------------------------------------ the walls and the passes

    void Walls()
    {
        // the borders: the hexes of the lower zone along each border walled, those of the other often too (a ridge or a
        // lake one or two hexes thick), a lake of a border now and then instead of mountains
        var water = new Dictionary<long, bool>();
        foreach (var key in _borders.Keys) water[key] = R() < .22;
        var wet = new bool[W * H];
        foreach (var (key, list) in _borders)
            foreach (var (a, b) in list)
            {
                _blocked[a] = true;
                if (_n2.Sample(_g[b].Col * .3, _g[b].Row * .3) > .45) _blocked[b] = true;
                if (water[key]) { wet[a] = true; wet[b] = true; }
            }
        // the rim: mountains, or the sea
        foreach (var cell in _g)
            if (cell.Col == 0 || cell.Row == 0 || cell.Col == W - 1 || cell.Row == H - 1)
            {
                _blocked[Idx(cell)] = true;
                wet[Idx(cell)] = _n3.Sample(cell.Col * .08, cell.Row * .08) > .55;
            }
        foreach (var cell in _g)
            if (_blocked[Idx(cell)])
            {
                if (wet[Idx(cell)]) { cell.Ground = HexGround.DeepWater; cell.Relief = HexRelief.Flat; }
                else { cell.Relief = HexRelief.Mountain; }
            }
        // a pass in each linked border; a zone left with none (its borders too short) given one to a neighbour that has
        var made = new HashSet<long>();
        foreach (var (za, zb) in _links) if (MakePass(za, zb)) made.Add(Pair(za, zb));
        for (int round = 0; round < _zones; round++)
        {
            var reached = Steps(_c.Zones.Find(z => z.Kind == ZoneKind.Home).Id, (a, b) => made.Contains(Pair(a, b)));
            int cut = Array.FindIndex(reached, d => d == int.MaxValue);
            if (cut < 0) break;
            bool joined = false;
            foreach (int n in Neighbours(cut))
                if (reached[n] != int.MaxValue && !made.Contains(Pair(cut, n)) && MakePass(cut, n)) { made.Add(Pair(cut, n)); joined = true; break; }
            if (!joined) break;
        }
        // the tiers as the passes made have them
        var tiers = Steps(_c.Zones.Find(z => z.Kind == ZoneKind.Home).Id, (a, b) => made.Contains(Pair(a, b)));
        foreach (var z in _c.Zones) if (tiers[z.Id] != int.MaxValue) z.Tier = tiers[z.Id];
        foreach (var z in _c.Zones) z.Links.Clear();
        _links.Clear();
        foreach (long key in made)
        {
            int a = (int)(key >> 32), b = (int)(key & 0xffffffff);
            _links.Add((a, b));
            _c.Zones[a].Links.Add(b); _c.Zones[b].Links.Add(a);
        }
    }

    /// <summary>
    /// A pass between two zones: where a hex of each meets near their border's middle (alone, if it can be), the walls
    /// opened there and on into each zone, whatever else touches it walled up, and a stack set on it as strong as the
    /// further of the two is from ours. False if the border has no place for one.
    /// </summary>
    bool MakePass(int za, int zb)
    {
        if (!_borders.TryGetValue(Pair(za, zb), out var list)) return false;
        double mx = 0, mz = 0;
        foreach (var (a, b) in list) { var (x, z) = Campaign.Plane(_g[a].Col, _g[a].Row); mx += x / list.Count; mz += z / list.Count; }
        (int a, int b) pick = (-1, -1);
        for (int round = 0; round < 2 && pick.a < 0; round++)
        {
            double pd = double.MaxValue;
            foreach (var (a, b) in list)
            {
                var ca = _g[a]; var cb = _g[b];
                if (OnRim(ca) || OnRim(cb) || ca.Pass || cb.Pass || NearPass(ca, 1) || NearPass(cb, 1) || round == 0 && (Touches(ca, za, zb) || Touches(cb, za, zb))) continue;
                var (x, z) = Campaign.Plane(ca.Col, ca.Row);
                double d = (x - mx) * (x - mx) + (z - mz) * (z - mz);
                if (d < pd) { pd = d; pick = (a, b); }
            }
        }
        if (pick.a < 0) return false;
        foreach (int i in new[] { pick.a, pick.b }) Open(_g[i], true);
        foreach (int i in new[] { pick.a, pick.b }) Seal(_g[i]);
        foreach (int i in new[] { pick.a, pick.b }) Inward(_g[i]);
        int ta = _c.Zones[_g[pick.a].Zone].Tier, tb = _c.Zones[_g[pick.b].Zone].Tier;
        var guard = _g[ta >= tb ? pick.a : pick.b];
        int t = Math.Max(ta, tb) + 1;
        if (_c.Zones[_g[pick.a].Zone].Kind == ZoneKind.Horde || _c.Zones[_g[pick.b].Zone].Kind == ZoneKind.Horde) t++;
        Keep(guard, ThingKind.Monster, t);
        return true;
    }

    bool OnRim(MapCell c) => c.Col <= 0 || c.Row <= 0 || c.Col >= W - 1 || c.Row >= H - 1;

    /// <summary>Whether a hex has a neighbour of a zone other than those two (or the rim): opened, it would let a third in.</summary>
    bool Touches(MapCell c, int za, int zb)
    {
        for (int s = 0; s < 6; s++)
            if (Nb(c, s) is not { } b || OnRim(b) || b.Zone != za && b.Zone != zb) return true;
        return false;
    }

    /// <summary>The hexes of other zones next to one opened walled up, so that opening it lets nobody else in.</summary>
    void Seal(MapCell c)
    {
        for (int s = 0; s < 6; s++)
            if (Nb(c, s) is { } b && b.Zone != c.Zone && !b.Pass && !_blocked[Idx(b)])
            {
                _blocked[Idx(b)] = true;
                if (b.Ground != HexGround.DeepWater) b.Relief = HexRelief.Mountain;
            }
    }

    void Open(MapCell c, bool pass)
    {
        _blocked[Idx(c)] = false;
        c.Relief = HexRelief.Flat;
        if (c.Ground == HexGround.DeepWater) c.Ground = HexGround.Dirt;
        c.Pass |= pass;
    }

    /// <summary>A pass's hex led on into its zone: its neighbour of the same zone farthest from the border opened, if none is open.</summary>
    void Inward(MapCell c)
    {
        for (int s = 0; s < 6; s++) if (Nb(c, s) is { } b && b.Zone == c.Zone && !_blocked[Idx(b)]) return;
        var z = _c.Zones[c.Zone];
        MapCell best = null; double bd = double.MaxValue;
        for (int s = 0; s < 6; s++)
        {
            if (Nb(c, s) is not { } b || b.Zone != c.Zone || OnRim(b)) continue;
            double d = Campaign.Distance(b.Q, b.R, Campaign.Axial(z.Col, z.Row).q, z.Row);
            if (d < bd) { bd = d; best = b; }
        }
        if (best == null) return;
        Open(best, false);
        Seal(best);
        Inward(best);
    }

    // ------------------------------------------------------------ the ground

    void Ground()
    {
        foreach (var cell in _g)
        {
            var z = _c.Zones[cell.Zone];
            double x = cell.Col, y = cell.Row;
            if (!_blocked[Idx(cell)])
            {
                cell.Ground = z.Ground;
                // patches of a second ground
                if (_n2.Sample(x * .17 + 40, y * .17) > .66)
                    cell.Ground = z.Ground switch
                    {
                        HexGround.Grass => HexGround.Dirt, HexGround.Snow => HexGround.Rough, HexGround.Sand => HexGround.Dirt,
                        HexGround.Scorched => HexGround.Lava, HexGround.Swamp => HexGround.Grass, _ => HexGround.Grass,
                    };
                // woods in clumps, hills, and a few crags
                double wood = z.Ground switch { HexGround.Sand or HexGround.Lava or HexGround.Scorched => .74, HexGround.Swamp => .68, _ => .6 };
                if (_n3.Sample(x * .19, y * .19 + 90) > wood) cell.Relief = HexRelief.Forest;
                else if (_n4.Sample(x * .21 + 13, y * .21) > .7) cell.Relief = HexRelief.Hills;
                if (_n1.Sample(x * .29 + 70, y * .29 - 20) > .84 && !NearPass(cell, 2)) cell.Relief = HexRelief.Mountain;
            }
        }
        // lakes, a few a zone: deep in the middle, shallow about
        foreach (var z in _c.Zones)
        {
            int lakes = z.Area / 170;
            for (int k = 0; k < lakes; k++)
            {
                var c = PickIn(z, h => h.Relief == HexRelief.Flat && !NearPass(h, 2), 0, 99);
                if (c == null) break;
                c.Ground = HexGround.DeepWater;
                for (int s = 0; s < 6; s++)
                    if (Nb(c, s) is { } b && b.Zone == c.Zone && !_blocked[Idx(b)] && !b.Pass && !NearPass(b, 1))
                    {
                        b.Relief = HexRelief.Flat;
                        b.Ground = R() < .45 ? HexGround.DeepWater : HexGround.Water;
                    }
            }
        }
        // what cannot be reached from a zone's middle walled up too, so no hex of a zone is cut off
        foreach (var z in _c.Zones)
        {
            var mid = G(z.Col, z.Row);
            Open(mid, false);
            Seal(mid);
            mid.Ground = z.Ground == HexGround.Scorched || z.Kind != ZoneKind.Horde ? z.Ground : HexGround.Scorched;
            if (mid.Ground is HexGround.Water or HexGround.DeepWater) mid.Ground = HexGround.Grass;
            var reached = Reached(z, mid);
            // a pass cut off from the middle by a crag or a lake: a way carved to it
            for (int tries = 0; tries < 6; tries++)
            {
                int k = _zoneHexes[z.Id].FindIndex(i => _g[i].Pass && !reached.Contains(i));
                if (k < 0) break;
                Carve(z, _g[_zoneHexes[z.Id][k]], reached);
                reached = Reached(z, mid);
            }
            foreach (int i in _zoneHexes[z.Id])
            {
                var c = _g[i];
                if (reached.Contains(i) || !c.Passable || c.Pass) continue;
                if (c.Ground == HexGround.Water) c.Ground = HexGround.DeepWater; else c.Relief = HexRelief.Mountain;
            }
        }
        // heights, for the eye
        foreach (var cell in _g)
        {
            double n = _n4.Sample(cell.Col * .09, cell.Row * .09 + 300);
            int h = cell.Relief switch
            {
                HexRelief.Mountain => 12 + (int)(n * 4),
                HexRelief.Hills => 7 + (int)(n * 3),
                HexRelief.Forest => 4 + (int)(n * 3),
                _ => 2 + (int)(n * 4),
            };
            if (cell.Ground is HexGround.Water or HexGround.DeepWater) h = cell.Ground == HexGround.DeepWater ? 0 : 1;
            cell.Height = (byte)Math.Clamp(h, 0, 15);
        }
    }

    /// <summary>The hexes of a zone walked to from a hex of it.</summary>
    HashSet<int> Reached(MapZone z, MapCell from)
    {
        var reached = new HashSet<int> { Idx(from) };
        var q = new Queue<MapCell>();
        q.Enqueue(from);
        while (q.Count > 0)
        {
            var a = q.Dequeue();
            for (int s = 0; s < 6; s++)
                if (Nb(a, s) is { } b && b.Zone == z.Id && b.Passable && (!_blocked[Idx(b)] || b.Pass) && reached.Add(Idx(b))) q.Enqueue(b);
        }
        return reached;
    }

    /// <summary>A way opened through a zone from a hex cut off to the nearest hex walked to from its middle.</summary>
    void Carve(MapZone z, MapCell from, HashSet<int> reached)
    {
        var back = new Dictionary<int, int> { [Idx(from)] = -1 };
        var q = new Queue<MapCell>();
        q.Enqueue(from);
        MapCell hit = null;
        while (q.Count > 0 && hit == null)
        {
            var a = q.Dequeue();
            for (int s = 0; s < 6; s++)
            {
                if (Nb(a, s) is not { } b || b.Zone != z.Id || OnRim(b) || back.ContainsKey(Idx(b))) continue;
                back[Idx(b)] = Idx(a);
                if (reached.Contains(Idx(b))) { hit = b; break; }
                q.Enqueue(b);
            }
        }
        if (hit == null) return;
        for (int i = back[Idx(hit)]; i >= 0 && i != Idx(from); i = back[i])
        {
            var c = _g[i];
            Open(c, false);
            Seal(c);
        }
    }

    bool NearPass(MapCell c, int reach)
    {
        if (c.Pass) return true;
        if (reach <= 0) return false;
        for (int s = 0; s < 6; s++) if (Nb(c, s) is { } b && NearPass(b, reach - 1)) return true;
        return false;
    }

    // ------------------------------------------------------------ the waters

    void Waters()
    {
        foreach (var z in _c.Zones)
        {
            int rivers = Math.Max(z.Kind == ZoneKind.Home ? 1 : 0, z.Area / 230);
            for (int k = 0; k < rivers; k++)
            {
                // from the foot of hills or a crag, down the heights, into a lake, the sea or the walls
                var src = PickIn(z, h => h.Rivers == 0 && h.Relief != HexRelief.Mountain && !h.Pass && HasNear(h, b => b.Relief is HexRelief.Mountain or HexRelief.Hills), 0, 99);
                if (src == null) continue;
                var line = new List<MapCell> { src };
                var at = src;
                for (int steps = 0; steps < 26; steps++)
                {
                    MapCell next = null; double low = double.MaxValue;
                    for (int s = 0; s < 6; s++)
                    {
                        if (Nb(at, s) is not { } b || line.Contains(b)) continue;
                        double h = b.Height + R() * 2.5 + (b.Zone != z.Id ? 6 : 0);
                        if (b.Ground is HexGround.Water or HexGround.DeepWater) h = -10;
                        if (h < low) { low = h; next = b; }
                    }
                    if (next == null) break;
                    line.Add(next);
                    if (next.Ground is HexGround.Water or HexGround.DeepWater || !next.Passable || next.Rivers != 0 || next.Zone != z.Id) break;
                    at = next;
                }
                if (line.Count < 4) continue;
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    int s = Side(line[i], line[i + 1]);
                    if (s < 0) break;
                    line[i].Rivers |= (byte)(1 << s);
                    if (line[i + 1].Passable && line[i + 1].Ground is not (HexGround.Water or HexGround.DeepWater)) line[i + 1].Rivers |= (byte)(1 << (s + 3) % 6);
                }
            }
        }
    }

    bool HasNear(MapCell c, Func<MapCell, bool> f)
    {
        for (int s = 0; s < 6; s++) if (Nb(c, s) is { } b && f(b)) return true;
        return false;
    }

    int Side(MapCell a, MapCell b)
    {
        for (int s = 0; s < 6; s++) if (Nb(a, s) == b) return s;
        return -1;
    }

    // ------------------------------------------------------------ the roads

    void Roads()
    {
        foreach (var z in _c.Zones)
        {
            var mid = G(z.Col, z.Row);
            foreach (int i in _zoneHexes[z.Id])
                if (_g[i].Pass) Road(mid, _g[i]);
        }
        // across each pass, from its hex in one zone to its hex in the other
        foreach (var cell in _g)
            if (cell.Pass)
                for (int s = 0; s < 6; s++)
                    if (Nb(cell, s) is { Pass: true } b && b.Zone != cell.Zone)
                    {
                        cell.Roads |= (byte)(1 << s);
                        b.Roads |= (byte)(1 << (s + 3) % 6);
                    }
    }

    /// <summary>A road laid from one hex to another of the same zone, the cheapest way over its ground, along roads laid before.</summary>
    void Road(MapCell from, MapCell to)
    {
        int zone = from.Zone;
        var cost = new Dictionary<MapCell, double> { [from] = 0 };
        var back = new Dictionary<MapCell, MapCell>();
        var open = new PriorityQueue<MapCell, double>();
        open.Enqueue(from, 0);
        var done = new HashSet<MapCell>();
        while (open.TryDequeue(out var a, out _))
        {
            if (a == to) break;
            if (!done.Add(a)) continue;
            for (int s = 0; s < 6; s++)
            {
                if (Nb(a, s) is not { } b || done.Contains(b) || b.Zone != zone || !b.Passable || _blocked[Idx(b)] && !b.Pass) continue;
                double step = b.Roads != 0 ? .3 : b.Relief switch { HexRelief.Forest => 2.4, HexRelief.Hills => 2, _ => 1 };
                if (b.Ground is HexGround.Swamp or HexGround.Water) step += 1.5;
                if (b.Rivers != 0) step += 1.2;
                if (b.Thing != null && b != to) step += 6;
                double g = cost[a] + step;
                if (cost.TryGetValue(b, out double had) && had <= g) continue;
                cost[b] = g; back[b] = a;
                open.Enqueue(b, g);
            }
        }
        if (!back.ContainsKey(to)) return;
        for (var x = to; x != from; x = back[x])
        {
            var y = back[x];
            int s = Side(y, x);
            if (s < 0) break;
            y.Roads |= (byte)(1 << s);
            x.Roads |= (byte)(1 << (s + 3) % 6);
        }
    }

    // ------------------------------------------------------------ the things

    /// <summary>A hex of a zone for a thing, at so many hexes from its middle: open, free, nothing in the next hexes, not on the walls.</summary>
    MapCell PickIn(MapZone z, Func<MapCell, bool> where, int near, int far, bool spaced = true)
    {
        var mid = G(z.Col, z.Row);
        var list = new List<MapCell>();
        foreach (int i in _zoneHexes[z.Id])
        {
            var c = _g[i];
            if (_blocked[i] || !c.Passable || c.Pass || c.Thing != null || c == mid || OnRim(c)) continue;
            int d = Campaign.Distance(c.Q, c.R, mid.Q, mid.R);
            if (d < near || d > far || !where(c)) continue;
            if (spaced && HasNear(c, b => b.Thing != null || b == mid && near > 0)) continue;
            list.Add(c);
        }
        return list.Count == 0 ? null : list[RInt(list.Count)];
    }

    MapThing Put(MapCell c, ThingKind kind, Res res = Res.Gold, float amount = 0)
    {
        if (c == null) return null;
        c.Thing = new MapThing { Kind = kind, Res = res, Amount = amount };
        if (c.Ground is HexGround.Water) c.Ground = HexGround.Grass;
        return c.Thing;
    }

    /// <summary>A hex kept by a stack of so strong (and a thing put on it, if none is there).</summary>
    void Keep(MapCell c, ThingKind kind, int tier)
    {
        if (c == null) return;
        c.Thing ??= new MapThing { Kind = kind };
        foreach (var t in MapThing.Stack(tier, R)) c.Sum.Keep.Add(Campaign.Raider(t));
    }

    void Things()
    {
        var rich = new[] { Res.Gold, Res.Metal, Res.Mercury, Res.Sulfur, Res.Crystal, Res.Gems };
        var plain = new[] { Res.Logs, Res.Stone, Res.Metal, Res.Gold };
        var builds = new[] { ThingKind.Windmill, ThingKind.WaterWheel, ThingKind.Learning, ThingKind.Stables, ThingKind.Observatory, ThingKind.Dwelling, ThingKind.Shrine };
        var shrines = new[] { Blessing.Hardy, Blessing.Plenty, Blessing.Mending };
        foreach (var z in _c.Zones)
        {
            var mid = G(z.Col, z.Row);
            int a = z.Area;
            if (z.Kind == ZoneKind.Home)
            {
                mid.Home = true; mid.Owner = Owner.Ours; mid.Holding = Holding.Citadel;
                Put(mid, ThingKind.Citadel);
                Put(PickIn(z, c => HasNear(c, b => b.Relief == HexRelief.Forest), 2, 6) ?? PickIn(z, _ => true, 2, 7), ThingKind.Mine, Res.Logs, MapThing.Daily(Res.Logs));
                Put(PickIn(z, c => HasNear(c, b => b.Relief is HexRelief.Hills or HexRelief.Mountain), 2, 6) ?? PickIn(z, _ => true, 2, 7), ThingKind.Mine, Res.Stone, MapThing.Daily(Res.Stone));
                foreach (var r in new[] { Res.Logs, Res.Logs, Res.Stone, Res.Gold }) Pile(PickIn(z, _ => true, 2, 8), r);
                Put(PickIn(z, _ => true, 2, 8), ThingKind.Campfire, PlainRes(), 0);
                Put(PickIn(z, _ => true, 3, 9), R() < .5 ? ThingKind.Windmill : ThingKind.WaterWheel, RareRes(), 0);
                Dwell(PickIn(z, _ => true, 3, 9), 0);
                Put(PickIn(z, _ => true, 3, 10), ThingKind.Learning);
                Sign(z, mid);
                continue;
            }
            if (z.Kind == ZoneKind.Horde)
            {
                mid.Owner = Owner.Horde; mid.Holding = Holding.HordeTown;
                Put(mid, ThingKind.HordeTown);
                for (int k = 0, n = 1 + a / 260; k < n; k++)
                    if (PickIn(z, _ => true, 3, 8) is { } lair) { lair.Owner = Owner.Horde; lair.Holding = Holding.HordeTown; Put(lair, ThingKind.Lair); }
                foreach (var r in new[] { Res.Metal, Res.Gold })
                    if (Put(PickIn(z, _ => true, 2, 9), ThingKind.Mine, r, MapThing.Daily(r)) is { } m) m.Owner = Owner.Horde;
                Sign(z, mid);
                continue;
            }
            int tier = z.Tier;
            bool treasure = z.Kind == ZoneKind.Treasure;
            // mines: the common ones near us, the rare and the gold further out; kept from the second tier on
            for (int k = 0, n = Math.Max(1, a / 170); k < n; k++)
            {
                var r = tier <= 1 ? plain[RInt(plain.Length)] : rich[RInt(rich.Length)];
                var c = PickIn(z, _ => true, 2, 99);
                Put(c, ThingKind.Mine, r, MapThing.Daily(r));
                if (c != null && (tier >= 2 || R() < .4)) Keep(c, ThingKind.Mine, tier);
            }
            for (int k = 0, n = a / 45; k < n; k++) Pile(PickIn(z, _ => true, 1, 99), R() < .35 ? RareRes() : PlainRes());
            for (int k = 0, n = a / (treasure ? 60 : 110); k < n; k++) Put(PickIn(z, _ => true, 1, 99), ThingKind.Chest, Res.Gold, tier);
            for (int k = 0, n = a / 140; k < n; k++) Put(PickIn(z, _ => true, 1, 99), ThingKind.Campfire, PlainRes(), 0);
            for (int k = 0, n = Math.Max(1, a / 100); k < n; k++)
            {
                var kind = builds[RInt(builds.Length)];
                var c = PickIn(z, _ => true, 1, 99);
                if (c == null) continue;
                if (kind == ThingKind.Dwelling) Dwell(c, tier);
                else if (kind == ThingKind.Shrine) { var t = Put(c, ThingKind.Shrine); t.Blessing = shrines[RInt(shrines.Length)]; }
                else Put(c, kind, RareRes(), 0);
            }
            // the treasures of the valleys: from the second tier, more in a rich zone
            if (tier >= 2)
                for (int k = 0, n = treasure ? 2 : 1; k < n; k++)
                {
                    var c = PickIn(z, _ => true, 2, 99);
                    if (c == null) continue;
                    var kind = (TroveKind)(1 + RInt(4));
                    c.Trove = kind;
                    Put(c, ThingKind.Trove);
                    if (kind == TroveKind.Shrine) c.Blessing = shrines[RInt(shrines.Length)];
                    foreach (var t in Campaign.KeepersOf(kind, tier + 1)) c.Sum.Keep.Add(Campaign.Raider(t));
                }
            // stacks wandering the zone, standing in the way
            for (int k = 0, n = a / 85; k < n; k++) Keep(PickIn(z, _ => true, 2, 99), ThingKind.Monster, tier);
            Sign(z, mid);
        }
    }

    Res PlainRes() => R() switch { < .35 => Res.Logs, < .7 => Res.Stone, < .85 => Res.Metal, _ => Res.Gold };
    Res RareRes() => (Res)((int)Res.Mercury + RInt(4));

    void Pile(MapCell c, Res r) => Put(c, ThingKind.Pile, r, MapThing.PileOf(r, R()));

    void Dwell(MapCell c, int tier)
    {
        var t = Put(c, ThingKind.Dwelling);
        if (t == null) return;
        var kinds = tier <= 0 ? new[] { Kinds.Spearman, Kinds.Archer, Kinds.Slinger }
            : tier == 1 ? new[] { Kinds.Swordsman, Kinds.Crossbowman, Kinds.Hound }
            : new[] { Kinds.Halberdier, Kinds.Rider, Kinds.Knight };
        t.Breed = kinds[RInt(kinds.Length)];
        t.Stock = MapThing.Growth(t.Breed);
    }

    /// <summary>A signpost by a zone's middle, on a road if one passes, its line telling where the zone's passes go.</summary>
    void Sign(MapZone z, MapCell mid)
    {
        var c = PickIn(z, h => h.Roads != 0, 1, 4) ?? PickIn(z, _ => true, 1, 4);
        var t = Put(c, ThingKind.Signpost);
        if (t == null) return;
        string Of(MapZone o) => o.Kind switch
        {
            ZoneKind.Home => "к вашей цитадели", ZoneKind.Horde => "к землям орды", ZoneKind.Treasure => "к богатым землям под стражей",
            _ => "к ничьим землям",
        };
        var ways = new List<string>();
        foreach (int l in z.Links) ways.Add(Of(_c.Zones[l]));
        t.Text = ways.Count == 0 ? "Здесь кончается дорога." : $"Перевалов отсюда: {ways.Count}. Дороги ведут {string.Join(", ", ways)}.";
    }

    // ------------------------------------------------------------ the enemy's side of each valley

    /// <summary>Each hex's enemy comes by its road, or by the side toward the nearest town of the horde.</summary>
    void EntrySides()
    {
        var towns = new List<MapCell>();
        foreach (var z in _c.Zones) if (z.Kind == ZoneKind.Horde) towns.Add(G(z.Col, z.Row));
        foreach (var cell in _g)
        {
            if (cell.Roads != 0) continue;
            MapCell best = null; int bd = int.MaxValue;
            foreach (var t in towns) { int d = Campaign.Distance(cell.Q, cell.R, t.Q, t.R); if (d < bd) { bd = d; best = t; } }
            if (best != null && best != cell) cell.EntrySide = Campaign.SideToward(cell, best.Q, best.R);
        }
    }
}
