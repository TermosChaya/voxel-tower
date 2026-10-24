using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A body once it has been hurt: its own copy of its half-voxels with what the blows carved out of them, what still
/// works (the hand that holds the weapon, the one that holds the shield or the bow, the legs), how fast it bleeds, and
/// the arrows stuck in it.
/// </summary>
public sealed class BodyState
{
    public readonly BodyPlan Plan;
    public readonly PartGrid[] Grid = new PartGrid[BodyPlan.Parts];
    /// <summary>Goes up with every wound: the picture of it is drawn again.</summary>
    public int Rev;
    public bool MainHand = true, OffHand = true, Shield;
    public int Legs = 2;
    /// <summary>Hit points lost a second; it slows as the blood clots.</summary>
    public float Bleed;
    /// <summary>Arrows stuck in it: the part, where the arrow went in and which way it points, in figure voxels.</summary>
    public readonly List<(BodyPart part, float x, float y, float z, float dx, float dy, float dz)> Arrows = new();
    /// <summary>The worst of it, for the panel: "отрублена правая рука".</summary>
    public readonly List<string> Wounds = new();
    /// <summary>The bones broken (Sim.Trauma.cs), those a comrade splinted, and how far each has knit, 0…1 (legs, arms, ribs).</summary>
    public Break Broken, Splinted;
    public readonly float[] Knit = new float[5];

    public BodyState(BodyPlan plan)
    {
        Plan = plan;
        for (int p = 0; p < BodyPlan.Parts; p++) Grid[p] = plan.Grid[p]?.Clone();
        Shield = plan.Shield;
    }

    public byte Mat(int x, int y, int z, out int part)
    {
        for (part = 0; part < BodyPlan.Parts; part++)
        {
            var g = Grid[part];
            if (g != null && g.In(x, y, z) && g.M[g.Index(x, y, z)] != 0) return g.M[g.Index(x, y, z)];
        }
        part = -1;
        return 0;
    }
}

/// <summary>A piece cut off a body — a hand, an arm, a leg, a head — lying where it fell.</summary>
public sealed class Remnant
{
    /// <summary>Its half-voxels, in the figure coordinates of the body it came from.</summary>
    public PartGrid Grid;
    public BodyPart From;
    /// <summary>Where that body stood and how it faced when the piece came off, and how big it was.</summary>
    public float X, Y, Z, Facing, Scale = 1;
    public double At;
    public Unit Of;
}

public sealed partial class Sim
{
    public readonly List<Remnant> Remnants = new();
    /// <summary>A piece came off a body.</summary>
    public event Action<Remnant> Severed;
    /// <summary>A blow drew blood: where (world voxels) and how much (half-voxels of flesh cut). Not for a blade's blow laid out along its stroke: <see cref="Smote"/> tells of that.</summary>
    public Action<float, float, float, int> Splashed;

    /// <summary>How a weapon cuts: a sweep through a plane, a deep narrow chop, a thrust, a crushing blow, an arrow.</summary>
    public enum Cut : byte { Slash, Chop, Stab, Crush, Arrow }

    /// <summary>
    /// What a kind strikes with: how it cuts, how deep it goes (half-voxels of flesh, 6 cm each) and how wide it sweeps:
    /// a sword bites 20 cm deep across a hand's breadth either way, an axe deeper and narrower, a club crushes a ball.
    /// </summary>
    public static (Cut cut, float depth, float reach) WeaponOf(UnitType t) => t.Tool switch
    {
        "sword" => (Cut.Slash, 3.5f, 4),
        "axe" => (Cut.Chop, 4.5f, 2.5f),
        "knife" => (Cut.Stab, 3, 1),
        "club" => (Cut.Crush, 6, 3.5f),
        "pick" => (Cut.Stab, 4, 1),
        "mallet" => (Cut.Crush, 2.5f, 2),
        "bow" => (Cut.Arrow, 4.5f, 0),
        // the troops': a spear and a lance thrust deep and narrow, a halberd chops deeper than an axe, a mace and a maul
        // crush, a long sword sweeps wider than a short one; a bolt goes in deeper than an arrow, a sling stone bruises
        // a little round and stays out; a hound's teeth go in narrow
        "spear" => (Cut.Stab, 5, 1),
        "lance" => (Cut.Stab, 6, 1.2f),
        "halberd" => (Cut.Chop, 6, 3),
        "mace" => (Cut.Crush, 4, 2.5f),
        "maul" => (Cut.Crush, 7, 4.5f),
        "longsword" => (Cut.Slash, 4, 4.5f),
        "crossbow" => (Cut.Arrow, 6, 0),
        "sling" => (Cut.Crush, 2, 1.5f),
        "bombard" => (Cut.Crush, 2, 1.5f),
        "bite" => (Cut.Stab, 3.5f, 1.2f),
        _ => (Cut.Stab, 3, 1),
    };

