using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The ground of the map of valleys: its height, its water and what it is, column by column, made once from the campaign.
/// Each cell's ground is of its kind — the level fields of the first valley, the rolling meadows of a holy one, the low wet
/// ground of a river's valley, the knolls of an ore valley, the mountains' grey peaks — and goes over into its neighbour's
/// across their side. The rivers run in at the side the campaign laid them by and out at the other, a chain of cells from
/// the mountains to the sea; the roads from a cell's middle to the middle of each side a road crosses, so that they run on
/// from cell to cell, fording the rivers. Lakes lie in the hollows, the marshes are pools among the reeds. Round the field
/// of cells the land rises into mountains or falls away into the sea.
/// </summary>
public sealed partial class Atlas
{
    /// <summary>Per column: the first air over the ground; the first air over the water on it (0, none); the ground's top; what lies on it.</summary>
    int[] _h, _wet;
    byte[] _top;
    Lie[] _lie;
    /// <summary>Per column: how far to the nearest river's middle, the nearest way's (a road or a track), the nearest road's, voxels (far off, a large number).</summary>
    float[] _dRiver, _dRoad, _dMain;
    /// <summary>Per column: the way a river's water runs over it (for the water's flow), voxels a second.</summary>
    float[] _flowX, _flowZ;

    /// <summary>The roads and the rivers of each cell, as lines of points; the spot of each cell's thing off its middle.</summary>
    readonly Dictionary<MapCell, List<(float x, float z)[]>> _roads = new(), _rivers = new(), _mainRoads = new();
    readonly Dictionary<MapCell, (float x, float z)> _spot = new();
    /// <summary>The lakes: middle, radii across and along, turned so far.</summary>
    readonly List<(float x, float z, float rx, float rz, float turn)> _lakes = new();
    /// <summary>The peaks: middle, height over the ground about, radius, how sharp.</summary>
    readonly List<(float x, float z, float h, float r)> _peaks = new();
    /// <summary>The fields about the first valley's castle: middle, half sizes, turned so far, what grows (0 wheat, 1 green, 2 ploughed).</summary>
    readonly List<(float x, float z, float hx, float hz, float turn, int kind)> _fields = new();

    ValueNoise _n1, _n2, _n3;

    uint SeedOf(MapCell c, int what) => unchecked(Campaign.Seed * 2654435761u + (uint)(c.Q * 7919 + c.R * 104729 + what * 15485863));

