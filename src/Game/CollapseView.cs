using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// What weight does to what is built, as it is seen and heard: the cracks of cracked masonry and timber, dark lines
/// over each cracked voxel's open faces that open and widen as it cracks; dust and chips trickling out of what is
/// cracking under its load, and its grinding or creaking; chips where a voxel crumbles out; the ground shaking as a
/// building begins to go over; and a spray of water where a piece comes down into it.
/// </summary>
public partial class CollapseView : Node3D
{
    Main _m;
    MultiMeshInstance3D _cracks;
    float[] _buf = Array.Empty<float>();
    int _version = -1;
    double _drawnAt;
    float _trickle, _creak;
    readonly Random _rnd = new(11);
    readonly List<GpuParticles3D> _sprays = new();
    int _nextSpray;
    /// <summary>The most cracked voxels drawn at once.</summary>
    const int Most = 12000;
    const int Stride = 16;

    /// <summary>Cracked voxels drawn now (the self-test asks).</summary>
    public int CracksDrawn => _cracks.Multimesh.VisibleInstanceCount;

    public void Init(Main m)
    {
        _m = m;
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/cracks.gdshader") };
        mat.SetShaderParameter("vox", VoxelWorld.Vox);
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One * VoxelWorld.Vox * 1.03f },
        };
        _cracks = new MultiMeshInstance3D
        {
            Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-100, -10, -100), new Vector3(200, 60, 200)),
        };
        AddChild(_cracks);
        for (int i = 0; i < 4; i++) _sprays.Add(Spray());
        var sim = m.Sim;
        sim.Crumbled += Crumbled;
        sim.Plunged += Plunged;
        sim.GoingOver += f => _m.Spectacle?.Felt(f.X, f.Z, Math.Clamp(f.Voxels.Count / 12000f, .15f, .5f), flash: false);
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("CollapseView");
        var sim = _m?.Sim;
        if (sim == null) return;
        // drawn again as they change, ten times a second at most
        double now = Time.GetTicksMsec() / 1000.0;
        if (sim.CracksVersion != _version && now - _drawnAt > .1) { _version = sim.CracksVersion; _drawnAt = now; Draw(sim); }
        Trickle(sim, (float)Math.Min(delta, .1));
    }

    /// <summary>Every cracked voxel open to the air on some side, a box over it the crack is drawn on.</summary>
    void Draw(Sim sim)
    {
        var w = _m.World;
        var mm = _cracks.Multimesh;
        int need = Math.Min(Most, sim.Cracks.Count);
        if (mm.InstanceCount < need)
        {
            mm.InstanceCount = Math.Min(Most, Math.Max(256, need * 2));
            _buf = new float[mm.InstanceCount * Stride];
        }
        int n = 0;
        foreach (var (p, c) in sim.Cracks)
        {
            if (n >= mm.InstanceCount) break;
            if (c < .03f) continue;
            var (x, y, z) = WaterPlanner.Unpack(p);
            if (!Open(w, x + 1, y, z) && !Open(w, x - 1, y, z) && !Open(w, x, y + 1, z) && !Open(w, x, y - 1, z) && !Open(w, x, y, z + 1) && !Open(w, x, y, z - 1)) continue;
            int o = n * Stride;
            _buf[o] = 1; _buf[o + 1] = 0; _buf[o + 2] = 0; _buf[o + 3] = w.WX(x + .5f);
            _buf[o + 4] = 0; _buf[o + 5] = 1; _buf[o + 6] = 0; _buf[o + 7] = w.WY(y + .5f);
            _buf[o + 8] = 0; _buf[o + 9] = 0; _buf[o + 10] = 1; _buf[o + 11] = w.WZ(z + .5f);
            _buf[o + 12] = c; _buf[o + 13] = 0; _buf[o + 14] = 0; _buf[o + 15] = 1;
            n++;
        }
        if (mm.InstanceCount > 0) RenderingServer.MultimeshSetBuffer(mm.GetRid(), _buf);
        mm.VisibleInstanceCount = n;
    }

    static bool Open(VoxelWorld w, int x, int y, int z)
    {
        if (!w.InBounds(x, y, z)) return true;
        byte m = w.Get(x, y, z);
        return m == Mat.Air || m == Mat.Water || Mat.Passable[m];
    }

    /// <summary>
    /// Out of what is cracking under its load: a chip or a little dust now and then, the more of it cracking the more,
    /// and its grinding or creaking, near the view, every so often.
    /// </summary>
    void Trickle(Sim sim, float dt)
    {
        var list = sim.Cracking;
        if (list.Count == 0 || _m.Paused) return;
        _trickle += dt * Math.Min(14, 2 + list.Count * .1f);
        _creak -= dt;
        var w = _m.World;
        while (_trickle >= 1)
        {
            _trickle--;
            var (x, y, z) = WaterPlanner.Unpack(list[_rnd.Next(list.Count)]);
            byte m = w.Get(x, y, z);
            if (m == Mat.Air) continue;
            _m.Debris.Spawn(m, x, y, z, 1, .35f, .45f);
        }
        if (_creak > 0) return;
        _creak = .9f + (float)_rnd.NextDouble() * 1.2f;
        var (cx, cy, cz) = WaterPlanner.Unpack(list[_rnd.Next(list.Count)]);
        byte cm = w.Get(cx, cy, cz);
        _m.Sound?.Strain(cx + .5f, cy, cz + .5f, !Mat.IsTrunk(cm) && cm is not (Mat.Wood or Mat.Deck), Math.Clamp(.3f + list.Count / 300f, .3f, .9f));
    }

    /// <summary>A voxel crumbled out under its load or cracked through by a blow: chips of it.</summary>
    void Crumbled(int x, int y, int z, byte m) => _m.Debris.Spawn(m, x, y, z, 3, 1.1f, .7f);

    /// <summary>A piece come down into water: a spray off the surface, the higher and the more of it the heavier and the faster.</summary>
    void Plunged(float x, float y, float z, int n, float speed)
    {
        var w = _m.World;
        int ix = Math.Clamp((int)x, 0, w.SX - 1), iz = Math.Clamp((int)z, 0, w.SZ - 1), iy = Math.Clamp((int)y, 1, w.SY - 2);
        while (iy > 1 && w.Get(ix, iy - 1, iz) != Mat.Water) iy--;
        while (iy < w.SY - 1 && w.Get(ix, iy, iz) == Mat.Water) iy++;
        var s = _sprays[_nextSpray];
        _nextSpray = (_nextSpray + 1) % _sprays.Count;
        float k = Math.Clamp(MathF.Sqrt(n) / 40 * speed / (8 * VoxelWorld.PerMetre), .3f, 1.6f);
        s.Position = new Vector3(w.WX(x), w.WY(iy), w.WZ(z));
        s.Scale = Vector3.One * Math.Clamp(MathF.Cbrt(n) / 10, .5f, 2.5f);
        s.SpeedScale = 1;
        ((ParticleProcessMaterial)s.ProcessMaterial).InitialVelocityMax = 3.5f * k;
        s.Amount = Math.Clamp(16 + n / 20, 24, 160);
        s.Restart();
        s.Emitting = true;
    }

    /// <summary>A spray of water, once off: drops thrown up and out, falling back.</summary>
    GpuParticles3D Spray()
    {
        var pm = new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 38, InitialVelocityMin = .8f, InitialVelocityMax = 3.5f, Gravity = new Vector3(0, -4.9f, 0),
            ScaleMin = .4f, ScaleMax = 1.2f, EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = .25f,
            Color = new Color(.82f, .88f, .9f, .85f),
        };
        var drop = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true,
        };
        var p = new GpuParticles3D
        {
            OneShot = true, Emitting = false, Explosiveness = .85f, Lifetime = 1.3, Amount = 64, ProcessMaterial = pm,
            DrawPass1 = new QuadMesh { Size = new Vector2(.07f, .07f), Material = drop },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityAabb = new Aabb(new Vector3(-6, -2, -6), new Vector3(12, 10, 12)),
        };
        AddChild(p);
        return p;
    }
}