    /// <summary>Half-voxels the last blow carved out of a body (tests, and the chips that fly).</summary>
    public int LastCut { get; private set; }

    /// <summary>
    /// How much a half-voxel of something stops a blow: flesh gives, bone less, mail much less and plate hardly at all;
    /// a club does not cut, it breaks bones and stoves in mail and plate alike.
    /// </summary>
    static float Toughness(byte m, Tissue t, Cut cut) => t switch
    {
        Tissue.Bone => cut == Cut.Crush ? 1.2f : 2.2f,
        Tissue.Gear => 6,
        Tissue.Armor when cut == Cut.Crush => 1.6f,
        Tissue.Armor => m is Mat.Steel or Mat.Iron or Mat.Brass ? 8 : m == Mat.Mail ? 4 : 2.5f,
        _ => 1,
    };

    /// <summary>What still works of a body (all of it, for one never hurt): a hand cut off, or its arm broken, holds nothing.</summary>
    public static bool HasMainHand(Unit u) => u.Wounds == null || u.Wounds.MainHand && (u.Wounds.Broken & Break.Main) == 0;
    public static bool HasOffHand(Unit u) => u.Wounds == null || u.Wounds.OffHand && (u.Wounds.Broken & Break.Off) == 0;

    /// <summary>Can still fight: a hand for the weapon, and both for a bow.</summary>
    public static bool Armed(Unit u) => !Kinds.IsEngine(u.T) && (u.Wounds == null || HasMainHand(u) && (!u.T.Ranged || HasOffHand(u)));

    /// <summary>
    /// How well it walks: on two legs, hopping on one, or dragging itself; limping on a broken leg (a little better
    /// splinted), crawling on two; slower with its ribs broken (Sim.Trauma.cs).
    /// </summary>
    public static float Gait(Unit u)
    {
        var w = u.Wounds;
        if (w == null) return 1;
        float g = w.Legs switch { >= 2 => 1, 1 => .4f, _ => .15f };
        var legs = w.Plan.Beast ? Break.LegR | Break.LegL | Break.Main | Break.Off : Break.LegR | Break.LegL;
        var broken = w.Broken & legs;
        if (broken != Break.None)
        {
            int n = System.Numerics.BitOperations.PopCount((uint)broken);
            g *= n >= 2 ? .12f : w.Plan.Beast ? .5f : (w.Splinted & broken) != 0 ? .45f : .35f;
        }
        if ((w.Broken & Break.Ribs) != 0) g *= .8f;
        return g;
    }

    static float ScaleOf(Unit u) => u.Leader ? 1.15f : 1;

    /// <summary>A point in the world (voxels) in the body's own half-voxels, as it stands at rest.</summary>
    static (float x, float y, float z) ToBody(Unit u, float wx, float wy, float wz)
    {
        float s = BodyPlan.Sub / ScaleOf(u), sin = MathF.Sin(u.Facing), cos = MathF.Cos(u.Facing);
        wx -= u.X; wy -= u.Y; wz -= u.Z;
        return ((wx * sin - wz * cos) * s, wy * s, (wx * cos + wz * sin) * s);
    }

    /// <summary>A direction in the world in the body's own frame.</summary>
    static (float x, float y, float z) DirToBody(Unit u, float dx, float dy, float dz)
    {
        float sin = MathF.Sin(u.Facing), cos = MathF.Cos(u.Facing);
        return (dx * sin - dz * cos, dy, dx * cos + dz * sin);
    }

