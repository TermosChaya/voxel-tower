using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>The beasts of the valley: deer, the bear, boars and hares.</summary>
public static partial class Bodies
{
    // ---------------------------------------------------------------- beasts: Main and Off are the front legs

    /// <summary>Four legs at the corners of a body, with hooves (or paws) at the bottom.</summary>
    static Sculpt Quadruped(BodyPlan f, int legH, int legW, int halfX, int frontZ, int backZ, byte fur, byte foot)
    {
        var s = new Sculpt();
        foreach (var (p, x0, z0) in new[] { (Main, -halfX, frontZ), (Off, halfX - legW, frontZ), (LegR, -halfX, backZ), (LegL, halfX - legW, backZ) })
        {
            s.Box(p, x0, 0, z0, x0 + legW, legH, z0 + legW, fur);
            s.Box(p, x0, 0, z0, x0 + legW, 1, z0 + legW, foot);
            Joint(f, p, (x0 + legW / 2f) / H, legH / H, (z0 + legW / 2f) / H);
        }
        f.Beast = true;
        return s;
    }

    /// <summary>
    /// A deer: slender legs, fuller above the knees; a long barrel of a body rounded along its edges and at its ends, the
    /// withers up over the shoulders, a pale belly, a white rump and a short tail; the neck up and forward in steps, the
    /// throat pale; the head with its corners taken off, a narrower muzzle, a dark nose, ears out; a stag (look 0) with
    /// antlers. A white stag all pale.
    /// </summary>
    static BodyPlan Deer(int look, bool white = false)
    {
        var f = new BodyPlan { Stride = 12 };
        byte fur = white ? Mat.Bone : Mat.FurDeer, pale = white ? Mat.Bone : Mat.Belly;
        var s = Quadruped(f, 12, 2, 3, 6, -9, fur, Mat.Hoof);
        foreach (var (leg, x0) in new[] { (Main, -3), (Off, 1) }) s.Box(leg, x0, 7, 5, x0 + 2, 12, 8, fur);
        foreach (var (leg, x0) in new[] { (LegR, -3), (LegL, 1) }) s.Box(leg, x0, 6, -10, x0 + 2, 12, -6, fur);
        s.Box(Body, -3, 12, -10, 3, 20, 9, fur);
        s.Cut(Body, -3, 12, -10, -2, 13, 9, both: true);
        s.Cut(Body, -3, 19, -10, -2, 20, 9, both: true);
        foreach (int z in new[] { -10, 8 }) s.Cut(Body, -3, 12, z, -2, 20, z + 1, both: true);
        s.Box(Body, -2, 20, 3, 2, 21, 8, fur);
        s.Paint(Body, -2, 12, -9, 2, 13, 8, pale);
        s.Paint(Body, -2, 13, -10, 2, 19, -9, pale);
        s.Box(Body, -1, 17, -11, 1, 19, -10, fur);
        s.Paint(Body, -1, 17, -11, 1, 18, -10, pale);
        s.Box(Body, -2, 18, 6, 2, 21, 9, fur);
        s.Box(Body, -2, 20, 7, 2, 23, 10, fur);
        s.Box(Body, -2, 22, 8, 2, 25, 11, fur);
        foreach (var (y0, z) in new[] { (18, 8), (20, 9), (22, 10) }) s.Paint(Body, -1, y0, z, 1, y0 + 2, z + 1, pale);
        s.Box(Body, -2, 23, 9, 2, 27, 14, fur);
        s.Round(Body, -2, 23, 9, 2, 27, 14);
        s.Box(Body, -1, 23, 14, 1, 26, 17, fur);
        s.Box(Body, -1, 24, 17, 1, 26, 18, Mat.Hoof);
        s.Pair(Body, Body, -2, 25, 12, -1, 26, 13, Mat.Eye);
        // the ears on the crown's corners (the antlers stand on them)
        s.Pair(Body, Body, -4, 26, 10, -1, 27, 12, fur);
        s.Pair(Body, Body, -5, 27, 10, -3, 28, 11, fur);
        // a stag's antlers: two beams up from the crown, each with a brow tine forward, one out and one back
        if (look == 0)
            foreach (int m in new[] { 1, -1 })
            {
                int X(int x) => m > 0 ? x : -1 - x;
                void Tine(int x0, int y0, int z0, int x1, int y1, int z1) => s.Box(Body, Math.Min(X(x0), X(x1 - 1)), y0, z0, Math.Max(X(x0), X(x1 - 1)) + 1, y1, z1, Mat.Bone);
                Tine(-2, 27, 10, -1, 36, 11);
                Tine(-2, 29, 11, -1, 30, 13);
                Tine(-4, 32, 10, -2, 33, 11);
                Tine(-2, 34, 8, -1, 35, 10);
                Tine(-3, 35, 10, -2, 37, 11);
            }
        Joint(f, Body, 0, 12 / H, 6 / H);
        f.HeadY0 = 23; f.HeadY1 = 27;
        f.NeckZ = 7; f.NeckY = 20;
        return s.Into(f, 12, 20, 4);
    }

