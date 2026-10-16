using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Builds the map, the view, the camera, the tools and the panel, and wires them together.
/// Command-line options after "--": --shot=file.png [--frames=N] takes a screenshot and quits,
/// --demo builds a few things to look at, --calm stops the embers and the flicker.
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

    public override void _Ready()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--shot=")) _shot = a["--shot=".Length..];
            else if (a.StartsWith("--frames=") && int.TryParse(a["--frames=".Length..], out int f)) _shotFrames = f;
            else if (a == "--demo") _demo = true;
            else if (a == "--calm") _calm = true;
            else if (a == "--selftest") _selfTest = true;
        }

        var sw = Stopwatch.StartNew();
        World = new VoxelWorld(WorldGen.SizeX, WorldGen.SizeY, WorldGen.SizeZ);
        Site = WorldGen.Generate(World);
        World.Compact();
        double genMs = sw.Elapsed.TotalMilliseconds;

        var atmo = new Atmosphere();
        AddChild(atmo);
        atmo.Build(World, Site, _calm || _shot != null);

        View = new WorldView();
        AddChild(View);
        View.Init(World);

        Rig = new CameraRig { PanelWidth = Hud.Width };
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

        if (!LoadBlueprints())
        {
            int wx = Site.WallX;
            AddBlueprint(Blueprint.Capture(World, wx - 12, 100, wx + 11, 147, NextBlueprintId++, "Пролёт вала, 3 м"), save: false);
            AddBlueprint(Blueprint.Capture(World, wx - 12, 4, wx + 16, 32, NextBlueprintId++, "Угловая башня"), save: true);
        }
        Hud.Init(this);

        Hud.Log("Пока это только стройка: орду, стражу и разрушения переношу следующими шагами.", Hud.Kind.Warn);
        Hud.Log($"Мир собран за {Fmt.Int(genMs)} мс, сетка из {Fmt.Int(View.Triangles() / 1000.0)} тыс. треугольников — за {Fmt.Int(View.LastFullMeshMs)} мс на всех ядрах.");
        Hud.Log("Готов вал: снаружи камень, внутри земля, сверху деревянные зубцы. Каменные ступени ведут на галерею.");
        GD.Print($"[voxel-tower] world {genMs:0} ms, mesh {View.LastFullMeshMs:0} ms, {View.Triangles()} triangles");

        if (_demo) Demo();
        if (_selfTest) SelfTest();
        // a hidden or minimised window never draws a frame: don't let scripted runs hang
        if (_shot != null || _selfTest) GetTree().CreateTimer(60).Timeout += () => { GD.PrintErr("[voxel-tower] gave up waiting for a frame"); GetTree().Quit(2); };
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
        if (Hud.IsInsideTree()) Hud.RebuildBlueprints();
    }

    public void RemoveBlueprint(Blueprint bp)
    {
        Blueprints.Remove(bp);
        if (Tools.PasteBp == bp) Tools.SetTool(Tool.None);
        SaveBlueprints();
        Hud.RebuildBlueprints();
        Hud.Log($"Чертёж «{bp.Name}» удалён.");
    }

    sealed class BlueprintStore
    {
        public int Seq { get; set; }
        public List<Blueprint> List { get; set; } = new();
    }

    void SaveBlueprints()
    {
        if (_selfTest || _demo) return;   // test runs leave the player's blueprints alone
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
        async System.Threading.Tasks.Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        Vector2 Screen(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(World.WX(x + .5f), World.WY(World.SurfAt(x, z)), World.WZ(z + .5f)));
        void Move(Vector2 p) => Tools._UnhandledInput(new InputEventMouseMotion { Position = p });
        void Button(Vector2 p, bool down, bool shift = false) => Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = p, ShiftPressed = shift });
        void KeyPress(Key k) => Tools._UnhandledInput(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true });
        async System.Threading.Tasks.Task Click(int x, int z) { var p = Screen(x, z); Move(p); await Frames(2); Button(p, true); Button(p, false); await Frames(2); }
        async System.Threading.Tasks.Task Drag(int x0, int z0, int x1, int z1) { var a = Screen(x0, z0); var b = Screen(x1, z1); Move(a); Button(a, true); for (int i = 1; i <= 8; i++) { Move(a.Lerp(b, i / 8f)); await Frames(1); } Button(b, false); await Frames(2); }

        await Frames(5);
        Wallet.Gold = 5000; Wallet.Earth = 400;
        KeyPress(Key.Key1);
        Check("key 1 picks the wall", Tools.Tool == Tool.Wall);
        double g0 = Wallet.Gold;
        await Drag(120, 60, 120, 110);
        Check("wall drag builds and pays", Wallet.Gold < g0, $"{g0 - Wallet.Gold:0} gold");
        KeyPress(Key.Key2);
        g0 = Wallet.Gold;
        Move(Screen(150, 150)); await Frames(3);
        Check("tower ghost shows its price", Tools.PlanText != null && Tools.PlanText.StartsWith("Башня"), Tools.PlanText ?? "no plan");
        await Click(150, 150);
        Check("tower click builds", Wallet.Gold < g0, $"{g0 - Wallet.Gold:0} gold");
        KeyPress(Key.Key6);
        double e0 = Wallet.Earth;
        Move(Screen(58, 40)); await Frames(1);
        string onBoulder = Tools.HoverText;
        await Drag(58, 40, 58, 46);
        Check("spades do not cut the boulder", Wallet.Earth - e0 < .02, $"{onBoulder}, +{Wallet.Earth - e0:0.00} m³");
        e0 = Wallet.Earth;
        await Drag(70, 70, 100, 70);
        Check("dig stroke yields earth", Wallet.Earth > e0 + .1, $"+{Wallet.Earth - e0:0.00} m³");
        KeyPress(Key.Key7);
        e0 = Wallet.Earth;
        await Click(40, 100);
        Check("fill spends earth", Wallet.Earth < e0, $"-{e0 - Wallet.Earth:0.00} m³");
        int n0 = Blueprints.Count;
        KeyPress(Key.B);
        await Drag(110, 55, 130, 115);
        Check("capture makes a blueprint", Blueprints.Count == n0 + 1, Blueprints.Count > n0 ? Blueprints[^1].Describe() : "");
        if (Blueprints.Count > n0)
        {
            Tools.StartPaste(Blueprints[^1]);
            KeyPress(Key.R);
            Check("R turns the paste", Tools.PasteRot == 1);
            g0 = Wallet.Gold;
            await Click(80, 150);
            Check("paste builds the copy", Wallet.Gold < g0, $"{g0 - Wallet.Gold:0} gold");
        }
        KeyPress(Key.Key8);
        g0 = Wallet.Gold;
        await Click(120, 85);
        Check("remove refunds", Wallet.Gold > g0, $"+{Wallet.Gold - g0:0.0} gold");
        KeyPress(Key.Key5);
        g0 = Wallet.Gold;
        await Click(200, 30);
        Check("brush lays stone", Wallet.Gold < g0 || Wallet.Gold == g0, $"{g0 - Wallet.Gold:0} gold");
        KeyPress(Key.Escape);
        Check("Esc drops the tool", Tools.Tool == Tool.None);
        await Frames(10);
        Check("chunks re-meshed", World.DirtyCount == 0, $"{View.Triangles()} triangles");
        GD.Print($"[selftest] {(fails == 0 ? "all passed" : fails + " failed")}");
        if (_shot == null) GetTree().Quit(fails == 0 ? 0 : 1);
    }

    // ---------------------------------------------------------------- demo

    /// <summary>A few builds for screenshots: the southern gap closed in stone, a moat, a pasted tower, a palisade below.</summary>
    void Demo()
    {
        Wallet.Gold = 3000; Wallet.Earth = 300;
        int wx = Site.WallX;
        Builder.TryBuild(World, Wallet, Brushes.Wall(World, Site, wx, 184, wx, 223, Brushes.StoneWall), out _);
        for (int z = 30; z <= 160; z += 5) Builder.Dig(World, Site, Wallet, wx - 18, z, World.SurfAt(wx - 18, z) - 16, 6, out _);
        var tower = Blueprints.Find(b => b.Name == "Угловая башня");
        if (tower != null) Builder.TryBuild(World, Wallet, tower.PlanPaste(World, Site, wx - 12, 82, 0, World.GroundTop(wx + 2, 96)), out _);
        Builder.TryBuild(World, Wallet, Brushes.Wall(World, Site, 120, 150, 170, 186, Brushes.Palisade), out _);
        Wallet.Gold = 420; Wallet.Earth = 24;
        Hud.Refresh();
        // a tower waiting to be placed on the plateau, shown as the ghost the player sees
        Tools.SetTool(Tool.Tower);
        CallDeferred(MethodName.PointDemoGhost);
    }

    void PointDemoGhost()
    {
        int x = 292, z = 178;
        Tools.PointAt(new Vector3(World.WX(x), World.WY(World.SurfAt(x, z)), World.WZ(z)));
    }
}
