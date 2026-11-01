using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The architect's plan as a ghost on the ground while he is chosen (Sim.Architect.cs): his line round the point it
/// holds, in pale green where it runs along what stands and in blueprint blue where it is to be built; each piece he has
/// yet to mark drawn along its line (or round its place: the towers, the posts, the gates) brighter, each one marked and
/// being built fainter; what is finished is not drawn but for the line. Drawn over everything, made again twice a second.
/// </summary>
public partial class ArchitectView : Node3D
{
    Main _m;
    ImmediateMesh _mesh;
    double _next;
    /// <summary>How many pieces are drawn now (the self-test asks).</summary>
    public int Shown { get; private set; }

    static readonly Color Planned = new(.55f, .78f, 1f, .75f), Building = new(.55f, .78f, 1f, .32f),
        Stands = new(.62f, .9f, .55f, .55f), New = new(.55f, .78f, 1f, .5f), Nature = new(.5f, .7f, .8f, .3f);

    public void Init(Main m)
    {
        _m = m;
        _mesh = new ImmediateMesh();
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled, NoDepthTest = true, RenderPriority = 5,
        };
        AddChild(new MeshInstance3D { Mesh = _mesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    public override void _Process(double delta)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (_m?.Sim == null || now < _next) return;
        _next = now + .5;
        _mesh.ClearSurfaces();
        Shown = 0;
        var sim = _m.Sim;
        if (sim.ArchPlan is not { } p || _m.FP.On) return;
        bool chosen = false;
        foreach (var u in _m.Selected) chosen |= !u.Dead && u.T == Kinds.Architect;
        if (!chosen) return;
        var w = _m.World;
        bool any = false;
        void Begin() { if (!any) { _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles); any = true; } }
        // the line: what stands, what is to be built, water and cliffs that bar the way
        for (int i = 0; i < p.Ring.Count; i++)
        {
            var (ax, az) = p.Ring[i]; var (bx, bz) = p.Ring[(i + 1) % p.Ring.Count];
            byte e = i < p.Edge.Count ? p.Edge[i] : (byte)0;
            Begin();
            Band(w, ax, az, bx, bz, e == 0 ? New : e == 2 ? Nature : Stands, .6f);
        }
        foreach (var piece in sim.ArchPieces(p))
        {
            ulong bit = 1UL << piece.Index;
            if ((p.Done & bit) != 0) continue;
            var c = (p.Marked & bit) != 0 ? Building : Planned;
            Begin();
            Shown++;
            if (piece.Round > 0) { Ring(w, piece.AtX, piece.AtZ, piece.Round, c); continue; }
            if (piece.Line.Length >= 4 && piece.Work is ArchWorkKind.Wall or ArchWorkKind.Unbuild or ArchWorkKind.Stair)
            {
                for (int k = 0; k + 3 < piece.Line.Length; k += 2) Band(w, piece.Line[k], piece.Line[k + 1], piece.Line[k + 2], piece.Line[k + 3], c, 1.4f);
                continue;
            }
            if (piece.Work is ArchWorkKind.Ditch or ArchWorkKind.Stakes)
            {
                // along its quarter of the line, so far out from it
                int q = p.Works[piece.Index].Quarter;
                float off = piece.Work == ArchWorkKind.Ditch ? 34 : 56;
                for (int i = 0; i < p.Ring.Count; i++)
                {
                    var (ax, az) = p.Ring[i]; var (bx, bz) = p.Ring[(i + 1) % p.Ring.Count];
                    if (Quarter(p, ax, az) != q && Quarter(p, bx, bz) != q) continue;
                    var (oax, oaz) = Out(p, ax, az, off); var (obx, obz) = Out(p, bx, bz, off);
                    Band(w, oax, oaz, obx, obz, c, piece.Work == ArchWorkKind.Ditch ? 1.6f : .9f);
                }
                continue;
            }
            // the outline of its box on the ground, a line a few voxels wide
            Band(w, piece.X0, piece.Z0, piece.X1, piece.Z0, c); Band(w, piece.X1, piece.Z0, piece.X1, piece.Z1, c);
            Band(w, piece.X1, piece.Z1, piece.X0, piece.Z1, c); Band(w, piece.X0, piece.Z1, piece.X0, piece.Z0, c);
        }
        if (any) _mesh.SurfaceEnd();
    }

    /// <summary>The quarter about the plan's point a column is in (0 north … 3 west), as the plan splits its ditch.</summary>
    static int Quarter(ArchPlan p, float x, float z)
    {
        float dx = x - p.CX, dz = z - p.CZ;
        return Math.Abs(dz) >= Math.Abs(dx) ? (dz < 0 ? 0 : 2) : (dx > 0 ? 1 : 3);
    }

    /// <summary>A point of the line pushed so far out from the plan's point.</summary>
    static (float x, float z) Out(ArchPlan p, float x, float z, float by)
    {
        float dx = x - p.CX, dz = z - p.CZ, d = MathF.Max(1, MathF.Sqrt(dx * dx + dz * dz));
        return (x + dx / d * by, z + dz / d * by);
    }

    /// <summary>A flat band on the ground from one point to another (voxels), so wide, following the ground every few metres.</summary>
    void Band(VoxelWorld w, float ax, float az, float bx, float bz, Color c, float wide = .9f)
    {
        float len = MathF.Max(1, MathF.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)));
        int n = Math.Max(1, (int)(len / 16));
        float sx = -(bz - az) / len * wide, sz = (bx - ax) / len * wide;
        for (int i = 0; i < n; i++)
        {
            float t0 = (float)i / n, t1 = (float)(i + 1) / n;
            float x0 = ax + (bx - ax) * t0, z0 = az + (bz - az) * t0, x1 = ax + (bx - ax) * t1, z1 = az + (bz - az) * t1;
            float y0 = w.WY(w.SurfAt((int)x0, (int)z0) + .5f), y1 = w.WY(w.SurfAt((int)x1, (int)z1) + .5f);
            var p0 = new Vector3(w.WX(x0 + sx), y0, w.WZ(z0 + sz)); var p1 = new Vector3(w.WX(x0 - sx), y0, w.WZ(z0 - sz));
            var p2 = new Vector3(w.WX(x1 + sx), y1, w.WZ(z1 + sz)); var p3 = new Vector3(w.WX(x1 - sx), y1, w.WZ(z1 - sz));
            Tri(p0, p1, p2, c); Tri(p2, p1, p3, c);
        }
    }

    /// <summary>A ring on the ground about a point (voxels).</summary>
    void Ring(VoxelWorld w, float x, float z, float r, Color c)
    {
        const int N = 24;
        for (int i = 0; i < N; i++)
        {
            float a0 = MathF.Tau * i / N, a1 = MathF.Tau * (i + 1) / N;
            Band(w, x + MathF.Cos(a0) * r, z + MathF.Sin(a0) * r, x + MathF.Cos(a1) * r, z + MathF.Sin(a1) * r, c);
        }
    }

    void Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
    {
        _mesh.SurfaceSetColor(col); _mesh.SurfaceAddVertex(a);
        _mesh.SurfaceSetColor(col); _mesh.SurfaceAddVertex(b);
        _mesh.SurfaceSetColor(col); _mesh.SurfaceAddVertex(c);
    }
}
