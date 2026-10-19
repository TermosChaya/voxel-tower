using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The land beyond the valley, round it out to some seven hundred metres, seen only from someone's eyes (a camera with
/// a view to the horizon); over the diorama the valley stays a block on its sea of mist. Its inner edge takes the
/// valley's own ground along the border a voxel lower, so the land goes on without a step and the block's earthen
/// sides are hidden; outward the border's heights smooth into rolling country with woods on it, and it rises into hills
/// towards the rim and into the haze. Nearer the valley, out to some hundred and forty metres, the woods are trees, each
/// standing by itself as the valley's own do and as tall, eight to twelve metres: spruces and pines, oaks and birches,
/// blocky as the voxels they are seen beside, out of a dark mass of the lower crowns; further off only the mass of the
/// crowns, as high as the trees. The old road goes on west out of the valley, the river north and south out of it in a
/// channel of its own. One mesh in one draw call, with no shadows, lit by the same sun as the valley and going into the
/// colour of the horizon the further off it is (shaders/far_land.gdshader). Some two hundred and fifty thousand
/// triangles; it is built once with the valley, from the valley's own heights.
/// </summary>
public partial class FarLand : MeshInstance3D
{
    /// <summary>How far the far land reaches from the middle of the valley, in scene units (two metres each).</summary>
    public const float Reach = 360;
    /// <summary>How far out from the valley's edge the woods are trees one by one (voxels): beyond, the mass of their crowns.</summary>
    const float TreeReach = 1100;
    /// <summary>The triangles it is drawn with, and the trees standing in it.</summary>
    public int Triangles { get; private set; }
    public int Trees { get; private set; }

    public void Build(VoxelWorld w, Site s)
    {
        ulong began = Time.GetTicksMsec();
        int SX = w.SX, SZ = w.SZ;
        float reach = Reach / VoxelWorld.Vox, beyond = reach - Math.Min(SX, SZ) / 2f;
        // each valley its own far land: the seed is read off the valley itself
        uint seed = (uint)(s.HTop[0] * 7919 + s.HTop[^1] * 104729 + s.KeepX * 31 + s.KeepZ);
        var nHill = new ValueNoise(seed + 1); var nHill2 = new ValueNoise(seed + 2); var nRidge = new ValueNoise(seed + 3);
        var nRise = new ValueNoise(seed + 4); var nWood = new ValueNoise(seed + 5); var nCrown = new ValueNoise(seed + 6);
        var nDry = new ValueNoise(seed + 7); var nKind = new ValueNoise(seed + 8);
        int salt = (int)(seed % 9973) * 16;

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

        // the height of the ground at a spot (voxels), without the woods; how far it is from the river's edge and the road's,
        // and from the valley's edge
        float LandAt(float x, float z, out float riverD, out float roadD, out float sd)
        {
            float ox = Math.Max(0, Math.Max(-x, x - SX)), oz = Math.Max(0, Math.Max(-z, z - SZ));
            sd = MathF.Sqrt(ox * ox + oz * oz);
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
            riverD = 1e4f; roadD = 1e4f;
            if (river && (z <= 0 || z >= SZ))
            {
                float rx = RiverAt(z, out float half), d = MathF.Abs(x - rx);
                riverD = d - half;
                if (riverD < 0) h = MapGen.RiverLevel;
                else h = Math.Max(Math.Min(h, MapGen.RiverLevel + 2 + riverD * .42f + Math.Max(0, riverD - 30) * .95f), MapGen.RiverLevel + 1);
            }
            if (x <= 0) roadD = MathF.Abs(z - RoadZ(x)) - 6;
            return h;
        }
        // how much of a spot is wood: not on the river or the road, nor high up the rim, and only a little way out from
        // the valley's edge
        float WoodAt(float x, float z, float h, float sd, float riverD, float roadD) =>
            Smooth(((float)nWood.Sample(x * .0035, z * .0035) - .5f) / .06f) * Smooth((sd - 60) / 220)
            * Smooth((riverD - 24) / 40) * Smooth((roadD - 10) / 30) * (1 - Smooth((h - 300) / 120));

        var pos = new Vector3[nx * nz];
        var col = new Color[nx * nz];
        var lane = new Vector2[nx * nz];
        Color grass = new Color("4a5c33").SrgbToLinear(), lush = new Color("3c5a30").SrgbToLinear(), dry = new Color("585d34").SrgbToLinear();
        Color wood = new Color("223322").SrgbToLinear(), rock = new Color("5a5862").SrgbToLinear();
        var forest = new float[nx * nz];
        // how much of the ground is the mass of the crowns, for the shader to dome crown by crown
        var canopy = new float[nx * nz];
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                float x = ax[i], z = az[j];
                float h = LandAt(x, z, out float riverD, out float roadD, out float sd);
                float f = WoodAt(x, z, h, sd, riverD, roadD);
                forest[j * nx + i] = f;
                // the mass of the crowns, where the trees one by one give out, as high as they are, eight to eleven
                // metres; nearer the valley only the dark ground under the trees
                float rise = Smooth((sd - TreeReach + 400) / 500);
                h += f * rise * (62 + 26 * (float)nCrown.Sample(x * .03, z * .03));
                canopy[j * nx + i] = f * rise;
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
                // the steep side of a wood is no crag but its trees, the shade under their crowns going in among the trunks
                float steep = Smooth((1 - n.Y - .3f) / .35f) + Smooth((pos[k].Y / VoxelWorld.Vox - 250) / 150) * .5f;
                float bare = Math.Min(1, steep) * (1 - Math.Min(1, forest[k] * 1.6f));
                float under = forest[k] * Smooth((1 - n.Y - .2f) / .45f);
                col[k] = col[k].Lerp(wood, forest[k]).Lerp(rock, bare).Lerp(wood * .5f, under);
                // how much of it is meadow, for its grass to dry to straw in patches as the valley's does
                col[k].A = (1 - forest[k]) * (1 - bare);
            }

