using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The sounds of acid: a flask breaking, bright glass shattering over a wet slap; a fizz where it eats through
/// something or is lost in water; a hoarse cough in its fumes; and the sizzle of it eating, a loop, as loud as there is
/// acid near the middle of the view.
/// </summary>
public partial class Sound
{
    AudioStreamPlayer _sizzle;

    void MakeAcid()
    {
        Add("glass", Glass());
        Add("fizz", Fizz());
        Add("cough", Cough());
        Add("sizzle", Sizzle(), loop: true);
    }

    void HookAcid(Sim sim)
    {
        _sizzle = Loop("sizzle");
        sim.FlaskBroke += (x, y, z) => { Play("glass", x, y, z, .05, .85f); Play("fizz", x, y, z, .05, .6f); };
        sim.Dissolved += (x, y, z, m) => Play("fizz", x + .5f, y, z + .5f, .35, m == Mat.Water ? .55f : .3f, pitch: m == Mat.Water ? .8f : 0);
        sim.Coughed += u => Play("cough", u.X, u.Y, u.Z, .5, .5f, pitch: u.T.BodyH < 12 ? 1.3f : 0);
        sim.Threw += u => Play("swish", u.X, u.Y, u.Z, .1, .4f, pitch: 1.25f);
    }

    /// <summary>The sizzle, by how much acid lies near the middle of the view.</summary>
    void AcidLoop(Sim sim)
    {
        if (_sizzle == null) return;
        float near = 0;
        int look = 0;
        foreach (int p in sim.Acids)
        {
            if (++look > 300) break;
            var (x, _, z) = WaterPlanner.Unpack(p);
            near += Near(x, z);
        }
        _sizzle.VolumeDb = near < .5f ? -80 : Mathf.LinearToDb(Math.Clamp(near / 40f, .1f, .55f));
    }

    /// <summary>Glass breaking: a slap, then a shower of bright rings, each struck once and dying fast, over a short hiss of the spill.</summary>
    float[] Glass()
    {
        var s = Buf(.7f);
        var lo = new Low(900);
        for (int i = 0; i < 600; i++) s[i] += lo.Step(Noise()) * 2.2f * Decay(i, .012f);
        for (int k = 0; k < 26; k++)
        {
            int at = (int)(Rate * (.005f + .22f * (float)_rnd.NextDouble() * (float)_rnd.NextDouble()));
            var r = new Ring(2800 + (float)_rnd.NextDouble() * 5200, 40);
            float amp = .4f + (float)_rnd.NextDouble() * .6f;
            for (int j = 0; j < 2600 && at + j < s.Length; j++) s[at + j] += r.Step(j < 12 ? Noise() : 0) * 14 * amp * Decay(j, .03f);
        }
        var hiss = new Low(6000); var cut = new Low(1800);
        for (int i = 0; i < s.Length; i++) { float t = i / (float)Rate, n = hiss.Step(Noise()); s[i] += (n - cut.Step(n)) * .25f * Math.Min(1, t / .05f) * MathF.Exp(-t / .25f); }
        Normalise(s, .7f);
        return s;
    }

    /// <summary>A fizz: a short burst of fine hiss, crackling with tiny bubbles bursting.</summary>
    float[] Fizz()
    {
        var s = Buf(.6f);
        var lo = new Low(7000); var hi = new Low(2500);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, n = lo.Step(Noise());
            float pop = _rnd.NextDouble() < .006 ? Noise() * 3 : 0;
            s[i] = ((n - hi.Step(n)) + pop) * Math.Min(1, t / .02f) * MathF.Exp(-t / .2f);
        }
        Normalise(s, .45f);
        return s;
    }

    /// <summary>A cough: two harsh bursts of breath through the throat, the second weaker.</summary>
    float[] Cough()
    {
        var s = Buf(.55f);
        var f1 = new Ring(520, 3); var f2 = new Ring(1300, 4); var lo = new Low(2500);
        foreach (var (t0, amp) in new[] { (0f, 1f), (.27f, .65f) })
        {
            int at = (int)(t0 * Rate);
            for (int j = 0; j < (int)(.2f * Rate) && at + j < s.Length; j++)
            {
                float t = j / (float)Rate, n = lo.Step(Noise());
                float env = Math.Min(1, t / .008f) * MathF.Exp(-t / .07f) * amp;
                s[at + j] += (f1.Step(n) * 2 + f2.Step(n) + n * .4f) * env;
            }
        }
        Normalise(s, .5f);
        return s;
    }

    /// <summary>Acid eating: a low steady sizzle with bubbles bursting through it, two seconds that go round.</summary>
    float[] Sizzle()
    {
        var s = Buf(2);
        var lo = new Low(5000); var hi = new Low(1200);
        for (int i = 0; i < s.Length; i++) { float n = lo.Step(Noise()); s[i] = (n - hi.Step(n)) * .55f; }
        for (int k = 0; k < 120; k++)
        {
            int at = _rnd.Next(s.Length - 300);
            var r = new Ring(2200 + (float)_rnd.NextDouble() * 3000, 10);
            float amp = .2f + (float)_rnd.NextDouble() * .4f;
            for (int j = 0; j < 250; j++) s[at + j] += r.Step(j < 8 ? Noise() : 0) * 5 * amp * Decay(j, .003f);
        }
        for (int j = 0; j < 400; j++) { float k = j / 400f; s[j] = s[j] * k + s[s.Length - 400 + j] * (1 - k); }
        Normalise(s, .5f);
        return s;
    }
}
