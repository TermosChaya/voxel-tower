using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

public enum SpellKind : byte { Hand, Mire, Fall, FireRain, Thunder, BloodMoon, Dread, Chains, Raise, Eye }

/// <summary>A spell: what it does, its circle, what learning it takes, what a casting costs, and how far it reaches.</summary>
public sealed class Spell
{
    public SpellKind Kind;
    public string Name = "", About = "";
    public int Circle;
    /// <summary>Learnt for intervention (the first of each circle) or for revelations, and how much.</summary>
    public bool ByFavour;
    public int Learn;
    /// <summary>Intervention a casting takes, and seconds before it can be cast again.</summary>
    public double Cost, Cooldown;
    /// <summary>How far it reaches on the field, in voxels: the circle it fills; 0 for the whole valley.</summary>
    public float Radius;
    /// <summary>Drawn as a stroke, from where the button goes down to where it comes up.</summary>
    public bool Stroke;
}

/// <summary>A patch of ground made a bog by a spell, and till when.</summary>
public sealed class Mire { public float X, Z, R; public double Until; }

/// <summary>A gobbet of burning pitch falling out of the sky: where it comes down, and when.</summary>
public sealed class Drop { public float X, Y, Z; public double At; }

/// <summary>
/// Spells: the player's own hand in the fight, paid for in intervention. Three circles — earth, fire and the sky, death —
/// the first spell of each learnt for intervention, the rest for revelations. Each costs intervention to cast and wants
/// a while before it can be cast again.
/// </summary>
public sealed partial class Sim
{
    public static readonly string[] Circles = { "Круг земли", "Круг огня и неба", "Круг смерти" };

    public static readonly Spell[] Spells =
    {
        new() { Kind = SpellKind.Hand, Circle = 0, ByFavour = true, Learn = 20, Name = "Длань земли", Cost = 20, Cooldown = 45, Stroke = true,
            About = "Вал из грунта вдоль проведённой линии: 2 м шириной, 2,5 м высотой, до 16 м длиной. Встаёт за три секунды; кто стоял на его месте, оказывается рядом." },
        new() { Kind = SpellKind.Mire, Circle = 0, Learn = 2, Name = "Трясина", Cost = 25, Cooldown = 40, Radius = 6 * VoxelWorld.PerMetre,
            About = "Круг в 6 м на минуту становится топью: всякий, кто в ней, — и орда, и наши — еле переставляет ноги." },
        new() { Kind = SpellKind.Fall, Circle = 0, Learn = 2, Name = "Обвал", Cost = 30, Cooldown = 45, Radius = 2.5f * VoxelWorld.PerMetre,
            About = "Выбивает шар в 2,5 м из того, на что наведено, — стены, скалы, осадной башни: что держалось на нём, рушится и давит тех, кто внизу." },
        new() { Kind = SpellKind.FireRain, Circle = 1, ByFavour = true, Learn = 25, Name = "Огненный дождь", Cost = 30, Cooldown = 50, Radius = 5 * VoxelWorld.PerMetre,
            About = "Горящая смола три секунды падает с неба на круг в 5 м: поджигает дерево, обжигает и поджигает налётчиков. В дождь — вдвое слабее." },
        new() { Kind = SpellKind.Thunder, Circle = 1, Learn = 2, Name = "Перст грома", Cost = 25, Cooldown = 25, Radius = 3 * VoxelWorld.PerMetre,
            About = "Молния в налётчика у курсора (или в землю): 150 урона — и машине тоже, поджигает; рядом стоящих бьёт слабее. В дождь — в полтора раза сильнее." },
        new() { Kind = SpellKind.BloodMoon, Circle = 1, Learn = 3, Name = "Кровавая луна", Cost = 40, Cooldown = 240,
            About = "Полторы минуты луна светит красным, и светло всюду: лучники бьют в полную силу и в темноте. Только ночью." },
        new() { Kind = SpellKind.Dread, Circle = 2, ByFavour = true, Learn = 25, Name = "Ужас", Cost = 30, Cooldown = 60, Radius = 7 * VoxelWorld.PerMetre,
            About = "Налётчики в круге в 7 м бросают всё и бегут на запад; вожаки и тролли поддаются через раз, машины — никогда." },
        new() { Kind = SpellKind.Chains, Circle = 2, Learn = 2, Name = "Цепи", Cost = 25, Cooldown = 40, Radius = 5 * VoxelWorld.PerMetre,
            About = "Налётчики в круге в 5 м скованы на 10 секунд: не идут, не рубят и не бьют. Скованного мечник с «Живьём» валит уже при трёх пятых сил." },
        new() { Kind = SpellKind.Raise, Circle = 2, Learn = 3, Name = "Поднять мёртвых", Cost = 45, Cooldown = 90, Radius = 8 * VoxelWorld.PerMetre,
            About = "До шести павших налётчиков в круге в 8 м (тела лежат полминуты) встают мертвецами и минуту бьются за вас, потом рассыпаются." },
        new() { Kind = SpellKind.Eye, Circle = 2, Learn = 2, Name = "Око", Cost = 20, Cooldown = 120,
            About = "Две минуты видна вся орда — метка над каждым налётчиком сквозь стены и лес, — и туман не мешает лучникам." },
    };

