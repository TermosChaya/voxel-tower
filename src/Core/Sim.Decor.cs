using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>A building's dressing (a torch in a sconce, a banner, a barrel, a crate, sacks); or the fire of a campfire or of a torch or brazier set out on the ground (Sim.Yard.cs).</summary>
public enum FixtureKind : byte { Torch, Banner, Barrel, Crate, Sacks, Campfire, Brazier }

/// <summary>A piece of a building's dressing: what it is, the voxels it is made of, the building voxel it hangs on or stands by.</summary>
public sealed class Fixture
{
    public FixtureKind Kind;
    /// <summary>The voxel of the building it belongs to, packed: when that is gone, so is it.</summary>
    public int Anchor;
    public readonly List<(int x, int y, int z, byte m)> Voxels = new();
    /// <summary>Where a torch's flame burns, in voxels.</summary>
    public float LX, LY, LZ;
    /// <summary>A torch a witch of the horde put out: till when it stays dark (0 for one alight).</summary>
    public double DarkUntil;
}

/// <summary>
/// Buildings dress themselves. A while after walls and towers of ours go up, torches are set in sconces along their
/// inner faces and either side of their gates, a banner hangs from the top of a tower toward the west, and barrels,
/// crates and sacks stand along the foot of the walls in the yard. Nothing of it costs anything or blocks a builder:
/// whatever is laid where it stands takes its place, and when the wall it hangs on falls it goes with it.
/// </summary>
public sealed partial class Sim
{
    public readonly List<Fixture> Fixtures = new();
    public event Action<Fixture> FixtureAdded, FixtureRemoved;
    /// <summary>Chunks of the design where something of ours was laid since they were last looked over.</summary>
    readonly HashSet<int> _decorDirty = new();
    double _nextDecor;

    const int TorchHigh = 18, TorchApart = 32, BannerApart = 80, PropApart = 44;

    void DecorDirty(int x, int y, int z) => _decorDirty.Add(Design.ChunkIndex(x >> VoxelWorld.Shift, y >> VoxelWorld.Shift, z >> VoxelWorld.Shift));

    /// <summary>Every couple of seconds: takes down what lost its wall, and dresses a few chunks of what was built.</summary>
    void TickDecor()
    {
        if (Time < _nextDecor) return;
        _nextDecor = Time + 2;
        for (int i = Fixtures.Count - 1; i >= 0; i--) if (!Whole(Fixtures[i])) TakeDown(i);
        // the torches put out are lit again when their minute is up
        foreach (var f in Fixtures) if (f.DarkUntil > 0 && f.DarkUntil <= Time) Relight(f);
        Decorate(8);
    }

    /// <summary>Dresses up to <paramref name="chunks"/> chunks of what was built since last time (all of it, given int.MaxValue).</summary>
    public void Decorate(int chunks = int.MaxValue)
    {
        if (_decorDirty.Count == 0) return;
        var todo = new List<int>();
        foreach (int ci in _decorDirty) { todo.Add(ci); if (todo.Count >= chunks) break; }
        foreach (int ci in todo) { _decorDirty.Remove(ci); DecorateChunk(ci); }
    }

    bool Whole(Fixture f)
    {
        var (ax, ay, az) = WaterPlanner.Unpack(f.Anchor);
        if (MeantAt(ax, ay, az) == 0 || Brushes.Free(World.Get(ax, ay, az))) return false;
        // a torch put out stands with its sconce where the flame was
        foreach (var (x, y, z, m) in f.Voxels) if (World.Get(x, y, z) != m && !(m == Mat.Flame && f.DarkUntil > 0 && World.Get(x, y, z) == Mat.Sconce)) return false;
        return true;
    }

    /// <summary>A torch put out: its flame is gone for a while.</summary>
    void Quench(Fixture f, double seconds)
    {
        f.DarkUntil = Time + seconds;
        foreach (var (x, y, z, m) in f.Voxels) if (m == Mat.Flame && World.Get(x, y, z) == Mat.Flame) World.Set(x, y, z, Mat.Sconce);
    }

