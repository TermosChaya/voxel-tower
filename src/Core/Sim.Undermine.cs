using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Digging as a careful hand digs. Nobody digs out the ground under its own feet: that is dug from beside. Nobody digs
/// at the footing of masonry or timber it stands under. The last of the ground holding something built up (found by
/// following what is built, as the rules of what holds what up have it) is not simply dug out from under it: the
/// digger calls out, those of ours about it walk out of the ground it will come down on and scatter over, and only then
/// is it undercut; the digger steps back out of the way with them, and it gives way under the weight a few seconds
/// later. One shut in at the bottom of a pit it dug cuts steps up out of it at once, and the rest in the pit use them
/// (Sim.Escape.cs). Our countermine is dug no further than its last frame of props: without logs, the rest of the bore
/// waits, and a step left bare is shored as soon as there are logs again (Sim.Tunnels.cs).
/// </summary>
public sealed partial class Sim
{
    /// <summary>Seconds an undercut footing holds before it gives way under what it bears.</summary>
    const double FootingHolds = 3;
    /// <summary>The footings undercut and about to give way: where, what they are, when, by whom, the ground kept clear.</summary>
    readonly List<(int x, int y, int z, byte m, double at, Unit by, Danger zone)> _footings = new();
    readonly HashSet<int> _footingsAt = new();
    /// <summary>What a dig is about to bring down, worked out before its last blow (the same box each time it is weighed).</summary>
    readonly List<FellPlan> _drops = new();
    readonly HashSet<int> _dropSeen = new();
    double _footingSaid = -99;

    /// <summary>
    /// The voxel is under the feet of the one digging: in its column, below its feet. Dug from beside instead, so that
    /// nobody digs itself down into a hole.
    /// </summary>
    static bool UnderOwnFeet(Unit u, int x, int y, int z, int feet) =>
        y < feet && MathF.Abs(x + .5f - u.X) < u.T.BodyR + 1 && MathF.Abs(z + .5f - u.Z) < u.T.BodyR + 1;

    /// <summary>Whether anything built hangs over one's head: masonry or timber in its column, up to three metres over it.</summary>
    bool BuiltOverhead(Unit u)
    {
        int x = (int)u.X, z = (int)u.Z, y0 = (int)MathF.Round(u.Y + u.T.BodyH), y1 = Math.Min(World.SY, y0 + 3 * VoxelWorld.PerMetre);
        for (int y = y0; y < y1; y++)
            if (World.InBounds(x, y, z) && Bearing(World.Get(x, y, z))) return true;
        return false;
    }

    /// <summary>Something built and bearing (not a tree, not leaves): what comes down when what it rests on goes.</summary>
    static bool Bearing(byte m) => Holds(m) && !Mat.IsTrunk(m) && !Mat.IsLeaf(m);

    /// <summary>Whether something built rests on a voxel, or is set beside it: over it, or at one of its sides.</summary>
    bool BearsBuilt(int x, int y, int z)
    {
        for (int d = 0; d < 6; d++)
        {
            if (d == 3) continue;
            var (nx, ny, nz) = Step6(x, y, z, d);
            if (World.InBounds(nx, ny, nz) && Bearing(World.Get(nx, ny, nz))) return true;
        }
        return false;
    }

