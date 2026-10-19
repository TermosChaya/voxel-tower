using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Timber gives way under what is too heavy for it. A deck, a bridge, a floor or a roof of timber over open air or water
/// holds only so much: what comes down on it — or rolls onto it, or is set down on it — weighs on it all the more the
/// harder it comes, and if that is more than the planks under it bear (with those about them taking a share), they break
/// and fall in splinters, and it goes on down through the hole, the slower for it. A trap floor of thin poles gives way
/// under anything heavy, as it does under the horde; the horde's decks and ladders hold little, our log floors a good
/// deal; the drawbridge's oak bound with iron, and anything resting on the ground, holds whatever comes. A ladder in the
/// way of something flying hard is torn off the wall where it is struck.
/// </summary>
public sealed partial class Sim
{
    double _crushSaid = -99;

    /// <summary>How much a voxel of timber over a gap bears, as voxels of what comes down at a walk: a trap floor of
    /// poles next to nothing, the horde's decks little, our floors and roofs of logs a good deal; 0 for what does not
    /// give way.</summary>
    static float Bears(byte m) => m switch { Mat.TrapDeck => 2, Mat.Deck => 6, Mat.Wood => 60, _ => 0 };

    /// <summary>How thick the timber is at a voxel, down to the open air or water under it (three at most); 0 if it rests
    /// on something firm, or is a wall of it rather than a floor.</summary>
    int Span(int x, int y, int z)
    {
        for (int k = 0; k < 3; k++)
        {
            int yy = y - k;
            if (yy < 1 || Bears(World.Get(x, yy, z)) <= 0) return 0;
            byte below = World.Get(x, yy - 1, z);
            if (below == Mat.Air || below == Mat.Water || Mat.Passable[below]) return k + 1;
        }
        return 0;
    }

