using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The town's beasts (Units.Tame.cs). Hens come to a dwelling as it is finished, three to a house: they peck about its
/// yard, scatter clucking from whoever walks into them and from a rider, run from the horde, and roost by the door at
/// night. A yard dog comes once there is a dwelling (a second once there are five): it trots after one of the workers,
/// lies down by the fire of an evening or by the citadel's door, and barks at the horde as it comes, and the third bark
/// at a raider near it raises the alarm if nobody has. Horses graze in the stables' paddock once the stables work, and
/// goats with them; from the horde they bolt for the stables. None of them is hunted, takes a place in the household or
/// climbs a ladder, and the horde cuts them down if they are in its way. New ones come only with no alarm up. They draw
/// their own dice (<see cref="_tame"/>), so the simulation's draws are what they were; the scenes staged for other things
/// go without them (the game turns <see cref="TownBeasts"/> on).
/// </summary>
public sealed partial class Sim
{
    /// <summary>Whether the town keeps beasts: the game's valleys do; the bench's scenes and the self-test's go without unless they ask.</summary>
    public bool TownBeasts;
    /// <summary>Barks at the horde, hens scattered, alarms the dog raised, since the start (the bench asks).</summary>
    public int Barks, Scattered, DogAlarms;
    /// <summary>Whether the yard dog raises the alarm at a raider near (the switch in the town's tab, on unless turned off); off, it only barks.</summary>
    public bool DogAlarm = true;
    /// <summary>How far off the yard dog barks at the horde, and how near a raider has to be for its third bark to raise the alarm, metres.</summary>
    public const int DogBarkAt = 40, DogAlarmAt = 25;

    Random _tameRng;
    Random _tame => _tameRng ??= new Random(Site.KeepX * 31 + Site.KeepZ * 7 + 5);
    double _nextTownBeasts;
    // whom each dog keeps with, and how often it has barked at whom
    readonly Dictionary<int, Unit> _follows = new();
    readonly Dictionary<int, (int at, int n)> _barkedAt = new();

    float TameRnd => (float)_tame.NextDouble();

    /// <summary>The town's beasts living now, of a kind (or all).</summary>
    public int TameCount(UnitType t = null)
    {
        int n = 0;
        foreach (var u in Units) if (!u.Dead && u.T.Tame && (t == null || u.T == t)) n++;
        return n;
    }

    /// <summary>
    /// Every eight seconds with no alarm: a hen more to a dwelling with fewer than three, the dog, horses and goats to the
    /// stables' paddock — one beast at a time, so they come in one by one as the town grows.
    /// </summary>
    void TendTownBeasts()
    {
        if (!TownBeasts || Time < _nextTownBeasts) return;
        _nextTownBeasts = Time + 8;
        if (Alarm || Over || NearAny(Site.KeepX, Site.KeepZ, Team.Horde, 90 * VoxelWorld.PerMetre)) return;
        // the hens: three to a dwelling, fourteen at most
        var houses = new List<Zone>();
        foreach (var z in Zones)
        {
            if (z.Kind != Zone.Dwelling || !z.Closed) continue;
            // (a house's floors and rooms are zones of their own: it is the one house; and a town building's own rooms are no dwelling)
            float mx = (z.X0 + z.X1) / 2f, mz = (z.Z0 + z.Z1) / 2f;
            if (Buildings.Exists(b => mx >= b.X0 && mx <= b.X1 && mz >= b.Z0 && mz <= b.Z1)) continue;
            if (!houses.Exists(h => mx >= h.X0 - 8 && mx <= h.X1 + 8 && mz >= h.Z0 - 8 && mz <= h.Z1 + 8)) houses.Add(z);
        }
        int hens = TameCount(Kinds.Hen);
        if (houses.Count > 0 && hens < Math.Min(14, houses.Count * 3))
        {
            Zone fewest = null; int least = int.MaxValue;
            foreach (var z in houses)
            {
                int here = 0;
                foreach (var u in Units)
                    if (!u.Dead && u.T == Kinds.Hen && u.Home >= 0 && Near(u.Home, (z.X0 + z.X1) / 2, (z.Z0 + z.Z1) / 2, Math.Max(z.X1 - z.X0, z.Z1 - z.Z0) + 16)) here++;
                if (here < least) { least = here; fewest = z; }
            }
            int yard = YardOf(fewest);
            if (yard >= 0) { var h = Spawn(Kinds.Hen, yard); h.Home = yard; return; }
        }
        // the dog: one with a dwelling or three workers, two with five dwellings
        int curs = TameCount(Kinds.Cur), wantCurs = houses.Count >= 5 ? 2 : houses.Count > 0 || Count(Kinds.Worker) >= 3 ? 1 : 0;
        if (Site.HasKeep && curs < wantCurs && Gate >= 0) { var d = Spawn(Kinds.Cur, Gate); d.Home = Gate; return; }
        // the stables' paddock: two horses and two goats
        foreach (var b in Buildings)
        {
            if (b.Kind != "stables" || !b.Ready) continue;
            int pen = Paddock(b);
            if (pen < 0) break;
            if (TameCount(Kinds.Horse) < 2) { var hs = Spawn(Kinds.Horse, pen); hs.Home = pen; return; }
            if (TameCount(Kinds.Goat) < 2) { var g = Spawn(Kinds.Goat, pen); g.Home = pen; return; }
            break;
        }
    }

