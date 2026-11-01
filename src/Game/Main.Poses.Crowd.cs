using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// A large household about the citadel (--pose=crowd, --look=how many in all, 30 if not said), for the interface with
    /// many of them: the faces along the top in their rows and the «+N», the console with many kinds (--select chooses
    /// them, --tool takes up a tool).
    /// </summary>
    void CrowdPose()
    {
        var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int n = _look > 0 ? _look : 30, have = 0;
        var kinds = new[] { Kinds.Worker, Kinds.Archer, Kinds.Swordsman, Kinds.Spearman, Kinds.Priest, Kinds.Crossbowman, Kinds.Slinger };
        foreach (var u in sim.Units) if (!u.Dead && u.Team == Team.Player && !u.T.Called) have++;
        for (int i = have; i < n; i++)
        {
            var t = kinds[i % kinds.Length];
            int c = sim.NearestStand(Site.KeepX + (i % 10 - 5) * 10, Site.KeepZ + Site.KeepR + 30 + i / 10 * 10, t.Move.Height);
            if (c < 0) continue;
            var u = sim.Spawn(t, c);
            (u.X, u.Y, u.Z) = sim.Nav.Feet(c);
        }
        Paused = true;
    }
}
