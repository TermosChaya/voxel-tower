using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The horde's other sorcerers. A witch puts out the torches on our walls about her — they stay dark a minute, and our
/// archers see no better there than anywhere else at night — and the fires that burn the horde: what burns near her goes
/// out, and the raiders on fire with it. A necromancer gets the horde's fallen up again as bone thralls, three at a time,
/// that fight a minute and fall apart; and near a sanctuary of ours the intervention drains away while it stands there.
/// Consecrated ground is proof against the witch as against a shaman's curse; our archers shoot both before anyone else,
/// and either taken alive is worth as much to the old powers as a shaman.
/// </summary>
public sealed partial class Sim
{
    const double DouseEvery = 2, QuenchEvery = 15, QuenchFor = 60, RaiseEvery = 20, ThrallFor = 60;
    const float DouseReach = 10 * VoxelWorld.PerMetre, QuenchReach = 16 * VoxelWorld.PerMetre, RaiseReach = 10 * VoxelWorld.PerMetre,
        DefileReach = 25 * VoxelWorld.PerMetre;
    /// <summary>The most a necromancer gets up at once.</summary>
    const int RaiseAtOnce = 3;

    /// <summary>A witch put fires out about her, and our torches (where she stands); a necromancer got the fallen up.</summary>
    public event Action<float, float, float> Doused, Darkened, Woken;
    double _quenchSaid = -99, _wokenSaid = -99, _defileSaid = -99;
    readonly List<int> _doused = new();

    /// <summary>A witch's spells, each on its own clock, cast as she goes: she neither stops for them nor fights the less.</summary>
    void WitchTick(Unit u)
    {
        if (u.NextHeal <= 0) { u.NextHeal = Time + 1 + u.Id % 3; u.NextCurse = Time + 4 + u.Id % 6; }
        if (Time >= u.NextHeal)
        {
            u.NextHeal = Time + DouseEvery;
            if (!Warded(u.X, u.Z)) Douse(u);
        }
        if (Time < u.NextCurse) return;
        u.NextCurse = Time + QuenchEvery;
        int n = 0;
        foreach (var f in Fixtures)
        {
            if (f.Kind != FixtureKind.Torch || f.DarkUntil > Time) continue;
            float dx = f.LX - u.X, dz = f.LZ - u.Z;
            if (dx * dx + dz * dz > QuenchReach * QuenchReach || Warded(f.LX, f.LZ)) continue;
            Quench(f, QuenchFor);
            n++;
        }
        if (n == 0) return;
        u.Swing = 1;
        _litAt = -99;
        Darkened?.Invoke(u.X, u.Y, u.Z);
        if (Time - _quenchSaid > 20)
        {
            _quenchSaid = Time;
            Say($"Ворожея погасила наши факелы ({n}). Минуту у стен темно, лучники видят хуже. Святилище и жрецы защищают от неё.", 3, u);
        }
    }

    /// <summary>What burns about a witch goes out, and the raiders burning there with it.</summary>
    void Douse(Unit u)
    {
        _doused.Clear();
        foreach (int p in Burning.Keys)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            if ((x - u.X) * (x - u.X) + (z - u.Z) * (z - u.Z) < DouseReach * DouseReach && MathF.Abs(y - u.Y) < DouseReach) _doused.Add(p);
        }
        foreach (int p in _doused) Burning.Remove(p);
        int n = _doused.Count;
        foreach (var o in Units)
            if (!o.Dead && o.Team == Team.Horde && o.Burn > 0 && Dist(u, o) < DouseReach) { o.Burn = 0; n++; }
        if (n == 0) return;
        u.Swing = 1;
        Doused?.Invoke(u.X, u.Y, u.Z);
    }

    /// <summary>Whether a spot is near a sanctuary of ours.</summary>
    bool NearSanctum(float x, float z, float reach)
    {
        foreach (var zone in Zones)
        {
            if (zone.Kind != Zone.Sanctum) continue;
            float dx = Math.Max(0, Math.Max(zone.X0 - x, x - zone.X1)), dz = Math.Max(0, Math.Max(zone.Z0 - z, z - zone.Z1));
            if (dx * dx + dz * dz < reach * reach) return true;
        }
        return false;
    }

    /// <summary>
    /// A necromancer: near a sanctuary of ours the intervention drains away; every so often the fallen of the horde about
    /// it get up as bone thralls, the nearest three.
    /// </summary>
    void NecromancerTick(Unit u, float dt)
    {
        if (u.NextHeal <= 0) u.NextHeal = Time + 5 + u.Id % 5;
        if (Favour > 0 && NearSanctum(u.X, u.Z, DefileReach))
        {
            Favour = Math.Max(0, Favour - dt);
            if (Time - _defileSaid > 30)
            {
                _defileSaid = Time;
                Say("Некромант у святилища: вмешательство утекает, пока он там стоит.", 3, u);
            }
        }
        if (Time < u.NextHeal) return;
        var fallen = new List<Unit>();
        foreach (var c in Units)
        {
            if (!c.Dead || c.Team != Team.Horde || Kinds.IsEngine(c.T) || c.T == Kinds.Thrall || c.DiedAt < 0 || Time - c.DiedAt > 30
                || c.KilledBy == GivenToPowers || Dist(u, c) > RaiseReach) continue;
            fallen.Add(c);
        }
        if (fallen.Count == 0) { u.NextHeal = Time + 2; return; }
        u.NextHeal = Time + RaiseEvery;
        fallen.Sort((a, b) => Dist(u, a).CompareTo(Dist(u, b)));
        int n = 0;
        foreach (var c in fallen)
        {
            if (n >= RaiseAtOnce) break;
            int cell = NearestStand((int)c.X, (int)c.Z, Kinds.Thrall.Move.Height);
            if (cell < 0) continue;
            var t = Spawn(Kinds.Thrall, cell);
            t.CrumbleAt = Time + ThrallFor;
            t.Facing = c.Facing;
            // the body it rose from is gone from where it lay
            c.DiedAt = -999;
            n++;
        }
        if (n == 0) return;
        u.Swing = 1;
        Woken?.Invoke(u.X, u.Y, u.Z);
        if (Time - _wokenSaid > 25)
        {
            _wokenSaid = Time;
            Say($"Некромант поднял павших ({n}). Мертвецы рассыплются через минуту.", 3, u);
        }
    }
}
