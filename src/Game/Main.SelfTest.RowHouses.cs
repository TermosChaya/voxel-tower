using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// Houses side by side and their doors, through the tools' input as the player has them (25.10.2026: «поставила два
    /// домика рядом — двери оказались недоступны»): on a field evened out, a house drawn with the rectangle's house form,
    /// and a second drawn right up against the wall its door is in, its ghost saying the wall is shared; built, the two
    /// share it, the first one's door in it walled up and cut on a free side, both closed with doors that open outside.
    /// The gate tool on a wall of the second where outside is free puts a door there, cut and hung; on its old door, walls
    /// it up; whoever goes in goes in by the new one. The library's «Декор» tab sets out a campfire with a click. Cleared
    /// away after.
    /// </summary>
    async Task SelfTestRowHouses(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var w = World; var sim = Sim;
        const int t = Shapes.HouseWall;
        Vector2 Screen(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(w.WX(x + .5f), w.WY(w.SurfAt(x, z)), w.WZ(z + .5f)));
        // (on the field's turf, not on the eaves of a house standing over it)
        int g0 = 0;
        Vector2 OnField(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(w.WX(x + .5f), w.WY(g0), w.WZ(z + .5f)));
        void Move(Vector2 p) => Tools._UnhandledInput(new InputEventMouseMotion { Position = p });
        void Button(Vector2 p, bool down) => Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = p });
        string said = "", hovered = "";
        async Task Drag(int ax, int az, int bx, int bz)
        {
            var a = OnField(ax, az); var b = OnField(bx, bz);
            Move(a); await Frames(2);
            Button(a, true);
            for (int i = 1; i <= 8; i++) { Move(a.Lerp(b, i / 8f)); await Frames(1); }
            await GhostCaughtUp();
            said = Tools.PlanText ?? "";
            Button(b, false);
            await Frames(2);
        }
        // the camera put square before a face, a few metres out from it, and the face clicked where it is
        async Task ClickFace(float x, float y, float z, int nx, int nz)
        {
            // (the camera stands off the way of its yaw: sin east, cos south; looking well down at it, over the field's
            // banks)
            Rig.SetAt(w.WX(x), w.WY(y), w.WZ(z), MathF.Atan2(nx, nz), 1f, 1.1f);
            await Frames(4);
            var p = Rig.Cam.UnprojectPosition(new Vector3(w.WX(x), w.WY(y), w.WZ(z)));
            Move(p); await GhostCaughtUp();
            said = Tools.PlanText ?? "";
            hovered = (Tools.HoverGate != null ? $"gate {Tools.HoverGate.Id}" : "no gate") + (Tools.Hovered is { } h ? $" at {h.X},{h.Y},{h.Z} ({h.NX},{h.NY},{h.NZ}) {Mat.NameOf(h.M)}" : " over nothing") + $", screen {p}";
            Button(p, true); Button(p, false);
            await Frames(2);
        }
        // built at once by many hands, the doors cut and hung, the rooms furnished
        void Build()
        {
            for (int i = 0; i < 40; i++)
            {
                sim.CompleteAll();
                Advance(1);
                if (sim.Orders.Total == 0 && sim.HouseDoors.Count == 0 && i > 4) break;
            }
        }
        Zone HouseAt(int x, int z) => sim.Zones.Find(h => h.Kind == Zone.Dwelling && x >= h.X0 && x <= h.X1 && z >= h.Z0 && z <= h.Z1);
        bool Out(Zone h) => sim.DoorsOf(h) is { Count: > 0 } doors && doors.TrueForAll(g => g.Ready && sim.DoorLeadsOut(g, h));

        // a field of level turf a good way from the citadel, on the land, bare of trees, clear of what the tests before
        // set out (a dwelling or a building of the town there holds its ground even with its walls gone)
        bool Field(int x, int z)
        {
            if (sim.Zones.Exists(h => h.X1 >= x - 110 && h.X0 <= x + 110 && h.Z1 >= z - 90 && h.Z0 <= z + 90)) return false;
            for (int dz = -80; dz <= 80; dz += 10)
                for (int dx = -100; dx <= 100; dx += 10)
                    if (!w.InBounds(x + dx, 1, z + dz) || !w.OnLand(x + dx, z + dz) || Site.Forbidden(x + dx, z + dz)) return false;
            // (and no water on it: evened out, a pond in it runs back over it)
            for (int dz = -90; dz <= 90; dz += 6)
                for (int dx = -110; dx <= 110; dx += 6)
                    if (!w.InBounds(x + dx, 1, z + dz) || w.Get(x + dx, w.SurfAt(x + dx, z + dz), z + dz) == Mat.Water) return false;
            return true;
        }
        // (rings about the citadel, the nearest first)
        int fx = -1, fz = -1;
        for (int r = Site.KeepR + 200; r < 700 && fx < 0; r += 20)
            for (int k = 0; k < 36 && fx < 0; k++)
            {
                int x = Site.KeepX + (int)(MathF.Cos(k * MathF.Tau / 36) * r), z = Site.KeepZ + (int)(MathF.Sin(k * MathF.Tau / 36) * r);
                if (Field(x, z)) (fx, fz) = (x, z);
            }
        if (fx < 0) { check("a field for houses side by side", false, "none found about the citadel"); return; }
        // (the turf at the middle height of the field, so that its banks are low; the faces clicked are looked down
        // at over them)
        {
            var hs = new System.Collections.Generic.List<int>();
            for (int dz = -80; dz <= 80; dz += 8) for (int dx = -100; dx <= 100; dx += 8) hs.Add(w.SurfAt(fx + dx, fz + dz));
            hs.Sort();
            g0 = hs[hs.Count / 2];
        }
        // (the trees about it felled too: the camera looks in over them, and a crown in the way takes the click)
        ClearTrees(fx - 220, fz - 200, fx + 220, fz + 200);
        for (int z = fz - 80; z <= fz + 80; z++)
            for (int x = fx - 100; x <= fx + 100; x++)
            {
                int s = w.SurfAt(x, z);
                for (int y = Math.Max(1, Math.Min(s, g0) - 2); y < Math.Max(s, g0) + 64 && y < w.SY; y++)
                {
                    byte want = y < g0 ? (y == g0 - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
            }
        sim.CancelArea(fx - 100, fz - 80, fx + 100, fz + 80);
        if (sim.Alarm) sim.StandDown();
        sim.Stock[Stockpile.Stone] = Math.Max(sim.Stock[Stockpile.Stone], 600.0 * VoxelWorld.PerM3);
        sim.Stock[Stockpile.Wood] = Math.Max(sim.Stock[Stockpile.Wood], 400.0 * VoxelWorld.PerM3);
        Rig.SetAt(w.WX(fx), w.WY(g0), w.WZ(fz), -.4f, .7f, 2.4f);
        await Frames(6);

        // ---- a house, and a second drawn right up against the wall its door is in, a voxel off it
        Tools.SetTool(Tool.Box);
        Tools.Opt.Dig = false; Tools.Opt.Hollow = true; Tools.Opt.House = true; Tools.Opt.Material = Mat.Stone; Tools.Opt.HouseDoor = -1;
        int ax0 = fx - 30, az0 = fz - 18, ax1 = ax0 + 55, az1 = az0 + 35;
        await Drag(ax0, az0, ax1, az1);
        var a = HouseAt(ax0 + 20, az0 + 10);
        int doorIdx = sim.HouseDoors.FindIndex(d => a != null && d.door.X >= a.X0 && d.door.X <= a.X1 && d.door.Z >= a.Z0 && d.door.Z <= a.Z1);
        if (a == null || doorIdx < 0) { check("a house drawn with the house form has its door waiting", false, $"house {a != null}, door {doorIdx}"); return; }
        int sideA = sim.HouseDoors[doorIdx].door.Side;
        Build();
        var gatesA0 = sim.DoorsOf(a);
        var (nx, nz) = Shapes.SideNormal(sideA);
        int bx0, bz0, bx1, bz1;
        if (nz != 0) { bx0 = a.X0; bx1 = a.X1; if (nz < 0) { bz1 = a.Z0 - 1; bz0 = bz1 - 35; } else { bz0 = a.Z1 + 1; bz1 = bz0 + 35; } }
        else { bz0 = a.Z0; bz1 = a.Z1; if (nx < 0) { bx1 = a.X0 - 1; bx0 = bx1 - 55; } else { bx0 = a.X1 + 1; bx1 = bx0 + 55; } }
        // looked at from beyond it, drawn from its far corner to the one by the first house's wall
        Rig.SetAt(w.WX((bx0 + bx1) / 2), w.WY(g0), w.WZ((bz0 + bz1) / 2), MathF.Atan2(nx, nz) + .3f, 1.1f, 2.4f);
        await Frames(4);
        bool farLow = nz < 0 || nx < 0;
        if (farLow) await Drag(bx0, bz0, bx1, bz1); else await Drag(bx1, bz1, bx0, bz0);
        string sharedSaid = said;
        var drawn = Tools.HouseDrawn;
        var b = HouseAt((bx0 + bx1) / 2, (bz0 + bz1) / 2);
        Build();
        // (not laid out: why, side by side)
        string whys = "";
        if (b == null)
            for (int s = 0; s < 4; s++)
            {
                sim.PlanHouseAt(bx0, bz0, bx1, bz1, new Stroke { Mat = Mat.Stone, Width = 4, Height = 32, Level = true }, s, out _, out string why, out _, out var rect, out _, out _);
                whys += $" {Shapes.SideWords[s]}: {rect} «{why}»;";
            }
        // (the first one's doors still waiting, and whether they could open)
        string waitingA = "";
        foreach (var (d, since) in sim.HouseDoors)
            if (d.X >= a.X0 && d.X <= a.X1 && d.Z >= a.Z0 && d.Z <= a.Z1)
                waitingA += $" {Shapes.SideWords[d.Side]} at {d.X},{d.Y},{d.Z} free {Shapes.DoorFree(w, Site, d, sim.TakenBesides(a.X0, a.Z0, a.X1, a.Z1))}, gate {GatePlan.At(w, Site, new VoxelHit { X = d.X, Y = d.Y, Z = d.Z, NX = d.NX, NZ = d.NZ }, d.Y - 8, outward: true) != null}, waiting {sim.Time - since:0} s;";
        bool shares = b != null && (nz < 0 ? b.Z1 == a.Z0 + t - 1 : nz > 0 ? b.Z0 == a.Z1 - t + 1 : nx < 0 ? b.X1 == a.X0 + t - 1 : b.X0 == a.X1 - t + 1);
        bool oldGone = gatesA0.Count > 0 && gatesA0.TrueForAll(g => !sim.Gates.Contains(g));
        if (b != null) { sim.LookOver(a); sim.LookOver(b); }
        check("a house drawn right up against the wall another's door is in shares that wall, its ghost says so; built, the first one's door is walled up and cut on a free side, both closed with doors opening outside",
            b != null && sharedSaid.Contains("стена общая") && shares && oldGone && Out(a) && Out(b) && a.Closed && b.Closed,
            $"«{sharedSaid}»; drawn {drawn}, meant {(bx0, bz0, bx1, bz1)}, first {a.X0},{a.Z0}–{a.X1},{a.Z1}; second {(b == null ? "none:" + whys : $"{b.X0},{b.Z0}–{b.X1},{b.Z1}")}, shares {shares}; old door gone {oldGone}; doors {sim.DoorsOf(a).Count}/{(b == null ? 0 : sim.DoorsOf(b).Count)}, " +
            $"out {Out(a)}/{(b != null && Out(b))}; closed {a.Closed} «{a.Why}»/{b?.Closed} «{b?.Why}»; the first one's doors waiting:{waitingA}");
        if (b == null) return;

        // ---- the gate tool: a door put in the second's wall where outside is free, and its old door walled up
        Tools.SetTool(Tool.Gate);
        var oldB = sim.DoorsOf(b);
        int shared = sim.SharedSidesOf(b), had = sim.DoorSidesOf(b), side = -1;
        for (int s = 0; s < 4 && side < 0; s++) if ((shared & 1 << s) == 0 && (had & 1 << s) == 0) side = s;
        var (sx, sz) = Shapes.SideNormal(side < 0 ? 0 : side);
        int floorTop = b.Depth - Shapes.HouseHeight;
        float fxw = sx < 0 ? b.X0 - .01f : sx > 0 ? b.X1 + 1.01f : (b.X0 + b.X1) / 2 + .5f;
        float fzw = sz < 0 ? b.Z0 - .01f : sz > 0 ? b.Z1 + 1.01f : (b.Z0 + b.Z1) / 2 + .5f;
        int added0 = sim.DoorsAdded, waiting0 = sim.HouseDoors.Count;
        await ClickFace(fxw, floorTop + 10.5f, fzw, sx, sz);
        string addSaid = said;
        bool added = sim.DoorsAdded == added0 + 1 && sim.HouseDoors.Count == waiting0 + 1;
        // its old door: a click on its leaves
        string wallSaid = "";
        int stepAt = -1;
        string leafAt = "";
        bool walled = false;
        if (oldB.Count > 0)
        {
            var g = oldB[0];
            int walledWas = sim.DoorsWalled;
            // (barred and shut, nobody left in its way, so that nobody going in swings it open under the click)
            g.Mode = GateMode.Shut;
            var (gx, _, gz) = g.Middle;
            for (int i = 0; i < 20 && g.Step != 0; i++)
            {
                foreach (var u in sim.Units)
                    if (!u.Dead && !u.Inside && Math.Abs(u.X - gx) < 14 && Math.Abs(u.Z - gz) < 14)
                    {
                        u.Cell = sim.NearestStand((int)gx + (g.AlongX ? 0 : g.In * 40), (int)gz + (g.AlongX ? g.In * 40 : 0), u.T.Move.Height);
                        (u.X, u.Y, u.Z) = sim.Nav.Feet(u.Cell); u.Path = null;
                    }
                Advance(.5);
            }
            stepAt = g.Step;
            // (on its leaf as it hangs: the outermost voxel of the first leaf halfway along it and halfway up)
            int leaf = Gateway.Thick - 1 + Gateway.Thick * (Gateway.Half / 2 + Gateway.Half * (Gateway.Height / 2));
            var (lx, ly, lz) = g.At[leaf] >= 0 ? WaterPlanner.Unpack(g.At[leaf]) : (0, 0, 0);
            float ox = g.AlongX ? 0 : g.In * .49f, oz = g.AlongX ? g.In * .49f : 0;
            leafAt = $"{lx},{ly},{lz} {Mat.NameOf(w.Get(lx, ly, lz))}";
            await ClickFace(lx + .5f + ox, ly + .5f, lz + .5f + oz, g.AlongX ? 0 : g.In, g.AlongX ? g.In : 0);
            wallSaid = said;
            walled = sim.DoorsWalled == walledWas + 1 && !sim.Gates.Contains(g);
        }
        Build();
        sim.LookOver(b);
        var nowB = sim.DoorsOf(b);
        var fresh = nowB.Find(g => !oldB.Contains(g));
        // one of ours from the field outside goes in by the new door
        int cx = (b.X0 + b.X1) / 2, cz = (b.Z0 + b.Z1) / 2;
        int from = sim.NearestStand(cx + sx * 60, cz + sz * 60, 2), inner = sim.Nav.CellOf(cx, floorTop + 1, cz);
        Gateway via = null;
        bool got = false;
        if (from >= 0 && fresh != null)
        {
            for (int i = 0; i < 30 && nowB.Exists(g => g.Step != 0); i++) Advance(1);
            var u = sim.Spawn(Kinds.Worker, from);
            u.Hold = true;
            got = sim.GoTo(u, inner, adjacent: true, budget: 60000);
            if (got && u.Path != null) foreach (int c in u.Path) foreach (var g in nowB) if (g.Cells.Contains(c)) via = g;
            u.Dead = true; u.DiedAt = -999;
        }
        check("the gate tool on a house's wall where outside is free puts a door there, cut and hung; on its old door, walls it up; whoever goes in goes in by the new one",
            side >= 0 && added && addSaid.Contains("Дверь дома") && walled && wallSaid.Contains("Заложить ворота") && fresh is { Ready: true } && nowB.Count == 1 && got && via == fresh && b.Closed,
            $"side {(side < 0 ? "none" : Shapes.SideWords[side])}: added {added} («{addSaid}»); walled {walled} («{wallSaid}», its leaves at step {stepAt}, clicked at {leafAt}, {hovered} under the cursor); doors {oldB.Count} → {nowB.Count}, the new ready {fresh?.Ready}; " +
            $"in {got} by {(via == null ? "none" : via == fresh ? "the new" : "another")}; closed {b.Closed} «{b.Why}»");

        // ---- the library's «Декор» tab: a campfire set out with a click
        Tools.SetTool(Tool.Template);
        Tools.PickTemplateTab(2);
        Tools.PickTemplateDecor(Array.IndexOf(Yard.All, DecorKind.Campfire));
        Rig.SetAt(w.WX(fx), w.WY(g0), w.WZ(fz + 60), 0, 1.2f, 1.8f);
        await Frames(4);
        int yards0 = sim.Yards.Count;
        var spot = OnField(fx, fz + 60);
        Move(spot); await GhostCaughtUp();
        string decorSaid = Tools.PlanText ?? "";
        Button(spot, true); Button(spot, false);
        await Frames(2);
        var fire = sim.Yards.Count > yards0 ? sim.Yards[^1] : null;
        check("the template library's «Декор» tab sets out a campfire with a click, as the «Обустройство» tool does",
            Tools.DecorTab && fire is { Kind: DecorKind.Campfire },
            $"decor tab {Tools.DecorTab} («{decorSaid}»); set out {(fire == null ? "nothing" : fire.Kind.ToString())}");

        Tools.SetTool(Tool.None);
        Tools.Opt.House = false; Tools.Opt.Hollow = false; Tools.Opt.TemplateTab = 0;
        if (fire != null) { sim.DesignateRemove(fire.Voxels); sim.CompleteAll(); Advance(1); }
        await Frames(2);
    }
}
