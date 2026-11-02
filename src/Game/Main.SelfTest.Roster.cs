using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The household's list narrowed, the map shown large, the citadel's muster and the buildings' kinds: a click on the
    /// shooters' mark by the faces leaves only those who shoot along the top and on the console, a second click all again;
    /// Shift on the mark of the line chooses all the line; the hex of the valley fills the map's ring; {MapBig} shows the
    /// ring large over the lower left corner, the valley filling it, and puts it back on the console; the architect's
    /// tile in the muster, with no masons' yard, opens the town, and with one hires him there at its own price.
    /// </summary>
    async Task SelfTestRoster(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var added = new List<Unit>();
        var kinds = new[] { Kinds.Archer, Kinds.Slinger, Kinds.Swordsman, Kinds.Spearman, Kinds.Rider, Kinds.Captain };
        for (int i = 0; i < 12; i++)
        {
            var t = kinds[i % kinds.Length];
            int c = Sim.NearestStand(Site.KeepX + (i % 6 - 3) * 12, Site.KeepZ + Site.KeepR + 40 + i / 6 * 12, t.Move.Height);
            if (c >= 0) added.Add(Sim.Spawn(t, c));
        }
        Selected.Clear();
        Hud.OnSelectionChanged();
        await Frames(4);
        var bar = Hud.Colonists;
        void Click(Vector2 at, bool shift = false) => bar._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, ShiftPressed = shift });
        var mark = bar.MarkOf(Roster.Shooters);
        if (mark is { } m1) Click(m1);
        await Frames(4);
        bool narrowed = bar.Filter == Roster.Shooters;
        int shown = 0, wrong = 0;
        foreach (var u in Sim.Units)
        {
            if (u.Dead || u.Team != Team.Player || u.T.Called || bar.FaceOf(u) == null) continue;
            shown++;
            if (!Rosters.Holds(Roster.Shooters, u.T)) wrong++;
        }
        Hud.Inspector.Refresh(true);
        await Frames(2);
        bool console = Hud.Inspector.KeyNow.StartsWith("home:Shooters");
        if (bar.MarkOf(Roster.Shooters) is { } m2) Click(m2);
        await Frames(4);
        bool back = bar.Filter == Roster.All;
        if (bar.MarkOf(Roster.Melee) is { } m3) Click(m3, shift: true);
        await Frames(4);
        int melee = 0;
        foreach (var u in Sim.Units) if (!u.Dead && u.Team == Team.Player && Rosters.Holds(Roster.Melee, u.T)) melee++;
        bool chose = Selected.Count == melee && melee > 0 && Selected.All(u => Rosters.Holds(Roster.Melee, u.T));
        check("the shooters' mark by the faces leaves only those who shoot along the top and on the console, a second click all again; Shift on the line's mark chooses all the line",
            mark != null && narrowed && shown > 0 && wrong == 0 && console && back && chose,
            $"narrowed {narrowed}: {shown} faces, {wrong} not shooters, the console «{Hud.Inspector.KeyNow}»; back to all {back}; chosen {Selected.Count} of the line's {melee}");
        bar.Narrow(Roster.All);
        Selected.Clear();
        Hud.OnSelectionChanged();

        // the valley's hex fills its ring; shown large over the lower left corner, and back on the console
        var ring = Hud.Ring;
        float Fill()
        {
            var land = World.Land;
            if (land == null) return 1;
            var map = Hud.Map;
            var c = land.Corners();
            float most = 0;
            var mid = ring.GetGlobalRect().GetCenter();
            foreach (var (x, z) in c) most = MathF.Max(most, (map.GetGlobalRect().Position + new Vector2(x / World.SX * map.Size.X, z / World.SZ * map.Size.Y) - mid).Length());
            return most / (MapRing.Inner * ring.Scale);
        }
        float small = Fill();
        Hud.ToggleBigMap();
        await Frames(4);
        var vp = GetViewport().GetVisibleRect().Size;
        var big = ring.GetGlobalRect();
        float bigFill = Fill(), scale = ring.Scale;
        bool onScreen = big.Position.X >= 0 && big.Position.Y >= 0 && big.End.X <= vp.X + 1 && big.End.Y <= vp.Y + 1;
        Hud.ToggleBigMap();
        await Frames(4);
        bool home = ring.Scale == 1 && ring.GetParent() is BottomConsole.Plate;
        check("the valley's hex fills the map's ring; {MapBig} shows the ring large over the lower left corner, the valley filling it, and puts it back on the console",
            small is > .9f and < 1.02f && scale > 1.25f && onScreen && bigFill is > .9f and < 1.02f && home,
            $"the hex's corners at {small:0.00} of the ring; large ×{scale:0.0}, at {big}, filled {bigFill:0.00}; back {home}");

        // the architect's tile in the muster: no masons' yard, it opens the town; with one, it hires him there (those
        // put in for the list gone first, for the household to have a place for him)
        foreach (var u in added) { u.Dead = true; u.DiedAt = -999; }
        added.Clear();
        await Frames(2);
        Command.OpenKeep();
        Hud.Inspector.Refresh(true);
        await Frames(3);
        var tile = Hud.Inspector.TileOf(Kinds.Architect);
        Sim.Buildings.RemoveAll(b => b.Kind == "masons");
        tile?.Press();
        await Frames(3);
        bool town = Hud.PanelOpen == "town";
        Hud.ClosePanel();
        var yard = new Building { Kind = "masons", X0 = Site.KeepX + 60, Z0 = Site.KeepZ + 60, X1 = Site.KeepX + 92, Z1 = Site.KeepZ + 100, Level = 1, Ready = true };
        yard.Stock[Kinds.Architect.Key] = 1;
        Sim.Buildings.Add(yard);
        foreach (var u in Sim.Units) if (u.T == Kinds.Architect && !u.Dead) { u.Dead = true; u.DiedAt = -999; }
        Sim.Stock.Gold = Math.Max(Sim.Stock.Gold, 500);
        double gold0 = Sim.Stock.Gold;
        int price = Sim.HireCost(Kinds.Architect), arch0 = Sim.Count(Kinds.Architect);
        string tip = tile?._GetTooltip(Vector2.Zero) ?? "";
        tile?.Press();
        await Frames(3);
        bool hired = Sim.Count(Kinds.Architect) == arch0 + 1 && Math.Abs(gold0 - Sim.Stock.Gold - price) < .01;
        Sim.Buildings.Remove(yard);
        foreach (var u in Sim.Units) if (u.T == Kinds.Architect && !u.Dead) { u.Dead = true; u.DiedAt = -999; }
        Selected.Clear();
        Hud.OnSelectionChanged();
        check("the architect's tile in the citadel's muster opens the town with no masons' yard, and with one hires him there at its own price, the tooltip saying where",
            tile != null && town && hired && tip.Contains($"{price} золота") && tip.Contains("Двор каменщиков"),
            $"the town opened {town}; hired {hired} for {price}; «{tip.Split('\n')[^1]}»");
        foreach (var u in added) { u.Dead = true; u.DiedAt = -999; }
        await Frames(3);
    }
}
