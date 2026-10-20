using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>A lens as it is set at one moment of a shot: where it stands, what it looks at, and how wide it sees, in degrees.</summary>
public readonly record struct Lens(Vector3 Eye, Vector3 At, float Fov = 38)
{
    public Lens Lerp(Lens b, float k) => new(Eye.Lerp(b.Eye, k), At.Lerp(b.At, k), Mathf.Lerp(Fov, b.Fov, k));
}

/// <summary>
/// The trailer's camera: the valley seen as a film camera sees it, in perspective and with depth, where the diorama's own
/// camera looks straight along from far off. Each shot moves it along a path worked out for every moment (key lenses run
/// through with a Catmull-Rom curve and eased, an orbit, a crane, a dolly, a follow); a hand on it sways it a little, the
/// great moments shake it, and it is focused on what it looks at, the near and the far going soft. While it is in use the
/// figures are drawn fine whatever the distance, the sward grows round what it looks at as near as a voxel is large
/// there on the screen, the haze stands back beyond it, and the sounds, the embers and the shaking follow it as they
/// follow the diorama's camera.
/// </summary>
public partial class CineCam : Node3D
{
    Main _m;
    CameraAttributesPractical _attr;
    public Camera3D Cam { get; private set; }
    public bool On { get; private set; }
    /// <summary>What it looks at now, world units: what it is focused on, and what the sward grows round.</summary>
    public Vector3 Focus { get; private set; }
    /// <summary>How far the sward is grown in round what it looks at, nothing to full, by how large a voxel is there on the screen.</summary>
    public float Grow { get; private set; }
    /// <summary>How much a hand on the camera sways it: 1 a steady hand, 0 a tripod.</summary>
    public float Hand = 1;
    /// <summary>How much of what is nearer and further than what it looks at goes soft: 0 none, 1 the usual.</summary>
    public float Blur = 1;
    /// <summary>How far beyond what it looks at the air stays clear before the haze comes in, world units.</summary>
    public float Clear = 34;
    float _t;

