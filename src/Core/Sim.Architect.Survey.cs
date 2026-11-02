using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The architect's survey and plan (Sim.Architect.cs). Before he plans he walks the ground about the point the valley
/// holds and notes it metre by metre: what of ours stands there (a wall a man and a half high and more, of masonry or
/// of timber, a gateway in one, a tower), what was built there and is broken, what is marked to be built, the houses
/// and the town's buildings, deep water and drops of more than two and a half metres, trees and roads. Then he looks
/// for the cheapest line round the point and what must stand inside it: along what stands it costs next to nothing,
/// along water or a cliff little, over open ground a wall's worth, more on a slope, among trees, in shallow water, along
/// a road, and much more where it would run beside a wall that stands (it would double it) or inside the reach he wants
/// for the garrison; never through a house, beside one, through the citadel or off the land. What stands of that line
/// he mends, raises and puts in stone; the rest he closes with new wall, and he adds gates where the roads cross it,
/// towers on its corners, stairs up it, the ditch and the stakes before it.
/// </summary>
public sealed partial class Sim
{
    /// <summary>What a metre of ground about the point is to the architect; several may hold at once.</summary>
    [Flags]
    enum Lie : ushort
    {
        None = 0,
        /// <summary>Off the land or at its edge, or not to be built on (the citadel, the strip the horde comes in by).</summary>
        Off = 1,
        /// <summary>Under a roof, a house or a town building; or beside one.</summary>
        House = 2, ByHouse = 4,
        /// <summary>A wall of ours a man and a half high and more (standing, meant or marked): of masonry or earth, or of timber; a gateway in one.</summary>
        Wall = 8, Timber = 16, Gate = 32,
        /// <summary>Water too deep to wade, water to wade, a drop of more than two and a half metres to the ground beside.</summary>
        Deep = 64, Shallow = 128, Cliff = 256,
        Tree = 512,
        /// <summary>On a road; within four metres of one; the road there runs along x (else along z).</summary>
        Road = 1024, NearRoad = 2048, RoadAlongX = 4096,
        /// <summary>Some of what was built there is gone; some of it stands a tower's height; a roof or a deck over room to stand (a gateway's, a bridge's).</summary>
        Broken = 8192, Tall = 16384, Roofed = 32768,
    }

    const Lie Barring = Lie.Wall | Lie.Timber | Lie.Gate;

    /// <summary>The ground about a point, a metre to a cell (as the walking grid's columns): what each cell is, its ground, how high our works stand on it.</summary>
    sealed class ArchGround
    {
        public int I0, K0, NI, NK, PI, PK;
        public Lie[] Is;
        public short[] Floor, Rise;
        public int Idx(int gi, int gk) => gk * NI + gi;
        public bool Has(int c, Lie l) => (Is[c] & l) != 0;
        /// <summary>A cell's middle, voxels.</summary>
        public (int x, int z) Mid(int c) => (((I0 + c % NI) << NavGrid.Shift) + NavGrid.CS / 2, ((K0 + c / NI) << NavGrid.Shift) + NavGrid.CS / 2);
        /// <summary>The cell a column is in, or −1 outside.</summary>
        public int CellAt(int x, int z)
        {
            int gi = (x >> NavGrid.Shift) - I0, gk = (z >> NavGrid.Shift) - K0;
            return x < 0 || z < 0 || gi < 0 || gk < 0 || gi >= NI || gk >= NK ? -1 : gk * NI + gi;
        }
    }

    /// <summary>How much of the planning one slice does (a slice a step of the game while he plans, Sim.Architect.cs): rows of sampled columns of the survey, chunks of a layer looked through, places the search for the line takes up.</summary>
    const int SurveyRows = 8, TopsChunks = 8, RingSteps = 6144;

    /// <summary>
    /// Walks the ground within so far of a point (see <see cref="Lie"/>) into the ground given, a slice at a time: it
    /// stops after every few rows of what it looks over, to go on the next step (see <see cref="PlanSlices"/>).
    /// </summary>
    IEnumerable<bool> SurveySlices(int px, int pz, int reach, ArchGround s)
    {
        const int C = NavGrid.CS, Sh = NavGrid.Shift;
        int pi = px >> Sh, pk = pz >> Sh, r = (reach + C - 1) / C;
        s.I0 = Math.Max(0, pi - r); s.K0 = Math.Max(0, pk - r);
        int i1 = Math.Min(Nav.NX - 1, pi + r), k1 = Math.Min(Nav.NZ - 1, pk + r);
        s.NI = i1 - s.I0 + 1; s.NK = k1 - s.K0 + 1; s.PI = pi - s.I0; s.PK = pk - s.K0;
        int n = s.NI * s.NK, x0 = s.I0 * C, z0 = s.K0 * C, nx = s.NI * C, nz = s.NK * C;
        s.Is = new Lie[n]; s.Floor = new short[n]; s.Rise = new short[n];
        // what is meant to stand and what is marked to be laid, the top of each column
        var meant = new int[nx * nz]; var marked = new int[nx * nz];
        foreach (var _ in TopsIn(Design, x0, z0, nx, nz, meant)) yield return false;
        foreach (var _ in TopsIn(Orders.Layer, x0, z0, nx, nz, marked)) yield return false;
        var walls = new int[n]; var timber = new int[n]; var roofed = new int[n]; var tall = new int[n]; var broken = new int[n]; var off = new int[n]; var ground = new long[n];
        // (the castle's lean-tos are buildings, their fronts no walls to take a line along)
        var sheds = StartingFort.Sheds(this);
        // every other column each way, sixteen to a cell: a wall that bars the way is two voxels thick at the least
        int rows = 0;
        for (int z = z0; z < z0 + nz; z += 2)
        {
            for (int x = x0; x < x0 + nx; x += 2)
            {
                int c = ((z - z0) >> Sh) * s.NI + ((x - x0) >> Sh), col = (z - z0) * nx + (x - x0);
                if (!World.OnLand(x, z) || Site.Forbidden(x, z) || !Site.Inland(x, z, 12)) { off[c]++; continue; }
                int g = GroundAt(x, z);
                ground[c] += g;
                int built = OursTop(x, z, g), top = Math.Max(built, Math.Max(meant[col], marked[col]));
                if (top - g < Fortification.Bars || sheds.Exists(r => x >= r.x0 && x <= r.x1 && z >= r.z0 && z <= r.z1)) continue;
                // a roof or a deck over room to stand in (a gateway's, a bridge's, a room in a tower): no wall to bar the way there
                if (built > g + 12 && Brushes.Free(World.Get(x, g + 1, z)) && Brushes.Free(World.Get(x, g + 6, z))) { roofed[c]++; continue; }
                walls[c]++;
                s.Rise[c] = (short)Math.Max(s.Rise[c], top - g);
                if (top - g >= Fortification.TowerLeast) tall[c]++;
                if (meant[col] > built + 1) broken[c]++;
                byte m = built >= top ? World.Get(x, top - 1, z) : meant[col] >= top ? Design.Get(x, top - 1, z) : Orders.At(x, top - 1, z);
                if (m == Mat.Wood) timber[c]++;
            }
            if (++rows % SurveyRows == 0) yield return false;
        }
        for (int c = 0; c < n; c++)
        {
            int cols = C * C / 4 - off[c];
            if (off[c] > 0) s.Is[c] |= Lie.Off;
            s.Floor[c] = (short)(cols > 0 ? ground[c] / cols : 0);
            if (walls[c] >= 2) s.Is[c] |= timber[c] * 2 >= walls[c] ? Lie.Timber : Lie.Wall;
            if (roofed[c] >= 3) s.Is[c] |= Lie.Roofed;
            if (tall[c] >= 2) s.Is[c] |= Lie.Tall;
            if (broken[c] >= 1) s.Is[c] |= Lie.Broken;
        }
        // the houses and the town's buildings
        void Rect(int ax, int az, int bx, int bz, Lie l)
        {
            for (int k = Math.Min(az, bz) >> Sh; k <= Math.Max(az, bz) >> Sh; k++)
                for (int i = Math.Min(ax, bx) >> Sh; i <= Math.Max(ax, bx) >> Sh; i++)
                {
                    int gi = i - s.I0, gk = k - s.K0;
                    if (gi >= 0 && gk >= 0 && gi < s.NI && gk < s.NK) s.Is[s.Idx(gi, gk)] |= l;
                }
        }
        foreach (var b in Buildings) Rect(b.X0, b.Z0, b.X1, b.Z1, Lie.House);
        foreach (var zn in Zones) if (zn.Kind == Zone.Dwelling) Rect(zn.X0, zn.Z0, zn.X1, zn.Z1, Lie.House);
        // the gateways in our walls (a house's door is the house's)
        foreach (var gt in Gates)
        {
            var (mx, _, mz) = gt.Middle;
            if (InHouse((int)mx, (int)mz, 4)) continue;
            for (int u = gt.U0; u < gt.U0 + Gateway.Width; u += 2)
                for (int a = gt.P - 6; a <= gt.P + 6; a += 2)
                {
                    int c = gt.AlongX ? s.CellAt(u, a) : s.CellAt(a, u);
                    if (c >= 0) s.Is[c] = (s.Is[c] | Lie.Gate) & ~Lie.House;
                }
        }
        // water, trees and drops, by the walking grid and the ground
        for (int c = 0; c < n; c++)
        {
            if (s.Has(c, Lie.Off)) continue;
            int gi = c % s.NI, gk = c / s.NI, y = Math.Clamp(s.Floor[c] >> Sh, 1, Nav.NY - 1), nc = Nav.Id(s.I0 + gi, y, s.K0 + gk);
            if (Nav.Water[nc] >= NavGrid.DeepT) s.Is[c] |= Lie.Deep;
            else if (Nav.Water[nc] > 0) s.Is[c] |= Lie.Shallow;
            if (Nav.Tree[nc] != 0) s.Is[c] |= Lie.Tree;
            foreach (var (di, dk) in Four)
            {
                int ni = gi + di, nk = gk + dk;
                if (ni < 0 || nk < 0 || ni >= s.NI || nk >= s.NK) continue;
                int o = s.Idx(ni, nk);
                if (!s.Has(o, Lie.Off) && s.Floor[c] - s.Floor[o] >= 20) s.Is[c] |= Lie.Cliff;
            }
        }
        // the roads, and which way each runs
        var segs = new List<(float ax, float az, float bx, float bz)>();
        foreach (var road in Site.Roads) for (int i = 0; i + 1 < road.Count; i++) segs.Add((road[i].X, road[i].Z, road[i + 1].X, road[i + 1].Z));
        if (segs.Count > 0)
            for (int c = 0; c < n; c++)
            {
                var (x, z) = s.Mid(c);
                float best = float.MaxValue; bool alongX = true;
                foreach (var (ax, az, bx, bz) in segs)
                {
                    float dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz, t = l2 < 1e-4f ? 0 : Math.Clamp(((x - ax) * dx + (z - az) * dz) / l2, 0, 1);
                    float ex = ax + dx * t - x, ez = az + dz * t - z, d = ex * ex + ez * ez;
                    if (d < best) { best = d; alongX = Math.Abs(dx) >= Math.Abs(dz); }
                }
                best = MathF.Sqrt(best);
                if (best < 10) s.Is[c] |= Lie.Road;
                if (best < 32) s.Is[c] |= Lie.NearRoad | (alongX ? Lie.RoadAlongX : 0);
                if ((c & 4095) == 4095) yield return false;
            }
        // beside a house
        for (int c = 0; c < n; c++)
        {
            if (!s.Has(c, Lie.House)) continue;
            int gi = c % s.NI, gk = c / s.NI;
            for (int dk = -1; dk <= 1; dk++)
                for (int di = -1; di <= 1; di++)
                {
                    int ni = gi + di, nk = gk + dk;
                    if (ni >= 0 && nk >= 0 && ni < s.NI && nk < s.NK && !s.Has(s.Idx(ni, nk), Lie.House)) s.Is[s.Idx(ni, nk)] |= Lie.ByHouse;
                }
        }
    }

