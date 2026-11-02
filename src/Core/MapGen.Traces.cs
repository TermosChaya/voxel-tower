using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>The old works of people the wild of a valley keeps (<see cref="MapGen"/>, its traces).</summary>
public enum OldWork : byte { Shrine, Milestone, Cairn, Standing, Ring, Ruin, Footing, Logs, Fence }

public sealed partial class Site
{
    /// <summary>The old works of people laid in the valley's wild when it was made: what, and where.</summary>
    public readonly List<(OldWork Kind, int X, int Z)> Traces = new();
    /// <summary>Whether the valley was made with them (<see cref="MapParams.Traces"/>), for a save to make it again so.</summary>
    public bool Traced;
}

public static partial class MapGen
{
    /// <summary>
    /// The last touch of a valley made by itself: what people who passed through it long before the citadel left in its
    /// wild (the author: «Больше декораций богу декораций»). By the roads, wayside crosses on stone posts under little
    /// roofs and milestones; on the heights cairns; standing stones alone and in rings, most of them in the holy valley;
    /// the low walls of old enclosures and the footings of houses long gone, overgrown with moss; woodcutters' piles of
    /// logs gone grey at the forest's edges; the posts of old fences across the meadows. How much of each the kind of
    /// valley says, the horde's bog least. All by the valley's seed, on open wild ground well away from the citadel, the
    /// roads' beds, the river, the named places, the ore and the horde's strip, and apart from one another; nothing else
    /// of the valley is moved by them.
    /// </summary>
    static void Traces(VoxelWorld w, Site site, MapParams p)
    {
        var hex = site.Hex;
        if (hex == null || site.GroundKind == null) return;
        int SX = w.SX;
        var kind = site.GroundKind;
        uint seed = p.Seed;
        int salt = (int)(seed * 2246822519u >> 11);
        double H(int a, int b, int c) => Hash.H3(a + salt, b - salt * 5, c + 977);
        var laid = new List<(int x, int z, int r)>();

        // ---- where an old work may stand: wild ground, dry, level enough, nothing standing on it
        bool Wild(int x, int z, int margin, bool heights)
        {
            if (!w.OnLand(x, z) || hex.Edge(x, z) < margin + 16 || site.InStrip(x, z) || site.Forbidden(x, z)) return false;
            int i = z * SX + x;
            byte k = kind[i];
            if (!(k is Relief.Low or Relief.Bank || heights && k is Relief.Knoll or Relief.Crag)) return false;
            if (site.WaterTop != null && site.WaterTop[i] > 0 || site.HTop[i] > w.SY - 40) return false;
            return Brushes.Dist(x - site.KeepX, z - site.KeepZ) > site.KeepR + 300 && !Landmarks.Taken(site, x, z, margin + 6);
        }
        bool Apart(int x, int z, int r)
        {
            foreach (var (lx, lz, lr) in laid) if (Brushes.Dist(x - lx, z - lz) < r + lr + 48) return false;
            return true;
        }
        // open ground in a box: natural ground at the top of each column, nothing over it but sprigs, the lowest and the
        // highest no more apart than so much; the highest ground
        int Open(int x0, int z0, int x1, int z1, int level, bool heights, bool byRoad = false)
        {
            // (off the roads, but for what is set by one: its corners and its middle two metres from a road's line)
            if (!byRoad)
                foreach (var (cx, cz) in new[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1), ((x0 + x1) / 2, (z0 + z1) / 2) })
                    if (site.RoadDist(cx, cz) < 16) return -1;
            int lo = int.MaxValue, hi = int.MinValue;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!Wild(x, z, 0, heights)) return -1;
                    int g = site.HTop[z * SX + x];
                    byte top = w.Get(x, g - 1, z);
                    if (!(Mat.IsGrass(top) || top is Mat.Soil or Mat.Moss or Mat.Peat or Mat.Rock or Mat.Scree or Mat.RockPale or Mat.Sand or Mat.Snow)) return -1;
                    for (int y = g; y < g + 14 && y < w.SY; y++)
                    {
                        byte m = w.Get(x, y, z);
                        if (m != Mat.Air && !Mat.IsSprig(m)) return -1;
                    }
                    lo = Math.Min(lo, g); hi = Math.Max(hi, g);
                }
            return hi - lo <= level ? hi : -1;
        }
        void Put(int x, int y, int z, byte m)
        {
            if (!w.InBounds(x, y, z)) return;
            byte was = w.Get(x, y, z);
            if (was == Mat.Air || Mat.IsSprig(was) || Mat.IsVeg[was] && !Mat.IsTrunk(was)) w.Set(x, y, z, m);
        }
        // a column of something from the ground under it up to a height over the given ground
        void Column(int x, int z, int top, byte m)
        {
            for (int y = site.HTop[z * SX + x]; y <= top; y++) Put(x, y, z, m);
        }
        byte Weathered(int x, int y, int z) => Hash.H3(x * 3 + salt, y * 7, z * 5) < .22 ? Mat.Moss : Mat.Ruin;

        // ---- how much of each the kind of valley has
        var k0 = p.Kind;
        int shrines = k0 == ValleyKind.Bog ? 0 : k0 == ValleyKind.Sacred ? 4 : 2;
        int miles = k0 == ValleyKind.Bog ? 1 : 3;
        int cairns = k0 == ValleyKind.Mountain ? 9 : k0 is ValleyKind.Ore or ValleyKind.Sacred ? 3 : 1;
        int standing = k0 == ValleyKind.Sacred ? 5 : k0 is ValleyKind.First or ValleyKind.Marsh ? 2 : 1;
        int rings = k0 == ValleyKind.Sacred ? 2 : k0 == ValleyKind.First ? 1 : 0;
        int ruins = k0 is ValleyKind.Forest or ValleyKind.Mountain or ValleyKind.Ore ? 3 : k0 == ValleyKind.Bog ? 1 : 2;
        int footings = k0 is ValleyKind.First or ValleyKind.Marsh or ValleyKind.Forest ? 2 : 1;
        int logs = k0 == ValleyKind.Forest ? 5 : k0 == ValleyKind.Bog ? 0 : 2;
        int fences = k0 is ValleyKind.First or ValleyKind.Marsh ? 3 : k0 is ValleyKind.Bog or ValleyKind.Mountain ? 0 : 1;

        // ---- by the roads: a cross on a stone post under a little roof, its niche to the road; milestones
        var roadSpots = new List<(int x, int z, int nx, int nz)>();
        foreach (var road in site.Roads)
            for (int i = 0; i + 1 < road.Count; i++)
            {
                float ax = road[i].X, az = road[i].Z, bx = road[i + 1].X, bz = road[i + 1].Z, len = MathF.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                if (len < 1) continue;
                float ux = (bx - ax) / len, uz = (bz - az) / len;
                for (float t = 40; t < len; t += 96)
                    foreach (int s in new[] { 1, -1 })
                    {
                        // a pace or two off the road's edge, on its side
                        float off = 30 * s;
                        int x = (int)(ax + ux * t - uz * off), z = (int)(az + uz * t + ux * off);
                        // the way from it back to the road, along the nearer axis
                        float tx = uz * s, tz = -ux * s;
                        int nx = MathF.Abs(tx) >= MathF.Abs(tz) ? Math.Sign(tx) : 0, nz = MathF.Abs(tx) >= MathF.Abs(tz) ? 0 : Math.Sign(tz);
                        if (nx == 0 && nz == 0) continue;
                        roadSpots.Add((x, z, nx, nz));
                    }
            }
        // (in an order the seed gives, so that the road's first stretch is not always the one)
        roadSpots.Sort((a, b) => H(a.x, a.z, 1).CompareTo(H(b.x, b.z, 1)));
        foreach (var (x, z, nx, nz) in roadSpots)
        {
            if (shrines <= 0 && miles <= 0) break;
            if (site.RoadDist(x, z) < 18 || !Apart(x, z, 8)) continue;
            bool shrine = shrines > 0 && (miles <= 0 || H(x, z, 2) < .55);
            int r = shrine ? 3 : 2;
            int g = Open(x - r, z - r, x + r, z + r, 2, false, byRoad: true);
            if (g < 0) continue;
            if (shrine)
            {
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        for (int y = g - 1; y <= g + 9; y++)
                        {
                            // the niche to the road, a candle's place in it
                            if (dx == nx && dz == nz && y >= g + 5 && y <= g + 7) continue;
                            Put(x + dx, y, z + dz, y >= g + 9 ? Mat.Moss : Weathered(x + dx, y, z + dz));
                        }
                // its little roof of planks, and the iron cross over it
                for (int a = -2; a <= 2; a++)
                    for (int b = -2; b <= 2; b++)
                    {
                        Put(x + a, g + 10, z + b, Mat.Plank);
                        if (Math.Abs(nx != 0 ? b : a) <= 1) Put(x + a, g + 11, z + b, Mat.Plank);
                    }
                for (int y = g + 12; y <= g + 15; y++) Put(x, y, z, Mat.Iron);
                Put(x + (nx == 0 ? 1 : 0), g + 14, z + (nz == 0 ? 1 : 0), Mat.Iron);
                Put(x - (nx == 0 ? 1 : 0), g + 14, z - (nz == 0 ? 1 : 0), Mat.Iron);
                // flowers left at its foot
                for (int a = -2; a <= 2; a++) if (H(x + a, z, 3) < .6) Put(x + nx * 2 + (nx == 0 ? a : 0), g, z + nz * 2 + (nz == 0 ? a : 0), H(a, x, 4) < .5 ? Mat.FlowerWhite : Mat.FlowerRed);
                shrines--;
                site.Traces.Add((OldWork.Shrine, x, z));
            }
            else
            {
                for (int dz = 0; dz <= 1; dz++) for (int dx = 0; dx <= 1; dx++) for (int y = g - 1; y <= g + 2; y++) Put(x + dx, y, z + dz, y == g + 2 ? Mat.Moss : Mat.Ruin);
                miles--;
                site.Traces.Add((OldWork.Milestone, x, z));
            }
            laid.Add((x, z, r));
        }

        // ---- the rest on a jittered grid, each kind on its own, the seed choosing where
        IEnumerable<(int x, int z)> Spots(int step, int s)
        {
            var list = new List<(int x, int z, double o)>();
            for (int qz = step / 2; qz < w.SZ; qz += step)
                for (int qx = step / 2; qx < SX; qx += step)
                    list.Add((qx + (int)((H(qx, qz, s) - .5) * step * .8), qz + (int)((H(qz, qx, s + 1) - .5) * step * .8), H(qx, qz, s + 2)));
            list.Sort((a, b) => a.o.CompareTo(b.o));
            foreach (var (x, z, _) in list) yield return (x, z);
        }

        // cairns: heaps of stones on the heights (the highest of the spots first)
        if (cairns > 0)
        {
            var high = new List<(int x, int z)>(Spots(80, 10));
            high.Sort((a, b) => (w.OnLand(b.x, b.z) ? site.HTop[b.z * SX + b.x] : 0).CompareTo(w.OnLand(a.x, a.z) ? site.HTop[a.z * SX + a.x] : 0));
            foreach (var (x, z) in high)
            {
                if (cairns <= 0) break;
                int r = 3 + (int)(H(x, z, 11) * 2);
                if (!Apart(x, z, r)) continue;
                int g = Open(x - r, z - r, x + r, z + r, 4, true);
                if (g < 0) continue;
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        double d = Math.Sqrt(dx * dx + dz * dz);
                        if (d > r + .3) continue;
                        int top = g + (int)((r + .5 - d) * 1.7);
                        for (int y = site.HTop[(z + dz) * SX + x + dx]; y <= top; y++)
                            Put(x + dx, y, z + dz, H(x + dx, y, z + dz) < .3 ? Mat.RockPale : H(x + dx, z + dz, y) < .2 ? Mat.Moss : Mat.Rock);
                    }
                cairns--;
                laid.Add((x, z, r));
                site.Traces.Add((OldWork.Cairn, x, z));
            }
        }

        // a standing stone: two by two at its foot, narrowing to its top, leaning a little; moss low on it
        void Stone(int x, int z, int high, int lean, bool sideways)
        {
            int g = site.HTop[z * SX + x];
            for (int y = 0; y <= high; y++)
            {
                int off = lean * y / Math.Max(1, high);
                int sx = sideways ? off : 0, sz = sideways ? 0 : off;
                bool thin = y > high * 2 / 3;
                for (int dz = 0; dz <= 1; dz++)
                    for (int dx = 0; dx <= 1; dx++)
                    {
                        if (thin && (sideways ? dz : dx) == 1) continue;
                        int px = x + dx + sx, pz = z + dz + sz;
                        for (int yy = y == 0 ? site.HTop[pz * SX + px] - 1 : g + y; yy <= g + y; yy++)
                            Put(px, yy, pz, y < 3 && H(px, yy, pz) < .5 ? Mat.Moss : Mat.Ruin);
                    }
            }
        }
        foreach (var (x, z) in Spots(120, 20))
        {
            if (standing <= 0 && rings <= 0) break;
            bool ring = rings > 0 && (standing <= 0 || H(x, z, 21) < .4);
            int r = ring ? 18 : 2;
            if (!Apart(x, z, r)) continue;
            if (ring)
            {
                int lo = int.MaxValue, hi = int.MinValue;
                bool open = true;
                for (int s = -1; s < 7 && open; s++)
                {
                    double a = s * Math.Tau / 7 + (s < 0 ? 0 : H(x, s, 23) * .3);
                    int sx = s < 0 ? x : x + (int)Math.Round(Math.Cos(a) * 15), sz = s < 0 ? z : z + (int)Math.Round(Math.Sin(a) * 15);
                    int g = Open(sx - 2, sz - 2, sx + 3, sz + 3, 2, false);
                    if (g < 0) open = false;
                    else { lo = Math.Min(lo, g); hi = Math.Max(hi, g); }
                }
                if (!open || hi - lo > 8) continue;
            }
            else if (Open(x - r, z - r, x + r, z + r, 2, false) < 0) continue;
            if (ring)
            {
                int n = 7, fallen = (int)(H(x, z, 22) * n);
                for (int s = 0; s < n; s++)
                {
                    double a = s * Math.Tau / n + H(x, s, 23) * .3;
                    int sx = x + (int)Math.Round(Math.Cos(a) * 15), sz = z + (int)Math.Round(Math.Sin(a) * 15);
                    if (s == fallen)
                    {
                        // one fallen, lying on its side out from the ring
                        int g = site.HTop[sz * SX + sx];
                        for (int k = 0; k < 7; k++)
                            for (int t = 0; t <= 1; t++)
                            {
                                int px = sx + (int)Math.Round(Math.Cos(a) * k) + (t == 1 ? (int)Math.Round(-Math.Sin(a)) : 0), pz = sz + (int)Math.Round(Math.Sin(a) * k) + (t == 1 ? (int)Math.Round(Math.Cos(a)) : 0);
                                Put(px, g, pz, H(px, g, pz) < .4 ? Mat.Moss : Mat.Ruin);
                                Put(px, g + 1, pz, Mat.Ruin);
                            }
                        continue;
                    }
                    Stone(sx, sz, 6 + (int)(H(sx, sz, 24) * 4), (int)(H(sz, sx, 25) * 3) - 1, H(s, x, 26) < .5);
                }
                rings--;
                site.Traces.Add((OldWork.Ring, x, z));
            }
            else
            {
                Stone(x, z, 10 + (int)(H(x, z, 27) * 5), (int)(H(z, x, 28) * 4) - 2, H(x, z, 29) < .5);
                standing--;
                site.Traces.Add((OldWork.Standing, x, z));
            }
            laid.Add((x, z, r));
        }

        // the low walls of old enclosures, and the footings of houses long gone: old stones two thick, broken down
        // unevenly, gaps in them, moss on top, a few stones fallen beside
        void Wall(int x0, int z0, int x1, int z1, int g, int most, int gapSalt)
        {
            float len = MathF.Max(1, MathF.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)));
            for (float t = 0; t <= len; t += .5f)
            {
                int x = x0 + (int)Math.Round((x1 - x0) * t / len), z = z0 + (int)Math.Round((z1 - z0) * t / len);
                double n = H(x >> 2, z >> 2, gapSalt);
                if (n < .18) continue;
                int high = 1 + (int)(n * most);
                for (int dz = 0; dz <= 1; dz++)
                    for (int dx = 0; dx <= 1; dx++)
                        for (int y = site.HTop[(z + dz) * SX + x + dx] - 1; y <= g + high; y++)
                            Put(x + dx, y, z + dz, y == g + high ? (H(x, y, z) < .6 ? Mat.Moss : Mat.Ruin) : Weathered(x + dx, y, z + dz));
                if (H(x, z, gapSalt + 1) < .06) Put(x + 2, site.HTop[z * SX + x + 2], z + 1, Mat.Ruin);
            }
        }
        foreach (var (x, z) in Spots(110, 40))
        {
            if (ruins <= 0 && footings <= 0) break;
            bool footing = footings > 0 && (ruins <= 0 || H(x, z, 41) < .45);
            int hw = footing ? 12 : 14, hd = footing ? 9 : 2;
            bool alongX = H(x, z, 42) < .5;
            int ax = alongX ? hw : hd, az = alongX ? hd : hw;
            if (!Apart(x, z, Math.Max(ax, az))) continue;
            int g = Open(x - ax, z - az, x + ax, z + az, 4, footing ? false : true);
            if (g < 0) continue;
            if (footing)
            {
                Wall(x - ax, z - az, x + ax, z - az, g, 3, 43);
                Wall(x + ax - 1, z - az, x + ax - 1, z + az, g, 3, 44);
                Wall(x + ax, z + az - 1, x - ax, z + az - 1, g, 3, 45);
                Wall(x - ax, z + az, x - ax, z - az, g, 3, 46);
                footings--;
                site.Traces.Add((OldWork.Footing, x, z));
            }
            else
            {
                // a stretch with a corner at one end, as of a pen or a field's wall
                Wall(x - ax, z - az, x + ax, z + az, g, 5, 47);
                if (H(x, z, 48) < .6) Wall(x + ax, z + az, x + ax + (alongX ? 0 : 12), z + az + (alongX ? 12 : 0), g, 4, 49);
                ruins--;
                site.Traces.Add((OldWork.Ruin, x, z));
            }
            laid.Add((x, z, Math.Max(ax, az)));
        }

        // woodcutters' piles of logs gone grey, at the woods' edges (by a tree, not under one)
        foreach (var (x, z) in Spots(96, 60))
        {
            if (logs <= 0) break;
            if (!Apart(x, z, 7)) continue;
            bool alongX = H(x, z, 61) < .5;
            int ax = alongX ? 6 : 2, az = alongX ? 2 : 6;
            int g = Open(x - ax, z - az, x + ax, z + az, 1, false);
            if (g < 0) continue;
            // a tree within six metres
            bool woods = false;
            for (int dz = -48; dz <= 48 && !woods; dz += 8)
                for (int dx = -48; dx <= 48 && !woods; dx += 8)
                {
                    int px = x + dx, pz = z + dz;
                    if (!w.InBounds(px, 1, pz)) continue;
                    int tg = site.HTop[pz * SX + px];
                    woods = Mat.IsTrunk(w.Get(px, tg + 4, pz));
                }
            if (!woods) continue;
            // two logs side by side and one on them, each two across, the cut ends out
            foreach (var (o, y) in new[] { (-1, 0), (1, 0), (0, 2) })
                for (int a = -5; a <= 5; a++)
                    for (int t = 0; t <= 1; t++)
                        for (int yy = 0; yy <= 1; yy++)
                        {
                            int px = alongX ? x + a : x + o + t, pz = alongX ? z + o + t : z + a;
                            Put(px, g + y + yy, pz, Math.Abs(a) == 5 ? Mat.Lumber : Mat.Snag);
                        }
            logs--;
            laid.Add((x, z, 7));
            site.Traces.Add((OldWork.Logs, x, z));
        }

        // the posts of old fences across the meadows, some leaning, some gone, a rail left here and there
        foreach (var (x, z) in Spots(128, 70))
        {
            if (fences <= 0) break;
            bool alongX = H(x, z, 71) < .5;
            int len = 20 + (int)(H(x, z, 72) * 20);
            int ax = alongX ? len : 1, az = alongX ? 1 : len;
            if (!Apart(x, z, len)) continue;
            int g = Open(x - ax, z - az, x + ax, z + az, 5, false);
            if (g < 0) continue;
            int lastPost = int.MinValue;
            for (int a = -len; a <= len; a += 4)
            {
                if (H(a, x, 73) < .25) continue;
                int px = alongX ? x + a : x, pz = alongX ? z : z + a;
                int pg = site.HTop[pz * SX + px];
                int high = 4 + (int)(H(px, pz, 74) * 2);
                for (int y = pg - 1; y <= pg + high; y++) Put(px, y, pz, Mat.Snag);
                // a rail to the post before, now and then
                if (lastPost == a - 4 && H(a, z, 75) < .45)
                    for (int b = 1; b < 4; b++) Put(alongX ? px - b : px, pg + 3, alongX ? pz : pz - b, Mat.Snag);
                lastPost = a;
            }
            fences--;
            laid.Add((x, z, len));
            site.Traces.Add((OldWork.Fence, x, z));
        }
    }
}
