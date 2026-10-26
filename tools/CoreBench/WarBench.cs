using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's war (--war). A band's spirit falls with its fallen, a blast and one of it thrown, and at the bottom the
/// band breaks whole; a raider low in spirit breaks the sooner, one by its warlord the later. A warlord comes at the
/// head of his army with his guard, his cry takes the pain off those about him and turns back the broken, his blow to
/// the ground throws ours off their feet and cracks the wall by him, and killed he breaks his army; badly hurt he goes
/// off the field, gets away with his wounds and comes on in level for what his army did, and comes back as he went.
/// Engines left alone are given up and burn, so a raid ends. Our archers loose at the warlord first, our swordsmen go
/// for the sapper with a charge, a commander stands its line at the breach. A warlord's raid fought out at a walled fort
/// to its end, its report adding up and read back from a save; and a raid two hundred strong, for how long a step takes.
/// (--war-audit: raids of rising threat at the fort, told out for the review.)
/// </summary>
static class WarBench
{
    static Sim Fresh()
    {
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var s = new Sim(w, site) { WildTarget = 0 };
        s.SetSource(new MapRaids());
        s.Clock.Left = 1e9;
        return s;
    }

    /// <summary>A ring of stone wall round the citadel, five metres high, archers on it, a line of foot and a commander inside.</summary>
    static List<Unit> Fort(Sim s, int archers, int foot)
    {
        var w = s.World; var site = s.Site;
        int kx = site.KeepX, kz = site.KeepZ, r = site.KeepR + 44;
        s.InstantLabour = true;
        int top = Shapes.LevelTop(w, kx - r, kz, 40);
        s.DesignateBuild(Shapes.Circle(w, site, kx, kz, r, new Stroke { Mat = Mat.Stone, Width = 10, Height = 40, Level = true, Hollow = true }, top));
        s.CompleteAll();
        s.InstantLabour = false;
        var ours = new List<Unit>();
        for (int i = 0; i < archers; i++)
        {
            double a = i * Math.PI * 2 / archers;
            int c = s.NearestStand(kx + (int)(Math.Cos(a) * r), kz + (int)(Math.Sin(a) * r), 4);
            var u = s.Spawn(Kinds.Archer, c);
            s.Command(new[] { u }, c);
            ours.Add(u);
        }
        var kinds = new[] { Kinds.Spearman, Kinds.Swordsman, Kinds.Shieldbearer, Kinds.Knight };
        for (int i = 0; i < foot; i++)
        {
            double a = i * Math.PI * 2 / foot;
            int c = s.NearestStand(kx + (int)(Math.Cos(a) * (r - 18)), kz + (int)(Math.Sin(a) * (r - 18)), 2);
            ours.Add(s.Spawn(kinds[i % kinds.Length], c));
        }
        ours.Add(s.Spawn(Kinds.Captain, s.NearestStand(kx - r + 30, kz, 2)));
        return ours;
    }

    static int Stone(Sim s)
    {
        var w = s.World; var site = s.Site;
        int n = 0, r = site.KeepR + 44;
        for (int z = site.KeepZ - r - 8; z <= site.KeepZ + r + 8; z++)
            for (int x = site.KeepX - r - 8; x <= site.KeepX + r + 8; x++)
            {
                float d = MathF.Sqrt((x - site.KeepX) * (x - site.KeepX) + (z - site.KeepZ) * (z - site.KeepZ));
                if (MathF.Abs(d - r) > 7) continue;
                for (int y = w.SurfAt(x, z); y > 0 && w.Get(x, y, z) is Mat.Stone or Mat.Air; y--) if (w.Get(x, y, z) == Mat.Stone) n++;
            }
        return n;
    }

