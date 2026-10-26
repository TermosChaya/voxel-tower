using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace VoxelTower.Core;

/// <summary>
/// What holds a valley, and whose it is: the citadel of the first valley; the place an outpost is to be raised on, in a
/// valley taken or to be taken (the level ground in the middle and the stone that marks it, <see cref="Holdings.OutpostPlace"/>);
/// the horde's town, its huts and its palisade, where an enemy's valley has its lair (<see cref="Holdings.HordeTown"/>).
/// </summary>
public enum Holding : byte { Citadel, Outpost, HordeTown }

/// <summary>Whose a cell of the map is: nobody's, ours, the horde's.</summary>
public enum Owner : byte { None, Ours, Horde }

/// <summary>
/// The one clock of the map, as a valley reads it and is set by it: the day, and how far into it (seconds from its dawn,
/// the daylight first and the night after it: <see cref="DayClock.DayLength"/> of day, then the night).
/// </summary>
public readonly record struct ClockTime(int Day, double At)
{
    /// <summary>Seconds of the map's time since the first dawn: a day and its night together are a cycle.</summary>
    public double Total => (Day - 1) * DayClock.Cycle + At;
}

/// <summary>
/// One of ours on the way between valleys: what he is, how he looks, how hurt, what work he does; whether, a commander,
/// he sees to those about him. He walks out over an edge (<see cref="Sim.Depart"/>), and into the next valley as he went
/// (<see cref="Sim.Arrive"/>).
/// </summary>
public sealed class Traveller
{
    public UnitType Kind;
    public int Look;
    public float Hp, MaxHp;
    public BodyState Wounds;
    public byte[] Priority;
    public bool Militia, Commands = true;
    public string Title;
    /// <summary>The valley whose places he holds (its cell's key, <see cref="Campaign.KeyOf"/>); -1 for the valley he walked out of.</summary>
    public int Billet = -1;
    /// <summary>A commander's hero: his name, level and skills (Heroes.cs).</summary>
    public Hero Hero;

    /// <summary>A pawn of the valley taken off as he walks out of it; one of the valley's own holds the places of the valley <paramref name="here"/>.</summary>
    public static Traveller Of(Unit u, int here = -1) => new()
    {
        Kind = u.T, Look = u.Look, Hp = u.Hp, MaxHp = u.MaxHp, Wounds = u.Wounds, Priority = (byte[])u.Priority.Clone(),
        Militia = u.Militia, Commands = u.Commands, Title = u.Title, Billet = u.Billet >= 0 ? u.Billet : here, Hero = u.Hero,
    };

    /// <summary>He as he was, onto one come into a valley: a guest there unless it is the valley whose places he holds (<paramref name="here"/>).</summary>
    internal void Onto(Unit u, int here = -1)
    {
        u.Look = Look; u.MaxHp = MaxHp; u.Hp = Math.Min(Hp, MaxHp); u.Wounds = Wounds;
        if (Priority != null) Array.Copy(Priority, u.Priority, Math.Min(Priority.Length, u.Priority.Length));
        u.Militia = Militia; u.Commands = Commands; u.Title = Title;
        u.Billet = Billet < 0 || Billet == here ? -1 : Billet;
        if (Hero != null) u.Hero = Hero;
    }

    internal void Write(BinaryWriter w)
    {
        w.Write(Kind.Key); w.Write(Look); w.Write(Hp); w.Write(MaxHp);
        w.Write((byte)(Priority?.Length ?? 0)); if (Priority != null) w.Write(Priority);
        w.Write(Militia); w.Write(Commands); w.Write(Title ?? ""); w.Write(Billet);
        Heroes.Write(w, Hero);
        Sim.WriteBody(w, Wounds);
    }

    internal static Traveller Read(BinaryReader r)
    {
        var kind = Sim.KindOf(r.ReadString());
        var t = new Traveller { Kind = kind, Look = r.ReadInt32(), Hp = r.ReadSingle(), MaxHp = r.ReadSingle() };
        t.Priority = r.ReadBytes(r.ReadByte());
        t.Militia = r.ReadBoolean(); t.Commands = r.ReadBoolean(); t.Title = r.ReadString(); t.Billet = r.ReadInt32();
        t.Hero = Heroes.Read(r);
        if (t.Title.Length == 0) t.Title = null;
        t.Wounds = Sim.ReadBody(r, kind, t.Look);
        return kind == null ? null : t;
    }
}

