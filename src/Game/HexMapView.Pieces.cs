using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The world map's land kept up about the eye: the pieces the screen shows and a ring about them made in voxels on the
/// worker threads (HexTiles.cs), a few at a time, nearest first; made into Godot's meshes here on the main thread, so
/// many vertices a frame; those far behind let go. A piece whose hexes changed is made again, its old mesh shown till the
/// new one comes. From far off no piece is made: the plain far view (HexFar.cs) is the land, its things marked by small
/// blocks of their colour.
/// </summary>
public partial class HexMapView
{
    ShaderMaterial _solid, _water, _farMat;

    /// <summary>A piece of the land drawn: its ground and what stands on it, its water; whether it is to be made again.</summary>
    sealed class Piece
    {
        public MeshInstance3D Solid, Water, Shadow;
        public bool Stale;
        public int Made;
    }

    /// <summary>A piece made on a worker: its meshes, still the core's buffers, and how long it took.</summary>
    struct Built
    {
        public readonly MeshBuffers Solid, Water, Shadow;
        public readonly double Ms;
        /// <summary>Its ground put in already, its water too (what is left goes in on the next frames).</summary>
        public bool Begun, WaterDone;
        public Built(MeshBuffers s, MeshBuffers w, MeshBuffers sh, double ms) { Solid = s; Water = w; Shadow = sh; Ms = ms; Begun = false; WaterDone = false; }
    }

    readonly Dictionary<int, Piece> _pieces = new();
    readonly Dictionary<int, Task<Built>> _making = new();
    readonly Queue<(int key, Built b)> _ready = new();
    readonly HashSet<int> _want = new();
    /// <summary>Whether what the screen wants has been worked out since the eye last jumped (the self-test waits for it).</summary>
    bool _wantKnown;
    /// <summary>The longest a frame spent putting meshes in, and the whole of the map's work, since the map was opened, ms.</summary>
    public double WorstPutMs, WorstWorkMs;
    readonly Stopwatch _clock = new();
    readonly List<int> _order = new();
    int _madeCount;

    /// <summary>Pieces drawn, being made, waiting to be put in; how long the last few took on the workers.</summary>
    public int PiecesShown => _pieces.Count;
    public int PiecesMaking => _making.Count + _ready.Count;
    public double PieceMs { get; private set; }
    /// <summary>Vertices put into Godot's meshes a frame at most (the first piece waiting always goes in).</summary>
    const int VerticesAFrame = 50_000;

    int PieceKey(int tx, int tz) => tz * Layout.TilesX + tx;

    void Clear()
    {
        foreach (var t in _making.Values) { try { t.Wait(); } catch { } }
        _making.Clear();
        while (_ready.Count > 0) _ready.Dequeue();
        foreach (var p in _pieces.Values) Drop(p);
        _pieces.Clear();
        foreach (var f in _farBlocks) if (f != null && IsInstanceValid(f)) { var m = f.Mesh; f.QueueFree(); m?.Dispose(); }
        _farBlocks.Clear();
    }

    static void Drop(Piece p)
    {
        foreach (var mi in new[] { p.Solid, p.Water, p.Shadow })
            if (mi != null && IsInstanceValid(mi)) { var m = mi.Mesh; mi.QueueFree(); m?.Dispose(); }
    }

