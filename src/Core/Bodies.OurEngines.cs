using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Our engines (in half-voxels, facing +z, as the horde's are), in clean timber bound with iron and flying our blue:
/// a stone-thrower on four wheels with its skein of twisted rope, a ballista on its carriage, and a trebuchet in its
/// frames. Each is sculpted in pieces the view moves as the engine works (UnitView.Engines.cs): the arm, the beam or
/// the stock (Main); the windlass, the counterweight or the left string (Off); the keg, the sling or the right string
/// (LegR); the stone or the bolt it is loaded with (LegL). The plans are marked as machines, so that none of it is
/// taken for a man's limbs.
/// </summary>
public static partial class Bodies
{
    /// <summary>
    /// A timber from one point to another (half-voxels): sculpted finer, a beam of so many half-voxels across laid
    /// straight along the line; in half-voxels alone, square blocks of that size stepped along it.
    /// </summary>
    static void Strut(Sculpt s, BodyPart p, float ax, float ay, float az, float bx, float by, float bz, int w, byte m, Tissue t = Tissue.Skin)
    {
        if (s.Detail) { Beam(s, p, ax, ay, az, bx, by, bz, w, m, t); return; }
        float dx = bx - ax, dy = by - ay, dz = bz - az, len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        int steps = Math.Max(1, (int)MathF.Ceiling(len));
        int lo = w / 2, hi = w - lo;
        for (int i = 0; i <= steps; i++)
        {
            float k = i / (float)steps;
            int x = (int)MathF.Round(ax + dx * k), y = (int)MathF.Round(ay + dy * k), z = (int)MathF.Round(az + dz * k);
            s.Box(p, x - lo, y - lo, z - lo, x + hi, y + hi, z + hi, m, t);
        }
    }

    /// <summary>
    /// A timber laid in cells, straight along its line: square across where it lies in a plane square to one axis (as
    /// the legs of a frame do), so that its long faces are flat, few faces to draw; a round rod where it runs slant
    /// every way.
    /// </summary>
    static void Beam(Sculpt s, BodyPart p, float ax, float ay, float az, float bx, float by, float bz, int w, byte m, Tissue t)
    {
        float hw = w * .5f;
        float[] a = { ax, ay, az }, b = { bx, by, bz };
        int flat = -1;
        for (int i = 0; i < 3; i++) if (MathF.Abs(b[i] - a[i]) < 1e-3f) { flat = i; break; }
        if (flat < 0) { s.FRod(p, ax, ay, az, bx, by, bz, hw, m, t); return; }
        int u = flat == 0 ? 1 : 0, v = flat == 2 ? 1 : 2;
        float du = b[u] - a[u], dv = b[v] - a[v], ll = du * du + dv * dv;
        int k = s.K;
        int[] lo = new int[3], hi = new int[3];
        for (int i = 0; i < 3; i++)
        {
            lo[i] = (int)MathF.Floor((MathF.Min(a[i], b[i]) - hw) * k);
            hi[i] = (int)MathF.Ceiling((MathF.Max(a[i], b[i]) + hw) * k);
        }
        var c = new float[3];
        for (int y = lo[1]; y < hi[1]; y++)
            for (int z = lo[2]; z < hi[2]; z++)
                for (int x = lo[0]; x < hi[0]; x++)
                {
                    c[0] = (x + .5f) / k; c[1] = (y + .5f) / k; c[2] = (z + .5f) / k;
                    if (MathF.Abs(c[flat] - a[flat]) > hw) continue;
                    float pu = c[u] - a[u], pv = c[v] - a[v];
                    float q = ll < 1e-6f ? 0 : Math.Clamp((pu * du + pv * dv) / ll, 0, 1);
                    float eu = pu - du * q, ev = pv - dv * q;
                    if (eu * eu + ev * ev <= hw * hw) s.FSet(p, x, y, z, m, t);
                }
    }

    /// <summary>A rope (half-voxels) one half-voxel thick, stepped along its line, each step going over a face of the
    /// last (along x, then y, then z) so that it holds together.</summary>
    static void Cord(Sculpt s, BodyPart p, float ax, float ay, float az, float bx, float by, float bz, byte m)
    {
        float dx = bx - ax, dy = by - ay, dz = bz - az, len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        int steps = Math.Max(1, (int)MathF.Ceiling(len * 1.5f));
        int px = (int)MathF.Floor(ax), py = (int)MathF.Floor(ay), pz = (int)MathF.Floor(az);
        for (int i = 0; i <= steps; i++)
        {
            float k = i / (float)steps;
            int x = (int)MathF.Floor(ax + dx * k), y = (int)MathF.Floor(ay + dy * k), z = (int)MathF.Floor(az + dz * k);
            s.Box(p, Math.Min(px, x), py, pz, Math.Max(px, x) + 1, py + 1, pz + 1, m);
            s.Box(p, x, Math.Min(py, y), pz, x + 1, Math.Max(py, y) + 1, pz + 1, m);
            s.Box(p, x, y, Math.Min(pz, z), x + 1, y + 1, Math.Max(pz, z) + 1, m);
            (px, py, pz) = (x, y, z);
        }
    }

    /// <summary>Iron bands round a beam lying along z (its box in half-voxels), finer: a band a cell wide every so often.</summary>
    static void Bands(Sculpt s, BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, int every)
    {
        for (int z = z0 + every / 2; z < z1; z += every) s.Paint(p, x0, y0, z, x1, y1, z + 1, Mat.Iron);
    }

    /// <summary>A spoked wheel of a windlass seen from the side (in the plane x = x0..x1), its hub at (cy, cz): a cross of spokes and a rim.</summary>
    static void Spokes(Sculpt s, BodyPart p, int x0, int x1, int cy, int cz, int r, byte m)
    {
        s.Box(p, x0, cy - r, cz - 1, x1, cy + r, cz + 1, m);
        s.Box(p, x0, cy - 1, cz - r, x1, cy + 1, cz + r, m);
        s.Box(p, x0, cy - 1, cz - 1, x1, cy + 1, cz + 1, Mat.Iron);
    }

    /// <summary>
    /// Our stone-thrower, a mangonel: a heavy frame on four wheels; across it a skein of rope twisted tight between two
    /// iron washers and their levers outside the frame; the arm standing out of the skein, lying back when it is wound
    /// down, a spoon of oak at its end; before it two posts and the crossbeam it strikes, padded with straw sacks and
    /// braced down to the frame; a windlass of two spoked wheels at the back, and our pennant on a post.
    /// </summary>
    static BodyPlan MangonelPlan(int look)
    {
        var f = new BodyPlan { Chop = 1, Stride = 40, Machine = true };
        var s = new Sculpt();
        byte wood = look == 0 ? Mat.Board : Mat.BoardDark, dark = Mat.Wood;
        Wheels(s, 13, 24, -24);
        // the frame: two sills, cross pieces at the ends and under the skein
        s.Box(Body, -13, 5, -26, -9, 10, 26, wood);
        s.Box(Body, 9, 5, -26, 13, 10, 26, wood);
        s.Box(Body, -9, 5, -26, 9, 9, -22, wood);
        s.Box(Body, -9, 5, 22, 9, 9, 26, wood);
        s.Box(Body, -9, 4, -8, 9, 7, -4, wood);
        Bands(s, Body, -13, 5, -26, -9, 10, 26, 13);
        Bands(s, Body, 9, 5, -26, 13, 10, 26, 13);
        // the skein: rope twisted between the sills, its washers and levers on their outsides
        s.Box(Body, -9, 7, -6, 9, 13, 0, Mat.Linen);
        for (int x = -9; x < 9; x += 3) s.Box(Body, x, 7, -6, x + 1, 13, 0, Mat.Rag);
        foreach (int sg in new[] { -1, 1 })
        {
            int x0 = sg > 0 ? 13 : -14, x1 = x0 + 1;
            s.Box(Body, x0, 6, -7, x1, 14, 1, Mat.Iron);
            s.Box(Body, sg > 0 ? 14 : -15, 9, -12, sg > 0 ? 15 : -14, 11, 6, Mat.Handle);
        }
        // the posts and the crossbeam the arm strikes, the straw sacks on its back, braces down to the front
        foreach (int x in new[] { -12, 9 })
        {
            s.Box(Body, x, 10, 2, x + 3, 32, 6, wood);
            Strut(s, Body, x + 1.5f, 30, 5, x + 1.5f, 11, 20, 3, wood);
        }
        s.Box(Body, -12, 29, 2, 12, 33, 6, dark);
        s.Box(Body, -6, 25, 0, 6, 31, 2, Mat.Sack);
        s.Box(Body, -5, 26, -1, 5, 30, 0, Mat.Straw);
        s.Paint(Body, -12, 29, 2, -9, 33, 6, Mat.Iron);
        s.Paint(Body, 9, 29, 2, 12, 33, 6, Mat.Iron);
        // the pennant on a pole at the back corner
        s.Box(Body, -13, 10, -25, -12, 46, -24, Mat.Handle);
        s.Box(Body, -12, 39, -25, -3, 45, -24, Mat.ClothBlue);
        s.Box(Body, -10, 41, -25, -6, 43, -24, Mat.ClothOchre);
        // the windlass: a drum between the sills, its spoked wheels outside them; it turns as the crew winds (Off)
        s.Box(Off, -16, 6, -21, 16, 9, -18, dark);
        s.Box(Off, -9, 6, -21, 9, 9, -18, Mat.Linen);
        Spokes(s, Off, -17, -16, 7, -19, 5, Mat.Handle);
        Spokes(s, Off, 16, 17, 7, -19, 5, Mat.Handle);
        Joint(f, Off, 0, 7.5f / H, -19.5f / H);
        // the arm, lying back from the skein to the windlass, banded, the spoon at its end hollowed for the shot
        s.Box(Main, -2, 9, -10, 2, 13, -2, dark);
        s.Box(Main, -2, 9, -22, 2, 12, -10, dark);
        s.Box(Main, -1, 9, -26, 1, 12, -22, dark);
        s.Paint(Main, -2, 9, -8, 2, 13, -7, Mat.Iron);
        s.Paint(Main, -2, 9, -18, 2, 12, -17, Mat.Iron);
        s.Box(Main, -4, 9, -33, 4, 14, -25, wood);
        s.Cut(Main, -3, 11, -32, 3, 14, -26);
        s.Paint(Main, -4, 9, -33, 4, 10, -25, Mat.Iron);
        Joint(f, Main, 0, 10 / H, -3 / H);
        // the shot in the spoon: a stone (LegL), or a keg of pitch alight (LegR)
        s.Box(LegL, -2, 11, -31, 2, 15, -27, Mat.Stone);
        s.Box(LegR, -2, 11, -31, 2, 16, -27, Mat.Cask);
        s.Paint(LegR, -2, 12, -31, 2, 13, -27, Mat.Hoop);
        s.Paint(LegR, -2, 14, -31, 2, 15, -27, Mat.Hoop);
        s.Box(LegR, -1, 16, -30, 1, 18, -28, Mat.Flame);
        Joint(f, LegL, 0, 10 / H, -3 / H);
        Joint(f, LegR, 0, 10 / H, -3 / H);
        if (s.Detail)
        {
            // the stone round in the spoon, the keg's flame ragged
            s.FCut(LegL, -2, 11, -31, 2, 15, -27);
            s.FBall(LegL, 0, 13, -29, 2, 2, 2, Mat.Stone);
            s.FCut(LegR, -1, 17, -30, 1, 18, -28);
            s.FBox(LegR, -.5f, 17, -29.5f, .5f, 18, -28.5f, Mat.Flame);
            // the grain of the sills: a darker seam along them
            s.FPattern(Body, -26, 10, -52, 26, 20, 52, (x, y, z) => s.FMat(Body, x, y, z) == wood && y == 15 && Math.Abs(x) > 17 ? Mat.BoardDark : (byte)0);
        }
        // (the portrait shows it from its wheels up)
        f.HeadY0 = 4; f.HeadY1 = 34;
        return s.Into(f, 8, 26, 12);
    }

    /// <summary>
    /// A ballista: a great crossbow on a carriage of two wheels and a trail, its stock turning on a post; at the stock's
    /// head two skeins of rope stand upright, an arm of horn and ash out of each, and the string runs from the arms' tips
    /// back to the claw when it is drawn; the windlass at the stock's foot winds it, and a bolt like a spear lies in the
    /// groove. The stock is Main; the left and the right string Off and LegR, each turning at its arm's tip; the bolt LegL.
    /// </summary>
    static BodyPlan BallistaPlan(int look)
    {
        var f = new BodyPlan { Chop = 1, Stride = 40, Machine = true };
        var s = new Sculpt();
        byte wood = look == 0 ? Mat.Board : Mat.BoardDark, dark = Mat.Wood;
        // the carriage: two wheels on an axle, the bed, the trail down to the ground behind
        foreach (int x in new[] { -13, 11 })
        {
            s.Box(Body, x, 0, -5, x + 2, 10, 5, dark);
            s.Box(Body, x, 4, -1, x + 2, 6, 1, Mat.Iron);
            s.Cut(Body, x, 0, -5, x + 2, 2, -3, both: true);
            s.Cut(Body, x, 8, -5, x + 2, 10, -3, both: true);
            s.Cut(Body, x, 0, 3, x + 2, 2, 5, both: true);
            s.Cut(Body, x, 8, 3, x + 2, 10, 5, both: true);
        }
        s.Box(Body, -11, 4, -1, 11, 6, 1, Mat.Iron);
        s.Box(Body, -6, 6, -10, 6, 9, 8, wood);
        Strut(s, Body, 0, 7, -10, 0, 1, -26, 4, wood);
        s.Box(Body, -3, 0, -28, 3, 2, -24, dark);
        // the post the stock turns on, its iron collar
        s.Box(Body, -2, 9, -2, 2, 15, 2, wood);
        s.Box(Body, -3, 13, -3, 3, 15, 3, Mat.Iron);
        // the stock with its groove, the claw and the windlass at its foot
        s.Box(Main, -3, 15, -20, 3, 19, 20, wood);
        s.Cut(Main, -1, 18, -16, 1, 19, 20);
        Bands(s, Main, -3, 15, -20, 3, 19, 20, 10);
        s.Box(Main, -2, 18, -13, 2, 21, -10, Mat.Iron);
        s.Box(Main, -7, 15, -20, 7, 18, -17, dark);
        Spokes(s, Main, -8, -7, 16, -18, 3, Mat.Handle);
        Spokes(s, Main, 7, 8, 16, -18, 3, Mat.Handle);
        // the head: a frame across the stock, two skeins upright in it, an arm out of each sweeping back a little
        s.Box(Main, -8, 13, 14, 8, 15, 20, dark);
        s.Box(Main, -8, 23, 14, 8, 25, 20, dark);
        foreach (int sg in new[] { -1, 1 })
        {
            int x0 = sg > 0 ? 3 : -7, x1 = x0 + 4;
            s.Box(Main, x0, 15, 15, x1, 23, 19, Mat.Linen);
            s.Box(Main, sg > 0 ? 7 : -8, 15, 14, sg > 0 ? 8 : -7, 23, 20, Mat.Iron);
            Strut(s, Main, sg * 7, 19, 17, sg * 17, 19, 12, 2, look == 0 ? Mat.Horn : dark);
            s.Box(Main, sg > 0 ? 16 : -18, 18, 11, sg > 0 ? 18 : -16, 21, 13, Mat.Iron);
        }
        Joint(f, Main, 0, 15 / H, 0);
        // the strings, drawn back from each arm's tip to the claw
        Cord(s, Off, -17, 19.5f, 12, -.5f, 19.5f, -11.5f, Mat.Linen);
        Cord(s, LegR, 17, 19.5f, 12, .5f, 19.5f, -11.5f, Mat.Linen);
        Joint(f, Off, -17 / H, 19.5f / H, 12 / H);
        Joint(f, LegR, 17 / H, 19.5f / H, 12 / H);
        // the bolt in the groove: an ash shaft, three vanes at its foot, the iron head out over the head of the stock
        s.Box(LegL, -1, 18, -10, 1, 20, 24, Mat.Shaft);
        s.Box(LegL, -2, 18, 24, 2, 21, 29, Mat.Iron);
        s.Box(LegL, -2, 20, -10, 2, 21, -5, Mat.Board);
        Joint(f, LegL, 0, 19.5f / H, -11.5f / H);
        if (s.Detail)
        {
            // the head of the bolt to a point, the skeins' twist
            s.FCut(LegL, -2, 18, 26, 2, 21, 29);
            s.FRod(LegL, 0, 19.5f, 24, 0, 19.5f, 28.8f, .9f, Mat.Iron);
            foreach (int sg in new[] { -1, 1 })
                for (int y = 30; y < 46; y += 3) s.FPaint(Main, sg > 0 ? 3 : -7, y / 2f, 15, sg > 0 ? 7 : -3, y / 2f + .5f, 19, Mat.Rag);
        }
        f.HeadY0 = 2; f.HeadY1 = 24;
        return s.Into(f, 8, 20, 10);
    }

    /// <summary>
    /// A trebuchet: a base of heavy sills on small wheels with a trough behind for the sling to lie in; on each side an
    /// A-frame of two legs and a king post, tied across at half its height, iron bearings at its top and the axle through
    /// them; on the axle the beam (Main), heavy at the axle and tapering out to the sling's hook. From its short end hangs
    /// the counterweight (Off), a crate of stones on iron hangers, free to swing; from the long end the sling (LegR), two
    /// ropes and a leather pouch, and the stone in it (LegL). A windlass of two spoked wheels at the back of the base
    /// hauls the long end down; our pennant flies from a frame.
    /// </summary>
    static BodyPlan TrebuchetPlan(int look)
    {
        var f = new BodyPlan { Chop = 1, Stride = 40, Machine = true };
        var s = new Sculpt();
        byte wood = look == 0 ? Mat.Board : Mat.BoardDark, dark = Mat.Wood;
        Wheels(s, 14, 34, -34);
        // the base: the sills, the cross sills, and the trough behind, pegged to the last of them
        s.Box(Body, -14, 5, -36, -10, 11, 36, wood);
        s.Box(Body, 10, 5, -36, 14, 11, 36, wood);
        foreach (int z in new[] { -36, -16, 12, 32 }) s.Box(Body, -10, 6, z, 10, 10, z + 4, wood);
        Bands(s, Body, -14, 5, -36, -10, 11, 36, 18);
        Bands(s, Body, 10, 5, -36, 14, 11, 36, 18);
        s.Box(Body, -5, 0, -54, 5, 2, -36, dark);
        s.Box(Body, -5, 2, -54, -4, 4, -36, dark);
        s.Box(Body, 4, 2, -54, 5, 4, -36, dark);
        s.Box(Body, -5, 2, -38, 5, 7, -36, dark);
        // the A-frames, their ties, their bearings, and the axle across
        foreach (int x in new[] { -14, 10 })
        {
            Strut(s, Body, x + 2, 11, -30, x + 2, 71, -2, 4, wood);
            Strut(s, Body, x + 2, 11, 30, x + 2, 71, 2, 4, wood);
            s.Box(Body, x, 11, -2, x + 4, 72, 2, wood);
            s.Box(Body, x, 38, -17, x + 4, 41, 17, wood);
            s.Box(Body, x - 1, 70, -4, x + 5, 76, 4, Mat.Iron);
        }
        s.Box(Body, -15, 72, -1, 15, 74, 1, Mat.Iron);
        // the windlass behind, its drum across the sills and a spoked wheel outside each
        s.Box(Body, -16, 11, -34, 16, 15, -30, dark);
        s.Box(Body, -10, 11, -34, 10, 15, -30, Mat.Linen);
        Spokes(s, Body, -18, -16, 13, -32, 7, Mat.Handle);
        Spokes(s, Body, 16, 18, 13, -32, 7, Mat.Handle);
        // a pennant of ours on the left frame
        s.Box(Body, -13, 76, -1, -12, 94, 0, Mat.Handle);
        s.Box(Body, -12, 86, -1, -2, 93, 0, Mat.ClothBlue);
        s.Box(Body, -10, 88, -1, -5, 90, 0, Mat.ClothOchre);
        // the beam, lying level as it was hewn: heavy at the axle, tapering out to the hook at the long end
        s.Box(Main, -3, 69, -14, 3, 77, 14, dark);
        s.Box(Main, -3, 70, -40, 3, 76, -14, dark);
        s.Box(Main, -2, 71, -60, 2, 75, -40, dark);
        s.Box(Main, -2, 71, -71, 2, 74, -60, dark);
        s.Box(Main, -3, 70, 14, 3, 76, 25, dark);
        foreach (int z in new[] { -56, -36, -16, 10, 22 }) s.Paint(Main, -3, 69, z, 3, 77, z + 1, Mat.Iron);
        s.Box(Main, -1, 70, -72, 1, 73, -70, Mat.Iron);
        // the hinge at the short end
        s.Box(Main, -5, 69, 20, -3, 74, 24, Mat.Iron);
        s.Box(Main, 3, 69, 20, 5, 74, 24, Mat.Iron);
        Joint(f, Main, 0, 73 / H, 0);
        // the counterweight: a crate of stones on two iron hangers from the hinge, down into the stones
        s.Box(Off, -8, 36, 12, 8, 58, 32, wood);
        s.Cut(Off, -7, 52, 13, 7, 58, 31);
        s.Box(Off, -7, 52, 13, 7, 56, 31, Mat.Stone);
        foreach (var (x, z) in new[] { (-5, 16), (2, 18), (-2, 25), (4, 27), (-6, 28), (0, 14), (5, 21) })
            s.Box(Off, x, 56, z, x + 2, 58, z + 2, (x + z & 1) == 0 ? Mat.StoneCool : Mat.StoneWarm);
        foreach (int y in new[] { 38, 47, 56 }) s.Paint(Off, -8, y, 12, 8, y + 1, 32, Mat.Iron);
        s.Box(Off, -6, 56, 21, -4, 70, 23, Mat.Iron);
        s.Box(Off, 4, 56, 21, 6, 70, 23, Mat.Iron);
        Joint(f, Off, 0, 70 / H, 22 / H);
        // the sling, hanging from the hook: two ropes and the pouch
        s.Box(LegR, -2, 54, -71, -1, 71, -70, Mat.Linen);
        s.Box(LegR, 1, 54, -71, 2, 71, -70, Mat.Linen);
        s.Box(LegR, -3, 50, -74, 3, 52, -67, Mat.Leather);
        s.Box(LegR, -3, 52, -74, -2, 55, -67, Mat.Leather);
        s.Box(LegR, 2, 52, -74, 3, 55, -67, Mat.Leather);
        Joint(f, LegR, 0, 71 / H, -70.5f / H);
        // the stone in the pouch
        s.Box(LegL, -2, 52, -73, 2, 57, -68, Mat.Stone);
        Joint(f, LegL, 0, 71 / H, -70.5f / H);
        if (s.Detail)
        {
            s.FCut(LegL, -2, 52, -73, 2, 57, -68);
            s.FBall(LegL, 0, 54.5f, -70.5f, 2.3f, 2.4f, 2.3f, Mat.Stone);
        }
        f.HeadY0 = 6; f.HeadY1 = 80;
        return s.Into(f, 8, 40, 14);
    }
}
