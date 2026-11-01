using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Where a commander's shooters stand (Sim.Captains.cs): up on our works, over the way the horde comes. A post is a place
/// to shoot from that our building gives: a tower's top, the walk along a wall, the top of a gatehouse, a watchtower's
/// deck, the citadel's top if a way leads up. It is a cell two metres and more over the ground, on our masonry or timber,
/// with nothing over the head, that a man can walk up to from what is kept. A post is worth as much of the horde's way in
/// (as the horde finds it: through a gate, a gap or a wall it hacks) and of its breaches as a bow reaches and sees from
/// there. A shooter on a post worth something, or shooting from it, stays there. The rest go up to the best free posts,
/// the nearest shooter to each: a tower's top first, then the walls where the horde comes, then the gate's flanks, a
/// couple of metres apart. One on our works with nowhere better to go stays where it is; only those on the ground with no
/// post to go up to stand behind the line. At the alarm, with no horde in sight, the same is done over the way it will
/// come in by, and the foot stand inside the gate, the gap or the wall on that way.
/// </summary>
public sealed partial class Sim
{
    /// <summary>A place on our works to shoot from: its cell, its floor, how high that is over the ground (voxels), whether it is a tower's top or a watchtower's deck.</summary>
    internal readonly struct Perch
    {
        public readonly int Cell, High;
        public readonly float X, Y, Z;
        public readonly bool Tower;
        public Perch(int cell, float x, float y, float z, int high, bool tower) { Cell = cell; X = x; Y = y; Z = z; High = high; Tower = tower; }
    }

    /// <summary>How far about what a commander keeps it looks for posts (48 m); how high over the ground a post stands at least (2 m).</summary>
    public const float PerchReach = 48 * VoxelWorld.PerMetre;
    const int PerchLeast = 2 * VoxelWorld.PerMetre;

    // each commander's posts, for what state of the valley, when and about where they were looked for
    readonly Dictionary<int, (int version, double at, int x, int z, List<Perch> list)> _perches = new();
    // the cells a man walks to from where each commander stood, and when that was worked out
    readonly Dictionary<int, (double at, HashSet<int> cells)> _walkable = new();
    // what each post of a commander's is worth against the way the horde comes, and for which way
    readonly Dictionary<int, (long key, double at, int[] worth)> _perchWorth = new();
    /// <summary>Shooters a commander sent up onto our works, to a tower's top or a wall's walk (for the bench).</summary>
    public int CaptainTowers, CaptainWalls;

    /// <summary>
    /// The posts about what a commander keeps: looked for again once the valley has changed and a few seconds have gone,
    /// or when it keeps somewhere else. Where a man walks from where it stands is worked out again every quarter of a
    /// minute (a stair built or burnt shows then), or when it has gone off where that reached.
    /// </summary>
    internal List<Perch> Perches(Unit cap, float hx, float hz)
    {
        if (_perches.TryGetValue(cap.Id, out var was) && Math.Abs(was.x - (int)hx) < 16 && Math.Abs(was.z - (int)hz) < 16
            && (was.version == Nav.Version || Time - was.at < 6)) return was.list;
        if (!_walkable.TryGetValue(cap.Id, out var walk) || Time - walk.at > 15 || !walk.cells.Contains(cap.Cell))
        {
            var near = new List<int>();
            int r = (int)(PerchReach / NavGrid.CS);
            if (cap.Cell >= 0) Paths.Reach(cap.Cell, Kinds.Archer.Move, (int)(Math.PI * r * r * 1.3) + 2000, gates: true, near);
            walk = (Time, new HashSet<int>(near));
            _walkable[cap.Id] = walk;
        }
        var list = Survey(hx, hz, PerchReach, walk.cells);
        _perches[cap.Id] = (Nav.Version, Time, (int)hx, (int)hz, list);
        _perchWorth.Remove(cap.Id);
        return list;
    }

    /// <summary>Every commander's posts and what they are worth looked for afresh at its next look (the bench).</summary>
    internal void ForgetPosts() { _perches.Clear(); _walkable.Clear(); _perchWorth.Clear(); }

