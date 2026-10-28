using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The horde's faith, of the wild and the swamps. Its orcs and trolls eat the fallen where they lie, the horde's own and
/// ours: one with nobody of ours at hand and a body fresh within a few steps falls on it and tears at it a few seconds,
/// and gets up mended and the fiercer a while, and nothing is left of the body. The goblins' shamans work the swamp's
/// magic: a band's ladders near one grow as ivy, four times as fast and with no timber, that can only be cut or burnt
/// away; and they throw the swamp's poison over a knot of ours, a green reek that eats at those in it and slows them.
/// Every body eaten feeds the horde's faith (one of ours three times over), and every curse and poison of a shaman; when
/// it is full, in a raid, the shamans call the Great Toad up out of a bog on the horde's side of the valley.
/// </summary>
public sealed partial class Sim
{
    /// <summary>The horde's faith, and what it takes to call its god.</summary>
    public double HordeFaith;
    public const double HordeFaithFull = 150;
    /// <summary>How near a body an eater falls on it (voxels), how long it eats, how long it is the fiercer for it.</summary>
    const float FeastReach = 6 * VoxelWorld.PerMetre, FeastFor = 3, GorgedFor = 60;
    /// <summary>How near a shaman a band's ladders grow as ivy (voxels), and how much faster.</summary>
    const float IvyReach = 16 * VoxelWorld.PerMetre, IvyPace = 4;
    /// <summary>The swamp's poison: how far a shaman throws it, how wide it lies, how long, what it takes a half second.</summary>
    const float PoisonReach = 14 * VoxelWorld.PerMetre, PoisonR = 4 * VoxelWorld.PerMetre, PoisonHarm = 1.5f;
    const double PoisonEvery = 18, PoisonFor = 12;
    public const string PoisonCause = "болотный яд";
    /// <summary>The swamp's poison lying on the field now.</summary>
    public readonly List<Patch> Miasmas = new();
    /// <summary>One of the horde ate a body (where it lay); the horde called its god.</summary>
    public event Action<float, float, float> Devoured;
    readonly HashSet<Unit> _meals = new();
    double _feastSaid = -99;
    int _faithTold;

    /// <summary>Who eats the fallen: the orcs and the trolls.</summary>
    static bool Eats(UnitType t) => t == Kinds.Orc || t == Kinds.Miner || t == Kinds.Troll;

    /// <summary>
    /// An orc or a troll at the fallen: to the body it has picked, and at it; or, once a second, a look about for one
    /// (a hurt one always, a whole one only if it has not eaten a while). True while it is about it.
    /// </summary>
    bool Devours(Unit u, float dt)
    {
        if (!Eats(u.T)) return false;
        if (u.Meal is { } c)
        {
            // the body gone from under it (eaten, carried, burnt to nothing): it gives up
            if (!c.Dead || c.DiedAt < 0 || c.Airborne != null) { Unclaim(u); return false; }
            float d = MathF.Sqrt((c.X - u.X) * (c.X - u.X) + (c.Z - u.Z) * (c.Z - u.Z));
            if (d > 1.6f * VoxelWorld.PerMetre)
            {
                if (u.Path == null || u.PathI >= u.Path.Count)
                {
                    if (!GoTo(u, c.Cell, adjacent: true, budget: 3000)) { Unclaim(u); return false; }
                }
                Walk(u, dt);
                return true;
            }
            u.Path = null;
            u.State = UnitState.Work;
            u.Facing = MathF.Atan2(c.Z - u.Z, c.X - u.X);
            u.Swing = Math.Max(u.Swing, .6f);
            u.MealDone += dt;
            if (u.MealDone >= FeastFor) Feast(u, c);
            return true;
        }
        if (Time < u.NextMeal) return false;
        u.NextMeal = Time + 1 + u.Id % 5 * .1;
        if (u.Hp >= u.MaxHp * .95f && Time < u.GorgedUntil) return false;
        Unit best = null; float bd = FeastReach * FeastReach;
        foreach (var o in Units)
        {
            if (!o.Dead || o.DiedAt < 0 || Time - o.DiedAt > 25 || o.Airborne != null || o.Inside || Kinds.IsEngine(o.T) || o.T.Called || _meals.Contains(o)) continue;
            float d2 = (o.X - u.X) * (o.X - u.X) + (o.Z - u.Z) * (o.Z - u.Z);
            if (d2 < bd && MathF.Abs(o.Y - u.Y) < 2 * VoxelWorld.PerMetre) { bd = d2; best = o; }
        }
        if (best == null) return false;
        u.Meal = best; u.MealDone = 0; u.Path = null;
        _meals.Add(best);
        return true;
    }

    void Unclaim(Unit u)
    {
        if (u.Meal != null) _meals.Remove(u.Meal);
        u.Meal = null; u.MealDone = 0; u.Path = null;
    }

    /// <summary>The body eaten: nothing left of it, the eater mended by two fifths and the fiercer a minute, the horde's faith fed.</summary>
    void Feast(Unit u, Unit c)
    {
        Unclaim(u);
        c.DiedAt = -999;
        u.Hp = Math.Min(u.MaxHp, u.Hp + u.MaxHp * .4f);
        if (u.Wounds != null) u.Wounds.Bleed *= .5f;
        u.GorgedUntil = Time + GorgedFor;
        Devoured?.Invoke(c.X, c.Y, c.Z);
        Feed(c.Team == Team.Player ? 3 : 1);
        if (Time - _feastSaid > 30)
        {
            _feastSaid = Time;
            Say(c.Team == Team.Player ? $"Орк пожирает павшего: {c.T.Name.ToLowerInvariant()}. Вера орды растёт." : "Орки пожирают своих павших и встают сильнее.", 3, c.X, c.Z);
        }
    }

