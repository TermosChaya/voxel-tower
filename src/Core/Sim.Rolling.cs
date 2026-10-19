using System;

namespace VoxelTower.Core;

/// <summary>
/// What is round rolls. A ball that comes down — a catapult's stone, a ball of earth or stone the hand of telekinesis tore
/// out — does not stop where it struck: it rolls on the way it was going and down whatever slope there is, the steeper
/// the faster, held back all the while by what it rolls on (grass and earth more than stone or planks). A step it comes
/// at fast enough it climbs; off a wall it is turned back; over an edge it goes and falls, and comes down on whatever is
/// below. Whoever it runs over is struck, the heavier and faster the harder, and knocked aside. Where it stops, on the
/// level or nearly, it lies whole, as anything that flew lies.
/// </summary>
public sealed partial class Sim
{
    /// <summary>How much of its going a ball keeps, turned back off a wall or an engine.</summary>
    const float Rebound = .25f;
    /// <summary>The most a ball keeps of its going along the ground when it comes down, metres a second.</summary>
    const float RollOnAt = 4;
    /// <summary>How long a ball may roll, seconds: then it stops wherever it is.</summary>
    const double RollFor = 30;

    /// <summary>
    /// Whether a piece is round enough to roll, and how far it reaches from its middle as a ball: about as wide every way,
    /// with no corners standing out beyond its round, and its voxels gathered about its middle as a ball's are, not out
    /// toward the corners as a block's.
    /// </summary>
    static bool IsRound(Flier f, out float r)
    {
        r = 0;
        int n = f.Voxels.Count;
        if (n < 24 || f.Unit != null) return false;
        int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
        double sx = 0, sy = 0, sz = 0;
        foreach (var (dx, dy, dz, _) in f.Voxels)
        {
            x0 = Math.Min(x0, dx); x1 = Math.Max(x1, dx); y0 = Math.Min(y0, dy); y1 = Math.Max(y1, dy); z0 = Math.Min(z0, dz); z1 = Math.Max(z1, dz);
            sx += dx; sy += dy; sz += dz;
        }
        int hi = Math.Max(x1 - x0, Math.Max(y1 - y0, z1 - z0)) + 1, lo = Math.Min(x1 - x0, Math.Min(y1 - y0, z1 - z0)) + 1;
        r = hi * .5f;
        if (hi > lo * 1.3f) return false;
        // how far its voxels lie from its middle, on the whole (a ball's three fifths of its radius squared, a block's
        // all of it) and at the farthest (a ball's corner no farther than its round, a block's three quarters again)
        double mx = sx / n, my = sy / n, mz = sz / n, m2 = 0, far = 0, bx = (x0 + x1) * .5, by = (y0 + y1) * .5, bz = (z0 + z1) * .5;
        foreach (var (dx, dy, dz, _) in f.Voxels)
        {
            m2 += (dx - mx) * (dx - mx) + (dy - my) * (dy - my) + (dz - mz) * (dz - mz);
            far = Math.Max(far, (dx - bx) * (dx - bx) + (dy - by) * (dy - by) + (dz - bz) * (dz - bz));
        }
        double k = m2 / n / (r * r);
        return k > .45 && k < .78 && far <= r * r * 1.17;
    }

    /// <summary>Whether a voxel holds up what rolls or falls on it: not air, water or anything one walks through.</summary>
    bool SolidAt(int x, int y, int z)
    {
        if (!World.InBounds(x, y, z)) return y < 1;
        byte m = World.Get(x, y, z);
        return m != Mat.Air && m != Mat.Water && !Mat.Passable[m];
    }

    /// <summary>
    /// How high the middle of a ball (of a radius, voxels) comes to rest over a spot: on the highest of the solid voxels
    /// under and about it, touching it, as a ball rests on the edge of a step or down in a hollow. Only what is below its
    /// middle as it is now counts (a wall is there below it too); with nothing there at all, −∞.
    /// </summary>
    float RestOver(float x, float z, float y, float r)
    {
        float best = float.NegativeInfinity, r2 = r * r;
        int x0 = (int)MathF.Floor(x - r), x1 = (int)MathF.Floor(x + r), z0 = (int)MathF.Floor(z - r), z1 = (int)MathF.Floor(z + r);
        int top = Math.Min(World.SY - 1, (int)MathF.Floor(y)), bottom = Math.Max(0, (int)MathF.Floor(y - r - 3));
        for (int vz = z0; vz <= z1; vz++)
        {
            float dz = z < vz ? vz - z : z > vz + 1 ? z - vz - 1 : 0;
            for (int vx = x0; vx <= x1; vx++)
            {
                float dx = x < vx ? vx - x : x > vx + 1 ? x - vx - 1 : 0, d2 = dx * dx + dz * dz;
                if (d2 >= r2) continue;
                // the edge of the world is a wall
                if (vx < 1 || vz < 1 || vx >= World.SX - 1 || vz >= World.SZ - 1) return float.PositiveInfinity;
                for (int vy = top; vy >= bottom; vy--)
                    if (SolidAt(vx, vy, vz)) { best = MathF.Max(best, vy + 1 + MathF.Sqrt(r2 - d2)); break; }
            }
        }
        return best;
    }

