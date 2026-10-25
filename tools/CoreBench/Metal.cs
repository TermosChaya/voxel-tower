using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// Iron (--metal; MapGen.Ore.cs, Sim.Metal.cs): every kind of valley has outcrops of ore standing out of the ground and
/// veins hidden under it, the valleys of ore the most; ore broken out with the pick comes into the store of metal, half
/// of it, and the chronicle says what it is for; a mine (the делянка's metal) goes down where there is ore and nowhere
/// else; the miners' building is refused off an outcrop, and on one is built by the workers and brings in metal by
/// itself; iron plate holds out a ledge that stone lets fall and does not burn; a gate is hung in iron; the horde hacks
/// iron three times as long as stone; and all of it is read back from a save.
/// </summary>
static class MetalBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        // every kind of valley: its outcrops standing up out of the ground round them, rusty on top, and ore in veins
        // under the ground well away from any outcrop
        var kinds = new List<string>();
        var outcropsOf = new Dictionary<ValleyKind, int>();
        bool every = true;
        foreach (var k in Enum.GetValues<ValleyKind>())
        {
            var mpk = new MapParams { Kind = k, Hexagon = true };
            var wk = new VoxelWorld(mpk.SizeX, mpk.SizeY, mpk.SizeZ);
            var sk = MapGen.Generate(wk, mpk);
            int standing = 0;
            var low = new List<string>();
            foreach (var o in sk.Outcrops)
            {
                double around = 0;
                for (int a = 0; a < 8; a++)
                    around += wk.GroundTop(o.X + (int)(Math.Cos(a * Math.PI / 4) * (o.R + 8)), o.Z + (int)(Math.Sin(a * Math.PI / 4) * (o.R + 8))) / 8.0;
                int top = wk.GroundTop(o.X, o.Z);
                byte face = wk.Get(o.X, top - 1, o.Z);
                if (Mat.IsIronOre(face) && top >= around + 3) standing++;
                else low.Add($"{top} over {around:0} of {Mat.Def[face].Name}");
            }
            long hidden = 0;
            for (int z = 0; z < wk.SZ; z += 2)
                for (int x = 0; x < wk.SX; x += 2)
                {
                    if (!wk.OnLand(x, z) || sk.Outcrops.Any(o => Brushes.Dist(x - o.X, z - o.Z) < 130)) continue;
                    int top = wk.GroundTop(x, z);
                    for (int y = 4; y < top - 6; y++) if (Mat.IsIronOre(wk.Get(x, y, z))) hidden++;
                }
            outcropsOf[k] = sk.Outcrops.Count;
            if (standing < 1 || standing < sk.Outcrops.Count || hidden < 50) every = false;
            kinds.Add($"{k} {standing} of {sk.Outcrops.Count} standing{(low.Count > 0 ? $" ({string.Join(", ", low)})" : "")}, {hidden} hidden");
        }
        check("every kind of valley has outcrops of ore standing out of the ground, rusty on top, and veins hidden under it; the valley of ore has the most outcrops",
            every && outcropsOf.All(kv => kv.Value <= outcropsOf[ValleyKind.Ore]) && outcropsOf[ValleyKind.Ore] > outcropsOf[ValleyKind.First],
            string.Join("; ", kinds));

        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site);
        sim.Waves.Countdown = 1e9; sim.WildTarget = 0;
        StartingFort.Raise(sim);
        int kx = site.KeepX, kz = site.KeepZ;
        var crops = site.Outcrops;
        if (crops.Count < 2) { check("the bench's valley has two outcrops of ore", false, $"{crops.Count}"); return; }
        for (int i = 0; i < 8; i++) sim.Spawn(Kinds.Worker, sim.Gate);
        sim.Stock[Stockpile.Wood] += 800.0 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Stone] += 800.0 * VoxelWorld.PerM3;
        sim.Stock.Gold = 2000;
        var said = new List<string>();
        sim.Said += (text, _, _, _) => said.Add(text);
        void Run(double s) { for (double t = 0; t < s; t += .05) sim.Step(.05f); }
        double Metal() => sim.Stock[Stockpile.Metal];
        // level spots a walk from the citadel, clear of the outcrops and of one another
        var spots = new List<(int x, int z)>();
        for (int r = 120; r < 520 && spots.Count < 3; r += 8)
            for (int a = 0; a < 24 && spots.Count < 3; a++)
            {
                int x = kx + (int)(Math.Cos(a * Math.Tau / 24) * r), z = kz + (int)(Math.Sin(a * Math.Tau / 24) * r);
                if (x < 90 || z < 90 || x > w.SX - 90 || z > w.SZ - 90 || spots.Exists(s => Math.Abs(s.x - x) < 110 && Math.Abs(s.z - z) < 110)) continue;
                if (crops.Any(o => Brushes.Dist(x - o.X, z - o.Z) < o.R + 90)) continue;
                int lo = int.MaxValue, hi = 0;
                for (int dx = -44; dx <= 44; dx += 4) for (int dz = -44; dz <= 44; dz += 4) { int h = w.SurfAt(x + dx, z + dz); lo = Math.Min(lo, h); hi = Math.Max(hi, h); if (Mat.IsTrunk(w.Get(x + dx, h + 2, z + dz))) hi = 999; }
                if (hi - lo <= 2 && !site.Forbidden(x, z)) spots.Add((x, z));
            }
        if (spots.Count < 3) { check("level ground for the bench", false, $"{spots.Count} spots"); return; }

        // the knob of an outcrop broken out with the pick: half of it into the store of metal, and the chronicle says so
        var knob = new VoxelList();
        var o1 = crops[1];
        int ground1 = int.MaxValue;
        for (int a = 0; a < 8; a++) ground1 = Math.Min(ground1, w.GroundTop(o1.X + (int)(Math.Cos(a * Math.PI / 4) * (o1.R + 8)), o1.Z + (int)(Math.Sin(a * Math.PI / 4) * (o1.R + 8))));
        for (int z = o1.Z - o1.R - 2; z <= o1.Z + o1.R + 2; z++)
            for (int x = o1.X - o1.R - 2; x <= o1.X + o1.R + 2; x++)
                for (int y = ground1; y < w.GroundTop(x, z); y++)
                    if (Mat.IsIronOre(w.Get(x, y, z))) knob.Add(x, y, z, Mat.Air);
        double metal0 = Metal();
        int marked = sim.DesignateRemove(knob);
        for (int i = 0; i < 1200 && sim.Orders.Total > 0; i++) Run(.5);
        double got = Metal() - metal0;
        check("ore broken out with the pick comes into the store of metal, half of it, and the chronicle says what metal is for",
            knob.Count > 100 && marked > 0 && sim.Orders.Total == 0 && got >= knob.Count * .4 && got <= knob.Count * .6 && sim.OreStruck && said.Any(s => s.Contains("железная руда")),
            $"{knob.Count} voxels of ore, {marked} marked, {sim.Orders.Total} left at {sim.Time:0} s; metal +{got:0} ({got / Math.Max(1, knob.Count):0.00} a voxel); struck {sim.OreStruck}");

        // a mine round it: an open pit down to the ore, its sides a stair's pitch; the ground far from any ore left
        int mx0 = o1.X - 60, mz0 = o1.Z - 60, mx1 = o1.X + 60, mz1 = o1.Z + 60, depth = 24;
        var top0 = new Dictionary<(int, int), int>();
        var ore = new List<(int x, int z)>();
        for (int z = mz0; z <= mz1; z++)
            for (int x = mx0; x <= mx1; x++)
            {
                int top = w.GroundTop(x, z);
                top0[(x, z)] = top;
                for (int y = top - 1; y >= top - depth; y--) if (Mat.IsIronOre(w.Get(x, y, z))) { ore.Add((x, z)); break; }
            }
        var survey = sim.Survey(mx0, mz0, mx1, mz1, Stockpile.Metal, depth);
        var mine = sim.AddZone(mx0, mz0, mx1, mz1, Stockpile.Metal, depth);
        double metal1 = Metal();
        for (int i = 0; i < 1200 && sim.Zones.Contains(mine); i++) Run(.5);
        int dug = 0, far = 0, farDug = 0, steep = 0;
        foreach (var ((x, z), top) in top0)
        {
            bool down = w.GroundTop(x, z) < top - 1;
            if (down) dug++;
            if (!ore.Exists(c => Math.Max(Math.Abs(c.x - x), Math.Abs(c.z - z)) <= depth + 2)) { far++; if (down) farDug++; }
            // a step to the next column over more than a cell, inside the pit
            if (down && top0.ContainsKey((x + 1, z)) && Math.Abs(w.GroundTop(x + 1, z) - w.GroundTop(x, z)) > 9) steep++;
            if (down && top0.ContainsKey((x, z + 1)) && Math.Abs(w.GroundTop(x, z + 1) - w.GroundTop(x, z)) > 9) steep++;
        }
        check("a mine is an open pit down to the ore, its sides a stair's pitch, and it leaves the ground far from any ore; it brings the ore in",
            survey.voxels > 0 && Metal() > metal1 + 50 && dug > 20 && far > 0 && farDug == 0 && steep <= dug / 20,
            $"surveyed {survey.voxels:0} voxels of ore; done {!sim.Zones.Contains(mine)} at {sim.Time:0} s; metal +{Metal() - metal1:0}; {dug} columns dug, {steep} steps over a cell; " +
            $"far from ore {farDug} of {far} dug");
        sim.RemoveZones(mx0, mz0, mx1, mz1);
        sim.CancelArea(mx0 - 8, mz0 - 8, mx1 + 8, mz1 + 8);

        // the miners' building: refused off an outcrop; on one built by the workers, and then it brings in metal by itself
        var miners = Sim.TownKindOf("mine");
        sim.Knowledge = 40;
        bool learnt = sim.Learn("b_smithy") && sim.Learn("b_mine");
        string off = sim.PutUp(miners, spots[0].x, spots[0].z, false, out _);
        var o0 = crops[0];
        string on = sim.PutUp(miners, o0.X, o0.Z, false, out var b);
        double built = -1;
        for (int i = 0; i < 1600 && b != null && !b.Ready; i++) { Run(.5); if (b.Ready) built = sim.Time; }
        double metal2 = Metal();
        Run(60);
        double minute = (Metal() - metal2) / VoxelWorld.PerM3;
        check("the miners' building is refused off an outcrop; put up on one, the workers build it, and it brings in 0.3 m³ of metal a minute by itself",
            learnt && off != null && off.Contains("рудный выход") && on == null && b is { Ready: true } && Math.Abs(minute - .3) < .03 && Math.Abs(sim.TownMetal() - .3f) < .001f,
            $"learnt {learnt}; off an outcrop «{off}»; on one «{on}», ready {b?.Ready} at {built:0} s; {minute:0.000} m³ in a minute");

        // iron plate: a ledge of it held out of a pillar where one of stone comes down; it does not burn
        int px = spots[0].x, pz = spots[0].z, pg = w.SurfAt(px, pz);
        for (int z = pz - 30; z <= pz + 30; z++)
            for (int x = px - 40; x <= px + 40; x++)
                for (int y = pg; y < pg + 60; y++) if (w.Get(x, y, z) != Mat.Air) w.Set(x, y, z, Mat.Air);
        for (int z = pz - 2; z < pz + 2; z++) for (int x = px - 2; x < px + 2; x++) for (int y = w.GroundTop(x, z); y < pg + 28; y++) w.Set(x, y, z, Mat.Stone);
        for (int z = pz - 1; z < pz + 1; z++)
            for (int k = 0; k < 16; k++)
                for (int y = pg + 22; y < pg + 24; y++) { w.Set(px - 3 - k, y, z, Mat.Stone); w.Set(px + 2 + k, y, z, Mat.Plate); }
        Run(8);
        int stoneLedge = 0, ironLedge = 0;
        for (int z = pz - 1; z < pz + 1; z++)
            for (int k = 0; k < 16; k++)
                for (int y = pg + 22; y < pg + 24; y++) { if (Mat.IsMasonry(w.Get(px - 3 - k, y, z))) stoneLedge++; if (w.Get(px + 2 + k, y, z) == Mat.Plate) ironLedge++; }
        bool lit = sim.Ignite(px + 10, pg + 23, pz);
        check("a ledge of iron plate two metres long holds out of a pillar where one of stone comes down, and iron does not catch fire",
            ironLedge == 64 && stoneLedge < 48 && !lit && !Mat.Burns(Mat.Plate) && !Mat.Burns(Mat.IronGate) && Mat.Burns(Mat.Gate),
            $"of 64 voxels each the stone ledge keeps {stoneLedge}, the iron one {ironLedge}; lit {lit}");

        // a gate hung in iron in a wall of iron plate: its leaves of iron plate, barred iron, and turned open iron
        for (int z = pz + 16; z < pz + 24; z++)
            for (int x = px - 36; x <= px + 36; x++)
                for (int y = w.GroundTop(x, z); y < pg + 32; y++) w.Set(x, y, z, Mat.Plate);
        Run(.5);
        int gy = pg + 6, gz = pz + 16;
        var gp = GatePlan.At(w, site, new VoxelHit { X = px + 16, Y = gy, Z = gz, NZ = -1, M = w.Get(px + 16, gy, gz) }, iron: true);
        var ironGate = gp == null ? null : sim.AddGate(gp, built: true);
        Run(1.5);
        int Leaves(byte m)
        {
            int n = 0;
            if (ironGate == null) return 0;
            var (gx, _, gzz) = ironGate.Middle;
            for (int z = (int)gzz - 20; z <= (int)gzz + 20; z++)
                for (int x = (int)gx - 20; x <= (int)gx + 20; x++)
                    for (int y = ironGate.Y; y < ironGate.Y + Gateway.Height; y++) if (w.Get(x, y, z) == m) n++;
            return n;
        }
        int hung = Leaves(Mat.IronGate);
        sim.SetGateMode(ironGate, GateMode.Shut);
        int barred = Leaves(Mat.IronGateShut);
        sim.SetGateMode(ironGate, GateMode.Open);
        Run(2);
        int open = Leaves(Mat.IronGateOpen);
        sim.SetGateMode(ironGate, GateMode.Auto);
        Run(2);
        check("a gate hung in iron has leaves of iron plate: barred they are iron on the bar, turned open iron still",
            ironGate is { Iron: true, Ready: true } && hung > Gateway.Voxels / 2 && barred == hung && open > Gateway.Voxels / 2 && Leaves(Mat.Gate) == 0,
            $"gate {(ironGate == null ? "not hung" : $"ready {ironGate.Ready}")}; leaves {hung}, barred {barred}, open {open}");

        // a worker gone into lays iron with the fourth key, out of the store of metal
        var hand = sim.Spawn(Kinds.Worker, sim.NearestStand(px - 25, pz - 20, 2));
        sim.Possess(hand);
        sim.LayMat = Mat.Plate;
        sim.LookYaw = 0; sim.LookPitch = -.6f;
        double iron0 = Metal();
        int plates0 = 0;
        for (int z = pz - 30; z <= pz + 10; z++) for (int x = px - 40; x <= px - 6; x++) for (int y = pg - 4; y < pg + 30; y++) if (w.Get(x, y, z) == Mat.Plate) plates0++;
        sim.WishLay = true;
        Run(3);
        sim.WishLay = false;
        int plates1 = 0;
        for (int z = pz - 30; z <= pz + 10; z++) for (int x = px - 40; x <= px - 6; x++) for (int y = pg - 4; y < pg + 30; y++) if (w.Get(x, y, z) == Mat.Plate) plates1++;
        sim.Unpossess("bench");
        check("a worker gone into lays iron plate where it looks, out of the store of metal",
            plates1 - plates0 > 20 && iron0 - Metal() >= (plates1 - plates0) * .9,
            $"{plates1 - plates0} plates laid, metal {iron0:0} → {Metal():0}");

        // the horde hacks iron three times as long as stone: an orc shut in a yard walled in stone hacks its way out,
        // one in a yard walled in iron plate takes far the longer
        void Yard((int x, int z) s, byte m)
        {
            sim.InstantLabour = true;
            int ytop = Shapes.LevelTop(w, s.x, s.z, 32);
            sim.DesignateBuild(Shapes.Box(w, site, s.x - 32, s.z - 32, s.x + 32, s.z + 32, new Stroke { Mat = m, Width = 8, Height = 32, Level = true, Hollow = true }, ytop));
            sim.InstantLabour = false;
        }
        Yard(spots[1], Mat.Stone);
        Yard(spots[2], Mat.Plate);
        Run(1);
        sim.HordeStock[Stockpile.Wood] = sim.HordeStock[Stockpile.Earth] = 0;
        Unit Brute((int x, int z) s) { var u = sim.Spawn(Kinds.Orc, sim.NearestStand(s.x, s.z, 2)); u.Torch = false; return u; }
        var inStone = Brute(spots[1]);
        var inIron = Brute(spots[2]);
        sim.ComputeFields();
        bool Out(Unit u, (int x, int z) s) => Math.Abs(u.X - s.x) > 36 || Math.Abs(u.Z - s.z) > 36;
        double stoneOut = -1, ironOut = -1, t0 = sim.Time;
        for (int t = 0; t < 400 * 20 && (stoneOut < 0 || ironOut < 0); t++)
        {
            sim.Step(.05f);
            if (stoneOut < 0 && Out(inStone, spots[1])) stoneOut = sim.Time - t0;
            if (ironOut < 0 && Out(inIron, spots[2])) ironOut = sim.Time - t0;
        }
        check("the horde hacks iron plate three times as long as stone: an orc walled in by iron takes far longer to get out than one walled in by stone",
            stoneOut > 0 && (ironOut < 0 || ironOut > stoneOut * 2.2),
            $"out of the stone yard in {stoneOut:0} s, of the iron one {(ironOut < 0 ? "not in 400 s" : $"in {ironOut:0} s")}");
        inStone.Dead = inIron.Dead = true; inStone.DiedAt = inIron.DiedAt = -999;

        // all of it read back
        var buf = new System.IO.MemoryStream();
        sim.Save(buf, mp.Seed, "metal", "");
        buf.Position = 0;
        var w2 = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        Sim sim2;
        using (var save = new SaveReader(buf))
        {
            var site2 = MapGen.Generate(w2, new MapParams { Seed = save.Seed });
            w2.Compact();
            save.Valley(w2, site2);
            sim2 = new Sim(w2, site2);
            sim2.Load(save);
        }
        var b2 = sim2.BuildingOf("mine");
        var g2 = ironGate == null ? null : sim2.Gates.Find(g => g.Id == ironGate.Id);
        check("metal is read back from a save: the store, what was mined, the miners' building at work, the iron gate",
            Math.Abs(sim2.Stock[Stockpile.Metal] - sim.Stock[Stockpile.Metal]) < 1 && sim2.OreStruck && Math.Abs(sim2.MetalMined - sim.MetalMined) < 1
            && b2 is { Ready: true } && Math.Abs(sim2.TownMetal() - .3f) < .001f && g2 is { Iron: true },
            $"metal {sim.Stock[Stockpile.Metal]:0} → {sim2.Stock[Stockpile.Metal]:0}; struck {sim2.OreStruck}; mined {sim.MetalMined:0} → {sim2.MetalMined:0}; " +
            $"the building {(b2 == null ? "gone" : $"ready {b2.Ready}")}; the gate iron {g2?.Iron}");
    }
}
