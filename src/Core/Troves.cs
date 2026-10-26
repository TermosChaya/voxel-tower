using System;

namespace VoxelTower.Core;

/// <summary>
/// What a valley of the map nobody holds may keep (docs/handoff/13-world-map.md, phase 2): an abandoned mine where the
/// dead keep a chest of the old miners' silver; old ruins where a pack of wolves has made its den over a chest; a den of
/// beasts in the rocks, bears and wolves on the bones of what they dragged in; a shrine of trial, a ring of stones round
/// an altar, where whoever holds the altar against the dead that come for it is blessed.
/// </summary>
public enum TroveKind : byte { None, DeadMine, Ruins, Den, Shrine }

/// <summary>
/// The treasures of the map laid in their valleys as the valley is made (MapGen.Relief.cs), off the middle on the side
/// away from the enemy's: of rock, old masonry and timber like everything else, to be dug, burnt and knocked down. Each
/// keeps its chest (or the shrine its altar) at <see cref="Site.TroveX"/>, and its guardians' spots in
/// <see cref="Site.TrovePosts"/>; the map (Campaign.Troves.cs) says who keeps it and what it gives.
/// </summary>
public static class Troves
{
    /// <summary>What a treasure is called.</summary>
    public static string Name(TroveKind k) => k switch
    {
        TroveKind.DeadMine => "Заброшенный рудник",
        TroveKind.Ruins => "Старые руины",
        TroveKind.Den => "Логово зверей",
        TroveKind.Shrine => "Святилище испытаний",
        _ => "",
    };

    /// <summary>How far round its middle a treasure spreads (voxels), for its place to be found clear of everything else.</summary>
    public static int Room(TroveKind k) => k switch { TroveKind.DeadMine => 46, TroveKind.Ruins => 60, TroveKind.Den => 42, _ => 76 };

    /// <summary>The treasure laid about a spot; its landmark among the valley's named places.</summary>
    public static Landmark Lay(VoxelWorld w, Site s, TroveKind k, int cx, int cz, bool taken, uint seed)
    {
        s.Trove = k;
        s.TrovePosts.Clear();
        switch (k)
        {
            case TroveKind.DeadMine: DeadMine(w, s, cx, cz, taken, seed); break;
            case TroveKind.Ruins: Ruins(w, s, cx, cz, taken, seed); break;
            case TroveKind.Den: Den(w, s, cx, cz, taken, seed); break;
            case TroveKind.Shrine: Shrine(w, s, cx, cz, seed); break;
            default: return null;
        }
        var l = new Landmark { Kind = LandmarkKind.Trove, Name = Name(k), X = cx, Z = cz, R = Room(k), StandX = s.TroveX, StandZ = s.TroveZ };
        s.Landmarks.Add(l);
        return l;
    }

    static int Ground(VoxelWorld w, Site s, int x, int z) => s.HTop[Math.Clamp(z, 0, w.SZ - 1) * w.SX + Math.Clamp(x, 0, w.SX - 1)];

    static bool In(VoxelWorld w, int x, int z) => x >= 2 && z >= 2 && x < w.SX - 2 && z < w.SZ - 2 && w.OnLand(x, z);

    static void Set(VoxelWorld w, int x, int y, int z, byte m) { if (w.InBounds(x, y, z)) w.SetRaw(x, y, z, m); }

    /// <summary>The side from a spot that faces the valley's middle, as a unit step along x or z, and the step across it.</summary>
    static ((int x, int z) along, (int x, int z) across) Facing(Site s, int cx, int cz)
    {
        int dx = s.KeepX - cx, dz = s.KeepZ - cz;
        bool alongX = Math.Abs(dx) >= Math.Abs(dz);
        int dir = alongX ? Math.Sign(dx) : Math.Sign(dz);
        if (dir == 0) dir = 1;
        return alongX ? ((dir, 0), (0, 1)) : ((0, dir), (1, 0));
    }