    bool Near(int cell, float x, float z, float r)
    {
        var (cx, _, cz) = Nav.Feet(cell);
        return (cx - x) * (cx - x) + (cz - z) * (cz - z) < r * r;
    }

    /// <summary>The yard of a dwelling: ground outside its walls on the side toward the citadel, two metres off.</summary>
    int YardOf(Zone z)
    {
        float mx = (z.X0 + z.X1) / 2f, mz = (z.Z0 + z.Z1) / 2f, dx = Site.KeepX - mx, dz = Site.KeepZ - mz;
        int x = (int)mx, y = (int)mz;
        if (Math.Abs(dx) > Math.Abs(dz)) x = dx > 0 ? z.X1 + 16 : z.X0 - 16;
        else y = dz > 0 ? z.Z1 + 16 : z.Z0 - 16;
        int c = NearestStand(x, y, Kinds.Hen.Move.Height);
        return c >= 0 && Nav.Water[c] == 0 ? c : -1;
    }

    /// <summary>The middle of the stables' paddock (its rails among the frontage), else the ground before its door; -1 if neither.</summary>
    int Paddock(Building b)
    {
        foreach (var f in FrontageOf(b))
        {
            if (f.Kind != FixtureKind.Trade || f.Voxels.Count < 40) continue;
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue, rails = 0;
            foreach (var (x, _, z, m) in f.Voxels)
            {
                if (m != Mat.CrateEdge) continue;
                rails++;
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z);
            }
            if (rails < 40 || x1 - x0 < 12 || z1 - z0 < 12) continue;
            int c = NearestStand((x0 + x1) / 2, (z0 + z1) / 2, Kinds.Horse.Move.Height);
            if (c >= 0) return c;
        }
        return DoorStand(b);
    }

    // ------------------------------------------------------------ each tick

    /// <summary>One of the town's beasts, each tick: out of a fire, away from the horde, then its own ways.</summary>
    void TameTick(Unit u, float dt)
    {
        if (EscapeFire(u, dt)) return;
        if (u.T == Kinds.Cur) { DogTick(u, dt); return; }
        // the horde near: away from it, a hen anywhere, a horse or a goat toward its stables
        if (Nearest(u, Team.Horde, 14 * VoxelWorld.PerMetre) is { } raider && !raider.Bound && !raider.Envoy)
        {
            Bolt(u, raider.X, raider.Z, 10 * VoxelWorld.PerMetre, dt);
            return;
        }
        // finishing a scatter or a bolt
        if (u.State == UnitState.Flee && u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt); return; }
        if (u.State == UnitState.Flee) { u.State = UnitState.Idle; u.Path = null; }
        if (u.T == Kinds.Hen && Scatter(u, dt)) return;
        // at night the hens roost by their door and the paddock sleeps
        bool night = Clock.Darkness > .7f;
        if (u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt, u.T == Kinds.Hen ? .5f : .35f); return; }
        u.State = UnitState.Idle;
        u.Pastime = night ? Pastime.Sleep : Pastime.None;
        if (Time < u.NextSearch) return;
        u.NextSearch = Time + (night ? 20 : 4) + TameRnd * 7;
        int home = u.Home >= 0 ? u.Home : u.Cell;
        var (hx, hy, hz) = Nav.Feet(home);
        if (night) { if (CellsApart(u.Cell, home) > 2) GoTo(u, home, budget: 1500); return; }
        // now and then a cluck, a whinny, a bleat
        if (TameRnd < .25f) Speak(u, u.T == Kinds.Hen ? "Ко-ко!" : u.T == Kinds.Horse ? "И-го-го!" : "Ме-е!", u.T == Kinds.Hen ? Voice.Cluck : u.T == Kinds.Horse ? Voice.Neigh : Voice.Bleat);
        float roam = (u.T == Kinds.Hen ? 5 : 7) * VoxelWorld.PerMetre;
        int at = StandNear((int)(hx + (TameRnd * 2 - 1) * roam), (int)hy, (int)(hz + (TameRnd * 2 - 1) * roam), u.T.Move.Height);
        if (at >= 0 && at != u.Cell && !Nav.Deep(at) && !GoTo(u, at, budget: 1500)) u.Path = null;
    }

    /// <summary>
    /// A hen that someone walks into (within a metre and a half, a rider within three) runs off clucking a few metres the
    /// other way. True if she did.
    /// </summary>
    bool Scatter(Unit u, float dt)
    {
        Unit by = null;
        foreach (var o in Units)
        {
            if (o == u || o.Dead || o.Inside || o.T.Tame && o.T != Kinds.Cur || o.State != UnitState.Walk && !o.T.Mounted) continue;
            float r = (o.T.Mounted ? 3 : 1.5f) * VoxelWorld.PerMetre;
            if (MathF.Abs(o.X - u.X) > r || MathF.Abs(o.Z - u.Z) > r || Dist(u, o) > r) continue;
            by = o;
            break;
        }
        if (by == null) return false;
        Scattered++;
        if (TameRnd < .5f) Speak(u, "Ко-ко-ко!", Voice.Cluck);
        Bolt(u, by.X, by.Z, (2 + TameRnd * 2) * VoxelWorld.PerMetre, dt);
        return true;
    }

    /// <summary>Runs off so far away from a point (a horse or a goat toward its stables from the horde).</summary>
    void Bolt(Unit u, float fromX, float fromZ, float run, float dt)
    {
        if (u.State == UnitState.Flee && u.Path != null && u.PathI < u.Path.Count && Time < u.NextSearch) { Walk(u, dt); return; }
        u.State = UnitState.Flee;
        u.Pastime = Pastime.None;
        u.NextSearch = Time + 1.2;
        float ax = u.X - fromX, az = u.Z - fromZ, len = MathF.Max(.01f, MathF.Sqrt(ax * ax + az * az));
        var (fx, fz) = FleeTo(u, ax / len, az / len, run, (TameRnd - .5f) * 24, (TameRnd - .5f) * 24);
        int c = NearestStand((int)Math.Clamp(fx, 8, World.SX - 9), (int)Math.Clamp(fz, 8, World.SZ - 9), u.T.Move.Height);
        if (c < 0 || Nav.Deep(c) || !GoTo(u, c, budget: 2000)) u.Path = null;
        if (u.Path != null) Walk(u, dt);
    }

    /// <summary>
    /// The yard dog: barks at the horde within 40 m (and the third bark at a raider within 25 m raises the alarm if it is
    /// not up), keeping away from it; by day it trots after its worker, a few metres behind; of an evening and at night
    /// it lies by the nearest fire, or by the citadel's door.
    /// </summary>
    void DogTick(Unit u, float dt)
    {
        var raider = Nearest(u, Team.Horde, DogBarkAt * VoxelWorld.PerMetre);
        if (raider != null && !raider.Bound && !raider.Envoy && !raider.Inside)
        {
            u.Facing = MathF.Atan2(raider.Z - u.Z, raider.X - u.X);
            if (Time >= u.NextOrders)
            {
                u.NextOrders = Time + 1.1 + TameRnd * .8;
                Barks++;
                Speak(u, "Гав!", Voice.Bark);
                var was = _barkedAt.GetValueOrDefault(u.Id);
                int n = was.at == raider.Id ? was.n + 1 : 1;
                _barkedAt[u.Id] = (raider.Id, n);
                if (n >= 3 && DogAlarm && !Alarm && Dist(u, raider) < DogAlarmAt * VoxelWorld.PerMetre)
                {
                    DogAlarms++;
                    Say("Пёс лает на орду: тревога.", 2, u);
                    RaiseAlarm();
                }
            }
            // it keeps its distance, backing off toward the citadel
            if (Dist(u, raider) < 12 * VoxelWorld.PerMetre) Bolt(u, raider.X, raider.Z, 10 * VoxelWorld.PerMetre, dt);
            else { u.State = UnitState.Idle; u.Path = null; }
            return;
        }
        if (u.State == UnitState.Flee && u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt); return; }
        if (u.State == UnitState.Flee) { u.State = UnitState.Idle; u.Path = null; }
        bool evening = Clock.Darkness > .4f;
        if (evening)
        {
            // by the fire, lying down
            if (u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt, .6f); return; }
            u.State = UnitState.Idle;
            if (Time < u.NextSearch) return;
            u.NextSearch = Time + 10;
            int rest = FireToLieBy(u);
            if (rest >= 0 && CellsApart(u.Cell, rest) > 2) GoTo(u, rest, budget: 3000);
            else u.Pastime = Pastime.Sleep;
            return;
        }
        u.Pastime = Pastime.None;
        // by day: after its worker
        if (!_follows.TryGetValue(u.Id, out var who) || who.Dead || who.Inside || Time >= u.NextSearch && TameRnd < .1f)
        {
            who = null;
            float best = float.MaxValue;
            foreach (var o in Units)
            {
                if (o.Dead || o.Inside || o.T != Kinds.Worker) continue;
                float d = Dist(u, o) + (o.Id * 7919 % 97);
                if (d < best) { best = d; who = o; }
            }
            if (who != null) _follows[u.Id] = who; else _follows.Remove(u.Id);
        }
        if (who == null)
        {
            if (u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt, .5f); return; }
            u.State = UnitState.Idle;
            return;
        }
        float far = Dist(u, who);
        if (far < 4 * VoxelWorld.PerMetre) { u.Path = null; u.State = UnitState.Idle; u.Facing = MathF.Atan2(who.Z - u.Z, who.X - u.X); return; }
        if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
        {
            u.NextSearch = Time + 1.5 + TameRnd;
            int c = NearestStand((int)(who.X + (u.X - who.X) / far * 20), (int)(who.Z + (u.Z - who.Z) / far * 20), u.T.Move.Height);
            if (c < 0 || !GoTo(u, c, budget: 3000)) u.Path = null;
        }
        if (u.Path != null) Walk(u, dt, far > 12 * VoxelWorld.PerMetre ? 1 : .7f);
    }

    /// <summary>Where the dog lies of an evening: beside the nearest campfire burning, else by the citadel's door.</summary>
    int FireToLieBy(Unit u)
    {
        DecorSite best = null; float bd = 80 * VoxelWorld.PerMetre;
        foreach (var y in Yards)
        {
            if (!y.Ready || y.Kind != DecorKind.Campfire) continue;
            float d = MathF.Sqrt((y.X - u.X) * (y.X - u.X) + (y.Z - u.Z) * (y.Z - u.Z));
            if (d < bd) { bd = d; best = y; }
        }
        if (best != null) return NearestStand(best.X + 18, best.Z + 6, u.T.Move.Height);
        return Gate;
    }
}
