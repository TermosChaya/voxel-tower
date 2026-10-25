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
        // an engine of ours goes about its own work; its crew keeps by it (Sim.OurEngines.cs)
        if (Kinds.IsEngine(u.T)) { OurEngineTick(u, dt); return; }
        TroopTick(u, dt);
        // bound and led off by a thief: it goes where it is led till it is freed
        if (u.Captor != null) { Led(u, dt); return; }
        // carrying a comrade who cannot get up, or helping one along: on with it, unless it must see to itself first (Sim.Carry.cs)
        if ((u.Carrying != null || u.Propping != null) && Aid(u, dt)) { Busy(u); return; }
        // standing in the flames: out of them before anything
        if (EscapeFire(u, dt)) { Busy(u); return; }
        // out of the way of a tree coming down, or of what a dig is bringing down (Sim.Felling.cs)
        if (u.Avoiding != null && u != Possessed && KeepOut(u, dt)) { Busy(u); return; }
        if (CrewTick(u, dt)) { Busy(u); return; }
        // a sapper's fuse burning down: one runs to cut it, the rest near it get away from the wall
        if (u.Defusing != null && Defuse(u, dt)) { Busy(u); return; }
        if (u.Shunning != null && KeepClear(u, dt)) { Busy(u); return; }
        // badly hurt: out of the fight to mend by the citadel
        if (FallBack(u, dt)) { Busy(u); return; }
        // a commander looks over the field now and then and gives its orders; one following it keeps behind it (Sim.Captains.cs)
        if (u.T == Kinds.Captain) CaptainThink(u);
        if (u.Follows != null && FollowTick(u, dt)) { Busy(u); return; }
        // one that has lost the hand it fights with (or, with a bow, either hand) runs like a worker
        bool soldier = u.T.Work < 1, fighter = (soldier || u.Militia) && Armed(u);
        // a hound or a rider does none of what is done on foot with the hands: no ladder thrown down, no wound bound, no fire put out
        bool handy = !u.T.NoHands && !u.T.Mounted;
        // a swordsman beside a siege ladder throws it down before anything else, climbers and all
        if (soldier && handy && !u.T.Ranged && HasMainHand(u) && PushLadders(u, dt)) { Busy(u); return; }
        if (fighter && Fight(u, dt)) { Busy(u); ReleaseSite(u); return; }
        if (!fighter && Flee(u, dt)) { Busy(u); return; }
        // the alarm is up: to shelter (one the player sent somewhere goes where it was sent)
        if (!fighter && Alarm && !u.Hold && TakeShelter(u, dt)) { Busy(u); return; }
        if (u.Prey != null && HuntTick(u, dt)) { Busy(u); return; }
        // a priest prays by day and shelters by night, and does nothing else (unless the player sent it somewhere)
        if (u.T.Prays && !u.Hold) { PriestTick(u, dt); return; }
        // a comrade pinned under what came down comes before any work: dug out (a soldier only close by while the horde is here)
        if (!u.T.Prays && handy && Rescue(u, dt, (soldier && Raids.Active ? 15 : 40) * VoxelWorld.PerMetre)) { Busy(u); return; }
        // a comrade bleeding close by comes before any work (soldiers keep to their posts while the horde is here)
        if (!u.T.Prays && handy && (!soldier || !Raids.Active) && Bandage(u, dt)) { Busy(u); return; }
        // one who cannot get up carried to mend, one limping helped along (soldiers only while the horde is away)
        if (!u.T.Prays && handy && (!soldier || !Raids.Active) && LookForFallen(u) && Aid(u, dt)) { Busy(u); return; }
        // a fire in what we built or round the citadel, if putting it out comes before the work at hand
        if (!u.T.Prays && handy && (!soldier || !Raids.Active) && FightFire(u, dt)) { Busy(u); return; }
        // a raider bound out in the field: a free worker goes for it and leads it into the citadel
        if (!soldier && !u.T.Prays && FetchCaptive(u, dt)) { Busy(u); return; }
        if (soldier && handy && HasMainHand(u) && PushLadders(u, dt)) { Busy(u); return; }
        if (u.Path != null && u.PathI < u.Path.Count && (u.Hold || u.Site < 0) && u.Pastime != Pastime.Stroll) { Walk(u, dt); return; }
        // a worker sent somewhere is there (or as near as it gets): back to work, from the area it was sent into first
        if (u.Hold && u.Back && (u.Dest < 0 || CellsApart(u.Cell, u.Dest) <= 1 || u.Path == null || u.PathI >= u.Path.Count))
        {
            u.Hold = false; u.Back = false;
            if (u.Dest >= 0) u.Focus = SetAsideAt(u.Dest);
            u.Dest = -1; u.NextSearch = 0;
        }
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
        if (soldier && (Raids.Active || Alarm))
        {
            // while the horde is in the valley, or the alarm is up, soldiers keep to their posts and look out
            ReleaseSite(u);
            if (u.Post >= 0 && u.Cell != u.Post && Time >= u.NextSearch) { u.NextSearch = Time + 2; Busy(u); GoTo(u, u.Post); }
            if (u.Path != null && u.Pastime != Pastime.Stroll) Walk(u, dt); else Idle(u, dt, stay: true);
            return;
        }
        // a hound or a rider has no work of the pawns': between waves it idles about its post
        if (u.T.Work <= 0) { ReleaseSite(u); u.Why = ""; Idle(u, dt, stay: false); return; }
        // at its scaffolding: raising it to get out or to work high up, or taking it down (Sim.Scaffold.cs)
        if (u.Raising != null && ScaffoldTick(u, dt)) { Busy(u); return; }
        // shut in by its own work (a pit, a walled tower): a way out is marked, and it is the nearest work there is
        LookForWayOut(u);
        if (u.Raising != null) { Busy(u); return; }
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
            // in a space being walled shut: out of it before anything (Sim.Seal.cs)
            if (Time >= u.NextSearch && GetOut(u)) { Busy(u); return; }
            if (Time >= u.NextSearch) FindWork(u);
            if (u.Site >= 0) { Busy(u); return; }
            // nothing to do up there: down by the scaffolding while it stands (Sim.Scaffold.cs)
            if (GetDown(u)) { Busy(u); return; }
            Idle(u, dt, stay: false);
            return;
        }
        // work where a tree is about to come down, or something built, waits till it is down (Sim.Undermine.cs)
        if (SiteInDanger(u)) { u.Unreachable[u.Site] = Time + 8; ReleaseSite(u); u.Why = "ждёт: там сейчас упадёт"; return; }
        if (u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt); return; }
        int si = u.Site % Nav.NX, sk = u.Site / Nav.NX;
        if (Math.Abs(Nav.I(u.Cell) - si) > 1 || Math.Abs(Nav.K(u.Cell) - sk) > 1) { ReleaseSite(u); return; }
        if (u.JobI >= u.Jobs.Count)
        {
            Gather(u);
            if (u.Jobs.Count == 0)
            {
                // nothing to do from here after all (stores empty, or someone stands where the masonry goes):
                // leave this spot to others for a while and look elsewhere; what waits for something to be laid under
                // it waits a good while — walking back to it every few seconds only takes the hands off other work
                bool aside = _inTheWay && StepAside(u);
                u.Unreachable[u.Site] = Time + (aside ? 1.5 : u.Why.StartsWith("ждёт опоры") ? 30 : u.Why.StartsWith("нет") ? 8 : 4);
                ReleaseSite(u);
                u.NextSearch = Time + .2;
                return;
            }
            // fresh work in reach: the tool already up, so the hands do not stand idle a whole blow
            u.Beat = Math.Max(u.Beat, .62f);
        }
        u.State = UnitState.Work;
        // mourning one of their own given to the old powers, they work a quarter slower
        u.Effort += (float)(Labour.WorkPerSecond * u.T.Work * u.Pace * dt) * (Time < MournUntil ? .75f : 1) * Hands(u);
        // the tool comes down in blows, one every so long by the body (a heavy one slower): at each it does as much as
        // the effort since the last affords — a stone may take two or three blows to come away, loose earth comes a few
        // spadefuls to the blow — and between them the arm draws back to come down on the very voxel it works next
        if (u.JobI < u.Jobs.Count) { var next = u.Jobs[u.JobI]; u.Facing = MathF.Atan2(next.z + .5f - u.Z, next.x + .5f - u.X); }
        float chop = Math.Max(.3f, Bodies.Plan(u.T, u.Look).Chop);
        u.Beat += dt * u.Pace / chop;
        // what is put by between blows is the work of one blow (none stored up while it walked or stood about)
        if (u.Beat < 1) { u.Effort = Math.Min(u.Effort, (float)(Labour.WorkPerSecond * u.T.Work * chop) * 1.1f + 60); return; }
        // the last blows on a tree's butt wait till its lane is clear, the feller at its back (Sim.Felling.cs); so does
        // the last of the ground under something built (Sim.Undermine.cs)
        if (u.JobI < u.Jobs.Count && FellHold(u, u.Jobs[u.JobI])) { u.Beat = .95f; return; }
        u.Beat -= MathF.Floor(u.Beat);
        // the blow lands on the voxel it was drawn at: chips and the ring of it, whether or not it comes away this time
        if (u.JobI < u.Jobs.Count)
        {
            var hit = u.Jobs[u.JobI];
            bool dig = Orders.IsRemove(hit.m);
            Worked?.Invoke(hit.x, hit.y, hit.z, dig ? World.Get(hit.x, hit.y, hit.z) : hit.m, dig);
        }
        int guard = 0;
        while (u.JobI < u.Jobs.Count && guard++ < 4096)
        {
            var j = u.Jobs[u.JobI];
            float cost = Orders.IsRemove(j.m) ? Labour.Dig(World.Get(j.x, j.y, j.z)) / DigPace : Labour.Lay(j.m) / LayPace;
            if (cost <= 0) cost = .25f;
            if (u.Effort < cost) break;
            // (each voxel of a trunk, or of a footing, weighed before it goes: the one that brings it down waits)
            if (FellHold(u, j)) break;
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
            DigAway(j.x, j.y, j.z, cur, u.T.Yield);
            return true;
        }
        if (!Brushes.Free(cur) || cur == j.m) { Orders.Clear(j.x, j.y, j.z); return false; }
        // pitch is poured, not laid: it needs nothing under it, it runs down
        int jc = Nav.CellOf(j.x, j.y, j.z);
        bool underfoot = _bodies[jc] > 0 && Underfoot(j.x, j.y, j.z);
        if (_bodies[jc] > 0 && !underfoot || j.m != Mat.Pitch && !Footed(j.x, j.y, j.z)) return false;
        // nor what would be out further than it bears from what holds it, and come straight down (Sim.Builders.cs); nor
        // what is to rest on a voxel not laid yet, held out from the side meanwhile
        if (j.m != Mat.Pitch && (Overreaches(j.x, j.y, j.z, j.m) || UnderMarked(j.x, j.y, j.z))) return false;
        // the voxel that would wall one of ours in waits, to be laid from outside once nobody is in (Sim.Seal.cs)
        if (ShutsIn(u, j.x, j.y, j.z, j.m)) return false;
        int ck = Labour.CostKind(j.m);
        if (ck != Stockpile.None && !Stock.Take(ck, 1)) { u.Why = "нет: " + Stockpile.Name(ck); return false; }
        World.Set(j.x, j.y, j.z, j.m);
        Filled(j.x, j.y, j.z, j.m);
        Remember(j.x, j.y, j.z, j.m);
        Orders.Clear(j.x, j.y, j.z);
        BuiltVoxels++;
        // whoever stood where the floor went in stands on it now
        if (underfoot)
            foreach (var o in Units)
                if (!o.Dead && !o.Inside && o.Airborne == null && o.Cell == jc && o.Y < j.y + 1 && Math.Abs(o.X - j.x - .5f) < 4 && Math.Abs(o.Z - j.z - .5f) < 4) o.Y = j.y + 1;
        return true;
    }

    /// <summary>
    /// A voxel of a floor rather than of a wall: in the lowest two above where the feet rest in its cell, with nothing to
    /// be laid over it higher in the cell. It may be laid under someone standing there, lifting them.
    /// </summary>
    bool Underfoot(int x, int y, int z)
    {
        int c = Nav.CellOf(x, y, z), f = Nav.Floor[c];
        if (y < f || y >= f + 2) return false;
        for (int yy = f + 2; yy < f + NavGrid.CS && yy < World.SY; yy++)
        {
            byte o = Orders.At(x, yy, z);
            if (o != 0 && !Orders.IsRemove(o)) return false;
        }
        return true;
    }

    /// <summary>Called for every voxel a pawn digs out, for chips and dust.</summary>
    public Action<int, int, int, byte> Mined;

    /// <summary>A blow of a pawn's tool lands (or a stone is set down): the voxel, what it is of, and whether it is dug or laid.</summary>
    public Action<int, int, int, byte, bool> Worked;

    /// <summary>
    /// The orders within reach of where the pawn stands, of the work it came here for: digging top-down first, then
    /// laying bottom-up, and within each course the voxels nearest to it first, so two at one site work different ends.
    /// </summary>
    // set by Gather: what is to be laid waits on the very spot the one gathering stands on
    bool _inTheWay;

    void Gather(Unit u)
    {
        u.Jobs.Clear(); u.JobI = 0; u.Why = ""; _inTheWay = false;
        int i = Nav.I(u.Cell), k = Nav.K(u.Cell), feet = (int)MathF.Round(u.Y);
        int x0 = Math.Max(0, (i - Labour.ReachAside) * NavGrid.CS), x1 = Math.Min(World.SX, (i + 1 + Labour.ReachAside) * NavGrid.CS);
        int z0 = Math.Max(0, (k - Labour.ReachAside) * NavGrid.CS), z1 = Math.Min(World.SZ, (k + 1 + Labour.ReachAside) * NavGrid.CS);
        int y0 = Math.Max(0, feet - Labour.ReachDown), y1 = Math.Min(World.SY, feet + Labour.ReachUp);
        var digs = new List<(int x, int y, int z, byte m)>();
        var builds = new List<(int x, int y, int z, byte m)>();
        string missing = "", held = "";
        bool crowded = false, unfooted = false, ownFeet = false, overhead = BuiltOverhead(u);
        // (all of what is in reach is looked over: digging is gathered from the top down, so that with a crown marked
        // over it, a tree's butt — what is cut first — would be the last found, and missed)
        for (int y = y1 - 1; y >= y0; y--)
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                {
                    byte o = Orders.At(x, y, z);
                    if (o == 0 || (u.SiteWork >> (int)Work.Of(o) & 1) == 0) continue;
                    if (Orders.IsRemove(o))
                    {
                        // a roof comes off from the ridge down (Sim.Builders.cs); of a crown, so much at a time; the wood always
                        bool wood = Mat.IsTrunk(World.Get(x, y, z));
                        if (digs.Count >= 2048 && !wood) continue;
                        // a standing trunk is cut at its foot only, never partway up: the top would come down on whoever is under it
                        if (wood && MidTrunk(x, y, z)) continue;
                        // the ground under its own feet is dug from beside, and the footing of what is built over its head
                        // from out from under it (what is built is taken down as builders do it); our countermine no
                        // further than its props (Sim.Undermine.cs)
                        if (!wood && Natural(World.Get(x, y, z)) && (UnderOwnFeet(u, x, y, z, feet) || overhead && BearsBuilt(x, y, z))) { ownFeet = true; continue; }
                        if (BoreHeld(x, y, z)) { held = "подкоп ждёт брёвен на крепь"; continue; }
                        if (_footingsAt.Count > 0 && _footingsAt.Contains(WaterPlanner.Pack(x, y, z))) continue;
                        if (Arched.Get(x, y, z) == 0 || !ArchOver(x, y, z)) digs.Add((x, y, z, o));
                        continue;
                    }
                    int ck = Labour.CostKind(o);
                    if (ck != Stockpile.None && Stock[ck] < 1) { missing = Stockpile.Name(ck); continue; }
                    int bc = Nav.CellOf(x, y, z);
                    // a gap left open so that nobody is walled in waits a while (Sim.Seal.cs)
                    if (Held(bc, out string why)) { held = why; continue; }
                    // a floor is laid under the feet of whoever stands on its spot, and lifts them (a house's floor goes in
                    // with the workers inside it, building its roof)
                    if (_bodies[bc] > 0 && !Underfoot(x, y, z))
                    {
                        crowded = true;
                        // it is itself where the masonry goes
                        if (Nav.I(bc) == Nav.I(u.Cell) && Nav.K(bc) == Nav.K(u.Cell) && Nav.Y(bc) >= Nav.Y(u.Cell) && Nav.Y(bc) < Nav.Y(u.Cell) + u.T.Move.Height) _inTheWay = true;
                        continue;
                    }
                    builds.Add((x, y, z, o));
                }
        // nothing yet to lay it on, nor anything laid first from here to lay it on: it waits for its footing
        if (builds.Count > 0 && Footing(builds)) unfooted = true;
        float ux = u.X, uz = u.Z;
        float Near((int x, int y, int z, byte m) j) => (j.x + .5f - ux) * (j.x + .5f - ux) + (j.z + .5f - uz) * (j.z + .5f - uz);
        // a tree is cut at its foot, so that it goes over whole on its stump; everything else is dug from the top down
        int Foot((int x, int y, int z, byte m) j) => Mat.IsTrunk(World.Get(j.x, j.y, j.z)) ? 0 : 1;
        digs.Sort((a, b) =>
        {
            int fa = Foot(a), fb = Foot(b);
            if (fa != fb) return fa.CompareTo(fb);
            if (a.y != b.y) return fa == 0 ? a.y.CompareTo(b.y) : b.y.CompareTo(a.y);
            return Near(a).CompareTo(Near(b));
        });
        builds.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : Near(a).CompareTo(Near(b)));
        u.Jobs.AddRange(digs);
        u.Jobs.AddRange(builds);
        if (u.Jobs.Count == 0) u.Why = missing != "" ? "нет: " + missing : held != "" ? held : crowded ? "на месте кладки стоят"
            : unfooted ? (_overreach ? "ждёт опоры: так далеко не удержится" : "ждёт опоры: класть не на что")
            : ownFeet ? "под собой не копает: зайдёт сбоку" : "";
    }

    readonly Dictionary<int, byte> _toLay = new();
    readonly Dictionary<int, int> _footed = new();
    readonly LinkedList<int> _footing = new();
    static readonly (int dx, int dy, int dz)[] Borne = { (0, 1, 0), (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1) };
    // set by Footing: something to lay here had a footing, but would be out further than it bears (Sim.Builders.cs)
    bool _overreach;

    /// <summary>
    /// Keeps of the voxels to lay from here those that stand on something already, or will once what is under them or
    /// beside them here is laid first (in the order the work goes, bottom up): so a wall is laid from one footing all the
    /// height in reach, not a course at a time. Pitch is poured, it needs nothing under it. What would be out over the
    /// air further than its material bears from what holds it is left too (Sim.Builders.cs): a roof waits for the wall
    /// across the room. True if any had to be left.
    /// </summary>
    bool Footing(List<(int x, int y, int z, byte m)> builds)
    {
        _toLay.Clear(); _footed.Clear(); _footing.Clear(); _overreach = false;
        foreach (var b in builds) _toLay[WaterPlanner.Pack(b.x, b.y, b.z)] = b.m;
        static int Bears(byte m) { int s = SpanOf(m); return s == 0 || s == Free ? int.MaxValue : s; }
        foreach (var b in builds)
        {
            if (b.m != Mat.Pitch && (!Footed(b.x, b.y, b.z) || UnderMarked(b.x, b.y, b.z))) continue;
            int d = b.m == Mat.Pitch ? 0 : ReachIfLaid(b.x, b.y, b.z, b.m);
            if (d > Bears(b.m)) { _overreach = true; continue; }
            int p = WaterPlanner.Pack(b.x, b.y, b.z);
            if (_footed.TryGetValue(p, out int had) && had <= d) continue;
            _footed[p] = d;
            if (d == 0) _footing.AddFirst(p); else _footing.AddLast(p);
        }
        // what each one laid will bear: the voxel over it as far out as it is held, those beside it a step further
        while (_footing.Count > 0)
        {
            int p = _footing.First.Value; _footing.RemoveFirst();
            var (x, y, z) = WaterPlanner.Unpack(p);
            int d = _footed[p];
            foreach (var (dx, dy, dz) in Borne)
            {
                int n = WaterPlanner.Pack(x + dx, y + dy, z + dz);
                if (!_toLay.TryGetValue(n, out byte m)) continue;
                var (nx, ny, nz) = (x + dx, y + dy, z + dz);
                // what stands as an arch needs only to be joined to the rest: it is held however far out it goes
                int nd = Bears(m) == int.MaxValue || Arched.Get(nx, ny, nz) != 0 ? 0 : d + (dy == 0 ? 1 : 0);
                if (nd > Bears(m)) { _overreach = true; continue; }
                if (_footed.TryGetValue(n, out int had) && had <= nd) continue;
                _footed[n] = nd;
                if (dy != 0 || nd == d) _footing.AddFirst(n); else _footing.AddLast(n);
            }
        }
        int had0 = builds.Count;
        builds.RemoveAll(b => !_footed.ContainsKey(WaterPlanner.Pack(b.x, b.y, b.z)));
        return builds.Count < had0;
    }

    /// <summary>
    /// One standing where its own masonry is to go steps off it to the nearest ground beside where nothing is to be
    /// laid, and comes back to the work from there; true if it found where to go.
    /// </summary>
    bool StepAside(Unit u)
    {
        int i = Nav.I(u.Cell), k = Nav.K(u.Cell), y = Nav.Y(u.Cell), h = u.T.Move.Height;
        for (int r = 1; r <= 2; r++)
            for (int dk = -r; dk <= r; dk++)
                for (int di = -r; di <= r; di++)
                {
                    if (Math.Max(Math.Abs(di), Math.Abs(dk)) != r) continue;
                    for (int dy = 0; dy <= 2; dy = dy <= 0 ? 1 - dy : -dy)
                    {
                        int ni = i + di, nk = k + dk, ny = y + dy;
                        // onto ground, or onto scaffolding or a ladder beside (up on a wall's top, that is all there may be)
                        if (ni < 0 || nk < 0 || ni >= Nav.NX || nk >= Nav.NZ || ny < 1 || ny >= Nav.NY || !Nav.CanBe(ni, ny, nk, h, true, u.T.Move.Ladders)) continue;
                        int c = Nav.Id(ni, ny, nk);
                        if (_bodies[c] > 0 || ToLay(c, h)) continue;
                        if (GoTo(u, c, budget: 600)) { u.Why = ""; return true; }
                    }
                }
        return false;
    }

    /// <summary>Whether anything is marked to be laid in a cell someone so tall would stand in.</summary>
    bool ToLay(int cell, int h)
    {
        int x0 = Nav.I(cell) << NavGrid.Shift, z0 = Nav.K(cell) << NavGrid.Shift, y0 = Nav.Y(cell) << NavGrid.Shift;
        for (int y = y0; y < Math.Min(World.SY, y0 + h * NavGrid.CS); y++)
            for (int z = z0; z < z0 + NavGrid.CS; z++)
                for (int x = x0; x < x0 + NavGrid.CS; x++)
                {
                    byte o = Orders.At(x, y, z);
                    if (o != 0 && !Orders.IsRemove(o)) return true;
                }
        return false;
    }

    void FindWork(Unit u)
    {
        u.NextSearch = Time + .3 + Rng.NextDouble() * .4;
        if ((Work.Allowed(u) & Work.Marked) == 0) { u.Why = "все работы отключены"; return; }
        // sent into an area set aside: its work first, a little more of it marked near the pawn, till it is spent
        if (u.Focus != null)
        {
            if (!Zones.Contains(u.Focus) || !Work.Does(u, Work.For(u.Focus.Kind))) u.Focus = null;
            else
            {
                Feed(u.Focus, u, out bool spent);
                if (spent) u.Focus = null;
                else if (Seek(u, u.Focus)) return;
            }
        }
        Seek(u, null);
    }

    /// <summary>A gather area (not a shelter, a sanctum or a house) at a cell, if the cell lies in one.</summary>
    Zone SetAsideAt(int cell)
    {
        int x = Nav.I(cell) << NavGrid.Shift, z = Nav.K(cell) << NavGrid.Shift;
        foreach (var zone in Zones)
            if (zone.Kind is not (Zone.Shelter or Zone.Sanctum or Zone.Dwelling) && !zone.Auto
                && x + 4 >= zone.X0 && x + 4 <= zone.X1 && z + 4 >= zone.Z0 && z + 4 <= zone.Z1) return zone;
        return null;
    }

    /// <summary>
    /// The nearest marked work this pawn can get to, the work it puts first before the next (inside an area only, when
    /// one is given); true when it is on its way to some.
    /// </summary>
    bool Seek(Unit u, Zone only)
    {
        if (Orders.Active.Count == 0)
        {
            if (only != null) return false;
            u.Why = "";
            // nothing marked: do what it was told to, or take a little more out of an area the player set aside
            if (DutyWork(u) || FeedFromZone(u)) u.NextSearch = Time;
            return false;
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
                if (only != null && !Inside(only, col)) continue;
                // what another pawn was sent to bring in is its work
                if (u.Duty != Stockpile.None && ZoneAt((col % Nav.NX << NavGrid.Shift) + 4, (col / Nav.NX << NavGrid.Shift) + 4) is { Auto: true } other && other != u.DutyZone) continue;
                if (u.Unreachable.TryGetValue(col, out double until) && Time < until) continue;
                // what one pawn found it could not get to, those standing about the same place do not try for again
                if (_noWay.TryGetValue(col, out var nw) && Time < nw.until && CellsApart(u.Cell, nw.from) <= 6 && MathF.Abs(Nav.Floor[u.Cell] - Nav.Floor[nw.from]) <= 8) continue;
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
                    && _bodies[c] - (c == self ? 1 : 0) <= 0
                    // not inside a space being walled shut: its gap is filled from outside (Sim.Seal.cs)
                    && !Closing(c);
                // the searches of a step share a budget: what is left over looks again a step later (a stretch of work
                // that cannot be got to costs a whole search to find out, and many pawns finding it out at once stop the game)
                if (Time != _seekAt) { _seekAt = Time; _seekSpent = 0; }
                if (_seekSpent >= SeekBudget) { u.NextSearch = Time; return false; }
                // what is to be laid goes up from its lowest course: a place to stand that reaches that first, if there is
                // one — up on the wall or the scaffolding the courses still wanting below are out of reach, and what is
                // over them has nothing to rest on (a tower's last gap was left so, for good)
                bool lays = (mask & 1) != 0 && Orders.ColWork[0][col] > 0;
                bool Low(int c) => Goal(c) && Nav.Floor[c] - Labour.ReachDown <= y0;
                List<int> path = null;
                if (lays)
                {
                    path = Low(u.Cell) ? new List<int>() : Paths.Find(u.Cell, Low, c => H(c, ci, ck), MoverOf(u), 30000, gates: true);
                    if (!Low(u.Cell)) _seekSpent += Paths.Expanded;
                }
                if (path == null)
                {
                    path = Goal(u.Cell) ? new List<int>() : Paths.Find(u.Cell, Goal, c => H(c, ci, ck), MoverOf(u), 30000, gates: true);
                    if (!Goal(u.Cell)) _seekSpent += Paths.Expanded;
                }
                if (path == null)
                {
                    // out of reach of anywhere to stand: scaffolding beside it, raised from the ground (Sim.Scaffold.cs)
                    if (u.T.Work >= 1 && Stock[Stockpile.Wood] >= 600 && PlanReach(u, col, y0, y1) is { } lift)
                    {
                        Scaffolds.Add(lift);
                        u.Raising = lift;
                        u.Why = "ставит леса к работе наверху";
                        return true;
                    }
                    u.Unreachable[col] = Time + 12;
                    if (_noWay.Count > 4096) _noWay.Clear();
                    _noWay[col] = (Time + 12, u.Cell);
                    continue;
                }
                u.Site = col; _claims[col]++;
                u.SiteWork = mask;
                u.Path = path.Count > 0 ? path : null; u.PathI = 0;
                u.Jobs.Clear(); u.JobI = 0; u.Why = "";
                return true;
            }
            if (near.Count > 0) unreached = true;
        }
        if (only != null) return false;
        if (unreached) u.Why = "до работы не дойти";
        else if (starved > 0) u.Why = "ждёт: " + Wanting();
        else if (Barred(u) is { Length: > 0 } barred) u.Why = "не велено: " + barred;
        // nothing marked that this one can get to: take a little more out of an area set aside for it
        if (DutyWork(u) || FeedFromZone(u)) { u.Why = ""; u.NextSearch = Time; }
        return false;
    }

    // the cells the pawns' searches for work have looked beyond this step, and at what time the step is
    int _seekSpent;
    double _seekAt = -1;
    // a whole search that finds no way costs about this much: one of those a step, as many short ones as come
    const int SeekBudget = 20_000;
    // the work columns some pawn found it could not get to lately: till when, and from where it looked
    readonly Dictionary<int, (double until, int from)> _noWay = new();

    bool Inside(Zone z, int col)
    {
        int x = ((col % Nav.NX) << NavGrid.Shift) + 4, zz = ((col / Nav.NX) << NavGrid.Shift) + 4;
        return x >= z.X0 && x <= z.X1 && zz >= z.Z0 && zz <= z.Z1;
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
        for (int k = 1; k < Stockpile.Kinds; k++) if (Orders.ColLay[k][col] > 0 && Stock[k] >= 1) return true;
        return false;
    }

    /// <summary>What the marked work waits for most: "камень", "брёвна".</summary>
    string Wanting()
    {
        var need = new long[Stockpile.Kinds];
        foreach (int col in Orders.Active) for (int k = 1; k < Stockpile.Kinds; k++) if (Stock[k] < 1) need[k] += Orders.ColLay[k][col];
        int best = 1;
        for (int k = 2; k < Stockpile.Kinds; k++) if (need[k] > need[best]) best = k;
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
            if (u.Hp >= u.MaxHp * Steadfast(u.Hold) && bleed <= 1.2f && !Disabled(u) && !u.SentBack) return false;
            u.Mending = true;
            ReleaseSite(u);
            u.Prey = null; u.Path = null; u.NextSearch = 0; u.Patient = null;
            Say($"{u.T.Name} тяжело ранен и отходит к цитадели.", 3, u);
        }
        else if (MendedEnough(u))
        {
            u.Mending = false;
            u.SentBack = false;
            u.Why = "";
            if (u.Post >= 0) GoTo(u, u.Post);
            return false;
        }
        u.Why = Disabled(u) ? "кость сломана: отлёживается" : HasMainHand(u) ? "ранен: отлёживается" : "без руки: работать не может";
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

    /// <summary>Mended enough to go back: three quarters of itself, the bleeding stopped, no leg or arm broken.</summary>
    bool MendedEnough(Unit u) => u.Hp >= u.MaxHp * .75f && (u.Wounds?.Bleed ?? 0) < .1f && !Disabled(u);

    /// <summary>Wants a comrade's hands: bleeding, or a broken limb not splinted yet.</summary>
    static bool WantsCare(Unit u) => u.Wounds is { Bleed: > .15f } || WantsSplint(u);

    /// <summary>
    /// One of ours with nobody on it sees a comrade bleeding close by, or with a limb broken, with no raider over it, and
    /// goes to bind the wound and splint the bone: a few seconds beside it and the bleeding all but stops, the bone knits
    /// twice as fast. True while at that.
    /// </summary>
    bool Bandage(Unit u, float dt)
    {
        var p = u.Patient;
        if (p == null || p.Dead || !WantsCare(p) || p.Bandager != u || p.PinKg > 0)
        {
            if (p != null && p.Bandager == u) p.Bandager = null;
            u.Patient = p = null;
            if (Time < u.NextLookForWounded || Nearest(u, Team.Horde, 12 * VoxelWorld.PerMetre) != null) return false;
            u.NextLookForWounded = Time + 1 + Rng.NextDouble() * .5;
            float best = 30 * VoxelWorld.PerMetre;
            foreach (var o in Units)
            {
                if (o == u || o.Dead || o.Inside || o.Team != Team.Player || o.PinKg > 0 || !WantsCare(o) || o.Bandager is { Dead: false } && o.Bandager != u) continue;
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
        if (u.BandageEffort >= (WantsSplint(p) ? 4 : 2.5f))
        {
            bool splint = WantsSplint(p);
            p.Wounds.Bleed *= .1f;
            SplintBones(p);
            Say(splint ? $"{u.T.Name} перевязал {Whom(p.T)} и наложил лубок." : $"{u.T.Name} перевязал {Whom(p.T)}.", 1, p);
            p.Bandager = null; u.Patient = null;
        }
        return true;
    }

    /// <summary>"рабочего", "лучника", "мечника".</summary>
    static string Whom(UnitType t) => t.Key switch
    {
        "worker" => "рабочего", "archer" => "лучника", "sword" => "мечника", "spear" => "копейщика", "shield" => "щитоносца", "halberd" => "алебардщика",
        "knight" => "рыцаря", "sling" => "пращника", "crossbow" => "арбалетчика", "bombard" => "бомбардира", "ouralchemist" => "алхимика", "hound" => "боевого пса", "rider" => "всадника",
        "horsearcher" => "конного лучника", "lancer" => "конного рыцаря", "tametroll" => "ручного тролля", "giant" => "великана",
        _ => t.Name.ToLowerInvariant(),
    };

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
        // (one hurt past bearing hits back no more: it is no fighter)
        if (InReach(u, foe) && Armed(u) && u.Pain < Cowed)
        {
            // caught: hit back — once it has turned round from running to face it
            if (u.State == UnitState.Flee) u.Cooldown = Math.Max(u.Cooldown, .5f);
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