    /// <summary>
    /// A knoll of rock raised over the ground round a spot (as a mine's), mossy on its crown, and ore in specks through
    /// it; returns the height of the ground at its foot.
    /// </summary>
    static int Knoll(VoxelWorld w, Site s, int cx, int cz, int r, int h, bool ore, uint seed)
    {
        var noise = new ValueNoise(seed * 59 + 3);
        int g0 = Ground(w, s, cx, cz);
        for (int z = cz - r; z <= cz + r; z++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                // (its edge ragged inward, never past its reach)
                double d0 = Brushes.Dist(x - cx, z - cz), d = Math.Min(r, d0 * (.85 + .3 * noise.Sample(x * .05, z * .05)) + d0 * .15);
                if (d0 > r || !In(w, x, z)) continue;
                int i = z * w.SX + x, g = s.HTop[i];
                int top = Math.Max(g, g0 + (int)((1 - d * d / (r * r)) * h * (.8 + .4 * noise.Sample(x * .13, z * .13))));
                for (int y = g; y < top; y++)
                {
                    byte m = y == top - 1 && Hash.H3(x, z, 131) < .45 ? Mat.Moss : Mat.Rock;
                    if (ore && m == Mat.Rock && Hash.H3(x * 7 + y, z, 133) < .04) m = Mat.Ore;
                    w.SetRaw(x, y, z, m);
                }
                if (top > g) s.HTop[i] = top;
            }
        return g0;
    }

    /// <summary>
    /// A chest of the old days on the ground at a spot, its long side across the way it is come at: boards bound in iron,
    /// a dark lid, a lock of brass. Its spot is the treasure's.
    /// </summary>
    static void Chest(VoxelWorld w, Site s, int x, int y, int z, (int x, int z) across)
    {
        for (int a = -3; a < 3; a++)
            for (int b = -2; b < 2; b++)
            {
                int px = x + across.x * a + (across.x == 0 ? b : 0), pz = z + across.z * a + (across.z == 0 ? b : 0);
                for (int dy = 0; dy < 4; dy++)
                {
                    bool band = a == -3 || a == 2, lid = dy == 3;
                    Set(w, px, y + dy, pz, lid ? (band ? Mat.Iron : Mat.BoardDark) : band ? Mat.Iron : Mat.Crate);
                }
            }
        // the lock on the face toward the way in
        Set(w, x, y + 2, z, Mat.Brass);
        s.TroveX = x; s.TroveY = y; s.TroveZ = z;
    }

    /// <summary>
    /// The way out of a mouth cut into a knoll at a floor: where the ground before it lies higher, a sloping cut down to the
    /// floor, so that whoever goes in or out walks it.
    /// </summary>
    static void Approach(VoxelWorld w, Site s, Func<int, int, (int x, int z)> at, int from, int half, int floor, int tall)
    {
        for (int a = from; a < from + 40; a++)
        {
            int want = floor + (a - from) / 2;
            bool any = false;
            for (int c = -half; c < half; c++)
            {
                var (x, z) = at(a, c);
                if (!In(w, x, z)) continue;
                int i = z * w.SX + x;
                if (s.HTop[i] <= want) continue;
                any = true;
                for (int y = want; y < s.HTop[i] + tall; y++) w.SetRaw(x, y, z, Mat.Air);
                w.SetRaw(x, want - 1, z, Mat.DirtPath);
                s.HTop[i] = want;
            }
            if (!any) break;
        }
    }

    /// <summary>A heap of bones on the ground: of those the dead or the beasts had.</summary>
    static void Bones(VoxelWorld w, Site s, int bx, int bz, int r, uint seed)
    {
        for (int z = bz - r; z <= bz + r; z++)
            for (int x = bx - r; x <= bx + r; x++)
            {
                int d = Math.Abs(x - bx) + Math.Abs(z - bz);
                if (d > r || !In(w, x, z) || Hash.H3(x + (int)seed, z, 141) < .3) continue;
                int g = Ground(w, s, x, z);
                if (w.Get(x, g, z) != Mat.Air && !Mat.IsVeg[w.Get(x, g, z)]) continue;
                for (int y = g; y < g + (d <= r / 3 ? 2 : 1); y++) w.SetRaw(x, y, z, Mat.BonePile);
            }
    }

    // ------------------------------------------------------------ the abandoned mine

