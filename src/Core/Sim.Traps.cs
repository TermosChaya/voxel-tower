using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The traps the brushes lay. Stakes: whoever is among them goes slowly and is torn, and treads one down now and then.
/// A trap floor: thin poles under turf laid flush over a pit; it holds our people, who know where to tread, and gives
/// way under the horde, which goes down into the pit onto whatever waits at its bottom. Stones on the battlements: when
/// a raider comes to the foot of the wall under them and one of ours stands by them, he tips them over onto it: they go
/// over the face as round stones, strike whoever they come down on and roll on down the slope before the wall.
/// </summary>
public sealed partial class Sim
{
    double _nextTrapLook;
    double _trapSaid = -99;
    /// <summary>Stones and logs laid to be thrown down, by metre column (i | k &lt;&lt; 10): the voxels of them in it, packed.</summary>
    readonly Dictionary<int, HashSet<int>> _dropStones = new();

    /// <summary>What a second among stakes costs whoever is in them, and the chance of treading one down in it.</summary>
    const float StakeHurt = 4, StakeTrample = .6f;
    /// <summary>How far off one of ours may stand from stones and still tip them over, in voxels.</summary>
    const float TipReach = 5 * VoxelWorld.PerMetre;

    static int StoneColumn(int x, int z) => (x >> NavGrid.Shift) | (z >> NavGrid.Shift) << 10;

    /// <summary>Keeps the stones and logs to throw down in mind as they are laid and taken away.</summary>
    void OnTrapEdit(int x, int y, int z, byte old)
    {
        byte now = World.Get(x, y, z);
        if (old == now) return;
        if (old is Mat.DropStone or Mat.DropLog && _dropStones.TryGetValue(StoneColumn(x, z), out var set))
        {
            set.Remove(WaterPlanner.Pack(x, y, z));
            if (set.Count == 0) _dropStones.Remove(StoneColumn(x, z));
        }
        if (now is Mat.DropStone or Mat.DropLog)
        {
            if (!_dropStones.TryGetValue(StoneColumn(x, z), out set)) _dropStones[StoneColumn(x, z)] = set = new HashSet<int>();
            set.Add(WaterPlanner.Pack(x, y, z));
        }
    }

    /// <summary>After a game is read back: the stones and logs to throw down found again in what play changed of the valley.</summary>
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
                if (d[i] is Mat.DropStone or Mat.DropLog)
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
        if (Rng.NextDouble() >= StakeTrample * StakeHold * dt) return;
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

