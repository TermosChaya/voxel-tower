using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The alarm and the shelters. With the alarm up, those of ours who don't fight leave their work for the nearest
/// shelter: the citadel, where they go in and nothing reaches them, or an area the player set aside for it, where they
/// wait; the soldiers keep to their posts. A worker running from the horde runs to a shelter too, and one who gets into
/// the citadel sits there till no raider has been near its door for a while; the badly hurt mend in there. The alarm
/// comes down when the player stands it down, or by itself once a wave is beaten back.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The alarm is up: those who don't fight are in shelter or making for it, the soldiers on their posts.</summary>
    public bool Alarm { get; private set; }

    /// <summary>How near the citadel's door a raider keeps those inside in, in voxels, and how long after one was there.</summary>
    const float KeepInR = 28 * VoxelWorld.PerMetre;
    const double KeepInFor = 6;
    double _raiderByDoor = -99, _nextDoorLook;

    /// <summary>Sounds the alarm: the soldiers to their posts, everyone else to shelter.</summary>
    public void RaiseAlarm()
    {
        if (Alarm || Over) return;
        Alarm = true;
        Recall();
        foreach (var u in Units)
            if (!u.Dead && u.Team == Team.Player && !Fighter(u) && !u.Hold) { ReleaseSite(u); u.Refuge = -1; u.Path = null; u.NextSearch = u.NoWayUntil = 0; Busy(u); }
        Say("Тревога: рабочие уходят в убежища, солдаты встают на посты.", 2);
    }

    /// <summary>Stands the alarm down: whoever sheltered comes out and goes back to work.</summary>
    public void StandDown(bool beaten = false)
    {
        if (!Alarm) return;
        Alarm = false;
        foreach (var u in Units) if (!u.Dead && !u.Inside) u.Refuge = -1;
        Say(beaten ? "Отбой: орда отбита, мирные выходят из убежищ." : "Отбой: мирные возвращаются к работе.", 1);
    }

    /// <summary>One that stands and fights rather than run: a soldier, or a worker called up, with a hand to fight with.</summary>
    bool Fighter(Unit u) => (u.T.Work < 1 || u.Militia) && Armed(u);

    /// <summary>How many of ours are in the citadel now.</summary>
    public int InCitadel()
    {
        int n = 0;
        foreach (var u in Units) if (!u.Dead && u.Inside) n++;
        return n;
    }

    /// <summary>Keeps count of whether a raider is about the citadel's door, for those who sheltered inside.</summary>
    void TickShelter()
    {
        if (Time < _nextDoorLook) return;
        _nextDoorLook = Time + .5;
        var (gx, _, gz) = Nav.Feet(Gate);
        foreach (var o in Units)
        {
            // a prisoner led in is no raider at the door
            if (o.Dead || o.Team != Team.Horde || o.Bound || o.Inside) continue;
            float dx = o.X - gx, dz = o.Z - gz;
            if (dx * dx + dz * dz < KeepInR * KeepInR) { _raiderByDoor = Time; break; }
        }
    }

    /// <summary>
    /// With the alarm up, a pawn that doesn't fight makes for the nearest shelter, goes into the citadel or takes its
    /// place in the shelter area and waits there. True while it is about it.
    /// </summary>
    bool TakeShelter(Unit u, float dt)
    {
        if (u.Refuge < 0)
        {
            u.Refuge = RefugeFor(u, null);
            u.Path = null;
            if (u.Refuge < 0) return false;
        }
        ReleaseSite(u);
        u.Prey = null; u.FireCell = -1; u.Patient = null;
        bool keep = u.Refuge == Gate;
        if (keep && CellsApart(u.Cell, Gate) <= 2) { Enter(u); return true; }
        if (!keep && u.Cell == u.Refuge)
        {
            u.Why = "тревога: в убежище";
            u.Path = null;
            Idle(u, dt, stay: true);
            return true;
        }
        u.Why = keep ? "тревога: идёт в цитадель" : "тревога: идёт в убежище";
        if (u.Path == null || u.PathI >= u.Path.Count)
        {
            if (Time < u.NextSearch || Time < u.NoWayUntil) { u.State = UnitState.Idle; return true; }
            u.NextSearch = Time + 2;
            if (!GoTo(u, u.Refuge, adjacent: keep, budget: 40000))
            {
                // the shelter can't be got to: the citadel then, or wait where it is
                if (!keep && GoTo(u, Gate, adjacent: true, budget: 40000)) u.Refuge = Gate;
                else
                {
                    // (shut out, the gate closed on it: looked for again a good while after)
                    u.NoWayUntil = Time + 8;
                    u.Path = null; u.Why = "тревога: до убежища не дойти"; u.State = UnitState.Idle; return true;
                }
            }
        }
        Walk(u, dt);
        return true;
    }

    /// <summary>
    /// The shelter a pawn makes for: the citadel's door or a free place in a shelter area, whichever is nearer — but
    /// never one the raider it runs from is nearer to than it is. −1 if there is none.
    /// </summary>
    int RefugeFor(Unit u, Unit foe)
    {
        int best = -1; float bs = float.MaxValue;
        void Consider(int cell)
        {
            if (cell < 0) return;
            var (x, _, z) = Nav.Feet(cell);
            float d = MathF.Sqrt((u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z));
            if (foe != null && MathF.Sqrt((foe.X - x) * (foe.X - x) + (foe.Z - z) * (foe.Z - z)) < d) d += 4000;
            if (d < bs) { bs = d; best = cell; }
        }
        Consider(Gate);
        HashSet<int> taken = null;
        foreach (var z in Zones)
        {
            if (z.Kind != Zone.Shelter) continue;
            if (taken == null)
            {
                taken = new HashSet<int>();
                foreach (var o in Units) if (!o.Dead && o != u && o.Refuge >= 0 && o.Refuge != Gate) taken.Add(o.Refuge);
            }
            Consider(ShelterSpot(z, u, taken));
        }
        return best;
    }

    /// <summary>
    /// The places in an area to shelter in: in each column of cells whose middle lies in it, the lowest floor — the
    /// ground, or the floor under a roof — if it is dry.
    /// </summary>
    IEnumerable<int> ShelterCells(int x0, int z0, int x1, int z1, int h)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        for (int k = Math.Max(0, z0 >> NavGrid.Shift); k <= Math.Min(Nav.NZ - 1, z1 >> NavGrid.Shift); k++)
            for (int i = Math.Max(0, x0 >> NavGrid.Shift); i <= Math.Min(Nav.NX - 1, x1 >> NavGrid.Shift); i++)
            {
                int cx = (i << NavGrid.Shift) + NavGrid.CS / 2, cz = (k << NavGrid.Shift) + NavGrid.CS / 2;
                if (cx < x0 || cx > x1 || cz < z0 || cz > z1) continue;
                for (int y = 1; y < Nav.NY - h; y++)
                {
                    if (!Nav.Stand(i, y, k, h)) continue;
                    int c = Nav.Id(i, y, k);
                    if (!Nav.Deep(c)) yield return c;
                    break;
                }
            }
    }

    /// <summary>How many can shelter in an area, a man to a cell (for the tool's tag).</summary>
    public int ShelterRoom(int x0, int z0, int x1, int z1)
    {
        int n = 0;
        foreach (int _ in ShelterCells(x0, z0, x1, z1, Kinds.Worker.Move.Height)) n++;
        return n;
    }

    /// <summary>The free place in a shelter area nearest a pawn.</summary>
    int ShelterSpot(Zone z, Unit u, HashSet<int> taken)
    {
        int best = -1;
        float bd = float.MaxValue;
        foreach (int c in ShelterCells(z.X0, z.Z0, z.X1, z.Z1, u.T.Move.Height))
        {
            if (taken.Contains(c)) continue;
            var (x, _, zz) = Nav.Feet(c);
            float d = (x - u.X) * (x - u.X) + (zz - u.Z) * (zz - u.Z);
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    /// <summary>Into the citadel: out of the world till it comes out again. Where there is no citadel it waits at the spot.</summary>
    void Enter(Unit u)
    {
        if (!Site.HasKeep)
        {
            u.Refuge = Gate; u.Path = null; u.State = UnitState.Idle;
            u.Why = (Site.Holding == Holding.Outpost ? "у места заставы: " : "у лагеря: ") + (u.Mending ? "отлёживается" : "пережидает");
            return;
        }
        // a prisoner it was leading goes in with it
        if (u.Captive is { Team: Team.Horde, Dead: false } c && c.Captor == u) { Imprison(c); u.Captive = null; }
        ReleaseSite(u);
        u.Inside = true;
        u.Refuge = Gate;
        u.Path = null; u.Target = null; u.Prey = null; u.Mate = null;
        u.State = UnitState.Idle; u.Pastime = Pastime.None;
        u.Why = u.Mending ? "в цитадели: отлёживается" : "в цитадели: пережидает";
    }

    /// <summary>Out of the citadel's door, to a free place beside it.</summary>
    void Leave(Unit u)
    {
        u.Inside = false;
        u.Refuge = -1;
        u.Why = "";
        int c = Spread(Gate, 1 + Rng.Next(6), u.T.Move.Height);
        u.Cell = c;
        (u.X, u.Y, u.Z) = Nav.Feet(c);
        u.Path = null; u.NextSearch = 0;
        Busy(u);
    }

    /// <summary>
    /// One in the citadel: the hurt mend till they are fit again and go back to their posts; the rest come out when the
    /// alarm is down and no raider has been about the door for a while — or at once, when the player sends them.
    /// </summary>
    void Indoors(Unit u, float dt)
    {
        u.State = UnitState.Idle; u.Path = null; u.Swing = 0;
        if (u.Mending)
        {
            u.Why = "в цитадели: отлёживается";
            if (!MendedEnough(u)) { if (Disabled(u)) u.Why = "в цитадели: срастается кость"; return; }
            // mended: sent back by its commander no longer, or it would go straight back in
            u.Mending = false; u.SentBack = false;
            Leave(u);
            if (u.Post >= 0) GoTo(u, u.Post);
            return;
        }
        u.Why = "в цитадели: пережидает";
        if (Alarm && !Fighter(u) || Time - _raiderByDoor < KeepInFor) return;
        // a priest keeps in till the morning
        if (u.T.Prays && Raids.Active) { u.Why = "в цитадели: молится до утра"; return; }
        Leave(u);
    }
}