    /// <summary>
    /// The abandoned mine: a big knoll of rock with ore in it, an adit driven in from the side facing the valley's middle,
    /// half its timbering fallen, to a chamber in the heart of the knoll where the old miners' chest stands among bones;
    /// the spoil heap grown over beside the mouth. Its dead keep the mouth, the adit and the chamber.
    /// </summary>
    static void DeadMine(VoxelWorld w, Site s, int cx, int cz, bool taken, uint seed)
    {
        const int R = 42, H = 52, Half = 9, Tall = 27, Room = 13, RoomTall = 22;
        var (along, across) = Facing(s, cx, cz);
        (int x, int z) At(int a, int c) => (cx + along.x * a + across.x * c, cz + along.z * a + across.z * c);
        int g0 = Knoll(w, s, cx, cz, R, H, true, seed);
        // the floor: the ground at the mouth, and no higher than the ground the knoll stands on
        var mouth = At(R + 2, 0);
        int floor = Math.Min(g0, Ground(w, s, mouth.x, mouth.z));
        // the adit, from beyond the mouth to the chamber
        for (int a = 0; a <= R + 4; a++)
            for (int c = -Half; c < Half; c++)
            {
                var (x, z) = At(a, c);
                if (!In(w, x, z)) continue;
                for (int y = floor; y < floor + Tall; y++) w.SetRaw(x, y, z, Mat.Air);
                w.SetRaw(x, floor - 1, z, Hash.H3(x, z, 151) < .3 ? Mat.Rubble : Mat.Rock);
            }
        // the chamber: a hollow in the heart of the knoll, round, its roof domed (a man's height and more to its sides, for
        // whoever walks into it)
        for (int z = cz - Room; z <= cz + Room; z++)
            for (int x = cx - Room; x <= cx + Room; x++)
            {
                double d = Brushes.Dist(x - cx, z - cz);
                if (d > Room || !In(w, x, z)) continue;
                int top = floor + (int)(RoomTall * Math.Sqrt(Math.Max(0, 1 - d * d / (Room * Room + 1))) + 24);
                for (int y = floor; y < top; y++) w.SetRaw(x, y, z, Mat.Air);
                w.SetRaw(x, floor - 1, z, Mat.Rock);
            }
        Approach(w, s, At, R + 5, Half, floor, 16);
        // the timbering: a post either side and a lintel every few metres, half of it long fallen
        for (int a = R; a >= Room; a -= 8)
        {
            bool fallen = Hash.H3(a, (int)seed, 153) < .45 && a < R;
            for (int c = -Half - 1; c <= Half; c++)
            {
                var (x, z) = At(a, c);
                if (!In(w, x, z)) continue;
                bool post = c == -Half - 1 || c == Half;
                if (fallen)
                {
                    // a lintel come down across the floor
                    if (!post && Hash.H3(x, z, 155) < .8) w.SetRaw(x, floor, z, Mat.Wood);
                    continue;
                }
                for (int y = floor; y < floor + Tall + 1; y++)
                    if (post || y >= floor + Tall - 1) w.SetRaw(x, y, z, Mat.Wood);
            }
        }
        // the spoil heap gone to grass beside the mouth
        var heap = At(R + 8, Half + 12);
        for (int z = heap.z - 8; z <= heap.z + 8; z++)
            for (int x = heap.x - 8; x <= heap.x + 8; x++)
            {
                double d = Brushes.Dist(x - heap.x, z - heap.z);
                if (d > 8 || !In(w, x, z)) continue;
                int g = Ground(w, s, x, z), hgt = (int)((1 - d / 8) * 6);
                for (int y = g; y < g + hgt; y++) w.SetRaw(x, y, z, y == g + hgt - 1 ? Mat.Grass : Hash.H3(x, y, z) < .3 ? Mat.Rock : Mat.Soil);
                if (hgt > 0) s.HTop[z * w.SX + x] = g + hgt;
            }
        // bones at the mouth and in the chamber, and the chest at the chamber's back
        var b0 = At(R + 4, -Half - 3);
        Bones(w, s, b0.x, b0.z, 3, seed);
        var back = At(-Room + 4, 0);
        if (!taken) Chest(w, s, back.x, floor, back.z, across);
        else { s.TroveX = back.x; s.TroveY = floor; s.TroveZ = back.z; }
        foreach (int c in new[] { -6, 6 })
        {
            var (x, z) = At(2, c);
            for (int y = floor; y < floor + 2; y++) if (Hash.H3(x, y, z) < .7) Set(w, x, y, z, Mat.BonePile);
        }
        // its dead: two outside the mouth, one in the adit, the rest in the chamber
        foreach (var (a, c) in new[] { (R + 10, -5), (R + 10, 5), (R / 2, 0), (2, -5), (2, 5), (-3, 0), (5, 0), (R + 14, 0) })
        {
            var (x, z) = At(a, c);
            s.TrovePosts.Add((x, a <= R ? floor : -1, z));
        }
    }

