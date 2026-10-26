using System;
using Godot;
using VoxelTower.Core;
using Holder = VoxelTower.Core.Owner;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// --campaign --pose=land --look=0…6: the big map as a land (AtlasView.cs), its cells laid as the campaign has them
    /// some days on — the horde holding a cell by its lair, an ore valley ours with its outpost, a treasure taken: 0 all of
    /// it as the first valley sees it, the rest in the fog; 1 all of it, the fog lifted; 2 the first valley near, its castle
    /// among its fields; 3 a lair of the horde in its scorched land; 4 the mountains and a river, a road fording it; 5 a
    /// treasure, a pointer over it saying what it is; 6 the middle of the map from halfway up.
    /// </summary>
    void LandPose()
    {
        var c = Atlas;
        Sim.Waves.Countdown = 1e9;
        // the campaign some days on: an ore valley taken and its outpost up, the horde in a cell next to a lair, a treasure taken
        var ore = c.Cells.Find(x => x.Kind == ValleyKind.Ore && Campaign.Distance(0, 0, x.Q, x.R) == 1);
        if (ore != null) { ore.Owner = Holder.Ours; ore.Outpost = true; }
        foreach (var lair in c.Cells.FindAll(x => x.Holding == Holding.HordeTown))
            if (c.Neighbour(lair, Campaign.SideToward(lair, 0, 0)) is { Owner: Holder.None, Home: false } next && next.Trove == TroveKind.None) { next.Owner = Holder.Horde; break; }
        var taken = c.Cells.Find(x => x.Trove == TroveKind.Ruins);
        if (taken != null) taken.TroveTaken = true;
        c.RevealAll = _look is >= 1 and <= 5;
        foreach (var x in new[] { ore, taken }) if (x != null) RefreshLand(x);
        foreach (var x in c.Cells) if (x.Owner == Holder.Horde && x.Holding != Holding.HordeTown) RefreshLand(x);
        OpenAtlas();
        var view = AtlasLand;
        // (no pointer over the map but where the scene puts one)
        view.HoverAt(new Vector2(-9999, -9999));
        var a = view.Atlas;
        GD.Print($"[atlas] {a.SX}×{a.SZ} voxels, meshed in {view.MeshMs:0} ms; {a.Things.Count} things");
        var (hx, hz) = a.Center(c.Home);
        switch (_look)
        {
            case 0: case 1: view.Snap(a.SX / 2f, a.SZ / 2f + 20, 999); break;
            case 2: view.Snap(hx, hz + 6, 6.5f); break;
            case 3:
            {
                var lair = c.Cells.Find(x => x.Holding == Holding.HordeTown && x.Owner == Holder.Horde);
                var (lx, lz) = a.Center(lair);
                view.Snap(lx, lz + 10, 9);
                break;
            }
            case 4:
            {
                // a mountain cell with a road fording its river, else any mountain cell
                MapCell best = null;
                foreach (var x in c.Cells) if (x.Kind == ValleyKind.Mountain && (best == null || Campaign.Distance(0, 0, x.Q, x.R) < Campaign.Distance(0, 0, best.Q, best.R))) best = x;
                var (mx, mz) = a.Center(best ?? c.Home);
                view.Snap(mx, mz + 10, 10);
                break;
            }
            case 5:
            {
                var t = a.Things.Find(x => x.Kind == AtlasThingKind.Shrine) ?? a.Things.Find(x => x.Kind == AtlasThingKind.DeadMine) ?? a.Things[0];
                view.Snap(t.X, t.Z - 2, 5.5f);
                _poseLater = () =>
                {
                    var at = view.ToScene(t.X, t.Z - 6);
                    view.HoverAt(view.Cam.UnprojectPosition(at));
                };
                break;
            }
            default: view.Snap(hx + 40, hz - 20, 16); break;
        }
    }
}
