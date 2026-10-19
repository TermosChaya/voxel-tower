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
/// A pawn, raider or beast as it is drawn: the meshes of its five parts and of what it wears and carries, cut from its
/// body (<see cref="BodyPlan"/>) at three distances — near in cells of 3 cm with its dress finer still, at the middle
/// distance in the half-voxels the blows cut, far in whole voxels — where the parts turn, and how it moves.
/// </summary>
public sealed class Figure
{
    public const int Parts = 5;
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
        Cache[plan] = f;
        return f;
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
    public static MeshBuffers NearOf(BodyPlan plan, int part, PartGrid now, out int quads)
        => Masked(plan.Fine[part], BodyPlan.K, now, plan.Grid[part], BodyPlan.FineSub, out quads);

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
    public static MeshBuffers MidOf(BodyPlan plan, int part, PartGrid now, int owner, out int quads)
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
                    if (now.M[i] == 0 || own[i] != owner || !solid[i]) continue;
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
    static MeshBuffers Masked(PartGrid fine, int per, PartGrid now, PartGrid was, int toVoxel, out int quads, byte without = 0)
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
            for (int z = 0; z < fine.SZ; z++)
            {
                int cz = Down(fine.Z0 + z, per);
                for (int x = 0; x < fine.SX; x++)
                {
                    byte m = fine.M[(y * fine.SZ + z) * fine.SX + x];
                    if (m == 0 || m == without) continue;
                    int cx = Down(fine.X0 + x, per);
                    if (!now.In(cx, cy, cz)) continue;
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