    // ------------------------------------------------------------ the ruins

    /// <summary>
    /// Old ruins: a long hall of old masonry, its walls standing to a man's height here and a stump there, its doorway
    /// toward the valley's middle; its pillars broken, two of them fallen full length; flags underfoot gone to moss,
    /// rubble round it; at its far end, where an altar was, the chest. The wolves keep about it.
    /// </summary>
    static void Ruins(VoxelWorld w, Site s, int cx, int cz, bool taken, uint seed)
    {
        const int Len = 44, Wide = 28, Thick = 3;
        var (along, across) = Facing(s, cx, cz);
        (int x, int z) At(int a, int c) => (cx + along.x * a + across.x * c, cz + along.z * a + across.z * c);
        int g = Ground(w, s, cx, cz);
        // the floor brought level, flags of old stone with moss between
        for (int a = -Len - 2; a <= Len + 2; a++)
            for (int c = -Wide - 2; c <= Wide + 2; c++)
            {
                var (x, z) = At(a, c);
                if (!In(w, x, z)) continue;
                int i = z * w.SX + x, h = s.HTop[i];
                for (int y = Math.Min(h, g); y < Math.Max(h, g) + 12; y++) w.SetRaw(x, y, z, y < g ? Mat.Soil : Mat.Air);
                w.SetRaw(x, g - 1, z, Hash.H3(x, z, 161) < .35 ? Mat.Moss : Mat.Ruin);
                s.HTop[i] = g;
            }
        // the walls, each stretch standing to its own height, the doorway in the near end
        for (int a = -Len; a <= Len; a++)
            for (int c = -Wide; c <= Wide; c++)
            {
                bool wall = Math.Abs(a) > Len - Thick || Math.Abs(c) > Wide - Thick;
                if (!wall) continue;
                if (a > Len - Thick && Math.Abs(c) < 6) continue;
                var (x, z) = At(a, c);
                if (!In(w, x, z)) continue;
                int stretch = (a + Len) / 9 * 31 + (c + Wide) / 9 * 17;
                int keep = 4 + (int)(Hash.H3(stretch, (int)seed, 163) * 26);
                for (int y = g; y < g + keep; y++)
                {
                    if (Hash.H3(x * 13 + y, z * 7 + (int)seed, 165) < .06) continue;
                    w.SetRaw(x, y, z, y == g + keep - 1 && Hash.H3(x, z, 167) < .4 ? Mat.Moss : Mat.Ruin);
                }
            }
        // the pillars down the hall, broken off; two lie full length where they fell
        int k = 0;
        foreach (int c in new[] { -Wide / 2, Wide / 2 })
            for (int a = -Len + 14; a <= Len - 14; a += 14, k++)
            {
                var (x, z) = At(a, c);
                bool fell = Hash.H3(k, (int)seed, 169) < .35;
                int high = fell ? 3 : 8 + (int)(Hash.H3(k, (int)seed, 171) * 18);
                for (int dz = 0; dz < 3; dz++)
                    for (int dx = 0; dx < 3; dx++)
                        for (int y = g; y < g + high; y++) Set(w, x + dx - 1, y, z + dz - 1, Mat.Ruin);
                if (!fell) continue;
                // lying along the hall, toward its back
                for (int t = 2; t < 18; t++)
                {
                    var (fx, fz) = At(a - t, c + (c < 0 ? 1 : -1));
                    for (int y = g; y < g + 3; y++) if (In(w, fx, fz)) w.SetRaw(fx, y, fz, Mat.Ruin);
                }
            }
        // rubble round about
        for (int a = -Len - 14; a <= Len + 14; a++)
            for (int c = -Wide - 14; c <= Wide + 14; c++)
            {
                if (Math.Abs(a) <= Len + 2 && Math.Abs(c) <= Wide + 2 || Hash.H3(a + 999, c + (int)seed, 173) > .05) continue;
                var (x, z) = At(a, c);
                if (!In(w, x, z)) continue;
                int h = Ground(w, s, x, z);
                if (w.Get(x, h, z) == Mat.Air || Mat.IsVeg[w.Get(x, h, z)]) w.SetRaw(x, h, z, Mat.Ruin);
            }
        // the old altar's step at the back, and on it the chest
        var altar = At(-Len + 9, 0);
        for (int a = -Len + 4; a <= -Len + 14; a++)
            for (int c = -8; c <= 8; c++)
            {
                var (x, z) = At(a, c);
                if (In(w, x, z)) w.SetRaw(x, g, z, Mat.Ruin);
            }
        if (!taken) Chest(w, s, altar.x, g + 1, altar.z, across);
        else { s.TroveX = altar.x; s.TroveY = g + 1; s.TroveZ = altar.z; }
        var b0 = At(Len - 12, Wide - 9);
        Bones(w, s, b0.x, b0.z, 3, seed);
        // the pack: about the altar, in the hall and at the doorway
        foreach (var (a, c) in new[] { (-Len + 16, -6), (-Len + 16, 6), (0, -10), (0, 10), (Len - 8, 0), (Len + 10, -6), (Len + 10, 6) })
        {
            var (x, z) = At(a, c);
            s.TrovePosts.Add((x, a <= Len ? g : -1, z));
        }
    }