    /// <summary>
    /// Whether taking out this voxel brings down something built that rests on it: what is built and joined to it that,
    /// without it, no longer rests on the ground anywhere (a tree is felled, not dug out: Sim.Felling.cs). The box it
    /// would come down on and scatter over: its own breadth and, round it, three metres and half its height more (what breaks off it flies).
    /// </summary>
    bool WouldDrop(int x, int y, int z, out Danger zone, out float cx, out float cz, out int y0)
    {
        zone = null; cx = cz = 0; y0 = 0;
        int cut = WaterPlanner.Pack(x, y, z);
        _dropSeen.Clear();
        for (int d = 0; d < 6; d++)
        {
            if (d == 3) continue;
            var (sx, sy, sz) = Step6(x, y, z, d);
            if (!World.InBounds(sx, sy, sz) || !Bearing(World.Get(sx, sy, sz))) continue;
            int s = WaterPlanner.Pack(sx, sy, sz);
            if (_dropSeen.Contains(s)) continue;
            var seen = new HashSet<int> { s };
            _fellQueue.Clear();
            _fellQueue.Enqueue(s);
            bool grounded = false, tree = false;
            // as the rules of what holds what up have it (Sim.Collapse.cs Adrift)
            while (_fellQueue.Count > 0 && !grounded)
            {
                var (px, py, pz) = WaterPlanner.Unpack(_fellQueue.Dequeue());
                byte here = World.Get(px, py, pz);
                for (int e = 0; e < 6; e++)
                {
                    var (qx, qy, qz) = Step6(px, py, pz, e);
                    if (qy < 0) { grounded = true; break; }
                    if (!World.InBounds(qx, qy, qz)) continue;
                    int q = WaterPlanner.Pack(qx, qy, qz);
                    if (q == cut) continue;
                    byte m = World.Get(qx, qy, qz);
                    if (e == 3 ? Ground(m) || m == Mat.Water && Floats(here) : e != 2 && HoldsAside(here, m)) { grounded = true; break; }
                    if (!Bound(m)) continue;
                    if (Mat.IsTrunk(m)) tree = true;
                    if (seen.Add(q)) _fellQueue.Enqueue(q);
                }
                // too much to follow: so great a work is held, as far as anyone digging under it can tell
                if (seen.Count > 30000) { grounded = true; break; }
            }
            foreach (int p in seen) _dropSeen.Add(p);
            if (grounded || tree || seen.Count < 30) continue;
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue, top = int.MinValue;
            y0 = int.MaxValue;
            foreach (int p in seen)
            {
                var (px, py, pz) = WaterPlanner.Unpack(p);
                x0 = Math.Min(x0, px); x1 = Math.Max(x1, px); z0 = Math.Min(z0, pz); z1 = Math.Max(z1, pz);
                y0 = Math.Min(y0, py); top = Math.Max(top, py);
            }
            float round = 3 * VoxelWorld.PerMetre + (top - y0) * .5f;
            cx = (x0 + x1 + 1) * .5f; cz = (z0 + z1 + 1) * .5f;
            zone = new Danger
            {
                PX = cx, PZ = cz, Len = (x1 - x0 + 1) * .5f + round, Half = (z1 - z0 + 1) * .5f + round,
                Y0 = y0 - 4 * VoxelWorld.PerMetre, Y1 = top + 2 * VoxelWorld.PerMetre, Why = "отошёл: сейчас рухнет",
            };
            return true;
        }
        return false;
    }

    /// <summary>
    /// Before a blow at the ground (or masonry) something built rests on: if it is the last that holds it up, those of
    /// ours it would come down on are sent out of the way first, then the digger calls out, undercuts it and steps back
    /// itself; it gives way a few seconds after (<see cref="TickUndercut"/>). True while the blow is held, or once the
    /// footing is undercut instead of dug out.
    /// </summary>
    bool DropHold(Unit u, (int x, int y, int z, byte m) j)
    {
        // (what is built is taken down as builders do it, from the top: Sim.Builders.cs)
        if (Mat.IsPlayer[World.Get(j.x, j.y, j.z)] || !BearsBuilt(j.x, j.y, j.z)) return false;
        int key = WaterPlanner.Pack(j.x, j.y, j.z);
        if (_footingsAt.Contains(key)) return true;
        if (u.FellHold && u.FellLooked == key && Time < u.FellLookedUntil) return true;
        u.FellLooked = key; u.FellLookedUntil = Time + .5;
        u.FellHold = false;
        if (!WouldDrop(j.x, j.y, j.z, out var fresh, out float cx, out float cz, out int y0)) return false;
        FellPlan plan = null;
        foreach (var p in _drops)
            if (MathF.Abs(p.PX - cx) < 12 && MathF.Abs(p.PZ - cz) < 12 && Math.Abs(p.Base - y0) < 16 && Time - p.Made < 120) { plan = p; break; }
        if (plan == null)
        {
            plan = new FellPlan { PX = cx, PZ = cz, Base = y0, Made = Time, Lane = fresh };
            _drops.Add(plan);
        }
        var zone = plan.Lane;
        if (!Dangers.Contains(zone)) Dangers.Add(zone);
        zone.By = u;
        zone.Until = Math.Max(zone.Until, Time + 6);
        if (plan.Warned < 0) { plan.Warned = Time; Shouted?.Invoke(u, "Берегись!"); }
        // (none of ours left under it, however long they take)
        int stay = ClearLane(zone, u);
        if (stay > 0)
        {
            u.Why = "ждёт, пока отойдут: сейчас рухнет";
            u.FellHold = true;
            return true;
        }
        // undercut: it gives way a moment after, and the digger is out of the way by then
        _footings.Add((j.x, j.y, j.z, World.Get(j.x, j.y, j.z), Time + FootingHolds, u, zone));
        _footingsAt.Add(key);
        zone.Until = Math.Max(zone.Until, Time + FootingHolds + 6);
        zone.By = null;
        _drops.Remove(plan);
        if (zone.Has(u.X, u.Y, u.Z, u.T.BodyR + 2) && u != Possessed) { u.Avoiding = zone; u.Path = null; u.NextSearch = 0; }
        if (Time - _footingSaid > 30) { _footingSaid = Time; Say($"{u.T.Name} подкопал последнюю опору постройки и отходит: сейчас рухнет.", 1, u); }
        u.FellHold = true;
        return true;
    }

