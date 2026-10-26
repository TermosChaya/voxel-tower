using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>
/// The commanders of ours as heroes, in the valley (the rules are Heroes.cs). Each new commander is a hero of level 1
/// with a name of his own. He gains experience by what the horde loses about him — every raider struck down within his
/// word's reach, twice for one by his own hand, a warlord's fall most of all — by a raid beaten off that he stood his
/// people against, and by a valley taken; each level he rises the player chooses him a skill. His skills bite where his
/// word reaches: the line takes less, the bows reach further and loose faster, the hurt mend faster, the workers work
/// faster, a volley strikes harder, those he rallies strike harder, a band lying in wait is seen before it rises. Fallen,
/// he is lost with his skills, and those he led strike weaker a minute for grief.
/// </summary>
public sealed partial class Sim
{
    /// <summary>A hero rose a level (the interface offers the choice of a skill).</summary>
    public event Action<Unit> HeroRose;
    /// <summary>Bands lying in wait a hero saw before they rose; the experience heroes had of raids beaten off (for the bench).</summary>
    public int AmbushesSpotted, HeroHeldXp;

    // the heroes of the valley, gathered once a step (they are read at every blow, shot and stroke of work)
    readonly List<Unit> _heroes = new();
    double _heroesAt = double.NaN;
    // the heroes who stood their people against the raid going on
    readonly HashSet<int> _heroFought = new();
    int _heroSalt = int.MinValue;

    /// <summary>The living commanders of the valley, each with his hero.</summary>
    public List<Unit> HeroesHere()
    {
        if (_heroesAt == Time) return _heroes;
        _heroesAt = Time;
        _heroes.Clear();
        foreach (var u in Units) if (!u.Dead && u.Hero != null && u.T == Kinds.Captain) _heroes.Add(u);
        return _heroes;
    }

    /// <summary>A new commander's hero: his name drawn by the valley and his number, none of the valley's heroes' names.</summary>
    internal Hero NewHero(Unit u)
    {
        if (_heroSalt == int.MinValue)
            _heroSalt = World.SurfAt(Math.Clamp(Site.KeepX + 37, 0, World.SX - 1), Math.Clamp(Site.KeepZ + 11, 0, World.SZ - 1)) * 7919
                + World.SurfAt(Math.Clamp(Site.KeepX - 53, 0, World.SX - 1), Math.Clamp(Site.KeepZ + 29, 0, World.SZ - 1)) * 131 + Site.KeepX;
        var taken = new HashSet<string>();
        foreach (var o in Units) if (!o.Dead && o.Hero != null) taken.Add(o.Hero.Name);
        // the valley's heroes gathered anew with him among them
        _heroesAt = double.NaN;
        return Heroes.Fresh(_heroSalt * 31 + u.Id * 977, taken);
    }

    /// <summary>The best grade of a skill among the heroes on their feet whose word reaches a spot (each by his own reach).</summary>
    int GradeAt(Skill s, float x, float y, float z, bool inside = false)
    {
        var heroes = HeroesHere();
        if (heroes.Count == 0) return 0;
        int best = 0;
        foreach (var h in heroes)
        {
            int g = h.Hero[s];
            if (g <= best || h.Inside && !inside) continue;
            float reach = CaptainReach * WordFar(h), dx = h.X - x, dz = h.Z - z;
            if (dx * dx + dz * dz <= reach * reach && MathF.Abs(h.Y - y) < 12 * VoxelWorld.PerMetre) best = g;
        }
        return best;
    }

    /// <summary>How far a commander's word and calls carry, as a share of the usual: further with Слово.</summary>
    public static float WordFar(Unit cap) => cap?.Hero == null ? 1 : 1 + Heroes.WordReach * cap.Hero[Skill.Word];

    /// <summary>Of a blow at one of ours in a hero's reach: less with Строй.</summary>
    float Steadied(Unit b) => b.Team != Team.Player || Kinds.IsEngine(b.T) ? 1 : 1 - Heroes.LineGuard * GradeAt(Skill.Line, b.X, b.Y, b.Z);

