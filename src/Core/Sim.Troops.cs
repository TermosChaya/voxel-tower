using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The troops the lore opens, at their trades. A spear strikes from where no sword reaches, and with the long shafts
/// further still; a shieldbearer covers those beside him with his tower shield. A sling stone knocks the wind out of a
/// raider and does not stick in it — cast in lead it strikes harder and knocks it back a step — and with the fire pots a
/// slinger now and then throws a pot of burning pitch instead. A crossbow bolt goes through mail, and with bodkin heads
/// through the first raider into the next behind it. A bombardier's pot of powder bursts where it comes down, among the
/// raiders and their ladders and decks, but leaves our own walls be; he never throws at a raider one of ours stands by.
/// Hounds and riders run down whatever flees, and with the hunt faster and harder; a rider's blow from the gallop is a
/// charge that throws a raider off its feet (a lancer's always is). A giant's or a tame troll's blow sends a raider
/// flying, and with the lore they tear stones out of the ground and hurl them at raiders out of reach, to come down and
/// roll on through the rest. A tame troll's flesh knits as a troll's does. No horse and no hound goes up a ladder.
/// The bows' lore is the bows' alone: a crossbow, a sling or a pot keeps its own reach and pace.
/// </summary>
public sealed partial class Sim
{
    /// <summary>A sling stone's knock, a charge's throw and a great one's blow, in metres a second out and up.</summary>
    const float KnockOut = 2.6f * VoxelWorld.PerMetre, KnockUp = 2 * VoxelWorld.PerMetre;
    const float ChargeOut = 5 * VoxelWorld.PerMetre, ChargeUp = 3.5f * VoxelWorld.PerMetre;
    const float GreatOut = 7 * VoxelWorld.PerMetre, GreatUp = 4.5f * VoxelWorld.PerMetre;
    /// <summary>How near a shieldbearer covers ours; how far a bodkin goes on through; how near ours a pot is not thrown.</summary>
    const float WallReach = 3 * VoxelWorld.PerMetre, PierceReach = 5 * VoxelWorld.PerMetre, PotClear = 2 * VoxelWorld.PerMetre;
    /// <summary>How far a pot's burst breaks the horde's works, voxels; it hurts and throws half again as far.</summary>
    public const float PotReach = 1.25f * VoxelWorld.PerMetre;
    /// <summary>How far hounds and riders run after one that flees, and how long a gallop holds for a charge, seconds.</summary>
    const float ChaseReach = 25 * VoxelWorld.PerMetre;
    const double GallopHolds = .8;
    /// <summary>A tame troll mending out of a fight, hit points a second; how often a great one hurls a stone, seconds.</summary>
    const float TrollMend = 2;
    const double HurlEvery = 8;
    /// <summary>The stones the great ones throw: reaching this far from the middle (half a metre across), and what they are called.</summary>
    const float BoulderR = 2.3f;
    public const string BoulderCause = "камень великана";

    // ------------------------------------------------------------ who is who

    static bool Polearm(UnitType t) => t.Tool is "spear" or "halberd";
    /// <summary>Hounds and riders: they run down whatever flees.</summary>
    static bool Hunter(UnitType t) => t.Team == Team.Player && (t.Mounted || t.NoHands);
    /// <summary>A giant or a tame troll: its blow sends a raider flying.</summary>
    static bool Great(UnitType t) => t == Kinds.Giant || t == Kinds.TameTroll;
    static bool Bow(UnitType t) => t.Tool == "bow";
    /// <summary>Whether a kind can loose fire: a bow or a crossbow can, a sling or a pot of powder cannot.</summary>
    public static bool Kindles(UnitType t) => t.Ranged && t.Tool is "bow" or "crossbow";
    /// <summary>What a kind of ours throws or shoots.</summary>
    static Shot ShotOf(UnitType t) => t.Tool switch { "crossbow" => Shot.Bolt, "sling" => Shot.Stone, "bombard" => Shot.Pot, _ => Shot.Arrow };
    /// <summary>A stone the great ones threw: it passes ours by.</summary>
    static bool Spares(Flier f, Unit u) => u.Team == Team.Player && f.Cause == BoulderCause;

