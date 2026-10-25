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
        // an outcrop of ore is kept clear too: no tree or boulder over it
        foreach (var o in s.Outcrops) if (Brushes.Dist(x - o.X, z - o.Z) < o.R + margin) return true;
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

    /// <summary>
    /// An old stone bridge over the river, weathered and mossy, arched from bank to bank. One whose middle fell in lies
    /// broken over the water, its stones on the riverbed below; mended, it carries the road across again.
    /// </summary>
    public static Landmark OldBridge(VoxelWorld w, Site s, int ax, int az, int bx, int bz, string name, bool whole, uint seed)
    {
        var plan = Bridges.Plan(w, s, ax, az, bx, bz, 12, Mat.Stone);
        s.Arched ??= new VoxelWorld(w.SX, w.SY, w.SZ);
        for (int i = 0; i < plan.Count; i++) s.Arched.SetRaw(plan.X(i), plan.Y(i), plan.Z(i), 1);
        double dx = bx - ax, dz = bz - az, span = Math.Sqrt(dx * dx + dz * dz);
        var noise = new ValueNoise(seed * 67 + 5);
        for (int i = 0; i < plan.Count; i++)
        {
            int x = plan.X(i), y = plan.Y(i), z = plan.Z(i);
            if (plan.M(i) == 0) continue;
            Meant(s, x, y, z);
            double t = ((x + .5 - ax - .5) * dx + (z + .5 - az - .5) * dz) / (span * span);
            if (!whole && Math.Abs(t - .5) < .16 + .07 * noise.Sample(x * .2, y * .2 + z * .2))
            {
                // fallen: a stone or two of it lies on the riverbed below
                if (Hash.H3(x * 3 + y, z, 87) < .07)
                {
                    int yy = y;
                    while (yy > 1 && Brushes.Free(w.Get(x, yy - 1, z))) yy--;
                    w.SetRaw(x, yy, z, Mat.Ruin);
                }
                continue;
            }
            w.SetRaw(x, y, z, Mat.Ruin);
        }
        // moss on what the rain falls on
        for (int i = 0; i < plan.Count; i++)
        {
            int x = plan.X(i), y = plan.Y(i), z = plan.Z(i);
            if (w.Get(x, y, z) == Mat.Ruin && Brushes.Free(w.Get(x, y + 1, z)) && Hash.H3(x, y, z * 7) < .22) w.SetRaw(x, y, z, Mat.Moss);
        }
        var l = new Landmark { Kind = LandmarkKind.Ruin, Name = name, X = (ax + bx) / 2, Z = (az + bz) / 2, R = (int)(span / 2) + 10 };
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

    // ---------------------------------------------------------------- places of power

    /// <summary>An old stone standing upright in the ground: a little rounded at the corners, moss on its crown.</summary>
    static void Standing(VoxelWorld w, Site s, int x0, int z0, int wx, int wz, int h, byte m)
    {
        int g = Ground(w, s, x0 + wx / 2, z0 + wz / 2);
        for (int z = z0; z < z0 + wz; z++)
            for (int x = x0; x < x0 + wx; x++)
            {
                if (!In(w, x, z)) continue;
                bool corner = (x == x0 || x == x0 + wx - 1) && (z == z0 || z == z0 + wz - 1);
                int top = g + h - (corner ? 2 : 0) - (int)(Hash.H3(x, z, 63) * 2);
                for (int y = g - 3; y < top; y++) w.SetRaw(x, y, z, y == top - 1 && m == Mat.Ruin && Hash.H3(x, z, 65) < .5 ? Mat.Moss : m);
            }
    }

    /// <summary>
    /// A barrow: a mound in long dry grass over the dead of the old days, a doorway of three stones in it facing the
    /// citadel and dark inside, a ring of standing stones round it, and a black stone laid flat on its crown.
    /// </summary>
    public static Landmark Barrow(VoxelWorld w, Site s, int cx, int cz, string name, uint seed)
    {
        const int R = 34, H = 18, Ring = 46;
        // the doorway looks toward the citadel
        double ax = s.KeepX - cx, az = s.KeepZ - cz, al = Math.Max(1, Math.Sqrt(ax * ax + az * az));
        ax /= al; az /= al;
        double Along(int x, int z) => (x - cx) * ax + (z - cz) * az;
        double Across(int x, int z) => -(x - cx) * az + (z - cz) * ax;
        for (int z = cz - R; z <= cz + R; z++)
            for (int x = cx - R; x <= cx + R; x++)
            {
                double d = Brushes.Dist(x - cx, z - cz);
                if (d > R || !In(w, x, z)) continue;
                // the forecourt before the doorway is cut back to the ground
                if (Along(x, z) > R - 13 && Math.Abs(Across(x, z)) < 10) continue;
                int i = z * w.SX + x, g = s.HTop[i];
                int top = g + (int)(H * Math.Pow(1 - d * d / (R * R), .7) * (.92 + .16 * Hash.H3(x >> 2, z >> 2, (int)seed)));
                if (top <= g) continue;
                for (int y = g; y < top; y++) w.SetRaw(x, y, z, y == top - 1 ? Mat.GrassDry : Mat.Soil);
                s.HTop[i] = top;
            }
        // the doorway: two stones and a black lintel across them, the passage into the mound behind
        int g0 = Ground(w, s, cx + (int)(ax * (R - 6)), cz + (int)(az * (R - 6)));
        for (int a = -24; a <= R - 12; a++)
            for (int c = -10; c <= 10; c++)
            {
                int x = cx + (int)Math.Round(ax * a - az * c), z = cz + (int)Math.Round(az * a + ax * c);
                if (!In(w, x, z)) continue;
                bool jamb = Math.Abs(c) >= 5 && Math.Abs(c) <= 9 && a >= R - 16, opening = Math.Abs(c) < 5;
                if (jamb) for (int y = g0 - 2; y < g0 + 16; y++) w.SetRaw(x, y, z, Mat.Ruin);
                else if (opening) for (int y = g0; y < g0 + 12; y++) w.SetRaw(x, y, z, Mat.Air);
                if (a >= R - 16 && Math.Abs(c) <= 9) for (int y = g0 + 12; y < g0 + 16; y++) if (!jamb) w.SetRaw(x, y, z, Mat.Menhir);
                if (a >= R - 16 && Math.Abs(c) <= 9) for (int y = g0 + 16; y < g0 + 18; y++) w.SetRaw(x, y, z, Mat.Menhir);
            }
        // the capstone on the crown, sunk in the turf
        int crown = Ground(w, s, cx, cz);
        for (int z = cz - 4; z <= cz + 4; z++)
            for (int x = cx - 4; x <= cx + 4; x++)
                if (In(w, x, z) && Brushes.Dist((x - cx) * .8, z - cz) < 3.7) for (int y = crown - 2; y < crown + 1; y++) w.SetRaw(x, y, z, Mat.Menhir);
        // the ring of stones, open where the doorway looks out
        for (int n = 0; n < 9; n++)
        {
            double a = Math.Atan2(az, ax) + Math.PI * 2 * (n + .5) / 9;
            int x = cx + (int)(Math.Cos(a) * Ring), z = cz + (int)(Math.Sin(a) * Ring);
            if (Math.Cos(a - Math.Atan2(az, ax)) > .94) continue;
            Standing(w, s, x - 2, z - 2, 5, 4 + n % 2, 12 + (int)(Hash.H3(n, (int)seed, 67) * 10), Mat.Ruin);
        }
        var l = new Landmark { Kind = LandmarkKind.Power, Name = name, X = cx, Z = cz, R = Ring + 10, StandX = cx, StandZ = cz };
        s.Landmarks.Add(l);
        return l;
    }

    /// <summary>
    /// The black stone: a knoll of bare rock and on it a menhir taller than three men, leaning a little, that nobody ever
    /// cut, of a stone found nowhere else in the valley; a few broken pieces of it lie in the grass below.
    /// </summary>
    public static Landmark BlackStone(VoxelWorld w, Site s, int cx, int cz, string name, uint seed)
    {
        const int R = 26, H = 10, Tall = 50;
        var noise = new ValueNoise(seed * 71 + 3);
        for (int z = cz - R; z <= cz + R; z++)
            for (int x = cx - R; x <= cx + R; x++)
            {
                double d = Brushes.Dist(x - cx, z - cz);
                if (d > R || !In(w, x, z)) continue;
                int i = z * w.SX + x, g = s.HTop[i];
                int top = g + (int)((1 - d * d / (R * R)) * H * (.75 + .5 * noise.Sample(x * .17, z * .17)));
                for (int y = g; y < top; y++) w.SetRaw(x, y, z, y == top - 1 && Hash.H3(x, z, 69) < .3 ? Mat.Moss : Mat.Rock);
                if (top > g) s.HTop[i] = top;
            }
        int foot = Ground(w, s, cx, cz) - 3;
        for (int y = 0; y < Tall + 5; y++)
        {
            double t = y / (double)Tall;
            // narrowing up to a blunt point, and leaning over toward the east
            double hw = y < Tall ? 5.4 - 2.2 * t : 3.2 - (y - Tall) * .6, hd = y < Tall ? 4.3 - 1.6 * t : 2.7 - (y - Tall) * .5, ox = 2.6 * t * t;
            for (int z = -6; z <= 6; z++)
                for (int x = -7; x <= 8; x++)
                {
                    double ex = (x + .5 - ox) / hw, ez = (z + .5) / hd;
                    if (ex * ex + ez * ez > 1 + (Hash.H3(x * 5 + y, z, 73) - .5) * .25) continue;
                    if (In(w, cx + x, cz + z)) w.SetRaw(cx + x, foot + y, cz + z, Mat.Menhir);
                }
        }
        // broken pieces in the grass
        for (int n = 0; n < 5; n++)
        {
            double a = Hash.H3(n, (int)seed, 75) * Math.PI * 2, r = R - 6 + Hash.H3(n, (int)seed, 77) * 12;
            int px = cx + (int)(Math.Cos(a) * r), pz = cz + (int)(Math.Sin(a) * r);
            int sz = 2 + n % 2, g = Ground(w, s, px, pz);
            for (int z = pz - sz; z <= pz + sz; z++)
                for (int x = px - sz; x <= px + sz; x++)
                    if (In(w, x, z) && Brushes.Dist(x - px, z - pz) <= sz) for (int y = g - 1; y < g + sz; y++) w.SetRaw(x, y, z, Mat.Menhir);
        }
        // held from beside the stone, on the side toward the citadel
        int toward = s.KeepX >= cx ? 1 : -1;
        var l = new Landmark { Kind = LandmarkKind.Power, Name = name, X = cx, Z = cz, R = R + 12, StandX = cx + toward * 16, StandZ = cz };
        s.Landmarks.Add(l);
        return l;
    }

    /// <summary>
    /// A dead spring: a black pool in a hollow of bare earth where nothing grows, ringed with old stones, and round it
    /// trees long dead and grey.
    /// </summary>
    public static Landmark DeadSpring(VoxelWorld w, Site s, int cx, int cz, string name, uint seed)
    {
        const int Pool = 14, Hollow = 32, Ring = 22;
        // the hollow: the ground goes down gently to the pool, bare
        for (int z = cz - Hollow; z <= cz + Hollow; z++)
            for (int x = cx - Hollow; x <= cx + Hollow; x++)
            {
                double d = Brushes.Dist(x - cx, z - cz);
                if (d > Hollow || !In(w, x, z)) continue;
                int i = z * w.SX + x, g = s.HTop[i];
                int top = g - (int)(5 * (1 - d / Hollow));
                for (int y = top; y < g + 2; y++) w.SetRaw(x, y, z, Mat.Air);
                w.SetRaw(x, top - 1, z, d < Hollow - 4 || Hash.H3(x, z, 79) < .5 ? Mat.Soil : Mat.GrassDry);
                s.HTop[i] = top;
            }
        // the pool, its surface a voxel under the lowest point of its edge, so it lies still
        int rim = int.MaxValue;
        for (int z = cz - Pool - 2; z <= cz + Pool + 2; z++)
            for (int x = cx - Pool - 2; x <= cx + Pool + 2; x++)
            {
                double d = Brushes.Dist(x - cx, z - cz);
                if (d >= Pool && d < Pool + 2 && In(w, x, z)) rim = Math.Min(rim, s.HTop[z * w.SX + x]);
            }
        for (int z = cz - Pool; z <= cz + Pool; z++)
            for (int x = cx - Pool; x <= cx + Pool; x++)
            {
                double d = Brushes.Dist(x - cx, z - cz);
                if (d >= Pool || !In(w, x, z)) continue;
                int i = z * w.SX + x, bed = rim - 6 - (int)(5 * (1 - d / Pool));
                for (int y = bed; y <= Math.Max(s.HTop[i], rim); y++) w.SetRaw(x, y, z, y < rim - 1 ? Mat.Water : Mat.Air);
                w.SetRaw(x, bed - 1, z, Mat.Soil);
                s.HTop[i] = bed;
                if (s.WaterTop != null) s.WaterTop[i] = (short)(rim - 1);
            }
        for (int n = 0; n < 8; n++)
        {
            double a = Math.PI * 2 * n / 8 + Hash.H3((int)seed, n, 81) * .3;
            int x = cx + (int)(Math.Cos(a) * Ring), z = cz + (int)(Math.Sin(a) * Ring);
            Standing(w, s, x - 2, z - 1, 4, 3, 6 + (int)(Hash.H3(n, (int)seed, 83) * 7), Mat.Ruin);
        }
        foreach (var (a, r) in new[] { (.4, 40), (2.5, 44), (4.4, 38) })
            Flora.DeadTree(w, cx + (int)(Math.Cos(a + seed % 7) * r), cz + (int)(Math.Sin(a + seed % 7) * r), grown: true, scale: 1.3);
        // held from the shore, on the side toward the citadel
        int toward = s.KeepX >= cx ? 1 : -1;
        var l = new Landmark { Kind = LandmarkKind.Power, Name = name, X = cx, Z = cz, R = Hollow + 18, StandX = cx + toward * (Pool + 5), StandZ = cz };
        s.Landmarks.Add(l);
        return l;
    }
}
