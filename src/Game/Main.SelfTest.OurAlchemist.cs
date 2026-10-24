using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The self-test's alchemist of ours: on a stone wall of ours far south-west of the citadel, with a goblin held on the horde's
    /// ladder up its outer face, it throws at the rungs under it: they are eaten through, the goblin comes down, our stone is
    /// not eaten, and the glass is heard. All of it cleared away after.
    /// </summary>
    async Task SelfTestOurAlchemist(Action<string, bool, string> check)
    {
        async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var w = World; var sim = Sim;
        bool knew = sim.Knows("alchemists");
        sim.Learnt.Add("alchemists");
        // a level patch far south-west of the citadel (no other check of the self-test builds there), a wall of stone
        // four metres high on it and the horde's ladder up its outer face, as on the bench (the wall's face on the line
        // between two cells of the walkable world, so that the ladder has a cell of its own to be climbed in)
        int wx = Math.Max(80, Site.KeepX - Site.KeepR - 320) & ~7, kz = Math.Max(80, Site.KeepZ - 220), foot = w.SurfAt(wx, kz);
        for (int z = kz - 34; z <= kz + 34; z++)
            for (int x = wx - 50; x <= wx + 12; x++)
                for (int y = 2; y < Math.Min(w.SY - 1, foot + 48); y++)
                {
                    byte want = y < foot ? Mat.Soil : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
        for (int z = kz - 24; z <= kz + 24; z++) for (int x = wx; x < wx + 6; x++) for (int y = foot; y < foot + 32; y++) w.Set(x, y, z, Mat.Stone);
        int face = wx - 1, top = foot + 32;
        for (int z = kz - 1; z <= kz; z++) for (int y = foot; y < top; y++) w.Set(face, y, z, Mat.SiegeLadder);
        Advance(.2);
        int Stone() { int n = 0; for (int z = kz - 24; z <= kz + 24; z++) for (int x = wx; x < wx + 6; x++) for (int y = foot; y < top; y++) if (w.Get(x, y, z) == Mat.Stone) n++; return n; }
        int c = sim.Nav.CellOf(face, foot + 20, kz);
        var gob = sim.Spawn(Kinds.Goblin, c);
        gob.Cell = c; (gob.X, gob.Y, gob.Z) = (face + .5f, foot + 20, kz + .5f);
        gob.ChainedUntil = 1e9; gob.Hp = gob.MaxHp = 5000;
        var al = sim.Spawn(Kinds.OurAlchemist, sim.NearestStand(wx, kz + 18, 2));
        al.Hold = true; al.Dest = al.Post = al.Cell; al.Hp = al.MaxHp = 5000;
        bool climbing = sim.Climbing(gob);
        int stone0 = Stone(), ate0 = sim.OurAte;
        Rig.Focus(w.WX(face - 20), w.WZ(kz));
        Advance(10);
        await Frames(20);
        check("an alchemist of ours on our wall throws at the rungs under a goblin on the horde's ladder: they are eaten through, it comes down, our stone stands, the glass is heard",
            climbing && sim.OurAte > ate0 && !sim.Climbing(gob) && gob.Y < foot + 8 && Stone() == stone0 && Sound.Heard.Contains("glass"),
            $"on the ladder {climbing}; {sim.OurAte - ate0} rungs eaten; the goblin {gob.Y - foot:0} up, on it {sim.Climbing(gob)}; our stone {stone0} → {Stone()}");
        gob.Dead = al.Dead = true; gob.DiedAt = al.DiedAt = -999;
        // the wall and what is left of the ladder taken away again
        for (int z = kz - 24; z <= kz + 24; z++) for (int x = face; x < wx + 6; x++) for (int y = foot; y < top + 2; y++) if (w.Get(x, y, z) != Mat.Air) w.Set(x, y, z, Mat.Air);
        if (!knew) sim.Learnt.Remove("alchemists");
        foreach (int p in sim.Acids.ToList()) { var (x, y, z) = WaterPlanner.Unpack(p); w.Set(x, y, z, Mat.Air); }
        sim.Arrows.RemoveAll(a => a.Kind == VoxelTower.Core.Shot.Flask);
        await Frames(2);
    }
}
