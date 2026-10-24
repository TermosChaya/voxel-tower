using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// Mines under the walls and what meets them (--mines): a tower built into a long wall, dug out under one side, cracks
/// along its joint and goes over by itself, tearing out of the wall, whose ends stand.
/// </summary>
static class MinesBench
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
        int Count(Func<byte, bool> what, int x0, int z0, int x1, int z1, int y0, int y1)
        {
            int n = 0;
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) if (what(w.Get(x, y, z))) n++;
            return n;
        }
        // a level patch of ground: the highest of it, with everything over it cleared
        int Flatten(int x0, int z0, int x1, int z1)
        {
            int g = 0;
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) g = Math.Max(g, w.SurfAt(x, z));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    for (int y = g; y < g + 100; y++) w.Set(x, y, z, Mat.Air);
                    for (int y = g - 1; y >= 0 && !Mat.IsAnchor[w.Get(x, y, z)]; y--) w.Set(x, y, z, Mat.Soil);
                    for (int y = g - 12; y < g; y++) w.Set(x, y, z, Mat.Soil);
                }
            return g;
        }
        static bool Stone(byte m) => Mat.IsMasonry(m);

        // a tower two and a half metres square and eight high in the middle of a wall fifteen metres long each way, three
        // quarters of a metre thick and four high; the ground dug out under the tower's south half, off the wall: the
        // tower cracks along its joints with the wall, tears out of it and goes over south; the wall's two ends stand
        {
            int tx = kx - 260, tz = kz - 60, g = Flatten(tx - 140, tz - 30, tx + 160, tz + 70);
            for (int z = tz; z < tz + 20; z++)
                for (int x = tx; x < tx + 20; x++)
                    for (int y = g; y < g + 64; y++)
                        if (x < tx + 3 || x >= tx + 17 || z < tz + 3 || z >= tz + 17 || y >= g + 60) w.Set(x, y, z, Mat.Stone);
            for (int z = tz + 7; z < tz + 13; z++)
                for (int y = g; y < g + 32; y++)
                {
                    for (int x = tx - 120; x < tx; x++) w.Set(x, y, z, Mat.Stone);
                    for (int x = tx + 20; x < tx + 140; x++) w.Set(x, y, z, Mat.Stone);
                }
            Run(2);
            int west0 = Count(Stone, tx - 120, tz + 7, tx - 12, tz + 12, g, g + 32), east0 = Count(Stone, tx + 32, tz + 7, tx + 139, tz + 12, g, g + 32);
            int over = 0, cracked = 0, jointCracks = 0;
            Flier going = null;
            sim.GoingOver = f => { over++; going ??= f; };
            string said = "";
            sim.Said += (text, _, _, _) => said += text + " ";
            for (int z = tz + 13; z < tz + 24; z++) for (int x = tx - 1; x < tx + 21; x++) for (int y = g - 6; y < g; y++) w.Set(x, y, z, Mat.Air);
            double t0 = sim.Time, wentAt = -1;
            for (int t = 0; t < 600 && wentAt < 0; t++)
            {
                Run(.1);
                int c = 0, j = 0;
                foreach (int p in sim.Cracks.Keys)
                {
                    var (x, _, z) = WaterPlanner.Unpack(p);
                    if (x >= tx - 2 && x <= tx + 21 && z >= tz - 2 && z <= tz + 21) c++;
                    if ((x >= tx - 3 && x < tx || x >= tx + 20 && x < tx + 23) && z >= tz + 7 && z < tz + 13) j++;
                }
                cracked = Math.Max(cracked, c); jointCracks = Math.Max(jointCracks, j);
                if (over > 0) wentAt = sim.Time - t0;
            }
            int size = going?.Voxels.Count ?? 0;
            Run(10);
            int standing = Count(Stone, tx, tz, tx + 19, tz + 19, g + 34, g + 70), south = Count(m => Mat.IsMasonry(m) || m == Mat.Rubble, tx - 20, tz + 21, tx + 40, tz + 110, g - 6, g + 30);
            int west1 = Count(Stone, tx - 120, tz + 7, tx - 12, tz + 12, g, g + 32), east1 = Count(Stone, tx + 32, tz + 7, tx + 139, tz + 12, g, g + 32);
            check("a tower in the middle of a long wall, dug out under its south half, cracks along its joints and tears out of the wall, going over south by itself; the wall's ends stand",
                wentAt > 0 && size > 8000 && size < 30000 && standing < 300 && south > 6000 && west1 > west0 * 9 / 10 && east1 > east0 * 9 / 10 && (said.Contains("отходит от стены") || said.Contains("отрывается")),
                $"went over after {wentAt:0.0} s, {size} voxels of it; {cracked} cracks in it at most, {jointCracks} at its joints; {standing} of it standing over four metres, {south} of stone and rubble south of it; the wall west {west0} → {west1}, east {east0} → {east1}; «{said.Trim()}»");
            sim.GoingOver = null;
        }

        // a ring of stone round the citadel, a worker of ours standing inside it to the west, and a miner of the horde coming
        // at it from twenty-five metres out: it goes under the wall, shoring as it goes; the worker hears it and is told;
        // under the wall it digs out a chamber on posts, fires them, and the wall over it comes down
        int ring = site.KeepR + 70;
        for (int z = kz - ring - 8; z <= kz + ring + 8; z++)
            for (int x = kx - ring - 8; x <= kx + ring + 8; x++)
            {
                float d = MathF.Sqrt((x - kx) * (x - kx) + (z - kz) * (z - kz));
                if (d < ring || d >= ring + 8) continue;
                int g = w.SurfAt(x, z);
                for (int y = g; y < g + 32; y++) w.Set(x, y, z, Mat.Stone);
            }
        Run(.5);
        sim.ComputeFields();
        var listener = sim.Spawn(Kinds.Worker, sim.NearestStand(kx - ring + 30, kz, 4));
        listener.Hold = true; listener.Dest = listener.Post = listener.Cell;
        var miner = sim.Spawn(Kinds.Miner, sim.NearestStand(kx - ring - 200, kz, 2));
        miner.Hp = miner.MaxHp = 1e6f;
        string heard = "";
        sim.Said += (text, _, _, _) => { if (text.Contains("кирки") || text.Contains("крепь") || text.Contains("крепи") || text.Contains("подкоп")) heard += text + " "; };
        int marks = 0; float markOff = 0;
        sim.PicksHeard = (t, x, y, z) => { marks++; var (fx, _, fz) = sim.Nav.Feet(t.Cells[Math.Min(t.Dug, t.Cells.Count - 1)]); markOff = MathF.Sqrt((x - fx) * (x - fx) + (z - fz) * (z - fz)); };
        Tunnel mine = null;
        double begunAt = -1, heardAt = -1, firedAt = -1, under = 0;
        int wallStretch() => Count(Stone, kx - ring - 10, kz - 30, kx - ring + 18, kz + 30, 0, 191);
        int wall0 = wallStretch(), wallAtFire = 0;
        for (int i = 0; i < 6000 && firedAt < 0; i++)
        {
            Run(.1);
            mine ??= sim.Tunnels.Find(t => t.Team == Team.Horde);
            if (mine != null && begunAt < 0) begunAt = sim.Time;
            if (mine != null && mine.Heard && heardAt < 0) heardAt = sim.Time;
            if (miner.Y < w.SurfAt((int)miner.X, (int)miner.Z) - 16) under = Math.Max(under, w.SurfAt((int)miner.X, (int)miner.Z) - miner.Y);
            if (mine is { State: TunnelState.Fired }) { firedAt = sim.Time; wallAtFire = wallStretch(); }
        }
        int heap = mine == null ? 0 : mine.Spoil / 12;
        check("a miner coming at a ring of stone of ours goes under it: a mine begun twenty-odd metres out, dug down and on under the ground, shored every metre, the earth heaped by its mouth",
            mine != null && mine.Dug == mine.Cells.Count && mine.Shored.TrueForAll(s => s) && under > 24 && heap > 200,
            mine == null ? "no mine begun" : $"begun at {begunAt:0} s, {mine.Cells.Count} steps, {mine.Dug} dug, {mine.Shored.FindAll(s => s).Count} shored; the miner {under / VoxelWorld.PerMetre:0.0} m under the ground at most; {heap} voxels heaped");
        check("ours near the wall hear the picks under the ground: told in the chronicle, the mark over the face trembling with each stroke, near the face",
            heardAt > 0 && heard.Contains("стучат кирки") && marks > 10 && markOff < 6 * VoxelWorld.PerMetre,
            $"heard at {heardAt:0} s ({marks} strokes heard, the last mark {markOff / VoxelWorld.PerMetre:0.0} m off the face); «{heard.Trim()}»");
        double fellAt = -1;
        for (int i = 0; i < 1200 && fellAt < 0; i++) { Run(.1); if (wallStretch() < wallAtFire - 1500) fellAt = sim.Time; }
        check("under the wall it digs a chamber on posts, the wall standing on them; it fires them and goes, and when they have burnt through the wall comes down",
            mine != null && mine.PostsSet >= 6 && firedAt > 0 && wallAtFire > wall0 - 600 && fellAt > firedAt && heard.Contains("подожгла крепь"),
            $"{mine?.PostsSet} posts of {mine?.Posts.Count}; fired at {firedAt:0} s; the wall there {wall0} → {wallAtFire} at the firing → {wallStretch()} ({(fellAt > 0 ? $"came down {fellAt - firedAt:0} s after" : "still standing")})");

        Unit Stand(UnitType t, int cell)
        {
            var u = sim.Spawn(t, cell);
            (u.X, u.Y, u.Z) = sim.Nav.Feet(cell);
            if (t.Team == Team.Horde) { u.Guard = true; u.Home = cell; } else { u.Hold = true; u.Dest = u.Post = cell; }
            return u;
        }

        // a countermine of ours dug with no logs in the stores goes unshored; told so; in a minute and a half or two it falls
        // in, the ground over it sunk into a hollow, and a goblin standing in it is struck and buried
        {
            int ax = kx + 40, az = kz + 300, g = Flatten(ax - 20, az - 30, ax + 140, az + 30);
            sim.Stock[Stockpile.Wood] = 0;
            heard = "";
            var plan = sim.PlanCountermine(ax, az, ax + 120, az, 24, out var ours);
            sim.InstantLabour = true;
            sim.DesignateRemove(plan);
            sim.InstantLabour = false;
            sim.AddCountermine(ours);
            Run(1);
            int mid = ours.Cells[ours.Cells.Count / 2];
            var (mx, _, mz) = sim.Nav.Feet(mid);
            var gob = Stand(Kinds.Goblin, mid);
            gob.Hp = gob.MaxHp = 400;
            int top0 = w.SurfAt((int)mx, (int)mz);
            bool unshored = ours.Dug == ours.Cells.Count && ours.Shored.TrueForAll(x => !x);
            double caved = -1, dugAt = sim.Time; bool pinned = false;
            for (int i = 0; i < 1800 && caved < 0; i++) { Run(.1); pinned |= gob.PinKg > 0; if (ours.Fallen.TrueForAll(x => x)) caved = sim.Time; }
            Run(2);
            pinned |= gob.PinKg > 0;
            check("a countermine dug with no logs to shore it is told of, falls in after a minute and a half or two, the ground over it sunk, the goblin in it struck and buried",
                unshored && heard.Contains("без крепи") && caved > dugAt + 80 && w.SurfAt((int)mx, (int)mz) < top0 - 1 && (pinned || gob.Dead || gob.Hp < gob.MaxHp),
                $"dug {ours.Dug} of {ours.Cells.Count}, shored {ours.Shored.FindAll(x => x).Count}; all fallen in {(caved > 0 ? $"{caved - dugAt:0} s after it was dug" : "not yet")}; the ground over its middle {top0} → {w.SurfAt((int)mx, (int)mz)}; the goblin {(gob.Dead ? "dead, " + gob.KilledBy : pinned ? "pinned" : $"{gob.Hp:0} of {gob.MaxHp:0}")}; «{heard.Trim()}»");
        }

        // the horde's mine, and our countermine drawn across it and dug: it breaks in and is told of; a worker of ours at the
        // breach knocks out the props of theirs beside it, and those steps fall in, burying the miner in them
        {
            int hx = kx - 120, hz = kz + 420, g = Flatten(hx - 20, hz - 90, hx + 180, hz + 40);
            sim.Stock[Stockpile.Wood] = 50;
            var theirs = sim.DigMineNow(hx, hz, hx + 160, hz, 24, Team.Horde);
            heard = "";
            var plan = sim.PlanCountermine(hx + 80, hz - 70, hx + 80, hz + 4, 24, out var ours);
            sim.InstantLabour = true;
            sim.DesignateRemove(plan);
            sim.InstantLabour = false;
            sim.AddCountermine(ours);
            int cross = theirs.Cells.FindIndex(c => sim.Nav.I(c) == (hx + 80) >> 3);
            var foe = Stand(Kinds.Miner, theirs.Cells[Math.Min(theirs.Cells.Count - 1, cross + 1)]);
            foe.Hp = foe.MaxHp = 300;
            // a swordsman sent down to the breach (a worker there would run from the miner)
            var digger = Stand(Kinds.Swordsman, ours.Cells[^2]);
            for (int i = 0; i < 20 && !theirs.Breached; i++) Run(.1);
            bool breached = theirs.Breached;
            int shored0 = theirs.Shored.FindAll(x => x).Count;
            bool hit = false;
            for (int i = 0; i < 300; i++) { Run(.1); hit |= foe.Dead || foe.PinKg > 0 || foe.Hp < foe.MaxHp; }
            int pulled = shored0 - theirs.Shored.FindAll(x => x).Count, fallen = theirs.Fallen.FindAll(x => x).Count;
            check("our countermine drawn across the horde's breaks into it and is told of; ours at the breach knock out its props there, those steps fall in, and the miner in them is struck",
                breached && heard.Contains("прорвался") && pulled >= 2 && fallen >= 2 && hit,
                $"breached {breached} (at step {cross} of {theirs.Cells.Count}); {pulled} frames knocked out, {fallen} steps of theirs fallen in ({string.Join(",", Enumerable.Range(0, theirs.Cells.Count).Where(i => theirs.Fallen[i]))}); the miner {(foe.Dead ? "dead, " + foe.KilledBy : foe.PinKg > 0 ? $"pinned under {foe.PinKg:0} kg" : $"{foe.Hp:0} of {foe.MaxHp:0}")}; «{heard.Trim()}»");

            // kept in a save: the mines, how far each is dug, its props and what has fallen in, and who digs it
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
            var t2 = sim2.Tunnels.Find(t => t.Id == theirs.Id);
            var m2 = mine == null ? null : sim2.Tunnels.Find(t => t.Id == mine.Id);
            check("the mines are kept in a save: their steps, props, what has fallen in and whether they were heard or broken into",
                sim2.Tunnels.Count == sim.Tunnels.Count && t2 != null && t2.Breached && t2.Fallen.FindAll(x => x).Count == fallen && t2.Shored.FindAll(x => x).Count == theirs.Shored.FindAll(x => x).Count
                    && (mine == null || m2 != null && m2.Heard && m2.State == mine.State && m2.PostsSet == mine.PostsSet),
                $"{sim2.Tunnels.Count} of {sim.Tunnels.Count} mines; theirs broken into {t2?.Breached}, fallen {t2?.Fallen.FindAll(x => x).Count} of {fallen}; the first heard {m2?.Heard}, {m2?.State}");
        }
    }
}
