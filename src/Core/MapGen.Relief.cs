using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoxelTower.Core;

/// <summary>
/// A side of a hexagonal valley as the map of valleys will know it, so that two neighbours meet along it: the heights
/// along the side are made from its seed alone, and a river or a road that crosses it crosses at the same place on both.
/// The neighbour across side i is across its side (i + 3) % 6, which runs the other way: both read the side from the
/// same end (<see cref="MapGen.EdgeHeight"/>), and its crossings at 1 − t.
/// </summary>
public sealed class HexEdge
{
    /// <summary>The seed the side's heights are made from: the same for the two valleys that share it.</summary>
    public uint Seed;
    /// <summary>Where along the side (0…1 from its first corner, round by the east) a river crosses it; below 0, none.</summary>
    public float River = -1;
    /// <summary>Where along it a road crosses; below 0, none.</summary>
    public float Road = -1;
}

/// <summary>
/// The ground of a valley as it was laid out, column by column, for what is laid into it afterwards (the ore under the
/// ground, by a part of the game of its own: <c>MapGen.AfterRelief</c>): what kind of ground each column is, how deep its
/// soil lies over the rock, where water stands, how wet it is.
/// </summary>
public sealed class Relief
{
    /// <summary>The kinds of ground: the lowland, the citadel's plateau and its rim, the ramps up to it, the roads,
    /// rocky knolls, the river's bed, the plateau's ponds, the river's banks, the lowland's lakes, and bare crags.</summary>
    public const byte Low = 0, Plateau = 1, Rim = 2, Ramp = 3, Road = 4, Knoll = 5, River = 6, Pond = 7, Bank = 8, Lake = 9, Crag = 10;
    public int SX, SZ;
    /// <summary>The kind of ground of each column (index z·SX + x).</summary>
    public byte[] Kind;
    /// <summary>How deep the soil lies over the rock in each column (voxels); 0 where the rock is bare.</summary>
    public byte[] Soil;
    /// <summary>How high water stands in each column; 0 where it is dry.</summary>
    public short[] Water;
    /// <summary>How wet each column's ground is, 0…1 or so (null for the old valley of the box).</summary>
    public float[] Moist;
}

public static partial class MapGen
{
    /// <summary>
    /// Called once a valley's ground is laid, before its named places, rocks and trees: what is laid into the ground
    /// itself — the ore under it — goes here, implemented in a file of its own.
    /// </summary>
    static partial void AfterRelief(VoxelWorld w, Site site, MapParams p, Relief relief);

    /// <summary>
    /// The height a side of a hexagonal valley comes to at a part t (0…1) of the way along it, made from the side's seed
    /// alone (not the valley's kind), so two neighbours on the map meet without a step: both read it from the same end.
    /// At the corners, where three valleys meet, every side comes down to the same height.
    /// </summary>
    public static double EdgeHeight(ValueNoise n, int side, double t, double len)
    {
        double u = side < 3 ? t : 1 - t, ends = Site.Smooth01(u / .12) * Site.Smooth01((1 - u) / .12);
        return 44 + ((n.Sample(u * len * .004 + 3.3, .5) - .5) * 30 + (n.Sample(u * len * .015 + 8.1, 7.5) - .5) * 8) * ends;
    }

    /// <summary>The seed of a side: the map's, or one made from the valley's own seed when it stands by itself.</summary>
    static uint EdgeSeed(MapParams p, int side) => p.Edges?[side] is { } e ? e.Seed : p.Seed * 7919u + (uint)side * 104729u + 17;

