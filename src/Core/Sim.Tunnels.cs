using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

public enum TunnelState : byte { Digging, Chamber, Fired, Done }

/// <summary>A mine under the ground: the horde's toward a wall of ours, or our countermine toward it.</summary>
public sealed class Tunnel
{
    public int Id;
    public Team Team;
    /// <summary>
    /// The bore, step by step: the cell of its floor (the bore is two cells high over each, a metre wide and two high),
    /// when each step falls in if it is left unshored (never while it is shored), whether it is shored with a frame of
    /// props, and whether it has fallen in.
    /// </summary>
    public readonly List<int> Cells = new();
    public readonly List<double> FallAt = new();
    public readonly List<bool> Shored = new();
    public readonly List<bool> Fallen = new();
    /// <summary>How many steps are dug out; and, for the horde's, where it was begun (the cell its miners stood in).</summary>
    public int Dug, Mouth;
    public TunnelState State;
    /// <summary>The horde's: the ground under our wall at its end (packed voxels, the columns from the middle out), how much
    /// of it is out, and the props set in it (each post's lowest voxel, two by two).</summary>
    public readonly List<int> Chamber = new();
    public int ChamberDug;
    public readonly List<int> Posts = new();
    public int PostsSet;
    public double FireAt = -1;
    /// <summary>Heard by ours, and where they make it out to be: the mark over it on the ground.</summary>
    public bool Heard;
    public float MarkX, MarkZ;
    public double KnockAt;
    public bool Breached, Flooded;
    public readonly List<Unit> Crew = new();
    /// <summary>Seconds of digging owed toward the next voxel, and earth to be carried out to the heap by the mouth.</summary>
    public float Effort;
    public int Spoil;
}

/// <summary>
/// Mines and countermines. A band with miners coming up on a wall of ours (twelve to thirty metres off along its way)
/// sends them under the ground: from where they stand they dig down a step a metre and on under everything, a metre
/// wide and two high, three metres under the lowest ground on the way (under a ditch too), and up again under the wall,
/// shoring each metre with a frame of props, the earth carried out to a heap by the mouth. Under the wall they dig out
/// a chamber five metres along it right up to its footing, setting posts under it as they go; then they set fire to the
/// posts and go, and when the posts have burnt through the wall comes down into the hole (Sim.Collapse, Sim.Strain).
/// Those of ours within twenty-four metres of where they dig, up on the ground, hear the picks: the chronicle tells of
/// it, and the ground over the face shows a mark that trembles with every stroke, the nearer the listener the truer.
/// Our countermine is a line drawn with the pick as a mine: a bore the same as theirs, dug by the workers and shored
/// as they go; with no logs for a frame they dig no further than the last one set (Sim.Undermine.cs). Where ours comes within a metre of theirs it breaks in: the soldiers can be sent down, and
/// ours there knock out the horde's props, and what is not shored falls in a little after, burying whoever is in it;
/// so does any mine left without props a minute and a half or more, and one shaken by a blast. Water in a mine floods
/// it: the horde gives that one up.
/// </summary>
public sealed partial class Sim
{
    public readonly List<Tunnel> Tunnels = new();
    int _nextTunnel = 1;
    double _tunnelLook, _tunnelSaid = -99, _breachSaid = -99, _fallSaid = -99, _shoreSaid = -99;

    /// <summary>A stroke of the picks was heard: the mine, and the mark on the ground over it (for its tremble and the sound).</summary>
    public Action<Tunnel, float, float, float> PicksHeard;

    /// <summary>How far off ours hear picks under the ground, voxels.</summary>
    public const float HearReach = 24 * VoxelWorld.PerMetre;
    /// <summary>How long a mine stands without props, seconds (each step a little more or less).</summary>
    const float Unshored = 90;
    /// <summary>The logs a frame of props takes.</summary>
    const double FrameLogs = .15;
    /// <summary>How much faster a miner digs a mine than it hacks through what is in its way: it is what it is for.</summary>
    const float MinePace = 3;
    /// <summary>How far ahead along its way a wall of ours has to be for miners to go under rather than at it, metres.</summary>
    const int MineNear = 12, MineFar = 30;

    public static int BoreY0(int cell, NavGrid nav) => nav.Y(cell) << NavGrid.Shift;

    // ------------------------------------------------------------ the bore

    /// <summary>
    /// The steps of a bore from one column to another (cells, in a line, each beside the last), the floor of each: down a
    /// step a metre from <paramref name="topY"/> to <paramref name="runY"/>, along, and up a step a metre to
    /// <paramref name="endY"/> at the end.
    /// </summary>
    List<int> BoreCells(int ai, int ak, int bi, int bk, int topY, int runY, int endY)
    {
        var cols = new List<(int i, int k)> { (ai, ak) };
        int i = ai, k = ak;
        while (i != bi || k != bk)
        {
            int di = Math.Sign(bi - i), dk = Math.Sign(bk - k);
            // a step at a time along one way, the one there is more of to go
            if (di != 0 && (dk == 0 || Math.Abs(bi - i) * Math.Abs(ak - bk) >= Math.Abs(bk - k) * Math.Abs(ai - bi))) i += di; else k += dk;
            cols.Add((i, k));
        }
        var cells = new List<int>(cols.Count);
        int n = cols.Count - 1;
        for (int s = 0; s <= n; s++)
        {
            int y = Math.Max(Math.Max(runY, topY - s), endY - (n - s));
            y = Math.Clamp(y, 2, Nav.NY - 3);
            cells.Add(Nav.Id(cols[s].i, y, cols[s].k));
        }
        return cells;
    }

    /// <summary>The voxels of a step of a bore: two cells over its floor.</summary>
    IEnumerable<(int x, int y, int z)> Bore(int cell)
    {
        int x0 = Nav.I(cell) << NavGrid.Shift, y0 = Nav.Y(cell) << NavGrid.Shift, z0 = Nav.K(cell) << NavGrid.Shift;
        for (int y = y0 + 2 * NavGrid.CS - 1; y >= y0; y--)
            for (int z = z0; z < z0 + NavGrid.CS; z++)
                for (int x = x0; x < x0 + NavGrid.CS; x++)
                    yield return (x, y, z);
    }