    void Relight(Fixture f)
    {
        f.DarkUntil = 0;
        foreach (var (x, y, z, m) in f.Voxels) if (m == Mat.Flame && World.Get(x, y, z) == Mat.Sconce) World.Set(x, y, z, Mat.Flame);
    }

    void TakeDown(int i)
    {
        var f = Fixtures[i];
        foreach (var (x, y, z, m) in f.Voxels) if (World.Get(x, y, z) == m) World.Set(x, y, z, Mat.Air);
        Fixtures.RemoveAt(i);
        FixtureRemoved?.Invoke(f);
    }

    static readonly int[] NX4 = { 1, -1, 0, 0 }, NZ4 = { 0, 0, 1, -1 };

    /// <summary>A meant voxel that stands.</summary>
    bool Built(int x, int y, int z)
    {
        byte meant = MeantAt(x, y, z);
        return meant != 0 && Stands(World.Get(x, y, z), meant);
    }

    bool Clear(int x, int y, int z) => World.InBounds(x, y, z) && World.Get(x, y, z) == Mat.Air;

    /// <summary>No fixture of a kind within a distance of a spot (heights count a third as much).</summary>
    bool Room(FixtureKind kind, float x, float y, float z, float apart)
    {
        foreach (var f in Fixtures)
        {
            if (f.Kind != kind && !(IsProp(kind) && IsProp(f.Kind))) continue;
            var (fx, fy, fz) = f.Voxels.Count > 0 ? (f.Voxels[0].x, f.Voxels[0].y, f.Voxels[0].z) : (0, 0, 0);
            float dx = fx - x, dy = (fy - y) * 3, dz = fz - z;
            if (dx * dx + dz * dz < apart * apart && Math.Abs(dy) < apart) return false;
        }
        return true;
    }

    static bool IsProp(FixtureKind k) => k is FixtureKind.Barrel or FixtureKind.Crate or FixtureKind.Sacks;

    bool NearGate(int x, int z, float r)
    {
        foreach (var g in Gates)
        {
            var (mx, _, mz) = g.Middle;
            if ((mx - x) * (mx - x) + (mz - z) * (mz - z) < r * r) return true;
        }
        return false;
    }

    bool NearLadder(int x, int y, int z, int r)
    {
        for (int dz = -r; dz <= r; dz += 2)
            for (int dx = -r; dx <= r; dx += 2)
                for (int dy = 0; dy < 16; dy += 3)
                    if (Mat.IsLadder[World.Get(x + dx, y + dy, z + dz)]) return true;
        return false;
    }

    /// <summary>Whether anything built, or meant to be, stands over a spot within twelve metres: a roof over it.</summary>
    bool Roofed(int x, int y, int z)
    {
        for (int yy = y + 8; yy < Math.Min(World.SY - 1, y + 96); yy++)
        {
            byte m = World.Get(x, yy, z);
            if (m != Mat.Air && Mat.IsPlayer[m] || MeantAt(x, yy, z) != 0) return true;
        }
        foreach (var zn in Zones) if (zn.Kind == Zone.Dwelling && zn.Contains(x, z)) return true;
        return false;
    }

