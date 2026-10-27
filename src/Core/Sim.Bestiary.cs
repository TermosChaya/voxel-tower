using System;

namespace VoxelTower.Core;

/// <summary>
/// What the creatures of the bestiary do in a valley besides biting and striking (Bestiary.cs): a pack comes all at
/// once; a spider throws webs that hold, a basilisk's look stiffens limbs, a golem hurls boulders, a dragon breathes fire
/// over the field, a cultist throws burning pitch; a blow of theirs leaves venom, throws men off their feet, drinks the
/// life it takes or sets them alight; a troll's and a treant's wounds close, but not under fire. They are a third
/// side: they go for ours and the horde's alike, and the horde turns on them as ours do.
/// </summary>
public sealed partial class Sim
{
    const float BM = VoxelWorld.PerMetre;

    /// <summary>Fire does not hurt it: a fire spirit, a dragon.</summary>
    public static bool FireProof(UnitType t) => t == Kinds.FireSpirit || t == Kinds.Dragon;

    /// <summary>Fire is its death: a treant burns three times as fast.</summary>
    static float FireHarm(UnitType t) => t == Kinds.Treant ? 3 : 1;

    double _webSaid = -999, _gazeSaid = -999, _breathSaid = -999, _knockSaid = -999;

    /// <summary>A dragon breathes fire: from its mouth (x, y, z) along (dx, dz), as far as its reach.</summary>
    public event Action<float, float, float, float, float, float> Breathed;

    /// <summary>
    /// A creature of the bestiary, before its chase: at a throw, it stands to it; set on someone, it rallies its pack,
    /// uses its power when it is ready (a breath, a web, a look, a boulder, a charge), and one that shoots or throws does
    /// so from where it stands. True while it is busy so.
    /// </summary>
    bool BeastTick(Unit u, float dt)
    {
        var b = Bestiary.Of(u.T);
        if (b == null) return false;
        if ((b.Powers & BeastPower.Boulders) != 0 && Hurling(u)) { HurlTick(u); return true; }
        if (u.Target is not { Dead: false, Inside: false } foe) return false;
        if ((b.Powers & BeastPower.Pack) != 0) Rally(u, foe);
        float d = Dist(u, foe);
        bool sees = d < (u.T.Range > 0 ? u.T.Range : 30 * BM) && Sight(u, foe);
        if (Time >= u.PowerAt && sees)
        {
            if ((b.Powers & BeastPower.Breath) != 0 && d < u.T.Range) { Breathe(u, foe); u.PowerAt = Time + 6 + Rng.NextDouble() * 2; u.Cooldown = 1.2f; return true; }
            if ((b.Powers & BeastPower.Web) != 0 && d > ReachOf(u) + foe.T.BodyR && d < u.T.Range) { Web(u, foe); u.PowerAt = Time + 7 + Rng.NextDouble() * 3; return true; }
            if ((b.Powers & BeastPower.Gaze) != 0 && d < u.T.Range) { Gaze(u, foe); u.PowerAt = Time + 9 + Rng.NextDouble() * 3; }
            if ((b.Powers & BeastPower.Boulders) != 0 && d > HurlNear && d < u.T.Range)
            {
                u.Facing = MathF.Atan2(foe.Z - u.Z, foe.X - u.X);
                var stone = HeftBoulder(u, HurledR, HurledCause);
                u.PowerAt = Time + (stone == null ? 3 : 8 + Rng.NextDouble() * 3);
                if (stone != null) { BeginHurl(u, stone, foe.X, foe.Y + HurledR, foe.Z, foe); return true; }
            }
            if ((b.Powers & BeastPower.Charge) != 0 && d > 6 * BM && d < 20 * BM) { u.ChargeUntil = Time + 3; u.PowerAt = Time + 12; }
        }
        // the ones that shoot or throw: from where they stand, out of the reach of a blow
        if (u.T.Ranged && sees && d <= u.T.Range && d > ReachOf(u) + foe.T.BodyR)
        {
            u.State = UnitState.Fight;
            u.Path = null;
            u.Facing = MathF.Atan2(foe.Z - u.Z, foe.X - u.X);
            if (u.Cooldown <= 0)
            {
                if ((b.Powers & BeastPower.Firebolt) != 0) Firebolt(u, foe); else Shoot(u, foe);
                u.Cooldown = u.T.AttackTime * (.9f + (float)Rng.NextDouble() * .2f);
            }
            return true;
        }
        return false;
    }

