using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

public sealed partial class Sim
{
    /// <summary>How many beasts the valley keeps: herds wander back in from the edges as they are hunted.</summary>
    public int WildTarget = 30;
    double _nextHerd = 60;

    /// <summary>Herds of deer, boars and hares spread over the valley, away from the citadel and the horde's edge.</summary>
    public int SeedWildlife(int herds = 12)
    {
        int n = 0;
        for (int h = 0; h < herds; h++) n += Herd(h % 3 == 0 ? Kinds.Boar : h % 3 == 1 ? Kinds.Deer : Kinds.Hare, far: false);
        return n;
    }

    /// <summary>A few beasts of a kind at a grassy spot; far keeps them away from the pawns.</summary>
    int Herd(UnitType t, bool far)
    {
        for (int tries = 0; tries < 40; tries++)
        {
            int x = Site.SpawnStrip + 40 + Rng.Next(World.SX - Site.SpawnStrip - 80), z = 40 + Rng.Next(World.SZ - 80);
            if (Math.Abs(x - Site.KeepX) < 220 && Math.Abs(z - Site.KeepZ) < 220) continue;
            // a herd's ground well in from the land's edge, where it has room to graze and to run
            if (Site.Hex != null && (!Site.Inland(x, z, EdgeRoom + 24) || Site.InStrip(x, z))) continue;
            int top = World.SurfAt(x, z);
            byte m = World.Get(x, top - 1, z);
            if (!Mat.IsGrass(m) && m != Mat.Soil && m != Mat.Moss) continue;
            if (far && NearAny(x, z, Team.Player, 30 * 16)) continue;
            int c = NearestStand(x, z, t.Move.Height);
            if (c < 0 || Nav.Water[c] > 0) continue;
            int count = t == Kinds.Deer ? 3 + Rng.Next(3) : t == Kinds.Boar ? 1 + Rng.Next(2) : 2 + Rng.Next(3);
            var (hx, _, hz) = Nav.Feet(c);
            for (int i = 0; i < count; i++)
            {
                // each beast of the herd has a spot of its own near the others
                int own = i == 0 ? c : NearestStand((int)hx + Rng.Next(-28, 29), (int)hz + Rng.Next(-28, 29), t.Move.Height);
                if (own < 0 || Nav.Water[own] > 0) own = c;
                var u = Spawn(t, own);
                u.Home = own;
                u.NextSearch = Time + Rng.NextDouble() * 6;
            }
            return count;
        }
        return 0;
    }

    /// <summary>How far in from the land's edge a beast keeps when it can (voxels): it grazes and runs inside that.</summary>
    const float EdgeRoom = 40;

    /// <summary>
    /// Where a beast bolts to: so far away along the way from the threat, give or take; and if that is out against the
    /// land's edge, a way turned aside from it, a little at a time either way — along the edge rather than into it — or
    /// failing that, back in toward the middle.
    /// </summary>
    (float x, float z) FleeTo(Unit u, float dx, float dz, float run, float jx, float jz)
    {
        float x = u.X + dx * run + jx, z = u.Z + dz * run + jz;
        // (the box of the bench has no such edge to keep off: its sides only stop the way)
        if (Site.Hex == null || Site.Inland(x, z, EdgeRoom)) return (x, z);
        for (int k = 1; k <= 5; k++)
            foreach (int sign in new[] { 1, -1 })
            {
                float a = sign * k * MathF.PI / 7, c = MathF.Cos(a), s = MathF.Sin(a);
                float rx = dx * c - dz * s, rz = dx * s + dz * c;
                float px = u.X + rx * run + jx, pz = u.Z + rz * run + jz;
                if (Site.Inland(px, pz, EdgeRoom)) return (px, pz);
            }
        return Site.PullIn(x, z, EdgeRoom);
    }