    // ------------------------------------------------------------ the den

    /// <summary>
    /// A den of beasts: a heap of rock with a cave mouth in it toward the valley's middle, low and ragged, going back to a
    /// hollow where they lie; gnawed bones strewn before it and a carcass half eaten; at the back what they dragged in,
    /// an old chest among it. Bears in the cave, wolves before it.
    /// </summary>
    static void Den(VoxelWorld w, Site s, int cx, int cz, bool taken, uint seed)
    {
        const int R = 36, H = 40, Hollow = 14;
        var (along, across) = Facing(s, cx, cz);
        (int x, int z) At(int a, int c) => (cx + along.x * a + across.x * c, cz + along.z * a + across.z * c);
        int g0 = Knoll(w, s, cx, cz, R, H, false, seed);
        var mouth = At(R + 2, 0);
        int floor = Math.Min(g0, Ground(w, s, mouth.x, mouth.z));
        var noise = new ValueNoise(seed * 61 + 9);
        // the cave: wider and taller at its mouth, ragged, narrowing in, opening out into the hollow
        for (int a = -Hollow; a <= R + 4; a++)
        {
            double t = Math.Clamp((double)(a + Hollow) / (R + Hollow + 4), 0, 1);
            int half = (int)(a < 0 ? Hollow * Math.Sqrt(1 - (double)a * a / (Hollow * Hollow + 1)) + 4 : 9 + 4 * t);
            for (int c = -half - 2; c <= half + 2; c++)
            {
                if (Math.Abs(c) > half + (noise.Sample(a * .3, c * .3) - .5) * 4) continue;
                var (x, z) = At(a, c);
                if (!In(w, x, z)) continue;
                int tall = (int)(26 + 6 * t + (noise.Sample(a * .2 + 7, c * .2) - .5) * 4 - Math.Abs(c) * .3);
                for (int y = floor; y < floor + tall; y++) w.SetRaw(x, y, z, Mat.Air);
                w.SetRaw(x, floor - 1, z, Mat.Soil);
            }
        }
        Approach(w, s, At, R + 5, 8, floor, 16);
        // bones strewn before the mouth, a carcass half eaten
        for (int i = 0; i < 4; i++)
        {
            var (bx, bz) = At(R + 8 + (int)(Hash.H3(i, (int)seed, 181) * 14), (int)((Hash.H3(i, (int)seed, 183) - .5) * 26));
            Bones(w, s, bx, bz, 2, seed + (uint)i);
        }
        for (int a = 0; a < 7; a++)
            for (int c = 0; c < 4; c++)
            {
                var (x, z) = At(R + 12 + a, 9 + c);
                if (!In(w, x, z)) continue;
                int h = Ground(w, s, x, z);
                w.SetRaw(x, h, z, (a + c) % 3 == 0 ? Mat.Bone : Mat.FurDeer);
                if (c is 1 or 2 && a is > 1 and < 6) w.SetRaw(x, h + 1, z, a % 2 == 0 ? Mat.Bone : Mat.FurDeer);
            }
        // what they dragged in, at the back: bones heaped about an old chest
        var back = At(-Hollow + 5, 0);
        Bones(w, s, back.x + across.x * 5, back.z + across.z * 5, 3, seed + 7);
        if (!taken) Chest(w, s, back.x, floor, back.z, across);
        else { s.TroveX = back.x; s.TroveY = floor; s.TroveZ = back.z; }
        // the bears in the hollow, the wolves before the mouth
        foreach (var (a, c) in new[] { (-2, -5), (-2, 5), (R + 10, -8), (R + 10, 8), (R + 16, 0), (R + 6, 0), (4, 0) })
        {
            var (x, z) = At(a, c);
            s.TrovePosts.Add((x, a <= R ? floor : -1, z));
        }
    }

