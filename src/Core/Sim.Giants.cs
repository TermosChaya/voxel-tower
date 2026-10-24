using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The great ones and their boulders. A hill giant of the horde, and a giant or a tame troll of ours with the lore of
/// boulders, throws in stages that take a while and can be seen coming: it stoops and tears a ball out of the ground
/// before its feet (the hole stays) or takes up a lump of rubble lying near, brings it up, raises it over its head and
/// hurls it high. The boulder flies as anything thrown does and strikes by its weight and speed: whoever it meets is
/// knocked flying and it goes on through them; a wall it meets is broken out about where it struck, as much as its
/// force will break — earth and timber easily, masonry less, dressed and mortared stone less again — in pieces that fly
/// back out of the hole and burst out of the far face; with force to spare it goes on through. On the open ground it
/// knocks out a bowl and bounces on if it came aslant, and at last it rolls (Sim.Rolling). Ours pass our people and
/// our walls by. A hill giant comes in the late waves: it throws at knots of our people in its sight and at the walls
/// on its way, and up against a wall it smashes through a heap at a blow, the pieces flying; whoever stands before it
/// is swatted into the air, it wades a ditch of water up to its chest, and arrows hardly go through its hide.
/// </summary>
public sealed partial class Sim
{
    /// <summary>How far a hill giant throws and how near it must be, voxels; how often it may, seconds.</summary>
    const float HurlFar = 30 * VoxelWorld.PerMetre, HurlNear = 8 * VoxelWorld.PerMetre;
    const double GiantHurlEvery = 7;
    /// <summary>
    /// The stages of a throw, seconds from its start: stooping to the stone and up with it to the hands; raised over the
    /// head; let go; and the thrower itself again.
    /// </summary>
    public const float HurlStoop = .35f, HurlLift = .95f, HurlRaise = 1.75f, HurlLoose = 2f, HurlDone = 2.5f;
    /// <summary>A hill giant's boulder reaches this far from its middle (near a metre across).</summary>
    const float HurledR = 3.5f;
    /// <summary>What a voxel of a boulder weighs, kilograms, whatever it was torn out of.</summary>
    const float BoulderKg = 5;
    /// <summary>The force of a hill giant's blow against a wall, kilojoules: what it breaks out at a blow.</summary>
    const float SmashKj = 85;
    public const string HurledCause = "валун великана", SwatCause = "удар горного великана", ChipsCause = "обломки стены";
    double _giantSaid = -999, _giantWallSaid = -999;

    /// <summary>Boulders the hill giants have hurled, and voxels of ours their boulders and blows have broken, since the start.</summary>
    public int GiantHurled, GiantBroke;
    /// <summary>Called as a boulder is let go: where, and how many voxels it is (for the sound of the heave).</summary>
    public event Action<float, float, float, int> Heaved;

    static bool IsBoulder(Flier f) => f.Cause is HurledCause or BoulderCause;

    /// <summary>Whether one is at a throw: the stone in its hands, or just let go and the arms coming down.</summary>
    public bool Hurling(Unit u) => u.Hefted != null || Time - u.HurlFrom < HurlDone;

    // ------------------------------------------------------------ the hill giant

    /// <summary>
    /// A hill giant each tick, before the rest of what a raider does: at a throw, it stands to it; else, every seven
    /// seconds or so, out of reach of ours and not at a wall, it looks for something to throw at and a stone to throw.
    /// True while it is at a throw (it does nothing else).
    /// </summary>
    bool GiantTick(Unit u, float dt)
    {
        if (Time - _giantSaid > 600)
        {
            _giantSaid = Time;
            Say("Идёт горный великан. Он мечет валуны в стены и в людей и проламывает стены дубиной. Стрелы его почти не ранят: бейте его копьями и огнём.", 3, u);
        }
        if (Hurling(u)) { HurlTick(u); return true; }
        if (Time < u.NextHurl || u.CarveCell >= 0 || u.Band is { State: BandState.Forming }) return false;
        u.NextHurl = Time + 1;
        if (Nearest(u, Team.Player, ReachOf(u) + 10) is { } foe && Dist(u, foe) < ReachOf(u) + foe.T.BodyR + 10) return false;
        if (!GiantAim(u, out float tx, out float ty, out float tz, out var at)) return false;
        u.Facing = MathF.Atan2(tz - u.Z, tx - u.X);
        var stone = HeftBoulder(u, HurledR, HurledCause);
        if (stone == null) { u.NextHurl = Time + 3; return false; }
        u.NextHurl = Time + GiantHurlEvery * (.85f + .3f * (float)Rng.NextDouble());
        BeginHurl(u, stone, tx, ty, tz, at);
        return true;
    }