    public static Spell SpellOf(SpellKind k) => Spells[(int)k];

    /// <summary>The spells the household knows.</summary>
    public readonly HashSet<SpellKind> Known = new();
    readonly double[] _spellReady = new double[Spells.Length];

    /// <summary>Seconds before a spell can be cast again.</summary>
    public double ReadyIn(SpellKind k) => Math.Max(0, _spellReady[(int)k] - Time);

    public bool CanLearnSpell(Spell s) => !Known.Contains(s.Kind) && (s.ByFavour ? Favour >= s.Learn : Revelations >= s.Learn);

    /// <summary>Learns a spell for what it takes: intervention for the first of a circle, revelations for the rest.</summary>
    public bool LearnSpell(SpellKind k)
    {
        var s = SpellOf(k);
        if (!CanLearnSpell(s)) return false;
        if (s.ByFavour) Favour -= s.Learn; else Revelations -= s.Learn;
        Known.Add(k);
        Say($"Постигнуто заклинание «{s.Name}». {s.About}", 1);
        return true;
    }

    /// <summary>Bogs made by a spell; gobbets of burning pitch on their way down.</summary>
    public readonly List<Mire> Mires = new();
    public readonly List<Drop> Drops = new();
    /// <summary>Till when the moon shines red, and till when the whole horde is seen.</summary>
    public double BloodMoonUntil = -1, EyeUntil = -1;
    /// <summary>The earth rising under the Hand, lowest first; how much of it is up, and since when it rises.</summary>
    readonly List<(int x, int y, int z)> _rising = new();
    int _risenEarth;
    double _riseFrom;
    const double RiseFor = 3;

    /// <summary>A spell cast, where: for whatever shows it.</summary>
    public event Action<SpellKind, float, float, float> Spelled;
    /// <summary>A bolt of lightning, from the sky to where it struck.</summary>
    public event Action<float, float, float> Struck;

    /// <summary>
    /// Casts a spell at a spot (voxels: for most the ground there; for the Fall the voxel the cursor is on; for the Hand a
    /// stroke from the first spot to the second). Null when it is cast; otherwise why it cannot be.
    /// </summary>
    public string Cast(SpellKind k, float x, float y, float z, float x2 = 0, float z2 = 0)
    {
        var s = SpellOf(k);
        if (Over) return "осада окончена";
        if (!Known.Contains(k)) return "это заклинание ещё не постигнуто";
        if (Time < _spellReady[(int)k]) return $"ещё не готово: {_spellReady[(int)k] - Time:0} с";
        if (Favour < s.Cost) return $"мало вмешательства: нужно {s.Cost:0}";
        string why = k switch
        {
            SpellKind.Hand => RaiseBank(x, z, x2, z2),
            SpellKind.Mire => BogDown(x, z, s.Radius),
            SpellKind.Fall => BringDown((int)x, (int)y, (int)z, s.Radius),
            SpellKind.FireRain => RainFireOn(x, z, s.Radius),
            SpellKind.Thunder => Thunderbolt(x, y, z, s.Radius),
            SpellKind.BloodMoon => Waves.Darkness < .3f ? "кровавая луна восходит только ночью" : Moon(),
            SpellKind.Dread => Terrify(x, z, s.Radius),
            SpellKind.Chains => Chain(x, z, s.Radius),
            SpellKind.Raise => RaiseDead(x, z, s.Radius),
            _ => Watch(),
        };
        if (why != null) return why;
        Favour -= s.Cost;
        _spellReady[(int)k] = Time + s.Cooldown;
        Spelled?.Invoke(k, x, y, z);
        Say($"Заклинание: «{s.Name}».", 1, x, z);
        return null;
    }

    string Moon() { BloodMoonUntil = Time + 90; return null; }

    string Watch() { EyeUntil = Time + 120; return null; }

    /// <summary>Whether a spot lies in a bog of the spell.</summary>
    bool InMire(float x, float z)
    {
        foreach (var m in Mires) if ((x - m.X) * (x - m.X) + (z - m.Z) * (z - m.Z) < m.R * m.R) return true;
        return false;
    }

