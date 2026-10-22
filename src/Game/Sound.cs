using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The valley's sounds, made by the game itself rather than recorded: each a moment of samples worked out at the start
/// from noise, strings and bells — a pick on rock, an axe in timber, a spade in earth, a stone set down, a bowstring, an
/// arrow going home, a blade swung through the air, blades meeting, a blade biting flesh, a club, a death cry, the
/// horn, stone coming down, thunder, a spell, the gong of a
/// rite, a shaman's croon, a blast, stone crashing down, the black wind — and the fire, the rain and the hum of the hand
/// of telekinesis as loops. Each is played where it happens, as loud as that is near the middle of the view, the same
/// one no more often than it would be heard apart, a score of them at most. The frame of the game has its own, heard
/// wherever the view is: a wooden tick for a button, a soft knock of chalk for work marked, a low muffled pair of notes
/// for what cannot be done, a small bell for work finished.
/// </summary>
public partial class Sound : Node
{
    const int Rate = 22050;
    readonly Dictionary<string, AudioStreamWav> _clips = new();
    readonly List<AudioStreamPlayer> _pool = new();
    readonly Dictionary<string, double> _last = new();
    readonly Dictionary<int, float> _swing = new();
    readonly HashSet<int> _dead = new();
    readonly HashSet<Arrow> _landed = new();
    AudioStreamPlayer _fire, _rain, _hum;
    Main _m;
    readonly Random _rnd = new(5);
    double _t;
    int _wave;

    /// <summary>Sounds played since the start, and which of them were heard at all (the self-test asks).</summary>
    public int Played { get; private set; }
    public readonly HashSet<string> Heard = new();
    /// <summary>The sounds there are.</summary>
    public int Clips => _clips.Count;

    /// <summary>Writes every sound into a folder as a .wav, to be listened to.</summary>
    public void SaveAll(string dir)
    {
        DirAccess.MakeDirRecursiveAbsolute(dir);
        foreach (var (name, wav) in _clips) wav.SaveToWav($"{dir}/{name}.wav");
    }

    /// <summary>The game's sounds now (for the frame's buttons, wherever they are).</summary>
    public static Sound Now { get; private set; }

    /// <summary>A sound of the frame: a button, a mark, a refusal, work done.</summary>
    public static void Ui(string clip, float loud = .55f) => Now?.Plain(clip, loud);

    void Plain(string clip, float loud)
    {
        if (!_clips.TryGetValue(clip, out var wav)) return;
        if (_last.TryGetValue(clip, out double at) && _t - at < .05) return;
        foreach (var p in _pool)
        {
            if (p.Playing) continue;
            _last[clip] = _t;
            p.Stream = wav;
            p.VolumeDb = Mathf.LinearToDb(loud * loud);
            p.PitchScale = .96f + (float)_rnd.NextDouble() * .08f;
            p.Play();
            Played++; Heard.Add(clip);
            return;
        }
    }

    /// <summary>Every button of the frame ticks when pressed, the ones made later too.</summary>
    void Hook(Node n)
    {
        // a button taken off and put back (a panel let down again) is hooked once
        if (n is not BaseButton b || b.HasMeta("tick_hooked")) return;
        b.SetMeta("tick_hooked", true);
        b.Pressed += () => Ui("tick");
    }