    static readonly (int di, int dk)[] Four = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    static readonly int[] RingDI = { 1, -1, 0, 0, 1, 1, -1, -1 }, RingDK = { 0, 0, 1, -1, 1, -1, 1, -1 };

    /// <summary>Whether a column is at the point or what stands at it (the citadel, the outpost's square, the mine), or so near it.</summary>
    bool InCore(int x, int z, int by)
    {
        var (cx, cz, _) = PointToHold();
        return HoldPoint is { } hp ? Brushes.Dist(x - hp.X, z - hp.Z) <= hp.R + by
            : Site.HasKeep ? Brushes.Dist(x - Site.KeepX, z - Site.KeepZ) <= Site.KeepR + by + 4
            : Math.Max(Math.Abs(x - cx), Math.Abs(z - cz)) <= Holdings.Square + by;
    }

    /// <summary>Whether a column is in a house or a town building, or so near one.</summary>
    bool InHouse(int x, int z, int by)
    {
        foreach (var b in Buildings) if (x >= b.X0 - by && x <= b.X1 + by && z >= b.Z0 - by && z <= b.Z1 + by) return true;
        foreach (var zn in Zones) if (zn.Kind == Zone.Dwelling && x >= zn.X0 - by && x <= zn.X1 + by && z >= zn.Z0 - by && z <= zn.Z1 + by) return true;
        return false;
    }

    /// <summary>One over the highest voxel of ours in a column that bars the way (not a ladder, a plant, dressing), over the ground; 0 for none.</summary>
    int OursTop(int x, int z, int ground)
    {
        for (int y = Math.Min(World.SurfAt(x, z), World.SY) - 1; y >= Math.Max(1, ground); y--)
        {
            byte m = World.Get(x, y, z);
            if (Mat.IsPlayer[m] && !Mat.Passable[m] && !Mat.IsDecor[m]) return y + 1;
        }
        return 0;
    }

    /// <summary>
    /// For each column of a box, one over the highest voxel of a layer (what is meant, or marked to be laid) that would
    /// bar the way; 0 for none. A slice at a time: it stops after every so many chunks it has looked through.
    /// </summary>
    static IEnumerable<bool> TopsIn(VoxelWorld layer, int x0, int z0, int nx, int nz, int[] tops)
    {
        int looked = 0;
        for (int cz = z0 >> VoxelWorld.Shift; cz <= (z0 + nz - 1) >> VoxelWorld.Shift && cz < layer.CZ; cz++)
            for (int cx = x0 >> VoxelWorld.Shift; cx <= (x0 + nx - 1) >> VoxelWorld.Shift && cx < layer.CX; cx++)
                for (int cy = 0; cy < layer.CY; cy++)
                {
                    var d = layer.ChunkData(layer.ChunkIndex(cx, cy, cz));
                    if (d == null) continue;
                    for (int i = 0; i < d.Length; i++)
                    {
                        byte m = d[i];
                        if (m == 0 || m >= Orders.RemoveStone || !Mat.IsPlayer[m] || Mat.Passable[m] || Mat.IsDecor[m]) continue;
                        int x = cx * VoxelWorld.CS + (i & 31) - x0, z = cz * VoxelWorld.CS + (i >> 5 & 31) - z0, y = cy * VoxelWorld.CS + (i >> 10);
                        if ((uint)x >= (uint)nx || (uint)z >= (uint)nz) continue;
                        int col = z * nx + x;
                        if (y + 1 > tops[col]) tops[col] = y + 1;
                    }
                    if (++looked % TopsChunks == 0) yield return false;
                }
    }

    // ------------------------------------------------------------ the line round the point

    /// <summary>
    /// What it costs to have the line pass through each cell (see the summary above), infinite where it may not. Cells
    /// that must be inside: the point and what stands at it, two metres clear; and the houses and town buildings within
    /// the reach wanted and six metres more, each with a lane of cells from it to the point, so that the line goes round
    /// them all. New wall inside the reach wanted costs up to four times as much, the nearer the point the more.
    /// </summary>
    float[] RingCosts(ArchGround s, int cx, int cz, int want, out int townIn)
    {
        int n = s.NI * s.NK;
        var must = new bool[n];
        // the point and what stands at it
        for (int c = 0; c < n; c++)
        {
            if (s.Has(c, Barring)) continue;
            var (x, z) = s.Mid(c);
            if (InCore(x, z, 16)) must[c] = true;
        }
        // the town: its houses and buildings near enough, and a lane from each to the point
        int town = 0;
        void Take(int ax, int az, int bx, int bz)
        {
            float nx = Math.Clamp(cx, ax, bx), nz = Math.Clamp(cz, az, bz);
            if (Brushes.Dist(nx - cx, nz - cz) > want + 48) return;
            town++;
            for (int z = az - 8; z <= bz + 8; z += 4)
                for (int x = ax - 8; x <= bx + 8; x += 4) { int c = s.CellAt(x, z); if (c >= 0) must[c] = true; }
            float mx = (ax + bx) / 2f, mz = (az + bz) / 2f, len = (float)Math.Max(1, Brushes.Dist(mx - cx, mz - cz));
            for (float t = 0; t <= len; t += 4) { int c = s.CellAt((int)(mx + (cx - mx) * t / len), (int)(mz + (cz - mz) * t / len)); if (c >= 0) must[c] = true; }
        }
        foreach (var b in Buildings) Take(Math.Min(b.X0, b.X1), Math.Min(b.Z0, b.Z1), Math.Max(b.X0, b.X1), Math.Max(b.Z0, b.Z1));
        foreach (var zn in Zones) if (zn.Kind == Zone.Dwelling) Take(zn.X0, zn.Z0, zn.X1, zn.Z1);
        townIn = town;
        // how much wall that stands is within two metres of each cell: new wall there would run beside it (the more of
        // it about, the dearer: by a wall's end, where new wall goes on from it, less than by its middle)
        var byWall = new byte[n];
        for (int c = 0; c < n; c++)
        {
            if (!s.Has(c, Barring)) continue;
            int gi = c % s.NI, gk = c / s.NI;
            for (int dk = -2; dk <= 2; dk++)
                for (int di = -2; di <= 2; di++)
                {
                    int ni = gi + di, nk = gk + dk;
                    if (ni >= 0 && nk >= 0 && ni < s.NI && nk < s.NK && byWall[s.Idx(ni, nk)] < 255) byWall[s.Idx(ni, nk)]++;
                }
        }
        var cost = new float[n];
        for (int c = 0; c < n; c++)
        {
            var l = s.Is[c];
            float k;
            if ((l & Lie.Off) != 0 || must[c] || (l & Lie.House) != 0) k = float.PositiveInfinity;
            else if ((l & (Lie.Wall | Lie.Gate)) != 0) k = (l & Lie.Broken) != 0 ? .1f : .06f;
            else if ((l & Lie.Timber) != 0) k = .15f;
            else if ((l & (Lie.ByHouse | Lie.Roofed)) != 0) k = float.PositiveInfinity;
            else if ((l & Lie.Deep) != 0) k = .6f;
            else if ((l & Lie.Cliff) != 0) k = .4f;
            else
            {
                var (x, z) = s.Mid(c);
                float d = HoldPoint == null && !Site.HasKeep ? Math.Max(Math.Abs(x - cx), Math.Abs(z - cz)) : (float)Brushes.Dist(x - cx, z - cz);
                // (a wall's worth more anywhere inside, so that the line keeps its corners square and room is left by them)
                k = 1 + (d < want ? 1f + 2f * (want - d) / want : .002f * (d - want));
                int gi = c % s.NI, gk = c / s.NI, slope = 0;
                foreach (var (di, dk) in Four)
                {
                    int ni = gi + di, nk = gk + dk;
                    if (ni >= 0 && nk >= 0 && ni < s.NI && nk < s.NK) slope = Math.Max(slope, Math.Abs(s.Floor[c] - s.Floor[s.Idx(ni, nk)]));
                }
                k += .04f * slope;
                if ((l & Lie.Tree) != 0) k += .4f;
                if ((l & Lie.Shallow) != 0) k += 2;
                if ((l & Lie.Road) != 0) k += .5f;
                k += Math.Min(8, .8f * byWall[c]);
            }
            cost[c] = k;
        }
        return cost;
    }

