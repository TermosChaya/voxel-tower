using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>A thing set out in the settlement with the «Обустройство» tool: what, where its spot is, how it is turned.</summary>
public sealed class DecorSite
{
    public DecorKind Kind;
    public int X, Y, Z, Rot;
    /// <summary>It stands (nine tenths of it and more): its fire burns, its seats are sat on.</summary>
    public bool Ready;
    public Fixture Light;
    /// <summary>Its voxels as they were set out.</summary>
    public VoxelList Voxels;
}

/// <summary>A place to sit: on a bench, its seat's height over the floor, the way one faces, and who sits there.</summary>
public sealed class Seat
{
    public DecorSite Of;
    public float X, Y, Z, Facing, High;
    public Unit By;
}

/// <summary>
/// The settlement's furnishing (Yard): each thing set out is marked for the workers to put together from the stores and
/// remembered; once it stands its fire is lit — a campfire, a torch on a pole, a brazier light the night round them and
/// people warm themselves at the campfire and the brazier — and its seats are taken by people with nothing to do. Taken
/// down or broken (under half of it left and nothing of it still marked), it is forgotten. Kept in the save.
/// </summary>
public sealed partial class Sim
{
    public readonly List<DecorSite> Yards = new();
    public readonly List<Seat> Seats = new();
    double _yardDue;
    /// <summary>The columns our furnishing stands on: the buildings' own dressing (Sim.Decor.cs) keeps off them.</summary>
    readonly HashSet<int> _yardColumns = new();

    bool InYard(int x, int z) => _yardColumns.Contains(z * World.SX + x);

    void YardColumns()
    {
        _yardColumns.Clear();
        foreach (var s in Yards)
            for (int j = 0; j < s.Voxels.Count; j++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++) _yardColumns.Add((s.Voxels.Z(j) + dz) * World.SX + s.Voxels.X(j) + dx);
    }

    /// <summary>
    /// Sets a thing out at a spot, turned so many quarter turns: marked for the workers. Returns null, or why it cannot
    /// stand there.
    /// </summary>
    public string PlaceDecor(DecorKind kind, int x, int z, int rot)
    {
        var plan = Yard.Plan(World, Site, kind, x, z, rot, out string why);
        if (plan == null) return why;
        var site = new DecorSite { Kind = kind, X = x, Y = World.SurfAt(x, z), Z = z, Rot = rot & 3, Voxels = plan };
        Yards.Add(site);
        YardColumns();
        DesignateBuild(plan);
        if (InstantLabour) TickYard(1, now: true);
        return null;
    }

    /// <summary>Once a second: what has come to stand is lit and sat on; what has fallen or been taken down is let go.</summary>
    void TickYard(float dt, bool now = false)
    {
        if (Yards.Count == 0 || !now && Time < _yardDue) return;
        _yardDue = Time + 1;
        bool seatsChanged = false;
        for (int i = Yards.Count - 1; i >= 0; i--)
        {
            var s = Yards[i];
            s.Voxels ??= Replan(s);
            int stand = 0, marked = 0;
            for (int j = 0; j < s.Voxels.Count; j++)
            {
                int x = s.Voxels.X(j), y = s.Voxels.Y(j), z = s.Voxels.Z(j);
                byte m = World.Get(x, y, z);
                if (m == s.Voxels.M(j) || m == Mat.Sconce && s.Voxels.M(j) == Mat.Flame) stand++;
                else if (Orders.At(x, y, z) != 0) marked++;
            }
            bool ready = stand >= s.Voxels.Count * .9f;
            if (ready != s.Ready)
            {
                s.Ready = ready;
                seatsChanged = true;
                if (!ready) Douse(s);
            }
            // lit once it stands, and again if its fire was taken down and its flames are back
            if (ready && (s.Light == null || !Fixtures.Contains(s.Light))) { s.Light = null; Kindle(s); }
            if (!ready && stand < s.Voxels.Count / 2 && marked == 0)
            {
                Douse(s);
                Yards.RemoveAt(i);
                seatsChanged = true;
                YardColumns();
            }
        }
        if (seatsChanged) SeatsAgain();
    }

    /// <summary>The voxels a thing was set out with, laid out again where it stands (a save read back).</summary>
    VoxelList Replan(DecorSite s)
    {
        var L = new VoxelList();
        foreach (var (dx, dy, dz, m) in Yard.Shape(s.Kind))
        {
            var (tx, tz) = Yard.Turn(dx, dz, s.Rot);
            L.Add(s.X + tx, s.Y + dy, s.Z + tz, m);
        }
        return L;
    }

    /// <summary>A thing come to stand: its fire lights the night round it.</summary>
    void Kindle(DecorSite s)
    {
        if (s.Light != null || Yard.LightAt(s.Kind) is not { } at) return;
        var (lx, lz) = Yard.Turn(at.dx - .5f, at.dz - .5f, s.Rot);
        var f = new Fixture
        {
            Kind = s.Kind == DecorKind.Campfire ? FixtureKind.Campfire : FixtureKind.Brazier,
            LX = s.X + lx + .5f, LY = s.Y + at.dy, LZ = s.Z + lz + .5f,
        };
        // it stands while its flames do, and while the embers under them are ours and stand
        bool anchored = false;
        for (int j = 0; j < s.Voxels.Count; j++)
        {
            int x = s.Voxels.X(j), y = s.Voxels.Y(j), z = s.Voxels.Z(j);
            byte m = s.Voxels.M(j);
            if (m == Mat.Flame && World.Get(x, y, z) == Mat.Flame) f.Voxels.Add((x, y, z, m));
            else if (!anchored && m == Mat.Ember && World.Get(x, y, z) == m) { f.Anchor = WaterPlanner.Pack(x, y, z); anchored = true; }
        }
        if (!anchored || f.Voxels.Count == 0) return;
        s.Light = f;
        Fixtures.Add(f);
        FixtureAdded?.Invoke(f);
    }

