using System;

namespace VoxelTower.Core;

/// <summary>Triangle data for one mesh, in voxel units. Colours are linear.</summary>
public sealed class MeshBuffers
{
    /// <summary>xyz per vertex.</summary>
    public float[] Pos;
    public float[] Nrm;
    /// <summary>Linear rgb plus the material's colour variance in alpha (none in a shadow's mesh, nor the uvs).</summary>
    public float[] Col;
    /// <summary>Corner shade (0..1) and glow strength.</summary>
    public float[] Uv;
    /// <summary>How much the vertex sways in the wind (grass tips, leaves), and what is drawn finer than the voxels (<see cref="Mat.Skin"/>).</summary>
    public float[] Uv2;
    public int[] Idx;
    public int Quads;
    public int Vertices => Quads * 4;
    /// <summary>The arrays are borrowed from the shared pool, and longer than the mesh (<see cref="GreedyMesher.Pooled"/>).</summary>
    public bool Pooled;

    /// <summary>Borrowed arrays given back to the pool, once what they hold is copied away; the buffers are not read after.</summary>
    public void Return()
    {
        if (!Pooled) return;
        Pooled = false;
        MeshPool<float>.Shared.Return(Pos);
        MeshPool<float>.Shared.Return(Nrm);
        if (Col != null) MeshPool<float>.Shared.Return(Col);
        if (Uv != null) MeshPool<float>.Shared.Return(Uv);
        if (Uv2 != null) MeshPool<float>.Shared.Return(Uv2);
        MeshPool<int>.Shared.Return(Idx);
        Pos = Nrm = Col = Uv = Uv2 = null;
        Idx = null;
    }
}

/// <summary>
/// The pool the meshes' buffers are borrowed from: a few of each size kept, not as many as the runtime's shared pool
/// keeps for every core, which held some 160 MB of a whole valley's meshing long after it was done.
/// </summary>
static class MeshPool<T>
{
    public static readonly System.Buffers.ArrayPool<T> Shared = System.Buffers.ArrayPool<T>.Create(1 << 21, 16);
}

/// <summary>
/// What grows on a chunk's ground, which the game draws in blades finer than the voxels instead of as cubes of them:
/// the tufts of grass and the flowers standing in it, and the open tops of its turf. Entries are packed: x | y &lt;&lt; 5 |
/// z &lt;&lt; 10 in the chunk, the material &lt;&lt; 15 and, for a tuft or a flower, how many voxels tall it stands &lt;&lt; 23; for
/// turf, which of its sides are open &lt;&lt; 23 (1 +x, 2 −x, 4 +z, 8 −z), where its blades hang over the edge.
/// </summary>
public sealed class Sward
{
    /// <summary>A tuft or a flower, once where it stands on something else, however tall it grows.</summary>
    public int[] Sprigs;
    /// <summary>Turf with nothing over it but air, a tuft or a flower.</summary>
    public int[] Turf;

    public static int X(int e) => e & 31;
    public static int Y(int e) => (e >> 5) & 31;
    public static int Z(int e) => (e >> 10) & 31;
    public static byte Mat(int e) => (byte)(e >> 15);
    public static int Tall(int e) => (e >> 23) & 7;
    public static int Sides(int e) => (e >> 23) & 15;
}

/// <summary>
/// Greedy mesher with per-corner shading, the same algorithm as the browser prototype.
/// Not thread-safe: give every worker its own instance.
/// </summary>
public sealed class GreedyMesher
{
    static readonly float[] CornerShade = { .38f, .58f, .8f, 1f };

