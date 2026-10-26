using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Pawns with nothing to do don't stand like posts. A worker strolls about where it ran out of work, looks round, sits
/// down, stretches, talks with whoever else is idle close by, warms its hands at a brazier, and by day goes into the
/// town's street (Sim.Streets.cs); a soldier does the same round its post and now and then looks out to the west, where
/// the horde comes from, and while the horde is in the valley does nothing but that. Of an evening they gather at the
/// campfires. Work, an order, the horn or an enemy ends it at once.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The way the horde comes from, as a facing: west.</summary>
    const float Westward = MathF.PI;

    /// <summary>Passes the time; <paramref name="stay"/> keeps it where it stands (sent there, or on guard).</summary>
    void Idle(Unit u, float dt, bool stay)
    {
        u.State = UnitState.Idle;
        // the street empties at dusk, at the alarm, when the horde comes, or when its place is gone (Sim.Streets.cs)
        OffStreet(u);
        if (u.Pastime == Pastime.Stroll)
        {
            if (!stay && u.Path != null && u.PathI < u.Path.Count && !Walk(u, dt, .45f)) return;
            u.Path = null;
            var next = u.After == Pastime.None ? Pastime.Look : u.After;
            Pass(u, next, next == Pastime.Warm ? 6 + Rng.NextDouble() * 8 : next == Pastime.Sit && u.SeatAt != null ? 10 + Rng.NextDouble() * 14 : 2 + Rng.NextDouble() * 3);
            if (next == Pastime.Sit) SitDown(u);
            else if (u.Spot != null && next == u.Spot.Does) TakeSpot(u);
            return;
        }
        if (u.Pastime == Pastime.Talk && (u.Mate == null || u.Mate.Dead || u.Mate.Mate != u)) u.PastimeUntil = 0;
        if (u.Pastime != Pastime.None && Time < u.PastimeUntil) { Tend(u, dt); return; }
        StandUp(u);
        Choose(u, stay);
    }

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
        if (u.Mate != null && u.Mate.Mate == u) { u.Mate.Mate = null; u.Mate.PastimeUntil = 0; }
        u.Mate = null;
        u.Pastime = u.After = Pastime.None;
        u.PastimeUntil = 0;
    }

    /// <summary>Keeps up a pastime: turns to the one it talks to, looks round now and then, looks out over the wall.</summary>
    void Tend(Unit u, float dt)
    {
        switch (u.Pastime)
        {
            case Pastime.Talk:
                u.Facing = MathF.Atan2(u.Mate.Z - u.Z, u.Mate.X - u.X);
                break;
            case Pastime.Look:
                if (Rng.NextDouble() < dt * .7) u.Facing += (float)(Rng.NextDouble() - .5) * 2.4f;
                break;
            case Pastime.Watch:
                u.Facing = u.Gaze + MathF.Sin((float)Time * .45f + u.Id) * .55f;
                break;
            case Pastime.Warm:
            case Pastime.Sell or Pastime.Hammer or Pastime.Saw or Pastime.Read or Pastime.Fetch or Pastime.Spar:
                u.Facing = u.Gaze;
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
            Pass(u, Rng.NextDouble() < .75 ? Pastime.Watch : Pastime.Look, 3 + Rng.NextDouble() * 4);
            return;
        }
        double r = Rng.NextDouble();
        // of an evening the fires and the benches draw them, the campfires from all about (Sim.Yard.cs)
        if (Clock.Darkness > .3f && r < .55 && !stay && TryWarm(u, gather: true)) return;
        // by day the street: a stall at the market, the smith's anvil, the hall's board, a well (Sim.Streets.cs)
        if (r < .45 && !stay && TryStreet(u)) return;
        if (r < .22 && TryTalk(u)) return;
        if (r < .34 && !stay && TryWarm(u)) return;
        if (r < .5 && !stay && TrySit(u)) return;
        if (r < .62 && !stay && TryStroll(u, anchor, soldier ? 2 : 4)) return;
        if (r < .74) { Pass(u, Pastime.Sit, 6 + Rng.NextDouble() * 9); return; }
        if (r < .82) { Pass(u, Pastime.Stretch, 2.2); return; }
        if (soldier && r < .92) { u.Gaze = Westward; Pass(u, Pastime.Watch, 4 + Rng.NextDouble() * 5); return; }
        Pass(u, Pastime.Look, 2.5 + Rng.NextDouble() * 4);
    }

    int CellsApart(int a, int b) => Math.Max(Math.Abs(Nav.I(a) - Nav.I(b)), Math.Abs(Nav.K(a) - Nav.K(b)));

    /// <summary>A short walk to a free spot near where it idles, slowly.</summary>
    bool TryStroll(Unit u, int anchor, int radius)
    {
        int i0 = Nav.I(anchor), k0 = Nav.K(anchor), y0 = Nav.Y(anchor), h = u.T.Move.Height;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            int i = i0 + Rng.Next(-radius, radius + 1), k = k0 + Rng.Next(-radius, radius + 1);
            for (int dy = 2; dy >= -2; dy--)
            {
                int y = y0 + dy;
                if (!Nav.Stand(i, y, k, h)) continue;
                int c = Nav.Id(i, y, k);
                if (c == u.Cell || _bodies[c] > 0 || Nav.Water[c] > 0) break;
                var path = Paths.Find(u.Cell, x => x == c, x => H(x, i, k), u.T.Move, 600, gates: true);
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

    /// <summary>Talks with another idle pawn standing close by: both turn to each other for a while.</summary>
    bool TryTalk(Unit u)
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
            double a = Rng.NextDouble() * Math.PI * 2;
            int c = NearestStand((int)(fx + Math.Cos(a) * 13), (int)(fz + Math.Sin(a) * 13), u.T.Move.Height);
            if (c < 0 || c != u.Cell && _bodies[c] > 0) continue;
            int ci = Nav.I(c), ck = Nav.K(c);
            var path = c == u.Cell ? new List<int>() : Paths.Find(u.Cell, x => x == c, x => H(x, ci, ck), u.T.Move, reach > 64 ? 6000 : 1500, gates: true);
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