    static float Smooth(float t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

    float Fbm(ValueNoise n, float x, float z)
    {
        float s = 0, amp = .5f, sum = 0;
        for (int o = 0; o < 3; o++) { s += amp * (float)n.Sample(x, z); sum += amp; x *= 2.03f; z *= 2.03f; amp *= .5f; }
        return s / sum;
    }

    float Ridge(float x, float z) { float v = Fbm(_n2, x, z); return 1 - MathF.Abs(2 * v - 1); }

    void Land()
    {
        int N = SX * SZ;
        _h = new int[N]; _wet = new int[N]; _top = new byte[N]; _lie = new Lie[N];
        _dRiver = new float[N]; _dRoad = new float[N]; _dMain = new float[N]; _flowX = new float[N]; _flowZ = new float[N];
        Array.Fill(_dRiver, 1e4f); Array.Fill(_dRoad, 1e4f); Array.Fill(_dMain, 1e4f);
        _n1 = new ValueNoise(Campaign.Seed * 31 + 5); _n2 = new ValueNoise(Campaign.Seed * 37 + 11); _n3 = new ValueNoise(Campaign.Seed * 41 + 17);
        Ways();
        Spots();
        Lakes();
        Peaks();
        Fields();
        Heights();
        Grounds();
    }

    // ------------------------------------------------------------ the ways: rivers and roads

    /// <summary>The point on a cell's side a river crosses it, the same point seen from either cell.</summary>
    (float x, float z) RiverPoint(MapCell c, int side) => HexOf(c).SidePoint(side, Campaign.RiverAt(c, side));

    /// <summary>The river's two sides of a cell, if it has one.</summary>
    static (int a, int b)? RiverSides(MapCell c)
    {
        int a = -1, b = -1;
        for (int s = 0; s < 6; s++) if (c.Edges[s] is { River: >= 0 }) { if (a < 0) a = s; else b = s; }
        return a >= 0 && b >= 0 ? (a, b) : null;
    }

    /// <summary>Whether the land off the field at a point rises into mountains (1) or falls into the sea (0), and between.</summary>
    float Highland(float x, float z)
    {
        float ang = MathF.Atan2(z - OZ, x - OX);
        float m = (float)_n3.Sample(MathF.Cos(ang) * 2.2f + 40, MathF.Sin(ang) * 2.2f + 40);
        return Smooth((m - .38f) / .2f);
    }

    void Ways()
    {
        // the rivers: each a chain of cells from the rim to the rim, run from the end in the mountains to the end by the sea
        var done = new HashSet<MapCell>();
        foreach (var start in Campaign.Cells)
        {
            if (done.Contains(start) || RiverSides(start) is not { } sides) continue;
            // walk to one end of the chain
            var c = start; int from = sides.a;
            for (int steps = 0; steps < Campaign.Cells.Count; steps++)
            {
                var n = Campaign.Neighbour(c, from);
                if (n == null || RiverSides(n) is not { } ns) break;
                int back = (from + 3) % 6;
                from = ns.a == back ? ns.b : ns.a;
                c = n;
            }
            // c lies at one end, its river leaving over «from» off the field: walk the chain from there to the other end
            var chain = new List<(MapCell cell, int inSide, int outSide)>();
            int inSide = from;
            for (int steps = 0; steps <= Campaign.Cells.Count && c != null; steps++)
            {
                var rs = RiverSides(c).Value;
                int outSide = rs.a == inSide ? rs.b : rs.a;
                chain.Add((c, inSide, outSide));
                done.Add(c);
                var n = Campaign.Neighbour(c, outSide);
                if (n == null || RiverSides(n) == null || done.Contains(n)) break;
                inSide = (outSide + 3) % 6;
                c = n;
            }
            // the end that lies in the mountains is the river's source
            var (fx, fz) = Off(chain[0].cell, chain[0].inSide);
            var (lx, lz) = Off(chain[^1].cell, chain[^1].outSide);
            if (Highland(lx, lz) > Highland(fx, fz))
            {
                chain.Reverse();
                for (int k = 0; k < chain.Count; k++) chain[k] = (chain[k].cell, chain[k].outSide, chain[k].inSide);
            }
            foreach (var (cell, i0, o0) in chain) LayRiver(cell, i0, o0);
        }
        // the roads: from each cell's middle to the middle of every side its valley's road crosses; where only the
        // neighbour's road comes in over a side, a track goes on from it to the middle, so that the ways run on
        foreach (var c in Campaign.Cells)
        {
            var list = new List<(float, float)[]>();
            var main = new List<(float, float)[]>();
            var (cx, cz) = Center(c);
            var hex = HexOf(c);
            for (int s = 0; s < 6; s++)
            {
                var n = Campaign.Neighbour(c, s);
                bool mine = c.Edges[s] is { Road: >= 0 }, theirs = n != null && n.Edges[(s + 3) % 6] is { Road: >= 0 };
                if (!mine && !theirs) continue;
                var (mx, mz) = hex.SidePoint(s, .5f);
                var (ix, iz) = hex.Inward(s);
                // (off the field it runs on a little into the hills and the dunes)
                if (n == null) { if (!mine) continue; mx -= ix * 14; mz -= iz * 14; }
                // out of the middle at a slant of its own, into the side square to it, so that it runs on smoothly
                // into the next cell's
                float l = Dist(cx, cz, mx, mz), turn = ((float)Hash.H3((int)mx, (int)mz, 977) - .5f) * 1.5f;
                float ox = (mx - cx) / l, oz = (mz - cz) / l, co = MathF.Cos(turn), si = MathF.Sin(turn);
                float ax = cx + (ox * co - oz * si) * l * .45f, az = cz + (ox * si + oz * co) * l * .45f;
                var line = Cubic(cx, cz, ax, az, mx + ix * l * .4f, mz + iz * l * .4f, mx, mz, 18);
                list.Add(line);
                if (mine) main.Add(line);
            }
            _roads[c] = list;
            _mainRoads[c] = main;
            foreach (var line in list) Rasterize(line, 4, _dRoad, null);
            foreach (var line in main) Rasterize(line, 4, _dMain, null);
        }
    }

    /// <summary>A point off the field beyond a cell's side, where a river comes from or goes to.</summary>
    (float x, float z) Off(MapCell c, int side)
    {
        var hex = HexOf(c);
        var (px, pz) = RiverPoint(c, side);
        var (ix, iz) = hex.Inward(side);
        return (px - ix * (Margin + 10), pz - iz * (Margin + 10));
    }

    /// <summary>A cell's river from where it comes in to where it goes out, bent toward the middle but clear of it.</summary>
    void LayRiver(MapCell c, int inSide, int outSide)
    {
        var (cx, cz) = Center(c);
        var (ax, az) = RiverPoint(c, inSide);
        var (bx, bz) = RiverPoint(c, outSide);
        float mx = (ax + bx) / 2, mz = (az + bz) / 2;
        float kx = mx + (cx - mx) * .45f, kz = mz + (cz - mz) * .45f;
        var pts = Curve(ax, az, kx, kz, bx, bz, 26);
        // clear of the middle, where the castle, the outpost or the lair stands
        for (int tries = 0; tries < 8 && Nearest(pts, cx, cz) < 22; tries++)
        {
            kx += (kx - cx) * .35f + (kx == cx ? 4 : 0); kz += (kz - cz) * .35f;
            pts = Curve(ax, az, kx, kz, bx, bz, 26);
        }
        // a little meander
        var list = new List<(float x, float z)>();
        if (Campaign.Neighbour(c, inSide) == null) { var (ox, oz) = Off(c, inSide); list.Add((ox, oz)); }
        for (int k = 0; k < pts.Length; k++)
        {
            float t = k / (float)(pts.Length - 1);
            float wob = MathF.Sin(t * MathF.PI) * (MathF.Sin(t * 9.4f + c.Q * 1.7f + c.R) * 4.2f + MathF.Sin(t * 21f + c.R * 2.3f) * 1.6f);
            var (dx, dz) = k + 1 < pts.Length ? (pts[k + 1].x - pts[k].x, pts[k + 1].z - pts[k].z) : (pts[k].x - pts[k - 1].x, pts[k].z - pts[k - 1].z);
            float l = MathF.Max(.001f, MathF.Sqrt(dx * dx + dz * dz));
            list.Add((pts[k].x - dz / l * wob, pts[k].z + dx / l * wob));
        }
        if (Campaign.Neighbour(c, outSide) == null) { var (ox, oz) = Off(c, outSide); list.Add((ox, oz)); }
        var line = list.ToArray();
        if (!_rivers.TryGetValue(c, out var rl)) _rivers[c] = rl = new List<(float, float)[]>();
        rl.Add(line);
        Rasterize(line, 12, _dRiver, (_flowX, _flowZ));
    }

    static (float x, float z)[] Cubic(float ax, float az, float kx, float kz, float jx, float jz, float bx, float bz, int n)
    {
        var p = new (float, float)[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n, u = 1 - t;
            p[i] = (u * u * u * ax + 3 * u * u * t * kx + 3 * u * t * t * jx + t * t * t * bx, u * u * u * az + 3 * u * u * t * kz + 3 * u * t * t * jz + t * t * t * bz);
        }
        return p;
    }

    static (float x, float z)[] Curve(float ax, float az, float kx, float kz, float bx, float bz, int n)
    {
        var p = new (float, float)[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n, u = 1 - t;
            p[i] = (u * u * ax + 2 * u * t * kx + t * t * bx, u * u * az + 2 * u * t * kz + t * t * bz);
        }
        return p;
    }

    static float Nearest((float x, float z)[] line, float x, float z)
    {
        float best = float.MaxValue;
        for (int k = 0; k + 1 < line.Length; k++) best = MathF.Min(best, SegDist(line[k], line[k + 1], x, z, out _));
        return best;
    }

    static float SegDist((float x, float z) a, (float x, float z) b, float x, float z, out float t)
    {
        float ex = b.x - a.x, ez = b.z - a.z, l2 = ex * ex + ez * ez;
        t = l2 < 1e-6f ? 0 : Math.Clamp(((x - a.x) * ex + (z - a.z) * ez) / l2, 0, 1);
        float px = a.x + ex * t - x, pz = a.z + ez * t - z;
        return MathF.Sqrt(px * px + pz * pz);
    }

    /// <summary>How far each column within reach is from a line, kept where it is the least so far; and the line's way there.</summary>
    void Rasterize((float x, float z)[] line, int reach, float[] dist, (float[] fx, float[] fz)? flow)
    {
        for (int k = 0; k + 1 < line.Length; k++)
        {
            var a = line[k]; var b = line[k + 1];
            int x0 = Math.Max(0, (int)MathF.Min(a.x, b.x) - reach), x1 = Math.Min(SX - 1, (int)MathF.Max(a.x, b.x) + reach);
            int z0 = Math.Max(0, (int)MathF.Min(a.z, b.z) - reach), z1 = Math.Min(SZ - 1, (int)MathF.Max(a.z, b.z) + reach);
            float ex = b.x - a.x, ez = b.z - a.z, el = MathF.Max(.001f, MathF.Sqrt(ex * ex + ez * ez));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = z * SX + x;
                    float d = SegDist(a, b, x + .5f, z + .5f, out _);
                    if (d >= dist[i]) continue;
                    dist[i] = d;
                    if (flow is { } f) { f.fx[i] = ex / el; f.fz[i] = ez / el; }
                }
        }
    }