    /// <summary>
    /// A valley made by itself on a hexagon (the game's): the citadel stands in the middle on a plateau whose rim winds
    /// round it with cliffs and ramps down to the lowland; the lowland rolls in hills and long ridges, breaks into crags and
    /// terraced bluffs, and comes to the heights of its sides that a neighbour on the map would share; a river comes in
    /// over one side and goes out over another, round the plateau, and a stream from a third side joins it; lakes lie in
    /// the hollows; the woods grow where the ground is wet and thin out up high; roads come in from the side the horde
    /// comes by and go out at the far one. The kind of valley steers it all (<see cref="Valleys.GroundOf"/>).
    /// </summary>
    static Site GenerateHex(VoxelWorld w, MapParams p)
    {
        int SX = w.SX, SZ = w.SZ, N = SX * SZ;
        uint seed = p.Seed;
        var hex = Hex.In(SX, SZ);
        w.SetLand(hex);
        var ground = Valleys.GroundOf(p.Kind);
        var places = Valleys.PlacesOf(p.Kind, p.Classic);
        int entry = ((p.EntrySide % 6) + 6) % 6;
        var site = new Site
        {
            HTop = new int[N], KeepR = 24, KeepX = (int)hex.CX, KeepZ = (int)hex.CZ, Design = new VoxelWorld(SX, w.SY, SZ),
            SX = SX, SZ = SZ, Hex = hex, Kind = p.Kind, Classic = p.Classic, EntrySide = entry, Edges = p.Edges,
        };
        if (entry == Hex.West)
        {
            site.SpawnStrip = (int)hex.WestX + Site.StripDepth;
            site.WestZ0 = (int)hex.WestZ0 + 24;
            site.WestZ1 = (int)hex.WestZ1 - 24;
        }
        else { site.SpawnStrip = 0; site.WestZ0 = 0; site.WestZ1 = SZ; }
        var rnd = new Mulberry32(seed * 2654435761u + 977);
        double Rand() => rnd.Next();
        float CX = hex.CX, CZ = hex.CZ, R = hex.R;

        var nMacro = new ValueNoise(seed * 11 + 101); var nWarp = new ValueNoise(seed * 13 + 102); var nHill = new ValueNoise(seed * 17 + 103);
        var nFine = new ValueNoise(seed * 19 + 104); var nRidge = new ValueNoise(seed * 23 + 105); var nKnoll = new ValueNoise(seed * 29 + 106);
        var nSoil = new ValueNoise(seed * 31 + 107); var nForest = new ValueNoise(seed * 37 + 108); var nMoist = new ValueNoise(seed * 41 + 109);
        var nBiome = new ValueNoise(seed * 43 + 110); var nBloom = new ValueNoise(seed * 47 + 111); var nRim = new ValueNoise(seed * 53 + 112);
        var nTerr = new ValueNoise(seed * 59 + 113); var nPond = new ValueNoise(seed * 61 + 114); var nPath = new ValueNoise(seed * 67 + 115);
        var nEdge = new ValueNoise[6];
        for (int i = 0; i < 6; i++) nEdge[i] = new ValueNoise(EdgeSeed(p, i));
        float sideLen = R;

        const int baseH = 40;
        double rise = 44 * ground.Lift;
        double Smooth(double t) => Site.Smooth01(t);

        // ---- the plateau: its rim winds round the citadel, as far out as the valley allows
        double Rb = Math.Min(300, R * .42);
        // toward the side the horde comes by, the plateau ends in a long cliff square across the way in, some 28 m out
        // from the citadel and wandering in and out, the lowland below it: as it always did on the west
        var (eix, eiz) = hex.Inward(entry);
        double towardEntry = Math.Atan2(-eiz, -eix), cliff = Math.Min(225, Rb * .75);
        double RimAt(double ang)
        {
            double c = Math.Cos(ang), s = Math.Sin(ang);
            double r = Rb * (1 + (nRim.Sample(c * 1.05 + 5, s * 1.05 + 5) - .5) * .55 + (nRim.Sample(c * 2.6 + 19, s * 2.6 + 19) - .5) * .22
                + (nRim.Sample(c * 7 + 31, s * 7 + 31) - .5) * .06);
            double toward = Math.Cos(ang - towardEntry), across = Math.Sin(ang - towardEntry);
            if (toward < .05) return r;
            // where along the cliff a way at this angle meets it, and how far out the cliff stands there
            double lateral = across / toward;
            double line = cliff + (nRim.Sample(lateral * 1.1 + 47, 3.7) - .5) * 70 + (nRim.Sample(lateral * 4 + 61, 9.1) - .5) * 14;
            return Math.Min(r, line / toward);
        }
        double rpMax = Rb * 1.45 + 4;
        double Plat(double x, double z)
        {
            double dk = Brushes.Dist(x - CX, z - CZ);
            double roll = Smooth((dk - 170) / 120) * ((nHill.Sample(x * .013 + 30, z * .013) - .5) * 14);
            return baseH + rise + (nHill.Sample(x * .02 + 40, z * .02 + 9) - .5) * 3 + roll;
        }

        // ---- the ramps: two toward the side the horde comes by (one either side of the way in), one to the far side
        var (ix, iz) = hex.Inward(entry);
        double toEntry = Math.Atan2(-iz, -ix);
        double rampLen = Math.Min(150, Rb * .5), rampHalf = 26;
        bool firstNorth = Rand() < .5;
        var ramps = new[] { toEntry + (firstNorth ? -.75 : .75), toEntry + (firstNorth ? .75 : -.75), toEntry + Math.PI + (Rand() - .5) * .5 };
        var rampRim = new double[ramps.Length];
        for (int k = 0; k < ramps.Length; k++) rampRim[k] = RimAt(ramps[k]);
        (double x, double z) Dir(double a) => (Math.Cos(a), Math.Sin(a));
        (double x, double z) AtAngle(double a, double r) => (CX + Math.Cos(a) * r, CZ + Math.Sin(a) * r);

        // ---- lines through the valley: roads and the river, as polylines, smoothed
        static List<(double x, double z)> Chaikin(List<(double x, double z)> pts, int times)
        {
            for (int t = 0; t < times; t++)
            {
                var o = new List<(double x, double z)> { pts[0] };
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var (ax, az) = pts[i]; var (bx, bz) = pts[i + 1];
                    o.Add((ax * .75 + bx * .25, az * .75 + bz * .25));
                    o.Add((ax * .25 + bx * .75, az * .25 + bz * .75));
                }
                o.Add(pts[^1]);
                pts = o;
            }
            return pts;
        }
        // a point kept off the plateau by so much beyond its rim, and in from the land's edge by so much
        (double x, double z) Keep(double x, double z, double clear, double inland)
        {
            double dx = x - CX, dz = z - CZ, d = Math.Sqrt(dx * dx + dz * dz), avoid = RimAt(Math.Atan2(dz, dx)) + clear;
            if (d < avoid && d > 1e-3) { x = CX + dx / d * avoid; z = CZ + dz / d * avoid; }
            if (inland > 0 && hex.Edge((float)x, (float)z) < inland)
            {
                // in toward the middle along the way to it, but not back onto the plateau
                for (int s = 0; s < 400 && hex.Edge((float)x, (float)z) < inland; s++)
                {
                    double ex = CX - x, ez = CZ - z, el = Math.Max(1, Math.Sqrt(ex * ex + ez * ez));
                    x += ex / el * 2; z += ez / el * 2;
                    if (el - 2 < avoid) break;
                }
            }
            return (x, z);
        }
        // a winding line from a to b: bent aside by noise and a slow swing, the bend dying away at the ends
        List<(double x, double z)> Wind(double ax, double az, double bx, double bz, int n, double amp, double phase, double avoid, double inland, double salt)
        {
            double dx = bx - ax, dz = bz - az, l = Math.Max(1, Math.Sqrt(dx * dx + dz * dz)), px = -dz / l, pz = dx / l;
            var pts = new List<(double x, double z)>();
            for (int i = 0; i <= n; i++)
            {
                double u = (double)i / n, env = Math.Sin(u * Math.PI);
                double off = (Math.Sin(u * Math.PI * 3 + phase) * .6 + (nPath.Sample(u * 5 + salt, salt * .7) - .5) * 1.6) * amp * env;
                var (x, z) = (ax + dx * u + px * off, az + dz * u + pz * off);
                // the ends stay where they are put; between them the line keeps its distance from the plateau and the edge
                if (i > 0 && i < n) (x, z) = Keep(x, z, avoid, inland * Math.Min(1, env * 3));
                pts.Add((x, z));
            }
            return Chaikin(pts, 3);
        }
        // the line carried on beyond an end, out of the box, winding a little: so the far land can lay it on
        static void Beyond(List<(double x, double z)> pts, bool atStart, double dx, double dz, double far, ValueNoise n, double salt)
        {
            var (ox, oz) = atStart ? pts[0] : pts[^1];
            var o = new List<(double x, double z)>();
            for (double s = 48; s <= far; s += 48)
            {
                double w = (n.Sample(s * .002 + salt, salt) - .5) * 160 * Math.Min(1, s / 600);
                o.Add((ox + dx * s - dz * w, oz + dz * s + dx * w));
            }
            if (atStart) { o.Reverse(); pts.InsertRange(0, o); }
            else pts.AddRange(o);
        }

