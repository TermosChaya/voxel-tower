using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>
/// What broken bones do to how a body holds itself and goes (Sim.Trauma.cs): an arm broken hangs limp at its side and
/// swings no more; a leg broken is kept stiff, and the body hitches down and leans off it each time it has to bear
/// on it; ribs broken, it goes bent, a hand to its side. The pinned lie under what is on them as the fallen lie.
/// </summary>
public partial class UnitView
{
    void Injured(Unit u, Figure f, Look look, ref Turns turns, ref float kneeR, ref float kneeL, ref float elbowR, ref float elbowL,
        ref float lean, ref float roll, ref Vector3 shift, ref float head)
    {
        var b = Sim.Broken(u);
        if (b == Break.None) return;
        // an arm broken hangs: the shoulder let go, the elbow straight, whatever the walk or the fight would have it do
        // splinted, it is carried in a sling instead: the forearm across the belly (UnitView.Carry.cs)
        var splinted = u.Wounds?.Splinted ?? Break.None;
        if ((b & Break.Main) != 0 && (splinted & Break.Main) != 0) { turns.Main = turns.Main.Slerp(new Quaternion(Vector3.Right, -.2f) * new Quaternion(Vector3.Back, .22f), .95f); elbowR = -1.5f; }
        else if ((b & Break.Main) != 0) { turns.Main = turns.Main.Slerp(new Quaternion(Vector3.Right, .06f) * new Quaternion(Vector3.Back, -.06f), .92f); elbowR = -.12f; }
        if ((b & Break.Off) != 0 && (splinted & Break.Off) != 0) { turns.Off = turns.Off.Slerp(new Quaternion(Vector3.Right, -.2f) * new Quaternion(Vector3.Back, -.22f), .95f); elbowL = -1.5f; }
        else if ((b & Break.Off) != 0) { turns.Off = turns.Off.Slerp(new Quaternion(Vector3.Right, .06f) * new Quaternion(Vector3.Back, .06f), .92f); elbowL = -.12f; }
        // the ribs: bent over them, the free hand pressed to the side
        if ((b & Break.Ribs) != 0)
        {
            lean += .13f; head += .06f;
            if ((b & Break.Off) == 0 && !f.Shield && turns.Fight < .3f)
            {
                turns.Off = turns.Off.Slerp(new Quaternion(Vector3.Right, -.95f), .7f);
                elbowL = Mathf.Lerp(elbowL, -1.75f, .7f);
            }
        }
        if (f.Mounted) return;
        bool right = (b & Break.LegR) != 0, left = (b & Break.LegL) != 0;
        if (!right && !left) return;
        // the broken leg kept straight
        if (right) kneeR *= .25f;
        if (left) kneeL *= .25f;
        // and the hitch as the weight comes on it: down and over toward the sound side, the trunk forward to ease it
        float going = Smooth(Math.Clamp(look.Move * 3, 0, 1));
        if (going <= 0 || right && left) return;
        const float duty = .6f;
        float p = look.Phase / MathF.Tau - .25f + (right ? 0 : .5f);
        p -= MathF.Floor(p);
        float bear = p < duty ? MathF.Sin(MathF.PI * p / duty) : 0;
        float side = right ? -1 : 1;
        roll += side * .13f * bear * going;
        lean += (.05f + .07f * bear) * going;
        shift += new Vector3(0, -.45f * bear * going, 0);
    }
}
