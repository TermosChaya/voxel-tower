using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A few of ours passing the time together, standing round a middle talking or squatting round it at dice: one of them
/// has the word (or the dice) at a time, and it passes round.
/// </summary>
public sealed class Huddle
{
    public Pastime Does;
    public readonly List<Unit> Members = new();
    /// <summary>The middle they stand or squat round.</summary>
    public float X, Z;
    /// <summary>Who has the word now (an index into the members), and when it passes on.</summary>
    public int Turn;
    public double TurnUntil;
    /// <summary>When the word last passed (the view's speaker gestures from it; Sim.Voices.cs may say a line then); when the ring breaks up.</summary>
    public double TurnAt, Until;

    /// <summary>The one with the word now, or null.</summary>
    public Unit Speaker => Turn >= 0 && Turn < Members.Count ? Members[Turn] : null;
}

/// <summary>
/// Where one of ours lies down for the night: on the bedding of a bunk, its feet at the bunk's foot end and its head on
/// the bolster, or on the ground.
/// </summary>
public sealed class Berth
{
    /// <summary>The bunk (its first voxel, packed), or −1 for the ground.</summary>
    public int Key = -1;
    /// <summary>Where its feet are, the way from its head to its feet, the height it lies at (voxels).</summary>
    public float X, Z, Facing, Y;
}

