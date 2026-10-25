using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>
/// Commanders of ours: a captain in a cloak and a plumed helm, with a sword and a horn, who sees to the soldiers about it
/// by itself, as far as <see cref="CaptainReach"/> — the player may turn that off and on, and give it a spot to hold.
/// Every few seconds it looks over the field. With the horde coming it forms those on foot up inside the wall the horde is
/// coming at (or before the citadel, where there is no wall), sends those who shoot up onto the wall-walks facing it,
/// sends the badly hurt back to mend, sounds the horn when its people waver and closes the shield wall when the horde is
/// on them; the band beaten off, it calls the workers to mend what was broken about it. At the alarm with no horde in
/// sight it forms them at the citadel's gate. Those the player sent somewhere itself are left be. Its four calls
/// (<see cref="Call"/>) the player can make too, gone into it: «За мной!», «Держать строй», «Залп!» and «Рог».
/// </summary>
public sealed partial class Sim
{
    /// <summary>How far a commander's word reaches, voxels (40 m).</summary>
    public const float CaptainReach = 40 * VoxelWorld.PerMetre;
    /// <summary>How many commanders the citadel takes.</summary>
    public const int MaxCaptains = 2;
    /// <summary>The four calls, their names and how long each takes to be ready again, seconds.</summary>
    public static readonly string[] CallNames = { "За мной!", "Держать строй", "Залп!", "Рог" };
    public static readonly float[] CallRest = { 40, 45, 30, 60 };
    public static readonly string[] CallAbout =
    {
        "Бойцы в 20 м идут за командиром 25 с и бьют на 20 % сильнее.",
        "Бойцы в 15 м встают, где стоят, на 20 с, получают на 35 % меньше урона и чаще отбивают удары.",
        "Стрелки в 50 м разом стреляют туда, куда смотрит командир.",
        "Наши в 40 м забывают боль и страх, раненые, кому это по силам, возвращаются в строй, бойцы 20 с бьют сильнее.",
    };
    /// <summary>A commander made a call: who, and which (0–3).</summary>
    public event Action<Unit, int> Called;
    /// <summary>Orders a commander gave the field over (for the bench): formed up, sent onto the walls, sent back to mend, mendings called.</summary>
    public int CaptainFormed, CaptainPosted, CaptainSentBack, CaptainMended;

    // one line in the chronicle for each kind of order, not oftener than a minute
    readonly Dictionary<string, double> _orderSaid = new();
    // the wall a commander last stood its people at, for the mending after
    readonly Dictionary<int, (int x, int z)> _lastFront = new();

    void OrderSaid(Unit cap, string key, string text)
    {
        if (_orderSaid.TryGetValue(key, out double at) && Time - at < 60) return;
        _orderSaid[key] = Time;
        Say(text, 1, cap);
    }

    /// <summary>Those under a commander's word now: soldiers of ours in its reach, on their feet, not sent elsewhere by the player.</summary>
    List<Unit> Under(Unit cap, float reach)
    {
        var list = new List<Unit>();
        foreach (var u in Units)
        {
            if (u == cap || u.Dead || u.Inside || u.Team != Team.Player || u == Possessed || u.Captor != null || u.Airborne != null) continue;
            if (u.T == Kinds.Captain || u.T == Kinds.Crewman || Kinds.IsEngine(u.T) || u.T.Prays || u.T == Kinds.Risen || u.Mending) continue;
            if (!(u.T.Work < 1 || u.Militia) || !Armed(u)) continue;
            // sent by the player: its own, till it is let go; set by another commander still about: that one's
            if (u.Hold && u.Ward == null) continue;
            if (u.Ward != null && u.Ward != cap && !u.Ward.Dead && u.Ward.T == Kinds.Captain) continue;
            if (Dist(u, cap) > reach) continue;
            list.Add(u);
        }
        return list;
    }

