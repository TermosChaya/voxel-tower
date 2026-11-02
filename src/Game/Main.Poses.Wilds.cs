using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// Nature's dressing of the open close (--pose=wilds, MapGen.Wilds.cs): --look=N the N-th thing of it as the valley was
    /// made, counted kind by kind (the list is printed; --valley= and --seed= choose the valley), from a few metres off;
    /// --tab=Lodge (any kind's name) counts only those of that kind, the ones in the open first; --look=-1 from high over
    /// the part of the valley most dressed; --cam=yaw,pitch,zoom as for the places.
    /// --perf-without=wilds makes the same valley without it, for the shots of before.
    /// </summary>
    void WildsPose()
    {
        var sim = Sim; var w = World; var s = Site;
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        if (_bare) Hud.Visible = false;
        float at(int k, float d) => _cam is { Length: > 2 } ? _cam[k] : d;
        // the valley as it would be made with it, to frame the same spots before it was laid
        var made = s;
        if (s.Wilds.Count == 0)
        {
            var p = new MapParams { Seed = _seed, Kind = _valley, Classic = _classic, Hexagon = _hexagon, EntrySide = _entry, SizeX = w.SX, SizeZ = w.SZ };
            var w2 = new VoxelWorld(w.SX, w.SY, w.SZ);
            made = MapGen.Generate(w2, p);
        }
        if (made.Wilds.Count == 0) { GD.Print("[pose] wilds: the valley has none"); return; }
        var list = made.Wilds.ConvertAll(t => t);
        // --tab=Kind: only those of a kind, the ones out in the open (no crowns over them) first
        bool one = Enum.TryParse<Wild>(_tab ?? "", out var only);
        if (one) list.RemoveAll(t => t.Kind != only);
        // (crowns looked for a little beyond it, so that a tall bush is not its own crown, and before and after agree)
        int Covered((Wild Kind, int X, int Z, int R) t)
        {
            if (!one) return 0;
            int n = 0;
            foreach (var (ox, oz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int x = t.X + ox * (t.R + 4), z = t.Z + oz * (t.R + 4);
                if (w.ColTop(x, z) > w.SurfAt(x, z) + 14) n++;
            }
            return n;
        }
        list.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : Covered(a) != Covered(b) ? Covered(a).CompareTo(Covered(b)) : (a.X * 7 + a.Z).CompareTo(b.X * 7 + b.Z));
        if (list.Count == 0) { GD.Print($"[pose] wilds: the valley has no {_tab}"); return; }
        int x, z;
        float zoom;
        if (_look < 0)
        {
            // the most dressed square of 192 voxels
            int best = -1, bx = 0, bz = 0;
            for (int qz = 0; qz < w.SZ; qz += 96)
                for (int qx = 0; qx < w.SX; qx += 96)
                {
                    int n = 0;
                    foreach (var t in list) if (t.Kind is not (Wild.Snow or Wild.Cranberry or Wild.Heather) && Math.Abs(t.X - qx - 96) < 96 && Math.Abs(t.Z - qz - 96) < 96) n++;
                    if (n > best) { best = n; bx = qx + 96; bz = qz + 96; }
                }
            (x, z, zoom) = (bx, bz, 1.6f);
        }
        else
        {
            var t = list[Math.Clamp(_look, 0, list.Count - 1)];
            (x, z) = (t.X, t.Z);
            // near enough to see it: the small things nearer
            zoom = t.Kind switch
            {
                Wild.Snow => 3.5f, Wild.Outcrop => 4.8f, Wild.Heather or Wild.Lodge or Wild.Erratic => 6.2f,
                Wild.Rose or Wild.Hawthorn or Wild.Broom or Wild.Driftwood => 7f, _ => 8f,
            };
        }
        // (looked at from well above: from low, a slope before it hides it, the view being 80 m off)
        Rig.SetAt(w.WX(x), w.WY(w.SurfAt(x, z)), w.WZ(z), at(0, -.9f), at(1, _look < 0 ? .5f : .75f), at(2, zoom));
        var counts = new System.Collections.Generic.List<string>();
        for (int i = 0; i < list.Count; i++) if (i == 0 || list[i].Kind != list[i - 1].Kind) counts.Add($"{list[i].Kind} from {i}");
        // (what stands over the ground there, to tell it is in the shot)
        int over = 0;
        for (int zz = z - 6; zz <= z + 6; zz++)
            for (int xx = x - 6; xx <= x + 6; xx++)
                if (w.InBounds(xx, 1, zz)) over += Math.Max(0, w.ColTop(xx, zz) - s.HTop[zz * w.SX + xx]);
        GD.Print($"[pose] wilds {_look} at {x},{z}{(s.Wilds.Count == 0 ? " (made without it)" : "")}, {over} voxels over the ground about it; the valley's: {string.Join(", ", counts)} of {list.Count}");
        Hud.Refresh();
    }
}
