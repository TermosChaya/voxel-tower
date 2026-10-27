using System;

namespace VoxelTower.Core;

/// <summary>
/// What stands in the middle of a valley that is not the first one (<see cref="Holding"/>): the place of an outpost, the
/// plateau levelled and cleared where the outpost is to rise, with its square marked out and a stone in the middle; or
/// the horde's town, a ring of sharpened logs round thatched huts, a fire and a totem hung with bones. Laid while the
/// valley is made, instead of the citadel.
/// </summary>
public static class Holdings
{
    /// <summary>How far the outpost's place is levelled round its middle (voxels: 12 m), and half its square (8 m).</summary>
    public const int PlaceR = 96, Square = 64;
    /// <summary>The horde's palisade, how far round its fire (15 m), and how high (3¼ m).</summary>
    public const int PalisadeR = 120, PalisadeH = 26;

    /// <summary>
    /// The place of an outpost: the plateau levelled round the middle and cleared of stones, its square marked out in
    /// rammed earth with a peg at each corner, and the stone that marks it in the middle, dressed like the citadel's
    /// masonry. The horde, raiding the valley, makes for the stone (the outpost to come stands there), so a raid has
    /// somewhere to go before anything is built. Returns the height of its ground.
    /// </summary>
    public static int OutpostPlace(VoxelWorld w, Site s)
    {
        int cx = s.KeepX, cz = s.KeepZ, g = s.HTop[cz * w.SX + cx];
        Level(w, s, cx, cz, PlaceR, 16, g);
        // the square, in rammed earth flush with the turf
        for (int z = cz - Square - 1; z <= cz + Square + 1; z++)
            for (int x = cx - Square - 1; x <= cx + Square + 1; x++)
            {
                int ax = Math.Abs(x - cx), az = Math.Abs(z - cz);
                if (Math.Max(ax, az) < Square - 1 || !w.OnLand(x, z)) continue;
                int h = s.HTop[z * w.SX + x];
                if (Math.Abs(h - g) <= 1) w.SetRaw(x, h - 1, z, Mat.DirtPath);
            }
        // a peg at each corner
        foreach (int dz in new[] { -Square, Square })
            foreach (int dx in new[] { -Square, Square })
                for (int pz = 0; pz < 2; pz++)
                    for (int px = 0; px < 2; px++)
                    {
                        int x = cx + dx - px, z = cz + dz - pz, h = s.HTop[z * w.SX + x];
                        for (int y = h; y < h + 12; y++) w.SetRaw(x, y, z, Mat.Wood);
                    }
        // the stone in the middle: a squat block of dressed stone, narrowing upward
        for (int y = 0; y < 16; y++)
        {
            int half = 6 - y / 4;
            for (int z = cz - half; z < cz + half; z++)
                for (int x = cx - half; x < cx + half; x++)
                    w.SetRaw(x, g + y, z, Shapes.Masonry(x, g + y, z) == Mat.StoneDark ? Mat.KeepShade : Mat.Keep);
        }
        return g;
    }

