using System;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's acid (--acid): a flask's splash runs down off a wall to its foot; it eats earth faster than timber and
/// timber faster than stone, and is spent; water takes it up; whoever stands in it is burnt and coughs; a shield is eaten
/// away; an alchemist stops out of reach and throws at the wall in its way, or into a knot of our people; it is saved and
/// read back.
/// </summary>
static class AcidBench
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
        int kx = site.KeepX, kz = site.KeepZ;
        Unit Camp(UnitType t, int x, int z) { var u = sim.Spawn(t, sim.NearestStand(x, z, t.Move.Height)); u.Guard = true; u.Home = u.Cell; return u; }
        void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
        for (int z = kz - 400; z < kz + 400; z++)
            for (int x = kx - 480; x < kx - 40; x++)
                for (int y = w.SurfAt(x, z), top = w.ColTop(x, z); y < top; y++)
                    if (Mat.IsVeg[w.Get(x, y, z)]) w.Set(x, y, z, Mat.Air);
        int Count(int x0, int z0, int x1, int z1, Func<byte, bool> what)
        {
            int n = 0;
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) for (int y = 4; y < w.SY - 1; y++) if (what(w.Get(x, y, z))) n++;
            return n;
        }

        // three short walls, of stone, of logs and of rammed earth, three metres high, a splash of acid on the top of each
        var walls = new[] { (Mat.Stone, kz - 200), (Mat.Wood, kz - 120), (Mat.Rammed, kz - 40) };
        int wx = kx - 260;
        var before = new int[3];
        var tops = new int[3];
        sim.InstantLabour = true;
        for (int i = 0; i < 3; i++)
        {
            var (m, z) = walls[i];
            tops[i] = Shapes.LevelTop(w, wx, z, 24);
            sim.DesignateBuild(Shapes.Line(w, site, wx, z - 12, wx, z + 12, new Stroke { Mat = m, Width = 8, Height = 24, Level = true }, tops[i]));
        }
        sim.InstantLabour = false;
        for (int i = 0; i < 3; i++)
        {
            var (m, z) = walls[i];
            before[i] = Count(wx - 8, z - 16, wx + 8, z + 16, b => b == m || m == Mat.Stone && Mat.IsMasonry(b));
            sim.FlaskBreaks(wx + .5f, tops[i] + .5f, z + .5f, null, null);
        }
        int splashed = sim.Acids.Count;
        Run(1.5);
        int lowest = sim.Acids.Count == 0 ? 0 : sim.Acids.Min(p => WaterPlanner.Unpack(p).y);
        check("a flask's splash runs off the top of a wall down to its foot",
            splashed >= 3 * Sim.FlaskAcid * 2 / 3 && lowest < tops.Min() - 16, $"{splashed} voxels of acid laid on three walls topping at {tops.Min()}..{tops.Max()}; the lowest now at {lowest}");
        Run(6);
        var eaten = new int[3];
        for (int i = 0; i < 3; i++) { var (m, z) = walls[i]; eaten[i] = before[i] - Count(wx - 8, z - 16, wx + 8, z + 16, b => b == m || m == Mat.Stone && Mat.IsMasonry(b)); }
        check("acid eats rammed earth faster than logs, and logs faster than laid stone, and eats stone too",
            eaten[2] > eaten[1] && eaten[1] > eaten[0] && eaten[0] > 0, $"in 7.5 s: stone {eaten[0]}, logs {eaten[1]}, rammed earth {eaten[2]} voxels");
        Run(272.5);
        check("acid is spent as it eats, and goes flat by itself in a few minutes", sim.Acids.Count <= splashed / 8,
            $"{sim.Acids.Count} of {splashed} voxels left after 280 s; {sim.AcidAte} eaten through in all");

        // into water it is lost at once
        int px = kx - 330, pz = kz + 120, pg = w.SurfAt(px, pz);
        for (int z = pz - 6; z <= pz + 6; z++) for (int x = px - 6; x <= px + 6; x++) for (int y = pg - 6; y < pg; y++) w.Set(x, y, z, Mat.Water);
        int diluted0 = sim.AcidDiluted;
        sim.FlaskBreaks(px + .5f, pg + 2, pz + .5f, null, null);
        Run(1);
        int near = 0;
        foreach (int p in sim.Acids) { var (x, _, z) = WaterPlanner.Unpack(p); if (Math.Abs(x - px) < 12 && Math.Abs(z - pz) < 12) near++; }
        check("water takes acid up: a flask broken over a pond leaves no acid there", near == 0 && sim.AcidDiluted > diluted0, $"{near} voxels left by the pond, {sim.AcidDiluted - diluted0} lost in it");

        // whoever it breaks on is burnt and its skin eaten; who stands in it is burnt about the feet and coughs
        var gob = Camp(Kinds.Goblin, kx - 330, kz + 200);
        float hp0 = gob.Hp;
        sim.FlaskBreaks(gob.X, gob.Y + 8, gob.Z, null, gob);
        check("a flask breaking on one burns it and eats its skin", gob.Hp < hp0 && gob.Wounds != null && gob.Wounds.Wounds.Contains("ожоги кислотой"),
            $"{hp0:0} → {gob.Hp:0} hp; {string.Join(", ", gob.Wounds?.Wounds ?? new())}");
        Gone(gob);
        // (in a pit, so that the acid does not run off downhill)
        int qx = kx - 330, qz = kz + 260, qg = w.SurfAt(qx, qz);
        for (int z = qz - 5; z <= qz + 5; z++) for (int x = qx - 5; x <= qx + 5; x++) for (int y = qg - 3; y < w.ColTop(x, z); y++) w.Set(x, y, z, Mat.Air);
        var worker = Camp(Kinds.Worker, qx, qz);
        worker.Hp = worker.MaxHp = 5000;
        int coughs = 0; sim.Coughed += u => { if (u == worker) coughs++; };
        sim.FlaskBreaks(worker.X + 2, worker.Y + 2, worker.Z, null, null);
        float hp1 = worker.Hp;
        Run(4);
        check("one standing in acid is burnt about the feet, and coughs in its fumes", worker.Hp < hp1 - 5 && coughs > 0, $"{hp1:0} → {worker.Hp:0} hp in 4 s, coughed {coughs} times");
        Gone(worker);

        // a shield under flask after flask is eaten away
        var bearer = Camp(Kinds.Shieldbearer, kx - 330, kz + 320);
        bearer.Hp = bearer.MaxHp = 1e6f;
        int flasks = 0;
        for (; flasks < 80 && (bearer.Wounds?.Shield ?? true); flasks++) sim.FlaskBreaks(bearer.X, bearer.Y + 8, bearer.Z, null, bearer);
        check("a shield is eaten away by the acid flask after flask, and is a shield no longer", bearer.Wounds is { Shield: false },
            $"{flasks} flasks; {string.Join(", ", bearer.Wounds?.Wounds ?? new())}");
        Gone(bearer);
        foreach (int p in sim.Acids.ToList()) { var (x, y, z) = WaterPlanner.Unpack(p); w.Set(x, y, z, Mat.Air); }

        // an alchemist stops out of reach of a stone wall in its way and throws at it
        int ax = kx - 160, az = kz;
        // (on ground made level, so that nothing stands between it and what it throws at)
        int flat = w.SurfAt(ax, az);
        for (int z = az - 40; z <= az + 40; z++)
            for (int x = ax - 150; x <= ax + 30; x++)
            {
                for (int y = flat; y < Math.Max(flat, w.ColTop(x, z)) + 1; y++) w.Set(x, y, z, Mat.Air);
                for (int y = flat - 6; y < flat; y++) w.Set(x, y, z, y == flat - 1 ? Mat.Grass : Mat.Soil);
            }
        int atop = Shapes.LevelTop(w, ax, az, 24);
        sim.InstantLabour = true;
        sim.DesignateBuild(Shapes.Line(w, site, ax, az - 24, ax, az + 24, new Stroke { Mat = Mat.Stone, Width = 8, Height = 24, Level = true }, atop));
        sim.InstantLabour = false;
        var alch = sim.Spawn(Kinds.Alchemist, sim.NearestStand(ax - 110, az, 2));
        alch.Guard = true; alch.Home = alch.Cell; alch.Hp = alch.MaxHp = 1e5f;
        int broken0 = sim.FlasksBroken, ate0 = sim.AcidAte;
        Run(25);
        float stood = alch.X;
        check("an alchemist throws its flasks at the wall in its way to the citadel, and the acid eats it",
            alch.Thrown >= 3 && sim.FlasksBroken - broken0 >= 3 && sim.AcidAte > ate0, $"{alch.Thrown} thrown, {sim.FlasksBroken - broken0} broken, {sim.AcidAte - ate0} voxels eaten");
        // two archers of ours standing together in sight of it: it throws at them instead
        var a1 = Camp(Kinds.Archer, (int)alch.X + 64, (int)alch.Z + 4); var a2 = Camp(Kinds.Archer, (int)alch.X + 64, (int)alch.Z + 10);
        a1.Hp = a1.MaxHp = a2.Hp = a2.MaxHp = 1e5f;
        foreach (var a in new[] { a1, a2 }) { a.Hold = true; a.Dest = a.Cell; }
        alch.Thrown = 0; alch.NextLook = 0;
        Run(6);
        check("an alchemist throws into a knot of our people before at a wall", alch.Target == a1 || alch.Target == a2,
            $"aims at {alch.Target?.T.Key ?? "a place"}; archers' hp {a1.Hp:0}, {a2.Hp:0}");
        Gone(a1); Gone(a2);

        // kept in a save: the acid where it lies and what it has eaten into, what the alchemist has thrown
        sim.FlaskBreaks(ax + .5f, atop + .5f, az + .5f, null, null);
        Run(.5);
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
        var alch2 = sim2.Units.Find(u => u.Id == alch.Id);
        check("acid and what it has eaten into are kept in a save, and the flasks an alchemist has thrown",
            sim2.Acids.Count == sim.Acids.Count && sim2.Acids.Count > 0 && sim2.Etched.Count == sim.Etched.Count && alch2?.Thrown == alch.Thrown,
            $"acid {sim2.Acids.Count} of {sim.Acids.Count}, eaten into {sim2.Etched.Count} of {sim.Etched.Count}, thrown {alch2?.Thrown} of {alch.Thrown}");
        _ = stood;
    }
}
