using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The world map seen from far off, as the voxels of it would be too many (HexMapView.cs draws it so from the strategic
/// view, and under the pieces made in voxels near the eye): the same land as the pieces (HexTiles.ColumnAt) sampled
/// every few voxels into a mesh of its heights, each point the colour of the ground there — the grass in its drifts,
/// the woods a dark roof of crowns over their ground, the mountains' rock with snow on the heights, the water blue —
/// so from high up it is the same country, only softer. The hexes' plain colours make the small map in the corner.
/// </summary>
public static class HexFar
{
    /// <summary>A hex's colour on the small map (sRGB): its ground, darker for a wood, grey for a mountain.</summary>
    public static uint ColourOf(in HexLook l)
    {
        if (l.Relief == HexRelief.Mountain) return l.Ground == HexGround.Snow ? 0xbfc4ccu : l.Ground is HexGround.Scorched or HexGround.Lava ? 0x2c2624u : 0x6b6560u;
        uint g = l.Ground switch
        {
            HexGround.Grass => 0x56703au, HexGround.Dirt => 0x6a573cu, HexGround.Sand => 0xa8946au, HexGround.Snow => 0xd0d5dc,
            HexGround.Swamp => 0x4a5838u, HexGround.Rough => 0x7d766au, HexGround.Lava => 0x47261cu, HexGround.Scorched => 0x342d2au,
            HexGround.Water => 0x2f5f86u, _ => 0x1f456eu,
        };
        if (l.Relief == HexRelief.Forest) g = l.Ground switch { HexGround.Snow => 0x4a5e52u, HexGround.Scorched or HexGround.Lava => 0x26201cu, HexGround.Sand => 0x67663eu, _ => 0x2f4a28u };
        else if (l.Relief == HexRelief.Hills) g = Shade(g, .9f);
        return g;
    }

    static uint Shade(uint c, float k) =>
        ((uint)Math.Min(255, ((c >> 16) & 255) * k) << 16) | ((uint)Math.Min(255, ((c >> 8) & 255) * k) << 8) | (uint)Math.Min(255, (c & 255) * k);

    static float Lin(uint c, int shift) => MathF.Pow(((c >> shift) & 255) / 255f, 2.2f);

    /// <summary>The far mesh of a block: positions and normals in the map's voxels, linear colours, its triangles by index.</summary>
    public sealed class Block
    {
        public readonly List<float> Pos = new(), Nrm = new(), Col = new();
        public readonly List<int> Idx = new();
        public int Vertices => Pos.Count / 3;
    }

    /// <summary>How many voxels apart the far mesh's points are.</summary>
    public const int Every = 8;

    /// <summary>
    /// The far mesh of the land under a block of rows and columns of hexes (from the west edge of the first column to the
    /// west edge of the one after the last, from the top of the first row to the top of the one after): the land's
    /// heights every <see cref="Every"/> voxels, laid a voxel and a half under the ground the pieces in voxels make, so
    /// that where those are drawn they hide it.
    /// </summary>
    public static Block Build(IHexSource src, HexMapLayout lay, int col0, int row0, int col1, int row1, float sink = 1.5f)
    {
        var b = new Block();
        float x0 = lay.OX + HexMapLayout.W * col0 - HexMapLayout.HalfW, x1 = lay.OX + HexMapLayout.W * col1 - HexMapLayout.HalfW;
        float z0 = lay.OZ + HexMapLayout.RowStep * row0 - HexMapLayout.RowStep * .5f, z1 = lay.OZ + HexMapLayout.RowStep * row1 - HexMapLayout.RowStep * .5f;
        x0 = Math.Max(0, x0); z0 = Math.Max(0, z0); x1 = Math.Min(lay.SX - 1, x1); z1 = Math.Min(lay.SZ - 1, z1);
        if (x1 <= x0 || z1 <= z0) return b;
        int i0 = (int)MathF.Floor(x0 / Every), i1 = (int)MathF.Ceiling(x1 / Every), j0 = (int)MathF.Floor(z0 / Every), j1 = (int)MathF.Ceiling(z1 / Every);
        int nx = i1 - i0 + 1, nz = j1 - j0 + 1;
        // a ring of points about the block, for the normals at its edges
        var h = new float[(nx + 2) * (nz + 2)];
        var colour = new uint[nx * nz];
        for (int j = -1; j <= nz; j++)
            for (int i = -1; i <= nx; i++)
            {
                int x = Math.Clamp((i0 + i) * Every, 0, lay.SX - 1), z = Math.Clamp((j0 + j) * Every, 0, lay.SZ - 1);
                var c = HexTiles.ColumnAt(src, lay, x, z);
                float y = Math.Max(c.Top, c.Water);
                bool inside = i >= 0 && j >= 0 && i < nx && j < nz;
                // the woods: a roof of crowns over their ground, dark, a little uneven
                bool wood = c.Wood > .5f && c.Water == 0 && c.Mount < 3;
                if (wood) y += 4 + (float)Hash.H3(x >> 3, 5, z >> 3) * 2;
                h[(j + 1) * (nx + 2) + i + 1] = y - sink;
                if (!inside) continue;
                uint col;
                if (c.Water > c.Top) col = c.Water >= HexTiles.WaterTop && c.Top < HexTiles.WaterTop - 4 ? 0x1d3f5cu : 0x2c5872u;
                else if (wood) col = c.Mat == Mat.Snow ? 0x405248u : Hash.H3(x >> 2, 3, z >> 2) < .5 ? 0x2a4422u : 0x34502au;
                else if (c.Mount > 6 && c.Top >= c.SnowAt && !c.Volcanic) col = 0xdfe4ea;
                else if (c.Mount > 4) col = c.Volcanic ? 0x2a2523u : Hash.H3(x >> 3, 7, z >> 3) < .5 ? 0x6a6560u : 0x5b5754u;
                else col = Mat.Def[c.Mat].Srgb;
                colour[j * nx + i] = col;
            }
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                float y = h[(j + 1) * (nx + 2) + i + 1];
                float dxh = h[(j + 1) * (nx + 2) + i + 2] - h[(j + 1) * (nx + 2) + i], dzh = h[(j + 2) * (nx + 2) + i + 1] - h[j * (nx + 2) + i + 1];
                float ny = 2 * Every, len = MathF.Sqrt(dxh * dxh + ny * ny + dzh * dzh);
                b.Pos.Add((i0 + i) * Every); b.Pos.Add(y); b.Pos.Add((j0 + j) * Every);
                b.Nrm.Add(-dxh / len); b.Nrm.Add(ny / len); b.Nrm.Add(-dzh / len);
                uint c = colour[j * nx + i];
                b.Col.Add(Lin(c, 16)); b.Col.Add(Lin(c, 8)); b.Col.Add(Lin(c, 0)); b.Col.Add(1);
            }
        // two triangles to a square, their corners clockwise as seen from above (as Godot takes a front face)
        for (int j = 0; j < nz - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                int a = j * nx + i, bb = a + 1, c = a + nx, d = c + 1;
                b.Idx.Add(a); b.Idx.Add(bb); b.Idx.Add(c);
                b.Idx.Add(bb); b.Idx.Add(d); b.Idx.Add(c);
            }
        return b;
    }
}
