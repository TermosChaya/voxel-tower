using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The world map seen from far off, as the voxels of it would be too many (HexMapView.cs draws it so from the strategic
/// view, and under the pieces made in voxels near the eye): each hex a flat hexagon of its ground's colour at its
/// height, a wood darker, a hill a low pyramid, a mountain a tall one with snow at its top, a lake blue. The same
/// colours make the small map in the corner.
/// </summary>
public static class HexFar
{
    /// <summary>A hex's colour on the small map and from far off (sRGB): its ground, darker for a wood, grey for a mountain.</summary>
    public static uint ColourOf(in HexLook l)
    {
        if (l.Relief == HexRelief.Mountain) return l.Ground == HexGround.Snow ? 0xc9ced6u : 0x77716bu;
        uint g = l.Ground switch
        {
            HexGround.Grass => 0x4f7a32u, HexGround.Dirt => 0x6e5536u, HexGround.Sand => 0xb39b6au, HexGround.Snow => 0xd4dae2,
            HexGround.Swamp => 0x435c3bu, HexGround.Rough => 0x8a8273u, HexGround.Lava => 0x4a241au, HexGround.Scorched => 0x2e2826u,
            HexGround.Water => 0x2a6aa6u, _ => 0x1c4880u,
        };
        if (l.Relief == HexRelief.Forest) g = l.Ground switch { HexGround.Snow => 0x40594au, HexGround.Scorched or HexGround.Lava => 0x241f1cu, HexGround.Sand => 0x6f6a3au, _ => 0x24461eu };
        else if (l.Relief == HexRelief.Hills) g = Shade(g, .88f);
        return g;
    }

    static uint Shade(uint c, float k) =>
        ((uint)Math.Min(255, ((c >> 16) & 255) * k) << 16) | ((uint)Math.Min(255, ((c >> 8) & 255) * k) << 8) | (uint)Math.Min(255, (c & 255) * k);

    static float Lin(uint c, int shift) => MathF.Pow(((c >> shift) & 255) / 255f, 2.2f);

    /// <summary>The far mesh of a block of hexes: positions and normals in the map's voxels, linear colours.</summary>
    public sealed class Block
    {
        public readonly List<float> Pos = new(), Nrm = new(), Col = new();
        public int Vertices => Pos.Count / 3;

        public void Tri((float x, float y, float z) a, (float x, float y, float z) b, (float x, float y, float z) c, uint colour, float shade = 1)
        {
            float ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx, len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-6f) return;
            nx /= len; ny /= len; nz /= len;
            // the face to look up and out, its corners clockwise as seen from there (as Godot takes a front face)
            if (ny > 0) (b, c) = (c, b);
            else { nx = -nx; ny = -ny; nz = -nz; }
            float r = Lin(colour, 16) * shade, g = Lin(colour, 8) * shade, bl = Lin(colour, 0) * shade;
            foreach (var p in new[] { a, b, c })
            {
                Pos.Add(p.x); Pos.Add(p.y); Pos.Add(p.z);
                Nrm.Add(nx); Nrm.Add(ny); Nrm.Add(nz);
                Col.Add(r); Col.Add(g); Col.Add(bl); Col.Add(1);
            }
        }
    }

    static readonly (float x, float z)[] Corners =
    {
        (0, -HexMapLayout.R), (HexMapLayout.HalfW, -HexMapLayout.R / 2f), (HexMapLayout.HalfW, HexMapLayout.R / 2f),
        (0, HexMapLayout.R), (-HexMapLayout.HalfW, HexMapLayout.R / 2f), (-HexMapLayout.HalfW, -HexMapLayout.R / 2f),
    };

    /// <summary>
    /// The far mesh of the hexes of a block of rows and columns: its hexes laid a voxel and a half under the ground the
    /// pieces in voxels make, so that where those are drawn they hide it.
    /// </summary>
    public static Block Build(IHexSource src, HexMapLayout lay, int col0, int row0, int col1, int row1, float sink = 1.5f)
    {
        var b = new Block();
        for (int row = row0; row < row1; row++)
            for (int col = col0; col < col1; col++)
            {
                var l = HexTiles.LookOf(src, col, row);
                var (cx, cz) = lay.Center(col, row);
                bool wet = l.Ground is HexGround.Water or HexGround.DeepWater;
                float y = (wet ? HexTiles.WaterTop : HexTiles.Base + l.Height * HexTiles.Step) - sink;
                uint colour = ColourOf(l);
                if (l.Relief is HexRelief.Mountain or HexRelief.Hills)
                {
                    float peak = l.Relief == HexRelief.Mountain ? (24 + HexTiles.Vary(col, row) % 11) * .55f : 1.5f;
                    var top = (cx, y + peak, cz);
                    for (int k = 0; k < 6; k++)
                    {
                        var (ax, az) = Corners[k]; var (bx, bz) = Corners[(k + 1) % 6];
                        float shade = .8f + .2f * MathF.Cos((k - 4) * MathF.PI / 3);
                        b.Tri((cx + ax, y, cz + az), (cx + bx, y, cz + bz), top, colour, shade);
                    }
                    if (l.Relief == HexRelief.Mountain && l.Ground != HexGround.Snow)
                    {
                        // a cap of snow: a small pyramid over the top third
                        float k3 = .32f;
                        var cap = (cx, y + peak + .2f, cz);
                        for (int k = 0; k < 6; k++)
                        {
                            var (ax, az) = Corners[k]; var (bx, bz) = Corners[(k + 1) % 6];
                            b.Tri((cx + ax * k3, y + peak * (1 - k3) + .2f, cz + az * k3), (cx + bx * k3, y + peak * (1 - k3) + .2f, cz + bz * k3), cap, 0xe8edf2u);
                        }
                    }
                    continue;
                }
                for (int k = 0; k < 6; k++)
                {
                    var (ax, az) = Corners[k]; var (bx, bz) = Corners[(k + 1) % 6];
                    b.Tri((cx, y, cz), (cx + ax, y, cz + az), (cx + bx, y, cz + bz), colour);
                }
            }
        return b;
    }
}
