using System;
using System.Collections.Generic;
using System.IO;

namespace VoxelTower.Core;

/// <summary>How a band of a raid ended: how many it came with, and of them fell, got away, were taken; whether it broke, and its spirit at the end.</summary>
public sealed class BandEnd
{
    public string Name = "";
    public int Id, Size, Fell, Fled, Taken;
    public bool Broken;
    public float Spirit;
}

/// <summary>
/// What a raid was, from the horn to its end: who of the horde came and what became of them, kind by kind; who of ours
/// fell; how each band ended; the warlord at its head and what became of him; how much of our building it broke and
/// what the citadel lost. Kept for the screen after the raid and for the world map, which will take the survivors and
/// the warlord back onto it.
/// </summary>
public sealed class BattleReport
{
    public int Number;
    /// <summary>The side of the land it came in by (−1 the siege's west).</summary>
    public int Edge = -1;
    public double Began, Ended;
    public float Threat;
    /// <summary>Beaten off; else the citadel fell.</summary>
    public bool Held;
    public readonly Dictionary<UnitType, int> Came = new(), Fell = new(), Fled = new(), Taken = new(), OursFell = new();
    public readonly List<BandEnd> Bands = new();
    /// <summary>The warlord at its head, if any: his name, his level when he came, what became of him, how many of ours his army killed.</summary>
    public string Lord;
    public int LordLevel, LordSlew;
    /// <summary>His kind («тролль-военачальник»).</summary>
    public string LordTitle = "";
    public LordFate LordFate;
    /// <summary>Voxels of our building the horde broke: hacked, smashed by stones and by a giant.</summary>
    public int Broke;
    /// <summary>The share of the citadel's strength it cost.</summary>
    public float KeepLost;
    internal float Keep0;

    public float Seconds => (float)(Ended - Began);
    public static int Sum(Dictionary<UnitType, int> d) { int n = 0; foreach (var v in d.Values) n += v; return n; }
    internal static void Add(Dictionary<UnitType, int> d, UnitType t) => d[t] = d.GetValueOrDefault(t) + 1;
}

public sealed partial class Sim
{
    /// <summary>The report of the last raid, once it is over; and of every raid since the start.</summary>
    public BattleReport LastBattle;
    public readonly List<BattleReport> Battles = new();
    /// <summary>The raid in the valley as it goes, or null between raids.</summary>
    BattleReport _battle;
    /// <summary>A raid's report was closed (for the screen).</summary>
    public event Action<BattleReport> BattleClosed;

    /// <summary>The horn: a new report begins.</summary>
    internal void BattleBegins(int number, float threat, List<Warband> bands)
    {
        _battle = new BattleReport { Number = number, Began = Time, Threat = threat, Keep0 = KeepHp, Edge = bands.Count > 0 ? bands[0].Edge : -1 };
    }

    /// <summary>One of the horde came into the valley in the raid.</summary>
    void BattleCame(Unit u)
    {
        if (_battle != null && u.Team == Team.Horde && u.T != Kinds.Thrall && !u.Envoy) BattleReport.Add(_battle.Came, u.T);
    }

    /// <summary>One fell in the raid, of the horde or of ours.</summary>
    void BattleFell(Unit u)
    {
        if (_battle == null) return;
        if (u.Team == Team.Horde && u.T != Kinds.Thrall) BattleReport.Add(_battle.Fell, u.T);
        else if (u.Team == Team.Player && u.T != Kinds.Risen) BattleReport.Add(_battle.OursFell, u.T);
    }

    /// <summary>One of the horde got out of the valley alive, or was taken alive.</summary>
    void BattleFled(Unit u) { if (_battle != null && u.Team == Team.Horde && !u.Envoy) BattleReport.Add(_battle.Fled, u.T); }
    void BattleTaken(Unit u)
    {
        if (_battle == null) return;
        BattleReport.Add(_battle.Taken, u.T);
        if (u.Band != null) u.Band.Taken++;
    }

    /// <summary>Voxels of our building broken by the horde, and by whom (his warlord counts it).</summary>
    void BattleBroke(int n, Unit by)
    {
        if (n <= 0) return;
        if (_battle != null) _battle.Broke += n;
        if (by?.Band?.Lord is { } lord) lord.Broke += n;
    }

    /// <summary>A band of the raid done with, all of it fallen, gone or taken (or the raid over): how it ended, written down once.</summary>
    internal void BattleBand(Warband b)
    {
        if (_battle == null || b.State == BandState.Coming || _battle.Bands.Exists(e => e.Id == b.Id)) return;
        var end = new BandEnd { Id = b.Id, Name = b.Name, Size = b.Spawned, Broken = b.Broken, Spirit = b.Spirit, Taken = b.Taken };
        foreach (var m in b.Members)
            if (m.Dead) { if (m.DiedAt == -999) end.Fled++; else end.Fell++; }
        _battle.Bands.Add(end);
    }

