using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The big world map's drawing, without the engine (--worldview; HexMapLayout.cs, DemoHexes.cs, HexTiles.cs, HexFar.cs):
/// where the hexes lie and which one a point is in, the fog's grid within its hexes, the pieces the map is drawn in
/// covering every hex that reaches into them; the made-up world the same for the same seed, every kind of thing on it,
/// its zones walled by mountains with passes between them so one may walk from our citadel to every zone; a piece made
/// and meshed in good time, the same each time, its ground at the heights of its hexes and its peaks where the
/// mountains are; the far view of the whole map not too heavy. --worldview-dump size seed file: the map from straight
/// above, a picture.
/// </summary>
static class WorldViewBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        // ---- the layout: a hex's middle is in that hex, any point is in the hex whose middle is nearest
        var lay = new HexMapLayout(144, 144);
        var rnd = new Random(5);
        int wrongMid = 0, wrongNear = 0;
        for (int row = 0; row < lay.Rows; row += 7)
            for (int col = 0; col < lay.Cols; col += 5)
            {
                var (x, z) = lay.Center(col, row);
                if (lay.HexAt(x, z) != (col, row)) wrongMid++;
            }
        for (int i = 0; i < 20000; i++)
        {
            float x = (float)(rnd.NextDouble() * lay.SX), z = (float)(rnd.NextDouble() * lay.SZ);
            var (hc, hr) = lay.HexAt(x, z);
            var (hx, hz) = lay.Center(hc, hr);
            float d0 = (hx - x) * (hx - x) + (hz - z) * (hz - z);
            for (int s = 0; s < 6; s++)
            {
                var (nc, nr) = HexMapLayout.Neighbour(hc, hr, s);
                var (nx, nz) = lay.Center(nc, nr);
                if ((nx - x) * (nx - x) + (nz - z) * (nz - z) < d0 - 1e-3f) { wrongNear++; break; }
            }
        }
        // the neighbours across each side are where the side's way points, and back again across the other side
        int wrongSides = 0;
        foreach (var (col, row) in new[] { (10, 10), (11, 11), (0, 1), (143, 142) })
            for (int s = 0; s < 6; s++)
            {
                var (nc, nr) = HexMapLayout.Neighbour(col, row, s);
                var (ax, az) = lay.Center(col, row); var (bx, bz) = lay.Center(nc, nr); var (tx, tz) = HexMapLayout.Toward(s);
                if (MathF.Abs(bx - ax - tx) > .01f || MathF.Abs(bz - az - tz) > .01f || HexMapLayout.Neighbour(nc, nr, (s + 3) % 6) != (col, row)) wrongSides++;
                if (HexMapLayout.SideToward(tx, tz) != s) wrongSides++;
            }
        check("the world map's hexes: a hex's middle is in it, any point in the hex whose middle is nearest, the sides' neighbours where they should be",
            wrongMid == 0 && wrongNear == 0 && wrongSides == 0, $"XL {lay.SX}×{lay.SZ} voxels, {lay.TilesX}×{lay.TilesZ} pieces; middles wrong {wrongMid}, points wrong {wrongNear} of 20000, sides wrong {wrongSides}");

        // ---- the fog's grid: each cell of it within the hex it says, read straight from a point's place
        int fogWrong = 0, fogCells = 0;
        for (int j = 0; j < lay.FogH; j++)
            for (int i = 0; i < lay.FogW; i++)
            {
                var (c, r) = lay.FogHex(i, j);
                if (c < 0) continue;
                fogCells++;
                // the middle of the fog cell is in that hex
                float x = (i + .5f) * HexMapLayout.HalfW, z = (j + .5f) * HexMapLayout.RowStep;
                if (lay.HexAt(x, z) != (c, r)) fogWrong++;
            }
        check("the fog's grid: every cell of it lies in the hex it stands for", fogWrong == 0 && fogCells == lay.Cols * lay.Rows * 2,
            $"{lay.FogW}×{lay.FogH} cells, {fogCells} on the field (two to a hex: {lay.Cols * lay.Rows * 2}), wrong {fogWrong}");

        // ---- the pieces: every hex that reaches into a piece is among those it gathers
        var list = new List<(int, int)>();
        int missed = 0;
        for (int t = 0; t < 40; t++)
        {
            int tx = rnd.Next(lay.TilesX), tz = rnd.Next(lay.TilesZ);
            float x0 = tx * HexMapLayout.Tile, z0 = tz * HexMapLayout.Tile, x1 = x0 + HexMapLayout.Tile, z1 = z0 + HexMapLayout.Tile;
            lay.HexesIn(x0, z0, x1, z1, list);
            var set = new HashSet<(int, int)>(list);
            for (float z = z0; z < z1; z += 3)
                for (float x = x0; x < x1; x += 3)
                {
                    var h = lay.HexAt(x, z);
                    if (lay.OnField(h.Col, h.Row) && !set.Contains(h)) missed++;
                }
        }
        check("a piece of the map gathers every hex that reaches into it", missed == 0, $"hexes missed {missed} over 40 pieces");

        // ---- the made-up world: the same for the same seed, another for another; every kind of thing on it
        foreach (var (size, side) in new[] { ("S", 36), ("M", 72), ("XL", 144) })
        {
            var sw = Stopwatch.StartNew();
            var a = new DemoHexes(side, side, 11);
            double ms = sw.Elapsed.TotalMilliseconds;
            var b = new DemoHexes(side, side, 11);
            var other = new DemoHexes(side, side, 12);
            var kinds = new HashSet<HexIcon>();
            int mountains = 0, water = 0, forest = 0, roads = 0, rivers = 0;
            for (int r = 0; r < side; r++)
                for (int c = 0; c < side; c++)
                {
                    var l = a.Look(c, r);
                    kinds.Add(l.Icon);
                    if (l.Relief == HexRelief.Mountain) mountains++;
                    if (l.Ground is HexGround.Water or HexGround.DeepWater) water++;
                    if (l.Relief == HexRelief.Forest) forest++;
                    if (l.Roads != 0) roads++;
                    if (l.Rivers != 0) rivers++;
                }
            int missing = Enum.GetValues<HexIcon>().Count(k => !kinds.Contains(k));
            // one may walk from our citadel to every zone's middle, over open land and through the passes
            var reach = Walkable(a, a.Home.Col, a.Home.Row);
            int cut = a.ZoneMiddles.Count(m => !reach.Contains(m));
            check($"the made-up world {size}: the same for the same seed and another for another; zones walled by mountains, passes between them, roads and rivers; {(side >= 72 ? "every kind of thing on it" : "its things")}",
                a.Fingerprint() == b.Fingerprint() && a.Fingerprint() != other.Fingerprint() && cut == 0 && mountains > side * side / 12 && roads > side && rivers > 3
                && (side < 72 || missing == 0) && ms < 1500,
                $"{side}×{side}: made in {ms:0} ms; {a.ZoneMiddles.Count} zones, {a.Towns.Count} towns of the horde; mountains {mountains}, water {water}, woods {forest}, " +
                $"with roads {roads}, with rivers {rivers}; kinds of things missing {missing}; zones cut off from our citadel {cut}");
        }

        // ---- a piece made and meshed: in good time, the same each time, at its hexes' heights, peaks where mountains are
        var world = new DemoHexes(72, 72, 11) { RevealAll = true };
        var lm = new HexMapLayout(72, 72);
        // the piece with our citadel in it, one with a mountain's peak in it, one in the middle of the map
        var (hx0, hz0) = lm.Center(world.Home.Col, world.Home.Row);
        (int, int) TileOf(float x, float z) => ((int)(x / HexMapLayout.Tile), (int)(z / HexMapLayout.Tile));
        var peakTile = TileOf(hx0, hz0);
        for (int r = 4; r < lm.Rows - 4 && peakTile == TileOf(hx0, hz0); r++)
            for (int c = 4; c < lm.Cols - 4; c++)
                if (world.Look(c, r).Relief == HexRelief.Mountain)
                {
                    var (mx, mz) = lm.Center(c, r);
                    int lx = (int)mx % HexMapLayout.Tile, lz = (int)mz % HexMapLayout.Tile;
                    if (lx > 30 && lz > 30 && lx < HexMapLayout.Tile - 30 && lz < HexMapLayout.Tile - 30) { peakTile = TileOf(mx, mz); break; }
                }
        var pieces = new[] { TileOf(hx0, hz0), peakTile, (lm.TilesX / 2, lm.TilesZ / 2) };
        double make = 0, mesh = 0; int verts = 0, waterVerts = 0;
        int wrongH = 0, peaks = 0, lowPeaks = 0, citadel = 0, differ = 0;
        var near = new List<(int Col, int Row)>();
        foreach (var (tx, tz) in pieces)
        {
            var sw = Stopwatch.StartNew();
            var w = HexTiles.Build(world, lm, tx, tz);
            make += sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var (solid, wat) = HexTiles.Mesh(w, tx, tz);
            mesh += sw.Elapsed.TotalMilliseconds;
            verts += solid?.Vertices ?? 0; waterVerts += wat?.Vertices ?? 0;
            // made again: the same
            var again = HexTiles.Build(world, lm, tx, tz);
            for (int y = 0; y < HexTiles.SY; y++)
                for (int z = 0; z < HexTiles.Size; z += 3)
                    for (int x = 0; x < HexTiles.Size; x += 3)
                        if (w.Get(x, y, z) != again.Get(x, y, z)) differ++;
            // heights: the ground at a hex's middle is its level; a mountain's middle well over it
            int ox = tx * HexMapLayout.Tile - HexTiles.Pad, oz = tz * HexMapLayout.Tile - HexTiles.Pad;
            lm.HexesIn(tx * HexMapLayout.Tile, tz * HexMapLayout.Tile, (tx + 1) * HexMapLayout.Tile, (tz + 1) * HexMapLayout.Tile, near);
            foreach (var (c, r) in near)
            {
                var l = world.Look(c, r);
                var (cx, cz) = lm.Center(c, r);
                int lx = (int)cx - ox, lz = (int)cz - oz;
                if (lx < HexTiles.Pad || lz < HexTiles.Pad || lx >= HexTiles.Pad + HexMapLayout.Tile || lz >= HexTiles.Pad + HexMapLayout.Tile) continue;
                int top = 0;
                for (int y = HexTiles.SY - 1; y > 0; y--) { byte m = w.Get(lx, y, lz); if (m != Mat.Air && m != Mat.Water && !Mat.IsVeg[m]) { top = y + 1; break; } }
                if (l.Relief == HexRelief.Mountain) { peaks++; if (top < HexTiles.Base + l.Height * HexTiles.Step + 14) lowPeaks++; }
                else if (l.Ground is not (HexGround.Water or HexGround.DeepWater) && l.Relief == HexRelief.Flat && l.Icon == HexIcon.None && l.Rivers == 0 && l.Roads == 0)
                {
                    int want = HexTiles.Base + l.Height * HexTiles.Step;
                    if (Math.Abs(top - want) > 1) wrongH++;
                }
                if (l.Icon == HexIcon.Citadel && top > HexTiles.Base + l.Height * HexTiles.Step + 12) citadel++;
            }
        }
        check("a piece of the world map made in voxels and meshed in good time, the same each time; open hexes at their height, peaks where the mountains are, the citadel standing",
            make / pieces.Length < 150 && mesh / pieces.Length < 150 && differ == 0 && wrongH == 0 && peaks > 0 && lowPeaks == 0 && citadel == 1 && verts > 3000,
            $"made in {make / pieces.Length:0} ms, meshed in {mesh / pieces.Length:0} ms a piece of {HexMapLayout.Tile}² voxels (one thread); {verts / pieces.Length} vertices of ground and {waterVerts / pieces.Length} of water a piece; " +
            $"voxels differing made again {differ}; open hexes off their height {wrongH}; peaks {peaks}, too low {lowPeaks}; the citadel standing {citadel}");

        // ---- the far view of the whole XL map: not too heavy
        {
            var xl = new DemoHexes(144, 144, 11);
            var lx = new HexMapLayout(144, 144);
            var sw = Stopwatch.StartNew();
            int fv = 0;
            for (int r = 0; r < 144; r += 16)
                for (int c = 0; c < 144; c += 16)
                    fv += HexFar.Build(xl, lx, c, r, c + 16, r + 16).Vertices;
            double ms = sw.Elapsed.TotalMilliseconds;
            check("the far view of the whole XL map: made in good time, not too many vertices", ms < 1500 && fv < 1_200_000, $"{fv} vertices in {ms:0} ms");
        }
    }

    /// <summary>What the tops of the columns of each kind of hex are made of near our citadel (a look at the drawing, not a check).</summary>
    public static void Probe()
    {
        var src = new DemoHexes(72, 72, 7);
        var lay = new HexMapLayout(72, 72);
        var (hx, hz) = lay.Center(src.Home.Col, src.Home.Row);
        int tx = (int)(hx / HexMapLayout.Tile), tz = (int)(hz / HexMapLayout.Tile);
        var w = HexTiles.Build(src, lay, tx, tz);
        var by = new Dictionary<string, Dictionary<string, int>>();
        for (int lz = HexTiles.Pad; lz < HexTiles.Pad + HexMapLayout.Tile; lz++)
            for (int lx = HexTiles.Pad; lx < HexTiles.Pad + HexMapLayout.Tile; lx++)
            {
                int x = tx * HexMapLayout.Tile + lx - HexTiles.Pad, z = tz * HexMapLayout.Tile + lz - HexTiles.Pad;
                var (c, r) = lay.HexAt(x + .5f, z + .5f);
                if (!lay.OnField(c, r)) continue;
                var l = src.Look(c, r);
                string k = $"{l.Ground}/{l.Relief}/h{l.Height}";
                int y = HexTiles.SY - 1;
                while (y > 0 && w.Get(lx, y, lz) == Mat.Air) y--;
                string m = Mat.Def[w.Get(lx, y, lz)].Name + "@" + y;
                if (!by.TryGetValue(k, out var d)) by[k] = d = new Dictionary<string, int>();
                d[m] = d.GetValueOrDefault(m) + 1;
            }
        foreach (var (k, d) in by)
            Console.WriteLine($"{k}: " + string.Join(", ", d.OrderByDescending(e => e.Value).Take(6).Select(e => $"{e.Key} {e.Value}")));
    }

    /// <summary>The hexes one may walk to from one: not over mountains or water.</summary>
    static HashSet<(int Col, int Row)> Walkable(DemoHexes w, int col, int row)
    {
        var seen = new HashSet<(int, int)> { (col, row) };
        var q = new Queue<(int, int)>();
        q.Enqueue((col, row));
        while (q.Count > 0)
        {
            var (c, r) = q.Dequeue();
            for (int s = 0; s < 6; s++)
            {
                var (nc, nr) = HexMapLayout.Neighbour(c, r, s);
                if ((uint)nc >= (uint)w.Width || (uint)nr >= (uint)w.Height || seen.Contains((nc, nr))) continue;
                var l = w.Look(nc, nr);
                if (l.Relief == HexRelief.Mountain || l.Ground is HexGround.Water or HexGround.DeepWater) continue;
                seen.Add((nc, nr));
                q.Enqueue((nc, nr));
            }
        }
        return seen;
    }

    /// <summary>The map from straight above, a picture (PPM): each column its top's colour, shaded by height.</summary>
    public static void Dump(string size, uint seed, string file, int every = 2)
    {
        int side = HexMapLayout.SideOf(size);
        var src = new DemoHexes(side, side, seed) { RevealAll = true };
        var lay = new HexMapLayout(side, side);
        int W = lay.SX / every, H = lay.SZ / every;
        var px = new byte[W * H * 3];
        for (int tz = 0; tz < lay.TilesZ; tz++)
            for (int tx = 0; tx < lay.TilesX; tx++)
            {
                var w = HexTiles.Build(src, lay, tx, tz);
                for (int lz = HexTiles.Pad; lz < HexTiles.Pad + HexMapLayout.Tile; lz += every)
                    for (int lx = HexTiles.Pad; lx < HexTiles.Pad + HexMapLayout.Tile; lx += every)
                    {
                        int x = tx * HexMapLayout.Tile + lx - HexTiles.Pad, z = tz * HexMapLayout.Tile + lz - HexTiles.Pad;
                        if (x >= lay.SX || z >= lay.SZ) continue;
                        int y = HexTiles.SY - 1;
                        while (y > 0 && w.Get(lx, y, lz) == Mat.Air) y--;
                        byte m = w.Get(lx, y, lz);
                        uint c = Mat.Def[m].Srgb;
                        float k = .55f + y / 60f;
                        int i = ((z / every) * W + x / every) * 3;
                        if (i + 2 >= px.Length) continue;
                        px[i] = (byte)Math.Min(255, ((c >> 16) & 255) * k); px[i + 1] = (byte)Math.Min(255, ((c >> 8) & 255) * k); px[i + 2] = (byte)Math.Min(255, (c & 255) * k);
                    }
            }
        using var f = File.Create(file);
        var head = System.Text.Encoding.ASCII.GetBytes($"P6\n{W} {H}\n255\n");
        f.Write(head);
        f.Write(px);
        Console.WriteLine($"[worldview] {side}×{side} map dumped to {file} ({W}×{H})");
    }
}