    int[] _mask = new int[VoxelWorld.CS * VoxelWorld.CS];
    /// <summary>
    /// Meshing the world: the earth showing in the side of a bank under its turf, two voxels of it, is grown over with
    /// grass like the turf above it (a bank a hand's breadth high is green all over, not a stripe of dirt).
    /// </summary>
    bool _banks;
    /// <summary>
    /// Meshing a shadow: every face of the solid one and the same (its material and its corners' shading are nothing to
    /// a shadow), so they merge into a few large ones; only positions and normals are kept.
    /// </summary>
    bool _shadow;
    /// <summary>The voxels a coarse block's cells are made from (<see cref="MeshCoarse"/>).</summary>
    byte[] _cube;
    float[] _pos = Array.Empty<float>(), _nrm = Array.Empty<float>(), _col = Array.Empty<float>(), _uv = Array.Empty<float>(), _uv2 = Array.Empty<float>();
    int[] _idx = Array.Empty<int>();
    int _q, _capQ;
    /// <summary>
    /// The buffers handed out are borrowed from the shared pool rather than made new, to be given back once copied away
    /// (<see cref="MeshBuffers.Return"/>): the valley's chunks, meshed again and again in play, would otherwise leave
    /// megabytes of large arrays a frame to the garbage collector, which gathers those only in its slowest sweeps.
    /// </summary>
    public bool Pooled;
    readonly System.Collections.Generic.List<int> _sprigs = new(), _turf = new();

    public GreedyMesher() { Grow(2048); }

    /// <summary>
    /// Quads a mesher keeps room for between meshes. A large mesh (a coarse block, a crowded chunk) grows the buffers as
    /// it needs, and they are let go again after it: every worker thread keeps a mesher, and each held 24 MB that way.
    /// </summary>
    const int KeptQ = 8192;

    void Grow(int capQ)
    {
        Array.Resize(ref _pos, capQ * 12);
        Array.Resize(ref _nrm, capQ * 12);
        Array.Resize(ref _col, capQ * 16);
        Array.Resize(ref _uv, capQ * 8);
        Array.Resize(ref _uv2, capQ * 8);
        Array.Resize(ref _idx, capQ * 6);
        _capQ = capQ;
    }

    /// <summary>A world chunk, or null when nothing of it can be seen.</summary>
    public MeshBuffers MeshChunk(VoxelWorld w, int ci, byte[] pad) => MeshChunk(w, ci, pad, null, out _);

    /// <summary>
    /// A world chunk in two parts: the solid voxels, meshed as if the water were not there so the bottom shows through it,
    /// and the water's own surface where it meets the air (null when the chunk holds no water).
    /// </summary>
    public MeshBuffers MeshChunk(VoxelWorld w, int ci, byte[] pad, byte[] wet, out MeshBuffers water) => MeshChunk(w, ci, pad, wet, out water, out _);

    /// <summary>
    /// A world chunk as above, and what grows on its ground (null when nothing does). The tufts and the flowers are not
    /// meshed: they hide no face of the turf under them and shade none of its corners, for the game draws them in blades.
    /// </summary>
    public MeshBuffers MeshChunk(VoxelWorld w, int ci, byte[] pad, byte[] wet, out MeshBuffers water, out Sward sward) =>
        MeshChunk(w, ci, pad, wet, out water, out sward, false, out _);

    /// <summary>
    /// A world chunk as above, and its shadow: the same solid voxels as the fewest faces that hold their shape, every
    /// face of them one, whatever it is of and however its corners are shaded (see <see cref="_shadow"/>).
    /// </summary>
    public MeshBuffers MeshChunk(VoxelWorld w, int ci, byte[] pad, byte[] wet, out MeshBuffers water, out Sward sward, out MeshBuffers shadow) =>
        MeshChunk(w, ci, pad, wet, out water, out sward, true, out shadow);

    /// <summary>A world chunk's shadow alone (null when nothing of it can be seen), as <see cref="MeshChunk(VoxelWorld, int, byte[], byte[], out MeshBuffers, out Sward, out MeshBuffers)"/> makes it.</summary>
    public MeshBuffers MeshShadow(VoxelWorld w, int ci, byte[] pad)
    {
        if (w.IsEmpty(ci) || w.IsBuried(ci)) return null;
        w.CopyPadded(ci, pad);
        w.ChunkCoords(ci, out int cx, out int cy, out int cz);
        // as the picture has it: no water, no tufts nor flowers
        for (int i = 0; i < pad.Length; i++) if (pad[i] == Mat.Water || Mat.IsSprig(pad[i])) pad[i] = Mat.Air;
        _shadow = true;
        var sb = Mesh(pad, VoxelWorld.CS, VoxelWorld.CS, VoxelWorld.CS, cx * VoxelWorld.CS, cy * VoxelWorld.CS, cz * VoxelWorld.CS);
        _shadow = false;
        return sb.Quads > 0 ? sb : null;
    }

