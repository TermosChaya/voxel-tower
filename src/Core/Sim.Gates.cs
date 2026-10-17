using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>How a gate behaves: opens for our people and shuts before the horde, stands open, or is barred.</summary>
public enum GateMode : byte { Auto, Open, Shut }

/// <summary>
/// A gate in a wall: two oak leaves bound with iron, hung on the inner face of the wall at the jambs and swinging inward
/// till they stand square to it. The leaves are voxels like everything else and are rewritten as they turn, so a shut
/// gate blocks the way, the horde hacks it like masonry, and what it hacks out is gone. Our people knock and it opens for
/// them, unless the horde is right at it; it shuts behind them.
/// </summary>
public sealed class Gateway
{
    /// <summary>The opening: two cells wide, three high; the leaves are three voxels (37 cm) of oak.</summary>
    public const int Width = 16, Height = 24, Thick = 3, Half = Width / 2;
    public const int Voxels = Width * Height * Thick;
    /// <summary>Steps a leaf turns through from shut to open; the voxels are rewritten at each.</summary>
    const int Steps = 12;

    public int Id;
    /// <summary>The wall runs along x (the gate looks along z), else along z.</summary>
    public bool AlongX;
    /// <summary>Which way is in across the wall, toward the citadel: +1 or −1.</summary>
    public int In;
    /// <summary>The left jamb along the wall, the plane of the inner face across it (where the hinges are), and the threshold.</summary>
    public int U0, P, Y;
    public GateMode Mode;
    /// <summary>How far the leaves stand open now (0 shut, 1 square to the wall), and where they are heading.</summary>
    public float Open, Want;
    /// <summary>All its leaves laid: it works.</summary>
    public bool Ready;
    /// <summary>Hacked through: it stands as it is until it is mended.</summary>
    public bool Broken;
    internal double Knocked = -99, NextLook;
    internal bool HordeClose;
    /// <summary>Leaf voxels that could not be laid when it was hung; what is gone beyond that was hacked out.</summary>
    internal int Missing;
    /// <summary>Voxels hacked out of the leaves past which the gate is holed through: about a cell's worth.</summary>
    public const int Breach = 64;
    internal int Step = -1;
    /// <summary>Where each leaf voxel stands now (packed), or −1; and which were hacked out.</summary>
    internal readonly int[] At = new int[Voxels];
    internal readonly bool[] Gone = new bool[Voxels];
    /// <summary>The cells the leaves fill shut or sweep through as they turn: our people knock and wait at these.</summary>
    public readonly HashSet<int> Cells = new();

    /// <summary>The middle of the gateway at the threshold, in voxels.</summary>
    public (float x, float y, float z) Middle => AlongX ? (U0 + Half, Y, P) : (P, Y, U0 + Half);

    /// <summary>Leaf voxels the horde (or anyone) has taken out since it was hung.</summary>
    public int Hacked
    {
        get { int n = 0; foreach (bool g in Gone) if (g) n++; return n - Missing; }
    }

    /// <summary>Where leaf voxel i stands with the leaves turned by step s.</summary>
    internal (int x, int y, int z) Place(int i, int s)
    {
        int dt = i % Thick, du = i / Thick % Half, rest = i / (Thick * Half), dy = rest % Height, leaf = rest / Height;
        double th = s / (double)Steps * Math.PI / 2, c = Math.Cos(th), sn = Math.Sin(th);
        double a = du + .5, b = dt + .5;
        double u = a * c - b * sn, t = a * sn + b * c;
        double along = leaf == 0 ? U0 + u : U0 + Width - u, across = P + t * In;
        int ua = (int)Math.Floor(along), ta = (int)Math.Floor(across);
        return AlongX ? (ua, Y + dy, ta) : (ta, Y + dy, ua);
    }

    internal static int StepOf(float open) => (int)MathF.Round(open * Steps);
}

/// <summary>Where a gate would go in a wall, and what cutting the gateway and hanging the leaves takes.</summary>
public sealed class GatePlan
{
    public bool AlongX;
    public int In, U0, P, Y;
    /// <summary>The wall to cut out of the gateway, and the leaves to lay in their shut place.</summary>
    public readonly VoxelList Cut = new(), Leaves = new();

    static bool Blocking(byte m) => m != 0 && !Mat.Passable[m];

