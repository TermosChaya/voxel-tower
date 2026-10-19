using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The ways a blow comes: down from over the head; down across the body from the high right or from the high left; flat
/// across from the right; the point thrust home; up from low on the right; the shield's face driven in; a polearm's head
/// brought down from high; a club or a maul smashed down; a beast's jaws.
/// </summary>
public enum StrokeKind : byte { Overhead, DiagR, DiagL, Sweep, Thrust, Rising, Bash, PoleChop, Smash, Bite }

/// <summary>A point or a direction, in voxels: in a figure's own frame (+x its left, +y up, +z the way it faces) or the world's.</summary>
public struct Vec
{
    public float X, Y, Z;
    public Vec(float x, float y, float z) { X = x; Y = y; Z = z; }
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec operator -(Vec a) => new(-a.X, -a.Y, -a.Z);
    public static Vec operator *(Vec a, float k) => new(a.X * k, a.Y * k, a.Z * k);
    public float Dot(Vec b) => X * b.X + Y * b.Y + Z * b.Z;
    public Vec Cross(Vec b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);
    public Vec Unit { get { float l = Length; return l < 1e-6f ? new Vec(0, 0, 1) : this * (1 / l); } }
    public static readonly Vec Up = new(0, 1, 0), Forward = new(0, 0, 1), Left = new(1, 0, 0);
}

