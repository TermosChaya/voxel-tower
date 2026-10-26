using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A world map of hexes made up to be drawn before the campaign makes its own (HexMapView.cs, --pose=world): zones of
/// one ground each, as «Heroes» lays its random maps, walled from one another by ridges of mountains with a pass or two
/// between each two that meet; woods, hills and lakes within them by noise; rivers running down from the ridges to the
/// lakes; roads from our citadel to every zone through the passes; and in each zone the things of the map — mines of
/// every resource, piles, chests, campfires, windmills, water wheels, shrines, dwellings, treasures and the monsters
/// that keep them — the horde's towns and lairs in the zones farthest from ours, the land about them burnt. The same
/// seed makes the same map. What is seen starts as the land about our citadel.
/// </summary>
public sealed class DemoHexes : IHexSource
{
    public int Width { get; }
    public int Height { get; }
    public event Action<int, int> Changed;
    /// <summary>The whole map seen (pictures).</summary>
    public bool RevealAll;
    public (int Col, int Row) Home { get; private set; }
    /// <summary>The horde's towns.</summary>
    public readonly List<(int Col, int Row)> Towns = new();
    /// <summary>The zone each hex lies in, and the middle of each zone.</summary>
    public readonly List<(int Col, int Row)> ZoneMiddles = new();

    readonly HexLook[] _look;
    readonly bool[] _seen;
    readonly uint _seed;

    public HexLook Look(int col, int row) => _look[row * Width + col];
    public bool Seen(int col, int row) => RevealAll || _seen[row * Width + col];

    /// <summary>A hex's look changed (taken, flagged, picked up), told to whoever draws the map.</summary>
    public void Set(int col, int row, HexLook look)
    {
        _look[row * Width + col] = look;
        Changed?.Invoke(col, row);
    }

    /// <summary>The land within so many hexes of one seen, each hex newly seen told.</summary>
    public void Reveal(int col, int row, int radius)
    {
        for (int r = Math.Max(0, row - radius); r <= Math.Min(Height - 1, row + radius); r++)
            for (int c = Math.Max(0, col - radius - 1); c <= Math.Min(Width - 1, col + radius + 1); c++)
            {
                if (Distance(col, row, c, r) > radius || _seen[r * Width + c]) continue;
                _seen[r * Width + c] = true;
                Changed?.Invoke(c, r);
            }
    }

    /// <summary>Hexes between two, as many steps as one walks.</summary>
    public static int Distance(int c0, int r0, int c1, int r1)
    {
        int q0 = c0 - (r0 - (r0 & 1)) / 2, q1 = c1 - (r1 - (r1 & 1)) / 2;
        int dq = q1 - q0, dr = r1 - r0;
        return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
    }

    bool In(int c, int r) => (uint)c < (uint)Width && (uint)r < (uint)Height;
    int I(int c, int r) => r * Width + c;