    // ------------------------------------------------------------ reach, range and pace

    /// <summary>How far one strikes from, voxels: a spear or a halberd of ours a quarter further with the long shafts.</summary>
    public float ReachOf(Unit a) => a.T.Reach * (a.Team == Team.Player && Polearm(a.T) && Knows("longshafts") ? 1.25f : 1);

    /// <summary>How far a shot of ours carries by day: a bow by the lore of bows, a crossbow, a sling or a pot by its own.</summary>
    public float ShotRange(Unit a) => Bow(a.T) ? BowRange(a) : a.T.Range;
    /// <summary>How much of its reach and aim the dark takes, as for a bow; only a bow's eye is the owl's.</summary>
    float ShotDark(Unit a) => Bow(a.T) ? DarkCost(a) : 1;
    /// <summary>How long between shots, as a share: only bows loose faster for the volleys.</summary>
    float ShotPace(Unit a) => Bow(a.T) ? VolleyPace(a) : 1;
    /// <summary>How much harder our shafts strike with faceted heads: arrows and bolts, not stones.</summary>
    float ShotHarm(Unit a) => a.Team == Team.Player && a.T.Tool is "bow" or "crossbow" ? ArrowHarm : 1;

    /// <summary>A third faster after one that flees, for a hound or a rider with the hunt; a soldier drilled on the drill yard, a seventh faster always.</summary>
    float TroopPace(Unit u) => (Hunter(u.T) && Knows("hunt") && u.Target is { Routed: true, Dead: false } ? 1.33f : 1) * (Soldier(u) && Knows("drillyard") ? 1.15f : 1);

    // ------------------------------------------------------------ every tick

    /// <summary>A troop of ours each tick, before whatever else it does: whether a rider is at the gallop; a tame troll mending.</summary>
    void TroopTick(Unit u, float dt)
    {
        if (u.T.Mounted)
        {
            if (!float.IsNaN(u.WasX) && dt > 0)
            {
                float dx = u.X - u.WasX, dz = u.Z - u.WasZ, speed = MathF.Sqrt(dx * dx + dz * dz) / dt, full = u.T.Move.Speed * NavGrid.CS;
                // at more than half its pace — and not set down somewhere else at a stroke
                if (speed > .55f * full && speed < 2.5f * full) u.GallopUntil = Time + GallopHolds;
            }
            u.WasX = u.X; u.WasZ = u.Z;
        }
        // a troll's flesh knits as you watch, out of the fight
        if (u.T == Kinds.TameTroll && u.Hp < u.MaxHp && Time - u.LastHurt > 3 && u.State != UnitState.Fight)
            u.Hp = Math.Min(u.MaxHp, u.Hp + TrollMend * dt);
    }

    // ------------------------------------------------------------ blows

    /// <summary>
    /// How much harder one of ours strikes this blow: a hound or a rider twice as hard at one that flees (with the hunt);
    /// a rider straight from the gallop twice as hard (a lancer always, the rest with the charge) — and the charge is
    /// spent in it.
    /// </summary>
    float TroopArm(Unit a, Unit b, out bool charge)
    {
        charge = false;
        if (a.Team != Team.Player) return 1;
        float k = 1;
        if (Hunter(a.T) && b.Routed && Knows("hunt")) k *= 2;
        if (a.T.Mounted && Time < a.GallopUntil && (a.T == Kinds.Lancer || Knows("charge")))
        {
            k *= 2;
            charge = true;
            a.GallopUntil = -1;
        }
        return k;
    }

    /// <summary>How much of a blow one of ours takes: a quarter less within three metres of a shieldbearer, with the shield wall.</summary>
    internal float ShieldWall(Unit b)
    {
        if (b.Team != Team.Player || !(b.T.Work < 1 || b.Militia) || !Knows("shieldwall")) return 1;
        foreach (var o in Units)
        {
            // one who has lost his shield arm covers nobody
            if (o == b || o.Dead || o.Inside || o.T != Kinds.Shieldbearer || o.Wounds is { Shield: false }) continue;
            float dx = o.X - b.X, dz = o.Z - b.Z;
            if (dx * dx + dz * dz <= WallReach * WallReach && MathF.Abs(o.Y - b.Y) < 2 * VoxelWorld.PerMetre) return .75f;
        }
        return 1;
    }