    /// <summary>
    /// The voxels of a step of a bore as it is dug: two cells over its floor, and where it goes down a step or up one, a
    /// third over the higher side, for the head of whoever steps down or up it.
    /// </summary>
    IEnumerable<(int x, int y, int z)> Step(List<int> cells, int s)
    {
        foreach (var v in Bore(cells[s])) yield return v;
        if (s == 0) yield break;
        int here = cells[s], back = cells[s - 1];
        int extra = Nav.Y(here) < Nav.Y(back) ? Nav.Id(Nav.I(here), Nav.Y(here) + 2, Nav.K(here))
            : Nav.Y(here) > Nav.Y(back) ? Nav.Id(Nav.I(back), Nav.Y(back) + 2, Nav.K(back)) : -1;
        if (extra < 0) yield break;
        int x0 = Nav.I(extra) << NavGrid.Shift, y0 = Nav.Y(extra) << NavGrid.Shift, z0 = Nav.K(extra) << NavGrid.Shift;
        for (int y = y0 + NavGrid.CS - 1; y >= y0; y--)
            for (int z = z0; z < z0 + NavGrid.CS; z++)
                for (int x = x0; x < x0 + NavGrid.CS; x++)
                    yield return (x, y, z);
    }

    /// <summary>What a mine digs through: the valley's own ground and rock (not the bedrock), not anything built.</summary>
    static bool Minable(byte m) => m != Mat.Air && m != Mat.Water && m != Mat.Bed && !Mat.IsPlayer[m] && !Mat.Passable[m]
        && (Labour.Diggable(m) || Mat.Def[m].Group is MatGroup.Rock or MatGroup.Earth);

    static int MineGroup(byte m) => Mat.Def[m].Group switch { MatGroup.Wood => 1, MatGroup.Stone => 2, MatGroup.Rock => 3, _ => 0 };

    /// <summary>A frame of props over a step: posts at its four corners and a cap round the top, set only where it is open.</summary>
    void Shore(int cell)
    {
        int x0 = Nav.I(cell) << NavGrid.Shift, y0 = Nav.Y(cell) << NavGrid.Shift, z0 = Nav.K(cell) << NavGrid.Shift, top = y0 + 2 * NavGrid.CS - 1;
        void Prop(int x, int y, int z) { if (World.InBounds(x, y, z) && World.Get(x, y, z) == Mat.Air) World.Set(x, y, z, Mat.Prop); }
        for (int y = y0; y <= top; y++) { Prop(x0, y, z0); Prop(x0 + 7, y, z0); Prop(x0, y, z0 + 7); Prop(x0 + 7, y, z0 + 7); }
        for (int a = 1; a < 7; a++) { Prop(x0 + a, top, z0); Prop(x0 + a, top, z0 + 7); Prop(x0, top, z0 + a); Prop(x0 + 7, top, z0 + a); }
    }

    /// <summary>Whether the frame over a step still stands: any of its posts.</summary>
    bool FrameStands(int cell)
    {
        int x0 = Nav.I(cell) << NavGrid.Shift, y0 = Nav.Y(cell) << NavGrid.Shift, z0 = Nav.K(cell) << NavGrid.Shift;
        int y = y0 + 4;
        return World.Get(x0, y, z0) == Mat.Prop || World.Get(x0 + 7, y, z0) == Mat.Prop || World.Get(x0, y, z0 + 7) == Mat.Prop || World.Get(x0 + 7, y, z0 + 7) == Mat.Prop;
    }

    /// <summary>The frame over a step knocked out: its timber back to the stores when ours take it.</summary>
    int Unshore(int cell)
    {
        int n = 0;
        foreach (var (x, y, z) in Bore(cell)) if (World.Get(x, y, z) == Mat.Prop) { World.Set(x, y, z, Mat.Air); n++; }
        return n;
    }

    /// <summary>Where a mine is being dug now: the middle of its face (voxels).</summary>
    (float x, float y, float z) Face(Tunnel t)
    {
        if (t.State == TunnelState.Chamber || t.Dug >= t.Cells.Count) { var (x, y, z) = Nav.Feet(t.Cells[^1]); return (x, y, z); }
        var (fx, fy, fz) = Nav.Feet(t.Cells[Math.Min(t.Dug, t.Cells.Count - 1)]);
        return (fx, fy, fz);
    }

    // ------------------------------------------------------------ the horde's miners

    /// <summary>
    /// A miner of the horde: digging its mine, or following the one who does, or — coming up on a wall of ours — sending
    /// its band's miners under it. True while it is about a mine.
    /// </summary>
    bool TunnelTick(Unit u, float dt)
    {
        var t = u.Bore;
        if (t == null)
        {
            if (u.Routed || u.Guard || u.CarveCell >= 0 || Time < u.NextBoreLook) return false;
            u.NextBoreLook = Time + 2 + (u.Id % 7) * .1;
            t = BeginMine(u);
            if (t == null) return false;
        }
        if (t.State is TunnelState.Fired or TunnelState.Done || t.Flooded) { u.Bore = null; return false; }
        u.Path = u.Path is { } p && u.PathI < p.Count ? p : null;
        // the first of the crew still standing digs; the rest keep behind it
        Unit digger = null;
        foreach (var c in t.Crew) if (!c.Dead && !c.Routed && c.Bore == t) { digger = c; break; }
        if (digger == null) return false;
        int stand = t.Dug == 0 ? t.Mouth : t.Cells[Math.Min(t.Dug, t.Cells.Count) - 1];
        if (u != digger) stand = t.Dug <= 1 ? t.Mouth : t.Cells[Math.Max(0, Math.Min(t.Dug, t.Cells.Count) - 2)];
        // the one digging is at the face when the step it digs is beside it (it may have dropped into what it dug)
        int face = t.Cells[Math.Min(t.Dug, t.Cells.Count - 1)];
        bool atFace = u == digger && Math.Abs(Nav.I(u.Cell) - Nav.I(face)) <= 1 && Math.Abs(Nav.K(u.Cell) - Nav.K(face)) <= 1 && Math.Abs(Nav.Y(u.Cell) - Nav.Y(face)) <= 2;
        if (!atFace && u.Cell != stand)
        {
            if (u.Path == null || u.PathI >= u.Path.Count)
            {
                if (Time < u.NextSearch) { u.State = UnitState.Idle; return true; }
                u.NextSearch = Time + 1;
                if (!GoTo(u, stand, adjacent: u != digger, budget: 6000)) { u.State = UnitState.Idle; return true; }
            }
            if (u.Path != null && u.PathI < u.Path.Count) { Walk(u, dt); return true; }
        }
        if (u != digger) { u.State = UnitState.Idle; return true; }
        int crew = 0;
        foreach (var c in t.Crew) if (!c.Dead && !c.Routed && c.Bore == t) crew++;
        DigMine(t, u, dt * MinePace * Math.Min(2, 1 + .5f * (crew - 1)));
        return true;
    }