    void DecorateChunk(int ci)
    {
        var d = Design.ChunkData(ci);
        if (d == null) return;
        Design.ChunkCoords(ci, out int cx, out int cy, out int cz);
        int bx = cx * VoxelWorld.CS, by = cy * VoxelWorld.CS, bz = cz * VoxelWorld.CS;
        for (int lz = 0; lz < VoxelWorld.CS; lz++)
            for (int lx = 0; lx < VoxelWorld.CS; lx++)
            {
                int x = bx + lx, z = bz + lz;
                bool any = false;
                for (int ly = 0; ly < VoxelWorld.CS && !any; ly++) any = d[(ly << 10) | (lz << 5) | lx] != 0;
                // (not round the settlement's furnishing: a torch on a pole is no wall to hang one on)
                if (!any || Site.Forbidden(x, z) || InYard(x, z)) continue;
                for (int dir = 0; dir < 4; dir++)
                {
                    int nx = NX4[dir], nz = NZ4[dir], fx = x + nx, fz = z + nz;
                    if (!World.InBounds(fx, 0, fz) || MeantAt(fx, World.SurfAt(fx, fz), fz) != 0) continue;
                    int ground = World.SurfAt(fx, fz);
                    bool inward = nx * (Site.KeepX - x) + nz * (Site.KeepZ - z) > 0;
                    TryTorch(x, z, nx, nz, ground, inward, by);
                    if (!inward) TryBanner(x, z, nx, nz, ground, by);
                    else TryProp(x, z, nx, nz, ground, by);
                }
            }
    }

    /// <summary>A torch in a sconce on a wall's face, a man's height and a bit over the ground in front: inside, or by a gate.</summary>
    void TryTorch(int x, int z, int nx, int nz, int ground, bool inward, int by)
    {
        int y = ground + TorchHigh, fx = x + nx, fz = z + nz;
        if (y < by || y >= by + VoxelWorld.CS) return;
        if (!inward && !NearGate(x, z, 30)) return;
        if (!Built(x, y, z) || !Built(x, y + 4, z) && !Built(x - nx, y + 4, z - nz) || !Built(x, y - 8, z) && !Built(x - nx, y - 8, z - nz)) return;
        for (int dy = -1; dy <= 3; dy++) if (!Clear(fx, y + dy, fz)) return;
        if (!Room(FixtureKind.Torch, fx, y, fz, TorchApart)) return;
        var f = new Fixture { Kind = FixtureKind.Torch, Anchor = WaterPlanner.Pack(x, y, z), LX = fx + .5f + nx * .2f, LY = y + 2.4f, LZ = fz + .5f + nz * .2f };
        f.Voxels.Add((fx, y - 1, fz, Mat.Sconce));
        f.Voxels.Add((fx, y, fz, Mat.Sconce));
        f.Voxels.Add((fx, y + 1, fz, Mat.Flame));
        Hang(f);
    }

    /// <summary>A banner hanging from the top of a tall wall or tower on its outer face: the blue of the garrison with a gold band.</summary>
    void TryBanner(int x, int z, int nx, int nz, int ground, int by)
    {
        // the top of this column of the building, well above the ground in front
        int top = -1;
        for (int y = Math.Max(by, ground + 30); y < by + VoxelWorld.CS; y++)
            if (MeantAt(x, y, z) != 0 && MeantAt(x, y + 1, z) == 0) { top = y; break; }
        if (top < 0 || top - ground < 34) return;
        int tx = -nz, tz = nx, fx = x + nx, fz = z + nz;
        const int W = 4, H = 9;
        for (int t = -2; t < W - 2; t++)
            for (int r = 0; r < H; r++)
            {
                // the face is rough: a stone set back or a raked joint behind the cloth is wall enough
                int y = top - 3 - r, ux = x + tx * t, uz = z + tz * t;
                if (!Built(ux, y, uz) && !Built(ux - nx, y, uz - nz) || !Clear(fx + tx * t, y, fz + tz * t)) return;
            }
        if (!Room(FixtureKind.Banner, fx, top, fz, BannerApart)) return;
        var f = new Fixture { Kind = FixtureKind.Banner, Anchor = WaterPlanner.Pack(x, top - 3, z) };
        for (int t = -2; t < W - 2; t++)
            for (int r = 0; r < H; r++)
            {
                bool edge = t == -2 || t == W - 3;
                // swallow-tailed at the foot, a gold band at the head and a gold mark in the middle
                if (r == H - 1 && !edge) continue;
                byte m = r == 1 || r == 4 && !edge ? Mat.BannerTrim : Mat.Banner;
                f.Voxels.Add((fx + tx * t, top - 3 - r, fz + tz * t, m));
            }
        Hang(f);
    }