    /// <summary>
    /// After a blow of ours lands: a giant's or a tame troll's sends the raider flying, a charge throws it off its
    /// feet — the bigger it is, the less; an engine stands.
    /// </summary>
    void TroopBlow(Unit a, Unit b, bool charge)
    {
        if (a.Team != Team.Player || b.Team == Team.Player || b.Inside || Kinds.IsEngine(b.T)) return;
        float out_, up;
        string cause;
        if (Great(a.T)) { out_ = GreatOut; up = GreatUp; cause = a.T == Kinds.Giant ? "удар великана" : "удар тролля"; }
        else if (charge) { out_ = ChargeOut; up = ChargeUp; cause = "натиск всадника"; }
        else return;
        float light = MathF.Min(1, 15 / b.T.BodyH), dx = b.X - a.X, dz = b.Z - a.Z, d = MathF.Sqrt(dx * dx + dz * dz);
        if (d < .5f) { dx = MathF.Cos(a.Facing); dz = MathF.Sin(a.Facing); d = 1; }
        Fling(b, dx / d * out_ * light, up * light, dz / d * out_ * light, cause);
    }

    // ------------------------------------------------------------ hounds and riders after those who flee

    /// <summary>The nearest raider fleeing within a hound's or a rider's run, or nobody.</summary>
    Unit Quarry(Unit u)
    {
        Unit best = null;
        float bd = ChaseReach;
        foreach (var o in Units)
        {
            if (o.Dead || !o.Routed || o.Team != Team.Horde || o.Inside || o.Bound || o.Envoy || o.Captor != null) continue;
            float d = Dist(u, o);
            if (d < bd) { bd = d; best = o; }
        }
        return best;
    }

    // ------------------------------------------------------------ the great ones' stones

