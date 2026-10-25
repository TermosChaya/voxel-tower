using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Houses side by side, and their doors as the player wants them (a player, 25.10.2026: «поставила два домика рядом —
/// двери оказались недоступны, установила новую дверь — её не используют, старую не сносят»; the author: «домики должны
/// быть кастомизируемые и предусматривать строительство вплотную»).
///
/// A house drawn up against another — its edge within half a metre of the other's wall, or into it — is moved onto that
/// wall: the two share it, one wall serving both, and neither puts its eaves out over the other's room (what of the
/// other's eaves reaches over the new room is taken down). A house's door goes only where it opens onto free ground:
/// not into the wall shared, nor a house or a wall standing or marked right before it (<see cref="Shapes.DoorFree"/>).
/// Built later in front of, a door still waiting for its wall goes to a free side of its house before it is cut.
///
/// The doors can be changed: the gate tool on a house's wall puts a door there (a house's door, its leaves outside on a
/// doorstep, cut once the wall stands), and on a gate walls it up again of the wall's own material. A dwelling whose
/// every door opens into a wall is not closed: it is told so, with what to do.
/// </summary>
public sealed partial class Sim
{
    /// <summary>How close to another house's wall a house drawn is taken onto it, voxels of gap.</summary>
    const int ShareGap = 4;

    /// <summary>
    /// What another building holds, by voxel, for a house planned on a rectangle: the rectangles of the dwellings and the
    /// town's buildings other than it, and whatever is marked to be built (a house drawn and not laid yet).
    /// </summary>
    public Func<int, int, int, bool> TakenBesides(int x0, int z0, int x1, int z1)
    {
        var rects = new List<(int x0, int z0, int x1, int z1)>();
        foreach (var z in Zones)
            if (z.Kind == Zone.Dwelling && !(z.X0 == x0 && z.Z0 == z0 && z.X1 == x1 && z.Z1 == z1)) rects.Add((z.X0, z.Z0, z.X1, z.Z1));
        return (x, y, z) =>
        {
            foreach (var r in rects) if (x >= r.x0 && x <= r.x1 && z >= r.z0 && z <= r.z1) return true;
            byte o = Orders.At(x, y, z);
            return o != 0 && !Orders.IsRemove(o) && !Mat.Passable[o];
        };
    }

    /// <summary>The rectangle of a house's room: inside its walls.</summary>
    static (int x0, int z0, int x1, int z1) RoomOf(int x0, int z0, int x1, int z1) =>
        (x0 + Shapes.HouseWall, z0 + Shapes.HouseWall, x1 - Shapes.HouseWall, z1 - Shapes.HouseWall);

    static bool Overlap(int a0, int a1, int b0, int b1) => a0 <= b1 && b0 <= a1;

    /// <summary>
    /// A house's rectangle fitted among the houses already there: a dwelling whose room it would cover (<paramref name="over"/>:
    /// a storey, or refused), else moved onto the wall of a house it is drawn up against, so that the two share it. The
    /// sides it shares (a bit for each side, as <see cref="Shapes.SideNormal"/>).
    /// </summary>
    public void FitHouse(ref int x0, ref int z0, ref int x1, ref int z1, out int shared, out Zone over)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        const int t = Shapes.HouseWall;
        shared = 0; over = null;
        bool movedX = false, movedZ = false;
        foreach (var z in Zones)
        {
            if (z.Kind != Zone.Dwelling) continue;
            var (rx0, rz0, rx1, rz1) = RoomOf(z.X0, z.Z0, z.X1, z.Z1);
            var (nx0, nz0, nx1, nz1) = RoomOf(x0, z0, x1, z1);
            // over the other's room: a storey, or nothing
            if (Overlap(nx0, nx1, rx0, rx1) && Overlap(nz0, nz1, rz0, rz1)) { over = z; return; }
            // side by side along x: the walls facing each other across z overlap enough to share
            if (!movedX && Math.Min(z1, z.Z1) - Math.Max(z0, z.Z0) >= 2 * t + 8)
            {
                if (x0 >= z.X1 - t + 1 - t && x0 <= z.X1 + ShareGap) { int d = z.X1 - t + 1 - x0; x0 += d; x1 += d; movedX = true; }
                else if (x1 <= z.X0 + t - 1 + t && x1 >= z.X0 - ShareGap) { int d = z.X0 + t - 1 - x1; x0 += d; x1 += d; movedX = true; }
            }
            if (!movedZ && Math.Min(x1, z.X1) - Math.Max(x0, z.X0) >= 2 * t + 8)
            {
                if (z0 >= z.Z1 - t + 1 - t && z0 <= z.Z1 + ShareGap) { int d = z.Z1 - t + 1 - z0; z0 += d; z1 += d; movedZ = true; }
                else if (z1 <= z.Z0 + t - 1 + t && z1 >= z.Z0 - ShareGap) { int d = z.Z0 + t - 1 - z1; z0 += d; z1 += d; movedZ = true; }
            }
        }
        // moved: over a room after all, or sharing what
        foreach (var z in Zones)
        {
            if (z.Kind != Zone.Dwelling) continue;
            var (rx0, rz0, rx1, rz1) = RoomOf(z.X0, z.Z0, z.X1, z.Z1);
            var (nx0, nz0, nx1, nz1) = RoomOf(x0, z0, x1, z1);
            if (Overlap(nx0, nx1, rx0, rx1) && Overlap(nz0, nz1, rz0, rz1)) { over = z; return; }
            if (Overlap(z0, z1, z.Z0, z.Z1))
            {
                if (x0 == z.X1 - t + 1) shared |= 1 << 2;
                if (x1 == z.X0 + t - 1) shared |= 1 << 3;
            }
            if (Overlap(x0, x1, z.X0, z.X1))
            {
                if (z0 == z.Z1 - t + 1) shared |= 1 << 0;
                if (z1 == z.Z0 + t - 1) shared |= 1 << 1;
            }
        }
    }

    /// <summary>
    /// A house planned on a rectangle as the player draws it (or a town's building on its plan): fitted among the houses
    /// there (<see cref="FitHouse"/>), its door on <paramref name="doorSide"/> or the first side that opens onto free
    /// ground. The rectangle it takes and the sides it shares come back; null and why if not here (over a room: why is
    /// null and <paramref name="over"/> the dwelling under it).
    /// </summary>
    public VoxelList PlanHouseAt(int ax, int az, int bx, int bz, in Stroke st, int doorSide, out Shapes.HouseDoor door, out string why, out VoxelList arched,
        out (int x0, int z0, int x1, int z1) rect, out int shared, out Zone over) =>
        PlanHouseAt(ax, az, bx, bz, st, doorSide, out door, out why, out arched, out rect, out shared, out over, out _);

    /// <summary>
    /// As above, with the doors of the houses beside it that it would wall in (<paramref name="moves"/>: those in a wall
    /// it comes to share, each with where it goes instead). Before a door of a house beside it, not sharing that wall, it
    /// may not stand at all; nor share a wall whose door has nowhere else to go.
    /// </summary>
    public VoxelList PlanHouseAt(int ax, int az, int bx, int bz, in Stroke st, int doorSide, out Shapes.HouseDoor door, out string why, out VoxelList arched,
        out (int x0, int z0, int x1, int z1) rect, out int shared, out Zone over, out List<DoorMove> moves, bool lax = false)
    {
        int x0 = ax, z0 = az, x1 = bx, z1 = bz;
        FitHouse(ref x0, ref z0, ref x1, ref z1, out shared, out over);
        rect = (x0, z0, x1, z1);
        door = default; arched = null; why = null; moves = null;
        if (over != null) return null;
        if (!World.OnLand(x0, z0) || !World.OnLand(x1, z0) || !World.OnLand(x0, z1) || !World.OnLand(x1, z1)) { why = "здесь край долины"; return null; }
        why = DoorsInTheWay(rect, out moves);
        if (why != null) return null;
        return Shapes.House(World, Site, x0, z0, x1, z1, st, 0, doorSide, TakenBesides(x0, z0, x1, z1), shared, out door, out why, out arched, lax);
    }

    /// <summary>A door of a house beside a new one that would be walled in by it: the gate there (or the door still waiting), and where it goes.</summary>
    public struct DoorMove { public Zone House; public Gateway Gate; public int Pending; public Shapes.HouseDoor To; }

    /// <summary>
    /// Where a door of a house is: the side of the house it is in, the middle of it along that wall, and its threshold.
    /// </summary>
    static void Face(Gateway g, Zone house, out int side, out int along, out int threshold)
    {
        var (mx, _, mz) = g.Middle;
        int cx = (house.X0 + house.X1) / 2, cz = (house.Z0 + house.Z1) / 2;
        side = g.AlongX ? (mz < cz ? 0 : 1) : (mx < cx ? 2 : 3);
        along = (int)(g.AlongX ? mx : mz);
        threshold = g.Y;
    }

    /// <summary>
    /// Whether a rectangle covers the ground a metre out before a door in a side of a house (a little wider than the door).
    /// </summary>
    static bool Before(Zone house, int side, int along, (int x0, int z0, int x1, int z1) r)
    {
        var (nx, nz) = Shapes.SideNormal(side);
        int face = side switch { 0 => house.Z0, 1 => house.Z1, 2 => house.X0, _ => house.X1 };
        int o0 = face + (nx + nz), o1 = face + 8 * (nx + nz);
        if (o1 < o0) (o0, o1) = (o1, o0);
        int a0 = along - Gateway.Half - 2, a1 = along + Gateway.Half + 2;
        return side < 2 ? Overlap(a0, a1, r.x0, r.x1) && Overlap(o0, o1, r.z0, r.z1) : Overlap(a0, a1, r.z0, r.z1) && Overlap(o0, o1, r.x0, r.x1);
    }

    /// <summary>
    /// The doors of the houses about a new house on a rectangle would stand before: in a wall it shares, each moved to a
    /// free side of its own house (or why not); not sharing it, why not.
    /// </summary>
    string DoorsInTheWay((int x0, int z0, int x1, int z1) r, out List<DoorMove> moves)
    {
        moves = new List<DoorMove>();
        const int t = Shapes.HouseWall;
        foreach (var z in Zones)
        {
            if (z.Kind != Zone.Dwelling || z.Depth <= 1 || z.X0 == r.x0 && z.Z0 == r.z0 && z.X1 == r.x1 && z.Z1 == r.z1) continue;
            // the sides of it the new house shares
            int shares = 0;
            if (Overlap(r.z0, r.z1, z.Z0, z.Z1)) { if (r.x0 == z.X1 - t + 1) shares |= 8; if (r.x1 == z.X0 + t - 1) shares |= 4; }
            if (Overlap(r.x0, r.x1, z.X0, z.X1)) { if (r.z0 == z.Z1 - t + 1) shares |= 2; if (r.z1 == z.Z0 + t - 1) shares |= 1; }
            var found = new List<(Gateway g, int pending, int side, int along, int threshold)>();
            foreach (var g in DoorsOf(z)) { Face(g, z, out int s, out int a, out int th); found.Add((g, -1, s, a, th)); }
            for (int i = 0; i < HouseDoors.Count; i++)
            {
                var d = HouseDoors[i].door;
                if (d.Top > 0 && d.X >= z.X0 && d.X <= z.X1 && d.Z >= z.Z0 && d.Z <= z.Z1) found.Add((null, i, d.Side, d.Side < 2 ? d.X : d.Z, d.Y - 8));
            }
            foreach (var (g, pending, side, along, threshold) in found)
            {
                if (!Before(z, side, along, r)) continue;
                if ((shares & 1 << side) == 0) return $"перед дверью дома «{z.Name}»: отступите на метр или поставьте вплотную, тогда стена станет общей";
                // a wall shared: the door goes to another side of its house, where the ground is free (the new house counted)
                var taken = TakenBesides(z.X0, z.Z0, z.X1, z.Z1);
                bool Taken(int x, int y, int zz) => taken(x, y, zz) || x >= r.x0 && x <= r.x1 && zz >= r.z0 && zz <= r.z1;
                int sharedNow = SharedSidesOf(z) | shares;
                Shapes.HouseDoor? to = null;
                // the opposite wall first, then the ends, the one toward the citadel first
                int towardX = Site.KeepX > (z.X0 + z.X1) / 2 ? 3 : 2, towardZ = Site.KeepZ > (z.Z0 + z.Z1) / 2 ? 1 : 0;
                foreach (int s in side < 2 ? new[] { side ^ 1, towardX, 5 - towardX } : new[] { side ^ 1, towardZ, 1 - towardZ })
                {
                    if ((sharedNow & 1 << s) != 0) continue;
                    var e = Shapes.DoorOn(s, z.X0, z.Z0, z.X1, z.Z1, threshold - 1, z.Depth);
                    if (Shapes.DoorFree(World, Site, e, Taken, BuildingOn(z) != null)) { to = e; break; }
                }
                if (to == null) return $"в общей стене дверь дома «{z.Name}», и ей некуда переехать";
                moves.Add(new DoorMove { House = z, Gate = g, Pending = pending, To = to.Value });
            }
        }
        return null;
    }

    /// <summary>
    /// A house planned put up: marked for the workers, its roof to stand as an arch, its door to wait for its wall, its
    /// floor a dwelling; and what of a neighbour's eaves reaches out over its room taken down.
    /// </summary>
    public int PutHouse(VoxelList plan, VoxelList arched, Shapes.HouseDoor door, (int x0, int z0, int x1, int z1) rect, List<DoorMove> moves = null)
    {
        int n = DesignateBuild(plan);
        if (n == 0) return 0;
        MoveDoors(moves);
        Arch(arched);
        PlanHouse(door, rect.x0, rect.z0, rect.x1, rect.z1);
        var eaves = EavesOver(rect.x0, rect.z0, rect.x1, rect.z1, door.Top);
        if (eaves.Count > 0) DesignateRemove(eaves);
        return n;
    }

    /// <summary>
    /// The doors a new house walls in, moved: a gate in the shared wall walled up and a door waiting on the free side
    /// instead; one still waiting for its wall, sent there at once.
    /// </summary>
    public void MoveDoors(List<DoorMove> moves)
    {
        if (moves == null) return;
        // the waiting ones last first, so the indices hold
        moves.Sort((a, b) => b.Pending.CompareTo(a.Pending));
        foreach (var m in moves)
        {
            if (m.Gate != null) { WallUp(m.Gate); HouseDoors.Add((m.To, Time)); }
            else if (m.Pending >= 0 && m.Pending < HouseDoors.Count) HouseDoors[m.Pending] = (m.To, HouseDoors[m.Pending].since);
            Say($"{m.House.Name}: стена теперь общая с новым домом, дверь переедет {Shapes.SideWords[m.To.Side]}.", 1, m.To.X, m.To.Z);
        }
    }

    /// <summary>What of the roofs about reaches out over the room of a house put up on a rectangle, to take down.</summary>
    public VoxelList EavesOverHouse((int x0, int z0, int x1, int z1) rect)
    {
        int ground = 0;
        for (int z = rect.z0; z <= rect.z1; z += 4) for (int x = rect.x0; x <= rect.x1; x += 4) ground = Math.Max(ground, World.SurfAt(x, z));
        return EavesOver(rect.x0, rect.z0, rect.x1, rect.z1, ground + 1 + Shapes.HouseHeight);
    }

    /// <summary>
    /// What of the roofs about reaches out over the room of a house on this rectangle: the slopes' voxels standing as an
    /// arch over its room's columns, under the top of its walls and a little over it (a neighbour's eaves, laid before
    /// this house was drawn up against it).
    /// </summary>
    VoxelList EavesOver(int x0, int z0, int x1, int z1, int top)
    {
        var L = new VoxelList();
        var (rx0, rz0, rx1, rz1) = RoomOf(x0, z0, x1, z1);
        for (int z = rz0; z <= rz1; z++)
            for (int x = rx0; x <= rx1; x++)
            {
                // only by the walls: the eaves reach in two voxels at most
                if (x - rx0 > Shapes.Eaves && rx1 - x > Shapes.Eaves && z - rz0 > Shapes.Eaves && rz1 - z > Shapes.Eaves) continue;
                for (int y = top - 4; y <= top + 2; y++)
                    if (World.InBounds(x, y, z) && Arched.Get(x, y, z) != 0 && Mat.IsPlayer[World.Get(x, y, z)]) L.Add(x, y, z, Mat.Air);
            }
        return L;
    }

    // ------------------------------------------------------------ a house's doors

    /// <summary>
    /// The house whose wall a face struck belongs to, which side of it, and where along that wall the door's middle would
    /// be (kept clear of the corners). False when the face is no house's wall, or the roof, or its top.
    /// </summary>
    public bool HouseWallAt(VoxelHit hit, out Zone house, out int side, out int along)
    {
        house = null; side = -1; along = 0;
        if (hit.NY != 0) return false;
        const int t = Shapes.HouseWall;
        foreach (var z in Zones)
        {
            if (z.Kind != Zone.Dwelling || z.Depth <= 1) continue;
            // in its walls' band, under the top of them
            if (hit.X < z.X0 || hit.X > z.X1 || hit.Z < z.Z0 || hit.Z > z.Z1 || hit.Y >= z.Depth) continue;
            // the face struck, the wall's outer face or its inner one from inside, tells the side
            int s = hit.NX != 0 ? (hit.X - z.X0 < t ? 2 : z.X1 - hit.X < t ? 3 : -1) : (hit.Z - z.Z0 < t ? 0 : z.Z1 - hit.Z < t ? 1 : -1);
            if (s < 0) continue;
            house = z; side = s;
            int lo = (s < 2 ? z.X0 : z.Z0) + t + Gateway.Half, hi = (s < 2 ? z.X1 : z.Z1) - t - Gateway.Half;
            along = Math.Clamp(s < 2 ? hit.X : hit.Z, lo, Math.Max(lo, hi));
            return true;
        }
        return false;
    }

    /// <summary>The top of a house's floor where a door would go in a side of it: the floor just inside the wall there.</summary>
    int FloorBy(Zone z, int side, int along)
    {
        const int t = Shapes.HouseWall;
        var (nx, nz) = Shapes.SideNormal(side);
        int x = side < 2 ? along : side == 2 ? z.X0 + t : z.X1 - t, zz = side < 2 ? (side == 0 ? z.Z0 + t : z.Z1 - t) : along;
        // down from the walls' top of the first storey (a floor laid over it is a storey's)
        int top = World.GroundTop(x, zz) + Shapes.HouseHeight;
        for (int y = Math.Min(top, World.SY - 2); y > 1; y--)
            if (!Brushes.Free(World.Get(x, y - 1, zz)) && !Mat.IsFurniture[World.Get(x, y - 1, zz)] && Brushes.Free(World.Get(x, y, zz)) && Brushes.Free(World.Get(x, y + 8, zz))) return y;
        return World.GroundTop(x, zz) + 1;
    }

    /// <summary>
    /// A door put in a side of a house: where it would be, or why not — the side opens onto a wall or another house, or
    /// a door is there already.
    /// </summary>
    public string HouseDoorAt(Zone z, int side, int along, out Shapes.HouseDoor door)
    {
        int threshold = FloorBy(z, side, along);
        door = Shapes.DoorOn(side, z.X0, z.Z0, z.X1, z.Z1, threshold - 1, z.Depth, along);
        foreach (var g in DoorsOf(z))
        {
            var (mx, _, mz) = g.Middle;
            float across = side < 2 ? mz - door.Z : mx - door.X, alongOff = side < 2 ? mx - door.X : mz - door.Z;
            if (Math.Abs(across) <= Shapes.HouseWall + 2 && Math.Abs(alongOff) < Gateway.Width + 4) return "здесь уже есть дверь";
        }
        foreach (var (d, _) in HouseDoors)
            if (d.Side == side && Math.Abs((side < 2 ? d.X : d.Z) - along) < Gateway.Width + 4 && (side < 2 ? d.Z == door.Z : d.X == door.X)) return "здесь дверь уже размечена";
        if (!Shapes.DoorFree(World, Site, door, TakenBesides(z.X0, z.Z0, z.X1, z.Z1))) return "снаружи стена, дом или обрыв: двери некуда открыться";
        return null;
    }

    /// <summary>A door for a house where the player put it: it waits for the wall and is cut as a house's door is. Why not, or null.</summary>
    public string AddHouseDoor(Zone z, int side, int along)
    {
        string no = HouseDoorAt(z, side, along, out var door);
        if (no != null) return no;
        HouseDoors.Add((door, Time));
        DoorsAdded++;
        return null;
    }

    /// <summary>Doors the player has put in houses, and walled up, this game (the tutorial and the tests ask).</summary>
    public int DoorsAdded, DoorsWalled;

    /// <summary>
    /// What walling a gate up takes: its gateway through the wall filled again, of what the wall about it is of — the
    /// leaves taken down, and every open voxel of the opening between the wall's two faces, read off the jambs beside it.
    /// </summary>
    public VoxelList WallUpPlan(Gateway g)
    {
        var L = new VoxelList();
        byte Get(int u, int y, int a) => g.AlongX ? World.Get(u, y, a) : World.Get(a, y, u);
        // the wall's thickness, read off the jambs: across from the hinges' plane both ways while the jambs stand
        bool Wall(int a) => !Brushes.Free(Get(g.U0 - 1, g.Y + 4, a)) && !Mat.IsLeaf(Get(g.U0 - 1, g.Y + 4, a))
            || !Brushes.Free(Get(g.U0 + Gateway.Width, g.Y + 4, a)) && !Mat.IsLeaf(Get(g.U0 + Gateway.Width, g.Y + 4, a));
        int a0 = g.P, a1 = g.P;
        for (int k = 0; k < 40 && Wall(a0 - 1); k++) a0--;
        for (int k = 0; k < 40 && Wall(a1); k++) a1++;
        a1--;
        if (a1 < a0) { a0 = g.P - 1; a1 = g.P - 1; for (int k = 0; k < 40 && Wall(a0 - 1); k++) a0--; }
        // of what: the most of what the jambs are laid of
        var count = new Dictionary<byte, int>();
        for (int a = a0; a <= a1; a++)
            for (int y = g.Y; y < g.Y + Gateway.Height; y++)
                foreach (int u in new[] { g.U0 - 1, g.U0 + Gateway.Width })
                {
                    byte m = Get(u, y, a);
                    // (the shades of laid stone are one stone)
                    if (m != Mat.Air && Mat.IsPlayer[m] && !Mat.IsLeaf(m) && !Mat.Passable[m]) { byte k = Mat.IsMasonry(m) ? Mat.Stone : m; count[k] = count.GetValueOrDefault(k) + 1; }
                }
        byte mat = Mat.Stone;
        int best = 0;
        foreach (var (m, n) in count) if (n > best) { best = n; mat = m; }
        for (int a = a0; a <= a1; a++)
            for (int u = g.U0; u < g.U0 + Gateway.Width; u++)
                for (int y = g.Y; y < g.Y + Gateway.Height; y++)
                {
                    int x = g.AlongX ? u : a, z = g.AlongX ? a : u;
                    if (World.InBounds(x, y, z) && (Brushes.Free(World.Get(x, y, z)) || Mat.IsLeaf(World.Get(x, y, z)))) L.Add(x, y, z, Shapes.Lay(mat, x, y, z));
                }
        return L;
    }

    /// <summary>
    /// A gate walled up: taken off the gates (its leaves down, nobody knocks at it any more) and its gateway marked to be
    /// laid of the wall's material. Why not, or null.
    /// </summary>
    public string WallUp(Gateway g)
    {
        if (g == null) return "здесь нет ворот";
        var plan = WallUpPlan(g);
        if (plan.Count == 0) return "здесь нечего закладывать";
        // the leaves taken down where they hang, the gate let go
        UnhangGate(g);
        Gates.Remove(g);
        if (g == FortGate) FortGate = null;
        for (int i = 0; i < plan.Count; i++)
        {
            int x = plan.X(i), y = plan.Y(i), z = plan.Z(i);
            if (Mat.IsLeaf(World.Get(x, y, z))) World.Set(x, y, z, Mat.Air);
        }
        DesignateBuild(plan);
        DoorsWalled++;
        return null;
    }

    /// <summary>
    /// Whether a gate in a house's wall opens onto somewhere: before its outer face, at most of its width, no wall or other
    /// house within a quarter of a metre at head height. Doors of what is no house (a gate in a wall) always do.
    /// </summary>
    public bool DoorLeadsOut(Gateway g, Zone house)
    {
        var (mx, _, mz) = g.Middle;
        int cx = (house.X0 + house.X1) / 2, cz = (house.Z0 + house.Z1) / 2;
        // the side it is in, by which wall band its middle is in
        int side = g.AlongX ? (mz < cz ? 0 : 1) : (mx < cx ? 2 : 3);
        var (nx, nz) = Shapes.SideNormal(side);
        int face = side switch { 0 => house.Z0, 1 => house.Z1, 2 => house.X0, _ => house.X1 };
        int blocked = 0;
        var taken = TakenBesides(house.X0, house.Z0, house.X1, house.Z1);
        for (int u = g.U0; u < g.U0 + Gateway.Width; u++)
        {
            bool wall = false;
            for (int k = 1; k <= 2 && !wall; k++)
            {
                int x = nx != 0 ? face + nx * k : u, z = nx != 0 ? u : face + nz * k;
                if (!World.InBounds(x, g.Y, z) || !World.OnLand(x, z)) { wall = true; break; }
                for (int y = g.Y + 2; y < g.Y + 16 && !wall; y++)
                {
                    byte m = World.Get(x, y, z);
                    wall = m != Mat.Air && !Mat.Passable[m] && !Mat.IsVeg[m] && !Mat.IsLeaf(m) || taken(x, y, z);
                }
            }
            if (wall) blocked++;
        }
        return blocked * 2 < Gateway.Width;
    }

    /// <summary>The gates in a house's walls: those whose middle stands in the band of its walls.</summary>
    public List<Gateway> DoorsOf(Zone house)
    {
        const int t = Shapes.HouseWall;
        var list = new List<Gateway>();
        foreach (var g in Gates)
        {
            var (mx, _, mz) = g.Middle;
            if (mx < house.X0 - 2 || mx > house.X1 + 2 || mz < house.Z0 - 2 || mz > house.Z1 + 2) continue;
            bool band = mx - house.X0 < t + 2 || house.X1 - mx < t + 2 || mz - house.Z0 < t + 2 || house.Z1 - mz < t + 2;
            if (band) list.Add(g);
        }
        return list;
    }

    /// <summary>The sides of a house with a door in them or waiting for its wall (a bit for each side).</summary>
    public int DoorSidesOf(Zone house)
    {
        int sides = 0;
        int cx = (house.X0 + house.X1) / 2, cz = (house.Z0 + house.Z1) / 2;
        foreach (var g in DoorsOf(house))
        {
            var (mx, _, mz) = g.Middle;
            sides |= 1 << (g.AlongX ? (mz < cz ? 0 : 1) : (mx < cx ? 2 : 3));
        }
        foreach (var (d, _) in HouseDoors)
            if (d.X >= house.X0 - 1 && d.X <= house.X1 + 1 && d.Z >= house.Z0 - 1 && d.Z <= house.Z1 + 1) sides |= 1 << d.Side;
        return sides;
    }

    /// <summary>The sides a house shares with the houses beside it (a bit for each side).</summary>
    public int SharedSidesOf(Zone house)
    {
        int x0 = house.X0, z0 = house.Z0, x1 = house.X1, z1 = house.Z1, shared = 0;
        const int t = Shapes.HouseWall;
        foreach (var z in Zones)
        {
            if (z == house || z.Kind != Zone.Dwelling) continue;
            if (Overlap(z0, z1, z.Z0, z.Z1)) { if (x0 == z.X1 - t + 1) shared |= 4; if (x1 == z.X0 + t - 1) shared |= 8; }
            if (Overlap(x0, x1, z.X0, z.X1)) { if (z0 == z.Z1 - t + 1) shared |= 1; if (z1 == z.Z0 + t - 1) shared |= 2; }
        }
        return shared;
    }

    /// <summary>
    /// A house's door waiting for its wall, looked at before it is cut: if what stands or is marked before it now (a house
    /// put up against it since) leaves it nowhere to open, it goes to a free side of its house; with none, it waits, and
    /// the player is told once. The door to cut, or null to wait.
    /// </summary>
    Shapes.HouseDoor? DoorStillFree(Shapes.HouseDoor d)
    {
        Zone house = null;
        foreach (var z in Zones)
            if (z.Kind == Zone.Dwelling && d.X >= z.X0 && d.X <= z.X1 && d.Z >= z.Z0 && d.Z <= z.Z1) { house = z; break; }
        // a gatehouse's or a barn's door: as it was planned
        if (house == null) return d;
        var taken = TakenBesides(house.X0, house.Z0, house.X1, house.Z1);
        // (only what is put up before it moves it: the lie of the land was looked at when it was planned, and the works
        // about a house going up — a heap, a hole dug — come and go)
        if (Shapes.DoorFree(World, Site, d, taken, true)) return d;
        bool lax = BuildingOn(house) != null;
        int shared = SharedSidesOf(house);
        for (int side = 0; side < 4; side++)
        {
            if (side == d.Side || (shared & 1 << side) != 0) continue;
            var e = Shapes.DoorOn(side, house.X0, house.Z0, house.X1, house.Z1, d.Y - 9, d.Top);
            if (!Shapes.DoorFree(World, Site, e, taken, lax)) continue;
            Say($"Дверь дома перенесена {Shapes.SideWords[side]}: перед прежней теперь стена.", 1, e.X, e.Z);
            return e;
        }
        if (_doorBlockedSaid.Add(house.Id))
            Say("Дому некуда открыть дверь: со всех сторон стены. Отступите соседний дом или поставьте дверь воротами ({ToolGate}) там, где снаружи свободно.", 2, d.X, d.Z);
        return null;
    }
    readonly HashSet<int> _doorBlockedSaid = new();
}