    /// <summary>
    /// A gate through the wall whose side face was clicked: the gateway centred on the click and snapped to the cells,
    /// the leaves on the side toward the citadel. Null if the face is no wall's side, or the wall is too thick.
    /// </summary>
    public static GatePlan At(VoxelWorld w, Site site, VoxelHit hit)
    {
        if (hit.NY != 0 || (hit.NX == 0 && hit.NZ == 0)) return null;
        bool alongX = hit.NZ != 0;
        int n = alongX ? hit.NZ : hit.NX;
        int hitA = alongX ? hit.Z : hit.X, hitU = alongX ? hit.X : hit.Z, keepA = alongX ? site.KeepZ : site.KeepX;
        byte Get(int u, int y, int a) => alongX ? w.Get(u, y, a) : w.Get(a, y, u);
        // through the wall from the clicked face to the air on its far side
        int far = hitA, thick = 0;
        while (thick < 40 && Blocking(Get(hitU, hit.Y, far))) { far -= n; thick++; }
        if (thick < 2 || thick > 32) return null;
        int nearWall = hitA, farWall = far + n;
        // in is toward the citadel; the inner face is where the wall meets the yard
        int inward = (keepA - hitA) * n > 0 ? n : -n;
        int innerWall = inward == n ? nearWall : farWall, outerWall = inward == n ? farWall : nearWall;
        int face = inward > 0 ? innerWall + 1 : innerWall;
        // the leaves hang flush on the inner face; the gateway is snapped to the cells, so open leaves clear it
        int p = face;
        int u0 = (hitU - Gateway.Half + 4) & ~7;
        var plan = new GatePlan { AlongX = alongX, In = inward, U0 = u0, P = p };
        // the threshold: the ground of the yard where the leaves will hang
        int y0 = 0;
        for (int u = u0; u < u0 + Gateway.Width; u++)
            for (int t = 0; t < Gateway.Thick; t++)
            {
                int a = p + (inward > 0 ? t : -1 - t);
                y0 = Math.Max(y0, alongX ? w.SurfAt(u, a) : w.SurfAt(a, u));
            }
        if (y0 <= 0 || y0 + Gateway.Height >= w.SY) return null;
        plan.Y = y0;
        // the gateway: through the wall from face to face
        int aFrom = Math.Min(outerWall, innerWall), aTo = Math.Max(outerWall, innerWall);
        for (int a = aFrom; a <= aTo; a++)
            for (int u = u0; u < u0 + Gateway.Width; u++)
            {
                int x = alongX ? u : a, z = alongX ? a : u;
                if (!w.InBounds(x, y0, z) || site.Forbidden(x, z)) return null;
                for (int y = y0; y < y0 + Gateway.Height; y++)
                {
                    byte m = w.Get(x, y, z);
                    if (m != 0 && Labour.Diggable(m) && !Mat.IsVeg[m]) plan.Cut.Add(x, y, z, Mat.Air);
                    else if (m != 0 && !Labour.Diggable(m) && Blocking(m)) return null;
                }
            }
        // the leaves, shut
        var g = plan.Make();
        for (int i = 0; i < Gateway.Voxels; i++)
        {
            var (x, y, z) = g.Place(i, 0);
            byte m = w.Get(x, y, z);
            if (m == 0 || Mat.IsVeg[m] || Mat.Passable[m] || Mat.IsDecor[m]) plan.Leaves.Add(x, y, z, Mat.Gate);
        }
        if (plan.Leaves.Count < Gateway.Voxels / 2) return null;
        return plan;
    }

    internal Gateway Make() => new() { AlongX = AlongX, In = In, U0 = U0, P = P, Y = Y };
}

public sealed partial class Sim
{
    public readonly List<Gateway> Gates = new();
    int _nextGate = 1;

