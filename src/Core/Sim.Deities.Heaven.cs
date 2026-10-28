using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The Archangel (Deities.cs), the household's god, and the leap both gods share. It comes down out of heaven in a
/// pillar of light: while it comes down the light on the spot blinds the horde about it, and as it touches the ground the
/// ground shakes and the horde is thrown off it, and the turf about its feet is burnt. Its sword of fire is swept along
/// the ground as the colossus's hand is, and sets alight what it passes; the pillar of heaven is a blow of fire out of the
/// sky at a spot far off; a beat of its wings blows the horde before it away and tears apart the ladders and decks in the
/// way; it flies over the valley in leaps and comes down hard; and at the sound of its trumpet the horde's works about it
/// fall to pieces and the horde runs. Its time over, or struck down by the other god, it goes up into heaven again.
/// </summary>
public sealed partial class Sim
{
    /// <summary>A beat of the wings: when it blows, when the act is done, how far it reaches (voxels) and how wide (radians either side).</summary>
    public const float GustAt = .9f, GustDone = 1.9f;
    const float GustR = 60 * VoxelWorld.PerMetre, GustSpread = .9f;
    /// <summary>A leap: the crouch before it, the flight (on wings, and a toad's hop), how far it goes at most (voxels).</summary>
    public const float LeapCrouch = .45f, FlightFor = 2.2f, HopFor = 1f;
    const float FlightFar = 90 * VoxelWorld.PerMetre;

    /// <summary>A pillar of fire came down out of heaven at a spot (voxels), for the view.</summary>
    public event Action<Deity, float, float, float> PillarFell;