    /// <summary>How much further a shooter of ours in a hero's reach carries, with Стрелки.</summary>
    float HeroBow(Unit a) => a.Team != Team.Player || !a.T.Ranged ? 1 : 1 + Heroes.BowReach * GradeAt(Skill.Archers, a.X, a.Y, a.Z);

    /// <summary>How much sooner a shooter of ours in a hero's reach looses again, with Стрелки.</summary>
    float HeroPace(Unit a) => a.Team != Team.Player || !a.T.Ranged ? 1 : 1 - Heroes.BowPace * GradeAt(Skill.Archers, a.X, a.Y, a.Z);

    /// <summary>
    /// The heartening of one of ours a commander rallied, beyond the usual: more with Слово of a hero about it, and with
    /// Тактика of the hero it follows by «За мной!».
    /// </summary>
    float Spoken(Unit a)
    {
        if (a.Team != Team.Player) return 0;
        float more = Heroes.WordArm * GradeAt(Skill.Word, a.X, a.Y, a.Z);
        if (a.Follows?.Hero is { } h && Time < a.FollowUntil) more += Heroes.ChargeArm * h[Skill.Tactics];
        return more;
    }

    /// <summary>One whose commander fell strikes weaker a while.</summary>
    float Grief(Unit a) => a.GriefUntil > Time ? .85f : 1;

    /// <summary>A volley a commander calls: harder with Тактика.</summary>
    static float VolleyHarmOf(Unit cap) => cap?.Hero == null ? 1 : 1 + Heroes.VolleyHarm * cap.Hero[Skill.Tactics];

    /// <summary>A worker's hands in a hero's reach, with Зодчий.</summary>
    float Builders(Unit u) => u.Team != Team.Player ? 1 : 1 + Heroes.BuildPace * GradeAt(Skill.Builder, u.X, u.Y, u.Z);

    /// <summary>
    /// How much faster one of ours mends with Лекарь: in a hero's reach, or resting by the citadel or in it while the
    /// hero is there too.
    /// </summary>
    float Medic(Unit u, bool home)
    {
        var heroes = HeroesHere();
        if (heroes.Count == 0) return 1;
        int best = GradeAt(Skill.Healer, u.X, u.Y, u.Z, inside: u.Inside);
        if (home || u.Inside)
            foreach (var h in heroes)
            {
                float dx = h.X - Site.KeepX, dz = h.Z - Site.KeepZ;
                if (h.Inside || dx * dx + dz * dz < MendBy * MendBy) best = Math.Max(best, h.Hero[Skill.Healer]);
            }
        return 1 + Heroes.Mending * best;
    }

    // ------------------------------------------------------------ experience

    /// <summary>The experience one of the horde is worth struck down: by its health, a leader half again.</summary>
    public static int KillXp(Unit u) => (int)Math.Min(80, Math.Round((4 + u.T.Hp / 15f) * (u.Leader ? 1.5f : 1)));

    /// <summary>
    /// One fell (from Kill): one of the horde gives its worth to the heroes whose word reaches where it fell or who
    /// set the one who struck it, twice to the one who struck it himself; a warlord's fall gives more to all; a hero of
    /// ours fallen is lost.
    /// </summary>
    void HeroDeed(Unit dead, Unit by)
    {
        if (dead.T == Kinds.Captain && dead.Hero != null && dead.Team == Team.Player) { HeroFell(dead); return; }
        if (dead.Team != Team.Horde || dead.T == Kinds.Thrall || dead.Envoy || by?.Team == Team.Horde) return;
        var heroes = HeroesHere();
        if (heroes.Count == 0) return;
        int worth = KillXp(dead);
        foreach (var h in heroes.ToArray())
        {
            if (h.Dead || h.Inside) continue;
            int xp = 0;
            if (by == h) { xp = worth * 2; h.Hero.Kills++; }
            else if (by != null && by.Ward == h) xp = worth;
            else
            {
                float reach = CaptainReach * WordFar(h);
                if (Dist(h, dead) <= reach) xp = worth;
            }
            // a warlord's fall: much to those about it, something to every hero of the valley
            if (dead.Lord != null && !dead.Lord.Champion) xp += Dist(h, dead) <= 60 * VoxelWorld.PerMetre ? 120 : 40;
            if (xp <= 0) continue;
            if (_battle != null) _heroFought.Add(h.Id);
            Glory(h, xp);
        }
    }

