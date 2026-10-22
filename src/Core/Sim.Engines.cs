using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The horde's engines. A ram goes the horde's way like any raider of its band, only slowly and through everything:
/// walls and gates give way to it like nothing else, and at the citadel it batters. A catapult goes its own way to
/// where it throws from, out of bowshot of the first of our building between it and the citadel, and throws stones
/// that break up walls and towers a few hands of voxels at a time and hurt whoever they come down on; two of the horde
/// keep by it as its guard. A mantlet is pushed before its band's leader, and the arrows loosed at those behind it
/// strike it instead. A siege tower rolls up to the first of our walls on its band's way and drops a ladder against it
/// inside its timbers that no one can throw down; the horde goes up it onto the wall, and when the tower is burnt or
/// broken the ladder goes with it. Arrows hardly hurt an engine; swords do, and fire most of all.
/// </summary>
public sealed partial class Sim
{
    /// <summary>How far a catapult throws, and how near it comes to what it throws at, in voxels.</summary>
    const float ThrowFar = 40 * VoxelWorld.PerMetre, ThrowFrom = 34 * VoxelWorld.PerMetre;
    /// <summary>How much a stone breaks out where it strikes, as a ball this many voxels across the middle.</summary>
    const int StoneR = 5;
    double _stoneSaid = -99;

    /// <summary>What of a blow or an arrow an engine feels: little of an arrow, more of a blade.</summary>
    public static float EngineShare(bool arrow) => arrow ? .2f : .6f;

    void EngineTick(Unit u, float dt)
    {
        u.Target = null;
        if (u.T == Kinds.Catapult) { CatapultTick(u, dt); return; }
        if (u.T == Kinds.Mantlet) { MantletTick(u, dt); return; }
        if (u.T == Kinds.SiegeTower) { TowerTick(u, dt); return; }
        March(u, dt);
    }

    /// <summary>A mantlet: before its band's leader, a little toward the citadel; with the band gone, where it stands.</summary>
    void MantletTick(Unit u, float dt)
    {
        var lead = u.Band?.Leader;
        if (lead == null || lead.Dead) { u.State = UnitState.Idle; return; }
        float dx = Site.KeepX - lead.X, dz = Site.KeepZ - lead.Z, d = MathF.Max(1, MathF.Sqrt(dx * dx + dz * dz));
        float tx = lead.X + dx / d * 14, tz = lead.Z + dz / d * 14;
        float far = MathF.Sqrt((tx - u.X) * (tx - u.X) + (tz - u.Z) * (tz - u.Z));
        u.Facing = MathF.Atan2(dz, dx);
        if (far < 6) { u.State = UnitState.Idle; u.Path = null; return; }
        if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
        {
            u.NextSearch = Time + 1;
            int spot = NearestStand((int)tx, (int)tz, u.T.Move.Height);
            if (spot < 0 || !GoTo(u, spot, adjacent: true, budget: 6000)) { u.Path = null; u.State = UnitState.Idle; return; }
        }
        Walk(u, dt);
        u.Facing = MathF.Atan2(dz, dx);
    }

    /// <summary>
    /// Whatever stands between an archer of ours and the one it shoots at, near enough before it to take the arrow:
    /// a mantlet, or null.
    /// </summary>
    Unit Cover(Unit archer, Unit target)
    {
        if (Kinds.IsEngine(target.T)) return null;
        float lx = target.X - archer.X, lz = target.Z - archer.Z, len2 = lx * lx + lz * lz;
        if (len2 < 1) return null;
        foreach (var o in Units)
        {
            if (o.Dead || o.T != Kinds.Mantlet || o == target) continue;
            float ox = o.X - archer.X, oz = o.Z - archer.Z;
            float t = (ox * lx + oz * lz) / len2;
            if (t <= 0 || t >= 1) continue;
            float before = (1 - t) * MathF.Sqrt(len2);
            if (before > 4 * VoxelWorld.PerMetre) continue;
            float px = archer.X + lx * t - o.X, pz = archer.Z + lz * t - o.Z;
            if (px * px + pz * pz < (o.T.BodyR + 4) * (o.T.BodyR + 4)) return o;
        }
        return null;
    }