    MeshBuffers MeshChunk(VoxelWorld w, int ci, byte[] pad, byte[] wet, out MeshBuffers water, out Sward sward, bool withShadow, out MeshBuffers shadow)
    {
        water = null;
        sward = null;
        shadow = null;
        if (w.IsEmpty(ci) || w.IsBuried(ci)) return null;
        w.CopyPadded(ci, pad);
        sward = Gather(pad, out bool sprigs);
        w.ChunkCoords(ci, out int cx, out int cy, out int cz);
        int ox = cx * VoxelWorld.CS, oy = cy * VoxelWorld.CS, oz = cz * VoxelWorld.CS;
        if (wet != null && Array.IndexOf(pad, Mat.Water) >= 0)
        {
            for (int i = 0; i < pad.Length; i++)
            {
                byte m = pad[i];
                wet[i] = m == Mat.Water ? Mat.Water : m == 0 ? (byte)0 : Blocker;
                if (m == Mat.Water) pad[i] = 0;
            }
            var wm = Mesh(wet, VoxelWorld.CS, VoxelWorld.CS, VoxelWorld.CS, ox, oy, oz, Blocker);
            water = wm.Quads > 0 ? wm : null;
        }
        // the water stops at a tuft, as it does in the world; then the tufts and flowers go from the block (its padding
        // too), so nothing is meshed of them and they hide no face of what they stand on
        if (sprigs)
            for (int i = 0; i < pad.Length; i++) if (Mat.IsSprig(pad[i])) pad[i] = Mat.Air;
        _banks = true;
        var mb = Mesh(pad, VoxelWorld.CS, VoxelWorld.CS, VoxelWorld.CS, ox, oy, oz);
        _banks = false;
        if (mb.Quads == 0) return null;
        if (withShadow)
        {
            _shadow = true;
            shadow = Mesh(pad, VoxelWorld.CS, VoxelWorld.CS, VoxelWorld.CS, ox, oy, oz);
            _shadow = false;
        }
        return mb;
    }

    /// <summary>
    /// A block of 2×2×2 chunks as it is seen from far off, every 2×2×2 voxels one cell: solid where at least 3 of its 8
    /// are (water, tufts and flowers are not counted), of the material most of them are, the upper ones winning a tie so
    /// that turf stays on top; meshed as the chunks are, at twice their size, and its shadow likewise. Null when nothing
    /// of it shows. <paramref name="grid"/> is a 34³ block for the cells and their shell.
    /// </summary>
    public MeshBuffers MeshCoarse(VoxelWorld w, int rx, int ry, int rz, byte[] grid, out MeshBuffers shadow)
    {
        const int N = VoxelWorld.CS, P = N + 2;
        shadow = null;
        int ox = rx * 2 * N, oy = ry * 2 * N, oz = rz * 2 * N;
        // nothing to see when every chunk of the block is empty, or buried whole
        bool seen = false;
        for (int k = 0; k < 8 && !seen; k++)
        {
            int cx = rx * 2 + (k & 1), cy = ry * 2 + (k >> 2 & 1), cz = rz * 2 + (k >> 1 & 1);
            if (cx >= w.CX || cy >= w.CY || cz >= w.CZ) continue;
            int ci = w.ChunkIndex(cx, cy, cz);
            seen = !w.IsEmpty(ci) && !w.IsBuried(ci);
        }
        if (!seen) return null;
        // the voxels of the cells and their shell at once (2·34 a side)
        const int B = 2 * P;
        if (_cube == null) _cube = new byte[B * B * B];
        w.CopyCube(ox - 2, oy - 2, oz - 2, B, _cube);
        Span<byte> mats = stackalloc byte[8];
        Span<int> counts = stackalloc int[8];
        for (int y = -1; y <= N; y++)
            for (int z = -1; z <= N; z++)
            {
                int row = ((y + 1) * P + (z + 1)) * P + 1;
                for (int x = -1; x <= N; x++)
                {
                    // the upper four first, so that a tie goes to them
                    int n = 0;
                    for (int k = 7; k >= 0; k--)
                    {
                        byte m = _cube[((2 * (y + 1) + (k >> 2 & 1)) * B + 2 * (z + 1) + (k >> 1 & 1)) * B + 2 * (x + 1) + (k & 1)];
                        if (m == Mat.Air || m == Mat.Water || Mat.IsSprig(m)) continue;
                        int j = 0;
                        while (j < n && mats[j] != m) j++;
                        if (j == n) { mats[n] = m; counts[n++] = 0; }
                        counts[j]++;
                    }
                    int solid = 0, best = -1;
                    for (int j = 0; j < n; j++) { solid += counts[j]; if (best < 0 || counts[j] > counts[best]) best = j; }
                    grid[row + x] = solid >= 3 ? mats[best] : Mat.Air;
                }
            }
        var mb = Mesh(grid, N, N, N, ox / 2, oy / 2, oz / 2);
        if (mb.Quads == 0) return null;
        _shadow = true;
        shadow = Mesh(grid, N, N, N, ox / 2, oy / 2, oz / 2);
        _shadow = false;
        foreach (var b in new[] { mb, shadow }) for (int i = 0; i < b.Pos.Length; i++) b.Pos[i] *= 2;
        return mb;
    }