    /// <summary>
    /// The cheapest line of cells round the point, in order, or null if there is none: for each cell east of the point in
    /// its row, the cheapest way from it back to itself that crosses the half-line east of the point once (the way is
    /// searched over two copies of the ground, crossing the half-line taking it from one to the other), and the cheapest
    /// of those. A diagonal step is no shorter than its two sides where either is barred, and near a road the line goes
    /// square across it, so that a gate can hang there.
    /// </summary>
    static IEnumerable<bool> CheapestRing(ArchGround s, float[] cost, int want, Action<List<int>> found)
    {
        int n = s.NI * s.NK;
        var dist = new float[2 * n]; var from = new int[2 * n]; var seen = new int[2 * n]; var closed = new int[2 * n];
        var open = new PriorityQueue<int, float>();
        float best = float.PositiveInfinity;
        List<int> ring = null;
        int gen = 0, taken = 0;
        // the cells of the half-line most likely to be on the line first (what stands, and about the reach wanted), so
        // that the cheapest found early cuts the later searches short; sixteen of them at the most
        var starts = new List<(int cell, float promise)>();
        for (int si = s.PI + 1; si < s.NI; si++)
        {
            int c = s.Idx(si, s.PK);
            if (!float.IsPositiveInfinity(cost[c])) starts.Add((c, cost[c] + Math.Abs((si - s.PI) * NavGrid.CS - want) / 32f));
        }
        starts.Sort((a, b) => a.promise.CompareTo(b.promise));
        if (starts.Count > 16) starts.RemoveRange(16, starts.Count - 16);
        foreach (var (start, _) in starts)
        {
            gen++;
            open.Clear();
            dist[start] = 0; seen[start] = gen; from[start] = -1;
            open.Enqueue(start, 0);
            int goal = start + n;
            while (open.TryDequeue(out int st, out float d))
            {
                if (++taken % RingSteps == 0) yield return false;
                if (closed[st] == gen) continue;
                closed[st] = gen;
                if (d >= best) break;
                if (st == goal)
                {
                    best = d;
                    ring = new List<int>();
                    for (int q = goal; q != start; q = from[q]) ring.Add(q % n);
                    ring.Reverse();
                    break;
                }
                int layer = st >= n ? 1 : 0, c = st - layer * n, ci = c % s.NI, ck = c / s.NI;
                for (int dir = 0; dir < 8; dir++)
                {
                    int ni = ci + RingDI[dir], nk = ck + RingDK[dir];
                    if (ni < 0 || nk < 0 || ni >= s.NI || nk >= s.NK) continue;
                    int nc = s.Idx(ni, nk);
                    float cc = cost[nc];
                    if (float.IsPositiveInfinity(cc)) continue;
                    bool diag = dir >= 4;
                    if (diag && (float.IsPositiveInfinity(cost[s.Idx(ni, ck)]) || float.IsPositiveInfinity(cost[s.Idx(ci, nk)]))) continue;
                    float step = cc * (diag ? 1.414f : 1);
                    // near a road the line crosses it square: no slant there, and not along it
                    if (s.Has(nc, Lie.NearRoad) && !s.Has(nc, Barring))
                    {
                        if (diag) step *= 2;
                        else if (s.Has(nc, Lie.Road) && (RingDK[dir] == 0) == s.Has(nc, Lie.RoadAlongX)) step *= 3;
                    }
                    int nl = layer;
                    if ((ck < s.PK) != (nk < s.PK) && Math.Min(ci, ni) > s.PI) nl ^= 1;
                    int ns = nc + nl * n;
                    float nd = d + step;
                    if (closed[ns] == gen || seen[ns] == gen && nd >= dist[ns]) continue;
                    seen[ns] = gen; dist[ns] = nd; from[ns] = st;
                    open.Enqueue(ns, nd);
                }
            }
        }
        found(ring);
    }

    // ------------------------------------------------------------ the plan

    /// <summary>Of each cell of the line: new wall to build there (0), a wall that stands (1), water or a cliff that bar the way (2), a palisade that stands (3).</summary>
    const byte LineNew = 0, LineStands = 1, LineNature = 2, LineTimber = 3;

    /// <summary>
    /// The architect looks the ground over and plans (see the summary of this file): the reach he wants inside the line
    /// is as big as clears the point by three metres (bigger for a stronger way and a bigger garrison); stone if the
    /// stores have well over half of what the new wall takes and the way is not the modest one, else a palisade first.
    /// What he found and means to do is said in <see cref="ArchPlan.Found"/>. All of it at once, as the bench asks it.
    /// </summary>
    public ArchPlan MakeArchPlan()
    {
        foreach (var plan in PlanSlices()) if (plan != null) return plan;
        return null;
    }

    /// <summary>
    /// The planning a slice at a time (<see cref="MakeArchPlan"/>): null after each slice of the survey and of the
    /// search for the line, the plan at the end. The game takes a slice a step (Sim.Architect.cs, TickArchPlan), so that
    /// no step waits on the whole of it; the slices are counted in work, not in time, and the plan comes out the same
    /// however fast the machine is.
    /// </summary>
    IEnumerable<ArchPlan> PlanSlices()
    {
        var (cx, cz, r) = PointToHold();
        var style = ArchStyle;
        int garrison = Units.FindAll(u => !u.Dead && u.Team == Team.Player && u.T.Work < 1 && !Kinds.IsEngine(u.T)).Count;
        int want = r + 24 + (style == ArchStyle.Sky ? 16 : style == ArchStyle.Strong ? 8 : 0) + Math.Clamp((garrison - 6) * 2, 0, 32);
        var p = new ArchPlan { CX = cx, CZ = cz, Style = style, H = want };
        // the horde's side: where its bands come into the valley
        var (ex, ez) = Site.Hex != null ? Site.EntryOn(Site.EntrySide, .5) : (Site.EntryX, cz);
        p.HordeSide = SideToward(ex - cx, ez - cz);
        // the ground about, wider if walls of ours stand at the edge of what he looked at
        int reach = Math.Min(want + 240, 640);
        var s = new ArchGround();
        foreach (var _ in SurveySlices(cx, cz, reach, s)) yield return null;
        if (reach < 640 && WallsAtEdge(s))
        {
            s = new ArchGround();
            foreach (var _ in SurveySlices(cx, cz, reach = 640, s)) yield return null;
        }
        var cost = RingCosts(s, cx, cz, want, out _);
        yield return null;
        List<int> ring = null;
        foreach (var _ in CheapestRing(s, cost, want, found => ring = found)) yield return null;
        if (ring == null || ring.Count < 8)
        {
            p.Found = "Архитектор не нашёл, где поставить стену вокруг: мешают дома или край долины.";
            yield return p;
            yield break;
        }
        BuildPlan(p, s, cost, ring);
        yield return p;
    }

    /// <summary>Whether a wall of ours stands within four cells of the edge of the ground looked at.</summary>
    static bool WallsAtEdge(ArchGround s)
    {
        for (int c = 0; c < s.Is.Length; c++)
        {
            if (!s.Has(c, Barring)) continue;
            int gi = c % s.NI, gk = c / s.NI;
            if (gi < 4 || gk < 4 || gi >= s.NI - 4 || gk >= s.NK - 4) return true;
        }
        return false;
    }

