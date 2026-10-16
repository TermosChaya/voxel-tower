using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Builds the valley, the simulation, the view, the camera, the tools and the interface, and wires them together.
/// Command-line options after "--": --shot=file.png [--frames=N] takes a screenshot and quits,
/// --demo sets up a fort with pawns at work (--pose=battle: with the horde at the wall),
/// --selftest drives the tools and the pawns and checks the results, --calm stops the embers and the flicker.
/// </summary>
public partial class Main : Node3D
{
    public VoxelWorld World { get; private set; }
    public Site Site { get; private set; }
    public Sim Sim { get; private set; }
    public List<Blueprint> Blueprints { get; } = new();
    public int NextBlueprintId = 1;

    public WorldView View { get; private set; }
    public CameraRig Rig { get; private set; }
    public BuildTools Tools { get; private set; }
    public Hud Hud { get; private set; }
    public Debris Debris { get; private set; }
    public UnitView Units { get; private set; }
    public OrdersView Marks { get; private set; }
    public Commander Command { get; private set; }

    /// <summary>The pawns the player has picked out.</summary>
    public readonly HashSet<Unit> Selected = new();
    /// <summary>Game seconds per real second: 1, 2 or 3.</summary>
    public int Speed = 1;
    public bool Paused;

    const string BlueprintFile = "user://blueprints.json";

    string _shot;
    int _shotFrames = 45, _frames;
    bool _demo, _calm, _selfTest, _finale;
    string _pose = "";