        // ---- the river: in over one side and out over another, round the plateau; on the horde's half mostly
        bool near = Rand() < .72;
        int rIn = near ? (entry + 1) % 6 : (entry + 2) % 6, rOut = near ? (entry + 5) % 6 : (entry + 4) % 6;
        double tIn = .3 + Rand() * .4, tOut = .3 + Rand() * .4;
        if (p.Edges != null)
        {
            // the map says where rivers cross: the first two sides it has one on
            var sides = new List<int>();
            for (int i = 0; i < 6; i++) if (p.Edges[i]?.River >= 0) sides.Add(i);
            if (sides.Count >= 2) { rIn = sides[0]; rOut = sides[1]; tIn = p.Edges[rIn].River; tOut = 1 - p.Edges[rOut].River; }
        }
        // (a long way off the cliff toward the horde's side, where the lowland under it is worked and fought over)
        double riverAvoid = 290;
        var (ax0, az0) = hex.SidePoint(rIn, (float)tIn); var (bx0, bz0) = hex.SidePoint(rOut, (float)tOut);
        var river = Wind(ax0, az0, bx0, bz0, 44, 80, Rand() * 6, riverAvoid, 90, 3.1);
        var (ri, rk) = hex.Inward(rIn); var (ro, rq) = hex.Inward(rOut);
        Beyond(river, true, -ri, -rk, 6200, nPath, 21.7);
        Beyond(river, false, -ro, -rq, 6200, nPath, 33.9);
        var riverHalf = new double[river.Count];
        for (int i = 0; i < river.Count; i++) riverHalf[i] = (15 + nPath.Sample(i * .09 + 50, 2.5) * 12) * ground.River;

        // ---- a stream from a third side joins the river, round the plateau's other flank
        int sStream = near ? (entry + 3) % 6 : entry;
        double tStream = .35 + Rand() * .3;
        // on the map of valleys the stream is the valley's own: in by a side no road or river of the map crosses, and
        // clear of its middle, where parties walk in
        bool mapped = false;
        for (int i = 0; i < 6 && p.Edges != null; i++) mapped |= p.Edges[i]?.Road >= 0 || p.Edges[i]?.River >= 0;
        if (mapped)
        {
            for (int k = 0; k < 6; k++)
            {
                int s2 = (sStream + k) % 6;
                if (!(p.Edges[s2]?.Road >= 0) && !(p.Edges[s2]?.River >= 0)) { sStream = s2; break; }
            }
            tStream += tStream < .5 ? -.2 : .2;
        }
        var (sx0, sz0) = hex.SidePoint(sStream, (float)tStream);
        // where it meets the river: a point of the river's course inside the land, a good way down it
        int inside0 = -1, inside1 = -1;
        for (int i = 0; i < river.Count; i++) if (hex.Edge((float)river[i].x, (float)river[i].z) > 30) { if (inside0 < 0) inside0 = i; inside1 = i; }
        var junction = river[inside0 + (int)((inside1 - inside0) * (.55 + Rand() * .2))];
        var stream = Wind(sx0, sz0, junction.x, junction.z, 36, 70, Rand() * 6, 110 + Rand() * 90, 60, 7.7);
        var (si, sk) = hex.Inward(sStream);
        Beyond(stream, true, -si, -sk, 2400, nPath, 44.4);

        // ---- the roads: in from the horde's side up its ramp to the citadel's gate, and out from the far ramp to the far side
        double tRoad = p.Edges?[entry]?.Road >= 0 ? p.Edges[entry].Road : .4 + Rand() * .2;
        var (pex, pez) = hex.SidePoint(entry, (float)tRoad);
        var (fx, fz) = AtAngle(ramps[0], rampRim[0] + rampLen + 18);
        var road1 = Wind(pex, pez, fx, fz, 24, 36, Rand() * 6, 20, 30, 11.3);
        // up the ramp and over the plateau to the gate on the citadel's western side
        var (rmx, rmz) = AtAngle(ramps[0], rampRim[0] - 30);
        road1.Add((rmx, rmz));
        double gx = site.KeepX - site.KeepR - 26, gz = site.KeepZ;
        if (Math.Cos(ramps[0]) > -.2)
        {
            // coming up east of the citadel: round it to the gate
            double sideZ = Math.Sin(ramps[0]) >= 0 ? 1 : -1;
            road1.Add((CX, CZ + sideZ * (site.KeepR + 56)));
        }
        road1.Add((gx, gz));
        road1 = Chaikin(road1, 1);
        Beyond(road1, true, -ix, -iz, 6200, nPath, 55.5);
        int far = (entry + 3) % 6;
        double tFar = p.Edges?[far]?.Road >= 0 ? 1 - p.Edges[far].Road : .4 + Rand() * .2;
        var (ex2, ez2) = hex.SidePoint(far, (float)tFar);
        var (f2x, f2z) = AtAngle(ramps[2], rampRim[2] + rampLen + 18);
        var road2 = Wind(f2x, f2z, ex2, ez2, 24, 36, Rand() * 6, 20, 30, 13.9);
        var (r2x, r2z) = AtAngle(ramps[2], rampRim[2] - 40);
        road2.Insert(0, (r2x, r2z));
        var (fi, fk) = hex.Inward(far);
        Beyond(road2, false, -fi, -fk, 6200, nPath, 66.6);

        // ---- how far each column is from those lines: stamped segment by segment, within reach of each
        var riverD = new float[N]; var riverH = new float[N]; var streamD = new float[N]; var roadD = new float[N];
        Array.Fill(riverD, 1e6f); Array.Fill(streamD, 1e6f); Array.Fill(roadD, 1e6f);
        // (row by row on all the cores: a row's columns are nobody else's, and the nearest is the nearest whichever
        // segment is looked at first, so the valley comes out the same)
        void Stamp(List<(double x, double z)> line, double[] halves, double reach, float[] dist, float[] half)
        {
            int n = line.Count - 1;
            if (n <= 0) return;
            var box = new (int x0, int x1, int z0, int z1)[n];
            for (int i = 0; i < n; i++)
            {
                var (ax, az) = line[i]; var (bx, bz) = line[i + 1];
                double rr = reach + Math.Max(halves?[i] ?? 0, halves?[i + 1] ?? 0);
                box[i] = (Math.Max(0, (int)(Math.Min(ax, bx) - rr)), Math.Min(SX - 1, (int)(Math.Max(ax, bx) + rr)),
                    Math.Max(0, (int)(Math.Min(az, bz) - rr)), Math.Min(SZ - 1, (int)(Math.Max(az, bz) + rr)));
            }
            Parallel.For(0, SZ, z =>
            {
                for (int i = 0; i < n; i++)
                {
                    var (x0, x1, z0, z1) = box[i];
                    if (z < z0 || z > z1 || x0 > x1) continue;
                    var (ax, az) = line[i]; var (bx, bz) = line[i + 1];
                    double h0 = halves?[i] ?? 0, h1 = halves?[i + 1] ?? 0;
                    double ex = bx - ax, ez = bz - az, ee = Math.Max(1e-9, ex * ex + ez * ez);
                    for (int x = x0; x <= x1; x++)
                    {
                        double t = Math.Clamp(((x + .5 - ax) * ex + (z + .5 - az) * ez) / ee, 0, 1);
                        double px = ax + ex * t - x - .5, pz = az + ez * t - z - .5, d = Math.Sqrt(px * px + pz * pz);
                        int c = z * SX + x;
                        if (d < dist[c]) { dist[c] = (float)d; if (half != null) half[c] = (float)(h0 + (h1 - h0) * t); }
                    }
                }
            });
        }
        Stamp(river, riverHalf, 150, riverD, riverH);
        Stamp(stream, null, 60, streamD, null);
        Stamp(road1, null, 40, roadD, null);
        Stamp(road2, null, 40, roadD, null);
        double streamHalf = 5 + 2 * ground.River;

