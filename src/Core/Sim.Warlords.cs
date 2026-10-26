using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What became of a warlord in a fight: still to come, in the field, gone off it alive, fallen, taken alive.</summary>
public enum LordFate : byte { Coming, Field, Fled, Fell, Taken }

/// <summary>
/// A warlord of the horde's realm: one who leads an army — on the world map one day, as our commanders will lead ours,
/// and into a valley at the head of a raid. He is himself one of the horde, an orc or a troll, the harder to kill and
/// the harder hitting the higher his level; he carries his wounds from one fight to the next, and what his army does
/// in a valley (ours killed, our building broken) and getting away alive bring him on in level.
/// </summary>
public sealed class Warlord
{
    public int Id;
    /// <summary>His name, and as said of him («дружина Угрюма»).</summary>
    public string Name = "", Of = "";
    public UnitType Kind = Kinds.Orc;
    /// <summary>1 to 5.</summary>
    public int Level = 1;
    /// <summary>What he has done toward his next level.</summary>
    public float Xp;
    /// <summary>The share of his health he has, as his last fight left him.</summary>
    public float Health = 1;
    public LordFate Fate;
    /// <summary>A chapter's champion or the horde's chief at the head of his band: as hard to kill as a champion is, not as a warlord.</summary>
    public bool Champion;
    /// <summary>What his army did in the valley this time: ours killed, voxels of our building broken.</summary>
    public int Slew, Broke;

    /// <summary>What a warlord of a level has to do to come to the next.</summary>
    public static float Next(int level) => 60 + 40 * level;
    public const int MaxLevel = 5;
}

/// <summary>
/// The horde's warlords in the valley. One leads his own band at the head of his army (every band that came in with
/// him), with a guard of his own. Those of the horde within his voice (<see cref="LordReach"/>) bear more pain before
/// they break; when his own waver he roars his war cry, and those about him shrug off their pain, strike harder a while
/// and the broken among them who can still fight turn back to it; when ours close on him he brings his weapon down on
/// the ground and throws them off their feet, and what is built about him cracks. Badly hurt he goes off the field
/// with his guard, and his army wavers; killed or taken alive, his army breaks and runs. A chapter's champion and the
/// horde's chief lead as warlords too, but fight on to the end.
/// </summary>
public sealed partial class Sim
{
    /// <summary>How far a warlord's voice carries, voxels (24 m).</summary>
    public const float LordReach = 24 * VoxelWorld.PerMetre;
    /// <summary>How long his cry and his blow to the ground take to be ready again, seconds.</summary>
    public const float CryRest = 40, SlamRest = 16;
    /// <summary>How far his blow to the ground throws those of ours about him, voxels (6 m).</summary>
    const float SlamR = 6 * VoxelWorld.PerMetre;
    public const string SlamCause = "удар военачальника";
    /// <summary>The share of his health under which a warlord leaves the field.</summary>
    const float LordGives = .3f;

    static readonly (string name, string of)[] LordNames =
    {
        ("Угрюм", "Угрюма"), ("Кривоклык", "Кривоклыка"), ("Бурзаг", "Бурзага"), ("Гхаш", "Гхаша"), ("Мордрак", "Мордрака"),
        ("Скалозуб", "Скалозуба"), ("Ургаш", "Ургаша"), ("Ракх", "Ракха"), ("Зубодёр", "Зубодёра"), ("Ворг", "Ворга"),
    };
    int _nextLord = 1;
    double _slamSaid = -999;

    /// <summary>The warlords in the valley now, on their feet or not yet dead.</summary>
    public readonly List<Unit> LordsHere = new();
    /// <summary>A warlord roared his cry; brought his weapon down on the ground (for the view and the sound).</summary>
    public event Action<Unit> LordCried, LordSlammed;
    /// <summary>Cries and blows to the ground since the start (the bench counts them).</summary>
    public int LordCries, LordSlams;

    /// <summary>A new warlord of the horde's realm, of a level, an orc or a troll (an orc unless told).</summary>
    public Warlord NewWarlord(int level, UnitType kind = null)
    {
        int id = _nextLord++;
        var (name, of) = LordNames[(id * 7 + level * 3 + (int)(Rng.NextDouble() * 4)) % LordNames.Length];
        return new Warlord { Id = id, Name = name, Of = of, Kind = kind ?? Kinds.Orc, Level = Math.Clamp(level, 1, Warlord.MaxLevel) };
    }

    /// <summary>The guard a warlord comes with: two orcs, a troll among them from the third level.</summary>
    static IEnumerable<UnitType> LordGuard(Warlord lord) =>
        lord.Level >= 3 ? new[] { Kinds.Troll, Kinds.Orc, Kinds.Orc } : new[] { Kinds.Orc, Kinds.Orc };