    /// <summary>The warlord at the head of the raid, as he comes and as his fate goes.</summary>
    void BattleLord(Warlord lord)
    {
        if (_battle == null || lord.Champion) return;
        if (_battle.Lord == null) { _battle.Lord = lord.Name; _battle.LordLevel = lord.Level; _battle.LordTitle = lord.Title; }
        if (_battle.Lord != lord.Name) return;
        _battle.LordFate = lord.Fate;
        _battle.LordSlew = lord.Slew;
    }

    /// <summary>The raid over: beaten off, or the citadel fallen. The bands still about written down.</summary>
    internal void CloseBattle(bool held)
    {
        var r = _battle;
        if (r == null) return;
        foreach (var b in Bands) { BattleBand(b); if (b.Lord != null) BattleLord(b.Lord); }
        _battle = null;
        r.Ended = Time;
        r.Held = held;
        r.KeepLost = Math.Clamp((r.Keep0 - KeepHp) / Math.Max(1, KeepMax), 0, 1);
        LastBattle = r;
        Battles.Add(r);
        BattleClosed?.Invoke(r);
    }

    void WriteBattles(BinaryWriter w)
    {
        static void Tally(BinaryWriter w, Dictionary<UnitType, int> d) { w.Write(d.Count); foreach (var (t, n) in d) { w.Write(t.Key); w.Write(n); } }
        void One(BattleReport r)
        {
            w.Write(r != null);
            if (r == null) return;
            w.Write(r.Number); w.Write(r.Edge); w.Write(r.Began); w.Write(r.Ended); w.Write(r.Threat); w.Write(r.Held);
            Tally(w, r.Came); Tally(w, r.Fell); Tally(w, r.Fled); Tally(w, r.Taken); Tally(w, r.OursFell);
            w.Write(r.Bands.Count);
            foreach (var b in r.Bands) { w.Write(b.Id); w.Write(b.Name); w.Write(b.Size); w.Write(b.Fell); w.Write(b.Fled); w.Write(b.Taken); w.Write(b.Broken); w.Write(b.Spirit); }
            w.Write(r.Lord ?? ""); w.Write(r.LordLevel); w.Write(r.LordSlew); w.Write((byte)r.LordFate); w.Write(r.LordTitle ?? "");
            w.Write(r.Broke); w.Write(r.KeepLost); w.Write(r.Keep0);
        }
        // the last few raids: the screen and the map look no further back
        int from = Math.Max(0, Battles.Count - 12);
        w.Write(Battles.Count - from);
        for (int i = from; i < Battles.Count; i++) One(Battles[i]);
        w.Write(LastBattle != null && Battles.Count > 0 && LastBattle == Battles[^1]);
        One(_battle);
    }

    void ReadBattles(BinaryReader r)
    {
        static void Tally(BinaryReader r, Dictionary<UnitType, int> d) { for (int n = r.ReadInt32(); n > 0; n--) { var t = KindOf(r.ReadString()); int k = r.ReadInt32(); if (t != null) d[t] = k; } }
        BattleReport One()
        {
            if (!r.ReadBoolean()) return null;
            var b = new BattleReport { Number = r.ReadInt32(), Edge = r.ReadInt32(), Began = r.ReadDouble(), Ended = r.ReadDouble(), Threat = r.ReadSingle(), Held = r.ReadBoolean() };
            Tally(r, b.Came); Tally(r, b.Fell); Tally(r, b.Fled); Tally(r, b.Taken); Tally(r, b.OursFell);
            for (int n = r.ReadInt32(); n > 0; n--)
                b.Bands.Add(new BandEnd { Id = r.ReadInt32(), Name = r.ReadString(), Size = r.ReadInt32(), Fell = r.ReadInt32(), Fled = r.ReadInt32(), Taken = r.ReadInt32(), Broken = r.ReadBoolean(), Spirit = r.ReadSingle() });
            b.Lord = r.ReadString(); if (b.Lord.Length == 0) b.Lord = null;
            b.LordLevel = r.ReadInt32(); b.LordSlew = r.ReadInt32(); b.LordFate = (LordFate)r.ReadByte(); b.LordTitle = r.ReadString();
            b.Broke = r.ReadInt32(); b.KeepLost = r.ReadSingle(); b.Keep0 = r.ReadSingle();
            return b;
        }
        Battles.Clear();
        for (int n = r.ReadInt32(); n > 0; n--) Battles.Add(One());
        LastBattle = r.ReadBoolean() ? Battles[^1] : null;
        _battle = One();
    }
}
