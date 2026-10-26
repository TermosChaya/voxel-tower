using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// How a building's walls are laid: of its own material all through; a stone ground floor under a timber frame filled
/// with daub (and frame on the storeys over it); frame from a low plinth up; daub between timber corners; or rough
/// masonry of big stones.
/// </summary>
public enum WallStyle : byte { Plain, StoneBase, Frame, Plaster, Rough }

/// <summary>
/// A roof's form: two slopes and two gables; sloping on all four sides (hipped); gables clipped halfway up into short
/// slopes (half-hipped); or two slopes as steep as they are wide, as thatch is laid.
/// </summary>
public enum RoofForm : byte { Gable, Hip, HalfHip, Steep }

/// <summary>What stands on a roof's ridge in the middle: nothing, a belfry with its bell, a louvre letting the smoke and the hay's breath out, a spire.</summary>
public enum RidgeTop : byte { None, Belfry, Louvre, Spire }

/// <summary>
/// A part built on to a building outside its walls, open to the air so that the rules of the room inside are its own:
/// a canopy on posts, a shed leaning on a wall, a porch over the door, a fenced yard, a tower standing by an end, a wing
/// of walls and a roof of its own standing out square from the building with a wide way into it at its far end.
/// </summary>
public enum AnnexKind : byte { Canopy, Shed, Porch, Yard, Tower, Wing }

/// <summary>A side of a building as one stepping out of its door sees it: the door's wall, the one behind, the wall on the left, the one on the right.</summary>
public enum Flank : byte { Front, Back, Left, Right }

/// <summary>
/// A part built on to a building (see <see cref="AnnexKind"/>): the side it stands by, how far out it reaches, how much of
/// the wall it runs along (0, all of it), how high it stands to its eaves, what its roof is covered with (0, the
/// building's covering), a canopy's roof in two slopes with its ridge running out from the wall (else one slope down
/// from the wall), a tower round, at the wall's end rather than its middle (a tower, a wing: the building's plan an L),
/// a yard walled in stone rather than fenced (a courtyard), and what stands under it or in it.
/// </summary>
public sealed class Annex
{
    public AnnexKind Kind;
    public Flank At;
    public int Depth, Span, Height;
    public byte Covering;
    public bool Gabled, Round, AtEnd, Walled;
    public Prop[] Holds = Array.Empty<Prop>();
}

/// <summary>
/// One of the looks a kind of town building is put up in (Sim.Looks.cs): its name for the player, how its walls are
/// laid, its roof's form and covering (0, the kind's), what stands on its ridge, how much longer or shorter, how much
/// narrower and how much higher than the kind's it is, and what is built on to it.
/// </summary>
public sealed class Look
{
    public string Name = "";
    public WallStyle Walls;
    public RoofForm Roof;
    public byte Covering;
    public RidgeTop Crown;
    public int DD, DW;
    /// <summary>How much higher its first walls stand than its kind's.</summary>
    public int DH;
    public Annex[] Annexes = Array.Empty<Annex>();
}

/// <summary>
/// The shapes a look gives a building's house (Shapes.House, Shapes.Storey): the face of its walls voxel by voxel, its
/// gables, its roof in any of the forms with what stands on the ridge. Everything stays what the workers lay, in the
/// materials they lay it of: timber framing is logs, the daub between it earth, the covering what the roof has.
/// </summary>
public static class Looks
{
    const int T = Shapes.HouseWall, E = Shapes.Eaves;
    /// <summary>Framing: a post every two metres, the rail under the windows, the plate at the top.</summary>
    const int PostEvery = 16, Rail = 10;

