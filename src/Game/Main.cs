using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Builds the map, the view, the camera, the tools and the interface, and wires them together.
/// Command-line options after "--": --shot=file.png [--frames=N] takes a screenshot and quits,
/// --demo draws a small fort with the brushes, --selftest drives every tool and checks the results,
/// --calm stops the embers and the flicker.
/// </summary>
public partial class Main : Node3D
{
    public VoxelWorld World { get; private set; }
    public Site Site { get; private set; }
    public Wallet Wallet { get; } = new();
    public List<Blueprint> Blueprints { get; } = new();
    public int NextBlueprintId = 1;

    public WorldView View { get; private set; }
    public CameraRig Rig { get; private set; }
    public BuildTools Tools { get; private set; }
    public Hud Hud { get; private set; }
    public Debris Debris { get; private set; }

    const string BlueprintFile = "user://blueprints.json";

    string _shot;
    int _shotFrames = 45, _frames;
    bool _demo, _calm, _selfTest;
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
        World = new VoxelWorld(WorldGen.SizeX, WorldGen.SizeY, WorldGen.SizeZ);
        Site = WorldGen.Generate(World, starterFort: false);
        World.Compact();
        double genMs = sw.Elapsed.TotalMilliseconds;

        var atmo = new Atmosphere();
        AddChild(atmo);
        atmo.Build(World, Site, _calm || _shot != null);

        View = new WorldView();
        AddChild(View);
        View.Init(World);

        Rig = new CameraRig { BottomInset = Hud.BottomBand, TopInset = Hud.TopBand };
        AddChild(Rig);
        Debris = new Debris();
        AddChild(Debris);
        Debris.Init(World);

        Tools = new BuildTools();
        AddChild(Tools);
        Hud = new Hud();
        AddChild(Hud);
        Tools.Init(this);
        Rig.RightClick += _ => Tools.Cancel();

        if (!_selfTest && !_demo) LoadBlueprints();
        Hud.Init(this);

        Hud.Log("Цена — за каждый воксель: камень 10 зол. за м³, брёвна 5, земля — из рва.");
        Hud.Log("Нарисованное снимается в чертёж (B) и ставится снова одним щелчком.");
        Hud.Log("Готовых построек нет: стены, башни и лестницы рисуются кистями.", Hud.Kind.Warn);
        GD.Print($"[voxel-tower] world {genMs:0} ms, mesh {View.LastFullMeshMs:0} ms, {View.Triangles()} triangles");

