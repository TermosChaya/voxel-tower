using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Smoke from the houses lived in: a dwelling closed and with bunks in it breathes a thin grey smoke out of the smoke
/// hole under its ridge over the far gable (Shapes.SmokeHole), rising and leaning away with the wind, more of it at
/// night and in the cold of the rain; none from a house that has lost its roof there. And from the chimneys of the town's
/// working buildings (Sim.Frontage.cs): the smithy's thick and dark, with sparks going up in it, the alchemists' tinged
/// green, the miners' smelter's brown; the rest a hearth's grey.
/// </summary>
public partial class ChimneyView : Node3D
{
    struct Puff { public Vector3 P, V; public float Age, Life, Size, Seed; public Color C; public bool Spark; }
    /// <summary>A place smoke comes out: how much of it, of what colour, with sparks or not.</summary>
    struct Top { public Vector3 P; public float Rate; public Color C; public bool Sparks; }

    Main _m;
    MultiMeshInstance3D _smoke;
    float[] _buf = Array.Empty<float>();
    readonly List<Puff> _puffs = new();
    readonly List<Top> _tops = new();
    float _weight;
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
        _weight = 0;
        var sim = _m.Sim; var w = _m.World;
        var hearth = new Color(.5f, .48f, .46f);
        foreach (var z in sim.Zones)
        {
            if (z.Kind != Zone.Dwelling || !z.Closed || z.Beds <= 0 || z.Depth <= 0 || sim.BuildingOn(z) != null) continue;
            var (x, cz, top) = Shapes.SmokeHole(z.X0, z.Z0, z.X1, z.Z1, z.Depth);
            // the ridge there, or within a voxel of it
            int y = top + 1;
            while (y > top - 2 && w.InBounds(x, y, cz) && w.Get(x, y, cz) == Mat.Air) y--;
            if (!w.InBounds(x, y, cz) || !Mat.IsPlayer[w.Get(x, y, cz)]) continue;
            _tops.Add(new Top { P = new Vector3(w.WX(x + .5f), w.WY(y + 1), w.WZ(cz + .5f)), Rate = 1, C = hearth });
        }
        foreach (var f in sim.Fixtures)
        {
            if (f.Kind != FixtureKind.Chimney) continue;
            var b = sim.BuildingAt((int)f.LX, (int)f.LZ) ?? Nearest(sim, f.LX, f.LZ);
            if (b is not { Ready: true }) continue;
            var top = new Top { P = new Vector3(w.WX(f.LX), w.WY(f.LY), w.WZ(f.LZ)), Rate = 1.2f, C = hearth };
            switch (b.Kind)
            {
                case "smithy": top.Rate = 2.4f; top.C = new Color(.3f, .29f, .28f); top.Sparks = true; break;
                case "lab": top.Rate = 1.6f; top.C = new Color(.44f, .62f, .42f); break;
                case "mine": top.Rate = 2f; top.C = new Color(.4f, .34f, .3f); break;
            }
            _tops.Add(top);
        }
        foreach (var t in _tops) _weight += t.Rate;
    }

    /// <summary>The building whose chimney stands out over a column, a little way off its walls.</summary>
    static Building Nearest(Sim sim, float x, float z)
    {
        Building best = null;
        float bd = 20 * 20;
        foreach (var b in sim.Buildings)
        {
            float dx = Math.Max(0, Math.Max(b.X0 - x, x - b.X1)), dz = Math.Max(0, Math.Max(b.Z0 - z, z - b.Z1));
            if (dx * dx + dz * dz < bd) { bd = dx * dx + dz * dz; best = b; }
        }
        return best;
    }

    /// <summary>One of the tops, the more smoke it gives the likelier.</summary>
    Top Pick()
    {
        float r = (float)_rnd.NextDouble() * _weight;
        foreach (var t in _tops) { r -= t.Rate; if (r <= 0) return t; }
        return _tops[^1];
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
        _due += rate * dt * _weight;
        while (_due >= 1 && _tops.Count > 0 && _puffs.Count < MaxPuffs)
        {
            _due -= 1;
            var at = Pick();
            _puffs.Add(new Puff
            {
                P = at.P + new Vector3(R(-.03f, .03f), 0, R(-.03f, .03f)),
                V = new Vector3(lean * R(.12f, .2f), R(.22f, .32f), lean * R(-.04f, .06f)),
                Life = R(4.5f, 7f), Size = R(.14f, .2f), Seed = (float)_rnd.NextDouble(), C = at.C,
            });
            // the smith's fire throws sparks up the flue now and then
            if (at.Sparks && _rnd.NextDouble() < .45 && _puffs.Count < MaxPuffs)
                _puffs.Add(new Puff
                {
                    P = at.P, V = new Vector3(R(-.12f, .12f), R(.7f, 1.1f), R(-.12f, .12f)),
                    Life = R(.5f, 1.1f), Size = R(.025f, .04f), Seed = (float)_rnd.NextDouble(), C = new Color(1f, .62f, .2f), Spark = true,
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
            float f = q.Age / q.Life, size = q.Spark ? q.Size : q.Size * (1 + f * 3.5f), a = q.Spark ? 1 - f * f : Math.Min(1, q.Age * 2f) * (1 - f) * .5f;
            int o = n++ * Stride;
            var b = _buf;
            b[o] = size; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = q.P.X;
            b[o + 4] = 0; b[o + 5] = size; b[o + 6] = 0; b[o + 7] = q.P.Y;
            b[o + 8] = 0; b[o + 9] = 0; b[o + 10] = size; b[o + 11] = q.P.Z;
            b[o + 12] = q.C.R; b[o + 13] = q.C.G; b[o + 14] = q.C.B; b[o + 15] = a;
            b[o + 16] = q.Seed; b[o + 17] = 0; b[o + 18] = 0; b[o + 19] = 0;
        }
        if (n > 0) RenderingServer.MultimeshSetBuffer(mm.GetRid(), _buf);
        mm.VisibleInstanceCount = n;
    }
}
