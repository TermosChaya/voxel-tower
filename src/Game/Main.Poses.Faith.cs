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

    /// <summary>
    /// The horde's faith (--pose=swamp), west of the citadel: --look=0 orcs at the fallen, tearing at them; 1 a sapper
    /// and a shaman by a wall of ours, the ivy grown up it; 2 the swamp's poison over a knot of ours; 3 the horde's tab
    /// with its faith.
    /// </summary>
    void SwampPose()
    {
        var sim = Sim; var w = World;
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        const float M = VoxelWorld.PerMetre;
        int gx = Site.KeepX - Site.KeepR - (int)(40 * M), gz = Site.KeepZ;
        ClearTrees(gx - 300, gz - 200, gx + 120, gz + 200);
        Unit Put(UnitType t, float x, float z) { var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height)); if (t.Team == Team.Horde) { u.Guard = true; u.Home = u.Cell; } else { u.Hold = true; u.Dest = u.Post = u.Cell; } return u; }
        float sx = gx, sz = gz;
        switch (_look)
        {
            case 0:
                for (int i = 0; i < 4; i++)
                {
                    var body = Put(i % 2 == 0 ? Kinds.Swordsman : Kinds.Goblin, gx + i * 3 * M, gz + (i % 2) * 2 * M);
                    sim.Hurt(body, null, 9999, "test");
                    var orc = Put(i == 3 ? Kinds.Troll : Kinds.Orc, gx + i * 3 * M + 1.5f * M, gz + (i % 2) * 2 * M + 1 * M);
                    orc.Hp = orc.MaxHp * .5f;
                }
                Advance(2.5);
                sx = gx + 4 * M;
                break;
            case 1:
            {
                // a ring of stone about the citadel, the horde with timber; a sapper comes at it from the west, its shaman by
                foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
                sim.InstantLabour = true;
                int ring = Site.KeepR + 44;
                sim.DesignateBuild(Shapes.Circle(w, Site, Site.KeepX, Site.KeepZ, ring, new Stroke { Mat = Mat.Stone, Width = 8, Height = 48, Hollow = true }, 0));
                sim.InstantLabour = false;
                sim.HordeStock[Stockpile.Wood] = 4096;
                sim.ComputeFields();
                var sapper = sim.Spawn(Kinds.Sapper, sim.NearestStand(Site.KeepX - ring - 36, Site.KeepZ, 3));
                var shaman = sim.Spawn(Kinds.Shaman, sim.NearestStand(Site.KeepX - ring - 40, Site.KeepZ + 4, 2));
                shaman.ChainedUntil = 1e9;
                int ivy = 0;
                sim.Raised = (x, y, z, m) => { if (m == Mat.Ivy) ivy++; };
                for (int i = 0; i < 1200 && ivy < 60; i++)
                {
                    Advance(.05);
                    if (i % 20 == 0) (shaman.X, shaman.Z) = (sapper.X - 2 * M, sapper.Z);
                }
                sx = sapper.X; sz = sapper.Z;
                break;
            }
            case 2:
                for (int i = 0; i < 6; i++) Put(Kinds.Swordsman, gx + i % 3 * 1.6f * M, gz + i / 3 * 1.6f * M);
                var sh = Put(Kinds.Shaman, gx - 10 * M, gz);
                sh.Hp = sh.MaxHp = 5000;
                for (int i = 0; i < 300 && sim.Miasmas.Count == 0; i++) Advance(.05);
                Advance(2);
                sx = gx - 2 * M;
                break;
            case 3:
                sim.HordeFaith = 96;
                Hud.OpenPanel("horde");
                break;
        }
        Paused = true;
        _shotFrames = Math.Max(_shotFrames, 60);
        float y = w.WY(w.SurfAt((int)sx, (int)sz)) + 2;
        float yaw = _cam is { Length: > 0 } ? _cam[0] : -1.1f;
        Rig.SetAt(w.WX(sx), y, w.WZ(sz), yaw, .42f, _look == 0 ? 4.5f : 3.2f);
        Hud.Refresh();
    }
}
