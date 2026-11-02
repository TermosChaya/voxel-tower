using System;
using Godot;
using VoxelTower.Core;

namespace VoxelTower.Game;

/// <summary>The kinds of ours the household's list can be narrowed to (the faces along the top, the kinds on the console).</summary>
public enum Roster { All, Workers, Melee, Shooters, Riders, Engines, Leaders, Faith, Hurt, Idle }

/// <summary>
/// The narrowing of the household's list: who of ours is in which part of it, and each part's name, word and mark. A part
/// is a kind of work (the workers, the men of the line, those who shoot, the riders, our engines and their crews, those
/// who lead, the faith's people) or a state (the hurt, the idle).
/// </summary>
public static class Rosters
{
    /// <summary>The parts in the order the list shows their marks.</summary>
    public static readonly Roster[] Order = { Roster.All, Roster.Workers, Roster.Melee, Roster.Shooters, Roster.Riders, Roster.Engines, Roster.Leaders, Roster.Faith, Roster.Hurt, Roster.Idle };

    /// <summary>Whether a kind of ours is in a part of the list by its work (the states are not kinds).</summary>
    public static bool Holds(Roster r, UnitType t) => r switch
    {
        Roster.All => true,
        Roster.Workers => t == Kinds.Worker,
        Roster.Melee => !t.Ranged && !t.Mounted && !t.Prays && t.Work < 1 && !Kinds.IsEngine(t) && t != Kinds.Captain && t != Kinds.Architect && t != Kinds.Crewman,
        Roster.Shooters => t.Ranged && !t.Mounted && !Kinds.IsEngine(t),
        Roster.Riders => t.Mounted,
        Roster.Engines => Kinds.IsOurEngine(t) || t == Kinds.Crewman,
        Roster.Leaders => t == Kinds.Captain || t == Kinds.Architect,
        Roster.Faith => t.Prays,
        _ => false,
    };

    /// <summary>Whether one of ours is in a part of the list now.</summary>
    public static bool Holds(Roster r, Unit u, Sim sim) => r switch
    {
        Roster.Hurt => u.Mending || u.Hp < u.MaxHp * .7f || u.Wounds is { } w && (w.Bleed > .15f || w.Broken != Break.None),
        Roster.Idle => u.State == UnitState.Idle && !u.Hold && !u.Inside && u.Pastime != Pastime.Pray,
        _ => Holds(r, u.T),
    };

    /// <summary>A part's name, for its tooltip: «Стрелки».</summary>
    public static string Name(Roster r) => r switch
    {
        Roster.All => "Все", Roster.Workers => "Рабочие", Roster.Melee => "Ближний бой", Roster.Shooters => "Стрелки", Roster.Riders => "Всадники",
        Roster.Engines => "Машины и расчёты", Roster.Leaders => "Управляющие", Roster.Faith => "Вера", Roster.Hurt => "Раненые", _ => "Без дела",
    };

    /// <summary>Who is in it, for its tooltip.</summary>
    public static string About(Roster r) => r switch
    {
        Roster.All => "весь список",
        Roster.Workers => "рабочие",
        Roster.Melee => "мечники, копейщики, щитоносцы, алебардщики, рыцари, псы, тролли и великаны",
        Roster.Shooters => "лучники, пращники, арбалетчики, бомбардиры и алхимики",
        Roster.Riders => "всадники, конные лучники и конные рыцари",
        Roster.Engines => "катапульты, баллисты, требушеты и механики",
        Roster.Leaders => "командиры и архитектор",
        Roster.Faith => "жрецы",
        Roster.Hurt => "раненые, истекающие кровью и со сломанными костями",
        _ => "те, кому нечего делать",
    };

    /// <summary>Its mark: an icon of the set, or (for the hurt and the idle) a drop of blood and three dots, as on the faces.</summary>
    public static void Paint(CanvasItem c, Rect2 r, Roster g, Color ink)
    {
        var mid = r.GetCenter();
        float s = Math.Min(r.Size.X, r.Size.Y);
        switch (g)
        {
            case Roster.Hurt:
                var col = new Color("c63c2c");
                c.DrawColoredPolygon(new[] { mid + new Vector2(0, -s * .38f), mid + new Vector2(s * .22f, s * .02f), mid + new Vector2(-s * .22f, s * .02f) }, col);
                c.DrawCircle(mid + new Vector2(0, s * .1f), s * .24f, col, true, -1, true);
                c.DrawCircle(mid + new Vector2(-s * .08f, s * .04f), s * .07f, new Color(1, 1, 1, .45f), true, -1, true);
                break;
            case Roster.Idle:
                for (int k = -1; k <= 1; k++) c.DrawCircle(mid + new Vector2(k * s * .22f, s * .05f), s * .08f, ink, true, -1, true);
                break;
            default:
                Icons.Glyph(c, r, ink, g switch
                {
                    Roster.All => "rally-the-troops", Roster.Workers => "mining", Roster.Melee => "swordman", Roster.Shooters => "bowman",
                    Roster.Riders => "horse-head", Roster.Engines => "catapult", Roster.Leaders => "knight-banner", _ => "prayer",
                });
                break;
        }
    }
}