    /// <summary>
    /// Whether the timber under something gives way: the voxels it rests on (each the top of what holds it up), and its
    /// heft (its voxels, the more as the square of how hard it comes). Anything firm among them holds it all; else, too
    /// heavy for what they bear and those about them, they break — the planks under it and a hand about, down through the
    /// floor's thickness, fall in splinters; a trap floor falls in as it does under the horde. How much of its heft the
    /// breaking took, or 0 if it held.
    /// </summary>
    float GivesWay(List<(int x, int y, int z)> under, float heft, string cause)
    {
        if (under.Count == 0) return 0;
        float holds = 0;
        int weak = 0;
        for (int i = 0; i < under.Count; i++)
        {
            var (x, y, z) = under[i];
            // splinters and broken stone lying on it hold nothing up by themselves: what they lie on does (and nothing,
            // where they lie across a gap)
            for (int k = 0; k < 4 && y > 2 && World.Get(x, y, z) is Mat.Lumber or Mat.Rubble; k++) y--;
            under[i] = (x, y, z);
            byte m = World.Get(x, y, z);
            if (m == Mat.Air || m == Mat.Water || Mat.Passable[m]) continue;
            float b = Bears(m);
            int t = b > 0 ? Span(x, y, z) : 0;
            if (t == 0) return 0;
            holds += b * t;
            weak++;
        }
        if (weak == 0) return 0;
        // the planks about it bear a share of the weight too
        holds *= 1 + 8f / weak;
        if (heft <= holds) return 0;
        var hole = new List<(int x, int y, int z)>();
        var seen = new HashSet<int>();
        bool trap = false; byte what = 0; float cx = 0, cy = 0, cz = 0;
        foreach (var (x, y, z) in under)
        {
            cx += x; cy += y; cz += z;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    for (int k = 0; k < 3; k++)
                    {
                        int hx = x + dx, hy = y - k, hz = z + dz;
                        if (!World.InBounds(hx, hy, hz) || hy < 2) break;
                        byte m = World.Get(hx, hy, hz);
                        if (Bears(m) <= 0) break;
                        if (!seen.Add(WaterPlanner.Pack(hx, hy, hz))) continue;
                        if (m == Mat.TrapDeck) trap = true;
                        else hole.Add((hx, hy, hz));
                        if (what == 0 || m == Mat.TrapDeck) what = m;
                    }
        }
        cx /= under.Count; cy /= under.Count; cz /= under.Count;
        // a trap floor falls in all together, as under the horde, and takes whoever stands on it down with it
        if (trap)
            foreach (var (x, y, z) in under)
                if (World.Get(x, y, z) == Mat.TrapDeck) { Spring(x, y, z, "Ловчий настил провалился: на него рухнуло тяжёлое."); break; }
        // the planks in splinters, falling down after it
        int room = MaxAloft - Aloft;
        foreach (var part in Split(hole, 10))
        {
            float px = 0, pz = 0;
            foreach (var (x, _, z) in part) { px += x; pz += z; }
            px = px / part.Count - cx; pz = pz / part.Count - cz;
            float pl = MathF.Max(.5f, MathF.Sqrt(px * px + pz * pz)), out_ = VoxelWorld.PerMetre;
            var c = part.Count <= room ? Loose(part, px / pl * out_, -2 * VoxelWorld.PerMetre, pz / pl * out_, cause, room) : null;
            if (c != null) room -= c.Voxels.Count;
            else foreach (var (x, y, z) in part) { World.Set(x, y, z, Mat.Air); if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z); }
        }
        foreach (var (x, y, z) in hole) Burning.Remove(WaterPlanner.Pack(x, y, z));
        Nav.Flush();
        InvalidateFields();
        if (hole.Count > 0)
        {
            Collapsed?.Invoke(hole.Count, cx, cy, cz);
            if (Time - _crushSaid > 10 && what != Mat.TrapDeck)
            {
                _crushSaid = Time;
                Say(what == Mat.Deck ? "Настил орды проломлен: тяжёлое рухнуло на него и ушло вниз." : "Бревенчатое перекрытие проломлено: тяжёлое рухнуло на него и ушло вниз.", 2, cx, cz);
            }
        }
        return holds / heft;
    }

    /// <summary>
    /// A piece about to lie down on what it came down on (its voxels as they would lie there): if that is timber over a
    /// gap too weak for it, it breaks through and falls on, the slower for what it broke. True if it did.
    /// </summary>
    bool BreaksThrough(Flier f, List<(int ox, int oy, int oz, byte m)> cells, int ix, int ay, int iz, float speed)
    {
        var own = new HashSet<(int, int, int)>(cells.Count);
        foreach (var (ox, oy, oz, _) in cells) own.Add((ox, oy, oz));
        var under = new List<(int x, int y, int z)>();
        foreach (var (ox, oy, oz, _) in cells)
        {
            if (own.Contains((ox, oy - 1, oz))) continue;
            int x = ix + ox, y = ay + oy - 1, z = iz + oz;
            if (SolidAt(x, y, z)) under.Add((x, y, z));
        }
        float k = speed / (2 * VoxelWorld.PerMetre);
        float spent = GivesWay(under, f.Voxels.Count * (1 + k * k), f.Cause);
        if (spent <= 0) return false;
        Slowed(f, spent);
        return true;
    }

    /// <summary>
    /// A ball on whatever is under the bottom of its round: if that is timber too weak for it — rolling onto it, the
    /// faster the heavier; coming down on it, the harder the heavier — it breaks through. True if it did.
    /// </summary>
    bool BallBreaks(Flier f, float speed, float hard)
    {
        float r = f.RollR, rr = MathF.Max(1, r * .5f);
        int row = (int)MathF.Floor(f.Y - r - .5f);
        var under = new List<(int x, int y, int z)>();
        for (int z = (int)MathF.Floor(f.Z - rr); z <= (int)MathF.Floor(f.Z + rr); z++)
            for (int x = (int)MathF.Floor(f.X - rr); x <= (int)MathF.Floor(f.X + rr); x++)
            {
                float dx = x + .5f - f.X, dz = z + .5f - f.Z;
                if (dx * dx + dz * dz <= rr * rr && SolidAt(x, row, z)) under.Add((x, row, z));
            }
        float roll = speed / (6 * VoxelWorld.PerMetre), drop = hard / (2 * VoxelWorld.PerMetre);
        float heft = f.Voxels.Count * (1 + roll * roll + drop * drop);
        float spent = GivesWay(under, heft, f.Cause);
        if (spent <= 0) return false;
        Slowed(f, spent);
        return true;
    }

    /// <summary>What broke through goes on the slower for what it broke, and down.</summary>
    void Slowed(Flier f, float spent)
    {
        float k = MathF.Sqrt(MathF.Max(.05f, 1 - spent));
        f.VX *= k; f.VZ *= k;
        f.VY = MathF.Min(f.VY * k, -VoxelWorld.PerMetre);
        // out in the open now, under it the hole: any voxel of it may strike what is below at once
        f.Since = Math.Min(f.Since, Time - .2);
    }

    /// <summary>
    /// A piece flying hard through a ladder — ours, or the horde's against our wall — tears it off where it strikes it:
    /// the ladder about it, as far as the piece reaches and a little more, flies off with it, and what was above comes
    /// down after.
    /// </summary>
    void TearLadders(Flier f, float speed)
    {
        if (f.Voxels.Count < 24 || speed < 3 * VoxelWorld.PerMetre) return;
        var (ax, ay, az) = AxisOf(f);
        float c = MathF.Cos(f.Turn), s = MathF.Sin(f.Turn);
        int stride = Math.Max(1, f.Voxels.Count / 200);
        bool struck = false;
        for (int i = 0; i < f.Voxels.Count && !struck; i += stride)
        {
            var (dx, dy, dz, _) = f.Voxels[i];
            var (px, py, pz) = Turned(dx, dy, dz, ax, ay, az, c, s);
            int x = (int)MathF.Floor(f.X + px), y = (int)MathF.Floor(f.Y + py), z = (int)MathF.Floor(f.Z + pz);
            struck = World.InBounds(x, y, z) && World.Get(x, y, z) is Mat.SiegeLadder or Mat.Ladder;
        }
        if (!struck) return;
        float reach = MathF.Max(4, f.R + 2);
        int ri = (int)MathF.Ceiling(reach), cx = (int)MathF.Floor(f.X), cy = (int)MathF.Floor(f.Y), cz = (int)MathF.Floor(f.Z);
        var rungs = new List<(int x, int y, int z)>();
        for (int y = cy - ri; y <= cy + ri; y++)
            for (int z = cz - ri; z <= cz + ri; z++)
                for (int x = cx - ri; x <= cx + ri; x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) + (z - cz) * (z - cz) <= reach * reach && World.InBounds(x, y, z)
                        && World.Get(x, y, z) is Mat.SiegeLadder or Mat.Ladder) rungs.Add((x, y, z));
        foreach (var part in Split(rungs, 12))
            Loose(part, f.VX * .5f, f.VY * .3f + 2 * VoxelWorld.PerMetre, f.VZ * .5f, f.Cause);
        // the blow takes something of its going
        f.VX *= .85f; f.VY *= .85f; f.VZ *= .85f;
        Nav.Flush();
        InvalidateFields();
    }
}
