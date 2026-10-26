using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A goblin warlord's ambush. Of the army he brings in, one band (the one with the most goblins in it) does not go for the
/// walls: it slips off beside the way the army comes, out past where it would gather to storm, into the thickest cover
/// there is near — trees, bushes, tall grass — and lies down in it. Lying there it is not seen: ours do not aim at it,
/// its trail and its name are not shown, and it lies so low that only the tops of heads show over the grass. When one
/// of ours comes within 7 m of it, or one of it is hurt, it rises all at once and falls on whoever is near: ours within
/// 10 m of the one who came are caught off guard a moment and their pain jumps, and the band strikes the harder a while. If nobody comes, it
/// rises and joins the storm once the rest of the army is gone or broken, or after 4 minutes.
/// </summary>
public sealed partial class Sim
{
    /// <summary>How near one of ours comes to an ambush before it rises, voxels (7 m); how far the surprise reaches (10 m).</summary>
    const float AmbushReach = 7 * VoxelWorld.PerMetre, AmbushStartle = 10 * VoxelWorld.PerMetre;
    /// <summary>How long an ambush lies in wait at most, seconds; how long after it rises it goes for those near, seconds.</summary>
    const double AmbushWait = 240, AmbushPounce = 20;

    /// <summary>Ambushes laid and sprung on ours since the start (the bench counts them).</summary>
    public int AmbushesLaid, AmbushesSprung;
    /// <summary>An ambush rose on ours: its leader (for the sound and the shout).</summary>
    public event Action<Unit> AmbushSprung;

    /// <summary>Of a goblin warlord's army, the band that will lie in wait: the one with the most goblins, not his own, nor one after a mine.</summary>
    static void TellOffAmbush(List<Warband> army)
    {
        Warband best = null; int most = 0;
        foreach (var b in army)
        {
            if (b.LordsOwn || b.Target != null) continue;
            int goblins = 0;
            foreach (var t in b.Roster) if (t == Kinds.Goblin) goblins++;
            if (best == null || goblins > most) { best = b; most = goblins; }
        }
        if (best != null) best.Lurk = true;
    }

    /// <summary>
    /// A band told off to lie in wait sets out: to its hiding place, as to where it would gather; false (it marches as
    /// any band does) if there is nowhere to hide.
    /// </summary>
    bool SetOutToLurk(Warband b)
    {
        int spot = AmbushSpot(b);
        if (spot < 0) { b.Lurk = false; return false; }
        b.LurkCell = spot;
        b.RallyCell = spot; b.RallyPath = null; b.Side = -1;
        return true;
    }

    /// <summary>
    /// Where a band lies in wait: beside the way its army comes, 14 m out past where it would gather to storm and 7 m to
    /// one side; of the places within 10 m of that, the one with the thickest cover about it (trees, bushes, tall grass)
    /// and nothing of ours within 5 m. −1 if there is none.
    /// </summary>
    int AmbushSpot(Warband b)
    {
        float m = VoxelWorld.PerMetre, rx, rz;
        if (b.RallyCell >= 0) (rx, _, rz) = Nav.Feet(b.RallyCell);
        else if (b.Entry >= 0) { var (ex, _, ez) = Nav.Feet(b.Entry); (rx, rz) = Toward(Site.KeepX, Site.KeepZ, ex, ez, 34 * m); }
        else return -1;
        float dx = rx - Site.KeepX, dz = rz - Site.KeepZ, d = MathF.Max(1, MathF.Sqrt(dx * dx + dz * dz)), ux = dx / d, uz = dz / d;
        float side = (b.Id % 2 == 0 ? 7 : -7) * m;
        float bx = rx + ux * 14 * m - uz * side, bz = rz + uz * 14 * m + ux * side;
        int best = -1; float bestScore = float.MinValue;
        for (float oz = -10 * m; oz <= 10 * m; oz += 2 * m)
            for (float ox = -10 * m; ox <= 10 * m; ox += 2 * m)
            {
                int x = (int)(bx + ox), z = (int)(bz + oz);
                if (!World.InBounds(x, 1, z) || !World.OnLand(x, z)) continue;
                int ground = World.GroundTop(x, z), cover = 0;
                bool ours = false;
                for (int cz = z - 5 * (int)m; cz <= z + 5 * (int)m && !ours; cz += 2)
                    for (int cx = x - 5 * (int)m; cx <= x + 5 * (int)m && !ours; cx += 2)
                    {
                        if (!World.InBounds(cx, 1, cz)) continue;
                        int g = World.GroundTop(cx, cz);
                        for (int y = g; y < Math.Min(World.SY, g + 20); y += 2)
                        {
                            byte v = World.Get(cx, y, cz);
                            if (Mat.IsPlayer[v]) { ours = true; break; }
                            if (Math.Abs(cx - x) <= 3 * m && Math.Abs(cz - z) <= 3 * m && (Mat.IsVeg[v] || Mat.IsLeafy(v) || Mat.IsTrunk(v))) cover++;
                        }
                    }
                if (ours) continue;
                int c = NearestStand(x, z, 2);
                if (c < 0) continue;
                var (fx, fy, fz) = Nav.Feet(c);
                if (MathF.Abs(fx - x) + MathF.Abs(fz - z) > 3 * m || MathF.Abs(fy - ground) > 8 || Nav.Water[c] > 0) continue;
                float score = cover - MathF.Sqrt(ox * ox + oz * oz) / m * 2;
                if (score > bestScore) { bestScore = score; best = c; }
            }
        return best;
    }

