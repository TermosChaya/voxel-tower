using System;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The voice of one of ours calling a warning (Sim.Felling.cs, Sim.Undermine.cs): «Бе-ре-гись!» before a tree goes over
/// or what is built comes down, three quick syllables shouted, the last higher and held, with its hiss at the end.
/// </summary>
public partial class Sound
{
    void MakeShout() => Add("warn", Warn());

    void HookShout(Sim sim)
    {
        sim.Shouted += (u, _) => Play("warn", u.X, u.Y + 12, u.Z, 1.2, 1.1f, pitch: .92f + (u.Id % 5) * .05f);
    }

    /// <summary>
    /// A shout of three syllables: a buzz at a man's raised pitch through the formants of «е», «е» and «и», each begun
    /// by its stop or tap (a dip in the voice), the last rising and held, then «сь» as a breath of high noise.
    /// </summary>
    float[] Warn()
    {
        var s = Buf(1f);
        var e1 = new Ring(530, 6); var e2 = new Ring(1850, 8); var e3 = new Ring(2600, 9);
        var i1 = new Ring(310, 5); var i2 = new Ring(2300, 8); var i3 = new Ring(3100, 9);
        var hiss = new Ring(5200, 3); var lo = new Low(4200);
        // each syllable: when it begins and ends, its pitch, how loud, and how much «и» is in its vowel
        (float a, float b, float pitch, float loud, float i)[] syl = { (0f, .15f, 215, .8f, 0), (.17f, .32f, 228, .85f, 0), (.35f, .74f, 262, 1f, 1) };
        float phase = 0;
        for (int n = 0; n < s.Length; n++)
        {
            float t = n / (float)Rate, env = 0, ii = 0, pitch = 220;
            foreach (var (a, b, p, loud, i) in syl)
            {
                if (t < a || t > b) continue;
                float k = (t - a) / (b - a);
                // the stop or tap that opens it: a moment's dip, then the vowel full
                env = loud * Math.Min(1, (t - a) / .025f) * (k > .8f && i == 0 ? (1 - k) / .2f : 1) * (i > 0 ? (k < .7f ? 1 : MathF.Exp(-(k - .7f) / .12f)) : 1);
                pitch = p + (i > 0 ? 18 * MathF.Sin(MathF.Min(1, k * 1.6f) * MathF.PI * .5f) - 30 * Math.Max(0, k - .6f) : 0);
                ii = i;
            }
            phase += (pitch + 5 * MathF.Sin(t * 31)) / Rate;
            phase -= MathF.Floor(phase);
            float buzz = phase * 2 - 1 + Noise() * .3f;
            float ve = e1.Step(buzz) * 2.2f + e2.Step(buzz) * 1.3f + e3.Step(buzz) * .5f;
            float vi = i1.Step(buzz) * 2.4f + i2.Step(buzz) * 1.1f + i3.Step(buzz) * .5f;
            float voice = (ve * (1 - ii) + vi * ii) * env;
            // «сь»: the breath through the teeth after the vowel
            float h = t > .66f && t < .9f ? MathF.Min(1, (t - .66f) / .05f) * MathF.Exp(-Math.Max(0, t - .72f) / .07f) : 0;
            s[n] = lo.Step(voice) + hiss.Step(Noise()) * h * 1.6f;
        }
        Normalise(s, .6f);
        return s;
    }
}
