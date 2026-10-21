using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Blood where it is spilt. A blow that draws blood spatters the ground on the way the blade went; one bleeding drips
/// from its wound as it goes, more the more it bleeds, and leaves a trail of drops behind it; the dead, and those down
/// and bleeding, lie in a pool that spreads out from under them, voxel by voxel. Every drop is a flat stain on the face
/// it fell on — bright and wet at first, drying dark, gone in a few minutes — and goes with the ground if that is dug
/// away or burns.
/// </summary>
public partial class BloodView : MultiMeshInstance3D
{
    const int MaxStains = 3000, MaxDrops = 400;
    /// <summary>How long a stain lies (seconds), and how long before it is gone it begins to go.</summary>
    const float Lasts = 160, Fades = 20;

    struct Stain { public Vector3 P; public float Size, Long, Yaw, Born, Grow; public int VX, VY, VZ; public bool Live; }
    struct Drop { public Vector3 P, V; public float Size; }
    sealed class Pool { public int X, Y, Z; public float Born, Spread; public List<(int dx, int dz, float at)> Tiles; public int Shown; }

    Main _m;
    VoxelWorld _w;
    readonly Stain[] _stains = new Stain[MaxStains];
    int _head, _count, _swept;
    readonly Drop[] _drops = new Drop[MaxDrops];
    int _nd;
    readonly List<Pool> _pools = new();
    // where on each one its last open wound is, in its own frame (voxels: ahead, to its left, up), and drops owed it
    readonly Dictionary<int, (float ahead, float left, float up)> _wound = new();
    readonly Dictionary<int, float> _due = new();
    readonly HashSet<int> _pooled = new();
    float[] _buf = new float[(MaxStains + MaxDrops) * 16];
    float _t, _look;
    Random _rnd = new();

    /// <summary>Stains lying now, and pools spread since the start (the self-test asks).</summary>
    public int Stains { get; private set; }
    public int Pools { get; private set; }
    public int Dripped { get; private set; }