    /// <summary>
    /// A giant or a tame troll with the lore of boulders, a raider beyond its reach: it tears a stone out of the ground
    /// before it (or takes one up, where the ground is ours or has none to give) and hurls it high, to come down on the
    /// raider and roll on through whoever is behind. Once every eight seconds or so; false if there is none to throw at.
    /// </summary>
    bool HurlBoulder(Unit u)
    {
        if (!Great(u.T) || !Knows("boulders") || Time < u.NextHurl || !HasMainHand(u)) return false;
        Unit at = null;
        float bd = 24 * VoxelWorld.PerMetre;
        foreach (var o in Units)
        {
            if (o.Dead || o.Team != Team.Horde || o.Inside || o.Bound || o.Envoy || o.Airborne != null) continue;
            float d = Dist(u, o);
            if (d >= bd || d < ReachOf(u) + o.T.BodyR + 2 * VoxelWorld.PerMetre || !Sight(u, o)) continue;
            bd = d; at = o;
        }
        if (at == null) { u.NextHurl = Time + 1; return false; }
        u.NextHurl = Time + HurlEvery * (.9f + .2f * (float)Rng.NextDouble());
        u.State = UnitState.Fight;
        u.Path = null;
        u.Facing = MathF.Atan2(at.Z - u.Z, at.X - u.X);
        u.Swing = 1;
        u.Cooldown = u.T.AttackTime;
        // the stone out of the ground before its feet, if the ground there is the valley's own
        float fx = MathF.Cos(u.Facing), fz = MathF.Sin(u.Facing);
        int sx = (int)(u.X + fx * (u.T.BodyR + 3)), sz = (int)(u.Z + fz * (u.T.BodyR + 3)), sy = (int)MathF.Floor(u.Y) - 2;
        byte stone = Mat.Rock;
        var hole = new List<(int x, int y, int z)>();
        int ri = (int)MathF.Ceiling(BoulderR);
        // not from under anything of ours: a wall over the hole would come down
        bool ours = false;
        for (int y = sy - 3; y <= sy + 8 && !ours; y++)
            for (int z = sz - 4; z <= sz + 4 && !ours; z++)
                for (int x = sx - 4; x <= sx + 4 && !ours; x++)
                    if (World.InBounds(x, y, z) && Mat.IsPlayer[World.Get(x, y, z)]) ours = true;
        for (int dy = -ri; dy <= ri && !ours; dy++)
            for (int dz = -ri; dz <= ri; dz++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    int x = sx + dx, y = sy + dy, z = sz + dz;
                    if (dx * dx + dy * dy + dz * dz > BoulderR * BoulderR || !World.InBounds(x, y, z) || y < 4) continue;
                    byte m = World.Get(x, y, z);
                    if (m == Mat.Air || m == Mat.Water || Mat.IsPlayer[m] || !Mat.IsNatural[m] || !Breakable(m)) continue;
                    hole.Add((x, y, z));
                }
        if (hole.Count >= 20)
        {
            foreach (var (x, y, z) in hole)
            {
                World.Set(x, y, z, Mat.Air);
                if (Orders.At(x, y, z) != 0) Orders.Clear(x, y, z);
            }
            Hacked?.Invoke(sx, sy, sz, Mat.Rock);
        }
        // and away: from over its head, so long in the air that it comes down where the raider stands
        float x0 = u.X + fx * 2, y0 = u.Y + u.T.BodyH + 2, z0 = u.Z + fz * 2;
        float d0 = MathF.Sqrt((at.X - x0) * (at.X - x0) + (at.Z - z0) * (at.Z - z0)), t = .8f + d0 / (28 * VoxelWorld.PerMetre);
        float tx = at.X + (float)(Rng.NextDouble() - .5) * 8, tz = at.Z + (float)(Rng.NextDouble() - .5) * 8, ty = at.Y + BoulderR;
        var f = Hurl(Ball(BoulderR, stone), x0, y0, z0, (tx - x0) / t, (ty - y0 + .5f * Gravity * t * t) / t, (tz - z0) / t, BoulderCause);
        if (f != null) { f.By = u; Hurled++; }
        return true;
    }

    /// <summary>Stones the great ones have hurled since the start.</summary>
    public int Hurled;

    // ------------------------------------------------------------ what the shots do where they land

    /// <summary>A stone that struck: with lead, it knocks the raider a step back.</summary>
    void StoneStrikes(Arrow a, Unit t)
    {
        if (!Knows("leadshot") || t.Dead || t.Team != Team.Horde || Kinds.IsEngine(t.T) || t.Inside) return;
        float dx = a.X1 - a.X0, dz = a.Z1 - a.Z0, l = MathF.Max(.01f, MathF.Sqrt(dx * dx + dz * dz)), light = MathF.Min(1, 15 / t.T.BodyH);
        Fling(t, dx / l * KnockOut * light, KnockUp * light, dz / l * KnockOut * light, "камень из пращи");
    }

    /// <summary>Bolts that went through one raider into the next, waiting to be let fly once the others are counted.</summary>
    readonly List<Arrow> _through = new();

    /// <summary>
    /// A bodkin bolt through the one it struck: it goes on along its flight into the next raider behind, within five
    /// metres and near its line, with what is left of its force; it goes through no more.
    /// </summary>
    void Pierce(Arrow a, Unit t)
    {
        if (a.Through || !Knows("bodkins")) return;
        float dx = a.X1 - a.X0, dz = a.Z1 - a.Z0, l = MathF.Sqrt(dx * dx + dz * dz);
        if (l < 1) return;
        float ux = dx / l, uz = dz / l;
        Unit next = null;
        float best = PierceReach;
        foreach (var o in Units)
        {
            if (o == t || o.Dead || o.Team != Team.Horde || o.Inside || o.Bound || o.Envoy) continue;
            float ox = o.X - t.X, oz = o.Z - t.Z, along = ox * ux + oz * uz;
            if (along <= 0 || along > best || MathF.Abs(ox * uz - oz * ux) > o.T.BodyR + 2 || MathF.Abs(o.Y - t.Y) > 12) continue;
            best = along; next = o;
        }
        if (next == null) return;
        _through.Add(new Arrow
        {
            X0 = t.X, Y0 = t.Y + t.T.BodyH * .55f, Z0 = t.Z, X1 = next.X, Y1 = next.Y + next.T.BodyH * .55f, Z1 = next.Z,
            Dur = Math.Max(.03f, best / BoltSpeed), Target = next, From = a.From, Hits = true, Damage = a.Damage * .7f, Kind = Shot.Bolt, Arc = 0, Through = true,
        });
        Pierced++;
    }

