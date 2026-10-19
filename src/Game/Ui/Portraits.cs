using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Faces for the interface: a pawn's head and shoulders cast from the cells it is sculpted in, as it is now — with its hat, its
/// beard and its wounds — a little turned and seen from a little above, lit from the upper left. Each look is cast once
/// at each size; a hurt body again after every wound.
/// </summary>
public static class Portraits
{
    static readonly Dictionary<(BodyPlan plan, int id, int rev, int size), ImageTexture> Cache = new();

    /// <summary>The face of a unit at a size in pixels (square).</summary>
    public static ImageTexture Of(Unit u, int size)
    {
        var plan = Bodies.Plan(u.T, u.Look);
        var w = u.Wounds;
        var key = (plan, w == null ? -1 : u.Id, w?.Rev ?? -1, size);
        if (Cache.TryGetValue(key, out var tex)) return tex;
        // a hurt body's older faces are no use any more
        if (w != null) foreach (var k in new List<(BodyPlan, int, int, int)>(Cache.Keys)) if (k.Item2 == u.Id && k.Item4 == size) Cache.Remove(k);
        tex = ImageTexture.CreateFromImage(Cast(w?.Grid ?? plan.Grid, plan, size));
        Cache[key] = tex;
        return tex;
    }

    /// <summary>The face of a kind in one of its looks, unhurt (for the hiring buttons and the like).</summary>
    public static ImageTexture Of(UnitType t, int look, int size)
    {
        var plan = Bodies.Plan(t, look);
        var key = (plan, -1, -1, size);
        if (Cache.TryGetValue(key, out var tex)) return tex;
        tex = ImageTexture.CreateFromImage(Cast(plan.Grid, plan, size));
        Cache[key] = tex;
        return tex;
    }

    const float Yaw = .5f, Pitch = .22f;
    static readonly (int, int)[] Beside = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    static readonly Vector3 Light = new Vector3(-.5f, .75f, .55f).Normalized();

