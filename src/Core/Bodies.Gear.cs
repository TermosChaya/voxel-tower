using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What everyone carries, held forward from the right fist: picks, swords, axes, knives, mallets, clubs, bows, shields.</summary>
public static partial class Bodies
{
    // ---------------------------------------------------------------- what they carry (held forward from the right fist)

    /// <summary>A stable 0..1 for a cell of something sculpted: where the paint is worn, where the rust is.</summary>
    static float Dice(int x, int y, int z, int salt = 0) => (float)Hash.H3(x * 7 + salt, y * 13 - salt, z * 5 + salt * 3);

    /// <summary>
    /// Something held in the right fist, to be sculpted in its own cells: the cell just short of the middle of the fist
    /// each way (a haft two cells square fills it from there), and the fist's front. The grip is the fist's middle.
    /// </summary>
    static Gear InFist(Sculpt s, Build b, out int x, out int y, out int z0, bool fine = true)
    {
        var g = s.Hold(Main, DressKind.Weapon, Tissue.Gear, fine);
        int q = g.C.R;
        x = b.FistX * q + q / 2 - 1; y = b.FistY * q + q / 2 - 1; z0 = b.ArmZ1 * q;
        g.Grip = (b.FistX + .5f, b.FistY + .5f, (b.ArmZ0 + b.ArmZ1) / 2f);
        return g;
    }

    /// <summary>Where a cell of a piece of dress is, in half-voxels (its middle).</summary>
    static (float, float, float) Half(Gear g, float x, float y, float z) => (x / g.C.R, y / g.C.R, z / g.C.R);

