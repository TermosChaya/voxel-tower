using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace VoxelTower.Core;

/// <summary>
/// The voxel field, stored as 32³ chunks. A chunk that is all one material (sky, deep bedrock)
/// keeps no array at all, so large maps stay cheap.
/// </summary>
public sealed class VoxelWorld
{
    public const int CS = 32, Shift = 5, Mask = 31, CS3 = CS * CS * CS;
    /// <summary>Edge of one voxel in scene units: what the engine draws it as. Only the camera, light and fog care.</summary>
    public const float Vox = 1f / 16f;
    /// <summary>
    /// Voxels in a metre as the valley's people measure it: a voxel is 12.5 cm, a man about 15 voxels tall,
    /// a tree 40 to 70, the plateau's cliffs 44.
    /// </summary>
    public const int PerMetre = 8;
    /// <summary>Voxels in one cubic metre.</summary>
    public const int PerM3 = PerMetre * PerMetre * PerMetre;

    public readonly int SX, SY, SZ, CX, CY, CZ, ChunkCount;
    /// <summary>World position (metres) of the corner of voxel (0,0,0); the field is centred on the origin.</summary>
    public readonly float OriginX, OriginZ;

    readonly byte[][] _data;
    readonly byte[] _uniform;
    readonly bool[] _dirty;
    /// <summary>Chunks written since the valley was made (a save keeps only these: the rest is made again from the seed).</summary>
    readonly bool[] _edited;
    /// <summary>Whether writes are being kept count of: off while the valley is being made, on once play begins.</summary>
    public bool TrackEdits;
    readonly List<int> _dirtyList = new();
    readonly bool[] _colDirty;
    readonly List<int> _colDirtyList = new();

    public int Revision { get; private set; }

    /// <summary>Called after every voxel written with Set: navigation and the map listen to it.</summary>
    public Action<int, int, int> Edited;

    /// <summary>Called after Edited with what the voxel held before: the water listens to it.</summary>
    public Action<int, int, int, byte> Replaced;

    public VoxelWorld(int sx, int sy, int sz)
    {
        if (sx % CS != 0 || sy % CS != 0 || sz % CS != 0) throw new ArgumentException("world size must be a multiple of 32");
        SX = sx; SY = sy; SZ = sz;
        CX = sx / CS; CY = sy / CS; CZ = sz / CS; ChunkCount = CX * CY * CZ;
        _data = new byte[ChunkCount][];
        _uniform = new byte[ChunkCount];
        _dirty = new bool[ChunkCount];
        _edited = new bool[ChunkCount];
        _colDirty = new bool[sx * sz];
        OriginX = -sx * Vox / 2;
        OriginZ = -sz * Vox / 2;
    }

    public int ChunkIndex(int cx, int cy, int cz) => (cy * CZ + cz) * CX + cx;
    public void ChunkCoords(int ci, out int cx, out int cy, out int cz) { cx = ci % CX; cz = ci / CX % CZ; cy = ci / (CX * CZ); }
    static int Local(int x, int y, int z) => ((y & Mask) << 10) | ((z & Mask) << 5) | (x & Mask);

    /// <summary>
    /// Whether a voxel is in the world: in the box, and on the land when the land is a shape within it (a hexagon, see
    /// <see cref="SetLand"/>) — outside that there is nothing, and nothing may be put there: water meets it as a wall,
    /// whatever is laid or fallen there is gone. Asked in every search through the field, so it is kept inlined.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InBounds(int x, int y, int z) => (uint)x < (uint)SX && (uint)y < (uint)SY && (uint)z < (uint)SZ && (_land == null || _land[z * SX + x]);

    /// <summary>The columns of the land (index z·SX + x), when it is a shape within the box; null for the whole box.</summary>
    bool[] _land;
    /// <summary>The shape of the land within the box, when it has one.</summary>
    public Hex Land { get; private set; }

    /// <summary>The land a hexagon within the box: the columns outside it are no part of the world.</summary>
    public void SetLand(Hex hex)
    {
        Land = hex;
        if (hex == null) { _land = null; return; }
        _land = new bool[SX * SZ];
        for (int z = 0; z < SZ; z++)
            for (int x = 0; x < SX; x++) _land[z * SX + x] = hex.Inside(x, z);
    }