    /// <summary>
    /// The posts about a point: in every column of cells within the reach, the highest cell a man stands in, if it is two
    /// metres and more over the ground, on our works and with nothing over his head (one under a roof is a house's), and
    /// a man walks to it. A post three quarters of a metre and more over the walls about it, or a deck over the air, is a
    /// tower's.
    /// </summary>
    List<Perch> Survey(float hx, float hz, float reach, HashSet<int> walk)
    {
        var found = new List<Perch>();
        var decks = new List<bool>();
        int ci = (int)hx >> NavGrid.Shift, ck = (int)hz >> NavGrid.Shift, r = (int)(reach / NavGrid.CS);
        for (int k = Math.Max(0, ck - r); k <= Math.Min(Nav.NZ - 1, ck + r); k++)
            for (int i = Math.Max(0, ci - r); i <= Math.Min(Nav.NX - 1, ci + r); i++)
            {
                if ((i - ci) * (i - ci) + (k - ck) * (k - ck) > r * r) continue;
                int c = -1;
                for (int y = Nav.NY - 1; y >= 1 && c < 0; y--) if (Nav.Stand(i, y, k, 2)) c = Nav.Id(i, y, k);
                if (c < 0 || Nav.Deep(c) || !walk.Contains(c)) continue;
                int x0 = i << NavGrid.Shift, z0 = k << NavGrid.Shift, floor = Nav.Floor[c];
                // on our works (the quick look: plain ground is not), and two metres over the ground as it is now
                // (not as the valley was made: a field levelled or dug lies under the ground it was made with)
                if (!OnWorks(x0, floor, z0)) continue;
                int ground = World.GroundTop(x0 + 4, z0 + 4);
                if (floor - ground < PerchLeast || World.SurfAt(x0 + 4, z0 + 4) > floor + 6) continue;
                var (fx, fy, fz) = Nav.Feet(c);
                found.Add(new Perch(c, fx, fy, fz, floor - ground, false));
                decks.Add(World.Get(x0 + 4, floor - 3, z0 + 4) == Mat.Air);
            }
        if (found.Count == 0) return found;
        var heights = new List<int>();
        foreach (var p in found) heights.Add(p.High);
        heights.Sort();
        int walls = heights[heights.Count / 2];
        var list = new List<Perch>(found.Count);
        for (int n = 0; n < found.Count; n++)
        {
            var p = found[n];
            list.Add(new Perch(p.Cell, p.X, p.Y, p.Z, p.High, decks[n] || p.High >= walls + 6));
        }
        return list;
    }

    /// <summary>Whether the floor of a cell is laid on our works (looked at in five places).</summary>
    bool OnWorks(int x0, int floor, int z0)
    {
        foreach (var (dx, dz) in Spots)
            if (OurWorks(World.Get(x0 + dx, floor - 1, z0 + dz))) return true;
        return false;
    }

    static readonly (int dx, int dz)[] Spots = { (4, 4), (1, 1), (6, 1), (1, 6), (6, 6) };

    /// <summary>What a post stands on: our masonry, timber, iron or a deck, or the citadel's stone; not a roof, a ladder, scaffolding or what dresses a wall.</summary>
    static bool OurWorks(byte m) => m is Mat.Keep or Mat.KeepDark
        || Mat.IsPlayer[m] && !Mat.IsDecor[m] && !Mat.IsRoofing(m) && m is not (Mat.Scaffold or Mat.Ladder or Mat.TowerLadder or Mat.SiegeLadder);

    // ------------------------------------------------------------ the way the horde comes

    /// <summary>
    /// The cells along the way the horde will come from a cell to what is kept, as its field leads: through a gate or a
    /// gap, or up to a wall it will hack through or raise a ladder against, and on; stopping at what is kept or after so
    /// many cells.
    /// </summary>
    List<int> WayIn(FlowField f, int from, float cx, float cz, int most = 240)
    {
        var cells = new List<int>();
        int c = from;
        float stop = Site.KeepR + 2 * VoxelWorld.PerMetre;
        for (int step = 0; step < most && c >= 0; step++)
        {
            cells.Add(c);
            var (x, _, z) = Nav.Feet(c);
            if ((x - cx) * (x - cx) + (z - cz) * (z - cz) < stop * stop) break;
            int next = f.Next(Nav, c, out _, out _);
            if (next < 0 || next == c) break;
            c = next;
        }
        return cells;
    }

