using System;
using System.Collections.Generic;

namespace VoxelTower.Core;

/// <summary>What a quick battle came to: who won, how likely it was, how strong each side stood, who fell on each.</summary>
public sealed class QuickResult
{
    public bool AttackerWon;
    /// <summary>The attacker's chance before it was fought, 0 to 1.</summary>
    public float Odds;
    public float Attack, Defence;
    public readonly List<UnitType> AttackerFell = new(), DefenderFell = new();
    /// <summary>Of the winners who went down, so many were carried off hurt and live (the rest fell).</summary>
    public int WinnersSaved;
    public bool AttackerLordFell, DefenderLordFell;
    /// <summary>Heroes of ours who fell in it (their names), and those who rose a level by it.</summary>
    public readonly List<string> HeroesFell = new();
    public readonly List<Hero> HeroesRose = new();
}

/// <summary>
/// A battle counted at once, where we are not (docs/handoff/13-world-map.md, 2.8). Each side's strength is the root of
/// all its health times all its blows a second (Lanchester's square law: twice as many is twice as strong), the hurt
/// counting by the health they have left; bows and the casters' spells count the more, engines not at all in the field.
/// Behind works (Fortification.cs, a worth of 0 to 5) the defenders are a siege's: their health and their shooting
/// raised by the whole of it, their blows at the gap by half of it, our engines among them adding a quarter each — so a
/// few behind a good wall and towers beat several times their number; the horde's rams, the hill giant, sappers and
/// trolls batter the works down first (<see cref="Breach"/>). The mountains help the defenders, the marsh hinders the
/// attackers; a warlord fights as his level makes him and steadies his side, a commander of ours steadies ours, the
/// more for his level and his fighting skills (Heroes.cs). Twice the strength wins 9 times in 10, the same strength
/// half the time. The winners lose too, the more the closer it was (an even fight half of them, three to one a tenth),
/// and of theirs who go down a quarter live, carried off hurt (a hero's Лекарь a tenth more a grade); of the beaten most
/// fall and the rest run. A hero of ours gains by it as a raid beaten off gains him.
/// </summary>
public static class QuickBattle
{
    /// <summary>How sharply the odds turn with the strengths: twice the strength wins 9 times in 10 (2^3.17 = 9).</summary>
    public const double Sharpness = 3.17;
    /// <summary>How much a commander of ours, and each level of a warlord, steady their side.</summary>
    public const float CommanderSteady = .1f, LordSteady = .05f;

    /// <summary>A member's health and blows a second as the battle counts them; an engine's nothing.</summary>
    public static (double hp, double dps) Worth(UnitType t, float hp)
    {
        if (Kinds.IsEngine(t)) return (0, 0);
        double dps = t.AttackTime > 0 ? t.Damage / t.AttackTime : 0;
        if (t == Kinds.Shaman || t == Kinds.Witch || t == Kinds.Necromancer || t == Kinds.Alchemist || t == Kinds.OurAlchemist || t == Kinds.Bombardier) dps *= 2.5;
        else if (t.Ranged) dps *= 1.2;
        return (Math.Max(0, hp), dps);
    }

    /// <summary>A warlord's health and blows, as hard to kill and as hard hitting as his level makes him.</summary>
    static (double hp, double dps) LordWorth(Warlord l)
    {
        var (hp, dps) = Worth(l.Kind, l.Kind.Hp);
        return (hp * (2 + .5 * l.Level) * Math.Clamp(l.Health, .2, 1), dps * (1.1 + .05 * l.Level));
    }

    /// <summary>
    /// How strong a side stands: the root of all its health times all its blows, behind its works if it keeps them (the
    /// share of them its foes batter down taken off, <see cref="Breach"/>), steadied by its commander or warlord.
    /// </summary>
    public static float Strength(IReadOnlyList<Traveller> side, Warlord lord, float walls = 0, float breach = 0)
    {
        double hp = 0, dps = 0;
        bool commander = false, archers = false;
        float hero = 0;
        int engines = 0;
        foreach (var m in side) { archers |= m.Kind.Ranged && m.Kind != Kinds.Captain; if (Kinds.IsOurEngine(m.Kind)) engines++; }
        // works only behind something: a field battle's engines of ours stand in the open
        float wall = walls > 0 ? Math.Max(0, walls * (1 - Math.Clamp(breach, 0, .9f))) + Math.Min(1, EngineWorks * engines) : 0;
        foreach (var m in side)
        {
            var (h, d) = Worth(m.Kind, m.Hp);
            hp += h * (1 + wall);
            dps += d * (m.Kind.Ranged ? 1 + wall : 1 + wall / 2);
            if (m.Kind == Kinds.Captain && m.Commands) { commander = true; hero = Math.Max(hero, Heroes.Steady(m.Hero, archers)); }
        }
        if (lord != null) { var (h, d) = LordWorth(lord); hp += h; dps += d; }
        double s = Math.Sqrt(hp * dps);
        if (commander) s *= 1 + CommanderSteady + hero;
        if (lord != null) s *= 1 + LordSteady * lord.Level;
        return (float)s;
    }

