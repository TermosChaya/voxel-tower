using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// What first person writes over the eyes besides the mark: its body part by part beside its name, what it lays and how
/// much the stores have of it, the horde's clock at the top, a word in the middle when the hands cannot do what was
/// asked, and for a commander its four calls on 1–4 with how long each has yet to wait. Here too the commander's calls
/// and its order by looking, the going over into another of ours, and the falling and going dark when it dies.
/// </summary>
public partial class FirstPerson
{
    ColorRect _fade;
    Label _held, _clock, _said;
    BodyChart _chart;
    CallBar _calls;
    string _saidText = "";
    double _saidAt = -99;

    void BuildHud(Control root)
    {
        _chart = new BodyChart { MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(44, 74) };
        root.AddChild(_chart);
        _held = Hud.Text("", Ornament.Serif, 14, Ornament.Parchment, outline: 5);
        _clock = Hud.Text("", Ornament.Serif, 14, Ornament.Aged, outline: 5);
        _said = Hud.Text("", Ornament.SerifBold, 15, Ornament.BronzeLight, outline: 6);
        foreach (var l in new[] { _held, _clock, _said }) { l.MouseFilter = Control.MouseFilterEnum.Ignore; root.AddChild(l); }
        _calls = new CallBar { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        root.AddChild(_calls);
        // over everything: the dark it goes into as it dies
        _fade = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Color = new Color(0, 0, 0, 0) };
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_fade);
    }

    /// <summary>What the frame says now (the self-test reads it): its name line, what it holds, the word in the middle, whether a commander's calls show, whether it is dying.</summary>
    public string NameLine => _name.Text;
    public string HeldLine => _held.Text;
    public string SaidLine => _said.Text;
    public bool CallsShown => _calls.Visible && _layer.Visible;
    public bool Dying => _dying > 0;

    /// <summary>A word in the middle of the sight for a few seconds: why the hands could not, or what an order did.</summary>
    void Said(string text) { _saidText = text; _saidAt = Time.GetTicksMsec() / 1000.0; }

    /// <summary>What the frame writes this frame: the body, what it holds, the clock, the word, a commander's calls.</summary>
    void ShowHud(Unit u, Sim sim, Vector2 vp)
    {
        _chart.Unit = u;
        _chart.Position = new Vector2(30, vp.Y - 100);
        _chart.QueueRedraw();
        // what it holds: for a worker the material it lays and the stores of it
        string held = "";
        if (u.T.Work > 0 && !Sim.Fights(u))
        {
            int ck = Labour.CostKind(sim.LayMat);
            held = "Кладёт: " + Mat.NameOf(sim.LayMat).ToLowerInvariant() + (ck != Stockpile.None ? $" · в запасе {Fmt.M3(sim.Stock[ck])} м³" : "");
        }
        else if (u.T.Ranged) held = u.FireArrows ? "Огненные стрелы" : "Стрелы";
        else if (u.T == Kinds.Captain) held = $"Бойцов в 40 м: {sim.Heeding(u)}";
        _held.Text = held;
        _held.Position = new Vector2(vp.X - _held.Size.X - 28, vp.Y - 84);
        // the horde's clock
        var wv = sim.Waves;
        _clock.Text = sim.Sandbox ? "" : wv.Active ? $"Волна {wv.Number} из {Waves.Total} идёт"
            : wv.Number >= Waves.Total || wv.Countdown > Waves.Prep * 2 ? "" : $"Волна {wv.Number + 1} через {Clock(wv.Countdown)}";
        _clock.Position = new Vector2((vp.X - _clock.Size.X) / 2, 18);
        // the word: the hands' refusal, or what was said here
        double now = Time.GetTicksMsec() / 1000.0;
        if (sim.Time - sim.RefusedAt < .05 && sim.Refused != _saidText) Said(sim.Refused);
        float left = (float)(2.6 - (now - _saidAt));
        _said.Text = left > 0 ? _saidText : "";
        _said.Modulate = new Color(1, 1, 1, Math.Clamp(left / .6f, 0, 1));
        _said.Position = new Vector2((vp.X - _said.Size.X) / 2, vp.Y / 2 + 62);
        // a commander's four calls, above the keys
        _calls.Visible = u.T == Kinds.Captain;
        if (_calls.Visible)
        {
            _calls.Size = new Vector2(CallBar.Width, CallBar.Height);
            _calls.Position = new Vector2((vp.X - CallBar.Width) / 2, vp.Y - 60 - CallBar.Height);
            _calls.Cap = u; _calls.Sim = sim;
            _calls.QueueRedraw();
        }
    }

    static string Clock(double s) { int t = (int)Math.Ceiling(Math.Max(0, s)); return $"{t / 60}:{t % 60:00}"; }

    /// <summary>The call a key makes for a commander: the keys 1–4, or -1.</summary>
    static int Slot(InputEventKey k) => Bindings.Is(k, Act.FpStone) ? 0 : Bindings.Is(k, Act.FpWood) ? 1 : Bindings.Is(k, Act.FpEarth) ? 2 : Bindings.Is(k, Act.FpFourth) ? 3 : -1;

    /// <summary>A commander's call: «Залп!» falls where it looks.</summary>
    public void CallOut(int n)
    {
        var sim = _m.Sim;
        if (_u == null) return;
        float ax = float.NaN, ay = float.NaN, az = float.NaN;
        // (eyes on the ground at its feet or a wall at hand: at the horde nearest it)
        if (n == 2 && sim.Aimed(_u, out float x, out float y, out float z, out var foe)
            && (foe != null || (x - _u.X) * (x - _u.X) + (z - _u.Z) * (z - _u.Z) > 100 * 100)) (ax, ay, az) = (x, y, z);
        string why = sim.Call(_u, n, ax, ay, az);
        if (why != null) { Said(why); return; }
        _calls.Flash(n);
        if (n == 2 && !float.IsNaN(ax)) _m.Rings.At(sim.NearestStand((int)ax, (int)az, 1), OrderMarks.Kind.Strike);
    }

    /// <summary>A commander's order by looking: its soldiers about strike the one it looks at, or stand where it looks.</summary>
    public void OrderByLook()
    {
        if (_u == null) return;
        if (_u.T != Kinds.Captain) { Said("приказы взглядом отдаёт командир"); return; }
        int n = _m.Sim.LookOrder(_u, out int cell, out var foe);
        if (n == 0) { Said("некого или некуда послать"); return; }
        if (foe != null) { _m.Rings.On(foe, OrderMarks.Kind.Strike); Said($"Бьют {foe.Name.ToLowerInvariant()}: бойцов {n}"); }
        else { _m.Rings.At(cell, OrderMarks.Kind.Go); Said($"Встают там: бойцов {n}"); }
    }

    /// <summary>Goes over into the one of ours it looks at, else the nearest of ours.</summary>
    public void Switch()
    {
        var sim = _m.Sim;
        if (_u == null) return;
        var next = sim.Sighted(_u, 40 * VoxelWorld.PerMetre, out _);
        if (next == null || !sim.CanPossess(next))
        {
            next = null;
            float best = float.MaxValue;
            foreach (var o in sim.Units)
            {
                if (o == _u || !sim.CanPossess(o)) continue;
                float d = (o.X - _u.X) * (o.X - _u.X) + (o.Z - _u.Z) * (o.Z - _u.Z);
                if (d < best) { best = d; next = o; }
            }
        }
        if (next == null) { Said("больше войти не в кого"); return; }
        _switching = true;
        try { Enter(next); }
        finally { _switching = false; }
    }

    /// <summary>Dying: the eyes sink to the ground and roll over on one side as the dark comes up; then it lets go.</summary>
    void Fall(float dt)
    {
        _dying -= dt;
        float t = 1 - Math.Clamp(_dying / 1.3f, 0, 1), e = t * t * (3 - 2 * t);
        _vm.Visible = false;
        _calls.Visible = false;
        _name.Text = _aim.Text = _hint.Text = _held.Text = _said.Text = "";
        _hurtMat.SetShaderParameter("strength", .9f);
        var w = _m.World;
        var eye = _eye;
        eye.Y = Mathf.Lerp(_eye.Y, w.WY(_u.Y + .3f * VoxelWorld.PerMetre), e);
        _roll = e * 1.25f;
        float pitch = Mathf.Lerp(_pitch, -.15f, e), fx = MathF.Cos(_yaw), fz = MathF.Sin(_yaw);
        var dir = new Vector3(fx * MathF.Cos(pitch), MathF.Sin(pitch), fz * MathF.Cos(pitch));
        var basis = Basis.LookingAt(dir, Vector3.Up) * new Basis(Vector3.Back, _roll);
        _cam.GlobalTransform = new Transform3D(basis, eye);
        _fade.Color = new Color(0, 0, 0, Math.Clamp((t - .2f) / .7f, 0, 1));
        if (_dying <= 0) Leave(_dyingWhy);
    }

    /// <summary>
    /// A commander's four calls in a row over the keys: each a plate with its sign and its key, darkened from the top down
    /// for as long as it has yet to wait and the seconds written on it, bright when ready, flashing as it is made.
    /// </summary>
    partial class CallBar : Control
    {
        public const float Slot = 56, Gap = 34, Width = Slot * 4 + Gap * 3, Height = Slot + 22;
        public Unit Cap;
        public Sim Sim;
        readonly double[] _flash = new double[4];
        static readonly Icons.Painter[] Signs = { Icons.Of("rally-the-troops"), Icons.Of("attached-shield"), Icons.Of("arrow-cluster"), Icons.Horn };
        static readonly Act[] Keys = { Act.FpStone, Act.FpWood, Act.FpEarth, Act.FpFourth };

        public void Flash(int n) => _flash[n] = Time.GetTicksMsec() / 1000.0;

        public override void _Draw()
        {
            if (Cap == null || Sim == null) return;
            double now = Time.GetTicksMsec() / 1000.0;
            for (int n = 0; n < 4; n++)
            {
                var r = new Rect2(n * (Slot + Gap), 0, Slot, Slot);
                double wait = Cap.Ready[n] - Sim.Time;
                bool ready = wait <= 0;
                float flash = (float)Math.Clamp(1 - (now - _flash[n]) / .5, 0, 1);
                DrawRect(r, new Color(Craft.Fill, .82f));
                Signs[n](this, r.Grow(-9), ready ? Ornament.Parchment : new Color(Ornament.Aged, .55f));
                if (!ready)
                {
                    // the dark over it, shrinking from the top as the wait runs out
                    float share = (float)Math.Clamp(wait / Core.Sim.CallRest[n], 0, 1);
                    DrawRect(new Rect2(r.Position.X, r.End.Y - r.Size.Y * share, r.Size.X, r.Size.Y * share), new Color(0, 0, 0, .55f));
                    string s = $"{Math.Ceiling(wait):0}";
                    var size = Ornament.SerifBold.GetStringSize(s, HorizontalAlignment.Left, -1, 18);
                    DrawString(Ornament.SerifBold, r.GetCenter() + new Vector2(-size.X / 2, 7), s, HorizontalAlignment.Left, -1, 18, Ornament.Parchment);
                }
                if (flash > 0) DrawRect(r, new Color(Ornament.BronzeLight, .45f * flash));
                DrawRect(r, ready ? Ornament.BronzeLight : Craft.BrassDark, false, ready ? 1.5f : 1);
                // its key in the corner and its name under it
                DrawString(Ornament.SerifBold, r.Position + new Vector2(5, 15), Bindings.Label(Keys[n]), HorizontalAlignment.Left, -1, 13, Ornament.BronzeLight);
                string name = Core.Sim.CallNames[n];
                var ns = Ornament.Serif.GetStringSize(name, HorizontalAlignment.Left, -1, 12);
                DrawString(Ornament.Serif, new Vector2(r.GetCenter().X - ns.X / 2, r.End.Y + 16), name, HorizontalAlignment.Left, -1, 12, ready ? Ornament.Parchment : Ornament.Aged);
            }
        }
    }
}
