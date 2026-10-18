using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>How a drawbridge behaves: down till the horde comes near, then up; held down; held up.</summary>
public enum DrawMode : byte { Auto, Down, Up }

/// <summary>
/// A drawbridge over a ditch or a moat: a deck of oak planks hinged at the bank nearer the citadel, that the chains
/// draw up till it stands on end over the gap. Down it carries everyone across; up it is a wall of timber on the
/// near side of the gap and the way is cut. It goes up by itself when the horde comes near and none of ours is on it,
/// and whoever of the horde is on it then goes down into the gap. Its planks are voxels like everything else,
/// rewritten as it turns: the horde hacks it like timber, fire burns it.
/// </summary>
public sealed class Drawbridge
{
    /// <summary>Planks three voxels thick; from two to six metres long; as wide as the stroke.</summary>
    public const int Thick = 3, MinLength = 16, MaxLength = 48;
    const int Steps = 12;

    public int Id;
    /// <summary>It spans along x (else along z), outward from the hinge by Dir (+1 or −1).</summary>
    public bool AlongX;
    public int Dir;
    /// <summary>The first column of the deck along the span, the first across it, its length and width, and the level walked on (one over the planks).</summary>
    public int Hinge, U0, Length, Width, Y;
    public DrawMode Mode;
    /// <summary>How far up it stands now (0 down, 1 on end), and where it is heading.</summary>
    public float Raise, Want;
    public bool Ready, Broken;
    internal int Step = -1;
    internal double NextLook;
    internal bool HordeNear, OursOn;
    internal int Missing;
    internal int[] At;
    internal bool[] Gone;

    public int Voxels => Length * Width * Thick;
    /// <summary>Planks hacked or burnt out past which it no longer works: a quarter of it.</summary>
    public int Breach => Voxels / 4;

    internal void Alloc() { At = new int[Voxels]; Gone = new bool[Voxels]; Array.Fill(At, -1); }

    /// <summary>The middle of the deck when down, in voxels.</summary>
    public (float x, float y, float z) Middle
    {
        get
        {
            float along = Hinge + Dir * Length / 2f + .5f, across = U0 + Width / 2f;
            return AlongX ? (along, Y, across) : (across, Y, along);
        }
    }

    public int Hacked
    {
        get { int n = 0; foreach (bool g in Gone) if (g) n++; return n - Missing; }
    }

    /// <summary>Where plank voxel i stands with the deck drawn up by step s.</summary>
    internal (int x, int y, int z) Place(int i, int s)
    {
        int t = i % Thick, w = i / Thick % Width, a = i / (Thick * Width);
        double th = s / (double)Steps * Math.PI / 2, c = Math.Cos(th), sn = Math.Sin(th);
        // across the hinge line: outward along the span and up, the deck's top at the hinge's height
        double o = a + .5, h = -(t + .5);
        double o2 = o * c - h * sn, h2 = o * sn + h * c;
        double line = Dir > 0 ? Hinge : Hinge + 1;
        int along = (int)Math.Floor(line + Dir * o2), y = (int)Math.Floor(Y + h2), across = U0 + w;
        return AlongX ? (along, y, across) : (across, y, along);
    }

    internal static int StepOf(float raise) => (int)MathF.Round(raise * Steps);

    /// <summary>Whether a point stands over the deck as it lies down.</summary>
    public bool Over(float x, float z)
    {
        float along = AlongX ? x : z, across = AlongX ? z : x;
        float lo = Dir > 0 ? Hinge : Hinge - Length + 1, hi = lo + Length;
        return along >= lo - 2 && along <= hi + 2 && across >= U0 - 2 && across <= U0 + Width + 2;
    }

    /// <summary>
    /// A drawbridge from the bank at A across to B: hinged where the near bank (to the citadel) gives way to the gap,
    /// as long as the gap is wide, to where the far bank comes up again; square to one axis. Null, and why, where none
    /// can go.
    /// </summary>
    /// <summary>Its planks where it lies down, those that have room: what the workers are to lay.</summary>
    public VoxelList Deck(VoxelWorld w)
    {
        var L = new VoxelList();
        for (int i = 0; i < Voxels; i++)
        {
            var (x, y, z) = Place(i, 0);
            byte m = w.Get(x, y, z);
            if (m == Mat.Air || Mat.IsVeg[m] || Mat.Passable[m] && !Mat.IsLadder[m] || m == Mat.Water) L.Add(x, y, z, Mat.DrawDeck);
        }
        return L;
    }

