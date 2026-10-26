using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Which way the world map's rivers run (HexMapView.cs gives it to the water's shader, so the ripples go downstream):
/// each river is followed from where it leaves — into a lake or the sea, off the map's edge, into a gorge of the
/// mountains — up through the hexes it runs through,
/// so every hex of it knows which of its river's sides is downstream; a river that meets no water runs down the hexes'
/// heights. Laid out as a texture of the current over the map, a texel to every <see cref="Texel"/> voxels: x and z each
/// a byte, 128 still.
/// </summary>
public static class HexFlow
{
    public const int Texel = 4;
    /// <summary>The current along a river, voxels a second, and the most the texture holds (the shader's flow_max).</summary>
    public const float Speed = 2.4f, Most = 6f;

    static bool Wet(in HexLook l) => l.Ground is HexGround.Water or HexGround.DeepWater;

    /// <summary>How many river steps each hex of a river is from where it meets water (−1: a hex with no river, or one that meets none).</summary>
    public static int[] Downhill(IHexSource src)
    {
        int w = src.Width, h = src.Height;
        var dist = new int[w * h];
        Array.Fill(dist, -1);
        var q = new Queue<(int c, int r)>();
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                var l = src.Look(c, r);
                if (l.Rivers == 0 || Wet(l)) continue;
                for (int s = 0; s < 6; s++)
                {
                    if ((l.Rivers & (1 << s)) == 0) continue;
                    var (nc, nr) = HexMapLayout.Neighbour(c, r, s);
                    bool off = (uint)nc >= (uint)w || (uint)nr >= (uint)h;
                    // it leaves over this side into water, off the map, or into land that takes no river back (it
                    // falls into a gorge in the mountains, or runs on beyond what the map tells)
                    if (off || Wet(src.Look(nc, nr)) || (src.Look(nc, nr).Rivers & (1 << ((s + 3) % 6))) == 0) { dist[r * w + c] = 0; q.Enqueue((c, r)); break; }
                }
            }
        while (q.Count > 0)
        {
            var (c, r) = q.Dequeue();
            var l = src.Look(c, r);
            for (int s = 0; s < 6; s++)
            {
                if ((l.Rivers & (1 << s)) == 0) continue;
                var (nc, nr) = HexMapLayout.Neighbour(c, r, s);
                if ((uint)nc >= (uint)w || (uint)nr >= (uint)h || dist[nr * w + nc] >= 0) continue;
                var n = src.Look(nc, nr);
                // the neighbour's river must come back over the same side
                if ((n.Rivers & (1 << ((s + 3) % 6))) == 0 || Wet(n)) continue;
                dist[nr * w + nc] = dist[r * w + c] + 1;
                q.Enqueue((nc, nr));
            }
        }
        return dist;
    }

    /// <summary>The current over the whole map: its texture's width and height, and its texels (x, z), 128 still.</summary>
    public static (int W, int H, byte[] Px) Lay(IHexSource src, HexMapLayout lay)
    {
        int tw = (lay.SX + Texel - 1) / Texel, th = (lay.SZ + Texel - 1) / Texel;
        var fx = new float[tw * th];
        var fz = new float[tw * th];
        var dist = Downhill(src);
        int w = src.Width, h = src.Height;
        const float reach = 5.5f;
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                var l = src.Look(c, r);
                if (l.Rivers == 0 || Wet(l)) continue;
                int d = dist[r * w + c];
                var (cx, cz) = lay.Center(c, r);
                for (int s = 0; s < 6; s++)
                {
                    if ((l.Rivers & (1 << s)) == 0) continue;
                    var (nc, nr) = HexMapLayout.Neighbour(c, r, s);
                    bool off = (uint)nc >= (uint)w || (uint)nr >= (uint)h;
                    var n = off ? default : src.Look(nc, nr);
                    // downstream over this side: toward water or wherever it leaves, toward the nearer end of the river, or down the land
                    bool leaves = off || Wet(n) || (n.Rivers & (1 << ((s + 3) % 6))) == 0;
                    bool down = leaves && d == 0 || off || Wet(n) || d >= 0 && dist[nr * w + nc] >= 0 && dist[nr * w + nc] < d
                        || (d < 0 || dist[nr * w + nc] < 0) && n.Height < l.Height;
                    var (tx, tz) = HexMapLayout.Toward(s);
                    float ex = cx + tx * .51f, ez = cz + tz * .51f;
                    float len = MathF.Sqrt((ex - cx) * (ex - cx) + (ez - cz) * (ez - cz));
                    float ux = (ex - cx) / len * (down ? 1 : -1), uz = (ez - cz) / len * (down ? 1 : -1);
                    int i0 = Math.Max(0, (int)((MathF.Min(cx, ex) - reach) / Texel)), i1 = Math.Min(tw - 1, (int)((MathF.Max(cx, ex) + reach) / Texel));
                    int j0 = Math.Max(0, (int)((MathF.Min(cz, ez) - reach) / Texel)), j1 = Math.Min(th - 1, (int)((MathF.Max(cz, ez) + reach) / Texel));
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            float px = (i + .5f) * Texel, pz = (j + .5f) * Texel;
                            float vx = ex - cx, vz = ez - cz, wx = px - cx, wz = pz - cz;
                            float t = Math.Clamp((wx * vx + wz * vz) / (vx * vx + vz * vz), 0, 1);
                            float ddx = wx - vx * t, ddz = wz - vz * t;
                            float dd = MathF.Sqrt(ddx * ddx + ddz * ddz);
                            if (dd > reach) continue;
                            float k = 1 - dd / reach;
                            fx[j * tw + i] += ux * k; fz[j * tw + i] += uz * k;
                        }
                }
            }
        var px2 = new byte[tw * th * 2];
        for (int i = 0; i < tw * th; i++)
        {
            float x = fx[i], z = fz[i], len = MathF.Sqrt(x * x + z * z);
            if (len > 1e-4f) { x = x / len * Speed; z = z / len * Speed; }
            px2[i * 2] = (byte)Math.Clamp((int)MathF.Round(128 + x / Most * 127), 0, 255);
            px2[i * 2 + 1] = (byte)Math.Clamp((int)MathF.Round(128 + z / Most * 127), 0, 255);
        }
        return (tw, th, px2);
    }
}
