using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The greatest spell of three circles, learnt last in each: the earth shaken under the horde, a star brought down out of
/// the sky, and a whirlwind let loose on the field. Each moves the world bodily, as the rest of the valley moves: what
/// is torn loose flies off in pieces on its arcs, what is thrown comes down where it comes down, the star rolls on.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The whirlwinds walking the field now.</summary>
    public readonly List<Whirl> Whirls = new();
    /// <summary>The earth shook: where, and how far (voxels), for whatever shows it.</summary>
    public event Action<float, float, float> Quaked;

    /// <summary>The horde's siege works in a circle — ladders and decks — a cell's worth to a piece, ready to be torn loose.</summary>
    Dictionary<int, List<(int x, int y, int z)>> SiegeWorksIn(float x, float z, float r)
    {
        var pieces = new Dictionary<int, List<(int x, int y, int z)>>();
        int ix = (int)x, iz = (int)z, ri = (int)MathF.Ceiling(r);
        for (int dz = -ri; dz <= ri; dz++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                if (dx * dx + dz * dz > r * r) continue;
                int cx = ix + dx, cz = iz + dz;
                if (cx < 2 || cz < 2 || cx >= World.SX - 2 || cz >= World.SZ - 2) continue;
                for (int y = 4, top = Math.Min(World.SY - 2, World.ColTop(cx, cz)); y < top; y++)
                {
                    byte m = World.Get(cx, y, cz);
                    if (m != Mat.SiegeLadder && m != Mat.Deck) continue;
                    int key = Nav.CellOf(cx, y, cz);
                    if (!pieces.TryGetValue(key, out var list)) pieces[key] = list = new List<(int, int, int)>();
                    list.Add((cx, y, cz));
                }
            }
        return pieces;
    }

    // ------------------------------------------------------------ the earthquake

    /// <summary>
    /// The earth shakes in the circle: the raiders are thrown up and off their feet every way, the horde's ladders and decks
    /// shaken apart into tumbling timber, its engines racked. What is ours stands: the shaking is the old powers', and
    /// they know whose walls these are.
    /// </summary>
    string Shake(float x, float z, float r)
    {
        int torn = 0, thrown = 0, racked = 0;
        foreach (var (key, list) in SiegeWorksIn(x, z, r))
        {
            float a = key * 2.39996f % MathF.Tau, s = (1.5f + key % 3) * VoxelWorld.PerMetre;
            if (Loose(list, MathF.Cos(a) * s, (3 + key % 4) * VoxelWorld.PerMetre, MathF.Sin(a) * s, "землетрясение") != null) torn++;
        }
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Team != Team.Horde || (u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z) > r * r) continue;
            if (Kinds.IsEngine(u.T)) { Hurt(u, null, 120 * Might, "землетрясение"); racked++; continue; }
            if (u.Bound || u.Envoy) continue;
            // the lighter are thrown the higher; every way, as the ground heaves under each
            float k = MathF.Min(1, 15 / u.T.BodyH), a = (u.Id * 2.39996f + (float)Time) % MathF.Tau;
            float s = (1 + u.Id % 4 * .5f) * VoxelWorld.PerMetre * k, up = (4.5f + u.Id % 3) * VoxelWorld.PerMetre * MathF.Sqrt(k);
            if (Fling(u, MathF.Cos(a) * s, up, MathF.Sin(a) * s, "землетрясение") != null) thrown++;
        }
        if (torn > 0) InvalidateFields();
        Quaked?.Invoke(x, z, r);
        Say(torn + thrown + racked == 0 ? "Земля дрогнула впустую: в круге нет орды." : $"Земля дрогнула: сброшено {thrown}, рассыпалось осадных работ {torn}, машин потрясено {racked}.", 1, x, z);
        return null;
    }

    // ------------------------------------------------------------ the falling star

    /// <summary>How far the falling star's blast reaches (voxels), and the size of the rock itself.</summary>
    const float StarBlast = 3 * VoxelWorld.PerMetre, StarRock = 7.5f;

    /// <summary>
    /// A star brought down: a ball of burning rock comes in slantwise out of the west from high over the valley and
    /// strikes the spot. Where it strikes the ground goes up (see <see cref="StarStrikes"/>), and the rock rolls on.
    /// </summary>
    string Fallstar(float x, float z, float r)
    {
        int ix = Math.Clamp((int)x, 2, World.SX - 3), iz = Math.Clamp((int)z, 2, World.SZ - 3);
        if (Site.Forbidden(ix, iz)) return "звезда не падёт на цитадель";
        float ground = World.SurfAt(ix, iz), g = Gravity, t = 1.4f;
        // from high over the valley and a dozen metres to the west, on an arc that ends at the spot — straight down
        // when a hill to the west stands in its way
        float y0 = Math.Min(World.SY - 12, ground + 40 * VoxelWorld.PerMetre), x0 = x - 12 * VoxelWorld.PerMetre;
        float lx = x - x0, ly = ground + StarRock - y0, ll = MathF.Sqrt(lx * lx + ly * ly);
        if (Raycast.Cast(World, x0, y0, z, lx / ll, ly / ll, 0, out var hit, skipVeg: true)
            && MathF.Sqrt((hit.X - x0) * (hit.X - x0) + (hit.Y - y0) * (hit.Y - y0)) < ll - 3 * StarRock) x0 = x;
        float vx = (x - x0) / t, vy = (ground + StarRock - y0 + g * t * t / 2) / t;
        var f = Hurl(Ball(StarRock, Mat.Rock), x0, y0, z, vx, vy, 0, "звезда");
        if (f == null) return "небо занято: слишком много летит";
        f.Star = true;
        f.Burning = f.Voxels.Count;
        return null;
    }

    /// <summary>
    /// The star strikes: a blast where it came down that breaks out a crater and throws the raiders about it, every engine
    /// near it battered, everything that burns about it alight; the rock goes on, rolling, and strikes as a rock.
    /// </summary>
    void StarStrikes(Flier f)
    {
        f.Star = false;
        float r = StarBlast * Might;
        Detonate(f.X, f.Y - f.R * .5f, f.Z, r, 90 * Might, "звезда", always: true);
        foreach (var u in Units)
            if (!u.Dead && Kinds.IsEngine(u.T) && (u.X - f.X) * (u.X - f.X) + (u.Z - f.Z) * (u.Z - f.Z) < 4 * r * r) Hurt(u, null, 300 * Might, "звезда");
        int ix = (int)f.X, iz = (int)f.Z, ri = (int)(r * 1.6f), lit = 0;
        for (int dz = -ri; dz <= ri && lit < 12; dz += 3)
            for (int dx = -ri; dx <= ri && lit < 12; dx += 3)
            {
                int sx = ix + dx, sz = iz + dz;
                if (dx * dx + dz * dz > ri * ri || sx < 0 || sz < 0 || sx >= World.SX || sz >= World.SZ) continue;
                int st = World.SurfAt(sx, sz);
                for (int y = st - 1; y >= st - 4; y--)
                    if (Mat.Burns(World.Get(sx, y, sz)) && Ignite(sx, y, sz)) { lit++; break; }
            }
    }

    // ------------------------------------------------------------ the whirlwind

    /// <summary>A whirlwind let loose at the spot, for ten seconds (more with the strength of faith).</summary>
    string Unleash(float x, float z, float r)
    {
        Whirls.Add(new Whirl { X = x, Z = z, R = r, Until = Time + 10 * Might, Next = Time });
        return null;
    }

    /// <summary>How fast a whirlwind walks, in voxels a second.</summary>
    const float WhirlPace = 3.5f * VoxelWorld.PerMetre;

    /// <summary>
    /// The whirlwinds walk the field, each toward the nearest of the horde, and five times a second take up whoever of
    /// the horde is in them: round and up and out, the lighter the farther. Ladders and decks they pass over come apart
    /// and go up with them; engines are battered. Ours they go round.
    /// </summary>
    void TickWhirls()
    {
        for (int i = Whirls.Count - 1; i >= 0; i--)
        {
            var w = Whirls[i];
            if (Time >= w.Until) { Whirls.RemoveAt(i); continue; }
            if (Time < w.Next) continue;
            const double Beat = .2;
            float dt = (float)(Time - w.Next + Beat);
            w.Next = Time + Beat;
            // toward the nearest raider within forty metres; with none, it drifts west, where the horde comes from
            Unit near = null; float bd = 40 * VoxelWorld.PerMetre;
            foreach (var u in Units)
            {
                if (u.Dead || u.Inside || u.Team != Team.Horde || u.Airborne != null || u.Bound || u.Envoy) continue;
                float d = MathF.Sqrt((u.X - w.X) * (u.X - w.X) + (u.Z - w.Z) * (u.Z - w.Z));
                if (d < bd) { bd = d; near = u; }
            }
            float tx = near != null ? near.X - w.X : -1, tz = near != null ? near.Z - w.Z : MathF.Sin((float)Time * .7f) * .5f;
            float tl = MathF.Max(.01f, MathF.Sqrt(tx * tx + tz * tz));
            // it turns toward its prey as a storm turns, not at once
            w.VX += (tx / tl * WhirlPace - w.VX) * MathF.Min(1, dt * 1.5f);
            w.VZ += (tz / tl * WhirlPace - w.VZ) * MathF.Min(1, dt * 1.5f);
            w.X = Math.Clamp(w.X + w.VX * dt, 4, World.SX - 5);
            w.Z = Math.Clamp(w.Z + w.VZ * dt, 4, World.SZ - 5);
            foreach (var u in Units)
            {
                if (u.Dead || u.Inside || u.Team != Team.Horde || u.Bound || u.Envoy || u.Airborne != null) continue;
                float dx = u.X - w.X, dz = u.Z - w.Z, d = MathF.Sqrt(dx * dx + dz * dz);
                if (d > w.R) continue;
                if (Kinds.IsEngine(u.T)) { Hurt(u, null, 12 * Might, "смерч"); continue; }
                // round it goes and up, and out: the swirl carries it a quarter round and flings it off
                float k = MathF.Min(1, 15 / u.T.BodyH), ox = d > .1f ? dx / d : 1, oz = d > .1f ? dz / d : 0;
                float round = 7 * VoxelWorld.PerMetre * k, outw = 4 * VoxelWorld.PerMetre * k, up = (8 + u.Id % 3) * VoxelWorld.PerMetre * MathF.Sqrt(k);
                Fling(u, -oz * round + ox * outw + w.VX, up, ox * round + oz * outw + w.VZ, "смерч");
            }
            bool tore = false;
            foreach (var (key, list) in SiegeWorksIn(w.X, w.Z, w.R))
            {
                float a = key * 2.39996f % MathF.Tau;
                if (Loose(list, MathF.Cos(a) * 3 * VoxelWorld.PerMetre + w.VX, (7 + key % 4) * VoxelWorld.PerMetre, MathF.Sin(a) * 3 * VoxelWorld.PerMetre + w.VZ, "смерч") != null) tore = true;
            }
            if (tore) InvalidateFields();
        }
    }
}
