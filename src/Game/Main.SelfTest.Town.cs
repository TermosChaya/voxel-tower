using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// A town building put up through the «Город» tab as the player does it (Sim.Town.cs): the barracks' lore learnt,
    /// its «Поставить» pressed, its plan following the cursor and a click setting it down; built at once and furnished
    /// by the workers, it is called «Казарма», its plate stands over it, and the tab hires a spearman out of its door,
    /// which the citadel will not; its sign, torch, chimney and racks go up before it (Sim.Frontage.cs), and the edge of
    /// its room no longer lies along its roof; a smithy put up beside it rings with its hammer by day.
    /// </summary>
    async Task SelfTestTown(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        Vector2 Screen(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(World.WX(x + .5f), World.WY(World.SurfAt(x, z)), World.WZ(z + .5f)));
        void Button(Vector2 p, bool down) => Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = p });
        // the buttons of a kind's row in the tab: under its name
        List<Button> Buttons(string name)
        {
            var found = new List<Button>();
            void Walk(Node n, bool inRow)
            {
                if (n is Label l && l.Text.StartsWith(name)) inRow = true;
                if (inRow && n is Button b) found.Add(b);
                foreach (var c in n.GetChildren()) Walk(c, inRow);
            }
            foreach (var c in Hud.Town.GetChildren())
                if (c is VBoxContainer rows)
                    foreach (var row in rows.GetChildren())
                        foreach (var col in row.GetChildren())
                            if (col is VBoxContainer v && v.GetChildCount() > 0 && v.GetChild(0) is Label l && l.Text.StartsWith(name)) Walk(v, true);
            return found;
        }
        var kind = Sim.TownKindOf("barracks");
        // level open ground south-west of the citadel, cleared of trees, room for the barracks either way round
        bool Level(int x, int z)
        {
            int h = World.SurfAt(x, z);
            for (int dz = -36; dz <= 36; dz += 6)
                for (int dx = -36; dx <= 36; dx += 6)
                    if (!World.InBounds(x + dx, 1, z + dz) || Math.Abs(World.SurfAt(x + dx, z + dz) - h) > 1 || Site.Forbidden(x + dx, z + dz)
                        || Sim.BuildingAt(x + dx, z + dz) != null || Sim.HouseUnder(x + dx - 2, z + dz - 2, x + dx + 2, z + dz + 2) != null) return false;
            return true;
        }
        var (hx, hz) = Nearest(Site.KeepX - Site.KeepR - 120, Site.KeepZ + 100, 200, Level);
        ClearTrees(hx - 50, hz - 50, hx + 50, hz + 50);
        if (Sim.Alarm) Sim.StandDown();
        Sim.Learnt.Add(kind.Lore);
        Sim.Stock.Gold = Math.Max(Sim.Stock.Gold, 2000);
        Rig.Focus(World.WX(hx), World.WZ(hz));
        Hud.OpenPanel("town");
        await Frames(30);
        // with no town hall yet, the tab says to put one up first
        string advice = Hud.Town.Advice;
        check("the «Город» tab says what to build next: with no town hall, the hall", advice != null && advice.Contains("ратуш"), $"«{advice}»");
        // (the tab is laid out again as things change: what a button said is read before it is pressed)
        var put = Buttons(kind.Name).Find(b => b.Text.StartsWith("Поставить"));
        string putText = put?.Text;
        put?.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        bool inHand = Tools.Tool == Tool.Building && Tools.Placing == kind;
        Tools._UnhandledInput(new InputEventMouseMotion { Position = Screen(hx, hz) });
        await Frames(4);
        string planned = Tools.PlanText;
        // {Form} steps to the kind's next look: the plan says which, and it goes up in it
        var looks = Sim.LooksOf(kind);
        int byPlace = Sim.DefaultLook(kind, hx, hz);
        Tools.ToggleForm();
        await Frames(4);
        string stepped = Tools.PlanText;
        int picked = Tools.PickedLook;
        var at = Screen(hx, hz);
        Button(at, true);
        await Frames(1);
        Button(at, false);
        await Frames(2);
        var b = Sim.BuildingOf(kind.Key);
        bool putAway = Tools.Tool == Tool.None;
        check("the barracks is put up from the «Город» tab: «Поставить» takes up its plan, the plan follows the cursor with its price, a click sets it down for the workers and puts the plan away",
            put != null && inHand && planned != null && planned.StartsWith(kind.Name) && b != null && putAway,
            $"the button {(put == null ? "not found" : $"«{putText}»")}; in hand {inHand}; the plan «{planned}»; put up {b != null}; put away {putAway}");
        check("{Form} with a building in hand steps to its next look: the plan names it, and the building goes up in it",
            looks.Length >= 2 && planned != null && planned.Contains($"«{looks[byPlace].Name}»") && picked == (byPlace + 1) % looks.Length
            && stepped != null && stepped.Contains($"«{looks[picked].Name}»") && b?.Variant == picked,
            $"{looks.Length} looks; by its place «{looks[byPlace].Name}», the plan «{planned}»; after {{Form}} picked {picked}, the plan «{stepped}»; put up in {b?.Variant}");
        if (b == null) { Hud.ClosePanel(); return; }

        // the walls, floor and roof laid at once, as by many hands; the door cut and hung by the household, then the
        // furnishing laid at once
        var zone = Sim.ZoneOf(b);
        for (int i = 0; i < 120 && !b.Ready; i++)
        {
            Sim.CompleteAll();
            Advance(1);
            if (zone != null) Sim.LookOver(zone);
        }
        await Frames(30);
        string plate = Plates.PlateOf(b);
        int spears = Sim.Count(Kinds.Spearman);
        var hire = Buttons(kind.Name).Find(x => x.Text.StartsWith(Kinds.Spearman.Name));
        bool canHire = hire is { Disabled: false };
        string hireText = hire?.Text;
        hire?.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        string atCitadel = Sim.CantEnlist(Kinds.Spearman);
        check("built and furnished, the barracks is called «Казарма», its plate stands over it, and the tab hires a spearman out of it, which the citadel will not",
            b.Ready && zone?.Name == kind.Name && plate != null && plate.StartsWith(kind.Name) && canHire && Sim.Count(Kinds.Spearman) == spears + 1 && atCitadel != null,
            $"ready {b.Ready} (level {b.Level}), called «{zone?.Name}»; the plate «{plate}»; the hire button {(hire == null ? "not found" : $"«{hireText}», {(canHire ? "enabled" : "disabled")}")}; " +
            $"spearmen {spears} → {Sim.Count(Kinds.Spearman)}; the citadel «{atCitadel}»");
        Hud.ClosePanel();
        await Frames(2);

        // working, it shows its trade outside: its sign by the door, a torch the other side, its chimney, its spears and
        // shields; the straw edge of its room shows over its roof no more, but for while the areas' brush is in hand
        Sim.DressTown();
        await Frames(20);
        var front = Sim.FrontageOf(b);
        int Of(FixtureKind k) => front.FindAll(f => f.Kind == k).Count;
        bool hidden = zone != null && !Areas.Shows(zone);
        Tools.SetTool(Tool.Gather);
        await Frames(20);
        bool shown = zone != null && Areas.Shows(zone);
        Tools.SetTool(Tool.None);
        await Frames(2);
        check("working, the barracks shows its trade outside: a sign by its door, a torch, a chimney and its racks; the edge of its room shows only while the areas' brush is in hand",
            Of(FixtureKind.Sign) == 1 && Of(FixtureKind.Torch) >= 1 && Of(FixtureKind.Chimney) == 1 && Of(FixtureKind.Trade) == 2 && hidden && shown,
            $"{string.Join(", ", front.ConvertAll(f => f.Kind.ToString()))}; the edge hidden {hidden}, with the brush {shown}");

        // a smithy beside it, put up at once: by day its hammer is heard on the anvil where the view is
        var smith = Sim.TownKindOf("smithy");
        Sim.Learnt.Add(smith.Lore);
        Sim.Stock[Stockpile.Metal] += 2.0 * VoxelWorld.PerM3;
        var (sx, sz) = Nearest(hx, hz + 110, 160, Level);
        ClearTrees(sx - 50, sz - 50, sx + 50, sz + 50);
        Sim.InstantLabour = true;
        Sim.PutUp(smith, sx, sz, false, out var forge);
        for (int i = 0; i < 60 && forge is { Ready: false }; i++) { Sim.CompleteAll(); Advance(1); if (Sim.ZoneOf(forge) is { } fz) Sim.LookOver(fz); }
        Sim.InstantLabour = false;
        Rig.Focus(World.WX(sx), World.WZ(sz));
        Sound.Heard.Remove("anvil");
        bool dark = Sim.Clock.Darkness > .5f;
        for (int i = 0; i < 180 && !Sound.Heard.Contains("anvil"); i++) await Frames(1);
        check("by day the smith's hammer is heard on the anvil where the view is", forge is { Ready: true } && (dark || Sound.Heard.Contains("anvil")),
            $"the smithy {(forge == null ? "not put up" : forge.Ready ? "ready" : "not ready")}; night {dark}; heard {Sound.Heard.Contains("anvil")}");
        // (and let go again, so that what it gives does not change the tests after)
        if (forge != null) { Sim.RemoveZones(forge.X0, forge.Z0, forge.X1, forge.Z1); Advance(3); }
    }
}
