namespace VoxelTower.Core;

public static partial class Bodies
{
    /// <summary>
    /// An alchemist of the horde: a goblin in a robe to the shins, stained, a leather apron over it burnt through at the
    /// hem, goggles of green glass in brass rims down over its eyes (or pushed up on its brow), a bandolier of flasks
    /// across its chest, and a flask in its fist, the acid glowing through the glass.
    /// </summary>
    static BodyPlan Alchemist(int look)
    {
        var f = new BodyPlan { Chop = .5f, Carry = -.3f, Stride = 6 };
        byte robe = new[] { Mat.ClothBrown, Mat.ClothDark, Mat.ClothGreen }[look % 3];
        var b = GoblinBuild(robe, robe, GoblinSkins[(look + 1) % 3]);
        b.Sleeve = robe;
        var s = Humanoid(b, f);
        GoblinFace(s, b, look == 2, look);
        Claws(s, b, Mat.Horn);
        int hx = b.HeadW, hz1 = b.HZ1, eye = b.Neck + 4;
        // the robe's skirt down over the legs to the shins, a slit up the front
        s.Box(Body, -b.Chest - 1, 2, b.Z0 - 1, b.Chest + 1, b.Waist, b.Z1 + 1, robe);
        s.Cut(Body, -1, 2, b.Z1, 1, 4, b.Z1 + 1, both: true);
        // the apron from the chest to the knees, on the robe, its strap round the neck
        s.Box(Body, -2, 3, b.Z1, 2, b.Neck - 2, b.Z1 + 1, Mat.Leather);
        s.Paint(Body, -2, b.Neck - 2, b.Z1 - 1, -1, b.Neck, b.Z1, Mat.Leather);
        s.Paint(Body, 1, b.Neck - 2, b.Z1 - 1, 2, b.Neck, b.Z1, Mat.Leather);
        // the bandolier from the left shoulder to the right hip, and three flasks in its loops
        for (int y = b.Waist; y < b.Neck; y++)
        {
            int x = b.Chest - 1 - (y - b.Waist) * (2 * b.Chest - 1) / (b.Neck - b.Waist);
            s.Paint(Body, x, y, b.Z1 - 1, x + 1, y + 1, b.Z1 + 1, Mat.LeatherDark, Tissue.Gear);
        }
        foreach (int k in new[] { 1, 3, 5 })
        {
            int y = b.Waist + k, x = b.Chest - 1 - k * (2 * b.Chest - 1) / (b.Neck - b.Waist);
            s.Box(Body, x, y - 1, b.Z1, x + 1, y + 1, b.Z1 + 2, Mat.Phial, Tissue.Gear);
            s.Dot(Body, x, y + 1, b.Z1, Mat.Handle, Tissue.Gear);
        }
        // goggles: a strap round the head, and two rims of brass with green glass in them, over the eyes or up on the brow
        int gy = look == 2 ? b.Crown - 2 : eye;
        s.Coat(Body, -hx - 1, gy, b.HeadZ - 1, hx + 1, gy + 1, hz1 + 1, Mat.LeatherDark, Tissue.Gear);
        s.Pair(Body, Body, -3, gy - (look == 2 ? 0 : 1), hz1, -1, gy + 1, hz1 + 1, Mat.Phial, Tissue.Gear);
        if (s.Detail)
        {
            int F = hz1 * 2, Y = gy * 2 - (look == 2 ? 0 : 2), W = b.Chest * 2;
            // the brass rims round the glass
            foreach (int sg in new[] { 1, -1 })
            {
                int X(int x) => sg > 0 ? x : -1 - x;
                for (int y = Y - 1; y <= Y + 4; y++)
                    for (int x = -7; x <= -1; x++)
                        if (y == Y - 1 || y == Y + 4 || x == -7 || x == -1) s.FSet(Body, X(x), y, F, Mat.Brass, Tissue.Gear);
            }
            // the robe stained where the acid splashed it, and the apron's hem eaten
            for (int y = 4; y < b.Waist * 2; y++)
                for (int x = -W - 2; x < W + 2; x++)
                    if (Dice(x, y, look, 91) < .05f) s.FPut(Body, x, y, (b.Z1 + 1) * 2 - 1, Mat.Phial);
            for (int x = -4; x < 4; x++) if (Dice(x, look, 2, 92) < .5f) s.FDel(Body, x, 6, (b.Z1 + 1) * 2 + 1);
            // a tuft of hair from under the strap on one
            if (look == 1) for (int x = -2; x < 2; x++) for (int z = -3; z < 1; z++) if (Dice(x, z, 1, 93) < .6f) s.FSet(Body, x, b.Crown * 2, z, Mat.Hair);
        }
        FlaskInHand(s, b);
        return Done(s, f, b);
    }

    /// <summary>A flask of acid in the right fist: a round belly of green glass, a neck, a stopper of wood, the acid glowing in it.</summary>
    static void FlaskInHand(Sculpt s, Build b)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        float cx = x + 1, cy = y + 1, cz = z0 + 6;
        g.Ball(cx / 4, cy / 4, cz / 4, 1.3f, 1.3f, 1.3f, Mat.Phial);
        g.Fill(x, y + 5, (int)cz - 1, x + 2, y + 8, (int)cz + 1, Mat.Phial);
        g.Fill(x, y + 8, (int)cz - 1, x + 2, y + 10, (int)cz + 1, Mat.Handle);
        g.Tip = Half(g, cx, cy, cz);
    }
}
