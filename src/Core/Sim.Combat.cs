using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What flies at the horde: an arrow, a crossbow's bolt, a sling's stone, a pot of powder, a pot of burning pitch.</summary>
public enum Shot : byte { Arrow, Bolt, Stone, Pot, FirePot, Flask, Spear, Keg }

/// <summary>An arrow (or a bolt, a stone, a pot) in flight, from bow to where the target was expected to be.</summary>
public sealed class Arrow
{
    public float X0, Y0, Z0, X1, Y1, Z1, T, Dur;
    public Unit Target, From;
    public bool Hits, Done;
    public float Damage;
    /// <summary>A fire arrow; and the voxel (packed) it was loosed at to set alight, or −1 when it was loosed at someone.</summary>
    public bool Fire;
    public int At = -1;
    public Shot Kind;
    /// <summary>How high it rises over its flight, as a share of how far it goes: an arrow a tenth, a pot lobbed a third.</summary>
    public float Arc = .1f;
    /// <summary>A bodkin bolt gone through one raider into the next: it goes through no more.</summary>
    public bool Through;
    /// <summary>A flask of ours of choking fumes rather than acid.</summary>
    public bool Fumes;
    /// <summary>A ballista's bolt: how many raiders it has gone through already.</summary>
    public int Hops;
}

public sealed partial class Sim
{
    public readonly List<Arrow> Arrows = new();
    /// <summary>Arrows loosed since the start, and how many found their mark.</summary>
    public int Shots, ShotHits;
    /// <summary>Raiders killed since the start.</summary>
    public int Kills;
    const float ArrowSpeed = 15 * 16;   // voxels a second
    /// <summary>How fast the rest fly: a bolt faster than an arrow, a sling stone a little slower, a pot lobbed slow.</summary>
    const float BoltSpeed = 20 * 16, StoneSpeed = 13 * 16, PotSpeed = 8 * 16;
    static float SpeedOf(Shot s) => s switch { Shot.Bolt or Shot.Spear => BoltSpeed, Shot.Stone => StoneSpeed, Shot.Pot or Shot.FirePot or Shot.Flask or Shot.Keg => PotSpeed, _ => ArrowSpeed };
    static float ArcOf(Shot s) => s switch { Shot.Bolt => .04f, Shot.Spear => .03f, Shot.Stone => .08f, Shot.Pot or Shot.FirePot or Shot.Keg => .32f, Shot.Flask => .36f, _ => .1f };
    /// <summary>What a shot that kills is put down as, for the chronicle.</summary>
    static string CauseOf(Shot s) => s switch { Shot.Bolt => "арбалетный болт", Shot.Stone => "камень из пращи", Shot.Spear => SpearCause, _ => "стрела" };

    public bool AnyRaider()
    {
        foreach (var u in Units) if (!u.Dead && u.Team == Team.Horde && !u.Guard && !u.Bound) return true;
        return false;
    }

    /// <summary>Living raiders in the valley.</summary>
    public int RaidersAlive()
    {
        int n = 0;
        foreach (var u in Units) if (!u.Dead && u.Team == Team.Horde && !u.Guard && !u.Bound) n++;
        return n;
    }

    /// <summary>Living units of a kind.</summary>
    public int Count(UnitType t)
    {
        int n = 0;
        foreach (var u in Units) if (!u.Dead && u.T == t) n++;
        return n;
    }