    /// <summary>
    /// Finds the tufts and flowers of a padded chunk and the turf open above (water over turf is not open), and says
    /// whether there are tufts or flowers anywhere in the block, its padding too.
    /// </summary>
    Sward Gather(byte[] pad, out bool sprigs)
    {
        const int P = VoxelWorld.CS + 2, Row = P * P;
        sprigs = Array.IndexOf(pad, Mat.Tuft) >= 0 || Array.IndexOf(pad, Mat.Flower) >= 0 || Array.IndexOf(pad, Mat.Bloom) >= 0;
        if (!sprigs && Array.IndexOf(pad, Mat.Grass) < 0 && Array.IndexOf(pad, Mat.GrassDry) < 0 && Array.IndexOf(pad, Mat.GrassLush) < 0)
            return null;
        _sprigs.Clear();
        _turf.Clear();
        for (int y = 0; y < VoxelWorld.CS; y++)
            for (int z = 0; z < VoxelWorld.CS; z++)
            {
                int idx = ((y + 1) * P + z + 1) * P + 1;
                for (int x = 0; x < VoxelWorld.CS; x++, idx++)
                {
                    byte m = pad[idx];
                    if (Mat.IsSprig(m))
                    {
                        if (Mat.IsSprig(pad[idx - Row])) continue;
                        int tall = 1;
                        while (tall < 7 && y + tall <= VoxelWorld.CS && pad[idx + tall * Row] == m) tall++;
                        _sprigs.Add(x | y << 5 | z << 10 | m << 15 | tall << 23);
                    }
                    else if (Mat.IsGrass(m))
                    {
                        byte up = pad[idx + Row];
                        if (up != Mat.Air && !Mat.IsSprig(up)) continue;
                        static bool Open(byte n) => n == Mat.Air || Mat.IsSprig(n);
                        int sides = (Open(pad[idx + 1]) ? 1 : 0) | (Open(pad[idx - 1]) ? 2 : 0) | (Open(pad[idx + P]) ? 4 : 0) | (Open(pad[idx - P]) ? 8 : 0);
                        _turf.Add(x | y << 5 | z << 10 | m << 15 | sides << 23);
                    }
                }
            }
        if (_sprigs.Count == 0 && _turf.Count == 0) return null;
        return new Sward { Sprigs = _sprigs.Count > 0 ? _sprigs.ToArray() : null, Turf = _turf.Count > 0 ? _turf.ToArray() : null };
    }

    /// <summary>Stands in for anything solid when meshing water: it hides water faces but has none of its own.</summary>
    const byte Blocker = 255;