    public override void _Ready()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--shot=")) _shot = a["--shot=".Length..];
            else if (a.StartsWith("--frames=") && int.TryParse(a["--frames=".Length..], out int f)) _shotFrames = f;
            else if (a == "--demo") _demo = true;
            else if (a == "--calm") _calm = true;
            else if (a == "--selftest") _selfTest = true;
            else if (a.StartsWith("--pose=")) _pose = a["--pose=".Length..];
        }

        var sw = Stopwatch.StartNew();
        var mp = new MapParams();
        World = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        Site = MapGen.Generate(World, mp);
        World.Compact();
        double genMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        Sim = new Sim(World, Site);
        double navMs = sw.Elapsed.TotalMilliseconds;

        var atmo = new Atmosphere();
        AddChild(atmo);
        atmo.Build(World, Site, _calm || _shot != null);

        View = new WorldView();
        AddChild(View);
        View.Init(World);

        Rig = new CameraRig { BottomInset = Hud.BottomBand, TopInset = Hud.TopBand };
        Rig.Bounds = new Rect2(World.OriginX, World.OriginZ, World.SX * VoxelWorld.Vox, World.SZ * VoxelWorld.Vox);
        AddChild(Rig);
        Rig.HomeAt(World.WX(Site.KeepX - Site.KeepR - 120), World.WZ(Site.KeepZ + 20));
        atmo.Follow = Rig;
        Debris = new Debris();
        AddChild(Debris);
        Debris.Init(World);

        Marks = new OrdersView();
        AddChild(Marks);
        Marks.Init(Sim);
        Units = new UnitView();
        AddChild(Units);
        Units.Init(this);

        Tools = new BuildTools();
        AddChild(Tools);
        Hud = new Hud();
        AddChild(Hud);
        Command = new Commander();
        AddChild(Command);
        Command.Init(this);
        Tools.Init(this);
        Rig.RightClick += p => { if (Tools.Tool != Tool.None) Tools.Cancel(); else Command.OrderTo(p); };

        if (!_selfTest && !_demo) LoadBlueprints();
        Hud.Init(this);

        Sim.Said += (text, kind) => Hud.Log(text, (Hud.Kind)Math.Clamp(kind, 0, 3));
        Sim.Mined = (x, y, z, m) => { if (Random.Shared.NextDouble() < .045) Debris.Spawn(m, x, y, z, 1, 1.3f); };
        Sim.Hacked = (x, y, z, m) => { if (Random.Shared.NextDouble() < .16) Debris.Spawn(m, x, y, z, 1, 1.8f); };

        // the household the siege finds at the citadel
        foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Archer, Kinds.Swordsman }) Sim.Spawn(t, Sim.Gate);

        Hud.Log("Пробел — позвать орду раньше: оставшееся время станет золотом.");
        Hud.Log("Кисти размечают работу, пешки её делают: кладут из запасов, копают, рубят. V — кирка.");
        Hud.Log("Орда придёт с запада через пять минут. Ройте, рубите и стройте, пока есть время.", Hud.Kind.Warn);
        GD.Print($"[voxel-tower] world {genMs:0} ms, navigation {navMs:0} ms, mesh {View.LastFullMeshMs:0} ms, {View.Triangles()} triangles");

        if (_demo) Demo();
        if (_selfTest) SelfTest();
        // a hidden or minimised window never draws a frame: don't let scripted runs hang
        if (_shot != null || _selfTest) GetTree().CreateTimer(120).Timeout += () => { GD.PrintErr("[voxel-tower] gave up waiting for a frame"); GetTree().Quit(2); };
    }

    double _slowest;

    public override void _Process(double delta)
    {
        if (_frames > 5) _slowest = Math.Max(_slowest, delta);
        if (!Paused && !_selfTest)
        {
            double dt = Math.Min(delta, .1) * Speed;
            while (dt > 1e-6)
            {
                float step = (float)Math.Min(dt, .05);
                Sim.Step(step);
                dt -= step;
            }
        }
        Command.Prune();
        View.RemeshDirty();
        if (Sim.Over && !_finale)
        {
            _finale = true;
            if (Sim.Won) Hud.Finale("Осада снята", $"Долина устояла: {Waves.Total} волн отбито, {Sim.Kills} налётчиков пало.", true);
            else Hud.Finale("Цитадель пала", $"Отбито волн: {Math.Max(0, Sim.Waves.Number - 1)} из {Waves.Total}. Налётчиков пало: {Sim.Kills}.", false);
        }
        if (_shot != null && ++_frames == _shotFrames) TakeShot();
    }

    async void TakeShot()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var vp = GetViewport();
        GD.Print($"[voxel-tower] {Engine.GetFramesPerSecond()} fps, {vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.PrimitivesInFrame) / 1000} k triangles in {vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame)} draw calls (+{vp.GetRenderInfo(Viewport.RenderInfoType.Shadow, Viewport.RenderInfo.PrimitivesInFrame) / 1000} k for shadows), {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576} MB video memory");
        GD.Print($"[voxel-tower] {Sim.Units.Count} units, {Sim.Orders.Total} voxels marked, game time {Sim.Time:0} s, slowest frame {_slowest * 1000:0} ms, {Units.Drawn()} figures and {Units.ArrowsDrawn} arrows drawn");
        if (OS.IsDebugBuild()) foreach (var u in Sim.Units) GD.Print($"[voxel-tower]   {u.T.Name} #{u.Id}: {u.State} at {u.X / 16:0.0},{u.Y / 16:0.0},{u.Z / 16:0.0} m{(u.Why != "" ? ", " + u.Why : "")}");
        var img = GetViewport().GetTexture().GetImage();
        if (img.GetWidth() > 1600) img.Resize(1600, img.GetHeight() * 1600 / img.GetWidth(), Image.Interpolation.Lanczos);
        var err = img.SavePng(_shot);
        GD.Print($"[voxel-tower] screenshot {_shot}: {err}");
        GetTree().Quit();
    }

    // ---------------------------------------------------------------- blueprints

    public void AddBlueprint(Blueprint bp, bool save = true)
    {
        if (bp == null) return;
        Blueprints.Add(bp);
        if (save) SaveBlueprints();
        Hud.RebuildBlueprints();
    }

    public void RemoveBlueprint(Blueprint bp)
    {
        Blueprints.Remove(bp);
        if (Tools.PasteBp == bp) Tools.SetTool(Tool.None);
        SaveBlueprints();
        Hud.RebuildBlueprints();
        Hud.Log($"Свиток «{bp.Name}» сожжён.");
    }

    sealed class BlueprintStore
    {
        public int Seq { get; set; }
        public List<Blueprint> List { get; set; } = new();
    }

    void SaveBlueprints()
    {
        if (_selfTest || _demo) return;   // scripted runs leave the player's blueprints alone
        try
        {
            using var f = Godot.FileAccess.Open(BlueprintFile, Godot.FileAccess.ModeFlags.Write);
            f?.StoreString(JsonSerializer.Serialize(new BlueprintStore { Seq = NextBlueprintId, List = Blueprints }));
        }
        catch (Exception e) { GD.PushWarning($"blueprints not saved: {e.Message}"); }
    }

    bool LoadBlueprints()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(BlueprintFile)) return false;
            using var f = Godot.FileAccess.Open(BlueprintFile, Godot.FileAccess.ModeFlags.Read);
            var store = JsonSerializer.Deserialize<BlueprintStore>(f.GetAsText());
            if (store?.List == null || store.List.Count == 0) return false;
            Blueprints.AddRange(store.List);
            NextBlueprintId = Math.Max(store.Seq, Blueprints.Count + 1);
            return true;
        }
        catch (Exception e) { GD.PushWarning($"blueprints not loaded: {e.Message}"); return false; }
    }

    /// <summary>Runs the simulation for a while at once, without drawing (tests and staged scenes).</summary>
    public void Advance(double seconds)
    {
        for (double t = 0; t < seconds; t += .05) Sim.Step(.05f);
    }

    // ---------------------------------------------------------------- self-test

    /// <summary>
    /// Drives the tools and the commands through their input handlers with synthetic clicks and drags, lets the pawns
    /// work through what was marked, checks the results and quits.
    /// </summary>
    async void SelfTest()
    {
        int fails = 0;
        void Check(string what, bool ok, string detail = "") { GD.Print($"[selftest] {(ok ? "ok  " : "FAIL")} {what} {detail}"); if (!ok) fails++; }
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        Vector2 Screen(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(World.WX(x + .5f), World.WY(World.SurfAt(x, z)), World.WZ(z + .5f)));
        Vector2 ScreenAt(float x, float y, float z) => Rig.Cam.UnprojectPosition(new Vector3(World.WX(x), World.WY(y), World.WZ(z)));
        void Move(Vector2 p) => Tools._UnhandledInput(new InputEventMouseMotion { Position = p });
        void Button(Vector2 p, bool down, bool shift = false) => Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = p, ShiftPressed = shift });
        void KeyPress(Key k) { var e = new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true }; Tools._UnhandledInput(e); Command._UnhandledInput(e); }
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
        Sim.Waves.Countdown = 1e9;
        Sim.Stock.Gold = 500;
        Check("no ready-made blueprints", Blueprints.Count == 0);
        Check("a household at the gate", Sim.Count(Kinds.Worker) == 3 && Sim.Count(Kinds.Archer) == 1 && Sim.Count(Kinds.Swordsman) == 1);
        await Frames(2);
        Check("the pawns are drawn", Units.Drawn() >= 5, $"{Units.Drawn()} figures");

        int w0 = Sim.Count(Kinds.Worker);
        Command.Hire(Kinds.Worker);
        Check("hiring takes gold and brings a worker", Sim.Count(Kinds.Worker) == w0 + 1 && Math.Abs(Sim.Stock.Gold - (500 - Kinds.Worker.Cost)) < .01, $"{Sim.Stock.Gold:0} gold left");

        // a stone wall is marked, not built
        await Look(740, 470);
        KeyPress(Key.Key2); KeyPress(Key.Z);
        Check("2 picks the line, Z the stone", Tools.Tool == Tool.Line && Tools.Opt.Material == Mat.Stone && !Tools.Opt.Dig);
        int lx = 750, ground = World.SurfAt(lx, 460), b0 = Sim.Orders.BuildTotal;
        await Drag((lx, 440), (lx, 480));
        Check("the line marks a wall for the workers", Sim.Orders.BuildTotal > b0 + 1500 && World.SurfAt(lx, 460) == ground,
            $"{Sim.Orders.BuildTotal - b0} voxels marked, ground still at {World.SurfAt(lx, 460)}");

        // the pick: a pit
        KeyPress(Key.V); KeyPress(Key.Key3);
        Check("V takes up the pick", Tools.Opt.Dig && Tools.Tool == Tool.Box);
        int px = 669, pz = 552, r0 = Removals();
        await Look(680, 560);
        await Drag((px, pz), (px + 24, pz + 24));
        int pit = Removals() - r0;
        Check("a box with the pick marks a pit", pit > 2000, $"{pit} voxels to dig");

        // a boulder broken up for stone with the ball of the brush
        await Look(710, 473);
        KeyPress(Key.Key1); KeyPress(Key.F);
        Check("the brush becomes a ball", Tools.Tool == Tool.Brush && Tools.Opt.Form == BrushForm.Ball);
        int r1 = Removals();
        var rockTop = ScreenAt(710.5f, World.SurfAt(710, 473), 473.5f);
        await ClickAt(rockTop);
        Check("the ball pick marks rock to break", Removals() > r1 + 50, $"{Removals() - r1} voxels");
        KeyPress(Key.F);

        // a tree, felled whole by clicking it
        await Look(691, 670);
        int r2 = Removals();
        var trunk = ScreenAt(691.5f, 92, 670.5f);
        await ClickAt(trunk);
        Check("a click with the pick fells a whole tree", Removals() > r2 + 300, $"{Removals() - r2} voxels of tree");

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
        Check("…lay the wall from the stores", World.SurfAt(lx, 460) > ground + 16, $"top {World.SurfAt(lx, 460)} over ground {ground}, stone {Fmt.M3(stone0)} → {Fmt.M3(Sim.Stock[Stockpile.Stone])} m³");
        Check("…and fell the tree for wood", Sim.Stock[Stockpile.Wood] > wood0 + 100, $"wood +{Fmt.M3(Sim.Stock[Stockpile.Wood] - wood0)} m³, {Sim.Orders.Total} of {left0} voxels left; 150 s in {ms} ms");
        foreach (var u in Sim.Units) if (u.Why != "") GD.Print($"[selftest]      {u.T.Name} #{u.Id}: {u.Why}");

        // marking and unmarking
        KeyPress(Key.Key2);
        await Look(640, 500);
        int r3 = Removals();
        await Drag((640, 440), (640, 560));
        int moat = Removals() - r3;
        Check("a line with the pick marks a moat", moat > 5000, $"{moat} voxels");
        KeyPress(Key.Key6);
        Check("6 picks up the unmarking tool", Tools.Tool == Tool.Unmark);
        await Drag((630, 430), (652, 570));
        Check("a frame takes the marks back", Removals() <= r3 + 10, $"{Removals() - r3} left of {moat}");

        // capture what the workers built, mark a copy
        KeyPress(Key.Escape); KeyPress(Key.Z);
        await Look(750, 460);
        KeyPress(Key.B);
        await Drag((lx - 10, 436), (lx + 10, 484));
        Check("capture makes a blueprint of the new wall", Blueprints.Count == 1, Blueprints.Count > 0 ? Blueprints[0].Describe() : "");
        if (Blueprints.Count > 0)
        {
            Tools.StartPaste(Blueprints[0]);
            KeyPress(Key.R);
            Check("R turns the copy", Tools.PasteRot == 1);
            int b1 = Sim.Orders.BuildTotal;
            await Click(780, 420);
            Check("the copy is marked for building", Sim.Orders.BuildTotal > b1 + 500, $"{Sim.Orders.BuildTotal - b1} voxels");
        }
        KeyPress(Key.Escape);
        Check("Esc drops the tool", Tools.Tool == Tool.None);

        // choosing and sending pawns
        Unit worker = null;
        foreach (var u in Sim.Units) if (!u.Dead && u.T == Kinds.Worker) { worker = u; break; }
        await Look(worker.X, worker.Z);
        await ClickAt(Command.Screen(worker, .5f));
        Check("a click picks a pawn", Selected.Count == 1 && Selected.Contains(worker), $"{Selected.Count} chosen");
        var dest = Screen(kx - 60, kz + 60);
        Rig._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = dest });
        Rig._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = dest });
        Check("a right click sends it and it stays", worker.Hold && worker.Path != null, $"hold {worker.Hold}, path {worker.Path?.Count}");
        Advance(15);
        var (fx, _, fz) = Sim.Nav.Feet(worker.Cell);
        Check("…and it gets there", MathF.Abs(fx - (kx - 60)) < 24 && MathF.Abs(fz - (kz + 60)) < 24, $"at {fx:0},{fz:0}");
        Command.ReleaseSelected();
        Check("«За работу» lets it go", !worker.Hold);
        Command.ClearSelection();

        // the horde called early
        Sim.Waves.Countdown = 200;
        double gold0 = Sim.Stock.Gold;
        KeyPress(Key.Space);
        Check("Space calls the horde for a bounty", Sim.Waves.Active && Sim.Waves.Number == 1 && Sim.Stock.Gold >= gold0 + 39, $"+{Sim.Stock.Gold - gold0:0} gold");
        Advance(90);
        Check("the first wave comes and fights", Sim.Kills > 0 && Sim.Shots > 0, $"{Sim.Kills} killed, {Sim.RaidersAlive()} alive, {Sim.Shots} arrows, citadel {Sim.KeepHp:0}");
        KeyPress(Key.P);
        Check("P pauses", Paused);
        KeyPress(Key.Tab);
        Check("Tab changes the speed", Speed == 2 && !Paused);
        Speed = 1;

        var before = Rig.Target;
        Hud.Map.Jump(new Vector2(10, 10));
        await Frames(2);
        Check("map click moves the view", Rig.Target.DistanceTo(before) > 1, $"{before} → {Rig.Target}");
        await Frames(10);
        Check("chunks re-meshed", World.DirtyCount == 0, $"{View.Triangles()} triangles");
        GD.Print($"[selftest] {(fails == 0 ? "all passed" : fails + " failed")}");
        if (_shot == null) GetTree().Quit(fails == 0 ? 0 : 1);
    }

    // ---------------------------------------------------------------- demo

    /// <summary>
    /// A fort in the making, set up the way a player would: a finished curtain wall with round towers and stairs,
    /// a wooden walk and a moat marked out, a tree to fell, and the pawns at it. With --pose=battle the horde is at the wall.
    /// </summary>
    void Demo()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = Waves.Prep;
        sim.Stock.Gold = 140;
        sim.Stock[Stockpile.Stone] = 30 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] = 14 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Earth] = 6 * VoxelWorld.PerM3;
        Stroke St(byte m, int width, int height, bool hollow = false) => new() { Mat = m, Width = width, Height = height, Level = true, Hollow = hollow };
        const int wx = 676, z0 = 436, z1 = 596;

        // what was built before the first wave: the wall, two towers, stairs up from the yard
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
        sim.InstantLabour = false;

        // what is marked for the pawns now: merlons and a wooden walk on the wall, a moat before it, a tree to fell
        for (int z = z0 + 4; z <= z1 - 8; z += 12) sim.DesignateBuild(Shapes.Box(w, s, wx - 6, z, wx - 3, z + 5, St(Mat.Stone, 4, 10), top + 10));
        sim.DesignateBuild(Shapes.Line(w, s, wx + 2, z0, wx + 2, z1, St(Mat.Wood, 6, 3), top + 3));
        sim.DesignateRemove(Shapes.Line(w, s, wx - 28, z0 - 20, wx - 28, z1 + 20, new Stroke { Width = 16, Height = 20, Dig = true }, 0));
        sim.DesignateRemove(Shapes.Tree(w, 691, 92, 670));

        // more hands, and the archers up on the wall
        for (int i = 0; i < 4; i++) sim.Spawn(Kinds.Worker, sim.Gate);
        var archers = new List<Unit> { sim.Spawn(Kinds.Archer, sim.Gate) };
        foreach (var u in sim.Units) if (u.T == Kinds.Archer && !archers.Contains(u)) archers.Add(u);
        var swords = new List<Unit>();
        foreach (var u in sim.Units) if (u.T == Kinds.Swordsman) swords.Add(u);
        swords.Add(sim.Spawn(Kinds.Swordsman, sim.Gate));
        int walk = sim.NearestStand(wx, 510, 4), walk2 = sim.NearestStand(wx, 560, 4);
        if (walk >= 0) sim.Command(archers.GetRange(0, 1), walk);
        if (walk2 >= 0) sim.Command(archers.GetRange(1, archers.Count - 1), walk2);
        int yard = sim.NearestStand(wx + 30, 500, 4);
        if (yard >= 0) sim.Command(swords, yard);
        Advance(_pose == "battle" ? 40 : 55);

        if (_pose == "battle")
        {
            // the horde already at the wall: goblins and orcs hacking, a miner digging under, a troll behind
            sim.Waves.Countdown = 0;
            sim.Step(.05f);
            sim.Waves.Countdown = 1e9;
            int n = 0;
            foreach (var (t, x, z) in new[] { (Kinds.Orc, wx - 44, 500), (Kinds.Goblin, wx - 40, 520), (Kinds.Goblin, wx - 46, 480), (Kinds.Orc, wx - 50, 548), (Kinds.Miner, wx - 42, 530), (Kinds.Goblin, wx - 60, 560), (Kinds.Troll, wx - 80, 512) })
            {
                int c = sim.NearestStand(x, z, t.Move.Height);
                if (c >= 0) { sim.Spawn(t, c); n++; }
            }
            Advance(9);
            Rig.Set(World.WX(wx - 18), World.WZ(518), -.9f, .6f, 1.25f, snap: true);
        }
        else if (_pose == "close") Rig.Set(World.WX(wx - 22), World.WZ(526), -.75f, .55f, 3.4f, snap: true);
        else Rig.Set(World.WX(wx + 6), World.WZ(560), -.7f, .62f, 1.15f, snap: true);

        AddBlueprint(Blueprint.Capture(w, wx - 16, z1 - 6, wx + 16, z1 + 26, NextBlueprintId++, "Круглая башня"), save: false);
        AddBlueprint(Blueprint.Capture(w, wx - 7, 500, wx + 7, 530, NextBlueprintId++, "Пролёт стены"), save: false);
        Hud.Refresh();
        Marks.Flush();

        if (_pose == "line") { Tools.SetTool(Tool.Line); Tools.SetDig(true); }
        else if (_pose is not ("battle" or "close")) Tools.StartPaste(Blueprints[0]);
        foreach (var u in archers) Selected.Add(u);
        CallDeferred(MethodName.PoseDemo);
    }

    /// <summary>Points the cursor for the screenshot: a blueprint copy beside the fort, or a moat half drawn.</summary>
    void PoseDemo()
    {
        Vector2 At(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(World.WX(x + .5f), World.WY(World.SurfAt(x, z)), World.WZ(z + .5f)));
        if (_pose == "line")
        {
            var a = At(612, 470); var b = At(616, 600);
            Tools._UnhandledInput(new InputEventMouseMotion { Position = a });
            Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a });
            for (int i = 1; i <= 6; i++) Tools._UnhandledInput(new InputEventMouseMotion { Position = a.Lerp(b, i / 6f) });
            return;
        }
        if (_pose is "battle" or "close") return;
        int x = 720, z = 640;
        Tools.PointAt(new Vector3(World.WX(x), World.WY(World.SurfAt(x, z)), World.WZ(z)));
    }
}