    public static float Dist(Unit a, Unit b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z, dy = (a.Y - b.Y) * .7f;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>The nearest living unit of a team within a distance (voxels), or null.</summary>
    public Unit Nearest(Unit u, Team team, float maxDist)
    {
        Unit best = null, down = null; float bd = maxDist, dd = maxDist;
        foreach (var o in Units)
        {
            // one bound and led off is in no fight, nor one lying bound, nor an envoy under a white rag
            if (o.Dead || o.Inside || o.Captor != null || o.Bound || o.Envoy || o.Team != team) continue;
            float d = Dist(u, o);
            // one down is out of the fight: it is passed over while any of its side still stand near
            if (o.Down) { if (d < dd) { dd = d; down = o; } continue; }
            if (d < bd) { bd = d; best = o; }
        }
        return best ?? down;
    }

    /// <summary>Close enough to strike, with nothing solid between them: no hitting through walls.</summary>
    public bool InReach(Unit a, Unit b) => Dist(a, b) <= ReachOf(a) + b.T.BodyR && Sight(a, b);

    /// <summary>
    /// A blow: the stroke the striker drew back for (chosen at its last blow, or now at one it has not struck before)
    /// is laid out against the one struck, and wounds it where the blade comes to its body, cutting along the edge's
    /// sweep — down through the head from over it, across the neck from the side — or driving the point, the shield or
    /// the club in. The next stroke is chosen at once, for the eye to see it drawn back for.
    /// </summary>
    internal void Strike(Unit a, Unit b)
    {
        // the same two draws as ever: now how high the stroke comes, and to which side of the middle
        float up = (float)Rng.NextDouble(), side = (float)Rng.NextDouble();
        // seen coming, it may be got out of the way of, or taken on a shield or a blade
        var k = a.StrokeFor == b ? a.NextStroke : Choose(a, b);
        var g = Defend(a, b, k);
        if (g is Guarded.StepBack or Guarded.Dodge or Guarded.Duck) { Missed(a, b, k, g); return; }
        if (g is Guarded.Block or Guarded.Parry) { Turned(a, b, k, g); return; }
        Strike(a, b, up, side, k);
    }

    /// <summary>A blow whose height and side (0…1) are given rather than drawn: the bench aims one at the neck so.</summary>
    internal void Strike(Unit a, Unit b, float up, float side, StrokeKind? stroke = null)
    {
        a.Swing = 1;
        var (cut, depth, reach) = WeaponOf(a.T);
        var k = stroke ?? (a.StrokeFor == b ? a.NextStroke : Choose(a, b));
        LayStroke(a, b, k, up, side, out var at, out var dir, out var root, out var edge);
        (cut, depth, reach) = Strokes.CutOf(k, cut, depth, reach);
        b.HurtBy = a;
        bool engine = Kinds.IsEngine(b.T);
        // the wound is cut by the whole blade coming on along its way, from a little short of where it met the body
        var from = root - dir * 4;
        float bleed0 = b.Wounds?.Bleed ?? 0;
        float share = engine ? EngineShare(false) : Wound(b, cut, depth, reach, from.X, from.Y, from.Z, dir.X, dir.Y, dir.Z, edge.X, edge.Y, edge.Z);
        // one told to take them alive strikes with the flat and the pommel more than the edge: the wound bleeds little
        if (a.TakeAlive && Takeable(b) && b.Wounds != null) b.Wounds.Bleed = bleed0 + Math.Max(0, b.Wounds.Bleed - bleed0) * .25f;
        // where it struck, for the pain of it: a blow to the head hurts most and dazes
        b.HitPart = LastPart;
        b.HitHead = !engine && LastMat != Mat.Air && LastPart == BodyPart.Body && LastRow >= Bodies.Plan(b.T, b.Look).HeadY0;
        a.Glanced = !engine && LastMat != Mat.Air && (LastCut == 0 || LastFlesh == 0 && share < .3f);
        if (k == StrokeKind.Bash) Staggered(a, b);
        Smote?.Invoke(new Smiting(a, b, engine || LastMat == Mat.Air ? at : LastAt, dir, engine ? Mat.Board : LastMat, engine ? 0 : LastFlesh, k, a.Glanced));
        // the next, drawn back for while the arm comes round again
        a.Strokes++;
        a.NextStroke = Choose(a, b);
        a.StrokeFor = b;
        float dmg = a.T.Damage * (.8f + (float)Rng.NextDouble() * .4f) * share * Oath(a) * SergeantArm(a) * TroopArm(a, b, out bool charge) * ShieldWall(b) * Grip(a)
            * (a.Team == Team.Horde ? HordeHarm * ChampionArm(a) : 1);
        // told to take them alive: the blow that would bring a raider down to a quarter of itself (three fifths in
        // chains), or kill it, fells it instead, to be bound where it lies
        if (a.TakeAlive && !b.Dead && Takeable(b) && b.Hp - dmg <= TakeAt(b))
        {
            b.Hp = Math.Max(1, Math.Min(b.Hp, b.Hp - dmg));
            b.LastHurt = Time;
            Subdue(b, a);
            return;
        }
        Hurt(b, a, dmg);
        TroopBlow(a, b, charge);
    }

    public void Hurt(Unit b, Unit by, float dmg, string cause = null)
    {
        if (b.Dead) return;
        dmg *= MailGuard(b);
        b.Hp -= dmg;
        b.LastHurt = Time;
        if (by != null) b.HurtBy = by;
        if (b.Hp > 0) Pained(b, by, dmg); else b.HitHead = false;
        // a wounded boar turns on whoever wounded it
        if (b.Team == Team.Wild && b.T.Damage > 0 && by != null && !by.Dead) b.Target = by;
        if (b.Hp <= 0) Kill(b, by, cause);
    }

    void Kill(Unit u, Unit by, string cause)
    {
        // what dies of its wounds dies by the hand that dealt them
        by ??= u.HurtBy;
        NightFell(u, by);
        u.Dead = true; u.DiedAt = Time; u.State = UnitState.Dead; u.Hp = 0;
        u.KilledBy = cause ?? by?.T.Name ?? "";
        if (u.Team == Team.Horde && u.T != Kinds.Thrall) Kills++;
        // the reaping: a raider the spells killed gives intervention back
        if (u.Team == Team.Horde && cause != null && SpellCauses.Contains(cause) && Knows("reap")) GainFavour(2);
        if (u.T == Kinds.Thief) ThiefFell(u);
        if (u.T == Kinds.SiegeTower) TowerFell(u);
        if (u.Charge) DropCharge(u, cause ?? "");
        if (u.Captor != null) { u.Captor.Captive = null; u.Captor = null; }
        // whoever it was leading goes free (a prisoner it led lies bound where it is)
        if (u.Captive != null) { u.Captive.Captor = null; u.Captive = null; }
        if (u.Rite != null) CancelRite(u, "жрец погиб");
        ReleaseSite(u);
        StopCarve(u); StopBuild(u);
        u.Path = null; u.Target = null; u.Prey = null;
        // a leader is worth thrice its kind
        if (u.Team == Team.Horde) Stock.Gold += u.T.Bounty * (u.Leader ? 3 : 1) * (HasRelic("horn") ? 1.5 : 1);
        if (u.Champion > 0) ChampionFell(u);
        else if (u.Team == Team.Wild)
        {
            if (by?.Team == Team.Player)
            {
                Stock.Gold += u.T.Bounty; Hunted++;
                // and its carcass, for the old powers if the priests ask
                Carcasses.Add(u.T);
                Say($"Добыт {u.T.Name.ToLowerInvariant()}: +{u.T.Bounty} золота.", 1, u);
            }
        }
        else if (cause != GivenToPowers && u.T != Kinds.Risen) Say($"{u.T.Name} пал.", 3, u);
    }

    /// <summary>Beasts taken by the pawns since the start.</summary>
    public int Hunted;

    // ------------------------------------------------------------ the player's quarry

    /// <summary>Sends pawns after a beast to hunt, or after a raider to strike down.</summary>
    public int Hunt(IEnumerable<Unit> hunters, Unit prey)
    {
        if (prey == null || prey.Dead || prey.Team == Team.Player) return 0;
        int n = 0;
        foreach (var u in hunters)
        {
            if (u.Dead || u.Team != Team.Player) continue;
            ReleaseSite(u);
            u.Prey = prey; u.Hold = false; u.Path = null; u.NextSearch = 0;
            n++;
        }
        return n;
    }

    /// <summary>Chasing what the player pointed at: archers shoot from range, the rest close in. False when it is over.</summary>
    bool HuntTick(Unit u, float dt)
    {
        var p = u.Prey;
        if (p == null || p.Dead || p.Bound || Dist(u, p) > 70 * 16 || !Armed(u)) { u.Prey = null; u.Path = null; return false; }
        bool engaged = u.State == UnitState.Fight && u.Path == null;
        u.State = UnitState.Fight;
        // a bowman on the way comes on to well within its range, so that a step of the quarry's does not take it out of
        // it; standing, it shoots while the quarry is anywhere in range
        if (u.T.Ranged ? Dist(u, p) <= RangeAt(u, p.X, p.Z) * (engaged ? 1 : .7f) && Sight(u, p) : InReach(u, p))
        {
            u.Path = null;
            u.Facing = MathF.Atan2(p.Z - u.Z, p.X - u.X);
            if (u.Cooldown <= 0)
            {
                if (u.T.Ranged) Shoot(u, p); else Strike(u, p);
                u.Cooldown = u.T.AttackTime * (.9f + (float)Rng.NextDouble() * .2f);
            }
            return true;
        }
        // hand to hand, a step or so off: after it straight, still at it
        if (!u.T.Ranged && engaged && StepAfter(u, p, dt)) return true;
        if (u.Path == null || Time >= u.NextSearch)
        {
            u.NextSearch = Time + .7;
            if (!GoTo(u, p.Cell, adjacent: true, budget: 8000)) u.Path = null;
        }
        if (u.Path != null) Walk(u, dt);
        else u.State = UnitState.Idle;
        return true;
    }

    /// <summary>
    /// One fighting hand to hand whose foe has stepped a little out of its reach — up to half as far again — goes after it
    /// straight over the same ground, facing it, its blow still drawn back: true while it does. Further off, or up or
    /// down a step, or with no way straight to it, it finds a way as before.
    /// </summary>
    bool StepAfter(Unit u, Unit foe, float dt)
    {
        float dx = foe.X - u.X, dz = foe.Z - u.Z, d = MathF.Sqrt(dx * dx + dz * dz);
        if (d < .01f || d > (ReachOf(u) + foe.T.BodyR) * 1.5f || MathF.Abs(foe.Y - u.Y) > 4 || !Sight(u, foe)) return false;
        float step = u.T.Move.Speed * NavGrid.CS * u.Pace * dt * .9f;
        float nx = u.X + dx / d * step, nz = u.Z + dz / d * step;
        int at = Nav.CellOf(nx, u.Y + 1, nz);
        if (at != u.Cell && !Passable(u, at)) return false;
        u.X = nx; u.Z = nz; u.Cell = at; u.Y = Nav.Floor[at];
        u.Stride += step;
        u.Facing = MathF.Atan2(dz, dx);
        u.Path = null;
        return true;
    }

    /// <summary>Soldiers: shoot or close in; true while busy fighting.</summary>
    bool Fight(Unit u, float dt)
    {
        if (u.T.Ranged)
        {
            // an alchemist of ours throws at the horde's works first (Sim.OurAlchemists.cs)
            if (u.T == Kinds.OurAlchemist) return OurAlchemistTick(u, dt);
            // what the player pointed at to set alight comes first
            if (u.KindleAt >= 0 && KindleShot(u, dt)) return true;
            var t = u.Target;
            if (t == null || t.Dead || Dist(u, t) > RangeAt(u, t.X, t.Z) || Time >= u.NextSearch)
            {
                t = BestTarget(u);
                u.Target = t;
                u.NextSearch = Time + .4;
            }
            if (t == null) return KindleShot(u, dt);
            // a bombardier holds his pot while one of ours is by the raider
            if (u.T.Tool == "bombard" && !PotSafe(t)) { u.Target = null; u.NextSearch = Time + .3; u.Cooldown = Math.Max(u.Cooldown, .3f); }
            // sent somewhere and not there yet: shoot on the way, but keep going
            bool marching = u.Hold && u.Dest >= 0 && CellsApart(u.Cell, u.Dest) > 1 && u.Path != null && u.PathI < u.Path.Count;
            if (!marching) { u.State = UnitState.Fight; u.Path = null; u.Facing = MathF.Atan2(t.Z - u.Z, t.X - u.X); }
            // a fire arrow takes a moment to light
            if (u.Cooldown <= 0) { Shoot(u, t); u.Cooldown = u.T.AttackTime * ShotPace(u) * (u.FireArrows ? Knows("soaked") ? 1.1f : 1.35f : 1) * (.9f + (float)Rng.NextDouble() * .2f) / Drum; }
            return !marching;
        }
        // swordsmen hold the ground round themselves or their post
        float leash = 6 * 16;
        var foe = Nearest(u, Team.Horde, leash);
        if (foe == null && u.Post >= 0)
        {
            var (px, py, pz) = Nav.Feet(u.Post);
            foreach (var o in Units)
            {
                if (o.Dead || o.Team != Team.Horde || o.Bound || o.Inside || o.Envoy) continue;
                float dx = o.X - px, dz = o.Z - pz;
                if (dx * dx + dz * dz < leash * leash) { foe = o; break; }
            }
        }
        // one of ours set upon close by: to its help, past the post if need be
        if (foe == null)
        {
            float best = 22 * VoxelWorld.PerMetre;
            foreach (var o in Units)
            {
                if (o.Dead || o.Team != Team.Horde || o.State != UnitState.Fight || o.Target is not { Dead: false, Team: Team.Player }) continue;
                float d = Dist(u, o);
                if (d < best) { best = d; foe = o; }
            }
        }
        // hounds and riders run down a raider that flees, unless one is at them already
        if (Hunter(u.T) && (foe == null || !InReach(u, foe)) && Quarry(u) is { } quarry) foe = quarry;
        // a giant or a tame troll hurls a stone at one out of its reach
        if ((Hurling(u) || foe == null || !InReach(u, foe)) && HurlBoulder(u)) { ReleaseSite(u); return true; }
        if (foe == null) return false;
        bool engaged = u.State == UnitState.Fight && u.Target == foe && u.Path == null;
        u.State = UnitState.Fight;
        u.Target = foe;
        ReleaseSite(u);
        if (InReach(u, foe))
        {
            u.Path = null;
            u.Facing = MathF.Atan2(foe.Z - u.Z, foe.X - u.X);
            if (u.Cooldown <= 0) { Strike(u, foe); u.Cooldown = u.T.AttackTime * (.9f + (float)Rng.NextDouble() * .2f) / (PainPace(u) * Drum); }
            Footwork(u, foe, dt);
            return true;
        }
        // the foe a step out of reach: after it, without stopping to find a way
        if (engaged && StepAfter(u, foe, dt)) return true;
        if (u.Path == null || Time >= u.NextSearch)
        {
            u.NextSearch = Time + .5;
            if (!GoTo(u, foe.Cell, adjacent: true, budget: 6000)) u.Path = null;
        }
        if (u.Path != null) Walk(u, dt);
        return true;
    }

    // ------------------------------------------------------------ the dark

    /// <summary>
    /// Patches of four metres where it is light at night: by the torches on the walls, by fires, round the citadel, round
    /// a raider carrying a torch and round anyone who burns.
    /// </summary>
    readonly HashSet<int> _lit = new();
    double _litAt = -99;

    static int LitKey(int x, int z) => (x >> 5) | (z >> 5) << 10;

    /// <summary>Lays out once a second where it is light.</summary>
    void TickLight()
    {
        if (Time - _litAt < 1) return;
        _litAt = Time;
        _lit.Clear();
        // by day the whole valley is light
        if (Waves.Darkness <= 0) return;
        void Around(float x, float z, float r)
        {
            for (float dz = -r; dz <= r; dz += 16)
                for (float dx = -r; dx <= r; dx += 16)
                    if (dx * dx + dz * dz <= r * r) _lit.Add(LitKey((int)Math.Clamp(x + dx, 0, World.SX - 1), (int)Math.Clamp(z + dz, 0, World.SZ - 1)));
        }
        // a torch a witch put out gives no light till it is lit again
        foreach (var f in Fixtures) if (f.Kind == FixtureKind.Torch && f.DarkUntil <= Time) Around(f.LX, f.LZ, TorchLight);
        Around(Site.KeepX, Site.KeepZ, Site.KeepR + 8 * VoxelWorld.PerMetre);
        int n = 0;
        foreach (int p in Burning.Keys)
        {
            if (n++ % 7 != 0) continue;
            var (x, _, z) = WaterPlanner.Unpack(p);
            Around(x, z, 5 * VoxelWorld.PerMetre);
        }
        foreach (var u in Units)
            if (!u.Dead && (u.Torch || u.Burn > 0)) Around(u.X, u.Z, 3 * VoxelWorld.PerMetre);
    }

    /// <summary>Whether a spot is lit at night.</summary>
    public bool Lit(float x, float z) => Time < BloodMoonUntil || _lit.Contains(LitKey((int)Math.Clamp(x, 0, World.SX - 1), (int)Math.Clamp(z, 0, World.SZ - 1)));

    /// <summary>How far a bow carries into the dark now: all the way by day, little more than half at night.</summary>
    public float DarkRange(Unit a) => ShotRange(a) * FogRange * (1 - .45f * Waves.Darkness * ShotDark(a));

    /// <summary>How far a bow carries to a spot: all the way by day or where it is light, little more than half in the dark.</summary>
    public float RangeAt(Unit a, float x, float z) => Waves.Darkness <= 0 || Lit(x, z) || HasRelic("eye") && a.Team == Team.Player ? ShotRange(a) * FogRange : DarkRange(a);

    /// <summary>A raider in range and in sight; the ones hacking walls or battering the citadel come first.</summary>
    Unit BestTarget(Unit u)
    {
        Unit best = null; float bd = float.MaxValue;
        foreach (var o in Units)
        {
            if (o.Dead || o.Team != Team.Horde || o.Envoy || o.Bound || o.Inside) continue;
            float d = Dist(u, o);
            if (d > RangeAt(u, o.X, o.Z)) continue;
            // a pot is not thrown where one of ours is by the raider
            if (u.T.Tool == "bombard" && !PotSafe(o)) continue;
            // those cutting down one of ours come first, then those hacking the walls, battering the citadel, raising
            // ladders or climbing them
            bool hurting = o.State == UnitState.Fight && o.Target is { Dead: false, Team: Team.Player };
            bool busy = o.State is UnitState.Carve or UnitState.Siege or UnitState.Work || Climbing(o);
            // a thief making off with what it took, or with one of ours, before anything; a sapper with a charge next
            bool making = o.Loot > 0 || o.Captive != null, charged = o.Charge;
            // an engine: hardly worth a plain arrow, the first mark for a fire arrow; a sorcerer before anyone
            // (a pot of powder breaks an engine up as well as fire does)
            float engine = Kinds.IsEngine(o.T) ? (u.FireArrows || u.T.Tool == "bombard" ? -96 : 96) : Kinds.IsCaster(o.T) || o.T == Kinds.Alchemist ? -110 : 0;
            // with fire arrows, those standing on a deck, a ladder or pitch: one arrow sets it all alight under them
            float pri = d - (making ? 96 : charged ? 80 : hurting ? 64 : busy ? 48 : 0) - (u.FireArrows && OnTinder(o) ? 40 : 0) + engine;
            if (pri >= bd || !Sight(u, o)) continue;
            bd = pri; best = o;
        }
        return best;
    }

    /// <summary>A clear line from the archer's eyes to the target's chest; leaves don't stop arrows.</summary>
    public bool Sight(Unit a, Unit b) => Sees(a, b.X, b.Y + b.T.BodyH * .55f, b.Z);

    /// <summary>A clear line from a unit's eyes to a point (voxels); leaves don't stop arrows.</summary>
    public bool Sees(Unit a, float bx, float by, float bz)
    {
        float ax = a.X, ay = a.Y + a.T.BodyH * .85f, az = a.Z;
        float dx = bx - ax, dy = by - ay, dz = bz - az, len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1) return true;
        if (!Raycast.Cast(World, ax, ay, az, dx / len, dy / len, dz / len, out var h, skipVeg: true)) return true;
        float hx = h.X + .5f - ax, hy = h.Y + .5f - ay, hz = h.Z + .5f - az;
        return MathF.Sqrt(hx * hx + hy * hy + hz * hz) >= len - 3;
    }