    /// <summary>
    /// Meshes a block of dx·dy·dz voxels that comes padded with one voxel of neighbours on every side:
    /// index ((y+1)·(dz+2) + (z+1))·(dx+2) + (x+1). Output positions are offset by (ox, oy, oz).
    /// Cells holding <paramref name="skip"/> hide their neighbours' faces but get none of their own.
    /// </summary>
    public MeshBuffers Mesh(byte[] grid, int dx, int dy, int dz, int ox, int oy, int oz, byte skip = 0)
    {
        _q = 0;
        int px = dx + 2, pz = dz + 2;
        Span<int> dims = stackalloc int[] { dx, dy, dz };
        Span<int> st = stackalloc int[] { 1, px * pz, px };
        Span<int> org = stackalloc int[] { ox, oy, oz };
        int need = Math.Max(dx * dy, Math.Max(dy * dz, dz * dx));
        if (_mask.Length < need) _mask = new int[need];
        var mask = _mask;

        for (int d = 0; d < 3; d++)
        {
            int u = (d + 1) % 3, v = (d + 2) % 3, du = dims[u], dv = dims[v];
            int sd = st[d], su = st[u], sv = st[v];
            for (int s = -1; s <= 1; s += 2)
            {
                int qoff = s * sd;
                for (int i = 0; i < dims[d]; i++)
                {
                    int n = 0;
                    for (int b = 0; b < dv; b++)
                    {
                        int idx = (i + 1) * sd + (b + 1) * sv + su;
                        for (int a = 0; a < du; a++, n++, idx += su)
                        {
                            byte m = grid[idx];
                            if (m == 0 || m == skip) { mask[n] = 0; continue; }
                            int q = idx + qoff;
                            if (grid[q] != 0) { mask[n] = 0; continue; }
                            if (_shadow) { mask[n] = 1; continue; }
                            int s1u = grid[q + su] != 0 ? 1 : 0, s0u = grid[q - su] != 0 ? 1 : 0;
                            int s1v = grid[q + sv] != 0 ? 1 : 0, s0v = grid[q - sv] != 0 ? 1 : 0;
                            int c00 = grid[q - su - sv] != 0 ? 1 : 0, c10 = grid[q + su - sv] != 0 ? 1 : 0;
                            int c11 = grid[q + su + sv] != 0 ? 1 : 0, c01 = grid[q - su + sv] != 0 ? 1 : 0;
                            int a0 = (s0u & s0v) != 0 ? 0 : 3 - (s0u + s0v + c00);
                            int a1 = (s1u & s0v) != 0 ? 0 : 3 - (s1u + s0v + c10);
                            int a2 = (s1u & s1v) != 0 ? 0 : 3 - (s1u + s1v + c11);
                            int a3 = (s0u & s1v) != 0 ? 0 : 3 - (s0u + s1v + c01);
                            int bank = 0;
                            if (_banks && d != 1 && m == Mat.Soil)
                            {
                                byte up = grid[idx + st[1]];
                                if (Mat.IsGrass(up)) { m = up; bank = 1; }
                                else if (up == Mat.Soil && idx / st[1] + 2 <= dy + 1 && Mat.IsGrass(grid[idx + 2 * st[1]])) { m = grid[idx + 2 * st[1]]; bank = 1; }
                            }
                            mask[n] = m | (a0 << 8) | (a1 << 10) | (a2 << 12) | (a3 << 14) | (bank << 16);
                        }
                    }
                    int plane = org[d] + i + (s > 0 ? 1 : 0);
                    for (int b = 0; b < dv; b++)
                    {
                        for (int a = 0; a < du;)
                        {
                            int val = mask[b * du + a];
                            if (val == 0) { a++; continue; }
                            int w = 1;
                            while (a + w < du && mask[b * du + a + w] == val) w++;
                            int h = 1;
                            for (; b + h < dv; h++)
                            {
                                int row = (b + h) * du + a; bool same = true;
                                for (int k = 0; k < w; k++) if (mask[row + k] != val) { same = false; break; }
                                if (!same) break;
                            }
                            Emit(d, u, v, s, plane, org[u] + a, org[u] + a + w, org[v] + b, org[v] + b + h, val);
                            for (int hh = 0; hh < h; hh++) Array.Clear(mask, (b + hh) * du + a, w);
                            a += w;
                        }
                    }
                }
            }
        }

        var made = new MeshBuffers
        {
            Quads = _q,
            Pooled = Pooled,
            Pos = Out(_pos, _q * 12),
            Nrm = Out(_nrm, _q * 12),
            Col = _shadow ? null : Out(_col, _q * 16),
            Uv = _shadow ? null : Out(_uv, _q * 8),
            Uv2 = _shadow ? null : Out(_uv2, _q * 8),
            Idx = Out(_idx, _q * 6),
        };
        if (_capQ > KeptQ)
        {
            _pos = _nrm = _col = _uv = _uv2 = Array.Empty<float>();
            _idx = Array.Empty<int>();
            Grow(KeptQ);
        }
        return made;
    }

