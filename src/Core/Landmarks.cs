using System;

namespace VoxelTower.Core;

/// <summary>
/// The valley's named places, laid when it is made: mines in rocky knolls, with a vein of ore and a timbered adit, and the
/// ruins of what stood here before — a watchtower by the ford, an outpost, the miners' houses, a tower fallen at the cliff —
/// broken down by the years, with their rubble round them. What each ruin was is remembered (<see cref="Site.Design"/>),
/// so it can be raised again as it stood. Laid before the trees, so none grow inside them.
/// </summary>
public static class Landmarks
{
    /// <summary>Columns a landmark (with a margin round it) takes: no trees or boulders go there.</summary>
    public static bool Taken(Site s, int x, int z, int margin)
    {
        foreach (var l in s.Landmarks) if (Brushes.Dist(x - l.X, z - l.Z) < l.R + margin) return true;
        return false;
    }

    static int Ground(VoxelWorld w, Site s, int x, int z) => s.HTop[Math.Clamp(z, 0, w.SZ - 1) * w.SX + Math.Clamp(x, 0, w.SX - 1)];

    static bool In(VoxelWorld w, int x, int z) => x >= 2 && z >= 2 && x < w.SX - 2 && z < w.SZ - 2;

    /// <summary>Remembers a voxel of what a ruin was: masonry, to be laid again if it is mended.</summary>
    static void Meant(Site s, int x, int y, int z)
    {
        if (s.Design != null && s.Design.InBounds(x, y, z)) s.Design.SetRaw(x, y, z, Shapes.Masonry(x, y, z));
    }

    /// <summary>Battlements every ten voxels along a wall's outer edge, six long: a merlon voxel here?</summary>
    static bool Merlon(int along) => (along % 10 + 10) % 10 < 6;

    /// <summary>
    /// A mine: a knoll of rock (raised if the ground is no knoll already) with a vein of ore in its heart, an adit timbered
    /// with logs driven into it from the side facing the citadel, and a spoil heap beside the mouth.
    /// </summary>
    public static Landmark Mine(VoxelWorld w, Site s, int cx, int cz, string name, int income, bool guarded, uint seed)
    {
        const int R = 18, H = 26;
        var noise = new ValueNoise(seed * 53 + 7);
        int g0 = Ground(w, s, cx, cz);
        for (int z = cz - R; z <= cz + R; z++)
            for (int x = cx - R; x <= cx + R; x++)
            {
                double d = Brushes.Dist(x - cx, z - cz);
                if (d > R || !In(w, x, z)) continue;
                int i = z * w.SX + x, g = s.HTop[i];
                int top = g + (int)((1 - d * d / (R * R)) * H * (.8 + .4 * noise.Sample(x * .15, z * .15)));
                for (int y = g; y < top; y++) w.SetRaw(x, y, z, y == top - 1 && Hash.H3(x, z, 31) < .35 ? Mat.Moss : Mat.Rock);
                if (top > g) s.HTop[i] = top;
            }
        // the vein: thick in the heart of the knoll, in specks further out
        int gc = g0 + 7;
        for (int y = gc - 6; y <= gc + 6; y++)
            for (int z = cz - 9; z <= cz + 9; z++)
                for (int x = cx - 9; x <= cx + 9; x++)
                {
                    double e = Math.Sqrt((x - cx) * (x - cx) / 81.0 + (y - gc) * (y - gc) / 36.0 + (z - cz) * (z - cz) / 81.0);
                    if (e > 1 || !In(w, x, z) || w.Get(x, y, z) is not (Mat.Rock or Mat.Moss or Mat.Soil)) continue;
                    if (Hash.H3(x * 7 + y, z, 37) < (e < .6 ? .9 : .4)) w.SetRaw(x, y, z, Mat.Ore);
                }
        // the adit, from the side facing the citadel
        int dx = s.KeepX - cx, dz = s.KeepZ - cz;
        bool alongX = Math.Abs(dx) >= Math.Abs(dz);
        int dir = alongX ? Math.Sign(dx) : Math.Sign(dz);
        if (dir == 0) dir = 1;
        (int x, int z) At(int along, int across) => alongX ? (cx + dir * along, cz + across) : (cx + across, cz + dir * along);
        var mouth = At(R, 0);
        int floor = Ground(w, s, mouth.x, mouth.z);
        // the ground the adit runs into may rise: start where the knoll begins
        for (int a = R + 6; a >= 0; a--)
        {
            var p = At(a, 0);
            if (Ground(w, s, p.x, p.z) > floor + 2) break;
            floor = Math.Min(floor, Ground(w, s, p.x, p.z));
        }
        const int Deep = 14, Half = 5, Tall = 17;
        for (int a = R - Deep; a <= R + 2; a++)
            for (int c = -Half; c < Half; c++)
            {
                var (x, z) = At(a, c);
                if (!In(w, x, z)) continue;
                for (int y = floor; y < floor + Tall; y++) w.SetRaw(x, y, z, Mat.Air);
            }
        // timbering: posts and a lintel at the mouth and twice inside
        foreach (int a in new[] { R, R - 6, R - 12 })
        {
            for (int c = -Half - 2; c < Half + 2; c++)
                for (int t = 0; t < 2; t++)
                {
                    var (x, z) = At(a - t, c);
                    if (!In(w, x, z)) continue;
                    bool post = c < -Half || c >= Half;
                    for (int y = floor; y < floor + Tall + 2; y++)
                        if (post || y >= floor + Tall - 1) w.SetRaw(x, y, z, Mat.Wood);
                }
        }
        // the spoil heap beside the mouth
        var heap = At(R + 4, Half + 9);
        for (int z = heap.z - 6; z <= heap.z + 6; z++)
            for (int x = heap.x - 6; x <= heap.x + 6; x++)
            {
                double d = Brushes.Dist(x - heap.x, z - heap.z);
                if (d > 6 || !In(w, x, z)) continue;
                int g = Ground(w, s, x, z), hgt = (int)((1 - d / 6) * 5);
                for (int y = g; y < g + hgt; y++) w.SetRaw(x, y, z, Hash.H3(x, y, z) < .3 ? Mat.Rock : Mat.Soil);
            }
        var stand = At(R + 10, 0);
        var l = new Landmark { Kind = LandmarkKind.Mine, Name = name, X = cx, Z = cz, R = R + 10, StandX = stand.x, StandZ = stand.z, Income = income, Guarded = guarded };
        s.Landmarks.Add(l);
        return l;
    }