    public void Init(Main m)
    {
        _m = m;
        _w = m.World;
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            InstanceCount = MaxStains + MaxDrops,
            VisibleInstanceCount = 0,
            // wet: it shines a little
            Mesh = new BoxMesh { Size = Vector3.One, Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .32f, MetallicSpecular = .7f } },
        };
        CastShadow = ShadowCastingSetting.Off;
        CustomAabb = new Aabb(new Vector3(-1e4f, -1e3f, -1e4f), new Vector3(2e4f, 2e3f, 2e4f));
        m.Units.Landed += Spattered;
    }

    float R(float a, float b) => a + (float)_rnd.NextDouble() * (b - a);

    /// <summary>A blow that drew blood: drops thrown on along the blade's way onto the ground, and the wound remembered for its dripping.</summary>
    void Spattered(Smiting h)
    {
        var b = h.On;
        if (b == null || h.Flesh <= 0 || h.Mat is Mat.Air or Mat.Steel or Mat.Board || Sim.Sense(b.T) <= 0) return;
        float c = MathF.Cos(b.Facing), s = MathF.Sin(b.Facing), dx = h.X - b.X, dz = h.Z - b.Z;
        _wound[b.Id] = (dx * c + dz * s, -dx * s + dz * c, h.Y - b.Y);
        float wx = h.DX, wz = h.DZ, wl = MathF.Sqrt(wx * wx + wz * wz);
        if (wl < .05f) { float a = R(0, MathF.Tau); wx = MathF.Cos(a); wz = MathF.Sin(a); wl = 1; }
        wx /= wl; wz /= wl;
        int n = Math.Clamp(h.Flesh / 3, 1, 8);
        for (int i = 0; i < n; i++)
        {
            float on = R(1, 13), aside = R(-2.5f, 2.5f);
            Fall(h.X + wx * on - wz * aside, h.Y, h.Z + wz * on + wx * aside, VoxelWorld.Vox * R(.35f, 1), R(1, 2.6f), MathF.Atan2(wz, wx));
        }
    }

    /// <summary>Blood out of a wound struck some other way (an arrow): a few drops round about.</summary>
    public void Splash(float x, float y, float z, int flesh)
    {
        int n = Math.Clamp(flesh / 3, 1, 5);
        for (int i = 0; i < n; i++) Fall(x + R(-5, 5), y, z + R(-5, 5), VoxelWorld.Vox * R(.35f, .8f), R(1, 1.8f), R(0, MathF.Tau));
    }

    /// <summary>A stain where a drop from a spot (voxels) comes down on the first face under it — none in water or off the world.</summary>
    void Fall(float x, float y, float z, float size, float stretch, float yaw)
    {
        int vx = (int)MathF.Floor(x), vz = (int)MathF.Floor(z);
        if (!_w.InBounds(vx, 0, vz)) return;
        for (int vy = Math.Min(_w.SY - 1, (int)y); vy >= 0 && vy > y - 48; vy--)
        {
            byte m = _w.Get(vx, vy, vz);
            if (m == 0) continue;
            if (m == Mat.Water) return;
            Lay(new Vector3(_w.WX(x), (vy + 1) * VoxelWorld.Vox, _w.WZ(z)), vx, vy, vz, size, stretch, yaw, 0);
            return;
        }
    }

    void Lay(Vector3 p, int vx, int vy, int vz, float size, float stretch, float yaw, float grow)
    {
        // the newest over the older, a hair above them so that they do not flicker through one another
        p.Y += .003f + (_head % 7) * .0007f;
        _stains[_head] = new Stain { P = p, Size = size, Long = stretch, Yaw = yaw, Born = _t, Grow = grow, VX = vx, VY = vy, VZ = vz, Live = true };
        _head = (_head + 1) % MaxStains;
        _count = Math.Min(MaxStains, _count + 1);
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("BloodView");
        if (_w == null) return;
        var sim = _m.Sim;
        float dt = _m.Paused ? 0 : (float)Math.Min(delta, .05);
        _t += dt;
        if (dt > 0)
        {
            Bleeding(sim, dt);
            Falling(dt);
            if ((_look += dt) >= .25f) { _look = 0; Fallen(sim); }
            Spread();
            Sweep();
        }
        Draw();
    }

    /// <summary>Drops out of the wounds of all that bleed on their feet, by how fast they bleed.</summary>
    void Bleeding(Sim sim, float dt)
    {
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.Inside || u.Down || u.Airborne != null || u.Wounds is not { Bleed: > .05f } w) continue;
            if (Sim.Sense(u.T) <= 0) continue;
            float due = (_due.TryGetValue(u.Id, out float d) ? d : R(0, 1)) + Math.Clamp(w.Bleed * 1.4f, .5f, 12) * dt;
            while (due >= 1 && _nd < MaxDrops)
            {
                due -= 1;
                float c = MathF.Cos(u.Facing), s = MathF.Sin(u.Facing);
                var (ahead, left, up) = _wound.TryGetValue(u.Id, out var wd) ? wd : (u.T.BodyR * .6f, 0, u.T.BodyH * .55f);
                float x = u.X + ahead * c - left * s, z = u.Z + ahead * s + left * c;
                _drops[_nd++] = new Drop
                {
                    P = new Vector3(_w.WX(x + R(-.3f, .3f)), _w.WY(u.Y + Math.Max(1, up - R(0, 1.5f))), _w.WZ(z + R(-.3f, .3f))),
                    V = new Vector3(R(-.12f, .12f), R(-.3f, 0), R(-.12f, .12f)),
                    Size = VoxelWorld.Vox * R(.18f, .32f),
                };
                Dripped++;
            }
            _due[u.Id] = Math.Min(due, 2);
        }
    }

    /// <summary>The drops falling; where one meets a face it lies on it as a stain (into water it is gone).</summary>
    void Falling(float dt)
    {
        int k = 0;
        for (int i = 0; i < _nd; i++)
        {
            var d = _drops[i];
            d.V.Y -= 9.8f * dt;
            d.P += d.V * dt;
            int vx = _w.VX(d.P.X), vy = (int)MathF.Floor(d.P.Y / VoxelWorld.Vox), vz = _w.VZ(d.P.Z);
            if (vy < 0 || !_w.InBounds(vx, 0, vz)) continue;
            byte m = vy < _w.SY ? _w.Get(vx, vy, vz) : (byte)0;
            if (m != 0)
            {
                if (m != Mat.Water) Lay(new Vector3(d.P.X, (vy + 1) * VoxelWorld.Vox, d.P.Z), vx, vy, vz, d.Size * R(1.4f, 2.4f), R(1, 1.4f), R(0, MathF.Tau), 0);
                continue;
            }
            _drops[k++] = d;
        }
        _nd = k;
    }

    /// <summary>The newly dead, and those down and bleeding hard: a pool begins to spread from under each.</summary>
    void Fallen(Sim sim)
    {
        foreach (var u in sim.Units)
        {
            if (u.Inside || u.Airborne != null || _pooled.Contains(u.Id) || Sim.Sense(u.T) <= 0) continue;
            bool dead = u.Dead && u.DiedAt > 0 && sim.Time - u.DiedAt < 20;
            bool down = !u.Dead && u.Down && u.Wounds is { Bleed: > .3f };
            if (!dead && !down) continue;
            _pooled.Add(u.Id);
            // a man lies on his back, his middle half his height behind where his feet were; a beast on its side
            float back = Bodies.Plan(u.T, u.Look).Beast ? 0 : u.T.BodyH * .4f;
            float x = u.X - MathF.Cos(u.Facing) * back, z = u.Z - MathF.Sin(u.Facing) * back;
            float r = Math.Clamp(u.T.BodyH * .32f, 2.5f, 8) * (dead ? 1 : .6f);
            Begin((int)MathF.Floor(x), (int)u.Y, (int)MathF.Floor(z), r, dead ? 14 : 8);
        }
    }

    void Begin(int x, int y, int z, float r, float spread)
    {
        // the tiles it will spread over, nearest first, its edge ragged
        var tiles = new List<(int, int, float)>();
        int ri = (int)MathF.Ceiling(r);
        for (int dz = -ri; dz <= ri; dz++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                float d = MathF.Sqrt(dx * dx + dz * dz) / r + (Hash01(x + dx, z + dz) - .5f) * .45f;
                if (d <= 1) tiles.Add((dx, dz, d));
            }
        tiles.Sort((a, b) => a.Item3.CompareTo(b.Item3));
        _pools.Add(new Pool { X = x, Y = y, Z = z, Born = _t, Spread = spread, Tiles = tiles });
        Pools++;
    }

    static float Hash01(int x, int z) { uint h = (uint)(x * 73856093 ^ z * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xffff) / 65535f; }

    /// <summary>The pools spreading: fast at first, slower as they go (as the square root of the time), a voxel at a time.</summary>
    void Spread()
    {
        for (int i = _pools.Count - 1; i >= 0; i--)
        {
            var p = _pools[i];
            float reach = MathF.Sqrt(Math.Min(1, (_t - p.Born) / p.Spread));
            while (p.Shown < p.Tiles.Count && p.Tiles[p.Shown].at <= reach)
            {
                var (dx, dz, _) = p.Tiles[p.Shown++];
                int vx = p.X + dx, vz = p.Z + dz;
                // on the face at about the height it lies at: not up a wall beside it, nor down a drop
                for (int vy = p.Y + 1; vy >= p.Y - 3; vy--)
                {
                    if (!_w.InBounds(vx, vy, vz) || _w.Get(vx, vy, vz) == 0) continue;
                    if (_w.Get(vx, vy, vz) == Mat.Water || vy + 1 < _w.SY && _w.Get(vx, vy + 1, vz) != 0) break;
                    Lay(new Vector3(_w.WX(vx + .5f), (vy + 1) * VoxelWorld.Vox, _w.WZ(vz + .5f)), vx, vy, vz, VoxelWorld.Vox * R(.92f, 1.04f), 1, 0, .9f);
                    break;
                }
            }
            if (p.Shown >= p.Tiles.Count) _pools.RemoveAt(i);
        }
        if (_pooled.Count > 4000) _pooled.Clear();
    }

    /// <summary>A few stains a frame looked at again: gone with the face they lay on, or old.</summary>
    void Sweep()
    {
        for (int n = 0; n < 80 && _count > 0; n++)
        {
            _swept = (_swept + 1) % MaxStains;
            ref var s = ref _stains[_swept];
            if (!s.Live) continue;
            if (_t - s.Born > Lasts || _w.Get(s.VX, s.VY, s.VZ) is 0 or Mat.Water) s.Live = false;
        }
        if (_wound.Count > 2000) { _wound.Clear(); _due.Clear(); }
    }

    void Draw()
    {
        int k = 0;
        var wet = new Color(Mat.Lin[Mat.Blood * 3] * .95f, Mat.Lin[Mat.Blood * 3 + 1] * .7f, Mat.Lin[Mat.Blood * 3 + 2] * .7f);
        var dry = new Color(wet.R * .42f, wet.G * .55f + .006f, wet.B * .5f + .004f);
        for (int i = 0; i < MaxStains; i++)
        {
            ref var s = ref _stains[i];
            if (!s.Live) continue;
            float age = _t - s.Born;
            if (age > Lasts) { s.Live = false; continue; }
            float size = s.Size * (s.Grow > 0 ? Math.Min(1, age / s.Grow) : 1) * Math.Min(1, (Lasts - age) / Fades);
            if (size <= .002f) continue;
            var c = wet.Lerp(dry, Math.Min(1, age / 70));
            var basis = new Basis(Vector3.Up, -s.Yaw) * Basis.FromScale(new Vector3(size * s.Long, .004f, size));
            Put(k++, basis, s.P, c);
        }
        Stains = k;
        for (int i = 0; i < _nd; i++)
        {
            ref var d = ref _drops[i];
            Put(k++, Basis.FromScale(new Vector3(d.Size, d.Size * 1.6f, d.Size)), d.P, wet);
        }
        if (k > 0) RenderingServer.MultimeshSetBuffer(Multimesh.GetRid(), _buf);
        Multimesh.VisibleInstanceCount = k;
    }

    void Put(int i, Basis b, Vector3 at, Color c)
    {
        int o = i * 16;
        _buf[o] = b.X.X; _buf[o + 1] = b.Y.X; _buf[o + 2] = b.Z.X; _buf[o + 3] = at.X;
        _buf[o + 4] = b.X.Y; _buf[o + 5] = b.Y.Y; _buf[o + 6] = b.Z.Y; _buf[o + 7] = at.Y;
        _buf[o + 8] = b.X.Z; _buf[o + 9] = b.Y.Z; _buf[o + 10] = b.Z.Z; _buf[o + 11] = at.Z;
        _buf[o + 12] = c.R; _buf[o + 13] = c.G; _buf[o + 14] = c.B; _buf[o + 15] = 1;
    }
}
