using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The wounded cared for (--pose=carry, --at=seconds, 2.5 by itself, the game stopped there), on the level ground by the citadel, at its far end: a worker
    /// carrying a swordsman with both legs broken and splinted toward the citadel on its shoulder, a spearman on a broken
    /// leg helped along by another, its arm over his shoulder, an archer with an arm in a sling, and a worker pinned under
    /// a heap calling out. --look=1 close on the carrier, 2 on the one helped along, 3 on the sling and the splints.
    /// </summary>
    void CarryPose()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9;
        // at the end of the level ground away from the citadel's door, so that there is a way to go
        var (gx, gz) = GroveAt;
        var (fx, _, fz) = sim.Nav.Feet(sim.Gate);
        int cx = Math.Abs(gx + 30 - fx) + Math.Abs(gz + 110 - fz) > Math.Abs(gx + 270 - fx) + Math.Abs(gz + 110 - fz) ? gx + 40 : gx + 260, cz = gz + 110, g = w.SurfAt(cx, cz);
        double at = _poseSeconds is > 0 and < 60 ? _poseSeconds : 2.5;
        Unit Put(UnitType t, int dx, int dz) => sim.Spawn(t, sim.NearestStand(cx + dx, cz + dz, t.Move.Height));
        var hurt = Put(Kinds.Swordsman, 0, 0);
        sim.BreakBone(hurt, Break.LegR); sim.BreakBone(hurt, Break.LegL);
        sim.SplintBones(hurt);
        var carrier = Put(Kinds.Worker, 10, 0);
        var lame = Put(Kinds.Spearman, -20, 26);
        sim.BreakBone(lame, Break.LegL);
        sim.SplintBones(lame);
        Put(Kinds.Worker, -8, 32);
        var buried = Put(Kinds.Worker, -34, -26);
        Mound(buried);
        Advance(.2);
        sim.OrderAid(new[] { carrier }, hurt);
        Advance(at);
        // (set down at the end, or it would be gone into the citadel to mend by now)
        var slung = sim.Spawn(Kinds.Archer, sim.NearestStand((int)carrier.X + 24, (int)carrier.Z - 16, Kinds.Archer.Move.Height));
        sim.BreakBone(slung, Break.Main);
        sim.SplintBones(slung);
        Paused = true;
        Unit near = _look switch { 1 => carrier, 2 => lame, 3 => slung, _ => null };
        if (near != null)
        {
            Selected.Add(near); Hud.OnSelectionChanged();
            // from its side, square to the way it goes
            Rig.SetAt(w.WX(near.X), w.WY(near.Y + 8), w.WZ(near.Z), -near.Facing, .32f, 6.5f);
        }
        else Rig.SetAt(w.WX(carrier.X - 10), w.WY(carrier.Y + 6), w.WZ(carrier.Z + 6), -.6f, .5f, 3.4f);
        Hud.Refresh();
    }
}
