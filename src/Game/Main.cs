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
/// Command-line options after "--": --shot=file.png [--frames=N] takes a screenshot and quits (--cam=x,z,yaw,pitch,zoom
/// points the camera first),
/// --demo sets up a fort with pawns at work (--pose=battle: the horde bridging the moat and laddering the wall),
/// --selftest drives the tools and the pawns and checks the results, --calm stops the embers and the flicker,
/// --top looks straight down, --look=N at the Nth named place of the valley, --seed=N makes another valley.
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
    public ZoneView Areas { get; private set; }
    public Commander Command { get; private set; }

    /// <summary>The pawns the player has picked out.</summary>
    public readonly HashSet<Unit> Selected = new();
    /// <summary>Game seconds per real second: 1, 2 or 3.</summary>
    public int Speed = 1;
    public bool Paused;

    const string BlueprintFile = "user://blueprints.json";

    string _shot;
    int _shotFrames = 45, _frames;
    bool _demo, _calm, _selfTest, _finale, _top;
    int _look = -1;
    /// <summary>--cam=x,z,yaw,pitch,zoom: where the camera looks for a screenshot, x and z in voxels.</summary>
    float[] _cam;
    uint _seed = 7;
    string _pose = "";
    /// <summary>How long the staged siege runs before the screenshot (--at=seconds).</summary>
    double _poseSeconds = 14;

    public override void _Ready()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--shot=")) _shot = a["--shot=".Length..];
            else if (a.StartsWith("--frames=") && int.TryParse(a["--frames=".Length..], out int f)) _shotFrames = f;
            else if (a == "--demo") _demo = true;
            else if (a == "--calm") _calm = true;
            else if (a == "--top") _top = true;
            else if (a.StartsWith("--seed=") && uint.TryParse(a["--seed=".Length..], out uint sd)) _seed = sd;
            else if (a.StartsWith("--look=") && int.TryParse(a["--look=".Length..], out int lk)) _look = lk;
            else if (a.StartsWith("--cam=")) _cam = Array.ConvertAll(a["--cam=".Length..].Split(','), v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
            else if (a == "--selftest") _selfTest = true;
            else if (a.StartsWith("--pose=")) _pose = a["--pose=".Length..];
            else if (a.StartsWith("--at=") && double.TryParse(a["--at=".Length..], System.Globalization.CultureInfo.InvariantCulture, out double at)) _poseSeconds = at;
        }

        var sw = Stopwatch.StartNew();
        var mp = new MapParams { Seed = _seed };
        World = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        Site = MapGen.Generate(World, mp);
        World.Compact();
        double genMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        Sim = new Sim(World, Site);
        // in play, where water goes is worked out off the main thread; scripted runs want it at once
        Sim.WaterInBackground = !_selfTest && !_demo && _shot == null;
        // a new game finds a fort half raised round the citadel; scripted runs set up their own scenes
        if (!_selfTest && !_demo) StartingFort.Raise(Sim);
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
        Rig.HomeAt(World.WX(Site.KeepX - Site.KeepR - 50), World.WZ(Site.KeepZ + 14));
        atmo.Follow = Rig;
        Debris = new Debris();
        AddChild(Debris);
        Debris.Init(World);

        Marks = new OrdersView();
        AddChild(Marks);
        Marks.Init(Sim);
        Areas = new ZoneView();
        AddChild(Areas);
        Areas.Init(Sim);
        var flags = new MineView();
        AddChild(flags);
        flags.Init(this);
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
        Sim.Raised = (x, y, z, m) => { if (Random.Shared.NextDouble() < .05) Debris.Spawn(m, x, y, z, 1, .8f); };

        // the household the siege finds at the citadel, the beasts of the valley, and the horde's camp at a mine
        foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Archer, Kinds.Swordsman }) Sim.Spawn(t, Sim.Gate);
        Sim.SeedWildlife();
        if (!_selfTest && !_demo) Sim.SeedMines();

        Hud.Log("Пробел — позвать орду раньше: золота за это нет, зато она меньше узнает о вашей обороне.");
        Hud.Log("Между волнами охотьтесь: выберите лучника и щёлкните ПКМ по зверю — добыча приносит золото.");
        Hud.Log("Кисти размечают работу, пешки её делают: кладут из запасов, копают, рубят. V — кирка, 7 — лестница, 8 — делянка: ресурс с участка.");
        if (!_selfTest && !_demo)
        {
            Hud.Log("Рудники платят золотом, пока они ваши: медный — к востоку от крепости, серебряную жилу в низине стерегут гоблины. " +
                "Пошлите людей к входу. Задачи — над картой.", Hud.Kind.Ok);
            Hud.Log("Старая крепость вокруг цитадели наполовину разрушена: стены проломлены, ворота сорваны с петель. Возьмите починку (0) " +
                "и щёлкните по стене — рабочие разберут завал и вернут ей прежний вид. Рамкой — только проломы. " +
                "Камня на всё не хватит — ломайте его в скалах, валунах и руинах (8 и Z).", Hud.Kind.Warn);
        }
        Hud.Log("Орда придёт с запада через десять минут. Ройте, рубите и стройте, пока есть время.", Hud.Kind.Warn);
        GD.Print($"[voxel-tower] world {genMs:0} ms, navigation {navMs:0} ms, mesh {View.LastFullMeshMs:0} ms, {View.Triangles()} triangles");

        if (_demo) Demo();
        if (_look >= 0 && _look < Site.Landmarks.Count)
        {
            var l = Site.Landmarks[_look];
            Rig.Set(World.WX(l.X), World.WZ(l.Z), -.7f, .6f, 2.4f, snap: true);
        }
        if (_cam is { Length: 5 }) Rig.Set(World.WX(_cam[0]), World.WZ(_cam[1]), _cam[2], _cam[3], _cam[4], snap: true);
        if (_top) Rig.ToggleTopDown();
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
        if (OS.IsDebugBuild())
            foreach (var u in Sim.Units)
            {
                if (u.Team == Team.Wild) continue;
                string field = "";
                if (u.Team == Team.Horde && Sim.Fields.TryGetValue(u.T, out var f))
                {
                    int next = f.Next(Sim.Nav, u.Cell, out float carve, out var works);
                    field = $", field {f.Dist[u.Cell]:0} s, next {next} {works} carve {carve:0.0}, no build until {u.NoBuildUntil:0.0}";
                }
                GD.Print($"[voxel-tower]   {u.T.Name} #{u.Id}: {u.State} {u.Building} at {u.X / VoxelWorld.PerMetre:0.0},{u.Y / VoxelWorld.PerMetre:0.0},{u.Z / VoxelWorld.PerMetre:0.0} m{(u.Why != "" ? ", " + u.Why : "")}{field}");
            }
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

    /// <summary>The column nearest a point (within a radius) that passes a test; the point itself if none does.</summary>
    (int x, int z) Nearest(int cx, int cz, int radius, Func<int, int, bool> test)
    {
        for (int r = 0; r <= radius; r++)
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    int x = cx + dx, z = cz + dz;
                    if (World.InBounds(x, 1, z) && test(x, z)) return (x, z);
                }
        return (cx, cz);
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
        Check("a household at the gate", Sim.Count(Kinds.Worker) == 4 && Sim.Count(Kinds.Archer) == 1 && Sim.Count(Kinds.Swordsman) == 1);
        await Frames(2);
        Check("the pawns and the beasts are drawn", Units.Drawn() >= 5 + Sim.WildAlive() && Sim.WildAlive() >= 15, $"{Units.Drawn()} figures, {Sim.WildAlive()} beasts");
        Check("the river and the ponds are drawn", View.WaterChunks() >= 20, $"{View.WaterChunks()} chunks with water");

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
        var (bx, bz) = Nearest(705, 475, 60, (x, z) => World.Get(x, World.SurfAt(x, z) - 1, z) == Mat.Rock && World.SurfAt(x, z) > Site.HTop[z * World.SX + x] + 3);
        await Look(bx, bz);
        KeyPress(Key.Key1); KeyPress(Key.F);
        Check("the brush becomes a ball", Tools.Tool == Tool.Brush && Tools.Opt.Form == BrushForm.Ball);
        int r1 = Removals();
        await ClickAt(ScreenAt(bx + .5f, World.SurfAt(bx, bz), bz + .5f));
        Check("the ball pick marks rock to break", Removals() > r1 + 50, $"{Removals() - r1} voxels of the boulder at {bx},{bz}");
        KeyPress(Key.F);

        // a tree, felled whole by clicking it
        var (tx, tz) = Nearest(691, 670, 80, (x, z) => Mat.IsTrunk(World.Get(x, World.SurfAt(x, z) + 2, z)));
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
        Check("…lay the wall from the stores", World.SurfAt(lx, 460) > ground + 16, $"top {World.SurfAt(lx, 460)} over ground {ground}, stone {Fmt.M3(stone0)} → {Fmt.M3(Sim.Stock[Stockpile.Stone])} m³");
        Check("…and fell the tree for wood", Sim.Stock[Stockpile.Wood] > wood0 + 100, $"wood +{Fmt.M3(Sim.Stock[Stockpile.Wood] - wood0)} m³, {Sim.Orders.Total} of {left0} voxels left; 150 s in {ms} ms");
        foreach (var u in Sim.Units) if (u.Why != "") GD.Print($"[selftest]      {u.T.Name} #{u.Id}: {u.Why}");

        // a ladder against the new wall, from the side the camera sees
        KeyPress(Key.Key7);
        Check("7 picks up the ladder", Tools.Tool == Tool.Ladder);
        await Look(lx, 460);
        int fy = ground + 10, face = lx;
        while (World.Get(face - 1, fy, 460) != Mat.Air) face--;
        int b2 = Sim.Orders.BuildTotal;
        await ClickAt(ScreenAt(face - .02f, fy + .5f, 460.5f));
        int rungs = Sim.Orders.BuildTotal - b2;
        Advance(40);
        int built = 0;
        for (int y = ground; y < ground + 48; y++) for (int z = 450; z < 472; z++) for (int x = face - 10; x < face; x++) if (World.Get(x, y, z) == Mat.Ladder) built++;
        Check("…the workers set it against the wall", rungs > 40 && built > 40, $"{rungs} voxels marked, {built} in place");

        // a gate through the same wall, further along it
        KeyPress(Key.Key9);
        Check("9 picks up the gate", Tools.Tool == Tool.Gate);
        int gz = 472, gface = lx;
        while (World.Get(gface - 1, fy, gz) != Mat.Air) gface--;
        int gates0 = Sim.Gates.Count;
        await ClickAt(ScreenAt(gface - .02f, fy + .5f, gz + .5f));
        var hung = Sim.Gates.Count > gates0 ? Sim.Gates[^1] : null;
        Advance(60);
        Check("…the workers cut the gateway and hang the gate", hung != null && hung.Ready,
            hung == null ? "no gate planned" : $"leaves on the {(hung.In > 0 ? "east" : "west")}, ready {hung.Ready}");

        // the horde knocks a hole into the new wall: a click with the mending tool lays it again as it was
        int hy = ground + 8, holed = 0;
        var was = new Dictionary<(int x, int y, int z), byte>();
        for (int y = hy; y < hy + 10; y++)
            for (int z = 450; z < 458; z++)
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
        int b3 = Sim.Orders.BuildTotal, wtop = World.SurfAt(lx, 446);
        await ClickAt(ScreenAt(lx + .5f, wtop + .02f, 446.5f));
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

        // marking and unmarking; on the grid, a line drawn askew comes out straight along it
        KeyPress(Key.Key2);
        if (!Tools.Opt.Dig) KeyPress(Key.V);
        KeyPress(Key.N);
        Check("N snaps to the grid", Tools.Opt.Grid == 8);
        await Look(640, 500);
        int r3 = Removals();
        await Drag((641, 441), (646, 558));
        int moat = Removals() - r3;
        int gx = kx + (int)Math.Round((641 - kx) / 8.0) * 8, askew = 0, minZ = int.MaxValue, maxZ = 0;
        for (int z = 420; z < 580; z++)
            for (int x = 600; x < 690; x++)
                for (int y = World.SurfAt(x, z) - 20; y < World.SurfAt(x, z) + 2; y++)
                    if (Sim.Orders.At(x, y, z) == Orders.Remove)
                    {
                        if (Math.Abs(x - gx) > 5) askew++;
                        minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
                    }
        Check("a line with the pick marks a moat, straight along the grid", moat > 5000 && askew == 0,
            $"{moat} voxels, {askew} off the line x = {gx}, z {minZ}…{maxZ}");
        KeyPress(Key.N);
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
        Command.ClearSelection();

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
    /// A fort in the making, set up the way a player would: a curtain wall with round towers and stairs, a moat let in from
    /// the pond (the rest of it marked for the workers), merlons and a wooden walk marked on the wall, a tree to fell,
    /// deer grazing beyond. With --pose=battle the moat is full and the horde is at it, bridging and raising ladders.
    /// </summary>
    void Demo()
    {
        if (_pose == "battle") { Siege(); return; }
        if (_pose == "flood") { Flood(); return; }
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
    /// A pond breached by a cut run out to the cliff: the water pours along it, over the edge and down into the lowland,
    /// and the pond drops (--at=seconds: how long it has been running).
    /// </summary>
    void Flood()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        var (px, pz, pr, _) = s.Ponds[1];
        int cliff = px - pr;
        while (cliff > 0 && w.SurfAt(cliff - 1, pz) > w.SurfAt(cliff, pz) - 12) cliff--;
        sim.InstantLabour = true;
        sim.DesignateRemove(Shapes.Line(w, s, px - MapGen.Shore(w, s, px, pz, -1, 0) + 6, pz, cliff, pz, new Stroke { Width = 6, Height = 16, Dig = true }, 0));
        sim.InstantLabour = false;
        Advance(_poseSeconds);
        Rig.Set(World.WX(cliff + 24), World.WZ(pz + 10), -2.2f, .7f, 2.6f, snap: true);
        Hud.Refresh();
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
        if (_pose is "battle" or "close" or "flood") return;
        int x = 720, z = 640;
        Tools.PointAt(new Vector3(World.WX(x), World.WY(World.SurfAt(x, z)), World.WZ(z)));
    }
}