    /// <summary>
    /// A miner looks along its way for a wall of ours twelve to thirty metres on; finding one, its band's miners near it
    /// go under: the mine is laid out from where it stands to under the middle of the wall. Null when not.
    /// </summary>
    Tunnel BeginMine(Unit u)
    {
        if (Tunnels.Count >= 12 || FieldOf(u.T) is not { } f) return null;
        foreach (var o in Tunnels) if (o.Team == Team.Horde && o.State is TunnelState.Digging or TunnelState.Chamber && o.Crew.Exists(c => c.Band == u.Band && u.Band != null)) return null;
        // the ground it stands on: not up on anything, not in water
        int i0 = Nav.I(u.Cell), y0 = Nav.Y(u.Cell), k0 = Nav.K(u.Cell);
        if (Nav.Deep(u.Cell) || Nav.Ladder[u.Cell] != 0 || y0 < 4) return null;
        int c = u.Cell, wall = -1, steps = 0;
        for (; steps < MineFar + 2; steps++)
        {
            int next = f.Next(Nav, c, out float carve, out _);
            if (next < 0) return null;
            if (carve > 0 && OursIn(next)) { wall = next; break; }
            c = next;
        }
        if (wall < 0 || steps < MineNear) return null;
        int wi = Nav.I(wall), wk = Nav.K(wall), cx = -1, cz = -1;
        // the wall's footing in that cell, the column of it nearest the middle: the lowest of ours over the ground
        int foot = -1, nearest = int.MaxValue;
        for (int z = wk << NavGrid.Shift; z < (wk + 1) << NavGrid.Shift; z++)
            for (int x = wi << NavGrid.Shift; x < (wi + 1) << NavGrid.Shift; x++)
            {
                int d = Math.Abs(2 * x - (2 * (wi << NavGrid.Shift) + 7)) + Math.Abs(2 * z - (2 * (wk << NavGrid.Shift) + 7));
                if (d >= nearest) continue;
                for (int y = (Nav.Y(wall) << NavGrid.Shift) + 12; y > (Nav.Y(wall) << NavGrid.Shift) - 24 && y > 2; y--)
                    if (Footing(x, y, z)) { foot = y; cx = x; cz = z; nearest = d; break; }
            }
        if (foot < 0) return null;
        int endY = (foot - 2 * NavGrid.CS) >> NavGrid.Shift;
        // three metres under the lowest ground on the way, and never up through a ditch
        int low = int.MaxValue;
        int si = i0, sk = k0;
        for (int s = 0; s <= 64; s++)
        {
            int x = ((si + (wi - si) * s / 64) << NavGrid.Shift) + 4, z = ((sk + (wk - sk) * s / 64) << NavGrid.Shift) + 4;
            low = Math.Min(low, World.SurfAt(x, z));
        }
        int runY = Math.Min(endY, (low >> NavGrid.Shift) - 3);
        var cells = BoreCells(i0, k0, wi, wk, y0 - 1, runY, endY);
        if (cells.Count < MineNear || cells.Count > MineFar + 8) return null;
        var t = new Tunnel { Id = _nextTunnel++, Team = Team.Horde, Mouth = u.Cell };
        foreach (int cell in cells) { t.Cells.Add(cell); t.FallAt.Add(double.NaN); t.Shored.Add(false); t.Fallen.Add(false); }
        PlanChamber(t, foot, cx, cz, i0 - wi, k0 - wk);
        if (t.Chamber.Count < 200) return null;
        Tunnels.Add(t);
        // the band's miners near it go with it
        foreach (var o in Units)
            if (o.T == Kinds.Miner && !o.Dead && !o.Routed && o.Bore == null && (o == u || o.Band == u.Band && u.Band != null && Dist(o, u) < 12 * VoxelWorld.PerMetre))
            { o.Bore = t; t.Crew.Add(o); StopCarve(o); o.Path = null; }
        return t;
    }

    /// <summary>The lowest voxel of a wall of ours in a column, over the valley's ground (a hair's gap under it let be).</summary>
    bool Footing(int x, int y, int z)
    {
        byte m = World.Get(x, y, z);
        if (!Mat.IsPlayer[m] || !Holds(m)) return false;
        for (int k = 1; k <= 2; k++)
        {
            byte under = World.Get(x, y - k, z);
            if (Ground(under) && !Mat.IsPlayer[under]) return true;
            if (under != Mat.Air) return false;
        }
        return false;
    }

    /// <summary>Whether a cell holds anything of ours.</summary>
    bool OursIn(int cell)
    {
        int x0 = Nav.I(cell) << NavGrid.Shift, y0 = Nav.Y(cell) << NavGrid.Shift, z0 = Nav.K(cell) << NavGrid.Shift;
        for (int y = y0; y < y0 + NavGrid.CS; y += 2)
            for (int z = z0; z < z0 + NavGrid.CS; z += 2)
                for (int x = x0; x < x0 + NavGrid.CS; x += 2)
                    if (Mat.IsPlayer[World.Get(x, y, z)]) return true;
        return false;
    }

