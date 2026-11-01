using System;
using System.Collections.Concurrent;

namespace VoxelTower.Core;

public static partial class Bodies
{
    static readonly ConcurrentDictionary<DeityKind, Lazy<BodyPlan>> Gods = new();

    /// <summary>
    /// A god's body, sculpted as a body of the valley is and drawn many times its size (its height over the
    /// <see cref="BodyPlan.Top"/> of this). Sculpted once, on whatever thread first wants it (another wanting it
    /// meanwhile waits for it rather than sculpting it again).
    /// </summary>
    public static BodyPlan God(DeityKind k) => Gods.GetOrAdd(k, kind => new Lazy<BodyPlan>(() => kind switch
    {
        // (the lesser angels are the archangel's body drawn smaller; the destroyer's dark)
        DeityKind.Archangel or DeityKind.Guardian or DeityKind.Cherub => Archangel(), DeityKind.Destroyer => Archangel(dark: true),
        DeityKind.Toad => Toad(), _ => Colossus(),
    })).Value;

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
    /// breastplate shaped to the chest over a belly of gold-edged lames, and the rest of its plate laid piece by piece over
    /// its shaped body (<see cref="Harness"/>); the face bare and calm, the eyes alight, gold hair standing up off the head
    /// in points; a long sword of fire in the right hand. Its wings and the light behind its head are drawn on
    /// their own (<see cref="Wing"/>, <see cref="Halo"/>). Dark, it is the destroying angel: black plate banded with
    /// iron, black hair, eyes like embers.
    /// </summary>
    static BodyPlan Archangel(bool dark = false)
    {
        var f = new BodyPlan { Chop = 2f, Carry = .1f, Stride = 30 };
        byte plate = dark ? Mat.Chitin : Mat.SteelDark, gold = dark ? Mat.SteelDark : Mat.Brass, glow = dark ? Mat.Ember : Mat.Crystal;
        var b = new Build
        {
            Leg = 42, LegW = 7, LegD = 8, Gap = 3, Boot = 10, Torso = 32, Chest = 13, ChestD = 11, Head = 12, HeadW = 6, HeadD = 11, HeadZ = -4,
            Arm = 34, ArmW = 6, ArmD = 7, Hand = 6, FaceUp = 1, Skin = Mat.SkinHuman, Shirt = plate, Sleeve = plate, Trousers = plate, Boots = plate,
            Belt = gold, Hair = dark ? Mat.Hair : gold, Brow = dark ? Mat.Hair : gold, HairStyle = 1, Eye = glow, Mouth = Mat.Lips,
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
        // gold hair standing up off the head in points, swept back
        foreach (var (x, z, h) in new[] { (-5, 2, 5), (-3, 4, 8), (-1, 3, 10), (1, 3, 10), (3, 4, 8), (5, 2, 5), (-4, -1, 7), (0, -1, 9), (4, -1, 7), (-2, -4, 6), (2, -4, 6) })
            for (int k = 0; k < h; k++)
                s.Box(Body, x - (k < h / 2 ? 1 : 0), crown - 2 + k, hz0 + 5 + z - k / 2, x + 1, crown - 1 + k, hz0 + 7 + z - k / 2, gold);
        s.Box(Body, -hx, hy0 + 2, hz0 - 1, hx, crown, hz0 + 2, gold);
        FlameSword(s, b, 72);
        // the body shaped first, then the plate laid over it
        Relief(s, f, b);
        Harness(s, b, plate, gold, glow, dark ? Mat.ClothDark : Mat.ClothWhite);
        return s.Into(f, b.Waist, b.Neck, b.Chest);
    }

    /// <summary>
    /// The archangel's plate laid over its shaped body, piece by piece and never a wall: the breastplate swelling either
    /// side of the cross and the cuirass rounded in every course, the head rounded under its hair, gauntlets; a belt with
    /// a buckle and a stone in it; two lames round the hips, a third over the seat behind; over the front and the outside
    /// of each thigh a tasset of four lames, each curved round the thigh and a little wider than the one over it, gold at
    /// its edge, the legs showing between and behind; a narrow cloth down the front to the knee, gold-bordered with a gold
    /// cross; below the knee the leg made again round, the greave narrowing to the ankle and the calf behind, the sabaton
    /// a wedge in lames; knee cops with a wing at the outside; pauldrons of a dome and three lames, a guard standing up
    /// from each by the neck; the arms made again round with their biceps and forearms; couters at the elbows; cuffs at
    /// the wrists.
    /// </summary>
    static void Harness(Sculpt s, Build b, byte plate, byte gold, byte glow, byte cloth)
    {
        float tz = (b.Z0 + b.Z1) / 2f;
        // the breast standing out either side of the cross, before the cuirass is rounded
        foreach (int sg in new[] { 1, -1 })
            s.FBall(Body, sg * 5.2f, b.Neck - 9.5f, b.Z1 + .4f, 5.4f, 4.4f, 2.4f, plate, Tissue.Armor);
        // the cuirass rounded: each course of the trunk cut to a rounded oblong a little larger than it is, so the
        // breast swells and the sides turn away instead of standing flat; and narrowed from the chest down to the
        // belly, a wedge and not a column, flaring a little again over the hips
        const int Q = BodyPlan.K;
        for (int y = b.Waist * Q; y < (b.Neck - 1) * Q; y++)
        {
            int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
            for (int z = (b.Z0 - 4) * Q; z < (b.Z1 + 6) * Q; z++)
                for (int x = -(b.Chest + 3) * Q; x < (b.Chest + 3) * Q; x++)
                    if (s.FHas(Body, x, y, z)) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z); }
            if (x0 > x1) continue;
            float t = (y / (float)Q - b.Waist) / (b.Neck - 1 - b.Waist), up = Math.Clamp((t - .2f) / .65f, 0, 1);
            float wedge = .7f + .3f * up * up * (3 - 2 * up) + .04f * MathF.Max(0, 1 - t / .12f), deep = .88f + .12f * up;
            float mx = (x0 + x1 + 1) / 2f, mz = (z0 + z1 + 1) / 2f, rx = MathF.Min((x1 - x0 + 1) / 2f, b.Chest * Q * wedge) * 1.07f, rz = (z1 - z0 + 1) / 2f * 1.16f * deep;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float ax = MathF.Abs(x + .5f - mx) / rx, az = MathF.Abs(z + .5f - mz) / rz;
                    if (MathF.Pow(ax, 2.2f) + MathF.Pow(az, 2.2f) > 1) s.FDel(Body, x, y, z);
                }
        }
        // the head rounded so, under the hair, its face kept
        for (int y = (b.Neck + 2) * Q; y < b.Crown * Q; y++)
        {
            int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
            for (int z = (b.HeadZ - 2) * Q; z < (b.HZ1 + 3) * Q; z++)
                for (int x = -(b.HeadW + 2) * Q; x < (b.HeadW + 2) * Q; x++)
                    if (s.FHas(Body, x, y, z)) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z); }
            if (x0 > x1) continue;
            float mx = (x0 + x1 + 1) / 2f, mz = (z0 + z1 + 1) / 2f, rx = (x1 - x0 + 1) / 2f * 1.07f, rz = (z1 - z0 + 1) / 2f * 1.14f;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (MathF.Pow(MathF.Abs(x + .5f - mx) / rx, 3) + MathF.Pow(MathF.Abs(z + .5f - mz) / rz, 3) > 1) s.FDel(Body, x, y, z);
        }
        // gauntlets: the fist made again round in plate, the fingers rolled over the grip with a ridge of gold over the
        // knuckles, the thumb across them, the back of the hand plated
        foreach (var (arm, sg) in new[] { (Main, -1), (Off, 1) })
        {
            float hx0 = sg < 0 ? b.ArmX0 : -b.ArmX0 - b.ArmW, hx1 = hx0 + b.ArmW, hc = (hx0 + hx1) / 2, hz = (b.ArmZ0 + b.ArmZ1) / 2f;
            float hy = b.HandY + b.Hand / 2f;
            s.FCut(arm, hx0 - 1, b.HandY - 1, b.ArmZ0 - 2, hx1 + 1, b.HandY + b.Hand, b.ArmZ1 + 2);
            Balls(s, arm, plate, (hc, hy + .4f, hz - .3f, b.ArmW / 2f + .2f, b.Hand / 2f + .2f, b.ArmD / 2f), (hc, hy - .6f, hz + 1, b.ArmW / 2f + .4f, b.Hand / 2f - .4f, b.ArmD / 2f + .2f));
            Limb(s, arm, gold, (hx0 + .9f, hy - .2f, b.ArmZ1 + .6f, 1.1f), (hx1 - .9f, hy - .2f, b.ArmZ1 + .6f, 1.1f));
            Limb(s, arm, plate, (hc - sg * (b.ArmW / 2f + .3f), hy + 1, hz + 1.2f, 1.1f), (hc - sg * .6f, hy - .8f, b.ArmZ1 + 1.1f, .9f));
            s.FSkin(arm, hx0 - 1, b.HandY + b.Hand - 1.4f, b.ArmZ0 - 2, hx1 + 1, b.HandY + b.Hand - .9f, b.ArmZ1 + 3, gold, Tissue.Armor);
        }
        // the belt, its buckle and the stone in it: close round the waist as the cuirass has left it
        float wx0 = float.MaxValue, wx1 = float.MinValue, wz0 = float.MaxValue, wz1 = float.MinValue;
        for (int z = (b.Z0 - 4) * Q; z < (b.Z1 + 6) * Q; z++)
            for (int x = -(b.Chest + 3) * Q; x < (b.Chest + 3) * Q; x++)
                if (s.FHas(Body, x, b.Waist * Q + 1, z)) { wx0 = MathF.Min(wx0, x); wx1 = MathF.Max(wx1, x + 1); wz0 = MathF.Min(wz0, z); wz1 = MathF.Max(wz1, z + 1); }
        float bx = (wx1 - wx0) / (2f * Q) + .4f, bzr = (wz1 - wz0) / (2f * Q) + .4f, bz = (wz0 + wz1) / (2f * Q);
        Band(s, Body, 0, bz, bx, bzr, 1.3f, b.Waist - .8f, b.Waist + 1.6f, 0, plate, gold);
        Band(s, Body, 0, bz, bx + .05f, bzr + .05f, 1.3f, b.Waist + 1.1f, b.Waist + 1.6f, 0, gold);
        s.FBox(Body, -2.2f, b.Waist - 1, b.Z1 + .4f, 2.2f, b.Waist + 1.8f, b.Z1 + 1.2f, gold, Tissue.Armor);
        s.FBox(Body, -.8f, b.Waist - .2f, b.Z1 + 1.1f, .8f, b.Waist + 1, b.Z1 + 1.6f, glow, Tissue.Armor);
        // two lames round the hips, each flaring a little, gold at the hem
        Band(s, Body, 0, tz, b.Chest - .3f, (b.Z1 - b.Z0) / 2f + 1, 1, b.Waist - 3.2f, b.Waist - .6f, .5f, plate, gold);
        Band(s, Body, 0, tz, b.Chest + .2f, (b.Z1 - b.Z0) / 2f + 1.4f, 1, b.Waist - 5.6f, b.Waist - 3, .5f, plate, gold);
        foreach (var (leg, sg) in new[] { (LegR, -1), (LegL, 1) })
        {
            // the tassets: over the front and the outside of each thigh, four lames down to above the knee
            float cx = sg * (b.Gap + b.LegW / 2f);
            for (int k = 0; k < 4; k++)
            {
                float y1 = b.Waist - 4.6f - k * 2.7f;
                Band(s, leg, cx, 0, b.LegW / 2f + .9f + k * .15f, b.LegD / 2f + 1.1f + k * .15f, 1, y1 - 3.2f, y1, .35f, plate, gold,
                    (dx, dz) => dz > -.5f || dx * sg > 1.5f);
            }
            // below the knee the leg made again round: the greave narrowing to the ankle, the calf behind; the sabaton a
            // wedge forward to a point, in lames, its sole dark. (Its top narrower than the thigh's foot over it, so that
            // bent at the knee the shin's cut top stays under the knee's cop.)
            int knee = b.Leg / 2;
            s.FCut(leg, cx - 6, -1, -8, cx + 6, knee, 8);
            Limb(s, leg, plate, (cx, knee - 1.3f, .2f, 3.1f), (cx, 12, 0, 3.2f), (cx, 5, -.2f, 2.5f));
            s.FBall(leg, cx, 13.5f, -1.3f, 3.1f, 4.4f, 2.9f, plate, Tissue.Armor);
            s.FBall(leg, cx, 4.2f, -.3f, 2.9f, 1.9f, 2.9f, plate, Tissue.Armor);
            Limb(s, leg, plate, (cx, 2.6f, -2.6f, 2.7f), (cx, 2.3f, 4.5f, 2.5f), (cx, 1.3f, 9.5f, .9f));
            s.FCut(leg, cx - 6, -3, -8, cx + 6, 0, 14);
            s.FPattern(leg, (int)((cx - 6) * Q), 0, -8 * Q, (int)((cx + 6) * Q), 6 * Q, 15 * Q, (x, y, z) =>
            {
                float my = s.FMid(y), mz = s.FMid(z);
                if (my < .6f) return Mat.SteelDark;
                foreach (float lz in new[] { 1.4f, 3.6f, 5.8f, 7.8f }) if (MathF.Abs(mz - lz) < .26f && my > 1.2f) return gold;
                return 0;
            });
            // the knee's cop on the foot of the thigh, over the bend, a wing at its outside, a boss of gold before
            Shell(s, leg, cx, knee + .9f, .3f, 4.1f, 2.9f, 4.6f, knee, knee + 3.8f, plate);
            Shell(s, leg, cx + sg * 3.7f, knee + .9f, .3f, 1, 2.6f, 3.2f, knee, knee + 3.5f, plate);
            Shell(s, leg, cx + sg * 4.1f, knee + .9f, .3f, .7f, 2, 2.5f, knee, knee + 3, gold);
            Shell(s, leg, cx, knee + 1, 4.6f, 1.1f, 1.1f, .7f, knee, knee + 3, gold);
            // the greave banded gold below the knee and at the ankle, a ridge down its front
            s.FSkin(leg, cx - 5, knee - 4.3f, -6, cx + 5, knee - 3.6f, 7, gold, Tissue.Armor);
            s.FSkin(leg, cx - 5, 5, -6, cx + 5, 5.6f, 7, gold, Tissue.Armor);
            s.FSkin(leg, cx - .45f, 5.6f, 1.5f, cx + .45f, knee - 4.3f, 7, gold, Tissue.Armor);
        }
        // the cloth down the front, wider toward its hem, falling in folds; gold at its edges and a gold cross on it
        float clothTop = b.Waist + 1, clothLow = b.Leg / 2f - 1, cz = b.Z1 + 1.6f;
        s.FFill(Body, s.FAt(-3.5f), s.FAt(clothLow), s.FAt(cz - .5f), s.FAt(3.5f) + 1, s.FAt(clothTop) + 1, s.FAt(cz + 1.4f) + 1, (x, y, z) =>
        {
            float my = s.FMid(y), mx = s.FMid(x), mz = s.FMid(z);
            if (my < clothLow || my >= clothTop) return 0;
            float u = (clothTop - my) / (clothTop - clothLow), w = 2.7f + .7f * u, fold = .35f * MathF.Sin(mx * 1.7f) * u;
            if (MathF.Abs(mx) >= w || mz < cz + fold || mz >= cz + fold + .8f) return 0;
            bool edge = MathF.Abs(mx) > w - .55f || my < clothLow + .55f;
            bool cross = MathF.Abs(mx) < .45f && my > clothLow + 3 && my < clothTop - 4 || MathF.Abs(my - (clothTop - 8)) < .45f && MathF.Abs(mx) < 1.8f;
            return edge || cross ? gold : cloth;
        }, Tissue.Gear);
        // behind, a third lame over the seat; down the back a gold ridge, the lower back in lames as the belly is
        Band(s, Body, 0, tz, b.Chest + .6f, (b.Z1 - b.Z0) / 2f + 1.8f, 1, b.Waist - 8, b.Waist - 5.4f, .4f, plate, gold, (dx, dz) => dz < -1.5f);
        s.FSkin(Body, -.5f, b.Waist + 2, b.Z0 - 2, .5f, b.Neck - 3, tz - 2, gold, Tissue.Armor);
        foreach (int k in new[] { 5, 9, 13 }) s.FSkin(Body, -b.Chest + 3, b.Waist + k, b.Z0 - 2, b.Chest - 3, b.Waist + k + .5f, tz - 2, gold, Tissue.Armor);
        // the pauldrons: a dome over the shoulder, three lames under it each a little narrower, gold at every edge; a
        // guard standing up from it by the neck
        foreach (var (arm, sg) in new[] { (Main, -1), (Off, 1) })
        {
            float cx = sg * (b.Chest + b.ArmW / 2f), az = (b.ArmZ0 + b.ArmZ1) / 2f, top = b.Neck - 8.5f;
            // (the elbow where the figure bends the arm: eleven twentieths up its rows)
            var (alo, ahi) = RowsOf(s, arm);
            float ey = alo + (ahi - alo + 1) * 11 / 20, wy = b.HandY + b.Hand;
            // the arm made again round over the hand: the upper arm with its biceps, the forearm swelling under the
            // elbow and narrowing to the wrist (its top narrower than the upper arm's foot); bands of gold at the elbow
            // and down the forearm
            s.FCut(arm, cx - 6, wy + .5f, az - 6, cx + 6, b.Neck - 2, az + 6);
            Limb(s, arm, plate, (cx, b.Neck - 4, az, 3.4f), (cx, ey, az, 3f));
            s.FBall(arm, cx, ey + 6, az + .9f, 2.9f, 4.6f, 2.6f, plate, Tissue.Armor);
            Limb(s, arm, plate, (cx, ey - .9f, az, 2.6f), (cx, ey - 5, az, 3.2f), (cx, wy + .5f, az, 2.4f));
            s.FBall(arm, cx, ey - 4, az, 3.3f, 3.8f, 3.1f, plate, Tissue.Armor);
            s.FSkin(arm, cx - 5, ey - 7.5f, az - 5, cx + 5, ey - 7, az + 5, gold, Tissue.Armor);
            s.FSkin(arm, cx - 5, ey + 2.5f, az - 5, cx + 5, ey + 3, az + 5, gold, Tissue.Armor);
            Shell(s, arm, cx + sg * .4f, top, az, b.ArmW / 2f + 3.4f, 6.2f, b.ArmD / 2f + 3.2f, top - .3f, top + 7, plate);
            Shell(s, arm, cx + sg * .4f, top, az, b.ArmW / 2f + 3.5f, 6.2f, b.ArmD / 2f + 3.3f, top - .3f, top + .3f, gold);
            for (int k = 0; k < 3; k++)
            {
                float y1 = top - .3f - k * 2.1f;
                Band(s, arm, cx + sg * .3f, az, b.ArmW / 2f + 2.6f - k * .45f, b.ArmD / 2f + 2.4f - k * .45f, 1, y1 - 2.4f, y1, .35f, plate, gold);
            }
            float gx = cx - sg * (b.ArmW / 2f + 1.2f);
            s.FBox(arm, gx - .45f, top + 2, az - 4, gx + .45f, top + 8.4f, az + 4, plate, Tissue.Armor);
            s.FBox(arm, gx - .5f, top + 7.8f, az - 4.1f, gx + .5f, top + 8.6f, az + 4.1f, gold, Tissue.Armor);
            // the couter on the foot of the upper arm, over the bend, a wing at its outside
            Shell(s, arm, cx, ey + .8f, az - .6f, 3.6f, 2.4f, 3.6f, ey, ey + 3.2f, plate);
            Shell(s, arm, cx + sg * 3.3f, ey + .9f, az, .9f, 2, 2.6f, ey, ey + 2.9f, gold);
            // the cuff at the wrist, flaring a little
            Band(s, arm, cx, az, b.ArmW / 2f - .1f, b.ArmD / 2f - .2f, .8f, b.HandY + b.Hand - .2f, b.HandY + b.Hand + 2.2f, .5f, plate, gold);
        }
    }

    /// <summary>
    /// A band round something upright (half-voxels): the cells between an oval, a little squared, and one as much smaller
    /// as it is thick, from y0 up to y1, growing by the flare from its top to its bottom; only where the keep allows (by
    /// the way out from its middle), and a rim of another colour along its bottom edge.
    /// </summary>
    static void Band(Sculpt s, BodyPart p, float cx, float cz, float rx, float rz, float thick, float y0, float y1, float flare, byte m,
        byte rim = 0, Func<float, float, bool> keep = null)
    {
        static float P4(float v) => MathF.Pow(MathF.Abs(v), 2.4f);
        float far = MathF.Max(rx, rz) + flare + 1;
        s.FFill(p, s.FAt(cx - far), s.FAt(y0), s.FAt(cz - far), s.FAt(cx + far) + 1, s.FAt(y1) + 1, s.FAt(cz + far) + 1, (x, y, z) =>
        {
            float my = s.FMid(y);
            if (my < y0 || my >= y1) return 0;
            float u = (y1 - my) / (y1 - y0), ax = rx + flare * u, az = rz + flare * u, dx = s.FMid(x) - cx, dz = s.FMid(z) - cz;
            if (P4(dx / ax) + P4(dz / az) > 1 || P4(dx / (ax - thick)) + P4(dz / (az - thick)) <= 1) return 0;
            if (keep != null && !keep(dx, dz)) return 0;
            return rim != 0 && my < y0 + .55f ? rim : m;
        }, Tissue.Armor);
    }

    /// <summary>The cells of an ellipsoid between two heights (half-voxels), set on a part: a dome, a cop, a boss.</summary>
    static void Shell(Sculpt s, BodyPart p, float cx, float cy, float cz, float rx, float ry, float rz, float y0, float y1, byte m)
    {
        float lo = MathF.Max(cy - ry, y0), hi = MathF.Min(cy + ry, y1);
        s.FFill(p, s.FAt(cx - rx), s.FAt(lo), s.FAt(cz - rz), s.FAt(cx + rx) + 1, s.FAt(hi) + 1, s.FAt(cz + rz) + 1, (x, y, z) =>
        {
            float my = s.FMid(y);
            if (my < lo || my > hi) return 0;
            float dx = (s.FMid(x) - cx) / rx, dy = (my - cy) / ry, dz = (s.FMid(z) - cz) / rz;
            return dx * dx + dy * dy + dz * dz <= 1 ? m : (byte)0;
        }, Tissue.Armor);
    }

    /// <summary>The lowest and the highest row (half-voxels) a part has cells in.</summary>
    static (int lo, int hi) RowsOf(Sculpt s, BodyPart p)
    {
        var g = s.FGrid(p);
        int lo = int.MaxValue, hi = int.MinValue;
        for (int y = 0; y < g.SY; y++)
            for (int i = y * g.SZ * g.SX, e = i + g.SZ * g.SX; i < e; i++)
                if (g.M[i] != 0) { int r = (int)MathF.Floor((g.Y0 + y) / (float)BodyPlan.K); lo = Math.Min(lo, r); hi = Math.Max(hi, r); break; }
        return (lo, hi);
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
    /// in long white blades, the longer the farther out, each a little apart from the next. Dark, the destroying angel's:
    /// black feathers on iron bones.
    /// </summary>
    public static PartGrid Wing(bool dark = false)
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
                byte m = isBone ? (plated && y > top - bone + 1 ? (dark ? Mat.SteelDark : Mat.Brass) : dark ? Mat.Chitin : Mat.FeatherPale)
                    : dark ? (y > top - bone - 4 ? Mat.Chitin : Mat.Char) : (y > top - bone - 4 ? Mat.ClothWhite : Mat.FeatherPale);
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
    /// The Great Toad, sitting as a toad sits: the body a pear, low and broad at the haunches and up at the shoulders; the
    /// head wide and flat, its snout rounded, the mouth a groove right round the front of it from under one eye to under
    /// the other; great gold eyes standing up out of the head with a dark bar across each and a heavy lid over it; the
    /// glands behind the eyes, the drum of the ear behind them, the nostrils at the snout's tip. Yellow-green, blotched
    /// darker, warty all over the back and the flanks and in rows down from the glands; the belly and the throat pale.
    /// The forelegs prop it up: the upper arm back from the shoulder to the elbow, the forearm down and forward to the
    /// hand, its four fingers splayed on the ground. The hind legs folded at its sides: the great thigh forward from the
    /// hip to the knee, the shin back along it to the heel, the long foot forward along the ground, five toes webbed. Its
    /// back is a bank of the swamp it came out of, moss and grass with ferns and toadstools, and the horde's shamans
    /// have stuck their totems in it, bone and horn.
    /// </summary>
    static BodyPlan Toad()
    {
        var f = new BodyPlan { Chop = 1.6f, Stride = 40 };
        byte skin = Mat.SkinGoblinPale, dark = Mat.ScaleGreen, belly = Mat.ScaleBelly, pale = Mat.Belly, wart = Mat.Hide;
        var s = new Sculpt();

        // ---- the body and the head, balls run together: the haunches, the middle, the shoulders, the head, the snout
        Balls(s, Body, skin, (0, 17, -17, 23, 14, 19), (0, 23, -3, 25, 16, 19), (0, 24, 10, 21, 14, 13), (0, 29, 19, 22, 10, 14), (0, 27.5f, 28, 17, 8, 9));
        // the head flat on top between the eyes
        s.FCut(Body, -12, 37.5f, 12, 12, 50, 40);
        // the throat and the belly pale
        s.FPaint(Body, -40, 0, -40, 40, 13, 40, belly);
        s.FPaint(Body, -40, 0, 8, 40, 22, 40, belly);
        s.FPaint(Body, -40, 13, 8, 40, 22, 40, pale);

        // ---- the mouth: a dark groove round the front of the head, its lip over it, rising a little to its corners
        s.FPattern(Body, -46, 44, 22, 46, 64, 80, (x, y, z) =>
        {
            float my = s.FMid(y), mz = s.FMid(z), line = 25 + MathF.Max(0, 14 - mz) * .3f;
            return mz > 11 && MathF.Abs(my - line) < .5f ? Mat.Maw : MathF.Abs(my - line - 1) < .5f && mz > 11 ? dark : (byte)0;
        });
        // the nostrils at the tip of the snout
        foreach (int sg in new[] { 1, -1 }) s.FBall(Body, sg * 3.5f, 32.8f, 35.4f, .7f, .6f, .9f, Mat.Maw, paint: true);

        // ---- the eyes, the glands behind them and the ears' drums
        foreach (int sg in new[] { 1, -1 })
        {
            // the eye standing up out of the head; gold, a dark bar across it; the heavy lid over it
            s.FBall(Body, sg * 14.5f, 35.5f, 21, 5.4f, 4.8f, 5.4f, skin);
            s.FBall(Body, sg * 15.6f, 36.4f, 22.2f, 4.2f, 3.8f, 4.2f, Mat.Sulphur);
            s.FBall(Body, sg * 15.6f, 36.4f, 22.2f, 4.3f, .8f, 4.3f, Mat.Hair, paint: true);
            s.FBall(Body, sg * 14.8f, 39.2f, 21.2f, 5.2f, 1.8f, 5.4f, dark);
            // the gland behind it, long and swollen, pitted
            s.FBall(Body, sg * 12.5f, 35.8f, 9, 4.6f, 2.8f, 7.5f, dark);
            for (int k = 0; k < 9; k++)
                s.FBall(Body, sg * (11 + Dice(k, sg, 2, 601) * 3.5f), 38.3f, 3.5f + Dice(k, sg, 3, 602) * 11, .45f, .45f, .45f, Mat.Maw, paint: true);
            // the drum of the ear behind the eye, a dark ring
            s.FBall(Body, sg * 20.2f, 32, 13.5f, 1.4f, 3, 3, wart, paint: true);
            s.FBall(Body, sg * 20.2f, 32, 13.5f, 1.6f, 2.1f, 2.1f, skin, paint: true);
        }

        // ---- the colour of its back and flanks: blotched darker, a pale line down its spine
        for (int k = 0; k < 54; k++)
        {
            float x = (Dice(k, 1, 7, 611) - .5f) * 52, z = -34 + Dice(k, 2, 7, 612) * 46, r = 2 + Dice(k, 3, 7, 613) * 3;
            s.FBall(Body, x, 16 + Dice(k, 4, 7, 614) * 26, z, r, r * 1.6f, r, dark, paint: true);
        }
        s.FPaint(Body, -.8f, 30, -36, .8f, 50, 6, skin);

        // ---- the warts: over the back and the flanks, in rows down from the glands
        var c0 = (x: 0f, y: 16f, z: -6f);
        for (int k = 0; k < 560; k++)
        {
            // a way out from the middle of the body, up and to the sides, never under the belly nor over the face
            float a = Dice(k, 5, 1, 621) * MathF.Tau, b = -.05f + Dice(k, 6, 1, 622) * 1.4f;
            float dx = MathF.Cos(a) * MathF.Cos(b), dy = MathF.Sin(b), dz = MathF.Sin(a) * MathF.Cos(b);
            if (dz > .55f && dy > .25f) continue;
            if (!Surface(s, Body, c0.x, c0.y, c0.z, dx, dy, dz, 40, out float wx, out float wy, out float wz) || wy < 12) continue;
            float r = .6f + Dice(k, 7, 1, 623) * .9f;
            byte m = Dice(k, 8, 1, 624) < .3f ? wart : Dice(k, 9, 1, 625) < .5f ? dark : skin;
            s.FBall(Body, wx + dx * r * .4f, wy + dy * r * .4f, wz + dz * r * .4f, r, r, r, m);
        }
        foreach (int sg in new[] { 1, -1 })
            for (int k = 0; k < 9; k++)
            {
                float z = 2 - k * 4.2f;
                if (Surface(s, Body, sg * 9, 16, z, sg * .35f, 1, 0, 40, out float wx, out float wy, out float wz))
                    s.FBall(Body, wx, wy + .5f, wz, 1.5f, 1.2f, 1.7f, k % 2 == 0 ? wart : dark);
            }

        // ---- the forelegs: from the shoulder back to the elbow, down and forward to the hand, four fingers splayed
        foreach (var (leg, sg) in new[] { (Main, -1), (Off, 1) })
        {
            var (sx, sy, sz) = (sg * 16f, 22f, 12f);
            var (ex, ey, ez) = (sg * 24f, 14f, 5f);
            var (wx, wy, wz) = (sg * 22f, 3.2f, 16f);
            Limb(s, leg, skin, (sx, sy, sz, 4.8f), (ex, ey, ez, 3.6f));
            Limb(s, leg, skin, (ex, ey, ez, 3.6f), (sg * 23.5f, 9, 9, 3.4f), (wx, wy, wz, 2.4f));
            // the forearm's muscle under the elbow, the wrist's knob
            s.FBall(leg, sg * 23.8f, 11, 8, 3.3f, 3.8f, 3.3f, skin);
            s.FBall(leg, wx, wy + .5f, wz, 2.6f, 2.2f, 2.6f, skin);
            // banded darker across the leg
            for (int k = 0; k < 5; k++)
            {
                float u = (k + .5f) / 5, bx = ex + (wx - ex) * u, by = ey + (wy - ey) * u, bz = ez + (wz - ez) * u;
                s.FBall(leg, bx, by, bz, 4.2f, .9f, 4.2f, dark, paint: true);
            }
            s.FBall(leg, (sx + ex) / 2, (sy + ey) / 2 + 1, (sz + ez) / 2, 5, 1.2f, 5, dark, paint: true);
            // the hand flat on the ground, the fingers out from it forward and in, knobbed at the joint and the tip
            var (hx, hz) = (wx, wz + 2.5f);
            s.FBall(leg, hx, 1.8f, hz, 3.6f, 1.7f, 3.4f, skin);
            float[] ang = { -40, -14, 12, 36 }, len = { 6, 9, 8.5f, 6.5f };
            for (int k = 0; k < 4; k++)
            {
                float a = ang[k] * MathF.PI / 180 * -sg, l = len[k];
                float tx = hx + MathF.Sin(a) * l, tz = hz + MathF.Cos(a) * l;
                float mx = hx + MathF.Sin(a) * l * .55f, mz = hz + MathF.Cos(a) * l * .55f;
                Limb(s, leg, skin, (hx, 1.6f, hz, 1.4f), (mx, 1.4f, mz, 1.15f), (tx, 1.1f, tz, 1f));
                s.FBall(leg, mx, 1.5f, mz, 1.35f, 1.25f, 1.35f, skin);
                s.FBall(leg, tx, 1.1f, tz, 1.4f, 1.1f, 1.4f, pale);
            }
            Joint(f, leg, sx / H, sy / H, sz / H);
        }

        // ---- the hind legs folded at its sides: the thigh forward to the knee, the shin back to the heel, the foot
        // forward along the ground, five toes webbed
        foreach (var (leg, sg) in new[] { (LegR, -1), (LegL, 1) })
        {
            var (hx, hy, hz) = (sg * 15f, 15f, -19f);
            var (kx, ky, kz) = (sg * 28f, 9f, 1f);
            var (ax, ay, az) = (sg * 32f, 4.5f, -25f);
            var (fx, fy, fz) = (sg * 35f, 2.2f, -6f);
            // the thigh, great, from the hip forward and out to the knee; the shin back along under its outer side to the
            // heel's knob; the long foot forward on the ground outside it
            Limb(s, leg, skin, (hx, hy, hz, 7.8f), (sg * 22f, 12.5f, -9, 7.2f), (kx, ky, kz, 5.2f));
            Limb(s, leg, skin, (kx + sg * 2, ky - 1.5f, kz - 1, 4.6f), (sg * 32f, 6, -12, 4f), (ax, ay, az, 3.2f));
            s.FBall(leg, ax, ay, az, 3.4f, 3.2f, 3.4f, skin);
            Limb(s, leg, skin, (ax + sg, 2.8f, az - 1, 2.9f), (sg * 34f, 2.4f, -16, 2.6f), (fx, fy, fz, 2.3f));
            // blotched darker over the top of the thigh and the shin
            for (int k = 0; k < 5; k++)
            {
                float u = (k + .5f) / 5, r = 2 + Dice(k, sg, 4, 631) * 1.5f;
                s.FBall(leg, hx + (kx - hx) * u, hy + (ky - hy) * u + 6, hz + (kz - hz) * u, r, 2.5f, r * 1.3f, dark, paint: true);
                if (k % 2 == 0) s.FBall(leg, kx + (ax - kx) * u + sg * 3.5f, ky + (ay - ky) * u + 1, kz + (az - kz) * u, 1.5f, r, r, dark, paint: true);
            }
            // the toes: fanned forward and out, the fourth the longest; webbed between to past half their length
            float[] ang = { -24, -8, 9, 26, 46 }, len = { 6, 8, 10.5f, 12.5f, 8.5f };
            var tips = new (float x, float z)[5];
            for (int k = 0; k < 5; k++)
            {
                float a = ang[k] * MathF.PI / 180 * sg, l = len[k];
                tips[k] = (fx + MathF.Sin(a) * l, fz + MathF.Cos(a) * l);
                Limb(s, leg, skin, (fx, 1.7f, fz, 1.35f), (tips[k].x, 1, tips[k].z, .95f));
                s.FBall(leg, tips[k].x, 1, tips[k].z, 1.25f, 1, 1.25f, pale);
            }
            for (int k = 0; k < 4; k++)
                for (float u = .1f; u <= .62f; u += .04f)
                {
                    var (ax2, az2) = (fx + (tips[k].x - fx) * u, fz + (tips[k].z - fz) * u);
                    var (bx2, bz2) = (fx + (tips[k + 1].x - fx) * u, fz + (tips[k + 1].z - fz) * u);
                    s.FLine(leg, ax2, 1.2f, az2, bx2, 1.2f, bz2, belly);
                }
            // warts over the thigh and the shin, from about their middles
            for (int k = 0; k < 60; k++)
            {
                float a = Dice(k, sg, 5, 641) * MathF.Tau, b2 = -.2f + Dice(k, sg, 6, 642) * 1.5f;
                float dx = MathF.Cos(a) * MathF.Cos(b2), dy = MathF.Sin(b2), dz = MathF.Sin(a) * MathF.Cos(b2);
                bool shin = k % 3 == 0;
                float ox = shin ? sg * 31 : sg * 22, oy = shin ? 6.5f : 12.5f, oz = shin ? -12 : -9;
                if (!Surface(s, leg, ox, oy, oz, dx, dy, dz, 14, out float wx, out float wy, out float wz) || wy < 2.5f) continue;
                float r = .5f + Dice(k, sg, 7, 643) * .8f;
                s.FBall(leg, wx + dx * r * .4f, wy + dy * r * .4f, wz + dz * r * .4f, r, r, r, Dice(k, sg, 8, 644) < .35f ? wart : dark);
            }
            Joint(f, leg, hx / H, hy / H, hz / H);
        }

        // ---- its back a bank of the swamp: moss over it, grass and earth here and there, ferns and toadstools
        for (int k = 0; k < 70; k++)
        {
            float x = (Dice(k, 1, 2, 501) - .5f) * 36, z = -30 + Dice(k, 3, 4, 502) * 30;
            if (!Surface(s, Body, x, 0, z, 0, 1, 0, 60, out _, out float top, out _)) continue;
            float r = Dice(k, 5, 6, 503);
            byte m = r < .55f ? Mat.Moss : r < .8f ? Mat.GrassLush : r < .9f ? Mat.Grass : Mat.Soil;
            float w = 1.5f + Dice(k, 7, 6, 506) * 2.5f;
            s.FBall(Body, x, top, z, w, .9f + r, w, m);
        }
        for (int k = 0; k < 18; k++)
        {
            int x = -12 + (int)(Dice(k, 7, 8, 504) * 24), z = -26 + (int)(Dice(k, 9, 1, 505) * 24);
            if (!Surface(s, Body, x + .5f, 0, z + .5f, 0, 1, 0, 60, out _, out float topF, out _)) continue;
            int top = (int)topF;
            if (k % 3 == 0) { s.Box(Body, x, top, z, x + 1, top + 2, z + 1, Mat.Snag); s.Box(Body, x - 1, top + 2, z - 1, x + 2, top + 3, z + 2, Mat.Mushroom); }
            else s.Box(Body, x, top, z, x + 2, top + 3 + k % 3, z + 1, Mat.Fern);
        }
        // the shamans' totems stuck in its back: a pole of bone with a skull, a pair of horns
        foreach (var (x, z) in new[] { (-9, -18), (8, -6), (-2, -28) })
        {
            if (!Surface(s, Body, x + .5f, 0, z + .5f, 0, 1, 0, 60, out _, out float topF, out _)) continue;
            int top = (int)topF;
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

    /// <summary>Ellipsoids run together into one body in cells (half-voxels), each cell set once.</summary>
    static void Balls(Sculpt s, BodyPart p, byte m, params (float x, float y, float z, float rx, float ry, float rz)[] balls)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
        foreach (var e in balls)
        {
            x0 = MathF.Min(x0, e.x - e.rx); x1 = MathF.Max(x1, e.x + e.rx);
            y0 = MathF.Min(y0, e.y - e.ry); y1 = MathF.Max(y1, e.y + e.ry);
            z0 = MathF.Min(z0, e.z - e.rz); z1 = MathF.Max(z1, e.z + e.rz);
        }
        s.FFill(p, s.FAt(x0), s.FAt(y0), s.FAt(z0), s.FAt(x1) + 1, s.FAt(y1) + 1, s.FAt(z1) + 1, (x, y, z) =>
        {
            float mx = s.FMid(x), my = s.FMid(y), mz = s.FMid(z);
            foreach (var e in balls)
            {
                float dx = (mx - e.x) / e.rx, dy = (my - e.y) / e.ry, dz = (mz - e.z) / e.rz;
                if (dx * dx + dy * dy + dz * dz <= 1) return m;
            }
            return 0;
        });
    }

    /// <summary>
    /// A limb in cells (half-voxels), through these points and as thick as these at each: balls run along it close
    /// together, so it swells and narrows as it goes.
    /// </summary>
    static void Limb(Sculpt s, BodyPart p, byte m, params (float x, float y, float z, float r)[] pts)
    {
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            var (ax, ay, az, ar) = pts[i];
            var (bx, by, bz, br) = pts[i + 1];
            // (a third of its thickness apart, never closer than a cell: near enough that its side stays smooth)
            float len = MathF.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay) + (bz - az) * (bz - az));
            int n = Math.Max(1, (int)MathF.Ceiling(len / MathF.Max(.4f, MathF.Min(ar, br) * .33f)));
            for (int k = 0; k <= n; k++)
            {
                float u = k / (float)n, r = ar + (br - ar) * u;
                s.FBall(p, ax + (bx - ax) * u, ay + (by - ay) * u, az + (bz - az) * u, r, r, r, m);
            }
        }
    }

    /// <summary>
    /// Where a line out from a point along a way first comes to a part's cells, walked in from far out along it
    /// (half-voxels): false when it meets none.
    /// </summary>
    static bool Surface(Sculpt s, BodyPart p, float cx, float cy, float cz, float dx, float dy, float dz, float far, out float x, out float y, out float z)
    {
        float l = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        dx /= l; dy /= l; dz /= l;
        for (float t = far; t > 0; t -= .25f)
        {
            x = cx + dx * t; y = cy + dy * t; z = cz + dz * t;
            if (s.FHas(p, s.FAt(x), s.FAt(y), s.FAt(z))) return true;
        }
        x = y = z = 0;
        return false;
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
