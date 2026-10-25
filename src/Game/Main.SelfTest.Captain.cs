using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The rings, first person and the commander through their input, on a field of turf west of the citadel: two
    /// swordsmen sent with a right click, a ring opens where they go; a worker gone into shows its name, its body and what
    /// it lays, and with the right button held lays the wall marked out before it; a commander gone into shows its four
    /// calls, «Держать строй» on its key sets the swordsmen about it to hold, «Залп!» with no archers is refused in words,
    /// G sends them where it looks; E goes over into another; that one killed, the view falls dark and lets go.
    /// </summary>
    async Task SelfTestCaptain(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var w = World; var sim = Sim;
        void Key_(Act a) => FP._Input(new InputEventKey { PhysicalKeycode = Bindings.KeyOf(a), Keycode = Bindings.KeyOf(a), Pressed = true });

        // a field of level turf, bare of trees
        int fx = Site.KeepX - Site.KeepR - 330, fz = Site.KeepZ - 150, g = w.SurfAt(fx, fz);
        ClearTrees(fx - 80, fz - 60, fx + 80, fz + 60);
        for (int z = fz - 50; z <= fz + 50; z++)
            for (int x = fx - 70; x <= fx + 70; x++)
                for (int y = g - 6; y < g + 40; y++)
                {
                    byte want = y < g ? (y == g - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
        if (sim.Alarm) sim.StandDown();
        Rig.SetAt(w.WX(fx), w.WY(g), w.WZ(fz), -.66f, .6f, 1.6f);
        var sw = sim.Spawn(Kinds.Swordsman, sim.NearestStand(fx - 20, fz + 20, 2));
        var sw2 = sim.Spawn(Kinds.Swordsman, sim.NearestStand(fx - 10, fz + 24, 2));
        Advance(.3);
        await Frames(6);

        // two swordsmen chosen and sent with a right click: a ring where they go
        Selected.Clear(); Selected.Add(sw); Selected.Add(sw2); Hud.OnSelectionChanged();
        int rings0 = Rings.Count;
        Command.OrderTo(Rig.Cam.UnprojectPosition(new Vector3(w.WX(fx + 10), w.WY(g), w.WZ(fz + 30))));
        check("a right click sends the chosen and a ring opens where they go", Rings.Count > rings0 && sw.Dest >= 0 && sw2.Dest >= 0 && sw.Hold,
            $"rings {rings0} → {Rings.Count}, sent to {sw.Dest} and {sw2.Dest}");
        Selected.Clear(); Hud.OnSelectionChanged();
        Advance(4);

        // a worker gone into before a wall marked out: its frame, and the right button held lays what is marked
        int lx = fx + 30;
        var plan = Shapes.Line(w, Site, lx, fz - 16, lx, fz + 16, new Stroke { Mat = Mat.Stone, Width = 8, Height = 8, Level = true }, Shapes.LevelTop(w, lx, fz, 8));
        sim.DesignateBuild(plan);
        sim.Stock[Stockpile.Stone] = Math.Max(sim.Stock[Stockpile.Stone], 20 * VoxelWorld.PerM3);
        var wk = sim.Spawn(Kinds.Worker, sim.NearestStand(lx - 26, fz, 2));
        Advance(.3);
        int Laid() { int n = 0; for (int i = 0; i < plan.Count; i++) if (Mat.IsMasonry(w.Get(plan.X(i), plan.Y(i), plan.Z(i)))) n++; return n; }
        int laid0 = Laid();
        bool entered = FP.Enter(wk);
        FP.Face(0, -.5f);
        await Frames(3);
        bool frame = FP.On && FP.NameLine.Contains(wk.Name) && FP.HeldLine.StartsWith("Кладёт") && !FP.CallsShown;
        FP._Input(new InputEventMouseButton { ButtonIndex = Bindings.Second, Pressed = true });
        for (int i = 0; i < 40; i++) { Advance(.2); await Frames(1); }
        FP._Input(new InputEventMouseButton { ButtonIndex = Bindings.Second, Pressed = false });
        int laid = Laid() - laid0;
        check("a worker gone into shows its name and what it lays; the right button held lays the wall marked out before it",
            entered && frame && laid > 0, $"in {entered}, frame {frame} («{FP.NameLine}», «{FP.HeldLine}»), laid {laid} of {plan.Count} marked");
        FP.Exit();
        sim.CancelArea(lx - 10, fz - 20, lx + 10, fz + 20);

        // a commander gone into: its calls on the keys
        var cap = sim.Spawn(Kinds.Captain, sim.NearestStand(fx - 4, fz + 22, 2));
        Advance(.3);
        FP.Enter(cap);
        // (looking west, away from the plateau, a little up at a post of stone 14 m off, so «Залп!» has somewhere to fall
        // that is no horde: the ground the way it happens to face may rise too near or fall away out of its sight, and
        // then it looks for the horde instead)
        FP.Face(MathF.PI, .02f);
        int px = (int)cap.X - 110, pz = (int)cap.Z;
        var post = new List<(int x, int y, int z)>();
        for (int z = pz - 2; z <= pz + 2; z++)
            for (int x = px - 2; x <= px + 2; x++)
                for (int y = w.SurfAt(x, z); y < (int)cap.Y + 30; y++) { w.Set(x, y, z, Mat.Stone); post.Add((x, y, z)); }
        await Frames(3);
        bool bar = FP.CallsShown;
        Key_(Act.FpWood);
        bool held = cap.Ready[1] > sim.Time && sw.WallUntil > sim.Time && sw.Ward == cap && sw2.Ward == cap;
        Key_(Act.FpEarth);
        await Frames(2);
        // (no archers near, or none near that can reach it — an archer of the tests before may stand within 50 m of
        // here, wherever the valley put this field: refused in words either way)
        bool refused = cap.Ready[2] <= sim.Time && FP.SaidLine.Contains("стрел");
        foreach (var (x, y, z) in post) w.Set(x, y, z, Mat.Air);
        check("a commander gone into shows its four calls; «Держать строй» on its key sets the swordsmen about it to hold, «Залп!» with no archers is refused in words",
            bar && held && refused, $"calls shown {bar}, held {held} (wall till {sw.WallUntil - sim.Time:0.0} s, ward {sw.Ward?.Id}), refused {refused} («{FP.SaidLine}»)");

        // G: the swordsmen go where it looks
        FP.Face(0, -.28f);
        await Frames(2);
        int dest0 = sw.Dest, rings1 = Rings.Count;
        Key_(Act.FpOrder);
        check("G sends the commander's swordsmen where it looks, with a ring there", sw.Dest != dest0 && sw.Ward == cap && Rings.Count > rings1,
            $"dest {dest0} → {sw.Dest}, rings {rings1} → {Rings.Count}, said «{FP.SaidLine}»");

        // E: into another of ours; that one killed, the view falls dark and lets go
        Key_(Act.FpSwitch);
        await Frames(2);
        var other = FP.Unit;
        bool switched = FP.On && other != null && other != cap && sim.Possessed == other;
        if (other != null) { sim.Hurt(other, null, other.Hp + 1000, "test"); Advance(.1); }
        await Frames(1);
        bool falling = FP.On && FP.Dying;
        for (int i = 0; i < 400 && FP.On; i++) await Frames(1);
        check("E goes over into another of ours; killed, the view falls dark and lets go", switched && falling && !FP.On && !Hud.InFirstPerson,
            $"switched {switched} (to {other?.T.Key} #{other?.Id}), falling {falling}, out {!FP.On}");
        if (FP.On) FP.Exit();
        foreach (var u in new[] { sw, sw2, cap, wk }) if (!u.Dead) { u.Dead = true; u.DiedAt = -999; }
        await Frames(2);
    }
}
