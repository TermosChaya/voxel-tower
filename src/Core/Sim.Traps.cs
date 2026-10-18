using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The traps the brushes lay. Stakes: whoever is among them goes slowly and is torn, and treads one down now and then.
/// A trap floor: thin poles under turf laid flush over a pit; it holds our people, who know where to tread, and gives
/// way under the horde, which goes down into the pit onto whatever waits at its bottom. Stones on the battlements: when
/// a raider comes to the foot of the wall under them and one of ours stands by them, he tips them over onto it, and
/// they hit whoever is under them on the way down.
/// </summary>
public sealed partial class Sim
{
    double _nextTrapLook;
    double _trapSaid = -99;
    /// <summary>Stones laid to be thrown down, by metre column (i | k &lt;&lt; 10): the voxels of them in it, packed.</summary>
    readonly Dictionary<int, HashSet<int>> _dropStones = new();

    /// <summary>What a second among stakes costs whoever is in them, and the chance of treading one down in it.</summary>
    const float StakeHurt = 4, StakeTrample = .6f;
    /// <summary>How far off one of ours may stand from stones and still tip them over, in voxels.</summary>
    const float TipReach = 5 * VoxelWorld.PerMetre;

    static int StoneColumn(int x, int z) => (x >> NavGrid.Shift) | (z >> NavGrid.Shift) << 10;

    /// <summary>Keeps the stones to throw down in mind as they are laid and taken away.</summary>
    void OnTrapEdit(int x, int y, int z, byte old)
    {
        byte now = World.Get(x, y, z);
        if (old == now) return;
        if (old == Mat.DropStone && _dropStones.TryGetValue(StoneColumn(x, z), out var set))
        {
            set.Remove(WaterPlanner.Pack(x, y, z));
            if (set.Count == 0) _dropStones.Remove(StoneColumn(x, z));
        }
        if (now == Mat.DropStone)
        {
            if (!_dropStones.TryGetValue(StoneColumn(x, z), out set)) _dropStones[StoneColumn(x, z)] = set = new HashSet<int>();
            set.Add(WaterPlanner.Pack(x, y, z));
        }
    }

    /// <summary>After a game is read back: the stones to throw down found again in what play changed of the valley.</summary>
    void FindDropStones()
    {
        _dropStones.Clear();
        for (int ci = 0; ci < World.ChunkCount; ci++)
        {
            if (!World.Changed(ci)) continue;
            var d = World.ChunkData(ci);
            if (d == null) continue;
            World.ChunkCoords(ci, out int cx, out int cy, out int cz);
            for (int i = 0; i < d.Length; i++)
                if (d[i] == Mat.DropStone)
                    OnTrapEdit((cx << VoxelWorld.Shift) + (i & 31), (cy << VoxelWorld.Shift) + (i >> 10), (cz << VoxelWorld.Shift) + ((i >> 5) & 31), Mat.Air);
        }
    }

