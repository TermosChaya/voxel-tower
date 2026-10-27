using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The circle of the angels: the lesser angels the household's spells send (Deities.cs). The guardian hovers over a spot
/// a minute, and ours under it take a third of the harm. The cherub stands where it is set with its sword of fire and
/// cuts down whoever of the horde comes near, as the archangel sweeps but smaller. The destroying angel passes over a
/// stretch of the field, and one in three of the horde under it falls dead. And the heavenly host: six warriors of
/// heaven come down in a pillar of light, fight for us a minute as the risen dead once did, and go up again.
/// </summary>
public sealed partial class Sim
{
    /// <summary>How far about the guardian ours are kept (voxels).</summary>
    const float WardR = 6 * VoxelWorld.PerMetre;
    /// <summary>The destroyer: seconds it takes to pass over, how high over the ground it goes, how far it begins and ends from the spot (voxels).</summary>
    public const float PassFor = 3.5f;
    const float PassHigh = 14 * VoxelWorld.PerMetre, PassFar = 35 * VoxelWorld.PerMetre, DoomR = 10 * VoxelWorld.PerMetre;
    /// <summary>What the chronicle says of an angel of the host gone up again.</summary>
    public const string AscendCause = "вознёсся";

    /// <summary>The host came down at a spot (for the light over it); one of it went up again.</summary>
    public event Action<float, float, float> HostCame, AngelRose;

    /// <summary>What ours take of a blow under a guardian angel: a third.</summary>
    float Warded(Unit b)
    {
        if (b.Team != Team.Player || Gods.Count == 0) return 1;
        foreach (var g in Gods)
            if (!g.Gone && g.T.Kind == DeityKind.Guardian && g.Act is DeityAct.Standing or DeityAct.Walking && Dist2(b.X, b.Z, g.X, g.Z) < WardR * WardR)
                return 1 / 3f;
        return 1;
    }

    /// <summary>
    /// A lesser angel left to itself: the guardian only hovers; the cherub turns on whoever of the horde comes within its
    /// sword's reach and cuts, the other way each time.
    /// </summary>
    void ThinkAngel(Deity d, float dt)
    {
        if (d.T.Kind != DeityKind.Cherub || Time < d.NextThink) return;
        d.NextThink = Time + .25;
        Unit near = null; float best = d.T.Reach + HandR * .8f;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null || !Hostile(d, u)) continue;
            float dist = MathF.Sqrt(Dist2(u.X, u.Z, d.X, d.Z));
            if (dist < best) { best = dist; near = u; }
        }
        if (near == null) return;
        d.Facing = MathF.Atan2(near.Z - d.Z, near.X - d.X);
        BeginSweep(d, -d.Side);
    }

    // ------------------------------------------------------------ the destroyer

    /// <summary>
    /// The destroying angel sent over a spot: it comes from the citadel's side high over the field and passes on over
    /// the spot toward the horde's.
    /// </summary>
    string SendDestroyer(float x, float z)
    {
        float ux = 1, uz = 0;
        if (Site.HasKeep)
        {
            float kx = Site.KeepX - x, kz = Site.KeepZ - z, kl = MathF.Sqrt(kx * kx + kz * kz);
            if (kl > 1) { ux = kx / kl; uz = kz / kl; }
        }
        string why = Awaken(DeityKind.Destroyer, x, z, 0, out var d);
        if (why != null) return why;
        d.LeapFromX = x + ux * PassFar; d.LeapFromZ = z + uz * PassFar;
        d.AimX = x - ux * PassFar; d.AimZ = z - uz * PassFar;
        d.ThrowX = x; d.ThrowZ = z;
        d.X = d.LeapFromX; d.Z = d.LeapFromZ;
        d.Y = Ground((int)Math.Clamp(d.X, 0, World.SX - 1), (int)Math.Clamp(d.Z, 0, World.SZ - 1)) + PassHigh;
        d.Facing = MathF.Atan2(-uz, -ux);
        d.Up = 1;
        Begin(d, DeityAct.Pass);
        return null;
    }

    /// <summary>The destroyer on its way over: over the spot, the doom; at the end of its way, gone.</summary>
    void Passing(Deity d, float dt)
    {
        float p = Math.Clamp(d.Into(Time) / PassFor, 0, 1);
        d.X = Lerp(d.LeapFromX, d.AimX, p); d.Z = Lerp(d.LeapFromZ, d.AimZ, p);
        float y = Ground((int)Math.Clamp(d.X, 0, World.SX - 1), (int)Math.Clamp(d.Z, 0, World.SZ - 1)) + PassHigh;
        d.Y += (y - d.Y) * MathF.Min(1, dt * 2);
        FeetUnder(d, false);
        if (!d.Landed && p >= .5f) { d.Landed = true; Doom(d, d.ThrowX, d.ThrowZ); }
        if (p >= 1) Gone(d);
    }

    /// <summary>
    /// The doom: of the horde under it, one in three falls dead where it stands, without a wound — a leader or a troll
    /// one in six; engines and those inside are let be.
    /// </summary>
    void Doom(Deity d, float x, float z)
    {
        int fell = 0;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Team != Team.Horde || Kinds.IsEngine(u.T) || Dist2(u.X, u.Z, x, z) > DoomR * DoomR) continue;
            if (Rng.NextDouble() >= (u.Leader || u.T == Kinds.Troll ? 1 / 6.0 : 1 / 3.0)) continue;
            Kill(u, null, d.T.Cause);
            d.Kills++;
            fell++;
        }
        Say(fell > 0 ? $"Ангел-губитель прошёл: пали {fell}." : "Ангел-губитель прошёл, но никого не взял.", 1, x, z);
    }

    // ------------------------------------------------------------ the heavenly host

    /// <summary>The heavenly host: six warriors of heaven come down about the spot, each where it can stand.</summary>
    string SendHost(float x, float z, float r)
    {
        int n = 0;
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.Tau / 6, rr = i == 0 ? 0 : r * .45f;
            int cell = NearestStand((int)(x + MathF.Cos(a) * rr), (int)(z + MathF.Sin(a) * rr), Kinds.Angel.Move.Height);
            if (cell < 0) continue;
            var u = Spawn(Kinds.Angel, cell);
            u.CrumbleAt = Time + 60 * Might;
            u.Facing = MathF.PI;
            n++;
        }
        if (n == 0) return "ангелам здесь негде встать";
        HostCame?.Invoke(x, World.SurfAt(Math.Clamp((int)x, 0, World.SX - 1), Math.Clamp((int)z, 0, World.SZ - 1)), z);
        return null;
    }
}