    // ------------------------------------------------------------ the spots of the things, the lakes, the peaks, the fields

    /// <summary>
    /// Where a cell's thing stands off its middle — a treasure, the mines of an ore valley, the barrow of a holy one: half
    /// way to one of its corners, the one farthest from its roads and its river.
    /// </summary>
    void Spots()
    {
        foreach (var c in Campaign.Cells)
        {
            bool wants = c.Trove != TroveKind.None || c.Kind is ValleyKind.Ore or ValleyKind.Sacred;
            if (!wants || c.Home) continue;
            var (cx, cz) = Center(c);
            float best = -1; (float, float) at = (cx, cz);
            for (int k = 0; k < 6; k++)
            {
                float ang = -MathF.PI / 2 + k * MathF.PI / 3;
                float x = cx + MathF.Cos(ang) * CellR * .52f, z = cz + MathF.Sin(ang) * CellR * .52f;
                int i = Math.Clamp((int)z, 0, SZ - 1) * SX + Math.Clamp((int)x, 0, SX - 1);
                // (the corner toward the south, before the camera, liked a little)
                float clear = MathF.Min(_dRoad[i], _dRiver[i]) + (MathF.Sin(ang) > .5f ? 3 : 0);
                if (clear > best) { best = clear; at = (x, z); }
            }
            _spot[c] = at;
        }
    }