    public void Init(Main m)
    {
        _m = m;
        _attr = new CameraAttributesPractical
        {
            DofBlurFarEnabled = true,
            DofBlurNearEnabled = true,
            DofBlurAmount = .06f,
            AutoExposureEnabled = false,
        };
        Cam = new Camera3D { Fov = 38, Near = .04f, Far = 400, Current = false, Attributes = _attr };
        AddChild(Cam);
        RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.High, true);
        RenderingServer.CameraAttributesSetDofBlurBokehShape(RenderingServer.DofBokehShape.Circle);
    }

    /// <summary>Takes over the view: the diorama's camera held, the figures kept fine, the drawn ways of the horde and the archers' cover put away.</summary>
    public void Begin()
    {
        On = true;
        Cam.MakeCurrent();
        _m.Rig.Held = true;
        _m.Units.ForceNear = true;
        _m.Atmos.Perspective = true;
        _m.Trails.Visible = false;
        _m.Cover.Visible = false;
        _m.Areas.Visible = false;
    }

    /// <summary>Gives the view back to someone's eyes (first person takes its own camera) or to the diorama.</summary>
    public void End()
    {
        On = false;
        _m.Atmos.FogShift = 0;
        _m.Atmos.Perspective = false;
    }

    /// <summary>Sets the lens for this frame.</summary>
    public void Frame(Lens l, float dt)
    {
        if (!On) return;
        _t += dt;
        var to = l.At - l.Eye;
        float dist = Math.Max(.05f, to.Length());
        var basis = Basis.LookingAt(to / dist, Vector3.Up);
        // a hand on it: a slow drift that never quite stops, a little turn, a little tilt, a little roll
        float h = Hand * .0042f;
        basis = basis * new Basis(Vector3.Up, h * Sway(_t, 0)) * new Basis(Vector3.Right, h * .8f * Sway(_t * 1.13f, 2.1f))
            * new Basis(Vector3.Forward, h * .5f * Sway(_t * .87f, 4.7f));
        // the great moments shake it, as they shake the diorama's camera
        float k = _m.Rig.Shaking * _m.Rig.Shaking * Settings.Shake;
        if (k > 1e-4f)
            basis = basis * new Basis(Vector3.Right, k * .035f * Wobble(_t, 0)) * new Basis(Vector3.Up, k * .03f * Wobble(_t, 1.3f))
                * new Basis(Vector3.Forward, k * .02f * Wobble(_t, 2.9f));
        Cam.GlobalTransform = new Transform3D(basis, l.Eye);
        Cam.Fov = l.Fov;
        Focus = l.At;

        // focused on what it looks at: what is much nearer or further goes soft, the more so the closer it looks
        _attr.DofBlurAmount = .05f * Blur;
        _attr.DofBlurFarEnabled = _attr.DofBlurNearEnabled = Blur > 0;
        _attr.DofBlurFarDistance = dist * 1.25f + 1.5f;
        _attr.DofBlurFarTransition = dist * 1.4f + 4;
        _attr.DofBlurNearDistance = dist * .55f;
        _attr.DofBlurNearTransition = dist * .4f;

        // a voxel's size on the screen where it looks: the sward grows in as the diorama's does when zoomed close
        float h1 = GetTree().Root.Size.Y, px = h1 / (2 * dist * MathF.Tan(Mathf.DegToRad(l.Fov) / 2)) * VoxelWorld.Vox;
        Grow = Mathf.SmoothStep(9, 13, px);
        // the haze stands back beyond what it looks at (the diorama's camera is some eighty units off what it looks at)
        _m.Atmos.FogShift = dist + Clear - 84;
        // the sounds, the shaking, the embers and the rain follow what it looks at; a near look hears near
        float yaw = MathF.Atan2(-to.X, -to.Z), pitch = Math.Clamp(MathF.Asin(Math.Clamp(-to.Y / dist, -1, 1)), .35f, 1.5f);
        _m.Rig.Set(l.At.X, l.At.Z, yaw, pitch, Math.Clamp(18 / dist, .4f, 6f), snap: true);
    }

    /// <summary>A sway: two slow waves over one another, about −1 to 1.</summary>
    static float Sway(float t, float seed) => MathF.Sin(t * .61f + seed) * .6f + MathF.Sin(t * 1.37f + seed * 1.9f) * .4f;

    /// <summary>A shaking: three tremors over one another, from a sway to a rattle.</summary>
    static float Wobble(float t, float seed) => MathF.Sin(t * 31 + seed) * .55f + MathF.Sin(t * 47 + seed * 2.3f) * .3f + MathF.Sin(t * 83 + seed * 4.1f) * .15f;

    // ---------------------------------------------------------------- moves

    /// <summary>Smootherstep: a move that sets off and comes to rest gently.</summary>
    public static float Ease(float k) { k = Math.Clamp(k, 0, 1); return k * k * k * (k * (k * 6 - 15) + 10); }

    /// <summary>Eases only the start (a move that sets off gently and is still going at the cut), or only the end (one cut into as it goes, coming to rest).</summary>
    public static float EaseIn(float k) { k = Math.Clamp(k, 0, 1); return k * k * (2 - k); }
    public static float EaseOut(float k) => 1 - EaseIn(1 - k);

    /// <summary>
    /// Through key lenses at their times: a Catmull-Rom curve through the eyes and through what they look at, the width of
    /// the view eased from key to key.
    /// </summary>
    public static Lens Keys(float t, params (float t, Lens l)[] keys)
    {
        if (keys.Length == 0) return default;
        if (t <= keys[0].t) return keys[0].l;
        int n = keys.Length - 1;
        if (t >= keys[n].t) return keys[n].l;
        int i = 0;
        while (i < n - 1 && t > keys[i + 1].t) i++;
        float k = (t - keys[i].t) / Math.Max(1e-4f, keys[i + 1].t - keys[i].t);
        var a = keys[Math.Max(0, i - 1)].l; var b = keys[i].l; var c = keys[i + 1].l; var d = keys[Math.Min(n, i + 2)].l;
        return new Lens(CatmullRom(a.Eye, b.Eye, c.Eye, d.Eye, k), CatmullRom(a.At, b.At, c.At, d.At, k), Mathf.Lerp(b.Fov, c.Fov, Ease(k)));
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return .5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
    }

    /// <summary>
    /// Round a point: from one bearing to another (radians, 0 looking north from the south), the distance and the height
    /// going from the first to the second as it goes, k from 0 to 1.
    /// </summary>
    public static Lens Orbit(Vector3 centre, float k, float a0, float a1, float r0, float r1, float h0, float h1, float fov = 38, Vector3 lookUp = default)
    {
        float a = Mathf.Lerp(a0, a1, k), r = Mathf.Lerp(r0, r1, k), h = Mathf.Lerp(h0, h1, k);
        var eye = centre + new Vector3(MathF.Sin(a) * r, h, MathF.Cos(a) * r);
        return new Lens(eye, centre + lookUp, fov);
    }

    /// <summary>Along a straight line, looking at a point that moves along its own (a dolly, a crane, a push in).</summary>
    public static Lens Dolly(float k, Vector3 eye0, Vector3 eye1, Vector3 at0, Vector3 at1, float fov0 = 38, float fov1 = 38) =>
        new(eye0.Lerp(eye1, k), at0.Lerp(at1, k), Mathf.Lerp(fov0, fov1, k));
}
