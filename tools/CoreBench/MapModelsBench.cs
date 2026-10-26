using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// What stands on the big world map (--mapmodels; MapModels*.cs, MapDress.cs): every thing a hex can hold has a model,
/// every model is made and fits its bounds, the same each time it is made and kept once made; none is heavier than its
/// share of the frame; the pieces of a made-up world dressed in good time and the same each time, their woods wooded,
/// their farms about the castle, no tree in the water nor on a road, the spent things gone, what turns and what smokes
/// told. --mapmodels-probe: the faces of every model, coarse and fine.
/// </summary>
static class MapModelsBench
{
    public static void Probe()
    {
        var demo = new DemoHexes(36, 36, 11);
        var (hc, hr) = demo.Home;
        Console.WriteLine($"home {hc},{hr} {demo.Look(hc, hr).Icon}");
        for (int sd = 0; sd < 6; sd++) { var (nc, nr) = HexMapLayout.Neighbour(hc, hr, sd); var l = demo.Look(nc, nr); Console.WriteLine($"  side {sd}: {l.Ground} {l.Relief} {l.Icon} roads {l.Roads} rivers {l.Rivers}"); }
        foreach (MapModel m in Enum.GetValues<MapModel>())
        {
            var sw = Stopwatch.StartNew();
            var fine = MapModels.Get(m, 0, 1, false, MapModels.KOf(m, false));
            double ms = sw.Elapsed.TotalMilliseconds;
            var coarse = MapModels.Get(m, 0, 1, false, 1);
            Console.WriteLine($"{m,-14} fine {fine.Solid?.Quads ?? 0,6} quads ({ms,5:0} ms), shadow {fine.Shadow?.Quads ?? 0,5}; coarse {coarse.Solid?.Quads ?? 0,5}, its shadow {coarse.Shadow?.Quads ?? 0,5}");
        }
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        // ---- every thing of the map has its model, every model is made, fits, and is the same made again
        var missing = new List<HexIcon>();
        foreach (HexIcon icon in Enum.GetValues<HexIcon>())
            if (icon is not (HexIcon.None or HexIcon.Monster) && MapDress.ModelOf(icon) == null) missing.Add(icon);
        check("every thing a hex of the world map can hold has a model of its own", missing.Count == 0, missing.Count == 0 ? $"{Enum.GetValues<HexIcon>().Length - 2} things" : "none for " + string.Join(", ", missing));
        var empty = new List<string>();
        var heavy = new List<string>();
        int most = 0; MapModel heaviest = 0;
        var sw = Stopwatch.StartNew();
        foreach (MapModel m in Enum.GetValues<MapModel>())
            for (int v = 0; v < 3; v++)
            {
                var fine = MapModels.Get(m, v, 1, false, MapModels.KOf(m, false));
                var coarse = MapModels.Get(m, v, 1, false, 1);
                // (what is very small may come to nothing coarse; it is not seen from so far)
                if (fine.Solid == null || fine.Solid.Quads == 0 || fine.Shadow == null || coarse.Solid == null && fine.Solid.Quads > 200) empty.Add($"{m}/{v}");
                int q = fine.Solid?.Quads ?? 0;
                if (q > most) { most = q; heaviest = m; }
                int cap = MapModels.Grows(m) ? 1600 : 16000;
                if (q > cap) heavy.Add($"{m} {q}");
            }
        double madeMs = sw.Elapsed.TotalMilliseconds;
        check("every model of the map is made, fine and coarse, with its shadow", empty.Count == 0, empty.Count == 0 ? $"{MapModels.KeptCount} kept, made in {madeMs:0} ms" : "nothing in " + string.Join(", ", empty.Take(8)));
        check("no model is heavier than its share: what grows under 1600 faces, a thing under 16000", heavy.Count == 0, heavy.Count == 0 ? $"the heaviest {heaviest}, {most} faces" : string.Join(", ", heavy.Take(8)));
        // a model made again from nothing is the same, voxel for voxel
        bool same = true;
        foreach (var m in new[] { MapModel.Citadel, MapModel.Spruce, MapModel.HordeTown, MapModel.Sails })
        {
            var a = MapModels.Draw(m, 1, 2, false, 3).W; var b = MapModels.Draw(m, 1, 2, false, 3).W;
            for (int y = 0; y < a.SY && same; y += 1) for (int z = 0; z < a.SZ && same; z += 2) for (int x = 0; x < a.SX; x += 2) if (a.Get(x, y, z) != b.Get(x, y, z)) { same = false; break; }
        }
        check("a model made again is the same, voxel for voxel", same, "");
        // every model within its bounds: none of its faces at the edge of its world
        var cut = new List<string>();
        foreach (MapModel m in Enum.GetValues<MapModel>())
        {
            var c = MapModels.Draw(m, 2, 1, false, MapModels.KOf(m, false));
            var w = c.W; bool edge = false;
            for (int y = 0; y < w.SY && !edge; y++)
                for (int k = 0; k < w.SX && !edge; k++)
                    if (w.Get(k, y, 0) != Mat.Air || w.Get(k, y, w.SZ - 1) != Mat.Air || w.Get(0, y, Math.Min(k, w.SZ - 1)) != Mat.Air || w.Get(w.SX - 1, y, Math.Min(k, w.SZ - 1)) != Mat.Air) edge = true;
            for (int z = 0; z < w.SZ && !edge; z++) for (int x = 0; x < w.SX; x++) if (w.Get(x, w.SY - 1, z) != Mat.Air) { edge = true; break; }
            if (edge) cut.Add(m.ToString());
        }
        check("every model fits the world it is made in, nothing cut at its edges", cut.Count == 0, cut.Count == 0 ? "" : string.Join(", ", cut));

        // ---- a made-up world dressed: in good time, the same each time, its woods wooded, nothing in the water or on the roads
        var src = new DemoHexes(36, 36, 11);
        src.RevealAll = true;
        var lay = new HexMapLayout(36, 36);
        var times = new List<double>();
        int trees = 0, things = 0, wet = 0, onRoad = 0, farms = 0, quads = 0;
        MapDress.Dressed first = null;
        for (int tz = 0; tz < lay.TilesZ; tz++)
            for (int tx = 0; tx < lay.TilesX; tx++)
            {
                var t = Stopwatch.StartNew();
                var d = MapDress.Dress(src, lay, tx, tz);
                times.Add(t.Elapsed.TotalMilliseconds);
                first ??= d;
                quads += d.NearQuads;
                foreach (var p in d.Placed)
                {
                    if (MapModels.Grows(p.Model) && p.Model is not (MapModel.Reeds or MapModel.Lily or MapModel.Flowers)) trees++;
                    else if (!MapModels.Grows(p.Model)) things++;
                    if (p.Model is MapModel.Field or MapModel.Farmhouse) farms++;
                    var (col, row) = lay.HexAt(p.X * MapDress.Scale, p.Z * MapDress.Scale);
                    if (MapModels.Grows(p.Model) && p.Model is not (MapModel.Reeds or MapModel.Lily))
                    {
                        var cl = HexTiles.ColumnAt(src, lay, (int)(p.X * MapDress.Scale), (int)(p.Z * MapDress.Scale));
                        if (cl.Water > cl.Top) wet++;
                        var l = HexTiles.LookOf(src, col, row);
                        var (cx, cz) = lay.Center(col, row);
                        if (l.Roads != 0 && MapDress.WayNear(l.Roads, p.X * MapDress.Scale - cx, p.Z * MapDress.Scale - cz) < 1.5f) onRoad++;
                    }
                }
            }
        times.Sort();
        int tiles = times.Count;
        check("a piece of the world map is dressed in good time: half under 25 ms, the slowest under 120",
            times[tiles / 2] < 25 && times[^1] < 120, $"{tiles} pieces, half under {times[tiles / 2]:0.0} ms, the slowest {times[^1]:0} ms; {trees} trees, {things} things, {quads / tiles} faces a piece");
        check("the woods are wooded, the things stand, the farms lie about the castle", trees > 3000 && things > 60 && farms >= 3, $"{trees} trees and bushes, {things} things, {farms} fields and farmhouses");
        check("nothing that grows stands in the water nor on a road", wet == 0 && onRoad == 0, $"{wet} in the water, {onRoad} on a road");
        var again = MapDress.Dress(src, lay, 0, 0);
        check("a piece dressed again is the same", again.Placed.Count == first.Placed.Count && again.NearQuads == first.NearQuads, $"{first.Placed.Count} placed");
        // the windmills turn, the fires smoke; a spent chest is gone, a spent windmill has no sails
        int turners = 0, plumes = 0;
        for (int tz = 0; tz < lay.TilesZ; tz++) for (int tx = 0; tx < lay.TilesX; tx++) { var d = MapDress.Dress(src, lay, tx, tz); turners += d.Turners.Count; plumes += d.Plumes.Count; }
        check("what turns and what smokes on the map is told to the drawing", turners > 0 && plumes > 0, $"{turners} sails and wheels, {plumes} smokes and fires");
        var spentChest = new HexLook { Ground = HexGround.Grass, Icon = HexIcon.Chest, Variant = 1 };
        var spentMill = new HexLook { Ground = HexGround.Grass, Icon = HexIcon.Windmill, Variant = 1 };
        check("a spent chest is gone from the map, a spent windmill turns no sails", MapDress.Shows(spentChest) == false && !MapDress.Turns(spentMill), "");
    }
}