    void Lakes()
    {
        foreach (var c in Campaign.Cells)
        {
            var rnd = new Mulberry32(SeedOf(c, 3));
            int n = c.Kind switch
            {
                ValleyKind.Marsh => 3,
                ValleyKind.Forest => rnd.Next() < .7 ? 1 : 0,
                ValleyKind.First => 1,
                ValleyKind.Sacred => rnd.Next() < .6 ? 1 : 0,
                ValleyKind.Mountain => rnd.Next() < .5 ? 1 : 0,
                _ => rnd.Next() < .3 ? 1 : 0,
            };
            var hex = HexOf(c);
            var (cx, cz) = Center(c);
            for (int k = 0, tries = 0; k < n && tries < 60; tries++)
            {
                float r = c.Kind == ValleyKind.Mountain ? 5 + (float)rnd.Next() * 3 : c.Kind == ValleyKind.Marsh ? 7 + (float)rnd.Next() * 7 : 6 + (float)rnd.Next() * 6;
                float rx = r * (1 + (float)rnd.Next() * .5f), rz = r * (.7f + (float)rnd.Next() * .3f), big = MathF.Max(rx, rz);
                float x = cx + ((float)rnd.Next() * 2 - 1) * CellR * .8f, z = cz + ((float)rnd.Next() * 2 - 1) * CellR * .8f;
                if (hex.Edge(x, z) < big + 6) continue;
                if (Dist(x, z, cx, cz) < big + (c.Home ? 34 : 22)) continue;
                int i = (int)z * SX + (int)x;
                if (_dRoad[i] < big + 4 || _dRiver[i] < big + 4) continue;
                if (_spot.TryGetValue(c, out var sp) && Dist(x, z, sp.x, sp.z) < big + 14) continue;
                bool apart = true;
                foreach (var l in _lakes) if (Dist(x, z, l.x, l.z) < big + MathF.Max(l.rx, l.rz) + 6) apart = false;
                if (!apart) continue;
                _lakes.Add((x, z, rx, rz, (float)rnd.Next() * MathF.PI));
                k++;
            }
        }
    }

