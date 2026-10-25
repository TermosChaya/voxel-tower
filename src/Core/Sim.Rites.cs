using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What is given to the old powers: a prisoner of the horde, a beast from the hunt, or one of our own.</summary>
public enum RiteKind : byte { Prisoner, Carcass, Own }

/// <summary>A sacrifice under way: what is given, where, and how far it has gone.</summary>
public sealed class Rite
{
    public RiteKind Kind;
    /// <summary>Who is given: a prisoner, or one of ours; null for a carcass.</summary>
    public Unit Victim;
    /// <summary>For a carcass, the beast it was.</summary>
    public UnitType Beast;
    /// <summary>Where it is done: the place in a sanctuary nearest its heart (a cell), −1 till it is found.</summary>
    public int Spot = -1;
    /// <summary>0 to the citadel's door for the prisoner, 1 on the way to the altar, 2 the rite itself.</summary>
    public int Stage;
    /// <summary>Seconds of the rite done.</summary>
    public float Done;
}

/// <summary>
/// Sacrifices. By day, in peace, the player asks for one, and a priest does it: it fetches a prisoner from the citadel
/// and leads it bound to the altar — the place in the sanctuary nearest its heart — or goes there with a carcass from the
/// hunt, or leads there one of our own, and gives it to the old powers. What they give back is intervention and,
/// for a life, revelations: what the old powers showed, with which spells are learnt. On a place of power we hold it is
/// worth half as much again. One of our own given up leaves the household mourning: for a while they work slower.
/// </summary>
public sealed partial class Sim
{
    /// <summary>Revelations: what the sacrifices have shown of the old powers; spells are learnt with them.</summary>
    public int Revelations;
    /// <summary>Beasts the hunters brought in, to be offered: each one's kind.</summary>
    public readonly List<UnitType> Carcasses = new();
    /// <summary>Till when the household mourns one of its own given to the old powers: it works a quarter slower.</summary>
    public double MournUntil;
    public const double MournFor = 300;
    /// <summary>How long a rite takes at the altar, in seconds.</summary>
    public const float RiteTime = 8;
    /// <summary>What one of our own is worth.</summary>
    public const double OwnFavour = 50;
    public const int OwnRevelations = 2;

    /// <summary>Fired when a rite is done, at the altar: for the dark flash over it.</summary>
    public event Action<float, float, float> RiteDone;

    /// <summary>What a prisoner is worth to the old powers, by its kind: intervention and revelations.</summary>
    public static (double favour, int revelations) Worth(UnitType t) => t.Key switch
    {
        "troll" or "shaman" or "witch" or "necro" or "alchemist" => (40, 3),
        "orc" or "miner" => (25, 2),
        _ => (15, 1),
    };

    /// <summary>What a carcass is worth: a hare next to nothing, a bear or a white stag a good deal.</summary>
    public static double CarcassWorth(UnitType t) => t.Key switch { "hare" => 1, "deer" => 4, "boar" => 5, "bear" => 10, "stag" => 15, _ => 3 };

    /// <summary>The rite under way, if any.</summary>
    public Rite RiteNow
    {
        get
        {
            foreach (var u in Units) if (!u.Dead && u.Rite != null) return u.Rite;
            return null;
        }
    }

