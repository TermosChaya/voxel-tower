using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Fire as it is seen: a tongue of flame over every burning voxel that faces the air (as many as can be drawn),
/// upright and licking on its own time, bigger over pitch; smoke rising from them and drifting off; a few lights on
/// the biggest fires, flickering; cinders when something burns away; and the torches the horde carries, alight by
/// their hands.
/// </summary>
public partial class FireView : Node3D
{
    struct Puff { public Vector3 P, V; public float Age, Life, Size, Seed; }

    Main _m;
    MultiMeshInstance3D _flames, _smoke;
    float[] _flameBuf = Array.Empty<float>(), _smokeBuf = Array.Empty<float>();
    readonly List<Puff> _puffs = new();
    readonly OmniLight3D[] _lights = new OmniLight3D[6];
    readonly Dictionary<int, (float x, float y, float z, int n)> _clusters = new();
    readonly List<(int key, int n)> _biggest = new();
    readonly Random _rnd = new(3);
    float _smokeDue;
    double _t;

    const int MaxFlames = 6000, MaxPuffs = 900, Stride = 20;

    public void Init(Main m)
    {
        _m = m;
        var quad = new QuadMesh { Size = Vector2.One };
        _flames = Multi(quad, new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/flame.gdshader") });
        _smoke = Multi(quad, new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/smoke.gdshader") });
        for (int i = 0; i < _lights.Length; i++)
        {
            _lights[i] = new OmniLight3D { LightColor = new Color(1f, .52f, .18f), ShadowEnabled = false, Visible = false, OmniAttenuation = 1.6f };
            AddChild(_lights[i]);
        }
        // what burns away falls in cinders
        m.Sim.Burnt += (x, y, z, mat) => { if (_rnd.NextDouble() < .1) m.Debris.Spawn(Mat.Char, x, y, z, 1, .5f, .7f); };
    }

    MultiMeshInstance3D Multi(Mesh mesh, Material mat)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh };
        var mi = new MultiMeshInstance3D
        {
            Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-100, -10, -100), new Vector3(200, 60, 200)),
        };
        AddChild(mi);
        return mi;
    }

    static void Grow(MultiMeshInstance3D mi, ref float[] buf, int need)
    {
        var mm = mi.Multimesh;
        if (mm.InstanceCount >= need) return;
        mm.InstanceCount = Math.Max(64, need * 2);
        buf = new float[mm.InstanceCount * Stride];
    }

    /// <summary>One instance: upright, scaled, at a spot in the world; its colour and its custom data.</summary>
    static void Put(float[] buf, int i, Vector3 at, float w, float h, Color c, float seed, float heat)
    {
        int o = i * Stride;
        buf[o] = w; buf[o + 1] = 0; buf[o + 2] = 0; buf[o + 3] = at.X;
        buf[o + 4] = 0; buf[o + 5] = h; buf[o + 6] = 0; buf[o + 7] = at.Y;
        buf[o + 8] = 0; buf[o + 9] = 0; buf[o + 10] = w; buf[o + 11] = at.Z;
        buf[o + 12] = c.R; buf[o + 13] = c.G; buf[o + 14] = c.B; buf[o + 15] = c.A;
        buf[o + 16] = seed; buf[o + 17] = heat; buf[o + 18] = 0; buf[o + 19] = 0;
    }

    public override void _Process(double delta)
    {
        if (_m?.Sim == null) return;
        float dt = (float)Math.Min(delta, .1);
        _t += dt;
        var sim = _m.Sim; var w = _m.World;
        int torches = 0;
        foreach (var u in sim.Units) if (!u.Dead && u.Torch) torches++;
        Grow(_flames, ref _flameBuf, Math.Min(MaxFlames, sim.Burning.Count) + torches);
        int n = 0, every = Math.Max(1, sim.Burning.Count / MaxFlames + 1), k = 0;
        _clusters.Clear();
        const float V = VoxelWorld.Vox;
        foreach (var (p, fuel) in sim.Burning)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            // fires sit in buckets of four metres, for the lights
            int key = (x >> 5) | (z >> 5) << 10 | (y >> 5) << 20;
            var c = _clusters.GetValueOrDefault(key);
            _clusters[key] = (c.x + x, c.y + y, c.z + z, c.n + 1);
            if (k++ % every != 0 || n >= MaxFlames) continue;
            // only what faces the air shows its flames: the heart of a burning wall is hidden
            if (w.Get(x, y + 1, z) != 0 && w.Get(x + 1, y, z) != 0 && w.Get(x - 1, y, z) != 0 && w.Get(x, y, z + 1) != 0 && w.Get(x, y, z - 1) != 0) continue;
            byte m = w.Get(x, y, z);
            float seed = (float)(Hash.H3(x, y, z) * 1000 % 1);
            float heat = Mat.Heat[m], size = (.8f + seed * .5f) * (heat > 1 ? 1.5f : 1) * Math.Min(1, .4f + fuel / 4);
            var at = new Vector3(w.WX(x + .5f), w.WY(y + .6f + 1.5f * size), w.WZ(z + .5f));
            Put(_flameBuf, n++, at, 2.3f * size * V, 3.6f * size * V, Colors.White, seed, heat);
        }
        // the horde's torches, alight by the off hand
        foreach (var u in sim.Units)
        {
            if (u.Dead || !u.Torch) continue;
            float side = u.Facing + MathF.PI / 2, reach = u.T.BodyR + .6f;
            var at = new Vector3(w.WX(u.X + MathF.Cos(side) * reach * .8f + MathF.Cos(u.Facing) * .8f), w.WY(u.Y + u.T.BodyH * .72f + 1.4f),
                w.WZ(u.Z + MathF.Sin(side) * reach * .8f + MathF.Sin(u.Facing) * .8f));
            Put(_flameBuf, n++, at, 1.2f * V, 2.4f * V, Colors.White, (u.Id * .37f) % 1, u.Kindling > 0 ? 1.6f : .8f);
        }
        RenderingServer.MultimeshSetBuffer(_flames.Multimesh.GetRid(), _flameBuf);
        _flames.Multimesh.VisibleInstanceCount = n;
        Smoke(sim, w, dt);
        Light();
    }

    /// <summary>Smoke: puffs from burning voxels picked at random, rising, drifting with the wind, swelling and thinning.</summary>
    void Smoke(Sim sim, VoxelWorld w, float dt)
    {
        _smokeDue += Math.Min(70, sim.Burning.Count * .35f) * dt;
        if (sim.Burning.Count > 0 && _smokeDue >= 1)
        {
            // a random burning voxel: the n-th of the dictionary, walked to at most a few hundred in
            int want = (int)_smokeDue;
            _smokeDue -= want;
            int skip = _rnd.Next(Math.Min(sim.Burning.Count, 400)), i = 0;
            foreach (int p in sim.Burning.Keys)
            {
                if (i++ < skip) continue;
                if (want-- <= 0 || _puffs.Count >= MaxPuffs) break;
                var (x, y, z) = WaterPlanner.Unpack(p);
                float heat = Mat.Heat[w.Get(x, y, z)];
                _puffs.Add(new Puff
                {
                    P = new Vector3(w.WX(x + .5f), w.WY(y + 2), w.WZ(z + .5f)),
                    V = new Vector3(.18f + (float)_rnd.NextDouble() * .2f, .55f + (float)_rnd.NextDouble() * .35f, .1f + (float)_rnd.NextDouble() * .15f),
                    Life = 4 + (float)_rnd.NextDouble() * 3, Size = (.35f + (float)_rnd.NextDouble() * .3f) * (heat > 1 ? 1.4f : 1),
                    Seed = (float)_rnd.NextDouble(),
                });
                skip = i + _rnd.Next(30);
            }
        }
        _smokeDue = Math.Min(_smokeDue, 10);
        Grow(_smoke, ref _smokeBuf, _puffs.Count);
        int n = 0;
        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            var q = _puffs[i];
            q.Age += dt;
            if (q.Age >= q.Life) { _puffs.RemoveAt(i); continue; }
            q.P += q.V * dt;
            q.V.Y *= 1 - dt * .12f;
            _puffs[i] = q;
            float f = q.Age / q.Life, size = q.Size * (1 + f * 3.2f);
            float a = Math.Min(1, q.Age * 2) * (1 - f) * .5f;
            float g = .07f + f * .1f;
            Put(_smokeBuf, n++, q.P, size, size, new Color(g, g * .95f, g * .92f, a), q.Seed, 0);
        }
        RenderingServer.MultimeshSetBuffer(_smoke.Multimesh.GetRid(), _smokeBuf);
        _smoke.Multimesh.VisibleInstanceCount = n;
    }

    /// <summary>A light on each of the biggest fires, flickering, stronger the more of it burns.</summary>
    void Light()
    {
        _biggest.Clear();
        foreach (var (key, c) in _clusters) _biggest.Add((key, c.n));
        _biggest.Sort((a, b) => b.n.CompareTo(a.n));
        var w = _m.World;
        for (int i = 0; i < _lights.Length; i++)
        {
            var l = _lights[i];
            if (i >= _biggest.Count) { l.Visible = false; continue; }
            var c = _clusters[_biggest[i].key];
            float sz = MathF.Sqrt(c.n);
            l.Visible = true;
            l.Position = new Vector3(w.WX(c.x / c.n), w.WY(c.y / c.n + 8), w.WZ(c.z / c.n));
            float flick = 1 + .18f * MathF.Sin((float)_t * 11 + i * 2.1f) + .1f * MathF.Sin((float)_t * 23 + i);
            l.LightEnergy = Math.Clamp(.5f + sz * .12f, .5f, 3.2f) * flick;
            l.OmniRange = Math.Clamp(3 + sz * .35f, 3, 11);
        }
    }
}