        // a ford on the river where it is shallow, a third of the way down it inside the land
        var fordAt = river[inside0 + (inside1 - inside0) / 3];

        // ---- the plateau's two ponds, either side of the citadel across the way in, a good way out (the ground 18 m round
        // the citadel left level and dry to build on) and well in from the rim: smaller rather than nearer where it is tight
        const int shore = 5;
        var ponds = new (int x, int z, int r, int level)[2];
        double Reach(int n, int pr, double dx, double dz)
        {
            double a = Math.Atan2(dz, dx);
            return pr * (.78 + .44 * nPond.Sample(Math.Cos(a) * 1.4 + n * 9 + 4, Math.Sin(a) * 1.4 + n * 5 + 4));
        }
        for (int n = 0; n < 2; n++)
        {
            double a = toEntry + (n == 0 ? -1 : 1) * (Math.PI / 2 + (Rand() - .5) * .5);
            int pr = (int)(Rb * (.13 + Rand() * .04));
            double dist = Rb * (.74 + Rand() * .06);
            int px = 0, pz = 0, span = 0;
            for (int tries = 0; tries < 40; tries++)
            {
                span = (int)(pr * 1.22) + shore;
                px = (int)(CX + Math.Cos(a) * dist); pz = (int)(CZ + Math.Sin(a) * dist);
                // the whole of it on the plateau, a margin in from the rim all round it
                bool fits = true;
                for (int k = 0; k < 16 && fits; k++)
                {
                    double b = k * Math.PI / 8, ex = px + Math.Cos(b) * (span + 18), ez = pz + Math.Sin(b) * (span + 18);
                    if (Brushes.Dist(ex - CX, ez - CZ) > RimAt(Math.Atan2(ez - CZ, ex - CX)) - 12) fits = false;
                }
                if (fits) break;
                if (pr > 24) pr -= 3; else dist -= 6;
            }
            int level = int.MaxValue;
            for (int z = pz - span; z <= pz + span; z++)
                for (int x = px - span; x <= px + span; x++)
                {
                    double d = Brushes.Dist(x - px, z - pz), r = Reach(n, pr, x - px, z - pz);
                    if (d >= r && d < r + shore) level = Math.Min(level, (int)Math.Round(Plat(x, z)) - 1);
                }
            ponds[n] = (px, pz, pr, level);
            site.Ponds.Add((px, pz, pr, level));
        }

