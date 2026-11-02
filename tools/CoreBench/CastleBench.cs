using System;
using System.Collections.Generic;
using System.Diagnostics;
using VoxelTower.Core;

/// <summary>
/// The citadel is a castle (--castle, StartingFort.cs). In every kind of valley on a few seeds, the castle a new game
/// finds is raised round the keep: the keep stands in a bailey walled in stone on three sides with a tower on each western
/// corner and a palisade behind the keep, a gatehouse in line with the keep's door with its gate in it, stairs up onto
/// the walk, lean-tos inside, the yard
/// furnished; half fallen, it leaves the workers its first job (the fort less than whole, its gate off its hinges); mended
/// whole and its gate hung, it shuts the keep in all round but for the gate: a worker inside goes nowhere outside with
/// the gate shut, and with it open walks out through it; one coming from the horde's side walks in to the keep's door
/// through the gate; the horde's field reaches the keep from outside (through the gate or over the walls). CASTLE_SAY=1
/// prints each valley's measures.
/// </summary>
static class CastleBench
{
    static readonly bool Say = Environment.GetEnvironmentVariable("CASTLE_SAY") == "1";

    /// <summary>
    /// Whether a square of ground so wide about a spot comes within three metres of the castle a new game finds: the
    /// scenes that look for level ground about the citadel to build on keep off it, so that what the castle is does not
    /// move them (they were written against the old fort's exact layout).
    /// </summary>
    public static bool NearFort(Sim sim, int x, int z, int half) => NearFort(sim.FortArea, x, z, half);

    /// <summary>The same before a game is begun in the valley: where its castle will stand.</summary>
    public static bool NearFort(Site site, int x, int z, int half) => NearFort(StartingFort.Area(site), x, z, half);

    static bool NearFort((int x0, int z0, int x1, int z1) area, int x, int z, int half)
    {
        var (fx0, fz0, fx1, fz1) = area;
        if (fx1 <= fx0) return false;
        const int Clear = 3 * VoxelWorld.PerMetre;
        return x + half >= fx0 - Clear && x - half <= fx1 + Clear && z + half >= fz0 - Clear && z - half <= fz1 + Clear;
    }