    /// <summary>
    /// The horde's town in the middle of its valley: its ground levelled and trodden, a palisade of sharpened logs round
    /// it with a gap toward the side the enemy comes by (<see cref="Site.EntrySide"/>, ours) between two tall posts with
    /// bones on them; four huts and the chief's bigger one, of logs under thatch, their doors to the middle; a fire in a
    /// ring of stones, a totem with a red rag and a skull, heaps of bones. The garrison keeps at the doors, the fire and
    /// the gap (<see cref="Site.HordePosts"/>). What the town has built on the big map (<paramref name="works"/>, a bit a
    /// <see cref="HordeWork"/>) stands about the yard: the fighting pit, the shaman's hut, the chief's banners nearer the
    /// fire; the trolls' den, the giant's cave, the yard of engines, the war camp's tents, the brewery, the sappers'
    /// burrows further out; with the stone wall the palisade is of rough stone. Returns the height of its ground.
    /// </summary>
    public static int HordeTown(VoxelWorld w, Site s, uint seed, uint works = 0)
    {
        bool Has(HordeWork k) => (works >> (int)k & 1) != 0;
        int cx = s.KeepX, cz = s.KeepZ, g = s.HTop[cz * w.SX + cx];
        Level(w, s, cx, cz, PalisadeR + 12, 20, g);
        var noise = new ValueNoise(seed * 71 + 5);
        // the yard trodden bare in patches
        for (int z = cz - PalisadeR; z <= cz + PalisadeR; z++)
            for (int x = cx - PalisadeR; x <= cx + PalisadeR; x++)
            {
                if (Brushes.Dist(x - cx, z - cz) > PalisadeR - 3 || !w.OnLand(x, z)) continue;
                int h = s.HTop[z * w.SX + x];
                if (h == g && noise.Sample(x * .06, z * .06) > .45) w.SetRaw(x, h - 1, z, Mat.DirtPath);
            }
        // the way in faces the side the enemy (we) comes by
        var (ix, iz) = s.Hex != null ? s.Hex.Inward(s.EntrySide) : (1f, 0f);
        double gate = Math.Atan2(-iz, -ix), gap = 20.0 / PalisadeR;
        // the palisade: logs a quarter of a metre thick standing side by side, every other one a little taller
        for (int z = cz - PalisadeR - 2; z <= cz + PalisadeR + 2; z++)
            for (int x = cx - PalisadeR - 2; x <= cx + PalisadeR + 2; x++)
            {
                double d = Brushes.Dist(x - cx + .5, z - cz + .5);
                if (d < PalisadeR - 1 || d >= PalisadeR + 1 || !w.OnLand(x, z)) continue;
                double a = Math.Atan2(z - cz, x - cx);
                if (Math.Abs(Angle(a - gate)) < gap) continue;
                int log = (int)Math.Floor((a + Math.PI) * PalisadeR / 2), h = s.HTop[z * w.SX + x];
                int top = h + PalisadeH + (log % 2 == 0 ? 2 : 0) + (int)(noise.Sample(log * .7, 3) * 3);
                // the logs set a little into the ground, sharpened: their outer half stops a voxel short; of rough stone
                // once the town has its stone wall
                for (int y = h - 2; y < top - (d > PalisadeR ? 1 : 0); y++) w.SetRaw(x, y, z, Has(HordeWork.StoneWall) ? Mat.Rock : Mat.Wood);
            }
        // the two posts either side of the gap, a heap of bones on each
        foreach (int side in new[] { -1, 1 })
        {
            double a = gate + side * (gap + 3.0 / PalisadeR);
            int px = cx + (int)Math.Round(Math.Cos(a) * PalisadeR), pz = cz + (int)Math.Round(Math.Sin(a) * PalisadeR), h = s.HTop[pz * w.SX + px];
            Block(w, px - 2, h - 2, pz - 2, 4, PalisadeH + 12, 4, Mat.Wood);
            Block(w, px - 1, h + PalisadeH + 10, pz - 1, 3, 3, 3, Mat.BonePile);
            // a guard either side of the gap, inside
            var (qx, qz) = (cx + (int)(Math.Cos(a) * (PalisadeR - 14)), cz + (int)(Math.Sin(a) * (PalisadeR - 14)));
            s.HordePosts.Add((qx, qz));
        }
        // the huts round the fire, the chief's across from the gap; their doors to the middle
        for (int k = 1; k <= 5; k++)
        {
            double a = gate + k * Math.PI / 3;
            bool chief = k == 3;
            int r = chief ? 26 : 18, dist = chief ? 74 : 70;
            int hx = cx + (int)Math.Round(Math.Cos(a) * dist), hz = cz + (int)Math.Round(Math.Sin(a) * dist);
            Hut(w, s, hx, hz, r, chief ? 18 : 14, a + Math.PI, g);
            s.HordePosts.Add((hx - (int)(Math.Cos(a) * (r + 6)), hz - (int)(Math.Sin(a) * (r + 6))));
        }
        // the fire: a ring of stones, logs and embers in it
        for (int z = cz - 10; z <= cz + 10; z++)
            for (int x = cx - 10; x <= cx + 10; x++)
            {
                double d = Brushes.Dist(x - cx + .5, z - cz + .5);
                if (d >= 7.5 && d < 10) for (int y = g; y < g + 2 + (Hash.H3(x, z, 61) < .4 ? 1 : 0); y++) w.SetRaw(x, y, z, Mat.Rock);
                else if (d < 7.5)
                {
                    w.SetRaw(x, g, z, Mat.Char);
                    if (d < 5.5) w.SetRaw(x, g + 1, z, Hash.H3(x, z, 62) < .5 ? Mat.Ember : Mat.Wood);
                    if (d < 3) w.SetRaw(x, g + 2, z, Mat.Ember);
                }
            }
        s.Torches.Add((cx + .5f, g + 6, cz + .5f));
        foreach (var (dx, dz) in new[] { (16, 0), (-16, 0), (0, 16), (0, -16) }) s.HordePosts.Add((cx + dx, cz + dz));
        // the totem beside the fire: a pole, a red rag hung from it, a skull on top
        {
            double a = gate + Math.PI / 6;
            int tx = cx + (int)Math.Round(Math.Cos(a) * 26), tz = cz + (int)Math.Round(Math.Sin(a) * 26);
            Block(w, tx - 1, g - 2, tz - 1, 2, 48, 2, Mat.Wood);
            Block(w, tx - 2, g + 46, tz - 2, 4, 4, 4, Mat.BonePile);
            Block(w, tx + 1, g + 26, tz - 4, 1, 16, 8, Mat.Paint);
        }
        // heaps of bones about the yard
        for (int k = 0; k < 4; k++)
        {
            double a = gate + .5 + k * 1.4, d = 40 + Hash.H3(k, 3, (int)seed) * 30;
            int bx = cx + (int)(Math.Cos(a) * d), bz = cz + (int)(Math.Sin(a) * d);
            for (int z = bz - 2; z <= bz + 2; z++)
                for (int x = bx - 2; x <= bx + 2; x++)
                    if (Math.Abs(x - bx) + Math.Abs(z - bz) <= 3) for (int y = g; y < g + (Math.Abs(x - bx) + Math.Abs(z - bz) <= 1 ? 2 : 1); y++) w.SetRaw(x, y, z, Mat.BonePile);
        }
        if (works != 0) Works(w, s, works, gate, cx, cz, g);
        s.Landmarks.Add(new Landmark { Kind = LandmarkKind.Lair, Name = "Логово орды", X = cx, Z = cz, R = PalisadeR + 10, StandX = cx, StandZ = cz });
        return g;
    }