    public void Init(Main m)
    {
        _m = m;
        Now = this;
        Make();
        GetTree().NodeAdded += Hook;
        TreeExiting += () => { GetTree().NodeAdded -= Hook; if (Now == this) Now = null; };
        for (int i = 0; i < 20; i++) { var p = new AudioStreamPlayer(); AddChild(p); _pool.Add(p); }
        _fire = Loop("fire");
        _rain = Loop("rain");
        _hum = Loop("hum");
        var sim = m.Sim;
        _wave = sim.Waves.Number;
        // a pawn's tool rings at each blow, whether a voxel comes away or not; a stone set down knocks softly
        sim.Worked += (x, y, z, mat, dig) => { if (dig) Struck(Dug(mat), x, y, z, .07); else Play("dig", x + .5f, y, z + .5f, .12, .35f, pitch: 1.45f); };
        sim.Hacked += (x, y, z, mat) => Struck(Dug(mat), x, y, z, .09);
        sim.Collapsed += (n, x, y, z) => Play("rumble", x, y, z, .4, Math.Clamp(.4f + n / 2000f, .4f, 1.3f));
        sim.Spelled += (k, x, y, z) =>
        {
            if (k == SpellKind.BlackWind) Play("wind", x, y, z, .2, 1.1f, far: true);
            if (k != SpellKind.Explosion) Play("spell", x, y, z, .2, 1, far: true);
        };
        // a blast, of the spell or of powder: the bigger, the louder
        sim.Exploded += (x, y, z, r) =>
        {
            float loud = Math.Clamp(.5f + r / (3 * VoxelWorld.PerMetre), .5f, 1.4f);
            Play("boom", x, y, z, .08, loud, far: true);
            // a great one rolls on after, low, as the valley throws it back
            if (r > 1.5f * VoxelWorld.PerMetre) GetTree().CreateTimer(.3).Timeout += () => Play("rumble", x, y, z, 0, loud * .8f, far: true, pitch: .7f);
        };
        sim.Planted += c => Play("fuse", c.X, c.Y, c.Z, .5, 1);
        // a blow heard where it lands, as the blade is seen to get there: steel rings on steel and mail, a shield's
        // boards knock, flesh takes a blade wetly and a club with a thud; and the blade through the air as it is seen
        // to start down
        m.Units.Landed += Landed;
        m.Units.Swung += Swish;
        // a piece of the world coming down: the heavier, the louder
        // a piece of the world coming down: the heavier, the louder, and a great one deeper
        sim.Landed += (x, y, z, n, speed) => { if (n >= 20) Play("crash", x, y, z, .07, Math.Clamp(.25f + n / 700f, .25f, 1.1f), pitch: n >= 600 ? .72f : 0); };
        sim.Struck += (x, y, z) => Play("thunder", x, y, z, .1, 1.3f, far: true);
        // and far off, low and long, after the sheet lightning of a night of rain
        m.Atmos.Thunder += loud => Everywhere("thunder", loud, .62f);
        sim.RiteDone += (x, y, z) => Play("gong", x, y, z, .5, 1, far: true);
        sim.Healed += (x, y, z) => Play("hex", x, y, z, .6, .5f);
        sim.Hexed += (x, y, z) => Play("hex", x, y, z, .6, 1);
        sim.Darkened += (x, y, z) => Play("hex", x, y, z, .6, .9f);
        sim.Woken += (x, y, z) => Play("gong", x, y, z, 1, .6f);
    }

    /// <summary>A blade, a club or a beast's jaws starting down at someone: the swish of it through the air, or a beast's lunge.</summary>
    void Swish(Unit u)
    {
        if (_m.Paused || u.Inside) return;
        var (cut, _, _) = Sim.WeaponOf(u.T);
        if (u.T.NoHands) Play("blunt", u.X, u.Y, u.Z, .09, .6f);
        else Play("swish", u.X, u.Y, u.Z, .05, cut is Sim.Cut.Crush ? .55f : .7f, pitch: cut is Sim.Cut.Crush ? .7f : u.T.BodyH > 18 ? .8f : 0);
    }

    /// <summary>A blow landing: on steel, iron or mail a ring (higher off one turned aside), on boards a knock, into flesh a blade's bite or a club's thud.</summary>
    void Landed(Smiting h)
    {
        byte m = h.Mat;
        bool metal = m != Mat.Air && (Mat.Metal[m] && m != Mat.Pitch || m == Mat.Iron);
        bool crush = h.Stroke is StrokeKind.Smash or StrokeKind.Bash || Sim.WeaponOf(h.By.T).cut == Sim.Cut.Crush;
        if (metal) Play("blade", h.X, h.Y, h.Z, .06, m == Mat.Mail ? .6f : .85f, pitch: h.Glanced ? 1.15f : m == Mat.Mail ? .8f : 0);
        else if (m is Mat.Board or Mat.BoardDark or Mat.Handle) Play("chop", h.X, h.Y, h.Z, .06, .8f, pitch: 1.2f);
        else if (h.Flesh > 0) Play(crush || h.By.T.NoHands ? "blunt" : "hew", h.X, h.Y, h.Z, .06, Math.Clamp(.55f + h.Flesh * .02f, .55f, .95f));
        else if (m != Mat.Air) Play("blunt", h.X, h.Y, h.Z, .08, .5f);
    }

    /// <summary>What a voxel dug or hacked out sounds like: rock and masonry knock, timber thuds, earth sighs.</summary>
    static string Dug(byte m) => Mat.Def[m].Group switch { MatGroup.Wood => "chop", MatGroup.Earth => "dig", _ => "pick" };

    void Struck(string clip, int x, int y, int z, double every) => Play(clip, x + .5f, y, z + .5f, every, .8f);