        // ---- the heights, column by column
        var kind = new byte[N];
        var soil = new byte[N];
        var water = new short[N];
        var moist = new float[N];
        double Ramp(double x, double z, out double along, out int which)
        {
            double best = 1e9; along = 0; which = -1;
            for (int k = 0; k < ramps.Length; k++)
            {
                var (ux, uz) = Dir(ramps[k]);
                double al = (x - CX) * ux + (z - CZ) * uz, lat = Math.Abs(-(x - CX) * uz + (z - CZ) * ux) - rampHalf;
                if (al > 0 && lat < best) { best = lat; along = al; which = k; }
            }
            return best;
        }
        Parallel.For(0, SZ, z =>
        {
            for (int x = 0; x < SX; x++)
            {
                int i = z * SX + x;
                float edgeIn = hex.Edge(x + .5f, z + .5f);
                if (edgeIn < -2) { site.HTop[i] = baseH; continue; }
                // the lowland: broad swells, warped so they wind, long ridges, hills and a little roughness
                double wx = x + (nWarp.Sample(x * .0021, z * .0021) - .5) * 220, wz = z + (nWarp.Sample(x * .0021 + 40, z * .0021 + 40) - .5) * 220;
                double macro = nMacro.Sample(wx * .0017, wz * .0017) * .65 + nMacro.Sample(wx * .0043 + 9, wz * .0043 + 9) * .35;
                double ridge = 1 - Math.Abs(nRidge.Sample(wx * .0058, wz * .0058) * 2 - 1);
                double low = baseH + (macro - .5) * 52 * ground.Relief + (nHill.Sample(x * .010, z * .010) - .5) * 22
                    + Math.Pow(ridge, 3) * 26 * ground.Ridges + (nFine.Sample(x * .11, z * .11) - .5) * p.Roughness;
                // bluffs: the higher ground breaks in steps here and there, a cliff to each step
                double terr = nTerr.Sample(x * .0045, z * .0045);
                if (terr > .56 && low > baseH + 6)
                {
                    double step = 11, f = (low - baseH) / step, fl = Math.Floor(f), fr = f - fl;
                    double stepped = baseH + (fl + Smooth((fr - .72) / .28)) * step;
                    low += (stepped - low) * Smooth((terr - .56) / .08);
                }
                low = Math.Clamp(low, MapGen.RiverLevel + 3, baseH + 34 + 26 * ground.Ridges);
                // toward the sides, the heights the neighbours share: on a side itself, its own height and nothing else
                double wsum = 0, esum = 0, dmin = 1e9;
                for (int s = 0; s < 6; s++)
                {
                    var (d, t) = hex.FromSide(s, x + .5f, z + .5f);
                    if (d > 170 || t < -.05 || t > 1.05) continue;
                    d = Math.Max(0, d);
                    double wt = 1 / ((d + 1) * (d + 1));
                    wsum += wt; dmin = Math.Min(dmin, d);
                    esum += wt * EdgeHeight(nEdge[s], s, Math.Clamp(t, 0, 1), sideLen);
                }
                if (wsum > 0) low += (esum / wsum - low) * Smooth(1 - dmin / 170);

                double dk = Brushes.Dist(x - CX, z - CZ), ang = Math.Atan2(z - CZ, x - CX), rp = RimAt(ang);
                double rampD = Ramp(x, z, out double along, out int which);
                double h = low; byte k = Relief.Low;
                if (dk < rp)
                {
                    h = Plat(x, z);
                    k = dk > rp - 5 && rampD > 0 ? Relief.Rim : Relief.Plateau;
                }
                else
                {
                    // rocky knolls out in the lowland, kept off the roads, the ramps and the horde's side
                    double kn = nKnoll.Sample(x * .018, z * .018);
                    if (kn > ground.Knoll)
                    {
                        double fade = Smooth((roadD[i] - 6) / 26) * Smooth(rampD / 30) * Smooth((edgeIn - 30) / 50) * Smooth((dk - rp - 40) / 60);
                        double hill = (kn - ground.Knoll) * 190 * ground.KnollHeight * fade;
                        if (hill > 1.5) { h = low + hill; k = Relief.Knoll; }
                    }
                    // the ramps up to the plateau, their sides sloping down to the lowland
                    if (which >= 0 && along < rampRim[which] + rampLen && rampD < 34)
                    {
                        double plat = Plat(x, z);
                        double r = low + (plat - low) * Smooth((rampRim[which] + rampLen - along) / rampLen);
                        double side = rampD <= 0 ? 1 : 1 - Smooth(rampD / 34);
                        double hr = h + (r - h) * side;
                        if (r > h && hr > h) { h = hr; k = rampD <= 0 ? Relief.Ramp : k; }
                    }
                }
                if (roadD[i] < 6 && k is not (Relief.Ramp or Relief.Rim)) k = Relief.Road;
                short wl = 0;
                // the river: banks that slope down to the water, a bed that drops away from them; shallow at the ford
                double rd = riverD[i], rh = riverH[i];
                // (on the map of valleys a road fords the river where it crosses, so that every side of the valley is to be
                // got to and gone out by: the bed shallow along the road and the banks gentle)
                bool forded = mapped && roadD[i] < 28;
                if (k is not (Relief.Plateau or Relief.Rim) || rd < rh + 20)
                {
                    if (rd < rh)
                    {
                        double t = rd / rh, deep = 3 + (1 - t * t) * 13;
                        double ford = Math.Min(Brushes.Dist(x - fordAt.x, z - fordAt.z), mapped ? roadD[i] : 1e9);
                        h = MapGen.RiverLevel - (2 + (deep - 2) * Smooth((ford - 8) / 16));
                        k = Relief.River; wl = MapGen.RiverLevel;
                    }
                    else if (rd < rh + 140)
                    {
                        double bank = forded ? MapGen.RiverLevel + 2 + (rd - rh) * .2 : MapGen.RiverLevel + 2 + (rd - rh) * .42 + Math.Max(0, rd - rh - 30) * .95;
                        if (h > bank) { h = bank; if (k is Relief.Low or Relief.Knoll) k = Relief.Bank; }
                        h = Math.Max(h, MapGen.RiverLevel + 1 - Math.Max(0, rd - rh - 40) * .25);
                    }
                }
                // the stream: a narrow, shallow channel at the river's level, wading depth, in a little ravine
                double sd = streamD[i];
                if (sd < streamHalf + 60 && k is not (Relief.Plateau or Relief.Rim or Relief.River))
                {
                    // (on the map of valleys waded anywhere, its ravine gentle: it cuts nothing off)
                    if (sd < streamHalf) { h = mapped ? MapGen.RiverLevel - 1 - (1 - sd / streamHalf) : MapGen.RiverLevel - 2 - (1 - sd / streamHalf) * 2; k = Relief.River; wl = MapGen.RiverLevel; }
                    else
                    {
                        double bank = mapped ? MapGen.RiverLevel + 1 + (sd - streamHalf) * .2 : MapGen.RiverLevel + 1 + (sd - streamHalf) * .5 + Math.Max(0, sd - streamHalf - 14) * .8;
                        if (h > bank) { h = bank; if (k is Relief.Low or Relief.Knoll) k = Relief.Bank; }
                    }
                }
                // the plateau's ponds
                for (int n = 0; n < 2; n++)
                {
                    var (px, pz, pr, level) = ponds[n];
                    double d = Brushes.Dist(x - px, z - pz);
                    if (d >= pr * 1.22 + shore || k is not (Relief.Plateau or Relief.Rim)) continue;
                    double r = Reach(n, pr, x - px, z - pz);
                    if (d >= r + shore) continue;
                    if (d < r) { double t = d / r; h = level - 1 - (1 - t * t) * 19; k = Relief.Pond; wl = (short)level; }
                    else h = level + (h - level) * Smooth((d - r) / shore);
                }
                kind[i] = k;
                water[i] = edgeIn >= 0 ? wl : (short)0;
                site.HTop[i] = (int)Math.Round(h);
                // wet by the water and in the hollows, dry up high
                double wet = nMoist.Sample(x * .009, z * .009) + ground.Wet - (h - baseH) / 110.0
                    + .3 * Smooth(1 - (rd - rh) / 90) + .2 * Smooth(1 - (sd - streamHalf) / 50);
                moist[i] = (float)wet;
                soil[i] = (byte)(k switch
                {
                    Relief.Knoll or Relief.Rim => 0, Relief.Plateau or Relief.Pond => 7, Relief.River => 4,
                    _ => (int)((9 + nSoil.Sample(x * .02, z * .02) * 6) * ground.Soil),
                });
            }
        });

