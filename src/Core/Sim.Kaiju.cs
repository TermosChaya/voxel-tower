using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>A piece a god lost, flying off it and then lying on the field: whose, which, where, how fast, how big, till when.</summary>
public sealed class GodPiece
{
    public int Id;
    public Deity Of;
    public GodWound Part;
    /// <summary>Its middle (voxels), its way (voxels a second), how fast it turns over (radians a second), and how far it reaches from its middle.</summary>
    public float X, Y, Z, VX, VY, VZ, Spin, R;
    public bool Landed;
    public double Until;
}

/// <summary>
/// Two gods at each other, the only thing that stands against a god. They do not only wear each other down: struck low
/// enough they lose what they lose for good. The archangel's sword through the toad takes a leg off it, a foreleg at
/// three fifths of its strength gone and a hind leg at three tenths; the leg flies off, turning over, comes down on the
/// field, crushes whoever is under it, of either side, throws those about it off their feet and dents the ground, and lies
/// there a while; the toad without it hops the shorter. The toad's tongue at the archangel's head tears an eye out at
/// three fifths — the archangel's pillar goes wide of where it looks — and at three tenths its jaws tear a wing off,
/// which comes down as the leg does, and the archangel flies no more.
/// </summary>
public sealed partial class Sim
{
    public readonly List<GodPiece> GodPieces = new();
    int _nextGodPiece;
    /// <summary>A god lost a piece of itself to the other (the piece that flies off, or null for an eye swallowed); a piece came down.</summary>
    public event Action<Deity, GodWound, GodPiece> GodMaimed;
    public event Action<GodPiece> GodPieceLanded;
    const GodWound Legs = GodWound.ForeRight | GodWound.ForeLeft | GodWound.HindRight | GodWound.HindLeft,
        Forelegs = GodWound.ForeRight | GodWound.ForeLeft, Eyes = GodWound.RightEye | GodWound.LeftEye, Wings = GodWound.RightWing | GodWound.LeftWing;

    /// <summary>How many wounds a god is due by how far it is struck down: one at three fifths gone, two at three tenths left.</summary>
    static int WoundsDue(Deity d) => d.Hp < d.T.Hp * .3f ? 2 : d.Hp < d.T.Hp * .6f ? 1 : 0;

    /// <summary>Whether a god can still fly (the archangel with both its wings).</summary>
    public static bool CanFly(Deity d) => d.T.Winged && (d.Wounds & Wings) == 0;

    /// <summary>
    /// The archangel's sword through the toad, if it is due a leg: the first a foreleg, the second a hind leg, of each
    /// pair the one on the side the sword is; off it flies, away from the archangel.
    /// </summary>
    void Sever(Deity d, Deity foe)
    {
        if (foe.T.Kind != DeityKind.Toad || foe.Lost(Legs) >= WoundsDue(foe)) return;
        bool fore = foe.Lost(Forelegs) == 0;
        var (rx, rz) = Right(foe.Facing);
        bool right = (d.HandX - foe.X) * rx + (d.HandZ - foe.Z) * rz >= 0;
        var w = fore ? (right ? GodWound.ForeRight : GodWound.ForeLeft) : (right ? GodWound.HindRight : GodWound.HindLeft);
        if ((foe.Wounds & w) != 0) w = w switch { GodWound.ForeRight => GodWound.ForeLeft, GodWound.ForeLeft => GodWound.ForeRight, GodWound.HindRight => GodWound.HindLeft, _ => GodWound.HindRight };
        foe.Wounds |= w;
        float fx = MathF.Cos(foe.Facing), fz = MathF.Sin(foe.Facing), along = fore ? .55f : -.45f, across = w is GodWound.ForeRight or GodWound.HindRight ? .5f : -.5f;
        float lx = foe.X + (fx * along + rx * across) * foe.T.Stance, lz = foe.Z + (fz * along + rz * across) * foe.T.Stance;
        Throw(foe, w, lx, foe.Y + foe.T.Height * .3f, lz, lx - d.X, lz - d.Z, foe.T.Height * .4f);
        Say(fore ? "Архангел отсёк Великой жабе переднюю лапу." : "Архангел отсёк Великой жабе заднюю лапу: прыгает она теперь недалеко.", d.Team == Team.Player ? 1 : 3, lx, lz);
    }

