using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Faith. Priests pray by day in a sanctuary — an area the player sets aside with the area brush — and what the old
/// powers hear of it is the Esoteric Intervention, which the player spends on spells. A sanctuary on a place of power we
/// hold is heard thrice as well; one on a place the horde has defiled is not heard at all. At night, while the horde is
/// in the valley, the priests shelter in the citadel like anyone who doesn't fight.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The Esoteric Intervention: what the old powers are moved to do for the valley; the player spends it on spells.</summary>
    public double Favour;
    /// <summary>Intervention one priest's prayer brings in a minute, in a sanctuary on plain ground.</summary>
    public const double PrayerPerMinute = 1;
    /// <summary>How much better prayer is heard on a place of power we hold.</summary>
    public const double PowerShare = 3;
    /// <summary>How near the heart of a place of power a sanctuary has to reach to lie on it, in voxels: four metres.</summary>
    const int OnPower = 4 * VoxelWorld.PerMetre;

    /// <summary>The place of power a sanctuary lies on, or null.</summary>
    public Mine PowerUnder(Zone z)
    {
        foreach (var m in Mines)
            if (m.Power && m.Place.StandX >= z.X0 - OnPower && m.Place.StandX <= z.X1 + OnPower && m.Place.StandZ >= z.Z0 - OnPower && m.Place.StandZ <= z.Z1 + OnPower)
                return m;
        return null;
    }

    /// <summary>The place of power an area would lie on, before it is set aside (for the tool's tag).</summary>
    public Mine PowerUnder(int x0, int z0, int x1, int z1) => PowerUnder(new Zone { X0 = Math.Min(x0, x1), Z0 = Math.Min(z0, z1), X1 = Math.Max(x0, x1), Z1 = Math.Max(z0, z1) });

    /// <summary>How well prayer in a sanctuary is heard: once on plain ground, thrice on a place of power we hold, not at all on one defiled.</summary>
    public double Hearing(Zone z)
    {
        var m = PowerUnder(z);
        return m == null || m.Holder == Holder.None ? 1 : m.Holder == Holder.Ours ? PowerShare : 0;
    }

    /// <summary>Intervention the priests bring in now, a minute: what they pray in the sanctuaries they stand in.</summary>
    public double PrayerNow()
    {
        double n = 0;
        foreach (var u in Units)
            if (!u.Dead && u.T.Prays && u.Pastime == Pastime.Pray && u.Shrine >= 0 && SanctumAt(u.Shrine) is { } z) n += PrayerPerMinute * Hearing(z) * PrayerMore;
        return n;
    }

    /// <summary>The sanctuary a cell lies in (its middle), or null: one standing anywhere in that cell stands in it.</summary>
    Zone SanctumAt(int cell)
    {
        var (x, _, z) = Nav.Feet(cell);
        foreach (var zone in Zones) if (zone.Kind == Zone.Sanctum && zone.Contains((int)x, (int)z)) return zone;
        return null;
    }

    /// <summary>
    /// A priest's day: to a place in the sanctuary where prayer is heard best and not yet taken by another, and prayer
    /// there; by night, shelter. It does no other work, binds no wounds and fights no fires.
    /// </summary>
    void PriestTick(Unit u, float dt)
    {
        // a sacrifice asked for comes before prayer
        if (u.Rite != null) { RiteTick(u, dt); return; }
        // while the horde is in the valley, into the citadel (or a shelter) and stay there till the morning
        if (Waves.Active)
        {
            u.Shrine = -1;
            if (u.Pastime == Pastime.Pray) Busy(u);
            if (!TakeShelter(u, dt)) { u.Why = "ночь: некуда укрыться"; Idle(u, dt, stay: true); }
            return;
        }
        if (u.Shrine >= 0 && SanctumAt(u.Shrine) == null) u.Shrine = -1;
        if (u.Shrine < 0)
        {
            if (Time < u.NextSearch) { Idle(u, dt, stay: false); return; }
            u.NextSearch = Time + 2;
            u.Shrine = ShrineFor(u);
            if (u.Shrine < 0)
            {
                u.Why = Zones.Exists(z => z.Kind == Zone.Sanctum) ? "в святилище нет места" : "нет святилища: отметьте его делянкой (8)";
                if (u.Pastime == Pastime.Pray) Busy(u);
                Idle(u, dt, stay: false);
                return;
            }
            u.Path = null;
            if (u.Pastime == Pastime.Pray) Busy(u);
        }
        if (u.Cell != u.Shrine)
        {
            u.Why = "идёт молиться";
            if (u.Path == null || u.PathI >= u.Path.Count)
            {
                if (Time < u.NextSearch) { u.State = UnitState.Idle; return; }
                u.NextSearch = Time + 2;
                if (!GoTo(u, u.Shrine, budget: 40000)) { u.Unreachable[u.Shrine] = Time + 20; u.Shrine = -1; u.Path = null; return; }
            }
            Walk(u, dt);
            return;
        }
        // at its place: prayer, facing the heart of the place of power (or the middle of the sanctuary)
        u.Path = null;
        u.State = UnitState.Idle;
        if (u.Pastime != Pastime.Pray) { Busy(u); u.Pastime = Pastime.Pray; u.PastimeUntil = double.MaxValue; }
        var z = SanctumAt(u.Shrine);
        var m = PowerUnder(z);
        float fx = m != null ? m.Place.X : (z.X0 + z.X1) / 2f, fz = m != null ? m.Place.Z : (z.Z0 + z.Z1) / 2f;
        if (MathF.Abs(fx - u.X) + MathF.Abs(fz - u.Z) > 6) u.Facing = MathF.Atan2(fz - u.Z, fx - u.X);
        double hear = Hearing(z);
        GainFavour(PrayerPerMinute / 60 * hear * dt * PrayerMore);
        u.Why = hear == 0 ? $"молится, но место осквернено: {m.Name}" : hear > 1 ? $"молится на месте силы: {m.Name}" : "молится в святилище";
    }

    /// <summary>
    /// Where a priest goes to pray: a free place in the sanctuary heard best (the nearest of those alike), that it can get
    /// to; −1 when there is none.
    /// </summary>
    int ShrineFor(Unit u)
    {
        var taken = new HashSet<int>();
        foreach (var o in Units) if (!o.Dead && o != u && o.Shrine >= 0) taken.Add(o.Shrine);
        int best = -1; double bs = double.MinValue;
        foreach (var z in Zones)
        {
            if (z.Kind != Zone.Sanctum) continue;
            double hear = Hearing(z);
            foreach (int c in ShelterCells(z.X0, z.Z0, z.X1, z.Z1, u.T.Move.Height))
            {
                if (taken.Contains(c) || u.Unreachable.TryGetValue(c, out double until) && until > Time) continue;
                var (x, _, zz) = Nav.Feet(c);
                double d = MathF.Sqrt((x - u.X) * (x - u.X) + (zz - u.Z) * (zz - u.Z));
                // a sanctuary heard thrice as well is worth a long walk
                double score = hear * 1000 - d;
                if (score > bs) { bs = score; best = c; }
            }
        }
        return best;
    }

    /// <summary>How many priests a sanctuary has room for, a man to a cell, and the place of power it lies on (for the tool's tag).</summary>
    public (int room, Mine power) SanctumRoom(int x0, int z0, int x1, int z1) => (ShelterRoom(x0, z0, x1, z1), PowerUnder(x0, z0, x1, z1));
}