    /// <summary>
    /// The chamber under a wall of ours at the end of a mine: the ground under its footing, two and a half metres each way
    /// along it (across the way the mine came), the whole of its thickness, two metres down from the footing; and the
    /// posts to be set in it, two voxels square every six, under the wall.
    /// </summary>
    void PlanChamber(Tunnel t, int foot, int cx, int cz, int fromI, int fromK)
    {
        bool alongX = Math.Abs(fromK) >= Math.Abs(fromI);
        var cols = new List<(int x, int z, int foot, float along)>();
        for (int a = -20; a <= 20; a++)
            for (int b = -10; b <= 10; b++)
            {
                int x = alongX ? cx + a : cx + b, z = alongX ? cz + b : cz + a;
                // the lowest of ours over the ground in this column, about as low as the middle's
                int f = -1;
                for (int y = foot + 8; y >= foot - 8 && y > 2; y--) if (Footing(x, y, z)) { f = y; break; }
                if (f < 0) continue;
                cols.Add((x, z, f, MathF.Abs(a) + MathF.Abs(b) * .25f));
            }
        cols.Sort((p, q) => p.along.CompareTo(q.along));
        var posts = new HashSet<(int, int)>();
        foreach (var (x, z, f, _) in cols)
        {
            for (int y = f - 1; y >= f - 2 * NavGrid.CS && y > 1; y--)
                if (Minable(World.Get(x, y, z))) t.Chamber.Add(WaterPlanner.Pack(x, y, z));
            // (a post stands from the chamber's floor up to the footing)
            int px = x - (((x - cx) % 6 + 6) % 6), pz = z - (((z - cz) % 6 + 6) % 6);
            if (posts.Add((px, pz)) && cols.Exists(q => q.x == px && q.z == pz) && cols.Exists(q => q.x == px + 1 && q.z == pz + 1))
                t.Posts.Add(WaterPlanner.Pack(px, f - 2 * NavGrid.CS, pz));
        }
    }

