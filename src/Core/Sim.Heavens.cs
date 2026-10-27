using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>A hailstorm over a circle of the field, and till when it lasts.</summary>
public sealed class Hailstorm { public float X, Z, R; public double Until; }

/// <summary>A pillar of fire standing on the field: where, till when, and when it next burns those at it.</summary>
public sealed class FirePost { public float X, Y, Z; public double Until, Next; }

/// <summary>
/// Two of the circle of the sky. Hail: for a few seconds stones of ice the size of a fist and more come down on a circle
/// of the field, each a body that falls as anything thrown does; where one comes down it strikes the horde there and
/// knocks it off its feet, and breaks a piece out of the horde's ladders, decks and bridges, and it melts. The pillar of
/// fire: a column of fire thirty metres high that stands on a spot a minute and a half; the horde that comes within three
/// metres of it burns and is thrown back, and at night it lights the field about it for our archers.
/// </summary>
public sealed partial class Sim
{
    public readonly List<Hailstorm> Hailstorms = new();
    public readonly List<FirePost> FirePillars = new();
    public const string HailCause = "град", PillarCause = "столп огненный";
    /// <summary>Hailstones a second over a storm; how high over the ground they come from and how fast (voxels).</summary>
    const float HailRate = 18, HailFrom = 40 * VoxelWorld.PerMetre, HailSpeed = 26 * VoxelWorld.PerMetre;
    /// <summary>How near where a stone comes down it strikes, and how near the pillar burns, and how far it lights (voxels).</summary>
    const float HailHit = 1.3f * VoxelWorld.PerMetre, PillarR = 3 * VoxelWorld.PerMetre, PillarLight = 30 * VoxelWorld.PerMetre;

    string CallHail(float x, float z, float r)
    {
        Hailstorms.Add(new Hailstorm { X = x, Z = z, R = r, Until = Time + 5 * Might });
        return null;
    }

    string RaisePillar(float x, float z)
    {
        int ix = Math.Clamp((int)x, 0, World.SX - 1), iz = Math.Clamp((int)z, 0, World.SZ - 1);
        float y = World.SurfAt(ix, iz);
        FirePillars.Add(new FirePost { X = x, Y = y, Z = z, Until = Time + 90 * Might });
        SearRound(x, y, z, PillarR);
        return null;
    }

    void TickHeavens(float dt)
    {
        for (int i = Hailstorms.Count - 1; i >= 0; i--)
        {
            var h = Hailstorms[i];
            if (Time >= h.Until) { Hailstorms.RemoveAt(i); continue; }
            float due = HailRate * dt;
            int n = (int)due + (Rng.NextDouble() < due - (int)due ? 1 : 0);
            for (int k = 0; k < n; k++)
            {
                float a = (float)Rng.NextDouble() * MathF.Tau, r = h.R * MathF.Sqrt((float)Rng.NextDouble());
                float x = h.X + MathF.Cos(a) * r, z = h.Z + MathF.Sin(a) * r;
                int ix = Math.Clamp((int)x, 2, World.SX - 3), iz = Math.Clamp((int)z, 2, World.SZ - 3);
                float y = Math.Min(World.SY - 4, World.ColTop(ix, iz) + HailFrom);
                // (a little slant, all one way, as hail comes on a wind)
                var f = Hurl(Ball(1.4f + (float)Rng.NextDouble() * 1.3f, Mat.Snow), x - 2 * VoxelWorld.PerMetre, y, z, 1.4f * VoxelWorld.PerMetre, -HailSpeed, 0, HailCause);
                if (f != null) f.Hail = true;
            }
        }
        for (int i = FirePillars.Count - 1; i >= 0; i--)
        {
            var p = FirePillars[i];
            if (Time >= p.Until) { FirePillars.RemoveAt(i); continue; }
            if (Time < p.Next) continue;
            p.Next = Time + .25;
            foreach (var u in Units)
            {
                if (u.Dead || u.Inside || u.Team != Team.Horde || Kinds.IsEngine(u.T) || u.Airborne != null) continue;
                float d2 = Dist2(u.X, u.Z, p.X, p.Z);
                if (d2 > PillarR * PillarR) continue;
                float d = MathF.Max(1, MathF.Sqrt(d2)), ox = (u.X - p.X) / d, oz = (u.Z - p.Z) / d;
                u.Burn = Math.Max(u.Burn, 3);
                Hurt(u, null, 8, PillarCause);
                if (!u.Dead) Fling(u, ox * 7 * VoxelWorld.PerMetre, 3 * VoxelWorld.PerMetre, oz * 7 * VoxelWorld.PerMetre, PillarCause);
            }
        }
    }

    /// <summary>The pillars of fire, lighting the night about them (for the laying out of the light).</summary>
    void PillarsLight(Action<float, float, float> around)
    {
        foreach (var p in FirePillars) around(p.X, p.Z, PillarLight);
    }

    /// <summary>
    /// A hailstone comes down: the horde about where it struck hit and knocked down (twice as hard in the rain), and a
    /// piece of the horde's works it struck broken out; then it melts.
    /// </summary>
    void HailLands(Flier f)
    {
        float harm = 22 * (Weather == Weather.Rain ? 2 : 1);
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Team != Team.Horde || Kinds.IsEngine(u.T) || u.Airborne != null) continue;
            if (Dist2(u.X, u.Z, f.X, f.Z) > HailHit * HailHit || MathF.Abs(u.Y + u.T.BodyH * .5f - f.Y) > 3 * VoxelWorld.PerMetre) continue;
            Hurt(u, null, harm, HailCause);
            if (!u.Dead) GoDown(u, 1.2);
        }
        bool broke = false;
        foreach (var (_, list) in SiegeWorksIn(f.X, f.Z, HailHit))
        {
            var chip = list.FindAll(v => MathF.Abs(v.y - f.Y) < HailHit);
            if (chip.Count == 0) continue;
            if (chip.Count > 12) chip.RemoveRange(12, chip.Count - 12);
            if (Loose(chip, 0, -2 * VoxelWorld.PerMetre, 0, HailCause) != null) broke = true;
        }
        if (broke) InvalidateFields();
    }
}
