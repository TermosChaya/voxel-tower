using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The land beyond the valley, round it out to some seven hundred metres, seen only from someone's eyes (a camera with
/// a view to the horizon); over the diorama the valley stays a block on its sea of mist. Its inner edge takes the
/// valley's own ground along the border a voxel lower, so the land goes on without a step and the block's earthen
/// sides are hidden; outward the border's heights smooth into rolling country, forests stand up out of it as dark heaps
/// of crowns, and it rises into hills towards the rim and into the haze. The old road goes on west out of the valley, the
/// river north and south out of it in a channel of its own. One mesh in one draw call, with no shadows, lit by the same
/// sun as the valley and going into the colour of the horizon the further off it is (shaders/far_land.gdshader).
/// Some fifty thousand triangles; it is built once with the valley, from the valley's own heights.
/// </summary>
public partial class FarLand : MeshInstance3D
{
    /// <summary>How far the far land reaches from the middle of the valley, in scene units (two metres each).</summary>
    public const float Reach = 360;
    /// <summary>The triangles it is drawn with.</summary>
    public int Triangles { get; private set; }

    public void Build(VoxelWorld w, Site s)
    {
        int SX = w.SX, SZ = w.SZ;
        float reach = Reach / VoxelWorld.Vox, beyond = reach - Math.Min(SX, SZ) / 2f;
        // each valley its own far land: the seed is read off the valley itself
        uint seed = (uint)(s.HTop[0] * 7919 + s.HTop[^1] * 104729 + s.KeepX * 31 + s.KeepZ);
        var nHill = new ValueNoise(seed + 1); var nHill2 = new ValueNoise(seed + 2); var nRidge = new ValueNoise(seed + 3);
        var nRise = new ValueNoise(seed + 4); var nWood = new ValueNoise(seed + 5); var nCrown = new ValueNoise(seed + 6);
        var nDry = new ValueNoise(seed + 7);

        // the lines of the grid: the valley's own every metre along its border, then outward in steps an eighth longer each
        var outs = new List<float>();
        for (float d = 8, step = 8; ; step *= 1.13f) { if (d >= beyond) { outs.Add(beyond); break; } outs.Add(d); d += step; }
        float[] Axis(int size)
        {
            var a = new List<float>();
            for (int i = outs.Count - 1; i >= 0; i--) a.Add(-outs[i]);
            for (int v = 0; v <= size; v += 8) a.Add(v);
            foreach (float d in outs) a.Add(size + d);
            return a.ToArray();
        }
        var ax = Axis(SX); var az = Axis(SZ);
        int nx = ax.Length, nz = az.Length;

        float Ground(float x, float z) => s.HTop[Math.Clamp((int)z, 0, SZ - 1) * SX + Math.Clamp((int)x, 0, SX - 1)];
        static float Smooth(float t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

        // the river out of the valley north and south: its middle goes on the way it was going, then winds; it widens a little
        bool river = s.RiverMid != null;
        float RiverAt(float z, out float half)
        {
            bool north = z <= 0;
            int edge = north ? 0 : SZ - 1, inner = north ? 24 : SZ - 25;
            float slope = Math.Clamp((s.RiverMid[edge] - s.RiverMid[inner]) / 24f, -.6f, .6f), t = north ? -z : z - SZ;
            t = Math.Max(0, t);
            half = s.RiverHalfWidth[edge] + t * .004f;
            return s.RiverMid[edge] + slope * 300 * (1 - MathF.Exp(-t / 300)) + 80 * MathF.Sin(t * .0032f) * Smooth(t / 700);
        }
        // the old road on west out of the valley, as the valley's own map lays it there (MapGen.RoadZ short of the plateau)
        float RoadZ(float x) => SZ * .56f + MathF.Sin(x * .018f) * 18;

        var pos = new Vector3[nx * nz];
        var col = new Color[nx * nz];
        var lane = new Vector2[nx * nz];
        Color grass = new Color("4a5c33").SrgbToLinear(), lush = new Color("3c5a30").SrgbToLinear(), dry = new Color("585d34").SrgbToLinear();
        Color wood = new Color("223322").SrgbToLinear(), rock = new Color("5a5862").SrgbToLinear();
        var forest = new float[nx * nz];
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                float x = ax[i], z = az[j];
                float ox = Math.Max(0, Math.Max(-x, x - SX)), oz = Math.Max(0, Math.Max(-z, z - SZ)), sd = MathF.Sqrt(ox * ox + oz * oz);
                float cx = Math.Clamp(x, 0, SX - 1), cz = Math.Clamp(z, 0, SZ - 1);
                float h = Ground(cx, cz) - 1;
                if (sd > 0)
                {
                    // the border's heights smoothed wider the further out, so the valley's knolls and channels do not run on
                    // in straight lines; hills and long ridges coming up over them; and the rise to the rim
                    float wd = Math.Min(sd * .6f, 420), sum = 0;
                    for (int a = -2; a <= 2; a++)
                        for (int b = -2; b <= 2; b++) sum += Ground(Math.Clamp(cx + a * wd / 2, 0, SX - 1), Math.Clamp(cz + b * wd / 2, 0, SZ - 1));
                    float ridge = 1 - MathF.Abs((float)nRidge.Sample(x * .004, z * .004) * 2 - 1);
                    float hills = ((float)nHill.Sample(x * .006, z * .006) - .5f) * 48 + ((float)nHill2.Sample(x * .02, z * .02) - .5f) * 14 + ridge * ridge * ridge * 34;
                    float rise = 330 * MathF.Pow(Smooth((sd - 900) / (beyond - 900)), 1.4f) * (.5f + (float)nRise.Sample(x * .0011, z * .0011));
                    h = Mathf.Lerp(h, sum / 25 + hills, Smooth(sd / 900)) + rise;
                }
                float riverD = 1e4f, roadD = 1e4f;
                if (river && (z <= 0 || z >= SZ))
                {
                    float rx = RiverAt(z, out float half), d = MathF.Abs(x - rx);
                    riverD = d - half;
                    if (riverD < 0) h = MapGen.RiverLevel;
                    else h = Math.Max(Math.Min(h, MapGen.RiverLevel + 2 + riverD * .42f + Math.Max(0, riverD - 30) * .95f), MapGen.RiverLevel + 1);
                }
                if (x <= 0) roadD = MathF.Abs(z - RoadZ(x)) - 6;
                // the forests: dark heaps of crowns standing up off the ground, not on the river or the road, and only a
                // little way out from the valley's edge
                float f = Smooth(((float)nWood.Sample(x * .0035, z * .0035) - .5f) / .06f) * Smooth((sd - 60) / 220)
                    * Smooth((riverD - 24) / 40) * Smooth((roadD - 10) / 30) * (1 - Smooth((h - 300) / 120));
                forest[j * nx + i] = f;
                h += f * (30 + 16 * (float)nCrown.Sample(x * .03, z * .03));
                pos[j * nx + i] = new Vector3(w.WX(x), w.WY(h), w.WZ(z));
                lane[j * nx + i] = new Vector2(riverD, roadD) * VoxelWorld.Vox;
                float moist = (float)nDry.Sample(x * .005, z * .005);
                col[j * nx + i] = moist < .4f ? dry.Lerp(grass, moist / .4f) : grass.Lerp(lush, Math.Min(1, (moist - .4f) / .4f));
            }

        // the normals from the neighbours on the grid; rock where it is steep or high, the forests dark
        var nrm = new Vector3[nx * nz];
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                var dx = pos[j * nx + Math.Min(i + 1, nx - 1)] - pos[j * nx + Math.Max(i - 1, 0)];
                var dz = pos[Math.Min(j + 1, nz - 1) * nx + i] - pos[Math.Max(j - 1, 0) * nx + i];
                var n = dz.Cross(dx).Normalized();
                nrm[j * nx + i] = n;
                int k = j * nx + i;
                float steep = Smooth((1 - n.Y - .3f) / .35f) + Smooth((pos[k].Y / VoxelWorld.Vox - 250) / 150) * .5f;
                float bare = Math.Min(1, steep) * (1 - forest[k] * .6f);
                col[k] = col[k].Lerp(wood, forest[k]).Lerp(rock, bare);
                // how much of it is meadow, for its grass to dry to straw in patches as the valley's does
                col[k].A = (1 - forest[k]) * (1 - bare);
            }

