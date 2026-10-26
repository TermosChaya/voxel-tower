using System;

namespace VoxelTower.Core;

/// <summary>
/// The heroes on the map (Heroes.cs): what a battle counted at once did to the heroes in it, said in the news; a valley
/// taken by a party, to its hero's credit; the cells a party sees with its hero's Разведка; how much its hero's Лекарь
/// mends it and his Зодчий hastens an outpost.
/// </summary>
public sealed partial class Campaign
{
    /// <summary>A battle counted at once over: the heroes of ours fallen in it and those risen by it, in the news.</summary>
    void HeroNews(QuickResult r, MapCell cell, MapParty p)
    {
        if (r == null) return;
        foreach (var name in r.HeroesFell) Tell(NewsKind.Lost, $"Командир {name} пал в бою {Names(cell).prep}. С ним ушли его умения.", cell, p);
        foreach (var h in r.HeroesRose) Tell(NewsKind.Fought, $"Командир {h.Name} достиг уровня {h.Level}. Выберите ему умение в его карточке.", cell, p);
    }

    /// <summary>A valley taken by a party of ours: its hero gains by it.</summary>
    void PartyGlory(MapParty p, MapCell cell)
    {
        var h = p?.Party.Commander?.Hero;
        if (h == null) return;
        if (Heroes.Gain(h, Heroes.TakeXp) > 0) Tell(NewsKind.Fought, $"Командир {h.Name} достиг уровня {h.Level}. Выберите ему умение в его карточке.", cell, p);
    }

    /// <summary>The grade of a skill of the hero at a party's head (0 without one).</summary>
    public static int GradeOf(MapParty p, Skill s) => p?.Party.Commander?.Hero?[s] ?? 0;

    /// <summary>Whether a cell is seen by a party of ours whose hero has Разведка: as many cells further than the rest.</summary>
    bool Scouted(MapCell c)
    {
        foreach (var p in Parties)
        {
            int g;
            if (p.Owner != Owner.Ours || (g = GradeOf(p, Skill.Scouting)) == 0) continue;
            if (Distance(p.Q, p.R, c.Q, c.R) <= 1 + g) return true;
        }
        return false;
    }

    /// <summary>A dawn's mending of a party of ours: in a valley of ours as the valley mends, the more for its hero's Лекарь; elsewhere only by him.</summary>
    float PartyMend(MapParty p, bool home) => home ? Mend * (1 + Heroes.Mending * GradeOf(p, Skill.Healer)) : Heroes.MarchMend * GradeOf(p, Skill.Healer);

    /// <summary>How much faster a party raises an outpost for its hero's Зодчий.</summary>
    static float OutpostHands(MapParty p) => 1 + Heroes.OutpostPace * GradeOf(p, Skill.Builder);
}
