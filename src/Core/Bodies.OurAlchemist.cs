namespace VoxelTower.Core;

public static partial class Bodies
{
    /// <summary>
    /// An alchemist of ours: a man in a shirt and dark breeches under a long leather apron from the chest to the knees,
    /// stained green where the acid splashed it, gloves of dark leather, goggles of green glass in brass rims over his eyes
    /// (or pushed up on the hood of the garrison's blue), a bandolier of flasks across his chest and a satchel at his hip,
    /// and a flask in his fist, the acid glowing through the glass.
    /// </summary>
    static BodyPlan OurAlchemist(int look)
    {
        var f = new BodyPlan { Chop = .8f, Carry = -.1f };
        var b = new Build
        {
            Shirt = look == 1 ? Mat.ClothBlue : Mat.Linen, Trousers = Mat.ClothDark, Skin = HumanSkins[(look + 1) % 3], Hair = Hairs[(look + 3) % 4],
            HairStyle = look == 2 ? 3 : 1,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz1 = b.HZ1;
        // the apron from the chest to the knees, its straps over the shoulders
        s.Box(Body, -3, b.Waist - 5, b.Z1, 3, b.Neck - 2, b.Z1 + 1, Mat.Leather);
        s.Box(Body, -3, b.Neck - 2, b.Z1 - 1, -2, b.Neck, b.Z1, Mat.Leather);
        s.Box(Body, 2, b.Neck - 2, b.Z1 - 1, 3, b.Neck, b.Z1, Mat.Leather);
        // gloves of dark leather over the hands and wrists
        s.Paint(Main, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + b.Hand, b.ArmZ1, Mat.LeatherDark, Tissue.Armor);
        s.Paint(Off, b.Chest, b.HandY, b.ArmZ0, -b.ArmX0, b.HandY + b.Hand, b.ArmZ1, Mat.LeatherDark, Tissue.Armor);
        // a leather cap, or a hood of our blue with the goggles pushed up on it, or the bare head and a beard
        if (look == 0)
        {
            s.Paint(Body, -hx, b.Crown - 3, b.HeadZ, hx, b.Crown, hz1, Mat.Leather);
            s.Box(Body, -hx + 1, b.Crown, b.HeadZ + 1, hx - 1, b.Crown + 1, hz1 - 1, Mat.Leather);
        }
        else if (look == 1) Hood(s, b, Mat.ClothBlue);
        else Beard(s, b, b.Hair);
        // the bandolier from the left shoulder to the right hip, and three flasks in its loops
        for (int y = b.Waist; y < b.Neck; y++)
        {
            int x = b.Chest - 1 - (y - b.Waist) * (2 * b.Chest - 1) / (b.Neck - b.Waist);
            s.Paint(Body, x, y, b.Z1 - 1, x + 1, y + 1, b.Z1 + 2, Mat.LeatherDark, Tissue.Gear);
        }
        foreach (int k in new[] { 2, 5, 8 })
        {
            int y = b.Waist + k, x = b.Chest - 1 - k * (2 * b.Chest - 1) / (b.Neck - b.Waist);
            s.Box(Body, x, y - 1, b.Z1 + 1, x + 1, y + 1, b.Z1 + 2, Mat.Phial, Tissue.Gear);
            s.Dot(Body, x, y + 1, b.Z1 + 1, Mat.Handle, Tissue.Gear);
        }
        // the belt, and the satchel at the left hip with the necks of two flasks out of it
        s.Box(Body, -b.Chest - 1, b.Waist, b.Z0 - 1, b.Chest + 1, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        s.Box(Body, b.Chest, b.Waist - 4, b.Z0, b.Chest + 2, b.Waist, b.Z1, Mat.LeatherDark, Tissue.Gear);
        s.Dot(Body, b.Chest, b.Waist, b.Z0, Mat.Phial, Tissue.Gear);
        s.Dot(Body, b.Chest + 1, b.Waist, b.Z1 - 1, Mat.Phial, Tissue.Gear);
        // goggles: a strap round the head, and two rims with green glass, over the eyes or up on the hood
        int gy = look == 1 ? b.Crown - 2 : b.Neck + 4;
        s.Coat(Body, -hx - 1, gy, b.HeadZ - 1, hx + 1, gy + 1, hz1 + 1, Mat.LeatherDark, Tissue.Gear);
        s.Pair(Body, Body, -3, gy - (look == 1 ? 0 : 1), hz1, -1, gy + 1, hz1 + 1, Mat.Phial, Tissue.Gear);
        if (s.Detail)
        {
            int F = hz1 * 2, Y = gy * 2 - (look == 1 ? 0 : 2);
            // the brass rims round the glass
            foreach (int sg in new[] { 1, -1 })
            {
                int X(int x) => sg > 0 ? x : -1 - x;
                for (int y = Y - 1; y <= Y + 4; y++)
                    for (int x = -7; x <= -1; x++)
                        if (y == Y - 1 || y == Y + 4 || x == -7 || x == -1) s.FSet(Body, X(x), y, F, Mat.Brass, Tissue.Gear);
            }
            // the apron stained green where the acid splashed it, and its hem eaten
            int A = (b.Z1 + 1) * 2 - 1;
            for (int y = (b.Waist - 5) * 2; y < (b.Neck - 2) * 2; y++)
                for (int x = -6; x < 6; x++)
                    if (Dice(x, y, look, 131) < .06f) s.FPut(Body, x, y, A, Mat.Phial);
            for (int x = -6; x < 6; x++) if (Dice(x, look, 3, 132) < .4f) s.FDel(Body, x, (b.Waist - 5) * 2, A + 2);
        }
        FlaskInHand(s, b);
        return Done(s, f, b);
    }
}
