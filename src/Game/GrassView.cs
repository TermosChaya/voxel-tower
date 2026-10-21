using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The grass finer than the voxels. A tuft of the meadow is no longer a green cube but a clump of long blades leaning
/// out every way, a flower a few stems with their heads; and when the view comes close — zoomed in, or through someone's
/// eyes — the open turf grows a short sward of blades as well, which gives way to the bare turf further off.
/// The world keeps its tufts and flowers as voxels (they burn, they are dug, a blast sears them away); the mesher leaves
/// them out of the chunk meshes and hands their places over (<see cref="Sward"/>), and this draws them, a MultiMesh for
/// each patch of the valley. Every blade bends in the wind (harder in a storm), away from whoever walks through it, before
/// a blast's wave and round a whirlwind: see shaders/grass.gdshader.
/// A child of the <see cref="WorldView"/>, so its instances stand in voxels as the chunks do.
/// </summary>
public partial class GrassView : Node3D
{
    /// <summary>Tufts and flowers are kept in patches of 4×4 chunk columns, the sward in tiles of 2×2.</summary>
    const int PatchChunks = 4, TileChunks = 2;
    /// <summary>Floats an instance takes in a buffer: its transform, its colour, its custom data.</summary>
    const int Stride = 20;
    /// <summary>Pixels a voxel takes on screen between which the sward grows in, from nothing to full.</summary>
    const float SwardFrom = 9, SwardFull = 13;
    /// <summary>Through someone's eyes, how far the sward reaches in full, and where it is gone, in voxels.</summary>
    const float EyeNear = 72, EyeFar = 112;
    /// <summary>At most this many sward tiles built in one frame, the nearest first, so a turn of the head does not stall.</summary>
    const int TilesPerFrame = 6;

    VoxelWorld _w;
    Main _m;
    ShaderMaterial _mat;
    ArrayMesh _tuft, _flower, _blades, _fringe;
    int[][] _sprigs, _turf;
    int _cx, _cz, _cy;

    sealed class Patch
    {
        public MultiMeshInstance3D Mi;
        public bool Dirty;
        public bool On;
        public float Dist;
    }
    Patch[] _tufts, _flowers, _sward, _edges;
    int _pw, _ph, _tw, _th;
    readonly List<int> _want = new();
    float[] _buf = Array.Empty<float>();

    readonly Vector4[] _pushers = new Vector4[32], _blasts = new Vector4[8], _blastSize = new Vector4[8], _whirls = new Vector4[4];
    readonly List<(float x, float y, float z, float r, double at)> _booms = new();
    readonly List<(Unit u, float d)> _near = new();
    float _wind = 1, _gale;

    public void Init(VoxelWorld w)
    {
        _w = w;
        _cx = w.SX / VoxelWorld.CS; _cy = w.SY / VoxelWorld.CS; _cz = w.SZ / VoxelWorld.CS;
        _sprigs = new int[w.ChunkCount][];
        _turf = new int[w.ChunkCount][];
        _pw = (_cx + PatchChunks - 1) / PatchChunks; _ph = (_cz + PatchChunks - 1) / PatchChunks;
        _tw = (_cx + TileChunks - 1) / TileChunks; _th = (_cz + TileChunks - 1) / TileChunks;
        _tufts = NewPatches(_pw * _ph);
        _flowers = NewPatches(_pw * _ph);
        _sward = NewPatches(_tw * _th);
        _edges = NewPatches(_tw * _th);
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grass.gdshader") };
        _mat.SetShaderParameter("vox", VoxelWorld.Vox);
        _mat.SetShaderParameter("origin", new Vector3(w.OriginX, 0, w.OriginZ));
        _tuft = Tuft();
        _flower = Flowers();
        _blades = Blades();
        _fringe = Fringe();
    }

    static Patch[] NewPatches(int n)
    {
        var a = new Patch[n];
        for (int i = 0; i < n; i++) a[i] = new Patch();
        return a;
    }

    /// <summary>The game to read the camera, the eyes and the sim from; and the blasts it hears of.</summary>
    public void Bind(Main m)
    {
        _m = m;
        m.Sim.Exploded += Boom;
        m.Sim.Quaked += Quake;
        m.Sim.Spelled += Spell;
    }

    public override void _ExitTree()
    {
        if (_m == null) return;
        _m.Sim.Exploded -= Boom;
        _m.Sim.Quaked -= Quake;
        _m.Sim.Spelled -= Spell;
    }