/// <summary>A party of ours between valleys: who goes, the commander at its head if it has one, and the side it went out by.</summary>
public sealed class Party
{
    public int Side;
    public readonly List<Traveller> Members = new();
    public Traveller Commander => Members.Find(m => m.Kind == Kinds.Captain);

    internal void Write(BinaryWriter w)
    {
        w.Write(Side); w.Write(Members.Count);
        foreach (var m in Members) m.Write(w);
    }

    internal static Party Read(BinaryReader r)
    {
        var p = new Party { Side = r.ReadInt32() };
        for (int n = r.ReadInt32(); n > 0; n--) if (Traveller.Read(r) is { } m) p.Members.Add(m);
        return p;
    }
}

/// <summary>
/// A cell of the map: one valley, all of it made again from what the cell keeps (its seed, its kind, what holds it, its
/// sides shared with its neighbours and the side its enemy comes by); whose it is, whether it is the first valley, and
/// whether we have been in it — then its valley lies in a file of the campaign's folder, as play left it.
/// </summary>
public sealed partial class MapCell
{
    public int Q, R;
    public uint Seed;
    public ValleyKind Kind;
    public Holding Holding;
    public Owner Owner;
    public int EntrySide = Hex.West;
    public bool Home, Visited;
    public readonly HexEdge[] Edges = new HexEdge[6];

    /// <summary>Its valley's file in the campaign's folder.</summary>
    public string File => $"valley_{Q}_{R}.sav";
    /// <summary>An outpost's tower was marked for the workers in its valley (Sim.Outpost.cs); standing, the cell is ours.</summary>
    public bool Outpost;
}

/// <summary>
/// A party on the map, ours or the horde's: in a cell, the cells it is to walk through next and how far it has gone
/// toward the first of them, seconds of the map's time (Campaign.Turn.cs walks it, MapPath.cs finds its way).
/// </summary>
public sealed partial class MapParty
{
    public int Q, R;
    public Party Party;
    public Owner Owner = Owner.Ours;
    public List<MapCell> Path;
    public double Progress;
}

/// <summary>
/// A campaign: the map of valleys and what goes on over all of them. It keeps the cells (each a valley made from its
/// seed, laid over with what play changed of it once we were there), the one clock every valley runs by, the gold that is
/// the realm's and not a valley's, the parties of ours between valleys, which cell we are in, and when the first valley
/// fell. On disk it is a folder: the map's file and a file for each valley visited, the valley's own save
/// (<see cref="Sim.Save"/>). What is decided on the map — who walks where, the enemy's turns, quick battles — comes
/// with the map itself (docs/handoff/13-world-map.md); this is what it stands on.
/// </summary>
public sealed partial class Campaign
{
    public const int Version = 8;
    public const string FileName = "campaign.dat";
    static readonly byte[] Magic = { (byte)'V', (byte)'T', (byte)'C', (byte)'M' };

    public uint Seed;
    public ClockTime Clock = new(1, 0);
    /// <summary>The realm's gold: every valley's town hall, market and mine pays into it, and every valley hires from it.</summary>
    public double Gold = 150;
    /// <summary>The cell we are in.</summary>
    public int HereQ, HereR;
    /// <summary>The day the first valley fell, −1 while it stands: three days to take another, or the campaign is lost.</summary>
    public int HomeFell = -1;
    public readonly List<MapCell> Cells = new();
    public readonly List<MapParty> Parties = new();

    /// <summary>The step to the neighbour across each side (axial q, r; r grows southward): north-east, east, south-east, south-west, west, north-west.</summary>
    public static readonly (int dq, int dr)[] Step = { (1, -1), (1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1) };

    /// <summary>How many days the realm has to take another valley once the first one fell.</summary>
    public const int DaysToRetake = 3;

    /// <summary>A cell by its axial place: on the big map from its grid, on the small one from an index of the rings.</summary>
    public MapCell At(int q, int r)
    {
        if (_grid != null) { var (col, row) = Offset(q, r); return Cell(col, row); }
        if (_ringIndex == null || _ringCount != Cells.Count)
        {
            _ringIndex = new Dictionary<long, MapCell>();
            foreach (var c in Cells) _ringIndex[(long)c.Q << 32 | (uint)c.R] = c;
            _ringCount = Cells.Count;
        }
        return _ringIndex.TryGetValue((long)q << 32 | (uint)r, out var cell) ? cell : null;
    }

