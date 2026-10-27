using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>How far the architect goes with a valley's defences: a palisade and a gate; walls with posts, a ditch and stakes; all of it high and in stone.</summary>
public enum ArchStyle : byte { Modest, Strong, Sky }

/// <summary>
/// What an architect has planned for the valley he is in: a square ring round the point it holds, so far out from it,
/// its gate on the side a road comes in by (or away from the horde), the side the horde comes from, how far he goes,
/// whether the ring is laid in stone or put up as a palisade first; and which of its pieces he has marked and which
/// the workers have finished. The pieces themselves are worked out again from this each time he looks.
/// </summary>
public sealed class ArchPlan
{
    public int CX, CZ;
    /// <summary>Half the side of the ring, voxels, from the point to the middle of a wall.</summary>
    public int H;
    /// <summary>The ring's sides: 0 north (−z), 1 east (+x), 2 south (+z), 3 west (−x).</summary>
    public int GateSide, HordeSide;
    /// <summary>The gate along its side from the side's middle, voxels (clockwise round the ring); a second gate where another road comes in, or −1.</summary>
    public int GateAt, ExtraSide = -1, ExtraAt;
    public ArchStyle Style;
    /// <summary>The ring in stone (else a palisade first); its gate a gatehouse with iron leaves (else a gate of planks cut through the wall).</summary>
    public bool Stone, Gatehouse;
    /// <summary>Pieces marked, and pieces finished (by their index, see <see cref="Sim.ArchPieces"/>).</summary>
    public ulong Marked, Done;
    /// <summary>The last stage the chronicle was told of.</summary>
    public int Said = -1;
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
    public const string ArchitectOne = "в долине уже есть архитектор: он один на долину";

    readonly HashSet<int> _archDuty = new();
    readonly HashSet<int> _archEngines = new();
    readonly Dictionary<int, (int left, double since)> _archLeft = new();
    double _archMendAt;

    // the pieces' indices in the plan's bit masks
    const int PSide = 0, PSideRight = 4, PGate = 5, PExtraGate = 6, PTower = 7, PDitch = 11, PStakes = 15, PUnbuild = 18, PStoneSide = 22, PStair = 26;

    /// <summary>The ring's sides: the way out of each, and the way along it (clockwise).</summary>
    static readonly (int ox, int oz, int tx, int tz)[] RingSides = { (0, -1, 1, 0), (1, 0, 0, 1), (0, 1, -1, 0), (-1, 0, 0, -1) };

    internal enum ArchKind : byte { Lay, Dig, Template, Gate }

    /// <summary>One piece of the plan: what it is, its stage, the box it is built in, where the architect stands to mark it, and what it lays.</summary>
    public sealed class ArchPiece
    {
        public int Index, Stage;
        public string Name = "";
        internal ArchKind Kind;
        public int X0, Z0, X1, Z1, X, Z;
        /// <summary>Its voxels as they would be marked now (the gate's from its plan), worked out when asked.</summary>
        internal Func<VoxelList> Lay;
        internal Func<TemplatePlan> Template;
        internal Func<GatePlan> Gate;
        /// <summary>Waits for these pieces to be finished first.</summary>
        public ulong After;
    }

    static readonly string[] ArchStages = { "ставит кольцо вокруг", "ставит ворота к дороге", "ставит башни по углам", "копает ров", "ставит колья перед стеной", "меняет частокол на камень" };

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

    // ------------------------------------------------------------ the plan

