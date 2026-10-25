using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The «Обустройство» tool through its input, on a field of turf evened out west of the citadel: a cobbled way drawn
    /// along the ground marks the turf under it to be taken up and cobbles to be laid in its place, and Ctrl+Z takes that
    /// back; drawn again by many hands it lies flush with the turf; {Form} goes on through the palette; a campfire set
    /// out with a click is put together and lit, a bench beside it gives two seats; the campfire's stones taken down,
    /// it is let go and its fire put out. Cleared away after.
    /// </summary>
    async Task SelfTestYard(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var w = World; var sim = Sim;
        Vector2 Screen(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(w.WX(x + .5f), w.WY(w.SurfAt(x, z)), w.WZ(z + .5f)));
        void Move(Vector2 p) => Tools._UnhandledInput(new InputEventMouseMotion { Position = p });
        void Button(Vector2 p, bool down) => Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = p });
        // what the tool says of the way while it is drawn
        string said = "";
        async Task Drag(int ax, int az, int bx, int bz)
        {
            var a = Screen(ax, az); var b = Screen(bx, bz);
            Move(a); await Frames(2);
            Button(a, true);
            for (int i = 1; i <= 8; i++) { Move(a.Lerp(b, i / 8f)); await Frames(1); }
            said = Tools.PlanText ?? "";
            Button(b, false);
            await Frames(2);
        }
        async Task Click(int x, int z) { var p = Screen(x, z); Move(p); await Frames(3); Button(p, true); Button(p, false); await Frames(2); }
        int Removals() => sim.Orders.Total - sim.Orders.BuildTotal;

        // a field of level turf, bare of trees
        int fx = Site.KeepX - Site.KeepR - 420, fz = Site.KeepZ + 120, g = w.SurfAt(fx, fz);
        ClearTrees(fx - 70, fz - 50, fx + 70, fz + 50);
        for (int z = fz - 44; z <= fz + 44; z++)
            for (int x = fx - 64; x <= fx + 64; x++)
                for (int y = g - 6; y < g + 40; y++)
                {
                    byte want = y < g ? (y == g - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
        if (sim.Alarm) sim.StandDown();
        Rig.SetAt(w.WX(fx), w.WY(g), w.WZ(fz), -.66f, .6f, 1.6f);
        await Frames(6);

        // a cobbled way, a metre and a half wide, drawn across the field
        Tools.SetTool(Tool.Yard);
        Tools.PickYard(2);
        Tools.Opt.Width = 12;
        var way = Sim.WayPlan(w, Site, fx - 40, fz - 10, fx + 40, fz - 10, 12, WayKind.Cobble);
        int r0 = Removals();
        await Drag(fx - 40, fz - 10, fx + 40, fz - 10);
        int marked = Removals() - r0, relays = sim.Relays.Count;
        var z1 = new InputEventKey { PhysicalKeycode = Key.Z, Keycode = Key.Z, Pressed = true, CtrlPressed = true };
        Tools._UnhandledInput(z1);
        int undone = Removals() - r0;
        Advance(.6);
        check("a cobbled way drawn with «Обустройство» marks the turf under it to be taken up and cobbles to be laid in its place; Ctrl+Z takes it back",
            way.Count > 600 && said.Contains("Булыжник") && marked >= way.Count * 9 / 10 && marked <= way.Count && relays == marked && undone == 0 && sim.Relays.Count == 0,
            $"{way.Count} columns, marked {marked}, to be laid {relays} («{said}»); after Ctrl+Z {undone} marked, {sim.Relays.Count} to be laid");

        // drawn again by many hands: laid at once, flush with the turf
        sim.InstantLabour = true;
        await Drag(fx - 40, fz - 10, fx + 40, fz - 10);
        // (where someone stands on the way its stone waits till they step off)
        for (int i = 0; i < 6; i++) { Advance(1); sim.CompleteAll(); }
        int laid = 0, flush = 0;
        for (int i = 0; i < way.Count; i++)
        {
            int x = way.X(i), y = way.Y(i), z = way.Z(i);
            if (w.Get(x, y, z) is Mat.Cobble or Mat.Flag) laid++;
            if (w.SurfAt(x, z) == g) flush++;
        }
        check("the way laid lies flush with the turf, of cobbles", laid >= way.Count * 9 / 10 && flush >= way.Count - 2, $"{laid} of {way.Count} laid, {flush} flush");

        // the palette: {Form} goes on to the next; a campfire set out with a click is put together and lit
        Tools.ToggleForm();
        bool stepped = Tools.Opt.YardPick == 3;
        Tools.PickYard(BuildTools.WayCount + Array.IndexOf(Yard.All, DecorKind.Campfire));
        int yards0 = sim.Yards.Count, seats0 = sim.Seats.Count;
        // (nobody of the household standing where the campfire and the bench go: nothing is built into anyone)
        var aside = new List<Unit>();
        foreach (var u in sim.Units)
            if (!u.Dead && !u.Inside && !u.Hold && u.X > fx - 16 && u.X < fx + 40 && Math.Abs(u.Z - (fz + 20)) < 16)
            {
                u.Cell = sim.NearestStand(fx + 12, fz + 64, u.T.Move.Height); (u.X, u.Y, u.Z) = sim.Nav.Feet(u.Cell); u.Path = null;
                u.Hold = true; u.Dest = u.Post = u.Cell;
                aside.Add(u);
            }
        await Click(fx, fz + 20);
        var fire = sim.Yards.Count > yards0 ? sim.Yards[^1] : null;
        Advance(1.2);
        Tools.PickYard(BuildTools.WayCount + Array.IndexOf(Yard.All, DecorKind.Bench));
        await Click(fx + 24, fz + 20);
        var bench = sim.Yards.Count > yards0 + 1 ? sim.Yards[^1] : null;
        // (the bench's seats are counted once it stands and nobody of the household is in the way of it)
        for (int i = 0; i < 10 && sim.Seats.Count - seats0 < 2; i++) Advance(.6);
        int seats = sim.Seats.Count - seats0;
        foreach (var u in aside) u.Hold = false;
        check("{Form} goes on through the palette; a campfire set out with a click is put together and lit, a bench beside it gives two seats",
            stepped && fire is { Kind: DecorKind.Campfire, Ready: true, Light: not null } && sim.Fixtures.Contains(fire.Light) && sim.Yards.Count == yards0 + 2 && seats == 2,
            $"stepped {stepped}; campfire {(fire == null ? "not set out" : $"ready {fire.Ready}, lit {fire.Light != null}")}; things {sim.Yards.Count - yards0}, bench {(bench == null ? "not set out" : $"{bench.Kind} ready {bench.Ready}")}, seats {seats}");

        // the campfire taken down: let go, its fire out
        if (fire != null)
        {
            sim.DesignateRemove(fire.Voxels);
            Advance(1.2);
            check("a campfire taken down is let go and its fire put out", !sim.Yards.Contains(fire) && !sim.Fixtures.Contains(fire.Light),
                $"still kept {sim.Yards.Contains(fire)}, still burning {fire.Light != null && sim.Fixtures.Contains(fire.Light)}");
        }
        sim.InstantLabour = false;
        Tools.SetTool(Tool.None);
        foreach (var y in sim.Yards.ToArray()) sim.DesignateRemove(y.Voxels);
        sim.CompleteAll();
        Advance(1.2);
        sim.CancelArea(fx - 64, fz - 44, fx + 64, fz + 44);
        sim.Relays.Clear();
        await Frames(2);
    }
}