    public DemoHexes(int cols, int rows, uint seed)
    {
        Width = cols; Height = rows; _seed = seed;
        int n = cols * rows;
        _look = new HexLook[n];
        _seen = new bool[n];
        var rnd = new Mulberry32(seed * 2654435761u + 7);
        var forest = new ValueNoise(seed + 11); var hills = new ValueNoise(seed + 23); var lakes = new ValueNoise(seed + 37);
        var heights = new ValueNoise(seed + 41); var wobble = new ValueNoise(seed + 53);

        // ---- zones: their middles spread over the map, each hex the nearest's (a little noise in where they meet)
        int zones = Math.Clamp(n / 700, 6, 40);
        var mid = new List<(int c, int r)>();
        int spacing = (int)(Math.Sqrt(n / (double)zones) * .7);
        for (int tries = 0; mid.Count < zones && tries < 20000; tries++)
        {
            int c = 2 + (int)(rnd.Next() * (cols - 4)), r = 2 + (int)(rnd.Next() * (rows - 4));
            bool far = true;
            foreach (var (mc, mr) in mid) if (Distance(c, r, mc, mr) < spacing) { far = false; break; }
            if (far) mid.Add((c, r));
            if (tries % 4000 == 3999) spacing = Math.Max(3, spacing * 4 / 5);
        }
        zones = mid.Count;
        ZoneMiddles.AddRange(mid);
        var zone = new short[n];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int best = 0; double bd = double.MaxValue;
                for (int z = 0; z < zones; z++)
                {
                    double d = Distance(c, r, mid[z].c, mid[z].r) + wobble.Sample(c * .35 + z * 17, r * .35) * 2.2;
                    if (d < bd) { bd = d; best = z; }
                }
                zone[I(c, r)] = (short)best;
            }
        // ours the zone nearest the south-west corner; the horde's the farthest from it
        int home = 0; double hd = double.MaxValue;
        for (int z = 0; z < zones; z++) { double d = Distance(mid[z].c, mid[z].r, cols / 5, rows * 4 / 5); if (d < hd) { hd = d; home = z; } }
        int hordeZones = cols >= 144 ? 4 : cols >= 108 ? 3 : cols >= 72 ? 2 : 1;
        var byFar = new List<int>();
        for (int z = 0; z < zones; z++) if (z != home) byFar.Add(z);
        byFar.Sort((a, b) => Distance(mid[b].c, mid[b].r, mid[home].c, mid[home].r).CompareTo(Distance(mid[a].c, mid[a].r, mid[home].c, mid[home].r)));
        var horde = new HashSet<int>(byFar.GetRange(0, Math.Min(hordeZones, byFar.Count)));
        var grounds = new[] { HexGround.Grass, HexGround.Grass, HexGround.Dirt, HexGround.Grass, HexGround.Sand, HexGround.Snow, HexGround.Swamp, HexGround.Rough, HexGround.Dirt };
        var zoneGround = new HexGround[zones];
        for (int z = 0; z < zones; z++)
            zoneGround[z] = z == home ? HexGround.Grass : horde.Contains(z) ? (rnd.Next() < .5 ? HexGround.Rough : HexGround.Dirt) : grounds[(int)(rnd.Next() * grounds.Length)];