    /// <summary>What a gorged eater's blow is: a third harder.</summary>
    float Gorged(Unit a) => Time < a.GorgedUntil ? 1.33f : 1;

    /// <summary>
    /// The horde's faith fed: the chronicle told at a half and at four fifths of it, and, full, in a raid, the Great Toad
    /// called on the horde's side of the valley.
    /// </summary>
    void Feed(double by)
    {
        HordeFaith += by;
        int told = HordeFaith >= HordeFaithFull * .8 ? 2 : HordeFaith >= HordeFaithFull * .5 ? 1 : 0;
        if (told > _faithTold)
        {
            _faithTold = told;
            Say(told == 1 ? $"Вера орды на половине: {HordeFaith:0} из {HordeFaithFull:0}. Полная вера призовёт Великую жабу."
                : $"Вера орды почти полна: {HordeFaith:0} из {HordeFaithFull:0}. Шаманы готовятся звать Великую жабу.", 3);
        }
    }

    /// <summary>When the horde's shamans answer the household's god with theirs (0: they do not).</summary>
    double _answerAt;

    /// <summary>
    /// The household's god come: if the horde's faith is half full or more, its shamans throw all of it into the call and
    /// answer with the Great Toad a little after — only a god stands against a god.
    /// </summary>
    void Answer()
    {
        if (HordeFaith >= HordeFaithFull * .5 && !Gods.Exists(g => !g.Gone && g.T == Deities.Toad)) _answerAt = Time + 12;
    }

    /// <summary>
    /// The horde's god called: its faith full in a raid, or in answer to the household's god — up out of a bog between
    /// the horde's edge and the citadel.
    /// </summary>
    void TickHordeFaith()
    {
        bool answer = _answerAt > 0 && Time >= _answerAt;
        if (!answer && (HordeFaith < HordeFaithFull || !Raids.Active)) return;
        _answerAt = 0;
        if (!Site.HasKeep || Gods.Exists(g => !g.Gone && g.T == Deities.Toad)) return;
        float x = Site.SpawnStrip + (Site.KeepX - Site.SpawnStrip) * .35f, z = Site.KeepZ + (float)(Rng.NextDouble() - .5) * 40 * VoxelWorld.PerMetre;
        if (Awaken(DeityKind.Toad, x, z) != null) return;
        HordeFaith = 0; _faithTold = 0;
        Say(answer ? "Орда отвечает богу богом: шаманы отдали зову всю веру орды, и из болот поднимается Великая жаба."
            : "Вера орды полна: шаманы призвали Великую жабу.", 3, x, z);
        // (and what may be done against it, once the household can)
        if (!answer && Known.Contains(SpellKind.Awaken)) Say("Против бога выстоит только бог: Явление.", 2);
    }

    // ------------------------------------------------------------ the shamans' swamp magic

    /// <summary>Whether a living shaman of the horde is near enough that a ladder put up here grows as ivy.</summary>
    bool IvyBy(Unit u)
    {
        foreach (var o in Units)
            if (!o.Dead && !o.Bound && !o.Routed && o.T == Kinds.Shaman && (o.X - u.X) * (o.X - u.X) + (o.Z - u.Z) * (o.Z - u.Z) < IvyReach * IvyReach) return true;
        return false;
    }

    /// <summary>
    /// A shaman's poison: over the thickest knot of ours (two at least) within its throw, unless the ground there is
    /// consecrated; the horde's faith fed for it.
    /// </summary>
    void Poison(Unit u)
    {
        Unit at = null; int most = 1;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Team != Team.Player || o.T.Called || Kinds.IsEngine(o.T) || Dist(u, o) > PoisonReach) continue;
            int n = 0;
            foreach (var p in Units)
                if (!p.Dead && !p.Inside && p.Team == Team.Player && (p.X - o.X) * (p.X - o.X) + (p.Z - o.Z) * (p.Z - o.Z) < PoisonR * PoisonR) n++;
            if (n > most) { most = n; at = o; }
        }
        if (at == null || Warded(at.X, at.Z)) return;
        u.Swing = 1;
        Miasmas.Add(new Patch { X = at.X, Y = at.Y, Z = at.Z, R = PoisonR, Until = Time + PoisonFor });
        Feed(1);
        if (Time - _poisonSaid > 20) { _poisonSaid = Time; Say("Шаман бросил болотный яд: наши в нём слабеют и идут медленнее.", 3, at.X, at.Z); }
    }

    double _poisonSaid = -99;

    /// <summary>Whether a spot lies in the swamp's poison.</summary>
    bool InMiasma(float x, float z)
    {
        foreach (var f in Miasmas) if ((x - f.X) * (x - f.X) + (z - f.Z) * (z - f.Z) < f.R * f.R) return true;
        return false;
    }

    /// <summary>The poison's toll, every half second, on ours in it.</summary>
    void TickMiasma()
    {
        if (Miasmas.Count == 0) return;
        Miasmas.RemoveAll(f => f.Until <= Time);
        foreach (var u in Units)
            if (!u.Dead && !u.Inside && u.Team == Team.Player && !u.T.Called && !Kinds.IsEngine(u.T) && InMiasma(u.X, u.Z)) Hurt(u, null, PoisonHarm, PoisonCause);
    }
}
