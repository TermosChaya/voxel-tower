using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>How far the architect goes with a valley's defences: a palisade and a gate; walls with posts, a ditch and stakes; all of it high and in stone.</summary>
public enum ArchStyle : byte { Modest, Strong, Sky }

/// <summary>
/// What a piece of the architect's plan is: new wall along a line; what stands mended, raised, or its timber taken down
/// for stone; a tower, a post, a gate cut through a wall, a gatehouse; the ditch, the stakes; a stair up a wall; a gate
/// hung again.
/// </summary>
public enum ArchWorkKind : byte { Wall, Mend, Raise, Unbuild, Tower, Post, Gate, Gatehouse, Ditch, Stakes, Stair, Hang }

/// <summary>
/// A piece of the architect's plan as he wrote it down (Sim.Architect.Survey.cs): what it is, its stage, the line it
/// is laid along or the cells of what stands that it works on, how high and how thick, in stone or timber, where it
/// stands and how it is turned, and which pieces it waits for. What it lays is worked out from this each time.
/// </summary>
public sealed class ArchWork
{
    public ArchWorkKind Kind;
    public int Stage;
    public string Name = "";
    public bool Stone;
    /// <summary>A wall's height over the ground (a tower's; a stair's top, as a height in the world), and a wall's thickness, voxels.</summary>
    public int H, Thick;
    /// <summary>Where it stands (a tower's middle, a post's, a gate's on its line, a stair's head), voxels; how it is turned.</summary>
    public int X, Z, Rot;
    /// <summary>The quarter about the point a ditch or stakes go along (0 north … 3 west).</summary>
    public int Quarter = -1;
    public ulong After;
    /// <summary>The line it is laid along, corner by corner (x, z pairs, voxels); the cells it works on (walking-grid columns, k·NX + i).</summary>
    public int[] Line = Array.Empty<int>(), Cells = Array.Empty<int>();
}

/// <summary>
/// What an architect has planned for the valley he is in: the line round the point it holds, corner by corner, and what
/// each stretch of it is over (new wall, what stands, water or a cliff); the pieces of work, each with its stage; the
/// side the horde comes from, the side of the main gate; how far he goes, whether the new wall is laid in stone or put
/// up as a palisade first; which pieces he has marked and which the workers have finished, and what he said of it.
/// </summary>
public sealed class ArchPlan
{
    public int CX, CZ;
    /// <summary>How far the line reaches from the point at the most, voxels.</summary>
    public int H;
    /// <summary>The ring's sides: 0 north (−z), 1 east (+x), 2 south (+z), 3 west (−x): the main gate's, the horde's.</summary>
    public int GateSide, HordeSide;
    public ArchStyle Style;
    /// <summary>The new wall in stone (else a palisade first); its main gate a gatehouse with iron leaves.</summary>
    public bool Stone, Gatehouse;
    /// <summary>Pieces marked, and pieces finished (by their index in <see cref="Works"/>).</summary>
    public ulong Marked, Done;
    /// <summary>The stages the chronicle was told of, a bit for each.</summary>
    public int Said;
    /// <summary>The line round the point, its corners in order (counter-clockwise, x east, z south), and what each stretch from a corner to the next is over.</summary>
    public readonly List<(int X, int Z)> Ring = new();
    public readonly List<byte> Edge = new();
    public readonly List<ArchWork> Works = new();
    /// <summary>What he found about the point and what he means to do, as he said it.</summary>
    public string Found = "";
}

public sealed partial class Sim
{
    /// <summary>The architect's ways, for the card: what he aims at.</summary>
    public static readonly string[] ArchStyleNames = { "сдержанно", "крепко", "до неба" };
    /// <summary>How far the valley's architect goes (the player sets it on his card).</summary>
    public ArchStyle ArchStyle = ArchStyle.Strong;
    /// <summary>
    /// The point the architect is to hold when it is not the citadel or the outpost's stone (a mine of a campaign's held
    /// cell): its middle and how far out what stands there reaches, voxels.
    /// </summary>
    public (int X, int Z, int R)? HoldPoint;
    /// <summary>The valley's plan (none till an architect has looked the ground over).</summary>
    public ArchPlan ArchPlan;
    /// <summary>What the architect is about and what he lacks, for his card and the chronicle; pieces he has marked since the start.</summary>
    public string ArchDoing = "", ArchLacks = "";
    public int ArchMarked, ArchMended;
    /// <summary>How many times he has planned, and how long the last plan took to make, ms (the bench asks).</summary>
    public int ArchPlans;
    public double ArchPlanMs;
    public const string ArchitectOne = "в долине уже есть архитектор: он один на долину";

    readonly HashSet<int> _archDuty = new();
    readonly HashSet<int> _archEngines = new();
    readonly Dictionary<int, (int left, double since)> _archLeft = new();
    /// <summary>The columns each piece marked takes (packed z·SX + x), and how many times each finished one was marked again.</summary>
    readonly Dictionary<int, HashSet<int>> _archCols = new();
    readonly Dictionary<int, int> _archAgain = new();
    /// <summary>The new walls of the plan that pass by a wall that stood when he planned them.</summary>
    readonly HashSet<int> _archBeside = new();
    double _archMendAt, _archLookedAt, _archMoved, _archPlannedAt = double.NegativeInfinity;
    /// <summary>He is to plan again (the way was changed, what was built stands in the way of what he meant).</summary>
    bool _archReplan;

    /// <summary>The ring's sides: the way out of each, and the way along it (clockwise).</summary>
    static readonly (int ox, int oz, int tx, int tz)[] RingSides = { (0, -1, 1, 0), (1, 0, 0, 1), (0, 1, -1, 0), (-1, 0, 0, -1) };

    internal enum ArchKind : byte { Lay, Dig, Template, Gate, Mend, Hang }

    /// <summary>One piece of the plan as it stands now: what it is, its stage, the box it is built in, where the architect stands to mark it, and what it lays.</summary>
    public sealed class ArchPiece
    {
        public int Index, Stage;
        public string Name = "";
        public ArchWorkKind Work;
        internal ArchKind Kind;
        public int X0, Z0, X1, Z1, X, Z;
        /// <summary>Its voxels as they would be marked now (the gate's from its plan), worked out when asked.</summary>
        internal Func<VoxelList> Lay;
        internal Func<TemplatePlan> Template;
        internal Func<GatePlan> Gate;
        /// <summary>Waits for these pieces to be finished first.</summary>
        public ulong After;
        /// <summary>How it is drawn on the ground: the line it goes along (x, z pairs), or a ring so wide about where it stands.</summary>
        public int[] Line = Array.Empty<int>();
        public int Round, AtX, AtZ;
    }

    static readonly string[] ArchStages =
    {
        "ставит стену", "ставит ворота к дороге", "ставит башни", "копает ров", "ставит колья перед стеной", "меняет частокол на камень",
        "чинит старую стену", "поднимает стену", "ставит лестницу на стену", "навешивает ворота",
    };