    /// <summary>
    /// What a town of the horde has built, about its yard between the huts: nearer the fire the fighting pit, the
    /// shaman's hut and the chief's banners; further out the trolls' den and the giant's cave (heaps of rough stone with a
    /// dark mouth), the yard of engines (a ram's frame of logs), the war camp (two tents of red cloth), the brewery (a
    /// cauldron of green brew in a ring of stones) and the sappers' burrows (mounds of earth).
    /// </summary>
    static void Works(VoxelWorld w, Site s, uint works, double gate, int cx, int cz, int g)
    {
        bool Has(HordeWork k) => (works >> (int)k & 1) != 0;
        (int x, int z) At(double a, int d) => (cx + (int)Math.Round(Math.Cos(a) * d), cz + (int)Math.Round(Math.Sin(a) * d));
        void Mound(int x0, int z0, int r, int tall, byte m, bool mouth, double face)
        {
            for (int z = z0 - r; z <= z0 + r; z++)
                for (int x = x0 - r; x <= x0 + r; x++)
                {
                    double d = Brushes.Dist(x - x0 + .5, z - z0 + .5);
                    if (d > r || !w.OnLand(x, z)) continue;
                    int top = g + (int)(tall * Math.Sqrt(1 - d * d / (r * r)));
                    bool door = mouth && Math.Abs(Angle(Math.Atan2(z - z0, x - x0) - face)) * r < 4;
                    for (int y = g; y < top; y++) if (!(door && y < g + 8 && d > r * .3)) w.SetRaw(x, y, z, m);
                }
        }
        // nearer the fire, between the huts
        int slot = 0;
        double Inner() => gate + Math.PI / 2 + Math.PI / 3 * slot++;
        if (Has(HordeWork.Pit))
        {
            var (px, pz) = At(Inner(), 46);
            for (int z = pz - 10; z <= pz + 10; z++)
                for (int x = px - 10; x <= px + 10; x++)
                {
                    double d = Brushes.Dist(x - px + .5, z - pz + .5);
                    if (d < 8) for (int y = g - 3; y < g; y++) w.SetRaw(x, y, z, y == g - 3 ? Mat.DirtPath : Mat.Air);
                    else if (d < 10 && Hash.H3(x, z, 63) < .35) for (int y = g - 1; y < g + 7; y++) w.SetRaw(x, y, z, Mat.Wood);
                }
        }
        if (Has(HordeWork.Shaman))
        {
            var (hx, hz) = At(Inner(), 46);
            Hut(w, s, hx, hz, 10, 10, Math.Atan2(cz - hz, cx - hx), g);
            Block(w, hx + 12, g - 2, hz - 1, 2, 30, 2, Mat.Wood);
            Block(w, hx + 11, g + 28, hz - 2, 4, 4, 4, Mat.BonePile);
            Block(w, hx + 14, g + 18, hz - 3, 1, 10, 6, Mat.Brew);
        }
        if (Has(HordeWork.ChiefHall))
        {
            var (bx, bz) = At(Inner(), 46);
            foreach (int dz in new[] { -5, 5 })
            {
                Block(w, bx - 1, g - 2, bz + dz - 1, 2, 40, 2, Mat.Wood);
                Block(w, bx + 1, g + 22, bz + dz - 4, 1, 16, 8, Mat.Paint);
            }
        }
        // further out
        slot = 0;
        double Outer() => gate + Math.PI / 6 + Math.PI / 3 * slot++;
        if (Has(HordeWork.TrollDen)) { var (x, z) = At(Outer(), 98); Mound(x, z, 12, 12, Mat.Rock, true, Math.Atan2(cz - z, cx - x)); }
        else slot++;
        if (Has(HordeWork.GiantCave)) { var (x, z) = At(Outer(), 98); Mound(x, z, 14, 16, Mat.Rock, true, Math.Atan2(cz - z, cx - x)); }
        else slot++;
        if (Has(HordeWork.Yard))
        {
            var (x, z) = At(Outer(), 98);
            Block(w, x - 8, g, z - 4, 16, 2, 2, Mat.Wood); Block(w, x - 8, g, z + 2, 16, 2, 2, Mat.Wood);
            Block(w, x - 6, g + 2, z - 4, 2, 8, 2, Mat.Wood); Block(w, x + 4, g + 2, z - 4, 2, 8, 2, Mat.Wood);
            Block(w, x - 6, g + 2, z + 2, 2, 8, 2, Mat.Wood); Block(w, x + 4, g + 2, z + 2, 2, 8, 2, Mat.Wood);
            Block(w, x - 9, g + 6, z - 1, 18, 2, 2, Mat.Wood);
        }
        else slot++;
        if (Has(HordeWork.WarCamp))
        {
            var (x, z) = At(Outer(), 98);
            foreach (int dx in new[] { -8, 8 })
                for (int y = 0; y < 9; y++)
                    Block(w, x + dx - 6 + y * 6 / 9, g + y, z - 5, Math.Max(1, 12 - y * 12 / 9), 1, 10, Mat.Paint);
        }
        else slot++;
        if (Has(HordeWork.Alchemist))
        {
            var (x, z) = At(Outer(), 98);
            for (int zz = z - 4; zz <= z + 4; zz++)
                for (int xx = x - 4; xx <= x + 4; xx++)
                {
                    double d = Brushes.Dist(xx - x + .5, zz - z + .5);
                    if (d < 4.5 && d >= 3) for (int y = g; y < g + 4; y++) w.SetRaw(xx, y, zz, Mat.Rock);
                    else if (d < 3) w.SetRaw(xx, g + 2, zz, Mat.Brew);
                }
        }
        else slot++;
        if (Has(HordeWork.Burrow))
        {
            var (x, z) = At(Outer(), 98);
            foreach (var (dx, dz) in new[] { (-7, -4), (6, -3), (0, 6) }) Mound(x + dx, z + dz, 5, 4, Mat.Soil, true, Math.Atan2(-dz, -dx));
        }
    }