/// <summary>A turn of a part about its joint, as a unit quaternion (the same as the view's).</summary>
public struct Turn
{
    public float X, Y, Z, W;
    public Turn(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
    public static readonly Turn None = new(0, 0, 0, 1);

    public static Turn About(Vec axis, float angle)
    {
        var a = axis.Unit;
        float s = MathF.Sin(angle / 2);
        return new Turn(a.X * s, a.Y * s, a.Z * s, MathF.Cos(angle / 2));
    }

    public static Turn operator *(Turn a, Turn b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    public Vec Apply(Vec v)
    {
        // v + 2w(q×v) + 2q×(q×v)
        var q = new Vec(X, Y, Z);
        var t = q.Cross(v) * 2;
        return v + t * W + q.Cross(t);
    }

    /// <summary>The turn whose columns are these three axes (each a unit, square to the others).</summary>
    public static Turn Of(Vec x, Vec y, Vec z)
    {
        float tr = x.X + y.Y + z.Z;
        Turn q;
        if (tr > 0)
        {
            float s = MathF.Sqrt(tr + 1) * 2;
            q = new Turn((y.Z - z.Y) / s, (z.X - x.Z) / s, (x.Y - y.X) / s, s / 4);
        }
        else if (x.X > y.Y && x.X > z.Z)
        {
            float s = MathF.Sqrt(1 + x.X - y.Y - z.Z) * 2;
            q = new Turn(s / 4, (y.X + x.Y) / s, (z.X + x.Z) / s, (y.Z - z.Y) / s);
        }
        else if (y.Y > z.Z)
        {
            float s = MathF.Sqrt(1 + y.Y - x.X - z.Z) * 2;
            q = new Turn((y.X + x.Y) / s, s / 4, (z.Y + y.Z) / s, (z.X - x.Z) / s);
        }
        else
        {
            float s = MathF.Sqrt(1 + z.Z - x.X - y.Y) * 2;
            q = new Turn((z.X + x.Z) / s, (z.Y + y.Z) / s, s / 4, (x.Y - y.X) / s);
        }
        return q.Norm();
    }

    /// <summary>
    /// An arm turned so that it runs from the shoulder along a, and what it holds (carried forward from the fist, square
    /// to the arm) points along b: the arm hanging with its weapon held out before it, turned.
    /// </summary>
    public static Turn Arm(Vec a, Vec b)
    {
        a = a.Unit;
        b = (b - a * b.Dot(a)).Unit;
        return Of(b.Cross(a), -a, b);
    }

    public Turn Norm()
    {
        float l = MathF.Sqrt(X * X + Y * Y + Z * Z + W * W);
        return l < 1e-6f ? None : new Turn(X / l, Y / l, Z / l, W / l);
    }

    public static Turn Slerp(Turn a, Turn b, float t)
    {
        float d = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        if (d < 0) { b = new Turn(-b.X, -b.Y, -b.Z, -b.W); d = -d; }
        if (d > .9995f) return new Turn(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.W + (b.W - a.W) * t).Norm();
        float th = MathF.Acos(d), s = MathF.Sin(th), ka = MathF.Sin((1 - t) * th) / s, kb = MathF.Sin(t * th) / s;
        return new Turn(a.X * ka + b.X * kb, a.Y * ka + b.Y * kb, a.Z * ka + b.Z * kb, a.W * ka + b.W * kb);
    }
}

/// <summary>How a figure stands at one moment of a stroke: its arms turned at the shoulders, its trunk twisted and leant at the hips, the step it has taken.</summary>
public struct StrokePose
{
    public Turn Main, Off;
    /// <summary>
    /// The trunk turned at the hips (radians; more brings the right shoulder forward) and leant (more is forward); the
    /// step forward (voxels), and how far the hips sink into it on legs that do not bend.
    /// </summary>
    public float Twist, Lean, Lunge, Drop;
}

/// <summary>
/// Where a figure holds what it strikes with, read once off its body: the joints, the fist at rest, how far before it
/// the blade (the head, the point) reaches and at what height; the middle of its shield; the front of a beast's head.
/// </summary>
public sealed class Grip
{
    public Vec Hip, Shoulder, OffShoulder, Fist, OffFist;
    /// <summary>How high the legs are hung at the hips: as long as they are.</summary>
    public float Leg;
    /// <summary>The striking part, at rest: from where to where along +z it reaches, at what height and to the side.</summary>
    public float Blade0, Blade1, BladeY0, BladeY1, BladeX;
    public Vec Boss, Snout;
    public bool Shield, Beast, Mounted, TwoHands;
    /// <summary>The figure's height, for scaling the step.</summary>
    public float Height;
}

/// <summary>A blow as it landed, for the eye and the ear: nothing in the simulation hangs on it.</summary>
public readonly struct Smiting
{
    public readonly Unit By, On;
    /// <summary>The spot struck (world voxels), which way the blade went through it, and along what its edge lay.</summary>
    public readonly float X, Y, Z, DX, DY, DZ;
    /// <summary>What it struck first: steel, mail, a shield's boards, skin; and how much flesh it cut (half-voxels).</summary>
    public readonly byte Mat;
    public readonly int Flesh;
    public readonly StrokeKind Stroke;
    /// <summary>Turned by armour or a shield, or caught on bone: the blade did not go through.</summary>
    public readonly bool Glanced;
    public Smiting(Unit by, Unit on, Vec at, Vec dir, byte mat, int flesh, StrokeKind stroke, bool glanced)
    {
        By = by; On = on; X = at.X; Y = at.Y; Z = at.Z; DX = dir.X; DY = dir.Y; DZ = dir.Z; Mat = mat; Flesh = flesh; Stroke = stroke; Glanced = glanced;
    }
}

/// <summary>
/// The strokes' shapes, shared by the simulation, which finds where the blade meets the body, and the view, which draws
/// the blow along the same sweep. A stroke is the arm and what it holds turned through a plane about the shoulder — from
/// drawn back, through the moment it meets the body, to spent — with the trunk twisting and leaning into it and a step
/// taken; a thrust and a shield's bash drive straight in; a beast's jaws go with its head.
/// </summary>
public static class Strokes
{
    public const int Count = 10;

    enum Way : byte { Arc, Line, Shield, Jaws }

    sealed class Shape
    {
        public Way Way;
        /// <summary>
        /// The arc's plane: the blade along U at angle 0 and along V a quarter turn on, and the arm a quarter turn ahead of
        /// the blade, the way it goes; the angles drawn back, met, spent; and where the arm comes into the arc from its
        /// guard, to run back along it to drawn back.
        /// </summary>
        public Vec U = Vec.Up, V = Vec.Forward;
        public float A0, Ac, A1, Entry;
        /// <summary>How far a shield is swung out to the left, out of the blade's way, as the blow meets (radians).</summary>
        public float Open;
        /// <summary>The trunk's twist, its lean and the share of the step at the three keys.</summary>
        public float T0, Tc, T1, L0, Lc, L1;
        /// <summary>How far a man stands back of where the stroke meets the body when it is drawn back (a thrust, a bash, a bite: back of the step it lunges in with), and on past it when it is spent; voxels.</summary>
        public float Back = .4f, Over = .5f;
        /// <summary>Aimed at the middle of the one struck (a stroke in the plane straight ahead from a shoulder off to the side).</summary>
        public bool Centre;
        /// <summary>The height on the one struck it is aimed at (a share of it), or below zero to come down on whatever is highest.</summary>
        public float Height = -1;
        /// <summary>The step a figure of a man's size takes into it, in voxels.</summary>
        public float Step = 2.5f;
        /// <summary>The shield arm's swing at the three keys (a bash).</summary>
        public float O0, Oc, O1;
    }

    static float D(float deg) => deg * MathF.PI / 180;
    static Vec V(float x, float y, float z) => new Vec(x, y, z).Unit;

    static readonly Shape[] Shapes =
    {
        // over the head: the blade cocked down the back, the fist over the head, brought down the middle and on till the
        // arm hangs and the blade points on before it
        new() { U = Vec.Up, V = Vec.Forward, A0 = D(-125), Ac = D(35), A1 = D(100), Entry = D(40), T0 = -.3f, Tc = .25f, T1 = .4f, L0 = -.12f, Lc = .12f, L1 = .28f, Centre = true, Step = 3, Open = .5f },
        // the forehand from the high right down to the low left, through the shoulder and the neck: cocked over the right
        // shoulder, spent with the fist by the left hip
        new() { U = V(-.7f, .72f, 0), V = Vec.Forward, A0 = D(-120), Ac = D(45), A1 = D(110), Entry = D(10), T0 = -.5f, Tc = .2f, T1 = .55f, L0 = -.05f, Lc = .1f, L1 = .2f, Height = .78f, Step = 3, Open = .7f },
        // the backhand from the high left down to the low right: cocked over the left shoulder, spent by the right hip
        new() { U = V(.7f, .72f, 0), V = Vec.Forward, A0 = D(-115), Ac = D(45), A1 = D(105), Entry = D(10), T0 = .45f, Tc = -.1f, T1 = -.45f, L0 = -.05f, Lc = .1f, L1 = .15f, Height = .74f, Step = 3, Open = -.25f },
        // flat across from the right, at the neck or the chest: the fist out to the right and the blade back, round
        // through the front to the left
        new() { U = new Vec(-1, 0, 0), V = Vec.Forward, A0 = D(-95), Ac = D(70), A1 = D(115), Entry = D(0), T0 = -.55f, Tc = .25f, T1 = .6f, L0 = 0, Lc = .05f, L1 = .08f, Height = .66f, Back = 0, Over = .4f, Step = 2.5f, Open = .8f },
        // the point driven home from the hip, the weight going in behind it
        new() { Way = Way.Line, T0 = -.35f, Tc = .3f, T1 = .35f, L0 = -.12f, Lc = .18f, L1 = .2f, Back = 3.5f, Over = .4f, Centre = true, Height = .55f, Step = 4 },
        // up from low on the right to high on the left, under a guard: the fist low by the right thigh and the blade back,
        // spent with the fist over the left shoulder
        new() { U = V(-.7f, -.72f, 0), V = Vec.Forward, A0 = D(-95), Ac = D(65), A1 = D(120), Entry = D(30), T0 = -.4f, Tc = .15f, T1 = .45f, L0 = .05f, Lc = .05f, L1 = -.1f, Back = .3f, Over = .3f, Height = .45f, Step = 3, Open = .4f },
        // the shield's face driven in, the left shoulder behind it
        new() { Way = Way.Shield, T0 = .35f, Tc = -.4f, T1 = -.45f, L0 = -.05f, Lc = .15f, L1 = .15f, Back = 2.5f, Over = 0, O0 = .25f, Oc = -.45f, O1 = -.35f, Height = .55f, Step = 4 },
        // a polearm's head brought down from high over the head, the whole body in it
        new() { U = Vec.Up, V = Vec.Forward, A0 = D(-115), Ac = D(40), A1 = D(95), Entry = D(40), T0 = -.35f, Tc = .2f, T1 = .3f, L0 = -.15f, Lc = .2f, L1 = .35f, Back = .8f, Over = .6f, Centre = true, Step = 3 },
        // a club or a maul swung up over the right shoulder and smashed down, a little from the right
        new() { U = V(-.3f, .95f, 0), V = Vec.Forward, A0 = D(-125), Ac = D(40), A1 = D(100), Entry = D(40), T0 = -.4f, Tc = .25f, T1 = .45f, L0 = -.15f, Lc = .2f, L1 = .38f, Back = .6f, Centre = true, Step = 3, Open = .5f },
        // the jaws: the head drawn back, then the lunge and the bite low on the one bitten
        new() { Way = Way.Jaws, L0 = -.15f, Lc = .25f, L1 = .3f, Back = 2.5f, Over = -.4f, Height = .35f, Step = 3 },
    };

    /// <summary>Whether a stroke is turned in to the middle of the one struck, the height it is aimed at (a share of it, or below zero for none), the step it takes for a man.</summary>
    public static bool Centred(StrokeKind k) => Shapes[(int)k].Centre;
    public static float Aimed(StrokeKind k) => Shapes[(int)k].Height;
    public static float StepOf(StrokeKind k) => Shapes[(int)k].Step;
    /// <summary>A stroke driven straight in — a thrust, a bash, a bite — whose step is the length that brings it to the body.</summary>
    public static bool Straight(StrokeKind k) => Shapes[(int)k].Way != Way.Arc;

    /// <summary>Where along the stroke (0 drawn back … 1 spent) the blade comes to the body, as the stroke is drawn.</summary>
    public static float Contact(StrokeKind k)
    {
        var sh = Shapes[(int)k];
        return sh.Way == Way.Arc ? (sh.Ac - sh.A0) / (sh.A1 - sh.A0) : .7f;
    }

    /// <summary>How hard a stroke comes: a thrust and a bite go in deep and narrow, a bash and a smash crush; the rest cut as the weapon cuts.</summary>
    public static (Sim.Cut cut, float depth, float reach) CutOf(StrokeKind k, Sim.Cut cut, float depth, float reach) => k switch
    {
        StrokeKind.Thrust => cut == Sim.Cut.Crush ? (Sim.Cut.Crush, depth * .6f, 1.2f) : (Sim.Cut.Stab, depth * 1.1f, 1),
        StrokeKind.Bash => (Sim.Cut.Crush, 1.5f, 1.5f),
        StrokeKind.Smash => cut == Sim.Cut.Crush ? (cut, depth, reach) : (Sim.Cut.Crush, depth * .8f, 2),
        // a knife slashes when it is not stabbing
        StrokeKind.DiagR or StrokeKind.DiagL or StrokeKind.Rising or StrokeKind.Sweep when cut == Sim.Cut.Stab && reach <= 1 && depth <= 3 => (Sim.Cut.Slash, depth * .8f, 1.5f),
        _ => (cut, depth, reach),
    };

    // ------------------------------------------------------------ what a figure holds, read off its body

    static readonly Dictionary<BodyPlan, Grip> Grips = new();

    /// <summary>How a figure holds what it strikes with (the fall-back reach for a body that holds nothing it can be read from).</summary>
    public static Grip GripOf(BodyPlan plan, UnitType t)
    {
        lock (Grips)
        {
            if (Grips.TryGetValue(plan, out var g)) return g;
            Grips[plan] = g = Read(plan, t);
            return g;
        }
    }

    static Grip Read(BodyPlan plan, UnitType t)
    {
        const float S = BodyPlan.Sub;
        Vec J(BodyPart p) => new(plan.JX[(int)p], plan.JY[(int)p], plan.JZ[(int)p]);
        var g = new Grip
        {
            Hip = J(BodyPart.Body), Shoulder = J(BodyPart.Main), OffShoulder = J(BodyPart.Off), Shield = plan.Shield, Beast = plan.Beast, Mounted = plan.Mounted,
            Height = t.BodyH, TwoHands = !plan.Shield && t.Tool is "halberd" or "spear" or "maul" or "pick" or "club" && !plan.Mounted,
            Leg = J(BodyPart.LegR).Y,
        };
        if (t.Tool == "club" && t.BodyH < 20) g.TwoHands = false;
        // the arm's lowest skin: the fist
        float Low(PartGrid pg, out float x)
        {
            x = 0;
            if (pg == null) return 0;
            int lo = int.MaxValue; float sx = 0; int n = 0;
            for (int i = 0; i < pg.M.Length; i++)
            {
                if (pg.M[i] == 0 || pg.T[i] == (byte)Tissue.Gear) continue;
                int y = i / (pg.SX * pg.SZ) + pg.Y0;
                if (y < lo) { lo = y; sx = 0; n = 0; }
                if (y == lo) { sx += i % pg.SX + pg.X0 + .5f; n++; }
            }
            x = n > 0 ? sx / n / S : 0;
            return lo == int.MaxValue ? 0 : (lo + 1.5f) / S;
        }
        float fy = Low(plan.Grid[(int)BodyPart.Main], out float fx);
        g.Fist = new Vec(fx, fy, g.Shoulder.Z);
        float oy = Low(plan.Grid[(int)BodyPart.Off], out float ox);
        g.OffFist = new Vec(ox, oy, g.OffShoulder.Z);
        // what the fist holds, forward of it: where it begins and ends, and the height of its far third (a blade, an axe's
        // head hanging below the haft, a mace's head)
        var m = plan.Grid[(int)BodyPart.Main];
        float z0 = float.MaxValue, z1 = float.MinValue;
        if (m != null && !plan.Beast)
            for (int i = 0; i < m.M.Length; i++)
            {
                if (m.M[i] == 0 || m.T[i] != (byte)Tissue.Gear) continue;
                float z = (i / m.SX % m.SZ + m.Z0 + .5f) / S;
                if (z <= g.Shoulder.Z + .6f) continue;
                z0 = Math.Min(z0, z); z1 = Math.Max(z1, z);
            }
        if (z1 > z0)
        {
            float far = z1 - (z1 - z0) / 3, ys = 0, xs = 0, yn = 0; int n = 0, nn = 0;
            for (int i = 0; i < m.M.Length; i++)
            {
                if (m.M[i] == 0 || m.T[i] != (byte)Tissue.Gear) continue;
                float z = (i / m.SX % m.SZ + m.Z0 + .5f) / S, y = (i / (m.SX * m.SZ) + m.Y0 + .5f) / S, x = (i % m.SX + m.X0 + .5f) / S;
                if (z >= far) { ys += y; n++; }
                if (z <= z0 + 1) { yn += y; xs += x; nn++; }
            }
            g.Blade0 = Math.Max(z0, g.Fist.Z + .5f); g.Blade1 = z1 + .25f;
            g.BladeY1 = n > 0 ? ys / n : g.Fist.Y;
            g.BladeY0 = nn > 0 ? yn / nn : g.Fist.Y;
            g.BladeX = nn > 0 ? xs / nn : g.Fist.X;
        }
        else
        {
            // a fist with nothing to read in it: reach as far as the kind reaches, less the arm
            g.Blade0 = g.Fist.Z + .5f; g.Blade1 = Math.Max(1.5f, t.Reach * .45f);
            g.BladeY0 = g.BladeY1 = g.Fist.Y; g.BladeX = g.Fist.X;
        }
        // the shield's middle: the armour on the off arm
        var o = plan.Grid[(int)BodyPart.Off];
        if (o != null && plan.Shield)
        {
            float sx = 0, sy = 0, sz = 0; int n = 0;
            for (int i = 0; i < o.M.Length; i++)
            {
                if (o.M[i] == 0 || o.T[i] != (byte)Tissue.Armor) continue;
                sx += i % o.SX + o.X0 + .5f; sz += i / o.SX % o.SZ + o.Z0 + .5f; sy += i / (o.SX * o.SZ) + o.Y0 + .5f; n++;
            }
            g.Boss = n > 0 ? new Vec(sx / n / S, sy / n / S, sz / n / S + .5f) : g.OffFist + new Vec(0, 0, 1.5f);
        }
        else g.Boss = g.OffFist + new Vec(0, 0, 1);
        // the front of the head: the foremost rows of the trunk
        var b = plan.Grid[(int)BodyPart.Body];
        if (b != null)
        {
            int front = int.MinValue;
            for (int i = 0; i < b.M.Length; i++) if (b.M[i] != 0) front = Math.Max(front, i / b.SX % b.SZ + b.Z0);
            float sx = 0, sy = 0; int n = 0;
            for (int i = 0; i < b.M.Length; i++)
            {
                if (b.M[i] == 0 || i / b.SX % b.SZ + b.Z0 < front - 1) continue;
                sx += i % b.SX + b.X0 + .5f; sy += i / (b.SX * b.SZ) + b.Y0 + .5f; n++;
            }
            g.Snout = new Vec(n > 0 ? sx / n / S : 0, n > 0 ? sy / n / S : t.BodyH * .6f, (front + 1) / S);
        }
        return g;
    }

    // ------------------------------------------------------------ the stroke at a moment

    static float Keys(float s, float sc, float a, float b, float c) => s <= sc ? a + (b - a) * Math.Clamp(s / Math.Max(sc, 1e-3f), 0, 1) : b + (c - b) * Math.Clamp((s - sc) / Math.Max(1 - sc, 1e-3f), 0, 1);

    /// <summary>How a figure holds itself when it is between blows, by what it fights with: its blade up before it, a spear levelled, a club on the shoulder.</summary>
    public static StrokePose Guard(Grip g, UnitType t)
    {
        var p = new StrokePose { Off = Turn.About(Vec.Left, -.45f) };
        switch (t.Tool)
        {
            case "spear" or "lance":
                p.Main = Turn.Arm(V(0, -1, .12f), V(.08f, .1f, 1));
                break;
            case "halberd":
                p.Main = Turn.Arm(V(0, -.9f, .45f), V(.15f, .55f, .82f));
                break;
            case "club" or "maul" or "mallet" or "pick":
                // up on the shoulder, ready to come down
                p.Main = Turn.Arm(V(-.15f, -.2f, .97f), V(0, .97f, .2f));
                break;
            default:
                // the blade raised before it, its point toward the one it faces
                p.Main = Turn.Arm(V(.1f, -.72f, .68f), V(.2f, .68f, .7f));
                break;
        }
        if (g.Shield) p.Off = Turn.About(Vec.Left, -1.05f);
        if (g.TwoHands) p.Off = OffGrip(g, p.Main, p.Off);
        p.Lean = .06f;
        return p;
    }

    /// <summary>
    /// The pose at a point of a stroke (s: 0 drawn back, then the blow, 1 spent), with the stroke tipped down by aim and
    /// turned toward the middle by yaw (radians), and the step of so many voxels.
    /// </summary>
    public static StrokePose Pose(Grip g, UnitType t, StrokeKind k, float s, float aim, float yaw, float lunge)
    {
        var sh = Shapes[(int)k];
        var p = new StrokePose();
        float sc = Contact(k);
        p.Twist = Keys(s, sc, sh.T0, sh.Tc, sh.T1);
        p.Lean = Keys(s, sc, sh.L0, sh.Lc, sh.L1);
        // the step: from drawn back (a thrust, a bash, a bite from back of the lunge) to where it meets, and on
        float kk = g.Height / 15;
        p.Lunge = Keys(s, sc, (sh.Way == Way.Arc ? 0 : lunge) - sh.Back * kk, lunge, lunge + sh.Over * kk);
        p.Drop = Drop(g, p.Lunge);
        var tilt = Turn.About(Vec.Up, yaw) * Turn.About(Vec.Left, aim);
        var guard = Guard(g, t);
        switch (sh.Way)
        {
            case Way.Arc:
            {
                // from the saddle the blow goes on down past the horse's shoulder
                float a = Keys(s, sc, sh.A0, sh.Ac, sh.A1 + (g.Mounted ? D(50) : 0));
                var b = sh.U * MathF.Cos(a) + sh.V * MathF.Sin(a);
                var arm = sh.U * -MathF.Sin(a) + sh.V * MathF.Cos(a);
                p.Main = tilt * Turn.Arm(arm, b);
                break;
            }
            case Way.Line:
                // the fist at the hip a little out to the side, the point straight on
                p.Main = tilt * Turn.Arm(V(-.42f, -.9f, 0), Vec.Forward);
                break;
            case Way.Shield:
                p.Main = guard.Main;
                p.Off = tilt * Turn.About(Vec.Left, Keys(s, sc, sh.O0, sh.Oc, sh.O1));
                break;
            case Way.Jaws:
                p.Main = p.Off = Turn.None;
                p.Lean += aim;
                break;
        }
        // a rider does not twist his horse round, nor lean it into the blow
        if (g.Mounted) { p.Twist = 0; p.Lean *= .25f; }
        if (sh.Way is Way.Arc or Way.Line)
            p.Off = g.TwoHands ? OffGrip(g, p.Main, guard.Off)
                // a shield kept up before the body, swung out of the blade's way as it comes across
                : g.Shield ? Turn.About(Vec.Up, Keys(s, sc, 0, sh.Open, sh.Open * .8f)) * Turn.About(Vec.Left, -1.05f + Keys(s, sc, .15f, -.1f, .1f))
                : Turn.About(Vec.Left, Keys(s, sc, -.6f, -.2f, .35f));
        return p;
    }

    /// <summary>
    /// Drawing back for a stroke, from the guard (w 0) to drawn back (w 1): a blade, an axe or a club comes from its guard
    /// into the stroke's own arc and runs back along it, up over the head and down the back, out to the right, or down
    /// by the thigh, so that it never swings the short way through the one it means to strike; a thrust, a shield or the
    /// jaws are simply drawn back.
    /// </summary>
    public static StrokePose Windup(Grip g, UnitType t, StrokeKind k, float w, float aim, float yaw, float lunge)
    {
        var sh = Shapes[(int)k];
        var guard = Guard(g, t);
        var back = Pose(g, t, k, 0, aim, yaw, lunge);
        if (sh.Way != Way.Arc || g.Beast) return Mix(guard, back, Smooth(w));
        // the arc from where the guard comes into it back to drawn back: the first fifth of the way into it, the rest along it
        const float into = .2f;
        var entry = Pose(g, t, k, (sh.Entry - sh.A0) / (sh.A1 - sh.A0), aim, yaw, lunge);
        entry.Twist = back.Twist * .3f; entry.Lean = guard.Lean; entry.Lunge = back.Lunge * .3f; entry.Drop = Drop(g, entry.Lunge);
        if (w < into) return Mix(guard, entry, Smooth(w / into));
        float x = Smooth((w - into) / (1 - into));
        var p = Pose(g, t, k, (sh.Entry + (sh.A0 - sh.Entry) * x - sh.A0) / (sh.A1 - sh.A0), aim, yaw, lunge);
        // the trunk winds up with it
        p.Twist = entry.Twist + (back.Twist - entry.Twist) * x; p.Lean = entry.Lean + (back.Lean - entry.Lean) * x;
        p.Lunge = entry.Lunge + (back.Lunge - entry.Lunge) * x; p.Drop = Drop(g, p.Lunge);
        return p;
    }

    static float Smooth(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

    /// <summary>Between two poses, so far from the one to the other.</summary>
    public static StrokePose Mix(in StrokePose a, in StrokePose b, float k) => new()
    {
        Main = Turn.Slerp(a.Main, b.Main, k), Off = Turn.Slerp(a.Off, b.Off, k),
        Twist = a.Twist + (b.Twist - a.Twist) * k, Lean = a.Lean + (b.Lean - a.Lean) * k,
        Lunge = a.Lunge + (b.Lunge - a.Lunge) * k, Drop = a.Drop + (b.Drop - a.Drop) * k,
    };

    /// <summary>
    /// How far the hips sink as a man steps so far forward (or back) on straight legs, the back foot where it stood and
    /// the hips over the middle of the step; a beast and a horse do not sink.
    /// </summary>
    public static float Drop(Grip g, float lunge)
    {
        if (g.Beast || g.Mounted || g.Leg <= 0) return 0;
        float l = Math.Min(MathF.Abs(lunge), g.Leg * .8f);
        return g.Leg - MathF.Sqrt(g.Leg * g.Leg - l * l);
    }

    /// <summary>The off hand on the haft of a two-handed weapon, a little before the main fist (as near as the arm reaches).</summary>
    static Turn OffGrip(Grip g, Turn main, Turn fallback)
    {
        var fist = g.Shoulder + main.Apply(g.Fist - g.Shoulder);
        var along = main.Apply(Vec.Forward);
        var want = fist + along * (g.Height * .16f) - g.OffShoulder;
        float reach = (g.OffFist - g.OffShoulder).Length;
        if (want.Length < .3f * reach) return fallback;
        return Turn.Arm(want, along);
    }

    // ------------------------------------------------------------ the pose in the world

    /// <summary>A point of the figure's trunk (rest frame) where the pose puts it, in the figure's frame.</summary>
    public static Vec OnTrunk(Grip g, in StrokePose p, Vec v)
    {
        var turn = Turn.About(Vec.Up, p.Twist) * Turn.About(Vec.Left, p.Lean);
        return g.Hip + turn.Apply(v - g.Hip) + new Vec(0, -p.Drop, p.Lunge);
    }

    /// <summary>A point of the main arm, or of what it holds (rest frame), where the pose puts it.</summary>
    public static Vec OnMain(Grip g, in StrokePose p, Vec v) => OnTrunk(g, p, g.Shoulder + p.Main.Apply(v - g.Shoulder));

    /// <summary>A point of the off arm (rest frame) where the pose puts it.</summary>
    public static Vec OnOff(Grip g, in StrokePose p, Vec v) => OnTrunk(g, p, g.OffShoulder + p.Off.Apply(v - g.OffShoulder));

    /// <summary>The striking part's line at rest, from near the fist (0) to its end (1).</summary>
    public static Vec Blade(Grip g, float k) => new(g.BladeX, g.BladeY0 + (g.BladeY1 - g.BladeY0) * k, g.Blade0 + (g.Blade1 - g.Blade0) * k);

    /// <summary>The striking part of a stroke at a point along it, posed: k from its root (0) to its end (1).</summary>
    public static Vec Striking(Grip g, StrokeKind k, in StrokePose p, float along)
    {
        switch (Shapes[(int)k].Way)
        {
            case Way.Shield:
                // across the shield's face, from its lower edge to its upper
                return OnOff(g, p, g.Boss + new Vec(0, (along - .5f) * g.Height * .2f, 0));
            case Way.Jaws:
                return OnTrunk(g, p, g.Snout + new Vec(0, (along - .5f) * g.Height * .15f, 0));
            default:
                return OnMain(g, p, Blade(g, along));
        }
    }

    /// <summary>A point of a figure's frame in the world (voxels), for one standing where it stands and facing as it faces.</summary>
    public static Vec ToWorld(Unit u, Vec v)
    {
        float s = u.Leader ? 1.15f : 1, sin = MathF.Sin(u.Facing), cos = MathF.Cos(u.Facing);
        return new Vec(u.X + (v.X * sin + v.Z * cos) * s, u.Y + v.Y * s, u.Z + (-v.X * cos + v.Z * sin) * s);
    }

    /// <summary>A world point (voxels) in a figure's frame.</summary>
    public static Vec FromWorld(Unit u, Vec w)
    {
        float s = u.Leader ? 1 / 1.15f : 1, sin = MathF.Sin(u.Facing), cos = MathF.Cos(u.Facing);
        float x = w.X - u.X, z = w.Z - u.Z;
        return new Vec((x * sin - z * cos) * s, (w.Y - u.Y) * s, (x * cos + z * sin) * s);
    }

    // ------------------------------------------------------------ which stroke

    /// <summary>
    /// How likely each stroke is for a striker at one it strikes, by what it fights with: a sword cuts every way and
    /// thrusts, an axe comes down, a halberd chops and thrusts, a spear thrusts, a club and a maul smash; a shield is
    /// driven in at one close enough; a rider strikes down from the saddle; a beast bites. One much shorter is cut down at
    /// from over it; one standing higher is thrust at and cut up at; one with a shield is taken on its open side.
    /// </summary>
    public static void Weights(Unit a, Unit b, Span<float> w)
    {
        w.Clear();
        var plan = Bodies.Plan(a.T, a.Look);
        var g = GripOf(plan, a.T);
        if (g.Beast || a.T.NoHands || a.T.Tool == "bite") { w[(int)StrokeKind.Bite] = 1; return; }
        switch (a.T.Tool)
        {
            case "sword":
                w[0] = 2; w[1] = 3; w[2] = 2; w[3] = 2; w[4] = 1.5f; w[5] = 1.5f; break;
            case "longsword":
                w[0] = 2; w[1] = 3; w[2] = 2.5f; w[3] = 2; w[4] = 2; w[5] = 1.5f; break;
            case "axe":
                w[0] = 3; w[1] = 3; w[2] = 1.5f; w[3] = 1; w[5] = .5f; break;
            case "knife":
                w[4] = 3; w[1] = 1.5f; w[2] = 1; w[5] = 1.5f; break;
            case "club" or "maul":
                w[(int)StrokeKind.Smash] = 3; w[3] = 2; w[1] = 1.5f; break;
            case "mace" or "mallet":
                w[(int)StrokeKind.Smash] = 2.5f; w[1] = 2; w[3] = 1; break;
            case "halberd":
                w[(int)StrokeKind.PoleChop] = 3; w[4] = 2.5f; w[3] = .8f; w[5] = .5f; break;
            case "spear":
                w[4] = 4; w[5] = 1; break;
            case "lance":
                w[4] = 1; break;
            case "pick":
                w[0] = 3; w[1] = 1.5f; w[2] = 1; break;
            case "staff":
                w[(int)StrokeKind.Smash] = 2; w[4] = 2; w[3] = 1; break;
            default:
                w[0] = 2; w[1] = 2; w[4] = 1; break;
        }
        // a shield driven in, at one close enough for it
        if (g.Shield && (a.Wounds?.Shield ?? true) && !g.Mounted && b != null)
        {
            float d = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z)) - b.T.BodyR;
            if (d < g.Boss.Z + MaxStep(a, g) * 1.5f) w[(int)StrokeKind.Bash] = a.T.Tool == "mace" ? 2 : 1;
        }
        // from the saddle, only down at them, or the lance couched
        if (g.Mounted)
            for (int i = 0; i < Count; i++)
                if (i is not ((int)StrokeKind.Overhead or (int)StrokeKind.DiagR) && !(a.T.Tool == "lance" && i == (int)StrokeKind.Thrust)) w[i] = 0;
        if (b == null) return;
        float sa = a.Leader ? 1.15f : 1, sb = b.Leader ? 1.15f : 1;
        float top = b.Y + b.T.BodyH * sb - (a.Y + a.T.BodyH * sa), above = b.Y - a.Y;
        if (above > a.T.BodyH * .3f)
        {
            // up on a wall or a ladder over it: the point and the rising cut reach it, a cut from over the head does not
            w[4] *= 2; w[5] *= 2; w[0] *= .3f; w[3] *= .5f; w[(int)StrokeKind.PoleChop] *= .4f; w[(int)StrokeKind.Smash] *= .5f;
        }
        else if (top < -a.T.BodyH * .25f)
        {
            // a goblin before a man: cut down at
            w[0] *= 1.6f; w[1] *= 1.4f; w[2] *= 1.4f; w[3] *= .3f; w[5] *= .4f; w[(int)StrokeKind.Smash] *= 1.4f;
        }
        // a shield on its left takes the forehand: go round to its right, or in with the point
        if (b.Wounds?.Shield ?? Bodies.Plan(b.T, b.Look).Shield)
        {
            w[2] *= 1.6f; w[4] *= 1.4f; w[1] *= .6f; w[3] *= .7f;
        }
    }

    /// <summary>How far a figure steps into a blow at the most: a man half a metre, a beast a pounce.</summary>
    public static float MaxStep(Unit a, Grip g) => g.Beast ? a.T.BodyR * 1.8f + 2 : g.Mounted ? 6.5f : Math.Max(4.5f * a.T.BodyH / 15, a.T.Reach * .6f);
}

public sealed partial class Sim
{
    /// <summary>A blow landed: where on the body, on what, with which stroke. For the sparks, the blood and the ring of it.</summary>
    public event Action<Smiting> Smote;