        if (_demo) Demo();
        if (_selfTest) SelfTest();
        // a hidden or minimised window never draws a frame: don't let scripted runs hang
        if (_shot != null || _selfTest) GetTree().CreateTimer(90).Timeout += () => { GD.PrintErr("[voxel-tower] gave up waiting for a frame"); GetTree().Quit(2); };
    }

    public override void _Process(double delta)
    {
        View.RemeshDirty();
        if (_shot != null && ++_frames == _shotFrames) TakeShot();
    }

    async void TakeShot()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
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

    // ---------------------------------------------------------------- self-test

    /// <summary>Drives the tools through their input handler with synthetic clicks and drags, checks the results, quits.</summary>
    async void SelfTest()
    {
        int fails = 0;
        void Check(string what, bool ok, string detail = "") { GD.Print($"[selftest] {(ok ? "ok  " : "FAIL")} {what} {detail}"); if (!ok) fails++; }
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        Vector2 Screen(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(World.WX(x + .5f), World.WY(World.SurfAt(x, z)), World.WZ(z + .5f)));
        void Move(Vector2 p) => Tools._UnhandledInput(new InputEventMouseMotion { Position = p });
        void Button(Vector2 p, bool down, bool shift = false) => Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = p, ShiftPressed = shift });
        void KeyPress(Key k) => Tools._UnhandledInput(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true });
        async Task Click(int x, int z) { var p = Screen(x, z); Move(p); await Frames(2); Button(p, true); Button(p, false); await Frames(2); }
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

        await Frames(5);
        Wallet.Gold = 9000; Wallet.Earth = 600;
        Check("no ready-made blueprints", Blueprints.Count == 0);

        KeyPress(Key.Key2); KeyPress(Key.Z);
        Check("2 picks the line, Z the stone", Tools.Tool == Tool.Line && Tools.Opt.Material == Mat.Stone);
        double g0 = Wallet.Gold;
        int lt = Shapes.LevelTop(World, 120, 60, Tools.Opt.Height);
        await Drag((120, 60), (120, 110));
        Check("line draws a level wall", Wallet.Gold < g0 && World.SurfAt(120, 85) == lt, $"{g0 - Wallet.Gold:0} gold, top {World.SurfAt(120, 85)} of {lt}");

        KeyPress(Key.X);
        for (int i = 0; i < 4; i++) KeyPress(Key.Minus);
        Check("− lowers the height", Tools.Opt.Height == 8, Tools.Opt.Height.ToString());
        await Drag((120, 64), (120, 106));
        Check("a wooden course on the wall top", World.Get(120, World.SurfAt(120, 85) - 1, 85) == Mat.Wood, Mat.NameOf(World.Get(120, World.SurfAt(120, 85) - 1, 85)));
        for (int i = 0; i < 4; i++) KeyPress(Key.Equal);

        KeyPress(Key.Key3); KeyPress(Key.F); KeyPress(Key.Z);
        int g = World.SurfAt(155, 155);
        await Drag((140, 140), (170, 170));
        Check("hollow rectangle: walls round an open yard", World.SurfAt(155, 155) == g && World.SurfAt(141, 155) > g + 20, $"yard {World.SurfAt(155, 155)}, wall {World.SurfAt(141, 155)}, ground {g}, built {Footprint(120, 130, 190, 190, g + 20)}");

        KeyPress(Key.Key4);
        Check("circle keeps the hollow fill", Tools.Opt.Hollow);
        g = World.SurfAt(60, 170);
        await Drag((60, 170), (74, 170));
        int ringTop = World.SurfAt(73, 170);
        Check("ring of a round tower", World.SurfAt(60, 170) == g && ringTop > g + 20, $"centre {World.SurfAt(60, 170)}, ring {ringTop}");

        KeyPress(Key.Key5);
        await Click(73, 170);
        Check("extrude raises the ring", World.SurfAt(73, 170) == ringTop + Tools.Opt.Step && World.SurfAt(47, 170) == ringTop + Tools.Opt.Step, $"{World.SurfAt(73, 170)}");

        KeyPress(Key.Key1);
        g0 = Wallet.Gold;
        await Drag((40, 60), (50, 70), (60, 60));
        Check("freehand column stroke", Wallet.Gold < g0 && World.SurfAt(50, 70) > World.GroundTop(50, 70) + 20, $"{g0 - Wallet.Gold:0} gold, built {Footprint(20, 40, 80, 90, World.GroundTop(50, 70) + 20)}");
        KeyPress(Key.F);
        Check("F turns the brush into a ball", Tools.Opt.Form == BrushForm.Ball);
        g0 = Wallet.Gold;
        await Click(200, 40);
        Check("ball dab", Wallet.Gold <= g0, $"{g0 - Wallet.Gold:0} gold");
        KeyPress(Key.F); KeyPress(Key.X);
        Check("then into repaint", Tools.Opt.Form == BrushForm.Repaint);
        int face = 120; while (World.Get(face - 1, World.GroundTop(120, 75) + 12, 75) != 0) face--;
        int fy = World.GroundTop(120, 75) + 12;
        var side = Rig.Cam.UnprojectPosition(new Vector3(World.WX(face) - .01f, World.WY(fy + .5f), World.WZ(75.5f)));
        Move(side); await Frames(2); Button(side, true); Button(side, false); await Frames(2);
        Check("repaint turns the stone face into logs", World.Get(face, fy, 75) == Mat.Wood, $"{Mat.NameOf(World.Get(face, fy, 75))} at x {face}");
        KeyPress(Key.F);

        KeyPress(Key.Key6);
        g0 = Wallet.Gold;
        await Click(120, 95);
        Check("eraser refunds", Wallet.Gold > g0, $"+{Wallet.Gold - g0:0.00} gold");

        KeyPress(Key.Key7);
        double e0 = Wallet.Earth;
        await Drag((70, 70), (100, 70));
        Check("spade yields earth", Wallet.Earth > e0 + .05, $"+{Wallet.Earth - e0:0.00} m³");

        KeyPress(Key.B);
        await Drag((44, 154), (76, 186));
        Check("capture makes a blueprint", Blueprints.Count == 1, Blueprints.Count > 0 ? Blueprints[0].Describe() : "");
        if (Blueprints.Count > 0)
        {
            Tools.StartPaste(Blueprints[0]);
            KeyPress(Key.R);
            Check("R turns the copy", Tools.PasteRot == 1);
            g0 = Wallet.Gold;
            await Click(200, 120);
            Check("paste builds the copy", Wallet.Gold < g0, $"{g0 - Wallet.Gold:0} gold");
        }
        KeyPress(Key.Escape);
        Check("Esc drops the tool", Tools.Tool == Tool.None);

        var before = Rig.Target;
        Hud.Map.Jump(new Vector2(10, 10));
        await Frames(2);
        Check("map click moves the view", Rig.Target.DistanceTo(before) > 1, $"{before} → {Rig.Target}");
        await Frames(10);
        Check("chunks re-meshed", World.DirtyCount == 0, $"{View.Triangles()} triangles");
        GD.Print($"[selftest] {(fails == 0 ? "all passed" : fails + " failed")}");
        if (_shot == null) GetTree().Quit(fails == 0 ? 0 : 1);
    }

    /// <summary>Bounds of the columns in a box that stand above a height: what a stroke actually built.</summary>
    string Footprint(int x0, int z0, int x1, int z1, int above)
    {
        int ax = int.MaxValue, az = int.MaxValue, bx = int.MinValue, bz = int.MinValue, n = 0;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                if (World.SurfAt(x, z) > above) { n++; ax = Math.Min(ax, x); az = Math.Min(az, z); bx = Math.Max(bx, x); bz = Math.Max(bz, z); }
        return n == 0 ? "nothing" : $"x {ax}..{bx}, z {az}..{bz}, {n} columns";
    }

    // ---------------------------------------------------------------- demo

    /// <summary>A small fort drawn with the brushes, the way a player would: walls, towers, merlons, stairs, a moat.</summary>
    void Demo()
    {
        Wallet.Gold = 9000; Wallet.Earth = 900;
        var w = World; var s = Site;
        int wx = s.WallX;
        void Lay(VoxelList L) => Builder.TryBuild(w, Wallet, L, out _);
        Stroke St(byte m, int width, int height, bool hollow = false) => new() { Mat = m, Width = width, Height = height, Level = true, Hollow = hollow };

        // curtain wall: rammed earth core faced with stone, then a wooden walk on top
        int top = Shapes.LevelTop(w, wx, 100, 40);
        Lay(Shapes.Line(w, s, wx, 34, wx, 178, St(Mat.Stone, 16, 40), top));
        Lay(Shapes.Line(w, s, wx, 34, wx, 178, St(Mat.Wood, 16, 2), top + 2));
        for (int z = 38; z <= 172; z += 12) Lay(Shapes.Box(w, s, wx - 7, z, wx - 4, z + 5, St(Mat.Stone, 4, 10), top + 2 + 10));

        // a round tower at each end, a wooden hoarding raised on the south one
        foreach (int tz in new[] { 24, 190 })
        {
            int tt = Shapes.LevelTop(w, wx + 2, tz, 64);
            Lay(Shapes.Circle(w, s, wx + 2, tz, 14, St(Mat.Stone, 4, 64, hollow: true), tt));
            int core = Shapes.LevelTop(w, wx + 2, tz, 48);
            Lay(Shapes.Circle(w, s, wx + 2, tz, 10, St(Mat.Rammed, 1, 48), core));
            Lay(Shapes.Circle(w, s, wx + 2, tz, 10, St(Mat.Wood, 1, 4), core + 4));
        }
        var hit = new VoxelHit { X = wx + 2 + 13, Y = w.SurfAt(wx + 15, 190) - 1, Z = 190, NY = 1 };
        hit.M = w.Get(hit.X, hit.Y, hit.Z);
        Lay(Shapes.Extrude(w, s, hit, 6, Mat.Wood, 6000, out _));

        // stairs down from the walk on the yard side: lines a step lower each time
        for (int k = 0; k < 5; k++)
            Lay(Shapes.Line(w, s, wx + 12 + k * 8, 100, wx + 12 + k * 8, 112, St(Mat.Stone, 8, 0), top - k * 8));

        // a moat in front of the wall
        for (int z = 40; z <= 172; z += 4) Builder.Dig(w, s, Wallet, wx - 20, z, w.SurfAt(wx - 20, z) - 16, 5, out _);

        AddBlueprint(Blueprint.Capture(w, wx - 12, 176, wx + 16, 204, NextBlueprintId++, "Круглая башня"), save: false);
        AddBlueprint(Blueprint.Capture(w, wx - 9, 60, wx + 9, 84, NextBlueprintId++, "Пролёт стены с зубцами"), save: false);
        Wallet.Gold = 420; Wallet.Earth = 24;
        Hud.Refresh();

        if (_pose == "line") { Tools.SetTool(Tool.Line); Tools.SetMaterial(Mat.Wood); }
        else Tools.StartPaste(Blueprints[0]);
        CallDeferred(MethodName.PoseDemo);
    }

    /// <summary>Points the cursor for the screenshot: a blueprint copy on the plateau, or a palisade line half drawn.</summary>
    void PoseDemo()
    {
        Vector2 At(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(World.WX(x + .5f), World.WY(World.SurfAt(x, z)), World.WZ(z + .5f)));
        if (_pose == "line")
        {
            var a = At(150, 150); var b = At(196, 112);
            Tools._UnhandledInput(new InputEventMouseMotion { Position = a });
            Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a });
            for (int i = 1; i <= 6; i++) Tools._UnhandledInput(new InputEventMouseMotion { Position = a.Lerp(b, i / 6f) });
            return;
        }
        int x = 292, z = 186;
        Tools.PointAt(new Vector3(World.WX(x), World.WY(World.SurfAt(x, z)), World.WZ(z)));
    }
}
