using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Our engines, learnt in the lore's fifth branch of the troops and taken on at the citadel's muster for gold and the
/// stores' timber: a stone-thrower (a mangonel), a ballista and a trebuchet. Each comes out of the citadel with its
/// crew, who wind it, load it and push it. Sent somewhere it is packed up first, rolled there slowly — the slower for a
/// short crew, and not at all with none — and set up again where it stops; a trebuchet takes the longest about both,
/// and throws farthest. Set up, it looks for a mark by itself: the horde's engines first (a siege tower, a ram, a
/// catapult), a hill giant, those on ladders and decks, else the thickest knot of a band, never where ours stand by it;
/// or what the player sent it at. A stone comes down as a giant's boulder does (Sim.Giants.cs): it breaks the horde's
/// works and engines it strikes and ploughs on through a band, rolling, passing our people and our walls by; with the
/// pitch kegs every other throw is a keg of burning pitch that splashes and sets alight what it comes down on. A
/// ballista's bolt flies straight, goes on through a file of raiders, and staggers a giant where it stands. With fewer
/// hands it winds slower; with none it stands, till a worker or a soldier of ours near it takes the crew's place. The
/// horde answers it: its catapult throws at our engines within its reach, a hill giant hurls at them first, raiders
/// near one go for it and its crew. An engine burns as the horde's do, and arrows hardly hurt it.
/// </summary>
public sealed partial class Sim
{
    public const string MangonelCause = "камень камнемёта", TrebuchetCause = "камень требушета", SpearCause = "копьё баллисты";

    /// <summary>Whether a flying stone is one of our engines'.</summary>
    static bool OurStone(string cause) => cause is MangonelCause or TrebuchetCause;

    /// <summary>Stones, kegs and bolts our engines have let fly since the start; raiders their bolts went through.</summary>
    public int EngineShots, SpearsThrough;
    /// <summary>An engine of ours has just loosed: its winding for the next shot is <see cref="Unit.Cooldown"/> now.</summary>
    public Action<Unit> EngineLoosed;
    double _crewlessSaid = -99, _engineMarkSaid = -99;

    /// <summary>How long an engine takes to pack up for the road, and to set up where it stops, seconds (with a full crew).</summary>
    public static float RigTime(UnitType t) => t == Kinds.Trebuchet ? 8 : 1.5f;
    /// <summary>How near an engine throws no closer than, voxels: a stone falls in an arc, a bolt flies straight.</summary>
    static float NearestThrow(UnitType t) => t == Kinds.Trebuchet ? 20 * VoxelWorld.PerMetre : t == Kinds.Mangonel ? 8 * VoxelWorld.PerMetre : 2 * VoxelWorld.PerMetre;
    /// <summary>How far an engine of ours reaches: a stone-thrower a sixth farther with the heavy stones.</summary>
    public float EngineRange(Unit u) => u.T.Range * (u.T == Kinds.Mangonel && Knows("heavystones") ? 1.15f : 1);
    /// <summary>How long an engine takes to wind and load, seconds: a quarter less with the windlasses.</summary>
    float EngineReload(Unit u) => u.T.AttackTime * (Knows("windlass") ? .75f : 1);
    /// <summary>How near its engine one of the crew works it from, voxels, past the engine's own size.</summary>
    const float CrewReach = 4 * VoxelWorld.PerMetre;

    /// <summary>The crew of an engine: ours keeping by it, wherever they are now.</summary>
    public int CrewOf(Unit e)
    {
        int n = 0;
        foreach (var o in Units) if (!o.Dead && o.Escort == e && o.Team == Team.Player) n++;
        return n;
    }

    /// <summary>The crew at its engine: by it, on their feet and out of the citadel.</summary>
    public int CrewAt(Unit e)
    {
        int n = 0;
        foreach (var o in Units)
            if (!o.Dead && o.Escort == e && o.Team == Team.Player && !o.Inside && !o.Down && o.Airborne == null && Dist(o, e) < e.T.BodyR + CrewReach) n++;
        return n;
    }

