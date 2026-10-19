using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

public sealed partial class Sim
{
    // ------------------------------------------------------------ what the player marks out

    /// <summary>Marks a planned building; pawns lay it voxel by voxel from the stores.</summary>
    public int DesignateBuild(VoxelList L, bool arched = false)
    {
        // an arch stands as long as it touches the ground: what is laid of it is marked so
        if (arched && L != null) for (int i = 0; i < L.Count; i++) Arched.SetRaw(L.X(i), L.Y(i), L.Z(i), 1);
        int n = Orders.PlanBuild(L);
        if (InstantLabour) CompleteAll();
        return n;
    }

    /// <summary>Marks earth to dig, rock to quarry, trees to fell or own masonry to take down.</summary>
    public int DesignateRemove(VoxelList L)
    {
        int n = Orders.PlanRemove(L);
        if (InstantLabour) CompleteAll();
        return n;
    }

    public int CancelOrders(int x, int y, int z, double radius) => Orders.Cancel(x, y, z, radius);

    /// <summary>Takes back every order on the columns of a rectangle.</summary>
    public int CancelArea(int x0, int z0, int x1, int z1) => Orders.CancelArea(x0, z0, x1, z1);

    /// <summary>The fort a new game begins with: its area (to say how sound it is) and its gate.</summary>
    public (int x0, int z0, int x1, int z1) FortArea;
    public Gateway FortGate;

    /// <summary>Tests: every order done at once, as if by an army of workers with full stores.</summary>
    public void CompleteAll()
    {
        CountBodies();
        var L = Orders.Layer;
        for (int ci = 0; ci < L.ChunkCount; ci++)
        {
            var d = L.ChunkData(ci);
            if (d == null) continue;
            L.ChunkCoords(ci, out int cx, out int cy, out int cz);
            for (int i = 0; i < d.Length; i++)
            {
                byte o = d[i];
                if (o == 0) continue;
                int x = cx * 32 + (i & 31), z = cz * 32 + (i >> 5 & 31), y = cy * 32 + (i >> 10);
                if (Orders.IsRemove(o))
                {
                    byte was = World.Get(x, y, z);
                    World.Set(x, y, z, 0);
                    Orders.Clear(x, y, z);
                    Dug(x, y, z, was);
                    // rubble cleared out of a building's way: what was meant goes back at once
                    o = Orders.At(x, y, z);
                    if (o == 0 || _bodies[Nav.CellOf(x, y, z)] > 0) continue;
                }
                else if (_bodies[Nav.CellOf(x, y, z)] > 0) continue;
                if (Brushes.Free(World.Get(x, y, z))) { World.Set(x, y, z, o); Remember(x, y, z, o); }
                Orders.Clear(x, y, z);
            }
        }
        Nav.Flush();
    }

    // ------------------------------------------------------------ pawns