    /// <summary>
    /// Experience for a hero of the valley: the levels it brings him to said in the chronicle, each a skill to choose.
    /// How many levels he rose.
    /// </summary>
    public int Glory(Unit h, int xp, string why = null)
    {
        if (h?.Hero == null || h.Dead) return 0;
        int rose = Heroes.Gain(h.Hero, xp);
        if (rose > 0)
        {
            Say($"Командир {h.Hero.Name} достиг уровня {h.Hero.Level}{(why != null ? $" ({why})" : "")}. Выберите ему умение.", 1, h);
            HeroRose?.Invoke(h);
        }
        return rose;
    }

    /// <summary>Experience for every hero of the valley on his feet: a valley taken, an outpost raised.</summary>
    public void GloryAll(int xp, string why)
    {
        foreach (var h in HeroesHere().ToArray()) Glory(h, xp, why);
    }

    /// <summary>A raid over: beaten off, the heroes who stood their people against it gain by its threat.</summary>
    void HeroesHeld(BattleReport r)
    {
        if (r.Held)
            foreach (var h in HeroesHere().ToArray())
                if (_heroFought.Contains(h.Id))
                {
                    int xp = 30 + (int)Math.Round(10 * r.Threat);
                    HeroHeldXp += xp;
                    Glory(h, xp, "набег отбит");
                }
        _heroFought.Clear();
    }

    /// <summary>A skill chosen for a hero of the valley at his level. False if it was not offered him.</summary>
    public bool ChooseSkill(Unit cap, Skill s)
    {
        if (cap?.Hero == null || !Heroes.Choose(cap.Hero, s)) return false;
        Say($"Командир {cap.Hero.Name}: {Heroes.NameOf(s)} {Roman(cap.Hero[s])}.", 1, cap);
        return true;
    }

    /// <summary>A hero fallen: lost with his skills; those he led strike weaker a minute.</summary>
    void HeroFell(Unit cap)
    {
        var h = cap.Hero;
        _heroesAt = double.NaN;
        string skills = Heroes.Skills(h);
        Say(skills == "нет умений" ? $"Командир {h.Name} пал." : $"Командир {h.Name} пал. С ним ушли умения: {skills}.", 3, cap);
        float reach = CaptainReach * WordFar(cap);
        int grieving = 0;
        foreach (var u in Units)
        {
            if (u.Dead || u == cap || u.Team != Team.Player || Kinds.IsEngine(u.T)) continue;
            if (u.Ward != cap && u.Follows != cap && Dist(u, cap) > reach) continue;
            u.GriefUntil = Time + 60;
            grieving++;
        }
        if (grieving > 0) Say($"Бойцы командира {h.Name} пали духом: минуту бьют слабее.", 2, cap);
    }

    // ------------------------------------------------------------ a band lying in wait seen

    /// <summary>A hero with Разведка near enough a band lying in wait to see it, or null.</summary>
    Unit Scout(Warband b)
    {
        var heroes = HeroesHere();
        if (heroes.Count == 0) return null;
        foreach (var h in heroes)
        {
            int g = h.Hero[Skill.Scouting];
            if (g == 0 || h.Inside) continue;
            float see = Heroes.ScoutMetres(g) * VoxelWorld.PerMetre;
            foreach (var u in b.Members)
                if (!u.Dead && u.Lurking && Dist(h, u) <= see) return h;
        }
        return null;
    }

    /// <summary>A band lying in wait seen by a hero: it rises then and there, with no one of ours caught off guard.</summary>
    void Spotted(Warband b, Unit hero)
    {
        foreach (var u in b.Members) u.Lurking = false;
        b.Lurk = false;
        b.RallyCell = -1; b.RallyPath = null;
        Enter(b, BandState.Storm);
        AmbushesSpotted++;
        Say($"Командир {hero.Hero.Name} заметил засаду: {b.Name}. Застать врасплох не вышло.", 1, b.Leader ?? hero);
    }
}