    /// <summary>
    /// A cave bear: heavy legs, fuller above the knees; a great body rounded at its edges, a hump of dark fur over the
    /// shoulders; a broad head low on a short neck, a paler muzzle, the nose, small ears, eyes red in the dark.
    /// </summary>
    static BodyPlan BearPlan()
    {
        var f = new BodyPlan { Stride = 10 };
        var s = Quadruped(f, 10, 4, 6, 6, -11, Mat.FurBoar, Mat.Hoof);
        foreach (var (leg, x0) in new[] { (Main, -6), (Off, 2) }) s.Box(leg, x0, 5, 5, x0 + 4, 10, 11, Mat.FurBoar);
        foreach (var (leg, x0) in new[] { (LegR, -6), (LegL, 2) }) s.Box(leg, x0, 4, -13, x0 + 4, 10, -6, Mat.FurBoar);
        s.Box(Body, -6, 10, -13, 6, 24, 8, Mat.FurBoar);
        s.Cut(Body, -6, 10, -13, -5, 11, 8, both: true);
        s.Cut(Body, -6, 23, -13, -5, 24, 8, both: true);
        foreach (int z in new[] { -13, 7 }) s.Cut(Body, -6, 10, z, -5, 24, z + 1, both: true);
        s.Box(Body, -5, 24, -4, 5, 28, 6, Mat.FurBoar);
        s.Round(Body, -5, 24, -4, 5, 28, 6);
        s.Paint(Body, -4, 10, -12, 4, 11, 7, Mat.HairBrown);
        s.Box(Body, -3, 15, 7, 3, 22, 9, Mat.FurBoar);
        s.Box(Body, -4, 14, 8, 4, 22, 15, Mat.FurBoar);
        s.Round(Body, -4, 14, 8, 4, 22, 15);
        s.Box(Body, -2, 14, 15, 2, 18, 19, Mat.HairBrown);
        s.Box(Body, -1, 16, 19, 1, 18, 20, Mat.Hoof);
        s.Pair(Body, Body, -4, 22, 9, -2, 24, 11, Mat.FurBoar);
        s.Pair(Body, Body, -3, 19, 14, -2, 20, 15, Mat.Ember);
        s.Box(Body, -1, 22, -14, 1, 24, -12, Mat.FurBoar);
        Joint(f, Body, 0, 10 / H, 4 / H);
        f.HeadY0 = 14; f.HeadY1 = 24;
        f.NeckZ = 8; f.NeckY = 13;
        return s.Into(f, 6, 24, 6);
    }