    public static bool IsArchitect(Unit u) => u.T == Kinds.Architect;

    /// <summary>The point the valley's defences go round: the given one, else the citadel, else the outpost's stone, and how far out what stands there reaches.</summary>
    (int x, int z, int r) PointToHold() =>
        HoldPoint is { } p ? (p.X, p.Z, p.R)
        : Site.HasKeep ? (Site.KeepX, Site.KeepZ, Site.KeepR + 40)
        : (Site.KeepX, Site.KeepZ, Holdings.Square + 8);

    /// <summary>How high the ring's walls stand for a way, voxels; how thick a stone one is.</summary>
    static int WallHeight(ArchStyle s) => s == ArchStyle.Sky ? 32 : s == ArchStyle.Strong ? 24 : 20;
    const int WallThick = 8;

    int GroundAt(int x, int z) => Site.HTop != null && x >= 0 && z >= 0 && x < World.SX && z < World.SZ ? Site.HTop[z * World.SX + x] : World.SurfAt(x, z);

    /// <summary>The side of the ring facing a way from its middle.</summary>
    static int SideToward(float dx, float dz)
    {
        int s = 0;
        float best = float.MinValue;
        for (int i = 0; i < 4; i++) { float d = RingSides[i].ox * dx + RingSides[i].oz * dz; if (d > best) { best = d; s = i; } }
        return s;
    }

    /// <summary>Whether two segments cross, and how far along the first (0 to 1).</summary>
    static bool Crosses(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz, out float u)
    {
        u = 0;
        float rx = bx - ax, rz = bz - az, sx = dx - cx, sz = dz - cz, den = rx * sz - rz * sx;
        if (Math.Abs(den) < 1e-6f) return false;
        float t = ((cx - ax) * sz - (cz - az) * sx) / den, v = ((cx - ax) * rz - (cz - az) * rx) / den;
        u = t;
        return t >= 0 && t <= 1 && v >= 0 && v <= 1;
    }

    static bool Blocking(byte m) => m != 0 && !Mat.Passable[m];

    // ------------------------------------------------------------ the pieces

    /// <summary>The plan's pieces, one for each of its works, in the order they were planned (the order they are marked in).</summary>
    public List<ArchPiece> ArchPieces(ArchPlan p)
    {
        var list = new List<ArchPiece>(p.Works.Count);
        for (int i = 0; i < p.Works.Count; i++) list.Add(PieceOf(p, i, p.Works[i]));
        return list;
    }

    ArchPiece PieceOf(ArchPlan p, int i, ArchWork w)
    {
        var piece = new ArchPiece { Index = i, Stage = w.Stage, Name = w.Name, Work = w.Kind, After = w.After, X = w.X, Z = w.Z, AtX = w.X, AtZ = w.Z };
        void Box(int pad)
        {
            if (w.Line.Length >= 2)
            {
                piece.X0 = piece.Z0 = int.MaxValue; piece.X1 = piece.Z1 = int.MinValue;
                for (int k = 0; k + 1 < w.Line.Length; k += 2)
                {
                    piece.X0 = Math.Min(piece.X0, w.Line[k] - pad); piece.X1 = Math.Max(piece.X1, w.Line[k] + pad);
                    piece.Z0 = Math.Min(piece.Z0, w.Line[k + 1] - pad); piece.Z1 = Math.Max(piece.Z1, w.Line[k + 1] + pad);
                }
            }
            else if (w.Cells.Length > 0)
            {
                piece.X0 = piece.Z0 = int.MaxValue; piece.X1 = piece.Z1 = int.MinValue;
                foreach (int col in w.Cells)
                {
                    int x = (col % Nav.NX) << NavGrid.Shift, z = (col / Nav.NX) << NavGrid.Shift;
                    piece.X0 = Math.Min(piece.X0, x - pad); piece.X1 = Math.Max(piece.X1, x + NavGrid.CS - 1 + pad);
                    piece.Z0 = Math.Min(piece.Z0, z - pad); piece.Z1 = Math.Max(piece.Z1, z + NavGrid.CS - 1 + pad);
                }
            }
            else { piece.X0 = w.X - pad; piece.Z0 = w.Z - pad; piece.X1 = w.X + pad; piece.Z1 = w.Z + pad; }
        }
        // where he stands to look at it: its middle, two metres toward the point
        void Stand(int x, int z)
        {
            float d = (float)Math.Max(1, Brushes.Dist(p.CX - x, p.CZ - z));
            piece.X = x + (int)((p.CX - x) / d * 16); piece.Z = z + (int)((p.CZ - z) / d * 16);
        }
        switch (w.Kind)
        {
            case ArchWorkKind.Wall:
                piece.Kind = ArchKind.Lay; piece.Line = w.Line; Box(w.Thick / 2 + 8);
                piece.Lay = () => WallAlong(p, w);
                Stand((piece.X0 + piece.X1) / 2, (piece.Z0 + piece.Z1) / 2);
                break;
            case ArchWorkKind.Mend:
                piece.Kind = ArchKind.Mend; Box(0);
                Stand((piece.X0 + piece.X1) / 2, (piece.Z0 + piece.Z1) / 2);
                break;
            case ArchWorkKind.Raise:
                piece.Kind = ArchKind.Lay; Box(0);
                piece.Lay = () => RaiseOver(p, w);
                Stand((piece.X0 + piece.X1) / 2, (piece.Z0 + piece.Z1) / 2);
                break;
            case ArchWorkKind.Unbuild:
                piece.Kind = ArchKind.Dig; piece.Line = w.Line; Box(4);
                piece.Lay = () => WoodIn(w);
                Stand((piece.X0 + piece.X1) / 2, (piece.Z0 + piece.Z1) / 2);
                break;
            case ArchWorkKind.Tower:
                piece.Kind = ArchKind.Template; piece.Round = 20; Box(32);
                piece.Template = () => RoundTower(World, Site, w.X, w.Z, w.Rot, w.H);
                Stand(w.X, w.Z);
                break;
            case ArchWorkKind.Post:
                piece.Kind = ArchKind.Template; piece.Round = 12; Box(20);
                piece.Template = () => Watchtower(World, Site, w.X, w.Z, w.Rot);
                Stand(w.X, w.Z);
                break;
            case ArchWorkKind.Gatehouse:
                piece.Kind = ArchKind.Template; piece.Round = 30; Box(48);
                piece.Template = () => Gatehouse(World, Site, w.X, w.Z, w.Rot);
                Stand(w.X, w.Z);
                break;
            case ArchWorkKind.Gate:
                piece.Kind = ArchKind.Gate; piece.Round = 10; Box(24);
                piece.Gate = () => CutPlan(w.X, w.Z, RingSides[w.Rot].ox, RingSides[w.Rot].oz);
                Stand(w.X, w.Z);
                break;
            case ArchWorkKind.Hang:
                piece.Kind = ArchKind.Hang; piece.Round = 10; Box(16);
                Stand(w.X, w.Z);
                break;
            case ArchWorkKind.Ditch:
            case ArchWorkKind.Stakes:
            {
                bool ditch = w.Kind == ArchWorkKind.Ditch;
                piece.Kind = ditch ? ArchKind.Dig : ArchKind.Lay;
                // its quarter of the line, out to where it goes
                int far = ditch ? DitchOff + DitchWidth : StakesFar;
                piece.X0 = piece.Z0 = int.MaxValue; piece.X1 = piece.Z1 = int.MinValue;
                var pts = QuarterOfRing(p, w.Quarter);
                foreach (var (x, z) in pts)
                {
                    piece.X0 = Math.Min(piece.X0, x - far); piece.X1 = Math.Max(piece.X1, x + far);
                    piece.Z0 = Math.Min(piece.Z0, z - far); piece.Z1 = Math.Max(piece.Z1, z + far);
                }
                if (pts.Count == 0) { piece.X0 = piece.X1 = p.CX; piece.Z0 = piece.Z1 = p.CZ; }
                piece.Line = Flat(pts);
                piece.Lay = () => ditch ? DitchIn(p, w) : StakesIn(p, w);
                var (mx, mz) = pts.Count > 0 ? pts[pts.Count / 2] : (p.CX, p.CZ);
                Stand(mx, mz);
                break;
            }
            default:
                piece.Kind = ArchKind.Lay; piece.Line = w.Line; Box(12);
                piece.Lay = () => Brushes.Stairs(World, Site, w.Line[0], w.Line[1], w.H, w.Line[2], w.Line[3], Mat.Stone);
                piece.X = w.Line[2]; piece.Z = w.Line[3];
                break;
        }
        return piece;
    }

