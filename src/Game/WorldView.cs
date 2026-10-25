using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// One MeshInstance3D per 32³ chunk, one more for the water in it, and one for its shadow: the chunk's solid voxels as
/// the fewest faces that hold their shape (GreedyMesher.MeshChunk), drawn into the sun's shadow only, so the shadow
/// pass draws a few large faces where the picture draws many. From far off (a voxel under 1,7 pixels on the screen) the
/// valley is drawn a level coarser instead: a mesh and a shadow for every block of 2×2×2 chunks, every 2×2×2 voxels one
/// cell (GreedyMesher.MeshCoarse), a quarter of the faces in an eighth of the draws; a block whose chunks changed is made
/// again while the coarse level is drawn, or when it comes on. Vertices are in voxel units; this node scales them
/// to metres and centres the field on the origin, so the shaders see whole-voxel coordinates.
///
/// The meshing is done on the worker threads, into buffers borrowed from a pool; only Godot's meshes are made here,
/// on the main thread (Godot's arrays and objects are never made or let go on a worker), compressed
/// (<see cref="GodotMesh"/>), and in play so many vertices of them a frame, the rest the next. At the start the main
/// thread makes the meshes while the workers are still meshing the rest of the valley.
/// </summary>
public partial class WorldView : Node3D
{
    VoxelWorld _w;
    ShaderMaterial _mat, _waterMat;
    MeshInstance3D[] _inst, _water, _shade;
    // the coarse level: a block of 2×2×2 chunks each, its picture and its shadow; the blocks to make again
    MeshInstance3D[] _far, _farShade;
    int _rx, _ry, _rz;
    readonly HashSet<int> _farDirty = new();
    // the chunks whose shadow is to be made again, a little after the chunk itself
    readonly HashSet<int> _shadeDirty = new();
    readonly List<int> _shadeBatch = new();
    readonly List<int> _farBatch = new();
    bool _coarse, _showSolid = true;
    /// <summary>Whether the valley is drawn at the coarse level now.</summary>
    public bool Coarse => _coarse;
    readonly List<int> _batch = new();
    /// <summary>
    /// Chunks changed in play are meshed off the main thread and put in when they are ready, a frame or two on; runs
    /// that take pictures or check themselves have them meshed and put in at once, so that what they look at is up to date.
    /// </summary>
    public bool Async;

    // what each worker thread meshes with
    sealed class Kit
    {
        public readonly GreedyMesher Mesher = new() { Pooled = true };
        public readonly byte[] Pad = new byte[34 * 34 * 34], Wet = new byte[34 * 34 * 34];
    }
    static readonly ThreadLocal<Kit> Kits = new(() => new Kit());

    /// <summary>What a chunk was meshed into, still the core's buffers: the picture, its water, its shadow (when asked for), the grass on it.</summary>
    struct Built
    {
        public MeshBuffers Solid, Water, Shadow;
        public Sward Sward;
    }

    /// <summary>Godot's mesh of the core's buffers, made here on the main thread, the borrowed buffers given back.</summary>
    static ArrayMesh ToGodot(MeshBuffers b)
    {
        var mesh = GodotMesh.From(b, compress: true);
        b?.Return();
        return mesh;
    }

    static int VerticesOf(MeshBuffers b) => b?.Vertices ?? 0;

    public double LastFullMeshMs { get; private set; }
    /// <summary>The tufts, the flowers and the sward, drawn in blades over the chunks.</summary>
    public GrassView Grass { get; private set; }