    /// <summary>What grows on a chunk, fresh from the mesher.</summary>
    public void Take(int ci, Sward s)
    {
        int[] sp = s?.Sprigs, tf = s?.Turf;
        if (sp == null && _sprigs[ci] == null && tf == null && _turf[ci] == null) return;
        bool sprigsChanged = !Same(sp, _sprigs[ci]), turfChanged = !Same(tf, _turf[ci]);
        _sprigs[ci] = sp;
        _turf[ci] = tf;
        _w.ChunkCoords(ci, out int cx, out _, out int cz);
        if (sprigsChanged)
        {
            int p = cz / PatchChunks * _pw + cx / PatchChunks;
            _tufts[p].Dirty = _flowers[p].Dirty = true;
        }
        if (turfChanged) _sward[cz / TileChunks * _tw + cx / TileChunks].Dirty = true;
    }

    static bool Same(int[] a, int[] b)
    {
        if (a == b) return true;
        if (a == null || b == null || a.Length != b.Length) return false;
        return a.AsSpan().SequenceEqual(b);
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("GrassView");
        if (_m == null) return;
        float dt = (float)delta;
        for (int p = 0; p < _tufts.Length; p++)
            if (_tufts[p].Dirty)
            {
                _tufts[p].Dirty = _flowers[p].Dirty = false;
                Sprigs(p);
            }
        GrowSward(dt);
        Push(dt);
    }

    // ------------------------------------------------------------------ tufts and flowers

    /// <summary>Lays out the tufts and the flowers of one patch afresh.</summary>
    void Sprigs(int p)
    {
        int px = p % _pw, pz = p / _pw;
        int nT = 0, nF = 0;
        ForChunks(px, pz, PatchChunks, ci =>
        {
            if (_sprigs[ci] == null) return;
            foreach (int e in _sprigs[ci]) if (Sward.Mat(e) == Mat.Tuft) nT++; else nF++;
        });
        Fill(_tufts[p], _tuft, nT, px, pz, PatchChunks, (buf, aabb) =>
        {
            int n = 0;
            ForChunks(px, pz, PatchChunks, ci =>
            {
                if (_sprigs[ci] == null) return;
                _w.ChunkCoords(ci, out int cx, out int cy, out int cz);
                foreach (int e in _sprigs[ci])
                {
                    if (Sward.Mat(e) != Mat.Tuft) continue;
                    int x = cx * VoxelWorld.CS + Sward.X(e), y = cy * VoxelWorld.CS + Sward.Y(e), z = cz * VoxelWorld.CS + Sward.Z(e);
                    double h = Hash.H3(x, y, z), h2 = Hash.H3(z, x, y + 7);
                    byte under = _w.Get(x, y - 1, z);
                    float dry = under == Mat.GrassDry ? .7f : under == Mat.GrassLush ? .05f : .3f;
                    // a tuft of the meadow stands a foot and more, knee-high where it grows two voxels of it
                    float tall = Sward.Tall(e) * (1.6f + (float)h2 * .5f);
                    Put(buf, n++, x + .5f + (float)(h - .5) * .3f, y, z + .5f + (float)(h2 - .5) * .3f, (float)(h * Math.Tau), .9f + (float)h * .25f, tall,
                        Mat.Tuft, dry, 0, (float)h2);
                    aabb.Add(y);
                }
            });
            return n;
        });
        Fill(_flowers[p], _flower, nF, px, pz, PatchChunks, (buf, aabb) =>
        {
            int n = 0;
            ForChunks(px, pz, PatchChunks, ci =>
            {
                if (_sprigs[ci] == null) return;
                _w.ChunkCoords(ci, out int cx, out int cy, out int cz);
                foreach (int e in _sprigs[ci])
                {
                    byte m = Sward.Mat(e);
                    if (m == Mat.Tuft) continue;
                    int x = cx * VoxelWorld.CS + Sward.X(e), y = cy * VoxelWorld.CS + Sward.Y(e), z = cz * VoxelWorld.CS + Sward.Z(e);
                    double h = Hash.H3(x, y, z + 3), h2 = Hash.H3(z, x + 5, y);
                    float tall = (1.25f + (float)h2 * .45f) * Math.Max(1, Sward.Tall(e));
                    Put(buf, n++, x + .5f, y, z + .5f, (float)(h * Math.Tau), .9f + (float)h * .3f, tall, Mat.Tuft, .1f, m == Mat.Flower ? 1 : 2, (float)h2);
                    aabb.Add(y);
                }
            });
            return n;
        });
    }