    Dictionary<long, MapCell> _ringIndex;
    int _ringCount;

    /// <summary>The big map's grid of cells, made by the maker (Campaign.World.cs).</summary>
    internal void UseGrid(MapCell[] grid) => _grid = grid;
    public MapCell Here => At(HereQ, HereR);
    public MapCell Home => Cells.Find(c => c.Home);
    public MapCell Neighbour(MapCell c, int side) => At(c.Q + Step[side].dq, c.R + Step[side].dr);

    /// <summary>The cells' distance on the map, in steps.</summary>
    public static int Distance(int q0, int r0, int q1, int r1)
    {
        int dq = q1 - q0, dr = r1 - r0;
        return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
    }

    /// <summary>
    /// The first valley lost and no other of ours with an outpost for the days allowed: the campaign is lost (the rule of
    /// «Heroes»: seven days without a town, here three).
    /// </summary>
    public bool Defeated => HomeFell >= 0 && Clock.Day - HomeFell >= DaysToRetake && !Cells.Exists(c => !c.Home && c.Owner == Owner.Ours);

    /// <summary>
    /// A new map of so many rings round the first valley (2: nineteen cells, the small map; 3: thirty-seven, the game's).
    /// The first valley in the middle, without the old set of places, its citadel ours; round it the other kinds, an ore
    /// valley and a holy one among the six nearest, nobody's, each with a place for an outpost; the horde's lairs in cells
    /// of the outer ring, one on the small map, two or three on the big one, as far apart as can be; the treasures in the
    /// cells nobody holds further out (Campaign.Troves.cs). Every two cells side by side share the seed of their side, so
    /// their valleys meet without a step; each valley's enemy comes by the side toward the nearest lair, a lair's toward
    /// the first valley.
    /// </summary>
    public static Campaign New(uint seed, int rings = 2)
    {
        var c = new Campaign { Seed = seed, Rings = rings };
        var rnd = new Mulberry32(seed * 2654435761u + 41);
        var near = new List<ValleyKind> { ValleyKind.Ore, ValleyKind.Sacred, ValleyKind.Forest, ValleyKind.Marsh, ValleyKind.Mountain, ValleyKind.Forest };
        var far = new[] { ValleyKind.Ore, ValleyKind.Sacred, ValleyKind.Forest, ValleyKind.Marsh, ValleyKind.Mountain };
        // the six nearest in an order of the seed's
        for (int i = near.Count - 1; i > 0; i--) { int j = (int)(rnd.Next() * (i + 1)); (near[i], near[j]) = (near[j], near[i]); }
        for (int r = -rings; r <= rings; r++)
            for (int q = -rings; q <= rings; q++)
            {
                int ring = Distance(0, 0, q, r);
                if (ring > rings) continue;
                var cell = new MapCell { Q = q, R = r, Seed = CellSeed(seed, q, r), Holding = Holding.Outpost };
                if (ring == 0) { cell.Kind = ValleyKind.First; cell.Holding = Holding.Citadel; cell.Owner = Owner.Ours; cell.Home = true; }
                else if (ring == 1) cell.Kind = near[SideOf(q, r)];
                else cell.Kind = far[(int)(rnd.Next() * far.Length)];
                c.Cells.Add(cell);
            }
        // the lairs: cells of the outer ring, the first by the seed, each next as far from those laid as can be
        var outer = c.Cells.FindAll(x => Distance(0, 0, x.Q, x.R) == rings);
        int lairs = rings <= 0 ? 0 : rings >= 3 ? 2 + (int)(rnd.Next() * 2) : 1;
        var dens = new List<MapCell>();
        for (int k = 0; k < lairs && outer.Count > 0; k++)
        {
            MapCell pick = null;
            if (k == 0) pick = outer[(int)(rnd.Next() * outer.Count)];
            else
            {
                int apart = -1;
                foreach (var o in outer)
                {
                    if (dens.Contains(o)) continue;
                    int least = int.MaxValue;
                    foreach (var d in dens) least = Math.Min(least, Distance(o.Q, o.R, d.Q, d.R));
                    if (least > apart) { apart = least; pick = o; }
                }
            }
            if (pick == null) break;
            pick.Holding = Holding.HordeTown; pick.Owner = Owner.Horde;
            dens.Add(pick);
        }
        MapCell NearestDen(MapCell cell)
        {
            MapCell best = null; int bd = int.MaxValue;
            foreach (var d in dens) { int dd = Distance(cell.Q, cell.R, d.Q, d.R); if (dd < bd) { bd = dd; best = d; } }
            return best;
        }
        foreach (var cell in c.Cells)
        {
            // the sides, shared with the neighbour across each
            for (int s = 0; s < 6; s++)
            {
                int nq = cell.Q + Step[s].dq, nr = cell.R + Step[s].dr;
                cell.Edges[s] = new HexEdge { Seed = EdgeSeed(seed, cell.Q, cell.R, nq, nr) };
            }
            // the enemy comes by the side toward the nearest lair; a lair's own, toward the first valley
            if (NearestDen(cell) is { } den) cell.EntrySide = dens.Contains(cell) ? SideToward(cell, 0, 0) : SideToward(cell, den.Q, den.R);
        }
        c.LayWays(new Mulberry32(seed * 2246822519u + 77));
        c.LayTroves(new Mulberry32(seed * 3266489917u + 101));
        return c;
    }

