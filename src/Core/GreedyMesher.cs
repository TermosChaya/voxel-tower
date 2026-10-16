using System;

namespace VoxelTower.Core;

/// <summary>Triangle data for one mesh, in voxel units. Colours are linear.</summary>
public sealed class MeshBuffers
{
    /// <summary>xyz per vertex.</summary>
    public float[] Pos;
    public float[] Nrm;
    /// <summary>Linear rgb plus the material's colour variance in alpha.</summary>
    public float[] Col;
    /// <summary>Corner shade (0..1) and glow strength.</summary>
    public float[] Uv;
    public int[] Idx;
    public int Quads;
    public int Vertices => Quads * 4;
}

/// <summary>
/// Greedy mesher with per-corner shading, the same algorithm as the browser prototype.
/// Not thread-safe: give every worker its own instance.
/// </summary>
public sealed class GreedyMesher
{
    static readonly float[] CornerShade = { .38f, .58f, .8f, 1f };

    int[] _mask = new int[VoxelWorld.CS * VoxelWorld.CS];
    float[] _pos = Array.Empty<float>(), _nrm = Array.Empty<float>(), _col = Array.Empty<float>(), _uv = Array.Empty<float>();
    int[] _idx = Array.Empty<int>();
    int _q, _capQ;

    public GreedyMesher() { Grow(2048); }

    void Grow(int capQ)
    {
        Array.Resize(ref _pos, capQ * 12);
        Array.Resize(ref _nrm, capQ * 12);
        Array.Resize(ref _col, capQ * 16);
        Array.Resize(ref _uv, capQ * 8);
        Array.Resize(ref _idx, capQ * 6);
        _capQ = capQ;
    }

    /// <summary>A world chunk, or null when nothing of it can be seen.</summary>
    public MeshBuffers MeshChunk(VoxelWorld w, int ci, byte[] pad)
    {
        if (w.IsEmpty(ci) || w.IsBuried(ci)) return null;
        w.CopyPadded(ci, pad);
        w.ChunkCoords(ci, out int cx, out int cy, out int cz);
        var mb = Mesh(pad, VoxelWorld.CS, VoxelWorld.CS, VoxelWorld.CS, cx * VoxelWorld.CS, cy * VoxelWorld.CS, cz * VoxelWorld.CS);
        return mb.Quads > 0 ? mb : null;
    }

    /// <summary>
    /// Meshes a block of dx·dy·dz voxels that comes padded with one voxel of neighbours on every side:
    /// index ((y+1)·(dz+2) + (z+1))·(dx+2) + (x+1). Output positions are offset by (ox, oy, oz).
    /// </summary>
    public MeshBuffers Mesh(byte[] grid, int dx, int dy, int dz, int ox, int oy, int oz)
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
                            if (m == 0) { mask[n] = 0; continue; }
                            int q = idx + qoff;
                            if (grid[q] != 0) { mask[n] = 0; continue; }
                            int s1u = grid[q + su] != 0 ? 1 : 0, s0u = grid[q - su] != 0 ? 1 : 0;
                            int s1v = grid[q + sv] != 0 ? 1 : 0, s0v = grid[q - sv] != 0 ? 1 : 0;
                            int c00 = grid[q - su - sv] != 0 ? 1 : 0, c10 = grid[q + su - sv] != 0 ? 1 : 0;
                            int c11 = grid[q + su + sv] != 0 ? 1 : 0, c01 = grid[q - su + sv] != 0 ? 1 : 0;
                            int a0 = (s0u & s0v) != 0 ? 0 : 3 - (s0u + s0v + c00);
                            int a1 = (s1u & s0v) != 0 ? 0 : 3 - (s1u + s0v + c10);
                            int a2 = (s1u & s1v) != 0 ? 0 : 3 - (s1u + s1v + c11);
                            int a3 = (s0u & s1v) != 0 ? 0 : 3 - (s0u + s1v + c01);
                            mask[n] = m | (a0 << 8) | (a1 << 10) | (a2 << 12) | (a3 << 14);
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

        return new MeshBuffers
        {
            Quads = _q,
            Pos = _pos.AsSpan(0, _q * 12).ToArray(),
            Nrm = _nrm.AsSpan(0, _q * 12).ToArray(),
            Col = _col.AsSpan(0, _q * 16).ToArray(),
            Uv = _uv.AsSpan(0, _q * 8).ToArray(),
            Idx = _idx.AsSpan(0, _q * 6).ToArray(),
        };
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