    /// <summary>
    /// A siege tower: to the first of our walls on its band's way to the citadel that it is tall enough for, up to the
    /// foot of its face; there it drops its ladder against it and stands.
    /// </summary>
    void TowerTick(Unit u, float dt)
    {
        if (u.Docked.Count > 0) { u.State = UnitState.Siege; u.Path = null; return; }
        if (u.AimAt < 0 || Time >= u.NextLook)
        {
            u.NextLook = Time + 5;
            u.AimAt = DockFor(u);
        }
        if (u.AimAt < 0) { u.State = UnitState.Idle; return; }
        var (wx, wy, wz) = WaterPlanner.Unpack(u.AimAt);
        float dx = wx + .5f - u.X, dz = wz + .5f - u.Z, d = MathF.Sqrt(dx * dx + dz * dz);
        // as near as the wall lets it: its own bulk before the face, and a cell's give
        if (d < u.T.BodyR + 2 * NavGrid.CS + 4) { Dock(u, wx, wy, wz); return; }
        if (u.Path == null || u.PathI >= u.Path.Count)
        {
            if (Time < u.NextSearch) { u.State = UnitState.Idle; return; }
            u.NextSearch = Time + 2;
            // to the ground right before the face, or failing that wherever is nearest there
            int nx = Math.Sign(-dx) * (Math.Abs(dx) > Math.Abs(dz) ? 1 : 0), nz = Math.Sign(-dz) * (Math.Abs(dz) >= Math.Abs(dx) ? 1 : 0);
            int spot = DockSpot(u, wx, wy, wz, nx, nz);
            if (spot < 0) spot = NearestStand(wx + nx * 10, wz + nz * 10, u.T.Move.Height);
            if (spot < 0 || !GoTo(u, spot, adjacent: true, budget: 60000)) { u.Path = null; u.AimAt = -1; u.State = UnitState.Idle; return; }
        }
        Walk(u, dt);
    }

    /// <summary>
    /// Where a siege tower stands to drop its ladder on a face (packed voxel at its foot) whose outside looks toward
    /// (nx, nz): the cell on the ground before it nearest the face — not one up a tree or on the wall's top, nor behind
    /// it. −1 if there is none within a few metres.
    /// </summary>
    int DockSpot(Unit u, int wx, int wy, int wz, int nx, int nz)
    {
        int h = u.T.Move.Height, ci = wx >> NavGrid.Shift, cy = wy >> NavGrid.Shift, ck = wz >> NavGrid.Shift, best = -1;
        float most = float.MaxValue;
        for (int y = cy - 2; y <= cy + 2; y++)
            for (int k = ck - 3; k <= ck + 3; k++)
                for (int i = ci - 3; i <= ci + 3; i++)
                {
                    if (!Nav.Stand(i, y, k, h)) continue;
                    int c = Nav.Id(i, y, k);
                    var (fx, fy, fz) = Nav.Feet(c);
                    float ox = fx - (wx + .5f), oz = fz - (wz + .5f), d = ox * ox + oz * oz;
                    if (ox * nx + oz * nz <= 0 || Math.Abs(fy - wy) > NavGrid.CS || d >= most) continue;
                    most = d; best = c;
                }
        return best;
    }

    /// <summary>
    /// The wall a siege tower makes for: on its line to the citadel, the first column of our building between two and
    /// five and a half metres above the ground before it. Packed voxel of the face, at the ground; −1 for none.
    /// </summary>
    int DockFor(Unit u)
    {
        float dx = Site.KeepX - u.X, dz = Site.KeepZ - u.Z, len = MathF.Sqrt(dx * dx + dz * dz);
        if (len < 1) return -1;
        dx /= len; dz /= len;
        int lastX = (int)u.X, lastZ = (int)u.Z;
        for (float s = 4; s < len - Site.KeepR; s += 2)
        {
            int x = (int)(u.X + dx * s), z = (int)(u.Z + dz * s);
            if (!World.InBounds(x, 1, z)) continue;
            int top = World.SurfAt(x, z), ground = World.SurfAt(lastX, lastZ);
            byte m = World.Get(x, top - 1, z);
            if (Mat.IsPlayer[m] && !Mat.Passable[m] && top - ground >= 2 * VoxelWorld.PerMetre)
                return top - ground <= 44 ? WaterPlanner.Pack(x, ground, z) : -1;
            lastX = x; lastZ = z;
        }
        return -1;
    }

