using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// What is held before the eyes in first person, on a field of turf east of the citadel: a swordsman, a worker and
    /// a bowman gone into with nobody near carry what they hold low at the side (the sword and the pick at the right, the
    /// bow at the left), in a small part of the sight and out of its middle; a goblin set down five metres off brings
    /// it up to the guard; the goblin gone, it goes down again a moment after.
    /// </summary>
    async Task SelfTestHands(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var w = World; var sim = Sim;
        // a field of level turf, bare of trees, nobody of the horde near
        int fx = Site.KeepX + Site.KeepR + 260, fz = Site.KeepZ + 160, g = w.SurfAt(fx, fz);
        ClearTrees(fx - 80, fz - 60, fx + 80, fz + 60);
        for (int z = fz - 50; z <= fz + 50; z++)
            for (int x = fx - 70; x <= fx + 70; x++)
                for (int y = g - 6; y < g + 40; y++)
                {
                    byte want = y < g ? (y == g - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
        if (sim.Alarm) sim.StandDown();
        foreach (var t in new[] { Kinds.Swordsman, Kinds.Worker, Kinds.Archer })
        {
            var me = sim.Spawn(t, sim.NearestStand(fx + 20, fz, 2));
            Advance(.3);
            bool inside = FP.Enter(me);
            FP.Face(MathF.PI, -.04f);
            await Frames(12);
            var vp = GetViewport().GetVisibleRect().Size;
            float share = FP.Hands.Covered(FP.Cam, vp, new Rect2(vp * .4f, vp * .2f), out bool middle, out _);
            FP.Hands.Covered(FP.Cam, vp, new Rect2(vp * .4f, vp * .2f), out _, out var at, heldOnly: true);
            bool side = t == Kinds.Archer ? at.X < vp.X * .5f : at.X > vp.X * .55f;
            float rest = FP.Hands.Raised;
            bool low = inside && rest < .05f && share > .005f && share < .15f && !middle && side && at.Y > vp.Y * .5f;
            // a goblin five metres before it, held fast where it stands
            var gob = sim.Spawn(Kinds.Goblin, sim.NearestStand((int)me.X - 40, (int)me.Z, 2));
            gob.ChainedUntil = 1e9;
            for (int i = 0; i < 90 && FP.Hands.Raised < .95f; i++) { Advance(.02); await Frames(1); }
            float up = FP.Hands.Raised;
            gob.Dead = true; gob.DiedAt = -999;
            int frames = 0;
            for (; frames < 600 && FP.Hands.Raised > .05f; frames++) { Advance(.02); await Frames(1); }
            float down = FP.Hands.Raised;
            check($"first person: {me.Name.ToLowerInvariant()} carries what it holds low at the side, out of the middle of the sight; a goblin near brings it up, gone it goes down again",
                low && up >= .95f && down <= .05f,
                $"in {inside}; at rest raised {rest:0.00}, covering {share * 100:0.0} % of the sight about {at.X / vp.X:0.00},{at.Y / vp.Y:0.00}, in its middle {middle}; goblin near: raised {up:0.00}; gone: {down:0.00} after {frames} frames");
            FP.Exit();
            me.Dead = true; me.DiedAt = -999;
            await Frames(2);
        }
    }
}