        // the cells: none inside the valley, none past the rim
        var index = new int[nx * nz];
        Array.Fill(index, -1);
        var vs = new List<Vector3>(); var ns = new List<Vector3>(); var cs = new List<Color>(); var us = new List<Vector2>(); var tris = new List<int>();
        int Vert(int i, int j)
        {
            int k = j * nx + i;
            if (index[k] < 0) { index[k] = vs.Count; vs.Add(pos[k]); ns.Add(nrm[k]); cs.Add(col[k]); us.Add(lane[k]); }
            return index[k];
        }
        float mx = SX / 2f, mz = SZ / 2f;
        for (int j = 0; j < nz - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                if (ax[i] >= 0 && ax[i + 1] <= SX && az[j] >= 0 && az[j + 1] <= SZ) continue;
                float ccx = (ax[i] + ax[i + 1]) / 2 - mx, ccz = (az[j] + az[j + 1]) / 2 - mz;
                if (ccx * ccx + ccz * ccz > reach * reach) continue;
                int a = Vert(i, j), b = Vert(i + 1, j), c = Vert(i + 1, j + 1), d = Vert(i, j + 1);
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vs.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = ns.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = cs.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = us.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = tris.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        Mesh = mesh;
        Triangles = tris.Count / 3;
        MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/far_land.gdshader") };
        CastShadow = ShadowCastingSetting.Off;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        // only a camera with a view to the horizon sees it: someone's eyes, not the diorama
        Visible = GetViewport().GetCamera3D() is { Projection: Camera3D.ProjectionType.Perspective };
    }
}
