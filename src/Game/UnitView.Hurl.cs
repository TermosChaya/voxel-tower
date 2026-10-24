using System;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class UnitView
{
    /// <summary>
    /// A great one at a throw (Sim.Giants.cs), by the time since it began: it stoops, the knees bent and the arms down to
    /// the stone; comes up with it held before its belly, the elbows bent; raises it over its head, leaning back; swings
    /// it forward and lets go, stepping into the throw; and comes back to itself. False when it is at no throw.
    /// </summary>
    bool HurlPose(Unit u, Figure f, ref float main, ref float off, ref float lean, ref float bob, ref float elbowR, ref float elbowL,
        ref float kneeR, ref float kneeL, ref float legR, ref float legL)
    {
        float t = (float)(_m.Sim.Time - u.HurlFrom);
        if (u.Dead || t < 0 || t >= Sim.HurlDone) return false;
        float arms, elbow, bend, knee, step = 0, low = 0;
        if (t < Sim.HurlStoop)
        {
            float k = Smooth(t / Sim.HurlStoop);
            arms = -.25f - .55f * k; elbow = -.1f; bend = .95f * k; knee = 1.1f * k; low = k;
        }
        else if (t < Sim.HurlLift)
        {
            float k = Smooth((t - Sim.HurlStoop) / (Sim.HurlLift - Sim.HurlStoop));
            arms = -.8f - .3f * k; elbow = -.1f - .9f * k; bend = .95f - .7f * k; knee = 1.1f - .85f * k; low = 1 - k;
        }
        else if (t < Sim.HurlRaise)
        {
            float k = Smooth((t - Sim.HurlLift) / (Sim.HurlRaise - Sim.HurlLift));
            arms = -1.1f - 1.9f * k; elbow = -1f + .3f * k; bend = .25f - .45f * k; knee = .25f - .1f * k;
        }
        else if (t < Sim.HurlLoose)
        {
            float k = (t - Sim.HurlRaise) / (Sim.HurlLoose - Sim.HurlRaise);
            k *= k;
            arms = -3f + 2f * k; elbow = -.7f + .6f * k; bend = -.2f + .65f * k; knee = .15f + .2f * k; step = k;
        }
        else
        {
            float k = Smooth((t - Sim.HurlLoose) / (Sim.HurlDone - Sim.HurlLoose));
            arms = -1f + (1 + f.Carry) * k; elbow = -.1f - .1f * k; bend = .45f - .4f * k; knee = .35f - .25f * k; step = 1 - k;
        }
        main = off = arms;
        elbowR = elbowL = elbow;
        lean = bend;
        kneeR = kneeL = knee;
        // down on its haunches as it stoops, the hips back over the heels
        bob = -low * f.Thigh * .38f;
        legR = legL = low * -.55f;
        // and a step into the throw with the left foot, the right pushing off behind
        legL += -.4f * step; legR += .25f * step;
        return true;
    }
}