    public void Init(VoxelWorld w)
    {
        _w = w;
        Position = new Vector3(w.OriginX, 0, w.OriginZ);
        Scale = Vector3.One * VoxelWorld.Vox;
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
        _waterMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
        _inst = new MeshInstance3D[w.ChunkCount];
        _water = new MeshInstance3D[w.ChunkCount];
        _shade = new MeshInstance3D[w.ChunkCount];
        Grass = new GrassView();
        AddChild(Grass);
        Grass.Init(w);

        var sw = Stopwatch.StartNew();
        // the workers mesh the valley while the main thread makes Godot's meshes of what is done so far
        using (var done = new BlockingCollection<(int ci, Built b)>(256))
        {
            var meshing = Task.Run(() =>
            {
                try { Parallel.For(0, w.ChunkCount, ci => done.Add((ci, MakeChunk(ci, shadow: true)))); }
                finally { done.CompleteAdding(); }
            });
            foreach (var (ci, b) in done.GetConsumingEnumerable())
            {
                Put(_inst, ci, ToGodot(b.Solid), _mat, GeometryInstance3D.ShadowCastingSetting.Off, _showSolid && !_coarse);
                Put(_water, ci, ToGodot(b.Water), _waterMat, GeometryInstance3D.ShadowCastingSetting.Off, true);
                Put(_shade, ci, ToGodot(b.Shadow), null, GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, _showSolid && !_coarse);
                Grass.Take(ci, b.Sward);
            }
            meshing.Wait();
        }
        _batch.Clear();
        w.TakeDirty(_batch);
        _batch.Clear();
        _rx = (w.CX + 1) / 2; _ry = (w.CY + 1) / 2; _rz = (w.CZ + 1) / 2;
        _far = new MeshInstance3D[_rx * _ry * _rz];
        _farShade = new MeshInstance3D[_far.Length];
        // the coarse level is wanted only from far off: meshed off the main thread, made Godot's a few dozen blocks a frame after
        var all = new int[_far.Length];
        for (int i = 0; i < all.Length; i++) all[i] = i;
        _farMade = Task.Run(() => MakeFar(all));
        LastFullMeshMs = sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>A chunk meshed, on whatever thread asks.</summary>
    Built MakeChunk(int ci, bool shadow)
    {
        var k = Kits.Value;
        var b = new Built();
        if (shadow) b.Solid = k.Mesher.MeshChunk(_w, ci, k.Pad, k.Wet, out b.Water, out b.Sward, out b.Shadow);
        else b.Solid = k.Mesher.MeshChunk(_w, ci, k.Pad, k.Wet, out b.Water, out b.Sward);
        return b;
    }

    /// <summary>These chunks meshed for the picture, over all the cores when there are more than two.</summary>
    Built[] MakeChunks(int[] chunks)
    {
        var made = new Built[chunks.Length];
        if (chunks.Length <= 2) for (int i = 0; i < chunks.Length; i++) made[i] = MakeChunk(chunks[i], shadow: false);
        else Parallel.For(0, chunks.Length, i => made[i] = MakeChunk(chunks[i], shadow: false));
        return made;
    }

    /// <summary>The shadows of these chunks.</summary>
    MeshBuffers[] MakeShadows(int[] chunks)
    {
        var made = new MeshBuffers[chunks.Length];
        void One(int i) { var k = Kits.Value; made[i] = k.Mesher.MeshShadow(_w, chunks[i], k.Pad); }
        if (chunks.Length <= 2) for (int i = 0; i < chunks.Length; i++) One(i);
        else Parallel.For(0, chunks.Length, One);
        return made;
    }

    /// <summary>These blocks of the coarse level, their picture and their shadow.</summary>
    (MeshBuffers solid, MeshBuffers shadow)[] MakeFar(int[] blocks)
    {
        var made = new (MeshBuffers solid, MeshBuffers shadow)[blocks.Length];
        void One(int i)
        {
            int ri = blocks[i];
            var k = Kits.Value;
            made[i].solid = k.Mesher.MeshCoarse(_w, ri % _rx, ri / (_rx * _rz), ri / _rx % _rz, k.Pad, out made[i].shadow);
        }
        if (blocks.Length <= 2) for (int i = 0; i < blocks.Length; i++) One(i);
        else Parallel.For(0, blocks.Length, One);
        return made;
    }

    // the coarse level as the thread meshed it at the start, and how much of it is put in so far
    Task<(MeshBuffers solid, MeshBuffers shadow)[]> _farMade;
    int _farPut;
    bool FarReady => _farMade == null;

    // the batches being meshed on the workers in play, and what they are of
    Task<Built[]> _making;
    int[] _makingOf;
    Task<MeshBuffers[]> _shading;
    int[] _shadingOf;
    Task<(MeshBuffers solid, MeshBuffers shadow)[]> _farMaking;
    int[] _farMakingOf;
    // chunks meshed and waiting to be made Godot's, in the order they were meshed, and where the first of them is
    readonly Queue<(int ci, Built b)> _waiting = new();
    readonly Queue<(int ci, MeshBuffers b)> _shadesWaiting = new();
    /// <summary>Vertices made into Godot's meshes a frame at most (the first chunk waiting always goes in).</summary>
    const int VerticesAFrame = 160_000;
    int _spent;

    int RegionOf(int ci)
    {
        _w.ChunkCoords(ci, out int cx, out int cy, out int cz);
        return ((cy >> 1) * _rz + (cz >> 1)) * _rx + (cx >> 1);
    }

    void PutFar(int[] blocks, (MeshBuffers solid, MeshBuffers shadow)[] made)
    {
        for (int i = 0; i < blocks.Length; i++)
        {
            Put(_far, blocks[i], ToGodot(made[i].solid), _mat, GeometryInstance3D.ShadowCastingSetting.Off, _showSolid && _coarse);
            Put(_farShade, blocks[i], ToGodot(made[i].shadow), null, GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, _showSolid && _coarse);
        }
    }

    /// <summary>
    /// The level the valley is drawn at, by how many pixels a voxel has on the screen (the coarse one under 1,7, back
    /// over 1,9); always the fine one through someone's eyes. Every block changed is made again before the coarse comes on.
    /// </summary>
    public void ChooseLevel(float pxPerVoxel, bool close)
    {
        bool want = FarReady && !close && (_coarse ? pxPerVoxel < 1.9f : pxPerVoxel < 1.7f);
        if (want == _coarse) return;
        _coarse = want;
        if (_coarse)
        {
            // a batch still being made goes in first, so that it does not come after the newer ones made now
            if (_farMaking != null) { PutFar(_farMakingOf, _farMaking.Result); _farMaking = null; }
            if (_farDirty.Count > 0)
            {
                var blocks = new int[_farDirty.Count];
                _farDirty.CopyTo(blocks);
                _farDirty.Clear();
                Prof.Count("far blocks", blocks.Length);
                PutFar(blocks, MakeFar(blocks));
            }
        }
        ShowLevels();
    }

    void ShowLevels()
    {
        foreach (var mi in _inst) if (mi != null) mi.Visible = _showSolid && !_coarse;
        foreach (var mi in _shade) if (mi != null) mi.Visible = _showSolid && !_coarse;
        foreach (var mi in _far) if (mi != null) mi.Visible = _showSolid && _coarse;
        foreach (var mi in _farShade) if (mi != null) mi.Visible = _showSolid && _coarse;
    }

    /// <summary>The ground and the water shown or not (--perf-without measures what each costs).</summary>
    public void ShowParts(bool solid, bool water)
    {
        _showSolid = solid;
        ShowLevels();
        foreach (var mi in _water) if (mi != null) mi.Visible = water;
    }

    /// <summary>
    /// Edited chunks meshed again, so many at a time: in play the batch meshed on the workers waits, when it is ready,
    /// to be made Godot's a few chunks a frame, and the next one is begun; else all at once. Their shadows follow a
    /// little after (<see cref="RemeshShadows"/>).
    /// </summary>
    public int RemeshDirty(int maxChunks = 64)
    {
        _spent = 0;
        if (_making != null && _making.IsCompleted)
        {
            var made = _making.Result;
            for (int i = 0; i < made.Length; i++) _waiting.Enqueue((_makingOf[i], made[i]));
            _making = null;
        }
        while (_waiting.Count > 0 && (_spent == 0 || _spent < VerticesAFrame))
        {
            var (ci, b) = _waiting.Dequeue();
            _spent += VerticesOf(b.Solid) + VerticesOf(b.Water);
            PutChunk(ci, b);
        }
        // the next batch while this one goes in, as long as not too much waits
        if (_making != null || _w.DirtyCount == 0 || _waiting.Count > maxChunks) return 0;
        _batch.Clear();
        _w.TakeDirty(_batch, maxChunks);
        Prof.Count("chunks", _batch.Count);
        var chunks = _batch.ToArray();
        if (Async)
        {
            _makingOf = chunks;
            _making = Task.Run(() => MakeChunks(chunks));
        }
        else
        {
            var made = MakeChunks(chunks);
            for (int i = 0; i < chunks.Length; i++) PutChunk(chunks[i], made[i]);
        }
        return chunks.Length;
    }

    void PutChunk(int ci, Built b)
    {
        Put(_inst, ci, ToGodot(b.Solid), _mat, GeometryInstance3D.ShadowCastingSetting.Off, _showSolid && !_coarse);
        Put(_water, ci, ToGodot(b.Water), _waterMat, GeometryInstance3D.ShadowCastingSetting.Off, true);
        Grass.Take(ci, b.Sward);
        _shadeDirty.Add(ci);
        _farDirty.Add(RegionOf(ci));
    }

    /// <summary>The shadows of the chunks made again lately, sixteen at a time, meshed as the chunks are and made Godot's in what is left of the frame's vertices.</summary>
    public void RemeshShadows()
    {
        if (_shading != null && _shading.IsCompleted)
        {
            var made = _shading.Result;
            for (int i = 0; i < made.Length; i++) _shadesWaiting.Enqueue((_shadingOf[i], made[i]));
            _shading = null;
        }
        while (_shadesWaiting.Count > 0 && (_spent == 0 || _spent < VerticesAFrame))
        {
            var (ci, b) = _shadesWaiting.Dequeue();
            _spent += VerticesOf(b);
            Put(_shade, ci, ToGodot(b), null, GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, _showSolid && !_coarse);
        }
        if (_shading != null || _shadeDirty.Count == 0 || _shadesWaiting.Count > 16) return;
        _shadeBatch.Clear();
        foreach (int ci in _shadeDirty) { _shadeBatch.Add(ci); if (_shadeBatch.Count >= 16) break; }
        foreach (int ci in _shadeBatch) _shadeDirty.Remove(ci);
        Prof.Count("shadows", _shadeBatch.Count);
        var chunks = _shadeBatch.ToArray();
        if (Async)
        {
            _shadingOf = chunks;
            _shading = Task.Run(() => MakeShadows(chunks));
        }
        else
        {
            var made = MakeShadows(chunks);
            for (int i = 0; i < chunks.Length; i++) Put(_shade, chunks[i], ToGodot(made[i]), null, GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, _showSolid && !_coarse);
        }
    }

    /// <summary>
    /// A few of the changed blocks of the coarse level made again while it is drawn; while it is not they wait, all made
    /// at once when it comes on (<see cref="ChooseLevel"/>), so the work of the near view is never slowed by the far one.
    /// </summary>
    public void RemeshFar()
    {
        // the first making of the coarse level, meshed off the main thread, made Godot's a few dozen blocks a frame
        if (_farMade != null)
        {
            if (!_farMade.IsCompleted) return;
            var made = _farMade.Result;
            for (int n = 0; n < 48 && _farPut < made.Length; n++, _farPut++)
            {
                Put(_far, _farPut, ToGodot(made[_farPut].solid), _mat, GeometryInstance3D.ShadowCastingSetting.Off, _showSolid && _coarse);
                Put(_farShade, _farPut, ToGodot(made[_farPut].shadow), null, GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, _showSolid && _coarse);
            }
            if (_farPut >= made.Length) _farMade = null;
            return;
        }
        if (_farMaking != null)
        {
            if (!_farMaking.IsCompleted) return;
            PutFar(_farMakingOf, _farMaking.Result);
            _farMaking = null;
        }
        if (!_coarse || _farDirty.Count == 0) return;
        _farBatch.Clear();
        const int most = 4;
        foreach (int ri in _farDirty) { _farBatch.Add(ri); if (_farBatch.Count >= most) break; }
        foreach (int ri in _farBatch) _farDirty.Remove(ri);
        Prof.Count("far blocks", _farBatch.Count);
        var blocks = _farBatch.ToArray();
        if (Async)
        {
            _farMakingOf = blocks;
            _farMaking = Task.Run(() => MakeFar(blocks));
        }
        else PutFar(blocks, MakeFar(blocks));
    }

    /// <summary>
    /// The scene going (a new game, a load): the batches still being meshed waited for, and the meshes hung on the
    /// nodes let go here on the main thread rather than by the garbage collector on a thread of its own.
    /// </summary>
    public override void _ExitTree()
    {
        _making?.Wait(); _shading?.Wait(); _farMaking?.Wait(); _farMade?.Wait();
        _making = null; _shading = null; _farMaking = null; _farMade = null;
        _waiting.Clear();
        _shadesWaiting.Clear();
        foreach (var slots in new[] { _inst, _water, _shade, _far, _farShade })
            if (slots != null)
                foreach (var mi in slots)
                    if (mi != null && IsInstanceValid(mi)) mi.Mesh?.Dispose();
    }

    /// <summary>A mesh hung on its slot's node (made if there is none), or the node let go if there is no mesh; the mesh it replaces is let go at once rather than when the garbage is next collected.</summary>
    void Put(MeshInstance3D[] slots, int ci, ArrayMesh mesh, Material mat, GeometryInstance3D.ShadowCastingSetting shadows, bool visible)
    {
        var mi = slots[ci];
        if (mesh == null)
        {
            if (mi != null) { var gone = mi.Mesh; mi.QueueFree(); slots[ci] = null; gone?.Dispose(); }
            return;
        }
        if (mi == null)
        {
            mi = new MeshInstance3D { MaterialOverride = mat, CastShadow = shadows, Visible = visible };
            AddChild(mi);
            slots[ci] = mi;
        }
        var old = mi.Mesh;
        mi.Mesh = mesh;
        old?.Dispose();
    }

    // ---------------------------------------------------------------- the current

    /// <summary>Strongest current the flow map holds, voxels a second.</summary>
    const float FlowMax = 6;
    float[] _riverFlow;
    Image _flowImg;
    ImageTexture _flowTex;
    byte[] _flowBytes;
    double _flowAt = -99;
    bool _flowStill = true;

    /// <summary>
    /// The current over the valley for the water shader, one texel to a 4×4 patch: the river runs along its bed, quicker
    /// where it is narrow and slack by the banks; water let out runs on the way it spread (see <see cref="UpdateFlow"/>).
    /// </summary>
    public void InitFlow(Site site)
    {
        int W = _w.SX / Sim.FlowPatch, H = _w.SZ / Sim.FlowPatch;
        _riverFlow = new float[W * H * 2];
        if (site.River != null && site.WaterTop != null)
            for (int j = 0; j < H; j++)
                for (int i = 0; i < W; i++)
                {
                    int x = i * Sim.FlowPatch + 2, z = j * Sim.FlowPatch + 2;
                    if (site.WaterTop[z * _w.SX + x] == 0) continue;
                    // the river's course, wherever it winds: along it, quicker where it is narrow
                    site.RiverAt(x, z, out float d, out float half, out float dx, out float dz);
                    float off = d / (half + 6);
                    if (off > 1.3f) continue;   // a pond or a lake: still
                    float speed = 64f / half * (1 - .6f * Math.Min(1, off * off));
                    _riverFlow[(j * W + i) * 2] = dx * speed;
                    _riverFlow[(j * W + i) * 2 + 1] = dz * speed;
                }
        _flowBytes = new byte[W * H * 2];
        FillFlow(null, 0);
        _flowImg = Image.CreateFromData(W, H, false, Image.Format.Rg8, _flowBytes);
        _flowTex = ImageTexture.CreateFromImage(_flowImg);
        _waterMat.SetShaderParameter("flow_tex", _flowTex);
        _waterMat.SetShaderParameter("map_size", new Vector2(_w.SX, _w.SZ));
        _waterMat.SetShaderParameter("flow_max", FlowMax);
    }

    /// <summary>The river's current, and where water has run lately the way it went: toward where it got to later.</summary>
    void FillFlow(float[] arrived, double now)
    {
        int W = _w.SX / Sim.FlowPatch, H = _w.SZ / Sim.FlowPatch;
        for (int j = 0; j < H; j++)
            for (int i = 0; i < W; i++)
            {
                int c = j * W + i;
                float fx = _riverFlow[c * 2], fz = _riverFlow[c * 2 + 1];
                double age = arrived == null ? 1e9 : now - arrived[c];
                if (age < 25)
                {
                    float At(int ii, int jj)
                    {
                        if (ii < 0 || jj < 0 || ii >= W || jj >= H) return arrived[c];
                        float a = arrived[jj * W + ii];
                        return now - a < 60 ? a : arrived[c];
                    }
                    float gx = At(i + 1, j) - At(i - 1, j), gz = At(i, j + 1) - At(i, j - 1), len = MathF.Sqrt(gx * gx + gz * gz);
                    if (len > 1e-4f)
                    {
                        float run = 4.5f * MathF.Exp(-(float)age / 10);
                        fx += gx / len * run; fz += gz / len * run;
                    }
                }
                _flowBytes[c * 2] = (byte)Math.Clamp(128 + fx / FlowMax * 127, 0, 255);
                _flowBytes[c * 2 + 1] = (byte)Math.Clamp(128 + fz / FlowMax * 127, 0, 255);
            }
    }

    /// <summary>Brings the current up to date a few times a second while water runs and for a while after it settles.</summary>
    public void UpdateFlow(Sim sim)
    {
        if (_flowTex == null || sim.WaterArrived == null || sim.Time - _flowAt < .25) return;
        bool running = sim.Time - sim.LastPour < 30;
        if (!running && _flowStill) return;
        _flowAt = sim.Time;
        _flowStill = !running;
        FillFlow(running ? sim.WaterArrived : null, sim.Time);
        _flowImg.SetData(_w.SX / Sim.FlowPatch, _w.SZ / Sim.FlowPatch, false, Image.Format.Rg8, _flowBytes);
        _flowTex.Update(_flowImg);
    }

    /// <summary>Chunks with water drawn in them.</summary>
    public int WaterChunks()
    {
        int n = 0;
        foreach (var mi in _water) if (mi != null) n++;
        return n;
    }

    public long Triangles()
    {
        long t = 0;
        foreach (var mi in _inst)
            if (mi?.Mesh is ArrayMesh am && am.GetSurfaceCount() > 0)
                t += am.SurfaceGetArrayIndexLen(0) / 3;
        return t;
    }
}