    // ------------------------------------------------------------------ the sward

    /// <summary>
    /// Which tiles of the valley grow a sward now: through someone's eyes, those round the eye; through the trailer's lens,
    /// those round what it looks at, once that is near enough for blades to be seen; from above, those the view sees once
    /// it is close enough, each tile tried against the camera's frustum between the lowest and the highest of its turf.
    /// Tiles that fall out of it are let go.
    /// </summary>
    void GrowSward(float dt)
    {
        var fp = _m.FP;
        var cine = _m.Cine;
        bool eyes = fp != null && fp.On, lens = !eyes && cine != null && cine.On, round = eyes || lens;
        float pxPerVox = GetTree().Root.Size.Y * Settings.Details[Settings.Detail] / Math.Max(.01f, _m.Rig.Cam.Size) * VoxelWorld.Vox;
        // (the grass as the player set it: none of the blades, only close to, or all)
        float lush = Settings.Grass == 1 ? 1.7f : 1, reach = Settings.Grass == 1 ? .6f : 1;
        float grow = Settings.Grass == 0 ? 0 : eyes ? 1 : lens ? cine.Grow : Mathf.SmoothStep(SwardFrom * lush, SwardFull * lush, pxPerVox);
        _mat.SetShaderParameter("keep", round ? 1f : .6f);
        // the ground in the middle of the view, which the grass parted nearest it is counted from
        {
            var t = _m.Rig.Target;
            int tx = Math.Clamp((int)((t.X - _w.OriginX) / VoxelWorld.Vox), 0, _w.SX - 1), tz = Math.Clamp((int)((t.Y - _w.OriginZ) / VoxelWorld.Vox), 0, _w.SZ - 1);
            var foot = _m.Rig.GroundFootprint(_w.WY(_w.SurfAt(tx, tz)));
            var mid = (foot[0] + foot[1] + foot[2] + foot[3]) * .25f;
            _centre = new Vector2((mid.X - _w.OriginX) / VoxelWorld.Vox, (mid.Z - _w.OriginZ) / VoxelWorld.Vox);
        }
        Vector3 focus;
        float near, far;
        Godot.Collections.Array<Plane> frustum = null;
        if (round)
        {
            focus = eyes ? fp.Cam.GlobalPosition : cine.Focus;
            near = EyeNear * reach * VoxelWorld.Vox;
            far = EyeFar * reach * VoxelWorld.Vox;
        }
        else
        {
            focus = new Vector3(_w.WX(_centre.X), 0, _w.WZ(_centre.Y));
            near = 1e6f;
            far = 2e6f;
            if (grow > 0) frustum = _m.Rig.Cam.GetFrustum();
        }
        RenderingServer.GlobalShaderParameterSet("sward_at", new Vector4(focus.X, focus.Y, focus.Z, eyes || grow > 0 ? grow : 0));
        RenderingServer.GlobalShaderParameterSet("sward_reach", new Vector2(near, far));

        int span = TileChunks * VoxelWorld.CS;
        float ex = (focus.X - _w.OriginX) / VoxelWorld.Vox, ez = (focus.Z - _w.OriginZ) / VoxelWorld.Vox;
        _want.Clear();
        for (int i = 0; i < _sward.Length; i++)
        {
            int tx = i % _tw, tz = i / _tw;
            float ax = tx * span, az = tz * span;
            var t = _sward[i];
            bool on;
            float ef = EyeFar * reach;
            if (round) on = grow > 0 && ax + span >= ex - ef && ax <= ex + ef && az + span >= ez - ef && az <= ez + ef;
            else if (frustum == null) on = false;
            else
            {
                var (lo, hi) = TileHeights(i);
                on = hi >= lo && Seen(frustum, new Vector3(_w.WX(ax), _w.WY(lo), _w.WZ(az)), new Vector3(_w.WX(ax + span), _w.WY(hi + 3), _w.WZ(az + span)));
            }
            if (!on)
            {
                if (t.Mi != null) { t.Mi.QueueFree(); t.Mi = null; }
                if (_edges[i].Mi != null) { _edges[i].Mi.QueueFree(); _edges[i].Mi = null; }
                t.On = false;
                continue;
            }
            if (!t.On || t.Dirty)
            {
                t.Dist = MathF.Abs(ax + span * .5f - (round ? ex : _centre.X)) + MathF.Abs(az + span * .5f - (round ? ez : _centre.Y));
                _want.Add(i);
            }
        }
        _want.Sort((a, b) => _sward[a].Dist.CompareTo(_sward[b].Dist));
        for (int k = 0; k < Math.Min(TilesPerFrame, _want.Count); k++)
        {
            int i = _want[k];
            var t = _sward[i];
            t.On = true;
            t.Dirty = false;
            Turf(i);
        }
    }

