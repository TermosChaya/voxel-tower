using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Burnt turf: where fire has gone over the grass, or a blast or a bolt from the sky has seared it, the turf is left
/// black («гарь»): no sward grows on it and no tuft stands. The grass creeps back over it from the turf beside it, a
/// little at a time and faster in the rain; a black spot with no turf beside it greens again in the end all the same.
/// </summary>
public sealed partial class Sim
{
    /// <summary>Burnt turf still to grow over, packed (<see cref="WaterPlanner.Pack"/>); what is no longer burnt drops off.</summary>
    readonly List<int> _scorched = new();
    double _nextTend;
    /// <summary>
    /// The burnt turf's own chances, apart from the sim's: a scorch healing never shifts how a blast's pieces fly or a
    /// fight goes after it.
    /// </summary>
    readonly Mulberry32 _turfRng = new(0x5C0C4);

    /// <summary>Burnt turf waiting to green again.</summary>
    public int Scorched => _scorched.Count;

    /// <summary>Sears the turf at (x, y, z), if turf is what is there, and burns off the grass and flowers standing on it.</summary>
    public bool Sear(int x, int y, int z)
    {
        if (!World.InBounds(x, y, z) || !Mat.IsGrass(World.Get(x, y, z))) return false;
        World.Set(x, y, z, Mat.Scorch);
        for (int up = y + 1; up < World.SY && Mat.IsSprig(World.Get(x, up, z)); up++)
        {
            World.Set(x, up, z, Mat.Air);
            if (Orders.At(x, up, z) != 0) Orders.Clear(x, up, z);
        }
        _scorched.Add(WaterPlanner.Pack(x, y, z));
        return true;
    }

    /// <summary>
    /// Sears the open turf within r voxels of (x, y, z): all of it near the middle, in patches toward the rim. Only turf
    /// open to the heat goes black: not under a roof or a wall, nor under water, nor far below a blast high on a wall.
    /// </summary>
    public int SearRound(float x, float y, float z, float r)
    {
        int n = 0, ri = (int)MathF.Ceiling(r);
        for (int dz = -ri; dz <= ri; dz++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                int sx = (int)x + dx, sz = (int)z + dz;
                if (sx < 0 || sz < 0 || sx >= World.SX || sz >= World.SZ) continue;
                int top = World.SurfAt(sx, sz);
                float dy = top - 1 - y, d2 = dx * dx + dy * dy + dz * dz;
                if (d2 > r * r || World.Get(sx, top, sz) == Mat.Water) continue;
                double keep = d2 / (r * r);
                if (keep > .3 && _turfRng.Next() < (keep - .3) * 1.3) continue;
                if (Sear(sx, top - 1, sz)) n++;
            }
        return n;
    }

    /// <summary>
    /// Every two seconds the grass creeps back over some of the burnt turf: a spot open to the sky with turf of any shade
    /// beside it takes that shade again in a minute and a half or so, half that in the rain, so a scorch heals from its
    /// edges inward; one with none beside it greens in the end by itself, in half an hour.
    /// </summary>
    void TendTurf()
    {
        if (Time < _nextTend || _scorched.Count == 0) return;
        _nextTend = Time + 2;
        // a long list is looked over a share at a time, each spot looked at the likelier to green for it
        int look = Math.Min(_scorched.Count, Math.Max(48, _scorched.Count / 20));
        double creep = (Weather == Weather.Rain ? .045 : .022) * _scorched.Count / look;
        for (int k = look; k > 0 && _scorched.Count > 0; k--)
        {
            int i = (int)(_turfRng.Next() * _scorched.Count), p = _scorched[i];
            var (x, y, z) = WaterPlanner.Unpack(p);
            if (World.Get(x, y, z) != Mat.Scorch) { Drop(i); continue; }
            byte up = World.Get(x, y + 1, z);
            if (up != Mat.Air && !(Mat.Passable[up] && up != Mat.Water)) continue;
            byte beside = 0;
            foreach (var (ox, oz) in TurfSides)
            {
                byte m = World.Get(x + ox, y, z + oz);
                if (Mat.IsGrass(m)) { beside = m; break; }
            }
            if (_turfRng.Next() < (beside != 0 ? creep : creep * .05))
            {
                World.Set(x, y, z, beside != 0 ? beside : Mat.Grass);
                Drop(i);
            }
        }

        void Drop(int i)
        {
            _scorched[i] = _scorched[^1];
            _scorched.RemoveAt(_scorched.Count - 1);
        }
    }

    static readonly (int x, int z)[] TurfSides = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>After a load: the burnt turf of the chunks play has touched back on the list (the rest is as the valley was made).</summary>
    void FindScorched()
    {
        _scorched.Clear();
        for (int ci = 0; ci < World.ChunkCount; ci++)
        {
            if (!World.Changed(ci) || World.IsEmpty(ci)) continue;
            World.ChunkCoords(ci, out int cx, out int cy, out int cz);
            for (int y = cy * VoxelWorld.CS; y < (cy + 1) * VoxelWorld.CS; y++)
                for (int z = cz * VoxelWorld.CS; z < (cz + 1) * VoxelWorld.CS; z++)
                    for (int x = cx * VoxelWorld.CS; x < (cx + 1) * VoxelWorld.CS; x++)
                        if (World.Get(x, y, z) == Mat.Scorch) _scorched.Add(WaterPlanner.Pack(x, y, z));
        }
    }
}
