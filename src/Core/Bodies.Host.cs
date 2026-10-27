using System;

namespace VoxelTower.Core;

public static partial class Bodies
{
    /// <summary>
    /// A warrior of the heavenly host: a white robe to the knee under a breastplate of bright silver banded with gold down
    /// its middle and at its rim, gold greaves, golden hair long down the back or cropped, eyes alight; a disc of light
    /// behind the head; two white wings half open behind the shoulders, their upper edges gold; a long sword.
    /// </summary>
    static BodyPlan HostAngel(int look)
    {
        var f = new BodyPlan { Chop = .8f, Carry = -.3f, Stride = 9 };
        var b = new Build
        {
            Shirt = Mat.ClothWhite, Sleeve = Mat.ClothWhite, Trousers = Mat.ClothWhite, Boots = Mat.Brass, Belt = Mat.Brass, Skin = Mat.SkinPale,
            Hair = Mat.Brass, Brow = Mat.Brass, Eye = Mat.Crystal, Mouth = Mat.Lips, HairStyle = look == 0 ? 2 : 1,
        };
        var s = Humanoid(b, f);
        // the breastplate, its rim and the band down its middle gold
        s.Paint(Body, -b.Chest, b.Waist + 3, b.Z0, b.Chest, b.Neck, b.Z1, Mat.Quicksilver, Tissue.Armor);
        s.Paint(Body, -b.Chest, b.Waist + 3, b.Z0, b.Chest, b.Waist + 4, b.Z1, Mat.Brass, Tissue.Armor);
        s.Paint(Body, -1, b.Waist + 3, b.Z1 - 1, 1, b.Neck, b.Z1, Mat.Brass, Tissue.Armor);
        // the robe's hem at the knee, the greaves gold below it
        s.Paint(LegR, -40, 0, -40, 40, b.Leg / 2, 40, Mat.Brass, Tissue.Armor);
        s.Paint(LegL, -40, 0, -40, 40, b.Leg / 2, 40, Mat.Brass, Tissue.Armor);
        s.Paint(LegR, -40, b.Leg / 2, -40, 40, b.Leg / 2 + 1, 40, Mat.ClothWhite);
        s.Paint(LegL, -40, b.Leg / 2, -40, 40, b.Leg / 2 + 1, 40, Mat.ClothWhite);
        // the wings, from either side of the spine out and up to the bend, the feathers hanging under the bone, longer
        // the farther out; a little apart behind the back
        int back = b.Z0 - 1, top = b.Neck + 1;
        for (int sg = -1; sg <= 1; sg += 2)
            for (int i = 0; i < 12; i++)
            {
                int x = sg > 0 ? i : -i - 1;
                int up = i < 6 ? top + i / 2 : top + 3 - (i - 6) / 2;
                int low = b.Waist + 3 - i / 2 - (i > 8 ? 1 : 0);
                // the inner three against the back, the rest a half-voxel off it (the fourth on both, to join them)
                int z0 = i < 3 ? back : back - 1, z1 = i == 3 ? back + 1 : z0 + 1;
                for (int y = low; y <= up; y++)
                {
                    // the feathers' lower ends a little ragged, every third one shorter
                    if (y < low + 2 && i % 3 == 2) continue;
                    byte m = y == up ? Mat.Brass : y >= up - 1 ? Mat.ClothWhite : Mat.FeatherPale;
                    s.Box(Body, x, y, z0, x + 1, y + 1, z1, m);
                }
            }
        // the light behind the head: a disc, its rim brighter
        int cy = (b.Neck + b.Crown) / 2 + 1, hz = b.HeadZ - 1;
        for (int y = cy - 5; y <= cy + 5; y++)
            for (int x = -5; x <= 5; x++)
            {
                float d = MathF.Sqrt(x * x + (y - cy) * (y - cy));
                if (d > 5.3f) continue;
                s.Box(Body, x, y, hz, x + 1, y + 1, hz + 1, d > 4.2f ? Mat.EyeGlint : Mat.Brass);
            }
        Sword(s, b, 16, Mat.Brass, longsword: true);
        return Done(s, f, b);
    }
}