    /// <summary>
    /// A band lying in wait, each tick: those come to the place lie down in the grass; one of ours come near, or one of
    /// it hurt, and it rises on them; nobody come, it rises and joins the storm when the rest of its army is gone or
    /// broken, or after 4 minutes.
    /// </summary>
    void AmbushTick(Warband b)
    {
        var l = b.Leader;
        if (Time - b.Since > 3)
            foreach (var u in b.Members)
                if (!u.Dead && !u.Lurking && !u.Routed && !u.Bound && u.Airborne == null && u.State == UnitState.Idle && Apart(u, l) <= (CaughtUp + 2) * NavGrid.CS)
                {
                    // the first down: the band is gone from sight
                    if (b.Members.TrueForAll(o => !o.Lurking)) { AmbushesLaid++; Say($"{b.Name} скрылась из виду. Где-то засада.", 2, u); }
                    u.Lurking = true; u.Path = null;
                }
        // a commander with Разведка about sees it lying there: it rises with nobody caught off guard
        if (b.Members.Exists(u => u.Lurking && !u.Dead) && Scout(b) is { } eye) { Spotted(b, eye); return; }
        Unit near = null, by = null;
        bool hurt = false;
        foreach (var u in b.Members)
        {
            if (u.Dead) continue;
            if (u.LastHurt > b.Since) { hurt = true; by ??= u.HurtBy; }
            if (near != null) continue;
            foreach (var o in Units)
            {
                if (o.Dead || o.Inside || o.Team != Team.Player || o.Airborne != null || Kinds.IsEngine(o.T)) continue;
                float dx = o.X - u.X, dz = o.Z - u.Z;
                if (dx * dx + dz * dz < AmbushReach * AmbushReach && MathF.Abs(o.Y - u.Y) < 3 * VoxelWorld.PerMetre) { near = o; break; }
            }
        }
        if (near != null || hurt) { Spring(b, near ?? by); return; }
        // the rest of the army gone, broken or long at the walls: no one is coming this way
        bool army = false;
        foreach (var o in Bands) if (o != b && o.Lord != null && o.Lord == b.Lord && !o.Broken && (o.State == BandState.Coming || o.Alive > 0)) army = true;
        if (!army || Time - b.Since > AmbushWait) Spring(b, null);
    }

    /// <summary>
    /// The ambush rises: on one of ours come near (those of ours about are caught off guard a moment, their pain jumps,
    /// and the band strikes harder a while and goes for those near), or by itself to join the storm.
    /// </summary>
    void Spring(Warband b, Unit on)
    {
        foreach (var u in b.Members) { u.Lurking = false; if (on != null && !u.Dead) u.RallyUntil = Time + 10; }
        b.Lurk = false;
        b.RallyCell = -1; b.RallyPath = null;
        Enter(b, BandState.Storm);
        if (on == null || on.Team != Team.Player)
        {
            Say($"{b.Name} выходит из засады и идёт на штурм.", 2, b.Leader);
            return;
        }
        b.SprungAt = Time;
        AmbushesSprung++;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Team != Team.Player || Kinds.IsEngine(o.T)) continue;
            float dx = o.X - on.X, dz = o.Z - on.Z;
            if (dx * dx + dz * dz > AmbushStartle * AmbushStartle) continue;
            o.StaggerUntil = Math.Max(o.StaggerUntil, Time + .7);
            o.Pain = Math.Min(1, o.Pain + .25f);
        }
        AmbushSprung?.Invoke(b.Leader);
        Shouted?.Invoke(b.Leader, "Засада!");
        Say($"Засада! {b.Name} поднялась из травы рядом с вашими людьми.", 3, b.Leader);
    }

    /// <summary>
    /// One of a band just risen from an ambush goes for the nearest of ours within 16 m, by its own way; true while it
    /// is on its way (beside them, the fight itself takes it).
    /// </summary>
    bool Pounce(Unit u, float dt)
    {
        if (u.Band is not { } b || Time - b.SprungAt > AmbushPounce || Climbing(u)) return false;
        var foe = Nearest(u, Team.Player, 16 * VoxelWorld.PerMetre);
        if (foe == null || foe.Airborne != null) return false;
        if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
        {
            u.NextSearch = Time + .8;
            if (!GoTo(u, foe.Cell, adjacent: true, budget: 2500)) u.Path = null;
        }
        if (u.Path == null || u.PathI >= u.Path.Count) return false;
        Walk(u, dt);
        return true;
    }
}
