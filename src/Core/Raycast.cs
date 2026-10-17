using System;

namespace VoxelTower.Core;

public struct VoxelHit
{
    public int X, Y, Z;
    /// <summary>Normal of the face the ray entered through.</summary>
    public int NX, NY, NZ;
    public byte M;

    /// <summary>The empty voxel in front of the face that was hit.</summary>
    public (int x, int y, int z) Front => (X + NX, Y + NY, Z + NZ);

    /// <summary>Where something placed here would stand: on top of the voxel, or on the ground in front of a side face.</summary>
    public (int x, int y, int z) Ground(VoxelWorld w) => NY == 1
        ? (X, Y + 1, Z)
        : (Math.Clamp(X + NX, 0, w.SX - 1), Y, Math.Clamp(Z + NZ, 0, w.SZ - 1));
}

public static class Raycast
{
    /// <summary>
    /// Walks the voxel grid along a ray (origin in voxel units) and stops at the first solid voxel.
    /// With skipVeg the ray passes through leaves, branches and grass, so tools reach the ground under trees.
    /// </summary>
    public static bool Cast(VoxelWorld w, double ox, double oy, double oz, double dx, double dy, double dz, out VoxelHit hit, bool skipVeg = false)
    {
        hit = default;
        Span<double> O = stackalloc double[] { ox, oy, oz };
        Span<double> D = stackalloc double[] { dx, dy, dz };
        Span<double> hi = stackalloc double[] { w.SX, w.SY, w.SZ };
        double t0 = 0, t1 = 1e7; int ax = -1;
        for (int a = 0; a < 3; a++)
        {
            if (Math.Abs(D[a]) < 1e-9) { if (O[a] < 0 || O[a] > hi[a]) return false; continue; }
            double ta = -O[a] / D[a], tb = (hi[a] - O[a]) / D[a];
            if (ta > tb) (ta, tb) = (tb, ta);
            if (ta > t0) { t0 = ta; ax = a; }
            if (tb < t1) t1 = tb;
            if (t0 > t1) return false;
        }
        double px = ox + dx * (t0 + 1e-4), py = oy + dy * (t0 + 1e-4), pz = oz + dz * (t0 + 1e-4);
        int x = Math.Clamp((int)Math.Floor(px), 0, w.SX - 1), y = Math.Clamp((int)Math.Floor(py), 0, w.SY - 1), z = Math.Clamp((int)Math.Floor(pz), 0, w.SZ - 1);
        int sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
        double tx = dx != 0 ? ((x + (sx > 0 ? 1 : 0)) - px) / dx : double.PositiveInfinity;
        double ty = dy != 0 ? ((y + (sy > 0 ? 1 : 0)) - py) / dy : double.PositiveInfinity;
        double tz = dz != 0 ? ((z + (sz > 0 ? 1 : 0)) - pz) / dz : double.PositiveInfinity;
        double ddx = Math.Abs(1 / dx), ddy = Math.Abs(1 / dy), ddz = Math.Abs(1 / dz);
        int nx = 0, ny = 1, nz = 0;
        if (ax == 0) { nx = -sx; ny = 0; } else if (ax == 1) { ny = -sy; } else if (ax == 2) { nz = -sz; ny = 0; }
        int limit = w.SX + w.SY + w.SZ + 8;
        for (int s = 0; s < limit; s++)
        {
            byte m = w.Get(x, y, z);
            if (m != 0 && !(skipVeg && (Mat.IsVeg[m] || Mat.IsDecor[m])))
            {
                hit = new VoxelHit { X = x, Y = y, Z = z, NX = nx, NY = ny, NZ = nz, M = m };
                return true;
            }
            if (tx < ty && tx < tz) { x += sx; tx += ddx; nx = -sx; ny = 0; nz = 0; if (x < 0 || x >= w.SX) return false; }
            else if (ty < tz) { y += sy; ty += ddy; nx = 0; ny = -sy; nz = 0; if (y < 0 || y >= w.SY) return false; }
            else { z += sz; tz += ddz; nx = 0; ny = 0; nz = -sz; if (z < 0 || z >= w.SZ) return false; }
        }
        return false;
    }
}
