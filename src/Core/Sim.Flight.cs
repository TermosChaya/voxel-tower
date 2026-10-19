using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Something flying through the air: a piece of the world broken loose — its voxels as they stood, about its middle —
/// or someone thrown bodily. It flies as anything thrown does, tumbling, and comes down where it comes down.
/// </summary>
public sealed class Flier
{
    public int Id;
    /// <summary>Its middle and how fast it goes, in voxels and voxels a second.</summary>
    public float X, Y, Z, VX, VY, VZ;
    /// <summary>How it tumbles, for the eye: the level axis it turns about, how far it has turned, how fast.</summary>
    public float AX = 1, AZ, Turn, Spin;
    /// <summary>The voxels it is made of, about its middle, and what each is; none for one thrown bodily.</summary>
    public readonly List<(sbyte dx, sbyte dy, sbyte dz, byte m)> Voxels = new();
    /// <summary>One thrown bodily, or null.</summary>
    public Unit Unit;
    /// <summary>In the grip of telekinesis: it goes where the hand is and does not fall, till it is let go or the grip fails.</summary>
    public bool Held;
    public double HeldUntil;
    /// <summary>One of ours let down gently from the grip: it comes to no harm landing.</summary>
    public bool Gentle;
    /// <summary>How far its voxels reach from its middle.</summary>
    public float R;
    public double Since;
    /// <summary>What sent it flying, for the chronicle of whoever it strikes.</summary>
    public string Cause = "";
    /// <summary>Voxels of it that were burning when it broke loose: it sets alight what it lands on.</summary>
    public int Burning;
    /// <summary>When it last struck someone while held and swung.</summary>
    public double LastBlow = -99;
    /// <summary>A catapult's stone: how much it breaks out where it strikes (voxels, as a ball's reach), and the engine that threw it.</summary>
    public int Smash;
    public Unit By;
}

/// <summary>
/// The air: what flies through it, and what becomes of it. A piece of the world broken loose — by a blast, by the
/// hand of telekinesis — goes up as one body and comes down as one, and where it comes down it strikes whoever is
/// there, knocks a little out of what it hits if it comes heavy and hard, and lies there in a heap of what it was made
/// of, broken. One thrown bodily flies the same way and is hurt as hard as it lands. Held in the grip of telekinesis, a
/// piece or a body goes where the player's hand goes, and flies off the way it was swung when it is let go.
/// </summary>
public sealed partial class Sim
{
    public readonly List<Flier> Fliers = new();
    int _nextFlier = 1;
    /// <summary>How hard things fall: voxels a second, each second.</summary>
    public const float Gravity = 9.8f * VoxelWorld.PerMetre;
    /// <summary>The most voxels in the air at once, all pieces together.</summary>
    public const int MaxAloft = 24000;
    /// <summary>How long the grip of telekinesis holds, in seconds.</summary>
    public const double GripFor = 8;

    /// <summary>Voxels in the air now.</summary>
    public int Aloft
    {
        get { int n = 0; foreach (var f in Fliers) n += f.Voxels.Count; return n; }
    }

    /// <summary>A piece came down: where, how many voxels, how fast (for the dust and the sound).</summary>
    public event Action<float, float, float, int, float> Landed;

    /// <summary>Whether a voxel can be broken loose: anything but the bedrock, the citadel, the black stone, water and air.</summary>
    public static bool Breakable(byte m) => m != Mat.Air && m != Mat.Water && Mat.Def[m].Group != MatGroup.Hard;

    /// <summary>What a voxel that flew is once it lies again: stone broken stone, timber splinters, turf loose earth;
    /// leaves and grass are gone (0).</summary>
    static byte Shattered(byte m)
    {
        if (Mat.IsTrunk(m) || Mat.IsDecor[m]) return Mat.Lumber;
        if (Mat.IsVeg[m] || m == Mat.Tuft) return 0;
        if (m is Mat.Rock or Mat.Moss or Mat.Ruin) return Mat.Rubble;
        if (m is Mat.Grass or Mat.GrassDry or Mat.GrassLush or Mat.Flower or Mat.Bloom or Mat.Path) return Mat.Soil;
        return Broken(m);
    }

