using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The sound of a mine: picks striking under the ground, heard through it as a dull thud with a little grit after, at
/// the mark over the face, each stroke ours up there hear.
/// </summary>
public partial class Sound
{
    void MakeTunnel() => Add("knock", Knock());

    void HookTunnel(Sim sim) =>
        sim.PicksHeard += (t, x, y, z) => Play("knock", x, y, z, .25, .5f, pitch: .85f + (float)_rnd.NextDouble() * .3f);

    /// <summary>A pick striking under the ground: a low thud, damped by the earth, and a short rattle of grit.</summary>
    float[] Knock()
    {
        var s = Buf(.4f);
        var body = new Low(240); var grit = new Low(1100); var crack = new Low(500);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            float thud = MathF.Sin(2 * MathF.PI * 64 * t) * MathF.Exp(-t / .05f);
            float hit = crack.Step(Noise()) * MathF.Exp(-t / .012f);
            float rattle = grit.Step(Noise()) * MathF.Exp(-MathF.Max(0, t - .03f) / .08f) * (t > .03f ? .3f : 0);
            s[i] = body.Step(thud * 1.6f + hit * 1.2f) + rattle;
        }
        Normalise(s, .5f);
        return s;
    }
}