        // ---- lakes in the lowland's hollows, their water as high as the lowest of their shores
        var lakes = new List<(int x, int z, int r, int level)>();
        for (int n = 0, tries = 0; n < ground.Lakes && tries < 60; tries++)
        {
            int r = (int)(40 + Rand() * 42);
            double a = Rand() * Math.PI * 2, d = rpMax + r + 70 + Rand() * Math.Max(0, R - rpMax - r - 150);
            int lx = (int)(CX + Math.Cos(a) * d), lz = (int)(CZ + Math.Sin(a) * d);
            if (lx < r + 8 || lz < r + 8 || lx >= SX - r - 8 || lz >= SZ - r - 8) continue;
            int c0 = lz * SX + lx;
            if (hex.Edge(lx, lz) < r + 70 || riverD[c0] - riverH[c0] < r + 60 || streamD[c0] < r + 50 || roadD[c0] < r + 30) continue;
            bool clear = true;
            foreach (var l in lakes) if (Brushes.Dist(l.x - lx, l.z - lz) < l.r + r + 60) clear = false;
            if (!clear) continue;
            int level = int.MaxValue;
            for (int k = 0; k < 48; k++)
            {
                double b = k * Math.PI / 24, sx = lx + Math.Cos(b) * (r + shore), sz = lz + Math.Sin(b) * (r + shore);
                level = Math.Min(level, site.HTop[(int)sz * SX + (int)sx] - 1);
            }
            if (level <= MapGen.RiverLevel + 1) continue;
            lakes.Add((lx, lz, r, level));
            n++;
        }
        foreach (var (lx, lz, lr, level) in lakes)
        {
            int span = (int)(lr * 1.25) + shore;
            for (int z = Math.Max(0, lz - span); z <= Math.Min(SZ - 1, lz + span); z++)
                for (int x = Math.Max(0, lx - span); x <= Math.Min(SX - 1, lx + span); x++)
                {
                    int i = z * SX + x;
                    double d = Brushes.Dist(x - lx, z - lz), r = Reach(7, lr, x - lx, z - lz);
                    if (d >= r + shore || kind[i] is Relief.River or Relief.Plateau or Relief.Rim) continue;
                    if (d < r)
                    {
                        double t = d / r;
                        site.HTop[i] = (int)Math.Round(level - 1 - (1 - t * t) * 14);
                        kind[i] = Relief.Lake; water[i] = hex.Inside(x, z) ? (short)level : (short)0; soil[i] = 4;
                        moist[i] += .3f;
                    }
                    else
                    {
                        double h = site.HTop[i];
                        site.HTop[i] = (int)Math.Round(level + Math.Max(0, h - level) * Smooth((d - r) / shore));
                    }
                }
        }
        site.Lakes.AddRange(lakes);

        // ---- the ground bare where it is steep: the faces of the bluffs and the knolls' flanks are rock
        Parallel.For(1, SZ - 1, z =>
        {
            for (int x = 1; x < SX - 1; x++)
            {
                int i = z * SX + x;
                if (kind[i] is not (Relief.Low or Relief.Bank or Relief.Knoll)) continue;
                int h = site.HTop[i];
                int drop = Math.Max(Math.Max(h - site.HTop[i - 1], h - site.HTop[i + 1]), Math.Max(h - site.HTop[i - SX], h - site.HTop[i + SX]));
                if (drop >= 3) { kind[i] = Relief.Crag; soil[i] = 0; }
            }
        });

        // ---- the columns filled: bedrock, rock, soil, the turf on top; water where it stands; tufts and flowers
        Parallel.For(0, w.CZ, cz =>
        {
            for (int z = cz * VoxelWorld.CS; z < (cz + 1) * VoxelWorld.CS && z < SZ; z++)
                for (int x = 0; x < SX; x++)
                {
                    if (!w.OnLand(x, z)) continue;
                    int i = z * SX + x, h = site.HTop[i], s = Math.Min(soil[i], Math.Max(0, h - 4));
                    byte k = kind[i];
                    w.FillColumnRaw(x, z, 0, 3, Mat.Bed);
                    w.FillColumnRaw(x, z, 3, h - s, Mat.Rock);
                    w.FillColumnRaw(x, z, h - s, h - 1, Mat.Soil);
                    double m = moist[i];
                    byte grass = m < .22 ? Mat.GrassDry : m > .64 ? Mat.GrassLush : Mat.Grass;
                    byte top = k switch
                    {
                        Relief.Rim => Mat.Moss,
                        Relief.Ramp or Relief.Road => Mat.Path,
                        Relief.Knoll => Hash.H3(x, z, 41) < .35 ? Mat.Moss : Mat.Rock,
                        Relief.Crag => Hash.H3(x, z, 43) < .25 ? Mat.Moss : Mat.Rock,
                        Relief.River or Relief.Pond or Relief.Lake => Mat.Soil,
                        _ => grass,
                    };
                    w.FillColumnRaw(x, z, h - 1, h, top);
                    if (water[i] > h) w.FillColumnRaw(x, z, h, water[i], Mat.Water);
                    else if (Mat.IsGrass(top))
                    {
                        double bloom = nBloom.Sample(x * .03, z * .03);
                        if (top != Mat.GrassDry && bloom > .74 && Hash.H3(x, z, 79) < .035)
                            w.FillColumnRaw(x, z, h, h + 1, Hash.H3(x, z, 80) < .6 ? Mat.Flower : Mat.Bloom);
                        else if (Hash.H3(x, z, 77) < p.TuftChance * (top == Mat.GrassDry ? 2.2 : 1))
                        {
                            w.FillColumnRaw(x, z, h, h + 1, Mat.Tuft);
                            if (Hash.H3(x, z, 78) < .35) w.FillColumnRaw(x, z, h + 1, h + 2, Mat.Tuft);
                        }
                        else if (k is Relief.Low or Relief.Bank && nKnoll.Sample(x * .018, z * .018) > .6 && Hash.H3(x, z, 81) < .01)
                            w.FillColumnRaw(x, z, h, h + 1 + (Hash.H3(x, z, 82) < .3 ? 1 : 0), Mat.Rock);
                    }
                }
        });
        site.WaterTop = water;
        var relief = new Relief { SX = SX, SZ = SZ, Kind = kind, Soil = soil, Water = water, Moist = moist };
        AfterRelief(w, site, p, relief);

        // ---- the lines kept for the far land, the current and whoever asks where the river and the roads run
        site.River = new List<(float X, float Z, float Half)>(river.Count);
        for (int i = 0; i < river.Count; i++) site.River.Add(((float)river[i].x, (float)river[i].z, (float)riverHalf[i]));
        foreach (var road in new[] { road1, road2 })
        {
            var r = new List<(float X, float Z)>(road.Count);
            foreach (var (x, z) in road) r.Add(((float)x, (float)z));
            site.Roads.Add(r);
        }

