using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Our alchemists (the lore «Алхимики», since 25.10.2026 in the fire pots' place among the shot): taken on at the citadel,
/// they keep out of the press and lob flasks of acid, first at what the horde has raised — a ladder with raiders on it (the
/// rungs under the highest of them, so that he comes down with all below him), an engine, a deck with raiders on it, a
/// ladder against our walls with nobody on it yet — then into a knot of raiders, then at whoever is in a throw. Never where
/// one of ours stands by the spot. Their acid is mixed to eat the horde's timber and hide, not stone: near anything of ours
/// it does not run free but eats into the horde's ladders and decks where it splashes them, through in a second or two,
/// and burns the horde (not ours) about it; out on open ground, well clear of ours, it lies and runs as the horde's acid
/// does. With the bombardiers learnt, into a knot of three or more they now and then throw a flask of choking fumes: those
/// in it cough, their arms down, the pain of it in them, and the weaker run. The horde's own alchemists throw at ours first.
/// </summary>
public sealed partial class Sim
{
    /// <summary>How near a raider may come before an alchemist of ours steps back out of its way.</summary>
    const float OurFlaskClose = 3.5f * VoxelWorld.PerMetre;
    /// <summary>How far from anything of ours acid of ours may lie and run free.</summary>
    const int OursClear = 5 * VoxelWorld.PerMetre;
    /// <summary>How far about where a flask of ours breaks the horde's ladders and decks are eaten, voxels.</summary>
    const int OurSplash = 9;
    /// <summary>Seconds the horde's ladders and decks take to be eaten through where a flask of ours splashed them.</summary>
    const float EatLadder = 1.2f, EatDeck = 1.8f;
    /// <summary>How many of the throws into a knot of three or more are fumes, with the bombardiers learnt.</summary>
    const double FumesShare = .4;

    /// <summary>The horde's works a flask of ours is eating: the voxel (packed), and the seconds left till it is through.</summary>
    readonly Dictionary<int, float> _oursEating = new();
    readonly List<int> _oursNow = new();
    /// <summary>Flasks of ours thrown, of them fumes; voxels of the horde's works eaten through by ours (for the tests).</summary>
    public int OurFlasks, OurFumes, OurAte;

    /// <summary>A flask of ours of fumes broke here: for the cloud of it.</summary>
    public event Action<float, float, float> Fumed;

    /// <summary>An alchemist of ours at its throws: true while it stands throwing (or steps back from the press).</summary>
    bool OurAlchemistTick(Unit u, float dt)
    {
        if (Climbing(u) || Time < u.StaggerUntil) return false;
        // a raider at its elbow: back out of its way, a flask is no blade
        var close = Nearest(u, Team.Horde, OurFlaskClose);
        if (close != null && !Kinds.IsEngine(close.T) && !close.Bound && StepBack(u, close, dt)) return true;
        if (u.Target is { Dead: true } || Time >= u.NextSearch)
        {
            u.NextSearch = Time + .9 + u.Id % 5 * .1;
            u.AimAt = OurMark(u, out var at);
            u.Target = at;
        }
        if (u.AimAt < 0) return false;
        var (ax, ay, az) = WaterPlanner.Unpack(u.AimAt);
        float tx = ax + .5f, ty = ay + .5f, tz = az + .5f;
        if (u.Target is { Dead: false } t) { tx = t.X; ty = t.Y + t.T.BodyH * .5f; tz = t.Z; }
        else if (u.Target != null) { u.Target = null; u.AimAt = -1; return false; }
        float dx = tx - u.X, dz = tz - u.Z, d = MathF.Sqrt(dx * dx + dz * dz);
        if (d > ShotRange(u)) return false;
        u.Path = null;
        u.State = UnitState.Fight;
        u.Facing = MathF.Atan2(dz, dx);
        if (u.Cooldown <= 0)
        {
            // a knot of three or more, with the bombardiers' lore: now and then the fumes
            bool fumes = u.Target != null && Knows("bombards") && Knot(u.Target) >= 2 && Rng.NextDouble() < FumesShare;
            ThrowOurFlask(u, tx, ty, tz, d, u.Target, u.Target == null ? u.AimAt : -1, fumes);
            u.Cooldown = u.T.AttackTime * ShotPace(u) * (.9f + (float)Rng.NextDouble() * .2f) / (PainPace(u) * Drum);
        }
        return true;
    }

    /// <summary>
    /// What an alchemist of ours throws at, in a throw of it: the rungs of a ladder under the highest raider on it, an
    /// engine of the horde, the deck under a raider standing on one, a ladder of the horde's with nobody on it, a knot of
    /// raiders, any raider; never a spot with one of ours by it. The one it throws at (null for a place), and the place
    /// (packed voxel, −1 for none).
    /// </summary>
    int OurMark(Unit u, out Unit at)
    {
        at = null;
        float far = ShotRange(u), best = float.MaxValue;
        int mark = -1;
        foreach (var o in Units)
        {
            if (o.Dead || o.Team != Team.Horde || o.Inside || o.Bound || o.Envoy || o.Airborne != null) continue;
            float d = Dist(u, o);
            if (d > far) continue;
            int spot = -1;
            float score;
            if (Climbing(o) && LadderUnder(o, out spot))
                // the higher up the ladder, the sooner: he comes down with all under him
                score = d - 400 - (o.Y - World.GroundTop((int)o.X, (int)o.Z)) * 4;
            else if (Kinds.IsEngine(o.T)) score = d - 300 - (o.State == UnitState.Siege ? 40 : 0);
            else if (DeckUnder(o, out spot)) score = d - 250;
            else score = d - 60 * Knot(o);
            if (score >= best) continue;
            float px = o.X, py = o.Y + o.T.BodyH * .5f, pz = o.Z;
            if (spot >= 0) (px, py, pz) = Centre(spot);
            if (OursBy(px, py, pz)) continue;
            best = score;
            mark = spot;
            at = spot >= 0 ? null : o;
        }
        if (at != null) return WaterPlanner.Pack((int)at.X, (int)at.Y, (int)at.Z);
        if (mark >= 0) return mark;
        // a ladder of the horde's against our walls with nobody on it yet
        foreach (int c in _works)
        {
            var (fx, fy, fz) = Nav.Feet(c);
            float dx = fx - u.X, dz = fz - u.Z;
            if (dx * dx + dz * dz > far * far) continue;
            if (!LadderIn(c, out int spot)) continue;
            var (px, py, pz) = Centre(spot);
            if (OursBy(px, py, pz)) continue;
            return spot;
        }
        return -1;

        static (float, float, float) Centre(int p) { var (x, y, z) = WaterPlanner.Unpack(p); return (x + .5f, y + .5f, z + .5f); }
    }

    /// <summary>Raiders about one, within two metres and a half of it.</summary>
    int Knot(Unit o)
    {
        int n = 0;
        foreach (var p in Units)
            if (p != o && !p.Dead && !p.Inside && p.Team == Team.Horde && MathF.Abs(p.X - o.X) < 20 && MathF.Abs(p.Z - o.Z) < 20 && MathF.Abs(p.Y - o.Y) < 12) n++;
        return n;
    }

    /// <summary>One of ours standing by a spot, so that a flask is not thrown at it.</summary>
    bool OursBy(float x, float y, float z)
    {
        foreach (var p in Units)
            if (!p.Dead && !p.Inside && p.Team == Team.Player && MathF.Abs(p.X - x) < 14 && MathF.Abs(p.Z - z) < 14 && p.Y < y + 12 && p.Y + p.T.BodyH > y - 12) return true;
        return false;
    }

    /// <summary>The rungs a metre and a half under one climbing a ladder (packed voxel).</summary>
    bool LadderUnder(Unit o, out int spot)
    {
        spot = -1;
        int ox = (int)o.X, oz = (int)o.Z;
        for (int y = (int)o.Y - 12; y < (int)o.Y; y++)
            for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++)
                    if (World.Get(ox + dx, y, oz + dz) == Mat.SiegeLadder) { spot = WaterPlanner.Pack(ox + dx, y, oz + dz); return true; }
        return false;
    }

    /// <summary>The planks of a horde's deck under one standing on it (packed voxel).</summary>
    bool DeckUnder(Unit o, out int spot)
    {
        spot = -1;
        int ox = (int)o.X, oy = (int)o.Y, oz = (int)o.Z;
        for (int y = oy - 1; y >= oy - 3; y--)
            if (World.Get(ox, y, oz) == Mat.Deck) { spot = WaterPlanner.Pack(ox, y, oz); return true; }
        return false;
    }

    /// <summary>A rung of the horde's ladder in a nav cell, against something of ours (packed voxel).</summary>
    bool LadderIn(int c, out int spot)
    {
        spot = -1;
        int x0 = Nav.I(c) << NavGrid.Shift, y0 = Nav.Y(c) << NavGrid.Shift, z0 = Nav.K(c) << NavGrid.Shift;
        for (int y = y0; y < y0 + NavGrid.CS; y += 2)
            for (int z = z0; z < z0 + NavGrid.CS; z++)
                for (int x = x0; x < x0 + NavGrid.CS; x++)
                {
                    if (World.Get(x, y, z) != Mat.SiegeLadder) continue;
                    bool leans = Mat.IsPlayer[World.Get(x + 1, y, z)] || Mat.IsPlayer[World.Get(x - 1, y, z)] || Mat.IsPlayer[World.Get(x, y, z + 1)] || Mat.IsPlayer[World.Get(x, y, z - 1)];
                    if (!leans) continue;
                    spot = WaterPlanner.Pack(x, y, z);
                    return true;
                }
        return false;
    }

    /// <summary>An alchemist of ours steps back from a raider at its elbow: true while it goes.</summary>
    bool StepBack(Unit u, Unit from, float dt)
    {
        if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
        {
            float dx = u.X - from.X, dz = u.Z - from.Z, l = MathF.Max(.5f, MathF.Sqrt(dx * dx + dz * dz));
            int c = NearestStand((int)(u.X + dx / l * 5 * VoxelWorld.PerMetre), (int)(u.Z + dz / l * 5 * VoxelWorld.PerMetre), u.T.Move.Height);
            u.NextSearch = Time + .8;
            if (c < 0 || !GoTo(u, c, budget: 3000)) return false;
        }
        if (u.Path == null || u.PathI >= u.Path.Count) return false;
        u.State = UnitState.Walk;
        Walk(u, dt);
        return true;
    }

    /// <summary>
    /// A flask of ours away, lobbed high: it comes down where it was thrown give or take a little, the more the farther; at
    /// one of the horde, it strikes it more often than not and breaks on it, else on the ground beside.
    /// </summary>
    void ThrowOurFlask(Unit u, float tx, float ty, float tz, float d, Unit at, int spot, bool fumes)
    {
        u.Swing = 1;
        Shots++;
        OurFlasks++;
        if (fumes) OurFumes++;
        float spread = spot >= 0 ? .5f + d / ShotRange(u) * 3 : 1 + d / ShotRange(u) * 6;
        float a = (float)(Rng.NextDouble() * MathF.Tau), r = (float)Rng.NextDouble() * spread;
        Arrows.Add(new Arrow
        {
            X0 = u.X, Y0 = u.Y + u.T.BodyH * .9f, Z0 = u.Z,
            X1 = tx + MathF.Cos(a) * r, Y1 = ty, Z1 = tz + MathF.Sin(a) * r,
            Dur = Math.Max(.35f, d / SpeedOf(Shot.Flask) * 1.2f), Target = at, From = u, Hits = at != null && Rng.NextDouble() < .6,
            Damage = u.T.Damage * ShotHarm(u) * Oath(u), Kind = Shot.Flask, Arc = ArcOf(Shot.Flask), At = spot, Fumes = fumes,
        });
        Threw?.Invoke(u);
    }

    /// <summary>A flask of ours comes down: on the one it was thrown at, if it struck, else where it was thrown.</summary>
    void OurFlaskLands(Arrow a)
    {
        float x = a.X1, y = a.Y1, z = a.Z1;
        if (a.Hits && a.Target is { Dead: false } t) { ShotHits++; x = t.X; y = t.Y + t.T.BodyH * .7f; z = t.Z; }
        else
        {
            // thrown at a wall or the ground, it breaks on the first of it in its way down the last of its flight
            float dx = a.X1 - a.X0, dz = a.Z1 - a.Z0, l = MathF.Max(1, MathF.Sqrt(dx * dx + dz * dz));
            for (int k = 0; k < 24 && World.InBounds((int)x, (int)y, (int)z) && Solid(World.Get((int)x, (int)y, (int)z)); k++)
            {
                x -= dx / l; z -= dz / l; y += .6f;
            }
        }
        FlaskBroke?.Invoke(x, y, z);
        if (a.Fumes) { FumesBreak(x, y, z); return; }
        // the horde about it burnt, the one it broke on the worst; never ours
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null || u.Team == Team.Player) continue;
            float ux = u.X - x, uz = u.Z - z, d = MathF.Sqrt(ux * ux + uz * uz);
            bool on = a.Hits && u == a.Target;
            if (!on && (d > SplashReach + u.T.BodyR || y < u.Y - 4 || y > u.Y + u.T.BodyH + 6)) continue;
            float k = on ? 1 : Math.Clamp(1 - d / (SplashReach + u.T.BodyR), .15f, 1);
            bool engine = Kinds.IsEngine(u.T);
            Hurt(u, a.From, a.Damage * k * (engine ? 2.5f : 1), "кислота");
            if (!engine && !u.Dead) Etch(u, on ? .45f : 0, on ? 1 : .5f, (int)(4 + 10 * k));
        }
        // the horde's ladders and decks it splashed, eaten through in a second or two
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y), iz = (int)MathF.Floor(z);
        for (int vy = iy - OurSplash; vy <= iy + OurSplash; vy++)
            for (int vz = iz - OurSplash; vz <= iz + OurSplash; vz++)
                for (int vx = ix - OurSplash; vx <= ix + OurSplash; vx++)
                {
                    int ex = vx - ix, ey = vy - iy, ez = vz - iz;
                    if (ex * ex + ey * ey + ez * ez > OurSplash * OurSplash || !World.InBounds(vx, vy, vz)) continue;
                    byte m = World.Get(vx, vy, vz);
                    if (m is not (Mat.SiegeLadder or Mat.Deck)) continue;
                    int p = WaterPlanner.Pack(vx, vy, vz);
                    float due = (m == Mat.SiegeLadder ? EatLadder : EatDeck) * (.8f + .4f * (float)Rng.NextDouble());
                    _oursEating[p] = Math.Min(_oursEating.GetValueOrDefault(p, due), due);
                }
        // out in the open, well clear of anything of ours, it lies and runs as acid does
        if (World.InBounds(ix, iy, iz) && World.Get(ix, iy, iz) != Mat.Water && !OursNear(ix, iy, iz)) LayOurAcid(ix, iy, iz, FlaskAcid / 2);

        static bool Solid(byte m) => m != Mat.Air && !Mat.Passable[m];
    }

    /// <summary>Anything of ours built within <see cref="OursClear"/> of a spot.</summary>
    bool OursNear(int x, int y, int z)
    {
        for (int dz = -OursClear; dz <= OursClear; dz += 2)
            for (int dx = -OursClear; dx <= OursClear; dx += 2)
            {
                int cx = x + dx, cz = z + dz;
                if (!World.InBounds(cx, 1, cz)) continue;
                int top = Math.Min(World.SY - 1, y + OursClear * 2);
                for (int cy = Math.Max(1, y - OursClear); cy < top; cy++)
                    if (Mat.IsPlayer[World.Get(cx, cy, cz)]) return true;
            }
        return false;
    }

    /// <summary>Acid of ours let out on open ground, as a flask of the horde's lets it out (<see cref="FlaskBreaks"/>).</summary>
    void LayOurAcid(int ix, int iy, int iz, int amount)
    {
        int laid = 0;
        for (int tries = 0; tries < amount * 4 && laid < amount; tries++)
        {
            float a = (float)(Rng.NextDouble() * MathF.Tau), r = (float)Rng.NextDouble() * (float)Rng.NextDouble() * 5;
            int ax = ix + (int)MathF.Round(MathF.Cos(a) * r), az = iz + (int)MathF.Round(MathF.Sin(a) * r);
            if (!World.InBounds(ax, iy, az)) continue;
            int ay = Math.Min(World.SY - 2, iy + 2);
            if (Blocks(World.Get(ax, ay, az))) continue;
            ay = Trickle(ax, ay, az);
            while (ay > 2 && !Blocks(World.Get(ax, ay - 1, az)) && World.Get(ax, ay - 1, az) != Mat.Acid) ay--;
            while (ay < World.SY - 2 && World.Get(ax, ay, az) == Mat.Acid) ay++;
            byte here = World.Get(ax, ay, az);
            if (here != Mat.Air && !Mat.IsSprig(here) && !Mat.IsLeafy(here)) continue;
            World.Set(ax, ay, az, Mat.Acid);
            laid++;
        }

        static bool Blocks(byte m) => m != Mat.Air && m != Mat.Acid && (!Mat.Passable[m] || Mat.IsLadder[m] || m == Mat.Water || m == Mat.Pitch || m == Mat.Stakes);
    }

    /// <summary>
    /// A flask of fumes breaks: the raiders within two metres and a half cough, the arm down, the pain of it in them, and
    /// step back out of it; the weaker of them run.
    /// </summary>
    void FumesBreak(float x, float y, float z)
    {
        Fumed?.Invoke(x, y, z);
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null || u.Team != Team.Horde || Kinds.IsEngine(u.T) || Sense(u.T) <= 0) continue;
            float dx = u.X - x, dz = u.Z - z, d = MathF.Sqrt(dx * dx + dz * dz);
            if (d > 2.5f * VoxelWorld.PerMetre || MathF.Abs(u.Y - y) > 3 * VoxelWorld.PerMetre) continue;
            u.CoughAt = Time;
            u.Clutch = 9; u.ClutchUntil = Time + 2.2;
            u.Cooldown = Math.Max(u.Cooldown, 1.6f);
            u.Pain = Math.Min(1.6f, u.Pain + .5f * Sense(u.T));
            if (u != Possessed && Time >= u.StaggerUntil)
            {
                float l = MathF.Max(.5f, d);
                u.StaggerUntil = Time + .8;
                u.StagX = dx / l * 10; u.StagZ = dz / l * 10;
            }
            if (u.T.Hp <= 60 && !u.Leader && Rng.NextDouble() < .6) u.Routed = true;
            Coughed?.Invoke(u);
        }
    }

    /// <summary>The horde's works a flask of ours splashed, eaten on: through when their time is up.</summary>
    void TickOurAcid(float dt)
    {
        if (_oursEating.Count == 0) return;
        _oursNow.Clear();
        _oursNow.AddRange(_oursEating.Keys);
        foreach (int p in _oursNow)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            byte m = World.Get(x, y, z);
            if (m is not (Mat.SiegeLadder or Mat.Deck)) { _oursEating.Remove(p); Etched.Remove(p); continue; }
            float left = _oursEating[p] - dt, all = m == Mat.SiegeLadder ? EatLadder : EatDeck;
            if (left > 0) { _oursEating[p] = left; Etched[p] = Math.Clamp(1 - left / all, 0, .99f); continue; }
            _oursEating.Remove(p);
            Etched.Remove(p);
            EatThrough(x, y, z, m);
            OurAte++;
        }
    }
}