    /// <summary>
    /// A warlord takes the lead of his band: as hard to kill again as his level makes him, with the wounds he came with
    /// (one who got away last time mended halfway since, as any who got away is).
    /// </summary>
    void Ennoble(Unit u, Warlord lord)
    {
        u.Lord = lord;
        u.Title = lord.Name;
        Rested(lord);
        lord.Fate = LordFate.Field;
        lord.Slew = lord.Broke = 0;
        u.CryReady = Time + 8;
        u.SlamReady = Time + 4;
        BattleLord(lord);
        if (!lord.Champion)
        {
            u.MaxHp *= 2 + .5f * lord.Level;
            u.Hp = u.MaxHp * Math.Clamp(lord.Health, .2f, 1);
        }
        if (!lord.Champion) Say($"Войско ведёт военачальник {lord.Name}, уровень {lord.Level}. Убейте его, и войско побежит.", 1, u);
    }

    /// <summary>A warlord who got away, setting out again: mended halfway since, as any who got away is.</summary>
    static void Rested(Warlord lord)
    {
        if (lord.Fate != LordFate.Fled) return;
        lord.Health += (1 - lord.Health) * .5f;
        lord.Fate = LordFate.Coming;
    }

    /// <summary>A warlord's blow, harder by his level (a champion's by his chapter).</summary>
    static float LordArm(Unit a) => a.Lord is { Champion: false } l ? 1.1f + .05f * l.Level : 1;

    /// <summary>The nearest warlord of the horde within his voice of one, on his feet and not running; null if none.</summary>
    Unit LordNear(Unit u)
    {
        Unit best = null; float bd = LordReach * LordReach;
        foreach (var l in LordsHere)
        {
            if (l.Dead || l.Routed || l.Bound || l.Inside) continue;
            float dx = l.X - u.X, dz = l.Z - u.Z, d = dx * dx + dz * dz;
            if (d <= bd) { bd = d; best = l; }
        }
        return best;
    }

    /// <summary>Every band of a warlord's army.</summary>
    IEnumerable<Warband> ArmyOf(Warlord lord)
    {
        foreach (var b in Bands) if (b.Lord == lord && b.State != BandState.Coming) yield return b;
    }

    /// <summary>The warlords in the valley counted afresh, each tick.</summary>
    void TickWarlords()
    {
        LordsHere.Clear();
        foreach (var u in Units) if (u.Lord != null && !u.Dead && u.Team == Team.Horde) LordsHere.Add(u);
    }

    /// <summary>A warlord, each tick: off the field if badly hurt; his cry when his own waver; his blow when ours close on him.</summary>
    void LordTick(Unit u)
    {
        var lord = u.Lord;
        if (u.Bound || Time < u.ChainedUntil) return;
        if (!lord.Champion && !u.Routed && u.Hp < u.MaxHp * LordGives)
        {
            // his own band covers him going
            u.Routed = true;
            if (u.Band != null) foreach (var m in u.Band.Members) if (!m.Dead && !m.Bound) m.Routed = true;
            foreach (var b in ArmyOf(lord)) Dismay(b, .3f);
            Say($"{lord.Name} тяжело ранен и уходит с поля. Его войско дрогнуло.", 1, u);
            return;
        }
        if (u.Routed || u.Down || Time < u.StaggerUntil) return;
        if (Time >= u.CryReady)
        {
            // those about him wavering, or a band of his low in spirit, or his own band going up to storm
            int wavering = 0;
            foreach (var o in Units)
            {
                if (o.Dead || o.Team != Team.Horde || o == u || o.Bound || o.Inside) continue;
                float dx = o.X - u.X, dz = o.Z - u.Z;
                if (dx * dx + dz * dz > LordReach * LordReach) continue;
                if (o.Pain > .35f || o.Routed && Armed(o)) wavering++;
            }
            bool low = false;
            foreach (var b in ArmyOf(lord)) if (b.Spirit < WaverSpirit) low = true;
            if (wavering >= 3 || low || u.Band is { State: BandState.Storm } && Time - u.Band.Since < 2) Cry(u);
        }
        if (Time >= u.SlamReady && !u.T.Ranged)
        {
            int close = 0;
            foreach (var o in Units)
            {
                if (o.Dead || o.Inside || o.Team != Team.Player || o.Airborne != null || Kinds.IsEngine(o.T)) continue;
                float dx = o.X - u.X, dz = o.Z - u.Z;
                if (dx * dx + dz * dz < 4 * 4 * VoxelWorld.PerMetre * VoxelWorld.PerMetre && MathF.Abs(o.Y - u.Y) < 16) close++;
            }
            if (close >= 2) Slam(u);
        }
    }