    /// <summary>Four times a second: the stakes tear whoever is in them, trap floors give way, stones are thrown down.</summary>
    void TickTraps()
    {
        if (Time < _nextTrapLook) return;
        const float Look = .25f;
        _nextTrapLook = Time + Look;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside) continue;
            if (Nav.Spikes[u.Cell] > 0) AmongStakes(u, Look);
            if (u.Team == Team.Horde && !u.Bound && !Climbing(u)) OnTrapFloor(u);
        }
        if (_dropStones.Count > 0) ThrowStones();
    }

    /// <summary>Among stakes: torn a little, and now and then one trodden down.</summary>
    void AmongStakes(Unit u, float dt)
    {
        Hurt(u, null, StakeHurt * StakeHarm * dt, "колья");
        if (Rng.NextDouble() >= StakeTrample * dt) return;
        // the stake nearest it goes down, the whole of it
        int bx = -1, bz = -1; float bd = float.MaxValue;
        int x0 = Nav.I(u.Cell) << NavGrid.Shift, y0 = Nav.Y(u.Cell) << NavGrid.Shift, z0 = Nav.K(u.Cell) << NavGrid.Shift;
        for (int z = z0; z < z0 + NavGrid.CS; z++)
            for (int x = x0; x < x0 + NavGrid.CS; x++)
            {
                bool stake = false;
                for (int y = y0 - 2; y < y0 + NavGrid.CS && !stake; y++) stake = World.Get(x, y, z) == Mat.Stakes;
                if (!stake) continue;
                float d = (x - u.X) * (x - u.X) + (z - u.Z) * (z - u.Z);
                if (d < bd) { bd = d; bx = x; bz = z; }
            }
        if (bx < 0) return;
        for (int y = y0 - 2; y < y0 + 2 * NavGrid.CS; y++)
            if (World.Get(bx, y, bz) == Mat.Stakes)
            {
                World.Set(bx, y, bz, Mat.Air);
                Fell?.Invoke(bx, y, bz, Mat.Stakes);
            }
    }

    /// <summary>A raider on a trap floor: the floor gives way under it, all that is joined to the spot within three metres.</summary>
    void OnTrapFloor(Unit u)
    {
        int fy = (int)MathF.Round(u.Y) - 1;
        float r = u.T.BodyR * .7f;
        foreach (var (ox, oz) in new[] { (0f, 0f), (r, 0f), (-r, 0f), (0f, r), (0f, -r) })
        {
            int x = (int)(u.X + ox), z = (int)(u.Z + oz);
            for (int y = fy; y >= fy - 1; y--)
                if (World.Get(x, y, z) == Mat.TrapDeck) { Spring(x, y, z); return; }
        }
    }

    /// <summary>The trap floor gives way: what of it is joined to the spot, within three metres, breaks and falls in.</summary>
    public int Spring(int x, int y, int z)
    {
        const int Reach = 3 * VoxelWorld.PerMetre;
        var queue = new Queue<(int x, int y, int z)>();
        var seen = new HashSet<int> { WaterPlanner.Pack(x, y, z) };
        queue.Enqueue((x, y, z));
        int n = 0, bottom = int.MaxValue;
        while (queue.Count > 0 && n < 4000)
        {
            var (cx, cy, cz) = queue.Dequeue();
            if (World.Get(cx, cy, cz) != Mat.TrapDeck) continue;
            World.Set(cx, cy, cz, Mat.Air);
            if (Orders.At(cx, cy, cz) != 0) Orders.Clear(cx, cy, cz);
            Burning.Remove(WaterPlanner.Pack(cx, cy, cz));
            if (Rng.NextDouble() < .3) Fell?.Invoke(cx, cy, cz, Mat.TrapDeck);
            n++;
            for (int d = 0; d < 6; d++)
            {
                int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = cy + (d == 2 ? 1 : d == 3 ? -1 : 0), nz = cz + (d == 4 ? 1 : d == 5 ? -1 : 0);
                if (Math.Abs(nx - x) > Reach || Math.Abs(nz - z) > Reach || !World.InBounds(nx, ny, nz)) continue;
                if (seen.Add(WaterPlanner.Pack(nx, ny, nz)) && World.Get(nx, ny, nz) == Mat.TrapDeck) queue.Enqueue((nx, ny, nz));
            }
        }
        if (n == 0) return 0;
        // how far down the pit goes under the spot: the raiders on it go down as far, and are hurt for it
        int floor = y;
        while (floor > 1 && World.Get(x, floor - 1, z) is var b && (b == Mat.Air || Mat.Passable[b] && b != Mat.Water)) floor--;
        bottom = floor;
        float drop = (y - bottom) / (float)VoxelWorld.PerMetre;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Bound || o.Team != Team.Horde || Math.Abs(o.X - x) > Reach || Math.Abs(o.Z - z) > Reach || Math.Abs(o.Y - (y + 1)) > 3) continue;
            // a long fall is the fall's to hurt (see Settle); into a pit it is the trap's
            if (drop >= 1 && drop < 4) Hurt(o, null, 9 * drop, "волчья яма");
            o.Path = null;
        }
        Nav.Flush();
        Collapsed?.Invoke(n, x, y, z);
        if (Time - _trapSaid > 6) { _trapSaid = Time; Say("Ловчий настил провалился под ордой.", 1, x, z); }
        return n;
    }

    /// <summary>
    /// A raider at the foot of a wall under stones laid on it to be thrown down, and one of ours up there by them: he
    /// tips them over onto it.
    /// </summary>
    void ThrowStones()
    {
        List<int> stones = null;
        foreach (var u in Units)
        {
            if (u.Dead || u.Team != Team.Horde || u.Bound || u.Inside) continue;
            int ci = (int)u.X >> NavGrid.Shift, ck = (int)u.Z >> NavGrid.Shift;
            float head = u.Y + u.T.BodyH;
            stones?.Clear();
            for (int dk = -1; dk <= 1; dk++)
                for (int di = -1; di <= 1; di++)
                {
                    if (!_dropStones.TryGetValue((ci + di) | (ck + dk) << 10, out var set)) continue;
                    foreach (int p in set)
                    {
                        var (_, y, _) = WaterPlanner.Unpack(p);
                        if (y > head + 4 && y < u.Y + 16 * VoxelWorld.PerMetre) (stones ??= new List<int>()).Add(p);
                    }
                }
            if (stones == null || stones.Count == 0) continue;
            float sx = 0, sy = 0, sz = 0;
            foreach (int p in stones) { var (x, y, z) = WaterPlanner.Unpack(p); sx += x; sy += y; sz += z; }
            sx /= stones.Count; sy /= stones.Count; sz /= stones.Count;
            // one of ours up there by them to tip them, and none of ours under them
            Unit hand = null; float hd = TipReach;
            bool ours = false;
            foreach (var o in Units)
            {
                if (o.Dead || o.Inside || o.Team != Team.Player) continue;
                float dx = o.X - sx, dz = o.Z - sz, d = MathF.Sqrt(dx * dx + dz * dz);
                if (MathF.Abs(o.Y - sy) < 3 * VoxelWorld.PerMetre && d < hd) { hd = d; hand = o; }
                if (o.Y < sy - 8 && MathF.Abs(o.X - u.X) < 16 && MathF.Abs(o.Z - u.Z) < 16) ours = true;
            }
            if (hand == null || ours) continue;
            Tip(stones, u, hand);
            return;
        }
    }

    /// <summary>
    /// Stones tipped over a wall onto a raider: each goes out over the face toward it and down its column, striking
    /// whoever is in the way, and lies at the foot as broken stone. One that the wall stands in the way of stays.
    /// </summary>
    void Tip(List<int> stones, Unit at, Unit hand)
    {
        float cx = 0, cz = 0;
        foreach (int p in stones) { var (x, _, z) = WaterPlanner.Unpack(p); cx += x; cz += z; }
        cx /= stones.Count; cz /= stones.Count;
        int ox = (int)MathF.Round(at.X - cx), oz = (int)MathF.Round(at.Z - cz);
        var under = Units.FindAll(o => !o.Dead && !o.Inside && MathF.Abs(o.X - at.X) < 32 && MathF.Abs(o.Z - at.Z) < 32);
        var hits = new Dictionary<Unit, int>();
        // the lowest first, so those above come down on them
        var order = new List<(int x, int y, int z)>();
        foreach (int p in stones) order.Add(WaterPlanner.Unpack(p));
        order.Sort((a, b) => a.y.CompareTo(b.y));
        int fell = 0; float ly0 = 0;
        _settling = true;
        foreach (var (x, y, z) in order)
        {
            int tx = x + ox, tz = z + oz;
            if (!World.InBounds(tx, y, tz) || (tx != x || tz != z) && !Brushes.Free(World.Get(tx, y, tz))) continue;
            World.Set(x, y, z, Mat.Air);
            if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
            var (lx, ly, lz) = Landing(tx, y, tz, false);
            if (ly < 0) continue;
            World.Set(lx, ly, lz, Mat.Rubble);
            fell++; ly0 += ly;
            if (Rng.NextDouble() < .3) Fell?.Invoke(lx, ly, lz, Mat.DropStone);
            foreach (var o in under)
            {
                if (o.Dead || MathF.Abs(o.X - tx - .5f) > o.T.BodyR + 1 || MathF.Abs(o.Z - tz - .5f) > o.T.BodyR + 1) continue;
                if (o.Y + o.T.BodyH < ly || o.Y > y) continue;
                hits[o] = hits.GetValueOrDefault(o) + 1;
            }
        }
        _settling = false;
        if (fell == 0) return;
        hand.Swing = 1;
        hand.Facing = MathF.Atan2(at.Z - hand.Z, at.X - hand.X);
        foreach (var (o, n) in hits) Hurt(o, hand, 25 + Math.Min(95, n * 3), "камни со стены");
        Collapsed?.Invoke(fell, at.X, ly0 / fell, at.Z);
        if (Time - _trapSaid > 6) { _trapSaid = Time; Say("Камни со стены — на головы орде.", 1, at.X, at.Z); }
    }
}
