using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The parts of a figure that move on their own: the body with the head, the arms with what they hold, the legs.
/// A beast's front legs take the arms' places.
/// </summary>
public enum Part { Body, Main, Off, LegR, LegL }

/// <summary>
/// What a figure is drawn in: the parts the blows cut (<see cref="Part"/>) cut again at the joints a man moves at — the
/// trunk and the head at the neck, each arm at the elbow, each leg at the knee. A beast is cut so too, its forelegs
/// taking the arms' bones: each leg at its knee (a hind leg at the hock), and the neck with the head off the trunk over
/// the shoulders. A horse with its rider and an engine keep their parts whole (Torso, UpperR, UpperL, ThighR, ThighL),
/// the rest of their bones empty.
/// </summary>
public enum Bone { Torso, Head, UpperR, LowerR, UpperL, LowerL, ThighR, ShinR, ThighL, ShinL }

/// <summary>
/// A pawn, raider or beast as it is drawn: the meshes of its five parts and of what it wears and carries, cut from its
/// body (<see cref="BodyPlan"/>) at three distances — near in cells of 3 cm with its dress finer still, at the middle
/// distance in the half-voxels the blows cut, far in whole voxels — where the parts turn, and how it moves.
/// </summary>
public sealed class Figure
{
    public const int Parts = 5, Bones = 10;
    /// <summary>The part each bone is cut from.</summary>
    public static readonly Part[] PartOf = { Part.Body, Part.Body, Part.Main, Part.Main, Part.Off, Part.Off, Part.LegR, Part.LegR, Part.LegL, Part.LegL };
    /// <summary>Knees, elbows and a neck: a man, a goblin, an orc, a troll (not a beast, a rider or an engine).</summary>
    public bool Jointed;
    /// <summary>Four legs with knees and a neck that turns: a beast (not a rider's horse).</summary>
    public bool Legged;
    /// <summary>The half-voxel rows of its part each bone is drawn from: [Lo, Hi).</summary>
    public readonly int[] Lo = new int[Bones], Hi = new int[Bones];
    /// <summary>
    /// A beast's neck cut: a bone with +1 is drawn of what lies before half-voxel z <see cref="FrontZ"/> and over y
    /// <see cref="FrontY"/> (the neck and the head), one with −1 of the rest (the trunk), one with 0 of all its rows.
    /// </summary>
    public readonly int[] FrontZ = new int[Bones], FrontY = new int[Bones], Front = new int[Bones];
    /// <summary>A beast's legs (front right, front left, hind right, hind left): from the shoulder or the hip to the knee or the hock, and from there to the sole, in figure voxels.</summary>
    public readonly float[] LegUpper = new float[4], LegLower = new float[4];
    /// <summary>Where each bone turns, in figure voxels: the hips, the neck, the shoulders, the elbows, the hips again, the knees.</summary>
    public readonly Vector3[] Pivot = new Vector3[Bones];
    /// <summary>Each bone's meshes near, at the middle distance and far (null where it has none).</summary>
    public readonly ArrayMesh[] BoneNear = new ArrayMesh[Bones], BoneMid = new ArrayMesh[Bones], BoneFar = new ArrayMesh[Bones];
    /// <summary>The bone each piece of dress goes with: a weapon and a shield the forearm, a helmet the head, a quiver the trunk.</summary>
    public Bone[] DressBone = Array.Empty<Bone>();
    /// <summary>How long the bones are, in figure voxels: hip to knee, knee to sole, shoulder to elbow, elbow to fist; the neck's height over the hips.</summary>
    public float Thigh, Shin, UpperArm, Forearm, NeckUp;
    /// <summary>Near: the body in cells of 3 cm, without its dress.</summary>
    public readonly ArrayMesh[] Mesh = new ArrayMesh[Parts];
    /// <summary>At the middle distance, and for the shadows near: the body in half-voxels, without its dress.</summary>
    public readonly ArrayMesh[] Mid = new ArrayMesh[Parts];
    /// <summary>Far, when a half-voxel is less than a pixel: the parts in whole voxels, what they hold and all.</summary>
    public readonly ArrayMesh[] Far = new ArrayMesh[Parts];
    /// <summary>Each piece of its dress, near in its own fine cells and at the middle distance in half-voxels, and the part it hangs on.</summary>
    public ArrayMesh[] DressNear = Array.Empty<ArrayMesh>(), DressMid = Array.Empty<ArrayMesh>();
    public Part[] DressOn = Array.Empty<Part>();
    /// <summary>Where each part turns: the body at the hips, the arms at the shoulders, the legs at the hips.</summary>
    public readonly Vector3[] Joint = new Vector3[Parts];
    /// <summary>Voxels walked in one full stride, both legs.</summary>
    public float Stride = 9;
    /// <summary>Seconds per blow when digging, building or hacking.</summary>
    public float Chop = .75f;
    /// <summary>How far forward the main hand is carried at rest (radians, negative is forward).</summary>
    public float Carry;
    /// <summary>A shield on the off arm, raised when fighting.</summary>
    public bool Shield;
    /// <summary>Four legs: the arms are front legs.</summary>
    public bool Beast;
    /// <summary>A rider on his horse: the arms are the rider's, the legs the horse's forelegs and hind legs.</summary>
    public bool Mounted;
    /// <summary>How far the off arm comes up in a fight (radians, negative is up).</summary>
    public float Guard = -1.05f;
    public BodyPlan Plan;