    /// <summary>Cells every two metres along a straight line on the ground, where the horde has no field to go by.</summary>
    List<int> Straight(float ax, float az, float bx, float bz)
    {
        var cells = new List<int>();
        float dx = bx - ax, dz = bz - az, d = MathF.Sqrt(dx * dx + dz * dz);
        for (float s = 0; s <= d; s += 2 * VoxelWorld.PerMetre)
        {
            int c = NearestStand((int)(ax + dx * s / Math.Max(1, d)), (int)(az + dz * s / Math.Max(1, d)), 2);
            if (c >= 0 && (cells.Count == 0 || cells[^1] != c)) cells.Add(c);
        }
        return cells;
    }

    /// <summary>
    /// The ways the horde about what is kept comes in by: from the knot about the nearest of it, and from up to two more
    /// knots of it twenty-five metres and more from the others, each along its own field.
    /// </summary>
    List<int> HordeWays(Unit lead, float cx, float cz)
    {
        var heads = new List<Unit> { lead };
        float apart = 25 * VoxelWorld.PerMetre, reach = 110 * VoxelWorld.PerMetre;
        foreach (var o in Units)
        {
            if (heads.Count >= 3) break;
            if (o.Dead || o.Inside || o.Team != Team.Horde || o.Bound || o.Envoy || o.Lurking || o.Routed) continue;
            if ((o.X - cx) * (o.X - cx) + (o.Z - cz) * (o.Z - cz) > reach * reach) continue;
            bool alone = true;
            foreach (var h in heads) if ((o.X - h.X) * (o.X - h.X) + (o.Z - h.Z) * (o.Z - h.Z) < apart * apart) { alone = false; break; }
            if (alone) heads.Add(o);
        }
        var way = new List<int>();
        foreach (var h in heads)
            way.AddRange(FieldOf(h.T) is { } f && !float.IsPositiveInfinity(f.Dist[h.Cell]) ? WayIn(f, h.Cell, cx, cz) : Straight(h.X, h.Z, cx, cz));
        return way;
    }

    /// <summary>
    /// Where the horde will come in from, with none of it in sight: where the bands on their way gather (or come in), the
    /// nearest of them; else the spot of the valley's side it comes by nearest what is kept.
    /// </summary>
    int HordeComesFrom(float cx, float cz)
    {
        int best = -1; float bd = float.MaxValue;
        foreach (var b in Bands)
        {
            if (b.State is not (BandState.Coming or BandState.Forming or BandState.March)) continue;
            int c = b.RallyCell >= 0 ? b.RallyCell : b.Entry;
            if (c < 0) continue;
            var (x, _, z) = Nav.Feet(c);
            float d = (x - cx) * (x - cx) + (z - cz) * (z - cz);
            if (d < bd) { bd = d; best = c; }
        }
        if (best >= 0) return best;
        var (ex, ez) = Site.EntryNear(cx, cz);
        return NearestStand(ex, ez, 2);
    }

    // ------------------------------------------------------------ what a post is worth

    /// <summary>
    /// What a commander's posts are worth against a way: for each, how many of the way's cells within a bow's reach of it
    /// a man on it sees (up to six looked at, spread along it). Worked out again when the way has changed much, or
    /// after a few seconds.
    /// </summary>
    int[] Worth(Unit cap, List<Perch> perches, List<int> way, float range)
    {
        // (the way's ends to four metres or so, its length to eight cells: the horde coming on along the same way is the same way)
        long key = way.Count == 0 ? 0 : (long)(way[0] >> 2) * 1000003L + (way[^1] >> 2) * 7919L + way.Count / 8 + (long)range;
        if (_perchWorth.TryGetValue(cap.Id, out var was) && was.key == key && Time - was.at < 6 && was.worth.Length == perches.Count) return was.worth;
        var worth = new int[perches.Count];
        var pts = new List<(float x, float y, float z)>(way.Count);
        foreach (int c in way) pts.Add(Nav.Feet(c));
        float r2 = range * range;
        var inReach = new List<int>();
        for (int n = 0; n < perches.Count; n++)
        {
            var p = perches[n];
            inReach.Clear();
            for (int i = 0; i < pts.Count; i++)
            {
                float dx = pts[i].x - p.X, dz = pts[i].z - p.Z;
                if (dx * dx + dz * dz <= r2) inReach.Add(i);
            }
            if (inReach.Count == 0) continue;
            int step = Math.Max(1, (inReach.Count + 5) / 6), looked = 0, seen = 0;
            for (int j = 0; j < inReach.Count; j += step)
            {
                var (x, y, z) = pts[inReach[j]];
                looked++;
                // from a man's eyes on the post to the chest of one on the way
                if (SeesFrom(p.X, p.Y + 13, p.Z, x, y + 8, z)) seen++;
            }
            worth[n] = (int)MathF.Ceiling(inReach.Count * seen / (float)looked);
        }
        _perchWorth[cap.Id] = (key, Time, worth);
        return worth;
    }

