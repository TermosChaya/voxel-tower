using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>The horde: goblins, sappers, thieves, shamans, witches, necromancers, miners, orcs and trolls, and its engines.</summary>
public static partial class Bodies
{
    // ---------------------------------------------------------------- the horde's engines, finer

    /// <summary>
    /// A ram drawn finer: the iron head on its log a ram's head, a long muzzle with its eyes and nostrils and a horn
    /// curling round on either side; the hides over the roof in pieces with dark seams; skulls nailed to the front
    /// posts; a red hand daubed on the boards of its sides.
    /// </summary>
    static void RamDetail(Sculpt s, int look)
    {
        if (!s.Detail) return;
        s.FCut(Main, -4, 13, 30, 4, 21, 35);
        s.FBall(Main, 0, 17, 32.5f, 3.1f, 3.4f, 3.4f, Mat.Iron, Tissue.Gear);
        s.FBall(Main, 0, 16, 35f, 2.2f, 2.4f, 1.8f, Mat.Iron, Tissue.Gear);
        foreach (int sg in new[] { 1, -1 })
        {
            for (int i = 0; i <= 24; i++)
            {
                float k = i / 24f, an = k * MathF.PI * 1.7f, r = 3.2f - k * 1.6f;
                float y = 18.5f + r * MathF.Cos(an), z = 31.5f - r * MathF.Sin(an) + k * 1.5f;
                s.FBall(Main, sg * (3.4f + k * 1.2f), y, z, 1.1f - k * .45f, 1.1f - k * .45f, 1.1f - k * .45f, (i & 3) == 0 ? Mat.Rust : Mat.Iron, Tissue.Gear);
            }
            // an eye glowing in each side of the head, a nostril at the end of the muzzle
            for (int k = 10; k >= 0; k--) { int ex = sg > 0 ? k : -1 - k; if (s.FHas(Main, ex, 37, 68)) { s.FPut(Main, ex, 37, 68, Mat.Ember); break; } }
            for (int k = 80; k >= 60; k--) { int nx = sg > 0 ? 1 : -2; if (s.FHas(Main, nx, 31, k)) { s.FPut(Main, nx, 31, k, Mat.Hair); break; } }
        }
        // the hides in pieces, sewn with dark seams
        s.FPattern(Body, -80, 56, -80, 80, 80, 80, (x, y, z) => s.FMat(Body, x, y, z) is Mat.Leather or Mat.FurBoar && (z + 64) % 12 == 0 ? Mat.LeatherDark : (byte)0);
        s.FPattern(Body, -80, 56, -80, 80, 80, 80, (x, y, z) => s.FMat(Body, x, y, z) == (look == 0 ? Mat.Leather : Mat.FurBoar) && ((z + 64) / 12 & 1) == 1 ? Mat.Hide : (byte)0);
        // skulls on the front posts; a red hand on either side
        foreach (int x in new[] { -23, 22 }) Trophy(s, x, 40, 43);
        foreach (int sg in new[] { 1, -1 })
            foreach (var (dy, dz) in new[] { (0, 0), (1, 0), (0, 1), (1, 1), (2, -1), (3, -1), (2, 0), (3, 0), (2, 1), (3, 1), (2, 2), (3, 2), (4, 2), (4, 0), (5, 0), (4, -1), (4, 1), (5, 1), (5, 2) })
                s.FPut(Body, sg > 0 ? 27 : -28, 34 + dy, 6 + dz, Mat.WarpaintRed);
    }

    /// <summary>A skull nailed up, facing +z, its middle at (x, y, z) in fine cells: the dome, dark sockets, the teeth.</summary>
    static void Trophy(Sculpt s, int x, int y, int z)
    {
        s.FBall(Body, (x + .5f) / 2, (y + .5f) / 2, (z + .5f) / 2, 1.2f, 1.2f, 1.1f, Mat.Bone, Tissue.Gear);
        s.FPut(Body, x - 1, y, z + 2, Mat.Maw); s.FPut(Body, x + 1, y, z + 2, Mat.Maw);
        s.FPut(Body, x, y - 2, z + 2, Mat.Maw);
        s.FSet(Body, x, y + 3, z, Mat.Iron, Tissue.Gear);
    }

    /// <summary>A stone-thrower drawn finer: the joints of its frame bound in rope, iron bolts through them, a round stone in the cup.</summary>
    static void CatapultDetail(Sculpt s)
    {
        if (!s.Detail) return;
        foreach (int y in new[] { 30, 31, 50, 51 })
            s.FPattern(Body, -80, y, 14, 80, y + 1, 26, (x, yy, z) => s.FMat(Body, x, yy, z) == Mat.Wood && Math.Abs(x + .5f) > 17 ? Mat.Linen : (byte)0);
        foreach (var (x, y, z) in new[] { (-24, 15, -40), (23, 15, -40), (-24, 15, 40), (23, 15, 40), (-24, 54, 22), (23, 54, 22) }) s.FSet(Body, x, y, z, Mat.Iron);
        s.FCut(Main, -2, 14, -30, 2, 18, -26);
        s.FBall(Main, 0, 16, -28, 2.2f, 2.1f, 2.2f, Mat.Stone, Tissue.Gear);
        for (int z = -50; z < -2; z += 8) for (int x = -4; x < 4; x++) s.FPut(Main, x, 23, z, Mat.Linen);
    }

    /// <summary>A mantlet drawn finer: the seams between its planks, the iron band riveted, a skull hung on its front, a red daub.</summary>
    static void MantletDetail(Sculpt s)
    {
        if (!s.Detail) return;
        s.FPattern(Body, -80, 0, 15, 80, 80, 16, (x, y, z) => s.FMat(Body, x, y, z) is Mat.BoardDark or Mat.Wood && (x + 64) % 5 == 0 ? Mat.Char : (byte)0);
        s.FPattern(Body, -80, 60, -80, 80, 64, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Iron && (x + 64) % 4 == 0 && y == 61 ? Mat.Steel : (byte)0);
        Trophy(s, -12, 50, 19);
        for (int k = 0; k < 9; k++) foreach (int z in new[] { 15, 17 }) { s.FPut(Body, 8 + k / 2, 30 + k, z, Mat.WarpaintRed); s.FPut(Body, 9 + k / 2, 30 + k, z, Mat.WarpaintRed); }
    }

    /// <summary>A siege tower drawn finer: its hides in pieces with dark seams, skulls hung under the fighting top, red daubs on its front.</summary>
    static void TowerDetail(Sculpt s)
    {
        if (!s.Detail) return;
        s.FPattern(Body, -80, 16, -80, 80, 160, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Leather && ((y % 14 == 0) || (x + z + 64) % 16 == 0) ? Mat.LeatherDark : (byte)0);
        foreach (int x in new[] { -20, -6, 8, 22 }) Trophy(s, x, 154, 35);
        for (int k = 0; k < 12; k++) for (int w = 0; w < 2; w++) { s.FPut(Body, -10 + k, 100 + k / 2 + w, 31, Mat.WarpaintRed); s.FPut(Body, 10 - k, 100 + k / 2 + w, 31, Mat.WarpaintRed); }
    }

    // ---------------------------------------------------------------- the horde

    static readonly byte[] GoblinSkins = { Mat.SkinGoblin, Mat.SkinGoblinDark, Mat.SkinGoblinPale };
    static readonly byte[] Rags = { Mat.Rag, Mat.ClothBrown, Mat.ClothOchre, Mat.Rag };

    /// <summary>A goblin's build: short bandy legs and bare feet, a narrow body and a big head.</summary>
    static Build GoblinBuild(byte shirt, byte trousers, byte skin) => new()
    {
        Leg = 6, LegW = 2, LegD = 3, Gap = 1, Boot = 0, Torso = 6, Chest = 3, ChestD = 4, Head = 8, HeadW = 4, HeadD = 7, HeadZ = -3,
        Arm = 6, ArmW = 2, ArmD = 2, Hand = 2, Skin = skin, Shirt = shirt, Trousers = trousers, Boots = skin, Hair = skin, Brow = skin,
        Eye = Mat.EyeGlint, Sleeve = skin, HairStyle = 3,
    };

    /// <summary>A shade darker than a skin, for the hollows of it: inside an ear, between the ribs.</summary>
    static byte Shade(byte skin) => skin switch
    {
        Mat.SkinGoblinPale => Mat.SkinGoblin, Mat.SkinGoblin => Mat.SkinGoblinDark, Mat.SkinOrc or Mat.SkinOrcGrey => Mat.SkinTrollDark,
        Mat.SkinTroll => Mat.SkinTrollDark, _ => Mat.Lips,
    };

