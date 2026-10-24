using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What our troops wear and carry: mail, plate, helms, surcoats; spears, maces, halberds, lances, slings, crossbows, pavises.</summary>
public static partial class Bodies
{
    // ---------------------------------------------------------------- our troops: what they wear

    /// <summary>Mail over the body and down the arms to the wrists, its rows of rings showing near, a brass hem at the sleeves.</summary>
    static void Mail(Sculpt s, Build b, byte m = Mat.Mail)
    {
        s.Paint(Body, -b.Chest, b.Waist + 1, b.Z0, b.Chest, b.Neck, b.Z1, m, Tissue.Armor);
        s.Paint(Main, b.ArmX0, b.HandY + b.Hand, b.ArmZ0, -b.Chest, b.Neck, b.ArmZ1, m, Tissue.Armor);
        s.Paint(Off, b.Chest, b.HandY + b.Hand, b.ArmZ0, -b.ArmX0, b.Neck, b.ArmZ1, m, Tissue.Armor);
        if (m != Mat.Mail) return;
        int cuff = (b.HandY + b.Hand) * 2;
        s.FPattern(Main, -80, cuff, -80, 80, cuff + 1, 80, (x, y, z) => s.FMat(Main, x, y, z) == Mat.Mail ? Mat.Brass : (byte)0);
        s.FPattern(Off, -80, cuff, -80, 80, cuff + 1, 80, (x, y, z) => s.FMat(Off, x, y, z) == Mat.Mail ? Mat.Brass : (byte)0);
        Gauntlets(s, b);
    }