    static void Run(Sim s, double seconds) { for (double t = 0; t < seconds; t += .05) s.Step(.05f); }
    static void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
    static float Apart(Unit a, Unit b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();

        // ---- a band's spirit: its fallen, a blast seen, one of it thrown; at the bottom it breaks whole
        {
            var s = Fresh();
            s.Raid(Enumerable.Repeat(Kinds.Goblin, 8).ToList(), 1, .5);
            for (int i = 0; i < 40 && !s.Bands.Exists(b => b.State != BandState.Coming && b.Spawned >= b.Size); i++) Run(s, .5);
            var band = s.Bands.First(b => b.State != BandState.Coming);
            var said = new List<string>();
            s.Said += (text, _, _, _) => said.Add(text);
            float s0 = band.Spirit;
            var men = band.Members.Where(u => !u.Dead && !u.Leader).ToList();
            s.Hurt(men[0], null, 1e6f, "test"); s.Hurt(men[1], null, 1e6f, "test");
            float s1 = band.Spirit;
            var near = men[2];
            s.Detonate(near.X + 24, near.Y + 2, near.Z, 3, 0, "test");
            float s2 = band.Spirit;
            s.Fling(men[3], 0, 16, 0, "test");
            float s3 = band.Spirit;
            float nerveLow = s.Nerve(men[4]);
            while (!band.Broken && band.Members.Exists(u => !u.Dead && !u.Leader)) s.Hurt(band.Members.First(u => !u.Dead && !u.Leader), null, 1e6f, "test");
            var left = band.Members.Where(u => !u.Dead).ToList();
            check("a band's spirit falls with each of it fallen, at a blast seen and at one of it thrown; it wavers under half and at the bottom breaks and runs whole",
                s0 == 1 && Math.Abs(s1 - (1 - 2 * .8f / 8)) < .01f && s1 - s2 > .1f && s2 - s3 > .02f && band.Broken && left.Count > 0 && left.All(u => u.Routed)
                && said.Exists(t => t.Contains("дрогнула")) && said.Exists(t => t.Contains("бежит")) && nerveLow < .95f,
                $"spirit {s0:0.00} → {s1:0.00} two fallen → {s2:0.00} a blast → {s3:0.00} one thrown; nerve then {nerveLow:0.00}; broken {band.Broken} with {left.Count} left, routed {left.Count(u => u.Routed)}; said {string.Join(" | ", said.Where(t => t.Contains("Ватага")))}");
        }

        // ---- a warlord at the head of his army: his guard, his hardness, his cry, his blow to the ground, his fall
        {
            var s = Fresh();
            var lord = s.NewWarlord(3);
            var roster = new List<UnitType> { Kinds.Orc, Kinds.Orc, Kinds.Orc, Kinds.Orc, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin };
            var said = new List<string>();
            s.Said += (text, _, _, _) => said.Add(text);
            s.Raid(roster, 1, .5, lord);
            Unit at = null;
            for (int i = 0; i < 60 && (at == null || s.Raids.Coming > 0); i++) { Run(s, .5); at = s.Units.FirstOrDefault(u => u.Lord == lord && !u.Dead); }
            var army = s.Bands.Where(b => b.Lord == lord).ToList();
            var own = army.FirstOrDefault(b => b.LordsOwn);
            bool troll = own != null && own.Members.Exists(u => u.T == Kinds.Troll);
            float hard = at == null ? 0 : at.MaxHp / (Kinds.Orc.Hp * s.HordeHardy);
            check("a warlord comes at the head of his army with his guard (a troll among it from the third level), as much harder to kill as his level makes him; every band is his army",
                at != null && at.Leader && own?.Leader == at && troll && army.Count >= 2 && Math.Abs(hard - 1.5f * 3.5f) < .05f && own.Name.Contains(lord.Of)
                && said.Exists(t => t.Contains("военачальник " + lord.Name)),
                $"{(at == null ? "no warlord" : $"{at.T.Key}, {at.MaxHp:0} hp, {hard:0.00} times his kind")}; his band {own?.Name} of {string.Join(", ", own?.Members.Select(u => u.T.Key) ?? new string[0])}; {army.Count} bands his");
            if (at == null) return;
            // three of his near him in pain, one of them broken: his cry
            var his = s.Units.Where(u => !u.Dead && u.Team == Team.Horde && u != at && u.Band?.Lord == lord && Apart(u, at) < 18 * VoxelWorld.PerMetre && !Kinds.IsEngine(u.T)).Take(4).ToList();
            foreach (var u in his) u.Pain = 1;
            var broke = his.Count > 0 ? his[^1] : null;
            if (broke != null) { broke.Routed = true; broke.Hp = broke.MaxHp; }
            at.CryReady = 0;
            int cries0 = s.LordCries;
            Run(s, .2);
            check("his cry, when those about him waver: they shrug off their pain and strike harder a while, and one broken turns back to the fight",
                his.Count >= 3 && s.LordCries == cries0 + 1 && his.All(u => u.Pain < .2f && u.RallyUntil > s.Time) && broke is { Routed: false },
                $"{his.Count} about him; cries {s.LordCries - cries0}; pain {string.Join(" ", his.Select(u => u.Pain.ToString("0.00")))}; the broken one routed {broke?.Routed}");
            // three swordsmen of ours closing on him, a pillar of our masonry beside him: his blow to the ground
            var w = s.World;
            int px = (int)at.X + 10, pz = (int)at.Z, py = w.SurfAt(px, pz);
            for (int y = py; y < py + 24; y++) for (int z = pz - 2; z <= pz + 2; z++) for (int x = px - 2; x <= px + 2; x++) w.Set(x, y, z, Mat.Stone);
            int cracks0 = s.Cracks.Count;
            var ours = Enumerable.Range(0, 3).Select(i => s.Spawn(Kinds.Swordsman, s.NearestStand((int)at.X - 12 + i * 4, (int)at.Z + 14, 2))).ToList();
            at.SlamReady = 0;
            at.ChainedUntil = 0;
            int slams0 = s.LordSlams;
            for (int i = 0; i < 20 && s.LordSlams == slams0; i++) Run(s, .1);
            check("his blow to the ground: those of ours about him are hurt and thrown off their feet, and our masonry beside him cracks",
                s.LordSlams == slams0 + 1 && ours.Count(u => u.Airborne != null || u.Hp < u.MaxHp) >= 2 && s.Cracks.Count > cracks0,
                $"blows {s.LordSlams - slams0}; ours thrown {ours.Count(u => u.Airborne != null)}, hurt {ours.Count(u => u.Hp < u.MaxHp)}; cracks {cracks0} → {s.Cracks.Count}");
            foreach (var u in ours) Gone(u);
            // a raider of his near him bears more pain than one of a band as low in spirit far from him
            var close = s.Units.FirstOrDefault(u => !u.Dead && u.Band?.Lord == lord && u != at && Apart(u, at) < 10 * VoxelWorld.PerMetre);
            float withLord = close == null ? 0 : s.Nerve(close);
            check("a raider by his warlord bears more pain before he breaks", close != null && withLord > 1.2f, $"nerve by him {withLord:0.00}");
            // killed: his army breaks and runs
            s.Hurt(at, null, 1e6f, "test");
            var fled = army.SelectMany(b => b.Members).Where(u => !u.Dead).ToList();
            check("the warlord killed: every band of his army breaks and runs",
                lord.Fate == LordFate.Fell && army.All(b => b.Broken) && fled.All(u => u.Routed) && said.Exists(t => t.Contains($"Военачальник {lord.Name} пал")),
                $"fate {lord.Fate}; bands broken {army.Count(b => b.Broken)} of {army.Count}; running {fled.Count(u => u.Routed)} of {fled.Count}");
            for (int i = 0; i < 400 && s.Raids.Active; i++) Run(s, 1);
            var r = s.LastBattle;
            int came = r == null ? 0 : BattleReport.Sum(r.Came), ended = r == null ? 0 : BattleReport.Sum(r.Fell) + BattleReport.Sum(r.Fled) + BattleReport.Sum(r.Taken);
            check("the raid over, its report: who came, kind by kind, and every one of them fallen, gone or taken; the warlord fallen; each band's end; a leader counted once at dawn",
                !s.Raids.Active && r != null && r.Held && r.Lord == lord.Name && r.LordFate == LordFate.Fell && came == roster.Count + 4 && ended == came
                && r.Bands.Count == army.Count && r.Bands.All(b => b.Broken) && s.LastNight is { } n && n.LeadersKilled <= n.Leaders,
                r == null ? "no report" : $"came {came} ({string.Join(", ", r.Came.Select(k => $"{k.Key.Key} {k.Value}"))}), fell {BattleReport.Sum(r.Fell)}, fled {BattleReport.Sum(r.Fled)}, taken {BattleReport.Sum(r.Taken)}; " +
                $"lord {r.Lord} {r.LordFate}; bands {string.Join("; ", r.Bands.Select(b => $"{b.Name} {b.Size}: {b.Fell}/{b.Fled} broken {b.Broken}"))}; leaders {s.LastNight?.LeadersKilled} of {s.LastNight?.Leaders}");
        }

        // ---- a warlord badly hurt: off the field with his guard; away with his wounds, on in level; back as he went
        {
            var s = Fresh();
            var lord = s.NewWarlord(1);
            s.Raid(new List<UnitType> { Kinds.Goblin, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin }, 1, .5, lord);
            Unit at = null;
            for (int i = 0; i < 60 && (at == null || s.Raids.Coming > 0); i++) { Run(s, .5); at = s.Units.FirstOrDefault(u => u.Lord == lord && !u.Dead); }
            if (at == null) { check("a warlord badly hurt goes off the field", false, "he never came"); return; }
            lord.Slew = 10;
            at.Hp = at.MaxHp * .28f;
            Run(s, .2);
            bool off = at.Routed, guard = at.Band.Members.Where(u => !u.Dead && u != at).All(u => u.Routed);
            for (int i = 0; i < 300 && !at.Dead; i++) Run(s, 1);
            var back = s.Escaped.FirstOrDefault(v => v.Lord == lord);
            check("a warlord badly hurt goes off the field with his guard; got away, he keeps his wounds and comes on in level for what his army did",
                off && guard && lord.Fate == LordFate.Fled && back != null && lord.Health < .35f && lord.Level == 2,
                $"routed {off}, his guard {guard}; fate {lord.Fate}, health {lord.Health:0.00}, level {lord.Level}, xp {lord.Xp:0}; in those got away {back != null}");
            for (int i = 0; i < 100 && s.Raids.Active; i++) Run(s, 1);

            float went = lord.Health;
            s.Raid(new List<UnitType> { Kinds.Goblin, Kinds.Goblin }, 3, .5, lord);
            Unit again = null;
            for (int i = 0; i < 60 && again == null; i++) { Run(s, .5); again = s.Units.FirstOrDefault(u => u.Lord == lord && !u.Dead); }
            check("…and comes back at the head of the next army with his level, his wounds mended halfway since",
                again != null && lord.Fate == LordFate.Field && Math.Abs(again.Hp / again.MaxHp - (went + (1 - went) * .5f)) < .05f && again.MaxHp > Kinds.Orc.Hp * s.HordeHardy * 1.5f * 2.9f,
                again == null ? "he did not come" : $"{again.Hp:0} of {again.MaxHp:0} hp ({again.Hp / again.MaxHp:0.00}, he went with {went:0.00}), level {lord.Level}");
        }

        // ---- engines left alone: given up and set alight, their keepers go, and the raid ends
        {
            var s = Fresh();
            s.Raid(new List<UnitType> { Kinds.Catapult, Kinds.Mantlet, Kinds.Goblin, Kinds.Goblin, Kinds.Goblin }, 1, .5);
            for (int i = 0; i < 40 && s.Raids.Coming > 0; i++) Run(s, .5);
            Run(s, 3);
            foreach (var u in s.Units) if (!u.Dead && u.Team == Team.Horde && !Kinds.IsEngine(u.T) && !u.Guard) s.Hurt(u, null, 1e6f, "test");
            var engines = s.Units.Where(u => !u.Dead && Kinds.IsEngine(u.T)).ToList();
            var keepers = s.Units.Where(u => !u.Dead && u.Escort != null).ToList();
            double t0 = s.Time;
            for (int i = 0; i < 300 && s.Raids.Active; i++) Run(s, 1);
            check("engines left with nobody of the raid but their keepers are given up and set alight; the keepers go, and the raid ends",
                engines.Count == 2 && engines.All(u => u.Abandoned && (u.Dead || u.Burn > 0)) && keepers.Count == 2 && keepers.All(u => u.Dead) && !s.Raids.Active && s.LastBattle != null,
                $"engines {string.Join(", ", engines.Select(u => $"{u.T.Key} given up {u.Abandoned}, burning {u.Burn:0}, dead {u.Dead}"))}; keepers {keepers.Count}, gone {keepers.Count(u => u.Dead)}; the raid over {!s.Raids.Active} after {s.Time - t0:0} s");
        }

        // ---- our archers loose at the warlord before a nearer goblin; our swordsmen go for the sapper with a charge
        {
            var s = Fresh();
            foreach (var u in s.Units) if (u.Team == Team.Player) Gone(u);
            int ax = s.Site.KeepX - 260, az = s.Site.KeepZ;
            Level(s, ax - 100, az, 140, 40);
            var archer = s.Spawn(Kinds.Archer, s.NearestStand(ax, az, 4));
            s.Command(new[] { archer }, archer.Cell);
            var goblin = s.Spawn(Kinds.Goblin, s.NearestStand(ax - 80, az + 10, 2));
            var lordUnit = s.Spawn(Kinds.Orc, s.NearestStand(ax - 140, az - 10, 3));
            lordUnit.Lord = s.NewWarlord(2);
            foreach (var u in new[] { goblin, lordUnit }) u.ChainedUntil = 1e9;
            Run(s, 1);
            bool atLord = archer.Target == lordUnit;
            Gone(archer); Gone(goblin); Gone(lordUnit);
            var sword = s.Spawn(Kinds.Swordsman, s.NearestStand(ax, az, 2));
            s.Command(new[] { sword }, sword.Cell);
            var g2 = s.Spawn(Kinds.Goblin, s.NearestStand(ax - 30, az + 4, 2));
            var sapper = s.Spawn(Kinds.Sapper, s.NearestStand(ax - 56, az - 4, 2));
            sapper.Charge = true;
            foreach (var u in new[] { g2, sapper }) u.ChainedUntil = 1e9;
            Run(s, .3);
            check("our archers loose at the warlord before a goblin nearer them; our swordsmen go for a sapper with a charge before a goblin nearer them",
                atLord && sword.Target == sapper, $"archer's mark {(atLord ? "the warlord" : "another")}; swordsman's {sword.Target?.T.Key ?? "none"}");
        }

        // ---- a commander stands its line at the breach: where the horde comes up a ladder onto our wall, not where the
        // straight way from the horde meets it
        {
            var s = Fresh();
            foreach (var u in s.Units) if (u.Team == Team.Player) Gone(u);
            var w = s.World; var site = s.Site;
            int wx = (site.KeepX - 300) & ~7, wz = site.KeepZ, fg = w.SurfAt(wx, wz);
            Level(s, wx - 60, wz, 180, 140);
            s.InstantLabour = true;
            var wall = new VoxelList();
            for (int z = wz - 100; z < wz + 100; z++) for (int x = wx; x < wx + 16; x++) for (int y = fg; y < fg + 32; y++) wall.Add(x, y, z, Mat.Stone);
            s.DesignateBuild(wall);
            s.CompleteAll();
            s.InstantLabour = false;
            // a ladder of the horde up its outer face, far up it from the straight way
            for (int z = wz + 88; z < wz + 92; z++) for (int y = fg; y < fg + 34; y++) w.Set(wx - 1, y, z, Mat.Ladder);
            Run(s, .3);
            var cap = s.Spawn(Kinds.Captain, s.NearestStand(wx + 120, wz, 2));
            var swords = Enumerable.Range(0, 4).Select(i => s.Spawn(Kinds.Swordsman, s.NearestStand(wx + 100 + i * 10, wz + 10, 2))).ToList();
            var climber = s.Spawn(Kinds.Goblin, s.NearestStand(wx - 12, wz + 90, 2));
            // up the ladder, a man's height off the ground
            {
                int li = (wx - 1) >> NavGrid.Shift, lk = (wz + 90) >> NavGrid.Shift;
                for (int y = (fg >> NavGrid.Shift) + 1; y < (fg + 34) >> NavGrid.Shift; y++)
                {
                    int c = s.Nav.Id(li, y, lk);
                    if (s.Nav.Ladder[c] == 0 || s.Nav.Ground(li, y - 1, lk)) continue;
                    climber.Cell = c; (climber.X, climber.Y, climber.Z) = s.Nav.Feet(c);
                    break;
                }
            }
            var knot = Enumerable.Range(0, 5).Select(i => s.Spawn(Kinds.Goblin, s.NearestStand(wx - 40, wz + 40 + i * 6, 2))).ToList();
            foreach (var u in knot.Append(climber)) { u.ChainedUntil = 1e9; }
            bool climbing = s.Climbing(climber);
            Run(s, 6);
            float postZ = (float)swords.Where(u => u.Post >= 0).Select(u => s.Nav.Feet(u.Post).z).DefaultIfEmpty(0).Average();
            check("a commander stands its line inside the wall where the horde comes up onto it, not where the straight way from the horde meets the wall",
                climbing && postZ > wz + 70 && s.CaptainFormed >= 1, $"climbing {climbing}; the line's posts at z {postZ - wz:0} from the middle (the ladder at 90, the knot at 40–64)");
        }

        // ---- a warlord's raid at a walled fort fought out to its end; its report read back from a save
        {
            var s = Fresh();
            var ours = Fort(s, 8, 8);
            Run(s, .1);
            var lord = s.NewWarlord(3);
            s.Raid(Sim.Roster(8, 1), 3, .5, lord);
            double t0 = s.Time, cpu = 0; int steps = 0;
            BattleReport mid = null; Sim back = null;
            while (s.Raids.Active && !s.Over && s.Time - t0 < 900)
            {
                double c0 = BenchClock.CpuMs();
                s.Step(.05f);
                cpu += BenchClock.CpuMs() - c0; steps++;
                // halfway through, saved and read back
                if (back == null && s.Time - t0 > 60) back = Reload(s);
            }
            var r = s.LastBattle;
            int came = r == null ? 0 : BattleReport.Sum(r.Came), ended = r == null ? 0 : BattleReport.Sum(r.Fell) + BattleReport.Sum(r.Fled) + BattleReport.Sum(r.Taken);
            var given = s.Units.Count(u => u.Abandoned && !u.Dead);
            check("a warlord's raid at a walled fort is fought out to its end: its report has every one who came fallen, gone, taken or an engine given up; the commander loosed volleys",
                !s.Raids.Active && !s.Over && r != null && r.Lord == lord.Name && came > 40 && came - ended <= given + 4 && s.CaptainVolleys >= 1,
                r == null ? $"no report after {s.Time - t0:0} s" : $"{r.Seconds:0} s; came {came}, fell {BattleReport.Sum(r.Fell)}, fled {BattleReport.Sum(r.Fled)}, taken {BattleReport.Sum(r.Taken)}, engines given up and burning {given}; ours fell {BattleReport.Sum(r.OursFell)} of {ours.Count}; " +
                $"the warlord {r.LordFate}; broke {Fmt.M3(r.Broke)} m³; cries {s.LordCries}, blows {s.LordSlams}, volleys {s.CaptainVolleys}; {cpu / Math.Max(1, steps):0.00} ms a step");
            var lordBack = back?.Units.FirstOrDefault(u => u.Lord != null && !u.Dead);
            var bandBack = back?.Bands.FirstOrDefault(b => b.Lord != null && b.State != BandState.Coming);
            check("…read back from a save in the middle of it: the warlord, his level and his army, the bands' spirit, the report so far",
                back != null && (lordBack == null ? lord.Fate != LordFate.Field : lordBack.Lord.Name == lord.Name && lordBack.Lord.Level == lord.Level) && bandBack != null && bandBack.Lord != null
                && back.Raids.Active && Reload(back) is { } again && again.Units.Count(u => !u.Dead) == back.Units.Count(u => !u.Dead),
                back == null ? "not saved" : $"warlord {lordBack?.Lord.Name ?? "-"} level {lordBack?.Lord.Level}; a band {bandBack?.Name} spirit {bandBack?.Spirit:0.00} of {bandBack?.Lord?.Name}");
        }

        // ---- two hundred of the horde at once round the fort, a warlord among them: how long a step takes
        {
            var s = Fresh();
            Fort(s, 16, 24);
            Run(s, .1);
            s.Raid(new List<UnitType>(), 4, .5, s.NewWarlord(4));
            int kx = s.Site.KeepX, kz = s.Site.KeepZ;
            // (a spot in the water or on a cliff takes no band: a little further round, then)
            int summoned = 0;
            for (int i = 0; i < 80 && summoned < 28; i++)
            {
                double a = i * Math.PI * 2 / 28;
                if (s.Summon(kx + (int)(Math.Cos(a) * (440 + i % 3 * 60)), kz + (int)(Math.Sin(a) * (440 + i % 3 * 60)), summoned % 7 == 0) != null) summoned++;
            }
            double cpu = 0, worst = 0; int steps = 0, peak = 0, horde = 0;
            string slowest = "";
            Sim.StageTicks = new Dictionary<string, long>();
            for (double t = 0; t < 120; t += .05)
            {
                Sim.StageTicks.Clear(); Sim.SlowestUnitTicks = 0;
                double c0 = BenchClock.CpuMs();
                s.Step(.05f);
                double c = BenchClock.CpuMs() - c0;
                if (c > worst)
                {
                    var who = s.Units.FirstOrDefault(u => u.Id == Sim.SlowestUnit);
                    slowest = $"at {t:0.0} s: " + string.Join(", ", Sim.StageTicks.OrderByDescending(k => k.Value).Take(3).Select(k => $"{k.Key} {k.Value * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0}"))
                        + $"; the slowest unit {who?.T.Key} {who?.State} {who?.Why} routed {who?.Routed} {Sim.SlowestUnitTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0} ms";
                }
                cpu += c; worst = Math.Max(worst, c); steps++;
                int alive = 0, raiders = 0;
                foreach (var u in s.Units) if (!u.Dead) { alive++; if (u.Team == Team.Horde) raiders++; }
                peak = Math.Max(peak, alive); horde = Math.Max(horde, raiders);
            }
            check("two hundred of the horde at once at the fort, a warlord among them: a step takes under 4 ms on the whole", cpu / steps < 4 && horde >= 200,
                $"{summoned} bands called up; {cpu / steps:0.00} ms a step on the whole, the slowest {worst:0.0} ms ({slowest}); up to {horde} of the horde, {peak} alive in all");
            Sim.StageTicks = null;
        }
    }

