using System;

namespace VoxelTower.Core;

/// <summary>
/// The map of the browser prototype: a lowland with an old road, a cliff plateau with the citadel,
/// and a starter wall built with the same brushes the player has.
/// </summary>
public static class WorldGen
{
    public const int SizeX = 320, SizeY = 160, SizeZ = 224;

    /// <param name="starterFort">The prototype's ready-made wall, towers and stairs. The game starts without them:
    /// the player draws every building. Tests keep them to compare against the prototype.</param>
    public static Site Generate(VoxelWorld w, bool starterFort = true)
    {
        var site = new Site { HTop = new int[w.SX * w.SZ] };
        Terrain(w, site);
        foreach (var (x, z, r) in new[] { (58, 40, 7), (96, 150, 9), (150, 64, 6), (40, 190, 8), (178, 124, 7), (118, 24, 6) })
            Flora.Boulder(w, x, z, r, site.HTop[z * w.SX + x]);

        var R = new Mulberry32(5);
        var trees = new[] { (30, 30), (46, 64), (80, 92), (22, 120), (70, 210), (100, 40), (132, 150), (160, 30), (188, 92), (60, 130), (112, 200), (172, 166), (256, 26), (300, 38), (302, 196), (262, 204), (312, 150) };
        for (int n = 0; n < trees.Length; n++)
        {
            var (x, z) = trees[n];
            if (x < 150 && Math.Abs(z - Site.PathZ(x)) < 10) continue;
            if (n % 5 == 3) Flora.DeadTree(w, x, z);
            else Flora.Cypress(w, x, z, 22 + (int)Math.Floor(R.Next() * 14));
        }

        site.KeepBase = Keep(w, site);

        if (starterFort)
        {
            int wx = site.WallX;
            Builder.ApplyFree(w, Brushes.Wall(w, site, wx, 22, wx, 166, Brushes.Rampart), raw: true);
            var t1 = Brushes.Tower(w, site, wx + 2, 170, 16, 60);
            Builder.ApplyFree(w, t1, raw: true);
            Brazier(w, site, wx + 2, t1.MetaPlat, 170);
            var t2 = Brushes.Tower(w, site, wx + 2, 18, 14, 56);
            Builder.ApplyFree(w, t2, raw: true);
            Brazier(w, site, wx + 2, t2.MetaPlat, 18);
            int walk = w.SurfAt(wx + 8, 70);
            Builder.ApplyFree(w, Brushes.Stairs(w, site, wx + 10, 70, walk, wx + 60, 70, Mat.Stone), raw: true);
        }
        else
        {
            // two braziers flank the citadel gate
            int gx = site.KeepX - site.KeepR - 7;
            foreach (int dz in new[] { -10, 10 }) Brazier(w, site, gx, w.SurfAt(gx, site.KeepZ + dz), site.KeepZ + dz);
        }
        site.Torches.Add((site.KeepX - site.KeepR - 3, site.KeepBase + 22, site.KeepZ));
        return site;
    }

    static void Put(VoxelWorld w, int x, int y, int z, byte m)
    {
        if (!w.InBounds(x, y, z)) return;
        byte cur = w.Get(x, y, z);
        if (cur == Mat.Air || cur == Mat.Tuft) w.SetRaw(x, y, z, m);
    }

    static void Terrain(VoxelWorld w, Site site)
    {
        var n1 = new ValueNoise(11); var n2 = new ValueNoise(23); var n3 = new ValueNoise(37);
        var R = new Mulberry32(77);
        var edgeX = new int[w.SZ];
        for (int z = 0; z < w.SZ; z++) edgeX[z] = 206 + Brushes.JsRound((n3.Sample(z * .045, 3.3) - .5) * 24);
        for (int z = 0; z < w.SZ; z++)
            for (int x = 0; x < w.SX; x++)
            {
                double f = n1.Sample(x * .03, z * .03) * .65 + n2.Sample(x * .08, z * .08) * .35;
                int low = 24 + Brushes.JsRound((f - .5) * 6), edge = edgeX[z];
                int plat = 72 + Brushes.JsRound((n2.Sample(x * .05 + 40, z * .05 + 9) - .5) * 4);
                bool plateau = x >= edge, ramp = false;
                int top = plateau ? plat : low;
                if (!plateau && z >= 176 && z <= 213 && x >= 128)
                {
                    double t = Math.Clamp((x - 128) / (double)(edge - 128), 0, 1);
                    int r = Brushes.JsRound(low + (plat - low) * t);
                    if (r > top) { top = r; ramp = true; }
                }
                bool onPath = !plateau && !ramp && x <= 140 && Math.Abs(z - Site.PathZ(x)) < 5.5;
                bool rim = plateau && x - edge < 5;
                for (int y = 0; y < top; y++)
                {
                    byte m;
                    if (y < 3) m = Mat.Bed;
                    else if (plateau) m = (y < top - 10 || (rim && y < top - 1)) ? Mat.Rock : Mat.Soil;
                    else m = y < 5 ? Mat.Rock : Mat.Soil;
                    if (y == top - 1) m = rim ? Mat.Moss : (ramp || onPath) ? Mat.Path : Mat.Grass;
                    w.SetRaw(x, y, z, m);
                }
                site.HTop[z * w.SX + x] = top;
                if (!onPath && !ramp && !rim && R.Next() < .07)
                {
                    w.SetRaw(x, top, z, Mat.Tuft);
                    if (R.Next() < .35) w.SetRaw(x, top + 1, z, Mat.Tuft);
                }
            }
    }

