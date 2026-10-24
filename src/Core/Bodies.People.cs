using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>Our people, the dead risen and the priests: the workers, archers and swordsmen, and each of them finer.</summary>
public static partial class Bodies
{
    // ---------------------------------------------------------------- people

    static readonly byte[] HumanSkins = { Mat.SkinHuman, Mat.SkinTan, Mat.SkinPale };
    static readonly byte[] Hairs = { Mat.Hair, Mat.HairBrown, Mat.HairGinger, Mat.HairGrey };

    static BodyPlan Worker(int look)
    {
        var f = new BodyPlan { Chop = .75f };
        byte hair = Hairs[(look + look / 4) % 4];
        var b = new Build
        {
            Skin = HumanSkins[look % 3 == 2 ? 1 : look % 5 == 4 ? 2 : 0], Hair = hair, HairStyle = look % 3 == 2 ? 2 : look % 2,
            Shirt = new[] { Mat.Linen, Mat.ClothBrown, Mat.Linen, Mat.ClothGreen }[look % 4], Trousers = look % 3 == 1 ? Mat.ClothBrown : Mat.ClothDark,
            Leg = look == 5 ? 11 : 10,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy1 = b.Crown;
        // sleeves rolled to the elbow on some; a leather apron from the chest over the thighs
        if (look % 2 == 1) s.Paint(Main, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + b.Hand + 3, b.ArmZ1, b.Skin);
        if (look % 2 == 1) s.Paint(Off, b.Chest, b.HandY, b.ArmZ0, -b.ArmX0, b.HandY + b.Hand + 3, b.ArmZ1, b.Skin);
        s.Box(Body, -3, b.Waist - 3, b.Z1, 3, b.Neck - 3, b.Z1 + 1, Mat.Leather);
        s.Box(Body, -3, b.Neck - 3, b.Z1 - 1, -2, b.Neck, b.Z1, Mat.Leather);
        s.Box(Body, 2, b.Neck - 3, b.Z1 - 1, 3, b.Neck, b.Z1, Mat.Leather);
        // a felt cap with a peak, a straw hat with a wide brim, the bare head, or a hood
        switch (look % 4)
        {
            case 0:
                s.Paint(Body, -hx, hy1 - 2, hz0, hx, hy1, hz1, Mat.Leather);
                s.Box(Body, -hx + 1, hy1, hz0 + 1, hx - 1, hy1 + 1, hz1 - 1, Mat.Leather);
                s.Box(Body, -3, hy1 - 1, hz1 - 1, 3, hy1, hz1 + 2, Mat.Leather);
                break;
            case 1:
                s.Flat(Body, hy1 - 1, 6.4f, Mat.Straw);
                s.Flat(Body, hy1, 3.3f, Mat.Leather);
                s.Flat(Body, hy1 + 1, 2.9f, Mat.Straw);
                break;
            case 3:
                Hood(s, b, Mat.ClothBrown);
                break;
        }
        if (look % 3 == 1 || look == 6) Beard(s, b, hair, look != 6);
        WorkerDetail(s, b, look);
        Pick(s, b, Mat.Iron);
        return Done(s, f, b);
    }

    static BodyPlan Archer(int look)
    {
        var f = new BodyPlan { Chop = .9f };
        byte hood = new[] { Mat.ClothDark, Mat.ClothGreen, Mat.ClothBrown }[look % 3];
        var b = new Build
        {
            Shirt = Mat.ClothBlue, Trousers = Mat.ClothDark, Sleeve = Mat.ClothBlue, Cuff = Mat.Leather, Skin = HumanSkins[look / 3 % 2 == 1 ? 1 : 0],
            Hair = Hairs[look % 4], HairStyle = 1,
        };
        var s = Humanoid(b, f);
        // a leather bracer on the bow arm, a strap across the chest to the quiver, the quiver on the back with its fletchings
        s.Paint(Off, b.Chest, b.HandY + b.Hand, b.ArmZ0, -b.ArmX0, b.HandY + b.Hand + 3, b.ArmZ1, Mat.Leather);
        for (int y = b.Waist + 1; y < b.Neck - 1; y++)
        {
            int x = b.Chest - 1 - (y - b.Waist - 1) * (2 * b.Chest - 2) / (b.Torso - 3);
            s.Paint(Body, x - 1, y, b.Z0, x + 1, y + 1, b.Z1, Mat.Leather);
        }
        Hood(s, b, hood);
        s.Box(Body, -3, b.Waist + 1, b.Z0 - 2, -1, b.Neck - 1, b.Z0, Mat.Leather, Tissue.Gear);
        foreach (var (x, y, c) in new[] { (-3, 0, Mat.Linen), (-2, 1, Mat.ClothRed), (-2, 0, Mat.Linen), (-3, 1, Mat.Linen) })
            s.Dot(Body, x, b.Neck - 1 + y, b.Z0 - 2 + (x + 3), c, Tissue.Gear);
        if (look == 4) Beard(s, b, b.Hair, false);
        ArcherDetail(s, b);
        Bow(s, b);
        return Done(s, f, b);
    }

    static BodyPlan Swordsman(int look)
    {
        var f = new BodyPlan { Chop = .8f, Carry = -.35f, Shield = true };
        var b = new Build
        {
            Shirt = Mat.Mail, Trousers = Mat.ClothDark, Sleeve = Mat.Mail, Belt = Mat.Leather, Skin = HumanSkins[look % 2], Hair = Hairs[(look + 1) % 4], HairStyle = 1,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        // mail on the body and the arms, its skirt hanging round the hips from a broad belt
        Mail(s, b);
        Skirt(s, b, Mat.Mail, Tissue.Armor);
        // a blue tabard over it with a gold cross, falling below the belt to a gold hem, gold along its edges near
        s.Box(Body, -3, b.Waist + 1, b.Z1, 3, b.Neck - 1, b.Z1 + 1, Mat.ClothBlue);
        s.Box(Body, -3, b.Waist - 3, b.Z1 + 1, 3, b.Waist, b.Z1 + 2, Mat.ClothBlue);
        s.Box(Body, -3, b.Waist - 3, b.Z1 + 1, 3, b.Waist - 2, b.Z1 + 2, Mat.ClothOchre);
        s.Box(Body, -1, b.Waist + 3, b.Z1, 1, b.Neck - 2, b.Z1 + 1, Mat.ClothOchre);
        s.Box(Body, -2, b.Neck - 5, b.Z1, 2, b.Neck - 4, b.Z1 + 1, Mat.ClothOchre);
        s.FPaint(Body, -3, b.Waist - 3, b.Z1, -2.5f, b.Neck - 1, b.Z1 + 2, Mat.ClothOchre);
        s.FPaint(Body, 2.5f, b.Waist - 3, b.Z1, 3, b.Neck - 1, b.Z1 + 2, Mat.ClothOchre);
        s.FPaint(Body, -.5f, b.Waist + 3, b.Z1, .5f, b.Neck - 2, b.Z1 + 1, Mat.ClothOchre);
        s.FPaint(Body, -1, b.Waist + 3, b.Z1, -.5f, b.Neck - 2, b.Z1 + 1, Mat.ClothBlue);
        s.FPaint(Body, .5f, b.Waist + 3, b.Z1, 1, b.Neck - 2, b.Z1 + 1, Mat.ClothBlue);
        s.FPaint(Body, -2, b.Neck - 5, b.Z1, 2, b.Neck - 4.5f, b.Z1 + 1, Mat.ClothBlue);
        s.FPaint(Body, -.5f, b.Neck - 5, b.Z1, .5f, b.Neck - 4.5f, b.Z1 + 1, Mat.ClothOchre);
        // steel on the shoulders; a scabbard at the left hip, a pouch at the right
        Spaulders(s, b);
        Scabbard(s, b);
        Pouch(s, b, -1);
        // a kettle hat, a nasal helm, or a mail coif over a bearded face
        switch (look % 3)
        {
            case 0: KettleHat(s, b); break;
            case 1: NasalHelm(s, b, aventail: true); break;
            default: Coif(s, b); Beard(s, b, b.Hair); break;
        }
        Sword(s, b);
        Shield(s, b);
        return Done(s, f, b);
    }

    /// <summary>
    /// A staff held upright in the right fist from the ground to over the head (its top <paramref name="top"/>
    /// half-voxels up): its cells, the column and the depth its shaft stands in.
    /// </summary>
    static Gear StaffIn(Sculpt s, Build b, out int x, out int zc)
    {
        var g = InFist(s, b, out x, out _, out _);
        zc = (b.ArmZ0 + b.ArmZ1) * 2 - 1;
        return g;
    }

    /// <summary>
    /// A skull a little bigger than a fist, facing +z with its middle at (cx, cy, cz) in cells: the dome of the cranium,
    /// dark sockets with a fire in their depths, the hole of the nose, a row of teeth and the jaw under them.
    /// </summary>
    static void Skull(Gear g, float cx, float cy, float cz, float r, byte fire)
    {
        for (int yy = (int)(cy - r * 1.3f); yy <= cy + r; yy++)
            for (int z = (int)(cz - r * 1.2f); z <= cz + r; z++)
                for (int xx = (int)(cx - r) - 1; xx <= cx + r; xx++)
                {
                    float dx = (xx + .5f - cx) / r, dy = (yy + .5f - cy) / (r * .95f), dz = (z + .5f - (cz - r * .12f)) / (r * 1.1f);
                    bool dome = dx * dx + dy * dy + dz * dz <= 1 && yy + .5f > cy - r * .55f;
                    // the jaw, narrower, under the cheekbones and forward
                    bool jaw = yy + .5f <= cy - r * .45f && yy + .5f > cy - r * 1.25f && MathF.Abs(dx) < .55f && z + .5f > cz - r * .2f && z + .5f < cz + r * .82f;
                    if (dome || jaw) g.Dot(xx, yy, z, Mat.Bone);
                }
        int front = (int)MathF.Floor(cz + r * .9f);
        foreach (int sgn in new[] { -1, 1 })
        {
            float ex = cx + sgn * r * .42f, ey = cy - r * .08f;
            for (int yy = (int)(ey - 2); yy <= ey + 1; yy++)
                for (int xx = (int)(ex - 2); xx <= ex + 1; xx++)
                    if ((xx + .5f - ex) * (xx + .5f - ex) + (yy + .5f - ey) * (yy + .5f - ey) <= 3.2f)
                        for (int z = front - 3; z <= front + 1; z++) if (g.Has(xx, yy, z)) g.Dot(xx, yy, z, z == front - 3 ? fire : Mat.Maw);
        }
        // the nose, and the teeth between the jaws
        for (int z = front - 1; z <= front + 1; z++) { g.Dot((int)MathF.Floor(cx), (int)(cy - r * .42f), z, Mat.Maw); g.Dot((int)MathF.Floor(cx) - 1, (int)(cy - r * .42f), z, Mat.Maw); }
        int ty = (int)MathF.Floor(cy - r * .62f);
        for (int xx = (int)(cx - r * .5f); xx < cx + r * .5f; xx++)
            for (int z = front - 4; z <= front + 1; z++)
                if (g.Has(xx, ty, z)) g.Dot(xx, ty, z, (xx & 1) == 0 ? Mat.Bone : Mat.Maw);
    }

    /// <summary>
    /// A priest's staff: a shaft with its knots through the fist from the ground to over the head, and bound at its top
    /// with a few turns of cord a skull with the pale fire in its eyes, a dark feather hanging from the cord.
    /// </summary>
    static void Staff(Sculpt s, Build b)
    {
        var g = StaffIn(s, b, out int x, out int zc);
        int top = (b.Crown + 3) * 4;
        g.Fill(x, 4, zc, x + 2, top, zc + 2, Mat.Handle);
        foreach (int k in new[] { 22, 49, 83 }) if (k < top - 8) g.Fill(x - 1, k, zc - 1, x + 3, k + 2, zc + 3, Mat.Handle);
        g.Fill(x, 2, zc, x + 2, 4, zc + 2, Mat.Iron);
        for (int yy = top - 5; yy < top; yy++) g.Fill(x - 1, yy, zc - 1, x + 3, yy + 1, zc + 3, (yy & 1) == 0 ? Mat.RobeTrim : Mat.Linen);
        Skull(g, x + 1, top + 5, zc + 1, 5.2f, Mat.SoulFire);
        for (int k = 0; k < 9; k++) g.Dot(x + 3, top - 4 - k, zc + 1 + k / 4, k < 6 ? Mat.ClothDark : Mat.Hair);
        g.Tip = Half(g, x + 1, top + 5, zc + 1);
        g.Edge = (0, 0, 1);
    }

    /// <summary>
    /// A rod of a staff bending as it goes: through (x, y, z) in cells, point to point, r cells thick either side.
    /// </summary>
    static void Crooked(Gear g, (float x, float y, float z)[] at, float r, byte m)
    {
        for (int i = 0; i + 1 < at.Length; i++)
            g.Rod(at[i].x / g.C.R, at[i].y / g.C.R, at[i].z / g.C.R, at[i + 1].x / g.C.R, at[i + 1].y / g.C.R, at[i + 1].z / g.C.R, r / g.C.R, m);
    }

    /// <summary>
    /// A goblin shaman's staff: a crooked shaft with a horn bound at its head, curving out and up, an ember glowing in
    /// its mouth; feathers of red and ochre hang from under it and a string of bone beads.
    /// </summary>
    static void HornStaff(Sculpt s, Build b)
    {
        var g = StaffIn(s, b, out int x, out int zc);
        float cx = x + 1, cz = zc + 1;
        int top = (b.Crown + 4) * 4;
        Crooked(g, new[] { (cx, 3f, cz), (cx + 1, top * .35f, cz - 1), (cx - 1, top * .7f, cz + 1), (cx, top, cz) }, 1.1f, Mat.Handle);
        // the horn: from the head of the staff, curving out and up, wide at its mouth
        for (int i = 0; i <= 12; i++)
        {
            float t = i / 12f, an = t * MathF.PI * .55f;
            float hx = cx - 7 * (1 - MathF.Cos(an)) * 1.4f, hy = top + 9 * MathF.Sin(an);
            g.Rod(hx / 4, hy / 4, cz / 4, hx / 4, (hy + .8f) / 4, cz / 4, (1.1f + 2.2f * t) / 4, i == 12 ? Mat.Ember : (i & 2) == 0 ? Mat.Horn : Mat.Bone);
        }
        g.Fill(x - 1, top - 3, zc - 1, x + 3, top, zc + 3, Mat.Leather);
        foreach (var (dx, len, m) in new[] { (-2, 9, Mat.ClothRed), (3, 7, Mat.ClothOchre), (1, 11, Mat.ClothRed) })
            for (int k = 0; k < len; k++) g.Dot(x + dx, top - 2 - k, zc + 1 + (k > len / 2 ? 1 : 0), m);
        for (int k = 0; k < 6; k++) g.Dot(x - 1 - k / 3, top - 3 - k, zc + 3, (k & 1) == 0 ? Mat.Bone : Mat.Hair);
        g.Tip = Half(g, cx - 9, top + 9, cz);
        g.Edge = (0, 0, 1);
    }

    /// <summary>A witch's staff: a crooked stick hooked over at its head, a bunch of herbs and flowers bound under the hook, a bone and a little pouch hung from it.</summary>
    static void HerbStaff(Sculpt s, Build b)
    {
        var g = StaffIn(s, b, out int x, out int zc);
        float cx = x + 1, cz = zc + 1;
        int top = (b.Crown + 2) * 4;
        Crooked(g, new[] { (cx, 3f, cz), (cx - 1, top * .4f, cz + 1), (cx + 1, top * .8f, cz), (cx, top, cz), (cx - 4, top + 4, cz), (cx - 7, top + 2, cz) }, 1f, Mat.Handle);
        for (int i = 0; i < 26; i++)
        {
            int hx = x - 7 + (int)(Dice(i, 1, 2, 61) * 6), hy = top - 4 + (int)(Dice(i, 3, 4, 62) * 7), hz = zc - 2 + (int)(Dice(i, 5, 6, 63) * 6);
            g.Dot(hx, hy, hz, i % 7 == 0 ? Mat.Flower : i % 5 == 0 ? Mat.Bloom : Mat.FoliageLight);
            g.Dot(hx, hy - 1, hz, Mat.Foliage);
        }
        for (int k = 0; k < 7; k++) g.Dot(x + 2, top - 3 - k, zc + 1, k < 4 ? Mat.Linen : Mat.Bone);
        g.Fill(x + 1, top - 13, zc, x + 4, top - 10, zc + 2, Mat.Bone);
        g.Fill(x - 2, top - 12, zc + 2, x + 1, top - 8, zc + 4, Mat.Leather);
        g.Tip = Half(g, cx - 5, top + 3, cz);
        g.Edge = (0, 0, 1);
    }

    /// <summary>A necromancer's staff: a dark shaft, a ring of small bones bound under a skull with the pale fire in its sockets, a black rag hanging.</summary>
    static void SkullStaff(Sculpt s, Build b)
    {
        var g = StaffIn(s, b, out int x, out int zc);
        int top = (b.Crown + 3) * 4;
        g.Fill(x, 4, zc, x + 2, top, zc + 2, Mat.Hair);
        foreach (int k in new[] { 30, 60, 90 }) if (k < top - 10) g.Fill(x - 1, k, zc - 1, x + 3, k + 1, zc + 3, Mat.Iron);
        for (int i = 0; i < 8; i++)
        {
            float an = i * MathF.PI / 4;
            int bx = (int)MathF.Floor(x + 1 + MathF.Cos(an) * 3.5f), bz = (int)MathF.Floor(zc + 1 + MathF.Sin(an) * 3.5f);
            g.Fill(bx, top - 5, bz, bx + 1, top - 1, bz + 1, Mat.Bone);
        }
        Skull(g, x + 1, top + 5, zc + 1, 5.6f, Mat.SoulFire);
        for (int k = 0; k < 12; k++) g.Fill(x - 2, top - 4 - k, zc + 2 + k / 5, x, top - 3 - k, zc + 3 + k / 5, Mat.ClothDark);
        g.Tip = Half(g, x + 1, top + 5, zc + 1);
        g.Edge = (0, 0, 1);
    }

    /// <summary>
    /// A priest of the old powers: a robe to the ankles dark as old blood (or black), its hem and a rope girdle in tarnished
    /// trim, the hood up over a pale face, a bone charm on the chest, and a staff taller than itself in the right hand.
    /// </summary>
    static BodyPlan Priest(int look)
    {
        var f = new BodyPlan { Chop = 1.2f, Stride = 7 };
        byte robe = look == 1 ? Mat.ClothDark : Mat.Robe;
        var b = new Build
        {
            Shirt = robe, Trousers = robe, Sleeve = robe, Boots = Mat.Leather, Belt = Mat.RobeTrim, Boot = 2,
            Skin = look == 2 ? Mat.SkinTan : Mat.SkinPale, Hair = Hairs[(look + 3) % 4], HairStyle = 3,
        };
        var s = Humanoid(b, f);
        // the skirt of the robe down to the ankles, a little wider than the legs, a slit at the front, the hem in trim
        s.Box(Body, -b.Chest, 3, b.Z0 - 1, b.Chest, b.Waist, b.Z1 + 1, robe);
        s.Cut(Body, -1, 3, b.Z1, 1, 7, b.Z1 + 1, both: false);
        s.Paint(Body, -b.Chest, 3, b.Z0 - 1, b.Chest, 4, b.Z1 + 1, Mat.RobeTrim);
        // the girdle's ends hanging, wide sleeves, the charm
        s.Box(Body, 1, b.Waist - 4, b.Z1 + 1, 2, b.Waist, b.Z1 + 2, Mat.RobeTrim);
        s.Box(Body, 2, b.Waist - 3, b.Z1 + 1, 3, b.Waist, b.Z1 + 2, Mat.RobeTrim);
        s.Pair(Main, Off, b.ArmX0 - 1, b.HandY + b.Hand, b.ArmZ0, b.ArmX0, b.HandY + b.Hand + 3, b.ArmZ1, robe);
        s.Box(Body, -1, b.Neck - 5, b.Z1, 1, b.Neck - 3, b.Z1 + 1, Mat.Bone, Tissue.Gear);
        s.Dot(Body, 0, b.Neck - 3, b.Z1, Mat.RobeTrim, Tissue.Gear);
        Hood(s, b, robe);
        // the trim round the face
        s.Paint(Body, -b.HeadW, b.Neck, b.HZ1 - 2, -b.HeadW + 1, b.Crown, b.HZ1, Mat.RobeTrim);
        s.Paint(Body, b.HeadW - 1, b.Neck, b.HZ1 - 2, b.HeadW, b.Crown, b.HZ1, Mat.RobeTrim);
        if (look == 0) Beard(s, b, Mat.HairGrey);
        PriestDetail(s, b);
        Staff(s, b);
        return Done(s, f, b);
    }

    /// <summary>
    /// One of the dead raised: bare bones held together by nothing that should hold them — the ribs showing in dark
    /// gaps, the belly gone to the spine — fire in its eye sockets (pale for ours, an ember's for the horde's thralls), a
    /// rag about its hips, a rusted blade; on one a dented helmet still.
    /// </summary>
    static BodyPlan Bones(int look, byte eye, byte rag)
    {
        var f = new BodyPlan { Chop = .7f, Carry = -.3f, Stride = 8 };
        var b = new Build
        {
            Skin = Mat.Bone, Shirt = Mat.Bone, Trousers = Mat.Bone, Sleeve = Mat.Bone, Boots = Mat.Bone, Belt = rag, Hair = Mat.Bone, Brow = Mat.Bone,
            Eye = eye, Mouth = Mat.Hair, HairStyle = 3, Boot = 0, Chest = 3, LegW = 2, Gap = 1,
        };
        var s = Humanoid(b, f);
        // the ribs across the chest, the belly gone to the spine
        for (int r = b.Waist + 4; r < b.Neck - 1; r += 2) s.Paint(Body, -b.Chest + 1, r, b.Z1 - 1, b.Chest - 1, r + 1, b.Z1, Mat.Hair);
        s.Cut(Body, -b.Chest, b.Waist + 1, b.Z0, -1, b.Waist + 3, b.Z1, both: true);
        // the eye sockets dark round the fire, the nose a hole, teeth
        s.Paint(Body, -b.HeadW + 1, b.Neck + 2, b.FaceZ, b.HeadW - 1, b.Neck + 3, b.HZ1, Mat.Hair);
        s.Pair(Body, Body, -2, b.Neck + 1, b.FaceZ, -1, b.Neck + 2, b.HZ1, Mat.Bone);
        // a rag about the hips
        s.Box(Body, -b.Chest, b.Waist - 2, b.Z0, b.Chest, b.Waist + 1, b.Z1, rag);
        s.Cut(Body, -1, b.Waist - 2, b.Z1 - 1, 1, b.Waist - 1, b.Z1, both: false);
        if (look == 1)
        {
            s.Paint(Body, -b.HeadW, b.Crown - 2, b.HeadZ, b.HeadW, b.Crown, b.HZ1, Mat.Iron, Tissue.Armor);
            s.Box(Body, -1, b.Crown, -1, 1, b.Crown + 1, 1, Mat.Iron, Tissue.Armor);
        }
        BonesDetail(s, b, look, eye);
        // a rusted blade, notched, its grip bound in rag, a bar of iron for its guard
        var g = InFist(s, b, out int x, out int y, out int z0);
        g.Wound(x, y, b.ArmZ0 * 4 - 3, z0, 2, Mat.Rag, rag);
        g.Fill(x - 1, y, b.ArmZ0 * 4 - 5, x + 3, y + 2, b.ArmZ0 * 4 - 3, Mat.Iron);
        g.Fill(x, y - 5, z0, x + 2, y + 7, z0 + 2, Mat.Iron);
        Blade(g, x, y + 1, z0 + 2, z0 + 44, 2.8f, .5f, Mat.Iron, true, look);
        g.Tip = Half(g, x + 1, y + 1, z0 + 44);
        return Done(s, f, b);
    }

    // ---------------------------------------------------------------- our people, finer

    /// <summary>
    /// A worker drawn finer: the felt cap's seam and its peak thinned, the straw hat woven in rings with a band round
    /// it, the rolled sleeves' roll, a pocket on the apron with a mallet's handle in it, a pouch on the belt.
    /// </summary>
    static void WorkerDetail(Sculpt s, Build b, int look)
    {
        if (!s.Detail) return;
        int hy1 = b.Crown, hz1 = b.HZ1;
        switch (look % 4)
        {
            case 0:
                for (int z = 2 * b.HeadZ; z < 2 * hz1; z++) s.FPut(Body, 0, hy1 * 2 + 1, z, Mat.LeatherDark);
                for (int x = -6; x < 6; x++) { s.FDel(Body, x, hy1 * 2 - 1, hz1 * 2 + 3); s.FDel(Body, x, hy1 * 2 - 2, hz1 * 2 + 2); s.FDel(Body, x, hy1 * 2 - 2, hz1 * 2 + 3); }
                break;
            case 1:
                s.FPattern(Body, -14, (hy1 - 1) * 2, -14, 14, (hy1 + 2) * 2, 14, (x, y, z) =>
                {
                    if (s.FMat(Body, x, y, z) != Mat.Straw) return 0;
                    float r = MathF.Sqrt((x + .5f) * (x + .5f) + (z + .5f) * (z + .5f));
                    return ((int)(r / 1.6f) & 1) == 0 ? Mat.ClothOchre : (byte)0;
                });
                break;
        }
        if (look % 2 == 1)
        {
            int roll = (b.HandY + b.Hand + 3) * 2;
            foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
                for (int z = b.ArmZ0 * 2 - 1; z <= b.ArmZ1 * 2; z++)
                    for (int x = b.ArmX0 * 2 - 1; x <= -b.Chest * 2; x++)
                        if (x == b.ArmX0 * 2 - 1 || z == b.ArmZ0 * 2 - 1 || z == b.ArmZ1 * 2) s.FSet(p, sg > 0 ? x : -1 - x, roll, z, b.Shirt);
        }
        // the pocket on the apron and the handle of a mallet standing out of it
        int py = (b.Waist + 1) * 2, pz = (b.Z1 + 1) * 2;
        for (int y = py; y < py + 5; y++) for (int x = -4; x < 2; x++) s.FSet(Body, x, y, pz, y == py + 4 ? Mat.LeatherDark : Mat.Leather);
        for (int y = py + 3; y < py + 9; y++) s.FSet(Body, 0, y, pz + 1, Mat.Handle, Tissue.Gear);
        Pouch(s, b, 1);
    }

    /// <summary>
    /// An archer drawn finer: arrows standing out of the quiver's mouth with their fletchings, its rim dark, a buckle
    /// on the strap across the chest, the bracer laced.
    /// </summary>
    static void ArcherDetail(Sculpt s, Build b)
    {
        if (!s.Detail) return;
        int top = (b.Neck - 1) * 2, qz = (b.Z0 - 2) * 2;
        for (int x = -6; x < -2; x++) for (int z = qz; z < qz + 4; z++) s.FPut(Body, x, top - 1, z, Mat.LeatherDark);
        foreach (var (x, z, h, f) in new[] { (-6, qz, 5, Mat.ClothRed), (-4, qz + 1, 7, Mat.Linen), (-5, qz + 3, 6, Mat.Linen), (-3, qz + 2, 4, Mat.ClothRed), (-4, qz + 3, 5, Mat.Linen) })
            for (int k = 0; k < h; k++) s.FSet(Body, x, top + k, z, k >= h - 3 ? f : Mat.Handle, Tissue.Gear);
        int by = (b.Waist + 5) * 2;
        s.FSet(Body, -1, by, b.Z1 * 2, Mat.Brass); s.FSet(Body, 0, by, b.Z1 * 2, Mat.Brass); s.FSet(Body, -1, by + 1, b.Z1 * 2, Mat.Brass);
        int ly = (b.HandY + b.Hand) * 2;
        for (int y = ly; y < ly + 6; y++) s.FPattern(Off, -80, y, -80, 80, y + 1, 80, (x, yy, z) => s.FMat(Off, x, yy, z) == Mat.Leather && (x + yy) % 3 == 0 ? Mat.LeatherDark : (byte)0);
    }

    /// <summary>A priest drawn finer: the knots of the rope girdle, a little skull for the charm, a zigzag worked into the hem.</summary>
    static void PriestDetail(Sculpt s, Build b)
    {
        if (!s.Detail) return;
        int cz = (b.Z1 + 1) * 2;
        foreach (int y in new[] { (b.Waist - 4) * 2, (b.Waist - 2) * 2 + 1 }) { s.FSet(Body, 2, y, cz, Mat.RobeTrim); s.FSet(Body, 3, y, cz, Mat.RobeTrim); s.FSet(Body, 2, y, cz + 1, Mat.Linen); }
        int sy = (b.Neck - 5) * 2, sz = b.Z1 * 2;
        s.FCut(Body, -1, b.Neck - 5, b.Z1, 1, b.Neck - 3, b.Z1 + 1);
        s.FBall(Body, 0, b.Neck - 4f, b.Z1 + .4f, .9f, .85f, .5f, Mat.Bone, Tissue.Gear);
        s.FPut(Body, -1, sy + 2, sz, Mat.Hair); s.FPut(Body, 0, sy + 2, sz, Mat.Hair); s.FPut(Body, -1, sy + 3, sz, Mat.Bone);
        s.FPattern(Body, -80, 6, -80, 80, 8, 80, (x, y, z) => s.FMat(Body, x, y, z) is var m && m != Mat.RobeTrim && m != 0 && ((x + z + 64) % 4 == (y - 6) * 2 || (x + z + 64) % 4 == 3 - (y - 6) * 2) ? Mat.RobeTrim : (byte)0);
    }

    /// <summary>The quilting of a gambeson drawn finer: its channels stitched upright, three cells apart.</summary>
    static void Quilting(Sculpt s)
    {
        foreach (var p in new[] { Body, Main, Off })
            s.FPattern(p, -80, 0, -80, 80, 200, 80, (x, y, z) => s.FMat(p, x, y, z) == Mat.Gambeson && (x + z + 64) % 3 == 0 ? Mat.Linen : (byte)0);
    }

    /// <summary>A slinger drawn finer: the jerkin laced across its opening, the ends of the headband hanging behind.</summary>
    static void SlingerDetail(Sculpt s, Build b, int look)
    {
        if (!s.Detail) return;
        int z = b.Z1 * 2;
        for (int y = (b.Waist + 1) * 2 + 1; y < b.Neck * 2 - 2; y += 3) { s.FSet(Body, -2, y, z, Mat.LeatherDark); s.FSet(Body, 1, y, z, Mat.LeatherDark); s.FSet(Body, -1, y + 1, z, Mat.LeatherDark); s.FSet(Body, 0, y + 1, z, Mat.LeatherDark); }
        if (look % 2 == 0)
            for (int k = 0; k < 6; k++) { s.FSet(Body, -1 - k / 3, (b.Crown - 3) * 2 - k, 2 * b.HeadZ - 1, Mat.ClothRed); s.FSet(Body, 1 + k / 4, (b.Crown - 3) * 2 - k, 2 * b.HeadZ - 1, Mat.ClothRed); }
    }

    /// <summary>A bombardier drawn finer: the apron scorched in patches, the strings of the cap's flaps hanging, rags stopping the pots at his belt.</summary>
    static void BombardierDetail(Sculpt s, Build b, int look)
    {
        if (!s.Detail) return;
        s.FPattern(Body, -80, (b.Waist - 5) * 2, -80, 80, (b.Neck - 2) * 2, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Leather && z >= b.Z1 * 2 && Dice(x >> 1, y >> 1, look, 141) < .14f ? Mat.Char : (byte)0);
        if (look != 1)
            foreach (int sg in new[] { 1, -1 })
                for (int k = 0; k < 5; k++) s.FSet(Body, sg > 0 ? -2 * b.HeadW - 1 : 2 * b.HeadW, (b.Neck + 2) * 2 - 1 - k, 0, Mat.LeatherDark);
    }

    /// <summary>
    /// A horse drawn finer: its eyes with a glint, dark nostrils at the end of a muzzle a shade darker, the mane and the
    /// tail hanging in strands, feathering over the hooves, brass studs on the bridle, the stirrups' irons.
    /// </summary>
    static void HorseDetail(Sculpt s, byte coat, byte mane, int seat)
    {
        if (!s.Detail) return;
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            s.FPut(Body, X(-4), 61, 37, Mat.EyeWhite); s.FPut(Body, X(-4), 60, 36, Mat.Eye);
            foreach (int z in new[] { 44, 47, 50 }) s.FPut(Body, X(-4), 52, z, Mat.Brass);
        }
        // the mane in strands down the crest, the tail's end ragged
        for (int k = 0; k < 5; k++)
            for (int x = -2; x < 2; x++)
                for (int z = (9 + k) * 2; z < (10 + k) * 2; z++)
                    if (Dice(x, z, k, 151) < .5f)
                        for (int y = (25 + 2 * k) * 2 + 2; y > (25 + 2 * k) * 2 - 1 - (int)(Dice(z, x, k, 152) * 4); y--) s.FSet(Body, x < 0 ? -3 : 2, y, z, mane);
        for (int x = -4; x < 4; x++) for (int z = -34; z < -31; z++) if (Dice(x, z, 0, 153) < .45f) s.FDel(Body, x, 24, z);
        // feathering over the hooves
        foreach (var p in new[] { LegR, LegL })
            s.FPattern(p, -80, 2, -80, 80, 4, 80, (x, y, z) => s.FMat(p, x, y, z) != 0 && ((x + z) & 1) == 0 ? mane : (byte)0);
    }

    /// <summary>A hound drawn finer: a glint in its eyes, its nose wet and dark, the spikes of its collar standing out.</summary>
    static void HoundDetail(Sculpt s)
    {
        if (!s.Detail) return;
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            s.FPut(Body, X(-5), 33, 23, Mat.EyeWhite);
        }
        foreach (var (x, z) in new[] { (-9, 14), (8, 14), (-9, 10), (8, 10), (-3, 22), (2, 22) }) s.FSet(Body, x, 24, z, Mat.Steel, Tissue.Gear);
    }

    /// <summary>A giant drawn finer: his beard and hair ragged in strands at their ends, the fur of the mantle and the kilt shaggy at its edges.</summary>
    static void GiantDetail(Sculpt s, Build b, byte hair, byte fur)
    {
        if (!s.Detail) return;
        int hy0 = b.Neck;
        for (int x = -4; x < 4; x++) for (int z = b.FaceZ * 2; z < b.HZ1 * 2; z++) if (Dice(x, z, 0, 161) < .5f) s.FSet(Body, x, (hy0 - 7) * 2 - 1, z, hair);
        for (int x = -b.Chest * 2; x < b.Chest * 2; x++)
            foreach (int z in new[] { b.Z0 * 2 - 2, b.Z1 * 2 + 1 })
            {
                if (Dice(x, z, 1, 162) < .5f) s.FSet(Body, x, (b.Neck - 4) * 2 - 1, z, fur);
                if (Dice(x, z, 2, 163) < .5f) s.FDel(Body, x, (b.Waist - 6) * 2, z);
            }
    }
}
