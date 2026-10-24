using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Acid as it is seen, besides its own glowing voxels in the world's mesh: pale green fumes rising off it and drifting,
/// the faces of what it is eating darkened and greened the farther it has got into them, a flask breaking in a spray of
/// glass and droplets, a fizz of chips where it eats a voxel through, and a weak green light over the biggest pools,
/// seen at night.
/// </summary>
public partial class AcidView : Node3D
{
    struct Puff { public Vector3 P, V; public float Age, Life, Size, Seed; }

    Main _m;
    MultiMeshInstance3D _fumes, _etch;
    float[] _fumeBuf = Array.Empty<float>(), _etchBuf = Array.Empty<float>();
    readonly List<Puff> _puffs = new();
    readonly OmniLight3D[] _lights = new OmniLight3D[3];
    readonly Dictionary<int, (float x, float y, float z, int n)> _pools = new();
    readonly List<(int key, int n)> _biggest = new();
    readonly Random _rnd = new(11);
    float _fumeDue;
    double _t;

    const int MaxPuffs = 400, MaxEtched = 3000, Stride = 20, EtchStride = 16;

    public void Init(Main m)
    {
        _m = m;
        var quad = new QuadMesh { Size = Vector2.One };
        _fumes = Multi(quad, new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/smoke.gdshader") }, true);
        // the eaten faces: a skin a hair over the voxel, dark and green, the more the farther the acid has got
        var cube = new BoxMesh { Size = Vector3.One * VoxelWorld.Vox * 1.02f };
        var etch = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = .35f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
        };
        _etch = Multi(cube, etch, false);
        for (int i = 0; i < _lights.Length; i++)
        {
            _lights[i] = new OmniLight3D { LightColor = new Color(.55f, 1f, .3f), ShadowEnabled = false, Visible = false, OmniAttenuation = 1.8f, LightSpecular = 0 };
            AddChild(_lights[i]);
        }
        var sim = m.Sim;
        // a flask breaks: glass flying, and droplets of acid
        sim.FlaskBroke += (x, y, z) =>
        {
            int ix = (int)x, iy = (int)y, iz = (int)z;
            m.Debris.Spawn(Mat.Phial, ix, iy, iz, 6, 2.4f, .35f);
            m.Debris.Spawn(Mat.Acid, ix, iy, iz, 8, 1.8f, .4f);
            for (int i = 0; i < 5; i++) Fume(x + R(-6, 6), y + R(0, 3), z + R(-6, 6), .22f);
        };
        // eaten through: a fizz of chips of it, and a puff; lost in water: a puff of steam
        sim.Dissolved += (x, y, z, mat) =>
        {
            if (mat != Mat.Water && _rnd.NextDouble() < .35) m.Debris.Spawn(mat, x, y, z, 1, .9f, .5f);
            if (_rnd.NextDouble() < .4) Fume(x + .5f, y + 1, z + .5f, mat == Mat.Water ? .3f : .18f);
        };
    }

    float R(float a, float b) => a + (float)_rnd.NextDouble() * (b - a);

    /// <summary>The fumes in the air laid at once (a staged scene, before it is filmed).</summary>
    public void Settle() => _puffs.Clear();

    void Fume(float x, float y, float z, float size)
    {
        if (_puffs.Count >= MaxPuffs) return;
        var w = _m.World;
        _puffs.Add(new Puff
        {
            P = new Vector3(w.WX(x), w.WY(y), w.WZ(z)),
            V = new Vector3(R(-.08f, .12f), R(.2f, .38f), R(-.08f, .1f)),
            Life = R(2.5f, 4.5f), Size = size * R(.7f, 1.1f), Seed = (float)_rnd.NextDouble(),
        });
    }