    /// <summary>Blows struck since the start, and how many came to the body along their own sweep (the rest were out of reach of any step).</summary>
    public int Blows, BlowsMet;

    /// <summary>The stroke the player means with the next blow of the one gone into, as the view would have it; null for the way the eyes swung before it.</summary>
    public StrokeKind? WishStroke;

    /// <summary>
    /// The stroke a swing of the eyes means, just before the blow (radians: right, and down): down is a cut from over the
    /// head, down and to the left the forehand across, down and to the right the backhand, to the left a sweep, up a
    /// rising cut; eyes held still thrust — or cut from over the head, looking down at the one struck.
    /// </summary>
    public static StrokeKind StrokeOfSwing(float right, float down, float pitch)
    {
        float l = MathF.Sqrt(right * right + down * down);
        if (l < .03f) return pitch < -.3f ? StrokeKind.Overhead : pitch > .35f ? StrokeKind.Rising : StrokeKind.Thrust;
        float a = MathF.Atan2(down, right) * 180 / MathF.PI;   // 0 right, 90 down, ±180 left, −90 up
        return a switch
        {
            >= 60 and < 120 => StrokeKind.Overhead,
            >= 120 and < 160 => StrokeKind.DiagR,
            >= 20 and < 60 => StrokeKind.DiagL,
            >= 160 or < -150 => StrokeKind.Sweep,
            >= -20 and < 20 => StrokeKind.DiagL,
            _ => StrokeKind.Rising,
        };
    }

