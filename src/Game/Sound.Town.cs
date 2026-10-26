using System;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Sound
{
    double _nextTown;

    /// <summary>
    /// The town at work by day, heard near where the view is (Sim.Town.cs): the smith's hammer on the anvil, two blows
    /// and a rest; the carpenter's adze and the workshop's mallet on timber; the masons' picks on stone. Quiet at night,
    /// and from a building that does not work. Where one of ours works outside it (Sim.Streets.cs), it is heard there.
    /// </summary>
    void TownLoop(Sim sim)
    {
        if (_t < _nextTown || sim.Buildings.Count == 0) return;
        _nextTown = _t + .45 + _rnd.NextDouble() * .5;
        if (sim.Clock.Darkness > .5f) return;
        foreach (var b in sim.Buildings)
        {
            if (!b.Ready) continue;
            float y = _m.World.SurfAt((int)b.CX, (int)b.CZ) + 8;
            var (cx, cz) = (b.CX, b.CZ);
            foreach (var s in sim.Street)
                if (s.Of == b && s.By is { Dead: false } at && at.Spot == s && at.Pastime is Pastime.Hammer or Pastime.Saw) { (cx, y, cz) = (s.X, s.Y + 6, s.Z); break; }
            switch (b.Kind)
            {
                case "smithy":
                    // (now and then he lets the iron rest in the fire)
                    if (_rnd.NextDouble() < .8) Play("anvil", cx, y, cz, .3, .55f, pitch: _rnd.NextDouble() < .5 ? 1 : 1.06f);
                    break;
                case "carpentry" or "workshop":
                    if (_rnd.NextDouble() < .45) Play("chop", cx, y, cz, .3, .35f, pitch: b.Kind == "workshop" ? .85f : 1.2f);
                    break;
                case "masons":
                    if (_rnd.NextDouble() < .45) Play("pick", cx, y, cz, .3, .35f, pitch: 1.1f);
                    break;
            }
        }
    }

    /// <summary>A hammer on an anvil: a bright ring of steel, its partials not in tune with one another, dying away in half a second.</summary>
    float[] Anvil()
    {
        var s = Buf(.6f);
        float[] f = { 880, 2420, 3960, 5730 }, tau = { .28f, .16f, .1f, .06f };
        var lo = new Low(7000);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate, v = 0;
            for (int k = 0; k < f.Length; k++) v += MathF.Sin(MathF.Tau * f[k] * t + k * .7f) * Decay(i, tau[k]) / (k + 1);
            s[i] = v * .85f + lo.Step(Noise()) * .6f * Decay(i, .008f);
        }
        Normalise(s, .5f);
        return s;
    }
}