    static float Dist(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    /// <summary>How far a point is into a lake: below 1 in it, 1 at its shore.</summary>
    static float InLake((float x, float z, float rx, float rz, float turn) l, float x, float z)
    {
        float dx = x - l.x, dz = z - l.z, c = MathF.Cos(l.turn), s = MathF.Sin(l.turn);
        float u = (dx * c + dz * s) / l.rx, v = (-dx * s + dz * c) / l.rz;
        return MathF.Sqrt(u * u + v * v);
    }

    /// <summary>
    /// The peaks: a crowd of them in each mountain cell, a few rocky ones in an ore valley, and the ranges off the field
    /// where it rises; never on a road or a river, nor in a cell's middle or at its thing.
    /// </summary>
    void Peaks()
    {
        bool Clear(float x, float z, float r, MapCell c)
        {
            if ((uint)(int)x >= (uint)SX || (uint)(int)z >= (uint)SZ) return false;
            int i = (int)z * SX + (int)x;
            if (_dRoad[i] < r * .55f + 5 || _dRiver[i] < r * .55f + 5) return false;
            if (c != null)
            {
                var (cx, cz) = Center(c);
                if (Dist(x, z, cx, cz) < r + 14) return false;
                if (_spot.TryGetValue(c, out var sp) && Dist(x, z, sp.x, sp.z) < r + 10) return false;
            }
            foreach (var l in _lakes) if (Dist(x, z, l.x, l.z) < r + MathF.Max(l.rx, l.rz)) return false;
            return true;
        }
        foreach (var c in Campaign.Cells)
        {
            if (c.Kind is not (ValleyKind.Mountain or ValleyKind.Ore)) continue;
            var rnd = new Mulberry32(SeedOf(c, 5));
            bool high = c.Kind == ValleyKind.Mountain;
            int want = high ? 10 + (int)(rnd.Next() * 5) : 4 + (int)(rnd.Next() * 3);
            var hex = HexOf(c);
            var (cx, cz) = Center(c);
            for (int k = 0, tries = 0; k < want && tries < 120; tries++)
            {
                float h = high ? 20 + (float)rnd.Next() * 28 : 9 + (float)rnd.Next() * 9;
                float r = h * (high ? .62f + (float)rnd.Next() * .22f : .9f + (float)rnd.Next() * .3f);
                float x = cx + ((float)rnd.Next() * 2 - 1) * CellR, z = cz + ((float)rnd.Next() * 2 - 1) * CellR;
                if (hex.Edge(x, z) < -r * .3f || !Clear(x, z, r, c)) continue;
                _peaks.Add((x, z, h, r));
                k++;
            }
        }
        // the ranges off the field, where the land rises
        var rim = new Mulberry32(Campaign.Seed * 97 + 3);
        float perimeter = 2 * (SX + SZ);
        for (int k = 0; k < perimeter / 9; k++)
        {
            float x = (float)rim.Next() * SX, z = (float)rim.Next() * SZ;
            if (OnField((int)x, (int)z) || Highland(x, z) < .5f) continue;
            float d = OffField(x, z);
            if (d < 8) continue;
            float h = 20 + (float)rim.Next() * 28 + MathF.Min(d, 50) * .25f, r = h * (.6f + (float)rim.Next() * .25f);
            if (!Clear(x, z, r, null)) continue;
            _peaks.Add((x, z, h, r));
        }
    }

    /// <summary>How far a point lies off the field of cells, voxels (0 on it).</summary>
    float OffField(float x, float z)
    {
        if (OnField((int)x, (int)z)) return 0;
        float best = float.MaxValue;
        foreach (var c in Campaign.Cells)
        {
            var (cx, cz) = Center(c);
            if (Dist(x, z, cx, cz) > best + CellR * 1.2f) continue;
            best = MathF.Min(best, HexOf(c).Distance(x, z));
        }
        return best;
    }

    /// <summary>The fields about the first valley's castle: a patchwork of wheat, green rows and ploughed land.</summary>
    void Fields()
    {
        var home = Campaign.Home;
        if (home == null) return;
        var rnd = new Mulberry32(SeedOf(home, 7));
        var (cx, cz) = Center(home);
        var hex = HexOf(home);
        for (int tries = 0; tries < 80 && _fields.Count < 11; tries++)
        {
            float ang = (float)rnd.Next() * MathF.Tau, dist = 30 + (float)rnd.Next() * 16;
            float x = cx + MathF.Cos(ang) * dist, z = cz + MathF.Sin(ang) * dist;
            float hx = 5 + (float)rnd.Next() * 4, hz = 3 + (float)rnd.Next() * 3, big = MathF.Max(hx, hz);
            if (hex.Edge(x, z) < big + 3) continue;
            int i = (int)z * SX + (int)x;
            if (_dRoad[i] < big + 2 || _dRiver[i] < big + 4) continue;
            bool apart = true;
            foreach (var f in _fields) if (Dist(x, z, f.x, f.z) < big + MathF.Max(f.hx, f.hz) + 1) apart = false;
            foreach (var l in _lakes) if (Dist(x, z, l.x, l.z) < big + MathF.Max(l.rx, l.rz) + 3) apart = false;
            if (!apart) continue;
            // (turned to the way about the middle, as fields lie round a village)
            _fields.Add((x, z, hx, hz, ang + ((float)rnd.Next() - .5f) * .4f, (int)(rnd.Next() * 3)));
        }
    }

    // ------------------------------------------------------------ the heights

    /// <summary>The ground of a kind of cell at a point: its level and how it swells.</summary>
    float Profile(ValleyKind k, int x, int z)
    {
        float a = (Fbm(_n1, x / 36f, z / 36f) - .5f) * 2, b = (Fbm(_n2, x / 13f, z / 13f) - .5f) * 2;
        return Ground - .5f + k switch
        {
            ValleyKind.First => .8f + a * 1.6f + b * .5f,
            ValleyKind.Sacred => 1.6f + a * 3.2f + b * .7f,
            ValleyKind.Forest => 1 + a * 2.2f + b * .8f,
            ValleyKind.Marsh => .3f + a * 1.1f + b * .4f,
            ValleyKind.Ore => 2 + a * 2.5f + Ridge(x / 15f, z / 15f) * 4,
            ValleyKind.Mountain => 3 + a * 3.5f + Ridge(x / 17f, z / 17f) * 3,
            _ => 1,
        };
    }

    /// <summary>The land off the field: rising with the distance into the hills, or falling into the sea.</summary>
    float MarginProfile(int x, int z, float d)
    {
        float hi = Highland(x, z), a = (Fbm(_n1, x / 30f, z / 30f) - .5f) * 2;
        float hills = Ground + 1.5f + MathF.Min(d, 40) * .32f + a * 3 + Ridge(x / 14f, z / 14f) * MathF.Min(d, 30) * .2f;
        float sea = Ground - .5f - MathF.Min(d * .4f, 9) + a * 1.2f;
        return sea + (hills - sea) * hi;
    }

    void Heights()
    {
        var hf = new float[SX * SZ];
        var peak = new float[SX * SZ];
        for (int z = 0; z < SZ; z++)
            for (int x = 0; x < SX; x++)
            {
                int i = z * SX + x, ci = _cellOf[i];
                if (ci < 0) { hf[i] = MarginProfile(x, z, OffField(x + .5f, z + .5f)); continue; }
                var c = Campaign.Cells[ci];
                var hex = _hex[ci];
                float e = hex.Edge(x + .5f, z + .5f);
                var n = Campaign.Neighbour(c, hex.NearestSide(x + .5f, z + .5f));
                float own = Profile(c.Kind, x, z), other = n != null ? Profile(n.Kind, x, z) : MarginProfile(x, z, 0);
                float w = .5f + .5f * Smooth(e / 18f);
                hf[i] = own * w + other * (1 - w);
            }
        // the peaks over the ground about them, never steep down onto a road or a river
        foreach (var (px, pz, ph, pr) in _peaks)
        {
            int x0 = Math.Max(0, (int)(px - pr)), x1 = Math.Min(SX - 1, (int)(px + pr)), z0 = Math.Max(0, (int)(pz - pr)), z1 = Math.Min(SZ - 1, (int)(pz + pr));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + .5f - px, dz = z + .5f - pz, ang = MathF.Atan2(dz, dx);
                    float reach = pr * (1 + .22f * MathF.Sin(ang * 3 + px) + .12f * MathF.Sin(ang * 7 + pz));
                    float d = MathF.Sqrt(dx * dx + dz * dz) / reach;
                    if (d >= 1) continue;
                    int i = z * SX + x;
                    float v = ph * MathF.Pow(1 - d, 1.25f) * (.82f + .36f * Ridge(x / 5f, z / 5f));
                    v *= Smooth((_dRoad[i] - 3) / 6) * Smooth((_dRiver[i] - 4) / 6);
                    if (v > peak[i]) peak[i] = v;
                }
        }
        // the middles levelled for what stands in them: the castle's mound, the lair's ground, the outpost's place; the
        // spots of the things
        foreach (var c in Campaign.Cells)
        {
            var (cx, cz) = Center(c);
            float r = c.Home ? 20 : c.Holding == Holding.HordeTown ? 17 : 9, lift = c.Home ? 3.5f : .5f;
            Level(hf, peak, cx, cz, r, 10, Ground + lift);
            if (_spot.TryGetValue(c, out var sp)) Level(hf, peak, sp.x, sp.z, 10, 6, float.NaN);
        }
        foreach (var f in _fields) Level(hf, peak, f.x, f.z, MathF.Max(f.hx, f.hz) + 1, 4, Ground + .5f);
        for (int i = 0; i < hf.Length; i++)
        {
            _h[i] = Math.Clamp((int)MathF.Round(hf[i] + peak[i]), 34, Height - 12);
            if (peak[i] > 4) _lie[i] |= Lie.Peak;
        }
        // the lakes, the marshes' pools, the rivers' beds and their banks, the fords
        for (int z = 0; z < SZ; z++)
            for (int x = 0; x < SX; x++)
            {
                int i = z * SX + x;
                foreach (var l in _lakes)
                {
                    if (MathF.Abs(x - l.x) > l.rx + l.rz + 4 || MathF.Abs(z - l.z) > l.rx + l.rz + 4) continue;
                    float d = InLake(l, x + .5f, z + .5f);
                    if (d < 1)
                    {
                        _h[i] = Math.Min(_h[i], WaterLevel - 1 - (int)MathF.Round(3 * (1 - d * d)));
                        _wet[i] = WaterLevel;
                        _lie[i] |= Lie.Lake;
                    }
                    else if (d < 1 + 3f / MathF.Min(l.rx, l.rz)) { _h[i] = Math.Min(_h[i], Ground); _lie[i] |= Lie.Shore; }
                }
                var c = CellOfColumn(x, z);
                if (c is { Kind: ValleyKind.Marsh } && _dRoad[i] > 3 && (_lie[i] & Lie.Peak) == 0 && Fbm(_n3, x / 9f, z / 9f) > .62f)
                {
                    var (cx, cz) = Center(c);
                    if (Dist(x, z, cx, cz) > 24 && _wet[i] == 0) { _h[i] = WaterLevel - 1; _wet[i] = WaterLevel; _lie[i] |= Lie.Marsh; }
                }
                float dr = _dRiver[i];
                var rc = c ?? CellAt(x, z);
                float half = rc is { Kind: ValleyKind.Marsh } ? 2.7f : 1.6f;
                if (dr < half)
                {
                    float t = dr / half;
                    bool ford = _dRoad[i] < 2.2f;
                    _h[i] = Math.Min(_h[i], ford ? WaterLevel - 1 : WaterLevel - 1 - (int)MathF.Round(2.4f * (1 - t * t)));
                    _wet[i] = WaterLevel;
                    _lie[i] |= Lie.River | (ford ? Lie.Ford : 0);
                }
                else if (dr < half + 7)
                {
                    _h[i] = Math.Min(_h[i], WaterLevel + 1 + (int)MathF.Floor((dr - half) * 1.1f));
                    if (dr < half + 1.6f) _lie[i] |= Lie.Shore;
                }
                if (_dMain[i] < 1.6f || _dRoad[i] < .9f) _lie[i] |= Lie.Road | (_wet[i] > _h[i] ? Lie.Ford : 0);
            }
        // (a pool or a lake's edge is ground at least as high as the water about it, so that none runs off)
        for (int z = 1; z < SZ - 1; z++)
            for (int x = 1; x < SX - 1; x++)
            {
                int i = z * SX + x;
                if (_wet[i] > 0) continue;
                for (int d = 0; d < 4; d++)
                {
                    int j = i + (d == 0 ? 1 : d == 1 ? -1 : d == 2 ? SX : -SX);
                    if (_wet[j] > 0 && _h[i] < _wet[j]) { _h[i] = WaterLevel; _wet[i] = WaterLevel; break; }
                }
            }
        // the sea, off the field where the land falls below the water
        for (int i = 0; i < _h.Length; i++)
            if (_cellOf[i] < 0 && _h[i] < WaterLevel) _wet[i] = WaterLevel;
    }