    /// <summary>
    /// The stroke a striker means next at one it strikes, worked out from the two of them alone and the count of its
    /// blows — not the one it struck last, but for a spear's thrust — so that the eye sees it drawn back for it before
    /// the blow, and the random stream of the simulation is not touched.
    /// </summary>
    public StrokeKind Choose(Unit a, Unit b)
    {
        Span<float> w = stackalloc float[Strokes.Count];
        Strokes.Weights(a, b, w);
        float sum = 0;
        for (int i = 0; i < w.Length; i++) sum += w[i];
        // not the one it struck last — unless that is what the weapon is for above all, as a spear's thrust, and then
        // less often
        if (a.Strokes > 0 && sum - w[(int)a.Stroke] > 1e-4f)
        {
            float last = w[(int)a.Stroke], keep = last >= sum * .6f ? last * .5f : 0;
            sum -= last - keep; w[(int)a.Stroke] = keep;
        }
        if (sum <= 0) return StrokeKind.Overhead;
        float r = (float)Hash.H3(a.Id * 31 + 7, a.Strokes, b?.Id ?? 0) * sum;
        for (int i = 0; i < w.Length; i++)
        {
            if (w[i] <= 0) continue;
            if ((r -= w[i]) < 0) return (StrokeKind)i;
        }
        for (int i = w.Length - 1; i >= 0; i--) if (w[i] > 0) return (StrokeKind)i;
        return StrokeKind.Overhead;
    }