    /// <summary>Docked: the tower's ladder against the face, from the ground to over the top; the tower stands there.</summary>
    void Dock(Unit u, int wx, int wy, int wz)
    {
        u.Path = null;
        // the face toward the tower
        int nx = 0, nz = 0;
        float dx = u.X - (wx + .5f), dz = u.Z - (wz + .5f);
        if (MathF.Abs(dx) > MathF.Abs(dz)) nx = Math.Sign(dx); else nz = Math.Sign(dz);
        int fx = wx, fz = wz;
        // the face itself: the outermost voxel of the wall toward the tower, at the height of its foot
        for (int k = 0; k < 12 && World.Get(fx + nx, wy + 4, fz + nz) is var m && m != Mat.Air && !Mat.Passable[m]; k++) { fx += nx; fz += nz; }
        var hit = new VoxelHit { X = fx, Y = wy + 4, Z = fz, NX = nx, NZ = nz, M = World.Get(fx, wy + 4, fz) };
        var L = Shapes.LadderAt(World, Site, hit, Mat.TowerLadder, crush: true);
        for (int i = 0; i < L.Count; i++)
        {
            World.Set(L.X(i), L.Y(i), L.Z(i), Mat.TowerLadder);
            u.Docked.Add(WaterPlanner.Pack(L.X(i), L.Y(i), L.Z(i)));
        }
        if (L.Count == 0) { u.AimAt = -1; u.NextLook = Time + 5; return; }
        u.State = UnitState.Siege;
        u.Facing = MathF.Atan2(-dz, -dx);
        Nav.Flush();
        InvalidateFields();
        Say("Осадная башня у стены: по её лестнице орда полезет наверх. Её не сбросить — только сжечь или разбить.", 3, u);
    }