    /// <summary>A field of level turf, so many voxels either way of a spot, bare to the sky.</summary>
    static void Level(Sim s, int cx, int cz, int rx, int rz)
    {
        var w = s.World;
        int fg = w.SurfAt(cx, cz);
        for (int z = cz - rz; z <= cz + rz; z++)
            for (int x = cx - rx; x <= cx + rx; x++)
                for (int y = 2; y < w.SY - 1; y++)
                {
                    byte want = y < fg ? (y == fg - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
        s.Step(.05f);
    }

    static Sim Reload(Sim s)
    {
        var ms = new System.IO.MemoryStream();
        s.Save(ms, new MapParams().Seed, "test", "");
        ms.Position = 0;
        var mp = new MapParams();
        var w2 = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        using var reader = new SaveReader(ms);
        var site2 = MapGen.Generate(w2, new MapParams { Seed = reader.Seed });
        w2.Compact();
        reader.Valley(w2, site2);
        var sim2 = new Sim(w2, site2);
        sim2.Load(reader);
        return sim2;
    }

    public static void Audit()
    {
        Console.WriteLine();
        foreach (var (threat, edge, lordLevel) in new[] { (3f, 4, 0), (6f, 1, 0), (10f, 2, 0), (14f, 5, 0), (8f, 3, 3) })
        {
            var s = Fresh();
            var ours = Fort(s, 8, 8);
            s.Step(.05f);
            int stone0 = Stone(s), kills0 = s.Kills, esc0 = s.Escaped.Count;
            var roster = Sim.Roster(threat, 1);
            var lord = lordLevel > 0 ? s.NewWarlord(lordLevel) : null;
            s.Raid(roster, edge, .5, lord);
            double t0 = s.Time, cpu = 0, worst = 0; int steps = 0, peak = 0;
            var horde = new HashSet<Unit>();
            int routed = 0;
            while (s.Raids.Active && !s.Over && s.Time - t0 < 900)
            {
                double c0 = BenchClock.CpuMs();
                s.Step(.05f);
                double c = BenchClock.CpuMs() - c0;
                cpu += c; worst = Math.Max(worst, c); steps++;
                int alive = 0;
                foreach (var u in s.Units) { if (u.Dead) continue; alive++; if (u.Team == Team.Horde) { if (horde.Add(u)) { } } }
                peak = Math.Max(peak, alive);
                if (steps % 1200 == 0)
                    Console.WriteLine($"[war]   {s.Time - t0:0} s: {s.Units.Count(u => !u.Dead && u.Team == Team.Horde)} of the horde in the valley, {s.Raids.Coming} to come, ours {ours.Count(u => !u.Dead)}; {cpu / steps:0.00} ms a step; {DateTime.Now:HH:mm:ss}");
            }
            routed = horde.Count(u => u.Routed);
            int lost = ours.Count(u => u.Dead), workersLost = s.Units.Count(u => u.Dead && u.Team == Team.Player && !ours.Contains(u));
            Console.WriteLine($"[war] threat {threat} by side {edge}: {roster.Count} come ({string.Join(" ", roster.GroupBy(t => t.Key).Select(g => $"{g.Key}{g.Count()}"))}); "
                + $"over {!s.Raids.Active} after {s.Time - t0:0} s; killed {s.Kills - kills0}, escaped {s.Escaped.Count - esc0}, routed at some point {routed}; "
                + $"ours lost {lost} of {ours.Count} soldiers, {workersLost} others; the keep {s.KeepHp:0}/{s.KeepMax:0}; wall stone {stone0} → {Stone(s)}; "
                + $"step {cpu / Math.Max(1, steps):0.00} ms on the whole, worst {worst:0.0} ms; peak {peak} alive");
            foreach (var u in s.Units)
                if (!u.Dead && u.Team == Team.Horde)
                    Console.WriteLine($"[war]   left: {u.T.Key} {u.State} routed {u.Routed} bound {u.Bound} inside {u.Inside} guard {u.Guard} envoy {u.Envoy} {MathF.Sqrt((u.X - s.Site.KeepX) * (u.X - s.Site.KeepX) + (u.Z - s.Site.KeepZ) * (u.Z - s.Site.KeepZ)) / 8:0} m off, hp {u.Hp:0}/{u.MaxHp:0}, why {u.Why}, path {(u.Path == null ? -1 : u.Path.Count - u.PathI)}");
            if (s.LastBattle is { } br)
                Console.WriteLine($"[war]   battle: {br.Seconds:0} s, held {br.Held}; came {BattleReport.Sum(br.Came)}, fell {BattleReport.Sum(br.Fell)}, fled {BattleReport.Sum(br.Fled)}, taken {BattleReport.Sum(br.Taken)}; ours fell {BattleReport.Sum(br.OursFell)}; broke {br.Broke}; keep lost {br.KeepLost:0.00}; lord {br.Lord} L{br.LordLevel} {br.LordFate} slew {br.LordSlew}; bands {string.Join("; ", br.Bands.Select(b => $"{b.Name} {b.Size}: fell {b.Fell} fled {b.Fled} taken {b.Taken} broken {b.Broken} spirit {b.Spirit:0.00}"))}; cries {s.LordCries} slams {s.LordSlams} volleys {s.CaptainVolleys}");
            if (s.LastNight is { } n) Console.WriteLine($"[war]   report: killed {n.Killed}, lost {n.Lost}, keep lost {n.KeepLost:0.00}, leaders {n.LeadersKilled}/{n.Leaders}, stars {n.Stars}; best {string.Join(", ", n.Best.Select(b => $"{b.who} {b.felled}"))}");
        }

        // two hundred and more: how long a step takes
        if (Environment.GetEnvironmentVariable("WAR200") == "1")
        {
            var s = Fresh();
            var ours = Fort(s, 16, 24);
            s.Step(.05f);
            var roster = new List<UnitType>();
            for (int i = 0; i < 120; i++) roster.Add(Kinds.Goblin);
            for (int i = 0; i < 50; i++) roster.Add(Kinds.Orc);
            for (int i = 0; i < 16; i++) roster.Add(Kinds.Sapper);
            for (int i = 0; i < 10; i++) roster.Add(Kinds.Troll);
            for (int i = 0; i < 4; i++) roster.Add(Kinds.Ram);
            s.Raid(roster, 4, .5);
            double t0 = s.Time;
            for (int phase = 0; phase < 6; phase++)
            {
                double cpu = 0, worst = 0; int steps = 0;
                for (double t = 0; t < 30; t += .05)
                {
                    double c0 = BenchClock.CpuMs();
                    s.Step(.05f);
                    double c = BenchClock.CpuMs() - c0;
                    cpu += c; worst = Math.Max(worst, c); steps++;
                }
                int horde = s.Units.Count(u => !u.Dead && u.Team == Team.Horde), all = s.Units.Count(u => !u.Dead);
                int fighting = s.Units.Count(u => !u.Dead && u.State == UnitState.Fight);
                Console.WriteLine($"[war] 200 raid, {s.Time - t0:0} s: {horde} of the horde, {all} alive, {fighting} fighting, {s.Units.Count} in the list; step {cpu / steps:0.00} ms, worst {worst:0.0} ms; ours lost {ours.Count(u => u.Dead)}; keep {s.KeepHp:0}");
            }
        }
    }
}