    /// <summary>
    /// Breaks voxels loose into a piece of the world that flies off as one: they are taken out of where they stood (and
    /// what they held up comes down after). Returns the piece, or null if there was nothing to it, or too much aloft.
    /// </summary>
    public Flier Loose(List<(int x, int y, int z)> voxels, float vx, float vy, float vz, string cause, int room = -1)
    {
        if (voxels.Count == 0) return null;
        if (room < 0) room = MaxAloft - Aloft;
        if (voxels.Count > room) return null;
        long sx = 0, sy = 0, sz = 0;
        foreach (var (x, y, z) in voxels) { sx += x; sy += y; sz += z; }
        int cx = (int)(sx / voxels.Count), cy = (int)(sy / voxels.Count), cz = (int)(sz / voxels.Count);
        var f = new Flier { Id = _nextFlier++, X = cx + .5f, Y = cy + .5f, Z = cz + .5f, VX = vx, VY = vy, VZ = vz, Since = Time, Cause = cause };
        float r2 = 0;
        foreach (var (x, y, z) in voxels)
        {
            byte m = World.Get(x, y, z);
            int dx = x - cx, dy = y - cy, dz = z - cz;
            if (!Breakable(m) || Math.Abs(dx) > 120 || Math.Abs(dy) > 120 || Math.Abs(dz) > 120) continue;
            f.Voxels.Add(((sbyte)dx, (sbyte)dy, (sbyte)dz, m));
            r2 = Math.Max(r2, dx * dx + dy * dy + dz * dz);
            World.Set(x, y, z, Mat.Air);
            if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
            if (Burning.Remove(WaterPlanner.Pack(x, y, z))) f.Burning++;
        }
        if (f.Voxels.Count == 0) return null;
        f.R = MathF.Sqrt(r2) + .7f;
        // it tumbles about a level axis across the way it flies, the smaller the faster
        float h = MathF.Sqrt(vx * vx + vz * vz);
        if (h > .1f) { f.AX = -vz / h; f.AZ = vx / h; }
        else { float a = (f.Id * 2.39996f) % MathF.Tau; f.AX = MathF.Cos(a); f.AZ = MathF.Sin(a); }
        f.Spin = (2 + f.Id % 7 * .7f) / MathF.Max(1, f.R / 4);
        Fliers.Add(f);
        return f;
    }

    /// <summary>A thing thrown into the air that was not part of the world: its voxels about its middle, as given.</summary>
    public Flier Hurl(List<(sbyte dx, sbyte dy, sbyte dz, byte m)> voxels, float x, float y, float z, float vx, float vy, float vz, string cause)
    {
        if (voxels.Count == 0 || Aloft + voxels.Count > MaxAloft) return null;
        var f = new Flier { Id = _nextFlier++, X = x, Y = y, Z = z, VX = vx, VY = vy, VZ = vz, Since = Time, Cause = cause };
        float r2 = 0;
        foreach (var v in voxels) { f.Voxels.Add(v); r2 = Math.Max(r2, v.dx * v.dx + v.dy * v.dy + v.dz * v.dz); }
        f.R = MathF.Sqrt(r2) + .7f;
        float h = MathF.Sqrt(vx * vx + vz * vz);
        if (h > .1f) { f.AX = -vz / h; f.AZ = vx / h; } else f.AX = 1;
        f.Spin = 2 + f.Id % 5 * .6f;
        Fliers.Add(f);
        return f;
    }

    /// <summary>A ball of a material reaching this many voxels from its middle.</summary>
    public static List<(sbyte dx, sbyte dy, sbyte dz, byte m)> Ball(float r, byte m)
    {
        var list = new List<(sbyte, sbyte, sbyte, byte)>();
        int ri = (int)MathF.Ceiling(r);
        for (int dy = -ri; dy <= ri; dy++)
            for (int dz = -ri; dz <= ri; dz++)
                for (int dx = -ri; dx <= ri; dx++)
                    if (dx * dx + dy * dy + dz * dz <= r * r) list.Add(((sbyte)dx, (sbyte)dy, (sbyte)dz, m));
        return list;
    }

