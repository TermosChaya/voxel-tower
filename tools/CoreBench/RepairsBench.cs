using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The workers' repairs and their scaffolding (--repairs), from a playtester's report: scaffolding left standing by a
/// manned wall, taken down by a worker who then fell, marked out by the player and dug from the foot up so that its top
/// hung in the air, burnt through at its foot; the old fort's west wall, its gate and its towers taken down for a wall
/// further out, and nothing of them built again after (a gate standing alone in the yard, a tower's top marked over where
/// it stood); a commander mending what a raid broke, not what lay broken before it; the head over the old fort's gate,
/// which in the first valley of a campaign came down as soon as the workers mended about it.
/// </summary>
static class RepairsBench
{
    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine();
        string only = Environment.GetEnvironmentVariable("REPAIRS_ONLY");
        if (only is null or "scaffold") Scaffolding(check);
        if (only is null or "oldwall") OldWall(check);
        if (only is null or "gate") GateHead(check);
    }

    /// <summary>Scaffolding standing in a column of cells, as the workers raise it: a pole at each corner, a ring of boards at each cell's middle.</summary>
    static Scaffold Stand(Sim sim, int i, int k, int y0, int y1)
    {
        var w = sim.World;
        var s = new Scaffold { Done = sim.Time, Used = sim.Time };
        int x0 = i << NavGrid.Shift, z0 = k << NavGrid.Shift, e = NavGrid.CS - 1;
        for (int y = y0 << NavGrid.Shift; y < (y1 + 1) << NavGrid.Shift; y++)
            foreach (var (x, z) in new[] { (x0, z0), (x0 + e, z0), (x0, z0 + e), (x0 + e, z0 + e) }.Concat((y & 7) == 3 ? Enumerable.Range(1, e - 1).SelectMany(d => new[] { (x0 + d, z0), (x0 + d, z0 + e), (x0, z0 + d), (x0 + e, z0 + d) }) : Array.Empty<(int, int)>()))
                if (w.Get(x, y, z) == Mat.Air) { w.Set(x, y, z, Mat.Scaffold); s.Voxels.Add(WaterPlanner.Pack(x, y, z)); }
        s.Columns.Add((i, k, y0, y1, false));
        s.From.Add(sim.Nav.Id(i, y0, k));
        sim.Scaffolds.Add(s);
        sim.Nav.Flush();
        return s;
    }

    /// <summary>Scaffolding in a box: how many voxels of it stand, and how many of those hang in the air (nothing under them, nor joined to what has).</summary>
    static (int stand, int hang) Hanging(VoxelWorld w, int x0, int z0, int x1, int z1, int y0, int y1)
    {
        var left = new HashSet<int>();
        var queue = new Queue<int>();
        for (int y = y0; y < y1; y++)
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                {
                    if (w.Get(x, y, z) != Mat.Scaffold) continue;
                    int p = WaterPlanner.Pack(x, y, z);
                    byte under = w.Get(x, y - 1, z);
                    if (under != Mat.Air && under != Mat.Water && under != Mat.Scaffold) queue.Enqueue(p); else left.Add(p);
                }
        int stand = left.Count + queue.Count;
        while (queue.Count > 0)
        {
            var (x, y, z) = WaterPlanner.Unpack(queue.Dequeue());
            foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                if (left.Remove(WaterPlanner.Pack(x + dx, y + dy, z + dz))) queue.Enqueue(WaterPlanner.Pack(x + dx, y + dy, z + dz));
        }
        HangingAt = left;
        return (stand, left.Count);
    }

    /// <summary>The voxels the last look found hanging.</summary>
    static HashSet<int> HangingAt = new();

    static void Scaffolding(Action<string, bool, string> check)
    {
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site) { WildTarget = 0 };
        sim.Waves.Countdown = 1e9;
        void Run(double seconds) { for (double t = 0; t < seconds; t += .05) sim.Step(.05f); }
        // a field of level ground west of the citadel, bare of trees, a wall eight metres high and three thick across it
        int fx0 = (site.KeepX - 460) & ~7, fz0 = (site.KeepZ - 140) & ~7, fg = w.SurfAt(fx0 + 160, fz0 + 140);
        for (int z = fz0; z < fz0 + 280; z++)
            for (int x = fx0; x < fx0 + 320; x++)
                for (int y = fg - 8; y < fg + 140; y++) w.Set(x, y, z, y < fg ? (y < fg - 4 ? Mat.Rock : Mat.Soil) : Mat.Air);
        int wx0 = fx0 + 120, wx1 = wx0 + 24, wz0 = fz0 + 40, wz1 = fz0 + 240;
        for (int z = wz0; z < wz1; z++) for (int x = wx0; x < wx1; x++) for (int y = fg; y < fg + 64; y++) { w.Set(x, y, z, Mat.Stone); sim.Remember(x, y, z, Mat.Stone); }
        Run(.5);
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        sim.Stock[Stockpile.Wood] = 400.0 * VoxelWorld.PerM3;
        var worker = sim.Spawn(Kinds.Worker, sim.NearestStand(wx0 - 40, fz0 + 140, 2));
        int gy = fg >> NavGrid.Shift, wi = (wx0 >> NavGrid.Shift) - 1;

        // ---- a scaffold by the wall, its work done, an archer at his post on the wall's top beside it: it comes down
        // all the same (it kept standing for good while the wall was manned)
        var archer = sim.Spawn(Kinds.Archer, sim.NearestStand(wx0 + 12, fz0 + 100, 2));
        (archer.X, archer.Y, archer.Z) = sim.Nav.Feet(archer.Cell);
        archer.Hold = true; archer.Dest = archer.Post = archer.Cell;
        var kept = Stand(sim, wi, (fz0 + 100) >> NavGrid.Shift, gy, gy + 7);
        int raised1 = kept.Voxels.Count;
        double wood1 = sim.Stock[Stockpile.Wood];
        Run(60);
        var (left1, _) = Hanging(w, wx0 - 16, fz0 + 80, wx0, fz0 + 120, fg, fg + 80);
        check("scaffolding done with by a wall comes down though an archer stands at his post on the wall beside it, its logs back in the stores",
            archer.Y >= fg + 56 && left1 == 0 && !sim.Scaffolds.Contains(kept) && sim.Stock[Stockpile.Wood] >= wood1 + raised1 - 1,
            $"the archer {archer.Y - fg:0} up; {raised1} voxels raised, {left1} left; wood {(sim.Stock[Stockpile.Wood] - wood1):0} back; {sim.Scaffolds.Count} scaffolds");
        archer.Dead = true; archer.DiedAt = -999;

        // ---- one being taken down by a worker who falls at it: another takes it on, or it comes down at once
        var second = sim.Spawn(Kinds.Worker, sim.NearestStand(wx0 - 30, fz0 + 160, 2));
        var orphan = Stand(sim, wi, (fz0 + 160) >> NavGrid.Shift, gy, gy + 9);
        orphan.Used = sim.Time - 60;
        int raised2 = orphan.Voxels.Count;
        for (int t = 0; t < 40 && !orphan.Lowering; t++) Run(.25);
        bool began = orphan.Lowering;
        var taker = sim.Units.FirstOrDefault(u => u.Raising == orphan);
        Run(1);
        if (taker != null) { taker.Dead = true; taker.DiedAt = -999; taker.Raising = null; }
        Run(40);
        var (left2, _) = Hanging(w, wx0 - 16, fz0 + 140, wx0, fz0 + 180, fg, fg + 90);
        check("scaffolding being taken down by a worker who falls at it is still taken down",
            began && taker != null && left2 == 0 && !sim.Scaffolds.Contains(orphan),
            $"began {began} by {taker?.Name ?? "nobody"}; {raised2} voxels raised, {left2} left; {sim.Scaffolds.Count} scaffolds");

        // ---- one twelve metres high in the open, the player marking its lower half to be taken out: it comes down whole
        // from the top, nothing of it ever hanging in the air (dug from the foot up, its top hung there for good)
        foreach (var u in sim.Units) if (u.Team == Team.Player && !u.Dead && u != worker && u != second) { u.Dead = true; u.DiedAt = -999; }
        int ti = (fx0 + 60) >> NavGrid.Shift, tk = (fz0 + 140) >> NavGrid.Shift;
        var tall = Stand(sim, ti, tk, gy, gy + 11);
        tall.Used = sim.Time;
        tall.Site = -1;
        // (kept from coming down by itself meanwhile: work marked in reach of it)
        tall.Site = tk * sim.Nav.NX + ti;
        var lower = new VoxelList();
        foreach (int p in tall.Voxels) { var (x, y, z) = WaterPlanner.Unpack(p); if (y < fg + 48) lower.Add(x, y, z, 0); }
        double wood3 = sim.Stock[Stockpile.Wood];
        int raised3 = tall.Voxels.Count;
        sim.DesignateRemove(lower);
        int marked3 = sim.Orders.Total, worst = 0;
        for (int t = 0; t < 120; t++)
        {
            Run(.5);
            var (_, hang) = Hanging(w, (ti << 3) - 8, (tk << 3) - 8, (ti << 3) + 16, (tk << 3) + 16, fg, fg + 110);
            worst = Math.Max(worst, hang);
            if (hang > 0 && Environment.GetEnvironmentVariable("REPAIRS_DEBUG") == "1")
                foreach (int p in HangingAt) { var (x, y, z) = WaterPlanner.Unpack(p); Console.WriteLine($"DBG {sim.Time:0.0} hangs {x - (ti << 3)},{y - fg},{z - (tk << 3)}; below {w.Get(x, y - 1, z)}, around {string.Join(",", new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1) }.Select(d => w.Get(x + d.Item1, y + d.Item2, z + d.Item3)))}; lowering {tall.Lowering}, {tall.Voxels.Count} left"); }
        }
        var (left3, _) = Hanging(w, (ti << 3) - 8, (tk << 3) - 8, (ti << 3) + 16, (tk << 3) + 16, fg, fg + 110);
        check("scaffolding the player marks to be taken out comes down whole from the top, nothing of it hanging in the air at any time, its logs back",
            marked3 == 0 && left3 == 0 && worst == 0 && !sim.Scaffolds.Contains(tall) && sim.Stock[Stockpile.Wood] >= wood3 + raised3 - 1,
            $"{lower.Count} of {raised3} voxels marked, {marked3} orders left on it; the most hanging at once {worst}; {left3} left; wood {(sim.Stock[Stockpile.Wood] - wood3):0} back");

        // ---- one whose foot is burnt through: what is over it comes down, broken timber at its foot, nothing hanging
        int bi = (fx0 + 40) >> NavGrid.Shift, bk = (fz0 + 200) >> NavGrid.Shift;
        var burnt = Stand(sim, bi, bk, gy, gy + 7);
        burnt.Site = bk * sim.Nav.NX + bi;
        Run(2);
        for (int y = fg; y < fg + 9; y++) for (int z = bk << 3; z < (bk + 1) << 3; z++) for (int x = bi << 3; x < (bi + 1) << 3; x++) if (w.Get(x, y, z) == Mat.Scaffold) w.Set(x, y, z, Mat.Air);
        Run(8);
        var (stand4, hang4) = Hanging(w, (bi << 3) - 8, (bk << 3) - 8, (bi << 3) + 16, (bk << 3) + 16, fg, fg + 80);
        int lumber = 0, upright = 0;
        for (int z = (bk << 3) - 24; z < (bk << 3) + 32; z++)
            for (int x = (bi << 3) - 24; x < (bi << 3) + 32; x++)
                for (int y = fg; y < fg + 80; y++)
                    if (w.Get(x, y, z) == Mat.Lumber) { lumber++; if (y >= fg + 16) upright++; }
        check("scaffolding burnt through at its foot comes down: what was over it falls and lies broken in a heap at its foot, nothing hangs",
            hang4 == 0 && stand4 == 0 && lumber > 150 && upright == 0, $"{stand4} still standing, {hang4} hanging; {lumber} voxels of broken timber at its foot, {upright} of them over two metres up");
    }

    static void OldWall(Action<string, bool, string> check)
    {
        var mp = new MapParams();
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site) { WildTarget = 0 };
        sim.Waves.Countdown = 1e9;
        void Run(double s) { for (double t = 0; t < s; t += .05) sim.Step(.05f); }
        var gate = StartingFort.Raise(sim);
        var (kx, kz, kb) = (site.KeepX, site.KeepZ, site.KeepBase);
        int x0 = kx - 84, z0 = kz - 60, z1 = kz + 60, top = kb + 28;
        // the old fort mended whole at once
        var fort = sim.BuildingFrom(sim.MeantNear(kx - 84, kb + 3, kz - 22));
        var lay = new VoxelList(); var clear = new VoxelList();
        sim.MendPlan(fort, lay, clear);
        sim.InstantLabour = true;
        sim.Mend(lay, clear); sim.MendGate(gate);
        sim.CompleteAll();
        sim.InstantLabour = false;
        Run(3);
        // a new west wall six metres further out, and its arms east to the old north and south walls
        int nx = x0 - 48;
        var stroke = new Stroke { Mat = Mat.Stone, Width = 6, Height = 28, Level = true };
        sim.InstantLabour = true;
        sim.DesignateBuild(Shapes.Line(w, site, nx, z0 - 3, nx, z1 + 3, stroke, top));
        sim.DesignateBuild(Shapes.Line(w, site, nx, z0, x0 + 16, z0, stroke, top));
        sim.DesignateBuild(Shapes.Line(w, site, nx, z1, x0 + 16, z1, stroke, top));
        sim.InstantLabour = false;
        Run(2);
        // the old west wall, its gate and its two towers taken down by the workers: dug from where they stand, so what is
        // out of their reach comes down by itself as they go
        var down = new VoxelList();
        for (int z = z0 - 14; z <= z1 + 14; z++)
            for (int x = x0 - 14; x <= x0 + 14; x++)
                for (int y = kb - 4; y < top + 24; y++)
                {
                    bool wall = Math.Abs(x - x0) <= 4 && z >= z0 && z <= z1;
                    bool tower = (x - x0) * (x - x0) + (z - z0) * (z - z0) <= 132 || (x - x0) * (x - x0) + (z - z1) * (z - z1) <= 132;
                    if ((wall || tower) && w.Get(x, y, z) != 0 && sim.MeantAt(x, y, z) != 0) down.Add(x, y, z, 0);
                }
        sim.DesignateRemove(down);
        for (int i = 0; i < 10; i++) sim.Spawn(Kinds.Worker, sim.Gate);
        int fellMeant = 0;
        sim.Fell += (x, y, z, m) => { if (sim.MeantAt(x, y, z) != 0) fellMeant++; };
        double t0 = sim.Time;
        while (sim.Orders.Total > 0 && sim.Time - t0 < 2400) Run(5);
        Run(10);
        var place = new HashSet<int>();
        for (int i = 0; i < down.Count; i++) place.Add(WaterPlanner.Pack(down.X(i), down.Y(i), down.Z(i)));
        int meantLeft = place.Count(p => { var (x, y, z) = WaterPlanner.Unpack(p); return sim.MeantAt(x, y, z) != 0; });
        bool gateGone = !sim.Gates.Contains(gate) && sim.FortGate == null;
        int leavesLeft = Enumerable.Range(0, Gateway.Voxels).Count(i => { var (x, y, z) = gate.Place(i, 0); return Mat.IsLeaf(w.Get(x, y, z)); });
        check("the old fort's west wall, its gate and its towers taken down by the workers: nothing of them is remembered to be mended, what came down by itself as they went neither, and the gate goes with its wall",
            sim.Orders.Total == 0 && meantLeft == 0 && fellMeant == 0 && gateGone && leavesLeft == 0,
            $"{down.Count} voxels taken down in {sim.Time - t0:0} s, {sim.Orders.Total} orders left; {meantLeft} still remembered there; {fellMeant} remembered came down; the gate let go {gateGone}, {leavesLeft} of its leaves left");

        // the player mends the new wall with a click on it: nothing of the old wall or its gate is marked; and a commander
        // whose band was beaten at the old wall's place marks nothing there
        var nw = sim.BuildingFrom(sim.MeantNear(nx, kb + 3, kz));
        var lay2 = new VoxelList(); var clear2 = new VoxelList();
        sim.MendPlan(nw, lay2, clear2);
        int oldLay = 0;
        for (int i = 0; i < lay2.Count; i++) if (place.Contains(WaterPlanner.Pack(lay2.X(i), lay2.Y(i), lay2.Z(i)))) oldLay++;
        var gates = sim.BrokenGatesIn(x0 - 200, z0 - 200, x0 + 200, z1 + 200);
        var mend = typeof(Sim).GetMethod("MendFront", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cap = sim.Spawn(Kinds.Captain, sim.Gate);
        mend.Invoke(sim, new object[] { cap, x0, kz });
        int inOld = place.Count(p => { var (x, y, z) = WaterPlanner.Unpack(p); return sim.Orders.At(x, y, z) != 0; })
            + Enumerable.Range(0, Gateway.Voxels).Count(i => { var (x, y, z) = gate.Place(i, 0); return sim.Orders.At(x, y, z) != 0; });
        check("…mending the new wall, or a commander mending after a raid there, marks nothing where the old wall and its gate stood",
            oldLay == 0 && gates.Count == 0 && inOld == 0, $"{nw.Count} voxels in the new wall's building, {oldLay} of the old wall to lay; {gates.Count} broken gates about; {inOld} marked where the old one stood");

        // a commander mends what a raid broke, not what lay broken before it: a breach knocked in the new wall now is
        // marked to be mended, a ruin of the valley's broken long before beside it is not
        int bz = kz, broke = 0;
        for (int z = bz - 8; z < bz + 8; z++) for (int x = nx - 3; x < nx + 4; x++) for (int y = kb + 4; y < top; y++) if (Mat.IsMasonry(w.Get(x, y, z)) && sim.MeantAt(x, y, z) != 0) { w.Set(x, y, z, Mat.Air); broke++; }
        var ruins = sim.MeantIn(nx - 200, kz - 200, nx + 200, kz + 200).Count(p => { var (x, y, z) = WaterPlanner.Unpack(p); return Brushes.Free(w.Get(x, y, z)) && !(Math.Abs(x - nx) <= 4 && Math.Abs(z - bz) <= 8); });
        int before = sim.Orders.Total;
        mend.Invoke(sim, new object[] { cap, nx, bz });
        int breach = 0, elsewhere = 0;
        for (int z = kz - 200; z < kz + 200; z++)
            for (int x = nx - 200; x < nx + 200; x++)
                for (int y = kb - 20; y < top + 40; y++)
                {
                    if (sim.Orders.At(x, y, z) == 0) continue;
                    if (Math.Abs(x - nx) <= 4 && Math.Abs(z - bz) <= 9) breach++; else elsewhere++;
                }
        check("a commander after a raid marks what it broke to be mended, and not what lay broken before it",
            broke > 500 && breach >= broke * 9 / 10 && elsewhere == 0,
            $"{broke} knocked out of the new wall, {breach} marked there, {elsewhere} elsewhere ({ruins} remembered and broken long before within its reach); orders {before} → {sim.Orders.Total}");
    }

    static void GateHead(Action<string, bool, string> check)
    {
        // the first valley of a campaign: the old fort's gate stands in a wall of the threshold's height and four courses
        // over it, its head; the workers mend the fort about it
        var camp = Campaign.New(11);
        var mp = camp.ParamsOf(camp.Home);
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        var sim = new Sim(w, site) { WildTarget = 0 };
        sim.Waves.Countdown = 1e9;
        var gate = StartingFort.Raise(sim);
        sim.Campaign = true;
        sim.SetSource(new MapRaids());
        sim.Bands.RemoveAll(b => b.State == BandState.Coming);
        camp.Prepare();
        camp.Enter(sim);
        camp.Settle(camp.Home, sim);
        int top = gate.Y + Gateway.Height;
        bool Head(int x, int y, int z)
        {
            if (y < top || y >= top + 12) return false;
            int u = gate.AlongX ? x : z, a = gate.AlongX ? z : x;
            return u >= gate.U0 && u < gate.U0 + Gateway.Width && Math.Abs(a - gate.P) <= 10;
        }
        int Over()
        {
            int n = 0;
            for (int y = top; y < top + 12; y++)
                for (int u = gate.U0; u < gate.U0 + Gateway.Width; u++)
                    for (int a = gate.P - 10; a <= gate.P + 10; a++)
                    {
                        int x = gate.AlongX ? u : a, z = gate.AlongX ? a : u;
                        if (sim.MeantAt(x, y, z) != 0 && !Brushes.Free(w.Get(x, y, z))) n++;
                    }
            return n;
        }
        int over0 = Over(), crumbled = 0, fell = 0;
        sim.Crumbled += (x, y, z, m) => { if (Head(x, y, z)) crumbled++; };
        sim.Fell += (x, y, z, m) => { if (Head(x, y, z)) fell++; };
        var (kx, kz, kb) = (site.KeepX, site.KeepZ, site.KeepBase);
        var fort = sim.BuildingFrom(sim.MeantNear(kx - 84, kb + 3, kz - 22));
        var lay = new VoxelList(); var clear = new VoxelList();
        sim.MendPlan(fort, lay, clear);
        sim.Mend(lay, clear); sim.MendGate(gate);
        for (int i = 0; i < 6; i++) sim.Spawn(Kinds.Worker, sim.Gate);
        sim.Stock[Stockpile.Stone] += 400 * VoxelWorld.PerM3;
        sim.Stock[Stockpile.Wood] += 100 * VoxelWorld.PerM3;
        int least = over0;
        for (int s = 0; s < 120; s++) { for (int k = 0; k < 20; k++) sim.Step(.05f); least = Math.Min(least, Over()); }
        int over1 = Over();
        check("in the first valley of a campaign the head over the old fort's gate stands while the workers mend the fort about it (it cracked and came down as soon as they did)",
            over0 > 300 && crumbled == 0 && fell == 0 && least >= over0 - 4 && over1 >= over0,
            $"the gate {gate.Y - kb} over the citadel's foot, {over0} of its head standing at first, the least {least}, after two minutes {over1}; {crumbled} cracked through, {fell} fell");
    }
}
