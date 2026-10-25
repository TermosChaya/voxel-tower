using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The ready templates (--pose=templates, Sim.Templates.cs) on level ground west of the citadel: a line of works
    /// running north and south — two round towers, a stretch of wall from each to a gatehouse between them, its gate
    /// facing west — and inside a watchtower, a barn and a palisade. --look=0 (by itself) all of it built, the library in
    /// hand with the round tower's ghost at the cursor; 1 the same marked and half built by eight workers; 2 a row of
    /// watchtowers drawn with Shift held, the button still down; 3 the settlement risen to «Село», the town tab beside it.
    /// </summary>
    void TemplatesPose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.Standing = 2;
        int kx = s.KeepX, kz = s.KeepZ;
        // a stretch of ground two hundred voxels across, as level as can be found west of the citadel
        int fx = kx - 260, fz = kz, best = int.MaxValue;
        for (int dx = -200; dx <= 0; dx += 16)
            for (int dz = -160; dz <= 160; dz += 16)
            {
                int cx = kx - 220 + dx, cz = kz + dz, lo = int.MaxValue, hi = int.MinValue;
                bool bad = false;
                for (int z = cz - 120; z <= cz + 120 && !bad; z += 8)
                    for (int x = cx - 40; x <= cx + 110 && !bad; x += 8)
                    {
                        if (!w.OnLand(x, z) || s.Forbidden(x, z)) { bad = true; break; }
                        int g = w.SurfAt(x, z); lo = Math.Min(lo, g); hi = Math.Max(hi, g);
                        if (w.Get(x, g - 1, z) == Mat.Water || Mat.IsPlayer[w.Get(x, g - 1, z)]) bad = true;
                    }
                int score = bad ? int.MaxValue : (hi - lo) * 1000 - dx;
                if (score < best) { best = score; fx = cx; fz = cz; }
            }
        ClearTrees(fx - 60, fz - 130, fx + 130, fz + 130);
        sim.Stock.Gold = Math.Max(sim.Stock.Gold, 1000);
        sim.Stock[Stockpile.Wood] += 800.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 1600.0 * VoxelWorld.PerM3;
        int look = Math.Max(0, _look);
        Template T(string key) => Sim.TemplateOf(key);
        void Put(string key, int x, int z, int rot, int bx = 0, int bz = 0)
        {
            var t = T(key);
            var plan = t.Plan(w, s, x, z, bx, bz, rot, t.Size);
            string no = sim.PutTemplate(t, plan);
            if (no != null) GD.Print($"[pose] {t.Name}: {no}");
        }
        bool instant = look != 1;
        sim.InstantLabour = instant;
        Put("roundtower", fx, fz - 96, 1);
        Put("roundtower", fx, fz + 96, 1);
        Put("gatehouse", fx, fz, 3);
        Put("wall", fx, fz - 96, 0, fx, fz - 44);
        Put("wall", fx, fz + 44, 0, fx, fz + 96);
        Put("watchtower", fx + 70, fz - 60, 3);
        Put("barn", fx + 80, fz + 40, 0);
        Put("palisade", fx + 40, fz + 110, 0, fx + 120, fz + 110);
        if (instant)
        {
            // the gate hung and the barn's door, as by many hands
            for (int i = 0; i < 6; i++) { Advance(2.5); sim.CompleteAll(); }
            sim.InstantLabour = false;
            Advance(2);
        }
        else
        {
            for (int i = 0; i < 8; i++) sim.Spawn(Kinds.Worker, sim.NearestStand(fx + 40 + i * 6, fz, 4));
            Advance(look == 1 ? 90 : 2);
        }
        if (look == 3)
        {
            // the settlement risen a rank on what it built
            sim.Standing = 1;
            sim.BuiltVoxels = Math.Max(sim.BuiltVoxels, 2000L * Sim.StandingPoints[2]);
            Advance(6);
        }
        GD.Print($"[pose] templates at {fx},{fz}: {sim.Orders.Total} orders left, gates {sim.Gates.Count}, «{sim.StandingLine()}»");
        Tools.SetTool(Tool.Template);
        Tools.PickTemplate(look == 2 ? Array.IndexOf(Sim.Templates, T("watchtower")) : 0);
        if (look == 1) Rig.SetAt(w.WX(fx + 20), w.WY(w.SurfAt(fx, fz)), w.WZ(fz), -.9f, .7f, 1.4f);
        else Rig.SetAt(w.WX(fx + 60), w.WY(w.SurfAt(fx, fz)), w.WZ(fz), -.9f, .66f, 1.05f);
        _poseLater = () =>
        {
            Vector2 At(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(w.WX(x + .5f), w.WY(w.SurfAt(x, z)), w.WZ(z + .5f)));
            void Move(Vector2 p) => Tools._UnhandledInput(new InputEventMouseMotion { Position = p });
            if (look == 2)
            {
                // the row: the button down at its start with Shift, the cursor at its end
                var a = At(fx + 120, fz - 100); var b = At(fx + 120, fz + 60);
                Move(a);
                Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a, ShiftPressed = true });
                for (int i = 1; i <= 8; i++) Move(a.Lerp(b, i / 8f));
            }
            else Move(At(fx + 120, fz - 20));
            Hud.Refresh();
        };
        Hud.Refresh();
    }
}
