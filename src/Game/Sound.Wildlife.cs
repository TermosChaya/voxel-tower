using System;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The small life's sounds (src/Core/Ambience.cs) and the town's beasts': a sparrow's chirp, two quick whistles rising;
/// a pigeon's soft «гу-у»; a crow's harsh «кар»; a flock going up, a rush of wingbeats; a frog's croak, a rattle at a low
/// pitch; a fish, a small plop; a hen's cluck; a dog's bark, a rough «гав» from the chest; a horse's whinny, a voice
/// shaking down; a goat's bleat, a voice shaking at one pitch. Each a little higher or lower each time.
/// </summary>
public partial class Sound
{
    void MakeWildlife()
    {
        Add("chirp", Chirp());
        Add("coo", Coo());
        Add("caw", Caw());
        Add("flap", Flap());
        Add("croak", Croak());
        Add("plop", Plop());
        Add("cluck", Cluck());
        Add("bark", Bark());
        Add("neigh", Neigh());
        Add("bleat", Bleat());
    }

    /// <summary>The small life's calls, flocks going up and fish jumping, heard where they are.</summary>
    public void HookWildlife(Ambience a)
    {
        a.TookOff += (k, x, y, z, n) => Play("flap", x, y, z, .25, MathF.Min(.9f, .25f + n * .07f), pitch: k == Critter.Crow ? .7f : k == Critter.Pigeon ? .85f : 1.1f);
        a.Called += (k, x, y, z) =>
        {
            float p = .9f + (float)_rnd.NextDouble() * .25f;
            switch (k)
            {
                case Critter.Sparrow: Play("chirp", x, y, z, .12, .32f, pitch: p * 1.05f); break;
                case Critter.Pigeon: Play("coo", x, y, z, .4, .3f, pitch: p); break;
                case Critter.Crow: Play("caw", x, y, z, .3, .55f, far: true, pitch: p * .95f); break;
                case Critter.Frog: Play("croak", x, y, z, .08, .4f, pitch: .8f + (float)_rnd.NextDouble() * .5f); break;
            }
        };
        a.Splashed += (x, y, z) => Play("plop", x, y, z, .15, .35f, pitch: .9f + (float)_rnd.NextDouble() * .3f);
    }