    /// <summary>Bodkin bolts that went on into a second raider since the start.</summary>
    public int Pierced;

    /// <summary>Where a pot came down: on the raider it was thrown at, or on the ground by where it fell.</summary>
    (float x, float y, float z) PotDown(Arrow a)
    {
        if (a.Hits && a.Target is { Dead: false } t) return (t.X, t.Y + 2, t.Z);
        int x = Math.Clamp((int)a.X1, 1, World.SX - 2), z = Math.Clamp((int)a.Z1, 1, World.SZ - 2);
        for (int y = Math.Min(World.SY - 2, (int)a.Y1 + 4), end = y - 40; y > end && y > 1; y--)
        {
            byte m = World.Get(x, y - 1, z);
            if (m != Mat.Air && !Mat.Passable[m]) return (a.X1, y + 1, a.Z1);
        }
        return (a.X1, a.Y1, a.Z1);
    }

    /// <summary>
    /// A bombardier's pot of powder bursts: a small blast a metre and a quarter about it. The horde's works in it — a
    /// siege ladder (all of it comes down, with whoever was on it), a deck, a tower's ladder — break and fly in splinters,
    /// and its engines are battered; whoever is within half again as far is hurt, the nearer the worse, and thrown out
    /// of it. Our walls and timber, and the ground, it leaves be. Returns how much it struck: voxels broken and bodies.
    /// </summary>
    public int PotBurst(float fx, float fy, float fz, float harm, Unit by)
    {
        float r = PotReach;
        int ri = (int)MathF.Ceiling(r), x0 = (int)fx, y0 = (int)fy, z0 = (int)fz, done = 0;
        var splinters = new List<(int x, int y, int z)>();
        for (int dy = -ri; dy <= ri; dy++)
            for (int dz = -ri; dz <= ri; dz++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    int x = x0 + dx, y = y0 + dy, z = z0 + dz;
                    if (dx * dx + dy * dy + dz * dz > r * r || !World.InBounds(x, y, z)) continue;
                    byte m = World.Get(x, y, z);
                    // a siege ladder comes down whole from where it is struck, and whoever was on it
                    if (m == Mat.SiegeLadder) { done += ThrowDown(x, y, z); continue; }
                    if (m is Mat.Deck or Mat.TowerLadder) splinters.Add((x, y, z));
                }
        if (splinters.Count > 0)
        {
            done += splinters.Count;
            int room = MaxAloft - Aloft;
            foreach (var piece in Split(splinters, 30))
            {
                float cx = 0, cy = 0, cz = 0;
                foreach (var (px, py, pz) in piece) { cx += px; cy += py; cz += pz; }
                cx = cx / piece.Count - fx; cy = cy / piece.Count - fy; cz = cz / piece.Count - fz;
                float d = MathF.Max(.5f, MathF.Sqrt(cx * cx + cy * cy + cz * cz)), speed = 6 * VoxelWorld.PerMetre;
                var f = piece.Count <= room ? Loose(piece, cx / d * speed, (MathF.Max(0, cy / d) + .6f) * speed, cz / d * speed, "горшок с порохом", room) : null;
                if (f != null) { room -= f.Voxels.Count; continue; }
                foreach (var (px, py, pz) in piece) World.Set(px, py, pz, Mat.Air);
            }
        }
        // those about it: hurt and thrown out of it; an engine battered where it stands
        float reach = r * 1.5f;
        var near = Units.FindAll(u => !u.Dead && !u.Inside && MathF.Abs(u.X - fx) < reach + u.T.BodyR && MathF.Abs(u.Z - fz) < reach + u.T.BodyR);
        int broke = done;
        foreach (var u in near)
        {
            // ours it passes by: the pot is thrown with a care for them, and the burst is the horde's
            if (u.Team == Team.Player) continue;
            float dx = u.X - fx, dz = u.Z - fz, dy = u.Y + MathF.Min(u.T.BodyH * .5f, 8) - fy;
            float d = MathF.Max(0, MathF.Sqrt(dx * dx + dy * dy + dz * dz) - u.T.BodyR * .5f);
            if (d > reach) continue;
            float k = 1 - d / reach;
            done++;
            if (Kinds.IsEngine(u.T)) { Hurt(u, by, harm * 2.5f * (.5f + .5f * k), "горшок с порохом"); continue; }
            Hurt(u, by, harm * (.35f + .65f * k), "горшок с порохом");
            float h = MathF.Sqrt(dx * dx + dz * dz), ux = h > .5f ? dx / h : MathF.Cos(u.Id), uz = h > .5f ? dz / h : MathF.Sin(u.Id);
            float push = (2.5f + 4 * k) * VoxelWorld.PerMetre * MathF.Min(1, 15 / u.T.BodyH);
            Fling(u, ux * push, push * .8f, uz * push, "горшок с порохом");
        }
        // the horde's ways are worked out again only when what it walks on changed, not for every body struck
        if (broke > 0) InvalidateFields();
        Exploded?.Invoke(fx, fy, fz, r);
        Bursts++;
        return done;
    }