    /// <summary>
    /// Casts a ray for every pixel through the body's trunk and arms (as they stand at rest), in the cells of 3 cm it is
    /// sculpted in, with what it wears and holds there, and paints the first cell it meets: its colour, shaded by the face
    /// it was met on, with a dark line where the shape ends. A cell is there while the half-voxel it lies in is; where a
    /// wound has changed that, the cell takes its colour (blood), or shows what the wound opened.
    /// </summary>
    static Image Cast(PartGrid[] parts, BodyPlan plan, int size)
    {
        const int K = BodyPlan.K;
        var grids = new List<(PartGrid fine, PartGrid now, PartGrid was)>();
        var worn = new List<(PartGrid cells, PartGrid now, int r)>();
        foreach (var p in new[] { BodyPart.Body, BodyPart.Main, BodyPart.Off })
        {
            if (parts[(int)p] is not { Count: > 0 } g || plan.Fine[(int)p] is not { } fine || g.M.Length != plan.Grid[(int)p].M.Length) continue;
            grids.Add((fine, g, plan.Grid[(int)p]));
            foreach (var d in plan.Dress) if (d.On == p) worn.Add((d.Grid, g, d.Sub / BodyPlan.FineSub));
        }
        // what to look at: from the shoulders to the top of the head, a little more for a big body
        float top = plan.Top * BodyPlan.FineSub + K, head = plan.HeadY0 * K, span = Math.Max(10 * K, (top - head) * 1.4f);
        float cy = top - span * .56f;
        var dir = new Vector3(-MathF.Sin(Yaw) * MathF.Cos(Pitch), -MathF.Sin(Pitch), -MathF.Cos(Yaw) * MathF.Cos(Pitch));
        var right = new Vector3(MathF.Cos(Yaw), 0, -MathF.Sin(Yaw));
        var up = right.Cross(dir).Normalized();
        if (up.Y < 0) up = -up;
        int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
        foreach (var (g, _, _) in grids)
        {
            x0 = Math.Min(x0, g.X0); y0 = Math.Min(y0, g.Y0); z0 = Math.Min(z0, g.Z0);
            x1 = Math.Max(x1, g.X0 + g.SX); y1 = Math.Max(y1, g.Y0 + g.SY); z1 = Math.Max(z1, g.Z0 + g.SZ);
        }
        foreach (var (g, _, r) in worn)
        {
            x0 = Math.Min(x0, Down(g.X0, r)); y0 = Math.Min(y0, Down(g.Y0, r)); z0 = Math.Min(z0, Down(g.Z0, r));
            x1 = Math.Max(x1, Down(g.X0 + g.SX, r) + 1); y1 = Math.Max(y1, Down(g.Y0 + g.SY, r) + 1); z1 = Math.Max(z1, Down(g.Z0 + g.SZ, r) + 1);
        }
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var depth = new float[size * size];
        Array.Fill(depth, float.PositiveInfinity);
        if (grids.Count == 0) return img;
        byte M(int x, int y, int z)
        {
            int cx = Down(x, K), cy = Down(y, K), cz = Down(z, K);
            foreach (var (fine, now, was) in grids)
            {
                byte m = fine.Mat(x, y, z);
                if (m == 0 || !now.In(cx, cy, cz)) continue;
                int ci = now.Index(cx, cy, cz);
                byte cm = now.M[ci];
                if (cm == 0) continue;
                if (ReferenceEquals(now, was)) return m;
                if (cm != was.M[ci]) return cm;
                if (now.T[ci] >= (byte)Tissue.Flesh)
                    foreach (var (dx, dy, dz) in Around)
                        if (was.Mat(cx + dx, cy + dy, cz + dz) != 0 && now.Mat(cx + dx, cy + dy, cz + dz) == 0) return cm;
                return m;
            }
            // the dress, finer: a cell here if any of its own in it is there
            foreach (var (cells, now, r) in worn)
            {
                if (now.Mat(cx, cy, cz) == 0) continue;
                for (int dy = 0; dy < r; dy++)
                    for (int dz = 0; dz < r; dz++)
                        for (int dx = 0; dx < r; dx++)
                        {
                            byte m = cells.Mat(x * r + dx, y * r + dy, z * r + dz);
                            if (m != 0) return m;
                        }
            }
            return 0;
        }
        var colour = new Color[size * size];
        // on the head, wherever it is: a man's over his feet, a hound's or a troll's out before its body
        float cz = 0;
        if (grids.Count > 0 && plan.Fine[(int)BodyPart.Body] is { } body)
        {
            double sum = 0; int n = 0;
            for (int y = Math.Max(plan.HeadY0 * K, body.Y0); y < Math.Min(plan.HeadY1 * K, body.Y0 + body.SY); y++)
                for (int z = body.Z0; z < body.Z0 + body.SZ; z++)
                    for (int x = body.X0; x < body.X0 + body.SX; x++)
                        if (body.Mat(x, y, z) != 0) { sum += z + .5; n++; }
            if (n > 0) cz = (float)(sum / n);
        }
        var centre = new Vector3(0, cy, cz);
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float sx = ((i + .5f) / size - .5f) * span, sy = (.5f - (j + .5f) / size) * span;
                var o = centre + right * sx + up * sy - dir * 60 * K;
                if (!March(o, dir, x0, y0, z0, x1, y1, z1, M, out var hit, out var n, out float t, out byte m)) continue;
                depth[j * size + i] = t;
                colour[j * size + i] = Paint(m, hit, n, dir);
            }
        // where the shape ends against the air, or one part stands well in front of another: a dark line
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float d = depth[j * size + i];
                if (float.IsPositiveInfinity(d)) { img.SetPixel(i, j, new Color(0, 0, 0, 0)); continue; }
                bool rim = false, step = false;
                foreach (var (di, dj) in Beside)
                {
                    int ni = i + di, nj = j + dj;
                    float nd = ni < 0 || nj < 0 || ni >= size || nj >= size ? float.PositiveInfinity : depth[nj * size + ni];
                    if (float.IsPositiveInfinity(nd)) rim = true;
                    else if (nd - d > 2.2f * K) step = true;
                }
                var c = colour[j * size + i];
                if (rim) c = c.Darkened(.55f);
                else if (step) c = c.Darkened(.25f);
                img.SetPixel(i, j, c);
            }
        return img;
    }

    static readonly (int, int, int)[] Around = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

    static int Down(int v, int r) => v >= 0 ? v / r : (v - r + 1) / r;

    /// <summary>A voxel's colour as the portrait shows it: lit by its face, a little different from its neighbours, metal glinting.</summary>
    static Color Paint(byte m, Vector3I v, Vector3 n, Vector3 dir)
    {
        float lr = Mat.Lin[m * 3], lg = Mat.Lin[m * 3 + 1], lb = Mat.Lin[m * 3 + 2];
        float jitter = 1 + ((float)Hash.H3(v.X * 7 + v.Y, v.Z, 31) - .5f) * Mat.Variance[m];
        float diffuse = Math.Max(0, n.Dot(Light));
        float k = (.52f + .62f * diffuse) * jitter;
        float glint = 0;
        if (Mat.Metal[m])
        {
            var h = (Light - dir).Normalized();
            glint = MathF.Pow(Math.Max(0, n.Dot(h)), 18) * .55f;
        }
        static float Srgb(float c) => c <= .0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - .055f;
        return new Color(Math.Min(1, Srgb(lr * k) + glint), Math.Min(1, Srgb(lg * k) + glint), Math.Min(1, Srgb(lb * k) + glint), 1);
    }

    /// <summary>Steps a ray through the grid cell by cell to the first solid one; the face it came in by is its normal.</summary>
    static bool March(Vector3 o, Vector3 d, int x0, int y0, int z0, int x1, int y1, int z1, Func<int, int, int, byte> mat,
        out Vector3I hit, out Vector3 normal, out float dist, out byte m)
    {
        hit = default; normal = default; dist = 0; m = 0;
        // into the box first
        float tmin = 0, tmax = float.PositiveInfinity;
        for (int a = 0; a < 3; a++)
        {
            float oa = o[a], da = d[a], lo = a == 0 ? x0 : a == 1 ? y0 : z0, hi = a == 0 ? x1 : a == 1 ? y1 : z1;
            if (MathF.Abs(da) < 1e-6f) { if (oa < lo || oa > hi) return false; continue; }
            float t1 = (lo - oa) / da, t2 = (hi - oa) / da;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = Math.Max(tmin, t1); tmax = Math.Min(tmax, t2);
            if (tmin > tmax) return false;
        }
        var p = o + d * (tmin + 1e-3f);
        int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y), z = (int)MathF.Floor(p.Z);
        int sx = Math.Sign(d.X), sy = Math.Sign(d.Y), sz = Math.Sign(d.Z);
        float Next(float pos, int cell, int s, float dd) => s == 0 ? float.PositiveInfinity : ((s > 0 ? cell + 1 : cell) - pos) / dd;
        float tx = Next(p.X, x, sx, d.X), ty = Next(p.Y, y, sy, d.Y), tz = Next(p.Z, z, sz, d.Z);
        float dx = sx == 0 ? float.PositiveInfinity : MathF.Abs(1 / d.X), dy = sy == 0 ? float.PositiveInfinity : MathF.Abs(1 / d.Y), dz = sz == 0 ? float.PositiveInfinity : MathF.Abs(1 / d.Z);
        int axis = -1;
        float t = tmin;
        for (int step = 0; step < 400 * BodyPlan.K; step++)
        {
            if (x < x0 - 1 || y < y0 - 1 || z < z0 - 1 || x > x1 || y > y1 || z > z1) return false;
            byte here = mat(x, y, z);
            if (here != 0)
            {
                hit = new Vector3I(x, y, z); m = here; dist = t;
                normal = axis switch { 0 => new Vector3(-sx, 0, 0), 1 => new Vector3(0, -sy, 0), 2 => new Vector3(0, 0, -sz), _ => -d };
                return true;
            }
            if (tx < ty && tx < tz) { x += sx; t = tmin + tx; tx += dx; axis = 0; }
            else if (ty < tz) { y += sy; t = tmin + ty; ty += dy; axis = 1; }
            else { z += sz; t = tmin + tz; tz += dz; axis = 2; }
        }
        return false;
    }
}

/// <summary>A unit's face in a frame of the interface: the portrait, and over it whatever the frame wants to say.</summary>
public partial class Face : Control
{
    public Unit Unit;
    public int Res = 64;

    public Face() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        if (Unit == null) return;
        var tex = Portraits.Of(Unit, Res);
        DrawTextureRect(tex, new Rect2(Vector2.Zero, Size), false, Unit.Dead ? new Color(.45f, .42f, .42f) : Colors.White);
    }
}