    static readonly Dictionary<BodyPlan, Figure> Cache = new();
    static readonly GreedyMesher Mesher = new();

    public static Figure For(UnitType t, int look) => For(Bodies.Plan(t, look));

    public static Figure For(BodyPlan plan)
    {
        if (Cache.TryGetValue(plan, out var f)) return f;
        f = new Figure { Plan = plan, Stride = plan.Stride, Chop = plan.Chop, Carry = plan.Carry, Shield = plan.Shield, Beast = plan.Beast, Mounted = plan.Mounted, Guard = plan.Guard };
        for (int p = 0; p < Parts; p++)
        {
            f.Joint[p] = new Vector3(plan.JX[p], plan.JY[p], plan.JZ[p]);
            f.Mesh[p] = GodotMesh.From(NearOf(plan, p, plan.Grid[p], out _));
            f.Mid[p] = GodotMesh.From(MidOf(plan, p, plan.Grid[p], 0, out _));
            f.Far[p] = CoarseOf(plan.Seen(p, plan.Grid[p]));
        }
        int dn = plan.Dress.Count;
        f.DressNear = new ArrayMesh[dn]; f.DressMid = new ArrayMesh[dn]; f.DressOn = new Part[dn];
        for (int d = 0; d < dn; d++)
        {
            var dress = plan.Dress[d];
            f.DressOn[d] = (Part)dress.On;
            f.DressNear[d] = GodotMesh.From(DressOf(plan, d, plan.Grid[(int)dress.On], out _));
            f.DressMid[d] = GodotMesh.From(MidOf(plan, (int)dress.On, plan.Grid[(int)dress.On], d + 1, out _));
        }
        Bones_(f, plan);
        Cache[plan] = f;
        return f;
    }