    /// <summary>Pots of powder burst since the start.</summary>
    public int Bursts;

    /// <summary>Whether a pot may be thrown at a raider: none of ours within two metres of it.</summary>
    bool PotSafe(Unit t)
    {
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Team != Team.Player) continue;
            float dx = o.X - t.X, dz = o.Z - t.Z;
            if (dx * dx + dz * dz < (PotClear + o.T.BodyR) * (PotClear + o.T.BodyR) && MathF.Abs(o.Y - t.Y) < 3 * VoxelWorld.PerMetre) return false;
        }
        return true;
    }

    /// <summary>
    /// A pot of burning pitch comes down: it breaks on the raider it was thrown at (or by it), sets it alight and
    /// splashes the pitch over the ground about it, burning. (A slinger throws one only where none of ours is near.)
    /// </summary>
    void PitchLands(Arrow a)
    {
        var (px, py, pz) = PotDown(a);
        if (a.Hits && a.Target is { Dead: false } t)
        {
            t.Burn = Math.Max(t.Burn, Kinds.IsEngine(t.T) ? 15 : 6);
            t.BurnHeat = Math.Max(t.BurnHeat, 2);
            Hurt(t, a.From, a.Damage * .5f, "горящая смола");
        }
        int lit = 0;
        for (int i = 0; i < 5; i++)
        {
            // where it broke, and a splash to each side
            int x = (int)px + (i == 0 ? 0 : i < 3 ? 2 * (i * 2 - 3) : 0) + Rng.Next(-1, 2), z = (int)pz + (i < 3 ? 0 : 2 * (i * 2 - 7)) + Rng.Next(-1, 2);
            if (!World.InBounds(x, (int)py, z)) continue;
            // down to whatever is under it there, and onto it
            int y = Math.Min(World.SY - 2, (int)py + 2);
            while (y > 2 && (World.Get(x, y - 1, z) is var b) && (b == Mat.Air || Mat.Passable[b] && b != Mat.Water)) y--;
            if (y < 2 || y < py - 12 || World.Get(x, y, z) != Mat.Air) continue;
            World.Set(x, y, z, Mat.Pitch);
            if (Ignite(x, y, z)) lit++;
        }
        if (lit > 0) Pitched++;
    }

    /// <summary>Fire pots that set the ground alight since the start.</summary>
    public int Pitched;
}
