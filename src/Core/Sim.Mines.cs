using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

public enum Holder : byte { None, Ours, Horde }

/// <summary>
/// A mine and who holds it. Whoever stands at its mouth while nobody of the other side is there takes it in a few seconds;
/// while it is ours it pays its gold every minute, and the ore in its vein pays more to whoever breaks it out. A place of
/// power is held the same way: it pays no gold, but prayer in a sanctuary on it is heard thrice as well, and the horde
/// comes to defile it.
/// </summary>
public sealed class Mine
{
    public Landmark Place;
    public Holder Holder;
    /// <summary>−1 the horde's … 0 nobody's … 1 ours: how far the taking has gone.</summary>
    public float Grip;
    /// <summary>Who stands at its mouth now: ours and theirs.</summary>
    public int Ours, Theirs;
    public string Name => Place.Name;
    public int Income => Place.Income;
    public bool Power => Place.Kind == LandmarkKind.Power;
}

public sealed partial class Sim
{
    public readonly List<Mine> Mines = new();
    /// <summary>How near the mouth of a mine one has to stand to hold it, in voxels: ten metres.</summary>
    public const float MineReach = 10 * VoxelWorld.PerMetre;
    /// <summary>Seconds of standing there alone to take a mine nobody holds (twice that from the other side).</summary>
    const float TakeSeconds = 8;
    double _nextMineLook;

    void FindMines()
    {
        foreach (var l in Site.Landmarks)
            if (l.Kind is LandmarkKind.Mine or LandmarkKind.Power) Mines.Add(new Mine { Place = l });
    }

    /// <summary>The horde's camps at the mines it holds when the game begins: goblins and an orc who keep by it.</summary>
    public void SeedMines()
    {
        foreach (var m in Mines)
        {
            if (!m.Place.Guarded) continue;
            m.Holder = Holder.Horde;
            m.Grip = -1;
            foreach (var t in new[] { Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Orc })
            {
                int c = NearestStand(m.Place.StandX + Rng.Next(-28, 29), m.Place.StandZ + Rng.Next(-28, 29), t.Move.Height);
                if (c < 0) continue;
                var u = Spawn(t, c);
                u.Guard = true;
                u.Home = c;
            }
        }
    }

    /// <summary>Pays for the mines we hold, and works out who holds which.</summary>
    void TickMines(float dt)
    {
        foreach (var m in Mines) if (m.Holder == Holder.Ours) Stock.Gold += m.Income / 60.0 * dt;
        if (Time < _nextMineLook) return;
        const float look = .5f;
        _nextMineLook = Time + look;
        foreach (var m in Mines)
        {
            m.Ours = m.Theirs = 0;
            float sx = m.Place.StandX, sz = m.Place.StandZ;
            foreach (var u in Units)
            {
                if (u.Dead || u.Inside || u.Bound || u.Team == Team.Wild) continue;
                float dx = u.X - sx, dz = u.Z - sz;
                if (dx * dx + dz * dz > MineReach * MineReach) continue;
                if (u.Team == Team.Player) m.Ours++; else m.Theirs++;
            }
            float step = look / TakeSeconds;
            if (m.Ours > 0 && m.Theirs == 0) m.Grip = Math.Min(1, m.Grip + step);
            else if (m.Theirs > 0 && m.Ours == 0) m.Grip = Math.Max(-1, m.Grip - step);
            if (m.Grip >= 1 && m.Holder != Holder.Ours)
            {
                m.Holder = Holder.Ours;
                Say(m.Power ? $"Место силы {m.Name} ваше: святилище здесь даёт втрое больше вмешательства."
                    : $"{m.Name} ваш: +{m.Income} золота в минуту, пока его держите.", 1, m.Place.StandX, m.Place.StandZ);
            }
            else if (m.Grip <= -1 && m.Holder != Holder.Horde)
            {
                m.Holder = Holder.Horde;
                Say(m.Power ? $"Орда захватила место силы {m.Name}. Святилище здесь не работает, пока его не отбить." : $"Орда захватила: {m.Name}.", 3, m.Place.StandX, m.Place.StandZ);
            }
            else if (m.Holder == Holder.Ours && m.Grip <= 0) { m.Holder = Holder.None; Say(m.Power ? $"Место силы {m.Name} потеряно." : $"{m.Name} потерян.", 2, m.Place.StandX, m.Place.StandZ); }
            else if (m.Holder == Holder.Horde && m.Grip >= 0)
            {
                m.Holder = Holder.None;
                Say(m.Power ? $"Место силы {m.Name} отбито у орды: постойте у него, чтобы занять." : $"{m.Name} отбит у орды: держитесь у входа, чтобы занять его.", 1, m.Place.StandX, m.Place.StandZ);
            }
        }
    }

    /// <summary>A raider camped at a mine: falls on whoever comes near it, and goes back to the fire afterwards.</summary>
    void GuardTick(Unit u, float dt)
    {
        const float Watch = 14 * VoxelWorld.PerMetre, Leash = 24 * VoxelWorld.PerMetre;
        var foe = Nearest(u, Team.Player, Watch);
        var (hx, _, hz) = u.Home >= 0 ? Nav.Feet(u.Home) : (u.X, u.Y, u.Z);
        float away = MathF.Sqrt((u.X - hx) * (u.X - hx) + (u.Z - hz) * (u.Z - hz));
        if (foe != null && away < Leash)
        {
            if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
            {
                u.NextSearch = Time + 1;
                if (!GoTo(u, foe.Cell, adjacent: true, budget: 4000)) u.Path = null;
            }
            if (u.Path != null && u.PathI < u.Path.Count) Walk(u, dt); else u.State = UnitState.Idle;
            return;
        }
        if (u.Home >= 0 && CellsApart(u.Cell, u.Home) > 2)
        {
            if ((u.Path == null || u.PathI >= u.Path.Count) && Time >= u.NextSearch)
            {
                u.NextSearch = Time + 2;
                if (!GoTo(u, u.Home, budget: 4000)) u.Path = null;
            }
            if (u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt); return; }
        }
        u.State = UnitState.Idle;
    }

    /// <summary>A raider sent to take a mine of ours: walks there round whatever is in the way and stays till it is the horde's.</summary>
    bool RaidTick(Unit u, float dt)
    {
        var m = u.Raid;
        if (m.Holder != Holder.Ours && m.Grip <= -1) { u.Raid = null; u.Path = null; return false; }
        float dx = u.X - m.Place.StandX, dz = u.Z - m.Place.StandZ;
        if (dx * dx + dz * dz < MineReach * MineReach * .3f) { u.State = UnitState.Idle; u.Path = null; return true; }
        if (u.Path == null || u.PathI >= u.Path.Count)
        {
            int c = NearestStand(m.Place.StandX, m.Place.StandZ, u.T.Move.Height);
            if (c < 0 || !GoTo(u, c, adjacent: true, budget: 30000)) { u.Raid = null; return false; }
        }
        Walk(u, dt);
        return true;
    }

    /// <summary>Some of a wave go for the mines we hold, the one nearest to where they come in.</summary>
    void SendRaid(Unit u)
    {
        if (u.T != Kinds.Goblin && u.T != Kinds.Orc || Rng.NextDouble() > .4) return;
        Mine best = null; float bd = float.MaxValue;
        foreach (var m in Mines)
        {
            if (m.Holder != Holder.Ours) continue;
            float d = MathF.Abs(m.Place.StandX - u.X) + MathF.Abs(m.Place.StandZ - u.Z);
            if (d < bd) { bd = d; best = m; }
        }
        u.Raid = best;
    }
}