    /// <summary>A round hut of logs under a cone of thatch, its door (a man's height, a metre wide) turned the way given.</summary>
    static void Hut(VoxelWorld w, Site s, int hx, int hz, int r, int wall, double door, int g)
    {
        int roof = r + 4, peak = wall + r + 6;
        for (int z = hz - roof; z <= hz + roof; z++)
            for (int x = hx - roof; x <= hx + roof; x++)
            {
                double d = Brushes.Dist(x - hx + .5, z - hz + .5);
                if (d > roof || !w.OnLand(x, z)) continue;
                bool inDoor = Math.Abs(Angle(Math.Atan2(z - hz, x - hx) - door)) * r < 5;
                // the floor at the yard's height, earth under it where the ground falls away
                for (int y = s.HTop[z * w.SX + x]; y < g; y++) w.SetRaw(x, y, z, Mat.Soil);
                if (d < r && d >= r - 2)
                    for (int y = g; y < g + wall; y++)
                        if (!(inDoor && y < g + 12)) w.SetRaw(x, y, z, Mat.Wood);
                // the thatch: a shell over the walls narrowing to the smoke hole at the top
                for (int y = g + wall - 2; y < g + peak; y++)
                {
                    double rr = roof * (1 - (double)(y - (g + wall - 2)) / (peak - wall + 2));
                    if (d < rr && d >= rr - 2.5 && rr > 2) w.SetRaw(x, y, z, Mat.Hay);
                }
            }
    }

