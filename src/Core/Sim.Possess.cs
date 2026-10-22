using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The player gone into one of ours, to see with its eyes and act with its hands. Its own wits are set aside while
/// the player is in it: it goes where it is told, at a walk or a run, up a step or down off a ledge as its legs allow,
/// up and down a ladder, through our gates, which open for it as for anyone of ours; it looks where the player looks.
/// It strikes whoever of the horde is before it and in reach, looses an arrow along the line it looks down — and the
/// arrow strikes whoever it meets first, or the ground, or nothing — and, if it can work, takes a pick to the voxel it
/// looks at and lays stone where it is told. All else goes on about it as ever: the horde comes at it, it bleeds, it
/// can be thrown or carried off. When it dies, goes into the citadel or is led away, the player is out of it.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The one of ours the player has gone into, or null.</summary>
    public Unit Possessed { get; private set; }
    /// <summary>
    /// What the player wants of it now: which way to go (voxels: x and z of a vector no longer than one), whether at a
    /// run; where it looks (the facing, as <see cref="Unit.Facing"/>, and up or down, radians); whether the blow (or the
    /// shot, or the pick) and the laying are held; up or down a ladder (+1, −1, 0); and what it lays.
    /// </summary>
    public float WishX, WishZ, LookYaw, LookPitch;
    public bool WishRun, WishStrike, WishLay;
    public int WishClimb;
    public byte LayMat = Mat.Stone;
    /// <summary>The player left it, and why (a word for the screen); null for leaving of the player's own will.</summary>
    public event Action<string> Unpossessed;

    /// <summary>How far one of ours reaches with a pick or a trowel from its eye, in voxels; and how big a bite the pick takes.</summary>
    const float HandReach = 4 * VoxelWorld.PerMetre, Bite = 2.2f;

    /// <summary>
    /// Seconds from the click to the blade at the body: the arm is cocked and brought across the sight before it lands
    /// (the hands before the eyes are drawn to the same beat), so the wound is cut then and not at the click.
    /// </summary>
    public const float HandBlowLag = .15f;
    // the blow swung and not yet landed: at whom, and when it lands
    Unit _handFoe;
    double _handLands;

    /// <summary>Whether this one can be gone into now: ours, alive and on the field, not one of the risen dead.</summary>
    public bool CanPossess(Unit u) => u is { Dead: false, Inside: false, Captor: null, Airborne: null } && u.Team == Team.Player && u.T != Kinds.Risen;

    /// <summary>Goes into one of ours: it leaves off whatever it was doing and waits on the player.</summary>
    public bool Possess(Unit u)
    {
        if (!CanPossess(u)) return false;
        if (Possessed != null) Unpossess(null);
        ReleaseSite(u); Busy(u); StopBuild(u);
        u.Path = null; u.Hold = false; u.Prey = null; u.Target = null; u.Patient = null;
        if (u.Defusing is { } c) { c.Defuser = null; u.Defusing = null; }
        u.Shunning = null; u.Mending = false;
        u.State = UnitState.Idle; u.Pastime = Pastime.None;
        u.Why = "в нём игрок";
        Possessed = u;
        LookYaw = u.Facing; LookPitch = 0;
        WishX = WishZ = 0; WishRun = WishStrike = WishLay = false; WishClimb = 0;
        _swingRight = _swingDown = 0; _lookYaw0 = float.NaN;
        _handFoe = null;
        return true;
    }

    /// <summary>
    /// Leaves the one gone into: it stands where it was left and minds itself again — a soldier holds the spot, a worker
    /// goes back to work.
    /// </summary>
    public void Unpossess(string why)
    {
        var u = Possessed;
        if (u == null) return;
        Possessed = null;
        WishX = WishZ = 0; WishStrike = WishLay = false; WishClimb = 0;
        _handFoe = null;
        u.Why = "";
        u.Effort = 0;
        if (!u.Dead && !u.Inside && u.Captor == null && u.T.Work < 1) { u.Post = u.Cell; u.Dest = u.Cell; }
        Unpossessed?.Invoke(why);
    }

    /// <summary>The player out of one that has died, gone into the citadel or been led off.</summary>
    void KeepPossession()
    {
        var u = Possessed;
        if (u == null) return;
        if (u.Dead) Unpossess($"{u.Name} пал");
        else if (u.Inside) Unpossess($"{u.Name} в цитадели");
        else if (u.Captor != null) Unpossess($"{u.Name} схвачен");
    }

    /// <summary>One gone into, for a tick: its going, its climbing, and what its hands do.</summary>
    void PossessedTick(Unit u, float dt)
    {
        // a rider at the gallop, a troll's flesh knitting: as under its own wits
        if (u.T.Mounted || u.T == Kinds.TameTroll) TroopTick(u, dt);
        u.Facing = LookYaw;
        // the eyes' swing of the last fifth of a second or so: the way the blade goes in the blow struck now
        if (!float.IsNaN(_lookYaw0))
        {
            float fade = MathF.Exp(-dt / .2f);
            _swingRight = _swingRight * fade + MathF.IEEERemainder(LookYaw - _lookYaw0, MathF.Tau);
            _swingDown = _swingDown * fade - (LookPitch - _lookPitch0);
        }
        _lookYaw0 = LookYaw; _lookPitch0 = LookPitch;
        float wl = MathF.Sqrt(WishX * WishX + WishZ * WishZ);
        bool moving = wl > .05f;
        float speed = u.T.Move.Speed * NavGrid.CS * u.Pace * CartPace(u) * (WishRun ? 1.6f : 1) * (u.Hp < u.MaxHp * .4f ? .7f : 1)
            * (Nav.Water[u.Cell] > 0 ? .6f : 1) * (Nav.Spikes[u.Cell] > 0 ? .4f : 1) * Gait(u) * (Mires.Count > 0 && InMire(u.X, u.Z) ? .35f : 1);
        bool climbed = false;
        if (WishClimb != 0) climbed = ClimbBy(u, WishClimb * speed * .5f * dt);
        if (moving && !climbed)
        {
            float k = speed * dt / Math.Max(1, wl), mx = WishX * k, mz = WishZ * k;
            bool went = StepTo(u, u.X + mx, u.Z + mz) || StepTo(u, u.X + mx, u.Z) || StepTo(u, u.X, u.Z + mz);
            // walking into a ladder climbs it
            if (!went && (Nav.Ladder[u.Cell] != 0 || u.Cell + Nav.Layer < Nav.Count && Nav.Ladder[u.Cell + Nav.Layer] != 0)) climbed = ClimbBy(u, speed * .5f * dt);
            if (went) u.Stride += speed * dt;
        }
        // on the ground the feet come to the floor of the cell, a step taken in a stride rather than at once
        if (!Climbing(u)) u.Y += (Nav.Floor[u.Cell] - u.Y) * Math.Min(1, dt * 14);
        u.State = moving || climbed ? UnitState.Walk : UnitState.Idle;
        u.Pastime = Pastime.None;
        if (_handFoe != null && Time >= _handLands) Land(u);
        if (WishStrike && u.Cooldown <= 0) Blow(u);
        else if (WishStrike && u.T.Work > 0 && !u.T.Ranged) Pick(u, dt);
        if (WishLay && u.T.Work > 0) Trowel(u, dt);
    }

    /// <summary>A step toward a spot: inside the cell it is in, or into the next cell — level, up a step its legs manage, or down off a ledge.</summary>
    bool StepTo(Unit u, float nx, float nz)
    {
        if (nx < 1 || nz < 1 || nx >= World.SX - 1 || nz >= World.SZ - 1) return false;
        int ci = Nav.I(u.Cell), cy = Nav.Y(u.Cell), ck = Nav.K(u.Cell);
        int ni = (int)MathF.Floor(nx) >> NavGrid.Shift, nk = (int)MathF.Floor(nz) >> NavGrid.Shift;
        if (ni == ci && nk == ck) { u.X = nx; u.Z = nz; return true; }
        // into a diagonal neighbour only round a corner that is open on both sides
        if (ni != ci && nk != ck && !(Passable(u, Nav.Id(ni, cy, ck)) && Passable(u, Nav.Id(ci, cy, nk)))) return false;
        var m = u.T.Move;
        for (int s = 0; s <= m.Climb + m.Drop; s++)
        {
            int dy = s <= m.Climb ? s : m.Climb - s, ny = cy + dy;
            if (!Nav.In(ni, ny, nk)) continue;
            int c = Nav.Id(ni, ny, nk);
            if (!Passable(u, c))
            {
                // a shut gate of ours opens for us: wait at it
                if (Gates.Count > 0 && Knock(c)) return false;
                continue;
            }
            if (!FlowField.StepsOk(m, Nav.Floor[u.Cell], Nav.Floor[c]) && Nav.Ladder[c] == 0) continue;
            if (Gates.Count > 0 && Knock(c)) return false;
            u.X = nx; u.Z = nz; u.Cell = c;
            return true;
        }
        return false;
    }

    /// <summary>Up (or down) the ladder it is at, by so many voxels; false if there is no ladder there or no room.</summary>
    bool ClimbBy(Unit u, float dy)
    {
        // a horse or a hound takes no ladder
        if (!u.T.Move.Ladders) return false;
        int i = Nav.I(u.Cell), y = Nav.Y(u.Cell), k = Nav.K(u.Cell), h = u.T.Move.Height;
        if (dy > 0)
        {
            bool ladder = Nav.Ladder[u.Cell] != 0 || Nav.In(i, y + 1, k) && Nav.Ladder[Nav.Id(i, y + 1, k)] != 0;
            if (!ladder || !Nav.CanBe(i, y + 1, k, h)) return false;
            u.Y += dy;
            if (u.Y >= (y + 1) << NavGrid.Shift)
            {
                u.Cell = Nav.Id(i, y + 1, k);
                if (Nav.Ground(i, y, k)) u.Y = Nav.Floor[u.Cell];
            }
            return true;
        }
        bool down = y >= 2 && (Nav.Ladder[u.Cell] != 0 || Nav.Ladder[Nav.Id(i, y - 1, k)] != 0) && Nav.CanBe(i, y - 1, k, h);
        if (!down) return false;
        u.Y += dy;
        if (u.Y < y << NavGrid.Shift) u.Cell = Nav.Id(i, y - 1, k);
        return true;
    }

    /// <summary>Where the eyes of one gone into are, and which way it looks (voxels).</summary>
    public (float x, float y, float z, float dx, float dy, float dz) Gaze(Unit u)
    {
        float cp = MathF.Cos(LookPitch);
        return (u.X, u.Y + u.T.BodyH * .88f, u.Z, MathF.Cos(LookYaw) * cp, MathF.Sin(LookPitch), MathF.Sin(LookYaw) * cp);
    }

    /// <summary>
    /// Whoever the line of the eyes meets first, of the horde, the beasts or ours, up to a distance: each body a standing
    /// cylinder. Null when it meets none; with how far along the line.
    /// </summary>
    public Unit Sighted(Unit from, float reach, out float along)
    {
        var (ox, oy, oz, dx, dy, dz) = Gaze(from);
        Unit best = null;
        along = reach;
        float hl = MathF.Sqrt(dx * dx + dz * dz);
        foreach (var u in Units)
        {
            if (u == from || u.Dead || u.Inside) continue;
            float r = u.T.BodyR + .8f;
            float t;
            if (hl < 1e-4f)
            {
                // straight up or down: only one stood right there
                if ((u.X - ox) * (u.X - ox) + (u.Z - oz) * (u.Z - oz) > r * r) continue;
                t = dy < 0 ? oy - (u.Y + u.T.BodyH) : u.Y - oy;
                if (t < 0) continue;
            }
            else
            {
                // where the line comes nearest the body's axis on the ground, and in from there to the round of it
                float px = u.X - ox, pz = u.Z - oz, ux = dx / hl, uz = dz / hl, tc = px * ux + pz * uz, d2 = px * px + pz * pz - tc * tc;
                if (d2 > r * r || tc < 0) continue;
                float th = tc - MathF.Sqrt(r * r - d2);
                t = Math.Max(0, th) / hl;
                float yAt = oy + dy * t;
                if (yAt < u.Y - 1 || yAt > u.Y + u.T.BodyH + 1) continue;
            }
            if (t < along) { along = t; best = u; }
        }
        return best;
    }

    /// <summary>The voxel the eyes rest on, if one is within reach.</summary>
    public bool LookedAt(Unit u, float reach, out VoxelHit hit)
    {
        var (ox, oy, oz, dx, dy, dz) = Gaze(u);
        if (!Raycast.Cast(World, ox, oy, oz, dx, dy, dz, out hit, skipGrass: true)) return false;
        float hx = hit.X + .5f - ox, hy = hit.Y + .5f - oy, hz = hit.Z + .5f - oz;
        return hx * hx + hy * hy + hz * hz <= reach * reach;
    }

    /// <summary>How far the eyes have swung of late, right and down (radians), fading as they rest; and where they were the tick before.</summary>
    float _swingRight, _swingDown, _lookYaw0 = float.NaN, _lookPitch0;

    /// <summary>
    /// The blow of one gone into: a shot along its eyes for a bowman, else a stroke at whoever is before it — the stroke
    /// the player swung for (the way the mouse went just before), or the nearest the weapon makes of it.
    /// </summary>
    /// <summary>The cuts of a drill at the air, in turn.</summary>
    static readonly StrokeKind[] Drill = { StrokeKind.DiagR, StrokeKind.DiagL, StrokeKind.Overhead, StrokeKind.Sweep, StrokeKind.Rising };
    int _drill;

    void Blow(Unit u)
    {
        if (!Armed(u)) return;
        if (u.T.Ranged) { Loose(u); return; }
        // whoever of the horde or the beasts stands before it in reach, nearest the line of its eyes
        Unit best = null; float bs = float.MaxValue;
        float fx = MathF.Cos(LookYaw), fz = MathF.Sin(LookYaw);
        foreach (var t in Units)
        {
            if (t.Dead || t.Inside || t.Team == Team.Player || t.Bound || t.Envoy) continue;
            float dx = t.X - u.X, dz = t.Z - u.Z, d = MathF.Sqrt(dx * dx + dz * dz);
            if (d > ReachOf(u) + t.T.BodyR + 2 || MathF.Abs(t.Y - u.Y) > Math.Max(u.T.BodyH, t.T.BodyH)) continue;
            float ahead = d < .5f ? 1 : (dx * fx + dz * fz) / d;
            if (ahead < .6f) continue;
            float score = d * (2 - ahead);
            if (score < bs && Sight(u, t)) { bs = score; best = t; }
        }
        u.Cooldown = u.T.AttackTime * (.95f + (float)Rng.NextDouble() * .1f);
        var wish = WishStroke ?? StrokeOfSwing(_swingRight, _swingDown, LookPitch);
        if (best == null)
        {
            // a stroke at the air — or, for one who can work, at the voxel before it
            if (u.T.Work <= 0)
            {
                u.Swing = 1;
                // the mouse still and nobody before it: not a poke at nothing but the cuts of a fighter's drill, one
                // after another, as at a foe (a spear still thrusts)
                bool still = WishStroke == null && _swingRight * _swingRight + _swingDown * _swingDown < .03f * .03f;
                var k = Fit(u, null, still && wish == StrokeKind.Thrust ? Drill[_drill++ % Drill.Length] : wish);
                var g = Strokes.GripOf(Bodies.Plan(u.T, u.Look), u.T);
                u.Stroke = k; u.StrokeTime = Time; u.StrokeAt = Strokes.Contact(k);
                u.StrokeAim = Math.Clamp(-LookPitch * .6f, -.5f, .6f); u.StrokeYaw = 0; u.Glanced = false;
                u.StrokeLunge = Strokes.Straight(k) ? 0 : Math.Min(Strokes.MaxStep(u, g), Strokes.StepOf(k) * u.T.BodyH / 15) * .5f;
            }
            return;
        }
        u.State = UnitState.Fight;
        u.NextStroke = Fit(u, best, wish); u.StrokeFor = best;
        // swung now, for the eye; it lands when the blade comes to the body
        u.Swing = 1;
        u.Stroke = u.NextStroke; u.StrokeTime = Time; u.Glanced = false;
        _handFoe = best; _handLands = Time + HandBlowLag;
    }

    /// <summary>
    /// The blow swung at the click comes to the body: the wound is cut and the hurt done as for any blow, if the foe is
    /// still within the blade's reach — one that stepped out of it or fell meanwhile is missed. The stroke goes on being
    /// drawn as it was begun.
    /// </summary>
    void Land(Unit u)
    {
        _handFoe = null;
        double swung = u.StrokeTime;
        var k = u.Stroke;
        // only what the blade itself comes to is struck: swept along the stroke as it is drawn before the eyes, the
        // first body any of its striking part goes into — or none, and it cut the air
        var t = Touched(u, k, out float up);
        if (t != null)
        {
            // seen coming, it may yet be stepped out of the way of, or taken on a shield
            var g = Defend(u, t, k);
            if (g is Guarded.StepBack or Guarded.Dodge or Guarded.Duck) Missed(u, t, k, g);
            else if (g is Guarded.Block or Guarded.Parry) Turned(u, t, k, g);
            else Strike(u, t, Math.Clamp(up, .05f, .98f), .5f);
        }
        u.Stroke = k; u.StrokeTime = swung;
    }

    /// <summary>
    /// The blade of one gone into swept along its stroke about the moment it meets — from a fifth of the stroke before to
    /// a fifth after, the striking part from the hand to its end — and the first raider or beast any of it goes into
    /// (as they stand), with how high on that one it struck (0 its feet, 1 its crown); null when it cut only air.
    /// </summary>
    Unit Touched(Unit u, StrokeKind k, out float up)
    {
        up = .5f;
        var g = Strokes.GripOf(Bodies.Plan(u.T, u.Look), u.T);
        float c = Strokes.Contact(k), aim = Math.Clamp(-LookPitch * .6f, -.5f, .6f);
        float lunge = Strokes.Straight(k) ? 0 : Math.Min(Strokes.MaxStep(u, g), Strokes.StepOf(k) * u.T.BodyH / 15) * .5f;
        var near = Units.FindAll(t => !t.Dead && !t.Inside && !t.Bound && t.Team != Team.Player && t != u && Dist(u, t) <= ReachOf(u) + t.T.BodyR + 6);
        if (near.Count == 0) return null;
        for (float s = c - .22f; s <= c + .22f; s += .04f)
        {
            var pose = Strokes.Pose(g, u.T, k, Math.Clamp(s, 0, 1), aim, 0, lunge);
            for (float along = .2f; along <= 1.001f; along += .2f)
            {
                var w = Strokes.ToWorld(u, Strokes.Striking(g, k, pose, along));
                foreach (var t in near)
                {
                    var (bx, by, bz) = ToBody(t, w.X, w.Y, w.Z);
                    int ix = (int)MathF.Floor(bx), iy = (int)MathF.Floor(by), iz = (int)MathF.Floor(bz);
                    if (!InBody(t, ix, iy, iz)) continue;
                    up = (w.Y - t.Y) / Math.Max(1, t.T.BodyH);
                    return t;
                }
            }
        }
        return null;
    }

    /// <summary>Whether a half-voxel of a body's own frame is of it, as its wounds have left it.</summary>
    static bool InBody(Unit t, int x, int y, int z)
    {
        if (t.Wounds != null) return t.Wounds.Mat(x, y, z, out _) != 0;
        var plan = Bodies.Plan(t.T, t.Look);
        for (int p = 0; p < BodyPlan.Parts; p++)
            if (plan.Grid[p] is { } g && g.In(x, y, z) && g.M[g.Index(x, y, z)] != 0) return true;
        return false;
    }

    /// <summary>
    /// A shot loosed along the eyes, of whatever the kind shoots — an arrow, a bolt, a sling stone, a pot of powder: it
    /// goes to whoever it meets first before the ground or a wall — and strikes it surely, held as steady as the player
    /// holds it — or into what it meets of the world (a pot bursts there), or on out of reach.
    /// </summary>
    void Loose(Unit u)
    {
        var (ox, oy, oz, dx, dy, dz) = Gaze(u);
        float range = ShotRange(u);
        float wall = range;
        bool struck = Raycast.Cast(World, ox, oy, oz, dx, dy, dz, out var hit, skipVeg: true);
        if (struck) wall = MathF.Min(range, MathF.Sqrt((hit.X + .5f - ox) * (hit.X + .5f - ox) + (hit.Y + .5f - oy) * (hit.Y + .5f - oy) + (hit.Z + .5f - oz) * (hit.Z + .5f - oz)));
        var t = Sighted(u, wall, out float along);
        if (t != null && t.Team == Team.Player) t = null;
        float d = t != null ? along : wall;
        float chance = Math.Clamp(.97f - d / range * .3f, .6f, .97f) * WeatherAim;
        var kind = ShotOf(u.T);
        float harm = kind == Shot.Stone && Knows("leadshot") ? 1.33f : 1;
        if (kind == Shot.Stone && Knows("firepots") && Rng.NextDouble() < FirePotShare && (t == null || PotSafe(t))) kind = Shot.FirePot;
        // a pot thrown at one of the horde stood among ours is held back, as a bombardier holds it
        if (kind == Shot.Pot && t != null && !PotSafe(t)) { Say("Горшок не брошен: рядом с врагом свои.", 2); u.Cooldown = .5f; return; }
        u.Swing = 1;
        u.Cooldown = u.T.AttackTime * ShotPace(u) * (u.FireArrows && Kindles(u.T) ? (Knows("soaked") ? 1.1f : 1.35f) : 1);
        Shots++;
        var a = new Arrow
        {
            X0 = ox, Y0 = oy, Z0 = oz, X1 = ox + dx * d, Y1 = oy + dy * d, Z1 = oz + dz * d, Dur = Math.Max(.06f, d / SpeedOf(kind)),
            Target = t, From = u, Hits = t != null && Rng.NextDouble() < chance,
            Damage = u.T.Damage * (.9f + (float)Rng.NextDouble() * .4f) * ShotHarm(u) * harm * Oath(u), Fire = u.FireArrows && Kindles(u.T),
            Kind = kind, Arc = ArcOf(kind),
        };
        // a fire arrow into the world sets alight what it strikes
        if (t == null && a.Fire && struck && wall < range) a.At = WaterPlanner.Pack(hit.X, hit.Y, hit.Z);
        Arrows.Add(a);
    }

    /// <summary>
    /// The pick of one gone into, held to what it looks at: a bite of it — a little ball about the voxel struck — comes out
    /// when the work for it is done, as fast as the household's hands dig anything, and goes to the stores.
    /// </summary>
    void Pick(Unit u, float dt)
    {
        if (!LookedAt(u, HandReach, out var hit) || !Labour.Diggable(hit.M) || Site.Forbidden(hit.X, hit.Z)) { u.Effort = 0; return; }
        u.State = UnitState.Work;
        u.Effort += (float)(Labour.WorkPerSecond * Math.Max(.4f, u.T.Work) * u.Pace * dt);
        if (u.Swing <= 0) u.Swing = 1;
        var bite = new List<(int x, int y, int z, byte m)>();
        float cost = 0;
        int r = (int)MathF.Ceiling(Bite);
        for (int dy = -r; dy <= r; dy++)
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy + dz * dz > Bite * Bite) continue;
                    int x = hit.X + dx, y = hit.Y + dy, z = hit.Z + dz;
                    if (!World.InBounds(x, y, z) || y < 4 || Site.Forbidden(x, z)) continue;
                    byte m = World.Get(x, y, z);
                    if (!Labour.Diggable(m)) continue;
                    bite.Add((x, y, z, m));
                    cost += Labour.Dig(m) / DigPace;
                }
        if (u.Effort < cost) return;
        u.Effort = 0;
        foreach (var (x, y, z, cur) in bite)
        {
            World.Set(x, y, z, Mat.Air);
            if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
            Dug(x, y, z, cur);
            int kind = Labour.YieldKind(cur);
            if (kind != Stockpile.None) Stock[kind] += Labour.YieldShare(cur) * Math.Max(.6f, u.T.Yield) * YieldMore;
            if (cur == Mat.Ore) Stock.Gold += Labour.OreGold * Math.Max(.6f, u.T.Yield);
            Mined?.Invoke(x, y, z, cur);
        }
    }

    /// <summary>
    /// The trowel of one gone into: a block of what it lays — two voxels on a side, on the grid of two — goes on the face
    /// it looks at, from the stores, each voxel only where it is free and has something under it or beside it.
    /// </summary>
    void Trowel(Unit u, float dt)
    {
        if (!LookedAt(u, HandReach, out var hit)) { u.Effort = 0; return; }
        var (fx, fy, fz) = hit.Front;
        int bx = fx & ~1, by = fy & ~1, bz = fz & ~1;
        var block = new List<(int x, int y, int z)>();
        for (int dy = 0; dy < 2; dy++)
            for (int dz = 0; dz < 2; dz++)
                for (int dx = 0; dx < 2; dx++)
                {
                    int x = bx + dx, y = by + dy, z = bz + dz;
                    if (!World.InBounds(x, y, z) || !Brushes.Free(World.Get(x, y, z)) || Site.Forbidden(x, z)) continue;
                    // not into a body, its own or another's
                    if (_bodies[Nav.CellOf(x, y, z)] > 0 && Nav.CellOf(x, y, z) != u.Cell) continue;
                    if (MathF.Abs(x + .5f - u.X) < u.T.BodyR + 1 && MathF.Abs(z + .5f - u.Z) < u.T.BodyR + 1 && y >= u.Y - 1 && y < u.Y + u.T.BodyH) continue;
                    block.Add((x, y, z));
                }
        if (block.Count == 0) { u.Effort = 0; return; }
        u.State = UnitState.Work;
        u.Effort += (float)(Labour.WorkPerSecond * Math.Max(.4f, u.T.Work) * u.Pace * dt);
        if (u.Swing <= 0) u.Swing = 1;
        float cost = block.Count * Labour.Lay(LayMat) / LayPace;
        if (u.Effort < cost) return;
        u.Effort = 0;
        int ck = Labour.CostKind(LayMat);
        foreach (var (x, y, z) in block)
        {
            if (!Footed(x, y, z)) continue;
            if (ck != Stockpile.None && !Stock.Take(ck, 1)) { Say("Запас пуст: " + Stockpile.Name(ck) + ".", 2); WishLay = false; return; }
            byte m = Shapes.Lay(LayMat, x, y, z);
            World.Set(x, y, z, m);
            Remember(x, y, z, m);
        }
    }
}