    /// <summary>Whether a ball (of a radius, voxels) about a point is clear of anything solid, give or take a little.</summary>
    bool BallFree(float x, float y, float z, float r)
    {
        float r2 = r * r * .97f;
        int x0 = (int)MathF.Floor(x - r), x1 = (int)MathF.Floor(x + r), y0 = (int)MathF.Floor(y - r), y1 = (int)MathF.Floor(y + r);
        int z0 = (int)MathF.Floor(z - r), z1 = (int)MathF.Floor(z + r);
        for (int vy = y0; vy <= y1; vy++)
        {
            float dy = y < vy ? vy - y : y > vy + 1 ? y - vy - 1 : 0;
            for (int vz = z0; vz <= z1; vz++)
            {
                float dz = z < vz ? vz - z : z > vz + 1 ? z - vz - 1 : 0;
                for (int vx = x0; vx <= x1; vx++)
                {
                    float dx = x < vx ? vx - x : x > vx + 1 ? x - vx - 1 : 0;
                    if (dx * dx + dy * dy + dz * dz < r2 && SolidAt(vx, vy, vz)) return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// A ball come down rolls on: turning about the middle of its round, out of what it went into (back the way it came),
    /// with what the blow left of its going along the ground (a share of it) and none of it up or down.
    /// </summary>
    void StartRoll(Flier f, float r, float keep)
    {
        int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
        foreach (var (dx, dy, dz, _) in f.Voxels)
        {
            x0 = Math.Min(x0, dx); x1 = Math.Max(x1, dx); y0 = Math.Min(y0, dy); y1 = Math.Max(y1, dy); z0 = Math.Min(z0, dz); z1 = Math.Max(z1, dz);
        }
        int sx = (int)MathF.Floor((x0 + x1) * .5f), sy = (int)MathF.Floor((y0 + y1) * .5f), sz = (int)MathF.Floor((z0 + z1) * .5f);
        if (sx != 0 || sy != 0 || sz != 0)
        {
            for (int i = 0; i < f.Voxels.Count; i++)
            {
                var (dx, dy, dz, m) = f.Voxels[i];
                f.Voxels[i] = ((sbyte)(dx - sx), (sbyte)(dy - sy), (sbyte)(dz - sz), m);
            }
            // its middle moves to the middle of its round, the voxels staying where they are
            var (ax, ay, az) = AxisOf(f);
            var (wx, wy, wz) = Turned(sx, sy, sz, ax, ay, az, MathF.Cos(f.Turn), MathF.Sin(f.Turn));
            f.X += wx; f.Y += wy; f.Z += wz;
        }
        float s = MathF.Sqrt(f.VX * f.VX + f.VY * f.VY + f.VZ * f.VZ);
        float ux = s > .01f ? f.VX / s : 0, uy = s > .01f ? f.VY / s : -1, uz = s > .01f ? f.VZ / s : 0;
        for (int k = 0; k < r * 3 + 6 && !BallFree(f.X, f.Y, f.Z, r); k++) { f.X -= ux * .35f; f.Y -= uy * .35f; f.Z -= uz * .35f; }
        for (int k = 0; k < r * 2 + 6 && !BallFree(f.X, f.Y, f.Z, r); k++) f.Y += .5f;
        float hx = f.VX * keep, hz = f.VZ * keep, hs = MathF.Sqrt(hx * hx + hz * hz), cap = RollOnAt * VoxelWorld.PerMetre;
        if (hs > cap) { hx *= cap / hs; hz *= cap / hs; }
        f.VX = hx; f.VY = 0; f.VZ = hz;
        f.Spin = 0;
        f.Rolling = true; f.RollFrom = Time; f.RollR = r;
    }

    /// <summary>How much what a ball rolls on holds it back, as a share of its weight: heaps of rubble most, then earth and
    /// grass, planks and timber less, stone least.</summary>
    float Grip(Flier f)
    {
        int x = (int)MathF.Floor(f.X), z = (int)MathF.Floor(f.Z), y = (int)MathF.Floor(f.Y - f.RollR - .5f);
        if (!World.InBounds(x, y, z)) return .12f;
        byte m = World.Get(x, y, z);
        if (m == Mat.Air && y > 0) m = World.Get(x, y - 1, z);
        if (m is Mat.Rubble or Mat.Lumber or Mat.Char) return .25f;
        return Mat.Def[m].Group switch { MatGroup.Earth => .12f, MatGroup.Wood => .07f, MatGroup.Stone or MatGroup.Rock or MatGroup.Hard => .05f, _ => .12f };
    }

    /// <summary>
    /// A ball rolling, a tick of it: down the slope under it, held back by what it rolls on; up a step it comes at fast
    /// enough, back off what it cannot climb; off an edge it falls, and comes down on whatever is below. Whoever it runs
    /// over is struck and knocked aside. True once it has stopped and lies there (or is gone out of the world).
    /// </summary>
    bool Roll(Flier f, float dt)
    {
        float r = f.RollR;
        float speed = MathF.Sqrt(f.VX * f.VX + f.VY * f.VY + f.VZ * f.VZ);
        int steps = Math.Clamp((int)MathF.Ceiling(speed * dt / .5f), 1, 40);
        float h = dt / steps, e = MathF.Max(.75f, r * .3f), push = 0, mx = 0, mz = 0;
        bool onGround = false;
        float Ground(float x, float z) { float g = RestOver(x, z, f.Y, r); return float.IsNegativeInfinity(g) ? f.Y - r - 3 : MathF.Min(g, f.Y + r); }
        for (int i = 0; i < steps; i++)
        {
            int cx = (int)MathF.Floor(f.X), cy = (int)MathF.Floor(f.Y), cz = (int)MathF.Floor(f.Z);
            bool wet = World.InBounds(cx, cy, cz) && World.Get(cx, cy, cz) == Mat.Water;
            onGround = f.Y <= RestOver(f.X, f.Z, f.Y, r) + .05f;
            if (onGround)
            {
                f.VY = 0;
                // down the slope under it: the steeper the faster, as a ball goes down a slope
                float gx = (Ground(f.X + e, f.Z) - Ground(f.X - e, f.Z)) / (2 * e), gz = (Ground(f.X, f.Z + e) - Ground(f.X, f.Z - e)) / (2 * e);
                float q = 1 + gx * gx + gz * gz, k = 5f / 7 * Gravity / q;
                f.VX -= k * gx * h; f.VZ -= k * gz * h;
                push = 5f / 7 * MathF.Sqrt(gx * gx + gz * gz) / q;
                float hs = MathF.Sqrt(f.VX * f.VX + f.VZ * f.VZ), drag = Grip(f) * Gravity * h * (wet ? 6 : 1);
                if (hs <= drag) f.VX = f.VZ = 0;
                else { f.VX *= 1 - drag / hs; f.VZ *= 1 - drag / hs; }
            }
            else
            {
                f.VY -= Gravity * h;
                // water holds it back: it sinks slowly
                if (wet) { f.VY = MathF.Max(f.VY, -2 * VoxelWorld.PerMetre); f.VX *= .9f; f.VZ *= .9f; }
            }
            // along each way in turn: up what it can climb, turned back off what it cannot
            float px = f.X, pz = f.Z;
            float nx = f.X + f.VX * h;
            if (RestOver(nx, f.Z, f.Y, r) > f.Y + MathF.Max(.4f, 3 * MathF.Abs(f.VX * h))) f.VX = -f.VX * Rebound; else f.X = nx;
            float nz = f.Z + f.VZ * h;
            if (RestOver(f.X, nz, f.Y, r) > f.Y + MathF.Max(.4f, 3 * MathF.Abs(f.VZ * h))) f.VZ = -f.VZ * Rebound; else f.Z = nz;
            mx += f.X - px; mz += f.Z - pz;
            if (f.X < 3 || f.Z < 3 || f.X >= World.SX - 3 || f.Z >= World.SZ - 3) return true;
            // and up or down: along the ground, or falling till it comes down on something
            float g1 = RestOver(f.X, f.Z, f.Y, r);
            if (float.IsPositiveInfinity(g1)) g1 = f.Y;
            if (onGround && g1 >= f.Y - .6f) f.Y = g1;
            else
            {
                float ny = f.Y + f.VY * h;
                if (ny > g1) f.Y = ny;
                else
                {
                    float hard = -f.VY;
                    f.Y = g1;
                    if (hard > 2 * VoxelWorld.PerMetre) { Pound(f, hard); f.VX *= .8f; f.VZ *= .8f; }
                    f.VY = hard > 4 * VoxelWorld.PerMetre ? hard * .15f : 0;
                }
            }
            if (f.Y < 2) return true;
        }
        // turned as a ball turns, as far as it went
        float moved = MathF.Sqrt(mx * mx + mz * mz);
        if (moved > 1e-3f) TurnMore(f, mz / moved, 0, -mx / moved, moved / r);
        // whoever it runs into
        float hs1 = MathF.Sqrt(f.VX * f.VX + f.VZ * f.VZ);
        if (hs1 > VoxelWorld.PerMetre)
            foreach (var u in Units)
                if (!u.Dead && !u.Inside && u.Airborne == null && Time - u.LastHurt >= .5 && Against(f, u)) RunOver(f, u, hs1);
        // all but still, on the level or as near it as holds it there, or held where it is against something: it lies
        // where it stopped
        f.Still = moved < .02f ? f.Still + dt : 0;
        if (onGround && (hs1 < .1f * VoxelWorld.PerMetre && push < Grip(f) * 1.05f || f.Still > .6f) || Time - f.RollFrom > RollFor)
        {
            f.Rolling = false;
            f.VX = f.VY = f.VZ = 0;
            return Lay(f, (int)MathF.Floor(f.X), (int)MathF.Floor(f.Y), (int)MathF.Floor(f.Z));
        }
        return false;
    }

    /// <summary>Whether a ball is against a body: its round within a hand of it.</summary>
    static bool Against(Flier f, Unit u)
    {
        float reach = f.RollR + u.T.BodyR * .8f + .5f, dx = u.X - f.X, dz = u.Z - f.Z, d2 = dx * dx + dz * dz;
        if (d2 > reach * reach) return false;
        float dy = f.Y - Math.Clamp(f.Y, u.Y, u.Y + u.T.BodyH);
        return d2 + dy * dy <= reach * reach;
    }

    /// <summary>A ball comes down hard on whatever is under it: whoever is there is struck.</summary>
    void Pound(Flier f, float hard)
    {
        float blow = Blow(f, hard);
        if (blow > 3)
            foreach (var u in Units)
                if (!u.Dead && !u.Inside && u.Airborne == null && Time - u.LastHurt >= .5 && Against(f, u))
                    Hurt(u, null, blow * (Kinds.IsEngine(u.T) ? 1.5f : 1), f.Cause);
        Landed?.Invoke(f.X, f.Y, f.Z, f.Voxels.Count, hard);
    }

    /// <summary>
    /// A ball runs into one: struck as hard as it comes, the heavier the harder. One not too big for it is knocked aside
    /// and down, and the ball goes on the slower for it; an engine stands, and turns it back.
    /// </summary>
    void RunOver(Flier f, Unit u, float speed)
    {
        bool engine = Kinds.IsEngine(u.T);
        Hurt(u, null, Blow(f, speed) * (engine ? 1.5f : 1), f.Cause);
        if (engine) { f.VX = -f.VX * Rebound; f.VZ = -f.VZ * Rebound; return; }
        // a body's weight against the ball's, as voxels of it
        float body = u.T.BodyR * u.T.BodyR * u.T.BodyH * 1.5f, n = f.Voxels.Count;
        if (n > body * .3f && speed > 2.5f * VoxelWorld.PerMetre)
        {
            float dx = u.X - f.X, dz = u.Z - f.Z, d = MathF.Max(.5f, MathF.Sqrt(dx * dx + dz * dz)), side = VoxelWorld.PerMetre;
            Fling(u, f.VX * .6f + dx / d * side, 2.5f * VoxelWorld.PerMetre, f.VZ * .6f + dz / d * side, f.Cause);
        }
        float keep = n / (n + body * .5f);
        f.VX *= keep; f.VZ *= keep;
    }

    /// <summary>
    /// Turns a flying thing on, about a further axis (of length one) through its middle, as a ball turns as it rolls: the
    /// turn it had and the new one made one, about an axis that may lean any way.
    /// </summary>
    static void TurnMore(Flier f, float bx, float by, float bz, float angle)
    {
        var (ax, ay, az) = AxisOf(f);
        float s1 = MathF.Sin(f.Turn * .5f), qw = MathF.Cos(f.Turn * .5f), qx = ax * s1, qy = ay * s1, qz = az * s1;
        float s2 = MathF.Sin(angle * .5f), pw = MathF.Cos(angle * .5f), px = bx * s2, py = by * s2, pz = bz * s2;
        // the new turn after the old, as quaternions are made one
        float w = pw * qw - (px * qx + py * qy + pz * qz);
        float x = pw * qx + qw * px + (py * qz - pz * qy);
        float y = pw * qy + qw * py + (pz * qx - px * qz);
        float z = pw * qz + qw * pz + (px * qy - py * qx);
        float l = MathF.Sqrt(w * w + x * x + y * y + z * z);
        w /= l; x /= l; y /= l; z /= l;
        if (w < 0) { w = -w; x = -x; y = -y; z = -z; }
        float sv = MathF.Sqrt(x * x + y * y + z * z);
        f.Turn = 2 * MathF.Atan2(sv, w);
        if (sv > 1e-6f) { f.AX = x / sv; f.AY = y / sv; f.AZ = z / sv; }
    }
}