    /// <summary>What each engine of ours among the keepers of works adds to them (a quarter, a whole at most).</summary>
    public const float EngineWorks = .25f;

    /// <summary>
    /// The share of the works a side comes against that it batters down before the fight: each ram or engine of the
    /// horde's 15 %, a hill giant 20 %, a sapper 8 %, a troll 4 %; nine tenths at most.
    /// </summary>
    public static float Breach(IReadOnlyList<Traveller> side)
    {
        float b = 0;
        foreach (var m in side)
        {
            var t = m.Kind;
            if (Kinds.IsEngine(t) && !Kinds.IsOurEngine(t)) b += .15f;
            else if (t == Kinds.HillGiant) b += .2f;
            else if (t == Kinds.Sapper) b += .08f;
            else if (t == Kinds.Troll) b += .04f;
        }
        return Math.Min(.9f, b);
    }

    /// <summary>The attacker's chance against a defender of these strengths.</summary>
    public static float Odds(float attack, float defence) =>
        attack <= 0 ? 0 : defence <= 0 ? 1 : (float)(1 / (1 + Math.Pow(defence / attack, Sharpness)));

    /// <summary>
    /// The attacker's chance against the defenders of a valley of some ground behind walls, before it is fought (for
    /// the horde's choices and the player's offer).
    /// </summary>
    public static float Chance(IReadOnlyList<Traveller> att, Warlord attLord, IReadOnlyList<Traveller> def, Warlord defLord, float walls, ValleyKind ground)
    {
        var (a, d) = Sides(att, attLord, def, defLord, walls, ground);
        return Odds(a, d);
    }

    static (float a, float d) Sides(IReadOnlyList<Traveller> att, Warlord attLord, IReadOnlyList<Traveller> def, Warlord defLord, float walls, ValleyKind ground)
    {
        float a = Strength(att, attLord), d = Strength(def, defLord, walls, Breach(att));
        if (ground == ValleyKind.Mountain) d *= 1.15f;
        if (ground == ValleyKind.Marsh) a *= .9f;
        return (a, d);
    }

    /// <summary>
    /// The battle fought: the attackers on the defenders of a valley of that ground behind those walls. Those who fall
    /// are taken out of the lists, the living hurt as the battle went; a warlord of the beaten side falls one time in
    /// three, else gets away hurt; the winners' gains toward his next level. Engines of the beaten side are lost.
    /// </summary>
    public static QuickResult Fight(List<Traveller> att, Warlord attLord, List<Traveller> def, Warlord defLord, float walls, ValleyKind ground, Func<double> roll)
    {
        var res = new QuickResult();
        var (a, d) = Sides(att, attLord, def, defLord, walls, ground);
        res.Attack = a; res.Defence = d;
        res.Odds = Odds(a, d);
        res.AttackerWon = roll() < res.Odds;
        float win = res.AttackerWon ? a : d, lose = res.AttackerWon ? d : a;
        // the winners lose the more the closer it was: an even fight half of them, three to one a tenth (an upset dear)
        double r = Math.Min(1, win > 0 ? lose / win : 1), lost = WinnersLose(r), keep = 1 - lost;
        var (winners, losers) = res.AttackerWon ? (att, def) : (def, att);
        var (winFell, loseFell) = res.AttackerWon ? (res.AttackerFell, res.DefenderFell) : (res.DefenderFell, res.AttackerFell);
        res.WinnersSaved = Fall(winners, lost, roll, winFell, false, res.HeroesFell, Saved(winners));
        Hurt(winners, keep, roll);
        // of the beaten most fall, the rest run; defenders shut in behind their walls the more
        double killed = .55 + .35 * roll() + (!res.AttackerWon ? 0 : walls > 0 ? .15 : 0);
        Fall(losers, Math.Min(1, killed), roll, loseFell, true, res.HeroesFell);
        Hurt(losers, .3, roll);
        // the heroes who came through it: the winners' by what the beaten lost, the beaten's a little
        Glory(winners, Heroes.WonXp + Heroes.FoeXp * loseFell.Count, res.HeroesRose);
        Glory(losers, 2 * winFell.Count, res.HeroesRose);
        var (winLord, loseLord) = res.AttackerWon ? (attLord, defLord) : (defLord, attLord);
        if (loseLord != null)
        {
            bool fell = roll() < .35;
            if (fell) loseLord.Fate = LordFate.Fell;
            else { loseLord.Health = (float)Math.Max(.2, loseLord.Health * .5); loseLord.Fate = LordFate.Fled; }
            if (res.AttackerWon) res.DefenderLordFell = fell; else res.AttackerLordFell = fell;
        }
        if (winLord != null)
        {
            winLord.Health = (float)Math.Clamp(winLord.Health * (.5 + .5 * keep), .2, 1);
            winLord.Xp += 10 * loseFell.Count;
            while (winLord.Level < Warlord.MaxLevel && winLord.Xp >= Warlord.Next(winLord.Level)) { winLord.Xp -= Warlord.Next(winLord.Level); winLord.Level++; }
        }
        return res;
    }

