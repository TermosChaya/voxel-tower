using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The self-test's god: the appearance taken in hand from «Таинства» and cast with a click west of the citadel, three
    /// priests of ours praying for it and a band of the horde there; the Archangel comes down and is drawn many times a
    /// man's height; the plate offers it; the possess key with none chosen goes into it, its eyes the view; the left
    /// button sweeps; Esc lets it go; its time over, it goes up and the view lets it go.
    /// </summary>
    async Task SelfTestGods(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var w = World; var sim = Sim;
        const float M = VoxelWorld.PerMetre;
        // (the view put back after as it was: the tests after this one find their ground by it)
        var view = Rig.Aim;
        int gx = Site.KeepX - Site.KeepR - (int)(50 * M), gz = Site.KeepZ;
        ClearTrees(gx - 300, gz - 200, gx + 120, gz + 200);
        var band = new System.Collections.Generic.List<Unit>();
        for (int i = 0; i < 12; i++)
        {
            int c = sim.NearestStand(gx - (int)(20 * M) - i % 4 * 12, gz - 30 + i / 4 * 20, 2);
            var u = sim.Spawn(Kinds.Goblin, c);
            u.Guard = true; u.Home = u.Cell;
            band.Add(u);
        }
        foreach (var k in new[] { SpellKind.Quake, SpellKind.Meteor, SpellKind.Whirlwind, SpellKind.Awaken }) sim.Known.Add(k);
        sim.ReadyNow(SpellKind.Awaken);
        double favourWas = sim.Favour, faithWas = sim.HordeFaith;
        // (the horde's faith low, so that its shamans do not answer with their toad here)
        sim.Favour = 300; sim.HordeFaith = 0;
        // the appearance is prayed for by three priests together
        var priests = new System.Collections.Generic.List<Unit>();
        for (int i = 0; i < 3; i++) priests.Add(sim.Spawn(Kinds.Priest, sim.NearestStand(Site.KeepX + i * 12, Site.KeepZ + Site.KeepR + 30, 2)));
        Selected.Clear(); Hud.OnSelectionChanged();
        // cast with a click, as the player does
        Tools.TakeSpell(SpellKind.Awaken);
        Rig.SetAt(w.WX(gx), w.WY(w.SurfAt(gx, gz)), w.WZ(gz), -1.05f, .45f, .5f);
        await Frames(3);
        var at = Rig.Cam.UnprojectPosition(new Vector3(w.WX(gx + .5f), w.WY(w.SurfAt(gx, gz)), w.WZ(gz + .5f)));
        Tools._UnhandledInput(new InputEventMouseMotion { Position = at });
        await Frames(2);
        Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
        var god = sim.OurGod;
        Tools.SetTool(Tool.None);
        check("the appearance cast with a click, three priests praying: the Archangel comes down there, taking the store of intervention",
            god != null && sim.Favour < 1 && MathF.Abs(god.X - gx) < 4 * M && MathF.Abs(god.Z - gz) < 4 * M,
            god == null ? $"no god; intervention {sim.Favour:0}" : $"at {god.X - gx:0},{god.Z - gz:0} from the spot, intervention {sim.Favour:0}");
        void Restore()
        {
            foreach (var u in priests) if (!u.Dead) { u.Dead = true; u.DiedAt = -999; }
            sim.Favour = favourWas; sim.HordeFaith = faithWas;
        }
        if (god == null) { Restore(); return; }
        Advance(Sim.GodRiseFor + .5);
        await Frames(4);
        bool drawn = GodView.Drawn(god, out float tall), plate = Gods.PlateShown;
        check("down from the sky it is drawn a quarter of the valley high, and a plate offers to go into it",
            god.Act != DeityAct.Rising && drawn && tall > 40 && plate, $"act {god.Act}, drawn {drawn} {tall:0} m high, the plate {plate}");
        // the possess key with none chosen: into it
        Command._UnhandledInput(new InputEventKey { PhysicalKeycode = Bindings.KeyOf(Act.Possess), Keycode = Bindings.KeyOf(Act.Possess), Pressed = true });
        await Frames(3);
        bool inIt = Gods.On && Gods.Cam.IsCurrent() && god.Ridden && !Gods.PlateShown;
        Gods.Face(MathF.PI, -.5f);
        Gods._Input(new InputEventMouseButton { ButtonIndex = Bindings.First, Pressed = true });
        Advance(.2);
        bool swept = god.Act == DeityAct.Sweep;
        Advance(Sim.SweepDone);
        await Frames(2);
        Gods._Input(new InputEventKey { PhysicalKeycode = Key.Escape, Keycode = Key.Escape, Pressed = true });
        await Frames(2);
        bool out_ = !Gods.On && Rig.Cam.IsCurrent() && !god.Ridden;
        check("the possess key with none chosen goes into it, its eyes the view; the left button sweeps; Esc lets it go",
            inIt && swept && out_, $"in it {inIt}, swept {swept}, out {out_}");
        // its time over: up into heaven, and let go by the view
        god.Until = sim.Time;
        Advance(Sim.SweepDone + Sim.GodSinkFor + 1.5);
        await Frames(3);
        bool gone = god.Gone && sim.OurGod == null && !GodView.Drawn(god, out _) && !Gods.PlateShown;
        check("its time over it goes up into heaven, and the view lets it go", gone, $"gone {god.Gone}, drawn {GodView.Drawn(god, out _)}");
        foreach (var u in band) if (!u.Dead) { u.Dead = true; u.DiedAt = -999; }
        Restore();
        Rig.Set(view.x, view.z, view.yaw, view.pitch, view.zoom, snap: true);
    }
}