    /// <summary>A commander, each tick: now and then it looks over the field and gives its orders.</summary>
    void CaptainThink(Unit cap)
    {
        if (!cap.Commands || cap == Possessed || Time < cap.NextOrders) return;
        cap.NextOrders = Time + 2.5 + cap.Id % 5 * .1;
        var men = Under(cap, CaptainReach);
        var (cx, cz) = CaptainHome(cap);
        // the horde: the nearest of it to what the commander keeps, within 110 m, and the knot about that one
        Unit lead = null;
        float best = 110 * VoxelWorld.PerMetre;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Team != Team.Horde || o.Bound || o.Envoy || o.Routed) continue;
            float d = MathF.Sqrt((o.X - cx) * (o.X - cx) + (o.Z - cz) * (o.Z - cz));
            if (d < best) { best = d; lead = o; }
        }
        if (lead != null) { Front(cap, men, lead, cx, cz); return; }
        // the band beaten off: what it broke about where the commander stood its people is mended
        if (_lastFront.Remove(cap.Id, out var front) && !Waves.Active) MendFront(cap, front.x, front.z);
        if (cap.Goal >= 0) { HoldGoal(cap, men); return; }
        if (Alarm)
        {
            var (gx, _, gz) = Nav.Feet(Gate);
            int spot = StandToward(gx, gz, cx, cz, 4 * VoxelWorld.PerMetre);
            Place(cap, Melee(men), spot, "alarm", "Командир собирает бойцов у ворот цитадели.");
        }
    }

    /// <summary>What a commander keeps: the spot the player gave it, or the citadel.</summary>
    (float x, float z) CaptainHome(Unit cap)
    {
        if (cap.Goal >= 0) { var (gx, _, gz) = Nav.Feet(cap.Goal); return (gx, gz); }
        return (Site.KeepX, Site.KeepZ);
    }

    static List<Unit> Melee(List<Unit> men) => men.FindAll(u => !u.T.Ranged || u.T.Mounted);
    static List<Unit> Shooters(List<Unit> men) => men.FindAll(u => u.T.Ranged && !u.T.Mounted);

    /// <summary>The horde is coming: the line formed inside the wall it comes at, the shooters up on it, the hurt sent back.</summary>
    void Front(Unit cap, List<Unit> men, Unit lead, float cx, float cz)
    {
        // the knot of the horde about the nearest of it
        float tx = 0, tz = 0; int n = 0;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Team != Team.Horde || o.Bound || o.Envoy) continue;
            if ((o.X - lead.X) * (o.X - lead.X) + (o.Z - lead.Z) * (o.Z - lead.Z) > 24 * 24 * 64) continue;
            tx += o.X; tz += o.Z; n++;
        }
        tx /= n; tz /= n;
        // the first wall of ours on the way from it to what is kept, and the line a few steps inside it
        var wall = WallBetween(tx, tz, cx, cz);
        float lx, lz;
        if (wall is { } w) { (lx, lz) = Toward(w.x, w.z, cx, cz, 3.5f * VoxelWorld.PerMetre); _lastFront[cap.Id] = (w.x, w.z); }
        else if (cap.Goal >= 0) (lx, lz) = (cx, cz);
        else (lx, lz) = Toward(cx, cz, tx, tz, 14 * VoxelWorld.PerMetre);
        int line = NearestStand((int)lx, (int)lz, 2);
        var melee = Melee(men);
        Place(cap, melee, line, "line", wall != null ? "Командир строит бойцов у стены, к которой идёт орда." : "Командир строит бойцов навстречу орде.");
        // the shooters onto the wall-walks that face the horde; with no wall, behind the line
        var shooters = Shooters(men);
        if (shooters.Count > 0)
        {
            var posts = wall is { } ww ? WallWalks(ww.x, ww.z, tx, tz, shooters.Count) : new List<int>();
            if (posts.Count == 0) Place(cap, shooters, NearestStand((int)Toward(lx, lz, cx, cz, 4 * VoxelWorld.PerMetre).x, (int)Toward(lx, lz, cx, cz, 4 * VoxelWorld.PerMetre).z, 2), "shooters", null);
            else
            {
                // each to the post nearest it it can get to (a wall with no stair up it is no post), the rest behind the line
                var free = new List<int>(posts);
                var below = new List<Unit>();
                bool moved = false;
                foreach (var s in shooters)
                {
                    if (s.Ward == cap && s.Post >= 0 && posts.Exists(p => CellsApart(s.Post, p) <= 2)) { free.RemoveAll(p => CellsApart(s.Post, p) <= 2); continue; }
                    bool placed = false;
                    for (int tries = 0; tries < 3 && free.Count > 0 && !placed; tries++)
                    {
                        int pick = 0; float near = float.MaxValue;
                        for (int i = 0; i < free.Count; i++)
                        {
                            var (px, _, pz) = Nav.Feet(free[i]);
                            float d = (px - s.X) * (px - s.X) + (pz - s.Z) * (pz - s.Z);
                            if (d < near) { near = d; pick = i; }
                        }
                        int post = free[pick];
                        free.RemoveAt(pick);
                        if (Command(new[] { s }, post) > 0) { s.Ward = cap; placed = moved = true; }
                    }
                    if (!placed) below.Add(s);
                }
                if (moved) { CaptainPosted++; OrderSaid(cap, "posted", "Командир ставит стрелков на стену против орды."); }
                if (below.Count > 0) { var (rx, rz) = Toward(lx, lz, cx, cz, 4 * VoxelWorld.PerMetre); Place(cap, below, NearestStand((int)rx, (int)rz, 2), "shooters", null); }
            }
        }
        // the commander itself a few steps behind its line
        var (bx, bz) = Toward(lx, lz, cx, cz, 5 * VoxelWorld.PerMetre);
        int own = NearestStand((int)bx, (int)bz, 2);
        if (own >= 0 && (cap.Post < 0 || CellsApart(cap.Post, own) > 3)) Command(new[] { cap }, own);
        // the badly hurt back to mend
        foreach (var u in men)
            if (!u.SentBack && u.Hp < u.MaxHp * .45f && Nearest(u, Team.Horde, 30 * VoxelWorld.PerMetre) != null)
            {
                u.SentBack = true;
                CaptainSentBack++;
                OrderSaid(cap, "back", "Командир отводит раненых к цитадели.");
            }
        // its people wavering: the horn; the horde on them: the shield wall
        int shaken = 0, pressed = 0;
        foreach (var u in Under(cap, CaptainReach))
        {
            if (u.Pain > .55f) shaken++;
            if (Nearest(u, Team.Horde, 6 * VoxelWorld.PerMetre) != null) pressed++;
        }
        if (shaken >= 2 && CallReady(cap, 3)) Call(cap, 3, float.NaN, 0, 0);
        else if (pressed >= 3 && CallReady(cap, 1)) Call(cap, 1, float.NaN, 0, 0);
    }

    /// <summary>The spot the player gave a commander: it stands there, and those under its word about it.</summary>
    void HoldGoal(Unit cap, List<Unit> men)
    {
        if (cap.Post != cap.Goal) Command(new[] { cap }, cap.Goal);
        var (gx, _, gz) = Nav.Feet(cap.Goal);
        int front = NearestStand((int)gx, (int)gz, 2);
        Place(cap, men, front, "goal", "Командир держит указанное место.");
    }

    /// <summary>
    /// Sends some to stand formed at a spot, as the player's word does, unless they stand there already by its own word:
    /// they are under the commander's word after it.
    /// </summary>
    void Place(Unit cap, List<Unit> men, int spot, string key, string said)
    {
        if (spot < 0 || men.Count == 0) return;
        var go = new List<Unit>();
        foreach (var u in men) if (u.Ward != cap || u.Post < 0 || CellsApart(u.Post, spot) > 4) go.Add(u);
        if (go.Count == 0) return;
        // all of them, so that the ranks are made up again round the new spot
        Command(men, spot);
        foreach (var u in men) u.Ward = cap;
        CaptainFormed++;
        if (said != null) OrderSaid(cap, key, said);
    }

    /// <summary>A point so far from one toward another (or the other itself, if nearer).</summary>
    static (float x, float z) Toward(float ax, float az, float bx, float bz, float by)
    {
        float dx = bx - ax, dz = bz - az, d = MathF.Sqrt(dx * dx + dz * dz);
        if (d < 1e-3f) return (ax, az);
        float k = MathF.Min(1, by / d);
        return (ax + dx * k, az + dz * k);
    }

    int StandToward(float ax, float az, float bx, float bz, float by)
    {
        var (x, z) = Toward(ax, az, bx, bz, by);
        return NearestStand((int)x, (int)z, 2);
    }

    /// <summary>Whether a column holds a wall of ours: our masonry or timber on top, two metres and more over the ground.</summary>
    bool OurWallAt(int x, int z)
    {
        if (!World.InBounds(x, 1, z)) return false;
        int top = World.SurfAt(x, z);
        return top - World.GroundTop(x, z) >= 2 * VoxelWorld.PerMetre && top > 0 && Mat.IsPlayer[World.Get(x, top - 1, z)];
    }

    /// <summary>The first wall of ours met going from the horde to what is kept, as a column; null if there is none.</summary>
    (int x, int z)? WallBetween(float fromX, float fromZ, float toX, float toZ)
    {
        float dx = toX - fromX, dz = toZ - fromZ, d = MathF.Sqrt(dx * dx + dz * dz);
        if (d < 1) return null;
        for (float s = 0; s <= d; s += 2)
        {
            int x = (int)(fromX + dx * s / d), z = (int)(fromZ + dz * s / d);
            if (OurWallAt(x, z)) return (x, z);
        }
        return null;
    }

    /// <summary>
    /// Cells on the walls of ours about a point of them, to stand on and shoot from: on top of our masonry or timber two
    /// metres and more over the ground, the nearest to the horde first, a couple of metres apart.
    /// </summary>
    List<int> WallWalks(int wx, int wz, float tx, float tz, int want)
    {
        var found = new List<(int cell, float d)>();
        int ci = wx >> NavGrid.Shift, ck = wz >> NavGrid.Shift, r = 18;
        for (int k = Math.Max(0, ck - r); k <= Math.Min(Nav.NZ - 1, ck + r); k++)
            for (int i = Math.Max(0, ci - r); i <= Math.Min(Nav.NX - 1, ci + r); i++)
            {
                int x = (i << NavGrid.Shift) + NavGrid.CS / 2, z = (k << NavGrid.Shift) + NavGrid.CS / 2;
                if (!OurWallAt(x, z)) continue;
                for (int y = Nav.NY - 1; y >= 1; y--)
                {
                    if (!Nav.Stand(i, y, k, 2)) continue;
                    int c = Nav.Id(i, y, k);
                    if (Nav.Floor[c] - World.GroundTop(x, z) < 2 * VoxelWorld.PerMetre) break;
                    found.Add((c, (x - tx) * (x - tx) + (z - tz) * (z - tz)));
                    break;
                }
            }
        found.Sort((a, b) => a.d.CompareTo(b.d));
        var picked = new List<int>();
        foreach (var (c, _) in found)
        {
            if (picked.Count >= want) break;
            bool apart = true;
            foreach (int p in picked) if (CellsApart(p, c) < 2) { apart = false; break; }
            if (apart) picked.Add(c);
        }
        return picked;
    }

    /// <summary>The band gone: what of ours was broken about the wall it came at is marked to be mended, for the workers.</summary>
    void MendFront(Unit cap, int x, int z)
    {
        int r = 24 * VoxelWorld.PerMetre;
        var meant = MeantIn(x - r, z - r, x + r, z + r);
        if (meant.Count == 0) return;
        var lay = new VoxelList(); var clear = new VoxelList();
        MendPlan(meant, lay, clear);
        if (lay.Count + clear.Count < 6) return;
        Mend(lay, clear);
        CaptainMended++;
        OrderSaid(cap, "mend", "Орда отбита. Командир зовёт рабочих чинить проломы.");
    }

    // ------------------------------------------------------------ the calls

    public bool CallReady(Unit cap, int n) => Time >= cap.Ready[n];

    /// <summary>
    /// A commander's call: 0 «За мной!», 1 «Держать строй», 2 «Залп!» at a point (voxels; NaN: at the horde nearest it),
    /// 3 «Рог». Null when made, else why it could not be.
    /// </summary>
    public string Call(Unit cap, int n, float ax, float ay, float az)
    {
        if (cap == null || cap.Dead || cap.T != Kinds.Captain) return "это не командир";
        if (!CallReady(cap, n)) return $"«{CallNames[n]}» будет готов через {cap.Ready[n] - Time:0} с";
        int done = 0;
        switch (n)
        {
            case 0:
                foreach (var u in Units)
                {
                    if (!Soldierly(u, cap) || Dist(u, cap) > 20 * VoxelWorld.PerMetre) continue;
                    u.Follows = cap; u.FollowUntil = Time + 25; u.RallyUntil = Math.Max(u.RallyUntil, Time + 25);
                    u.Ward = cap; u.Path = null; u.NextSearch = 0;
                    done++;
                }
                if (done == 0) return "рядом нет бойцов";
                break;
            case 1:
                foreach (var u in Units)
                {
                    if (!Soldierly(u, cap) || Dist(u, cap) > 15 * VoxelWorld.PerMetre) continue;
                    u.Follows = null;
                    Command(new[] { u }, u.Cell);
                    u.Ward = cap; u.WallUntil = Time + 20;
                    done++;
                }
                if (done == 0) return "рядом нет бойцов";
                break;
            case 2:
                if (float.IsNaN(ax))
                {
                    var foe = Nearest(cap, Team.Horde, 60 * VoxelWorld.PerMetre);
                    if (foe == null) return "орды не видно";
                    (ax, ay, az) = (foe.X, foe.Y + foe.T.BodyH * .5f, foe.Z);
                }
                int shooters = 0;
                foreach (var u in Units)
                {
                    if (!Soldierly(u, cap) || !u.T.Ranged || Dist(u, cap) > 50 * VoxelWorld.PerMetre) continue;
                    shooters++;
                    if (Volley(u, ax, ay, az)) done++;
                }
                if (done == 0) return shooters == 0 ? "рядом нет стрелков" : "стрелкам туда не достать";
                break;
            case 3:
                foreach (var u in Units)
                {
                    if (u.Dead || u.Inside || u.Team != Team.Player || Kinds.IsEngine(u.T) || Dist(u, cap) > 40 * VoxelWorld.PerMetre) continue;
                    u.Pain = 0;
                    // the hurt who can stand come back to the line
                    if ((u.Mending || u.SentBack) && u.Hp > u.MaxHp * .5f && !Disabled(u)) { u.Mending = false; u.SentBack = false; u.Why = ""; if (u.Post >= 0) GoTo(u, u.Post); }
                    // the frightened stop running, unless the horde is at their heels
                    if (u.State == UnitState.Flee && Nearest(u, Team.Horde, 9 * VoxelWorld.PerMetre) == null) { u.State = UnitState.Idle; u.Path = null; }
                    if (u.T.Work < 1 || u.Militia) u.RallyUntil = Math.Max(u.RallyUntil, Time + 20);
                    done++;
                }
                break;
        }
        cap.Ready[n] = Time + CallRest[n];
        Called?.Invoke(cap, n);
        Say($"Командир: «{CallNames[n]}»", 1, cap);
        return null;
    }

    /// <summary>
    /// Where a commander's eyes fall, up to 80 m (voxels): on one of the horde they meet, over a wall or not (it points
    /// past it), else on the ground or the wall they meet; false when they meet nothing so near. The one of the horde, if
    /// it is one.
    /// </summary>
    public bool Aimed(Unit cap, out float x, out float y, out float z, out Unit foe)
    {
        float reach = 80 * VoxelWorld.PerMetre;
        var seen = Sighted(cap, reach, out _);
        foe = seen != null && seen.Team != Team.Player && !seen.Bound ? seen : null;
        if (foe != null) { (x, y, z) = (foe.X, foe.Y + foe.T.BodyH * .5f, foe.Z); return true; }
        var (ox, oy, oz, dx, dy, dz) = Gaze(cap);
        bool wall = Raycast.Cast(World, ox, oy, oz, dx, dy, dz, out var hit, skipGrass: true);
        (x, y, z) = wall ? (hit.X + .5f, hit.Y + 1f, hit.Z + .5f) : (float.NaN, float.NaN, float.NaN);
        return wall && (x - ox) * (x - ox) + (y - oy) * (y - oy) + (z - oz) * (z - oz) <= reach * reach;
    }

    /// <summary>
    /// A commander's order by looking (first person): the soldiers under its word go strike the one of the horde its
    /// eyes rest on, or stand where they fall, in ranks. How many heed it; the place they were sent, or -1 for a strike.
    /// </summary>
    public int LookOrder(Unit cap, out int cell, out Unit foe)
    {
        cell = -1;
        if (!Aimed(cap, out float x, out float y, out float z, out foe)) return 0;
        var men = new List<Unit>();
        foreach (var u in Units) if (Soldierly(u, cap) && Dist(u, cap) <= CaptainReach) men.Add(u);
        if (men.Count == 0) return 0;
        int n;
        if (foe != null) n = Hunt(men, foe);
        else
        {
            cell = StandNear((int)x, (int)y + 2, (int)z, Kinds.Swordsman.Move.Height);
            if (cell < 0) return 0;
            n = Command(men, cell);
        }
        foreach (var u in men) { u.Ward = cap; u.Follows = null; }
        return n;
    }

    /// <summary>How many soldiers a commander's word reaches now (first person shows it).</summary>
    public int Heeding(Unit cap)
    {
        int n = 0;
        foreach (var u in Units) if (Soldierly(u, cap) && Dist(u, cap) <= CaptainReach) n++;
        return n;
    }

    /// <summary>One of ours who heeds a commander's call to arms: a soldier on its feet, not an engine.</summary>
    bool Soldierly(Unit u, Unit cap) => u != cap && !u.Dead && !u.Inside && u.Team == Team.Player && u.Captor == null && u.Airborne == null && u != Possessed
        && (u.T.Work < 1 || u.Militia) && !Kinds.IsEngine(u.T) && u.T != Kinds.Crewman && u.T != Kinds.Captain && !u.T.Prays && Armed(u) && !u.Mending;

    /// <summary>One shooter's part in a volley: a shot at the point, falling about it, at one of the horde near it if any.</summary>
    bool Volley(Unit u, float ax, float ay, float az)
    {
        if (u.T == Kinds.OurAlchemist) return false;
        float dx = ax - u.X, dz = az - u.Z, d = MathF.Sqrt(dx * dx + dz * dz);
        // a volley is loosed high, as far as the bow goes by day, the dark or not (it is aimed by the commander's eye)
        if (d > Math.Max(u.T.Range, 20 * VoxelWorld.PerMetre) * 1.2f) return false;
        // whoever of the horde stands nearest the point, within three metres of it, is what this one shoots at
        Unit mark = null; float best = 3 * VoxelWorld.PerMetre;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Team != Team.Horde || o.Bound) continue;
            float od = MathF.Sqrt((o.X - ax) * (o.X - ax) + (o.Z - az) * (o.Z - az)) + (float)Rng.NextDouble() * 6;
            if (od < best) { best = od; mark = o; }
        }
        float spread = 1.2f * VoxelWorld.PerMetre;
        float px = ax + ((float)Rng.NextDouble() - .5f) * 2 * spread, pz = az + ((float)Rng.NextDouble() - .5f) * 2 * spread;
        float ox = u.X, oy = u.Y + u.T.BodyH * .8f, oz = u.Z;
        var kind = ShotOf(u.T);
        float ex = mark?.X ?? px, ey = mark != null ? mark.Y + mark.T.BodyH * .55f : ay, ez = mark?.Z ?? pz;
        float len = MathF.Sqrt((ex - ox) * (ex - ox) + (ey - oy) * (ey - oy) + (ez - oz) * (ez - oz));
        u.Facing = MathF.Atan2(ez - oz, ex - ox);
        u.Swing = 1;
        u.Cooldown = u.T.AttackTime * ShotPace(u);
        Shots++;
        Arrows.Add(new Arrow
        {
            X0 = ox, Y0 = oy, Z0 = oz, X1 = ex, Y1 = ey, Z1 = ez, Dur = Math.Max(.06f, len / SpeedOf(kind)),
            Target = mark, From = u, Hits = mark != null && Rng.NextDouble() < .75 * WeatherAim,
            Damage = u.T.Damage * (.9f + (float)Rng.NextDouble() * .4f) * ShotHarm(u) * Heartened(u), Fire = u.FireArrows && Kindles(u.T),
            Kind = kind, Arc = ArcOf(kind),
        });
        return true;
    }

    /// <summary>One following its commander's «За мной!»: to its place behind it, unless the horde is at hand to fight.</summary>
    bool FollowTick(Unit u, float dt)
    {
        var c = u.Follows;
        if (c == null || c.Dead || c.Inside || Time >= u.FollowUntil)
        {
            u.Follows = null;
            // where the following ended, there it stands
            if (c != null && !c.Dead) { u.Hold = true; u.Post = u.Dest = u.Cell; u.Ward = c; }
            return false;
        }
        if (Nearest(u, Team.Horde, u.T.Reach + 10) != null) return false;
        // its place in a file of threes behind the commander's back
        int slot = (u.Id * 7) % 9;
        float fx = MathF.Cos(c.Facing), fz = MathF.Sin(c.Facing), back = (2.5f + slot / 3 * 1.4f) * VoxelWorld.PerMetre, side = (slot % 3 - 1) * 1.3f * VoxelWorld.PerMetre;
        float x = c.X - fx * back - fz * side, z = c.Z - fz * back + fx * side;
        int cell = StandNear((int)x, (int)c.Y + 2, (int)z, u.T.Move.Height);
        if (cell < 0) cell = c.Cell;
        u.Post = u.Dest = cell;
        if (CellsApart(u.Cell, cell) <= 1) { u.Path = null; u.State = UnitState.Idle; u.Facing = c.Facing; return true; }
        if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
        {
            u.NextSearch = Time + .8;
            if (!GoTo(u, cell, adjacent: true, budget: 4000)) u.Path = null;
        }
        if (u.Path != null) Walk(u, dt);
        else u.State = UnitState.Idle;
        return true;
    }

    /// <summary>How much harder one of ours strikes for its commander's word (and the one gone into, for a blow held back).</summary>
    float Heartened(Unit a) => (a.RallyUntil > Time ? 1.2f : 1) * (a == Possessed ? PossessedArm : 1);

    /// <summary>How much of a blow one of ours in its commander's shield wall takes.</summary>
    static float Walled(Unit b, double now) => b.WallUntil > now ? .65f : 1;

    // ------------------------------------------------------------ the save

    void WriteCaptains(BinaryWriter w)
    {
        var caps = Units.FindAll(u => !u.Dead && u.T == Kinds.Captain);
        w.Write(caps.Count);
        foreach (var c in caps)
        {
            w.Write(c.Id); w.Write(c.Commands); w.Write(c.Goal);
            for (int i = 0; i < 4; i++) w.Write(c.Ready[i] - Time);
        }
    }

    void ReadCaptains(BinaryReader r)
    {
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            int id = r.ReadInt32();
            bool commands = r.ReadBoolean();
            int goal = r.ReadInt32();
            var ready = new double[4];
            for (int i = 0; i < 4; i++) ready[i] = r.ReadDouble();
            var c = Units.Find(u => u.Id == id);
            if (c == null) continue;
            c.Commands = commands; c.Goal = goal;
            for (int i = 0; i < 4; i++) c.Ready[i] = Time + ready[i];
        }
    }
}