    /// <summary>
    /// A boar: short legs, full above the knees; a heavy dark body rounded at its edges, high at the shoulders, a
    /// bristled ridge; a wedge of a head with its corners off, a pale snout, tusks, ears up, a thin tail.
    /// </summary>
    static BodyPlan Boar()
    {
        var f = new BodyPlan { Stride = 7 };
        var s = Quadruped(f, 6, 2, 4, 4, -6, Mat.FurBoar, Mat.Hoof);
        foreach (var (leg, x0) in new[] { (Main, -4), (Off, 2) }) s.Box(leg, x0, 3, 3, x0 + 2, 6, 7, Mat.FurBoar);
        foreach (var (leg, x0) in new[] { (LegR, -4), (LegL, 2) }) s.Box(leg, x0, 3, -7, x0 + 2, 6, -3, Mat.FurBoar);
        s.Box(Body, -4, 4, -8, 4, 13, 6, Mat.FurBoar);
        s.Cut(Body, -4, 12, -8, -3, 13, 6, both: true);
        s.Cut(Body, -4, 4, -8, -3, 5, 6, both: true);
        s.Cut(Body, -4, 4, -8, -3, 13, -7, both: true);
        s.Box(Body, -3, 13, -1, 3, 14, 5, Mat.FurBoar);
        s.Box(Body, -1, 13, -6, 1, 15, 5, Mat.Hair);
        s.Box(Body, -3, 4, 6, 3, 11, 10, Mat.FurBoar);
        s.Round(Body, -3, 4, 6, 3, 11, 10);
        s.Box(Body, -2, 4, 10, 2, 8, 13, Mat.FurBoar);
        s.Box(Body, -2, 4, 13, 2, 7, 14, Mat.SkinHuman);
        s.Pair(Body, Body, -3, 6, 11, -2, 9, 12, Mat.Bone);
        s.Pair(Body, Body, -3, 9, 9, -2, 10, 10, Mat.Eye);
        s.Pair(Body, Body, -3, 10, 7, -2, 13, 8, Mat.FurBoar);
        s.Box(Body, -1, 9, -9, 1, 11, -8, Mat.Hair);
        Joint(f, Body, 0, 6 / H, 4 / H);
        f.HeadY0 = 6; f.HeadY1 = 11;
        f.NeckZ = 6; f.NeckY = 5;
        return s.Into(f, 4, 13, 4);
    }

    /// <summary>A hare: short forelegs, long hind legs with big feet; a round crouched body, a white scut, the long ears tipped dark.</summary>
    static BodyPlan Hare()
    {
        var f = new BodyPlan { Stride = 4 };
        var s = new Sculpt();
        foreach (var (leg, x0) in new[] { (Main, -2), (Off, 1) })
        {
            s.Box(leg, x0, 0, 2, x0 + 1, 4, 3, Mat.FurHare);
            Joint(f, leg, (x0 + .5f) / H, 4 / H, 2.5f / H);
        }
        foreach (var (leg, x0) in new[] { (LegR, -2), (LegL, 1) })
        {
            s.Box(leg, x0, 0, -3, x0 + 1, 5, -2, Mat.FurHare);
            s.Box(leg, x0, 0, -2, x0 + 1, 1, 0, Mat.FurHare);
            s.Box(leg, x0, 2, -4, x0 + 1, 5, -1, Mat.FurHare);
            Joint(f, leg, (x0 + .5f) / H, 5 / H, -2.5f / H);
        }
        f.Beast = true;
        s.Box(Body, -2, 3, -4, 2, 7, 4, Mat.FurHare);
        s.Cut(Body, -2, 6, -4, -1, 7, 4, both: true);
        s.Cut(Body, -2, 3, -4, -1, 7, -3, both: true);
        s.Paint(Body, -2, 3, -3, 2, 4, 3, Mat.Belly);
        s.Box(Body, -1, 5, -5, 1, 7, -4, Mat.Belly);
        s.Box(Body, -2, 5, 3, 2, 9, 7, Mat.FurHare);
        s.Round(Body, -2, 5, 3, 2, 9, 7);
        s.Pair(Body, Body, -2, 7, 5, -1, 8, 6, Mat.Eye);
        s.Box(Body, -1, 5, 7, 1, 6, 8, Mat.SkinHuman);
        s.Pair(Body, Body, -2, 8, 4, -1, 14, 5, Mat.FurHare);
        s.Pair(Body, Body, -2, 13, 4, -1, 14, 5, Mat.Hoof);
        Joint(f, Body, 0, 3 / H, 2 / H);
        f.HeadY0 = 5; f.HeadY1 = 9;
        f.NeckZ = 3; f.NeckY = 6;
        return s.Into(f, 3, 7, 2);
    }
}
