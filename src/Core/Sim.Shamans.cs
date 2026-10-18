using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>A patch of our walls under a shaman's curse: the horde hacks it twice as fast, till when.</summary>
public sealed class Curse { public float X, Y, Z, R; public double Until; }

/// <summary>
/// The horde's shamans: goblins with a skull for a face and an ember in the horn of their staff. One goes with its band
/// and keeps it on its feet — every few seconds those hurt about it mend a little and bleed the less — and now and then
/// it lays a curse on the nearest piece of our walls: for a while the horde hacks that twice as fast. Consecrated ground
/// is proof against it: no curse takes near a sanctuary, nor near a priest of ours. Our archers shoot a shaman before
/// anyone else, and one taken alive is worth the most to the old powers.
/// </summary>
public sealed partial class Sim
{
    public readonly List<Curse> Curses = new();
    const double HealEvery = 8, CurseEvery = 20, CurseFor = 40;
    const float HealBy = 25, HealReach = 6 * VoxelWorld.PerMetre, CurseReach = 20 * VoxelWorld.PerMetre, WardReach = 20 * VoxelWorld.PerMetre,
        CurseR = 2.5f * VoxelWorld.PerMetre;

    /// <summary>A shaman mended those about it; it laid a curse on a wall (where).</summary>
    public event Action<float, float, float> Healed, Hexed;

    /// <summary>A shaman's spells, each on its own clock, cast as it goes: it neither stops for them nor fights the less.</summary>
    void ShamanTick(Unit u)
    {
        if (u.NextHeal <= 0) { u.NextHeal = Time + 3 + u.Id % 4; u.NextCurse = Time + 8 + u.Id % 7; }
        if (Time >= u.NextHeal)
        {
            u.NextHeal = Time + HealEvery;
            int n = 0;
            foreach (var o in Units)
            {
                if (o.Dead || o.Inside || o.Bound || o.Team != Team.Horde || Kinds.IsEngine(o.T) || o.Hp >= o.MaxHp || Dist(u, o) > HealReach) continue;
                o.Hp = Math.Min(o.MaxHp, o.Hp + HealBy);
                if (o.Wounds != null) o.Wounds.Bleed *= .5f;
                n++;
            }
            if (n > 0) { u.Swing = 1; Healed?.Invoke(u.X, u.Y, u.Z); }
        }
        if (Time < u.NextCurse) return;
        u.NextCurse = Time + CurseEvery;
        if (CurseTarget(u) is not var (x, y, z) || Curses.Exists(c => MathF.Abs(c.X - x) + MathF.Abs(c.Z - z) < CurseR)) return;
        u.Swing = 1;
        if (Warded(x, z))
        {
            if (Time - _wardSaid > 20) { _wardSaid = Time; Say("Шаман наводит порчу на стену, но освящённая земля её не принимает.", 1, x, z); }
            return;
        }
        Curses.Add(new Curse { X = x, Y = y, Z = z, R = CurseR, Until = Time + CurseFor });
        Hexed?.Invoke(x, y, z);
        if (Time - _curseSaid > 15) { _curseSaid = Time; Say("Шаман навёл порчу на стену: орда рубит её вдвое быстрее. Святилище рядом от порчи бережёт.", 3, x, z); }
    }

    double _wardSaid = -99, _curseSaid = -99;

    /// <summary>The top of the piece of our walls nearest a shaman, within its reach, or nothing.</summary>
    (float x, float y, float z)? CurseTarget(Unit u)
    {
        int ux = (int)u.X, uz = (int)u.Z, r = (int)CurseReach;
        float bd = float.MaxValue; (float, float, float)? best = null;
        for (int z = Math.Max(1, uz - r); z <= Math.Min(World.SZ - 2, uz + r); z += 4)
            for (int x = Math.Max(1, ux - r); x <= Math.Min(World.SX - 2, ux + r); x += 4)
            {
                float d = (x - u.X) * (x - u.X) + (z - u.Z) * (z - u.Z);
                if (d >= bd || d > CurseReach * CurseReach) continue;
                int top = World.SurfAt(x, z);
                if (!Mat.IsPlayer[World.Get(x, top - 1, z)]) continue;
                bd = d; best = (x + .5f, top - 1, z + .5f);
            }
        return best;
    }

    /// <summary>Consecrated ground: near a sanctuary, or near a priest of ours, no curse takes.</summary>
    bool Warded(float x, float z)
    {
        foreach (var zone in Zones)
        {
            if (zone.Kind != Zone.Sanctum) continue;
            float dx = Math.Max(0, Math.Max(zone.X0 - x, x - zone.X1)), dz = Math.Max(0, Math.Max(zone.Z0 - z, z - zone.Z1));
            if (dx * dx + dz * dz < WardReach * WardReach) return true;
        }
        foreach (var o in Units)
            if (!o.Dead && o.T.Prays && (o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z) < WardReach * WardReach) return true;
        return false;
    }

    /// <summary>How much faster the horde hacks a voxel under a curse: twice, or not at all.</summary>
    internal float CurseHack(int x, int y, int z)
    {
        foreach (var c in Curses)
            if ((x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y) + (z - c.Z) * (z - c.Z) < c.R * c.R * 2.2f) return 2;
        return 1;
    }

    /// <summary>The curses wear off.</summary>
    void TickCurses()
    {
        if (Curses.Count > 0) Curses.RemoveAll(c => c.Until <= Time);
    }

    /// <summary>Whose way a raider goes: a shaman, a witch and a bone thrall the goblins', a necromancer the orcs'.</summary>
    public static UnitType FieldKind(UnitType t) => t == Kinds.Shaman || t == Kinds.Witch || t == Kinds.Thrall ? Kinds.Goblin : t == Kinds.Necromancer ? Kinds.Orc : t;

    /// <summary>The field a raider finds its way by.</summary>
    FlowField FieldOf(UnitType t) => Fields.TryGetValue(FieldKind(t), out var f) ? f : null;
}
