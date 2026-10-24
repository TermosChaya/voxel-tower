using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The mark over a mine of the horde that ours have heard: a pale ring on the ground over where they make the face out to
/// be, faint while they dig and starting out wide and bright with every stroke heard, a little earth jumping up in it.
/// Where it stands is the sim's (<see cref="Tunnel.MarkX"/>): the nearer the listener, the truer.
/// </summary>
public partial class TunnelView : Node3D
{
    Main _m;
    const int Most = 6;
    readonly MeshInstance3D[] _rings = new MeshInstance3D[Most];
    readonly StandardMaterial3D[] _mats = new StandardMaterial3D[Most];
    readonly Dictionary<int, float> _pulse = new();
    readonly List<int> _fading = new();
    /// <summary>How many marks are shown now (for the self-test).</summary>
    public int Shown { get; private set; }

    public void Init(Main m)
    {
        _m = m;
        var torus = new TorusMesh { InnerRadius = .86f, OuterRadius = 1f, Rings = 40, RingSegments = 4 };
        for (int i = 0; i < Most; i++)
        {
            _mats[i] = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(.86f, .72f, .46f, .3f), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            _rings[i] = new MeshInstance3D { Mesh = torus, MaterialOverride = _mats[i], CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            AddChild(_rings[i]);
        }
        m.Sim.PicksHeard += (t, x, y, z) =>
        {
            _pulse[t.Id] = 1;
            m.Debris.Spawn(Mat.Soil, (int)x, (int)y, (int)z, 2, .45f, .35f);
        };
    }

    public override void _Process(double delta)
    {
        if (_m?.Sim == null) return;
        float dt = (float)Math.Min(delta, .1);
        var w = _m.World;
        int n = 0;
        foreach (var t in _m.Sim.Tunnels)
        {
            if (n >= Most) break;
            if (t.Team != Team.Horde || !t.Heard || t.State is not (TunnelState.Digging or TunnelState.Chamber) || t.Flooded) continue;
            float pulse = _pulse.GetValueOrDefault(t.Id);
            int gx = (int)t.MarkX, gz = (int)t.MarkZ;
            float gy = w.InBounds(gx, 1, gz) ? w.SurfAt(gx, gz) : 0;
            var ring = _rings[n];
            float r = (1.2f + 1.3f * pulse) * VoxelWorld.PerMetre * VoxelWorld.Vox;
            ring.Position = new Vector3(w.WX(t.MarkX), w.WY(gy + .3f), w.WZ(t.MarkZ));
            ring.Scale = new Vector3(r, .02f, r);
            _mats[n].AlbedoColor = new Color(.86f, .72f, .46f, .22f + .55f * pulse);
            ring.Visible = true;
            n++;
        }
        for (int i = n; i < Most; i++) _rings[i].Visible = false;
        Shown = n;
        // each stroke's start-out dies away in half a second
        _fading.Clear();
        foreach (var (id, p) in _pulse) if (p > 0) _fading.Add(id);
        foreach (int id in _fading) _pulse[id] = Math.Max(0, _pulse[id] - dt * 2);
    }
}