    /// <summary>The works of the plan, from the line found (see <see cref="MakeArchPlan"/>).</summary>
    void BuildPlan(ArchPlan p, ArchGround s, float[] cost, List<int> ring)
    {
        int cx = p.CX, cz = p.CZ, h = WallHeight(p.Style);
        // the line counter-clockwise (x east, z south): outward is to the right of the way along
        var mids = ring.ConvertAll(c => s.Mid(c));
        if (Area(mids) < 0) { ring.Reverse(); mids.Reverse(); }
        int n = ring.Count;
        var kind = new byte[n];
        for (int j = 0; j < n; j++)
            kind[j] = s.Has(ring[j], Lie.Wall | Lie.Gate) ? LineStands : s.Has(ring[j], Lie.Timber) ? LineTimber : s.Has(ring[j], Lie.Deep | Lie.Cliff) ? LineNature : LineNew;
        bool Clear(int x, int z) { int c = s.CellAt(x, z); return c >= 0 && !float.IsPositiveInfinity(cost[c]); }
        // where the roads cross the line: the middle cell of each run of road cells on it
        var crossings = new List<int>();
        for (int j = 0; j < n; j++)
        {
            if (!s.Has(ring[j], Lie.Road) || s.Has(ring[(j - 1 + n) % n], Lie.Road)) continue;
            int len = 1;
            while (len < n && s.Has(ring[(j + len) % n], Lie.Road)) len++;
            if (len < n) crossings.Add((j + len / 2) % n);
        }
        // the horde's side last, and two at most
        int hdx = RingSides[p.HordeSide].ox, hdz = RingSides[p.HordeSide].oz;
        crossings.Sort((a, b) => Toward(mids[a]).CompareTo(Toward(mids[b])));
        float Toward((int x, int z) m) => (m.x - cx) * hdx + (m.z - cz) * hdz;
        if (crossings.Count > 2) crossings.RemoveRange(2, crossings.Count - 2);

        // the runs of the line: new wall, what stands, what bars the way by nature
        var runs = new List<(int from, int len, byte kind)>();
        int first = -1;
        for (int j = 0; j < n; j++) if (kind[j] != kind[(j - 1 + n) % n]) { first = j; break; }
        if (first < 0)
        {
            // all of a kind: begun at the cell farthest along it from the roads
            int at = 0, far = -1;
            for (int j = 0; j < n; j++)
            {
                int d = n;
                foreach (int c in crossings) d = Math.Min(d, Math.Min(Math.Abs(j - c), n - Math.Abs(j - c)));
                if (d > far) { far = d; at = j; }
            }
            runs.Add((at, n, kind[0]));
        }
        else
        {
            int j = first;
            do
            {
                int len = 1;
                while (len < n && kind[(j + len) % n] == kind[j]) len++;
                runs.Add((j, len, kind[j]));
                j = (j + len) % n;
            } while (j != first);
        }

        // the stone: the new wall in stone if the stores come to well over half of it (and the way is not the modest one)
        int newCells = 0;
        foreach (var run in runs) if (run.kind == LineNew) newCells += run.len;
        double need = newCells * NavGrid.CS * (double)WallThick * h;
        p.Stone = p.Style != ArchStyle.Modest && Stock[Stockpile.Stone] >= need * .6;
        // a gatehouse with its iron lintel and leaves, if there is the metal for it
        p.Gatehouse = p.Stone && HasMetal(.6f);

        var works = p.Works;
        var gates = new List<(int x, int z, int work)>();
        var mendOf = new Dictionary<int, int>();
        int Add(ArchWork w) { works.Add(w); return works.Count - 1; }

        // what stands: mended, put in stone, raised
        var stoneAfter = new List<(int unbuild, int[] line, int run)>();
        for (int ri = 0; ri < runs.Count; ri++)
        {
            var (from, len, k) = runs[ri];
            if (k is not (LineStands or LineTimber)) continue;
            var cells = new List<int>();
            var near = new HashSet<int>();
            int timber = 0, brokenCells = 0;
            var rises = new List<int>();
            for (int t = 0; t < len; t++)
            {
                int c = ring[(from + t) % n];
                cells.Add(c);
                if (s.Has(c, Lie.Timber)) timber++;
                if (s.Has(c, Lie.Broken)) brokenCells++;
                if (s.Has(c, Lie.Wall)) rises.Add(s.Rise[c]);
                int gi = c % s.NI, gk = c / s.NI;
                for (int dk = -1; dk <= 1; dk++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int ni = gi + di, nk = gk + dk;
                        if (ni < 0 || nk < 0 || ni >= s.NI || nk >= s.NK) continue;
                        // (a stone run's own masonry, towers and gateways; a palisade's own logs: the one is not mended as the other)
                        int o = s.Idx(ni, nk);
                        if (s.Has(o, k == LineTimber ? Lie.Timber : Lie.Wall | Lie.Gate | Lie.Tall) && !s.Has(o, Lie.House)) near.Add(o);
                    }
            }
            foreach (int c in near) if (s.Has(c, Lie.Broken)) brokenCells++;
            var cols = new List<int>();
            foreach (int c in near) cols.Add(((s.K0 + c / s.NI) * Nav.NX) + s.I0 + c % s.NI);
            bool isTimber = k == LineTimber;
            // a broken palisade is mended first unless the stone to put it in stone is there already
            if (brokenCells > 0 && (!isTimber || p.Style == ArchStyle.Modest || Stock[Stockpile.Stone] < len * NavGrid.CS * (double)WallThick * h * .8))
                mendOf[ri] = Add(new ArchWork { Kind = ArchWorkKind.Mend, Stage = 6, Name = isTimber ? "починить частокол" : "починить стену", Cells = cols.ToArray() });
            if (isTimber && p.Style != ArchStyle.Modest)
            {
                // the palisade put in stone, once the stores have the stone for it: taken down, and a stone wall along its line
                int un = Add(new ArchWork { Kind = ArchWorkKind.Unbuild, Stage = 5, Name = "частокол долой", Cells = cols.ToArray(),
                    After = mendOf.TryGetValue(ri, out int mended) ? 1UL << mended : 0 });
                var line = new List<(int x, int z)> { mids[(from - 1 + n) % n] };
                for (int t = 0; t < len; t++) line.Add(mids[(from + t) % n]);
                line.Add(mids[(from + len) % n]);
                stoneAfter.Add((un, Flat(Simplify(line, 4, null)), ri));
            }
            if (!isTimber && rises.Count > 0)
            {
                rises.Sort();
                int rise = rises[rises.Count / 2];
                if (rise < h - 6)
                    Add(new ArchWork { Kind = ArchWorkKind.Raise, Stage = 7, Name = "поднять стену", H = h, Cells = cols.ToArray(),
                        After = mendOf.TryGetValue(ri, out int m) ? 1UL << m : 0 });
            }
        }

        // the gates of ours on the line with their leaves gone: hung again
        foreach (var gt in Gates)
        {
            if (!gt.Broken) continue;
            var (mx, _, mz) = gt.Middle;
            if (InHouse((int)mx, (int)mz, 4)) continue;
            int c = s.CellAt((int)mx, (int)mz);
            if (c < 0 || !OnRing(ring, s, c, 2)) continue;
            Add(new ArchWork { Kind = ArchWorkKind.Hang, Stage = 9, Name = "навесить ворота", X = (int)mx, Z = (int)mz });
        }

        // new wall: each run from the wall before it to the wall after it, simplified; square across a road for its gate
        var parts = new List<(List<(int x, int z)> line, List<(int x, int z, int ox, int oz)> gates)>();
        for (int ri = 0; ri < runs.Count; ri++)
        {
            var (from, len, k) = runs[ri];
            if (k != LineNew) continue;
            bool whole = len == n;
            var line = new List<(int x, int z)>();
            if (!whole) line.Add(mids[(from - 1 + n) % n]);
            for (int t = 0; t < len; t++) line.Add(mids[(from + t) % n]);
            line.Add(whole ? mids[from] : mids[(from + len) % n]);
            line = Simplify(line, 6, Clear);
            // the roads it crosses: a straight stretch along x or z there
            var here = new List<(int x, int z, int ox, int oz)>();
            foreach (int c in crossings)
            {
                int off = (c - from + n) % n;
                if (off >= len) continue;
                var at = mids[c];
                bool roadX = s.Has(ring[c], Lie.RoadAlongX);
                int half = p.Gatehouse ? 60 : 28;
                if (!StraightAt(line, at, roadX, half, Clear, out var g)) continue;
                // outward: away from the point, along the road
                int ox = roadX ? Math.Sign(g.x - cx) : 0, oz = roadX ? 0 : Math.Sign(g.z - cz);
                if (ox == 0 && oz == 0) ox = 1;
                here.Add((g.x, g.z, ox, oz));
            }
            // split at the corners and at every 30 m or so, never inside a gate's stretch
            foreach (var part in SplitLine(line, here)) parts.Add((part, here));
        }
        // the side the horde comes from first
        float Facing(List<(int x, int z)> part) { var m = part[part.Count / 2]; return Toward(m); }
        parts.Sort((a, b) => Facing(b.line).CompareTo(Facing(a.line)));
        foreach (var (part, here) in parts)
        {
            int wi = Add(new ArchWork { Kind = ArchWorkKind.Wall, Stage = 0, Name = p.Stone ? "стена" : "частокол", Stone = p.Stone, H = h, Thick = p.Stone ? WallThick : 3, Line = Flat(part) });
            if (!p.Stone && p.Style != ArchStyle.Modest)
            {
                // the palisade put in stone later, as the stores allow
                int un = Add(new ArchWork { Kind = ArchWorkKind.Unbuild, Stage = 5, Name = "частокол долой", Line = Flat(part), Thick = 3, After = 1UL << wi });
                stoneAfter.Add((un, Flat(part), -1));
            }
            foreach (var (gx, gz, ox, oz) in here)
                if (OnLine(part, gx, gz, 2))
                {
                    int gw;
                    if (p.Gatehouse && gates.Count == 0)
                        gw = Add(new ArchWork { Kind = ArchWorkKind.Gatehouse, Stage = 1, Name = "надвратная башня", X = gx, Z = gz, Rot = OutRot(ox, oz), After = 1UL << wi });
                    else
                        gw = Add(new ArchWork { Kind = ArchWorkKind.Gate, Stage = 1, Name = "ворота", X = gx, Z = gz, Rot = SideOf(ox, oz), After = 1UL << wi });
                    gates.Add((gx, gz, gw));
                }
        }
        // the roads crossing what stands with no gate of ours near: a gate cut through it
        foreach (int c in crossings)
        {
            int j = c;
            if (kind[j] is not (LineStands or LineTimber)) continue;
            var at = mids[j];
            bool near = false;
            foreach (var gt in Gates)
            {
                var (mx, _, mz) = gt.Middle;
                if (!InHouse((int)mx, (int)mz, 4) && Brushes.Dist(mx - at.x, mz - at.z) < 72) near = true;
            }
            if (near) continue;
            bool roadX = s.Has(ring[j], Lie.RoadAlongX);
            int ox = roadX ? Math.Sign(at.x - cx) : 0, oz = roadX ? 0 : Math.Sign(at.z - cz);
            if (ox == 0 && oz == 0) ox = 1;
            int ri = RunOf(runs, j, n);
            int gw = Add(new ArchWork { Kind = ArchWorkKind.Gate, Stage = 1, Name = "ворота", X = at.x, Z = at.z, Rot = SideOf(ox, oz),
                After = mendOf.TryGetValue(ri, out int m) ? 1UL << m : 0 });
            gates.Add((at.x, at.z, gw));
        }
        // the palisades put in stone: the stone wall along each once its timber is down
        foreach (var (un, line, _) in stoneAfter)
        {
            var part = Unflat(line);
            if (part.Count < 2) continue;
            Add(new ArchWork { Kind = ArchWorkKind.Wall, Stage = 5, Name = "стена вместо частокола", Stone = true, H = h, Thick = WallThick, Line = line, After = 1UL << un });
        }

        // the line as drawn: what stands and what is new, its corners
        var simple = SimplifyLoop(mids, 6);
        p.Ring.Clear(); p.Edge.Clear();
        for (int v = 0; v < simple.Count; v++)
        {
            int a = simple[v], b = simple[(v + 1) % simple.Count];
            int stands = 0, natural = 0, all = 0;
            for (int j = a; j != b; j = (j + 1) % n) { all++; if (kind[j] != LineNew) stands++; if (kind[j] == LineNature) natural++; }
            p.Ring.Add(mids[a]);
            p.Edge.Add(stands * 2 > all ? (natural * 2 > all ? LineNature : LineStands) : LineNew);
        }
        foreach (var (x, z) in p.Ring) p.H = Math.Max(p.H, (int)Brushes.Dist(x - cx, z - cz));

        // towers: on the corners of the line and halfway along a long side, none where a tower stands, by a gate, on water
        var corners = new List<(int x, int z)>();
        for (int v = 0; v < simple.Count; v++)
        {
            var a = mids[simple[(v - 1 + simple.Count) % simple.Count]]; var b = mids[simple[v]]; var c = mids[simple[(v + 1) % simple.Count]];
            if (Math.Abs(Turn(a, b, c)) >= 35 * Math.PI / 180) corners.Add(b);
        }
        // a corner cut by two turns close together is one corner: its tower stands between them
        var sites = new List<(int x, int z)>();
        foreach (var (x, z) in corners)
        {
            int at = sites.FindIndex(o => Brushes.Dist(o.x - x, o.z - z) < 72);
            if (at < 0) sites.Add((x, z));
            else sites[at] = ((sites[at].x + x) / 2, (sites[at].z + z) / 2);
        }
        corners = new List<(int x, int z)>(sites);
        if (p.Style != ArchStyle.Modest)
            for (int v = 0; v < corners.Count; v++)
            {
                var a = corners[v]; var b = corners[(v + 1) % corners.Count];
                if (Brushes.Dist(a.x - b.x, a.z - b.z) > 320) sites.Add(((a.x + b.x) / 2, (a.z + b.z) / 2));
            }
        if (p.Style == ArchStyle.Modest)
        {
            // the modest way keeps the two nearest the horde
            sites.Sort((a, b) => Toward(b).CompareTo(Toward(a)));
            if (sites.Count > 2) sites.RemoveRange(2, sites.Count - 2);
        }
        var towers = new List<(int x, int z)>();
        bool towerStone = p.Stone && p.Style == ArchStyle.Sky;
        // a post stands on open ground a few metres inside its corner (further in if that is taken)
        bool OpenFor(int x, int z)
        {
            for (int dz = -12; dz <= 12; dz += 6)
                for (int dx = -12; dx <= 12; dx += 6)
                {
                    int c = s.CellAt(x + dx, z + dz);
                    if (c < 0 || s.Has(c, Lie.Off | Lie.House | Lie.ByHouse | Lie.Roofed | Lie.Deep | Lie.Tall | Barring) || InCore(x + dx, z + dz, 4)) return false;
                }
            return true;
        }
        foreach (var (x, z) in sites)
        {
            if (towers.Count >= 6) break;
            int at = s.CellAt(x, z);
            if (at < 0 || s.Has(at, Lie.Deep | Lie.Cliff | Lie.Off)) continue;
            if (TallNear(s, x, z, 40)) continue;
            bool clash = false;
            foreach (var (gx, gz, _) in gates) if (Brushes.Dist(gx - x, gz - z) < 64) clash = true;
            foreach (var gt in Gates) { var (mx, _, mz) = gt.Middle; if (!InHouse((int)mx, (int)mz, 4) && Brushes.Dist(mx - x, mz - z) < 56) clash = true; }
            foreach (var (tx, tz) in towers) if (Brushes.Dist(tx - x, tz - z) < 72) clash = true;
            if (clash) continue;
            ulong after = 0;
            for (int w = 0; w < works.Count; w++)
                if (works[w].Kind is ArchWorkKind.Wall or ArchWorkKind.Mend && NearWork(s, works[w], x, z, 48)) after |= 1UL << w;
            float il = (float)Math.Max(1, Brushes.Dist(cx - x, cz - z));
            if (towerStone)
            {
                towers.Add((x, z));
                Add(new ArchWork { Kind = ArchWorkKind.Tower, Stage = 2, Name = "башня на углу", X = x, Z = z, H = h + 8, Rot = InRot(cx - x, cz - z), After = after });
                continue;
            }
            foreach (int inward in new[] { 22, 32, 44 })
            {
                int px = x + (int)Math.Round((cx - x) / il * inward), pz = z + (int)Math.Round((cz - z) / il * inward);
                if (!OpenFor(px, pz)) continue;
                towers.Add((x, z));
                Add(new ArchWork { Kind = ArchWorkKind.Post, Stage = 2, Name = "вышка в углу", X = px, Z = pz, Rot = InRot(cx - px, cz - pz), After = after });
                break;
            }
        }

        // stairs up the stone sides with no way up, the horde's side first
        int stairs = p.Style == ArchStyle.Modest ? 1 : p.Style == ArchStyle.Strong ? 2 : 3;
        var sides = SidesOfRing(simple, mids, corners, p.HordeSide);
        sides.Sort((a, b) => (b.facing * 1000 + b.len).CompareTo(a.facing * 1000 + a.len));
        foreach (var side in sides)
        {
            if (stairs <= 0) break;
            if (side.len < 96) continue;
            var (mx, mz, ux, uz) = side.mid;
            // outward to the right of the way along; in is the other way
            float ix = -uz, iz = ux;
            int jm = NearestOnRing(mids, mx, mz);
            bool stands = kind[jm] == LineStands, timber = kind[jm] == LineTimber || !stands && !p.Stone;
            if (timber || kind[jm] == LineNature) continue;
            int sx, sz, top;
            if (stands)
            {
                if (WayUp(s, ring, jm, (int)mx, (int)mz, ix, iz)) continue;
                // from the inner face of the wall, at the height of its walk
                int x = (int)mx, z = (int)mz, walk = 0;
                for (int t = 0; t < 32; t++)
                {
                    int xx = (int)Math.Round(mx + ix * t), zz = (int)Math.Round(mz + iz * t), g = GroundAt(xx, zz), top0 = Math.Max(OursTop(xx, zz, g), MeantTop(xx, zz, g));
                    if (top0 - g < Fortification.Bars) { if (walk > 0) break; continue; }
                    x = xx; z = zz; walk = top0;
                }
                if (walk == 0) continue;
                sx = x + (int)Math.Round(ix * 2); sz = z + (int)Math.Round(iz * 2); top = walk;
            }
            else
            {
                sx = (int)Math.Round(mx + ix * (WallThick / 2 + 2)); sz = (int)Math.Round(mz + iz * (WallThick / 2 + 2));
                top = GroundAt(sx, sz) + h;
            }
            // along the wall toward its longer half, or the other way if that runs onto the place or a house; neither: none
            bool Fits(int ex0, int ez0)
            {
                for (double t = 0; t <= 1; t += .05)
                    for (int across = -7; across <= 7; across += 7)
                    {
                        int x = (int)Math.Round(sx + (ex0 - sx) * t - uz * across), z = (int)Math.Round(sz + (ez0 - sz) * t + ux * across);
                        if (InCore(x, z, 4) || InHouse(x, z, 2)) return false;
                    }
                return true;
            }
            int dir = side.toEnd >= side.toStart ? 1 : -1;
            int ex = sx + (int)Math.Round(ux * 40 * dir), ez = sz + (int)Math.Round(uz * 40 * dir);
            if (!Fits(ex, ez)) { dir = -dir; ex = sx + (int)Math.Round(ux * 40 * dir); ez = sz + (int)Math.Round(uz * 40 * dir); }
            if (!Fits(ex, ez)) continue;
            ex = sx + (int)Math.Round(ux * 64 * dir); ez = sz + (int)Math.Round(uz * 64 * dir);
            ulong after = 0;
            for (int w = 0; w < works.Count; w++)
                if (works[w].Kind is ArchWorkKind.Wall or ArchWorkKind.Mend or ArchWorkKind.Raise && NearWork(s, works[w], (int)mx, (int)mz, 24)) after |= 1UL << w;
            Add(new ArchWork { Kind = ArchWorkKind.Stair, Stage = 8, Name = "лестница на стену", X = sx, Z = sz, H = top, Line = new[] { sx, sz, ex, ez }, After = after });
            stairs--;
        }

        // the ditch outside the line and the stakes before the horde's side, once the line is closed
        ulong lineMask = 0;
        for (int w = 0; w < works.Count; w++) if (works[w].Kind is ArchWorkKind.Wall or ArchWorkKind.Mend or ArchWorkKind.Gate or ArchWorkKind.Gatehouse or ArchWorkKind.Hang && works[w].Stage != 5) lineMask |= 1UL << w;
        if (p.Style != ArchStyle.Modest)
        {
            for (int q = 0; q < 4; q++) Add(new ArchWork { Kind = ArchWorkKind.Ditch, Stage = 3, Name = "ров", Quarter = q, After = lineMask });
            Add(new ArchWork { Kind = ArchWorkKind.Stakes, Stage = 4, Name = "колья", Quarter = p.HordeSide, After = lineMask });
            if (p.Style == ArchStyle.Sky)
            {
                Add(new ArchWork { Kind = ArchWorkKind.Stakes, Stage = 4, Name = "колья", Quarter = (p.HordeSide + 1) % 4, After = lineMask });
                Add(new ArchWork { Kind = ArchWorkKind.Stakes, Stage = 4, Name = "колья", Quarter = (p.HordeSide + 3) % 4, After = lineMask });
            }
        }
        if (works.Count > 64) works.RemoveRange(64, works.Count - 64);
        // the order he marks them in: mending and gates hung first, then the new wall from the horde's side, and so on
        // (the indices are the plan's bits, so the order is kept as it was added; the list is walked by stage below)
        p.GateSide = gates.Count > 0 ? SideToward(gates[0].x - cx, gates[0].z - cz) : (p.HordeSide + 2) % 4;
        p.Found = Says(p, runs, n, gates.Count, towers.Count, towerStone, stairs);
    }