    // ------------------------------------------------------------ the circle of earth

    /// <summary>The Hand: a bank of earth along the stroke, rounded on top, lower at its ends, rising from the ground up.</summary>
    string RaiseBank(float x0, float z0, float x1, float z1)
    {
        float dx = x1 - x0, dz = z1 - z0, len = MathF.Sqrt(dx * dx + dz * dz);
        if (len < 2 * VoxelWorld.PerMetre) return "проведите линию подлиннее: от 2 м";
        float max = 16 * VoxelWorld.PerMetre;
        if (len > max) { x1 = x0 + dx / len * max; z1 = z0 + dz / len * max; len = max; }
        float ux = dx / len, uz = dz / len;
        const float Half = VoxelWorld.PerMetre, High = 20;
        var cols = new Dictionary<(int x, int z), int>();
        for (float t = 0; t <= len; t += .5f)
            for (float c = -Half; c <= Half; c += .5f)
            {
                int x = (int)MathF.Floor(x0 + ux * t - uz * c), z = (int)MathF.Floor(z0 + uz * t + ux * c);
                if (x < 1 || z < 1 || x >= World.SX - 1 || z >= World.SZ - 1 || Site.Forbidden(x, z)) continue;
                float end = MathF.Min(1, (len / 2 - MathF.Abs(t - len / 2)) / 8 + .45f);
                int h = (int)(High * MathF.Sqrt(Math.Max(0, 1 - c / Half * (c / Half))) * end);
                if (h <= 0) continue;
                cols[(x, z)] = Math.Max(cols.GetValueOrDefault((x, z)), h);
            }
        // what is still rising of the last one goes up at once
        RiseNow(_rising.Count);
        _rising.Clear(); _risenEarth = 0;
        foreach (var ((x, z), h) in cols)
        {
            int g = World.SurfAt(x, z);
            for (int y = g; y < g + h && y < World.SY - 1; y++) if (Brushes.Free(World.Get(x, y, z))) _rising.Add((x, y, z));
        }
        if (_rising.Count == 0) return "здесь валу не подняться";
        _rising.Sort((a, b) => a.y.CompareTo(b.y));
        _riseFrom = Time;
        return null;
    }

    /// <summary>Lays the rising earth up to the n-th voxel.</summary>
    void RiseNow(int n)
    {
        for (; _risenEarth < Math.Min(n, _rising.Count); _risenEarth++)
        {
            var (x, y, z) = _rising[_risenEarth];
            if (Brushes.Free(World.Get(x, y, z))) World.Set(x, y, z, Mat.Soil);
        }
    }

    string BogDown(float x, float z, float r)
    {
        Mires.Add(new Mire { X = x, Z = z, R = r, Until = Time + 60 });
        return null;
    }