    void PawnTick(Unit u, float dt)
    {
        // one of the dead raised: it goes for the horde, and does nothing else
        if (u.T == Kinds.Risen) { RisenTick(u, dt); return; }
        // bound and led off by a thief: it goes where it is led till it is freed
        if (u.Captor != null) { Led(u, dt); return; }
        // standing in the flames: out of them before anything
        if (EscapeFire(u, dt)) { Busy(u); return; }
        // a sapper's fuse burning down: one runs to cut it, the rest near it get away from the wall
        if (u.Defusing != null && Defuse(u, dt)) { Busy(u); return; }
        if (u.Shunning != null && KeepClear(u, dt)) { Busy(u); return; }
        // badly hurt: out of the fight to mend by the citadel
        if (FallBack(u, dt)) { Busy(u); return; }
        // one that has lost the hand it fights with (or, with a bow, either hand) runs like a worker
        bool soldier = u.T.Work < 1, fighter = (soldier || u.Militia) && Armed(u);
        // a swordsman beside a siege ladder throws it down before anything else, climbers and all
        if (soldier && !u.T.Ranged && HasMainHand(u) && PushLadders(u, dt)) { Busy(u); return; }
        if (fighter && Fight(u, dt)) { Busy(u); ReleaseSite(u); return; }
        if (!fighter && Flee(u, dt)) { Busy(u); return; }
        // the alarm is up: to shelter (one the player sent somewhere goes where it was sent)
        if (!fighter && Alarm && !u.Hold && TakeShelter(u, dt)) { Busy(u); return; }
        if (u.Prey != null && HuntTick(u, dt)) { Busy(u); return; }
        // a priest prays by day and shelters by night, and does nothing else (unless the player sent it somewhere)
        if (u.T.Prays && !u.Hold) { PriestTick(u, dt); return; }
        // a comrade bleeding close by comes before any work (soldiers keep to their posts while the horde is here)
        if (!u.T.Prays && (!soldier || !Waves.Active) && Bandage(u, dt)) { Busy(u); return; }
        // a fire in what we built or round the citadel, if putting it out comes before the work at hand
        if (!u.T.Prays && (!soldier || !Waves.Active) && FightFire(u, dt)) { Busy(u); return; }
        // a raider bound out in the field: a free worker goes for it and leads it into the citadel
        if (!soldier && !u.T.Prays && FetchCaptive(u, dt)) { Busy(u); return; }
        if (soldier && HasMainHand(u) && PushLadders(u, dt)) { Busy(u); return; }
        if (u.Path != null && u.PathI < u.Path.Count && (u.Hold || u.Site < 0) && u.Pastime != Pastime.Stroll) { Walk(u, dt); return; }
        if (u.Hold)
        {
            u.Why = "";
            // the way broke (a gate shut in its face, something built across it): find it again now and then
            if (u.Dest >= 0 && u.Cell != u.Dest && Time >= u.NextSearch)
            {
                u.NextSearch = Time + 2;
                if (CellsApart(u.Cell, u.Dest) > 1 && (GoTo(u, u.Dest, budget: 100000) || GoTo(u, u.Dest, adjacent: true, budget: 100000))) { Busy(u); return; }
            }
            Idle(u, dt, stay: true);
            return;
        }
        if (soldier && (Waves.Active || Alarm))
        {
            // while the horde is in the valley, or the alarm is up, soldiers keep to their posts and look out
            ReleaseSite(u);
            if (u.Post >= 0 && u.Cell != u.Post && Time >= u.NextSearch) { u.NextSearch = Time + 2; Busy(u); GoTo(u, u.Post); }
            if (u.Path != null && u.Pastime != Pastime.Stroll) Walk(u, dt); else Idle(u, dt, stay: true);
            return;
        }
        WorkTick(u, dt);
    }

