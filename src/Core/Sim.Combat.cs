using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>An arrow in flight, from bow to where the target was expected to be.</summary>
public sealed class Arrow
{
    public float X0, Y0, Z0, X1, Y1, Z1, T, Dur;
    public Unit Target;
    public bool Hits, Done;
    public float Damage;
}

public sealed partial class Sim
{
    public readonly List<Arrow> Arrows = new();
    /// <summary>Arrows loosed since the start, and how many found their mark.</summary>
    public int Shots, ShotHits;
    /// <summary>Raiders killed since the start.</summary>
    public int Kills;
    const float ArrowSpeed = 15 * 16;   // voxels a second

    public bool AnyRaider()
    {
        foreach (var u in Units) if (!u.Dead && u.Team == Team.Horde) return true;
        return false;
    }

    /// <summary>Living raiders in the valley.</summary>
    public int RaidersAlive()
    {
        int n = 0;
        foreach (var u in Units) if (!u.Dead && u.Team == Team.Horde) n++;
        return n;
    }

    /// <summary>Living units of a kind.</summary>
    public int Count(UnitType t)
    {
        int n = 0;
        foreach (var u in Units) if (!u.Dead && u.T == t) n++;
        return n;
    }

    public static float Dist(Unit a, Unit b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z, dy = (a.Y - b.Y) * .7f;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>The nearest living unit of a team within a distance (voxels), or null.</summary>
    public Unit Nearest(Unit u, Team team, float maxDist)
    {
        Unit best = null; float bd = maxDist;
        foreach (var o in Units)
        {
            if (o.Dead || o.Team != team) continue;
            float d = Dist(u, o);
            if (d < bd) { bd = d; best = o; }
        }
        return best;
    }

    /// <summary>Close enough to strike, with nothing solid between them: no hitting through walls.</summary>
    public bool InReach(Unit a, Unit b) => Dist(a, b) <= a.T.Reach + b.T.BodyR && Sight(a, b);

    void Strike(Unit a, Unit b)
    {
        a.Swing = 1;
        Hurt(b, a, a.T.Damage * (.8f + (float)Rng.NextDouble() * .4f));
    }

    public void Hurt(Unit b, Unit by, float dmg, string cause = null)
    {
        if (b.Dead) return;
        b.Hp -= dmg;
        b.LastHurt = Time;
        if (b.Hp <= 0) Kill(b, by, cause);
    }

    void Kill(Unit u, Unit by, string cause)
    {
        u.Dead = true; u.DiedAt = Time; u.State = UnitState.Dead; u.Hp = 0;
        u.KilledBy = cause ?? by?.T.Name ?? "";
        if (u.Team == Team.Horde) Kills++;
        ReleaseSite(u);
        u.Path = null; u.Target = null;
        if (u.Team == Team.Horde) Stock.Gold += u.T.Bounty;
        else Say($"{u.T.Name} пал.", 3);
    }

    /// <summary>Soldiers: shoot or close in; true while busy fighting.</summary>
    bool Fight(Unit u, float dt)
    {
        if (u.T.Ranged)
        {
            var t = u.Target;
            if (t == null || t.Dead || Dist(u, t) > u.T.Range || Time >= u.NextSearch)
            {
                t = BestTarget(u);
                u.Target = t;
                u.NextSearch = Time + .4;
            }
            if (t == null) return false;
            u.State = UnitState.Fight;
            u.Path = null;
            u.Facing = MathF.Atan2(t.Z - u.Z, t.X - u.X);
            if (u.Cooldown <= 0) { Shoot(u, t); u.Cooldown = u.T.AttackTime * (.9f + (float)Rng.NextDouble() * .2f); }
            return true;
        }
        // swordsmen hold the ground round themselves or their post
        float leash = 6 * 16;
        var foe = Nearest(u, Team.Horde, leash);
        if (foe == null && u.Post >= 0)
        {
            var (px, py, pz) = Nav.Feet(u.Post);
            foreach (var o in Units)
            {
                if (o.Dead || o.Team != Team.Horde) continue;
                float dx = o.X - px, dz = o.Z - pz;
                if (dx * dx + dz * dz < leash * leash) { foe = o; break; }
            }
        }
        if (foe == null) return false;
        u.State = UnitState.Fight;
        ReleaseSite(u);
        if (InReach(u, foe))
        {
            u.Path = null;
            u.Facing = MathF.Atan2(foe.Z - u.Z, foe.X - u.X);
            if (u.Cooldown <= 0) { Strike(u, foe); u.Cooldown = u.T.AttackTime * (.9f + (float)Rng.NextDouble() * .2f); }
            return true;
        }
        if (u.Path == null || Time >= u.NextSearch)
        {
            u.NextSearch = Time + .5;
            if (!GoTo(u, foe.Cell, adjacent: true, budget: 6000)) u.Path = null;
        }
        if (u.Path != null) Walk(u, dt);
        return true;
    }

    /// <summary>A raider in range and in sight; the ones hacking walls or battering the citadel come first.</summary>
    Unit BestTarget(Unit u)
    {
        Unit best = null; float bd = float.MaxValue;
        foreach (var o in Units)
        {
            if (o.Dead || o.Team != Team.Horde) continue;
            float d = Dist(u, o);
            if (d > u.T.Range) continue;
            float pri = d - (o.State is UnitState.Carve or UnitState.Siege ? 48 : 0);
            if (pri >= bd || !Sight(u, o)) continue;
            bd = pri; best = o;
        }
        return best;
    }

    /// <summary>A clear line from the archer's eyes to the target's chest; leaves don't stop arrows.</summary>
    public bool Sight(Unit a, Unit b)
    {
        float ax = a.X, ay = a.Y + a.T.BodyH * .85f, az = a.Z;
        float bx = b.X, by = b.Y + b.T.BodyH * .55f, bz = b.Z;
        float dx = bx - ax, dy = by - ay, dz = bz - az, len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1) return true;
        if (!Raycast.Cast(World, ax, ay, az, dx / len, dy / len, dz / len, out var h, skipVeg: true)) return true;
        float hx = h.X + .5f - ax, hy = h.Y + .5f - ay, hz = h.Z + .5f - az;
        return MathF.Sqrt(hx * hx + hy * hy + hz * hz) >= len - 3;
    }

    void Shoot(Unit u, Unit t)
    {
        float d = Dist(u, t);
        float chance = Math.Clamp(.9f - d / u.T.Range * .45f + (u.Y - t.Y > 16 ? .08f : 0), .35f, .92f);
        u.Swing = 1;
        Shots++;
        Arrows.Add(new Arrow
        {
            X0 = u.X, Y0 = u.Y + u.T.BodyH * .85f, Z0 = u.Z,
            X1 = t.X + (float)(Rng.NextDouble() - .5) * 4, Y1 = t.Y + t.T.BodyH * .55f, Z1 = t.Z + (float)(Rng.NextDouble() - .5) * 4,
            Dur = Math.Max(.08f, d / ArrowSpeed), Target = t, Hits = Rng.NextDouble() < chance,
            Damage = u.T.Damage * (.8f + (float)Rng.NextDouble() * .5f),
        });
    }

    void TickArrows(float dt)
    {
        foreach (var a in Arrows)
        {
            a.T += dt;
            if (a.T < a.Dur || a.Done) continue;
            a.Done = true;
            if (a.Hits && a.Target != null && !a.Target.Dead) { ShotHits++; Hurt(a.Target, null, a.Damage, "стрела"); }
        }
        Arrows.RemoveAll(a => a.Done && a.T > a.Dur + 1.5f);
    }
}
