using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Loose voxels flying off when something is dug out or taken apart; they bounce once and fade. Also what a blow throws
/// off where it lands: blood sprayed on along the blade's way, splinters off a shield's boards, and sparks off steel
/// and mail, drawn glowing and gone in a moment.
/// </summary>
public partial class Debris : MultiMeshInstance3D
{
    const int Max = 1500, MaxSparks = 400;
    struct Bit { public Vector3 Pos, Vel; public float Life, Size, Rx, Ry, Long, Fall; public Color C; }

    readonly Bit[] _bits = new Bit[Max];
    readonly Bit[] _sparks = new Bit[MaxSparks];
    int _n, _ns;
    VoxelWorld _w;
    float[] _buf, _sparkBuf;
    MultiMeshInstance3D _glow;
    readonly Random _rnd = new();

    /// <summary>While this holds (the game stopped), what flies hangs where it is.</summary>
    public Func<bool> Held;

    /// <summary>Sparks and bits thrown by blows since the start (the self-test asks).</summary>
    public int Sparked { get; private set; }
    public int Sprayed { get; private set; }

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
        // sparks: lit by nothing but themselves, added to what is behind them
        var hot = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _glow = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = MaxSparks, VisibleInstanceCount = 0,
                Mesh = new BoxMesh { Size = Vector3.One, Material = hot },
            },
            CastShadow = ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-1e4f, -1e3f, -1e4f), new Vector3(2e4f, 2e3f, 2e4f)),
        };
        AddChild(_glow);
        _sparkBuf = new float[MaxSparks * 16];
    }

    float R(float a, float b) => a + (float)_rnd.NextDouble() * (b - a);

    Vector3 Scatter() { var v = new Vector3(R(-1, 1), R(-1, 1), R(-1, 1)); return v.LengthSquared() > 1 ? v.Normalized() : v; }

    void Add(in Bit b)
    {
        if (_n >= Max) { Array.Copy(_bits, 1, _bits, 0, Max - 1); _n = Max - 1; }
        _bits[_n++] = b;
    }

    static Color Of(byte m, float shade) => new(Mat.Lin[m * 3] * shade, Mat.Lin[m * 3 + 1] * shade, Mat.Lin[m * 3 + 2] * shade);

    /// <summary>Throws n bits of material m from a voxel position.</summary>
    public void Spawn(byte m, int vx, int vy, int vz, int n, float speed, float size = 1)
    {
        var at = new Vector3(_w.WX(vx + .5f), _w.WY(vy + .5f), _w.WZ(vz + .5f));
        var c = Of(m, R(.75f, 1.1f));
        for (int i = 0; i < n; i++)
            Add(new Bit
            {
                Pos = at,
                Vel = new Vector3(R(-1, 1) * speed, R(.3f, 1.2f) * speed * 1.3f, R(-1, 1) * speed),
                Life = R(1.2f, 2.4f),
                Size = VoxelWorld.Vox * size * R(.8f, 1.6f),
                Rx = R(0, 6), Ry = R(0, 6), Long = 1, Fall = 1,
                C = c,
            });
    }

    /// <summary>
    /// Throws n bits of material m from a spot in the world (metres) on along a way (a unit vector), spread out so much
    /// (0 a thin jet, 1 every way), at about so many metres a second; drawn so long for their width (a splinter is long).
    /// </summary>
    public void Spray(byte m, Vector3 at, Vector3 way, int n, float speed, float spread, float size, float stretch = 1, float life = 1, float ahead = 0)
    {
        Sprayed += n;
        for (int i = 0; i < n; i++)
        {
            var v = (way * R(.55f, 1.15f) + Scatter() * spread).Normalized() * speed * R(.5f, 1.1f) + Vector3.Up * R(0, .6f) * speed * .3f;
            // already some way out, for a blow seen with the game stopped
            float t = R(.15f, 1) * ahead;
            Add(new Bit
            {
                Pos = at + Scatter() * VoxelWorld.Vox * .4f + v * t + Vector3.Down * 4.9f * t * t,
                Vel = v,
                Life = R(.7f, 1.3f) * life,
                Size = VoxelWorld.Vox * size * R(.6f, 1.3f),
                Rx = R(0, 6), Ry = R(0, 6), Long = stretch * R(.7f, 1.3f), Fall = 1,
                C = Of(m, R(.7f, 1.05f)),
            });
        }
    }

    /// <summary>Sparks struck off steel or mail at a spot (metres), flying on mostly the way the blade went and back off the face.</summary>
    public void Sparks(Vector3 at, Vector3 way, int n, float speed = 4.5f, float ahead = 0)
    {
        Sparked += n;
        for (int i = 0; i < n; i++)
        {
            if (_ns >= MaxSparks) { Array.Copy(_sparks, 1, _sparks, 0, MaxSparks - 1); _ns = MaxSparks - 1; }
            float hot = R(0, 1);
            var v = (way * R(-.4f, 1) + Scatter() * .9f).Normalized() * speed * R(.4f, 1.3f) + Vector3.Up * R(0, 1.2f);
            _sparks[_ns++] = new Bit
            {
                Pos = at + v * R(.1f, 1) * ahead,
                Vel = v,
                Life = R(.12f, .38f),
                Size = VoxelWorld.Vox * R(.07f, .14f),
                Long = R(4, 9), Fall = .35f,
                C = new Color(1.6f + hot * 1.4f, 1.0f + hot * 1.2f, .35f + hot * .7f),
            };
        }
    }

    public override void _Process(double delta)
    {
        float dt = Held?.Invoke() == true ? 0 : (float)Math.Min(delta, .05);
        Sparkle(dt);
        if (_n == 0) { if (Multimesh.VisibleInstanceCount != 0) Multimesh.VisibleInstanceCount = 0; return; }
        int k = 0;
        for (int i = 0; i < _n; i++)
        {
            var b = _bits[i];
            b.Life -= dt;
            if (b.Life <= 0) continue;
            b.Vel.Y -= 9.8f * dt * b.Fall;
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
            var basis = new Basis(new Vector3(1, 0, 0), b.Rx) * new Basis(new Vector3(0, 1, 0), b.Ry) * Basis.FromScale(new Vector3(s, s, s * b.Long));
            Put(_buf, i, basis, b.Pos, b.C, 1);
        }
        RenderingServer.MultimeshSetBuffer(Multimesh.GetRid(), _buf);
        Multimesh.VisibleInstanceCount = _n;
    }

    /// <summary>The sparks: each a streak along the way it flies, cooling from white to red as it goes out.</summary>
    void Sparkle(float dt)
    {
        var mm = _glow.Multimesh;
        if (_ns == 0) { if (mm.VisibleInstanceCount != 0) mm.VisibleInstanceCount = 0; return; }
        int k = 0;
        for (int i = 0; i < _ns; i++)
        {
            var b = _sparks[i];
            b.Life -= dt;
            if (b.Life <= 0) continue;
            b.Vel.Y -= 9.8f * dt * b.Fall;
            b.Vel *= 1 - 2.5f * dt;
            b.Pos += b.Vel * dt;
            _sparks[k++] = b;
        }
        _ns = k;
        for (int i = 0; i < _ns; i++)
        {
            ref var b = ref _sparks[i];
            var z = b.Vel.LengthSquared() > 1e-4f ? b.Vel.Normalized() : Vector3.Up;
            var x = z.Cross(Mathf.Abs(z.Y) < .9f ? Vector3.Up : Vector3.Right).Normalized();
            var y = z.Cross(x);
            float len = b.Size * b.Long * Math.Clamp(b.Vel.Length() / 3, .3f, 1.2f), fade = Math.Clamp(b.Life / .15f, 0, 1);
            var c = new Color(b.C.R * fade, b.C.G * fade * fade, b.C.B * fade * fade * fade);
            Put(_sparkBuf, i, new Basis(x * b.Size, y * b.Size, z * len), b.Pos, c, 1);
        }
        RenderingServer.MultimeshSetBuffer(mm.GetRid(), _sparkBuf);
        mm.VisibleInstanceCount = _ns;
    }

    static void Put(float[] buf, int i, Basis basis, Vector3 at, Color c, float a)
    {
        int o = i * 16;
        buf[o] = basis.X.X; buf[o + 1] = basis.Y.X; buf[o + 2] = basis.Z.X; buf[o + 3] = at.X;
        buf[o + 4] = basis.X.Y; buf[o + 5] = basis.Y.Y; buf[o + 6] = basis.Z.Y; buf[o + 7] = at.Y;
        buf[o + 8] = basis.X.Z; buf[o + 9] = basis.Y.Z; buf[o + 10] = basis.Z.Z; buf[o + 11] = at.Z;
        buf[o + 12] = c.R; buf[o + 13] = c.G; buf[o + 14] = c.B; buf[o + 15] = a;
    }
}