    /// <summary>
    /// The architect looks the ground over and plans: the ring as small as clears the point by three metres (bigger for
    /// a stronger way and a bigger garrison), pushed out a cell or two where that keeps it off what is built and off the
    /// water; the horde's side from where its bands come in; the gate where a road crosses the ring (another gate where a
    /// second road does), else on the side away from the horde; stone if the stores have well over half of what the
    /// ring takes and the way is not the modest one, else a palisade first.
    /// </summary>
    public ArchPlan MakeArchPlan()
    {
        var (cx, cz, r) = PointToHold();
        var style = ArchStyle;
        int garrison = Units.FindAll(u => !u.Dead && u.Team == Team.Player && u.T.Work < 1 && !Kinds.IsEngine(u.T)).Count;
        int h0 = r + 24 + (style == ArchStyle.Sky ? 16 : style == ArchStyle.Strong ? 8 : 0) + Math.Clamp((garrison - 6) * 2, 0, 32);
        h0 = (h0 + 7) & ~7;
        int bestH = h0;
        double best = double.MaxValue;
        for (int h = h0; h <= h0 + 48; h += 8)
        {
            double cost = RingCost(cx, cz, h) + (h - h0) * .5;
            if (cost < best) { best = cost; bestH = h; }
        }
        var p = new ArchPlan { CX = cx, CZ = cz, H = bestH, Style = style };
        // the horde's side: the ring's side facing where its bands come into the valley
        var (ex, ez) = Site.Hex != null ? Site.EntryOn(Site.EntrySide, .5) : (Site.EntryX, cz);
        p.HordeSide = SideToward(ex - cx, ez - cz);
        // the gates: where the roads cross the ring
        var crossings = RoadCrossings(p);
        crossings.Sort((a, b) => (a.side == p.HordeSide).CompareTo(b.side == p.HordeSide));
        if (crossings.Count > 0) { p.GateSide = crossings[0].side; p.GateAt = crossings[0].at; }
        else { p.GateSide = (p.HordeSide + 2) % 4; p.GateAt = 0; }
        foreach (var c in crossings)
            if (c.side != p.GateSide) { p.ExtraSide = c.side; p.ExtraAt = c.at; break; }
        // stone, if the stores come to well over half of the ring in it
        double need = 4.0 * 2 * bestH * WallThick * WallHeight(style);
        p.Stone = style != ArchStyle.Modest && Stock[Stockpile.Stone] >= need * .6;
        // a gatehouse with its iron lintel and leaves, if there is the metal for them
        // (a ring too small for one between its corners has a gate of planks)
        p.Gatehouse = p.Stone && HasMetal(.6f) && bestH >= 44 + 16 + 24;
        int room = Math.Max(0, p.Gatehouse ? bestH - 44 - 16 : bestH - 24);
        p.GateAt = Math.Clamp(p.GateAt, -room, room);
        p.ExtraAt = Math.Clamp(p.ExtraAt, -Math.Max(0, bestH - 24), Math.Max(0, bestH - 24));
        return p;
    }

    /// <summary>The side of the ring facing a way from its middle.</summary>
    static int SideToward(float dx, float dz)
    {
        int s = 0;
        float best = float.MinValue;
        for (int i = 0; i < 4; i++) { float d = RingSides[i].ox * dx + RingSides[i].oz * dz; if (d > best) { best = d; s = i; } }
        return s;
    }

    /// <summary>What a ring so far out would cost: what is built on its line (a house cut through), water, ground off the valley, steep ground.</summary>
    double RingCost(int cx, int cz, int h)
    {
        double cost = 0;
        for (int s = 0; s < 4; s++)
        {
            var (ox, oz, tx, tz) = RingSides[s];
            for (int t = -h; t <= h; t += 4)
            {
                int x = cx + ox * h + tx * t, z = cz + oz * h + tz * t;
                if (!World.OnLand(x, z) || !Site.Inland(x, z, 12) || Site.Forbidden(x, z)) { cost += 60; continue; }
                int g = GroundAt(x, z);
                if (Site.WaterTop != null && Site.WaterTop[z * World.SX + x] > 0) cost += 6;
                for (int y = g; y < g + 20; y += 4) if (Mat.IsPlayer[World.Get(x, y, z)]) { cost += 10; break; }
                if (Math.Abs(GroundAt(x + tx * 4, z + tz * 4) - g) > 5) cost += 2;
            }
        }
        return cost;
    }

