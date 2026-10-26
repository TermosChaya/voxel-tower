namespace VoxelTower.Core;

/// <summary>The guardians of the map's treasures (Troves.cs): the wolves of the ruins and the dens; the skeletons are the risen's bones.</summary>
public static partial class Bodies
{
    /// <summary>
    /// A grey wolf: leaner and longer in the leg than a war hound, its back level, a long narrow muzzle, the ears up and
    /// pointed, the ruff thick about the neck, a bushy tail hung low; grey, or brown-grey with a pale throat.
    /// </summary>
    static BodyPlan Wolf(int look)
    {
        var f = new BodyPlan { Stride = 10, Chop = .6f };
        byte fur = look == 0 ? Mat.HairGrey : Mat.Fawn, dark = Mat.FurBoar, pale = Mat.Belly;
        var s = Quadruped(f, 8, 2, 3, 4, -9, fur, dark);
        // the barrel, narrow and long, level along the back
        s.Box(Body, -3, 8, -10, 3, 13, 6, fur);
        s.Cut(Body, -3, 12, -10, -2, 13, 6, both: true);
        s.Paint(Body, -2, 8, -4, 2, 9, 5, pale);
        // the ruff about the neck, the neck forward, the head long and narrow, the muzzle darker at its tip
        s.Box(Body, -3, 10, 4, 3, 15, 8, fur);
        s.Box(Body, -2, 12, 7, 2, 16, 11, fur);
        s.Box(Body, -1, 12, 11, 1, 14, 15, fur);
        s.Box(Body, -1, 13, 14, 1, 14, 16, dark);
        s.Paint(Body, -1, 12, 11, 1, 13, 14, pale);
        // the eyes pale and the ears up
        s.Pair(Body, Body, -2, 14, 10, -1, 15, 11, Mat.Brew);
        s.Pair(Body, Body, -2, 16, 8, -1, 18, 9, fur);
        // the tail bushy and hung low behind
        s.Box(Body, -1, 9, -13, 1, 12, -10, fur);
        s.Box(Body, -1, 8, -14, 1, 10, -12, dark);
        Joint(f, Body, 0, 8 / H, 2 / H);
        f.HeadY0 = 12; f.HeadY1 = 18;
        f.NeckZ = 6; f.NeckY = 11;
        return s.Into(f, 8, 13, 3);
    }
}
