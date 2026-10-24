using System;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The sounds of weight: masonry grinding and cracking under a load, timber creaking, a voxel crumbling out, a
/// building beginning to go over (a deep grind and the rumble after it), and a piece coming down into water.
/// </summary>
public partial class Sound
{
    void MakeCollapse()
    {
        Add("grind", Grind());
        Add("creak", Creak());
        Add("splash", Splash());
    }

    void HookCollapse(Sim sim)
    {
        sim.Crumbled += (x, y, z, m) => Play(Mat.IsMasonry(m) ? "grind" : "creak", x + .5f, y, z + .5f, .3, .55f, pitch: 1.15f);
        sim.GoingOver += f =>
        {
            Play("grind", f.X, f.Y, f.Z, 0, 1.3f, far: true, pitch: .55f);
            Play("rumble", f.X, f.Y, f.Z, 0, Math.Clamp(.5f + f.Voxels.Count / 8000f, .5f, 1.2f), far: true, pitch: .8f);
        };
        sim.Plunged += (x, y, z, n, speed) => Play("splash", x, y, z, .08, Math.Clamp(.3f + n / 600f, .3f, 1.2f), pitch: n > 800 ? .7f : 0);
    }

    /// <summary>What carries more than it bears, cracking: masonry's grind, or timber's creak.</summary>
    public void Strain(float x, float y, float z, bool stone, float loud) => Play(stone ? "grind" : "creak", x, y, z, .8, loud, pitch: stone ? .8f : 0);

    /// <summary>Stone moving on stone under a load: a low gritty grinding, sharp cracks snapping through it now and then.</summary>
    float[] Grind()
    {
        var s = Buf(1f);
        var lo = new Low(300); var hi = new Low(2400);
        var ring = new Ring(950, 5);
        float brown = 0, amp = 0;
        int snapAt = -9999, next = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            brown = Math.Clamp(brown + Noise() * .08f, -1, 1);
            float env = MathF.Sin(MathF.PI * Math.Min(1, t / 1f));
            if (i >= next) { snapAt = i; next = i + (int)((.04 + _rnd.NextDouble() * .16) * Rate); amp = .4f + (float)_rnd.NextDouble() * .8f; }
            float k = (i - snapAt) / (.01f * Rate), snap = k < 1 ? Noise() * amp * (1 - k) : 0;
            s[i] = lo.Step(brown) * 2.2f * env + ring.Step(snap) * 2.5f + hi.Step(snap) * .6f;
        }
        Normalise(s, .8f);
        return s;
    }

    /// <summary>Timber under a load: the stick and slip of a groaning creak, its pitch wandering.</summary>
    float[] Creak()
    {
        var s = Buf(.85f);
        var r1 = new Ring(430, 9); var r2 = new Ring(1150, 12);
        float ph = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            float f = 105 + 38 * MathF.Sin(MathF.Tau * 1.3f * t) + 17 * MathF.Sin(MathF.Tau * 3.4f * t);
            ph += f / Rate;
            float stick = ph - MathF.Floor(ph) < .14f ? .6f + .4f * Noise() : 0;
            float env = MathF.Pow(MathF.Sin(MathF.PI * t / .85f), .7f);
            float x = stick * env;
            s[i] = r1.Step(x) * 2 + r2.Step(x) + x * .15f;
        }
        Normalise(s, .7f);
        return s;
    }

    /// <summary>Something heavy into water: a deep plop, the wash of it, and the hiss of the spray coming down.</summary>
    float[] Splash()
    {
        var s = Buf(1f);
        var lo = new Low(800); var hi = new Low(4200);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            float plop = MathF.Sin(MathF.Tau * (240 - 170 * t) * t) * Decay(i, .05f);
            float wash = lo.Step(Noise()) * Decay(i, .14f) * 2.2f;
            float hiss = hi.Step(Noise()) * Decay(i, .3f) * MathF.Min(1, t / .08f);
            s[i] = plop * .8f + wash + hiss * .5f;
        }
        Normalise(s, .8f);
        return s;
    }
}