    // ------------------------------------------------------------ the posting

    /// <summary>
    /// The shooters posted on our works over a way the horde comes (see the class). Returns those on the ground with no
    /// post to go up to, for the commander to stand behind its line.
    /// </summary>
    List<Unit> PostShooters(Unit cap, List<Unit> shooters, List<int> way, float fx, float fz, float hx, float hz, string said)
    {
        var perches = Perches(cap, hx, hz);
        float range = 0;
        foreach (var s in shooters) range = MathF.Max(range, ShotRange(s) * FogRange);
        // only the stretch of the way our works about what is kept can reach
        float far = PerchReach + range;
        way = way.FindAll(c => { var (x, _, z) = Nav.Feet(c); return (x - hx) * (x - hx) + (z - hz) * (z - hz) < far * far; });
        if (perches.Count == 0 || way.Count == 0) return shooters;
        var worth = Worth(cap, perches, way, range);
        var at = new Dictionary<int, int>();
        for (int n = 0; n < perches.Count; n++) at[perches[n].Cell] = n;
        int PerchOf(int c) => c >= 0 && at.TryGetValue(c, out int n) ? n : -1;
        // the posts taken: by whoever of ours stands on one or was set to (the player's own, another commander's)
        var taken = new List<int>();
        foreach (var u in Units)
        {
            if (u.Dead || u.Team != Team.Player || shooters.Contains(u)) continue;
            int n = PerchOf(u.Post);
            if (n < 0) n = PerchOf(u.Cell);
            if (n >= 0) taken.Add(perches[n].Cell);
        }
        // one on a post worth something, or shooting from it, stays there; the rest are to be posted
        var free = new List<Unit>();
        var onWorks = new HashSet<Unit>();
        foreach (var s in shooters)
        {
            // its post, unless it is kept from it (no way up there now): then where it stands
            int n = PerchOf(s.Post);
            if (n < 0 || s.Cell != s.Post && s.NoWayUntil > Time) n = PerchOf(s.Cell);
            if (n >= 0 && (worth[n] > 0 || Shooting(s)))
            {
                // held there by its word (up there by its own feet with its post elsewhere: that is its post now)
                if (s.Post != perches[n].Cell || !s.Hold) Command(new[] { s }, perches[n].Cell);
                s.Ward = cap;
                taken.Add(perches[n].Cell);
                continue;
            }
            if (n >= 0) onWorks.Add(s);
            free.Add(s);
        }
        if (free.Count == 0) return free;
        // the free posts worth something, the best first: a tower's top, the walls where the horde comes, the gate's flanks, the rest
        var gates = new List<(float x, float z)>();
        foreach (var g in Gates) if (!g.Broken) { var (gx, _, gz) = g.Middle; gates.Add((gx, gz)); }
        int Rank(int n)
        {
            var p = perches[n];
            if (p.Tower) return 0;
            if ((p.X - fx) * (p.X - fx) + (p.Z - fz) * (p.Z - fz) < 20 * 20 * 64) return 1;
            foreach (var (gx, gz) in gates) if ((p.X - gx) * (p.X - gx) + (p.Z - gz) * (p.Z - gz) < 10 * 10 * 64) return 2;
            return 3;
        }
        var order = new List<int>();
        for (int n = 0; n < perches.Count; n++)
            if (worth[n] > 0 && !(_noWayUp.TryGetValue(perches[n].Cell, out double until) && Time < until)) order.Add(n);
        var rank = new Dictionary<int, int>();
        foreach (int n in order) rank[n] = Rank(n);
        float ToFront(int n) => (perches[n].X - fx) * (perches[n].X - fx) + (perches[n].Z - fz) * (perches[n].Z - fz);
        order.Sort((a, b) => rank[a] != rank[b] ? rank[a].CompareTo(rank[b]) : worth[a] != worth[b] ? worth[b].CompareTo(worth[a]) : ToFront(a).CompareTo(ToFront(b)));
        // each to the post the nearest of them, a couple of metres from any taken
        int towers = 0, walls = 0, failed = 0;
        foreach (int n in order)
        {
            if (free.Count == 0 || failed >= 4) break;
            var p = perches[n];
            bool apart = true;
            foreach (int t in taken) if (CellsApart(t, p.Cell) < 2 && Math.Abs(Nav.Y(t) - Nav.Y(p.Cell)) <= 1) { apart = false; break; }
            if (!apart) continue;
            Unit pick = null; float near = float.MaxValue;
            foreach (var s in free)
            {
                float d = (s.X - p.X) * (s.X - p.X) + (s.Z - p.Z) * (s.Z - p.Z);
                if (d < near) { near = d; pick = s; }
            }
            if (Command(new[] { pick }, p.Cell) == 0) { _noWayUp[p.Cell] = Time + 20; failed++; continue; }
            pick.Ward = cap;
            free.Remove(pick);
            onWorks.Remove(pick);
            taken.Add(p.Cell);
            if (p.Tower) towers++; else walls++;
        }
        if (towers + walls > 0)
        {
            CaptainPosted++; CaptainTowers += towers; CaptainWalls += walls;
            OrderSaid(cap, "posted", said ?? (towers > 0 ? "Командир ставит стрелков на башни против орды." : "Командир ставит стрелков на стены против орды."));
        }
        // one up on our works with nowhere better to go stays where it is: none is brought down to the ground
        foreach (var s in onWorks) s.Ward = cap;
        free.RemoveAll(onWorks.Contains);
        return free;
    }