    /// <summary>
    /// The one at the face digs: the next step of the bore, a voxel at a time as hard as it is, the earth out to the heap
    /// by the mouth, each step shored as soon as it is open; at the end, the chamber under the wall, posts set under it as
    /// each is dug round; then, packed, the posts are set alight and the crew goes. The picks are heard by ours near.
    /// </summary>
    void DigMine(Tunnel t, Unit u, float budget)
    {
        u.State = UnitState.Carve;
        u.Swing = 1;
        var rate = u.T.Move.Carve;
        if (Time >= t.KnockAt) { t.KnockAt = Time + .9 + (u.Id % 5) * .1; Knock(t); }
        if (t.State == TunnelState.Digging)
        {
            int cell = t.Cells[t.Dug];
            u.Facing = MathF.Atan2((Nav.K(cell) << NavGrid.Shift) + 4 - u.Z, (Nav.I(cell) << NavGrid.Shift) + 4 - u.X);
            t.Effort += budget;
            foreach (var (x, y, z) in Step(t.Cells, t.Dug))
            {
                byte m = World.Get(x, y, z);
                if (m == Mat.Water) { t.Flooded = true; return; }
                // into something of ours on the way: the chamber is dug from here
                if (Mat.IsPlayer[m] && Holds(m)) { t.State = TunnelState.Chamber; t.Cells.RemoveRange(t.Dug, t.Cells.Count - t.Dug); t.FallAt.RemoveRange(t.Dug, t.FallAt.Count - t.Dug); t.Shored.RemoveRange(t.Dug, t.Shored.Count - t.Dug); t.Fallen.RemoveRange(t.Dug, t.Fallen.Count - t.Dug); return; }
                if (!Minable(m)) continue;
                float cost = 1f / Math.Max(1, rate[MineGroup(m)]);
                if (t.Effort < cost) return;
                t.Effort -= cost;
                World.Set(x, y, z, Mat.Air);
                if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
                if (++t.Spoil % 12 == 0) Heap(t);
                if (t.Spoil % 40 == 0) Hacked?.Invoke(x, y, z, m);
            }
            // the step is open: shored, and on to the next
            t.Effort = 0;
            t.FallAt[t.Dug] = double.NaN;
            Shore(cell);
            t.Shored[t.Dug] = true;
            if (++t.Dug >= t.Cells.Count) t.State = TunnelState.Chamber;
            return;
        }
        if (t.State != TunnelState.Chamber) return;
        if (t.ChamberDug < t.Chamber.Count)
        {
            t.Effort += budget;
            while (t.ChamberDug < t.Chamber.Count)
            {
                var (x, y, z) = WaterPlanner.Unpack(t.Chamber[t.ChamberDug]);
                byte m = World.Get(x, y, z);
                if (!Minable(m)) { t.ChamberDug++; continue; }
                float cost = 1f / Math.Max(1, rate[MineGroup(m)]);
                if (t.Effort < cost) break;
                t.Effort -= cost;
                World.Set(x, y, z, Mat.Air);
                if (++t.Spoil % 12 == 0) Heap(t);
                if (t.Spoil % 40 == 0) Hacked?.Invoke(x, y, z, m);
                t.ChamberDug++;
                SetPosts(t);
            }
            return;
        }
        SetPosts(t);
        // all out and propped: packed with brushwood a few seconds, set alight, and away
        if (t.FireAt < 0) { t.FireAt = Time + 4; return; }
        if (Time < t.FireAt) return;
        foreach (int p in t.Posts)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            for (int k = 0; k < 2 * NavGrid.CS; k += 5) Ignite(x, y + k, z);
        }
        t.State = TunnelState.Fired;
        foreach (var c in t.Crew) if (c.Bore == t) c.Bore = null;
        if (Time - _tunnelSaid > 20)
        {
            _tunnelSaid = Time;
            var (fx, _, fz) = Face(t);
            Say("Орда подожгла крепь под нашей стеной. Стена рухнет, когда крепь прогорит.", 3, fx, fz);
        }
    }

    /// <summary>The posts under the wall set wherever the ground is out round them: two voxels square, floor to footing.</summary>
    void SetPosts(Tunnel t)
    {
        for (int n = t.PostsSet; n < t.Posts.Count; n++)
        {
            var (x, y, z) = WaterPlanner.Unpack(t.Posts[n]);
            bool open = true;
            for (int k = 0; k < 2 * NavGrid.CS && open; k++)
                for (int d = 0; d < 4; d++)
                    if (Minable(World.Get(x + (d & 1), y + k, z + (d >> 1)))) { open = false; break; }
            if (!open) return;
            for (int k = 0; k < 2 * NavGrid.CS; k++)
                for (int d = 0; d < 4; d++)
                {
                    int px = x + (d & 1), pz = z + (d >> 1), py = y + k;
                    if (World.Get(px, py, pz) == Mat.Air) World.Set(px, py, pz, Mat.Prop);
                }
            t.PostsSet = n + 1;
        }
    }

    /// <summary>
    /// A voxel of earth out of the mine onto the heap behind its mouth: thrown near the middle of it, it rolls down any side
    /// steeper than earth lies, so the heap grows as a mound; none of it rolls back into the mouth.
    /// </summary>
    void Heap(Tunnel t)
    {
        var (mx, _, mz) = Nav.Feet(t.Mouth);
        var (ex, _, ez) = Nav.Feet(t.Cells[^1]);
        float dx = mx - ex, dz = mz - ez, d = MathF.Max(1, MathF.Sqrt(dx * dx + dz * dz));
        float hx = mx + dx / d * 16, hz = mz + dz / d * 16;
        float ang = (float)Hash.H3(t.Id, t.Spoil, 5) * 2 * MathF.PI, r = 10 * (float)(Hash.H3(t.Id, t.Spoil, 6) * Hash.H3(t.Id, t.Spoil, 7));
        int x = (int)(hx + MathF.Cos(ang) * r), z = (int)(hz + MathF.Sin(ang) * r);
        if (!World.InBounds(x, 1, z)) return;
        int y = World.SurfAt(x, z);
        for (int step = 0; step < 24; step++)
        {
            int bx = x, bz = z, by = y - 1;
            for (int k = 0; k < 4; k++)
            {
                int dir = (k + t.Spoil / 12) & 3;
                int nx = x + (dir == 0 ? 1 : dir == 1 ? -1 : 0), nz = z + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                if (!World.InBounds(nx, 1, nz) || (nx - mx) * (nx - mx) + (nz - mz) * (nz - mz) < 64) continue;
                int ny = World.SurfAt(nx, nz);
                if (ny < by) { by = ny; bx = nx; bz = nz; }
            }
            if (bx == x && bz == z) break;
            x = bx; z = bz; y = by;
        }
        if (!World.InBounds(x, y, z) || World.Get(x, y, z) != Mat.Air) return;
        World.Set(x, y, z, Mat.Soil);
    }

    /// <summary>
    /// A stroke of the picks under the ground: heard by ours up on the ground within reach, the first time told; the mark
    /// over the face is moved toward where it is, the nearer the listener the truer.
    /// </summary>
    void Knock(Tunnel t)
    {
        var (fx, fy, fz) = Face(t);
        float near = float.MaxValue;
        Unit listener = null;
        foreach (var o in Units)
        {
            if (o.Team != Team.Player || o.Dead || o.Inside || o.Y < fy + NavGrid.CS) continue;
            float dx = o.X - fx, dz = o.Z - fz, d = MathF.Sqrt(dx * dx + dz * dz);
            if (d < near) { near = d; listener = o; }
        }
        if (listener == null || near > HearReach) return;
        // off by a fifth of how far the listener is, a different way each stroke, and drawn toward it
        float off = near * .2f, ang = (float)Hash.H3(t.Id, (int)(Time * 3), 77) * 2 * MathF.PI;
        float tx = fx + MathF.Cos(ang) * off, tz = fz + MathF.Sin(ang) * off;
        if (!t.Heard) { t.MarkX = tx; t.MarkZ = tz; }
        else { t.MarkX += (tx - t.MarkX) * .35f; t.MarkZ += (tz - t.MarkZ) * .35f; }
        if (!t.Heard)
        {
            t.Heard = true;
            if (Time - _tunnelSaid > 20)
            {
                _tunnelSaid = Time;
                Say($"Под землёй стучат кирки: орда роет подкоп {Side(t.MarkX, t.MarkZ)}. Копайте навстречу: линия с киркой, вид «подкоп».", 3, t.MarkX, t.MarkZ);
            }
        }
        int gx = (int)t.MarkX, gz = (int)t.MarkZ;
        PicksHeard?.Invoke(t, t.MarkX, World.InBounds(gx, 1, gz) ? World.SurfAt(gx, gz) : fy, t.MarkZ);
    }

    /// <summary>Which side of the citadel a spot is on, as the chronicle says it: «с запада», «с северо-востока».</summary>
    string Side(float x, float z)
    {
        float a = MathF.Atan2(-(z - Site.KeepZ), x - Site.KeepX) * 180 / MathF.PI;
        string[] names = { "с востока", "с северо-востока", "с севера", "с северо-запада", "с запада", "с юго-запада", "с юга", "с юго-востока" };
        return names[((int)MathF.Round(a / 45) % 8 + 8) % 8];
    }

    // ------------------------------------------------------------ our countermines

    /// <summary>
    /// A countermine drawn from one column to another: a bore down a step a metre from the ground at its start to so many
    /// voxels under it, and along. The voxels to dig (the valley's own ground in it), and the mine, not yet kept: <see
    /// cref="AddCountermine"/> keeps it once its digging is marked.
    /// </summary>
    public VoxelList PlanCountermine(int ax, int az, int bx, int bz, int depth, out Tunnel mine)
    {
        mine = null;
        var plan = new VoxelList();
        if (!World.InBounds(ax, 1, az) || !World.InBounds(bx, 1, bz)) return plan;
        int top = World.SurfAt(ax, az) >> NavGrid.Shift, down = Math.Max(2, depth >> NavGrid.Shift);
        int runY = top - down;
        var cells = BoreCells(ax >> NavGrid.Shift, az >> NavGrid.Shift, bx >> NavGrid.Shift, bz >> NavGrid.Shift, top - 1, runY, runY);
        mine = new Tunnel { Id = 0, Team = Team.Player, Mouth = cells[0] };
        foreach (int cell in cells) { mine.Cells.Add(cell); mine.FallAt.Add(double.NaN); mine.Shored.Add(false); mine.Fallen.Add(false); }
        var seen = new HashSet<int>();
        for (int s = 0; s < cells.Count; s++)
            foreach (var (x, y, z) in Step(cells, s))
                if (Minable(World.Get(x, y, z)) && seen.Add(WaterPlanner.Pack(x, y, z))) plan.Add(x, y, z, Mat.Air);
        return plan;
    }

    /// <summary>
    /// For the tests and the staged scenes: a mine dug out at once and shored from one column to another, so many voxels
    /// under the ground at its start, the horde's (not yet heard) or ours.
    /// </summary>
    public Tunnel DigMineNow(int ax, int az, int bx, int bz, int depth, Team team)
    {
        var plan = PlanCountermine(ax, az, bx, bz, depth, out var t);
        if (t == null) return null;
        for (int i = 0; i < plan.Count; i++) World.Set(plan.X(i), plan.Y(i), plan.Z(i), Mat.Air);
        t.Team = team;
        t.Id = _nextTunnel++;
        t.Mouth = t.Cells[0];
        for (int s = 0; s < t.Cells.Count; s++) { Shore(t.Cells[s]); t.Shored[s] = true; t.FallAt[s] = double.NaN; }
        t.Dug = t.Cells.Count;
        t.KnockAt = Time;
        Tunnels.Add(t);
        return t;
    }

    /// <summary>A countermine whose digging is marked: kept, to be shored as it goes and to break into the horde's.</summary>
    public void AddCountermine(Tunnel mine)
    {
        if (mine == null || mine.Cells.Count == 0) return;
        mine.Id = _nextTunnel++;
        mine.KnockAt = Time;
        Tunnels.Add(mine);
    }

    /// <summary>Whether a step of a bore is all dug out (nothing of the ground left in it).</summary>
    bool Open(Tunnel t, int s)
    {
        foreach (var (x, y, z) in Step(t.Cells, s)) if (Minable(World.Get(x, y, z))) return false;
        return true;
    }

    // ------------------------------------------------------------ the mines kept

    /// <summary>
    /// Twice a second: our countermines' steps found dug and shored while there are logs; what has gone without props too
    /// long falls in, and props burnt or knocked out leave their step to fall in soon; ours breaking into the horde's, and
    /// ours there knocking out its props; water in a mine.
    /// </summary>
    void TickTunnels(float dt)
    {
        if (Tunnels.Count == 0 || Time < _tunnelLook) return;
        _tunnelLook = Time + .5;
        for (int n = Tunnels.Count - 1; n >= 0; n--)
        {
            var t = Tunnels[n];
            if (t.Team == Team.Player) TendCountermine(t);
            for (int i = 0; i < t.Dug && i < t.Cells.Count; i++)
            {
                if (t.Fallen[i]) continue;
                if (t.Shored[i] && !FrameStands(t.Cells[i]))
                {
                    // burnt or knocked out: it holds a few seconds more
                    t.Shored[i] = false;
                    t.FallAt[i] = Time + 6;
                }
                if (!t.Shored[i] && !double.IsNaN(t.FallAt[i]) && Time >= t.FallAt[i]) CaveIn(t, i);
            }
            if (!t.Flooded && t.Dug > 0)
            {
                foreach (int cell in t.Cells.GetRange(0, Math.Min(t.Dug, t.Cells.Count)))
                {
                    int x = (Nav.I(cell) << NavGrid.Shift) + 4, y = (Nav.Y(cell) << NavGrid.Shift) + 1, z = (Nav.K(cell) << NavGrid.Shift) + 4;
                    if (World.Get(x, y, z) != Mat.Water && World.Get(x, y + 2, z) != Mat.Water) continue;
                    t.Flooded = true;
                    if (t.Team == Team.Horde && t.State is TunnelState.Digging or TunnelState.Chamber)
                    {
                        t.State = TunnelState.Done;
                        foreach (var c in t.Crew) if (c.Bore == t) c.Bore = null;
                        Say("Подкоп орды затоплен.", 1, x, z);
                    }
                    break;
                }
            }
            // two posts in three burnt through somewhere along them, the rest cannot carry the wall
            if (t.State == TunnelState.Fired && 3 * t.Posts.FindAll(BurntThrough).Count >= 2 * t.Posts.Count)
            {
                t.State = TunnelState.Done;
                Sink(t);
            }
            // a countermine marked and never dug (the marking taken back) is forgotten
            if (t.Team == Team.Player && t.Dug == 0 && Time - t.KnockAt > 20 && !Marked(t)) Tunnels.RemoveAt(n);
        }
        Breaches();
    }

    /// <summary>Whether a post (2 by 2 voxels, two metres high) is burnt through: at some height none of it is left.</summary>
    bool BurntThrough(int post)
    {
        var (x, y, z) = WaterPlanner.Unpack(post);
        for (int k = 0; k < 2 * NavGrid.CS; k++)
        {
            bool left = false;
            for (int d = 0; d < 4 && !left; d++) left = World.Get(x + (d & 1), y + k, z + (d >> 1)) == Mat.Prop;
            if (!left) return true;
        }
        return false;
    }

    /// <summary>
    /// The posts under a wall burnt through: what is built over the chamber, from its foot up, comes down into it, each
    /// block of it whole (Sim.Collapse.cs); what is left beside it cracks or holds as it may. Where the chamber was filled
    /// in again, nothing comes down.
    /// </summary>
    void Sink(Tunnel t)
    {
        // the chamber's roof in each column
        var roofs = new Dictionary<int, int>();
        foreach (int p in t.Chamber)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            int key = x * World.SZ + z;
            if (!roofs.TryGetValue(key, out int r) || y > r) roofs[key] = y;
        }
        var falling = new HashSet<int>();
        float sx = 0, sz = 0;
        int cols = 0;
        foreach (var (key, roof) in roofs)
        {
            int x = key / World.SZ, z = key % World.SZ;
            // open under it still (loose stone fallen in counts as open; earth filled in does not)
            byte under = World.Get(x, roof, z);
            if (under != Mat.Air && under != Mat.Rubble) continue;
            int k = roof + 1;
            while (k < roof + 4 && !Built(World.Get(x, k, z))) k++;
            if (!Built(World.Get(x, k, z))) continue;
            for (; k < World.SY && Built(World.Get(x, k, z)); k++) falling.Add(WaterPlanner.Pack(x, k, z));
            sx += x; sz += z; cols++;
        }
        if (falling.Count == 0) return;
        Fall(falling, quiet: false);
        Say("Крепь прогорела. Стена над подкопом рухнула.", 2, sx / cols, sz / cols);
        static bool Built(byte m) => Mat.IsPlayer[m] && Holds(m);
    }

    bool Marked(Tunnel t)
    {
        for (int s = 0; s < t.Cells.Count; s++)
            foreach (var (x, y, z) in Step(t.Cells, s))
                if (Orders.At(x, y, z) != 0) return true;
        return false;
    }

    /// <summary>Our countermine: the steps dug out in order from its mouth, each shored if there are logs for it, and none dug past one left bare.</summary>
    void TendCountermine(Tunnel t)
    {
        // a step left without props for want of logs is shored as soon as there are logs, before it falls in
        for (int i = 0; i < t.Dug && i < t.Cells.Count; i++)
            if (!t.Shored[i] && !t.Fallen[i] && Stock[Stockpile.Wood] >= FrameLogs && Stock.Take(Stockpile.Wood, FrameLogs))
            {
                Shore(t.Cells[i]); t.Shored[i] = true; t.FallAt[i] = double.NaN;
            }
        while (t.Dug < t.Cells.Count && Open(t, t.Dug))
        {
            int cell = t.Cells[t.Dug];
            t.FallAt[t.Dug] = Time + Unshored + Hash.H3(t.Id, t.Dug, 41) * 40;
            if (Stock.Take(Stockpile.Wood, FrameLogs)) { Shore(cell); t.Shored[t.Dug] = true; t.FallAt[t.Dug] = double.NaN; }
            else if (Time - _shoreSaid > 60)
            {
                _shoreSaid = Time;
                var (x, _, z) = Nav.Feet(cell);
                Say("Подкоп без крепи осыплется: нужны брёвна.", 2, x, z);
            }
            t.Dug++;
        }
        // nobody digs on past a step without props, under ground that may fall in: the rest of the bore waits for logs
        // (with none in the stores, past the step being dug, which there will be nothing to shore with)
        HoldBore(t, t.Dug >= t.Cells.Count ? -1 : t.Dug > 0 && !t.Shored[t.Dug - 1] ? t.Dug : Stock[Stockpile.Wood] < FrameLogs ? t.Dug + 1 : -1);
    }

    /// <summary>
    /// Ours coming within a metre of the horde's: the ground between is opened and the two are one, and it is told; then
    /// ours in it or beside it knock out the horde's props there, a frame each second and a half, and those steps fall in
    /// a little after.
    /// </summary>
    void Breaches()
    {
        foreach (var ours in Tunnels)
        {
            if (ours.Team != Team.Player || ours.Dug == 0) continue;
            foreach (var theirs in Tunnels)
            {
                if (theirs.Team != Team.Horde || theirs.Dug == 0) continue;
                if (!theirs.Breached)
                {
                    int a = Math.Max(0, ours.Dug - 3);
                    for (int i = a; i < ours.Dug && !theirs.Breached; i++)
                        for (int j = 0; j < theirs.Dug && j < theirs.Cells.Count; j++)
                        {
                            int p = ours.Cells[i], q = theirs.Cells[j];
                            if (Math.Abs(Nav.I(p) - Nav.I(q)) > 1 || Math.Abs(Nav.K(p) - Nav.K(q)) > 1 || Math.Abs(Nav.Y(p) - Nav.Y(q)) > 2) continue;
                            BreakThrough(p, q);
                            theirs.Breached = ours.Breached = true;
                            if (Time - _breachSaid > 20)
                            {
                                _breachSaid = Time;
                                var (x, _, z) = Nav.Feet(p);
                                Say("Наш подкоп прорвался в подкоп орды. Рабочие выбивают крепь, бойцов можно послать вниз.", 3, x, z);
                            }
                            break;
                        }
                }
                if (theirs.Breached) PullProps(theirs);
            }
        }
    }

    /// <summary>The ground between two bores side by side dug out, so that the two are one.</summary>
    void BreakThrough(int p, int q)
    {
        int x0 = Math.Min(Nav.I(p), Nav.I(q)) << NavGrid.Shift, x1 = (Math.Max(Nav.I(p), Nav.I(q)) + 1) << NavGrid.Shift;
        int z0 = Math.Min(Nav.K(p), Nav.K(q)) << NavGrid.Shift, z1 = (Math.Max(Nav.K(p), Nav.K(q)) + 1) << NavGrid.Shift;
        int y0 = Math.Max(Nav.Y(p), Nav.Y(q)) << NavGrid.Shift, y1 = (Math.Min(Nav.Y(p), Nav.Y(q)) + 2) << NavGrid.Shift;
        if (y1 - y0 < NavGrid.CS) y1 = y0 + 2 * NavGrid.CS;
        for (int y = y0; y < y1; y++)
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                    if (Minable(World.Get(x, y, z))) World.Set(x, y, z, Mat.Air);
    }

    /// <summary>Ours in the horde's mine or beside it knock out its props, a frame each, a step at a time.</summary>
    void PullProps(Tunnel t)
    {
        foreach (var o in Units)
        {
            if (o.Team != Team.Player || o.Dead || o.Inside || o.Down || Time < o.NextBoreLook || o.T.NoHands) continue;
            int best = -1, bestD = int.MaxValue;
            for (int i = 0; i < t.Dug && i < t.Cells.Count; i++)
            {
                if (!t.Shored[i] || t.Fallen[i]) continue;
                int c = t.Cells[i];
                int d = Math.Max(Math.Abs(Nav.I(c) - Nav.I(o.Cell)), Math.Abs(Nav.K(c) - Nav.K(o.Cell)));
                if (d <= 1 && Math.Abs(Nav.Y(c) - Nav.Y(o.Cell)) <= 2 && d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) continue;
            o.NextBoreLook = Time + 1.5;
            int n = Unshore(t.Cells[best]);
            Stock[Stockpile.Wood] += n / 512.0;
            t.Shored[best] = false;
            t.FallAt[best] = Time + 5;
            o.Swing = 1;
        }
    }

    /// <summary>
    /// A step of a mine falls in: its bore full of loose earth, the ground over it sunk into a hollow, whoever is in it
    /// struck and buried (Sim.Trauma.cs).
    /// </summary>
    void CaveIn(Tunnel t, int i)
    {
        t.Fallen[i] = true;
        int cell = t.Cells[i];
        int n = 0;
        foreach (var (x, y, z) in Bore(cell))
        {
            byte m = World.Get(x, y, z);
            if (m == Mat.Air || m == Mat.Prop) { World.Set(x, y, z, Mat.Soil); n++; }
        }
        // the ground over it sinks: deepest over its middle
        int cx = (Nav.I(cell) << NavGrid.Shift) + 4, cz = (Nav.K(cell) << NavGrid.Shift) + 4;
        for (int dz = -7; dz <= 7; dz++)
            for (int dx = -7; dx <= 7; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (!World.InBounds(x, 1, z)) continue;
                int sink = 4 - Math.Max(Math.Abs(dx), Math.Abs(dz)) / 2;
                int y = World.SurfAt(x, z) - 1;
                for (int k = 0; k < sink && y - k > 1; k++)
                {
                    byte m = World.Get(x, y - k, z);
                    if (Mat.IsPlayer[m] || !Ground(m)) break;
                    World.Set(x, y - k, z, Mat.Air);
                }
            }
        // whoever is in it is struck by the earth coming down and buried
        int bottom = Nav.Y(cell);
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Airborne != null) continue;
            if (Nav.I(o.Cell) != Nav.I(cell) || Nav.K(o.Cell) != Nav.K(cell) || Nav.Y(o.Cell) < bottom || Nav.Y(o.Cell) > bottom + 1) continue;
            float e = 3500;
            StrikeHeap(o, new[] { e * .3f, e * .4f, e * .15f, e * .15f }, "обвал подкопа", 25);
            o.StruckAt = Time;
        }
        // the frames beside it, shaken, often go too, and the fall runs on along the gallery
        for (int d = -1; d <= 1; d += 2)
        {
            int j = i + d;
            if (j < 0 || j >= t.Dug || j >= t.Cells.Count || t.Fallen[j] || !t.Shored[j] || Hash.H3(t.Id, j, 53) > .6) continue;
            Unshore(t.Cells[j]);
            t.Shored[j] = false;
            t.FallAt[j] = Time + 1.5 + Hash.H3(t.Id, j, 59) * 1.5;
        }
        var (fx, fy, fz) = Nav.Feet(cell);
        Collapsed?.Invoke(n, fx, fy + NavGrid.CS, fz);
        if ((t.Heard || t.Team == Team.Player) && Time - _fallSaid > 20)
        {
            _fallSaid = Time;
            Say(t.Team == Team.Horde ? "Подкоп орды обвалился." : "Наш подкоп обвалился.", t.Team == Team.Horde ? 1 : 2, fx, fz);
        }
    }

    /// <summary>A blast or a heavy blow over a mine: a step without props within reach falls in, a shored one may lose its frame.</summary>
    void JarTunnels(float fx, float fy, float fz, float r)
    {
        if (Tunnels.Count == 0) return;
        float reach = r + 2 * NavGrid.CS;
        foreach (var t in Tunnels)
            for (int i = 0; i < t.Dug && i < t.Cells.Count; i++)
            {
                if (t.Fallen[i]) continue;
                var (x, y, z) = Nav.Feet(t.Cells[i]);
                float dx = x - fx, dy = y + NavGrid.CS - fy, dz = z - fz;
                if (dx * dx + dy * dy + dz * dz > reach * reach) continue;
                if (!t.Shored[i]) { CaveIn(t, i); continue; }
                if (Hash.H3(t.Id, i, (int)Time) < .4) { Unshore(t.Cells[i]); t.Shored[i] = false; t.FallAt[i] = Time + 2; }
            }
    }

    // ------------------------------------------------------------ kept in a save

    void WriteTunnels(BinaryWriter w)
    {
        w.Write(_nextTunnel);
        w.Write(Tunnels.Count);
        foreach (var t in Tunnels)
        {
            w.Write(t.Id); w.Write((byte)t.Team); w.Write((byte)t.State); w.Write(t.Dug); w.Write(t.Mouth);
            w.Write(t.Cells.Count);
            for (int i = 0; i < t.Cells.Count; i++) { w.Write(t.Cells[i]); w.Write(t.FallAt[i]); w.Write(t.Shored[i]); w.Write(t.Fallen[i]); }
            w.Write(t.Chamber.Count); foreach (int p in t.Chamber) w.Write(p);
            w.Write(t.ChamberDug);
            w.Write(t.Posts.Count); foreach (int p in t.Posts) w.Write(p);
            w.Write(t.PostsSet); w.Write(t.FireAt);
            w.Write(t.Heard); w.Write(t.MarkX); w.Write(t.MarkZ); w.Write(t.Breached); w.Write(t.Flooded); w.Write(t.Effort); w.Write(t.Spoil);
            var crew = t.Crew.FindAll(c => !c.Dead && c.Bore == t);
            w.Write(crew.Count); foreach (var c in crew) w.Write(c.Id);
        }
    }

    void ReadTunnels(BinaryReader r)
    {
        Tunnels.Clear();
        _nextTunnel = r.ReadInt32();
        var byId = new Dictionary<int, Unit>();
        foreach (var u in Units) byId[u.Id] = u;
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var t = new Tunnel { Id = r.ReadInt32(), Team = (Team)r.ReadByte(), State = (TunnelState)r.ReadByte(), Dug = r.ReadInt32(), Mouth = r.ReadInt32() };
            for (int i = r.ReadInt32(); i > 0; i--) { t.Cells.Add(r.ReadInt32()); t.FallAt.Add(r.ReadDouble()); t.Shored.Add(r.ReadBoolean()); t.Fallen.Add(r.ReadBoolean()); }
            for (int i = r.ReadInt32(); i > 0; i--) t.Chamber.Add(r.ReadInt32());
            t.ChamberDug = r.ReadInt32();
            for (int i = r.ReadInt32(); i > 0; i--) t.Posts.Add(r.ReadInt32());
            t.PostsSet = r.ReadInt32(); t.FireAt = r.ReadDouble();
            t.Heard = r.ReadBoolean(); t.MarkX = r.ReadSingle(); t.MarkZ = r.ReadSingle(); t.Breached = r.ReadBoolean(); t.Flooded = r.ReadBoolean(); t.Effort = r.ReadSingle(); t.Spoil = r.ReadInt32();
            for (int i = r.ReadInt32(); i > 0; i--)
                if (byId.TryGetValue(r.ReadInt32(), out var c)) { t.Crew.Add(c); c.Bore = t; }
            Tunnels.Add(t);
        }
    }
}