    void Shoot(Unit u, Unit t)
    {
        // a mantlet before the one aimed at takes the arrow
        t = Cover(u, t) ?? t;
        float d = Dist(u, t);
        float chance = Math.Clamp(.9f - d / ShotRange(u) * .45f + (u.Y - t.Y > 16 ? .08f : 0), .35f, .92f);
        // a shape in the dark is harder to hit than one in the light; rain and wind spoil aim
        if (!Lit(t.X, t.Z)) chance *= 1 - .2f * Waves.Darkness * ShotDark(u);
        chance *= WeatherAim;
        u.Swing = 1;
        Shots++;
        // a stone of lead strikes a third harder; now and then a slinger with the lead shot and pots throws one of burning pitch
        var kind = ShotOf(u.T);
        float harm = kind == Shot.Stone && Knows("leadshot") ? 1.33f : 1;
        if (kind == Shot.Stone && Knows("leadshot") && Rng.NextDouble() < FirePotShare && PotSafe(t)) kind = Shot.FirePot;
        float scatter = kind is Shot.Pot or Shot.FirePot ? 10 : 4;
        Arrows.Add(new Arrow
        {
            X0 = u.X, Y0 = u.Y + u.T.BodyH * .85f, Z0 = u.Z,
            X1 = t.X + (float)(Rng.NextDouble() - .5) * scatter, Y1 = t.Y + t.T.BodyH * .55f, Z1 = t.Z + (float)(Rng.NextDouble() - .5) * scatter,
            Dur = Math.Max(.08f, d / SpeedOf(kind)), Target = t, From = u, Hits = Rng.NextDouble() < chance,
            Damage = u.T.Damage * (.8f + (float)Rng.NextDouble() * .5f) * ShotHarm(u) * harm * Oath(u), Fire = u.FireArrows && Kindles(u.T), Kind = kind, Arc = ArcOf(kind),
        });
    }

