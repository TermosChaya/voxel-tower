using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace VoxelTower.Core;

/// <summary>
/// A game written down and read back. The valley itself is made again from its seed; what a save keeps is what play
/// changed of it — every chunk written since it was made — the layers of what is meant, of arches and of orders, and
/// the state of the siege: the household and the horde with their wounds, the bands and the waves still to come, the
/// plots, the gates, the mines, the dressing of the buildings, what is burning, the stores and the clock. Ways, jobs
/// and aims are not kept: every pawn and raider takes them up again as it finds itself. Written through gzip.
/// </summary>
public sealed partial class Sim
{
    public const int SaveVersion = 1;
    internal static readonly byte[] SaveMagic = { (byte)'S', (byte)'V', (byte)'A', (byte)'L' };

    static Dictionary<string, UnitType> _kindByKey;

    internal static UnitType KindOf(string key)
    {
        if (_kindByKey == null)
        {
            _kindByKey = new Dictionary<string, UnitType>();
            foreach (var arr in new[] { Kinds.PlayerKinds, Kinds.HordeKinds, Kinds.WildKinds }) foreach (var t in arr) _kindByKey[t.Key] = t;
        }
        return _kindByKey.TryGetValue(key, out var k) ? k : null;
    }

    /// <summary>Writes the game: the valley made from <paramref name="seed"/> as play changed it, and all of the siege.
    /// The label is for the list of saves; whatever the screen keeps (the camera, the chronicle) goes in as one string.</summary>
    public void Save(Stream to, uint seed, string label, string extra)
    {
        using var z = new GZipStream(to, CompressionLevel.Fastest, leaveOpen: true);
        using var w = new BinaryWriter(z, Encoding.UTF8, leaveOpen: true);
        w.Write(SaveMagic); w.Write(SaveVersion); w.Write(seed); w.Write(label ?? ""); w.Write(Time);
        // the valley as play changed it, what is meant of it and its arches
        WriteChunks(w, World, edited: true);
        WriteChunks(w, Design, edited: false);
        WriteChunks(w, Arched, edited: false);
        WriteChunks(w, Orders.Layer, edited: false);

        w.Write(KeepHp); w.Write(Over); w.Write(Won);
        w.Write(_nextId); w.Write(_nextGate); w.Write(_nextZone); w.Write(_nextBand);
        foreach (var st in new[] { Stock, HordeStock }) { w.Write(st.Gold); for (int k = 1; k < 4; k++) w.Write(st[k]); }
        w.Write(Kills); w.Write(Shots); w.Write(ShotHits); w.Write(Hunted); w.Write(WildTarget); w.Write(_nextHerd);
        w.Write(FortArea.x0); w.Write(FortArea.z0); w.Write(FortArea.x1); w.Write(FortArea.z1);
        w.Write(FortGate?.Id ?? -1);

        w.Write(Zones.Count);
        foreach (var zn in Zones)
        {
            w.Write(zn.Id); w.Write(zn.X0); w.Write(zn.Z0); w.Write(zn.X1); w.Write(zn.Z1); w.Write(zn.Kind); w.Write(zn.Depth);
            w.Write(zn.Marked); w.Write(zn.Auto);
            w.Write(zn.Floor?.Length ?? -1);
            if (zn.Floor != null) foreach (int f in zn.Floor) w.Write(f);
        }
        w.Write(Gates.Count);
        foreach (var g in Gates)
        {
            w.Write(g.Id); w.Write(g.AlongX); w.Write(g.In); w.Write(g.U0); w.Write(g.P); w.Write(g.Y); w.Write((byte)g.Mode);
            w.Write(g.Open); w.Write(g.Want); w.Write(g.Ready); w.Write(g.Broken); w.Write(g.Missing); w.Write(g.Step);
            for (int i = 0; i < Gateway.Voxels; i++) { w.Write(g.At[i]); w.Write(g.Gone[i]); }
        }
        w.Write(Mines.Count);
        foreach (var m in Mines) { w.Write((byte)m.Holder); w.Write(m.Grip); }
        w.Write(Fixtures.Count);
        foreach (var f in Fixtures)
        {
            w.Write((byte)f.Kind); w.Write(f.Anchor); w.Write(f.LX); w.Write(f.LY); w.Write(f.LZ);
            w.Write(f.Voxels.Count);
            foreach (var (x, y, zz, m) in f.Voxels) { w.Write(x); w.Write(y); w.Write(zz); w.Write(m); }
        }
        w.Write(Burning.Count);
        foreach (var (p, fuel) in Burning) { w.Write(p); w.Write(fuel); }
        w.Write(_relay.Count);
        foreach (int p in _relay) w.Write(p);

        var living = Units.FindAll(u => !u.Dead);
        w.Write(living.Count);
        foreach (var u in living) WriteUnit(w, u);
        w.Write(Bands.Count);
        foreach (var b in Bands) WriteBand(w, b);
        w.Write(Waves.Number); w.Write(Waves.Active); w.Write(Waves.Countdown); w.Write(Waves.Intel); w.Write(Waves._spawnT);
        w.Write(Waves._lastBand?.Id ?? -1);
        w.Write(Waves._queue.Count);
        foreach (var (kind, band, back) in Waves._queue) { w.Write(kind.Key); w.Write(band?.Id ?? -1); WriteSurvivor(w, back); }
        w.Write(Escaped.Count);
        foreach (var s in Escaped) WriteSurvivor(w, s);
        w.Write(extra ?? "");
    }