    /// <summary>The trap floor gives way: what of it is joined to the spot, within three metres, breaks and falls in
    /// (and the chronicle says so, as it is told).</summary>
    public int Spring(int x, int y, int z, string said = "Ловчий настил провалился под ордой.")
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
            if (drop >= 1 && drop < 4) Hurt(o, null, 9 * drop * PitHarm, "волчья яма");
            o.Path = null;
        }
        Nav.Flush();
        Collapsed?.Invoke(n, x, y, z);
        if (Time - _trapSaid > 6) { _trapSaid = Time; Say(said, 1, x, z); }
        return n;
    }

    /// <summary>
    /// A raider at the foot of a wall under stones or logs laid on it to be thrown down, and one of ours up there by them:
    /// he tips them over onto it.
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

    /// <summary>How many voxels of the heap go to each stone tipped over the wall: a ball of about two thirds of a metre.</summary>
    const int DropLump = 60;

    /// <summary>
    /// Stones and logs tipped over a wall onto a raider, by one of ours up there: each goes as it goes (see TipStones,
    /// TipLogs).
    /// </summary>
    void Tip(List<int> stones, Unit at, Unit hand)
    {
        var heap = new List<(int x, int y, int z)>(stones.Count);
        var logs = new List<(int x, int y, int z)>();
        foreach (int p in stones)
        {
            var v = WaterPlanner.Unpack(p);
            (World.Get(v.x, v.y, v.z) == Mat.DropLog ? logs : heap).Add(v);
        }
        int stonesGone = TipStones(heap, at, hand), logsGone = TipLogs(logs, at, hand);
        if (stonesGone + logsGone == 0) return;
        hand.Swing = 1;
        hand.Facing = MathF.Atan2(at.Z - hand.Z, at.X - hand.X);
        if (Time - _trapSaid > 6)
        {
            _trapSaid = Time;
            Say(logsGone == 0 ? "Камни со стены — на головы орде." : stonesGone == 0 ? "Брёвна со стены — на головы орде." : "Камни и брёвна со стены — на головы орде.", 1, at.X, at.Z);
        }
    }

    /// <summary>
    /// Stones tipped over a wall onto a raider: the heap goes over the face as round stones, a lump of it to each, out
    /// toward the raider and down onto it; they strike whoever they come down on and roll on down whatever slope is
    /// before the wall, through whoever is in their way. A stone the wall stands in the way of stays where it is.
    /// </summary>
    int TipStones(List<(int x, int y, int z)> heap, Unit at, Unit hand)
    {
        if (heap.Count == 0) return 0;
        // along the heap's length, a lump to a stone
        int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
        foreach (var (x, _, z) in heap) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z); }
        bool alongX = x1 - x0 >= z1 - z0;
        heap.Sort((a, b) => alongX ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));
        int lumps = Math.Max(1, (int)MathF.Round(heap.Count / (float)DropLump)), thrown = 0;
        for (int i = 0; i < lumps; i++)
        {
            int from = heap.Count * i / lumps, to = heap.Count * (i + 1) / lumps, n = to - from;
            if (n <= 0) continue;
            float cx = 0, cy = 0, cz = 0;
            for (int j = from; j < to; j++) { var (x, y, z) = heap[j]; cx += x + .5f; cy += y + .5f; cz += z + .5f; }
            cx /= n; cy /= n; cz /= n;
            float r = MathF.Max(1.6f, MathF.Cbrt(3 * n / (4 * MathF.PI)));
            var ball = Ball(r, Mat.DropStone);
            // out over the face (across the heap, on the raider's side), and on toward the raider, so as to come down on it
            float dx = at.X - cx, dz = at.Z - cz, bx = cx, bz = cz, by = cy + r * .5f;
            if (alongX) bz = (dz < 0 ? z0 : z1 + 1) + MathF.Sign(dz == 0 ? 1 : dz) * (r + .5f);
            else bx = (dx < 0 ? x0 : x1 + 1) + MathF.Sign(dx == 0 ? 1 : dx) * (r + .5f);
            float fall = MathF.Max(VoxelWorld.PerMetre, by - at.Y - at.T.BodyH * .5f), t = MathF.Sqrt(2 * fall / Gravity);
            float ox = at.X - bx, oz = at.Z - bz, od = MathF.Max(.01f, MathF.Sqrt(ox * ox + oz * oz));
            float out_ = Math.Clamp(od / t, VoxelWorld.PerMetre, 4 * VoxelWorld.PerMetre), ux = ox / od, uz = oz / od;
            if (Aloft + ball.Count > MaxAloft) continue;
            for (int j = from; j < to; j++)
            {
                var (x, y, z) = heap[j];
                World.Set(x, y, z, Mat.Air);
                if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
            }
            // the wall in its way: this one stays
            if (!BallFree(bx, by, bz, r))
            {
                for (int j = from; j < to; j++) { var (x, y, z) = heap[j]; World.Set(x, y, z, Mat.DropStone); }
                continue;
            }
            var f = Hurl(ball, bx, by, bz, ux * out_, .5f * VoxelWorld.PerMetre, uz * out_, "камни со стены");
            if (f != null) { f.By = hand; thrown++; }
        }
        return thrown;
    }

    /// <summary>
    /// Logs tipped over a wall onto a raider: the heap goes over the face in logs two voxels thick and up to four metres
    /// long, lying along the wall, pushed out just so far as to come down on the raider; they roll on down the slope
    /// across themselves through whoever is on it.
    /// </summary>
    int TipLogs(List<(int x, int y, int z)> heap, Unit at, Unit hand)
    {
        if (heap.Count == 0) return 0;
        int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, z0 = int.MaxValue, z1 = int.MinValue;
        foreach (var (x, y, z) in heap) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z); }
        bool alongX = x1 - x0 >= z1 - z0;
        float heapMid = alongX ? (z0 + z1 + 1) * .5f : (x0 + x1 + 1) * .5f;
        // into logs: two voxels up and across, four metres at most along the wall
        var logs = new Dictionary<(int, int, int), List<(int x, int y, int z)>>();
        foreach (var v in heap)
        {
            int along = alongX ? v.x - x0 : v.z - z0, across = alongX ? v.z - z0 : v.x - x0;
            var key = ((v.y - y0) / 2, across / 2, along / (4 * VoxelWorld.PerMetre));
            if (!logs.TryGetValue(key, out var list)) logs[key] = list = new List<(int x, int y, int z)>();
            list.Add(v);
        }
        int thrown = 0;
        foreach (var log in logs.Values)
        {
            if (Aloft + log.Count > MaxAloft) continue;
            float cx = 0, cy = 0, cz = 0;
            foreach (var (x, y, z) in log) { cx += x + .5f; cy += y + .5f; cz += z + .5f; }
            cx /= log.Count; cy /= log.Count; cz /= log.Count;
            // over the edge on the raider's side, and out as far as it is to the raider by the time it is down to it
            float side = (alongX ? at.Z : at.X) < heapMid ? -1 : 1, face = side < 0 ? (alongX ? z0 : x0) : (alongX ? z1 : x1) + 1;
            var f = Loose(log, 0, 0, 0, "брёвна со стены");
            if (f == null) continue;
            if (alongX) f.Z = face + side * 1.5f; else f.X = face + side * 1.5f;
            float across = alongX ? at.Z - f.Z : at.X - f.X;
            float fall = MathF.Max(VoxelWorld.PerMetre, cy - at.Y - at.T.BodyH * .5f), t = MathF.Sqrt(2 * fall / Gravity);
            float out_ = side * Math.Clamp(MathF.Abs(across) / t, .3f * VoxelWorld.PerMetre, 3 * VoxelWorld.PerMetre);
            if (alongX) f.VZ = out_; else f.VX = out_;
            f.VY = .5f * VoxelWorld.PerMetre;
            // turning over about its own length as it goes, the way it was rolled off
            (f.AX, f.AY, f.AZ) = alongX ? (side, 0f, 0f) : (0f, 0f, -side);
            f.Spin = MathF.Abs(out_) + VoxelWorld.PerMetre;
            f.By = hand; thrown++;
        }
        return thrown;
    }
}