    /// <summary>
    /// The toad's tongue at the archangel's head, if it is due a wound: an eye torn out first, swallowed with the
    /// tongue; then a wing torn off by the jaws, flying off away from the toad.
    /// </summary>
    void Tear(Deity d, Deity foe)
    {
        if (foe.T.Kind != DeityKind.Archangel || foe.Lost(Eyes | Wings) >= WoundsDue(foe)) return;
        if (foe.Lost(Eyes) == 0)
        {
            foe.Wounds |= GodWound.RightEye;
            GodMaimed?.Invoke(foe, GodWound.RightEye, null);
            Say("Великая жаба вырвала Архангелу глаз. Огненный столп теперь бьёт мимо.", d.Team == Team.Horde ? 3 : 1, foe.X, foe.Z);
            return;
        }
        foe.Wounds |= GodWound.LeftWing;
        var (rx, rz) = Right(foe.Facing);
        float wx = foe.X - rx * foe.T.Stance, wz = foe.Z - rz * foe.T.Stance;
        Throw(foe, GodWound.LeftWing, wx, foe.Y + foe.T.Height * .7f, wz, wx - d.X, wz - d.Z, foe.T.Height * .35f);
        Say("Великая жаба оторвала Архангелу крыло: летать он больше не может.", d.Team == Team.Horde ? 3 : 1, wx, wz);
    }

    /// <summary>A piece off a god, thrown up and away along a way (x, z), turning over.</summary>
    void Throw(Deity of, GodWound part, float x, float y, float z, float wx, float wz, float r)
    {
        float l = MathF.Max(1, MathF.Sqrt(wx * wx + wz * wz));
        var rem = new GodPiece
        {
            Id = _nextGodPiece++, Of = of, Part = part, X = x, Y = y, Z = z, R = r, Until = Time + 120,
            VX = wx / l * 9 * VoxelWorld.PerMetre, VY = 11 * VoxelWorld.PerMetre, VZ = wz / l * 9 * VoxelWorld.PerMetre, Spin = 1.4f + .4f * (float)Rng.NextDouble(),
        };
        GodPieces.Add(rem);
        GodMaimed?.Invoke(of, part, rem);
        GodShook?.Invoke(of, x, y, z, 2.5f);
    }

    /// <summary>What has flown off the gods, on its way down; what has lain there its while, gone.</summary>
    void TickGodPieces(float dt)
    {
        for (int i = GodPieces.Count - 1; i >= 0; i--)
        {
            var r = GodPieces[i];
            if (Time >= r.Until) { GodPieces.RemoveAt(i); continue; }
            if (r.Landed) continue;
            r.VY -= Gravity * dt;
            r.X += r.VX * dt; r.Y += r.VY * dt; r.Z += r.VZ * dt;
            int ix = Math.Clamp((int)r.X, 2, World.SX - 3), iz = Math.Clamp((int)r.Z, 2, World.SZ - 3);
            float g = World.ColTop(ix, iz) + 1;
            if (r.VY < 0 && r.Y - r.R * .3f <= g) { r.Y = g + r.R * .3f; r.Landed = true; Thud(r); }
        }
    }

    /// <summary>
    /// A piece of a god comes down: whoever of either side is under it crushed, those about it thrown off their feet, a
    /// bowl of the ground pressed out under it and thrown up, the valley shaken.
    /// </summary>
    void Thud(GodPiece r)
    {
        string cause = r.Part is GodWound.RightWing or GodWound.LeftWing ? "раздавлен крылом архангела" : "раздавлен лапой жабы";
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null) continue;
            float d = MathF.Sqrt(Dist2(u.X, u.Z, r.X, r.Z));
            if (d < r.R * .55f) Hurt(u, null, 99999, cause);
            else if (d < r.R * 1.6f) GoDown(u, 2.5 * (1 - d / (r.R * 1.6f)) + .5);
        }
        var bowl = new List<(int x, int y, int z)>();
        float crater = r.R * .45f;
        int ri = (int)crater, top0 = (int)(r.Y - r.R * .3f);
        for (int dz = -ri; dz <= ri; dz++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                float k = (dx * dx + dz * dz) / (crater * crater);
                int vx = (int)r.X + dx, vz = (int)r.Z + dz;
                if (k > 1 || vx < 2 || vz < 2 || vx >= World.SX - 2 || vz >= World.SZ - 2) continue;
                int top = World.ColTop(vx, vz), bottom = Math.Max(4, top0 - (int)(crater * .25f * (1 - k)));
                for (int vy = top; vy >= bottom; vy--) if (Breakable(World.Get(vx, vy, vz))) bowl.Add((vx, vy, vz));
            }
        Scatter(r.Of, bowl, r.X, r.Y - r.R * .3f, r.Z, 0, 0, crater * 1.6f, crater * 1.2f, 260);
        Jar(r.X, r.Y, r.Z, r.R * 1.2f, .6f);
        GodShook?.Invoke(r.Of, r.X, r.Y, r.Z, 3.5f);
        GodPieceLanded?.Invoke(r);
    }
}