    public static Drawbridge Plan(VoxelWorld w, Site site, int ax, int az, int bx, int bz, int width, out string why)
    {
        why = null;
        bool alongX = Math.Abs(bx - ax) >= Math.Abs(bz - az);
        // the hinge on the end nearer the citadel
        double da = (ax - site.KeepX) * (double)(ax - site.KeepX) + (az - site.KeepZ) * (double)(az - site.KeepZ);
        double db = (bx - site.KeepX) * (double)(bx - site.KeepX) + (bz - site.KeepZ) * (double)(bz - site.KeepZ);
        if (db < da) { (ax, bx) = (bx, ax); (az, bz) = (bz, az); }
        int near = alongX ? ax : az, far = alongX ? bx : bz, across = alongX ? az : ax;
        int dir = far >= near ? 1 : -1;
        int wd = Math.Clamp(width, Bridges.MinWidth, 24), u0 = across - wd / 2;
        int Surf(int along, int acr) => alongX ? w.SurfAt(Math.Clamp(along, 0, w.SX - 1), Math.Clamp(acr, 0, w.SZ - 1)) : w.SurfAt(Math.Clamp(acr, 0, w.SX - 1), Math.Clamp(along, 0, w.SZ - 1));
        int level = Surf(near, across);
        // out from where the stroke began to where the bank falls away under the deck, and across to where it comes up
        // again, within half a metre of the near bank's height
        int hinge = near;
        while (Math.Abs(hinge - near) < 40 && Surf(hinge, across) >= level - 4) hinge += dir;
        if (Math.Abs(hinge - near) >= 40) { why = "под мостом нет рва: начните у края"; return null; }
        int end = hinge;
        while (Math.Abs(end - hinge) <= MaxLength && Surf(end, across) < level - 4) end += dir;
        int length = Math.Abs(end - hinge);
        if (length > MaxLength) { why = "ров шире 6 м: подъёмному мосту не перекрыть"; return null; }
        if (length < MinLength) { why = "ров уже 2 м: подъёмный мост не нужен"; return null; }
        var b = new Drawbridge { AlongX = alongX, Dir = dir, Hinge = hinge, U0 = u0, Length = length, Width = wd, Y = level };
        // on end it must have room over the gap, and the citadel's ground is no place for it
        for (int i = 0; i < b.Voxels; i++)
        {
            var (x, y, z) = b.Place(i, 0);
            if (!w.InBounds(x, y, z) || site.Forbidden(x, z)) { why = "здесь мост не навесить"; return null; }
        }
        return b;
    }
}

public sealed partial class Sim
{
    public readonly List<Drawbridge> Drawbridges = new();
    int _nextDrawbridge = 1;

    /// <summary>Hangs a drawbridge: the workers lay its planks where it lies down, or (built) all of it at once.</summary>
    public Drawbridge AddDrawbridge(Drawbridge b, bool built = false)
    {
        b.Id = _nextDrawbridge++;
        b.Alloc();
        Drawbridges.Add(b);
        var L = b.Deck(World);
        if (built)
        {
            for (int i = 0; i < L.Count; i++) World.Set(L.X(i), L.Y(i), L.Z(i), Mat.DrawDeck);
            MountBridge(b);
        }
        else
        {
            Orders.Writing?.Drawn.Add(b);
            DesignateBuild(L);
        }
        return b;
    }

    /// <summary>Takes a drawbridge away altogether (undone before it was hung): its planks with it.</summary>
    internal void RemoveDrawbridge(Drawbridge b)
    {
        Drawbridges.Remove(b);
        for (int i = 0; i < b.Voxels; i++)
        {
            var (x, y, z) = b.At[i] >= 0 ? WaterPlanner.Unpack(b.At[i]) : b.Place(i, 0);
            if (World.Get(x, y, z) == Mat.DrawDeck) World.Set(x, y, z, Mat.Air);
            if (Orders.At(x, y, z) == Mat.DrawDeck) Orders.Clear(x, y, z);
        }
    }

    /// <summary>The drawbridge a voxel of whose planks this is, or null.</summary>
    public Drawbridge DrawbridgeAt(int x, int y, int z)
    {
        int p = WaterPlanner.Pack(x, y, z);
        foreach (var b in Drawbridges)
        {
            for (int i = 0; i < b.Voxels; i++) if (b.At[i] == p) return b;
            if (!b.Ready && b.Over(x, z) && Math.Abs(y - b.Y) < 6) return b;
        }
        return null;
    }

    /// <summary>The player's word to a drawbridge: up before the horde by itself, held down, or held up.</summary>
    public void SetDrawMode(Drawbridge b, DrawMode mode)
    {
        if (b.Broken || !b.Ready) return;
        b.Mode = mode;
    }

    /// <summary>All its planks laid where it lies down: it starts working.</summary>
    void MountBridge(Drawbridge b)
    {
        int laid = 0;
        for (int i = 0; i < b.Voxels; i++)
        {
            var (x, y, z) = b.Place(i, 0);
            if (World.Get(x, y, z) == Mat.DrawDeck) { b.At[i] = WaterPlanner.Pack(x, y, z); laid++; }
            else { b.At[i] = -1; b.Gone[i] = true; }
        }
        b.Ready = laid > 0;
        b.Missing = b.Voxels - laid;
        b.Step = 0; b.Raise = b.Want = 0;
    }

    bool BridgeLaid(Drawbridge b)
    {
        for (int i = 0; i < b.Voxels; i++)
        {
            var (x, y, z) = b.Place(i, 0);
            if (Orders.At(x, y, z) == Mat.DrawDeck) return false;
        }
        return true;
    }