    /// <summary>The ground about a point brought to a height (NaN: to its own, smoothed), going over into the land about over a rim; the peaks there let down.</summary>
    void Level(float[] hf, float[] peak, float x, float z, float r, float rim, float to)
    {
        int x0 = Math.Max(0, (int)(x - r - rim)), x1 = Math.Min(SX - 1, (int)(x + r + rim)), z0 = Math.Max(0, (int)(z - r - rim)), z1 = Math.Min(SZ - 1, (int)(z + r + rim));
        float level = float.IsNaN(to) ? hf[Math.Clamp((int)z, 0, SZ - 1) * SX + Math.Clamp((int)x, 0, SX - 1)] : to;
        for (int zz = z0; zz <= z1; zz++)
            for (int xx = x0; xx <= x1; xx++)
            {
                float d = Dist(xx + .5f, zz + .5f, x, z);
                float w = 1 - Smooth((d - r) / rim);
                if (w <= 0) continue;
                int i = zz * SX + xx;
                hf[i] += (level - hf[i]) * w;
                peak[i] *= 1 - w;
            }
    }

    // ------------------------------------------------------------ what the ground is

    void Grounds()
    {
        for (int z = 0; z < SZ; z++)
            for (int x = 0; x < SX; x++)
            {
                int i = z * SX + x;
                _top[i] = GroundOf(x, z, i);
            }
    }

