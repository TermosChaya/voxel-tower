using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Captives. A swordsman (or a worker called up) told to take them alive does not kill a raider he has beaten down: the
/// blow that would bring it to a quarter of itself, or kill it, fells it, and it is bound where it fell. A worker with
/// nothing more pressing goes out to it, unless the horde is about it, and leads it into the citadel, where it sits a
/// prisoner — for the priests, or for the horde's envoy to ransom. One left lying alone works itself free after a while
/// and runs; a prisoner left unguarded at night slips out of the citadel and runs too.
/// </summary>
public sealed partial class Sim
{
    /// <summary>Seconds a bound raider lies alone before it has worked itself free.</summary>
    const double WrigglesFree = 150;
    /// <summary>Seconds of a night with nobody of ours guarding the citadel before a prisoner slips out of it.</summary>
    const double SlipsOut = 120;
    /// <summary>How near a raider has to be for our workers to leave a bound one lying, in voxels.</summary>
    const float FetchSafe = 16 * VoxelWorld.PerMetre;
    /// <summary>How near the citadel's door one of ours has to stand to guard the prisoners, in voxels.</summary>
    const float GuardBy = 15 * VoxelWorld.PerMetre;

    /// <summary>One of ours that can take raiders alive: one who fights hand to hand — a swordsman, a worker called up.</summary>
    public static bool TakesAlive(Unit u) => !u.Dead && u.Team == Team.Player && !u.T.Ranged && !u.T.Prays && (u.T.Work < 1 || u.Militia);

    /// <summary>A raider that can be taken alive: not an engine, not the envoy, not one already bound.</summary>
    static bool Takeable(Unit u) => u.Team == Team.Horde && !Kinds.IsEngine(u.T) && u.T != Kinds.Thrall && !u.Envoy && !u.Bound;

    /// <summary>How low a raider has to be beaten before it can be felled and bound: a quarter of itself, three fifths in chains.</summary>
    float TakeAt(Unit u) => u.MaxHp * (Time < u.ChainedUntil ? .6f : .25f);

    /// <summary>The prisoners in the citadel.</summary>
    public int Prisoners()
    {
        int n = 0;
        foreach (var u in Units) if (!u.Dead && u.Inside && u.Team == Team.Horde) n++;
        return n;
    }

    /// <summary>Raiders bound and lying out in the field, or being led in.</summary>
    public int BoundInField()
    {
        int n = 0;
        // the one a priest leads to the altar is no longer waiting for anyone
        foreach (var u in Units) if (!u.Dead && u.Bound && !u.Inside && u.Captor is not { T.Prays: true }) n++;
        return n;
    }

    /// <summary>Struck down and bound where it fell: out of the fight, what it carried dropped, waiting to be led in.</summary>
    void Subdue(Unit u, Unit by)
    {
        if (u.T == Kinds.Thief) ThiefFell(u);
        if (u.Captive != null) { u.Captive.Captor = null; u.Captive = null; }
        u.Bound = true; u.BoundAt = Time;
        u.State = UnitState.Idle; u.Path = null; u.Target = null; u.Prey = null; u.Torch = false; u.Raid = null; u.Routed = false;
        u.Guard = false; u.Escort = null;
        StopCarve(u); StopBuild(u);
        // out of its band: it no longer counts there, and a leader taken leaves the band to choose another
        if (u.Band is { } b) { b.Members.Remove(u); if (b.Leader == u) b.Leader = null; u.Band = null; }
        u.Pastime = Pastime.Bound;
        // bound, and its wounds bound with it: it is wanted alive
        if (u.Wounds != null) u.Wounds.Bleed = 0;
        Say($"{by.T.Name} сбил с ног и связал: {u.Name.ToLowerInvariant()}. Свободный рабочий отведёт пленного в цитадель.", 1, u);
    }

    /// <summary>A raider lying bound: it goes where it is led; left alone long enough, it works itself free and runs.</summary>
    void BoundTick(Unit u, float dt)
    {
        if (u.Captor != null) { Led(u, dt); return; }
        u.State = UnitState.Idle; u.Path = null;
        u.Pastime = Pastime.Bound;
        if (Time - u.BoundAt < WrigglesFree) return;
        u.Bound = false; u.Pastime = Pastime.None;
        u.Routed = true;
        Say($"Связанный {u.T.Name.ToLowerInvariant()} распутался и бежит на запад!", 2, u);
    }

