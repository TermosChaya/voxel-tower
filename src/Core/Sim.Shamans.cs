using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A patch of our walls under a shaman's curse: the horde hacks it so many times as fast (twice), till when. A shaman
/// warlord's curse on the stone (<see cref="Stone"/>) takes a stretch of wall from its foot to its top, cracks it,
/// keeps it crumbling, and breaks under blows and boulders as under hacking.
/// </summary>
public sealed class Curse
{
    public float X, Y, Z, R, Power = 2;
    public double Until;
    public bool Stone;
    /// <summary>A curse on the stone: the faces of our building in its stretch (packed), as they were found, to crumble.</summary>
    internal List<int> Faces;
}

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
            if (Time - _wardSaid > 20) { _wardSaid = Time; Say("Шаман пытается проклясть стену, но рядом святилище, и проклятие не действует.", 1, x, z); }
            return;
        }
        Curses.Add(new Curse { X = x, Y = y, Z = z, R = CurseR, Until = Time + CurseFor });
        Hexed?.Invoke(x, y, z);
        if (Time - _curseSaid > 15) { _curseSaid = Time; Say("Шаман проклял стену: орда ломает её вдвое быстрее. Святилище рядом защищает от проклятий.", 3, x, z); }
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

    /// <summary>How much faster the horde hacks a voxel under a curse: twice, three times under a warlord's, or not at all.</summary>
    internal float CurseHack(int x, int y, int z)
    {
        float k = 1;
        foreach (var c in Curses)
            if (Cursed(c, x, y, z)) k = Math.Max(k, c.Power);
        return k;
    }

    /// <summary>How much easier a blow, a blast or a boulder breaks a voxel: as hacking, under a curse on the stone; else not at all.</summary>
    internal float CurseBlow(float x, float y, float z)
    {
        float k = 1;
        foreach (var c in Curses)
            if (c.Stone && Cursed(c, x, y, z)) k = Math.Max(k, c.Power);
        return k;
    }

    /// <summary>Whether a voxel is under a curse: about its middle, or for a curse on the stone in its stretch from foot to top.</summary>
    static bool Cursed(Curse c, float x, float y, float z) =>
        c.Stone ? (x - c.X) * (x - c.X) + (z - c.Z) * (z - c.Z) < c.R * c.R
        : (x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y) + (z - c.Z) * (z - c.Z) < c.R * c.R * 2.2f;

    /// <summary>The curses wear off; those on the stone crumble it while they last.</summary>
    void TickCurses()
    {
        if (Curses.Count == 0) return;
        Curses.RemoveAll(c => c.Until <= Time);
        if (Time < _nextCrumble) return;
        _nextCrumble = Time + .5;
        foreach (var c in Curses) if (c.Stone) Crumble(c);
    }
    double _nextCrumble;

    // ------------------------------------------------------------ a shaman warlord's curse on the stone

    /// <summary>How far about its middle a curse on the stone takes our wall, voxels (3 m), how long it lasts, seconds, and how much easier it breaks.</summary>
    const float StoneCurseR = 3 * VoxelWorld.PerMetre, StoneCursePower = 3;
    const double StoneCurseFor = 60;

    /// <summary>
    /// A shaman at the head of an army curses the stone of our walls: the stretch of our building nearest him within his
    /// voice, 3 m about and from its foot to its top, cracks over its faces at once and goes on crumbling while the curse
    /// lasts (a minute), bits of it falling away; hacking, blows, blasts and boulders break it there three times as easily.
    /// Consecrated ground is proof against it as against any curse. Every half minute, while there is a wall to curse.
    /// </summary>
    void LordCurse(Unit u)
    {
        u.SlamReady = Time + 2;
        if (StoneTarget(u) is not var (x, y, z) || Curses.Exists(c => c.Stone && MathF.Abs(c.X - x) + MathF.Abs(c.Z - z) < StoneCurseR)) return;
        u.Swing = 1;
        if (Warded(x, z))
        {
            u.SlamReady = Time + StoneCurseRest;
            if (Time - _wardSaid > 20) { _wardSaid = Time; Say("Шаман-военачальник пытается проклясть стену, но рядом святилище, и проклятие не действует.", 1, x, z); }
            return;
        }
        u.SlamReady = Time + StoneCurseRest;
        var c = new Curse { X = x, Y = y, Z = z, R = StoneCurseR, Power = StoneCursePower, Until = Time + StoneCurseFor, Stone = true };
        Curses.Add(c);
        LordCurses++;
        // the faces of the stretch crack at once, each stone as it lies
        foreach (int p in FacesOf(c))
        {
            var (fx, fy, fz) = WaterPlanner.Unpack(p);
            uint h = (uint)(fx * 73856093 ^ fy * 19349663 ^ fz * 83492791);
            float crack = .4f + (h >> 8 & 255) / 255f * .25f;
            if (Cracks.GetValueOrDefault(p) < crack) SetCrack(p, crack);
            _weighs.Add(PatchOf(fx, fz));
        }
        Hexed?.Invoke(x, y, z);
        Say($"{u.Lord.Name} проклял кладку стены на 1 минуту: она трескается, крошится и ломается втрое быстрее.", 3, x, z);
    }

    /// <summary>Whether a voxel shows a face: air, or anything one can stand in, beside it.</summary>
    bool Face(int x, int y, int z)
    {
        for (int k = 0; k < 6; k++)
        {
            int ax = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ay = y + (k == 2 ? 1 : k == 3 ? -1 : 0), az = z + (k == 4 ? 1 : k == 5 ? -1 : 0);
            if (!World.InBounds(ax, ay, az)) continue;
            byte n = World.Get(ax, ay, az);
            if (n == Mat.Air || Mat.Passable[n]) return true;
        }
        return false;
    }

    /// <summary>The faces of our building in the stretch a curse on the stone takes (found once, and again after a load).</summary>
    List<int> FacesOf(Curse c)
    {
        if (c.Faces != null) return c.Faces;
        c.Faces = new List<int>();
        int x0 = (int)(c.X - c.R), x1 = (int)(c.X + c.R), z0 = (int)(c.Z - c.R), z1 = (int)(c.Z + c.R);
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (!World.InBounds(x, 1, z) || !Cursed(c, x + .5f, 0, z + .5f)) continue;
                int top = World.SurfAt(x, z);
                for (int y = Math.Max(1, World.GroundTop(x, z) - 2); y < top; y++)
                {
                    byte m = World.Get(x, y, z);
                    if (Mat.IsPlayer[m] && BearsDown(m) > 0 && Face(x, y, z)) c.Faces.Add(WaterPlanner.Pack(x, y, z));
                }
            }
        return c.Faces;
    }

    /// <summary>The middle of the piece of our walls nearest a shaman warlord within his voice, half way up it, or nothing.</summary>
    (float x, float y, float z)? StoneTarget(Unit u)
    {
        int ux = (int)u.X, uz = (int)u.Z, r = (int)LordReach;
        float bd = float.MaxValue; (float, float, float)? best = null;
        for (int z = Math.Max(1, uz - r); z <= Math.Min(World.SZ - 2, uz + r); z += 4)
            for (int x = Math.Max(1, ux - r); x <= Math.Min(World.SX - 2, ux + r); x += 4)
            {
                float d = (x - u.X) * (x - u.X) + (z - u.Z) * (z - u.Z);
                if (d >= bd || d > LordReach * LordReach) continue;
                int top = World.SurfAt(x, z), ground = World.GroundTop(x, z);
                // a wall: our masonry or timber on top, a man's height and more over the ground
                if (top - ground < 2 * VoxelWorld.PerMetre || !Mat.IsPlayer[World.Get(x, top - 1, z)]) continue;
                bd = d; best = (x + .5f, (ground + top) * .5f, z + .5f);
            }
        return best;
    }

    /// <summary>
    /// A curse on the stone, every half second while it lasts: a stone or two on the faces of its stretch cracks through
    /// and falls away.
    /// </summary>
    void Crumble(Curse c)
    {
        var faces = FacesOf(c);
        for (int k = 0; k < 2 && faces.Count > 0; k++)
        {
            int i = Rng.Next(faces.Count), p = faces[i];
            faces[i] = faces[^1]; faces.RemoveAt(faces.Count - 1);
            var (x, y, z) = WaterPlanner.Unpack(p);
            byte m = World.Get(x, y, z);
            if (!Mat.IsPlayer[m] || BearsDown(m) <= 0) continue;
            SetCrack(p, 1);
            GiveWay(p);
            _weighs.Add(PatchOf(x, z));
        }
    }

    /// <summary>Whose way a raider goes: a shaman, a witch, a bone thrall and an alchemist the goblins', a necromancer the orcs'.</summary>
    public static UnitType FieldKind(UnitType t) => t == Kinds.Shaman || t == Kinds.Witch || t == Kinds.Thrall || t == Kinds.Alchemist ? Kinds.Goblin : t == Kinds.Necromancer ? Kinds.Orc : t;

    /// <summary>The field a raider finds its way by.</summary>
    FlowField FieldOf(UnitType t) => Fields.TryGetValue(FieldKind(t), out var f) ? f : null;
}