    /// <summary>
    /// Gauntlets of leather on the hands of one in mail; near, a cuff flared out a cell at the wrist, darker at its
    /// edge, and steel scales in rows over the back of the hand.
    /// </summary>
    static void Gauntlets(Sculpt s, Build b)
    {
        s.Paint(Main, b.ArmX0, b.HandY, b.ArmZ0, -b.Chest, b.HandY + b.Hand, b.ArmZ1, Mat.Leather, Tissue.Armor);
        s.Paint(Off, b.Chest, b.HandY, b.ArmZ0, -b.ArmX0, b.HandY + b.Hand, b.ArmZ1, Mat.Leather, Tissue.Armor);
        if (!s.Detail) return;
        int ax0 = b.ArmX0 * 2, ax1 = -b.Chest * 2, az0 = b.ArmZ0 * 2, az1 = b.ArmZ1 * 2, hand = b.HandY * 2, wrist = (b.HandY + b.Hand) * 2;
        foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            // the thumb laid along the fist, and whatever else of the hand still shows its skin
            s.FPattern(p, -80, hand - 1, -80, 80, wrist + 1, 80, (x, y, z) => s.FMat(p, x, y, z) == b.Skin ? Mat.Leather : (byte)0);
            for (int z = az0 - 1; z <= az1; z++)
                for (int x = ax0 - 1; x <= ax1; x++)
                    if (x == ax0 - 1 || x == ax1 || z == az0 - 1 || z == az1)
                    {
                        s.FSet(p, X(x), wrist - 1, z, Mat.Leather, Tissue.Armor);
                        s.FSet(p, X(x), wrist - 2, z, Mat.LeatherDark, Tissue.Armor);
                    }
            for (int y = hand + 1; y < wrist - 2; y += 2)
                for (int z = az0 + 1; z < az1 - 1; z++) s.FPut(p, X(ax0), y, z, Mat.Steel);
        }
    }

    /// <summary>
    /// A broad belt and a skirt hanging from it round the hips, of mail or of quilted cloth, a brass buckle before; near,
    /// the buckle is a frame with its tongue, the mail's hem is brass and the quilting is stitched in rows.
    /// </summary>
    static void Skirt(Sculpt s, Build b, byte m, Tissue t, int rows = 3)
    {
        s.Box(Body, -b.Chest - 1, b.Waist, b.Z0 - 1, b.Chest + 1, b.Waist + 1, b.Z1 + 1, Mat.Leather);
        for (int y = b.Waist - rows; y < b.Waist; y++)
        {
            s.Box(Body, -b.Chest - 1, y, b.Z0 - 1, b.Chest + 1, y + 1, b.Z0, m, t);
            s.Box(Body, -b.Chest - 1, y, b.Z1, b.Chest + 1, y + 1, b.Z1 + 1, m, t);
            s.Pair(Body, Body, -b.Chest - 1, y, b.Z0, -b.Chest, y + 1, b.Z1, m, t);
        }
        s.Box(Body, -1, b.Waist, b.Z1 + 1, 1, b.Waist + 1, b.Z1 + 2, Mat.Brass, Tissue.Armor);
        Buckle(s, b.Waist, b.Z1 + 1);
        int hem = (b.Waist - rows) * 2;
        if (m == Mat.Mail)
            s.FPattern(Body, -80, hem, -80, 80, hem + 1, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Mail ? Mat.Brass : (byte)0);
        else if (m == Mat.Gambeson)
            s.FPattern(Body, -80, hem, -80, 80, b.Waist * 2, 80, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Gambeson && ((x + 80) % 3 == 0 || (y - hem) % 4 == 3) ? Mat.Linen : (byte)0);
    }

    /// <summary>
    /// A belt's buckle drawn finer, before the half-voxel at (y, z) the plain one fills: a brass frame standing out of the
    /// belt with the leather showing in it and the tongue across.
    /// </summary>
    static void Buckle(Sculpt s, int y, int z)
    {
        int y0 = y * 2, f = z * 2 + 1;
        for (int yy = y0 - 1; yy < y0 + 3; yy++)
            for (int x = -2; x < 2; x++)
            {
                bool frame = yy == y0 - 1 || yy == y0 + 2 || x == -2 || x == 1;
                if (frame) s.FSet(Body, x, yy, f, Mat.Brass, Tissue.Armor);
                else s.FSet(Body, x, yy, f, x == 0 ? Mat.Brass : Mat.LeatherDark, Tissue.Armor);
            }
    }

    /// <summary>A pouch on the belt at one hip (+1 the left, -1 the right): a leather bag, its flap darker, a brass stud.</summary>
    static void Pouch(Sculpt s, Build b, int side)
    {
        if (!s.Detail) return;
        int y = b.Waist * 2 - 5, x0 = side > 0 ? b.Chest * 2 - 6 : -b.Chest * 2 + 2, z = b.Z1 * 2;
        for (int yy = y; yy < y + 6; yy++)
            for (int x = x0; x < x0 + 4; x++)
                for (int k = 0; k < 2; k++)
                    s.FSet(Body, x, yy, z + k, yy >= y + 3 && k == 1 ? Mat.LeatherDark : Mat.Leather, Tissue.Gear);
        s.FSet(Body, x0 + 1, y + 3, z + 2, Mat.Brass, Tissue.Gear);
        s.FSet(Body, x0 + 2, y + 3, z + 2, Mat.Brass, Tissue.Gear);
    }

    /// <summary>
    /// A scabbard hung from the belt at the left hip, slanting back past the thigh: dark leather, a steel locket at its
    /// mouth and a chape at its tip.
    /// </summary>
    static void Scabbard(Sculpt s, Build b, int len = 22)
    {
        if (!s.Detail) return;
        int x = b.Chest * 2, y0 = b.Waist * 2 + 1, z0 = b.Z1 * 2 - 2;
        for (int k = 0; k < len; k++)
        {
            int y = y0 - k, z = z0 - k * 2 / 5;
            byte m = k < 2 ? Mat.Steel : k >= len - 3 ? Mat.Steel : Mat.LeatherDark;
            for (int dz = 0; dz < 3; dz++) { s.FSet(Body, x, y, z + dz, m, Tissue.Gear); s.FSet(Body, x + 1, y, z + dz, m, Tissue.Gear); }
        }
        // the frog that holds it to the belt
        s.FSet(Body, x, y0 + 1, z0 + 1, Mat.Leather, Tissue.Gear);
    }

    /// <summary>
    /// A kettle hat: a steel crown with a broad brim all round; near, the brim thins and slopes down to a rolled edge,
    /// a ridge runs over the crown, a band of rivets goes round it and a strap under the chin holds it.
    /// </summary>
    static void KettleHat(Sculpt s, Build b)
    {
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        s.Paint(Body, -hx, hy1 - 3, hz0, hx, hy1, hz1, Mat.Steel, Tissue.Armor);
        s.Flat(Body, hy1 - 3, 6.2f, Mat.Steel, Tissue.Armor);
        s.Flat(Body, hy1, 2.2f, Mat.Steel, Tissue.Armor);
        if (!s.Detail) return;
        int by = (hy1 - 3) * 2;
        for (int z = -14; z < 14; z++)
            for (int x = -14; x < 14; x++)
            {
                float r = MathF.Sqrt((x + .5f) * (x + .5f) + (z + .5f) * (z + .5f)) / 2;
                if (r > 6.2f || r < hx + .4f) continue;
                s.FDel(Body, x, by + 1, z);
                if (r > 5.1f) { s.FDel(Body, x, by, z); s.FSet(Body, x, by - 1, z, Mat.Steel, Tissue.Armor); }
                if (r > 5.75f) s.FSet(Body, x, by, z, Mat.Steel, Tissue.Armor);
            }
        for (int z = -4; z < 4; z++) { s.FSet(Body, -1, hy1 * 2 + 2, z, Mat.Steel, Tissue.Armor); s.FSet(Body, 0, hy1 * 2 + 2, z, Mat.Steel, Tissue.Armor); }
        s.FPattern(Body, -2 * hx - 1, by + 3, 2 * hz0 - 1, 2 * hx + 1, by + 4, 2 * hz1 + 1, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Steel && (x + z + 64) % 3 == 0 ? Mat.SteelDark : (byte)0);
        ChinStrap(s, b, by);
    }

    /// <summary>A leather strap from under a helmet's edge (row y, fine) down either cheek before the ears and under the chin.</summary>
    static void ChinStrap(Sculpt s, Build b, int y)
    {
        int hx = b.HeadW;
        for (int yy = b.Neck * 2; yy < y; yy++) { s.FPut(Body, -2 * hx, yy, 2, Mat.Leather); s.FPut(Body, 2 * hx - 1, yy, 2, Mat.Leather); }
        for (int x = -2 * hx; x < 2 * hx; x++) { s.FPut(Body, x, b.Neck * 2, 2, Mat.Leather); s.FPut(Body, x, b.Neck * 2, 3, Mat.Leather); }
    }

    /// <summary>A mail coif over the head and the shoulders, the face showing; near, its rows of rings and a leather lace across the brow.</summary>
    static void Coif(Sculpt s, Build b)
    {
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        s.Coat(Body, -hx - 1, hy0, hz0 - 1, hx + 1, hy1 + 1, hz1 + 1, Mat.Mail, Tissue.Armor, (-hx + 1, hy0, hz1 - 2, hx - 1, hy1 - 2, hz1 + 2));
        s.Paint(Body, -b.Chest, b.Neck - 2, b.Z0, b.Chest, b.Neck, b.Z1, Mat.Mail, Tissue.Armor);
        int lace = (hy1 - 2) * 2;
        s.FPattern(Body, -2 * hx - 2, lace, -2 * hz0, 2 * hx + 2, lace + 1, 2 * hz1 + 2, (x, y, z) => s.FMat(Body, x, y, z) is Mat.Mail or Mat.SteelDark ? Mat.Leather : (byte)0);
    }

    /// <summary>
    /// A nasal helm: a steel cap with a rim and a nasal down over the nose; near, the cap is drawn up to a point, four
    /// brass bands run from the rim to the top, the rim is riveted, the nasal narrow over the bridge of the nose; with an
    /// aventail, mail hangs from the rim round the sides and the back to the shoulders, over the ears.
    /// </summary>
    static void NasalHelm(Sculpt s, Build b, bool aventail)
    {
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown, up = b.FaceUp;
        s.Paint(Body, -hx, hy1 - 3, hz0, hx, hy1, hz1, Mat.Steel, Tissue.Armor);
        s.Rim(Body, hy1 - 3, -hx, hz0, hx, hz1, Mat.Steel, Tissue.Armor);
        s.Box(Body, -2, hy1, -2, 2, hy1 + 1, 2, Mat.Steel, Tissue.Armor);
        s.Box(Body, -1, hy1 + 1, -1, 1, hy1 + 2, 1, Mat.Steel, Tissue.Armor);
        s.Box(Body, -1, hy0 + 2 + up, hz1, 1, hy1 - 3, hz1 + 1, Mat.Steel, Tissue.Armor);
        if (!s.Detail) return;
        int top = hy1 * 2, rim = (hy1 - 3) * 2, F = hz1 * 2, N = (hy0 + 2 + up) * 2;
        // the cap drawn up to a point
        s.FCut(Body, -2, hy1, -2, 2, hy1 + 2, 2);
        for (int k = 0; k < 6; k++)
        {
            float r = 6.6f - k * 1.15f;
            for (int z = -8; z < 8; z++)
                for (int x = -8; x < 8; x++)
                    if ((x + .5f) * (x + .5f) + (z + .5f) * (z + .5f) <= r * r) s.FSet(Body, x, top + k, z, Mat.Steel, Tissue.Armor);
        }
        // the rim a cell proud of the cap, not two, riveted; the bands from it to the top
        for (int z = 2 * hz0 - 2; z < 2 * hz1 + 2; z++)
            for (int x = -2 * hx - 2; x < 2 * hx + 2; x++)
            {
                bool outer = x < -2 * hx - 1 || x >= 2 * hx + 1 || z < 2 * hz0 - 1 || z >= 2 * hz1 + 1;
                if (outer && !(x is -1 or 0 && z >= F)) { s.FDel(Body, x, rim, z); s.FDel(Body, x, rim + 1, z); }
            }
        s.FPattern(Body, -2 * hx - 1, rim, 2 * hz0 - 1, 2 * hx + 1, rim + 2, 2 * hz1 + 1, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Steel && y == rim + 1 && (x + z + 64) % 4 == 0 ? Mat.Brass : (byte)0);
        s.FPattern(Body, -2 * hx, rim + 2, 2 * hz0, 2 * hx, top + 6, 2 * hz1, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Steel && (x is -1 or 0 || z is -1 or 0) ? Mat.Brass : (byte)0);
        // the nasal two cells across, the nose under its end
        for (int y = N; y < rim; y++)
        {
            foreach (int x in new[] { -2, 1 }) { s.FSet(Body, x, y, F, b.Skin); s.FDel(Body, x, y, F + 1); }
            foreach (int x in new[] { -1, 0 })
            {
                s.FSet(Body, x, y, F, y < N + 4 ? b.Skin : Mat.Steel, y < N + 4 ? Tissue.Skin : Tissue.Armor);
                if (y < N + 2) s.FSet(Body, x, y, F + 1, b.Skin); else s.FSet(Body, x, y, F + 1, Mat.Steel, Tissue.Armor);
            }
        }
        for (int y = N; y < N + 2; y++) for (int x = -1; x < 1; x++) s.FDel(Body, x, y, F + 2);
        if (aventail) Aventail(s, b, rim, false);
    }

    /// <summary>
    /// Mail hung from a helmet's lower edge (row y, fine) round the sides and the back of the head to the shoulders,
    /// over the ears, its hem in brass; a bascinet's comes round under the chin as well.
    /// </summary>
    static void Aventail(Sculpt s, Build b, int y, bool chin)
    {
        int hx = b.HeadW, x0 = -2 * hx - 1, x1 = 2 * hx, z0 = 2 * b.HeadZ - 1, z1 = chin ? 2 * b.HZ1 : 3, low = b.Neck * 2 - 3;
        for (int yy = low; yy < y; yy++)
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    bool shell = x == x0 || x == x1 || z == z0 || chin && z == z1 && yy < b.Neck * 2 + 2;
                    if (!shell) continue;
                    if (s.FHas(Body, x, yy, z) && s.FMat(Body, x, yy, z) != b.Skin) continue;
                    s.FSet(Body, x, yy, z, yy == low ? Mat.Brass : Mat.Mail, Tissue.Armor);
                }
        // the ears under it
        for (int yy = low; yy < y; yy++) for (int z = -2; z < 2; z++) { s.FDel(Body, x0 - 1, yy, z); s.FDel(Body, x1 + 1, yy, z); }
    }

    /// <summary>
    /// A sallet: a steel bowl down over the brow, its tail flaring out behind over the nape, a low ridge along the top;
    /// on some a visor down to the eyes with a slit to look through. Near, the tail sweeps back in steps, the ridge is
    /// narrow, and the visor has its breaths on the right and a bevor under it over the chin.
    /// </summary>
    static void Sallet(Sculpt s, Build b, bool visor)
    {
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown;
        s.Paint(Body, -hx, hy1 - 4, hz0, hx, hy1, hz1, Mat.Steel, Tissue.Armor);
        s.Box(Body, -hx + 1, hy1 - 5, hz0 - 2, hx - 1, hy1 - 3, hz0, Mat.Steel, Tissue.Armor);
        s.Box(Body, -1, hy1, hz0 + 1, 1, hy1 + 1, hz1 - 1, Mat.Steel, Tissue.Armor);
        if (visor)
        {
            s.Paint(Body, -hx, hy0 + 2, hz1 - 1, hx, hy1, hz1, Mat.Steel, Tissue.Armor);
            s.Paint(Body, -hx + 1, hy0 + 4, hz1 - 1, hx - 1, hy0 + 5, hz1, Mat.Hair);
        }
        if (!s.Detail) return;
        int bowl = (hy1 - 4) * 2, back = 2 * hz0;
        s.FCut(Body, -hx + 1, hy1 - 5, hz0 - 2, hx - 1, hy1 - 3, hz0);
        for (int k = 0; k < 7; k++)
        {
            int y = bowl + 1 - k, reach = 1 + k * 4 / 6, w = 2 * hx - 1 - k / 3;
            for (int x = -w; x < w; x++) for (int d = 1; d <= reach; d++) s.FSet(Body, x, y, back - d, Mat.Steel, Tissue.Armor);
        }
        for (int z = 2 * hz0 + 2; z < 2 * hz1 - 2; z++) { s.FDel(Body, -2, hy1 * 2, z); s.FDel(Body, 1, hy1 * 2, z); s.FDel(Body, -2, hy1 * 2 + 1, z); s.FDel(Body, 1, hy1 * 2 + 1, z); }
        if (!visor) return;
        int F = hz1 * 2 - 1, slit = (hy0 + 4) * 2;
        s.FPut(Body, -2 * hx + 2, slit + 1, F, Mat.Steel);
        for (int x = -2 * hx + 2; x < 2 * hx - 2; x++) s.FPut(Body, x, slit + 1, F, Mat.Steel);
        foreach (var (x, y) in new[] { (2, slit - 4), (4, slit - 4), (3, slit - 6), (5, slit - 6), (2, slit - 8), (4, slit - 8) }) s.FPut(Body, x, y, F, Mat.Hair);
        // the bevor over the chin and the throat
        for (int y = hy0 * 2 - 3; y < (hy0 + 2) * 2; y++)
            for (int x = -6; x < 6; x++) s.FSet(Body, x, y, F + 1, Mat.Steel, Tissue.Armor);
    }

    /// <summary>
    /// A bascinet with a pig-faced visor: a steel skull drawn up to a point at the back, a visor thrust out before the
    /// face in a snout with two slits over it to look through and rows of breaths in it, a boss on each side where it is
    /// hinged, and mail hung from its edge all round, over the shoulders and under the chin.
    /// </summary>
    static void Bascinet(Sculpt s, Build b)
    {
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown, eye = hy0 + 3;
        s.Paint(Body, -hx, hy0 + 1, hz0, hx, hy1, hz1, Mat.Steel, Tissue.Armor);
        s.Cut(Body, -hx - 1, eye, -1, -hx, eye + 2, 1, both: true);
        s.Box(Body, -3, eye - 2, hz1, 3, eye + 1, hz1 + 2, Mat.Steel, Tissue.Armor);
        s.Box(Body, -2, eye - 2, hz1 + 2, 2, eye, hz1 + 3, Mat.Steel, Tissue.Armor);
        s.Box(Body, -1, eye - 2, hz1 + 3, 1, eye - 1, hz1 + 4, Mat.Steel, Tissue.Armor);
        s.Box(Body, -1, hy1, hz0 + 1, 1, hy1 + 1, 1, Mat.Steel, Tissue.Armor);
        s.Paint(Body, -hx + 1, eye + 1, hz1 - 1, -1, eye + 2, hz1, Mat.Hair);
        s.Paint(Body, 1, eye + 1, hz1 - 1, hx - 1, eye + 2, hz1, Mat.Hair);
        s.Box(Body, -b.Chest, hy0 - 2, b.Z0 - 1, b.Chest, hy0, b.Z1 + 1, Mat.Mail, Tissue.Armor);
        if (!s.Detail) return;
        int F = hz1 * 2, cy = eye * 2 - 1;
        // the snout rounded into a cone, drawn down a little to its end
        for (int y = (eye - 2) * 2; y < (eye + 1) * 2; y++)
            for (int z = F; z < F + 8; z++)
                for (int x = -6; x < 6; x++)
                {
                    float k = (z - F) / 8f, r = 6.4f * (1 - k) + .9f, dx = x + .5f, dy = (y + .5f - (cy - k * 1.5f)) * 1.15f;
                    if (dx * dx + dy * dy > r * r) s.FDel(Body, x, y, z);
                    else s.FSet(Body, x, y, z, Mat.Steel, Tissue.Armor);
                }
        // the breaths: two rows of holes down each side of the snout
        for (int i = 0; i < 6; i++)
            foreach (int sg in new[] { 1, -1 })
            {
                int by = cy - 2 + i / 2 * 2 - (i & 1), bz = F + 1 + (i & 1) * 2 + i / 2;
                for (int k = 7; k >= 0; k--) { int bx = sg > 0 ? k : -1 - k; if (s.FHas(Body, bx, by, bz)) { s.FPut(Body, bx, by, bz, Mat.Hair); break; } }
            }
        // the slits one cell high under a brow standing out a cell
        int slit = (eye + 1) * 2;
        for (int x = -2 * hx + 2; x < 2 * hx - 2; x++)
        {
            s.FPut(Body, x, slit + 1, F - 1, Mat.Steel);
            if (x is < -1 or > 0) s.FSet(Body, x, slit + 2, F, Mat.Steel, Tissue.Armor);
        }
        // the point at the back of the skull, the hinges
        for (int k = 0; k < 4; k++) for (int x = -1; x < 1; x++) for (int z = 2 * hz0 + 2; z < 2 * hz0 + 6 - k; z++) s.FSet(Body, x, hy1 * 2 + 2 + k, z, Mat.Steel, Tissue.Armor);
        foreach (int x in new[] { -2 * hx - 1, 2 * hx }) { s.FSet(Body, x, slit, 2, Mat.Steel, Tissue.Armor); s.FSet(Body, x, slit + 1, 2, Mat.Steel, Tissue.Armor); }
        s.FPattern(Body, -80, (hy0 - 2) * 2, -80, 80, (hy0 - 2) * 2 + 1, 80, (x, y, z) => s.FMat(Body, x, y, z) is Mat.Mail or Mat.SteelDark ? Mat.Brass : (byte)0);
    }

    /// <summary>
    /// A great helm: a pot of steel over the whole head, flat on top, a slit across it to see through either side of a
    /// ridge down the face, holes below to breathe; on top a plume of feathers falling back, or a fan of a crest. Near,
    /// the slits are a cell high, the breaths are crosses on the right side, rows of brass rivets hold its plates, and
    /// the plume falls in separate feathers.
    /// </summary>
    static void GreatHelm(Sculpt s, Build b, byte plume, bool crest)
    {
        int hx = b.HeadW, hz0 = b.HeadZ, hz1 = b.HZ1, hy0 = b.Neck, hy1 = b.Crown, eye = hy0 + 4;
        s.Cut(Body, -hx - 1, hy0, hz0, -hx, hy1, hz1, both: true);
        s.Box(Body, -hx, hy0, hz0, hx, hy1, hz1, Mat.Steel, Tissue.Armor);
        s.Box(Body, -hx + 1, hy1, hz0 + 1, hx - 1, hy1 + 1, hz1 - 1, Mat.Steel, Tissue.Armor);
        s.Paint(Body, -hx + 1, eye, hz1 - 1, -1, eye + 1, hz1, Mat.Hair);
        s.Paint(Body, 1, eye, hz1 - 1, hx - 1, eye + 1, hz1, Mat.Hair);
        s.Box(Body, -1, hy0 + 1, hz1, 1, hy1, hz1 + 1, Mat.Steel, Tissue.Armor);
        foreach (int x in new[] { -3, 2 })
            for (int y = hy0 + 1; y < eye - 1; y++) s.Paint(Body, x, y, hz1 - 1, x + 1, y + 1, hz1, Mat.Hair);
        if (crest)
        {
            // a fan of a crest standing along the top, front to back
            s.Box(Body, 0, hy1 + 1, hz0 + 2, 1, hy1 + 4, hz1 - 2, plume, Tissue.Gear);
            s.Box(Body, 0, hy1 + 4, hz0 + 3, 1, hy1 + 5, hz1 - 3, plume, Tissue.Gear);
        }
        else
        {
            // a plume up from the top and falling back over the helm
            s.Box(Body, -1, hy1 + 1, -1, 1, hy1 + 4, 1, plume, Tissue.Gear);
            s.Box(Body, -1, hy1 + 3, hz0, 1, hy1 + 5, -1, plume, Tissue.Gear);
            s.Box(Body, -1, hy1 + 1, hz0 - 1, 1, hy1 + 4, hz0, plume, Tissue.Gear);
        }
        if (!s.Detail) return;
        int F = hz1 * 2 - 1, E = eye * 2;
        // the slits a cell high, the ridge narrower, the breaths in crosses on the right side only
        for (int x = -2 * hx + 2; x < 2 * hx - 2; x++) s.FPut(Body, x, E + 1, F, Mat.Steel);
        for (int y = hy0 * 2 + 2; y < E - 2; y++) foreach (int x in new[] { -6, -5, 4, 5 }) s.FPut(Body, x, y, F, Mat.Steel);
        for (int y = hy0 * 2 + 2; y < hy1 * 2; y++) { s.FDel(Body, -2, y, F + 2); s.FDel(Body, 1, y, F + 2); }
        foreach (var (cx, cy) in new[] { (4, E - 5), (4, E - 10), (1 + 2, E - 15) })
            foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) }) s.FPut(Body, cx + dx, cy + dy, F, Mat.Hair);
        // the rivets along the band round the top and down the seam behind
        s.FPattern(Body, -2 * hx - 1, (hy1 - 2) * 2, 2 * hz0 - 1, 2 * hx + 1, (hy1 - 2) * 2 + 1, 2 * hz1 + 2, (x, y, z) => s.FMat(Body, x, y, z) == Mat.Steel && (x + z + 64) % 3 == 0 ? Mat.Brass : (byte)0);
        for (int y = hy0 * 2 + 1; y < hy1 * 2; y += 3) s.FPut(Body, 0, y, 2 * hz0, Mat.Brass);
        if (crest)
        {
            // the crest's edge cut in a fan of points
            for (int z = 2 * hz0 + 4; z < 2 * hz1 - 4; z++)
                if (((z + 64) % 3) == 0) { s.FDel(Body, 0, hy1 * 2 + 9, z); s.FDel(Body, 1, hy1 * 2 + 9, z); }
            return;
        }
        // the plume in feathers: every other column of it taken back a cell, their ends ragged
        for (int y = hy1 * 2 + 2; y < hy1 * 2 + 10; y++)
            for (int z = 2 * hz0 - 2; z < 2; z++)
                if (((z + y) & 1) == 0 && s.FHas(Body, -2, y, z) && Dice(y, z, 0, 71) < .5f) s.FDel(Body, -2, y, z);
    }

    /// <summary>
    /// Plate all over: the body, the arms with their gauntlets, the legs to the steel shoes; pauldrons on the shoulders.
    /// Near: the pauldrons in lames, a couter with its wing at each elbow, the gauntlets flared at the wrist with the
    /// plates of their fingers, a poleyn with its wing at each knee, the sabatons in lames.
    /// </summary>
    static void Plate(Sculpt s, Build b, bool legs = true)
    {
        s.Paint(Body, -b.Chest, b.Waist + 1, b.Z0, b.Chest, b.Neck, b.Z1, Mat.Steel, Tissue.Armor);
        s.Paint(Main, -40, -40, -40, 0, 120, 40, Mat.Steel, Tissue.Armor);
        s.Paint(Off, 0, -40, -40, 40, 120, 40, Mat.Steel, Tissue.Armor);
        if (legs)
        {
            s.Paint(LegR, -40, 0, -40, 40, 120, 40, Mat.Steel, Tissue.Armor);
            s.Paint(LegL, -40, 0, -40, 40, 120, 40, Mat.Steel, Tissue.Armor);
        }
        Spaulders(s, b);
        // the steel shoes' points; the plates of the belly in rows
        for (int y = b.Waist + 2; y < b.Waist + 6; y += 2) s.Paint(Body, -b.Chest, y, b.Z1 - 1, b.Chest, y + 1, b.Z1, Mat.Iron);
        if (!s.Detail) return;
        int ax0 = b.ArmX0 * 2, ax1 = -b.Chest * 2, az0 = b.ArmZ0 * 2, az1 = b.ArmZ1 * 2, elbow = (b.Neck + b.HandY + b.Hand) - 1, wrist = (b.HandY + b.Hand) * 2;
        foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            // the couter: a cap at the back of the elbow, a wing out on its outer side
            for (int y = elbow - 2; y < elbow + 3; y++)
                for (int x = ax0; x < ax1; x++) s.FSet(p, X(x), y, az0 - 1, Mat.Steel, Tissue.Armor);
            for (int y = elbow - 3; y < elbow + 4; y++)
                for (int z = az0; z < az0 + 5; z++)
                    if ((y - elbow) * (y - elbow) + (z - az0 - 1) * (z - az0 - 1) <= 9) s.FSet(p, X(ax0 - 1), y, z, Mat.Steel, Tissue.Armor);
            foreach (int y in new[] { elbow - 3, elbow + 3 }) s.FPattern(p, -80, y, -80, 80, y + 1, 80, (x, yy, z) => s.FMat(p, x, yy, z) == Mat.Steel ? Mat.SteelDark : (byte)0);
            // the gauntlet: its cuff flared out a cell, the plates over the fingers in rows
            for (int z = az0 - 1; z <= az1; z++) for (int x = ax0 - 1; x <= ax1; x++)
                    if (x == ax0 - 1 || x == ax1 || z == az0 - 1 || z == az1) { s.FSet(p, X(x), wrist - 1, z, Mat.Steel, Tissue.Armor); s.FSet(p, X(x), wrist - 2, z, Mat.Steel, Tissue.Armor); }
            for (int y = b.HandY * 2 + 1; y < wrist - 3; y += 2) s.FPattern(p, -80, y, -80, 80, y + 1, 80, (x, yy, z) => s.FMat(p, x, yy, z) == Mat.Steel ? Mat.SteelDark : (byte)0);
        }
        if (!legs) return;
        int lx0 = b.LegX0 * 2, lx1 = b.LegX1 * 2, lz1 = (b.LegD - b.LegD / 2) * 2, knee = b.Leg - 1;
        foreach (var (p, sg) in new[] { (LegR, 1), (LegL, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            for (int y = knee - 2; y < knee + 3; y++) for (int x = lx0; x < lx1; x++) s.FSet(p, X(x), y, lz1, Mat.Steel, Tissue.Armor);
            s.FSet(p, X(lx0 + 1), knee, lz1 + 1, Mat.Steel, Tissue.Armor); s.FSet(p, X(lx0 + 2), knee, lz1 + 1, Mat.Steel, Tissue.Armor);
            for (int y = knee - 3; y < knee + 4; y++)
                for (int z = lz1 - 5; z < lz1; z++)
                    if ((y - knee) * (y - knee) + (z - lz1 + 2) * (z - lz1 + 2) <= 8) s.FSet(p, X(lx0 - 1), y, z, Mat.Steel, Tissue.Armor);
            foreach (int y in new[] { knee - 3, knee + 3, 2, 4 }) s.FPattern(p, -80, y, -80, 80, y + 1, 80, (x, yy, z) => s.FMat(p, x, yy, z) == Mat.Steel ? Mat.SteelDark : (byte)0);
        }
    }

    /// <summary>Steel over the shoulders and down the upper arms, the top edge rounded off; near, in lames, its edges rolled.</summary>
    static void Spaulders(Sculpt s, Build b)
    {
        s.Box(Main, b.ArmX0 - 1, b.Neck - 3, b.ArmZ0 - 1, -b.Chest, b.Neck, b.ArmZ1 + 1, Mat.Steel, Tissue.Armor);
        s.Box(Off, b.Chest, b.Neck - 3, b.ArmZ0 - 1, -b.ArmX0 + 1, b.Neck, b.ArmZ1 + 1, Mat.Steel, Tissue.Armor);
        s.Cut(Main, b.ArmX0 - 1, b.Neck - 1, b.ArmZ0 - 1, b.ArmX0, b.Neck, b.ArmZ1 + 1);
        s.Cut(Off, -b.ArmX0, b.Neck - 1, b.ArmZ0 - 1, -b.ArmX0 + 1, b.Neck, b.ArmZ1 + 1);
        if (!s.Detail) return;
        int x0 = (b.ArmX0 - 1) * 2, z0 = (b.ArmZ0 - 1) * 2, z1 = (b.ArmZ1 + 1) * 2;
        foreach (var (p, sg) in new[] { (Main, 1), (Off, -1) })
        {
            int X(int x) => sg > 0 ? x : -1 - x;
            s.FRound(p, sg > 0 ? b.ArmX0 - 1 : b.Chest, b.Neck - 3, b.ArmZ0 - 1, sg > 0 ? -b.Chest : -b.ArmX0 + 1, b.Neck, b.ArmZ1 + 1, .9f, true, 1);
            for (int y = (b.Neck - 3) * 2; y < b.Neck * 2; y += 2)
                for (int z = z0; z < z1; z++)
                    for (int x = x0; x < -b.Chest * 2; x++)
                        if ((x == x0 || z == z0 || z == z1 - 1) && s.FMat(p, X(x), y, z) == Mat.Steel) s.FPut(p, X(x), y, z, Mat.SteelDark);
            // the lowest lame's edge rolled out a cell
            for (int z = z0; z < z1; z++) s.FSet(p, X(x0 - 1), (b.Neck - 3) * 2, z, Mat.Steel, Tissue.Armor);
        }
    }

    /// <summary>
    /// A surcoat over the plate: a panel of cloth before and behind, hung from the shoulders free of the steel and
    /// falling below the belt (a rider's only to the saddle), girt at the waist, a cross on the breast; near, its hem
    /// cut in dags.
    /// </summary>
    static void Surcoat(Sculpt s, Build b, byte cloth, byte cross, int drop = 5)
    {
        foreach (int z in new[] { b.Z1 + 1, b.Z0 - 2 })
            s.Box(Body, -b.Chest + 1, b.Waist - drop, z, b.Chest - 1, b.Neck - 1, z + 1, cloth, Tissue.Armor);
        s.Box(Body, -b.Chest + 1, b.Neck - 1, b.Z0 - 2, b.Chest - 1, b.Neck, b.Z1 + 2, cloth, Tissue.Armor);
        s.Box(Body, -b.Chest, b.Waist, b.Z0 - 2, b.Chest, b.Waist + 1, b.Z1 + 2, Mat.Leather);
        s.Paint(Body, -1, b.Waist + 1, b.Z1 + 1, 1, b.Neck - 1, b.Z1 + 2, cross);
        s.Paint(Body, -b.Chest + 1, b.Neck - 5, b.Z1 + 1, b.Chest - 1, b.Neck - 4, b.Z1 + 2, cross);
        if (!s.Detail || drop == 0) return;
        int hem = (b.Waist - drop) * 2;
        for (int x = (-b.Chest + 1) * 2; x < (b.Chest - 1) * 2; x++)
            if (((x + 64) / 2 & 1) == 0)
                foreach (int z in new[] { (b.Z1 + 1) * 2, (b.Z1 + 1) * 2 + 1, (b.Z0 - 2) * 2, (b.Z0 - 2) * 2 + 1 }) s.FDel(Body, x, hem, z);
        Buckle(s, b.Waist, b.Z1 + 1);
    }

    // ---------------------------------------------------------------- our troops: what they carry

    /// <summary>
    /// A spear: an ash shaft through the fist with its butt well behind in an iron shoe, a socket with two brass rivets,
    /// and a leaf of steel, broadest a third of the way along, a raised rib down its middle and its edges honed.
    /// </summary>
    static void Spear(Sculpt s, Build b, int len = 26, int back = 6)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int q = 4, e = z0 + len * q, butt = b.ArmZ0 * q - back * q;
        g.Fill(x, y, butt + 3, x + 2, y + 2, e, Mat.Shaft);
        g.Fill(x, y, butt, x + 2, y + 2, butt + 3, Mat.Iron);
        g.Dot(x, y, butt - 1, Mat.Iron);
        Socket(g, x, y, e - 8, e);
        g.Dot(x - 2, y + 1, e - 5, Mat.Brass); g.Dot(x + 3, y, e - 5, Mat.Brass);
        const int L = 24;
        for (int k = 0; k < L; k++)
        {
            float t = (k + .5f) / L, w = 3.4f * MathF.Pow(MathF.Sin(MathF.PI * MathF.Pow(t, .62f)), .9f);
            for (int dy = -4; dy < 4; dy++)
            {
                float a = MathF.Abs(dy + .5f);
                bool rib = a < 1 && t < .93f;
                if (a > w + .01f && !rib) continue;
                byte m = !rib && a + 1 > w ? Mat.SteelEdge : Mat.Steel;
                g.Dot(x, y + 1 + dy, e + k, m);
                if (rib) g.Dot(x + 1, y + 1 + dy, e + k, m);
            }
        }
        g.Tip = Half(g, x + 1, y + 1, e + L);
    }

    /// <summary>The iron socket of a head on its shaft, four cells square with its corners taken off, from z0 up to z1.</summary>
    static void Socket(Gear g, int x, int y, int z0, int z1)
    {
        for (int z = z0; z < z1; z++)
            for (int dy = -1; dy < 3; dy++)
                for (int dx = -1; dx < 3; dx++)
                    if (!((dx == -1 || dx == 2) && (dy == -1 || dy == 2))) g.Dot(x + dx, y + dy, z, Mat.Iron);
    }

    /// <summary>
    /// A flanged mace: a haft with a wound grip and an iron knob behind, and at its end an iron head with steel flanges
    /// standing out eight ways, each rising to a point and falling again, a knob on top.
    /// </summary>
    static void Mace(Sculpt s, Build b)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int q = 4, e = z0 + 30;
        g.Fill(x, y, b.ArmZ0 * q - 3, x + 2, y + 2, e, Mat.Handle);
        g.Wound(x, y, b.ArmZ0 * q - 3, z0 + 3, 2, Mat.LeatherDark, Mat.Leather);
        g.Fill(x - 1, y - 1, b.ArmZ0 * q - 5, x + 3, y + 3, b.ArmZ0 * q - 3, Mat.Iron);
        float cx = x + 1, cy = y + 1;
        const int H = 15;
        for (int k = 0; k < H; k++)
        {
            int z = e - 1 + k;
            g.Plate(z, 1, cx, cy, 2.7f, Mat.Iron);
            float t = (k + .5f) / H, reach = 2.5f + 4.6f * MathF.Pow(MathF.Sin(MathF.PI * t), .7f);
            // four flanges square to the faces, two cells thick, and four between them along the diagonals
            for (int r = 2; r < reach; r++)
            {
                g.Fill(x, y + 2 + r - 2, z, x + 2, y + 3 + r - 2, z + 1, Mat.Steel);
                g.Fill(x, y - 1 - (r - 2), z, x + 2, y - (r - 2), z + 1, Mat.Steel);
                g.Fill(x + 2 + r - 2, y, z, x + 3 + r - 2, y + 2, z + 1, Mat.Steel);
                g.Fill(x - 1 - (r - 2), y, z, x - (r - 2), y + 2, z + 1, Mat.Steel);
            }
            for (int d = 1; d < reach * .72f; d++)
                foreach (var (sx, sy) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
                {
                    int px = sx > 0 ? x + 1 + d : x - d, py = sy > 0 ? y + 1 + d : y - d;
                    g.Dot(px, py, z, Mat.Steel);
                    g.Dot(px - sx, py, z, Mat.Steel);
                }
        }
        g.Fill(x - 1, y - 1, e + H - 1, x + 3, y + 3, e + H + 1, Mat.Iron);
        g.Fill(x, y, e + H + 1, x + 2, y + 2, e + H + 3, Mat.Steel);
        g.Tip = Half(g, cx, cy, e + H / 2);
    }

    /// <summary>
    /// A halberd: an ash haft of two metres with iron langets down it from the head, riveted; at the head a socket, an
    /// axe blade hanging below with its edge honed, a hooked beak above curving forward, a long square spike on beyond a
    /// steel rondel, and a red tassel under the head.
    /// </summary>
    static void Halberd(Sculpt s, Build b, int len = 20)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int q = 4, e = z0 + len * q, butt = b.ArmZ0 * q - 24;
        g.Fill(x, y, butt + 3, x + 2, y + 2, e + 2, Mat.Shaft);
        g.Fill(x, y, butt, x + 2, y + 2, butt + 3, Mat.Iron);
        g.Fill(x - 1, y, e - 28, x, y + 2, e, Mat.Iron);
        g.Fill(x + 2, y, e - 28, x + 3, y + 2, e, Mat.Iron);
        for (int z = e - 26; z < e; z += 8) { g.Dot(x - 2, y, z, Mat.Brass); g.Dot(x + 3, y + 1, z, Mat.Brass); }
        Socket(g, x, y, e - 7, e + 3);
        // the blade below
        const int B = 16;
        for (int v = 1; v <= B; v++)
        {
            float t = v / (float)B;
            int front = e + 6 - (int)MathF.Round(2.5f * t), rear = e - 5 - (int)MathF.Round(9 * t * t);
            for (int z = rear; z <= front; z++)
            {
                bool edge = v >= B - 1 || v > B - 4 && z <= rear + 1;
                g.Dot(x, y - v, z, edge ? Mat.SteelEdge : Mat.Steel);
                if (v <= 2) g.Dot(x + 1, y - v, z, Mat.Steel);
            }
        }
        // the beak above, curving forward to its point
        const int P = 12;
        for (int v = 0; v < P; v++)
        {
            float t = v / (float)P;
            int zc = e - 1 + (int)MathF.Round(6 * t * t), w = Math.Max(1, (int)MathF.Round(4 * (1 - t)));
            for (int k = 0; k < w; k++) { g.Dot(x, y + 2 + v, zc - k, Mat.Steel); if (v < 3) g.Dot(x + 1, y + 2 + v, zc - k, Mat.Steel); }
        }
        // the rondel and the spike
        g.Plate(e + 3, 1, x + 1, y + 1, 3.6f, Mat.Steel);
        for (int z = e + 4; z < e + 30; z++)
        {
            bool thin = z >= e + 24;
            g.Fill(x, y, z, thin ? x + 1 : x + 2, thin ? y + 1 : y + 2, z + 1, z >= e + 27 ? Mat.SteelEdge : Mat.Steel);
        }
        // the tassel hanging from under the socket
        for (int z = e - 11; z < e - 7; z++)
            for (int xx = x; xx < x + 2; xx++)
            {
                int hang = 6 + (int)(Dice(xx, z, 0, 21) * 4);
                for (int v = 1; v <= hang; v++) g.Dot(xx, y - v, z, v > hang - 2 ? Mat.ClothOchre : Mat.ClothRed);
            }
        g.Tip = Half(g, x + .5f, y + .5f, e + 30);
    }

    /// <summary>
    /// A lance: a long shaft couched under the arm, painted with a spiral of the house's colour, a steel vamplate in a
    /// cone before the hand, an iron ring under the head and a steel point; a pennon of the house's colour hangs from it
    /// under the head, a swallowtail at its fly and a gold edge along the shaft.
    /// </summary>
    static void Lance(Sculpt s, Build b, byte pennon, int len = 38)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int q = 4, e = z0 + len * q, butt = b.ArmZ0 * q - 32;
        for (int z = butt; z < e; z++)
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                {
                    int face = dy == 0 ? dx : 3 - dx;
                    g.Dot(x + dx, y + dy, z, (z + face * 3 + 1200) % 12 < 4 ? pennon : Mat.Shaft);
                }
        for (int k = 0; k < 9; k++) g.Plate(z0 + k, 1, x + 1, y + 1, 7.4f - k * .6f, Mat.Steel);
        Socket(g, x, y, e - 4, e);
        for (int z = e; z < e + 12; z++)
        {
            bool thin = z >= e + 7;
            g.Fill(x, y, z, thin ? x + 1 : x + 2, thin ? y + 1 : y + 2, z + 1, z >= e + 10 ? Mat.SteelEdge : Mat.Steel);
        }
        // the pennon: below the shaft, its fly toward the hand cut in a swallowtail
        const int Hh = 14, Ll = 28;
        for (int v = 1; v <= Hh; v++)
            for (int k = 0; k < Ll; k++)
            {
                float cut = 7 * (1 - MathF.Abs(v - Hh / 2f - .5f) / (Hh / 2f));
                if (k < cut) continue;
                g.Dot(x, y - v, e - 6 - Ll + k, v == 1 ? Mat.ClothOchre : pennon);
            }
        g.Tip = Half(g, x + .5f, y + .5f, e + 12);
    }

    /// <summary>
    /// A giant's maul: a young trunk for a haft, bound in leather where he grips it, and on its end a block of stone
    /// with its corners broken, bound round with two riveted bands of iron.
    /// </summary>
    static void Maul(Sculpt s, Build b, int len = 18)
    {
        var g = InFist(s, b, out int x, out int y, out int z0, fine: false);
        int q = 2, e = z0 + len * q;
        g.Fill(x - 1, y - 1, b.ArmZ0 * q - 6, x + 3, y + 3, e, Mat.Trunk);
        for (int z = b.ArmZ0 * q - 6; z < b.ArmZ0 * q - 1; z++)
            for (int dy = -1; dy < 3; dy++) for (int dx = -1; dx < 3; dx++) if (dx is -1 or 2 || dy is -1 or 2) g.Dot(x + dx, y + dy, z, ((z + dx) & 1) == 0 ? Mat.Leather : Mat.LeatherDark);
        int hx0 = x - 5, hx1 = x + 7, hy0 = y - 5, hy1 = y + 7, hz0 = e, hz1 = e + 14;
        for (int z = hz0; z < hz1; z++)
            for (int yy = hy0; yy < hy1; yy++)
                for (int xx = hx0; xx < hx1; xx++)
                {
                    // the corners and the edges broken off here and there
                    int ex = Math.Min(xx - hx0, hx1 - 1 - xx), ey = Math.Min(yy - hy0, hy1 - 1 - yy), ez = Math.Min(z - hz0, hz1 - 1 - z);
                    int edges = (ex == 0 ? 1 : 0) + (ey == 0 ? 1 : 0) + (ez == 0 ? 1 : 0);
                    if (edges >= 2 && Dice(xx, yy, z, 31) < .45f || edges == 3) continue;
                    if (edges >= 2 && ex + ey + ez <= 1 && Dice(xx / 2, yy / 2, z / 2, 32) < .5f) continue;
                    g.Dot(xx, yy, z, Dice(xx >> 1, yy >> 1, z >> 1, 33) < .25f ? Mat.StoneDark : Mat.Rock);
                }
        foreach (int z in new[] { hz0 + 2, hz1 - 4 })
            for (int yy = hy0 - 1; yy <= hy1; yy++)
                for (int xx = hx0 - 1; xx <= hx1; xx++)
                {
                    bool rim = xx == hx0 - 1 || xx == hx1 || yy == hy0 - 1 || yy == hy1;
                    if (!rim) continue;
                    g.Fill(xx, yy, z, xx + 1, yy + 1, z + 2, Mat.Iron);
                    if ((xx + yy) % 4 == 0)
                    {
                        int ox = xx == hx0 - 1 ? -1 : xx == hx1 ? 1 : 0, oy = ox != 0 ? 0 : yy == hy0 - 1 ? -1 : 1;
                        g.Dot(xx + ox, yy + oy, z, Mat.Steel);
                    }
                }
        g.Tip = Half(g, x + 1, y + 1, (hz0 + hz1) / 2f);
    }

    /// <summary>A sling hanging from the right fist: two cords, a leather pouch at their end and a stone in the pouch.</summary>
    static void Sling(Sculpt s, Build b)
    {
        var g = InFist(s, b, out int x, out _, out _);
        int q = 4, zc = (b.ArmZ0 + b.ArmZ1) * q / 2, top = b.HandY * q + 2, py = top - 26;
        for (int yy = py + 2; yy < top; yy++) { g.Dot(x, yy, zc - 3, Mat.Linen); g.Dot(x, yy, zc + 2, Mat.Linen); }
        for (int z = zc - 4; z < zc + 4; z++)
            for (int xx = x - 2; xx < x + 4; xx++)
            {
                bool side = z == zc - 4 || z == zc + 3 || xx == x - 2 || xx == x + 3;
                g.Dot(xx, py, z, Mat.Leather);
                if (side) g.Fill(xx, py + 1, z, xx + 1, py + 3, z + 1, Mat.Leather);
            }
        g.Fill(x - 1, py + 1, zc - 2, x + 3, py + 4, zc + 2, Mat.Rock);
        g.Tip = Half(g, x + 1, py + 2, zc);
    }

    /// <summary>
    /// A crossbow in the right fist, its stock down along the arm, so that raised to the shoulder it points ahead: a
    /// steel prod across its far end bending back at the tips, the string drawn back from them to the bone nut, a bolt
    /// laid in the groove, the long iron trigger lever under the stock and an iron stirrup to span it with the foot.
    /// </summary>
    static void Crossbow(Sculpt s, Build b)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int q = 4, top = (b.FistY + 1) * q + 2, end = (b.FistY - 7) * q, zf = z0 + 3;
        float cx = x + 1;
        // the stock, deeper at its butt
        g.Fill(x - 1, end, z0, x + 3, top, zf, Mat.Shaft);
        g.Fill(x - 1, top - 8, z0 - 2, x + 3, top, z0, Mat.Shaft);
        // the prod across the far end, bending back toward the shooter at the tips
        const int Hw = 20;
        for (int dx = -Hw; dx < Hw; dx++)
        {
            float a = MathF.Abs(dx + .5f) / Hw;
            int yo = (int)MathF.Round(4 * a * a), th = a < .5f ? 2 : 1;
            for (int k = 0; k < th; k++) { g.Dot(x + 1 + dx, end + 1 + yo + k, zf, Mat.Steel); g.Dot(x + 1 + dx, end + 1 + yo + k, zf - 1, a < .3f ? Mat.Steel : Mat.SteelDark); }
        }
        // the string from the tips back to the nut, the nut, the bolt in its groove
        int nut = end + 15;
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            float ax = cx + sgn * (Hw - .5f), ay = end + 5.5f;
            int n = 40;
            int lx = int.MinValue, ly = 0;
            for (int i = 0; i <= n; i++)
            {
                float k = i / (float)n;
                int px = (int)MathF.Floor(ax + (cx - ax) * k), py = (int)MathF.Floor(ay + (nut - ay) * k);
                if (lx != int.MinValue) while (lx != px || ly != py) { if (lx != px) lx += Math.Sign(px - lx); else ly += Math.Sign(py - ly); g.Dot(lx, ly, zf, Mat.Linen); }
                else { lx = px; ly = py; g.Dot(px, py, zf, Mat.Linen); }
            }
        }
        g.Fill(x, nut - 1, zf, x + 2, nut + 2, zf + 2, Mat.Bone);
        g.Fill(x, end - 3, zf, x + 1, nut - 1, zf + 1, Mat.Handle);
        g.Fill(x, end - 5, zf, x + 1, end - 3, zf + 1, Mat.Iron);
        g.Dot(x - 1, nut - 3, zf, Mat.ClothRed); g.Dot(x + 1, nut - 3, zf, Mat.ClothRed);
        g.Dot(x - 1, nut - 4, zf, Mat.Linen); g.Dot(x + 1, nut - 4, zf, Mat.Linen);
        // the trigger lever under the stock, and the stirrup beyond the prod
        g.Fill(x, nut - 2, z0 - 1, x + 2, nut + 10, z0, Mat.Iron);
        for (int yy = end - 7; yy <= end; yy++)
            for (int xx = x - 3; xx <= x + 4; xx++)
                if (xx == x - 3 || xx == x + 4 || yy == end - 7) g.Dot(xx, yy, z0 + 1, Mat.Iron);
        g.Tip = Half(g, cx, end - 5, zf);
        g.Edge = (0, 0, 1);
    }

    /// <summary>A round pot of powder held out in the right fist, a neck stopped with a rag, its fuse alight.</summary>
    static void PotInHand(Sculpt s, Build b)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        float cx = x + 1, cy = y + 1, cz = z0 + 6;
        g.Ball(cx / 4, cy / 4, cz / 4, 1.55f, 1.4f, 1.55f, Mat.Clay);
        for (int xx = x - 6; xx < x + 8; xx++) for (int z = z0; z < z0 + 13; z++) if (g.Has(xx, y - 2, z)) g.Dot(xx, y - 2, z, Mat.Char);
        g.Fill(x - 1, y + 6, (int)cz - 2, x + 3, y + 8, (int)cz + 2, Mat.Clay);
        g.Fill(x, y + 8, (int)cz - 1, x + 2, y + 10, (int)cz + 1, Mat.Rag);
        g.Fill(x, y + 10, (int)cz - 1, x + 1, y + 13, (int)cz, Mat.Ember);
        g.Tip = Half(g, cx, cy, cz);
    }

    /// <summary>
    /// A tower shield of planks strapped to the left forearm, from a man's chest to his knees: upright planks with dark
    /// seams, a rib standing out down the middle, an iron rim and iron at the corners, riveted; painted the garrison's
    /// blue with a gold tower on it, its battlements, two windows and the arch of its gate.
    /// </summary>
    static void Pavise(Sculpt s, Build b)
    {
        var g = s.Hold(Off, DressKind.Shield, Tissue.Armor);
        const int q = 4;
        int cxh = -b.ArmX0 - b.ArmW / 2, cx = cxh * q;
        int x0 = (cxh - 5) * q, x1 = (cxh + 5) * q, y0 = (b.HandY - 5) * q, y1 = (b.HandY + b.Hand + 9) * q, zp = (b.Z1 + 3) * q;
        Straps(g, b, cx, zp);
        int ty = (y0 + y1) / 2 - 10;
        for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++)
            {
                // the top corners rounded off
                float rx = Math.Max(0, Math.Max(x0 + 4 - xx - .5f, xx + .5f - (x1 - 4))), ry = Math.Max(0, yy + .5f - (y1 - 4));
                if (rx * rx + ry * ry > 16) continue;
                bool rim = xx < x0 + 2 || xx >= x1 - 2 || yy < y0 + 2 || yy >= y1 - 2 || rx * rx + ry * ry > 5;
                bool corner = (xx < x0 + 7 || xx >= x1 - 7) && (yy < y0 + 7 || yy >= y1 - 7) && (xx < x0 + 4 || xx >= x1 - 4 || yy < y0 + 4 || yy >= y1 - 4);
                bool rib = Math.Abs(xx + .5f - cx) < 4;
                int seam = (xx - x0) % 5;
                byte plank = seam == 0 ? Mat.BoardDark : Mat.Board;
                g.Dot(xx, yy, zp, plank);
                if (rim || corner) { g.Fill(xx, yy, zp - 1, xx + 1, yy + 1, zp + (rib ? 4 : 3), Mat.Iron); continue; }
                // the tower: its wall, the battlements, two windows, the arch of the gate
                int u = xx - cx, v = yy - ty;
                bool wall = u >= -8 && u < 8 && v >= 0 && v < 22, merlon = v >= 22 && v < 26 && u >= -8 && u < 8 && (u + 8) % 6 < 4;
                bool window = v >= 13 && v < 17 && (u is -5 or -4 || u is 3 or 4);
                bool gate = u >= -3 && u < 3 && v >= 0 && (v < 6 || v < 8 && Math.Abs(u + .5f) < 2);
                byte paint = (wall || merlon) && !window && !gate ? Mat.ClothOchre : window || gate ? Mat.Hair : Mat.ClothBlue;
                g.Dot(xx, yy, zp + 1, paint);
                if (rib) { g.Dot(xx, yy, zp + 2, paint); if (Math.Abs(xx + .5f - cx) < 2) g.Dot(xx, yy, zp + 3, paint); }
            }
        for (int yy = y0 + 3; yy < y1 - 3; yy += 8) { g.Dot(x0 + 1, yy, zp + 3, Mat.Steel); g.Dot(x1 - 2, yy, zp + 3, Mat.Steel); }
        g.Grip = (cx / (float)q, (y0 + y1) / 2f / q, zp / (float)q);
        g.Tip = (cx / (float)q, (y0 + y1) / 2f / q, (zp + 4) / (float)q);
        g.Edge = (0, 0, 1);
    }

    /// <summary>
    /// A heater shield on the left forearm: flat along the top, its sides curving in to a point below and bending back
    /// round the arm; a field of the house's colour, a chevron across it with a line of the field's colour along it, an
    /// iron rim.
    /// </summary>
    static void Heater(Sculpt s, Build b, byte field, byte charge)
    {
        var g = s.Hold(Off, DressKind.Shield, Tissue.Armor);
        const int q = 4;
        float cx = (-b.ArmX0 - b.ArmW / 2f) * q, half = 4.6f * q;
        int top = (b.HandY + b.Hand + 6) * q, H = 13 * q, zp = (b.Z1 + 2) * q + 1;
        Straps(g, b, (int)cx, zp);
        bool In(int xx, int i)
        {
            if (i < 0 || i >= H) return false;
            float k = i < H * .4f ? 1 : 1 - (i - H * .4f) / (H - 1 - H * .4f);
            return MathF.Abs(xx + .5f - cx) <= half * MathF.Sqrt(MathF.Max(0, k)) + .01f;
        }
        for (int i = 0; i < H; i++)
            for (int xx = (int)(cx - half) - 1; xx <= cx + half; xx++)
            {
                if (!In(xx, i)) continue;
                int yy = top - 1 - i;
                float d = (xx + .5f - cx) / half;
                int back = (int)MathF.Round(2.6f * d * d);
                bool rim = !In(xx - 2, i) || !In(xx + 2, i) || !In(xx, i - 2) || !In(xx, i + 2) || !In(xx - 1, i + 1) || !In(xx + 1, i + 1);
                g.Dot(xx, yy, zp - back, Mat.Board);
                if (rim) { g.Fill(xx, yy, zp - back, xx + 1, yy + 1, zp + 3 - back, Mat.Iron); continue; }
                // a chevron: a broad V upside down across the middle, a line of the field's colour along its edges
                float up = top - 20 - MathF.Abs(xx + .5f - cx) * .8f;
                bool chevron = yy <= up && yy > up - 8, line = chevron && (yy > up - 1.2f || yy <= up - 7);
                byte paint = chevron && !line ? charge : field;
                g.Dot(xx, yy, zp + 1 - back, paint);
            }
        g.Grip = (cx / q, (top - H / 2f) / q, zp / (float)q);
        g.Tip = (cx / q, (top - H / 2f) / q, (zp + 2) / (float)q);
        g.Edge = (0, 0, 1);
    }
}
