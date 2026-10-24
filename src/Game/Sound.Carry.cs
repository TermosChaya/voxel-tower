using System;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The voice of one of ours pinned under what came down (Sim.Trauma.cs): a hoarse «а-а!» calling for help, the pitch
/// falling, now and then while nobody is digging it out.
/// </summary>
public partial class Sound
{
    void MakeCarry() => Add("help", Call());

    void HookCarry(Sim sim)
    {
        sim.CalledOut += u => Play("help", u.X, u.Y + 4, u.Z, 1.5, .7f, pitch: .9f + (u.Id % 5) * .06f);
    }

    /// <summary>A call: a buzz at a voice's pitch, falling and wavering, through the two formants of an open «а», with breath in it.</summary>
    float[] Call()
    {
        var s = Buf(.75f);
        var f1 = new Ring(760, 6); var f2 = new Ring(1180, 7); var f3 = new Ring(2500, 9); var lo = new Low(3800);
        float phase = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            // the voice: up quick to its pitch, held, falling away at the end, a tremble in it
            float pitch = 300 - 90 * Math.Clamp((t - .3f) / .4f, 0, 1) + 6 * MathF.Sin(t * 34);
            phase += pitch / Rate;
            phase -= MathF.Floor(phase);
            float buzz = phase * 2 - 1 + Noise() * .35f;
            float env = Math.Min(1, t / .05f) * (t < .45f ? 1 : MathF.Exp(-(t - .45f) / .12f));
            float v = f1.Step(buzz) * 2.2f + f2.Step(buzz) * 1.4f + f3.Step(buzz) * .5f;
            s[i] = lo.Step(v) * env;
        }
        Normalise(s, .55f);
        return s;
    }
}
