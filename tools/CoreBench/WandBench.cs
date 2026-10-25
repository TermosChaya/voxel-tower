using System;
using System.Collections.Generic;
using VoxelTower.Core;

/// <summary>
/// The bench's magic wand (--wand): on a wall of stone with a timber walk on top, the wand on the stone takes the whole
/// stone of it, its mortar and every shade, and none of the timber; on the timber, the timber only; told exactly, one
/// stone's shade; a diagonal of single voxels is one thing with the corners and many without; it stops at its cap; a
/// part laid over again in logs comes down and goes up as logs, and what is still to be laid again is kept in a save;
/// a blueprint of what it took keeps only that.
/// </summary>
static class WandBench
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
        int kx = site.KeepX, kz = site.KeepZ, wx = kx - 220, z0 = kz - 40, z1 = kz + 40;
        for (int z = z0 - 30; z <= z1 + 30; z++)
            for (int x = wx - 40; x <= wx + 40; x++)
                for (int y = w.SurfAt(x, z), ct = w.ColTop(x, z); y < ct; y++)
                    if (Mat.IsVeg[w.Get(x, y, z)]) w.Set(x, y, z, Mat.Air);

        // a wall of stone ten metres long, a metre thick and three high, and a walk of logs half a metre thick on top
        sim.InstantLabour = true;
        int top = Shapes.LevelTop(w, wx, kz, 24);
        sim.DesignateBuild(Shapes.Line(w, site, wx, z0, wx, z1, new Stroke { Mat = Mat.Stone, Width = 8, Height = 24, Level = true }, top));
        int walk = Shapes.LevelTop(w, wx, kz, 4);
        sim.DesignateBuild(Shapes.Line(w, site, wx, z0, wx, z1, new Stroke { Mat = Mat.Wood, Width = 8, Height = 4, Level = true }, walk));
        sim.InstantLabour = false;
        int stone = 0, wood = 0;
        byte shade = 0;
        for (int z = z0 - 8; z <= z1 + 8; z++)
            for (int x = wx - 8; x <= wx + 8; x++)
                for (int y = 0; y < w.SY; y++)
                {
                    byte m = w.Get(x, y, z);
                    if (Mat.IsMasonry(m)) { stone++; if (m != Mat.Mortar) shade = m; }
                    else if (m == Mat.Wood) wood++;
                }

        var L = new VoxelList();
        var seen = new HashSet<int>();
        // the hovered voxel: on the stone's face, a metre up; on the walk's top
        int sy = top - 16, sx = wx;
        while (!Mat.IsMasonry(w.Get(sx, sy, kz))) sx--;
        bool whole = Wand.Fill(w, sx, sy, kz, false, false, L, seen);
        int took = L.Count, tookWood = 0, tookStone = 0;
        for (int i = 0; i < L.Count; i++) { if (L.M(i) == Mat.Wood) tookWood++; if (Mat.IsMasonry(L.M(i))) tookStone++; }
        check("the wand on a stone wall takes all of its stone, every shade and the mortar, and none of the logs on it",
            whole && stone > 1000 && tookStone == stone && tookWood == 0, $"stone {stone}, taken {took} ({tookStone} stone, {tookWood} logs)");

        int wy = walk - 1, wxx = wx;
        while (w.Get(wxx, wy, kz) != Mat.Wood && wy > 0) wy--;
        Wand.Fill(w, wxx, wy, kz, false, false, L, seen);
        int woodTaken = 0;
        for (int i = 0; i < L.Count; i++) if (L.M(i) == Mat.Wood) woodTaken++;
        check("the wand on the walk takes the logs of it only", wood > 100 && woodTaken == wood && L.Count == wood, $"logs {wood}, taken {L.Count}");

        // told exactly: a stone's own shade, joined face to face — far less than the wall (on the wall's face: within
        // it is all mortar)
        int ex = wx - 12, ey = sy, ez = kz;
        while (!Mat.IsMasonry(w.Get(ex, ey, ez))) ex++;
        // a stone of the face, past the joints of mortar between the courses and the stones
        for (int i = 0; i < 64 && w.Get(ex, ey, ez) is Mat.Mortar or Mat.Air; i++) { ey = sy + i % 8; ez = kz + i / 8; }
        byte one = w.Get(ex, ey, ez);
        Wand.Fill(w, ex, ey, ez, true, false, L, seen);
        bool allOne = true;
        for (int i = 0; i < L.Count; i++) if (L.M(i) != one) allOne = false;
        check("told exactly, the wand takes the one shade only, joined to it", allOne && L.Count > 0 && L.Count < stone / 3, $"«{Mat.NameOf(one)}» {one}: {L.Count} of {stone}");

        // a diagonal of single voxels up in the air: one thing with the corners, a voxel alone without
        int dy = w.SY - 20;
        for (int i = 0; i < 12; i++) w.Set(wx + 20 + i, dy + i, kz, Mat.Wood);
        Wand.Fill(w, wx + 20, dy, kz, true, false, L, seen);
        int faces = L.Count;
        Wand.Fill(w, wx + 20, dy, kz, true, true, L, seen);
        check("a diagonal of voxels is one with the edges and corners, each voxel alone without", faces == 1 && L.Count == 12, $"by faces {faces}, with corners {L.Count}");
        for (int i = 0; i < 12; i++) w.Set(wx + 20 + i, dy + i, kz, Mat.Air);

        // the valley's ground is no one thing: cut off at the cap
        int gx = wx - 30, gz = kz;
        bool groundWhole = Wand.Fill(w, gx, w.SurfAt(gx, gz) - 1, gz, false, false, L, seen);
        check("the valley's ground is cut off at the wand's cap", !groundWhole && L.Count == Wand.Cap, $"{L.Count} of {Wand.Cap}, whole {groundWhole}");

        // the walk laid over again in stone: it comes down, and goes up again as stone in courses
        Wand.Fill(w, wxx, wy, kz, false, false, L, seen);
        sim.InstantLabour = true;
        int relaid = sim.Relay(L, Mat.Stone);
        sim.InstantLabour = false;
        int woodLeft = 0, stoneNow = 0;
        for (int z = z0 - 8; z <= z1 + 8; z++)
            for (int x = wx - 8; x <= wx + 8; x++)
                for (int y = 0; y < w.SY; y++)
                {
                    byte m = w.Get(x, y, z);
                    if (m == Mat.Wood) woodLeft++; else if (Mat.IsMasonry(m)) stoneNow++;
                }
        check("the walk laid over again in stone comes down and goes up as stone", relaid == wood && woodLeft == 0 && stoneNow == stone + wood && sim.Relays.Count == 0,
            $"marked {relaid} of {wood}; logs left {woodLeft}, stone {stone} → {stoneNow}, still to lay {sim.Relays.Count}");

        // laid over again by the workers, taken back before it came down: forgotten
        Wand.Fill(w, sx, sy, kz, false, false, L, seen);
        sim.BeginMarking();
        int marked = sim.Relay(L, Mat.Wood);
        sim.EndMarking("Палочка");
        int waiting = sim.Relays.Count;

        // what is still to be laid again is kept in a save
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
        check("what is to be laid again is kept in a save", waiting > 1000 && sim2.Relays.Count == waiting, $"{waiting} waiting, {sim2.Relays.Count} read back");

        sim.UndoMarking(out int undone);
        for (int i = 0; i < 4; i++) sim.Step(.25f);
        check("laying over again taken back before it came down is forgotten", marked == waiting && undone > 0 && sim.Relays.Count == 0,
            $"marked {marked}, undone {undone}, still to lay {sim.Relays.Count}");

        // a blueprint of what the wand took: the stone of the wall alone, not the logs that were on it
        Wand.Fill(w, sx, sy, kz, false, false, L, seen);
        var bp = Blueprint.FromVoxels(L, 1, null);
        check("a blueprint of what the wand took keeps that only", bp != null && bp.Stone == stoneNow && bp.Wood == 0, $"{bp?.Describe()}: stone {bp?.Stone} of {stoneNow}, logs {bp?.Wood}");

        // fast enough to follow the cursor: the whole cap in a few milliseconds (of the processor's time, which does not
        // grow on a busy machine)
        double c0 = BenchClock.CpuMs();
        for (int i = 0; i < 5; i++) Wand.Fill(w, gx, w.SurfAt(gx, gz) - 1, gz, false, false, L, seen);
        double ms5 = (BenchClock.CpuMs() - c0) / 5;
        check("the wand fills its whole cap fast enough to follow the cursor", ms5 < 40, $"{ms5:0.0} ms for {Wand.Cap} voxels");
    }
}