    /// <summary>What he says of what he found and what he will do: «Старая стена стоит на 70 % круга. Архитектор закроет 2 пролома…».</summary>
    string Says(ArchPlan p, List<(int from, int len, byte kind)> runs, int n, int gates, int towers, bool towerStone, int stairsLeft)
    {
        int stands = 0, gaps = 0;
        foreach (var r in runs) { if (r.kind != LineNew) stands += r.len; else gaps++; }
        int share = (int)Math.Round(100.0 * stands / n);
        var w = p.Works;
        bool any(ArchWorkKind k) => w.Exists(x => x.Kind == k);
        int count(ArchWorkKind k) => w.FindAll(x => x.Kind == k).Count;
        string found = stands == 0 ? "Укреплений вокруг нет."
            : share >= 99 ? (any(ArchWorkKind.Mend) ? "Стена стоит кругом, местами разрушена." : "Стена стоит кругом.")
            : $"Стена стоит на {share} % круга.";
        // what he will put up, in one clause; the rest each by its own word
        var put = new List<string>();
        if (stands == 0) put.Add(p.Stone ? "каменную стену кругом" : "частокол кругом");
        if (gates > 0) put.Add(gates == 1 ? "ворота к дороге" : $"ворота к {gates} дорогам");
        if (towers > 0) put.Add(towerStone ? $"{towers} {Plural(towers, "башню", "башни", "башен")}" : $"{towers} {Plural(towers, "вышку", "вышки", "вышек")}");
        int st = count(ArchWorkKind.Stair);
        if (st > 0) put.Add(st == 1 ? "лестницу на стену" : $"{st} лестницы на стену");
        var will = new List<string>();
        if (stands > 0 && gaps > 0) will.Add($"закроет {gaps} {Plural(gaps, "пролом", "пролома", "проломов")}");
        if (any(ArchWorkKind.Mend)) will.Add("починит старую стену");
        if (any(ArchWorkKind.Hang)) will.Add("навесит ворота");
        if (put.Count > 0) will.Add("поставит " + (put.Count == 1 ? put[0] : string.Join(", ", put.GetRange(0, put.Count - 1)) + " и " + put[^1]));
        if (any(ArchWorkKind.Raise)) will.Add("поднимет низкую стену");
        if (w.Exists(x => x.Kind == ArchWorkKind.Unbuild && x.Cells.Length > 0)) will.Add("заменит частокол камнем");
        if (any(ArchWorkKind.Ditch)) will.Add("выкопает ров");
        if (any(ArchWorkKind.Stakes)) will.Add($"вобьёт колья {FromSide(p.HordeSide)}");
        if (will.Count == 0) return $"{found} Архитектору здесь нечего добавить.";
        string list = will.Count == 1 ? will[0] : string.Join(", ", will.GetRange(0, will.Count - 1)) + " и " + will[^1];
        return $"{found} Архитектор {list}.";
    }