    /// <summary>
    /// A wall along a line: so thick, each column standing so high over its own ground, the stretches meeting round at
    /// the corners; in stone with merlons along its outer edge (none over a gate), in timber as logs side by side three
    /// wide, their tops sharpened by turns. Nothing in a gateway of ours or in the place of a gatehouse of the plan.
    /// </summary>
    VoxelList WallAlong(ArchPlan p, ArchWork w)
    {
        var L = new VoxelList();
        var line = Unflat(w.Line);
        if (line.Count < 2) return L;
        double half = w.Thick / 2.0, run = 0;
        var done = new HashSet<int>();
        var gates = GatePoints(p);
        int pad = (int)Math.Ceiling(half) + 1;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var (ax, az) = line[i]; var (bx, bz) = line[i + 1];
            double len = Math.Sqrt((double)(bx - ax) * (bx - ax) + (double)(bz - az) * (bz - az));
            if (len < 1) continue;
            // outward is to the right of the way along: the line runs counter-clockwise
            double ux = (bx - ax) / len, uz = (bz - az) / len, nx = uz, nz = -ux;
            for (int z = Math.Min(az, bz) - pad; z <= Math.Max(az, bz) + pad; z++)
                for (int x = Math.Min(ax, bx) - pad; x <= Math.Max(ax, bx) + pad; x++)
                {
                    double rx = x + .5 - ax, rz = z + .5 - az, along = rx * ux + rz * uz, across = rx * nx + rz * nz;
                    if (along < 0) { if (rx * rx + rz * rz > half * half) continue; }
                    else if (along > len) { double ex = x + .5 - bx, ez = z + .5 - bz; if (ex * ex + ez * ez > half * half) continue; }
                    else if (Math.Abs(across) > half) continue;
                    if (!done.Add(z * World.SX + x) || !World.OnLand(x, z) || Site.Forbidden(x, z) || InGateway(x, z) || InGatehouse(p, x, z)) continue;
                    int g = GroundAt(x, z);
                    // never over a wall that stands: where the new meets it, it stops at its face
                    if (OursTop(x, z, g) - g >= Fortification.Bars) continue;
                    if (w.Stone)
                    {
                        Column(World, L, x, z, g, g + w.H, Mat.Stone);
                        if (across >= half - 3 && Merlon(run + Math.Clamp(along, 0, len)) && !NearGate(gates, x, z, Gateway.Half + 6))
                            Column(World, L, x, z, g + w.H, g + w.H + 5, Mat.Stone);
                    }
                    else Column(World, L, x, z, g, g + w.H - 4 + ((int)Math.Floor(run + along) / 3 % 2 == 0 ? 3 : 0), Mat.Wood);
                }
            run += len;
        }
        return L;
    }

    /// <summary>
    /// What stands raised to the way's height: each column of its cells that is wall (standing or meant) and lower, laid up
    /// in stone to so high over its ground, with merlons on the outer edge.
    /// </summary>
    VoxelList RaiseOver(ArchPlan p, ArchWork w)
    {
        var L = new VoxelList();
        var gates = GatePoints(p);
        bool IsWall(int x, int z, out int g, out int top)
        {
            g = GroundAt(x, z);
            top = Math.Max(OursTop(x, z, g), MeantTop(x, z, g));
            return top - g >= Fortification.Bars;
        }
        foreach (int col in w.Cells)
        {
            int x0 = (col % Nav.NX) << NavGrid.Shift, z0 = (col / Nav.NX) << NavGrid.Shift;
            for (int z = z0; z < z0 + NavGrid.CS; z++)
                for (int x = x0; x < x0 + NavGrid.CS; x++)
                {
                    if (!IsWall(x, z, out int g, out int top) || top >= g + w.H || InGateway(x, z) || InHouse(x, z, 2)) continue;
                    Column(World, L, x, z, top, g + w.H, Mat.Stone);
                    // the outer edge: the column a step out from the point is no wall
                    float d = (float)Math.Max(1, Brushes.Dist(x - p.CX, z - p.CZ));
                    int ox = x + (int)Math.Round((x - p.CX) / d * 2), oz = z + (int)Math.Round((z - p.CZ) / d * 2);
                    if (!IsWall(ox, oz, out _, out _) && Merlon(x + z) && !NearGate(gates, x, z, Gateway.Half + 6)) Column(World, L, x, z, g + w.H, g + w.H + 5, Mat.Stone);
                }
        }
        return L;
    }

    /// <summary>
    /// The timber of a palisade to take down: the logs of the columns it stands on that rise from the ground to under
    /// four and a half metres (not a post's), along its line or in its cells.
    /// </summary>
    VoxelList WoodIn(ArchWork w)
    {
        var L = new VoxelList();
        foreach (int key in WorkColumns(w))
        {
            int x = key % World.SX, z = key / World.SX, g = GroundAt(x, z);
            if (World.Get(x, g, z) != Mat.Wood && World.Get(x, g + 1, z) != Mat.Wood) continue;
            int top = g;
            while (top < g + 40 && World.Get(x, top, z) == Mat.Wood) top++;
            if (top - g > 36) continue;
            for (int y = Math.Max(1, g - 2); y < top; y++) if (World.Get(x, y, z) == Mat.Wood) L.Add(x, y, z, Mat.Air);
        }
        return L;
    }

    /// <summary>The columns a work takes, packed z·SX + x: its line's band, or its cells.</summary>
    HashSet<int> WorkColumns(ArchWork w)
    {
        var cols = new HashSet<int>();
        if (w.Line.Length >= 4)
        {
            var line = Unflat(w.Line);
            double half = Math.Max(2, w.Thick / 2.0 + 1);
            for (int i = 0; i + 1 < line.Count; i++)
            {
                var (ax, az) = line[i]; var (bx, bz) = line[i + 1];
                int pad = (int)Math.Ceiling(half) + 1;
                for (int z = Math.Min(az, bz) - pad; z <= Math.Max(az, bz) + pad; z++)
                    for (int x = Math.Min(ax, bx) - pad; x <= Math.Max(ax, bx) + pad; x++)
                        if (World.OnLand(x, z) && SegDist(x + .5, z + .5, ax, az, bx, bz) <= half) cols.Add(z * World.SX + x);
            }
        }
        foreach (int col in w.Cells)
        {
            int x0 = (col % Nav.NX) << NavGrid.Shift, z0 = (col / Nav.NX) << NavGrid.Shift;
            for (int z = z0; z < z0 + NavGrid.CS; z++) for (int x = x0; x < x0 + NavGrid.CS; x++) if (World.OnLand(x, z)) cols.Add(z * World.SX + x);
        }
        return cols;
    }

    /// <summary>Where the gates of the plan are and our gates in walls are (not houses' doors).</summary>
    List<(int x, int z)> GatePoints(ArchPlan p)
    {
        var list = new List<(int x, int z)>();
        foreach (var w in p.Works) if (w.Kind is ArchWorkKind.Gate or ArchWorkKind.Gatehouse) list.Add((w.X, w.Z));
        foreach (var g in Gates) { var (mx, _, mz) = g.Middle; if (!InHouse((int)mx, (int)mz, 4)) list.Add(((int)mx, (int)mz)); }
        return list;
    }

    static bool NearGate(List<(int x, int z)> gates, int x, int z, int by)
    {
        foreach (var (gx, gz) in gates) if (Math.Abs(gx - x) < by && Math.Abs(gz - z) < by) return true;
        return false;
    }

    /// <summary>Whether a column is in the opening of a gate of ours in a wall, or within a voxel of it.</summary>
    bool InGateway(int x, int z)
    {
        foreach (var g in Gates)
        {
            int along = g.AlongX ? x : z, across = g.AlongX ? z : x;
            if (along >= g.U0 - 1 && along <= g.U0 + Gateway.Width && Math.Abs(across - g.P) <= 12) return true;
        }
        return false;
    }

    /// <summary>Whether a column is where a gatehouse of the plan stands (seven metres along its wall, two across).</summary>
    static bool InGatehouse(ArchPlan p, int x, int z)
    {
        foreach (var w in p.Works)
        {
            if (w.Kind != ArchWorkKind.Gatehouse) continue;
            // (as the template lays it: its facing out, and along the wall square to that)
            int dx = (w.Rot & 3) switch { 1 => 1, 3 => -1, _ => 0 }, dz = (w.Rot & 3) switch { 0 => 1, 2 => -1, _ => 0 };
            int a = (x - w.X) * -dz + (z - w.Z) * dx, d = (x - w.X) * dx + (z - w.Z) * dz;
            if (a >= -45 && a <= 44 && d >= -9 && d <= 8) return true;
        }
        return false;
    }

    // ------------------------------------------------------------ the ditch and the stakes

    /// <summary>How far out from the line's middle the ditch begins, how wide and deep it is; where the stakes end.</summary>
    const int DitchOff = WallThick / 2 + 24, DitchWidth = 12, DitchDepth = 10, StakesNear = DitchOff + DitchWidth + 8, StakesFar = StakesNear + 16;

    /// <summary>The quarter of the ring a column is in, about the point (0 north … 3 west).</summary>
    static int QuarterOf(ArchPlan p, int x, int z) => SideToward(x - p.CX, z - p.CZ);

    /// <summary>The corners of the line in a quarter about the point, in order.</summary>
    static List<(int x, int z)> QuarterOfRing(ArchPlan p, int q)
    {
        var list = new List<(int x, int z)>();
        foreach (var (x, z) in p.Ring) if (QuarterOf(p, x, z) == q) list.Add((x, z));
        return list;
    }

    /// <summary>How far a column is from the line, the stretch nearest it, and whether it is inside the line.</summary>
    static (double d, int edge, bool inside) FromRing(ArchPlan p, double x, double z)
    {
        double best = double.MaxValue;
        int edge = -1, n = p.Ring.Count;
        bool inside = false;
        for (int i = 0; i < n; i++)
        {
            var (ax, az) = p.Ring[i]; var (bx, bz) = p.Ring[(i + 1) % n];
            double d = SegDist(x, z, ax, az, bx, bz);
            if (d < best) { best = d; edge = i; }
            if ((az > z) != (bz > z) && x < ax + (bx - ax) * (z - az) / (double)(bz - az)) inside = !inside;
        }
        return (best, edge, inside);
    }

    /// <summary>
    /// The ditch along a quarter of the line, outside it: dug down a man's height from the ground as it was, a causeway
    /// left at each gate and where a road crosses it; none along water or a cliff, under what we built, or by a house.
    /// </summary>
    VoxelList DitchIn(ArchPlan p, ArchWork w)
    {
        var L = new VoxelList();
        var pieceBox = PieceOf(p, 0, w);
        var gates = GatePoints(p);
        for (int z = pieceBox.Z0; z <= pieceBox.Z1; z++)
            for (int x = pieceBox.X0; x <= pieceBox.X1; x++)
            {
                if (QuarterOf(p, x, z) != w.Quarter || !World.OnLand(x, z) || Site.Forbidden(x, z) || ByWayIn(gates, x, z, 20)) continue;
                var (d, edge, inside) = FromRing(p, x + .5, z + .5);
                if (inside || d < DitchOff || d > DitchOff + DitchWidth || edge < 0 || p.Edge[edge] == LineNature || InHouse(x, z, 8)) continue;
                int g = World.SurfAt(x, z), bed = GroundAt(x, z) - DitchDepth;
                if (OursTop(x, z, GroundAt(x, z)) > 0) continue;
                for (int y = g - 1; y >= Math.Max(3, bed); y--)
                    if (Labour.Diggable(World.Get(x, y, z)) && !Mat.IsPlayer[World.Get(x, y, z)]) L.Add(x, y, z, Mat.Air);
                if (bed < g) Brushes.ClearVegAbove(World, L, x, z, g);
            }
        return L;
    }

    /// <summary>Stakes a hand apart before a quarter of the line, past the ditch, each a little taller or shorter than the next; none on a way in.</summary>
    VoxelList StakesIn(ArchPlan p, ArchWork w)
    {
        var L = new VoxelList();
        var pieceBox = PieceOf(p, 0, w);
        var gates = GatePoints(p);
        for (int z = pieceBox.Z0; z <= pieceBox.Z1; z++)
            for (int x = pieceBox.X0; x <= pieceBox.X1; x++)
            {
                if (x % 3 != 0 || z % 3 != 0 || QuarterOf(p, x, z) != w.Quarter || !World.OnLand(x, z) || Site.Forbidden(x, z) || ByWayIn(gates, x, z, 28)) continue;
                var (d, edge, inside) = FromRing(p, x + .5, z + .5);
                if (inside || d < StakesNear || d > StakesFar || edge < 0 || p.Edge[edge] == LineNature || InHouse(x, z, 8)) continue;
                // on the bare ground only: not in a hole, not on what lies or stands there
                int g = World.SurfAt(x, z), ground = GroundAt(x, z);
                if (g < ground - 2 || g > ground + 1 || OursTop(x, z, ground) > 0) continue;
                Column(World, L, x, z, g, g + 5 + (int)(Hash.H3(x, 7, z) * 3) - 1, Mat.Stakes);
            }
        return L;
    }

    /// <summary>Whether a column is by a gate (within so far) or on a road: left alone by the ditch and the stakes.</summary>
    bool ByWayIn(List<(int x, int z)> gates, int x, int z, int clear)
    {
        foreach (var (gx, gz) in gates) if (Brushes.Dist(x - gx, z - gz) < clear + 8) return true;
        return Site.RoadDist(x, z) < 10;
    }

    // ------------------------------------------------------------ at work

    /// <summary>
    /// The architect each tick, all he does: he looks over his plan now and then; he runs from the horde and takes
    /// shelter at the alarm as a worker does; he walks where he goes to look at a piece; he fights no one and lays no stone.
    /// </summary>
    bool ArchitectTick(Unit u, float dt)
    {
        ArchitectThink(u);
        if (Flee(u, dt) || Alarm && !u.Hold && TakeShelter(u, dt)) { Busy(u); return true; }
        if (u.Path != null && u.PathI < u.Path.Count && u.Pastime != Pastime.Stroll) { Busy(u); Walk(u, dt); return true; }
        // between marks he stands looking over the works, or strolls about them
        Idle(u, dt, stay: u.Hold);
        return true;
    }

    /// <summary>He looks the ground over and plans anew, and says what he found and means to do.</summary>
    void PlanAgain(Unit u)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        ArchPlan = MakeArchPlan();
        ArchPlanMs = clock.Elapsed.TotalMilliseconds;
        ArchPlans++;
        _archPlannedAt = _archMoved = Time;
        _archReplan = false;
        _archLeft.Clear(); _archCols.Clear(); _archAgain.Clear(); _archBeside.Clear();
        // new wall that has to pass by a wall that stands (there was no other way round) is not planned again for it
        for (int i = 0; i < ArchPlan.Works.Count; i++)
            if (ArchPlan.Works[i].Kind == ArchWorkKind.Wall && ArchPlan.Works[i].Stage == 0 && Overbuilt(ArchPlan.Works[i])) _archBeside.Add(i);
        Say(ArchPlan.Found, 0, u);
    }

    /// <summary>
    /// Every few seconds: with the horde about, he waits; after a raid he has what it broke mended; else he plans if he
    /// has not (or plans again once the way is changed, or once what was built since stands where he meant to build),
    /// and marks the next pieces — no more than two being built at once, each when the stores come to half of it at
    /// least — setting the idle workers to bring in what is short; and he stands our engines facing the horde.
    /// </summary>
    void ArchitectThink(Unit u)
    {
        if (u == Possessed || Time < u.NextOrders) return;
        u.NextOrders = Time + 4 + u.Id % 5 * .1;
        if (!u.Commands) { ArchDoing = "ждёт ваших приказов"; ArchLacks = ""; u.Why = "архитектор: ждёт ваших приказов"; ArchDuty(Stockpile.None); return; }
        if (ArchPlan == null || _archReplan && Time - _archPlannedAt > 10) PlanAgain(u);
        var p = ArchPlan;
        // the horde near the ring: nothing new is marked till it is gone
        if (HordeNear(p.CX, p.CZ, p.H + 60 * VoxelWorld.PerMetre))
        {
            ArchDoing = "орда рядом: ждёт"; u.Why = "архитектор: орда рядом, ждёт";
            return;
        }
        ArchitectMend(u, p);
        ArchitectEngines(p);
        // now and then: new wall of his not marked yet where something of ours stands now (built since he planned): he plans again
        if (Time >= _archLookedAt && Time - _archPlannedAt > 10)
        {
            _archLookedAt = Time + 15;
            for (int i = 0; i < p.Works.Count; i++)
                if (p.Works[i].Kind == ArchWorkKind.Wall && p.Works[i].Stage == 0 && (p.Marked >> i & 1) == 0 && !_archBeside.Contains(i) && Overbuilt(p.Works[i])) { _archReplan = true; break; }
        }
        var pieces = ArchPieces(p);
        int building = 0;
        string doing = "", lacks = "";
        int lackKind = Stockpile.None;
        ArchPiece next = null;
        var est = new double[Stockpile.Kinds];
        foreach (var piece in pieces)
        {
            ulong bit = 1UL << piece.Index;
            if ((p.Done & bit) != 0) continue;
            if ((p.Marked & bit) != 0)
            {
                // marked: finished when nothing is left marked in it
                int left = MarkedLeft(piece);
                if (left == 0)
                {
                    // what of it could not be laid when it was marked (something lay there) is marked again, twice at most
                    int again = _archAgain.GetValueOrDefault(piece.Index);
                    if (again < 2 && piece.Kind is ArchKind.Lay or ArchKind.Template && Voxels(piece).lay is { Count: > 24 } rest)
                    {
                        _archAgain[piece.Index] = again + 1;
                        if (DesignateBuild(rest) > 0) { Columns(piece.Index, rest); building++; if (doing == "") doing = piece.Name; continue; }
                    }
                    p.Done |= bit;
                    continue;
                }
                // (one that has not moved for four minutes — waiting on what cannot be laid — holds up nothing; when nothing
                // of the plan has moved for six, what is left of it is let go and it counts as done, so that what waits on
                // it goes on)
                if (!_archLeft.TryGetValue(piece.Index, out var was) || was.left != left) { _archLeft[piece.Index] = (left, Time); _archMoved = Time; }
                else if (Time - was.since > 360 && Time - _archMoved > 360) { LetGo(piece); p.Done |= bit; continue; }
                else if (Time - was.since > 240) continue;
                building++;
                if (doing == "") doing = piece.Name;
                continue;
            }
            if ((p.Done & piece.After) != piece.After) continue;
            if (next != null || building >= 2) continue;
            // new wall where something of ours stands now, built since he planned: he plans again, not over it
            if (piece.Work == ArchWorkKind.Wall && piece.Stage == 0 && Time - _archPlannedAt > 10 && !_archBeside.Contains(piece.Index) && Overbuilt(p.Works[piece.Index])) { _archReplan = true; continue; }
            // what it takes, against the stores
            var (lay, dig) = Voxels(piece);
            if (lay == null && dig == null && piece.Kind != ArchKind.Hang) { p.Done |= bit; continue; }
            Labour.Estimate(World, lay, false, est);
            // a palisade comes down for stone only once the stores have most of the stone for its wall
            if (piece.Work == ArchWorkKind.Unbuild) est[Stockpile.Stone] = Math.Max(est[Stockpile.Stone], StoneFor(p, piece.Index) * 1.6);
            int need = Stockpile.None;
            for (int k = 1; k < Stockpile.Kinds; k++)
                if (est[k] > 0 && Stock[k] < est[k] * .5) { need = k; break; }
            if (need != Stockpile.None)
            {
                // the stone the new wall would take not there yet: it goes up as a palisade first
                if (need == Stockpile.Stone && p.Stone && piece.Work == ArchWorkKind.Wall && piece.Stage == 0 && !AnyNewMarked(p)) { ToTimber(p); return; }
                if (lacks == "") { lacks = $"{Stockpile.Name(need)} {Fmt.M3(est[need] - Stock[need])} м³ на «{piece.Name}»"; lackKind = need; }
                continue;
            }
            next = piece;
            if (!Mark(p, piece, lay, dig)) { p.Done |= bit; next = null; continue; }
            p.Marked |= bit;
            ArchMarked++;
            building++;
            doing = piece.Name;
            if ((p.Said & 1 << piece.Stage) == 0)
            {
                p.Said |= 1 << piece.Stage;
                Say($"Архитектор {ArchStages[Math.Min(piece.Stage, ArchStages.Length - 1)]}: {piece.Name}.", 0, piece.X, piece.Z);
            }
            // he goes to look at it
            int at = NearestStand(piece.X, piece.Z, u.T.Move.Height);
            if (at >= 0 && CellsApart(u.Cell, at) > 2) Command(new[] { u }, at, stay: true);
        }
        bool all = (p.Done | ~PlanMask(pieces)) == ulong.MaxValue;
        ArchDoing = all ? "всё построено" : doing != "" ? doing : lacks != "" ? "ждёт материалов" : "ждёт";
        ArchLacks = lacks;
        u.Why = $"архитектор: {ArchDoing}{(lacks != "" ? $" · не хватает: {lacks}" : "")}";
        if (all && (p.Said & 1 << 30) == 0)
        {
            p.Said |= 1 << 30;
            Say(pieces.Count == 0 ? "Архитектор: здесь нечего строить." : "Архитектор: укрепления готовы. Дальше он только чинит то, что сломает орда.", 0, p.CX, p.CZ);
        }
        ArchDuty(lackKind);
    }

    static ulong PlanMask(List<ArchPiece> pieces)
    {
        ulong m = 0;
        foreach (var p in pieces) m |= 1UL << p.Index;
        return m;
    }

    /// <summary>The stone the wall that takes a palisade's place after a piece would take (voxels), by its length.</summary>
    static double StoneFor(ArchPlan p, int index)
    {
        foreach (var w in p.Works)
        {
            if (w.Kind != ArchWorkKind.Wall || w.After != 1UL << index) continue;
            double len = 0;
            for (int i = 0; i + 3 < w.Line.Length; i += 2) len += Brushes.Dist(w.Line[i + 2] - w.Line[i], w.Line[i + 3] - w.Line[i + 1]);
            return len * w.Thick * w.H;
        }
        return 0;
    }

    /// <summary>Whether any of the new wall is marked yet.</summary>
    static bool AnyNewMarked(ArchPlan p)
    {
        for (int i = 0; i < p.Works.Count; i++) if (p.Works[i].Kind == ArchWorkKind.Wall && p.Works[i].Stage == 0 && (p.Marked & 1UL << i) != 0) return true;
        return false;
    }

    /// <summary>The new wall put up as a palisade first, with stone after it as the stores allow: he plans again so.</summary>
    void ToTimber(ArchPlan p)
    {
        p.Stone = false;
        foreach (var w in p.Works) if (w.Kind == ArchWorkKind.Wall && w.Stage == 0) { w.Stone = false; w.Thick = 3; w.Name = "частокол"; }
        // the palisade put in stone later: its own pieces, after it
        if (p.Style == ArchStyle.Modest) return;
        int n = p.Works.Count;
        for (int i = 0; i < n && p.Works.Count < 63; i++)
        {
            var w = p.Works[i];
            if (w.Kind != ArchWorkKind.Wall || w.Stage != 0) continue;
            p.Works.Add(new ArchWork { Kind = ArchWorkKind.Unbuild, Stage = 5, Name = "частокол долой", Line = w.Line, Thick = 3, After = 1UL << i });
            p.Works.Add(new ArchWork { Kind = ArchWorkKind.Wall, Stage = 5, Name = "стена вместо частокола", Stone = true, H = w.H, Thick = WallThick, Line = w.Line, After = 1UL << (p.Works.Count - 1) });
        }
        // no gatehouse in a palisade: a gate of planks cut through it
        foreach (var w in p.Works) if (w.Kind == ArchWorkKind.Gatehouse) { w.Kind = ArchWorkKind.Gate; w.Name = "ворота"; w.Rot = w.Rot switch { 0 => 2, 1 => 1, 2 => 0, _ => 3 }; }
        p.Gatehouse = false;
    }

    /// <summary>
    /// Whether a quarter and more of the way along a new wall not built yet has a wall of ours standing on its line or
    /// within two and a half metres of it (built since he planned it): it would be laid over it or beside it.
    /// </summary>
    bool Overbuilt(ArchWork w)
    {
        var line = Unflat(w.Line);
        if (line.Count < 2) return false;
        int all = 0, taken = 0;
        double total = 0;
        for (int i = 0; i + 1 < line.Count; i++) total += Brushes.Dist(line[i + 1].x - line[i].x, line[i + 1].z - line[i].z);
        double run = 0;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var (ax, az) = line[i]; var (bx, bz) = line[i + 1];
            double len = Brushes.Dist(bx - ax, bz - az);
            if (len < 1) continue;
            double ux = (bx - ax) / len, uz = (bz - az) / len;
            for (double t = 0; t < len; t += 4)
            {
                // (its ends join walls that stand)
                if (run + t < 24 || total - run - t < 24) continue;
                all++;
                for (int off = -20; off <= 20; off += 4)
                {
                    int x = (int)Math.Round(ax + ux * t - uz * off), z = (int)Math.Round(az + uz * t + ux * off), g = GroundAt(x, z);
                    if (OursTop(x, z, g) - g >= Fortification.Bars) { taken++; break; }
                }
            }
            run += len;
        }
        return all > 0 && taken * 4 > all;
    }

    /// <summary>A piece's voxels as they would be marked now: what to lay, or what to dig out.</summary>
    (VoxelList lay, VoxelList dig) Voxels(ArchPiece piece)
    {
        switch (piece.Kind)
        {
            case ArchKind.Lay: return (Some(piece.Lay()), null);
            case ArchKind.Dig: return (null, Some(piece.Lay()));
            case ArchKind.Template: return (Some(piece.Template()?.Voxels), null);
            case ArchKind.Gate: return (piece.Gate() is { } g ? Some(g.Leaves) : null, null);
            case ArchKind.Mend:
            {
                var lay = new VoxelList(); var clear = new VoxelList();
                MendPlan(MeantOf(ArchPlan.Works[piece.Index]), lay, clear);
                return (Some(lay), Some(clear));
            }
            default:
                return (null, null);
        }
    }

    /// <summary>The meant voxels on the columns of a work's cells, packed.</summary>
    List<int> MeantOf(ArchWork w)
    {
        var list = new List<int>();
        foreach (int col in w.Cells)
        {
            int x0 = (col % Nav.NX) << NavGrid.Shift, z0 = (col / Nav.NX) << NavGrid.Shift;
            list.AddRange(MeantIn(x0, z0, x0 + NavGrid.CS - 1, z0 + NavGrid.CS - 1));
        }
        return list;
    }

    static VoxelList Some(VoxelList L) => L != null && L.Count > 0 ? L : null;

    /// <summary>Marks a piece for the workers; false if there is nothing of it to mark.</summary>
    bool Mark(ArchPlan p, ArchPiece piece, VoxelList lay, VoxelList dig)
    {
        switch (piece.Kind)
        {
            case ArchKind.Template:
                var t = piece.Template();
                if (t?.Voxels == null || DesignateBuild(t.Voxels) == 0) return false;
                Arch(t.Arched);
                if (t.Door is { } door) HouseDoors.Add((door, Time));
                Columns(piece.Index, t.Voxels);
                return true;
            case ArchKind.Gate:
                var g = piece.Gate();
                if (g == null) return false;
                AddGate(g);
                Columns(piece.Index, g.Cut); Columns(piece.Index, g.Leaves);
                return true;
            case ArchKind.Mend:
                if ((lay?.Count ?? 0) + (dig?.Count ?? 0) == 0) return false;
                Mend(lay ?? new VoxelList(), dig ?? new VoxelList());
                Columns(piece.Index, lay); Columns(piece.Index, dig);
                return true;
            case ArchKind.Hang:
            {
                var w = p.Works[piece.Index];
                var gate = Gates.Find(o => o.Broken && Brushes.Dist(o.Middle.x - w.X, o.Middle.z - w.Z) < 16);
                return gate != null && MendGate(gate) > 0;
            }
            case ArchKind.Dig:
                if (dig == null) return false;
                // a palisade taken down for stone is forgotten, its rotten logs too: nobody is to mend it after
                if (piece.Work == ArchWorkKind.Unbuild)
                    foreach (int key in WorkColumns(p.Works[piece.Index]))
                    {
                        int x = key % World.SX, z = key / World.SX, gr = GroundAt(x, z);
                        for (int y = Math.Max(1, gr - 2); y < gr + 40 && y < World.SY; y++)
                        {
                            if (MeantAt(x, y, z) == Mat.Wood) Forget(x, y, z);
                            // (and logs marked to be laid again in it let go: they would hang over the logs taken down)
                            if (Orders.At(x, y, z) == Mat.Wood) Orders.Clear(x, y, z);
                        }
                    }
                if (DesignateRemove(dig) == 0) return false;
                Columns(piece.Index, dig);
                return true;
            default:
                if (lay == null || DesignateBuild(lay) == 0) return false;
                Columns(piece.Index, lay);
                return true;
        }
    }

    /// <summary>Notes the columns of what was marked for a piece.</summary>
    void Columns(int index, VoxelList L)
    {
        if (L == null) return;
        if (!_archCols.TryGetValue(index, out var set)) _archCols[index] = set = new HashSet<int>();
        for (int i = 0; i < L.Count; i++) set.Add(L.Z(i) * World.SX + L.X(i));
    }

    /// <summary>How much is still marked to build or dig out in a piece: in the columns marked for it, or (read back from a save) in its box.</summary>
    int MarkedLeft(ArchPiece piece)
    {
        int n = 0;
        int Column(int x, int z)
        {
            if (!World.InBounds(x, 1, z)) return 0;
            int c = 0, g = GroundAt(x, z);
            for (int y = Math.Max(1, g - 14); y < Math.Min(World.SY, g + 56); y++) if (Orders.At(x, y, z) != 0) c++;
            return c;
        }
        if (_archCols.TryGetValue(piece.Index, out var cols))
        {
            foreach (int key in cols) n += Column(key % World.SX, key / World.SX);
            return n;
        }
        for (int z = piece.Z0; z <= piece.Z1; z++)
            for (int x = piece.X0; x <= piece.X1; x++)
            {
                // (the box's columns with any order at all, by the grid's count first)
                int ci = (z >> NavGrid.Shift) * Nav.NX + (x >> NavGrid.Shift);
                if (ci < 0 || ci >= Orders.ColCount.Length || Orders.ColCount[ci] == 0) continue;
                n += Column(x, z);
            }
        return n;
    }

    /// <summary>What is still marked of a piece, in the columns marked for it, let go.</summary>
    void LetGo(ArchPiece piece)
    {
        if (!_archCols.TryGetValue(piece.Index, out var cols)) return;
        foreach (int key in cols)
        {
            int x = key % World.SX, z = key / World.SX, g = GroundAt(x, z);
            for (int y = Math.Max(1, g - 14); y < Math.Min(World.SY, g + 56); y++) if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
        }
    }

    bool HordeNear(int x, int z, float r)
    {
        foreach (var o in Units)
            if (!o.Dead && !o.Inside && o.Team == Team.Horde && !o.Bound && !o.Envoy && (o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z) < r * r) return true;
        return false;
    }

    /// <summary>After a raid, what it broke of what he built and mended about the line is mended, at most every half minute.</summary>
    void ArchitectMend(Unit u, ArchPlan p)
    {
        if (Raids.Active || Time < _archMendAt) return;
        _archMendAt = Time + 30;
        // what was meant of his finished pieces only (not the old ruins the valley remembers about them)
        var meant = new List<int>();
        var seen = new HashSet<int>();
        foreach (var piece in ArchPieces(p))
        {
            if ((p.Done & 1UL << piece.Index) == 0 || piece.Kind is ArchKind.Dig or ArchKind.Gate or ArchKind.Hang) continue;
            foreach (int v in MeantIn(piece.X0, piece.Z0, piece.X1, piece.Z1)) if (seen.Add(v)) meant.Add(v);
        }
        if (meant.Count == 0) return;
        var lay = new VoxelList(); var clear = new VoxelList();
        MendPlan(meant, lay, clear);
        if (lay.Count + clear.Count < 6) return;
        Mend(lay, clear);
        ArchMended++;
        Say("Архитектор велел заделать то, что сломала орда.", 0, u);
    }

    /// <summary>Our engines in the valley he has not stood yet are stood inside the line by the horde's side, a few metres in, apart.</summary>
    void ArchitectEngines(ArchPlan p)
    {
        if (p.Ring.Count < 3) return;
        var (ex, ez) = Site.Hex != null ? Site.EntryOn(Site.EntrySide, .5) : (Site.EntryX, p.CZ);
        float hx = ex - p.CX, hz = ez - p.CZ, hl = MathF.Max(1, MathF.Sqrt(hx * hx + hz * hz));
        hx /= hl; hz /= hl;
        // where the line is that way
        float reach = p.H;
        for (int i = 0; i < p.Ring.Count; i++)
        {
            var (ax, az) = p.Ring[i]; var (bx, bz) = p.Ring[(i + 1) % p.Ring.Count];
            if (Crosses(p.CX, p.CZ, p.CX + hx * 4 * p.H, p.CZ + hz * 4 * p.H, ax, az, bx, bz, out float t)) reach = Math.Min(reach, t * 4 * p.H);
        }
        int k = 0;
        foreach (var e in Units)
        {
            if (e.Dead || e.Team != Team.Player || !Kinds.IsOurEngine(e.T)) continue;
            int slot = k++;
            if (!_archEngines.Add(e.Id)) continue;
            float along = (slot % 2 == 0 ? 1 : -1) * (slot + 1) / 2 * 32, back = Math.Max(16, reach - WallThick / 2 - 40);
            int at = NearestStand((int)(p.CX + hx * back - hz * along), (int)(p.CZ + hz * back + hx * along), e.T.Move.Height);
            if (at >= 0) Command(new[] { e }, at, stay: true);
        }
    }

    /// <summary>The idle workers set to bring in what the next piece is short of (none: back to what they did).</summary>
    void ArchDuty(int kind)
    {
        foreach (var w in Units)
        {
            if (w.Dead || w.T != Kinds.Worker || w.Team != Team.Player) continue;
            if (kind != Stockpile.None && w.Duty == Stockpile.None && (kind == Stockpile.Stone || kind == Stockpile.Wood)) { w.Duty = kind; _archDuty.Add(w.Id); }
            else if (_archDuty.Contains(w.Id) && w.Duty != kind) { w.Duty = Stockpile.None; _archDuty.Remove(w.Id); }
        }
    }

    /// <summary>The architect's way changed (his card): he plans again for the new way; what he finished stands and counts as built.</summary>
    public void SetArchStyle(ArchStyle s)
    {
        if (s == ArchStyle) return;
        ArchStyle = s;
        if (ArchPlan != null) { _archReplan = true; _archPlannedAt = double.NegativeInfinity; }
    }

    /// <summary>The main gate of the plan, and the way out of it (for the pictures), or null.</summary>
    public (int x, int z, int side)? ArchGate()
    {
        if (ArchPlan is not { } p) return null;
        foreach (var w in p.Works)
            if (w.Kind == ArchWorkKind.Gate) return (w.X, w.Z, w.Rot);
            else if (w.Kind == ArchWorkKind.Gatehouse) return (w.X, w.Z, w.Rot switch { 0 => 2, 1 => 1, 2 => 0, _ => 3 });
        return null;
    }

    /// <summary>Whether a column is inside the line of the plan.</summary>
    public bool InsideArchRing(float x, float z) => ArchPlan is { Ring.Count: >= 3 } p && FromRing(p, x, z).inside;

    // ------------------------------------------------------------ the save

    /// <summary>The way's byte carries this bit when the plan after it is written as works (since the architect surveys); without it the plan is the old square ring, planned again.</summary>
    const byte ArchWorksMark = 0x40;

    void WriteArchitect(BinaryWriter w)
    {
        w.Write((byte)((byte)ArchStyle | ArchWorksMark));
        w.Write(ArchPlan != null);
        if (ArchPlan is not { } p) return;
        w.Write(p.CX); w.Write(p.CZ); w.Write(p.H); w.Write(p.GateSide); w.Write(p.HordeSide);
        w.Write((byte)p.Style); w.Write(p.Stone); w.Write(p.Gatehouse); w.Write(p.Marked); w.Write(p.Done); w.Write(p.Said); w.Write(p.Found);
        w.Write(p.Ring.Count);
        for (int i = 0; i < p.Ring.Count; i++) { w.Write(p.Ring[i].X); w.Write(p.Ring[i].Z); w.Write(p.Edge[i]); }
        w.Write(p.Works.Count);
        foreach (var k in p.Works)
        {
            w.Write((byte)k.Kind); w.Write(k.Stage); w.Write(k.Name); w.Write(k.Stone); w.Write(k.H); w.Write(k.Thick);
            w.Write(k.X); w.Write(k.Z); w.Write(k.Rot); w.Write(k.Quarter); w.Write(k.After);
            w.Write(k.Line.Length); foreach (int v in k.Line) w.Write(v);
            w.Write(k.Cells.Length); foreach (int v in k.Cells) w.Write(v);
        }
    }

    void ReadArchitect(BinaryReader r)
    {
        byte way = r.ReadByte();
        ArchStyle = (ArchStyle)(way & 3);
        ArchPlan = null;
        if (!r.ReadBoolean()) return;
        if ((way & ArchWorksMark) == 0)
        {
            // a square ring of a game saved before he surveyed: read past, and planned again from what stands now
            r.ReadInt32(); r.ReadInt32(); r.ReadInt32(); r.ReadInt32(); r.ReadInt32(); r.ReadInt32(); r.ReadInt32(); r.ReadInt32();
            r.ReadByte(); r.ReadBoolean(); r.ReadBoolean(); r.ReadUInt64(); r.ReadUInt64(); r.ReadInt32();
            return;
        }
        var p = new ArchPlan
        {
            CX = r.ReadInt32(), CZ = r.ReadInt32(), H = r.ReadInt32(), GateSide = r.ReadInt32(), HordeSide = r.ReadInt32(),
            Style = (ArchStyle)r.ReadByte(), Stone = r.ReadBoolean(), Gatehouse = r.ReadBoolean(), Marked = r.ReadUInt64(), Done = r.ReadUInt64(), Said = r.ReadInt32(), Found = r.ReadString(),
        };
        for (int n = r.ReadInt32(); n > 0; n--) { p.Ring.Add((r.ReadInt32(), r.ReadInt32())); p.Edge.Add(r.ReadByte()); }
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var k = new ArchWork
            {
                Kind = (ArchWorkKind)r.ReadByte(), Stage = r.ReadInt32(), Name = r.ReadString(), Stone = r.ReadBoolean(), H = r.ReadInt32(), Thick = r.ReadInt32(),
                X = r.ReadInt32(), Z = r.ReadInt32(), Rot = r.ReadInt32(), Quarter = r.ReadInt32(), After = r.ReadUInt64(),
            };
            k.Line = new int[r.ReadInt32()]; for (int i = 0; i < k.Line.Length; i++) k.Line[i] = r.ReadInt32();
            k.Cells = new int[r.ReadInt32()]; for (int i = 0; i < k.Cells.Length; i++) k.Cells[i] = r.ReadInt32();
            p.Works.Add(k);
        }
        ArchPlan = p;
        _archPlannedAt = Time;
    }
}