    /// <summary>The stroke a striker is drawing back for now, at whoever it is set on.</summary>
    public StrokeKind Upcoming(Unit a)
    {
        var b = a.Target ?? a.StrokeFor;
        return b == null || b == a.StrokeFor ? a.NextStroke : Choose(a, b);
    }

    /// <summary>A stroke the player wished for, or the nearest the weapon can make of it (an axe does not thrust, a spear only does).</summary>
    StrokeKind Fit(Unit a, Unit b, StrokeKind wish)
    {
        Span<float> w = stackalloc float[Strokes.Count];
        Strokes.Weights(a, b, w);
        if (w[(int)wish] > 0) return wish;
        // the nearest in kind: a cut for a cut, coming the same way
        var near = wish switch
        {
            StrokeKind.Overhead => new[] { StrokeKind.PoleChop, StrokeKind.Smash, StrokeKind.DiagR, StrokeKind.Thrust },
            StrokeKind.DiagR => new[] { StrokeKind.Smash, StrokeKind.Overhead, StrokeKind.PoleChop, StrokeKind.Sweep, StrokeKind.Thrust },
            StrokeKind.DiagL => new[] { StrokeKind.DiagR, StrokeKind.Smash, StrokeKind.Overhead, StrokeKind.PoleChop, StrokeKind.Thrust },
            StrokeKind.Sweep => new[] { StrokeKind.DiagR, StrokeKind.Smash, StrokeKind.Overhead, StrokeKind.PoleChop, StrokeKind.Thrust },
            StrokeKind.Rising => new[] { StrokeKind.Thrust, StrokeKind.DiagR, StrokeKind.Smash },
            StrokeKind.Thrust => new[] { StrokeKind.Rising, StrokeKind.Overhead, StrokeKind.PoleChop, StrokeKind.Smash, StrokeKind.DiagR },
            _ => new[] { StrokeKind.Overhead, StrokeKind.Thrust, StrokeKind.Smash },
        };
        foreach (var k in near) if (w[(int)k] > 0) return k;
        for (int i = 0; i < w.Length; i++) if (w[i] > 0) return (StrokeKind)i;
        return wish;
    }