    /// <summary>«с севера», «с востока»… for a side of the ring.</summary>
    static string FromSide(int side) => side switch { 0 => "с севера", 1 => "с востока", 2 => "с юга", _ => "с запада" };

    // ------------------------------------------------------------ geometry of the line

    /// <summary>Twice the area a closed line holds, signed: positive counter-clockwise (x east, z south).</summary>
    static double Area(List<(int x, int z)> pts)
    {
        double a = 0;
        for (int i = 0; i < pts.Count; i++) { var (x0, z0) = pts[i]; var (x1, z1) = pts[(i + 1) % pts.Count]; a += (double)x0 * z1 - (double)x1 * z0; }
        return a;
    }

    /// <summary>The turn at b going from a through b to c, radians (left positive).</summary>
    static double Turn((int x, int z) a, (int x, int z) b, (int x, int z) c)
    {
        double ux = b.x - a.x, uz = b.z - a.z, vx = c.x - b.x, vz = c.z - b.z;
        return Math.Atan2(ux * vz - uz * vx, ux * vx + uz * vz);
    }

    static int[] Flat(List<(int x, int z)> pts)
    {
        var a = new int[pts.Count * 2];
        for (int i = 0; i < pts.Count; i++) { a[2 * i] = pts[i].x; a[2 * i + 1] = pts[i].z; }
        return a;
    }

    static List<(int x, int z)> Unflat(int[] a)
    {
        var pts = new List<(int x, int z)>(a.Length / 2);
        for (int i = 0; i + 1 < a.Length; i += 2) pts.Add((a[i], a[i + 1]));
        return pts;
    }

    /// <summary>
    /// A line with its corners only (its ends kept), none further than so far from what it was; a stretch kept only if
    /// its band a wall wide is clear all along (when asked).
    /// </summary>
    static List<(int x, int z)> Simplify(List<(int x, int z)> pts, double eps, Func<int, int, bool> clear)
    {
        if (pts.Count <= 2) return new List<(int x, int z)>(pts);
        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        void Go(int a, int b)
        {
            if (b - a < 2) return;
            double best = -1; int at = -1;
            var (ax, az) = pts[a]; var (bx, bz) = pts[b];
            for (int i = a + 1; i < b; i++)
            {
                double d = SegDist(pts[i].x, pts[i].z, ax, az, bx, bz);
                if (d > best) { best = d; at = i; }
            }
            if (best > eps || clear != null && !BandClear(ax, az, bx, bz, clear)) { keep[at] = true; Go(a, at); Go(at, b); }
        }
        Go(0, pts.Count - 1);
        var outp = new List<(int x, int z)>();
        for (int i = 0; i < pts.Count; i++) if (keep[i]) outp.Add(pts[i]);
        return outp;
    }

