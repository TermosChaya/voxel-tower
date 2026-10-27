using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The trailer, shot by shot (--trailer=N, see tools/trailer.command): each shot is staged in a run of its own, filmed
/// through the <see cref="CineCam"/> and written out by Godot's movie maker (--write-movie, --fixed-fps 30). A shot is a
/// set-up, the moments its timeline sets off (a spell cast, a band let loose, kegs lit), the camera's move, the light, how
/// fast the game runs (a time-lapse, slow motion), and the words over it, in the game's own letters, between black bars.
/// It opens on black while the valley's meshes catch up with the set-up (the log says for how many frames, to be cut),
/// fades in, plays, fades out and quits. --stills=folder:t1,t2,… writes the frames at those seconds as pictures instead,
/// --lens=x,y,z,x,y,z[,fov] (voxels) holds the camera there to frame a shot, and --dry runs the shot's game without
/// drawing it, saying how it goes.
/// </summary>
public partial class Main
{
    /// <summary>--trailer=N: the Nth shot of the trailer is staged and filmed.</summary>
    int _trailer = -1;
    /// <summary>The trailer's camera.</summary>
    public CineCam Cine { get; private set; }
    /// <summary>How much faster than as played the trailer runs the game: a time-lapse over 1.</summary>
    public double Pace = 1;

    /// <summary>The frames of film a second.</summary>
    const int Fps = 30;

    /// <summary>
    /// Words over a shot: from when to when, large and small, in the middle or low in the frame, or the title; or (Corner)
    /// the plain name of what the shot shows, a few small letters in the corner of the black under the picture.
    /// </summary>
    sealed record Card(float From, float To, string Big, string Small = "", bool Low = false, bool Title = false, bool High = false, bool Corner = false);

    /// <summary>One shot of the trailer.</summary>
    sealed class Shot
    {
        public string Name;
        /// <summary>Seconds of film.</summary>
        public float Length;
        /// <summary>Trees taken off these stretches of the valley (voxels) before it is drawn.</summary>
        public (int x0, int z0, int x1, int z1)[] Clear = Array.Empty<(int, int, int, int)>();
        /// <summary>Whether the boulders go off those stretches too, not only the trees.</summary>
        public bool ClearRock;
        /// <summary>The set-up, once, before the first frame.</summary>
        public Action Stage;
        /// <summary>What happens at what second.</summary>
        public readonly List<(float at, Action act)> Moments = new();
        /// <summary>The camera at a second of the shot.</summary>
        public Func<float, Lens> Lens;
        /// <summary>The time of day and the darkness at a second of the shot (null: the sim's own).</summary>
        public Func<float, float> Day, Dark;
        /// <summary>How fast the game runs at a second of the shot: over 1 a time-lapse, under 1 slow motion.</summary>
        public Func<float, float> Pace;
        public readonly List<Card> Cards = new();
        /// <summary>Seconds of fading from black at the start and to black at the end (the cuts between the shots are made by tools/trailer.command).</summary>
        public float FadeIn, FadeOut;
        /// <summary>The camera: how much a hand sways it, how soft out of focus, how far the air is clear beyond what it looks at; how hazy the valley.</summary>
        public float Hand = 1, Blur = 1, ClearAir = 34, Haze;
        /// <summary>How much the picture is brightened over the game's own exposure: the nights a little more, so they read on any screen.</summary>
        public float Exposure = 1.1f;
        /// <summary>Whether a great blast near the camera slows the game by itself, as in play (a shot that slows it on its own says no).</summary>
        public bool SlowMo = true;
        /// <summary>Called every frame after the moments, for what follows something moving.</summary>
        public Action<float> Each;

        public Shot At(float t, Action a) { Moments.Add((t, a)); return this; }
        public Shot Say(float from, float to, string big, string small = "", bool low = false, bool title = false, bool high = false) { Cards.Add(new Card(from, to, big, small, low, title, high)); return this; }
        /// <summary>Names what is on the screen, in two words or so, in the corner.</summary>
        public Shot Tag(float from, float to, string what) { Cards.Add(new Card(from, to, what, Corner: true)); return this; }
    }

    Shot _reel;
    int _frame, _preroll, _moment;
    bool _rolling, _dry;
    CanvasLayer _cut;
    ColorRect _barTop, _barBottom, _black;
    Label _big, _small;
    Control _words;
    /// <summary>--stills=folder:t1,t2,…: the frames at these seconds written there as pictures, and out.</summary>
    string _stillsTo;
    readonly List<float> _stills = new();
    /// <summary>--lens=…: the camera held at one place, to frame a shot (voxels: the eye, what it looks at, the width); --light=day,dark: the light held.</summary>
    float[] _lens, _light;

    /// <summary>Reads the trailer's arguments: --trailer=N, --stills=…, --lens=…, --dry.</summary>
    void TrailerArg(string a)
    {
        var inv = CultureInfo.InvariantCulture;
        if (a.StartsWith("--trailer=") && int.TryParse(a["--trailer=".Length..], out int n)) { _trailer = n; _demo = true; }
        else if (a == "--dry") _dry = true;
        else if (a.StartsWith("--lens=")) _lens = Array.ConvertAll(a["--lens=".Length..].Split(','), v => float.Parse(v, inv));
        else if (a.StartsWith("--light=")) _light = Array.ConvertAll(a["--light=".Length..].Split(','), v => float.Parse(v, inv));
        else if (a.StartsWith("--stills="))
        {
            var parts = a["--stills=".Length..].Split(':');
            _stillsTo = parts[0];
            if (parts.Length > 1)
                foreach (var t in parts[1].Split(','))
                    if (float.TryParse(t, NumberStyles.Float, inv, out float f)) _stills.Add(f);
        }
    }

    /// <summary>The trailer's shots, in order.</summary>
    List<Shot> Reel() => new()
    {
        TakeValley(), TakeRaise(), TakeHorde(), TakeAssault(), TakeMelee(), TakeBlast(), TakeHurl(), TakeRoll(), TakeMagic(), TakeEyes(), TakeTitle(),
    };

    /// <summary>Takes the trees off where the shot wants open ground, before the valley is packed and drawn.</summary>
    void TrailerGround()
    {
        var reel = Reel();
        if (_trailer < 0 || _trailer >= reel.Count) return;
        _reel = reel[_trailer];
        foreach (var (x0, z0, x1, z1) in _reel.Clear)
        {
            ClearTrees(x0, z0, x1, z1);
            if (!_reel.ClearRock) continue;
            // what stands on the turf of rock and moss, above the land as it was made
            for (int z = Math.Max(0, z0); z < Math.Min(World.SZ, z1); z++)
                for (int x = Math.Max(0, x0); x < Math.Min(World.SX, x1); x++)
                {
                    int top = Site.HTop[z * World.SX + x];
                    for (int y = top; y < World.SY; y++)
                        if (World.Get(x, y, z) is Mat.Rock or Mat.Moss) World.SetRaw(x, y, z, Mat.Air);
                    // the boulders' sunk feet turfed over, so no bald patch of stone is left in the grass
                    for (int y = top - 5; y < top; y++)
                        if (y > 0 && World.Get(x, y, z) is Mat.Rock or Mat.Moss) World.SetRaw(x, y, z, y == top - 1 ? Mat.Grass : Mat.Soil);
                }
        }
    }

    /// <summary>Stages the shot: its set-up, the camera, the black bars and the words; the film starts once the valley is drawn.</summary>
    void Trailer()
    {
        if (_reel == null) { GD.PrintErr($"[trailer] no shot {_trailer}"); GetTree().Quit(1); return; }
        Hud.Visible = false;
        FP.Bare = true;
        Sim.WantTrails = false;
        Settings.Feel(1, _reel.SlowMo);
        Settings.Hear(1);
        // every chip flies the same way each time the shot is filmed
        Debris.Seed(17 + _trailer);
        Sim.Waves.Countdown = 1e9;
        _reel.Stage?.Invoke();
        if (_lens is { Length: >= 6 })
        {
            var l = new Lens(V(_lens[0], _lens[1], _lens[2]), V(_lens[3], _lens[4], _lens[5]), _lens.Length > 6 ? _lens[6] : 38);
            _reel.Lens = _ => l;
            _reel.FadeIn = 0;
            _reel.Cards.Clear();
            if (_lens.Length > 7) _reel.Blur = _lens[7];
        }
        if (_light is { Length: 2 }) { float d = _light[0], k = _light[1]; _reel.Day = _ => d; _reel.Dark = _ => k; }
        Cine.Hand = _reel.Hand; Cine.Blur = _reel.Blur; Cine.Clear = _reel.ClearAir;
        Atmos.Haze = _reel.Haze;
        Atmos.Env.TonemapExposure = _reel.Exposure;
        // what the set-up threw up (dust, chips) is settled before the film starts
        Debris.Clear();
        foreach (var n in GetChildren()) if (n is FireView f) f.Settle();
        Cine.Begin();
        Light(0);
        Cine.Frame(_reel.Lens(0), 0);
        Cutting();
        GD.Print($"[trailer] shot {_trailer} «{_reel.Name}», {_reel.Length.ToString("0.0", CultureInfo.InvariantCulture)} s");
        // the great moments and the name coming up, for the score to strike with them (tools/trailer.command reads them here)
        float lastHit = -9;
        void Hit()
        {
            float t = _frame / (float)Fps;
            if (!_rolling || t - lastHit < 1.2f) return;
            lastHit = t;
            GD.Print($"[trailer] hit {t.ToString("0.00", CultureInfo.InvariantCulture)}");
        }
        Sim.Exploded += (x, y, z, r) => { if (r > 1.5f * VoxelWorld.PerMetre) Hit(); };
        Sim.Struck += (x, y, z) => Hit();
        Sim.Quaked += (x, z, r) => Hit();
        Sim.Landed += (x, y, z, n, speed) => { if (n >= 400) Hit(); };
        foreach (var c in _reel.Cards) if (c.Title) GD.Print($"[trailer] reveal {c.From.ToString("0.00", CultureInfo.InvariantCulture)}");
        if (_dry) { Dry(); return; }
        Paused = true;
    }

