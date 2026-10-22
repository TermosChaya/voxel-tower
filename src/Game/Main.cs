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
/// --demo sets up a fort with pawns at work (--pose=battle: the horde bridging the moat and laddering the wall;
/// --pose=faith: priests at prayer on the barrow, --pose=captives: a rite at the altar there, prisoners bound and led,
/// --pose=spells: the spells at work at night under the red moon (--look=1 a bolt of lightning, close), --pose=shaman:
/// a goblin shaman cursing a wall of ours, --pose=blast: a wall of ours blown up, its pieces in the air (--look=1 coming
/// down, --look=2 what is left), --pose=grip: a ball of earth held by telekinesis over a band, --pose=wind: the black wind
/// throwing a band off our wall with its ladders, --pose=hexes: the fog of the dead, the plague, the blood oath and a stone
/// skin at night, --pose=sorcerers: a witch with our torches put out and a necromancer with his thralls, at night,
/// --pose=powder: kegs of powder before our wall going up one after another under a band (--look=1 a trail of it
/// burning across the field), --pose=charge: a sapper's charge against our ring of stone, its fuse burning (--look=1 the
/// moment it goes, --look=2 a swordsman of ours running for the fuse), --pose=stones: a catapult's stone high on its
/// arc (--look=1 striking the wall), --pose=topple: a block come down on a narrow post with its weight off it, going
/// over the post's edge (--look=1 standing on its end beside it after), --pose=roll: a ball of earth torn out by
/// telekinesis rolling down a slope into a band (--look=1 lying at the foot after, the band knocked about, --look=2 left
/// running, its trail of dust),
/// --pose=crush: a ball of earth let fall on the horde's deck over a ditch, breaking through it with the band on it
/// (--look=1 after: the hole, the ball and the band down in the ditch), --pose=drops: stones and logs tipped off a wall
/// on a hill onto the band at its foot, rolling down the slope through the rest of it (--look=1 lying at the bottom after),
/// --pose=trees: the valley's trees in a row, --look=0..2 choosing which; --pose=grass: a meadow, --look=0..3 from the
/// usual view to as close as it goes, 4 through a swordsman's eyes with a band wading through it, 5 a blast's wave
/// running out over it, 6 a whirlwind crossing it, 7 and 8 those two close; --pose=people: every figure in every look,
/// --look=0..9 choosing a row), --pose=troops: the troops the lore opens out before the walls meeting a band (--at=
/// seconds into it, --look=1 closer),
/// --selftest drives the tools and the pawns and checks the results, --calm stops the embers and the flicker,
/// --top looks straight down, --look=N at the Nth named place of the valley, --seed=N makes another valley; --select[=N]
/// chooses our pawns (or the first N), --tab=key lets a panel down from the beam, --tool=N takes up the Nth tool,
/// --ui-scale=S draws the interface at that size instead of the player's own, --inspect looks at the first raider (or beast),
/// --day=F starts at that time of the day (0 dawn, 1 sunset), --alarm=S raises the alarm and runs on S seconds,
/// --weather=rain|fog|wind brings that weather at once, --envoy sends the horde's envoy and waits for its offer,
/// --lore=N,key,… puts N knowledge in hand with that lore learnt, --sounds=folder writes every sound out as a .wav and quits,
/// --load=file.sav reads a saved game back, --sandbox opens a game with everything learnt and plenty of all (O calls up
/// a band of the horde under the pointer), --trailer=N stages and films the Nth shot of the trailer (Main.Trailer.cs).
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
    public BloodView Blood { get; private set; }
    public WorkProgress Progress { get; private set; }
    public UnitView Units { get; private set; }
    public OrdersView Marks { get; private set; }
    public ZoneView Areas { get; private set; }
    public Commander Command { get; private set; }
    /// <summary>First person: the player in one of ours.</summary>
    public FirstPerson FP { get; private set; }
    /// <summary>The horde's ways and our archers' cover, drawn over the field: put away in first person.</summary>
    public Node3D Trails { get; private set; }
    public Node3D Cover { get; private set; }
    /// <summary>The light of the valley: the sun through the day, the moon at night, the lamps.</summary>
    public Atmosphere Atmos { get; private set; }

    /// <summary>The pawns the player has picked out.</summary>
    public readonly HashSet<Unit> Selected = new();
    /// <summary>A raider or a beast the player clicked on to look at (with none of ours chosen), or null.</summary>
    public Unit Inspected;
    /// <summary>The citadel was clicked (or K pressed): the console shows its muster.</summary>
    public bool KeepChosen;
    /// <summary>Game seconds per real second: 1, 2 or 3.</summary>
    public int Speed = 1;
    /// <summary>The game's time slowed for a moment after a great blast or fall (see Spectacle): 1 is as fast as it goes.</summary>
    public float Slow = 1;
    public bool Paused;
    /// <summary>The valley's sounds.</summary>
    public Sound Sound;
    public MagicView Magic;
    /// <summary>The great moments made to be felt: the view shaken, the screen flashed, the time slowed a moment.</summary>
    public Spectacle Spectacle;
    /// <summary>A run driven by a script (a test, a staged scene, a screenshot), not by a hand on the mouse.</summary>
    public bool Scripted => _selfTest || _demo || _shot != null || _tutCheck;

    const string BlueprintFile = "user://blueprints.json";

    /// <summary>A saved game to read back when the scene starts (set, then the scene started again: <see cref="Saves.Read"/>).</summary>
    public static string PendingLoad;
    /// <summary>What the next start of the scene begins with, asked from the menu: "game" — a new siege, "tutorial" — the tutorial; none — the title.</summary>
    public static string PendingStart;
    /// <summary>How hard the siege begun from the menu in play is to be (the scene begins again for it).</summary>
    public static Hardness? PendingHardness;
    /// <summary>The tutorial, while this game is one.</summary>
    public Tutorial Tutor { get; private set; }
    /// <summary>--tutorial or --tutorial=N: the tutorial from its Nth step; --title: the title over the valley (screenshots, tests).</summary>
    int _tutorialAt = -1;
    bool _titleArg;
    /// <summary>--sandbox: a game with everything open and plenty of all, to try every part of it at once (Sim.Sandbox.cs).</summary>
    bool _sandbox;
    /// <summary>The seed the valley was made from: what a save keeps of it.</summary>
    public uint Seed => _seed;
    bool _loaded, _waveWasActive;
    string _loadedFrom;
    /// <summary>--menu or --menu=save|load|settings: the menu open at the start (screenshots).</summary>
    string _menu;

    string _shot;
    int _shotFrames = 45, _frames;
    bool _demo, _calm, _selfTest, _finale, _top, _inspect;
    int _look = -1;
    /// <summary>--cam=x,z,yaw,pitch,zoom: where the camera looks for a screenshot, x and z in voxels.</summary>
    float[] _cam;
    /// <summary>--wave=seconds: a new game with the horde called at once and this long into its wave (screenshots).</summary>
    double _waveAt = -1;
    /// <summary>--day=F: a new game at that time of the day before the first wave (0 dawn, 1 sunset), the countdown set to match.</summary>
    double _dayAt = -1;
    /// <summary>--alarm=S: the alarm raised at the start and the game run on this many seconds (screenshots).</summary>
    double _alarmAt = -1;
    /// <summary>--weather=rain|fog|wind: that weather from the start; --envoy: an envoy sent at once and waited for (screenshots).</summary>
    string _weather;
    bool _envoy;
    /// <summary>--lore=N,key,…: N knowledge in hand and that lore learnt already (screenshots of «Знания»).</summary>
    string _lore;
    /// <summary>--sounds=folder: every sound the game makes written there as a .wav, and out.</summary>
    string _soundsTo;
    /// <summary>--select or --select=N: all our pawns (or the first N) chosen at the start, for screenshots of the console.</summary>
    int _select;
    /// <summary>--tab=key: a panel let down from the beam at the start; --tool=N: the Nth tool of the grid in hand.</summary>
    string _tab;
    int _tool = -1;
    /// <summary>--grid=N: the brushes snap to a grid of N voxels; --aim=x,z: the cursor over that voxel of the valley (screenshots).</summary>
    int _grid = -1;
    float[] _aim;
    uint _seed = 7;
    string _pose = "";
    /// <summary>How long the staged siege runs before the screenshot (--at=seconds).</summary>
    double _poseSeconds = 14;
    /// <summary>--ui-scale=S: the interface drawn at this size for this run, whatever the player set.</summary>
    float _uiScale;
    /// <summary>--lines: the voxels drawn with their lines round them, whatever the player set.</summary>
    bool _lines;

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
            else if (a.StartsWith("--wave=") && double.TryParse(a["--wave=".Length..], System.Globalization.CultureInfo.InvariantCulture, out double wa)) _waveAt = wa;
            else if (a.StartsWith("--day=") && double.TryParse(a["--day=".Length..], System.Globalization.CultureInfo.InvariantCulture, out double da)) _dayAt = da;
            else if (a.StartsWith("--alarm=") && double.TryParse(a["--alarm=".Length..], System.Globalization.CultureInfo.InvariantCulture, out double al)) _alarmAt = al;
            else if (a.StartsWith("--weather=")) _weather = a["--weather=".Length..];
            else if (a == "--envoy") _envoy = true;
            else if (a.StartsWith("--lore=")) _lore = a["--lore=".Length..];
            else if (a.StartsWith("--sounds=")) _soundsTo = a["--sounds=".Length..];
            else if (a == "--select") _select = int.MaxValue;
            else if (a.StartsWith("--select=") && int.TryParse(a["--select=".Length..], out int sn)) _select = sn;
            else if (a.StartsWith("--tab=")) _tab = a["--tab=".Length..];
            else if (a.StartsWith("--tool=") && int.TryParse(a["--tool=".Length..], out int tn)) _tool = tn;
            else if (a.StartsWith("--grid=") && int.TryParse(a["--grid=".Length..], out int gn)) _grid = gn;
            else if (a.StartsWith("--aim=")) _aim = Array.ConvertAll(a["--aim=".Length..].Split(','), v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
            else if (a.StartsWith("--cam=")) _cam = Array.ConvertAll(a["--cam=".Length..].Split(','), v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
            else if (a == "--selftest") _selfTest = true;
            else if (a == "--inspect") _inspect = true;
            else if (a.StartsWith("--load=")) PendingLoad = a["--load=".Length..];
            else if (a == "--tutorial") _tutorialAt = 0;
            else if (a.StartsWith("--tutorial=") && int.TryParse(a["--tutorial=".Length..], out int ta)) _tutorialAt = ta;
            else if (a == "--title") _titleArg = true;
            else if (a == "--sandbox") _sandbox = true;
            else if (a == "--tutorial-check") { _tutCheck = true; _tutorialAt = 0; }
            else if (a == "--menu" || a.StartsWith("--menu=")) _menu = a.Length > 7 ? a[7..] : "";
            else if (a.StartsWith("--pose=")) _pose = a["--pose=".Length..];
            else if (a.StartsWith("--quality=") && int.TryParse(a["--quality=".Length..], out int qa)) _quality = qa;
            else if (a.StartsWith("--perf=") && double.TryParse(a["--perf=".Length..], System.Globalization.CultureInfo.InvariantCulture, out double pf)) _perf = pf;
            else if (a.StartsWith("--at=") && double.TryParse(a["--at=".Length..], System.Globalization.CultureInfo.InvariantCulture, out double at)) _poseSeconds = at;
            else if (a.StartsWith("--ui-scale=") && float.TryParse(a["--ui-scale=".Length..], System.Globalization.CultureInfo.InvariantCulture, out float us)) _uiScale = us;
            else if (a == "--lines") _lines = true;
            else TrailerArg(a);
        }
        // scripted runs draw the interface at its usual size, unless told otherwise, and leave the player's settings be
        if (_selfTest || _demo || _shot != null || _tutCheck || _uiScale > 0) Settings.Scripted(_uiScale);
        else Settings.Load();
        if (_quality >= 0) Settings.ScriptQuality(_quality);
        // the self-test and the screenshots play out of the player's sight (--show keeps the window up)
        if ((_selfTest || _shot != null || _trailer >= 0 || _tutCheck) && !OS.GetCmdlineUserArgs().Contains("--show")) Backstage.Withdraw(this);
        Settings.Apply(GetTree().Root);
        Settings.ApplyVolume();
        if (_lines) Settings.ScriptLines(true);
        else Settings.ApplyLines();

        // a saved game: its valley is made again from its seed, and what play changed of it laid over
        SaveReader save = null;
        if (PendingLoad != null)
        {
            _loadedFrom = PendingLoad;
            try { save = new SaveReader(System.IO.File.OpenRead(PendingLoad)); _seed = save.Seed; }
            catch (Exception e) { GD.PrintErr($"[voxel-tower] could not read {PendingLoad}: {e.Message}"); save?.Dispose(); save = null; }
            PendingLoad = null;
            _demo = _selfTest = false;
        }
        var sw = Stopwatch.StartNew();
        var mp = new MapParams { Seed = _seed };
        World = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        Site = MapGen.Generate(World, mp);
        if (_demo && _pose == "trees") Grove();
        if (_demo && _pose is "people" or "duel" or "reel" or "blood") ClearGrove();
        // the field of the traps cleared of trees, so they are to be seen
        if (_demo && _pose == "engines") ClearTrees(Site.KeepX - Site.KeepR - 420, Site.KeepZ - 140, Site.KeepX - Site.KeepR - 20, Site.KeepZ + 140);
        if (_demo && _pose == "drawbridge") ClearTrees(Site.KeepX - Site.KeepR - 220, Site.KeepZ - 120, Site.KeepX - Site.KeepR - 30, Site.KeepZ + 120);
        if (_demo && _pose == "shaman") ClearTrees(Site.KeepX - Site.KeepR - 240, Site.KeepZ - 90, Site.KeepX - Site.KeepR - 40, Site.KeepZ + 90);
        if (_demo && _pose == "spells") ClearTrees(Site.KeepX - Site.KeepR - 260, Site.KeepZ - 100, Site.KeepX - Site.KeepR - 40, Site.KeepZ + 120);
        if (_demo && _pose == "traps") ClearTrees(Site.KeepX - Site.KeepR - 220, Site.KeepZ - 120, Site.KeepX - Site.KeepR - 40, Site.KeepZ + 120);
        if (_trailer >= 0) TrailerGround();
        if (_demo && _pose is "blast" or "grip" or "wind" or "hexes" or "sorcerers" or "wonders" or "fp" or "powder" or "charge" or "stones" or "topple" or "roll" or "crush" or "drops" or "troops") ClearTrees(Site.KeepX - Site.KeepR - 300, Site.KeepZ - 140, Site.KeepX - Site.KeepR - 30, Site.KeepZ + 140);
        World.Compact();
        save?.Valley(World, Site);
        double genMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        Sim = new Sim(World, Site);
        // in play, where water goes is worked out off the main thread; scripted runs want it at once
        Sim.WaterInBackground = !_selfTest && !_demo && _shot == null;
        // a new game finds a fort half raised round the citadel; scripted runs set up their own scenes
        string extra = null;
        if (save != null)
        {
            try { extra = Sim.Load(save); _loaded = true; }
            catch (Exception e) { GD.PrintErr($"[voxel-tower] the save is broken: {e}"); }
            save.Dispose();
        }
        else if (!_selfTest && !_demo) StartingFort.Raise(Sim);
        double navMs = sw.Elapsed.TotalMilliseconds;

        var atmo = Atmos = new Atmosphere();
        AddChild(atmo);
        atmo.Build(World, Site, _calm || _shot != null);
        atmo.Watch(Sim);

        View = new WorldView();
        AddChild(View);
        View.Init(World);
        View.InitFlow(Site);

        Rig = new CameraRig { BottomInset = Hud.BottomBand, TopInset = Hud.TopBand };
        Rig.Bounds = new Rect2(World.OriginX, World.OriginZ, World.SX * VoxelWorld.Vox, World.SZ * VoxelWorld.Vox);
        AddChild(Rig);
        Rig.HomeAt(World.WX(Site.KeepX - Site.KeepR - 50), World.WZ(Site.KeepZ + 14));
        atmo.Follow = Rig;
        Debris = new Debris();
        AddChild(Debris);
        Debris.Init(World);
        // what flies hangs where it is while the game is stopped
        Debris.Held = () => Paused;

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
        var trails = new TrailView(); Trails = trails;
        AddChild(trails);
        trails.Init(this);
        var cover = new CoverView(); Cover = cover;
        AddChild(cover);
        cover.Init(this);
        Blood = new BloodView();
        AddChild(Blood);
        Blood.Init(this);
        var fell = new FellView();
        AddChild(fell);
        fell.Init(this);
        var fire = new FireView();
        AddChild(fire);
        fire.Init(this);
        Magic = new MagicView();
        AddChild(Magic);
        Magic.Init(this);
        Sound = new Sound();
        AddChild(Sound);
        Sound.Init(this);
        Spectacle = new Spectacle();
        AddChild(Spectacle);
        Spectacle.Init(this);
        Progress = new WorkProgress();
        AddChild(Progress);
        Progress.Init(this);
        Graphics.Apply(this);
        var meter = new CanvasLayer { Layer = 50 };
        AddChild(meter);
        meter.AddChild(new FrameMeter());
        if (_soundsTo != null) { Sound.SaveAll(_soundsTo); GD.Print($"[voxel-tower] {Sound.Clips} sounds written to {_soundsTo}"); GetTree().Quit(); }
        // where the warbands will go is shown in play; scripted runs keep the horde's ways to the waves
        Sim.WantTrails = !_selfTest;
        if (Sim.WantTrails && !_demo) Sim.ComputeFields(coming: true);

        Tools = new BuildTools();
        AddChild(Tools);
        Hud = new Hud();
        AddChild(Hud);
        Command = new Commander();
        AddChild(Command);
        Command.Init(this);
        // last, so it hears the keys and the mouse before anything else when the player is in one of ours; the mouse,
        // held in first person, is free again in a game loaded from there
        Input.MouseMode = Input.MouseModeEnum.Visible;
        FP = new FirstPerson();
        AddChild(FP);
        Cine = new CineCam();
        AddChild(Cine);
        Cine.Init(this);
        View.Grass.Bind(this);
        Tools.Init(this);
        Rig.RightClick += p => { if (Tools.Tool != Tool.None) Tools.Cancel(); else Command.OrderTo(p); };

        if (!_selfTest && !_demo) LoadBlueprints();
        Hud.Init(this);
        FP.Init(this);

        Sim.Said += (text, kind, x, z) => Hud.Log(text, (Hud.Kind)Math.Clamp(kind, 0, 3), x, z);
        // an errand done rings the small bell
        Sim.ErrandDone += _ => Sound.Ui("done");
        // the chronicle in the self-test's log, for when something goes wrong
        if (_selfTest) Sim.Said += (text, kind, x, z) => GD.Print($"[chronicle] {Sim.Time:0} s: {text}");
        // (added to, not set: the sounds hear these too)
        Sim.Mined += (x, y, z, m) => { if (Random.Shared.NextDouble() < .12) Debris.Spawn(m, x, y, z, 1, 1.3f); };
        Sim.Hacked += (x, y, z, m) => { if (Random.Shared.NextDouble() < .16) Debris.Spawn(m, x, y, z, 1, 1.8f); };
        Sim.Raised += (x, y, z, m) => { if (Random.Shared.NextDouble() < .05) Debris.Spawn(m, x, y, z, 1, .8f); };
        // every blow of a pawn's tool knocks chips off where it lands; a stone set down puffs a little dust of mortar
        Sim.Worked += (x, y, z, m, dig) => { if (m != Mat.Air) Debris.Spawn(m, x, y, z, dig ? 2 + Random.Shared.Next(2) : 1, dig ? 1.4f : .5f, dig ? .8f : .6f); };
        Sim.Splashed = (x, y, z, n) => { Debris.Spawn(Mat.Blood, (int)x, (int)y, (int)z, Math.Clamp(n / 3, 1, 6), 1.1f, .4f); Blood?.Splash(x, y, z, n); };

        if (_loaded)
        {
            // a game read back: the screen as it was, and the chronicle up to where it was saved
            Saves.Apply(this, extra);
            Hud.Log($"Игра загружена: {Saves.NameOf(System.IO.Path.GetFileNameWithoutExtension(_loadedFrom ?? ""))}.", Hud.Kind.Ok);
            _waveWasActive = Sim.Waves.Active;
            GD.Print($"[voxel-tower] world {genMs:0} ms, navigation {navMs:0} ms, mesh {View.LastFullMeshMs:0} ms, {View.Triangles()} triangles; loaded");
            return;
        }
        // the household the siege finds at the citadel, the beasts of the valley, and the horde's camp at a mine
        foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Archer, Kinds.Swordsman }) Sim.Spawn(t, Sim.Gate);
        Sim.SeedWildlife();
        if (!_selfTest && !_demo) Sim.SeedMines();

        // a game of one's own opens on the title over the valley; the tutorial, asked for, on its first step
        _startTutorial = PendingStart == "tutorial" || _tutorialAt >= 0;
        _startTitle = !_startTutorial && !_sandbox && (_titleArg || (PendingStart == null && !Scripted));
        PendingStart = null;
        if (PendingHardness is { } hard) { Sim.SetHardness(hard); PendingHardness = null; }
        if (!_startTutorial && !_startTitle)
        {
            if (_sandbox) StartSandbox();
            else Welcome();
        }
        GD.Print($"[voxel-tower] world {genMs:0} ms, navigation {navMs:0} ms, mesh {View.LastFullMeshMs:0} ms, {View.Triangles()} triangles");

        if (_demo) Demo();
        if (_look >= 0 && _look < Site.Landmarks.Count && !_demo)
        {
            var l = Site.Landmarks[_look];
            Rig.Set(World.WX(l.X), World.WZ(l.Z), -.7f, .6f, 2.4f, snap: true);
        }
        if (_waveAt >= 0 && !_demo && !_selfTest) { Sim.ComputeFields(); Sim.Waves.StartNow(Sim); Advance(_waveAt); Hud.Refresh(); }
        if (_dayAt >= 0 && _waveAt < 0 && !_demo && !_selfTest) { Sim.Waves.Countdown = Waves.Prep * (1 - Math.Clamp(_dayAt, 0, .998)); Hud.Refresh(); }
        if (_alarmAt >= 0 && !_selfTest) { Sim.RaiseAlarm(); Advance(_alarmAt); Hud.Refresh(); }
        if (_weather != null && !_selfTest)
            Sim.StartWeather(_weather switch { "rain" => Weather.Rain, "fog" => Weather.Fog, _ => Weather.Wind }, 900);
        if (_envoy && !_selfTest)
        {
            Sim.Waves.Number = Math.Max(1, Sim.Waves.Number);
            Sim.SendEnvoy();
            for (int t = 0; t < 600 && Sim.Offer is { Kind: DealKind.None }; t++) Advance(.5);
            if (Sim.Offer?.Envoy is { } e) Rig.Set(World.WX(e.X), World.WZ(e.Z), -.66f, .62f, 2f, snap: true);
            Hud.Refresh();
        }
        if (_lore != null && !_selfTest)
        {
            var parts = _lore.Split(',');
            foreach (string key in parts[1..])
                if (Sim.LoreOf(key) is { } l)
                {
                    if (l.Coin == Coin.Favour) Sim.Favour += l.Cost; else if (l.Coin == Coin.Revelations) Sim.Revelations += l.Cost; else Sim.Knowledge += l.Cost;
                    Sim.Learn(key);
                }
            Sim.Knowledge = double.TryParse(parts[0], out double k) ? k : 0;
            Hud.Refresh();
        }
        if (_select > 0)
        {
            foreach (var u in Sim.Units) if (!u.Dead && u.Team == Team.Player && Selected.Count < _select) Selected.Add(u);
            Hud.OnSelectionChanged();
        }
        if (_tab != null) Hud.OpenPanel(_tab);
        if (_menu != null) Hud.OpenMenu(_menu);
        if (_inspect && (Sim.Units.Find(u => !u.Dead && u.Team == Team.Horde) ?? Sim.Units.Find(u => !u.Dead && u.Team == Team.Wild)) is { } seen)
        {
            Inspected = seen;
            Rig.Set(World.WX(seen.X), World.WZ(seen.Z), -.66f, .62f, 2.2f, snap: true);
        }
        if (_tool >= 0 && _tool < Hud.ToolDefs.Length) Tools.SetTool(Hud.ToolDefs[_tool].tool);
        if (_grid >= 0) Tools.SetGrid(_grid);
        if (_cam is { Length: 5 }) Rig.Set(World.WX(_cam[0]), World.WZ(_cam[1]), _cam[2], _cam[3], _cam[4], snap: true);
        if (_top) Rig.ToggleTopDown();
        if (_startTutorial) StartTutorial(Math.Max(0, _tutorialAt));
        else if (_startTitle) Hud.ShowTitle();
        if (_selfTest) SelfTest();
        if (_tutCheck) TutorialCheck();
        if (_perf > 0) Perf(_perf);
        // a hidden or minimised window never draws a frame: don't let scripted runs hang
        if (_shot != null || _selfTest) GetTree().CreateTimer(120).Timeout += () => { GD.PrintErr("[voxel-tower] gave up waiting for a frame"); GetTree().Quit(2); };
    }

    bool _startTutorial, _startTitle;
    int _quality = -1;
    double _perf;

    /// <summary>
    /// How fast the picture is drawn (--perf=seconds; --quality=0…3 for the picture, --show to keep the window up): after
    /// three seconds settling, the frames over so many seconds — how many a second on the whole, the slowest one, the
    /// calls to draw and the triangles of the last — written out, and the game left.
    /// </summary>
    async void Perf(double seconds)
    {
        // not held to the screen's refresh: as fast as it can be drawn
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        for (int i = 0; i < 180; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ulong t0 = Time.GetTicksUsec(), last = t0;
        int frames = 0;
        double worst = 0, process = 0;
        _simTicks = 0;
        Prof.On = true; Prof.Reset();
        var vpRid = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(vpRid, true);
        double gpu = 0, cpu = 0;
        while ((Time.GetTicksUsec() - t0) / 1e6 < seconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong now = Time.GetTicksUsec();
            worst = Math.Max(worst, (now - last) / 1e3);
            last = now;
            frames++;
            process += Performance.GetMonitor(Performance.Monitor.TimeProcess);
            gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vpRid);
            cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vpRid) + RenderingServer.GetFrameSetupTimeCpu();
        }
        double simMs = _simTicks * 1000.0 / Stopwatch.Frequency / Math.Max(1, frames), procMs = process * 1000 / Math.Max(1, frames);
        double took = (Time.GetTicksUsec() - t0) / 1e6;
        var draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var tris = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        var objects = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        GD.Print($"[perf] picture {Settings.Qualities[Settings.Quality]}: {frames / took:0.0} frames a second over {took:0.0} s, the slowest {worst:0.0} ms; a frame's process {procMs:0.0} ms, of it the simulation {simMs:0.0} ms; " +
            $"{draws:0} draw calls, {objects:0} objects, {tris / 1e6:0.00} M triangles; {Sim.Units.Count} units; {RenderingServer.GetVideoAdapterName()}");
        // what is drawn, by who made it
        var kinds = new Dictionary<string, int>();
        void Count(Node n)
        {
            if (n is GeometryInstance3D g && g.IsVisibleInTree())
            {
                string by = (n.GetParent()?.GetType().Name ?? "?") + (g.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off ? "+shadow" : "");
                kinds[by] = kinds.GetValueOrDefault(by) + 1;
            }
            foreach (var c in n.GetChildren()) Count(c);
        }
        Count(this);
        var told = new List<string>();
        foreach (var (k, v) in kinds) told.Add($"{k} {v}");
        told.Sort((a, b) => int.Parse(b[(b.LastIndexOf(' ') + 1)..]).CompareTo(int.Parse(a[(a.LastIndexOf(' ') + 1)..])));
        GD.Print($"[perf] drawn: {string.Join(", ", told.GetRange(0, Math.Min(12, told.Count)))}");
        GD.Print($"[perf] ms a frame: drawing {cpu / Math.Max(1, frames):0.0} on the processor, {gpu / Math.Max(1, frames):0.0} on the graphics card; the views: {Prof.Heaviest(frames, 8)}");
        GetTree().Quit();
    }

    /// <summary>The sandbox: everything open, the stores never short, and the horde called up by hand.</summary>
    void StartSandbox()
    {
        Sim.BeginSandbox();
        Hud.Log("Песочница: всё изучено, запасы не кончаются, заклинания бесплатные и без перезарядки, цитадель не падает.", Hud.Kind.Ok);
        Hud.Log("O — ватага орды под курсором, Shift+O — тяжёлая ватага. Пробел — позвать волну.", Hud.Kind.Ok);
        Hud.Refresh();
    }

    /// <summary>The tips of the start, into the chronicle: a game of one's own begins with them.</summary>
    public void Welcome()
    {
        // a siege of one's own: what to do next is in the errands from the first minute
        if (!Scripted)
        {
            Sim.BeginErrands();
            Hud.Log("Орда придёт через 10 минут.", Hud.Kind.Warn);
            Hud.Log("Что делать дальше, написано слева в «Поручениях». {Help} — все клавиши.", Hud.Kind.Ok);
            return;
        }
        Hud.Log("Орда придёт с запада через 10 минут.", Hud.Kind.Warn);
    }

    /// <summary>The tutorial, from its step n: the frame shut, the card on the first step.</summary>
    public void StartTutorial(int from = 0)
    {
        if (Tutor != null) return;
        Tutor = new Tutorial();
        AddChild(Tutor);
        Tutor.Init(this, from);
    }

    /// <summary>A new siege (or the tutorial) from the menu: the scene begins again, without the title.</summary>
    public void NewGame(bool tutorial = false)
    {
        PendingStart = tutorial ? "tutorial" : "game";
        GetTree().ReloadCurrentScene();
    }

    double _slowest;
    // the time the simulation took, for --perf
    long _simTicks;

    public override void _Process(double delta)
    {
        if (_frames > 5) _slowest = Math.Max(_slowest, delta);
        if (_trailer >= 0) Roll();
        // under the title the valley turns slowly round the citadel
        if (Hud.TitleOpen) { var a = Rig.Aim; Rig.Set(a.x, a.z, a.yaw + (float)delta * .045f, a.pitch, a.zoom, snap: false); }
        if (!Paused && !_selfTest)
        {
            double dt = Math.Min(delta, .1) * Speed * Slow * Pace;
            long t0 = Stopwatch.GetTimestamp();
            while (dt > 1e-6)
            {
                float step = (float)Math.Min(dt, .05);
                Sim.Step(step);
                dt -= step;
            }
            _simTicks += Stopwatch.GetTimestamp() - t0;
        }
        // a wave beaten back is written down by itself
        if (_shot == null && !_selfTest && !_demo)
        {
            if (_waveWasActive && !Sim.Waves.Active && !Sim.Over && Tutor is not { Over: false } && Saves.Write(this, Saves.Auto)) Hud.Log("Автосохранение: волна отбита.", Hud.Kind.Ok, toast: false);
            _waveWasActive = Sim.Waves.Active;
        }
        Command.Prune();
        using (Prof.Time("Remesh")) View.RemeshDirty();
        using (Prof.Time("Flow")) View.UpdateFlow(Sim);
        if (Sim.Over && !_finale)
        {
            _finale = true;
            if (Sim.Won) Hud.Finale("Осада снята", $"Отбито волн: {Waves.Total}. Налётчиков убито: {Sim.Kills}.", true);
            else Hud.Finale("Цитадель пала", $"Отбито волн: {Math.Max(0, Sim.Waves.Number - 1)} из {Waves.Total}. Налётчиков убито: {Sim.Kills}.", false);
        }
        // the cursor laid over a voxel for a screenshot, once the view has settled
        if (_shot != null && _aim is { Length: 2 } && _frames == Math.Max(2, _shotFrames - 8))
            Tools._UnhandledInput(new InputEventMouseMotion { Position = Rig.Cam.UnprojectPosition(new Vector3(World.WX(_aim[0] + .5f), World.WY(World.SurfAt((int)_aim[0], (int)_aim[1])), World.WZ(_aim[1] + .5f))) });
        if (_shot != null && ++_frames == _shotFrames) TakeShot();
    }

    async void TakeShot()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var vp = GetViewport();
        GD.Print($"[voxel-tower] {Engine.GetFramesPerSecond()} fps, {vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.PrimitivesInFrame) / 1000} k triangles in {vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame)} draw calls (+{vp.GetRenderInfo(Viewport.RenderInfoType.Shadow, Viewport.RenderInfo.PrimitivesInFrame) / 1000} k for shadows), {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576} MB video memory");
        GD.Print($"[voxel-tower] {Sim.Units.Count} units, {Sim.Orders.Total} voxels marked, {Sim.Burning.Count} burning, game time {Sim.Time:0} s, slowest frame {_slowest * 1000:0} ms, {Units.Drawn()} figures and {Units.ArrowsDrawn} arrows drawn");
        if (OS.IsDebugBuild())
            foreach (var u in Sim.Units)
            {
                if (u.Team == Team.Wild) continue;
                string field = "";
                if (u.Team == Team.Horde && Sim.Fields.TryGetValue(Sim.FieldKind(u.T), out var f))
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
        Hud.Log($"Чертёж «{bp.Name}» удалён.");
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
        await Look(lx, 460);
        int fy = ground + 10, face = lx;
        while (World.Get(face - 1, fy, 460) != Mat.Air) face--;
        int b2 = Sim.Orders.BuildTotal;
        await ClickAt(ScreenAt(face - .02f, fy + .5f, 460.5f));
        int rungs = Sim.Orders.BuildTotal - b2;
        Advance(40);
        int built = 0;
        for (int y = ground; y < ground + 48; y++) for (int z = 450; z < 472; z++) for (int x = face - 10; x < face; x++) if (World.Get(x, y, z) == Mat.Ladder) built++;
        Check("…the workers set it against the wall", rungs > 40 && built > 40, $"{rungs} voxels marked, {built} in place; clicked on «{Tools.HoverText}», west of the face {Mat.NameOf(World.Get(face - 1, fy, 460))}, the face {Mat.NameOf(World.Get(face, fy, 460))} at {face},{fy}, wall top {World.SurfAt(lx, 460)}");

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
            hung == null ? "no gate planned" : $"leaves on the {(hung.In > 0 ? "east" : "west")}, ready {hung.Ready}; {Sim.Orders.Total} marked, {Sim.Orders.BuildTotal} to lay, wood {Fmt.M3(Sim.Stock[Stockpile.Wood])} m³; " +
                string.Join(", ", Sim.Units.FindAll(u => u.T == Kinds.Worker && !u.Dead).ConvertAll(u => $"#{u.Id} {u.State} {u.Why}")));

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
        await Look(640, 500);
        int r3 = Removals();
        await Drag((641, 441), (646, 558));
        int moat = Removals() - r3;
        // (a tree the moat cuts into comes down whole, roots and all: its wood is not the line's)
        int gx = kx + (int)Math.Round((641 - kx) / 8.0) * 8, askew = 0, minZ = int.MaxValue, maxZ = 0;
        for (int z = 420; z < 580; z++)
            for (int x = 600; x < 690; x++)
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
        await Drag((620, 500), (668, 500));
        int bridged = Sim.Orders.BuildTotal - bb0, deckMid = 0, deckEnd = 0;
        for (int y = World.SY - 1; y > 0 && deckMid == 0; y--) if (Sim.Orders.At(643, y, 500) != 0) deckMid = y;
        for (int y = World.SY - 1; y > 0 && deckEnd == 0; y--) if (Sim.Orders.At(626, y, 500) != 0) deckEnd = y;
        Check("…a drag marks a bridge over the moat, higher in the middle", bridged > 1500 && deckMid > deckEnd + 2,
            $"{bridged} voxels, deck at {deckMid} in the middle and {deckEnd} near the end");
        KeyPress(Key.Key6);
        Check("6 picks up the unmarking tool", Tools.Tool == Tool.Unmark);
        await Drag((608, 486), (680, 514));
        Check("a frame takes the bridge back", Sim.Orders.BuildTotal <= bb0 + 10, $"{Sim.Orders.BuildTotal - bb0} left of {bridged}");
        await Drag((630, 430), (652, 570));
        Check("a frame takes the marks back", Removals() <= r3 + 10, $"{Removals() - r3} left of {moat}");
        // Ctrl+Z takes the frames back, the last first: the moat is marked again, then the bridge with the end of the moat under it
        KeyPress(Key.Z, ctrl: true);
        int moatBack = Removals() - r3;
        KeyPress(Key.Z, ctrl: true);
        Check("Ctrl+Z takes the frames back one by one: the moat is marked again, then the bridge",
            moatBack > moat / 2 && Removals() - r3 >= moat - 10 && Sim.Orders.BuildTotal - bb0 >= bridged - 10,
            $"moat {moatBack}, then {Removals() - r3} of {moat}; bridge {Sim.Orders.BuildTotal - bb0} of {bridged}");
        Sim.CancelArea(600, 420, 690, 580);

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
        Check("a right click sends it and it stays", worker.Hold && worker.Path != null, $"hold {worker.Hold}, path {worker.Path?.Count}");
        Advance(15);
        var (fx, _, fz) = Sim.Nav.Feet(worker.Cell);
        Check("…and it gets there", MathF.Abs(fx - (kx - 60)) < 24 && MathF.Abs(fz - (kz + 60)) < 24, $"at {fx:0},{fz:0}");
        Command.ReleaseSelected();
        Check("«За работу» lets it go", !worker.Hold);
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
        await Look(face - 6, 460);
        var rung = ScreenAt(face - .5f, fy + 2.5f, 460.5f);
        Rig._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = rung });
        Rig._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = rung });
        Check("«Огонь» gives the archer fire arrows, and a right click on the ladder tells it to set it alight", archer.FireArrows && archer.KindleAt >= 0,
            $"fire arrows {archer.FireArrows}, mark {archer.KindleAt}, at the ladder: {Mat.NameOf(World.Get(face - 1, fy + 2, 460))}");
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
        var yew = Hud.Lore.PlaqueOf("yew");
        bool could = !yew.Disabled;
        yew.EmitSignal(BaseButton.SignalName.Pressed);
        Check("a medallion in «Знания» learns its lore for the knowledge it costs", could && Sim.Knows("yew") && Sim.Knowledge == 0 && Hud.Lore.PlaqueOf("barbs").Disabled,
            $"yew learnt {Sim.Knows("yew")}, {Sim.Knowledge} knowledge left");
        // the troops' page: a kind learnt there is one the citadel will take on
        Hud.OpenPanel("lore:1");
        Sim.Knowledge = 3;
        Hud.Refresh();
        bool troopsShown = Hud.Lore.Page == 1 && Hud.Lore.PlaqueOf("spearmen").IsVisibleInTree() && !Hud.Lore.PlaqueOf("yew").IsVisibleInTree();
        Hud.Lore.PlaqueOf("spearmen").EmitSignal(BaseButton.SignalName.Pressed);
        Check("the troops' page of «Знания» learns a kind of troops for knowledge", troopsShown && Sim.Knows("spearmen") && Sim.Knowledge == 0,
            $"page {Hud.Lore.Page}, spearmen learnt {Sim.Knows("spearmen")}, {Sim.Knowledge} knowledge left");
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
        await Frames(2);
        int drawn = Magic.PiecesDrawn;
        Advance(10);
        Check("a blast at a click: what is there goes up in pieces, seen flying, that come down", flyingNow > 3 && drawn > 500 && Sim.Fliers.Count == 0,
            $"{flyingNow} pieces, {drawn} voxels drawn in the air; {Sim.Fliers.Count} still flying after 10 s");
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
        Action<float, float, float, float> heard = (_, _, _, _) => booms++;
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
            $"in hand {powderInHand}; {booms} blasts, {kegsLeft} of {kegs.Count} kegs left; the goblin {(nearKegs.Dead ? "dead" : $"{nearKegs.Hp:0} hp")}");
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

        Check("the sounds are made by the game itself and heard as it plays", Sound.Clips == 28 && Sound.Heard.Count >= 4,
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
        Rig.SetAt(World.WX(Site.KeepX - 120), World.WY(World.SurfAt(Site.KeepX - 120, Site.KeepZ)), World.WZ(Site.KeepZ), aim0.yaw, .6f, 2.2f);
        await Frames(20);
        bool linesOff = !Settings.Lines;
        var shot0 = GetViewport().GetTexture().GetImage();
        await Frames(2);
        var shot1 = GetViewport().GetTexture().GetImage();
        Settings.ScriptLines(true);
        await Frames(2);
        var shot2 = GetViewport().GetTexture().GetImage();
        Settings.ScriptLines(false);
        // how much darker the second picture is than the first, on average, in steps of 255
        float Darker(Image a, Image b)
        {
            double d = 0; int n = 0;
            for (int y = a.GetHeight() / 5; y < a.GetHeight() * 4 / 5; y += 5)
                for (int x = a.GetWidth() / 5; x < a.GetWidth() * 4 / 5; x += 5, n++)
                    d += a.GetPixel(x, y).Luminance - b.GetPixel(x, y).Luminance;
            return (float)(d / Math.Max(1, n) * 255);
        }
        float idle = Darker(shot0, shot1), lined = Darker(shot1, shot2);
        Rig.Set(aim0.x, aim0.z, aim0.yaw, aim0.pitch, aim0.zoom, snap: true);
        Check("the lines round the voxels are off by default, and turned on in the settings they are drawn at once",
            linesOff && lined > .6f && lined > Math.Abs(idle) * 3, $"the picture {lined:0.00} darker with the lines, {idle:0.00} by itself");
        // first person: one of ours chosen (a swordsman if one is about), I goes into it: the view is its eyes, the frame of
        // the game put away; it walks where it is told, and Esc lets it go, the view back over the valley and it chosen
        // one standing on the ground (not up on a wall, with nowhere to walk), a swordsman if there is one
        bool Grounded(Unit u) => u.Y - World.GroundTop((int)u.X, (int)u.Z) < 3;
        var mine = Sim.Units.Find(u => Sim.CanPossess(u) && u.T == Kinds.Swordsman && Grounded(u)) ?? Sim.Units.Find(u => Sim.CanPossess(u) && u.T.Work < 1 && Grounded(u))
            ?? Sim.Units.Find(u => Sim.CanPossess(u) && Grounded(u)) ?? Sim.Units.Find(Sim.CanPossess);
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
        var about = Sim.Units.FindAll(u => !u.Dead && !u.Inside && u.Team == Team.Player && Brushes.Dist(u.X - kx, u.Z - kz) < Site.KeepR + 40);
        if (about.Count > 0) { Sim.Command(about, Sim.NearestStand(kx + 90, kz + 60, 2)); for (int t = 0; t < 40; t++) Advance(.5); }
        Sim.Stock.Gold = 500;
        int w0 = Sim.Count(Kinds.Worker);
        await Look(kx, kz);
        await ClickAt(ScreenAt(kx, Site.KeepBase + 40, kz));
        bool keepOpen = Hud.KeepShown;
        string instead = Inspected != null ? $" ({Inspected.T.Name} looked at instead)" : "";
        foreach (var u in Selected) instead = $" ({u.T.Name} chosen instead)";
        Hud.Inspector.TileOf(Kinds.Worker)?.Press();
        bool paidNow = Math.Abs(Sim.Stock.Gold - (500 - Kinds.Worker.Cost)) < .01 && Sim.Recruits.Count == 1 && Sim.Count(Kinds.Worker) == w0;
        Advance(Sim.TrainTime(Kinds.Worker) + 8);
        Check("a click on the citadel opens its muster; a worker taken on is paid for at once and comes out when made ready",
            keepOpen && paidNow && Sim.Count(Kinds.Worker) == w0 + 1 && Sim.Recruits.Count == 0 && Sim.Places == Sim.CitadelPlaces,
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
        if (_pose == "flood") { Flood(); return; }
        if (_pose == "trees") { LookAtGrove(); return; }
        if (_pose == "grass") { Meadow(); return; }
        if (_pose == "people") { People(); return; }
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
        if (_pose == "topple") { Topple(); return; }
        if (_pose == "roll") { RollDown(); return; }
        if (_pose == "crush") { CrushDeck(); return; }
        if (_pose == "drops") { Drops(); return; }
        if (_pose == "felling") { Felling(); return; }
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
    /// sword up; with --look=1 an archer drawing on a goblin; with --look=2 a worker with the pick at the ground.
    /// </summary>
    void Eyes()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9;
        int cx = Site.KeepX - Site.KeepR - 150, cz = Site.KeepZ + 10;
        Unit Raider(UnitType t, int dx, int dz) { var u = sim.Spawn(t, sim.NearestStand(cx + dx, cz + dz, t.Move.Height)); u.Guard = true; u.Home = u.Cell; return u; }
        var kind = _look == 1 ? Kinds.Archer : _look == 2 ? Kinds.Worker : Kinds.Swordsman;
        var me = sim.Spawn(kind, sim.NearestStand(cx + 40, cz, 2));
        foreach (var (t, dx, dz) in _look == 1
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
        if (_look == 2) { sim.WishStrike = true; Advance(1.2); }
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

    /// <summary>
    /// Everyone in every look in rows on cleared ground, facing the camera, for a close look at the figures (--pose=people):
    /// the workers, one swinging his pick; archers and swordsmen, every other swordsman with his shield up; goblins and
    /// sappers; orcs and miners; trolls and the beasts; the engines; and the troops the lore opens, every other one as it
    /// fights: spearmen, shieldbearers, halberdiers and knights; slingers, crossbowmen, bombardiers and hounds; the
    /// riders; a tame troll and the giants. --look=0..9 frames one row.
    /// </summary>
    void People()
    {
        var sim = Sim;
        sim.Waves.Countdown = 1e9;
        // the figures stand as they are put: nothing moves but their breathing
        Paused = true;
        var rows = new[]
        {
            new[] { Kinds.Worker, Kinds.Priest }, new[] { Kinds.Archer, Kinds.Swordsman, Kinds.Risen, Kinds.Thrall }, new[] { Kinds.Goblin, Kinds.Sapper, Kinds.Thief, Kinds.Shaman, Kinds.Witch },
            new[] { Kinds.Orc, Kinds.Miner, Kinds.Necromancer }, new[] { Kinds.Troll, Kinds.Deer, Kinds.Boar, Kinds.Hare, Kinds.Bear, Kinds.WhiteStag }, new[] { Kinds.Ram, Kinds.Catapult, Kinds.Mantlet, Kinds.SiegeTower },
            // the troops the lore opens: men on foot; men who throw and shoot, and the hounds; the riders; the great ones
            new[] { Kinds.Spearman, Kinds.Shieldbearer, Kinds.Halberdier, Kinds.Knight }, new[] { Kinds.Slinger, Kinds.Crossbowman, Kinds.Bombardier, Kinds.Hound },
            new[] { Kinds.Rider, Kinds.HorseArcher, Kinds.Lancer }, new[] { Kinds.TameTroll, Kinds.Giant },
        };
        const float yaw = -.3f, pitch = .42f, gap = 34;
        float sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
        var (gx, gz) = GroveAt;
        float cx = gx + 150, cz = gz + 110;
        bool one = _look >= 0 && _look < rows.Length;
        float width = 0;
        for (int r = 0; r < rows.Length; r++)
        {
            float span = 0;
            foreach (var t in rows[r]) span += Bodies.Looks(t) * Spacing(t);
            if (!one || r == _look) width = Math.Max(width, span);
            // the rows run across the view, one behind the other going away from the camera
            float along = -span / 2, back = (r - (one ? _look : 2)) * gap;
            foreach (var t in rows[r])
                for (int l = 0; l < Bodies.Looks(t); l++)
                {
                    along += Spacing(t) / 2;
                    float x = cx + along * cos - back * sin, z = cz - along * sin - back * cos;
                    var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height));
                    u.Look = l;
                    u.X = x; u.Z = z; u.Y = World.SurfAt((int)x, (int)z);
                    u.Facing = MathF.Atan2(cos, sin);
                    u.Hold = true;
                    if (t == Kinds.Swordsman && l % 2 == 1) { u.State = UnitState.Fight; u.Cooldown = 1; }
                    // the troops turned three-quarters, so that a spear or a lance shows, and every other one as it
                    // fights: a shield up, a crossbow levelled, a sling whirling
                    if (Array.IndexOf(Kinds.Troops, t) >= 0)
                    {
                        u.Facing -= .95f;
                        if (l % 2 == 1) { u.State = UnitState.Fight; u.Cooldown = .5f; }
                    }
                    if (t == Kinds.Worker && l == 3) u.State = UnitState.Work;
                    along += Spacing(t) / 2;
                }
        }
        static float Spacing(UnitType t) => Kinds.IsEngine(t) ? 34 : t.BodyH >= 24 ? 20 : t.Mounted ? 16 : t == Kinds.Shieldbearer ? 13 : t == Kinds.Deer || t == Kinds.WhiteStag ? 14 : t == Kinds.Bear ? 18 : t == Kinds.Hare ? 6 : t.BodyH <= 10 ? 9 : 11;
        // the camera looks at a plane 4 m up: the figures' middles are higher, so look past it by as much
        float over = (World.WY(World.SurfAt((int)cx, (int)cz) + 8) - 4) / MathF.Tan(pitch);
        float zoom = Math.Clamp(248 / (width / 1.6f + 12), 1.2f, 9);
        Rig.Set(World.WX(cx) - sin * over, World.WZ(cz) - cos * over, yaw, pitch, one ? zoom : 1.6f, snap: true);
        GD.Print($"[voxel-tower] the figures stand round {cx:0},{cz:0}, {gap:0} voxels a row; closer: --cam={cx - sin * over / VoxelWorld.Vox:0},{cz - cos * over / VoxelWorld.Vox:0},-0.3,0.42,6");
        Hud.Refresh();
    }

    /// <summary>
    /// Pairs fighting hand to hand on cleared ground, for a close look at the blows (--pose=duel), each with a stroke of
    /// its own: a swordsman cutting down at an orc, a knight across at one, a swordsman sweeping at one, a halberd
    /// chopping, a spear thrust, a shield driven in, an orc's backhand at a swordsman, a knight's rising cut; a troll's
    /// club, a rider from the saddle, a hound's teeth, a giant's maul. --look=0 they fight (--at=seconds); with the game
    /// stopped, 1 every striker drawn back for its stroke, 2 the moment the blade meets the body, 3 on through it; 4 and
    /// on one pair close, at the moment it meets (--at=seconds after the blow, under one, for another moment; below
    /// nought, so long before it: the blade coming down in the last 0.085 s, drawing back before that).
    /// </summary>
    void Duel()
    {
        var sim = Sim;
        sim.Waves.Countdown = 1e9;
        var pairs = new (UnitType a, UnitType b, StrokeKind k)[]
        {
            (Kinds.Swordsman, Kinds.Orc, StrokeKind.Overhead), (Kinds.Knight, Kinds.Orc, StrokeKind.DiagR), (Kinds.Swordsman, Kinds.Orc, StrokeKind.Sweep), (Kinds.Halberdier, Kinds.Orc, StrokeKind.PoleChop),
            (Kinds.Spearman, Kinds.Orc, StrokeKind.Thrust), (Kinds.Shieldbearer, Kinds.Goblin, StrokeKind.Bash), (Kinds.Orc, Kinds.Swordsman, StrokeKind.DiagL), (Kinds.Knight, Kinds.Goblin, StrokeKind.Rising),
            (Kinds.Troll, Kinds.Knight, StrokeKind.Smash), (Kinds.Rider, Kinds.Goblin, StrokeKind.Overhead), (Kinds.Hound, Kinds.Goblin, StrokeKind.Bite), (Kinds.Giant, Kinds.Orc, StrokeKind.Sweep),
        };
        const float yaw = -.3f, pitch = .42f, apart = 44, gap = 36;
        float sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
        var (gx, gz) = GroveAt;
        float cx = gx + 150, cz = gz + 110;
        int close = _look >= 4 && _look < 4 + pairs.Length ? _look - 4 : -1;
        var strikers = new List<(Unit a, Unit b, StrokeKind k)>();
        for (int i = 0; i < pairs.Length; i++)
        {
            var (ta, tb, k) = pairs[i];
            // four pairs to a row, the rows going away from the camera; the striker on the left, facing on along the row
            // to the right, so that its sword arm is toward the camera and its shield away from it — but one driving
            // its shield in, on the right with its shield toward the camera
            float along = (i % 4 - 1.5f) * apart, back = (i / 4 - 1) * gap, side = k == StrokeKind.Bash ? -1 : 1;
            float d = k == StrokeKind.Bash ? ta.BodyR + tb.BodyR + 3 : ta.Reach + tb.BodyR - 2;
            Unit Put(UnitType t, float al, float face)
            {
                float x = cx + al * cos - back * sin, z = cz - al * sin - back * cos;
                var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height));
                u.X = x; u.Z = z; u.Y = World.SurfAt((int)x, (int)z);
                u.Facing = face; u.Look = 0;
                u.Hp = u.MaxHp = 100000;
                if (t.Team == Team.Horde) { u.Guard = true; u.Home = u.Cell; } else { u.Hold = true; u.Post = u.Cell; u.Dest = u.Cell; }
                return u;
            }
            var a = Put(ta, along - side * d / 2, MathF.Atan2(-sin * side, cos * side));
            var b = Put(tb, along + side * d / 2, MathF.Atan2(sin * side, -cos * side));
            a.Target = b; b.Target = a;
            strikers.Add((a, b, k));
        }
        if (_look <= 0) Advance(_poseSeconds > 0 && _poseSeconds < 6 ? _poseSeconds : 2.5);
        else
        {
            // the game stopped at one moment of every blow (close up, --at=seconds after it, or before it below nought)
            Paused = true;
            bool at = close >= 0 && _poseSeconds > -1 && _poseSeconds < 1;
            float since = at ? (float)_poseSeconds : _look == 3 ? UnitView.FollowTime * .6f : 0;
            foreach (var (a, b, k) in strikers)
            {
                a.State = b.State = UnitState.Fight;
                a.NextStroke = k; a.StrokeFor = b;
                b.Cooldown = b.T.AttackTime;
                // drawn back all the way, the blade about to start down; or so long before the blow
                if (_look == 1 || since < 0) { a.Cooldown = _look == 1 ? UnitView.StrikeTime : -since; a.StrokeTime = -99; continue; }
                sim.Strike(a, b);
                a.Cooldown = a.T.AttackTime;
                a.StrokeTime = sim.Time - since;
            }
        }
        if (close >= 0)
        {
            var (a, b, _) = strikers[close];
            float mx = (a.X + b.X) / 2, mz = (a.Z + b.Z) / 2;
            float over = (World.WY(a.Y + 8) - 4) / MathF.Tan(pitch);
            Rig.Set(World.WX(mx) - sin * over, World.WZ(mz) - cos * over, yaw, pitch, a.T.BodyH > 18 || b.T.BodyH > 18 ? 6 : 8.5f, snap: true);
        }
        else
        {
            float over = (World.WY(World.SurfAt((int)cx, (int)cz) + 8) - 4) / MathF.Tan(pitch);
            Rig.Set(World.WX(cx) - sin * over, World.WZ(cz) - cos * over, yaw, pitch, 2.6f, snap: true);
        }
        var told = new List<string>();
        foreach (var (a, b, k) in strikers) told.Add($"{a.T.Key} {k} at {b.T.Key}");
        GD.Print($"[voxel-tower] the pairs fight round {cx:0},{cz:0}: {string.Join(", ", told)}");
        Hud.Refresh();
    }

    /// <summary>
    /// Pairs fighting hand to hand (--pose=reel), the game stopped just as the one struck at takes the blow its own way:
    /// an orc stepping back from a sword coming down, a goblin leaning out from under a slanting cut, one ducking a
    /// sweep, a shield taking an orc's axe, a swordsman turning one with his blade; an orc reeling from a knight's
    /// thrust, a goblin with its hand to its arm and its head swimming, one off its feet. --at=seconds since the blow
    /// (0.15 by itself); --look=4 and on one pair close.
    /// </summary>
    void Reeling()
    {
        var sim = Sim;
        sim.Waves.Countdown = 1e9;
        var pairs = new (UnitType a, UnitType b, StrokeKind k, Guarded g)[]
        {
            (Kinds.Swordsman, Kinds.Orc, StrokeKind.Overhead, Guarded.StepBack), (Kinds.Swordsman, Kinds.Goblin, StrokeKind.DiagR, Guarded.Dodge),
            (Kinds.Knight, Kinds.Goblin, StrokeKind.Sweep, Guarded.Duck), (Kinds.Orc, Kinds.Shieldbearer, StrokeKind.DiagL, Guarded.Block),
            (Kinds.Orc, Kinds.Swordsman, StrokeKind.Overhead, Guarded.Parry), (Kinds.Knight, Kinds.Orc, StrokeKind.Thrust, Guarded.None),
            (Kinds.Spearman, Kinds.Goblin, StrokeKind.Thrust, Guarded.None), (Kinds.Halberdier, Kinds.Goblin, StrokeKind.PoleChop, Guarded.None),
        };
        const float yaw = -.3f, pitch = .42f, apart = 44, gap = 36;
        float sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
        var (gx, gz) = GroveAt;
        float cx = gx + 150, cz = gz + 110;
        float since = _poseSeconds > 0 && _poseSeconds < 1 ? (float)_poseSeconds : .15f;
        int close = _look >= 4 && _look < 4 + pairs.Length ? _look - 4 : -1;
        var put = new List<(Unit a, Unit b)>();
        Paused = true;
        for (int i = 0; i < pairs.Length; i++)
        {
            var (ta, tb, k, g) = pairs[i];
            // four pairs to a row, as in the duels: the striker on the left, its sword arm toward the camera
            float along = (i % 4 - 1.5f) * apart, back = (i / 4 - .5f) * gap, d = ta.Reach + tb.BodyR - 2;
            Unit Put(UnitType t, float al, float face)
            {
                float x = cx + al * cos - back * sin, z = cz - al * sin - back * cos;
                var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height));
                u.X = x; u.Z = z; u.Y = World.SurfAt((int)x, (int)z);
                u.Facing = face; u.Look = 0;
                u.Hp = u.MaxHp = 100000;
                if (t.Team == Team.Horde) { u.Guard = true; u.Home = u.Cell; } else { u.Hold = true; u.Post = u.Cell; u.Dest = u.Cell; }
                return u;
            }
            var a = Put(ta, along - d / 2, MathF.Atan2(-sin, cos));
            var b = Put(tb, along + d / 2, MathF.Atan2(sin, -cos));
            a.Target = b; b.Target = a;
            a.State = b.State = UnitState.Fight;
            b.Cooldown = b.T.AttackTime;
            if (g is Guarded.StepBack or Guarded.Dodge or Guarded.Duck) sim.Missed(a, b, k, g);
            else if (g is Guarded.Block or Guarded.Parry) sim.Turned(a, b, k, g);
            else sim.Strike(a, b, .55f, .5f, k);
            a.Cooldown = a.T.AttackTime;
            a.StrokeTime = sim.Time - since;
            b.ReactAt = sim.Time - since;
            b.StagX = b.StagZ = 0;
            // what the blows that struck home did, held at their worst
            if (i == 5) b.StaggerUntil = sim.Time + .5;
            if (i == 6) { b.Clutch = (byte)BodyPart.Off; b.ClutchUntil = sim.Time + 3; b.DazedUntil = sim.Time + 3; b.Pain = .5f; }
            if (i == 7) { b.Down = true; b.DownSince = sim.Time - 2; b.DownUntil = double.PositiveInfinity; b.State = UnitState.Idle; }
            put.Add((a, b));
        }
        if (close >= 0)
        {
            var (a, b) = put[close];
            float mx = (a.X + b.X) / 2, mz = (a.Z + b.Z) / 2;
            float over = (World.WY(a.Y + 8) - 4) / MathF.Tan(pitch);
            Rig.Set(World.WX(mx) - sin * over, World.WZ(mz) - cos * over, yaw, pitch, 8.5f, snap: true);
        }
        else
        {
            float over = (World.WY(World.SurfAt((int)cx, (int)cz) + 8) - 4) / MathF.Tan(pitch);
            Rig.Set(World.WX(cx) - sin * over, World.WZ(cz) - cos * over, yaw, pitch, 3.4f, snap: true);
        }
        Hud.Refresh();
    }

    /// <summary>
    /// The dawn after a first night as a player of one's own sees it (--pose=dawn): a band of three with its leader
    /// cut down by the archer and the swordsman, nobody of ours lost, and the gifts to choose from.
    /// </summary>
    void DawnPose()
    {
        Sim.BeginErrands();
        Sim.Waves.Number = 1;
        Sim.NightBegins(1);
        var archer = Sim.Units.Find(u => u.T == Kinds.Archer);
        var sword = Sim.Units.Find(u => u.T == Kinds.Swordsman);
        var (gx, _, gz) = Sim.Nav.Feet(Sim.Gate);
        Sim.Waves.Active = true;
        for (int i = 0; i < 3; i++)
        {
            var g = Sim.Spawn(Kinds.Goblin, Sim.NearestStand((int)gx - 60, (int)gz + i * 6, 2));
            if (i == 0) g.Leader = true;
            // --look=1: the first chapter's champion among them
            if (i == 0 && _look == 1) { g.Champion = 1; g.Title = "Гнилозуб"; }
            Sim.Hurt(g, i == 1 ? sword : archer, 1000, null);
        }
        Sim.Waves.Active = false;
        Sim.Dawn(_look == 1 ? 3 : 1);
        Hud.Refresh();
    }

    /// <summary>
    /// The siege's first dusk as a player of one's own sees it (--pose=dusk; --at=seconds before the horn, 40 by itself):
    /// the errands on the left, the card of what comes tonight under the beam.
    /// </summary>
    void Dusk()
    {
        Sim.BeginErrands();
        // --look=n: the dusk before night n (3 — the first chapter's champion comes)
        if (_look > 1)
        {
            Sim.Waves.Number = _look - 1;
            Sim.PlanWave(_look);
        }
        Sim.Waves.Countdown = _poseSeconds > 0 ? _poseSeconds : 40;
        Hud.Refresh();
    }

    /// <summary>
    /// Work marked and under way (--pose=works, --at=seconds of it, 25 by itself): a stone wall, a ditch and a timber
    /// platform west of the citadel, each with its tag of how far it has got.
    /// </summary>
    void Works()
    {
        var sim = Sim; var s = Site;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX - s.KeepR - 40, kz = s.KeepZ;
        foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker }) sim.Spawn(t, sim.Gate);
        int top = Shapes.LevelTop(World, kx - 20, kz - 30, 16);
        var wall = Shapes.Line(World, s, kx - 20, kz - 50, kx - 20, kz - 10, new Stroke { Mat = Mat.Stone, Width = 6, Height = 16, Level = true }, top);
        sim.DesignateBuild(wall); Progress.Track("Камень", wall, false);
        var ditch = Shapes.Line(World, s, kx - 40, kz + 10, kx - 40, kz + 50, new Stroke { Mat = Mat.Air, Width = 8, Height = 8 }, World.SurfAt(kx - 40, kz + 30));
        sim.DesignateRemove(ditch); Progress.Track("Копать", ditch, true);
        int deck = Shapes.LevelTop(World, kx, kz + 30, 6);
        var plat = Shapes.Box(World, s, kx - 6, kz + 20, kx + 6, kz + 40, new Stroke { Mat = Mat.Wood, Height = 6, Level = true }, deck);
        sim.DesignateBuild(plat); Progress.Track("Брёвна", plat, false);
        Advance(_poseSeconds > 0 ? _poseSeconds : 25);
        const float yaw = -.3f, pitch = .62f;
        float over = (World.WY(top) - 4) / MathF.Tan(pitch);
        Rig.Set(World.WX(kx - 20) - MathF.Sin(yaw) * over, World.WZ(kz) - MathF.Cos(yaw) * over, yaw, pitch, 1.6f, snap: true);
        Hud.Refresh();
    }

    /// <summary>
    /// Blood spilt (--pose=blood, --at=seconds of fighting, 14 by itself): two swordsmen and a knight against goblins and
    /// an orc on cleared ground, close; the blows spatter the grass, the wounded drip as they go, the dead lie in their pools.
    /// </summary>
    void Bloodshed()
    {
        var sim = Sim;
        sim.Waves.Countdown = 1e9;
        var (gx, gz) = GroveAt;
        float cx = gx + 150, cz = gz + 110;
        void Stand(UnitType t, float x, float z)
        {
            var u = sim.Spawn(t, sim.NearestStand((int)(cx + x), (int)(cz + z), t.Move.Height));
            if (t.Team == Team.Player) { u.Hold = true; u.Post = u.Dest = u.Cell; } else { u.Guard = true; u.Home = u.Cell; }
        }
        Stand(Kinds.Swordsman, -12, -10); Stand(Kinds.Swordsman, -12, 10); Stand(Kinds.Knight, -16, 0);
        Stand(Kinds.Goblin, 2, -12); Stand(Kinds.Goblin, 2, 12); Stand(Kinds.Goblin, 0, 0); Stand(Kinds.Goblin, 6, 6); Stand(Kinds.Orc, 6, -4);
        Advance(_poseSeconds > 0 ? _poseSeconds : 14);
        const float yaw = -.3f, pitch = .5f;
        float over = (World.WY(World.SurfAt((int)cx, (int)cz) + 8) - 4) / MathF.Tan(pitch);
        Rig.Set(World.WX(cx) - MathF.Sin(yaw) * over, World.WZ(cz) - MathF.Cos(yaw) * over, yaw, pitch, 4, snap: true);
        Hud.Refresh();
    }

    /// <summary>
    /// The troops the lore opens, out before the walls to meet a band (--pose=troops, --at=seconds into the fight): a line
    /// of spearmen, shieldbearers, halberdiers and knights, slingers, crossbowmen and bombardiers behind it, a giant and a
    /// tame troll behind them, the hounds on one wing and the riders, horse archers and lancers on the other; all their
    /// lore learnt. Goblins, orcs, sappers, a troll and a ram come at them from the west. --look=1 frames the line close.
    /// </summary>
    void Troops()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        foreach (string k in new[] { "shieldwall", "longshafts", "leadshot", "bodkins", "firepots", "hunt", "charge", "boulders" }) sim.Learnt.Add(k);
        int kx = s.KeepX, kz = s.KeepZ, line = kx - s.KeepR - 70;
        sim.InstantLabour = true;
        int top = Shapes.LevelTop(w, kx - 64, kz, 40);
        sim.DesignateBuild(Shapes.Circle(w, s, kx, kz, s.KeepR + 44, new Stroke { Mat = Mat.Stone, Width = 10, Height = 40, Level = true, Hollow = true }, top));
        sim.InstantLabour = false;
        void Stand(UnitType t, int x, int z)
        {
            int c = sim.NearestStand(x, z, t.Move.Height);
            if (c < 0) return;
            var u = sim.Spawn(t, c);
            sim.Command(new[] { u }, c);
            u.Facing = MathF.PI;
        }
        var foot = new[] { Kinds.Spearman, Kinds.Shieldbearer, Kinds.Halberdier, Kinds.Knight, Kinds.Spearman, Kinds.Shieldbearer, Kinds.Knight, Kinds.Halberdier, Kinds.Spearman };
        for (int i = 0; i < foot.Length; i++) Stand(foot[i], line, kz - 36 + i * 9);
        var missiles = new[] { Kinds.Slinger, Kinds.Crossbowman, Kinds.Bombardier, Kinds.Slinger, Kinds.Crossbowman, Kinds.Bombardier, Kinds.Slinger };
        for (int i = 0; i < missiles.Length; i++) Stand(missiles[i], line + 16, kz - 30 + i * 10);
        Stand(Kinds.Giant, line + 30, kz - 8);
        Stand(Kinds.TameTroll, line + 30, kz + 14);
        foreach (int dz in new[] { -58, -66, -74 }) Stand(Kinds.Hound, line - 4, kz + dz);
        foreach (var (t, dx, dz) in new[] { (Kinds.Rider, 0, 56), (Kinds.Rider, 8, 66), (Kinds.Lancer, -6, 76), (Kinds.HorseArcher, 18, 58), (Kinds.HorseArcher, 18, 72) })
            Stand(t, line + dx, kz + dz);
        // a band come by day, ahead of its wave, so the fight is seen in the light
        Advance(1);
        sim.HordeStock[Stockpile.Wood] = 24 * VoxelWorld.PerM3;
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, -80, -20), (Kinds.Goblin, -84, 6), (Kinds.Goblin, -78, 26), (Kinds.Goblin, -90, -40), (Kinds.Orc, -94, -8),
            (Kinds.Orc, -96, 14), (Kinds.Orc, -100, 36), (Kinds.Troll, -110, 0), (Kinds.Sapper, -104, -26), (Kinds.Sapper, -106, 48), (Kinds.Goblin, -88, 60),
            (Kinds.Orc, -98, -56), (Kinds.Ram, -150, 10) })
        {
            int c = sim.NearestStand(line + dx, kz + dz, t.Move.Height);
            if (c >= 0) sim.Spawn(t, c);
        }
        sim.ComputeFields();
        Advance(_poseSeconds);
        if (_look == 1) Rig.Set(World.WX(line - 20), World.WZ(kz + 4), -.75f, .5f, 3.2f, snap: true);
        else Rig.Set(World.WX(line - 34), World.WZ(kz + 10), -.75f, .55f, 1.7f, snap: true);
        Hud.Refresh();
        Marks.Flush();
    }

    void LookAtGrove()
    {
        Sim.Waves.Countdown = 1e9;
        var (gx, gz) = GroveAt;
        int cx = gx + 148, cz = gz + 110;
        float yaw = -.2f, pitch = .36f, zoom = _look is >= 0 and < 3 ? 1.35f : 1f;
        // the camera looks at a plane 4 m up: the trees' middles are higher, so look past it by as much
        float over = (World.WY(World.SurfAt(cx, cz) + 40) - 4) / MathF.Tan(pitch);
        Rig.Set(World.WX(cx) - MathF.Sin(yaw) * over, World.WZ(cz) - MathF.Cos(yaw) * over, yaw, pitch, zoom, snap: true);
        Hud.Refresh();
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
        if (_pose is "battle" or "close" or "flood" or "troops") return;
        int x = 720, z = 640;
        Tools.PointAt(new Vector3(World.WX(x), World.WY(World.SurfAt(x, z)), World.WZ(z)));
    }
}
