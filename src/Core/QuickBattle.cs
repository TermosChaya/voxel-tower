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
    public bool AttackerLordFell, DefenderLordFell;
}

/// <summary>
/// A battle counted at once, where we are not (docs/handoff/13-world-map.md, 2.8). Each side's strength is the root of
/// all its health times all its blows a second (Lanchester's square law: twice as many is twice as strong), the hurt
/// counting by the health they have left; bows and the casters' spells count the more, engines not at all in the field
/// — the horde's batter the defenders' walls down instead. The defenders' walls add to their health and to their
/// shooters, the mountains to the defenders, the marsh takes from the attackers; a warlord fights as his level makes
/// him and steadies his side, a commander of ours steadies ours. Twice the strength wins 9 times in 10, the same
/// strength half the time. The winners lose the fewer the stronger they were; of the beaten most fall and the rest run.
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
    /// How strong a side stands: the root of all its health times all its blows, with its walls if it keeps them (the
    /// horde's engines batter them down), steadied by its commander or warlord.
    /// </summary>
    public static float Strength(IReadOnlyList<Traveller> side, Warlord lord, float walls = 0, int rams = 0)
    {
        double hp = 0, dps = 0;
        bool commander = false;
        float wall = Math.Max(0, walls * (1 - .15f * rams));
        foreach (var m in side)
        {
            var (h, d) = Worth(m.Kind, m.Hp);
            hp += h * (1 + wall);
            dps += m.Kind.Ranged ? d * (1 + wall) : d;
            if (m.Kind == Kinds.Captain && m.Commands) commander = true;
            if (Kinds.IsOurEngine(m.Kind)) wall += .1f;
        }
        if (lord != null) { var (h, d) = LordWorth(lord); hp += h; dps += d; }
        double s = Math.Sqrt(hp * dps);
        if (commander) s *= 1 + CommanderSteady;
        if (lord != null) s *= 1 + LordSteady * lord.Level;
        return (float)s;
    }

    /// <summary>The horde's engines among a side: each batters a share of the walls down.</summary>
    static int Rams(IReadOnlyList<Traveller> side)
    {
        int n = 0;
        foreach (var m in side) if (Kinds.IsEngine(m.Kind) && !Kinds.IsOurEngine(m.Kind)) n++;
        return n;
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
        float a = Strength(att, attLord), d = Strength(def, defLord, walls, Rams(att));
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
        // the winners keep the more of themselves the stronger they were (the square law), an upset costing dear
        double r = Math.Min(.95, win > 0 ? lose / win : .95), keep = Math.Sqrt(1 - r * r);
        var (winners, losers) = res.AttackerWon ? (att, def) : (def, att);
        var (winFell, loseFell) = res.AttackerWon ? (res.AttackerFell, res.DefenderFell) : (res.DefenderFell, res.AttackerFell);
        Fall(winners, 1 - keep, roll, winFell, false);
        Hurt(winners, keep, roll);
        // of the beaten most fall, the rest run; defenders shut in behind their walls the more
        double killed = .55 + .35 * roll() + (!res.AttackerWon ? 0 : walls > 0 ? .15 : 0);
        Fall(losers, Math.Min(1, killed), roll, loseFell, true);
        Hurt(losers, .3, roll);
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

    /// <summary>So many of a side fall, the hurt and the weak first, with luck in it; the beaten lose their engines.</summary>
    static void Fall(List<Traveller> side, double share, Func<double> roll, List<UnitType> fell, bool beaten)
    {
        if (beaten)
            for (int i = side.Count - 1; i >= 0; i--)
                if (Kinds.IsEngine(side[i].Kind)) { fell.Add(side[i].Kind); side.RemoveAt(i); }
        int men = side.Count, n = (int)Math.Floor(men * share + roll());
        if (n <= 0 || men == 0) return;
        var order = new List<(double key, Traveller m)>();
        foreach (var m in side) order.Add((m.Hp / Math.Max(1, m.MaxHp) + roll() * .6 + (m.Kind == Kinds.Captain ? .5 : 0), m));
        order.Sort((x, y) => x.key.CompareTo(y.key));
        for (int i = 0; i < Math.Min(n, order.Count); i++) { fell.Add(order[i].m.Kind); side.Remove(order[i].m); }
    }

    /// <summary>The living of a side hurt: each keeping between so much of his health and all of it, by luck.</summary>
    static void Hurt(List<Traveller> side, double keep, Func<double> roll)
    {
        foreach (var m in side)
            if (!Kinds.IsEngine(m.Kind)) m.Hp = (float)Math.Max(1, m.Hp * (keep + (1 - keep) * roll()));
    }
}
