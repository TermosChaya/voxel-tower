using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// Iron through the game's own input (Sim.Metal.cs): the beam shows the store of metal; {MatMetal} takes up iron for
    /// the line, and a wall drawn with it on level ground is marked in plates to be laid out of that store, the plan
    /// pricing it in metal; the «Рудокопня» taken
    /// from the «Город» tab marks the outcrops it may stand on, is refused on bare ground and priced on an outcrop, and
    /// its marks go when it is put away. Cleared away after.
    /// </summary>
    async Task SelfTestMetal(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var w = World; var sim = Sim;
        Vector2 Screen(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(w.WX(x + .5f), w.WY(w.SurfAt(x, z)), w.WZ(z + .5f)));
        void Move(Vector2 p) => Tools._UnhandledInput(new InputEventMouseMotion { Position = p });
        void Button(Vector2 p, bool down) => Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = p });
        // what the tool says of the wall while it is drawn
        string said = "";
        async Task Drag(int ax, int az, int bx, int bz)
        {
            var a = Screen(ax, az); var b = Screen(bx, bz);
            Move(a); await Frames(2);
            Button(a, true);
            for (int i = 1; i <= 8; i++) { Move(a.Lerp(b, i / 8f)); await Frames(1); }
            await GhostCaughtUp();
            said = Tools.PlanText ?? "";
            Button(b, false);
            await Frames(2);
        }

        // the store of metal on the beam
        sim.Stock[Stockpile.Metal] = 6 * VoxelWorld.PerM3;
        await Frames(120);
        string shown = Hud.StoreShown(Stockpile.Metal);

        // a field of level turf, bare of trees, south of the citadel
        int fx = Site.KeepX - Site.KeepR - 300, fz = Site.KeepZ - 160, g = w.SurfAt(fx, fz);
        ClearTrees(fx - 60, fz - 40, fx + 60, fz + 40);
        for (int z = fz - 34; z <= fz + 34; z++)
            for (int x = fx - 54; x <= fx + 54; x++)
                for (int y = g - 6; y < g + 40; y++)
                {
                    byte want = y < g ? (y == g - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
        if (sim.Alarm) sim.StandDown();
        Rig.SetAt(w.WX(fx), w.WY(g), w.WZ(fz), -.66f, .6f, 1.6f);
        await Frames(6);

        // {MatMetal} with the line in hand: a wall of iron plate marked, to be laid out of the store of metal
        Tools.SetTool(Tool.Line);
        Tools._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Comma, Keycode = Key.Comma, Pressed = true });
        bool iron = Tools.Opt.Material == Mat.Plate;
        await Drag(fx - 30, fz, fx + 30, fz);
        int plates = 0;
        for (int z = fz - 8; z <= fz + 8; z++) for (int x = fx - 36; x <= fx + 36; x++) for (int y = g; y < g + 40; y++) if (sim.Orders.At(x, y, z) == Mat.Plate) plates++;
        long fromMetal = 0;
        foreach (int col in sim.Orders.Active) fromMetal += sim.Orders.ColLay[Stockpile.Metal][col];
        Tools.SetTool(Tool.None);
        check("the beam shows the store of metal; {MatMetal} takes up iron, and a wall drawn with it is marked in plates to be laid out of the store, priced in metal",
            shown != null && shown.StartsWith("6") && iron && plates > 200 && fromMetal >= plates && said.Contains("металл"),
            $"the beam «{shown}»; iron in hand {iron}; {plates} plates marked, {fromMetal} to lay from the metal; the plan «{said}»");
        sim.CancelArea(fx - 64, fz - 44, fx + 64, fz + 44);

        // the miners' building from the «Город» tab: the outcrops marked, refused on bare ground, priced on an outcrop
        var kind = Sim.TownKindOf("mine");
        sim.Learnt.Add("b_smithy"); sim.Learnt.Add(kind.Lore);
        sim.Stock.Gold = Math.Max(sim.Stock.Gold, 2000);
        Hud.OpenPanel("town");
        await Frames(30);
        Button put = null;
        void Walk(Node n)
        {
            if (put == null && n is Button b && b.Text.StartsWith("Поставить")) put = b;
            foreach (var c in n.GetChildren()) Walk(c);
        }
        foreach (var c in Hud.Town.GetChildren())
            if (c is VBoxContainer rows)
                foreach (var row in rows.GetChildren())
                    foreach (var col in row.GetChildren())
                        if (col is VBoxContainer v && v.GetChildCount() > 0 && v.GetChild(0) is Label l && l.Text.StartsWith(kind.Name)) Walk(v);
        put?.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        bool inHand = Tools.Tool == Tool.Building && Tools.Placing == kind;
        int marks = Tools.OutcropMarks;
        Move(Screen(fx, fz));
        await Frames(4);
        string bare = Tools.PlanText;
        // an outcrop with its ore in it, looked at
        (int X, int Z, int Y, int R) crop = (-1, -1, -1, -1);
        foreach (var o in Site.Outcrops) if (sim.OreLeft(o.X, o.Z, o.R) >= 40) { crop = o; break; }
        string onOre = null;
        if (crop.X >= 0)
        {
            Rig.SetAt(w.WX(crop.X), w.WY(crop.Y), w.WZ(crop.Z), -.66f, .6f, 1.6f);
            await Frames(6);
            Move(Screen(crop.X, crop.Z));
            await Frames(4);
            onOre = Tools.PlanText;
        }
        Tools.SetTool(Tool.None);
        await Frames(2);
        check("the «Рудокопня» taken from the «Город» tab marks the outcrops of ore, is refused on bare ground and priced on an outcrop, and its marks go when it is put away",
            put != null && inHand && marks > 0 && bare != null && bare.Contains("рудный выход") && onOre != null && onOre.StartsWith(kind.Name + " ·") && Tools.OutcropMarks == 0,
            $"the button {(put == null ? "not found" : $"«{put.Text}»")}; in hand {inHand}; {marks} outcrops marked; on bare ground «{bare}»; on an outcrop «{onOre}»; marks after {Tools.OutcropMarks}");
        Hud.ClosePanel();
        sim.Learnt.Remove(kind.Lore);
        await Frames(2);
    }
}
