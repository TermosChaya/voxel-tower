using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Lava as it is seen, besides its own voxels in the world's mesh (their crust and glowing cracks are the voxel
/// shader's): an orange light over the biggest pools of it, that lights the night about them; a thin dark smoke off
/// it; and where it meets water, a burst of white steam.
/// </summary>
public partial class LavaView : Node3D
{
    struct Puff { public Vector3 P, V; public float Age, Life, Size, Seed, Grey; }

    Main _m;
    MultiMeshInstance3D _smoke;
    float[] _buf = Array.Empty<float>();
    readonly List<Puff> _puffs = new();
    readonly OmniLight3D[] _lights = new OmniLight3D[4];
    readonly Dictionary<int, (float x, float y, float z, int n)> _pools = new();
    readonly List<(int key, int n)> _biggest = new();
    readonly Random _rnd = new(23);
    float _smokeDue;
    double _t;

    const int MaxPuffs = 400, Stride = 20;

    public void Init(Main m)
    {
        _m = m;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = new QuadMesh { Size = Vector2.One } };
        _smoke = new MultiMeshInstance3D
        {
            Multimesh = mm, MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/smoke.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, CustomAabb = new Aabb(new Vector3(-100, -10, -100), new Vector3(200, 60, 200)),
        };
        AddChild(_smoke);
        for (int i = 0; i < _lights.Length; i++)
        {
            _lights[i] = new OmniLight3D { LightColor = new Color(1f, .45f, .15f), ShadowEnabled = false, Visible = false, OmniAttenuation = 1.6f, LightSpecular = .2f };
            AddChild(_lights[i]);
        }
        // lava meets water: a burst of steam, white and thick, rising fast
        m.Sim.Quenched += (x, y, z) =>
        {
            for (int i = 0; i < 3; i++) Add(x + R(-.5f, 1.5f), y + R(.5f, 1.5f), z + R(-.5f, 1.5f), R(.25f, .4f), 1, R(.5f, .9f));
        };
    }

    float R(float a, float b) => a + (float)_rnd.NextDouble() * (b - a);

    /// <summary>The steam and smoke in the air laid at once (a staged scene, before it is filmed).</summary>
    public void Settle() => _puffs.Clear();

    void Add(float x, float y, float z, float size, float grey, float rise)
    {
        if (_puffs.Count >= MaxPuffs) return;
        var w = _m.World;
        _puffs.Add(new Puff
        {
            P = new Vector3(w.WX(x), w.WY(y), w.WZ(z)), V = new Vector3(R(-.08f, .08f), rise, R(-.08f, .08f)),
            Life = R(2f, 3.5f), Size = size, Seed = (float)_rnd.NextDouble(), Grey = grey,
        });
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("LavaView");
        if (_m?.Sim == null) return;
        float dt = _m.Paused ? 0 : (float)Math.Min(delta, .1);
        _t += dt;
        var sim = _m.Sim; var w = _m.World;
        _pools.Clear();
        // a thin dark smoke off the lava, from voxels of it picked at random; its pools for the lights
        _smokeDue += Math.Min(10, sim.Lavas.Count * .05f) * dt;
        if (sim.Lavas.Count > 0)
        {
            int want = (int)_smokeDue, skip = _rnd.Next(Math.Max(1, Math.Min(sim.Lavas.Count, 200))), i = 0;
            _smokeDue -= want;
            foreach (int p in sim.Lavas)
            {
                var (x, y, z) = WaterPlanner.Unpack(p);
                int key = (x >> 4) | (z >> 4) << 10 | (y >> 4) << 20;
                var c = _pools.GetValueOrDefault(key);
                _pools[key] = (c.x + x, c.y + y, c.z + z, c.n + 1);
                if (want > 0 && i++ >= skip && w.Get(x, y + 1, z) == Mat.Air) { Add(x + .5f, y + 1.2f, z + .5f, .18f, .25f, R(.25f, .4f)); want--; skip = i + _rnd.Next(30); }
            }
        }
        _smokeDue = Math.Min(_smokeDue, 4);
        var mm = _smoke.Multimesh;
        if (mm.InstanceCount < _puffs.Count) { mm.InstanceCount = Math.Max(64, _puffs.Count * 2); _buf = new float[mm.InstanceCount * Stride]; }
        int n = 0;
        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            var q = _puffs[i];
            q.Age += dt;
            if (q.Age >= q.Life) { _puffs.RemoveAt(i); continue; }
            q.P += q.V * dt;
            q.V.Y *= MathF.Exp(-dt * .6f);
            _puffs[i] = q;
            float f = q.Age / q.Life, size = q.Size * (1 + f * 2.5f), a = Math.Min(1, q.Age * 3) * (1 - f) * (.18f + .2f * q.Grey);
            float g = .2f + .7f * q.Grey;
            int o = n++ * Stride;
            var b = _buf;
            b[o] = size; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = q.P.X;
            b[o + 4] = 0; b[o + 5] = size; b[o + 6] = 0; b[o + 7] = q.P.Y;
            b[o + 8] = 0; b[o + 9] = 0; b[o + 10] = size; b[o + 11] = q.P.Z;
            b[o + 12] = g; b[o + 13] = g; b[o + 14] = g; b[o + 15] = a;
            b[o + 16] = q.Seed; b[o + 17] = 0; b[o + 18] = 0; b[o + 19] = 0;
        }
        if (n > 0) RenderingServer.MultimeshSetBuffer(mm.GetRid(), _buf);
        mm.VisibleInstanceCount = n;
        Light(w);
    }

    /// <summary>A warm light over each of the biggest pools of lava, as strong and as far as the pool is big, flickering a little.</summary>
    void Light(VoxelWorld w)
    {
        _biggest.Clear();
        foreach (var (key, c) in _pools) if (c.n >= 4) _biggest.Add((key, c.n));
        _biggest.Sort((a, b) => b.n.CompareTo(a.n));
        for (int i = 0; i < _lights.Length; i++)
        {
            var l = _lights[i];
            if (i >= _biggest.Count) { l.Visible = false; continue; }
            var c = _pools[_biggest[i].key];
            l.Visible = true;
            l.Position = new Vector3(w.WX(c.x / c.n), w.WY(c.y / c.n + 5), w.WZ(c.z / c.n));
            float waver = 1 + .1f * MathF.Sin((float)_t * 2.3f + i * 1.9f) + .05f * MathF.Sin((float)_t * 7.1f + i);
            l.LightEnergy = Math.Clamp(.5f + MathF.Sqrt(c.n) * .12f, .5f, 2.2f) * waver * (_m.Atmos?.LampBoost ?? 1);
            l.OmniRange = Math.Clamp(3f + MathF.Sqrt(c.n) * .35f, 3, 10);
        }
    }
}
