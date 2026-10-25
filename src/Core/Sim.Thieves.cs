using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// Thieves. They come with the bands, and once a band storms, or they are near the citadel, they go about their own
/// business: a worker of ours caught alone, with no soldier near, is beaten down, bound and led off; otherwise they make
/// for the citadel's door, where the stores are, take what they can carry of the store it holds most of (or gold), and
/// run west. One that gets out of the valley takes it with it, and the worker too; one cut down on the way drops it,
/// and the worker is free. The badly hurt flee empty-handed, like any raider.
/// </summary>
public sealed partial class Sim
{
    /// <summary>What a thief carries off: three cubic metres of a store, or so much gold.</summary>
    const double LootVoxels = 3 * VoxelWorld.PerM3, LootGold = 25;
    /// <summary>How near the citadel, in voxels, a thief turns from its band to its own business.</summary>
    const float ThiefNear = 30 * VoxelWorld.PerMetre;
    /// <summary>A worker is alone when none of our soldiers is this near it, in voxels; thieves see one this far off.</summary>
    const float Alone = 8 * VoxelWorld.PerMetre, ThiefEye = 7 * VoxelWorld.PerMetre;

    /// <summary>"брёвна 3 м³", "25 золота".</summary>
    public static string LootText(Unit u) => u.LootKind == 0 ? $"{u.Loot:0} золота" : $"{Stockpile.Name(u.LootKind)} {Fmt.M3(u.Loot)} м³";

    /// <summary>A thief's own business; true while it is about it (false: it goes on with its band, as any raider).</summary>
    bool ThiefTick(Unit u, float dt)
    {
        // nothing in the stores to take: an ordinary raider from now on
        if (u.LootKind == -2) return false;
        // with what it took, or the one it took: away west, out of the valley
        if (u.Loot > 0 || u.Captive != null)
        {
            if (u.Captive is { Dead: true } or { Captor: null }) u.Captive = null;
            StopCarve(u); StopBuild(u);
            Retreat(u, dt);
            return true;
        }
        float dk = MathF.Sqrt((u.X - Site.KeepX) * (u.X - Site.KeepX) + (u.Z - Site.KeepZ) * (u.Z - Site.KeepZ));
        bool storming = u.Band is { State: BandState.Storm or BandState.Rally } || u.Band == null;
        if (!storming && dk > ThiefNear) return false;
        // a worker of ours alone close by: beaten down and bound
        var prey = LonePrey(u);
        if (prey != null)
        {
            StopCarve(u); StopBuild(u);
            if (InReach(u, prey))
            {
                u.State = UnitState.Fight; u.Target = prey;
                u.Facing = MathF.Atan2(prey.Z - u.Z, prey.X - u.X);
                if (u.Cooldown <= 0) { Strike(u, prey); u.Cooldown = u.T.AttackTime; }
                // beaten till it hits back no more, stunned or down: bound
                if (!prey.Dead && (prey.Hp < prey.MaxHp * .6f || prey.Pain >= Cowed || Time < prey.DazedUntil || prey.Down)) Bind(u, prey);
                return true;
            }
            // close on the same ground: straight at it
            float d = Dist(u, prey);
            if (d < 4 * VoxelWorld.PerMetre && MathF.Abs(prey.Y - u.Y) < 6)
            {
                float step = u.T.Move.Speed * NavGrid.CS * dt, dx = prey.X - u.X, dz = prey.Z - u.Z;
                u.Facing = MathF.Atan2(dz, dx);
                int at = Nav.CellOf(u.X + dx / d * step, u.Y + 1, u.Z + dz / d * step);
                if (Passable(u, at))
                {
                    u.X += dx / d * step; u.Z += dz / d * step; u.Cell = at; u.Y = Nav.Floor[at];
                    u.Stride += step; u.State = UnitState.Walk; u.Path = null;
                    return true;
                }
            }
            if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
            {
                u.NextSearch = Time + .7;
                if (!GoTo(u, prey.Cell, adjacent: true, budget: 4000) && !GoTo(u, prey.Cell, budget: 4000)) u.Path = null;
            }
            if (u.Path != null) { Walk(u, dt); return true; }
        }
        // one of our fighters at it: it strikes back
        var foe = Nearest(u, Team.Player, u.T.Reach + 8);
        if (foe != null && InReach(u, foe) && Armed(u))
        {
            u.State = UnitState.Fight; u.Target = foe;
            u.Facing = MathF.Atan2(foe.Z - u.Z, foe.X - u.X);
            if (u.Cooldown <= 0) { Strike(u, foe); u.Cooldown = u.T.AttackTime; }
            return true;
        }
        // the citadel's door: in to the stores
        if (CellsApart(u.Cell, Gate) <= 2) { Steal(u); return true; }
        if (u.Path == null || u.PathI >= u.Path.Count || Time >= u.NextSearch)
        {
            u.NextSearch = Time + 2;
            StopCarve(u); StopBuild(u);
            if (!GoTo(u, Gate, adjacent: true, budget: 20000)) { u.Path = null; return false; }
        }
        if (u.Path == null) return false;
        Walk(u, dt);
        return true;
    }

