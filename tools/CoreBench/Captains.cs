using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's commanders (--captains) and the hands of one gone into: a commander inside a wall of ours, a band of the
/// horde beyond it: it forms its swordsmen up inside the wall and sends its archers up onto it, sends the badly hurt back
/// to mend, leaves be one the player sent elsewhere, and does nothing with its word turned off; its calls — the shield
/// wall takes a third off a blow, «За мной!» brings them after it, the volley looses every archer at once, the horn
/// takes the pain away — and each waits its time; the band gone, it calls the workers to mend the wall. One gone into
/// guards with the right button, a shield taking the blows it faces; a blow held back lands harder; a kick shoves a
/// goblin back; a worker gone into lays what is marked where it looks, and digs what is marked, only that. Saved and read back.
/// </summary>
static class CaptainsBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
        int kx = site.KeepX, kz = site.KeepZ;

        // a level field west of the citadel, a stone wall of ours across it four metres high, built by the workers' rules;
        // the field at the height the citadel stands on and right up to its foot (made level at the lowland's height, it
        // left a cliff six metres high between the wall and the citadel, and the horde's way rightly went round by the
        // plateau's slope, nowhere near the wall)
        int wx = (kx - 300) & ~7, wz = kz, fg = site.KeepBase;
        for (int z = wz - 140; z <= wz + 140; z++)
            for (int x = wx - 240; x <= kx - site.KeepR - 20; x++)
                for (int y = 2; y < w.SY - 1; y++)
                {
                    byte want = y < fg ? Mat.Soil : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
        sim.InstantLabour = true;
        var wall = new VoxelList();
        for (int z = wz - 100; z < wz + 100; z++)
            for (int x = wx; x < wx + 16; x++)
                for (int y = fg; y < fg + 32; y++) wall.Add(x, y, z, Mat.Stone);
        sim.DesignateBuild(wall);
        sim.CompleteAll();
        sim.InstantLabour = false;
        // a ladder of ours up its inner face, the way up onto it (its own cell beside the wall)
        for (int z = wz + 40; z < wz + 42; z++) for (int y = fg; y < fg + 34; y++) w.Set(wx + 16, y, z, Mat.Ladder);
        Run(.3);

        // the commander and its people inside the wall
        var cap = sim.Spawn(Kinds.Captain, sim.NearestStand(wx + 120, wz, 2));
        var swords = Enumerable.Range(0, 4).Select(i => sim.Spawn(Kinds.Swordsman, sim.NearestStand(wx + 100 + i * 10, wz + 30, 2))).ToArray();
        var archers = Enumerable.Range(0, 2).Select(i => sim.Spawn(Kinds.Archer, sim.NearestStand(wx + 110 + i * 10, wz - 30, 2))).ToArray();
        // one the player sent somewhere of its own
        var own = sim.Spawn(Kinds.Swordsman, sim.NearestStand(wx + 90, wz - 50, 2));
        sim.Command(new[] { own }, own.Cell);
        int ownPost = own.Post;
        Run(1);
        // a band of the horde beyond the wall, standing
        var band = Enumerable.Range(0, 5).Select(i =>
        {
            var g = sim.Spawn(Kinds.Goblin, sim.NearestStand(wx - 160, wz - 20 + i * 10, 2));
            g.Guard = true; g.Home = g.Cell; g.ChainedUntil = 1e9;
            return g;
        }).ToArray();
        Run(6);
        bool Inside(Unit u) { var (px, _, _) = sim.Nav.Feet(u.Post); return u.Post >= 0 && px >= wx + 16 && px <= wx + 16 + 10 * 8; }
        bool OnWall(Unit u) { var (px, py, _) = sim.Nav.Feet(u.Post); return u.Post >= 0 && px >= wx - 1 && px <= wx + 17 && py >= fg + 24; }
        int formed = swords.Count(Inside), posted = archers.Count(OnWall);
        check("a commander forms its swordsmen up inside the wall the horde is coming at, and sends its archers up onto the wall",
            formed == swords.Length && posted == archers.Length && sim.CaptainFormed >= 1 && sim.CaptainPosted >= 1 && swords.All(s => s.Ward == cap),
            $"inside {formed} of {swords.Length} ({string.Join(", ", swords.Select(s => s.Post < 0 ? "no post" : $"x {sim.Nav.Feet(s.Post).x - wx:0}"))}); " +
            $"on the wall {posted} of {archers.Length} ({string.Join(", ", archers.Select(s => s.Post < 0 ? "no post" : $"x {sim.Nav.Feet(s.Post).x - wx:0} y {sim.Nav.Feet(s.Post).y - fg:0}"))}); " +
            $"formed {sim.CaptainFormed}, posted {sim.CaptainPosted}");
        check("…and leaves be one the player sent somewhere of its own", own.Post == ownPost && own.Ward == null, $"its post {(own.Post == ownPost ? "kept" : "moved")}, ward {own.Ward?.T.Key ?? "none"}");

        // the badly hurt sent back to mend
        swords[0].Hp = swords[0].MaxHp * .4f;
        Run(4);
        check("a commander sends the badly hurt back to mend, before they would go of themselves",
            swords[0].SentBack && swords[0].Mending && sim.CaptainSentBack >= 1, $"sent back {swords[0].SentBack}, mending {swords[0].Mending}, {swords[0].Hp:0} of {swords[0].MaxHp:0}");
        swords[0].Hp = swords[0].MaxHp; swords[0].Mending = false; swords[0].SentBack = false;

        // the shield wall: a blow taken by one in it costs two thirds of one out of it
        foreach (var s in swords) s.WallUntil = 0;
        float hp0 = swords[1].Hp;
        sim.Hurt(swords[1], band[0], 30);
        float bare = hp0 - swords[1].Hp;
        swords[1].Hp = hp0;
        var (sx, _, sz) = sim.Nav.Feet(swords[1].Cell);
        cap.X = sx + 8; cap.Z = sz; cap.Cell = swords[1].Cell;
        string wallSaid = sim.Call(cap, 1, float.NaN, 0, 0);
        sim.Hurt(swords[1], band[0], 30);
        float walled = hp0 - swords[1].Hp;
        string again = sim.Call(cap, 1, float.NaN, 0, 0);
        check("«Держать строй»: those about the commander stand and take a third off every blow; called again at once it is not ready",
            wallSaid == null && swords[1].WallUntil > sim.Time && MathF.Abs(walled - bare * .65f) < 1 && again != null && again.Contains("готов"),
            $"call «{wallSaid ?? "made"}»; a blow of 30 took {bare:0.0} bare, {walled:0.0} in the wall; again «{again}»");
        swords[1].Hp = swords[1].MaxHp;

        // the horn: the pain of those about it gone, the heart in them
        foreach (var s in swords) s.Pain = .9f;
        string horn = sim.Call(cap, 3, float.NaN, 0, 0);
        check("«Рог»: the pain of ours about the commander is gone and they strike harder a while",
            horn == null && swords.All(s => s.Pain == 0 && s.RallyUntil > sim.Time), $"call «{horn ?? "made"}»; pains {string.Join(", ", swords.Select(s => s.Pain.ToString("0.0")))}");

        // the volley: every archer in reach looses at once where it is called
        int shots0 = sim.Shots;
        foreach (var a in archers) a.Cooldown = 5;
        // (at the ground eighteen metres out before the wall, within their bows)
        float vx = wx - 18 * VoxelWorld.PerMetre, vz = archers.Average(a => a.Z);
        string volley = sim.Call(cap, 2, vx, w.SurfAt((int)vx, (int)vz) + 4, vz);
        check("«Залп!»: every archer about the commander looses at once at the point called", volley == null && sim.Shots - shots0 == archers.Length,
            $"call «{volley ?? "made"}»; {sim.Shots - shots0} arrows of {archers.Length} archers; " + string.Join(", ", archers.Select(a => $"{MathF.Sqrt((a.X - vx) * (a.X - vx) + (a.Z - vz) * (a.Z - vz)) / VoxelWorld.PerMetre:0.0} m to the point")));

        // follow me: those about it come after it
        string follow = sim.Call(cap, 0, float.NaN, 0, 0);
        var followers = swords.Where(s => s.Follows == cap).ToArray();
        int there = sim.NearestStand(wx + 200, wz + 60, 2);
        sim.Command(new[] { cap }, there);
        Run(14);
        float near = followers.Length == 0 ? 999 : followers.Max(s => Sim.Dist(s, cap)) / VoxelWorld.PerMetre;
        check("«За мной!»: those about the commander come after it where it goes, and strike harder meanwhile",
            follow == null && followers.Length >= 3 && near < 8 && followers.All(s => s.RallyUntil > sim.Time),
            $"call «{follow ?? "made"}»; {followers.Length} following; the farthest {near:0.0} m from it");

        // with its word turned off it gives none
        foreach (var s in swords) { s.Follows = null; sim.ReleaseHold(new[] { s }); s.Ward = null; }
        cap.Commands = false;
        int formed0 = sim.CaptainFormed;
        Run(6);
        check("with its word turned off a commander gives no orders", sim.CaptainFormed == formed0, $"orders {sim.CaptainFormed - formed0}");
        cap.Commands = true;

        // the band gone: the wall it came at mended
        int build0 = sim.Orders.BuildTotal, mended0 = sim.CaptainMended;
        int broke = 0;
        for (int z = wz - 6; z < wz + 6; z++) for (int y = fg + 8; y < fg + 32; y++) for (int x = wx; x < wx + 16; x++) if (w.Get(x, y, z) == Mat.Stone) { w.Set(x, y, z, Mat.Air); broke++; }
        sim.Command(new[] { cap }, sim.NearestStand(wx + 120, wz, 2));
        cap.Goal = -1;
        Run(4);
        foreach (var g in band) Gone(g);
        Run(6);
        check("the band gone, the commander calls the workers to mend what it broke of the wall", sim.CaptainMended > mended0 && sim.Orders.BuildTotal > build0,
            $"broke {broke}; mendings called {sim.CaptainMended - mended0}; marked to lay {sim.Orders.BuildTotal - build0}");

        // kept in a save: whether it commands, the spot it holds, how long its calls have yet to wait
        cap.Commands = false; cap.Goal = cap.Cell;
        var ms = new System.IO.MemoryStream();
        sim.Save(ms, mp.Seed, "test", "");
        ms.Position = 0;
        var w2 = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        Sim sim2;
        using (var reader = new SaveReader(ms))
        {
            var site2 = MapGen.Generate(w2, new MapParams { Seed = reader.Seed });
            w2.Compact();
            reader.Valley(w2, site2);
            sim2 = new Sim(w2, site2);
            sim2.Load(reader);
        }
        var cap2 = sim2.Units.Find(u => u.Id == cap.Id);
        check("a commander is kept in a save: its kind, whether it commands, the spot it holds, its calls' waits",
            cap2 != null && cap2.T == Kinds.Captain && !cap2.Commands && cap2.Goal == cap.Goal && MathF.Abs((float)(cap2.Ready[3] - sim2.Time) - (float)(cap.Ready[3] - sim.Time)) < .1f,
            cap2 == null ? "not read back" : $"{cap2.T.Key}, commands {cap2.Commands}, goal {cap2.Goal == cap.Goal}, horn in {cap2.Ready[3] - sim2.Time:0} s");
        foreach (var u in sim.Units) if (u.Team == Team.Player) Gone(u);
        Run(.5);

        // one gone into, guarding: a shield takes the orc's blows it faces; turned away, they land
        var sw = sim.Spawn(Kinds.Swordsman, sim.NearestStand(wx + 60, wz + 60, 2));
        sw.Hp = sw.MaxHp = 5000;
        var orc = sim.Spawn(Kinds.Orc, sim.NearestStand(wx + 60 + 10, wz + 60, 2));
        orc.Guard = true; orc.Home = orc.Cell;
        sim.Possess(sw);
        sim.LookYaw = MathF.Atan2(orc.Z - sw.Z, orc.X - sw.X); sim.LookPitch = 0;
        sim.WishGuard = true;
        float g0 = sw.Hp; int blocked0 = sim.GuardedBlows;
        Run(8);
        float guarded = g0 - sw.Hp; int blocked = sim.GuardedBlows - blocked0;
        sim.LookYaw += MathF.PI;
        float g1 = sw.Hp;
        Run(8);
        float turned = g1 - sw.Hp;
        sim.WishGuard = false;
        check("one gone into guarding with its shield takes the blows it faces; turned away, they land",
            blocked >= 2 && guarded < turned * .5f, $"facing: lost {guarded:0}, blocked {blocked}; turned away: lost {turned:0}");

        // a blow held back lands harder than one at a click (at a goblin held where it stands, looked at in the middle; a
        // fresh one for each blow, and the blow's own harm counted rather than what the goblin has left: one blow may take
        // its head off, and then the blows after it fall on nothing)
        Gone(orc);
        sw.Wounds = null; sw.Hp = sw.MaxHp; sw.Pain = 0; sw.Down = false;
        Unit dummy = null;
        float dealt = 0;
        sim.Hurting = (u, dmg, _) => { if (u == dummy) dealt += dmg; };
        float Swing(bool held)
        {
            dummy = sim.Spawn(Kinds.Goblin, sim.NearestStand((int)sw.X + 9, (int)sw.Z, 2));
            dummy.ChainedUntil = 1e9; dummy.Hp = dummy.MaxHp = 5000; dummy.Guard = true; dummy.Home = dummy.Cell;
            Run(1.5);
            float ddx = dummy.X - sw.X, ddz = dummy.Z - sw.Z, ddy = dummy.Y + dummy.T.BodyH * .55f - (sw.Y + sw.T.BodyH * .88f);
            sim.LookYaw = MathF.Atan2(ddz, ddx); sim.LookPitch = MathF.Atan2(ddy, MathF.Sqrt(ddx * ddx + ddz * ddz));
            dealt = 0;
            sim.WishStrike = true;
            sim.Step(.05f);
            if (held)
            {
                sim.Step(.05f);
                Run(1.6);
                dealt = 0;
            }
            sim.WishStrike = false;
            Run(.6);
            Gone(dummy);
            return dealt;
        }
        var lights = new List<float>(); var heavies = new List<float>();
        for (int i = 0; i < 6; i++) { lights.Add(Swing(false)); heavies.Add(Swing(true)); }
        sim.Hurting = null;
        // (a blow the goblin took on its blade or stepped out of is no measure of how hard it was struck)
        var landedL = lights.Where(d => d > 0).ToList(); var landedH = heavies.Where(d => d > 0).ToList();
        check("a blow held back lands nearly twice as hard as one at a click",
            landedL.Count >= 2 && landedH.Count >= 2 && landedH.Average() > landedL.Average() * 1.4f,
            $"light blows {string.Join(", ", lights.Select(d => d.ToString("0")))}; held back {string.Join(", ", heavies.Select(d => d.ToString("0")))}");

        // a kick shoves a goblin back staggering
        var gob = sim.Spawn(Kinds.Goblin, sim.NearestStand((int)sw.X + 8, (int)sw.Z, 2));
        gob.Guard = true; gob.Home = gob.Cell; gob.Hp = gob.MaxHp = 500;
        Run(.3);
        sim.LookYaw = MathF.Atan2(gob.Z - sw.Z, gob.X - sw.X);
        sim.Stamina = 1;
        Run(1.5);
        sim.WishKick = true;
        sim.Step(.05f);
        check("a kick shoves the goblin before it back, staggering", gob.StaggerUntil > sim.Time && gob.Hp < gob.MaxHp && sim.KickedAt > sim.Time - .1,
            $"staggering {gob.StaggerUntil > sim.Time}, {gob.Hp:0} of {gob.MaxHp:0}");
        Gone(gob); sim.Unpossess(null); Gone(sw);

        // a worker gone into lays what is marked where it looks, and digs what is marked to come out, only that
        var wk = sim.Spawn(Kinds.Worker, sim.NearestStand(wx + 80, wz - 60, 2));
        sim.Possess(wk);
        Run(.3);
        int mx = (int)wk.X + 12, mz = (int)wk.Z, my = w.SurfAt(mx, mz);
        var plan = new VoxelList();
        for (int y = my; y < my + 6; y++) for (int z = mz - 4; z < mz + 4; z++) for (int x = mx; x < mx + 2; x++) plan.Add(x, y, z, Mat.Stone);
        sim.Stock[Stockpile.Stone] = 1000;
        int marked = sim.DesignateBuild(plan);
        // the look swept along the marking as one lays a wall, low and high
        sim.WishLay = true;
        foreach (int lz in new[] { mz - 3, mz, mz + 3 })
            foreach (int ly in new[] { my + 1, my + 4 })
            {
                float ex = mx + 1 - wk.X, ey = ly - (wk.Y + wk.T.BodyH * .88f), ez = lz - wk.Z;
                sim.LookYaw = MathF.Atan2(ez, ex); sim.LookPitch = MathF.Atan2(ey, MathF.Sqrt(ex * ex + ez * ez));
                Run(3);
            }
        sim.WishLay = false;
        int laidMarked = 0, laidElse = 0;
        for (int y = my - 2; y < my + 10; y++) for (int z = mz - 8; z < mz + 8; z++) for (int x = mx - 4; x < mx + 6; x++)
                {
                    byte m = w.Get(x, y, z);
                    if (!Mat.IsPlayer[m]) continue;
                    if (x >= mx && x < mx + 2 && z >= mz - 4 && z < mz + 4 && y >= my && y < my + 6) laidMarked++; else laidElse++;
                }
        check("a worker gone into lays the stone marked where it looks, and nothing else", laidMarked >= marked * 3 / 4 && laidElse == 0,
            $"marked {marked}, laid {laidMarked} of them, {laidElse} elsewhere");
        // marked to come out: the marked half of the wall comes out, the other half stays
        var outPlan = new VoxelList();
        for (int y = my; y < my + 6; y++) for (int z = mz - 4; z < mz; z++) for (int x = mx; x < mx + 2; x++) outPlan.Add(x, y, z, Mat.Air);
        sim.DesignateRemove(outPlan);
        sim.LookYaw = MathF.Atan2(mz - 2 - wk.Z, mx + .5f - wk.X); sim.LookPitch = MathF.Atan2(my + 2 - (wk.Y + wk.T.BodyH * .88f), MathF.Abs(mx - wk.X));
        sim.WishStrike = true;
        Run(8);
        sim.WishStrike = false;
        int leftMarked = 0, leftOther = 0;
        for (int y = my; y < my + 6; y++) for (int z = mz - 4; z < mz + 4; z++) for (int x = mx; x < mx + 2; x++)
                    if (Mat.IsPlayer[w.Get(x, y, z)]) { if (z < mz) leftMarked++; else leftOther++; }
        check("…and digs out what is marked to come out where it looks, and only that", leftMarked < 24 && leftOther >= laidMarked / 2 - 4,
            $"of the marked half {leftMarked} left, of the other {leftOther} standing");
        sim.Unpossess(null);
        Posts(check);
    }

    /// <summary>
    /// A commander uses our works (the author: «снимает лучников с башен, вместо того, чтобы пользоваться ими»). A square
    /// ring of stone round the citadel, 20 m from it, 3 m high, towers a metre higher at its corners, a ladder up the
    /// middle of each wall inside; an archer on each tower, swordsmen and the commander inside, the horde 25 m west. Those
    /// on the western towers stay there; those on the eastern go up onto the western towers or walls, none down to the
    /// ground. With the horde gone and the alarm up, the shooters go up on the side the horde comes from and the foot
    /// stand inside the western wall, not at the citadel's gate; with a gap in that wall, across the gap. Given a spot
    /// to hold by the western wall, the commander keeps its archers on the towers over it.
    /// </summary>
    static void Posts(Action<string, bool, string> check)
    {
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
        foreach (var u in sim.Units) if (u.Team == Team.Player) Gone(u);
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        int kx = site.KeepX, kz = site.KeepZ, fg = site.KeepBase, H = 160;
        // level ground about the citadel and out west, the citadel itself left standing
        for (int z = kz - 210; z <= kz + 210; z++)
            for (int x = kx - 480; x <= kx + 200; x++)
            {
                if ((x - kx) * (x - kx) + (z - kz) * (z - kz) <= (site.KeepR + 3) * (site.KeepR + 3)) continue;
                for (int y = 2; y < w.SY - 1; y++)
                {
                    byte want = y < fg ? (y == fg - 1 ? Mat.Grass : Mat.Soil) : Mat.Air;
                    if (w.Get(x, y, z) != want) w.Set(x, y, z, want);
                }
            }
        // the ring on the metre cells: walls two cells thick and 3 m high, square towers six cells across at the corners a
        // metre higher
        int W0 = ((kx - H) & ~7) - 8, E1 = ((kx + H) & ~7) + 8, N0 = ((kz - H) & ~7) - 8, S1 = ((kz + H) & ~7) + 8;
        sim.InstantLabour = true;
        var ring = new VoxelList();
        for (int z = N0 - 16; z < S1 + 16; z++)
            for (int x = W0 - 16; x < E1 + 16; x++)
            {
                bool inX = x >= W0 && x < E1, inZ = z >= N0 && z < S1;
                bool wall = (x < W0 + 16 || x >= E1 - 16) && inX && inZ || (z < N0 + 16 || z >= S1 - 16) && inX && inZ;
                bool tower = (x < W0 + 32 || x >= E1 - 32) && (z < N0 + 32 || z >= S1 - 32);
                if (!tower && !wall) continue;
                for (int y = fg; y < fg + (tower ? 32 : 24); y++) ring.Add(x, y, z, Mat.Stone);
            }
        sim.DesignateBuild(ring);
        sim.CompleteAll();
        sim.InstantLabour = false;
        // a ladder up the inner face of each wall at its middle
        foreach (var (lx, lz, alongX) in new[] { (W0 + 16, kz, false), (E1 - 18, kz, false), (kx, N0 + 16, true), (kx, S1 - 18, true) })
            for (int t = 0; t < 2; t++)
                for (int y = fg; y < fg + 26; y++) w.Set(lx + (alongX ? t : 0), y, lz + (alongX ? 0 : t), Mat.Ladder);
        Run(.3);
        // (the horde with no timber or earth: no ladder up the ring, it hacks through the wall nearest it)
        sim.HordeStock[Stockpile.Wood] = sim.HordeStock[Stockpile.Earth] = 0;
        sim.ComputeFields();
        int Top(int x, int z) => sim.StandNear(x, fg + 34, z, 2);
        int Apart(int a, int b) => Math.Max(Math.Abs(sim.Nav.I(a) - sim.Nav.I(b)), Math.Abs(sim.Nav.K(a) - sim.Nav.K(b)));
        var corners = new[] { (W0 + 8, N0 + 8), (W0 + 8, S1 - 8), (E1 - 8, N0 + 8), (E1 - 8, S1 - 8) };
        bool Perched(Unit u) { if (u.Post < 0) return false; var (_, py, _) = sim.Nav.Feet(u.Post); return py >= fg + 16; }
        bool West(Unit u) { if (u.Post < 0) return false; var (px, _, _) = sim.Nav.Feet(u.Post); return px < W0 + 40; }
        string Where(Unit u) { if (u.Post < 0) return "no post"; var (px, py, pz) = sim.Nav.Feet(u.Post); return $"({(px - kx) / 8:0},{(py - fg) / 8:0.0},{(pz - kz) / 8:0})"; }
        List<Unit> Muster()
        {
            foreach (var u in sim.Units) if (u.Team == Team.Player) Gone(u);
            var list = new List<Unit>();
            foreach (var (x, z) in corners) list.Add(sim.Spawn(Kinds.Archer, Top(x, z)));
            for (int i = 0; i < 4; i++) list.Add(sim.Spawn(Kinds.Swordsman, sim.NearestStand(kx - 60 + i * 10, kz + 40, 2)));
            list.Add(sim.Spawn(Kinds.Captain, sim.NearestStand(kx - 60, kz - 40, 2)));
            Run(.3);
            return list;
        }

        // the horde 25 m west of the ring
        var ours = Muster();
        var archers = ours.Take(4).ToArray();
        int[] towerCells = archers.Select(a => a.Post).ToArray();
        var band = Enumerable.Range(0, 6).Select(i =>
        {
            var g = sim.Spawn(Kinds.Goblin, sim.NearestStand(W0 - 200, kz - 25 + i * 10, 2));
            g.Guard = true; g.Home = g.Cell; g.ChainedUntil = 1e9;
            return g;
        }).ToArray();
        Run(7);
        bool westStay = archers[0].Post == towerCells[0] && archers[1].Post == towerCells[1] && Apart(archers[0].Cell, towerCells[0]) <= 1 && Apart(archers[1].Cell, towerCells[1]) <= 1;
        check("a commander keeps the archers on the towers over the way the horde comes where they are",
            westStay, $"west towers: {Where(archers[0])}, {Where(archers[1])} (they stood at {string.Join(", ", towerCells.Take(2).Select(c => $"({(sim.Nav.Feet(c).x - kx) / 8:0},{(sim.Nav.Feet(c).z - kz) / 8:0})"))})");
        check("…and sends those on the towers that do not reach the way up onto the towers or walls that do, none down to the ground",
            archers.Skip(2).All(a => Perched(a) && West(a) && a.Ward != null) && archers.All(Perched) && sim.CaptainTowers + sim.CaptainWalls >= 2,
            $"east towers' archers now at {string.Join(", ", archers.Skip(2).Select(Where))}; to towers {sim.CaptainTowers}, to walls {sim.CaptainWalls}");
        // its look over the works afresh (the posts, where a man walks up from it, what each post is worth) costs little
        var lookCap = ours[^1];
        double cpu = 0;
        for (int n = 0; n < 3; n++)
        {
            sim.ForgetPosts();
            lookCap.NextOrders = 0;
            double c0 = BenchClock.CpuMs();
            sim.Step(.05f);
            cpu = Math.Max(cpu, BenchClock.CpuMs() - c0);
        }
        check("a commander's look over the works afresh, posts and all, takes under 15 ms", cpu < 15, $"the worst of 3: {cpu:0.0} ms of work");
        foreach (var g in band) Gone(g);
        Run(1);

        // the alarm, the horde gone: the shooters up on the side it comes from, the foot inside the wall it will come at
        ours = Muster();
        archers = ours.Take(4).ToArray();
        var swords = ours.Skip(4).Take(4).ToArray();
        var (gateX, _, gateZ) = sim.Nav.Feet(sim.Gate);
        int towers0 = sim.CaptainTowers, walls0 = sim.CaptainWalls;
        sim.RaiseAlarm();
        Run(7);
        float lineX = (float)swords.Where(u => u.Post >= 0).Select(u => sim.Nav.Feet(u.Post).x).DefaultIfEmpty(kx).Average();
        check("at the alarm, no horde in sight, a commander posts its archers on the towers and walls on the side the horde comes from",
            archers.All(a => Perched(a) && West(a)) && sim.CaptainTowers + sim.CaptainWalls > towers0 + walls0,
            $"archers at {string.Join(", ", archers.Select(Where))}");
        check("…and stands its foot inside the wall the horde will come at, not at the citadel's gate",
            swords.All(u => u.Post >= 0 && u.Ward != null) && lineX < W0 + 16 + 48 && lineX > W0 + 16 && Math.Abs(lineX - gateX) > 60,
            $"the line at x {(lineX - kx) / 8:0.0} m from the citadel (the western wall's inner face at {(W0 + 16 - kx) / 8:0.0} m, the citadel's gate at {(gateX - kx) / 8:0.0} m)");

        // a gap in the western wall: at the alarm the foot stand across it, a step inside
        sim.StandDown();
        for (int z = kz - 16; z < kz + 16; z++) for (int x = W0; x < W0 + 18; x++) for (int y = fg; y < fg + 26; y++) w.Set(x, y, z, Mat.Air);
        Run(.3);
        sim.ComputeFields();
        ours = Muster();
        swords = ours.Skip(4).Take(4).ToArray();
        int across0 = sim.CaptainAcross;
        sim.RaiseAlarm();
        Run(7);
        bool inGap = swords.All(u => u.Post >= 0 && sim.Nav.Feet(u.Post).x > W0 + 8 && sim.Nav.Feet(u.Post).x < W0 + 16 + 48 && Math.Abs(sim.Nav.Feet(u.Post).z - kz) < 48);
        check("…with a gap in that wall, across the gap a step inside it", inGap && sim.CaptainAcross > across0,
            $"lines across {sim.CaptainAcross - across0}; posts {string.Join(", ", swords.Select(Where))}");
        // one of the horde just outside the wall beside the gap, within a swordsman's reach of his place: the line holds
        // the gap, nobody goes out after it
        var lurker = sim.Spawn(Kinds.Goblin, sim.NearestStand(W0 - 8, kz + 40, 2));
        lurker.Guard = true; lurker.Home = lurker.Cell; lurker.ChainedUntil = 1e9; lurker.Hp = lurker.MaxHp = 5000;
        Run(5);
        float outmost = swords.Min(u => u.X);
        check("…and does not go out past the wall after one of the horde beyond it", outmost > W0 + 8 && swords.All(u => !u.Dead && u.Ward != null),
            $"the westmost of them {(outmost - W0) / 8:0.0} m in from the wall's outer face; the goblin {(lurker.X - W0) / 8:0.0} m");
        Gone(lurker);
        sim.StandDown();
        Run(1);

        // a spot to hold by the western wall: the archers on the towers over it stay there, the rest come up to them
        ours = Muster();
        archers = ours.Take(4).ToArray();
        var cap = ours[^1];
        towerCells = archers.Select(a => a.Post).ToArray();
        sim.Command(new[] { cap }, sim.NearestStand(W0 + 16 + 24, kz + 24, 2));
        cap.Goal = cap.Dest;
        Run(7);
        check("a commander holding a spot by a wall keeps its archers up on the towers and walls over it, not down at the spot",
            archers.All(Perched) && archers[0].Post == towerCells[0] && archers[1].Post == towerCells[1],
            $"archers at {string.Join(", ", archers.Select(Where))}");
    }
}