        // ---- the named places, before the rocks and trees
        bool NearKeep(int x, int z, int margin) => Brushes.Dist(x - site.KeepX, z - site.KeepZ) < site.KeepR + margin;
        (int x, int z) Free(double fx0, double fz0, int r)
        {
            int x = (int)fx0, z = (int)fz0;
            bool Ok(int cx, int cz)
            {
                if (cx < r + 16 || cz < r + 16 || cx >= SX - r - 16 || cz >= SZ - r - 16 || hex.Edge(cx, cz) < r + 16 || NearKeep(cx, cz, 100 + r) || site.InStrip(cx, cz)) return false;
                foreach (var (px, pz, pr, _) in ponds) if (Brushes.Dist(cx - px, cz - pz) < pr + r + 16) return false;
                foreach (var (lx, lz, lr, _) in lakes) if (Brushes.Dist(cx - lx, cz - lz) < lr + r + 16) return false;
                foreach (var l in site.Landmarks) if (Brushes.Dist(cx - l.X, cz - l.Z) < l.R + r + 12) return false;
                for (int dz = -r; dz <= r; dz += 4)
                    for (int dx = -r; dx <= r; dx += 4)
                        if (dx * dx + dz * dz <= r * r && kind[(cz + dz) * SX + cx + dx] is Relief.River or Relief.Pond or Relief.Lake or Relief.Road or Relief.Ramp or Relief.Rim) return false;
                return true;
            }
            for (int ring = 0; ring <= 240; ring += 12)
                for (int a = 0; a < 360; a += ring == 0 ? 360 : 30)
                {
                    int cx = x + (int)(Math.Cos(a * Math.PI / 180) * ring), cz = z + (int)(Math.Sin(a * Math.PI / 180) * ring);
                    if (Ok(cx, cz)) return (cx, cz);
                }
            return (x, z);
        }
        // a point between the river and the plateau's rim: the lowland the horde crosses
        (double x, double z) Between(double share)
        {
            var (rx, rz) = river[inside0 + (int)((inside1 - inside0) * share)];
            double dx = CX - rx, dz = CZ - rz, l = Math.Max(1, Math.Sqrt(dx * dx + dz * dz)), rim = RimAt(Math.Atan2(rz - CZ, rx - CX));
            double gap = Math.Max(0, l - rim);
            return (rx + dx / l * gap * .5, rz + dz / l * gap * .5);
        }
        var copper = (x: 0, z: 0);
        if ((places & Places.Mines) != 0)
        {
            var (cx, cz) = AtAngle(toEntry + Math.PI * .85, Rb * .6);
            copper = Free(cx, cz, 30);
            Landmarks.Mine(w, site, copper.x, copper.z, "Медный рудник", 3, false, seed + 1);
        }
        if ((places & Places.MinersHouses) != 0 && (places & Places.Mines) != 0)
        {
            var houses = Free(copper.x - 52, copper.z + 46, 34);
            Landmarks.Houses(w, site, houses.x, houses.z, "Брошенные дома рудокопов", seed + 2);
        }
        if ((places & (Places.Mines | Places.Silver)) != 0)
        {
            var (sx, sz) = Between(.35);
            var silver = Free(sx, sz, 30);
            Landmarks.Mine(w, site, silver.x, silver.z, "Серебряная жила", 6, true, seed + 3);
        }
        var (gfx, gfz) = hex.SidePoint(entry, .3f, 100);
        var gold = ((int)gfx, (int)gfz);
        if ((places & Places.Mines) != 0)
        {
            gold = Free(gfx, gfz, 30);
            Landmarks.Mine(w, site, gold.Item1, gold.Item2, "Золотой прииск", 10, false, seed + 4);
        }
        if ((places & Places.Outpost) != 0)
        {
            var (ox, oz) = hex.SidePoint(entry, .3f + 64 / sideLen, 160);
            var outpost = Free(ox, oz, 32);
            Landmarks.Outpost(w, site, outpost.x, outpost.z, "Старая застава", seed + 5);
        }
        if ((places & Places.Watchtowers) != 0)
        {
            double fdx = CX - fordAt.x, fdz = CZ - fordAt.z, fl = Math.Max(1, Math.Sqrt(fdx * fdx + fdz * fdz));
            var tower = Free(fordAt.x + fdx / fl * 60, fordAt.z + fdz / fl * 60, 20);
            Landmarks.Watchtower(w, site, tower.x, tower.z, "Руины сторожевой башни", seed + 6);
            var (rfx, rfz) = AtAngle(ramps[1], rampRim[1] + rampLen * .6);
            var (pxr, pzr) = Dir(ramps[1] + Math.PI / 2);
            var fallen = Free(rfx + pxr * 60, rfz + pzr * 60, 20);
            Landmarks.Watchtower(w, site, fallen.x, fallen.z, "Обвалившаяся башня", seed + 7, .45);
        }
        if ((places & Places.Bridges) != 0)
        {
            // the road's bridge where the road from the horde's side crosses the river, whole; a fallen one up the river
            (double x, double z)? cross = null;
            for (int i = 0; i + 1 < road1.Count && cross == null; i++)
            {
                var (x, z) = road1[i];
                int c = (int)z * SX + (int)x;
                if (x >= 0 && z >= 0 && x < SX && z < SZ && hex.Edge((float)x, (float)z) > 8 && riverD[c] < 2) cross = (x, z);
            }
            void Span((double x, double z) at, double dx, double dz, bool whole, string name)
            {
                double l = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz)); dx /= l; dz /= l;
                double a = 0, b = 0;
                while (a < 200 && Wet(at.x - dx * a, at.z - dz * a)) a += 1;
                while (b < 200 && Wet(at.x + dx * b, at.z + dz * b)) b += 1;
                a += 10; b += 10;
                int xa = (int)(at.x - dx * a), za = (int)(at.z - dz * a), xb = (int)(at.x + dx * b), zb = (int)(at.z + dz * b);
                if (!hex.Inside(xa, za) || !hex.Inside(xb, zb)) return;
                Landmarks.OldBridge(w, site, xa, za, xb, zb, name, whole, seed + (whole ? 8u : 9u));
            }
            bool Wet(double x, double z) => x >= 0 && z >= 0 && x < SX && z < SZ && water[(int)z * SX + (int)x] > 0;
            if (cross is { } c0)
            {
                // along the road where it crosses
                int ci = road1.FindIndex(q => q == c0);
                var (ax, az) = road1[Math.Max(0, ci - 2)]; var (bx, bz) = road1[Math.Min(road1.Count - 1, ci + 2)];
                Span(c0, bx - ax, bz - az, true, "Старый мост");
            }
            // the fallen one between the ford and the crossing, square across the river
            int fi0 = inside0 + (inside1 - inside0) / 5;
            var (ux0, uz0) = river[fi0]; var (ux1, uz1) = river[Math.Min(river.Count - 1, fi0 + 2)];
            bool clear = true;
            foreach (var l in site.Landmarks) if (Brushes.Dist(ux0 - l.X, uz0 - l.Z) < l.R + 50) clear = false;
            if (clear) Span((ux0, uz0), -(uz1 - uz0), ux1 - ux0, false, "Обрушенный мост");
        }
        if ((places & Places.Power) != 0)
        {
            var (bx, bz) = AtAngle(toEntry + Math.PI * .6, Rb * .62);
            var barrow = Free(bx, bz, 56);
            Landmarks.Barrow(w, site, barrow.x, barrow.z, "Курган", seed + 10);
            var (kx, kz) = AtAngle(toEntry - 1.1, rpMax + 110);
            var black = Free(kx, kz, 38);
            Landmarks.BlackStone(w, site, black.x, black.z, "Чёрный камень", seed + 11);
            var (dx, dz) = hex.SidePoint(entry, .72f, 120);
            var spring = Free(dx, dz, 50);
            Landmarks.DeadSpring(w, site, spring.x, spring.z, "Мёртвый родник", seed + 12);
        }

        // the treasure of a valley of the map nobody holds (Troves.cs): behind the middle, on the side away from the enemy's
        if (p.Trove != TroveKind.None)
        {
            var (tx, tz) = AtAngle(toEntry + Math.PI, Math.Min(rpMax + 110, hex.HalfW - Troves.Room(p.Trove) - 40));
            var spot = Free(tx, tz, Troves.Room(p.Trove));
            Troves.Lay(w, site, p.Trove, spot.x, spot.z, p.TroveTaken, seed + 13);
        }

        bool Clear(int x, int z)
        {
            // (a tree's crown kept off the land's edge, and the horde's side kept open)
            if (x < 8 || x >= SX - 8 || z < 8 || z >= SZ - 8 || hex.Edge(x, z) < 20 || site.InStrip(x, z)) return false;
            byte k = kind[z * SX + x];
            return k is Relief.Low or Relief.Plateau or Relief.Bank && !NearKeep(x, z, 48) && !Landmarks.Taken(site, x, z, 6);
        }

        // iron: veins through the rock, outcrops where they come up (MapGen.Ore.cs), before what grows over the ground
        OreVeins.Lay(w, site, seed);

        // boulders, more of them on the rocky ground
        for (int qz = 0; qz < SZ; qz += 56)
            for (int qx = 0; qx < SX; qx += 56)
            {
                int x = qx + (int)(Hash.H3(qx, qz, 101) * 56), z = qz + (int)(Hash.H3(qx, qz, 102) * 56);
                if (x >= SX || z >= SZ || !Clear(x, z)) continue;
                double chance = nKnoll.Sample(x * .018, z * .018) > .6 ? .55 : .2;
                if (Hash.H3(qx, qz, 103) > chance) continue;
                Flora.Boulder(w, x, z, 5 + (int)(Hash.H3(qx, qz, 104) * 9), site.HTop[z * SX + x]);
            }

        // the woods: thick where the ground is wet, thin where it is dry; firs and pines up high, oaks and birches low
        // and wet, cypresses on the plateau; a clearing round the citadel
        for (int qz = 0; qz < SZ; qz += 20)
            for (int qx = 0; qx < SX; qx += 20)
                if (Grow(qx, qz, 0) == false) Grow(qx, qz, 1);
        bool? Grow(int qx, int qz, int again)
        {
            int x = qx + (int)(Hash.H3(qx, qz, 201 + again * 10) * 16), z = qz + (int)(Hash.H3(qx, qz, 202 + again * 10) * 16);
            if (x >= SX || z >= SZ || !Clear(x, z) || !Mat.IsGrass(w.Get(x, w.SurfAt(x, z) - 1, z))) return null;
            int i = z * SX + x;
            double f = nForest.Sample(x * .006, z * .006), m = moist[i], hgt = site.HTop[i] - baseH;
            double density = f > .58 ? .72 : f > .48 ? .28 : .04;
            // (thick by the water, thin on the dry ground: as many trees to the hectare as the old valley had, for the
            // valley is twice as big and so many more would cost twice the triangles)
            density *= Math.Clamp(.05 + m * .75, .1, 1);
            bool plateau = kind[i] == Relief.Plateau;
            if (plateau) density = NearKeep(x, z, 160) ? 0 : density * .3;
            if (Hash.H3(qx, qz, 203) > density * p.TreeDensity * ground.Trees) return null;
            double r = Hash.H3(qx, qz, 204), size = Hash.H3(qx, qz, 205), biome = nBiome.Sample(x * .004, z * .004) - (hgt - 12) / 60 + (m - .45) * .5;
            if (r > .94) return Flora.DeadTree(w, x, z, grown: true, scale: 1.45);
            if (r > .86) { Flora.Bush(w, x, z, 3 + (int)(size * 4)); return true; }
            if (plateau)
                return r < .45 ? Flora.Cypress(w, x, z, 42 + (int)(size * 26), grain: 1, grown: true)
                    : r < .7 ? Flora.Birch(w, x, z, 48 + (int)(size * 26))
                    : Flora.Pine(w, x, z, 66 + (int)(size * 34));
            if (biome < .4)
                return r < .55 ? Flora.Spruce(w, x, z, 56 + (int)(size * 40), size > .5 ? 3 : 2) : Flora.Pine(w, x, z, 68 + (int)(size * 36));
            if (biome > .6)
                return r < .5 ? Flora.Oak(w, x, z, 60 + (int)(size * 28), 4) : Flora.Birch(w, x, z, 46 + (int)(size * 26));
            return r < .3 ? Flora.Spruce(w, x, z, 56 + (int)(size * 40), size > .5 ? 3 : 2)
                : r < .55 ? Flora.Oak(w, x, z, 60 + (int)(size * 28), 4)
                : r < .72 ? Flora.Cypress(w, x, z, 42 + (int)(size * 26), grain: 1, grown: true)
                : Flora.Birch(w, x, z, 46 + (int)(size * 26));
        }

        // what stands in the middle: the citadel with its braziers at the door; or, in a valley of the map, the place of an
        // outpost or the horde's town (Holdings.cs)
        site.Holding = p.Holding;
        // the small things (MapGen.Nature.cs): bushes, fallen trees, flowers, reeds and the rest, clear of the middle
        // whatever stands there
        if (p.Details) Nature(w, site, p, relief);
        if (p.Holding == Holding.Outpost) { site.KeepBase = Holdings.OutpostPlace(w, site); return site; }
        if (p.Holding == Holding.HordeTown) { site.KeepBase = Holdings.HordeTown(w, site, seed); return site; }

        site.KeepBase = WorldGen.Keep(w, site);
        int gx2 = site.KeepX - site.KeepR - 7;
        foreach (int dz in new[] { -10, 10 }) WorldGen.Brazier(w, site, gx2, w.SurfAt(gx2, site.KeepZ + dz), site.KeepZ + dz, post: 4);
        site.Torches.Add((site.KeepX - site.KeepR - 3, site.KeepBase + 22, site.KeepZ));
        return site;
    }
}
