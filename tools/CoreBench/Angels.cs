using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The faith's spells of the sky and the angels (--angels): the guardian over a file of ours and what it keeps off them;
/// the cherub at its spot cutting down the horde that comes near and sparing ours; the destroyer over a band and the
/// third of it that falls; the host come down, fighting and going up again and leaving nothing; hail on a band and its
/// ladder, and our wall under it; the pillar of fire burning those at it and lighting the night about it.
/// </summary>
static class AngelsBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        const float M = VoxelWorld.PerMetre;
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        // a broad field west of the citadel, level and bare
        int fx0 = (site.KeepX - 760) & ~7, fz0 = (site.KeepZ - 300) & ~7, fg = w.SurfAt(fx0 + 300, fz0 + 300);
        for (int z = fz0; z < fz0 + 600; z++)
            for (int x = fx0; x < fx0 + 600; x++)
            {
                if (x < 4 || z < 4 || x >= w.SX - 4 || z >= w.SZ - 4) continue;
                for (int y = fg - 12; y < fg + 60; y++) w.Set(x, y, z, y < fg ? (y < fg - 5 ? Mat.Rock : Mat.Soil) : Mat.Air);
                site.HTop[z * w.SX + x] = fg;
            }
        Run(.5);
        foreach (var s in Sim.Spells) if (!s.Draft) sim.Known.Add(s.Kind);
        Unit Put(UnitType t, float x, float z)
        {
            int c = sim.NearestStand(Math.Clamp((int)x, 8, w.SX - 9), Math.Clamp((int)z, 8, w.SZ - 9), t.Move.Height);
            var u = sim.Spawn(t, c);
            (u.X, u.Y, u.Z) = sim.Nav.Feet(c);
            if (t.Team == Team.Horde) { u.Guard = true; u.Home = u.Cell; }
            else { u.Hold = true; u.Dest = u.Cell; u.Post = u.Cell; }
            return u;
        }
        void Gone(IEnumerable<Unit> us) { foreach (var u in us) if (!u.Dead) { u.Dead = true; u.DiedAt = -999; } }
        List<Unit> Band(int n, float x, float z, Func<int, UnitType> kind = null)
        {
            var b = new List<Unit>();
            for (int i = 0; i < n; i++) b.Add(Put(kind?.Invoke(i) ?? (i % 3 == 0 ? Kinds.Orc : Kinds.Goblin), x + (i % 6 - 2.5f) * 1.5f * M, z + (i / 6 - 1.5f) * 1.5f * M));
            return b;
        }
        string Cast(SpellKind k, float x, float z) { sim.Favour = 300; sim.ReadyNow(k); return sim.Cast(k, x, fg, z); }
        float cx = fx0 + 300, cz = fz0 + 300;

        // ---- the guardian: a blow on one of ours under it takes a third
        var file = new List<Unit>();
        for (int i = 0; i < 4; i++) file.Add(Put(Kinds.Swordsman, cx + i * 1.5f * M, cz));
        float hp0 = file[0].Hp;
        sim.Hurt(file[0], null, 30, "test");
        float bare = hp0 - file[0].Hp;
        string g = Cast(SpellKind.Guardian, cx + 2 * M, cz);
        Run(Deities.Guardian.RiseFor + .3);
        var guard = sim.Gods.Find(d => d.T.Kind == DeityKind.Guardian);
        float hp1 = file[0].Hp;
        sim.Hurt(file[0], null, 30, "test");
        float warded = hp1 - file[0].Hp;
        var far = Put(Kinds.Swordsman, cx + 20 * M, cz);
        float hp2 = far.Hp;
        sim.Hurt(far, null, 30, "test");
        float unwarded = hp2 - far.Hp;
        check("a guardian angel hovers over the spot, and a blow on one of ours under it takes a third of what it would; one away from it takes it all",
            g == null && guard != null && guard.Act == DeityAct.Standing && MathF.Abs(warded - bare / 3) < .5f && MathF.Abs(unwarded - bare) < .5f && sim.OurGod == null,
            $"cast «{g}», {guard?.Act}; the blow {bare:0.0} bare, {warded:0.0} under it, {unwarded:0.0} away; the household's god {sim.OurGod?.T.Name ?? "none"}");
        Run(62);
        check("its minute over, it goes up and is gone", guard != null && guard.Gone && !sim.Gods.Exists(d => !d.Gone), $"gone {guard?.Gone}");
        Gone(file); Gone(new[] { far });

        // ---- the cherub: cuts down the horde that comes near, spares ours by it
        string ch = Cast(SpellKind.Cherub, cx, cz);
        Run(Deities.Cherub.RiseFor + .3);
        var cherub = sim.Gods.Find(d => !d.Gone && d.T.Kind == DeityKind.Cherub);
        var ours = new List<Unit> { Put(Kinds.Swordsman, cx + 3 * M, cz + 2 * M), Put(Kinds.Swordsman, cx + 3 * M, cz - 2 * M) };
        var near = Band(12, cx - 6 * M, cz);
        foreach (var u in near) u.Guard = false;
        var acts = new HashSet<DeityAct>();
        for (int i = 0; i < 400; i++) { Run(.05); if (cherub != null) acts.Add(cherub.Act); }
        int cut = near.Count(u => u.Dead), oursUp = ours.Count(u => !u.Dead);
        check("the cherub stands at its spot and cuts down the horde that comes within its sword's reach, and does not touch ours by it",
            ch == null && cherub != null && acts.Contains(DeityAct.Sweep) && cut >= 6 && oursUp == 2 && MathF.Abs(cherub.X - cx) < 1,
            $"cast «{ch}»; acts {string.Join(",", acts)}; cut down {cut} of {near.Count}; ours alive {oursUp} of 2; moved {MathF.Abs((cherub?.X ?? cx) - cx) / M:0.0} m");
        if (cherub != null) cherub.Until = sim.Time;
        Run(4);
        Gone(near); Gone(ours);

        // ---- the destroyer: a third of the band under it falls dead, without a wound
        var band = Band(36, cx - 30 * M, cz, i => Kinds.Goblin);
        string de = Cast(SpellKind.Destroyer, cx - 30 * M, cz);
        var destroyer = sim.Gods.Find(d => !d.Gone && d.T.Kind == DeityKind.Destroyer);
        float hiest = 0;
        for (int i = 0; i < 100 && destroyer is { Gone: false }; i++) { Run(.05); hiest = MathF.Max(hiest, (destroyer.Y - fg) / M); }
        int fell = band.Count(u => u.Dead && u.KilledBy == Deities.Destroyer.Cause), hurt = band.Count(u => !u.Dead && u.Hp < u.MaxHp);
        check("the destroying angel passes high over the band, and about one in three of it falls dead where it stands; the rest are untouched",
            de == null && destroyer != null && destroyer.Gone && hiest > 10 && fell >= 6 && fell <= 20 && hurt == 0,
            $"cast «{de}»; as high as {hiest:0} m; fell {fell} of {band.Count}, hurt {hurt}; gone {destroyer?.Gone}");
        Gone(band);

        // ---- the host: six come down, fight, and go up again leaving nothing
        var foes = Band(12, cx - 6 * M, cz + 20 * M);
        foreach (var u in foes) u.Guard = false;
        string ho = Cast(SpellKind.Host, cx, cz + 20 * M);
        var host = sim.Units.Where(u => u.T == Kinds.Angel && !u.Dead).ToList();
        Run(30);
        int beaten = foes.Count(u => u.Dead);
        check("the heavenly host comes down: six warriors of heaven, who go for the horde and beat it",
            ho == null && host.Count == 6 && beaten >= 6, $"cast «{ho}»; came {host.Count}; the horde dead {beaten} of {foes.Count}; the host up {host.Count(u => !u.Dead)}");
        Run(32);
        int ascended = host.Count(u => u.KilledBy == Sim.AscendCause && u.DiedAt == -999);
        check("its minute over, every one of the host still standing goes up again and leaves nothing on the field, and none counts as one of ours lost",
            host.All(u => u.Dead) && ascended >= 1 && host.Where(u => u.KilledBy == Sim.AscendCause).All(u => u.DiedAt == -999),
            $"went up {ascended} of {host.Count}, all gone {host.All(u => u.Dead)}");
        Gone(foes);

        // ---- hail: on the band and its ladder, not on our wall
        int lx = (int)(cx - 10 * M), lz = (int)(cz - 40 * M);
        for (int y = fg; y < fg + 40; y++) for (int z = lz; z < lz + 8; z++) { w.Set(lx, y, z, Mat.SiegeLadder); w.Set(lx + 1, y, z, Mat.SiegeLadder); }
        int wx = (int)(cx + 6 * M);
        for (int y = fg; y < fg + 16; y++) for (int z = lz - 24; z < lz + 32; z++) for (int x = wx; x < wx + 4; x++) w.Set(x, y, z, Mat.Stone);
        int Count(byte m, int x0, int x1, int z0, int z1) { int n = 0; for (int y = fg; y < fg + 44; y++) for (int z = z0; z < z1; z++) for (int x = x0; x < x1; x++) if (w.Get(x, y, z) == m) n++; return n; }
        // (what the bench set straight into the world settles first: the wall's loose top course comes down)
        Run(6);
        int ladder0 = Count(Mat.SiegeLadder, lx, lx + 2, lz, lz + 8), wall0 = Count(Mat.Stone, wx, wx + 4, lz - 24, lz + 32);
        var under = Band(18, cx - 4 * M, lz + 4);
        string ha = Cast(SpellKind.Hail, cx - 2 * M, lz + 4);
        int stones = 0;
        for (int i = 0; i < 140; i++) { Run(.05); stones = Math.Max(stones, sim.Fliers.Count(f => f.Hail)); }
        Run(3);
        int ladder1 = Count(Mat.SiegeLadder, lx, lx + 2, lz, lz + 8), wall1 = Count(Mat.Stone, wx, wx + 4, lz - 24, lz + 32);
        int struck = under.Count(u => u.Dead || u.Hp < u.MaxHp);
        check("hail comes down on the circle: stones in the air, the band under it struck, the horde's ladder broken into, our wall not touched, and no heaps of ice left",
            ha == null && stones >= 5 && struck >= 8 && ladder1 < ladder0 && wall1 == wall0 && sim.Fliers.Count(f => f.Hail) == 0,
            $"cast «{ha}»; up to {stones} stones in the air; struck {struck} of {under.Count}; the ladder {ladder0} → {ladder1}; our wall {wall0} → {wall1}");
        Gone(under);

        // ---- the pillar of fire: burns and throws back those at it, lights the night about it
        sim.Waves.StartNow(sim);
        sim.Waves._queue.Clear();
        foreach (var u in sim.Units) if (u.Team == Team.Horde && !u.Dead && !u.Guard) { u.Dead = true; u.DiedAt = -999; }
        var straggler = Put(Kinds.Goblin, cx - 80 * M, cz);
        straggler.ChainedUntil = 1e9;
        float px = cx - 40 * M, pz = cz + 40 * M;
        for (int i = 0; i < 400 && sim.Clock.Darkness < .5f; i++) Run(1);
        bool darkBefore = !sim.Lit(px + 20 * M, pz);
        string pi = Cast(SpellKind.FirePillar, px, pz);
        var atIt = new List<Unit>();
        for (int i = 0; i < 6; i++) atIt.Add(Put(Kinds.Goblin, px + MathF.Cos(i) * 1.6f * M, pz + MathF.Sin(i) * 1.6f * M));
        Run(2.5);
        int burnt = atIt.Count(u => u.Dead || u.Burn > 0 || u.Hp < u.MaxHp);
        float pushed = atIt.Where(u => !u.Dead).Select(u => MathF.Sqrt((u.X - px) * (u.X - px) + (u.Z - pz) * (u.Z - pz)) / M).DefaultIfEmpty(99).Min();
        bool litAfter = sim.Lit(px + 20 * M, pz);
        check("the pillar of fire burns those at it and throws them back, and lights the night 20 m off it, where it was dark",
            pi == null && burnt >= 4 && pushed > 2.5f && darkBefore && litAfter,
            $"cast «{pi}»; burnt {burnt} of {atIt.Count}, the nearest left {pushed:0.0} m off; dark before {darkBefore}, lit after {litAfter} (darkness {sim.Clock.Darkness:0.00})");
        sim.Waves.CallOff(sim);
    }
}
