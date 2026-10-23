using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// How a beast of four legs goes, worked out each frame rather than played: its gait by its pace — a walk, the legs one
/// after another round; a trot, by diagonal pairs; a gallop, the hind legs driving and the forelegs reaching; a hare
/// bounding on its hind legs together — each foot set down where the stride puts it and standing there while the body
/// passes over it (so nothing slides), lifted and swung on to the next spot, and set down on the ground that is there:
/// the leg bent to it at the knee (a hind leg at the hock, the other way) by the lengths of its two bones, the body
/// lowered to the foot that steps down furthest and tilted with the slope. The head bobs with the stride, goes down to
/// the grass and up again while it grazes, comes up when something is near, and lunges when it bites.
/// </summary>
public partial class UnitView
{
    /// <summary>
    /// A way of going: how much of each stride a foot stands on the ground; where in the stride each leg (front right,
    /// front left, hind right, hind left) sets down; how long a step is to the leg, how high a hoof comes up; how much the
    /// body rises and falls and rocks.
    /// </summary>
    readonly record struct Going(float Duty, float FR, float FL, float HR, float HL, float Reach, float Lift, float Rise, float Rock)
    {
        public float At(int leg) => leg switch { 0 => FR, 1 => FL, 2 => HR, _ => HL };
    }

    const int Walk = 0, Trot = 1, Gallop = 2, Bound = 3;
    static readonly Going[] Goings =
    {
        // the legs round one after another, the hind leg leading its side: left hind, left fore, right hind, right fore
        new(.64f, .75f, .25f, .5f, 0, .72f, .13f, .025f, 0),
        new(.46f, 0, .5f, .5f, 0, .95f, .22f, .05f, 0),
        new(.32f, .62f, .52f, .1f, 0, 1.35f, .3f, .09f, .1f),
        new(.26f, .5f, .55f, .02f, 0, 1.7f, .45f, .16f, .16f),
    };

    // the pose of a beast this frame, for the bones: each leg's turn at the shoulder or the hip and at the knee; the
    // body's tilt and roll; the head's turn and nod
    readonly float[] _legA = new float[4], _kneeA = new float[4];
    float _bodyPitch, _bodyRoll, _neckYaw, _neckPitch;

    static readonly Part[] LegParts = { Part.Main, Part.Off, Part.LegR, Part.LegL };
    static readonly Bone[] Uppers = { Bone.UpperR, Bone.UpperL, Bone.ThighR, Bone.ThighL }, Lowers = { Bone.LowerR, Bone.LowerL, Bone.ShinR, Bone.ShinL };

    /// <summary>Where a foot is in its stride, ahead of under its hip (+) or behind, and how high it is lifted.</summary>
    static (float z, float y) Step(in Going g, int leg, float phase, float reach, float lift)
    {
        float p = phase + g.At(leg);
        p -= MathF.Floor(p);
        if (p < g.Duty) return (reach * (.5f - p / g.Duty), 0);
        float w = (p - g.Duty) / (1 - g.Duty);
        return (reach * (Smooth(w) - .5f), lift * MathF.Sin(MathF.PI * w));
    }

    /// <summary>
    /// The two turns (about the figure's own x, as the legs swing) that put the sole of a leg of two bones on a spot from
    /// its hip: the upper bone's from straight down, and the knee's. A man's knee and a foreleg's bend forward (the foot
    /// going back as it bends), a hind leg's hock the other way.
    /// </summary>
    static (float upper, float knee) Reach(float hipY, float hipZ, float footY, float footZ, float a, float b, bool forward)
    {
        float dy = footY - hipY, dz = footZ - hipZ;
        float d = Math.Clamp(MathF.Sqrt(dy * dy + dz * dz), MathF.Abs(a - b) + .02f, a + b - .02f);
        float line = MathF.Atan2(-dz, -dy);
        float at = MathF.Acos(Math.Clamp((a * a + d * d - b * b) / (2 * a * d), -1, 1));
        float bend = MathF.PI - MathF.Acos(Math.Clamp((a * a + b * b - d * d) / (2 * a * b), -1, 1));
        return forward ? (line - at, bend) : (line + at, -bend);
    }

