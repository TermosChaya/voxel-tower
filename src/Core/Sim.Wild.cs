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
        if (EscapeFire(u, dt)) return;
        // a bear goes for whoever comes near it
        BearTick(u);
        // a wounded boar goes for whoever wounded it, for a while
        var foe = u.Target;
        if (foe != null && (foe.Dead || Time - u.LastHurt > 8 || Dist(u, foe) > 24 * 16)) { u.Target = foe = null; u.Path = null; }
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
                int tx = (int)Math.Clamp(u.X + ax / len * run + (Rng.NextDouble() - .5) * 60, 8, World.SX - 9);
                int tz = (int)Math.Clamp(u.Z + az / len * run + (Rng.NextDouble() - .5) * 60, 8, World.SZ - 9);
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
        var (hx, _, hz) = Nav.Feet(home);
        int wx = (int)Math.Clamp(hx + (Rng.NextDouble() - .5) * 2 * 10 * 16, 8, World.SX - 9);
        int wz = (int)Math.Clamp(hz + (Rng.NextDouble() - .5) * 2 * 10 * 16, 8, World.SZ - 9);
        int w = NearestStand(wx, wz, u.T.Move.Height);
        if (w >= 0 && !Nav.Deep(w) && !GoTo(u, w, budget: 2500)) u.Path = null;
    }
}