    /// <summary>The light on the spot as the archangel comes down: the horde about it is afraid, the nearer the surer.</summary>
    void Heralded(Deity d)
    {
        const float r = 40 * VoxelWorld.PerMetre;
        int fled = 0;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Team != Team.Horde || Kinds.IsEngine(u.T)) continue;
            float d2 = Dist2(u.X, u.Z, d.X, d.Z);
            if (d2 > r * r || (u.Leader || u.T == Kinds.Troll) && Rng.NextDouble() < .5) continue;
            u.Routed = true; u.Raid = null; u.Path = null; u.Target = null;
            StopCarve(u); StopBuild(u);
            fled++;
        }
        DismayAbout(d.X, d.Z, r * 1.5f, .2f);
        PillarFell?.Invoke(d, d.X, d.Y, d.Z);
    }

    /// <summary>It touches the ground: a blast about its feet, the turf burnt round them.</summary>
    void TouchDown(Deity d)
    {
        Blast(d, d.X, d.Y, d.Z, 4 * VoxelWorld.PerMetre, 30 * VoxelWorld.PerMetre, 50, false);
        SearRound(d.X, d.Y, d.Z, 10 * VoxelWorld.PerMetre);
    }

    /// <summary>The pillar of heaven: a blow of fire out of the sky at the spot — a crater, a blast, all about it alight.</summary>
    void Pillar(Deity d)
    {
        // (with an eye gone it comes down wide of where it was looked at, up to six metres)
        if (d.Lost(Eyes) > 0)
        {
            float a = (float)Rng.NextDouble() * MathF.Tau, off = (2 + 4 * (float)Rng.NextDouble()) * VoxelWorld.PerMetre;
            d.AimX = Math.Clamp(d.AimX + MathF.Cos(a) * off, 8, World.SX - 9); d.AimZ = Math.Clamp(d.AimZ + MathF.Sin(a) * off, 8, World.SZ - 9);
            d.AimY = Ground((int)d.AimX, (int)d.AimZ, false);
        }
        Blast(d, d.AimX, d.AimY, d.AimZ, 5.5f * VoxelWorld.PerMetre, 28 * VoxelWorld.PerMetre, 140, true);
        SearRound(d.AimX, d.AimY, d.AimZ, 8 * VoxelWorld.PerMetre);
        PillarFell?.Invoke(d, d.AimX, d.AimY, d.AimZ);
    }

    /// <summary>
    /// A blast where a god's blow, its landing or its touching down meets the ground: a bowl broken out of it and thrown
    /// up, the enemy about it hurt and thrown out from it (crushed under it, at its heart), ours knocked down (the
    /// household's god spares the household's works, and its own), the other god struck if it stood there; the horde's
    /// god at the citadel's foot batters the citadel. With fire, what burns about it catches.
    /// </summary>
    void Blast(Deity d, float x, float y, float z, float crater, float reach, float harm, bool fire)
    {
        var bowl = new List<(int x, int y, int z)>();
        int ri = (int)crater;
        for (int dz = -ri; dz <= ri; dz++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                float k = (dx * dx + dz * dz) / (crater * crater);
                int vx = (int)x + dx, vz = (int)z + dz;
                if (k > 1 || vx < 2 || vz < 2 || vx >= World.SX - 2 || vz >= World.SZ - 2) continue;
                int top = World.ColTop(vx, vz), bottom = Math.Max(4, (int)y - (int)(crater * .55f * (1 - k)));
                for (int vy = top; vy >= bottom; vy--)
                {
                    byte m = World.Get(vx, vy, vz);
                    if (!Breakable(m) || d.Team == Team.Player && Mat.IsPlayer[m]) continue;
                    bowl.Add((vx, vy, vz));
                }
            }
        Scatter(d, bowl, x, y - crater * .5f, z, 0, 0, crater * 2.2f, crater * 1.6f, 260);
        if (d.Team != Team.Player) Jar(x, y, z, crater * 1.6f, .7f);
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null) continue;
            float d2 = Dist2(u.X, u.Z, x, z);
            if (d2 > reach * reach) continue;
            float dist = MathF.Sqrt(d2), k = 1 - dist / reach, ox = dist > .5f ? (u.X - x) / dist : 1, oz = dist > .5f ? (u.Z - z) / dist : 0;
            // (its own side it spares: ours knocked down, the horde's god's own let be)
            if (!Hostile(d, u)) { if (d.Team == Team.Player && k > .35f) GoDown(u, 2 * k); continue; }
            if (dist < crater * .6f + u.T.BodyR) { Strike(d, u, 99999); continue; }
            float lift = MathF.Min(1, 15 / u.T.BodyH);
            Strike(d, u, (Kinds.IsEngine(u.T) ? harm * 8 : harm) * k);
            Fling(u, ox * (8 + 16 * k) * VoxelWorld.PerMetre * lift, (5 + 11 * k) * VoxelWorld.PerMetre * MathF.Sqrt(lift), oz * (8 + 16 * k) * VoxelWorld.PerMetre * lift, d.T.Cause);
        }
        // the other god in it struck as hard as the blow is (the pillar hardest, coming down off a flight or a hop less)
        if (d.Foe is { } foe2 && Dist2(foe2.X, foe2.Z, x, z) < MathF.Pow(foe2.T.Stance + crater, 2)) Smite(d, foe2, harm * 30, x - d.X, z - d.Z);
        if (d.Team == Team.Horde && Site.HasKeep && Dist2(x, z, Site.KeepX, Site.KeepZ) < MathF.Pow(Site.KeepR + crater + 6 * VoxelWorld.PerMetre, 2))
        {
            KeepHp -= 160;
            Say($"{d.T.Name} бьёт по цитадели.", 3, x, z);
        }
        if (fire) Kindle(x, y, z, crater * 2.2f, 16);
        InvalidateFields();
        Quaked?.Invoke(x, z, reach);
        GodShook?.Invoke(d, x, y, z, 3);
    }

    // ------------------------------------------------------------ the wings

    void BeginGust(Deity d) { d.GoX = float.NaN; Begin(d, DeityAct.Gust); }

    /// <summary>
    /// The wings beat at what is before it: the horde in a wide wedge before it blown away, the nearer the harder, the
    /// ladders and decks in the wedge torn apart and thrown after it; ours there knocked down; the other god thrown back.
    /// </summary>
    void Gusted(Deity d)
    {
        int thrown = 0;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null) continue;
            float d2 = Dist2(u.X, u.Z, d.X, d.Z);
            if (d2 > GustR * GustR || MathF.Abs(Off(d, u.X, u.Z)) > GustSpread) continue;
            float dist = MathF.Sqrt(d2), k = 1 - dist / GustR, ox = (u.X - d.X) / MathF.Max(1, dist), oz = (u.Z - d.Z) / MathF.Max(1, dist);
            if (!Hostile(d, u)) { if (k > .3f) GoDown(u, 1.5); continue; }
            float lift = MathF.Min(1, 15 / u.T.BodyH);
            Strike(d, u, (Kinds.IsEngine(u.T) ? 250 : 40) * k);
            if (Fling(u, ox * (10 + 22 * k) * VoxelWorld.PerMetre * lift, (4 + 8 * k) * VoxelWorld.PerMetre * MathF.Sqrt(lift), oz * (10 + 22 * k) * VoxelWorld.PerMetre * lift, d.T.Cause) != null) thrown++;
        }
        bool tore = false;
        float cx = d.X + MathF.Cos(d.Facing) * GustR * .5f, cz = d.Z + MathF.Sin(d.Facing) * GustR * .5f;
        foreach (var (key, list) in SiegeWorksIn(cx, cz, GustR * .55f))
        {
            var (px, _, pz) = list[0];
            if (MathF.Abs(Off(d, px, pz)) > GustSpread) continue;
            float ox = px - d.X, oz = pz - d.Z, ol = MathF.Max(1, MathF.Sqrt(ox * ox + oz * oz));
            if (Loose(list, ox / ol * 14 * VoxelWorld.PerMetre, (5 + key % 4) * VoxelWorld.PerMetre, oz / ol * 14 * VoxelWorld.PerMetre, d.T.Cause) != null) tore = true;
        }
        if (tore) InvalidateFields();
        // the other god in the wedge thrown back out of reach of whatever it was at, into reach of the sword
        if (d.Foe is { } foe && Dist2(foe.X, foe.Z, d.X, d.Z) < GustR * GustR && MathF.Abs(Off(d, foe.X, foe.Z)) < GustSpread + .4f)
        {
            float k = 1 - MathF.Sqrt(Dist2(foe.X, foe.Z, d.X, d.Z)) / GustR;
            Smite(d, foe, 1500, foe.X - d.X, foe.Z - d.Z);
            Shove(foe, foe.X - d.X, foe.Z - d.Z, (6 + 18 * k) * VoxelWorld.PerMetre);
        }
        GodShook?.Invoke(d, cx, d.Y, cz, 1.2f);
    }

    /// <summary>
    /// The trumpet: the horde for seventy metres about it runs, the hardest of them one in two, and the horde's ladders,
    /// decks and bridges for forty metres about it fall to pieces.
    /// </summary>
    void Trumpet(Deity d)
    {
        const float r = 70 * VoxelWorld.PerMetre;
        int fled = 0, fell = 0;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Team != Team.Horde || Kinds.IsEngine(u.T) || Dist2(u.X, u.Z, d.X, d.Z) > r * r) continue;
            if ((u.Leader || u.T == Kinds.Troll) && Rng.NextDouble() < .5) continue;
            u.Routed = true; u.Raid = null; u.Path = null; u.Target = null;
            StopCarve(u); StopBuild(u);
            fled++;
        }
        foreach (var (key, list) in SiegeWorksIn(d.X, d.Z, 40 * VoxelWorld.PerMetre))
        {
            float a = key * 2.39996f % MathF.Tau;
            if (Loose(list, MathF.Cos(a) * 2 * VoxelWorld.PerMetre, (2 + key % 3) * VoxelWorld.PerMetre, MathF.Sin(a) * 2 * VoxelWorld.PerMetre, d.T.Cause) != null) fell++;
        }
        if (fell > 0) InvalidateFields();
        DismayAbout(d.X, d.Z, r, .35f);
        Say(fled + fell == 0 ? "Труба Архангела: орды рядом нет." : $"Труба Архангела: бегут {fled}, рассыпались работы орды: {fell}.", 1, d.X, d.Z);
        GodShook?.Invoke(d, d.X, d.Y + d.T.Height * .8f, d.Z, .6f);
    }

    // ------------------------------------------------------------ the leap

    /// <summary>A leap toward a spot: on wings as far as seventy metres, a toad's hop as far as its stride.</summary>
    void BeginLeap(Deity d, float x, float z)
    {
        // (a toad short of a leg hops the shorter, a fifth for each)
        float far = d.T.Winged ? FlightFar : d.T.Stride * (1 - .2f * d.Lost(Legs));
        float dx = x - d.X, dz = z - d.Z, dist = MathF.Sqrt(dx * dx + dz * dz);
        if (dist > far) { x = d.X + dx / dist * far; z = d.Z + dz / dist * far; }
        x = Math.Clamp(x, 12, World.SX - 13); z = Math.Clamp(z, 12, World.SZ - 13);
        for (int i = 0; i < 20 && Site.Hex != null && Site.Hex.Edge(x, z) < 6 * VoxelWorld.PerMetre; i++) { x += (World.SX * .5f - x) * .1f; z += (World.SZ * .5f - z) * .1f; }
        if (dist > 1) d.Facing = MathF.Atan2(z - d.Z, x - d.X);
        d.LeapFromX = d.X; d.LeapFromY = d.Y; d.LeapFromZ = d.Z;
        d.AimX = x; d.AimZ = z; d.AimY = Ground((int)x, (int)z, d.Team == Team.Player);
        d.LeapArc = d.T.Winged ? 22 * VoxelWorld.PerMetre : 9 * VoxelWorld.PerMetre;
        d.Lifted = -1;
        Begin(d, DeityAct.Leap);
    }

    /// <summary>
    /// Leaping: crouched, then up and along its arc, and down on the spot — a blast where it comes down (a toad crushing
    /// all under its belly, the ground there gone to bog); then on its way again, or standing.
    /// </summary>
    void Leaping(Deity d, float t)
    {
        float fly = d.T.Winged ? FlightFor : HopFor;
        if (t < LeapCrouch) return;
        float p = Math.Clamp((t - LeapCrouch) / fly, 0, 1), s = d.T.Winged ? Smooth(p) : p;
        d.X = Lerp(d.LeapFromX, d.AimX, s); d.Z = Lerp(d.LeapFromZ, d.AimZ, s);
        d.Y = Lerp(d.LeapFromY, d.AimY, p) + d.LeapArc * MathF.Sin(MathF.PI * p);
        if (p < 1) return;
        if (!d.Landed)
        {
            d.Landed = true;
            d.X = d.AimX; d.Y = d.AimY; d.Z = d.AimZ;
            FeetUnder(d, true);
            if (d.T.Hops)
            {
                Blast(d, d.X, d.Y, d.Z, 5 * VoxelWorld.PerMetre, 18 * VoxelWorld.PerMetre, 50, false);
                Crush(d, d.X, d.Y, d.Z, d.T.Foot);
                Bogged(d, d.X, d.Z, 8 * VoxelWorld.PerMetre, false);
            }
            else Blast(d, d.X, d.Y, d.Z, 4 * VoxelWorld.PerMetre, 26 * VoxelWorld.PerMetre, 80, false);
        }
        if (t >= LeapCrouch + fly + .4f) Begin(d, float.IsNaN(d.GoX) ? DeityAct.Standing : DeityAct.Walking);
    }

    /// <summary>The archangel gliding over the ground toward where it goes, as fast as it walks; the ground under it followed.</summary>
    void Glide(Deity d, float dt)
    {
        if (d.Act == DeityAct.Walking && !float.IsNaN(d.GoX))
        {
            float gx = d.GoX - d.X, gz = d.GoZ - d.Z, gl = MathF.Sqrt(gx * gx + gz * gz);
            if (gl > 1)
            {
                float step = MathF.Min(gl, d.T.Speed * dt);
                d.X += gx / gl * step; d.Z += gz / gl * step;
            }
        }
        float y = d.Y;
        FeetUnder(d, true);
        d.Y = y + (d.Y - y) * MathF.Min(1, dt * 3);
    }

    /// <summary>Its feet set under its body, a stance apart across it, on the ground there (and the body settled on it).</summary>
    void FeetUnder(Deity d, bool settle)
    {
        var (rx, rz) = Right(d.Facing);
        float y = 0;
        for (int i = 0; i < 2; i++)
        {
            float s = i == 0 ? 1 : -1;
            d.FootX[i] = d.X + rx * d.T.Stance * .5f * s; d.FootZ[i] = d.Z + rz * d.T.Stance * .5f * s;
            d.FootY[i] = Ground((int)d.FootX[i], (int)d.FootZ[i], d.Team == Team.Player);
            y += d.FootY[i] * .5f;
        }
        if (settle) d.Y = y;
    }

    /// <summary>Whoever of its enemy is under it where it comes down is crushed.</summary>
    void Crush(Deity d, float x, float y, float z, float r)
    {
        foreach (var u in Units)
            if (!u.Dead && !u.Inside && u.Airborne == null && Hostile(d, u) && Dist2(u.X, u.Z, x, z) < (r + u.T.BodyR) * (r + u.T.BodyR) && u.Y < y + 4 * VoxelWorld.PerMetre)
                Strike(d, u, 99999);
    }

    // ------------------------------------------------------------ what it does of itself

    /// <summary>
    /// The archangel left to itself: at the other god with sword and pillar; else at the thickest knot of the horde — the
    /// sword through it if it is in reach before it, the pillar on it if it is a little off, the wings at it if it is near
    /// and thick, a flight toward it if it is far; with no horde in the valley it stands, and now and then sounds the
    /// trumpet. Where more of ours stand than of the horde it does not strike.
    /// </summary>
    void ThinkHeaven(Deity d, float dt)
    {
        if (d.Act == DeityAct.Walking && !float.IsNaN(d.GoX))
        {
            Turn(d, MathF.Atan2(d.GoZ - d.Z, d.GoX - d.X), dt);
            if (Dist2(d.GoX, d.GoZ, d.X, d.Z) < MathF.Pow(d.T.Stride * .35f, 2)) { d.GoX = float.NaN; Begin(d, DeityAct.Standing); }
        }
        if (Time < d.NextThink) return;
        d.NextThink = Time + .4;
        if (d.Foe is { } foe)
        {
            float fd = MathF.Sqrt(Dist2(foe.X, foe.Z, d.X, d.Z)), fa = Off(d, foe.X, foe.Z), ux = (foe.X - d.X) / MathF.Max(1, fd), uz = (foe.Z - d.Z) / MathF.Max(1, fd);
            // too close for the sword: the wings throw it back
            if (fd < d.T.Reach * .5f) { d.Facing = MathF.Atan2(uz, ux); BeginGust(d); return; }
            if (fd < d.T.Reach * 1.1f && MathF.Abs(fa) < SweepSpread * .8f && Rng.NextDouble() < .6) { BeginSweep(d, fa > 0 ? -1 : 1); return; }
            if (fd < d.T.SlamFar && MathF.Abs(fa) < .5f) { BeginSlam(d, foe.X, foe.Z); return; }
            // (with a wing torn off it walks to it)
            if (CanFly(d)) BeginLeap(d, foe.X - ux * d.T.Reach * .7f, foe.Z - uz * d.T.Reach * .7f);
            else GoTo(d, foe.X - ux * d.T.Reach * .7f, foe.Z - uz * d.T.Reach * .7f);
            return;
        }
        var (cx, cz, score, ours) = Thickest(d);
        if (score <= 0)
        {
            if (d.Act == DeityAct.Walking) return;
            if (Time - d.ActFrom > 22 && Rng.NextDouble() < .1) BeginRoar(d);
            return;
        }
        float dist = MathF.Sqrt(Dist2(cx, cz, d.X, d.Z)), off = Off(d, cx, cz);
        if (dist < d.T.Reach + HandR * .6f && dist > d.T.Reach * .45f && MathF.Abs(off) < SweepSpread && score >= 2 && OursInSweep(d, off > 0 ? -1 : 1) * 2 <= score)
        {
            BeginSweep(d, off > 0 ? -1 : 1);
            return;
        }
        if (dist < 35 * VoxelWorld.PerMetre && MathF.Abs(off) < .6f && score >= 4 && ours == 0 && Rng.NextDouble() < .35) { BeginGust(d); return; }
        if (dist >= d.T.SlamNear && dist <= d.T.SlamFar && MathF.Abs(off) < .55f && ours == 0) { BeginSlam(d, cx, cz); return; }
        if (dist > 55 * VoxelWorld.PerMetre && CanFly(d))
        {
            float k = (dist - d.T.Reach * .8f) / dist;
            BeginLeap(d, d.X + (cx - d.X) * k, d.Z + (cz - d.Z) * k);
            return;
        }
        if (dist < d.T.SlamNear * .8f)
        {
            GoTo(d, d.X - (cx - d.X) / MathF.Max(1, dist) * d.T.Stride, d.Z - (cz - d.Z) / MathF.Max(1, dist) * d.T.Stride);
            return;
        }
        float stop = d.T.Reach * .75f;
        GoTo(d, cx + (d.X - cx) / MathF.Max(1, dist) * stop, cz + (d.Z - cz) / MathF.Max(1, dist) * stop);
    }
}
