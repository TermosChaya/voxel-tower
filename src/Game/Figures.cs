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
/// A pawn, raider or beast as it is drawn: the meshes of its five parts, cut from its body (<see cref="BodyPlan"/>,
/// in half-voxels, meshed at the scale of the world's voxels), where they turn, and how it moves.
/// </summary>
public sealed class Figure
{
    public const int Parts = 5;
    public readonly ArrayMesh[] Mesh = new ArrayMesh[Parts];
    /// <summary>The same parts in whole voxels, for when the camera is far and a half-voxel is less than a pixel.</summary>
    public readonly ArrayMesh[] Far = new ArrayMesh[Parts];
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
            f.Mesh[p] = MeshOf(plan.Grid[p]);
            f.Far[p] = CoarseOf(plan.Grid[p]);
        }
        Cache[plan] = f;
        return f;
    }

    /// <summary>A part's voxels as a mesh in figure voxels (its half-voxels at half size).</summary>
    public static ArrayMesh MeshOf(PartGrid g)
    {
        if (g == null || g.Count == 0) return null;
        var b = Mesher.Mesh(g.Padded(), g.SX, g.SY, g.SZ, g.X0, g.Y0, g.Z0);
        if (b == null || b.Quads == 0) return null;
        for (int i = 0; i < b.Pos.Length; i++) b.Pos[i] /= BodyPlan.Sub;
        return GodotMesh.From(b);
    }

    static int Down(int v) => v >= 0 ? v / BodyPlan.Sub : (v - BodyPlan.Sub + 1) / BodyPlan.Sub;

    /// <summary>
    /// A part in whole voxels: each voxel is filled where two of its eight half-voxels are, so that blades and shafts
    /// a half-voxel thin are kept, with the material most of them are of.
    /// </summary>
    public static ArrayMesh CoarseOf(PartGrid g)
    {
        if (g == null || g.Count == 0) return null;
        const int S = BodyPlan.Sub;
        int x0 = Down(g.X0), y0 = Down(g.Y0), z0 = Down(g.Z0);
        int sx = Down(g.X0 + g.SX - 1) - x0 + 1, sy = Down(g.Y0 + g.SY - 1) - y0 + 1, sz = Down(g.Z0 + g.SZ - 1) - z0 + 1;
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
        return b == null || b.Quads == 0 ? null : GodotMesh.From(b);
    }
}