    /// <summary>
    /// What a voxel of a building's wall is laid as: the look's walls at <paramref name="h"/> voxels over the floor of
    /// the storey (<paramref name="upper"/> for one over the first), its walls <paramref name="high"/> voxels high,
    /// <paramref name="a"/> along a wall <paramref name="len"/> long, on its outer face or not.
    /// </summary>
    static byte Face(Look look, byte wallMat, int x, int y, int z, int h, int a, int len, bool outer, bool upper, int high)
    {
        bool corner = a < T || a >= len - T;
        int q = a - T;
        bool post = !corner && q >= PostEvery && q % PostEvery < 2 && len - T - a > 6;
        switch (look.Walls)
        {
            case WallStyle.Rough:
                // big stones: the coursed masonry's pattern twice its size
                return Shapes.Masonry(x >> 1, y >> 1, z >> 1);
            case WallStyle.Plaster:
                if (h < (upper ? 0 : 3)) return Shapes.Masonry(x, y, z);
                if (corner || h >= high - 1) return Mat.Wood;
                return Mat.Daub;
            case WallStyle.StoneBase:
                if (!upper && h < Rail) return Shapes.Masonry(x, y, z);
                return Frame(h, q, len, corner, post, outer, braced: upper, high);
            case WallStyle.Frame:
                if (!upper && h < 2) return Shapes.Masonry(x, y, z);
                return Frame(h, q, len, corner, post, outer, braced: true, high);
            default:
                return Shapes.Lay(wallMat, x, y, z);
        }
    }

    /// <summary>
    /// A voxel of timber framing: corner posts, posts every two metres, the sill, the rail under the windows and the plate
    /// at the top of logs; on the outer face a brace rising to the rail in the end panels and another from the rail up;
    /// daub between.
    /// </summary>
    static byte Frame(int h, int q, int len, bool corner, bool post, bool outer, bool braced, int high)
    {
        if (corner || post || h <= 0 || h == Rail || h >= high - 1) return Mat.Wood;
        if (!outer) return Mat.Daub;
        // the end panels' braces, leaning in toward the middle of the wall
        int inner = len - 2 * T, p = q % PostEvery - 2, last = inner - q - 1;
        if (q < PostEvery && (braced && h < Rail ? p + 2 == h : h > Rail && p + 2 == (h - Rail) * 3 / 4 + 1)) return Mat.Wood;
        if (last < PostEvery - 2 && (braced && h < Rail ? last == h : h > Rail && last == (h - Rail) * 3 / 4 + 1)) return Mat.Wood;
        return Mat.Daub;
    }

    /// <summary>
    /// A voxel of a wall in the band of a building's rectangle, its walls <paramref name="high"/> voxels over the floor of
    /// the storey: where along which wall it is, and laid as the look has it.
    /// </summary>
    public static byte Wall(Look look, byte wallMat, int x, int y, int z, int x0, int z0, int x1, int z1, int floor, bool upper, int high = Shapes.HouseHeight)
    {
        if (look == null) return Shapes.Lay(wallMat, x, y, z);
        bool zWall = z - z0 < T || z1 - z < T;
        int a = zWall ? x - x0 : z - z0, len = zWall ? x1 - x0 + 1 : z1 - z0 + 1;
        bool outer = x == x0 || x == x1 || z == z0 || z == z1;
        return Face(look, wallMat, x, y, z, y - floor, a, len, outer, upper, high);
    }

    /// <summary>
    /// A voxel of a gable over an end wall: of the walls' own material for plain walls; for framed and daubed ones daub
    /// with a king post up its middle and a collar beam across.
    /// </summary>
    static byte Gable(Look look, byte wallMat, int x, int y, int z, int c, int cmid, int top)
    {
        switch (look?.Walls ?? WallStyle.Plain)
        {
            case WallStyle.Rough: return Shapes.Masonry(x >> 1, y >> 1, z >> 1);
            case WallStyle.StoneBase or WallStyle.Frame or WallStyle.Plaster:
                return Math.Abs(c - cmid) <= 1 || y == top + 4 ? Mat.Wood : Mat.Daub;
            default: return Shapes.Lay(wallMat, x, y, z);
        }
    }

