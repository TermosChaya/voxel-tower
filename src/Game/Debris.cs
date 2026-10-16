using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>Loose voxels flying off when something is dug out or taken apart; they bounce once and fade.</summary>
public partial class Debris : MultiMeshInstance3D
{
    const int Max = 1500;
    struct Bit { public Vector3 Pos, Vel; public float Life, Size, Rx, Ry; public Color C; }

    readonly Bit[] _bits = new Bit[Max];
    int _n;
    VoxelWorld _w;
    float[] _buf;
    readonly Random _rnd = new();

    public void Init(VoxelWorld w)
    {
        _w = w;
        var mat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .9f };
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            InstanceCount = Max,
            VisibleInstanceCount = 0,
            Mesh = new BoxMesh { Size = Vector3.One, Material = mat },
        };
        CastShadow = ShadowCastingSetting.On;
        _buf = new float[Max * 16];
    }

    float R(float a, float b) => a + (float)_rnd.NextDouble() * (b - a);

    /// <summary>Throws n bits of material m from a voxel position.</summary>
    public void Spawn(byte m, int vx, int vy, int vz, int n, float speed)
    {
        var at = new Vector3(_w.WX(vx + .5f), _w.WY(vy + .5f), _w.WZ(vz + .5f));
        float shade = R(.75f, 1.1f);
        var c = new Color(Mat.Lin[m * 3] * shade, Mat.Lin[m * 3 + 1] * shade, Mat.Lin[m * 3 + 2] * shade);
        for (int i = 0; i < n; i++)
        {
            if (_n >= Max) { Array.Copy(_bits, 1, _bits, 0, Max - 1); _n = Max - 1; }
            _bits[_n++] = new Bit
            {
                Pos = at,
                Vel = new Vector3(R(-1, 1) * speed, R(.3f, 1.2f) * speed * 1.3f, R(-1, 1) * speed),
                Life = R(1.2f, 2.4f),
                Size = VoxelWorld.Vox * R(.8f, 1.6f),
                Rx = R(0, 6), Ry = R(0, 6),
                C = c,
            };
        }
    }

    public override void _Process(double delta)
    {
        if (_n == 0) { if (Multimesh.VisibleInstanceCount != 0) Multimesh.VisibleInstanceCount = 0; return; }
        float dt = (float)Math.Min(delta, .05);
        int k = 0;
        for (int i = 0; i < _n; i++)
        {
            var b = _bits[i];
            b.Life -= dt;
            if (b.Life <= 0) continue;
            b.Vel.Y -= 9.8f * dt;
            b.Pos += b.Vel * dt;
            b.Rx += b.Vel.X * dt * 3;
            if (b.Vel.Y < 0 && _w.Get(_w.VX(b.Pos.X), _w.VY(b.Pos.Y - b.Size / 2), _w.VZ(b.Pos.Z)) != 0)
            {
                b.Pos.Y = (_w.VY(b.Pos.Y - b.Size / 2) + 1) * VoxelWorld.Vox + b.Size / 2;
                b.Vel.X *= .45f; b.Vel.Z *= .45f; b.Vel.Y = 0;
            }
            _bits[k++] = b;
        }
        _n = k;
        for (int i = 0; i < _n; i++)
        {
            ref var b = ref _bits[i];
            float s = b.Size * Math.Min(1f, b.Life * 2);
            var basis = new Basis(new Vector3(1, 0, 0), b.Rx) * new Basis(new Vector3(0, 1, 0), b.Ry);
            basis = basis.Scaled(new Vector3(s, s, s));
            int o = i * 16;
            _buf[o] = basis.X.X; _buf[o + 1] = basis.Y.X; _buf[o + 2] = basis.Z.X; _buf[o + 3] = b.Pos.X;
            _buf[o + 4] = basis.X.Y; _buf[o + 5] = basis.Y.Y; _buf[o + 6] = basis.Z.Y; _buf[o + 7] = b.Pos.Y;
            _buf[o + 8] = basis.X.Z; _buf[o + 9] = basis.Y.Z; _buf[o + 10] = basis.Z.Z; _buf[o + 11] = b.Pos.Z;
            _buf[o + 12] = b.C.R; _buf[o + 13] = b.C.G; _buf[o + 14] = b.C.B; _buf[o + 15] = 1;
        }
        RenderingServer.MultimeshSetBuffer(Multimesh.GetRid(), _buf);
        Multimesh.VisibleInstanceCount = _n;
    }
}