    /// <summary>One voxel of old masonry, if the years left it: tops broken off unevenly, holes knocked through, moss on what is left.</summary>
    static void Old(VoxelWorld w, int x, int y, int z, int bottom, int keep, int top, uint seed)
    {
        if (y >= bottom + keep) return;
        if (Hash.H3(x * 13 + y, z * 7 + (int)seed, 41) < .07) return;
        w.SetRaw(x, y, z, y == bottom + keep - 1 && Hash.H3(x, z, 43) < .4 ? Mat.Moss : Mat.Ruin);
    }

    /// <summary>Rubble fallen round a ruin: a voxel or two of old masonry here and there on the ground.</summary>
    static void Rubble(VoxelWorld w, Site s, int cx, int cz, int r0, int r1, double share)
    {
        for (int z = cz - r1; z <= cz + r1; z++)
            for (int x = cx - r1; x <= cx + r1; x++)
            {
                double d = Brushes.Dist(x - cx, z - cz);
                if (d < r0 || d > r1 || !In(w, x, z) || Hash.H3(x, z, 47) > share * (1 - (d - r0) / (r1 - r0 + 1))) continue;
                int g = Ground(w, s, x, z), n = Hash.H3(x, z, 49) < .3 ? 2 : 1;
                if (w.Get(x, g, z) != Mat.Air && !Mat.IsVeg[w.Get(x, g, z)]) continue;
                for (int y = g; y < g + n; y++) w.SetRaw(x, y, z, Mat.Ruin);
            }
    }

    /// <summary>A round watchtower fallen in on itself: one side still stands high, the other is a stump.</summary>
    public static Landmark Watchtower(VoxelWorld w, Site s, int cx, int cz, string name, uint seed, double standing = .75)
    {
        const int R = 9, T = 3, H = 40;
        double side = Hash.H3((int)seed, 5, 51) * Math.PI * 2;
        int top = Ground(w, s, cx, cz) + H;
        for (int z = cz - R; z <= cz + R; z++)
            for (int x = cx - R; x <= cx + R; x++)
            {
                double d = Brushes.Dist(x - cx + .5, z - cz + .5);
                if (d > R || d <= R - T || !In(w, x, z)) continue;
                double a = Math.Atan2(z - cz, x - cx);
                double up = .5 + .5 * Math.Cos(a - side);
                int keep = (int)(H * standing * (.15 + .85 * up * up) * (.85 + .3 * Hash.H3(x, z, 53)));
                int g = Ground(w, s, x, z);
                // a doorway at the foot, away from the standing side
                bool door = Math.Cos(a - side - Math.PI) > .96;
                for (int y = g; y < g + keep; y++) if (!door || y >= g + 16) Old(w, x, y, z, g, keep, H, seed);
                // as it stood: up to a level top, with battlements round it
                bool merlon = (int)Math.Floor((a + Math.PI) / (Math.PI * 2) * 20) % 2 == 0;
                for (int y = g; y < top + (merlon ? 5 : 0); y++) if (!door || y >= g + 16) Meant(s, x, y, z);
            }
        Rubble(w, s, cx, cz, R, R + 10, .22);
        var l = new Landmark { Kind = LandmarkKind.Ruin, Name = name, X = cx, Z = cz, R = R + 10 };
        s.Landmarks.Add(l);
        return l;
    }