    bool NearAny(int x, int z, Team team, float dist)
    {
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Team != team) continue;
            float dx = u.X - x, dz = u.Z - z;
            if (dx * dx + dz * dz < dist * dist) return true;
        }
        return false;
    }

    /// <summary>A guardian drawn too far from the spot it keeps (30 m): it lets its foe go and goes back.</summary>
    bool Leashed(Unit u)
    {
        if (!u.Guard || u.Home < 0) return false;
        var (hx, _, hz) = Nav.Feet(u.Home);
        float dx = u.X - hx, dz = u.Z - hz;
        return dx * dx + dz * dz > GuardLeash * GuardLeash;
    }

    const float GuardLeash = 30 * VoxelWorld.PerMetre;

    /// <summary>
    /// A fighter of ours turns on a beast that goes for whoever comes near (a bear, a guardian of a treasure): one that
    /// struck him a moment ago, or, if nobody sent him anywhere, one set on one of ours within 12 m of him. True if he did.
    /// </summary>
    bool Beset(Unit u)
    {
        if (u.HurtBy is { Dead: false, Team: Team.Wild } by && by.T.Aggro > 0 && Time - u.LastHurt < 3) { u.Prey = by; u.Path = null; return true; }
        if (u.Hold) return false;
        Unit best = null; float bd = 12 * VoxelWorld.PerMetre;
        foreach (var o in Units)
        {
            if (o.Dead || o.Team != Team.Wild || o.T.Aggro <= 0 || o.Target is not { Dead: false, Team: Team.Player }) continue;
            float d = Dist(u, o);
            if (d < bd) { bd = d; best = o; }
        }
        if (best == null) return false;
        u.Prey = best; u.Path = null;
        return true;
    }

    /// <summary>Living beasts in the valley.</summary>
    public int WildAlive()
    {
        int n = 0;
        foreach (var u in Units) if (!u.Dead && u.Team == Team.Wild) n++;
        return n;
    }

    /// <summary>Now and then a new herd wanders in to replace what was hunted.</summary>
    void TendWildlife()
    {
        if (Time < _nextHerd) return;
        _nextHerd = Time + 90 + Rng.NextDouble() * 60;
        if (WildAlive() >= WildTarget) return;
        var t = Rng.Next(3) switch { 0 => Kinds.Boar, 1 => Kinds.Deer, _ => Kinds.Hare };
        Herd(t, far: true);
    }

    void WildTick(Unit u, float dt)
    {
        if (!FireProof(u.T) && EscapeFire(u, dt)) return;
        // a bear goes for whoever comes near it
        BearTick(u);
        // a creature of the bestiary: its pack, its power, its bow or its fire (Sim.Bestiary.cs)
        if (BeastTick(u, dt)) return;
        // a wounded boar goes for whoever wounded it, for a while
        var foe = u.Target;
        if (foe != null && (foe.Dead || Time - u.LastHurt > 8 || Dist(u, foe) > 24 * 16 || Leashed(u))) { u.Target = foe = null; u.Path = null; }
        if (foe != null)
        {
            u.State = UnitState.Fight;
            if (InReach(u, foe))
            {
                u.Path = null;
                u.Facing = MathF.Atan2(foe.Z - u.Z, foe.X - u.X);
                if (u.Cooldown <= 0) { Strike(u, foe); u.Cooldown = u.T.AttackTime * (.9f + (float)Rng.NextDouble() * .2f); }
                return;
            }
            if (u.Path == null || Time >= u.NextSearch)
            {
                u.NextSearch = Time + .6;
                if (!GoTo(u, foe.Cell, adjacent: true, budget: 4000)) u.Path = null;
            }
            if (u.Path != null) Walk(u, dt);
            return;
        }

        // bolt from pawns and raiders that come too close
        Unit threat = null; float td = u.T.FleeDist;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Team == Team.Wild) continue;
            float d = Dist(u, o);
            if (d < td) { td = d; threat = o; }
        }
        if (threat != null || (u.State == UnitState.Flee && Time - u.LastHurt < 3))
        {
            if (u.State != UnitState.Flee || u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
            {
                u.State = UnitState.Flee;
                u.NextSearch = Time + 1.2;
                float ax = threat != null ? u.X - threat.X : (float)(Rng.NextDouble() - .5), az = threat != null ? u.Z - threat.Z : (float)(Rng.NextDouble() - .5);
                float len = MathF.Max(.01f, MathF.Sqrt(ax * ax + az * az));
                float run = 14 * NavGrid.CS;
                float jx = (float)(Rng.NextDouble() - .5) * 60, jz = (float)(Rng.NextDouble() - .5) * 60;
                var (fx, fz) = FleeTo(u, ax / len, az / len, run, jx, jz);
                int tx = (int)Math.Clamp(fx, 8, World.SX - 9), tz = (int)Math.Clamp(fz, 8, World.SZ - 9);
                int c = NearestStand(tx, tz, u.T.Move.Height);
                if (c < 0 || !GoTo(u, c, budget: 3000)) u.Path = null;
            }
            if (u.Path != null) Walk(u, dt);
            if (threat == null && (u.Path == null || u.PathI >= u.Path.Count)) u.State = UnitState.Idle;
            return;
        }
        if (u.State == UnitState.Flee) { u.State = UnitState.Idle; u.Path = null; }

        // graze, and now and then amble somewhere near home
        if (u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt, .35f); return; }
        u.State = UnitState.Idle;
        if (Time < u.NextSearch) return;
        u.NextSearch = Time + 5 + Rng.NextDouble() * 10;
        int home = u.Home >= 0 ? u.Home : u.Cell;
        var (hx, hy, hz) = Nav.Feet(home);
        // a keeper keeps to its spot, a step or two about it, under the roof of its mine or its den too
        if (u.Guard)
        {
            int at = StandNear((int)hx + Rng.Next(-12, 13), (int)hy, (int)hz + Rng.Next(-12, 13), u.T.Move.Height);
            if (at >= 0 && at != u.Cell && !GoTo(u, at, budget: 1500)) u.Path = null;
            return;
        }
        float ox = (float)(Rng.NextDouble() - .5) * 2 * 10 * 16, oz = (float)(Rng.NextDouble() - .5) * 2 * 10 * 16;
        // somewhere near home, and never out against the land's edge
        var (gx, gz) = Site.Hex != null ? Site.PullIn(hx + ox, hz + oz, EdgeRoom) : (hx + ox, hz + oz);
        int wx = (int)Math.Clamp(gx, 8, World.SX - 9), wz = (int)Math.Clamp(gz, 8, World.SZ - 9);
        int w = NearestStand(wx, wz, u.T.Move.Height);
        if (w >= 0 && !Nav.Deep(w) && !GoTo(u, w, budget: 2500)) u.Path = null;
    }
}