    /// <summary>
    /// What a hill giant throws at: a knot of ours in its sight within its throw — two or more together, or an archer
    /// up on a wall — the more the likelier; else the first wall of ours on its way to the citadel, low on its face.
    /// </summary>
    bool GiantAim(Unit u, out float tx, out float ty, out float tz, out Unit at)
    {
        at = null;
        float best = 1.5f;
        const float Knot = 3 * VoxelWorld.PerMetre;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Team != Team.Player || o.Airborne != null || o.Captor != null) continue;
            float dx = o.X - u.X, dz = o.Z - u.Z, d = MathF.Sqrt(dx * dx + dz * dz);
            if (d < HurlNear || d > HurlFar) continue;
            float score = 0;
            foreach (var p in Units)
                if (!p.Dead && !p.Inside && p.Team == Team.Player && MathF.Abs(p.X - o.X) < Knot && MathF.Abs(p.Z - o.Z) < Knot && MathF.Abs(p.Y - o.Y) < 16) score++;
            if (o.T.Ranged) score += .5f;
            if (o.Y > World.GroundTop((int)o.X, (int)o.Z) + VoxelWorld.PerMetre) score += 1;
            if (score <= best || !Sight(u, o)) continue;
            best = score; at = o;
        }
        if (at != null) { tx = at.X; ty = at.Y + HurledR; tz = at.Z; return true; }
        return WallAhead(u, out tx, out ty, out tz);
    }

    /// <summary>
    /// The first wall of ours on a raider's way, by its field: the cell it would have to hack into, a man's height up from its
    /// foot. False if there is none within a throw (or it is nearer than a throw starts).
    /// </summary>
    bool WallAhead(Unit u, out float tx, out float ty, out float tz)
    {
        tx = ty = tz = 0;
        if (FieldOf(u.T) is not { } field) return false;
        int c = u.Cell;
        for (int step = 0; step < 90; step++)
        {
            int next = field.Next(Nav, c, out float carve, out _);
            if (next < 0 || next == c) return false;
            int i = Nav.I(next), y = Nav.Y(next), k = Nav.K(next);
            float cx = (i << NavGrid.Shift) + 4, cz = (k << NavGrid.Shift) + 4, d = MathF.Sqrt((cx - u.X) * (cx - u.X) + (cz - u.Z) * (cz - u.Z));
            if (d > HurlFar) return false;
            if (carve > 0)
            {
                int x0 = i << NavGrid.Shift, z0 = k << NavGrid.Shift, y0 = y << NavGrid.Shift;
                for (int yy = y0; yy < y0 + 3 * NavGrid.CS && yy < World.SY; yy++)
                    for (int z = z0; z < z0 + NavGrid.CS; z++)
                        for (int x = x0; x < x0 + NavGrid.CS; x++)
                        {
                            if (!Mat.IsPlayer[World.Get(x, yy, z)]) continue;
                            if (d < HurlNear) return false;
                            tx = cx; tz = cz; ty = Math.Max(yy, y0) + 1.6f * VoxelWorld.PerMetre;
                            return true;
                        }
            }
            c = next;
        }
        return false;
    }

    // ------------------------------------------------------------ a throw, for every great one

    /// <summary>
    /// A stone for a great one to throw, in its hands' reach: a lump of rubble lying near, taken up as it is; else a
    /// ball torn out of the ground before its feet (not from under anything of ours), the hole left; one of ours with
    /// neither to hand takes a stone up all the same. Null when there is none.
    /// </summary>
    Flier HeftBoulder(Unit u, float r, string cause)
    {
        float fx = MathF.Cos(u.Facing), fz = MathF.Sin(u.Facing);
        Flier f = null;
        var lump = RubbleLump(u, r);
        if (lump != null) f = Loose(lump, 0, 0, 0, cause);
        // else a ball pried whole out of the ground before its feet, or a little to either side where it has torn one
        // already: of the spots in reach the one with the most rock in it, just under the turf or a ball deeper, the
        // earth over it thrown aside
        List<(int x, int y, int z)> best = null;
        float most = -1;
        int bx = 0, by = 0, bz = 0, bdeep = 0;
        for (int k = 0; k < 14 && f == null && most < .75f; k++)
        {
            int turn = k % 7;
            float a = u.Facing + (turn == 0 ? 0 : (turn + 1) / 2 * .6f * (turn % 2 == 0 ? -1 : 1)), out_ = u.T.BodyR + r + 1 + (k / 7) * (2 * r + 1);
            int sx = (int)(u.X + MathF.Cos(a) * out_), sz = (int)(u.Z + MathF.Sin(a) * out_), top = -1;
            for (int y = Math.Min(World.SY - 1, (int)u.Y + 4); y > Math.Max(4, (int)u.Y - 6); y--)
            {
                if (!World.InBounds(sx, y, sz)) break;
                byte m = World.Get(sx, y, sz);
                if (m != Mat.Air && !Mat.Passable[m]) { top = y; break; }
            }
            if (top < 0 || top < (int)u.Y - 3) continue;
            for (int deep = 0; deep < 2; deep++)
            {
                int cy = top - (int)MathF.Floor(r) - deep * (int)MathF.Ceiling(r);
                var hole = GroundBall(sx, cy, sz, r);
                if (hole == null || hole.Count < 3.6f * r * r * r) continue;
                int rock = 0;
                foreach (var (x, y, z) in hole) if (Mat.Def[World.Get(x, y, z)].Group is MatGroup.Rock or MatGroup.Stone) rock++;
                float share = rock / (float)hole.Count - deep * .2f;
                if (share > most) { most = share; best = hole; bx = sx; by = cy; bz = sz; bdeep = deep; }
            }
        }
        if (f == null && best != null)
        {
            if (bdeep > 0)
            {
                // the earth over it, dug out with the hands and flung aside
                var over = new List<(int x, int y, int z)>();
                int ri = (int)MathF.Ceiling(r);
                for (int y = by + 1; y <= by + 3 * ri; y++)
                    for (int dz = -ri; dz <= ri; dz++)
                        for (int dx = -ri; dx <= ri; dx++)
                        {
                            int x = bx + dx, z = bz + dz;
                            if (dx * dx + dz * dz > r * r || !World.InBounds(x, y, z) || best.Contains((x, y, z))) continue;
                            byte m = World.Get(x, y, z);
                            if (m != Mat.Air && m != Mat.Water && !Mat.IsPlayer[m] && Breakable(m)) over.Add((x, y, z));
                        }
                float sx0 = bx - u.X, sz0 = bz - u.Z, sl = MathF.Max(1, MathF.Sqrt(sx0 * sx0 + sz0 * sz0));
                Fly(over, bx, by, bz, -sz0 / sl, .3f, sx0 / sl, 0, ChipsCause);
            }
            f = Loose(best, 0, 0, 0, cause);
            if (f != null) Hacked?.Invoke(bx, by, bz, Mat.Rock);
        }
        if (f == null && u.Team == Team.Player)
            f = Hurl(Ball(r, Mat.Rock), u.X + fx * (u.T.BodyR + r), u.Y + r, u.Z + fz * (u.T.BodyR + r), 0, 0, 0, cause);
        if (f == null) return null;
        f.Spin = 0;
        return f;
    }

    /// <summary>A ball of the valley's own ground about a spot, to be torn out; none if anything of ours is near enough above it to come down.</summary>
    List<(int x, int y, int z)> GroundBall(int sx, int sy, int sz, float r)
    {
        int ri = (int)MathF.Ceiling(r);
        for (int y = sy - 3; y <= sy + 8; y++)
            for (int z = sz - 4; z <= sz + 4; z++)
                for (int x = sx - 4; x <= sx + 4; x++)
                    if (World.InBounds(x, y, z) && Mat.IsPlayer[World.Get(x, y, z)]) return null;
        var hole = new List<(int x, int y, int z)>();
        for (int dy = -ri; dy <= ri; dy++)
            for (int dz = -ri; dz <= ri; dz++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    int x = sx + dx, y = sy + dy, z = sz + dz;
                    if (dx * dx + dy * dy + dz * dz > r * r || !World.InBounds(x, y, z) || y < 4) continue;
                    byte m = World.Get(x, y, z);
                    if (m == Mat.Air || m == Mat.Water || Mat.IsPlayer[m] || !Mat.IsNatural[m] || !Breakable(m)) continue;
                    hole.Add((x, y, z));
                }
        return hole;
    }

    /// <summary>A lump of rubble lying within four metres before a great one's feet (what is left of a fallen wall), enough of it for a stone; or null.</summary>
    List<(int x, int y, int z)> RubbleLump(Unit u, float r)
    {
        int ux = (int)u.X, uy = (int)u.Y, uz = (int)u.Z, reach = 4 * VoxelWorld.PerMetre, bd = int.MaxValue;
        (int x, int y, int z) seed = (-1, -1, -1);
        for (int y = Math.Max(2, uy - 4); y < Math.Min(World.SY, uy + 16); y += 2)
            for (int z = uz - reach; z <= uz + reach; z += 2)
                for (int x = ux - reach; x <= ux + reach; x += 2)
                {
                    if (!World.InBounds(x, y, z) || World.Get(x, y, z) is not (Mat.Rubble or Mat.Ruin)) continue;
                    int d = (x - ux) * (x - ux) + (z - uz) * (z - uz) + (y - uy) * (y - uy);
                    if (d < bd) { bd = d; seed = (x, y, z); }
                }
        if (seed.x < 0) return null;
        var lump = new List<(int x, int y, int z)>();
        int ri = (int)MathF.Ceiling(r + .5f);
        for (int dy = -ri; dy <= ri; dy++)
            for (int dz = -ri; dz <= ri; dz++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    int x = seed.x + dx, y = seed.y + dy, z = seed.z + dz;
                    if (dx * dx + dy * dy + dz * dz > (r + .5f) * (r + .5f) || !World.InBounds(x, y, z)) continue;
                    if (World.Get(x, y, z) is Mat.Rubble or Mat.Ruin) lump.Add((x, y, z));
                }
        return lump.Count >= 2.5f * r * r * r ? lump : null;
    }

    /// <summary>
    /// A great one told to throw at a spot (voxels) now, whatever it was at (for the staged scenes and the tests): a
    /// stone hefted as for any throw. False if there is none to be had.
    /// </summary>
    public bool OrderHurl(Unit u, float tx, float ty, float tz)
    {
        if (u.Dead || Hurling(u)) return false;
        u.Facing = MathF.Atan2(tz - u.Z, tx - u.X);
        var stone = HeftBoulder(u, u.T == Kinds.HillGiant ? HurledR : BoulderR, u.Team == Team.Player ? BoulderCause : HurledCause);
        if (stone == null) return false;
        u.NextHurl = Time + GiantHurlEvery;
        BeginHurl(u, stone, tx, ty, tz, null);
        return true;
    }

    /// <summary>A hill giant set to smash its way into a cell beside it (for the staged scenes and the tests).</summary>
    public void SmashInto(Unit u, int cell)
    {
        StartCarve(u, cell);
        u.Path = null;
    }

    /// <summary>A throw begins: the stone is the thrower's to carry, from where it lay, at what it is aimed at.</summary>
    void BeginHurl(Unit u, Flier stone, float tx, float ty, float tz, Unit at)
    {
        u.Hefted = stone;
        u.HurlFrom = Time;
        u.HurlSX = stone.X; u.HurlSY = stone.Y; u.HurlSZ = stone.Z;
        u.HurlTX = tx; u.HurlTY = ty; u.HurlTZ = tz;
        u.HurlAt = at;
        stone.Carrier = u;
        stone.By = u;
        stone.VX = stone.VY = stone.VZ = 0;
        StopCarve(u); StopBuild(u);
        if (u.Team == Team.Player) ReleaseSite(u);
        u.Path = null;
        u.State = UnitState.Siege;
        u.Facing = MathF.Atan2(tz - u.Z, tx - u.X);
    }

    /// <summary>At a throw: it stands to it, turned to what it throws at.</summary>
    void HurlTick(Unit u)
    {
        u.Path = null;
        u.State = UnitState.Siege;
        if (u.Hefted == null) return;
        float tx = u.HurlAt is { Dead: false } at ? at.X : u.HurlTX, tz = u.HurlAt is { Dead: false } at2 ? at2.Z : u.HurlTZ;
        u.Facing = MathF.Atan2(tz - u.Z, tx - u.X);
    }

    /// <summary>
    /// The stone in a great one's hands, through the stages of the throw: in its socket while the thrower stoops to it,
    /// brought up before its belly, raised over its head, swung forward and let go; dropped where it is if the thrower
    /// is felled, thrown or killed meanwhile.
    /// </summary>
    void Carried(Flier f, float dt)
    {
        var u = f.Carrier;
        if (u == null || u.Dead || u.Airborne != null || u.Down || u.Inside || u.Hefted != f || Time < u.ChainedUntil) { Drop(f); return; }
        float t = (float)(Time - u.HurlFrom);
        if (t >= HurlLoose) { LetFly(u, f); return; }
        float fx = MathF.Cos(u.Facing), fz = MathF.Sin(u.Facing), h = u.T.BodyH, r = f.R, out_ = u.T.BodyR + r * .8f;
        // low before it, over its head a little back, and out before it high where it lets go
        float lx = u.X + fx * out_, ly = u.Y + h * .36f, lz = u.Z + fz * out_;
        float ox = u.X - fx * 1.5f, oy = u.Y + h + r * .6f, oz = u.Z - fz * 1.5f;
        float rx = u.X + fx * out_, ry = u.Y + h * .92f, rz = u.Z + fz * out_;
        float x, y, z;
        if (t < HurlLift)
        {
            float k = Smooth(Math.Max(0, t - HurlStoop) / (HurlLift - HurlStoop));
            x = Lerp(u.HurlSX, lx, k); y = Lerp(u.HurlSY, ly, k); z = Lerp(u.HurlSZ, lz, k);
        }
        else if (t < HurlRaise)
        {
            float k = Smooth((t - HurlLift) / (HurlRaise - HurlLift));
            x = Lerp(lx, ox, k); y = Lerp(ly, oy, k) + MathF.Sin(k * MathF.PI) * 2; z = Lerp(lz, oz, k);
        }
        else
        {
            float k = (t - HurlRaise) / (HurlLoose - HurlRaise);
            k *= k;
            x = Lerp(ox, rx, k); y = Lerp(oy, ry, k) + MathF.Sin(k * MathF.PI) * 3; z = Lerp(oz, rz, k);
        }
        if (dt > 0) { f.VX = (x - f.X) / dt; f.VY = (y - f.Y) / dt; f.VZ = (z - f.Z) / dt; }
        f.X = x; f.Y = y; f.Z = z;
        f.Turn += .5f * dt;
    }

    static float Lerp(float a, float b, float k) => a + (b - a) * k;
    static float Smooth(float k) { k = Math.Clamp(k, 0, 1); return k * k * (3 - 2 * k); }

    /// <summary>A stone let fall from a great one's hands where it is.</summary>
    static void Drop(Flier f)
    {
        if (f.Carrier is { } u && u.Hefted == f) u.Hefted = null;
        f.Carrier = null;
        f.VX = f.VZ = 0; f.VY = 0;
    }

    /// <summary>
    /// The stone away: from the thrower's hands, high on its arc and down on its mark — the one aimed at where it stands
    /// now — astray to the sides and short or long by half a metre or so, the farther the more.
    /// </summary>
    void LetFly(Unit u, Flier f)
    {
        u.Hefted = null;
        f.Carrier = null;
        if (u.HurlAt is { Dead: false, Inside: false } at) { u.HurlTX = at.X; u.HurlTY = at.Y + f.R; u.HurlTZ = at.Z; }
        float dx = u.HurlTX - f.X, dz = u.HurlTZ - f.Z, d = MathF.Sqrt(dx * dx + dz * dz);
        float spread = VoxelWorld.PerMetre * (.2f + d / HurlFar * .6f);
        float tx = u.HurlTX + (float)(Rng.NextDouble() - .5) * 2 * spread, tz = u.HurlTZ + (float)(Rng.NextDouble() - .5) * 2 * spread;
        float ty = u.HurlTY + (float)(Rng.NextDouble() - .5) * spread;
        float t = .75f + d / (24 * VoxelWorld.PerMetre);
        f.VX = (tx - f.X) / t; f.VZ = (tz - f.Z) / t; f.VY = (ty - f.Y + .5f * Gravity * t * t) / t;
        float hs = MathF.Sqrt(f.VX * f.VX + f.VZ * f.VZ);
        if (hs > .1f) { f.AX = -f.VZ / hs; f.AY = 0; f.AZ = f.VX / hs; }
        f.Spin = 2.4f;
        f.Since = Time;
        f.By = u;
        u.Swing = 1;
        if (u.Team == Team.Player) Hurled++; else GiantHurled++;
        Heaved?.Invoke(f.X, f.Y, f.Z, f.Voxels.Count);
    }

    // ------------------------------------------------------------ the boulder strikes

    /// <summary>The force a flying piece comes with, kilojoules: half its weight by its speed squared.</summary>
    static float Kj(Flier f, float speed)
    {
        float v = speed / VoxelWorld.PerMetre;
        return .5f * f.Voxels.Count * BoulderKg * v * v / 1000;
    }

    /// <summary>
    /// What it takes to break a voxel out with a heavy blow, kilojoules: grass and leaves nothing, loose rubble and earth
    /// little, timber more, masonry more again (mortared and dressed stone more still), the rock of the valley most;
    /// the bedrock, the citadel and the black stone it does not break (infinity), nor, for one of ours, anything of ours.
    /// Zero for what it passes through.
    /// </summary>
    float BreakCost(byte m, bool spareOurs)
    {
        if (m == Mat.Air || m == Mat.Water || m == Mat.Pitch || Mat.Passable[m]) return 0;
        if (Mat.Def[m].Group == MatGroup.Hard) return float.PositiveInfinity;
        if (spareOurs && Mat.IsPlayer[m]) return float.PositiveInfinity;
        if (Mat.IsVeg[m] || Mat.IsLeafy(m)) return .02f;
        if (m is Mat.Rubble or Mat.Ruin or Mat.Lumber) return .1f;
        if (m is Mat.Deck or Mat.SiegeLadder or Mat.TowerLadder or Mat.TrapDeck) return .08f;
        return Mat.Def[m].Group switch
        {
            MatGroup.Earth => .16f,
            MatGroup.Wood => .22f,
            MatGroup.Stone => .2f / (StoneHack * HideHack) * (Knows("granite") ? 1.3f : 1),
            _ => .8f,
        };
    }

    /// <summary>
    /// A boulder strikes something on its way. Whoever it meets is struck as hard as it comes and knocked flying, and it
    /// goes on through them (an engine it is checked by). Into the ground it knocks out a bowl, and bounces on if it came
    /// aslant and hard (twice at most). Into anything else it breaks out what its force will break, about where it
    /// struck and on along its way, the pieces flying; with force to spare it goes on through. True while it flies on;
    /// false when it comes down here (and rolls, or lies).
    /// </summary>
    bool BoulderStrikes(Flier f, float speed)
    {
        bool ours = f.Cause == BoulderCause;
        float s = MathF.Max(.01f, speed), ux = f.VX / s, uy = f.VY / s, uz = f.VZ / s, kj = Kj(f, speed);
        // bodies in its way
        float slow = 1;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Airborne != null || u == f.By || Spares(f, u) || f.Struck != null && f.Struck.Contains(u)) continue;
            float dx = u.X - f.X, dz = u.Z - f.Z, dy = u.Y + u.T.BodyH * .5f - f.Y, reach = f.R + u.T.BodyR;
            if (dx * dx + dz * dz > reach * reach || MathF.Abs(dy) > u.T.BodyH * .5f + f.R || !Touches(f, u, .5f)) continue;
            (f.Struck ??= new List<Unit>()).Add(u);
            bool engine = Kinds.IsEngine(u.T);
            // a man struck by a third of a ton at a run is broken; a troll or a giant takes it and stands
            Hurt(u, f.By, Math.Min(engine ? 260 : 420, kj * 3.2f) * (engine ? 1.5f : 1), f.Cause);
            if (engine) { slow *= .35f; continue; }
            float light = MathF.Min(1, 15 / u.T.BodyH), out_ = speed * .5f * light;
            Fling(u, ux * out_, MathF.Max(2.5f * VoxelWorld.PerMetre, out_ * .45f), uz * out_, f.Cause);
            slow *= .82f;
        }
        if (slow < 1) { f.VX *= slow; f.VY *= slow; f.VZ *= slow; }
        if (!Embedded(f)) return true;
        // what it is in: the ground of the valley only, or water only, or something else
        var (ax, ay, az) = AxisOf(f);
        float c = MathF.Cos(f.Turn), sn = MathF.Sin(f.Turn);
        int ground = 0, other = 0, water = 0;
        foreach (var (vx, vy, vz, _) in f.Voxels)
        {
            var (px, py, pz) = Turned(vx, vy, vz, ax, ay, az, c, sn);
            int x = (int)MathF.Floor(f.X + px), y = (int)MathF.Floor(f.Y + py), z = (int)MathF.Floor(f.Z + pz);
            if (!World.InBounds(x, y, z)) { ground++; continue; }
            byte m = World.Get(x, y, z);
            if (m == Mat.Air || Mat.Passable[m] && m != Mat.Water) continue;
            if (m == Mat.Water) water++;
            else if (Mat.IsNatural[m] && !Mat.IsVeg[m] && !Mat.IsTrunk(m) && !Mat.IsLeafy(m) && Mat.Def[m].Group != MatGroup.Wood) ground++;
            else other++;
        }
        // into water: it goes under
        if (ground == 0 && other == 0) return water == 0;
        int ix = (int)MathF.Floor(f.X + ux * f.R * .8f), iy = (int)MathF.Floor(f.Y + uy * f.R * .8f), iz = (int)MathF.Floor(f.Z + uz * f.R * .8f);
        if (other == 0)
        {
            // the ground: a bowl knocked out of its earth, and on it bounces if it came aslant and hard
            Break(f, ix, iy, iz, ux, uy, uz, kj * .2f, ours, groundOnly: true, out _);
            if (f.Bounces < 2 && uy > -.8f && speed > 7 * VoxelWorld.PerMetre)
            {
                f.Bounces++;
                for (int k = 0; k < 10 && Embedded(f); k++) { f.X -= ux * .7f; f.Y += .7f; f.Z -= uz * .7f; }
                f.VY = MathF.Abs(f.VY) * .4f + 1.5f * VoxelWorld.PerMetre;
                f.VX *= .6f; f.VZ *= .6f;
                Landed?.Invoke(f.X, f.Y, f.Z, f.Voxels.Count, speed);
                return true;
            }
            return false;
        }
        int broken = Break(f, ix, iy, iz, ux, uy, uz, kj, ours, groundOnly: false, out float left);
        if (broken > 0)
        {
            Collapsed?.Invoke(Math.Max(broken, 40), ix, iy, iz);
            Landed?.Invoke(f.X, f.Y, f.Z, f.Voxels.Count, speed);
        }
        // through, with force to spare: on it goes, the slower for what it broke
        if (left > kj * .2f && !Embedded(f))
        {
            float k = MathF.Sqrt(left / kj);
            f.VX *= k; f.VY *= k; f.VZ *= k;
            return true;
        }
        // spent: it falls at the foot of what it struck
        f.VX *= .1f; f.VZ *= .1f; f.VY = Math.Min(0, f.VY) * .2f;
        return false;
    }

    /// <summary>
    /// Breaks out what a heavy blow at a spot breaks, going along a way: the voxels about the spot and on along the way
    /// beyond it, the nearest first (a bowl into the face, deepening along the way), each as its stuff takes, as far as
    /// the force goes; none past what it cannot break. The ground only, if so told, and that of earth. The pieces fly:
    /// back out of the hole, and on out of the far side what bursts through. Powder in it goes up. Returns how many
    /// voxels it broke, and the force left over.
    /// </summary>
    int Break(Flier f, int ix, int iy, int iz, float ux, float uy, float uz, float kj, bool spareOurs, bool groundOnly, out float left)
    {
        left = kj;
        float lat = f.R + 1.5f;
        const int Deep = 12;
        int ri = (int)MathF.Ceiling(MathF.Max(lat, Deep));
        var cand = new List<(float key, int x, int y, int z, float cost)>();
        for (int dy = -ri; dy <= ri; dy++)
            for (int dz = -ri; dz <= ri; dz++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    float along = dx * ux + dy * uy + dz * uz;
                    if (along < -2 || along > (groundOnly ? f.R : Deep)) continue;
                    float l2 = dx * dx + dy * dy + dz * dz - along * along;
                    if (l2 > lat * lat) continue;
                    int x = ix + dx, y = iy + dy, z = iz + dz;
                    if (!World.InBounds(x, y, z) || y < 3) continue;
                    byte m = World.Get(x, y, z);
                    if (m == Mat.Air || m == Mat.Water) continue;
                    if (m == Mat.Powder) { Fuse(x, y, z, .2, "порох"); continue; }
                    bool natural = Mat.IsNatural[m] && !Mat.IsVeg[m] && !Mat.IsTrunk(m) && !Mat.IsLeafy(m);
                    if (groundOnly && (!natural || !Mat.IsEarthy[m])) continue;
                    // the ground about a wall's foot only where it struck, not along under the wall
                    if (!groundOnly && natural && dx * dx + dy * dy + dz * dz > f.R * f.R) continue;
                    float cost = BreakCost(m, spareOurs);
                    if (cost <= 0) continue;
                    cand.Add((MathF.Sqrt(l2) + MathF.Max(0, along) * .45f, x, y, z, cost));
                }
        cand.Sort((a, b) => a.key.CompareTo(b.key));
        var broke = new List<(int x, int y, int z)>();
        bool keep = false;
        foreach (var (_, x, y, z, cost) in cand)
        {
            if (float.IsPositiveInfinity(cost))
            {
                // the citadel takes it on its own stone
                if (!keep && MathF.Sqrt((x - Site.KeepX) * (x - Site.KeepX) + (z - Site.KeepZ) * (z - Site.KeepZ)) < Site.KeepR + 6)
                {
                    keep = true;
                    KeepHp -= Math.Min(16, left * .2f) * KeepGuard * KeepHurt;
                }
                left *= .5f;
                continue;
            }
            if (left < cost) break;
            left -= cost;
            broke.Add((x, y, z));
        }
        if (broke.Count == 0) return 0;
        int ourBroke = 0;
        foreach (var (x, y, z) in broke) if (Mat.IsPlayer[World.Get(x, y, z)]) ourBroke++;
        if (f.Cause == HurledCause) GiantBroke += ourBroke;
        Fly(broke, ix, iy, iz, ux, uy, uz, MathF.Sqrt(left / MathF.Max(1, kj)), ChipsCause);
        if (ourBroke >= 60 && Time - _giantWallSaid > 40 && f.Cause == HurledCause)
        {
            _giantWallSaid = Time;
            Say("Горный великан разбивает стену валунами. Выйдите к нему с копьями или жгите его, издали его не остановить.", 3, ix, iz);
        }
        return broke.Count;
    }

    /// <summary>
    /// Voxels knocked out by a blow fly off in pieces: those short of the middle of the blow back out of the hole, up
    /// and aside; those beyond it on out of the far face, with what force is left (a share, 0…1). What there is no room
    /// in the air for is gone in dust.
    /// </summary>
    void Fly(List<(int x, int y, int z)> voxels, int ix, int iy, int iz, float ux, float uy, float uz, float spare, string cause)
    {
        int room = MaxAloft - Aloft;
        foreach (var piece in Split(voxels, 25))
        {
            float cx = 0, cy = 0, cz = 0;
            foreach (var (px, py, pz) in piece) { cx += px; cy += py; cz += pz; }
            cx = cx / piece.Count - ix; cy = cy / piece.Count - iy; cz = cz / piece.Count - iz;
            float along = cx * ux + cy * uy + cz * uz, sx = cx - along * ux, sz = cz - along * uz, sl = MathF.Max(.5f, MathF.Sqrt(sx * sx + sz * sz));
            float r1 = (float)Rng.NextDouble(), m = VoxelWorld.PerMetre;
            float vx, vy, vz;
            if (along > 3)
            {
                float on = (2.5f + 8 * spare + 2 * r1) * m;
                vx = ux * on + sx / sl * m; vy = MathF.Max(0, uy) * on + (1.5f + 2 * r1) * m; vz = uz * on + sz / sl * m;
            }
            else
            {
                float back = (1.5f + 2.5f * r1) * m, side = (1.5f + 2 * r1) * m;
                vx = -ux * back + sx / sl * side; vy = (3 + 3 * r1) * m; vz = -uz * back + sz / sl * side;
            }
            var p = piece.Count <= room ? Loose(piece, vx, vy, vz, cause, room) : null;
            if (p != null) { room -= p.Voxels.Count; continue; }
            foreach (var (px, py, pz) in piece)
            {
                byte was = World.Get(px, py, pz);
                World.Set(px, py, pz, Mat.Air);
                if (Orders.At(px, py, pz) != 0) Orders.Clear(px, py, pz);
                Burning.Remove(WaterPlanner.Pack(px, py, pz));
                if (Rng.NextDouble() < .15) Fell?.Invoke(px, py, pz, was);
            }
        }
    }

    // ------------------------------------------------------------ the hill giant at a wall, and hand to hand

    /// <summary>
    /// A hill giant at what stands in its way does not hack at it: every blow of its club breaks out a heap of it at
    /// once, the nearest first as far as the blow's force goes, and the pieces fly on out of the far side. Done when
    /// the way is open.
    /// </summary>
    void GiantSmash(Unit u)
    {
        u.State = UnitState.Carve;
        int target = u.CarveCell;
        float wx = (Nav.I(target) << NavGrid.Shift) + 4, wz = (Nav.K(target) << NavGrid.Shift) + 4;
        u.Facing = MathF.Atan2(wz - u.Z, wx - u.X);
        if (u.Cooldown > 0) return;
        u.Cooldown = u.T.AttackTime * (.9f + .2f * (float)Rng.NextDouble());
        u.Swing = 1;
        float left = SmashKj;
        var broke = new List<(int x, int y, int z)>();
        while (u.CarveI < u.CarveList.Count)
        {
            var (x, y, z) = u.CarveList[u.CarveI];
            byte m = World.Get(x, y, z);
            if (!Blocks(m)) { u.CarveI++; continue; }
            // what a shaman cursed gives twice as easily, under a stone skin it holds three times as long
            float cost = BreakCost(m, false) / ((Curses.Count > 0 ? CurseHack(x, y, z) : 1) * (Skins.Count > 0 ? SkinHack(x, y, z) : 1));
            if (float.IsPositiveInfinity(cost)) { StopCarve(u); return; }
            if (left < cost) break;
            left -= cost;
            u.CarveI++;
            broke.Add((x, y, z));
        }
        if (broke.Count > 0)
        {
            int ours = 0;
            foreach (var (x, y, z) in broke) if (Mat.IsPlayer[World.Get(x, y, z)]) ours++;
            GiantBroke += ours;
            var (hx, hy, hz) = broke[0];
            Hacked?.Invoke(hx, hy, hz, World.Get(hx, hy, hz));
            float dx = wx - u.X, dz = wz - u.Z, d = MathF.Max(1, MathF.Sqrt(dx * dx + dz * dz));
            Fly(broke, (int)u.X, (int)(u.Y + u.T.BodyH * .5f), (int)u.Z, dx / d, 0, dz / d, .4f, ChipsCause);
            Collapsed?.Invoke(broke.Count, hx, hy, hz);
        }
        if (u.CarveI >= u.CarveList.Count || broke.Count > 0 && WayOpen(u)) StopCarve(u);
    }

    /// <summary>
    /// A hill giant's blow: the one it struck flies, and those of ours beside it before the giant are swept off their
    /// feet and thrown too, hurt the less. An engine stands.
    /// </summary>
    void Swat(Unit a, Unit b)
    {
        float fx = MathF.Cos(a.Facing), fz = MathF.Sin(a.Facing), reach = ReachOf(a) + 4;
        foreach (var o in Units)
        {
            if (o != b && o.Dead || o.Inside || o.Airborne != null || o.Team == a.Team || Kinds.IsEngine(o.T) || o.ChainedUntil > Time) continue;
            float dx = o.X - a.X, dz = o.Z - a.Z, d = MathF.Sqrt(dx * dx + dz * dz);
            if (o != b && (d > reach + o.T.BodyR || dx * fx + dz * fz < .35f * d || MathF.Abs(o.Y - a.Y) > 16)) continue;
            if (o != b) Hurt(o, a, a.T.Damage * .35f, SwatCause);
            float light = MathF.Min(1, 15 / o.T.BodyH), ux = d > .5f ? dx / d : fx, uz = d > .5f ? dz / d : fz;
            Fling(o, ux * GreatOut * 1.15f * light, GreatUp * light, uz * GreatOut * 1.15f * light, SwatCause);
        }
    }

    /// <summary>How much of a shot gets through a hill giant's hide: a fifth of an arrow, a quarter of a sling stone, under half a bolt.</summary>
    static float HideShare(Unit t, Shot kind) =>
        t.T != Kinds.HillGiant ? 1 : kind switch { Shot.Arrow => .2f, Shot.Stone => .25f, Shot.Bolt => .45f, _ => 1 };
}
