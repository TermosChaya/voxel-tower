using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

public static partial class Bodies
{
    /// <summary>
    /// The body shaped out of its boxes, in the finer cells only (the half-voxels the blows cut stay as they were): the
    /// upright edges of the trunk, the arms and the legs taken off, so none of them is a box in section; the waist drawn
    /// in over the hips and the chest; the chest and the shoulder blades standing out; the shoulders sloping from the
    /// neck; at the top of the arm the shoulder's cap, the biceps before, the forearm out below the elbow, the wrist drawn
    /// in; the thigh before, the buttock behind, the knee drawn in, the calf behind, the ankle drawn in. The greater the
    /// body the deeper all of it. Not for a beast, a rider on his horse or an engine.
    /// </summary>
    static void Relief(Sculpt s, BodyPlan f, Build b)
    {
        if (!s.Detail || f.Beast || f.Mounted || f.Machine) return;
        const int Q = BodyPlan.K;
        var shaped = new[] { BodyPart.Body, BodyPart.Main, BodyPart.Off, BodyPart.LegR, BodyPart.LegL };
        var before = new PartGrid[shaped.Length];
        for (int i = 0; i < shaped.Length; i++) before[i] = s.FGrid(shaped[i]);
        // how deep it all goes, in cells: a cell for a man, more for a giant, a good deal for a god; the trunk's edges
        // taken off by a sixth of its breadth, a limb's by a quarter of its own
        int d = Math.Max(1, b.Chest * Q / 8), ch = Math.Max(1, b.Chest * Q / 6);

        // the trunk: broadest at the shoulders, drawn in down to the waist and out a little again at the hips (a wedge, not
        // a barrel); sloping from the neck out to the shoulders at the top; the chest before, the shoulder blades behind
        int tx0 = -b.Chest * Q, tx1 = b.Chest * Q, ty0 = b.Waist * Q, ty1 = b.Neck * Q, tz0 = b.Z0 * Q, tz1 = b.Z1 * Q;
        int vee = Math.Max(1, b.Chest * Q / 5), neck = Math.Max(2, b.HeadW * Q * 3 / 4);
        for (int y = ty0; y < ty1; y++)
        {
            float t = (y - ty0 + .5f) / (ty1 - ty0);
            int waist = t < .62f ? (int)MathF.Round(vee * (t < .15f ? .55f + .45f * t / .15f : 1 - (t - .15f) / .47f)) : 0;
            Narrow(s, BodyPart.Body, tx0, tx1, y, tz0, tz1, waist, 0, 0);
            if (t > .58f && t < .84f)
            {
                Swell(s, BodyPart.Body, tx0 + ch + d, -d, y, tz1, +1, d);
                Swell(s, BodyPart.Body, d, tx1 - ch - d, y, tz1, +1, d);
                if (t > .68f) { Swell(s, BodyPart.Body, tx0 + ch + 2 * d, -2 * d, y, tz0, -1, Math.Max(1, d - 1)); Swell(s, BodyPart.Body, 2 * d, tx1 - ch - 2 * d, y, tz0, -1, Math.Max(1, d - 1)); }
            }
            Chamfer(s, BodyPart.Body, tx0 + waist, tx1 - waist, y, tz0, tz1, ch);
        }

        // out past the neck, the top taken off in a slope down to the shoulders: from the top row down, and only where
        // nothing stands on it (a collar, a chain, the head's sides keep what is under them)
        for (int y = ty1 - 1; y >= ty0; y--)
        {
            float t = (y - ty0 + .5f) / (ty1 - ty0);
            if (t <= .84f) break;
            int keep = neck + (int)((tx1 - neck) * (1 - (t - .84f) / .16f));
            for (int x = tx0; x < tx1; x++)
                if (Math.Abs(x + .5f) > keep + .5f)
                    for (int z = tz0; z < tz1; z++)
                        if (!s.FHas(BodyPart.Body, x, y + 1, z)) s.FDel(BodyPart.Body, x, y, z);
        }

        // the head: the jaw drawn in toward the chin and the nape behind it, so the face narrows under the cheekbones
        int hx0 = -b.HeadW * Q, hx1 = b.HeadW * Q, hy0 = b.Neck * Q, hy1 = b.Crown * Q, hz0 = b.HeadZ * Q, hz1 = b.HZ1 * Q;
        int jaw = Math.Max(1, b.HeadW * Q / 4);
        for (int y = hy0; y < hy1; y++)
        {
            float t = (y - hy0 + .5f) / (hy1 - hy0);
            if (t > .36f) break;
            int j = (int)MathF.Round(jaw * (1 - t / .36f));
            if (j > 0) Narrow(s, BodyPart.Body, hx0, hx1, y, hz0, hz1, j, 0, j);
        }

        // the arms, from the wrist to the shoulder (the hand below has its own finer work)
        foreach (var arm in new[] { BodyPart.Main, BodyPart.Off })
        {
            bool right = arm == BodyPart.Main;
            int ax0 = right ? b.ArmX0 * Q : b.Chest * Q, ax1 = right ? -b.Chest * Q : -b.ArmX0 * Q, az0 = b.ArmZ0 * Q, az1 = b.ArmZ1 * Q;
            int ay0 = (b.HandY + b.Hand) * Q, ay1 = b.Neck * Q, outer = right ? -1 : +1;
            for (int y = ay0; y < ay1; y++)
            {
                float t = (y - ay0 + .5f) / (ay1 - ay0);
                if (t < .1f) Narrow(s, arm, ax0, ax1, y, az0, az1, 0, 1, 1);
                // the forearm out below the elbow, the biceps before, the shoulder's cap
                int ac = Math.Max(1, (ax1 - ax0) / 4), ad = Math.Max(1, (ax1 - ax0) / 5);
                if (t > .2f && t < .42f) SwellSide(s, arm, ax0, ax1, y, az0 + ac, az1 - ac, outer, ad);
                if (t > .58f && t < .8f) Swell(s, arm, ax0 + ac, ax1 - ac, y, az1, +1, ad);
                if (t > .82f)
                {
                    SwellSide(s, arm, ax0, ax1, y, az0 + ac, az1 - ac, outer, ad);
                    Swell(s, arm, ax0 + ac, ax1 - ac, y, az1, +1, ad);
                    Swell(s, arm, ax0 + ac, ax1 - ac, y, az0, -1, ad);
                }
                Chamfer(s, arm, ax0, ax1, y, az0, az1, Math.Max(1, (ax1 - ax0) / 4));
            }
        }

        // the legs, from the ankle (over the boot) to the hip
        int lz0 = -(b.LegD / 2) * Q, lz1 = (b.LegD - b.LegD / 2) * Q;
        foreach (var leg in new[] { BodyPart.LegR, BodyPart.LegL })
        {
            bool right = leg == BodyPart.LegR;
            int lx0 = right ? b.LegX0 * Q : -b.LegX1 * Q, lx1 = right ? b.LegX1 * Q : -b.LegX0 * Q, ly0 = Math.Max(1, b.Boot) * Q, ly1 = b.Leg * Q;
            for (int y = ly0; y < ly1; y++)
            {
                float t = (y - ly0 + .5f) / (ly1 - ly0);
                int lc = Math.Max(1, (lx1 - lx0) / 4), ld = Math.Max(1, (lx1 - lx0) / 5);
                if (t < .1f && b.Boot == 0) Narrow(s, leg, lx0, lx1, y, lz0, lz1, ld, 0, ld);
                if (t > .15f && t < .42f) Swell(s, leg, lx0 + lc, lx1 - lc, y, lz0, -1, ld);
                if (t > .46f && t < .54f) Narrow(s, leg, lx0, lx1, y, lz0, lz1, ld, 0, 0);
                if (t > .58f && t < .88f) Swell(s, leg, lx0 + lc, lx1 - lc, y, lz1, +1, ld);
                if (t > .82f) Swell(s, leg, lx0 + lc, lx1 - lc, y, lz0, -1, ld);
                Chamfer(s, leg, lx0, lx1, y, lz0, lz1, Math.Max(1, (lx1 - lx0) / 4));
            }
        }

        for (int i = 0; i < shaped.Length; i++) if (before[i] != null) Mend(s, shaped[i], before[i]);
    }

