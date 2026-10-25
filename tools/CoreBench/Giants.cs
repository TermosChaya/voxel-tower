using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The hill giant and the great ones' boulders (--giants): when it comes, how it looks, its field through a ditch of
/// water; a throw at a wall (the hole in the ground, where it strikes, what it breaks, a breach after a few), through a
/// file of ours on the open ground and bouncing on; ours sparing our wall; smashing a wall by hand; swatting; arrows on
/// its hide; and saved and read back.
/// </summary>
static class GiantsBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        var G = Kinds.HillGiant;
        // when it comes and what it is
        check("a hill giant comes from the ninth wave, one a night: a horde kind with a field of its own, a body of its own taller than a troll's, wading two metres",
            !Sim.Roster(8, 1).Contains(G) && Sim.Roster(9, 1).Count(t => t == G) == 1 && Sim.Roster(12, 1.6f).Count(t => t == G) == 1 && Kinds.HordeKinds.Contains(G)
            && G.Team == Team.Horde && Bodies.Looks(G) == 2 && Bodies.Plan(G, 0) != Bodies.Plan(Kinds.Troll, 0) && Bodies.Plan(G, 0).Top > Bodies.Plan(Kinds.Troll, 0).Top
            && G.Move.Wade == 2 && Kinds.Troll.Move.Wade == 0,
            $"wave 8: {Sim.Roster(8, 1).Count(t => t == G)}, 9: {Sim.Roster(9, 1).Count(t => t == G)}, 12: {Sim.Roster(12, 1.6f).Count(t => t == G)}; top {Bodies.Plan(G, 0).Top} (a troll's {Bodies.Plan(Kinds.Troll, 0).Top})");

        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
        float Metres(float a, float b, float c, float d) => MathF.Sqrt((a - c) * (a - c) + (b - d) * (b - d)) / VoxelWorld.PerMetre;
        // a field of level ground west of the citadel, bare of trees
        int fx0 = (site.KeepX - 460) & ~7, fz0 = (site.KeepZ - 140) & ~7, fg = w.SurfAt(fx0 + 160, fz0 + 140);
        for (int z = fz0; z < fz0 + 280; z++)
            for (int x = fx0; x < fx0 + 320; x++)
                for (int y = fg - 8; y < fg + 50; y++) w.Set(x, y, z, y < fg ? (y < fg - 4 ? Mat.Rock : Mat.Soil) : Mat.Air);
        Run(.5);
        Unit Put(UnitType t, int x, int z, bool guard = true)
        {
            int c = sim.NearestStand(x, z, t.Move.Height);
            var u = sim.Spawn(t, c);
            (u.X, u.Y, u.Z) = sim.Nav.Feet(c);
            if (t.Team == Team.Horde) { if (guard) { u.Guard = true; u.Home = u.Cell; } }
            else { u.Hold = true; u.Dest = u.Cell; u.Post = u.Cell; }
            return u;
        }
        int Count(int x0, int y0, int z0, int x1, int y1, int z1, Func<byte, bool> what)
        {
            int n = 0;
            for (int y = y0; y < y1; y++) for (int z = z0; z < z1; z++) for (int x = x0; x < x1; x++) if (what(w.Get(x, y, z))) n++;
            return n;
        }
        var landed = new List<(float x, float y, float z)>();
        sim.Landed += (x, y, z, n, s) => { if (n >= 60) landed.Add((x, y, z)); };

        // a wall of ours across the field, eight voxels thick; a hill giant eighteen metres short of it throws: it tears
        // its stone out of the ground before its feet, and the stone strikes the wall near where it was thrown at
        int wx0 = fx0 + 200, wz0 = fz0 + 100, wz1 = fz0 + 180;
        for (int z = wz0; z < wz1; z++) for (int x = wx0; x < wx0 + 8; x++) for (int y = fg; y < fg + 32; y++) w.Set(x, y, z, Mat.Stone);
        Run(.2);
        bool Stone(byte m) => m == Mat.Stone;
        int wall0 = Count(wx0, fg, wz0, wx0 + 8, fg + 32, wz1, Stone);
        var giant = Put(G, wx0 - 150, fz0 + 140);
        giant.Hp = giant.MaxHp = 1e6f;
        int gx = (int)giant.X, gz = (int)giant.Z;
        bool Solid(byte m) => m != Mat.Air && !Mat.Passable[m];
        int ground0 = Count(gx, fg - 16, gz - 16, gx + 24, fg + 2, gz + 16, Solid);
        float aimX = wx0, aimY = fg + 13, aimZ = fz0 + 140;
        landed.Clear();
        bool ordered = sim.OrderHurl(giant, aimX, aimY, aimZ);
        var stone = giant.Hefted;
        int stoneVox = stone?.Voxels.Count ?? 0;
        int ground1 = Count(gx, fg - 16, gz - 16, gx + 24, fg + 2, gz + 16, Solid);
        bool over = false, struck = false;
        var hit = (x: 0f, y: 0f, z: 0f);
        for (int i = 0; i < 120 && !struck; i++)
        {
            Run(.05);
            if (stone != null && giant.Hefted == stone && stone.Y > giant.Y + giant.T.BodyH) over = true;
            if (sim.GiantBroke > 0 && stone != null) { struck = true; hit = (stone.X, stone.Y, stone.Z); }
        }
        Run(1);
        int wall1 = Count(wx0, fg, wz0, wx0 + 8, fg + 32, wz1, Stone);
        check("a hill giant tears a stone out of the ground before its feet, raises it over its head and hurls it eighteen metres: it strikes the wall near its mark and breaks it",
            ordered && stoneVox >= 120 && ground0 - ground1 >= 40 && over && struck && Metres(hit.x, hit.z, aimX, aimZ) < 3 && wall0 - wall1 >= 80 && sim.GiantHurled == 1,
            $"stone {stoneVox} voxels, the ground {ground0} → {ground1}; over its head {over}; struck {(struck ? $"{Metres(hit.x, hit.z, aimX, aimZ):0.0} m from the mark" : "nothing")}; the wall {wall0} → {wall1}");

        // more of them, and the wall is broken through
        int throws = 1;
        for (int i = 0; i < 600 && throws < 8; i++)
        {
            if (!sim.Hurling(giant) && sim.OrderHurl(giant, aimX, aimY, aimZ)) throws++;
            Run(.1);
        }
        Run(4);
        int through = 0;
        for (int y = fg; y < fg + 24; y++)
            for (int z = wz0; z < wz1; z++)
            {
                bool open = true;
                for (int x = wx0; x < wx0 + 8 && open; x++) if (w.Get(x, y, z) != Mat.Air) open = false;
                if (open) through++;
            }
        int wall2 = Count(wx0, fg, wz0, wx0 + 8, fg + 32, wz1, Stone);
        check("after eight boulders the wall is broken through", throws == 8 && through >= 6 && sim.GiantBroke > 400,
            $"{throws} thrown; the wall {wall0} → {wall2}, {sim.GiantBroke} of ours broken; {through} rows open right through");
        Gone(giant);
        Run(3);

        // on the open ground: thrown at a file of three of ours close together, it knocks them flying and goes on
        // through them, bounces, and rolls
        int ox = fx0 + 40, oz = fz0 + 30;
        var giant2 = Put(G, ox, oz);
        giant2.Hp = giant2.MaxHp = 1e6f;
        var file = new[] { Put(Kinds.Spearman, ox + 96, oz), Put(Kinds.Spearman, ox + 104, oz), Put(Kinds.Spearman, ox + 112, oz) };
        foreach (var s in file) s.Hp = s.MaxHp = 5000;
        Run(.3);
        var fileHp = file.Select(s => s.Hp).ToArray();
        sim.OrderHurl(giant2, file[0].X, file[0].Y + 3, file[0].Z);
        var stone2 = giant2.Hefted;
        bool rolled = false; int bounced = 0;
        var flew = new HashSet<Unit>();
        for (int i = 0; i < 200; i++)
        {
            Run(.05);
            foreach (var s in file) if (s.Airborne != null) flew.Add(s);
            if (stone2 != null) { bounced = Math.Max(bounced, stone2.Bounces); rolled |= stone2.Rolling; }
        }
        int hurt = file.Count(s => s.Hp < fileHp[Array.IndexOf(file, s)] - 50);
        check("a boulder thrown at a file of three of ours knocks the first flying and ploughs on, through the next or bouncing over; it comes down and rolls",
            stone2 != null && flew.Contains(file[0]) && hurt >= 2 && (stone2.Struck?.Count ?? 0) >= 1 && (bounced >= 1 || (stone2.Struck?.Count ?? 0) >= 2) && rolled,
            $"thrown {flew.Count}, hurt {hurt}, struck {stone2?.Struck?.Count ?? 0}; bounced {bounced}, rolled {rolled}");
        foreach (var s in file) Gone(s);
        // thrown aslant at nobody, far off, on the open ground it bounces on
        var stone3 = sim.OrderHurl(giant2, ox + 230, fg + 3, oz + 60) ? giant2.Hefted : null;
        int bounced3 = 0;
        var path3 = new List<string>();
        for (int i = 0; i < 160; i++)
        {
            Run(.05);
            if (stone3 == null) continue;
            bounced3 = Math.Max(bounced3, stone3.Bounces);
            if (i % 8 == 0) path3.Add($"{(stone3.X - ox) / 8:0.0},{(stone3.Y - fg) / 8:0.0}{(stone3.Rolling ? "r" : "")}");
        }
        check("a boulder coming down aslant on the open ground knocks a bowl out of it and bounces on", stone3 != null && bounced3 >= 1, $"bounced {bounced3}; its way {string.Join(" ", path3)}");
        Gone(giant2);

        // a giant of ours with the lore of boulders throws at our own wall: it breaks none of it (what the tests before
        // it broke let come down first, lest a piece still falling strike it, and what they cracked crumble out)
        for (int i = 0; i < 60 && (sim.Fliers.Count > 0 || sim.Cracking.Count > 0); i++) Run(.5);
        sim.Fliers.Clear();
        sim.Learnt.Add("boulders");
        var ours = Put(Kinds.Giant, wx0 - 120, fz0 + 110);
        int wallO = Count(wx0, fg, wz0, wx0 + 8, fg + 32, wz1, Stone);
        landed.Clear();
        bool oursThrew = sim.OrderHurl(ours, wx0, fg + 12, fz0 + 110);
        Run(5);
        int wallO1 = Count(wx0, fg, wz0, wx0 + 8, fg + 32, wz1, Stone);
        check("a giant of ours throws the same way, and its boulder breaks nothing of ours", oursThrew && sim.Hurled >= 1 && wallO1 == wallO,
            $"thrown {oursThrew}, ours hurled {sim.Hurled}; our wall {wallO} → {wallO1}");
        Gone(ours);
        sim.Learnt.Remove("boulders");

        // up against a wall it smashes through a heap at a blow, the pieces flying
        int sx0 = fx0 + 280, sz0 = fz0 + 200;
        for (int z = sz0; z < sz0 + 40; z++) for (int x = sx0; x < sx0 + 8; x++) for (int y = fg; y < fg + 32; y++) w.Set(x, y, z, Mat.Stone);
        Run(.2);
        var smasher = Put(G, sx0 - 8, sz0 + 20, guard: false);
        smasher.Hp = smasher.MaxHp = 1e6f;
        int s0 = Count(sx0, fg, sz0, sx0 + 8, fg + 32, sz0 + 40, Stone);
        sim.SmashInto(smasher, sim.Nav.CellOf(sx0 + 2, (int)smasher.Y + 1, (int)smasher.Z));
        int pieces = 0;
        for (int i = 0; i < 80; i++)
        {
            Run(.1);
            pieces = Math.Max(pieces, sim.Fliers.Count(f => f.Cause == Sim.ChipsCause));
            if (smasher.CarveCell < 0) break;
        }
        int s1 = Count(sx0, fg, sz0, sx0 + 8, fg + 32, sz0 + 40, Stone);
        check("a hill giant at a wall breaks out a heap of it at every blow, the pieces flying, till the way is open",
            s0 - s1 >= 400 && pieces >= 5,
            $"the wall {s0} → {s1}; pieces in the air at once {pieces}; the way open {smasher.CarveCell < 0}");
        Gone(smasher);

        // whoever it strikes flies, and the one beside too
        var swatter = Put(G, fx0 + 60, fz0 + 230);
        swatter.Hp = swatter.MaxHp = 1e6f;
        var a = Put(Kinds.Spearman, fx0 + 60 + 16, fz0 + 228);
        var b = Put(Kinds.Spearman, fx0 + 60 + 16, fz0 + 236);
        a.Hp = a.MaxHp = b.Hp = b.MaxHp = 5000;
        var swatted = new HashSet<Unit>();
        for (int i = 0; i < 120; i++) { Run(.05); foreach (var s in new[] { a, b }) if (s.Airborne != null) swatted.Add(s); }
        check("a hill giant's blow throws the one it strikes into the air, and the one of ours beside it", swatted.Count == 2 && a.Hp < 5000 && b.Hp < 5000,
            $"thrown {swatted.Count}; {a.Hp:0} and {b.Hp:0} of 5000");
        foreach (var u in new[] { swatter, a, b }) Gone(u);

        // arrows hardly go through its hide: an archer at a hill giant does a fraction of what another does at an orc
        (float lost, int hits) Shot(UnitType t)
        {
            var target = Put(t, fx0 + 40, fz0 + 220);
            target.ChainedUntil = 1e9; target.Hp = target.MaxHp = 5000;
            var archer = Put(Kinds.Archer, fx0 + 140, fz0 + 220);
            int hits0 = sim.ShotHits;
            Run(20);
            var r = (5000 - target.Hp, sim.ShotHits - hits0);
            Gone(target); Gone(archer);
            Run(2);
            return r;
        }
        var (lostG, hitsG) = Shot(G);
        var (lostO, hitsO) = Shot(Kinds.Orc);
        float perG = lostG / Math.Max(1, hitsG), perO = lostO / Math.Max(1, hitsO);
        check("arrows hardly go through a hill giant's hide: an arrow does it under half of what it does an orc in its mail", hitsG > 3 && hitsO > 3 && perG < perO / 2,
            $"the giant lost {lostG:0} in {hitsG} hits ({perG:0.0} each), the orc {lostO:0} in {hitsO} ({perO:0.0} each)");
        Run(1);

        // a ditch of water two metres deep across the field: the giant's field goes through it, a troll's does not; and
        // set marching, it wades over
        int dx0 = fx0 + 120;
        for (int z = fz0; z < fz0 + 280; z++) for (int x = dx0; x < dx0 + 24; x++) for (int y = fg - 16; y < fg; y++) w.Set(x, y, z, y < fg - 3 ? Mat.Water : Mat.Air);
        Run(.5);
        sim.ComputeFields();
        int bottom = sim.Nav.CellOf(dx0 + 12, fg - 15, fz0 + 140);
        bool deep = sim.Nav.Deep(bottom), wades = G.Move.Wades(sim.Nav, sim.Nav.I(bottom), sim.Nav.Y(bottom), sim.Nav.K(bottom));
        bool fieldG = !float.IsInfinity(sim.Fields[G].Dist[bottom]), fieldT = !float.IsInfinity(sim.Fields[Kinds.Troll].Dist[bottom]);
        var wader = Put(G, dx0 - 16, fz0 + 140, guard: false);
        wader.Hp = wader.MaxHp = 1e6f;
        bool wet = false;
        for (int i = 0; i < 300 && wader.X < dx0 + 40; i++) { Run(.1); if (wader.X > dx0 + 4 && wader.X < dx0 + 20 && wader.Y < fg - 8) wet = true; }
        check("a hill giant wades a ditch two metres deep that a troll must bridge: its field goes through the water, and it walks over the bottom",
            deep && wades && fieldG && !fieldT && wet && wader.X > dx0 + 30,
            $"deep {deep}, wades {wades}; its field there {fieldG}, a troll's {fieldT}; in the water {wet}, now {(wader.X - dx0) / VoxelWorld.PerMetre:0.0} m past the near bank");

        // left to itself: a ring of stone round the citadel, and a hill giant coming at it by its field throws at the
        // wall on its way and breaks it (the walls of the field taken away first: it comes on across the field, and one
        // of them right before it would take the stone thrown at it point-blank back down on its head)
        for (int z = fz0; z < fz0 + 280; z++)
            for (int x = fx0; x < fx0 + 320; x++)
                for (int y = fg; y < fg + 50; y++) if (w.Get(x, y, z) != Mat.Air) w.Set(x, y, z, Mat.Air);
        int kx = site.KeepX, kz = site.KeepZ, ring = site.KeepR + 70;
        for (int z = kz - ring - 8; z <= kz + ring + 8; z++)
            for (int x = kx - ring - 8; x <= kx + ring + 8; x++)
            {
                float d = MathF.Sqrt((x - kx) * (x - kx) + (z - kz) * (z - kz));
                if (d < ring || d >= ring + 8) continue;
                int g = w.GroundTop(x, z);
                for (int y = g + 1; y < g + 33; y++) w.Set(x, y, z, Mat.Stone);
            }
        Run(.5);
        sim.ComputeFields();
        int thrown0 = sim.GiantHurled, broke0 = sim.GiantBroke;
        var comer = sim.Spawn(G, sim.NearestStand(kx - ring - 200, kz, 3));
        comer.Hp = comer.MaxHp = 1e6f;
        for (int i = 0; i < 900 && sim.GiantBroke == broke0; i++) Run(.1);
        float left = MathF.Sqrt((comer.X - kx) * (comer.X - kx) + (comer.Z - kz) * (comer.Z - kz)) - ring;
        check("a hill giant left to itself, coming at a ring of stone round the citadel, throws at the wall on its way and breaks it",
            sim.GiantHurled > thrown0 && sim.GiantBroke > broke0,
            $"thrown {sim.GiantHurled - thrown0}, broke {sim.GiantBroke - broke0} of ours, {left / VoxelWorld.PerMetre:0} m from the wall at {sim.Time:0} s");
        Gone(comer);

        // saved and read back, it is a hill giant still
        var ms = new System.IO.MemoryStream();
        sim.Save(ms, mp.Seed, "giants", "");
        ms.Position = 0;
        var wL = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        Sim simL;
        using (var reader = new SaveReader(ms))
        {
            var siteL = MapGen.Generate(wL, new MapParams { Seed = reader.Seed });
            wL.Compact();
            reader.Valley(wL, siteL);
            simL = new Sim(wL, siteL);
            simL.Load(reader);
        }
        var back = simL.Units.Find(u => u.Id == wader.Id);
        check("a hill giant is saved and read back as itself", back != null && back.T == G && back.Look == wader.Look, back == null ? "gone" : back.T.Key);
    }
}