    /// <summary>
    /// The player asks for a sacrifice: of the prisoner worth most, of the best carcass, or of one of ours. Null when a
    /// priest has taken it in hand; otherwise why it cannot be done now.
    /// </summary>
    public string Sacrifice(RiteKind kind, Unit victim = null)
    {
        if (Raids.Active) return "жертвы приносят днём, когда орды нет в долине";
        if (!Zones.Exists(z => z.Kind == Zone.Sanctum)) return "нет святилища: отметьте его делянкой ({ToolGather} → «святилище»)";
        if (RiteNow != null) return "обряд уже идёт";
        Unit priest = null; float bd = float.MaxValue;
        foreach (var u in Units)
        {
            if (u.Dead || !u.T.Prays || u.Captor != null || u == victim) continue;
            float d = MathF.Abs(u.X - Site.KeepX) + MathF.Abs(u.Z - Site.KeepZ);
            if (u.Pastime == Pastime.Pray) d -= 100000;
            if (d < bd) { bd = d; priest = u; }
        }
        if (priest == null) return "нет жреца: наберите его в цитадели";
        var r = new Rite { Kind = kind };
        switch (kind)
        {
            case RiteKind.Prisoner:
            {
                Unit best = null; double worth = -1;
                foreach (var u in Units)
                    if (!u.Dead && u.Inside && u.Team == Team.Horde && Worth(u.T).favour > worth) { worth = Worth(u.T).favour; best = u; }
                if (best == null) return "в цитадели нет пленных";
                r.Victim = best;
                r.Stage = 0;
                break;
            }
            case RiteKind.Carcass:
            {
                if (Carcasses.Count == 0) return "туш нет: их приносит охота";
                var beast = Carcasses[0];
                foreach (var t in Carcasses) if (CarcassWorth(t) > CarcassWorth(beast)) beast = t;
                Carcasses.Remove(beast);
                r.Beast = beast;
                r.Stage = 1;
                break;
            }
            default:
                if (victim == null || victim.Dead || victim.Team != Team.Player || victim.T.Prays || victim.Captor != null) return "выберите одного из своих, не жреца";
                r.Victim = victim;
                r.Stage = 1;
                if (victim.Inside) { victim.Mending = false; Leave(victim); }
                ReleaseSite(victim);
                victim.Hold = false; victim.Dest = -1; victim.Path = null; victim.Prey = null; victim.Patient = null; victim.Refuge = -1; victim.Fetching = null;
                Busy(victim);
                victim.Captor = priest; priest.Captive = victim;
                break;
        }
        if (priest.Inside) Leave(priest);
        Busy(priest);
        priest.Shrine = -1; priest.Hold = false; priest.Path = null; priest.NextSearch = 0;
        priest.Rite = r;
        Say(kind switch
        {
            RiteKind.Prisoner => $"Жрец идёт за пленным: {r.Victim.Name.ToLowerInvariant()} будет принесён в жертву.",
            RiteKind.Carcass => $"Жрец несёт в святилище тушу: {r.Beast.Name.ToLowerInvariant()}.",
            _ => $"Жрец уводит в святилище: {r.Victim.T.Name.ToLowerInvariant()} №{r.Victim.Id}.",
        }, 2, priest);
        return null;
    }

    /// <summary>
    /// A priest at a rite: to the citadel's door for the prisoner, which comes out bound at its back; to the altar; and
    /// there, a while of chanting, and the life is given. Night falling, or the one to be given gone, ends it.
    /// </summary>
    void RiteTick(Unit u, float dt)
    {
        var r = u.Rite;
        if (Raids.Active) { CancelRite(u, "пришла ночь"); return; }
        if (r.Victim != null && (r.Victim.Dead || r.Stage >= 1 && r.Victim.Captor != u)) { CancelRite(u, null); return; }
        switch (r.Stage)
        {
            case 0:
                u.Why = "идёт за пленным в цитадель";
                if (CellsApart(u.Cell, Gate) <= 2)
                {
                    var v = r.Victim;
                    if (!v.Inside) { CancelRite(u, null); return; }
                    // out of the door, bound, at the priest's back
                    v.Inside = false; v.Bound = true;
                    int cell = Spread(Gate, 1 + Rng.Next(3), v.T.Move.Height);
                    v.Cell = cell; (v.X, v.Y, v.Z) = Nav.Feet(cell);
                    v.Captor = u; u.Captive = v;
                    r.Stage = 1; u.Path = null; u.NextSearch = 0;
                    return;
                }
                if (u.Path == null || u.PathI >= u.Path.Count)
                {
                    if (Time < u.NextSearch) { u.State = UnitState.Idle; return; }
                    u.NextSearch = Time + 2;
                    if (!GoTo(u, Gate, adjacent: true, budget: 40000)) { CancelRite(u, "жрецу не дойти до цитадели"); return; }
                }
                Walk(u, dt);
                return;
            case 1:
                u.Why = r.Kind == RiteKind.Carcass ? "несёт тушу в святилище" : "ведёт жертву в святилище";
                if (r.Spot < 0 || SanctumAt(r.Spot) == null) r.Spot = AltarFor(u);
                if (r.Spot < 0) { CancelRite(u, "в святилище не встать"); return; }
                if (u.Cell == r.Spot) { r.Stage = 2; r.Done = 0; u.Path = null; return; }
                if (u.Path == null || u.PathI >= u.Path.Count)
                {
                    if (Time < u.NextSearch) { u.State = UnitState.Idle; return; }
                    u.NextSearch = Time + 2;
                    if (!GoTo(u, r.Spot, budget: 40000)) { u.Unreachable[r.Spot] = Time + 30; r.Spot = -1; return; }
                }
                // slow and solemn, the one to be given at its back
                Walk(u, dt, .7f);
                return;
            default:
                u.Why = "приносит жертву";
                u.Path = null;
                u.State = UnitState.Work;
                var z = SanctumAt(r.Spot);
                var m = z != null ? PowerUnder(z) : null;
                float hx = m != null ? m.Place.X : z != null ? (z.X0 + z.X1) / 2f : u.X, hz = m != null ? m.Place.Z : z != null ? (z.Z0 + z.Z1) / 2f : u.Z;
                if (MathF.Abs(hx - u.X) + MathF.Abs(hz - u.Z) > 6) u.Facing = MathF.Atan2(hz - u.Z, hx - u.X);
                r.Done += dt;
                if (r.Done >= RiteTime) Fulfil(u, r);
                return;
        }
    }