        // the cells: none inside the valley, none past the rim
        var index = new int[nx * nz];
        Array.Fill(index, -1);
        var vs = new List<Vector3>(); var ns = new List<Vector3>(); var cs = new List<Color>(); var us = new List<Vector2>(); var woods = new List<Vector2>(); var tris = new List<int>();
        int Vert(int i, int j)
        {
            int k = j * nx + i;
            if (index[k] < 0) { index[k] = vs.Count; vs.Add(pos[k]); ns.Add(nrm[k]); cs.Add(col[k]); us.Add(lane[k]); woods.Add(new Vector2(canopy[k], 0)); }
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

        // a block of a tree (voxels), turned about the upright, flat on each face, darker at its foot as the shade gathers
        // under a crown; marked for the shader as a tree's and no meadow, on no road nor river
        var noLane = new Vector2(1e4f, 1e4f) * VoxelWorld.Vox;
        void Block(float x, float y0, float z, float hx, float hy, float hz, float yaw, Color c)
        {
            float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
            Vector3 Corner(float sx, float sy, float sz)
            {
                float lx = sx * hx, lz = sz * hz;
                return new Vector3(w.WX(x + lx * cos - lz * sin), w.WY(y0 + hy + sy * hy), w.WZ(z + lx * sin + lz * cos));
            }
            Vector3 Turn(Vector3 n) => new(n.X * cos - n.Z * sin, n.Y, n.X * sin + n.Z * cos);
            Color foot = c * .62f; foot.A = 0; c.A = 0;
            void Face(Vector3 n, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, bool low0, bool low1, bool low2, bool low3)
            {
                int b = vs.Count;
                vs.Add(p0); vs.Add(p1); vs.Add(p2); vs.Add(p3);
                var tn = Turn(n);
                for (int q = 0; q < 4; q++) { ns.Add(tn); us.Add(noLane); woods.Add(new Vector2(0, 1)); }
                cs.Add(low0 ? foot : c); cs.Add(low1 ? foot : c); cs.Add(low2 ? foot : c); cs.Add(low3 ? foot : c);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            // the corners counter-clockwise seen from outside, as the grid's own triangles wind
            Face(Vector3.Up, Corner(-1, 1, -1), Corner(1, 1, -1), Corner(1, 1, 1), Corner(-1, 1, 1), false, false, false, false);
            Face(Vector3.Down, Corner(-1, -1, -1), Corner(-1, -1, 1), Corner(1, -1, 1), Corner(1, -1, -1), true, true, true, true);
            Face(Vector3.Right, Corner(1, -1, -1), Corner(1, -1, 1), Corner(1, 1, 1), Corner(1, 1, -1), true, true, false, false);
            Face(Vector3.Left, Corner(-1, -1, -1), Corner(-1, 1, -1), Corner(-1, 1, 1), Corner(-1, -1, 1), true, false, false, true);
            Face(Vector3.Back, Corner(-1, -1, 1), Corner(-1, 1, 1), Corner(1, 1, 1), Corner(1, -1, 1), true, false, false, true);
            Face(Vector3.Forward, Corner(-1, -1, -1), Corner(1, -1, -1), Corner(1, 1, -1), Corner(-1, 1, -1), true, true, false, false);
        }

        // the trees nearer the valley, on a grid of four and a half metres each a little off its place: where the wood is
        // thick nearly every place holds one, at its edge fewer, and now and then one stands alone in the meadow; the
        // kinds of the valley in its proportions, conifers in some woods and broadleaves in others, mixed between
        Color needle = new Color("263828").SrgbToLinear(), needleLight = new Color("37522f").SrgbToLinear();
        Color foliage = new Color("415426").SrgbToLinear(), foliageLight = new Color("5a6c2d").SrgbToLinear();
        Color bark = new Color("40362e").SrgbToLinear(), pineBark = new Color("7a4b30").SrgbToLinear(), birch = new Color("cfc8b8").SrgbToLinear();
        const float Step = 36;
        int trees = 0;
        for (float gz = -TreeReach; gz < SZ + TreeReach; gz += Step)
            for (float gx = -TreeReach; gx < SX + TreeReach; gx += Step)
            {
                int ix = (int)MathF.Floor(gx / Step), iz = (int)MathF.Floor(gz / Step);
                float R(int k) => (float)Hash.H3(ix, iz, salt + k);
                float x = gx + Step * (.1f + .8f * R(1)), z = gz + Step * (.1f + .8f * R(2));
                float y = LandAt(x, z, out float riverD, out float roadD, out float sd);
                if (sd < 40 || sd > TreeReach || riverD < 12 || roadD < 6) continue;
                float f = WoodAt(x, z, y, sd, riverD, roadD) * (1 - Smooth((sd - TreeReach + 300) / 300));
                if (R(3) > f * .94f + .025f * (1 - Smooth((sd - 200) / 500))) continue;
                trees++;
                float size = R(4), yaw = R(5) * MathF.Tau, tint = .82f + .36f * R(6), kind = (float)nKind.Sample(x * .004, z * .004), r = R(7);
                y -= 3;
                int type = kind < .45f ? (r < .6f ? 0 : 1) : kind > .58f ? (r < .55f ? 2 : 3) : r < .3f ? 0 : r < .55f ? 2 : r < .75f ? 1 : 3;
                Color leaf;
                switch (type)
                {
                    case 0:
                    {
                        // a spruce: a cone in tiers, each turned an eighth from the one under it
                        float H = 56 + 40 * size;
                        leaf = needle.Lerp(needleLight, R(8) * .5f) * tint;
                        Block(x, y, z, 2, .1f * H + 2, 2, yaw, bark);
                        for (int t = 0; t < 4; t++)
                        {
                            float hw = .25f * H * (1 - t * .23f) + 2;
                            Block(x, y + .16f * H + t * .19f * H, z, hw, .14f * H, hw, yaw + t * .785f, leaf * (1 + t * .05f));
                        }
                        Block(x, y + .9f * H, z, 3, .06f * H, 3, yaw, leaf * 1.15f);
                        break;
                    }
                    case 1:
                    {
                        // a pine: a tall bare trunk, its crown flat heaps at the top
                        float H = 68 + 36 * size;
                        leaf = needleLight.Lerp(needle, R(8) * .6f) * tint;
                        Block(x, y, z, 2.4f, .36f * H + 2, 2.4f, yaw, pineBark);
                        float ox = MathF.Cos(yaw) * .07f * H, oz = MathF.Sin(yaw) * .07f * H;
                        Block(x + ox, y + .6f * H, z + oz, .2f * H, .1f * H, .17f * H, yaw, leaf);
                        Block(x - ox * .6f, y + .78f * H, z - oz * .6f, .16f * H, .1f * H, .14f * H, yaw + .5f, leaf * 1.1f);
                        Block(x, y + .92f * H, z, .08f * H, .05f * H, .08f * H, yaw + 1.1f, leaf * 1.2f);
                        break;
                    }
                    case 2:
                    {
                        // an oak: a stout trunk under a broad crown heaped up of lobes round its middle
                        float H = 60 + 28 * size;
                        leaf = foliage.Lerp(foliageLight, R(8) * .5f) * tint;
                        Block(x, y, z, 3, .22f * H + 2, 3, yaw, bark);
                        Block(x, y + .34f * H, z, .24f * H, .25f * H, .22f * H, yaw, leaf);
                        for (int l = 0; l < 4; l++)
                        {
                            float a = yaw + l * 1.571f + .4f * R(10 + l), out0 = (.17f + .07f * R(14 + l)) * H, lw = (.13f + .05f * R(18 + l)) * H;
                            Block(x + MathF.Cos(a) * out0, y + (.36f + .14f * R(22 + l)) * H, z + MathF.Sin(a) * out0, lw, lw * .85f, lw, a, leaf * (.88f + .1f * l));
                        }
                        Block(x, y + .76f * H, z, .15f * H, .12f * H, .15f * H, yaw + .4f, leaf * 1.12f);
                        break;
                    }
                    default:
                    {
                        // a birch: a white trunk, a light narrow crown hanging in tufts down it
                        float H = 46 + 26 * size;
                        leaf = foliageLight.Lerp(foliage, R(8) * .4f) * tint;
                        Block(x, y, z, 1.6f, .3f * H + 2, 1.6f, yaw, birch * .8f);
                        Block(x, y + .4f * H, z, .13f * H, .26f * H, .12f * H, yaw, leaf);
                        for (int l = 0; l < 3; l++)
                        {
                            float a = yaw + l * 2.094f + .5f * R(10 + l), out0 = .1f * H;
                            Block(x + MathF.Cos(a) * out0, y + (.3f + .2f * l) * H, z + MathF.Sin(a) * out0, .1f * H, .12f * H, .1f * H, a, leaf * (.9f + .08f * l));
                        }
                        break;
                    }
                }
            }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vs.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = ns.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = cs.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = us.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = woods.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = tris.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        Mesh = mesh;
        Triangles = tris.Count / 3;
        Trees = trees;
        GD.Print($"[far land] {trees} trees, {Triangles} triangles, built in {Time.GetTicksMsec() - began} ms");
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