    /// <summary>The bones of a figure: where its parts are cut and turn, and the meshes of each piece.</summary>
    static void Bones_(Figure f, BodyPlan plan)
    {
        f.Jointed = !plan.Beast && !plan.Mounted && !plan.Machine && plan.HeadY1 > plan.HeadY0 && plan.Grid[(int)Part.LegR] is { Count: > 0 } && plan.Grid[(int)Part.Main] is { Count: > 0 };
        f.Legged = plan.Beast && !plan.Mounted && plan.NeckZ != int.MinValue && plan.Grid[(int)Part.LegR] is { Count: > 0 } && plan.Grid[(int)Part.Main] is { Count: > 0 };
        const int S = BodyPlan.Sub;
        // a part whole: every row of it
        for (int b = 0; b < Bones; b++) { f.Lo[b] = int.MinValue; f.Hi[b] = int.MaxValue; f.Pivot[b] = f.Joint[(int)PartOf[b]]; }
        // the rows of a part's own body (not what it holds), lowest and highest
        (int lo, int hi) Rows(Part p)
        {
            var g = plan.Grid[(int)p]; var own = plan.Owner[(int)p];
            int lo = int.MaxValue, hi = int.MinValue;
            for (int y = 0; y < g.SY; y++)
                for (int z = 0; z < g.SZ; z++)
                    for (int x = 0; x < g.SX; x++)
                    {
                        int i = (y * g.SZ + z) * g.SX + x;
                        if (g.M[i] == 0 || own != null && own[i] != 0) continue;
                        lo = Math.Min(lo, g.Y0 + y); hi = Math.Max(hi, g.Y0 + y);
                    }
            return (lo, hi);
        }
        void Cut(Bone upper, Bone lower, int row, Vector3 joint)
        {
            f.Lo[(int)upper] = row; f.Hi[(int)lower] = row;
            f.Pivot[(int)lower] = new Vector3(joint.X, row / (float)S, joint.Z);
        }
        if (f.Legged)
        {
            // each leg at its knee: a foreleg's a little under half way up it, a hind leg's hock lower
            var legs = new[] { (Part.Main, Bone.UpperR, Bone.LowerR, .46f), (Part.Off, Bone.UpperL, Bone.LowerL, .46f), (Part.LegR, Bone.ThighR, Bone.ShinR, .38f), (Part.LegL, Bone.ThighL, Bone.ShinL, .38f) };
            for (int l = 0; l < 4; l++)
            {
                var (part, upper, lower, share) = legs[l];
                var (lo, hi) = Rows(part);
                int knee = lo + Math.Max(1, (int)MathF.Round((hi - lo + 1) * share));
                Cut(upper, lower, knee, f.Joint[(int)part]);
                f.LegUpper[l] = f.Joint[(int)part].Y - knee / (float)S;
                f.LegLower[l] = (knee - lo) / (float)S;
            }
            // the neck with the head off the trunk, turning at the neck's root over the shoulders
            foreach (var (bone, side) in new[] { (Bone.Head, 1), (Bone.Torso, -1) })
            {
                f.FrontZ[(int)bone] = plan.NeckZ; f.FrontY[(int)bone] = plan.NeckY; f.Front[(int)bone] = side;
            }
            f.Pivot[(int)Bone.Head] = new Vector3(0, plan.NeckY / (float)S, plan.NeckZ / (float)S);
        }
        if (f.Jointed)
        {
            var (legLo, legHi) = Rows(Part.LegR);
            var (armLo, armHi) = Rows(Part.Main);
            int knee = legLo + (legHi - legLo + 1) / 2, elbow = armLo + (armHi - armLo + 1) * 11 / 20, neck = plan.HeadY0;
            Cut(Bone.UpperR, Bone.LowerR, elbow, f.Joint[(int)Part.Main]);
            Cut(Bone.UpperL, Bone.LowerL, elbow, f.Joint[(int)Part.Off]);
            Cut(Bone.ThighR, Bone.ShinR, knee, f.Joint[(int)Part.LegR]);
            Cut(Bone.ThighL, Bone.ShinL, knee, f.Joint[(int)Part.LegL]);
            // the trunk below the neck, the head above it; the head turns where the neck meets the trunk, at its middle
            f.Hi[(int)Bone.Torso] = neck; f.Lo[(int)Bone.Head] = neck;
            var body = plan.Grid[(int)Part.Body];
            float nx = 0, nz = 0; int n = 0;
            for (int z = 0; z < body.SZ; z++)
                for (int x = 0; x < body.SX; x++)
                    for (int y = neck; y < neck + 2; y++)
                        if (body.In(body.X0 + x, y, body.Z0 + z) && body.M[body.Index(body.X0 + x, y, body.Z0 + z)] != 0) { nx += body.X0 + x + .5f; nz += body.Z0 + z + .5f; n++; }
            f.Pivot[(int)Bone.Head] = n > 0 ? new Vector3(nx / n / S, neck / (float)S, nz / n / S) : new Vector3(0, neck / (float)S, 0);
            f.Thigh = f.Joint[(int)Part.LegR].Y - knee / (float)S;
            f.Shin = (knee - legLo) / (float)S;
            f.UpperArm = f.Joint[(int)Part.Main].Y - elbow / (float)S;
            f.Forearm = (elbow - armLo) / (float)S;
            f.NeckUp = neck / (float)S - f.Joint[(int)Part.Body].Y;
        }
        // the dress: with the forearm (or the arm), the head or the trunk, the shin or the thigh, by where most of it is
        int dn = plan.Dress.Count;
        f.DressBone = new Bone[dn];
        for (int d = 0; d < dn; d++)
        {
            var on = (Part)plan.Dress[d].On;
            Bone upper = BonesOf(on).upper, lower = BonesOf(on).lower;
            if (!f.Jointed || upper == lower) { f.DressBone[d] = upper; continue; }
            if (on is Part.Main or Part.Off) { f.DressBone[d] = lower; continue; }
            var g = plan.Grid[(int)on]; var own = plan.Owner[(int)on];
            long sum = 0; int cnt = 0;
            for (int i = 0; i < g.M.Length; i++) if (g.M[i] != 0 && own != null && own[i] == d + 1) { sum += g.Y0 + i / (g.SX * g.SZ); cnt++; }
            f.DressBone[d] = cnt > 0 ? f.BoneAt(on, sum / (float)cnt / S) : upper;
        }
        for (int b = 0; b < Bones; b++)
        {
            var bone = (Bone)b;
            int p = (int)PartOf[b];
            if (!f.Jointed && !f.Legged && bone is Bone.Head or Bone.LowerR or Bone.LowerL or Bone.ShinR or Bone.ShinL) continue;
            f.BoneNear[b] = GodotMesh.From(f.NearOf(plan, b, plan.Grid[p]));
            f.BoneMid[b] = GodotMesh.From(f.MidOf(plan, b, plan.Grid[p]));
            f.BoneFar[b] = CoarseOf(f.Keep(plan, bone, plan.Seen(p, plan.Grid[p])));
        }
    }

