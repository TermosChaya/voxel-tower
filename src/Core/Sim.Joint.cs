using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// A tower built into a long wall is too great a body with the wall to be weighed as one (<see cref="BodyMost"/>), and
/// was held by its courses only: undermined, it crumbled where it was dug out and stood on. Now it is found by itself,
/// cut from the wall where the two are joined — the narrowest place between them, seen from above: what is left of all
/// that is built about it (within the box a body is looked for in) when everything thinner than half the tower is worn
/// away, grown back as far as it was worn; the wall's courses beside that cut are the joint. Weighed so, the tower
/// cracks along its joint as it leans, and goes over by itself, tearing out of the wall, whose broken end stays.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The body just found was cut from what it is built into, at <see cref="_joint"/>.</summary>
    bool _byJoint;
    /// <summary>The voxels of what the body was cut from that touch it: the joint, which cracks and tears.</summary>
    readonly List<int> _joint = new();
    readonly List<int> _jointSeen = new();
    int[] _cols = Array.Empty<int>(), _depth = Array.Empty<int>(), _mark = Array.Empty<int>();
    readonly Queue<int> _colQueue = new();

    /// <summary>The most built voxels looked over about a tower for its joint.</summary>
    const int JointMost = 100000;
    /// <summary>The fewest voxels from its edge to its middle (seen from above) a part has to be a tower of its own.</summary>
    const int TowerLeast = 6;

    /// <summary>
    /// The tower a built voxel belongs to, cut from the wall it is built into: null when there is none (what is built
    /// about it is all as thin as a wall, or more than <see cref="JointMost"/>, or the stout part reaches the edge of the
    /// box). <paramref name="seen"/> is the search's number, for what it saw not to be searched again when it fails.
    /// </summary>
    Bulk? Joint(int sx, int sy, int sz, HashSet<int> taken, out int seen)
    {
        const int N = 2 * Box + 1;
        int gen = ++_gen, ox = sx - Box, oz = sz - Box;
        seen = gen;
        _jointSeen.Clear(); _bodyStack.Clear();
        _bodyStack.Push(WaterPlanner.Pack(sx, sy, sz)); _stamp[StampAt(ox, oz, sx, sy, sz)] = gen;
        if (_cols.Length < N * N) { _cols = new int[N * N]; _depth = new int[N * N]; _mark = new int[N * N]; }
        Array.Clear(_cols, 0, N * N);
        // all that is built and joined to it within the box, and how much of it stands in each column
        while (_bodyStack.Count > 0)
        {
            int p = _bodyStack.Pop();
            if (_jointSeen.Count >= JointMost) return null;
            _jointSeen.Add(p);
            var (x, y, z) = WaterPlanner.Unpack(p);
            _cols[(z - oz) * N + x - ox]++;
            for (int d = 0; d < 6; d++)
            {
                var (nx, ny, nz) = Step6(x, y, z, d);
                if (!World.InBounds(nx, ny, nz) || !Rigid(World.Get(nx, ny, nz))) continue;
                int i = StampAt(ox, oz, nx, ny, nz);
                if (i < 0 || _stamp[i] == gen) continue;
                int q = WaterPlanner.Pack(nx, ny, nz);
                if (taken.Contains(q)) continue;
                _stamp[i] = gen;
                _bodyStack.Push(q);
            }
        }
        // how far each column is from the edge of what is built, seen from above (the box's edge counting as an edge)
        const int Far = 1 << 20;
        for (int c = 0; c < N * N; c++) _depth[c] = _cols[c] > 0 ? Far : 0;
        for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                int c = z * N + x;
                if (_depth[c] == 0) continue;
                int d = x == 0 || z == 0 ? 1 : Far;
                if (x > 0) d = Math.Min(d, _depth[c - 1] + 1);
                if (z > 0) d = Math.Min(d, Math.Min(_depth[c - N] + 1, Math.Min(x > 0 ? _depth[c - N - 1] + 1 : Far, x < N - 1 ? _depth[c - N + 1] + 1 : Far)));
                _depth[c] = d;
            }
        for (int z = N - 1; z >= 0; z--)
            for (int x = N - 1; x >= 0; x--)
            {
                int c = z * N + x;
                if (_depth[c] == 0) continue;
                int d = _depth[c];
                if (x == N - 1 || z == N - 1) d = 1;
                if (x < N - 1) d = Math.Min(d, _depth[c + 1] + 1);
                if (z < N - 1) d = Math.Min(d, Math.Min(_depth[c + N] + 1, Math.Min(x < N - 1 ? _depth[c + N + 1] + 1 : Far, x > 0 ? _depth[c + N - 1] + 1 : Far)));
                _depth[c] = d;
            }
        // the stoutest part near where it was found, and half its depth: what is thinner than that is wall
        int cx = sx - ox, cz = sz - oz, deepest = 0, best = -1, bestD = int.MaxValue;
        for (int z = Math.Max(0, cz - 32); z <= Math.Min(N - 1, cz + 32); z++)
            for (int x = Math.Max(0, cx - 32); x <= Math.Min(N - 1, cx + 32); x++)
                deepest = Math.Max(deepest, _depth[z * N + x]);
        if (deepest < TowerLeast) return null;
        int half = (deepest + 1) / 2;
        for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                if (_depth[z * N + x] < half) continue;
                int d = Math.Max(Math.Abs(x - cx), Math.Abs(z - cz));
                if (d < bestD) { bestD = d; best = z * N + x; }
            }
        if (best < 0 || bestD > 40) return null;
        // its stout part, and that grown back out as far as it was worn away: the tower
        int mark = gen;
        _colQueue.Clear();
        _mark[best] = mark; _colQueue.Enqueue(best);
        var core = new List<int>();
        while (_colQueue.Count > 0)
        {
            int c = _colQueue.Dequeue();
            core.Add(c);
            int x = c % N, z = c / N;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if ((uint)nx >= N || (uint)nz >= N) continue;
                    int n = nz * N + nx;
                    if (_mark[n] == mark || _depth[n] < half) continue;
                    _mark[n] = mark;
                    _colQueue.Enqueue(n);
                }
        }
        // grown back out: steps of a column, all eight ways, half the depth less one, and only over what is built
        var ring = new List<int>(core);
        for (int step = 0; step < half - 1; step++)
        {
            var next = new List<int>();
            foreach (int c in ring)
            {
                int x = c % N, z = c / N;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if ((uint)nx >= N || (uint)nz >= N) continue;
                        int n = nz * N + nx;
                        if (_mark[n] == mark || _cols[n] == 0) continue;
                        _mark[n] = mark;
                        next.Add(n);
                    }
            }
            ring = next;
        }
        // a tower out to the edge of the box is no tower cut from a wall
        for (int i = 0; i < N; i++)
            if (_mark[i] == mark || _mark[(N - 1) * N + i] == mark || _mark[i * N] == mark || _mark[i * N + N - 1] == mark) return null;
        // the body: what stands in the tower's columns; the joint: what of the rest touches it
        _body.Clear(); _stands.Clear(); _joint.Clear();
        var b = Empty();
        foreach (int p in _jointSeen)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            if (_mark[(z - oz) * N + x - ox] == mark) Take(ref b, p, x, y, z);
        }
        if (_body.Count < BodyLeast || _body.Count > BodyMost || _body.Count == _jointSeen.Count) return null;
        foreach (int p in _jointSeen)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            if (_mark[(z - oz) * N + x - ox] == mark) continue;
            for (int d = 0; d < 6; d++)
            {
                var (nx, ny, nz) = Step6(x, y, z, d);
                int c = (nz - oz) * N + nx - ox;
                if ((uint)(nx - ox) < N && (uint)(nz - oz) < N && _mark[c] == mark && Rigid(World.Get(nx, ny, nz))) { _joint.Add(p); break; }
            }
        }
        foreach (int q in _body) taken.Add(q);
        _byJoint = true;
        return Finish(ref b);
    }

    /// <summary>
    /// A tower leaning in the wall it is built into: the joint carries what the tower's foot cannot, and cracks the more,
    /// the further the foot is gone (<paramref name="worst"/>: the most any voxel it stands on carries over what it bears).
    /// </summary>
    void StrainJoint(float worst)
    {
        if (!_byJoint || worst <= 1) return;
        bool ours = false;
        float sx = 0, sz = 0;
        foreach (int p in _joint)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            byte m = World.Get(x, y, z);
            float down = BearsDown(m);
            if (down <= 0) continue;
            Strain(p, down * Math.Min(2.5f, .6f + .5f * worst), down, body: true);
            if (Mat.IsPlayer[m] && !ours) { ours = true; sx = x; sz = z; }
        }
        if (ours && Time - _jointSaid > 30)
        {
            _jointSaid = Time;
            Say("Башня отходит от стены по стыку. Засыпьте подкоп или подоприте её.", 3, sx, sz);
        }
    }

    double _jointSaid = -99;

    /// <summary>A tower going over out of the wall it was built into: its joint tears, the wall's broken end left ragged.</summary>
    void TearJoint()
    {
        if (!_byJoint) return;
        foreach (int p in _joint)
        {
            if (Rng.NextDouble() > .6) continue;
            var (x, y, z) = WaterPlanner.Unpack(p);
            byte m = World.Get(x, y, z);
            if (BearsDown(m) <= 0) continue;
            _strained.Remove(p);
            if (Cracks.Remove(p)) CracksVersion++;
            World.Set(x, y, z, Mat.Air);
            if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
            if (Rng.NextDouble() < .3) Fell?.Invoke(x, y, z, m);
        }
        _byJoint = false;
    }
}