    /// <summary>Whether a column is on the land (and in the box).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool OnLand(int x, int z) => (uint)x < (uint)SX && (uint)z < (uint)SZ && (_land == null || _land[z * SX + x]);

    /// <summary>Material at a voxel. Everything below the field is bedrock, even past its edges; beside and above it is air.</summary>
    public byte Get(int x, int y, int z)
    {
        if (y < 0) return Mat.Bed;
        if ((uint)x >= (uint)SX || (uint)z >= (uint)SZ || y >= SY) return Mat.Air;
        int ci = ((y >> Shift) * CZ + (z >> Shift)) * CX + (x >> Shift);
        var d = _data[ci];
        return d == null ? _uniform[ci] : d[Local(x, y, z)];
    }

    /// <summary>Write without marking anything; used while generating the map.</summary>
    public void SetRaw(int x, int y, int z, byte m)
    {
        // nothing is made off the land, a tree's crown over its edge or a ruin's stone
        if (_land != null && !_land[z * SX + x]) return;
        int ci = ((y >> Shift) * CZ + (z >> Shift)) * CX + (x >> Shift);
        if (TrackEdits) _edited[ci] = true;
        var d = _data[ci];
        if (d == null)
        {
            if (_uniform[ci] == m) return;
            d = _data[ci] = new byte[CS3];
            if (_uniform[ci] != 0) Array.Fill(d, _uniform[ci]);
        }
        d[Local(x, y, z)] = m;
    }

    /// <summary>Write one voxel and queue the chunks whose faces it touches for re-meshing.</summary>
    public bool Set(int x, int y, int z, byte m)
    {
        if (!InBounds(x, y, z)) return false;
        byte old = Get(x, y, z);
        if (old == m) return false;
        SetRaw(x, y, z, m);
        Revision++;
        Touch(x, y, z);
        int col = z * SX + x;
        if (!_colDirty[col]) { _colDirty[col] = true; _colDirtyList.Add(col); }
        Edited?.Invoke(x, y, z);
        Replaced?.Invoke(x, y, z, old);
        return true;
    }

    /// <summary>Hands over the columns (z·SX + x) edited since the last call, for the map in the corner.</summary>
    public int TakeDirtyColumns(List<int> into)
    {
        foreach (int c in _colDirtyList) { _colDirty[c] = false; into.Add(c); }
        int n = _colDirtyList.Count;
        _colDirtyList.Clear();
        return n;
    }

    void Touch(int x, int y, int z)
    {
        int cx = x >> Shift, cy = y >> Shift, cz = z >> Shift;
        MarkChunk(cx, cy, cz);
        int lx = x & Mask, ly = y & Mask, lz = z & Mask;
        if (lx == 0) MarkChunk(cx - 1, cy, cz); else if (lx == Mask) MarkChunk(cx + 1, cy, cz);
        if (ly == 0) MarkChunk(cx, cy - 1, cz); else if (ly == Mask) MarkChunk(cx, cy + 1, cz);
        if (lz == 0) MarkChunk(cx, cy, cz - 1); else if (lz == Mask) MarkChunk(cx, cy, cz + 1);
    }

    public void MarkChunk(int cx, int cy, int cz)
    {
        if ((uint)cx >= (uint)CX || (uint)cy >= (uint)CY || (uint)cz >= (uint)CZ) return;
        int ci = ChunkIndex(cx, cy, cz);
        if (_dirty[ci]) return;
        _dirty[ci] = true;
        _dirtyList.Add(ci);
    }

    public void MarkAllDirty()
    {
        for (int ci = 0; ci < ChunkCount; ci++) if (!_dirty[ci]) { _dirty[ci] = true; _dirtyList.Add(ci); }
    }

    /// <summary>Hands over the chunks waiting for a new mesh.</summary>
    public int TakeDirty(List<int> into, int max = int.MaxValue)
    {
        int n = Math.Min(max, _dirtyList.Count);
        for (int i = 0; i < n; i++) { int ci = _dirtyList[i]; _dirty[ci] = false; into.Add(ci); }
        _dirtyList.RemoveRange(0, n);
        return n;
    }

    public int DirtyCount => _dirtyList.Count;