    /// <summary>An outpost: a square of walls with its gateway gaping and a corner tower, mostly gone to the ground.</summary>
    public static Landmark Outpost(VoxelWorld w, Site s, int cx, int cz, string name, uint seed)
    {
        const int A = 22, T = 4, H = 24;
        var noise = new ValueNoise(seed * 61 + 3);
        int top = Ground(w, s, cx, cz) + H;
        for (int z = cz - A; z <= cz + A; z++)
            for (int x = cx - A; x <= cx + A; x++)
            {
                if (!In(w, x, z)) continue;
                int west = x - (cx - A), east = (cx + A) - x, north = z - (cz - A), south = (cz + A) - z;
                bool wall = west < T || east < T || north < T || south < T;
                bool gateway = east < T && Math.Abs(z - cz) < 6;
                if (!wall || gateway) continue;
                int g = Ground(w, s, x, z);
                int keep = (int)(H * Math.Max(0, noise.Sample(x * .09, z * .09) * 1.4 - .25));
                for (int y = g; y < g + keep; y++) Old(w, x, y, z, g, keep, H, seed);
                // as it stood: walls to a level top, battlements along their outer edge
                bool edge = west < 2 || east < 2 || north < 2 || south < 2;
                bool merlon = edge && Merlon(west < 2 || east < 2 ? z - cz : x - cx);
                for (int y = g; y < top + (merlon ? 6 : 0); y++) Meant(s, x, y, z);
            }
        Watchtower(w, s, cx - A + 2, cz - A + 2, name, seed + 1, .9);
        s.Landmarks.RemoveAt(s.Landmarks.Count - 1);
        Rubble(w, s, cx, cz, 0, A + 8, .1);
        var l = new Landmark { Kind = LandmarkKind.Ruin, Name = name, X = cx, Z = cz, R = A + 10 };
        s.Landmarks.Add(l);
        return l;
    }

    /// <summary>A few small houses with their roofs long gone: low walls, doorways, a fallen gable.</summary>
    public static Landmark Houses(VoxelWorld w, Site s, int cx, int cz, string name, uint seed)
    {
        var spots = new[] { (-18, -4), (12, -14), (8, 16) };
        int n = 0;
        foreach (var (ox, oz) in spots)
        {
            int hx = cx + ox, hz = cz + oz;
            bool turned = Hash.H3(hx, hz, 57) < .5;
            int a = turned ? 6 : 8, b = turned ? 8 : 6;
            const int T = 2, H = 14;
            int g0 = Ground(w, s, hx, hz);
            for (int z = hz - b; z <= hz + b; z++)
                for (int x = hx - a; x <= hx + a; x++)
                {
                    if (!In(w, x, z)) continue;
                    bool wall = x - (hx - a) < T || (hx + a) - x < T || z - (hz - b) < T || (hz + b) - z < T;
                    bool door = (hz + b) - z < T && Math.Abs(x - hx) < 2;
                    if (!wall) continue;
                    if (door)
                    {
                        int gd = Ground(w, s, x, z), eave0 = g0 + (int)(H * .6);
                        for (int y = gd + 12; y < eave0; y++) Meant(s, x, y, z);
                        continue;
                    }
                    int g = Ground(w, s, x, z);
                    // the gable ends stand taller
                    bool gable = turned ? z - (hz - b) < T || (hz + b) - z < T : x - (hx - a) < T || (hx + a) - x < T;
                    int keep = (int)((gable ? H : H * .6) * (.35 + .65 * Hash.H3(x, z, 59 + n)));
                    for (int y = g; y < g + keep; y++) Old(w, x, y, z, g, keep, H, seed);
                    // as it stood: walls to the eaves, the gables up to a point, a lintel over the door
                    int eave = g0 + (int)(H * .6), ridge = gable ? eave + (int)((H - H * .6) * (1 - Math.Abs(turned ? x - hx : z - hz) / (double)(turned ? a : b))) : eave;
                    for (int y = g; y < ridge; y++) if (!door || y >= g + 12) Meant(s, x, y, z);
                }
            Rubble(w, s, hx, hz, 0, 12, .08);
            n++;
        }
        var l = new Landmark { Kind = LandmarkKind.Ruin, Name = name, X = cx, Z = cz, R = 34 };
        s.Landmarks.Add(l);
        return l;
    }
}