    /// <summary>
    /// The roof of a building of this look over the rectangle of its walls, standing on them at <paramref name="top"/>:
    /// slopes a voxel thick from eaves a quarter of a metre out, a voxel up for every two in (every one in for a steep
    /// roof), up to a ridge along the middle; a hipped roof's ends slope as its sides do, a half-hipped one's from
    /// halfway up, over gables below; no eaves out over a side shared with a building beside it, and no hip at a shared
    /// end. The ridge and the first course of the eaves are logs, the rest the covering. Under the slopes over the end
    /// walls the gables go up. What stands on the ridge is set on it. Into the plan, and what holds as an arch into
    /// <paramref name="arched"/>.
    /// </summary>
    public static void Roof(VoxelWorld w, Site site, VoxelList L, VoxelList arched, int x0, int z0, int x1, int z1, int top, byte wallMat, int shared, byte covering, Look look)
    {
        bool alongX = x1 - x0 >= z1 - z0;
        int c0 = alongX ? z0 : x0, c1 = alongX ? z1 : x1, l0 = alongX ? x0 : z0, l1 = alongX ? x1 : z1;
        bool shareC0 = (shared & (alongX ? 1 : 4)) != 0, shareC1 = (shared & (alongX ? 2 : 8)) != 0;
        bool shareL0 = (shared & (alongX ? 4 : 1)) != 0, shareL1 = (shared & (alongX ? 8 : 2)) != 0;
        var form = look.Roof;
        int pitch = form == RoofForm.Steep ? 1 : 2;
        int cmin = c0 - E, cmax = c1 + E, lmin = shareL0 ? l0 : l0 - E, lmax = shareL1 ? l1 : l1 + E;
        int dmax = (cmax - cmin) / 2, K = dmax / pitch, cmid = (c0 + c1) / 2;
        // the course the ends begin to slope from: a hip's from the eaves, a half-hip's from halfway up
        int kh = form == RoofForm.Hip ? 0 : form == RoofForm.HalfHip ? (K + 1) / 2 : int.MaxValue / 4;
        bool hip0 = kh < K && !shareL0, hip1 = kh < K && !shareL1;
        byte cover = covering == 0 ? Mat.Wood : covering;
        int Course(int l, int c)
        {
            int d = Math.Min(c - cmin, cmax - c);
            if (hip0) d = Math.Min(d, l - lmin + pitch * kh);
            if (hip1) d = Math.Min(d, lmax - l + pitch * kh);
            return Math.Min(d / pitch, K);
        }
        (int x, int z) At(int l, int c) => alongX ? (l, c) : (c, l);
        for (int l = lmin; l <= lmax; l++)
            for (int c = cmin; c <= cmax; c++)
            {
                if (c < c0 && shareC0 || c > c1 && shareC1) continue;
                var (x, z) = At(l, c);
                if (!w.InBounds(x, top, z) || site.Forbidden(x, z)) continue;
                int k = Course(l, c), y = top + k;
                // the gable under the slope over an end wall, and the walls going on up under it everywhere (a steep
                // roof's first courses stand a voxel in from one another: under them the room would be open); held with
                // the roof, so that a storey going up takes it down after the slope over it, from the ridge down
                bool overWall = l >= l0 && l <= l1 && c >= c0 && c <= c1 && (l - l0 < T || l1 - l < T || c - c0 < T || c1 - c < T);
                if (overWall)
                    for (int yy = top; yy < y && yy < w.SY - 1; yy++)
                        if (Brushes.Free(w.Get(x, yy, z)))
                        {
                            byte g = Gable(look, wallMat, x, yy, z, c, cmid, top);
                            L.Add(x, yy, z, g);
                            arched.Add(x, yy, z, g);
                        }
                if (y >= w.SY - 1 || !Brushes.Free(w.Get(x, y, z))) continue;
                byte m = k == 0 || k == K ? Mat.Wood : cover;
                L.Add(x, y, z, m);
                arched.Add(x, y, z, m);
            }
        if (look.Crown != RidgeTop.None)
        {
            int lm = (l0 + l1) / 2;
            int ridge = top + K;
            CrownOn(w, site, L, arched, look.Crown, alongX, lm, cmid, ridge, cover, (l, c) => top + Course(l, c));
        }
    }