/// <summary>
/// Pawns with nothing to do don't stand like posts. A worker strolls about where it ran out of work, looks round, sits
/// down, stretches, talks in twos and threes with whoever else is idle close by, warms its hands at a brazier, and by
/// day goes into the town's street (Sim.Streets.cs), fishes from the bank, sweeps before a door, tends the flower beds and
/// kneels in the sanctuary; a soldier does the same round its post, sharpens its blade, plays at dice with the others
/// and now and then looks out to the west, where the horde comes from, and while the horde is in the valley does
/// nothing but that. The day has its shape: at noon they eat, on a bench, by a fire or where they stand; of an evening
/// they gather at the campfires; at night they sleep, in the bunks of the dwellings or on the ground by a fire, every
/// other soldier keeping the watch, and they get up at dawn with a stretch. Work, an order, the horn or an enemy ends
/// it at once: nothing of it keeps one of ours from work that waits.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The way the horde comes from, as a facing: west.</summary>
    const float Westward = MathF.PI;

    /// <summary>The leisure's own dice, apart from the simulation's: every pastime, its length and its spot, the street, what is said; so that what our idle people do never moves the draws the fighting and the falling take.</summary>
    readonly Random _life = new(4177);

    /// <summary>The dice the pastimes are chosen with: in the quiet way the simulation's own, as they always were (the same draws in the same order), else the leisure's own.</summary>
    Random Dice => QuietLife ? Rng : _life;
    /// <summary>Who lies in which bunk, by the bunk; and what each bunk is to lie in, found from its voxels.</summary>
    readonly Dictionary<int, Unit> _bunkBy = new();
    readonly Dictionary<int, Berth> _bunks = new();

    /// <summary>What the town's people have done of an evening and a night (for the bench): meals eaten, gone to sleep in a bunk or on the ground, rings formed, pastimes taken up.</summary>
    public int Meals, SleptInBunks, SleptOut, Rings, Fished, Swept, Tended, Knelt, Sharpened, Diced;

    /// <summary>
    /// The quiet way: those with nothing to do pass the time exactly as they did before the town had its day, its rings
    /// and its own pastimes (strolls, a seat, a pair talking, the fires, the street), with the simulation's own dice in the
    /// same order; no bed, no meal, no trip. For the scenes that stage something else (the self-test's, and the bench's of
    /// building, felling, the gods, the architect, the raids), whose checks follow the dice's order.
    /// </summary>
    public bool QuietLife;

    /// <summary>Noon: the middle sixth of the day, when those with nothing to do eat.</summary>
    public bool Noon => !Clock.Night && Clock.Share is > .42f and < .58f;
    /// <summary>Bedtime: the night, with no horde in the valley and no alarm.</summary>
    public bool Bedtime => Clock.Night && !Raids.Active && !Alarm;

    /// <summary>Passes the time; <paramref name="stay"/> keeps it where it stands (sent there, or on guard).</summary>
    void Idle(Unit u, float dt, bool stay)
    {
        u.State = UnitState.Idle;
        // the street empties at dusk, at the alarm, when the horde comes, or when its place is gone (Sim.Streets.cs)
        OffStreet(u);
        // woken by the day, the horde or the alarm, or by work marked about it (a house raised a storey over it)
        // (a bed staged for a scene, InBed, holds till the scene lets it go)
        if (u.Pastime == Pastime.Sleep && u.PastimeUntil < 1e5 && (!Bedtime || u.Berth is { } lying && WorkAbout((int)lying.X - 16, (int)lying.Z - 16, (int)lying.X + 16, (int)lying.Z + 16))) { Wake(u); return; }
        if (u.Pastime == Pastime.Stroll)
        {
            if (!stay && u.Path != null && u.PathI < u.Path.Count && !Walk(u, dt, .45f)) return;
            u.Path = null;
            var next = u.After == Pastime.None ? Pastime.Look : u.After;
            Pass(u, next, Lasts(u, next));
            if (next is Pastime.Sit or Pastime.Eat or Pastime.Sharpen && u.SeatAt != null) SitDown(u);
            else if (u.Spot != null && next == u.Spot.Does) TakeSpot(u);
            else if (next == Pastime.Sleep) LieDown(u);
            else if (next is Pastime.Fish or Pastime.Sweep or Pastime.Tend or Pastime.Kneel or Pastime.Eat) u.Facing = u.Gaze;
            return;
        }
        // a ring left with one in it is no ring (in the quiet way, a pair one of whom has gone)
        if (QuietLife) { if (u.Pastime == Pastime.Talk && (u.Mate == null || u.Mate.Dead || u.Mate.Mate != u)) u.PastimeUntil = 0; }
        else
        {
            if (u.Group != null && (u.Pastime is not (Pastime.Talk or Pastime.Dice) || u.Group.Members.Count < 2)) LeaveGroup(u);
            if (u.Pastime is Pastime.Talk or Pastime.Dice && u.Group == null) u.PastimeUntil = 0;
        }
        if (u.Pastime != Pastime.None && Time < u.PastimeUntil) { Tend(u, dt); return; }
        var was = u.Pastime;
        StandUp(u);
        // water drawn at the well: a drink of it
        if (was == Pastime.Fetch && !QuietLife && Dice.NextDouble() < .6) { Pass(u, Pastime.Drink, 3.5); return; }
        Choose(u, stay);
    }

    /// <summary>How long a pastime is kept up once taken up at the end of a stroll.</summary>
    double Lasts(Unit u, Pastime p) => p switch
    {
        Pastime.Sleep => SleepLeft(),
        Pastime.Eat => 12 + Dice.NextDouble() * 9,
        Pastime.Sharpen => 12 + Dice.NextDouble() * 10,
        Pastime.Fish => 30 + Dice.NextDouble() * 30,
        Pastime.Sweep => 10 + Dice.NextDouble() * 8,
        Pastime.Tend => 12 + Dice.NextDouble() * 10,
        Pastime.Kneel => 10 + Dice.NextDouble() * 8,
        Pastime.Warm => 6 + Dice.NextDouble() * 8,
        Pastime.Sit when u.SeatAt != null => 10 + Dice.NextDouble() * 14,
        _ => 2 + Dice.NextDouble() * 3,
    };

    void Pass(Unit u, Pastime p, double seconds)
    {
        u.Pastime = p;
        u.After = Pastime.None;
        u.PastimeUntil = Time + seconds;
    }

    /// <summary>Drops whatever it was passing the time with: there is something to do.</summary>
    void Busy(Unit u)
    {
        if (u.Pastime == Pastime.None) return;
        StandUp(u);
        LeaveGroup(u);
        if (u.Mate != null && u.Mate.Mate == u) { u.Mate.Mate = null; u.Mate.PastimeUntil = 0; }
        u.Mate = null;
        u.Pastime = u.After = Pastime.None;
        u.PastimeUntil = 0;
    }

    /// <summary>Keeps up a pastime: turns to the one who has the word, looks round now and then, looks out over the wall.</summary>
    void Tend(Unit u, float dt)
    {
        switch (u.Pastime)
        {
            case Pastime.Talk when u.Group == null:
                if (u.Mate != null) u.Facing = MathF.Atan2(u.Mate.Z - u.Z, u.Mate.X - u.X);
                break;
            case Pastime.Talk:
            case Pastime.Dice:
                var g = u.Group;
                if (Time >= g.TurnUntil)
                {
                    // the word (the dice) passes to another of the ring
                    g.Turn = (g.Turn + 1 + Dice.Next(Math.Max(1, g.Members.Count - 1))) % g.Members.Count;
                    g.TurnAt = Time;
                    g.TurnUntil = Time + (g.Does == Pastime.Dice ? 2.2 + Dice.NextDouble() * 1.5 : 2.5 + Dice.NextDouble() * 3.5);
                    Chatter(g);
                }
                var speaker = g.Speaker;
                float tx = speaker == u || speaker == null || g.Does == Pastime.Dice ? g.X : speaker.X, tz = speaker == u || speaker == null || g.Does == Pastime.Dice ? g.Z : speaker.Z;
                if (MathF.Abs(tx - u.X) + MathF.Abs(tz - u.Z) > .5f) u.Facing = MathF.Atan2(tz - u.Z, tx - u.X);
                break;
            case Pastime.Look:
                if (Dice.NextDouble() < dt * .7) u.Facing += (float)(Dice.NextDouble() - .5) * 2.4f;
                break;
            case Pastime.Watch:
                u.Facing = u.Gaze + MathF.Sin((float)Time * .45f + u.Id) * .55f;
                break;
            case Pastime.Warm:
            case Pastime.Sell or Pastime.Hammer or Pastime.Saw or Pastime.Read or Pastime.Fetch or Pastime.Spar:
            case Pastime.Fish or Pastime.Tend or Pastime.Kneel:
                u.Facing = u.Gaze;
                break;
            case Pastime.Sweep:
                // along the doorstep and back
                u.Facing = u.Gaze + MathF.Sin((float)Time * .5f + u.Id) * .5f;
                break;
            case Pastime.Shop:
                // looking over the goods from one end of the counter to the other
                u.Facing = u.Gaze + MathF.Sin((float)Time * .6f + u.Id) * .35f;
                break;
        }
    }

    /// <summary>Picks the next way to pass the time.</summary>
    void Choose(Unit u, bool stay)
    {
        bool soldier = u.T.Work < 1;
        // where it idles round: a soldier's post, or where a worker ran out of things to do
        if (u.Home < 0 || CellsApart(u.Home, u.Cell) > 6) u.Home = u.Cell;
        int anchor = soldier && u.Post >= 0 ? u.Post : u.Home;
        if (soldier && Raids.Active)
        {
            u.Gaze = Westward;
            Pass(u, Dice.NextDouble() < .75 ? Pastime.Watch : Pastime.Look, 3 + Dice.NextDouble() * 4);
            return;
        }
        // the night: to bed, every other soldier keeping the watch
        if (Bedtime && Sleeper(u) && !QuietLife)
        {
            if ((!soldier || u.Id % 2 == 0) && TrySleep(u, stay)) return;
            if (soldier) { u.Gaze = Westward; Pass(u, Dice.NextDouble() < .7 ? Pastime.Watch : Pastime.Look, 4 + Dice.NextDouble() * 5); return; }
        }
        // noon: something to eat, once a day (one hurt eats where it is; one just out of work, which may be fed more
        // in a moment, not yet)
        if (Noon && !QuietLife && u.AteDay != Clock.Day && Sleeper(u) && Time - u.BusyAt > 20 && TryEat(u, stay || !Hale(u))) return;
        // (in the town's way, out of the way of work marked about it: a ring walled in by a tower going up round it)
        if (!QuietLife && !stay && TryClear(u)) return;
        double r = Dice.NextDouble();
        // of an evening the fires and the benches draw them, the campfires from all about (Sim.Yard.cs)
        if (Clock.Darkness > .3f && r < .55 && !stay && TryWarm(u, gather: true)) return;
        // by day the street: a stall at the market, the smith's anvil, the hall's board, a well (Sim.Streets.cs)
        if (r < .45 && !stay && TryStreet(u)) return;
        if (r < .22 && (QuietLife ? TryPair(u) : TryTalk(u))) return;
        // the town's own pastimes, each where there is a place for it: dice, a whetstone, fishing, a broom, the flower
        // beds, the sanctuary
        // (not one hurt, nor one just out of work, which may be fed more in a moment)
        if (r < .5 && !stay && !QuietLife && Time >= u.LifeAgain && Sleeper(u) && Hale(u) && Time - u.BusyAt > 20 && TryTownPastime(u, soldier)) return;
        if (r < .34 && !stay && TryWarm(u)) return;
        if (r < .5 && !stay && TrySit(u)) return;
        if (r < .62 && !stay && TryStroll(u, anchor, soldier ? 2 : 4)) return;
        if (r < .74 && (QuietLife || !ByAFront(u.X, u.Z))) { Pass(u, Pastime.Sit, 6 + Dice.NextDouble() * 9); return; }
        if (r < .82) { Pass(u, Pastime.Stretch, 2.2); return; }
        if (soldier && r < .92) { u.Gaze = Westward; Pass(u, Pastime.Watch, 4 + Dice.NextDouble() * 5); return; }
        Pass(u, Pastime.Look, 2.5 + Dice.NextDouble() * 4);
    }

    /// <summary>
    /// Whether a spot is before a working building's door, within 4 m: its trade's things are set out there and its street
    /// is kept there, so nobody sits down, squats or lies down there to pass the time.
    /// </summary>
    bool ByAFront(float x, float z)
    {
        foreach (var b in Buildings)
            if (b.Ready && FrontOf(b, out int fx, out _, out int fz, out _, out _) && (fx - x) * (fx - x) + (fz - z) * (fz - z) < 32 * 32) return true;
        return false;
    }

    /// <summary>Whether there is work marked in a rectangle of columns (voxels): nobody lies down to sleep where it is to be done.</summary>
    bool WorkAbout(int x0, int z0, int x1, int z1)
    {
        if (Orders.Active.Count == 0) return false;
        TownMarksFresh();
        int i0 = Math.Max(0, x0 >> NavGrid.Shift), i1 = Math.Min(Nav.NX - 1, x1 >> NavGrid.Shift), k0 = Math.Max(0, z0 >> NavGrid.Shift), k1 = Math.Min(Nav.NZ - 1, z1 >> NavGrid.Shift);
        for (int k = k0; k <= k1; k++)
            for (int i = i0; i <= i1; i++)
                if (Orders.Active.Contains(k * Nav.NX + i) && !_townMarks.Contains(k * Nav.NX + i)) return true;
        return false;
    }

    // the work columns of what the town set out by itself and is still going up (Sim.TownDecor.cs), looked at again every
    // few seconds: it keeps nobody from bed, a meal or a pastime (a bench set out of an evening by a house left its bunks
    // empty the night long, the workers lying down on the ground)
    readonly HashSet<int> _townMarks = new();
    double _townMarksAt = -1;

    void TownMarksFresh()
    {
        if (Time < _townMarksAt) return;
        _townMarksAt = Time + 3;
        _townMarks.Clear();
        foreach (var s in Yards)
            if (s.ByTown && !s.Ready && s.Voxels != null)
                for (int i = 0; i < s.Voxels.Count; i++) _townMarks.Add((s.Voxels.Z(i) >> NavGrid.Shift) * Nav.NX + (s.Voxels.X(i) >> NavGrid.Shift));
    }

    /// <summary>Whole enough to go about its pastimes: not bleeding, not lame, not to be splinted or mended, over half its health.</summary>
    static bool Hale(Unit u) => !WantsCare(u) && !Lame(u) && !u.Mending && u.Hp >= u.MaxHp * .5f;

    /// <summary>One of ours who lives as a man on foot does: eats, sleeps, sits down to its own pastimes (not a rider, a hound, a giant, an engine).</summary>
    static bool Sleeper(Unit u) => !u.T.Mounted && !u.T.NoHands && u.T.Room <= 1 && !Kinds.IsEngine(u.T) && u.T != Kinds.TameTroll;

    int CellsApart(int a, int b) => Math.Max(Math.Abs(Nav.I(a) - Nav.I(b)), Math.Abs(Nav.K(a) - Nav.K(b)));

    /// <summary>A short walk to a free spot near where it idles, slowly.</summary>
    bool TryStroll(Unit u, int anchor, int radius)
    {
        int i0 = Nav.I(anchor), k0 = Nav.K(anchor), y0 = Nav.Y(anchor), h = u.T.Move.Height;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            int i = i0 + Dice.Next(-radius, radius + 1), k = k0 + Dice.Next(-radius, radius + 1);
            for (int dy = 2; dy >= -2; dy--)
            {
                int y = y0 + dy;
                if (!Nav.Stand(i, y, k, h)) continue;
                int c = Nav.Id(i, y, k);
                if (c == u.Cell || _bodies[c] > 0 || Nav.Water[c] > 0) break;
                var path = ShortWay(u, u.Cell, x => x == c, x => H(x, i, k), 600);
                if (path == null || path.Count > radius * 3) break;
                // nobody climbs a ladder or scaffolding for a stroll
                if (path.Exists(x => Nav.Ladder[x] != 0)) break;
                u.Path = path; u.PathI = 0;
                u.Pastime = Pastime.Stroll; u.After = Pastime.None; u.PastimeUntil = Time + 30;
                return true;
            }
        }
        return false;
    }

    /// <summary>One idle and free to stand in a ring with others: on its feet, at nothing that holds it.</summary>
    bool FreeToJoin(Unit v, Unit u)
    {
        if (v == u || v.Dead || v.Inside || v == Possessed || v.Team != Team.Player || v.State != UnitState.Idle || v.Mate != null) return false;
        if (v.Pastime is Pastime.Stroll or Pastime.Talk or Pastime.Watch or Pastime.Dice or Pastime.Sleep or Pastime.Pray or Pastime.Bound || v.Path != null && v.PathI < v.Path.Count) return false;
        return !v.Bound && v.Captor == null && Sleeper(v) && v.Spot == null && v.SeatH <= 0;
    }

    /// <summary>
    /// Talks with others idle close by: with one standing near, the two turning to each other, or as a third to two who
    /// talk already; the word passing round among them.
    /// </summary>
    bool TryTalk(Unit u)
    {
        Unit best = null;
        Huddle join = null;
        float bd = 26 * 26;
        foreach (var v in Units)
        {
            if (v == u || v.Dead || v.Inside || v.Team != Team.Player || MathF.Abs(v.Y - u.Y) >= 8) continue;
            float dx = v.X - u.X, dz = v.Z - u.Z, d = dx * dx + dz * dz;
            if (d >= bd) continue;
            // two talking already: the third makes a ring of three
            if (v.Group is { Does: Pastime.Talk } g && g.Members.Count < 3 && v.Pastime == Pastime.Talk && g.Until - Time > 4) { bd = d; best = v; join = g; continue; }
            if (!FreeToJoin(v, u)) continue;
            bd = d; best = v; join = null;
        }
        if (best == null || u.Group != null) return false;
        if (join != null)
        {
            Join(join, u);
            u.Pastime = Pastime.Talk; u.After = Pastime.None; u.PastimeUntil = join.Until;
            return true;
        }
        double t = 4 + Dice.NextDouble() * 6;
        var ring = Form(Pastime.Talk, t * (1.3 + Dice.NextDouble()), u, best);
        Pass(u, Pastime.Talk, ring.Until - Time);
        Pass(best, Pastime.Talk, ring.Until - Time);
        return true;
    }

    /// <summary>The quiet way's talk: with the nearest one idle and free, the two turning to each other for a while.</summary>
    bool TryPair(Unit u)
    {
        Unit best = null;
        float bd = 26 * 26;
        foreach (var v in Units)
        {
            if (v == u || v.Dead || v.Inside || v == Possessed || v.Team != Team.Player || v.State != UnitState.Idle || v.Mate != null) continue;
            if (v.Pastime is Pastime.Stroll or Pastime.Talk or Pastime.Watch || v.Path != null && v.PathI < v.Path.Count) continue;
            float dx = v.X - u.X, dz = v.Z - u.Z, d = dx * dx + dz * dz;
            if (d < bd && MathF.Abs(v.Y - u.Y) < 8) { bd = d; best = v; }
        }
        if (best == null) return false;
        double t = 4 + Rng.NextDouble() * 6;
        Pass(u, Pastime.Talk, t);
        Pass(best, Pastime.Talk, t);
        u.Mate = best; best.Mate = u;
        return true;
    }

    /// <summary>
    /// One with nothing to do standing within 3 m of work marked walks off 5 to 8 m to ground with none about it, so that
    /// it is not in the way, nor walled in by a tower or a house going up round it; false if it is clear, or there is no
    /// such ground to walk to.
    /// </summary>
    bool TryClear(Unit u)
    {
        if (!WorkAbout((int)u.X - 24, (int)u.Z - 24, (int)u.X + 24, (int)u.Z + 24)) return false;
        double a0 = Dice.NextDouble() * Math.Tau;
        for (int n = 0; n < 12; n++)
        {
            double a = a0 + n * Math.Tau / 12, r = 40 + n % 3 * 12;
            int x = (int)(u.X + Math.Cos(a) * r), z = (int)(u.Z + Math.Sin(a) * r);
            if (WorkAbout(x - 24, z - 24, x + 24, z + 24)) continue;
            int c = StandNear(x, (int)u.Y, z, u.T.Move.Height);
            if (c < 0 || c == u.Cell || _bodies[c] > 0 || Nav.Water[c] > 0) continue;
            int ci = Nav.I(c), ck = Nav.K(c);
            var path = ShortWay(u, u.Cell, q => q == c, q => H(q, ci, ck), 1500);
            if (path == null || path.Exists(q => Nav.Ladder[q] != 0)) continue;
            u.Path = path; u.PathI = 0;
            u.Pastime = Pastime.Stroll; u.After = Pastime.Look; u.PastimeUntil = Time + 30;
            return true;
        }
        return false;
    }

    /// <summary>A new ring of some of ours, for so long; its middle among them, and the word with the first.</summary>
    Huddle Form(Pastime does, double seconds, params Unit[] who)
    {
        var g = new Huddle { Does = does, Until = Time + seconds, TurnAt = Time, TurnUntil = Time + 1.5 + Dice.NextDouble() * 2 };
        foreach (var v in who) { Busy(v); g.Members.Add(v); v.Group = g; }
        Centre(g);
        Rings++;
        return g;
    }

    void Join(Huddle g, Unit u)
    {
        g.Members.Add(u);
        u.Group = g;
        Centre(g);
    }

    static void Centre(Huddle g)
    {
        float x = 0, z = 0;
        foreach (var m in g.Members) { x += m.X; z += m.Z; }
        g.X = x / Math.Max(1, g.Members.Count); g.Z = z / Math.Max(1, g.Members.Count);
    }

    /// <summary>Out of its ring, if it stood in one: a ring left with one stops talking.</summary>
    void LeaveGroup(Unit u)
    {
        var g = u.Group;
        if (g == null) return;
        u.Group = null;
        int at = g.Members.IndexOf(u);
        if (at < 0) return;
        g.Members.RemoveAt(at);
        if (g.Turn > at || g.Turn >= g.Members.Count) g.Turn = Math.Max(0, g.Turn - 1);
        if (g.Members.Count >= 2) { Centre(g); return; }
        foreach (var m in g.Members) { m.Group = null; m.PastimeUntil = 0; }
        g.Members.Clear();
    }

    /// <summary>
    /// A pastime of the town for one with nothing to do, by what there is about: a soldier plays at dice with others idle
    /// close by or sharpens its blade; a worker fishes from a bank, sweeps before a door, tends a flower bed; anyone kneels
    /// a while in the sanctuary. Not looked for again for a while if there is none.
    /// </summary>
    bool TryTownPastime(Unit u, bool soldier)
    {
        u.LifeAgain = Time + 8 + Dice.NextDouble() * 6;
        int start = Dice.Next(4);
        for (int n = 0; n < 4; n++)
            switch ((start + n) % 4)
            {
                case 0: if (soldier ? TryDice(u) : TryFish(u)) return true; break;
                case 1: if (soldier ? TrySharpen(u) : TrySweep(u)) return true; break;
                case 2: if (soldier ? TryDice(u) : TryTend(u)) return true; break;
                case 3: if (Dice.NextDouble() < .35 && TryKneel(u)) return true; break;
            }
        return false;
    }

    /// <summary>Dice with one or two others idle close by: they squat round a middle and throw in turn.</summary>
    bool TryDice(Unit u)
    {
        if (ByAFront(u.X, u.Z)) return false;
        var near = new List<(float d, Unit v)>();
        foreach (var v in Units)
        {
            if (!FreeToJoin(v, u) || MathF.Abs(v.Y - u.Y) >= 6) continue;
            float dx = v.X - u.X, dz = v.Z - u.Z, d = dx * dx + dz * dz;
            if (d < 24 * 24) near.Add((d, v));
        }
        if (near.Count == 0) return false;
        near.Sort((a, b) => a.d.CompareTo(b.d));
        double t = 15 + Dice.NextDouble() * 14;
        var g = near.Count >= 2 && Dice.NextDouble() < .6 ? Form(Pastime.Dice, t, u, near[0].v, near[1].v) : Form(Pastime.Dice, t, u, near[0].v);
        foreach (var m in g.Members) Pass(m, Pastime.Dice, t);
        Diced++;
        return true;
    }

    /// <summary>Sits down to its whetstone and goes over its blade: on a bench close by, or on the ground where it is.</summary>
    bool TrySharpen(Unit u)
    {
        if (!HasMainHand(u) || !Armed(u) || u.T.Ranged) return false;
        if (TrySit(u)) { Sharpened++; u.After = Pastime.Sharpen; return true; }
        if (ByAFront(u.X, u.Z)) return false;
        Sharpened++;
        Pass(u, Pastime.Sharpen, Lasts(u, Pastime.Sharpen));
        return true;
    }

    /// <summary>
    /// Goes to fish from a bank close by (within 30 m of where it idles): a dry place to stand with water before it, and
    /// stands there with its rod over the water.
    /// </summary>
    bool TryFish(Unit u)
    {
        if (!HasMainHand(u)) return false;
        Shores();
        int best = -1, toward = -1;
        float bd = 240 * 240;
        foreach (var (stand, water) in _shores)
        {
            var (x, _, z) = Nav.Feet(stand);
            float dx = x - u.X, dz = z - u.Z, d = dx * dx + dz * dz;
            if (d >= bd || _bodies[stand] > 0 || MathF.Abs(Nav.Floor[stand] - u.Y) > 40) continue;
            bd = d; best = stand; toward = water;
        }
        if (best < 0 || !GoAside(u, best, Pastime.Fish)) return false;
        var (sx, _, sz) = Nav.Feet(best);
        var (wx, _, wz) = Nav.Feet(toward);
        u.Gaze = MathF.Atan2(wz - sz, wx - sx);
        Fished++;
        return true;
    }

    /// <summary>
    /// Sweeps the ground before a door close by (within 30 m): a house's or a wall's, not a working building's, whose
    /// front is its trade's and its street's; on the outer side, off to one side of the doorway, the broom going toward it.
    /// </summary>
    bool TrySweep(Unit u)
    {
        if (!HasMainHand(u)) return false;
        Gateway best = null;
        float bd = 240 * 240;
        var fronts = new HashSet<Gateway>();
        foreach (var b in Buildings) if (DoorOf(b) is { } d) fronts.Add(d);
        foreach (var g in Gates)
        {
            if (g.Broken || fronts.Contains(g)) continue;
            var (gx, gy, gz) = g.Middle;
            float dx = gx - u.X, dz = gz - u.Z, d = dx * dx + dz * dz;
            if (d < bd && MathF.Abs(gy - u.Y) < 16) { bd = d; best = g; }
        }
        if (best == null) return false;
        var (mx, my, mz) = best.Middle;
        // a step and a half out of the doorway, on the side away from the citadel, and off to one side of it
        float side = (Dice.Next(2) * 2 - 1) * 12;
        float ox = best.AlongX ? side : -best.In * 13, oz = best.AlongX ? -best.In * 13 : side;
        int c = StandNear((int)(mx + ox), (int)my, (int)(mz + oz), u.T.Move.Height);
        if (c < 0 || _bodies[c] > 0 || !GoAside(u, c, Pastime.Sweep)) return false;
        var (sx, _, sz) = Nav.Feet(c);
        u.Gaze = MathF.Atan2(mz - sz, mx - sx);
        Swept++;
        return true;
    }

    /// <summary>Tends a flower bed close by (within 30 m): kneels at its edge, the hands in the earth.</summary>
    bool TryTend(Unit u)
    {
        if (!HasMainHand(u)) return false;
        DecorSite best = null;
        float bd = 240 * 240;
        foreach (var y in Yards)
        {
            if (!y.Ready || y.Kind != DecorKind.FlowerBed) continue;
            float dx = y.X - u.X, dz = y.Z - u.Z, d = dx * dx + dz * dz;
            if (d < bd && MathF.Abs(y.Y - u.Y) < 16) { bd = d; best = y; }
        }
        if (best == null) return false;
        // at the side of the bed nearest it, a pace out from the bed's middle
        float ax = u.X - best.X, az = u.Z - best.Z, al = MathF.Max(1, MathF.Sqrt(ax * ax + az * az));
        int c = StandNear((int)(best.X + ax / al * 11), best.Y, (int)(best.Z + az / al * 11), u.T.Move.Height);
        if (c < 0 || _bodies[c] > 0 || !GoAside(u, c, Pastime.Tend)) return false;
        var (sx, _, sz) = Nav.Feet(c);
        u.Gaze = MathF.Atan2(best.Z + .5f - sz, best.X + .5f - sx);
        Tended++;
        return true;
    }

    /// <summary>Kneels a while in the sanctuary close by (within 40 m), facing its middle, out of the priests' places.</summary>
    bool TryKneel(Unit u)
    {
        Zone best = null;
        float bd = 320 * 320;
        foreach (var z in Zones)
        {
            if (z.Kind != Zone.Sanctum) continue;
            float cx = (z.X0 + z.X1) / 2f, cz = (z.Z0 + z.Z1) / 2f, d = (cx - u.X) * (cx - u.X) + (cz - u.Z) * (cz - u.Z);
            if (d < bd) { bd = d; best = z; }
        }
        if (best == null) return false;
        var cells = new List<int>();
        foreach (int c in ShelterCells(best.X0, best.Z0, best.X1, best.Z1, u.T.Move.Height))
            if (_bodies[c] == 0 && Nav.Water[c] == 0) cells.Add(c);
        // a priest's place is a priest's: kept clear
        foreach (var v in Units) if (!v.Dead && v.T.Prays && v.Shrine >= 0) cells.Remove(v.Shrine);
        if (cells.Count == 0) return false;
        int at = cells[Dice.Next(cells.Count)];
        if (!GoAside(u, at, Pastime.Kneel)) return false;
        var (sx, _, sz) = Nav.Feet(at);
        var power = PowerUnder(best);
        float fx = power != null ? power.Place.X : (best.X0 + best.X1) / 2f, fz = power != null ? power.Place.Z : (best.Z0 + best.Z1) / 2f;
        u.Gaze = MathF.Abs(fx - sx) + MathF.Abs(fz - sz) > 4 ? MathF.Atan2(fz - sz, fx - sx) : u.Facing;
        Knelt++;
        return true;
    }

    /// <summary>Strolls to a cell to take up a pastime there, if there is a way not up a ladder; false if not.</summary>
    bool GoAside(Unit u, int c, Pastime does)
    {
        int ci = Nav.I(c), ck = Nav.K(c);
        var path = c == u.Cell ? new List<int>() : ShortWay(u, u.Cell, x => x == c, x => H(x, ci, ck), 4000);
        if (path == null || path.Exists(x => Nav.Ladder[x] != 0)) return false;
        Busy(u);
        u.Path = path.Count > 0 ? path : null; u.PathI = 0;
        u.Pastime = Pastime.Stroll; u.After = does; u.PastimeUntil = Time + 60;
        return true;
    }

    // ------------------------------------------------------------ the banks

    /// <summary>Places on the banks to fish from: a dry cell to stand in and the cell of water before it, found again every two minutes.</summary>
    readonly List<(int stand, int water)> _shores = new();
    double _shoresAt = -999;

    void Shores()
    {
        if (Time - _shoresAt < 120) return;
        _shoresAt = Time;
        _shores.Clear();
        int h = Kinds.Worker.Move.Height;
        for (int k = 1; k < Nav.NZ - 1; k++)
            for (int i = 1; i < Nav.NX - 1; i++)
                for (int y = 1; y < Nav.NY - h; y++)
                {
                    int c = Nav.Id(i, y, k);
                    if (Nav.Water[c] < NavGrid.DeepT || y + 1 >= Nav.NY || Nav.Water[c + Nav.Layer] > 0) continue;
                    // the top of water a body would drown in: a dry place beside it, level or a step up
                    for (int d = 0; d < 4; d++)
                    {
                        int ni = i + DX8[d], nk = k + DK8[d];
                        for (int dy = 0; dy <= 1; dy++)
                        {
                            if (!Nav.Stand(ni, y + dy, nk, h)) continue;
                            int s = Nav.Id(ni, y + dy, nk);
                            if (Nav.Water[s] == 0) _shores.Add((s, c));
                            break;
                        }
                    }
                    break;
                }
    }

    // ------------------------------------------------------------ the day: a meal at noon, the night in bed

    /// <summary>Eats something at noon: on a bench close by, by a fire, or where it stands.</summary>
    bool TryEat(Unit u, bool stay)
    {
        u.AteDay = Clock.Day;
        Meals++;
        if (!stay && TrySit(u)) { u.After = Pastime.Eat; return true; }
        if (!stay && TryWarm(u)) { u.After = Pastime.Eat; return true; }
        // (standing before a working building's door: a bite where it stands, not sitting down to it)
        Pass(u, ByAFront(u.X, u.Z) ? Pastime.Look : Pastime.Eat, Lasts(u, Pastime.Eat));
        return true;
    }

    /// <summary>Seconds of the night left, and a little more or less: they do not all get up at once.</summary>
    double SleepLeft() => Math.Max(4, DayClock.NightLength - Clock.NightT) + Dice.NextDouble() * 8;

    /// <summary>
    /// Goes to sleep: to a free bunk of a closed dwelling within 80 m, else to the ground by a campfire that burns within
    /// 40 m, else lies down where it stands (sent somewhere, it stays there).
    /// </summary>
    bool TrySleep(Unit u, bool stay)
    {
        if (!stay)
        {
            Berth bed = null;
            int at = -1;
            float bd = 640 * 640;
            foreach (var z in Zones)
            {
                // (not a house with work marked in it or about it: being built on, raised a storey, mended)
                if (z.Kind != Zone.Dwelling || !z.Closed || _noBunks || WorkAbout(z.X0 - 16, z.Z0 - 16, z.X1 + 16, z.Z1 + 16)) continue;
                foreach (int key in z.BunksWhole)
                {
                    if (_bunkBy.TryGetValue(key, out var by) && by != u && !by.Dead && by.Berth?.Key == key) continue;
                    var b = BunkBerth(key);
                    if (b == null) continue;
                    float d = (b.X - u.X) * (b.X - u.X) + (b.Z - u.Z) * (b.Z - u.Z);
                    if (d >= bd) continue;
                    int c = StandNear((int)b.X, (int)b.Y - 2, (int)b.Z, u.T.Move.Height);
                    if (c < 0) continue;
                    var (fx, _, fz) = Nav.Feet(c);
                    if (!z.Contains((int)fx, (int)fz)) continue;
                    bd = d; bed = b; at = c;
                }
            }
            if (bed != null && GoAside(u, at, Pastime.Sleep))
            {
                u.Berth = bed;
                _bunkBy[bed.Key] = u;
                SleptInBunks++;
                return true;
            }
            // by a campfire that burns
            foreach (var s in Yards)
            {
                if (s.Kind != DecorKind.Campfire || !s.Ready || s.Light == null || s.Light.DarkUntil > Time) continue;
                float dx = s.Light.LX - u.X, dz = s.Light.LZ - u.Z;
                if (dx * dx + dz * dz > 320 * 320) continue;
                double a = Dice.NextDouble() * Math.PI * 2;
                int c = NearestStand((int)(s.Light.LX + Math.Cos(a) * 19), (int)(s.Light.LZ + Math.Sin(a) * 19), u.T.Move.Height);
                if (c < 0 || _bodies[c] > 0 || MathF.Abs(Nav.Floor[c] - s.Light.LY) > 16) continue;
                var (cx0, _, cz0) = Nav.Feet(c);
                if (WorkAbout((int)cx0 - 16, (int)cz0 - 16, (int)cx0 + 16, (int)cz0 + 16) || !GoAside(u, c, Pastime.Sleep)) continue;
                var (fx, fy, fz) = Nav.Feet(c);
                // the feet to the fire
                u.Berth = new Berth { X = fx, Z = fz, Y = fy, Facing = MathF.Atan2(s.Light.LZ - fz, s.Light.LX - fx) };
                SleptOut++;
                return true;
            }
        }
        // where it stands (not where work is marked about it: it stays up)
        if (WorkAbout((int)u.X - 16, (int)u.Z - 16, (int)u.X + 16, (int)u.Z + 16)) return false;
        Busy(u);
        u.Berth = new Berth { X = u.X, Z = u.Z, Y = Nav.Floor[u.Cell], Facing = u.Facing };
        Pass(u, Pastime.Sleep, SleepLeft());
        LieDown(u);
        SleptOut++;
        return true;
    }

    /// <summary>
    /// What a bunk is to lie in, from its voxels: its bedding found from its first post, the way it lies, the end the
    /// bolster is at; its feet a little in from the other end. Null if its bedding is gone.
    /// </summary>
    Berth BunkBerth(int key)
    {
        if (_bunks.TryGetValue(key, out var b))
        {
            if (b != null && World.InBounds((int)b.X, (int)b.Y, (int)b.Z)
                && World.Get((int)(b.X - MathF.Cos(b.Facing) * 4), (int)b.Y - 1, (int)(b.Z - MathF.Sin(b.Facing) * 4)) == Mat.Bedding) return b;
            _bunks.Remove(key);
        }
        var (x0, y0, z0) = WaterPlanner.Unpack(key);
        // the bedding nearest the post, and all of it that hangs together
        int seed = -1;
        float sd = float.MaxValue;
        for (int y = y0; y <= y0 + 7; y++)
            for (int z = z0 - 2; z <= z0 + 2; z++)
                for (int x = x0 - 2; x <= x0 + 2; x++)
                    if (World.InBounds(x, y, z) && World.Get(x, y, z) == Mat.Bedding)
                    {
                        float d = (x - x0) * (x - x0) + (z - z0) * (z - z0) + (y - y0) * .1f;
                        if (d < sd) { sd = d; seed = WaterPlanner.Pack(x, y, z); }
                    }
        // (a post standing at a corner: the bedding starts a voxel in from it, across the frame)
        if (seed < 0)
            for (int y = y0; y <= y0 + 7 && seed < 0; y++)
                for (int z = z0 - 3; z <= z0 + 3 && seed < 0; z++)
                    for (int x = x0 - 3; x <= x0 + 3 && seed < 0; x++)
                        if (World.InBounds(x, y, z) && World.Get(x, y, z) == Mat.Bedding) seed = WaterPlanner.Pack(x, y, z);
        if (seed < 0) { _bunks[key] = null; return null; }
        var seen = new HashSet<int> { seed };
        var todo = new Stack<int>();
        todo.Push(seed);
        int lx = int.MaxValue, hx = int.MinValue, lz = int.MaxValue, hz = int.MinValue, low = int.MaxValue, high = int.MinValue;
        var tops = new List<(int x, int y, int z)>();
        while (todo.Count > 0 && seen.Count < 400)
        {
            var (x, y, z) = WaterPlanner.Unpack(todo.Pop());
            lx = Math.Min(lx, x); hx = Math.Max(hx, x); lz = Math.Min(lz, z); hz = Math.Max(hz, z);
            low = Math.Min(low, y); high = Math.Max(high, y);
            tops.Add((x, y, z));
            foreach (var (dx, dy, dz) in Around6)
            {
                int nx = x + dx, ny = y + dy, nz = z + dz;
                if (!World.InBounds(nx, ny, nz) || World.Get(nx, ny, nz) != Mat.Bedding) continue;
                if (seen.Add(WaterPlanner.Pack(nx, ny, nz))) todo.Push(WaterPlanner.Pack(nx, ny, nz));
            }
        }
        if (hx - lx < 3 || hz - lz < 3) { _bunks[key] = null; return null; }
        bool alongX = hx - lx > hz - lz;
        // the bolster: the bedding standing a voxel over the rest, at the head
        float bolster = 0; int n = 0;
        foreach (var (x, y, z) in tops) if (y > low) { bolster += alongX ? x : z; n++; }
        float mid = alongX ? (lx + hx) / 2f : (lz + hz) / 2f;
        bool headLow = n > 0 ? bolster / n < mid : true;
        float fx, fz, face;
        if (alongX) { fx = headLow ? hx - 1f : lx + 2f; fz = (lz + hz + 1) / 2f; face = headLow ? 0 : MathF.PI; }
        else { fz = headLow ? hz - 1f : lz + 2f; fx = (lx + hx + 1) / 2f; face = headLow ? MathF.PI / 2 : -MathF.PI / 2; }
        b = new Berth { Key = key, X = fx, Z = fz, Facing = face, Y = low + 1 };
        _bunks[key] = b;
        return b;
    }

    static readonly (int, int, int)[] Around6 = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

    /// <summary>Lies down in its berth: in the bunk, its feet at the foot end, or on the ground where it chose.</summary>
    void LieDown(Unit u)
    {
        var b = u.Berth;
        if (b == null) return;
        u.X = b.X; u.Z = b.Z; u.Facing = b.Facing;
        u.SeatH = Math.Max(0, b.Y - u.Y);
        u.Why = "спит";
    }

    /// <summary>Up out of its berth, with a stretch.</summary>
    void Wake(Unit u)
    {
        StandUp(u);
        u.Why = "";
        Pass(u, Pastime.Stretch, 2.2);
    }

    /// <summary>Sends one of ours to bed now, whatever the hour (for the scenes staged and the tests); true if it found a bunk or a place by a fire.</summary>
    public bool ToBed(Unit u, bool bunk = true)
    {
        Busy(u);
        _noBunks = !bunk;
        try { return TrySleep(u, stay: false) && u.Berth is { } b && (b.Key >= 0 || u.After == Pastime.Sleep); }
        finally { _noBunks = false; }
    }

    /// <summary>For the scenes staged: the bunks passed over, to sleep by the fire.</summary>
    bool _noBunks;

    /// <summary>Puts one of ours sent to bed in its berth at once, as if it had walked there (for the scenes staged).</summary>
    public void InBed(Unit u)
    {
        if (u.Berth == null) return;
        if (u.Path is { Count: > 0 } p) { u.Cell = p[^1]; (u.X, u.Y, u.Z) = Nav.Feet(u.Cell); }
        u.Path = null;
        Pass(u, Pastime.Sleep, 1e6);
        LieDown(u);
    }

    /// <summary>Takes up a pastime of the town now, for the scenes staged and the tests: true if there was a place for it.</summary>
    public bool TakeUp(Unit u, Pastime does)
    {
        Busy(u);
        u.LifeAgain = 0;
        // (the banks found again: water may have been laid since)
        if (does == Pastime.Fish) _shoresAt = -999;
        return does switch
        {
            Pastime.Talk => TryTalk(u),
            Pastime.Dice => TryDice(u),
            Pastime.Sharpen => TrySharpen(u),
            Pastime.Fish => TryFish(u),
            Pastime.Sweep => TrySweep(u),
            Pastime.Tend => TryTend(u),
            Pastime.Kneel => TryKneel(u),
            Pastime.Eat => TryEat(u, false),
            _ => false,
        };
    }

    /// <summary>
    /// Goes to stand by a brazier close by and warms its hands; to <paramref name="gather"/>, of an evening, it goes as
    /// far as 40 m to the nearest campfire, where the others gather.
    /// </summary>
    bool TryWarm(Unit u, bool gather = false)
    {
        IEnumerable<(float x, float y, float z)> fires = Hearths();
        int reach = 64;
        if (gather)
        {
            var camp = new List<(float x, float y, float z)>();
            foreach (var s in Yards)
                if (s.Kind == DecorKind.Campfire && s.Ready && s.Light != null && s.Light.DarkUntil <= Time) camp.Add((s.Light.LX, s.Light.LY, s.Light.LZ));
            camp.Sort((a, b) => ((a.x - u.X) * (a.x - u.X) + (a.z - u.Z) * (a.z - u.Z)).CompareTo((b.x - u.X) * (b.x - u.X) + (b.z - u.Z) * (b.z - u.Z)));
            if (camp.Count > 0) { fires = camp; reach = 320; }
        }
        foreach (var (fx, fy, fz) in fires)
        {
            float dx = fx - u.X, dz = fz - u.Z;
            if (dx * dx + dz * dz > reach * reach || MathF.Abs(fy - u.Y) > (reach > 64 ? 40 : 12)) continue;
            double a = Dice.NextDouble() * Math.PI * 2;
            int c = NearestStand((int)(fx + Math.Cos(a) * 13), (int)(fz + Math.Sin(a) * 13), u.T.Move.Height);
            if (c < 0 || c != u.Cell && _bodies[c] > 0) continue;
            int ci = Nav.I(c), ck = Nav.K(c);
            var path = c == u.Cell ? new List<int>() : ShortWay(u, u.Cell, x => x == c, x => H(x, ci, ck), reach > 64 ? 6000 : 1500);
            if (path == null) continue;
            var (cx, _, cz) = Nav.Feet(c);
            u.Path = path.Count > 0 ? path : null; u.PathI = 0;
            u.Pastime = Pastime.Stroll; u.After = Pastime.Warm; u.PastimeUntil = Time + 40;
            u.Gaze = MathF.Atan2(fz - cz, fx - cx);
            return true;
        }
        return false;
    }
}
