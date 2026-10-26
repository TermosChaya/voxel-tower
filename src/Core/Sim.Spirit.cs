using System;

namespace VoxelTower.Core;

/// <summary>
/// The spirit of the horde's bands. A band comes in whole. Every one of it who falls takes a little of it, its leader
/// more; one of it thrown through the air, set alight or ridden down, a blast of powder among it shake it, and a band
/// sees its neighbours fall; the blood of ours and its warlord's cry raise it, and it comes back slowly while nothing
/// goes wrong. A raider of a band low in spirit breaks with less pain, one near its warlord with more. Under half the
/// band wavers (said once); at the bottom it breaks and runs whole, as one whose leader fell with two in three of it
/// always did.
/// </summary>
public sealed partial class Sim
{
    /// <summary>Spirit under which a band breaks; under which it wavers.</summary>
    public const float BreakSpirit = .2f, WaverSpirit = .5f;
    /// <summary>How much of a band's spirit goes with all of it fallen, shared among them; and more with its leader.</summary>
    const float LossSpirit = .8f, LeaderSpirit = .2f;
    /// <summary>How fast a band's spirit comes back while nothing goes wrong (a share a second), and after how long.</summary>
    const float SpiritBack = .006f, SpiritCalm = 15;

    /// <summary>How much more pain than its kind bears a raider bears: less in a band low in spirit, more near its warlord.</summary>
    internal float Nerve(Unit u)
    {
        float k = u.Band is { } b ? .55f + .45f * b.Spirit : 1;
        if (LordNear(u) is { } l) k *= 1.25f + .05f * l.Lord.Level;
        return k;
    }

    /// <summary>A band's spirit lowered: it wavers under half, breaks at the bottom once all of it has come in.</summary>
    internal void Dismay(Warband b, float by)
    {
        if (b == null || b.Broken || by <= 0) return;
        b.Spirit = Math.Max(0, b.Spirit - by);
        b.ShakenAt = Time;
        if (!b.Wavered && b.Spirit < WaverSpirit && b.Alive > 1) { b.Wavered = true; Say($"{b.Name} дрогнула.", 2, b.Leader); }
        if (b.Spirit < BreakSpirit && b.Spawned >= b.Size && b.Target == null) BreakBand(b);
    }

    /// <summary>A band's spirit raised.</summary>
    internal void Hearten(Warband b, float by)
    {
        if (b == null || b.Broken) return;
        b.Spirit = Math.Min(1, b.Spirit + by);
    }

    /// <summary>Every band with one of its own within so far of a spot (voxels) shaken by so much: a blast seen, a fall seen.</summary>
    internal void DismayAbout(float x, float z, float r, float by, Warband but = null)
    {
        for (int i = Bands.Count - 1; i >= 0; i--)
        {
            var b = Bands[i];
            if (b == but || b.Broken || b.State == BandState.Coming) continue;
            foreach (var u in b.Members)
                if (!u.Dead && (u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z) < r * r) { Dismay(b, by); break; }
        }
    }

    /// <summary>One of the horde fell: its band loses its share and more for its leader; bands close by saw it.</summary>
    void BandLoss(Unit u, Unit by)
    {
        if (u.Band is { } b) Dismay(b, LossSpirit / Math.Max(5, b.Size) + (u.Leader ? LeaderSpirit : 0));
        DismayAbout(u.X, u.Z, 12 * VoxelWorld.PerMetre, .015f, u.Band);
        if (by is { Team: Team.Horde, Band: { } kb }) Hearten(kb, .01f);
    }

    /// <summary>One of ours fell to the horde: the band of the one who felled it takes heart, and its warlord counts it.</summary>
    void BandBlood(Unit by)
    {
        if (by is not { Team: Team.Horde }) return;
        if (by.Band is { } b) { Hearten(b, .05f); if (b.Lord != null) { b.Lord.Slew++; BattleLord(b.Lord); } }
    }

    /// <summary>The bands' spirit coming back while nothing goes wrong, every second or so.</summary>
    void TickSpirit()
    {
        if (Time < _nextSpirit) return;
        _nextSpirit = Time + 1;
        foreach (var b in Bands)
            if (!b.Broken && b.State != BandState.Coming && Time - b.ShakenAt > SpiritCalm) b.Spirit = Math.Min(1, b.Spirit + SpiritBack);
    }
    double _nextSpirit;

    /// <summary>A band breaks: every one of it runs.</summary>
    internal void BreakBand(Warband b)
    {
        if (b.Broken) return;
        b.Broken = true;
        foreach (var m in b.Members) if (!m.Dead && !m.Bound) m.Routed = true;
        int alive = b.Alive;
        if (alive > 0) Say($"{b.Name} бежит: {(alive == 1 ? "уцелел один" : $"уцелело {alive}")}.", 1, b.Leader is { Dead: false } l ? l : b.Members.Find(m => !m.Dead));
    }
}
