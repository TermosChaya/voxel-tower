using System;
using System.Collections.Generic;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// The faith's spells at work (--pose=angels), on the open ground west of the citadel, a band of the horde coming on:
    /// --look=0 a guardian angel over a file of ours the band is at; 1 the cherub cutting through the band; 2 the
    /// destroyer passing over it; 3 the heavenly host come down on it; 4 hail on it and its ladder; 5 a pillar of fire at
    /// night with the band at it and our archers by; 6 the faith's page of the tree; 7 the rites on the panel of mysteries.
    /// </summary>
    void FaithPose()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        foreach (var s in Sim.Spells) if (!s.Draft) sim.Known.Add(s.Kind);
        sim.Favour = 600;
        if (_look == 6)
        {
            foreach (var l in Sim.LoresOn(2)) if (l.Tier < 2) { if (l.Spell is { } k) sim.Known.Add(k); else sim.Grant(l); }
            foreach (var s in Sim.Spells) if (s.Circle == 2 && s.Kind != SpellKind.Guardian) sim.Known.Remove(s.Kind);
            sim.Revelations = 9; sim.Favour = 150;
            Hud.OpenPanel("lore");
            Hud.Lore.ShowPage(2);
            return;
        }
        if (_look == 7)
        {
            for (int i = 0; i < 3; i++) { var p = sim.Spawn(Kinds.Orc, sim.Gate); p.Inside = true; p.Bound = true; }
            sim.Carcasses.Add(Kinds.Deer);
            Hud.OpenPanel("lore");
            Hud.Lore.ShowMysteries(true);
            return;
        }
        const float M = VoxelWorld.PerMetre;
        if (_look == 5) Nightfall();
        int gx = Site.KeepX - Site.KeepR - (int)(40 * M), gz = Site.KeepZ;
        ClearTrees(gx - 300, gz - 200, gx + 120, gz + 200);
        Unit Raider(UnitType t, float x, float z) { var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height)); u.Guard = true; u.Home = u.Cell; return u; }
        Unit Ours(UnitType t, float x, float z) { var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height)); u.Hold = true; u.Dest = u.Cell; u.Post = u.Cell; return u; }
        // the band, a little west of the spot
        var band = new List<Unit>();
        for (int i = 0; i < 24; i++)
            band.Add(Raider(i % 8 == 0 ? Kinds.Troll : i % 3 == 0 ? Kinds.Orc : Kinds.Goblin, gx - 6 * M - i % 6 * 1.6f * M, gz - 4 * M + i / 6 * 1.8f * M));
        float sx = gx, sz = gz;
        switch (_look)
        {
            case 0:
                for (int i = 0; i < 6; i++) Ours(Kinds.Swordsman, gx + 1 * M, gz - 4 * M + i * 1.6f * M);
                foreach (var u in band) u.Guard = false;
                sim.Cast(SpellKind.Guardian, gx + 1 * M, w.SurfAt(gx, gz), gz);
                Advance(4);
                break;
            case 1:
                sim.Cast(SpellKind.Cherub, gx - 2 * M, w.SurfAt(gx, gz), gz);
                foreach (var u in band) u.Guard = false;
                Advance(Deities.Cherub.RiseFor + .2);
                var cherub = sim.Gods.Find(g => g.T.Kind == DeityKind.Cherub);
                for (int i = 0; i < 120 && cherub != null && !(cherub.Act == DeityAct.Sweep && cherub.Into(sim.Time) > 1.1f); i++) sim.Step(.05f);
                sx = gx - 4 * M;
                break;
            case 2:
                sim.Cast(SpellKind.Destroyer, gx - 10 * M, w.SurfAt(gx, gz), gz);
                Advance(Sim.PassFor * .55f);
                sx = gx - 10 * M;
                break;
            case 3:
                sim.Cast(SpellKind.Host, gx, w.SurfAt(gx, gz), gz);
                foreach (var u in band) u.Guard = false;
                Advance(3);
                sx = gx - 4 * M;
                break;
            case 4:
                sim.Cast(SpellKind.Hail, gx - 10 * M, w.SurfAt(gx, gz), gz);
                Advance(2.2);
                sx = gx - 10 * M;
                break;
            case 5:
                for (int i = 0; i < 5; i++) Ours(Kinds.Archer, gx + 12 * M, gz - 4 * M + i * 2 * M);
                sim.Cast(SpellKind.FirePillar, gx - 2 * M, w.SurfAt(gx, gz), gz);
                foreach (var u in band) u.Guard = false;
                Advance(3);
                break;
        }
        Paused = true;
        _shotFrames = Math.Max(_shotFrames, 60);
        // (the destroyer is high over the field)
        float y = w.WY(w.SurfAt((int)sx, (int)sz)) + (_look == 2 ? 7 : 3);
        float yaw = _cam is { Length: > 0 } ? _cam[0] : -1.1f;
        Rig.SetAt(w.WX(sx), y, w.WZ(sz), yaw, .42f, 1.6f);
        Hud.Refresh();
    }
}
