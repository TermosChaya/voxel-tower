using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>An arrow in flight, from bow to where the target was expected to be.</summary>
public sealed class Arrow
{
    public float X0, Y0, Z0, X1, Y1, Z1, T, Dur;
    public Unit Target, From;
    public bool Hits, Done;
    public float Damage;
    /// <summary>A fire arrow; and the voxel (packed) it was loosed at to set alight, or −1 when it was loosed at someone.</summary>
    public bool Fire;
    /// <summary>A catapult's stone, not an arrow: it breaks out what it comes down on.</summary>
    public bool Stone;
    public int At = -1;
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
        foreach (var u in Units) if (!u.Dead && u.Team == Team.Horde && !u.Guard) return true;
        return false;
    }

    /// <summary>Living raiders in the valley.</summary>
    public int RaidersAlive()
    {
        int n = 0;
        foreach (var u in Units) if (!u.Dead && u.Team == Team.Horde && !u.Guard) n++;
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
            // one of ours bound and led off is in no fight, nor an envoy under a white rag
            if (o.Dead || o.Inside || o.Captor != null || o.Envoy || o.Team != team) continue;
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
        // the blow comes from the striker's hand at the height it holds its weapon, at a spot on the body near that height
        var (cut, depth, reach) = WeaponOf(a.T);
        float sa = a.Leader ? 1.15f : 1, sb = b.Leader ? 1.15f : 1;
        float hand = a.Y + a.T.BodyH * sa * (a.T.Team == Team.Wild ? .3f : .62f);
        float aim = Math.Clamp(hand + (float)(Rng.NextDouble() - .45) * b.T.BodyH * sb * .5f, b.Y + 1, b.Y + b.T.BodyH * sb - .5f);
        float side = (float)(Rng.NextDouble() - .5) * b.T.BodyR * 1.6f;
        float dx = b.X - a.X, dz = b.Z - a.Z, l = MathF.Max(.01f, MathF.Sqrt(dx * dx + dz * dz));
        float tx = b.X - dz / l * side, tz = b.Z + dx / l * side;
        b.HurtBy = a;
        float share = Kinds.IsEngine(b.T) ? EngineShare(false) : Wound(b, cut, depth, reach, a.X, hand, a.Z, tx - a.X, aim - hand, tz - a.Z);
        Hurt(b, a, a.T.Damage * (.8f + (float)Rng.NextDouble() * .4f) * share);
    }

    public void Hurt(Unit b, Unit by, float dmg, string cause = null)
    {
        if (b.Dead) return;
        b.Hp -= dmg;
        b.LastHurt = Time;
        if (by != null) b.HurtBy = by;
        // a wounded boar turns on whoever wounded it
        if (b.Team == Team.Wild && b.T.Damage > 0 && by != null && !by.Dead) b.Target = by;
        if (b.Hp <= 0) Kill(b, by, cause);
    }

    void Kill(Unit u, Unit by, string cause)
    {
        // what dies of its wounds dies by the hand that dealt them
        by ??= u.HurtBy;
        u.Dead = true; u.DiedAt = Time; u.State = UnitState.Dead; u.Hp = 0;
        u.KilledBy = cause ?? by?.T.Name ?? "";
        if (u.Team == Team.Horde) Kills++;
        if (u.T == Kinds.Thief) ThiefFell(u);
        if (u.T == Kinds.SiegeTower) TowerFell(u);
        if (u.Captor != null) { u.Captor.Captive = null; u.Captor = null; }
        ReleaseSite(u);
        StopCarve(u); StopBuild(u);
        u.Path = null; u.Target = null; u.Prey = null;
        // a leader is worth thrice its kind
        if (u.Team == Team.Horde) Stock.Gold += u.T.Bounty * (u.Leader ? 3 : 1);
        else if (u.Team == Team.Wild)
        {
            if (by?.Team == Team.Player) { Stock.Gold += u.T.Bounty; Hunted++; Say($"Добыт {u.T.Name.ToLowerInvariant()}: +{u.T.Bounty} золота.", 1, u); }
        }
        else Say($"{u.T.Name} пал.", 3, u);
    }

    /// <summary>Beasts taken by the pawns since the start.</summary>
    public int Hunted;

    // ------------------------------------------------------------ the player's quarry

    /// <summary>Sends pawns after a beast to hunt, or after a raider to strike down.</summary>
    public int Hunt(IEnumerable<Unit> hunters, Unit prey)
    {
        if (prey == null || prey.Dead || prey.Team == Team.Player) return 0;
        int n = 0;
        foreach (var u in hunters)
        {
            if (u.Dead || u.Team != Team.Player) continue;
            ReleaseSite(u);
            u.Prey = prey; u.Hold = false; u.Path = null; u.NextSearch = 0;
            n++;
        }
        return n;
    }

    /// <summary>Chasing what the player pointed at: archers shoot from range, the rest close in. False when it is over.</summary>
    bool HuntTick(Unit u, float dt)
    {
        var p = u.Prey;
        if (p == null || p.Dead || Dist(u, p) > 70 * 16 || !Armed(u)) { u.Prey = null; u.Path = null; return false; }
        u.State = UnitState.Fight;
        if (u.T.Ranged ? Dist(u, p) <= RangeAt(u, p.X, p.Z) && Sight(u, p) : InReach(u, p))
        {
            u.Path = null;
            u.Facing = MathF.Atan2(p.Z - u.Z, p.X - u.X);
            if (u.Cooldown <= 0)
            {
                if (u.T.Ranged) Shoot(u, p); else Strike(u, p);
                u.Cooldown = u.T.AttackTime * (.9f + (float)Rng.NextDouble() * .2f);
            }
            return true;
        }
        if (u.Path == null || Time >= u.NextSearch)
        {
            u.NextSearch = Time + .7;
            if (!GoTo(u, p.Cell, adjacent: true, budget: 8000)) u.Path = null;
        }
        if (u.Path != null) Walk(u, dt);
        else u.State = UnitState.Idle;
        return true;
    }

    /// <summary>Soldiers: shoot or close in; true while busy fighting.</summary>
    bool Fight(Unit u, float dt)
    {
        if (u.T.Ranged)
        {
            // what the player pointed at to set alight comes first
            if (u.KindleAt >= 0 && KindleShot(u, dt)) return true;
            var t = u.Target;
            if (t == null || t.Dead || Dist(u, t) > RangeAt(u, t.X, t.Z) || Time >= u.NextSearch)
            {
                t = BestTarget(u);
                u.Target = t;
                u.NextSearch = Time + .4;
            }
            if (t == null) return KindleShot(u, dt);
            // sent somewhere and not there yet: shoot on the way, but keep going
            bool marching = u.Hold && u.Dest >= 0 && CellsApart(u.Cell, u.Dest) > 1 && u.Path != null && u.PathI < u.Path.Count;
            if (!marching) { u.State = UnitState.Fight; u.Path = null; u.Facing = MathF.Atan2(t.Z - u.Z, t.X - u.X); }
            // a fire arrow takes a moment to light
            if (u.Cooldown <= 0) { Shoot(u, t); u.Cooldown = u.T.AttackTime * (u.FireArrows ? 1.35f : 1) * (.9f + (float)Rng.NextDouble() * .2f); }
            return !marching;
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
        // one of ours set upon close by: to its help, past the post if need be
        if (foe == null)
        {
            float best = 22 * VoxelWorld.PerMetre;
            foreach (var o in Units)
            {
                if (o.Dead || o.Team != Team.Horde || o.State != UnitState.Fight || o.Target is not { Dead: false, Team: Team.Player }) continue;
                float d = Dist(u, o);
                if (d < best) { best = d; foe = o; }
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

    // ------------------------------------------------------------ the dark

    /// <summary>
    /// Patches of four metres where it is light at night: by the torches on the walls, by fires, round the citadel, round
    /// a raider carrying a torch and round anyone who burns.
    /// </summary>
    readonly HashSet<int> _lit = new();
    double _litAt = -99;

    static int LitKey(int x, int z) => (x >> 5) | (z >> 5) << 10;

    /// <summary>Lays out once a second where it is light.</summary>
    void TickLight()
    {
        if (Time - _litAt < 1) return;
        _litAt = Time;
        _lit.Clear();
        // by day the whole valley is light
        if (Waves.Darkness <= 0) return;
        void Around(float x, float z, float r)
        {
            for (float dz = -r; dz <= r; dz += 16)
                for (float dx = -r; dx <= r; dx += 16)
                    if (dx * dx + dz * dz <= r * r) _lit.Add(LitKey((int)Math.Clamp(x + dx, 0, World.SX - 1), (int)Math.Clamp(z + dz, 0, World.SZ - 1)));
        }
        foreach (var f in Fixtures) if (f.Kind == FixtureKind.Torch) Around(f.LX, f.LZ, 6 * VoxelWorld.PerMetre);
        Around(Site.KeepX, Site.KeepZ, Site.KeepR + 8 * VoxelWorld.PerMetre);
        int n = 0;
        foreach (int p in Burning.Keys)
        {
            if (n++ % 7 != 0) continue;
            var (x, _, z) = WaterPlanner.Unpack(p);
            Around(x, z, 5 * VoxelWorld.PerMetre);
        }
        foreach (var u in Units)
            if (!u.Dead && (u.Torch || u.Burn > 0)) Around(u.X, u.Z, 3 * VoxelWorld.PerMetre);
    }

    /// <summary>Whether a spot is lit at night.</summary>
    public bool Lit(float x, float z) => _lit.Contains(LitKey((int)Math.Clamp(x, 0, World.SX - 1), (int)Math.Clamp(z, 0, World.SZ - 1)));

    /// <summary>How far a bow carries into the dark now: all the way by day, little more than half at night.</summary>
    public float DarkRange(Unit a) => a.T.Range * FogRange * (1 - .45f * Waves.Darkness);

    /// <summary>How far a bow carries to a spot: all the way by day or where it is light, little more than half in the dark.</summary>
    public float RangeAt(Unit a, float x, float z) => Waves.Darkness <= 0 || Lit(x, z) ? a.T.Range * FogRange : DarkRange(a);

    /// <summary>A raider in range and in sight; the ones hacking walls or battering the citadel come first.</summary>
    Unit BestTarget(Unit u)
    {
        Unit best = null; float bd = float.MaxValue;
        foreach (var o in Units)
        {
            if (o.Dead || o.Team != Team.Horde || o.Envoy) continue;
            float d = Dist(u, o);
            if (d > RangeAt(u, o.X, o.Z)) continue;
            // those cutting down one of ours come first, then those hacking the walls, battering the citadel, raising
            // ladders or climbing them
            bool hurting = o.State == UnitState.Fight && o.Target is { Dead: false, Team: Team.Player };
            bool busy = o.State is UnitState.Carve or UnitState.Siege or UnitState.Work || Climbing(o);
            // a thief making off with what it took, or with one of ours, before anything
            bool making = o.Loot > 0 || o.Captive != null;
            // an engine: hardly worth a plain arrow, the first mark for a fire arrow
            float engine = Kinds.IsEngine(o.T) ? (u.FireArrows ? -96 : 96) : 0;
            // with fire arrows, those standing on a deck, a ladder or pitch: one arrow sets it all alight under them
            float pri = d - (making ? 96 : hurting ? 64 : busy ? 48 : 0) - (u.FireArrows && OnTinder(o) ? 40 : 0) + engine;
            if (pri >= bd || !Sight(u, o)) continue;
            bd = pri; best = o;
        }
        return best;
    }

    /// <summary>A clear line from the archer's eyes to the target's chest; leaves don't stop arrows.</summary>
    public bool Sight(Unit a, Unit b) => Sees(a, b.X, b.Y + b.T.BodyH * .55f, b.Z);

    /// <summary>A clear line from a unit's eyes to a point (voxels); leaves don't stop arrows.</summary>
    public bool Sees(Unit a, float bx, float by, float bz)
    {
        float ax = a.X, ay = a.Y + a.T.BodyH * .85f, az = a.Z;
        float dx = bx - ax, dy = by - ay, dz = bz - az, len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1) return true;
        if (!Raycast.Cast(World, ax, ay, az, dx / len, dy / len, dz / len, out var h, skipVeg: true)) return true;
        float hx = h.X + .5f - ax, hy = h.Y + .5f - ay, hz = h.Z + .5f - az;
        return MathF.Sqrt(hx * hx + hy * hy + hz * hz) >= len - 3;
    }

    void Shoot(Unit u, Unit t)
    {
        // a mantlet before the one aimed at takes the arrow
        t = Cover(u, t) ?? t;
        float d = Dist(u, t);
        float chance = Math.Clamp(.9f - d / u.T.Range * .45f + (u.Y - t.Y > 16 ? .08f : 0), .35f, .92f);
        // a shape in the dark is harder to hit than one in the light; rain and wind spoil aim
        if (!Lit(t.X, t.Z)) chance *= 1 - .2f * Waves.Darkness;
        chance *= WeatherAim;
        u.Swing = 1;
        Shots++;
        Arrows.Add(new Arrow
        {
            X0 = u.X, Y0 = u.Y + u.T.BodyH * .85f, Z0 = u.Z,
            X1 = t.X + (float)(Rng.NextDouble() - .5) * 4, Y1 = t.Y + t.T.BodyH * .55f, Z1 = t.Z + (float)(Rng.NextDouble() - .5) * 4,
            Dur = Math.Max(.08f, d / ArrowSpeed), Target = t, From = u, Hits = Rng.NextDouble() < chance,
            Damage = u.T.Damage * (.8f + (float)Rng.NextDouble() * .5f), Fire = u.FireArrows,
        });
    }

    void TickArrows(float dt)
    {
        foreach (var a in Arrows)
        {
            a.T += dt;
            if (a.T < a.Dur || a.Done) continue;
            a.Done = true;
            if (a.Stone) { StoneLands(a); continue; }
            if (a.Hits && a.Target != null && !a.Target.Dead)
            {
                ShotHits++;
                // the arrow comes in along its flight, at a spot on the body where it was going
                var t = a.Target;
                float st = t.Leader ? 1.15f : 1;
                // coming down at the end of its arc (it rose a tenth of the way it flew)
                float dx = a.X1 - a.X0, dz = a.Z1 - a.Z0, dy = a.Y1 - a.Y0 - .4f * MathF.Sqrt(dx * dx + dz * dz);
                float hy = t.Y + t.T.BodyH * st * (.25f + (float)Rng.NextDouble() * .7f);
                float side = (float)(Rng.NextDouble() - .5) * t.T.BodyR * 1.4f, l = MathF.Max(.01f, MathF.Sqrt(dx * dx + dz * dz));
                float tx = t.X - dz / l * side, tz = t.Z + dx / l * side;
                float dl = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                var (cut, depth, _) = WeaponOf(a.From?.T ?? Kinds.Archer);
                if (a.From != null) t.HurtBy = a.From;
                float share = Kinds.IsEngine(t.T) ? EngineShare(true) : Wound(t, cut, depth, 0, tx - dx / dl * 30, hy - dy / dl * 30, tz - dz / dl * 30, dx, dy, dz);
                Hurt(t, a.From, a.Damage * share, "стрела");
            }
            if (a.Fire) FireArrowLands(a);
        }
        Arrows.RemoveAll(a => a.Done && a.T > a.Dur + 1.5f);
    }
}
