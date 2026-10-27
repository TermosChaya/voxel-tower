using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// What stands about a held point of a valley, as a siege counts it (Fortification.cs): how much of the way round it a
/// wall shuts it in and how high, the towers over it, the gates in it, a ditch before it (with water, a moat), the traps,
/// the engines of ours among its keepers; and what all of it is worth to them (<see cref="Score"/>, 0 to
/// <see cref="Fortification.Most"/>), the share their strength is raised by behind it.
/// </summary>
public readonly struct Fort
{
    /// <summary>The share of the way round the point a wall stands across (0 to 1), and its height there, metres.</summary>
    public readonly float Ring, High;
    public readonly int Towers, Gates, Traps, Engines;
    /// <summary>The share of the way round a ditch is dug before the wall, and whether water stands in it.</summary>
    public readonly float Ditch;
    public readonly bool Moat;
    /// <summary>A citadel stands at the point: its keep shelters those who hold it.</summary>
    public readonly bool Keep;
    public readonly float Score;

    public Fort(float ring, float high, int towers, int gates, float ditch, bool moat, int traps, int engines, bool keep)
    {
        Ring = ring; High = high; Towers = towers; Gates = gates; Ditch = ditch; Moat = moat; Traps = traps; Engines = engines; Keep = keep;
        Score = Fortification.ScoreOf(ring, high, towers, gates, ditch, moat, traps, keep);
    }

    /// <summary>What stands, said: «стена кольцом на 90 %, 3,5 м · 2 башни · ворота · ров с водой».</summary>
    public string Words()
    {
        var parts = new List<string>();
        if (Keep) parts.Add("цитадель");
        if (Ring >= .05f) parts.Add(Ring >= .95f ? $"стена кругом, {Fmt.One(High)} м" : $"стена на {Ring * 100:0} % круга, {Fmt.One(High)} м");
        if (Towers > 0) parts.Add(Towers == 1 ? "башня" : $"башен {Towers}");
        if (Gates > 0) parts.Add(Gates == 1 ? "ворота" : $"ворот {Gates}");
        if (Ditch >= .1f) parts.Add(Moat ? "ров с водой" : "ров");
        if (Traps > 0) parts.Add("ловушки");
        if (Engines > 0) parts.Add(Engines == 1 ? "машина" : $"машин {Engines}");
        return parts.Count == 0 ? "укреплений нет" : string.Join(" · ", parts);
    }
}

/// <summary>
/// The works that make a few men hard to take (docs/handoff/13-world-map.md, holding a point), measured from the valley
/// as it stands: from the point (the citadel, or where its keepers stand) the ground is looked along in many ways out to
/// the reach, and what of ours stands across each way — masonry or timber at least a man and a half high — makes the
/// ring; the columns of it standing well over the ring are its towers; the gates within the reach, the ground dug down
/// before it, the traps laid, the engines. A whole ring of good height is the most of it; towers, a gate to come and go
/// by, a ditch and traps add. Rams, the hill giant and sappers among those who come take it down again (QuickBattle.cs).
/// </summary>
public static class Fortification
{
    /// <summary>How much all of it can be worth: a keeper's strength raised so many times over, and one more.</summary>
    public const float Most = 5;
    /// <summary>How far round a point it is looked for (24 m), round a citadel (40 m); from how far out (3 m: the point itself is not its wall).</summary>
    public const float PointReach = 24 * VoxelWorld.PerMetre, KeepReach = 40 * VoxelWorld.PerMetre, Inner = 3 * VoxelWorld.PerMetre;
    /// <summary>The height a wall must stand to bar a way (1½ m), and how much higher than the ring a column stands to be a tower's (1½ m more, and at least 5 m).</summary>
    public const int Bars = 12, TowerOver = 12, TowerLeast = 40;
    /// <summary>How far before a wall a ditch is looked for (4 m).</summary>
    public const int DitchOut = 4 * VoxelWorld.PerMetre;
    /// <summary>The ways looked along, round the point.</summary>
    public const int Ways = 48;

    /// <summary>
    /// The worth of the works: the ring by how much of it stands and how high (a whole ring 3 m high 2½, higher a little
    /// more; gaps let the enemy in, so half a ring is a quarter of it), each tower up to three 0.4, gates in a ring 0.2,
    /// a ditch 0.6 all round (with water 1), traps 0.3; a citadel's keep 0.6 by itself.
    /// </summary>
    public static float ScoreOf(float ring, float high, int towers, int gates, float ditch, bool moat, int traps, bool keep)
    {
        float s = ring * ring * Math.Clamp(high / 3f, 0, 1.3f) * 2.5f;
        s += .4f * Math.Min(towers, 3);
        if (gates > 0 && ring >= .5f) s += .2f;
        s += ditch * (moat ? 1f : .6f);
        if (traps > 0) s += .3f;
        if (keep) s += .6f;
        return Math.Clamp(s, 0, Most);
    }

