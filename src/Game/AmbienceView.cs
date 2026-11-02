using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The small life about the view drawn (src/Core/Ambience.cs): sparrows, pigeons and crows, bats, butterflies and bees,
/// fireflies, jumping fish and midges, each kind a MultiMesh of a few boxes whose wings beat on their hinges
/// (shaders/critters.gdshader; the fireflies' light, shaders/glow_motes.gdshader). It goes on in the game's time: still
/// when the game stands, quicker when it runs quicker. A child of the <see cref="WorldView"/>, so it stands in voxels as the
/// chunks do. It hears what startles the birds: a blast, stone coming down, a heavy fall, a quake, lightning, a god's step.
/// </summary>
public partial class AmbienceView : Node3D
{
    const int Stride = 20;
    const float Pm = VoxelWorld.PerMetre;

    Main _m;
    VoxelWorld _w;
    public Ambience Life { get; private set; }
    ShaderMaterial _mat, _glow;
    readonly Dictionary<Critter, (MultiMeshInstance3D mi, int cap)> _drawn = new();
    readonly Dictionary<Critter, ArrayMesh> _mesh = new();
    float[] _buf = Array.Empty<float>();
    double _was = double.NaN;

    /// <summary>How many of each kind were drawn last frame (the self-test asks).</summary>
    public readonly Dictionary<Critter, int> Shown = new();

    public void Init(Main m)
    {
        _m = m;
        _w = m.World;
        Life = new Ambience(m.Sim, 11 + m.Site.KeepX * 7 + m.Site.KeepZ);
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/critters.gdshader") };
        _glow = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/glow_motes.gdshader") };
        _mesh[Critter.Sparrow] = _mesh[Critter.Pigeon] = _mesh[Critter.Crow] = Bird();
        _mesh[Critter.Bat] = Bat();
        _mesh[Critter.Butterfly] = Butterfly();
        _mesh[Critter.Bee] = Bee();
        _mesh[Critter.Fish] = Fish();
        _mesh[Critter.Midge] = _mesh[Critter.Firefly] = Cube();
        var s = m.Sim;
        s.Exploded += Boom;
        s.Collapsed += Fell;
        s.Landed += Landed;
        s.Quaked += Quake;
        s.Struck += Bolt;
        s.GodShook += Step;
        s.Heaved += Heave;
        m.Sound?.HookWildlife(Life);
    }

    public override void _ExitTree()
    {
        if (_m == null) return;
        var s = _m.Sim;
        s.Exploded -= Boom;
        s.Collapsed -= Fell;
        s.Landed -= Landed;
        s.Quaked -= Quake;
        s.Struck -= Bolt;
        s.GodShook -= Step;
        s.Heaved -= Heave;
    }

