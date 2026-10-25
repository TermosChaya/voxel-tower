using System;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The self-test's engines of ours: the catapults learnt, one taken on as a click on its tile takes it, paid in gold
    /// and timber, out of the citadel with its crew; a band of goblins on the field west of the citadel within its reach,
    /// and a stone thrown at them, the panel saying so, the stone drawn in the air. Cleared away after.
    /// </summary>
    async Task SelfTestOurEngines(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var sim = Sim;
        if (sim.Alarm) sim.StandDown();
        foreach (var r in sim.Units) if (r.Team == Team.Horde && !r.Dead) { r.Dead = true; r.DiedAt = -999; }
        sim.Knowledge += 10;
        // assembled at a workshop since 25.10.2026 (Sim.Town.cs): one here as it would stand, by the citadel's door
        bool learnt = sim.Knows("b_workshop") || sim.Learn("b_workshop");
        sim.Stock.Gold = Math.Max(sim.Stock.Gold, 400);
        sim.Stock[Stockpile.Wood] = Math.Max(sim.Stock[Stockpile.Wood], 10 * VoxelWorld.PerM3);
        // (and some iron, since 25.10.2026)
        sim.Stock[Stockpile.Metal] = Math.Max(sim.Stock[Stockpile.Metal], 2 * VoxelWorld.PerM3);
        double wood0 = sim.Stock[Stockpile.Wood];
        var (sx, _, sz) = sim.Nav.Feet(sim.Gate);
        var shop = new Building { Kind = "workshop", X0 = (int)sx - 18, Z0 = (int)sz + 30, X1 = (int)sx + 18, Z1 = (int)sz + 66, Level = 1, Ready = true };
        shop.Stock[Kinds.Mangonel.Key] = 1;
        sim.Buildings.Add(shop);
        bool took = sim.HireAt(shop, Kinds.Mangonel) != null;
        // (read at once: the household brings timber in and takes it out while the engine comes out)
        double wood1 = sim.Stock[Stockpile.Wood];
        sim.Buildings.Remove(shop);
        var e = sim.Units.FindLast(u => u.T == Kinds.Mangonel && !u.Dead);
        int crew = e == null ? 0 : sim.CrewOf(e);
        // out of the citadel with its crew, and a band of goblins coming on west of it, within its reach
        for (int i = 0; i < 40 && e != null && (e.Inside || sim.CrewAt(e) < 2); i++) Advance(.5);
        int shots0 = sim.EngineShots;
        if (e != null)
        {
            int gx = (int)e.X - 220, gz = (int)e.Z;
            ClearTrees(gx - 30, gz - 30, gx + 30, gz + 30);
            for (int i = 0; i < 4; i++)
            {
                var g = sim.Spawn(Kinds.Goblin, sim.NearestStand(gx + i % 2 * 6, gz + i / 2 * 6, 2));
                g.Guard = true; g.Home = g.Cell; g.ChainedUntil = 1e9;
            }
        }
        bool aloft = false;
        string doing = "";
        for (int i = 0; i < 60 && e != null && sim.EngineShots == shots0; i++) Advance(.5);
        if (e != null && sim.EngineShots > shots0)
        {
            Rig.Focus(World.WX(e.X - 110), World.WZ(e.Z));
            await Frames(3);
            aloft = sim.Fliers.Exists(f => f.Cause == Sim.MangonelCause);
            doing = Retinue.Doing(e, sim);
        }
        check("a catapult of ours is learnt, taken on at the muster for gold and timber, comes out of the citadel with its crew of two and throws at a band within its reach, the stone in the air and the panel saying at whom",
            learnt && took && crew == 2 && Math.Abs(wood0 - wood1 - Kinds.Mangonel.Logs * VoxelWorld.PerM3) < .01 && sim.EngineShots > shots0 && aloft && doing.Contains("бьёт"),
            $"learnt {learnt}, taken {took}, crew {crew}, timber {wood0:0.#} → {wood1:0.#}; thrown {sim.EngineShots - shots0}, aloft {aloft}; «{doing}»");
        foreach (var u in sim.Units)
            if (!u.Dead && (u == e || u.Escort == e && e != null || u.Team == Team.Horde)) { u.Dead = true; u.DiedAt = -999; }
        sim.Fliers.Clear();
        await Frames(2);
    }
}