    // whether one of a commander's line holds its place rather than go after one of the horde, and till when that is so
    readonly Dictionary<int, (Unit foe, double until, bool hold)> _holdLine = new();

    /// <summary>
    /// Whether one on foot in a commander's line keeps its place rather than go after one of the horde: one further out
    /// from what the commander keeps than the place it stood it, with a wall of ours between them, and not up on our works
    /// nor climbing them. It holds the wall, the breach or the gate from inside and does not go out into the open after
    /// it. Looked at again every half second.
    /// </summary>
    bool HoldsLine(Unit u, Unit foe)
    {
        if (u.T.Ranged || u.Post < 0 || u.Ward is not { Dead: false } w || w.T != Kinds.Captain) return false;
        if (_holdLine.TryGetValue(u.Id, out var was) && was.foe == foe && Time < was.until) return was.hold;
        bool hold = false;
        var (px, _, pz) = Nav.Feet(u.Post);
        var (hx, hz) = CaptainHome(w);
        if ((foe.X - hx) * (foe.X - hx) + (foe.Z - hz) * (foe.Z - hz) > (px - hx) * (px - hx) + (pz - hz) * (pz - hz)
            && !Climbing(foe) && !(OurWallAt((int)foe.X, (int)foe.Z) && foe.Y > World.GroundTop((int)foe.X, (int)foe.Z) + 12))
        {
            // (from a step off the foe toward the post: the wall it is hacking at is between them)
            var (sx, sz) = Toward(foe.X, foe.Z, px, pz, 4);
            hold = WallBetween(sx, sz, px, pz) != null;
        }
        _holdLine[u.Id] = (foe, Time + .5, hold);
        return hold;
    }

    /// <summary>Whether a shooter has one of the horde in its reach and sight just now.</summary>
    bool Shooting(Unit s) => s.Target is { Dead: false, Team: Team.Horde } t && Dist(s, t) <= RangeAt(s, t.X, t.Z) && Sight(s, t);