    /// <summary>The ground in the middle of the view, in voxels (x, z).</summary>
    Vector2 _centre;

    /// <summary>The lowest and the highest open turf of a tile, in voxels (lo > hi when it has none).</summary>
    (int lo, int hi) TileHeights(int i)
    {
        int lo = int.MaxValue, hi = int.MinValue;
        ForChunks(i % _tw, i / _tw, TileChunks, ci =>
        {
            var tf = _turf[ci];
            if (tf == null) return;
            int y0 = ci / (_cx * _cz) * VoxelWorld.CS;
            foreach (int e in tf) { int y = y0 + Sward.Y(e) + 1; if (y < lo) lo = y; if (y > hi) hi = y; }
        });
        return (lo, hi);
    }

    /// <summary>Whether a box (world) is at least partly inside the frustum: on the inner side of every one of its planes.</summary>
    static bool Seen(Godot.Collections.Array<Plane> frustum, Vector3 a, Vector3 b)
    {
        foreach (var pl in frustum)
        {
            // the corner of the box furthest in against the plane's normal: if even it is over the plane, all of it is
            var c = new Vector3(pl.Normal.X > 0 ? a.X : b.X, pl.Normal.Y > 0 ? a.Y : b.Y, pl.Normal.Z > 0 ? a.Z : b.Z);
            if (pl.IsPointOver(c)) return false;
        }
        return true;
    }

    /// <summary>Grows the sward of one tile afresh: a clump of short blades on every open top of its turf.</summary>
    void Turf(int i)
    {
        int tx = i % _tw, tz = i / _tw, n0 = 0;
        ForChunks(tx, tz, TileChunks, ci => n0 += _turf[ci]?.Length ?? 0);
        Fill(_sward[i], _blades, n0, tx, tz, TileChunks, (buf, aabb) =>
        {
            int n = 0;
            ForChunks(tx, tz, TileChunks, ci =>
            {
                if (_turf[ci] == null) return;
                _w.ChunkCoords(ci, out int cx, out int cy, out int cz);
                foreach (int e in _turf[ci])
                {
                    byte m = Sward.Mat(e);
                    int x = cx * VoxelWorld.CS + Sward.X(e), y = cy * VoxelWorld.CS + Sward.Y(e), z = cz * VoxelWorld.CS + Sward.Z(e);
                    double h = Hash.H3(x, y + 11, z), h2 = Hash.H3(z + 13, x, y);
                    float dry = m == Mat.GrassDry ? .75f : m == Mat.GrassLush ? .05f : .3f;
                    float tall = (m == Mat.GrassLush ? 1.2f : m == Mat.GrassDry ? .75f : .95f) * (.8f + (float)h2 * .4f);
                    Put(buf, n++, x + .5f, y + 1, z + .5f, (float)(h * Math.Tau), 1, tall, m, dry, 3, (float)h);
                    aabb.Add(y + 1);
                }
            });
            return n;
        });
        // and over every open edge of it, the blades hanging down the side
        int nE = 0;
        ForChunks(tx, tz, TileChunks, ci => { if (_turf[ci] != null) foreach (int e in _turf[ci]) nE += BitCount(Sward.Sides(e)); });
        Fill(_edges[i], _fringe, nE, tx, tz, TileChunks, (buf, aabb) =>
        {
            int n = 0;
            ForChunks(tx, tz, TileChunks, ci =>
            {
                if (_turf[ci] == null) return;
                _w.ChunkCoords(ci, out int cx, out int cy, out int cz);
                foreach (int e in _turf[ci])
                {
                    int sides = Sward.Sides(e);
                    if (sides == 0) continue;
                    byte m = Sward.Mat(e);
                    int x = cx * VoxelWorld.CS + Sward.X(e), y = cy * VoxelWorld.CS + Sward.Y(e), z = cz * VoxelWorld.CS + Sward.Z(e);
                    float dry = m == Mat.GrassDry ? .75f : m == Mat.GrassLush ? .05f : .3f;
                    for (int k = 0; k < 4; k++)
                    {
                        if ((sides >> k & 1) == 0) continue;
                        // +x, -x, +z, -z: the fringe's mesh hangs toward +z, turned to face the open side
                        float yaw = k switch { 0 => MathF.PI / 2, 1 => -MathF.PI / 2, 2 => 0, _ => MathF.PI };
                        double h = Hash.H3(x + k * 7, y, z);
                        Put(buf, n++, x + .5f, y + 1, z + .5f, yaw, 1, .85f + (float)h * .4f, m, dry, 3, (float)h);
                        aabb.Add(y - 2);
                    }
                }
            });
            return n;
        });
    }