    /// <summary>
    /// A bound raider out in the field, and nobody fetching it: the nearest worker free for it goes out, binds it to
    /// itself and leads it to the citadel's door — unless the horde is about it. True while it is at that.
    /// </summary>
    bool FetchCaptive(Unit u, float dt)
    {
        // leading one in
        if (u.Captive is { } c && c.Team == Team.Horde)
        {
            if (c.Dead || c.Captor != u) { u.Captive = null; return false; }
            u.Why = "ведёт пленного в цитадель";
            if (CellsApart(u.Cell, Gate) <= 2) { Imprison(c); u.Captive = null; u.Path = null; u.Why = ""; return false; }
            if (u.Path == null || u.PathI >= u.Path.Count)
            {
                if (Time < u.NextSearch) { u.State = UnitState.Idle; return true; }
                u.NextSearch = Time + 2;
                if (!GoTo(u, Gate, adjacent: true, budget: 40000)) { c.Captor = null; u.Captive = null; u.Path = null; return false; }
            }
            Walk(u, dt, .8f);
            return true;
        }
        if (u.Fetching is { } f)
        {
            if (f.Dead || !f.Bound || f.Inside || f.Captor != null || HordeNear(f)) { u.Fetching = null; u.Path = null; return false; }
            u.Why = "идёт за пленным";
            if (Dist(u, f) <= u.T.Reach + f.T.BodyR + 4)
            {
                // tied to it, and away
                f.Captor = u; u.Captive = f; u.Fetching = null; u.Path = null; u.NextSearch = 0;
                return true;
            }
            if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
            {
                u.NextSearch = Time + 1.5;
                if (!GoTo(u, f.Cell, adjacent: true, budget: 40000)) { u.Unreachable[f.Cell] = Time + 20; u.Fetching = null; u.Path = null; return false; }
            }
            Walk(u, dt);
            return true;
        }
        if (Time < u.NextFetchLook) return false;
        // each a little apart from the others, without the valley's dice: looking costs the rest of the valley nothing
        u.NextFetchLook = Time + 1.5 + u.Id % 5 * .2;
        Unit best = null; float bd = float.MaxValue;
        foreach (var o in Units)
        {
            if (o.Dead || !o.Bound || o.Inside || o.Captor != null || u.Unreachable.TryGetValue(o.Cell, out double until) && until > Time) continue;
            // one at a time to each
            bool taken = false;
            foreach (var w in Units) if (w != u && !w.Dead && w.Fetching == o) { taken = true; break; }
            if (taken || HordeNear(o)) continue;
            float d = Dist(u, o);
            if (d < bd) { bd = d; best = o; }
        }
        if (best == null) return false;
        u.Fetching = best;
        ReleaseSite(u);
        u.Path = null; u.NextSearch = 0;
        return true;
    }

    /// <summary>Some raider still free and fighting close to a spot: no worker goes out there.</summary>
    bool HordeNear(Unit at)
    {
        foreach (var o in Units)
        {
            if (o.Dead || o.Team != Team.Horde || o.Bound || o.Inside || o.Envoy) continue;
            float dx = o.X - at.X, dz = o.Z - at.Z;
            if (dx * dx + dz * dz < FetchSafe * FetchSafe) return true;
        }
        return false;
    }

    /// <summary>Into the citadel, a prisoner.</summary>
    void Imprison(Unit c)
    {
        c.Captor = null;
        c.Inside = true; c.Path = null; c.State = UnitState.Idle;
        c.Unguarded = 0;
        Say($"Пленный в цитадели: {c.Name.ToLowerInvariant()}. Жрецы могут принести его в жертву (вкладка «Таинства»).", 1, c);
    }

    /// <summary>
    /// A prisoner in the citadel: by night, with nobody of ours about the door or inside to guard it, it works at its
    /// bonds, and in a while slips out and runs.
    /// </summary>
    void Imprisoned(Unit u, float dt)
    {
        u.State = UnitState.Idle; u.Path = null;
        if (!Waves.Active) return;
        if (GuardedKeep()) return;
        u.Unguarded += dt;
        if (u.Unguarded < SlipsOut) return;
        // out of the door, unbound, and away west
        u.Inside = false; u.Bound = false; u.Pastime = Pastime.None; u.Routed = true; u.Unguarded = 0;
        int cell = Spread(Gate, 1 + Rng.Next(4), u.T.Move.Height);
        u.Cell = cell; (u.X, u.Y, u.Z) = Nav.Feet(cell);
        Say($"Пленный {u.T.Name.ToLowerInvariant()} выбрался из цитадели: её никто не сторожил. Перехватите его!", 3, u);
    }

    double _guardLook = -99;
    bool _guarded;

    /// <summary>Some of our fighters by the citadel's door or inside it (not lying there to mend): the prisoners are watched.</summary>
    bool GuardedKeep()
    {
        if (Time - _guardLook < .5) return _guarded;
        _guardLook = Time;
        var (gx, _, gz) = Nav.Feet(Gate);
        _guarded = false;
        foreach (var o in Units)
        {
            // (one lying hurt in the citadel to mend watches nobody)
            if (o.Dead || o.Team != Team.Player || !Fighter(o) || o.Inside && o.Mending) continue;
            float dx = o.X - gx, dz = o.Z - gz;
            if (o.Inside || dx * dx + dz * dz < GuardBy * GuardBy) { _guarded = true; break; }
        }
        return _guarded;
    }

    /// <summary>The prisoners go back to the horde for a ransom: out of the citadel and out of the valley.</summary>
    void Ransom()
    {
        foreach (var u in Units)
            if (!u.Dead && u.Inside && u.Team == Team.Horde) { u.Dead = true; u.DiedAt = -999; u.State = UnitState.Dead; u.KilledBy = "выкуплен ордой"; }
    }

    /// <summary>What the horde pays for its people: four times their heads, and a little over for each.</summary>
    double RansomFor()
    {
        double gold = 0;
        foreach (var u in Units) if (!u.Dead && u.Inside && u.Team == Team.Horde) gold += u.T.Bounty * 4 + 10;
        return gold;
    }
}
