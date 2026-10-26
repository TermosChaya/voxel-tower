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
/// seconds into it, --look=1 closer), --pose=beasts: the beasts going by close, at a walk, bolting and grazing (--look=1..3
/// one of them closer), --pose=walk: people walking over a step of stone (--look=1 felled on its edge, 2 a blast among
/// them as they lie),
/// --selftest drives the tools and the pawns and checks the results, --calm stops the embers and the flicker,
/// --top looks straight down, --look=N at the Nth named place of the valley, --seed=N makes another valley; --select[=N]
/// chooses our pawns (or the first N), --tab=key lets a panel down from the beam, --tool=N takes up the Nth tool,
/// --ui-scale=S draws the interface at that size instead of the player's own, --inspect looks at the first raider (or beast),
/// --unfold shows the frame as a new siege of one's own begins it, its later tools and the lore not yet let out,
/// --keep opens the citadel's muster,
/// --day=F starts at that time of the day (0 dawn, 1 sunset), --alarm=S raises the alarm and runs on S seconds,
/// --weather=rain|fog|wind brings that weather at once, --envoy sends the horde's envoy and waits for its offer,
/// --lore=N,key,… puts N knowledge in hand with that lore learnt, --sounds=folder writes every sound out as a .wav and quits,
/// --load=file.sav reads a saved game back, --sandbox opens the sandbox with everything learnt and plenty of all (the
/// horde on call, O calls up a band of it under the pointer), --trailer=N stages and films the Nth shot of the trailer (Main.Trailer.cs).
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
    public TownPlates Plates { get; private set; }
    public ShoutTags Shouts { get; private set; }
    public UnitView Units { get; private set; }
    public OrdersView Marks { get; private set; }
    public ZoneView Areas { get; private set; }
    public Commander Command { get; private set; }
    /// <summary>First person: the player in one of ours.</summary>
    public FirstPerson FP { get; private set; }
    /// <summary>The rings where the player sent its people (OrderMarks.cs).</summary>
    public OrderMarks Rings { get; private set; }
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
    /// <summary>The marks over the horde's mines that ours have heard.</summary>
    public TunnelView Tunnels;
    public CollapseView Collapse;
    /// <summary>The great moments made to be felt: the view shaken, the screen flashed, the time slowed a moment.</summary>
    public Spectacle Spectacle;
    /// <summary>A run driven by a script (a test, a staged scene, a screenshot), not by a hand on the mouse.</summary>
    public bool Scripted => _selfTest || _demo || _shot != null || _tutCheck;

    const string BlueprintFile = "user://blueprints.json";

    /// <summary>A saved game to read back when the scene starts (set, then the scene started again: <see cref="Saves.Read"/>).</summary>
    public static string PendingLoad;
    /// <summary>
    /// What the next start of the scene begins with, asked from the menu: "game" — a new siege, "tutorial" — the tutorial,
    /// "sandbox" — the sandbox with everything open, "sandbox-own" — a siege of one's own with the horde on call; none — the title.
    /// </summary>
    public static string PendingStart;
    /// <summary>How hard the siege begun from the menu in play is to be (the scene begins again for it).</summary>
    public static Hardness? PendingHardness;
    /// <summary>The tutorial, while this game is one.</summary>
    public Tutorial Tutor { get; private set; }
    /// <summary>--tutorial or --tutorial=N: the tutorial from its Nth step; --title: the title over the valley (screenshots, tests).</summary>
    int _tutorialAt = -1;
    bool _titleArg;
    /// <summary>--sandbox: the sandbox with everything open and plenty of all, to try every part of it at once (Sim.Sandbox.cs).</summary>
    bool _sandbox;
    /// <summary>--captain: a commander with the household in a scripted run too (a game of one's own has one by the setting).</summary>
    bool _captainArg;
    /// <summary>The seed the valley was made from: what a save keeps of it.</summary>
    public uint Seed => _seed;
    bool _loaded;
    // the raids beaten back as last looked at: one more is written down by itself
    int _repelledSeen;
    /// <summary>What brings the raids, asked for on the command line (--raids=siege|call|map).</summary>
    string _raidsArg;
    string _loadedFrom;
    /// <summary>--menu or --menu=save|load|settings: the menu open at the start (screenshots).</summary>
    string _menu;

    string _shot;
    int _shotFrames = 45, _frames;
    bool _demo, _calm, _selfTest, _finale, _top, _inspect, _keep, _unfold;
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
    /// <summary>The kind of valley to make (--valley=first|ore|sacred|forest|marsh|mountain), whether the first keeps every
    /// place it always had (--classic=0 takes them away), and whether the land is a hexagon (--square for the old box).</summary>
    ValleyKind _valley = ValleyKind.First;
    bool _classic = true, _hexagon = true;
    /// <summary>The world's box across (voxels) when not the default (--size=1536; a multiple of 32).</summary>
    int _size;
    /// <summary>The side of the hexagon the horde comes by (--entry=0…5: north-east, east, south-east, south-west, west, north-west).</summary>
    int _entry = Hex.West;
    /// <summary>What stands in the middle of the valley (--holding=citadel|outpost|horde), and its sides as a save keeps them.</summary>
    Holding _holding = Holding.Citadel;
    HexEdge[] _edges;
    string _pose = "";
    /// <summary>How long the staged siege runs before the screenshot (--at=seconds).</summary>
    double _poseSeconds = 14;
    /// <summary>--ui-scale=S: the interface drawn at this size for this run, whatever the player set.</summary>
    float _uiScale;
    /// <summary>--lines: the voxels drawn with their lines round them, whatever the player set.</summary>
    bool _lines;

    public override void _Ready()
    {
        // a sweep of the oldest garbage stops the game for tens of milliseconds: put off while it can be, and done
        // alongside the game rather than in its way
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--shot=")) _shot = a["--shot=".Length..];
            else if (a.StartsWith("--frames=") && int.TryParse(a["--frames=".Length..], out int f)) _shotFrames = f;
            else if (a == "--demo") _demo = true;
            else if (a == "--calm") _calm = true;
            else if (a == "--top") _top = true;
            else if (a.StartsWith("--seed=") && uint.TryParse(a["--seed=".Length..], out uint sd)) _seed = sd;
            else if (a.StartsWith("--valley=") && Valleys.Parse(a["--valley=".Length..]) is { } vk) _valley = vk;
            else if (a == "--classic=0") _classic = false;
            else if (a == "--square") _hexagon = false;
            else if (a.StartsWith("--size=") && int.TryParse(a["--size=".Length..], out int sz) && sz >= 256) _size = sz / 32 * 32;
            else if (a.StartsWith("--entry=") && int.TryParse(a["--entry=".Length..], out int es) && es is >= 0 and < 6) _entry = es;
            else if (a.StartsWith("--holding=")) _holding = a["--holding=".Length..] switch { "outpost" => Holding.Outpost, "horde" => Holding.HordeTown, _ => Holding.Citadel };
            else if (a.StartsWith("--look=") && int.TryParse(a["--look=".Length..], out int lk)) _look = lk;
            else if (a.StartsWith("--wave=") && double.TryParse(a["--wave=".Length..], System.Globalization.CultureInfo.InvariantCulture, out double wa)) _waveAt = wa;
            else if (a.StartsWith("--day=") && double.TryParse(a["--day=".Length..], System.Globalization.CultureInfo.InvariantCulture, out double da)) _dayAt = da;
            else if (a.StartsWith("--alarm=") && double.TryParse(a["--alarm=".Length..], System.Globalization.CultureInfo.InvariantCulture, out double al)) _alarmAt = al;
            else if (a.StartsWith("--weather=")) _weather = a["--weather=".Length..];
            else if (a == "--envoy") _envoy = true;
            else if (a.StartsWith("--raids=")) _raidsArg = a["--raids=".Length..];
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
            else if (a == "--keep") _keep = true;
            else if (a == "--unfold") _unfold = true;
            else if (a.StartsWith("--load=")) PendingLoad = a["--load=".Length..];
            else if (a == "--tutorial") _tutorialAt = 0;
            else if (a.StartsWith("--tutorial=") && int.TryParse(a["--tutorial=".Length..], out int ta)) _tutorialAt = ta;
            else if (a == "--title") _titleArg = true;
            else if (a == "--sandbox") _sandbox = true;
            else if (a == "--captain") _captainArg = true;
            else if (a == "--tutorial-check") { _tutCheck = true; _tutorialAt = 0; }
            else if (a == "--menu" || a.StartsWith("--menu=")) _menu = a.Length > 7 ? a[7..] : "";
            else if (a.StartsWith("--pose=")) _pose = a["--pose=".Length..];
            else if (a.StartsWith("--quality=") && int.TryParse(a["--quality=".Length..], out int qa)) _quality = qa;
            else if (a.StartsWith("--perf=") && double.TryParse(a["--perf=".Length..], System.Globalization.CultureInfo.InvariantCulture, out double pf)) _perf = pf;
            else if (a.StartsWith("--perf-without=")) _perfWithout = a["--perf-without=".Length..].Split(',');
            else if (a == "--spikes") Hitches.Tell = true;
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
            try { save = new SaveReader(System.IO.File.OpenRead(PendingLoad)); _seed = save.Seed; _valley = save.Kind; _classic = save.Classic; _hexagon = save.Hexagon; _size = save.SizeX; _entry = save.EntrySide; _holding = save.Holding; _edges = save.Edges; }
            catch (Exception e) { GD.PrintErr($"[voxel-tower] could not read {PendingLoad}: {e.Message}"); save?.Dispose(); save = null; }
            PendingLoad = null;
            _demo = _selfTest = false;
        }
        // every body sculpted while the valley is made, off the main thread: a body first wanted held up its frame
        Task.Run(() => Bodies.Warm(Kinds.All()));
        var sw = Stopwatch.StartNew();
        var mp = new MapParams { Seed = _seed, Kind = _valley, Classic = _classic, Hexagon = _hexagon, EntrySide = _entry, Holding = _holding, Edges = _edges };
        // the game's valley: a hexagon in a box a half again as wide as the bench's (192 m from point to point)
        if (_hexagon) mp.SizeX = mp.SizeZ = MapParams.GameSize;
        if (_size > 0) mp.SizeX = mp.SizeZ = _size;
        World = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        Site = MapGen.Generate(World, mp);
        if (_demo && _pose == "trees") Grove();
        if (_demo && _pose is "people" or "duel" or "reel" or "blood" or "beasts" or "walk" or "trauma" or "scaffold" or "carry" or "felling" or "pit") ClearGrove();
        // the field of the traps cleared of trees, so they are to be seen
        if (_demo && _pose == "engines") ClearTrees(Site.KeepX - Site.KeepR - 420, Site.KeepZ - 140, Site.KeepX - Site.KeepR - 20, Site.KeepZ + 140);
        if (_demo && _pose == "ourengines") ClearTrees(Site.KeepX - Site.KeepR - 720, Site.KeepZ - 160, Site.KeepX - Site.KeepR - 10, Site.KeepZ + 160);
        if (_demo && _pose == "drawbridge") ClearTrees(Site.KeepX - Site.KeepR - 220, Site.KeepZ - 120, Site.KeepX - Site.KeepR - 30, Site.KeepZ + 120);
        if (_demo && _pose == "ouralchemist") ClearTrees(Site.KeepX - Site.KeepR - 260, Site.KeepZ - 90, Site.KeepX - Site.KeepR - 40, Site.KeepZ + 90);
        if (_demo && _pose == "acid") ClearTrees(Site.KeepX - Site.KeepR - 260, Site.KeepZ - 90, Site.KeepX - Site.KeepR - 40, Site.KeepZ + 90);
        if (_demo && _pose == "countermine") ClearTrees(Site.KeepX - Site.KeepR - 420, Site.KeepZ - 110, Site.KeepX - Site.KeepR - 60, Site.KeepZ + 110);
        if (_demo && _pose == "shaman") ClearTrees(Site.KeepX - Site.KeepR - 240, Site.KeepZ - 90, Site.KeepX - Site.KeepR - 40, Site.KeepZ + 90);
        if (_demo && _pose == "spells") ClearTrees(Site.KeepX - Site.KeepR - 260, Site.KeepZ - 100, Site.KeepX - Site.KeepR - 40, Site.KeepZ + 120);
        if (_demo && _pose == "traps") ClearTrees(Site.KeepX - Site.KeepR - 220, Site.KeepZ - 120, Site.KeepX - Site.KeepR - 40, Site.KeepZ + 120);
        if (_demo && _pose == "square") SquareGround();
        if (_trailer >= 0) TrailerGround();
        if (_demo && _pose is "blast" or "grip" or "wind" or "hexes" or "sorcerers" or "wonders" or "fp" or "powder" or "charge" or "stones" or "topple" or "roll" or "crush" or "drops" or "troops" or "wave" or "lava") ClearTrees(Site.KeepX - Site.KeepR - 300, Site.KeepZ - 140, Site.KeepX - Site.KeepR - 30, Site.KeepZ + 140);
        World.Compact();
        save?.Valley(World, Site);
        double genMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        Sim = new Sim(World, Site);
        // a new game finds a fort half raised round the citadel; scripted runs set up their own scenes
        string extra = null;
        if (save != null)
        {
            try { extra = Sim.Load(save); _loaded = true; }
            catch (Exception e) { GD.PrintErr($"[voxel-tower] the save is broken: {e}"); }
            save.Dispose();
        }
        else if (!_selfTest && !_demo && Site.HasKeep) StartingFort.Raise(Sim);
        // the horde's town has its garrison from the start
        if (save == null && Site.Holding == Holding.HordeTown) Sim.Garrison(Sim.Roster(Campaign.LairThreat, 1));
        double navMs = sw.Elapsed.TotalMilliseconds;

        var atmo = Atmos = new Atmosphere();
        AddChild(atmo);
        atmo.Build(World, Site, _calm || _shot != null);
        atmo.Watch(Sim);

        View = new WorldView();
        AddChild(View);
        View.Init(World);
        // in play a chunk changed is meshed off the main thread; pictures, the self-test and the trailer want it at once
        View.Async = _shot == null && !_selfTest && !_tutCheck && _trailer < 0;
        View.InitFlow(Site);

        Rig = new CameraRig { BottomInset = Hud.BottomBand, TopInset = Hud.TopBand };
        Rig.Bounds = new Rect2(World.OriginX, World.OriginZ, World.SX * VoxelWorld.Vox, World.SZ * VoxelWorld.Vox);
        // out far enough to see the whole valley however wide it is
        Rig.MinZoom = .28f * 1024f / Math.Max(World.SX, World.SZ);
        Rig.GroundLow = World.WY(0); Rig.GroundHigh = World.WY(World.SY);
        Rig.Sun = Atmos.Sun;
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
        Marks.Async = View.Async;
        Areas = new ZoneView();
        AddChild(Areas);
        Areas.Init(Sim);
        Areas.ShowAll = () => Tools?.Tool is Tool.Gather or Tool.Unmark;
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
        var acid = new AcidView();
        AddChild(acid);
        acid.Init(this);
        var chimneys = new ChimneyView();
        AddChild(chimneys);
        chimneys.Init(this);
        var boulders = new BoulderTrail();
        AddChild(boulders);
        boulders.Init(this);
        Tunnels = new TunnelView();
        AddChild(Tunnels);
        Tunnels.Init(this);
        var lava = new LavaView();
        AddChild(lava);
        lava.Init(this);
        Magic = new MagicView();
        AddChild(Magic);
        Magic.Init(this);
        Collapse = new CollapseView();
        AddChild(Collapse);
        Sound = new Sound();
        AddChild(Sound);
        Sound.Init(this);
        Spectacle = new Spectacle();
        AddChild(Spectacle);
        Spectacle.Init(this);
        Collapse.Init(this);
        Progress = new WorkProgress();
        AddChild(Progress);
        Progress.Init(this);
        var pins = new PinTags();
        AddChild(pins);
        pins.Init(this);
        // a feller's «Берегись!» over the one who calls it (Ui/ShoutTags.cs)
        Shouts = new ShoutTags();
        AddChild(Shouts);
        Shouts.Init(this);
        // the town's buildings, named over their roofs (Ui/Town.cs)
        Plates = new TownPlates();
        AddChild(Plates);
        Plates.Init(this);
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
        Rings = new OrderMarks();
        AddChild(Rings);
        Rings.Init(this);
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
            Hud.Unfold.Apply();
            Hud.Log($"Игра загружена: {Saves.NameOf(System.IO.Path.GetFileNameWithoutExtension(_loadedFrom ?? ""))}.", Hud.Kind.Ok);
            _repelledSeen = Sim.Raids.Repelled;
            GD.Print($"[voxel-tower] world {genMs:0} ms, navigation {navMs:0} ms, mesh {View.LastFullMeshMs:0} ms, {View.Triangles()} triangles; loaded");
            return;
        }
        // the household the siege finds at the citadel, the beasts of the valley, and the horde's camp at a mine
        foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Archer, Kinds.Swordsman }) Sim.Spawn(t, Sim.Gate);
        if (_captainArg) Sim.Spawn(Kinds.Captain, Sim.Gate);
        Sim.SeedWildlife();
        if (!_selfTest && !_demo) Sim.SeedMines();

        // a game of one's own opens on the title over the valley; the tutorial, asked for, on its first step
        _startTutorial = PendingStart == "tutorial" || _tutorialAt >= 0;
        string sandbox = _sandbox ? "sandbox" : PendingStart is "sandbox" or "sandbox-own" ? PendingStart : null;
        _startTitle = !_startTutorial && sandbox == null && (_titleArg || (PendingStart == null && !Scripted));
        PendingStart = null;
        if (PendingHardness is { } hard) { Sim.SetHardness(hard); PendingHardness = null; }
        if (!_startTutorial && !_startTitle)
        {
            if (sandbox != null) StartSandbox(open: sandbox == "sandbox");
            else Welcome();
        }
        GD.Print($"[voxel-tower] world {genMs:0} ms, navigation {navMs:0} ms, mesh {View.LastFullMeshMs:0} ms, {View.Triangles()} triangles");
        // another source of raids asked for: the sandbox's call, or the map's way (raids only by Sim.Raid, the nights going by)
        if (_raidsArg != null)
        {
            Sim.SetSource(Sim.SourceOf(_raidsArg));
            if (!Sim.Source.Foretold) Sim.Bands.RemoveAll(b => b.State == BandState.Coming);
            Hud.Refresh();
        }

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
        if (_keep) Command.OpenKeep();
        if (_unfold) Hud.Unfold.Begin();
        if (_tool >= 0 && _tool < Hud.ToolDefs.Length) Tools.SetTool(Hud.ToolDefs[_tool].tool);
        if (_grid >= 0) Tools.SetGrid(_grid);
        if (_cam is { Length: 5 }) Rig.Set(World.WX(_cam[0]), World.WZ(_cam[1]), _cam[2], _cam[3], _cam[4], snap: true);
        if (_top) Rig.ToggleTopDown();
        if (_startTutorial) StartTutorial(Math.Max(0, _tutorialAt));
        else if (_startTitle) Hud.ShowTitle();
        if (_selfTest) SelfTest();
        if (_tutCheck) TutorialCheck();
        if (_perf > 0) Perf(_perf);
        else if (Hitches.Tell) Hitches.Start(GetViewport());
        // a hidden or minimised window never draws a frame: don't let scripted runs hang (but a long self-test on a
        // busy machine draws its frames all the while, and is let run)
        int drawnAtStart = Engine.GetFramesDrawn();
        if (_shot != null || _selfTest) GetTree().CreateTimer(120).Timeout += () =>
        {
            if (_selfTest && Engine.GetFramesDrawn() > drawnAtStart + 10) return;
            GD.PrintErr("[voxel-tower] gave up waiting for a frame"); GetTree().Quit(2);
        };
        // from here on a frame that does not come is a hang, and is written down
        Diag.Start(Sim, Scripted || _trailer >= 0);
        if (_startTitle && Diag.LastEndedBadly) Hud.OpenMenu("report");
    }

    public override void _ExitTree() => Diag.Stop();

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) Diag.Stop();
    }

    bool _startTutorial, _startTitle;
    int _quality = -1;
    double _perf;
    /// <summary>--perf-without=world,water,grass,units,shadows,penumbra,filter,far,post: what the measurement is made without, to see what it costs.</summary>
    string[] _perfWithout = Array.Empty<string>();

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
        foreach (string part in _perfWithout)
            switch (part)
            {
                case "world": View.ShowParts(solid: false, water: true); break;
                case "water": View.ShowParts(solid: true, water: false); break;
                case "grass": View.Grass.Visible = false; break;
                case "units": Units.Visible = false; break;
                case "shadows": Atmos.Sun.ShadowEnabled = false; break;
                case "penumbra": Atmos.Sun.LightAngularDistance = 0; break;
                case "filter": RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftLow); break;
                case "msaa": GetViewport().Msaa3D = Viewport.Msaa.Disabled; break;
                case "glow": if (GetViewport().World3D.Environment is { } glowEnv) glowEnv.GlowEnabled = false; break;
                case "far": if (Atmos.Far != null) Atmos.Far.Visible = false; break;
                case "post": GetViewport().Msaa3D = Viewport.Msaa.Disabled; if (GetViewport().World3D.Environment is { } env) { env.SsaoEnabled = false; env.GlowEnabled = false; env.SsilEnabled = false; } break;
            }
        if (_perfWithout.Length > 0) GD.Print($"[perf] without {string.Join(", ", _perfWithout)}");
        for (int i = 0; i < 180; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ulong t0 = Time.GetTicksUsec(), last = t0;
        int frames = 0;
        double worst = 0, process = 0;
        _simTicks = 0;
        Prof.On = true; Prof.Reset();
        Hitches.Start(GetViewport());
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
        GD.Print($"[perf] KB allocated a frame on the main thread by: {Prof.Allocating(frames)}");
        GD.Print($"[perf] late frames: {Hitches.Over25} over 25 ms, {Hitches.Over50} over 50 ms of {Hitches.Frames}; {Hitches.Allocated / 1048576.0 / took:0.0} MB allocated a second; " +
            $"{RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576} MB video memory ({RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.BufferMemUsed) / 1048576} in buffers, {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) / 1048576} in textures), {OS.GetStaticMemoryUsage() / 1048576} MB the engine's memory, {GC.GetTotalMemory(false) / 1048576} MB the managed heap, {System.Environment.WorkingSet / 1048576} MB in all");
        GetTree().Quit();
    }

    /// <summary>
    /// The sandbox: the horde comes only when called. Open, everything is learnt and the stores are never short; else it
    /// is a siege of one's own, errands and all, with only the clock standing.
    /// </summary>
    public void StartSandbox(bool open)
    {
        if (open)
        {
            Sim.BeginSandbox();
            Hud.Log("Песочница: всё изучено, запасы не кончаются, заклинания бесплатные и без перезарядки, цитадель не падает.", Hud.Kind.Ok);
        }
        else
        {
            Sim.Waves.OnCall = true;
            if (!Scripted) { Sim.BeginErrands(); GiveCaptain(); }
            Hud.Log("Песочница своими силами: всё как в осаде, только орда не придёт, пока её не позовёте.", Hud.Kind.Ok);
        }
        Hud.Log("{CallWave} — позвать волну. {Summon} — ватага орды под курсором, Shift+{Summon} — тяжёлая ватага, Ctrl+{Summon} — войско с военачальником.", Hud.Kind.Ok);
        Hud.Refresh();
    }

    /// <summary>The tips of the start, into the chronicle: a game of one's own begins with them.</summary>
    public void Welcome()
    {
        // a siege of one's own: what to do next is in the errands from the first minute, the frame unfolding as it goes
        if (!Scripted)
        {
            Sim.BeginErrands();
            Hud.Unfold.Begin();
            GiveCaptain();
            Hud.Log("Орда придёт через 10 минут.", Hud.Kind.Warn);
            Hud.Log("Что делать дальше, написано слева в «Поручениях». {Help} — все клавиши.", Hud.Kind.Ok);
            return;
        }
        Hud.Log($"Орда придёт {Sim.EdgeName(Sim.HomeEdge)} через 10 минут.", Hud.Kind.Warn);
    }

    /// <summary>A commander with the household at the start of a siege of one's own, to try it (the «Командир в начале» setting).</summary>
    void GiveCaptain()
    {
        if (!Settings.StartCaptain || Sim.Units.Exists(u => u.T == Kinds.Captain && !u.Dead)) return;
        Sim.Spawn(Kinds.Captain, Sim.Gate);
        Hud.Log("С вами командир. Он сам строит бойцов к бою. Выберите его и вселитесь ({Possess}): 1–4 — призывы, {FpOrder} — приказ взглядом.", Hud.Kind.Ok);
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

    /// <summary>The sandbox begun from the menu in play: the scene begins again for it.</summary>
    public void NewSandbox(bool open)
    {
        PendingStart = open ? "sandbox" : "sandbox-own";
        GetTree().ReloadCurrentScene();
    }

    double _slowest;
    // the time the simulation took, for --perf
    long _simTicks;

    public override void _Process(double delta)
    {
        Diag.Beat(GetWindow().HasFocus());
        Hitches.Frame(Sim);
        if (_frames > 5) _slowest = Math.Max(_slowest, delta);
        if (_trailer >= 0) Roll();
        // under the title the valley turns slowly round the citadel
        if (Hud.TitleOpen) { var a = Rig.Aim; Rig.Set(a.x, a.z, a.yaw + (float)delta * .045f, a.pitch, a.zoom, snap: false); }
        if (!Paused && !_selfTest)
        {
            double dt = Math.Min(delta, .1) * Speed * Slow * Pace;
            long t0 = Stopwatch.GetTimestamp();
            using (Prof.Time("Sim"))
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
            if (Sim.Raids.Repelled > _repelledSeen && !Sim.Over && Tutor is not { Over: false } && Saves.Write(this, Saves.Auto)) Hud.Log("Автосохранение: волна отбита.", Hud.Kind.Ok, toast: false);
            _repelledSeen = Sim.Raids.Repelled;
        }
        Command.Prune();
        using (Prof.Time("Remesh"))
        {
            // the valley a level coarser from far off: a voxel under two pixels or so (the grass reckons them the same way)
            View.ChooseLevel(GetTree().Root.Size.Y * Settings.Details[Settings.Detail] / Math.Max(.01f, Rig.Cam.Size) * VoxelWorld.Vox, FP.On);
            View.RemeshDirty();
            View.RemeshShadows();
            View.RemeshFar();
        }
        using (Prof.Time("Flow")) View.UpdateFlow(Sim);
        if (Sim.Over && !_finale)
        {
            _finale = true;
            int all = Sim.Source.Total(Sim);
            if (Sim.Won) Hud.Finale("Осада снята", $"Отбито волн: {Sim.Raids.Repelled}. Налётчиков убито: {Sim.Kills}.", true);
            else Hud.Finale("Цитадель пала", $"Отбито волн: {Sim.Raids.Repelled}" + (all > 0 ? $" из {all}" : "") + $". Налётчиков убито: {Sim.Kills}.", false);
        }
        // the cursor laid over a voxel for a screenshot, once the view has settled
        if (_shot != null && _aim is { Length: 2 } && _frames == Math.Max(2, _shotFrames - 8))
            Tools._UnhandledInput(new InputEventMouseMotion { Position = Rig.Cam.UnprojectPosition(new Vector3(World.WX(_aim[0] + .5f), World.WY(World.SurfAt((int)_aim[0], (int)_aim[1])), World.WZ(_aim[1] + .5f))) });
        // what a staged scene does with the cursor once the view has settled (--pose=wand)
        if (_shot != null && _poseLater != null && _frames == Math.Max(2, _shotFrames - 8)) { var later = _poseLater; _poseLater = null; later(); }
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
}