    /// <summary>
    /// A stroke laid out against the one it is struck at: aimed, and then the sweep followed from drawn back to spent
    /// until the striking part first comes inside the body (a standing cylinder): there, the spot it meets, the way it is
    /// going and the line of its edge. Most often the step is the least that brings the blade there; one out of reach of
    /// any step is met where the sweep comes nearest.
    /// </summary>
    void LayStroke(Unit a, Unit b, StrokeKind k, float jitterUp, float jitterSide, out Vec at, out Vec dir, out Vec root, out Vec edge)
    {
        var g = Strokes.GripOf(Bodies.Plan(a.T, a.Look), a.T);
        float sb = b.Leader ? 1.15f : 1, br = b.T.BodyR * sb + .25f, bh = b.T.BodyH * sb;
        Aim(a, b, k, jitterUp, jitterSide, out float aim, out float yaw, out float lunge);
        float step = Strokes.MaxStep(a, g);
        // the least step that brings it to the body: from half a stride on, longer till it does
        float s0 = 0;
        Vec c = default, t = default, r0 = default, e = default;
        bool met = false;
        if (!Strokes.Straight(k)) lunge *= .5f;
        for (int tries = 0; tries < 8 && !met; tries++)
        {
            met = Meet(a, g, k, aim, yaw, lunge, b, br, bh, out s0, out c, out t, out r0, out e);
            if (!met) { if (lunge >= step) break; lunge = Math.Min(step, lunge + 1); }
        }
        Blows++;
        if (met) BlowsMet++;
        else
        {
            // out of reach of any step: where the sweep comes nearest, taken in to the body's round
            Nearest(a, g, k, aim, yaw, lunge, b, bh, out s0, out c, out t, out r0, out e);
            float dx = c.X - b.X, dz = c.Z - b.Z, d = MathF.Sqrt(dx * dx + dz * dz);
            var moved = c;
            if (d > br) { moved.X = b.X + dx / d * br; moved.Z = b.Z + dz / d * br; }
            moved.Y = Math.Clamp(c.Y, b.Y + .5f, b.Y + bh - .5f);
            // the blade taken in with it
            r0 = r0 + (moved - c); c = moved;
        }
        a.Stroke = k; a.StrokeAim = aim; a.StrokeYaw = yaw; a.StrokeLunge = lunge; a.StrokeAt = s0; a.StrokeTime = Time;
        at = c; dir = t; root = r0; edge = e;
    }