    AudioStreamPlayer Loop(string clip)
    {
        var p = new AudioStreamPlayer { Stream = _clips[clip], VolumeDb = -80 };
        AddChild(p);
        p.Play();
        return p;
    }

    /// <summary>How loud a spot of the valley is heard: full at the middle of the view, nothing some fifty metres off.</summary>
    float Near(float x, float z, bool far = false)
    {
        var w = _m.World;
        var c = _m.Rig.Target;
        float dx = w.WX(x) - c.X, dz = w.WZ(z) - c.Y, d = MathF.Sqrt(dx * dx + dz * dz);
        float reach = (far ? 110 : 50) / MathF.Sqrt(Math.Max(.4f, _m.Rig.Aim.zoom));
        return Math.Clamp(1 - d / reach, 0, 1);
    }

    /// <summary>Plays a sound at a spot (voxels), unless the same one was heard just now or it is too far to be heard.</summary>
    /// <summary>A soft stroke of the gong, high, heard wherever the view is: a step of the tutorial done.</summary>
    public void Chime()
    {
        if (!_clips.TryGetValue("gong", out var wav)) return;
        foreach (var p in _pool)
        {
            if (p.Playing) continue;
            p.Stream = wav;
            p.VolumeDb = Mathf.LinearToDb(.2f);
            p.PitchScale = 1.7f;
            p.Play();
            return;
        }
    }

    void Play(string clip, float x, float y, float z, double every, float loud, bool far = false, float pitch = 0)
    {
        if (!_clips.TryGetValue(clip, out var wav)) return;
        if (_last.TryGetValue(clip, out double at) && _t - at < every) return;
        float gain = Near(x, z, far) * loud;
        if (gain < .03f) return;
        foreach (var p in _pool)
        {
            if (p.Playing) continue;
            _last[clip] = _t;
            p.Stream = wav;
            p.VolumeDb = Mathf.LinearToDb(gain * gain);
            p.PitchScale = (pitch > 0 ? pitch : 1) * (.92f + (float)_rnd.NextDouble() * .16f);
            p.Play();
            Played++;
            Heard.Add(clip);
            return;
        }
    }

    /// <summary>A sound heard everywhere alike: the horn, thunder far off.</summary>
    void Everywhere(string clip, float loud, float pitch = 1)
    {
        if (!_clips.TryGetValue(clip, out var wav)) return;
        foreach (var p in _pool)
        {
            if (p.Playing) continue;
            p.Stream = wav; p.VolumeDb = Mathf.LinearToDb(loud); p.PitchScale = pitch;
            p.Play();
            Played++;
            Heard.Add(clip);
            return;
        }
    }

    public override void _Process(double delta)
    {
        using var prof = Prof.Time("Sound");
        if (_m?.Sim == null) return;
        _t += delta;
        var sim = _m.Sim;
        // the horn for every wave that comes, even one begun and over between two frames
        if (sim.Waves.Number > _wave) Everywhere("horn", .9f);
        _wave = sim.Waves.Number;
        if (_m.Paused) { _fire.VolumeDb = _rain.VolumeDb = _hum.VolumeDb = -80; return; }
        _hum.VolumeDb = sim.HeldNow is { } held && Near(held.X, held.Z, far: true) is var hn && hn > .05f ? Mathf.LinearToDb(hn * .5f) : -80;
        // blows struck and bows loosed, as they happen; the fallen, once
        foreach (var u in sim.Units)
        {
            if (u.Dead)
            {
                if (_dead.Add(u.Id) && !Kinds.IsEngine(u.T) && sim.Time - u.DiedAt < 1) Play(u.Team == Team.Wild ? "blunt" : "death", u.X, u.Y, u.Z, .15, .9f);
                continue;
            }
            float was = _swing.GetValueOrDefault(u.Id);
            _swing[u.Id] = u.Swing;
            if (u.Swing < .99f || was > .6f || u.Inside) continue;
            // (a blow hand to hand is heard as the eye sees it: Swish, Landed)
            if (u.State == UnitState.Fight)
            {
                if (u.T.Ranged) Play("bow", u.X, u.Y, u.Z, .08, .7f);
            }
            else if (u.State == UnitState.Work && u.Team == Team.Player && u.JobI > 0 && u.JobI <= u.Jobs.Count && !Orders.IsRemove(u.Jobs[u.JobI - 1].m))
                Play("lay", u.X, u.Y, u.Z, .08, .55f);
        }
        foreach (var a in sim.Arrows)
            if (a.Done && a.Hits && a.Kind is not (Shot.Pot or Shot.FirePot) && _landed.Add(a) && a.Target != null) Play(a.Kind == Shot.Stone ? "blunt" : "arrow", a.X1, a.Y1, a.Z1, .06, .7f);
        if (_landed.Count > 400) _landed.RemoveWhere(a => !sim.Arrows.Contains(a));
        if (_dead.Count > 2000) _dead.Clear();
        // the fire, by how much of it burns near the middle of the view; the rain, while it rains
        int burning = 0, look = 0;
        foreach (int p in sim.Burning.Keys)
        {
            if (++look > 600) break;
            var (x, _, z) = WaterPlanner.Unpack(p);
            if (Near(x, z) > .3f) burning++;
        }
        _fire.VolumeDb = burning == 0 ? -80 : Mathf.LinearToDb(Math.Clamp(burning / 120f, .12f, .8f));
        _rain.VolumeDb = sim.Weather == Weather.Rain ? -9 : -80;
    }

