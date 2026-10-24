using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>The set-ups for screenshots (--demo, --pose=…): the fort in the making, the sieges, the spells, the blasts, the traps, the works.</summary>
public partial class Main
{
    /// <summary>
    /// A fort in the making, set up the way a player would: a curtain wall with round towers and stairs, a moat let in from
    /// the pond (the rest of it marked for the workers), merlons and a wooden walk marked on the wall, a tree to fell,
    /// deer grazing beyond. With --pose=battle the moat is full and the horde is at it, bridging and raising ladders.
    /// </summary>
    void Demo()
    {
        if (_pose == "sky") { Skies(); return; }
        if (_trailer >= 0) { Trailer(); return; }
        if (_pose == "battle") { Siege(); return; }
        if (_pose == "fire") { Blaze(); return; }
        if (_pose == "sap") { Sapping(); return; }
        if (_pose == "shelter") { Sheltering(); return; }
        if (_pose == "dwelling") { Housing(); return; }
        if (_pose == "traps") { Trapped(); return; }
        if (_pose == "drawbridge") { Drawn(); return; }
        if (_pose == "thieves") { Robbed(); return; }
        if (_pose == "engines") { Engines(); return; }
        if (_pose == "wounds") { Wounded(); return; }
        if (_pose == "trauma") { Trauma(); return; }
        if (_pose == "flood") { Flood(); return; }
        if (_pose == "trees") { LookAtGrove(); return; }
        if (_pose == "grass") { Meadow(); return; }
        if (_pose == "people") { People(); return; }
        if (_pose == "beasts") { Beasts(); return; }
        if (_pose == "walk") { Walkers(); return; }
        if (_pose == "duel") { Duel(); return; }
        if (_pose == "reel") { Reeling(); return; }
        if (_pose == "blood") { Bloodshed(); return; }
        if (_pose == "works") { Works(); return; }
        if (_pose == "dusk") { Dusk(); return; }
        if (_pose == "dawn") { DawnPose(); return; }
        if (_pose == "troops") { Troops(); return; }
        if (_pose == "faith") { Faith(); return; }
        if (_pose == "captives") { Captives(); return; }
        if (_pose == "spells") { Spellwork(); return; }
        if (_pose == "wonders") { Wonders(); return; }
        if (_pose == "fp") { Eyes(); return; }
        if (_pose == "shaman") { Hexing(); return; }
        if (_pose == "blast") { Blasting(); return; }
        if (_pose == "grip") { Gripping(); return; }
        if (_pose == "wind") { Gale(); return; }
        if (_pose == "hexes") { Hexes(); return; }
        if (_pose == "sorcerers") { Sorcerers(); return; }
        if (_pose == "powder") { Kegs(); return; }
        if (_pose == "charge") { Sapped(); return; }
        if (_pose == "stones") { Stones(); return; }
        if (_pose == "giants") { HillGiants(); return; }
        if (_pose == "topple") { Topple(); return; }
        if (_pose == "undermine") { Undermine(); return; }
        if (_pose == "cracks") { Cracked(); return; }
        if (_pose == "roll") { RollDown(); return; }
        if (_pose == "crush") { CrushDeck(); return; }
        if (_pose == "drops") { Drops(); return; }
        if (_pose == "felling") { Felling(); return; }
        if (_pose == "acid") { Acid(); return; }
        if (_pose == "wand") { WandPose(); return; }
        if (_pose == "sigils")
        {
            var layer = new CanvasLayer { Layer = 100 };
            var gallery = new SigilGallery { Page = Math.Max(0, _look) };
            gallery.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(gallery);
            AddChild(layer);
            return;
        }
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = Waves.Prep;
        sim.Stock.Gold = 140;
        sim.Stock[Stockpile.Stone] = 60 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] = 40 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Earth] = 30 * VoxelWorld.PerM3;
        Stroke St(byte m, int width, int height, bool hollow = false) => new() { Mat = m, Width = width, Height = height, Level = true, Hollow = hollow };
        const int wx = 676, z0 = 436, z1 = 596, mx = wx - 30;
        bool battle = _pose == "battle";

        // what was built before: the wall, two towers, stairs up from the yard
        sim.InstantLabour = true;
        int top = Shapes.LevelTop(w, wx, 516, 40);
        sim.DesignateBuild(Shapes.Line(w, s, wx, z0, wx, z1, St(Mat.Stone, 12, 40), top));
        foreach (int tz in new[] { z0 - 8, z1 + 8 })
        {
            int tt = Shapes.LevelTop(w, wx, tz, 64);
            sim.DesignateBuild(Shapes.Circle(w, s, wx, tz, 15, St(Mat.Stone, 5, 64, hollow: true), tt));
            sim.DesignateBuild(Shapes.Circle(w, s, wx, tz, 11, St(Mat.Rammed, 1, 56), Shapes.LevelTop(w, wx, tz, 56)));
        }
        for (int k = 0; k < 5; k++)
            sim.DesignateBuild(Shapes.Line(w, s, wx + 10 + k * 8, 540, wx + 10 + k * 8, 552, St(Mat.Stone, 8, 0), top - k * 8));
        // the moat's southern half is dug, and a cut from its end into the nearest pond lets the water in; in battle all of it
        var moat = new Stroke { Width = 16, Height = 20, Dig = true };
        sim.DesignateRemove(Shapes.Line(w, s, mx, battle ? z0 - 24 : 524, mx, 684, moat, 0));
        var pond = s.Ponds[0];
        foreach (var p in s.Ponds)
            if (Brushes.Dist(p.X - mx, p.Z - 684) < Brushes.Dist(pond.X - mx, pond.Z - 684)) pond = p;
        double pd = Brushes.Dist(mx - pond.X, 684 - pond.Z);
        int reach = MapGen.Shore(w, s, pond.X, pond.Z, mx - pond.X, 684 - pond.Z) - 6;
        int cutX = pond.X + (int)((mx - pond.X) / pd * reach), cutZ = pond.Z + (int)((684 - pond.Z) / pd * reach);
        sim.DesignateRemove(Shapes.Line(w, s, mx, 684, cutX, cutZ, new Stroke { Width = 8, Height = 20, Dig = true }, 0));
        sim.InstantLabour = false;

        // what is marked for the pawns now: merlons and a wooden walk on the wall, the rest of the moat, a tree to fell
        for (int z = z0 + 4; z <= z1 - 8; z += 12) sim.DesignateBuild(Shapes.Box(w, s, wx - 6, z, wx - 3, z + 5, St(Mat.Stone, 4, 10), top + 10));
        sim.DesignateBuild(Shapes.Line(w, s, wx + 2, z0, wx + 2, z1, St(Mat.Wood, 6, 3), top + 3));
        if (!battle) sim.DesignateRemove(Shapes.Line(w, s, mx, z0 - 24, mx, 524, moat, 0));
        var (treeX, treeZ) = Nearest(691, 670, 80, (x, z) => Mat.IsTrunk(w.Get(x, w.SurfAt(x, z) + 2, z)));
        sim.DesignateRemove(Shapes.Tree(w, treeX, w.SurfAt(treeX, treeZ) + 4, treeZ));
        // a grove and a quarry set aside: the workers turn to them whenever the marked work runs out
        sim.AddZone(treeX + 20, treeZ - 40, treeX + 110, treeZ + 40, Stockpile.Wood, 0);
        var (rx, rz) = Nearest(705, 475, 60, (x, z) => w.Get(x, w.SurfAt(x, z) - 1, z) == Mat.Rock && w.SurfAt(x, z) > s.HTop[z * w.SX + x] + 3);
        sim.AddZone(rx - 16, rz - 16, rx + 16, rz + 16, Stockpile.Stone, 16);

        // more hands, the archers up on the wall, deer grazing out of the way
        for (int i = 0; i < 4; i++) sim.Spawn(Kinds.Worker, sim.Gate);
        var archers = new List<Unit> { sim.Spawn(Kinds.Archer, sim.Gate) };
        foreach (var u in sim.Units) if (u.T == Kinds.Archer && !archers.Contains(u)) archers.Add(u);
        var swords = new List<Unit>();
        foreach (var u in sim.Units) if (u.T == Kinds.Swordsman) swords.Add(u);
        swords.Add(sim.Spawn(Kinds.Swordsman, sim.Gate));
        int walk = sim.NearestStand(wx, 500, 4), walk2 = sim.NearestStand(wx, 560, 4);
        if (walk >= 0) sim.Command(archers.GetRange(0, 1), walk);
        if (walk2 >= 0) sim.Command(archers.GetRange(1, archers.Count - 1), walk2);
        int yard = sim.NearestStand(wx + 30, 500, 4);
        if (yard >= 0) sim.Command(swords, yard);
        foreach (var (dx, dz) in new[] { (0, 0), (14, 9), (-10, 16), (6, -14) })
        {
            var deer = sim.Spawn(Kinds.Deer, sim.NearestStand(wx + 70 + dx, 770 + dz, 3));
            deer.Home = deer.Cell;
        }
        Advance(battle ? 30 : 55);

        if (battle)
        {
            // the horde already at the moat with timber for bridges and ladders
            sim.Waves.Countdown = 0;
            sim.Step(.05f);
            sim.Waves.Countdown = 1e9;
            sim.HordeStock[Stockpile.Wood] = 24 * VoxelWorld.PerM3;
            foreach (var (t, x, z) in new[] { (Kinds.Sapper, wx - 64, 492), (Kinds.Sapper, wx - 62, 532), (Kinds.Sapper, wx - 66, 566), (Kinds.Goblin, wx - 70, 510),
                (Kinds.Goblin, wx - 74, 548), (Kinds.Orc, wx - 80, 520), (Kinds.Orc, wx - 84, 500), (Kinds.Troll, wx - 100, 530) })
            {
                int c = sim.NearestStand(x, z, t.Move.Height);
                if (c >= 0) sim.Spawn(t, c);
            }
            sim.ComputeFields();
            Advance(16);
            Rig.Set(World.WX(wx - 22), World.WZ(520), -.95f, .62f, 1.3f, snap: true);
        }
        else if (_pose == "close") Rig.Set(World.WX(wx - 22), World.WZ(526), -.75f, .55f, 3.4f, snap: true);
        else Rig.Set(World.WX(wx + 4), World.WZ(600), -.7f, .62f, 1.1f, snap: true);

        AddBlueprint(Blueprint.Capture(w, wx - 16, z1 - 6, wx + 16, z1 + 26, NextBlueprintId++, "Круглая башня"), save: false);
        AddBlueprint(Blueprint.Capture(w, wx - 7, 500, wx + 7, 530, NextBlueprintId++, "Пролёт стены"), save: false);
        Hud.Refresh();
        Marks.Flush();

        if (_pose == "line") { Tools.SetTool(Tool.Line); Tools.SetDig(true); }
        else if (_pose is not ("battle" or "close")) Tools.StartPaste(Blueprints[0]);
        foreach (var u in archers) Selected.Add(u);
        CallDeferred(MethodName.PoseDemo);
    }

    /// <summary>
    /// The citadel ringed by a stone wall and a moat full of water, archers on the wall, and the horde arrived with timber:
    /// sappers laying bridges over the water and raising ladders against the wall, goblins and orcs behind them.
    /// </summary>
    void Siege()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.Stock.Gold = 90;
        int kx = s.KeepX, kz = s.KeepZ;
        sim.InstantLabour = true;
        int top = Shapes.LevelTop(w, kx - 64, kz, 40);
        sim.DesignateBuild(Shapes.Circle(w, s, kx, kz, s.KeepR + 44, new Stroke { Mat = Mat.Stone, Width = 10, Height = 40, Level = true, Hollow = true }, top));
        var moat = Shapes.Circle(w, s, kx, kz, s.KeepR + 84, new Stroke { Width = 18, Height = 24, Dig = true, Hollow = true }, 0);
        sim.DesignateRemove(moat);
        sim.InstantLabour = false;
        // let the water in, up to a hand below the brim
        int level = w.SurfAt(kx - 130, kz) - 3;
        for (int i = 0; i < moat.Count; i++)
            if (moat.Y(i) < level && w.Get(moat.X(i), moat.Y(i), moat.Z(i)) == Mat.Air) w.Set(moat.X(i), moat.Y(i), moat.Z(i), Mat.Water);
        var archers = new List<Unit>();
        foreach (var (dx, dz) in new[] { (-62, -14), (-60, 18), (-52, -36), (-48, 40), (-63, 2) })
        {
            int c = sim.NearestStand(kx + dx, kz + dz, 4);
            var a = sim.Spawn(Kinds.Archer, c);
            sim.Command(new[] { a }, c);
            archers.Add(a);
        }
        Advance(2);
        sim.Waves.Countdown = 0;
        sim.Step(.05f);
        sim.Waves.Countdown = 1e9;
        sim.HordeStock[Stockpile.Wood] = 24 * VoxelWorld.PerM3;
        foreach (var (t, dx, dz) in new[] { (Kinds.Sapper, -122, -20), (Kinds.Sapper, -120, 12), (Kinds.Sapper, -118, 36), (Kinds.Goblin, -130, 0),
            (Kinds.Goblin, -134, 24), (Kinds.Orc, -138, -8), (Kinds.Orc, -142, 18), (Kinds.Troll, -150, 4), (Kinds.Goblin, -126, -34) })
        {
            int c = sim.NearestStand(kx + dx, kz + dz, t.Move.Height);
            if (c >= 0) sim.Spawn(t, c);
        }
        sim.ComputeFields();
        Advance(_poseSeconds);
        Rig.Set(World.WX(kx - 64), World.WZ(kz + 2), -1.0f, .6f, 1.7f, snap: true);
        Hud.Refresh();
        Marks.Flush();
        foreach (var u in archers) Selected.Add(u);
    }

    /// <summary>
    /// The siege by fire: a ring of wall round the citadel whose western arc is of logs, a dry ditch before it floored
    /// with pitch, archers on the wall with fire arrows, goblins and orcs with torches coming at the timber, and a tree
    /// by the ditch already alight.
    /// </summary>
    void Blaze()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.Stock.Gold = 90;
        int kx = s.KeepX, kz = s.KeepZ;
        bool West(int x, int z) => MathF.Abs(Mathf.Wrap(MathF.Atan2(z - kz, x - kx) - MathF.PI, -MathF.PI, MathF.PI)) < .55f;
        sim.InstantLabour = true;
        int top = Shapes.LevelTop(w, kx - 64, kz, 40);
        var ring = Shapes.Circle(w, s, kx, kz, s.KeepR + 44, new Stroke { Mat = Mat.Stone, Width = 10, Height = 40, Level = true, Hollow = true }, top);
        var laid = new VoxelList();
        for (int i = 0; i < ring.Count; i++) laid.Add(ring.X(i), ring.Y(i), ring.Z(i), West(ring.X(i), ring.Z(i)) ? Mat.Wood : ring.M(i));
        sim.DesignateBuild(laid);
        var ditch = Shapes.Circle(w, s, kx, kz, s.KeepR + 84, new Stroke { Width = 18, Height = 20, Dig = true, Hollow = true }, 0);
        sim.DesignateRemove(ditch);
        sim.InstantLabour = false;
        // the bottom of the ditch on the west poured with pitch
        var floor = new HashSet<(int, int)>();
        for (int i = 0; i < ditch.Count; i++) if (West(ditch.X(i), ditch.Z(i))) floor.Add((ditch.X(i), ditch.Z(i)));
        foreach (var (x, z) in floor) { int y = w.SurfAt(x, z) - 1; if (y > 0) w.Set(x, y, z, Mat.Pitch); }
        var archers = new List<Unit>();
        foreach (var (dx, dz) in new[] { (-62, -14), (-60, 18), (-52, -36), (-48, 40), (-63, 2) })
        {
            int c = sim.NearestStand(kx + dx, kz + dz, 4);
            var a = sim.Spawn(Kinds.Archer, c);
            sim.Command(new[] { a }, c);
            a.FireArrows = true;
            archers.Add(a);
        }
        Advance(2);
        sim.Waves.Countdown = 0;
        sim.Step(.05f);
        sim.Waves.Countdown = 1e9;
        sim.HordeStock[Stockpile.Wood] = 24 * VoxelWorld.PerM3;
        foreach (var (t, dx, dz, torch) in new[] { (Kinds.Goblin, -128, -6, true), (Kinds.Goblin, -132, 20, true), (Kinds.Orc, -140, 4, true), (Kinds.Sapper, -122, -24, false),
            (Kinds.Sapper, -120, 30, false), (Kinds.Orc, -146, -16, false), (Kinds.Goblin, -126, 40, true) })
        {
            int c = sim.NearestStand(kx + dx, kz + dz, t.Move.Height);
            if (c >= 0) sim.Spawn(t, c).Torch = torch;
        }
        // the nearest tree outside the ditch, its crown alight
        (int x, int z) tree = (-1, -1);
        for (int r = 130; r < 400 && tree.x < 0; r += 6)
            for (int a = 0; a < 360 && tree.x < 0; a += 4)
            {
                int x = kx + (int)(MathF.Cos(a * MathF.PI / 180) * r), z = kz + (int)(MathF.Sin(a * MathF.PI / 180) * r);
                if (x > 10 && z > 10 && x < w.SX - 10 && z < w.SZ - 10 && x < kx && Mat.IsTrunk(w.Get(x, w.SurfAt(x, z) + 2, z))) tree = (x, z);
            }
        if (tree.x >= 0)
        {
            int g = w.SurfAt(tree.x, tree.z), lit = 0;
            for (int y = g; y < g + 90 && lit < 6; y++)
                for (int dz = -10; dz <= 10 && lit < 6; dz += 2)
                    for (int dx = -10; dx <= 10 && lit < 6; dx += 2)
                        if (Mat.IsLeafy(w.Get(tree.x + dx, y, tree.z + dz)) && sim.Ignite(tree.x + dx, y, tree.z + dz)) lit++;
            GD.Print($"[fire] a tree alight at {tree.x},{tree.z}: {lit} voxels of its crown");
        }
        sim.ComputeFields();
        if (OS.GetEnvironment("FIRE_DEBUG") != "")
            for (double t = 0; t < _poseSeconds; t += 5)
            {
                Advance(5);
                int wood = 0, pitch = 0;
                foreach (int p in sim.Burning.Keys) { var (x, y, z) = WaterPlanner.Unpack(p); byte m = w.Get(x, y, z); if (m == Mat.Wood) wood++; else if (m == Mat.Pitch) pitch++; }
                GD.Print($"[fire] {t + 5:0} s: {sim.Burning.Count} burning, {wood} logs, {pitch} pitch; " +
                    string.Join(", ", sim.Units.FindAll(u => !u.Dead && u.Team == Team.Player).ConvertAll(u => $"{u.T.Name} {u.State} fire {u.FireCell}")));
            }
        else Advance(_poseSeconds);
        Rig.Set(World.WX(kx - 90), World.WZ(kz + 4), -1.0f, .62f, 1.15f, snap: true);
        Hud.Refresh();
        Marks.Flush();
        foreach (var u in archers) Selected.Add(u);
    }

    /// <summary>A ring of stone round the citadel with archers on it, and miners coming at it to dig the ground out from under it.</summary>
    void Sapping()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.Stock.Gold = 90;
        int kx = s.KeepX, kz = s.KeepZ;
        sim.InstantLabour = true;
        int top = Shapes.LevelTop(w, kx - 64, kz, 40);
        sim.DesignateBuild(Shapes.Circle(w, s, kx, kz, s.KeepR + 44, new Stroke { Mat = Mat.Stone, Width = 10, Height = 40, Level = true, Hollow = true }, top));
        sim.InstantLabour = false;
        var archers = new List<Unit>();
        foreach (var (dx, dz) in new[] { (-62, -30), (-52, 40) })
        {
            int c = sim.NearestStand(kx + dx, kz + dz, 4);
            var a = sim.Spawn(Kinds.Archer, c);
            sim.Command(new[] { a }, c);
            archers.Add(a);
        }
        Advance(2);
        sim.Waves.Countdown = 0;
        sim.Step(.05f);
        sim.Waves.Countdown = 1e9;
        sim.HordeStock[Stockpile.Wood] = sim.HordeStock[Stockpile.Earth] = 0;
        foreach (var (t, dx, dz) in new[] { (Kinds.Miner, -110, -6), (Kinds.Miner, -116, 22), (Kinds.Goblin, -124, 6), (Kinds.Orc, -130, -14) })
        {
            int c = sim.NearestStand(kx + dx, kz + dz, t.Move.Height);
            if (c >= 0) sim.Spawn(t, c);
        }
        sim.ComputeFields();
        Advance(_poseSeconds);
        Rig.Set(World.WX(kx - 80), World.WZ(kz + 6), -1.1f, .62f, 1.3f, snap: true);
        Hud.Refresh();
        Marks.Flush();
    }

    /// <summary>
    /// The alarm out in the valley: a palisade yard south-east of the citadel set aside as a shelter, the workers who were
    /// about it standing in it, a place each; the rest of ours gone into the citadel.
    /// </summary>
    /// <summary>Priests at prayer on the barrow, in a sanctuary set aside round it, in the evening light; the place is ours.</summary>
    void Faith()
    {
        var sim = Sim;
        sim.Waves.Countdown = 100;
        var barrow = sim.Mines.Find(m => m.Power && m.Name == "Курган");
        var p = barrow.Place;
        sim.AddZone(p.X - 40, p.Z - 40, p.X + 40, p.Z + 40, Zone.Sanctum, 1);
        barrow.Holder = Holder.Ours; barrow.Grip = 1;
        foreach (var (dx, dz) in new[] { (30, 10), (-20, 34), (10, -36), (-36, -10), (40, -24) })
            sim.Spawn(Kinds.Priest, sim.NearestStand(p.X + dx, p.Z + dz, 2));
        sim.Favour = 23;
        Advance(_poseSeconds + 6);
        Rig.SetAt(World.WX(p.X + 6), World.WY(World.SurfAt(p.X, p.Z) - 6), World.WZ(p.Z + 10), -.62f, .56f, _look == 1 ? 3.2f : 1.6f);
        Hud.Refresh();
        Marks.Flush();
    }

    /// <summary>
    /// The captives: on the barrow a priest at the altar with a goblin on its knees before it, a worker leading a bound
    /// orc toward the citadel, another orc lying bound in the grass; the place is ours, and two more priests pray.
    /// </summary>
    void Captives()
    {
        var sim = Sim;
        sim.Waves.Countdown = 100;
        var barrow = sim.Mines.Find(m => m.Power && m.Name == "Курган");
        var p = barrow.Place;
        sim.AddZone(p.X - 40, p.Z - 40, p.X + 40, p.Z + 40, Zone.Sanctum, 1);
        barrow.Holder = Holder.Ours; barrow.Grip = 1;
        foreach (var (dx, dz) in new[] { (30, 10), (-20, 34), (10, -36) })
            sim.Spawn(Kinds.Priest, sim.NearestStand(p.X + dx, p.Z + dz, 2));
        var prisoner = sim.Spawn(Kinds.Goblin, sim.Gate);
        prisoner.Bound = true; prisoner.Inside = true;
        Advance(4);
        sim.Sacrifice(RiteKind.Prisoner);
        for (int t = 0; t < 400 && sim.RiteNow is not { Stage: 2, Done: > 2.5f }; t++) Advance(.25);
        var r = sim.RiteNow;
        var at = r?.Victim ?? prisoner;
        // bound in the grass below, and one being led in past it
        var lying = sim.Spawn(Kinds.Orc, sim.NearestStand((int)at.X + 20, (int)at.Z + 18, 2));
        lying.Bound = true; lying.BoundAt = sim.Time; lying.Pastime = Pastime.Bound;
        var led = sim.Spawn(Kinds.Orc, sim.NearestStand((int)at.X + 30, (int)at.Z - 4, 2));
        led.Bound = true; led.BoundAt = sim.Time;
        var porter = sim.Spawn(Kinds.Worker, sim.NearestStand((int)at.X + 34, (int)at.Z, 2));
        porter.Captive = led; led.Captor = porter;
        Advance(1.5);
        Rig.SetAt(World.WX(at.X + 12), World.WY(at.Y + 4), World.WZ(at.Z + 6), -.62f, .52f, _look == 1 ? 3.4f : 2.2f);
        Hud.Refresh();
        Marks.Flush();
    }

    /// <summary>
    /// The spells at work, at night under the red moon: a band coming on west of the citadel, in a bog, chains on some of
    /// it, burning pitch coming down on the rest, the dead got up among them, the Eye open; with --look=1 a bolt of
    /// lightning striking a troll, close.
    /// </summary>
    void Spellwork()
    {
        var sim = Sim; var w = World;
        foreach (var s in Sim.Spells) sim.Known.Add(s.Kind);
        sim.Favour = 600;
        sim.Waves.StartNow(sim);
        sim.Waves._queue.Clear();
        foreach (var u in sim.Units) if (u.Team == Team.Horde && !u.Dead && !u.Guard) { u.Dead = true; u.DiedAt = -999; }
        int cx = Site.KeepX - Site.KeepR - 150, cz = Site.KeepZ + 10;
        Unit Raider(UnitType t, int dx, int dz) { var u = sim.Spawn(t, sim.NearestStand(cx + dx, cz + dz, t.Move.Height)); u.Guard = true; u.Home = u.Cell; return u; }
        // one raider far off in the west keeps the night going: the wave is not over while it is in the valley
        var straggler = sim.Spawn(Kinds.Goblin, sim.NearestStand(Site.SpawnStrip + 30, Site.KeepZ, 2));
        straggler.ChainedUntil = 1e9;
        if (_look == 1)
        {
            var troll = Raider(Kinds.Troll, 0, 0);
            Raider(Kinds.Goblin, -16, 12); Raider(Kinds.Goblin, -10, -14);
            Advance(.5);
            sim.Cast(SpellKind.BloodMoon, cx, 0, cz);
            Advance(.2);
            Rig.SetAt(w.WX(troll.X), w.WY(troll.Y + 12), w.WZ(troll.Z), -.62f, .5f, 2.6f);
            sim.Cast(SpellKind.Thunder, troll.X, troll.Y, troll.Z);
            _shotFrames = Math.Min(_shotFrames, 4);
            Hud.Refresh();
            return;
        }
        // the dead to raise
        for (int i = 0; i < 3; i++) { var d = Raider(Kinds.Goblin, 30 + i * 9, -30); sim.Hurt(d, null, 1000, "test"); }
        var band = new List<Unit>();
        foreach (var (t, dx, dz) in new[] { (Kinds.Orc, -20, 0), (Kinds.Goblin, -10, 14), (Kinds.Goblin, -6, -12), (Kinds.Troll, 6, 4), (Kinds.Goblin, 14, 18),
            (Kinds.Orc, 20, -8), (Kinds.Goblin, 28, 10), (Kinds.Sapper, -26, 20), (Kinds.Goblin, 36, -20), (Kinds.Orc, 44, 6) })
            band.Add(Raider(t, dx, dz));
        Advance(.5);
        sim.Cast(SpellKind.BloodMoon, cx, 0, cz);
        sim.Cast(SpellKind.Mire, cx - 10, w.SurfAt(cx - 10, cz), cz + 4);
        sim.Cast(SpellKind.Chains, cx + 24, w.SurfAt(cx + 24, cz), cz);
        sim.Cast(SpellKind.Raise, cx + 40, w.SurfAt(cx + 40, cz - 30), cz - 30);
        sim.Cast(SpellKind.Eye, cx, 0, cz);
        Advance(1.2);
        sim.Cast(SpellKind.FireRain, cx - 12, w.SurfAt(cx - 12, cz), cz + 6);
        Advance(2.2);
        Rig.SetAt(w.WX(cx + 8), w.WY(w.SurfAt(cx, cz) + 4), w.WZ(cz), -.62f, .55f, 1.5f);
        Hud.Refresh();
    }

    /// <summary>
    /// The greatest spells, each on a band west of the citadel, the game stopped a moment after the casting to see it:
    /// the earthquake (the raiders thrown up, their ladder coming apart), with --look=1 the falling star coming down on
    /// them, with --look=2 the whirlwind among them.
    /// </summary>
    void Wonders()
    {
        var sim = Sim; var w = World;
        Sorcerous();
        int cx = Site.KeepX - Site.KeepR - 150, cz = Site.KeepZ + 10;
        WonderBand(cx, cz);
        Advance(.5);
        float ground = w.SurfAt(cx, cz);
        if (_look == 1)
        {
            sim.Cast(SpellKind.Meteor, cx, ground, cz);
            Advance(1.05);
        }
        else if (_look == 2)
        {
            sim.Cast(SpellKind.Whirlwind, cx - 60, w.SurfAt(cx - 60, cz), cz);
            Advance(3.2);
        }
        else
        {
            sim.Cast(SpellKind.Quake, cx, ground, cz);
            Advance(.45);
        }
        Paused = true;
        Rig.SetAt(w.WX(cx), w.WY(ground + 8), w.WZ(cz), -.62f, .5f, 1.7f);
        Hud.Refresh();
    }

    /// <summary>A band of the horde standing on guard round a spot in the field, a siege ladder of theirs standing by it and a deck laid beside: what the great spells are shown on.</summary>
    void WonderBand(int cx, int cz)
    {
        var w = World;
        foreach (var (t, dx, dz) in new[] { (Kinds.Orc, -20, 0), (Kinds.Goblin, -10, 14), (Kinds.Goblin, -6, -12), (Kinds.Troll, 6, 4), (Kinds.Goblin, 14, 18),
            (Kinds.Orc, 20, -8), (Kinds.Goblin, 28, 10), (Kinds.Sapper, -26, 20), (Kinds.Goblin, 36, -20), (Kinds.Orc, 2, -26) })
            Camp(t, cx + dx, cz + dz);
        // a siege ladder of theirs standing in the field, a deck laid by it
        int lg = w.SurfAt(cx + 8, cz - 40);
        for (int y = lg; y < lg + 28; y++) for (int x = cx + 6; x < cx + 9; x++) w.Set(x, y, cz - 40, Mat.SiegeLadder);
        for (int z = cz + 30; z < cz + 46; z++) for (int x = cx - 10; x < cx + 10; x++) w.Set(x, w.SurfAt(x, z), z, Mat.Deck);
    }

    /// <summary>
    /// A meadow, the most flowered and tufted patch of open turf near the citadel: --look=0 as the view first opens on
    /// it, 1 closer, where the sward grows in, 2 close, 3 as close as the view goes; 4 through a swordsman's eyes, a band
    /// wading through the grass at him; 5 a keg's blast in it, its wave running out; 6 a whirlwind crossing it; 7 and 8
    /// the blast and the whirlwind as close as the view goes.
    /// </summary>
    void Meadow()
    {
        Sim.Waves.Countdown = 1e9;
        var (cx, cz) = MeadowAt();
        GD.Print($"[voxel-tower] the meadow at {cx},{cz}");
        MeadowShot(cx, cz);
    }

    /// <summary>The most flowered and tufted patch of open turf near the citadel, out of its walls' way (voxels).</summary>
    (int x, int z) MeadowAt()
    {
        var w = World;
        int bx = Site.KeepX, bz = Site.KeepZ, best = -1, cx = bx - 200, cz = bz;
        for (int z = Math.Max(40, bz - 420); z < Math.Min(w.SZ - 40, bz + 420); z += 16)
            for (int x = Math.Max(Site.SpawnStrip + 40, bx - 420); x < Math.Min(w.SX - 40, bx + 420); x += 16)
            {
                if ((x - bx) * (x - bx) + (z - bz) * (z - bz) < (Site.KeepR + 60) * (Site.KeepR + 60)) continue;
                int score = 0, h0 = w.SurfAt(x, z);
                for (int dz = -24; dz <= 24; dz += 2)
                    for (int dx = -24; dx <= 24; dx += 2)
                    {
                        int s = w.SurfAt(x + dx, z + dz);
                        byte top = w.Get(x + dx, s, z + dz), under = w.Get(x + dx, s - 1, z + dz);
                        if (Math.Abs(s - h0) > 6 || !Mat.IsGrass(under)) { score -= 3; continue; }
                        if (Mat.IsSprig(top)) score += top == Mat.Tuft ? 4 : 6;
                        else if (top != Mat.Air) score -= 4;
                        else score++;
                    }
                if (score > best) { best = score; cx = x; cz = z; }
            }
        return (cx, cz);
    }

    /// <summary>
    /// The camera on the meadow, from the usual view to as close as it goes; or a swordsman there, a band wading at him,
    /// gone into; or a blast or a whirlwind in the grass.
    /// </summary>
    void MeadowShot(int cx, int cz)
    {
        var sim = Sim; var w = World;
        float ground = w.SurfAt(cx, cz);
        if (_look == 4)
        {
            var me = sim.Spawn(Kinds.Swordsman, sim.NearestStand(cx + 30, cz, 2));
            foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, -2, 3), (Kinds.Orc, -12, -9), (Kinds.Goblin, -16, 12), (Kinds.Goblin, -26, -2) })
            {
                var u = sim.Spawn(t, sim.NearestStand(cx + dx, cz + dz, t.Move.Height));
                u.Guard = true; u.Home = u.Cell;
            }
            Advance(.3);
            FP.Enter(me);
            FP.Face(MathF.PI, -.2f);
            Paused = true;
            Hud.Refresh();
            return;
        }
        if (_look is 5 or 7) { sim.Detonate(cx, ground, cz, 9, 0, "", always: true); Advance(.3); Paused = true; }
        if (_look is 6 or 8)
        {
            foreach (var s in Sim.Spells) sim.Known.Add(s.Kind);
            sim.Favour = 600;
            sim.Cast(SpellKind.Whirlwind, cx - 30, w.SurfAt(cx - 30, cz), cz);
            Advance(2.2);
            Paused = true;
        }
        float zoom = _look switch { 1 => 2.6f, 2 => 4.2f, 3 or 7 or 8 => 8f, 5 or 6 => 4.5f, _ => 1f };
        Rig.SetAt(w.WX(cx), w.WY(ground), w.WZ(cz), -.62f, .5f, zoom);
        Hud.Refresh();
    }

    /// <summary>
    /// First person: one of ours gone into on the field west of the citadel, a band coming at it — a swordsman with his
    /// sword up; with --look=1 an archer drawing on a goblin; with --look=2 a worker with the pick at the ground; with
    /// --look=3 the archer turned from the band, drawing for a shot at the sky.
    /// </summary>
    void Eyes()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9;
        int cx = Site.KeepX - Site.KeepR - 150, cz = Site.KeepZ + 10;
        Unit Raider(UnitType t, int dx, int dz) { var u = sim.Spawn(t, sim.NearestStand(cx + dx, cz + dz, t.Move.Height)); u.Guard = true; u.Home = u.Cell; return u; }
        var kind = _look is 1 or 3 ? Kinds.Archer : _look == 2 ? Kinds.Worker : Kinds.Swordsman;
        var me = sim.Spawn(kind, sim.NearestStand(cx + 40, cz, 2));
        foreach (var (t, dx, dz) in _look is 1 or 3
            ? new[] { (Kinds.Goblin, -90, 6), (Kinds.Orc, -110, -20), (Kinds.Goblin, -120, 24), (Kinds.Troll, -150, 0) }
            : new[] { (Kinds.Goblin, 26, 4), (Kinds.Orc, 12, -12), (Kinds.Goblin, 6, 16), (Kinds.Troll, -14, 2), (Kinds.Goblin, -4, -26) })
            Raider(t, dx, dz);
        Advance(.3);
        FP.Enter(me);
        var aim = sim.Units.Find(u => u.Team == Team.Horde && !u.Dead && u.T == Kinds.Goblin);
        float tdx = aim.X - me.X, tdz = aim.Z - me.Z;
        // the swordsman looks the goblin in the face, which is well below his own
        float face = MathF.Atan2(aim.Y + aim.T.BodyH * .75f - (me.Y + me.T.BodyH * .88f), MathF.Sqrt(tdx * tdx + tdz * tdz));
        FP.Face(MathF.Atan2(tdz, tdx), _look == 2 ? -.62f : _look == 1 ? .02f : Math.Clamp(face, -.6f, .05f));
        if (_look == 3) { FP.Face(MathF.Atan2(tdz, tdx) + MathF.PI, .25f); sim.WishStrike = true; Advance(.2); }
        else if (_look == 2) { sim.WishStrike = true; Advance(1.2); }
        else if (_look == 1) { sim.WishStrike = true; Advance(.35); sim.WishStrike = false; Advance(.08); }
        else { sim.WishStrike = true; Advance(.2); }
        Paused = true;
        Hud.Refresh();
    }

    /// <summary>
    /// The sky, to be looked at: --look=0 noon, 1 the sun going down, 2 night under the moon, 3 rain, 4 the blood moon,
    /// 5 daybreak, 6 a night of rain with sheet lightning in it; the diorama zoomed out towards the sun or the moon so its
    /// backdrop shows round the valley. The same with ten added (10–16) from the eyes of a swordsman of ours on a lookout
    /// on the rim of the plateau, looking out over the valley to the far land and the mountains; 17 at noon from the
    /// valley's western edge on the old road, where the valley goes over into the far land. The clock is stopped.
    /// </summary>
    void Skies()
    {
        var sim = Sim; var w = World;
        int mood = _look == 17 ? 0 : Math.Max(0, _look) % 10;
        bool eyes = _look >= 10;
        sim.Waves.Countdown = Waves.Prep * .5;
        if (mood == 1) sim.Waves.Countdown = 33;
        if (mood == 3) { sim.Waves.Countdown = Waves.Prep * .4; sim.StartWeather(Weather.Rain, 900); }
        if (mood == 5) { sim.Waves.Countdown = Waves.Prep; sim.Waves.DawnLeft = Waves.Dawn * .25; }
        if (mood is 2 or 4 or 6) Nightfall();
        if (mood == 4) sim.BloodMoonUntil = 1e9;
        if (mood == 6) sim.StartWeather(Weather.Rain, 900);
        // which way to look: west into the sunset, east into the daybreak, south-west to the moon
        float toward = mood switch { 1 or 3 => MathF.PI * .87f, 5 => .62f, 2 or 4 => 2.27f, 6 => 2.6f, _ => MathF.PI * .8f };
        Paused = true;
        if (!eyes)
        {
            // the low sun off to one side, where the block of the valley stands lowest before the backdrop
            float look = toward + (mood == 1 ? .65f : mood == 5 ? -1.02f : 0);
            Rig.Set(MathF.Cos(look) * 14, MathF.Sin(look) * 14, -look - MathF.PI / 2, .42f, .28f, snap: true);
            if (mood == 6) Atmos.Flash(new Vector3(-.6f, .3f, .5f), 1);
            Hud.Refresh();
            return;
        }
        if (_look == 17)
        {
            // on the old road at the western edge, where the horde comes in, looking out along it
            int rz = (int)(w.SZ * .56f);
            var walker = sim.Spawn(Kinds.Swordsman, sim.NearestStand(30, rz, 2));
            FP.Enter(walker);
            FP.Face(MathF.PI - .12f, -.04f);
            Hud.Refresh();
            return;
        }
        // a stone lookout eight metres tall on the plateau's rim west of the citadel, over the crowns of the trees
        int z = Site.KeepZ - 130, x = Site.KeepX;
        while (x > 40 && Site.HTop[z * w.SX + x - 6] > Site.KeepBase - 30) x -= 2;
        x += 12;
        sim.InstantLabour = true;
        sim.DesignateBuild(Shapes.Circle(w, Site, x, z, 7, new Stroke { Mat = Mat.Stone, Width = 7, Height = 64, Level = true }, Shapes.LevelTop(w, x, z, 64)));
        sim.InstantLabour = false;
        var me = sim.Spawn(Kinds.Swordsman, sim.NearestStand(x, z, 2));
        FP.Enter(me);
        FP.Face(toward, mood is 2 or 4 ? .42f : mood == 6 ? .16f : .05f);
        if (mood == 6) Atmos.Flash(new Vector3(MathF.Cos(toward + .4f), .3f, MathF.Sin(toward + .4f)), 1);
        GD.Print($"[pose] the sky from the eyes of a swordsman at {me.X:0},{me.Z:0}, height {me.Y:0}");
        Hud.Refresh();
    }

    /// <summary>Every spell known, intervention to spare, and the clock of the waves stopped.</summary>
    void Sorcerous()
    {
        foreach (var s in Sim.Spells) Sim.Known.Add(s.Kind);
        Sim.Favour = 600;
        Sim.Waves.Countdown = 1e9;
    }

    /// <summary>Night, with no wave coming in: one raider far off in the west, held fast, keeps it going.</summary>
    void Nightfall()
    {
        var sim = Sim;
        sim.Waves.StartNow(sim);
        sim.Waves._queue.Clear();
        foreach (var u in sim.Units) if (u.Team == Team.Horde && !u.Dead && !u.Guard) { u.Dead = true; u.DiedAt = -999; }
        var straggler = sim.Spawn(Kinds.Goblin, sim.NearestStand(Site.SpawnStrip + 30, Site.KeepZ, 2));
        straggler.ChainedUntil = 1e9;
    }

    /// <summary>A wall of ours a little west of the citadel, its top; and a raider standing on guard by it.</summary>
    int WallWest(int dx, int height, int halfLength)
    {
        int wx = Site.KeepX - Site.KeepR + dx, kz = Site.KeepZ;
        int top = Shapes.LevelTop(World, wx, kz, height);
        Sim.InstantLabour = true;
        Sim.DesignateBuild(Shapes.Line(World, Site, wx, kz - halfLength, wx, kz + halfLength, new Stroke { Mat = Mat.Stone, Width = 8, Height = height, Level = true }, top));
        Sim.InstantLabour = false;
        return top;
    }

    Unit Camp(UnitType t, int x, int z) { var u = Sim.Spawn(t, Sim.NearestStand(x, z, t.Move.Height)); u.Guard = true; u.Home = u.Cell; return u; }

    /// <summary>A wall of ours west of the citadel blown up before a band of the horde: its pieces in the air.</summary>
    void Blasting()
    {
        var sim = Sim; var w = World;
        Sorcerous();
        int wx = Site.KeepX - Site.KeepR - 110, kz = Site.KeepZ, top = WallWest(-110, 40, 56);
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, -22, -10), (Kinds.Orc, -26, 10), (Kinds.Goblin, -18, 22), (Kinds.Goblin, -30, -26), (Kinds.Troll, -44, 2), (Kinds.Orc, -40, -18) })
            Camp(t, wx + dx, kz + dz);
        Advance(.3);
        sim.Cast(SpellKind.Explosion, wx, top - 22, kz);
        Advance(_look == 2 ? 10 : _look == 1 ? .9 : .3);
        Paused = true;
        Rig.SetAt(w.WX(wx - 24), w.WY(top - 18), w.WZ(kz), -.75f, .45f, _look == 2 ? 2.2f : 2.9f);
        Hud.Refresh();
    }

    /// <summary>A ball of earth torn out of the ground by telekinesis and held over a band of the horde, the hole it left beside them.</summary>
    void Gripping()
    {
        var sim = Sim; var w = World;
        Sorcerous();
        int cx = Site.KeepX - Site.KeepR - 120, cz = Site.KeepZ + 10;
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, -8, -10), (Kinds.Orc, 4, 6), (Kinds.Goblin, 12, -4), (Kinds.Goblin, -14, 12), (Kinds.Orc, 10, 18), (Kinds.Sapper, -4, 22) })
            Camp(t, cx + dx, cz + dz);
        int bx = cx + 40, bz = cz - 30, g = w.SurfAt(bx, bz);
        sim.Cast(SpellKind.Telekinesis, bx + .5f, g - 3, bz + .5f);
        sim.HoldAt(cx, w.SurfAt(cx, cz) + 48, cz + 4);
        Advance(1.5);
        // over the band where it stands now
        float mx = 0, mz = 0; int n = 0;
        foreach (var u in sim.Units) if (!u.Dead && u.Team == Team.Horde && Math.Abs(u.X - cx) < 60 && Math.Abs(u.Z - cz) < 60) { mx += u.X; mz += u.Z; n++; }
        if (n > 0) sim.HoldAt(mx / n, w.SurfAt((int)(mx / n), (int)(mz / n)) + 48, mz / n);
        Advance(1.2);
        Paused = true;
        Rig.SetAt(w.WX(cx + 14), w.WY(w.SurfAt(cx, cz) + 18), w.WZ(cz - 6), -.75f, .42f, 2.6f);
        Hud.Refresh();
    }

    /// <summary>The black wind over our wall: the band on its ladders and at its foot thrown off through the air, the ladders torn away in pieces.</summary>
    void Gale()
    {
        var sim = Sim; var w = World;
        Sorcerous();
        int wx = Site.KeepX - Site.KeepR - 100, kz = Site.KeepZ, top = WallWest(-100, 40, 48);
        // two ladders against the outer face
        foreach (int lz in new[] { kz - 18, kz + 14 })
            for (int z = lz; z < lz + 6; z++)
            {
                int g = w.SurfAt(wx - 6, z);
                for (int y = g; y < top + 2; y++) if (w.Get(wx - 5, y, z) == Mat.Air) w.Set(wx - 5, y, z, Mat.SiegeLadder);
            }
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, -12, -14), (Kinds.Orc, -16, 4), (Kinds.Goblin, -14, 18), (Kinds.Goblin, -24, -4), (Kinds.Orc, -22, 24), (Kinds.Goblin, -30, 10) })
            Camp(t, wx + dx, kz + dz);
        Advance(.5);
        sim.Cast(SpellKind.BlackWind, wx - 14, w.SurfAt(wx - 14, kz), kz);
        Advance(.55);
        Paused = true;
        Rig.SetAt(w.WX(wx - 40), w.WY(top - 10), w.WZ(kz), -.75f, .4f, 3.6f);
        Hud.Refresh();
    }

    /// <summary>At night: a band in the fog of the dead, the plague going through it, our swordsmen under the blood oath by a wall with a stone skin on it.</summary>
    void Hexes()
    {
        var sim = Sim; var w = World;
        Sorcerous();
        Nightfall();
        int wx = Site.KeepX - Site.KeepR - 90, kz = Site.KeepZ, top = WallWest(-90, 32, 44);
        sim.Decorate();
        var band = new List<Unit>();
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, -40, -12), (Kinds.Orc, -46, 6), (Kinds.Goblin, -36, 18), (Kinds.Goblin, -54, -20), (Kinds.Orc, -58, 14), (Kinds.Goblin, -50, 30), (Kinds.Troll, -64, -2) })
            band.Add(Camp(t, wx + dx, kz + dz));
        foreach (var (dx, dz) in new[] { (12, -10), (14, 8), (12, 22) }) { var s = Camp(Kinds.Swordsman, wx + dx, kz + dz); s.Hp = s.MaxHp * .5f; }
        Advance(.5);
        sim.Cast(SpellKind.DeadFog, wx - 48, w.SurfAt(wx - 48, kz), kz + 4);
        sim.Cast(SpellKind.Plague, wx - 40, w.SurfAt(wx - 40, kz), kz);
        sim.Cast(SpellKind.Oath, wx + 13, w.SurfAt(wx + 13, kz), kz + 8);
        sim.Cast(SpellKind.Skin, wx, top - 12, kz);
        Advance(3);
        Rig.SetAt(w.WX(wx - 22), w.WY(top - 18), w.WZ(kz + 4), -.75f, .5f, 2.4f);
        Hud.Refresh();
    }

    /// <summary>At night: a witch of the horde by our wall with its torches put out, and a necromancer getting the fallen up as thralls.</summary>
    void Sorcerers()
    {
        var sim = Sim; var w = World;
        Sorcerous();
        Nightfall();
        // a long wall: the torches at its far end out of the witch's reach still burn
        int wx = Site.KeepX - Site.KeepR - 90, kz = Site.KeepZ, top = WallWest(-90, 32, 150);
        sim.Decorate();
        var witch = Camp(Kinds.Witch, wx - 30, kz - 110);
        for (int t = 0; t < 60 && !sim.Fixtures.Exists(f => f.DarkUntil > sim.Time); t++) Advance(.25);
        var necro = Camp(Kinds.Necromancer, wx - 44, kz - 60);
        foreach (var (dx, dz) in new[] { (8, -6), (12, 8), (-4, 12) }) { var d = Camp(Kinds.Goblin, wx - 44 + dx, kz - 60 + dz); sim.Hurt(d, null, 1000, "test"); }
        Camp(Kinds.Goblin, wx - 28, kz - 84); Camp(Kinds.Orc, wx - 36, kz - 90);
        for (int t = 0; t < 120 && sim.Count(Kinds.Thrall) == 0; t++) Advance(.25);
        Advance(1.2);
        // from inside the wall along it: its far end lit, the near one dark, the witch and the necromancer beyond it
        Rig.SetAt(w.WX(wx - 20), w.WY(top - 24), w.WZ(kz - 40), _look == 1 ? -.75f : 2.39f, .5f, 2.6f);
        Hud.Refresh();
    }

    /// <summary>Kegs of powder stacked on the ground: a cube this many voxels a side (a box of x by z, as high as the side, given them).</summary>
    void Stack(int x0, int z0, int size, int sx = 0, int sz = 0)
    {
        for (int z = z0; z < z0 + (sz > 0 ? sz : size); z++)
            for (int x = x0; x < x0 + (sx > 0 ? sx : size); x++)
                for (int y = World.SurfAt(x, z), y1 = y + size; y < y1; y++) if (World.Get(x, y, z) == Mat.Air) World.Set(x, y, z, Mat.Powder);
    }

    /// <summary>Kegs of powder before a wall of ours going up one after another under a band of the horde (--look=1: a trail of it burning across the field).</summary>
    void Kegs()
    {
        var sim = Sim; var w = World;
        Sorcerous();
        int wx = Site.KeepX - Site.KeepR - 100, kz = Site.KeepZ, top = WallWest(-100, 32, 64);
        if (_look == 1)
        {
            // a trail of powder across the way, a voxel wide, lit at its south end
            Stack(wx - 40, kz - 60, 1, sx: 1, sz: 120);
            foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, -44, -20), (Kinds.Orc, -36, 4), (Kinds.Goblin, -46, 26), (Kinds.Goblin, -38, 44), (Kinds.Orc, -48, -40) })
                Camp(t, wx + dx, kz + dz);
            Advance(.3);
            // the top of the powder there
            int g = w.SurfAt(wx - 40, kz - 60) - 1;
            sim.Ignite(wx - 40, g, kz - 60);
            Advance(1.6);
            Paused = true;
            Rig.SetAt(w.WX(wx - 40), w.WY(g + 4), w.WZ(kz), -.75f, .45f, 2.2f);
            Hud.Refresh();
            return;
        }
        var (sx, sy, sz) = KegsBefore(wx, kz, 26, 3);
        Advance(.3);
        sim.Ignite(sx, sy, sz);
        Advance(1.25);
        Paused = true;
        Rig.SetAt(w.WX(wx - 30), w.WY(top - 22), w.WZ(kz), -.75f, .45f, 2.4f);
        Hud.Refresh();
    }

    /// <summary>
    /// Four stacks of kegs before a wall of ours (this many voxels out from its line, this many a side) and a band among
    /// them, nothing lit yet: the keg of the first stack to light, which sets the rest off one after another.
    /// </summary>
    (int x, int y, int z) KegsBefore(int wx, int kz, int before, int size)
    {
        foreach (int dz in new[] { -36, -12, 12, 36 }) Stack(wx - before, kz + dz, size);
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, -32, -24), (Kinds.Orc, -36, 0), (Kinds.Goblin, -30, 22), (Kinds.Goblin, -40, 40), (Kinds.Troll, -44, -6), (Kinds.Orc, -34, -44) })
            Camp(t, wx + dx - before + 26, kz + dz);
        return (wx - before + 1, World.SurfAt(wx - before + 1, kz - 35) - 1, kz - 35);
    }

    /// <summary>A sapper's charge against our ring of stone round the citadel, its fuse burning, the sapper running (--look=1: the moment it goes; --look=2: a swordsman of ours running for the fuse).</summary>
    void Sapped()
    {
        var sim = Sim; var w = World;
        Sorcerous();
        int r = Site.KeepR + 44;
        sim.InstantLabour = true;
        sim.DesignateBuild(Shapes.Circle(w, Site, Site.KeepX, Site.KeepZ, r, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Hollow = true }, 0));
        sim.InstantLabour = false;
        sim.HordeStock[Stockpile.Wood] = sim.HordeStock[Stockpile.Earth] = 0;
        sim.ComputeFields();
        var sap = sim.Spawn(Kinds.Sapper, sim.NearestStand(Site.KeepX - r - 60, Site.KeepZ, 2));
        sap.Charge = true;
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, -80, -16), (Kinds.Orc, -88, 10), (Kinds.Goblin, -84, 30) }) Camp(t, Site.KeepX - r + dx, Site.KeepZ + dz);
        Charge laid = null;
        for (int t = 0; t < 240 && laid == null; t++) { Advance(.25); laid = sim.Charges.Find(c => c.Planted); }
        if (laid == null) { Hud.Refresh(); return; }
        // --look=2: a swordsman of ours a few paces off runs for the fuse
        if (_look == 2) { sap.Dead = true; sap.DiedAt = -999; sim.Spawn(Kinds.Swordsman, sim.NearestStand((int)laid.X - 40, (int)laid.Z + 20, 2)); }
        Advance(_look == 1 ? 6.3 : _look == 2 ? 2.2 : 1.5);
        Paused = true;
        // from outside the ring: the kegs at the foot of its face, the sapper making off
        Rig.SetAt(w.WX(laid.X - 6), w.WY(laid.Y + 4), w.WZ(laid.Z), 2.39f, .42f, _look == 1 ? 2.2f : 3f);
        Hud.Refresh();
    }

    /// <summary>A long block of rock let fall on a narrow post with its weight off it, going over the post's edge (--look=1: after, on its end beside it).</summary>
    void Topple()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9;
        int px = Site.KeepX - Site.KeepR - 90, pz = Site.KeepZ, g = w.SurfAt(px, pz);
        for (int z = pz - 16; z < pz + 16; z++)
            for (int x = px - 16; x < px + 40; x++)
            {
                for (int y = g; y < g + 48; y++) w.Set(x, y, z, Mat.Air);
                for (int y = g - 3; y < g; y++) if (w.Get(x, y, z) == Mat.Air || Mat.IsVeg[w.Get(x, y, z)]) w.Set(x, y, z, Mat.Soil);
            }
        // a post of stone, a quarter of a metre square and a metre and a half high
        for (int z = pz; z < pz + 2; z++) for (int x = px; x < px + 2; x++) for (int y = g; y < g + 12; y++) w.Set(x, y, z, Mat.Stone);
        var slab = new List<(sbyte, sbyte, sbyte, byte)>();
        for (int dz = -3; dz < 3; dz++) for (int dy = -3; dy < 3; dy++) for (int dx = -8; dx < 8; dx++) slab.Add(((sbyte)dx, (sbyte)dy, (sbyte)dz, Mat.Rock));
        var block = sim.Hurl(slab, px + 7.5f, g + 26.5f, pz + 1, 0, 0, 0, "обвал");
        for (int t = 0; t < 80 && block != null && !block.Tipping; t++) Advance(.05);
        Advance(_look == 1 ? 3 : .38);
        Paused = true;
        Rig.SetAt(w.WX(px + 4), w.WY(g + 8), w.WZ(pz + 1), -.75f, .35f, 4.5f);
        Hud.Refresh();
    }

    /// <summary>
    /// A round stone tower of ours west of the citadel, eight metres high, a stretch of wall east of it; the horde's miners
    /// have dug the ground out from under the east two fifths of its foot (--at=seconds since, 1.6 by itself): it stands,
    /// cracking along that side; at about three seconds it goes over east, at five it comes down on the wall, and by ten
    /// it lies in rubble, the wall broken. --look=1 close on the cracks, 2 from the usual height of the view.
    /// </summary>
    void Undermine()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9;
        int tx = Site.KeepX - Site.KeepR - 80, tz = Site.KeepZ + 30, g = w.SurfAt(tx, tz);
        ClearTrees(tx - 60, tz - 70, tx + 140, tz + 160);
        for (int z = tz - 50; z < tz + 50; z++)
            for (int x = tx - 30; x < tx + 100; x++)
            {
                for (int y = g; y < g + 90; y++) if (w.Get(x, y, z) != Mat.Air) w.Set(x, y, z, Mat.Air);
                for (int y = g - 6; y < g; y++) if (!Mat.IsEarthy[w.Get(x, y, z)]) w.Set(x, y, z, Mat.Soil);
                w.Set(x, g - 1, z, Mat.Grass);
            }
        sim.InstantLabour = true;
        sim.DesignateBuild(Shapes.Circle(w, Site, tx, tz, 12, new Stroke { Mat = Mat.Stone, Width = 4, Height = 64, Level = true, Hollow = true }, g + 64));
        sim.DesignateBuild(Shapes.Line(w, Site, tx + 44, tz - 40, tx + 44, tz + 24, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Level = true }, g + 32));
        sim.InstantLabour = false;
        Advance(1);
        // the miners' hole under its east side, and the miners by it
        for (int z = tz - 14; z <= tz + 14; z++) for (int x = tx + 3; x <= tx + 16; x++) for (int y = g - 4; y < g; y++) w.Set(x, y, z, Mat.Air);
        foreach (var (dx, dz) in new[] { (18, -16), (22, 14) }) { var u = Camp(Kinds.Miner, tx + dx, tz + dz); u.Facing = MathF.PI; }
        Advance(_poseSeconds == 14 ? 1.6 : _poseSeconds);
        Paused = true;
        if (_look == 1) Rig.SetAt(w.WX(tx + 2), w.WY(g + 6), w.WZ(tz + 10), -.25f, .6f, 4.2f);
        // as the game is usually seen: from the home height of the view
        else if (_look == 2) Rig.SetAt(w.WX(tx + 24), w.WY(g + 10), w.WZ(tz + 4), .2f, .62f, 1f);
        else Rig.SetAt(w.WX(tx + 26), w.WY(g + 20), w.WZ(tz + 4), .2f, .42f, 2.1f);
        Hud.Refresh();
    }

    /// <summary>
    /// A stone wall of ours west of the citadel, three metres high, jarred at four places along it, harder and harder from
    /// south to north, as by blows of a catapult's stones: its cracks from the first hairlines to cracked all but through
    /// (--look=1 from the usual height of the view).
    /// </summary>
    void Cracked()
    {
        var w = World;
        Sim.Waves.Countdown = 1e9;
        int wx = Site.KeepX - Site.KeepR - 60, kz = Site.KeepZ, top = WallWest(-60, 24, 40);
        int face = wx - 8; while (face < wx + 8 && !Mat.IsMasonry(w.Get(face, top - 12, kz))) face++;
        float[] hard = { .3f, .5f, .7f, .9f };
        for (int i = 0; i < hard.Length; i++) Sim.Jar(face - 1, top - 12, kz + 8 - i * 16, 7, hard[i]);
        Advance(.2);
        Paused = true;
        if (_look == 1) Rig.SetAt(w.WX(face), w.WY(top - 12), w.WZ(kz - 16), -1.45f, .62f, 1f);
        else Rig.SetAt(w.WX(face), w.WY(top - 12), w.WZ(kz - 16), -1.45f, .3f, 3.2f);
        Hud.Refresh();
    }

    /// <summary>
    /// A ball of earth the hand tore out of a hilltop, let go over the slope: rolling down it into a band of the horde at
    /// the foot (--look=1: lying still at the foot after, the band knocked about).
    /// </summary>
    void RollDown()
    {
        var sim = Sim; var w = World;
        var (sx, sz, g) = HillStage();
        sim.Cast(SpellKind.Telekinesis, sx + 10.5f, g + 23.5f, sz + .5f);
        var ball = sim.HeldNow;
        sim.HoldAt(sx + 28, g + 44, sz + .5f);
        Advance(2.5);
        sim.LetGo();
        // down the slope and among them (or till it lies still)
        for (int t = 0; t < 600 && ball != null && sim.Fliers.Contains(ball) && (_look == 1 || !ball.Rolling || ball.X < sx + (_look == 2 ? 40 : 70)); t++) Advance(.05);
        if (_look == 1) Advance(2);
        // --look=2: left running, to see the dust it raises as it goes (shoot a few dozen frames in)
        Paused = _look != 2;
        Rig.SetAt(w.WX(sx + 72), w.WY(g + 6), w.WZ(sz), -.75f, .38f, 4f);
        Hud.Refresh();
    }

    /// <summary>
    /// A hill of earth west of the citadel falling away eastward, three metres down over six, into a hollow that rises
    /// gently again beyond, and a band of the horde standing in the hollow; our fighters off the field: where the hill
    /// stands (voxels) and the ground it was raised from.
    /// </summary>
    (int sx, int sz, int g) HillStage()
    {
        var sim = Sim; var w = World;
        Sorcerous();
        int sx = Site.KeepX - Site.KeepR - 200, sz = Site.KeepZ + 40, g = w.SurfAt(sx, sz);
        // its sides going down into the ground about it
        int Top(int x) => x < sx + 24 ? g + 24 : x < sx + 72 ? g + 24 - (x - sx - 24) / 2 : x < sx + 122 ? g + (x - sx - 72) / 5 : g + 10;
        var ground = new int[148, 80];
        for (int z = 0; z < 80; z++) for (int x = 0; x < 148; x++) ground[x, z] = w.SurfAt(sx - 8 + x, sz - 40 + z);
        for (int z = sz - 40; z < sz + 40; z++)
            for (int x = sx - 8; x < sx + 140; x++)
            {
                float side = Math.Clamp((Math.Abs(z - sz) - 18) / 22f, 0, 1);
                int was = ground[x - sx + 8, z - sz + 40], top = (int)MathF.Round(Top(x) * (1 - side) + was * side);
                for (int y = Math.Min(top, was) - 4; y < Math.Max(top, was) + 40; y++) w.Set(x, y, z, y < top - 1 ? Mat.Soil : y == top - 1 ? Mat.Grass : Mat.Air);
            }
        // our fighters off the field, so the band stands where it is
        foreach (var u in sim.Units) if (u.Team == Team.Player && u.T.Work < 1) { u.Dead = true; u.DiedAt = -999; }
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, 80, -3), (Kinds.Orc, 88, 5), (Kinds.Goblin, 84, 10), (Kinds.Goblin, 94, -8), (Kinds.Orc, 98, 2), (Kinds.Goblin, 76, 6) })
            Camp(t, sx + dx, sz + dz);
        return (sx, sz, g);
    }

    /// <summary>
    /// The horde's deck over a ditch, a band on it, and a ball of earth the hand tore out let fall on them: it breaks
    /// through the planks in splinters, and the band goes down with it (--look=1: after, the hole and the ditch).
    /// </summary>
    void CrushDeck()
    {
        var sim = Sim; var w = World;
        var (sx, sz, g) = DeckStage();
        sim.Cast(SpellKind.Telekinesis, sx - 30.5f, g - .5f, sz + 30.5f);
        var ball = sim.HeldNow;
        sim.HoldAt(sx + 14, g + 52, sz + .5f);
        Advance(3);
        sim.LetGo();
        // till it is going through the planks (or, --look=1, lying in the ditch after)
        for (int t = 0; t < 200 && ball != null && sim.Fliers.Contains(ball) && ball.Y > g + 6; t++) Advance(.05);
        Advance(_look == 1 ? 5 : .15);
        Paused = true;
        Rig.SetAt(w.WX(sx + 14), w.WY(g - 6), w.WZ(sz), -.75f, .42f, 3.6f);
        Hud.Refresh();
    }

    /// <summary>
    /// A ditch west of the citadel, three metres deep and three and a half across, the horde's deck over it two metres
    /// wide, and a band on the deck; our fighters off the field: where the ditch begins (voxels) and the ground there.
    /// </summary>
    (int sx, int sz, int g) DeckStage()
    {
        var sim = Sim; var w = World;
        Sorcerous();
        foreach (var u in sim.Units) if (u.Team == Team.Player && u.T.Work < 1) { u.Dead = true; u.DiedAt = -999; }
        int sx = Site.KeepX - Site.KeepR - 180, sz = Site.KeepZ, g = w.SurfAt(sx, sz);
        for (int z = sz - 40; z < sz + 40; z++)
            for (int x = sx - 40; x < sx + 60; x++)
                for (int y = g - 30; y < g + 40; y++)
                {
                    bool ditch = x >= sx && x < sx + 28 && Math.Abs(z - sz) < 30 && y >= g - 24;
                    bool deck = y >= g - 2 && y < g && x >= sx - 4 && x < sx + 32 && Math.Abs(z - sz) < 8;
                    w.Set(x, y, z, deck ? Mat.Deck : ditch || y >= g ? Mat.Air : y == g - 1 ? Mat.Grass : Mat.Soil);
                }
        Advance(.5);
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, 8, -3), (Kinds.Orc, 14, 2), (Kinds.Goblin, 20, -2), (Kinds.Goblin, 11, 4), (Kinds.Orc, 18, -5) })
            Camp(t, sx + dx, sz + dz);
        return (sx, sz, g);
    }

    /// <summary>
    /// A wall of ours on a hilltop, stones and logs laid along its outer edge and archers by them; the band at its foot
    /// and on the slope below: the stones and logs go over onto those at the foot and roll on down the slope through the
    /// rest (--look=1: lying at the bottom after).
    /// </summary>
    /// <summary>
    /// The household of a new game felling the trees nearest the fort west of the citadel, marked as the player marks
    /// them; --at= seconds of the work: with time enough, stumps and logs being cut up, and no crown left in the air;
    /// --look=1 close on the first of them; --look=2 the first caught halfway over, the game stopped.
    /// </summary>
    void Felling()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9;
        StartingFort.Raise(sim);
        foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker }) sim.Spawn(t, sim.Gate);
        int cx = Site.KeepX - Site.KeepR - 90, cz = Site.KeepZ + 40, felled = 0, fx = cx, fz = cz;
        var taken = new HashSet<(int, int)>();
        for (int r = 0; r < 120 && felled < 3; r += 3)
            for (int a = 0; a < 32 && felled < 3; a++)
            {
                int x = cx + (int)(MathF.Cos(a * MathF.Tau / 32) * r), z = cz + (int)(MathF.Sin(a * MathF.Tau / 32) * r);
                if (!w.InBounds(x, 2, z) || taken.Contains((x >> 4, z >> 4))) continue;
                int g = w.SurfAt(x, z);
                if (!Mat.IsTrunk(w.Get(x, g + 2, z))) continue;
                sim.DesignateRemove(Shapes.Tree(w, x, g + 2, z));
                for (int dz = -3; dz <= 3; dz++) for (int dx = -3; dx <= 3; dx++) taken.Add(((x >> 4) + dx, (z >> 4) + dz));
                if (felled++ == 0) (fx, fz) = (x, z);
            }
        // --look=2: the first tree caught halfway over, the game stopped
        if (_look == 2)
        {
            Toppling first = null;
            sim.Toppled += t => first ??= t;
            for (int t = 0; t < 1600 && first == null; t++) Advance(.25);
            GD.Print($"[pose] felling: {(first == null ? "no tree went over" : $"a tree went over at {first.From:0} s, {first.Voxels.Count} voxels, foot {first.PX:0},{first.PY:0},{first.PZ:0}")}; " +
                string.Join("; ", sim.Units.FindAll(x => x.T == Kinds.Worker).ConvertAll(x => $"#{x.Id} {x.State} {x.Why} at {x.X:0},{x.Y:0},{x.Z:0}")));
            if (first != null)
            {
                Advance((first.To - first.From) * .6);
                Paused = true;
                Rig.Set(w.WX(first.PX + first.DX * 12), w.WZ(first.PZ + first.DZ * 12), -.66f, .42f, 1.6f, snap: true);
            }
            Hud.Refresh();
            return;
        }
        Advance(_poseSeconds);
        // --look=1: close on the first tree, to watch it felled
        if (_look == 1) Rig.Set(w.WX(fx), w.WZ(fz), -.66f, .5f, 2.6f, snap: true);
        else Rig.Set(w.WX(cx), w.WZ(cz), -.66f, .6f, 1.5f, snap: true);
        Hud.Refresh();
    }

    void Drops()
    {
        var sim = Sim; var w = World; var s = Site;
        sim.Waves.Countdown = 1e9;
        foreach (var u in sim.Units) if (u.Team == Team.Player && u.T.Work < 1) { u.Dead = true; u.DiedAt = -999; }
        int sx = s.KeepX - s.KeepR - 200, sz = s.KeepZ + 40, g = w.SurfAt(sx, sz);
        // a hill falling away eastward, three metres down over six, its sides going down into the ground about it
        int Top(int x) => x < sx + 24 ? g + 24 : x < sx + 72 ? g + 24 - (x - sx - 24) / 2 : g;
        var ground = new int[160, 96];
        for (int z = 0; z < 96; z++) for (int x = 0; x < 160; x++) ground[x, z] = w.SurfAt(sx - 20 + x, sz - 48 + z);
        for (int z = sz - 48; z < sz + 48; z++)
            for (int x = sx - 20; x < sx + 140; x++)
            {
                float side = Math.Clamp((Math.Abs(z - sz) - 28) / 20f, 0, 1);
                int was = ground[x - sx + 20, z - sz + 48], top = (int)MathF.Round(Top(x) * (1 - side) + was * side);
                for (int y = Math.Min(top, was) - 4; y < Math.Max(top, was) + 48; y++) w.Set(x, y, z, y < top - 1 ? Mat.Soil : y == top - 1 ? Mat.Grass : Mat.Air);
            }
        // the wall on the hilltop, three metres high, and along its outer edge stones and logs to throw down
        sim.InstantLabour = true;
        int top2 = Shapes.LevelTop(w, sx + 12, sz, 24), edge = sx + 20;
        sim.DesignateBuild(Shapes.Line(w, s, sx + 12, sz - 30, sx + 12, sz + 30, new Stroke { Mat = Mat.Stone, Width = 8, Height = 24, Level = true }, top2));
        while (edge > sx && w.Get(edge, top2 - 1, sz) == Mat.Air) edge--;
        var heaps = new VoxelList();
        for (int z = sz - 22; z < sz - 6; z++) for (int y = top2; y < top2 + 3; y++) { heaps.Add(edge, y, z, Mat.DropStone); heaps.Add(edge - 1, y, z, Mat.DropStone); }
        for (int z = sz + 6; z < sz + 22; z++) for (int y = top2; y < top2 + 2; y++) { heaps.Add(edge, y, z, Mat.DropLog); heaps.Add(edge - 1, y, z, Mat.DropLog); }
        sim.DesignateBuild(heaps);
        sim.InstantLabour = false;
        Advance(.5);
        foreach (int dz in new[] { -14, 14 })
        {
            int c = sim.NearestStand(edge - 5, sz + dz, 4);
            var a = sim.Spawn(Kinds.Archer, c);
            sim.Command(new[] { a }, c);
        }
        // the band: two at the foot of the wall under the heaps (in the first metre of ground clear of it), the rest on the
        // slope below
        int foot = ((edge >> NavGrid.Shift) + 1 << NavGrid.Shift) + NavGrid.CS / 2;
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, 0, -14), (Kinds.Orc, 0, 14), (Kinds.Goblin, 16, -8), (Kinds.Goblin, 24, 6), (Kinds.Orc, 32, -2), (Kinds.Goblin, 20, 18), (Kinds.Goblin, 28, -16) })
            Camp(t, foot + dx, sz + dz);
        // till both heaps are over (or, --look=1, all lying still after)
        int Heaped() { int n = 0; for (int z = sz - 24; z < sz + 24; z++) for (int x = edge - 2; x <= edge; x++) for (int y = top2; y < top2 + 3; y++) if (w.Get(x, y, z) is Mat.DropStone or Mat.DropLog) n++; return n; }
        for (int t = 0; t < 200 && Heaped() > 16; t++) Advance(.05);
        Advance(_look == 1 ? 6 : .9);
        Paused = true;
        Rig.SetAt(w.WX(sx + 52), w.WY(g + 8), w.WZ(sz), 1.57f, .72f, 1.6f);
        Hud.Refresh();
    }

    /// <summary>A catapult throwing at a wall of ours: its stone high on its arc (--look=1: striking the wall).</summary>
    void Stones()
    {
        var sim = Sim; var w = World;
        Sorcerous();
        int wx = Site.KeepX - Site.KeepR - 60, kz = Site.KeepZ, top = WallWest(-60, 32, 60);
        var cat = sim.Spawn(Kinds.Catapult, sim.NearestStand(wx - 240, kz, 3));
        int face = wx - 8; while (face < wx + 8 && !Mat.IsMasonry(w.Get(face, top - 6, kz))) face++;
        cat.AimAt = WaterPlanner.Pack(face, top - 6, kz); cat.NextLook = 1e9;
        Flier stone = null;
        for (int t = 0; t < 200 && stone == null; t++) { Advance(.1); stone = sim.Fliers.Find(f => f.Smash > 0); }
        if (stone != null)
        {
            if (_look == 1) for (int t = 0; t < 120 && sim.Fliers.Contains(stone); t++) Advance(.05);
            else for (int t = 0; t < 120 && stone.VY > 0; t++) Advance(.05);
        }
        Paused = true;
        if (_look == 1) Rig.SetAt(w.WX(wx - 20), w.WY(top - 16), w.WZ(kz), -.75f, .45f, 2.6f);
        else Rig.SetAt(w.WX(stone?.X ?? wx - 120), w.WY(stone?.Y ?? top), w.WZ(stone?.Z ?? kz), -.75f, .4f, 3.2f);
        Hud.Refresh();
    }

    /// <summary>
    /// A hill giant before a stone wall of ours west of the citadel, archers on it, the game stopped: --look=0 stooping to
    /// tear a boulder out of the ground (--at=seconds into the throw, .6 by itself), 1 the boulder high on its arc, 2 the
    /// moment it strikes the wall, pieces flying, 3 after five of them, the wall broken through; 4 another one up against
    /// the wall smashing it with its club, swordsmen of ours by it thrown into the air.
    /// </summary>
    void HillGiants()
    {
        var sim = Sim; var w = World;
        Sorcerous();
        sim.WantTrails = false;
        int wx = Site.KeepX - Site.KeepR - 60, kz = Site.KeepZ, top = WallWest(-60, 32, 60);
        // the field before the wall cleared of trees, from the ground up, and behind it
        for (int z = kz - 90; z < kz + 150; z++)
            for (int x = wx - 260; x < wx + 40; x++)
                for (int y = w.GroundTop(x, z); y < w.SY; y++)
                    if (w.Get(x, y, z) is var m && (Mat.IsTrunk(m) || Mat.IsLeafy(m))) w.Set(x, y, z, Mat.Air);
        foreach (int dz in new[] { -24, 0, 24 })
        {
            int c = sim.NearestStand(wx + 2, kz + dz, 4);
            sim.Command(new[] { sim.Spawn(Kinds.Archer, c) }, c);
        }
        int face = wx - 8; while (face < wx + 8 && !Mat.IsMasonry(w.Get(face, top - 18, kz))) face++;
        float aimY = top - 18;
        if (_look == 4)
        {
            var smasher = Camp(Kinds.HillGiant, face - 10, kz + 6);
            smasher.Guard = false;
            sim.SmashInto(smasher, sim.Nav.CellOf(face + 2, (int)smasher.Y + 1, (int)smasher.Z));
            Advance(_poseSeconds != 14 ? _poseSeconds : 5);
            // and two of ours come at it: the first blow throws them
            foreach (int dz in new[] { -6, 20 })
            {
                var s = sim.Spawn(Kinds.Swordsman, sim.NearestStand(face - 14, kz + dz, 2));
                s.Hp = s.MaxHp = 4000;
                sim.Command(new[] { s }, s.Cell);
            }
            for (int t = 0; t < 80 && !sim.Units.Exists(u => u.T == Kinds.Swordsman && u.Airborne != null); t++) Advance(.05);
            Advance(.15);
            Paused = true;
            Rig.SetAt(w.WX(face - 4), w.WY(smasher.Y + 14), w.WZ(kz + 6), -.45f, .42f, 3.2f);
            Hud.Refresh();
            return;
        }
        var giant = Camp(Kinds.HillGiant, wx - 150, kz - 4);
        giant.Hp = giant.MaxHp = 1e5f;
        giant.NextHurl = 1e9;
        Advance(.3);
        if (_look == 3)
        {
            for (int n = 0; n < 5; n++)
            {
                for (int t = 0; t < 80 && sim.Hurling(giant); t++) Advance(.1);
                sim.OrderHurl(giant, face, aimY, kz);
            }
            Advance(14);
            Paused = true;
            Rig.SetAt(w.WX(face - 6), w.WY(top - 16), w.WZ(kz), -1.25f, .32f, 2.6f);
            Hud.Refresh();
            return;
        }
        sim.OrderHurl(giant, face, aimY, kz);
        var stone = giant.Hefted;

        if (_look == 1) for (int t = 0; t < 120 && stone != null && (stone.Carrier != null || stone.VY > 0); t++) Advance(.05);
        else if (_look == 2)
        {
            int broke = sim.GiantBroke;
            for (int t = 0; t < 400 && sim.GiantBroke == broke; t++) Advance(.02);
            Advance(.08);
        }
        else Advance(_poseSeconds != 14 ? _poseSeconds : .6);
        Paused = true;
        if (_look == 2) Rig.SetAt(w.WX(face - 10), w.WY(top - 14), w.WZ(kz), -.85f, .4f, 3f);
        else if (_look == 1) Rig.SetAt(w.WX(stone?.X ?? giant.X), w.WY(stone?.Y ?? top), w.WZ(stone?.Z ?? kz), -.75f, .38f, 2.6f);
        else Rig.SetAt(w.WX(giant.X + 6), w.WY(giant.Y + 14), w.WZ(giant.Z), 2.1f, .3f, 3.6f);
        Hud.Refresh();
    }

    /// <summary>A goblin shaman before a wall of ours west of the citadel, a curse laid on it, the hurt of its band mending about it.</summary>
    void Hexing()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 100;
        int wx = Site.KeepX - Site.KeepR - 90, kz = Site.KeepZ;
        int top = Shapes.LevelTop(w, wx, kz, 32);
        sim.InstantLabour = true;
        sim.DesignateBuild(Shapes.Line(w, Site, wx, kz - 40, wx, kz + 40, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Level = true }, top));
        sim.InstantLabour = false;
        Unit Camp(UnitType t, int dx, int dz) { var u = sim.Spawn(t, sim.NearestStand(wx + dx, kz + dz, t.Move.Height)); u.Guard = true; u.Home = u.Cell; return u; }
        var shaman = Camp(Kinds.Shaman, -64, 4);
        foreach (var (dx, dz) in new[] { (-56, -14), (-52, 16), (-74, 22), (-70, -20) }) { var g = Camp(Kinds.Goblin, dx, dz); g.Hp = g.MaxHp * .4f; }
        Camp(Kinds.Orc, -60, -30);
        for (int t = 0; t < 120 && sim.Curses.Count == 0; t++) Advance(.25);
        Advance(1);
        Rig.SetAt(w.WX(wx - 30), w.WY(top - 16), w.WZ(kz), -.75f, .55f, _look == 1 ? 3.4f : 2.1f);
        Hud.Refresh();
    }

    void Sheltering()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX, kz = s.KeepZ;
        int x0 = kx + 120, z0 = kz + 150, x1 = x0 + 48, z1 = z0 + 40;
        int top = Shapes.LevelTop(w, (x0 + x1) / 2, (z0 + z1) / 2, 30);
        var st = new Stroke { Mat = Mat.Wood, Width = 2, Height = 16 };
        sim.InstantLabour = true;
        // a palisade a man and a half high, its way in on the east, toward the citadel
        sim.DesignateBuild(Shapes.Line(w, s, x0, z0, x1, z0, st, top));
        sim.DesignateBuild(Shapes.Line(w, s, x0, z1, x1, z1, st, top));
        sim.DesignateBuild(Shapes.Line(w, s, x0, z0, x0, z1, st, top));
        sim.DesignateBuild(Shapes.Line(w, s, x1, z0, x1, z0 + 14, st, top));
        sim.DesignateBuild(Shapes.Line(w, s, x1, z1 - 14, x1, z1, st, top));
        sim.InstantLabour = false;
        sim.AddZone(x0 + 3, z0 + 3, x1 - 3, z1 - 3, Zone.Shelter, 0);
        foreach (var (dx, dz) in new[] { (80, 0), (90, 30), (-30, 60), (20, 80), (100, 60) })
            sim.Spawn(Kinds.Worker, sim.NearestStand(x0 + dx, z0 + dz, 4));
        Advance(2);
        sim.RaiseAlarm();
        Advance(_poseSeconds);
        Rig.Set(World.WX((x0 + x1) / 2f + 10), World.WZ((z0 + z1) / 2f), -.66f, .7f, 1.6f, snap: true);
        Hud.Refresh();
        Marks.Flush();
    }

    /// <summary>
    /// A dwelling: a stone house roofed with timber, a gate in its side, framed as a dwelling and furnished by the workers
    /// with bunks along its walls; for the picture half its roof is lifted off to show them. The citadel's muster is open
    /// in the console with the places the bunks give.
    /// </summary>
    void Housing()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX, kz = s.KeepZ;
        // the nearest flat dry ground east of the citadel, clear of the fort
        int hx = kx + 150, hz = kz + 160;
        bool found = false;
        for (int r = 110; r < 400 && !found; r += 12)
            for (int a = 0; a < 24 && !found; a++)
            {
                int cx = kx + (int)(r * Math.Cos(a * Math.PI / 12)), cz = kz + (int)(r * Math.Sin(a * Math.PI / 12));
                int lo = int.MaxValue, hi = int.MinValue; bool bad = false;
                for (int z = cz - 48; z <= cz + 48 && !bad; z += 4)
                    for (int x = cx - 36; x <= cx + 36 && !bad; x += 4)
                    {
                        if (x < 0 || z < 0 || x >= w.SX || z >= w.SZ || s.Forbidden(x, z)) { bad = true; break; }
                        int g = w.SurfAt(x, z); lo = Math.Min(lo, g); hi = Math.Max(hi, g);
                        if (w.Get(x, g, z) == Mat.Water || w.Get(x, g - 1, z) == Mat.Water || Mat.IsPlayer[w.Get(x, g - 1, z)]) bad = true;
                    }
                if (!bad && hi - lo <= 2) { hx = cx; hz = cz; found = true; }
            }
        int top = Shapes.LevelTop(w, hx, hz, 32), floor = top - 32;
        sim.InstantLabour = true;
        // five metres by nine, walls a metre thick: the timber roof spans the three metres between the long walls
        sim.DesignateBuild(Shapes.Box(w, s, hx - 20, hz - 36, hx + 19, hz + 35, new Stroke { Mat = Mat.Stone, Width = 8, Height = 32, Level = true, Hollow = true }, top));
        for (int z = hz - 36; z <= hz + 35; z++) for (int x = hx - 20; x <= hx + 19; x++) for (int y = top; y < top + 4; y++) w.Set(x, y, z, Mat.Wood);
        // the gate in the north end wall
        int fy = floor + 6, fz = hz;
        while (w.Get(hx, fy, fz) == Mat.Air) fz--;
        while (w.Get(hx, fy, fz) != Mat.Air) fz--;
        var plan = GatePlan.At(w, s, new VoxelHit { X = hx, Y = fy, Z = fz + 1, NZ = -1, M = w.Get(hx, fy, fz + 1) });
        if (plan != null) sim.AddGate(plan, built: true);
        Advance(1);
        var home = sim.AddZone(hx - 23, hz - 40, hx + 22, hz + 39, Zone.Dwelling, 0);
        sim.LookOver(home);
        sim.InstantLabour = false;
        sim.LookOver(home);
        foreach (var (dx, dz) in new[] { (-60, 10), (-70, 40), (-50, -30) })
            sim.Spawn(Kinds.Worker, sim.NearestStand(hx + dx, hz + dz, 4));
        Advance(_poseSeconds);
        // half the roof off, to see in (the dwelling counts as closed till it is next looked over, seconds from now)
        sim.LookOver(home);
        GD.Print($"[pose] dwelling at {hx},{hz}: {(home.Closed ? "closed" : home.Why)}, {home.Beds} of {home.Planned} bunks, {sim.Places} places");
        for (int z = hz; z <= hz + 35; z++) for (int x = hx - 20; x <= hx + 19; x++) for (int y = top; y < top + 4; y++) w.Set(x, y, z, Mat.Air);
        Command.OpenKeep();
        Rig.Set(World.WX(hx), World.WZ(hz + 16), -.6f, 1.05f, 2.2f, snap: true);
        Hud.Refresh();
        Marks.Flush();
    }

    /// <summary>
    /// The traps at work: a stone wall west of the citadel with stones heaped on its battlements and archers by them, a
    /// strip of stakes before it, a wolf pit under a trap floor further out with stakes at its bottom, and a band of
    /// the horde coming on.
    /// </summary>
    void Trapped()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX, kz = s.KeepZ;
        int wx = kx - s.KeepR - 70, z0 = kz - 64, z1 = kz + 64;
        int top = Shapes.LevelTop(w, wx, kz, 32);
        sim.InstantLabour = true;
        sim.DesignateBuild(Shapes.Line(w, s, wx, z0, wx, z1, new Stroke { Mat = Mat.Stone, Width = 9, Height = 32, Level = true }, top));
        // stones along the outer edge of its top, in the middle
        int edge = wx - 8;
        while (edge < wx && w.Get(edge, top - 1, kz) == Mat.Air) edge++;
        var stones = new VoxelList();
        for (int z = kz - 30; z < kz + 30; z++)
            for (int y = top; y < top + 3; y++) { stones.Add(edge, y, z, Mat.DropStone); stones.Add(edge + 1, y, z, Mat.DropStone); }
        sim.DesignateBuild(stones);
        // stakes a metre high, two to five metres out
        sim.DesignateBuild(Shapes.Box(w, s, wx - 40, z0 + 10, wx - 18, z1 - 10, new Stroke { Mat = Mat.Stakes, Height = 8 }, 0));
        // a wolf pit further out: two metres down, stakes at its bottom, a trap floor over it flush with the ground
        int px = wx - 90, pz = kz - 12, ground = Shapes.LevelTop(w, px + 12, pz + 12, 0);
        var pit = new VoxelList();
        for (int z = pz; z < pz + 24; z++)
            for (int x = px; x < px + 24; x++)
                for (int y = w.SurfAt(x, z) - 1; y >= ground - 18; y--) if (w.Get(x, y, z) != Mat.Air) w.Set(x, y, z, Mat.Air);
        for (int z = pz; z < pz + 24; z++)
            for (int x = px; x < px + 24; x++)
                if (x % 3 == 0 && z % 3 == 0) for (int y = ground - 18; y < ground - 12; y++) w.Set(x, y, z, Mat.Stakes);
        sim.DesignateBuild(Shapes.Box(w, s, px, pz, px + 23, pz + 23, new Stroke { Mat = Mat.TrapDeck, Height = 0, Level = true }, ground));
        sim.InstantLabour = false;
        foreach (int dz in new[] { -20, 0, 20 })
        {
            int c = sim.NearestStand(edge + 6, kz + dz, 4);
            var a = sim.Spawn(Kinds.Archer, c);
            sim.Command(new[] { a }, c);
        }
        // a band come on by day, so the traps are to be seen
        Advance(1);
        sim.HordeStock[Stockpile.Wood] = sim.HordeStock[Stockpile.Earth] = 0;
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, -120, -10), (Kinds.Goblin, -126, 8), (Kinds.Goblin, -116, 20), (Kinds.Orc, -134, 0),
            (Kinds.Goblin, -60, -40), (Kinds.Goblin, -66, 30), (Kinds.Orc, -72, -20), (Kinds.Goblin, -56, 44) })
        {
            int c = sim.NearestStand(wx + dx, kz + dz, t.Move.Height);
            if (c >= 0) sim.Spawn(t, c);
        }
        sim.ComputeFields();
        Advance(_poseSeconds);
        Rig.Set(World.WX(wx - 48), World.WZ(kz - 4), -.5f, .8f, 1.35f, snap: true);
        Hud.Refresh();
        Marks.Flush();
    }

    /// <summary>
    /// A ditch west of the citadel with water at its bottom and pitch on the water, a drawbridge over it with a stone
    /// wall either side, our people on the near side and a band of the horde coming on (--at: how long they come; the
    /// bridge goes up before them, and who is on it goes into the ditch); the pitch set alight further along.
    /// </summary>
    void Drawn()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX, kz = s.KeepZ;
        int cx = kx - s.KeepR - 110, z0 = kz - 90, z1 = kz + 90, g = Shapes.LevelTop(w, cx, kz, 0);
        // flat ground either side, the ditch between: four metres wide, two and a half deep, a metre of water in it
        for (int z = z0; z < z1; z++)
            for (int x = cx - 80; x < cx + 60; x++)
            {
                for (int y = g; y < g + 40; y++) if (w.Get(x, y, z) != Mat.Air) w.Set(x, y, z, Mat.Air);
                for (int y = g - 24; y < g; y++) if (w.Get(x, y, z) == Mat.Air) w.Set(x, y, z, Mat.Soil);
                w.Set(x, g - 1, z, Math.Abs(x - cx) <= 16 ? Mat.Soil : Mat.Grass);
                if (Math.Abs(x - cx) > 16) continue;
                for (int y = g - 20; y < g; y++) w.Set(x, y, z, Mat.Air);
                for (int y = g - 20; y < g - 12; y++) w.Set(x, y, z, Mat.Water);
            }
        sim.InstantLabour = true;
        // a stone wall on the near bank either side of the bridge
        foreach (var (a, b) in new[] { (z0 + 10, kz - 14), (kz + 14, z1 - 10) })
            sim.DesignateBuild(Shapes.Line(w, s, cx + 26, a, cx + 26, b, new Stroke { Mat = Mat.Stone, Width = 6, Height = 24, Level = true }, g + 24));
        sim.InstantLabour = false;
        var bridge = Drawbridge.Plan(w, s, cx + 22, kz, cx - 22, kz, 12, out _);
        if (bridge != null) sim.AddDrawbridge(bridge, built: true);
        // pitch on the water to the north, alight
        for (int z = z0 + 20; z < z0 + 60; z++) for (int x = cx - 16; x <= cx + 16; x++) w.Set(x, g - 12, z, Mat.Pitch);
        Advance(1);
        sim.Ignite(cx, g - 12, z0 + 40);
        foreach (var (dx, dz) in new[] { (60, -20), (64, 10), (58, 26) })
        {
            int c = sim.NearestStand(cx + dx, kz + dz, 4);
            var a = sim.Spawn(Kinds.Archer, c);
            sim.Command(new[] { a }, c);
        }
        sim.HordeStock[Stockpile.Wood] = sim.HordeStock[Stockpile.Earth] = 0;
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, -50, -8), (Kinds.Goblin, -56, 10), (Kinds.Orc, -62, 0), (Kinds.Goblin, -70, -20), (Kinds.Goblin, -66, 24) })
        {
            int c = sim.NearestStand(cx + dx, kz + dz, t.Move.Height);
            if (c >= 0) sim.Spawn(t, c);
        }
        sim.ComputeFields();
        Advance(_poseSeconds);
        Rig.Set(World.WX(cx + 4), World.WZ(kz + 4), -.62f, .7f, 1.6f, snap: true);
        Hud.Refresh();
        Marks.Flush();
    }

    /// <summary>
    /// Thieves at work by the citadel: workers felling west of it with nobody to guard them, and three thieves come
    /// on (--at: how long after): one beats a worker down and leads him off bound, others make off with the stores.
    /// </summary>
    void Robbed()
    {
        var sim = Sim; var s = Site;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX, kz = s.KeepZ;
        sim.Stock[Stockpile.Wood] = 30 * VoxelWorld.PerM3;
        foreach (var u in sim.Units) if (u.Team == Team.Player && u.T.Work < 1) { u.Dead = true; u.DiedAt = -999; }
        foreach (var (dx, dz) in new[] { (-120, -30), (-128, 10), (-110, 34) })
            sim.Command(new[] { sim.Spawn(Kinds.Worker, sim.NearestStand(kx - s.KeepR + dx, kz + dz, 4)) }, sim.NearestStand(kx - s.KeepR + dx, kz + dz, 4));
        foreach (var (dx, dz) in new[] { (-170, -24), (-176, 4), (-168, 30) })
            sim.Spawn(Kinds.Thief, sim.NearestStand(kx - s.KeepR + dx, kz + dz, 2));
        sim.ComputeFields();
        Advance(_poseSeconds);
        Rig.Set(World.WX(kx - s.KeepR - 70), World.WZ(kz + 6), -.66f, .62f, 1.9f, snap: true);
        Hud.Refresh();
        Marks.Flush();
    }

    /// <summary>
    /// The horde's engines before a stone wall west of the citadel: a catapult with its guard throwing at the wall from
    /// out of bowshot, a ram rolling up to it, archers on the wall (--at: how long it has gone on).
    /// </summary>
    void Engines()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX, kz = s.KeepZ;
        int wx = kx - s.KeepR - 70;
        int top = Shapes.LevelTop(w, wx, kz, 32);
        sim.InstantLabour = true;
        sim.DesignateBuild(Shapes.Line(w, s, wx, kz - 110, wx, kz + 110, new Stroke { Mat = Mat.Stone, Width = 9, Height = 32, Level = true }, top));
        sim.InstantLabour = false;
        foreach (int dz in new[] { -30, 0, 30 })
        {
            int c = sim.NearestStand(wx + 2, kz + dz, 4);
            sim.Command(new[] { sim.Spawn(Kinds.Archer, c) }, c);
        }
        sim.HordeStock[Stockpile.Wood] = sim.HordeStock[Stockpile.Earth] = 0;
        var cat = sim.Spawn(Kinds.Catapult, sim.NearestStand(wx - 300, kz - 10, 3));
        foreach (var t in new[] { Kinds.Orc, Kinds.Goblin })
        {
            var g = sim.Spawn(t, sim.NearestStand((int)cat.X + 12, (int)cat.Z + 14, 2));
            g.Escort = cat; g.Guard = true; g.Home = cat.Cell;
        }
        sim.ComputeFields();
        sim.Spawn(Kinds.Ram, sim.NearestStand(wx - 120, kz + 30, 3));
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, -130, 20), (Kinds.Goblin, -136, 44), (Kinds.Orc, -140, 34) })
            sim.Spawn(t, sim.NearestStand(wx + dx, kz + dz, t.Move.Height));
        // a siege tower making for the wall further along, a mantlet before a band of goblins
        sim.Spawn(Kinds.SiegeTower, sim.NearestStand(wx - 110, kz - 70, 3));
        var band = new Warband { Id = 99, Name = "Ватага Грызя", State = BandState.March };
        var lead = sim.Spawn(Kinds.Orc, sim.NearestStand(wx - 170, kz - 20, 2));
        band.Members.Add(lead); band.Leader = lead; lead.Band = band;
        sim.Bands.Add(band);
        var shield = sim.Spawn(Kinds.Mantlet, sim.NearestStand(wx - 156, kz - 20, 2));
        shield.Band = band; band.Members.Add(shield);
        foreach (var (dx, dz) in new[] { (-180, -14), (-180, -26), (-186, -20) })
        {
            var g = sim.Spawn(Kinds.Goblin, sim.NearestStand(wx + dx, kz + dz, 2));
            g.Band = band; band.Members.Add(g);
        }
        Advance(_poseSeconds);
        Rig.Set(World.WX(wx - 150), World.WZ(kz + 10), -.95f, .66f, .95f, snap: true);
        Hud.Refresh();
        Marks.Flush();
    }

    /// <summary>A row of the hurt for a close look: an arm cut off, arrows stuck in, a head split, a goblin cut down.</summary>
    void Wounded()
    {
        var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = Site.KeepX, kz = Site.KeepZ;
        Unit Put(UnitType t, int dx, int dz)
        {
            var u = sim.Spawn(t, sim.NearestStand(kx + dx, kz + dz, 4));
            u.Facing = MathF.PI * .75f;
            sim.Command(new[] { u }, u.Cell);
            return u;
        }
        var (sc, sd, sr) = Sim.WeaponOf(Kinds.Swordsman);
        var (bc, bd, _) = Sim.WeaponOf(Kinds.Archer);
        var (ac, ad, ar) = Sim.WeaponOf(Kinds.Orc);
        float sin = MathF.Sin(MathF.PI * .75f), cos = MathF.Cos(MathF.PI * .75f);
        // a point and a direction in a body's own frame, out into the world
        (float, float, float) W(Unit u, float lx, float ly, float lz) => (u.X + lx * sin + lz * cos, u.Y + ly, u.Z - lx * cos + lz * sin);
        (float, float, float) D(float lx, float ly, float lz) => (lx * sin + lz * cos, ly, -lx * cos + lz * sin);
        void Blow(Unit u, Sim.Cut c, float depth, float reach, (float x, float y, float z) from, (float x, float y, float z) dir) => sim.Wound(u, c, depth, reach, from.x, from.y, from.z, dir.x, dir.y, dir.z);
        var armless = Put(Kinds.Worker, -70, -20);
        Blow(armless, sc, sd, sr, W(armless, -14, 7.5f, 0), D(1, 0, 0));
        var pierced = Put(Kinds.Swordsman, -70, -4);
        foreach (var (y, x) in new[] { (9f, 1f), (6.5f, -1f), (12.5f, .5f) }) Blow(pierced, bc, bd, 0, W(pierced, x, y, 20), D(0, -.1f, -1));
        var hewn = Put(Kinds.Orc, -70, 12);
        Blow(hewn, ac, ad, ar, W(hewn, 0, 14, 16), D(0, 0, -1));
        Blow(hewn, sc, sd, sr, W(hewn, 16, 9, 0), D(-1, 0, 0));
        var gob = Put(Kinds.Goblin, -70, 26);
        Blow(gob, sc, sd, sr, W(gob, -12, 4, 0), D(1, 0, 0));
        Blow(gob, sc, sd, sr, W(gob, 0, 2, 12), D(0, 0, -1));
        Advance(_poseSeconds > 0 && _poseSeconds < 14 ? _poseSeconds : 1.5);
        // the armless one is chosen: the console shows what he lost
        Selected.Add(armless);
        Hud.OnSelectionChanged();
        var look = _look >= 0 && _look < 4 ? new[] { armless, pierced, hewn, gob }[_look] : null;
        float mx = look?.X ?? (armless.X + pierced.X + hewn.X + gob.X) / 4, mz = look?.Z ?? (armless.Z + pierced.Z + hewn.Z + gob.Z) / 4;
        // the camera looks at a plane 4 m up: the ground here is higher, so look past it by as much
        const float yaw = -.55f, pitch = .5f;
        float over = (World.WY(armless.Y + 8) - 4) / MathF.Tan(pitch);
        Rig.Set(World.WX(mx) - MathF.Sin(yaw) * over, World.WZ(mz) - MathF.Cos(yaw) * over, yaw, pitch, look != null ? 7.5f : 3.6f, snap: true);
        Hud.Refresh();
    }

    /// <summary>
    /// What falls on people (--pose=trauma), on cleared ground west of the citadel: one of ours pinned under a heap of
    /// rubble and a worker come to dig him out, a swordsman limping off on a broken leg (chosen: the console says so),
    /// a worker going off with his arm hanging and his pick on the ground, an archer bent over his broken ribs.
    /// --look=1 pieces of a wall coming down on a band of the horde (the game stopped as they strike), --look=2 the band
    /// after it: the dead, the pinned, the broken (one of them chosen). --at= seconds after (2.5 by itself).
    /// </summary>
    void Trauma()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9;
        // on the level ground beside the citadel the figures are looked at on, its trees taken off before it was drawn
        var (gx, gz) = GroveAt;
        int cx = gx + 150, cz = gz + 110, g = w.SurfAt(cx, cz);
        double at = _poseSeconds is > 0 and < 14 ? _poseSeconds : 2.5;
        Unit Put(UnitType t, int dx, int dz) => sim.Spawn(t, sim.NearestStand(cx + dx, cz + dz, t.Move.Height));
        if (_look >= 1)
        {
            var band = new List<Unit>();
            for (int i = 0; i < 8; i++) band.Add(Camp(i % 3 == 0 ? Kinds.Goblin : Kinds.Orc, cx + i % 4 * 11 - 16, cz + i / 4 * 13 - 6));
            // the top of a wall breaking up over them: blocks of it, each a little off the last
            var rnd = new Random(7);
            for (int k = 0; k < 7; k++)
            {
                int sx = 2 + rnd.Next(2), sy = 2 + rnd.Next(2), sz = 2 + rnd.Next(2);
                var block = new List<(sbyte, sbyte, sbyte, byte)>();
                for (int dz = -sz; dz < sz; dz++) for (int dy = -sy; dy < sy; dy++) for (int dx = -sx; dx < sx; dx++) block.Add(((sbyte)dx, (sbyte)dy, (sbyte)dz, rnd.Next(4) == 0 ? Mat.Mortar : Mat.Stone));
                sim.Hurl(block, cx - 18 + rnd.Next(36), g + 30 + rnd.Next(14), cz - 9 + rnd.Next(22), 0, -4 - rnd.Next(4), 0, "обвал");
            }
            Advance(_look == 1 ? .78 : at + 1.5);
            if (_look == 1) Paused = true;
            if (_look == 2 && (band.Find(u => !u.Dead && Sim.Broken(u) != Break.None) ?? band.Find(u => !u.Dead)) is { } hurt) { Selected.Add(hurt); Hud.OnSelectionChanged(); }
            Rig.SetAt(w.WX(cx), w.WY(g + 8), w.WZ(cz), -.7f, .5f, 2.6f);
            Hud.Refresh();
            return;
        }
        var buried = Put(Kinds.Worker, 0, 0);
        Mound(buried);
        Put(Kinds.Worker, 22, 10);
        var limping = Put(Kinds.Swordsman, -24, -16);
        sim.BreakBone(limping, Break.LegL);
        var hung = Put(Kinds.Worker, -18, 18);
        sim.BreakBone(hung, Break.Main);
        var bent = Put(Kinds.Archer, 14, -22);
        sim.BreakBone(bent, Break.Ribs);
        Advance(at);
        Selected.Add(limping);
        Hud.OnSelectionChanged();
        Rig.SetAt(w.WX(cx - 4), w.WY(g + 6), w.WZ(cz), -.6f, .5f, 2.6f);
        Hud.Refresh();
    }

    /// <summary>A mound of rubble heaped over one, as a fall leaves it (no steeper than loose stone lies), just come down on it.</summary>
    void Mound(Unit u)
    {
        var w = World;
        int ux = (int)u.X, uz = (int)u.Z, uy = (int)MathF.Floor(u.Y + .01f);
        for (int z = uz - 6; z <= uz + 6; z++)
            for (int x = ux - 6; x <= ux + 6; x++)
            {
                int h = 7 - Math.Max(Math.Abs(x - ux), Math.Abs(z - uz));
                // the tufts and flowers it comes down on buried with the rest, lest the rubble over them stand on nothing
                for (int y = uy; y < uy + h; y++) if (w.Get(x, y, z) is Mat.Air or Mat.Tuft or Mat.Flower or Mat.Bloom) w.Set(x, y, z, Mat.Rubble);
            }
        // whatever of it stands on nothing settles at once, quietly, as a new game's ruins do: the heap a fall leaves
        Sim.Subside(ux - 8, uz - 8, ux + 8, uz + 8);
        u.StruckAt = Sim.Time;
    }

    /// <summary>Where the grove is planted (--pose=trees): dry level ground on the plateau beside the citadel, cleared first.</summary>
    (int x, int z) _groveAt = (-1, -1);
    (int x, int z) GroveAt
    {
        get
        {
            if (_groveAt.x >= 0) return _groveAt;
            var w = World;
            bool Fits(int gx, int gz)
            {
                if (gx - 50 < 0 || gz - 50 < 0 || gx + 340 >= w.SX || gz + 300 >= w.SZ) return false;
                int lo = int.MaxValue, hi = int.MinValue;
                for (int z = gz + 40; z < gz + 180; z += 6)
                    for (int x = gx; x < gx + 300; x += 6)
                    {
                        int y = w.SurfAt(x, z);
                        if (Site.WaterTop[z * w.SX + x] > 0) return false;
                        foreach (var p in Site.Ponds) if (Brushes.Dist(x - p.X, z - p.Z) < p.R + 24) return false;
                        lo = Math.Min(lo, y); hi = Math.Max(hi, y);
                    }
                return hi - lo < 40 && !Site.Landmarks.Exists(l => Brushes.Dist(l.X - gx - 150, l.Z - gz - 110) < 150);
            }
            (int x, int z) best = (-1, -1);
            double near = double.MaxValue;
            for (int gz = 50; gz < w.SZ - 300; gz += 20)
                for (int gx = Site.KeepX - 330; gx < Site.KeepX + 40; gx += 20)
                {
                    double d = Brushes.Dist(gx + 150 - Site.KeepX, gz + 110 - Site.KeepZ);
                    if (d > 120 && d < near && Fits(gx, gz)) { near = d; best = (gx, gz); }
                }
            return _groveAt = best.x >= 0 ? best : (Site.KeepX - 150, Site.KeepZ - 330);
        }
    }

    /// <summary>The valley's trees and bushes in three rows, each kind at two sizes, for a look at them.</summary>
    void Grove()
    {
        var w = World;
        var plant = new Action<int, int>[]
        {
            (x, z) => Flora.Pine(w, x, z, 100), (x, z) => Flora.Pine(w, x, z, 72), (x, z) => Flora.Spruce(w, x, z, 92, 3), (x, z) => Flora.Spruce(w, x, z, 60, 2),
            (x, z) => Flora.Oak(w, x, z, 86, 4), (x, z) => Flora.Oak(w, x, z, 62, 4), (x, z) => Flora.Birch(w, x, z, 72), (x, z) => Flora.Birch(w, x, z, 50),
            (x, z) => Flora.Cypress(w, x, z, 64, grain: 1, grown: true), (x, z) => Flora.DeadTree(w, x, z, grown: true, scale: 1.45),
            (x, z) => Flora.Bush(w, x, z, 6), (x, z) => Flora.Bush(w, x, z, 3),
        };
        var (gx, gz) = GroveAt;
        ClearGrove();
        // --look=0..2 plants one row of four for a close look (the log says where to look at each one's foot); without
        // it, all of them in two rows
        if (_look is >= 0 and < 3)
            for (int i = 0; i < 4; i++)
            {
                int x = gx + 40 + i * 72, z = gz + 110;
                plant[_look * 4 + i](x, z);
                float over = (World.WY(w.SurfAt(x, z) + 6) - 4) / MathF.Tan(.42f) / VoxelWorld.Vox;
                GD.Print($"[voxel-tower] tree {i + 1} stands at {x},{z}; its foot: --cam={x - MathF.Sin(-.3f) * over:0},{z - MathF.Cos(-.3f) * over:0},-0.3,0.42,6");
            }
        else for (int i = 0; i < plant.Length; i++) plant[i](gx + i % 6 * 58, gz + i / 6 * 110);
    }

    /// <summary>Takes the trees off the grove's ground (and, for the figures, the grass that would hide their feet).</summary>
    /// <summary>Takes the trees off a stretch of the valley before it is drawn.</summary>
    void ClearTrees(int x0, int z0, int x1, int z1)
    {
        var w = World;
        for (int z = Math.Max(0, z0); z < Math.Min(w.SZ, z1); z++)
            for (int x = Math.Max(0, x0); x < Math.Min(w.SX, x1); x++)
                for (int y = w.SurfAt(x, z); y < w.SY; y++)
                {
                    byte m = w.Get(x, y, z);
                    if (Mat.IsTrunk(m) || Mat.IsLeafy(m)) w.SetRaw(x, y, z, Mat.Air);
                }
    }

    void ClearGrove()
    {
        var w = World;
        var (gx, gz) = GroveAt;
        bool bare = _pose == "people";
        for (int z = gz - 50; z < gz + 300; z++)
            for (int x = gx - 50; x < gx + 340; x++)
                for (int y = w.SurfAt(x, z); y < w.SY; y++)
                {
                    byte m = w.Get(x, y, z);
                    if (Mat.IsTrunk(m) || Mat.IsLeafy(m) || bare && m is Mat.Tuft or Mat.Flower or Mat.Bloom) w.SetRaw(x, y, z, Mat.Air);
                }
    }
}