    /// <summary>The point a valley is held at: its citadel, else where its fighters of a side stand, else its middle.</summary>
    public static (int x, int z) PointOf(Sim sim, Team team)
    {
        var s = sim.Site;
        if (s.HasKeep) return (s.KeepX, s.KeepZ);
        float x = 0, z = 0;
        int n = 0;
        foreach (var u in sim.Units)
            if (!u.Dead && u.Team == team && Fighter(u) && !u.Inside) { x += u.X; z += u.Z; n++; }
        return n > 0 ? ((int)(x / n), (int)(z / n)) : (s.KeepX, s.KeepZ);
    }

    /// <summary>One who fights for a point: a soldier, a commander, an engine's crew, a worker called to arms; not a worker at his work, nor a priest.</summary>
    public static bool Fighter(Unit u) => !u.Dead && u.T != Kinds.Risen && !u.T.Prays && (u.T.Work < 1 || u.Militia);

    /// <summary>What stands about the point a valley is held at by a side (<see cref="PointOf"/>).</summary>
    public static Fort Measure(Sim sim, Team team = Team.Player)
    {
        var (px, pz) = PointOf(sim, team);
        return Measure(sim, px, pz, team);
    }

    /// <summary>What stands about a point of a valley, measured from the voxels.</summary>
    public static Fort Measure(Sim sim, int px, int pz, Team team = Team.Player)
    {
        var w = sim.World; var s = sim.Site;
        bool keep = s.HasKeep && Math.Abs(px - s.KeepX) < 8 && Math.Abs(pz - s.KeepZ) < 8;
        int reach = (int)(keep ? KeepReach : PointReach), inner = (int)(keep ? s.KeepR + Inner : Inner);
        // a column's built height over its ground, of what of ours bars the way (masonry, timber, iron)
        int Built(int x, int z)
        {
            if (!w.InBounds(x, 1, z)) return 0;
            int top = w.SurfAt(x, z), ground = w.GroundTop(x, z);
            if (top <= ground) return 0;
            byte m = w.Get(x, top - 1, z);
            return Mat.IsPlayer[m] && !Mat.IsDecor[m] && !Mat.Passable[m] ? top - ground : 0;
        }
        // the ways round the point: the first wall across each, and the ditch before it
        int barred = 0, dug = 0, wet = 0;
        var heights = new List<int>();
        for (int k = 0; k < Ways; k++)
        {
            double a = k * Math.Tau / Ways, dx = Math.Cos(a), dz = Math.Sin(a);
            int wall = 0, beyond = -1;
            bool ditch = false, water = false;
            for (int r = inner; r <= reach + DitchOut && (beyond < 0 || r <= beyond); r++)
            {
                int x = px + (int)Math.Round(dx * r), z = pz + (int)Math.Round(dz * r);
                if (!w.InBounds(x, 1, z)) break;
                int h = Built(x, z);
                if (wall == 0 && r <= reach && h >= Bars) { wall = h; beyond = r + DitchOut; continue; }
                // ground dug down a man's height and more under what it was, inside the wall or just before it
                if (s.HTop != null && s.HTop[z * w.SX + x] - w.GroundTop(x, z) >= Bars)
                {
                    ditch = true;
                    if (w.Get(x, w.GroundTop(x, z), z) == Mat.Water) water = true;
                }
            }
            if (wall > 0) { barred++; heights.Add(wall); if (ditch) dug++; if (water) wet++; }
        }
        float ring = barred / (float)Ways, high = 0;
        if (heights.Count > 0) { heights.Sort(); high = heights[heights.Count / 2] / (float)VoxelWorld.PerMetre; }
        // the towers: columns standing over the ring, a tower's worth of them each (about 2 m across)
        int ringH = (int)(high * VoxelWorld.PerMetre), over = Math.Max(TowerLeast, ringH + TowerOver), tall = 0, traps = 0;
        for (int z = pz - reach; z <= pz + reach; z++)
            for (int x = px - reach; x <= px + reach; x++)
            {
                int ddx = x - px, ddz = z - pz;
                if (ddx * ddx + ddz * ddz > reach * reach || !w.InBounds(x, 1, z)) continue;
                if (keep && ddx * ddx + ddz * ddz < inner * inner) continue;
                if (Built(x, z) >= over) tall++;
                int g = w.GroundTop(x, z);
                byte top = w.Get(x, g, z);
                if (top is Mat.Stakes or Mat.TrapDeck or Mat.DropStone) traps++;
            }
        int towers = Math.Min(4, tall / 160);
        int gates = 0;
        foreach (var gt in sim.Gates)
        {
            if (gt.Broken) continue;
            var (gx, _, gz) = gt.Middle;
            if ((gx - px) * (gx - px) + (gz - pz) * (gz - pz) <= reach * reach) gates++;
        }
        int engines = 0;
        foreach (var u in sim.Units) if (!u.Dead && u.Team == team && Kinds.IsOurEngine(u.T)) engines++;
        return new Fort(ring, high, towers, gates, dug / (float)Ways, wet > dug / 2 && wet > 0, traps / 12, engines, keep);
    }
}
