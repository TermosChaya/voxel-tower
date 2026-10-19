using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The great moments made to be felt. A blast, a great fall, a heavy piece of the world coming down, a bolt out of the
/// sky, the earth shaken by a spell shake the view, the harder the greater they are and the nearer to what it looks at; a great blast or bolt near it
/// flashes the screen, and the greatest slow the game's time for a moment, to see the pieces go, before it comes back
/// by itself. The shaking and the flashes, and the slowing, are as the player set them.
/// </summary>
public partial class Spectacle : Node
{
    Main _m;
    ColorRect _flash;
    float _flashA;
    double _now, _slowFrom = -99;

    public void Init(Main m)
    {
        _m = m;
        var layer = new CanvasLayer { Layer = 40 };
        AddChild(layer);
        _flash = new ColorRect { Color = new Color(1, .86f, .64f, 0), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_flash);
        var sim = m.Sim;
        sim.Exploded += (x, y, z, r) => Felt(x, z, Math.Clamp(r / (2.5f * VoxelWorld.PerMetre), .35f, 1.3f), flash: true);
        sim.Collapsed += (n, x, y, z) => { if (n >= 200) Felt(x, z, Math.Clamp(n / 2500f, .08f, 1f), flash: false); };
        sim.Landed += (x, y, z, n, speed) =>
        {
            // the heavier and the harder it comes down, the more it shakes the ground
            float k = MathF.Sqrt(n) * speed / VoxelWorld.PerMetre / 350;
            if (k > .06f) Felt(x, z, Math.Min(.8f, k), flash: false);
        };
        sim.Struck += (x, y, z) => Felt(x, z, .6f, flash: true);
        sim.Quaked += (x, z, r) => Felt(x, z, .9f, flash: false);
    }

    /// <summary>How bright the screen's flash is now, nothing to a third.</summary>
    public float FlashNow => _flashA;

    /// <summary>How near a spot of the valley is to what the view looks at, as far as the view goes: 1 in its middle,
    /// half toward its edges, little beyond them.</summary>
    float Near(float x, float z)
    {
        var w = _m.World;
        var c = _m.Rig.Target;
        float dx = w.WX(x) - c.X, dz = w.WZ(z) - c.Y, view = MathF.Max(4, _m.Rig.Cam.Size * .6f);
        return 1 / (1 + (dx * dx + dz * dz) / (view * view));
    }

    /// <summary>Something great happened at a spot (voxels): shaken, flashed, and the time slowed, by how great and how near.</summary>
    public void Felt(float x, float z, float great, bool flash)
    {
        float k = great * Near(x, z);
        _m.Rig.Shake(k * .9f);
        if (flash && k > .25f && Settings.Shake > 0) _flashA = Math.Max(_flashA, Math.Min(.35f, k * .3f));
        // the greatest, near what the view looks at: the time slows for a moment (not again too soon)
        if (k > .6f && Settings.SlowMo && _now - _slowFrom > 6) _slowFrom = _now;
    }

    public override void _Process(double delta)
    {
        _now += delta;
        double t = _now - _slowFrom;
        // held slow a quarter of a second, then let back to the full pace over a second
        _m.Slow = t < .25 ? .3f : t < 1.25 ? (float)(.3 + .7 * Smooth((t - .25) / 1.0)) : 1;
        if (_flashA <= 0 && !_flash.Visible) return;
        _flashA = Math.Max(0, _flashA - (float)delta * 2.2f);
        _flash.Color = new Color(_flash.Color, _flashA);
        _flash.Visible = _flashA > 0;
    }

    static double Smooth(double k) => k * k * (3 - 2 * k);
}