    /// <summary>Hangs a gate: the gateway cut and the leaves laid by the workers, or (built) all of it at once.</summary>
    public Gateway AddGate(GatePlan plan, bool built = false)
    {
        var g = plan.Make();
        g.Id = _nextGate++;
        Array.Fill(g.At, -1);
        for (int s = 0; s <= Gateway.StepOf(1); s++)
            for (int i = 0; i < Gateway.Voxels; i++)
            {
                var (x, y, z) = g.Place(i, s);
                g.Cells.Add(Nav.CellOf(x, y, z));
            }
        Gates.Add(g);
        if (built)
        {
            for (int i = 0; i < plan.Cut.Count; i++)
            {
                World.Set(plan.Cut.X(i), plan.Cut.Y(i), plan.Cut.Z(i), Mat.Air);
                Forget(plan.Cut.X(i), plan.Cut.Y(i), plan.Cut.Z(i));
            }
            for (int i = 0; i < plan.Leaves.Count; i++) World.Set(plan.Leaves.X(i), plan.Leaves.Y(i), plan.Leaves.Z(i), Mat.Gate);
            Mount(g);
        }
        else
        {
            DesignateRemove(plan.Cut);
            DesignateBuild(plan.Leaves);
        }
        return g;
    }

    /// <summary>The gate a voxel of whose leaves this is, or null.</summary>
    public Gateway GateAt(int x, int y, int z)
    {
        int p = WaterPlanner.Pack(x, y, z);
        foreach (var g in Gates)
            for (int i = 0; i < Gateway.Voxels; i++)
                if (g.At[i] == p) return g;
        // not hung yet: a leaf voxel of its shut place
        foreach (var g in Gates)
            if (!g.Ready && g.Cells.Contains(Nav.CellOf(x, y, z))) return g;
        return null;
    }

    /// <summary>The player's word to a gate: opens for our people, stands open, or barred. A broken gate is mended instead.</summary>
    public void SetGateMode(Gateway g, GateMode mode)
    {
        if (g.Broken || !g.Ready) return;
        g.Mode = mode;
        byte m = LeafMat(g, g.Step);
        for (int i = 0; i < Gateway.Voxels; i++)
        {
            if (g.At[i] < 0) continue;
            var (x, y, z) = WaterPlanner.Unpack(g.At[i]);
            byte cur = World.Get(x, y, z);
            if (Mat.IsLeaf(cur) && cur != m) World.Set(x, y, z, m);
        }
    }

    /// <summary>Takes the leaves off a gate (they rotted, they fell): the gateway stands open until the gate is mended.</summary>
    public void UnhangGate(Gateway g)
    {
        for (int i = 0; i < Gateway.Voxels; i++)
        {
            if (g.At[i] >= 0)
            {
                var (x, y, z) = WaterPlanner.Unpack(g.At[i]);
                if (Mat.IsLeaf(World.Get(x, y, z))) World.Set(x, y, z, Mat.Air);
            }
            g.At[i] = -1;
            g.Gone[i] = true;
        }
        g.Missing = 0;
        g.Broken = true;
        g.Open = g.Want = 0;
    }

    /// <summary>Lays the leaves of a broken gate anew where they hang shut.</summary>
    public int MendGate(Gateway g)
    {
        if (!g.Broken) return 0;
        for (int i = 0; i < Gateway.Voxels; i++)
        {
            if (g.At[i] < 0) continue;
            var (x, y, z) = WaterPlanner.Unpack(g.At[i]);
            if (Mat.IsLeaf(World.Get(x, y, z))) World.Set(x, y, z, Mat.Air);
        }
        Array.Fill(g.At, -1);
        Array.Clear(g.Gone);
        g.Broken = false; g.Ready = false; g.Open = g.Want = 0; g.Step = -1; g.Mode = GateMode.Auto;
        var L = new VoxelList();
        for (int i = 0; i < Gateway.Voxels; i++)
        {
            var (x, y, z) = g.Place(i, 0);
            if (World.Get(x, y, z) == Mat.Air) L.Add(x, y, z, Mat.Gate);
        }
        return DesignateBuild(L);
    }

    /// <summary>
    /// A pawn whose way goes through this cell asks the gate there to open. True while it should wait: the gate is shut
    /// or still turning and will open for it. Wide open, whatever stands in the way is a leaf: go round it.
    /// </summary>
    bool Knock(int cell)
    {
        foreach (var g in Gates)
        {
            if (!g.Ready || g.Broken || g.Mode == GateMode.Shut || !g.Cells.Contains(cell)) continue;
            g.Knocked = Time;
            if (g.Open >= 1) return false;
            return !g.HordeClose || g.Mode == GateMode.Open;
        }
        return false;
    }

    /// <summary>What the leaves are made of at a step: shut (ours knock and pass) or barred, else swinging or open (in everyone's way).</summary>
    static byte LeafMat(Gateway g, int step) => step > 0 ? Mat.GateOpen : g.Mode == GateMode.Shut ? Mat.GateShut : Mat.Gate;

