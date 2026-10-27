namespace VoxelTower.Core;

/// <summary>Our architect: the master builder who plans a valley's defences and marks them out for the workers (Sim.Architect.cs).</summary>
public static partial class Bodies
{
    /// <summary>
    /// A master builder of ours: a long coat of ochre wool (a blue one on the other look) to the knees over a dark tunic,
    /// belted, a strap across the chest to a satchel at the hip, a broad-brimmed hat of dark felt over a greying head, a
    /// roll of plans under the left arm tied with a cord, brass dividers at the belt, and in the right hand a measuring
    /// rod taller than himself, marked off in red and pale bands every half metre.
    /// </summary>
    static BodyPlan Architect(int look)
    {
        var f = new BodyPlan { Chop = 1.2f, Stride = 7 };
        byte coat = look == 1 ? Mat.ClothBlue : Mat.ClothOchre;
        var b = new Build
        {
            Shirt = coat, Trousers = Mat.ClothDark, Sleeve = coat, Boots = Mat.Leather, Belt = Mat.LeatherDark, Boot = 2,
            Skin = HumanSkins[(look + 1) % 3], Hair = look == 1 ? Mat.HairBrown : Mat.HairGrey, HairStyle = 1,
        };
        var s = Humanoid(b, f);
        int hy1 = b.Crown;
        // the coat's skirts to the knees, open a little at the front, its hem a darker band
        s.Box(Body, -b.Chest, b.Waist - 5, b.Z0 - 1, b.Chest, b.Waist, b.Z1 + 1, coat);
        s.Cut(Body, -1, b.Waist - 5, b.Z1, 1, b.Waist - 2, b.Z1 + 1, both: false);
        s.Paint(Body, -b.Chest, b.Waist - 5, b.Z0 - 1, b.Chest, b.Waist - 4, b.Z1 + 1, Mat.ClothBrown);
        // the strap from the right shoulder across the chest to the satchel at the left hip
        for (int k = 0; k < 7; k++) s.Dot(Body, -b.Chest + 1 + k, b.Neck - 1 - k, b.Z1, Mat.LeatherDark, Tissue.Gear);
        s.Box(Body, b.Chest, b.Waist - 3, -1, b.Chest + 1, b.Waist + 1, 2, Mat.Leather, Tissue.Gear);
        // the dividers in brass, hanging from the belt on the coat's front: two legs from a hinge
        s.Dot(Body, -2, b.Waist - 1, b.Z1 + 1, Mat.Brass, Tissue.Gear);
        s.Dot(Body, -3, b.Waist - 2, b.Z1 + 1, Mat.Brass, Tissue.Gear);
        s.Dot(Body, -1, b.Waist - 2, b.Z1 + 1, Mat.Brass, Tissue.Gear);
        // the roll of plans under the off arm: a pale cylinder along the side, tied with a red cord in its middle
        s.Box(Body, b.Chest, b.Neck - 6, b.Z0 - 2, b.Chest + 1, b.Neck - 4, b.Z1 + 3, Mat.Linen, Tissue.Gear);
        s.Box(Body, b.Chest, b.Neck - 6, 0, b.Chest + 1, b.Neck - 4, 1, Mat.ClothRed, Tissue.Gear);
        // the hat: a brim two wider than the head each way, the crown on it, a band round the crown
        s.Box(Body, -b.HeadW - 2, hy1, b.HeadZ - 2, b.HeadW + 2, hy1 + 1, b.HZ1 + 2, Mat.LeatherDark, Tissue.Gear);
        s.Box(Body, -b.HeadW + 1, hy1 + 1, b.HeadZ + 1, b.HeadW - 1, hy1 + 4, b.HZ1 - 1, Mat.LeatherDark, Tissue.Gear);
        s.Paint(Body, -b.HeadW + 1, hy1 + 1, b.HeadZ + 1, b.HeadW - 1, hy1 + 2, b.HZ1 - 1, Mat.ClothBrown);
        if (look == 0) Beard(s, b, Mat.HairGrey, full: false);
        MeasuringRod(s, b);
        return Done(s, f, b);
    }

    /// <summary>
    /// A builder's measuring rod: a straight shaft through the fist from the ground to over the head, painted in red and
    /// pale bands half a metre each, an iron shoe at its foot and a brass cap on its top.
    /// </summary>
    static void MeasuringRod(Sculpt s, Build b)
    {
        var g = StaffIn(s, b, out int x, out int zc);
        int top = (b.Crown + 2) * 4, band = g.C.R * VoxelWorld.PerMetre / 2;
        for (int y = 4; y < top; y++) g.Fill(x, y, zc, x + 2, y + 1, zc + 2, y / band % 2 == 0 ? Mat.ClothRed : Mat.Linen);
        g.Fill(x, 2, zc, x + 2, 4, zc + 2, Mat.Iron);
        g.Fill(x - 1, top, zc - 1, x + 3, top + 2, zc + 3, Mat.Brass);
        g.Tip = Half(g, x + 1, top + 1, zc + 1);
        g.Edge = (0, 0, 1);
    }
}