    static void WriteChunks(BinaryWriter w, VoxelWorld v, bool edited)
    {
        var list = new List<int>();
        for (int ci = 0; ci < v.ChunkCount; ci++) if (edited ? v.Changed(ci) : !v.IsEmpty(ci)) list.Add(ci);
        w.Write(list.Count);
        foreach (int ci in list)
        {
            w.Write(ci);
            var d = v.ChunkData(ci);
            if (d == null) { w.Write((byte)0); w.Write(v.ChunkUniform(ci)); }
            else { w.Write((byte)1); w.Write(d); }
        }
    }

    internal static void ReadChunks(BinaryReader r, VoxelWorld v)
    {
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            int ci = r.ReadInt32();
            if (r.ReadByte() == 0) v.SetChunk(ci, null, r.ReadByte());
            else v.SetChunk(ci, r.ReadBytes(VoxelWorld.CS3), 0);
        }
    }

    void WriteUnit(BinaryWriter w, Unit u)
    {
        w.Write(u.Id); w.Write(u.T.Key); w.Write(u.Look);
        w.Write(u.X); w.Write(u.Y); w.Write(u.Z); w.Write(u.Facing); w.Write(u.Cell); w.Write(u.OffX); w.Write(u.OffZ); w.Write(u.Pace);
        w.Write(u.Hp); w.Write(u.MaxHp);
        w.Write(u.Hold); w.Write(u.Dest); w.Write(u.Post); w.Write(u.Home);
        w.Write(u.Duty); w.Write(u.DutyZone?.Id ?? -1); w.Write(u.Militia);
        w.Write((byte)u.Priority.Length); foreach (byte p in u.Priority) w.Write(p);
        w.Write(u.FireArrows); w.Write(u.KindleAt); w.Write(u.Torch); w.Write(u.Burn); w.Write(u.BurnHeat);
        w.Write(u.Guard); w.Write(u.Raid == null ? -1 : Mines.IndexOf(u.Raid)); w.Write(u.Band?.Id ?? -1); w.Write(u.Leader);
        w.Write(u.Routed); w.Write(u.Returned); w.Write(u.Mending); w.Write(u.Sapped);
        WriteBody(w, u.Wounds);
    }

    static void WriteSurvivor(BinaryWriter w, Survivor s)
    {
        w.Write(s != null);
        if (s == null) return;
        w.Write(s.Kind.Key); w.Write(s.Look); w.Write(s.Hp);
        WriteBody(w, s.Wounds);
    }

    static void WriteBody(BinaryWriter w, BodyState b)
    {
        w.Write(b != null);
        if (b == null) return;
        w.Write(b.Rev); w.Write(b.MainHand); w.Write(b.OffHand); w.Write(b.Shield); w.Write(b.Legs); w.Write(b.Bleed);
        w.Write(b.Wounds.Count);
        foreach (var s in b.Wounds) w.Write(s);
        w.Write(b.Arrows.Count);
        foreach (var (part, x, y, z, dx, dy, dz) in b.Arrows) { w.Write((byte)part); w.Write(x); w.Write(y); w.Write(z); w.Write(dx); w.Write(dy); w.Write(dz); }
        for (int p = 0; p < BodyPlan.Parts; p++)
        {
            var g = b.Grid[p]; var o = b.Plan.Grid[p];
            if (g == null) { w.Write((byte)0); continue; }
            if (o != null && g.Count == o.Count && g.M.AsSpan().SequenceEqual(o.M) && g.T.AsSpan().SequenceEqual(o.T)) { w.Write((byte)1); continue; }
            w.Write((byte)2);
            w.Write(g.X0); w.Write(g.Y0); w.Write(g.Z0); w.Write(g.SX); w.Write(g.SY); w.Write(g.SZ); w.Write(g.Count);
            w.Write(g.M.Length); w.Write(g.M); w.Write(g.T);
        }
    }

    void WriteBand(BinaryWriter w, Warband b)
    {
        w.Write(b.Id); w.Write(b.Name); w.Write((byte)b.State); w.Write(b.Since); w.Write(b.Entry);
        w.Write(b.Roster.Count); foreach (var t in b.Roster) w.Write(t.Key);
        w.Write(b.Veterans.Count); foreach (var v in b.Veterans) WriteSurvivor(w, v);
        var members = b.Members.FindAll(u => !u.Dead);
        w.Write(members.Count); foreach (var u in members) w.Write(u.Id);
        w.Write(b.Leader is { Dead: false } l ? l.Id : -1);
        w.Write(b.Spawned); w.Write(b.Target == null ? -1 : Mines.IndexOf(b.Target));
        w.Write(b.Side); w.Write(b.RallyCell); w.Write(b.Why ?? "");
        w.Write(b.Hunch?.Length ?? -1); if (b.Hunch != null) foreach (float h in b.Hunch) w.Write(h);
        w.Write(b.LostLeader); w.Write(b.Broken); w.Write(b.HX); w.Write(b.HZ);
        w.Write(b.History.Count); foreach (var s in b.History) w.Write((byte)s);
    }

    /// <summary>Reads the siege back, after the valley (<see cref="SaveReader.Valley"/>) and a new sim on it; returns what
    /// the screen kept.</summary>
    public string Load(SaveReader save)
    {
        var r = save.R;
        Time = save.Time;
        Orders.Restore(r);
        KeepHp = r.ReadSingle(); Over = r.ReadBoolean(); Won = r.ReadBoolean();
        _nextId = r.ReadInt32(); _nextGate = r.ReadInt32(); _nextZone = r.ReadInt32(); _nextBand = r.ReadInt32();
        foreach (var st in new[] { Stock, HordeStock }) { st.Gold = r.ReadDouble(); for (int k = 1; k < 4; k++) st[k] = r.ReadDouble(); }
        Kills = r.ReadInt32(); Shots = r.ReadInt32(); ShotHits = r.ReadInt32(); Hunted = r.ReadInt32(); WildTarget = r.ReadInt32(); _nextHerd = r.ReadDouble();
        FortArea = (r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        int fortGate = r.ReadInt32();

        Zones.Clear();
        int nz = r.ReadInt32();
        for (int i = 0; i < nz; i++)
        {
            var zn = new Zone { Id = r.ReadInt32(), X0 = r.ReadInt32(), Z0 = r.ReadInt32(), X1 = r.ReadInt32(), Z1 = r.ReadInt32(), Kind = r.ReadInt32(), Depth = r.ReadInt32() };
            zn.Marked = r.ReadInt32(); zn.Auto = r.ReadBoolean();
            int fl = r.ReadInt32();
            if (fl >= 0) { zn.Floor = new int[fl]; for (int k = 0; k < fl; k++) zn.Floor[k] = r.ReadInt32(); }
            Zones.Add(zn);
        }
        ZonesVersion++;
        Gates.Clear();
        int ng = r.ReadInt32();
        for (int i = 0; i < ng; i++)
        {
            var g = new Gateway { Id = r.ReadInt32(), AlongX = r.ReadBoolean(), In = r.ReadInt32(), U0 = r.ReadInt32(), P = r.ReadInt32(), Y = r.ReadInt32() };
            g.Mode = (GateMode)r.ReadByte(); g.Open = r.ReadSingle(); g.Want = r.ReadSingle(); g.Ready = r.ReadBoolean(); g.Broken = r.ReadBoolean();
            g.Missing = r.ReadInt32(); g.Step = r.ReadInt32();
            for (int k = 0; k < Gateway.Voxels; k++) { g.At[k] = r.ReadInt32(); g.Gone[k] = r.ReadBoolean(); }
            SweepOf(g);
            Gates.Add(g);
        }
        FortGate = Gates.Find(g => g.Id == fortGate);
        int nm = r.ReadInt32();
        for (int i = 0; i < nm; i++)
        {
            var holder = (Holder)r.ReadByte(); float grip = r.ReadSingle();
            if (i < Mines.Count) { Mines[i].Holder = holder; Mines[i].Grip = grip; }
        }
        Fixtures.Clear();
        int nf = r.ReadInt32();
        for (int i = 0; i < nf; i++)
        {
            var f = new Fixture { Kind = (FixtureKind)r.ReadByte(), Anchor = r.ReadInt32(), LX = r.ReadSingle(), LY = r.ReadSingle(), LZ = r.ReadSingle() };
            int nv = r.ReadInt32();
            for (int k = 0; k < nv; k++) f.Voxels.Add((r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadByte()));
            Fixtures.Add(f);
        }
        Burning.Clear();
        int nb = r.ReadInt32();
        for (int i = 0; i < nb; i++) Burning[r.ReadInt32()] = r.ReadSingle();
        _relay.Clear();
        int nr = r.ReadInt32();
        for (int i = 0; i < nr; i++) _relay.Add(r.ReadInt32());

        Units.Clear();
        var byId = new Dictionary<int, Unit>();
        var bandOf = new Dictionary<Unit, int>();
        int nu = r.ReadInt32();
        for (int i = 0; i < nu; i++)
        {
            var u = ReadUnit(r, out int band);
            if (u == null) continue;
            Units.Add(u);
            byId[u.Id] = u;
            if (band >= 0) bandOf[u] = band;
        }
        Bands.Clear();
        int nbands = r.ReadInt32();
        for (int i = 0; i < nbands; i++) Bands.Add(ReadBand(r, byId));
        foreach (var (u, id) in bandOf) u.Band = Bands.Find(b => b.Id == id);
        Waves.Number = r.ReadInt32(); Waves.Active = r.ReadBoolean(); Waves.Countdown = r.ReadDouble(); Waves.Intel = r.ReadDouble(); Waves._spawnT = r.ReadDouble();
        int last = r.ReadInt32();
        Waves._lastBand = Bands.Find(b => b.Id == last);
        Waves._queue.Clear();
        int nq = r.ReadInt32();
        for (int i = 0; i < nq; i++)
        {
            var kind = KindOf(r.ReadString()); int band = r.ReadInt32(); var back = ReadSurvivor(r);
            if (kind != null) Waves._queue.Enqueue((kind, Bands.Find(b => b.Id == band), back));
        }
        Escaped.Clear();
        int ne = r.ReadInt32();
        for (int i = 0; i < ne; i++) { var s = ReadSurvivor(r); if (s != null) Escaped.Add(s); }
        string extra = r.ReadString();
        // the ways of the horde are worked out again for the valley as it now is
        _fieldVersion = -1;
        SidesRev++;
        return extra;
    }

    Unit ReadUnit(BinaryReader r, out int band)
    {
        int id = r.ReadInt32();
        var t = KindOf(r.ReadString());
        var u = new Unit { Id = id, T = t, Look = r.ReadInt32() };
        u.X = r.ReadSingle(); u.Y = r.ReadSingle(); u.Z = r.ReadSingle(); u.Facing = r.ReadSingle(); u.Cell = r.ReadInt32();
        u.OffX = r.ReadSingle(); u.OffZ = r.ReadSingle(); u.Pace = r.ReadSingle();
        u.Hp = r.ReadSingle(); u.MaxHp = r.ReadSingle();
        u.Hold = r.ReadBoolean(); u.Dest = r.ReadInt32(); u.Post = r.ReadInt32(); u.Home = r.ReadInt32();
        u.Duty = r.ReadInt32(); int zone = r.ReadInt32(); u.Militia = r.ReadBoolean();
        int np = r.ReadByte();
        for (int k = 0; k < np; k++) { byte p = r.ReadByte(); if (k < u.Priority.Length) u.Priority[k] = p; }
        u.FireArrows = r.ReadBoolean(); u.KindleAt = r.ReadInt32(); u.Torch = r.ReadBoolean(); u.Burn = r.ReadSingle(); u.BurnHeat = r.ReadSingle();
        u.Guard = r.ReadBoolean(); int raid = r.ReadInt32(); band = r.ReadInt32(); u.Leader = r.ReadBoolean();
        u.Routed = r.ReadBoolean(); u.Returned = r.ReadBoolean(); u.Mending = r.ReadBoolean(); u.Sapped = r.ReadInt32();
        var body = ReadBody(r, t, u.Look);
        if (t == null) return null;
        u.Wounds = body;
        u.DutyZone = Zones.Find(z => z.Id == zone);
        u.Raid = raid >= 0 && raid < Mines.Count ? Mines[raid] : null;
        return u;
    }

    static Survivor ReadSurvivor(BinaryReader r)
    {
        if (!r.ReadBoolean()) return null;
        var t = KindOf(r.ReadString());
        int look = r.ReadInt32(); float hp = r.ReadSingle();
        var body = ReadBody(r, t, look);
        return t == null ? null : new Survivor { Kind = t, Look = look, Hp = hp, Wounds = body };
    }

    static BodyState ReadBody(BinaryReader r, UnitType t, int look)
    {
        if (!r.ReadBoolean()) return null;
        var b = t == null ? null : new BodyState(Bodies.Plan(t, look));
        int rev = r.ReadInt32(); bool main = r.ReadBoolean(), off = r.ReadBoolean(), shield = r.ReadBoolean(); int legs = r.ReadInt32(); float bleed = r.ReadSingle();
        var wounds = new List<string>();
        int nw = r.ReadInt32();
        for (int i = 0; i < nw; i++) wounds.Add(r.ReadString());
        var arrows = new List<(BodyPart, float, float, float, float, float, float)>();
        int na = r.ReadInt32();
        for (int i = 0; i < na; i++) arrows.Add(((BodyPart)r.ReadByte(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
        var grids = new PartGrid[BodyPlan.Parts];
        var kept = new bool[BodyPlan.Parts];
        for (int p = 0; p < BodyPlan.Parts; p++)
        {
            byte how = r.ReadByte();
            if (how == 1) { kept[p] = true; continue; }
            if (how == 0) continue;
            var g = new PartGrid { X0 = r.ReadInt32(), Y0 = r.ReadInt32(), Z0 = r.ReadInt32(), SX = r.ReadInt32(), SY = r.ReadInt32(), SZ = r.ReadInt32(), Count = r.ReadInt32() };
            int len = r.ReadInt32();
            g.M = r.ReadBytes(len); g.T = r.ReadBytes(len);
            grids[p] = g;
        }
        if (b == null) return null;
        b.Rev = rev + 1; b.MainHand = main; b.OffHand = off; b.Shield = shield; b.Legs = legs; b.Bleed = bleed;
        b.Wounds.AddRange(wounds);
        b.Arrows.AddRange(arrows);
        for (int p = 0; p < BodyPlan.Parts; p++) if (!kept[p]) b.Grid[p] = grids[p];
        return b;
    }

    Warband ReadBand(BinaryReader r, Dictionary<int, Unit> byId)
    {
        var b = new Warband { Id = r.ReadInt32(), Name = r.ReadString(), State = (BandState)r.ReadByte(), Since = r.ReadDouble(), Entry = r.ReadInt32() };
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++) { var t = KindOf(r.ReadString()); if (t != null) b.Roster.Add(t); }
        n = r.ReadInt32();
        for (int i = 0; i < n; i++) { var v = ReadSurvivor(r); if (v != null) b.Veterans.Add(v); }
        n = r.ReadInt32();
        for (int i = 0; i < n; i++) if (byId.TryGetValue(r.ReadInt32(), out var u)) b.Members.Add(u);
        int leader = r.ReadInt32();
        b.Leader = byId.GetValueOrDefault(leader);
        b.Spawned = r.ReadInt32();
        int target = r.ReadInt32();
        b.Target = target >= 0 && target < Mines.Count ? Mines[target] : null;
        b.Side = r.ReadInt32(); b.RallyCell = r.ReadInt32(); b.Why = r.ReadString();
        int nh = r.ReadInt32();
        if (nh >= 0) { b.Hunch = new float[nh]; for (int i = 0; i < nh; i++) b.Hunch[i] = r.ReadSingle(); }
        b.LostLeader = r.ReadBoolean(); b.Broken = r.ReadBoolean(); b.HX = r.ReadSingle(); b.HZ = r.ReadSingle();
        b.History.Clear();
        n = r.ReadInt32();
        for (int i = 0; i < n; i++) b.History.Add((BandState)r.ReadByte());
        b.TrailRev = -1;
        return b;
    }
}

/// <summary>
/// A saved game being read back, in the order it was written: the header (seed, label, game time) at once, so a list
/// of saves can show them; then the valley, into a world made again from the seed (<see cref="Valley"/>); then, once a
/// sim stands on it, the siege (<see cref="Sim.Load"/>).
/// </summary>
public sealed class SaveReader : IDisposable
{
    readonly GZipStream _z;
    public readonly BinaryReader R;
    public readonly uint Seed;
    public readonly string Label;
    public readonly double Time;

    public SaveReader(Stream from)
    {
        _z = new GZipStream(from, CompressionMode.Decompress, leaveOpen: false);
        R = new BinaryReader(_z, Encoding.UTF8);
        var magic = R.ReadBytes(4);
        if (magic.Length != 4 || magic[0] != Sim.SaveMagic[0] || magic[1] != Sim.SaveMagic[1] || magic[2] != Sim.SaveMagic[2] || magic[3] != Sim.SaveMagic[3])
            throw new InvalidDataException("not a saved game");
        int version = R.ReadInt32();
        if (version != Sim.SaveVersion) throw new InvalidDataException($"a save of version {version}, not {Sim.SaveVersion}");
        Seed = R.ReadUInt32(); Label = R.ReadString(); Time = R.ReadDouble();
    }

    /// <summary>What play changed of the valley, into the world made again from the seed; what is meant of it and its arches into the site.</summary>
    public void Valley(VoxelWorld world, Site site)
    {
        Sim.ReadChunks(R, world);
        site.Design = new VoxelWorld(world.SX, world.SY, world.SZ);
        Sim.ReadChunks(R, site.Design);
        site.Arched = new VoxelWorld(world.SX, world.SY, world.SZ);
        Sim.ReadChunks(R, site.Arched);
    }

    public void Dispose() { R.Dispose(); _z.Dispose(); }
}