    /// <summary>All the leaves laid where they hang shut: the gate starts working.</summary>
    void Mount(Gateway g)
    {
        int laid = 0;
        for (int i = 0; i < Gateway.Voxels; i++)
        {
            var (x, y, z) = g.Place(i, 0);
            if (Mat.IsLeaf(World.Get(x, y, z))) { g.At[i] = WaterPlanner.Pack(x, y, z); laid++; }
            else { g.At[i] = -1; g.Gone[i] = true; }
        }
        g.Ready = laid > 0;
        g.Missing = Gateway.Voxels - laid;
        g.Step = 0; g.Open = g.Want = 0;
    }

    /// <summary>Leaves laid in their shut place, out of all that can be: the gate is ready when nothing is left to lay.</summary>
    bool Hung(Gateway g)
    {
        for (int i = 0; i < Gateway.Voxels; i++)
        {
            var (x, y, z) = g.Place(i, 0);
            if (Orders.At(x, y, z) == Mat.Gate) return false;
        }
        return true;
    }

    /// <summary>Opens and shuts the gates: for whoever knocked, before the horde, as the player told them.</summary>
    void TickGates(float dt)
    {
        foreach (var g in Gates)
        {
            if (!g.Ready)
            {
                if (Time >= g.NextLook) { g.NextLook = Time + 1; if (Hung(g)) Mount(g); }
                continue;
            }
            if (g.Broken) continue;
            if (Time >= g.NextLook)
            {
                g.NextLook = Time + .3;
                Inspect(g);
                if (g.Broken) continue;
            }
            bool asked = Time - g.Knocked < 2.5;
            g.Want = g.Mode switch
            {
                GateMode.Open => 1,
                GateMode.Shut => 0,
                _ => asked && !g.HordeClose ? 1 : 0,
            };
            float step = dt / 1.2f;
            g.Open = g.Want > g.Open ? Math.Min(g.Want, g.Open + step) : Math.Max(g.Want, g.Open - step);
            int s = Gateway.StepOf(g.Open);
            if (s != g.Step) Turn(g, s);
        }
    }

    /// <summary>Counts what the horde has hacked out of the leaves, and looks for raiders at the gate.</summary>
    void Inspect(Gateway g)
    {
        for (int i = 0; i < Gateway.Voxels; i++)
        {
            if (g.Gone[i] || g.At[i] < 0) continue;
            var (x, y, z) = WaterPlanner.Unpack(g.At[i]);
            if (!Mat.IsLeaf(World.Get(x, y, z))) { g.Gone[i] = true; g.At[i] = -1; }
        }
        if (g.Hacked >= Gateway.Breach)
        {
            g.Broken = true;
            Say("Ворота прорублены: створки больше не закрываются, пока их не починят.", 3);
            return;
        }
        var (mx, my, mz) = g.Middle;
        g.HordeClose = false;
        foreach (var u in Units)
        {
            if (u.Dead || u.Team != Team.Horde) continue;
            float dx = u.X - mx, dz = u.Z - mz;
            if (dx * dx + dz * dz < 44 * 44 && MathF.Abs(u.Y - my) < 32) { g.HordeClose = true; break; }
        }
    }

    /// <summary>Turns the leaves to a step: lifts them out where they stand and sets them down turned.</summary>
    void Turn(Gateway g, int s)
    {
        byte mat = LeafMat(g, s);
        for (int i = 0; i < Gateway.Voxels; i++)
        {
            if (g.Gone[i] || g.At[i] < 0) continue;
            var (x, y, z) = WaterPlanner.Unpack(g.At[i]);
            if (Mat.IsLeaf(World.Get(x, y, z))) World.Set(x, y, z, Mat.Air);
            else g.Gone[i] = true;
            g.At[i] = -1;
        }
        for (int i = 0; i < Gateway.Voxels; i++)
        {
            if (g.Gone[i]) continue;
            var (x, y, z) = g.Place(i, s);
            if (!World.InBounds(x, y, z) || World.Get(x, y, z) != Mat.Air) continue;
            World.Set(x, y, z, mat);
            g.At[i] = WaterPlanner.Pack(x, y, z);
        }
        g.Step = s;
    }
}