    /// <summary>
    /// How a stroke would be laid out against one it is struck at, with the draws at their middle: for the eye, which
    /// draws the striker back for it before the blow.
    /// </summary>
    public void Aim(Unit a, Unit b, StrokeKind k, out float aim, out float yaw, out float lunge) => Aim(a, b, k, .45f, .5f, out aim, out yaw, out lunge);

    /// <summary>
    /// A stroke aimed: tipped down so far to come at the height it means (by the first draw), turned in to the middle and
    /// to one side or the other (by the second), and the step it is struck with, the least for an arc and for a thrust,
    /// a bash or a bite whatever brings it a little into the body.
    /// </summary>
    void Aim(Unit a, Unit b, StrokeKind k, float jitterUp, float jitterSide, out float aim, out float yaw, out float lunge)
    {
        var plan = Bodies.Plan(a.T, a.Look);
        var g = Strokes.GripOf(plan, a.T);
        float sb = b.Leader ? 1.15f : 1, br = b.T.BodyR * sb + .25f, bh = b.T.BodyH * sb;
        // the one struck in the striker's own frame
        var bl = Strokes.FromWorld(a, new Vec(b.X, b.Y, b.Z));
        float dist = MathF.Sqrt(bl.X * bl.X + bl.Z * bl.Z);
        float ka = a.Leader ? 1 / 1.15f : 1;
        float sc = Strokes.Contact(k), step = Strokes.MaxStep(a, g);
        lunge = Math.Min(step, Strokes.StepOf(k) * a.T.BodyH / 15);
        // aimed: in to the middle, at the height meant
        aim = (jitterUp - .45f) * .3f; yaw = 0;
        var mid = Strokes.Pose(g, a.T, k, sc, 0, 0, lunge);
        var sweet = Strokes.Striking(g, k, mid, .7f);
        var pivot = k == StrokeKind.Bash ? Strokes.OnTrunk(g, mid, g.OffShoulder) : k == StrokeKind.Bite ? Strokes.OnTrunk(g, mid, g.Hip) : Strokes.OnTrunk(g, mid, g.Shoulder);
        float height = Strokes.Aimed(k), ty = 0;
        if (height >= 0)
        {
            // a sweep at the neck or across the chest, by the first draw, a cut from high at where the neck meets the
            // shoulder; the rest at their height on the body, give or take
            float neck = Bodies.Plan(b.T, b.Look).HeadY0 / (float)BodyPlan.Sub * sb;
            ty = b.Y + bh * (height + (jitterUp - .45f) * .12f);
            if (k == StrokeKind.Sweep) ty = b.Y + (jitterUp < .5f ? neck - bh * .17f + (jitterUp - .25f) * 2 : neck + .25f + (jitterUp - .75f) * .8f);
            else if (k is StrokeKind.DiagR or StrokeKind.DiagL) ty = b.Y + neck - .3f + (jitterUp - .45f) * 2;
            ty = (ty - a.Y) * ka;
            float hz = Math.Max(1, dist - br * ka * .6f - pivot.Z);
            float now = MathF.Atan2(sweet.Y - pivot.Y, MathF.Max(.5f, sweet.Z - pivot.Z)), want = MathF.Atan2(ty - pivot.Y, hz);
            // from the saddle a long way down
            aim = Math.Clamp(now - want, -.75f, g.Mounted ? 1.3f : .75f);
            if (k == StrokeKind.Bite) aim = Math.Clamp(aim, -.3f, .5f);
        }
        // turned in to the middle — and every blow from the saddle, down at one beside the horse's head
        if (Strokes.Centred(k) || g.Mounted)
        {
            var side = sweet - pivot;
            float r = MathF.Sqrt(side.X * side.X + side.Z * side.Z);
            yaw = MathF.Atan2(-pivot.X + bl.X, Math.Max(1, r)) - MathF.Atan2(side.X, Math.Max(.5f, side.Z));
        }
        yaw += (jitterSide - .5f) * 1.6f * br / Math.Max(4, dist);
        // a thrust, a bash, a bite: the step that brings the point (the shield, the jaws) a little into the body as the stroke meets
        float Reach(float aim, float yaw)
        {
            float tip = Strokes.Striking(g, k, Strokes.Pose(g, a.T, k, sc, aim, yaw, 0), k == StrokeKind.Thrust ? 1 : .5f).Z;
            return Math.Clamp(dist - br * ka + (k == StrokeKind.Thrust ? 1.5f : .6f) - tip, -2 * a.T.BodyH / 15, k == StrokeKind.Bash ? step * 1.6f : step);
        }
        if (Strokes.Straight(k)) lunge = Reach(aim, yaw);
        // a thrust is a straight shaft from the hip, not a sweep about the shoulder: it is set again by where the shaft
        // passes the front of the body, a few times over, till it goes in at the height and the side meant
        if (k == StrokeKind.Thrust && height >= 0)
        {
            float front = dist - br * ka, tx = bl.X + (jitterSide - .5f) * br * ka * .8f;
            for (int i = 0; i < 3; i++)
            {
                var pz = Strokes.Pose(g, a.T, k, sc, aim, yaw, lunge);
                var at = Strokes.Striking(g, k, pz, 1);
                for (int j = 0; j <= 12; j++) { var q = Strokes.Striking(g, k, pz, j / 12f); if (q.Z >= front) { at = q; break; } }
                var piv = Strokes.OnTrunk(g, pz, g.Shoulder);
                float rz = Math.Max(1, at.Z - piv.Z);
                aim = Math.Clamp(aim + MathF.Atan2(at.Y - piv.Y, rz) - MathF.Atan2(ty - piv.Y, rz), -.9f, g.Mounted ? 1.3f : .9f);
                yaw += MathF.Atan2(tx - piv.X, rz) - MathF.Atan2(at.X - piv.X, rz);
                lunge = Reach(aim, yaw);
            }
        }
    }