    /// <summary>The bones a part is cut into: its upper and its lower (one and the same for the trunk's head-less kinds).</summary>
    public static (Bone upper, Bone lower) BonesOf(Part p) => p switch
    {
        Part.Body => (Bone.Torso, Bone.Head), Part.Main => (Bone.UpperR, Bone.LowerR), Part.Off => (Bone.UpperL, Bone.LowerL),
        Part.LegR => (Bone.ThighR, Bone.ShinR), _ => (Bone.ThighL, Bone.ShinL),
    };

    /// <summary>The bone of a part a point of it (in figure voxels: its height, and how far forward) is drawn with.</summary>
    public Bone BoneAt(Part p, float y, float z = 0)
    {
        var (upper, lower) = BonesOf(p);
        if (!Jointed && !Legged) return upper;
        // a beast's neck and head are before the shoulders and over the back; a man's head over the neck's cut
        if (p == Part.Body) return Legged ? (z * BodyPlan.Sub >= FrontZ[(int)Bone.Head] && y * BodyPlan.Sub >= FrontY[(int)Bone.Head] ? Bone.Head : Bone.Torso)
            : y * BodyPlan.Sub >= Lo[(int)Bone.Head] ? Bone.Head : Bone.Torso;
        return y * BodyPlan.Sub >= Lo[(int)upper] ? upper : lower;
    }

    /// <summary>Whether a half-voxel row and depth of a part is drawn with this bone: in its rows, and on its side of a beast's neck cut.</summary>
    public bool Holds(int bone, int y, int z) => y >= Lo[bone] && y < Hi[bone] && (Front[bone] == 0 || (z >= FrontZ[bone] && y >= FrontY[bone]) == Front[bone] > 0);

    /// <summary>One bone near, in cells of 3 cm, as much of it as <paramref name="now"/> holds.</summary>
    public MeshBuffers NearOf(BodyPlan plan, int bone, PartGrid now) => Front[bone] == 0
        ? NearOf(plan, (int)PartOf[bone], now, out _, Lo[bone], Hi[bone])
        : Masked(plan.Fine[(int)PartOf[bone]], BodyPlan.K, now, plan.Grid[(int)PartOf[bone]], BodyPlan.FineSub, out _, 0, Lo[bone], Hi[bone], this, bone);

