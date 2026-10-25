using System;
using System.Collections.Generic;
using System.Linq;
using VoxelTower.Core;

/// <summary>
/// The bench's --smartwork: the workers at dangerous work, and how much it costs them. A grove of nine trees close
/// together felled by six workers while two more dig a ditch beside it; a ditch dug along the foot of a stone wall;
/// the ground taken out from under a block of masonry, which comes down as it must; a shaft four metres deep; our
/// countermine with no logs to shore it. Each is counted for those hurt, bones broken, pinned and killed, and for a way
/// out of the shaft; the trees for which way they fell, and whether any came down on one of ours.
/// </summary>
static class SmartworkBench
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
        // the household at the gate kept out of it: these tests bring their own
        foreach (var u in sim.Units) if (u.Team == Team.Player) { u.Dead = true; u.DiedAt = -999; }
        Run(.1);
        var (gx, _, gz) = sim.Nav.Feet(sim.Gate);

        // what the work costs: every hurt, bone broken and pin of the crew at it, by cause
        var crew = new HashSet<Unit>();
        var hurts = new Dictionary<string, int>();
        int bones = 0;
        var pinned = new HashSet<Unit>();
        sim.Hurting += (u, dmg, cause) => { if (crew.Contains(u) && dmg > 0) hurts[cause] = hurts.GetValueOrDefault(cause) + 1; };
        sim.BoneBroke += (u, _) => { if (crew.Contains(u)) bones++; };
        void Watch(double seconds, Func<bool> done = null)
        {
            for (double t = 0; t < seconds; t += .25)
            {
                Run(.25);
                foreach (var u in crew) if (u.PinKg > 0) pinned.Add(u);
                if (done != null && done()) return;
            }
        }
        string Cost() => $"hurt {hurts.Values.Sum()} times ({(hurts.Count == 0 ? "—" : string.Join(", ", hurts.Select(h => $"{h.Key} ×{h.Value}")))}), bones {bones}, pinned {pinned.Count}, dead {crew.Count(u => u.Dead)}";
        void Reset() { foreach (var u in crew) if (!u.Dead) { u.Dead = true; u.DiedAt = -999; } crew.Clear(); hurts.Clear(); bones = 0; pinned.Clear(); Run(.2); }
        Unit At(UnitType t, float x, float z)
        {
            int c = sim.NearestStand(Math.Clamp((int)x, 8, w.SX - 9), Math.Clamp((int)z, 8, w.SZ - 9), t.Move.Height);
            if (c < 0) throw new InvalidOperationException($"nowhere to stand by {x:0},{z:0}");
            var u = sim.Spawn(t, c);
            crew.Add(u);
            return u;
        }
        // a field cleared of what grows on it, whole trees and all (what was cut of those standing over its edge comes down
        // before anyone is set to work there)
        void Clear(int x0, int z0, int x1, int z1)
        {
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                    for (int y = w.SurfAt(x, z), top = w.ColTop(x, z); y < top; y++)
                    {
                        byte m = w.Get(x, y, z);
                        if (!Mat.IsVeg[m] && !Mat.IsTrunk(m)) continue;
                        if (Mat.IsTrunk(m)) { var tree = Shapes.Tree(w, x, y, z); for (int i = 0; i < tree.Count; i++) w.Set(tree.X(i), tree.Y(i), tree.Z(i), Mat.Air); }
                        w.Set(x, y, z, Mat.Air);
                    }
            Run(20);
        }

        // ---- a grove felled: nine trees five metres apart, six workers at them and two digging a ditch beside it
        int fx0 = (int)gx - 330, fz0 = (int)gz - 60;
        Clear(fx0 - 80, fz0 - 80, fx0 + 160, fz0 + 160);
        Run(.5);
        var trees = new List<(int x, int z, VoxelList plan)>();
        for (int j = 0; j < 3; j++)
            for (int i = 0; i < 3; i++)
            {
                int x = fx0 + i * 40, z = fz0 + j * 40, kind = (i + j * 3) % 3, h = 84 + (i * 7 + j * 11) % 24;
                bool grown = kind switch { 0 => Flora.Pine(w, x, z, h), 1 => Flora.Oak(w, x, z, h - 10), _ => Flora.Birch(w, x, z, h) };
                if (!grown) continue;
                int g = w.SurfAt(x, z);
                for (int y = g; y < g + 6; y++)
                    if (Mat.IsTrunk(w.Get(x, y, z)) || w.Get(x, y, z) == Mat.Birch) { trees.Add((x, z, Shapes.Tree(w, x, y, z))); break; }
            }
        // (what of the new trees hangs by a corner only comes down before anyone is near)
        Run(5);
        foreach (var (_, _, plan) in trees) sim.DesignateRemove(plan);
        for (int i = 0; i < 6; i++) At(Kinds.Worker, fx0 + 40 + (i % 3 - 1) * 16, fz0 + 40 + (i / 3) * 16 - 8);
        // a ditch beside the grove, dug by the rest of them: they walk and work where the trees go down
        var ditch = new VoxelList();
        for (int z = fz0 - 20; z < fz0 + 100; z++)
            for (int x = fx0 + 100; x < fx0 + 108; x++)
                for (int y = w.SurfAt(x, z) - 1, d = 0; d < 8; y--, d++)
                    if (Labour.Diggable(w.Get(x, y, z))) ditch.Add(x, y, z, Mat.Air);
        sim.DesignateRemove(ditch);
        At(Kinds.Worker, fx0 + 104, fz0 + 20); At(Kinds.Worker, fx0 + 104, fz0 + 60);
        // which way each tree went, and whether one of ours stood in its way
        int fells = 0, felled = 0, ontoOurs = 0;
        var falls = new List<string>();
        sim.Toppled += t =>
        {
            fells++;
            // a tree felled as it should be (its lane cleared first) must go over onto nobody; one cut loose by another's
            // fall only must hurt nobody
            if (!t.Felled) return;
            felled++;
            // the lane it comes down along: as long as it is tall, a metre and a half to either side
            int top = 0;
            foreach (var (_, y, _, _) in t.Voxels) top = Math.Max(top, y - (int)t.PY);
            foreach (var u in crew)
            {
                if (u.Dead) continue;
                float ax = u.X - t.PX, az = u.Z - t.PZ, along = ax * t.DX + az * t.DZ, across = MathF.Abs(ax * t.DZ - az * t.DX);
                if (along > 0 && along < top + 4 && across < 12) { ontoOurs++; falls.Add($"#{u.Id} {along / 8:0.0} m along, {across / 8:0.0} aside"); }
            }
        };
        Watch(900, () => trees.All(t => !Mat.IsTrunk(w.Get(t.x, w.SurfAt(t.x, t.z) + 3, t.z)) && w.Get(t.x, w.SurfAt(t.x, t.z) + 3, t.z) != Mat.Birch));
        int standing = trees.Count(t => { int g = w.SurfAt(t.x, t.z); return Mat.IsTrunk(w.Get(t.x, g + 3, t.z)) || w.Get(t.x, g + 3, t.z) == Mat.Birch; });
        string felledCost = Cost();
        bool safeFelling = hurts.Count == 0 && bones == 0 && pinned.Count == 0 && crew.All(u => !u.Dead);
        check("six workers fell a grove of nine trees five metres apart while two dig a ditch beside it: none of them is hurt, and no tree goes over onto one of ours",
            trees.Count >= 8 && standing == 0 && safeFelling && ontoOurs == 0 && felled >= trees.Count - 2,
            $"{trees.Count} trees, {standing} still standing after {sim.Time:0} s; {fells} went over, {felled} of them felled with a warning, {ontoOurs} onto one of ours{(falls.Count > 0 ? " (" + string.Join("; ", falls.Take(4)) + ")" : "")}; {felledCost}");
        Reset();

        // ---- a ditch two metres deep along the foot of a stone wall
        int wx0 = (int)gx - 330, wz0 = (int)gz + 160;
        Clear(wx0 - 40, wz0 - 40, wx0 + 160, wz0 + 60);
        int wallBase = int.MaxValue;
        for (int x = wx0; x < wx0 + 120; x++) wallBase = Math.Min(wallBase, w.SurfAt(x, wz0));
        var wall = new List<(int x, int y, int z)>();
        for (int x = wx0; x < wx0 + 120; x++)
            for (int z = wz0; z < wz0 + 8; z++)
                for (int y = w.SurfAt(x, z); y < wallBase + 24; y++) { w.Set(x, y, z, Mat.Stone); wall.Add((x, y, z)); }
        var moat = new VoxelList();
        for (int x = wx0; x < wx0 + 120; x++)
            for (int z = wz0 + 8; z < wz0 + 24; z++)
                for (int y = w.SurfAt(x, z) - 1, d = 0; d < 16; y--, d++)
                    if (Labour.Diggable(w.Get(x, y, z))) moat.Add(x, y, z, Mat.Air);
        Run(.5);
        sim.DesignateRemove(moat);
        for (int i = 0; i < 4; i++) At(Kinds.Worker, wx0 + 20 + i * 24, wz0 + 40);
        int Left(VoxelList L) { int n = 0; for (int i = 0; i < L.Count; i++) if (sim.Orders.At(L.X(i), L.Y(i), L.Z(i)) != 0) n++; return n; }
        Watch(600, () => Left(moat) == 0);
        int wallLeft = wall.Count(p => w.Get(p.x, p.y, p.z) == Mat.Stone);
        string moatCost = Cost();
        check("four workers dig a ditch two metres deep along the foot of a stone wall: none hurt, the wall stands, and they come up out of it",
            Left(moat) == 0 && hurts.Count == 0 && bones == 0 && pinned.Count == 0 && wallLeft == wall.Count && crew.All(u => u.Y > wallBase - 4),
            $"{moat.Count - Left(moat)} of {moat.Count} dug; the wall {wallLeft} of {wall.Count}; {moatCost}; the lowest of them {crew.Min(u => u.Y) - wallBase:0} voxels from the ground");
        Reset();

        // ---- the ground taken out from under a block of masonry, as ordered: it comes down, on none of them
        int bx0 = (int)gx - 120, bz0 = (int)gz + 160;
        Clear(bx0 - 40, bz0 - 40, bx0 + 80, bz0 + 80);
        int bBase = int.MaxValue;
        for (int z = bz0; z < bz0 + 24; z++) for (int x = bx0; x < bx0 + 24; x++) bBase = Math.Min(bBase, w.SurfAt(x, z));
        for (int z = bz0; z < bz0 + 24; z++)
            for (int x = bx0; x < bx0 + 24; x++)
            {
                for (int y = bBase; y < w.SurfAt(x, z); y++) w.Set(x, y, z, Mat.Air);
                for (int y = bBase; y < bBase + 24; y++) w.Set(x, y, z, Mat.Stone);
            }
        var under = new VoxelList();
        for (int z = bz0 - 4; z < bz0 + 28; z++)
            for (int x = bx0 - 4; x < bx0 + 28; x++)
                for (int y = bBase - 1; y >= bBase - 8; y--)
                    if (Labour.Diggable(w.Get(x, y, z))) under.Add(x, y, z, Mat.Air);
        Run(.5);
        sim.DesignateRemove(under);
        for (int i = 0; i < 4; i++) At(Kinds.Worker, bx0 + 12 + (i % 2 * 2 - 1) * 30, bz0 + 12 + (i / 2 * 2 - 1) * 30);
        int shouts = 0;
        sim.Shouted += (_, _) => shouts++;
        Watch(600, () => Left(under) == 0);
        Run(8);
        int blockStands = 0;
        for (int z = bz0; z < bz0 + 24; z++) for (int x = bx0; x < bx0 + 24; x++) for (int y = bBase; y < bBase + 24; y++) if (w.Get(x, y, z) == Mat.Stone) blockStands++;
        string underCost = Cost();
        check("the ground taken out from under a block of masonry, as the player marked it: the block comes down, and none of the four digging is under it",
            Left(under) < under.Count / 4 && blockStands < 24 * 24 * 24 / 2 && shouts > 0 && hurts.Count == 0 && bones == 0 && pinned.Count == 0 && crew.All(u => !u.Dead),
            $"{under.Count - Left(under)} of {under.Count} dug; the block {(blockStands < 24 * 24 * 24 / 2 ? "came down" : "still stands")} ({blockStands} of {24 * 24 * 24} where they were), {shouts} warnings called; {underCost}");
        Reset();

        // ---- a shaft three metres across and four deep: dug from its edge down, with a way out at the end
        int sx0 = (int)gx - 120, sz0 = (int)gz - 200;
        Clear(sx0 - 40, sz0 - 40, sx0 + 64, sz0 + 64);
        int sTop = w.SurfAt(sx0 + 12, sz0 + 12);
        var shaft = new VoxelList();
        for (int z = sz0; z < sz0 + 24; z++)
            for (int x = sx0; x < sx0 + 24; x++)
                for (int y = w.SurfAt(x, z) - 1; y >= sTop - 32; y--)
                    if (Labour.Diggable(w.Get(x, y, z))) shaft.Add(x, y, z, Mat.Air);
        Run(.5);
        sim.DesignateRemove(shaft);
        for (int i = 0; i < 3; i++) At(Kinds.Worker, sx0 + 12 + (i - 1) * 16, sz0 + 40);
        Watch(600, () => Left(shaft) == 0);
        // and the way out they cut for themselves, if they had to, dug too
        int Marked(int x0, int z0, int x1, int z1, int y0, int y1)
        {
            int n = 0;
            for (int y = y0; y < y1; y++) for (int z = z0; z < z1; z++) for (int x = x0; x < x1; x++) if (sim.Orders.At(x, y, z) != 0) n++;
            return n;
        }
        double lookAt = 0;
        Watch(300, () => { if (sim.Time < lookAt) return false; lookAt = sim.Time + 5; return Marked(sx0 - 140, sz0 - 140, sx0 + 164, sz0 + 164, sTop - 40, sTop + 16) == 0; });
        // and then, each set at the bottom of it and called away, every one of them gets out of it to the ground beside
        Run(5);
        int outside = sim.NearestStand(sx0 + 12, sz0 + 50, 2), bottom = sim.NearestStand(sx0 + 12, sz0 + 12, 2);
        int outOfIt = 0;
        foreach (var u in crew.Where(u => !u.Dead))
        {
            u.Site = -1; u.Jobs.Clear();
            u.Cell = bottom; (u.X, u.Y, u.Z) = sim.Nav.Feet(bottom); u.Path = null;
            u.Hold = true; u.Dest = u.Post = outside; u.NextSearch = 0;
            for (int t = 0; t < 160 && u.Cell != outside; t++) Run(.25);
            if (u.Y > sTop - 4) outOfIt++;
        }
        string shaftCost = Cost();
        check("three workers dig a shaft three metres across and four deep: none hurt, and every one of them can climb out of it after",
            Left(shaft) == 0 && hurts.Count == 0 && bones == 0 && pinned.Count == 0 && outOfIt == crew.Count(u => !u.Dead),
            $"{shaft.Count - Left(shaft)} of {shaft.Count} dug; {outOfIt} of {crew.Count(u => !u.Dead)} out from its bottom; {shaftCost}");
        Reset();

        // ---- our countermine, dug by three workers with no logs in the stores: no further than its first step, under
        // no ground without props; given logs, that step is shored and the rest dug on, every step shored, none fallen in
        // (where it goes on to be out of their reach is another matter)
        int mx0 = (int)gx - 120, mz0 = (int)gz + 330;
        Clear(mx0 - 40, mz0 - 40, mx0 + 160, mz0 + 40);
        double wood0 = sim.Stock[Stockpile.Wood];
        sim.Stock[Stockpile.Wood] = 0;
        var bore = sim.PlanCountermine(mx0, mz0, mx0 + 120, mz0, 24, out var mine);
        sim.DesignateRemove(bore);
        sim.AddCountermine(mine);
        for (int i = 0; i < 3; i++) At(Kinds.Worker, mx0 - 16, mz0 - 16 + i * 16);
        Watch(60);
        int bareDug = mine.Dug;
        sim.Stock[Stockpile.Wood] = Math.Max(50, wood0);
        Watch(300, () => mine.Dug == mine.Cells.Count);
        int shored = mine.Shored.FindAll(x => x).Count;
        string mineCost = Cost();
        int fallen = mine.Fallen.FindAll(x => x).Count;
        check("our countermine dug with no logs goes no further than its first step; given logs it is shored and dug on, every step shored, none fallen in, none hurt",
            bareDug <= 1 && mine.Dug > bareDug + 2 && shored >= mine.Dug - bareDug && fallen == 0 && hurts.Count == 0 && crew.All(u => !u.Dead),
            $"{bareDug} of {mine.Cells.Count} steps dug in a minute with no logs; then {mine.Dug} dug, {shored} shored, {fallen} fallen in; {mineCost}");
        Reset();
    }
}