    /// <summary>Whether an engine stands where it was sent (or was sent nowhere).</summary>
    bool AtPlace(Unit u) => u.Dest < 0 || Math.Abs(Nav.I(u.Cell) - Nav.I(u.Dest)) <= 1 && Math.Abs(Nav.K(u.Cell) - Nav.K(u.Dest)) <= 1 && Math.Abs(Nav.Y(u.Cell) - Nav.Y(u.Dest)) <= 2;

    /// <summary>An engine of ours each tick: packed, rolled, set up, and at its work.</summary>
    void OurEngineTick(Unit u, float dt)
    {
        u.Target = null;
        // read back from a save: set up if it stands where it was sent
        if (u.Rig < 0) u.Rig = AtPlace(u) ? 1 : 0;
        if (CrewOf(u) < u.T.Crew && Time >= u.NextLook) { u.NextLook = Time + 2; ManEngine(u); }
        int at = CrewAt(u);
        float hands = Math.Min(1, at / (float)Math.Max(1, u.T.Crew));
        if (!AtPlace(u))
        {
            // packed up first, then pushed along by its crew
            if (u.Rig > 0)
            {
                u.Path = null;
                if (at == 0) { u.State = UnitState.Idle; u.Why = "без расчёта"; return; }
                u.Rig = Math.Max(0, u.Rig - dt / RigTime(u.T) * hands);
                u.State = UnitState.Work; u.Why = "разбирают, чтобы катить";
                return;
            }
            if (at == 0) { u.State = UnitState.Idle; u.Path = null; u.Why = "без расчёта: катить некому"; return; }
            if (u.Path == null || u.PathI >= u.Path.Count)
            {
                if (Time < u.NextSearch) { u.State = UnitState.Idle; return; }
                u.NextSearch = Time + 2;
                if (!GoTo(u, u.Dest, adjacent: true, budget: 40000))
                {
                    // there is no rolling it there: it is set up where it stands
                    u.Dest = -1;
                    Say($"{u.T.Name}: туда не прокатить. Машина остаётся, где стоит.", 2, u);
                    return;
                }
            }
            u.Why = "катят";
            Walk(u, dt * (.5f + .5f * hands));
            return;
        }
        u.Path = null;
        if (u.Rig < 1)
        {
            if (at == 0) { u.State = UnitState.Idle; u.Why = "без расчёта"; return; }
            // set down where it stops: what it stands on bears its weight, or gives way under it
            if (u.Rig == 0) SetDown(u);
            u.Rig = Math.Min(1, u.Rig + dt / RigTime(u.T) * hands);
            u.State = UnitState.Work; u.Why = "собирают";
            return;
        }
        if (at == 0)
        {
            u.State = UnitState.Idle; u.Why = "без расчёта";
            if (CrewOf(u) == 0 && Time - _crewlessSaid > 30)
            {
                _crewlessSaid = Time;
                Say($"{u.T.Name} без расчёта и стоит. Пошлите к нему рабочего или солдата: он встанет к машине.", 2, u);
            }
            return;
        }
        // a short crew winds the slower: of the time the step took off, a share is given back
        if (u.Cooldown > 0 && hands < 1) u.Cooldown += dt * (1 - hands);
        var mark = EngineMark(u);
        if (mark == null) { u.State = UnitState.Idle; u.Why = "ждёт цели"; return; }
        u.Facing = MathF.Atan2(mark.Z - u.Z, mark.X - u.X);
        u.State = UnitState.Siege;
        if (u.Cooldown > 0) { u.Why = "заряжают"; return; }
        if (u.T == Kinds.Ballista) Spear(u, mark);
        else Lob(u, mark);
        u.Cooldown = EngineReload(u) * (.95f + .1f * (float)Rng.NextDouble());
        u.Why = "";
        EngineLoosed?.Invoke(u);
    }