    byte GroundOf(int x, int z, int i)
    {
        int h = _h[i];
        var lie = _lie[i];
        float n = Fbm(_n3, x / 7f, z / 7f), m = Fbm(_n1, x / 19f + 50, z / 19f);
        if (_wet[i] > h)
        {
            if ((lie & Lie.Ford) != 0) return Mat.Gravel;
            // the bed: dark mud and stones, sand only in the shallows of a lake's or the sea's shore
            if ((lie & (Lie.Marsh | Lie.River)) != 0) return n < .5f ? Mat.Soil : Mat.Gravel;
            return _wet[i] - h <= 1 ? Mat.Sand : n < .55f ? Mat.Soil : Mat.Rock;
        }
        if ((lie & Lie.Road) != 0) return _dMain[i] < 1.6f ? Mat.Dust : Mat.Trodden;
        if (_dMain[i] < 2.1f && Hash.H3(x, z, 61) < .4) return Mat.Dust;
        foreach (var f in _fields)
        {
            if (MathF.Abs(x - f.x) > f.hx + f.hz || MathF.Abs(z - f.z) > f.hx + f.hz) continue;
            float dx = x + .5f - f.x, dz = z + .5f - f.z, co = MathF.Cos(f.turn), si = MathF.Sin(f.turn);
            float u = dx * co + dz * si, v = -dx * si + dz * co;
            if (MathF.Abs(u) > f.hx || MathF.Abs(v) > f.hz) continue;
            _lie[i] |= Lie.Field;
            int row = (int)MathF.Floor(v + 100);
            return f.kind switch
            {
                0 => row % 3 == 2 ? Mat.Hay : Mat.Wheat,
                1 => row % 2 == 0 ? Mat.GrassLush : Mat.Soil,
                _ => row % 2 == 0 ? Mat.Soil : Mat.Rammed,
            };
        }
        // steep, or up a peak: rock, snow on the heights
        int steep = 0;
        for (int d = 0; d < 4; d++)
        {
            int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
            if ((uint)nx < (uint)SX && (uint)nz < (uint)SZ) steep = Math.Max(steep, Math.Abs(_h[nz * SX + nx] - h));
        }
        if (h > Ground + (OnField(x, z) ? 38 : 46) + n * 6) return Mat.Snow;
        if ((lie & Lie.Peak) != 0 || steep >= 3) return m < .35f ? Mat.StoneDark : steep >= 4 || n > .4f ? Mat.Rock : Mat.Moss;
        var c = CellOfColumn(x, z);
        // sand on the shores of the lakes and of the sea; the marshes' pools and the streams have grass to their edge
        if ((lie & Lie.Shore) != 0 && _dRiver[i] > 3 && c is not { Kind: ValleyKind.Marsh } || c == null && h <= WaterLevel + 1 && NearWater(x, z, 2)) return Mat.Sand;
        if (c == null) return h > Ground + 6 ? (n < .5f ? Mat.GrassDry : Mat.Rock) : n < .5f ? Mat.GrassDry : Mat.Grass;
        return c.Kind switch
        {
            ValleyKind.Marsh => n < .3f ? Mat.Moss : Mat.GrassLush,
            ValleyKind.Forest => n < .25f ? Mat.Moss : m < .45f ? Mat.GrassLush : Mat.Grass,
            ValleyKind.Ore => n < .28f ? Mat.Rock : m < .5f ? Mat.GrassDry : Mat.Grass,
            ValleyKind.Mountain => n < .3f ? Mat.GrassDry : Mat.Grass,
            ValleyKind.Sacred => m < .4f ? Mat.GrassLush : Mat.Grass,
            _ => m < .35f ? Mat.GrassLush : Mat.Grass,
        };
    }

    bool NearWater(int x, int z, int r)
    {
        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int nx = x + dx, nz = z + dz;
                if ((uint)nx < (uint)SX && (uint)nz < (uint)SZ && _wet[nz * SX + nx] > _h[nz * SX + nx]) return true;
            }
        return false;
    }
}