    /// <summary>Draws the bridges up and lets them down: before the horde, as the player told them.</summary>
    void TickDrawbridges(float dt)
    {
        foreach (var b in Drawbridges)
        {
            if (!b.Ready)
            {
                if (Time >= b.NextLook) { b.NextLook = Time + 1; if (BridgeLaid(b)) MountBridge(b); }
                continue;
            }
            if (b.Broken) continue;
            if (Time >= b.NextLook)
            {
                b.NextLook = Time + .3;
                InspectBridge(b);
                if (b.Broken) continue;
            }
            b.Want = b.Mode switch
            {
                DrawMode.Up => 1,
                DrawMode.Down => 0,
                // up before the horde, but not with any of ours on it; once up it stays up while the horde is near
                _ => b.HordeNear && (!b.OursOn || b.Raise > 0) ? 1 : 0,
            };
            float step = dt / 2.5f;
            float was = b.Raise;
            b.Raise = b.Want > b.Raise ? Math.Min(b.Want, b.Raise + step) : Math.Max(b.Want, b.Raise - step);
            int s = Drawbridge.StepOf(b.Raise);
            // as it leaves the ground whoever stands on it goes down into the gap
            if (was == 0 && b.Raise > 0) ThrowOff(b);
            if (s != b.Step) TurnBridge(b, s);
        }
    }

    /// <summary>Counts what has been hacked or burnt out of the planks; looks for the horde near and for ours on it.</summary>
    void InspectBridge(Drawbridge b)
    {
        for (int i = 0; i < b.Voxels; i++)
        {
            if (b.Gone[i] || b.At[i] < 0) continue;
            var (x, y, z) = WaterPlanner.Unpack(b.At[i]);
            if (World.Get(x, y, z) != Mat.DrawDeck) { b.Gone[i] = true; b.At[i] = -1; }
        }
        var (mx, my, mz) = b.Middle;
        if (b.Hacked >= b.Breach)
        {
            b.Broken = true;
            Say("Подъёмный мост разбит: он больше не поднимается.", 3, mx, mz);
            return;
        }
        b.HordeNear = b.OursOn = false;
        const float Near = 10 * VoxelWorld.PerMetre;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside) continue;
            if (u.Team == Team.Horde && MathF.Abs(u.Y - my) < 32 && (u.X - mx) * (u.X - mx) + (u.Z - mz) * (u.Z - mz) < Near * Near) b.HordeNear = true;
            if (u.Team == Team.Player && b.Over(u.X, u.Z) && MathF.Abs(u.Y - b.Y) < 6) b.OursOn = true;
        }
    }

    /// <summary>Whoever stands on the deck as it goes up drops off it, down into the gap below.</summary>
    void ThrowOff(Drawbridge b)
    {
        int thrown = 0;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || !b.Over(u.X, u.Z) || MathF.Abs(u.Y - b.Y) > 6) continue;
            int x = (int)u.X, z = (int)u.Z, top = b.Y - Drawbridge.Thick, y = top;
            // down through the gap to whatever is under it, skipping the planks themselves; one on the bank stays there
            while (y > 1 && World.Get(x, y - 1, z) is var m && (m == Mat.Air || m == Mat.DrawDeck || Mat.Passable[m] && m != Mat.Water || m == Mat.Water)) y--;
            if (y >= top - 2) continue;
            int c = Nav.CellOf(x, y, z);
            int fall = (b.Y - y) >> NavGrid.Shift;
            u.Cell = c; u.Y = y;
            u.Path = null; StopCarve(u); StopBuild(u);
            if (fall >= 2) Hurt(u, null, 8 * fall, "сброшен с моста");
            if (u.Team == Team.Horde) thrown++;
        }
        if (thrown > 0) { var (mx, _, mz) = b.Middle; Say(thrown == 1 ? "Мост подняли: налётчик полетел в ров." : $"Мост подняли: {thrown} налётчиков полетели в ров.", 1, mx, mz); }
    }

    /// <summary>Turns the deck to a step: lifts the planks out where they stand and sets them down turned.</summary>
    void TurnBridge(Drawbridge b, int s)
    {
        for (int i = 0; i < b.Voxels; i++)
        {
            if (b.Gone[i] || b.At[i] < 0) continue;
            var (x, y, z) = WaterPlanner.Unpack(b.At[i]);
            if (World.Get(x, y, z) == Mat.DrawDeck) World.Set(x, y, z, Mat.Air);
            else b.Gone[i] = true;
            b.At[i] = -1;
        }
        for (int i = 0; i < b.Voxels; i++)
        {
            if (b.Gone[i]) continue;
            var (x, y, z) = b.Place(i, s);
            if (!World.InBounds(x, y, z) || World.Get(x, y, z) is not (Mat.Air or Mat.Water)) continue;
            World.Set(x, y, z, Mat.DrawDeck);
            b.At[i] = WaterPlanner.Pack(x, y, z);
        }
        b.Step = s;
    }
}