    /// <summary>
    /// What the shaping left hanging by nothing put back on: a strap, a collar, a flap of hide that hung by a corner the
    /// shaping took off (the blows go by half-voxels, and a half-voxel all of whose cells went is gone to them). Every
    /// half-voxel not with the most of the part has the cells taken off beside it given back, until it hangs on again.
    /// </summary>
    static void Mend(Sculpt s, BodyPart p, PartGrid was)
    {
        const int Q = BodyPlan.K;
        static int Half(int v) => v >= 0 ? v / Q : -((-v + Q - 1) / Q);
        for (int round = 0; round < 12; round++)
        {
            var g = s.FGrid(p);
            if (g == null) return;
            var held = new HashSet<(int, int, int)>();
            for (int i = 0; i < g.M.Length; i++)
                if (g.M[i] != 0) held.Add((Half(i % g.SX + g.X0), Half(i / (g.SX * g.SZ) + g.Y0), Half(i / g.SX % g.SZ + g.Z0)));
            // the half-voxels by the pieces they hang together in, the greatest piece the part itself
            var piece = new Dictionary<(int, int, int), int>();
            var sizes = new List<int>();
            var faces = new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
            foreach (var h in held)
            {
                if (piece.ContainsKey(h)) continue;
                int id = sizes.Count, n = 0;
                var q = new Queue<(int, int, int)>();
                q.Enqueue(h); piece[h] = id;
                while (q.Count > 0)
                {
                    var (x, y, z) = q.Dequeue(); n++;
                    foreach (var (dx, dy, dz) in faces)
                    {
                        var o = (x + dx, y + dy, z + dz);
                        if (held.Contains(o) && !piece.ContainsKey(o)) { piece[o] = id; q.Enqueue(o); }
                    }
                }
                sizes.Add(n);
            }
            if (sizes.Count <= 1) return;
            int main = 0;
            for (int k = 1; k < sizes.Count; k++) if (sizes[k] > sizes[main]) main = k;
            // the cells taken off beside a piece hanging loose, given back
            bool gave = false;
            for (int i = 0; i < was.M.Length; i++)
            {
                if (was.M[i] == 0) continue;
                int fx = i % was.SX + was.X0, fy = i / (was.SX * was.SZ) + was.Y0, fz = i / was.SX % was.SZ + was.Z0;
                var h = (Half(fx), Half(fy), Half(fz));
                if (held.Contains(h) || s.FHas(p, fx, fy, fz)) continue;
                foreach (var (dx, dy, dz) in faces)
                    if (piece.TryGetValue((h.Item1 + dx, h.Item2 + dy, h.Item3 + dz), out int id) && id != main)
                    {
                        s.FSet(p, fx, fy, fz, was.M[i], (Tissue)was.T[i]);
                        gave = true;
                        break;
                    }
            }
            if (!gave) return;
        }
    }