    static int BitCount(int v) => System.Numerics.BitOperations.PopCount((uint)v);

    // ------------------------------------------------------------------ the buffers

    sealed class Span1
    {
        public int Lo = int.MaxValue, Hi = int.MinValue;
        public void Add(int y) { if (y < Lo) Lo = y; if (y > Hi) Hi = y; }
    }

    void ForChunks(int px, int pz, int size, Action<int> each)
    {
        for (int cz = pz * size; cz < Math.Min(_cz, pz * size + size); cz++)
            for (int cx = px * size; cx < Math.Min(_cx, px * size + size); cx++)
                for (int cy = 0; cy < _cy; cy++)
                    each(_w.ChunkIndex(cx, cy, cz));
    }

    /// <summary>Writes n instances into a patch's MultiMesh through <paramref name="write"/>, making or dropping it as needed.</summary>
    void Fill(Patch p, Mesh mesh, int n, int px, int pz, int size, Func<float[], Span1, int> write)
    {
        if (n == 0)
        {
            if (p.Mi != null) { p.Mi.QueueFree(); p.Mi = null; }
            return;
        }
        if (_buf.Length < n * Stride) _buf = new float[Math.Max(n * Stride, _buf.Length * 2)];
        var ys = new Span1();
        n = write(_buf, ys);
        if (p.Mi == null)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh };
            p.Mi = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(p.Mi);
        }
        var multi = p.Mi.Multimesh;
        multi.InstanceCount = n;
        var data = n * Stride == _buf.Length ? _buf : _buf.AsSpan(0, n * Stride).ToArray();
        RenderingServer.MultimeshSetBuffer(multi.GetRid(), data);
        int side = size * VoxelWorld.CS;
        p.Mi.CustomAabb = new Aabb(new Vector3(px * side - 2, ys.Lo - 1, pz * side - 2), new Vector3(side + 4, ys.Hi - ys.Lo + 6, side + 4));
    }

    /// <summary>One instance: where it stands, its turn and size, the colour of what it is, how dry, its kind and its own random.</summary>
    static void Put(float[] b, int i, float x, float y, float z, float yaw, float wide, float tall, byte colourOf, float dry, int kind, float own)
    {
        int o = i * Stride;
        float c = MathF.Cos(yaw) * wide, s = MathF.Sin(yaw) * wide;
        b[o] = c; b[o + 1] = 0; b[o + 2] = s; b[o + 3] = x;
        b[o + 4] = 0; b[o + 5] = tall; b[o + 6] = 0; b[o + 7] = y;
        b[o + 8] = -s; b[o + 9] = 0; b[o + 10] = c; b[o + 11] = z;
        b[o + 12] = Mat.Lin[colourOf * 3]; b[o + 13] = Mat.Lin[colourOf * 3 + 1]; b[o + 14] = Mat.Lin[colourOf * 3 + 2]; b[o + 15] = dry;
        b[o + 16] = kind; b[o + 17] = own; b[o + 18] = 0; b[o + 19] = 0;
    }

    // ------------------------------------------------------------------ what bends the grass

    void Boom(float x, float y, float z, float r) => _booms.Add((x, y, z, r, _m.Sim.Time));

    void Quake(float x, float z, float r) => _booms.Add((x, _w.SurfAt(Math.Clamp((int)x, 0, _w.SX - 1), Math.Clamp((int)z, 0, _w.SZ - 1)), z, -r, _m.Sim.Time));

    void Spell(SpellKind k, float x, float y, float z)
    {
        // the black wind is one great gust over the valley
        if (k == SpellKind.BlackWind) _gale = 3;
    }

    /// <summary>
    /// Tells the grass what bends it this frame: the wind's strength by the weather, whoever stands nearest the view,
    /// the blasts of the last few seconds (a quake as a blast that shakes rather than throws) and the whirlwinds.
    /// </summary>
    void Push(float dt)
    {
        var sim = _m.Sim;
        float aim = sim.Weather switch { Weather.Wind => 2.3f, Weather.Rain => 1.45f, Weather.Fog => .45f, _ => 1 };
        _wind += (aim - _wind) * (1 - MathF.Exp(-dt * .5f));
        _gale = Math.Max(0, _gale - dt * .6f);
        RenderingServer.GlobalShaderParameterSet("wind", _wind + _gale);

        var fp = _m.FP;
        float ax = _centre.X, az = _centre.Y;
        if (fp != null && fp.On || _m.Cine is { On: true })
        {
            var at = fp != null && fp.On ? fp.Cam.GlobalPosition : _m.Cine.Focus;
            ax = (at.X - _w.OriginX) / VoxelWorld.Vox;
            az = (at.Z - _w.OriginZ) / VoxelWorld.Vox;
        }
        _near.Clear();
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null) continue;
            float d = (u.X - ax) * (u.X - ax) + (u.Z - az) * (u.Z - az);
            if (d < 260 * 260) _near.Add((u, d));
        }
        _near.Sort((a, b) => a.d.CompareTo(b.d));
        int n = Math.Min(_pushers.Length, _near.Count);
        for (int i = 0; i < n; i++)
        {
            var u = _near[i].u;
            _pushers[i] = new Vector4(_w.WX(u.X), _w.WY(u.Y), _w.WZ(u.Z), (u.T.BodyR * 1.35f + 1.2f) * VoxelWorld.Vox);
        }
        _mat.SetShaderParameter("pushers", _pushers);
        _mat.SetShaderParameter("pusher_count", n);

        _booms.RemoveAll(b => sim.Time - b.at > 4 || sim.Time < b.at);
        int nb = 0;
        for (int i = _booms.Count - 1; i >= 0 && nb < _blasts.Length; i--, nb++)
        {
            var (x, y, z, r, t0) = _booms[i];
            bool quake = r < 0;
            float reach = MathF.Max(Math.Abs(r) * (quake ? 1 : 3.2f), 6) * VoxelWorld.Vox;
            _blasts[nb] = new Vector4(_w.WX(x), _w.WY(y), _w.WZ(z), (float)(sim.Time - t0));
            _blastSize[nb] = new Vector4(reach, quake ? .5f : Math.Clamp(Math.Abs(r) / 8f, .7f, 2f), quake ? 1 : 0, 0);
        }
        _mat.SetShaderParameter("blasts", _blasts);
        _mat.SetShaderParameter("blast_size", _blastSize);
        _mat.SetShaderParameter("blast_count", nb);

        int nw = 0;
        foreach (var wh in sim.Whirls)
        {
            if (nw >= _whirls.Length) break;
            float left = Math.Clamp((float)(wh.Until - sim.Time), 0, 1);
            if (left <= 0) continue;
            float lag = (float)(sim.Time - (wh.Next - .2));
            float x = wh.X + wh.VX * lag, z = wh.Z + wh.VZ * lag;
            int cx = Math.Clamp((int)x, 0, _w.SX - 1), cz = Math.Clamp((int)z, 0, _w.SZ - 1);
            _whirls[nw++] = new Vector4(_w.WX(x), _w.WY(_w.SurfAt(cx, cz)), _w.WZ(z), wh.R * left * VoxelWorld.Vox);
        }
        _mat.SetShaderParameter("whirls", _whirls);
        _mat.SetShaderParameter("whirl_count", nw);
    }

    // ------------------------------------------------------------------ the meshes

    /// <summary>Collects the blades, stems and petals of one clump into a mesh.</summary>
    sealed class Clump
    {
        readonly List<Vector3> _v = new(), _n = new();
        readonly List<Vector2> _uv = new(), _uv2 = new();
        readonly List<float> _off = new();
        readonly List<int> _i = new();
        float _id;
        Vector3 _root;
        public int Count;

        void Vert(Vector3 p, Vector3 off, Vector3 n, float up, float part)
        {
            _v.Add(p + off); _n.Add(n); _uv.Add(new Vector2(up, _id)); _uv2.Add(new Vector2(part, _root.Z));
            _off.Add(off.X); _off.Add(off.Y); _off.Add(off.Z); _off.Add(_root.X);
        }

        /// <summary>
        /// A blade from its root leaning out along the way it faces (az), curving over as it rises: <paramref name="len"/>
        /// long, <paramref name="half"/> wide either side at the root and narrowing to a point. Returns where its tip is.
        /// </summary>
        public Vector3 Blade(Vector3 root, float az, float lean, float len, float half, int segs, float id, float curl = 1)
        {
            _id = id;
            _root = root;
            Count++;
            var outw = new Vector3(MathF.Cos(az), 0, MathF.Sin(az));
            var across = new Vector3(-outw.Z, 0, outw.X);
            int b = _v.Count;
            var p = root;
            for (int s = 0; s <= segs; s++)
            {
                float t = s / (float)segs, a = lean * (.25f + .75f * t * curl);
                var along = outw * MathF.Sin(a) + Vector3.Up * MathF.Cos(a);
                var n = along.Cross(across).Normalized();
                if (s < segs)
                {
                    float w = half * (1 - .75f * t);
                    Vert(p, -across * w, n, t, 0);
                    Vert(p, across * w, n, t, 0);
                }
                else Vert(p, Vector3.Zero, n, 1, 0);
                if (s < segs)
                {
                    float an = lean * (.25f + .75f * (s + .5f) / segs * curl);
                    p += (outw * MathF.Sin(an) + Vector3.Up * MathF.Cos(an)) * (len / segs);
                }
            }
            for (int s = 0; s < segs - 1; s++)
            {
                int q = b + s * 2;
                _i.Add(q); _i.Add(q + 1); _i.Add(q + 3);
                _i.Add(q); _i.Add(q + 3); _i.Add(q + 2);
            }
            int l = b + (segs - 1) * 2;
            _i.Add(l); _i.Add(l + 1); _i.Add(l + 2);
            return p;
        }

        /// <summary>A flower's head on its stem's tip: five petals round a heart, turned a little up.</summary>
        public void Head(Vector3 at, float r, float tilt, float id)
        {
            _id = id;
            int petals = 5;
            for (int k = 0; k < petals; k++)
            {
                float a = k * MathF.Tau / petals + tilt;
                var d = new Vector3(MathF.Cos(a), .35f, MathF.Sin(a)).Normalized();
                var side = new Vector3(-MathF.Sin(a), 0, MathF.Cos(a));
                int b = _v.Count;
                Vert(at, side * r * .08f, Vector3.Up, 1, 1);
                Vert(at, d * r * .55f + side * r * .38f, Vector3.Up, 1, 1);
                Vert(at, d * r, Vector3.Up, 1, 1);
                Vert(at, d * r * .55f - side * r * .38f, Vector3.Up, 1, 1);
                _i.Add(b); _i.Add(b + 1); _i.Add(b + 2);
                _i.Add(b); _i.Add(b + 2); _i.Add(b + 3);
            }
            int h = _v.Count;
            Vert(at, new Vector3(0, r * .22f, 0), Vector3.Up, 1, 2);
            for (int k = 0; k < 4; k++)
            {
                float a = k * MathF.Tau / 4 + tilt;
                Vert(at, new Vector3(MathF.Cos(a) * r * .28f, r * .05f, MathF.Sin(a) * r * .28f), Vector3.Up, 1, 2);
            }
            for (int k = 0; k < 4; k++) { _i.Add(h); _i.Add(h + 1 + k); _i.Add(h + 1 + (k + 1) % 4); }
        }

        public ArrayMesh Mesh()
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = _v.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Normal] = _n.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = _uv.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.TexUV2] = _uv2.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Custom0] = _off.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Index] = _i.ToArray();
            var mesh = new ArrayMesh();
            var flags = (Godot.Mesh.ArrayFormat)((int)Godot.Mesh.ArrayCustomFormat.RgbaFloat << (int)Godot.Mesh.ArrayFormat.FormatCustom0Shift);
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays, null, null, flags);
            return mesh;
        }
    }

    /// <summary>
    /// A tuft, a voxel tall before its instance stretches it: eleven long blades, a centimetre wide, from roots
    /// gathered in the middle of the voxel and leaning out every way, the outer ones furthest.
    /// </summary>
    static ArrayMesh Tuft()
    {
        var c = new Clump();
        var r = new Random(18);
        const int n = 11;
        for (int k = 0; k < n; k++)
        {
            float az = k * 2.39996f + (float)r.NextDouble() * .6f, out1 = (float)Math.Sqrt(r.NextDouble()) * .22f;
            var root = new Vector3(MathF.Cos(az) * out1, 0, MathF.Sin(az) * out1);
            float lean = .12f + out1 * 1.6f + (float)r.NextDouble() * .3f, len = .7f + (float)r.NextDouble() * .38f;
            c.Blade(root, az, lean, len, .05f, 3, (k + .5f) / n, 1.2f);
        }
        return c.Mesh();
    }

    /// <summary>Flowers: three stems with their heads at different heights over a few leaves, a voxel tall in all.</summary>
    static ArrayMesh Flowers()
    {
        var c = new Clump();
        var r = new Random(56);
        for (int k = 0; k < 5; k++)
        {
            float az = k * 1.3f + (float)r.NextDouble();
            c.Blade(new Vector3(MathF.Cos(az) * .08f, 0, MathF.Sin(az) * .08f), az, .5f + (float)r.NextDouble() * .4f, .38f + (float)r.NextDouble() * .2f, .05f, 2, (k + .5f) / 8);
        }
        for (int k = 0; k < 3; k++)
        {
            float az = k * 2.1f + (float)r.NextDouble() * .7f, off = .1f + (float)r.NextDouble() * .12f;
            var root = new Vector3(MathF.Cos(az) * off, 0, MathF.Sin(az) * off);
            var tip = c.Blade(root, az, .12f + (float)r.NextDouble() * .2f, .72f + k * .14f, .016f, 3, (5 + k + .5f) / 8, .6f);
            c.Head(tip, .17f + (float)r.NextDouble() * .05f, (float)r.NextDouble() * 6, (5 + k + .5f) / 8);
        }
        return c.Mesh();
    }

    /// <summary>
    /// The sward on a voxel of turf: a dozen blades, ten to twenty-five centimetres, a meadow's and not a lawn's, rooted
    /// all over its top in a jittered grid so none bunch up, leaning every way.
    /// </summary>
    static ArrayMesh Blades()
    {
        var c = new Clump();
        var r = new Random(3);
        const int n = 12;
        for (int k = 0; k < n; k++)
        {
            float gx = (k % 4 + (float)r.NextDouble()) / 4 - .5f, gz = (k / 4 + (float)r.NextDouble()) / 3 - .5f;
            var root = new Vector3(gx, 0, gz) * .96f;
            float az = (float)(r.NextDouble() * Math.Tau), lean = .15f + (float)r.NextDouble() * .55f, len = .85f + (float)r.NextDouble() * 1.15f;
            // which blades go first when the clump thins is shuffled, so what is left is still spread over the voxel
            c.Blade(root, az, lean, len, .04f, 3, (k * 7 % n + .5f) / n, 1.3f);
        }
        return c.Mesh();
    }

    /// <summary>
    /// The blades over an open edge of turf: seven rooted along the edge, leaning out over it and curving down the side,
    /// so a bank is fringed with grass hanging over it rather than cut off square.
    /// </summary>
    static ArrayMesh Fringe()
    {
        var c = new Clump();
        var r = new Random(23);
        const int n = 7;
        for (int k = 0; k < n; k++)
        {
            var root = new Vector3((k + (float)r.NextDouble()) / n - .5f, 0, .38f + (float)r.NextDouble() * .1f);
            float az = MathF.PI / 2 + ((float)r.NextDouble() - .5f) * .9f, lean = 1.1f + (float)r.NextDouble() * .5f, len = .8f + (float)r.NextDouble() * .7f;
            c.Blade(root, az, lean, len, .04f, 3, (k * 3 % n + .5f) / n, 1.5f);
        }
        return c.Mesh();
    }

    /// <summary>Tufts, flowers and sward instances drawn now, for the self-test and the shots.</summary>
    public (int tufts, int flowers, int sward) Drawn()
    {
        int t = 0, f = 0, s = 0;
        foreach (var p in _tufts) t += p.Mi?.Multimesh.InstanceCount ?? 0;
        foreach (var p in _flowers) f += p.Mi?.Multimesh.InstanceCount ?? 0;
        foreach (var p in _sward) s += p.Mi?.Multimesh.InstanceCount ?? 0;
        return (t, f, s);
    }
}
