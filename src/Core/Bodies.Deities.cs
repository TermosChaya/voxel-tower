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
    public static BodyPlan God(DeityKind k) => Gods.GetOrAdd(k, kind => kind switch { DeityKind.Archangel => Archangel(), DeityKind.Toad => Toad(), _ => Colossus() });

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
    /// The Archangel: tall, broad in the shoulder and narrow in the waist, in dark plate edged and banded with gold — a
    /// breastplate shaped to the chest over a belly of gold-edged lames, round pauldrons, vambraces and greaves, a skirt of
    /// plates to the knee with a gold panel down its front; the face bare and calm, the eyes alight, gold hair standing up
    /// off the head in points; a long sword of fire in the right hand. Its wings and the light behind its head are drawn on
    /// their own (<see cref="Wing"/>, <see cref="Halo"/>).
    /// </summary>
    static BodyPlan Archangel()
    {
        var f = new BodyPlan { Chop = 2f, Carry = .1f, Stride = 30 };
        byte plate = Mat.SteelDark, gold = Mat.Brass, glow = Mat.Crystal;
        var b = new Build
        {
            Leg = 42, LegW = 7, LegD = 8, Gap = 3, Boot = 10, Torso = 32, Chest = 13, ChestD = 11, Head = 12, HeadW = 6, HeadD = 11, HeadZ = -4,
            Arm = 34, ArmW = 6, ArmD = 7, Hand = 6, FaceUp = 1, Skin = Mat.SkinHuman, Shirt = plate, Sleeve = plate, Trousers = plate, Boots = plate,
            Belt = gold, Hair = gold, Brow = gold, HairStyle = 1, Eye = glow, Mouth = Mat.Lips,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, crown = b.Crown, T = b.Torso;
        // the trunk drawn in to the waist: three half-voxels off each side at the waist, none at the chest
        for (int y = b.Waist; y < b.Waist + T * 45 / 100; y++)
        {
            int c = (int)MathF.Round(3 * (1 - (y - b.Waist) / (T * .45f)));
            if (c <= 0) continue;
            s.Cut(Body, -b.Chest, y, b.Z0, -b.Chest + c, y + 1, b.Z1, both: true);
        }
        // the breastplate: the chest standing out either side of a gold ridge, gold at its upper edge
        int pec0 = b.Waist + T * 58 / 100, pec1 = b.Neck - 3;
        s.Box(Body, -b.Chest + 2, pec0, b.Z1, -1, pec1, b.Z1 + 2, plate);
        s.Box(Body, 1, pec0, b.Z1, b.Chest - 2, pec1, b.Z1 + 2, plate);
        s.Cut(Body, -b.Chest + 2, pec0, b.Z1 + 1, b.Chest - 2, pec0 + 1, b.Z1 + 2);
        s.Box(Body, -1, b.Waist + 4, b.Z1, 1, pec1, b.Z1 + 2, gold);
        s.Paint(Body, -b.Chest, b.Neck - 2, b.Z0, b.Chest, b.Neck, b.Z1 + 2, gold);
        // the belly in lames, each edged with gold
        for (int k = 0; k < 4; k++)
        {
            int y = b.Waist + 2 + k * 4;
            s.Box(Body, -b.Chest + 4, y, b.Z1, b.Chest - 4, y + 3, b.Z1 + 1, plate);
            s.Box(Body, -b.Chest + 4, y + 3, b.Z1, b.Chest - 4, y + 4, b.Z1 + 1, gold);
        }
        // the sun on the breast, alight at its heart
        s.Box(Body, -3, pec0 + 2, b.Z1 + 2, 3, pec1 - 1, b.Z1 + 3, gold);
        s.Box(Body, -5, pec0 + 4, b.Z1 + 2, 5, pec0 + 6, b.Z1 + 3, gold);
        s.Box(Body, -1, pec0 + 1, b.Z1 + 3, 1, pec1, b.Z1 + 4, gold);
        s.Box(Body, -1, pec0 + 4, b.Z1 + 3, 1, pec0 + 6, b.Z1 + 4, glow);
        // the gorget, gold, round the neck
        s.Box(Body, -hx, b.Neck - 1, b.Z0, hx, b.Neck + 2, b.Z1, gold);
        // the shoulder blades behind
        s.Box(Body, -b.Chest + 2, pec0, b.Z0 - 1, -2, pec1, b.Z0, plate);
        s.Box(Body, 2, pec0, b.Z0 - 1, b.Chest - 2, pec1, b.Z0, plate);
        // round pauldrons over the shoulders, in lames, gold-rimmed; on the arm, to go with it
        foreach (var (arm, sg) in new[] { (Main, -1), (Off, 1) })
        {
            float cx = sg * (b.Chest + b.ArmW / 2f), cz = (b.ArmZ0 + b.ArmZ1) / 2f;
            Dome(s, arm, cx, b.Neck - 6, cz, b.ArmW / 2f + 3, 7, b.ArmD / 2f + 3, plate);
            Dome(s, arm, cx, b.Neck - 9, cz, b.ArmW / 2f + 3.5f, 2, b.ArmD / 2f + 3.5f, gold);
            Dome(s, arm, cx, b.Neck - 3, cz, b.ArmW / 2f + 2, 4, b.ArmD / 2f + 2, plate);
        }
        // the forearm drawn in to the wrist, the vambrace gold-rimmed; the biceps before
        for (int y = b.HandY + b.Hand; y < b.HandY + b.Hand + 12; y++)
        {
            int c = y < b.HandY + b.Hand + 6 ? 1 : 0;
            if (c == 0) continue;
            s.Cut(Main, b.ArmX0, y, b.ArmZ0, b.ArmX0 + 1, y + 1, b.ArmZ1);
            s.Cut(Off, -b.ArmX0 - 1, y, b.ArmZ0, -b.ArmX0, y + 1, b.ArmZ1);
        }
        s.Pair(Main, Off, b.ArmX0, b.HandY + b.Hand + 12, b.ArmZ0 - 1, -b.Chest, b.HandY + b.Hand + 14, b.ArmZ1 + 1, gold, Tissue.Armor);
        s.Pair(Main, Off, b.ArmX0 + 1, b.Neck - 20, b.ArmZ1, -b.Chest - 1, b.Neck - 12, b.ArmZ1 + 1, plate, Tissue.Armor);
        // the legs: the knee guarded, the greave narrower below it, gold at its top; the sabaton pointed
        int lz0 = -(b.LegD / 2), lz1 = b.LegD - b.LegD / 2, knee = b.Leg / 2;
        s.Pair(LegR, LegL, b.LegX0 + 1, knee - 2, lz1, b.LegX1 - 1, knee + 3, lz1 + 2, plate, Tissue.Armor);
        s.Pair(LegR, LegL, b.LegX0 + 2, knee - 1, lz1 + 2, b.LegX1 - 2, knee + 2, lz1 + 3, gold, Tissue.Armor);
        for (int y = b.Boot; y < knee - 2; y++)
        {
            s.Cut(LegR, b.LegX0, y, lz0, b.LegX0 + 1, y + 1, lz1);
            s.Cut(LegL, -b.LegX0 - 1, y, lz0, -b.LegX0, y + 1, lz1);
        }
        s.Pair(LegR, LegL, b.LegX0, knee - 4, lz0, b.LegX1, knee - 3, lz1, gold, Tissue.Armor);
        s.Pair(LegR, LegL, b.LegX0 + 1, 0, lz1 + 1, b.LegX1 - 1, 2, lz1 + 3, plate, Tissue.Armor);
        // the skirt of plates to the knee, a gold hem, a gold panel down its front
        Skirt(s, b, plate, Tissue.Gear, 18);
        s.Paint(Body, -b.Chest - 1, b.Waist - 18, b.Z0 - 1, b.Chest + 1, b.Waist - 17, b.Z1 + 1, gold);
        s.Box(Body, -3, b.Waist - 20, b.Z1 + 1, 3, b.Waist + 1, b.Z1 + 2, gold, Tissue.Gear);
        s.Box(Body, -1, b.Waist - 16, b.Z1 + 2, 1, b.Waist - 4, b.Z1 + 3, Mat.ClothWhite, Tissue.Gear);
        // gold hair standing up off the head in points, swept back
        foreach (var (x, z, h) in new[] { (-5, 2, 5), (-3, 4, 8), (-1, 3, 10), (1, 3, 10), (3, 4, 8), (5, 2, 5), (-4, -1, 7), (0, -1, 9), (4, -1, 7), (-2, -4, 6), (2, -4, 6) })
            for (int k = 0; k < h; k++)
                s.Box(Body, x - (k < h / 2 ? 1 : 0), crown - 2 + k, hz0 + 5 + z - k / 2, x + 1, crown - 1 + k, hz0 + 7 + z - k / 2, gold);
        s.Box(Body, -hx, hy0 + 2, hz0 - 1, hx, crown, hz0 + 2, gold);
        FlameSword(s, b, 72);
        return Done(s, f, b);
    }

    /// <summary>The upper half of a ball of a part (half-voxels), row by row.</summary>
    static void Dome(Sculpt s, BodyPart p, float cx, float cy, float cz, float rx, float ry, float rz, byte m)
    {
        for (int y = (int)cy; y <= (int)(cy + ry); y++)
            for (int z = (int)(cz - rz); z <= (int)(cz + rz); z++)
            {
                float a = (y - cy) / ry, c = (z + .5f - cz) / rz, w = 1 - a * a - c * c;
                if (w <= 0) continue;
                float hw = rx * MathF.Sqrt(w);
                if (hw < .5f) continue;
                s.Box(p, (int)MathF.Round(cx - hw), y, z, (int)MathF.Round(cx + hw), y + 1, z + 1, m);
            }
    }

    /// <summary>A whole ball of a part (half-voxels), row by row.</summary>
    static void Blob(Sculpt s, BodyPart p, float cx, float cy, float cz, float rx, float ry, float rz, byte m)
    {
        for (int y = (int)(cy - ry); y <= (int)(cy + ry); y++)
            for (int z = (int)(cz - rz); z <= (int)(cz + rz); z++)
            {
                float a = (y + .5f - cy) / ry, c = (z + .5f - cz) / rz, w = 1 - a * a - c * c;
                if (w <= 0) continue;
                float hw = rx * MathF.Sqrt(w);
                if (hw < .5f) continue;
                s.Box(p, (int)MathF.Round(cx - hw), y, z, (int)MathF.Round(cx + hw), y + 1, z + 1, m);
            }
    }

    /// <summary>
    /// The sword of fire, as tall as most of the archangel: a gold pommel over the fist, the grip through it, a broad gold
    /// guard under it, and the blade going on from the fist the way the forearm goes — so that the arm and the sword are
    /// one line — broad and long, all fire, brightest down its middle.
    /// </summary>
    static void FlameSword(Sculpt s, Build b, int len)
    {
        var g = InFist(s, b, out int x, out _, out _);
        int q = 4, cz = (b.ArmZ0 + b.ArmZ1) * q / 2, top = (b.HandY + b.Hand) * q, low = b.HandY * q;
        // the grip through the fist, the pommel over it
        g.Fill(x, low - 1, cz - 1, x + 2, top + 2, cz + 1, Mat.LeatherDark);
        g.Fill(x - 1, top + 2, cz - 2, x + 3, top + 6, cz + 2, Mat.Brass);
        // the guard, broad, its ends turned down the blade
        for (int dz = -16; dz < 16; dz++)
        {
            int bend = Math.Max(0, Math.Abs(dz) - 12);
            g.Fill(x - 1, low - 4 - bend, cz + dz, x + 3, low - 1 - bend, cz + dz + 1, Mat.Brass);
        }
        int y0 = low - 4, y1 = y0 - len * q;
        for (int y = y0; y > y1; y--)
        {
            float t = (y0 - y) / (float)(y0 - y1);
            float w = t < .85f ? 7.5f - t * 2.5f : 5.4f * (1 - (t - .85f) / .15f);
            for (int dz = -9; dz < 9; dz++)
            {
                float a = MathF.Abs(dz + .5f);
                if (a > w) continue;
                // the flame flickers out from the edges, now and then
                if (a + 1.5f > w && Dice(x, y, dz, 401) < .35f) continue;
                byte m = a < 1.6f ? Mat.EyeGlint : Mat.Flame;
                g.Dot(x, y, cz + dz, m);
                g.Dot(x + 1, y, cz + dz, m);
            }
        }
        g.Tip = Half(g, x + 1, y1, cz);
    }

    /// <summary>
    /// One of the archangel's wings, in half-voxels from its root (the left one: it goes out along +x and up; the right
    /// is its mirror). As a bird's: its bones from the root up and out to the bend, out to the wrist, and out and a little
    /// down to the tip, plated in gold along their middles and white at the joints; the feathers hanging under the bones
    /// in long white blades, the longer the farther out, each a little apart from the next.
    /// </summary>
    public static PartGrid Wing()
    {
        // the bones: root → bend → wrist → tip
        var pts = new (float x, float y)[] { (0, 0), (16, 26), (46, 38), (74, 32) };
        const int W = 78, Hi = 44, Lo = -40, D = 4;
        var g = new PartGrid { X0 = 0, Y0 = Lo, Z0 = -D / 2, SX = W, SY = Hi - Lo, SZ = D };
        g.M = new byte[g.SX * g.SY * g.SZ]; g.T = new byte[g.M.Length];
        void Put(int x, int y, int z, byte m)
        {
            if (!g.In(x, y, z)) return;
            int i = g.Index(x, y, z);
            if (g.M[i] == 0) g.Count++;
            g.M[i] = m; g.T[i] = (byte)Tissue.Skin;
        }
        for (int x = 0; x < W - 2; x++)
        {
            // where the bone is over this column, and which bone, and how far along it
            int seg = x < pts[1].x ? 0 : x < pts[2].x ? 1 : 2;
            var (ax, ay) = pts[seg]; var (bx, by) = pts[seg + 1];
            float u = Math.Clamp((x - ax) / (bx - ax), 0, 1), top = ay + (by - ay) * u, t = x / (float)(W - 1);
            int bone = seg == 0 ? 5 : seg == 1 ? 4 : 3;
            bool plated = seg < 2 && u > .18f && u < .82f;
            int thick = seg == 0 ? 2 : 1;
            int feather = (int)(8 + 34 * t * t + 6 * t);
            for (int y = (int)top - bone - feather; y <= (int)top; y++)
            {
                bool isBone = y > top - bone;
                // the feathers a little apart: every fourth column left open in their lower half
                if (!isBone && x % 4 == 3 && y < top - bone - feather / 2) continue;
                // the tip of each feather drawn to a point
                if (!isBone && y < top - bone - feather + 2 && x % 4 is 0 or 2) continue;
                byte m = isBone ? (plated && y > top - bone + 1 ? Mat.Brass : Mat.FeatherPale) : (y > top - bone - 4 ? Mat.ClothWhite : Mat.FeatherPale);
                for (int z = -thick; z <= thick - (isBone ? 0 : 1); z++) Put(x, y, z, m);
            }
        }
        return g;
    }

    /// <summary>
    /// The light behind the archangel's head, in half-voxels about its middle, upright, facing forward: a ring and rays
    /// out of it, long and short by turns, the two longest straight up.
    /// </summary>
    public static PartGrid Halo()
    {
        const int R = 9, Far = 22;
        var g = new PartGrid { X0 = -Far, Y0 = -Far, Z0 = 0, SX = 2 * Far + 1, SY = 2 * Far + 1, SZ = 1 };
        g.M = new byte[g.SX * g.SY * g.SZ]; g.T = new byte[g.M.Length];
        void Put(int x, int y)
        {
            if (!g.In(x, y, 0)) return;
            int i = g.Index(x, y, 0);
            if (g.M[i] == 0) g.Count++;
            g.M[i] = Mat.EyeGlint; g.T[i] = (byte)Tissue.Skin;
        }
        for (int y = -Far; y <= Far; y++)
            for (int x = -Far; x <= Far; x++)
            {
                float d = MathF.Sqrt(x * x + y * y);
                if (MathF.Abs(d - R) < .8f) Put(x, y);
            }
        for (int k = 0; k < 16; k++)
        {
            float a = k * MathF.Tau / 16 + MathF.PI / 2;
            int len = k == 0 ? 13 : k % 2 == 0 ? 8 : 4;
            for (int r = R + 1; r <= R + len; r++) Put((int)MathF.Round(MathF.Cos(a) * r), (int)MathF.Round(MathF.Sin(a) * r));
        }
        // the two long rays up, side by side
        for (int r = R + 1; r <= R + 13; r++) { Put(-2, r); Put(2, r); }
        return g;
    }

    /// <summary>
    /// The Great Toad, sitting: a broad round body and a wide flat head, yellow-green, striped darker down its flanks and
    /// its legs, its belly pale; a mouth right round the front of its head; great gold eyes on the sides of its head,
    /// a dark bar across each; short forelegs straight down to wide pale fingers, the hind legs folded big at its sides
    /// down to long pale toes. Its back is a bank of the swamp it came out of — moss and grass on earth, bare earth here
    /// and there, ferns and toadstools growing out of it — and the horde's shamans have stuck their totems in it, bone
    /// and horn. Its throat's sac, glowing, is drawn on its own.
    /// </summary>
    static BodyPlan Toad()
    {
        var f = new BodyPlan { Chop = 1.6f, Stride = 40 };
        byte skin = Mat.SkinGoblinPale, dark = Mat.SkinGoblin, belly = Mat.ScaleBelly, toe = Mat.Belly;
        var s = new Sculpt();
        // the forelegs: from the shoulder straight down, a little out; wide pale fingers splayed on the ground
        foreach (var (leg, sg) in new[] { (Main, -1), (Off, 1) })
        {
            int x0 = sg < 0 ? -20 : 13;
            s.Box(leg, x0, 0, 18, x0 + 7, 20, 25, skin);
            for (int y = 2; y < 20; y += 3) s.Paint(leg, x0, y, 18, x0 + 7, y + 1, 25, dark);
            for (int k = 0; k < 4; k++)
            {
                int fx = x0 - 2 + k * 3;
                s.Box(leg, fx, 0, 23, fx + 2, 2, 31 - Math.Abs(k - 1) * 2, toe);
            }
            Joint(f, leg, (x0 + 3.5f) / H, 20 / H, 21.5f / H);
        }
        // the hind legs folded at its sides: the thigh a great round, the foot along the ground forward, long toes
        foreach (var (leg, sg) in new[] { (LegR, -1), (LegL, 1) })
        {
            float cx = sg * 25;
            Blob(s, leg, cx, 13, -14, 8, 12, 17, skin);
            for (int y = 4; y < 26; y += 3) s.Paint(leg, (int)cx - 9, y, -32, (int)cx + 9, y + 1, 4, dark);
            s.Box(leg, (int)cx - 5, 0, -26, (int)cx + 5, 4, 6, skin);
            for (int k = 0; k < 4; k++)
            {
                int fx = (int)cx - 5 + k * 3;
                s.Box(leg, fx, 0, 6, fx + 2, 2, 14 - Math.Abs(k - 2) * 2, toe);
            }
            Joint(f, leg, cx / H, 20 / H, -14 / H);
        }
        // the body, round, the back up; the belly pale
        Blob(s, Body, 0, 26, -8, 25, 17, 29, skin);
        Blob(s, Body, 0, 20, 2, 21, 10, 22, belly);
        // the head: wide and flat-topped before the body
        Blob(s, Body, 0, 30, 18, 23, 12, 17, skin);
        // the mouth right round the front of the head, a lip over it
        for (int z = 10; z < 36; z++)
            for (int x = -24; x <= 24; x++)
            {
                float e = (x / 23f) * (x / 23f) + ((z - 18) / 17f) * ((z - 18) / 17f);
                if (e > .85f && e < 1.15f && z > 14) s.Paint(Body, x, 25, z, x + 1, 27, z + 1, Mat.Maw);
            }
        // the flanks striped darker
        for (int z = -34; z < 14; z += 4) s.Paint(Body, -26, 12, z, 26, 32, z + 1, dark);
        // the eyes: on the sides of the head toward its top, gold, a dark bar across each
        foreach (int sg in new[] { 1, -1 })
        {
            float ex = sg * 17;
            Blob(s, Body, ex, 38, 20, 6, 5, 6, skin);
            int x0 = sg > 0 ? (int)ex + 3 : (int)ex - 5;
            s.Box(Body, x0, 35, 17, x0 + 3, 42, 25, Mat.Sulphur);
            s.Box(Body, x0 + (sg > 0 ? 2 : 0), 38, 19, x0 + (sg > 0 ? 3 : 1), 40, 24, Mat.Hair);
            s.Box(Body, (int)ex - 4, 42, 18, (int)ex + 4, 43, 24, dark);
        }
        // its back a bank of the swamp: earth under moss and grass, bare earth in places, ferns and toadstools
        for (int k = 0; k < 220; k++)
        {
            int x = -22 + (int)(Dice(k, 1, 2, 501) * 44), z = -32 + (int)(Dice(k, 3, 4, 502) * 52);
            float sx = x / 25f, sz = (z + 8) / 29f, w = 1 - sx * sx - sz * sz;
            if (w <= 0) continue;
            int top = (int)(26 + 17 * MathF.Sqrt(w));
            float r = Dice(k, 5, 6, 503);
            byte m = r < .45f ? Mat.Moss : r < .75f ? Mat.GrassLush : r < .88f ? Mat.Grass : Mat.Soil;
            s.Box(Body, x, top - 2, z, x + 3, top + (r < .3f ? 2 : 1), z + 3, m);
        }
        for (int k = 0; k < 18; k++)
        {
            int x = -14 + (int)(Dice(k, 7, 8, 504) * 28), z = -26 + (int)(Dice(k, 9, 1, 505) * 34);
            float sx = x / 25f, sz = (z + 8) / 29f, w = 1 - sx * sx - sz * sz;
            if (w <= 0) continue;
            int top = (int)(26 + 17 * MathF.Sqrt(w)) + 1;
            if (k % 3 == 0) { s.Box(Body, x, top, z, x + 1, top + 2, z + 1, Mat.Snag); s.Box(Body, x - 1, top + 2, z - 1, x + 2, top + 3, z + 2, Mat.Mushroom); }
            else s.Box(Body, x, top, z, x + 2, top + 3 + k % 3, z + 1, Mat.Fern);
        }
        // the shamans' totems stuck in its back: a pole of bone with a skull, a pair of horns
        foreach (var (x, z) in new[] { (-9, -18), (8, -6), (-2, -28) })
        {
            float sx = x / 25f, sz = (z + 8) / 29f;
            int top = (int)(26 + 17 * MathF.Sqrt(Math.Max(0, 1 - sx * sx - sz * sz)));
            s.Box(Body, x, top, z, x + 1, top + 8, z + 1, Mat.Bone, Tissue.Gear);
            s.Box(Body, x - 1, top + 8, z - 1, x + 2, top + 11, z + 2, Mat.Bone, Tissue.Gear);
            s.Box(Body, x - 1, top + 9, z + 1, x, top + 10, z + 2, Mat.Hair, Tissue.Gear);
            s.Box(Body, x - 3, top + 10, z, x - 1, top + 13, z + 1, Mat.Horn, Tissue.Gear);
            s.Box(Body, x + 2, top + 10, z, x + 4, top + 13, z + 1, Mat.Horn, Tissue.Gear);
            s.Box(Body, x - 1, top + 4, z, x + 2, top + 5, z + 1, Mat.Hide, Tissue.Gear);
        }
        f.Beast = true;
        Joint(f, Body, 0, 14 / H, 0);
        f.HeadY0 = 22; f.HeadY1 = 44;
        f.NeckZ = 8; f.NeckY = 20;
        return s.Into(f, 14, 40, 25);
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
