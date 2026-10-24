using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Our engines (in half-voxels, facing +z, as the horde's are): a stone-thrower in clean timber bound in iron with our
/// blue pennant, a ballista on its carriage, and a trebuchet in its tall frame. The arm, the bow and the beam are the
/// moving part (Main), and of the body, not carried: the view swings them.
/// </summary>
public static partial class Bodies
{
    /// <summary>Our stone-thrower: the horde's frame, but squared and bound with iron at its corners, a pennant of ours on its post.</summary>
    static BodyPlan MangonelPlan(int look)
    {
        var f = new BodyPlan { Chop = 1, Stride = 40 };
        var s = new Sculpt();
        Wheels(s, 12, 22, -22);
        byte beam = look == 0 ? Mat.Wood : Mat.Plank;
        s.Box(Body, -12, 5, -22, -8, 9, 22, beam);
        s.Box(Body, 8, 5, -22, 12, 9, 22, beam);
        s.Box(Body, -12, 5, -22, 12, 9, -18, beam);
        s.Box(Body, -12, 5, 18, 12, 9, 22, beam);
        foreach (int x in new[] { -12, 10 }) foreach (int z in new[] { -22, 20 }) s.Box(Body, x, 9, z, x + 2, 10, z + 2, Mat.Iron);
        // the posts and the beam the arm strikes, padded with sacking
        s.Box(Body, -12, 9, 8, -9, 30, 12, beam);
        s.Box(Body, 9, 9, 8, 12, 30, 12, beam);
        s.Box(Body, -12, 27, 8, 12, 30, 12, Mat.Board);
        s.Box(Body, -6, 25, 8, 6, 27, 12, Mat.Sack);
        s.Box(Body, -12, 20, 8, -9, 21, 12, Mat.Iron);
        s.Box(Body, 9, 20, 8, 12, 21, 12, Mat.Iron);
        // the pennant: a pole on the post, the blue cloth out from it
        s.Box(Body, -12, 30, 10, -11, 44, 11, Mat.Handle);
        s.Box(Body, -11, 38, 10, -3, 43, 11, Mat.ClothBlue);
        s.Box(Body, -9, 40, 10, -6, 41, 11, Mat.ClothOchre);
        // the winch at the back, the skein of rope round the axle at the arm's foot
        s.Box(Body, -8, 9, -20, 8, 11, -17, Mat.Handle);
        s.Box(Body, -8, 8, -6, 8, 12, -2, Mat.Rag);
        // the arm, lying back from its axle, the cup and the stone at its end
        s.Box(Main, -2, 9, -26, 2, 12, -2, Mat.Trunk);
        s.Box(Main, -2, 9, -10, 2, 12, -9, Mat.Iron);
        s.Box(Main, -4, 10, -32, 4, 14, -24, Mat.Leather);
        s.Box(Main, -2, 14, -30, 2, 18, -26, Mat.Stone, Tissue.Gear);
        Joint(f, Main, 0, 10 / H, -4 / H);
        // (the portrait shows it from its wheels up)
        f.HeadY0 = 8; f.HeadY1 = 30;
        return s.Into(f, 8, 26, 12);
    }

    /// <summary>
    /// A ballista: a great crossbow on a carriage of two wheels, its stock along it on a post, the bow of horn and timber
    /// across its head, the string drawn back to the claw and a bolt like a spear laid in its groove.
    /// </summary>
    static BodyPlan BallistaPlan(int look)
    {
        var f = new BodyPlan { Chop = 1, Stride = 40 };
        var s = new Sculpt();
        byte beam = look == 0 ? Mat.Wood : Mat.Plank;
        // the carriage: two wheels and a trail to the ground behind
        foreach (int x in new[] { -12, 10 })
        {
            s.Box(Body, x, 0, -4, x + 2, 8, 4, Mat.Wood);
            s.Box(Body, x, 3, -1, x + 2, 5, 1, Mat.Iron);
            s.Cut(Body, x, 0, -4, x + 2, 1, -3, both: true);
            s.Cut(Body, x, 7, -4, x + 2, 8, -3, both: true);
            s.Cut(Body, x, 0, 3, x + 2, 1, 4, both: true);
            s.Cut(Body, x, 7, 3, x + 2, 8, 4, both: true);
        }
        s.Box(Body, -10, 3, -1, 10, 5, 1, Mat.Handle);
        s.Box(Body, -6, 5, -20, 6, 8, 6, beam);
        s.Box(Body, -3, 0, -22, 3, 5, -18, beam);
        // the post the stock turns on
        s.Box(Body, -2, 8, -2, 2, 15, 2, beam);
        s.Box(Body, -3, 13, -3, 3, 15, 3, Mat.Iron);
        // the stock, the bow across its head, the string back to the claw, the bolt in its groove
        s.Box(Main, -2, 15, -18, 2, 19, 20, beam);
        s.Box(Main, -16, 16, 16, 16, 19, 19, look == 0 ? Mat.Horn : Mat.Wood);
        s.Box(Main, -17, 16, 14, -15, 19, 16, Mat.Iron);
        s.Box(Main, 15, 16, 14, 17, 19, 16, Mat.Iron);
        s.Box(Main, -15, 17, -11, -14, 18, 16, Mat.Linen);
        s.Box(Main, 14, 17, -11, 15, 18, 16, Mat.Linen);
        s.Box(Main, -15, 17, -12, 15, 18, -11, Mat.Linen);
        s.Box(Main, -3, 19, -14, 3, 20, -10, Mat.Iron);
        s.Box(Main, -1, 19, -10, 1, 21, 26, Mat.Shaft);
        s.Box(Main, -2, 19, 26, 2, 22, 30, Mat.Iron);
        Joint(f, Main, 0, 15 / H, 0);
        f.HeadY0 = 4; f.HeadY1 = 22;
        return s.Into(f, 8, 20, 10);
    }

    /// <summary>
    /// A trebuchet: a base of heavy beams on small wheels, two uprights braced to it rising five metres, the axle across
    /// their tops, and on it the beam — the counterweight, a box of stones, hung from its short end, the long end lying
    /// back with the sling and its stone on the ground.
    /// </summary>
    static BodyPlan TrebuchetPlan(int look)
    {
        var f = new BodyPlan { Chop = 1, Stride = 40 };
        var s = new Sculpt();
        Wheels(s, 14, 32, -32);
        byte beam = look == 0 ? Mat.Wood : Mat.Plank;
        // the base: two long sills and the cross pieces
        s.Box(Body, -14, 5, -34, -10, 10, 34, beam);
        s.Box(Body, 10, 5, -34, 14, 10, 34, beam);
        foreach (int z in new[] { -34, -6, 6, 30 }) s.Box(Body, -14, 5, z, 14, 9, z + 4, beam);
        // the uprights, braced fore and aft in steps
        foreach (int x in new[] { -14, 10 })
        {
            s.Box(Body, x, 10, -3, x + 4, 76, 3, beam);
            for (int k = 0; k < 9; k++)
            {
                int y = 10 + k * 4;
                s.Box(Body, x, y, -24 + k * 2, x + 4, y + 5, -20 + k * 2, beam);
                s.Box(Body, x, y, 20 - k * 2, x + 4, y + 5, 24 - k * 2, beam);
            }
            s.Box(Body, x - 1, 70, -4, x + 5, 72, 4, Mat.Iron);
        }
        s.Box(Body, -10, 72, -2, 10, 75, 2, Mat.Iron);
        // a pennant of ours on an upright
        s.Box(Body, -14, 76, -1, -13, 92, 0, Mat.Handle);
        s.Box(Body, -13, 85, -1, -3, 91, 0, Mat.ClothBlue);
        // the beam on the axle: the long arm back, the short one forward with the counterweight under it
        s.Box(Main, -2, 71, -70, 2, 76, 22, Mat.Trunk);
        s.Box(Main, -3, 71, -40, 3, 76, -38, Mat.Iron);
        s.Box(Main, -2, 60, 14, 2, 71, 18, Mat.Iron);
        s.Box(Main, -9, 40, 8, 9, 60, 24, Mat.Board);
        s.Box(Main, -8, 58, 9, 8, 61, 23, Mat.Stone);
        // the sling hanging from the long end, its stone in it
        s.Box(Main, -1, 62, -70, 1, 71, -68, Mat.Linen);
        s.Box(Main, -4, 58, -72, 4, 62, -66, Mat.Leather);
        s.Box(Main, -2, 61, -71, 2, 65, -67, Mat.Stone, Tissue.Gear);
        Joint(f, Main, 0, 73 / H, 0);
        f.HeadY0 = 24; f.HeadY1 = 76;
        return s.Into(f, 8, 40, 14);
    }
}