    /// <summary>
    /// What stands on a roof's ridge in its middle: a belfry — four posts on the slopes, a bell hung from a beam between
    /// them, a little roof of the covering coming to a point; a louvre — a low box of boards with its own little roof; a
    /// spire — a slim square turret of the covering going up to a point. <paramref name="surface"/> tells the roof's
    /// height over each column.
    /// </summary>
    static void CrownOn(VoxelWorld w, Site site, VoxelList L, VoxelList arched, RidgeTop crown, bool alongX, int lm, int cm, int ridge, byte cover, Func<int, int, int> surface)
    {
        (int x, int z) At(int l, int c) => alongX ? (l, c) : (c, l);
        // (all of it held as an arch: so that, a storey going up, it is taken down from its top before the roof it stands on)
        void Put(int l, int y, int c, byte m, bool arch)
        {
            var (x, z) = At(l, c);
            if (!w.InBounds(x, y, z) || site.Forbidden(x, z) || !Brushes.Free(w.Get(x, y, z))) return;
            L.Add(x, y, z, m);
            if (arch) arched.Add(x, y, z, m);
        }
        // a little roof in courses coming to a point from a square r out from the middle, starting at y0
        void Point(int r, int y0, int step)
        {
            for (int k = 0; k <= r; k++)
                for (int dl = -(r - k); dl <= r - k; dl++)
                    for (int dc = -(r - k); dc <= r - k; dc++)
                        if (Math.Max(Math.Abs(dl), Math.Abs(dc)) == r - k || k == r)
                            for (int s = 0; s < step; s++) Put(lm + dl, y0 + k * step + s, cm + dc, k == r ? Mat.Wood : cover, true);
        }
        switch (crown)
        {
            case RidgeTop.Belfry:
            {
                // posts from the slopes to a beam two metres over the ridge; the bell hung from the beam
                const int r = 4, hgt = 13;
                foreach (var (dl, dc) in new[] { (-r, -r), (-r, r), (r, -r), (r, r) })
                    for (int y = surface(lm + dl, cm + dc) + 1; y <= ridge + hgt; y++) Put(lm + dl, y, cm + dc, Mat.Wood, true);
                for (int d = -r; d <= r; d++) { Put(lm + d, ridge + hgt, cm - r, Mat.Wood, true); Put(lm + d, ridge + hgt, cm + r, Mat.Wood, true); Put(lm - r, ridge + hgt, cm + d, Mat.Wood, true); Put(lm + r, ridge + hgt, cm + d, Mat.Wood, true); }
                // the floor of boards over the ridge between the posts; the bell on its stock in the middle of it, its
                // mouth down, its crown up under the beam (it rests on the stock, so that it is taken down from the top
                // as it was put up)
                for (int dl = -r + 1; dl < r; dl++) for (int dc = -r + 1; dc < r; dc++) Put(lm + dl, ridge + 1, cm + dc, Mat.Wood, true);
                for (int y = ridge + 2; y < ridge + 5; y++) Put(lm, y, cm, Mat.Wood, true);
                for (int y = ridge + 5; y < ridge + hgt - 1; y++)
                {
                    int br = y < ridge + 7 ? 2 : y < ridge + 10 ? 1 : 0;
                    for (int dl = -br; dl <= br; dl++) for (int dc = -br; dc <= br; dc++) Put(lm + dl, y, cm + dc, Mat.Bell, true);
                }
                Point(r + 1, ridge + hgt + 1, 1);
                break;
            }
            case RidgeTop.Louvre:
            {
                // a box of boards over the ridge, open between its posts, its own roof over it
                const int r = 3;
                for (int dl = -r; dl <= r; dl++)
                    for (int dc = -r; dc <= r; dc++)
                    {
                        bool rim = Math.Abs(dl) == r || Math.Abs(dc) == r;
                        if (!rim) continue;
                        bool corner = Math.Abs(dl) == r && Math.Abs(dc) == r;
                        for (int y = surface(lm + dl, cm + dc) + 1; y <= ridge + 5; y++)
                            if (corner || y <= ridge + 1 || y == ridge + 5) Put(lm + dl, y, cm + dc, Mat.Wood, true);
                    }
                Point(r + 1, ridge + 6, 1);
                break;
            }
            case RidgeTop.Spire:
            {
                // a square turret of boards up out of the ridge, then its spire, steep, a voxel in for every two up
                const int r = 3;
                for (int dl = -r; dl <= r; dl++)
                    for (int dc = -r; dc <= r; dc++)
                        if (Math.Abs(dl) == r || Math.Abs(dc) == r)
                            for (int y = surface(lm + dl, cm + dc) + 1; y <= ridge + 6; y++) Put(lm + dl, y, cm + dc, y == ridge + 3 ? Mat.Wood : cover, true);
                Point(r + 1, ridge + 7, 2);
                break;
            }
        }
    }

    /// <summary>
    /// What of a building stands over its walls' top, by the most a look's roof rises with what stands on its ridge: a
    /// storey going up takes all of it down first.
    /// </summary>
    public const int HighestRoof = 2 * Shapes.HouseHeight;
}