    // ---------------------------------------------------------------- making the sounds

    void Make()
    {
        Add("pick", Pick());
        Add("chop", Chop());
        Add("dig", Dig());
        Add("lay", Lay());
        Add("bow", Bow());
        Add("arrow", ArrowIn());
        Add("swish", Swish());
        Add("blade", Blades());
        Add("hew", Hew());
        Add("blunt", Thud());
        Add("death", Cry());
        Add("horn", Horn());
        Add("rumble", Rumble(1.4f, .9f));
        Add("thunder", Thunder());
        Add("spell", Spell());
        Add("gong", Gong());
        Add("hex", Croon());
        Add("boom", Boom());
        Add("crash", Crash());
        Add("wind", Gust());
        Add("fuse", Hiss());
        Add("fire", Crackle(), loop: true);
        Add("rain", RainLoop(), loop: true);
        Add("hum", Hum(), loop: true);
        Add("tick", Tick());
        Add("mark", Chalk());
        Add("nope", Nope());
        Add("done", Bell());
    }

    void Add(string name, float[] s, bool loop = false)
    {
        var bytes = new byte[s.Length * 2];
        for (int i = 0; i < s.Length; i++)
        {
            short v = (short)Math.Clamp(s[i] * 32000, -32768, 32767);
            bytes[2 * i] = (byte)v; bytes[2 * i + 1] = (byte)(v >> 8);
        }
        var wav = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = bytes };
        if (loop) { wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward; wav.LoopBegin = 0; wav.LoopEnd = s.Length; }
        _clips[name] = wav;
    }

    static float[] Buf(float seconds) => new float[(int)(seconds * Rate)];
    float Noise() => (float)(_rnd.NextDouble() * 2 - 1);
    static float Decay(int i, float tau) => MathF.Exp(-i / (tau * Rate));

    /// <summary>A two-pole resonator: noise through it rings at a pitch.</summary>
    sealed class Ring
    {
        readonly float _a1, _a2, _g;
        float _y1, _y2;
        public Ring(float freq, float q)
        {
            float r = MathF.Exp(-MathF.PI * freq / (q * Rate)), th = MathF.Tau * freq / Rate;
            _a1 = 2 * r * MathF.Cos(th); _a2 = -r * r; _g = 1 - r;
        }
        public float Step(float x) { float y = _g * x + _a1 * _y1 + _a2 * _y2; _y2 = _y1; _y1 = y; return y; }
    }

    /// <summary>A one-pole low-pass: what is above the cut-off fades.</summary>
    sealed class Low
    {
        readonly float _a; float _y;
        public Low(float cut) { _a = 1 - MathF.Exp(-MathF.Tau * cut / Rate); }
        public float Step(float x) => _y += _a * (x - _y);
    }

    static void Normalise(float[] s, float peak = .9f)
    {
        float m = 1e-6f;
        foreach (float v in s) m = Math.Max(m, Math.Abs(v));
        for (int i = 0; i < s.Length; i++) s[i] *= peak / m;
    }

    /// <summary>A pick on rock: a sharp tick and the iron ringing a moment.</summary>
    float[] Pick()
    {
        var s = Buf(.14f);
        var r1 = new Ring(2300, 18); var r2 = new Ring(3550, 22); var lo = new Low(5000);
        for (int i = 0; i < s.Length; i++)
        {
            float n = Noise() * Decay(i, .006f);
            s[i] = lo.Step(n) * .6f + r1.Step(n) * 9 * Decay(i, .05f) + r2.Step(n) * 6 * Decay(i, .035f);
        }
        Normalise(s, .7f);
        return s;
    }

    /// <summary>An axe in timber: a dull knock with the wood's body under it.</summary>
    float[] Chop()
    {
        var s = Buf(.2f);
        var lo = new Low(900); var body = new Ring(260, 6);
        for (int i = 0; i < s.Length; i++)
        {
            float n = Noise() * Decay(i, .02f);
            float t = i / (float)Rate, thump = MathF.Sin(MathF.Tau * (150 - 60 * t) * t) * Decay(i, .06f);
            s[i] = lo.Step(n) * .8f + body.Step(n) * 3 + thump * .6f;
        }
        Normalise(s, .75f);
        return s;
    }

    /// <summary>A spade in earth: a soft gritty push.</summary>
    float[] Dig()
    {
        var s = Buf(.18f);
        var lo = new Low(650); var lo2 = new Low(1800);
        for (int i = 0; i < s.Length; i++)
        {
            float env = Math.Min(1, i / (.012f * Rate)) * Decay(i, .06f);
            s[i] = (lo.Step(Noise()) * 1.4f + lo2.Step(Noise()) * .25f) * env;
        }
        Normalise(s, .6f);
        return s;
    }

    /// <summary>A stone set down on stone, or a log on a log: a short clack.</summary>
    float[] Lay()
    {
        var s = Buf(.1f);
        var r = new Ring(1150, 9); var lo = new Low(2500);
        for (int i = 0; i < s.Length; i++)
        {
            float n = Noise() * Decay(i, .008f);
            s[i] = r.Step(n) * 4 * Decay(i, .02f) + lo.Step(n) * .5f;
        }
        Normalise(s, .6f);
        return s;
    }

    /// <summary>A bowstring let go: a plucked string, quickly damped, and the hiss of the arrow leaving.</summary>
    float[] Bow()
    {
        var s = Buf(.32f);
        int period = Rate / 170;
        var line = new float[period];
        for (int i = 0; i < period; i++) line[i] = Noise();
        var lo = new Low(3000);
        for (int i = 0, k = 0; i < s.Length; i++, k = (k + 1) % period)
        {
            float v = line[k];
            line[k] = (line[k] + line[(k + 1) % period]) * .493f;
            float hiss = lo.Step(Noise()) * .12f * Decay(i, .05f);
            s[i] = v * Decay(i, .09f) + hiss;
        }
        Normalise(s, .55f);
        return s;
    }

    /// <summary>An arrow going home: a short dull thud with a click on it.</summary>
    float[] ArrowIn()
    {
        var s = Buf(.09f);
        var lo = new Low(500);
        for (int i = 0; i < s.Length; i++) s[i] = lo.Step(Noise()) * 2 * Decay(i, .025f) + (i < 40 ? Noise() * .3f : 0);
        Normalise(s, .6f);
        return s;
    }

    /// <summary>Blades meeting: iron ringing in several pitches that are no chord, and the scrape of the edge.</summary>
    float[] Blades()
    {
        var s = Buf(.45f);
        float[] f = { 1240, 2870, 4110, 5420 }, tau = { .18f, .12f, .09f, .06f };
        var lo = new Low(6000);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, v = 0;
            for (int k = 0; k < f.Length; k++) v += MathF.Sin(MathF.Tau * f[k] * t + k) * Decay(i, tau[k]) / (k + 1);
            s[i] = v * .8f + lo.Step(Noise()) * .5f * Decay(i, .012f);
        }
        Normalise(s, .55f);
        return s;
    }

    /// <summary>A blade swung through the air: a breath of noise rising and falling in pitch as the edge goes by.</summary>
    float[] Swish()
    {
        var s = Buf(.24f);
        for (int i = 0; i < s.Length; i++)
        {
            float k = i / (float)s.Length;
            // the edge nearest at a third of the way: loudest and highest there
            float near = MathF.Exp(-(k - .35f) * (k - .35f) / .03f);
            s[i] = Noise() * near;
        }
        // the hiss through a resonance that sweeps up and back down
        var outp = new float[s.Length];
        float y1 = 0, y2 = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float k = i / (float)s.Length, f = 700 + 1500 * MathF.Exp(-(k - .35f) * (k - .35f) / .02f);
            float r = MathF.Exp(-MathF.PI * f / (3.5f * Rate)), th = MathF.Tau * f / Rate;
            float y = (1 - r) * s[i] + 2 * r * MathF.Cos(th) * y1 - r * r * y2;
            y2 = y1; y1 = y; outp[i] = y;
        }
        Normalise(outp, .45f);
        return outp;
    }

    /// <summary>A blade biting into flesh: a short wet knock with a tearing edge to it.</summary>
    float[] Hew()
    {
        var s = Buf(.2f);
        var lo = new Low(420); var body = new Ring(170, 4); var hi = new Low(2600); var under = new Low(600);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, n = Noise();
            float knock = body.Step(n * Decay(i, .012f)) * 5;
            float wet = lo.Step(n) * Decay(i, .05f) * 1.4f * (1 + .5f * MathF.Sin(MathF.Tau * 31 * t));
            float tear = (hi.Step(n) - under.Step(n)) * Decay(i, .018f) * .5f;
            s[i] = knock + wet + tear;
        }
        Normalise(s, .7f);
        return s;
    }

    /// <summary>A club or a fist on a body: a deep thud.</summary>
    float[] Thud()
    {
        var s = Buf(.16f);
        var lo = new Low(300);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            s[i] = MathF.Sin(MathF.Tau * (110 - 50 * t) * t) * Decay(i, .05f) + lo.Step(Noise()) * Decay(i, .03f) * 1.5f;
        }
        Normalise(s, .7f);
        return s;
    }

    /// <summary>A death cry: a rough voice falling away, through the throat's resonance.</summary>
    float[] Cry()
    {
        var s = Buf(.42f);
        var f1 = new Ring(720, 5); var f2 = new Ring(1150, 6);
        float phase = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, pitch = 190 - 120 * t;
            phase += pitch / Rate;
            float saw = 2 * (phase - MathF.Floor(phase)) - 1 + Noise() * .15f;
            float env = Math.Min(1, t / .03f) * MathF.Exp(-t / .16f);
            s[i] = (f1.Step(saw) + f2.Step(saw) * .6f) * env;
        }
        Normalise(s, .5f);
        return s;
    }

    /// <summary>The war horn: a low brazen note swelling, wavering, and dying away.</summary>
    float[] Horn()
    {
        var s = Buf(2.6f);
        var lo = new Low(700); var lo2 = new Low(1100);
        float phase = 0, phase2 = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, f = 98 * (1 + .012f * MathF.Sin(MathF.Tau * 5 * t)) * (t < .2f ? .94f + .3f * t : 1);
            phase += f / Rate; phase2 += f * 1.5f / Rate;
            float saw = 2 * (phase - MathF.Floor(phase)) - 1, saw2 = 2 * (phase2 - MathF.Floor(phase2)) - 1;
            float env = Math.Min(1, t / .3f) * (t > 2f ? Math.Max(0, 1 - (t - 2f) / .6f) : 1);
            s[i] = (lo2.Step(lo.Step(saw)) + lo.Step(saw2) * .25f) * env;
        }
        Normalise(s, .7f);
        return s;
    }

    /// <summary>Stone coming down: a rolling rumble with cracks in its first moments.</summary>
    float[] Rumble(float seconds, float peak)
    {
        var s = Buf(seconds);
        var lo = new Low(140); var lo2 = new Low(260);
        float brown = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            brown = Math.Clamp(brown + Noise() * .06f, -1, 1);
            float crack = t < .35f && _rnd.NextDouble() < .002 ? 1 : 0;
            s[i] = (lo.Step(brown) * 3 + lo2.Step(Noise()) * .4f) * Math.Min(1, t / .02f) * MathF.Exp(-t / (seconds * .4f)) + crack * Noise();
        }
        Normalise(s, peak);
        return s;
    }

    /// <summary>Thunder close by: a crack like the sky splitting, and the long roll after it.</summary>
    float[] Thunder()
    {
        var roll = Rumble(2.4f, .9f);
        for (int i = 0; i < (int)(.05f * Rate); i++) roll[i] += Noise() * (1 - i / (.05f * Rate));
        Normalise(roll, .9f);
        return roll;
    }

    /// <summary>A spell: a rushing breath rising in pitch, and a cold chime over it.</summary>
    float[] Spell()
    {
        var s = Buf(1.1f);
        float y1 = 0, y2 = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, f = 300 + 1700 * t / 1.1f, r = .985f, th = MathF.Tau * f / Rate;
            float y = (1 - r) * Noise() * 14 + 2 * r * MathF.Cos(th) * y1 - r * r * y2;
            y2 = y1; y1 = y;
            float swell = MathF.Sin(MathF.PI * Math.Min(1, t / 1.1f));
            float chime = (MathF.Sin(MathF.Tau * 880 * t) + .6f * MathF.Sin(MathF.Tau * 1318 * t)) * (t > .45f ? MathF.Exp(-(t - .45f) / .35f) : 0);
            s[i] = y * swell * .6f + chime * .35f;
        }
        Normalise(s, .55f);
        return s;
    }

    /// <summary>A blast: a crack that splits the air, a blow felt in the chest falling in pitch, and the rubble raining after.</summary>
    float[] Boom()
    {
        var s = Buf(3.2f);
        var lo = new Low(90); var lo2 = new Low(400);
        float brown = 0, phase = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            brown = Math.Clamp(brown + Noise() * .08f, -1, 1);
            phase += (62 - 34 * Math.Min(1, t / .6f)) / Rate;
            float thump = MathF.Sin(MathF.Tau * phase) * MathF.Exp(-t / .35f) * 1.6f;
            float crack = t < .04f ? Noise() * (1 - t / .04f) * 1.3f : 0;
            float roll = (lo.Step(brown) * 4 + lo2.Step(Noise()) * .5f) * Math.Min(1, t / .01f) * MathF.Exp(-t / .9f);
            // stones pattering down after it
            float patter = t > .6f && _rnd.NextDouble() < .0016 * MathF.Exp(-(t - .6f) / .9f) ? 1 : 0;
            s[i] = thump + crack + roll + patter * Noise() * .8f;
        }
        Normalise(s, .95f);
        return s;
    }

    /// <summary>A stone coming down on the ground: a heavy dull blow and the grit of it.</summary>
    float[] Crash()
    {
        var s = Buf(.5f);
        var lo = new Low(180); var lo2 = new Low(1400);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            float body = MathF.Sin(MathF.Tau * (85 - 40 * t) * t) * Decay(i, .07f);
            s[i] = body * 1.2f + lo.Step(Noise()) * 2.5f * Decay(i, .09f) + lo2.Step(Noise()) * .5f * Decay(i, .04f);
        }
        Normalise(s, .8f);
        return s;
    }

    /// <summary>The black wind: a gust howling up and dying away.</summary>
    float[] Gust()
    {
        var s = Buf(2.2f);
        float y1 = 0, y2 = 0;
        var lo = new Low(500);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, f = 220 + 520 * MathF.Sin(MathF.PI * t / 2.2f), r = .992f, th = MathF.Tau * f / Rate;
            float y = (1 - r) * Noise() * 10 + 2 * r * MathF.Cos(th) * y1 - r * r * y2;
            y2 = y1; y1 = y;
            s[i] = (y * .7f + lo.Step(Noise()) * 1.4f) * MathF.Sin(MathF.PI * t / 2.2f);
        }
        Normalise(s, .7f);
        return s;
    }

    /// <summary>A fuse lit: a fizzing hiss, spitting now and then.</summary>
    float[] Hiss()
    {
        var s = Buf(1.4f);
        var lo = new Low(5200); var hi = new Low(1500);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, n = lo.Step(Noise());
            float spit = _rnd.NextDouble() < .004 ? Noise() * 2 : 0;
            s[i] = ((n - hi.Step(n)) * (.7f + .3f * MathF.Sin(MathF.Tau * 13 * t)) + spit) * Math.Min(1, t / .03f) * Math.Min(1, (1.4f - t) / .2f);
        }
        Normalise(s, .45f);
        return s;
    }

    /// <summary>The hand of telekinesis holding: a low hum, beating slowly, a second that goes round.</summary>
    float[] Hum()
    {
        var s = Buf(1);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            s[i] = (MathF.Sin(MathF.Tau * 74 * t) + .6f * MathF.Sin(MathF.Tau * 75 * t) + .25f * MathF.Sin(MathF.Tau * 148 * t)) * .5f;
        }
        Normalise(s, .5f);
        return s;
    }

    /// <summary>The gong of a rite: deep tones a little apart, beating as they fade.</summary>
    float[] Gong()
    {
        var s = Buf(2.8f);
        float[] f = { 72, 73.4f, 146, 215, 290 }, a = { 1, .8f, .5f, .3f, .15f };
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, v = 0;
            for (int k = 0; k < f.Length; k++) v += a[k] * MathF.Sin(MathF.Tau * f[k] * t) * MathF.Exp(-t / (1.6f - k * .2f));
            s[i] = v * Math.Min(1, t / .01f);
        }
        Normalise(s, .7f);
        return s;
    }

    /// <summary>A button: a dry wooden tick.</summary>
    float[] Tick()
    {
        var s = Buf(.05f);
        var ring = new Ring(2300, 9);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            s[i] = ring.Step(Noise()) * Decay(i, .005f) * 3 + MathF.Sin(MathF.Tau * 540 * t) * Decay(i, .01f) * .5f;
        }
        Normalise(s, .45f);
        return s;
    }

    /// <summary>Work marked: a soft knock, and chalk drawn a moment over wood.</summary>
    float[] Chalk()
    {
        var s = Buf(.18f);
        var lo = new Low(1800);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            float scrape = lo.Step(Noise()) * Math.Min(1, t / .012f) * MathF.Exp(-t / .05f);
            s[i] = scrape + MathF.Sin(MathF.Tau * 170 * t) * MathF.Exp(-t / .03f) * .7f;
        }
        Normalise(s, .5f);
        return s;
    }

    /// <summary>What cannot be done: two low notes, the second lower, muffled.</summary>
    float[] Nope()
    {
        var s = Buf(.24f);
        var lo = new Low(700);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, f = t < .1f ? 196 : 156, tt = t < .1f ? t : t - .11f;
            float env = tt < 0 ? 0 : Math.Min(1, tt / .008f) * MathF.Exp(-tt / .05f);
            float v = MathF.Sin(MathF.Tau * f * t);
            s[i] = lo.Step(MathF.Sign(v) * .6f + v * .4f) * env;
        }
        Normalise(s, .42f);
        return s;
    }

    /// <summary>Work finished: a small bell struck twice, the second a fourth higher.</summary>
    float[] Bell()
    {
        var s = Buf(1.1f);
        float[] p = { 1, 2.76f, 5.4f }, a = { 1, .35f, .12f };
        var strokes = new[] { (880f, 0f), (1174.7f, .13f) };
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, v = 0;
            foreach (var (f0, at) in strokes)
            {
                float tt = t - at;
                if (tt < 0) continue;
                for (int k = 0; k < p.Length; k++) v += a[k] * MathF.Sin(MathF.Tau * f0 * p[k] * tt) * MathF.Exp(-tt / (.45f - k * .12f)) * Math.Min(1, tt / .002f);
            }
            s[i] = v;
        }
        Normalise(s, .38f);
        return s;
    }

    /// <summary>A shaman's croon: a low tone wavering, a hiss under it.</summary>
    float[] Croon()
    {
        var s = Buf(.9f);
        var lo = new Low(2500);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, trem = .6f + .4f * MathF.Sin(MathF.Tau * 7 * t);
            float env = MathF.Sin(MathF.PI * t / .9f);
            s[i] = (MathF.Sin(MathF.Tau * 160 * t + .8f * MathF.Sin(MathF.Tau * 3 * t)) * trem + lo.Step(Noise()) * .2f) * env;
        }
        Normalise(s, .5f);
        return s;
    }

    /// <summary>A fire: a low roar and the crackle of what it burns, two seconds that go round.</summary>
    float[] Crackle()
    {
        var s = Buf(2);
        var lo = new Low(220);
        for (int i = 0; i < s.Length; i++) s[i] = lo.Step(Noise()) * .9f;
        for (int k = 0; k < 70; k++)
        {
            int at = _rnd.Next(s.Length - 400);
            float amp = .2f + (float)_rnd.NextDouble() * .5f;
            var r = new Ring(1500 + (float)_rnd.NextDouble() * 2500, 8);
            for (int j = 0; j < 300; j++) s[at + j] += r.Step(Noise() * (j < 30 ? 1 : 0)) * 6 * amp * Decay(j, .004f);
        }
        // the ends meet without a click
        for (int j = 0; j < 400; j++) { float k = j / 400f; s[j] = s[j] * k + s[s.Length - 400 + j] * (1 - k); }
        Normalise(s, .6f);
        return s;
    }

    /// <summary>Rain: a steady hush of many drops, two seconds that go round.</summary>
    float[] RainLoop()
    {
        var s = Buf(2);
        var lo = new Low(3800); var hi = new Low(400);
        for (int i = 0; i < s.Length; i++) { float n = lo.Step(Noise()); s[i] = n - hi.Step(n); }
        for (int k = 0; k < 160; k++)
        {
            int at = _rnd.Next(s.Length - 120);
            for (int j = 0; j < 100; j++) s[at + j] += Noise() * .25f * Decay(j, .002f);
        }
        for (int j = 0; j < 400; j++) { float k = j / 400f; s[j] = s[j] * k + s[s.Length - 400 + j] * (1 - k); }
        Normalise(s, .5f);
        return s;
    }
}