    /// <summary>Drops the arrays of chunks that turned out to be a single material.</summary>
    public int Compact()
    {
        int freed = 0;
        for (int ci = 0; ci < ChunkCount; ci++)
        {
            var d = _data[ci]; if (d == null) continue;
            byte first = d[0]; bool same = true;
            for (int i = 1; i < d.Length; i++) if (d[i] != first) { same = false; break; }
            if (!same) continue;
            _data[ci] = null; _uniform[ci] = first; freed++;
        }
        return freed;
    }

    public int AllocatedChunks { get { int n = 0; foreach (var d in _data) if (d != null) n++; return n; } }

    /// <summary>All one material and boxed in by solid chunks: nothing of it can be seen.</summary>
    public bool IsBuried(int ci)
    {
        if (_data[ci] != null || _uniform[ci] == Mat.Air) return false;
        ChunkCoords(ci, out int cx, out int cy, out int cz);
        Span<int> n = stackalloc int[] { 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1 };
        for (int k = 0; k < 18; k += 3)
        {
            int nx = cx + n[k], ny = cy + n[k + 1], nz = cz + n[k + 2];
            if (ny < 0) continue;
            if (nx < 0 || nx >= CX || nz < 0 || nz >= CZ || ny >= CY) return false;
            int ni = ChunkIndex(nx, ny, nz);
            if (_data[ni] != null || _uniform[ni] == Mat.Air) return false;
        }
        return true;
    }

    public bool IsEmpty(int ci) => _data[ci] == null && _uniform[ci] == Mat.Air;

    /// <summary>A chunk's voxels, or null when the whole chunk is one material (see <see cref="ChunkUniform"/>).</summary>
    public byte[] ChunkData(int ci) => _data[ci];
    public byte ChunkUniform(int ci) => _uniform[ci];

    /// <summary>Written since the valley was made (see <see cref="TrackEdits"/>).</summary>
    public bool Changed(int ci) => _edited[ci];

    /// <summary>Puts a whole chunk back as it was saved: its voxels, or (data null) one material through all of it.</summary>
    public void SetChunk(int ci, byte[] data, byte uniform)
    {
        _data[ci] = data;
        _uniform[ci] = uniform;
        _edited[ci] = true;
        Revision++;
        ChunkCoords(ci, out int cx, out int cy, out int cz);
        MarkChunk(cx, cy, cz);
    }

    /// <summary>
    /// Copies a cube of voxels, n a side from (x0, y0, z0), into a flat array, x fastest; what lies past the field as
    /// <see cref="Get"/> has it (bedrock under, air beside and over). A chunk's row at a time: much quicker than a Get each.
    /// </summary>
    public void CopyCube(int x0, int y0, int z0, int n, byte[] into)
    {
        for (int y = 0; y < n; y++)
            for (int z = 0; z < n; z++)
            {
                int row = (y * n + z) * n, wy = y0 + y, wz = z0 + z;
                if (wy < 0 || wy >= SY || wz < 0 || wz >= SZ) { into.AsSpan(row, n).Fill(wy < 0 ? Mat.Bed : Mat.Air); continue; }
                for (int x = 0; x < n;)
                {
                    int wx = x0 + x;
                    if (wx < 0 || wx >= SX) { into[row + x++] = Mat.Air; continue; }
                    int ci = ((wy >> Shift) * CZ + (wz >> Shift)) * CX + (wx >> Shift);
                    int run = Math.Min(Math.Min(n - x, CS - (wx & Mask)), SX - wx);
                    var d = _data[ci];
                    if (d == null) into.AsSpan(row + x, run).Fill(_uniform[ci]);
                    else d.AsSpan(Local(wx, wy, wz), run).CopyTo(into.AsSpan(row + x, run));
                    x += run;
                }
            }
    }