    /// <summary>
    /// A blow lands on a body, coming from a point (world voxels) along a direction: the voxels it strikes are carved
    /// out of it as the weapon cuts — a plane for a sweep, a wedge for a chop, a narrow channel for a thrust or an arrow,
    /// a ball for a club — as deep as the weapon gets through what is there (flesh gives, bone and mail less, plate
    /// hardly). What is cut off falls; a head split or cut off, a heart pierced, a body cut through is death. Returns
    /// the share of the blow that got to the flesh.
    /// </summary>
    public float Wound(Unit b, Cut cut, float depth, float reach, float ox, float oy, float oz, float dx, float dy, float dz)
        => Wound(b, cut, depth, reach, ox, oy, oz, dx, dy, dz, float.NaN, 0, 0);

    /// <summary>What the last blow struck first, where (world voxels), and how much flesh it cut (half-voxels); air for a blow that met nothing.</summary>
    public byte LastMat { get; private set; }
    /// <summary>The part the last wound was cut into first, and the half-voxel row it was struck at.</summary>
    public BodyPart LastPart { get; private set; }
    public int LastRow { get; private set; }
    public Vec LastAt { get; private set; }
    public int LastFlesh { get; private set; }

    /// <summary>
    /// A blow whose blade is known: (ox…oz) is now its root and (ex…ez) runs from there to its point (world voxels), and
    /// the whole of it goes along the way of the blow; it strikes where any of it first meets the body. The sweep follows
    /// the edge — the cut lies in the plane of the way the blade goes and the line of its edge, give or take a little —
    /// instead of a tilt at random; and a blade that passes the body by close is taken on in to it. Without a blade (NaN)
    /// it is the plain blow from a point.
    /// </summary>
    public float Wound(Unit b, Cut cut, float depth, float reach, float ox, float oy, float oz, float dx, float dy, float dz, float ex, float ey, float ez)
    {
        LastMat = Mat.Air; LastFlesh = 0;
        if (b.Dead) return 0;
        var body = b.Wounds ??= new BodyState(Bodies.Plan(b.T, b.Look));
        float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1e-4f) return 1;
        bool edged = !float.IsNaN(ex);
        var (px, py, pz) = ToBody(b, ox, oy, oz);
        var (lx, ly, lz) = DirToBody(b, dx / len, dy / len, dz / len);
        // where it strikes: the first voxel on its way
        int ix = 0, iy = 0, iz = 0;
        bool hit = false;
        if (edged)
        {
            // the blade's length, root to point, each spot of it on along the way: whichever meets the body first
            float el = MathF.Sqrt(ex * ex + ey * ey + ez * ez), span = el * BodyPlan.Sub / ScaleOf(b);
            var (ux, uy, uz) = el < 1e-4f ? (0f, 0f, 0f) : DirToBody(b, ex / el, ey / el, ez / el);
            int n = Math.Clamp((int)(span / 1.5f) + 1, 1, 14);
            float first = 140;
            // the body's box, a half-voxel over all round: no spot of the way outside it strikes anything
            float bx0 = float.MaxValue, by0 = float.MaxValue, bz0 = float.MaxValue, bx1 = float.MinValue, by1 = float.MinValue, bz1 = float.MinValue;
            foreach (var g in body.Grid)
            {
                if (g == null) continue;
                bx0 = Math.Min(bx0, g.X0 - 1); by0 = Math.Min(by0, g.Y0 - 1); bz0 = Math.Min(bz0, g.Z0 - 1);
                bx1 = Math.Max(bx1, g.X0 + g.SX + 1); by1 = Math.Max(by1, g.Y0 + g.SY + 1); bz1 = Math.Max(bz1, g.Z0 + g.SZ + 1);
            }
            // how far along the way from q it is in the box
            bool Through(float q, float l, float lo, float hi, ref float tin, ref float tout)
            {
                if (MathF.Abs(l) < 1e-6f) return q >= lo && q <= hi;
                float t0 = (lo - q) / l, t1 = (hi - q) / l;
                if (t0 > t1) (t0, t1) = (t1, t0);
                tin = Math.Max(tin, t0); tout = Math.Min(tout, t1);
                return tin <= tout;
            }
            for (int j = 0; j < n; j++)
            {
                float o = n == 1 ? 0 : span * j / (n - 1), qx = px + ux * o, qy = py + uy * o, qz = pz + uz * o;
                float tin = 0, tout = first;
                if (!Through(qx, lx, bx0, bx1, ref tin, ref tout) || !Through(qy, ly, by0, by1, ref tin, ref tout) || !Through(qz, lz, bz0, bz1, ref tin, ref tout)) continue;
                for (float t = MathF.Floor(tin * 2) / 2; t < first && t <= tout; t += .5f)
                {
                    int x = (int)MathF.Floor(qx + lx * t), y = (int)MathF.Floor(qy + ly * t), z = (int)MathF.Floor(qz + lz * t);
                    if (body.Mat(x, y, z, out _) == 0) continue;
                    first = t; ix = x; iy = y; iz = z; hit = true;
                    break;
                }
            }
            if (!hit) { px += ux * span * .7f; py += uy * span * .7f; pz += uz * span * .7f; }
        }
        else
            for (float t = 0; t < 140; t += .5f)
            {
                float x = px + lx * t, y = py + ly * t, z = pz + lz * t;
                ix = (int)MathF.Floor(x); iy = (int)MathF.Floor(y); iz = (int)MathF.Floor(z);
                if (body.Mat(ix, iy, iz, out _) != 0) { hit = true; break; }
            }
        // a stroke that came to the body's round beside it goes on in toward its middle, from where it came nearest
        if (edged && !hit)
        {
            float hl2 = lx * lx + lz * lz, tq = hl2 > 1e-4f ? MathF.Max(0, -(px * lx + pz * lz) / hl2) : 0;
            float qx = px + lx * tq, qy = py + ly * tq, qz = pz + lz * tq, ql = MathF.Max(1e-3f, MathF.Sqrt(qx * qx + qz * qz));
            if (body.Plan.Grid[0] is { } trunk) qy = Math.Clamp(qy, trunk.Y0 + 1, trunk.Y0 + trunk.SY - 2);
            for (float t = 0; t < ql + 4 && !hit; t += .5f)
            {
                ix = (int)MathF.Floor(qx - qx / ql * t); iy = (int)MathF.Floor(qy); iz = (int)MathF.Floor(qz - qz / ql * t);
                if (body.Mat(ix, iy, iz, out _) != 0) hit = true;
            }
        }
        if (!hit) { LastCut = 0; LastFlesh = 0; LastMat = Mat.Air; LastPart = BodyPart.Body; LastRow = -1; return .5f; }
        LastMat = body.Mat(ix, iy, iz, out int lastPart);
        LastPart = (BodyPart)lastPart; LastRow = iy;
        float cx = ix + .5f, cy = iy + .5f, cz = iz + .5f;
        float sx, sy, sz;
        if (edged)
        {
            // the sweep of this blade: along its edge, square to the way it goes in, turned a little either way
            var (ax, ay, az) = DirToBody(b, ex, ey, ez);
            float along = ax * lx + ay * ly + az * lz;
            ax -= lx * along; ay -= ly * along; az -= lz * along;
            float al = MathF.Sqrt(ax * ax + ay * ay + az * az);
            if (al < 1e-3f) { ax = -lz; ay = 0; az = lx; al = MathF.Max(1e-3f, MathF.Sqrt(ax * ax + az * az)); }
            ax /= al; ay /= al; az /= al;
            float turn = (float)(Rng.NextDouble() - .5) * .3f, c = MathF.Cos(turn), s = MathF.Sin(turn);
            // turned about the way in: a·cos + (l×a)·sin
            float qx = ly * az - lz * ay, qy = lz * ax - lx * az, qz = lx * ay - ly * ax;
            sx = ax * c + qx * s; sy = ay * c + qy * s; sz = az * c + qz * s;
        }
        else
        {
            // the sweep of a blade: across the way it comes, tilted anywhere from flat to steep
            float tilt = (float)(Rng.NextDouble() - .5) * 1.8f;
            float hx = -lz, hz = lx, hl = MathF.Sqrt(hx * hx + hz * hz);
            if (hl < 1e-3f) { hx = 1; hz = 0; hl = 1; }
            hx /= hl; hz /= hl;
            sx = hx * MathF.Cos(tilt); sy = MathF.Sin(tilt); sz = hz * MathF.Cos(tilt);
        }
        // the plane's normal: across both the way in and the sweep
        float nx = ly * sz - lz * sy, ny = lz * sx - lx * sz, nz = lx * sy - ly * sx;
        // the voxels it can reach, by how deep they lie along the blow
        int r = (int)MathF.Ceiling(Math.Max(depth, reach)) + 2;
        var layers = new SortedDictionary<int, List<(int part, int i)>>();
        for (int p = 0; p < BodyPlan.Parts; p++)
        {
            var g = body.Grid[p];
            if (g == null) continue;
            for (int y = Math.Max(g.Y0, iy - r); y <= Math.Min(g.Y0 + g.SY - 1, iy + r); y++)
                for (int z = Math.Max(g.Z0, iz - r); z <= Math.Min(g.Z0 + g.SZ - 1, iz + r); z++)
                    for (int x = Math.Max(g.X0, ix - r); x <= Math.Min(g.X0 + g.SX - 1, ix + r); x++)
                    {
                        int i = g.Index(x, y, z);
                        if (g.M[i] == 0) continue;
                        float vx = x + .5f - cx, vy = y + .5f - cy, vz = z + .5f - cz;
                        float along = vx * lx + vy * ly + vz * lz;
                        if (along < -1 || along > depth + 1) continue;
                        bool inside = cut switch
                        {
                            Cut.Slash or Cut.Chop => MathF.Abs(vx * nx + vy * ny + vz * nz) <= (cut == Cut.Chop ? .9f : .7f)
                                && MathF.Abs(vx * sx + vy * sy + vz * sz) <= reach,
                            Cut.Crush => vx * vx + vy * vy + vz * vz <= reach * reach,
                            _ => Across(vx, vy, vz, lx, ly, lz, along) <= (cut == Cut.Arrow ? .6f : .9f),
                        };
                        if (!inside) continue;
                        int layer = (int)MathF.Floor(Math.Max(0, along));
                        if (!layers.TryGetValue(layer, out var list)) layers[layer] = list = new List<(int, int)>();
                        list.Add((p, i));
                    }
        }
        // the weapon goes in layer by layer, losing its force on what it meets
        float power = depth;
        int flesh = 0, armour = 0;
        var removed = new List<(int part, int x, int y, int z)>();
        foreach (var (_, list) in layers)
        {
            if (power <= 0) break;
            float tough = 0;
            foreach (var (p, i) in list)
            {
                var g = body.Grid[p];
                tough = Math.Max(tough, Toughness(g.M[i], (Tissue)g.T[i], cut));
            }
            // what is left of the blow has to be up to half of what is in the way, or it stops there
            if (power < tough * .5f) break;
            power -= tough;
            foreach (var (p, i) in list)
            {
                var g = body.Grid[p];
                var t = (Tissue)g.T[i];
                if (t == Tissue.Gear) continue;
                if (t == Tissue.Armor) armour++; else flesh++;
                int x = i % g.SX + g.X0, z = i / g.SX % g.SZ + g.Z0, y = i / (g.SX * g.SZ) + g.Y0;
                removed.Add((p, x, y, z));
            }
        }
        foreach (var (p, x, y, z) in removed) body.Grid[p].Clear(x, y, z);
        LastCut = removed.Count;
        LastFlesh = flesh;
        float wk = ScaleOf(b) / BodyPlan.Sub, wsin = MathF.Sin(b.Facing), wcos = MathF.Cos(b.Facing);
        LastAt = new Vec(b.X + (cx * wsin + cz * wcos) * wk, b.Y + cy * wk, b.Z + (-cx * wcos + cz * wsin) * wk);
        // blood on the skin round the wound
        foreach (var (p, x, y, z) in removed)
            foreach (var (ax, ay, az) in Around)
            {
                var g = body.Grid[p];
                int qx = x + ax, qy = y + ay, qz = z + az;
                if (!g.In(qx, qy, qz)) continue;
                int q = g.Index(qx, qy, qz);
                if (g.M[q] != 0 && g.T[q] == (byte)Tissue.Skin && Hash.H3(qx * 3 + qy, qz, b.Id) < .3) g.M[q] = Mat.Blood;
            }
        // (a blade's blood is thrown by the eye along the way it went, as it is seen to get there)
        if (flesh > 0 && Splashed != null && !edged)
        {
            // back from the body's half-voxels to the world
            float k = ScaleOf(b) / BodyPlan.Sub, sin = MathF.Sin(b.Facing), cos = MathF.Cos(b.Facing);
            Splashed(b.X + (cx * sin + cz * cos) * k, b.Y + cy * k, b.Z + (-cx * cos + cz * sin) * k, flesh);
        }
        if (cut == Cut.Arrow && flesh > 0)
        {
            var (part, x, y, z) = removed[^1];
            body.Arrows.Add(((BodyPart)part, (x + .5f) / BodyPlan.Sub, (y + .5f) / BodyPlan.Sub, (z + .5f) / BodyPlan.Sub, lx, ly, lz));
        }
        body.Bleed = Math.Min(12, body.Bleed + flesh * .05f);
        body.Rev++;
        bool vital = Sever(b, body);
        // the brain and the heart do not take much: a fist's worth of brain, a couple of half-voxels of heart
        if (body.Plan.Brain > 0 && body.Plan.Brain - Count(body, Tissue.Brain) >= Math.Max(4, body.Plan.Brain * .12f)) { vital = true; Note(body, "разбита голова"); }
        // a pierced heart does not stop at once: the blood goes in a few heartbeats
        if (body.Plan.Heart > 0 && !body.Wounds.Contains("пробито сердце") && body.Plan.Heart - Count(body, Tissue.Heart) >= Math.Max(2, body.Plan.Heart * .1f))
        {
            Note(body, "пробито сердце");
            body.Bleed = Math.Min(12, body.Bleed + b.MaxHp / 8);
        }
        if (vital) { Hurt(b, null, b.Hp + 1, "смертельная рана"); return 1; }
        return flesh == 0 ? .25f : Math.Min(1, .6f + flesh * .05f);
    }

    static float Across(float vx, float vy, float vz, float lx, float ly, float lz, float along)
    {
        float ax = vx - lx * along, ay = vy - ly * along, az = vz - lz * along;
        return MathF.Sqrt(ax * ax + ay * ay + az * az);
    }

    static int Count(BodyState body, Tissue t)
    {
        int n = 0;
        foreach (var g in body.Grid)
            if (g != null)
                foreach (byte k in g.T) if (k == (byte)t) n++;
        return n;
    }

    static void Note(BodyState body, string wound) { if (!body.Wounds.Contains(wound)) body.Wounds.Add(wound); }

    /// <summary>
    /// Each thing carried (a run of gear touching itself) stays with the side of the cut it holds on to most: a sword or
    /// a bow goes with the fist round its grip, a quiver with the back it is slung on, even where it touches the head too.
    /// One that touches nothing kept goes with what fell.
    /// </summary>
    static void Carried(PartGrid g, bool[] kept)
    {
        var seen = new bool[g.M.Length];
        var run = new List<int>();
        var q = new Queue<int>();
        for (int i0 = 0; i0 < g.M.Length; i0++)
        {
            if (seen[i0] || g.M[i0] == 0 || g.T[i0] != (byte)Tissue.Gear) continue;
            run.Clear();
            seen[i0] = true; q.Enqueue(i0);
            int stays = 0, goes = 0;
            while (q.Count > 0)
            {
                int i = q.Dequeue(), x = i % g.SX, z = i / g.SX % g.SZ, y = i / (g.SX * g.SZ);
                run.Add(i);
                foreach (var (ax, ay, az) in Around)
                {
                    int nx = x + ax, ny = y + ay, nz = z + az;
                    if (nx < 0 || ny < 0 || nz < 0 || nx >= g.SX || ny >= g.SY || nz >= g.SZ) continue;
                    int n = (ny * g.SZ + nz) * g.SX + nx;
                    if (g.M[n] == 0) continue;
                    if (g.T[n] == (byte)Tissue.Gear) { if (!seen[n]) { seen[n] = true; q.Enqueue(n); } }
                    else if (kept[n]) stays++;
                    else goes++;
                }
            }
            if (stays > 0 && stays >= goes) foreach (int i in run) kept[i] = true;
        }
    }

    /// <summary>
    /// Whatever no longer hangs together with the rest of its part falls off: a hand, an arm, a foot, a leg, the head. Then
    /// what still works is worked out again. True when what fell (the head, half the trunk) is death.
    /// </summary>
    internal bool Sever(Unit u, BodyState body)
    {
        bool vital = false;
        var plan = body.Plan;
        for (int p = 0; p < BodyPlan.Parts; p++)
        {
            var g = body.Grid[p];
            if (g == null || g.Count == 0) continue;
            // from the joint (for the trunk, the hips) through what is left
            float jx = plan.JX[p] * BodyPlan.Sub, jy = plan.JY[p] * BodyPlan.Sub, jz = plan.JZ[p] * BodyPlan.Sub;
            int seed = -1; float best = 9;
            for (int i = 0; i < g.M.Length; i++)
            {
                if (g.M[i] == 0 || g.T[i] == (byte)Tissue.Gear) continue;
                float x = i % g.SX + g.X0 + .5f - jx, z = i / g.SX % g.SZ + g.Z0 + .5f - jz, y = i / (g.SX * g.SZ) + g.Y0 + .5f - jy;
                float d = x * x + y * y + z * z;
                if (d < best) { best = d; seed = i; }
            }
            // what is carried does not hold flesh together: the body first, through itself alone
            var kept = new bool[g.M.Length];
            if (seed >= 0)
            {
                var q = new Queue<int>();
                q.Enqueue(seed); kept[seed] = true;
                while (q.Count > 0)
                {
                    int i = q.Dequeue(), x = i % g.SX, z = i / g.SX % g.SZ, y = i / (g.SX * g.SZ);
                    foreach (var (ax, ay, az) in Around)
                    {
                        int nx = x + ax, ny = y + ay, nz = z + az;
                        if (nx < 0 || ny < 0 || nz < 0 || nx >= g.SX || ny >= g.SY || nz >= g.SZ) continue;
                        int n = (ny * g.SZ + nz) * g.SX + nx;
                        if (g.M[n] == 0 || kept[n] || g.T[n] == (byte)Tissue.Gear) continue;
                        kept[n] = true;
                        q.Enqueue(n);
                    }
                }
                Carried(g, kept);
            }
            int off = 0, offHead = 0, head = 0;
            for (int i = 0; i < g.M.Length; i++)
            {
                if (g.M[i] == 0) continue;
                int y = i / (g.SX * g.SZ) + g.Y0;
                // the head is what grows there: a quiver's top beside it or a ladder over it stays with the back
                bool inHead = p == 0 && y >= plan.HeadY0 && y < plan.HeadY1 && g.T[i] != (byte)Tissue.Gear;
                if (inHead) head++;
                if (kept[i]) continue;
                off++;
                if (inHead) offHead++;
            }
            if (off == 0) continue;
            // the piece that fell, on its own
            var piece = new PartGrid { X0 = g.X0, Y0 = g.Y0, Z0 = g.Z0, SX = g.SX, SY = g.SY, SZ = g.SZ, M = new byte[g.M.Length], T = new byte[g.M.Length] };
            for (int i = 0; i < g.M.Length; i++)
            {
                if (g.M[i] == 0 || kept[i]) continue;
                piece.M[i] = g.M[i]; piece.T[i] = g.T[i]; piece.Count++;
                g.M[i] = 0; g.T[i] = 0; g.Count--;
            }
            var rem = new Remnant { Grid = piece, From = (BodyPart)p, X = u.X, Y = u.Y, Z = u.Z, Facing = u.Facing, Scale = ScaleOf(u), At = Time, Of = u };
            Remnants.Add(rem);
            Severed?.Invoke(rem);
            body.Bleed = Math.Min(12, body.Bleed + (piece.Count > 20 ? 1.2f : .3f));
            if (p == 0)
            {
                if (head > 0 && offHead * 4 >= head * 3) { vital = true; Note(body, "отрублена голова"); }
                else if (off * 4 >= g.Count + off) { vital = true; Note(body, "разрублен пополам"); }
            }
            // a limb is said to be off only when a good part of it is: a sliver or the gear on it cut away is no limb
            // lost, and the hand or the foot alone is told as that
            else if (Flesh(piece) * 4 >= Flesh(plan.Grid[p])) Note(body, p switch
            {
                (int)BodyPart.Main => plan.Beast ? "отрублена передняя нога" : "отрублена правая рука",
                (int)BodyPart.Off => plan.Beast ? "отрублена передняя нога" : "отрублена левая рука",
                // a horse's legs are a pair to a part: forelegs and hind legs
                (int)BodyPart.LegR when plan.Mounted => "коню перерублена передняя нога",
                _ when plan.Mounted => "коню перерублена задняя нога",
                _ => "отрублена нога",
            });
            else if (!plan.Beast && !plan.Mounted && !Holds(body, (BodyPart)p)) Note(body, p switch
            {
                (int)BodyPart.Main => "отрублена правая кисть",
                (int)BodyPart.Off => "отрублена левая кисть",
                _ => "отрублена стопа",
            });
        }
        // what still works: the hand that holds (its lower quarter), the feet (their lower rows)
        body.MainHand = Holds(body, BodyPart.Main);
        body.OffHand = Holds(body, BodyPart.Off);
        body.Shield = plan.Shield && body.OffHand && Share(body, BodyPart.Off, Tissue.Armor) > .4f;
        body.Legs = (Holds(body, BodyPart.LegR) ? 1 : 0) + (Holds(body, BodyPart.LegL) ? 1 : 0);
        if (plan.Beast) body.Legs = Math.Min(body.Legs, body.MainHand && body.OffHand ? 2 : body.MainHand || body.OffHand ? 1 : 0);
        return vital;
    }

    /// <summary>The cells of a part that are the body itself, not what it wears or carries.</summary>
    static int Flesh(PartGrid g)
    {
        if (g == null) return 0;
        int n = 0;
        for (int i = 0; i < g.M.Length; i++) if (g.M[i] != 0 && g.T[i] != (byte)Tissue.Gear && g.T[i] != (byte)Tissue.Armor) n++;
        return n;
    }

    /// <summary>The lower end of a limb (the hand, the foot) is mostly still there.</summary>
    static bool Holds(BodyState body, BodyPart p)
    {
        var g = body.Grid[(int)p];
        var g0 = body.Plan.Grid[(int)p];
        if (g0 == null) return true;
        if (g == null) return false;
        int lowest = int.MaxValue, highest = int.MinValue;
        for (int i = 0; i < g0.M.Length; i++)
        {
            if (g0.M[i] == 0 || g0.T[i] == (byte)Tissue.Gear) continue;
            int y = i / (g0.SX * g0.SZ) + g0.Y0;
            lowest = Math.Min(lowest, y); highest = Math.Max(highest, y);
        }
        if (lowest == int.MaxValue) return true;
        int end = lowest + Math.Max(2, (highest - lowest + 1) / 4), had = 0, has = 0;
        for (int i = 0; i < g0.M.Length; i++)
        {
            if (g0.M[i] == 0 || g0.T[i] == (byte)Tissue.Gear) continue;
            int y = i / (g0.SX * g0.SZ) + g0.Y0;
            if (y >= end) continue;
            had++;
            if (g.M[i] != 0) has++;
        }
        return had == 0 || has * 10 >= had * 4;
    }

    static float Share(BodyState body, BodyPart p, Tissue t)
    {
        var g = body.Grid[(int)p]; var g0 = body.Plan.Grid[(int)p];
        if (g0 == null) return 0;
        int had = 0, has = 0;
        for (int i = 0; i < g0.M.Length; i++)
        {
            if (g0.T[i] != (byte)t) continue;
            had++;
            if (g.T[i] == (byte)t) has++;
        }
        return had == 0 ? 0 : has / (float)had;
    }

    /// <summary>
    /// Open wounds bleed; the bleeding slows as the blood clots. Our people out of the fight mend slowly, three times as
    /// fast resting by the citadel; what was cut off stays off. Pieces cut off lie a minute and a half, then are gone.
    /// </summary>
    void TickBleeding(float dt)
    {
        foreach (var u in Units)
        {
            if (u.Dead) continue;
            if (u.Wounds is { Bleed: > 0 } w)
            {
                u.Hp -= w.Bleed * dt;
                w.Bleed = Math.Max(0, w.Bleed * (1 - .03f * dt) - .01f * dt);
                if (u.Hp <= 0) Hurt(u, null, 1, "потеря крови");
                if (w.Bleed > .05f) continue;
            }
            if (u.Team != Team.Player || u.Hp >= u.MaxHp || Time - u.LastHurt < 8 || u.State is UnitState.Fight or UnitState.Flee) continue;
            float dx = u.X - Site.KeepX, dz = u.Z - Site.KeepZ;
            bool home = dx * dx + dz * dz < MendBy * MendBy;
            u.Hp = Math.Min(u.MaxHp, u.Hp + u.MaxHp * (home || u.Inside ? .0035f * MendHome : .0012f) * dt);
        }
        if (Remnants.Count > 0) Remnants.RemoveAll(r => Time - r.At > 90);
    }

    /// <summary>How near the citadel, in voxels, the hurt mend at their best.</summary>
    const float MendBy = 40 * VoxelWorld.PerMetre;
}