    /// <summary>The first so many of a buffer, in an array of their own or one borrowed from the pool.</summary>
    T[] Out<T>(T[] from, int n)
    {
        if (!Pooled) return from.AsSpan(0, n).ToArray();
        var a = MeshPool<T>.Shared.Rent(n);
        from.AsSpan(0, n).CopyTo(a);
        return a;
    }

    void Emit(int d, int u, int v, int s, float pd, float u0, float u1, float v0, float v1, int val)
    {
        if (_q >= _capQ) Grow(_capQ * 2);
        int m = val & 255;
        int q = _q++, vb = q * 4;
        Span<int> ao = stackalloc int[] { (val >> 8) & 3, (val >> 10) & 3, (val >> 12) & 3, (val >> 14) & 3 };
        Span<float> cu = stackalloc float[] { u0, u1, u1, u0 };
        Span<float> cv = stackalloc float[] { v0, v0, v1, v1 };
        float r = Mat.Lin[m * 3], g = Mat.Lin[m * 3 + 1], bl = Mat.Lin[m * 3 + 2], va = Mat.Variance[m], em = Mat.Emission[m];
        for (int k = 0; k < 4; k++)
        {
            int o = (vb + k) * 3;
            _pos[o + d] = pd; _pos[o + u] = cu[k]; _pos[o + v] = cv[k];
            _nrm[o] = d == 0 ? s : 0; _nrm[o + 1] = d == 1 ? s : 0; _nrm[o + 2] = d == 2 ? s : 0;
            int c = (vb + k) * 4;
            _col[c] = r; _col[c + 1] = g; _col[c + 2] = bl; _col[c + 3] = va;
            int t = (vb + k) * 2;
            _uv[t] = CornerShade[ao[k]]; _uv[t + 1] = em;
            // grass sways at its tips, leaves as a mass; stone is dressed smooth, and metal shines
            float sway = 0;
            if (Mat.SwayKind[m] == 1)
            {
                float y = d == 1 ? pd : d == 0 ? cu[k] : cv[k], top = d == 1 ? pd : d == 0 ? Math.Max(u0, u1) : Math.Max(v0, v1);
                sway = d == 1 ? (s > 0 ? 1 : 0) : y >= top ? 1 : 0;
            }
            else if (Mat.SwayKind[m] == 2) sway = .55f;
            _uv2[t] = sway; _uv2[t + 1] = (val >> 16 & 1) != 0 ? 9 : Mat.Skin[m];
        }
        // Godot treats clockwise triangles as front faces; the diagonal follows the darker corners
        bool flip = ao[0] + ao[2] > ao[1] + ao[3];
        int ib = q * 6;
        if (s > 0)
        {
            if (flip) Tri(ib, vb + 1, vb + 3, vb + 2, vb + 1, vb + 0, vb + 3);
            else Tri(ib, vb + 0, vb + 2, vb + 1, vb + 0, vb + 3, vb + 2);
        }
        else
        {
            if (flip) Tri(ib, vb + 1, vb + 2, vb + 3, vb + 1, vb + 3, vb + 0);
            else Tri(ib, vb + 0, vb + 1, vb + 2, vb + 0, vb + 2, vb + 3);
        }
    }

    void Tri(int ib, int a, int b, int c, int d, int e, int f)
    {
        _idx[ib] = a; _idx[ib + 1] = b; _idx[ib + 2] = c; _idx[ib + 3] = d; _idx[ib + 4] = e; _idx[ib + 5] = f;
    }
}