    /// <summary>The footings undercut give way when their time comes: dug out as by the one who undercut them.</summary>
    void TickUndercut()
    {
        for (int n = _footings.Count - 1; n >= 0; n--)
        {
            var (x, y, z, m, at, by, zone) = _footings[n];
            if (Time < at) continue;
            _footings.RemoveAt(n);
            _footingsAt.Remove(WaterPlanner.Pack(x, y, z));
            byte cur = World.Get(x, y, z);
            if (cur != m || !Orders.IsRemove(Orders.At(x, y, z)) || !Labour.Diggable(cur)) continue;
            ClearLane(zone, null);
            DigAway(x, y, z, cur, by is { Dead: false } ? by.T.Yield : 1);
        }
        if (_drops.Count > 0) _drops.RemoveAll(p => Time - p.Made > 120);
    }

    /// <summary>A voxel dug out: the ground open there, what it gives in the stores.</summary>
    void DigAway(int x, int y, int z, byte cur, float yield)
    {
        World.Set(x, y, z, Mat.Air);
        Orders.Clear(x, y, z);
        Dug(x, y, z, cur);
        int kind = Labour.YieldKind(cur);
        if (kind != Stockpile.None) Stock[kind] += Labour.YieldShare(cur) * yield * YieldMore;
        if (cur == Mat.Ore) Stock.Gold += Labour.OreGold * yield;
        if (Mat.IsIronOre(cur)) StruckOre(cur, Labour.YieldShare(cur) * yield * YieldMore);
        Mined?.Invoke(x, y, z, cur);
    }

    /// <summary>
    /// Whether one stands at the bottom of a pit: the ground three metres off higher than it climbs, most of the way
    /// round.
    /// </summary>
    bool InPit(Unit u) => PitAt(u.X, u.Z, Nav.Floor[u.Cell], (u.T.Move.Climb + 1) * NavGrid.CS);

    bool PitAt(float px, float pz, int floor, int over)
    {
        int higher = 0;
        foreach (var (dx, dz) in Ways8)
        {
            int x = (int)(px + dx * 3 * VoxelWorld.PerMetre), z = (int)(pz + dz * 3 * VoxelWorld.PerMetre);
            if (World.InBounds(x, 2, z) && World.SurfAt(x, z) >= floor + over) higher++;
        }
        return higher >= 6;
    }

    /// <summary>Scaffolding put up out of a pit: it stays, the pit's way out for whoever works in it after.</summary>
    bool OutOfPit(Scaffold s)
    {
        var c = s.Columns[0];
        return PitAt((c.i << NavGrid.Shift) + 4, (c.k << NavGrid.Shift) + 4, c.y0 << NavGrid.Shift, 2 * NavGrid.CS);
    }

    /// <summary>Where a worker would stand to work is in a danger not its own: a tree about to come down, something built.</summary>
    bool SiteInDanger(Unit u)
    {
        if (Dangers.Count == 0 || u.Site < 0) return false;
        float x = (u.Site % Nav.NX << NavGrid.Shift) + 4, z = (u.Site / Nav.NX << NavGrid.Shift) + 4;
        foreach (var d in Dangers) if (d.By != u && d.Has(x, u.Y, z, u.T.BodyR + 2)) return true;
        return false;
    }

    // ------------------------------------------------------------ our countermine, no further than its props

    readonly HashSet<int> _boreHeld = new();
    readonly Dictionary<int, int> _boreHeldFrom = new();

    /// <summary>Whether a voxel marked is of our countermine past a step without props: it waits for logs.</summary>
    bool BoreHeld(int x, int y, int z) => _boreHeld.Count > 0 && _boreHeld.Contains(WaterPlanner.Pack(x, y, z));

    /// <summary>
    /// The steps of our countermine from <paramref name="from"/> on wait (-1: none do): kept for the work to pass over;
    /// gathered again only when that changes.
    /// </summary>
    void HoldBore(Tunnel t, int from)
    {
        if (_boreHeldFrom.GetValueOrDefault(t.Id, -1) == from) return;
        if (from < 0) _boreHeldFrom.Remove(t.Id); else _boreHeldFrom[t.Id] = from;
        _boreHeld.Clear();
        foreach (var o in Tunnels)
            if (o.Team == Team.Player && _boreHeldFrom.TryGetValue(o.Id, out int f))
                for (int s = f; s < o.Cells.Count; s++)
                    foreach (var (x, y, z) in Step(o.Cells, s)) _boreHeld.Add(WaterPlanner.Pack(x, y, z));
    }
}