    /// <summary>
    /// The alarm, with no horde in sight: the shooters up on our works over the way the horde will come in by, the foot
    /// inside the gate or the gap on that way, or inside the wall it will come at; with no wall on it, at the citadel's
    /// gate. Those under its word on the ground with no post stand behind the foot.
    /// </summary>
    void Watch(Unit cap, List<Unit> men, float cx, float cz)
    {
        int from = HordeComesFrom(cx, cz);
        var f = FieldOf(Kinds.Goblin);
        bool led = f != null && from >= 0 && !float.IsPositiveInfinity(f.Dist[from]);
        var (ex, _, ez) = from >= 0 ? Nav.Feet(from) : ((float)Site.EntryX, 0f, (float)cz);
        var way = led ? WayIn(f, from, cx, cz, 400) : Straight(ex, ez, cx, cz);
        // only its last stretch, where our works about what is kept can reach it
        float keep = PerchReach + 30 * VoxelWorld.PerMetre;
        int first = way.FindIndex(c => { var (x, _, z) = Nav.Feet(c); return (x - cx) * (x - cx) + (z - cz) * (z - cz) < keep * keep; });
        if (first > 0) way.RemoveRange(0, first);
        var melee = Melee(men);
        // the foot: across the way in through a gate or a gap, a step inside it
        var gap = led && way.Count > 0 ? Opening(f, way[0], cx, cz, Site.KeepR + VoxelWorld.PerMetre, 240) : null;
        // else the wall where it hacks through or raises its ladder on that stretch (or the first of ours it meets on it)
        (int x, int z)? wall = null;
        for (int i = 0; gap == null && led && wall == null && i + 1 < way.Count; i++)
            if (f.Next(Nav, way[i], out float carve, out var works) == way[i + 1] && (carve > 0 || works != Works.None))
            {
                var (bx, _, bz) = Nav.Feet(way[i + 1]);
                wall = ((int)bx, (int)bz);
            }
        if (gap == null && wall == null)
            foreach (int c in way)
            {
                var (x, _, z) = Nav.Feet(c);
                if (OurWallAt((int)x, (int)z)) { wall = ((int)x, (int)z); break; }
            }
        float lx, lz;
        if (gap is { } g)
        {
            (lx, lz) = (g.x + g.ix * 1.5f * VoxelWorld.PerMetre, g.z + g.iz * 1.5f * VoxelWorld.PerMetre);
            PlaceAcross(cap, melee, lx, lz, g.ix, g.iz, g.width, "Тревога. Командир ставит бойцов в проходе, через который пойдёт орда.");
        }
        else if (wall is { } w)
        {
            (lx, lz) = Toward(w.x, w.z, cx, cz, 3.5f * VoxelWorld.PerMetre);
            Place(cap, melee, NearestStand((int)lx, (int)lz, 2), "alarm", "Тревога. Командир ставит бойцов у стены, к которой придёт орда.");
        }
        else
        {
            var (gx, _, gz) = Nav.Feet(Gate);
            (lx, lz) = (gx, gz);
            Place(cap, melee, StandToward(gx, gz, cx, cz, 4 * VoxelWorld.PerMetre), "alarm", "Командир собирает бойцов у ворот цитадели.");
        }
        var shooters = Shooters(men);
        if (shooters.Count == 0) return;
        var (fx, fz) = gap is { } gg ? (gg.x, gg.z) : wall is { } ww ? (ww.x, ww.z) : (lx, lz);
        var below = PostShooters(cap, shooters, way, fx, fz, cx, cz, "Тревога. Командир ставит стрелков на стены и башни со стороны орды.");
        // (with no wall about what is kept the shooters keep to their posts, as they always did at the alarm)
        if (below.Count > 0 && (gap != null || wall != null))
        {
            var (rx, rz) = Toward(lx, lz, cx, cz, 4 * VoxelWorld.PerMetre);
            Place(cap, below, NearestStand((int)rx, (int)rz, 2), "shooters", null);
        }
    }

    /// <summary>Points about a spot a commander holds, for its shooters to cover: the spot, and eight round it at six metres and at twelve.</summary>
    List<int> Round(float x, float z)
    {
        var cells = new List<int>();
        int at = NearestStand((int)x, (int)z, 2);
        if (at >= 0) cells.Add(at);
        foreach (float r in new[] { 6f, 12f })
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.Tau / 8;
                int c = NearestStand((int)(x + Math.Cos(a) * r * VoxelWorld.PerMetre), (int)(z + Math.Sin(a) * r * VoxelWorld.PerMetre), 2);
                if (c >= 0) cells.Add(c);
            }
        return cells;
    }

    /// <summary>A clear line from one point to another (voxels); leaves don't stop arrows.</summary>
    public bool SeesFrom(float ax, float ay, float az, float bx, float by, float bz)
    {
        float dx = bx - ax, dy = by - ay, dz = bz - az, len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1) return true;
        if (!Raycast.Cast(World, ax, ay, az, dx / len, dy / len, dz / len, out var h, skipVeg: true)) return true;
        float hx = h.X + .5f - ax, hy = h.Y + .5f - ay, hz = h.Z + .5f - az;
        return MathF.Sqrt(hx * hx + hy * hy + hz * hz) >= len - 3;
    }
}