    /// <summary>A row of a box (fine cells) drawn in: so many cells off each side, off the front, off the back.</summary>
    static void Narrow(Sculpt s, BodyPart p, int x0, int x1, int y, int z0, int z1, int sides, int front, int back)
    {
        for (int i = 0; i < sides; i++)
            for (int z = z0; z < z1; z++) { s.FDel(p, x0 + i, y, z); s.FDel(p, x1 - 1 - i, y, z); }
        for (int i = 0; i < front; i++)
            for (int x = x0; x < x1; x++) s.FDel(p, x, y, z1 - 1 - i);
        for (int i = 0; i < back; i++)
            for (int x = x0; x < x1; x++) s.FDel(p, x, y, z0 + i);
    }

    /// <summary>The upright edges of a row of a box taken off, a corner of so many cells at each.</summary>
    static void Chamfer(Sculpt s, BodyPart p, int x0, int x1, int y, int z0, int z1, int c)
    {
        for (int i = 0; i < c; i++)
            for (int j = 0; i + j < c; j++)
            {
                s.FDel(p, x0 + i, y, z0 + j); s.FDel(p, x1 - 1 - i, y, z0 + j);
                s.FDel(p, x0 + i, y, z1 - 1 - j); s.FDel(p, x1 - 1 - i, y, z1 - 1 - j);
            }
    }

    /// <summary>
    /// A row's face standing out before (+1, past z) or behind (−1, before z) by so many cells, between two x: each cell
    /// of what is under it there, where something is under it.
    /// </summary>
    static void Swell(Sculpt s, BodyPart p, int x0, int x1, int y, int z, int way, int by)
    {
        for (int x = x0; x < x1; x++)
        {
            int inner = way > 0 ? z - 1 : z;
            if (!s.FGet(p, x, y, inner, out byte m, out var t) || t == Tissue.Gear) continue;
            for (int k = 0; k < by; k++)
            {
                int at = way > 0 ? z + k : z - 1 - k;
                if (!s.FHas(p, x, y, at)) s.FSet(p, x, y, at, m, t);
            }
        }
    }

    /// <summary>A row's side standing out (−1 past x0, +1 past x1) by so many cells, between two z.</summary>
    static void SwellSide(Sculpt s, BodyPart p, int x0, int x1, int y, int z0, int z1, int way, int by)
    {
        for (int z = z0; z < z1; z++)
        {
            int inner = way > 0 ? x1 - 1 : x0;
            if (!s.FGet(p, inner, y, z, out byte m, out var t) || t == Tissue.Gear) continue;
            for (int k = 0; k < by; k++)
            {
                int at = way > 0 ? x1 + k : x0 - 1 - k;
                if (!s.FHas(p, at, y, z)) s.FSet(p, at, y, z, m, t);
            }
        }
    }
}