    public static void Run(Action<string, bool, string> check)
    {
        var kinds = (ValleyKind[])Enum.GetValues(typeof(ValleyKind));
        int valleys = 0, raised = 0, ruined = 0, whole = 0, shut = 0, outThrough = 0, inThrough = 0, reached = 0, settled = 0, settled0 = 0;
        double slowest = 0;
        var bad = new List<string>();
        foreach (var kind in kinds)
            foreach (uint seed in new uint[] { 1, 2, 3, 5 })
            {
                valleys++;
                var mp = new MapParams { Seed = seed, Kind = kind, Hexagon = true, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize };
                var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
                var site = MapGen.Generate(w, mp);
                w.Compact();
                if (!site.HasKeep) { valleys--; continue; }
                var sim = new Sim(w, site) { WildTarget = 0 };
                foreach (var u in sim.Units) if (u.Team == Team.Wild) { u.Dead = true; u.DiedAt = -999; }
                string tag = $"{kind} {seed}";
                Gateway gate;
                double cpu0 = BenchClock.CpuMs();
                try { gate = StartingFort.Raise(sim); }
                catch (Exception e) { bad.Add($"{tag}: raising it threw {e.GetType().Name} {e.Message}"); continue; }
                double raiseMs = BenchClock.CpuMs() - cpu0;
                slowest = Math.Max(slowest, raiseMs);
                raised++;
                var (x0, z0, x1, z1) = sim.FortArea;
                var sound0 = sim.Soundness(x0, z0, x1, z1);
                // (as found, the ruin has settled: nothing of it comes down in the first two minutes of a game)
                if (seed == 1)
                {
                    int fell0 = 0;
                    var first = new List<string>();
                    sim.Fell += (fx, fy, fz, m) => { if (fell0++ < 4) first.Add($"({fx - site.KeepX}, {fy - site.KeepBase}, {fz - site.KeepZ}) {Mat.NameOf(m)}"); };
                    for (int i = 0; i < 2400; i++) sim.Step(.05f);
                    if (fell0 > 0) bad.Add($"{tag}: as found, {fell0} voxels come down in two minutes, the first {string.Join(", ", first)}");
                    else settled0++;
                }
                else settled0++;
                bool ruin = sound0.standing < sound0.meant && gate is { Broken: true } && sim.Yards.Count >= 6;
                if (ruin) ruined++;
                else bad.Add($"{tag}: as found {sound0.standing}/{sound0.meant} standing, gate {(gate == null ? "none" : gate.Broken ? "off its hinges" : "hung")}, yard things {string.Join(", ", sim.Yards.ConvertAll(y => y.Kind.ToString()))}; not set out: {StartingFort.Refused}");
                // mended whole and its gate hung
                sim.InstantLabour = true;
                // (rubble cleared off a course is laid again by an order of its own: a few rounds)
                for (int round = 0; round < 4; round++)
                {
                    var lay = new VoxelList(); var clear = new VoxelList();
                    sim.MendPlan(sim.MeantIn(x0, z0, x1, z1), lay, clear);
                    if (lay.Count + clear.Count == 0) break;
                    sim.Mend(lay, clear);
                }
                if (gate != null) sim.MendGate(gate);
                sim.InstantLabour = false;
                // (a gate's leaves laid, it works from its next look)
                for (int i = 0; i < 20; i++) sim.Step(.05f);
                sim.Nav.Flush();
                var sound = sim.Soundness(x0, z0, x1, z1);
                // (two minutes on, on the first seed of each kind: what stands whole keeps standing, the lean-tos' roofs too)
                if (seed == 1) for (int i = 0; i < 2400; i++) sim.Step(.05f);
                var later = sim.Soundness(x0, z0, x1, z1);
                if (later.standing < sound.standing)
                {
                    int shown = 0;
                    foreach (int p in sim.MeantIn(x0, z0, x1, z1))
                    {
                        var (px, py, pz) = WaterPlanner.Unpack(p);
                        if (!Brushes.Free(w.Get(px, py, pz)) || shown++ >= 6) continue;
                        bad.Add($"{tag}: two minutes on, ({px - site.KeepX}, {py - site.KeepBase}, {pz - site.KeepZ}) {Mat.NameOf(sim.MeantAt(px, py, pz))} is gone");
                    }
                }
                settled += later.standing >= sound.standing ? 1 : 0;
                if (sound.standing >= sound.meant * 99 / 100 && gate is { Broken: false, Ready: true }) whole++;
                else bad.Add($"{tag}: mended {sound.standing}/{sound.meant}, gate {(gate == null ? "none" : gate.Broken ? "broken" : gate.Ready ? "hung" : "not ready")}");
                if (gate == null) continue;
                // shut in: from before the gatehouse nobody walks in to the keep's door with the gate shut, neither a worker nor
                // one of the horde (who has to hack the gate or bring ladders); with it open a worker in the yard walks out
                // through it (from the walk he may jump down outside, but nobody climbs up it)
                bool Outside(int c) { var (x, _, z) = sim.Nav.Feet(c); return x < x0 || x > x1 || z < z0 || z > z1; }
                var (kdx, _, kdz) = sim.Nav.Feet(sim.Gate);
                bool AtDoor(int c) { var (x, _, z) = sim.Nav.Feet(c); return Math.Abs(x - kdx) < 12 && Math.Abs(z - kdz) < 12; }
                int before = sim.NearestStand(x0 - 60, (z0 + z1) / 2, Kinds.Orc.Move.Height);
                var sneak = before < 0 ? null : sim.Paths.Find(before, AtDoor, _ => 0, Kinds.Worker.Move, 400000);
                var raid = before < 0 ? null : sim.Paths.Find(before, AtDoor, _ => 0, Kinds.Orc.Move, 400000);
                if (before >= 0 && sneak == null && raid == null) shut++;
                else
                {
                    var way = sneak ?? raid;
                    string where = "";
                    // where it crosses into the bailey, between the curtains' inner faces
                    bool Bailey(int c) { var (x, _, z) = sim.Nav.Feet(c); return x > site.KeepX - 80 && x < site.KeepX + 32 && z > site.KeepZ - 56 && z < site.KeepZ + 56; }
                    if (way != null)
                        for (int i = 1; i < way.Count; i++)
                        {
                            if (!Bailey(way[i]) || Bailey(way[i - 1])) continue;
                            for (int j = Math.Max(0, i - 3); j <= Math.Min(way.Count - 1, i + 1); j++)
                            {
                                var (ox, oy, oz) = sim.Nav.Feet(way[j]);
                                where += $" ({ox - site.KeepX:0}, {oy - site.KeepBase:0}, {oz - site.KeepZ:0})";
                            }
                            break;
                        }
                    if (gate != null) where += $"; the gate open {gate.Open:0.00}, ready {gate.Ready}, broken {gate.Broken}, at ({gate.Middle.x - site.KeepX:0}, {gate.Middle.z - site.KeepZ:0})";
                    if (Say && way != null)
                        foreach (int c in way)
                        {
                            var (fx, fy, fz) = sim.Nav.Feet(c);
                            if (!Bailey(c) && Math.Abs(fz - site.KeepZ) > 60 && fy - site.KeepBase > 6)
                            {
                                int cx = (int)fx, cz = (int)fz;
                                var col = new System.Text.StringBuilder();
                                for (int y = site.KeepBase - 2; y < site.KeepBase + 14; y++) col.Append(Mat.NameOf(w.Get(cx, y, cz))[..Math.Min(4, Mat.NameOf(w.Get(cx, y, cz)).Length)]).Append(' ');
                                where += $"; the column at ({cx - site.KeepX}, {cz - site.KeepZ}), ground made {site.HTop[cz * w.SX + cx] - site.KeepBase}: {col}";
                                break;
                            }
                        }
                    bad.Add($"{tag}: with the gate shut {(before < 0 ? "no cell before the gatehouse" : sneak != null ? "a worker" : "one of the horde")} walks in to the keep's door{where}");
                }
                // (through a gate of the castle)
                bool ByGate(List<int> way)
                {
                    if (way == null) return false;
                    foreach (int c in way)
                    {
                        var (x, _, z) = sim.Nav.Feet(c);
                        foreach (var g in sim.Gates) { var (gx, _, gz) = g.Middle; if (Math.Abs(x - gx) < 12 && Math.Abs(z - gz) < 12) return true; }
                    }
                    return false;
                }
                var outWay = sim.Paths.Find(sim.Gate, Outside, _ => 0, Kinds.Worker.Move, 200000, gates: true);
                if (ByGate(outWay)) outThrough++; else bad.Add($"{tag}: with the gate open a worker in the yard {(outWay == null ? "gets nowhere outside" : "gets out not by the gate")}");
                // one coming from the horde's side walks in to the keep's door through the gate
                var (ex, ez) = site.EntryOn(site.EntrySide, .5);
                int from = sim.NearestStand(ex, ez, 2);
                var inWay = from < 0 ? null : sim.Paths.Find(from, AtDoor, _ => 0, Kinds.Worker.Move, 600000, gates: true);
                if (ByGate(inWay)) inThrough++; else bad.Add($"{tag}: from the horde's side a worker {(inWay == null ? "gets nowhere near the keep's door" : "gets to the keep's door not by the gate")}");
                // the horde's field reaches the keep from outside the castle
                var field = FlowField.Compute(sim.Nav, Kinds.Orc.Move, sim.Goals, 900, wood: true, earth: true);
                if (before >= 0 && !float.IsPositiveInfinity(field.Dist[before])) reached++;
                else bad.Add($"{tag}: the horde's field from before the gatehouse {(before < 0 ? "has no cell to start at" : "does not reach the keep")}");
                if (Say) Console.WriteLine($"      {tag}: found {sound0.standing * 100 / Math.Max(1, sound0.meant)} % standing, mended {sound.standing * 100 / Math.Max(1, sound.meant)} %; in by {(inWay?.Count ?? -1)} cells; the horde {(before >= 0 ? field.Dist[before] : -1):0} s off; raised in {raiseMs:0} ms of work");
            }
        foreach (var b in bad) Console.WriteLine($"      {b}");
        check($"the castle is raised round the keep in every kind of valley ({valleys} valleys), half fallen as a new game finds it, its gate off its hinges, its yard furnished",
            raised == valleys && ruined == valleys && settled0 == valleys, $"raised {raised}, as found {ruined}, settled {settled0}; the slowest {slowest:0} ms");
        check("mended whole and its gate hung it shuts the keep in: with the gate shut nobody walks in from before the gatehouse, with it open a worker in the yard walks out through it; and nothing of it comes down by itself in two minutes",
            whole == valleys && shut == valleys && outThrough == valleys && settled == valleys, $"whole {whole}, shut in {shut}, out through the gate {outThrough}, still whole two minutes on {settled} of {valleys}");
        check("one coming from the horde's side walks in to the keep's door through the gate, and the horde's field reaches the keep from before the gatehouse",
            inThrough == valleys && reached == valleys, $"in through the gate {inThrough}, the horde's field {reached} of {valleys}");
        check("the castle is raised quickly (under a second and a half of work a valley)", slowest < 1500, $"the slowest {slowest:0} ms");
    }
}
