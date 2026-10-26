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
    /// <summary>20: the bodies sculpted finer, their half-voxels (and so a wounded body's) not what they were.</summary>
    public const int SaveVersion = 60;
    internal static readonly byte[] SaveMagic = { (byte)'S', (byte)'V', (byte)'A', (byte)'L' };

    static Dictionary<string, UnitType> _kindByKey;

    internal static UnitType KindOf(string key)
    {
        if (_kindByKey == null)
        {
            _kindByKey = new Dictionary<string, UnitType>();
            foreach (var t in Kinds.All()) _kindByKey[t.Key] = t;
        }
        return _kindByKey.TryGetValue(key, out var k) ? k : null;
    }

    /// <summary>Writes the game: the valley made from <paramref name="seed"/> as play changed it, and all of the siege.
    /// The label is for the list of saves; whatever the screen keeps (the camera, the chronicle) goes in as one string.</summary>
    public void Save(Stream to, uint seed, string label, string extra)
    {
        // a tree going over is laid down where it is going, and what of a felled crown hangs on nothing comes down (as it
        // would a moment later, and does at once when the game is read back): the save keeps nothing in the air
        LandToppling(all: true);
        DropStrayLeaves();
        using var z = new GZipStream(to, CompressionLevel.Fastest, leaveOpen: true);
        using var w = new BinaryWriter(z, Encoding.UTF8, leaveOpen: true);
        w.Write(SaveMagic); w.Write(SaveVersion); w.Write(seed);
        // the valley's kind and shape, made again from them with the seed (version 40); its size and the side the horde
        // comes by (version 45)
        w.Write((byte)Site.Kind); w.Write(Site.Classic); w.Write(Site.Hex != null);
        w.Write(World.SX); w.Write(World.SZ); w.Write((byte)Site.EntrySide);
        // what holds it, and its sides as the map of valleys gave them (version 60)
        w.Write((byte)Site.Holding);
        w.Write((byte)(Site.Edges?.Length ?? 0));
        if (Site.Edges != null) foreach (var e in Site.Edges) { w.Write(e?.Seed ?? 0); w.Write(e?.River ?? -1); w.Write(e?.Road ?? -1); }
        w.Write(label ?? ""); w.Write(Time);
        // the valley as play changed it, what is meant of it and its arches
        WriteChunks(w, World, edited: true);
        WriteChunks(w, Design, edited: false);
        WriteChunks(w, Arched, edited: false);
        WriteChunks(w, Orders.Layer, edited: false);

        w.Write(KeepHp); w.Write(Over); w.Write(Won);
        w.Write(_nextId); w.Write(_nextGate); w.Write(_nextZone); w.Write(_nextBand);
        foreach (var st in new[] { Stock, HordeStock }) { w.Write(st.Gold); for (int k = 1; k < Stockpile.Kinds; k++) w.Write(st[k]); }
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
            w.Write(g.Id); w.Write(g.AlongX); w.Write(g.In); w.Write(g.U0); w.Write(g.P); w.Write(g.Y); w.Write((byte)g.Mode); w.Write(g.Iron);
            w.Write(g.Open); w.Write(g.Want); w.Write(g.Ready); w.Write(g.Broken); w.Write(g.Missing); w.Write(g.Step);
            for (int i = 0; i < Gateway.Voxels; i++) { w.Write(g.At[i]); w.Write(g.Gone[i]); }
        }
        w.Write(_nextDrawbridge); w.Write(Drawbridges.Count);
        foreach (var b in Drawbridges)
        {
            w.Write(b.Id); w.Write(b.AlongX); w.Write(b.Dir); w.Write(b.Hinge); w.Write(b.U0); w.Write(b.Length); w.Write(b.Width); w.Write(b.Y);
            w.Write((byte)b.Mode); w.Write(b.Raise); w.Write(b.Want); w.Write(b.Ready); w.Write(b.Broken); w.Write(b.Missing); w.Write(b.Step);
            for (int i = 0; i < b.Voxels; i++) { w.Write(b.At[i]); w.Write(b.Gone[i]); }
        }
        w.Write(Mines.Count);
        foreach (var m in Mines) { w.Write((byte)m.Holder); w.Write(m.Grip); }
        w.Write(Fixtures.Count);
        foreach (var f in Fixtures)
        {
            w.Write((byte)f.Kind); w.Write(f.Anchor); w.Write(f.LX); w.Write(f.LY); w.Write(f.LZ); w.Write(f.DarkUntil);
            w.Write(f.Voxels.Count);
            foreach (var (x, y, zz, m) in f.Voxels) { w.Write(x); w.Write(y); w.Write(zz); w.Write(m); }
        }
        w.Write(Burning.Count);
        foreach (var (p, fuel) in Burning) { w.Write(p); w.Write(fuel); }
        w.Write(_relay.Count);
        foreach (int p in _relay) w.Write(p);

        var living = Units.FindAll(u => !u.Dead);
        // the warlords first: the units, the bands and those who got away point at them (version 59)
        WriteLords(w, living);
        w.Write(living.Count);
        foreach (var u in living) WriteUnit(w, u);
        w.Write(Bands.Count);
        foreach (var b in Bands) WriteBand(w, b);
        w.Write(Raids.Count); w.Write(Raids.Active); w.Write(Clock.Left); w.Write(Raids.Intel); w.Write(Raids._spawnT); w.Write(Clock.DawnLeft); w.Write(Alarm);
        w.Write((byte)Weather); w.Write(WeatherLeft); w.Write(_nextWeather); w.Write(_nextBeast); w.Write(_nextEnvoy); w.Write(Angered);
        w.Write(Offer != null); if (Offer != null) { w.Write((byte)Offer.Kind); w.Write(Offer.Store); w.Write(Offer.Cost); w.Write(Offer.Envoy?.Id ?? -1); w.Write(Offer.Until); }
        w.Write(Favour); w.Write(Revelations); w.Write(MournUntil);
        w.Write(Carcasses.Count); foreach (var t in Carcasses) w.Write(t.Key);
        // the spells known and when each is ready; the bogs, the red moon, the eye
        w.Write(Known.Count); foreach (var k in Known) w.Write((byte)k);
        foreach (double t in _spellReady) w.Write(t);
        w.Write(Mires.Count); foreach (var m in Mires) { w.Write(m.X); w.Write(m.Z); w.Write(m.R); w.Write(m.Until); }
        w.Write(BloodMoonUntil); w.Write(EyeUntil);
        w.Write(Curses.Count); foreach (var c in Curses) { w.Write(c.X); w.Write(c.Y); w.Write(c.Z); w.Write(c.R); w.Write(c.Until); }
        foreach (var list in new[] { Skins, DeadFogs })
        {
            w.Write(list.Count);
            foreach (var p in list) { w.Write(p.X); w.Write(p.Y); w.Write(p.Z); w.Write(p.R); w.Write(p.Until); }
        }
        // powder about to go up, and the sappers' fuses burning down
        w.Write(Charges.Count);
        foreach (var c in Charges)
        {
            w.Write(c.X); w.Write(c.Y); w.Write(c.Z); w.Write(c.BlowAt); w.Write(c.Planted); w.Write(c.Cutting); w.Write(c.Cause);
            w.Write(c.Voxels.Count); foreach (int p in c.Voxels) w.Write(p);
        }
        // pieces of the world in the air, going over or rolling, each on its way as it was (one thrown bodily is kept as
        // itself, standing where it was, and comes down from there)
        int pieces = 0;
        foreach (var f in Fliers) if (f.Unit == null) pieces++;
        w.Write(pieces);
        foreach (var f in Fliers)
        {
            if (f.Unit != null) continue;
            w.Write(f.X); w.Write(f.Y); w.Write(f.Z); w.Write(f.VX); w.Write(f.VY); w.Write(f.VZ);
            w.Write(f.AX); w.Write(f.AY); w.Write(f.AZ); w.Write(f.Turn); w.Write(f.Spin); w.Write(f.R); w.Write(f.Since);
            w.Write(f.Cause); w.Write(f.Burning); w.Write(f.Smash);
            w.Write(f.Tipping); w.Write(f.PX); w.Write(f.PY); w.Write(f.PZ); w.Write(f.CX0); w.Write(f.CY0); w.Write(f.CZ0); w.Write(f.TipFrom); w.Write(f.Tipped);
            w.Write(f.Rolling); w.Write(f.RollFrom); w.Write(f.RollR); w.Write(f.Still);
            w.Write(f.RollAX); w.Write(f.RollAZ); w.Write(f.RollH); w.Write(f.Marked); w.Write(f.Intact); w.Write(f.Star);
            w.Write(f.Voxels.Count);
            foreach (var (dx, dy, dz, m) in f.Voxels) { w.Write(dx); w.Write(dy); w.Write(dz); w.Write(m); }
        }
        w.Write(Knowledge); w.Write(Learnt.Count);
        foreach (string k in Learnt) w.Write(k);
        w.Write(Raids._lastBand?.Id ?? -1);
        w.Write(Raids._queue.Count);
        foreach (var (kind, band, back) in Raids._queue) { w.Write(kind.Key); w.Write(band?.Id ?? -1); WriteSurvivor(w, back); }
        w.Write(Escaped.Count);
        foreach (var s in Escaped) WriteSurvivor(w, s);
        w.Write(Whirls.Count);
        foreach (var wh in Whirls) { w.Write(wh.X); w.Write(wh.Z); w.Write(wh.VX); w.Write(wh.VZ); w.Write(wh.R); w.Write(wh.Until); w.Write(wh.Next); }
        WriteProgress(w);
        WriteDawn(w);
        WriteBattles(w);
        WriteChapters(w);
        WriteHouseDoors(w);
        w.Write(Sandbox); w.Write(Source is OnCallRaids);
        w.Write(Unfolds); w.Write(LetOut.Count); foreach (var k in LetOut) w.Write(k);
        WriteAcid(w);
        w.Write(Cracks.Count); foreach (var (p, c) in Cracks) { w.Write(p); w.Write(c); }
        WriteRelays(w);
        WriteStoreys(w);
        WriteScaffolds(w);
        WriteTunnels(w);
        WriteTown(w);
        WriteYards(w);
        WriteLiquids(w);
        WriteLava(w);
        WriteStanding(w);
        WriteRaids(w);
        WriteCaptains(w);
        WriteCampaign(w);
        w.Write(OreStruck); w.Write(MetalMined);
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
        w.Write(u.Back); w.Write(u.Focus?.Id ?? -1);
        w.Write(u.Duty); w.Write(u.DutyZone?.Id ?? -1); w.Write(u.Militia); w.Write(u.Inside);
        w.Write((byte)u.Priority.Length); foreach (byte p in u.Priority) w.Write(p);
        w.Write(u.FireArrows); w.Write(u.KindleAt); w.Write(u.Torch); w.Write(u.Burn); w.Write(u.BurnHeat);
        w.Write(u.Guard); w.Write(u.Raid == null ? -1 : Mines.IndexOf(u.Raid)); w.Write(u.Band?.Id ?? -1); w.Write(u.Leader);
        w.Write(u.Routed); w.Write(u.Returned); w.Write(u.Mending); w.Write(u.Sapped);
        w.Write(u.LootKind); w.Write(u.Loot); w.Write(u.Captor?.Id ?? -1);
        w.Write(u.Escort?.Id ?? -1); w.Write(u.AimAt);
        w.Write(u.Docked.Count); foreach (int p in u.Docked) w.Write(p);
        w.Write(u.Envoy);
        w.Write(u.TakeAlive); w.Write(u.Bound); w.Write(u.BoundAt); w.Write(u.Unguarded); w.Write(u.ChainedUntil); w.Write(u.CrumbleAt);
        w.Write(u.PlagueUntil); w.Write(u.OathUntil); w.Write(u.NextHeal); w.Write(u.NextCurse); w.Write(u.Charge);
        w.Write(u.Champion); w.Write(u.Title ?? "");
        w.Write(u.Lord?.Id ?? -1); w.Write(u.CryReady); w.Write(u.SlamReady); w.Write(u.Abandoned);
        // a sacrifice under way
        w.Write(u.Rite != null);
        if (u.Rite is { } rite)
        {
            w.Write((byte)rite.Kind); w.Write(rite.Victim?.Id ?? -1); w.Write(rite.Beast?.Key ?? ""); w.Write(rite.Spot); w.Write(rite.Stage); w.Write(rite.Done);
        }
        WriteBody(w, u.Wounds);
        w.Write(u.PinKg); w.Write(u.PinnedSince);
    }

    static void WriteSurvivor(BinaryWriter w, Survivor s)
    {
        w.Write(s != null);
        if (s == null) return;
        w.Write(s.Kind.Key); w.Write(s.Look); w.Write(s.Hp); w.Write(s.Champion); w.Write(s.Title ?? ""); w.Write(s.Lord?.Id ?? -1);
        WriteBody(w, s.Wounds);
    }

    internal static void WriteBody(BinaryWriter w, BodyState b)
    {
        w.Write(b != null);
        if (b == null) return;
        w.Write(b.Rev); w.Write(b.MainHand); w.Write(b.OffHand); w.Write(b.Shield); w.Write(b.Legs); w.Write(b.Bleed);
        w.Write((byte)b.Broken); w.Write((byte)b.Splinted); foreach (float k in b.Knit) w.Write(k);
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
        w.Write(b.Id); w.Write(b.Name); w.Write((byte)b.State); w.Write(b.Since); w.Write(b.Entry); w.Write(b.Champion); w.Write(b.Title ?? "");
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
        w.Write(b.Lord?.Id ?? -1); w.Write(b.LordsOwn); w.Write(b.Spirit); w.Write(b.Wavered); w.Write(b.Taken);
    }

    /// <summary>Every warlord anything in the valley points at: in it, at the head of a band, still to come, got away.</summary>
    void WriteLords(BinaryWriter w, List<Unit> living)
    {
        var lords = new List<Warlord>();
        void Add(Warlord l) { if (l != null && !lords.Contains(l)) lords.Add(l); }
        foreach (var u in living) Add(u.Lord);
        foreach (var b in Bands) { Add(b.Lord); foreach (var v in b.Veterans) Add(v.Lord); }
        foreach (var (_, _, back) in Raids._queue) Add(back?.Lord);
        foreach (var s in Escaped) Add(s.Lord);
        w.Write(_nextLord);
        w.Write(lords.Count);
        foreach (var l in lords)
        {
            w.Write(l.Id); w.Write(l.Name); w.Write(l.Of); w.Write(l.Kind.Key); w.Write(l.Level); w.Write(l.Xp); w.Write(l.Health);
            w.Write((byte)l.Fate); w.Write(l.Champion); w.Write(l.Slew); w.Write(l.Broke);
        }
    }

    void ReadLords(BinaryReader r)
    {
        _lordById.Clear();
        _nextLord = r.ReadInt32();
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var l = new Warlord { Id = r.ReadInt32(), Name = r.ReadString(), Of = r.ReadString() };
            l.Kind = KindOf(r.ReadString()) ?? Kinds.Orc;
            l.Level = r.ReadInt32(); l.Xp = r.ReadSingle(); l.Health = r.ReadSingle(); l.Fate = (LordFate)r.ReadByte(); l.Champion = r.ReadBoolean();
            l.Slew = r.ReadInt32(); l.Broke = r.ReadInt32();
            _lordById[l.Id] = l;
        }
    }

    readonly Dictionary<int, Warlord> _lordById = new();
    Warlord LordById(int id) => id >= 0 ? _lordById.GetValueOrDefault(id) : null;

    /// <summary>Reads the siege back, after the valley (<see cref="SaveReader.Valley"/>) and a new sim on it; returns what
    /// the screen kept.</summary>
    readonly Dictionary<Unit, int> _captorOf = new(), _escortOf = new(), _victimOf = new();

    public string Load(SaveReader save)
    {
        var r = save.R;
        Time = save.Time;
        Orders.Restore(r);
        KeepHp = r.ReadSingle(); Over = r.ReadBoolean(); Won = r.ReadBoolean();
        _nextId = r.ReadInt32(); _nextGate = r.ReadInt32(); _nextZone = r.ReadInt32(); _nextBand = r.ReadInt32();
        foreach (var st in new[] { Stock, HordeStock }) { st.Gold = r.ReadDouble(); for (int k = 1; k < Stockpile.Kinds; k++) st[k] = r.ReadDouble(); }
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
            g.Mode = (GateMode)r.ReadByte(); g.Iron = r.ReadBoolean(); g.Open = r.ReadSingle(); g.Want = r.ReadSingle(); g.Ready = r.ReadBoolean(); g.Broken = r.ReadBoolean();
            g.Missing = r.ReadInt32(); g.Step = r.ReadInt32();
            for (int k = 0; k < Gateway.Voxels; k++) { g.At[k] = r.ReadInt32(); g.Gone[k] = r.ReadBoolean(); }
            SweepOf(g);
            Gates.Add(g);
        }
        FortGate = Gates.Find(g => g.Id == fortGate);
        Drawbridges.Clear();
        _nextDrawbridge = r.ReadInt32();
        int nd = r.ReadInt32();
        for (int i = 0; i < nd; i++)
        {
            var b = new Drawbridge { Id = r.ReadInt32(), AlongX = r.ReadBoolean(), Dir = r.ReadInt32(), Hinge = r.ReadInt32(), U0 = r.ReadInt32(),
                Length = r.ReadInt32(), Width = r.ReadInt32(), Y = r.ReadInt32() };
            b.Mode = (DrawMode)r.ReadByte(); b.Raise = r.ReadSingle(); b.Want = r.ReadSingle(); b.Ready = r.ReadBoolean(); b.Broken = r.ReadBoolean();
            b.Missing = r.ReadInt32(); b.Step = r.ReadInt32();
            b.Alloc();
            for (int k = 0; k < b.Voxels; k++) { b.At[k] = r.ReadInt32(); b.Gone[k] = r.ReadBoolean(); }
            Drawbridges.Add(b);
        }
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
            var f = new Fixture { Kind = (FixtureKind)r.ReadByte(), Anchor = r.ReadInt32(), LX = r.ReadSingle(), LY = r.ReadSingle(), LZ = r.ReadSingle(), DarkUntil = r.ReadDouble() };
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

        ReadLords(r);
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
        // who is led off bound by whom
        foreach (var (u, id) in _captorOf)
            if (id >= 0 && byId.TryGetValue(id, out var thief)) { u.Captor = thief; thief.Captive = u; }
        _captorOf.Clear();
        foreach (var (u, id) in _escortOf) if (id >= 0 && byId.TryGetValue(id, out var engine)) u.Escort = engine;
        _escortOf.Clear();
        foreach (var (u, id) in _victimOf) if (id >= 0 && byId.TryGetValue(id, out var victim)) u.Rite.Victim = victim;
        _victimOf.Clear();
        Raids.Count = r.ReadInt32(); Raids.Active = r.ReadBoolean(); Clock.Left = r.ReadDouble(); Raids.Intel = r.ReadDouble(); Raids._spawnT = r.ReadDouble();
        Clock.DawnLeft = r.ReadDouble();
        Alarm = r.ReadBoolean();
        Weather = (Weather)r.ReadByte(); WeatherLeft = r.ReadDouble(); _nextWeather = r.ReadDouble(); _nextBeast = r.ReadDouble(); _nextEnvoy = r.ReadDouble(); Angered = r.ReadBoolean();
        Offer = null;
        if (r.ReadBoolean())
        {
            var d = new Deal { Kind = (DealKind)r.ReadByte(), Store = r.ReadInt32(), Cost = r.ReadDouble() };
            int envoy = r.ReadInt32(); d.Until = r.ReadDouble();
            d.Envoy = byId.TryGetValue(envoy, out var e) ? e : null;
            if (d.Envoy != null) Offer = d;
        }
        Favour = r.ReadDouble(); Revelations = r.ReadInt32(); MournUntil = r.ReadDouble();
        Carcasses.Clear();
        for (int i = r.ReadInt32(); i > 0; i--) if (KindOf(r.ReadString()) is { } beast) Carcasses.Add(beast);
        Known.Clear();
        for (int i = r.ReadInt32(); i > 0; i--) Known.Add((SpellKind)r.ReadByte());
        for (int i = 0; i < _spellReady.Length; i++) _spellReady[i] = r.ReadDouble();
        Mires.Clear();
        for (int i = r.ReadInt32(); i > 0; i--) Mires.Add(new Mire { X = r.ReadSingle(), Z = r.ReadSingle(), R = r.ReadSingle(), Until = r.ReadDouble() });
        BloodMoonUntil = r.ReadDouble(); EyeUntil = r.ReadDouble();
        Curses.Clear();
        for (int i = r.ReadInt32(); i > 0; i--) Curses.Add(new Curse { X = r.ReadSingle(), Y = r.ReadSingle(), Z = r.ReadSingle(), R = r.ReadSingle(), Until = r.ReadDouble() });
        foreach (var list in new[] { Skins, DeadFogs })
        {
            list.Clear();
            for (int i = r.ReadInt32(); i > 0; i--) list.Add(new Patch { X = r.ReadSingle(), Y = r.ReadSingle(), Z = r.ReadSingle(), R = r.ReadSingle(), Until = r.ReadDouble() });
        }
        Charges.Clear(); _fused.Clear();
        for (int i = r.ReadInt32(); i > 0; i--)
        {
            var c = new Charge { X = r.ReadSingle(), Y = r.ReadSingle(), Z = r.ReadSingle(), BlowAt = r.ReadDouble(), Planted = r.ReadBoolean(), Cutting = r.ReadDouble(), Cause = r.ReadString() };
            for (int k = r.ReadInt32(); k > 0; k--) { int p = r.ReadInt32(); c.Voxels.Add(p); _fused[p] = c; }
            Charges.Add(c);
        }
        // pieces in the air on their way as they were; one thrown bodily stands where the save found it, and comes down from there
        Fliers.Clear();
        for (int i = r.ReadInt32(); i > 0; i--)
        {
            var f = new Flier
            {
                Id = _nextFlier++, X = r.ReadSingle(), Y = r.ReadSingle(), Z = r.ReadSingle(), VX = r.ReadSingle(), VY = r.ReadSingle(), VZ = r.ReadSingle(),
                AX = r.ReadSingle(), AY = r.ReadSingle(), AZ = r.ReadSingle(), Turn = r.ReadSingle(), Spin = r.ReadSingle(), R = r.ReadSingle(), Since = r.ReadDouble(),
                Cause = r.ReadString(), Burning = r.ReadInt32(), Smash = r.ReadInt32(),
                Tipping = r.ReadBoolean(), PX = r.ReadSingle(), PY = r.ReadSingle(), PZ = r.ReadSingle(), CX0 = r.ReadSingle(), CY0 = r.ReadSingle(), CZ0 = r.ReadSingle(),
                TipFrom = r.ReadDouble(), Tipped = r.ReadInt32(),
                Rolling = r.ReadBoolean(), RollFrom = r.ReadDouble(), RollR = r.ReadSingle(), Still = r.ReadSingle(),
                RollAX = r.ReadSingle(), RollAZ = r.ReadSingle(), RollH = r.ReadSingle(), Marked = r.ReadBoolean(), Intact = r.ReadBoolean(), Star = r.ReadBoolean(),
            };
            for (int k = r.ReadInt32(); k > 0; k--) f.Voxels.Add((r.ReadSByte(), r.ReadSByte(), r.ReadSByte(), r.ReadByte()));
            Fliers.Add(f);
        }
        Knowledge = r.ReadDouble();
        Learnt.Clear();
        for (int i = r.ReadInt32(); i > 0; i--) Learnt.Add(r.ReadString());
        int last = r.ReadInt32();
        Raids._lastBand = Bands.Find(b => b.Id == last);
        Raids._queue.Clear();
        int nq = r.ReadInt32();
        for (int i = 0; i < nq; i++)
        {
            var kind = KindOf(r.ReadString()); int band = r.ReadInt32(); var back = ReadSurvivor(r);
            if (kind != null) Raids._queue.Enqueue((kind, Bands.Find(b => b.Id == band), back));
        }
        Escaped.Clear();
        int ne = r.ReadInt32();
        for (int i = 0; i < ne; i++) { var s = ReadSurvivor(r); if (s != null) Escaped.Add(s); }
        Whirls.Clear();
        for (int i = r.ReadInt32(); i > 0; i--)
            Whirls.Add(new Whirl { X = r.ReadSingle(), Z = r.ReadSingle(), VX = r.ReadSingle(), VZ = r.ReadSingle(), R = r.ReadSingle(), Until = r.ReadDouble(), Next = r.ReadDouble() });
        ReadProgress(r);
        ReadDawn(r);
        ReadBattles(r);
        ReadChapters(r);
        ReadHouseDoors(r);
        Sandbox = r.ReadBoolean(); SetSource(r.ReadBoolean() ? new OnCallRaids() : new SiegeScenario());
        Unfolds = r.ReadBoolean(); LetOut.Clear();
        for (int i = r.ReadInt32(); i > 0; i--) LetOut.Add(r.ReadString());
        ReadAcid(r);
        Cracks.Clear(); _strained.Clear(); _undercut.Clear();
        for (int i = r.ReadInt32(); i > 0; i--)
        {
            int p = r.ReadInt32(); float c = r.ReadSingle();
            Cracks[p] = c;
            // what is cracked is weighed again, and the buildings it is in
            var (x, _, z) = WaterPlanner.Unpack(p);
            _weighs.Add(PatchOf(x, z));
            _undercut.Add(p);
        }
        CracksVersion++;
        ReadRelays(r);
        ReadStoreys(r);
        ReadScaffolds(r);
        ReadTunnels(r);
        ReadTown(r);
        ReadYards(r);
        ReadLiquids(r);
        ReadLava(r);
        ReadStanding(r);
        ReadRaids(r);
        ReadCaptains(r);
        ReadCampaign(r);
        OreStruck = r.ReadBoolean(); MetalMined = r.ReadDouble();
        string extra = r.ReadString();
        // the ways of the horde are worked out again for the valley as it now is, and the stones to throw down found again
        _fieldVersion = -1;
        SidesRev++;
        FindDropStones();
        FindScorched();
        // what an old felling left hanging up in the air comes down
        DropStrayLeaves();
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
        u.Back = r.ReadBoolean(); int focus = r.ReadInt32();
        u.Duty = r.ReadInt32(); int zone = r.ReadInt32(); u.Militia = r.ReadBoolean(); u.Inside = r.ReadBoolean();
        if (u.Inside) u.Refuge = Gate;
        int np = r.ReadByte();
        for (int k = 0; k < np; k++) { byte p = r.ReadByte(); if (k < u.Priority.Length) u.Priority[k] = p; }
        u.FireArrows = r.ReadBoolean(); u.KindleAt = r.ReadInt32(); u.Torch = r.ReadBoolean(); u.Burn = r.ReadSingle(); u.BurnHeat = r.ReadSingle();
        u.Guard = r.ReadBoolean(); int raid = r.ReadInt32(); band = r.ReadInt32(); u.Leader = r.ReadBoolean();
        u.Routed = r.ReadBoolean(); u.Returned = r.ReadBoolean(); u.Mending = r.ReadBoolean(); u.Sapped = r.ReadInt32();
        u.LootKind = r.ReadInt32(); u.Loot = r.ReadDouble(); _captorOf[u] = r.ReadInt32();
        _escortOf[u] = r.ReadInt32(); u.AimAt = r.ReadInt32();
        int nd = r.ReadInt32(); for (int k = 0; k < nd; k++) u.Docked.Add(r.ReadInt32());
        u.Envoy = r.ReadBoolean();
        u.TakeAlive = r.ReadBoolean(); u.Bound = r.ReadBoolean(); u.BoundAt = r.ReadDouble(); u.Unguarded = r.ReadDouble(); u.ChainedUntil = r.ReadDouble(); u.CrumbleAt = r.ReadDouble();
        u.PlagueUntil = r.ReadDouble(); u.OathUntil = r.ReadDouble(); u.NextHeal = r.ReadDouble(); u.NextCurse = r.ReadDouble(); u.Charge = r.ReadBoolean();
        u.Champion = r.ReadByte(); u.Title = r.ReadString(); if (u.Title.Length == 0) u.Title = null;
        u.Lord = LordById(r.ReadInt32()); u.CryReady = r.ReadDouble(); u.SlamReady = r.ReadDouble(); u.Abandoned = r.ReadBoolean();
        if (r.ReadBoolean())
        {
            var rite = new Rite { Kind = (RiteKind)r.ReadByte() };
            _victimOf[u] = r.ReadInt32();
            string beast = r.ReadString();
            rite.Beast = beast.Length > 0 ? KindOf(beast) : null;
            rite.Spot = r.ReadInt32(); rite.Stage = r.ReadInt32(); rite.Done = r.ReadSingle();
            u.Rite = rite;
        }
        var body = ReadBody(r, t, u.Look);
        u.PinKg = r.ReadSingle(); u.PinnedSince = r.ReadDouble();
        if (t == null) return null;
        u.Wounds = body;
        u.DutyZone = Zones.Find(z => z.Id == zone);
        u.Focus = Zones.Find(z => z.Id == focus);
        u.Raid = raid >= 0 && raid < Mines.Count ? Mines[raid] : null;
        return u;
    }

    Survivor ReadSurvivor(BinaryReader r)
    {
        if (!r.ReadBoolean()) return null;
        var t = KindOf(r.ReadString());
        int look = r.ReadInt32(); float hp = r.ReadSingle(); byte champion = r.ReadByte(); string title = r.ReadString(); var lord = LordById(r.ReadInt32());
        var body = ReadBody(r, t, look);
        return t == null ? null : new Survivor { Kind = t, Look = look, Hp = hp, Wounds = body, Champion = champion, Title = title.Length > 0 ? title : null, Lord = lord };
    }

    internal static BodyState ReadBody(BinaryReader r, UnitType t, int look)
    {
        if (!r.ReadBoolean()) return null;
        var b = t == null ? null : new BodyState(Bodies.Plan(t, look));
        int rev = r.ReadInt32(); bool main = r.ReadBoolean(), off = r.ReadBoolean(), shield = r.ReadBoolean(); int legs = r.ReadInt32(); float bleed = r.ReadSingle();
        var broken = (Break)r.ReadByte(); var splinted = (Break)r.ReadByte();
        var knit = new float[5];
        for (int i = 0; i < knit.Length; i++) knit[i] = r.ReadSingle();
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
        b.Broken = broken; b.Splinted = splinted; Array.Copy(knit, b.Knit, knit.Length);
        b.Wounds.AddRange(wounds);
        b.Arrows.AddRange(arrows);
        // a part whose half-voxels are not of the shape its body has now (sculpted anew since) is read back whole
        for (int p = 0; p < BodyPlan.Parts; p++)
        {
            var g = grids[p]; var o = b.Plan.Grid[p];
            if (kept[p] || g != null && (o == null || g.X0 != o.X0 || g.Y0 != o.Y0 || g.Z0 != o.Z0 || g.SX != o.SX || g.SY != o.SY || g.SZ != o.SZ)) continue;
            b.Grid[p] = g;
        }
        return b;
    }

    Warband ReadBand(BinaryReader r, Dictionary<int, Unit> byId)
    {
        var b = new Warband { Id = r.ReadInt32(), Name = r.ReadString(), State = (BandState)r.ReadByte(), Since = r.ReadDouble(), Entry = r.ReadInt32(), Champion = r.ReadByte() };
        b.Title = r.ReadString(); if (b.Title.Length == 0) b.Title = null;
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
        b.Lord = LordById(r.ReadInt32()); b.LordsOwn = r.ReadBoolean(); b.Spirit = r.ReadSingle(); b.Wavered = r.ReadBoolean(); b.Taken = r.ReadInt32();
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
    /// <summary>The valley's kind, whether it keeps the old places, and whether it is a hexagon.</summary>
    public readonly ValleyKind Kind;
    public readonly bool Classic, Hexagon;
    /// <summary>The box the valley was made in, across, and the side the horde comes by.</summary>
    public readonly int SizeX, SizeZ, EntrySide;
    /// <summary>What holds the valley, and its sides as the map of valleys gave them (null when it was made by itself).</summary>
    public readonly Holding Holding;
    public readonly HexEdge[] Edges;
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
        Seed = R.ReadUInt32();
        Kind = (ValleyKind)R.ReadByte(); Classic = R.ReadBoolean(); Hexagon = R.ReadBoolean();
        SizeX = R.ReadInt32(); SizeZ = R.ReadInt32(); EntrySide = R.ReadByte();
        Holding = (Holding)R.ReadByte();
        int edges = R.ReadByte();
        if (edges > 0)
        {
            Edges = new HexEdge[edges];
            for (int i = 0; i < edges; i++) Edges[i] = new HexEdge { Seed = R.ReadUInt32(), River = R.ReadSingle(), Road = R.ReadSingle() };
        }
        Label = R.ReadString(); Time = R.ReadDouble();
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