    /// <summary>Follows a stroke from drawn back to spent: the first moment any of its striking part is inside the body.</summary>
    static bool Meet(Unit a, Grip g, StrokeKind k, float aim, float yaw, float lunge, Unit b, float br, float bh, out float sAt, out Vec at, out Vec dir, out Vec root, out Vec edge)
    {
        const int Steps = 28, Along = 7;
        Span<Vec> prev = stackalloc Vec[Along], now = stackalloc Vec[Along];
        for (int i = 0; i <= Steps; i++)
        {
            float s = i / (float)Steps;
            var p = Strokes.Pose(g, a.T, k, s, aim, yaw, lunge);
            int best = -1;
            float bestK = 9;
            for (int j = 0; j < Along; j++)
            {
                float kk = .1f + .9f * j / (Along - 1);
                var w = now[j] = Strokes.ToWorld(a, Strokes.Striking(g, k, p, kk));
                if (i == 0) continue;
                float dx = w.X - b.X, dz = w.Z - b.Z;
                bool inside = dx * dx + dz * dz <= br * br && w.Y >= b.Y - .5f && w.Y <= b.Y + bh + .5f;
                // of what is inside, the part of the blade that strikes best, two thirds of the way out
                if (inside && MathF.Abs(kk - .7f) < bestK) { bestK = MathF.Abs(kk - .7f); best = j; }
            }
            if (best >= 0)
            {
                at = now[best];
                var d = now[best] - prev[best];
                dir = d.Length < 1e-3f ? (new Vec(b.X, at.Y, b.Z) - at).Unit : d.Unit;
                // the blade from its root to its point as it meets the body
                root = now[0]; edge = now[Along - 1] - now[0];
                sAt = s;
                return true;
            }
            now.CopyTo(prev);
        }
        sAt = Strokes.Contact(k); at = default; dir = default; root = default; edge = default;
        return false;
    }

    /// <summary>Where a stroke that meets nothing comes nearest the body's axis, and how it is going there.</summary>
    static void Nearest(Unit a, Grip g, StrokeKind k, float aim, float yaw, float lunge, Unit b, float bh, out float sAt, out Vec at, out Vec dir, out Vec root, out Vec edge)
    {
        const int Steps = 28;
        float best = float.MaxValue;
        sAt = Strokes.Contact(k); at = new Vec(b.X, b.Y + bh * .6f, b.Z); dir = (at - new Vec(a.X, at.Y, a.Z)).Unit; root = at; edge = default;
        Vec last = default;
        for (int i = 0; i <= Steps; i++)
        {
            float s = i / (float)Steps;
            var p = Strokes.Pose(g, a.T, k, s, aim, yaw, lunge);
            var w = Strokes.ToWorld(a, Strokes.Striking(g, k, p, 1));
            float dx = w.X - b.X, dz = w.Z - b.Z, dy = Math.Max(0, Math.Max(b.Y - w.Y, w.Y - b.Y - bh));
            float d = dx * dx + dz * dz + dy * dy;
            if (i > 0 && d < best)
            {
                best = d; sAt = s; at = w;
                var m = w - last;
                if (m.Length > 1e-3f) dir = m.Unit;
                root = Strokes.ToWorld(a, Strokes.Striking(g, k, p, .1f));
                edge = w - root;
            }
            last = w;
        }
    }

    /// <summary>The struck one staggered by a shield driven into it: its own next blow comes a little later — one much bigger shrugs it off.</summary>
    void Staggered(Unit a, Unit b)
    {
        if (b.T.BodyH > a.T.BodyH * 1.3f || Kinds.IsEngine(b.T)) return;
        b.Cooldown = Math.Max(b.Cooldown, .5f);
    }
}
