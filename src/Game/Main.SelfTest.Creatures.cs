using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The self-test's small life and town beasts (Ambience.cs, AmbienceView.cs, Sim.Tame.cs), on a clearing west of the
    /// citadel with a copse in it: by day flocks settle about it and are drawn; a worker set down by a flock puts it up;
    /// a hen and the yard dog set down there are drawn, the hen's hover words say what she does and nobody can be sent to
    /// hunt her.
    /// </summary>
    async Task SelfTestCreatures(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var sim = Sim; var w = World;
        if (sim.Alarm) sim.StandDown();
        int cx = Site.KeepX - Site.KeepR - 170, cz = Site.KeepZ - 120;
        ClearTrees(cx - 70, cz - 70, cx + 70, cz + 70);
        // a copse of four trees in the clearing
        foreach (var (dx, dz) in new[] { (-30, -20), (-10, -36), (18, -24), (-24, 10) })
        {
            int tx = cx + dx, tz = cz + dz, tg = w.SurfAt(tx, tz);
            for (int y = tg; y < tg + 28; y++) w.Set(tx, y, tz, Mat.Trunk);
            for (int y = tg + 22; y < tg + 36; y++)
                for (int ddz = -8; ddz <= 8; ddz++)
                    for (int ddx = -8; ddx <= 8; ddx++)
                        if (ddx * ddx + ddz * ddz + (y - tg - 29) * (y - tg - 29) < 60 && w.Get(tx + ddx, y, tz + ddz) == 0) w.Set(tx + ddx, y, tz + ddz, Mat.Leaf);
        }
        sim.Clock.Night = false; sim.Clock.DawnLeft = 0; sim.Clock.Left = DayClock.DayLength / 2;
        sim.Weather = Weather.Clear;
        Rig.Set(w.WX(cx), w.WZ(cz), -.66f, .6f, 2.2f, snap: true);
        await Frames(3);
        Life.Warm(45);
        await Frames(4);
        var flocks = Life.Life.SettledFlocks(Critter.Sparrow);
        int drawn = Life.Shown.GetValueOrDefault(Critter.Sparrow) + Life.Shown.GetValueOrDefault(Critter.Pigeon);
        check("small life: by day sparrows settle about the clearing and are drawn", flocks.Count > 0 && drawn > 0, $"settled sparrow flocks {flocks.Count}, birds drawn {drawn}");
        int ups = Life.Life.TakeOffs;
        Unit walker = null;
        if (flocks.Count > 0)
        {
            walker = sim.Spawn(Kinds.Worker, sim.NearestStand((int)flocks[0].x + 16, (int)flocks[0].z, 2));
            Life.Warm(.6);
        }
        check("small life: a worker set down by a flock puts it up", Life.Life.TakeOffs > ups, $"take-offs {Life.Life.TakeOffs - ups}");

        // the town's beasts: a hen and the yard dog
        var hen = sim.Spawn(Kinds.Hen, sim.NearestStand(cx + 30, cz + 20, 1));
        var cur = sim.Spawn(Kinds.Cur, sim.NearestStand(cx + 40, cz + 24, 1));
        hen.Home = hen.Cell; cur.Home = cur.Cell;
        Advance(1);
        await Frames(6);
        string words = Retinue.Doing(hen, sim);
        var archer = sim.Spawn(Kinds.Archer, sim.NearestStand(cx + 10, cz + 30, 2));
        int sent = sim.Hunt(new[] { archer }, hen);
        check("the town's beasts: a hen and the yard dog are drawn, the hen's words say what she does, and nobody is sent to hunt her",
            Units.Shows(hen.Id) && Units.Shows(cur.Id) && !hen.Dead && words.Length > 0 && !words.Contains("добыт") && sent == 0,
            $"the hen drawn {Units.Shows(hen.Id)}, the dog drawn {Units.Shows(cur.Id)}, the hen «{words}», hunters sent {sent}");
        foreach (var u in new[] { hen, cur, archer, walker }) if (u != null) { u.Dead = true; u.DiedAt = -999; }
        Advance(.2);
    }
}
