using System;

namespace VoxelTower.Core;

/// <summary>
/// The horde's alchemists. One goes with its band the goblins' way; with a mark within a throw of it, and none of ours
/// near enough to come to blows, it stops where it is and throws its flasks of acid at it, one every few seconds, high
/// over in an arc: into a knot of our people first, then at a gate of ours, then at the piece of our walls in its way to
/// the citadel, then at a ladder of ours. It carries eight; with them thrown it is a goblin with a knife like any other.
/// Our archers shoot an alchemist before anyone else but the sorcerers.
/// </summary>
public sealed partial class Sim
{
    /// <summary>How far an alchemist throws a flask; how near ours may come before it fights instead.</summary>
    public const float FlaskFar = 18 * VoxelWorld.PerMetre, FlaskClose = 5 * VoxelWorld.PerMetre;
    /// <summary>Flasks an alchemist comes with.</summary>
    public const int FlasksCarried = 8;

    /// <summary>An alchemist threw a flask.</summary>
    public event Action<Unit> Threw;

    /// <summary>An alchemist's throws, while it has flasks and a mark: true while it stands throwing, false to go on as any raider.</summary>
    bool AlchemistTick(Unit u, float dt)
    {
        if (u.Thrown >= FlasksCarried || Climbing(u) || u.CarveCell >= 0 || u.BuildCell >= 0 || Time < u.StaggerUntil) return false;
        // one of ours close: it fights for its life
        if (Nearest(u, Team.Player, FlaskClose) != null) return false;
        if (Time >= u.NextLook)
        {
            u.NextLook = Time + 1.5 + u.Id % 5 * .1;
            u.AimAt = FlaskMark(u, out var at);
            u.Target = at;
        }
        if (u.AimAt < 0) return false;
        var (ax, ay, az) = WaterPlanner.Unpack(u.AimAt);
        if (u.Target is { Dead: false } t) { ax = (int)t.X; ay = (int)(t.Y + t.T.BodyH * .5f); az = (int)t.Z; }
        else if (u.Target != null) { u.Target = null; u.AimAt = -1; return false; }
        float dx = ax + .5f - u.X, dz = az + .5f - u.Z, d = MathF.Sqrt(dx * dx + dz * dz);
        if (d > FlaskFar) return false;
        u.Path = null;
        u.State = UnitState.Siege;
        u.Facing = MathF.Atan2(dz, dx);
        if (u.Cooldown <= 0)
        {
            ThrowFlask(u, ax + .5f, ay + .5f, az + .5f, d, u.Target);
            u.Cooldown = u.T.AttackTime * (.85f + (float)Rng.NextDouble() * .3f) / PainPace(u);
        }
        return true;
    }

    /// <summary>
    /// What an alchemist throws at, within a throw and in sight: two or more of ours standing together (on a wall the more
    /// so), a gate of ours, the first of our walls on its line to the citadel (the top of it), a ladder of ours. The one it
    /// throws at (null for a place), and the place (packed voxel, −1 for none).
    /// </summary>
    int FlaskMark(Unit u, out Unit at)
    {
        at = null;
        // a knot of our people: the one with most of ours about it
        float best = 0;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Team != Team.Player || o.Airborne != null || Dist(u, o) > FlaskFar) continue;
            float knot = 0;
            foreach (var p in Units)
                if (p != o && !p.Dead && !p.Inside && p.Team == Team.Player && MathF.Abs(p.X - o.X) < 20 && MathF.Abs(p.Z - o.Z) < 20 && MathF.Abs(p.Y - o.Y) < 12) knot++;
            if (knot < 1) continue;
            // those up on a wall the more
            float score = knot + (o.Y - World.GroundTop((int)o.X, (int)o.Z) > 12 ? 1.5f : 0);
            if (score <= best || !Sight(u, o)) continue;
            best = score; at = o;
        }
        if (at != null) return WaterPlanner.Pack((int)at.X, (int)at.Y, (int)at.Z);
        int ux = (int)u.X, uz = (int)u.Z, r = (int)FlaskFar;
        // a gate of ours
        float gd = float.MaxValue; int gate = -1;
        for (int z = Math.Max(1, uz - r); z <= Math.Min(World.SZ - 2, uz + r); z += 3)
            for (int x = Math.Max(1, ux - r); x <= Math.Min(World.SX - 2, ux + r); x += 3)
            {
                float d = (x - u.X) * (x - u.X) + (z - u.Z) * (z - u.Z);
                if (d >= gd || d > FlaskFar * FlaskFar) continue;
                int top = World.SurfAt(x, z);
                byte m = World.Get(x, top - 1, z);
                if (m is not (Mat.Gate or Mat.GateShut)) continue;
                if (!Sees(u, x + .5f, top - 2, z + .5f)) continue;
                gd = d; gate = WaterPlanner.Pack(x, top - 4, z);
            }
        if (gate >= 0) return gate;
        // the first of our walls on its line to the citadel, on top of it
        float lx = Site.KeepX - u.X, lz = Site.KeepZ - u.Z, len = MathF.Sqrt(lx * lx + lz * lz);
        if (len > 1)
        {
            lx /= len; lz /= len;
            for (float s = 6; s < Math.Min(FlaskFar, len - Site.KeepR); s += 2)
            {
                int x = (int)(u.X + lx * s), z = (int)(u.Z + lz * s);
                if (!World.InBounds(x, 1, z)) continue;
                int top = World.SurfAt(x, z);
                byte m = World.Get(x, top - 1, z);
                if (!Mat.IsPlayer[m] || Mat.Passable[m] && !Mat.IsLadder[m] || top - World.GroundTop(x, z) < VoxelWorld.PerMetre) continue;
                return WaterPlanner.Pack(x, top, z);
            }
        }
        // a ladder of ours
        for (int z = Math.Max(1, uz - r); z <= Math.Min(World.SZ - 2, uz + r); z += 3)
            for (int x = Math.Max(1, ux - r); x <= Math.Min(World.SX - 2, ux + r); x += 3)
            {
                if ((x - u.X) * (x - u.X) + (z - u.Z) * (z - u.Z) > FlaskFar * FlaskFar) continue;
                int top = World.ColTop(x, z);
                if (World.Get(x, top - 1, z) == Mat.Ladder && Sees(u, x + .5f, top - 2, z + .5f)) return WaterPlanner.Pack(x, top - 3, z);
            }
        return -1;
    }

    /// <summary>
    /// A flask away, lobbed high: it comes down where it was thrown give or take a metre or two, the more the farther;
    /// at one of ours, it strikes about half the time and breaks on it, else on the ground beside.
    /// </summary>
    void ThrowFlask(Unit u, float tx, float ty, float tz, float d, Unit at)
    {
        u.Swing = 1;
        u.Thrown++;
        float spread = 1.5f + d / FlaskFar * 10;
        float a = (float)(Rng.NextDouble() * MathF.Tau), r = (float)Rng.NextDouble() * spread;
        Arrows.Add(new Arrow
        {
            X0 = u.X, Y0 = u.Y + u.T.BodyH * .9f, Z0 = u.Z,
            X1 = tx + MathF.Cos(a) * r, Y1 = at != null ? at.Y + at.T.BodyH * .55f : ty, Z1 = tz + MathF.Sin(a) * r,
            Dur = Math.Max(.35f, d / SpeedOf(Shot.Flask) * 1.2f), Target = at, From = u, Hits = at != null && Rng.NextDouble() < .5,
            Damage = u.T.Damage, Kind = Shot.Flask, Arc = ArcOf(Shot.Flask),
        });
        Threw?.Invoke(u);
    }
}
