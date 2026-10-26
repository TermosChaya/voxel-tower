using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// A kind of town building in each of its looks side by side (--pose=looks, Sim.Looks.cs): on a field evened out north
    /// of the citadel, put up as by many hands, the doors to the citadel, the fronts dressed. --look=0…16 the kind (in the
    /// order of the «Город» tab, the miners' building left out: it wants an outcrop), from before their doors; --at=1
    /// from the other side, behind them; --at=2, 3, 4 close on the first, the second, the third.
    /// </summary>
    void LooksPose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.ManyOfAKind = true;
        var kinds = Array.FindAll(Sim.TownKinds, k => !k.OnOre);
        var k = kinds[Math.Clamp(Math.Max(0, _look), 0, kinds.Length - 1)];
        var looks = Sim.LooksOf(k);
        foreach (var kk in Sim.TownKinds) sim.Learnt.Add(kk.Lore);
        sim.Stock.Gold = Math.Max(sim.Stock.Gold, 9000);
        sim.Stock[Stockpile.Wood] += 3000.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 3000.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Earth] += 3000.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Metal] += 40.0 * VoxelWorld.PerM3;
        if (sim.Alarm) sim.StandDown();
        // (the buildings it needs, put up out of the way behind the citadel)
        int kx = s.KeepX, kz = s.KeepZ;
        // the row: along x, north of the citadel, the doors looking south to it
        int deep = 0;
        foreach (var l in looks) foreach (var a in l.Annexes) if (a.At is Flank.Left or Flank.Right) deep = Math.Max(deep, a.Depth + 8);
        int pitch = k.D + 2 * deep + 40, along = pitch * looks.Length;
        int cz = kz - Math.Max(s.KeepR + 190, 280), cx = kx;
        int g = w.SurfAt(cx, cz);
        for (int x = cx - along / 2 - 40; x <= cx + along / 2 + 40; x++)
            for (int z = cz - 110; z <= cz + 90; z++)
            {
                if (!w.InBounds(x, 0, z) || s.Forbidden(x, z)) continue;
                for (int y = g - 8; y < g + 72; y++)
                {
                    byte want = y < g ? (y == g - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                    if (sim.Design.Get(x, y, z) != 0) sim.Design.SetRaw(x, y, z, 0);
                }
            }
        var put = new List<Building>();
        sim.InstantLabour = true;
        for (int i = 0; i < looks.Length; i++)
        {
            int bx = cx - along / 2 + pitch * i + pitch / 2;
            string no = sim.PutUp(k, bx, cz, turned: true, out var b, i);
            if (no != null) GD.Print($"[pose] looks {k.Key} {i}: {no}");
            if (b == null) continue;
            put.Add(b);
            for (int t = 0; t < 40 && !b.Ready; t++) { sim.CompleteAll(); Advance(1); if (sim.ZoneOf(b) is { } z) sim.LookOver(z); }
        }
        sim.InstantLabour = false;
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.Team != Team.Player) continue;
            u.Cell = sim.Gate; (u.X, u.Y, u.Z) = sim.Nav.Feet(u.Cell); u.Path = null;
            u.Hold = true; u.Dest = u.Post = u.Cell;
        }
        Advance(3);
        sim.DressTown();
        Advance(2);
        GD.Print($"[pose] looks {k.Key}: " + string.Join(", ", put.ConvertAll(b => $"{b.Look?.Name} {(b.Ready ? "ready" : $"not ready ({sim.ZoneOf(b)?.Why})")}")));
        bool behind = _poseSeconds > .5 && _poseSeconds < 1.5;
        int close = _poseSeconds > 1.5 ? (int)Math.Round(_poseSeconds) - 2 : -1;
        float span = along / 2f;
        if (close >= 0 && close < put.Count)
        {
            var b = put[close];
            Rig.SetAt(w.WX(b.CX), w.WY(g + 10), w.WZ(b.CZ + 24), .5f, .5f, 1.15f);
        }
        else Rig.SetAt(w.WX(cx), w.WY(g + 10), w.WZ(cz + (behind ? -30 : 30)), behind ? MathF.PI + .15f : .15f, .55f, Math.Clamp(170f / span, .3f, 1.3f));
        Hud.Refresh();
        if (_bare) Hud.Visible = false;
    }
}
