using System;
using System.Linq;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// --campaign --pose=holdfight [--size=S] [--at=seconds]: a mine of the big map held by a garrison of ours — a
    /// commander, archers, spearmen, workers — behind a square of stone wall with towers at its corners, and a party of
    /// the horde come in on it as the game's battle for a point brings it (Main.Defence.cs), the map stopped meanwhile;
    /// the picture after so many seconds of the battle (30 by itself), from inside the wall toward where they come.
    /// </summary>
    void HoldFightBefore()
    {
        var c = Atlas; var home = c.Home;
        var mine = c.Cells.Where(x => x.Thing is { Kind: ThingKind.Mine } && x.Passable && x.Sum.Keep.Count == 0 && !x.Home)
            .OrderBy(x => Campaign.Distance(x.Q, x.R, home.Q, home.R)).First();
        mine.Owner = Holder.Ours; mine.Thing.Owner = Holder.Ours;
        c.HereQ = mine.Q; c.HereR = mine.R;
    }

    void HoldFight()
    {
        var c = Atlas; var mine = c.Here;
        var (lx, lz) = LevelNearKeep();
        if (lx < 0) (lx, lz) = (Site.KeepX, Site.KeepZ);
        // the works: a square of wall 3 m high, towers 6 m at its corners
        int top = Shapes.LevelTop(World, lx, lz, 26);
        Sim.InstantLabour = true;
        Sim.DesignateBuild(Shapes.Box(World, Site, lx - 48, lz - 48, lx + 48, lz + 48, new Stroke { Mat = Mat.Stone, Width = 4, Height = 26, Level = true, Hollow = true }, top));
        foreach (var (tx, tz) in new[] { (lx - 48, lz - 48), (lx + 48, lz - 48), (lx - 48, lz + 48), (lx + 48, lz + 48) })
            Sim.DesignateBuild(Shapes.Box(World, Site, tx - 6, tz - 6, tx + 6, tz + 6, new Stroke { Mat = Mat.Stone, Width = 12, Height = 48, Level = true }, top + 22));
        Sim.CompleteAll();
        Sim.InstantLabour = false;
        // the garrison: the archers up on the walls, the rest at their posts inside, the commander holding them there
        var held = new System.Collections.Generic.List<Unit>();
        foreach (var t in new[] { Kinds.Captain, Kinds.Spearman, Kinds.Spearman, Kinds.Swordsman, Kinds.Worker, Kinds.Worker })
            held.Add(Sim.Spawn(t, Sim.NearestStand(lx + Sim.Rng.Next(-14, 15), lz + Sim.Rng.Next(-14, 15), t.Move.Height)));
        foreach (var (wx, wz) in new[] { (lx, lz - 48), (lx, lz + 48), (lx - 48, lz), (lx + 48, lz) })
        {
            int at = Sim.StandNear(wx, top, wz, Kinds.Archer.Move.Height);
            if (at >= 0) held.Add(Sim.Spawn(Kinds.Archer, at));
        }
        foreach (var u in held) { u.Garrison = true; u.Commands = false; Sim.Command(new[] { u }, u.Cell, stay: true); }
        // the horde's party come on it: the map stopped for the battle, as the game's flow has it
        c.Frozen = mine;
        int side = mine.EntrySide;
        var enemy = new MapParty { Id = 9950, Owner = Holder.Horde, Party = new Party(), Q = mine.Q, R = mine.R, CameQ = mine.Q, CameR = mine.R, Aim = PartyAim.Strike };
        foreach (var t in new[] { Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Orc, Kinds.Orc, Kinds.Orc, Kinds.Orc, Kinds.Troll })
            enemy.Party.Members.Add(new Traveller { Kind = t, Hp = t.Hp, MaxHp = t.Hp });
        DefenceBegins(enemy, side);
        Advance(_poseSeconds == 14 ? 30 : _poseSeconds);
        var (ix, iz) = (Site.Hex ?? Hex.In(World.SX, World.SZ)).Inward(side);
        Rig.SetAt(World.WX(lx + ix * 20), World.WY(top), World.WZ(lz + iz * 20), MathF.Atan2(-ix, -iz) + .35f, .62f, 1.3f);
    }
}