    /// <summary>--dry: the shot's game run through without drawing it, its moments set off on time, what happens said every second.</summary>
    void Dry()
    {
        // what comes down raises dust: where, and how much
        Sim.Collapsed += (n, x, y, z) => GD.Print($"[dry]   {_frame / (float)Fps:0.0} s: {n} voxels came down at {x:0},{y:0},{z:0}");
        for (_frame = 0; _frame <= _reel.Length * Fps; _frame++)
        {
            float t = _frame / (float)Fps;
            while (_moment < _reel.Moments.Count && _reel.Moments[_moment].at <= t) _reel.Moments[_moment++].act();
            _reel.Each?.Invoke(t);
            float pace = _reel.Pace?.Invoke(t) ?? 1;
            for (double left = pace / Fps; left > 1e-6; left -= .05) Sim.Step((float)Math.Min(.05, left));
            if (_frame % Fps == 0)
            {
                int ours = 0, horde = 0, flying = Sim.Fliers.Count;
                foreach (var u in Sim.Units) if (!u.Dead) { if (u.Team == Team.Player) ours++; else if (u.Team == Team.Horde) horde++; }
                GD.Print($"[dry] {t,5:0.0} s, game {Sim.Time,6:0} s: {Sim.Orders.Total,7} voxels marked, {ours} ours, {horde} of the horde, {flying} in the air, {Sim.Kills} killed{DryNote?.Invoke()}");
            }
        }
        // what is left marked, by the side of the citadel it is on, and what our people say they are waiting for
        var sides = new int[8];
        foreach (int col in Sim.Orders.Active)
        {
            float x = (col % Sim.Nav.NX << NavGrid.Shift) + 4 - Site.KeepX, z = (col / Sim.Nav.NX << NavGrid.Shift) + 4 - Site.KeepZ;
            sides[(int)((MathF.Atan2(z, x) + MathF.PI) / MathF.Tau * 8) % 8]++;
        }
        GD.Print($"[dry] columns marked by side (from the west, turning by the north): {string.Join(" ", sides)}");
        var why = new Dictionary<string, int>();
        foreach (var u in Sim.Units) if (!u.Dead && u.Team == Team.Player) why[$"{u.State} {u.Why}"] = why.GetValueOrDefault($"{u.State} {u.Why}") + 1;
        foreach (var (k, n) in why) GD.Print($"[dry]   {n} × {k}");
        GetTree().Quit();
    }

    /// <summary>What a shot adds to the dry run's lines.</summary>
    Func<string> DryNote;

