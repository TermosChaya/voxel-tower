using System;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// The valley's voices (Sim.Voices.cs), made as the warning is (Sound.Shout.cs): a buzz at a man's pitch through the
/// formants of each vowel. A greeting of three syllables rising at the end; talk as a low murmur of four; a laugh, three
/// breathy «ха»; a call, one «эй» loud and high; a cheer, «у-ра» rising and held; the horde's chant, a low «хо» of three
/// voices together; its roar, rough and long; and its growl, heard now and then from one of it near the middle of the
/// view. Each voice a little higher or lower by who says it.
/// </summary>
public partial class Sound
{
    double _nextGrowl;

    void MakeVoices()
    {
        Add("greet", Speech(.62f, new[] { (0f, .14f, 190f, .7f, 'о'), (.16f, .3f, 205f, .8f, 'о'), (.32f, .58f, 228f, 1f, 'о') }));
        Add("chat", Speech(.55f, new[] { (0f, .1f, 165f, .55f, 'а'), (.12f, .22f, 178f, .6f, 'е'), (.25f, .35f, 160f, .5f, 'о'), (.38f, .52f, 170f, .55f, 'и') }, level: .32f));
        Add("laugh", Speech(.62f, new[] { (0f, .13f, 232f, .9f, 'а'), (.2f, .32f, 246f, .8f, 'а'), (.39f, .52f, 226f, .65f, 'а') }, breath: 1.2f, level: .45f));
        Add("call", Speech(.38f, new[] { (0f, .34f, 245f, 1f, 'е') }, level: .65f));
        Add("cheer", Speech(.85f, new[] { (0f, .2f, 212f, .85f, 'у'), (.23f, .8f, 252f, 1f, 'а') }, level: .65f));
        Add("chant", Speech(.42f, new[] { (0f, .36f, 118f, 1f, 'о') }, breath: .8f, rough: .25f, voices: 3, level: .7f));
        Add("roar", Speech(.9f, new[] { (0f, .85f, 112f, 1f, 'а') }, breath: .5f, rough: .6f, voices: 2, level: .8f));
        Add("growl", Speech(.75f, new[] { (0f, .7f, 74f, 1f, 'у') }, rough: .8f, level: .5f));
    }

    void HookVoices(Sim sim)
    {
        sim.Spoke += (u, _, v) =>
        {
            var (clip, every, loud) = v switch
            {
                Voice.Greet => ("greet", .25, .5f),
                Voice.Chat => ("chat", .2, .36f),
                Voice.Laugh => ("laugh", .3, .42f),
                Voice.Call => ("call", .3, .8f),
                Voice.Praise => ("cheer", .3, .95f),
                Voice.Cheer => ("cheer", .1, .65f),
                Voice.Chant => ("chant", .25, .75f),
                _ => ("roar", .15, 1f),
            };
            float pitch = u.Team == Team.Horde ? .84f + u.Id % 4 * .05f : .9f + u.Id % 6 * .04f;
            Play(clip, u.X, u.Y + 12, u.Z, every, loud, pitch: pitch);
        };
    }

    /// <summary>Now and then one of the horde near the middle of the view growls.</summary>
    void Growls(Sim sim)
    {
        if (_t < _nextGrowl) return;
        _nextGrowl = _t + 2.5 + _rnd.NextDouble() * 3;
        foreach (var u in sim.Units)
        {
            if (u.Dead || u.Inside || u.Team != Team.Horde || u.T.NoHands || Kinds.IsEngine(u.T) || u.Bound || Near(u.X, u.Z) < .45f) continue;
            Play("growl", u.X, u.Y + 10, u.Z, 1.5, .55f, pitch: .85f + u.Id % 5 * .06f);
            return;
        }
    }

    /// <summary>
    /// Syllables said in one breath: each its time, its pitch, how loud and its vowel; a dip in the voice opens each.
    /// Breath puts a rush of air before each (the «х» of «ха», «хо»); rough shakes the voice as a growl does; voices are
    /// several saying it together, each a little off the others' pitch.
    /// </summary>
    float[] Speech(float seconds, (float a, float b, float pitch, float loud, char vowel)[] syl, float breath = 0, float rough = 0, int voices = 1, float level = .55f)
    {
        var s = Buf(seconds);
        var lo = new Low(3600);
        var hiss = new Ring(3200, 2);
        for (int v = 0; v < voices; v++)
        {
            var f1 = new Ring(500, 6); var f2 = new Ring(1500, 8); var f3 = new Ring(2500, 9);
            float phase = (float)_rnd.NextDouble(), detune = 1 + (v - (voices - 1) / 2f) * .045f, shake = 0;
            char was = ' ';
            for (int n = 0; n < s.Length; n++)
            {
                float t = n / (float)Rate, env = 0, pitch = 0, h = 0;
                char vowel = was;
                foreach (var (a, b, p, loud, vw) in syl)
                {
                    if (t < a - .06f || t > b) continue;
                    if (t < a) { h = breath * (1 - (a - t) / .06f); continue; }
                    float k = (t - a) / (b - a);
                    env = loud * Math.Min(1, (t - a) / .02f) * (k > .75f ? MathF.Max(0, (1 - k) / .25f) : 1);
                    pitch = p * (1 + .06f * MathF.Sin(MathF.PI * k));
                    vowel = vw;
                }
                if (vowel != was)
                {
                    var (a1, a2, a3) = Formants(vowel);
                    f1 = new Ring(a1, 6); f2 = new Ring(a2, 8); f3 = new Ring(a3, 9);
                    was = vowel;
                }
                if (rough > 0) shake += ((float)_rnd.NextDouble() * 2 - 1 - shake) * .02f;
                phase += pitch * detune * (1 + rough * shake * .4f) / Rate;
                phase -= MathF.Floor(phase);
                float buzz = phase * 2 - 1 + Noise() * (.25f + rough * .5f);
                float voice = (f1.Step(buzz) * 2.2f + f2.Step(buzz) * 1.2f + f3.Step(buzz) * .45f) * env * (1 + rough * shake * 1.5f);
                s[n] += (lo.Step(voice) + hiss.Step(Noise()) * h * 1.4f) / voices;
            }
        }
        Normalise(s, level);
        return s;
    }

    /// <summary>The three formants of a vowel, Hz, for a man's voice.</summary>
    static (float, float, float) Formants(char v) => v switch
    {
        'а' => (720, 1220, 2600),
        'о' => (520, 900, 2400),
        'у' => (330, 820, 2240),
        'и' => (310, 2250, 3050),
        _ => (540, 1820, 2600),
    };
}
