using System;

namespace VoxelTower.Core;

public static partial class Bodies
{
    /// <summary>
    /// A hill giant of the horde: three and a half metres, broad as a door, a belly on it; the skin weathered ruddy brown
    /// (or grey as a crag on the second), a shaggy mane down the back and a beard to the chest; a bear's pelt over the
    /// shoulders, a vest of raw hide open over the chest daubed with stripes of woad (bands of ochre on the second), a
    /// kilt of boar fur, the shins wound with hide, bare feet; a string of bones round its neck, an iron ring on one
    /// wrist; a crooked young trunk for a club.
    /// </summary>
    static BodyPlan HillGiant(int look)
    {
        var f = new BodyPlan { Chop = 1.8f, Carry = -.2f, Stride = 17 };
        bool grey = look == 1;
        byte skin = grey ? Mat.SkinGiantGrey : Mat.SkinGiant, hair = grey ? Mat.HairGrey : Mat.HairBrown, pelt = grey ? Mat.FurDeer : Mat.FurBoar;
        var b = new Build
        {
            Leg = 22, LegW = 6, LegD = 7, Gap = 1, Boot = 0, Torso = 21, Chest = 9, ChestD = 11, Head = 11, HeadW = 5, HeadD = 10, HeadZ = -4,
            Arm = 23, ArmW = 5, ArmD = 6, Hand = 7, FaceUp = 3, Skin = skin, Shirt = Mat.Hide, Sleeve = skin, Trousers = skin,
            Boots = skin, Belt = Mat.LeatherDark, Hair = hair, Brow = hair, HairStyle = 2,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz1 = b.HZ1, hy0 = b.Neck, up = b.FaceUp, eye = hy0 + 3 + up;
        // the vest open down the front over the bare chest, and the belly hanging out of it
        s.Paint(Body, -2, b.Waist + 1, b.Z1 - 1, 2, b.Neck - 1, b.Z1, skin);
        s.Box(Body, -b.Chest + 2, b.Waist + 2, b.Z1, b.Chest - 2, b.Waist + 11, b.Z1 + 1, skin);
        s.Box(Body, -b.Chest + 3, b.Waist + 3, b.Z1 + 1, b.Chest - 3, b.Waist + 9, b.Z1 + 2, skin);
        // the kilt of boar fur on a belt of dark leather
        Skirt(s, b, Mat.FurBoar, Tissue.Skin, 7);
        // the shins wound with strips of hide
        for (int y = 2; y < b.Leg - 8; y += 3)
        {
            s.Paint(LegR, -40, y, -40, 40, y + 1, 40, Mat.Hide);
            s.Paint(LegL, -40, y, -40, 40, y + 1, 40, Mat.Hide);
        }
        // the brow standing out over the eyes, a broad flat nose
        s.Box(Body, -hx, eye + 2, hz1, hx, eye + 3, hz1 + 1, skin);
        s.Box(Body, -2, hy0 + 2 + up, hz1, 2, hy0 + 4 + up, hz1 + 1, skin);
        // the beard along the jaw and down onto the chest
        s.Paint(Body, -hx, hy0, 0, hx, hy0 + 2 + up, hz1, hair);
        s.Box(Body, -4, hy0 - 6, b.FaceZ - 1, 4, hy0, hz1, hair);
        s.Box(Body, -3, hy0 - 8, b.Z1, 3, hy0 - 6, b.Z1 + 2, hair);
        // the bear's pelt: over the shoulders and the upper back, down over the tops of the arms, its paws knotted in front
        s.Box(Body, -b.Chest - 1, b.Neck - 2, b.Z0 - 1, b.Chest + 1, b.Neck + 1, b.Z1 + 1, pelt);
        s.Box(Body, -b.Chest, b.Neck - 9, b.Z0 - 1, b.Chest, b.Neck - 2, b.Z0, pelt);
        s.Box(Main, b.ArmX0 - 1, b.Neck - 5, b.ArmZ0 - 1, -b.Chest, b.Neck + 1, b.ArmZ1 + 1, pelt);
        s.Box(Off, b.Chest, b.Neck - 5, b.ArmZ0 - 1, -b.ArmX0 + 1, b.Neck + 1, b.ArmZ1 + 1, pelt);
        s.Box(Body, -2, b.Neck - 4, b.Z1 + 1, 2, b.Neck - 1, b.Z1 + 2, pelt);
        if (!grey)
        {
            // stripes of woad: across the cheeks under the eyes, down the chest, round the arms
            s.Paint(Body, -hx, eye - 1, hz1 - 1, hx, eye, hz1, Mat.Woad);
            for (int y = b.Waist + 4; y < b.Neck - 3; y += 3) s.Paint(Body, -2, y, b.Z1 - 1, 2, y + 1, b.Z1 + 2, Mat.Woad);
            foreach (int y in new[] { b.HandY + b.Hand + 3, b.HandY + b.Hand + 6 })
            {
                s.Paint(Main, b.ArmX0, y, b.ArmZ0, -b.Chest, y + 1, b.ArmZ1, Mat.Woad);
                s.Paint(Off, b.Chest, y, b.ArmZ0, -b.ArmX0, y + 1, b.ArmZ1, Mat.Woad);
            }
        }
        else
        {
            // bands of ochre across the brow and round the arms, a scar down the cheek and the scalp, the vest darker
            s.Paint(Body, -b.Chest, b.Waist + 1, b.Z0, b.Chest, b.Neck, b.Z1, Mat.LeatherDark);
            s.Paint(Body, -2, b.Waist + 1, b.Z1 - 1, 2, b.Neck - 1, b.Z1, skin);
            s.Paint(Body, -hx, eye + 1, hz1 - 1, hx, eye + 2, hz1 + 1, Mat.WarpaintRed);
            foreach (int y in new[] { b.HandY + b.Hand + 3, b.HandY + b.Hand + 6 })
            {
                s.Paint(Main, b.ArmX0, y, b.ArmZ0, -b.Chest, y + 1, b.ArmZ1, Mat.WarpaintRed);
                s.Paint(Off, b.Chest, y, b.ArmZ0, -b.ArmX0, y + 1, b.ArmZ1, Mat.WarpaintRed);
            }
            s.Paint(Body, hx - 2, hy0 + 2, hz1 - 1, hx - 1, b.Crown, hz1, Mat.Scar);
        }
        // a string of bones round the neck, hanging lowest in front
        for (int x = -b.Chest + 2; x < b.Chest - 2; x += 2)
            s.Dot(Body, x, b.Neck - 3 - (Math.Abs(x) < 4 ? 1 : 0), b.Z1, x % 4 == 0 ? Mat.Bone : Mat.Horn, Tissue.Gear);
        // an iron ring on the left wrist
        s.Paint(Off, -b.ArmX0 - b.ArmW, b.HandY + b.Hand, b.ArmZ0, -b.ArmX0, b.HandY + b.Hand + 2, b.ArmZ1, Mat.Iron, Tissue.Armor);
        HillGiantDetail(s, b, hair);
        Club(s, b);
        return Done(s, f, b);
    }

    /// <summary>A hill giant drawn finer: the beard ragged at its end, the fur of the kilt shaggy at its hem, the vest's edges laced.</summary>
    static void HillGiantDetail(Sculpt s, Build b, byte beard)
    {
        if (!s.Detail) return;
        for (int x = -6; x < 6; x++)
            for (int z = b.FaceZ * 2; z < b.HZ1 * 2; z++)
                if (Dice(x, z, 0, 171) < .5f) s.FSet(Body, x, (b.Neck - 2) * 2 - 1, z, beard);
        for (int x = -b.Chest * 2 - 2; x < b.Chest * 2 + 2; x++)
            foreach (int z in new[] { b.Z0 * 2 - 2, b.Z1 * 2 + 1 })
                if (Dice(x, z, 1, 172) < .45f) s.FDel(Body, x, (b.Waist - 7) * 2, z);
        int z1 = b.Z1 * 2;
        for (int y = (b.Waist + 3) * 2; y < (b.Neck - 2) * 2; y += 4)
            foreach (int x in new[] { -5, 4 }) s.FPut(Body, x, y, z1 - 1, Mat.Leather);
    }
}