    /// <summary>The black bars, the fade and the words, over everything.</summary>
    void Cutting()
    {
        _cut = new CanvasLayer { Layer = 60 };
        AddChild(_cut);
        _barTop = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        _barBottom = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        _words = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _black = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        _big = Hud.Text("", Ornament.Display, 54, Ornament.Parchment);
        _small = Hud.Text("", Ornament.SerifItalic, 25, Ornament.Aged);
        foreach (var l in new[] { _big, _small })
        {
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .8f));
            l.AddThemeConstantOverride("shadow_offset_x", 0);
            l.AddThemeConstantOverride("shadow_offset_y", 2);
            l.AddThemeConstantOverride("shadow_outline_size", 12);
            _words.AddChild(l);
        }
        _cut.AddChild(_barTop);
        _cut.AddChild(_barBottom);
        _cut.AddChild(_words);
        _cut.AddChild(_black);
        Bars(0, 1);
    }

    /// <summary>Lays out the bars (the frame cut to 2.39 to 1), the words and the black over it (1 all black).</summary>
    void Bars(float t, float black)
    {
        var vp = GetViewport().GetVisibleRect().Size;
        float bar = Math.Max(0, (vp.Y - vp.X / 2.39f) / 2);
        _barTop.Position = Vector2.Zero; _barTop.Size = new Vector2(vp.X, bar);
        _barBottom.Position = new Vector2(0, vp.Y - bar); _barBottom.Size = new Vector2(vp.X, bar);
        _black.Position = Vector2.Zero; _black.Size = vp;
        _black.Color = new Color(0, 0, 0, black);
        _black.Visible = black > 0;
        // the words: each fades in and out, and drifts up a little as it stands
        Card on = null;
        foreach (var c in _reel.Cards) if (t >= c.From && t < c.To) on = c;
        _words.Visible = on != null;
        if (on == null) return;
        float a = Math.Min(Math.Clamp((t - on.From) / .8f, 0, 1), Math.Clamp((on.To - t) / .6f, 0, 1)), k = (t - on.From) / Math.Max(.1f, on.To - on.From);
        _words.Modulate = new Color(1, 1, 1, CineCam.Ease(a));
        _big.Text = on.Big; _small.Text = on.Small;
        if (on.Corner)
        {
            // small capitals at the left of the black under the picture, standing still
            _big.HorizontalAlignment = HorizontalAlignment.Left;
            _big.AddThemeFontSizeOverride("font_size", 27);
            _big.AddThemeFontOverride("font", Ornament.Caps);
            _big.AddThemeColorOverride("font_color", Ornament.Parchment);
            _big.Size = new Vector2(vp.X, 0);
            _big.Position = new Vector2(vp.X * .045f, vp.Y - bar + Math.Max(0, (bar - 34) / 2));
            _small.Text = "";
            return;
        }
        _big.HorizontalAlignment = HorizontalAlignment.Center;
        _big.AddThemeFontSizeOverride("font_size", on.Title ? 104 : on.Low ? 34 : 54);
        _big.AddThemeFontOverride("font", on.Low ? Ornament.Caps : Ornament.Display);
        _big.AddThemeColorOverride("font_color", on.Title ? Ornament.BronzeLight : Ornament.Parchment);
        _small.AddThemeFontSizeOverride("font_size", on.Title ? 28 : on.Low ? 21 : 25);
        _big.Size = new Vector2(vp.X, 0); _small.Size = new Vector2(vp.X, 0);
        // low over the action, high over a sky, the title in the upper third over the fort
        float y = on.Low ? vp.Y - bar - (on.Small != "" ? 104 : 72) : on.High ? bar + 56 : on.Title ? bar + 70 : vp.Y / 2 - 46 - (on.Small != "" ? 18 : 0);
        float drift = on.Low ? 0 : k * 8;
        _big.Position = new Vector2(0, y - drift);
        _small.Position = new Vector2(0, y + (on.Title ? 126 : on.Low ? 44 : 68) - drift);
    }

    /// <summary>The light of the shot at a second of it.</summary>
    void Light(float t)
    {
        Atmos.DayOverride = _reel.Day?.Invoke(t) ?? -1;
        Atmos.DarkOverride = _reel.Dark?.Invoke(t) ?? -1;
    }

    /// <summary>One frame of the trailer: the black while the valley catches up, then the shot's moments, camera, light, pace and words.</summary>
    void Roll()
    {
        if (_reel == null || _dry) return;
        if (!_rolling)
        {
            // the black before the shot: the game stands still till every chunk is meshed and the grass grown
            _preroll++;
            AudioServer.SetBusVolumeDb(0, -80);
            Cine.Frame(_reel.Lens(0), 0);
            Bars(0, 1);
            if (_preroll >= 30 && World.DirtyCount == 0 || _preroll > 600)
            {
                _rolling = true;
                Paused = false;
                GD.Print($"[trailer] preroll {_preroll}");
            }
            return;
        }
        // the frame drawn last is the one a still was asked for
        if (_snap >= 0) { Still(_snap); _snap = -1; }
        float t = _frame / (float)Fps;
        _frame++;
        while (_moment < _reel.Moments.Count && _reel.Moments[_moment].at <= t) _reel.Moments[_moment++].act();
        _reel.Each?.Invoke(t);
        float pace = _reel.Pace?.Invoke(t) ?? 1;
        // a time-lapse runs the game faster; slow motion slows everything, the sounds deepening with it
        Pace = Math.Max(1, pace);
        Engine.TimeScale = Math.Min(1, pace);
        AudioServer.PlaybackSpeedScale = Math.Clamp(pace, .55f, 1);
        Light(t);
        if (Cine.On) Cine.Frame(_reel.Lens(t), 1f / Fps);
        float fade = Math.Max(_reel.FadeIn > 0 ? 1 - t / _reel.FadeIn : 0, _reel.FadeOut > 0 ? 1 - (_reel.Length - t) / _reel.FadeOut : 0);
        fade = Math.Clamp(fade, 0, 1);
        Bars(t, fade);
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Math.Max(1e-4f, 1 - fade)));
        foreach (float s in _stills) if (Math.Abs(t - s) < .5f / Fps) _snap = t;
        if (t >= _reel.Length || _stills.Count > 0 && t > _stills[^1] + .1f)
        {
            GD.Print($"[trailer] frames {_frame}");
            GetTree().Quit();
        }
    }

    /// <summary>The second of the shot a still is to be taken of, once its frame is drawn; below 0, none.</summary>
    float _snap = -1;

    /// <summary>The frame drawn last written out as a picture (--stills), small enough to look over.</summary>
    void Still(float t)
    {
        var img = GetViewport().GetTexture().GetImage();
        img.Resize(960, img.GetHeight() * 960 / img.GetWidth(), Image.Interpolation.Lanczos);
        DirAccess.MakeDirRecursiveAbsolute(_stillsTo);
        img.SavePng($"{_stillsTo}/s{_trailer:00}_{t.ToString("00.0", CultureInfo.InvariantCulture)}.png");
    }

    // ---------------------------------------------------------------- places

    /// <summary>World units of a point given in voxels.</summary>
    Vector3 V(float x, float y, float z) => new(World.WX(x), World.WY(y), World.WZ(z));

    /// <summary>World units of a point on the ground, so many voxels above it.</summary>
    Vector3 G(float x, float z, float up = 0) => V(x, World.SurfAt(Math.Clamp((int)x, 0, World.SX - 1), Math.Clamp((int)z, 0, World.SZ - 1)) + up, z);

    /// <summary>The middle of those of a team still standing within a reach of a point (voxels; the point itself if none are).</summary>
    Vector3 Middle(Team team, float x, float z, float reach)
    {
        float sx = 0, sy = 0, sz = 0; int n = 0;
        foreach (var u in Sim.Units)
        {
            if (u.Dead || u.Inside || u.Team != team || u.ChainedUntil > Sim.Time || (u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z) > reach * reach) continue;
            sx += u.X; sy += u.Y; sz += u.Z; n++;
        }
        return n == 0 ? G(x, z) : V(sx / n, sy / n, sz / n);
    }

    /// <summary>Round a point (world units), as <see cref="CineCam.Orbit"/>, the distance and the height given in voxels.</summary>
    static Lens Orbit(Vector3 c, float k, float a0, float a1, float r0, float r1, float h0, float h1, float fov = 40) =>
        CineCam.Orbit(c, k, a0, a1, r0 * VoxelWorld.Vox, r1 * VoxelWorld.Vox, h0 * VoxelWorld.Vox, h1 * VoxelWorld.Vox, fov);

    /// <summary>The middle of a band's members still on their feet (world units), or of the whole horde if none are.</summary>
    Vector3 BandMiddle(Warband band)
    {
        float sx = 0, sy = 0, sz = 0; int n = 0;
        if (band != null)
            foreach (var u in band.Members)
                if (!u.Dead && u.Airborne == null) { sx += u.X; sy += u.Y; sz += u.Z; n++; }
        return n == 0 ? Middle(Team.Horde, Site.KeepX - 200, Site.KeepZ, 800) : V(sx / n, sy / n, sz / n);
    }

    /// <summary>Eases a followed point toward where it is now, so the camera follows without a jolt.</summary>
    static Vector3 Toward(Vector3 from, Vector3 to, float rate) => from.Lerp(to, 1 - MathF.Exp(-rate / Fps));

    // ---------------------------------------------------------------- the fort the trailer raises

    int RingR => Site.KeepR + 44;
    int MoatR => Site.KeepR + 84;
    int _ringTop = -1;
    /// <summary>The level top of the ring wall: five metres over the ground on the west of it.</summary>
    int RingTop => _ringTop >= 0 ? _ringTop : _ringTop = Shapes.LevelTop(World, Site.KeepX - 64, Site.KeepZ, 40);
    static readonly float[] TowerAt = { MathF.PI * .25f, MathF.PI * .75f, MathF.PI * 1.25f, MathF.PI * 1.75f };

    static Stroke Wall(byte m, int width, int height, bool hollow = false) => new() { Mat = m, Width = width, Height = height, Level = true, Hollow = hollow };

    /// <summary>The stretch of wall on the east the gate goes into, drawn first with the line brush, so the workers have a way in and out once the ring closes.</summary>
    void MarkGatehouse() => Sim.DesignateBuild(Shapes.Line(World, Site, Site.KeepX + RingR, Site.KeepZ - 16, Site.KeepX + RingR, Site.KeepZ + 16, Wall(Mat.Stone, 10, 40), RingTop));

    /// <summary>
    /// The ring wall round the citadel, drawn with the circle brush: stone a metre and a quarter thick, five metres high;
    /// left unmarked where the gatehouse stands (the gateway cut through it is not to be filled again).
    /// </summary>
    void MarkRing()
    {
        var ring = Shapes.Circle(World, Site, Site.KeepX, Site.KeepZ, RingR, Wall(Mat.Stone, 10, 40, hollow: true), RingTop);
        var kept = new VoxelList();
        for (int i = 0; i < ring.Count; i++)
            if (!(ring.X(i) > Site.KeepX && Math.Abs(ring.Z(i) - Site.KeepZ) <= 13)) kept.Add(ring.X(i), ring.Y(i), ring.Z(i), ring.M(i));
        Sim.DesignateBuild(kept);
    }

    /// <summary>Four round towers on the ring, each a hollow circle of stone two metres higher than the wall round a core of rammed earth.</summary>
    void MarkTowers()
    {
        foreach (float a in TowerAt)
        {
            int tx = Site.KeepX + (int)(MathF.Cos(a) * RingR), tz = Site.KeepZ + (int)(MathF.Sin(a) * RingR);
            Sim.DesignateBuild(Shapes.Circle(World, Site, tx, tz, 13, Wall(Mat.Stone, 5, 56, hollow: true), RingTop + 16));
            Sim.DesignateBuild(Shapes.Circle(World, Site, tx, tz, 9, Wall(Mat.Rammed, 1, 48), RingTop + 8));
        }
    }

    /// <summary>Stairs of stone up to the wall walk on the north and the south of the yard, a line brush stroke for each step.</summary>
    void MarkStairs()
    {
        foreach (int side in new[] { -1, 1 })
            for (int k = 0; k < 5; k++)
            {
                int z = Site.KeepZ + side * (RingR - 6 - k * 8);
                Sim.DesignateBuild(Shapes.Line(World, Site, Site.KeepX + 8, z, Site.KeepX + 20, z, Wall(Mat.Stone, 8, 0), RingTop - k * 8));
            }
    }

    /// <summary>Merlons along the outer edge of the wall walk, a box brush stroke for each, a metre and a half apart.</summary>
    void MarkMerlons()
    {
        int r = RingR + 3, n = (int)(MathF.Tau * r / 12);
        for (int i = 0; i < n; i++)
        {
            float a = i * MathF.Tau / n;
            int x = Site.KeepX + (int)MathF.Round(MathF.Cos(a) * r), z = Site.KeepZ + (int)MathF.Round(MathF.Sin(a) * r);
            // none where a tower stands
            bool tower = false;
            foreach (float ta in TowerAt) if (Math.Abs(Mathf.Wrap(a - ta, -MathF.PI, MathF.PI)) < .2f) tower = true;
            if (!tower) Sim.DesignateBuild(Shapes.Box(World, Site, x - 2, z - 2, x + 1, z + 1, Wall(Mat.Stone, 4, 10), RingTop + 10));
        }
    }

    /// <summary>The moat round it all, dug with the circle brush and the pick, two metres and a quarter wide and three deep; the causeway to the gate on the east left unmarked.</summary>
    VoxelList MarkMoat()
    {
        var moat = Shapes.Circle(World, Site, Site.KeepX, Site.KeepZ, MoatR, new Stroke { Width = 16, Height = 20, Dig = true, Hollow = true }, 0);
        var kept = new VoxelList();
        for (int i = 0; i < moat.Count; i++)
            if (!(moat.X(i) > Site.KeepX && Math.Abs(moat.Z(i) - Site.KeepZ) < 9)) kept.Add(moat.X(i), moat.Y(i), moat.Z(i), moat.M(i));
        Sim.DesignateRemove(kept);
        return kept;
    }

    /// <summary>Whether the ring wall stands at a spot of its line (voxels) up to near its top.</summary>
    bool RingStands(float a)
    {
        int x = Site.KeepX + (int)(MathF.Cos(a) * RingR), z = Site.KeepZ + (int)(MathF.Sin(a) * RingR);
        return Mat.IsMasonry(World.Get(x, RingTop - 3, z));
    }

    /// <summary>A face of the ring wall seen from a direction: the first voxel of it met coming in from outside (or out from the yard), and its face's normal.</summary>
    VoxelHit RingFace(float a, bool inner, int up)
    {
        var w = World;
        float cx = MathF.Cos(a), cz = MathF.Sin(a);
        // the wall faces along whichever of x and z is nearer the bearing
        bool alongX = MathF.Abs(cx) >= MathF.Abs(cz);
        int dirX = alongX ? Math.Sign(cx) : 0, dirZ = alongX ? 0 : Math.Sign(cz);
        int sx = Site.KeepX + (int)(cx * RingR), sz = Site.KeepZ + (int)(cz * RingR);
        int y = w.SurfAt(sx - dirX * 12, sz - dirZ * 12) + up;
        int step = inner ? 1 : -1;
        // from the yard outward (inner) or from outside inward (outer), to the first stone
        int x = sx - step * dirX * 14, z = sz - step * dirZ * 14;
        for (int i = 0; i < 28; i++, x += step * dirX, z += step * dirZ)
            if (Mat.IsMasonry(w.Get(x, y, z))) return new VoxelHit { X = x, Y = y, Z = z, NX = -step * dirX, NZ = -step * dirZ, M = w.Get(x, y, z) };
        return default;
    }

    /// <summary>The gate on the east, cut into the wall that stands there, as the gate tool does; at once, or for the workers.</summary>
    bool MarkGate(bool built)
    {
        var hit = RingFace(0, inner: false, up: 6);
        if (hit.M == 0) return false;
        var plan = GatePlan.At(World, Site, hit);
        if (plan == null) return false;
        Sim.AddGate(plan, built);
        return true;
    }

    /// <summary>Ladders up the inner face of the wall on the west, north-west and south-west, as the ladder tool sets them.</summary>
    void MarkLadders()
    {
        foreach (float a in new[] { MathF.PI, MathF.PI * .89f, MathF.PI * 1.11f })
        {
            var hit = RingFace(a, inner: true, up: 4);
            if (hit.M == 0) continue;
            var plan = Shapes.LadderAt(World, Site, hit);
            if (plan.Count > 0) Sim.DesignateBuild(plan);
        }
    }

    /// <summary>The cut from the south of the moat into the pond below it: the water comes in.</summary>
    void MarkChannel()
    {
        var w = World; var s = Site;
        int mx = s.KeepX, mz = s.KeepZ + MoatR;
        var pond = s.Ponds[0];
        foreach (var p in s.Ponds) if (Brushes.Dist(p.X - mx, p.Z - mz) < Brushes.Dist(pond.X - mx, pond.Z - mz)) pond = p;
        double pd = Brushes.Dist(mx - pond.X, mz - pond.Z);
        int reach = MapGen.Shore(w, s, pond.X, pond.Z, mx - pond.X, mz - pond.Z) - 6;
        int cutX = pond.X + (int)((mx - pond.X) / pd * reach), cutZ = pond.Z + (int)((mz - pond.Z) / pd * reach);
        Sim.DesignateRemove(Shapes.Line(w, s, mx, mz, cutX, cutZ, new Stroke { Width = 8, Height = 20, Dig = true }, 0));
    }

    /// <summary>The whole fort at once, as it stands when the work is done (the shots after the time-lapse), its moat full; torches up on it.</summary>
    void FortStanding()
    {
        Sim.InstantLabour = true;
        MarkGatehouse(); MarkRing(); MarkTowers(); MarkStairs(); MarkMerlons();
        var moat = MarkMoat();
        Sim.InstantLabour = false;
        MarkGate(built: true);
        Sim.InstantLabour = true;
        MarkLadders();
        Sim.InstantLabour = false;
        // the water let in, a hand below the brim
        int level = World.SurfAt(Site.KeepX - MoatR - 20, Site.KeepZ) - 4;
        for (int i = 0; i < moat.Count; i++)
            if (moat.Y(i) < level && World.Get(moat.X(i), moat.Y(i), moat.Z(i)) == Mat.Air) World.Set(moat.X(i), moat.Y(i), moat.Z(i), Mat.Water);
        Sim.Decorate();
    }

    /// <summary>Our people on the fort's walls: archers along the west, swordsmen behind them, each held at his post.</summary>
    void ManTheWalls(int archers, int swords)
    {
        for (int i = 0; i < archers; i++)
        {
            float a = MathF.PI + (i - (archers - 1) / 2f) * .16f;
            int c = Sim.NearestStand(Site.KeepX + (int)(MathF.Cos(a) * (RingR - 1)), Site.KeepZ + (int)(MathF.Sin(a) * (RingR - 1)), 4);
            if (c < 0) continue;
            var u = Sim.Spawn(Kinds.Archer, c);
            Sim.Command(new[] { u }, c);
        }
        for (int i = 0; i < swords; i++)
        {
            float a = MathF.PI + (i - (swords - 1) / 2f) * .2f;
            int c = Sim.NearestStand(Site.KeepX + (int)(MathF.Cos(a) * (RingR - 3)), Site.KeepZ + (int)(MathF.Sin(a) * (RingR - 3)), 4);
            if (c < 0) continue;
            var u = Sim.Spawn(Kinds.Swordsman, c);
            Sim.Command(new[] { u }, c);
        }
    }

    // ---------------------------------------------------------------- the shots

    /// <summary>1. The valley at dusk, the citadel alone on its plateau over the river: no wall about it yet.</summary>
    Shot TakeValley()
    {
        var s = Site;
        // (set out round the citadel where it stood at 819,512 on the old valley: they go with it)
        int ox = s.KeepX - 819, oz = s.KeepZ - 512;
        var shot = new Shot
        {
            Name = "valley",
            Length = 8,
            // the trees that would stand between the lens and the citadel at the end of its rise
            Clear = new[] { (560 + ox, 500 + oz, 790 + ox, 600 + oz) },
            FadeIn = 1.6f,
            Day = _ => .93f, Dark = _ => .04f,
            Haze = .6f, ClearAir = 60, Hand = .7f,
            Stage = () =>
            {
                // a few of ours about the citadel, deer at the edge of the wood
                foreach (var (t, dx, dz) in new[] { (Kinds.Worker, -40, 10), (Kinds.Worker, -46, -6), (Kinds.Archer, -30, 30), (Kinds.Worker, -20, -36) })
                {
                    var u = Sim.Spawn(t, Sim.NearestStand(s.KeepX + dx, s.KeepZ + dz, 4));
                    Sim.Command(new[] { u }, u.Cell);
                }
                Advance(4);
            },
            Lens = t =>
            {
                // low over the river under the cliff, rising and drawing back till the citadel stands over the valley
                float k = CineCam.Ease(t / 8);
                return CineCam.Dolly(k, V(400 + ox, 66, 760 + oz), V(330 + ox, 168, 650 + oz), V(610 + ox, 72, 610 + oz), V(s.KeepX, 100, s.KeepZ), 44, 36);
            },
        };
        shot.Tag(2, 7.4f, "Процедурная долина");
        return shot;
    }

    /// <summary>
    /// 2. The time-lapse: the fort raised from nothing round the citadel, ten times as fast as played, the sun going over.
    /// The player's strokes come one after another as a player would make them: the ring wall with the circle brush,
    /// four towers, the stairs, the merlons, the moat with the pick; the gate and the ladders once the wall stands for
    /// them; at last the cut into the pond that lets the water in. Thirty workers lay and dig it all with their own hands.
    /// </summary>
    Shot TakeRaise()
    {
        var s = Site;
        bool gate = false, ladders = false, channel = false;
        var shot = new Shot
        {
            Name = "raise",
            Length = 22,
            Day = t => Mathf.Lerp(.05f, .9f, t / 22),
            Dark = _ => 0,
            Pace = _ => 10,
            Haze = .45f, ClearAir = 40, Hand = .5f,
            Stage = () =>
            {
                var sim = Sim;
                sim.Stock.Gold = 400;
                sim.Stock[Stockpile.Stone] = 900 * VoxelWorld.PerM3;
                sim.Stock[Stockpile.Wood] = 120 * VoxelWorld.PerM3;
                sim.Stock[Stockpile.Earth] = 60 * VoxelWorld.PerM3;
                for (int i = 0; i < 44; i++) sim.Spawn(Kinds.Worker, sim.Gate);
                Advance(1);
                DryNote = () =>
                {
                    // how much of the ring stands, how much of the moat is dug to its floor, and whether water is in it
                    int up = 0, dug = 0, wet = 0;
                    string down = "";
                    for (int i = 0; i < 36; i++)
                    {
                        float a = i * MathF.Tau / 36;
                        if (RingStands(a)) up++;
                        else down += $" {i * 10}°";
                        int mx = s.KeepX + (int)(MathF.Cos(a) * MoatR), mz = s.KeepZ + (int)(MathF.Sin(a) * MoatR);
                        int floor = World.SurfAt(mx, mz);
                        if (World.Get(mx, floor - 1, mz) == Mat.Water || World.Get(mx, floor, mz) == Mat.Water) wet++;
                        if (floor < World.SurfAt(mx + (int)(MathF.Cos(a) * 20), mz + (int)(MathF.Sin(a) * 20)) - 16) dug++;
                    }
                    return $", ring {up}/36 (not at{down}), moat dug {dug}/36, wet {wet}/36, gate {(gate ? "marked" : "-")}, ladders {(ladders ? "marked" : "-")}, channel {(channel ? "marked" : "-")}";
                };
            },
            Each = t =>
            {
                // the gate cut through the gatehouse once it stands; the ladders once the west stands
                if (!gate && RingStands(0) && RingStands(-.12f) && RingStands(.12f))
                {
                    gate = MarkGate(built: false);
                    GD.Print($"[trailer] {t:0.0} s: the gate {(gate ? "marked" : "would not go in")}");
                }
                if (!ladders && t > 4 && RingStands(MathF.PI) && RingStands(MathF.PI * .89f) && RingStands(MathF.PI * 1.11f)) { MarkLadders(); ladders = true; }
            },
            Lens = t =>
            {
                // round the east of it, the valley behind it: from the morning side to the evening side
                var c = V(s.KeepX - 10, s.KeepBase + 14, s.KeepZ);
                return Orbit(c, CineCam.Ease(t / 22), 1.1f, 2.3f, 300, 270, 190, 150, 40);
            },
        };
        shot.At(.3f, () => MarkGatehouse())
            .At(1.0f, () => MarkRing())
            .At(1.7f, () => MarkTowers())
            .At(2.4f, () => MarkStairs())
            .At(3.1f, () => MarkMoat())
            .At(3.8f, () => MarkMerlons())
            .At(7f, () => { MarkChannel(); channel = true; });
        shot.Tag(.8f, 6.6f, "Разметка кистью")
            .Tag(7.6f, 20.8f, "Стройка пешками");
        return shot;
    }

    /// <summary>3. The horde comes with the night: its bands across the river and up to the fort, ten times as fast.</summary>
    Shot TakeHorde()
    {
        var s = Site;
        var follow = Vector3.Zero;
        var way = new Vector3(1, 0, 0);
        Warband tracked = null;
        var shot = new Shot
        {
            Name = "horde",
            Length = 9,
            Day = _ => .99f, Dark = t => Mathf.Lerp(.12f, .5f, Math.Clamp(t / 8, 0, 1)),
            Pace = t => t < 6.5f ? 7 : Mathf.Lerp(7, 1.5f, (t - 6.5f) / 2.5f),
            Haze = .4f, ClearAir = 50, Hand = .9f, Exposure = 1.45f, Blur = .5f,
            Stage = () =>
            {
                var sim = Sim;
                FortStanding();
                ManTheWalls(5, 2);
                Nightfall();
                sim.HordeStock[Stockpile.Wood] = 40 * VoxelWorld.PerM3;
                // four bands out of the west, each behind its chief, torches among them, coming on side by side (set out
                // from the western side and the citadel's row as they were on the old valley)
                var kinds = new[] { Kinds.Goblin, Kinds.Goblin, Kinds.Orc, Kinds.Goblin, Kinds.Sapper, Kinds.Orc, Kinds.Goblin, Kinds.Goblin };
                int id = 90, wx = s.Hex != null ? (int)s.Hex.WestX - 70 : 0, oz = s.KeepZ - 512;
                foreach (var (bx0, bz0, chief) in new[] { (150, 400, Kinds.Troll), (110, 500, Kinds.Orc), (170, 590, Kinds.Troll), (90, 680, Kinds.Orc) })
                {
                    int bx = bx0 + wx, bz = bz0 + oz;
                    var band = new Warband { Id = id++, Name = "Ватага", State = BandState.March };
                    var lead = sim.Spawn(chief, sim.NearestStand(bx + 14, bz, chief.Move.Height));
                    band.Members.Add(lead); band.Leader = lead; lead.Band = band;
                    for (int i = 0; i < kinds.Length; i++)
                    {
                        var t = kinds[i];
                        int c = sim.NearestStand(bx - 6 - i / 3 * 8, bz - 10 + i % 3 * 10, t.Move.Height);
                        if (c < 0) continue;
                        var u = sim.Spawn(t, c);
                        u.Band = band; band.Members.Add(u);
                        u.Torch = i % 3 == 0;
                    }
                    sim.Bands.Add(band);
                    if (chief == Kinds.Troll && tracked == null) tracked = band;
                }
                sim.ComputeFields();
                Advance(2);
                follow = BandMiddle(tracked);
            },
            Each = t =>
            {
                // after the troll's band, the way it goes eased, so the camera swings round with it and never jerks
                var at = BandMiddle(tracked);
                var to = new Vector3(World.WX(s.KeepX), 0, World.WZ(s.KeepZ)) - new Vector3(at.X, 0, at.Z);
                if (to.Length() > .5f) way = way.Lerp(to.Normalized(), 1 - MathF.Exp(-1.2f / Fps)).Normalized();
                follow = Toward(follow, at, 2.5f);
            },
            Lens = t =>
            {
                // a drone over them, above the treetops: behind and a little to the side, looking down past them at the
                // way ahead, the fort coming up out of the dusk at its end
                var side = new Vector3(-way.Z, 0, way.X);
                var eye = follow - way * (150 * VoxelWorld.Vox) + side * (60 * VoxelWorld.Vox);
                float ground = World.WY(World.SurfAt(Math.Clamp(World.VX(eye.X), 0, World.SX - 1), Math.Clamp(World.VZ(eye.Z), 0, World.SZ - 1)));
                eye.Y = Math.Max(follow.Y + 140 * VoxelWorld.Vox, ground + 110 * VoxelWorld.Vox);
                return new Lens(eye, follow + way * (60 * VoxelWorld.Vox), 42);
            },
        };
        shot.Tag(.8f, 8.2f, "Ватаги орды");
        return shot;
    }

    /// <summary>4. The assault at night, as played: bridges over the moat, ladders up the wall, our archers shooting from it.</summary>
    Shot TakeAssault()
    {
        var s = Site;
        var shot = new Shot
        {
            Name = "assault",
            Length = 12,
            Day = _ => 1, Dark = _ => 1,
            Haze = .3f, ClearAir = 30,
            Exposure = 1.3f,
            Stage = () =>
            {
                var sim = Sim;
                FortStanding();
                Nightfall();
                // timber for bridges and ladders, and no earth: earth thrown into the water would only sink in a cloud of dust
                sim.HordeStock[Stockpile.Wood] = 60 * VoxelWorld.PerM3;
                sim.HordeStock[Stockpile.Earth] = 0;
                int kx = s.KeepX, kz = s.KeepZ;
                foreach (var (t, dx, dz) in new[] { (Kinds.Sapper, -140, -26), (Kinds.Sapper, -138, 4), (Kinds.Sapper, -142, 30), (Kinds.Sapper, -146, -8),
                    (Kinds.Goblin, -150, -18), (Kinds.Goblin, -152, 14), (Kinds.Orc, -156, -4), (Kinds.Orc, -158, 22), (Kinds.Troll, -168, 0),
                    (Kinds.Goblin, -160, -34), (Kinds.Goblin, -164, 38), (Kinds.Orc, -170, -20), (Kinds.Goblin, -174, 30), (Kinds.Sapper, -150, 42),
                    (Kinds.Orc, -178, 10), (Kinds.Goblin, -182, -30), (Kinds.Troll, -186, 22), (Kinds.Goblin, -190, -6), (Kinds.Orc, -194, -40),
                    (Kinds.Goblin, -198, 16), (Kinds.Goblin, -202, -18), (Kinds.Orc, -206, 34), (Kinds.Goblin, -210, -26) })
                {
                    int c = sim.NearestStand(kx + dx, kz + dz, t.Move.Height);
                    if (c >= 0) sim.Spawn(t, c).Torch = (dx + dz) % 3 == 0;
                }
                sim.ComputeFields();
                // the horde gets there before us: bridges over the moat, ladders up the wall; then our archers come up
                // onto it (archers only: a swordsman would throw the ladders down at once)
                Advance(16);
                ManTheWalls(8, 0);
                Advance(.5);
                DryNote = () =>
                {
                    int deck = 0, ladder = 0;
                    for (int z = kz - 80; z < kz + 80; z++)
                        for (int x = kx - MoatR - 30; x < kx - RingR + 10; x++)
                            for (int y = 60; y < RingTop + 4; y++)
                            {
                                byte m = World.Get(x, y, z);
                                if (m == Mat.Deck) deck++; else if (m == Mat.SiegeLadder) ladder++;
                            }
                    return $", {deck} voxels of deck, {ladder} of ladders";
                };
            },
            Lens = t =>
            {
                int kx = s.KeepX, kz = s.KeepZ;
                if (t < 6.5f)
                {
                    // from the field south of them, up at the wall and the ladders, drifting along it
                    float k = CineCam.Ease(t / 6.5f);
                    return CineCam.Dolly(k, G(kx - MoatR - 50, kz + 82, 26), G(kx - MoatR - 40, kz + 58, 28), V(kx - RingR - 6, RingTop - 12, kz + 6), V(kx - RingR - 6, RingTop - 10, kz - 10), 40, 38);
                }
                // high over the moat, craning down on the wall: the ladders up its face full of climbers, our archers on
                // its walk loosing down on them, the bridges below
                float k2 = CineCam.Ease((t - 6.5f) / 5.5f);
                return CineCam.Dolly(k2, V(kx - MoatR - 20, RingTop + 56, kz - 70), V(kx - MoatR - 6, RingTop + 44, kz - 52), V(kx - RingR - 6, RingTop - 8, kz - 4), V(kx - RingR - 6, RingTop - 10, kz + 4), 42, 40);
            },
        };
        shot.Tag(.8f, 11.2f, "Штурм стен");
        return shot;
    }

    /// <summary>
    /// 5. Hand to hand on the meadow in the evening, slowed: a line of ours — swordsmen, a knight, a halberdier, a
    /// spearman — stands against orcs and goblins a stride off, each pair at it; every blow comes its own way and cuts
    /// where it lands. The camera is low beside the line at a man's shoulder, the knight's pair nearest, the rest going
    /// away behind it out of focus.
    /// </summary>
    Shot TakeMelee()
    {
        var (cx, cz) = MeadowAt();
        Unit knight = null, foe = null;
        var look = Vector3.Zero;
        Vector3 Between(Unit a, Unit b) => V((a.X + b.X) / 2, Math.Max(a.Y, b.Y) + 10, (a.Z + b.Z) / 2);
        var shot = new Shot
        {
            Name = "melee",
            Length = 10,
            // the bushes and trees off the meadow round the line, so nothing stands before the lens
            Clear = new[] { (cx - 70, cz - 70, cx + 70, cz + 110) }, ClearRock = true,
            Day = _ => .86f, Dark = _ => 0,
            ClearAir = 16, Blur = 1.4f, Hand = .8f, Haze = .25f,
            SlowMo = false,
            Pace = t => t < .5f ? 1 : Mathf.Lerp(1, .3f, Math.Min(1, (t - .5f) / .4f)),
            Stage = () =>
            {
                var sim = Sim;
                var ours = new[] { Kinds.Spearman, Kinds.Swordsman, Kinds.Halberdier, Kinds.Swordsman, Kinds.Knight };
                var theirs = new[] { Kinds.Goblin, Kinds.Orc, Kinds.Orc, Kinds.Goblin, Kinds.Orc };
                for (int i = 0; i < ours.Length; i++)
                {
                    // the pairs along the meadow north to south, ours on the east facing west, each a stride from its foe
                    float z = cz + (i - 2) * 18, d = ours[i].Reach + theirs[i].BodyR - 2;
                    Unit Put(UnitType t, float x, float face)
                    {
                        var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height));
                        u.X = x; u.Z = z; u.Y = World.SurfAt((int)x, (int)z); u.Facing = face;
                        if (t.Team == Team.Horde) { u.Guard = true; u.Home = u.Cell; } else { u.Hold = true; u.Post = u.Cell; u.Dest = u.Cell; }
                        return u;
                    }
                    var a = Put(ours[i], cx + d / 2, MathF.PI);
                    var b = Put(theirs[i], cx - d / 2, 0);
                    a.Target = b; b.Target = a;
                    if (i == ours.Length - 1) { knight = a; foe = b; }
                }
                Advance(.2);
                look = Between(knight, foe);
            },
            Each = t => look = Toward(look, Between(knight, foe), 2),
            Lens = t =>
            {
                // from the south of the line at a man's shoulder, drifting across it and in
                float k = CineCam.Ease(t / 10);
                var eye = look + new Vector3(Mathf.Lerp(-14, 10, k), Mathf.Lerp(2, 0, k), Mathf.Lerp(46, 36, k)) * VoxelWorld.Vox;
                return new Lens(eye, look, Mathf.Lerp(38, 35, k));
            },
        };
        shot.Tag(.5f, 4.6f, "Ближний бой").Tag(5.2f, 9.6f, "Воксельные раны");
        return shot;
    }

    /// <summary>6. Kegs of powder at the foot of a wall of ours go up under a band, one after another: the wall broken through, its top coming down.</summary>
    Shot TakeBlast()
    {
        var s = Site;
        int wx = s.KeepX - s.KeepR - 100, kz = s.KeepZ, top = 0;
        (int x, int y, int z) fuse = default;
        var shot = new Shot
        {
            Name = "blast",
            Length = 8,
            Clear = new[] { (s.KeepX - s.KeepR - 300, s.KeepZ - 140, s.KeepX - s.KeepR - 30, s.KeepZ + 140) },
            Day = _ => .93f, Dark = _ => .25f,
            SlowMo = false,
            Pace = t => t < 2.0f ? 1 : t < 2.4f ? Mathf.Lerp(1, .22f, (t - 2) / .4f) : t < 4.6f ? .22f : Math.Min(1, Mathf.Lerp(.22f, 1, (t - 4.6f) / 1.2f)),
            Haze = .3f, ClearAir = 30,
            Stage = () =>
            {
                Sorcerous();
                top = WallWest(-100, 40, 64);
                fuse = KegsBefore(wx, kz, 9, 4);
                Advance(.3);
            },
            Lens = t =>
            {
                float k = CineCam.Ease(t / 8);
                return CineCam.Dolly(k, G(wx - 120, kz + 110, 26), G(wx - 100, kz + 90, 22), V(wx - 6, top - 22, kz - 6), V(wx - 6, top - 22, kz - 4), 40, 36);
            },
        };
        shot.At(.9f, () => Sim.Ignite(fuse.x, fuse.y, fuse.z));
        shot.Tag(.6f, 3.6f, "Пороховые бочки").Tag(4.2f, 7.4f, "Обрушение стены");
        return shot;
    }

    /// <summary>7. The hand of telekinesis tears a ball of earth out of the field, swings it up and hurls it down on the horde's deck over a ditch, the band on it.</summary>
    Shot TakeHurl()
    {
        var s = Site;
        int sx = 0, sz = 0, g = 0;
        Flier ball = null;
        var look = Vector3.Zero;
        var shot = new Shot
        {
            Name = "hurl",
            Length = 8,
            Clear = new[] { (s.KeepX - s.KeepR - 300, s.KeepZ - 140, s.KeepX - s.KeepR - 30, s.KeepZ + 140) },
            Day = _ => .84f, Dark = _ => 0,
            SlowMo = false,
            Pace = t => t < 4.3f ? 1 : t < 6.9f ? .35f : 1,
            Haze = .3f, ClearAir = 30,
            Stage = () =>
            {
                (sx, sz, g) = DeckStage();
                look = G(sx - 30, sz + 30, 4);
                DryNote = () => ball != null && Sim.Fliers.Contains(ball) ? $", the ball at {ball.X:0},{ball.Y:0},{ball.Z:0}" : "";
            },
            Each = t =>
            {
                // between the ball and the deck under it, so both are in the frame as it is swung over and brought down
                var deck = G(sx + 14, sz, 4);
                var at = ball != null && Sim.Fliers.Contains(ball) ? V(ball.X, ball.Y, ball.Z).Lerp(deck, t < 2 ? .2f : .5f) : deck;
                look = Toward(look, at, 3);
                // the hand: up out of the ground, swung over the deck and stopped above it, then down on them hard
                if (t >= .8f && t < 2.3f) Sim.HoldAt(sx - 30, g + 8 + 48 * CineCam.Ease((t - .8f) / 1.5f), sz + 30);
                else if (t >= 2.3f && t < 3.5f)
                {
                    float k = CineCam.Ease((t - 2.3f) / 1.2f);
                    Sim.HoldAt(Mathf.Lerp(sx - 30, sx + 14, k), g + 56 + 12 * MathF.Sin(k * MathF.PI), Mathf.Lerp(sz + 30, sz, k));
                }
                else if (t >= 3.5f && t < 3.85f) Sim.HoldAt(sx + 14, g + 56, sz);
                else if (t >= 3.85f && t < 4.0f) Sim.HoldAt(sx + 14, Mathf.Lerp(g + 56, g + 14, (t - 3.85f) / .15f), sz);
            },
            Lens = t =>
            {
                var eye = G(sx - 70, sz + 104, 34).Lerp(G(sx - 40, sz + 84, 24), CineCam.Ease(t / 8));
                return new Lens(eye, look, 48);
            },
        };
        shot.At(.8f, () => { Sim.Cast(SpellKind.Telekinesis, sx - 30.5f, g - .5f, sz + 30.5f); ball = Sim.HeldNow; Sim.HoldAt(sx - 30, g + 8, sz + 30); })
            .At(4.0f, () => Sim.LetGo());
        shot.Tag(.6f, 7.4f, "Телекинез земли");
        return shot;
    }

    /// <summary>8. A ball of earth torn out of a hilltop and let go over its slope: it rolls down through the band in the hollow below.</summary>
    Shot TakeRoll()
    {
        var s = Site;
        int sx = 0, sz = 0, g = 0;
        Flier ball = null;
        var look = Vector3.Zero;
        var shot = new Shot
        {
            Name = "roll",
            Length = 5.2f,
            Clear = new[] { (s.KeepX - s.KeepR - 300, s.KeepZ - 140, s.KeepX - s.KeepR - 30, s.KeepZ + 140) },
            Day = _ => .8f, Dark = _ => 0,
            Haze = .3f, ClearAir = 30,
            Stage = () =>
            {
                (sx, sz, g) = HillStage();
                DryNote = () => ball != null && Sim.Fliers.Contains(ball) ? $", the ball at {ball.X:0},{ball.Y:0},{ball.Z:0}" : "";
                Sim.Cast(SpellKind.Telekinesis, sx + 10.5f, g + 23.5f, sz + .5f);
                ball = Sim.HeldNow;
                Sim.HoldAt(sx + 28, g + 44, sz + .5f);
                Advance(1.6);
                look = ball != null ? V(ball.X, ball.Y, ball.Z) : G(sx + 30, sz);
            },
            Each = t =>
            {
                var at = ball != null && Sim.Fliers.Contains(ball) ? V(ball.X, ball.Y, ball.Z) : G(sx + 80, sz);
                look = Toward(look, at, 3.5f);
            },
            Lens = t =>
            {
                var eye = new Vector3(look.X - .8f, G(sx + 40, sz, 0).Y + 1.6f, look.Z + 6.2f);
                return new Lens(eye, look, 40);
            },
        };
        shot.At(.7f, () => Sim.LetGo());
        shot.Tag(.5f, 4.8f, "Физика обломков");
        return shot;
    }

    /// <summary>
    /// 9. The great spells, at night: the blood moon reddens it, burning pitch rains on a band, a bolt of thunder strikes a
    /// troll, the earth shakes under another band, a star falls on a third, a whirlwind goes through a fourth; each named
    /// low in the frame.
    /// </summary>
    Shot TakeMagic()
    {
        var s = Site;
        int bx = s.KeepX - s.KeepR - 95, bz = s.KeepZ;
        Unit troll = null;
        var parts = new (float from, string name)[] { (0, "Магия жрецов"), (3.2f, "Огонь и сера"), (6.6f, "Перст грома"), (9.4f, "Землетрясение"), (12.6f, "Звезда Полынь"), (15.8f, "Смерч") };
        // where each spell falls (voxels)
        (int x, int z) fire = (bx + 30, bz - 72), bolt = (bx + 50, bz + 78), quake = (bx - 20, bz + 4), star = (bx - 60, bz - 64), whirl = (bx - 50, bz + 88);
        var shot = new Shot
        {
            Name = "magic",
            Length = 19.5f,
            Clear = new[] { (s.KeepX - s.KeepR - 320, s.KeepZ - 180, s.KeepX - s.KeepR - 20, s.KeepZ + 180) },
            // deep dusk: dark enough for the fire and the bolt to light it, light enough to see who they fall on
            Day = _ => .97f, Dark = _ => .62f,
            Haze = .3f, ClearAir = 34, Exposure = 1.3f,
            Stage = () =>
            {
                Sorcerous();
                Nightfall();
                Sim.Favour = 2000;
                // ours off the field: the bands stand where they are, rather than make for the household at the gate
                foreach (var u in Sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
                foreach (var (x, z) in new[] { fire, quake, star })
                    foreach (var (t, dx, dz) in new[] { (Kinds.Orc, -14, 0), (Kinds.Goblin, -8, 12), (Kinds.Goblin, -4, -10), (Kinds.Goblin, 10, 14), (Kinds.Orc, 14, -6), (Kinds.Goblin, 20, 6), (Kinds.Sapper, -18, 16) })
                        Camp(t, x + dx, z + dz);
                WonderBand(whirl.x, whirl.z);
                troll = Camp(Kinds.Troll, bolt.x, bolt.z);
                Camp(Kinds.Goblin, bolt.x - 14, bolt.z + 10); Camp(Kinds.Goblin, bolt.x - 8, bolt.z - 12);
                Advance(.5);
            },
            Lens = t =>
            {
                var w = World;
                if (t < 3.2f)
                {
                    // a band in the field by night, the citadel beyond it, and the moon's red light coming over them
                    float k = CineCam.Ease(t / 3.2f);
                    return CineCam.Dolly(k, G(fire.x - 84, fire.z + 52, 22), G(fire.x - 66, fire.z + 40, 18), G(fire.x + 8, fire.z, 6), G(fire.x + 4, fire.z, 6), 40, 38);
                }
                if (t < 6.6f) return Orbit(G(fire.x, fire.z, 4), CineCam.Ease((t - 3.2f) / 3.4f), -2.15f, -1.85f, 76, 66, 34, 28, 40);
                // the troll from the side and a little below, the sky over him for the bolt to come down out of
                if (t < 9.4f) return CineCam.Dolly(CineCam.Ease((t - 6.6f) / 2.8f), G(bolt.x - 30, bolt.z + 74, 14), G(bolt.x - 22, bolt.z + 64, 12), G(bolt.x, bolt.z, 26), G(bolt.x, bolt.z, 22), 46, 44);
                if (t < 12.6f) return Orbit(G(quake.x, quake.z, 6), CineCam.Ease((t - 9.4f) / 3.2f), 2.3f, 2.0f, 110, 100, 34, 30, 40);
                if (t < 15.8f) return CineCam.Dolly(CineCam.Ease((t - 12.6f) / 3.2f), G(star.x + 110, star.z + 90, 60), G(star.x + 96, star.z + 80, 50), G(star.x, star.z, 6), G(star.x, star.z, 4), 42, 40);
                return Orbit(G(whirl.x, whirl.z, 10), CineCam.Ease((t - 15.8f) / 3.7f), -2.6f, -2.2f, 110, 100, 36, 32, 42);
            },
        };
        shot.At(3.6f, () => Sim.Cast(SpellKind.FireRain, fire.x, World.SurfAt(fire.x, fire.z), fire.z))
            .At(7.4f, () => { if (troll != null && !troll.Dead) Sim.Cast(SpellKind.Thunder, troll.X, troll.Y, troll.Z); })
            .At(9.9f, () => Sim.Cast(SpellKind.Quake, quake.x, World.SurfAt(quake.x, quake.z), quake.z))
            .At(13.0f, () => Sim.Cast(SpellKind.Meteor, star.x, World.SurfAt(star.x, star.z), star.z))
            .At(16.0f, () => Sim.Cast(SpellKind.Whirlwind, whirl.x - 50, World.SurfAt(whirl.x - 50, whirl.z), whirl.z));
        for (int i = 0; i < parts.Length; i++)
            shot.Tag(parts[i].from + .3f, (i + 1 < parts.Length ? parts[i + 1].from : 19.5f) - .2f, parts[i].name);
        return shot;
    }

    /// <summary>
    /// 10. First person: the camera comes down behind a swordsman standing in the tall grass of the meadow and goes into
    /// him; he wades at the band coming through the grass, striking; then an archer of ours draws on a goblin and looses.
    /// </summary>
    Shot TakeEyes()
    {
        int cx = 0, cz = 0;
        Unit me = null, bow = null;
        var shot = new Shot
        {
            Name = "eyes",
            Length = 11.6f,
            Day = _ => .82f, Dark = _ => 0,
            ClearAir = 26, Blur = 1.4f,
            // the arrows slowed in flight, to be seen going
            Pace = t => t is > 9.3f and < 10.1f || t is > 10.5f and < 11.3f ? .4f : 1,
            Stage = () =>
            {
                var sim = Sim;
                (cx, cz) = MeadowAt();
                me = sim.Spawn(Kinds.Swordsman, sim.NearestStand(cx + 34, cz, 2));
                me.Facing = MathF.PI;
                foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, -4, 4), (Kinds.Orc, -22, -14), (Kinds.Goblin, -34, 16) })
                {
                    var u = sim.Spawn(t, sim.NearestStand(cx + dx, cz + dz, t.Move.Height));
                    u.Guard = true; u.Home = u.Cell;
                }
                // the archer a little way off, where nothing stands between him and the band: no boulder, no rise; the
                // bushes about him cleared, so they do not fill his eyes
                int ax = cx + 40, az = cz + 22;
                foreach (float a in new[] { .5f, .2f, .8f, -.1f, 1.1f, -.4f, 1.4f })
                {
                    int x = cx + (int)(MathF.Cos(a) * 44), z = cz + (int)(MathF.Sin(a) * 44), g0 = World.SurfAt(x, z);
                    bool open = true;
                    for (int i = 4; i < 40 && open; i += 2)
                    {
                        int sx = x + (cx - x) * i / 44, sz = z + (cz - z) * i / 44;
                        if (World.SurfAt(sx, sz) > g0 + 3 || Mat.IsLeafy(World.Get(sx, World.SurfAt(sx, sz) - 1, sz))) open = false;
                    }
                    if (open) { ax = x; az = z; break; }
                }
                int ag = World.SurfAt(ax, az);
                for (int z = az - 20; z <= az + 20; z++)
                    for (int x = ax - 20; x <= ax + 20; x++)
                        for (int y = ag - 4; y < ag + 16; y++)
                            if (World.InBounds(x, y, z) && Mat.IsLeafy(World.Get(x, y, z)) && World.Get(x, y - 1, z) is var under && !Mat.IsTrunk(under)) World.Set(x, y, z, Mat.Air);
                bow = sim.Spawn(Kinds.Archer, sim.NearestStand(ax, az, 2));
                bow.FireArrows = true;
                sim.Command(new[] { bow }, bow.Cell);
                Advance(.4);
            },
            Each = t =>
            {
                var sim = Sim;
                if (FP.On && FP.Unit == me)
                {
                    // the nearest of them standing: the head turns to it, he walks at it, and swings when he is on it
                    Unit foe = null; float best = 1e9f;
                    foreach (var u in sim.Units)
                        if (!u.Dead && u.Team == Team.Horde && u.Airborne == null)
                        {
                            float d = (u.X - me.X) * (u.X - me.X) + (u.Z - me.Z) * (u.Z - me.Z);
                            if (d < best) { best = d; foe = u; }
                        }
                    if (foe != null)
                    {
                        float want = MathF.Atan2(foe.Z - me.Z, foe.X - me.X), d = MathF.Sqrt(best);
                        _eyesYaw = _eyesYaw + Mathf.Wrap(want - _eyesYaw, -MathF.PI, MathF.PI) * (1 - MathF.Exp(-4f / Fps));
                        FP.Face(_eyesYaw, -.12f - Math.Clamp(6 / Math.Max(d, 1), 0, .2f));
                        // in at it, and stepping aside round it as he fights, as one does
                        float reach = me.T.Reach + foe.T.BodyR;
                        FP.TestMove = new Vector2(.55f * MathF.Sin(t * 1.9f), d > reach + 1 ? 1 : d < reach - 2 ? -1 : 0);
                        sim.WishStrike = d < me.T.Reach + foe.T.BodyR + 6;
                    }
                    else { FP.TestMove = Vector2.Zero; sim.WishStrike = false; }
                }
            },
            Lens = t =>
            {
                // down behind him through the grass to over his shoulder, the band before him
                float k = CineCam.Ease(Math.Min(1, t / 2.7f));
                var eye = V(me.X + 44, me.Y + 44, me.Z + 30).Lerp(V(me.X + 13, me.Y + 19, me.Z + 6), k);
                var at = V(cx - 6, me.Y + 6, cz).Lerp(V(cx - 4, me.Y + 9, cz + 2), k);
                return new Lens(eye, at, Mathf.Lerp(40, 50, k));
            },
        };
        shot.At(2.8f, () =>
        {
            Cine.End();
            FP.Enter(me);
            _eyesYaw = MathF.PI;
            FP.Face(_eyesYaw, -.1f);
        });
        shot.At(9.0f, () =>
        {
            // the archer, out on the meadow's other side: three goblins come running at him across the open grass, and
            // he looses fire arrows at them
            var sim = Sim;
            sim.WishStrike = false;
            FP.TestMove = Vector2.Zero;
            FP.Exit();
            // the swordsman back among the band he fought, out of the archer's sight
            if (me != null && !me.Dead && sim.NearestStand(cx - 10, cz, 2) is int back and >= 0)
            {
                var (fx, fy, fz) = sim.Nav.Feet(back);
                me.Cell = back; me.X = fx; me.Y = fy; me.Z = fz;
            }
            if (bow == null || bow.Dead) return;
            float ox = bow.X - cx, oz = bow.Z - cz, ol = MathF.Max(1, MathF.Sqrt(ox * ox + oz * oz));
            // across the way from the fight, so it is behind him
            float dx = -oz / ol, dz = ox / ol;
            _runners.Clear();
            for (int i = 0; i < 3; i++)
            {
                int c = sim.NearestStand((int)(bow.X + dx * 64 + dz * (i - 1) * 9), (int)(bow.Z + dz * 64 - dx * (i - 1) * 9), 2);
                if (c >= 0) _runners.Add(sim.Spawn(Kinds.Goblin, c));
            }
            FP.Enter(bow);
            FP.Face(MathF.Atan2(dz, dx), -.02f);
        });
        shot.At(9.3f, () => { if (FP.On) { AimAt(bow); Sim.WishStrike = true; } });
        shot.At(9.5f, () => Sim.WishStrike = false);
        shot.At(10.5f, () => { if (FP.On) { AimAt(bow); Sim.WishStrike = true; } });
        shot.At(10.7f, () => Sim.WishStrike = false);
        shot.Tag(.4f, 8.8f, "Вид от первого лица").Tag(9.2f, 11.2f, "Огненные стрелы");
        return shot;
    }

    float _eyesYaw;
    /// <summary>The goblins that come running at the archer of the first-person shot.</summary>
    readonly List<Unit> _runners = new();

    /// <summary>The archer gone into turns his head on the nearest goblin running at him, a little over it for the drop.</summary>
    void AimAt(Unit bow)
    {
        Unit aim = null; float best = 1e9f;
        foreach (var u in _runners)
        {
            if (u.Dead) continue;
            float d = (u.X - bow.X) * (u.X - bow.X) + (u.Z - bow.Z) * (u.Z - bow.Z);
            if (d < best) { best = d; aim = u; }
        }
        if (aim == null) return;
        float dist = MathF.Sqrt(best);
        FP.Face(MathF.Atan2(aim.Z - bow.Z, aim.X - bow.X), MathF.Atan2(aim.Y + aim.T.BodyH * .6f - bow.Y - bow.T.BodyH * .88f, dist) + .015f);
    }

    /// <summary>11. Night, the fort under the blood moon, torches along its walls; the camera rises over the moat to the citadel, and the name of the game.</summary>
    Shot TakeTitle()
    {
        var s = Site;
        var shot = new Shot
        {
            Name = "title",
            Length = 11,
            FadeOut = 2.4f,
            Day = _ => 1, Dark = _ => .92f,
            Haze = .45f, ClearAir = 40, Hand = .6f, Exposure = 1.45f,
            Stage = () =>
            {
                Sorcerous();
                FortStanding();
                ManTheWalls(6, 2);
                Nightfall();
                Advance(1);
                Sim.Cast(SpellKind.BloodMoon, s.KeepX, 0, s.KeepZ);
                Advance(4);
            },
            Lens = t =>
            {
                // from the causeway on the east, low by the gate, rising and drawing back over the moat till the fort
                // lies under the red night with the valley beyond it
                float k = CineCam.Ease(t / 11);
                int kx = s.KeepX, kz = s.KeepZ;
                return CineCam.Dolly(k, G(kx + MoatR + 70, kz + 40, 40), G(kx + MoatR + 170, kz + 130, 170), V(kx + RingR, RingTop - 8, kz), V(kx - 20, s.KeepBase + 40, kz - 10), 42, 40);
            },
        };
        shot.Say(3.4f, 10.4f, "СУМРАЧНЫЙ ВАЛ", "", title: true);
        return shot;
    }
}