    void WorkTick(Unit u, float dt)
    {
        // no hand to hold a tool with: no work
        if (!HasMainHand(u))
        {
            ReleaseSite(u);
            u.Why = "без руки: работать не может";
            Idle(u, dt, stay: false);
            return;
        }
        if (u.Site < 0)
        {
            u.State = UnitState.Idle;
            if (Time >= u.NextSearch) FindWork(u);
            if (u.Site >= 0) { Busy(u); return; }
            Idle(u, dt, stay: false);
            return;
        }
        if (u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt); return; }
        int si = u.Site % Nav.NX, sk = u.Site / Nav.NX;
        if (Math.Abs(Nav.I(u.Cell) - si) > 1 || Math.Abs(Nav.K(u.Cell) - sk) > 1) { ReleaseSite(u); return; }
        if (u.JobI >= u.Jobs.Count)
        {
            Gather(u);
            if (u.Jobs.Count == 0)
            {
                // nothing to do from here after all (stores empty, or someone stands where the masonry goes):
                // leave this spot to others for a while and look elsewhere
                u.Unreachable[u.Site] = Time + (u.Why.StartsWith("нет") ? 8 : 4);
                ReleaseSite(u);
                u.NextSearch = Time + .2;
                return;
            }
        }
        u.State = UnitState.Work;
        // mourning one of their own given to the old powers, they work a quarter slower
        u.Effort += (float)(Labour.WorkPerSecond * u.T.Work * u.Pace * dt) * (Time < MournUntil ? .75f : 1);
        int guard = 0;
        while (u.JobI < u.Jobs.Count && guard++ < 256)
        {
            var j = u.Jobs[u.JobI];
            float cost = Orders.IsRemove(j.m) ? Labour.Dig(World.Get(j.x, j.y, j.z)) / DigPace : Labour.Lay(j.m) / LayPace;
            if (cost <= 0) cost = .25f;
            if (u.Effort < cost) break;
            u.JobI++;
            if (Do(u, j))
            {
                u.Effort -= cost;
                u.Swing = 1;
                u.Facing = MathF.Atan2(j.z + .5f - u.Z, j.x + .5f - u.X);
            }
        }
        u.Effort = Math.Min(u.Effort, 60);
    }

    /// <summary>Does one voxel of work; false if there was nothing to do there after all.</summary>
    bool Do(Unit u, (int x, int y, int z, byte m) j)
    {
        byte cur = World.Get(j.x, j.y, j.z);
        if (Orders.At(j.x, j.y, j.z) != j.m) return false;
        if (Orders.IsRemove(j.m))
        {
            if (!Labour.Diggable(cur)) { Orders.Clear(j.x, j.y, j.z); return false; }
            World.Set(j.x, j.y, j.z, Mat.Air);
            Orders.Clear(j.x, j.y, j.z);
            Dug(j.x, j.y, j.z, cur);
            int kind = Labour.YieldKind(cur);
            if (kind != Stockpile.None) Stock[kind] += Labour.YieldShare(cur) * u.T.Yield * YieldMore;
            if (cur == Mat.Ore) Stock.Gold += Labour.OreGold * u.T.Yield;
            Mined?.Invoke(j.x, j.y, j.z, cur);
            return true;
        }
        if (!Brushes.Free(cur) || cur == j.m) { Orders.Clear(j.x, j.y, j.z); return false; }
        // pitch is poured, not laid: it needs nothing under it, it runs down
        if (_bodies[Nav.CellOf(j.x, j.y, j.z)] > 0 || j.m != Mat.Pitch && !Footed(j.x, j.y, j.z)) return false;
        int ck = Labour.CostKind(j.m);
        if (ck != Stockpile.None && !Stock.Take(ck, 1)) { u.Why = "нет: " + Stockpile.Name(ck); return false; }
        World.Set(j.x, j.y, j.z, j.m);
        Remember(j.x, j.y, j.z, j.m);
        Orders.Clear(j.x, j.y, j.z);
        return true;
    }

    /// <summary>Called for every voxel a pawn digs out, for chips and dust.</summary>
    public Action<int, int, int, byte> Mined;

    /// <summary>
    /// The orders within reach of where the pawn stands, of the work it came here for: digging top-down first, then
    /// laying bottom-up, and within each course the voxels nearest to it first, so two at one site work different ends.
    /// </summary>
    void Gather(Unit u)
    {
        u.Jobs.Clear(); u.JobI = 0; u.Why = "";
        int i = Nav.I(u.Cell), k = Nav.K(u.Cell), feet = (int)MathF.Round(u.Y);
        int x0 = Math.Max(0, (i - 1) * NavGrid.CS), x1 = Math.Min(World.SX, (i + 2) * NavGrid.CS);
        int z0 = Math.Max(0, (k - 1) * NavGrid.CS), z1 = Math.Min(World.SZ, (k + 2) * NavGrid.CS);
        int y0 = Math.Max(0, feet - Labour.ReachDown), y1 = Math.Min(World.SY, feet + Labour.ReachUp);
        var digs = new List<(int x, int y, int z, byte m)>();
        var builds = new List<(int x, int y, int z, byte m)>();
        string missing = "";
        bool crowded = false, unfooted = false;
        for (int y = y1 - 1; y >= y0 && digs.Count < 2048; y--)
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                {
                    byte o = Orders.At(x, y, z);
                    if (o == 0 || (u.SiteWork >> (int)Work.Of(o) & 1) == 0) continue;
                    if (Orders.IsRemove(o)) { digs.Add((x, y, z, o)); continue; }
                    int ck = Labour.CostKind(o);
                    if (ck != Stockpile.None && Stock[ck] < 1) { missing = Stockpile.Name(ck); continue; }
                    if (_bodies[Nav.CellOf(x, y, z)] > 0) { crowded = true; continue; }
                    // nothing yet to lay it on: what is under it first (it is gathered again once that is laid)
                    if (o != Mat.Pitch && !Footed(x, y, z) && !builds.Exists(b => Math.Abs(b.x - x) + Math.Abs(b.y - y) + Math.Abs(b.z - z) == 1)) { unfooted = true; continue; }
                    builds.Add((x, y, z, o));
                }
        float ux = u.X, uz = u.Z;
        float Near((int x, int y, int z, byte m) j) => (j.x + .5f - ux) * (j.x + .5f - ux) + (j.z + .5f - uz) * (j.z + .5f - uz);
        digs.Sort((a, b) => a.y != b.y ? b.y.CompareTo(a.y) : Near(a).CompareTo(Near(b)));
        builds.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : Near(a).CompareTo(Near(b)));
        u.Jobs.AddRange(digs);
        u.Jobs.AddRange(builds);
        if (u.Jobs.Count == 0) u.Why = missing != "" ? "нет: " + missing : crowded ? "на месте кладки стоят" : unfooted ? "ждёт опоры: класть не на что" : "";
    }

    void FindWork(Unit u)
    {
        u.NextSearch = Time + .3 + Rng.NextDouble() * .4;
        if ((Work.Allowed(u) & Work.Marked) == 0) { u.Why = "все работы отключены"; return; }
        if (Orders.Active.Count == 0)
        {
            u.Why = "";
            // nothing marked: do what it was told to, or take a little more out of an area the player set aside
            if (DutyWork(u) || FeedFromZone(u)) u.NextSearch = Time;
            return;
        }
        int ui = Nav.I(u.Cell), uk = Nav.K(u.Cell), h = u.T.Move.Height, starved = 0;
        bool unreached = false;
        // the work it puts first, then the next: of each, the nearest it can get to
        for (int priority = 1; priority <= 4; priority++)
        {
            int mask = Work.Mask(u, priority);
            if (mask == 0) continue;
            var near = new List<(float d, int col)>();
            foreach (int col in Orders.Active)
            {
                if (!HasWork(col, mask)) continue;
                // nothing there to dig, and nothing to lay that the stores hold: don't even go
                if (!Workable(col, mask)) { starved++; continue; }
                if (_claims[col] >= 2) continue;
                // what another pawn was sent to bring in is its work
                if (u.Duty != Stockpile.None && ZoneAt((col % Nav.NX << NavGrid.Shift) + 4, (col / Nav.NX << NavGrid.Shift) + 4) is { Auto: true } other && other != u.DutyZone) continue;
                if (u.Unreachable.TryGetValue(col, out double until) && Time < until) continue;
                float d = PathFinder.Octile(col % Nav.NX - ui, col / Nav.NX - uk);
                if (near.Count < 6) { near.Add((d, col)); continue; }
                int worst = 0;
                for (int n = 1; n < near.Count; n++) if (near[n].d > near[worst].d) worst = n;
                if (d < near[worst].d) near[worst] = (d, col);
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var (_, col) in near)
            {
                if (!Orders.Span(col, out int y0, out int y1)) continue;
                int ci = col % Nav.NX, ck = col / Nav.NX;
                // stand beside the work (in its column only if nothing is to be laid where the body would be),
                // and not where another pawn already stands
                int self = u.Cell;
                bool Goal(int c) => Math.Abs(Nav.I(c) - ci) <= 1 && Math.Abs(Nav.K(c) - ck) <= 1
                    && Nav.Floor[c] - Labour.ReachDown <= y1 && Nav.Floor[c] + Labour.ReachUp > y0
                    && !(Nav.I(c) == ci && Nav.K(c) == ck && BuildsInBody(c, h))
                    && _bodies[c] - (c == self ? 1 : 0) <= 0;
                List<int> path = Goal(u.Cell) ? new List<int>() : Paths.Find(u.Cell, Goal, c => H(c, ci, ck), u.T.Move, 30000, gates: true);
                if (path == null) { u.Unreachable[col] = Time + 12; continue; }
                u.Site = col; _claims[col]++;
                u.SiteWork = mask;
                u.Path = path.Count > 0 ? path : null; u.PathI = 0;
                u.Jobs.Clear(); u.JobI = 0; u.Why = "";
                return;
            }
            if (near.Count > 0) unreached = true;
        }
        if (unreached) u.Why = "до работы не дойти";
        else if (starved > 0) u.Why = "ждёт: " + Wanting();
        else if (Barred(u) is { Length: > 0 } barred) u.Why = "не велено: " + barred;
        // nothing marked that this one can get to: take a little more out of an area set aside for it
        if (DutyWork(u) || FeedFromZone(u)) { u.Why = ""; u.NextSearch = Time; }
    }

    /// <summary>The marked work there is that this pawn is set never to do: "копать, рубить".</summary>
    string Barred(Unit u)
    {
        int allowed = Work.Allowed(u), barred = 0;
        foreach (int col in Orders.Active)
            for (int k = 0; k < Work.Kinds; k++)
                if ((allowed >> k & 1) == 0 && Orders.ColWork[k][col] > 0) barred |= 1 << k;
        var names = new List<string>();
        for (int k = 0; k < Work.Kinds; k++) if ((barred >> k & 1) != 0) names.Add(Work.Verbs[k]);
        return string.Join(", ", names);
    }

    /// <summary>A column holds work of one of these kinds (bits by <see cref="WorkKind"/>).</summary>
    bool HasWork(int col, int mask)
    {
        for (int k = 0; k < Work.Kinds; k++) if ((mask >> k & 1) != 0 && Orders.ColWork[k][col] > 0) return true;
        return false;
    }

    /// <summary>
    /// A column worth walking to for work of these kinds: something there to take out, or something to lay that the
    /// stores hold (or that costs nothing).
    /// </summary>
    bool Workable(int col, int mask)
    {
        for (int k = 1; k < Work.Kinds; k++) if ((mask >> k & 1) != 0 && Orders.ColWork[k][col] > 0) return true;
        if ((mask & 1) == 0) return false;
        if (Orders.ColLay[Stockpile.None][col] > 0) return true;
        for (int k = 1; k < 4; k++) if (Orders.ColLay[k][col] > 0 && Stock[k] >= 1) return true;
        return false;
    }

    /// <summary>What the marked work waits for most: "камень", "брёвна".</summary>
    string Wanting()
    {
        var need = new long[4];
        foreach (int col in Orders.Active) for (int k = 1; k < 4; k++) if (Stock[k] < 1) need[k] += Orders.ColLay[k][col];
        int best = 1;
        for (int k = 2; k < 4; k++) if (need[k] > need[best]) best = k;
        return Stockpile.Name(best);
    }

    /// <summary>Planned masonry where a pawn would stand: don't let it wall itself in.</summary>
    bool BuildsInBody(int c, int h)
    {
        int x0 = Nav.I(c) << NavGrid.Shift, z0 = Nav.K(c) << NavGrid.Shift, y0 = Nav.Y(c) << NavGrid.Shift;
        for (int y = y0; y < Math.Min(World.SY, y0 + h * NavGrid.CS); y++)
            for (int z = z0; z < z0 + NavGrid.CS; z++)
                for (int x = x0; x < x0 + NavGrid.CS; x++)
                {
                    byte o = Orders.At(x, y, z);
                    if (o != 0 && !Orders.IsRemove(o)) return true;
                }
        return false;
    }

    void ReleaseSite(Unit u)
    {
        if (u.Site >= 0) { _claims[u.Site] = Math.Max(0, _claims[u.Site] - 1); u.Site = -1; }
        u.Jobs.Clear(); u.JobI = 0;
    }

    /// <summary>Soldiers throw down the horde's ladders within reach; true while at it.</summary>
    bool PushLadders(Unit u, float dt)
    {
        if (u.PushCell < 0)
        {
            if (Time < u.NextLook) return false;
            u.NextLook = Time + .4 + Rng.NextDouble() * .3;
            int i = Nav.I(u.Cell), y = Nav.Y(u.Cell), k = Nav.K(u.Cell);
            for (int dy = -3; dy <= 1 && u.PushCell < 0; dy++)
                for (int dk = -1; dk <= 1 && u.PushCell < 0; dk++)
                    for (int di = -1; di <= 1 && u.PushCell < 0; di++)
                    {
                        if (!Nav.In(i + di, y + dy, k + dk)) continue;
                        int c = Nav.Id(i + di, y + dy, k + dk);
                        if (Nav.Ladder[c] != 0 && SiegeVoxel(c, out var v)) { u.PushCell = c; u.PushVoxel = v; u.PushEffort = 0; }
                    }
            if (u.PushCell < 0) return false;
            ReleaseSite(u);
        }
        u.State = UnitState.Work;
        u.Path = null;
        u.Facing = MathF.Atan2(u.PushVoxel.z + .5f - u.Z, u.PushVoxel.x + .5f - u.X);
        u.PushEffort += dt;
        if (u.PushEffort < 1.2f) return true;
        u.Swing = 1;
        int n = ThrowDown(u.PushVoxel.x, u.PushVoxel.y, u.PushVoxel.z);
        if (n > 20) Say($"{u.T.Name} сбросил осадную лестницу.", 1, u);
        u.PushCell = -1;
        return true;
    }

    bool SiegeVoxel(int c, out (int x, int y, int z) v)
    {
        int x0 = Nav.I(c) << NavGrid.Shift, y0 = Nav.Y(c) << NavGrid.Shift, z0 = Nav.K(c) << NavGrid.Shift;
        for (int y = y0; y < y0 + NavGrid.CS; y++)
            for (int z = z0; z < z0 + NavGrid.CS; z++)
                for (int x = x0; x < x0 + NavGrid.CS; x++)
                    if (World.Get(x, y, z) == Mat.SiegeLadder) { v = (x, y, z); return true; }
        v = default;
        return false;
    }

    /// <summary>Takes away a whole siege ladder from one of its voxels; whoever was on it falls.</summary>
    public int ThrowDown(int x0, int y0, int z0)
    {
        var queue = new Queue<(int x, int y, int z)>();
        var seen = new HashSet<long>();
        long Key(int x, int y, int z) => ((long)y * World.SZ + z) * World.SX + x;
        queue.Enqueue((x0, y0, z0)); seen.Add(Key(x0, y0, z0));
        int n = 0;
        while (queue.Count > 0 && n < 3000)
        {
            var (x, y, z) = queue.Dequeue();
            if (World.Get(x, y, z) != Mat.SiegeLadder) continue;
            World.Set(x, y, z, Mat.Air);
            Hacked?.Invoke(x, y, z, Mat.SiegeLadder);
            n++;
            foreach (var (dx, dy, dz) in Around)
            {
                int nx = x + dx, ny = y + dy, nz = z + dz;
                if (Math.Abs(nx - x0) > 12 || Math.Abs(nz - z0) > 12 || !seen.Add(Key(nx, ny, nz))) continue;
                queue.Enqueue((nx, ny, nz));
            }
        }
        return n;
    }

    static readonly (int, int, int)[] Around = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

    /// <summary>
    /// One of ours badly hurt — a third of itself left, or bleeding hard — leaves the fight and goes to mend by the
    /// citadel, fighting only what has caught it; one held at a spot by an order stays till it is worse. Mended to three
    /// quarters it goes back to its post. True while at that.
    /// </summary>
    bool FallBack(Unit u, float dt)
    {
        float bleed = u.Wounds?.Bleed ?? 0;
        if (!u.Mending)
        {
            if (u.Hp >= u.MaxHp * (u.Hold ? .2f : .35f) && bleed <= 1.2f) return false;
            u.Mending = true;
            ReleaseSite(u);
            u.Prey = null; u.Path = null; u.NextSearch = 0; u.Patient = null;
            Say($"{u.T.Name} тяжело ранен и отходит к цитадели.", 3, u);
        }
        else if (u.Hp >= u.MaxHp * .75f && bleed < .1f)
        {
            u.Mending = false;
            u.Why = "";
            if (u.Post >= 0) GoTo(u, u.Post);
            return false;
        }
        u.Why = HasMainHand(u) ? "ранен: отлёживается" : "без руки: работать не может";
        var foe = Nearest(u, Team.Horde, u.T.Reach + 8);
        if (foe != null && InReach(u, foe) && Armed(u) && Time - u.LastHurt < 1.5)
        {
            u.State = UnitState.Fight;
            u.Facing = MathF.Atan2(foe.Z - u.Z, foe.X - u.X);
            if (u.Cooldown <= 0) { Strike(u, foe); u.Cooldown = u.T.AttackTime; }
            return true;
        }
        if (CellsApart(u.Cell, Gate) > 2)
        {
            if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
            {
                u.NextSearch = Time + 2;
                if (!GoTo(u, Gate, adjacent: true, budget: 20000)) u.Path = null;
            }
            if (u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt, .8f); return true; }
        }
        // there: into the citadel, to mend out of harm's way
        Enter(u);
        return true;
    }

    /// <summary>
    /// One of ours with nobody on it sees a comrade bleeding close by, with no raider over it, and goes to bind the
    /// wound: a few seconds beside it and the bleeding all but stops. True while at that.
    /// </summary>
    bool Bandage(Unit u, float dt)
    {
        var p = u.Patient;
        if (p == null || p.Dead || p.Wounds is not { Bleed: > .15f } || p.Bandager != u)
        {
            if (p != null && p.Bandager == u) p.Bandager = null;
            u.Patient = p = null;
            if (Time < u.NextLookForWounded || Nearest(u, Team.Horde, 12 * VoxelWorld.PerMetre) != null) return false;
            u.NextLookForWounded = Time + 1 + Rng.NextDouble() * .5;
            float best = 30 * VoxelWorld.PerMetre;
            foreach (var o in Units)
            {
                if (o == u || o.Dead || o.Inside || o.Team != Team.Player || o.Wounds is not { Bleed: > .15f } || o.Bandager is { Dead: false } && o.Bandager != u) continue;
                float d = Dist(u, o);
                if (d < best && Nearest(o, Team.Horde, 12 * VoxelWorld.PerMetre) == null) { best = d; p = o; }
            }
            if (p == null) return false;
            u.Patient = p; p.Bandager = u; u.BandageEffort = 0;
            ReleaseSite(u);
            u.Path = null;
        }
        if (Dist(u, p) > u.T.BodyR + p.T.BodyR + 9)
        {
            if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
            {
                u.NextSearch = Time + .7;
                if (!GoTo(u, p.Cell, adjacent: true, budget: 6000)) { p.Bandager = null; u.Patient = null; u.NextLookForWounded = Time + 4; return false; }
            }
            if (u.Path != null && u.PathI < u.Path.Count) Walk(u, dt);
            return true;
        }
        u.Path = null;
        u.State = UnitState.Work;
        u.Facing = MathF.Atan2(p.Z - u.Z, p.X - u.X);
        u.BandageEffort += dt;
        if (u.BandageEffort >= 2.5f)
        {
            p.Wounds.Bleed *= .1f;
            Say($"{u.T.Name} перевязал {Whom(p.T)}.", 1, p);
            p.Bandager = null; u.Patient = null;
        }
        return true;
    }

    /// <summary>"рабочего", "лучника", "мечника".</summary>
    static string Whom(UnitType t) => t.Key switch { "worker" => "рабочего", "archer" => "лучника", "sword" => "мечника", _ => t.Name.ToLowerInvariant() };

    /// <summary>
    /// Workers run for the nearest shelter when a raider comes close — not one the raider is nearer to — and into the
    /// citadel if that is it; true while running.
    /// </summary>
    bool Flee(Unit u, float dt)
    {
        var foe = Nearest(u, Team.Horde, 72);
        if (foe == null)
        {
            if (u.State != UnitState.Flee) return false;
            if (Nearest(u, Team.Horde, 140) != null && u.Path != null)
            {
                Walk(u, dt);
                if (u.Refuge == Gate && CellsApart(u.Cell, Gate) <= 2) Enter(u);
                return true;
            }
            u.State = UnitState.Idle; u.Path = null;
            if (!Alarm) u.Refuge = -1;
            return false;
        }
        if (InReach(u, foe) && Armed(u))
        {
            // caught: hit back
            u.State = UnitState.Fight;
            u.Facing = MathF.Atan2(foe.Z - u.Z, foe.X - u.X);
            if (u.Cooldown <= 0) { Strike(u, foe); u.Cooldown = u.T.AttackTime; }
            return true;
        }
        if (u.State != UnitState.Flee || u.Path == null)
        {
            ReleaseSite(u);
            u.State = UnitState.Flee;
            u.Refuge = RefugeFor(u, foe);
            if (u.Refuge < 0 || !GoTo(u, u.Refuge, adjacent: u.Refuge == Gate, budget: 20000)) u.Path = null;
        }
        if (u.Path != null) Walk(u, dt);
        // at the citadel's door: in
        if (u.Refuge == Gate && CellsApart(u.Cell, Gate) <= 2) Enter(u);
        return true;
    }
}