    /// <summary>Where the valley's roads cross the ring: the side and how far along it from its middle.</summary>
    List<(int side, int at)> RoadCrossings(ArchPlan p)
    {
        var found = new List<(int side, int at)>();
        for (int s = 0; s < 4; s++)
        {
            var (ox, oz, tx, tz) = RingSides[s];
            float ax = p.CX + ox * p.H - tx * p.H, az = p.CZ + oz * p.H - tz * p.H, bx = p.CX + ox * p.H + tx * p.H, bz = p.CZ + oz * p.H + tz * p.H;
            foreach (var road in Site.Roads)
                for (int i = 0; i + 1 < road.Count; i++)
                    if (Crosses(ax, az, bx, bz, road[i].X, road[i].Z, road[i + 1].X, road[i + 1].Z, out float u))
                    {
                        int at = (int)Math.Round(u * 2 * p.H - p.H);
                        if (!found.Exists(f => f.side == s)) found.Add((s, at));
                    }
        }
        return found;
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

    // ------------------------------------------------------------ the pieces

    /// <summary>A point on the ring's side, so far along it from its middle and so far out of it (inward when negative).</summary>
    static (int x, int z) OnSide(ArchPlan p, int s, int along, int outward = 0)
    {
        var (ox, oz, tx, tz) = RingSides[s];
        return (p.CX + ox * (p.H + outward) + tx * along, p.CZ + oz * (p.H + outward) + tz * along);
    }

    /// <summary>
    /// The plan's pieces in the order they are marked: the ring (the horde's side first, then the sides beside it, the
    /// gate's last), the gate, the towers or posts at the corners, the ditch outside, stakes before the horde's side,
    /// the palisade put in stone when the stores come to it, a stair up onto a stone wall.
    /// </summary>
    public List<ArchPiece> ArchPieces(ArchPlan p)
    {
        var list = new List<ArchPiece>();
        int h = WallHeight(p.Style);
        int[] order = { p.HordeSide, (p.HordeSide + 1) % 4, (p.HordeSide + 3) % 4, (p.HordeSide + 2) % 4 };
        if (order[3] != p.GateSide) { int gi = Array.IndexOf(order, p.GateSide); (order[gi], order[3]) = (order[3], order[gi]); }
        ulong ring = 0;
        foreach (int s in order)
        {
            bool gateHere = s == p.GateSide && p.Gatehouse;
            if (gateHere)
            {
                // two stretches either side of the gatehouse
                list.Add(SidePiece(p, s, PSide + s, -p.H - 4, p.GateAt - 44, h, "стена у ворот"));
                list.Add(SidePiece(p, s, PSideRight, p.GateAt + 44, p.H + 4, h, "стена у ворот"));
                ring |= 1UL << (PSide + s) | 1UL << PSideRight;
            }
            else
            {
                list.Add(SidePiece(p, s, PSide + s, -p.H - 4, p.H + 4, h, p.Stone ? "стена" : "частокол"));
                ring |= 1UL << (PSide + s);
            }
        }
        // the gate: a gatehouse in a stone ring with the metal for it, else a gate of planks cut through the wall once that side stands
        list.Add(GatePiece(p, PGate, p.GateSide, p.GateAt, p.Gatehouse, ring));
        if (p.ExtraSide >= 0) list.Add(GatePiece(p, PExtraGate, p.ExtraSide, p.ExtraAt, false, ring));
        // the corners: stone towers on the highest way with stone, wooden posts inside the corners otherwise
        int towers = p.Style == ArchStyle.Modest ? 2 : 4;
        for (int c = 0; c < 4; c++)
        {
            // the corner at the end of side c (clockwise), the modest way keeping only the two on the horde's side
            int s = c, s2 = (c + 1) % 4;
            if (towers == 2 && s != p.HordeSide && s2 != p.HordeSide) continue;
            list.Add(CornerPiece(p, c, h, ring));
        }
        // the ditch outside, a causeway left at each gate and where a road crosses it
        if (p.Style != ArchStyle.Modest)
            foreach (int s in order) list.Add(DitchPiece(p, s, ring));
        // stakes before the horde's side (and the sides beside it, on the highest way)
        if (p.Style != ArchStyle.Modest)
        {
            list.Add(StakesPiece(p, p.HordeSide, PStakes, ring));
            if (p.Style == ArchStyle.Sky)
            {
                list.Add(StakesPiece(p, (p.HordeSide + 1) % 4, PStakes + 1, ring));
                list.Add(StakesPiece(p, (p.HordeSide + 3) % 4, PStakes + 2, ring));
            }
        }
        // the palisade put in stone, side by side, the gate's side last and not at all (its gate hangs in it)
        if (!p.Stone && p.Style != ArchStyle.Modest)
            foreach (int s in order)
            {
                if (s == p.GateSide || s == p.ExtraSide) continue;
                list.Add(UnbuildPiece(p, s));
                list.Add(SidePiece(p, s, PStoneSide + s, -p.H - 4, p.H + 4, h, "стена вместо частокола", stone: true, after: 1UL << (PUnbuild + s)));
            }
        // a stair up onto a stone wall from inside, along the horde's side from its middle
        if (p.Stone && p.HordeSide != p.GateSide) list.Add(StairPiece(p, h));
        return list;
    }

    ArchPiece SidePiece(ArchPlan p, int s, int index, int from, int to, int h, string name, bool stone = false, ulong after = 0)
    {
        var (ax, az) = OnSide(p, s, from);
        var (bx, bz) = OnSide(p, s, to);
        bool inStone = p.Stone || stone;
        var piece = new ArchPiece
        {
            Index = index, Stage = stone ? 5 : 0, Name = name, Kind = ArchKind.Lay, After = after,
            X0 = Math.Min(ax, bx) - 8, Z0 = Math.Min(az, bz) - 8, X1 = Math.Max(ax, bx) + 8, Z1 = Math.Max(az, bz) + 8,
        };
        (piece.X, piece.Z) = OnSide(p, s, (from + to) / 2, -16);
        piece.Lay = () => inStone ? StoneStretch(p, s, ax, az, bx, bz, h) : TimberStretch(p, s, ax, az, bx, bz, h);
        return piece;
    }

    /// <summary>A stretch of the ring in stone: a metre thick, so high over the ground each column stands on, merlons on its outer edge.</summary>
    VoxelList StoneStretch(ArchPlan p, int s, int ax, int az, int bx, int bz, int h)
    {
        var L = new VoxelList();
        var (ox, oz, tx, tz) = RingSides[s];
        int x0 = Math.Min(ax, bx) - WallThick, x1 = Math.Max(ax, bx) + WallThick, z0 = Math.Min(az, bz) - WallThick, z1 = Math.Max(az, bz) + WallThick;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (!World.OnLand(x, z) || Site.Forbidden(x, z)) continue;
                // across the wall (outward positive) and along it
                int across = (x - ax) * ox + (z - az) * oz, along = (x - ax) * tx + (z - az) * tz, len = (bx - ax) * tx + (bz - az) * tz;
                if (along < 0 || along > len || across < -WallThick / 2 || across >= WallThick / 2) continue;
                int g = GroundAt(x, z), top = g + h;
                Column(World, L, x, z, g, top, Mat.Stone);
                // (none over a gate cut through it: they would hang over the gateway)
                if (across >= WallThick / 2 - 3 && Merlon(along) && !OverGate(p, s, x, z)) Column(World, L, x, z, top, top + 5, Mat.Stone);
            }
        return L;
    }

