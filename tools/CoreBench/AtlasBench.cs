using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's ground for the heroes walking the map's land (--atlas, AtlasWalk.cs, Campaign.Defend.cs): a new
/// campaign's first hero by the first valley's citadel with his army; every party put on ground it can stand on in its
/// own cell; a walk takes as long as the land's cost says, a stretch of open ground the hours a cell takes over a cell's
/// width, a road its share of them; ways found to every cell of the map never cross ground that cannot be stood on
/// (crags, water off a ford); the cells crossed are the party's cells in turn and the enemy stops it at the first of his;
/// ours come to the valley we play in stand by it unless sent into it; a campaign written halfway along a walk and read
/// back walks on the same; an army of the horde met at once before the walls of a valley we are not in is fought there;
/// a way across the map is found in a few milliseconds.
/// </summary>
static class AtlasBench
{
    static (float X, float Z) Center(Atlas land, MapCell c) => land.Center(c);

    static float Dist((float X, float Z) a, (float X, float Z) b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>A party walked until it stands, the map's seconds it took (in steps of a second).</summary>
    static double WalkOut(Campaign c, MapParty p, double most = 40000)
    {
        double t = 0;
        while ((p.Walking || p.Moving) && t < most && c.Parties.Contains(p)) { c.Advance(1); t += 1; }
        return t;
    }

    public static void Run(Action<string, bool, string> check)
    {
        // ---- a new campaign: the first hero by the citadel, his army with him, every party on ground it can stand on
        var c = Campaign.New(41, 3);
        var hero = c.AddHero();
        var land = Atlas.Build(c);
        c.Attach(land);
        var home = c.Home;
        float sp = AtlasWalk.Spacing(land);
        bool placed = c.Parties.All(p => p.Placed && land.Walkable((int)p.AtX, (int)p.AtZ) && land.CellAt(p.AtX, p.AtZ) == c.CellOf(p));
        float fromHome = Dist((hero.AtX, hero.AtZ), Center(land, home));
        var army = hero.Party.Members.Where(m => m.Kind != Kinds.Captain).Select(m => m.Kind).ToList();
        check("a new campaign opens with its first hero on the map by the first valley's citadel: a commander with a name, his army with him (two swordsmen, two archers), holding the first valley's places",
            hero.Party.Commander?.Hero is { Name.Length: > 0 } && army.Count(k => k == Kinds.Swordsman) == 2 && army.Count(k => k == Kinds.Archer) == 2
            && hero.Party.Members.All(m => m.Billet == Campaign.KeyOf(home)) && placed && fromHome < sp * .45f && !hero.Walking,
            $"hero {hero.Party.Commander?.Hero?.Name}, army {string.Join(",", army.Select(k => k.Key))}, placed {placed}, {fromHome:0} of {sp:0} voxels from the citadel");

        // ---- the time a walk takes is the land's cost: a stretch of open ground, and of road
        double per = AtlasWalk.SecondsPer(land, hero, 1);
        double cellHours = per * sp / MapPath.HourSeconds;
        (float, float)? openA = null, openB = null, roadA = null, roadB = null;
        foreach (var cell in c.Cells)
        {
            var (cx, cz) = Center(land, cell);
            for (int k = 0; k < 24 && (openA == null || roadA == null); k++)
            {
                float a = k * MathF.Tau / 24;
                (float, float) p0 = (cx + MathF.Cos(a) * sp * .1f, cz + MathF.Sin(a) * sp * .1f), p1 = (cx + MathF.Cos(a) * sp * .4f, cz + MathF.Sin(a) * sp * .4f);
                var costs = Enumerable.Range(0, 30).Select(i => { float t = i / 29f; int x = (int)(p0.Item1 + (p1.Item1 - p0.Item1) * t), z = (int)(p0.Item2 + (p1.Item2 - p0.Item2) * t); return land.Walkable(x, z) ? land.Cost(x, z) : -1; }).ToList();
                if (openA == null && cell.Kind == ValleyKind.Ore && costs.All(v => Math.Abs(v - 1) < 1e-4)) (openA, openB) = (p0, p1);
                if (roadA == null && costs.All(v => Math.Abs(v - MapPath.RoadShare) < 1e-4)) (roadA, roadB) = (p0, p1);
            }
        }
        double openTime = openA == null ? -1 : c.WaySeconds(hero, new List<(float X, float Z)> { openA.Value, openB.Value });
        double roadTime = roadA == null ? -1 : c.WaySeconds(hero, new List<(float X, float Z)> { roadA.Value, roadB.Value });
        double openLen = openA == null ? 0 : Dist(openA.Value, openB.Value), roadLen = roadA == null ? 0 : Dist(roadA.Value, roadB.Value);
        // (the land's roads bend: no straight line from a cell's middle need keep to one; then a stretch of a road's own line)
        if (roadA == null)
            foreach (var cell in c.Cells)
            {
                foreach (var line in land.RoadsOf(cell))
                {
                    var pts = new List<(float X, float Z)>();
                    float along = 0;
                    for (int i = 0; i < line.Length; i++)
                    {
                        if (i > 0) along += Dist((line[i - 1].x, line[i - 1].z), (line[i].x, line[i].z));
                        if (along >= sp * .1f && along <= sp * .4f) pts.Add((line[i].x, line[i].z));
                    }
                    if (pts.Count < 3) continue;
                    bool all = true;
                    for (int i = 1; i < pts.Count && all; i++)
                        for (int k = 0; k <= 10; k++)
                        {
                            float t = k / 10f;
                            int x = (int)(pts[i - 1].X + (pts[i].X - pts[i - 1].X) * t), z = (int)(pts[i - 1].Z + (pts[i].Z - pts[i - 1].Z) * t);
                            if (!land.Walkable(x, z) || Math.Abs(land.Cost(x, z) - MapPath.RoadShare) > 1e-4) { all = false; break; }
                        }
                    if (!all) continue;
                    roadTime = c.WaySeconds(hero, pts);
                    roadLen = 0;
                    for (int i = 1; i < pts.Count; i++) roadLen += Dist(pts[i - 1], pts[i]);
                    break;
                }
                if (roadTime > 0) break;
            }
        check("a walk takes the hours the cells say: open ground as wide as a cell is a cell's eight hours; along a road, its share of them",
            Math.Abs(cellHours - 8 * MapPath.Pace(hero)) < .01 && openTime > 0 && Math.Abs(openTime / openLen - per) < per * .01 && roadTime > 0 && Math.Abs(roadTime / roadLen - per * MapPath.RoadShare) < per * .01,
            $"a cell's width {cellHours:0.00} h; open {openTime / Math.Max(1, openLen) / per:0.000} of open, road {roadTime / Math.Max(1, roadLen) / per:0.000} of open ({openLen:0} and {roadLen:0} voxels)");

        // ---- ways to every cell: never over ground that cannot be stood on; roads taken where they go; the time said is the time walked
        var sw = Stopwatch.StartNew();
        int ways = 0, bad = 0, fords = 0, none = 0; double worstMs = 0; float roadShare = 0; int roadWays = 0;
        foreach (var cell in c.Cells)
        {
            if (cell == home) continue;
            var (tx, tz) = Center(land, cell);
            var t0 = sw.Elapsed.TotalMilliseconds;
            var way = AtlasWalk.Find(land, hero.AtX, hero.AtZ, tx, tz);
            worstMs = Math.Max(worstMs, sw.Elapsed.TotalMilliseconds - t0);
            if (way == null) { none++; continue; }
            ways++;
            bool crossedFord = false;
            float cheap = 0, all = 0;
            for (int i = 1; i < way.Count; i++)
            {
                var (ax, az) = way[i - 1]; var (bx, bz) = way[i];
                float len = Dist(way[i - 1], way[i]);
                // every half voxel of it, its ends too, can be stood on
                if (float.IsInfinity(AtlasWalk.LineCost(land, way[i - 1], way[i]))) bad++;
                for (float d = 0; d < len; d += .5f)
                {
                    float x = ax + (bx - ax) * d / Math.Max(len, 1e-3f), z = az + (bz - az) * d / Math.Max(len, 1e-3f);
                    if (!land.Walkable((int)x, (int)z)) continue;
                    float cost = land.Cost((int)x, (int)z);
                    if (cost > 2) crossedFord = true;
                    all += .5f; if (cost < .7f) cheap += .5f;
                }
            }
            if (crossedFord) fords++;
            if (Campaign.Distance(0, 0, cell.Q, cell.R) == 1 && MapPath.Side(home, cell) is int s and >= 0 && home.Edges[s] is { Road: >= 0 })
            {
                roadShare += cheap / Math.Max(1, all); roadWays++;
            }
        }
        check("ways from the first valley to every cell of the map are found and never cross ground that cannot be stood on (crags, water off a ford); to a neighbour the road runs to, mostly by the road",
            none == 0 && bad == 0 && ways == c.Cells.Count - 1 && (roadWays == 0 || roadShare / roadWays > .5f),
            $"{ways} ways, {none} none, {bad} stretches over ground that cannot be stood on, {fords} over a ford; by road {(roadWays == 0 ? 0 : roadShare / roadWays):P0} of the way to {roadWays} neighbours on a road");
        check("a way across the map is found in a few milliseconds", worstMs < 40, $"the worst {worstMs:0.0} ms");

        // ---- walked: as long as said; the cells crossed are its cells in turn; it stands where it was led
        var ore = c.Cells.First(x => x.Kind == ValleyKind.Ore && Campaign.Distance(0, 0, x.Q, x.R) == 1);
        var (ox, oz) = Center(land, ore);
        bool led = c.Lead(hero, ox, oz);
        double said = c.TrailLeft(hero);
        var cellsSaid = new List<MapCell>(hero.Path);
        var seen = new List<MapCell>();
        double walked = 0;
        while (hero.Walking && walked < 20000)
        {
            var before = c.CellOf(hero);
            c.Advance(1); walked += 1;
            if (c.CellOf(hero) != before) seen.Add(c.CellOf(hero));
        }
        var told = c.News.Find(n => n.Kind == NewsKind.Arrived && n.Party == hero);
        check("led over the land to the ore valley, the hero walks there as long as was said, through the cells said, and stands where he was led, told come",
            led && Math.Abs(walked - said) <= 2 && seen.SequenceEqual(cellsSaid) && c.CellOf(hero) == ore && Dist((hero.AtX, hero.AtZ), (ox, oz)) < 2 && !hero.Moving && told != null,
            $"led {led}; said {said:0} s, walked {walked:0} s; cells {string.Join(">", seen.Select(x => x.Kind))} of {string.Join(">", cellsSaid.Select(x => x.Kind))}; {Dist((hero.AtX, hero.AtZ), (ox, oz)):0.0} voxels off");
        c.News.Clear();

        // ---- a campaign written halfway along a walk and read back walks on the same
        var lairCell = c.Lair;
        var nextTo = c.Cells.Where(x => Campaign.Distance(x.Q, x.R, ore.Q, ore.R) == 1 && x.Owner == Owner.None && x != home).First();
        var (nx, nz) = Center(land, nextTo);
        c.Lead(hero, nx, nz);
        for (int i = 0; i < 60; i++) c.Advance(1);
        string dir = Path.Combine(Path.GetTempPath(), $"vt-atlas-{Environment.ProcessId}");
        c.Write(dir);
        var r = Campaign.Read(dir);
        Directory.Delete(dir, true);
        r.Attach(Atlas.Build(r));
        var rh = r.Parties.First(p => p.Owner == Owner.Ours && p.Party.Commander?.Hero != null);
        bool sameNow = Math.Abs(rh.AtX - hero.AtX) < 1e-3 && Math.Abs(rh.AtZ - hero.AtZ) < 1e-3 && rh.Trail?.Count == hero.Trail?.Count;
        double w1 = WalkOut(c, hero), w2 = WalkOut(r, rh);
        check("a campaign written halfway along a walk and read back walks on the same: where he stands, his way, when he gets there",
            sameNow && Math.Abs(w1 - w2) < 1 && Math.Abs(rh.AtX - hero.AtX) < 1e-2 && Math.Abs(rh.AtZ - hero.AtZ) < 1e-2,
            $"same at the reading {sameNow}; walked on {w1:0} and {w2:0} s; at {hero.AtX:0.0},{hero.AtZ:0.0} and {rh.AtX:0.0},{rh.AtZ:0.0}");

        // ---- the enemy stops him at the first cell of his; ours come to the valley we play in stand by it
        var e = Campaign.New(43, 3);
        var eh = e.AddHero();
        e.Attach(Atlas.Build(e));
        var lair = e.Lair;
        var before1 = e.Cells.Where(x => Campaign.Distance(x.Q, x.R, lair.Q, lair.R) == 1).OrderBy(x => Campaign.Distance(0, 0, x.Q, x.R)).First();
        eh.Q = before1.Q; eh.R = before1.R; eh.CameQ = before1.Q; eh.CameR = before1.R;
        e.PlaceIn(eh, before1);
        var (lx, lz) = e.Land.Center(lair);
        bool sent = e.Lead(eh, lx, lz);
        WalkOut(e, eh);
        bool stopped = e.CellOf(eh) == lair && eh.Waits && !eh.Walking && e.News.Exists(n => n.Kind == NewsKind.Arrived && n.Party == eh && n.Text.Contains("врагов"));
        // the valley we play in: ours come to its cell stand by it, unless sent into it
        var v = Campaign.New(47, 2);
        var vh = v.AddHero();
        v.Attach(Atlas.Build(v));
        var vore = v.Cells.First(x => x.Kind == ValleyKind.Ore && Campaign.Distance(0, 0, x.Q, x.R) == 1);
        v.HereQ = vore.Q; v.HereR = vore.R; v.InValley = true;
        var (vx, vz) = v.Land.Center(vore);
        v.Lead(vh, vx, vz);
        WalkOut(v, vh);
        bool stands = v.Parties.Contains(vh) && v.CellOf(vh) == vore && !v.TakeArrival(out _, out _);
        var vs = v.Out(new Party { Side = 0, Members = { new Traveller { Kind = Kinds.Captain, Hp = 100, MaxHp = 100 } } }, null);
        vs.WalkIn = true;
        var (wx, wz) = v.Land.Center(v.Home);
        v.HereQ = v.Home.Q; v.HereR = v.Home.R;
        v.PlaceIn(vs, vore);
        vs.Q = vore.Q; vs.R = vore.R;
        v.Lead(vs, wx, wz);
        WalkOut(v, vs);
        bool walkedIn = !v.Parties.Contains(vs) && v.TakeArrival(out var came, out _) && came == vs;
        check("walking into the horde's lair the hero stops at its edge, the enemy told; ours come to the valley we play in stand on the map by it, and only those sent into it walk in",
            sent && stopped && stands && walkedIn,
            $"sent {sent}, stopped at the lair {stopped} ({e.CellOf(eh).Kind}, waits {eh.Waits}); stands by the valley {stands}; sent in walks in {walkedIn}");

        // ---- out of a valley: by the side gone out over; an army met before the walls of a valley we are not in
        var o = Campaign.New(53, 2);
        o.Attach(Atlas.Build(o));
        var side = 1;
        var outP = o.Out(new Party { Side = side, Members = { new Traveller { Kind = Kinds.Captain, Hp = 100, MaxHp = 100 } } }, null);
        var (hx, hz) = o.Land.Center(o.Home);
        float a1 = MathF.PI / 180 * (-60 + 60 * side);
        float offSide = Dist((outP.AtX, outP.AtZ), (hx + MathF.Cos(a1) * sp * .5f, hz + MathF.Sin(a1) * sp * .5f));
        // an outpost of ours away, an army of the horde on its way to it, met at once
        var post = o.Cells.First(x => x.Owner == Owner.None && Campaign.Distance(0, 0, x.Q, x.R) == 1);
        post.Outpost = true; o.OutpostStands(post);
        foreach (var k in new[] { Kinds.Captain, Kinds.Swordsman, Kinds.Swordsman, Kinds.Archer, Kinds.Archer, Kinds.Spearman }) post.Sum.Keep.Add(new Traveller { Kind = k, Hp = k.Hp, MaxHp = k.Hp });
        post.Sum.Walls = .4f;
        var foe = new MapParty { Q = o.Lair.Q, R = o.Lair.R, CameQ = o.Lair.Q, CameR = o.Lair.R, Owner = Owner.Horde, Aim = PartyAim.Strike, Party = new Party() };
        foreach (var k in new[] { Kinds.Goblin, Kinds.Goblin, Kinds.Goblin }) foe.Party.Members.Add(new Traveller { Kind = k, Hp = k.Hp, MaxHp = k.Hp });
        o.Parties.Add(foe);
        o.Send(foe, post);
        var res = o.DefendNow(foe);
        bool met = res != null && (!o.Parties.Contains(foe) || foe.Aim == PartyAim.Return) && post.Owner == Owner.Ours;
        check("ours gone out of a valley stand on the map by the side they went over; an army of the horde on its way to a valley of ours we are not in, met at once, is fought before its walls (three goblins against six behind a wall lose)",
            offSide < sp * .3f && met, $"{offSide:0} voxels from the side's middle; met {res != null}, the outpost {post.Owner}, the army {(o.Parties.Contains(foe) ? foe.Aim.ToString() : "gone")}");
    }
}