    /// <summary>
    /// Throws one bodily: off its feet and into the air, to come down where it comes down. Not one in the citadel, nor
    /// one held fast in chains. Null when it cannot be thrown.
    /// </summary>
    public Flier Fling(Unit u, float vx, float vy, float vz, string cause)
    {
        if (u.Dead && u.DiedAt < 0 || u.Inside || u.ChainedUntil > Time) return null;
        if (u.Airborne is { } up) { if (!up.Held) { up.VX += vx; up.VY += vy; up.VZ += vz; } return up; }
        StopCarve(u); StopBuild(u);
        if (u.Team == Team.Player) ReleaseSite(u);
        u.Path = null; u.Target = null; u.State = UnitState.Idle; u.Swing = 0;
        // one leading a captive lets go of it, and one led goes free of its leader
        if (u.Captive != null) { u.Captive.Captor = null; u.Captive = null; }
        if (u.Captor != null) { u.Captor.Captive = null; u.Captor = null; }
        // a siege tower torn from the wall takes its ladder with it
        if (u.T == Kinds.SiegeTower && u.Docked.Count > 0) TowerFell(u);
        var f = new Flier
        {
            Id = _nextFlier++, X = u.X, Y = u.Y + u.T.BodyH * .5f, Z = u.Z, VX = vx, VY = vy, VZ = vz, Unit = u, Since = Time, Cause = cause,
            R = u.T.BodyR + 1, Spin = 3 + u.Id % 5 * .6f,
        };
        float h = MathF.Sqrt(vx * vx + vz * vz);
        if (h > .1f) { f.AX = -vz / h; f.AZ = vx / h; } else { f.AX = MathF.Cos(u.Facing); f.AZ = MathF.Sin(u.Facing); }
        u.Airborne = f;
        Fliers.Add(f);
        return f;
    }

    // ------------------------------------------------------------ telekinesis: the hand

    /// <summary>Where the hand of telekinesis holds what it holds, in voxels.</summary>
    float _handX, _handY, _handZ;

    /// <summary>The piece or body held by telekinesis, if any.</summary>
    public Flier HeldNow
    {
        get
        {
            foreach (var f in Fliers) if (f.Held) return f;
            return null;
        }
    }

    /// <summary>Moves the hand that holds: what is held follows it.</summary>
    public void HoldAt(float x, float y, float z) { _handX = x; _handY = y; _handZ = z; }

    /// <summary>Lets go of what is held: it flies on the way it was swung; one of ours, let down slowly, is set down gently.</summary>
    public void LetGo()
    {
        foreach (var f in Fliers)
        {
            if (!f.Held) continue;
            f.Held = false;
            float s = MathF.Sqrt(f.VX * f.VX + f.VY * f.VY + f.VZ * f.VZ);
            if (f.Unit is { Team: Team.Player } && s < 4 * VoxelWorld.PerMetre) { f.Gentle = true; f.VX *= .3f; f.VY = 0; f.VZ *= .3f; continue; }
            f.VX *= 1.25f; f.VY = f.VY * 1.25f + 10; f.VZ *= 1.25f;
            float h = MathF.Sqrt(f.VX * f.VX + f.VZ * f.VZ);
            if (h > .1f) { f.AX = -f.VZ / h; f.AZ = f.VX / h; }
        }
    }

