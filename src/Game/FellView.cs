using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Trees going over: a tree cut through is taken out of the valley's voxels at once (<see cref="Toppling"/>), and drawn
/// here whole, crown and all, turning on its stump the way it goes — slowly at first and then faster, as a falling
/// trunk does — till it is down; then it is gone from here and the simulation lays the log along the ground, and the
/// crown bursts into leaves and twigs where it struck.
/// </summary>
public partial class FellView : Node3D
{
    sealed class Falling { public Toppling T; public MeshInstance3D Node; public Vector3 Pivot, Axis; public Vector3[] Leaves; public byte LeafMat; }

    Main _m;
    readonly List<Falling> _falling = new();
    readonly GreedyMesher _mesher = new();

    public void Init(Main m)
    {
        _m = m;
        m.Sim.Toppled += Begin;
    }

    void Begin(Toppling t)
    {
        if (t.Voxels.Count == 0) return;
        int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
        foreach (var (x, y, z, _) in t.Voxels) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z); }
        int sx = x1 - x0 + 1, sy = y1 - y0 + 1, sz = z1 - z0 + 1, px = sx + 2, pz = sz + 2;
        var pad = new byte[px * (sy + 2) * pz];
        var leaves = new List<Vector3>();
        byte leafMat = 0;
        foreach (var (x, y, z, m) in t.Voxels)
        {
            pad[((y - y0 + 1) * pz + (z - z0 + 1)) * px + (x - x0 + 1)] = m;
            // a few of the crown's voxels, for where its leaves burst when it comes down
            if (Mat.IsLeafy(m) && (x * 7 + y * 13 + z * 5) % 23 == 0) { leaves.Add(new Vector3(x + .5f, y + .5f, z + .5f)); leafMat = m; }
        }
        var b = _mesher.Mesh(pad, sx, sy, sz, x0, y0, z0);
        if (b == null || b.Quads == 0) return;
        var node = new MeshInstance3D { Mesh = GodotMesh.From(b), MaterialOverride = Figure.Material(1), CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        AddChild(node);
        // the figures' shader takes its tint from the instance (its kind's figures have theirs from the MultiMesh)
        node.SetInstanceShaderParameter("tint", Colors.White);
        var dir = new Vector3(t.DX, 0, t.DZ);
        var f = new Falling { T = t, Node = node, Pivot = new Vector3(t.PX, t.PY, t.PZ), Axis = Vector3.Up.Cross(dir).Normalized(), Leaves = leaves.ToArray(), LeafMat = leafMat };
        _falling.Add(f);
        Place(f, 0);
    }

    /// <summary>The tree turned so far over on its stump (0 standing, π/2 down along the ground).</summary>
    void Place(Falling f, float angle)
    {
        var w = _m.World;
        var turn = new Basis(f.Axis, angle);
        // voxels of the valley to the world, about the stump
        var inVoxels = new Transform3D(turn, f.Pivot - turn * f.Pivot);
        f.Node.Transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * VoxelWorld.Vox), new Vector3(w.OriginX, 0, w.OriginZ)) * inVoxels;
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("FellView");
        if (_falling.Count == 0) return;
        double now = _m.Sim.Time;
        for (int i = _falling.Count - 1; i >= 0; i--)
        {
            var f = _falling[i];
            double span = Math.Max(.1, f.T.To - f.T.From);
            float k = (float)Math.Clamp((now - f.T.From) / span, 0, 1);
            if (k >= 1)
            {
                // down: the leaves and twigs burst where the crown struck
                var turn = new Basis(f.Axis, MathF.PI / 2);
                int n = 0;
                foreach (var v in f.Leaves)
                {
                    if (n++ > 60) break;
                    var at = f.Pivot + turn * (v - f.Pivot);
                    _m.Debris.Spawn(f.LeafMat, (int)at.X, (int)at.Y + 1, (int)at.Z, 1, 1.6f, .7f);
                }
                f.Node.QueueFree();
                _falling.RemoveAt(i);
                continue;
            }
            // over slowly at first, then faster as it goes, as a trunk falls about its foot
            Place(f, MathF.PI / 2 * k * k);
        }
    }
}