    /// <summary>The height of the ground under a point of the field, near the height given (voxels); that height if nothing is there.</summary>
    float GroundUnder(float x, float z, float near)
    {
        var w = _m.World;
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z), top = (int)MathF.Floor(near) + 4;
        for (int y = top; y >= top - 10 && y >= 0; y--)
            if (NavGrid.Blocks(w.Get(ix, y, iz))) return y + 1;
        return near;
    }

    /// <summary>
    /// A beast's legs, body and head this frame (into the fields the bones are posed from): its gait by its pace, each
    /// foot on the ground under it, the head grazing or looking about. What the body rises by is returned.
    /// </summary>
    float Strides(Unit u, Figure f, Look look)
    {
        float t = (float)_t, seed = u.Id * .618f;
        float leg = (f.LegUpper[0] + f.LegLower[0] + f.LegUpper[2] + f.LegLower[2]) / 2;
        float a = look.Move, v = look.Speed;
        bool hare = u.T == Kinds.Hare;
        // the gait the pace wants: a walk, a trot, a gallop (a hare hops and bounds), a little held either way so a pace
        // on the edge does not change it every step
        int want = hare ? (v < 1.6f * leg ? Walk : Bound)
            : v < (look.Gait == Walk ? 2.6f : 2.2f) * leg ? Walk : v < (look.Gait == Gallop ? 4.4f : 5) * leg ? Trot : Gallop;
        if (a < .05f) want = Walk;
        if (want != look.Gait) { look.GaitWas = look.Gait; look.Gait = want; look.GaitMix = 0; }
        look.GaitMix = Math.Min(1, look.GaitMix + _dt / .35f);
        var g = Goings[look.Gait];
        var was = Goings[look.GaitWas];
        float reach = g.Reach * leg, lift = g.Lift * leg;
        // a stride: as far as the body goes while a foot stands, and as long again as the foot is in the air
        look.GaitPhase += look.Dist / MathF.Max(1, reach / g.Duty);
        float phase = look.GaitPhase, mix = Smooth(look.GaitMix), going = Smooth(Math.Clamp(a * 3, 0, 1));

        // the feet: where the stride puts each, on the ground under it
        float face = look.Face, sinF = MathF.Sin(face), cosF = MathF.Cos(face);
        Span<float> fy = stackalloc float[4], fz = stackalloc float[4];
        float front = 0, hind = 0;
        for (int l = 0; l < 4; l++)
        {
            var j = f.Joint[(int)LegParts[l]];
            var (z1, y1) = Step(g, l, phase, reach, lift);
            if (mix < 1)
            {
                var (z0, y0) = Step(was, l, phase, was.Reach * leg, was.Lift * leg);
                z1 = Mathf.Lerp(z0, z1, mix); y1 = Mathf.Lerp(y0, y1, mix);
            }
            float dz = z1 * going, lifted = y1 * going;
            float lx = j.X, lz = j.Z + dz;
            float ground = u.Dead || u.Airborne != null ? u.Y : GroundUnder(u.X + lx * sinF + lz * cosF, u.Z - lx * cosF + lz * sinF, u.Y);
            float under = Math.Clamp(ground - u.Y, -leg * .6f, leg * .6f);
            fy[l] = under + lifted; fz[l] = lz;
            if (l < 2) front += under / 2; else hind += under / 2;
        }

        // the body: down to the foot that steps furthest down, tilted to the slope under it, rising and falling with the
        // stride, rocking at a gallop
        float span = MathF.Max(1, f.Joint[(int)Part.Main].Z - f.Joint[(int)Part.LegR].Z);
        _bodyPitch = -MathF.Atan2(front - hind, span) * .8f + (g.Rock * mix + was.Rock * (1 - mix)) * MathF.Sin(MathF.Tau * phase) * going;
        _bodyRoll = 0;
        float rise = (g.Rise * mix + was.Rise * (1 - mix)) * leg * going * (look.Gait >= Gallop ? MathF.Sin(MathF.Tau * phase) : MathF.Abs(MathF.Sin(MathF.Tau * phase)) - .5f);
        float bob = rise - .03f * leg + MathF.Min(front, hind);
        var centre = (f.Joint[(int)Part.Main] + f.Joint[(int)Part.LegR]) / 2;
        var tilt = new Basis(Vector3.Right, _bodyPitch);
        for (int l = 0; l < 4; l++)
        {
            int p = (int)LegParts[l];
            var hip = centre + tilt * (f.Joint[p] - centre) + new Vector3(0, bob, 0);
            float a1 = f.LegUpper[l], b1 = f.LegLower[l];
            // a foot out of reach below brings the body down to it
            float dy = fy[l] - hip.Y, dz = fz[l] - hip.Z, d = MathF.Sqrt(dy * dy + dz * dz);
            if (d > a1 + b1 - .05f && dy < 0) { float down = d - (a1 + b1 - .05f); bob -= down; hip.Y -= down; }
        }
        for (int l = 0; l < 4; l++)
        {
            int p = (int)LegParts[l];
            var hip = centre + tilt * (f.Joint[p] - centre) + new Vector3(0, bob, 0);
            // the legs a little bent under the weight, the hind legs the more
            var (up, knee) = Reach(hip.Y, hip.Z, fy[l], fz[l], f.LegUpper[l], f.LegLower[l], l < 2);
            // the turn is taken against the body, which is itself tilted
            _legA[l] = up - _bodyPitch;
            _kneeA[l] = knee;
        }

        // the head: down to the grass and up again while it stands grazing, up when something is near or it runs, a nod
        // with the stride, a lunge when it bites
        bool alert = u.State == UnitState.Flee || u.State == UnitState.Fight || look.Run > .3f;
        float grazing = !alert && a < .05f && u.T.Team == Team.Wild ? (MathF.Sin(t * .32f + seed * 3) > -.2f ? 1 : 0) : 0;
        look.Graze += (grazing - look.Graze) * Math.Min(1, _dt * (grazing > look.Graze ? .9f : 1.6f));
        float nod = .07f * MathF.Sin(MathF.Tau * 2 * phase) * going;
        float pitch = Mathf.Lerp(alert ? -.18f : .05f, u.T == Kinds.Boar ? .55f : 1.15f, Smooth(look.Graze)) + nod;
        if (u.State == UnitState.Fight) pitch = .25f + u.Swing * .45f;
        float yaw = 0;
        if (a < .05f && !alert && look.Graze < .5f)
            yaw = (float)(Hash01(u.Id, (int)(t / 2.6f)) - .5f) * 1.1f;
        look.HeadPitch += (pitch - look.HeadPitch) * Math.Min(1, _dt * 4);
        look.HeadYaw += (yaw - look.HeadYaw) * Math.Min(1, _dt * 2.5f);
        _neckPitch = look.HeadPitch; _neckYaw = look.HeadYaw;
        return bob;
    }

    /// <summary>
    /// A man's legs this frame, walking or standing on ground that is not level: each foot set down where the stride
    /// puts it and standing there while the body passes over it, on the ground that is under it, lifted and swung on to
    /// the next spot; the leg bent to it at the knee by the lengths of thigh and shin; the hips brought down to the foot
    /// that steps furthest down. Standing on level ground the pose it has is left as it is. What the hips rise by is
    /// returned.
    /// </summary>
    float Footing(Unit u, Figure f, Look look, float bob, ref float legR, ref float legL, ref float kneeR, ref float kneeL)
    {
        float a = look.Move, run = look.Run, leg = f.Thigh + f.Shin;
        float duty = Mathf.Lerp(.6f, .36f, run), reach = f.Stride * (1 + .35f * run) * duty, lift = leg * (.1f + .14f * run);
        float going = Smooth(Math.Clamp(a * 3, 0, 1));
        // (the right foot comes down in front where the old swing had the right leg furthest forward)
        float phase = look.Phase / MathF.Tau - .25f;
        float sinF = MathF.Sin(look.Face), cosF = MathF.Cos(look.Face);
        Span<float> fy = stackalloc float[2], fz = stackalloc float[2];
        for (int l = 0; l < 2; l++)
        {
            var j = f.Joint[(int)(l == 0 ? Part.LegR : Part.LegL)];
            float p = phase + l * .5f;
            p -= MathF.Floor(p);
            float z, y;
            if (p < duty) { z = reach * (.5f - p / duty); y = 0; }
            else { float w = (p - duty) / (1 - duty); z = reach * (Smooth(w) - .5f); y = lift * MathF.Sin(MathF.PI * w); }
            float lx = j.X, lz = j.Z + z * going;
            float ground = GroundUnder(u.X + lx * sinF + lz * cosF, u.Z - lx * cosF + lz * sinF, u.Y);
            fy[l] = Math.Clamp(ground - u.Y, -leg * .5f, leg * .5f) + y * going;
            fz[l] = lz;
        }
        if (a < .05f && MathF.Abs(fy[0] - fy[1]) < .5f && MathF.Abs(fy[0]) < .5f) return bob;
        // the hips down to a foot out of reach below them, and a little bent at the knees under the weight
        float hip = f.Joint[(int)Part.LegR].Y, hipZ = f.Joint[(int)Part.LegR].Z;
        for (int l = 0; l < 2; l++)
        {
            float dy = fy[l] - (hip + bob), dz = fz[l] - hipZ, d = MathF.Sqrt(dy * dy + dz * dz);
            if (d > leg - .04f && dy < 0) bob -= d - (leg - .04f);
        }
        bob -= .025f * leg * going;
        var (r, kr) = Reach(hip + bob, hipZ, fy[0], fz[0], f.Thigh, f.Shin, true);
        var (lf, kl) = Reach(hip + bob, f.Joint[(int)Part.LegL].Z, fy[1], fz[1], f.Thigh, f.Shin, true);
        legR = r; kneeR = kr; legL = lf; kneeL = kl;
        return bob;
    }

    /// <summary>A beast down or in the air: its legs stiff out, or flung about, and its head as it falls.</summary>
    void Stiff(Unit u, Figure f, float flail)
    {
        for (int l = 0; l < 4; l++)
        {
            _legA[l] = flail != 0 ? (l % 2 == 0 ? 1 : -1) * .6f * flail : l < 2 ? .5f : -.4f;
            _kneeA[l] = l < 2 ? .1f : -.1f;
        }
        _bodyPitch = _bodyRoll = 0;
        _neckPitch = u.Dead ? .3f : -.2f; _neckYaw = 0;
    }

    /// <summary>The bones of a beast posed from the hips: the trunk with its tilt, the neck and head turned at the neck's root, each leg from its shoulder or hip and again at its knee.</summary>
    void PoseLegs(Figure f, Transform3D hips, float inhale)
    {
        var centre = (f.Joint[(int)Part.Main] + f.Joint[(int)Part.LegR]) / 2;
        var body = hips * RotAt(centre, Vector3.Right, _bodyPitch) * RotAt(centre, Vector3.Back, _bodyRoll);
        _xf[(int)Bone.Torso] = body * new Transform3D(Basis.Identity, centre) * new Transform3D(Basis.FromScale(new Vector3(1 + .02f * inhale, 1 + .012f * inhale, 1)), Vector3.Zero)
            * new Transform3D(Basis.Identity, -centre);
        var neck = f.Pivot[(int)Bone.Head];
        var turn = new Basis(Vector3.Up, _neckYaw) * new Basis(Vector3.Right, _neckPitch);
        _xf[(int)Bone.Head] = body * new Transform3D(turn, neck - turn * neck);
        for (int l = 0; l < 4; l++)
        {
            var upper = body * Swing(f, LegParts[l], _legA[l]);
            _xf[(int)Uppers[l]] = upper;
            _xf[(int)Lowers[l]] = upper * RotAt(f.Pivot[(int)Lowers[l]], Vector3.Right, _kneeA[l]);
        }
    }
}
