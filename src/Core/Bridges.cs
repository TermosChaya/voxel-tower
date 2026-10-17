using System;

namespace VoxelTower.Core;

/// <summary>
/// Bridges that arch over a gap as real ones do. The deck rises from bank to bank and falls again; under a stone one an
/// arch springs from the banks, thick where it meets them and thin at the crown, with a parapet along either side; a
/// timber one is a plank deck on curved ribs, posts from the ribs up to the deck, and a railing. Laid, like everything,
/// by the workers: from the banks outward, standing on what they have laid.
/// </summary>
public static class Bridges
{
    /// <summary>Spans shorter than this are a step, longer ones would not stand: voxels between the ends.</summary>
    public const int MinSpan = 16, MaxSpan = 200;
    /// <summary>The narrowest walkway that carries a body all the way along, at any angle; the parapets come on top.</summary>
    public const int MinWidth = 10;
    const int Parapet = 2, ParapetHigh = 5, Abutment = 6;

    /// <summary>How far the deck rises over its ends at the middle of a span.</summary>
    public static int Rise(double span) => (int)Math.Clamp(span * .12, 3, 20);

    /// <summary>
    /// A bridge from the ground at A to the ground at B, its walkway <paramref name="width"/> voxels wide, of stone (laid
    /// as rough masonry, or as <paramref name="old"/> weathered masonry) or of timber. Only what is free is planned:
    /// where the arch meets the banks it fills the ground's hollows and stops at the ground.
    /// </summary>
    public static VoxelList Plan(VoxelWorld w, Site site, int ax, int az, int bx, int bz, int width, byte mat, byte old = 0)
    {
        var L = new VoxelList();
        double dx = bx - ax, dz = bz - az, span = Math.Sqrt(dx * dx + dz * dz);
        if (span < MinSpan || span > MaxSpan) return L;
        dx /= span; dz /= span;
        bool stone = mat != Mat.Wood;
        double half = Math.Max(MinWidth, width) / 2.0, outer = half + (stone ? Parapet : 1);
        int ya = w.SurfAt(Math.Clamp(ax, 0, w.SX - 1), Math.Clamp(az, 0, w.SZ - 1)) - 1;
        int yb = w.SurfAt(Math.Clamp(bx, 0, w.SX - 1), Math.Clamp(bz, 0, w.SZ - 1)) - 1;
        int rise = Rise(span), deep = Math.Min(16, rise + 8);
        double reach = outer + 1;
        int x0 = (int)Math.Floor(Math.Min(ax, bx) - reach - Abutment), x1 = (int)Math.Ceiling(Math.Max(ax, bx) + reach + Abutment);
        int z0 = (int)Math.Floor(Math.Min(az, bz) - reach - Abutment), z1 = (int)Math.Ceiling(Math.Max(az, bz) + reach + Abutment);
        void Put(int x, int y, int z, byte m)
        {
            if (y < 1 || y >= w.SY || !Brushes.Free(w.Get(x, y, z))) return;
            L.Add(x, y, z, old != 0 ? old : m == Mat.Stone ? Shapes.Masonry(x, y, z) : m);
        }
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (x < 0 || z < 0 || x >= w.SX || z >= w.SZ || site.Forbidden(x, z)) continue;
                double px = x + .5 - (ax + .5), pz = z + .5 - (az + .5);
                double along = px * dx + pz * dz, side = Math.Abs(-px * dz + pz * dx);
                if (side > outer || along < -Abutment || along > span + Abutment) continue;
                double t = Math.Clamp(along / span, 0, 1), arc = Math.Sin(Math.PI * t);
                int top = (int)Math.Round(ya + (yb - ya) * t + rise * arc);
                bool walk = side <= half;
                if (stone)
                {
                    // the arch: the deck's own thickness at the crown, deep into the banks at the springing
                    int thick = 4 + (int)Math.Round(deep * Math.Pow(1 - arc, 2));
                    int upTo = walk ? top : top + ParapetHigh;
                    for (int y = top - thick + 1; y <= upTo; y++) Put(x, y, z, Mat.Stone);
                }
                else
                {
                    // planks on top; along the edges a curved rib under the deck, and posts from it to the deck
                    if (walk) for (int y = top - 2; y <= top; y++) Put(x, y, z, Mat.Wood);
                    bool edge = side > half - 2.2;
                    if (edge && side <= half)
                    {
                        int rib = top - 3 - (int)Math.Round(deep * Math.Pow(1 - arc, 1.5));
                        Put(x, rib, z, Mat.Wood); Put(x, rib - 1, z, Mat.Wood);
                        if (((int)Math.Floor(along) % 6 + 6) % 6 < 2) for (int y = rib + 1; y < top - 2; y++) Put(x, y, z, Mat.Wood);
                    }
                    if (!walk)
                    {
                        // the railing: a post every six voxels, a rail along the top
                        bool post = ((int)Math.Floor(along) % 6 + 6) % 6 < 1;
                        Put(x, top, z, Mat.Wood);
                        if (post) for (int y = top + 1; y <= top + ParapetHigh; y++) Put(x, y, z, Mat.Wood);
                        else Put(x, top + ParapetHigh, z, Mat.Wood);
                    }
                }
            }
        return L;
    }
}