    /// <summary>
    /// The ground round a spot brought to one height, blending back into the land over a rim, and cleared of the stones,
    /// tufts and flowers on it; columns standing far off the height (a cliff, a pond) are left as they are.
    /// </summary>
    static void Level(VoxelWorld w, Site s, int cx, int cz, int r, int rim, int g)
    {
        for (int z = cz - r - rim; z <= cz + r + rim; z++)
            for (int x = cx - r - rim; x <= cx + r + rim; x++)
            {
                if (!w.OnLand(x, z)) continue;
                int i = z * w.SX + x, h = s.HTop[i];
                double d = Brushes.Dist(x - cx + .5, z - cz + .5);
                if (d > r + rim || Math.Abs(h - g) > 10 || s.WaterTop != null && s.WaterTop[i] > 0) continue;
                int want = d <= r ? g : (int)Math.Round(g + (h - g) * (d - r) / rim);
                byte top = w.Get(x, h - 1, z);
                if (!Mat.IsGrass(top) && top != Mat.Path && top != Mat.Moss) top = Mat.Grass;
                // what lay on the ground goes; over the rim, only low things, so the trees about stand
                for (int y = Math.Min(h, want); y < h + (d <= r ? 40 : 12); y++) w.SetRaw(x, y, z, Mat.Air);
                for (int y = h - 1; y < want - 1; y++) w.SetRaw(x, y, z, Mat.Soil);
                w.SetRaw(x, want - 1, z, top);
                s.HTop[i] = want;
            }
    }

    static void Block(VoxelWorld w, int x0, int y0, int z0, int sx, int sy, int sz, byte m)
    {
        for (int y = y0; y < y0 + sy; y++)
            for (int z = z0; z < z0 + sz; z++)
                for (int x = x0; x < x0 + sx; x++)
                    if (w.InBounds(x, y, z)) w.SetRaw(x, y, z, m);
    }

    static double Angle(double a)
    {
        while (a > Math.PI) a -= Math.PI * 2;
        while (a < -Math.PI) a += Math.PI * 2;
        return a;
    }
}