    /// <summary>
    /// Takes hold of what is at a spot (voxels): one standing there — a raider, a beast, one of ours, even an engine —
    /// or else a ball of a metre of whatever is there to be broken loose. Null when it is held; otherwise why nothing
    /// could be.
    /// </summary>
    string Grab(float x, float y, float z)
    {
        if (HeldNow != null) return "рука уже держит";
        Unit who = null; float bd = 2.5f * VoxelWorld.PerMetre;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Envoy || u.Airborne != null || u.ChainedUntil > Time) continue;
            float my = u.Y + u.T.BodyH * .5f;
            float d = MathF.Sqrt((u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z) + (my - y) * (my - y) * .3f) - u.T.BodyR;
            if (d < bd) { bd = d; who = u; }
        }
        Flier f;
        if (who != null) f = Fling(who, 0, 0, 0, "телекинез");
        else
        {
            const int R = VoxelWorld.PerMetre;
            var ball = new List<(int, int, int)>();
            int ix = (int)x, iy = (int)y, iz = (int)z;
            for (int dy = -R; dy <= R; dy++)
                for (int dz = -R; dz <= R; dz++)
                    for (int dx = -R; dx <= R; dx++)
                    {
                        if (dx * dx + dy * dy + dz * dz > R * R) continue;
                        int vx = ix + dx, vy = iy + dy, vz = iz + dz;
                        if (World.InBounds(vx, vy, vz) && vy > 3 && Breakable(World.Get(vx, vy, vz))) ball.Add((vx, vy, vz));
                    }
            if (ball.Count < 24) return "здесь нечего поднять: наведите на камень, стену, землю или на кого-то";
            f = Loose(ball, 0, 0, 0, "телекинез");
            if (f == null) return "в воздухе и так слишком много";
        }
        if (f == null) return "его не поднять";
        f.Held = true;
        f.HeldUntil = Time + GripFor;
        _handX = f.X; _handY = f.Y + 3 * VoxelWorld.PerMetre; _handZ = f.Z;
        return null;
    }

    /// <summary>How readily a thing goes where the hand pulls it: a man or a stone at once, a troll slower, a siege tower slowest.</summary>
    static float Lightness(Flier f) =>
        f.Unit != null ? MathF.Min(1, 15 / f.Unit.T.BodyH) : MathF.Min(1, 1.6f / MathF.Sqrt(1 + f.Voxels.Count / 900f));

    /// <summary>Held: it goes where the hand is, swiftly but not at once, sliding along what it meets; swung hard into
    /// someone, it strikes them.</summary>
    void Hold(Flier f, float dt)
    {
        if (Time > f.HeldUntil) { f.Held = false; return; }
        float k = Lightness(f), pull = 6 * k, max = 30 * VoxelWorld.PerMetre * k;
        float vx = (_handX - f.X) * pull, vy = (_handY - f.Y) * pull, vz = (_handZ - f.Z) * pull;
        float s = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
        if (s > max) { vx *= max / s; vy *= max / s; vz *= max / s; s = max; }
        // it eases into the new way rather than turning on the spot
        float ease = Math.Min(1, dt * 10);
        f.VX += (vx - f.VX) * ease; f.VY += (vy - f.VY) * ease; f.VZ += (vz - f.VZ) * ease;
        int steps = Math.Max(1, (int)MathF.Ceiling(s * dt / 1.5f));
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            // still in what it was torn out of, or pressed into something: it comes out of it any way it is pulled
            if (Blocked(f, f.X, f.Y, f.Z)) { f.X += f.VX * h; f.Y += f.VY * h; f.Z += f.VZ * h; continue; }
            // along each way in turn, so it slides along a wall instead of sticking to it
            if (!Blocked(f, f.X + f.VX * h, f.Y, f.Z)) f.X += f.VX * h; else f.VX = 0;
            if (!Blocked(f, f.X, f.Y + f.VY * h, f.Z)) f.Y += f.VY * h; else f.VY = 0;
            if (!Blocked(f, f.X, f.Y, f.Z + f.VZ * h)) f.Z += f.VZ * h; else f.VZ = 0;
        }
        f.Turn += f.Spin * .2f * dt;
        if (f.Unit != null) Carry(f);
        // a stone swung through a band strikes whoever it passes, and a body swung into them knocks them down
        float speed = MathF.Sqrt(f.VX * f.VX + f.VY * f.VY + f.VZ * f.VZ);
        if (speed < 7 * VoxelWorld.PerMetre || Time - f.LastBlow < .4) return;
        float blow = f.Unit != null ? (speed - 5.5f * VoxelWorld.PerMetre) * .35f : Blow(f, speed) * .6f;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null) continue;
            float dx = u.X - f.X, dz = u.Z - f.Z, dy = u.Y + u.T.BodyH * .5f - f.Y, reach = f.R + u.T.BodyR + 2;
            if (dx * dx + dz * dz + dy * dy * .5f > reach * reach) continue;
            Hurt(u, null, blow, f.Cause);
            f.LastBlow = Time;
        }
    }

    /// <summary>One carried through the air is where its middle is.</summary>
    void Carry(Flier f)
    {
        var u = f.Unit;
        u.X = f.X; u.Y = f.Y - u.T.BodyH * .5f; u.Z = f.Z;
        u.Cell = Nav.CellOf(Math.Clamp(u.X, 0, World.SX - 1), Math.Clamp(u.Y + 1, 0, World.SY - 1), Math.Clamp(u.Z, 0, World.SZ - 1));
    }

    /// <summary>How hard a piece strikes: the heavier and the faster, the harder.</summary>
    static float Blow(Flier f, float speed) => Math.Min(320, .045f * MathF.Sqrt(Math.Max(1, f.Voxels.Count)) * speed);

    /// <summary>A voxel a flying thing stops at: anything solid, and water.</summary>
    bool Stops(float x, float y, float z)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y), iz = (int)MathF.Floor(z);
        if (!World.InBounds(ix, iy, iz)) return iy < 1;
        byte m = World.Get(ix, iy, iz);
        return m == Mat.Water || m != Mat.Air && !Mat.Passable[m];
    }

    /// <summary>Whether a held thing would be in something solid with its middle here: its edges each way, and its feet.</summary>
    bool Blocked(Flier f, float x, float y, float z)
    {
        if (x < 2 || z < 2 || x >= World.SX - 2 || z >= World.SZ - 2 || y >= World.SY - 2) return true;
        if (f.Unit != null)
        {
            float half = f.Unit.T.BodyH * .5f, r = f.Unit.T.BodyR * .7f;
            return Stops(x, y - half + .5f, z) || Stops(x, y, z) || Stops(x, y + half - .5f, z)
                || Stops(x + r, y, z) || Stops(x - r, y, z) || Stops(x, y, z + r) || Stops(x, y, z - r);
        }
        float e = f.R * .5f;
        return Stops(x, y, z) || Stops(x + e, y, z) || Stops(x - e, y, z) || Stops(x, y + e, z) || Stops(x, y - e, z) || Stops(x, y, z + e) || Stops(x, y, z - e);
    }

    /// <summary>
    /// Whether a flying thing has struck something: a piece the ground, a wall or (one of any size) somebody; one thrown,
    /// the ground under its feet. One thrown against a wall or a trunk, or with its head against what is over it, is
    /// stopped there — and hurt, if it came hard — and falls on down from it.
    /// </summary>
    bool Hits(Flier f)
    {
        float s = MathF.Sqrt(f.VX * f.VX + f.VY * f.VY + f.VZ * f.VZ);
        float ux = s > .01f ? f.VX / s : 0, uy = s > .01f ? f.VY / s : -1, uz = s > .01f ? f.VZ / s : 0;
        if (f.Unit != null)
        {
            float feet = f.Y - f.Unit.T.BodyH * .5f, hs = MathF.Sqrt(f.VX * f.VX + f.VZ * f.VZ);
            if (hs > .01f && Stops(f.X + f.VX / hs * f.R, f.Y, f.Z + f.VZ / hs * f.R))
            {
                float hard = (hs - 5.5f * VoxelWorld.PerMetre) * .5f;
                if (hard > 0) Hurt(f.Unit, null, hard, f.Cause);
                f.VX = f.VZ = 0;
                f.X -= ux * .5f; f.Z -= uz * .5f;
            }
            if (f.VY > 0 && Stops(f.X, f.Y + f.Unit.T.BodyH * .5f, f.Z)) f.VY = 0;
            return f.VY <= 0 && Stops(f.X, feet - .5f, f.Z);
        }
        // a piece just torn out is still in its socket: only its middle can strike anything till it is out
        if (Time - f.Since < .15) return Stops(f.X, f.Y, f.Z);
        float lead = f.R * .7f;
        if (Stops(f.X + ux * lead, f.Y + uy * lead, f.Z + uz * lead) || f.VY <= 0 && Stops(f.X, f.Y - lead, f.Z)) return true;
        if (f.Voxels.Count < 24) return false;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null || u == f.By) continue;
            float dx = u.X - f.X, dz = u.Z - f.Z, dy = u.Y + u.T.BodyH * .5f - f.Y, reach = f.R * .8f + u.T.BodyR;
            if (dx * dx + dz * dz < reach * reach && MathF.Abs(dy) < u.T.BodyH * .5f + f.R * .8f) return true;
        }
        return false;
    }

    /// <summary>What flies, flies; what is held, follows the hand; what strikes something, comes down.</summary>
    void TickFlight(float dt)
    {
        if (Fliers.Count == 0) return;
        for (int i = Fliers.Count - 1; i >= 0; i--)
        {
            if (i >= Fliers.Count) continue;
            var f = Fliers[i];
            if (f.Held && (f.Unit is { Dead: true } || f.Unit is { Inside: true })) f.Held = false;
            if (f.Held) { Hold(f, dt); continue; }
            f.Turn += f.Spin * dt;
            float speed = MathF.Sqrt(f.VX * f.VX + f.VY * f.VY + f.VZ * f.VZ);
            // in steps of a voxel or so, so nothing flies through a wall
            int steps = Math.Max(1, (int)MathF.Ceiling(speed * dt / 1.2f));
            float h = dt / steps;
            bool down = false;
            for (int k = 0; k < steps && !down; k++)
            {
                f.VY -= Gravity * h;
                f.X += f.VX * h; f.Y += f.VY * h; f.Z += f.VZ * h;
                if (f.X < 2 || f.Z < 2 || f.X >= World.SX - 2 || f.Z >= World.SZ - 2 || f.Y < 2) { down = true; break; }
                // nothing goes up out of the world: it stops there and falls back
                if (f.Y > World.SY - 4) { f.Y = World.SY - 4; f.VY = Math.Min(0, f.VY); }
                down = Hits(f);
            }
            if (f.Unit != null) Carry(f);
            if (!down && Time - f.Since < 20) continue;
            Fliers.Remove(f);
            ComeDown(f);
        }
    }

    /// <summary>
    /// Something comes down. One thrown lands on its feet or not, hurt as hard as it came down; a piece strikes whoever
    /// it lands on, knocks a little out of what it hits if it comes heavy and hard, and lies there in a heap, broken.
    /// </summary>
    void ComeDown(Flier f)
    {
        float speed = MathF.Sqrt(f.VX * f.VX + f.VY * f.VY + f.VZ * f.VZ);
        if (f.Unit != null) { Alight(f, speed); return; }
        if (f.Smash > 0) StoneLands(f, speed);
        int ix = Math.Clamp((int)f.X, 2, World.SX - 3), iz = Math.Clamp((int)f.Z, 2, World.SZ - 3), iy = Math.Clamp((int)f.Y, 2, World.SY - 3);
        // a catapult's stone has done its harm already, as stones do
        float blow = f.Smash > 0 ? 0 : Blow(f, speed), reach = f.R + 1.2f * VoxelWorld.PerMetre;
        if (blow > 3)
            foreach (var u in Units)
            {
                if (u.Dead || u.Inside || u.Airborne != null) continue;
                float dx = u.X - f.X, dz = u.Z - f.Z, dy = u.Y + u.T.BodyH * .5f - f.Y, d = MathF.Sqrt(dx * dx + dz * dz + dy * dy * .5f);
                if (d < reach) Hurt(u, null, blow * (1 - .5f * d / reach) * (Kinds.IsEngine(u.T) ? 1.5f : 1), f.Cause);
            }
        // heavy and hard, it knocks out a little of what it strikes, which lies in the heap with it
        if (blow > 60 && f.Voxels.Count > 150)
        {
            int cr = (int)(f.R * .45f * MathF.Min(1.5f, blow / 120));
            float s = MathF.Max(1, speed);
            int bx = ix + (int)(f.VX / s * f.R * .6f), by = iy + (int)(f.VY / s * f.R * .6f), bz = iz + (int)(f.VZ / s * f.R * .6f);
            int knocked = 0;
            for (int dy = -cr; dy <= cr; dy++)
                for (int dz = -cr; dz <= cr; dz++)
                    for (int dx = -cr; dx <= cr; dx++)
                    {
                        int x = bx + dx, y = by + dy, z = bz + dz;
                        if (dx * dx + dy * dy + dz * dz > cr * cr || !World.InBounds(x, y, z) || y < 4) continue;
                        byte m = World.Get(x, y, z);
                        if (!Breakable(m)) continue;
                        // powder it falls on is not knocked aside: it goes up
                        if (m == Mat.Powder) { Fuse(x, y, z, .2, "порох"); continue; }
                        World.Set(x, y, z, Mat.Air);
                        if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
                        Burning.Remove(WaterPlanner.Pack(x, y, z));
                        if (++knocked % 2 == 0 && f.Voxels.Count < 4000) f.Voxels.Add(((sbyte)Math.Clamp(x - ix, -120, 120), (sbyte)Math.Clamp(y - iy, -120, 120), (sbyte)Math.Clamp(z - iz, -120, 120), m));
                    }
        }
        // its voxels lie where it came down, lowest first, in a heap of what they were, broken
        f.Voxels.Sort((a, b) => a.dy.CompareTo(b.dy));
        _settling = true;
        int lit = 0;
        foreach (var (dx, dy, dz, m) in f.Voxels)
        {
            byte b = Shattered(m);
            if (b == 0) continue;
            int x = Math.Clamp(ix + (int)(dx * .8f), 2, World.SX - 3), z = Math.Clamp(iz + (int)(dz * .8f), 2, World.SZ - 3);
            int from = Math.Clamp(iy + dy, 2, World.SY - 3);
            // out of whatever it came down into, up to the first air above
            for (int up = 0; up < 40 && from < World.SY - 3 && Stops(x, from, z) && World.Get(x, from, z) != Mat.Water; up++) from++;
            var (lx, ly, lz) = Landing(x, from, z, b == Mat.Lumber);
            if (ly < 2 || ly >= World.SY - 2) continue;
            byte at = World.Get(lx, ly, lz);
            if (at != Mat.Air && at != Mat.Water && !Mat.Passable[at]) continue;
            World.Set(lx, ly, lz, b);
            if (Orders.At(lx, ly, lz) != 0) Orders.Clear(lx, ly, lz);
            if (f.Burning > 0 && lit < Math.Min(4, f.Burning) && Mat.Burns(b) && Ignite(lx, ly, lz)) lit++;
        }
        _settling = false;
        Landed?.Invoke(f.X, f.Y, f.Z, f.Voxels.Count, speed);
    }

    /// <summary>One thrown comes down: onto whatever is under it, hurt as hard as it landed, and those it lands on too.</summary>
    void Alight(Flier f, float speed)
    {
        var u = f.Unit;
        u.Airborne = null;
        int cell = StandBelow(u.X, u.Y + 2, u.Z, u.T.Move.Height);
        if (cell >= 0) { u.Cell = cell; var (fx, fy, fz) = Nav.Feet(cell); u.X = Math.Clamp(u.X, fx - 3.5f, fx + 3.5f); u.Y = fy; u.Z = Math.Clamp(u.Z, fz - 3.5f, fz + 3.5f); }
        u.Path = null; u.NextSearch = 0; u.State = u.Dead ? UnitState.Dead : UnitState.Idle;
        if (u.Dead || f.Gentle) return;
        float hard = Math.Max(0, speed - 5.5f * VoxelWorld.PerMetre) * .5f;
        if (hard <= 0) return;
        foreach (var o in Units)
        {
            if (o == u || o.Dead || o.Inside || o.Airborne != null) continue;
            if (MathF.Abs(o.X - u.X) < u.T.BodyR + o.T.BodyR + 1 && MathF.Abs(o.Z - u.Z) < u.T.BodyR + o.T.BodyR + 1 && MathF.Abs(o.Y - u.Y) < 10)
                Hurt(o, null, hard * .5f * Math.Min(2, u.T.BodyH / 15), f.Cause);
        }
        Hurt(u, null, Kinds.IsEngine(u.T) ? hard * 3 : hard, f.Cause.Length > 0 ? f.Cause : "падение");
    }

    /// <summary>A cell to stand in under a spot (voxels), or as near under it as there is one; failing that, anywhere near.</summary>
    int StandBelow(float x, float y, float z, int h)
    {
        int ci = Math.Clamp((int)x >> NavGrid.Shift, 0, Nav.NX - 1), ck = Math.Clamp((int)z >> NavGrid.Shift, 0, Nav.NZ - 1);
        int cy = Math.Clamp(((int)y >> NavGrid.Shift) + 1, 1, Nav.NY - 1);
        for (int r = 0; r < 5; r++)
            for (int dk = -r; dk <= r; dk++)
                for (int di = -r; di <= r; di++)
                {
                    if (Math.Max(Math.Abs(di), Math.Abs(dk)) != r) continue;
                    int i = ci + di, k = ck + dk;
                    if (i < 0 || k < 0 || i >= Nav.NX || k >= Nav.NZ) continue;
                    for (int yy = cy; yy >= 1; yy--)
                        if (Nav.Stand(i, yy, k, h)) return Nav.Id(i, yy, k);
                }
        return NearestStand((int)x, (int)z, h);
    }
}
