using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// Our engines at their work, by how far they are through it since they last loosed (the simulation's winding,
/// <see cref="Unit.Cooldown"/>, against what it was at the shot): a trebuchet's beam swinging up and over with the sling
/// whipping round after it and the counterweight dropping between the frames and rocking, then hauled down again, the
/// sling laid back in its trough and a stone put in it; a stone-thrower's arm slamming up against its padded beam,
/// wound down by the windlass, a stone or a keg alight put in its spoon; a ballista's strings snapping forward, drawn
/// back to the claw by the windlass, a bolt laid in the groove. Packed up for the road they lie still and empty. Their
/// crews wind, haul and lift with them, and knock the pins home as they set them up. Each shot kicks up dust about the
/// engine and leaves a faint trail behind what it throws.
/// </summary>
public partial class UnitView
{
    /// <summary>The winding each engine of ours was given at its last shot, seconds (its <see cref="Unit.Cooldown"/> then).</summary>
    readonly Dictionary<int, float> _winding = new();

    const float TrebCocked = -.95f, SlingLaid = 1.25f, ArmStop = 1.9f;

    void EnginesInit(Sim sim)
    {
        sim.EngineLoosed += u => _winding[u.Id] = Math.Max(.5f, u.Cooldown);
        var quad = new QuadMesh { Size = Vector2.One };
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = quad };
        _wake = new MultiMeshInstance3D
        {
            Multimesh = mm, MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/smoke.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, CustomAabb = Everywhere,
        };
        AddChild(_wake);
    }

    // ---------------------------------------------------------------- what they throw, and the trail it leaves

    struct Wisp { public Vector3 P; public float Age, Life, Size, Seed; public Color C; }

    MultiMeshInstance3D _wake;
    readonly List<Wisp> _wisps = new();
    float[] _wispBuf = Array.Empty<float>();
    float _wispDue;
    const int MaxWisps = 900, WispStride = 20;

    /// <summary>
    /// A faint trail behind what our engines throw, so it is followed across the sky at the usual height of the view: a
    /// pale dust off a stone, smoke and a glow off a keg alight, a thin grey line behind a bolt. Puffs a little bigger
    /// the farther the view, so they are not lost.
    /// </summary>
    void DrawTrails(Sim sim, float dt)
    {
        var w = _w;
        // a puff about every hundredth of a second behind each, the more of them the faster the game goes
        _wispDue += dt * 90;
        int due = (int)_wispDue;
        _wispDue -= due;
        float far = Math.Clamp(_m.Rig.Cam.Size / 14f, .6f, 2.2f);
        if (due > 0 && dt > 0)
        {
            // (laid back along the way it came this frame, so that a slow frame leaves a line, not a string of beads)
            float game = dt * _m.Speed;
            foreach (var f in sim.Fliers)
                if (f.Cause is Sim.MangonelCause or Sim.TrebuchetCause && f.Carrier == null && !f.Rolling)
                    for (int i = 0; i < due; i++)
                    {
                        float back = game * i / due;
                        Puff(new Vector3(w.WX(f.X - f.VX * back), w.WY(f.Y - f.VY * back), w.WZ(f.Z - f.VZ * back)), .2f * far, .9f, new Color(.8f, .76f, .68f, .2f));
                    }
            foreach (var a in sim.Arrows)
            {
                if (a.Done || a.From == null || !Kinds.IsOurEngine(a.From.T)) continue;
                var p0 = new Vector3(a.X0, a.Y0, a.Z0); var p1 = new Vector3(a.X1, a.Y1, a.Z1);
                float h = 4 * p0.DistanceTo(p1) * a.Arc;
                for (int i = 0; i < due; i++)
                {
                    float k = Math.Clamp((a.T - game * i / due) / Math.Max(a.Dur, 1e-3f), 0, 1);
                    var p = p0.Lerp(p1, k) + Vector3.Up * h * k * (1 - k);
                    var at = new Vector3(w.WX(p.X), w.WY(p.Y), w.WZ(p.Z));
                    if (a.Kind == Shot.Keg) Puff(at, .2f * far, 1.1f, i % 3 == 0 ? new Color(1f, .55f, .18f, .45f) : new Color(.2f, .18f, .17f, .3f));
                    else Puff(at, .08f * far, .4f, new Color(.72f, .72f, .72f, .22f));
                }
            }
        }
        GrowWisps(ref _wispBuf, _wisps.Count);
        int n = 0;
        for (int i = _wisps.Count - 1; i >= 0; i--)
        {
            var q = _wisps[i];
            q.Age += dt;
            if (q.Age >= q.Life) { _wisps.RemoveAt(i); continue; }
            _wisps[i] = q;
            float f = q.Age / q.Life, size = q.Size * (1 + f * 1.6f), alpha = q.C.A * (1 - f);
            int o = n++ * WispStride;
            var b = _wispBuf;
            b[o] = size; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = q.P.X;
            b[o + 4] = 0; b[o + 5] = size; b[o + 6] = 0; b[o + 7] = q.P.Y + f * .08f;
            b[o + 8] = 0; b[o + 9] = 0; b[o + 10] = size; b[o + 11] = q.P.Z;
            b[o + 12] = q.C.R; b[o + 13] = q.C.G; b[o + 14] = q.C.B; b[o + 15] = alpha;
            b[o + 16] = q.Seed; b[o + 17] = 0; b[o + 18] = 0; b[o + 19] = 0;
        }
        if (n > 0) RenderingServer.MultimeshSetBuffer(_wake.Multimesh.GetRid(), _wispBuf);
        _wake.Multimesh.VisibleInstanceCount = n;
    }

    void Puff(Vector3 at, float size, float life, Color c)
    {
        if (_wisps.Count >= MaxWisps) return;
        float j = (float)(_wisps.Count * .618 % 1);
        _wisps.Add(new Wisp { P = at + new Vector3(j - .5f, (j * 7 % 1) - .5f, (j * 13 % 1) - .5f) * size * .6f, Size = size, Life = life, Seed = j, C = c });
    }

    void GrowWisps(ref float[] buf, int need)
    {
        var mm = _wake.Multimesh;
        if (mm.InstanceCount >= need) return;
        mm.InstanceCount = Math.Max(64, need * 2);
        mm.VisibleInstanceCount = 0;
        buf = new float[mm.InstanceCount * WispStride];
    }

    /// <summary>Seconds since an engine of ours last loosed (infinity: it stands ready), and the whole of its winding.</summary>
    (float since, float whole) EngineTime(Unit u)
    {
        float whole = _winding.TryGetValue(u.Id, out float w) ? w : u.T.AttackTime;
        return (u.Cooldown > 0 ? Math.Max(0, whole - u.Cooldown) : float.PositiveInfinity, whole);
    }

    /// <summary>A rotation about the figure's x (across the engine) at a point.</summary>
    static Transform3D About(Vector3 at, float angle) => RotAt(at, Vector3.Right, angle);

    /// <summary>A piece hung from a point that has moved: where it hangs now, turned so far from hanging as it was sculpted.</summary>
    static Transform3D HungFrom(Vector3 rest, Vector3 now, float angle)
    {
        var b = new Basis(Vector3.Right, angle);
        return new Transform3D(b, now - b * rest);
    }

    /// <summary>Writes the moving pieces of one of our engines over the plain pose: the body is already in place.</summary>
    void OurEngine(Unit u, Figure f, Transform3D body)
    {
        var (since, whole) = EngineTime(u);
        float rig = u.Dead ? 0 : u.Rig < 0 ? 1 : Math.Clamp(u.Rig, 0, 1);
        if (u.T == Kinds.Trebuchet) Trebuchet(u, f, body, since, whole, rig);
        else if (u.T == Kinds.Mangonel) Mangonel(u, f, body, since, whole, rig);
        else if (u.T == Kinds.Ballista) Ballista(u, f, body, since, whole, rig);
    }

    void Trebuchet(Unit u, Figure f, Transform3D body, float since, float whole, float rig)
    {
        float beam, sling, swing = 0, t = (float)_t + u.Id * .37f;
        bool stone;
        if (rig < 1 || u.Dead) { beam = TrebCocked; sling = SlingLaid; stone = false; }
        else if (since < .45f)
        {
            // loosed: the beam comes up and over, faster and faster, the sling whipping round behind it
            float k = since / .45f;
            beam = TrebCocked + (2.3f - TrebCocked) * k * k;
            sling = SlingLaid + (-2.6f - SlingLaid) * MathF.Pow(k, 1.5f);
            stone = false;
        }
        else if (since < 4)
        {
            // over the top, it falls back and rocks to rest with the counterweight hanging low between the frames, the
            // empty sling going on round and hanging down
            float s = since - .45f;
            beam = MathF.PI / 2 + (2.3f - MathF.PI / 2) * MathF.Exp(-s / 1.2f) * MathF.Cos(MathF.Tau * s / 1.8f);
            sling = -MathF.Tau + (MathF.Tau - 2.6f) * MathF.Exp(-s / .5f) * MathF.Cos(MathF.Tau * s / 1.2f);
            swing = .45f * MathF.Exp(-s) * MathF.Sin(MathF.Tau * s / 1.4f);
            stone = false;
        }
        else if (since < whole - 2)
        {
            // hauled down by the windlass, the counterweight rising; at the bottom the sling is laid back in its trough
            float k = Smooth((since - 4) / Math.Max(.1f, whole - 6));
            beam = MathF.PI / 2 + (TrebCocked - MathF.PI / 2) * k;
            sling = k < .85f ? 0 : SlingLaid * Smooth((k - .85f) / .15f);
            swing = .05f * MathF.Sin(t * 1.3f);
            stone = false;
        }
        else
        {
            // wound down: a stone in the pouch, waiting for the word
            beam = TrebCocked; sling = SlingLaid; swing = .015f * MathF.Sin(t * .9f);
            stone = since >= whole - 1.5f;
        }
        var jBeam = f.Joint[(int)Part.Main];
        var b = About(jBeam, beam);
        var jHinge = f.Joint[(int)Part.Off];
        var jHook = f.Joint[(int)Part.LegR];
        var sl = HungFrom(jHook, b * jHook, sling);
        _xf[(int)Bone.UpperR] = body * b;
        _xf[(int)Bone.UpperL] = body * HungFrom(jHinge, b * jHinge, swing);
        _xf[(int)Bone.ThighR] = body * sl;
        _xf[(int)Bone.ThighL] = stone ? body * sl : Folded;
    }

    void Mangonel(Unit u, Figure f, Transform3D body, float since, float whole, float rig)
    {
        float arm, wind = 0;
        bool loaded;
        if (rig < 1 || u.Dead) { arm = 0; loaded = false; }
        else if (since < .14f)
        {
            // loosed: the skein flings the arm up against the padded beam
            float k = since / .14f;
            arm = ArmStop * k * k; loaded = false;
        }
        else if (since < .8f)
        {
            // it strikes the beam and jars back off it a little
            float s = since - .14f;
            arm = ArmStop - .14f * MathF.Exp(-s / .12f) * MathF.Abs(MathF.Sin(s * 22)); loaded = false;
        }
        else if (since < whole - 1.2f)
        {
            // wound down by the windlass, its wheels going round
            float k = Smooth((since - .8f) / Math.Max(.1f, whole - 2f));
            arm = ArmStop * (1 - k); wind = -k * 7 * MathF.PI; loaded = false;
        }
        else { arm = 0; wind = -7 * MathF.PI; loaded = since >= whole - .9f; }
        bool keg = _m.Sim.Knows("pitchkegs") && (u.Strokes & 1) == 0;
        var a = About(f.Joint[(int)Part.Main], arm);
        _xf[(int)Bone.UpperR] = body * a;
        _xf[(int)Bone.UpperL] = body * About(f.Joint[(int)Part.Off], wind);
        _xf[(int)Bone.ThighL] = loaded && !keg ? body * a : Folded;
        _xf[(int)Bone.ThighR] = loaded && keg ? body * a : Folded;
    }

    void Ballista(Unit u, Figure f, Transform3D body, float since, float whole, float rig)
    {
        // how far the strings are drawn back (1 to the claw, 0 straight across between the arms' tips)
        float drawn;
        bool bolt;
        if (rig < 1 || u.Dead) { drawn = 0; bolt = false; }
        else if (since < .08f) { drawn = 1 - since / .08f; bolt = false; }
        else if (since < .5f) { drawn = 0; bolt = false; }
        else if (since < whole - .8f) { drawn = Smooth((since - .5f) / Math.Max(.1f, whole - 1.3f)); bolt = false; }
        else { drawn = 1; bolt = since >= whole - .6f; }
        // the stock kicks as it looses
        float kick = since < .5f ? -.08f * MathF.Exp(-since / .08f) : 0;
        var k = About(f.Joint[(int)Part.Main], kick);
        var claw = f.Joint[(int)Part.LegL];
        _xf[(int)Bone.UpperR] = body * k;
        _xf[(int)Bone.UpperL] = body * k * String(f.Joint[(int)Part.Off], claw, drawn);
        _xf[(int)Bone.ThighR] = body * k * String(f.Joint[(int)Part.LegR], claw, drawn);
        _xf[(int)Bone.ThighL] = bolt ? body * k : Folded;
    }

    /// <summary>
    /// A string sculpted drawn, from an arm's tip back to the claw, turned about the tip and shortened so that it runs
    /// to a point between the claw (drawn 1) and straight across before the stock's head (drawn 0).
    /// </summary>
    static Transform3D String(Vector3 tip, Vector3 claw, float drawn)
    {
        var across = new Vector3(0, tip.Y, tip.Z);
        var to = across.Lerp(claw, drawn);
        var d0 = claw - tip; var d1 = to - tip;
        d0.Y = 0; d1.Y = 0;
        float l0 = d0.Length(), l1 = Math.Max(.05f, d1.Length());
        if (l0 < 1e-3f) return Transform3D.Identity;
        var a0 = d0 / l0;
        float r = l1 / l0;
        // shorten along the string as it was sculpted, then turn it about the tip to where it runs now
        var shorten = new Basis(Vector3.Right + (r - 1) * a0 * a0.X, Vector3.Up, Vector3.Back + (r - 1) * a0 * a0.Z);
        float turn = MathF.Atan2(d0.X, d0.Z) - MathF.Atan2(d1.X, d1.Z);
        var m = new Basis(Vector3.Up, -turn) * shorten;
        return new Transform3D(m, tip - m * tip);
    }

    /// <summary>
    /// One of an engine's crew at its work, by where the engine is in its own: knocking the pins home as it is set up
    /// or taken down, stooping for the shot and heaving it up as it is loaded, else both hands on a windlass's spokes
    /// going round.
    /// </summary>
    void Crewing(Unit u, Unit e, Figure f, float seed, ref float main, ref float off, ref float lean, ref float elbowR, ref float elbowL, ref float kneeR, ref float kneeL)
    {
        var (since, whole) = EngineTime(e);
        float t = (float)_t + seed;
        if (e.Rig < 1)
        {
            Chop(f.Chop * 1.2f, t, out main, out lean);
            off = -.35f; elbowR = -.12f; elbowL = -.9f; kneeR = kneeL = .2f;
            return;
        }
        if (since >= whole - 2 && since < whole - .6f)
        {
            float up = MathF.Sin(Math.Clamp((since - (whole - 2)) / 1.4f, 0, 1) * MathF.PI);
            main = off = -.5f - 1.4f * up;
            lean = .6f - .5f * up;
            elbowR = elbowL = -.6f + .3f * up;
            kneeR = kneeL = .55f - .35f * up;
            return;
        }
        float a = t * 3.4f;
        main = -1.3f + .5f * MathF.Sin(a);
        off = -1.3f + .5f * MathF.Sin(a + MathF.PI);
        elbowR = -.55f - .4f * MathF.Cos(a);
        elbowL = -.55f - .4f * MathF.Cos(a + MathF.PI);
        lean = .2f + .07f * MathF.Sin(a * 2);
        kneeR = kneeL = .22f;
    }
}
