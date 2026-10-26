using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>
/// The valley as a cell of the map of valleys (Campaign.cs). It runs by the map's clock, set from outside as it is
/// entered and read back as it is left. Parties of ours come in over any of its six sides, each as he left the last
/// valley, and go out over them to the map. Its fall is the valley's, not the game's: the citadel (or an outpost's stone)
/// down, the raid ends, the horde stays to hold the valley and the map is told. The horde's town of an enemy's valley
/// has its garrison.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The valley is a cell of a campaign's map: its fall is its own, and the game goes on.</summary>
    public bool Campaign;
    /// <summary>The valley fell in a campaign — its citadel or its outpost down, the horde holding it — when, and on what day.</summary>
    public bool Lost;
    public double LostAt;
    public int LostDay;
    /// <summary>The valley fell: the map is told.</summary>
    public event Action ValleyFell;
    /// <summary>Parties of ours gone out over the valley's sides, a party a side, for the map to take (<see cref="TakeDeparted"/>).</summary>
    public readonly List<Party> Departed = new();
    /// <summary>
    /// The valley's cell on the map (<see cref="VoxelTower.Core.Campaign.KeyOf"/>), set as it is entered; -1 outside a
    /// campaign. Those come in who hold the places of another valley are its guests (<see cref="Unit.Billet"/>).
    /// </summary>
    public int CellKey = -1;
    /// <summary>
    /// What the rest of the realm keeps for the valley, set by the map as it runs: the places held by the valley's own
    /// who are away (on the map or in another valley), the commanders not in it, and the levels of the town halls of
    /// the other valleys of ours (each gives a commander more, <see cref="CaptainCap"/>).
    /// </summary>
    public int AwayPlaces, CaptainsAway, HallsAway;
    double _nextDepartLook;

    /// <summary>How far in from its side a party come into the valley stands (30 m), and how far apart its file walks in (1½ m).</summary>
    public const float ArriveIn = 30 * VoxelWorld.PerMetre, FileApart = 1.5f * VoxelWorld.PerMetre;

    /// <summary>
    /// The valley set to the map's time as it is entered. What grows by the day grows for the days it was away, a week at
    /// most (the town's stocks never past double): the map counted the days, the valley catches up on them.
    /// </summary>
    public void SetClock(ClockTime t)
    {
        int passed = Math.Min(t.Day - Clock.Day, 7);
        Clock.Set(t);
        for (int d = 0; d < passed; d++) TownDay(say: d == passed - 1);
    }

    /// <summary>
    /// A party of ours comes into the valley over a side (0 the north-eastern, round by the east): in a file along the
    /// middle of it, the commander in the middle of the file, each as he left the last valley — his wounds, his work, his
    /// look — and then some way in, in ranks, where they stand (soldiers take the spot as their post). In the horde's
    /// valley where they came in is their camp: the wounded are carried back there. Returns those come.
    /// </summary>
    public List<Unit> Arrive(Party party, int side)
    {
        side = ((side % 6) + 6) % 6;
        var hex = Site.Hex ?? Hex.In(World.SX, World.SZ);
        var come = new List<Unit>();
        var order = new List<Traveller>(party.Members);
        int head = order.FindIndex(m => m.Kind == Kinds.Captain);
        if (head >= 0) { var c = order[head]; order.RemoveAt(head); order.Insert(order.Count / 2, c); }
        var (ix, iz) = hex.Inward(side);
        for (int i = 0; i < order.Count; i++)
        {
            var m = order[i];
            var (x, z) = EdgePoint(side, Along(hex, side, i, order.Count));
            int cell = NearestStand(x, z, m.Kind.Move.Height);
            if (cell < 0) continue;
            var u = Spawn(m.Kind, cell);
            m.Onto(u, CellKey);
            u.Facing = MathF.Atan2(iz, ix);
            come.Add(u);
        }
        if (come.Count == 0) return come;
        var (sx, sz) = hex.SidePoint(side, .5f, ArriveIn);
        int spot = NearestStand((int)sx, (int)sz, Kinds.Worker.Move.Height);
        // short of what stands in the way (a river, a crag, a thick wood): the farthest spot in along the middle they get
        // to without going far round
        int from = come[come.Count / 2].Cell;
        for (float d = ArriveIn; d >= 6 * VoxelWorld.PerMetre; d -= 3 * VoxelWorld.PerMetre)
        {
            var (px, pz) = hex.SidePoint(side, .5f, d);
            int c = NearestStand((int)px, (int)pz, Kinds.Worker.Move.Height);
            if (c < 0) continue;
            var way = Paths.Find(from, x => x == c, _ => 0, Kinds.Worker.Move, 8000);
            if (way != null && way.Count <= d / NavGrid.CS * 1.5f + 8) { spot = c; break; }
        }
        if (spot >= 0) Command(come, spot);
        if (Site.Holding == Holding.HordeTown)
        {
            var (gx, gz) = EdgePoint(side, .5);
            if (NearestStand(gx, gz, Kinds.Worker.Move.Height) is int g and >= 0) Gate = g;
        }
        bool led = come.Exists(u => u.T == Kinds.Captain);
        Say($"Отряд пришёл {EdgeName(side)}: {come.Count} {Plural(come.Count, "человек", "человека", "человек")}{(led ? ", ведёт командир" : "")}.", 1, come[0]);
        return come;
    }

    /// <summary>Where along a side the i-th of a file of n stands (0…1), the file a metre and a half apart about the middle.</summary>
    static double Along(Hex hex, int side, int i, int n)
    {
        var (a, b) = hex.Side(side);
        float len = MathF.Max(1, MathF.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z)));
        return .5 + (i - (n - 1) / 2.0) * FileApart / len;
    }

    /// <summary>
    /// Sends ours out of the valley over a side, to the map: each to a spot of its own along the middle of it, and over the
    /// edge there into <see cref="Departed"/>. Another word given them takes it back. Returns how many go.
    /// </summary>
    public int Depart(IEnumerable<Unit> units, int side)
    {
        side = ((side % 6) + 6) % 6;
        var hex = Site.Hex ?? Hex.In(World.SX, World.SZ);
        var group = new List<Unit>();
        foreach (var u in units) if (!u.Dead && u.Team == Team.Player && u.T != Kinds.Risen) group.Add(u);
        // across the way out as they stand, so that nobody crosses another's way to the edge
        var (ix, iz) = hex.Inward(side);
        group.Sort((a, b) => (a.X * -iz + a.Z * ix).CompareTo(b.X * -iz + b.Z * ix));
        int n = 0;
        for (int i = 0; i < group.Count; i++)
        {
            var u = group[i];
            var (x, z) = EdgePoint(side, Along(hex, side, i, group.Count));
            int spot = NearestStand(x, z, u.T.Move.Height);
            if (spot < 0) continue;
            Command(new[] { u }, spot);
            u.LeaveBy = side;
            n++;
        }
        if (n > 0) Say($"Отряд уходит {EdgeToward(side)}: {n} {Plural(n, "человек", "человека", "человек")}.", 1, group[0]);
        return n;
    }

    /// <summary>The parties gone out of the valley since the map last took them; taken, they are the map's.</summary>
    public List<Party> TakeDeparted()
    {
        var list = new List<Party>(Departed);
        Departed.Clear();
        return list;
    }

    /// <summary>
    /// Those sent out at the edge go over it: off the valley and into the party of their side. Not one lying down, held,
    /// carried or carrying, nor the one the player is in (his way out of the valley is the map's to show).
    /// </summary>
    void TickDepartures()
    {
        if (Time < _nextDepartLook) return;
        _nextDepartLook = Time + .5;
        Hex hex = null;
        foreach (var u in Units)
        {
            if (u.LeaveBy < 0 || u.Dead || u.Inside || u.Airborne != null || u.Down || u.PinKg > 0 || u.CarriedBy != null || u.Carrying != null
                || u.Captor != null || u == Possessed) continue;
            hex ??= Site.Hex ?? Hex.In(World.SX, World.SZ);
            var (d, t) = hex.FromSide(u.LeaveBy, u.X, u.Z);
            if (d > Site.StripDepth + NavGrid.CS || t < 0 || t > 1) continue;
            GoneOver(u);
        }
    }

    void GoneOver(Unit u)
    {
        int side = u.LeaveBy;
        var party = Departed.Find(p => p.Side == side);
        if (party == null) Departed.Add(party = new Party { Side = side });
        party.Members.Add(Traveller.Of(u, CellKey));
        if (u.Captive != null) { u.Captive.Captor = null; u.Captive = null; }
        ReleaseSite(u);
        u.LeaveBy = -1;
        u.Dead = true; u.DiedAt = -999; u.State = UnitState.Dead; u.Path = null;
        if (!Units.Exists(o => !o.Dead && o.LeaveBy == side))
            Say($"Отряд ушёл из долины {EdgeToward(side)}: {party.Members.Count} {Plural(party.Members.Count, "человек", "человека", "человек")}.", 1);
    }

    /// <summary>
    /// The horde's town gets its garrison: these kinds standing at its posts — the huts' doors, the fire, the gap in the
    /// palisade — each keeping by its own spot, falling on whoever of ours comes near and going back after, as the camps
    /// at the mines do. Engines and those who only come raiding (miners, sappers, thieves) stay out of it. Returns them.
    /// </summary>
    /// <summary>Whether one of this kind stands in a garrison: not engines, nor those who only come raiding (miners, sappers, thieves).</summary>
    public static bool Holds(UnitType t) => !Kinds.IsEngine(t) && t != Kinds.Miner && t != Kinds.Sapper && t != Kinds.Thief;

    public List<Unit> Garrison(IReadOnlyList<UnitType> roster)
    {
        var list = new List<Unit>();
        var posts = Site.HordePosts.Count > 0 ? Site.HordePosts : new List<(int X, int Z)> { (Site.KeepX, Site.KeepZ) };
        int k = 0;
        foreach (var t in roster)
        {
            if (!Holds(t)) continue;
            var (px, pz) = posts[k++ % posts.Count];
            int c = NearestStand(px + Rng.Next(-6, 7), pz + Rng.Next(-6, 7), t.Move.Height);
            if (c < 0) continue;
            var u = Spawn(t, c);
            u.Guard = true; u.Home = c;
            list.Add(u);
        }
        return list;
    }

    /// <summary>
    /// The citadel (or the outpost's stone) down in a valley of the map: the valley is lost, not the game. The raid ends
    /// there — its raiders stay and hold the valley as a garrison, its bands are done — and the map is told; the valley
    /// runs on, its people where they are.
    /// </summary>
    void ValleyLost()
    {
        Lost = true; LostAt = Time; LostDay = Clock.Day;
        KeepHp = 0;
        CloseBattle(held: false);
        Raids._queue.Clear();
        foreach (var u in Units)
            if (!u.Dead && u.Team == Team.Horde && !u.Envoy && !u.Bound && u.Captor == null && !u.Inside)
            {
                u.Guard = true; u.Home = u.Cell; u.Raid = null; u.Path = null; u.Routed = false;
                StopCarve(u); StopBuild(u);
            }
        if (Raids.Active) { Raids.Active = false; Bands.RemoveAll(b => b.State != BandState.Coming); }
        Say(Site.HasKeep ? "Цитадель пала. Долина потеряна." : "Застава пала. Долина потеряна.", 3);
        ValleyFell?.Invoke();
    }

    // ------------------------------------------------------------ the save

    void WriteCampaign(BinaryWriter w)
    {
        w.Write(Campaign); w.Write(Lost); w.Write(LostAt); w.Write(LostDay); w.Write(Gate);
        var leaving = Units.FindAll(u => !u.Dead && u.LeaveBy >= 0);
        w.Write(leaving.Count);
        foreach (var u in leaving) { w.Write(u.Id); w.Write(u.LeaveBy); }
        w.Write(Departed.Count);
        foreach (var p in Departed) p.Write(w);
        var guests = Units.FindAll(u => !u.Dead && u.Billet >= 0);
        w.Write(guests.Count);
        foreach (var u in guests) { w.Write(u.Id); w.Write(u.Billet); }
    }

    void ReadCampaign(BinaryReader r)
    {
        Campaign = r.ReadBoolean(); Lost = r.ReadBoolean(); LostAt = r.ReadDouble(); LostDay = r.ReadInt32();
        int gate = r.ReadInt32();
        if (gate >= 0 && gate < Nav.Count) Gate = gate;
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            int id = r.ReadInt32(), side = r.ReadInt32();
            if (Units.Find(u => u.Id == id) is { } u) u.LeaveBy = side;
        }
        Departed.Clear();
        for (int n = r.ReadInt32(); n > 0; n--) Departed.Add(Party.Read(r));
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            int id = r.ReadInt32(), billet = r.ReadInt32();
            if (Units.Find(u => u.Id == id) is { } u) u.Billet = billet;
        }
    }
}
