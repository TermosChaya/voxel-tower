using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The mark over a mine of the horde that ours have heard: a pale ring on the ground over where they make the face out to
/// be, the turf in it darkened as if stirred, and with every stroke heard a ripple running out from it and a little
/// earth jumping up. Where it stands is the sim's (<see cref="Tunnel.MarkX"/>): the nearer the listener, the truer.
/// </summary>
public partial class TunnelView : Node3D
{
    Main _m;
    const int Most = 6;
    readonly MeshInstance3D[] _rings = new MeshInstance3D[Most], _ripples = new MeshInstance3D[Most], _patches = new MeshInstance3D[Most];
    readonly StandardMaterial3D[] _mats = new StandardMaterial3D[Most], _rippleMats = new StandardMaterial3D[Most];
    readonly Dictionary<int, float> _pulse = new();
    readonly List<int> _fading = new();
    /// <summary>How many marks are shown now (for the self-test).</summary>
    public int Shown { get; private set; }

    public void Init(Main m)
    {
        _m = m;
        var torus = new TorusMesh { InnerRadius = .74f, OuterRadius = 1f, Rings = 40, RingSegments = 4 };
        var thin = new TorusMesh { InnerRadius = .9f, OuterRadius = 1f, Rings = 40, RingSegments = 4 };
        var disc = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 24, Rings = 1 };
        // the stirred turf: dark earth seen through it, the darker toward the middle
        var patch = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(.18f, .12f, .07f, .32f),
        };
        StandardMaterial3D Glow(float a) => new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(.92f, .78f, .5f, a), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        for (int i = 0; i < Most; i++)
        {
            _mats[i] = Glow(.3f);
            _rippleMats[i] = Glow(0);
            _rings[i] = new MeshInstance3D { Mesh = torus, MaterialOverride = _mats[i], CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            _ripples[i] = new MeshInstance3D { Mesh = thin, MaterialOverride = _rippleMats[i], CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            _patches[i] = new MeshInstance3D { Mesh = disc, MaterialOverride = patch, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            AddChild(_patches[i]); AddChild(_rings[i]); AddChild(_ripples[i]);
        }
        m.Sim.PicksHeard += (t, x, y, z) =>
        {
            _pulse[t.Id] = 1;
            m.Debris.Spawn(Mat.Soil, (int)x, (int)y, (int)z, 4, .6f, .4f);
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
            float m1 = VoxelWorld.PerMetre * VoxelWorld.Vox, r = (1.5f + .5f * pulse) * m1;
            var at = new Vector3(w.WX(t.MarkX), w.WY(gy + .3f), w.WZ(t.MarkZ));
            ring.Position = at;
            ring.Scale = new Vector3(r, .02f, r);
            _mats[n].AlbedoColor = new Color(.92f, .78f, .5f, .4f + .5f * pulse);
            ring.Visible = true;
            _patches[n].Position = at - new Vector3(0, .005f, 0);
            _patches[n].Scale = new Vector3(1.3f * m1, .004f, 1.3f * m1);
            _patches[n].Visible = true;
            // the stroke heard: a ripple running out to three metres and fading
            float out_ = 1 - pulse, rr = (1.5f + 2f * out_) * m1;
            _ripples[n].Position = at;
            _ripples[n].Scale = new Vector3(rr, .02f, rr);
            _rippleMats[n].AlbedoColor = new Color(.92f, .78f, .5f, .55f * pulse);
            _ripples[n].Visible = pulse > 0;
            n++;
        }
        for (int i = n; i < Most; i++) _rings[i].Visible = _ripples[i].Visible = _patches[i].Visible = false;
        Shown = n;
        // each stroke's start-out dies away in three quarters of a second
        _fading.Clear();
        foreach (var (id, p) in _pulse) if (p > 0) _fading.Add(id);
        foreach (int id in _fading) _pulse[id] = Math.Max(0, _pulse[id] - dt * 1.4f);
    }
}
