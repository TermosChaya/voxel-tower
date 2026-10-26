using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A place in the town where one of ours passes the time at something: behind a market stall or before it, at the
/// smith's anvil, the carpenter's trestles, the mason's banker, a dummy on the drill yard, the hall's board, a well. Where
/// one stands, the way one faces there, what one does, and who is there now.
/// </summary>
public sealed class StreetSpot
{
    public Pastime Does;
    /// <summary>The building whose trade it is by, or null for a well.</summary>
    public Building Of;
    public DecorSite Well;
    public float X, Y, Z, Facing;
    public Unit By;
    /// <summary>Where it is and what is done there, the same each time it is found again.</summary>
    public long Key;
}

/// <summary>
/// The street. By day the town's working buildings and its wells draw those of ours with nothing to do: a worker goes to
/// keep a market stall or to stand before one choosing, to work the anvil outside the forge, the log on the carpenter's
/// trestles or the block on the mason's banker, to read what is pinned on the hall's board, to draw water at a well; a
/// soldier goes to strike at a dummy on the drill yard. Of an evening they gather at the campfires instead (Sim.Life.cs).
/// It is only the look of a town at work, no trade and no wage: work, an order, the alarm, the horde in the valley or
/// the dusk ends it at once. The places are found again every few seconds from the frontage that stands (Sim.Frontage.cs)
/// and the wells set out (Sim.Yard.cs); nothing of it is kept in the save.
/// </summary>
public sealed partial class Sim
{
    public readonly List<StreetSpot> Street = new();
    double _nextStreet;

    /// <summary>How far from where it idles one goes to the street, voxels (50 m).</summary>
    const int StreetReach = 400;

    /// <summary>The street is empty now: of an evening, at night, under the alarm, with the horde in the valley.</summary>
    bool StreetClosed => Clock.Darkness > .3f || Alarm || Raids.Active;

    /// <summary>Every few seconds: the places found again, those still there kept with whoever is at them.</summary>
    void TickStreet()
    {
        if (Time < _nextStreet) return;
        _nextStreet = Time + 4;
        var fresh = new List<StreetSpot>();
        foreach (var b in Buildings) if (b.Ready) StandsOf(b, fresh);
        foreach (var y in Yards)
        {
            if (!y.Ready || y.Kind != DecorKind.Well) continue;
            // before the well and behind it, between the posts of its windlass, facing it
            foreach (int side in new[] { 1, -1 })
            {
                var (tx, tz) = Yard.Turn(0f, 8.5f * side, y.Rot);
                fresh.Add(Spot(Pastime.Fetch, null, y, y.X + .5f + tx, y.Y, y.Z + .5f + tz, -tx, -tz));
            }
        }
        var old = new Dictionary<long, StreetSpot>();
        foreach (var s in Street) old[s.Key] = s;
        Street.Clear();
        foreach (var s in fresh) Street.Add(old.TryGetValue(s.Key, out var was) ? was : s);
    }

    /// <summary>A place at (x, y, z) facing along (fx, fz).</summary>
    static StreetSpot Spot(Pastime does, Building of, DecorSite well, float x, int y, float z, float fx, float fz) => new()
    {
        Does = does, Of = of, Well = well, X = x, Y = y, Z = z, Facing = MathF.Atan2(fz, fx),
        Key = (long)WaterPlanner.Pack((int)x, y, (int)z) << 8 | (byte)does,
    };

    /// <summary>
    /// The places before a working building: each piece of its frontage that has them standing as it was set out (all of
    /// its voxels where they were laid), and each of its places on the ground there.
    /// </summary>
    void StandsOf(Building b, List<StreetSpot> into)
    {
        if (!FrontOf(b, out int fx, out _, out int fz, out int nx, out int nz)) return;
        int ax = -nz, az = nx;
        int ua = nx != 0 ? (b.Z0 - fz) * az : (b.X0 - fx) * ax, ub = nx != 0 ? (b.Z1 - fz) * az : (b.X1 - fx) * ax;
        int umin = Math.Min(ua, ub), umax = Math.Max(ua, ub);
        foreach (var kit in KitsOf(b.Kind))
        {
            if (kit.Stands.Count == 0 || kit.Hung) continue;
            // moved along the front toward the door as it was set out (Sim.Frontage.cs SetOut)
            int klo = int.MaxValue, khi = int.MinValue;
            foreach (var ((u, _, _), _) in kit.V) { klo = Math.Min(klo, u); khi = Math.Max(khi, u); }
            int shift = khi > umax ? umax - khi : klo < umin ? umin - klo : 0;
            if (klo + shift < umin || khi + shift > umax) continue;
            (int x, int z) At(int u, int t) => (fx + ax * (u + shift) + nx * t, fz + az * (u + shift) + nz * t);
            // set out over the highest ground under it
            var low = new Dictionary<(int u, int t), int>();
            foreach (var ((u, y, t), _) in kit.V)
                if (!low.TryGetValue((u, t), out int was) || y < was) low[(u, t)] = y;
            int baseY = int.MinValue;
            foreach (var (c, _) in low)
            {
                var (x, z) = At(c.u, c.t);
                if (!World.InBounds(x, 0, z)) { baseY = int.MinValue; break; }
                baseY = Math.Max(baseY, FrontGround(x, z));
            }
            if (baseY == int.MinValue) continue;
            bool stands = true;
            foreach (var ((u, y, t), m) in kit.V)
            {
                var (x, z) = At(u, t);
                if (World.Get(x, baseY + y, z) != m) { stands = false; break; }
            }
            if (!stands) continue;
            foreach (var (su, st, du, dt, does) in kit.Stands)
            {
                float x = fx + ax * (su + shift) + nx * st + .5f, z = fz + az * (su + shift) + nz * st + .5f;
                into.Add(Spot(does, b, null, x, FrontGround((int)x, (int)z), z, ax * du + nx * dt, az * du + nz * dt));
            }
        }
    }