    /// <summary>The citadel: a round brick keep with arrow slits that glow, a plank door and a slate cone.</summary>
    internal static int Keep(VoxelWorld w, Site s)
    {
        int KX = s.KeepX, KZ = s.KeepZ, KR = s.KeepR, bse = 0;
        for (int z = KZ - KR; z <= KZ + KR; z++)
            for (int x = KX - KR; x <= KX + KR; x++)
                if (Brushes.Dist(x - KX + .5, z - KZ + .5) <= KR) bse = Math.Max(bse, s.HTop[z * w.SX + x]);
        const int H = 54;
        for (int z = KZ - KR; z <= KZ + KR; z++)
            for (int x = KX - KR; x <= KX + KR; x++)
            {
                if (x < 0 || x >= w.SX || z < 0 || z >= w.SZ) continue;
                double d = Brushes.Dist(x - KX + .5, z - KZ + .5);
                if (d > KR) continue;
                double ang = Math.Atan2(z - KZ, x - KX);
                int al = Brushes.JsRound((ang + Math.PI) * KR), g = s.HTop[z * w.SX + x];
                bool door = Math.Abs(z - KZ + .5) < 6 && x < KX && d > KR - 5;
                for (int y = Math.Max(0, g - 2); y < bse + H; y++)
                {
                    int py = y - bse; byte m;
                    if (d > KR - 4)
                    {
                        if (py >= H - 8) m = ((int)Math.Floor(al / 7.0) & 1) == 0 ? Mat.Keep : Mat.Air;
                        else
                        {
                            int pq = py + 64, course = pq / 5, row = pq % 5, off = (course & 1) * 5;
                            bool joint = (al + off) % 10 == 0;
                            m = (row == 4 || joint) ? (d > KR - 1 ? Mat.Air : Mat.KeepDark) : Mat.Keep;
                        }
                        double slit = Brushes.JsRound(ang / (Math.PI / 3)) * (Math.PI / 3);
                        if (Math.Abs(AngDiff(ang, slit)) * KR < 1.3 && ((py >= 16 && py < 24) || (py >= 34 && py < 40)) && !door)
                            m = d > KR - 2 ? Mat.Air : (d > KR - 3 ? Mat.Ember : Mat.Keep);
                        if (door && py >= 0 && py < 20)
                            m = d > KR - 2 ? Mat.Air : ((x * 3 + y) % 5 == 0 ? Mat.Iron : Mat.Plank);
                    }
                    else m = py < H - 9 ? Mat.KeepDark : (py == H - 9 ? Mat.Wood : Mat.Air);
                    if (m != 0) w.SetRaw(x, y, z, m);
                }
            }
        for (int y = bse + H - 2; y < bse + H + 26 && y < w.SY; y++)
        {
            double t = (y - (bse + H - 2)) / 26.0, r = (KR - 3) * (1 - t);
            for (int z = (int)Math.Floor(KZ - r - 1); z <= KZ + r + 1; z++)
                for (int x = (int)Math.Floor(KX - r - 1); x <= KX + r + 1; x++)
                {
                    double d = Brushes.Dist(x - KX + .5, z - KZ + .5);
                    if (d < r && d > r - 3.2) w.SetRaw(x, y, z, Mat.Roof);
                }
        }
        for (int y = bse + H + 23; y < Math.Min(w.SY, bse + H + 29); y++) w.SetRaw(KX, y, KZ, Mat.Iron);
        return bse;
    }

    internal static void Brazier(VoxelWorld w, Site s, int x, int y, int z)
    {
        for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dz) > 3) continue;
                Put(w, x + dx, y, z + dz, Mat.Iron);
                if (Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1) Put(w, x + dx, y + 1, z + dz, Mat.Ember);
            }
        Put(w, x, y + 2, z, Mat.Ember);
        s.Torches.Add((x + .5f, y + 4, z + .5f));
    }

    static double AngDiff(double a, double b)
    {
        double d = b - a;
        while (d > Math.PI) d -= Math.PI * 2;
        while (d < -Math.PI) d += Math.PI * 2;
        return d;
    }
}