    /// <summary>The life given: what the old powers send back, and the household's grief if it was one of ours.</summary>
    void Fulfil(Unit priest, Rite r)
    {
        var z = SanctumAt(r.Spot);
        bool power = z != null && PowerUnder(z) is { Holder: Holder.Ours };
        double favour; int revelations; string what;
        float ax = priest.X + MathF.Cos(priest.Facing) * 6, az = priest.Z + MathF.Sin(priest.Facing) * 6, ay = priest.Y;
        switch (r.Kind)
        {
            case RiteKind.Prisoner:
                (favour, revelations) = Worth(r.Victim.T);
                if (r.Victim.Leader) favour *= 1.5;
                what = r.Victim.Name.ToLowerInvariant();
                (ax, ay, az) = (r.Victim.X, r.Victim.Y, r.Victim.Z);
                Kill(r.Victim, priest, GivenToPowers);
                break;
            case RiteKind.Carcass:
                favour = CarcassWorth(r.Beast); revelations = 0;
                what = "туша, " + r.Beast.Name.ToLowerInvariant();
                break;
            default:
                favour = OwnFavour; revelations = OwnRevelations;
                what = $"{r.Victim.T.Name.ToLowerInvariant()} №{r.Victim.Id}";
                (ax, ay, az) = (r.Victim.X, r.Victim.Y, r.Victim.Z);
                Kill(r.Victim, null, GivenToPowers);
                MournUntil = Time + MournFor;
                break;
        }
        if (power) favour *= 1.5;
        // the altar of grace: half as much again, and a revelation more
        favour *= SacrificeMore;
        if (Knows("altar")) revelations++;
        double held = Favour;
        GainFavour(favour);
        // what the household could hold of it: past the vessel's brim it is lost
        double kept = Favour - held;
        Revelations += revelations;
        priest.Rite = null; priest.Captive = null; priest.State = UnitState.Idle; priest.NextSearch = 0;
        RiteDone?.Invoke(ax, ay, az);
        Say($"Жертва принята: {what}. Вмешательство +{kept:0}" + (kept < favour - .5 ? $" (из {favour:0}: больше не вместить)" : "")
            + (revelations > 0 ? $", откровения +{revelations}" : "") + (power ? " (на месте силы в 1,5 раза больше)." : "."), 1, priest);
        if (r.Kind == RiteKind.Own) Say("Дружина скорбит: 5 минут работа идёт на 25 % медленнее.", 2);
    }

    /// <summary>What the chronicle says killed one given to the old powers.</summary>
    public const string GivenToPowers = "принесён в жертву";

    /// <summary>A rite ended before it was done: the prisoner goes back into the citadel, one of ours back to its work, a carcass back to the stores.</summary>
    void CancelRite(Unit u, string why)
    {
        var r = u.Rite;
        u.Rite = null;
        u.Path = null; u.NextSearch = 0;
        if (r.Victim is { Dead: false } v && v.Captor == u)
        {
            v.Captor = null; u.Captive = null;
            if (v.Team == Team.Horde) { v.Inside = true; v.Unguarded = 0; v.Path = null; }
        }
        if (r.Kind == RiteKind.Carcass && r.Beast != null) Carcasses.Add(r.Beast);
        if (why != null) Say("Обряд прерван: " + why + ".", 2, u);
    }

    /// <summary>The altar: the place in the sanctuary heard best that lies nearest its heart, or −1.</summary>
    int AltarFor(Unit u)
    {
        int best = -1; double bs = double.MinValue;
        foreach (var z in Zones)
        {
            if (z.Kind != Zone.Sanctum) continue;
            var m = PowerUnder(z);
            float hx = m != null ? m.Place.X : (z.X0 + z.X1) / 2f, hz = m != null ? m.Place.Z : (z.Z0 + z.Z1) / 2f;
            double hear = Hearing(z);
            foreach (int c in ShelterCells(z.X0, z.Z0, z.X1, z.Z1, u.T.Move.Height))
            {
                if (u.Unreachable.TryGetValue(c, out double until) && until > Time) continue;
                var (x, _, cz) = Nav.Feet(c);
                double score = hear * 1000 - MathF.Sqrt((x - hx) * (x - hx) + (cz - hz) * (cz - hz));
                if (score > bs) { bs = score; best = c; }
            }
        }
        return best;
    }
}