    /// <summary>
    /// His war cry: those of the horde within his voice shrug off their pain and strike harder a while, and those who
    /// broke and can still fight turn back to it; the bands of his army about him take heart.
    /// </summary>
    void Cry(Unit u)
    {
        u.CryReady = Time + CryRest;
        LordCries++;
        foreach (var o in Units)
        {
            if (o.Dead || o.Team != Team.Horde || o.Bound || o.Inside || o.Envoy || Kinds.IsEngine(o.T)) continue;
            float dx = o.X - u.X, dz = o.Z - u.Z;
            if (dx * dx + dz * dz > LordReach * LordReach) continue;
            o.Pain = 0;
            o.RallyUntil = Time + 12;
            if (o != u && o.Routed && Armed(o) && o.Hp > o.MaxHp * .3f && !Lame(o) && o.Band is not { Broken: true }) o.Routed = false;
        }
        foreach (var b in ArmyOf(u.Lord))
            if (b.Leader is { Dead: false } l && (l.X - u.X) * (l.X - u.X) + (l.Z - u.Z) * (l.Z - u.Z) < LordReach * LordReach * 4) Hearten(b, .3f);
        LordCried?.Invoke(u);
        Shouted?.Invoke(u, "В бой!");
        Say($"Военачальник {u.Lord.Name} кричит боевой клич: налётчики рядом с ним 12 секунд не чувствуют боли и бьют сильнее.", 2, u);
    }

    /// <summary>
    /// His blow to the ground: those of ours about him are hurt and thrown off their feet, the nearer the harder; what is
    /// built about him cracks.
    /// </summary>
    void Slam(Unit u)
    {
        u.SlamReady = Time + SlamRest;
        u.Swing = 1;
        LordSlams++;
        float fx = MathF.Cos(u.Facing), fz = MathF.Sin(u.Facing);
        foreach (var o in Units)
        {
            if (o.Dead || o.Inside || o.Team != Team.Player || o.Airborne != null || Kinds.IsEngine(o.T) || o.ChainedUntil > Time) continue;
            float dx = o.X - u.X, dz = o.Z - u.Z, d = MathF.Sqrt(dx * dx + dz * dz);
            if (d > SlamR || MathF.Abs(o.Y - u.Y) > 16) continue;
            float near = 1 - d / SlamR, light = MathF.Min(1, 15 / o.T.BodyH), ux = d > .5f ? dx / d : fx, uz = d > .5f ? dz / d : fz;
            Hurt(o, u, u.T.Damage * (.2f + .3f * near), SlamCause);
            Fling(o, ux * (3 + 5 * near) * VoxelWorld.PerMetre * light, (2.5f + 3 * near) * VoxelWorld.PerMetre * light, uz * (3 + 5 * near) * VoxelWorld.PerMetre * light, SlamCause);
        }
        Jar(u.X, u.Y + 2, u.Z, 2.5f * VoxelWorld.PerMetre, .3f + .06f * u.Lord.Level);
        LordSlammed?.Invoke(u);
        if (Time - _slamSaid > 120) { _slamSaid = Time; Say($"Военачальник {u.Lord.Name} бьёт оземь и раскидывает бойцов. Не подходите к нему толпой, стреляйте издали.", 2, u); }
    }

    /// <summary>A warlord fallen or taken: his army breaks and runs (the chief's fall, the whole horde: Sim.Chapters.cs).</summary>
    void LordLost(Unit u, LordFate fate)
    {
        var lord = u.Lord;
        if (lord.Fate is LordFate.Fell or LordFate.Taken) return;
        lord.Fate = fate;
        lord.Health = fate == LordFate.Fell ? 0 : u.Hp / u.MaxHp;
        foreach (var b in ArmyOf(lord)) { b.Spirit = 0; BreakBand(b); }
        BattleLord(lord);
        if (lord.Champion) return;
        Say(fate == LordFate.Fell ? $"Военачальник {lord.Name} пал. Его войско бежит." : $"Военачальник {lord.Name} взят в плен. Его войско бежит.", 1, u);
    }

    /// <summary>A warlord gone out of the valley alive: his wounds go with him, and what his army did brings him on.</summary>
    void LordAway(Unit u)
    {
        var lord = u.Lord;
        lord.Fate = LordFate.Fled;
        lord.Health = u.Hp / u.MaxHp;
        BattleLord(lord);
        if (lord.Champion) return;
        int was = lord.Level;
        lord.Xp += 20 + 10 * lord.Slew + lord.Broke / 64f;
        while (lord.Level < Warlord.MaxLevel && lord.Xp >= Warlord.Next(lord.Level)) { lord.Xp -= Warlord.Next(lord.Level); lord.Level++; }
        Say(lord.Level > was ? $"Военачальник {lord.Name} ушёл живым и стал опытнее: уровень {lord.Level}." : $"Военачальник {lord.Name} ушёл живым.", 1, u);
    }
}
