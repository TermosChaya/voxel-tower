using System;
using System.Collections.Generic;
using VoxelTower.Core;

namespace VoxelTower.Game;

public partial class Main
{
    /// <summary>
    /// A warlord's army at the walls (--pose=warlord, --at=seconds into the fight, 14 by itself): a ring of stone wall
    /// round the citadel, a commander with a line of spearmen and swordsmen before it and archers on the wall, and an
    /// army of the horde's realm come in from the west by day, a warlord of the third level at its head with his guard.
    /// --look=1 close on the warlord; --look=2 the warlord killed, his army run and gone, the raid's report up;
    /// --look=3 a raid two hundred strong on the walls, for how fast it is drawn (--perf).
    /// </summary>
    void WarlordPose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX, kz = s.KeepZ, line = kx - s.KeepR - 70;
        sim.InstantLabour = true;
        int top = Shapes.LevelTop(w, kx - 64, kz, 40);
        sim.DesignateBuild(Shapes.Circle(w, s, kx, kz, s.KeepR + 44, new Stroke { Mat = Mat.Stone, Width = 10, Height = 40, Level = true, Hollow = true }, top));
        sim.InstantLabour = false;
        Unit Stand(UnitType t, int x, int z)
        {
            int c = sim.NearestStand(x, z, t.Move.Height);
            if (c < 0) return null;
            var u = sim.Spawn(t, c);
            sim.Command(new[] { u }, c);
            u.Facing = MathF.PI;
            return u;
        }
        var foot = new[] { Kinds.Spearman, Kinds.Swordsman, Kinds.Shieldbearer, Kinds.Spearman, Kinds.Knight, Kinds.Swordsman, Kinds.Spearman };
        for (int i = 0; i < foot.Length; i++) Stand(foot[i], line, kz - 30 + i * 10);
        // the archers on the wall's western arc
        foreach (var (dx, dz) in new[] { (-62, -14), (-60, 18), (-52, -36), (-48, 40), (-63, 2) }) Stand(Kinds.Archer, kx + dx, kz + dz);
        var cap = Stand(Kinds.Captain, line + 26, kz);
        if (cap != null) sim.Command(new[] { cap }, cap.Cell);
        Advance(1);
        // the army: a warlord with his guard and two bands, come in a little way off the line rather than at the
        // valley's far edge, so the fight is soon
        var lord = sim.NewWarlord(3);
        bool host = _look == 3;
        var roster = new List<UnitType>();
        void Add(UnitType t, int n) { for (int i = 0; i < n; i++) roster.Add(t); }
        if (host) { Add(Kinds.Goblin, 110); Add(Kinds.Orc, 50); Add(Kinds.Sapper, 16); Add(Kinds.Troll, 10); Add(Kinds.Ram, 4); }
        else { Add(Kinds.Orc, 5); Add(Kinds.Goblin, 8); Add(Kinds.Sapper, 2); Add(Kinds.Troll, 1); }
        sim.HordeStock[Stockpile.Wood] = 24 * VoxelWorld.PerM3;
        sim.Raid(roster, Sim.WestEdge, .6, lord);
        int k = 0;
        foreach (var b in sim.Bands)
        {
            if (b.Lord != lord) continue;
            int dz = b.LordsOwn ? 0 : (k % 2 == 0 ? -1 : 1) * (36 + k / 2 * 30);
            b.Entry = sim.NearestStand(line - (host ? 150 : 110) - (b.LordsOwn ? 20 : 0), kz + dz, 4);
            if (!b.LordsOwn) k++;
        }
        sim.ComputeFields();
        Advance(_poseSeconds);
        Unit at = null;
        foreach (var u in sim.Units) if (u.Lord == lord && !u.Dead) at = u;
        if (_look == 2)
        {
            if (at != null) sim.Hurt(at, null, 1e6f, "пробит насквозь");
            for (int i = 0; i < 300 && sim.Raids.Active; i++) Advance(1);
        }
        if (_look == 1 && at != null) Rig.Set(World.WX(at.X + 6), World.WZ(at.Z + 4), -.75f, .5f, 3.4f, snap: true);
        else if (at != null && _look != 2) Rig.Set(World.WX((at.X + line) / 2), World.WZ((at.Z + kz) / 2), -.75f, .55f, host ? 1.2f : 1.7f, snap: true);
        else Rig.Set(World.WX(line - 34), World.WZ(kz + 6), -.75f, .55f, host ? 1.2f : 1.6f, snap: true);
        Hud.Refresh();
        Marks.Flush();
    }

    /// <summary>
    /// The warlords' kinds and the line across a gate (--pose=lords): a ring of stone wall round the citadel with
    /// archers on its western arc and a line of foot inside, and an army from the west led by a warlord of a kind.
    /// --look=1 a troll warlord with a boulder over his head and another in the air at the wall; --look=2 a shaman
    /// warlord's curse on a stretch of the wall, cracked and crumbling; --look=3 a goblin warlord's band lying in wait in
    /// the grass; --look=4 the same risen on three of ours walking by; --look=5 the line across an open way through the
    /// ring, shields in front, an orc warlord's army coming at it.
    /// </summary>
    void LordsPose()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX, kz = s.KeepZ, r = s.KeepR + 44, line = kx - r + 30;
        sim.InstantLabour = true;
        int top = Shapes.LevelTop(w, kx - 64, kz, 40);
        var ring = Shapes.Circle(w, s, kx, kz, r, new Stroke { Mat = Mat.Stone, Width = 10, Height = 40, Level = true, Hollow = true }, top);
        if (_look == 5)
        {
            // a way 3 m wide left open through the western arc
            var open = new VoxelList();
            for (int i = 0; i < ring.Count; i++) if (ring.X(i) > kx || Math.Abs(ring.Z(i) - kz) >= 12) open.Add(ring.X(i), ring.Y(i), ring.Z(i), ring.M(i));
            ring = open;
        }
        sim.DesignateBuild(ring);
        sim.InstantLabour = false;
        Unit Stand(UnitType t, int x, int z)
        {
            int c = sim.NearestStand(x, z, t.Move.Height);
            if (c < 0) return null;
            var u = sim.Spawn(t, c);
            sim.Command(new[] { u }, c);
            u.Facing = MathF.PI;
            return u;
        }
        if (_look is 1 or 2)
            foreach (var (dx, dz) in new[] { (-62, -14), (-60, 18), (-63, 2), (-61, -6) }) Stand(Kinds.Archer, kx + dx, kz + dz);
        var foot = new[] { Kinds.Shieldbearer, Kinds.Spearman, Kinds.Swordsman, Kinds.Shieldbearer, Kinds.Spearman, Kinds.Halberdier, Kinds.Shieldbearer, Kinds.Swordsman };
        if (_look == 5)
        {
            for (int i = 0; i < foot.Length; i++) { var u = Stand(foot[i], kx - 20 + i % 4 * 10, kz + 40 + i / 4 * 10); if (u != null) u.Hold = false; }
            var cap = Stand(Kinds.Captain, kx - 10, kz + 30);
            if (cap != null) cap.Hold = false;
        }
        Advance(1);
        var kind = _look switch { 1 => Kinds.Troll, 2 => Kinds.Shaman, 3 or 4 => Kinds.Goblin, _ => Kinds.Orc };
        var lord = sim.NewWarlord(3, kind);
        var roster = new List<UnitType>();
        void Add(UnitType t, int n) { for (int i = 0; i < n; i++) roster.Add(t); }
        Add(Kinds.Orc, 4); Add(Kinds.Goblin, 10);
        sim.Raid(roster, Sim.WestEdge, .6, lord);
        int k = 0;
        foreach (var b in sim.Bands)
        {
            if (b.Lord != lord) continue;
            int dz = b.LordsOwn ? 0 : (k % 2 == 0 ? -1 : 1) * (36 + k / 2 * 30);
            b.Entry = sim.NearestStand(kx - r - 150 - (b.LordsOwn ? 20 : 0), kz + dz, 4);
            if (!b.LordsOwn) k++;
        }
        sim.ComputeFields();
        Unit Lord() { foreach (var u in sim.Units) if (u.Lord == lord && !u.Dead) return u; return null; }
        switch (_look)
        {
            case 1:
            {
                // one stone in the air, the next over his head
                Flier flying = null;
                for (int t = 0; t < 900 && flying == null; t++) { Advance(.1); flying = sim.Fliers.Find(f => f.Cause == Sim.LordStoneCause && f.Carrier == null); }
                for (int t = 0; t < 120 && sim.Fliers.Contains(flying); t++) Advance(.05);
                var at = Lord();
                if (at != null) at.SlamReady = 0;
                for (int t = 0; t < 60 && at != null && !(sim.Hurling(at) && sim.Time - at.HurlFrom > Sim.HurlRaise - .1); t++) Advance(.05);
                if (at != null) Rig.Set(World.WX((at.X + kx - r) / 2), World.WZ((at.Z + kz) / 2), -.75f, .55f, 1.7f, snap: true);
                break;
            }
            case 2:
            {
                for (int t = 0; t < 900 && sim.LordCurses == 0; t++) Advance(.1);
                Advance(8);
                var c = sim.Curses.Find(cc => cc.Stone);
                if (c != null) Rig.SetAt(World.WX(c.X - 30), World.WY(c.Y), World.WZ(c.Z + 10), -.9f, .5f, 2.6f);
                break;
            }
            case 3:
            case 4:
            {
                Warband lurk = sim.Bands.Find(b => b.Lurk);
                for (int t = 0; t < 600 && !(lurk is { State: BandState.Lurk } && lurk.Members.FindAll(u => u.Lurking).Count >= 4); t++) Advance(.25);
                if (lurk == null || lurk.Leader == null) break;
                var (lx, _, lz) = sim.Nav.Feet(lurk.LurkCell);
                if (_look == 4)
                {
                    // three of ours walking by it
                    var by = new List<Unit>();
                    for (int i = 0; i < 3; i++)
                    {
                        int c = sim.NearestStand((int)lx + 90 + i * 10, (int)lz + 30, 2);
                        if (c >= 0) by.Add(sim.Spawn(i == 1 ? Kinds.Spearman : Kinds.Swordsman, c));
                    }
                    sim.Command(by, sim.NearestStand((int)lx - 60, (int)lz + 20, 2));
                    for (int t = 0; t < 400 && sim.AmbushesSprung == 0; t++) Advance(.05);
                    Advance(1.2);
                }
                Rig.Set(World.WX(lx + 8), World.WZ(lz + 6), -.75f, .55f, 2.6f, snap: true);
                break;
            }
            default:
            {
                // the line formed across the way in and stood to, and the first of the horde at it
                for (int t = 0; t < 600 && sim.CaptainAcross == 0; t++) Advance(.1);
                Advance(6);
                for (int t = 0; t < 600; t++)
                {
                    bool there = false;
                    foreach (var u in sim.Units) if (!u.Dead && u.Team == Team.Horde && MathF.Abs(u.X - (kx - r)) < 120 && MathF.Abs(u.Z - kz) < 60) there = true;
                    if (there) break;
                    Advance(.1);
                }
                Godot.GD.Print($"[lords] lines across {sim.CaptainAcross}, formed {sim.CaptainFormed}, at {sim.Time:0} s");
                Rig.SetAt(World.WX(kx - r + 12), World.WY(World.SurfAt(kx - r - 30, kz)), World.WZ(kz), -1.2f, 1.05f, 2.4f);
                break;
            }
        }
        Hud.Refresh();
        Marks.Flush();
    }
}
