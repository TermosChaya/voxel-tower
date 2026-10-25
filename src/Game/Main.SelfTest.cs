using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>The self-test (--selftest): the tools, the commands and the pawns driven and their results checked.</summary>
public partial class Main
{
    /// <summary>
    /// Drives the tools and the commands through their input handlers with synthetic clicks and drags, lets the pawns
    /// work through what was marked, checks the results and quits.
    /// </summary>
    async void SelfTest()
    {
        int fails = 0;
        // each check says how long the way to it took, when it was more than a second
        var since = Stopwatch.StartNew();
        void Check(string what, bool ok, string detail = "")
        {
            double took = since.Elapsed.TotalSeconds;
            since.Restart();
            GD.Print($"[selftest] {(ok ? "ok  " : "FAIL")} {what} {detail}{(took >= 1 ? $" [{took:0.0} s]" : "")}");
            if (!ok) fails++;
        }
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        Vector2 Screen(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(World.WX(x + .5f), World.WY(World.SurfAt(x, z)), World.WZ(z + .5f)));
        Vector2 ScreenAt(float x, float y, float z) => Rig.Cam.UnprojectPosition(new Vector3(World.WX(x), World.WY(y), World.WZ(z)));
        void Move(Vector2 p) => Tools._UnhandledInput(new InputEventMouseMotion { Position = p });
        void Button(Vector2 p, bool down, bool shift = false) => Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = p, ShiftPressed = shift });
        void KeyPress(Key k, bool ctrl = false, bool shift = false)
        {
            var e = new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true, CtrlPressed = ctrl, ShiftPressed = shift };
            Tools._UnhandledInput(e); Command._UnhandledInput(e);
        }
        async Task ClickAt(Vector2 p) { Move(p); await Frames(2); Button(p, true); Button(p, false); await Frames(2); }
        async Task Click(int x, int z) => await ClickAt(Screen(x, z));
        async Task Look(float vx, float vz) { Rig.Focus(World.WX(vx), World.WZ(vz)); await Frames(45); }
        async Task Drag(params (int x, int z)[] path)
        {
            var a = Screen(path[0].x, path[0].z); Move(a); Button(a, true);
            for (int k = 1; k < path.Length; k++)
            {
                var from = Screen(path[k - 1].x, path[k - 1].z); var to = Screen(path[k].x, path[k].z);
                for (int i = 1; i <= 8; i++) { Move(from.Lerp(to, i / 8f)); await Frames(1); }
            }
            Button(Screen(path[^1].x, path[^1].z), false);
            await Frames(2);
        }
        int Removals() => Sim.Orders.Total - Sim.Orders.BuildTotal;

        await Frames(5);
        int kx = Site.KeepX, kz = Site.KeepZ;
        // the places below were set out round the citadel where it stood at 819,512 on the old valley: they go with it
        int ox = kx - 819, oz = kz - 512;
        Sim.Waves.Countdown = 1e9;
        Sim.Stock.Gold = 500;
        Check("no ready-made blueprints", Blueprints.Count == 0);
        Check("a household at the gate", Sim.Count(Kinds.Worker) == 4 && Sim.Count(Kinds.Archer) == 1 && Sim.Count(Kinds.Swordsman) == 1);
        await Frames(2);
        Check("the pawns and the beasts are drawn", Units.Drawn() >= 5 + Sim.WildAlive() && Sim.WildAlive() >= 15, $"{Units.Drawn()} figures, {Sim.WildAlive()} beasts");
        Check("the river and the ponds are drawn", View.WaterChunks() >= 20, $"{View.WaterChunks()} chunks with water");

        // a siege of one's own: the errands on the panel, the main one first; at dusk the card of what comes tonight
        Sim.BeginErrands();
        double dusk0 = Sim.Waves.Countdown;
        Sim.Waves.Countdown = 30;
        Hud.Refresh();
        await Frames(2);
        int errandLines = Hud.ErrandsShown;
        bool duskCard = Hud.DuskShown, mainFirst = Sim.Errands.Count > 0 && Sim.Errands[0].Main;
        Sim.Waves.Countdown = dusk0;
        // and the dawn after a night held: its card with the gifts to choose from
        Sim.NightBegins(1);
        Sim.Dawn(1);
        Hud.Refresh();
        await Frames(2);
        bool dawnCard = Hud.DawnShown;
        int giftsShown = Sim.Gifts.Count;
        Sim.Gifts.Clear();
        Sim.NoErrands = true; Sim.Errands.Clear();
        Hud.Refresh();
        await Frames(2);
        Check("after a night held the dawn's card offers gifts to choose from, and goes with them",
            dawnCard && giftsShown >= 3 && !Hud.DawnShown, $"the card {dawnCard} with {giftsShown} gifts, after {Hud.DawnShown}");
        Check("in a siege of one's own the errands stand first on the panel, the main one first, and at dusk a card says what comes tonight",
            errandLines >= 2 && mainFirst && duskCard && !Hud.DuskShown, $"{errandLines} errand lines, main first {mainFirst}; the card at dusk {duskCard}, after {Hud.DuskShown}");


        // a stone wall is marked, not built
        await Look(740 + ox, 470 + oz);
        KeyPress(Key.Key2); KeyPress(Key.Z);
        Check("2 picks the line, Z the stone", Tools.Tool == Tool.Line && Tools.Opt.Material == Mat.Stone && !Tools.Opt.Dig);
        int lx = 750 + ox, ground = World.SurfAt(lx, 460 + oz), b0 = Sim.Orders.BuildTotal;
        await Drag((lx, 440 + oz), (lx, 480 + oz));
        Check("the line marks a wall for the workers", Sim.Orders.BuildTotal > b0 + 1500 && World.SurfAt(lx, 460 + oz) == ground,
            $"{Sim.Orders.BuildTotal - b0} voxels marked, ground still at {World.SurfAt(lx, 460 + oz)}");

        // the pick: a pit
        KeyPress(Key.V); KeyPress(Key.Key3);
        Check("V takes up the pick", Tools.Opt.Dig && Tools.Tool == Tool.Box);
        int px = 669 + ox, pz = 552 + oz, r0 = Removals();
        await Look(680 + ox, 560 + oz);
        await Drag((px, pz), (px + 24, pz + 24));
        int pit = Removals() - r0;
        Check("a box with the pick marks a pit", pit > 2000, $"{pit} voxels to dig");

        // a boulder broken up for stone with the ball of the brush
        var (bx, bz) = Nearest(705 + ox, 475 + oz, 60, (x, z) => World.Get(x, World.SurfAt(x, z) - 1, z) == Mat.Rock && World.SurfAt(x, z) > Site.HTop[z * World.SX + x] + 3);
        await Look(bx, bz);
        KeyPress(Key.Key1); KeyPress(Key.F);
        Check("the brush becomes a ball", Tools.Tool == Tool.Brush && Tools.Opt.Form == BrushForm.Ball);
        int r1 = Removals();
        await ClickAt(ScreenAt(bx + .5f, World.SurfAt(bx, bz), bz + .5f));
        Check("the ball pick marks rock to break", Removals() > r1 + 50, $"{Removals() - r1} voxels of the boulder at {bx},{bz}");
        KeyPress(Key.F);

        // a tree, felled whole by clicking it
        var (tx, tz) = Nearest(691 + ox, 670 + oz, 80, (x, z) => Mat.IsTrunk(World.Get(x, World.SurfAt(x, z) + 2, z)));
        await Look(tx, tz);
        int r2 = Removals();
        await ClickAt(ScreenAt(tx + .5f, World.SurfAt(tx, tz) + 6, tz + .5f));
        Check("a click with the pick fells a whole tree", Removals() > r2 + 300, $"{Removals() - r2} voxels of the tree at {tx},{tz}");

        // the pawns at work
        double earth0 = Sim.Stock[Stockpile.Earth], stone0 = Sim.Stock[Stockpile.Stone], wood0 = Sim.Stock[Stockpile.Wood];
        int left0 = Sim.Orders.Total;
        var sw = Stopwatch.StartNew();
        Advance(150);
        long ms = sw.ElapsedMilliseconds;
        await Frames(3);
        int dugCols = 0;
        for (int z = pz + 2; z < pz + 23; z += 3) for (int x = px + 2; x < px + 23; x += 3) if (World.SurfAt(x, z) < Site.HTop[z * World.SX + x] - 8) dugCols++;
        Check("workers dig the pit", dugCols > 30 && Sim.Stock[Stockpile.Earth] > earth0 + 1000, $"{dugCols} of 64 columns down, earth +{Fmt.M3(Sim.Stock[Stockpile.Earth] - earth0)} m³");
        Check("…lay the wall from the stores", World.SurfAt(lx, 460 + oz) > ground + 16, $"top {World.SurfAt(lx, 460 + oz)} over ground {ground}, stone {Fmt.M3(stone0)} → {Fmt.M3(Sim.Stock[Stockpile.Stone])} m³");
        // (the wood of the tree still marked, and how high the lowest of it lies over the ground, for when it fails)
        int woodLeft = 0, woodLow = int.MaxValue;
        for (int z = tz - 90; z < tz + 90; z++)
            for (int x = tx - 90; x < tx + 90; x++)
            {
                if (!World.InBounds(x, 1, z)) continue;
                int g = World.GroundTop(x, z);
                for (int y = g; y < g + 100 && y < World.SY; y++)
                    if (Mat.IsTrunk(World.Get(x, y, z)) && Orders.IsRemove(Sim.Orders.At(x, y, z))) { woodLeft++; woodLow = Math.Min(woodLow, y - g); }
            }
        Check("…and fell the tree for wood", Sim.Stock[Stockpile.Wood] > wood0 + 100, $"wood +{Fmt.M3(Sim.Stock[Stockpile.Wood] - wood0)} m³, {Sim.Orders.Total} of {left0} voxels left, {woodLeft} of the tree's wood still marked, the lowest {(woodLeft > 0 ? woodLow : 0)} over the ground; 150 s in {ms} ms");
        foreach (var u in Sim.Units) if (u.Why != "") GD.Print($"[selftest]      {u.T.Name} #{u.Id}: {u.Why}");

        // a ladder against the new wall, from the side the camera sees
        KeyPress(Key.Key7);
        Check("7 picks up the ladder", Tools.Tool == Tool.Ladder);
        await Look(lx, 460 + oz);
        int fy = ground + 10, face = lx;
        while (World.Get(face - 1, fy, 460 + oz) != Mat.Air) face--;
        int b2 = Sim.Orders.BuildTotal;
        await ClickAt(ScreenAt(face - .02f, fy + .5f, (460.5f + oz)));
        int rungs = Sim.Orders.BuildTotal - b2;
        Advance(40);
        int built = 0;
        for (int y = ground; y < ground + 48; y++) for (int z = 450 + oz; z < 472 + oz; z++) for (int x = face - 10; x < face; x++) if (World.Get(x, y, z) == Mat.Ladder) built++;
        Check("…the workers set it against the wall", rungs > 40 && built > 40, $"{rungs} voxels marked, {built} in place; clicked on «{Tools.HoverText}», west of the face {Mat.NameOf(World.Get(face - 1, fy, 460 + oz))}, the face {Mat.NameOf(World.Get(face, fy, 460 + oz))} at {face},{fy}, wall top {World.SurfAt(lx, 460 + oz)}");

        // a gate through the same wall, further along it
        KeyPress(Key.Key9);
        Check("9 picks up the gate", Tools.Tool == Tool.Gate);
        int gz = 472 + oz, gface = lx;
        while (World.Get(gface - 1, fy, gz) != Mat.Air) gface--;
        int gates0 = Sim.Gates.Count;
        await ClickAt(ScreenAt(gface - .02f, fy + .5f, gz + .5f));
        var hung = Sim.Gates.Count > gates0 ? Sim.Gates[^1] : null;
        Advance(60);
        Check("…the workers cut the gateway and hang the gate", hung != null && hung.Ready,
            hung == null ? "no gate planned" : $"leaves on the {(hung.In > 0 ? "east" : "west")}, ready {hung.Ready}; {Sim.Orders.Total} marked, {Sim.Orders.BuildTotal} to lay, wood {Fmt.M3(Sim.Stock[Stockpile.Wood])} m³; " +
                string.Join(", ", Sim.Units.FindAll(u => u.T == Kinds.Worker && !u.Dead).ConvertAll(u => $"#{u.Id} {u.State} {u.Why}")));

        // the horde knocks a hole into the new wall: a click with the mending tool lays it again as it was
        int hy = ground + 8, holed = 0;
        var was = new Dictionary<(int x, int y, int z), byte>();
        for (int y = hy; y < hy + 10; y++)
            for (int z = 450 + oz; z < 458 + oz; z++)
                for (int x = lx - 6; x <= lx + 6; x++)
                {
                    byte m = World.Get(x, y, z);
                    if (!Mat.IsMasonry(m)) continue;
                    was[(x, y, z)] = m;
                    World.Set(x, y, z, Mat.Air);
                    holed++;
                }
        Sim.Stock[Stockpile.Stone] += 4 * VoxelWorld.PerM3;
        KeyPress(Key.Key0);
        Check("0 picks up mending", Tools.Tool == Tool.Repair);
        // on the top of the wall beside the hole, where the ladder is not in the way
        int b3 = Sim.Orders.BuildTotal, wtop = World.SurfAt(lx, 446 + oz);
        await ClickAt(ScreenAt(lx + .5f, wtop + .02f, (446.5f + oz)));
        int mended = Sim.Orders.BuildTotal - b3;
        Advance(40);
        int back = 0;
        foreach (var (v, m) in was) if (World.Get(v.x, v.y, v.z) == m) back++;
        Check("…a click on the holed wall has it laid again as it was", holed > 100 && mended >= holed * 9 / 10 && back >= holed * 9 / 10,
            $"{holed} knocked out, {mended} marked, {back} back stone for stone");

        // an area set aside for stone: the boulder that is left goes to the workers by itself
        KeyPress(Key.Key8); KeyPress(Key.Z);
        Check("8 and Z set out a quarry", Tools.Tool == Tool.Gather && Tools.GatherKind == Stockpile.Stone);
        await Look(bx, bz);
        int zones0 = Sim.Zones.Count;
        double st0 = Sim.Stock[Stockpile.Stone];
        await Drag((bx - 18, bz - 18), (bx + 18, bz + 18));
        Check("a frame sets the quarry", Sim.Zones.Count == zones0 + 1, $"{Sim.Zones.Count} zones, {Areas.GetChildCount()} drawn");
        Advance(60);
        Check("…and the workers quarry it unasked", Sim.Stock[Stockpile.Stone] > st0 + 200, $"stone +{Fmt.M3(Sim.Stock[Stockpile.Stone] - st0)} m³");
        KeyPress(Key.Key6);
        await Drag((bx - 24, bz - 24), (bx + 24, bz + 24));
        Check("the unmarking frame takes the quarry back", Sim.Zones.Count == zones0, $"{Sim.Zones.Count} zones");

        // a sanctuary: the area tool set to «святилище», a priest hired, and it goes there and prays
        KeyPress(Key.Key8);
        Tools.SetSanctum(true);
        Check("8 with «святилище» sets out a sanctuary", Tools.Tool == Tool.Gather && Tools.GatherKind == Zone.Sanctum);
        int sx0 = kx - 200, sz0 = kz + 60;
        await Look(sx0 + 20, sz0 + 20);
        await Drag((sx0, sz0), (sx0 + 40, sz0 + 40));
        var sanctum = Sim.Zones.Find(z => z.Kind == Zone.Sanctum);
        var priest = Command.Hire(Kinds.Priest);
        double favour0 = Sim.Favour;
        Advance(60);
        Check("a frame sets the sanctuary, and a priest hired goes there and prays", sanctum != null && priest != null && priest.Pastime == Pastime.Pray && Sim.Favour > favour0 + .4,
            $"sanctuary {(sanctum != null ? $"{sanctum.W}×{sanctum.D}" : "none")}; the priest: {(priest == null ? "not hired" : Retinue.DoingNow(priest, Sim))}; intervention +{Sim.Favour - favour0:0.00}");
        // a dwelling framed on open ground is set out, and waits: it gives no places till it is a closed room with a door
        Tools.SetDwelling(true);
        int dx0 = kx - 260, dz0 = kz + 120;
        await Look(dx0 + 20, dz0 + 20);
        await Drag((dx0, dz0), (dx0 + 40, dz0 + 40));
        var home = Sim.Zones.Find(z => z.Kind == Zone.Dwelling);
        Advance(5);
        Check("8 with «жилой дом» frames a dwelling, which gives no places while it is open ground", Tools.GatherKind == Zone.Dwelling && home != null && !home.Closed && home.Why != "" && Sim.Places == Sim.CitadelPlaces,
            home == null ? "no dwelling set" : $"«{home.Why}», places {Sim.Places}");
        if (home != null) Sim.RemoveZones(home.X0, home.Z0, home.X1, home.Z1);
        Tools.SetMaterial(Mat.Stone);

        // taken alive: «Живьём» for the swordsman, a goblin beaten down and bound, a worker leading it into the citadel,
        // and the plaque in «Таинства» that has the priest give it to the old powers
        Unit swordsman = null;
        foreach (var u in Sim.Units) if (!u.Dead && u.T == Kinds.Swordsman) { swordsman = u; break; }
        Selected.Clear(); Selected.Add(swordsman);
        Hud.OnSelectionChanged();
        Command.TakeAliveSelected();
        // (the archers, told nothing of it, stood well off meanwhile: they would shoot it down)
        var bowmen = Sim.Units.FindAll(u => !u.Dead && !u.Inside && u.Team == Team.Player && u.T.Ranged);
        var bowAt = bowmen.ConvertAll(u => u.Cell);
        foreach (var b in bowmen)
        {
            int far = Sim.NearestStand((int)b.X + (b.X > World.SX / 2 ? -300 : 300), (int)b.Z, b.T.Move.Height);
            if (far >= 0) { b.Cell = far; (b.X, b.Y, b.Z) = Sim.Nav.Feet(far); b.Path = null; }
        }
        var captive = Sim.Spawn(Kinds.Goblin, Sim.NearestStand((int)swordsman.X + 10, (int)swordsman.Z, 2));
        captive.Guard = true; captive.Home = captive.Cell;
        for (int t = 0; t < 120 && !captive.Bound && !captive.Dead; t++) Advance(.25);
        bool felled = captive.Bound && !captive.Dead;
        for (int t = 0; t < 240 && !captive.Inside && !captive.Dead; t++) Advance(.5);
        Check("«Живьём»: the swordsman fells a goblin and binds it, and a worker leads it into the citadel", swordsman.TakeAlive && felled && captive.Inside,
            $"take alive {swordsman.TakeAlive}, felled and bound {felled}, inside {captive.Inside}{(captive.Dead ? ", dead: " + captive.KilledBy : "")}; hurt last by {captive.HurtBy?.T.Name}");
        for (int i = 0; i < bowmen.Count; i++) { var b = bowmen[i]; b.Cell = bowAt[i]; (b.X, b.Y, b.Z) = Sim.Nav.Feet(bowAt[i]); b.Path = null; }
        Command.ClearSelection();
        Hud.OpenPanel("magic");
        Hud.Refresh();
        int revelations0 = Sim.Revelations;
        Hud.Mysteries.PlaqueOf(RiteKind.Prisoner).EmitSignal(BaseButton.SignalName.Pressed);
        for (int t = 0; t < 240 && !captive.Dead; t++) Advance(.5);
        Check("…and the plaque in «Таинства» has the priest give the prisoner to the old powers", captive.Dead && captive.KilledBy == Sim.GivenToPowers && Sim.Revelations == revelations0 + 1,
            $"the goblin {(captive.Dead ? captive.KilledBy : "alive")}, revelations {revelations0} → {Sim.Revelations}");
        Hud.ClosePanel();

        // marking and unmarking; on the grid, a line drawn askew comes out straight along it
        KeyPress(Key.Key2);
        if (!Tools.Opt.Dig) KeyPress(Key.V);
        KeyPress(Key.N);
        Check("N snaps to the grid", Tools.Opt.Grid == 8, $"grid {Tools.Opt.Grid}, last {Tools.Opt.GridLast}, tool {Tools.Tool}, menu {Hud.MenuOpen}");
        await Look(640 + ox, 500 + oz);
        int r3 = Removals();
        await Drag((641 + ox, 441 + oz), (646 + ox, 558 + oz));
        int moat = Removals() - r3;
        // (a tree the moat cuts into comes down whole, roots and all: its wood is not the line's)
        int gx = kx + (int)Math.Round((641 + ox - kx) / 8.0) * 8, askew = 0, minZ = int.MaxValue, maxZ = 0;
        for (int z = 420 + oz; z < 580 + oz; z++)
            for (int x = 600 + ox; x < 690 + ox; x++)
                for (int y = World.SurfAt(x, z) - 20; y < World.SurfAt(x, z) + 2; y++)
                    if (Orders.IsRemove(Sim.Orders.At(x, y, z)) && !Mat.IsTrunk(World.Get(x, y, z)) && !Mat.IsLeafy(World.Get(x, y, z)))
                    {
                        if (Math.Abs(x - gx) > 5) askew++;
                        minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
                    }
        Check("a line with the pick marks a moat, straight along the grid", moat > 5000 && askew == 0,
            $"{moat} voxels, {askew} off the line x = {gx}, z {minZ}…{maxZ}");
        KeyPress(Key.N);
        // a bridge thrown across the moat, arched over it, from bank to bank
        KeyPress(Key.M);
        Check("M picks up the bridge", Tools.Tool == Tool.Bridge && !Tools.Opt.Dig);
        int bb0 = Sim.Orders.BuildTotal;
        await Drag((620 + ox, 500 + oz), (668 + ox, 500 + oz));
        int bridged = Sim.Orders.BuildTotal - bb0, deckMid = 0, deckEnd = 0;
        for (int y = World.SY - 1; y > 0 && deckMid == 0; y--) if (Sim.Orders.At(643 + ox, y, 500 + oz) != 0) deckMid = y;
        for (int y = World.SY - 1; y > 0 && deckEnd == 0; y--) if (Sim.Orders.At(626 + ox, y, 500 + oz) != 0) deckEnd = y;
        Check("…a drag marks a bridge over the moat, higher in the middle", bridged > 1500 && deckMid > deckEnd + 2,
            $"{bridged} voxels, deck at {deckMid} in the middle and {deckEnd} near the end");
        KeyPress(Key.Key6);
        Check("6 picks up the unmarking tool", Tools.Tool == Tool.Unmark);
        await Drag((608 + ox, 486 + oz), (680 + ox, 514 + oz));
        Check("a frame takes the bridge back", Sim.Orders.BuildTotal <= bb0 + 10, $"{Sim.Orders.BuildTotal - bb0} left of {bridged}");
        await Drag((630 + ox, 430 + oz), (652 + ox, 570 + oz));
        Check("a frame takes the marks back", Removals() <= r3 + 10, $"{Removals() - r3} left of {moat}");
        // Ctrl+Z takes the frames back, the last first: the moat is marked again, then the bridge with the end of the moat under it
        KeyPress(Key.Z, ctrl: true);
        int moatBack = Removals() - r3;
        KeyPress(Key.Z, ctrl: true);
        Check("Ctrl+Z takes the frames back one by one: the moat is marked again, then the bridge",
            moatBack > moat / 2 && Removals() - r3 >= moat - 10 && Sim.Orders.BuildTotal - bb0 >= bridged - 10,
            $"moat {moatBack}, then {Removals() - r3} of {moat}; bridge {Sim.Orders.BuildTotal - bb0} of {bridged}");
        Sim.CancelArea(600 + ox, 420 + oz, 690 + ox, 580 + oz);

        // capture what the workers built, mark a copy
        KeyPress(Key.Escape); KeyPress(Key.Z);
        await Look(750 + ox, 460 + oz);
        KeyPress(Key.B);
        await Drag((lx - 10, 436 + oz), (lx + 10, 484 + oz));
        Check("capture makes a blueprint of the new wall", Blueprints.Count == 1, Blueprints.Count > 0 ? Blueprints[0].Describe() : "");
        if (Blueprints.Count > 0)
        {
            Tools.StartPaste(Blueprints[0]);
            KeyPress(Key.R);
            Check("R turns the copy", Tools.PasteRot == 1);
            int b1 = Sim.Orders.BuildTotal;
            await Click(780 + ox, 420 + oz);
            Check("the copy is marked for building", Sim.Orders.BuildTotal > b1 + 500, $"{Sim.Orders.BuildTotal - b1} voxels");
        }
        KeyPress(Key.Escape);
        Check("Esc drops the tool", Tools.Tool == Tool.None);

        // choosing and sending pawns
        // a worker with nobody right beside it, so the click is on it and not on a neighbour in front of it
        Unit worker = null;
        foreach (var u in Sim.Units)
        {
            if (u.Dead || u.T != Kinds.Worker || u.Inside) continue;
            worker ??= u;
            if (!Sim.Units.Exists(o => o != u && !o.Dead && !o.Inside && MathF.Abs(o.X - u.X) < 6 && MathF.Abs(o.Z - u.Z) < 6)) { worker = u; break; }
        }
        await Look(worker.X, worker.Z);
        await ClickAt(Command.Screen(worker, .5f));
        Check("a click picks a pawn, and the console shows it in full", Selected.Count == 1 && Selected.Contains(worker) && Hud.Inspecting == worker, $"{Selected.Count} chosen, the console on {Hud.Inspecting?.Name}");
        // a squad: Ctrl+2 remembers the chosen, Shift+2 calls them up again
        KeyPress(Key.Key2, ctrl: true);
        Selected.Clear();
        var tool0 = Tools.Tool;
        KeyPress(Key.Key2, shift: true);
        Check("Ctrl+2 makes the chosen squad II, Shift+2 picks it again (and takes up no brush)", Selected.Count == 1 && Selected.Contains(worker) && Tools.Tool == tool0,
            $"{Selected.Count} chosen, tool {Tools.Tool}");
        var dest = Screen(kx - 60, kz + 60);
        Rig._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = dest });
        Rig._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = dest });
        Check("a right click sends it on its way", worker.Hold && worker.Back && worker.Path != null, $"hold {worker.Hold}, back to work after {worker.Back}, path {worker.Path?.Count}");
        // it gets there, and there goes back to work by itself (no «За работу» wanted)
        float nearest = float.MaxValue, fx = 0, fz = 0;
        for (int t = 0; t < 30; t++)
        {
            Advance(.5);
            (fx, _, fz) = Sim.Nav.Feet(worker.Cell);
            nearest = Math.Min(nearest, Math.Max(MathF.Abs(fx - (kx - 60)), MathF.Abs(fz - (kz + 60))));
        }
        Check("…and it gets there, then back to work by itself", nearest < 24 && !worker.Hold, $"came within {nearest:0} voxels, held now {worker.Hold}, at {fx:0},{fz:0}");
        Command.ReleaseSelected();
        Check("«За работу» lets it go", !worker.Hold);
        Command.ClearSelection();
        // the mouse's buttons swapped on the controls page: the right one picks, the left one sends; and back. A worker
        // with nobody beside it again (they have all gone about their work since), so the click is on it alone (with
        // another worker if the copy's masonry came down on this one meanwhile)
        foreach (var u in Sim.Units)
            if (!u.Dead && u.T == Kinds.Worker && !u.Inside && !u.Down && !Sim.Units.Exists(o => o != u && !o.Dead && !o.Inside && MathF.Abs(o.X - u.X) < 6 && MathF.Abs(o.Z - u.Z) < 6)) { worker = u; break; }
        if (worker.Dead || worker.Inside || worker.Down) worker = Sim.Units.Find(u => !u.Dead && !u.Inside && !u.Down && u.T == Kinds.Worker) ?? worker;
        Bindings.SetSwapMouse(true);
        await Look(worker.X, worker.Z);
        var onIt = Command.Screen(worker, .5f);
        Move(onIt); await Frames(2);
        Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = onIt });
        Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = onIt });
        await Frames(2);
        bool pickedByRight = Selected.Count == 1 && Selected.Contains(worker);
        string picked = Selected.Count == 0 ? "nobody" : string.Join(", ", new List<Unit>(Selected).ConvertAll(u => u.Name));
        var there = Screen(kx - 40, kz + 40);
        Rig._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = there });
        Rig._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = there });
        bool sentByLeft = worker.Hold && worker.Path != null;
        string said = Bindings.Fill("{Lmb} {Rmb}");
        Bindings.SetSwapMouse(false);
        Check("with the mouse's buttons swapped the right one picks a pawn, the left one sends it, and the hints say so",
            pickedByRight && sentByLeft && said == "ПКМ ЛКМ" && Bindings.First == MouseButton.Left,
            $"picked {pickedByRight} ({picked} for {worker.Name}), sent {sentByLeft}, the hints «{said}»");
        Command.ReleaseSelected();
        Command.ClearSelection();

        // the faces of the household along the top pick a pawn; the work table sets what it does first
        await Frames(2);
        Unit firstFace = null;
        foreach (var u in Sim.Units)
            if (!u.Dead && u.Team == Team.Player && (firstFace == null || Array.IndexOf(Kinds.PlayerKinds, u.T) < Array.IndexOf(Kinds.PlayerKinds, firstFace.T)
                || u.T == firstFace.T && u.Id < firstFace.Id)) firstFace = u;
        Hud.Colonists._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(8, 12) });
        await Frames(2);
        Check("a click on a face along the top picks that pawn, and the console shows it", Selected.Count == 1 && Selected.Contains(firstFace) && Hud.Inspecting == firstFace,
            $"{Selected.Count} chosen, the console on {Hud.Inspecting?.Name} №{Hud.Inspecting?.Id}");
        Hud.OpenPanel("works");
        await Frames(3);
        byte dig0 = firstFace.Priority[(int)WorkKind.Dig];
        Hud.Works._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(WorkTable.WorkX((int)WorkKind.Dig) + 30, WorkTable.Head + 12) });
        Check("the work table opens from the beam, and a click on a cell changes the priority", Hud.OpenTab == "works" && firstFace.Priority[(int)WorkKind.Dig] == Work.Cycle(dig0, false),
            $"open: {Hud.OpenTab}, digging {dig0} → {firstFace.Priority[(int)WorkKind.Dig]}");
        Sim.SetPriority(firstFace, WorkKind.Dig, dig0);
        Hud.OpenPanel("goals");
        Command.ClearSelection();

        // a hunt: the archer after a boar, for gold
        var boar = Sim.Spawn(Kinds.Boar, Sim.NearestStand(kx - 150, kz + 110, 2));
        Unit archer = null;
        foreach (var u in Sim.Units) if (!u.Dead && u.T == Kinds.Archer) { archer = u; break; }
        Selected.Clear(); Selected.Add(archer);
        await Look(boar.X, boar.Z);
        var at = Command.Screen(boar, .5f);
        Rig._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        Rig._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
        Check("a right click on a beast sends the archer hunting", archer.Prey == boar, $"prey {archer.Prey?.T.Name}");
        double gold1 = Sim.Stock.Gold;
        Advance(60);
        Check("…and the boar is taken for gold", boar.Dead && Sim.Stock.Gold >= gold1 + Kinds.Boar.Bounty, $"boar {(boar.Dead ? "taken" : $"{boar.Hp:0} hp")}, +{Sim.Stock.Gold - gold1:0} gold");

        // fire arrows: «Огонь», then a right click on the ladder sets it alight, and the workers put it out
        Selected.Clear(); Selected.Add(archer);
        Hud.OnSelectionChanged();
        Command.FireArrowsSelected();
        await Look(face - 6, 460 + oz);
        // (the ladder stands where the wall's foot let it, a voxel or two out from the face)
        int lad = face - 1;
        for (int k = 1; k <= 6; k++) if (World.Get(face - k, fy + 2, 460 + oz) == Mat.Ladder) { lad = face - k; break; }
        var rung = ScreenAt(lad + .5f, fy + 2.5f, (460.5f + oz));
        Rig._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = rung });
        Rig._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = rung });
        Check("«Огонь» gives the archer fire arrows, and a right click on the ladder tells it to set it alight", archer.FireArrows && archer.KindleAt >= 0,
            $"fire arrows {archer.FireArrows}, mark {archer.KindleAt}, at the ladder: {Mat.NameOf(World.Get(lad, fy + 2, 460 + oz))}");
        var (mx, my, mz) = archer.KindleAt >= 0 ? WaterPlanner.Unpack(archer.KindleAt) : (0, 0, 0);
        string marked = $"{Mat.NameOf(World.Get(mx, my, mz))} at {mx},{my},{mz} (the wall face at {face}, {fy})";
        int caughtN = 0;
        Action<int, int, int, byte> count = (_, _, _, _) => caughtN++;
        Sim.Caught += count;
        for (int t = 0; t < 40 && (caughtN == 0 || Sim.Burning.Count > 0); t++) Advance(.5);
        Sim.Caught -= count;
        Check("…it catches, and the workers put it out", caughtN > 0 && Sim.Burning.Count == 0, $"{marked}: {caughtN} caught, {Sim.Burning.Count} left burning");
        Command.FireArrowsSelected();
        Command.ClearSelection();

        // the alarm: two fresh workers out in the field go into the citadel; the all-clear lets them out
        var (gfx, _, gfz) = Sim.Nav.Feet(Sim.Gate);
        var fresh = new List<Unit>();
        foreach (int dz in new[] { -40, 40 }) fresh.Add(Sim.Spawn(Kinds.Worker, Sim.NearestStand((int)gfx - 160, (int)gfz + dz, 4)));
        KeyPress(Key.T);
        bool alarmUp = Sim.Alarm;
        for (int t = 0; t < 80 && !fresh.TrueForAll(u => u.Inside); t++) Advance(.5);
        int wentIn = fresh.FindAll(u => u.Inside).Count;
        KeyPress(Key.T);
        Advance(8);
        int still = fresh.FindAll(u => u.Inside).Count;
        Check("T sounds the alarm and the workers go into the citadel; T again stands it down and they come out", alarmUp && wentIn == 2 && !Sim.Alarm && still == 0,
            $"{wentIn} of 2 went in, {still} still inside");

        // lore: knowledge to spend, and a plaque in «Знания» learns it
        Hud.OpenPanel("lore");
        Hud.Lore.ShowPage(0);
        Sim.Knowledge = 2;
        Hud.Refresh();
        // (the town's page since 25.10.2026: the town hall, and the market resting on it)
        var yew = Hud.Lore.PlaqueOf("b_hall");
        bool could = !yew.Disabled;
        yew.EmitSignal(BaseButton.SignalName.Pressed);
        Check("a medallion in «Знания» learns its lore for the knowledge it costs", could && Sim.Knows("b_hall") && Sim.Knowledge == 0 && Hud.Lore.PlaqueOf("b_market").Disabled,
            $"the town hall learnt {Sim.Knows("b_hall")}, {Sim.Knowledge} knowledge left");
        // the troops' page: a building that hires a kind
        Hud.OpenPanel("lore:1");
        Sim.Knowledge = 3;
        Hud.Refresh();
        bool troopsShown = Hud.Lore.Page == 1 && Hud.Lore.PlaqueOf("b_barracks").IsVisibleInTree() && !Hud.Lore.PlaqueOf("b_hall").IsVisibleInTree();
        Hud.Lore.PlaqueOf("b_barracks").EmitSignal(BaseButton.SignalName.Pressed);
        Check("the troops' page of «Знания» opens a building that hires troops, for knowledge", troopsShown && Sim.Knows("b_barracks") && Sim.Knowledge == 0,
            $"page {Hud.Lore.Page}, the barracks learnt {Sim.Knows("b_barracks")}, {Sim.Knowledge} knowledge left");
        Hud.ClosePanel();

        // a spell: a click on Dread's plaque in «Таинства» before it is learnt opens the intervention's page of the tree;
        // there it is learnt with a click on its medallion, taken in hand from «Таинства», and a click on the field among
        // three goblins puts them to flight
        Sim.Favour += 60;
        Hud.OpenPanel("magic");
        Hud.Refresh();
        Hud.Mysteries.SpellPlaque(SpellKind.Dread).EmitSignal(BaseButton.SignalName.Pressed);
        bool toTree = Hud.Lore.Page == 2 && Hud.Lore.IsVisibleInTree() && !Sim.Known.Contains(SpellKind.Dread);
        Hud.Lore.PlaqueOf(Sim.SpellKey(SpellKind.Dread)).EmitSignal(BaseButton.SignalName.Pressed);
        bool learnt = toTree && Sim.Known.Contains(SpellKind.Dread);
        Hud.OpenPanel("magic");
        Hud.Refresh();
        Hud.Mysteries.SpellPlaque(SpellKind.Dread).EmitSignal(BaseButton.SignalName.Pressed);
        bool inHand = Tools.Tool == Tool.Spell && Tools.CurrentSpell == SpellKind.Dread;
        int sx1 = kx - 260, sz1 = kz - 120;
        var flock = new List<Unit>();
        for (int i = 0; i < 3; i++)
        {
            var g = Sim.Spawn(Kinds.Goblin, Sim.NearestStand(sx1 + i * 10, sz1, 2));
            g.Guard = true; g.Home = g.Cell;
            flock.Add(g);
        }
        await Look(sx1 + 10, sz1);
        double favourBefore = Sim.Favour;
        await Click(sx1 + 10, sz1);
        Check("a spell is learnt in the tree, taken in hand from «Таинства», and a click on the field casts it: Dread puts the goblins to flight",
            learnt && inHand && flock.TrueForAll(g => g.Routed) && Sim.Favour < favourBefore,
            $"learnt {learnt}, in hand {inHand}, running {flock.FindAll(g => g.Routed).Count} of 3, intervention {favourBefore:0} → {Sim.Favour:0}");
        KeyPress(Key.Escape);
        foreach (var g in flock) { g.Dead = true; g.DiedAt = -999; }

        // the spells in their four circles, twenty-three of them, each with its plaque and its medallion on the tree
        bool plaques = Array.TrueForAll(Sim.Spells, sp => Hud.Mysteries.SpellPlaque(sp.Kind) != null && Hud.Lore.PlaqueOf(Sim.SpellKey(sp.Kind)) != null);
        Check("«Таинства» hold the spells in four circles, twenty-three of them, each with its plaque and its medallion on the tree", Sim.Circles.Length == 4 && Sim.Spells.Length == 23 && plaques,
            $"{Sim.Circles.Length} circles, {Sim.Spells.Length} spells, plaques for all {plaques}");
        Hud.ClosePanel();

        // a blast at a click: the ground there goes up in pieces that are seen flying, and come down
        Sim.Known.Add(SpellKind.Explosion); Sim.Known.Add(SpellKind.Telekinesis);
        Sim.Favour += 200;
        Tools.TakeSpell(SpellKind.Explosion);
        int ex1 = kx - 240, ez1 = kz + 80;
        await Look(ex1, ez1);
        await Click(ex1, ez1);
        int flyingNow = Sim.Fliers.Count;
        // (its own pieces: what else of the test's works may be coming down meanwhile is not the blast's)
        var blasted = new HashSet<int>(Sim.Fliers.ConvertAll(f => f.Id));
        await Frames(2);
        int drawn = Magic.PiecesDrawn;
        Advance(10);
        var aloft = Sim.Fliers.FindAll(f => blasted.Contains(f.Id));
        Check("a blast at a click: what is there goes up in pieces, seen flying, that come down", flyingNow > 3 && drawn > 500 && aloft.Count == 0,
            $"{flyingNow} pieces, {drawn} voxels drawn in the air; {aloft.Count} of them still flying after 10 s"
            + string.Join("", aloft.ConvertAll(f => $"; {(f.Unit != null ? f.Unit.T.Key : f.Voxels.Count + " voxels")}{(f.Rolling ? " rolling" : "")}{(f.Tipping ? " tipping" : "")} at {f.X:0},{f.Y:0},{f.Z:0}")));
        KeyPress(Key.Escape);

        // telekinesis with the mouse: the button goes down on a goblin, it is lifted, carried after the cursor, and thrown
        Tools.TakeSpell(SpellKind.Telekinesis);
        int tx1 = kx - 200, tz1 = kz - 160;
        var lifted = Sim.Spawn(Kinds.Goblin, Sim.NearestStand(tx1, tz1, 2));
        lifted.Guard = true; lifted.Home = lifted.Cell;
        await Look(tx1, tz1);
        float lx0 = lifted.X, lz0 = lifted.Z;
        var grab = ScreenAt(lifted.X, lifted.Y + 6, lifted.Z);
        Move(grab); await Frames(2); Button(grab, true);
        bool gripped = Sim.HeldNow?.Unit == lifted;
        var away = Screen(tx1 + 120, tz1);
        // the test runs the valley by hand: a twentieth of a second for every frame of the stroke
        for (int i = 1; i <= 20; i++) { Move(grab.Lerp(away, i / 20f)); await Frames(1); Advance(.05); }
        Button(away, false);
        await Frames(2);
        Advance(6);
        float flown = MathF.Sqrt((lifted.X - lx0) * (lifted.X - lx0) + (lifted.Z - lz0) * (lifted.Z - lz0)) / VoxelWorld.PerMetre;
        Check("telekinesis with the mouse: a goblin taken up, carried after the cursor and thrown", gripped && lifted.Airborne == null && flown > 4,
            $"gripped {gripped}; it came down {flown:0.0} m off, {(lifted.Dead ? "dead" : $"{lifted.Hp:0} hp")}");
        KeyPress(Key.Escape);
        lifted.Dead = true; lifted.DiedAt = -999;

        // powder: the brush lays it as a trap, and an archer told to set the kegs alight with a fire arrow blows them up
        Tools.SetTool(Tool.Line);
        Tools.SetMaterial(Mat.Powder);
        bool powderInHand = Tools.Opt.Material == Mat.Powder && !Tools.Opt.Dig;
        KeyPress(Key.Escape);
        int px1 = kx - 190, pz1 = kz + 40;
        var kegs = new List<(int x, int y, int z)>();
        for (int z = pz1; z < pz1 + 4; z++)
            for (int x = px1; x < px1 + 4; x++)
                for (int y = World.SurfAt(x, z), y1 = y + 4; y < y1; y++) { World.Set(x, y, z, Mat.Powder); kegs.Add((x, y, z)); }
        var nearKegs = Sim.Spawn(Kinds.Goblin, Sim.NearestStand(px1 - 12, pz1 + 2, 2));
        nearKegs.Guard = true; nearKegs.Home = nearKegs.Cell;
        int booms = 0;
        // (how far from the blast the goblin stood when it went up, for when it fails)
        float shy = -1;
        Action<float, float, float, float> heard = (hx, _, hz, _) => { booms++; shy = MathF.Sqrt((nearKegs.X - hx) * (nearKegs.X - hx) + (nearKegs.Z - hz) * (nearKegs.Z - hz)); };
        Sim.Exploded += heard;
        Sim.SetFireArrows(new[] { archer }, true);
        var (kgx, kgy, kgz) = kegs[^1];
        Sim.KindleOrder(new[] { archer }, kgx, kgy, kgz);
        for (int t = 0; t < 80 && booms == 0; t++) Advance(.5);
        Advance(1);
        Sim.Exploded -= heard;
        Sim.SetFireArrows(new[] { archer }, false);
        int kegsLeft = kegs.FindAll(k => World.Get(k.x, k.y, k.z) == Mat.Powder).Count;
        Check("powder is a trap the brush lays, and kegs set alight by a fire arrow go up and throw the goblin by them", powderInHand && booms > 0 && kegsLeft == 0 && (nearKegs.Dead || nearKegs.Hp < nearKegs.MaxHp),
            $"in hand {powderInHand}; {booms} blasts, {kegsLeft} of {kegs.Count} kegs left; the goblin {(nearKegs.Dead ? "dead" : $"{nearKegs.Hp:0} hp")}, {shy / VoxelWorld.PerMetre:0.0} m off the blast");
        nearKegs.Dead = true; nearKegs.DiedAt = -999;

        // a great blast in the middle of the view: it shakes, the screen flashes and the time slows a moment, and all of
        // it passes by itself (the shaking and the slowing turned on for this, and off again after)
        await Look(Site.KeepX, Site.KeepZ);
        Settings.Feel(1, true);
        Spectacle.Felt(Site.KeepX, Site.KeepZ, 1.2f, flash: true);
        // the flash is gone in a tenth of a second, and a hidden run draws each frame itself, slowly: it is read at once
        float flashed = Spectacle.FlashNow;
        await Frames(3);
        float shook = Rig.Shaking, slowed = Slow;
        await Frames(150);
        Check("a great blast in the middle of the view shakes it, flashes the screen and slows the time a moment, and it all passes",
            shook > .3f && slowed < .5f && flashed > .05f && Slow == 1 && Rig.Shaking < shook && Spectacle.FlashNow == 0,
            $"shaking {shook:0.00} → {Rig.Shaking:0.00}, time ×{slowed:0.00} → ×{Slow:0.00}, flash {flashed:0.00} → {Spectacle.FlashNow:0.00}");
        Settings.Feel(0, false);

        // the horde called early: no gold for it, only less known of the defences
        Sim.Waves.Countdown = 200;
        double gold0 = Sim.Stock.Gold;
        KeyPress(Key.Space);
        Check("Space calls the horde early: no gold, less scouted", Sim.Waves.Active && Sim.Waves.Number == 1 && Math.Abs(Sim.Stock.Gold - gold0) < .01 && Math.Abs(Sim.Waves.Intel - 400.0 / 600) < .01,
            $"intel {Sim.Waves.Intel * 100:0} %, gold {gold0:0} → {Sim.Stock.Gold:0}");
        Advance(90);
        Check("the first wave comes and fights", Sim.Kills > 0 && Sim.Shots > 0, $"{Sim.Kills} killed, {Sim.RaidersAlive()} alive, {Sim.Shots} arrows, citadel {Sim.KeepHp:0}");
        KeyPress(Key.P);
        Check("P pauses", Paused);
        KeyPress(Key.Tab);
        Check("Tab changes the speed", Speed == 2 && !Paused);
        Speed = 1;
        KeyPress(Key.Equal, ctrl: true);
        float larger = GetTree().Root.ContentScaleFactor;
        KeyPress(Key.Key0, ctrl: true);
        Check("Ctrl+= draws the interface larger, Ctrl+0 as it was", Math.Abs(larger - 1.1f) < .01f && Math.Abs(GetTree().Root.ContentScaleFactor - 1) < .01f,
            $"{larger:0.00} → {GetTree().Root.ContentScaleFactor:0.00}");

        // the picture lowered for a weak machine and put back: no shadows, fewer pixels, no smoothing past FXAA, no blades
        Settings.ScriptQuality(0);
        Graphics.Apply(this);
        var vpG = GetViewport();
        bool lowered = !Atmos.Sun.ShadowEnabled && vpG.Scaling3DScale < .7f && vpG.Msaa3D == Viewport.Msaa.Disabled && vpG.ScreenSpaceAA == Viewport.ScreenSpaceAAEnum.Fxaa && !Atmos.Env.GlowEnabled;
        Settings.ScriptQuality(2);
        Graphics.Apply(this);
        bool restored = Atmos.Sun.ShadowEnabled && vpG.Scaling3DScale == 1 && vpG.Msaa3D == Viewport.Msaa.Msaa2X && Atmos.Env.GlowEnabled;
        Check("the picture can be lowered for a weak machine and put back", lowered && restored, $"lowered {lowered}, back {restored}");

        Check("the sounds are made by the game itself and heard as it plays", Sound.Clips == 38 && Sound.Heard.Count >= 4,
            $"{Sound.Clips} sounds, {Sound.Played} played: {string.Join(", ", Sound.Heard)}");

        var before = Rig.Target;
        Hud.Map.Jump(new Vector2(10, 10));
        await Frames(2);
        Check("map click moves the view", Rig.Target.DistanceTo(before) > 1, $"{before} → {Rig.Target}");
        await Frames(10);
        Check("chunks re-meshed", World.DirtyCount == 0, $"{View.Triangles()} triangles");
        // the grass in blades: tufts and flowers drawn all over the valley; a sward on the turf only once the view is close
        var (tufts, flowers, swardFar) = View.Grass.Drawn();
        var aim0 = Rig.Aim;
        Rig.SetAt(World.WX(Site.KeepX - 120), World.WY(World.SurfAt(Site.KeepX - 120, Site.KeepZ)), World.WZ(Site.KeepZ), aim0.yaw, .5f, 6f);
        await Frames(12);
        int swardNear = View.Grass.Drawn().sward;
        Rig.Set(aim0.x, aim0.z, aim0.yaw, aim0.pitch, aim0.zoom, snap: true);
        await Frames(4);
        int swardBack = View.Grass.Drawn().sward;
        Check("tufts and flowers are drawn in blades; the turf grows a sward of them when the view comes close, and loses it going back",
            tufts > 1000 && flowers > 100 && swardFar == 0 && swardNear > 2000 && swardBack == 0,
            $"{tufts} tufts, {flowers} flowers; sward {swardFar} from afar, {swardNear} close, {swardBack} back again");
        // the lines round the voxels: off unless the player turns them on in the settings, and seen at once when they are:
        // they darken the picture, where the wind in the grass from one frame to the next only stirs it
        // not so close that the sward is still growing in over the turf, which darkens it by itself
        // (the game stopped and the grass put away meanwhile: on a busy machine the blades stirring in the wind and the
        // people walking change the picture between two frames as much as the lines do)
        Rig.SetAt(World.WX(Site.KeepX - 120), World.WY(World.SurfAt(Site.KeepX - 120, Site.KeepZ)), World.WZ(Site.KeepZ), aim0.yaw, .6f, 2.2f);
        bool wasPaused = Paused;
        Paused = true;
        // (the water's ripples and the people's breath go on with the game stopped: they are put away too)
        View.Grass.Visible = false;
        View.ShowParts(solid: true, water: false);
        Units.Visible = false;
        await Frames(20);
        bool linesOff = !Settings.Lines;
        // how much darker the second picture is than the first, on average, in steps of 255
        float Darker(Image a, Image b)
        {
            double d = 0; int n = 0;
            for (int y = a.GetHeight() / 5; y < a.GetHeight() * 4 / 5; y += 5)
                for (int x = a.GetWidth() / 5; x < a.GetWidth() * 4 / 5; x += 5, n++)
                    d += a.GetPixel(x, y).Luminance - b.GetPixel(x, y).Luminance;
            return (float)(d / Math.Max(1, n) * 255);
        }
        // the view settled first: on a busy machine the valley's meshes are still coming in a while after the move
        var shot0 = GetViewport().GetTexture().GetImage();
        await Frames(2);
        var shot1 = GetViewport().GetTexture().GetImage();
        for (int i = 0; i < 30 && Math.Abs(Darker(shot0, shot1)) > .3f; i++)
        {
            await Frames(4);
            shot0 = shot1;
            await Frames(2);
            shot1 = GetViewport().GetTexture().GetImage();
        }
        // the lines put on and off by turns, and each picture with them weighed against those without them on either
        // side, so that whatever drifts by itself (smoke, a torch, the light) cancels out
        var shots = new List<Image> { shot1 };
        for (int t = 1; t <= 4; t++)
        {
            Settings.ScriptLines(t % 2 == 1);
            await Frames(2);
            shots.Add(GetViewport().GetTexture().GetImage());
        }
        Settings.ScriptLines(false);
        View.Grass.Visible = true;
        View.ShowParts(solid: true, water: true);
        Units.Visible = true;
        Paused = wasPaused;
        float lined = (Darker(shots[0], shots[1]) + Darker(shots[2], shots[1]) + Darker(shots[2], shots[3]) + Darker(shots[4], shots[3])) / 4;
        float idle = Math.Max(Math.Abs(Darker(shots[0], shots[2])), Math.Abs(Darker(shots[2], shots[4])));
        Rig.Set(aim0.x, aim0.z, aim0.yaw, aim0.pitch, aim0.zoom, snap: true);
        Check("the lines round the voxels are off by default, and turned on in the settings they are drawn at once",
            linesOff && lined > .6f && lined > idle * 1.5f, $"the picture {lined:0.00} darker with the lines, {idle:0.00} by itself");
        // first person: one of ours chosen (a swordsman if one is about), I goes into it: the view is its eyes, the frame of
        // the game put away; it walks where it is told, and Esc lets it go, the view back over the valley and it chosen
        // one standing on the ground (not up on a wall, with nowhere to walk), a swordsman if there is one
        bool Grounded(Unit u) => u.Y - World.GroundTop((int)u.X, (int)u.Z) < 3;
        var mine = Sim.Units.Find(u => Sim.CanPossess(u) && u.T == Kinds.Swordsman && Grounded(u)) ?? Sim.Units.Find(u => Sim.CanPossess(u) && u.T.Work < 1 && Grounded(u))
            ?? Sim.Units.Find(u => Sim.CanPossess(u) && Grounded(u))
            // (all of them up on the walls, or hurt: a swordsman come to the gate)
            ?? Sim.Spawn(Kinds.Swordsman, Sim.NearestStand(kx - 40, kz + 30, 4));
        Selected.Clear(); Selected.Add(mine); Hud.OnSelectionChanged();
        // a nick out of its off arm first, so that the arm is drawn on its own: put away with it, and back after
        var nicked = new BodyState(Bodies.Plan(mine.T, mine.Look));
        var arm = nicked.Grid[(int)BodyPart.Off];
        int nick = Array.FindLastIndex(arm.M, m => m != 0);
        arm.Clear(nick % arm.SX + arm.X0, nick / (arm.SX * arm.SZ) + arm.Y0, nick / arm.SX % arm.SZ + arm.Z0);
        nicked.Rev++;
        mine.Wounds = nicked;
        await Frames(2);
        bool nickShown = Units.HurtShown(mine.Id);
        KeyPress(Key.I);
        bool into = FP.On && FP.Unit == mine && Sim.Possessed == mine && FP.Cam.Current && Hud.InFirstPerson && Rig.Held && Units.HiddenId == mine.Id;
        // forward for two seconds; if a wall is right before it, the other ways round, the best of them counting
        float strode = 0, face0 = mine.Facing;
        for (int way = 0; way < 4 && strode <= 2; way++)
        {
            float fx0 = mine.X, fz0 = mine.Z;
            FP.Face(face0 + way * MathF.PI / 2, 0);
            FP.TestMove = new Vector2(0, 1);
            await Frames(2);
            Advance(2);
            FP.TestMove = Vector2.Zero;
            await Frames(2);
            strode = Math.Max(strode, MathF.Sqrt((mine.X - fx0) * (mine.X - fx0) + (mine.Z - fz0) * (mine.Z - fz0)) / VoxelWorld.PerMetre);
        }
        bool farSeen = Atmos.Far.Visible, eyesSky = Atmos.Told["sky_view"].Z > .5f;
        FP._Input(new InputEventKey { PhysicalKeycode = Key.Escape, Keycode = Key.Escape, Pressed = true });
        await Frames(2);
        bool outOf = !FP.On && Sim.Possessed == null && Rig.Cam.Current && !Hud.InFirstPerson && !Rig.Held && Units.HiddenId < 0 && Selected.Contains(mine);
        bool nickBack = Units.HurtShown(mine.Id);
        Check("I goes into the one chosen and the view is its eyes; it walks where it is told; Esc lets it go and the view goes back over the valley, its nicked arm drawn again",
            into && strode > 2 && outOf && nickShown && nickBack, $"in {into}, walked {strode:0.0} m, out {outOf}; the nicked arm drawn {nickShown}, again after {nickBack}; it stood at {mine.X:0},{mine.Y:0},{mine.Z:0} ({mine.Y - World.GroundTop((int)mine.X, (int)mine.Z):0} over the ground), {mine.State}, climbing {Sim.Climbing(mine)}");
        // the land beyond the valley and the sky looked along the view are for someone's eyes; over the diorama the valley
        // floats on its backdrop again
        bool farGone = !Atmos.Far.Visible, backdrop = Atmos.Told["sky_view"].Z < .5f;
        Check("from someone's eyes the far land round the valley shows and the sky is looked along the view; over the diorama neither",
            farSeen && eyesSky && farGone && backdrop && Atmos.Far.Triangles > 10000 && Atmos.Far.Trees > 300,
            $"far land {(farSeen ? "seen" : "unseen")} from the eyes, {(farGone ? "gone" : "still there")} after, {Atmos.Far.Trees} trees, {Atmos.Far.Triangles} triangles; sky {(eyesSky ? "along the view" : "a backdrop")} → {(backdrop ? "a backdrop" : "along the view")}");

        // hand to hand, close before the eye: a swordsman and an orc, too tough to fall, fight it out for a few seconds;
        // the swordsman's blows come many ways, the arms turn every way at the shoulders, and where the blows land
        // sparks, splinters or blood fly and the blade leaves its smear
        var ways = new HashSet<StrokeKind>();
        int landed = 0;
        var duelist = Sim.Spawn(Kinds.Swordsman, Sim.NearestStand(kx - 140, kz + 150, 2));
        var orc = Sim.Spawn(Kinds.Orc, Sim.NearestStand((int)duelist.X + 11, (int)duelist.Z, 2));
        void Watch(Smiting h) { if (h.By == duelist) { ways.Add(h.Stroke); landed++; } }
        Sim.Smote += Watch;
        duelist.Hold = true; duelist.Post = duelist.Dest = duelist.Cell;
        orc.Guard = true; orc.Home = orc.Cell;
        duelist.Hp = duelist.MaxHp = orc.Hp = orc.MaxHp = 100000;
        var aim1 = Rig.Aim;
        // the camera looks at a plane 4 m up: past it by as much as the two stand under it
        float overD = (World.WY(duelist.Y + 8) - 4) / MathF.Tan(.5f);
        Rig.Set(World.WX(duelist.X) - MathF.Sin(-.3f) * overD, World.WZ(duelist.Z) - MathF.Cos(-.3f) * overD, -.3f, .5f, 7, snap: true);
        int twisted0 = Units.Twisted, smears0 = Units.Smears, thrown0 = Debris.Sparked + Debris.Sprayed, blows0 = Units.Blows, coming0 = Units.BlowsSeenComing, drips0 = Blood.Dripped;
        for (int i = 0; i < 140; i++) { Advance(.05); await Frames(1); }
        Sim.Smote -= Watch;
        // the blade is seen brought down to the body by the moment the blow is struck, so that the wound opens as it gets
        // there: all but the first, struck as the two came into reach
        int blowsH = Units.Blows - blows0, comingH = Units.BlowsSeenComing - coming0;
        Check("hand to hand the blows come many ways, the arms turn every way, the blade gets to the body as the blow is struck, and where it lands sparks, splinters or blood fly and it leaves its smear",
            ways.Count >= 3 && Units.Twisted > twisted0 && Units.Smears > smears0 && Debris.Sparked + Debris.Sprayed > thrown0 && blowsH >= 4 && comingH * 3 >= blowsH * 2,
            $"{landed} blows, {ways.Count} ways ({string.Join(", ", ways)}); {comingH} of {blowsH} blows of the two brought down to the body by the time they were struck; {Units.Twisted - twisted0} figures turned out of a plain swing, {Units.Smears - smears0} smears, {Debris.Sparked + Debris.Sprayed - thrown0} sparks and bits");
        duelist.Dead = orc.Dead = true; duelist.DiedAt = orc.DiedAt = -999;

        // blood spilt: the blows spatter the grass, the wounded drip as they go, and one killed lies in a pool spreading from under it
        int pools0 = Blood.Pools, stainsH = Blood.Stains;
        var slain = Sim.Spawn(Kinds.Goblin, Sim.NearestStand(kx - 140, kz + 175, 2));
        Sim.Hurt(slain, null, 1000, "test");
        for (int i = 0; i < 20; i++) { Advance(.1); await Frames(1); }
        Check("blood is spilt: the blows spatter the ground, the wounded drip as they go, and one killed lies in a pool",
            Blood.Dripped > drips0 && stainsH > 20 && Blood.Pools > pools0 && Blood.Stains > stainsH,
            $"{Blood.Dripped - drips0} drops dripped, {stainsH} stains after the fight, {Blood.Pools - pools0} pool spreading, {Blood.Stains} stains now");
        Rig.Set(aim1.x, aim1.z, aim1.yaw, aim1.pitch, aim1.zoom, snap: true);

        // work marked is followed: a tag over it says how far it has got, and when it is done the chronicle says so
        // (the valley cleared of raiders left over, which would keep the workers in the citadel)
        foreach (var u in Sim.Units) if (u.Team == Team.Horde && !u.Dead && !u.Bound && !u.Inside) { u.Dead = true; u.DiedAt = -999; }
        int lowTop = Shapes.LevelTop(World, kx - 110, kz + 70, 4);
        var low = Shapes.Line(World, Site, kx - 116, kz + 70, kx - 104, kz + 70, new Stroke { Mat = Mat.Stone, Width = 2, Height = 2, Level = true }, lowTop);
        int finished0 = Progress.Finished, lines0 = Hud.Lines.Count;
        Sim.DesignateBuild(low);
        Progress.Track("Камень", low, false);
        bool following = Progress.Following > 0;
        for (int t = 0; t < 240 && Progress.Finished == finished0; t++) { Advance(.5); await Frames(1); }
        bool toldDone = false;
        for (int i = lines0; i < Hud.Lines.Count; i++) toldDone |= Hud.Lines[i].text.StartsWith("Готово: камень");
        Check("work marked is followed with how far it has got, and when it is done the chronicle says so",
            following && Progress.Finished > finished0 && toldDone,
            $"followed {following}; finished {Progress.Finished - finished0}, told {toldDone}; " +
            string.Join(", ", Sim.Units.FindAll(u => u.T == Kinds.Worker && !u.Dead).ConvertAll(u => $"#{u.Id} {u.State} {u.Why}")));

        // the citadel's muster: a click on the citadel opens it; a worker taken on is paid for at once and comes out of
        // its door only when made ready. Whoever stands about the citadel's foot by now is sent a little way off first (an
        // envoy come to the walls is taken off), so that the click finds the citadel and not one of them
        if (Sim.Offer?.Envoy is { } envoy) { Sim.Offer = null; envoy.Dead = true; envoy.DiedAt = -999; }
        // (out of the camera's way, which looks from the south-east: to the north-west of the citadel)
        var about = Sim.Units.FindAll(u => !u.Dead && !u.Inside && u.Team == Team.Player && Brushes.Dist(u.X - kx, u.Z - kz) < Site.KeepR + 70);
        if (about.Count > 0) { Sim.Command(about, Sim.NearestStand(kx - 100, kz - 100, 2)); for (int t = 0; t < 40; t++) Advance(.5); }
        Selected.Clear(); Hud.OnSelectionChanged();
        Sim.Stock.Gold = 500;
        int w0 = Sim.Count(Kinds.Worker);
        await Look(kx, kz);
        await ClickAt(ScreenAt(kx, Site.KeepBase + 40, kz));
        bool keepOpen = Hud.KeepShown;
        string instead = Inspected != null ? $" ({Inspected.T.Name} looked at instead)" : "";
        foreach (var u in Selected) instead = $" ({u.T.Name} chosen instead)";
        Hud.Inspector.TileOf(Kinds.Worker)?.Press();
        bool paidNow = Math.Abs(Sim.Stock.Gold - (500 - Kinds.Worker.Cost)) < .01 && Sim.Count(Kinds.Worker) == w0 + 1;
        Advance(8);
        Check("a click on the citadel opens its muster; a worker taken on is paid for and comes out at once",
            keepOpen && paidNow && Sim.Count(Kinds.Worker) == w0 + 1 && Sim.Places == Sim.CitadelPlaces,
            $"muster {(keepOpen ? "open" : "not open")}{instead}, paid at once {paidNow}, {Sim.Stock.Gold:0} gold left, workers {w0} → {Sim.Count(Kinds.Worker)}");
        KeyPress(Key.Escape);
        Check("Esc lets the citadel go", !KeepChosen && !Hud.KeepShown);
        // the game written down, and its head read back for the list of saves
        bool saved = Saves.Write(this, "selftest");
        var peek = Saves.Peek("selftest");
        Check("the game is saved, and the list of saves can read what it is", saved && peek is { } pk && pk.label.Length > 0 && Math.Abs(pk.time - Sim.Time) < .01,
            $"{Saves.PathOf("selftest")}: {(peek is { } pv ? $"«{pv.label}», {pv.time:0} s" : "unreadable")}, {new System.IO.FileInfo(Saves.PathOf("selftest")).Length / 1024} KB");

        // the sky goes with the day: high sun and a bright sky at noon; at the dusk the sun on the horizon and the sky red
        // there; at night the sun under it, the stars out, the moon up; under the spell the moon red and the horizon too
        Vector4 Sky(string name) => Atmos.Told[name];
        float Luma(Vector4 c) => .2126f * c.X + .7152f * c.Y + .0722f * c.Z;
        async Task<(Vector4 sun, Vector4 zenith, Vector4 horizon, Vector4 light, Vector4 moon)> SkyAt(float day, float dark)
        {
            Atmos.Pin(day, dark);
            await Frames(2);
            return (Sky("sky_sun"), Sky("sky_zenith"), Sky("sky_horizon"), Sky("sky_light"), Sky("sky_moon"));
        }
        // under a clear sky: rain or fog would grey it all
        var weatherWas = Sim.Weather;
        Sim.Weather = Weather.Clear;
        var noon = await SkyAt(.5f, 0);
        var dusk = await SkyAt(1, .5f);
        var night = await SkyAt(1, 1);
        double bloodWas = Sim.BloodMoonUntil;
        Sim.BloodMoonUntil = Sim.Time + 100;
        Atmos.Snap();
        var blood = await SkyAt(1, 1);
        Sim.BloodMoonUntil = bloodWas;
        Sim.Weather = weatherWas;
        Atmos.Unpin();
        await Frames(2);
        float Deg(Vector4 v) => Mathf.RadToDeg(MathF.Asin(Math.Clamp(v.Y, -1, 1)));
        Check("the sky goes with the day: a high sun at noon, the sun going down red at dusk, the stars and the moon at night, the blood moon red",
            Deg(noon.sun) > 40 && Luma(noon.zenith) > .1f && noon.zenith.W < .01f
            && Deg(dusk.sun) is > -2 and < 6 && dusk.horizon.X > dusk.horizon.Z * 2.5f
            && Deg(night.sun) < -8 && Luma(night.zenith) < .01f && night.zenith.W > .9f && night.light.W > .9f
            && blood.moon.W > .9f && blood.horizon.X > blood.horizon.Y * 4,
            $"sun {Deg(noon.sun):0}° at noon, {Deg(dusk.sun):0.0}° at dusk, {Deg(night.sun):0}° at night; zenith {Luma(noon.zenith):0.000} → {Luma(night.zenith):0.0000}; " +
            $"dusk horizon {dusk.horizon.X:0.00},{dusk.horizon.Y:0.00},{dusk.horizon.Z:0.00}; stars {night.zenith.W:0.00}, moon {night.light.W:0.00}, red {blood.moon.W:0.00}");
        // a wave beaten back starts the day over in the dark: the night does not go through noon on the way to the morning,
        // the sun stays under the horizon while it is dark; and lightning seen as the game is stopped dies away all the same
        Atmos.Pin(1, 1);
        await Frames(2);
        bool pausedWas = Paused;
        Paused = true;
        Atmos.Pin(0, 1, eased: true);
        Atmos.Flash(Vector3.Up, 1);
        float highest = -90, struck = 0;
        ulong waitFrom = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - waitFrom < 2000)
        {
            await Frames(1);
            highest = Math.Max(highest, Deg(Sky("sky_sun")));
            struck = Math.Max(struck, Sky("sky_flash").W);
        }
        float shownDay = Atmos.ShownDay, struckAfter = Sky("sky_flash").W;
        Paused = pausedWas;
        Atmos.Unpin();
        await Frames(2);
        Check("a wave beaten back: the night stays night, the sun under the horizon, the morning taken up at once; lightning dies away while the game is stopped",
            highest < -5 && shownDay < .01f && struck > .05f && struckAfter < .01f,
            $"the sky's sun at most {highest:0.0}° over two seconds, the day shown {shownDay:0.00}; lightning {struck:0.00} → {struckAfter:0.00}");
        // a long game's chronicle: past its 80 lines the oldest go, and the game goes on (it used to hang right there)
        for (int i = 0; i < 120; i++) Hud.Log($"проверка летописи {i}", toast: false);
        await Frames(2);
        Check("the chronicle keeps its last 80 lines and the game goes on past them", Hud.HistoryCount == 80 && Hud.Lines.Count == 80,
            $"{Hud.HistoryCount} in the tab, {Hud.Lines.Count} kept");
        // the frame unfolding, from nothing done (what the test did before is set aside): at the start no circle on the
        // belt nor its key, no lore tab, and so it stays while nothing is done; three markings let the circle out marked
        // new, a beaten night the lore; and all at once again after
        Tools.SetTool(Tool.None);
        int markedWas = Sim.MarkingsMade, dugWas = Tools.DigMarkings, numberWas = Sim.Waves.Number, dayWas = Sim.Clock.Day;
        double knewWas = Sim.Knowledge;
        Sim.MarkingsMade = 0; Tools.DigMarkings = 0; Sim.Waves.Number = 0; Sim.Knowledge = 0; Sim.Clock.Day = 1;
        Hud.Unfold.Begin();
        bool hiddenAtFirst = Hud.Locked(Tool.Circle) && Hud.Hidden(Hud.Part.Lore) && !Hud.Locked(Tool.Brush);
        Tools.SetTool(Tool.Circle);
        bool refused = Tools.Tool != Tool.Circle;
        // (it looks once a second, by the clock on the wall)
        for (ulong from = Time.GetTicksMsec(); Time.GetTicksMsec() - from < 1300;) await Frames(1);
        bool stays = Hud.Locked(Tool.Circle) && Hud.Hidden(Hud.Part.Lore);
        Sim.MarkingsMade = 3; Sim.Waves.Number = 1;
        for (ulong from = Time.GetTicksMsec(); Time.GetTicksMsec() - from < 1300;) await Frames(1);
        bool letOut = !Hud.Locked(Tool.Circle) && Sim.LetOut.Contains("tool:Circle") && Hud.Fresh(Tool.Circle) && Hud.Locked(Tool.Bridge)
            && !Hud.Hidden(Hud.Part.Lore);
        Sim.MarkingsMade = markedWas; Tools.DigMarkings = dugWas; Sim.Waves.Number = numberWas; Sim.Knowledge = knewWas; Sim.Clock.Day = dayWas;
        Hud.Unfold.OpenAll();
        Check("the frame unfolds: no circle and no lore at the start, its key refused, so while nothing is done; three markings let the circle out marked new (not the bridge), a night the lore",
            hiddenAtFirst && refused && stays && letOut,
            $"hidden at first {hiddenAtFirst}, key refused {refused}, stays {stays}, let out {letOut}");
        await AcidSelfTest((what, ok, detail) => Check(what, ok, detail));
        await SelfTestOurAlchemist((what, ok, detail) => Check(what, ok, detail));
        await LiquidsSelfTest((what, ok, detail) => Check(what, ok, detail));
        await SelfTestGiants(Check);
        await SelfTestTrauma(Check);
        await SelfTestWand(Check);
        await SelfTestHouses(Check);
        await SelfTestScaffold(Check);
        await SelfTestCarry(Check);
        await SelfTestOurEngines(Check);
        await SelfTestTown(Check);
        await MinesSelfTest((what, ok, detail) => Check(what, ok, detail));
        await SelfTestYard(Check);
        await SelfTestTemplates(Check);
        await SelfTestHex(Check);
        await SelfTestCaptain(Check);
        await SelfTestMetal(Check);
        // the report for the author: one file with the system, the hangs, the last events and the logs
        string report = Diag.Report(this, ProjectSettings.GlobalizePath("user://logs"));
        string body = System.IO.File.Exists(report) ? System.IO.File.ReadAllText(report) : "";
        System.IO.File.Delete(report);
        Check("«Отчёт об ошибке» writes one file with the system, the video card, the hangs, the last events and the logs",
            body.Contains("Видеокарта:") && body.Contains("===== Зависания") && body.Contains("===== Последние события") && body.Contains("===== Лог "),
            $"{body.Length} characters in {System.IO.Path.GetFileName(report)}");
        await SelfTestStrain(Check, Frames);
        GD.Print($"[selftest] {(fails == 0 ? "all passed" : fails + " failed")}");
        if (_shot == null) GetTree().Quit(fails == 0 ? 0 : 1);
    }

    /// <summary>
    /// The hill giant on the screen: told to throw west of the citadel, it stoops to the stone (its body bent over), the
    /// panel says it is tearing a boulder out, the stone is drawn in the air as it goes, and the heave is heard; the
    /// horde tab and the dusk card say what it is.
    /// </summary>
    async Task SelfTestGiants(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        // on level ground with no tree by it: torn out of a slope, the stone lets the rim slump in on the giant's feet
        bool Level(int x, int z)
        {
            int h = World.SurfAt(x, z);
            for (int dz = -16; dz <= 16; dz += 4)
                for (int dx = -16; dx <= 16; dx += 4)
                    if (!World.InBounds(x + dx, 1, z + dz) || Math.Abs(World.SurfAt(x + dx, z + dz) - h) > 1) return false;
            return true;
        }
        var (gx, gz) = Nearest(Site.KeepX - Site.KeepR - 220, Site.KeepZ + 30, 120, Level);
        ClearTrees(gx - 30, gz - 30, gx + 30, gz + 30);
        var g = Sim.Spawn(Kinds.HillGiant, Sim.NearestStand(gx, gz, 3));
        g.Guard = true; g.Home = g.Cell; g.Hp = g.MaxHp = 1e5f; g.NextHurl = 1e9;
        Rig.SetAt(World.WX(g.X), World.WY(g.Y + 14), World.WZ(g.Z), -.7f, .45f, 3f);
        await Frames(6);
        float upright = Units.TryBody(g.Id, out var b0) ? b0.Basis.Y.Normalized().Y : 0;
        float tx = g.X + 150, tz = g.Z;
        bool ordered = Sim.OrderHurl(g, tx, World.SurfAt((int)tx, (int)tz) + 4, tz);
        Advance(.35);
        await Frames(3);
        float stooped = Units.TryBody(g.Id, out var b1) ? b1.Basis.Y.Normalized().Y : 1;
        string doing = Retinue.Doing(g, Sim);
        int heard = Sound.Played;
        for (int i = 0; i < 40 && g.Hefted != null; i++) { Advance(.05); await Frames(1); }
        await Frames(3);
        bool heaved = Sound.Played > heard && Sound.Heard.Contains("swish");
        bool flying = Sim.Fliers.Exists(f => f.Cause == Sim.HurledCause && f.Carrier == null);
        check("a hill giant told to throw stoops to tear the stone out, the panel says so, and the stone goes off with the sound of it; the dusk card knows it",
            ordered && upright > .95f && stooped < .9f && doing.StartsWith("выворачивает валун") && heaved && flying && Sim.GiantHurled > 0 && DuskCard.Against.ContainsKey("hillgiant"),
            $"ordered {ordered}, upright {upright:0.00}, stooping {stooped:0.00}; «{doing}»; heard {heaved}, in the air {flying}, hurled {Sim.GiantHurled}, still in hand {g.Hefted != null}, down {g.Down}, bones {Sim.Broken(g)}");
        Advance(6);
        g.Dead = true; g.DiedAt = -999;
        await Frames(2);
    }

    /// <summary>
    /// What blunt things do (Sim.Trauma.cs), in the game: one of ours buried under a heap of rubble is pinned, tagged over
    /// the heap, and dug out by a worker near; a broken leg limps and is told in the console.
    /// </summary>
    async Task SelfTestTrauma(Action<string, bool, string> check)
    {
        var sim = Sim; var w = World;
        // nothing of what went before in the way: no alarm, no raider about, nothing still flying or rolling (the hill
        // giant's boulder) to come down on the one buried
        if (sim.Alarm) sim.StandDown();
        for (int i = 0; i < 40 && sim.Fliers.Count > 0; i++) Advance(.5);
        foreach (var r in sim.Units) if (r.Team == Team.Horde && !r.Dead) { r.Dead = true; r.DiedAt = -999; }
        int cx = Site.KeepX - Site.KeepR - 150, cz = Site.KeepZ + 60;
        ClearTrees(cx - 40, cz - 40, cx + 40, cz + 40);
        var buried = sim.Spawn(Kinds.Worker, sim.NearestStand(cx, cz, 4));
        var digger = sim.Spawn(Kinds.Worker, sim.NearestStand(cx + 24, cz, 4));
        Mound(buried);
        Advance(.5);
        Rig.Focus(w.WX(cx), w.WZ(cz));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        bool pinned = buried.PinKg > 0, tagged = false;
        foreach (var n in GetChildren()) if (n is PinTags t) foreach (var c in t.GetChildren()) foreach (var l in c.GetChildren()) if (l is Label { Visible: true } lab && lab.Text.StartsWith("Придавлен")) tagged = true;
        string doing = Retinue.DoingNow(buried, sim);
        for (int i = 0; i < 60 && buried.PinKg > 0; i++) Advance(.5);
        Advance(2);
        check("one of ours buried under rubble is pinned, tagged, and dug out by a worker near",
            pinned && tagged && doing.StartsWith("придавлен") && buried.PinKg == 0 && !buried.Dead && !buried.Down,
            $"pinned {pinned}, tagged {tagged}, «{doing}»; now {buried.PinKg:0} kg, {(buried.Dead ? $"dead ({buried.KilledBy})" : $"{buried.Hp:0} hp")}, down {buried.Down}; the digger: {Retinue.DoingNow(digger, sim)}");
        sim.BreakBone(digger, Break.LegR);
        string hurt = Retinue.Hurt(digger);
        check("a broken leg limps and is told", Sim.Gait(digger) < .4f && hurt.Contains("сломана правая нога") && !sim.Holds(digger), $"gait {Sim.Gait(digger):0.00}, «{hurt}»");
        buried.Dead = digger.Dead = true; buried.DiedAt = digger.DiedAt = -999;
    }

    /// <summary>
    /// Weight and cracks, seen: a wall struck hard has its cracks drawn; a round tower dug out under two thirds of its foot
    /// goes over whole, drawn turning in the air, and lies in rubble a few seconds after.
    /// </summary>
    async Task SelfTestStrain(Action<string, bool, string> check, Func<int, Task> frames)
    {
        var w = World;
        int x0 = Site.KeepX - Site.KeepR - 160, z0 = Site.KeepZ + 160, g = w.SurfAt(x0, z0);
        ClearTrees(x0 - 40, z0 - 40, x0 + 100, z0 + 40);
        for (int z = z0 - 30; z < z0 + 30; z++)
            for (int x = x0 - 30; x < x0 + 90; x++)
            {
                for (int y = g; y < g + 70; y++) if (w.Get(x, y, z) != Mat.Air) w.Set(x, y, z, Mat.Air);
                for (int y = g - 6; y < g; y++) if (!Mat.IsEarthy[w.Get(x, y, z)]) w.Set(x, y, z, Mat.Soil);
            }
        Sim.InstantLabour = true;
        Sim.DesignateBuild(Shapes.Circle(w, Site, x0, z0, 9, new Stroke { Mat = Mat.Stone, Width = 3, Height = 48, Level = true, Hollow = true }, g + 48));
        Sim.DesignateBuild(Shapes.Line(w, Site, x0 + 60, z0 - 20, x0 + 60, z0 + 20, new Stroke { Mat = Mat.Stone, Width = 8, Height = 24, Level = true }, g + 24));
        Sim.InstantLabour = false;
        Advance(1);
        Rig.Focus(w.WX(x0 + 30), w.WZ(z0));
        Sim.Jar(x0 + 55, g + 12, z0, 6, .7f);
        await frames(20);
        int cracks = Collapse.CracksDrawn;
        int over = 0;
        Sim.GoingOver += f => over++;
        for (int z = z0 - 10; z <= z0 + 10; z++) for (int x = x0 - 3; x <= x0 + 12; x++) for (int y = g - 4; y < g; y++) w.Set(x, y, z, Mat.Air);
        Advance(1.5);
        await frames(2);
        int drawn = Magic.PiecesDrawn;
        Advance(10);
        int standing = 0;
        for (int z = z0 - 10; z <= z0 + 10; z++) for (int x = x0 - 10; x <= x0 + 10; x++) for (int y = g + 16; y < g + 50; y++) if (Mat.IsMasonry(w.Get(x, y, z))) standing++;
        check("a wall struck hard has its cracks drawn; a tower dug out under its foot goes over whole, drawn in the air, and lies in rubble after",
            cracks > 20 && over > 0 && drawn > 1000 && standing < 100,
            $"{cracks} cracked voxels drawn; went over {over} times, {drawn} voxels drawn in the air; {standing} of it standing over two metres after");
    }
}