    /// <summary>Copies a chunk plus a one-voxel shell of its neighbours into a 34³ block for the mesher.</summary>
    public void CopyPadded(int ci, byte[] pad)
    {
        const int P = CS + 2;
        ChunkCoords(ci, out int cx, out int cy, out int cz);
        int ox = cx * CS, oy = cy * CS, oz = cz * CS;
        var d = _data[ci]; byte u = _uniform[ci];
        for (int y = 0; y < CS; y++)
            for (int z = 0; z < CS; z++)
            {
                int dst = ((y + 1) * P + (z + 1)) * P + 1;
                if (d == null) pad.AsSpan(dst, CS).Fill(u);
                else d.AsSpan((y << 10) | (z << 5), CS).CopyTo(pad.AsSpan(dst, CS));
            }
        for (int y = -1; y <= CS; y++)
            for (int z = -1; z <= CS; z++)
            {
                int row = ((y + 1) * P + (z + 1)) * P;
                if (y < 0 || y == CS || z < 0 || z == CS)
                    for (int x = -1; x <= CS; x++) pad[row + x + 1] = Get(ox + x, oy + y, oz + z);
                else
                {
                    pad[row] = Get(ox - 1, oy + y, oz + z);
                    pad[row + CS + 1] = Get(ox + CS, oy + y, oz + z);
                }
            }
    }

    // ---- columns ----

    /// <summary>Top of the highest solid voxel that is not plant growth or water: the bed under a pond.</summary>
    public int SurfAt(int x, int z) => TopWhere(x, z, 1);

    /// <summary>Top of the natural ground, ignoring anything the player built.</summary>
    public int GroundTop(int x, int z) => TopWhere(x, z, 2);

    /// <summary>Top of anything at all, plants and water included.</summary>
    public int ColTop(int x, int z) => TopWhere(x, z, 0);

    /// <summary>
    /// Scans a column from the top, skipping whole chunks of air: 0 counts every voxel,
    /// 1 skips plants, 2 skips plants and the player's own masonry.
    /// </summary>
    int TopWhere(int x, int z, int skip)
    {
        if ((uint)x >= (uint)SX || (uint)z >= (uint)SZ) return 0;
        int cx = x >> Shift, cz = z >> Shift, local = ((z & Mask) << 5) | (x & Mask);
        for (int cy = CY - 1; cy >= 0; cy--)
        {
            int ci = (cy * CZ + cz) * CX + cx;
            var d = _data[ci];
            if (d == null)
            {
                byte u = _uniform[ci];
                if (u != 0 && Counts(u, skip)) return cy * CS + CS;
                continue;
            }
            for (int ly = CS - 1; ly >= 0; ly--)
            {
                byte m = d[(ly << 10) | local];
                if (m != 0 && Counts(m, skip)) return cy * CS + ly + 1;
            }
        }
        return 0;
    }

    static bool Counts(byte m, int skip) => skip switch
    {
        0 => true,
        1 => !Mat.IsVeg[m] && m != Mat.Water && !Mat.IsDecor[m],
        _ => !Mat.IsVeg[m] && m != Mat.Water && !Mat.IsPlayer[m] && !Mat.IsDecor[m],
    };

    /// <summary>Writes a vertical run of one material straight into the chunk arrays (map generation).</summary>
    public void FillColumnRaw(int x, int z, int y0, int y1, byte m)
    {
        if ((uint)x >= (uint)SX || (uint)z >= (uint)SZ || _land != null && !_land[z * SX + x]) return;
        y0 = Math.Max(0, y0); y1 = Math.Min(SY, y1);
        int local = ((z & Mask) << 5) | (x & Mask);
        for (int y = y0; y < y1;)
        {
            int cy = y >> Shift, ci = (cy * CZ + (z >> Shift)) * CX + (x >> Shift);
            int end = Math.Min(y1, (cy + 1) * CS);
            var d = _data[ci];
            if (d == null)
            {
                if (_uniform[ci] == m) { y = end; continue; }
                d = _data[ci] = new byte[CS3];
                if (_uniform[ci] != 0) Array.Fill(d, _uniform[ci]);
            }
            for (; y < end; y++) d[((y & Mask) << 10) | local] = m;
        }
    }

    // ---- coordinates ----
    public float WX(float x) => x * Vox + OriginX;
    public float WY(float y) => y * Vox;
    public float WZ(float z) => z * Vox + OriginZ;
    public int VX(float wx) => (int)MathF.Floor((wx - OriginX) / Vox);
    public int VY(float wy) => (int)MathF.Floor(wy / Vox);
    public int VZ(float wz) => (int)MathF.Floor((wz - OriginZ) / Vox);
}