    /// <summary>Two quick whistles, each sliding up, the second higher.</summary>
    float[] Chirp()
    {
        var s = Buf(.22f);
        float ph = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            int n = t < .08f ? 0 : t > .1f && t < .2f ? 1 : -1;
            if (n < 0) continue;
            float u = n == 0 ? t / .08f : (t - .1f) / .1f;
            float f = (n == 0 ? 3600 : 4200) + 1400 * u;
            ph += MathF.Tau * f / Rate;
            s[i] = MathF.Sin(ph) * MathF.Sin(MathF.PI * u);
        }
        Normalise(s, .45f);
        return s;
    }

    /// <summary>A soft low «гу-у»: a hollow tone with a dip in the middle.</summary>
    float[] Coo()
    {
        var s = Buf(.75f);
        float ph = 0;
        var lo = new Low(900);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, u = t / .75f;
            float f = 420 + 60 * MathF.Sin(MathF.PI * u) - (u > .35f && u < .45f ? 80 : 0);
            ph += MathF.Tau * f / Rate;
            float env = MathF.Sin(MathF.PI * MathF.Min(1, u * 1.3f)) * (u > .4f && u < .47f ? .3f : 1);
            s[i] = lo.Step(MathF.Sin(ph) + .3f * MathF.Sin(2 * ph)) * env;
        }
        Normalise(s, .5f);
        return s;
    }

    /// <summary>A crow's «кар»: a harsh buzz through a nasal ring, falling a little.</summary>
    float[] Caw()
    {
        var s = Buf(.38f);
        var r1 = new Ring(1300, 4); var r2 = new Ring(2600, 6);
        float ph = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, u = t / .38f;
            ph += MathF.Tau * (560 - 160 * u) / Rate;
            float saw = 2 * (ph / MathF.Tau - MathF.Floor(ph / MathF.Tau + .5f)) + Noise() * .5f;
            float env = MathF.Min(1, t / .02f) * MathF.Pow(1 - u, .6f);
            s[i] = (r1.Step(saw) * 2 + r2.Step(saw)) * env;
        }
        Normalise(s, .6f);
        return s;
    }

    /// <summary>A flock going up: a quick rush of soft beats, each a puff of breath through feathers.</summary>
    float[] Flap()
    {
        var s = Buf(.6f);
        var lo = new Low(1800);
        for (int b = 0; b < 9; b++)
        {
            int at = (int)((b * .055f + _rnd.NextDouble() * .02) * Rate);
            float loud = 1 - b / 11f;
            for (int i = 0; i < (int)(.05f * Rate) && at + i < s.Length; i++)
                s[at + i] += lo.Step(Noise()) * MathF.Sin(MathF.PI * i / (.05f * Rate)) * loud;
        }
        Normalise(s, .55f);
        return s;
    }

    /// <summary>A frog: a low rattle, a dozen quick pulses through a hollow ring.</summary>
    float[] Croak()
    {
        var s = Buf(.35f);
        var r = new Ring(420, 5); var r2 = new Ring(900, 7);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            float pulse = MathF.Max(0, MathF.Sin(MathF.Tau * 34 * t)), env = MathF.Sin(MathF.PI * t / .35f);
            float x = (pulse * pulse * pulse + Noise() * .15f) * env;
            s[i] = r.Step(x) * 3 + r2.Step(x);
        }
        Normalise(s, .55f);
        return s;
    }

    /// <summary>A fish breaking the water: a short plop, a drop's ring falling quick.</summary>
    float[] Plop()
    {
        var s = Buf(.18f);
        float ph = 0;
        var lo = new Low(3000);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            ph += MathF.Tau * (900 - 2600 * t) / Rate;
            s[i] = MathF.Sin(ph) * Decay(i, .035f) + lo.Step(Noise()) * .4f * Decay(i, .012f);
        }
        Normalise(s, .45f);
        return s;
    }

    /// <summary>A hen's cluck: three short knocks of the throat, the last higher.</summary>
    float[] Cluck()
    {
        var s = Buf(.45f);
        var r = new Ring(780, 6);
        for (int k = 0; k < 3; k++)
        {
            int at = (int)((k * .12f) * Rate);
            float f = k == 2 ? 1.25f : 1;
            for (int i = 0; i < (int)(.06f * Rate) && at + i < s.Length; i++)
            {
                float t = i / (float)Rate;
                s[at + i] += MathF.Sin(MathF.Tau * 520 * f * t) * Decay(i, .02f) + Noise() * .2f * Decay(i, .01f);
            }
        }
        for (int i = 0; i < s.Length; i++) s[i] = s[i] * .6f + r.Step(s[i]) * .8f;
        Normalise(s, .45f);
        return s;
    }

    /// <summary>A dog's bark: a rough «гав» from the chest, quick to start and gone.</summary>
    float[] Bark() => Speech(.24f, new[] { (0f, .2f, 260f, 1f, 'а') }, breath: .3f, rough: .7f, level: .75f);

    /// <summary>A horse's whinny: a voice shaking quickly, sliding down from high.</summary>
    float[] Neigh()
    {
        var s = Buf(1.1f);
        float ph = 0;
        var r1 = new Ring(900, 4); var r2 = new Ring(2100, 5);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, u = t / 1.1f;
            float f = 900 - 520 * u + 40 * MathF.Sin(MathF.Tau * 11 * t);
            ph += MathF.Tau * f / Rate;
            float saw = 2 * (ph / MathF.Tau - MathF.Floor(ph / MathF.Tau + .5f));
            float env = MathF.Min(1, t / .05f) * MathF.Pow(1 - u, .8f);
            s[i] = (r1.Step(saw) + r2.Step(saw) * .6f + saw * .2f) * env;
        }
        Normalise(s, .55f);
        return s;
    }

    /// <summary>A goat's bleat: a nasal «ме-е-е», the voice shaking at one pitch.</summary>
    float[] Bleat()
    {
        var s = Buf(.7f);
        float ph = 0;
        var r1 = new Ring(1100, 5); var r2 = new Ring(2500, 6);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, u = t / .7f;
            float f = 380 * (1 + .06f * MathF.Sin(MathF.Tau * 8 * t));
            ph += MathF.Tau * f / Rate;
            float saw = 2 * (ph / MathF.Tau - MathF.Floor(ph / MathF.Tau + .5f));
            float env = MathF.Min(1, t / .04f) * MathF.Pow(1 - u, .5f) * (.7f + .3f * MathF.Abs(MathF.Sin(MathF.Tau * 8 * t)));
            s[i] = (r1.Step(saw) * 1.5f + r2.Step(saw)) * env;
        }
        Normalise(s, .5f);
        return s;
    }
}