    // ------------------------------------------------------------ the shrine of trial

    /// <summary>How far round the altar of a shrine of trial its holders must stand (8 m).</summary>
    public const int AltarReach = 64;

    /// <summary>
    /// A shrine of trial: a ring of tall rough stones, some leaning, round a floor of old flags, and in the middle an altar
    /// of dressed stone with a fire of the old powers burning cold on it. Whoever holds the altar long enough against the
    /// dead that come for it is blessed (Campaign.Troves.cs). No guardian keeps it: they come when it is held.
    /// </summary>
    static void Shrine(VoxelWorld w, Site s, int cx, int cz, uint seed)
    {
        const int Ring = 52, Floor = Ring + 10;
        int g = Ground(w, s, cx, cz);
        // the floor of flags round the altar, brought level
        for (int z = cz - Floor - 12; z <= cz + Floor + 12; z++)
            for (int x = cx - Floor - 12; x <= cx + Floor + 12; x++)
            {
                double d = Brushes.Dist(x - cx, z - cz);
                if (d > Floor + 12 || !In(w, x, z)) continue;
                int i = z * w.SX + x, h = s.HTop[i];
                // the ground brought level, blending back over a rim; flags only about the altar, turf out to the stones
                int want = d <= Floor ? g : (int)Math.Round(g + (h - g) * (d - Floor) / 12.0);
                for (int y = Math.Min(h, want); y < Math.Max(h, want) + 14; y++) w.SetRaw(x, y, z, y < want ? Mat.Soil : Mat.Air);
                w.SetRaw(x, want - 1, z, d <= 26 ? (Hash.H3(x, z, 191) < .3 ? Mat.Moss : Mat.Ruin) : Mat.Grass);
                s.HTop[i] = want;
            }
        // the stones of the ring, each rough and of its own height, a few leaning
        for (int k = 0; k < 9; k++)
        {
            double a = k * Math.PI * 2 / 9 + Hash.H3(k, (int)seed, 193) * .2;
            int sx = cx + (int)Math.Round(Math.Cos(a) * Ring), sz = cz + (int)Math.Round(Math.Sin(a) * Ring);
            int h = 22 + (int)(Hash.H3(k, (int)seed, 195) * 12), lean = Hash.H3(k, (int)seed, 197) < .3 ? 1 : 0;
            int gs = Ground(w, s, sx, sz);
            for (int y = gs - 3; y < gs + h; y++)
            {
                int off = lean * (y - gs) / 9;
                for (int dz = 0; dz < 3; dz++)
                    for (int dx = 0; dx < 4; dx++)
                    {
                        bool corner = (dx == 0 || dx == 3) && (dz == 0 || dz == 2) && y > gs + h - 3;
                        if (!corner) Set(w, sx + dx - 2 + (int)(Math.Cos(a) * off), y, sz + dz - 1 + (int)(Math.Sin(a) * off), y == gs + h - 1 && dx % 2 == 0 ? Mat.Moss : Mat.Menhir);
                    }
            }
        }
        // the altar: a block of dressed stone, its top a slab, a cold fire of the old powers burning on it
        for (int y = g; y < g + 7; y++)
            for (int z = cz - 4; z < cz + 4; z++)
                for (int x = cx - 6; x < cx + 6; x++)
                {
                    bool slab = y == g + 6;
                    if (!slab && (x == cx - 6 || x == cx + 5) && (z == cz - 4 || z == cz + 3)) continue;
                    w.SetRaw(x, y, z, Shapes.Masonry(x, y, z) == Mat.StoneDark ? Mat.KeepShade : Mat.Keep);
                }
        for (int z = cz - 1; z < cz + 1; z++)
            for (int x = cx - 2; x < cx + 2; x++)
                w.SetRaw(x, g + 7, z, Mat.SoulFire);
        s.TroveX = cx; s.TroveY = g + 7; s.TroveZ = cz;
        // the dead come in from round about, toward the altar: where they rise
        for (int k = 0; k < 6; k++)
        {
            double a = k * Math.PI / 3 + .4;
            s.TrovePosts.Add((cx + (int)(Math.Cos(a) * (Ring + 60)), -1, cz + (int)(Math.Sin(a) * (Ring + 60))));
        }
    }
}