    /// <summary>
    /// Long ears sweeping out and up (or one torn short), a hooked nose, yellow eyes with black pupils, a wide mouth with
    /// teeth. Near: the ears pointed, their hollows dark, notched and ringed with brass on some; the eyes sunk in dark
    /// sockets under a brow sloping down to the nose, glowing, with slits for pupils; the nose long and hooked down
    /// over the lip; the grin full of needle teeth with its corners turned up, the jaw narrowing to the chin; and on the
    /// horde's footmen warpaint — chalk across the eyes, ochre down the cheeks, a chalk skull — or old scars.
    /// </summary>
    static void GoblinFace(Sculpt s, Build b, bool torn = false, int look = -1)
    {
        int hx = b.HeadW, hz1 = b.HZ1, hy0 = b.Neck, eye = hy0 + 4;
        s.Box(Body, -hx - (torn ? 2 : 4), eye, -1, -hx, eye + 1, 1, b.Skin);
        if (!torn) s.Box(Body, -hx - 5, eye + 1, -1, -hx - 3, eye + 2, 0, b.Skin);
        s.Box(Body, hx, eye, -1, hx + 4, eye + 1, 1, b.Skin);
        s.Box(Body, hx + 3, eye + 1, -1, hx + 5, eye + 2, 0, b.Skin);
        s.Paint(Body, -hx, eye - 1, -1, hx, eye + 3, hz1, b.Skin);
        s.Pair(Body, Body, -3, eye, b.FaceZ, -1, eye + 1, hz1, Mat.Brass);
        s.Pair(Body, Body, -2, eye, b.FaceZ, -1, eye + 1, hz1, Mat.Eye);
        s.Pair(Body, Body, -3, eye + 1, b.FaceZ, -1, eye + 2, hz1, b.Skin);
        s.Box(Body, -1, hy0 + 2, hz1, 1, eye + 1, hz1 + 1, b.Skin);
        s.Box(Body, -1, hy0 + 2, hz1 + 1, 1, hy0 + 3, hz1 + 2, b.Skin);
        s.Box(Body, -3, hy0 + 1, b.FaceZ, 3, hy0 + 2, hz1, Mat.Hair);
        s.Pair(Body, Body, -2, hy0 + 1, b.FaceZ, -1, hy0 + 2, hz1, Mat.Bone);
        if (!s.Detail) return;
        int F = hz1 * 2 - 1, E = eye * 2, H0 = hy0 * 2, W = hx * 2, M = (hy0 + 1) * 2;
        byte shade = Shade(b.Skin);
        // the jaw narrowing to the chin
        for (int z = -16; z < 16; z++)
            for (int x = -W; x < W; x++)
            {
                if (MathF.Abs(x + .5f) > W - 2.5f) s.FDel(Body, x, H0, z);
                if (MathF.Abs(x + .5f) > W - 1.5f) s.FDel(Body, x, H0 + 1, z);
            }
        // the ears
        s.FCut(Body, -hx - 6, eye, -1, -hx, eye + 2, 1, both: true);
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            int L = torn && sg > 0 ? 5 : 12;
            for (int d = 0; d <= L; d++)
            {
                int top = E + 2 + (int)(d * .32f), bottom = E - 1 + (int)(d * .6f);
                if (bottom > top) bottom = top;
                for (int y = bottom; y <= top; y++)
                {
                    bool edge = y == top || y == bottom || d == L, notch = !torn && look % 3 == 1 && y == top && d is 6 or 7;
                    if (notch) continue;
                    s.FSet(Body, X(-W - 1 - d), y, -1, b.Skin);
                    s.FSet(Body, X(-W - 1 - d), y, 0, !edge && d > 1 && d < L - 2 ? shade : b.Skin);
                }
            }
            // a brass ring through the lobe
            if (look >= 0 && look % 2 == 0 && !(torn && sg > 0))
            {
                int ry = E - 2;
                foreach (var (dx, dy) in new[] { (2, 0), (3, -1), (3, -2), (2, -3), (1, -2), (1, -1) }) s.FSet(Body, X(-W - 1 - dx), ry + dy, -1, Mat.Brass);
            }
            // the eye in its socket, the brow over it
            for (int y = E - 1; y < E + 3; y++) for (int x = -7; x < -2; x++) s.FPut(Body, X(x), y, F, Mat.Maw);
            for (int y = E; y < E + 2; y++)
                for (int x = -6; x < -3; x++) { s.FDel(Body, X(x), y, F); s.FSet(Body, X(x), y, F - 1, x == -5 ? Mat.Eye : Mat.EyeGlint); }
            for (int x = -8; x < -2; x++) s.FSet(Body, X(x), E + 3, F + 1, b.Skin);
            s.FSet(Body, X(-3), E + 2, F + 1, b.Skin); s.FSet(Body, X(-2), E + 2, F + 1, b.Skin);
        }
        // the nose, long and hooked down over the lip
        for (int y = M + 2; y < E + 2; y++) for (int x = -2; x < 2; x++) for (int z = F + 1; z < F + 5; z++) s.FDel(Body, x, y, z);
        for (int y = M + 3; y < E + 2; y++) { s.FSet(Body, -1, y, F + 1, b.Skin); s.FSet(Body, 0, y, F + 1, b.Skin); }
        for (int y = M + 3; y < M + 6; y++) { s.FSet(Body, -1, y, F + 2, b.Skin); s.FSet(Body, 0, y, F + 2, b.Skin); }
        for (int y = M + 2; y < M + 5; y++) { s.FSet(Body, -1, y, F + 3, b.Skin); s.FSet(Body, 0, y, F + 3, b.Skin); }
        s.FSet(Body, -1, M + 2, F + 4, b.Skin); s.FSet(Body, 0, M + 2, F + 4, b.Skin);
        s.FSet(Body, -2, M + 3, F + 1, shade); s.FSet(Body, 1, M + 3, F + 1, shade);
        // the grin: needle teeth top and bottom in a dark mouth, its corners turned up
        for (int x = -7; x < 7; x++) for (int y = M - 1; y < M + 3; y++) s.FPut(Body, x, y, F, b.Skin);
        for (int x = -5; x < 5; x++)
        {
            s.FPut(Body, x, M + 1, F, (x & 1) == 1 ? Mat.Bone : Mat.Maw);
            s.FPut(Body, x, M, F, Mat.Maw);
        }
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            s.FPut(Body, X(-6), M + 1, F, Mat.Maw); s.FPut(Body, X(-7), M + 2, F, Mat.Maw);
            // a fang up out of the lower jaw over the lip
            s.FPut(Body, X(-3), M, F, Mat.Bone); s.FSet(Body, X(-3), M + 1, F + 1, Mat.Bone);
        }
        if (look < 0) return;
        // warpaint, or scars
        var skin = b.Skin;
        bool Bare(int x, int y, int z) => s.FMat(Body, x, y, z) == skin;
        switch (look % 4)
        {
            case 0:
                s.FPattern(Body, -W, E - 2, 0, W, E + 3, F + 2, (x, y, z) => Bare(x, y, z) ? Mat.Warpaint : (byte)0);
                break;
            case 1:
                s.FPattern(Body, -W, M + 2, 0, W, E - 1, F + 2, (x, y, z) => Bare(x, y, z) && Math.Abs(x + .5f) is 3.5f or 5.5f ? Mat.WarpaintRed : (byte)0);
                break;
            case 2:
                s.FPattern(Body, -W, E - 3, 0, W, E + 4, F + 2, (x, y, z) => Bare(x, y, z) && Math.Abs(x + .5f) is > 1.5f and < 7.5f ? Mat.Warpaint : (byte)0);
                s.FPattern(Body, -W, H0 + 1, 0, W, M, F + 2, (x, y, z) => Bare(x, y, z) ? Mat.Warpaint : (byte)0);
                break;
            default:
                for (int k = 0; k < 9; k++) s.FPut(Body, -7 + k, E + 4 - k, F, Mat.Scar);
                break;
        }
    }

    /// <summary>
    /// Bare feet drawn finer: toes parted by grooves along the front of the foot, with dark claws at their tips.
    /// </summary>
    static void Claws(Sculpt s, Build b, byte claw)
    {
        if (!s.Detail) return;
        int lz1 = (b.LegD - b.LegD / 2) * 2, x0 = b.LegX0 * 2, x1 = b.LegX1 * 2;
        foreach (var (p, sg) in new[] { (LegR, 1), (LegL, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int x = x0; x < x1; x++)
            {
                bool groove = (x - x0) % 2 == 1 && x < x1 - 1;
                if (groove) { s.FDel(p, X(x), 1, lz1 + 1); continue; }
                s.FSet(p, X(x), 0, lz1 + 2, claw);
            }
        }
    }

    /// <summary>
    /// A goblin of the horde in its rags: a loincloth, a tuft of hair on the crown of some and a bone through it on one;
    /// near, its shirt torn at the hem, a string of finger bones round the neck, the ribs showing on one that goes
    /// bare, a little skull hung at the hip of some, claws on its bare feet; one wears a dented pot of a helmet far too
    /// big for it, one carries a pot lid for a shield.
    /// </summary>
    static BodyPlan Goblin(int look)
    {
        var f = new BodyPlan { Chop = .45f, Carry = -.3f, Stride = 6 };
        byte skin = GoblinSkins[look % 3];
        var b = GoblinBuild(look % 4 == 3 ? skin : Rags[look % 4], look % 3 == 2 ? Mat.Rag : Mat.Leather, skin);
        var s = Humanoid(b, f);
        GoblinFace(s, b, look % 4 == 3, look);
        // a ragged loincloth, a tuft of hair on the crown of some, a bone through it on one
        s.Box(Body, -2, b.Waist - 3, b.Z1, 2, b.Waist + 1, b.Z1 + 1, Rags[(look + 1) % 4]);
        s.Cut(Body, -2, b.Waist - 3, b.Z1, -1, b.Waist - 2, b.Z1 + 1, both: true);
        if (look % 2 == 0 && look != 6) s.Box(Body, -1, b.Crown, -1, 1, b.Crown + 2, 1, Mat.Hair);
        if (look == 5) s.Box(Body, -4, b.Crown, 0, 4, b.Crown + 1, 1, Mat.Bone);
        Claws(s, b, Mat.Horn);
        if (s.Detail)
        {
            int W = b.Chest * 2, waist = b.Waist * 2, neck = b.Neck * 2, z1 = b.Z1 * 2;
            // the hem of the shirt torn, the loincloth's too
            if (look % 4 != 3)
                for (int x = -W; x < W; x++)
                    if (Dice(x, look, 0, 81) < .45f) for (int z = -16; z < 16; z++) s.FPut(Body, x, waist + 2, z, b.Trousers);
            for (int x = -4; x < 4; x++) if (Dice(x, look, 1, 82) < .5f) { s.FDel(Body, x, (b.Waist - 3) * 2, z1); s.FDel(Body, x, (b.Waist - 3) * 2, z1 + 1); }
            // the tuft in strands, ragged
            if (look % 2 == 0 && look != 6)
                for (int x = -2; x < 2; x++) for (int z = -2; z < 2; z++) if (Dice(x, z, look, 83) < .4f) s.FDel(Body, x, b.Crown * 2 + 3, z);
            // a string of finger bones round the neck, worn on the skin and cut with it like the shirt: were it carried
            // gear, it would stop a blade at the throat as a haft does and, never cut, hold the head on
            for (int x = -W + 1; x < W - 1; x += 2)
            {
                int y = neck - 2 - (int)(MathF.Abs(x + .5f) < 3 ? 1 : 0);
                s.FSet(Body, x, y, z1, Mat.Bone); s.FSet(Body, x, y - 1, z1, Mat.Bone);
                s.FSet(Body, x + 1, y, z1, Mat.Linen);
            }
            // the ribs of one that goes bare
            if (look % 4 == 3)
                for (int y = waist + 4; y < neck - 3; y += 2)
                    for (int x = -W + 1; x < W - 1; x++) if (Math.Abs(x + .5f) > 1) s.FPut(Body, x, y, z1 - 1, Shade(skin));
            // a little skull at the hip
            if (look % 4 == 2)
            {
                int hx = W - 2, hy = waist - 3;
                s.FBall(Body, (hx + 1) / 2f, (hy + 1) / 2f, (z1 + 1.5f) / 2f, 1.4f, 1.2f, 1.2f, Mat.Bone, Tissue.Gear);
                s.FPut(Body, hx, hy + 1, z1 + 3, Mat.Maw); s.FPut(Body, hx + 2, hy + 1, z1 + 3, Mat.Maw);
                s.FSet(Body, hx + 1, hy + 4, z1 + 1, Mat.Linen, Tissue.Gear);
            }
        }
        // a dented pot of a helmet too big for it
        if (look == 6)
        {
            s.Paint(Body, -b.HeadW, b.Crown - 3, b.HeadZ, b.HeadW, b.Crown, b.HZ1, Mat.Iron, Tissue.Armor);
            s.Flat(Body, b.Crown - 3, 5.3f, Mat.Iron, Tissue.Armor);
            s.Flat(Body, b.Crown, 3.6f, Mat.Iron, Tissue.Armor);
            if (s.Detail)
            {
                for (int x = 1; x < 6; x++) for (int z = -4; z < 2; z++) { s.FDel(Body, x, b.Crown * 2 + 1, z); if (x > 2 && z > -3) s.FDel(Body, x, b.Crown * 2, z); }
                s.FPattern(Body, -14, (b.Crown - 3) * 2 - 1, -14, 14, b.Crown * 2 + 2, 14, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Iron && Dice(x >> 1, y >> 1, z >> 1, 84) < .3f ? Mat.Rust : (byte)0);
            }
        }
        // a pot lid on the left forearm
        if (look == 4)
        {
            var g = s.Hold(Off, DressKind.Shield, Tissue.Armor);
            float cx = (-b.ArmX0 - b.ArmW / 2f) * 4, cy = (b.HandY + b.Hand) * 4 + 2;
            int zp = b.ArmZ1 * 4 + 2;
            g.Fill((int)cx - 2, (int)cy - 6, b.ArmZ1 * 4, (int)cx + 2, (int)cy - 3, zp, Mat.Leather);
            for (int yy = (int)(cy - 11); yy <= cy + 11; yy++)
                for (int xx = (int)(cx - 11); xx <= cx + 11; xx++)
                {
                    float r = MathF.Sqrt((xx + .5f - cx) * (xx + .5f - cx) + (yy + .5f - cy) * (yy + .5f - cy));
                    if (r > 10.6f) continue;
                    byte m = Dice(xx >> 2, yy >> 2, 0, 85) < .3f ? Mat.Rust : Mat.Iron;
                    g.Dot(xx, yy, zp, m);
                    if (r > 9.4f || r < 6) g.Dot(xx, yy, zp + 1, m);
                    if (r < 2.2f) { g.Dot(xx, yy, zp + 2, Mat.Iron); g.Dot(xx, yy, zp + 3, Mat.Iron); }
                }
            g.Grip = (cx / 4, cy / 4, zp / 4f); g.Tip = (cx / 4, cy / 4, (zp + 3) / 4f); g.Edge = (0, 0, 1);
        }
        Knife(s, b);
        return Done(s, f, b);
    }

    static BodyPlan Sapper(int look)
    {
        var f = new BodyPlan { Chop = .55f, Stride = 6 };
        var b = GoblinBuild(Mat.Leather, Mat.Rag, GoblinSkins[look % 3]);
        var s = Humanoid(b, f);
        GoblinFace(s, b, false, look);
        Claws(s, b, Mat.Horn);
        // a cap, a length of ladder on the back, a mallet in the fist
        byte cap = new[] { Mat.Leather, Mat.ClothBrown, Mat.ClothGreen, Mat.ClothOchre }[look % 4];
        s.Paint(Body, -b.HeadW, b.Crown - 2, b.HeadZ, b.HeadW, b.Crown, b.HZ1, cap);
        s.Box(Body, -3, b.Crown, b.HeadZ + 1, 3, b.Crown + 1, b.HZ1 - 1, cap);
        s.Box(Body, -3, b.Waist, b.Z0 - 1, -2, b.Neck + 1, b.Z0, Mat.Handle, Tissue.Gear);
        s.Box(Body, 2, b.Waist, b.Z0 - 1, 3, b.Neck + 1, b.Z0, Mat.Handle, Tissue.Gear);
        for (int y = b.Waist + 1; y < b.Neck + 1; y += 4) s.Box(Body, -2, y, b.Z0 - 1, 2, y + 1, b.Z0, Mat.Handle, Tissue.Gear);
        Mallet(s, b);
        return Done(s, f, b);
    }

    /// <summary>A goblin in a dark hood and a cloak to the knees, an empty sack slung on its back, a knife in the fist.</summary>
    static BodyPlan Thief(int look)
    {
        var f = new BodyPlan { Chop = .45f, Carry = -.3f, Stride = 6 };
        byte cloak = new[] { Mat.ClothDark, Mat.ClothBrown, Mat.ClothGreen }[look % 3];
        var b = GoblinBuild(cloak, Mat.Leather, GoblinSkins[(look + 1) % 3]);
        var s = Humanoid(b, f);
        GoblinFace(s, b, look == 2);
        // the hood over the crown and down the back of the head, its peak behind
        s.Paint(Body, -b.HeadW, b.Crown - 3, b.HeadZ, b.HeadW, b.Crown, b.HZ1 - 1, cloak);
        s.Box(Body, -b.HeadW + 1, b.Crown, b.HeadZ, b.HeadW - 1, b.Crown + 1, b.HZ1 - 2, cloak);
        s.Box(Body, -1, b.Crown - 1, b.HeadZ - 1, 1, b.Crown + 1, b.HeadZ, cloak);
        // the cloak's skirt down over the legs, a little apart at the front
        s.Box(Body, -3, b.Waist - 3, b.Z0, 3, b.Waist, b.Z1, cloak);
        s.Cut(Body, -1, b.Waist - 3, b.Z1 - 1, 1, b.Waist - 1, b.Z1, both: true);
        // the sack on the back
        s.Box(Body, -3, b.Waist, b.Z0 - 3, 3, b.Neck, b.Z0, Mat.Sack, Tissue.Gear);
        s.Box(Body, -2, b.Neck, b.Z0 - 2, 2, b.Neck + 2, b.Z0, Mat.Sack, Tissue.Gear);
        Knife(s, b, old: false);
        return Done(s, f, b);
    }

    /// <summary>
    /// A goblin shaman: a skull worn over its face with the glint showing through the eye holes, a crest of red and
    /// ochre feathers over it, a cloak of hides, a necklace of teeth, and a crooked staff with a horn at its head and an
    /// ember glowing in the horn.
    /// </summary>
    static BodyPlan Shaman(int look)
    {
        var f = new BodyPlan { Chop = .9f, Carry = -.2f, Stride = 6 };
        var b = GoblinBuild(look == 0 ? Mat.ClothBrown : Mat.Rag, Mat.Leather, GoblinSkins[(look + 2) % 3]);
        var s = Humanoid(b, f);
        GoblinFace(s, b);
        int hx = b.HeadW, hz1 = b.HZ1, hy0 = b.Neck, eye = hy0 + 4;
        s.Box(Body, -hx + 1, hy0 + 1, hz1, hx - 1, b.Crown - 1, hz1 + 1, Mat.Bone, Tissue.Gear);
        s.Pair(Body, Body, -3, eye, hz1, -1, eye + 1, hz1 + 1, Mat.EyeGlint, Tissue.Gear);
        s.Box(Body, -1, hy0 + 1, hz1 + 1, 1, hy0 + 3, hz1 + 2, Mat.Bone, Tissue.Gear);
        for (int i = -2; i <= 2; i++) s.Box(Body, i, b.Crown, -1, i + 1, b.Crown + 3 - Math.Abs(i), 0, (i & 1) == 0 ? Mat.ClothRed : Mat.ClothOchre, Tissue.Gear);
        s.Box(Body, -b.Chest - 1, b.Waist - 2, b.Z0 - 1, b.Chest + 1, b.Neck + 1, b.Z0, Mat.FurBoar, Tissue.Gear);
        for (int x = -2; x <= 1; x++) s.Dot(Body, x, b.Neck - 1, b.Z1, Mat.Bone, Tissue.Gear);
        ShamanDetail(s, b, look);
        HornStaff(s, b);
        return Done(s, f, b);
    }

    /// <summary>
    /// A witch of the horde: an old orc woman in a long skirt of sacking and a shawl over her head and shoulders, grey
    /// hair hanging out from under it, a hooked nose and a tusk; a crooked staff with a bunch of herbs and a bone hung
    /// from its head.
    /// </summary>
    static BodyPlan Witch(int look)
    {
        var f = new BodyPlan { Chop = 1.1f, Carry = -.2f, Stride = 6 };
        byte skin = look == 1 ? Mat.SkinOrcGrey : Mat.SkinOrc, shawl = look == 1 ? Mat.ClothDark : Mat.ClothBrown;
        var b = new Build
        {
            Leg = 9, LegW = 3, LegD = 4, Gap = 1, Boot = 0, Torso = 9, Chest = 4, ChestD = 5, Head = 8, HeadW = 4, HeadD = 8, HeadZ = -3,
            Arm = 9, ArmW = 2, ArmD = 3, Hand = 3, Skin = skin, Shirt = shawl, Trousers = Mat.Rag, Boots = skin, Sleeve = shawl,
            Hair = Mat.HairGrey, HairStyle = 2, Eye = Mat.Ember, Brow = Mat.HairGrey, Mouth = Mat.Hair,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz1 = b.HZ1, hy0 = b.Neck;
        // a hooked nose, and a tusk out of the lower lip
        s.Box(Body, -1, hy0 + 3, hz1, 1, hy0 + 5, hz1 + 1, skin);
        s.Box(Body, -1, hy0 + 2, hz1 + 1, 0, hy0 + 3, hz1 + 2, skin);
        s.Box(Body, 1, hy0 + 1, hz1, 2, hy0 + 3, hz1 + 1, Mat.Bone);
        // the skirt to the ground, wider than the legs, and its ragged hem
        s.Box(Body, -b.Chest - 1, 1, b.Z0 - 1, b.Chest + 1, b.Waist, b.Z1 + 1, Mat.Rag);
        for (int x = -b.Chest - 1; x < b.Chest + 1; x += 3) s.Cut(Body, x, 1, b.Z1, x + 1, 2, b.Z1 + 1, both: false);
        // the shawl over the head and down over the shoulders, grey hair hanging out of it either side of the face
        Hood(s, b, shawl);
        s.Box(Body, -b.Chest - 1, b.Neck - 4, b.Z0 - 1, b.Chest + 1, b.Neck, b.Z1 + 1, shawl);
        s.Pair(Body, Body, -hx - 1, hy0 - 3, hz1 - 3, -hx, hy0 + 3, hz1 - 1, Mat.HairGrey);
        WitchDetail(s, b, look);
        HerbStaff(s, b);
        return Done(s, f, b);
    }

    /// <summary>
    /// A necromancer of the horde: a tall orc in a black robe to the ground and a hood drawn low, pale fire for eyes under
    /// it, small bones strung across its chest, and a staff topped with a skull with the same fire in its sockets.
    /// </summary>
    static BodyPlan Necromancer(int look)
    {
        var f = new BodyPlan { Chop = 1.2f, Carry = -.25f, Stride = 9 };
        byte skin = look == 1 ? Mat.SkinOrcGrey : Mat.SkinOrc, robe = Mat.ClothDark;
        var b = new Build
        {
            Leg = 12, LegW = 3, LegD = 5, Gap = 1, Boot = 2, Torso = 12, Chest = 5, ChestD = 6, Head = 8, HeadW = 4, HeadD = 8, HeadZ = -2,
            Arm = 12, ArmW = 3, ArmD = 4, Hand = 3, Skin = skin, Shirt = robe, Trousers = robe, Sleeve = robe, Boots = Mat.Leather, Belt = Mat.Bone,
            Eye = Mat.SoulFire, Brow = skin, Mouth = Mat.Hair, HairStyle = 3,
        };
        var s = Humanoid(b, f);
        int hy0 = b.Neck, hz1 = b.HZ1;
        // tusks out of the jaw
        s.Pair(Body, Body, -3, hy0 + 1, hz1, -2, hy0 + 3, hz1 + 1, Mat.Bone);
        // the robe to the ground with its hem dark as old blood, wide sleeves, the hood
        s.Box(Body, -b.Chest - 1, 1, b.Z0 - 1, b.Chest + 1, b.Waist, b.Z1 + 1, robe);
        s.Paint(Body, -b.Chest - 1, 1, b.Z0 - 1, b.Chest + 1, 2, b.Z1 + 1, Mat.Robe);
        s.Pair(Main, Off, b.ArmX0 - 1, b.HandY + b.Hand, b.ArmZ0 - 1, b.ArmX0, b.HandY + b.Hand + 4, b.ArmZ1 + 1, robe);
        Hood(s, b, robe);
        // a string of small bones across the chest
        for (int x = -b.Chest + 1; x < b.Chest - 1; x += 2) s.Dot(Body, x, b.Neck - 3 - Math.Abs(x) / 2, b.Z1, Mat.Bone, Tissue.Gear);
        NecroDetail(s, b, look);
        SkullStaff(s, b);
        return Done(s, f, b);
    }

    // ---------------------------------------------------------------- the horde's engines (in half-voxels, facing +z)

    /// <summary>Four wheels on axles under a frame of beams, the timber of an engine's base.</summary>
    static void Wheels(Sculpt s, int halfX, int frontZ, int backZ)
    {
        foreach (int x in new[] { -halfX - 2, halfX })
            foreach (int z in new[] { backZ, frontZ - 8 })
            {
                s.Box(Body, x, 0, z, x + 2, 8, z + 8, Mat.Wood);
                s.Box(Body, x, 3, z + 3, x + 2, 5, z + 5, Mat.Iron);
                s.Cut(Body, x, 0, z, x + 2, 1, z + 1, both: true);
                s.Cut(Body, x, 7, z, x + 2, 8, z + 1, both: true);
                s.Cut(Body, x, 0, z + 7, x + 2, 1, z + 8, both: true);
                s.Cut(Body, x, 7, z + 7, x + 2, 8, z + 8, both: true);
            }
        s.Box(Body, -halfX, 3, backZ + 3, halfX, 5, backZ + 5, Mat.Handle);
        s.Box(Body, -halfX, 3, frontZ - 5, halfX, 5, frontZ - 3, Mat.Handle);
    }

    /// <summary>A battering ram: a shed of posts and boards under a ridge of wet hides, on four wheels, and slung from its roof a log with an iron head that swings out before it.</summary>
    static BodyPlan RamPlan(int look)
    {
        var f = new BodyPlan { Chop = 1.2f, Stride = 40 };
        var s = new Sculpt();
        Wheels(s, 12, 22, -22);
        s.Box(Body, -12, 5, -22, 12, 7, 22, Mat.Wood);
        foreach (int x in new[] { -12, 10 })
            foreach (int z in new[] { -22, -2, 18 })
                s.Box(Body, x, 7, z, x + 2, 26, z + 3, Mat.Wood);
        // the roof: boards, and hides over them against fire
        byte hide = look == 0 ? Mat.Leather : Mat.FurBoar;
        s.Box(Body, -14, 26, -23, 14, 28, 23, Mat.Board);
        s.Box(Body, -11, 28, -23, 11, 31, 23, hide);
        s.Box(Body, -6, 31, -23, 6, 34, 23, hide);
        s.Box(Body, -2, 34, -23, 2, 36, 23, hide);
        // boards down the sides, a gap at the foot
        s.Box(Body, -14, 12, -20, -12, 26, 20, Mat.BoardDark);
        s.Box(Body, 12, 12, -20, 14, 26, 20, Mat.BoardDark);
        // the log on its chains, its iron head out before the shed
        s.Box(Main, -1, 19, -12, 1, 26, -10, Mat.Iron, Tissue.Gear);
        s.Box(Main, -1, 19, 10, 1, 26, 12, Mat.Iron, Tissue.Gear);
        s.Box(Main, -3, 14, -22, 3, 20, 30, Mat.Trunk, Tissue.Gear);
        s.Box(Main, -4, 13, 30, 4, 21, 35, Mat.Iron, Tissue.Gear);
        RamDetail(s, look);
        Joint(f, Main, 0, 26 / H, 0);
        f.HeadY0 = 26; f.HeadY1 = 36;
        return s.Into(f, 8, 26, 12);
    }

    /// <summary>A stone-thrower: a frame of beams on wheels, two posts with a padded beam across them before, and between them the arm, cocked back, a sling-cup of hide at its end with a stone in it.</summary>
    static BodyPlan CatapultPlan(int look)
    {
        var f = new BodyPlan { Chop = 1, Stride = 40 };
        var s = new Sculpt();
        Wheels(s, 12, 22, -22);
        s.Box(Body, -12, 5, -22, -8, 9, 22, Mat.Wood);
        s.Box(Body, 8, 5, -22, 12, 9, 22, Mat.Wood);
        s.Box(Body, -12, 5, -22, 12, 9, -18, Mat.Wood);
        s.Box(Body, -12, 5, 18, 12, 9, 22, Mat.Wood);
        // the posts and the beam the arm strikes, padded with straw
        s.Box(Body, -12, 9, 8, -9, 30, 12, Mat.Wood);
        s.Box(Body, 9, 9, 8, 12, 30, 12, Mat.Wood);
        s.Box(Body, -12, 27, 8, 12, 30, 12, Mat.Board);
        s.Box(Body, -6, 25, 8, 6, 27, 12, look == 0 ? Mat.Straw : Mat.Sack);
        // the winch at the back, the skein of rope round the axle at the arm's foot
        s.Box(Body, -8, 9, -20, 8, 11, -17, Mat.Handle);
        s.Box(Body, -8, 8, -6, 8, 12, -2, Mat.Rag);
        // the arm, lying back from its axle, the cup and the stone at its end
        s.Box(Main, -2, 9, -26, 2, 12, -2, Mat.Trunk, Tissue.Gear);
        s.Box(Main, -4, 10, -32, 4, 14, -24, Mat.Leather, Tissue.Gear);
        s.Box(Main, -2, 14, -30, 2, 18, -26, Mat.Stone, Tissue.Gear);
        CatapultDetail(s);
        Joint(f, Main, 0, 10 / H, -4 / H);
        f.HeadY0 = 24; f.HeadY1 = 30;
        return s.Into(f, 8, 26, 12);
    }

    /// <summary>A mantlet: a wall of upright planks a little over a man's height, an iron band along its top and a slit to look through, on two wheels, the handles to push it by behind.</summary>
    static BodyPlan MantletPlan(int look)
    {
        var f = new BodyPlan { Chop = 1, Stride = 40 };
        var s = new Sculpt();
        byte plank = look == 0 ? Mat.BoardDark : Mat.Wood;
        s.Box(Body, -18, 3, 4, 18, 32, 8, plank);
        for (int x = -16; x < 18; x += 5) s.Box(Body, x, 3, 8, x + 1, 32, 9, Mat.Wood);
        s.Box(Body, -18, 30, 3, 18, 32, 9, Mat.Iron);
        s.Cut(Body, -6, 22, 4, 6, 23, 8, both: true);
        foreach (int x in new[] { -20, 18 })
        {
            s.Box(Body, x, 0, 0, x + 2, 8, 8, Mat.Wood);
            s.Box(Body, x, 3, 3, x + 2, 5, 5, Mat.Iron);
        }
        s.Box(Body, -18, 3, 2, 18, 5, 4, Mat.Handle);
        s.Box(Body, -12, 12, -10, -10, 14, 4, Mat.Handle);
        s.Box(Body, 10, 12, -10, 12, 14, 4, Mat.Handle);
        MantletDetail(s);
        f.HeadY0 = 22; f.HeadY1 = 32;
        return s.Into(f, 8, 26, 12);
    }

    /// <summary>A siege tower: posts and boards five metres up on a frame with six wheels, hides hung over its front and sides against fire, a fighting top with merlons, and a flap at the top to drop onto a wall.</summary>
    static BodyPlan TowerPlan()
    {
        var f = new BodyPlan { Chop = 1, Stride = 40 };
        var s = new Sculpt();
        foreach (int x in new[] { -18, 16 })
            foreach (int z in new[] { -18, -4, 10 })
            {
                s.Box(Body, x, 0, z, x + 2, 8, z + 8, Mat.Wood);
                s.Box(Body, x, 3, z + 3, x + 2, 5, z + 5, Mat.Iron);
            }
        s.Box(Body, -16, 4, -18, 16, 8, 18, Mat.Wood);
        // the corner posts and the boarded sides, hides over the front and the flanks
        foreach (int x in new[] { -16, 13 })
            foreach (int z in new[] { -16, 13 })
                s.Box(Body, x, 8, z, x + 3, 84, z + 3, Mat.Wood);
        s.Box(Body, -16, 8, -16, 16, 80, -14, Mat.BoardDark);
        s.Box(Body, -16, 8, -16, -14, 80, 16, Mat.Leather);
        s.Box(Body, 14, 8, -16, 16, 80, 16, Mat.Leather);
        s.Box(Body, -16, 8, 14, 16, 70, 16, Mat.Leather);
        for (int y = 20; y < 80; y += 14) s.Box(Body, -16, y, 14, 16, y + 2, 17, Mat.Wood);
        // the fighting top: a floor, merlons round it, the flap hinged at its front edge
        s.Box(Body, -17, 80, -17, 17, 82, 17, Mat.Board);
        for (int x = -17; x < 17; x += 8)
        {
            s.Box(Body, x, 82, -17, x + 4, 88, -15, Mat.Board);
            s.Box(Body, -17, 82, x, -15, 88, x + 4, Mat.Board);
            s.Box(Body, 15, 82, x, 17, 88, x + 4, Mat.Board);
        }
        s.Box(Body, -12, 70, 16, 12, 72, 30, Mat.Board);
        TowerDetail(s);
        f.HeadY0 = 80; f.HeadY1 = 88;
        return s.Into(f, 8, 26, 12);
    }

    static BodyPlan Miner(int look)
    {
        var f = new BodyPlan { Chop = .8f, Stride = 6 };
        byte skin = look % 2 == 1 ? Mat.SkinOrcGrey : Mat.SkinOrc;
        var b = new Build
        {
            Leg = 6, LegW = 3, LegD = 4, Gap = 1, Boot = 2, Torso = 8, Chest = 4, ChestD = 4, Head = 8, HeadW = 4, HeadD = 8, HeadZ = -4,
            Arm = 8, ArmW = 2, ArmD = 3, Hand = 3, Skin = skin, Shirt = Mat.Leather, Trousers = Mat.Rag, Boots = Mat.Iron, Eye = Mat.EyeGlint, Brow = skin,
            Sleeve = Mat.Rag, HairStyle = 3,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy1 = b.Crown;
        // a brass helmet with a candle burning on its brow, a great beard, a sack of ore on the back
        s.Paint(Body, -hx, hy1 - 3, hz0, hx, hy1, hz1, Mat.Brass, Tissue.Armor);
        s.Rim(Body, hy1 - 3, -hx, hz0, hx, hz1, Mat.Brass, Tissue.Armor);
        s.Box(Body, -1, hy1 - 2, hz1, 1, hy1, hz1 + 1, Mat.Iron, Tissue.Gear);
        s.Box(Body, -1, hy1, hz1 - 1, 1, hy1 + 1, hz1 + 1, Mat.Ember, Tissue.Gear);
        MinerDetail(s, b);
        Beard(s, b, new[] { Mat.Hair, Mat.HairGinger, Mat.HairGrey, Mat.HairBrown }[look % 4]);
        s.Box(Body, -3, b.Waist, b.Z0 - 3, 3, b.Neck, b.Z0, Mat.Linen, Tissue.Gear);
        s.Box(Body, -2, b.Neck, b.Z0 - 2, 2, b.Neck + 1, b.Z0 - 1, Mat.Leather, Tissue.Gear);
        Pick(s, b, Mat.Iron, 10);
        return Done(s, f, b);
    }

    /// <summary>
    /// Horns curving out of a head's sides and up, from the cell (x, y) at its left or right side (fine coordinates),
    /// tapering, their tips bone: an orc's helm's, a shaman's mask's.
    /// </summary>
    static void Horns(Sculpt s, float x, float y, float z, float len, float rise, float thick, Tissue t)
    {
        int n = (int)MathF.Ceiling(len);
        foreach (int sg in new[] { 1, -1 })
            for (int i = 0; i <= n; i++)
            {
                float k = i / (float)n, an = k * MathF.PI * .62f;
                float hx = x - len * .55f * MathF.Sin(an) * 1.1f, hy = y + rise * (1 - MathF.Cos(an)) * 1.4f, r = thick * (1 - k * .8f) / 2 + .26f;
                s.FBall(Body, (sg > 0 ? hx : -hx) / 2, hy / 2, z / 2, r, r, r, k > .8f ? Mat.Bone : Mat.Horn, t);
            }
    }

    /// <summary>
    /// A shaman's mask and finery drawn finer: the mask a skull, its corners rounded, the glint deep in dark eye holes,
    /// a hole for the nose, cheekbones, a row of teeth; horns out of its brow (antlers on the other look); the crest's
    /// feathers apart with dark tips; strings of beads on the chest over a rune that glows; the hides' hem ragged.
    /// </summary>
    static void ShamanDetail(Sculpt s, Build b, int look)
    {
        if (!s.Detail) return;
        int hx = b.HeadW, hz1 = b.HZ1, hy0 = b.Neck, E = (hy0 + 4) * 2, z = hz1 * 2, x0 = (-hx + 1) * 2, y0 = (hy0 + 1) * 2, y1 = (b.Crown - 1) * 2;
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            foreach (int y in new[] { y0, y1 - 1 }) { s.FDel(Body, X(x0), y, z); s.FDel(Body, X(x0), y, z + 1); }
            for (int y = E - 1; y < E + 3; y++) for (int x = x0; x < x0 + 4; x++) { s.FPut(Body, X(x), y, z + 1, Mat.Maw); s.FPut(Body, X(x), y, z, Mat.Maw); }
            s.FDel(Body, X(x0 + 1), E, z + 1); s.FDel(Body, X(x0 + 2), E, z + 1); s.FDel(Body, X(x0 + 1), E + 1, z + 1); s.FDel(Body, X(x0 + 2), E + 1, z + 1);
            s.FPut(Body, X(x0 + 1), E, z, Mat.EyeGlint); s.FPut(Body, X(x0 + 2), E + 1, z, Mat.EyeGlint);
            for (int x = x0; x < x0 + 3; x++) s.FSet(Body, X(x), E - 3, z + 2, Mat.Bone, Tissue.Gear);
        }
        s.FPut(Body, -1, E - 3, z + 1, Mat.Maw); s.FPut(Body, 0, E - 3, z + 1, Mat.Maw); s.FPut(Body, -1, E - 2, z + 1, Mat.Maw); s.FPut(Body, 0, E - 2, z + 1, Mat.Maw);
        for (int x = -5; x < 5; x++) s.FPut(Body, x, y0 + 1, z + 1, (x & 1) == 0 ? Mat.Bone : Mat.Maw);
        if (look == 0) Horns(s, x0 - .5f, y1 - 2, z + 1, 9, 5, 2.4f, Tissue.Gear);
        else
            foreach (int sg in new[] { 1, -1 })
            {
                int X(int x) => sg > 0 ? x : -1 - x;
                float bx = X(x0) + .5f, by = y1 - .5f, bz = z + .5f, o = sg > 0 ? -1 : 1;
                s.FLine(Body, bx / 2, by / 2, bz / 2, (bx + o * 3) / 2, (by + 9) / 2, bz / 2, Mat.Bone, Tissue.Gear);
                s.FLine(Body, (bx + o * 1) / 2, (by + 4) / 2, bz / 2, (bx + o * 5) / 2, (by + 6) / 2, bz / 2, Mat.Bone, Tissue.Gear);
                s.FLine(Body, (bx + o * 3) / 2, (by + 9) / 2, bz / 2, (bx + o * 2) / 2, (by + 13) / 2, (bz - 2) / 2, Mat.Bone, Tissue.Gear);
                s.FLine(Body, (bx + o * 3) / 2, (by + 9) / 2, bz / 2, (bx + o * 6) / 2, (by + 11) / 2, bz / 2, Mat.Bone, Tissue.Gear);
            }
        // the feathers of the crest apart, dark at their tips
        int top = b.Crown * 2;
        for (int x = -6; x < 6; x++)
        {
            if ((x & 1) == 1) { for (int y = top; y < top + 8; y++) { s.FDel(Body, x, y, -2); s.FDel(Body, x, y, -1); } continue; }
            int h = 7 - (int)MathF.Abs(x + .5f) + 1;
            for (int y = top + h - 1; y < top + h + 1; y++) { s.FPut(Body, x, y, -2, Mat.Hair); s.FPut(Body, x, y, -1, Mat.Hair); }
        }
        // the strings of beads, the rune under them
        int cz = b.Z1 * 2, neck = b.Neck * 2;
        for (int k = 0; k < 10; k++)
        {
            int bx = -5 + k, by = neck - 3 - (int)(4 * MathF.Sin(MathF.PI * k / 9f));
            s.FSet(Body, bx, by, cz, k % 3 == 0 ? Mat.ClothRed : k % 3 == 1 ? Mat.Bone : Mat.Brass, Tissue.Gear);
        }
        foreach (var (x, y) in new[] { (0, 0), (0, 1), (0, 2), (0, 3), (0, 4), (-1, 3), (-2, 4), (1, 3), (2, 4), (-1, 1), (1, 1) })
            s.FPut(Body, x - 1 + 1, b.Waist * 2 + 3 + y, cz - 1, Mat.EyeGlint);
        for (int x = -b.Chest * 2 - 2; x < b.Chest * 2 + 2; x++) if (Dice(x, look, 0, 111) < .5f) { s.FDel(Body, x, (b.Waist - 2) * 2, b.Z0 * 2 - 1); s.FDel(Body, x, (b.Waist - 2) * 2, b.Z0 * 2 - 2); }
    }

    /// <summary>
    /// A witch drawn finer: the eyes glowing in their sockets, a wart on the hooked nose, grey hair hanging in strands
    /// from under the shawl, the shawl fringed, bunches of herbs and a pouch at her belt, her skirt's hem in tatters.
    /// </summary>
    static void WitchDetail(Sculpt s, Build b, int look)
    {
        if (!s.Detail) return;
        int hy0 = b.Neck, F = b.HZ1 * 2 - 1, E = (hy0 + 3) * 2, W = b.HeadW * 2;
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = E; y < E + 3; y++) for (int x = -7; x < -3; x++) s.FPut(Body, X(x), y, F, Mat.Maw);
            s.FDel(Body, X(-6), E + 1, F); s.FDel(Body, X(-5), E + 1, F); s.FSet(Body, X(-6), E + 1, F - 1, Mat.Ember); s.FSet(Body, X(-5), E + 1, F - 1, Mat.Ember);
            // strands of grey hair out of the shawl
            for (int z = F - 8; z < F - 1; z += 2)
            {
                int len = 4 + (int)(Dice(z, sg, look, 121) * 6);
                for (int k = 0; k < len; k++) s.FSet(Body, X(-W - 1 - (k > len - 3 ? 1 : 0)), hy0 * 2 - 6 - k + 6, z, Mat.HairGrey);
            }
        }
        // the nose drawn out and hooked down, a wart on it
        for (int k = 0; k < 4; k++) { s.FSet(Body, -1, (hy0 + 3) * 2 + 1 - k, F + 3 + k / 2, b.Skin); s.FSet(Body, 0, (hy0 + 3) * 2 + 1 - k, F + 3 + k / 2, b.Skin); }
        s.FSet(Body, -1, (hy0 + 3) * 2 - 3, F + 4, b.Skin); s.FSet(Body, 0, (hy0 + 3) * 2 - 3, F + 4, b.Skin);
        s.FSet(Body, 1, (hy0 + 4) * 2, F + 2, Mat.Crag);
        // the grey hair beside the face in strands
        foreach (int sg in new[] { 1, -1 })
            for (int y = (hy0 - 3) * 2; y < (hy0 + 3) * 2; y++)
                for (int z = (b.HZ1 - 3) * 2; z < (b.HZ1 - 1) * 2; z++)
                    if ((z & 1) == 1 && y < (hy0 + 1) * 2 || y < (hy0 - 3) * 2 + 1 + (int)(Dice(z, sg, look, 126) * 4))
                    {
                        s.FDel(Body, sg > 0 ? -2 * b.HeadW - 2 : 2 * b.HeadW + 1, y, z);
                        if (y < (hy0 - 1) * 2) s.FDel(Body, sg > 0 ? -2 * b.HeadW - 1 : 2 * b.HeadW, y, z);
                    }
        // her back bowed under the shawl
        s.FBall(Body, 0, b.Neck - 2.5f, b.Z0 - .6f, b.Chest - .5f, 2.4f, 1.4f, b.Shirt);
        // the shawl's fringe, the skirt's tatters
        int sy = (b.Neck - 4) * 2;
        for (int x = -b.Chest * 2 - 2; x < b.Chest * 2 + 2; x++)
            foreach (int z in new[] { (b.Z1 + 1) * 2 - 1, (b.Z0 - 1) * 2 })
            {
                if ((x & 1) == 0) s.FSet(Body, x, sy - 1, z, b.Shirt); else s.FDel(Body, x, sy, z);
                if (Dice(x, z, look, 122) < .4f) s.FDel(Body, x, 2, z);
            }
        // herbs at the belt on the right, a pouch on the left
        int wy = b.Waist * 2, cz = (b.Z1 + 1) * 2;
        for (int i = 0; i < 14; i++)
        {
            int hx = -b.Chest * 2 + (int)(Dice(i, 1, look, 123) * 5), hy = wy - 1 - (int)(Dice(i, 2, look, 124) * 7), hz = cz - 1 + (int)(Dice(i, 3, look, 125) * 2);
            s.FSet(Body, hx, hy, hz, i % 5 == 0 ? Mat.Bloom : i % 4 == 0 ? Mat.Flower : Mat.FoliageLight, Tissue.Gear);
        }
        for (int y = wy - 6; y < wy; y++) for (int x = b.Chest * 2 - 4; x < b.Chest * 2; x++) { s.FSet(Body, x, y, cz, Mat.Leather, Tissue.Gear); if (y > wy - 3) s.FSet(Body, x, y, cz + 1, Mat.LeatherDark, Tissue.Gear); }
        for (int x = -b.Chest * 2 - 2; x < b.Chest * 2 + 2; x++) s.FSet(Body, x, wy, cz, Mat.Linen, Tissue.Gear);
    }

    /// <summary>
    /// A necromancer drawn finer: the face lost in the shadow of the hood but for the pale fire of its eyes and its
    /// tusks, a mantle of bones over the shoulders, runes burning down the front of the robe and round its hem, the
    /// hands gone to bone, and small skulls strung at its girdle.
    /// </summary>
    static void NecroDetail(Sculpt s, Build b, int look)
    {
        if (!s.Detail) return;
        int hy0 = b.Neck, F = b.HZ1 * 2 - 1, E = (hy0 + 3) * 2, W = b.HeadW * 2;
        byte skin = b.Skin;
        s.FPattern(Body, -W, hy0 * 2, F - 2, W, b.Crown * 2, F + 3, (x, y, z) => s.FMat(Body, x, y, z) == skin ? (y >= E - 1 ? Mat.Hair : Shade(skin)) : (byte)0);
        // the mantle of bones over the shoulders, worn and cut like the robe (as carried gear it would hold the head on)
        int ny = (b.Neck - 1) * 2, cz0 = b.Z0 * 2 - 1, cz1 = b.Z1 * 2;
        for (int x = -b.Chest * 2 - 1; x <= b.Chest * 2; x++)
            for (int z = cz0; z <= cz1; z++)
            {
                bool rim = x == -b.Chest * 2 - 1 || x == b.Chest * 2 || z == cz0 || z == cz1;
                if (!rim || ((x + z) & 1) == 1) continue;
                s.FSet(Body, x, ny, z, Mat.Bone);
                if (((x + z) & 3) == 0) s.FSet(Body, x, ny - 1, z, Mat.Bone);
            }
        // runes burning down the front and round the hem
        var glyphs = new[] { new[] { (0, 0), (0, 1), (0, 2), (0, 3), (1, 2), (1, 1) }, new[] { (0, 0), (0, 1), (0, 2), (0, 3), (-1, 3), (1, 3) }, new[] { (0, 0), (1, 1), (0, 2), (1, 3), (0, 1) } };
        int front = (b.Z1 + 1) * 2 - 1;
        for (int i = 0; i < 4; i++)
            foreach (var (gx, gy) in glyphs[i % 3]) s.FPut(Body, gx - 1 + (i & 1), 6 + i * 6 + gy, front, Mat.SoulFire);
        for (int x = -b.Chest * 2; x < b.Chest * 2; x += 5)
            foreach (var (gx, gy) in glyphs[(x + 20) % 3]) s.FPut(Body, x + gx, 4 + gy, front, Mat.SoulFire);
        // the hands of bone, the fingers apart
        int hx0 = b.ArmX0 * 2, hx1 = -b.Chest * 2, hy = b.HandY * 2, hz0 = b.ArmZ0 * 2, hz1 = b.ArmZ1 * 2;
        foreach (var p in new[] { Main, Off })
            s.FPattern(p, -80, hy, -80, 80, hy + b.Hand * 2, 80, (x, y, z) => s.FMat(p, x, y, z) == skin ? (((z - hz0) % 2 == 1 && y < hy + 3) ? Mat.Maw : Mat.Bone) : (byte)0);
        // small skulls at the girdle
        int wy = b.Waist * 2;
        foreach (int x in new[] { -6, -1, 4 })
        {
            s.FBall(Body, (x + 1) / 2f, (wy - 2.5f) / 2, (front + 1.5f) / 2, 1.1f, 1f, 1f, Mat.Bone, Tissue.Gear);
            s.FPut(Body, x, wy - 2, front + 3, Mat.Maw); s.FPut(Body, x + 2, wy - 2, front + 3, Mat.Maw);
        }
    }

    /// <summary>
    /// The dead raised, drawn finer: the skull with its sockets deep and the fire in them, the hole of the nose, the jaw
    /// apart from the teeth, the ribs with the dark between them, the spine down the back in its knuckles, the limbs
    /// thinned to bones with knobs at the elbows and knees; and on the one with a helmet, rusted mail left on its shoulders.
    /// </summary>
    static void BonesDetail(Sculpt s, Build b, int look, byte fire)
    {
        if (!s.Detail) return;
        int hy0 = b.Neck, F = b.HZ1 * 2 - 1, E = (hy0 + 3) * 2, H0 = hy0 * 2;
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = E; y < E + 4; y++) for (int x = -6; x < -2; x++) { s.FDel(Body, X(x), y, F); s.FPut(Body, X(x), y, F - 1, x is -5 or -4 && y > E && y < E + 3 ? fire : Mat.Maw); }
            s.FSet(Body, X(-7), E - 1, F + 1, Mat.Bone); s.FSet(Body, X(-6), E - 1, F + 1, Mat.Bone);
        }
        s.FPut(Body, -1, E - 2, F, Mat.Maw); s.FPut(Body, 0, E - 2, F, Mat.Maw); s.FPut(Body, -1, E - 3, F, Mat.Maw); s.FPut(Body, 0, E - 3, F, Mat.Maw);
        for (int x = -5; x < 5; x++) { s.FPut(Body, x, H0 + 4, F, (x & 1) == 0 ? Mat.Bone : Mat.Maw); s.FPut(Body, x, H0 + 3, F, Mat.Maw); s.FPut(Body, x, H0 + 2, F, Mat.Bone); }
        // the ribs and the dark between them, front and sides; the spine's knuckles behind
        int w = b.Chest * 2, z0 = b.Z0 * 2, z1 = b.Z1 * 2;
        for (int y = (b.Waist + 3) * 2; y < (b.Neck - 1) * 2; y++)
        {
            if ((y & 1) == 0) continue;
            for (int x = -w; x < w; x++)
            {
                if (Math.Abs(x + .5f) < 1) continue;
                s.FDel(Body, x, y, z1 - 1); s.FPut(Body, x, y, z1 - 2, Mat.Maw);
            }
            for (int z = z0 + 1; z < z1 - 1; z++) foreach (int x in new[] { -w, w - 1 }) { s.FDel(Body, x, y, z); s.FPut(Body, x + (x < 0 ? 1 : -1), y, z, Mat.Maw); }
        }
        for (int y = b.Waist * 2; y < b.Neck * 2; y += 2) { s.FSet(Body, -1, y, z0 - 1, Mat.Bone); s.FSet(Body, 0, y, z0 - 1, Mat.Bone); }
        // the limbs thinned to bones, knobs left at the elbows and the knees
        int ax0 = b.ArmX0 * 2, ax1 = -b.Chest * 2, az0 = b.ArmZ0 * 2, az1 = b.ArmZ1 * 2, elbow = b.Neck + b.HandY + b.Hand;
        foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = (b.HandY + b.Hand) * 2; y < (b.Neck - 1) * 2; y++)
            {
                if (Math.Abs(y - elbow) < 2) continue;
                for (int z = az0; z < az1; z++) for (int x = ax0; x < ax1; x++)
                        if (x == ax0 || z == az0 || z == az1 - 1) s.FDel(p, X(x), y, z);
            }
        }
        int lx0 = b.LegX0 * 2, lx1 = b.LegX1 * 2, lz0 = -(b.LegD / 2) * 2, lz1 = (b.LegD - b.LegD / 2) * 2, knee = b.Leg;
        foreach (var (p, sg) in new[] { (LegR, 1), (LegL, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = 2; y < b.Leg * 2 - 2; y++)
            {
                if (Math.Abs(y - knee) < 2) continue;
                for (int z = lz0; z < lz1; z++) for (int x = lx0; x < lx1; x++)
                        if (x == lx0 || z == lz0 || z == lz1 - 1) s.FDel(p, X(x), y, z);
            }
        }
        if (look == 1)
            s.FPattern(Body, -80, (b.Neck - 3) * 2, -80, 80, b.Neck * 2, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Bone && Dice(x >> 1, y, z >> 1, 131) < .6f ? (Dice(x, y, z, 132) < .3f ? Mat.Rust : Mat.Mail) : (byte)0);
    }

    /// <summary>A miner's brass helmet drawn finer: its rim riveted, a candle of tallow in an iron cup on its brow, burning.</summary>
    static void MinerDetail(Sculpt s, Build b)
    {
        if (!s.Detail) return;
        int hz1 = b.HZ1, hy1 = b.Crown, rim = (hy1 - 3) * 2, F = hz1 * 2;
        s.FPattern(Body, -80, rim, -80, 80, rim + 2, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Brass && (x + z + 64) % 4 == 0 && y == rim + 1 ? Mat.Iron : (byte)0);
        s.FCut(Body, -1, hy1, hz1 - 1, 1, hy1 + 1, hz1 + 1);
        for (int y = hy1 * 2 - 3; y < hy1 * 2; y++) for (int x = -2; x < 2; x++) s.FSet(Body, x, y, F + 1, Mat.Iron, Tissue.Gear);
        for (int y = hy1 * 2; y < hy1 * 2 + 3; y++) { s.FSet(Body, -1, y, F + 1, Mat.Linen, Tissue.Gear); s.FSet(Body, 0, y, F + 1, Mat.Linen, Tissue.Gear); }
        s.FSet(Body, -1, hy1 * 2 + 3, F + 1, Mat.Ember, Tissue.Gear); s.FSet(Body, 0, hy1 * 2 + 3, F + 1, Mat.Ember, Tissue.Gear); s.FSet(Body, -1, hy1 * 2 + 4, F + 1, Mat.Ember, Tissue.Gear);
    }

    /// <summary>
    /// An orc of the horde: a heavy brow, a broad flat nose, a jaw thrust out with tusks rising from it, ears standing
    /// out; a sash or a chain across the chest, iron on the shoulders with spikes, a kilt of leather strips; a topknot,
    /// or an iron cap with horns. Near: eyes glowing deep under the scowling brow, the nostrils flared, a brass ring
    /// through the nose of some, the tusks curving up and out of the lower jaw past small teeth, ears pointed back and
    /// ringed; scars over the eye of some; a crest of hair over a shaved scalp inked with spirals, or the iron cap riveted
    /// with great horns curving out of it; a breastplate of rusted iron, its edges ragged, riveted over the jerkin; the
    /// pauldrons ragged and riveted, horn spikes on them; the kilt of hide, fur at its belt; ink round the bare arms.
    /// </summary>
    static BodyPlan Orc(int look)
    {
        var f = new BodyPlan { Chop = .95f, Carry = -.3f, Stride = 10 };
        byte skin = look % 2 == 1 ? Mat.SkinOrcGrey : Mat.SkinOrc;
        var b = new Build
        {
            Leg = 12, LegW = 4, LegD = 5, Gap = 1, Boot = 3, Torso = 12, Chest = 6, ChestD = 7, Head = 8, HeadW = 4, HeadD = 8, HeadZ = -2,
            Arm = 12, ArmW = 4, ArmD = 4, Hand = 4, Skin = skin, Shirt = Mat.Leather, Trousers = Mat.ClothDark, Boots = Mat.Leather,
            Eye = Mat.Ember, Brow = skin, Mouth = Mat.Hair, Sleeve = skin, HairStyle = 3,
        };
        var s = Humanoid(b, f);
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        // a heavy brow, a broad flat nose, a jaw thrust out with tusks rising from it
        s.Box(Body, -hx + 1, hy0 + 5, hz1, hx - 1, hy0 + 6, hz1 + 1, skin);
        s.Box(Body, -2, hy0 + 2, hz1, 2, hy0 + 4, hz1 + 1, skin);
        s.Box(Body, -3, hy0, hz1, 3, hy0 + 2, hz1 + 1, skin);
        s.Pair(Body, Body, -3, hy0 + 1, hz1 + 1, -2, hy0 + 4, hz1 + 2, Mat.Bone);
        s.Box(Body, -2, hy0 + 1, hz1 + 1, 2, hy0 + 2, hz1 + 2, Mat.Hair);
        s.Pair(Body, Body, -hx - 2, hy0 + 4, 0, -hx, hy0 + 5, 1, skin);
        // a sash across the chest, or a chain; iron on the shoulders with spikes, a kilt of leather strips
        byte sash = look % 3 == 2 ? Mat.ClothOchre : Mat.ClothRed;
        bool chain = look % 2 == 1;
        for (int y = b.Waist + 1; y < b.Neck; y++)
        {
            int x = -b.Chest + (y - b.Waist - 1) * (2 * b.Chest - 2) / (b.Torso - 1);
            s.Paint(Body, x, y, b.Z0, x + 3, y + 1, b.Z1, chain ? Mat.Leather : sash);
        }
        s.Box(Main, b.ArmX0 - 1, b.Neck - 4, b.ArmZ0 - 1, -b.Chest, b.Neck, b.ArmZ1 + 1, Mat.Iron, Tissue.Armor);
        s.Box(Off, b.Chest, b.Neck - 4, b.ArmZ0 - 1, -b.ArmX0 + 1, b.Neck, b.ArmZ1 + 1, Mat.Iron, Tissue.Armor);
        s.Box(Main, b.ArmX0, b.Neck, -1, b.ArmX0 + 1, b.Neck + 2, 0, Mat.Bone, Tissue.Armor);
        s.Box(Off, -b.ArmX0 - 1, b.Neck, -1, -b.ArmX0, b.Neck + 2, 0, Mat.Bone, Tissue.Armor);
        for (int x = -b.Chest; x < 0; x += 2)
            s.Pair(Body, Body, x, b.Waist - 4, b.Z1, x + 1, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        // a topknot, or an iron cap with horns
        if (look >= 3)
        {
            s.Paint(Body, -hx, hy1 - 3, hz0, hx, hy1, hz1, Mat.Iron, Tissue.Armor);
            s.Rim(Body, hy1 - 3, -hx, hz0, hx, hz1, Mat.Iron, Tissue.Armor);
            s.Pair(Body, Body, -hx - 1, hy1 - 2, 0, -hx, hy1 + 2, 2, Mat.Bone, Tissue.Armor);
            s.Pair(Body, Body, -hx - 2, hy1 + 1, 0, -hx - 1, hy1 + 3, 2, Mat.Bone, Tissue.Armor);
        }
        else
        {
            s.Paint(Body, -hx, hy1 - 1, hz0, hx, hy1, hz1, Mat.Hair);
            s.Box(Body, -1, hy1, -1, 1, hy1 + 3, 1, Mat.Hair);
            s.Box(Body, -1, hy1 + 1, -3, 1, hy1 + 2, -1, Mat.Hair);
        }
        OrcDetail(s, b, look, chain);
        Axe(s, b);
        return Done(s, f, b);
    }

    /// <summary>The finer work on an orc (see <see cref="Orc"/>).</summary>
    static void OrcDetail(Sculpt s, Build b, int look, bool chain)
    {
        if (!s.Detail) return;
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        int F = hz1 * 2 - 1, H0 = hy0 * 2, W = hx * 2, E = (hy0 + 3) * 2;
        byte skin = b.Skin, shade = Shade(skin);
        // the brow scowling: low over the nose, the furrow between, its ends cut away
        for (int x = -3; x < 3; x++) s.FSet(Body, x, H0 + 9, F + 1, skin);
        s.FDel(Body, -1, H0 + 11, F + 2); s.FDel(Body, 0, H0 + 11, F + 2);
        foreach (int x in new[] { -6, 5 }) { s.FDel(Body, x, H0 + 11, F + 1); s.FDel(Body, x, H0 + 11, F + 2); }
        // the eyes: dark sockets under it, a slit of ember deep in each
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = E; y < E + 4; y++) for (int x = -7; x < -3; x++) s.FPut(Body, X(x), y, F, y == E ? skin : Mat.Maw);
            for (int x = -6; x < -4; x++) { s.FDel(Body, X(x), E + 2, F); s.FSet(Body, X(x), E + 2, F - 1, Mat.Ember); }
        }
        // the nose flattened, its nostrils flared and dark
        foreach (int x in new[] { -4, -3, 2, 3 }) for (int y = H0 + 6; y < H0 + 8; y++) { s.FDel(Body, x, y, F + 1); s.FDel(Body, x, y, F + 2); }
        for (int y = H0 + 6; y < H0 + 8; y++) for (int x = -2; x < 2; x++) s.FDel(Body, x, y, F + 2);
        s.FPut(Body, -3, H0 + 4, F + 2, Mat.Maw); s.FPut(Body, 2, H0 + 4, F + 2, Mat.Maw);
        if (look % 3 == 1)
            foreach (var (x, y) in new[] { (-1, H0 + 3), (0, H0 + 3), (-2, H0 + 4), (1, H0 + 4) }) s.FSet(Body, x, y, F + 3, Mat.Brass);
        // the mouth a dark line with small teeth along the jutting lower lip
        for (int x = -4; x < 4; x++)
        {
            s.FDel(Body, x, H0 + 2, F + 4); s.FDel(Body, x, H0 + 3, F + 4);
            s.FSet(Body, x, H0 + 3, F + 3, (x + 8) % 3 == 0 ? Mat.Bone : Mat.Maw);
            s.FSet(Body, x, H0 + 2, F + 3, skin);
        }
        // the tusks curving up and out of the lower jaw
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = H0 + 2; y < H0 + 8; y++) for (int z = F + 3; z < F + 5; z++) { s.FDel(Body, X(-6), y, z); s.FDel(Body, X(-5), y, z); }
            foreach (var (x, y, z) in new[] { (-6, 2, 3), (-5, 2, 3), (-6, 3, 3), (-5, 3, 3), (-6, 4, 3), (-6, 4, 4), (-6, 5, 4), (-7, 5, 4), (-7, 6, 4), (-7, 7, 4), (-7, 7, 3), (-7, 8, 3) })
                s.FSet(Body, X(x), H0 + y, F + z, y >= 7 ? Mat.Bone : Mat.Tusk);
        }
        // the ears pointed back, ringed on some
        s.FCut(Body, -hx - 2, hy0 + 4, 0, -hx, hy0 + 5, 1, both: true);
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int d = 0; d < 5; d++)
                for (int y = E + 1 + d / 2; y < E + 5 - d / 3; y++)
                    for (int z = 2 - d; z < 4 - d; z++) s.FSet(Body, X(-W - 1 - d / 2), y, z, d > 0 && d < 4 && y > E + 1 + d / 2 ? shade : skin);
            if (look % 3 == 1) { s.FSet(Body, X(-W - 1), E, 3, Mat.Brass); s.FSet(Body, X(-W - 1), E - 1, 2, Mat.Brass); s.FSet(Body, X(-W - 1), E, 1, Mat.Brass); }
        }
        // a scar over the right eye on some
        if (look % 3 == 0)
            for (int k = 0; k < 8; k++) s.FPut(Body, -7 + k / 2, H0 + 13 - k, F, Mat.Scar);
        if (look < 3)
        {
            // the crest of hair over the shaved scalp, inked with spirals
            int top = hy1 * 2;
            s.FCut(Body, -1, hy1, -3, 1, hy1 + 3, 1);
            for (int z = 2 * hz0 + 1; z < 2 * hz1 - 2; z++)
                for (int x = -1; x < 1; x++)
                {
                    int h = 2 + (int)(Dice(z, x, look, 91) * 3) + (z > 0 ? 1 : 0);
                    for (int y = top - 2; y < top + h; y++) s.FSet(Body, x, y, z, Mat.Hair);
                }
            s.FPattern(Body, -W, top - 2, 2 * hz0, W, top, 2 * hz1, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Hair && Math.Abs(x + .5f) > 1 ? skin : (byte)0);
            foreach (int sg in new[] { 1, -1 })
            {
                int X(int x) => sg > 0 ? x : -1 - x;
                foreach (var (y, z) in new[] { (0, 0), (0, 1), (0, 2), (1, 2), (2, 2), (2, 1), (2, 0), (2, -1), (1, -1), (-1, -1), (-1, 3), (3, 3), (3, -2) })
                    s.FPut(Body, X(-W), H0 + 11 + y, 2 + z, Mat.Ink);
            }
        }
        else
        {
            // the cap riveted, and horns curving out of it and up
            int rim = (hy1 - 3) * 2;
            s.FPattern(Body, -W - 2, rim, 2 * hz0 - 2, W + 2, rim + 2, 2 * hz1 + 2, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Iron && (x + z + 64) % 4 == 0 && y == rim + 1 ? Mat.Steel : (byte)0);
            s.FPattern(Body, -W - 2, rim, 2 * hz0 - 2, W + 2, hy1 * 2 + 1, 2 * hz1 + 2, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Iron && Dice(x >> 1, y >> 1, z >> 1, 92) < .18f ? Mat.Rust : (byte)0);
            s.FCut(Body, -hx - 2, hy1 - 2, 0, -hx, hy1 + 3, 2, both: true);
            bool chief = look == 5;
            Horns(s, -(W + .5f), (hy1 - 1) * 2, 1.5f, chief ? 15 : 10, chief ? 9 : 6, chief ? 4.8f : 3.6f, Tissue.Armor);
        }
        // the breastplate of rusted iron, ragged at its edges and riveted, or a chain across the chest
        int cw = b.Chest * 2, z1 = b.Z1 * 2, y0 = (b.Waist + 3) * 2, y1 = (b.Neck - 2) * 2;
        if (!chain)
            for (int y = y0; y < y1; y++)
                for (int x = -cw + 2; x < cw - 2; x++)
                {
                    bool edge = y < y0 + 2 || y >= y1 - 2 || x < -cw + 4 || x >= cw - 4;
                    if (edge && Dice(x, y, look, 93) < .35f) continue;
                    bool rivet = edge && (x + y) % 4 == 0;
                    byte m = rivet ? Mat.Steel : Dice(x >> 1, y >> 1, look, 94) < .18f ? Mat.Rust : Mat.Iron;
                    s.FSet(Body, x, y, z1, m, Tissue.Armor); s.FSet(Body, x, y, z1 + 1, m, Tissue.Armor);
                }
        else
            for (int y = (b.Waist + 1) * 2; y < b.Neck * 2; y++)
            {
                float k = (y - (b.Waist + 1) * 2) / (float)((b.Torso - 1) * 2);
                int x = (int)(-cw + 2 + k * (2 * cw - 4)), z = (y & 2) == 0 ? z1 : z1 + 1;
                s.FSet(Body, x, y, z, Mat.Iron, Tissue.Armor); s.FSet(Body, x + 1, y, z, Mat.Iron, Tissue.Armor);
                if ((y & 2) == 0) s.FSet(Body, x + 1, y, z1 + 1, Mat.Iron, Tissue.Armor);
            }
        // the pauldrons ragged and riveted, horn spikes on the left
        int ax0 = (b.ArmX0 - 1) * 2, az0 = (b.ArmZ0 - 1) * 2, az1 = (b.ArmZ1 + 1) * 2, py = (b.Neck - 4) * 2;
        foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int z = az0; z < az1; z++)
                for (int x = ax0; x < -cw; x++)
                {
                    if (Dice(x, z, sg, 95) < .4f) s.FDel(p, X(x), py, z);
                    if ((x + z) % 3 == 0 && (x == ax0 || z == az0 || z == az1 - 1)) s.FPut(p, X(x), py + 3, z, Mat.Steel);
                }
            s.FPattern(p, -80, py, -80, 80, b.Neck * 2, 80, (x, y, z) => s.FMat(p, x, y, z) == Mat.Iron && Dice(x >> 1, y >> 1, z >> 1, 96 + sg) < .18f ? Mat.Rust : (byte)0);
            if (sg < 0)
                foreach (int z in new[] { az0 + 2, az1 - 3 })
                    for (int k = 0; k < 4; k++) { s.FSet(p, X(ax0 + 4), b.Neck * 2 + k, z, k == 3 ? Mat.Bone : Mat.Horn, Tissue.Armor); if (k < 2) s.FSet(p, X(ax0 + 3), b.Neck * 2 + k, z, Mat.Horn, Tissue.Armor); }
            // ink round the bare arm, a leather wrap at the wrist
            for (int y = (b.HandY + b.Hand) * 2; y < (b.HandY + b.Hand) * 2 + 3; y++) s.FPattern(p, -80, y, -80, 80, y + 1, 80, (x, yy, z) => s.FMat(p, x, yy, z) == skin ? Mat.Leather : (byte)0);
            for (int y = py - 8; y < py - 2; y++) s.FPattern(p, -80, y, -80, 80, y + 1, 80, (x, yy, z) => s.FMat(p, x, yy, z) == skin && ((x + z + yy) % 4 == 0 || yy == py - 8 || yy == py - 3) ? Mat.Ink : (byte)0);
        }
        // the kilt of hide in strips, ragged, fur at its belt; fur at the boots' tops
        for (int x = -cw; x < cw; x++)
            for (int z = -16; z < 20; z++)
            {
                if (s.FMat(Body, x, (b.Waist - 4) * 2, z) == Mat.Leather && (x & 1) == 0) s.FDel(Body, x, (b.Waist - 4) * 2, z);
                for (int y = (b.Waist - 4) * 2; y < b.Waist * 2; y++) if (s.FMat(Body, x, y, z) == Mat.Leather && z > z1 - 2) s.FPut(Body, x, y, z, Mat.Hide);
            }
        s.FPattern(Body, -80, b.Waist * 2, -80, 80, b.Waist * 2 + 2, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Leather ? Mat.FurBoar : (byte)0);
        foreach (var p in new[] { LegR, LegL }) s.FPattern(p, -80, b.Boot * 2 - 1, -80, 80, b.Boot * 2, 80, (x, y, z) => s.FHas(p, x, y, z) ? Mat.FurBoar : (byte)0);
    }

    /// <summary>
    /// A troll drawn finer: its hide in two shades, blotched; growths of stone on its shoulders, its back, its elbows and
    /// its knees and warts of it on its face; a heavy brow over small eyes glowing in their sockets, a long nose drooping
    /// to a bulb over the jaw, teeth of all sizes out of the jaw (one tusk broken), a beard of moss hanging from the jaw,
    /// claws on its fingers and toes; a wild one has a shackle on its left wrist with a length of broken chain.
    /// </summary>
    static void TrollDetail(Sculpt s, Build b, int look, bool tame)
    {
        if (!s.Detail) return;
        int hy0 = b.Neck, hz1 = b.HZ1, F = hz1 * 2 - 1, H0 = hy0 * 2, E = (hy0 + 3) * 2, W = b.HeadW * 2;
        byte skin = b.Skin;
        // the hide blotched darker
        foreach (var p in new[] { Body, Main, Off, LegR, LegL })
            s.FPattern(p, -80, 0, -80, 80, 200, 80, (x, y, z) => s.FMat(p, x, y, z) == skin && Dice(x >> 2, y >> 2, z >> 2, 100 + look) < .34f ? Mat.SkinTrollDark : (byte)0);
        // growths of stone
        float cz = (b.Z0 + b.Z1) / 2f;
        foreach (var (x, y, z, r) in new[] { (-b.Chest + 1.5f, b.Neck - .6f, cz - 1f, 1.5f), (b.Chest - 2.5f, b.Neck - .2f, cz + .5f, 1.2f), (-2f, b.Neck - 4f, b.Z0 - .2f, 1.4f),
            (3f, b.Neck - 8f, b.Z0 - .2f, 1.1f), (0f, b.Waist + 5f, b.Z0 - .2f, 1.3f), (-3f, b.Crown - .3f, 5f, 1.3f), (2.5f, b.Crown - .2f, 7f, 1f) })
            s.FBall(Body, x, y, z, r, r * .8f, r, Mat.Crag);
        float ex = b.ArmX0 + b.ArmW / 2f, ey = (b.Neck + b.HandY + b.Hand) / 2f, ez = b.ArmZ0 - .1f;
        s.FBall(Main, ex, ey, ez, 1.3f, 1.6f, 1.1f, Mat.Crag); s.FBall(Off, -ex, ey + 1, ez, 1.1f, 1.4f, 1f, Mat.Crag);
        float kx = (b.LegX0 + b.LegX1) / 2f, ky = b.Leg * .55f, kz = b.LegD - b.LegD / 2 + .1f;
        s.FBall(LegR, kx, ky, kz, 1.2f, 1.2f, 1f, Mat.Crag); s.FBall(LegL, -kx, ky - 1, kz, 1f, 1f, .9f, Mat.Crag);
        foreach (var (x, y) in new[] { (-9, E - 3), (8, E - 1), (5, E + 6) }) s.FSet(Body, x, y, F + 1, Mat.Crag);
        // the brow, and small eyes glowing in their sockets under it
        for (int x = -W + 2; x < W - 2; x++) { s.FSet(Body, x, E + 5, F + 1, skin); s.FSet(Body, x, E + 6, F + 1, skin); if (Math.Abs(x + .5f) > 2) s.FSet(Body, x, E + 5, F + 2, skin); }
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = E + 1; y < E + 4; y++) for (int x = -11; x < -8; x++) s.FPut(Body, X(x), y, F, y == E + 3 || x == -11 ? Mat.Maw : skin);
            for (int x = -11; x < -9; x++) { s.FDel(Body, X(x), E + 2, F); s.FSet(Body, X(x), E + 2, F - 1, Mat.Ember); }
        }
        // the long nose drooping to its bulb
        for (int y = E - 2; y < E + 6; y++) for (int x = -2; x < 2; x++) for (int z = F + 1; z < F + 3; z++) s.FDel(Body, x, y, z);
        s.FRod(Body, 0, (E + 5) / 2f, (F + 1) / 2f, 0, (E - 3) / 2f, (F + 11) / 2f, 1.3f, skin);
        s.FBall(Body, 0, (E - 4f) / 2, (F + 11.5f) / 2, 2f, 1.8f, 1.8f, Mat.SkinTrollDark);
        for (int z = F + 16; z > F; z--) if (s.FHas(Body, -2, E - 8, z)) { s.FPut(Body, -2, E - 8, z, Mat.Maw); s.FPut(Body, 1, E - 8, z, Mat.Maw); break; }
        s.FSet(Body, 3, E - 3, F + 13, Mat.Crag);
        // the mouth along the top of the jaw, and teeth of all sizes up out of it, the right tusk broken
        int jaw = (hy0 + 3) * 2, front = (hz1 + 3) * 2 - 1;
        for (int x = -6; x < 6; x++) s.FPut(Body, x, jaw - 1, front, Mat.Maw);
        foreach (int sg in new[] { 1, -1 })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = jaw; y < jaw + 4; y++) for (int z = (hz1 + 1) * 2; z < (hz1 + 3) * 2; z++) { s.FDel(Body, X(-8), y, z); s.FDel(Body, X(-7), y, z); s.FDel(Body, X(-4), y, z); s.FDel(Body, X(-3), y, z); }
            int tall = sg > 0 ? 6 : 2;
            for (int y = jaw - 1; y < jaw + tall; y++) { s.FSet(Body, X(-7), y, front - 2, Mat.Tusk); if (y < jaw + tall - 2) s.FSet(Body, X(-8), y, front - 2, Mat.Tusk); }
            s.FSet(Body, X(-4), jaw, front - 1, Mat.Bone); s.FSet(Body, X(-4), jaw + 1, front - 1, Mat.Bone); s.FSet(Body, X(-2), jaw, front - 1, Mat.Bone);
        }
        // a beard of moss hanging from the jaw
        if (look != 2)
            for (int x = -7; x < 7; x++)
                for (int z = front - 4; z <= front; z += 2)
                {
                    int len = 2 + (int)(Dice(x, z, look, 101) * 8);
                    for (int k = 1; k <= len; k++) s.FSet(Body, x, H0 - k, z, Mat.Moss);
                }
        // claws on the fingers and on the toes
        int hx0 = b.ArmX0 * 2, hx1 = -b.Chest * 2, hy = b.HandY * 2, hz0 = b.ArmZ0 * 2, hzz = b.ArmZ1 * 2;
        foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int z = hz0 + 1; z < hzz - 1; z += 3) { s.FSet(p, X(hx1 - 1), hy, z, Mat.Horn); s.FSet(p, X(hx1 - 1), hy - 1, z, Mat.Horn); }
        }
        Claws(s, b, Mat.Horn);
        if (tame) return;
        // a shackle on the left wrist and a length of broken chain from it
        int wy = (b.HandY + b.Hand) * 2;
        for (int y = wy; y < wy + 4; y++)
            for (int z = hz0 - 1; z <= hzz; z++)
                for (int x = -hx1; x <= -hx0; x++)
                    if (x == -hx0 || z == hz0 - 1 || z == hzz) s.FSet(Off, x, y, z, y == wy + 1 || y == wy + 2 ? Mat.Iron : Mat.Rust, Tissue.Armor);
        for (int k = 0; k < 12; k++)
        {
            int y = wy - 1 - k, z = hzz - 1 - (k & 1);
            s.FSet(Off, -hx0, y, z, Mat.Iron, Tissue.Armor);
            if (k % 4 == 1) s.FSet(Off, -hx0 + 1, y, z, Mat.Iron, Tissue.Armor);
        }
    }

    static BodyPlan Troll(int look)
    {
        var f = new BodyPlan { Chop = 1.4f, Carry = -.2f, Stride = 14 };
        var b = new Build
        {
            Leg = 16, LegW = 6, LegD = 6, Gap = 2, Boot = 0, Torso = 20, Chest = 8, ChestD = 10, Head = 10, HeadW = 6, HeadD = 10, HeadZ = 0,
            Arm = 24, ArmW = 6, ArmD = 6, Hand = 5, Skin = Mat.SkinTroll, Shirt = Mat.SkinTroll, Trousers = Mat.SkinTroll, Boots = Mat.SkinTroll,
            Belt = Mat.Leather, Hair = look == 2 ? Mat.SkinTroll : Mat.Moss, Brow = Mat.SkinTroll, Eye = Mat.Ember, Mouth = Mat.Hair, HairStyle = 1,
        };
        var s = Humanoid(b, f);
        int hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown, eye = hy0 + 3;
        // its ears set back along the sides of its head, where they hold on
        s.Cut(Body, -b.HeadW - 1, eye, -1, -b.HeadW, eye + 2, 1, both: true);
        s.Pair(Body, Body, -b.HeadW - 1, eye, 3, -b.HeadW, eye + 3, 5, Mat.SkinTroll);
        // a loincloth, moss on the shoulders and the crown, warts, a jaw that juts with teeth
        s.Box(Body, -b.Chest, b.Waist - 6, b.Z0 - 1, b.Chest, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        s.Cut(Body, -b.Chest, b.Waist - 6, b.Z0 - 1, -b.Chest + 2, b.Waist - 2, b.Z1 + 1, both: true);
        if (look != 1) s.Coat(Body, -b.Chest, b.Neck - 3, b.Z0, -b.Chest + 5, b.Neck, b.Z1, Mat.Moss);
        s.Coat(Body, b.Chest - 4, b.Neck - 3, b.Z0, b.Chest, b.Neck, b.Z1, Mat.Moss);
        foreach (var (x, y) in new[] { (-5, 14), (3, 20), (-2, 30), (6, 25) }) s.Paint(Body, x, b.Waist + y % b.Torso, b.Z1 - 1, x + 1, b.Waist + y % b.Torso + 1, b.Z1, Mat.Moss);
        s.Box(Body, -4, hy0, hz1, 4, hy0 + 3, hz1 + 3, Mat.SkinTroll);
        s.Pair(Body, Body, -4, hy0 + 3, hz1 + 1, -3, hy0 + 5, hz1 + 2, Mat.Bone);
        s.Pair(Body, Body, -2, hy0 + 3, hz1 + 2, -1, hy0 + 4, hz1 + 3, Mat.Bone);
        s.Box(Body, -2, hy1, 2, 2, hy1 + 1, 6, Mat.Moss);
        TrollDetail(s, b, look, tame: false);
        Club(s, b);
        return Done(s, f, b);
    }
}