        // ---- the ground, the relief, the height of every hex
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int i = I(c, r), z = zone[i];
                var g = zoneGround[z];
                double f = forest.Sample(c * .23, r * .23), h = hills.Sample(c * .19 + 40, r * .19), w = lakes.Sample(c * .16 + 90, r * .16 + 30);
                var look = new HexLook { Ground = g, Zone = (short)z, Height = (byte)Math.Clamp((int)(heights.Sample(c * .045, r * .045) * 5) - 1, 0, 3) };
                double woods = g switch { HexGround.Grass => .6, HexGround.Swamp => .55, HexGround.Snow => .58, HexGround.Dirt => .64, HexGround.Sand => .8, HexGround.Rough => .74, _ => .9 };
                double hilly = g switch { HexGround.Rough or HexGround.Snow => .66, HexGround.Dirt or HexGround.Sand => .72, _ => .78 };
                if (f > woods) look.Relief = HexRelief.Forest;
                else if (h > hilly) look.Relief = HexRelief.Hills;
                if (h > .86 && Distance(c, r, mid[z].c, mid[z].r) > 3) look.Relief = HexRelief.Mountain;
                if (w > .8 && g != HexGround.Sand && Distance(c, r, mid[z].c, mid[z].r) > 3) { look.Ground = w > .87 ? HexGround.DeepWater : HexGround.Water; look.Relief = HexRelief.Flat; look.Height = 0; }
                _look[i] = look;
            }
        // ---- the ridges between zones: a hex beside one of a zone counted before its own is a mountain
        var border = new bool[n];
        var taken = new bool[n];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int i = I(c, r);
                for (int s = 0; s < 6; s++)
                {
                    var (nc, nr) = HexMapLayout.Neighbour(c, r, s);
                    if (In(nc, nr) && zone[I(nc, nr)] < zone[i]) { border[i] = true; break; }
                }
                // and a frame of mountains round the edge of the map
                if (c == 0 || r == 0 || c == cols - 1 || r == rows - 1) border[i] = true;
                if (border[i]) { _look[i].Relief = HexRelief.Mountain; if (_look[i].Ground is HexGround.Water or HexGround.DeepWater) _look[i].Ground = HexGround.Rough; }
            }
        // ---- the passes: between each two zones that meet, one hex of their ridge (two for the larger), as near the
        // line between their middles as may be, left open
        var pairs = new Dictionary<(int, int), List<int>>();
        for (int r = 1; r < rows - 1; r++)
            for (int c = 1; c < cols - 1; c++)
            {
                int i = I(c, r);
                if (!border[i]) continue;
                for (int s = 0; s < 6; s++)
                {
                    var (nc, nr) = HexMapLayout.Neighbour(c, r, s);
                    if (!In(nc, nr)) continue;
                    int a = zone[i], b = zone[I(nc, nr)];
                    if (a == b || border[I(nc, nr)]) continue;
                    // a hex of the ridge with open land of another zone across it, and of its own behind it
                    bool own = false;
                    for (int t = 0; t < 6; t++)
                    {
                        var (oc, or) = HexMapLayout.Neighbour(c, r, t);
                        if (In(oc, or) && zone[I(oc, or)] == a && !border[I(oc, or)]) { own = true; break; }
                    }
                    if (!own) continue;
                    var key = (Math.Min(a, b), Math.Max(a, b));
                    if (!pairs.TryGetValue(key, out var l)) pairs[key] = l = new List<int>();
                    if (!l.Contains(i)) l.Add(i);
                }
            }
        var passes = new List<int>();
        foreach (var ((a, b), l) in pairs)
        {
            int ma = I(mid[a].c, mid[a].r), mb = I(mid[b].c, mid[b].r);
            l.Sort((x, y) => (Distance(x % cols, x / cols, mid[a].c, mid[a].r) + Distance(x % cols, x / cols, mid[b].c, mid[b].r))
                .CompareTo(Distance(y % cols, y / cols, mid[a].c, mid[a].r) + Distance(y % cols, y / cols, mid[b].c, mid[b].r)));
            if (l.Count < 3) continue;
            int open = l.Count > 12 ? 2 : 1;
            for (int k = 0; k < open; k++)
            {
                int p = l[k * l.Count / 2];
                _look[p].Relief = HexRelief.Flat;
                _look[p].Ground = zoneGround[zone[p]] is HexGround.Water or HexGround.DeepWater ? HexGround.Dirt : zoneGround[zone[p]];
                passes.Add(p);
            }
        }
        // the middles of zones open land, whatever the noise put there
        for (int z = 0; z < zones; z++)
            foreach (var (c, r) in Ring(mid[z].c, mid[z].r, 2))
            {
                int i = I(c, r);
                if (border[i]) continue;
                if (_look[i].Ground is HexGround.Water or HexGround.DeepWater) _look[i].Ground = zoneGround[z];
                if (_look[i].Relief == HexRelief.Mountain) _look[i].Relief = HexRelief.Flat;
            }

        // ---- the horde's land burnt about its towns, our citadel and the horde's towns at the zones' middles
        Home = mid[home];
        foreach (int z in horde)
        {
            var (tc, tr) = mid[z];
            Towns.Add((tc, tr));
            foreach (var (c, r) in Ring(tc, tr, 5))
            {
                int i = I(c, r);
                if (zone[i] != z || _look[i].Ground is HexGround.Water or HexGround.DeepWater) continue;
                int d = Distance(c, r, tc, tr);
                _look[i].Ground = d <= 1 ? HexGround.Lava : HexGround.Scorched;
                if (_look[i].Relief == HexRelief.Forest && d <= 3) _look[i].Relief = HexRelief.Flat;
            }
        }

        // ---- roads from our citadel to every zone's middle, over open land and through the passes
        var passSet = new HashSet<int>(passes);
        for (int z = 0; z < zones; z++)
            if (z != home) Road(mid[home], mid[z]);

        // ---- rivers: from beside a ridge, downhill as the land goes, into a lake or off the field
        int rivers = Math.Max(3, n / 1500);
        for (int k = 0, tries = 0; k < rivers && tries < rivers * 30; tries++)
        {
            int c = 1 + (int)(rnd.Next() * (cols - 2)), r = 1 + (int)(rnd.Next() * (rows - 2)), i = I(c, r);
            if (border[i] || _look[i].Relief == HexRelief.Mountain || !NextTo(c, r, x => x.Relief == HexRelief.Mountain)) continue;
            if (River(c, r, rnd)) k++;
        }

        // ---- the things of the map
        foreach (var (c, r) in Towns) taken[I(c, r)] = true;
        taken[I(Home.Col, Home.Row)] = true;
        Put(Home.Col, Home.Row, HexIcon.Citadel, 1);
        foreach (var (c, r) in Towns) Put(c, r, HexIcon.HordeTown, 2);
        var mines = new[] { HexIcon.MineLogs, HexIcon.MineStone, HexIcon.MineMetal, HexIcon.MineGold, HexIcon.MineMercury, HexIcon.MineSulfur, HexIcon.MineCrystal, HexIcon.MineGems };
        var sights = new[] { HexIcon.Windmill, HexIcon.Shrine, HexIcon.Learning, HexIcon.Stables, HexIcon.Observatory, HexIcon.Dwelling };
        var troves = new[] { HexIcon.DeadMine, HexIcon.Ruins, HexIcon.Den, HexIcon.Trial };
        for (int z = 0; z < zones; z++)
        {
            bool ours = z == home, theirs = horde.Contains(z);
            int size = 0;
            for (int i = 0; i < n; i++) if (zone[i] == z && !border[i]) size++;
            int scale = Math.Max(1, size / 90);
            if (ours)
            {
                Place(z, HexIcon.MineLogs, 1, 2, 4); Place(z, HexIcon.MineStone, 1, 2, 5);
                Place(z, HexIcon.Windmill, 0, 2, 5); Place(z, HexIcon.Campfire, 0, 2, 4);
                for (int k = 0; k < 2; k++) Place(z, HexIcon.Pile, 0, 2, 6);
                Place(z, HexIcon.Chest, 0, 3, 7); Place(z, HexIcon.Signpost, 0, 1, 3);
                continue;
            }
            if (theirs)
            {
                Place(z, HexIcon.Lair, 2, 2, 4);
                Place(z, mines[(int)(rnd.Next() * 4)], 2, 3, 7);
                Place(z, HexIcon.Monster, 2, 2, 6);
                continue;
            }
            for (int k = 0; k < 1 + scale; k++)
            {
                var m = mines[(int)(rnd.Next() * mines.Length)];
                int at = Place(z, m, 0, 2, 9);
                if (at >= 0 && rnd.Next() < .7) Guard(at);
            }
            for (int k = 0; k < 2 + scale; k++) Place(z, HexIcon.Pile, 0, 1, 10);
            for (int k = 0; k < 1 + scale / 2; k++) Place(z, HexIcon.Chest, 0, 2, 10);
            Place(z, HexIcon.Campfire, 0, 1, 10);
            Place(z, sights[(int)(rnd.Next() * sights.Length)], 0, 1, 9);
            if (rnd.Next() < .6) Place(z, HexIcon.WaterWheel, 0, 1, 10, riverOnly: true);
            if (rnd.Next() < .7) { int at = Place(z, troves[(int)(rnd.Next() * troves.Length)], 0, 3, 10); if (at >= 0) Guard(at); }
            for (int k = 0; k < scale; k++) Place(z, HexIcon.Monster, 0, 2, 10);
        }
        // an outpost of ours in the zone nearest ours, a place for one in the next; then every kind of thing the map has
        // put once at least, round the zones that are no one's
        var others = new List<int>();
        for (int z = 0; z < zones; z++) if (z != home && !horde.Contains(z)) others.Add(z);
        others.Sort((a, b) => Distance(mid[a].c, mid[a].r, mid[home].c, mid[home].r).CompareTo(Distance(mid[b].c, mid[b].r, mid[home].c, mid[home].r)));
        if (others.Count > 0) Place(others[0], HexIcon.Outpost, 1, 1, 3);
        if (others.Count > 1) Place(others[1], HexIcon.OutpostPlace, 0, 1, 3);
        var have = new HashSet<HexIcon>();
        foreach (var l in _look) have.Add(l.Icon);
        int round = 0;
        foreach (var k in Enum.GetValues<HexIcon>())
        {
            if (k is HexIcon.None or HexIcon.Pass || have.Contains(k) || others.Count == 0) continue;
            for (int t = 0; t < others.Count && !have.Contains(k); t++)
                if (Place(others[(round + t) % others.Count], k, 0, 1, 10, riverOnly: k == HexIcon.WaterWheel && t < others.Count / 2) >= 0) have.Add(k);
            round++;
        }
        // the passes kept: a gate on half of them, a monster on the rest
        foreach (int p in passes)
        {
            if (_look[p].Icon != HexIcon.None) continue;
            if (rnd.Next() < .5) Put(p % cols, p / cols, HexIcon.Pass, 0);
            else Put(p % cols, p / cols, HexIcon.Monster, 0);
        }

        // what is seen at the start: the land about our citadel
        Reveal(Home.Col, Home.Row, 6);

        // ------------------------------------------------------------ helpers of the making

        void Put(int c, int r, HexIcon icon, byte owner)
        {
            int i = I(c, r);
            _look[i].Icon = icon;
            _look[i].Owner = owner;
            if (_look[i].Relief is HexRelief.Forest or HexRelief.Mountain && icon is not (HexIcon.MineStone or HexIcon.MineMetal or HexIcon.MineGold or HexIcon.MineCrystal or HexIcon.DeadMine or HexIcon.Den))
                _look[i].Relief = HexRelief.Flat;
            if (_look[i].Relief == HexRelief.Mountain) _look[i].Relief = HexRelief.Hills;
            taken[i] = true;
        }

        // a thing put in a zone, so many hexes from its middle, on open land, not beside another thing
        int Place(int z, HexIcon icon, byte owner, int near, int far, bool riverOnly = false)
        {
            var (zc, zr) = mid[z];
            for (int t = 0; t < 120; t++)
            {
                int c = zc + (int)((rnd.Next() * 2 - 1) * (far + 1)), r = zr + (int)((rnd.Next() * 2 - 1) * (far + 1));
                if (!In(c, r)) continue;
                int i = I(c, r), d = Distance(c, r, zc, zr);
                if (d < near || d > far || zone[i] != z || border[i] || taken[i] || passSet.Contains(i)) continue;
                if (_look[i].Ground is HexGround.Water or HexGround.DeepWater) continue;
                if (riverOnly && _look[i].Rivers == 0) continue;
                if (NextTo(c, r, x => x.Icon != HexIcon.None && x.Icon != HexIcon.Monster)) continue;
                Put(c, r, icon, owner);
                return i;
            }
            return -1;
        }

        // a monster on open land beside a thing, toward the zone's middle
        void Guard(int at)
        {
            int c = at % cols, r = at / cols, z = zone[at];
            int best = -1, bd = int.MaxValue;
            for (int s = 0; s < 6; s++)
            {
                var (nc, nr) = HexMapLayout.Neighbour(c, r, s);
                if (!In(nc, nr)) continue;
                int i = I(nc, nr);
                if (taken[i] || border[i] || zone[i] != z || _look[i].Ground is HexGround.Water or HexGround.DeepWater) continue;
                int d = Distance(nc, nr, mid[z].c, mid[z].r);
                if (d < bd) { bd = d; best = i; }
            }
            if (best >= 0) Put(best % cols, best / cols, HexIcon.Monster, 0);
        }

        bool NextTo(int c, int r, Func<HexLook, bool> what)
        {
            for (int s = 0; s < 6; s++)
            {
                var (nc, nr) = HexMapLayout.Neighbour(c, r, s);
                if (In(nc, nr) && what(_look[I(nc, nr)])) return true;
            }
            return false;
        }

        // the way by land between two hexes, cheapest over open ground and along roads already laid, its sides marked
        void Road((int c, int r) from, (int c, int r) to)
        {
            var cost = new float[n];
            Array.Fill(cost, float.MaxValue);
            var came = new int[n];
            var open = new PriorityQueue<int, float>();
            int s0 = I(from.c, from.r), goal = I(to.c, to.r);
            cost[s0] = 0; open.Enqueue(s0, 0);
            while (open.TryDequeue(out int i, out float pri))
            {
                if (i == goal) break;
                if (pri > cost[i] + Distance(i % cols, i / cols, to.c, to.r) + 1e-3f) continue;
                int c = i % cols, r = i / cols;
                for (int s = 0; s < 6; s++)
                {
                    var (nc, nr) = HexMapLayout.Neighbour(c, r, s);
                    if (!In(nc, nr)) continue;
                    int j = I(nc, nr);
                    var l = _look[j];
                    if (l.Relief == HexRelief.Mountain || l.Ground is HexGround.Water or HexGround.DeepWater) continue;
                    float step = l.Roads != 0 ? .4f : l.Relief switch { HexRelief.Forest => 2.2f, HexRelief.Hills => 1.6f, _ => 1 };
                    if (l.Ground == HexGround.Swamp) step += .8f;
                    float nc2 = cost[i] + step;
                    if (nc2 >= cost[j]) continue;
                    cost[j] = nc2; came[j] = i;
                    open.Enqueue(j, nc2 + Distance(nc, nr, to.c, to.r));
                }
            }
            if (cost[goal] == float.MaxValue) return;
            for (int j = goal; j != s0; j = came[j])
            {
                int i = came[j];
                Join(i, j, roads: true);
            }
        }

        // a river from a hex downhill, into a lake or off the field; false if it came to nothing
        bool River(int c, int r, Mulberry32 rr)
        {
            var run = new List<int> { I(c, r) };
            int dir = (int)(rr.Next() * 6);
            for (int step = 0; step < 40; step++)
            {
                int i = run[^1], ic = i % cols, ir = i / cols, best = -1; double bd = double.MaxValue;
                for (int d = -1; d <= 1; d++)
                {
                    int s = (dir + d + 6) % 6;
                    var (nc, nr) = HexMapLayout.Neighbour(ic, ir, s);
                    if (!In(nc, nr)) continue;
                    int j = I(nc, nr);
                    var l = _look[j];
                    if (l.Relief == HexRelief.Mountain || run.Contains(j) || border[j] || taken[j]) continue;
                    double score = l.Height + rr.Next() * 1.6 + (d == 0 ? -.4 : 0) + (l.Rivers != 0 ? -.8 : 0);
                    if (score < bd) { bd = score; best = j; dir = s; }
                }
                if (best < 0) break;
                run.Add(best);
                var bl = _look[best];
                if (bl.Ground is HexGround.Water or HexGround.DeepWater || bl.Rivers != 0) break;
            }
            if (run.Count < 4) return false;
            for (int k = 1; k < run.Count; k++) Join(run[k - 1], run[k], roads: false);
            return true;
        }

        // two neighbouring hexes joined by a road or a river over the side they share
        void Join(int a, int b, bool roads)
        {
            int ac = a % cols, ar = a / cols, bc = b % cols, br = b / cols;
            for (int s = 0; s < 6; s++)
            {
                if (HexMapLayout.Neighbour(ac, ar, s) != (bc, br)) continue;
                int back = (s + 3) % 6;
                if (roads) { _look[a].Roads |= (byte)(1 << s); _look[b].Roads |= (byte)(1 << back); }
                else
                {
                    if (_look[a].Ground is not (HexGround.Water or HexGround.DeepWater)) _look[a].Rivers |= (byte)(1 << s);
                    if (_look[b].Ground is not (HexGround.Water or HexGround.DeepWater)) _look[b].Rivers |= (byte)(1 << back);
                }
                return;
            }
        }
    }

    /// <summary>The hexes within so many of one, on the field.</summary>
    IEnumerable<(int c, int r)> Ring(int col, int row, int radius)
    {
        for (int r = Math.Max(0, row - radius); r <= Math.Min(Height - 1, row + radius); r++)
            for (int c = Math.Max(0, col - radius - 1); c <= Math.Min(Width - 1, col + radius + 1); c++)
                if (Distance(col, row, c, r) <= radius) yield return (c, r);
    }

    /// <summary>A number standing for the whole map's looks, the same for the same seed (the bench's check).</summary>
    public ulong Fingerprint()
    {
        ulong h = 1469598103934665603UL;
        foreach (var l in _look)
            unchecked
            {
                h = (h ^ (ulong)l.Ground) * 1099511628211UL; h = (h ^ (ulong)l.Relief) * 1099511628211UL; h = (h ^ l.Height) * 1099511628211UL;
                h = (h ^ l.Roads) * 1099511628211UL; h = (h ^ l.Rivers) * 1099511628211UL; h = (h ^ (ulong)l.Icon) * 1099511628211UL;
                h = (h ^ l.Owner) * 1099511628211UL; h = (h ^ (ulong)(ushort)l.Zone) * 1099511628211UL;
            }
        return h;
    }
}
