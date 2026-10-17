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
    public BodyPlan Plan;

    static readonly Dictionary<BodyPlan, Figure> Cache = new();
    static readonly GreedyMesher Mesher = new();

    public static Figure For(UnitType t, int look) => For(Bodies.Plan(t, look));

    public static Figure For(BodyPlan plan)
    {
        if (Cache.TryGetValue(plan, out var f)) return f;
        f = new Figure { Plan = plan, Stride = plan.Stride, Chop = plan.Chop, Carry = plan.Carry, Shield = plan.Shield, Beast = plan.Beast };
        for (int p = 0; p < Parts; p++)
        {
            f.Joint[p] = new Vector3(plan.JX[p], plan.JY[p], plan.JZ[p]);
            f.Mesh[p] = MeshOf(plan.Grid[p]);
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
}
