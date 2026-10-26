using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// Every kind of town building at once (--pose=street, Sim.Frontage.cs): on a field evened out by the citadel, three
    /// rows of them put up as by many hands, the doors to the citadel, each with its sign, its chimney if it has a hearth
    /// and the things of its trade before it (the miners' building, which wants an outcrop of ore, is in --pose=metal).
    /// --look=0…5 three of them at a time from before their doors, 6 the first three at night, 7 all of it from above.
    /// </summary>
    void StreetPose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX, kz = s.KeepZ;
        // the rows, in the order their needs allow, each on its own side of the citadel so that none stands before
        // another's doors; three buildings a look
        string[][] rows =
        {
            new[] { "hall", "market", "smithy", "carpentry", "masons", "workshop" },
            new[] { "barracks", "knights", "drill", "infirmary", "range", "lab" },
            new[] { "kennel", "stables", "traps", "trolls", "giants" },
        };
        (int dx, int dz)[] sides = { (1, 0), (-1, 0), (0, 1) };
        const int Along = 560;
        // (far enough out that the rows' ends do not meet at the corners)
        int dist = Math.Max(s.KeepR + 170, 300);
        foreach (var r in rows) foreach (var key in r) sim.Learnt.Add(Sim.TownKindOf(key).Lore);
        sim.Stock.Gold = Math.Max(sim.Stock.Gold, 9000);
        sim.Stock[Stockpile.Wood] += 3000.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 3000.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Metal] += 40.0 * VoxelWorld.PerM3;
        for (int i = 0; i < 6; i++) sim.Spawn(Kinds.Worker, sim.Gate);
        if (sim.Alarm) sim.StandDown();
        var put = new List<Building>();
        var ground = new int[rows.Length];
        // (the knights want the barracks raised a storey first)
        void Raise(string key)
        {
            var b = sim.BuildingOf(key);
            if (b is not { Ready: true } || sim.Raise(b) != null) return;
            for (int t = 0; t < 30 && b.Level < 2; t++) { sim.CompleteAll(); Advance(1); if (sim.ZoneOf(b) is { } z) sim.LookOver(z); }
        }
        // every row's field evened out first (the fields meet at the corners): along it, and across from its front
        // yards to behind it
        for (int r = 0; r < rows.Length; r++)
        {
            var (dx, dz) = sides[r];
            int ax = Math.Abs(dz), az = Math.Abs(dx);
            int cx = kx + dx * dist, cz = kz + dz * dist;
            int g = ground[r] = w.SurfAt(cx, cz);
            for (int a = -Along / 2 - 20; a <= Along / 2 + 20; a++)
                for (int c = -60; c <= 36; c++)
                {
                    int x = cx + ax * a + dx * c, z = cz + az * a + dz * c;
                    if (!w.InBounds(x, 0, z) || s.Forbidden(x, z)) continue;
                    for (int y = g - 8; y < g + 48; y++)
                    {
                        byte want = y < g ? (y == g - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                        if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                        // (and nothing of the starting fort remembered there to be mended)
                        if (sim.Design.Get(x, y, z) != 0) sim.Design.SetRaw(x, y, z, 0);
                    }
                }
        }
        for (int r = 0; r < rows.Length; r++)
        {
            var (dx, dz) = sides[r];
            int ax = Math.Abs(dz), az = Math.Abs(dx);
            int cx = kx + dx * dist, cz = kz + dz * dist;
            sim.InstantLabour = true;
            int at = 0;
            foreach (var key in rows[r]) at -= Sim.TownKindOf(key).D + 24;
            at /= 2;
            foreach (var key in rows[r])
            {
                var k = Sim.TownKindOf(key);
                if (key == "knights") Raise("barracks");
                int along = at + k.D / 2;
                string no = sim.PutUp(k, cx + ax * along, cz + az * along, turned: ax == 1, out var b);
                if (no != null) GD.Print($"[pose] {key}: {no}");
                at += k.D + 24;
                if (b == null) continue;
                put.Add(b);
                for (int t = 0; t < 40 && !b.Ready; t++) { sim.CompleteAll(); Advance(1); if (sim.ZoneOf(b) is { } z) sim.LookOver(z); }
            }
            sim.InstantLabour = false;
        }
        // the household out of the way, by the citadel, and every front dressed
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.Team != Team.Player) continue;
            u.Cell = sim.Gate; (u.X, u.Y, u.Z) = sim.Nav.Feet(u.Cell); u.Path = null;
            u.Hold = true; u.Dest = u.Post = u.Cell;
        }
        Advance(3);
        sim.DressTown();
        Advance(2);
        GD.Print($"[pose] street: " + string.Join(", ", put.ConvertAll(b => $"{b.Kind} {(b.Ready ? "ready" : $"not ready ({sim.ZoneOf(b)?.Why})")} {sim.FrontageOf(b).Count}")));
        int look = Math.Max(0, _look);
        if (look == 6)
        {
            sim.Waves.Countdown = Waves.Dusk + 1;
            Advance(2);
            for (int t = 0; t < 6; t++) { sim.Waves.Countdown = 8; Advance(1); }
        }
        if (look == 7) { Rig.SetAt(w.WX(kx), w.WY(ground[0]), w.WZ(kz), .5f, 1.2f, .3f); Hud.Refresh(); return; }
        int group = look % 6, row = group / 2;
        var three = new List<Building>();
        foreach (var b in put) if (Array.IndexOf(rows[row], b.Kind) is int i && i >= 0 && i / 3 == group % 2) three.Add(b);
        if (three.Count == 0) return;
        float mx = 0, mz = 0;
        foreach (var b in three) { mx += b.CX; mz += b.CZ; }
        mx /= three.Count; mz /= three.Count;
        // from before the doors, which look to the citadel
        var (sx, sz) = sides[row];
        Rig.SetAt(w.WX(mx - sx * 30), w.WY(ground[row] + 10), w.WZ(mz - sz * 30), MathF.Atan2(-sx, -sz) + .4f, .55f, 1.05f);
        Hud.Refresh();
    }
}