    /// <summary>The pieces kept up: those the screen shows wanted, made if missing or stale, put in as they come, those far off let go.</summary>
    void Pieces()
    {
        // what is made comes in first
        foreach (var (key, task) in new List<KeyValuePair<int, Task<Built>>>(_making))
        {
            if (!task.IsCompleted) continue;
            _making.Remove(key);
            if (task.IsFaulted) { GD.PrintErr($"[world map] a piece failed: {task.Exception?.GetBaseException().Message}"); continue; }
            _ready.Enqueue((key, task.Result));
        }
        int spent = 0;
        _clock.Restart();
        // a piece's meshes put in one at a time, so many vertices a frame (the first always): its ground first, then
        // its water and its shadow, a frame or two after
        while (_ready.Count > 0 && (spent == 0 || spent < VerticesAFrame))
        {
            var (key, b) = _ready.Peek();
            spent += PutNext(key, ref b);
            _ready.Dequeue();
            if (b.Begun) _ready.Enqueue((key, b));
            if (_ready.Count > 0 && _ready.Peek().key == key) break;
        }
        WorstPutMs = Math.Max(WorstPutMs, _clock.Elapsed.TotalMilliseconds);
        // what the screen shows, and a ring about it
        _want.Clear();
        if (_dist < NearMost)
        {
            var shown = GroundShown();
            float m = HexMapLayout.Tile * .6f;
            var (tx0, tz0, tx1, tz1) = Layout.TilesIn(shown.Position.X - m, shown.Position.Y - m, shown.End.X + m, shown.End.Y + m);
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++) _want.Add(PieceKey(tx, tz));
        }
        _wantKnown = true;
        // made nearest the eye first, so many at once
        _order.Clear();
        foreach (int k in _want)
            if ((!_pieces.TryGetValue(k, out var p) || p.Stale) && !_making.ContainsKey(k)) _order.Add(k);
        _order.Sort((a, b) => Far(a).CompareTo(Far(b)));
        int most = Math.Max(2, System.Environment.ProcessorCount - 3);
        foreach (int k in _order)
        {
            if (_making.Count >= most) break;
            int tx = k % Layout.TilesX, tz = k / Layout.TilesX;
            var src = Source; var lay = Layout;
            _making[k] = Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var w = HexTiles.Build(src, lay, tx, tz);
                var (s, wa, sh) = HexTiles.Mesh(w, tx, tz, shadow: true);
                return new Built(s, wa, sh, sw.Elapsed.TotalMilliseconds);
            });
            if (_pieces.TryGetValue(k, out var stale)) stale.Stale = false;
        }
        // those far off let go: out of what is wanted by more than a piece
        if (_pieces.Count > _want.Count + 8 || _dist >= NearMost)
        {
            var keep = new HashSet<int>();
            if (_dist < NearMost)
                foreach (int k in _want)
                {
                    int tx = k % Layout.TilesX, tz = k / Layout.TilesX;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++) keep.Add(PieceKey(Math.Clamp(tx + dx, 0, Layout.TilesX - 1), Math.Clamp(tz + dz, 0, Layout.TilesZ - 1)));
                }
            foreach (int k in new List<int>(_pieces.Keys))
                if (!keep.Contains(k)) { Drop(_pieces[k]); _pieces.Remove(k); }
        }
    }

    float Far(int key)
    {
        float cx = (key % Layout.TilesX + .5f) * HexMapLayout.Tile, cz = (key / Layout.TilesX + .5f) * HexMapLayout.Tile;
        return (cx - _look.X) * (cx - _look.X) + (cz - _look.Y) * (cz - _look.Y);
    }

    /// <summary>The next of a piece's meshes put in (its ground, then its water, then its shadow), taken off what is left of it; the vertices that cost.</summary>
    int PutNext(int key, ref Built b)
    {
        // a piece no longer wanted is not put in
        if (!_want.Contains(key) && !_pieces.ContainsKey(key)) { b = default; return 0; }
        if (!_pieces.TryGetValue(key, out var p)) { p = new Piece(); _pieces[key] = p; }
        if (b.Solid != null || !b.Begun)
        {
            PieceMs = PieceMs <= 0 ? b.Ms : PieceMs * .8 + b.Ms * .2;
            _madeCount++;
            p.Made = _madeCount;
            int n = b.Solid?.Vertices ?? 0;
            p.Solid = Hang(p.Solid, b.Solid, _solid, GeometryInstance3D.ShadowCastingSetting.Off);
            b = new Built(null, b.Water, b.Shadow, b.Ms) { Begun = true };
            return n;
        }
        if (b.Water != null || !b.WaterDone)
        {
            int n = b.Water?.Vertices ?? 0;
            p.Water = Hang(p.Water, b.Water, _water, GeometryInstance3D.ShadowCastingSetting.Off);
            b = new Built(null, null, b.Shadow, b.Ms) { Begun = true, WaterDone = true };
            return n;
        }
        int m = b.Shadow?.Vertices ?? 0;
        p.Shadow = Hang(p.Shadow, b.Shadow, null, GeometryInstance3D.ShadowCastingSetting.ShadowsOnly);
        b = default;
        return m;
    }

    /// <summary>A mesh hung on a node of the land (made if there is none), the old one let go at once; no mesh, no node.</summary>
    MeshInstance3D Hang(MeshInstance3D mi, MeshBuffers b, Material mat, GeometryInstance3D.ShadowCastingSetting shadows)
    {
        var mesh = GodotMesh.From(b, compress: false);
        if (mesh == null)
        {
            if (mi != null && IsInstanceValid(mi)) { var gone = mi.Mesh; mi.QueueFree(); gone?.Dispose(); }
            return null;
        }
        if (mi == null || !IsInstanceValid(mi))
        {
            mi = new MeshInstance3D { MaterialOverride = mat, CastShadow = shadows };
            _land.AddChild(mi);
        }
        var old = mi.Mesh;
        mi.Mesh = mesh;
        old?.Dispose();
        return mi;
    }

    /// <summary>The pieces a hex's things and trees may reach into, made again; its far block too.</summary>
    void LookChanged(int col, int row)
    {
        var look = Source.Look(col, row);
        int i = row * Layout.Cols + col;
        if (_looks != null && _looks[i].Equals(look)) return;
        if (_looks != null) _looks[i] = look;
        var (cx, cz) = Layout.Center(col, row);
        float r = HexMapLayout.R + 10;
        var (tx0, tz0, tx1, tz1) = Layout.TilesIn(cx - r, cz - r, cx + r, cz + r);
        for (int tz = tz0; tz <= tz1; tz++)
            for (int tx = tx0; tx <= tx1; tx++)
                if (_pieces.TryGetValue(PieceKey(tx, tz), out var p)) p.Stale = true;
        _farStale.Add((row / FarBlock) * _farCols + col / FarBlock);
        _marksStale = true;
        _miniStale = true;
    }

    /// <summary>Waits until every piece the screen wants is made and put in (pictures, the self-test).</summary>
    public bool Settled => _wantKnown && _making.Count == 0 && _ready.Count == 0 && AllWanted();

    bool AllWanted()
    {
        foreach (int k in _want) if (!_pieces.TryGetValue(k, out var p) || p.Stale) return false;
        return true;
    }

    // ------------------------------------------------------------ the far view

    const int FarBlock = 16;
    readonly List<MeshInstance3D> _farBlocks = new();
    readonly HashSet<int> _farStale = new();
    int _farCols, _farRows;
    HexLook[] _looks;
    MultiMeshInstance3D _farMarks;
    bool _marksStale;

    /// <summary>The far view made of the whole map, block by block; what each hex looks like kept, to know when it changes.</summary>
    void FarLay()
    {
        var lay = Layout;
        _looks = new HexLook[lay.Cols * lay.Rows];
        for (int r = 0; r < lay.Rows; r++)
            for (int c = 0; c < lay.Cols; c++) _looks[r * lay.Cols + c] = Source.Look(c, r);
        _farCols = (lay.Cols + FarBlock - 1) / FarBlock; _farRows = (lay.Rows + FarBlock - 1) / FarBlock;
        for (int i = 0; i < _farCols * _farRows; i++) _farBlocks.Add(null);
        // (the blocks' land worked out on the workers all at once, their meshes made here)
        _farMade = new HexFar.Block[_farBlocks.Count];
        var src = Source;
        System.Threading.Tasks.Parallel.For(0, _farBlocks.Count, i =>
        {
            var (c0, r0, c1, r1) = FarRange(i);
            _farMade[i] = HexFar.Build(src, lay, c0, r0, c1, r1);
        });
        for (int i = 0; i < _farBlocks.Count; i++) FarBlockMade(i);
        _farMade = null;
        _marksStale = true;
    }

    HexFar.Block[] _farMade;

    /// <summary>The hexes a block of the far view covers (the blocks at the map's edge take in the margin of mountains that frames it).</summary>
    (int c0, int r0, int c1, int r1) FarRange(int i)
    {
        int bc = i % _farCols, br = i / _farCols;
        int c0 = bc * FarBlock - (bc == 0 ? 3 : 0), r0 = br * FarBlock - (br == 0 ? 3 : 0);
        int c1 = Math.Min((bc + 1) * FarBlock, Layout.Cols) + (bc == _farCols - 1 ? 3 : 0), r1 = Math.Min((br + 1) * FarBlock, Layout.Rows) + (br == _farRows - 1 ? 3 : 0);
        return (c0, r0, c1, r1);
    }

    void FarBlockMade(int i)
    {
        var (c0, r0, c1, r1) = FarRange(i);
        var b = _farMade != null && _farMade[i] != null ? _farMade[i] : HexFar.Build(Source, Layout, c0, r0, c1, r1);
        if (_farMade != null) _farMade[i] = null;
        ArrayMesh mesh = null;
        if (b.Vertices > 0 && b.Idx.Count > 0)
        {
            mesh = new ArrayMesh();
            using var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            var pos = new Vector3[b.Vertices]; var nrm = new Vector3[b.Vertices]; var col = new Color[b.Vertices];
            for (int v = 0; v < b.Vertices; v++)
            {
                pos[v] = new Vector3(b.Pos[v * 3], b.Pos[v * 3 + 1], b.Pos[v * 3 + 2]);
                nrm[v] = new Vector3(b.Nrm[v * 3], b.Nrm[v * 3 + 1], b.Nrm[v * 3 + 2]);
                col[v] = new Color(b.Col[v * 4], b.Col[v * 4 + 1], b.Col[v * 4 + 2], 1);
            }
            arrays[(int)Mesh.ArrayType.Vertex] = pos;
            arrays[(int)Mesh.ArrayType.Normal] = nrm;
            arrays[(int)Mesh.ArrayType.Color] = col;
            arrays[(int)Mesh.ArrayType.Index] = b.Idx.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
        var mi = _farBlocks[i];
        if (mi == null)
        {
            mi = new MeshInstance3D { MaterialOverride = _farMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _land.AddChild(mi);
            _farBlocks[i] = mi;
        }
        var old = mi.Mesh;
        mi.Mesh = mesh;
        old?.Dispose();
    }

    /// <summary>The far view's blocks made again where hexes changed; its marks of things shown from far off only.</summary>
    void FarShow()
    {
        if (_farStale.Count > 0)
        {
            foreach (int i in _farStale) if (i >= 0 && i < _farBlocks.Count) FarBlockMade(i);
            _farStale.Clear();
        }
        if (_marksStale) { FarMarks(); _marksStale = false; }
        if (_farMarks != null) _farMarks.Visible = _dist > NearMost * .85f;
        // a block of the far view is hidden where the pieces in voxels cover all of it: its hills and peaks would stand through them
        for (int i = 0; i < _farBlocks.Count; i++)
        {
            var mi = _farBlocks[i];
            if (mi == null) continue;
            mi.Visible = !Covered(i);
        }
    }

    /// <summary>Whether every piece a block of the far view lies over is drawn in voxels now.</summary>
    bool Covered(int i)
    {
        if (_dist >= NearMost || _pieces.Count == 0) return false;
        int bc = i % _farCols, br = i / _farCols;
        var (x0, z0) = Layout.Center(bc * FarBlock, br * FarBlock);
        var (x1, z1) = Layout.Center(Math.Min(Layout.Cols - 1, (bc + 1) * FarBlock), Math.Min(Layout.Rows - 1, (br + 1) * FarBlock));
        float m = HexMapLayout.W;
        var (tx0, tz0, tx1, tz1) = Layout.TilesIn(x0 - m, z0 - m, x1 + m, z1 + m);
        for (int tz = tz0; tz <= tz1; tz++)
            for (int tx = tx0; tx <= tx1; tx++)
                if (!_pieces.ContainsKey(PieceKey(tx, tz))) return false;
        return true;
    }

    /// <summary>Each thing of the map from far off: a block of its colour standing on its hex — ours blue, the horde's red, a mine gold.</summary>
    void FarMarks()
    {
        var at = new List<(Vector3 p, Color c, float s)>();
        for (int r = 0; r < Layout.Rows; r++)
            for (int c = 0; c < Layout.Cols; c++)
            {
                var l = _looks[r * Layout.Cols + c];
                if (l.Icon == HexIcon.None || !Source.Seen(c, r)) continue;
                var (x, z) = Layout.Center(c, r);
                float y = HexTiles.Base + l.Height * HexTiles.Step;
                Color col = l.Owner == 1 ? new Color("4f7fd6") : l.Owner == 2 ? new Color("c8422e")
                    : l.Icon is >= HexIcon.MineLogs and <= HexIcon.MineGems ? new Color("d9ad42")
                    : l.Icon == HexIcon.Monster ? new Color("9a3328") : new Color("a89a7c");
                float size = l.Icon is HexIcon.Citadel or HexIcon.HordeTown ? 12 : l.Icon == HexIcon.Monster ? 5 : 5;
                at.Add((new Vector3(x, y + size / 2, z) * V, col, size * V));
            }
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh { Size = Vector3.One } };
        mm.InstanceCount = at.Count;
        for (int i = 0; i < at.Count; i++)
        {
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * at[i].s), at[i].p));
            mm.SetInstanceColor(i, at[i].c);
        }
        if (_farMarks == null)
        {
            _farMarks = new MultiMeshInstance3D
            {
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .8f },
            };
            _world.AddChild(_farMarks);
        }
        var old = _farMarks.Multimesh;
        _farMarks.Multimesh = mm;
        old?.Dispose();
    }
}
