using System;
using System.Collections.Concurrent;

namespace VoxelTower.Core;

public static partial class Bodies
{
    static readonly ConcurrentDictionary<DeityKind, BodyPlan> Gods = new();

    /// <summary>
    /// A god's body, sculpted as a body of the valley is and drawn many times its size (its height over the
    /// <see cref="BodyPlan.Top"/> of this). Sculpted once, on whatever thread first wants it.
    /// </summary>
    public static BodyPlan God(DeityKind k) => Gods.GetOrAdd(k, kind => kind == DeityKind.Colossus ? Colossus() : Devourer());

    /// <summary>
    /// The Colossus: a hulk of dark rock stooped under shoulders heaped with crags, the head sunk between them, long arms
    /// hanging to the knees. A crown of crystal grows out of its head and its shoulders; light from inside the earth
    /// shows through the cracks across its chest and arms, in its eyes and its mouth; moss lies on its shoulders and its
    /// back, roots hang off it, clods of earth cling to its legs. Broken iron is round its wrists and its ankles, and the
    /// ends of the chains that held it hang from them.
    /// </summary>
    static BodyPlan Colossus()
    {
        var f = new BodyPlan { Chop = 2.4f, Carry = -.3f, Stride = 30 };
        byte rock = Mat.StoneDark, crag = Mat.Crag, glow = Mat.Ember;
        var b = new Build
        {
            Leg = 40, LegW = 10, LegD = 12, Gap = 4, Boot = 0, Torso = 36, Chest = 18, ChestD = 18, Head = 16, HeadW = 9, HeadD = 13, HeadZ = 0,
            Arm = 46, ArmW = 8, ArmD = 10, Hand = 10, FaceUp = 2, Skin = rock, Shirt = rock, Sleeve = rock, Trousers = rock, Boots = rock, Belt = Mat.Rock,
            Hair = Mat.Rock, Brow = crag, HairStyle = 3, Eye = glow, Mouth = glow,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz1 = b.HZ1, hy0 = b.Neck, eye = hy0 + 3 + b.FaceUp;
        // the belt and its buckle are no part of it: rock where they were
        s.Paint(Body, -b.Chest, b.Waist, b.Z0, b.Chest, b.Waist + 1, b.Z1 + 1, rock);
        // the shoulders heaped up with crags round the sunk head, and the hump of the back
        s.Pair(Body, Body, -b.Chest - 3, b.Neck - 7, b.Z0 - 1, -b.Chest + 6, b.Neck + 3, b.Z1 + 1, crag);
        s.Pair(Body, Body, -b.Chest - 1, b.Neck + 3, b.Z0 + 2, -b.Chest + 4, b.Neck + 6, b.Z1 - 3, Mat.Rock);
        s.Box(Body, -b.Chest + 4, b.Neck - 14, b.Z0 - 4, b.Chest - 4, b.Neck + 3, b.Z0, Mat.Rock);
        s.Round(Body, -b.Chest + 4, b.Neck - 14, b.Z0 - 4, b.Chest - 4, b.Neck + 3, b.Z0);
        // the brow a ledge over the eyes, the eyes wide and burning, the mouth a long crack of light
        s.Box(Body, -hx, eye + 2, hz1, hx, eye + 4, hz1 + 2, crag);
        s.Pair(Body, Body, -hx + 1, eye, b.FaceZ, -1, eye + 2, hz1 + 1, glow);
        s.Box(Body, -hx + 2, hy0 + 2, b.FaceZ, hx - 2, hy0 + 3, hz1 + 1, glow);
        s.Box(Body, -hx + 1, hy0, hz1, hx - 1, hy0 + 2, hz1 + 2, crag);
        // the crown: shards of crystal out of the head, the tallest at its back, and out of the crags of the shoulders
        int crown = b.Crown;
        foreach (var (x, z, h) in new[] { (-6, -2, 6), (-3, -5, 9), (0, -6, 12), (3, -5, 9), (6, -2, 6), (-5, 2, 4), (5, 2, 4), (0, -1, 7) })
        {
            // (dark rock at the root, the crystal showing only toward the tip)
            s.Box(Body, x, crown - 2, b.HeadZ + 6 + z, x + 1, crown + h / 2, b.HeadZ + 7 + z, crag, Tissue.Armor);
            s.Box(Body, x, crown + h / 2, b.HeadZ + 6 + z, x + 1, crown + h, b.HeadZ + 7 + z, Mat.Crystal, Tissue.Armor);
        }
        foreach (int sg in new[] { 1, -1 })
            foreach (var (x, z, h) in new[] { (-b.Chest - 1, -4, 8), (-b.Chest + 3, -7, 11), (-b.Chest + 1, 2, 5) })
            {
                int x0 = sg > 0 ? x : -x - 2;
                s.Box(Body, x0, b.Neck + 2, z, x0 + 2, b.Neck + 4 + h / 2, z + 2, crag, Tissue.Armor);
                s.Box(Body, x0, b.Neck + 4 + h / 2, z, x0 + 1, b.Neck + 4 + h, z + 1, Mat.Crystal, Tissue.Armor);
            }
        // the cracks lit from inside: across the chest from each shoulder down to the middle, and down the arms
        for (int k = 0; k < 16; k++)
        {
            int y = b.Neck - 4 - k, x = -b.Chest + 3 + k;
            s.Paint(Body, x, y, b.Z1 - 1, x + 2, y + 1, b.Z1, glow);
            s.Paint(Body, -x - 2, y - 2, b.Z1 - 1, -x, y - 1, b.Z1, glow);
        }
        s.Paint(Body, -1, b.Waist + 4, b.Z1 - 1, 1, b.Neck - 18, b.Z1, glow);
        for (int y = b.HandY + b.Hand + 4; y < b.Neck - 4; y += 2)
        {
            int dx = (y / 2) % 3 - 1;
            s.Paint(Main, b.ArmX0 + 3 + dx, y, b.ArmZ1 - 1, b.ArmX0 + 4 + dx, y + 2, b.ArmZ1, glow);
            s.Paint(Off, -b.ArmX0 - 4 - dx, y, b.ArmZ1 - 1, -b.ArmX0 - 3 - dx, y + 2, b.ArmZ1, glow);
        }
        // lumps of crag standing out of it everywhere, so its edge against the sky is broken
        Crags(s, Body, -b.Chest, b.Waist, b.Z0, b.Chest, b.Neck - 6, b.Z1, crag, 31);
        Crags(s, Main, b.ArmX0, b.HandY + b.Hand, b.ArmZ0, -b.Chest, b.Neck - 6, b.ArmZ1, crag, 37);
        Crags(s, Off, b.Chest, b.HandY + b.Hand, b.ArmZ0, -b.ArmX0, b.Neck - 6, b.ArmZ1, crag, 41);
        Crags(s, LegR, b.LegX0, 6, -(b.LegD / 2), b.LegX1, b.Leg - 2, b.LegD - b.LegD / 2, crag, 43);
        Crags(s, LegL, -b.LegX1, 6, -(b.LegD / 2), -b.LegX0, b.Leg - 2, b.LegD - b.LegD / 2, crag, 47);
        // moss on the shoulders and down the back
        s.Paint(Body, -b.Chest - 3, b.Neck + 1, b.Z0 - 1, b.Chest + 3, b.Neck + 3, b.Z1 + 1, Mat.Moss);
        s.Paint(Body, -b.Chest + 5, b.Neck - 10, b.Z0 - 4, b.Chest - 5, b.Neck + 3, b.Z0 - 3, Mat.Moss);
        // clods of earth on the legs and the feet
        s.Paint(LegR, b.LegX0, 0, -(b.LegD / 2), b.LegX1, 7, b.LegD - b.LegD / 2 + 1, Mat.Soil);
        s.Paint(LegL, -b.LegX1, 0, -(b.LegD / 2), -b.LegX0, 7, b.LegD - b.LegD / 2 + 1, Mat.Soil);
        // the broken irons round the wrists and the ankles, rust on them, the chains' ends hanging
        int wy = b.HandY + b.Hand;
        s.Pair(Main, Off, b.ArmX0 - 1, wy, b.ArmZ0 - 1, -b.Chest + 1, wy + 4, b.ArmZ1 + 1, Mat.Iron, Tissue.Armor);
        s.Pair(Main, Off, b.ArmX0 - 1, wy + 1, b.ArmZ0 - 1, b.ArmX0 + 3, wy + 3, b.ArmZ1 + 1, Mat.Rust, Tissue.Armor);
        int lz0 = -(b.LegD / 2), lz1 = b.LegD - b.LegD / 2;
        s.Pair(LegR, LegL, b.LegX0 - 1, 4, lz0 - 1, b.LegX1 + 1, 8, lz1 + 1, Mat.Iron, Tissue.Armor);
        s.Pair(LegR, LegL, b.LegX0 - 1, 5, lz0 - 1, b.LegX0 + 2, 7, lz1 + 1, Mat.Rust, Tissue.Armor);
        for (int k = 0; k < 7; k++)
        {
            // links hanging off the outer side of each wrist, each a ring turned from the one before
            int y = wy - 2 - k * 2;
            s.Pair(Main, Off, b.ArmX0 - 2, y, (k % 2 == 0 ? -1 : 0), b.ArmX0 - 1, y + 2, (k % 2 == 0 ? 1 : 0) + 1, Mat.Iron, Tissue.Armor);
        }
        for (int k = 0; k < 4; k++)
        {
            int z = lz1 + 1 + k * 2;
            s.Pair(LegR, LegL, b.LegX0 - 1, 0, z, b.LegX0 + 1, 2, z + 2, Mat.Iron, Tissue.Armor);
        }
        // roots hanging off the arms and the trunk
        for (int k = 0; k < 14; k++)
        {
            float u = Dice(k, 3, 9, 211);
            int x = -b.Chest + 2 + (int)(u * (2 * b.Chest - 4)), y = b.Waist + 6 + (int)(Dice(k, 5, 1, 212) * (b.Torso - 14)), len = 4 + (int)(Dice(k, 7, 3, 213) * 8);
            int z = k % 2 == 0 ? b.Z1 : b.Z0 - 1;
            s.Box(Body, x, y - len, z, x + 1, y, z + 1, Mat.PineBark, Tissue.Gear);
        }
        foreach (var arm in new[] { Main, Off })
            for (int k = 0; k < 5; k++)
            {
                int x = arm == Main ? b.ArmX0 - 1 : -b.ArmX0, y = b.HandY + b.Hand + 6 + k * 5, len = 3 + k % 3 * 2;
                s.Box(arm, x, y - len, b.ArmZ0 + 2 + k % 4, x + 1, y, b.ArmZ0 + 3 + k % 4, Mat.PineBark, Tissue.Gear);
            }
        f.Shield = false;
        return Done(s, f, b);
    }

    /// <summary>
    /// The Devourer: a hunched thing of black glass and char, the head low and thrust forward under two great horns,
    /// a mouth full of fire, the chest split open on the fire inside it; a ridge of spikes down its back, arms longer than
    /// its legs that go down on their knuckles, claws of bone.
    /// </summary>
    static BodyPlan Devourer()
    {
        var f = new BodyPlan { Chop = 2.2f, Carry = -.3f, Stride = 30 };
        byte glass = Mat.Basalt, burnt = Mat.Char, fire = Mat.Lava;
        var b = new Build
        {
            Leg = 28, LegW = 11, LegD = 12, Gap = 4, Boot = 0, Torso = 40, Chest = 21, ChestD = 22, Head = 14, HeadW = 8, HeadD = 15, HeadZ = 0,
            Arm = 50, ArmW = 9, ArmD = 10, Hand = 11, FaceUp = 1, Skin = glass, Shirt = glass, Sleeve = glass, Trousers = burnt, Boots = burnt, Belt = burnt,
            Hair = burnt, Brow = glass, HairStyle = 3, Eye = fire, Mouth = fire,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz1 = b.HZ1, hy0 = b.Neck, eye = hy0 + 3 + b.FaceUp;
        s.Paint(Body, -b.Chest, b.Waist, b.Z0, b.Chest, b.Waist + 1, b.Z1 + 1, burnt);
        // the shoulders thrown up high and forward, the head low between them
        s.Pair(Body, Body, -b.Chest - 3, b.Neck - 8, b.Z0, -b.Chest + 8, b.Neck + 6, b.Z1 + 3, glass);
        // the horns: out from the sides of the head, then up and forward, thick at the root and drawn to a point
        foreach (int sg in new[] { 1, -1 })
            for (int k = 0; k < 22; k++)
            {
                float t = k / 21f;
                int w = 4 - (int)(t * 3.2f), out_ = k < 9 ? k : 9 + (k - 9) / 3, x = sg > 0 ? -hx - out_ - w : hx + out_;
                int y = b.Crown - 5 + (k < 7 ? k / 2 : 3 + (k - 7)), z = b.HeadZ + 5 + (k > 12 ? (k - 12) * 2 / 3 : 0);
                s.Box(Body, x, y, z, x + w, y + w, z + w, k > 17 ? Mat.Bone : Mat.Horn, Tissue.Armor);
            }
        // the mouth a wide maw of fire under a jaw of glass, the eyes slits of fire, the brow heavy
        s.Box(Body, -hx + 1, hy0, hz1 - 1, hx - 1, hy0 + 4, hz1 + 1, fire);
        s.Box(Body, -hx, hy0 - 1, hz1 - 3, hx, hy0 + 1, hz1 + 2, glass);
        for (int x = -hx + 2; x < hx - 1; x += 2) { s.Box(Body, x, hy0 + 3, hz1, x + 1, hy0 + 4, hz1 + 2, Mat.Bone, Tissue.Armor); s.Box(Body, x, hy0 + 1, hz1 + 1, x + 1, hy0 + 2, hz1 + 2, Mat.Bone, Tissue.Armor); }
        s.Pair(Body, Body, -hx + 1, eye + 1, b.FaceZ, -2, eye + 2, hz1 + 1, fire);
        s.Box(Body, -hx, eye + 2, hz1, hx, eye + 4, hz1 + 2, glass);
        // the chest split open on the fire inside, the fire showing at the joints
        s.Box(Body, -5, b.Waist + 6, b.Z1 - 1, 5, b.Neck - 8, b.Z1 + 1, fire);
        s.Box(Body, -7, b.Waist + 8, b.Z1, -5, b.Neck - 10, b.Z1 + 2, glass);
        s.Box(Body, 5, b.Waist + 8, b.Z1, 7, b.Neck - 10, b.Z1 + 2, glass);
        for (int y = b.Waist + 9; y < b.Neck - 10; y += 4) s.Box(Body, -5, y, b.Z1 + 1, 5, y + 1, b.Z1 + 2, Mat.Bone, Tissue.Armor);
        s.Paint(Main, b.ArmX0, b.Neck - 3, b.ArmZ0, -b.Chest, b.Neck - 1, b.ArmZ1, fire);
        s.Paint(Off, b.Chest, b.Neck - 3, b.ArmZ0, -b.ArmX0, b.Neck - 1, b.ArmZ1, fire);
        s.Paint(LegR, b.LegX0, b.Leg / 2, -(b.LegD / 2), b.LegX1, b.Leg / 2 + 2, b.LegD - b.LegD / 2, fire);
        s.Paint(LegL, -b.LegX1, b.Leg / 2, -(b.LegD / 2), -b.LegX0, b.Leg / 2 + 2, b.LegD - b.LegD / 2, fire);
        // the spikes down its back, the longest at the shoulders
        for (int k = 0; k < 9; k++)
        {
            int y = b.Neck + 4 - k * 4, h = 10 - k;
            s.Box(Body, -1, y, b.Z0 - h, 1, y + 2, b.Z0, k % 2 == 0 ? Mat.Bone : glass, Tissue.Armor);
            s.Box(Body, -1, y + 1, b.Z0 - h - 2, 0, y + 2, b.Z0 - h, Mat.Bone, Tissue.Armor);
        }
        Crags(s, Body, -b.Chest, b.Waist, b.Z0, b.Chest, b.Neck - 6, b.Z1, glass, 53);
        Crags(s, Main, b.ArmX0, b.HandY + b.Hand, b.ArmZ0, -b.Chest, b.Neck - 6, b.ArmZ1, burnt, 59);
        Crags(s, Off, b.Chest, b.HandY + b.Hand, b.ArmZ0, -b.ArmX0, b.Neck - 6, b.ArmZ1, burnt, 61);
        Talons(s, b, Mat.Bone, 9);
        return Done(s, f, b);
    }

    /// <summary>
    /// Lumps standing out of the faces of a box of a part, a half-voxel or two proud, placed by the dice: the broken
    /// face of a thing made of rock.
    /// </summary>
    static void Crags(Sculpt s, BodyPart p, int x0, int y0, int z0, int x1, int y1, int z1, byte m, int salt)
    {
        for (int y = y0; y < y1; y += 3)
            for (int k = 0; k < 6; k++)
            {
                float d = Dice(k, y, salt, salt * 7);
                int w = 1 + (int)(Dice(y, k, salt, 3) * 3), h = 2 + (int)(Dice(k, salt, y, 5) * 3);
                switch (k % 4)
                {
                    case 0: { int x = x0 + (int)(d * (x1 - x0 - w)); s.Box(p, x, y, z1, x + w, y + h, z1 + 1, m); break; }
                    case 1: { int x = x0 + (int)(d * (x1 - x0 - w)); s.Box(p, x, y, z0 - 1, x + w, y + h, z0, m); break; }
                    case 2: { int z = z0 + (int)(d * (z1 - z0 - w)); s.Box(p, x0 - 1, y, z, x0, y + h, z + w, m); break; }
                    default: { int z = z0 + (int)(d * (z1 - z0 - w)); s.Box(p, x1, y, z, x1 + 1, y + h, z + w, m); break; }
                }
            }
    }
}
