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
        // (no archers near: those of the tests before, wherever they stood, sent across the valley for the call, and
        // back after it — one within 50 m that could reach the post looses the volley, as it should)
        var bows = sim.Units.FindAll(u => !u.Dead && !u.Inside && u.Team == Team.Player && u.T.Ranged);
        var bowsAt = bows.ConvertAll(u => u.Cell);
        foreach (var b in bows)
        {
            int far = sim.NearestStand((int)b.X + (b.X > w.SX / 2 ? -500 : 500), (int)b.Z, b.T.Move.Height);
            if (far >= 0) { b.Cell = far; (b.X, b.Y, b.Z) = sim.Nav.Feet(far); b.Path = null; }
        }
        Key_(Act.FpEarth);
        await Frames(2);
        bool refused = cap.Ready[2] <= sim.Time && FP.SaidLine.Contains("стрел");
        for (int i = 0; i < bows.Count; i++) { var b = bows[i]; b.Cell = bowsAt[i]; (b.X, b.Y, b.Z) = sim.Nav.Feet(bowsAt[i]); b.Path = null; }
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

        // a commander holding the middle of a square of walls with towers at its corners: the archers on the towers stay
        // there, those on the ground go up onto the towers or the walls, none comes down to the spot (Sim.Captains.Posts.cs)
        int x0 = (fx - 40) & ~7, z0 = (fz - 40) & ~7, x1 = x0 + 80, z1 = z0 + 80;
        var ring = new VoxelList();
        for (int z = z0; z < z1; z++)
            for (int x = x0; x < x1; x++)
            {
                bool tower = (x < x0 + 24 || x >= x1 - 24) && (z < z0 + 24 || z >= z1 - 24);
                if (!tower && x >= x0 + 16 && x < x1 - 16 && z >= z0 + 16 && z < z1 - 16) continue;
                for (int y = g; y < g + (tower ? 32 : 24); y++) ring.Add(x, y, z, Mat.Stone);
            }
        // (laid straight into the world, not remembered as built: taken away after, nobody mends it)
        for (int i = 0; i < ring.Count; i++) w.Set(ring.X(i), ring.Y(i), ring.Z(i), Mat.Stone);
        for (int t = 0; t < 2; t++) for (int y = g; y < g + 26; y++) w.Set(x1 - 18, y, (z0 + z1) / 2 + t, Mat.Ladder);
        // (no raider of the tests before within the commander's sight: it holds its spot, it does not stand against one)
        foreach (var o in sim.Units)
            if (!o.Dead && o.Team == Team.Horde && (o.X - fx) * (o.X - fx) + (o.Z - fz) * (o.Z - fz) < 130 * 130 * 64) { o.Dead = true; o.DiedAt = -999; }
        Advance(.3);
        var high = new List<Unit>();
        foreach (var (tx, tz) in new[] { (x0 + 12, z0 + 12), (x0 + 12, z1 - 12), (x1 - 12, z0 + 12), (x1 - 12, z1 - 12) })
            high.Add(sim.Spawn(Kinds.Archer, sim.StandNear(tx, g + 34, tz, 2)));
        var low = new[] { sim.Spawn(Kinds.Archer, sim.NearestStand(x0 + 40, z0 + 30, 2)), sim.Spawn(Kinds.Archer, sim.NearestStand(x0 + 40, z0 + 50, 2)) };
        var tops = new List<int>();
        foreach (var a in high) tops.Add(a.Post);
        var keeper = sim.Spawn(Kinds.Captain, sim.NearestStand(x0 + 44, z0 + 40, 2));
        sim.Command(new[] { keeper }, keeper.Cell);
        keeper.Goal = keeper.Dest;
        for (int i = 0; i < 40; i++) { Advance(.2); await Frames(1); }
        bool stayed = true, upped = true;
        for (int i = 0; i < high.Count; i++) stayed &= high[i].Post == tops[i] && tops[i] >= 0;
        foreach (var a in low) upped &= a.Post >= 0 && sim.Nav.Feet(a.Post).y >= g + 16;
        string Up(Unit a) => a.Post < 0 ? "no post" : $"{(sim.Nav.Feet(a.Post).y - g) / 8:0.0} m up";
        check("a commander holding a spot inside walls keeps the archers on the towers over it and sends those below up onto the works",
            stayed && upped, $"on the towers: {string.Join(", ", high.ConvertAll(a => Up(a) + (a.Post == tops[high.IndexOf(a)] ? "" : " (moved)")))}; from the ground: {Up(low[0])}, {Up(low[1])}; «{keeper.Why}»");
        foreach (var u in high) { u.Dead = true; u.DiedAt = -999; }
        foreach (var u in low) { u.Dead = true; u.DiedAt = -999; }
        keeper.Dead = true; keeper.DiedAt = -999;
        for (int i = 0; i < ring.Count; i++) w.Set(ring.X(i), ring.Y(i), ring.Z(i), Mat.Air);
        for (int t = 0; t < 2; t++) for (int y = g; y < g + 26; y++) w.Set(x1 - 18, y, (z0 + z1) / 2 + t, Mat.Air);
        Advance(.3);
        await Frames(2);
    }
}