    /// <summary>One bone at the middle distance, in half-voxels.</summary>
    public MeshBuffers MidOf(BodyPlan plan, int bone, PartGrid now) => MidOf(plan, (int)PartOf[bone], now, 0, out _, Lo[bone], Hi[bone], this, bone);

    /// <summary>
    /// A part's half-voxels in whole voxels (or as wounds left them) cut to one bone: its own rows of the body, and the
    /// dress that goes with it whole wherever it reaches.
    /// </summary>
    public PartGrid Keep(BodyPlan plan, Bone bone, PartGrid g)
    {
        if (g == null || g.Count == 0) return null;
        int p = (int)PartOf[(int)bone];
        var own = plan.Owner[p];
        int lo = Lo[(int)bone], hi = Hi[(int)bone];
        if (lo == int.MinValue && hi == int.MaxValue && Front[(int)bone] == 0) return g;
        var k = g.Clone();
        int count = 0;
        for (int i = 0; i < k.M.Length; i++)
        {
            if (k.M[i] == 0) continue;
            int row = k.Y0 + i / (k.SX * k.SZ), depth = k.Z0 + i / k.SX % k.SZ;
            int d = own != null && own.Length == k.M.Length ? own[i] : 0;
            bool keep = d == 0 ? Holds((int)bone, row, depth) : DressBone.Length >= d && DressBone[d - 1] == bone;
            if (keep) count++; else { k.M[i] = 0; k.T[i] = 0; }
        }
        k.Count = count;
        return count == 0 ? null : k;
    }

    // ---------------------------------------------------------------- materials

    static readonly Dictionary<int, ShaderMaterial> Mats = new();

    /// <summary>The figures' material for cells so many to a voxel: the grain and the bevel of each face follow them.</summary>
    public static ShaderMaterial Material(int grid)
    {
        if (Mats.TryGetValue(grid, out var m)) return m;
        m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
        m.SetShaderParameter("grid", (float)grid);
        Mats[grid] = m;
        return m;
    }

    // ---------------------------------------------------------------- meshes, whole or hurt

    /// <summary>A part's half-voxels as a mesh in figure voxels, all of them (a piece cut off, at the middle distance).</summary>
    public static ArrayMesh MeshOf(PartGrid g)
    {
        if (g == null || g.Count == 0) return null;
        var b = Mesher.Mesh(g.Padded(), g.SX, g.SY, g.SZ, g.X0, g.Y0, g.Z0);
        if (b == null || b.Quads == 0) return null;
        for (int i = 0; i < b.Pos.Length; i++) b.Pos[i] /= BodyPlan.Sub;
        return GodotMesh.From(b);
    }

    /// <summary>
    /// A part's body near, in cells of 3 cm, as much of it as is left of its half-voxels <paramref name="now"/>
    /// (null when nothing is): where a wound has carved, the cells round it show what it opened — flesh, bone — or the
    /// blood on the skin.
    /// </summary>
    public static MeshBuffers NearOf(BodyPlan plan, int part, PartGrid now, out int quads, int lo = int.MinValue, int hi = int.MaxValue)
        => Masked(plan.Fine[part], BodyPlan.K, now, plan.Grid[part], BodyPlan.FineSub, out quads, 0, lo, hi);

    /// <summary>
    /// A piece of dress near, in its own fine cells, as much of it as the half-voxels it fills of its part still hold —
    /// leaving out the cells of <paramref name="without"/> if given (a bow's string, drawn apart before the eyes).
    /// </summary>
    public static MeshBuffers DressOf(BodyPlan plan, int d, PartGrid now, out int quads, byte without = 0)
    {
        var dress = plan.Dress[d];
        return Masked(dress.Grid, dress.Sub / BodyPlan.Sub, now, plan.Grid[(int)dress.On], dress.Sub, out quads, without);
    }