    /// <summary>
    /// The roads and the rivers over the sides of the cells, as their valleys lay them (MapGen.Relief.cs): each valley's
    /// road comes in at the middle of the side its enemy comes by, up to the stronghold, and out at the middle of the side
    /// across; its river comes in over one side and goes out over another, between the road's two sides, so that the road
    /// fords it and no part of the valley is cut off. Two cells side by side agree whether a river runs between them, and
    /// where; no river runs round in a ring, they run off the map at its rim. The enemy comes by the side toward the lair,
    /// or by one next to it where the rivers need it (never so in the first valley or the lair). A river crosses a side a
    /// quarter of the way along it, clear of the middle where parties walk in.
    /// </summary>
    void LayWays(Mulberry32 rnd)
    {
        var cells = new List<MapCell>(Cells);
        cells.Sort((a, b) => a.R != b.R ? a.R.CompareTo(b.R) : a.Q.CompareTo(b.Q));
        var index = new Dictionary<MapCell, int>();
        for (int i = 0; i < cells.Count; i++) index[cells[i]] = i;
        // what each cell may have: its enemy's side (the one toward the lair first, then those next to it, unless it is the
        // first valley or the lair), and its river's two sides, one on each side of the road's line, in an order of the seed's
        var choices = new List<(int entry, int a, int b)>[cells.Count];
        void Shuffle()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                var entries = c.Home || c.Holding == Holding.HordeTown ? new[] { c.EntrySide } : new[] { c.EntrySide, (c.EntrySide + 1) % 6, (c.EntrySide + 5) % 6 };
                var list = new List<(int, int, int)>();
                foreach (int e in entries)
                {
                    var one = new List<(int, int, int)>();
                    foreach (int x in new[] { (e + 1) % 6, (e + 2) % 6 })
                        foreach (int y in new[] { (e + 4) % 6, (e + 5) % 6 })
                            one.Add((e, Math.Min(x, y), Math.Max(x, y)));
                    for (int k = one.Count - 1; k > 0; k--) { int j = (int)(rnd.Next() * (k + 1)); (one[k], one[j]) = (one[j], one[k]); }
                    list.AddRange(one);
                }
                choices[i] = list;
            }
        }
        var pick = new (int entry, int a, int b)[cells.Count];
        int tried = 0;
        static bool Has((int entry, int a, int b) p, int s) => p.a == s || p.b == s;
        // a river leaving one cell over a side goes into the next over the side across; round in a ring, it never reaches the rim
        int Next(int at, ref int side)
        {
            var n = Neighbour(cells[at], side);
            if (n == null) return -1;
            int j = index[n], back = (side + 3) % 6;
            side = pick[j].a == back ? pick[j].b : pick[j].a;
            return j;
        }
        bool Ringless()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                int side = pick[i].a, at = i;
                for (int steps = 0; ; steps++)
                {
                    at = Next(at, ref side);
                    if (at < 0) break;
                    if (at == i || steps > cells.Count) return false;
                }
            }
            return true;
        }
        bool Fits(int i, (int entry, int a, int b) p)
        {
            var c = cells[i];
            for (int s = 0; s < 6; s++)
            {
                if (Neighbour(c, s) is not { } n || index[n] >= i) continue;
                if (Has(p, s) != Has(pick[index[n]], (s + 3) % 6)) return false;
            }
            // both its river's ends running into cells laid already: followed from one, it must not come back by the other
            if (Neighbour(c, p.a) is { } na && index[na] < i && Neighbour(c, p.b) is { } nb && index[nb] < i)
            {
                int at = index[na], side = (p.a + 3) % 6;
                side = pick[at].a == side ? pick[at].b : pick[at].a;
                for (int steps = 0; steps <= cells.Count; steps++)
                {
                    if (at == index[nb]) return false;
                    var n = Neighbour(cells[at], side);
                    if (n == null || index[n] >= i) break;
                    int back = (side + 3) % 6;
                    at = index[n];
                    side = pick[at].a == back ? pick[at].b : pick[at].a;
                }
            }
            return true;
        }
        bool Lay(int i)
        {
            if (i == cells.Count) return Ringless();
            foreach (var p in choices[i])
            {
                if (++tried > 150000) return false;
                if (!Fits(i, p)) continue;
                pick[i] = p;
                if (Lay(i + 1)) return true;
            }
            return false;
        }
        // (the search given up after so many tries, it begins again in another order of the seed's)
        bool laid = false;
        for (int round = 0; round < 12 && !laid; round++) { Shuffle(); tried = 0; laid = Lay(0); }
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (laid) c.EntrySide = pick[i].entry;
            c.Edges[c.EntrySide].Road = .5f;
            c.Edges[(c.EntrySide + 3) % 6].Road = .5f;
        }
        if (!laid) return;
        // where along each side the river crosses: a quarter of the way from the first corner of the side of the cell
        // first in the order; the cell across reads it from its own first corner, and a valley reads the side its river
        // comes in by as it is and the side it goes out by the other way round
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            foreach (int side in new[] { pick[i].a, pick[i].b })
            {
                var n = Neighbour(c, side);
                float at = n != null && index[n] < i ? .75f : .25f;
                c.Edges[side].River = side == pick[i].a ? at : 1 - at;
            }
        }
    }

    /// <summary>Where along a side of a cell its river crosses, from that side's first corner (round by the east); -1 if none does.</summary>
    public static float RiverAt(MapCell c, int side)
    {
        if (c.Edges[side] is not { River: >= 0 } e) return -1;
        // (the first of its river's sides is read as it is, the other the other way round, as the valley lays it)
        for (int s = 0; s < side; s++) if (c.Edges[s] is { River: >= 0 }) return 1 - e.River;
        return e.River;
    }

    /// <summary>Which side of the middle cell a cell of the first ring lies across.</summary>
    static int SideOf(int q, int r)
    {
        for (int s = 0; s < 6; s++) if (Step[s] == (q, r)) return s;
        return 0;
    }

    /// <summary>The side of a cell that faces most nearly toward another cell.</summary>
    public static int SideToward(MapCell c, int q, int r)
    {
        // axial to the plane, pointed north and south: x = √3 (q + r/2), z = 1.5 r
        double fx = Math.Sqrt(3) * (q - c.Q + (r - c.R) / 2.0), fz = 1.5 * (r - c.R);
        int best = 0; double most = double.MinValue;
        for (int s = 0; s < 6; s++)
        {
            var (dq, dr) = Step[s];
            double sx = Math.Sqrt(3) * (dq + dr / 2.0), sz = 1.5 * dr, dot = sx * fx + sz * fz;
            if (dot > most) { most = dot; best = s; }
        }
        return best;
    }

    internal static uint CellSeedOf(uint seed, int q, int r) => CellSeed(seed, q, r);

    static uint CellSeed(uint seed, int q, int r) => (uint)(seed * 73856093u ^ (uint)(q + 4096) * 19349663u ^ (uint)(r + 4096) * 83492791u) | 1;

    /// <summary>The seed of the side between two cells: the same read from either.</summary>
    static uint EdgeSeed(uint seed, int q0, int r0, int q1, int r1)
    {
        if (q1 < q0 || q1 == q0 && r1 < r0) (q0, r0, q1, r1) = (q1, r1, q0, r0);
        return CellSeed(seed ^ 0x9e3779b9u, q0 * 3 + q1 * 7919, r0 * 5 + r1 * 104729);
    }

    /// <summary>What the valley of a cell is made from: a hexagon of the game's size, the first valley without the old places.</summary>
    public MapParams ParamsOf(MapCell c) => new()
    {
        Seed = c.Seed, Kind = c.Kind, Classic = false, Hexagon = true, SizeX = MapParams.GameSize, SizeZ = MapParams.GameSize,
        Edges = c.Edges, EntrySide = c.EntrySide, Holding = c.Holding, Trove = c.Trove, TroveTaken = c.TroveTaken,
    };

    // ------------------------------------------------------------ going in and out of a valley

    /// <summary>
    /// The valley of a cell, made from its seed and, if we were there, laid over with what play changed of it (its file
    /// in the folder); a lair never visited gets its garrison. It runs by the map's clock and the realm's gold, and its
    /// raids are the map's. What the screen kept beside it comes back in <paramref name="extra"/>.
    /// </summary>
    public Sim OpenValley(string dir, MapCell cell, out string extra)
    {
        extra = null;
        var mp = ParamsOf(cell);
        var w = new VoxelWorld(mp.SizeX, mp.SizeY, mp.SizeZ);
        var site = MapGen.Generate(w, mp);
        w.Compact();
        string path = Path.Combine(dir, cell.File);
        SaveReader save = cell.Visited && System.IO.File.Exists(path) ? new SaveReader(System.IO.File.OpenRead(path)) : null;
        Ready();
        try
        {
            save?.Valley(w, site);
            var sim = new Sim(w, site);
            if (save != null) extra = sim.Load(save);
            else
            {
                sim.SetSource(new MapRaids());
                // the siege's first raid, drawn up as the valley was made, is not the map's
                sim.Bands.RemoveAll(b => b.State == BandState.Coming);
                // (a lair the map keeps a garrison for gets that one as it settles)
                if (cell.Holding == Holding.HordeTown && cell.Sum.Keep.Count == 0) sim.Garrison(Sim.Roster(LairThreat, 1));
            }
            sim.Campaign = true;
            Enter(sim, cell);
            Settle(cell, sim);
            return sim;
        }
        finally { save?.Dispose(); }
    }

    /// <summary>How strong a lair's garrison is, against the siege's nights.</summary>
    public const float LairThreat = 3;

    /// <summary>
    /// A valley we go into (the cell we are in, if not said) runs by the map's clock and holds the realm's gold; it knows
    /// its cell, and what the rest of the realm keeps for it (<see cref="Reckon"/>).
    /// </summary>
    public void Enter(Sim sim, MapCell cell = null)
    {
        cell ??= Here;
        sim.SetClock(Clock);
        sim.Stock.Gold = Gold;
        Array.Copy(Rare, sim.Rare, Rare.Length);
        sim.CellKey = KeyOf(cell);
        Live = sim; InValley = true;
        _hallLook = 0;
        Bless(sim);
        Reckon(sim, cell);
    }

    /// <summary>
    /// A valley we leave (or look at, staying in it): the clock and the gold back to the map, the parties gone out over its
    /// edges onto the map at its cell, and whether the first valley fell.
    /// </summary>
    public void Leave(Sim sim, MapCell cell)
    {
        Clock = sim.Clock.Now;
        Gold = sim.Stock.Gold;
        Array.Copy(sim.Rare, Rare, Rare.Length);
        foreach (var p in sim.TakeDeparted())
        {
            foreach (var m in p.Members) if (m.Billet < 0) m.Billet = KeyOf(cell);
            Parties.Add(new MapParty { Id = _nextParty++, Q = cell.Q, R = cell.R, CameQ = cell.Q, CameR = cell.R, Party = p });
        }
        if (cell.Home && sim.Lost && HomeFell < 0) HomeFell = sim.LostDay;
        Summarise(cell, sim);
    }

    // ------------------------------------------------------------ the folder

    public static bool Exists(string dir) => System.IO.File.Exists(Path.Combine(dir, FileName));

    /// <summary>A cell's valley written to the folder as play left it; the cell is visited from now on.</summary>
    public void SaveValley(string dir, MapCell cell, Sim sim, string label, string extra = "")
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, cell.File), tmp = path + ".part";
        using (var f = System.IO.File.Create(tmp)) sim.Save(f, cell.Seed, label, extra);
        System.IO.File.Move(tmp, path, overwrite: true);
        cell.Visited = true;
    }

    /// <summary>The map written to the folder: the cells, the clock, the gold, the parties on the map.</summary>
    public void Write(string dir)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, FileName), tmp = path + ".part";
        using (var f = System.IO.File.Create(tmp))
        using (var z = new GZipStream(f, CompressionLevel.Fastest))
        using (var w = new BinaryWriter(z, Encoding.UTF8))
        {
            w.Write(Magic); w.Write(Version); w.Write(Sim.SaveVersion);
            w.Write(Seed); w.Write(Clock.Day); w.Write(Clock.At); w.Write(Gold); w.Write(HereQ); w.Write(HereR); w.Write(HomeFell);
            w.Write(Rings); w.Write(Blessings.Count); foreach (var b in Blessings) w.Write((byte)b);
            // the big map is made again from its seed, and only what changed of it is written (WriteWorld)
            w.Write(Width); w.Write(Height);
            w.Write(Big ? 0 : Cells.Count);
            foreach (var c in Big ? new List<MapCell>() : Cells)
            {
                w.Write(c.Q); w.Write(c.R); w.Write(c.Seed); w.Write((byte)c.Kind); w.Write((byte)c.Holding); w.Write((byte)c.Owner);
                w.Write((byte)c.EntrySide); w.Write(c.Home); w.Write(c.Visited);
                foreach (var e in c.Edges) { w.Write(e.Seed); w.Write(e.River); w.Write(e.Road); }
                w.Write(c.Outpost);
                w.Write((byte)c.Trove); w.Write(c.TroveTaken); w.Write((byte)c.Blessing);
            }
            w.Write(Parties.Count);
            foreach (var p in Parties) { w.Write(p.Q); w.Write(p.R); p.Party.Write(w); WriteWay(w, p); }
            WriteTurn(w);
            WriteWorld(w);
        }
        System.IO.File.Move(tmp, path, overwrite: true);
    }

    /// <summary>The map read back from a folder; it throws on a file of another version.</summary>
    public static Campaign Read(string dir)
    {
        using var f = System.IO.File.OpenRead(Path.Combine(dir, FileName));
        using var z = new GZipStream(f, CompressionMode.Decompress);
        using var r = new BinaryReader(z, Encoding.UTF8);
        var magic = r.ReadBytes(4);
        if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
            throw new InvalidDataException("not a campaign");
        int version = r.ReadInt32(), saves = r.ReadInt32();
        if (version != Version || saves != Sim.SaveVersion) throw new InvalidDataException($"a campaign of version {version}/{saves}, not {Version}/{Sim.SaveVersion}");
        uint seed = r.ReadUInt32();
        var clock = new ClockTime(r.ReadInt32(), r.ReadDouble());
        double gold = r.ReadDouble(); int hq = r.ReadInt32(), hr = r.ReadInt32(), fell = r.ReadInt32(), rings = r.ReadInt32();
        var blessings = new List<Blessing>();
        for (int n = r.ReadInt32(); n > 0; n--) blessings.Add((Blessing)r.ReadByte());
        int width = r.ReadInt32(), height = r.ReadInt32();
        // the big map made again from its seed, the small one read cell by cell
        var c = width > 0 ? NewWorld(seed, width) : new Campaign { Seed = seed };
        c.Clock = clock; c.Gold = gold; c.HereQ = hq; c.HereR = hr; c.HomeFell = fell; c.Rings = rings;
        c.Blessings.AddRange(blessings);
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var cell = new MapCell
            {
                Q = r.ReadInt32(), R = r.ReadInt32(), Seed = r.ReadUInt32(), Kind = (ValleyKind)r.ReadByte(), Holding = (Holding)r.ReadByte(),
                Owner = (Owner)r.ReadByte(), EntrySide = r.ReadByte(), Home = r.ReadBoolean(), Visited = r.ReadBoolean(),
            };
            for (int s = 0; s < 6; s++) cell.Edges[s] = new HexEdge { Seed = r.ReadUInt32(), River = r.ReadSingle(), Road = r.ReadSingle() };
            cell.Outpost = r.ReadBoolean();
            cell.Trove = (TroveKind)r.ReadByte(); cell.TroveTaken = r.ReadBoolean(); cell.Blessing = (Blessing)r.ReadByte();
            c.Cells.Add(cell);
        }
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var p = new MapParty { Q = r.ReadInt32(), R = r.ReadInt32(), Party = Party.Read(r) };
            c.ReadWay(r, p);
            c.Parties.Add(p);
        }
        c.ReadTurn(r);
        c.ReadWorld(r);
        return c;
    }
}