    void Boom(float x, float y, float z, float r) => Life.Startle(x, y, z, r);
    void Fell(int n, float x, float y, float z) { if (n >= 12) Life.Startle(x, y, z, MathF.Min(40, 2 + MathF.Sqrt(n))); }
    void Landed(float x, float y, float z, int n, float speed) { if (n >= 20) Life.Startle(x, y, z, MathF.Min(40, 2 + MathF.Sqrt(n))); }
    void Quake(float x, float z, float r) => Life.Startle(x, _w.SurfAt(Math.Clamp((int)x, 0, _w.SX - 1), Math.Clamp((int)z, 0, _w.SZ - 1)), z, r);
    void Bolt(float x, float y, float z) => Life.Startle(x, y, z, 30 * Pm);
    void Step(Deity d, float x, float y, float z, float r) => Life.Startle(x, y, z, r);
    void Heave(float x, float y, float z, int n) => Life.Startle(x, y, z, 20 * Pm);

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("AmbienceView");
        if (_m?.Sim == null) return;
        // the game's own time: nothing moves while it stands
        double now = _m.Sim.Time;
        float dt = double.IsNaN(_was) ? 0 : (float)(now - _was);
        _was = now;
        if (dt < 0 || dt > .5f) dt = 0;
        var (fx, fy, fz, reach, small) = Focus();
        if (dt > 0) Life.Tick(dt, fx, fy, fz, reach, small);
        Draw(fx, fz, reach);
    }

    /// <summary>So many seconds of the small life about the view now, all at once (for the scenes staged: the birds in and settled).</summary>
    public void Warm(double seconds)
    {
        var (fx, fy, fz, reach, small) = Focus();
        for (double t = 0; t < seconds; t += 1 / 30.0) Life.Tick(1 / 30f, fx, fy, fz, reach, small);
    }

    /// <summary>About the eyes, or the ground in the middle of the view; how far round; the insects only near enough to be seen.</summary>
    (float x, float y, float z, float reach, bool small) Focus()
    {
        var fp = _m.FP;
        bool eyes = fp != null && fp.On;
        float fx, fy, fz, reach;
        bool small;
        if (eyes)
        {
            var p = fp.Cam.GlobalPosition;
            fx = (p.X - _w.OriginX) / VoxelWorld.Vox; fy = p.Y / VoxelWorld.Vox; fz = (p.Z - _w.OriginZ) / VoxelWorld.Vox;
            reach = 45 * Pm;
            small = true;
        }
        else
        {
            var t = _m.Rig.Target;
            int tx = Math.Clamp((int)((t.X - _w.OriginX) / VoxelWorld.Vox), 0, _w.SX - 1), tz = Math.Clamp((int)((t.Y - _w.OriginZ) / VoxelWorld.Vox), 0, _w.SZ - 1);
            fx = tx; fz = tz; fy = _w.SurfAt(tx, tz);
            float size = Math.Max(.01f, _m.Rig.Cam.Size);
            float pxPerVox = GetTree().Root.Size.Y / size * VoxelWorld.Vox;
            reach = Math.Clamp(size / VoxelWorld.Vox * .6f, 30 * Pm, 75 * Pm);
            small = pxPerVox >= 5;
        }
        return (fx, fy, fz, reach, small);
    }

    // ------------------------------------------------------------ drawing

    /// <summary>Each kind's colours (sRGB): the body, and the accent as a share of it (below 1 darker, above lighter).</summary>
    static (Color c, float acc) Paint(Critter k, byte tint) => k switch
    {
        Critter.Sparrow => tint == 0 ? (new Color(.47f, .33f, .2f), 1.7f) : (new Color(.42f, .38f, .32f), 1.6f),
        Critter.Pigeon => tint switch { 0 => (new Color(.55f, .58f, .64f), .75f), 1 => (new Color(.86f, .86f, .84f), .9f), _ => (new Color(.36f, .37f, .4f), 1.3f) },
        Critter.Crow => (new Color(.09f, .09f, .11f), 1.6f),
        Critter.Bat => (new Color(.2f, .15f, .13f), .7f),
        Critter.Butterfly => tint switch
        {
            0 => (new Color(.95f, .95f, .9f), .25f),
            1 => (new Color(.98f, .85f, .28f), .3f),
            2 => (new Color(.95f, .52f, .15f), .15f),
            _ => (new Color(.38f, .58f, .95f), .3f),
        },
        Critter.Bee => (new Color(.95f, .72f, .14f), .08f),
        Critter.Fish => tint == 0 ? (new Color(.7f, .73f, .75f), .55f) : (new Color(.6f, .55f, .38f), .6f),
        Critter.Firefly => (new Color(.75f, 1f, .35f), 1),
        Critter.Midge => (new Color(.16f, .16f, .14f), 1),
        _ => (new Color(1, 1, 1), 1),
    };

    /// <summary>How long a kind is drawn, voxels: a little over its size, so a bird reads from above.</summary>
    static float Size(Critter k) => k switch
    {
        Critter.Sparrow => 2.2f, Critter.Pigeon => 3.4f, Critter.Crow => 4.6f, Critter.Bat => 2.2f, Critter.Butterfly => 1.1f,
        Critter.Bee => .45f, Critter.Fish => 2.6f, Critter.Firefly => .28f, Critter.Midge => .14f, _ => 1,
    };

    readonly List<Mote>[] _byKind = new List<Mote>[10];

    void Draw(float fx, float fz, float reach)
    {
        for (int k = 0; k < _byKind.Length; k++) (_byKind[k] ??= new List<Mote>()).Clear();
        foreach (var m in Life.Motes) _byKind[(int)m.Kind].Add(m);
        for (int k = 0; k < _byKind.Length; k++)
        {
            var kind = (Critter)k;
            if (kind == Critter.Frog) continue;
            var list = _byKind[k];
            Shown[kind] = list.Count;
            if (!_drawn.TryGetValue(kind, out var d))
            {
                if (list.Count == 0) continue;
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = _mesh[kind] };
                var mi = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = kind == Critter.Firefly ? _glow : _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(mi);
                d = (mi, 0);
            }
            int cap = d.cap;
            if (list.Count > cap)
            {
                cap = Math.Max(16, list.Count * 2);
                d.mi.Multimesh.InstanceCount = cap;
            }
            _drawn[kind] = (d.mi, cap);
            if (cap == 0) continue;
            if (_buf.Length < cap * Stride) _buf = new float[cap * Stride];
            float size = Size(kind);
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                var (c, acc) = Paint(kind, m.Tint);
                float glow = kind == Critter.Firefly ? m.Glow : 1;
                bool bird = kind is Critter.Sparrow or Critter.Pigeon or Critter.Crow;
                Put(_buf, i, m.X, m.Y, m.Z, m.Yaw, m.Pitch, size, c.SrgbToLinear(), glow, m.Wing, bird && m.Perched ? 1 : 0, acc);
            }
            d.mi.Multimesh.VisibleInstanceCount = list.Count;
            RenderingServer.MultimeshSetBuffer(d.mi.Multimesh.GetRid(), cap * Stride == _buf.Length ? _buf : _buf.AsSpan(0, cap * Stride).ToArray());
            d.mi.CustomAabb = new Aabb(new Vector3(fx - reach * 2.5f, 0, fz - reach * 2.5f), new Vector3(reach * 5, _w.SY, reach * 5));
        }
    }

    /// <summary>One creature: where it is, turned by its heading and its pitch, so long; its colour, how bright; its wings.</summary>
    static void Put(float[] b, int i, float x, float y, float z, float yaw, float pitch, float s, Color c, float alpha, float wing, float fold, float acc)
    {
        int o = i * Stride;
        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw), cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        b[o] = cy * s; b[o + 1] = sy * sp * s; b[o + 2] = sy * cp * s; b[o + 3] = x;
        b[o + 4] = 0; b[o + 5] = cp * s; b[o + 6] = -sp * s; b[o + 7] = y;
        b[o + 8] = -sy * s; b[o + 9] = cy * sp * s; b[o + 10] = cy * cp * s; b[o + 11] = z;
        b[o + 12] = c.R; b[o + 13] = c.G; b[o + 14] = c.B; b[o + 15] = alpha;
        b[o + 16] = wing; b[o + 17] = fold; b[o + 18] = acc; b[o + 19] = 0;
    }

    // ------------------------------------------------------------ the creatures' boxes

    /// <summary>Boxes made into a mesh: each with which wing it is and its hinge, how light it is, how much accent it takes.</summary>
    sealed class Boxes
    {
        readonly List<Vector3> _v = new(), _n = new();
        readonly List<Vector2> _uv = new(), _uv2 = new();

        public Boxes Box(float x0, float y0, float z0, float x1, float y1, float z1, float shade = 1, float accent = 0, float side = 0, float hinge = 0)
        {
            var a = new Vector3(x0, y0, z0); var c = new Vector3(x1, y1, z1);
            void Face(Vector3 n, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
            {
                foreach (var p in new[] { p0, p1, p2, p0, p2, p3 })
                {
                    _v.Add(p); _n.Add(n);
                    _uv.Add(new Vector2(side, hinge)); _uv2.Add(new Vector2(shade, accent));
                }
            }
            Face(Vector3.Up, new(a.X, c.Y, a.Z), new(c.X, c.Y, a.Z), new(c.X, c.Y, c.Z), new(a.X, c.Y, c.Z));
            Face(Vector3.Down, new(a.X, a.Y, a.Z), new(a.X, a.Y, c.Z), new(c.X, a.Y, c.Z), new(c.X, a.Y, a.Z));
            Face(Vector3.Right, new(c.X, a.Y, a.Z), new(c.X, a.Y, c.Z), new(c.X, c.Y, c.Z), new(c.X, c.Y, a.Z));
            Face(Vector3.Left, new(a.X, a.Y, a.Z), new(a.X, c.Y, a.Z), new(a.X, c.Y, c.Z), new(a.X, a.Y, c.Z));
            Face(Vector3.Back, new(a.X, a.Y, c.Z), new(a.X, c.Y, c.Z), new(c.X, c.Y, c.Z), new(c.X, a.Y, c.Z));
            Face(Vector3.Forward, new(a.X, a.Y, a.Z), new(c.X, a.Y, a.Z), new(c.X, c.Y, a.Z), new(a.X, c.Y, a.Z));
            return this;
        }

        /// <summary>A pair of boxes either side of the middle, mirrored: the left (+x) given; a wing pair hinged at the body's side.</summary>
        public Boxes Pair(float x0, float y0, float z0, float x1, float y1, float z1, float shade = 1, float accent = 0, bool wing = false, float hinge = 0)
        {
            Box(x0, y0, z0, x1, y1, z1, shade, accent, wing ? 1 : 0, hinge);
            Box(-x1, y0, z0, -x0, y1, z1, shade, accent, wing ? -1 : 0, hinge);
            return this;
        }

        public ArrayMesh Mesh()
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = _v.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Normal] = _n.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = _uv.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.TexUV2] = _uv2.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }
    }

    /// <summary>A bird a body long: the body with a pale belly, the head, a dark beak, the tail, two short legs, the wings.</summary>
    static ArrayMesh Bird() => new Boxes()
        .Box(-.16f, .06f, -.32f, .16f, .27f, .24f)
        .Box(-.15f, .02f, -.22f, .15f, .1f, .2f, accent: 1)
        .Box(-.12f, .17f, .2f, .12f, .37f, .44f, accent: .35f)
        .Box(-.035f, .23f, .44f, .035f, .28f, .54f, shade: .4f)
        .Pair(.05f, .3f, .38f, .08f, .33f, .41f, shade: .1f)
        .Box(-.1f, .14f, -.6f, .1f, .19f, -.3f, shade: .8f)
        .Pair(.04f, -.08f, -.02f, .07f, .03f, .02f, shade: .3f)
        .Pair(.16f, .17f, -.22f, .66f, .2f, .18f, shade: .82f, wing: true, hinge: .16f)
        .Mesh();

    /// <summary>A bat: a small furred body, its head with two ears, broad thin wings.</summary>
    static ArrayMesh Bat() => new Boxes()
        .Box(-.12f, 0, -.2f, .12f, .18f, .2f)
        .Box(-.09f, .04f, .2f, .09f, .19f, .33f)
        .Pair(.03f, .19f, .24f, .07f, .27f, .28f)
        .Pair(.12f, .09f, -.24f, .5f, .11f, .16f, shade: .75f, wing: true, hinge: .12f)
        .Pair(.5f, .09f, -.18f, .95f, .11f, .1f, shade: .7f, wing: true, hinge: .12f)
        .Mesh();

    /// <summary>A butterfly: a thin dark body, the fore and hind wings either side, their tips in the accent.</summary>
    static ArrayMesh Butterfly() => new Boxes()
        .Box(-.03f, 0, -.18f, .03f, .05f, .2f, shade: .3f)
        .Pair(.03f, .03f, -.02f, .3f, .04f, .3f, wing: true, hinge: .03f)
        .Pair(.3f, .03f, .02f, .5f, .04f, .28f, accent: 1, wing: true, hinge: .03f)
        .Pair(.03f, .03f, -.3f, .34f, .04f, -.02f, wing: true, hinge: .03f)
        .Mesh();

    /// <summary>A bee: a round striped body, a dark head, two pale wings.</summary>
    static ArrayMesh Bee() => new Boxes()
        .Box(-.18f, 0, -.34f, .18f, .3f, -.18f, accent: 1)
        .Box(-.19f, 0, -.18f, .19f, .31f, -.02f)
        .Box(-.18f, 0, -.02f, .18f, .3f, .12f, accent: 1)
        .Box(-.16f, .02f, .12f, .16f, .28f, .26f)
        .Box(-.12f, .04f, .26f, .12f, .24f, .4f, shade: .25f)
        .Pair(.18f, .27f, -.14f, .5f, .29f, .14f, shade: 1.8f, wing: true, hinge: .18f)
        .Mesh();

    /// <summary>A fish: a slim body with a darker back, a fin on top, the tail.</summary>
    static ArrayMesh Fish() => new Boxes()
        .Box(-.08f, -.1f, -.45f, .08f, .08f, .42f)
        .Box(-.07f, .05f, -.4f, .07f, .13f, .36f, accent: 1)
        .Box(-.012f, .13f, -.12f, .012f, .2f, .1f, accent: 1)
        .Box(-.012f, -.14f, -.68f, .012f, .14f, -.45f, shade: .9f, accent: .5f)
        .Pair(.03f, .02f, .32f, .06f, .05f, .35f, shade: .1f)
        .Mesh();

    static ArrayMesh Cube() => new Boxes().Box(-.5f, -.5f, -.5f, .5f, .5f, .5f).Mesh();
}