    MultiMeshInstance3D Multi(Mesh mesh, Material mat, bool custom)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = custom, Mesh = mesh };
        var mi = new MultiMeshInstance3D
        {
            Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-100, -10, -100), new Vector3(200, 60, 200)),
        };
        AddChild(mi);
        return mi;
    }

    static void Grow(MultiMeshInstance3D mi, ref float[] buf, int need, int stride)
    {
        var mm = mi.Multimesh;
        if (mm.InstanceCount >= need) return;
        mm.InstanceCount = Math.Max(64, need * 2);
        buf = new float[mm.InstanceCount * stride];
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("AcidView");
        if (_m?.Sim == null) return;
        float dt = _m.Paused ? 0 : (float)Math.Min(delta, .1);
        _t += dt;
        var sim = _m.Sim; var w = _m.World;
        Etching(sim, w);
        // fumes off the acid, the more of it the more, from voxels of it picked at random
        _fumeDue += Math.Min(24, sim.Acids.Count * .2f) * dt;
        _pools.Clear();
        if (sim.Acids.Count > 0)
        {
            int want = (int)_fumeDue, skip = _rnd.Next(Math.Max(1, Math.Min(sim.Acids.Count, 300))), i = 0;
            _fumeDue -= want;
            foreach (int p in sim.Acids)
            {
                var (x, y, z) = WaterPlanner.Unpack(p);
                int key = (x >> 5) | (z >> 5) << 10 | (y >> 5) << 20;
                var c = _pools.GetValueOrDefault(key);
                _pools[key] = (c.x + x, c.y + y, c.z + z, c.n + 1);
                if (want > 0 && i++ >= skip) { Fume(x + .5f, y + 1, z + .5f, .16f); want--; skip = i + _rnd.Next(20); }
            }
        }
        _fumeDue = Math.Min(_fumeDue, 6);
        Grow(_fumes, ref _fumeBuf, _puffs.Count, Stride);
        int n = 0;
        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            var q = _puffs[i];
            q.Age += dt;
            if (q.Age >= q.Life) { _puffs.RemoveAt(i); continue; }
            q.P += q.V * dt;
            _puffs[i] = q;
            float f = q.Age / q.Life, size = q.Size * (1 + f * 2.2f), a = Math.Min(1, q.Age * 2.5f) * (1 - f) * .22f;
            int o = n++ * Stride;
            var b = _fumeBuf;
            b[o] = size; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = q.P.X;
            b[o + 4] = 0; b[o + 5] = size; b[o + 6] = 0; b[o + 7] = q.P.Y;
            b[o + 8] = 0; b[o + 9] = 0; b[o + 10] = size; b[o + 11] = q.P.Z;
            b[o + 12] = .3f; b[o + 13] = .42f; b[o + 14] = .17f; b[o + 15] = a;
            b[o + 16] = q.Seed; b[o + 17] = 0; b[o + 18] = 0; b[o + 19] = 0;
        }
        if (n > 0) RenderingServer.MultimeshSetBuffer(_fumes.Multimesh.GetRid(), _fumeBuf);
        _fumes.Multimesh.VisibleInstanceCount = n;
        Light(w);
    }

    /// <summary>The faces of what the acid is eating, darker and greener the farther it has got into them.</summary>
    void Etching(Sim sim, VoxelWorld w)
    {
        int count = Math.Min(MaxEtched, sim.Etched.Count);
        Grow(_etch, ref _etchBuf, count, EtchStride);
        int n = 0;
        foreach (var (p, e) in sim.Etched)
        {
            if (n >= count) break;
            var (x, y, z) = WaterPlanner.Unpack(p);
            if (w.Get(x, y, z) == Mat.Air) continue;
            int o = n++ * EtchStride;
            var b = _etchBuf;
            b[o] = 1; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = w.WX(x + .5f);
            b[o + 4] = 0; b[o + 5] = 1; b[o + 6] = 0; b[o + 7] = w.WY(y + .5f);
            b[o + 8] = 0; b[o + 9] = 0; b[o + 10] = 1; b[o + 11] = w.WZ(z + .5f);
            b[o + 12] = .1f; b[o + 13] = .16f + .08f * e; b[o + 14] = .04f; b[o + 15] = .25f + .5f * Math.Clamp(e, 0, 1);
        }
        if (n > 0) RenderingServer.MultimeshSetBuffer(_etch.Multimesh.GetRid(), _etchBuf);
        _etch.Multimesh.VisibleInstanceCount = n;
    }

    /// <summary>A light over each of the biggest pools of acid, weak, wavering a little; it shows at night.</summary>
    void Light(VoxelWorld w)
    {
        _biggest.Clear();
        foreach (var (key, c) in _pools) if (c.n >= 6) _biggest.Add((key, c.n));
        _biggest.Sort((a, b) => b.n.CompareTo(a.n));
        for (int i = 0; i < _lights.Length; i++)
        {
            var l = _lights[i];
            if (i >= _biggest.Count) { l.Visible = false; continue; }
            var c = _pools[_biggest[i].key];
            l.Visible = true;
            l.Position = new Vector3(w.WX(c.x / c.n), w.WY(c.y / c.n + 6), w.WZ(c.z / c.n));
            float waver = 1 + .08f * MathF.Sin((float)_t * 3.1f + i * 1.7f);
            l.LightEnergy = Math.Clamp(.25f + MathF.Sqrt(c.n) * .08f, .25f, 1.2f) * waver * (_m.Atmos?.LampBoost ?? 1);
            l.OmniRange = Math.Clamp(2.5f + MathF.Sqrt(c.n) * .25f, 2.5f, 7);
        }
    }
}