    /// <summary>The share of the winners who go down, by how near the beaten came to them (their strength over the winners', 0 to 1).</summary>
    public static double WinnersLose(double r) => .05 + .5 * Math.Pow(Math.Clamp(r, 0, 1), 1.5);

    /// <summary>The share of a winning side's men who go down that live, carried off hurt: a quarter, its best hero's Лекарь a tenth more a grade.</summary>
    static double Saved(List<Traveller> side)
    {
        int healer = 0;
        foreach (var m in side) if (m.Hero != null) healer = Math.Max(healer, m.Hero[Skill.Healer]);
        return .25 + .1 * healer;
    }

    /// <summary>
    /// So many of a side go down, the hurt and the weak first, with luck in it; of them, so many (<paramref name="saved"/>)
    /// live, carried off hurt to a tenth or a quarter of their health, and the rest fall; the beaten lose their engines.
    /// Returns how many lived.
    /// </summary>
    static int Fall(List<Traveller> side, double share, Func<double> roll, List<UnitType> fell, bool beaten, List<string> heroes, double saved = 0)
    {
        if (beaten)
            for (int i = side.Count - 1; i >= 0; i--)
                if (Kinds.IsEngine(side[i].Kind)) { fell.Add(side[i].Kind); side.RemoveAt(i); }
        int men = side.Count, n = (int)Math.Floor(men * share + roll());
        if (n <= 0 || men == 0) return 0;
        var order = new List<(double key, Traveller m)>();
        foreach (var m in side) order.Add((m.Hp / Math.Max(1, m.MaxHp) + roll() * .6 + (m.Kind == Kinds.Captain ? .5 : 0), m));
        order.Sort((x, y) => x.key.CompareTo(y.key));
        int lived = 0;
        for (int i = 0; i < Math.Min(n, order.Count); i++)
        {
            var m = order[i].m;
            if (!Kinds.IsEngine(m.Kind) && roll() < saved) { m.Hp = (float)Math.Max(1, m.MaxHp * (.1 + .15 * roll())); lived++; continue; }
            fell.Add(m.Kind); side.Remove(m);
            if (m.Hero is { } h) heroes.Add(h.Name);
        }
        return lived;
    }

    /// <summary>Experience for the heroes of a side come through a battle; those who rose a level by it.</summary>
    static void Glory(List<Traveller> side, int xp, List<Hero> rose)
    {
        foreach (var m in side) if (m.Hero != null && Heroes.Gain(m.Hero, xp) > 0) rose.Add(m.Hero);
    }

    /// <summary>The living of a side hurt: each keeping between so much of his health and all of it, by luck.</summary>
    static void Hurt(List<Traveller> side, double keep, Func<double> roll)
    {
        foreach (var m in side)
            if (!Kinds.IsEngine(m.Kind) && m.Hp > m.MaxHp * .3f) m.Hp = (float)Math.Max(1, m.Hp * (keep + (1 - keep) * roll()));
    }
}