    /// <summary>The corners of a closed line (indices into it), none further than so far from it.</summary>
    static List<int> SimplifyLoop(List<(int x, int z)> pts, double eps)
    {
        int n = pts.Count, far = 0;
        double fd = -1;
        for (int i = 1; i < n; i++) { double d = Brushes.Dist(pts[i].x - pts[0].x, pts[i].z - pts[0].z); if (d > fd) { fd = d; far = i; } }
        var a = new List<(int x, int z)>(); var ai = new List<int>();
        for (int i = 0; i <= far; i++) { a.Add(pts[i]); ai.Add(i); }
        var b = new List<(int x, int z)>(); var bi = new List<int>();
        for (int i = far; i <= n; i++) { b.Add(pts[i % n]); bi.Add(i % n); }
        var keepA = Simplify(a, eps, null); var keepB = Simplify(b, eps, null);
        var outp = new List<int>();
        int k = 0;
        for (int i = 0; i < a.Count && k < keepA.Count; i++) if (a[i] == keepA[k]) { outp.Add(ai[i]); k++; }
        k = 1;
        for (int i = 1; i < b.Count - 1 && k < keepB.Count - 1; i++) if (b[i] == keepB[k]) { outp.Add(bi[i]); k++; }
        if (outp.Count < 3) { outp.Clear(); for (int i = 0; i < n; i += Math.Max(1, n / 8)) outp.Add(i); }
        return outp;
    }

    static double SegDist(double px, double pz, double ax, double az, double bx, double bz)
    {
        double dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz, t = l2 < 1e-9 ? 0 : Math.Clamp(((px - ax) * dx + (pz - az) * dz) / l2, 0, 1);
        double ex = ax + dx * t - px, ez = az + dz * t - pz;
        return Math.Sqrt(ex * ex + ez * ez);
    }

    /// <summary>Whether a band a wall wide (and a little) along a stretch is clear to build on all along.</summary>
    static bool BandClear(int ax, int az, int bx, int bz, Func<int, int, bool> clear)
    {
        double len = Math.Max(1, Math.Sqrt((double)(bx - ax) * (bx - ax) + (double)(bz - az) * (bz - az)));
        double ux = (bx - ax) / len, uz = (bz - az) / len;
        for (double t = 0; t <= len; t += 3)
            foreach (int side in new[] { 0, -5, 5 })
                if (!clear((int)Math.Round(ax + ux * t - uz * side), (int)Math.Round(az + uz * t + ux * side))) return false;
        return true;
    }

