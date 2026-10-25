using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Smoke from the houses lived in: a dwelling closed and with bunks in it breathes a thin grey smoke out of the smoke
/// hole under its ridge over the far gable (Shapes.SmokeHole), rising and leaning away with the wind, more of it at
/// night and in the cold of the rain; none from a house that has lost its roof there.
/// </summary>
public partial class ChimneyView : Node3D
{
    struct Puff { public Vector3 P, V; public float Age, Life, Size, Seed; }

    Main _m;
    MultiMeshInstance3D _smoke;
    float[] _buf = Array.Empty<float>();
    readonly List<Puff> _puffs = new();
    readonly List<Vector3> _tops = new();
    readonly Random _rnd = new(23);
    float _due;
    double _nextLook;

    const int MaxPuffs = 360, Stride = 20;

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
    }

    float R(float a, float b) => a + (float)_rnd.NextDouble() * (b - a);

    /// <summary>The smoke holes that smoke now, looked for again every couple of seconds.</summary>
    void LookForChimneys()
    {
        _tops.Clear();
        var sim = _m.Sim; var w = _m.World;
        foreach (var z in sim.Zones)
        {
            if (z.Kind != Zone.Dwelling || !z.Closed || z.Beds <= 0 || z.Depth <= 0) continue;
            var (x, cz, top) = Shapes.SmokeHole(z.X0, z.Z0, z.X1, z.Z1, z.Depth);
            // the ridge there, or within a voxel of it
            int y = top + 1;
            while (y > top - 2 && w.InBounds(x, y, cz) && w.Get(x, y, cz) == Mat.Air) y--;
            if (!w.InBounds(x, y, cz) || !Mat.IsPlayer[w.Get(x, y, cz)]) continue;
            _tops.Add(new Vector3(w.WX(x + .5f), w.WY(y + 1), w.WZ(cz + .5f)));
        }
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("ChimneyView");
        if (_m?.Sim == null) return;
        float dt = _m.Paused ? 0 : (float)Math.Min(delta, .1);
        if (Time.GetTicksMsec() / 1000.0 >= _nextLook) { _nextLook = Time.GetTicksMsec() / 1000.0 + 2; LookForChimneys(); }
        var sim = _m.Sim;
        // more of it by night and in the rain, and the wind lays it over
        float rate = (1.2f + (_m.Atmos?.LampBoost ?? 1) * .6f) * (sim.Weather == Weather.Rain ? 1.4f : 1);
        float lean = sim.Weather == Weather.Wind ? .9f : .25f;
        _due += rate * dt * _tops.Count;
        while (_due >= 1 && _tops.Count > 0 && _puffs.Count < MaxPuffs)
        {
            _due -= 1;
            var at = _tops[_rnd.Next(_tops.Count)];
            _puffs.Add(new Puff
            {
                P = at + new Vector3(R(-.03f, .03f), 0, R(-.03f, .03f)),
                V = new Vector3(lean * R(.12f, .2f), R(.22f, .32f), lean * R(-.04f, .06f)),
                Life = R(4.5f, 7f), Size = R(.14f, .2f), Seed = (float)_rnd.NextDouble(),
            });
        }
        _due = Math.Min(_due, 3);
        var mm = _smoke.Multimesh;
        if (mm.InstanceCount < _puffs.Count) { mm.InstanceCount = Math.Max(64, _puffs.Count * 2); _buf = new float[mm.InstanceCount * Stride]; }
        int n = 0;
        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            var q = _puffs[i];
            q.Age += dt;
            if (q.Age >= q.Life) { _puffs.RemoveAt(i); continue; }
            // slowing as it rises, spreading as it goes
            q.P += q.V * dt;
            q.V.Y *= 1 - .12f * dt;
            _puffs[i] = q;
            float f = q.Age / q.Life, size = q.Size * (1 + f * 3.5f), a = Math.Min(1, q.Age * 2f) * (1 - f) * .5f;
            int o = n++ * Stride;
            var b = _buf;
            b[o] = size; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = q.P.X;
            b[o + 4] = 0; b[o + 5] = size; b[o + 6] = 0; b[o + 7] = q.P.Y;
            b[o + 8] = 0; b[o + 9] = 0; b[o + 10] = size; b[o + 11] = q.P.Z;
            b[o + 12] = .5f; b[o + 13] = .48f; b[o + 14] = .46f; b[o + 15] = a;
            b[o + 16] = q.Seed; b[o + 17] = 0; b[o + 18] = 0; b[o + 19] = 0;
        }
        if (n > 0) RenderingServer.MultimeshSetBuffer(mm.GetRid(), _buf);
        mm.VisibleInstanceCount = n;
    }
}