    /// <summary>One of a pack set upon someone: the rest of its kind within 25 m that are not at someone yet come too.</summary>
    void Rally(Unit u, Unit foe)
    {
        foreach (var o in Units)
        {
            if (o == u || o.Dead || o.T != u.T || o.Target is { Dead: false }) continue;
            if (Dist(u, o) > 25 * BM) continue;
            o.Target = foe; o.LastHurt = Time;
        }
    }

    /// <summary>
    /// A dragon's breath: a cone of fire 18 m long before it — whoever is in it, ours or the horde's, burns and is hurt,
    /// what will burn on the ground and in the timber in it is set alight, the bare turf seared black.
    /// </summary>
    void Breathe(Unit u, Unit foe)
    {
        float fx = foe.X - u.X, fz = foe.Z - u.Z, len = MathF.Max(.01f, MathF.Sqrt(fx * fx + fz * fz));
        fx /= len; fz /= len;
        u.Facing = MathF.Atan2(fz, fx);
        u.State = UnitState.Fight; u.Path = null; u.Swing = 1;
        float range = u.T.Range;
        Breathed?.Invoke(u.X + fx * u.T.BodyR * 2.2f, u.Y + u.T.BodyH * .62f, u.Z + fz * u.T.BodyR * 2.2f, fx, fz, range);
        int burnt = 0;
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o == u || FireProof(o.T)) continue;
            float dx = o.X - u.X, dz = o.Z - u.Z, d = MathF.Sqrt(dx * dx + dz * dz);
            if (d > range || d < 1) continue;
            // in the cone: within thirty degrees either side of the way it breathes
            if ((dx * fx + dz * fz) / d < .85f) continue;
            o.Burn = Math.Max(o.Burn, 4); o.BurnHeat = Math.Max(o.BurnHeat, 1.6f);
            Hurt(o, u, u.T.Damage * .5f * (1 - d / range * .5f), "пламя дракона");
            burnt++;
        }
        // the ground under the cone catches where it will
        for (float s = 3 * BM; s < range; s += 2 * BM)
            for (int k = -2; k <= 2; k++)
            {
                float w = s * .45f * k / 2;
                int x = (int)(u.X + fx * s - fz * w), z = (int)(u.Z + fz * s + fx * w);
                if (!World.InBounds(x, 1, z)) continue;
                int top = World.SurfAt(x, z);
                // what burns catches; the turf with nothing on it to burn is seared black
                if (Ignite(x, top, z) || Ignite(x, top - 1, z)) continue;
                if (Mat.IsGrass(World.Get(x, top - 1, z))) World.Set(x, top - 1, z, Mat.Scorch);
            }
        if (Time - _breathSaid > 120) { _breathSaid = Time; Say("Дракон дышит огнём: всё в конусе перед ним горит. Расходитесь, не стойте толпой перед его мордой.", 3, u); }
    }

    /// <summary>
    /// A spider's web over whoever it is thrown at: held, it goes at a third of its pace for five seconds, and the web
    /// lies on the ground about it (it burns at a spark).
    /// </summary>
    void Web(Unit u, Unit foe)
    {
        u.Facing = MathF.Atan2(foe.Z - u.Z, foe.X - u.X);
        u.Swing = 1;
        foe.SlowUntil = Time + 5; foe.SlowBy = .33f;
        int fx = (int)foe.X, fz = (int)foe.Z, top = World.SurfAt(fx, fz);
        for (int dz = -3; dz <= 3; dz++)
            for (int dx = -3; dx <= 3; dx++)
            {
                if ((dx * dx + dz * dz) > 9 || ((dx ^ dz) & 1) != 0) continue;
                int x = fx + dx, z = fz + dz, y = World.SurfAt(x, z);
                if (Math.Abs(y - top) > 3 || !World.InBounds(x, y, z) || World.Get(x, y, z) != Mat.Air) continue;
                World.Set(x, y, z, Mat.Web);
            }
        if (Time - _webSaid > 90) { _webSaid = Time; Say("Паук опутал паутиной: попавший в неё еле идёт. Паутина горит от искры.", 2, foe); }
    }

    /// <summary>A basilisk's look: whoever it catches stiffens, at a seventh of its pace and a blow at half its speed, for six seconds.</summary>
    void Gaze(Unit u, Unit foe)
    {
        foe.SlowUntil = Time + 6; foe.SlowBy = .15f;
        foe.Cooldown = Math.Max(foe.Cooldown, 1.5f);
        if (Time - _gazeSaid > 90) { _gazeSaid = Time; Say("Взгляд василиска сковывает: кого он увидел, тот каменеет на несколько секунд.", 2, foe); }
    }

    /// <summary>A cultist's ball of burning pitch, thrown at whoever it is set on: it breaks and burns where it comes down.</summary>
    void Firebolt(Unit u, Unit t)
    {
        float d = Dist(u, t);
        u.Swing = 1;
        Arrows.Add(new Arrow
        {
            X0 = u.X, Y0 = u.Y + u.T.BodyH * .85f, Z0 = u.Z,
            X1 = t.X + (float)(Rng.NextDouble() - .5) * 8, Y1 = t.Y + t.T.BodyH * .5f, Z1 = t.Z + (float)(Rng.NextDouble() - .5) * 8,
            Dur = Math.Max(.1f, d / (14 * BM)), Target = t, From = u, Hits = Rng.NextDouble() < .7, Damage = u.T.Damage, Kind = Shot.FirePot, Arc = .3f,
        });
    }

    /// <summary>
    /// A blow of a creature of the wild landed (Strike): its venom left in the wound, a great one's blow or a charge
    /// throwing the struck off its feet, a wraith drinking what it took, a fire spirit setting the struck alight.
    /// </summary>
    void BeastBlow(Unit a, Unit b, float dmg)
    {
        var br = Bestiary.Of(a.T);
        if (br == null || b.Dead && b.DiedAt >= 0 && b.DiedAt < Time) return;
        if ((br.Powers & BeastPower.Venom) != 0) { b.VenomUntil = Time + 5; b.VenomHarm = Math.Max(b.VenomHarm, a.T.Damage * .12f); b.VenomBy = a; }
        if ((br.Powers & BeastPower.Drain) != 0) a.Hp = Math.Min(a.MaxHp, a.Hp + dmg * .5f);
        if ((br.Powers & BeastPower.Ablaze) != 0 && !FireProof(b.T)) { b.Burn = Math.Max(b.Burn, 3); b.BurnHeat = Math.Max(b.BurnHeat, 1.3f); }
        bool charging = Time < a.ChargeUntil;
        if (charging) a.ChargeUntil = 0;
        if (((br.Powers & BeastPower.Knock) != 0 || charging) && !b.Inside && !Kinds.IsEngine(b.T) && b.T.BodyH < a.T.BodyH)
        {
            float light = MathF.Min(1, 15 / b.T.BodyH), dx = b.X - a.X, dz = b.Z - a.Z, d = MathF.Sqrt(dx * dx + dz * dz);
            if (d < .5f) { dx = MathF.Cos(a.Facing); dz = MathF.Sin(a.Facing); d = 1; }
            float out_ = (charging ? 8 : 6) * BM, up = (charging ? 4.5f : 3.5f) * BM;
            Fling(b, dx / d * out_ * light, up * light, dz / d * out_ * light, charging ? $"натиск: {a.T.Name.ToLowerInvariant()}" : $"удар: {a.T.Name.ToLowerInvariant()}");
            if (Time - _knockSaid > 120) { _knockSaid = Time; Say($"{a.T.Name} сбивает с ног. Держитесь от него дальше, бейте издали.", 2, a); }
        }
    }

    /// <summary>A charge's first blow strikes twice as hard (BeastBlow throws whoever it struck).</summary>
    float BeastArm(Unit a) => a.Team == Team.Wild && Time < a.ChargeUntil ? 2 : 1;

    /// <summary>
    /// What lasts on the bodies a second at a time: venom eating on in a wound; a troll's and a treant's wounds closing,
    /// unless fire was on it in the last ten seconds.
    /// </summary>
    void TickBeasts(float dt)
    {
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside) continue;
            if (u.VenomUntil > Time) Hurt(u, u.VenomBy is { Dead: false } by ? by : null, u.VenomHarm * dt, "яд");
            else u.VenomHarm = 0;
            if (u.T.Regen > 0 && u.Hp < u.MaxHp && u.Burn <= 0 && Time - u.ScorchedAt > 10) u.Hp = Math.Min(u.MaxHp, u.Hp + u.T.Regen * dt);
        }
    }

    /// <summary>
    /// A raider set upon by a creature of the wild turns on it as ours do (Beset): the one that struck it a moment ago,
    /// if it is near enough to strike back.
    /// </summary>
    Unit WildFoe(Unit u)
    {
        if (u.HurtBy is not { Dead: false, Team: Team.Wild } by || by.T.Aggro <= 0 || Time - u.LastHurt > 4) return null;
        return Dist(u, by) < ReachOf(u) + by.T.BodyR + 12 ? by : null;
    }
}