    /// <summary>
    /// A part at the middle distance, in half-voxels: only the body's own (owner 0), or only one piece of its dress (1 +
    /// its index), and only what its cells fill enough of (<see cref="BodyPlan.Solid"/>), each showing what it shows
    /// (<see cref="BodyPlan.Shown"/>) where no wound has changed it.
    /// </summary>
    public static MeshBuffers MidOf(BodyPlan plan, int part, PartGrid now, int owner, out int quads, int lo = int.MinValue, int hi = int.MaxValue, Figure cut = null, int bone = -1)
    {
        quads = 0;
        var own = plan.Owner[part];
        var solid = plan.Solid[part];
        var shown = plan.Shown[part];
        var was = plan.Grid[part];
        if (now == null || now.Count == 0 || own == null || own.Length != now.M.Length) return null;
        int px = now.SX + 2, pz = now.SZ + 2;
        var pad = new byte[px * (now.SY + 2) * pz];
        bool any = false;
        for (int y = 0; y < now.SY; y++)
            for (int z = 0; z < now.SZ; z++)
                for (int x = 0; x < now.SX; x++)
                {
                    int i = (y * now.SZ + z) * now.SX + x;
                    if (now.M[i] == 0 || own[i] != owner || !solid[i] || now.Y0 + y < lo || now.Y0 + y >= hi) continue;
                    if (cut != null && !cut.Holds(bone, now.Y0 + y, now.Z0 + z)) continue;
                    pad[((y + 1) * pz + (z + 1)) * px + (x + 1)] = shown != null && shown[i] != 0 && now.M[i] == was.M[i] ? shown[i] : now.M[i];
                    any = true;
                }
        if (!any) return null;
        var b = Mesher.Mesh(pad, now.SX, now.SY, now.SZ, now.X0, now.Y0, now.Z0);
        if (b == null || b.Quads == 0) return null;
        for (int i = 0; i < b.Pos.Length; i++) b.Pos[i] /= BodyPlan.Sub;
        quads = b.Quads;
        return b;
    }

    /// <summary>
    /// Fine cells (<paramref name="per"/> to a half-voxel each way) drawn where the half-voxel they lie in is still there in
    /// <paramref name="now"/>. Where a wound has changed that half-voxel — blood on the skin — the cells take its colour;
    /// where it has opened one of flesh, bone, brain or heart to the air, they show that.
    /// </summary>
    static MeshBuffers Masked(PartGrid fine, int per, PartGrid now, PartGrid was, int toVoxel, out int quads, byte without = 0, int lo = int.MinValue, int hi = int.MaxValue,
        Figure cut = null, int bone = -1)
    {
        quads = 0;
        if (fine == null || now == null || now.Count == 0 || was == null || now.M.Length != was.M.Length) return null;
        bool hurt = !ReferenceEquals(now, was);
        // what each half-voxel is: 0 not yet looked at, 1 as it was, 2 opened by a wound
        var open = hurt ? new byte[now.M.Length] : null;
        int px = fine.SX + 2, pz = fine.SZ + 2;
        var pad = new byte[px * (fine.SY + 2) * pz];
        bool any = false;
        for (int y = 0; y < fine.SY; y++)
        {
            int cy = Down(fine.Y0 + y, per);
            // only the rows of one bone of the part
            if (cy < lo || cy >= hi) continue;
            for (int z = 0; z < fine.SZ; z++)
            {
                int cz = Down(fine.Z0 + z, per);
                for (int x = 0; x < fine.SX; x++)
                {
                    byte m = fine.M[(y * fine.SZ + z) * fine.SX + x];
                    if (m == 0 || m == without) continue;
                    int cx = Down(fine.X0 + x, per);
                    if (!now.In(cx, cy, cz) || cut != null && !cut.Holds(bone, cy, cz)) continue;
                    int ci = now.Index(cx, cy, cz);
                    byte cm = now.M[ci];
                    if (cm == 0) continue;
                    if (hurt)
                    {
                        if (cm != was.M[ci]) m = cm;
                        else if (now.T[ci] >= (byte)Tissue.Flesh && Opened(now, was, open, cx, cy, cz, ci)) m = cm;
                    }
                    pad[((y + 1) * pz + (z + 1)) * px + (x + 1)] = m;
                    any = true;
                }
            }
        }
        if (!any) return null;
        var b = Mesher.Mesh(pad, fine.SX, fine.SY, fine.SZ, fine.X0, fine.Y0, fine.Z0);
        if (b == null || b.Quads == 0) return null;
        for (int i = 0; i < b.Pos.Length; i++) b.Pos[i] /= toVoxel;
        quads = b.Quads;
        return b;
    }