    /// <summary>Whether a column of a side is over one of the plan's gates of planks (or beside it within half a metre).</summary>
    static bool OverGate(ArchPlan p, int s, int x, int z)
    {
        foreach (var (side, at) in new[] { (p.Gatehouse ? -1 : p.GateSide, p.GateAt), (p.ExtraSide, p.ExtraAt) })
        {
            if (side != s) continue;
            var (gx, gz) = OnSide(p, s, at);
            int along = Math.Abs((x - gx) * RingSides[s].tx + (z - gz) * RingSides[s].tz);
            if (along < Gateway.Half + 6) return true;
        }
        return false;
    }

    /// <summary>A stretch of palisade: logs side by side three wide, each on its own ground, their tops sharpened by turns.</summary>
    VoxelList TimberStretch(ArchPlan p, int s, int ax, int az, int bx, int bz, int h)
    {
        var L = new VoxelList();
        var (ox, oz, tx, tz) = RingSides[s];
        int x0 = Math.Min(ax, bx) - 2, x1 = Math.Max(ax, bx) + 2, z0 = Math.Min(az, bz) - 2, z1 = Math.Max(az, bz) + 2;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (!World.OnLand(x, z) || Site.Forbidden(x, z)) continue;
                int across = (x - ax) * ox + (z - az) * oz, along = (x - ax) * tx + (z - az) * tz, len = (bx - ax) * tx + (bz - az) * tz;
                if (along < 0 || along > len || across < -1 || across > 1) continue;
                int g = GroundAt(x, z);
                Column(World, L, x, z, g, g + h - 4 + ((along / 3) % 2 == 0 ? 3 : 0), Mat.Wood);
            }
        return L;
    }

    ArchPiece GatePiece(ArchPlan p, int index, int s, int at, bool stone, ulong ring)
    {
        var (gx, gz) = OnSide(p, s, at);
        var (ox, oz, _, _) = RingSides[s];
        var piece = new ArchPiece
        {
            Index = index, Stage = 1, Name = stone ? "надвратная башня" : "ворота",
            X0 = gx - 48, Z0 = gz - 48, X1 = gx + 48, Z1 = gz + 48,
            // a gate of planks is cut once the palisade on its side stands
            After = stone ? 0 : 1UL << (PSide + s),
        };
        (piece.X, piece.Z) = OnSide(p, s, at, -20);
        if (stone)
        {
            int rot = s switch { 0 => 2, 1 => 1, 2 => 0, _ => 3 };
            piece.Kind = ArchKind.Template;
            piece.Template = () => Gatehouse(World, Site, gx, gz, rot);
        }
        else
        {
            piece.Kind = ArchKind.Gate;
            piece.Gate = () =>
            {
                // the palisade's outer face at the gate, a metre over the ground
                int y = GroundAt(gx, gz) + 8, x = gx + ox * 6, z = gz + oz * 6;
                for (int k = 0; k < 10 && !Blocking(World.Get(x - ox, y, z - oz)); k++) { x -= ox; z -= oz; }
                var hit = new VoxelHit { X = x - ox, Y = y, Z = z - oz, NX = ox, NZ = oz, M = World.Get(x - ox, y, z - oz) };
                return GatePlan.At(World, Site, hit, out _);
            };
        }
        return piece;
    }

    static bool Blocking(byte m) => m != 0 && !Mat.Passable[m];

    ArchPiece CornerPiece(ArchPlan p, int c, int h, ulong ring)
    {
        // the corner at the end of side c going clockwise
        var (ox, oz, tx, tz) = RingSides[c];
        int x = p.CX + ox * p.H + tx * p.H, z = p.CZ + oz * p.H + tz * p.H;
        int inx = Math.Sign(p.CX - x), inz = Math.Sign(p.CZ - z);
        bool stone = p.Stone && p.Style == ArchStyle.Sky;
        var piece = new ArchPiece
        {
            Index = PTower + c, Stage = 2, Name = stone ? "башня на углу" : "вышка в углу",
            X0 = x - 32, Z0 = z - 32, X1 = x + 32, Z1 = z + 32, X = x + inx * 32, Z = z + inz * 32, Kind = ArchKind.Template,
            // the corner's two sides stand first
            After = 1UL << (PSide + c) | 1UL << (PSide + (c + 1) % 4),
        };
        if (stone) piece.Template = () => RoundTower(World, Site, x, z, 0, h + 8);
        else
        {
            // a wooden post inside the corner, its ladder toward the middle
            int px = x + inx * 22, pz = z + inz * 22;
            int rot = Math.Abs(p.CX - px) > Math.Abs(p.CZ - pz) ? (p.CX > px ? 1 : 3) : (p.CZ > pz ? 0 : 2);
            piece.Template = () => Watchtower(World, Site, px, pz, rot);
        }
        return piece;
    }

    /// <summary>Whether a column is by a gate of the plan or on a road: left alone by the ditch and the stakes.</summary>
    bool ByWayIn(ArchPlan p, int x, int z, int clear)
    {
        var (gx, gz) = OnSide(p, p.GateSide, p.GateAt);
        int gr = p.Gatehouse ? 48 : clear;
        if (Math.Abs(x - gx) * Math.Abs(RingSides[p.GateSide].tx) + Math.Abs(z - gz) * Math.Abs(RingSides[p.GateSide].tz) < gr) return true;
        if (p.ExtraSide >= 0)
        {
            var (ex, ez) = OnSide(p, p.ExtraSide, p.ExtraAt);
            if (Math.Abs(x - ex) * Math.Abs(RingSides[p.ExtraSide].tx) + Math.Abs(z - ez) * Math.Abs(RingSides[p.ExtraSide].tz) < clear) return true;
        }
        return Site.RoadDist(x, z) < 10;
    }

    ArchPiece DitchPiece(ArchPlan p, int s, ulong ring)
    {
        const int Off = WallThick / 2 + 24, Width = 12, Depth = 10;
        var (ox, oz, tx, tz) = RingSides[s];
        var (ax, az) = OnSide(p, s, -p.H - Off - Width, Off);
        var (bx, bz) = OnSide(p, s, p.H + Off + Width, Off + Width);
        var piece = new ArchPiece
        {
            Index = PDitch + s, Stage = 3, Name = "ров", Kind = ArchKind.Dig,
            X0 = Math.Min(ax, bx), Z0 = Math.Min(az, bz), X1 = Math.Max(ax, bx), Z1 = Math.Max(az, bz), After = ring,
        };
        (piece.X, piece.Z) = OnSide(p, s, 0, -16);
        piece.Lay = () =>
        {
            var L = new VoxelList();
            for (int z = piece.Z0; z <= piece.Z1; z++)
                for (int x = piece.X0; x <= piece.X1; x++)
                {
                    if (!World.OnLand(x, z) || Site.Forbidden(x, z) || ByWayIn(p, x, z, 20)) continue;
                    int g = World.SurfAt(x, z), bed = GroundAt(x, z) - Depth;
                    for (int y = g - 1; y >= Math.Max(3, bed); y--)
                        if (Labour.Diggable(World.Get(x, y, z)) && !Mat.IsPlayer[World.Get(x, y, z)]) L.Add(x, y, z, Mat.Air);
                    if (bed < g) Brushes.ClearVegAbove(World, L, x, z, g);
                }
            return L;
        };
        return piece;
    }

    ArchPiece StakesPiece(ArchPlan p, int s, int index, ulong ring)
    {
        const int Near = WallThick / 2 + 24 + 12 + 8, Far = Near + 16;
        var (ax, az) = OnSide(p, s, -p.H - Far, Near);
        var (bx, bz) = OnSide(p, s, p.H + Far, Far);
        var piece = new ArchPiece
        {
            Index = index, Stage = 4, Name = "колья", Kind = ArchKind.Lay,
            X0 = Math.Min(ax, bx), Z0 = Math.Min(az, bz), X1 = Math.Max(ax, bx), Z1 = Math.Max(az, bz), After = ring,
        };
        (piece.X, piece.Z) = OnSide(p, s, 0, -16);
        piece.Lay = () =>
        {
            var L = new VoxelList();
            for (int z = piece.Z0; z <= piece.Z1; z++)
                for (int x = piece.X0; x <= piece.X1; x++)
                {
                    // stakes a hand apart, each a little taller or shorter than the next, none on a way in
                    if (x % 3 != 0 || z % 3 != 0 || !World.OnLand(x, z) || Site.Forbidden(x, z) || ByWayIn(p, x, z, 28)) continue;
                    int g = World.SurfAt(x, z);
                    if (g < GroundAt(x, z) - 2) continue;
                    Column(World, L, x, z, g, g + 5 + (int)(Hash.H3(x, 7, z) * 3) - 1, Mat.Stakes);
                }
            return L;
        };
        return piece;
    }

    ArchPiece UnbuildPiece(ArchPlan p, int s)
    {
        var (ax, az) = OnSide(p, s, -p.H - 4);
        var (bx, bz) = OnSide(p, s, p.H + 4);
        var piece = new ArchPiece
        {
            Index = PUnbuild + s, Stage = 5, Name = "частокол долой", Kind = ArchKind.Dig,
            X0 = Math.Min(ax, bx) - 2, Z0 = Math.Min(az, bz) - 2, X1 = Math.Max(ax, bx) + 2, Z1 = Math.Max(az, bz) + 2,
            After = 1UL << (PSide + s),
        };
        (piece.X, piece.Z) = OnSide(p, s, 0, -16);
        piece.Lay = () =>
        {
            var L = new VoxelList();
            for (int z = piece.Z0; z <= piece.Z1; z++)
                for (int x = piece.X0; x <= piece.X1; x++)
                {
                    int g = GroundAt(x, z);
                    for (int y = g; y < g + 32 && y < World.SY; y++) if (World.Get(x, y, z) == Mat.Wood) L.Add(x, y, z, Mat.Air);
                }
            return L;
        };
        return piece;
    }

    ArchPiece StairPiece(ArchPlan p, int h)
    {
        // down along the wall's inner face from its walk at the middle of the horde's side
        var (sx, sz) = OnSide(p, p.HordeSide, 0, -WallThick / 2 - 2);
        var (ex, ez) = OnSide(p, p.HordeSide, 64, -WallThick / 2 - 2);
        var piece = new ArchPiece
        {
            Index = PStair, Stage = 2, Name = "лестница на стену", Kind = ArchKind.Lay,
            X0 = Math.Min(sx, ex) - 8, Z0 = Math.Min(sz, ez) - 8, X1 = Math.Max(sx, ex) + 8, Z1 = Math.Max(sz, ez) + 8, X = ex, Z = ez,
            After = 1UL << (PSide + p.HordeSide),
        };
        piece.Lay = () => Brushes.Stairs(World, Site, sx, sz, GroundAt(sx, sz) + h, ex, ez, Mat.Stone);
        return piece;
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

    /// <summary>
    /// Every few seconds: with the horde about, he waits; after a raid he has what it broke mended; else he plans if he
    /// has not, and marks the next pieces — no more than two being built at once, each when the stores come to half of
    /// it at least — setting the idle workers to bring in what is short; and he stands our engines facing the horde.
    /// </summary>
    void ArchitectThink(Unit u)
    {
        if (u == Possessed || Time < u.NextOrders) return;
        u.NextOrders = Time + 4 + u.Id % 5 * .1;
        if (!u.Commands) { ArchDoing = "ждёт ваших приказов"; ArchLacks = ""; u.Why = "архитектор: ждёт ваших приказов"; ArchDuty(Stockpile.None); return; }
        ArchPlan ??= MakeArchPlan();
        var p = ArchPlan;
        p.Style = ArchStyle;
        // the horde near the ring: nothing new is marked till it is gone
        if (HordeNear(p.CX, p.CZ, p.H + 60 * VoxelWorld.PerMetre))
        {
            ArchDoing = "орда рядом: ждёт"; u.Why = "архитектор: орда рядом, ждёт";
            return;
        }
        ArchitectMend(u, p);
        ArchitectEngines(p);
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
                // marked: finished when nothing is left marked in its box
                int left = MarkedLeft(piece);
                if (left == 0) { p.Done |= bit; continue; }
                // (one that has not moved for four minutes — waiting on what cannot be laid — holds up nothing)
                if (!_archLeft.TryGetValue(piece.Index, out var was) || was.left != left) _archLeft[piece.Index] = (left, Time);
                else if (Time - was.since > 240) continue;
                building++;
                if (doing == "") doing = piece.Name;
                continue;
            }
            if ((p.Done & piece.After) != piece.After) continue;
            if (next != null || building >= 2) continue;
            // what it takes, against the stores
            var (lay, dig) = Voxels(piece);
            if (lay == null && dig == null) { p.Done |= bit; continue; }
            Labour.Estimate(World, lay, false, est);
            int need = Stockpile.None;
            for (int k = 1; k < Stockpile.Kinds; k++)
                if (est[k] > 0 && Stock[k] < est[k] * .5) { need = k; break; }
            if (need != Stockpile.None)
            {
                // the stone the ring would take not there yet: the ring goes up as a palisade first
                if (need == Stockpile.Stone && p.Stone && piece.Stage == 0 && (p.Marked & ((1UL << 5) - 1)) == 0) { p.Stone = false; next = null; return; }
                if (lacks == "") { lacks = $"{Stockpile.Name(need)} {Fmt.M3(est[need] - Stock[need])} м³ на «{piece.Name}»"; lackKind = need; }
                continue;
            }
            next = piece;
            if (!Mark(piece, lay, dig)) { p.Done |= bit; next = null; continue; }
            p.Marked |= bit;
            ArchMarked++;
            building++;
            doing = piece.Name;
            if (p.Said < piece.Stage)
            {
                p.Said = piece.Stage;
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
        if (all && p.Said < 99) { p.Said = 99; Say("Архитектор: укрепления готовы. Дальше он только чинит то, что сломает орда.", 0, p.CX, p.CZ); }
        ArchDuty(lackKind);
    }

    static ulong PlanMask(List<ArchPiece> pieces)
    {
        ulong m = 0;
        foreach (var p in pieces) m |= 1UL << p.Index;
        return m;
    }

    /// <summary>A piece's voxels as they would be marked now: what to lay, or what to dig out.</summary>
    (VoxelList lay, VoxelList dig) Voxels(ArchPiece piece) => piece.Kind switch
    {
        ArchKind.Lay => (Some(piece.Lay()), null),
        ArchKind.Dig => (null, Some(piece.Lay())),
        ArchKind.Template => (Some(piece.Template()?.Voxels), null),
        _ => (piece.Gate() is { } g ? Some(g.Leaves) : null, null),
    };

    static VoxelList Some(VoxelList L) => L != null && L.Count > 0 ? L : null;

    /// <summary>Marks a piece for the workers; false if there is nothing of it to mark.</summary>
    bool Mark(ArchPiece piece, VoxelList lay, VoxelList dig)
    {
        switch (piece.Kind)
        {
            case ArchKind.Template:
                var t = piece.Template();
                if (t?.Voxels == null || DesignateBuild(t.Voxels) == 0) return false;
                Arch(t.Arched);
                if (t.Door is { } door) HouseDoors.Add((door, Time));
                return true;
            case ArchKind.Gate:
                var g = piece.Gate();
                if (g == null) return false;
                AddGate(g);
                return true;
            case ArchKind.Dig:
                return dig != null && DesignateRemove(dig) > 0;
            default:
                return lay != null && DesignateBuild(lay) > 0;
        }
    }

    /// <summary>How much is still marked to build or dig out in a piece's box.</summary>
    int MarkedLeft(ArchPiece piece)
    {
        int n = 0;
        for (int z = piece.Z0; z <= piece.Z1; z++)
            for (int x = piece.X0; x <= piece.X1; x++)
            {
                if (!World.InBounds(x, 1, z)) continue;
                int g = GroundAt(x, z);
                for (int y = Math.Max(1, g - 14); y < Math.Min(World.SY, g + 52); y++)
                    if (Orders.At(x, y, z) != 0) n++;
            }
        return n;
    }

    bool HordeNear(int x, int z, float r)
    {
        foreach (var o in Units)
            if (!o.Dead && !o.Inside && o.Team == Team.Horde && !o.Bound && !o.Envoy && (o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z) < r * r) return true;
        return false;
    }

    /// <summary>After a raid, what it broke of what we built about the ring is mended, at most every half minute.</summary>
    void ArchitectMend(Unit u, ArchPlan p)
    {
        if (Raids.Active || Time < _archMendAt) return;
        _archMendAt = Time + 30;
        // what was meant of his finished pieces only (not the old ruins the valley remembers about them)
        var meant = new List<int>();
        var seen = new HashSet<int>();
        foreach (var piece in ArchPieces(p))
        {
            if ((p.Done & 1UL << piece.Index) == 0 || piece.Kind == ArchKind.Dig) continue;
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

    /// <summary>Our engines in the valley he has not stood yet are stood inside the ring by the horde's side, a few metres in, apart.</summary>
    void ArchitectEngines(ArchPlan p)
    {
        int k = 0;
        foreach (var e in Units)
        {
            if (e.Dead || e.Team != Team.Player || !Kinds.IsOurEngine(e.T)) continue;
            int slot = k++;
            if (!_archEngines.Add(e.Id)) continue;
            int along = (slot % 2 == 0 ? 1 : -1) * (slot + 1) / 2 * 32;
            var (x, z) = OnSide(p, p.HordeSide, Math.Clamp(along, -p.H + 24, p.H - 24), -WallThick / 2 - 40);
            int at = NearestStand(x, z, e.T.Move.Height);
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

    /// <summary>The architect switched on or off (his card), or his way changed: what is not marked yet is planned for the new way.</summary>
    public void SetArchStyle(ArchStyle s) { ArchStyle = s; if (ArchPlan != null) ArchPlan.Style = s; }

    // ------------------------------------------------------------ the save

    void WriteArchitect(BinaryWriter w)
    {
        w.Write((byte)ArchStyle);
        w.Write(ArchPlan != null);
        if (ArchPlan is not { } p) return;
        w.Write(p.CX); w.Write(p.CZ); w.Write(p.H); w.Write(p.GateSide); w.Write(p.HordeSide); w.Write(p.GateAt);
        w.Write(p.ExtraSide); w.Write(p.ExtraAt); w.Write((byte)p.Style); w.Write(p.Stone); w.Write(p.Gatehouse); w.Write(p.Marked); w.Write(p.Done); w.Write(p.Said);
    }

    void ReadArchitect(BinaryReader r)
    {
        ArchStyle = (ArchStyle)r.ReadByte();
        if (!r.ReadBoolean()) { ArchPlan = null; return; }
        ArchPlan = new ArchPlan
        {
            CX = r.ReadInt32(), CZ = r.ReadInt32(), H = r.ReadInt32(), GateSide = r.ReadInt32(), HordeSide = r.ReadInt32(), GateAt = r.ReadInt32(),
            ExtraSide = r.ReadInt32(), ExtraAt = r.ReadInt32(), Style = (ArchStyle)r.ReadByte(), Stone = r.ReadBoolean(), Gatehouse = r.ReadBoolean(), Marked = r.ReadUInt64(),
            Done = r.ReadUInt64(), Said = r.ReadInt32(),
        };
    }
}