    /// <summary>A worker of ours outside, alone and near enough to be caught, or null.</summary>
    Unit LonePrey(Unit u)
    {
        Unit best = null; float bd = ThiefEye;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Team != Team.Player || o.T.Work < 1 || o.Captor != null) continue;
            float d = Dist(u, o);
            if (d >= bd) continue;
            bool guarded = false;
            foreach (var g in Units)
                if (!g.Dead && !g.Inside && g.Team == Team.Player && g.T.Work < 1 && Dist(g, o) < Alone) { guarded = true; break; }
            if (guarded) continue;
            bd = d; best = o;
        }
        return best;
    }

    /// <summary>At the citadel's door: what it can carry of the store the citadel holds most of, or gold.</summary>
    void Steal(Unit u)
    {
        int kind = -1; double most = 0;
        foreach (int k in new[] { Stockpile.Stone, Stockpile.Wood, Stockpile.Earth, Stockpile.Metal })
            if (Stock[k] > most) { most = Stock[k]; kind = k; }
        if (Stock.Gold >= LootGold * 2 && (kind < 0 || most < LootVoxels)) kind = 0;
        if (kind < 0) { u.Loot = 0; u.LootKind = -2; return; }
        u.LootKind = kind;
        if (kind == 0) { u.Loot = Math.Min(LootGold, Stock.Gold); Stock.Gold -= u.Loot; }
        else { u.Loot = Math.Min(LootVoxels, Stock[kind]); Stock[kind] -= u.Loot; }
        u.Path = null; u.NextSearch = 0;
        Say($"Грабитель украл из цитадели: {LootText(u)}. Перехватите его, пока он не ушёл.", 3, u);
    }

    /// <summary>A worker beaten down is bound and led off.</summary>
    void Bind(Unit thief, Unit prey)
    {
        thief.Captive = prey;
        prey.Captor = thief;
        ReleaseSite(prey);
        prey.Path = null; prey.Hold = false; prey.Refuge = -1; prey.Prey = null; prey.Patient = null;
        prey.Mending = false;
        thief.Path = null; thief.NextSearch = 0; thief.Target = null;
        Say($"Грабитель схватил: {prey.T.Name.ToLowerInvariant()} №{prey.Id}. Отбейте, пока не увели.", 3, prey);
    }

    /// <summary>
    /// One led off bound — one of ours by a thief, a raider by our worker or a priest, one of ours by a priest to the
    /// altar: it goes where it is led, at its leader's back, till it is freed; at the altar it kneels before the priest.
    /// </summary>
    void Led(Unit u, float dt)
    {
        var t = u.Captor;
        if (t == null || t.Dead || t.Captive != u)
        {
            bool fromThief = u.Team == Team.Player && u.Why == "в плену у грабителя";
            u.Captor = null;
            u.State = UnitState.Idle; u.Path = null; u.NextSearch = 0;
            u.Why = "";
            // a raider dropped on the way lies bound where it is, a while more
            if (u.Bound) u.BoundAt = Time;
            if (fromThief) Say($"{u.T.Name} №{u.Id} отбит у грабителя.", 1, u);
            return;
        }
        // at the altar: on its knees before the priest, facing it
        if (t.Rite is { Stage: 2 })
        {
            float k = t.T.BodyR + u.T.BodyR + 3;
            u.X = t.X + MathF.Cos(t.Facing) * k; u.Z = t.Z + MathF.Sin(t.Facing) * k; u.Y = t.Y;
            u.Facing = t.Facing + MathF.PI; u.Cell = t.Cell;
            u.State = UnitState.Idle; u.Pastime = Pastime.Bound;
            u.Why = "на алтаре";
            return;
        }
        // a step behind it, on its heels
        float back = t.T.BodyR + u.T.BodyR + 3;
        float tx = t.X - MathF.Cos(t.Facing) * back, tz = t.Z - MathF.Sin(t.Facing) * back;
        float dx = tx - u.X, dz = tz - u.Z, d = MathF.Sqrt(dx * dx + dz * dz);
        float step = t.T.Move.Speed * NavGrid.CS * dt * 1.2f;
        if (d > step) { u.X += dx / d * step; u.Z += dz / d * step; } else { u.X = tx; u.Z = tz; }
        u.Y = t.Y;
        u.Cell = t.Cell;
        u.Facing = t.Facing;
        u.Stride += step;
        u.State = d > .5f ? UnitState.Walk : UnitState.Idle;
        u.Why = t.Team == Team.Horde ? "в плену у грабителя" : t.T.Prays ? "его ведут к алтарю" : "его ведут в цитадель";
    }

    /// <summary>A thief cut down drops what it carried: the stores have it back, the worker is free.</summary>
    void ThiefFell(Unit u)
    {
        if (u.Loot > 0)
        {
            if (u.LootKind == 0) Stock.Gold += u.Loot; else Stock[u.LootKind] += u.Loot;
            Say($"Грабитель убит, украденное возвращено: {LootText(u)}.", 1, u);
            u.Loot = 0; u.LootKind = -1;
        }
        if (u.Captive != null) { u.Captive.Captor = null; u.Captive = null; }
    }

    /// <summary>A thief gets out of the valley: what it carried is gone, and the one it led off.</summary>
    void ThiefGone(Unit u)
    {
        if (u.Loot > 0 || u.Captive is { Dead: false }) _thiefAway = true;
        if (u.Loot > 0) { Say($"Грабитель ушёл из долины с добычей: {LootText(u)}.", 3, u); u.Loot = 0; u.LootKind = -1; }
        if (u.Captive is { Dead: false } c)
        {
            c.Captor = null;
            c.Dead = true; c.DiedAt = Time; c.State = UnitState.Dead; c.KilledBy = "уведён в плен";
            Say($"Грабитель увёл в плен: {c.T.Name.ToLowerInvariant()} №{c.Id}.", 3, u);
        }
        u.Captive = null;
    }
}