    /// <summary>
    /// Barrels, crates or sacks at the foot of a wall's inner face, standing clear of gates, ladders and work: in a yard
    /// under the open sky, never in a room under a roof (a house's floor is for its furniture).
    /// </summary>
    void TryProp(int x, int z, int nx, int nz, int ground, int by)
    {
        if (ground < by || ground >= by + VoxelWorld.CS) return;
        if (Roofed(x + nx * 3, ground, z + nz * 3)) return;
        if (!Built(x, ground + 2, z) && !Built(x - nx, ground + 2, z - nz) || !Built(x, ground + 14, z) && !Built(x - nx, ground + 14, z - nz)) return;
        if (NearGate(x, z, 30) || NearLadder(x + nx * 4, ground, z + nz * 4, 12)) return;
        double pick = Hash.H3(x * 3, ground, z * 5);
        var kind = pick < .5 ? FixtureKind.Barrel : pick < .82 ? FixtureKind.Crate : FixtureKind.Sacks;
        int size = kind == FixtureKind.Barrel ? 5 : 4;
        // the footprint: a voxel off the wall, across its face from the spot on along it
        int tx = -nz, tz = nx, ox = x + nx * 2, oz = z + nz * 2;
        if (!Room(FixtureKind.Barrel, ox, ground, oz, PropApart)) return;
        var f = new Fixture { Kind = kind, Anchor = WaterPlanner.Pack(x, ground + 2, z) };
        for (int a = 0; a < size; a++)
            for (int t = 0; t < size; t++)
            {
                int px = ox + nx * a + tx * t, pz = oz + nz * a + tz * t;
                if (World.SurfAt(px, pz) != ground || Orders.At(px, ground, pz) != 0 || MeantAt(px, ground, pz) != 0) return;
                for (int y = ground; y < ground + 7; y++) if (!Clear(px, y, pz) && !Mat.IsVeg[World.Get(px, y, pz)]) return;
                double ca = a - (size - 1) / 2.0, ct = t - (size - 1) / 2.0;
                switch (kind)
                {
                    case FixtureKind.Barrel:
                        if (ca * ca + ct * ct > 2.6 * 2.6 - .5) break;
                        for (int y = 0; y < 6; y++) f.Voxels.Add((px, ground + y, pz, y is 1 or 4 ? Mat.Hoop : Mat.Cask));
                        break;
                    case FixtureKind.Crate:
                        for (int y = 0; y < 4; y++)
                        {
                            int edges = (a == 0 || a == size - 1 ? 1 : 0) + (t == 0 || t == size - 1 ? 1 : 0) + (y == 0 || y == 3 ? 1 : 0);
                            f.Voxels.Add((px, ground + y, pz, edges >= 2 ? Mat.CrateEdge : Mat.Crate));
                        }
                        // a smaller one on top, at the back
                        if (a < 2 && t < 3) for (int y = 4; y < 6; y++) f.Voxels.Add((px, ground + y, pz, y == 5 || a == 0 ? Mat.CrateEdge : Mat.Crate));
                        break;
                    default:
                        if (ca * ca + ct * ct > 4) break;
                        int high = ca * ca + ct * ct < 1.2 ? 3 : 2;
                        for (int y = 0; y < high; y++) f.Voxels.Add((px, ground + y, pz, Mat.Sack));
                        break;
                }
            }
        if (f.Voxels.Count > 0) Hang(f);
    }

    void Hang(Fixture f)
    {
        // nothing in the way of a gate's leaves as they swing
        foreach (var (x, y, z, _) in f.Voxels)
            foreach (var g in Gates)
                if (g.Cells.Contains(Nav.CellOf(x, y, z))) return;
        foreach (var (x, y, z, m) in f.Voxels) World.Set(x, y, z, m);
        Fixtures.Add(f);
        FixtureAdded?.Invoke(f);
    }
}