    /// <summary>
    /// Ours near an engine short of its crew take their places: anyone idle within 25 metres, or anyone the player sent
    /// to stand by it — not a priest, a hound, a rider or another engine's crew.
    /// </summary>
    void ManEngine(Unit e)
    {
        int need = e.T.Crew - CrewOf(e);
        while (need > 0)
        {
            Unit best = null;
            float bd = 25 * VoxelWorld.PerMetre;
            foreach (var o in Units)
            {
                if (o.Dead || o.Inside || o.Down || o.Team != Team.Player || o.Escort != null || Kinds.IsEngine(o.T) || o.T.Prays || o.T.NoHands || o.T.Mounted
                    || o.T == Kinds.Risen || o.Captor != null || o.PinKg > 0 || o.Airborne != null) continue;
                float d = Dist(o, e);
                bool sent = o.Hold && d < e.T.BodyR + CrewReach + VoxelWorld.PerMetre;
                bool idle = !o.Hold && o.Site < 0 && o.State is UnitState.Idle or UnitState.Walk && o.Target == null && o.Prey == null;
                if (!sent && !idle || d >= bd) continue;
                bd = d; best = o;
            }
            if (best == null) return;
            best.Escort = e;
            best.Hold = false;
            ReleaseSite(best);
            StopCarve(best); StopBuild(best);
            best.Path = null;
            Say($"{best.T.Name} встал к машине: {e.T.Name.ToLowerInvariant()}.", 1, best);
            need--;
        }
    }

    /// <summary>
    /// One of an engine's crew each tick, before the rest of what a pawn does: by its engine, at its own place about it,
    /// winding and loading while it is, and with it as it rolls. False when it has no engine (it goes about as a worker)
    /// or the horde is on it (it runs as anyone does, and comes back).
    /// </summary>
    bool CrewTick(Unit u, float dt)
    {
        var e = u.Escort;
        if (e == null || e.Team != Team.Player) return false;
        if (e.Dead) { u.Escort = null; return false; }
        if (e.Inside) { u.State = UnitState.Idle; return false; }
        if (Nearest(u, Team.Horde, 6 * VoxelWorld.PerMetre) is { } close && !close.Routed) return false;
        // its place: about the engine's back, one a side
        int slot = 0, n = 0;
        foreach (var o in Units) if (!o.Dead && o.Escort == e) { if (o == u) slot = n; n++; }
        float back = e.Facing + MathF.PI, spread = (slot - (n - 1) * .5f) * .9f;
        float r = e.T.BodyR + 1.2f * VoxelWorld.PerMetre;
        float px = e.X + MathF.Cos(back + spread) * r, pz = e.Z + MathF.Sin(back + spread) * r;
        float far = MathF.Sqrt((px - u.X) * (px - u.X) + (pz - u.Z) * (pz - u.Z));
        if (far > 1.5f * VoxelWorld.PerMetre && Dist(u, e) > e.T.BodyR + CrewReach * .6f || far > 3 * VoxelWorld.PerMetre)
        {
            if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
            {
                u.NextSearch = Time + (e.Path != null ? .7 : 2);
                int spot = NearestStand((int)px, (int)pz, u.T.Move.Height);
                if (spot < 0 || !GoTo(u, spot, adjacent: true, budget: 6000)) { u.Path = null; u.State = UnitState.Idle; return true; }
            }
            Walk(u, dt);
            return true;
        }
        u.Path = null;
        u.Facing = MathF.Atan2(e.Z - u.Z, e.X - u.X);
        // at the winch while the engine is wound, set up or packed up; else standing by it
        bool busy = e.State == UnitState.Work || e.State == UnitState.Siege && e.Cooldown > 0;
        u.State = busy ? UnitState.Work : UnitState.Idle;
        if (busy) { u.Beat = (u.Beat + dt * 1.2f) % 1; if (u.Beat < dt * 1.2f) u.Swing = 1; }
        u.Pastime = Pastime.None;
        return true;
    }