    /// <summary>
    /// A pick: an ash haft through the fist, a leather grip behind it, and across its end an iron head in an eye round the
    /// haft, curving back toward the hand, drawn out to a point below and a chisel above, both ends honed bright.
    /// </summary>
    static void Pick(Sculpt s, Build b, byte head, int len = 12)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int e = z0 + len * 4, back = b.ArmZ0 * 4 - 3;
        g.Fill(x, y, back, x + 2, y + 2, e + 1, Mat.Handle);
        g.Wound(x, y, back, b.ArmZ0 * 4 + 2, 2, Mat.LeatherDark, Mat.Leather);
        for (int v = -16; v < 16; v++)
        {
            float a = MathF.Abs(v + .5f) / 16;
            int sweep = (int)MathF.Round(5 * a * a);
            bool point = v < 0;
            // the head thins toward its ends: four cells deep at the eye, one at the point, two at the chisel
            int deep = Math.Max(point ? 1 : 2, (int)MathF.Round(4.6f * (1 - a)));
            int thick = point && a > .7f ? 1 : 2;
            int zc = e - 2 - sweep;
            byte m = a > .8f ? Mat.SteelEdge : a > .62f ? Mat.Steel : head;
            for (int k = 0; k < deep; k++)
                for (int dx = 0; dx < thick; dx++) g.Dot(x + dx, y + 1 + v, zc - deep / 2 + k, m);
        }
        // the chisel's end flares a little; the eye round the haft
        g.Fill(x - 1, y + 15, e - 9, x + 3, y + 17, e - 6, Mat.SteelEdge);
        g.Fill(x - 1, y - 1, e - 5, x + 3, y + 3, e + 1, head);
        g.Tip = Half(g, x + 1, y - 15, e - 7);
    }

    /// <summary>
    /// A sword: a grip wound in two leathers through the fist, a wheel of a pommel behind it with a boss through it, a
    /// guard across the blade with its ends turned toward the point, and a blade narrowing to the point, its edges
    /// honed bright, a fuller dark down the first part of it; the edge below leads a cut as the arm comes down. A
    /// longsword's grip is long enough for both hands, its guard longer and its fuller shorter.
    /// </summary>
    static void Sword(Sculpt s, Build b, int len = 13, byte guard = Mat.Brass, bool longsword = false)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int q = 4, back = b.ArmZ0 * q - (longsword ? 10 : 3);
        g.Wound(x, y, back, z0, 2, Mat.LeatherDark, Mat.Leather);
        g.Wheel(x, 2, y + 1, back - 3, longsword ? 3.3f : 2.8f, guard);
        g.Fill(x - 1, y, back - 4, x, y + 2, back - 2, guard);
        g.Fill(x + 2, y, back - 4, x + 3, y + 2, back - 2, guard);
        // the guard, its quillons bending toward the point over their last three cells
        int hl = longsword ? 12 : 9;
        for (int dy = -hl; dy < hl; dy++)
        {
            int bend = Math.Max(0, (int)(MathF.Abs(dy + .5f) - (hl - 3.5f)));
            g.Fill(x, y + 1 + dy, z0 + bend, x + 2, y + 2 + dy, z0 + 2 + bend, guard);
        }
        g.Fill(x - 1, y - 1, z0, x + 3, y + 3, z0 + 3, guard);
        Blade(g, x, y + 1, z0 + 3, z0 + len * q, 3.1f, longsword ? .5f : .62f, Mat.Steel, false, 0);
        g.Tip = Half(g, x + 1, y + 1, z0 + len * q);
    }

    /// <summary>
    /// A blade along z from z0 to its point at z1, centred on the row boundary yc, flat to x: as broad as twice hw at the
    /// guard, narrowing slowly and then to the point; its edges a cell of honed steel, the middle two cells of steel
    /// twice as thick (a fuller cut dark down the first part of it, to <paramref name="fuller"/> of its length). An old
    /// one is notched along its edges and spotted with rust.
    /// </summary>
    static void Blade(Gear g, int x, int yc, int z0, int z1, float hw, float fuller, byte steel, bool old, int salt)
    {
        for (int z = z0; z < z1; z++)
        {
            float t = (z - z0) / (float)(z1 - z0);
            float w = t < .72f ? hw - t / .72f * (hw - 2.1f) : 2.1f * (1 - (t - .72f) / .28f);
            if (w < .5f) w = t < .97f ? .5f : 0;
            for (int dy = -5; dy < 5; dy++)
            {
                float a = MathF.Abs(dy + .5f);
                if (a > w + .01f) continue;
                bool edge = a + 1 > w, mid = a < 1;
                if (old && edge && Dice(x, yc + dy, z, salt) < .16f) continue;
                byte m = edge ? Mat.SteelEdge : mid && t < fuller ? Mat.SteelDark : steel;
                if (old && Dice(z >> 2, x, (yc + dy) >> 1, salt + 5) < .3f) m = Mat.Rust;
                g.Dot(x, yc + dy, z, m);
                if (!edge && !(mid && t < fuller)) g.Dot(x + 1, yc + dy, z, m);
            }
        }
    }

    /// <summary>
    /// A bearded axe on a long haft: an iron cap on its butt, a grip bound in hide, the head in an eye round the haft
    /// with its blade hanging below — square off the eye, its beard sweeping back down the haft, the toe out before the
    /// end, the edge honed — and a spike behind; on an orc's, old iron spotted with rust and the edge notched.
    /// </summary>
    static void Axe(Sculpt s, Build b, int len = 15)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int q = 4, e = z0 + len * q, butt = b.ArmZ0 * q - 4;
        g.Fill(x, y, butt, x + 2, y + 2, e + 2, Mat.Handle);
        g.Wound(x, y, butt, butt + 5, 2, Mat.Hide, Mat.LeatherDark);
        g.Wound(x, y, z0, z0 + 5, 2, Mat.Hide, Mat.LeatherDark);
        g.Fill(x - 1, y - 1, butt - 2, x + 3, y + 3, butt, Mat.Iron);
        g.Fill(x - 1, y - 1, e - 6, x + 3, y + 3, e + 2, Mat.Iron);
        const int B = 24;
        for (int v = 1; v <= B; v++)
        {
            float t = v / (float)B;
            int front = e + 2 + (int)MathF.Round(4 * t * t), rear = e - 6 - (int)MathF.Round(17 * t * t * t);
            for (int z = rear; z <= front; z++)
            {
                bool edge = v >= B - 1 || v > B - 6 && z <= rear + 1 || v > B - 9 && z >= front - 1;
                // two notches out of the edge
                if (v >= B - 2 && (z - rear) / 3 is 2 or 5 && Dice(z >> 2, 0, 0, 3) < .9f) continue;
                byte m = edge ? Mat.SteelEdge : Dice(z >> 2, v >> 2, x, 4) < .3f ? Mat.Rust : Mat.Iron;
                g.Dot(x, y - v, z, m);
                if (v <= 5 && !edge) g.Dot(x + 1, y - v, z, m);
            }
        }
        // the spike behind, rising from the eye and narrowing
        for (int h = 0; h < 7; h++)
        {
            int z1 = e - 1 - h / 2, za = e - 5 + h / 2;
            for (int z = za; z <= z1; z++) { g.Dot(x, y + 2 + h, z, Mat.Iron); if (h < 3) g.Dot(x + 1, y + 2 + h, z, Mat.Iron); }
        }
        g.Tip = Half(g, x + 1, y - B / 2f, e - 6);
    }

    /// <summary>
    /// A knife: a grip bound in rag or leather, an iron bolster, and a blade straight along its back with the edge below
    /// bellying out and sweeping up to the point; an old one notched and rusty.
    /// </summary>
    static void Knife(Sculpt s, Build b, bool old = true)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        int back = b.ArmZ0 * 4 - 2, L = 18;
        g.Wound(x, y, back, z0 + 1, 2, old ? Mat.Rag : Mat.LeatherDark, Mat.Leather);
        g.Fill(x - 1, y - 1, z0 + 1, x + 3, y + 3, z0 + 2, Mat.Iron);
        for (int k = 0; k < L; k++)
        {
            int z = z0 + 2 + k;
            float t = k / (float)L;
            int top = y + 2 - (t > .78f ? (int)MathF.Round((t - .78f) / .22f * 3) : 0);
            int bottom = y - 1 - (int)MathF.Round(1.9f * MathF.Sin(MathF.PI * t) - t * 1.4f);
            for (int yy = bottom; yy <= top; yy++)
            {
                bool edge = yy == bottom;
                if (old && edge && Dice(k, x, 1) < .18f) continue;
                byte m = edge ? Mat.SteelEdge : old ? (Dice(z >> 1, yy >> 1, x, 2) < .3f ? Mat.Rust : Mat.Iron) : Mat.Steel;
                g.Dot(x, yy, z, m);
                if (yy == top && t < .7f) g.Dot(x + 1, yy, z, m);
            }
        }
        g.Tip = Half(g, x + 1, y, z0 + 2 + L);
    }

    /// <summary>A mallet: a short handle and a round head of wood across its end, its end grain showing, bound with two iron hoops.</summary>
    static void Mallet(Sculpt s, Build b)
    {
        var g = InFist(s, b, out int x, out int y, out int z0);
        g.Fill(x, y, b.ArmZ0 * 4 - 2, x + 2, y + 2, z0 + 24, Mat.Handle);
        float cx = x + 1, cz = z0 + 28, r = 5.2f;
        for (int yy = y - 7; yy < y + 9; yy++)
        {
            int i = yy - (y - 7);
            bool hoop = i is 2 or 3 or 12 or 13, end = i is 0 or 15;
            float rr = hoop ? r + .7f : r;
            for (int z = (int)(cz - rr) - 1; z <= cz + rr; z++)
                for (int xx = (int)(cx - rr) - 1; xx <= cx + rr; xx++)
                    if ((xx + .5f - cx) * (xx + .5f - cx) + (z + .5f - cz) * (z + .5f - cz) <= rr * rr)
                        g.Dot(xx, yy, z, hoop ? Mat.Iron : end ? Mat.Board : Mat.Wood);
        }
        g.Tip = Half(g, cx, y - 7, cz);
    }

    /// <summary>
    /// A troll's club: a crooked young trunk with its bark on, thickening to a knot at its end, the stumps of boughs on
    /// it, iron nails driven through the knot and the shards of bones bound in; on a tamed troll's, iron bands round it.
    /// </summary>
    static void Club(Sculpt s, Build b, bool banded = false)
    {
        var g = InFist(s, b, out int x, out int y, out int z0, fine: false);
        // along its length (cells of 3 cm): where its middle is and how thick it is there
        float cx = x + 1, cy = y + 1;
        var line = new (float z, float dx, float dy, float r)[] { (b.ArmZ0 * 2 - 2, 0, 0, 2.4f), (z0 + 10, .6f, -.4f, 2.6f), (z0 + 20, -.6f, .5f, 3.4f), (z0 + 30, .5f, 1.2f, 5f), (z0 + 38, 0, 1.6f, 6f), (z0 + 43, -.4f, 1.4f, 4.6f) };
        for (int i = 0; i + 1 < line.Length; i++)
        {
            var (za, ax, ay, ra) = line[i]; var (zb, bx, by, rb) = line[i + 1];
            for (int z = (int)za; z < zb; z++)
            {
                float k = (z - za) / (zb - za), r = ra + (rb - ra) * k, mx = cx + ax + (bx - ax) * k, my = cy + ay + (by - ay) * k;
                for (int yy = (int)(my - r) - 1; yy <= my + r; yy++)
                    for (int xx = (int)(mx - r) - 1; xx <= mx + r; xx++)
                    {
                        float d = (xx + .5f - mx) * (xx + .5f - mx) + (yy + .5f - my) * (yy + .5f - my);
                        if (d > r * r) continue;
                        bool end = z >= zb - 1 && i == line.Length - 2;
                        g.Dot(xx, yy, z, end ? Mat.Snag : Dice(xx >> 1, yy >> 1, z >> 2, 9) < .2f ? Mat.PineBark : Mat.Trunk);
                    }
            }
        }
        // the stumps of two boughs
        g.Fill(x + 3, y, z0 + 18, x + 6, y + 2, z0 + 20, Mat.Trunk);
        g.Fill(x - 3, y + 3, z0 + 26, x - 1, y + 7, z0 + 28, Mat.Trunk);
        // nails through the knot, and shards of bone
        foreach (var (sx, sy, dz, m) in new[] { (-1, 0, 32, Mat.Iron), (1, 0, 35, Mat.Iron), (0, -1, 33, Mat.Iron), (0, 1, 36, Mat.Iron), (-1, 0, 38, Mat.Iron), (1, 0, 30, Mat.Iron),
            (0, -1, 37, Mat.Bone), (1, 0, 39, Mat.Bone), (0, 1, 40, Mat.Bone) })
        {
            // driven in from the side: out from the middle to the bark, and two cells standing out of it
            int px = x + 1, py = y + 2, z = z0 + dz;
            while (g.Has(px, py, z)) { px += sx; py += sy; }
            g.Dot(px, py, z, m); g.Dot(px + sx, py + sy, z, m);
            if (m == Mat.Bone) g.Dot(px + sx, py + sy, z + 1, m);
        }
        if (banded)
            foreach (int z in new[] { z0 + 28, z0 + 35 })
                for (int yy = y - 6; yy < y + 10; yy++)
                    for (int xx = x - 6; xx < x + 8; xx++)
                    {
                        if (!g.Has(xx, yy, z)) continue;
                        bool rim = !g.Has(xx + 1, yy, z) || !g.Has(xx - 1, yy, z) || !g.Has(xx, yy + 1, z) || !g.Has(xx, yy - 1, z);
                        if (rim) { g.Dot(xx, yy, z, Mat.Iron); g.Dot(xx, yy, z + 1, Mat.Iron); }
                    }
        g.Tip = Half(g, cx, cy + 1.5f, z0 + 38);
    }

    /// <summary>
    /// A bow in the left fist, its limbs along z and bending toward the string at the tips (held up, the arm forward, it
    /// stands upright): a two-toned stave, the grip wound in leather, horn at the nocks, the string tip to tip.
    /// </summary>
    static void Bow(Sculpt s, Build b)
    {
        var g = s.Hold(Off, DressKind.Weapon);
        int x = -b.FistX * 4 - 3, y = b.FistY * 4 + 1;
        g.Grip = (-b.FistX - .5f, b.FistY + .5f, (b.ArmZ0 + b.ArmZ1) / 2f);
        const int Half = 40, Brace = 11;
        for (int z = -Half; z < Half; z++)
        {
            float a = MathF.Abs(z + .5f) / Half;
            int yo = (int)MathF.Round(Brace * MathF.Pow(a, 1.8f));
            int th = a < .25f ? 3 : a < .65f ? 2 : 1, tx = a < .8f ? 2 : 1;
            for (int dy = 0; dy < th; dy++)
                for (int dx = 0; dx < tx; dx++)
                {
                    byte m = a > .94f ? Mat.Horn : MathF.Abs(z + .5f) < 9 ? (((z + dy) >> 1 & 1) == 0 ? Mat.LeatherDark : Mat.Leather) : dy == th - 1 ? Mat.Shaft : Mat.Handle;
                    g.Dot(x + dx, y + yo - (th - 1) / 2 + dy, z, m);
                }
        }
        for (int z = -Half + 1; z < Half - 1; z++) g.Dot(x, y + Brace, z, Mat.Linen);
        g.Tip = (-b.FistX - .5f, (y + Brace) / 4f, Half / 4f);
    }

    /// <summary>
    /// A round shield of planks on the left forearm, strapped to it: the planks with dark seams between them, painted in
    /// the garrison's blue with a gold cross worn through here and there to the wood, an iron rim standing proud of the
    /// face, a domed steel boss with a ring of brass rivets round it and more round the rim. It stands clear of the body
    /// in front of the arm.
    /// </summary>
    static void Shield(Sculpt s, Build b, byte field = Mat.ClothBlue, byte charge = Mat.ClothOchre)
    {
        var g = s.Hold(Off, DressKind.Shield, Tissue.Armor);
        const int q = 4;
        float cx = (-b.ArmX0 - b.ArmW / 2f) * q, cy = (b.HandY + b.Hand + 1.5f) * q, R = 4.7f * q;
        int zp = (b.Z1 + 2) * q + 1;
        Straps(g, b, (int)cx, zp);
        for (int yy = (int)(cy - R) - 1; yy <= cy + R; yy++)
            for (int xx = (int)(cx - R) - 1; xx <= cx + R; xx++)
            {
                float dx = xx + .5f - cx, dy = yy + .5f - cy, r = MathF.Sqrt(dx * dx + dy * dy);
                if (r > R) continue;
                if (r > R - 2.2f) { g.Fill(xx, yy, zp, xx + 1, yy + 1, zp + 3, Mat.Iron); continue; }
                int seam = (int)MathF.Floor(dx + 100) % 5;
                byte plank = seam == 0 ? Mat.BoardDark : Mat.Board;
                bool cross = MathF.Abs(dx) < 2.6f || MathF.Abs(dy) < 2.6f;
                byte paint = r > R - 6 && Dice(xx >> 2, yy >> 2, 0, 11) < .12f ? plank : cross ? charge : field;
                g.Dot(xx, yy, zp, plank);
                g.Dot(xx, yy, zp + 1, paint);
            }
        Boss(g, cx, cy, zp + 2);
        for (int i = 0; i < 8; i++)
        {
            float an = (i + .5f) * MathF.PI / 4;
            g.Dot((int)MathF.Floor(cx + MathF.Cos(an) * 7.5f), (int)MathF.Floor(cy + MathF.Sin(an) * 7.5f), zp + 2, Mat.Brass);
        }
        for (int i = 0; i < 8; i++)
        {
            float an = i * MathF.PI / 4;
            g.Dot((int)MathF.Floor(cx + MathF.Cos(an) * (R - 1.1f)), (int)MathF.Floor(cy + MathF.Sin(an) * (R - 1.1f)), zp + 3, Mat.Brass);
        }
        g.Grip = (cx / q, cy / q, zp / (float)q);
        g.Tip = (cx / q, cy / q, (zp + 6) / (float)q);
        g.Edge = (0, 0, 1);
    }

    /// <summary>A shield's boss: a steel dome in layers before its face, a flange round it.</summary>
    static void Boss(Gear g, float cx, float cy, int z)
    {
        g.Plate(z, 1, cx, cy, 5.6f, Mat.Steel);
        g.Plate(z + 1, 1, cx, cy, 4.6f, Mat.Steel);
        g.Plate(z + 2, 1, cx, cy, 3.6f, Mat.Steel);
        g.Plate(z + 3, 1, cx, cy, 2.1f, Mat.Steel);
    }

    /// <summary>The leather straps a shield is borne by, from the forearm's front to the shield's back at zp.</summary>
    static void Straps(Gear g, Build b, int cx, int zp)
    {
        int y0 = (b.HandY + 1) * 4;
        g.Fill(cx - 3, y0, b.ArmZ1 * 4, cx + 3, y0 + 3, zp, Mat.Leather);
        g.Fill(cx - 3, y0 + 6, b.ArmZ1 * 4, cx + 3, y0 + 9, zp, Mat.Leather);
    }
}