    /// <summary>A siege tower burnt or broken: its ladder comes down with it.</summary>
    void TowerFell(Unit u)
    {
        foreach (int p in u.Docked)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            if (World.Get(x, y, z) == Mat.TowerLadder) World.Set(x, y, z, Mat.Air);
        }
        u.Docked.Clear();
        Nav.Flush();
        InvalidateFields();
    }

    /// <summary>A catapult: to where it throws from, then stone after stone at what it aims at.</summary>
    void CatapultTick(Unit u, float dt)
    {
        if (u.AimAt < 0 || Time >= u.NextLook || World.Get(WaterPlanner.Unpack(u.AimAt).x, WaterPlanner.Unpack(u.AimAt).y, WaterPlanner.Unpack(u.AimAt).z) == Mat.Air)
        {
            u.NextLook = Time + 6;
            u.AimAt = AimFor(u);
        }
        var (ax, ay, az) = WaterPlanner.Unpack(u.AimAt);
        float dx = ax - u.X, dz = az - u.Z, d = MathF.Sqrt(dx * dx + dz * dz);
        if (d <= ThrowFar)
        {
            u.Path = null;
            u.State = UnitState.Siege;
            u.Facing = MathF.Atan2(dz, dx);
            if (u.Cooldown <= 0) { Throw(u, ax, ay, az, d); u.Cooldown = u.T.AttackTime * (.9f + (float)Rng.NextDouble() * .2f); }
            return;
        }
        // on toward it, to a place out of bowshot of it: along the line to it, nearer if the way there cannot be found
        if (u.Path == null || u.PathI >= u.Path.Count)
        {
            if (Time < u.NextSearch) { u.State = UnitState.Idle; return; }
            u.NextSearch = Time + 3;
            bool found = false;
            for (float from = ThrowFrom; from > 12 * VoxelWorld.PerMetre && !found; from -= 6 * VoxelWorld.PerMetre)
            {
                int spot = NearestStand((int)(ax - dx / d * from), (int)(az - dz / d * from), u.T.Move.Height);
                found = spot >= 0 && GoTo(u, spot, adjacent: true, budget: 60000);
            }
            if (!found) { u.State = UnitState.Idle; return; }
        }
        Walk(u, dt);
    }

    /// <summary>
    /// What a catapult throws at: the first of our building on its line to the citadel, a little under its top; else
    /// the citadel's own wall on the side it faces. Packed voxel.
    /// </summary>
    int AimFor(Unit u)
    {
        float dx = Site.KeepX - u.X, dz = Site.KeepZ - u.Z, len = MathF.Sqrt(dx * dx + dz * dz);
        if (len < 1) return WaterPlanner.Pack(Site.KeepX, World.SurfAt(Site.KeepX, Site.KeepZ) - 8, Site.KeepZ);
        dx /= len; dz /= len;
        for (float s = 8; s < len - Site.KeepR; s += 3)
        {
            int x = (int)(u.X + dx * s), z = (int)(u.Z + dz * s);
            if (!World.InBounds(x, 1, z)) continue;
            int top = World.SurfAt(x, z);
            byte m = World.Get(x, top - 1, z);
            if (!Mat.IsPlayer[m] || Mat.Passable[m] || top - World.GroundTop(x, z) < VoxelWorld.PerMetre) continue;
            return WaterPlanner.Pack(x, Math.Max(1, top - 6), z);
        }
        int kx = (int)(Site.KeepX - dx * Site.KeepR), kz = (int)(Site.KeepZ - dz * Site.KeepR);
        return WaterPlanner.Pack(kx, Math.Max(1, World.SurfAt(kx, kz) - 10), kz);
    }

    /// <summary>The stones a catapult throws: rock reaching this many voxels from the middle (two thirds of a metre across).</summary>
    const float StoneBall = 2.6f;

    /// <summary>
    /// A stone away: a ball of rock off the end of the arm, high over on its arc and down on its mark, give or take a metre
    /// or two the farther it goes. It flies as anything thrown does, and strikes whatever is in its way.
    /// </summary>
    void Throw(Unit u, int ax, int ay, int az, float d)
    {
        u.Swing = 1;
        // it goes astray to the sides and high or low, the farther the more; a stone that flies short or long only strikes
        // the face lower or higher
        float spread = VoxelWorld.PerMetre * (.6f + d / ThrowFar * 1.4f), side = (float)(Rng.NextDouble() - .5) * 2 * spread;
        float lx = d > 1 ? (ax - u.X) / d : 1, lz = d > 1 ? (az - u.Z) / d : 0;
        float tx = ax + .5f - lz * side, ty = ay + .5f + (float)(Rng.NextDouble() - .5) * 1.4f * spread, tz = az + .5f + lx * side;
        // off the end of the arm, over the engine's head toward its mark
        float fx = MathF.Cos(u.Facing), fz = MathF.Sin(u.Facing);
        float x0 = u.X + fx * 4, y0 = u.Y + u.T.BodyH + 4, z0 = u.Z + fz * 4;
        // so long in the air that it comes down where it was meant to
        float t = 1.6f + d / (30 * VoxelWorld.PerMetre);
        float vx = (tx - x0) / t, vz = (tz - z0) / t, vy = (ty - y0 + .5f * Gravity * t * t) / t;
        var f = Hurl(Ball(StoneBall, Mat.Rock), x0, y0, z0, vx, vy, vz, "камень из камнемёта");
        if (f == null) return;
        f.Smash = StoneR;
        f.By = u;
    }

    /// <summary>
    /// A stone comes down: on whatever it struck. It breaks out what is built in a ball round where it strikes (and a
    /// little of the ground), hurts the citadel if it is the citadel, and those it comes down on and beside; then it lies
    /// there, broken, with what it broke. Powder it falls on goes up.
    /// </summary>
    int StoneLands(Flier f, float speed)
    {
        // where it touched: its leading edge
        float s = MathF.Max(1, speed);
        int x = (int)(f.X + f.VX / s * f.R * .8f), y = (int)(f.Y + f.VY / s * f.R * .8f), z = (int)(f.Z + f.VZ / s * f.R * .8f);
        if (!World.InBounds(x, y, z)) return 0;
        // dressed stone gives up less to it
        int broken = 0, r = Knows("granite") ? Math.Max(2, f.Smash - 1) : f.Smash;
        for (int dy = -r; dy <= r; dy++)
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int r2 = dx * dx + dy * dy + dz * dz;
                    if (r2 > r * r) continue;
                    int px = x + dx, py = y + dy, pz = z + dz;
                    byte m = World.Get(px, py, pz);
                    if (m == Mat.Air || m == Mat.Water || m == Mat.Pitch) continue;
                    if (m == Mat.Powder) { Fuse(px, py, pz, .2, "порох"); continue; }
                    bool built = Mat.IsPlayer[m] || m is Mat.Rubble or Mat.Lumber or Mat.Ruin || Mat.Swings(m) || Mat.IsDecor[m];
                    bool ground = Mat.IsEarthy[m] && r2 <= 6;
                    if (!built && !ground) continue;
                    World.Set(px, py, pz, Mat.Air);
                    if (Orders.IsRemove(Orders.At(px, py, pz))) Orders.Clear(px, py, pz);
                    Burning.Remove(WaterPlanner.Pack(px, py, pz));
                    if (Rng.NextDouble() < .12) Fell?.Invoke(px, py, pz, m);
                    broken++;
                }
        // the citadel takes it on its own stone
        float kd = MathF.Sqrt((x - Site.KeepX) * (x - Site.KeepX) + (z - Site.KeepZ) * (z - Site.KeepZ));
        if (kd < Site.KeepR + 6) KeepHp -= 10 * KeepGuard * KeepHurt;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o == f.By || o.Airborne != null) continue;
            float ddx = o.X - x, ddz = o.Z - z, dd = MathF.Sqrt(ddx * ddx + ddz * ddz);
            if (dd > 14 || MathF.Abs(o.Y - y) > 16) continue;
            Hurt(o, f.By, 38 * (1 - dd / 18) * (Kinds.IsEngine(o.T) ? .5f : 1), "камень из камнемёта");
        }
        Collapsed?.Invoke(Math.Max(broken, 30), x, y, z);
        if (broken > 0 && kd >= Site.KeepR + 6 && Time - _stoneSaid > 20)
        {
            _stoneSaid = Time;
            Say("Камнемёт бьёт по стенам: выбивает кладку. Сожгите его огненными стрелами или выйдите на него.", 3, x, z);
        }
        return broken;
    }

    /// <summary>A raider keeping by an engine: by it, and at whoever of ours comes near it; with the engine gone, a raider like any.</summary>
    void KeepEscort(Unit u)
    {
        if (u.Escort == null) return;
        if (u.Escort.Dead) { u.Escort = null; u.Guard = false; u.Home = -1; return; }
        u.Home = u.Escort.Cell;
    }

    /// <summary>A catapult comes in: where its band comes in, with two of the horde to guard it, and goes its own way.</summary>
    Unit SpawnCatapult(Warband b)
    {
        int at = b.Entry >= 0 ? Spread(b.Entry, b.Spawned + 2, Kinds.Catapult.Move.Height) : -1;
        var c = at >= 0 ? Spawn(Kinds.Catapult, at) : SpawnRaider(Kinds.Catapult);
        if (c == null) return null;
        foreach (var t in new[] { Kinds.Orc, Kinds.Goblin })
        {
            int g = Spread(c.Cell, 2, t.Move.Height);
            var guard = Spawn(t, g >= 0 ? g : c.Cell);
            guard.Escort = c; guard.Guard = true; guard.Home = c.Cell;
        }
        Say("С ватагой идёт камнемёт с охраной: он встанет за пределами выстрела и будет разбивать стены.", 3, c);
        return c;
    }
}