    /// <summary>
    /// What an engine throws at now: what the player sent it at, if it can reach it; else the horde's engines first, a
    /// hill giant (a ballista's first mark), those on a ladder or a deck or building one, then the thickest knot of a
    /// band — never one with ours by it (a stone breaks up about it, a keg splashes) — within its reach and past its
    /// nearest; a ballista only what it sees.
    /// </summary>
    Unit EngineMark(Unit u)
    {
        float far = EngineRange(u), near = NearestThrow(u.T);
        bool straight = u.T == Kinds.Ballista;
        if (u.Prey is { Dead: false, Inside: false } prey && prey.Team == Team.Horde)
        {
            float pd = Flat(u, prey);
            if (pd <= far && pd >= near && (!straight || Sight(u, prey))) return prey;
        }
        // the mark is kept a moment, not weighed again every tick
        if (u.HurlAt is { Dead: false, Inside: false, Bound: false } kept && Time < u.NextHurl)
        {
            float kd = Flat(u, kept);
            if (kd <= far && kd >= near) return kept;
        }
        Unit best = null;
        float bestScore = 0;
        const float Knot = 4 * VoxelWorld.PerMetre;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Team != Team.Horde || o.Bound || o.Envoy || o.Lurking || o.Airborne != null) continue;
            float d = Flat(u, o);
            if (d > far || d < near) continue;
            float score;
            if (Kinds.IsEngine(o.T)) score = 1000 + (o.T == Kinds.SiegeTower ? 300 : o.T == Kinds.Ram ? 250 : o.T == Kinds.Catapult ? 200 : 50);
            else if (o.T == Kinds.HillGiant) score = straight ? 1400 : 700;
            else
            {
                score = Climbing(o) || o.State == UnitState.Work && o.Building is Works.Ladder or Works.Bridge ? 400 : 0;
                foreach (var p in Units)
                    if (!p.Dead && !p.Inside && p.Team == Team.Horde && MathF.Abs(p.X - o.X) < Knot && MathF.Abs(p.Z - o.Z) < Knot && MathF.Abs(p.Y - o.Y) < 16) score += 100;
            }
            score -= d * .05f;
            if (score <= bestScore) continue;
            if (straight ? !Sight(u, o) : !Clear(o, Kinds.IsEngine(o.T) ? 2 : 3)) continue;
            bestScore = score; best = o;
        }
        u.HurlAt = best;
        u.NextHurl = Time + 3;
        if (best != null && Kinds.IsEngine(best.T) && Time - _engineMarkSaid > 60)
        {
            _engineMarkSaid = Time;
            Say($"{u.T.Name} бьёт по машине орды: {best.T.Name.ToLowerInvariant()}.", 1, u);
        }
        return best;
    }

    /// <summary>The distance on the ground between two, voxels.</summary>
    static float Flat(Unit a, Unit b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>None of ours within so many metres of a raider: a stone or a keg may come down on it.</summary>
    bool Clear(Unit t, float metres)
    {
        float r = metres * VoxelWorld.PerMetre;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Team != Team.Player) continue;
            if (MathF.Abs(o.X - t.X) < r + o.T.BodyR && MathF.Abs(o.Z - t.Z) < r + o.T.BodyR && MathF.Abs(o.Y - t.Y) < 3 * VoxelWorld.PerMetre) return false;
        }
        return true;
    }

    /// <summary>
    /// A stone (or, with the pitch kegs, every other throw a keg of burning pitch) off the end of the arm, high on its
    /// arc and down on the mark, give or take a metre or two the farther it goes: a trebuchet's bigger and higher.
    /// </summary>
    void Lob(Unit u, Unit t)
    {
        bool treb = u.T == Kinds.Trebuchet;
        float fx = MathF.Cos(u.Facing), fz = MathF.Sin(u.Facing);
        float x0 = u.X + fx * 3, y0 = u.Y + u.T.BodyH * (treb ? 1.1f : 1) + 2, z0 = u.Z + fz * 3;
        float tx = t.X, ty = t.Y + t.T.BodyH * .4f, tz = t.Z;
        // a slow mark is led a little: where it will be when the stone comes down
        float d = MathF.Sqrt((tx - x0) * (tx - x0) + (tz - z0) * (tz - z0));
        float flight = (treb ? 2.2f : 1.5f) + d / (30 * VoxelWorld.PerMetre);
        if (t.Path != null && t.PathI < t.Path.Count && !Kinds.IsEngine(t.T))
        {
            var (nx, _, nz) = Nav.Feet(t.Path[t.PathI]);
            float sx = nx - t.X, sz = nz - t.Z, sl = MathF.Sqrt(sx * sx + sz * sz);
            if (sl > .5f) { float lead = MathF.Min(t.T.Move.Speed * NavGrid.CS * flight, 3 * VoxelWorld.PerMetre); tx += sx / sl * lead; tz += sz / sl * lead; }
        }
        float spread = VoxelWorld.PerMetre * (.3f + d / EngineRange(u) * (treb ? 1.4f : 1.1f));
        float a = (float)(Rng.NextDouble() * MathF.Tau), rr = (float)Rng.NextDouble() * spread;
        tx += MathF.Cos(a) * rr; tz += MathF.Sin(a) * rr;
        u.Swing = 1;
        EngineShots++;
        if (Knows("pitchkegs") && (u.Strokes++ & 1) == 0)
        {
            Arrows.Add(new Arrow
            {
                X0 = x0, Y0 = y0, Z0 = z0, X1 = tx, Y1 = ty, Z1 = tz, Dur = flight, Target = t, From = u, Hits = Rng.NextDouble() < .35,
                Damage = 30, Kind = Shot.Keg, Arc = .45f,
            });
            Heaved?.Invoke(x0, y0, z0, 30);
            return;
        }
        float r = (treb ? 3.4f : 2.5f) * (Knows("heavystones") ? 1.1f : 1);
        float vy = ArcUp(y0, ty, ref flight);
        float vx = (tx - x0) / flight, vz = (tz - z0) / flight;
        var f = Hurl(Ball(r, Mat.Rock), x0, y0, z0, vx, vy, vz, treb ? TrebuchetCause : MangonelCause);
        if (f == null) return;
        f.By = u;
        Heaved?.Invoke(x0, y0, z0, f.Voxels.Count);
    }

    /// <summary>A ballista's bolt: straight at the mark, struck as it is aimed nine times in ten.</summary>
    void Spear(Unit u, Unit t)
    {
        u.Swing = 1;
        EngineShots++;
        float d = Dist(u, t);
        Arrows.Add(new Arrow
        {
            X0 = u.X, Y0 = u.Y + u.T.BodyH * .8f, Z0 = u.Z,
            X1 = t.X + (float)(Rng.NextDouble() - .5) * 2, Y1 = t.Y + t.T.BodyH * .55f, Z1 = t.Z + (float)(Rng.NextDouble() - .5) * 2,
            Dur = Math.Max(.08f, d / SpeedOf(Shot.Spear)), Target = t, From = u, Hits = Rng.NextDouble() < .9,
            Damage = u.T.Damage * (.9f + (float)Rng.NextDouble() * .2f), Kind = Shot.Spear, Arc = ArcOf(Shot.Spear),
        });
        Heaved?.Invoke(u.X, u.Y + u.T.BodyH, u.Z, 20);
    }

    /// <summary>
    /// A ballista's bolt through the one it struck: on along its flight into the next raider behind, within eight
    /// metres and near its line, with three quarters of its force, up to four in all. A great one it staggers where it
    /// stands, its throw and its blow put off.
    /// </summary>
    void SpearOn(Arrow a, Unit t)
    {
        if (t.T.BodyH >= 24)
        {
            t.Path = null;
            t.StaggerUntil = Math.Max(t.StaggerUntil, Time + 1.2);
            t.Cooldown = Math.Max(t.Cooldown, 3);
            t.NextHurl = Math.Max(t.NextHurl, Time + 4);
            return;
        }
        if (a.Hops >= 3) return;
        float dx = a.X1 - a.X0, dz = a.Z1 - a.Z0, l = MathF.Sqrt(dx * dx + dz * dz);
        if (l < 1) return;
        float ux = dx / l, uz = dz / l;
        Unit next = null;
        float best = 8 * VoxelWorld.PerMetre;
        foreach (var o in Units)
        {
            if (o == t || o.Dead || o.Team != Team.Horde || o.Inside || o.Bound || o.Envoy) continue;
            float ox = o.X - t.X, oz = o.Z - t.Z, along = ox * ux + oz * uz;
            if (along <= 0 || along > best || MathF.Abs(ox * uz - oz * ux) > o.T.BodyR + 2.5f || MathF.Abs(o.Y - t.Y) > 14) continue;
            best = along; next = o;
        }
        if (next == null) return;
        _through.Add(new Arrow
        {
            X0 = t.X, Y0 = t.Y + t.T.BodyH * .55f, Z0 = t.Z, X1 = next.X, Y1 = next.Y + next.T.BodyH * .55f, Z1 = next.Z,
            Dur = Math.Max(.03f, best / SpeedOf(Shot.Spear)), Target = next, From = a.From, Hits = true, Damage = a.Damage * .75f,
            Kind = Shot.Spear, Arc = 0, Hops = a.Hops + 1,
        });
        SpearsThrough++;
    }

    /// <summary>
    /// An engine set down where it stopped: timber over a gap under it bears its weight or gives way (Sim.Crush.cs) —
    /// a trebuchet weighs most.
    /// </summary>
    void SetDown(Unit u)
    {
        var under = new List<(int x, int y, int z)>();
        int r = (int)u.T.BodyR, y = (int)MathF.Floor(u.Y) - 1;
        for (int z = (int)u.Z - r; z <= (int)u.Z + r; z += 2)
            for (int x = (int)u.X - r; x <= (int)u.X + r; x += 2)
                if (World.InBounds(x, y, z)) under.Add((x, y, z));
        float heft = u.T == Kinds.Trebuchet ? 2400 : u.T == Kinds.Mangonel ? 900 : 500;
        if (GivesWay(under, heft, "машина провалилась") > 0) Settle(u);
    }

    /// <summary>
    /// The horde's answer to an engine of ours: the nearest one within so many voxels of a raider, set up or rolling,
    /// out of the citadel; or null.
    /// </summary>
    Unit OurEngineNear(Unit u, float within)
    {
        Unit best = null;
        float bd = within;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || !Kinds.IsOurEngine(o.T)) continue;
            float d = Flat(u, o);
            if (d < bd) { bd = d; best = o; }
        }
        return best;
    }

    /// <summary>
    /// A raider with nobody of ours at hand goes for an engine of ours within fourteen metres, and its crew: the horde
    /// knows what breaks its works. True while it is on its way.
    /// </summary>
    bool GoForEngine(Unit u, float dt)
    {
        if (u.T.Move.Build >= 1 || Kinds.IsCaster(u.T) || u.Charge || u.Loot > 0) return false;
        var e = OurEngineNear(u, 14 * VoxelWorld.PerMetre);
        if (e == null || !Sight(u, e)) return false;
        if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
        {
            u.NextSearch = Time + 1.5;
            if (!GoTo(u, e.Cell, adjacent: true, budget: 4000)) return false;
        }
        u.State = UnitState.Walk;
        Walk(u, dt);
        return true;
    }
}