    /// <summary>The Fall: a ball knocked out of what is there; it comes down in pieces, and what it held up with it.</summary>
    string BringDown(int x, int y, int z, float r)
    {
        var ball = new HashSet<int>();
        int ri = (int)MathF.Ceiling(r);
        for (int dy = -ri; dy <= ri; dy++)
            for (int dz = -ri; dz <= ri; dz++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    if (dx * dx + dy * dy + dz * dz > r * r) continue;
                    int vx = x + dx, vy = y + dy, vz = z + dz;
                    if (!World.InBounds(vx, vy, vz) || vy < 4) continue;
                    byte m = World.Get(vx, vy, vz);
                    // the citadel, the black stone and the bedrock do not give; water and the air are not there to fall
                    if (m == Mat.Air || m == Mat.Water || Mat.Def[m].Group == MatGroup.Hard || Mat.Passable[m] && !Mat.IsLadder[m]) continue;
                    ball.Add(WaterPlanner.Pack(vx, vy, vz));
                }
        if (ball.Count == 0) return "здесь нечему рушиться: наведите на стену, скалу или постройку";
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside) continue;
            float d = MathF.Sqrt((u.X - x) * (u.X - x) + (u.Y - y) * (u.Y - y) * .5f + (u.Z - z) * (u.Z - z));
            if (Kinds.IsEngine(u.T) && d < r + 3 * VoxelWorld.PerMetre) Hurt(u, null, 280, "обвал");
            else if (d < r + 1.5f * VoxelWorld.PerMetre) Hurt(u, null, 45, "обвал");
        }
        Fall(ball, quiet: false);
        return null;
    }

    // ------------------------------------------------------------ the circle of fire and the sky

    string RainFireOn(float x, float z, float r)
    {
        // the pitch finds the raiders under it, a gobbet for each (a dozen at most); the rest falls where it falls
        var under = HordeIn(x, z, r);
        for (int i = 0; i < 24; i++)
        {
            float dx, dz;
            if (i < Math.Min(12, under.Count)) { dx = under[i].X - x + (float)(Rng.NextDouble() - .5) * 6; dz = under[i].Z - z + (float)(Rng.NextDouble() - .5) * 6; }
            else
            {
                double a = Rng.NextDouble() * Math.PI * 2, d = Math.Sqrt(Rng.NextDouble()) * r;
                dx = (float)(Math.Cos(a) * d); dz = (float)(Math.Sin(a) * d);
            }
            int px = Math.Clamp((int)(x + dx), 0, World.SX - 1), pz = Math.Clamp((int)(z + dz), 0, World.SZ - 1);
            Drops.Add(new Drop { X = x + dx, Y = World.SurfAt(px, pz), Z = z + dz, At = Time + .4 + (i * 7 % 24) * 3.0 / 24 });
        }
        return null;
    }

    /// <summary>A gobbet of burning pitch comes down and splashes: what burns within a metre of it catches, whoever it falls on burns.</summary>
    void Land(Drop d)
    {
        float k = Weather == Weather.Rain ? .5f : 1;
        int x = (int)d.X, z = (int)d.Z, top = World.SurfAt(Math.Clamp(x, 0, World.SX - 1), Math.Clamp(z, 0, World.SZ - 1));
        const int Splatter = VoxelWorld.PerMetre;
        int lit = 0;
        for (int dz = -Splatter; dz <= Splatter && lit < 3; dz += 2)
            for (int dx = -Splatter; dx <= Splatter && lit < 3; dx += 2)
            {
                if (dx * dx + dz * dz > Splatter * Splatter) continue;
                int sx = x + dx, sz = z + dz;
                if (sx < 0 || sz < 0 || sx >= World.SX || sz >= World.SZ) continue;
                // the top of whatever stands there, and down it a little
                int st = World.SurfAt(sx, sz);
                for (int y = st - 1; y >= st - 3 && y >= top - 16; y--)
                    if (Mat.Burns(World.Get(sx, y, sz)) && (k >= 1 || Rng.NextDouble() < k) && Ignite(sx, y, sz)) { lit++; break; }
            }
        const float Splash = 1.5f * VoxelWorld.PerMetre;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || MathF.Abs(u.X - d.X) > Splash || MathF.Abs(u.Z - d.Z) > Splash || MathF.Abs(u.Y - top) > 12) continue;
            Hurt(u, null, 12 * k, "огненный дождь");
            if (!u.Dead) { u.Burn = Math.Max(u.Burn, 4 * k); u.BurnHeat = 1; }
        }
    }

    /// <summary>A bolt out of the sky at the raider nearest the spot, or at the ground there.</summary>
    string Thunderbolt(float x, float y, float z, float r)
    {
        Unit t = null; float bd = r;
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Bound || u.Team != Team.Horde) continue;
            float d = MathF.Sqrt((u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z));
            if (d < bd) { bd = d; t = u; }
        }
        float k = Weather == Weather.Rain ? 1.5f : 1;
        if (t != null) { (x, y, z) = (t.X, t.Y, t.Z); }
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u == t) continue;
            if (MathF.Abs(u.X - x) < 10 && MathF.Abs(u.Z - z) < 10 && MathF.Abs(u.Y - y) < 12) Hurt(u, null, 30 * k, "молния");
        }
        if (t != null)
        {
            Hurt(t, null, 150 * k, "молния");
            if (!t.Dead) { t.Burn = Math.Max(t.Burn, 5); t.BurnHeat = 1; }
        }
        int ix = (int)x, iz = (int)z, top = World.SurfAt(Math.Clamp(ix, 0, World.SX - 1), Math.Clamp(iz, 0, World.SZ - 1));
        if (Mat.Burns(World.Get(ix, top - 1, iz))) Ignite(ix, top - 1, iz);
        Struck?.Invoke(x, t != null ? y : top, z);
        return null;
    }

    // ------------------------------------------------------------ the circle of death

    /// <summary>Raiders in the circle (not engines, not the bound), each of them.</summary>
    List<Unit> HordeIn(float x, float z, float r)
    {
        var list = new List<Unit>();
        foreach (var u in Units)
        {
            if (u.Dead || u.Inside || u.Bound || u.Envoy || u.Team != Team.Horde || Kinds.IsEngine(u.T)) continue;
            if ((u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z) < r * r) list.Add(u);
        }
        return list;
    }

    string Terrify(float x, float z, float r)
    {
        var them = HordeIn(x, z, r);
        if (them.Count == 0) return "в круге нет налётчиков";
        int fled = 0;
        foreach (var u in them)
        {
            // a leader and a troll are made of harder stuff
            if ((u.Leader || u.T == Kinds.Troll) && Rng.NextDouble() < .5) continue;
            u.Routed = true; u.Raid = null; u.Path = null; u.Target = null;
            StopCarve(u); StopBuild(u);
            fled++;
        }
        Say(fled == 0 ? "Ужас не взял никого." : $"Ужас: бегут {fled} из {them.Count}.", fled > 0 ? 1 : 2, x, z);
        return null;
    }

    string Chain(float x, float z, float r)
    {
        var them = HordeIn(x, z, r);
        if (them.Count == 0) return "в круге нет налётчиков";
        foreach (var u in them) { u.ChainedUntil = Time + 10; u.Path = null; StopCarve(u); StopBuild(u); }
        return null;
    }

    /// <summary>The fallen of the horde in the circle stand up, ours for a minute.</summary>
    string RaiseDead(float x, float z, float r)
    {
        var fallen = new List<Unit>();
        foreach (var u in Units)
        {
            if (!u.Dead || u.Team != Team.Horde || Kinds.IsEngine(u.T) || u.DiedAt < 0 || Time - u.DiedAt > 30 || u.KilledBy == GivenToPowers) continue;
            if ((u.X - x) * (u.X - x) + (u.Z - z) * (u.Z - z) < r * r) fallen.Add(u);
        }
        if (fallen.Count == 0) return "здесь нет павших: тела лежат полминуты";
        fallen.Sort((a, b) => ((a.X - x) * (a.X - x) + (a.Z - z) * (a.Z - z)).CompareTo((b.X - x) * (b.X - x) + (b.Z - z) * (b.Z - z)));
        int n = 0;
        foreach (var c in fallen)
        {
            if (n >= 6) break;
            int cell = NearestStand((int)c.X, (int)c.Z, Kinds.Risen.Move.Height);
            if (cell < 0) continue;
            var r2 = Spawn(Kinds.Risen, cell);
            r2.CrumbleAt = Time + 60;
            r2.Facing = c.Facing;
            // the body it rose from is gone from where it lay
            c.DiedAt = -999;
            n++;
        }
        return n == 0 ? "павшим не встать: им негде стоять" : null;
    }

    /// <summary>One of the risen: at the nearest raider, and down it; after its minute, it falls apart.</summary>
    void RisenTick(Unit u, float dt)
    {
        if (Time >= u.CrumbleAt) { Kill(u, null, "рассыпался"); return; }
        const float Seek = 30 * VoxelWorld.PerMetre;
        if (u.Target == null || u.Target.Dead || u.Target.Bound || Time >= u.NextSearch)
        {
            u.NextSearch = Time + .7;
            u.Target = null; float bd = Seek;
            foreach (var o in Units)
            {
                if (o.Dead || o.Inside || o.Bound || o.Envoy || o.Team != Team.Horde) continue;
                float d = Dist(u, o);
                if (d < bd) { bd = d; u.Target = o; }
            }
            if (u.Target != null && !InReach(u, u.Target) && !GoTo(u, u.Target.Cell, adjacent: true, budget: 6000)) u.Path = null;
        }
        var t = u.Target;
        if (t == null) { u.State = UnitState.Idle; u.Path = null; return; }
        if (InReach(u, t))
        {
            u.State = UnitState.Fight; u.Path = null;
            u.Facing = MathF.Atan2(t.Z - u.Z, t.X - u.X);
            if (u.Cooldown <= 0) { Strike(u, t); u.Cooldown = u.T.AttackTime * (.9f + (float)Rng.NextDouble() * .2f); }
            return;
        }
        if (u.Path != null && u.PathI < u.Path.Count) { u.State = UnitState.Walk; Walk(u, dt); }
        else u.State = UnitState.Idle;
    }

    /// <summary>What the spells keep doing: the earth rising, the pitch coming down, the bogs drying up.</summary>
    void TickSpells()
    {
        if (_risenEarth < _rising.Count) RiseNow((int)(_rising.Count * Math.Clamp((Time - _riseFrom) / RiseFor, 0, 1)) + 1);
        for (int i = Drops.Count - 1; i >= 0; i--)
            if (Drops[i].At <= Time) { Land(Drops[i]); Drops.RemoveAt(i); }
        if (Mires.Count > 0) Mires.RemoveAll(m => m.Until <= Time);
    }
}
