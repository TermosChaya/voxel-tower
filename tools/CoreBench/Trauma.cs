using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's --trauma: what blunt things do to bodies (Sim.Trauma.cs). The energy of what strikes and where it lands
/// against the harm done, a helmet and big bones against the same blow, falls from a height, a broken arm dropping what
/// it held, a broken leg that takes no ladder, the pinned dug out by ours and by the horde or crushed with nobody to
/// come, a wall brought down on a band, the bones knitting and splinted, and all of it kept in a save.
/// </summary>
static class TraumaBench
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
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        int kx = site.KeepX, kz = site.KeepZ;
        // a clear field west of the citadel, its trees gone
        for (int z = kz - 400; z < kz + 400; z++)
            for (int x = kx - 480; x < kx - 40; x++)
                for (int y = w.SurfAt(x, z), top = w.ColTop(x, z); y < top; y++)
                    if (Mat.IsVeg[w.Get(x, y, z)]) w.Set(x, y, z, Mat.Air);
        Unit At(UnitType t, int x, int z, bool guard = true)
        {
            var u = sim.Spawn(t, sim.NearestStand(x, z, t.Move.Height));
            if (t.Team == Team.Horde) { u.Guard = guard; u.Home = u.Cell; }
            return u;
        }
        void Gone(Unit u) { u.Dead = true; u.DiedAt = -999; }
        void GoneAll(IEnumerable<Unit> us) { foreach (var u in us) Gone(u); }
        bool Notes(Unit u, string s) => u.Wounds != null && u.Wounds.Wounds.Exists(n => n.StartsWith(s));

        // a stone on the head: a worker bare-headed is knocked senseless, a knight in his helmet only dazed
        var bare = new List<Unit>(); var helmed = new List<Unit>();
        for (int i = 0; i < 12; i++) { bare.Add(At(Kinds.Worker, kx - 300 + i * 6, kz - 200)); helmed.Add(At(Kinds.Knight, kx - 300 + i * 6, kz - 180)); }
        foreach (var u in bare.Concat(helmed)) sim.Strike(u, 4.5f, 8.2f, 0, -1, 0, .95f, "камень");
        int knocked = bare.Count(u => u.Down && Notes(u, "сотрясение")), knightsDown = helmed.Count(u => u.Down), knightsDazed = helmed.Count(u => u.DazedUntil > sim.Time);
        check("a stone on the head: the bare-headed knocked senseless, the helmed only dazed",
            knocked >= 10 && knightsDown == 0 && knightsDazed >= 10 && bare.Concat(helmed).All(u => !u.Dead),
            $"151 J: {knocked} of 12 workers senseless, knights {knightsDown} down and {knightsDazed} dazed");
        GoneAll(bare); GoneAll(helmed);

        // a log at the legs: a man's leg goes, a troll's does not
        var men = new List<Unit>(); var trolls = new List<Unit>();
        for (int i = 0; i < 16; i++) { men.Add(At(Kinds.Worker, kx - 300 + i * 6, kz - 140)); trolls.Add(At(Kinds.Troll, kx - 300 + i * 10, kz - 110)); }
        foreach (var u in men.Concat(trolls)) sim.Strike(u, 30, 4.5f, 1, 0, 0, .2f, "бревно");
        int legsMen = men.Count(u => Sim.Lame(u)), legsTrolls = trolls.Count(u => Sim.Lame(u));
        float gait = men.Where(u => Sim.Lame(u)).Select(Sim.Gait).DefaultIfEmpty(1).Max();
        check("a log at the legs breaks a man's leg, not a troll's; the lame goes at a third of the pace",
            legsMen >= 8 && legsTrolls == 0 && gait < .4f && men.Where(u => Sim.Lame(u)).All(u => Notes(u, "сломана")),
            $"304 J: {legsMen} of 16 men lame, {legsTrolls} of 16 trolls; the gait {gait:0.00}");
        GoneAll(men); GoneAll(trolls);

        // a block of a wall on the head is death
        var under = At(Kinds.Swordsman, kx - 300, kz - 80);
        sim.Strike(under, 2000, 6, 0, -1, 0, float.NaN, "обвал");
        check("a tonne of wall on the head kills, and the body says what did it", under.Dead && (Notes(under, "проломлен") || Notes(under, "раздавлен")),
            $"{(under.Dead ? "dead: " + string.Join(", ", under.Wounds?.Wounds ?? new List<string>()) : $"{under.Hp:0} hp")}");
        Gone(under);

        // falls, by the bones alone (no health taken): from two metres nothing, from six a leg may go and nobody dies of
        // it, from twenty nobody gets up
        var low = new List<Unit>(); var mid = new List<Unit>(); var high = new List<Unit>();
        for (int i = 0; i < 16; i++) { low.Add(At(Kinds.Worker, kx - 300 + i * 6, kz - 40)); mid.Add(At(Kinds.Worker, kx - 300 + i * 6, kz - 25)); high.Add(At(Kinds.Worker, kx - 300 + i * 6, kz - 10)); }
        foreach (var u in low) sim.Tumble(u, 2, "падение", 0);
        foreach (var u in mid) sim.Tumble(u, 6, "падение", 0);
        foreach (var u in high) sim.Tumble(u, 20, "падение", 0);
        int lowHurt = low.Count(u => Sim.Broken(u) != Break.None || u.Dead), midLame = mid.Count(Sim.Lame), midDead = mid.Count(u => u.Dead), highDead = high.Count(u => u.Dead);
        check("a fall of 2 m hurts nobody; of 6 m breaks a leg as often as not and kills nobody; of 20 m kills", lowHurt == 0 && midLame >= 3 && midLame <= 14 && midDead == 0 && highDead == 16,
            $"2 m: {lowHurt} hurt of 16; 6 m: {midLame} lame, {midDead} dead; 20 m: {highDead} dead");
        GoneAll(low); GoneAll(mid); GoneAll(high);

        // an arm broken: the sword falls from it, it fights no more, and works at a third
        var sword = At(Kinds.Swordsman, kx - 300, kz + 20);
        var worker = At(Kinds.Worker, kx - 290, kz + 20);
        int rem0 = sim.Remnants.Count;
        bool armed0 = Sim.Armed(sword);
        sim.BreakBone(sword, Break.Main);
        sim.BreakBone(worker, Break.Main);
        check("a broken arm drops what it holds: the swordsman fights no more, the worker works at a third",
            armed0 && !Sim.Armed(sword) && !Sim.HasMainHand(sword) && sim.Remnants.Count > rem0 && sim.Remnants[^1].Grid.Count > 0 && Sim.Hands(worker) < .35f,
            $"armed {armed0} → {Sim.Armed(sword)}, {sim.Remnants.Count - rem0} pieces dropped ({(sim.Remnants.Count > rem0 ? sim.Remnants[^1].Grid.Count : 0)} half-voxels), the worker's hands {Sim.Hands(worker):0.00}");
        Gone(sword); Gone(worker);

        // a broken leg takes no ladder: a tower of stone five metres high with a ladder up its side, a way up it by the
        // ladder for one whole, none for one lame
        {
            int tx = kx - 380, tz = kz + 150, gy = w.SurfAt(tx, tz);
            for (int z = tz - 12; z <= tz + 12; z++)
                for (int x = tx - 12; x <= tx + 12; x++)
                    for (int y = gy - 6; y < gy + 40; y++)
                        if (w.Get(x, y, z) == Mat.Air || Mat.IsVeg[w.Get(x, y, z)]) w.Set(x, y, z, Mat.Stone);
            sim.InstantLabour = true;
            var face = new VoxelHit { X = tx + 12, Y = gy + 4, Z = tz, NX = 1, M = Mat.Stone };
            int rungs = sim.DesignateBuild(Shapes.LadderAt(w, site, face));
            sim.InstantLabour = false;
            sim.Nav.Flush();
            var whole = At(Kinds.Worker, tx + 40, tz);
            var lame = At(Kinds.Worker, tx + 40, tz + 6);
            sim.BreakBone(lame, Break.LegL);
            int wi = tx >> NavGrid.Shift, wk = tz >> NavGrid.Shift, top = -1;
            for (int y = sim.Nav.NY - 2; y > 1 && top < 0; y--) if (sim.Nav.Stand(wi, y, wk, 2)) top = sim.Nav.Id(wi, y, wk);
            bool up = sim.GoTo(whole, top), upLame = sim.GoTo(lame, top);
            check("a broken leg takes no ladder: the whole goes up a tower by it, the lame cannot",
                rungs > 30 && top >= 0 && up && !upLame && !sim.Holds(lame) && sim.Holds(whole),
                $"{rungs} rungs; a way up for the whole {up} ({whole.Path?.Count} cells), for the lame {upLame}");
            Gone(whole); Gone(lame);
        }

        // pinned under a heap: a worker near digs it out, the rubble going to the stores
        void Heap(Unit u, int r, int h, byte m = Mat.Rubble)
        {
            int ux = (int)u.X, uz = (int)u.Z, uy = (int)MathF.Floor(u.Y + .01f);
            for (int y = uy; y < uy + h; y++)
                for (int z = uz - r; z <= uz + r; z++)
                    for (int x = ux - r; x <= ux + r; x++)
                        if (w.Get(x, y, z) == Mat.Air) w.Set(x, y, z, m);
            u.StruckAt = sim.Time;
        }
        var buried = At(Kinds.Worker, kx - 200, kz + 220);
        var digger = At(Kinds.Worker, kx - 170, kz + 220);
        Heap(buried, 2, 9);
        double stone0 = sim.Stock[Stockpile.Stone];
        Run(.5);
        float pinKg = buried.PinKg;
        bool pinned = pinKg > 0 && buried.Down, going = false;
        for (int i = 0; i < 40 && buried.PinKg > 0; i++) { Run(.5); going |= digger.Freeing == buried; }
        Run(2);
        check("one pinned under a heap is dug out by a worker near, and gets up; the rubble goes to the stores",
            pinned && going && buried.PinKg == 0 && !buried.Dead && !buried.Down && sim.Load(buried) < Sim.BodyKg(buried.T) * .3f && sim.Stock[Stockpile.Stone] > stone0,
            $"pinned under {pinKg:0} kg {pinned}; dug by the worker {going}; now {buried.PinKg:0} kg on it, {(buried.Dead ? "dead" : $"{buried.Hp:0} hp")}, down {buried.Down}; stone +{sim.Stock[Stockpile.Stone] - stone0:0}");
        Gone(buried); Gone(digger);

        // the horde digs out its own
        var gob = At(Kinds.Goblin, kx - 360, kz + 300);
        var orc = At(Kinds.Orc, kx - 335, kz + 300);
        Heap(gob, 1, 7);
        Run(.5);
        bool gobPinned = gob.PinKg > 0;
        for (int i = 0; i < 40 && gob.PinKg > 0; i++) Run(.5);
        check("a raider pinned is dug out by its own", gobPinned && gob.PinKg == 0 && !gob.Dead, $"pinned {gobPinned}; now {gob.PinKg:0} kg, {(gob.Dead ? "dead" : $"{gob.Hp:0} hp")}");
        Gone(gob); Gone(orc);

        // pinned under a great weight with nobody to come: crushed (under a block of masonry, which does not slide off as a heap does)
        var lone = At(Kinds.Goblin, kx - 420, kz - 300);
        Heap(lone, 4, 8, Mat.Stone);
        Run(.5);
        float loneKg = lone.PinKg;
        Run(90);
        check("under a great weight with nobody to come, it is crushed", loneKg > 250 && lone.Dead && lone.KilledBy == "задавлен обломками",
            $"{loneKg:0} kg; {(lone.Dead ? "dead: " + lone.KilledBy : $"{lone.Hp:0} hp")}");
        Gone(lone);

        // a wall brought down on a band at its foot: bones broken, heads, the pinned
        foreach (var s in Sim.Spells) sim.Known.Add(s.Kind);
        sim.Favour = 5000;
        {
            int wx = kx - 300, wz0 = kz + 380, wz1 = wz0 + 48;
            int wtop = Shapes.LevelTop(w, wx, (wz0 + wz1) / 2, 48);
            sim.InstantLabour = true;
            sim.DesignateBuild(Shapes.Line(w, site, wx, wz0, wx, wz1, new Stroke { Mat = Mat.Stone, Width = 10, Height = 48, Level = true }, wtop));
            sim.InstantLabour = false;
            sim.Nav.Flush();
            var band = new List<Unit>();
            for (int i = 0; i < 6; i++) band.Add(At(Kinds.Orc, wx - 9, wz0 + 6 + i * 7));
            string cast = sim.Cast(SpellKind.Fall, wx, wtop - 40, (wz0 + wz1) / 2);
            Run(6);
            int harmed = band.Count(u => Sim.Broken(u) != Break.None || u.PinKg > 0 || Notes(u, "проломлен") || Notes(u, "раздавлен") || Notes(u, "сотрясение"));
            check("a wall brought down on a band at its foot breaks bones and heads, and pins some under it",
                cast == null && harmed >= 2,
                $"cast: {cast ?? "yes"}; of 6 orcs {harmed} with harms: " + string.Join("; ", band.Select(u => (u.Dead ? "dead " : "") + (u.PinKg > 0 ? $"pinned {u.PinKg:0} kg " : "") + string.Join(", ", u.Wounds?.Wounds ?? new List<string>()))));
            GoneAll(band);
        }

        // the bones knit: by the citadel thrice as fast; a comrade splints them
        var mend = At(Kinds.Worker, kx - 60, kz);
        sim.BreakBone(mend, Break.LegR);
        var hand = At(Kinds.Worker, kx - 300, kz - 290);
        var splinted = At(Kinds.Worker, kx - 300, kz - 300);
        sim.BreakBone(splinted, Break.LegL);
        mend.Wounds.Knit[0] = .97f;
        Run(12);
        bool knitHome = !Sim.Lame(mend) && !Notes(mend, "сломана");
        for (int i = 0; i < 30 && (splinted.Wounds.Splinted & Break.LegL) == 0; i++) Run(1);
        check("bones knit, three times as fast by the citadel; a comrade near splints a broken leg",
            knitHome && (splinted.Wounds.Splinted & Break.LegL) != 0 && Notes(splinted, "сломана левая нога, в лубке"),
            $"knit by the citadel {knitHome}; splinted {(splinted.Wounds.Splinted & Break.LegL) != 0} ({string.Join(", ", splinted.Wounds.Wounds)})");

        // kept in a save
        var pin = At(Kinds.Worker, kx - 420, kz + 420);
        Heap(pin, 1, 8);
        Run(.5);
        var ms = new System.IO.MemoryStream();
        sim.Save(ms, mp.Seed, "trauma", "");
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
        var s2 = sim2.Units.Find(u => u.Id == splinted.Id);
        var p2 = sim2.Units.Find(u => u.Id == pin.Id);
        check("broken bones, splints and the pinned are kept in a save",
            s2 != null && Sim.Broken(s2) == Break.LegL && (s2.Wounds.Splinted & Break.LegL) != 0 && MathF.Abs(s2.Wounds.Knit[1] - splinted.Wounds.Knit[1]) < 1e-4f && p2 != null && p2.PinKg > 0 && pin.PinKg > 0,
            $"bones {(s2 == null ? "-" : Sim.Broken(s2).ToString())}, splinted {(s2 == null ? "-" : s2.Wounds.Splinted.ToString())}, pinned {pin.PinKg:0} → {(p2 == null ? "-" : p2.PinKg.ToString("0"))} kg");
    }
}