    /// <summary>Someone is at the place now (or on the way to it).</summary>
    static bool Taken(StreetSpot s) => s.By != null && !s.By.Dead && s.By.Spot == s
        && (s.By.Pastime == s.Does || s.By.Pastime == Pastime.Stroll && s.By.After == s.Does);

    /// <summary>Who may pass the time at what: a soldier who fights hand to hand at a dummy, anyone at the hall's board, a worker at the rest.</summary>
    static bool Suits(Unit u, Pastime does) => does switch
    {
        Pastime.Spar => u.T.Work < 1 && !u.T.Ranged && !u.T.Mounted && !u.T.NoHands && Armed(u),
        Pastime.Read => !u.T.NoHands && !u.T.Mounted,
        _ => u.T.Work >= 1 && HasMainHand(u),
    };

    /// <summary>Goes to a free place in the street, one of the nearest few, if the street is open and there is a way there.</summary>
    bool TryStreet(Unit u)
    {
        if (Street.Count == 0 || StreetClosed || Time < u.StreetAgain || u.Inside) return false;
        // (an attempt is not made again at once, found or not: a search across the town is no small thing)
        u.StreetAgain = Time + 12;
        var near = new List<(float d, StreetSpot s)>();
        foreach (var s in Street)
        {
            if (Taken(s) || !Suits(u, s.Does)) continue;
            float dx = s.X - u.X, dz = s.Z - u.Z, d = dx * dx + dz * dz;
            if (d <= StreetReach * StreetReach) near.Add((d, s));
        }
        if (near.Count == 0) return false;
        near.Sort((a, b) => a.d.CompareTo(b.d));
        var spot = near[Rng.Next(Math.Min(3, near.Count))].s;
        int c = StandNear((int)spot.X, (int)spot.Y, (int)spot.Z, u.T.Move.Height);
        // (something built over the place since: none of it)
        if (c < 0 || CellsApart(c, Nav.CellOf((int)spot.X, (int)spot.Y, (int)spot.Z)) > 1) return false;
        int ci = Nav.I(c), ck = Nav.K(c);
        var path = c == u.Cell ? new List<int>() : Paths.Find(u.Cell, x => x == c, x => H(x, ci, ck), u.T.Move, 8000, gates: true);
        if (path == null || path.Exists(x => Nav.Ladder[x] != 0)) return false;
        Busy(u);
        u.Path = path.Count > 0 ? path : null; u.PathI = 0;
        u.Pastime = Pastime.Stroll; u.After = spot.Does; u.PastimeUntil = Time + 90;
        spot.By = u;
        u.Spot = spot;
        return true;
    }

    /// <summary>Come to its place: stands on it, facing the way one faces there, for a while by what it does there.</summary>
    void TakeSpot(Unit u)
    {
        var s = u.Spot;
        if (s == null) return;
        u.X = s.X; u.Z = s.Z;
        u.Facing = u.Gaze = s.Facing;
        double stay = s.Does switch
        {
            Pastime.Sell or Pastime.Hammer or Pastime.Saw => 30 + Rng.NextDouble() * 40,
            Pastime.Spar => 20 + Rng.NextDouble() * 25,
            Pastime.Shop or Pastime.Fetch => 8 + Rng.NextDouble() * 12,
            _ => 6 + Rng.NextDouble() * 8,
        };
        u.PastimeUntil = Time + stay;
    }

    /// <summary>Leaves its place in the street, if it had one.</summary>
    void LeaveSpot(Unit u)
    {
        if (u.Spot != null && u.Spot.By == u) u.Spot.By = null;
        u.Spot = null;
    }

    /// <summary>Off the street at once: the dusk is come, the alarm is up, the horde is in the valley, or its place is gone.</summary>
    void OffStreet(Unit u)
    {
        if (u.Spot == null) return;
        if (!StreetClosed && u.Spot.By == u && Street.Contains(u.Spot)) return;
        bool going = u.Pastime == Pastime.Stroll && u.After == u.Spot.Does;
        LeaveSpot(u);
        if (going) { u.Path = null; u.After = Pastime.None; }
        u.PastimeUntil = 0;
    }

    /// <summary>Sends one of ours, idle, to the street (for the scenes staged and the tests): to a place of what it does, the nearest free.</summary>
    public bool ToStreet(Unit u, Pastime does)
    {
        u.StreetAgain = 0;
        var keep = new List<StreetSpot>(Street);
        Street.RemoveAll(s => s.Does != does);
        try { return TryStreet(u); }
        finally { Street.Clear(); Street.AddRange(keep); }
    }

    /// <summary>Puts one of ours at its place in the street at once, as if it had walked there (for the scenes staged).</summary>
    public void AtStreet(Unit u)
    {
        if (u.Spot == null) return;
        u.Cell = StandNear((int)u.Spot.X, (int)u.Spot.Y, (int)u.Spot.Z, u.T.Move.Height);
        (u.X, u.Y, u.Z) = Nav.Feet(u.Cell);
        u.Path = null;
        u.Pastime = u.Spot.Does; u.After = Pastime.None;
        TakeSpot(u);
    }
}