    void Douse(DecorSite s)
    {
        if (s.Light == null) return;
        int i = Fixtures.IndexOf(s.Light);
        if (i >= 0) { Fixtures.RemoveAt(i); FixtureRemoved?.Invoke(s.Light); }
        s.Light = null;
    }

    /// <summary>The seats of the things that stand, laid out again; whoever sat on one of a thing no longer standing gets up.</summary>
    void SeatsAgain()
    {
        foreach (var u in Units) if (u.SeatAt != null && !u.SeatAt.Of.Ready) StandUp(u);
        var kept = new List<Seat>();
        foreach (var seat in Seats) if (seat.Of.Ready && Yards.Contains(seat.Of)) kept.Add(seat);
        Seats.Clear();
        Seats.AddRange(kept);
        foreach (var s in Yards)
        {
            if (!s.Ready || kept.Exists(k => k.Of == s)) continue;
            foreach (var (dx, dz, h) in Yard.Seats(s.Kind))
            {
                var (tx, tz) = Yard.Turn(dx, dz, s.Rot);
                // one sits facing the way the thing faces: +z, turned
                var (fx, fz) = Yard.Turn(0f, 1f, s.Rot);
                Seats.Add(new Seat { Of = s, X = s.X + tx, Y = s.Y, Z = s.Z + tz, Facing = MathF.Atan2(fz, fx), High = h });
            }
        }
    }

    /// <summary>Goes to sit on a free seat close by.</summary>
    bool TrySit(Unit u)
    {
        Seat best = null;
        float bd = 64 * 64;
        foreach (var seat in Seats)
        {
            if (seat.By != null || MathF.Abs(seat.Y - u.Y) > 12) continue;
            float dx = seat.X - u.X, dz = seat.Z - u.Z, d = dx * dx + dz * dz;
            if (d < bd) { bd = d; best = seat; }
        }
        if (best == null) return false;
        int c = NearestStand((int)best.X, (int)best.Z, u.T.Move.Height);
        if (c < 0) return false;
        int ci = Nav.I(c), ck = Nav.K(c);
        var path = c == u.Cell ? new List<int>() : Paths.Find(u.Cell, x => x == c, x => H(x, ci, ck), u.T.Move, 1500, gates: true);
        if (path == null) return false;
        u.Path = path.Count > 0 ? path : null; u.PathI = 0;
        u.Pastime = Pastime.Stroll; u.After = Pastime.Sit; u.PastimeUntil = Time + 40;
        best.By = u;
        u.SeatAt = best;
        return true;
    }

    /// <summary>Sends one of ours, idle, to sit on a bench close by or to warm itself at a fire (for the scenes staged and the tests).</summary>
    public bool Rest(Unit u, bool warm) => warm ? TryWarm(u) : TrySit(u);

    /// <summary>Come to the seat it went for: sits on it, facing out.</summary>
    void SitDown(Unit u)
    {
        var seat = u.SeatAt;
        if (seat == null) return;
        u.X = seat.X; u.Z = seat.Z;
        u.Facing = seat.Facing;
        u.SeatH = seat.High;
    }

    /// <summary>Gets up from its seat, if it sat on one, and leaves its place in the street, if it had one.</summary>
    void StandUp(Unit u)
    {
        if (u.SeatAt != null && u.SeatAt.By == u) u.SeatAt.By = null;
        u.SeatAt = null;
        u.SeatH = 0;
        LeaveSpot(u);
    }

    /// <summary>The fires people warm themselves at besides the citadel's braziers: our campfires and braziers that burn.</summary>
    IEnumerable<(float x, float y, float z)> Hearths()
    {
        foreach (var (x, y, z) in Site.Torches) yield return (x, y, z);
        foreach (var s in Yards)
            if (s.Ready && s.Light != null && s.Light.DarkUntil <= Time && Yard.Warms(s.Kind)) yield return (s.Light.LX, s.Light.LY, s.Light.LZ);
    }

    void WriteYards(BinaryWriter w)
    {
        w.Write(Yards.Count);
        foreach (var s in Yards) { w.Write((byte)s.Kind); w.Write(s.X); w.Write(s.Y); w.Write(s.Z); w.Write((byte)s.Rot); }
    }

    void ReadYards(BinaryReader r)
    {
        Yards.Clear(); Seats.Clear();
        for (int i = r.ReadInt32(); i > 0; i--)
        {
            var s = new DecorSite { Kind = (DecorKind)r.ReadByte(), X = r.ReadInt32(), Y = r.ReadInt32(), Z = r.ReadInt32(), Rot = r.ReadByte() };
            s.Voxels = Replan(s);
            // its fire, read back with the rest of the fixtures (it is anchored on its embers)
            var embers = new HashSet<int>();
            for (int j = 0; j < s.Voxels.Count; j++) if (s.Voxels.M(j) == Mat.Ember) embers.Add(WaterPlanner.Pack(s.Voxels.X(j), s.Voxels.Y(j), s.Voxels.Z(j)));
            s.Light = Fixtures.Find(f => f.Kind is FixtureKind.Campfire or FixtureKind.Brazier && embers.Contains(f.Anchor));
            s.Ready = s.Light != null;
            Yards.Add(s);
        }
        YardColumns();
        _yardDue = 0;
    }
}