    /// <summary>How many of a slinger's throws are pots of burning pitch, with the fire pots.</summary>
    const double FirePotShare = .25;

    void TickArrows(float dt)
    {
        foreach (var a in Arrows)
        {
            a.T += dt;
            if (a.T < a.Dur || a.Done) continue;
            a.Done = true;
            // an alchemist's flask breaks and lets out its acid; ours eats what the horde has raised (Sim.OurAlchemists.cs)
            if (a.Kind == Shot.Flask) { if (a.From?.Team == Team.Player) OurFlaskLands(a); else FlaskLands(a); continue; }
            // a pot bursts, or breaks and burns, wherever it comes down, on the raider or by it
            if (a.Kind == Shot.Pot) { if (a.Hits) ShotHits++; var (px, py, pz) = PotDown(a); PotBurst(px, py, pz, a.Damage, a.From); continue; }
            if (a.Kind is Shot.FirePot or Shot.Keg) { if (a.Hits) ShotHits++; PitchLands(a); continue; }
            if (a.Hits && a.Target != null && !a.Target.Dead)
            {
                ShotHits++;
                // the arrow comes in along its flight, at a spot on the body where it was going
                var t = a.Target;
                float st = t.Leader ? 1.15f : 1;
                // coming down at the end of its arc (it rose a tenth of the way it flew)
                float dx = a.X1 - a.X0, dz = a.Z1 - a.Z0, dy = a.Y1 - a.Y0 - .4f * MathF.Sqrt(dx * dx + dz * dz);
                float hy = t.Y + t.T.BodyH * st * (.25f + (float)Rng.NextDouble() * .7f);
                float side = (float)(Rng.NextDouble() - .5) * t.T.BodyR * 1.4f, l = MathF.Max(.01f, MathF.Sqrt(dx * dx + dz * dz));
                float tx = t.X - dz / l * side, tz = t.Z + dx / l * side;
                float dl = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                var (cut, depth, reach) = WeaponOf(a.From?.T ?? Kinds.Archer);
                if (a.From != null) t.HurtBy = a.From;
                // a shaft goes in along a narrow channel; a stone strikes a little round, and does not stay in
                // (a ballista's bolt breaks into an engine as a blade would, and more)
                float share = Kinds.IsEngine(t.T) ? (a.Kind == Shot.Spear ? 1.2f : EngineShare(true)) : Wound(t, cut, depth, cut == Cut.Arrow ? 0 : reach, tx - dx / dl * 30, hy - dy / dl * 30, tz - dz / dl * 30, dx, dy, dz);
                Hurt(t, a.From, a.Damage * share * HideShare(t, a.Kind), CauseOf(a.Kind));
                if (a.Kind == Shot.Stone) StoneStrikes(a, t);
                if (a.Kind == Shot.Bolt && !Kinds.IsEngine(t.T)) Pierce(a, t);
                if (a.Kind == Shot.Spear && !Kinds.IsEngine(t.T)) SpearOn(a, t);
            }
            if (a.Fire) FireArrowLands(a);
        }
        // a bodkin's bolt on into the next one behind
        if (_through.Count > 0) { Arrows.AddRange(_through); _through.Clear(); }
        Arrows.RemoveAll(a => a.Done && a.T > a.Dur + 1.5f);
    }
}
