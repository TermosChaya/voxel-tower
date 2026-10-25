using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>The set-ups for a close look at the figures (--pose=people, beasts, walk, duel, reel, troops…) and the dawn, dusk and works cards.</summary>
public partial class Main
{
    /// <summary>
    /// Everyone in every look in rows on cleared ground, facing the camera, for a close look at the figures (--pose=people):
    /// the workers, one swinging his pick; archers and swordsmen, every other swordsman with his shield up; goblins and
    /// sappers; orcs and miners; trolls and the beasts; the engines; and the troops the lore opens, every other one as it
    /// fights: spearmen, shieldbearers, halberdiers and knights; slingers, crossbowmen, bombardiers and hounds; the
    /// riders; a tame troll and the giants. --look=0..9 frames one row.
    /// </summary>
    void People()
    {
        var sim = Sim;
        sim.Waves.Countdown = 1e9;
        // the figures stand as they are put: nothing moves but their breathing
        Paused = true;
        var rows = new[]
        {
            new[] { Kinds.Worker, Kinds.Priest }, new[] { Kinds.Archer, Kinds.Swordsman, Kinds.Risen, Kinds.Thrall }, new[] { Kinds.Goblin, Kinds.Sapper, Kinds.Thief, Kinds.Shaman, Kinds.Witch, Kinds.Alchemist },
            new[] { Kinds.Orc, Kinds.Miner, Kinds.Necromancer }, new[] { Kinds.Troll, Kinds.Deer, Kinds.Boar, Kinds.Hare, Kinds.Bear, Kinds.WhiteStag }, new[] { Kinds.Ram, Kinds.Catapult, Kinds.Mantlet, Kinds.SiegeTower },
            // the troops the lore opens: men on foot; men who throw and shoot, and the hounds; the riders; the great ones
            new[] { Kinds.Spearman, Kinds.Shieldbearer, Kinds.Halberdier, Kinds.Knight }, new[] { Kinds.Slinger, Kinds.Crossbowman, Kinds.Bombardier, Kinds.OurAlchemist, Kinds.Hound },
            new[] { Kinds.Rider, Kinds.HorseArcher, Kinds.Lancer }, new[] { Kinds.TameTroll, Kinds.Giant, Kinds.HillGiant },
        };
        const float yaw = -.3f, pitch = .42f, gap = 34;
        float sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
        var (gx, gz) = GroveAt;
        float cx = gx + 150, cz = gz + 110;
        bool one = _look >= 0 && _look < rows.Length;
        float width = 0;
        for (int r = 0; r < rows.Length; r++)
        {
            float span = 0;
            foreach (var t in rows[r]) span += Bodies.Looks(t) * Spacing(t);
            if (!one || r == _look) width = Math.Max(width, span);
            // the rows run across the view, one behind the other going away from the camera
            float along = -span / 2, back = (r - (one ? _look : 2)) * gap;
            foreach (var t in rows[r])
                for (int l = 0; l < Bodies.Looks(t); l++)
                {
                    along += Spacing(t) / 2;
                    float x = cx + along * cos - back * sin, z = cz - along * sin - back * cos;
                    var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height));
                    u.Look = l;
                    u.X = x; u.Z = z; u.Y = World.SurfAt((int)x, (int)z);
                    u.Facing = MathF.Atan2(cos, sin);
                    u.Hold = true;
                    if (t == Kinds.Swordsman && l % 2 == 1) { u.State = UnitState.Fight; u.Cooldown = 1; }
                    // the troops turned three-quarters, so that a spear or a lance shows, and every other one as it
                    // fights: a shield up, a crossbow levelled, a sling whirling
                    if (Array.IndexOf(Kinds.Troops, t) >= 0)
                    {
                        u.Facing -= .95f;
                        if (l % 2 == 1) { u.State = UnitState.Fight; u.Cooldown = .5f; }
                    }
                    if (t == Kinds.Worker && l == 3) u.State = UnitState.Work;
                    along += Spacing(t) / 2;
                }
        }
        static float Spacing(UnitType t) => Kinds.IsEngine(t) ? 34 : t.BodyH >= 24 ? 20 : t.Mounted ? 16 : t == Kinds.Shieldbearer ? 13 : t == Kinds.Deer || t == Kinds.WhiteStag ? 14 : t == Kinds.Bear ? 18 : t == Kinds.Hare ? 6 : t.BodyH <= 10 ? 9 : 11;
        // the camera looks at a plane 4 m up: the figures' middles are higher, so look past it by as much
        float over = (World.WY(World.SurfAt((int)cx, (int)cz) + 8) - 4) / MathF.Tan(pitch);
        float zoom = Math.Clamp(248 / (width / 1.6f + 12), 1.2f, 9);
        Rig.Set(World.WX(cx) - sin * over, World.WZ(cz) - cos * over, yaw, pitch, one ? zoom : 1.6f, snap: true);
        GD.Print($"[voxel-tower] the figures stand round {cx:0},{cz:0}, {gap:0} voxels a row; closer: --cam={cx - sin * over / VoxelWorld.Vox:0},{cz - cos * over / VoxelWorld.Vox:0},-0.3,0.42,6");
        Hud.Refresh();
    }

    /// <summary>
    /// The beasts close up and going (--pose=beasts): deer at a walk, a white stag with them, deer and a hare bolting,
    /// boars trotting, and deer grazing in front; each set off so far back that it is before the camera 1.3 s on
    /// (--frames=80). The household waits in the citadel, so that nothing takes fright at it. --look=1 the stag walking
    /// close, 2 a deer bolting close, 3 a boar trotting close.
    /// </summary>
    void Beasts()
    {
        var sim = Sim;
        sim.Waves.Countdown = 1e9;
        sim.WildTarget = 0;
        foreach (var w in sim.Units)
        {
            if (w.Team == Team.Wild) { w.Dead = true; w.DiedAt = -999; }
            else if (w.Team == Team.Player) w.Inside = true;
        }
        const float yaw = -.3f, pitch = .42f, gap = 20;
        float sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
        var (gx, gz) = GroveAt;
        float cx = gx + 150, cz = gz + 110;
        // (kind, look, how it goes: 0 at a walk, 1 bolting, 2 grazing), a row to each, going along the rows
        var rows = new[]
        {
            new[] { (Kinds.Deer, 0, 0), (Kinds.Deer, 1, 0), (Kinds.WhiteStag, 0, 0), (Kinds.Deer, 2, 0) },
            new[] { (Kinds.Deer, 0, 1), (Kinds.Deer, 1, 1), (Kinds.Hare, 0, 1), (Kinds.Hare, 0, 0) },
            new[] { (Kinds.Boar, 0, 0), (Kinds.Hare, 0, 0), (Kinds.WhiteStag, 0, 1), (Kinds.Boar, 0, 1) },
            new[] { (Kinds.Deer, 1, 2), (Kinds.Deer, 0, 2), (Kinds.Boar, 0, 2), (Kinds.Hare, 0, 2) },
        };
        (float x, float z) close = (cx, cz);
        for (int r = 0; r < rows.Length; r++)
            for (int i = 0; i < rows[r].Length; i++)
            {
                var (t, look, how) = rows[r][i];
                float speed = how == 2 ? 0 : t.Move.Speed * NavGrid.CS * (how == 0 && t.Team == Team.Wild ? .35f : 1);
                float along = (i - 1.5f) * 15 - speed * 1.3f, back = (r - 1.5f) * gap;
                float x = cx + along * cos - back * sin, z = cz - along * sin - back * cos;
                // where it will be 1.3 s on, for the camera close up
                if ((_look, r, i) is (1, 0, 0) or (2, 1, 0) or (3, 2, 0)) close = (x + speed * 1.3f * cos, z - speed * 1.3f * sin);
                var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height));
                u.Look = look;
                u.Facing = MathF.Atan2(-sin, cos);
                if (how == 2) { u.Home = u.Cell; u.NextSearch = 1e9; continue; }
                int to = sim.NearestStand((int)(x + 400 * cos), (int)(z - 400 * sin), t.Move.Height);
                if (t.Team == Team.Player) { sim.Command(new[] { u }, to); continue; }
                sim.GoTo(u, to, budget: 20000);
                u.Home = to;
                if (how == 1) { u.State = UnitState.Flee; u.LastHurt = sim.Time + 60; u.NextSearch = 1e9; }
            }
        // (the view on the rows' middle, a row nearer than the middle of them)
        float over = (World.WY(World.SurfAt((int)cx, (int)cz) + 8) - 4) / MathF.Tan(pitch), near = gap * 1.6f;
        if (_look is >= 1 and <= 3)
        {
            float overClose = (World.WY(World.SurfAt((int)close.x, (int)close.z) + 5) - 4) / MathF.Tan(pitch);
            Rig.Set(World.WX(close.x) - sin * overClose, World.WZ(close.z) - cos * overClose, yaw, pitch, 9, snap: true);
        }
        else Rig.Set(World.WX(cx + near * sin) - sin * over, World.WZ(cz + near * cos) - cos * over, yaw, pitch, 3.4f, snap: true);
        GD.Print($"[voxel-tower] the beasts go by {cx:0},{cz:0}; the walking deer close: --cam={cx + gap * 1.5f * sin - sin * over / VoxelWorld.Vox:0},{cz + gap * 1.5f * cos - cos * over / VoxelWorld.Vox:0},-0.3,0.42,8");
        Hud.Refresh();
    }

    /// <summary>
    /// People going by close (--pose=walk), to look at how their feet are set down: a worker, a swordsman, an archer, a
    /// spearman and a knight walking across a step of stone half a metre high, each before the camera 1.3 s on
    /// (--frames=80). --look=1 they and a deer, a boar and an orc are felled where they stand on the step's edge and fall
    /// off it limp; --look=2 a blast goes off among them as they lie (--frames=150).
    /// </summary>
    void Walkers()
    {
        var sim = Sim;
        sim.Waves.Countdown = 1e9;
        foreach (var w in sim.Units)
        {
            if (w.Team == Team.Wild) { w.Dead = true; w.DiedAt = -999; }
            // the household kept in, out of the way of the dead
            else if (_look is 1 or 2 && w.Team == Team.Player) w.Inside = true;
        }
        const float yaw = -.3f, pitch = .42f, gap = 14;
        float sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
        var (gx, gz) = GroveAt;
        float cx = gx + 150, cz = gz + 110;
        // the step: a slab of stone four voxels high across the way, a metre and a half along it, on the ground
        for (int x = (int)cx - 50; x <= (int)cx + 50; x++)
            for (int z = (int)cz - 6; z <= (int)cz + 6; z++)
            {
                int top = World.SurfAt(x, z);
                while (top > 0 && !NavGrid.Blocks(World.Get(x, top, z))) top--;
                for (int y = top + 1; y <= top + 4; y++) World.Set(x, y, z, Mat.Stone);
            }
        sim.Nav.Flush();
        var kinds = new[] { Kinds.Worker, Kinds.Swordsman, Kinds.Archer, Kinds.Spearman, Kinds.Knight };
        if (_look is 1 or 2)
        {
            // felled on the step's edge, one after another, each struck from its side so that it goes over the edge
            var felled = new[] { Kinds.Worker, Kinds.Swordsman, Kinds.Deer, Kinds.Orc, Kinds.Knight, Kinds.Boar, Kinds.Archer };
            var down = new List<Unit>();
            for (int i = 0; i < felled.Length; i++)
            {
                var t = felled[i];
                float x = cx - 30 + i * 10, z = cz + 5;
                var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height));
                u.X = x; u.Z = z; u.Y = World.SurfAt((int)x, (int)z) + 1;
                u.Facing = MathF.PI / 2;
                u.Hold = true;
                down.Add(u);
            }
            Advance(.4);
            foreach (var u in down) sim.Hurt(u, null, 9999, "test");
            // the blast a second on, with the bodies down, on the step behind them (it is seen by the frames, not stepped past)
            if (_look == 2) GetTree().CreateTimer(1).Timeout += () => sim.Detonate(cx, World.SurfAt((int)cx, (int)cz - 2) + 1, cz - 2, 1.2f * VoxelWorld.PerMetre, 0, "test", always: true);
            float overF = (World.WY(World.SurfAt((int)cx, (int)cz) + 4) - 4) / MathF.Tan(pitch);
            Rig.Set(World.WX(cx) - sin * overF, World.WZ(cz + 5) - cos * overF, yaw, pitch, 4.5f, snap: true);
            Hud.Refresh();
            return;
        }
        for (int i = 0; i < kinds.Length; i++)
        {
            var t = kinds[i];
            float speed = t.Move.Speed * NavGrid.CS, along = -speed * 1.3f + 4, back = (i - 2) * gap;
            float x = cx + along * cos - back * sin, z = cz - along * sin - back * cos;
            var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height));
            u.Facing = MathF.Atan2(-sin, cos);
            sim.Command(new[] { u }, sim.NearestStand((int)(x + 300 * cos), (int)(z - 300 * sin), t.Move.Height));
        }
        float over = (World.WY(World.SurfAt((int)cx, (int)cz) + 8) - 4) / MathF.Tan(pitch);
        Rig.Set(World.WX(cx) - sin * over, World.WZ(cz) - cos * over, yaw, pitch, 4.5f, snap: true);
        Hud.Refresh();
    }

    /// <summary>
    /// Pairs fighting hand to hand on cleared ground, for a close look at the blows (--pose=duel), each with a stroke of
    /// its own: a swordsman cutting down at an orc, a knight across at one, a swordsman sweeping at one, a halberd
    /// chopping, a spear thrust, a shield driven in, an orc's backhand at a swordsman, a knight's rising cut; a troll's
    /// club, a rider from the saddle, a hound's teeth, a giant's maul. --look=0 they fight (--at=seconds); with the game
    /// stopped, 1 every striker drawn back for its stroke, 2 the moment the blade meets the body, 3 on through it; 4 and
    /// on one pair close, at the moment it meets (--at=seconds after the blow, under one, for another moment; below
    /// nought, so long before it: the blade coming down in the last 0.085 s, drawing back before that).
    /// </summary>
    void Duel()
    {
        var sim = Sim;
        sim.Waves.Countdown = 1e9;
        var pairs = new (UnitType a, UnitType b, StrokeKind k)[]
        {
            (Kinds.Swordsman, Kinds.Orc, StrokeKind.Overhead), (Kinds.Knight, Kinds.Orc, StrokeKind.DiagR), (Kinds.Swordsman, Kinds.Orc, StrokeKind.Sweep), (Kinds.Halberdier, Kinds.Orc, StrokeKind.PoleChop),
            (Kinds.Spearman, Kinds.Orc, StrokeKind.Thrust), (Kinds.Shieldbearer, Kinds.Goblin, StrokeKind.Bash), (Kinds.Orc, Kinds.Swordsman, StrokeKind.DiagL), (Kinds.Knight, Kinds.Goblin, StrokeKind.Rising),
            (Kinds.Troll, Kinds.Knight, StrokeKind.Smash), (Kinds.Rider, Kinds.Goblin, StrokeKind.Overhead), (Kinds.Hound, Kinds.Goblin, StrokeKind.Bite), (Kinds.Giant, Kinds.Orc, StrokeKind.Sweep),
        };
        const float yaw = -.3f, pitch = .42f, apart = 44, gap = 36;
        float sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
        var (gx, gz) = GroveAt;
        float cx = gx + 150, cz = gz + 110;
        int close = _look >= 4 && _look < 4 + pairs.Length ? _look - 4 : -1;
        var strikers = new List<(Unit a, Unit b, StrokeKind k)>();
        for (int i = 0; i < pairs.Length; i++)
        {
            var (ta, tb, k) = pairs[i];
            // four pairs to a row, the rows going away from the camera; the striker on the left, facing on along the row
            // to the right, so that its sword arm is toward the camera and its shield away from it — but one driving
            // its shield in, on the right with its shield toward the camera
            float along = (i % 4 - 1.5f) * apart, back = (i / 4 - 1) * gap, side = k == StrokeKind.Bash ? -1 : 1;
            float d = k == StrokeKind.Bash ? ta.BodyR + tb.BodyR + 3 : ta.Reach + tb.BodyR - 2;
            Unit Put(UnitType t, float al, float face)
            {
                float x = cx + al * cos - back * sin, z = cz - al * sin - back * cos;
                var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height));
                u.X = x; u.Z = z; u.Y = World.SurfAt((int)x, (int)z);
                u.Facing = face; u.Look = 0;
                u.Hp = u.MaxHp = 100000;
                if (t.Team == Team.Horde) { u.Guard = true; u.Home = u.Cell; } else { u.Hold = true; u.Post = u.Cell; u.Dest = u.Cell; }
                return u;
            }
            var a = Put(ta, along - side * d / 2, MathF.Atan2(-sin * side, cos * side));
            var b = Put(tb, along + side * d / 2, MathF.Atan2(sin * side, -cos * side));
            a.Target = b; b.Target = a;
            strikers.Add((a, b, k));
        }
        if (_look <= 0) Advance(_poseSeconds > 0 && _poseSeconds < 6 ? _poseSeconds : 2.5);
        else
        {
            // the game stopped at one moment of every blow (close up, --at=seconds after it, or before it below nought)
            Paused = true;
            bool at = close >= 0 && _poseSeconds > -1 && _poseSeconds < 1;
            float since = at ? (float)_poseSeconds : _look == 3 ? UnitView.FollowTime * .6f : 0;
            foreach (var (a, b, k) in strikers)
            {
                a.State = b.State = UnitState.Fight;
                a.NextStroke = k; a.StrokeFor = b;
                b.Cooldown = b.T.AttackTime;
                // drawn back all the way, the blade about to start down; or so long before the blow
                if (_look == 1 || since < 0) { a.Cooldown = _look == 1 ? UnitView.StrikeTime : -since; a.StrokeTime = -99; continue; }
                sim.Strike(a, b);
                a.Cooldown = a.T.AttackTime;
                a.StrokeTime = sim.Time - since;
            }
        }
        if (close >= 0)
        {
            var (a, b, _) = strikers[close];
            float mx = (a.X + b.X) / 2, mz = (a.Z + b.Z) / 2;
            float over = (World.WY(a.Y + 8) - 4) / MathF.Tan(pitch);
            Rig.Set(World.WX(mx) - sin * over, World.WZ(mz) - cos * over, yaw, pitch, a.T.BodyH > 18 || b.T.BodyH > 18 ? 6 : 8.5f, snap: true);
        }
        else
        {
            float over = (World.WY(World.SurfAt((int)cx, (int)cz) + 8) - 4) / MathF.Tan(pitch);
            Rig.Set(World.WX(cx) - sin * over, World.WZ(cz) - cos * over, yaw, pitch, 2.6f, snap: true);
        }
        var told = new List<string>();
        foreach (var (a, b, k) in strikers) told.Add($"{a.T.Key} {k} at {b.T.Key}");
        GD.Print($"[voxel-tower] the pairs fight round {cx:0},{cz:0}: {string.Join(", ", told)}");
        Hud.Refresh();
    }

    /// <summary>
    /// Pairs fighting hand to hand (--pose=reel), the game stopped just as the one struck at takes the blow its own way:
    /// an orc stepping back from a sword coming down, a goblin leaning out from under a slanting cut, one ducking a
    /// sweep, a shield taking an orc's axe, a swordsman turning one with his blade; an orc reeling from a knight's
    /// thrust, a goblin with its hand to its arm and its head swimming, one off its feet. --at=seconds since the blow
    /// (0.15 by itself); --look=4 and on one pair close.
    /// </summary>
    void Reeling()
    {
        var sim = Sim;
        sim.Waves.Countdown = 1e9;
        var pairs = new (UnitType a, UnitType b, StrokeKind k, Guarded g)[]
        {
            (Kinds.Swordsman, Kinds.Orc, StrokeKind.Overhead, Guarded.StepBack), (Kinds.Swordsman, Kinds.Goblin, StrokeKind.DiagR, Guarded.Dodge),
            (Kinds.Knight, Kinds.Goblin, StrokeKind.Sweep, Guarded.Duck), (Kinds.Orc, Kinds.Shieldbearer, StrokeKind.DiagL, Guarded.Block),
            (Kinds.Orc, Kinds.Swordsman, StrokeKind.Overhead, Guarded.Parry), (Kinds.Knight, Kinds.Orc, StrokeKind.Thrust, Guarded.None),
            (Kinds.Spearman, Kinds.Goblin, StrokeKind.Thrust, Guarded.None), (Kinds.Halberdier, Kinds.Goblin, StrokeKind.PoleChop, Guarded.None),
        };
        const float yaw = -.3f, pitch = .42f, apart = 44, gap = 36;
        float sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
        var (gx, gz) = GroveAt;
        float cx = gx + 150, cz = gz + 110;
        float since = _poseSeconds > 0 && _poseSeconds < 1 ? (float)_poseSeconds : .15f;
        int close = _look >= 4 && _look < 4 + pairs.Length ? _look - 4 : -1;
        var put = new List<(Unit a, Unit b)>();
        Paused = true;
        for (int i = 0; i < pairs.Length; i++)
        {
            var (ta, tb, k, g) = pairs[i];
            // four pairs to a row, as in the duels: the striker on the left, its sword arm toward the camera
            float along = (i % 4 - 1.5f) * apart, back = (i / 4 - .5f) * gap, d = ta.Reach + tb.BodyR - 2;
            Unit Put(UnitType t, float al, float face)
            {
                float x = cx + al * cos - back * sin, z = cz - al * sin - back * cos;
                var u = sim.Spawn(t, sim.NearestStand((int)x, (int)z, t.Move.Height));
                u.X = x; u.Z = z; u.Y = World.SurfAt((int)x, (int)z);
                u.Facing = face; u.Look = 0;
                u.Hp = u.MaxHp = 100000;
                if (t.Team == Team.Horde) { u.Guard = true; u.Home = u.Cell; } else { u.Hold = true; u.Post = u.Cell; u.Dest = u.Cell; }
                return u;
            }
            var a = Put(ta, along - d / 2, MathF.Atan2(-sin, cos));
            var b = Put(tb, along + d / 2, MathF.Atan2(sin, -cos));
            a.Target = b; b.Target = a;
            a.State = b.State = UnitState.Fight;
            b.Cooldown = b.T.AttackTime;
            if (g is Guarded.StepBack or Guarded.Dodge or Guarded.Duck) sim.Missed(a, b, k, g);
            else if (g is Guarded.Block or Guarded.Parry) sim.Turned(a, b, k, g);
            else sim.Strike(a, b, .55f, .5f, k);
            a.Cooldown = a.T.AttackTime;
            a.StrokeTime = sim.Time - since;
            b.ReactAt = sim.Time - since;
            b.StagX = b.StagZ = 0;
            // what the blows that struck home did, held at their worst
            if (i == 5) b.StaggerUntil = sim.Time + .5;
            if (i == 6) { b.Clutch = (byte)BodyPart.Off; b.ClutchUntil = sim.Time + 3; b.DazedUntil = sim.Time + 3; b.Pain = .5f; }
            if (i == 7) { b.Down = true; b.DownSince = sim.Time - 2; b.DownUntil = double.PositiveInfinity; b.State = UnitState.Idle; }
            put.Add((a, b));
        }
        if (close >= 0)
        {
            var (a, b) = put[close];
            float mx = (a.X + b.X) / 2, mz = (a.Z + b.Z) / 2;
            float over = (World.WY(a.Y + 8) - 4) / MathF.Tan(pitch);
            Rig.Set(World.WX(mx) - sin * over, World.WZ(mz) - cos * over, yaw, pitch, 8.5f, snap: true);
        }
        else
        {
            float over = (World.WY(World.SurfAt((int)cx, (int)cz) + 8) - 4) / MathF.Tan(pitch);
            Rig.Set(World.WX(cx) - sin * over, World.WZ(cz) - cos * over, yaw, pitch, 3.4f, snap: true);
        }
        Hud.Refresh();
    }

    /// <summary>
    /// The dawn after a first night as a player of one's own sees it (--pose=dawn): a band of three with its leader
    /// cut down by the archer and the swordsman, nobody of ours lost, and the gifts to choose from.
    /// </summary>
    void DawnPose()
    {
        Sim.BeginErrands();
        Sim.Waves.Number = 1;
        Sim.NightBegins(1);
        var archer = Sim.Units.Find(u => u.T == Kinds.Archer);
        var sword = Sim.Units.Find(u => u.T == Kinds.Swordsman);
        var (gx, _, gz) = Sim.Nav.Feet(Sim.Gate);
        Sim.Waves.Active = true;
        for (int i = 0; i < 3; i++)
        {
            var g = Sim.Spawn(Kinds.Goblin, Sim.NearestStand((int)gx - 60, (int)gz + i * 6, 2));
            if (i == 0) g.Leader = true;
            // --look=1: the first chapter's champion among them
            if (i == 0 && _look == 1) { g.Champion = 1; g.Title = "Гнилозуб"; }
            Sim.Hurt(g, i == 1 ? sword : archer, 1000, null);
        }
        Sim.Waves.Active = false;
        Sim.Dawn(_look == 1 ? 3 : 1);
        Hud.Refresh();
    }

    /// <summary>
    /// The siege's first dusk as a player of one's own sees it (--pose=dusk; --at=seconds before the horn, 40 by itself):
    /// the errands on the left, the card of what comes tonight under the beam.
    /// </summary>
    void Dusk()
    {
        Sim.BeginErrands();
        // --look=n: the dusk before night n (3 — the first chapter's champion comes)
        if (_look > 1)
        {
            Sim.Waves.Number = _look - 1;
            Sim.PlanWave(_look);
        }
        Sim.Waves.Countdown = _poseSeconds > 0 ? _poseSeconds : 40;
        Hud.Refresh();
    }

    /// <summary>
    /// Work marked and under way (--pose=works, --at=seconds of it, 25 by itself): a stone wall, a ditch and a timber
    /// platform west of the citadel, each with its tag of how far it has got.
    /// </summary>
    void Works()
    {
        var sim = Sim; var s = Site;
        sim.Waves.Countdown = 1e9;
        int kx = s.KeepX - s.KeepR - 40, kz = s.KeepZ;
        foreach (var t in new[] { Kinds.Worker, Kinds.Worker, Kinds.Worker, Kinds.Worker }) sim.Spawn(t, sim.Gate);
        int top = Shapes.LevelTop(World, kx - 20, kz - 30, 16);
        var wall = Shapes.Line(World, s, kx - 20, kz - 50, kx - 20, kz - 10, new Stroke { Mat = Mat.Stone, Width = 6, Height = 16, Level = true }, top);
        sim.DesignateBuild(wall); Progress.Track("Камень", wall, false);
        var ditch = Shapes.Line(World, s, kx - 40, kz + 10, kx - 40, kz + 50, new Stroke { Mat = Mat.Air, Width = 8, Height = 8 }, World.SurfAt(kx - 40, kz + 30));
        sim.DesignateRemove(ditch); Progress.Track("Копать", ditch, true);
        int deck = Shapes.LevelTop(World, kx, kz + 30, 6);
        var plat = Shapes.Box(World, s, kx - 6, kz + 20, kx + 6, kz + 40, new Stroke { Mat = Mat.Wood, Height = 6, Level = true }, deck);
        sim.DesignateBuild(plat); Progress.Track("Брёвна", plat, false);
        Advance(_poseSeconds > 0 ? _poseSeconds : 25);
        const float yaw = -.3f, pitch = .62f;
        float over = (World.WY(top) - 4) / MathF.Tan(pitch);
        Rig.Set(World.WX(kx - 20) - MathF.Sin(yaw) * over, World.WZ(kz) - MathF.Cos(yaw) * over, yaw, pitch, 1.6f, snap: true);
        Hud.Refresh();
    }

    /// <summary>
    /// Blood spilt (--pose=blood, --at=seconds of fighting, 14 by itself): two swordsmen and a knight against goblins and
    /// an orc on cleared ground, close; the blows spatter the grass, the wounded drip as they go, the dead lie in their pools.
    /// </summary>
    void Bloodshed()
    {
        var sim = Sim;
        sim.Waves.Countdown = 1e9;
        var (gx, gz) = GroveAt;
        float cx = gx + 150, cz = gz + 110;
        void Stand(UnitType t, float x, float z)
        {
            var u = sim.Spawn(t, sim.NearestStand((int)(cx + x), (int)(cz + z), t.Move.Height));
            if (t.Team == Team.Player) { u.Hold = true; u.Post = u.Dest = u.Cell; } else { u.Guard = true; u.Home = u.Cell; }
        }
        Stand(Kinds.Swordsman, -12, -10); Stand(Kinds.Swordsman, -12, 10); Stand(Kinds.Knight, -16, 0);
        Stand(Kinds.Goblin, 2, -12); Stand(Kinds.Goblin, 2, 12); Stand(Kinds.Goblin, 0, 0); Stand(Kinds.Goblin, 6, 6); Stand(Kinds.Orc, 6, -4);
        Advance(_poseSeconds > 0 ? _poseSeconds : 14);
        const float yaw = -.3f, pitch = .5f;
        float over = (World.WY(World.SurfAt((int)cx, (int)cz) + 8) - 4) / MathF.Tan(pitch);
        Rig.Set(World.WX(cx) - MathF.Sin(yaw) * over, World.WZ(cz) - MathF.Cos(yaw) * over, yaw, pitch, 4, snap: true);
        Hud.Refresh();
    }

    /// <summary>
    /// The troops the lore opens, out before the walls to meet a band (--pose=troops, --at=seconds into the fight): a line
    /// of spearmen, shieldbearers, halberdiers and knights, slingers, crossbowmen and bombardiers behind it, a giant and a
    /// tame troll behind them, the hounds on one wing and the riders, horse archers and lancers on the other; all their
    /// lore learnt. Goblins, orcs, sappers, a troll and a ram come at them from the west. --look=1 frames the line close.
    /// </summary>
    void Troops()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        foreach (string k in new[] { "shieldwall", "longshafts", "leadshot", "bodkins", "firepots", "hunt", "charge", "boulders" }) sim.Learnt.Add(k);
        int kx = s.KeepX, kz = s.KeepZ, line = kx - s.KeepR - 70;
        sim.InstantLabour = true;
        int top = Shapes.LevelTop(w, kx - 64, kz, 40);
        sim.DesignateBuild(Shapes.Circle(w, s, kx, kz, s.KeepR + 44, new Stroke { Mat = Mat.Stone, Width = 10, Height = 40, Level = true, Hollow = true }, top));
        sim.InstantLabour = false;
        void Stand(UnitType t, int x, int z)
        {
            int c = sim.NearestStand(x, z, t.Move.Height);
            if (c < 0) return;
            var u = sim.Spawn(t, c);
            sim.Command(new[] { u }, c);
            u.Facing = MathF.PI;
        }
        var foot = new[] { Kinds.Spearman, Kinds.Shieldbearer, Kinds.Halberdier, Kinds.Knight, Kinds.Spearman, Kinds.Shieldbearer, Kinds.Knight, Kinds.Halberdier, Kinds.Spearman };
        for (int i = 0; i < foot.Length; i++) Stand(foot[i], line, kz - 36 + i * 9);
        var missiles = new[] { Kinds.Slinger, Kinds.Crossbowman, Kinds.Bombardier, Kinds.Slinger, Kinds.Crossbowman, Kinds.Bombardier, Kinds.Slinger };
        for (int i = 0; i < missiles.Length; i++) Stand(missiles[i], line + 16, kz - 30 + i * 10);
        Stand(Kinds.Giant, line + 30, kz - 8);
        Stand(Kinds.TameTroll, line + 30, kz + 14);
        foreach (int dz in new[] { -58, -66, -74 }) Stand(Kinds.Hound, line - 4, kz + dz);
        foreach (var (t, dx, dz) in new[] { (Kinds.Rider, 0, 56), (Kinds.Rider, 8, 66), (Kinds.Lancer, -6, 76), (Kinds.HorseArcher, 18, 58), (Kinds.HorseArcher, 18, 72) })
            Stand(t, line + dx, kz + dz);
        // a band come by day, ahead of its wave, so the fight is seen in the light
        Advance(1);
        sim.HordeStock[Stockpile.Wood] = 24 * VoxelWorld.PerM3;
        foreach (var (t, dx, dz) in new[] { (Kinds.Goblin, -80, -20), (Kinds.Goblin, -84, 6), (Kinds.Goblin, -78, 26), (Kinds.Goblin, -90, -40), (Kinds.Orc, -94, -8),
            (Kinds.Orc, -96, 14), (Kinds.Orc, -100, 36), (Kinds.Troll, -110, 0), (Kinds.Sapper, -104, -26), (Kinds.Sapper, -106, 48), (Kinds.Goblin, -88, 60),
            (Kinds.Orc, -98, -56), (Kinds.Ram, -150, 10) })
        {
            int c = sim.NearestStand(line + dx, kz + dz, t.Move.Height);
            if (c >= 0) sim.Spawn(t, c);
        }
        sim.ComputeFields();
        Advance(_poseSeconds);
        if (_look == 1) Rig.Set(World.WX(line - 20), World.WZ(kz + 4), -.75f, .5f, 3.2f, snap: true);
        else Rig.Set(World.WX(line - 34), World.WZ(kz + 10), -.75f, .55f, 1.7f, snap: true);
        Hud.Refresh();
        Marks.Flush();
    }

    void LookAtGrove()
    {
        Sim.Waves.Countdown = 1e9;
        var (gx, gz) = GroveAt;
        int cx = gx + 148, cz = gz + 110;
        float yaw = -.2f, pitch = .36f, zoom = _look is >= 0 and < 3 ? 1.35f : 1f;
        // the camera looks at a plane 4 m up: the trees' middles are higher, so look past it by as much
        float over = (World.WY(World.SurfAt(cx, cz) + 40) - 4) / MathF.Tan(pitch);
        Rig.Set(World.WX(cx) - MathF.Sin(yaw) * over, World.WZ(cz) - MathF.Cos(yaw) * over, yaw, pitch, zoom, snap: true);
        Hud.Refresh();
    }

    /// <summary>
    /// A pond breached by a cut run out to the cliff: the water pours along it, over the edge and down into the lowland,
    /// and the pond drops (--at=seconds: how long it has been running).
    /// </summary>
    void Flood()
    {
        var w = World; var s = Site; var sim = Sim;
        sim.Waves.Countdown = 1e9;
        var (px, pz, pr, _) = s.Ponds[1];
        int cliff = px - pr;
        while (cliff > 0 && w.SurfAt(cliff - 1, pz) > w.SurfAt(cliff, pz) - 12) cliff--;
        sim.InstantLabour = true;
        sim.DesignateRemove(Shapes.Line(w, s, px - MapGen.Shore(w, s, px, pz, -1, 0) + 6, pz, cliff, pz, new Stroke { Width = 6, Height = 16, Dig = true }, 0));
        sim.InstantLabour = false;
        Advance(_poseSeconds);
        Rig.Set(World.WX(cliff + 24), World.WZ(pz + 10), -2.2f, .7f, 2.6f, snap: true);
        Hud.Refresh();
    }

    /// <summary>Points the cursor for the screenshot: a blueprint copy beside the fort, or a moat half drawn.</summary>
    void PoseDemo()
    {
        // (set out round the citadel where it stood at 819,512 on the old valley: it goes with it)
        int ox = Site.KeepX - 819, oz = Site.KeepZ - 512;
        Vector2 At(int x, int z) => Rig.Cam.UnprojectPosition(new Vector3(World.WX(x + ox + .5f), World.WY(World.SurfAt(x + ox, z + oz)), World.WZ(z + oz + .5f)));
        if (_pose == "line")
        {
            var a = At(612, 470); var b = At(616, 600);
            Tools._UnhandledInput(new InputEventMouseMotion { Position = a });
            Tools._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a });
            for (int i = 1; i <= 6; i++) Tools._UnhandledInput(new InputEventMouseMotion { Position = a.Lerp(b, i / 6f) });
            return;
        }
        if (_pose is "battle" or "close" or "flood" or "troops") return;
        int x = 720 + ox, z = 640 + oz;
        Tools.PointAt(new Vector3(World.WX(x), World.WY(World.SurfAt(x, z)), World.WZ(z)));
    }
}