    /// <summary>Has a wound taken a half-voxel beside this one, open to it now?</summary>
    static bool Opened(PartGrid now, PartGrid was, byte[] open, int x, int y, int z, int i)
    {
        if (open[i] != 0) return open[i] == 2;
        bool o = false;
        foreach (var (dx, dy, dz) in Around)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (!now.In(nx, ny, nz)) continue;
            int n = now.Index(nx, ny, nz);
            if (was.M[n] != 0 && now.M[n] == 0) { o = true; break; }
        }
        open[i] = o ? (byte)2 : (byte)1;
        return o;
    }

    static readonly (int, int, int)[] Around = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

    static int Down(int v, int r) => v >= 0 ? v / r : (v - r + 1) / r;

    /// <summary>
    /// A part in whole voxels: each voxel is filled where two of its eight half-voxels are, so that blades and shafts
    /// a half-voxel thin are kept, with the material most of them are of.
    /// </summary>
    public static ArrayMesh CoarseOf(PartGrid g)
    {
        if (g == null || g.Count == 0) return null;
        const int S = BodyPlan.Sub;
        int x0 = Down(g.X0, S), y0 = Down(g.Y0, S), z0 = Down(g.Z0, S);
        int sx = Down(g.X0 + g.SX - 1, S) - x0 + 1, sy = Down(g.Y0 + g.SY - 1, S) - y0 + 1, sz = Down(g.Z0 + g.SZ - 1, S) - z0 + 1;
        int px = sx + 2, pz = sz + 2;
        var vox = new byte[px * (sy + 2) * pz];
        Span<byte> mats = stackalloc byte[S * S * S];
        int any = 0;
        for (int y = 0; y < sy; y++)
            for (int z = 0; z < sz; z++)
                for (int x = 0; x < sx; x++)
                {
                    int n = 0;
                    for (int dy = 0; dy < S; dy++)
                        for (int dz = 0; dz < S; dz++)
                            for (int dx = 0; dx < S; dx++)
                            {
                                byte m = g.Mat((x0 + x) * S + dx, (y0 + y) * S + dy, (z0 + z) * S + dz);
                                if (m != 0) mats[n++] = m;
                            }
                    if (n < 2) continue;
                    byte best = mats[0];
                    int bestN = 0;
                    for (int i = 0; i < n; i++)
                    {
                        int c = 0;
                        for (int j = 0; j < n; j++) if (mats[j] == mats[i]) c++;
                        if (c > bestN) { bestN = c; best = mats[i]; }
                    }
                    vox[((y + 1) * pz + (z + 1)) * px + (x + 1)] = best;
                    any++;
                }
        if (any == 0) return null;
        var b = Mesher.Mesh(vox, sx, sy, sz, x0, y0, z0);
        if (b == null || b.Quads == 0) return null;
        return GodotMesh.From(b);
    }

    /// <summary>
    /// One part near with what hangs on it, as one mesh of a surface each, each surface with its own material: an arm
    /// before the eyes (first person), a piece cut off lying on the ground. <paramref name="now"/> is what is left of
    /// the part, or the piece that came off it.
    /// </summary>
    public static ArrayMesh Whole(BodyPlan plan, int part, PartGrid now)
    {
        var mesh = new ArrayMesh();
        int surfaces = 0;
        void Add(MeshBuffers b, int grid)
        {
            if (b == null || b.Quads == 0) return;
            GodotMesh.AddTo(mesh, b);
            mesh.SurfaceSetMaterial(surfaces++, Material(grid));
        }
        Add(NearOf(plan, part, now, out _), BodyPlan.FineSub);
        for (int d = 0; d < plan.Dress.Count; d++)
            if ((int)plan.Dress[d].On == part) Add(DressOf(plan, d, now, out _), plan.Dress[d].Sub);
        return surfaces > 0 ? mesh : null;
    }
}
