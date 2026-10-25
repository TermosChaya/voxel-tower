using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>How a room framed as a dwelling stands: whether it is one, why not, its floor, and what furniture fits in it.</summary>
public sealed class DwellingCheck
{
    /// <summary>Closed all round, roofed, with a door, and floor enough: a dwelling.</summary>
    public bool Ok;
    /// <summary>What it lacks, for the player; empty when it lacks nothing.</summary>
    public string Why = "";
    /// <summary>Square metres of floor with a man's headroom over it.</summary>
    public float Area;
    /// <summary>How many bunks the workers will set in it.</summary>
    public int Bunks;
}

/// <summary>A dwelling's furnishing as it is meant to stand: every voxel of it, and which of them make up each bunk.</summary>
sealed class Furnishing
{
    public readonly VoxelList Voxels = new();
    /// <summary>For each bunk, the first of its voxels and how many there are, in <see cref="Voxels"/>.</summary>
    public readonly List<(int from, int count)> Bunks = new();
    /// <summary>The floor it was laid out for: when the floor is no longer that one, it is laid out again.</summary>
    public long Key;
}

/// <summary>
/// Dwellings. The player frames a room with the zone brush and calls it a dwelling; it is one when it is closed — walls
/// all round with no gap a body could pass, a roof over it, a door (a gate) in it — and has floor enough. Then the
/// workers furnish it of the timber store: a bunk with its bedding along the walls for every few square metres of floor,
/// and a table with its benches in the middle. Every bunk that stands whole is a place in the household. A dwelling
/// broken open — a wall or the roof hacked through or burnt, its door broken — gives no places until it is closed again.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The least floor a dwelling may have, in square metres; and the floor a bunk wants, with the room to pass it.</summary>
    public const float DwellingFloor = 6, FloorPerBunk = 3;
    const int MostBunks = 12;
    /// <summary>A man's headroom over the floor, in voxels.</summary>
    const int Headroom = 16;
    /// <summary>How far out of the frame and how high over the floor the room is looked for before it counts as open.</summary>
    const int RoomReach = 24, RoomHigh = 88, RoomSpan = 160;

    readonly Dictionary<int, Furnishing> _furnishing = new();
    double _nextDwellings;

    /// <summary>
    /// The doors of houses drawn with the rectangle, waiting for their wall. When the wall stands where the door goes,
    /// up over the doorway, what is still marked to be laid in the doorway is let go, and the gateway is cut and the
    /// gate hung as the gate tool does. A door whose house is taken back (nothing stands or is marked there) is let go.
    /// </summary>
    public readonly List<(Shapes.HouseDoor door, double since)> HouseDoors = new();
    double _nextDoors;

    /// <summary>
    /// A house drawn: its door waits for the wall, and its floor is a dwelling to furnish once it is closed; the dwelling
    /// keeps the top of the walls (its Depth), where a storey would go up.
    /// </summary>
    public void PlanHouse(Shapes.HouseDoor door, int x0, int z0, int x1, int z1)
    {
        HouseDoors.Add((door, Time));
        AddZone(x0, z0, x1, z1, Zone.Dwelling, door.Top);
    }

    /// <summary>Voxels to stand as an arch once they are laid: held as long as they are joined to the rest (a house's roof).</summary>
    public void Arch(VoxelList L)
    {
        if (L != null) for (int i = 0; i < L.Count; i++) Arched.SetRaw(L.X(i), L.Y(i), L.Z(i), 1);
    }

    void WriteHouseDoors(System.IO.BinaryWriter w)
    {
        w.Write(HouseDoors.Count);
        foreach (var (d, since) in HouseDoors) { w.Write(d.X); w.Write(d.Y); w.Write(d.Z); w.Write(d.NX); w.Write(d.NZ); w.Write(d.Thick); w.Write(d.Top); w.Write(since); }
    }

    void ReadHouseDoors(System.IO.BinaryReader r)
    {
        HouseDoors.Clear();
        for (int i = r.ReadInt32(); i > 0; i--)
            HouseDoors.Add((new Shapes.HouseDoor { X = r.ReadInt32(), Y = r.ReadInt32(), Z = r.ReadInt32(), NX = r.ReadInt32(), NZ = r.ReadInt32(), Thick = r.ReadInt32(), Top = r.ReadInt32() }, r.ReadDouble()));
    }

    void TickHouseDoors()
    {
        TickStoreys();
        if (HouseDoors.Count == 0 || Time < _nextDoors) return;
        _nextDoors = Time + 2;
        for (int i = HouseDoors.Count - 1; i >= 0; i--)
        {
            var (d, since) = HouseDoors[i];
            int ax = -d.NX, az = -d.NZ;
            // something put up before it since it was planned: to a free side of its house, or it waits (Sim.Houses.cs)
            if (d.Top > 0)
            {
                if (DoorStillFree(d) is not { } free) continue;
                if (free.X != d.X || free.Z != d.Z) { HouseDoors[i] = (free, since); continue; }
            }
            bool stands = true, meant = false;
            int wallTop = d.Top > 0 ? d.Top : d.Y - 8 + Gateway.Height + 4;
            // the wall through its thickness at the door's middle, from the threshold to over the lintel
            for (int k = 0; k < d.Thick && stands; k++)
                for (int y = d.Y - 8; y < d.Y - 8 + Gateway.Height + 4; y++)
                {
                    int x = d.X + ax * k, z = d.Z + az * k;
                    if (Orders.At(x, y, z) != 0) meant = true;
                    if (World.Get(x, y, z) == Mat.Air) { stands = false; break; }
                }
            // and all of it round the doorway and over it to the walls' top laid: cut under a wall half laid, what
            // is laid over the doorway has nothing at its ends to hold it
            for (int k = 0; k < d.Thick && stands; k++)
                for (int u = -Gateway.Half - 6; u <= Gateway.Half + 6 && stands; u++)
                    for (int y = d.Y - 8; y < wallTop && stands; y++)
                    {
                        int x = d.NX != 0 ? d.X + ax * k : d.X + u, z = d.NX != 0 ? d.Z + u : d.Z + az * k;
                        byte o = Orders.At(x, y, z);
                        if (o != 0 && !Orders.IsRemove(o)) { meant = true; stands = false; }
                    }
            if (!stands)
            {
                // nothing standing and nothing marked there for a long while: the house was taken back
                if (!meant && Orders.At(d.X, d.Y, d.Z) == 0 && Time - since > 60) HouseDoors.RemoveAt(i);
                continue;
            }
            // the threshold at the top of the house's floor, whatever the ground outside
            // iron over the doorway (the gatehouse's lintel, a house walled in iron): its leaves are iron too
            bool iron = false;
            for (int y = d.Y - 8 + Gateway.Height - 2; y < d.Y - 8 + Gateway.Height + 6 && !iron; y++) iron = Mat.IsMetalWork(World.Get(d.X, y, d.Z));
            var plan = GatePlan.At(World, Site, new VoxelHit { X = d.X, Y = d.Y, Z = d.Z, NX = d.NX, NZ = d.NZ }, d.Top > 0 ? d.Y - 8 : -1, iron, outward: d.Top > 0);
            if (plan == null) continue;
            // the leaves hang on the face the door looks out of, their foot at the top of the floor, and that is a
            // course or more over the ground outside: a doorstep of what the wall is of is laid under them down to the
            // ground, so that the first course of them is set on something (laid on nothing, it would wait for ever, or
            // come down whole on whoever is under it)
            var step = new VoxelList();
            byte sm = Labour.CostKind(World.Get(d.X, plan.Y - 1, d.Z)) == Stockpile.Stone ? Mat.Stone : Mat.Wood;
            for (int j = 0; j < plan.Leaves.Count; j++)
                if (plan.Leaves.Y(j) == plan.Y)
                    for (int y = plan.Y - 1; y > 0 && plan.Y - y <= 16 && Brushes.Free(World.Get(plan.Leaves.X(j), y, plan.Leaves.Z(j))); y--)
                        step.Add(plan.Leaves.X(j), y, plan.Leaves.Z(j), sm);
            if (step.Count > 0) DesignateBuild(step);
            // the doorway is not to be laid any more: what is marked in it is let go before it is cut
            for (int k = 0; k < d.Thick; k++)
                for (int u = plan.U0; u < plan.U0 + Gateway.Width; u++)
                    for (int y = plan.Y; y < plan.Y + Gateway.Height; y++)
                    {
                        int x = d.NX != 0 ? d.X + ax * k : u, z = d.NX != 0 ? u : d.Z + az * k;
                        if (Orders.At(x, y, z) != 0 && !Orders.IsRemove(Orders.At(x, y, z))) Orders.Clear(x, y, z);
                    }
            AddGate(plan);
            HouseDoors.RemoveAt(i);
        }
    }

    /// <summary>The bunks standing whole in closed dwellings: the household's places beyond the citadel's.</summary>
    public int DwellingBeds()
    {
        // a house framed twice over is one house: each bunk counts once, by where it stands
        _bunksCounted.Clear();
        foreach (var z in Zones) if (z.Kind == Zone.Dwelling && z.Closed) _bunksCounted.UnionWith(z.BunksWhole);
        return _bunksCounted.Count;
    }
    readonly HashSet<int> _bunksCounted = new();

    /// <summary>
    /// The room in a frame: the space reached from the floor inside it without passing a wall, a roof or a door. It is a
    /// dwelling if it is closed, has a ready door in it and floor enough; its floor cells are given back for furnishing.
    /// </summary>
    public DwellingCheck CheckDwelling(int x0, int z0, int x1, int z1) => Room(x0, z0, x1, z1, null);

    DwellingCheck Room(int x0, int z0, int x1, int z1, List<int> floor)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        var res = new DwellingCheck();
        if (x1 - x0 > RoomSpan || z1 - z0 > RoomSpan) { res.Why = "слишком большой участок для одного дома"; return res; }
        // the floors in the frame: in each column, every open voxel standing on something solid from the natural ground
        // up (the floor of a room, and its roof seen from above; not a hollow deep under the ground)
        var seeds = new List<(int x, int y, int z)>();
        int lowest = int.MaxValue;
        for (int z = z0 + 1; z < z1; z += 2)
            for (int x = x0 + 1; x < x1; x += 2)
            {
                int g = World.GroundTop(x, z);
                for (int y = Math.Max(1, g - 2); y < Math.Min(World.SY - 1, g + RoomHigh); y++)
                    if (!Walled(World.Get(x, y, z)) && Walled(World.Get(x, y - 1, z)))
                    {
                        seeds.Add((x, y, z));
                        lowest = Math.Min(lowest, y);
                    }
            }
        if (seeds.Count == 0) { res.Why = "в рамке нет пола"; return res; }
        // the doors: the cells a gate's leaves fill or sweep through stand for wall, and are seen (a gate hung and still
        // being laid is a door already; one hacked through is a hole)
        var doors = new HashSet<int>();
        foreach (var g in Gates)
        {
            if (g.Broken) continue;
            doors.UnionWith(g.Cells);
            // a door hung outside (a house's) sweeps nothing in the room: its doorway through the wall is the door seen
            // from within
            for (int k = 0; k <= Shapes.HouseWall; k++)
                for (int u = g.U0; u < g.U0 + Gateway.Width; u += 4)
                    for (int y = g.Y; y < g.Y + Gateway.Height; y += 4)
                    {
                        int a = g.P - g.In * k;
                        doors.Add(Nav.CellOf(g.AlongX ? u : a, y, g.AlongX ? a : u));
                    }
        }
        int bx0 = Math.Max(0, x0 - RoomReach), bz0 = Math.Max(0, z0 - RoomReach), bx1 = Math.Min(World.SX - 1, x1 + RoomReach), bz1 = Math.Min(World.SZ - 1, z1 + RoomReach);
        int by0 = Math.Max(1, lowest - 2), by1 = Math.Min(World.SY - 1, lowest + RoomHigh);
        int W = bx1 - bx0 + 1, D = bz1 - bz0 + 1, H = by1 - by0 + 1;
        // which space each voxel was reached from (0 none yet), and how each space got out if it did: a space left half
        // filled when it got out is outside, and so is any that later runs into it
        var seen = new int[W * D * H];
        var outOf = new List<(bool sideways, bool upward)> { (false, false) };
        int Idx(int x, int y, int z) => ((y - by0) * D + (z - bz0)) * W + (x - bx0);
        // the seeds nearest the frame's middle first: the room is there, the outside about the edges
        float mx = (x0 + x1) / 2f, mz = (z0 + z1) / 2f;
        seeds.Sort((a, b) =>
        {
            int c = ((a.x - mx) * (a.x - mx) + (a.z - mz) * (a.z - mz)).CompareTo((b.x - mx) * (b.x - mx) + (b.z - mz) * (b.z - mz));
            return c != 0 ? c : a.y.CompareTo(b.y);
        });
        string firstWhy = null;
        List<int> best = null;
        bool bestDoor = false;
        int bestFloor = 0;
        var stack = new Stack<int>();
        foreach (var s in seeds)
        {
            if (s.y < by0 || s.y > by1 || seen[Idx(s.x, s.y, s.z)] != 0) continue;
            var region = new List<int>();
            bool sideways = false, upward = false, door = false;
            int id = outOf.Count;
            outOf.Add((false, false));
            seen[Idx(s.x, s.y, s.z)] = id;
            stack.Clear();
            stack.Push(WaterPlanner.Pack(s.x, s.y, s.z));
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                region.Add(p);
                var (x, y, z) = WaterPlanner.Unpack(p);
                for (int d = 0; d < 6; d++)
                {
                    int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0), nz = z + (d == 4 ? 1 : d == 5 ? -1 : 0);
                    if (nx < bx0 || nx > bx1 || nz < bz0 || nz > bz1) { sideways = true; continue; }
                    if (ny > by1) { upward = true; continue; }
                    if (ny < by0) continue;
                    int i = Idx(nx, ny, nz);
                    if (seen[i] == id) continue;
                    if (Walled(World.Get(nx, ny, nz))) continue;
                    if (doors.Contains(Nav.CellOf(nx, ny, nz))) { door = true; continue; }
                    if (seen[i] != 0) { var (sw, up) = outOf[seen[i]]; sideways |= sw; upward |= up; continue; }
                    seen[i] = id;
                    stack.Push(WaterPlanner.Pack(nx, ny, nz));
                }
                // an open space runs on and on: no need to fill the valley to know it
                if (sideways && upward) break;
            }
            outOf[id] = (sideways, upward);
            if (sideways || upward)
            {
                // open: under the open sky, or through a gap in the walls
                if (firstWhy == null)
                {
                    bool roofed = false;
                    for (int y = s.y + 1; y <= by1 && !roofed; y++) roofed = Walled(World.Get(s.x, y, s.z));
                    firstWhy = roofed ? "стены не сомкнуты: есть проход наружу" : "нет крыши";
                }
                continue;
            }
            // a closed space with no floor a man can stand on (a hollow in a wall) is no room
            int floorHere = FloorOf(region, null);
            if (floorHere > bestFloor) { best = region; bestDoor = door; bestFloor = floorHere; }
        }
        if (best == null) { res.Why = firstWhy ?? "комната не найдена"; return res; }
        var cells = new List<int>();
        FloorOf(best, cells);
        res.Area = cells.Count / (float)(VoxelWorld.PerMetre * VoxelWorld.PerMetre);
        res.Bunks = FurnishLevels(cells, Math.Clamp((int)(res.Area / FloorPerBunk), 1, MostBunks)).Bunks.Count;
        if (!bestDoor) { res.Why = "нет двери: поставьте ворота ({ToolGate}) в стену"; return res; }
        if (res.Area < DwellingFloor) { res.Why = $"тесно: {res.Area:0.#} м² пола, нужно хотя бы {DwellingFloor:0}"; return res; }
        if (res.Bunks == 0) { res.Why = "лежанке негде встать: нужна стена и пол два метра в длину"; return res; }
        res.Ok = true;
        floor?.AddRange(cells);
        return res;
    }

    /// <summary>The floor of a space: its open voxels over something solid, with a man's headroom over them.</summary>
    int FloorOf(List<int> region, List<int> cells)
    {
        var room = new HashSet<int>(region);
        int n = 0;
        foreach (int p in region)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            byte under = World.Get(x, y - 1, z);
            // the floor is what the room stands on, not the tops of its furniture
            if (!Walled(under)) continue;
            bool head = true;
            for (int k = 1; k < Headroom && head; k++) head = room.Contains(WaterPlanner.Pack(x, y + k, z));
            if (!head) continue;
            n++;
            cells?.Add(p);
        }
        return n;
    }

    /// <summary>What closes a room: anything a body cannot pass (furniture and the dressing do not; a ladder's hole does not).</summary>
    static bool Walled(byte m) => m != Mat.Air && !Mat.Passable[m];

    /// <summary>Each dwelling looked over now and then: whether it still stands closed, its furniture ordered, its bunks counted.</summary>
    void TickDwellings()
    {
        if (Time < _nextDwellings) return;
        _nextDwellings = Time + 2;
        foreach (var z in Zones)
        {
            if (z.Kind != Zone.Dwelling || Time < z.NextLook) continue;
            z.NextLook = Time + 4 + (z.Id % 5) * .3;
            LookOver(z);
        }
    }

    /// <summary>A dwelling looked over: closed or not, its furnishing planned and ordered, its whole bunks counted.</summary>
    public void LookOver(Zone z)
    {
        var floor = new List<int>();
        var check = Room(z.X0, z.Z0, z.X1, z.Z1, floor);
        // the floor in one order however the room was flooded (a hatch swung open moves where the flood starts), so
        // that its layout holds as long as the floor does
        floor.Sort();
        bool was = z.Closed;
        int had = z.Beds;
        // looked over the first time (just set out, or read back from a save): no word of it, as nothing changed
        bool quiet = !z.Seen;
        z.Seen = true;
        z.Closed = check.Ok;
        z.Why = check.Why;
        z.Area = check.Area;
        // closed, but every door of it opening into a wall or another house: nobody gets in (Sim.Houses.cs)
        if (check.Ok && z.Depth > 1 && DoorsOf(z) is { Count: > 0 } doors && !doors.Exists(g => g.Broken || DoorLeadsOut(g, z)))
        {
            check.Ok = false;
            check.Why = "дверь упирается в стену: поставьте новую воротами ({ToolGate}) по стене дома, где снаружи свободно, а старую заложите щелчком воротами по ней";
            z.Closed = false; z.Why = check.Why;
            if (!quiet && _doorBlockedSaid.Add(-z.Id)) Say("Дверь жилого дома упирается в стену: в дом не войти. Поставьте новую дверь воротами по стене дома, где снаружи свободно, а старую заложите.", 2, (z.X0 + z.X1) / 2f, (z.Z0 + z.Z1) / 2f);
        }
        // a town building is furnished as its kind wants, and gives no beds (Sim.Town.cs)
        if (BuildingOn(z) is { } building) { LookOverBuilding(z, building, check, floor, quiet); return; }
        if (!check.Ok)
        {
            z.Beds = 0;
            z.BunksWhole.Clear();
            // (not while its door is moving: the new one is cut as soon as its wall stands)
            bool moving = HouseDoors.Exists(d => d.door.Top > 0 && d.door.X >= z.X0 && d.door.X <= z.X1 && d.door.Z >= z.Z0 && d.door.Z <= z.Z1);
            if (was && had > 0 && !quiet && !moving) Say($"Жилой дом открыт ({check.Why}): его {had} {PlacesWord(had)} не считаются, пока его не закроют.", 2, (z.X0 + z.X1) / 2f, (z.Z0 + z.Z1) / 2f);
            return;
        }
        // laid out once for its floor and its doors; again if the floor is another now (a storey over it, the floor laid
        // since) or a door is put in or walled up, what stands of the old layout and is not in the new one taken away
        long key = FloorKey(floor);
        if (z.Depth > 1) foreach (var g in DoorsOf(z)) key = key * 37 + g.Id;
        if (!_furnishing.TryGetValue(z.Id, out var plan) || plan.Voxels.Count == 0 || plan.Key != key)
        {
            var fresh = FurnishLevels(floor, check.Bunks);
            fresh.Key = key;
            if (plan != null) Unfurnish(plan, fresh);
            _furnishing[z.Id] = plan = fresh;
        }
        z.Planned = plan.Bunks.Count;
        // what of the furnishing is not standing and not ordered yet is ordered
        var want = new VoxelList();
        for (int i = 0; i < plan.Voxels.Count; i++)
        {
            int x = plan.Voxels.X(i), y = plan.Voxels.Y(i), zz = plan.Voxels.Z(i);
            byte m = plan.Voxels.M(i), now = World.Get(x, y, zz);
            if (now != m && Brushes.Free(now) && Orders.At(x, y, zz) == 0) want.Add(x, y, zz, m);
        }
        if (want.Count > 0) DesignateBuild(want);
        // a bunk counts once it stands near whole
        int beds = 0;
        z.BunksWhole.Clear();
        foreach (var (from, count) in plan.Bunks)
        {
            int whole = 0;
            for (int i = from; i < from + count; i++)
                if (World.Get(plan.Voxels.X(i), plan.Voxels.Y(i), plan.Voxels.Z(i)) == plan.Voxels.M(i)) whole++;
            if (whole < count * .9f) continue;
            beds++;
            z.BunksWhole.Add(WaterPlanner.Pack(plan.Voxels.X(from), plan.Voxels.Y(from), plan.Voxels.Z(from)));
        }
        z.Beds = beds;
        if (quiet) return;
        if (beds > had) Say(had == 0 && !was ? $"Жилой дом обставлен: {beds} {PlacesWord(beds)} в войске." : $"В жилом доме готово лежанок: {beds}.", 1, (z.X0 + z.X1) / 2f, (z.Z0 + z.Z1) / 2f);
        else if (!was && beds > 0) Say($"Жилой дом снова закрыт: {beds} {PlacesWord(beds)} в войске.", 1, (z.X0 + z.X1) / 2f, (z.Z0 + z.Z1) / 2f);
    }

    /// <summary>What tells one floor from another: how many cells it has and where.</summary>
    static long FloorKey(List<int> floor)
    {
        long k = floor.Count;
        foreach (int p in floor) k = k * 31 + p;
        return k;
    }

    /// <summary>What stands of an old layout and has no place in the new: taken away, as the workers would carry it off.</summary>
    void Unfurnish(Furnishing old, Furnishing fresh)
    {
        var keep = new HashSet<int>();
        for (int i = 0; i < fresh.Voxels.Count; i++) keep.Add(WaterPlanner.Pack(fresh.Voxels.X(i), fresh.Voxels.Y(i), fresh.Voxels.Z(i)));
        for (int i = 0; i < old.Voxels.Count; i++)
        {
            int x = old.Voxels.X(i), y = old.Voxels.Y(i), z = old.Voxels.Z(i);
            if (keep.Contains(WaterPlanner.Pack(x, y, z)) || !Mat.IsFurniture[World.Get(x, y, z)]) continue;
            World.Set(x, y, z, Mat.Air);
            Forget(x, y, z);
        }
    }

    /// <summary>
    /// The furnishing of a house of more than one floor: each floor (the cells within a few voxels of the same height)
    /// furnished as a room of its own, the bunks shared out by how much floor each has, up to the bunks wanted.
    /// </summary>
    Furnishing FurnishLevels(List<int> floorCells, int bunks)
    {
        var heights = new List<int>();
        foreach (int p in floorCells) heights.Add(WaterPlanner.Unpack(p).y);
        heights.Sort();
        // the floors: heights that run on with gaps of a few voxels at most
        var cuts = new List<int>();
        for (int i = 1; i < heights.Count; i++) if (heights[i] - heights[i - 1] > 6) cuts.Add(heights[i]);
        if (cuts.Count == 0) return Furnish(floorCells, bunks);
        var plan = new Furnishing();
        int left = bunks;
        for (int level = 0; level <= cuts.Count && left > 0; level++)
        {
            int lo = level == 0 ? int.MinValue : cuts[level - 1], hi = level == cuts.Count ? int.MaxValue : cuts[level];
            var cells = floorCells.FindAll(p => { int y = WaterPlanner.Unpack(p).y; return y >= lo && y < hi; });
            int want = Math.Min(left, (int)(cells.Count / (VoxelWorld.PerMetre * VoxelWorld.PerMetre) / FloorPerBunk));
            if (want <= 0) continue;
            var part = Furnish(cells, want);
            int from = plan.Voxels.Count;
            for (int i = 0; i < part.Voxels.Count; i++) plan.Voxels.Add(part.Voxels.X(i), part.Voxels.Y(i), part.Voxels.Z(i), part.Voxels.M(i));
            foreach (var (f, c) in part.Bunks) plan.Bunks.Add((f + from, c));
            left -= part.Bunks.Count;
        }
        return plan;
    }

    /// <summary>
    /// The furnishing of a room: bunks set head to a wall along it, side by side with room to pass between, and a table
    /// with a bench on either side where floor is left in the middle. A floor of trodden earth is not flat: each piece
    /// stands at the highest point under it, its legs let down to the floor under each. Laid out the same way every time
    /// for the same room.
    /// </summary>
    Furnishing Furnish(List<int> floorCells, int bunks)
    {
        var plan = new Furnishing();
        // no furniture in a door's way: the cells its leaves fill and sweep through are kept clear
        var clear = new HashSet<int>();
        foreach (var g in Gates) clear.UnionWith(g.Cells);
        // and the way through a door kept clear a man's width either side of the wall: a house's door, hung outward,
        // sweeps nothing in the room
        foreach (var g in Gates)
            for (int k = -Shapes.HouseWall - 6; k <= Shapes.HouseWall + 6; k += 2)
                for (int u = g.U0; u < g.U0 + Gateway.Width; u += 2)
                {
                    int a = g.P + k;
                    clear.Add(Nav.CellOf(g.AlongX ? u : a, g.Y, g.AlongX ? a : u));
                }
        // nor at a ladder's foot: the cells it stands in are kept clear
        foreach (int p in floorCells)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            for (int k = 0; k < 4; k++) if (Mat.IsLadder[World.Get(x, y + k, z)]) { clear.Add(Nav.CellOf(x, y, z)); break; }
        }
        // the floor by column: where each column's floor is
        var floor = new Dictionary<int, int>();
        int Col(int x, int z) => z * 4096 + x;
        foreach (int p in floorCells)
        {
            var (x, y, z) = WaterPlanner.Unpack(p);
            if (clear.Contains(Nav.CellOf(x, y, z))) continue;
            if (!floor.TryGetValue(Col(x, z), out int was) || y < was) floor[Col(x, z)] = y;
        }
        // the hollows between the stones of a rough wall face are no floor: whatever stands well over the room's floor goes
        if (floor.Count > 0)
        {
            var heights = new List<int>(floor.Values);
            heights.Sort();
            int usual = heights[heights.Count / 2];
            var high = new List<int>();
            foreach (var (key, y) in floor) if (y > usual + 2) high.Add(key);
            foreach (int key in high) floor.Remove(key);
        }
        var taken = new HashSet<int>();
        // a footprint of free floor no rougher than two voxels, and margin free of other pieces about it; where it stands
        int Fits(int x0, int z0, int w, int d, int margin)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            for (int z = z0 - margin; z < z0 + d + margin; z++)
                for (int x = x0 - margin; x < x0 + w + margin; x++)
                {
                    if (taken.Contains(Col(x, z))) return -1;
                    if (x < x0 || x >= x0 + w || z < z0 || z >= z0 + d) continue;
                    if (!floor.TryGetValue(Col(x, z), out int y)) return -1;
                    lo = Math.Min(lo, y); hi = Math.Max(hi, y);
                }
            return hi - lo <= 2 ? hi : -1;
        }
        void Take(int x0, int z0, int w, int d, int margin)
        {
            for (int z = z0 - margin; z < z0 + d + margin; z++)
                for (int x = x0 - margin; x < x0 + w + margin; x++) taken.Add(Col(x, z));
        }
        int FloorAt(int x, int z, int at) => floor.TryGetValue(Col(x, z), out int y) ? y : at;
        // a wall at the head: solid a little way up, at the face or a stone's depth behind it (the joints are raked)
        bool Solid(int x, int y, int z) => Walled(World.Get(x, y + 2, z)) && Walled(World.Get(x, y + 4, z));
        bool Wall(int x, int y, int z, int dx, int dz) => Solid(x, y, z) || Solid(x + dx, y, z + dz);
        var cols = new List<int>(floor.Keys);
        cols.Sort();
        // the bunks: a metre wide and two long, the head to a wall
        const int BW = 8, BL = 16;
        foreach (int key in cols)
        {
            if (plan.Bunks.Count >= bunks) break;
            int x = key % 4096, z = key / 4096, fy = floor[key];
            // the four ways a bunk may lie with its head against the wall at this corner of floor
            foreach (int way in new[] { 0, 1, 2, 3 })
            {
                bool alongZ = way < 2;
                int w = alongZ ? BW : BL, d = alongZ ? BL : BW;
                bool head = way switch
                {
                    0 => Wall(x, fy, z - 1, 0, -1) && Wall(x + BW - 1, fy, z - 1, 0, -1),
                    1 => Wall(x, fy, z + BL, 0, 1) && Wall(x + BW - 1, fy, z + BL, 0, 1),
                    2 => Wall(x - 1, fy, z, -1, 0) && Wall(x - 1, fy, z + BW - 1, -1, 0),
                    _ => Wall(x + BL, fy, z, 1, 0) && Wall(x + BL, fy, z + BW - 1, 1, 0),
                };
                if (!head) continue;
                int y = Fits(x, z, w, d, 0);
                if (y < 0 || Fits(x, z, w, d, 1) < 0) continue;
                int from = plan.Voxels.Count;
                Bunk(plan.Voxels, x, y, z, way, FloorAt);
                plan.Bunks.Add((from, plan.Voxels.Count - from));
                Take(x, z, w, d, 2);
                break;
            }
        }
        // the table, as near the middle as it fits, with a bench along each long side
        if (cols.Count > 0)
        {
            double cx = 0, cz = 0;
            foreach (int key in cols) { cx += key % 4096; cz += key / 4096; }
            cx /= cols.Count; cz /= cols.Count;
            var byMiddle = new List<int>(cols);
            byMiddle.Sort((a, b) =>
            {
                double da = (a % 4096 - cx) * (a % 4096 - cx) + (a / 4096 - cz) * (a / 4096 - cz), db = (b % 4096 - cx) * (b % 4096 - cx) + (b / 4096 - cz) * (b / 4096 - cz);
                int c = da.CompareTo(db);
                return c != 0 ? c : a.CompareTo(b);
            });
            const int TW = 7, TL = 12;
            foreach (int key in byMiddle)
            {
                int x = key % 4096, z = key / 4096;
                // the benches stand two voxels off the table's long sides: the whole set is 15 wide
                int y = Fits(x - 4, z, TW + 8, TL, 1);
                if (y < 0) continue;
                Board(plan.Voxels, x, y, z, TW, TL, 5, Mat.Table, FloorAt);
                Board(plan.Voxels, x - 4, y, z + 1, 2, TL - 2, 2, Mat.Bench, FloorAt);
                Board(plan.Voxels, x + TW + 2, y, z + 1, 2, TL - 2, 2, Mat.Bench, FloorAt);
                Take(x - 4, z, TW + 8, TL, 1);
                break;
            }
        }
        return plan;
    }

    /// <summary>
    /// A bunk standing at y over (x, z): four posts let down to the floor, a frame of boards with the bedding laid in it,
    /// a bolster and a headboard.
    /// </summary>
    static void Bunk(VoxelList L, int x, int y, int z, int way, Func<int, int, int, int> floorAt)
    {
        const int BW = 8, BL = 16;
        // local u across the bunk, v along it from the head; the head lies against the wall
        (int, int) At(int u, int v) => way switch
        {
            0 => (x + u, z + v),
            1 => (x + u, z + BL - 1 - v),
            2 => (x + v, z + u),
            _ => (x + BL - 1 - v, z + u),
        };
        for (int v = 0; v < BL; v++)
            for (int u = 0; u < BW; u++)
            {
                var (px, pz) = At(u, v);
                bool edge = u == 0 || u == BW - 1 || v == 0 || v == BL - 1, corner = (u == 0 || u == BW - 1) && (v == 0 || v == BL - 1);
                if (corner) for (int k = floorAt(px, pz, y); k < y + 2; k++) L.Add(px, k, pz, Mat.Bunk);
                L.Add(px, y + 2, pz, Mat.Bunk);
                L.Add(px, y + 3, pz, edge ? Mat.Bunk : Mat.Bedding);
                // the bolster at the head, and the headboard standing up behind it
                if (!edge && v <= 3) L.Add(px, y + 4, pz, Mat.Bedding);
                if (v == 0) for (int k = 4; k <= 6; k++) L.Add(px, y + k, pz, Mat.Bunk);
            }
    }

    /// <summary>A board on legs: a top <paramref name="h"/> voxels over y on a w×d footprint, a leg let down to the floor under each corner.</summary>
    static void Board(VoxelList L, int x, int y, int z, int w, int d, int h, byte m, Func<int, int, int, int> floorAt)
    {
        for (int dz = 0; dz < d; dz++)
            for (int dx = 0; dx < w; dx++)
            {
                bool corner = (dx == 0 || dx == w - 1) && (dz == 0 || dz == d - 1);
                if (corner) for (int k = floorAt(x + dx, z + dz, y); k < y + h; k++) L.Add(x + dx, k, z + dz, m);
                L.Add(x + dx, y + h, z + dz, m);
            }
    }
}