    /// <summary>
    /// A line made to run straight along x or z for so far either side of where a road crosses it, square to the road:
    /// a gate hangs only in a wall that runs so. False (and the line as it was) if there is no room or it would cross what
    /// it may not.
    /// </summary>
    static bool StraightAt(List<(int x, int z)> line, (int x, int z) at, bool roadAlongX, int half, Func<int, int, bool> clear, out (int x, int z) gate)
    {
        gate = at;
        // the nearest point of the line, and how far along it that is
        double best = double.MaxValue, along = 0, run = 0;
        int seg = 0;
        (double x, double z) near = at;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var (ax, az) = line[i]; var (bx, bz) = line[i + 1];
            double dx = bx - ax, dz = bz - az, l = Math.Sqrt(dx * dx + dz * dz), t = l < 1e-9 ? 0 : Math.Clamp(((at.x - ax) * dx + (at.z - az) * dz) / (l * l), 0, 1);
            double px = ax + dx * t, pz = az + dz * t, d = (px - at.x) * (px - at.x) + (pz - at.z) * (pz - at.z);
            if (d < best) { best = d; along = run + t * l; seg = i; near = (px, pz); }
            run += l;
        }
        if (along < half + 8 || run - along < half + 8) return false;
        // the wall runs along z where the road runs along x, and the other way about; the same way as the line goes there
        var (sx, sz) = line[seg]; var (tx, tz) = line[seg + 1];
        int ux = roadAlongX ? 0 : Math.Sign(tx - sx), uz = roadAlongX ? Math.Sign(tz - sz) : 0;
        if (ux == 0 && uz == 0) { if (roadAlongX) uz = 1; else ux = 1; }
        var g = ((int)Math.Round(near.x), (int)Math.Round(near.z));
        var g1 = (g.Item1 - ux * half, g.Item2 - uz * half); var g2 = (g.Item1 + ux * half, g.Item2 + uz * half);
        var before = new List<(int x, int z)>(); var after = new List<(int x, int z)>();
        run = 0;
        before.Add(line[0]);
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var (ax, az) = line[i]; var (bx, bz) = line[i + 1];
            double l = Math.Sqrt((double)(bx - ax) * (bx - ax) + (double)(bz - az) * (bz - az));
            double end = run + l;
            if (end < along - half - 4) before.Add(line[i + 1]);
            else if (end > along + half + 4) after.Add(line[i + 1]);
            run = end;
        }
        if (after.Count == 0) return false;
        var outp = new List<(int x, int z)>(before) { g1, g2 };
        outp.AddRange(after);
        for (int i = Math.Max(0, before.Count - 1); i + 1 < before.Count + 3 && i + 1 < outp.Count; i++)
            if (!BandClear(outp[i].x, outp[i].z, outp[i + 1].x, outp[i + 1].z, clear)) return false;
        line.Clear(); line.AddRange(outp);
        gate = g;
        return true;
    }

    /// <summary>
    /// A line cut into the pieces it is built in: at its corners (a turn of 35° and more) and every 30 m or so, never
    /// within a gate's stretch.
    /// </summary>
    static List<List<(int x, int z)>> SplitLine(List<(int x, int z)> line, List<(int x, int z, int ox, int oz)> gates)
    {
        var parts = new List<List<(int x, int z)>>();
        var cur = new List<(int x, int z)> { line[0] };
        double len = 0;
        for (int i = 1; i < line.Count; i++)
        {
            var a = line[i - 1]; var b = line[i];
            len += Brushes.Dist(b.x - a.x, b.z - a.z);
            cur.Add(b);
            if (i == line.Count - 1) break;
            bool corner = Math.Abs(Turn(a, b, line[i + 1])) >= 35 * Math.PI / 180;
            bool byGate = false;
            foreach (var (gx, gz, _, _) in gates) if (Brushes.Dist(gx - b.x, gz - b.z) < 72) byGate = true;
            if ((corner && len >= 48 || len > 240) && !byGate) { parts.Add(cur); cur = new List<(int x, int z)> { b }; len = 0; }
        }
        if (cur.Count >= 2) parts.Add(cur);
        return parts;
    }

    /// <summary>Whether a point lies on a line within so far.</summary>
    static bool OnLine(List<(int x, int z)> line, int x, int z, double by)
    {
        for (int i = 0; i + 1 < line.Count; i++) if (SegDist(x, z, line[i].x, line[i].z, line[i + 1].x, line[i + 1].z) <= by) return true;
        return false;
    }

    /// <summary>Whether a cell is on the line or within so many cells of it.</summary>
    static bool OnRing(List<int> ring, ArchGround s, int c, int by)
    {
        int gi = c % s.NI, gk = c / s.NI;
        foreach (int r in ring) if (Math.Max(Math.Abs(r % s.NI - gi), Math.Abs(r / s.NI - gk)) <= by) return true;
        return false;
    }

    static int RunOf(List<(int from, int len, byte kind)> runs, int j, int n)
    {
        for (int i = 0; i < runs.Count; i++) if ((j - runs[i].from + n) % n < runs[i].len) return i;
        return -1;
    }

    static int NearestOnRing(List<(int x, int z)> mids, float x, float z)
    {
        int best = 0; float bd = float.MaxValue;
        for (int j = 0; j < mids.Count; j++) { float d = (mids[j].x - x) * (mids[j].x - x) + (mids[j].z - z) * (mids[j].z - z); if (d < bd) { bd = d; best = j; } }
        return best;
    }

    /// <summary>Whether something stands a tower's height within so far of a column.</summary>
    static bool TallNear(ArchGround s, int x, int z, int by)
    {
        for (int dz = -by; dz <= by; dz += 4)
            for (int dx = -by; dx <= by; dx += 4)
            {
                if (dx * dx + dz * dz > by * by) continue;
                int c = s.CellAt(x + dx, z + dz);
                if (c >= 0 && s.Has(c, Lie.Tall)) return true;
            }
        return false;
    }

    /// <summary>Whether a work of the plan is within so far of a point (its line, or its cells).</summary>
    bool NearWork(ArchGround s, ArchWork w, int x, int z, int by)
    {
        if (w.Line.Length >= 4)
        {
            var line = Unflat(w.Line);
            return OnLine(line, x, z, by);
        }
        foreach (int col in w.Cells)
        {
            int cx = (col % Nav.NX << NavGrid.Shift) + 4, cz = (col / Nav.NX << NavGrid.Shift) + 4;
            if (Brushes.Dist(cx - x, cz - z) <= by) return true;
        }
        return false;
    }

    /// <summary>
    /// The sides of the line between its corners (in quarters if it has fewer than three): the middle of each (a point and
    /// the way along there), how long it is, how far along to its middle from either end (the same), and how much it
    /// faces the horde (1 the most, 0 not at all).
    /// </summary>
    static List<((float x, float z, float ux, float uz) mid, double len, double toStart, double toEnd, double facing)> SidesOfRing(
        List<int> simple, List<(int x, int z)> mids, List<(int x, int z)> corners, int hordeSide)
    {
        var list = new List<((float, float, float, float), double, double, double, double)>();
        var verts = simple.ConvertAll(i => mids[i]);
        int m = verts.Count;
        var set = new HashSet<(int, int)>(corners);
        var cut = new List<int>();
        for (int v = 0; v < m; v++) if (set.Contains(verts[v])) cut.Add(v);
        if (cut.Count < 3) { cut.Clear(); for (int q = 0; q < Math.Min(4, m); q++) if (!cut.Contains(q * m / Math.Min(4, m))) cut.Add(q * m / Math.Min(4, m)); }
        int hx = RingSides[hordeSide].ox, hz = RingSides[hordeSide].oz;
        for (int ci = 0; ci < cut.Count; ci++)
        {
            int a = cut[ci], b = cut[(ci + 1) % cut.Count];
            var pts = new List<(int x, int z)>();
            for (int v = a; ; v = (v + 1) % m) { pts.Add(verts[v]); if (v == b && pts.Count > 1) break; }
            double total = 0;
            for (int i = 0; i + 1 < pts.Count; i++) total += Brushes.Dist(pts[i + 1].x - pts[i].x, pts[i + 1].z - pts[i].z);
            double half = total / 2, run = 0;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var pa = pts[i]; var pb = pts[i + 1];
                double l = Brushes.Dist(pb.x - pa.x, pb.z - pa.z);
                if (run + l >= half && l > 0)
                {
                    double t = (half - run) / l;
                    float ux = (float)((pb.x - pa.x) / l), uz = (float)((pb.z - pa.z) / l);
                    // outward is (uz, −ux) for the line counter-clockwise
                    double facing = Math.Max(0, uz * hx - ux * hz);
                    list.Add((((float)(pa.x + (pb.x - pa.x) * t), (float)(pa.z + (pb.z - pa.z) * t), ux, uz), total, half, total - half, facing));
                    break;
                }
                run += l;
            }
        }
        return list;
    }

    /// <summary>
    /// Whether a man inside can get up onto a stretch of wall that stands: from the ground two metres in from its middle,
    /// by its ladders and stairs, onto its top within a few metres.
    /// </summary>
    bool WayUp(ArchGround s, List<int> ring, int jm, int mx, int mz, float ix, float iz)
    {
        int from = NearestStand((int)(mx + ix * 24), (int)(mz + iz * 24), 2);
        if (from < 0) return true;
        var tops = new HashSet<int>();
        for (int d = -12; d <= 12; d++)
        {
            int c = ring[(jm + d + ring.Count) % ring.Count];
            tops.Add(((s.K0 + c / s.NI) * Nav.NX) + s.I0 + c % s.NI);
        }
        var way = Paths.Find(from, c =>
        {
            int col = Nav.K(c) * Nav.NX + Nav.I(c);
            if (!tops.Contains(col)) return false;
            var (x, _, z) = Nav.Feet(c);
            return Nav.Floor[c] - GroundAt((int)x, (int)z) >= Fortification.Bars;
        }, c => 0, Kinds.Worker.Move, 6000);
        return way != null;
    }

    /// <summary>One over the highest meant voxel of ours in a column that bars the way, over the ground; 0 for none.</summary>
    int MeantTop(int x, int z, int ground)
    {
        for (int y = Math.Min(World.SY - 1, ground + 80); y >= Math.Max(1, ground); y--)
        {
            byte m = Design.Get(x, y, z);
            if (m != 0 && Mat.IsPlayer[m] && !Mat.Passable[m] && !Mat.IsDecor[m]) return y + 1;
        }
        return 0;
    }

    /// <summary>The ring's side (0 north … 3 west) a step out along x or z faces.</summary>
    static int SideOf(int ox, int oz) => oz < 0 ? 0 : ox > 0 ? 1 : oz > 0 ? 2 : 3;

    /// <summary>A gatehouse's turn for a way out: the turn whose facing is that way (Sim.Templates.cs, Facing4).</summary>
    static int OutRot(int ox, int oz) => oz > 0 ? 0 : ox > 0 ? 1 : oz < 0 ? 2 : 3;

    /// <summary>The turn (as <see cref="OutRot"/>) facing the nearest of x and z toward a way: a tower's stair, a post's ladder, inward.</summary>
    static int InRot(int dx, int dz) => Math.Abs(dx) >= Math.Abs(dz) ? (dx > 0 ? 1 : 3) : (dz > 0 ? 0 : 2);

    /// <summary>
    /// Stone walls laid at once along a line of corners, as if the household had built them by hand (for the bench and
    /// the pictures): so thick, standing so high over the ground of each column, merlons along the side away from the
    /// point the valley holds, every voxel remembered as built; and, if asked, a gate cut and hung where a road crosses
    /// a stretch that runs along x or z.
    /// </summary>
    public VoxelList RaiseWalls(IReadOnlyList<(int x, int z)> corners, bool closed, int h, int thick, bool gates = false)
    {
        var laid = new VoxelList();
        var (px, pz, _) = PointToHold();
        int n = corners.Count, segs = closed ? n : n - 1;
        var stretches = new List<(int ax, int az, int bx, int bz, int nx, int nz)>();
        for (int s = 0; s < segs; s++)
        {
            var (ax, az) = corners[s];
            var (bx, bz) = corners[(s + 1) % n];
            double len = Math.Sqrt((double)(bx - ax) * (bx - ax) + (double)(bz - az) * (bz - az));
            if (len < 1) continue;
            double ux = (bx - ax) / len, uz = (bz - az) / len, nx = -uz, nz = ux;
            if (nx * ((ax + bx) / 2.0 - px) + nz * ((az + bz) / 2.0 - pz) < 0) { nx = -nx; nz = -nz; }
            double half = thick / 2.0;
            for (int z = Math.Min(az, bz) - thick; z <= Math.Max(az, bz) + thick; z++)
                for (int x = Math.Min(ax, bx) - thick; x <= Math.Max(ax, bx) + thick; x++)
                {
                    if (!World.OnLand(x, z) || Site.Forbidden(x, z)) continue;
                    double rx = x - ax, rz = z - az, along = rx * ux + rz * uz, across = rx * nx + rz * nz;
                    if (along < -half || along > len + half || Math.Abs(across) > half) continue;
                    int g = GroundAt(x, z), top = g + h;
                    for (int y = g; y < top && y < World.SY; y++)
                        if (Brushes.Free(World.Get(x, y, z))) { byte m = Shapes.Masonry(x, y, z); World.Set(x, y, z, m); Remember(x, y, z, m); laid.Add(x, y, z, m); }
                    if (across < half - 2 || !Merlon(along + half)) continue;
                    for (int y = top; y < top + 5 && y < World.SY; y++)
                        if (Brushes.Free(World.Get(x, y, z))) { byte m = Shapes.Masonry(x, y, z); World.Set(x, y, z, m); Remember(x, y, z, m); laid.Add(x, y, z, m); }
                }
            stretches.Add((ax, az, bx, bz, Math.Sign((int)Math.Round(nx)), Math.Sign((int)Math.Round(nz))));
        }
        if (gates)
            foreach (var (ax, az, bx, bz, nx, nz) in stretches)
            {
                // a stretch along x or z only: a gate hangs square to the wall
                if (ax != bx && az != bz || nx != 0 && nz != 0) continue;
                foreach (var road in Site.Roads)
                    for (int i = 0; i + 1 < road.Count; i++)
                    {
                        if (!Crosses(ax, az, bx, bz, road[i].X, road[i].Z, road[i + 1].X, road[i + 1].Z, out float u)) continue;
                        int gx = (int)Math.Round(ax + (bx - ax) * u), gz = (int)Math.Round(az + (bz - az) * u);
                        if (CutPlan(gx, gz, nx, nz) is { } plan) AddGate(plan, built: true);
                    }
            }
        Nav.Flush();
        return laid;
    }

    /// <summary>
    /// The gate a wall of ours would take at a point of its line, looking out so (a step along x or z): the outer face
    /// found by going in from outside, a metre over the ground, and the gate planned there or a cell or two along the
    /// wall. Null where no gate will go.
    /// </summary>
    GatePlan CutPlan(int gx, int gz, int ox, int oz)
    {
        foreach (int shift in new[] { 0, 8, -8, 16, -16 })
        {
            int sx = gx + (ox == 0 ? shift : 0), sz = gz + (oz == 0 ? shift : 0);
            int y = GroundAt(sx, sz) + 8, x = sx + ox * 40, z = sz + oz * 40, k = 0;
            for (; k < 60 && !(Mat.IsPlayer[World.Get(x, y, z)] && Blocking(World.Get(x, y, z))); k++) { x -= ox; z -= oz; }
            if (k >= 60) continue;
            // the threshold on the ground there, whatever lies or stands inside the wall (a scaffold, rubble)
            var hit = new VoxelHit { X = x, Y = y, Z = z, NX = ox, NZ = oz, M = World.Get(x, y, z) };
            var plan = GatePlan.At(World, Site, hit, threshold: GroundAt(sx, sz));
            if (plan != null && plan.Bears(World)) return plan;
        }
        return null;
    }
}
