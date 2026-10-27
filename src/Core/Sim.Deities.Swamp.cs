using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The Great Toad (Deities.cs), the horde's god out of the swamps. The ground where it is called goes to bog: it comes up
/// out of the mud, and the pit it leaves fills with water and the ground about it with mire. It goes by hops, coming down
/// on whatever it hops on and crushing all under its belly, and every place it comes down goes to mire. Its tongue shoots
/// out forty-five metres, catches whoever is at its tip — up to six — or tears out a piece of what stands there, and
/// draws it back into the mouth: what it catches it swallows, and is the stronger for it. It spits venom that burns those
/// about where it falls and lies there eating into stone and timber; its croak knocks down all of ours about it.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The tongue: out by this, holding till this, back by this, and the act done; the venom spat; done.</summary>
    public const float TongueGrab = .35f, TongueBack = .5f, TongueHome = .95f, TongueDone = 1.25f, SpitAt = .8f, SpitDone = 1.4f;
    /// <summary>How near its tip the tongue catches, voxels; how many at once; what each one swallowed gives it back.</summary>
    const float TongueCatch = 5 * VoxelWorld.PerMetre;
    const int TongueMost = 6;
    const float Swallowed = 600;
    public const string SwallowCause = "проглочен жабой", VenomCause = "яд жабы";

    /// <summary>How far out the tongue is, 0 in the mouth to 1 at the spot, by the time into the act.</summary>
    public static float TongueOut(float t) => t < TongueGrab ? t / TongueGrab : t < TongueBack ? 1 : t < TongueHome ? 1 - (t - TongueBack) / (TongueHome - TongueBack) : 0;

    /// <summary>Its mouth, at the front of its head (voxels).</summary>
    public static (float x, float y, float z) Mouth(Deity d) =>
        (d.X + MathF.Cos(d.Facing) * d.T.Stance * .95f, d.Y + d.T.Height * .42f * d.Up, d.Z + MathF.Sin(d.Facing) * d.T.Stance * .95f);

    /// <summary>
    /// The ground goes to bog about a spot: a mire laid on it for a while, and, where it came up out of the ground, water
    /// let into its pit.
    /// </summary>
    void Bogged(Deity d, float x, float z, float r, bool pond)
    {
        Mires.Add(new Mire { X = x, Z = z, R = r, Until = Time + (pond ? 300 : 120) });
        if (!pond) return;
        // its pit: water over the bottom a metre and a half deep, let in over all of it at once
        int ix = (int)x, iz = (int)z, ri = (int)PitR;
        if (!World.InBounds(ix, 8, iz)) return;
        int top = World.SurfAt(ix, iz) + (int)(1.5f * VoxelWorld.PerMetre);
        for (int dz = -ri; dz <= ri; dz++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                int vx = ix + dx, vz = iz + dz;
                if (dx * dx + dz * dz > ri * ri || vx < 2 || vz < 2 || vx >= World.SX - 2 || vz >= World.SZ - 2) continue;
                int y = World.SurfAt(vx, vz);
                if (y < top) Pour(Mat.Water, vx, y, vz, top - y);
            }
    }

    // ------------------------------------------------------------ the tongue

    void BeginTongue(Deity d, float x, float z)
    {
        d.AimX = x; d.AimZ = z; d.AimY = Ground((int)x, (int)z, false);
        d.Facing = MathF.Atan2(z - d.Z, x - d.X);
        d.GoX = float.NaN;
        Begin(d, DeityAct.Tongue);
    }

    /// <summary>
    /// The tongue at its work: at its tip it catches whoever of its enemy is there — or, with none there, tears out a
    /// piece of what stands there — and draws it all back; in the mouth, swallowed.
    /// </summary>
    void Tonguing(Deity d, float t)
    {
        if (!d.Landed && t >= TongueGrab)
        {
            d.Landed = true;
            foreach (var u in Units)
            {
                if (d.Caught.Count >= TongueMost) break;
                if (u.Dead || u.Inside || !Hostile(d, u) || Kinds.IsEngine(u.T) || Dist2(u.X, u.Z, d.AimX, d.AimZ) > TongueCatch * TongueCatch) continue;
                if (Fling(u, 0, 0, 0, d.T.Cause) is { } f) { f.Bearer = d; d.Caught.Add(u); }
            }
            if (d.Caught.Count == 0)
            {
                // nobody: a piece of what stands there, torn out
                var piece = new List<(int x, int y, int z)>();
                const float r = 8;
                int ri = (int)r;
                for (int dy = -ri; dy <= ri; dy++)
                    for (int dz = -ri; dz <= ri; dz++)
                        for (int dx = -ri; dx <= ri; dx++)
                        {
                            if (dx * dx + dy * dy + dz * dz > r * r) continue;
                            int vx = (int)d.AimX + dx, vy = (int)d.AimY + ri / 2 + dy, vz = (int)d.AimZ + dz;
                            if (World.InBounds(vx, vy, vz) && vy > 3 && Breakable(World.Get(vx, vy, vz))) piece.Add((vx, vy, vz));
                        }
                if (Loose(piece, 0, 0, 0, d.T.Cause) is { } lump) { lump.Bearer = d; d.Held = lump; d.Broke += lump.Voxels.Count; InvalidateFields(); }
            }
        }
        if (t >= TongueHome && d.Landed && (d.Caught.Count > 0 || d.Held != null)) Swallow(d);
        if (t >= TongueDone) Begin(d, DeityAct.Standing);
    }

    /// <summary>What the tongue brought back goes down: those caught swallowed (and the toad the stronger), a piece torn out gone.</summary>
    void Swallow(Deity d)
    {
        var names = new List<string>();
        foreach (var u in d.Caught)
        {
            if (u.Airborne is { } f) { f.Bearer = null; Fliers.Remove(f); u.Airborne = null; }
            if (u.Dead) continue;
            Strike(d, u, 99999);
            u.KilledBy = SwallowCause;
            // nothing of it left to lie on the field
            u.DiedAt = -999;
            d.Hp = Math.Min(d.T.Hp, d.Hp + Swallowed);
            names.Add(u.T.Name.ToLowerInvariant());
        }
        d.Caught.Clear();
        if (d.Held is { } lump) { lump.Bearer = null; Fliers.Remove(lump); d.Held = null; }
        if (names.Count > 0) Say($"Великая жаба проглотила: {string.Join(", ", names)}.", d.Team == Team.Horde ? 3 : 1, d.X, d.Z);
    }

    /// <summary>What a god holds or has caught, let go where it is (it is going, or struck).</summary>
    void LetCaughtGo(Deity d)
    {
        foreach (var u in d.Caught) if (u.Airborne is { } f) f.Bearer = null;
        d.Caught.Clear();
        if (d.Held != null) { d.Held.Bearer = null; d.Held = null; }
    }

    /// <summary>Whether a piece in the air is in a god's hand, or on its tongue.</summary>
    static bool InGrip(Deity d, Flier f) => d.Held == f || f.Unit != null && d.Caught.Contains(f.Unit);

    // ------------------------------------------------------------ the venom and the croak

    void BeginSpit(Deity d, float x, float z)
    {
        d.AimX = x; d.AimZ = z; d.AimY = Ground((int)x, (int)z, false);
        d.GoX = float.NaN; d.LastSpit = Time;
        Begin(d, DeityAct.Spit);
    }

    /// <summary>The venom spat: a ball of it out of the mouth on its arc to the spot.</summary>
    void Spat(Deity d)
    {
        var (mx, my, mz) = Mouth(d);
        float dx = d.AimX - mx, dz = d.AimZ - mz, dist = MathF.Sqrt(dx * dx + dz * dz), t = Math.Clamp(.6f + dist / (35 * VoxelWorld.PerMetre), .6f, 2.2f);
        float vy = (d.AimY - my + .5f * Gravity * t * t) / t;
        var f = Hurl(Ball(7, Mat.Brew), mx, my, mz, dx / t, vy, dz / t, VenomCause);
        if (f == null) return;
        f.Venom = true;
        f.By = null;
    }

    /// <summary>
    /// The venom comes down: those about where it fell burnt the more the nearer, and the venom lies there and eats into
    /// what is built, as acid does.
    /// </summary>
    void VenomLands(Flier f)
    {
        const float r = 7 * VoxelWorld.PerMetre;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Team == Team.Horde) continue;
            float d2 = Dist2(u.X, u.Z, f.X, f.Z);
            if (d2 > r * r) continue;
            Hurt(u, null, 90 * (1 - MathF.Sqrt(d2) / r) + 10, VenomCause);
        }
        int ix = (int)f.X, iz = (int)f.Z;
        int y = World.SurfAt(Math.Clamp(ix, 0, World.SX - 1), Math.Clamp(iz, 0, World.SZ - 1));
        if (World.InBounds(ix, y, iz)) Pour(Mat.Acid, ix, y, iz, 160);
        // spat at the other god: it burns it
        var toad = Gods.Find(g => !g.Gone && g.T.Kind == DeityKind.Toad);
        if (toad?.Foe is { } foe && Dist2(foe.X, foe.Z, f.X, f.Z) < MathF.Pow(foe.T.Stance + r, 2)) Smite(toad, foe, 1500, foe.X - toad.X, foe.Z - toad.Z);
        VenomSplashed?.Invoke(f.X, f.Y, f.Z);
    }

    /// <summary>The venom splashed down (for the view).</summary>
    public event Action<float, float, float> VenomSplashed;

    /// <summary>The croak: all of its enemy for forty-five metres about it knocked off their feet, the nearer the longer.</summary>
    void Croak(Deity d)
    {
        const float r = 45 * VoxelWorld.PerMetre;
        int down = 0;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || !Hostile(d, u) || Kinds.IsEngine(u.T)) continue;
            float d2 = Dist2(u.X, u.Z, d.X, d.Z);
            if (d2 > r * r) continue;
            GoDown(u, 1.5 + 3 * (1 - MathF.Sqrt(d2) / r));
            down++;
        }
        if (down > 0) Say($"Великая жаба квакнула: сбиты с ног {down}.", 3, d.X, d.Z);
        GodShook?.Invoke(d, d.X, d.Y + d.T.Height * .4f, d.Z, 1.2f);
    }

    // ------------------------------------------------------------ what it does of itself

    /// <summary>
    /// The toad left to itself: at the other god by hops, coming down on it and spitting at it; else at the thickest
    /// knot of ours — a croak at it if it is close (to knock it down for the tongue), the tongue at it if it is in reach
    /// before it, venom at it if it is just beyond, a hop onto it if it is a little off, hops toward it if it is far;
    /// with nobody of ours about, at the citadel.
    /// </summary>
    void ThinkSwamp(Deity d, float dt)
    {
        if (Time < d.NextThink) return;
        d.NextThink = Time + .4;
        if (d.Foe is { } foe)
        {
            float fd = MathF.Sqrt(Dist2(foe.X, foe.Z, d.X, d.Z));
            if (fd < d.T.SlamFar && Rng.NextDouble() < .5) { BeginLeap(d, foe.X, foe.Z); return; }
            if (fd < d.T.Reach) { BeginSpit(d, foe.X, foe.Z); return; }
            BeginLeap(d, foe.X, foe.Z);
            return;
        }
        var (cx, cz, score, _) = Thickest(d);
        if (score <= 0)
        {
            // nobody of ours about: the citadel
            if (Site.HasKeep && Dist2(Site.KeepX, Site.KeepZ, d.X, d.Z) > MathF.Pow(Site.KeepR + 14 * VoxelWorld.PerMetre, 2))
            {
                BeginLeap(d, Site.KeepX, Site.KeepZ);
                return;
            }
            if (Site.HasKeep && Rng.NextDouble() < .4) { BeginSpit(d, Site.KeepX, Site.KeepZ); return; }
            if (Time - d.ActFrom > 15 && Rng.NextDouble() < .1) BeginRoar(d);
            return;
        }
        float dist = MathF.Sqrt(Dist2(cx, cz, d.X, d.Z)), off = Off(d, cx, cz);
        // a knot of ours close by: a croak now and then, to knock them down for the tongue
        if (dist < 30 * VoxelWorld.PerMetre && score >= 3 && Time - d.LastRoar > 12) { BeginRoar(d); return; }
        if (dist <= d.T.Reach && MathF.Abs(off) < 1.2f && Rng.NextDouble() < .55) { BeginTongue(d, cx, cz); return; }
        // just out of the tongue's reach: venom, now and then
        if (dist <= d.T.Reach * 1.3f && Time - d.LastSpit > 6) { BeginSpit(d, cx, cz); return; }
        if (dist >= d.T.SlamNear && dist <= d.T.SlamFar) { BeginLeap(d, cx, cz); return; }
        BeginLeap(d, cx, cz);
    }
}
